#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Specification 275 section 10.1: the local-file adapter - immutable entry bytes and bounded
' lazy asset snapshots under one canonical root. The root is canonicalized once at Open; every
' later read authorizes on the opened handle (HtmlFileAccess), never on a path string.
Friend NotInheritable Class LocalHtmlDocumentSource
    Implements IHtmlDocumentSource

    ' Section 14: the configured snapshot cache never exceeds 256 MiB.
    Private Const MaxCacheBytes As Long = 256L * 1024L * 1024L

    Private NotInheritable Class Snapshot
        Public ReadOnly Kind As HtmlResourceKind
        Public ReadOnly MimeType As String
        Public ReadOnly Bytes As Byte()
        Public ReadOnly Version As HtmlSourceVersion

        Public Sub New(kind As HtmlResourceKind, mimeType As String, bytes As Byte(), version As HtmlSourceVersion)
            Me.Kind = kind
            Me.MimeType = mimeType
            Me.Bytes = bytes
            Me.Version = version
        End Sub
    End Class

    Private ReadOnly _request As HtmlOpenRequest
    Private ReadOnly _root As String
    Private ReadOnly _cacheLimit As Long
    Private ReadOnly _gate As New SemaphoreSlim(HtmlDocumentPolicy.MaxOutstandingReads, HtmlDocumentPolicy.MaxOutstandingReads)
    ' Neither a timer nor a wait handle is ever created from it, so there is nothing to release.
    Private ReadOnly _cancel As New CancellationTokenSource()
    Private ReadOnly _sync As New Object()
    Private ReadOnly _keys As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lru As New LinkedList(Of KeyValuePair(Of String, Snapshot))()
    Private ReadOnly _index As New Dictionary(Of String, LinkedListNode(Of KeyValuePair(Of String, Snapshot)))(StringComparer.OrdinalIgnoreCase)
    Private _cacheBytes As Long
    Private _generation As Integer
    Private _entryVersion As HtmlSourceVersion
    Private _disposed As Boolean

    Private Sub New(request As HtmlOpenRequest, root As String, cacheLimit As Long)
        _request = request
        _root = root
        _cacheLimit = cacheLimit
    End Sub

    ' Secure containers are refused before any filesystem work. A null/empty root means the
    ' entry's own directory; an explicit root must contain the entry.
    Friend Shared Function Open(entryPath As String, rootDirectory As String, isSecure As Boolean,
                                snapshotCacheBytes As Long, ByRef source As LocalHtmlDocumentSource) As HtmlOutcome
        source = Nothing
        If isSecure Then Return HtmlOutcome.SecureRefused
        If String.IsNullOrWhiteSpace(entryPath) Then Return HtmlOutcome.SourceMissing

        Dim rootInput As String = rootDirectory
        If String.IsNullOrWhiteSpace(rootInput) Then
            Try
                rootInput = Path.GetDirectoryName(Path.GetFullPath(entryPath))
            Catch ex As ArgumentException
                Return HtmlOutcome.SourceMissing
            Catch ex As NotSupportedException
                Return HtmlOutcome.SourceMissing
            Catch ex As PathTooLongException
                Return HtmlOutcome.UnsupportedResource
            End Try
            If String.IsNullOrEmpty(rootInput) Then Return HtmlOutcome.SourceMissing
        End If

        Dim canonicalRoot As String = ""
        Dim status As HtmlOutcome = HtmlFileAccess.CanonicalRoot(rootInput, canonicalRoot)
        If status <> HtmlOutcome.Ok Then Return status

        Dim canonicalEntry As String = ""
        status = HtmlFileAccess.CanonicalFile(entryPath, canonicalEntry)
        If status <> HtmlOutcome.Ok Then Return status
        If Not HtmlFileAccess.IsUnderRoot(canonicalRoot, canonicalEntry) Then Return HtmlOutcome.RootEscape

        Dim key As String = canonicalEntry.Substring(canonicalRoot.Length).TrimStart("\"c).Replace("\"c, "/"c)
        Dim keyCheck As HtmlKeyResolution = HtmlDocumentPolicy.ValidateKey(key)
        If Not keyCheck.IsAllowed Then Return keyCheck.Status

        Dim request As New HtmlOpenRequest With {
            .SourceId = Guid.NewGuid().ToString("N"),
            .SourceKind = HtmlSourceKind.LocalFile,
            .DisplayName = key.Substring(key.LastIndexOf("/"c) + 1),
            .OriginalLocator = entryPath,
            .EntryResourceKey = key,
            .RootDescriptor = canonicalRoot,
            .IsSecure = False,
            .IsReadOnly = True}
        source = New LocalHtmlDocumentSource(request, canonicalRoot, Math.Max(0L, Math.Min(snapshotCacheBytes, MaxCacheBytes)))
        Return HtmlOutcome.Ok
    End Function

    Public ReadOnly Property Request As HtmlOpenRequest Implements IHtmlDocumentSource.Request
        Get
            Return _request
        End Get
    End Property

    Friend ReadOnly Property RootPath As String
        Get
            Return _root
        End Get
    End Property

    ' Per-resource response deadline (queue wait included); a test seam, the product keeps the default.
    Friend Property DeadlineMs As Integer = HtmlDocumentPolicy.LocalResourceDeadlineMs

    ' The joined full path of an allowed key for the typed opener / browser launcher; "" when refused.
    Friend Function LocalPathOf(key As String) As String
        Return HtmlFileAccess.LocalPathOf(_root, key)
    End Function

    ' The entry bytes. Each call re-reads (Reload), refreshes the session version and drops the
    ' asset snapshots of the previous read.
    Public Function ReadEntryAsync(token As CancellationToken) As Task(Of HtmlResourceResult) Implements IHtmlDocumentSource.ReadEntryAsync
        If IsDisposed Then Return Task.FromResult(HtmlResourceResult.Refused(HtmlOutcome.Cancelled))
        Return Task.Run(Function() ReadEntryCore(token))
    End Function

    Private Function ReadEntryCore(token As CancellationToken) As HtmlResourceResult
        Dim key As String = _request.EntryResourceKey
        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel.Token)
            Dim read As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(_root, key, HtmlDocumentPolicy.MaxEntryBytes, linked.Token)
            If read.Status <> HtmlOutcome.Ok Then Return HtmlResourceResult.Refused(read.Status)
            SyncLock _sync
                If _disposed Then Return HtmlResourceResult.Refused(HtmlOutcome.Cancelled)
                _entryVersion = read.Version
                _request.SourceVersion = read.Version
                _generation += 1
                _keys.Clear()
                ClearCacheLocked()
            End SyncLock
            Return New HtmlResourceResult With {
                .Status = HtmlOutcome.Ok,
                .Kind = HtmlResourceKind.Document,
                .MimeType = HtmlDocumentPolicy.MimeTypeOf(key),
                .Bytes = read.Bytes,
                .ResourceVersion = read.Version}
        End Using
    End Function

    ' Tracks the distinct allowed keys of the session (case-insensitive: the filesystem is) and
    ' refuses the 2049th. A key that did not come from the request parser is re-validated.
    Public Function ResolveKey(key As String) As HtmlKeyResolution Implements IHtmlDocumentSource.ResolveKey
        Dim verdict As HtmlKeyResolution = HtmlDocumentPolicy.ValidateKey(key)
        If Not verdict.IsAllowed Then Return verdict
        SyncLock _sync
            If _disposed Then Return HtmlKeyResolution.Refused(HtmlOutcome.Cancelled)
            If Not _keys.Contains(key) Then
                If _keys.Count >= HtmlDocumentPolicy.MaxUniqueResources Then Return HtmlKeyResolution.Refused(HtmlOutcome.LimitExceeded)
                _keys.Add(key)
            End If
        End SyncLock
        Return verdict
    End Function

    Public Function KindOf(key As String) As HtmlResourceKind Implements IHtmlDocumentSource.KindOf
        Return HtmlDocumentPolicy.KindOfExtension(key)
    End Function

    Public Async Function ReadResourceAsync(key As String, token As CancellationToken) As Task(Of HtmlResourceResult) Implements IHtmlDocumentSource.ReadResourceAsync
        If IsDisposed Then Return HtmlResourceResult.Refused(HtmlOutcome.Cancelled)
        Dim resolution As HtmlKeyResolution = ResolveKey(key)
        If Not resolution.IsAllowed Then Return HtmlResourceResult.Refused(resolution.Status)
        Dim kind As HtmlResourceKind = HtmlDocumentPolicy.KindOfExtension(key)
        If kind = HtmlResourceKind.Other Then Return HtmlResourceResult.Refused(HtmlOutcome.UnsupportedResource)
        Dim limit As Long = HtmlDocumentPolicy.MaxBytesFor(kind)

        ' The entry is never served from the snapshot cache: asking for it again is how a
        ' replaced original is noticed.
        Dim isEntry As Boolean = String.Equals(key, _request.EntryResourceKey, StringComparison.OrdinalIgnoreCase)
        Dim generation As Integer
        SyncLock _sync
            generation = _generation
        End SyncLock
        If Not isEntry Then
            Dim cached As HtmlResourceResult = TryGetCached(key)
            If cached IsNot Nothing Then Return cached
        End If

        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel.Token)
            Dim linkedToken As CancellationToken = linked.Token
            linked.CancelAfter(DeadlineMs)
            Try
                Await _gate.WaitAsync(linkedToken).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return HtmlResourceResult.Refused(CancelReason(token))
            End Try

            ' The slot is released by the worker itself, so a read that outlives its deadline keeps
            ' counting as outstanding until it really ends; the waiter is released at the deadline.
            Dim work As Task(Of HtmlFileRead) = Task.Run(Function() ReadAndRelease(key, limit, linkedToken))
            Dim read As HtmlFileRead
            Try
                read = Await work.WaitAsync(linkedToken).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return HtmlResourceResult.Refused(CancelReason(token))
            End Try

            If read.Status = HtmlOutcome.Cancelled Then Return HtmlResourceResult.Refused(CancelReason(token))
            If read.Status <> HtmlOutcome.Ok Then Return HtmlResourceResult.Refused(read.Status)
            If IsDisposed Then Return HtmlResourceResult.Refused(HtmlOutcome.Cancelled)

            If isEntry Then
                Dim expected As HtmlSourceVersion
                SyncLock _sync
                    expected = _entryVersion
                End SyncLock
                If expected IsNot Nothing AndAlso Not String.Equals(read.Version.Digest, expected.Digest, StringComparison.Ordinal) Then
                    Return HtmlResourceResult.Refused(HtmlOutcome.SourceChanged)
                End If
            End If

            Dim result As New HtmlResourceResult With {
                .Status = HtmlOutcome.Ok,
                .Kind = kind,
                .MimeType = HtmlDocumentPolicy.MimeTypeOf(key),
                .Bytes = read.Bytes,
                .ResourceVersion = read.Version}
            If Not isEntry Then Store(key, result, generation)
            Return result
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _sync
            If _disposed Then Return
            _disposed = True
            _keys.Clear()
            ClearCacheLocked()
        End SyncLock
        Try
            _cancel.Cancel()
        Catch ex As AggregateException
            ' A cancellation callback failed; the source is released either way.
        End Try
    End Sub

    ' ---- internals ------------------------------------------------------------------------

    Private ReadOnly Property IsDisposed As Boolean
        Get
            SyncLock _sync
                Return _disposed
            End SyncLock
        End Get
    End Property

    Private Function CancelReason(callerToken As CancellationToken) As HtmlOutcome
        Return If(callerToken.IsCancellationRequested OrElse _cancel.IsCancellationRequested, HtmlOutcome.Cancelled, HtmlOutcome.TimedOut)
    End Function

    Private Function ReadAndRelease(key As String, limit As Long, token As CancellationToken) As HtmlFileRead
        Try
            Return HtmlFileAccess.ReadUnderRoot(_root, key, limit, token)
        Catch ex As Exception
            Return HtmlFileRead.Failed(HtmlOutcome.AccessDenied)
        Finally
            _gate.Release()
        End Try
    End Function

    Private Function TryGetCached(key As String) As HtmlResourceResult
        SyncLock _sync
            Dim node As LinkedListNode(Of KeyValuePair(Of String, Snapshot)) = Nothing
            If _disposed OrElse Not _index.TryGetValue(key, node) Then Return Nothing
            _lru.Remove(node)
            _lru.AddFirst(node)
            Dim snapshot As Snapshot = node.Value.Value
            Return New HtmlResourceResult With {
                .Status = HtmlOutcome.Ok,
                .Kind = snapshot.Kind,
                .MimeType = snapshot.MimeType,
                .Bytes = snapshot.Bytes,
                .ResourceVersion = snapshot.Version}
        End SyncLock
    End Function

    ' A resource bigger than the whole cache is served but never cached; a stale generation
    ' (the entry was re-read while this one was in flight) is not stored either.
    Private Sub Store(key As String, result As HtmlResourceResult, generation As Integer)
        Dim size As Long = result.Bytes.LongLength
        If _cacheLimit <= 0L OrElse size > _cacheLimit Then Return
        SyncLock _sync
            If _disposed OrElse generation <> _generation Then Return
            Dim existing As LinkedListNode(Of KeyValuePair(Of String, Snapshot)) = Nothing
            If _index.TryGetValue(key, existing) Then RemoveNodeLocked(existing)
            While _cacheBytes + size > _cacheLimit AndAlso _lru.Last IsNot Nothing
                RemoveNodeLocked(_lru.Last)
            End While
            Dim node As LinkedListNode(Of KeyValuePair(Of String, Snapshot)) =
                _lru.AddFirst(New KeyValuePair(Of String, Snapshot)(key, New Snapshot(result.Kind, result.MimeType, result.Bytes, result.ResourceVersion)))
            _index(key) = node
            _cacheBytes += size
        End SyncLock
    End Sub

    Private Sub RemoveNodeLocked(node As LinkedListNode(Of KeyValuePair(Of String, Snapshot)))
        _lru.Remove(node)
        _index.Remove(node.Value.Key)
        _cacheBytes -= node.Value.Value.Bytes.LongLength
    End Sub

    Private Sub ClearCacheLocked()
        _lru.Clear()
        _index.Clear()
        _cacheBytes = 0L
    End Sub
End Class
#End If
