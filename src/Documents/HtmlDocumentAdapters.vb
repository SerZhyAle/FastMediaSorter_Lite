#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Threading
Imports System.Threading.Tasks

' Specification 275 section 10.1 / W08: EPUB-package, single-entry snapshot and verified-converter
' adapters over the same IHtmlDocumentSource seam. They supply bytes and provenance only: no adapter
' can change host flags, the content policy or the request filter.

''' <summary>One logical resource a package declares: what it is (from the package's own metadata,
''' never from a response header) and the media type it is served with.</summary>
Friend NotInheritable Class HtmlPackageEntry
    Public ReadOnly Kind As HtmlResourceKind
    Public ReadOnly MimeType As String

    Public Sub New(kind As HtmlResourceKind, mimeType As String)
        Me.Kind = kind
        Me.MimeType = If(String.IsNullOrWhiteSpace(mimeType), "application/octet-stream", mimeType)
    End Sub
End Class

''' <summary>A read-only package (an EPUB, a materialized archive or provider entry) as a closed set of
''' known keys. A key the package did not declare is refused; nothing is listed from a directory.</summary>
Friend NotInheritable Class PackageHtmlDocumentSource
    Implements IHtmlDocumentSource

    Private ReadOnly _request As HtmlOpenRequest
    Private ReadOnly _entries As Dictionary(Of String, HtmlPackageEntry)
    Private ReadOnly _read As Func(Of String, CancellationToken, Byte())
    Private ReadOnly _lease As IDisposable
    Private ReadOnly _gate As New SemaphoreSlim(HtmlDocumentPolicy.MaxOutstandingReads, HtmlDocumentPolicy.MaxOutstandingReads)
    Private ReadOnly _cancel As New CancellationTokenSource()
    Private _disposed As Integer

    ''' <param name="read">Reads one declared key's bytes; may throw, is called off the UI thread.</param>
    ''' <param name="lease">Released after every read has stopped (the package snapshot, a result lease).</param>
    Friend Sub New(request As HtmlOpenRequest, entries As IDictionary(Of String, HtmlPackageEntry),
                   read As Func(Of String, CancellationToken, Byte()), Optional lease As IDisposable = Nothing)
        If request Is Nothing Then Throw New ArgumentNullException(NameOf(request))
        If entries Is Nothing Then Throw New ArgumentNullException(NameOf(entries))
        If read Is Nothing Then Throw New ArgumentNullException(NameOf(read))
        ' Package keys are case-sensitive (ZIP names, manifest hrefs).
        _entries = New Dictionary(Of String, HtmlPackageEntry)(StringComparer.Ordinal)
        For Each pair In entries
            If HtmlDocumentPolicy.ValidateKey(pair.Key).IsAllowed AndAlso pair.Value IsNot Nothing AndAlso _entries.Count < HtmlDocumentPolicy.MaxUniqueResources Then
                _entries(pair.Key) = pair.Value
            End If
        Next
        Dim entry As HtmlPackageEntry = Nothing
        If Not _entries.TryGetValue(request.EntryResourceKey, entry) OrElse entry.Kind <> HtmlResourceKind.Document Then
            Throw New ArgumentException("The entry key is not a declared document of the package.", NameOf(request))
        End If
        request.IsReadOnly = True
        _request = request
        _read = read
        _lease = lease
    End Sub

    Public ReadOnly Property Request As HtmlOpenRequest Implements IHtmlDocumentSource.Request
        Get
            Return _request
        End Get
    End Property

    Private ReadOnly Property IsDisposed As Boolean
        Get
            Return Volatile.Read(_disposed) <> 0
        End Get
    End Property

    Public Async Function ReadEntryAsync(token As CancellationToken) As Task(Of HtmlResourceResult) Implements IHtmlDocumentSource.ReadEntryAsync
        Dim result = Await ReadCoreAsync(_request.EntryResourceKey, token).ConfigureAwait(False)
        If result.IsOk Then _request.SourceVersion = result.ResourceVersion
        Return result
    End Function

    Public Function ResolveKey(key As String) As HtmlKeyResolution Implements IHtmlDocumentSource.ResolveKey
        If IsDisposed Then Return HtmlKeyResolution.Refused(HtmlOutcome.Cancelled)
        Dim verdict = HtmlDocumentPolicy.ValidateKey(key)
        If Not verdict.IsAllowed Then Return verdict
        If Not _entries.ContainsKey(key) Then Return HtmlKeyResolution.Refused(HtmlOutcome.SourceMissing)
        Return verdict
    End Function

    Public Function KindOf(key As String) As HtmlResourceKind Implements IHtmlDocumentSource.KindOf
        Dim entry As HtmlPackageEntry = Nothing
        If key IsNot Nothing AndAlso _entries.TryGetValue(key, entry) Then Return entry.Kind
        Return HtmlResourceKind.Other
    End Function

    Public Function ReadResourceAsync(key As String, token As CancellationToken) As Task(Of HtmlResourceResult) Implements IHtmlDocumentSource.ReadResourceAsync
        Return ReadCoreAsync(key, token)
    End Function

    Private Async Function ReadCoreAsync(key As String, token As CancellationToken) As Task(Of HtmlResourceResult)
        Dim resolution = ResolveKey(key)
        If Not resolution.IsAllowed Then Return HtmlResourceResult.Refused(resolution.Status)
        Dim entry = _entries(key)
        If entry.Kind = HtmlResourceKind.Other Then Return HtmlResourceResult.Refused(HtmlOutcome.UnsupportedResource)
        Dim limit = If(entry.Kind = HtmlResourceKind.Document, HtmlDocumentPolicy.MaxEntryBytes, HtmlDocumentPolicy.MaxBytesFor(entry.Kind))
        Using linked = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel.Token)
            linked.CancelAfter(HtmlDocumentPolicy.LocalResourceDeadlineMs)
            Dim linkedToken = linked.Token
            Try
                Await _gate.WaitAsync(linkedToken).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return HtmlResourceResult.Refused(If(token.IsCancellationRequested OrElse IsDisposed, HtmlOutcome.Cancelled, HtmlOutcome.TimedOut))
            End Try
            Try
                Dim bytes = Await Task.Run(Function() _read(key, linkedToken), linkedToken).ConfigureAwait(False)
                If IsDisposed Then Return HtmlResourceResult.Refused(HtmlOutcome.Cancelled)
                If bytes Is Nothing Then Return HtmlResourceResult.Refused(HtmlOutcome.SourceMissing)
                If bytes.LongLength > limit Then Return HtmlResourceResult.Refused(HtmlOutcome.LimitExceeded)
                Dim version As New HtmlSourceVersion With {.Length = bytes.LongLength, .Digest = HtmlDocumentPolicy.ToLowerHex(SHA256.HashData(bytes))}
                Return New HtmlResourceResult With {.Status = HtmlOutcome.Ok, .Kind = entry.Kind, .MimeType = entry.MimeType, .Bytes = bytes, .ResourceVersion = version}
            Catch ex As OperationCanceledException
                Return HtmlResourceResult.Refused(If(token.IsCancellationRequested OrElse IsDisposed, HtmlOutcome.Cancelled, HtmlOutcome.TimedOut))
            Catch ex As UnauthorizedAccessException
                Return HtmlResourceResult.Refused(HtmlOutcome.AccessDenied)
            Catch ex As NotSupportedException
                ' A DRM-protected or otherwise unreadable entry.
                Return HtmlResourceResult.Refused(HtmlOutcome.UnsupportedResource)
            Catch ex As InvalidDataException
                Return HtmlResourceResult.Refused(HtmlOutcome.InvalidMarkup)
            Catch ex As IOException
                Return HtmlResourceResult.Refused(HtmlOutcome.SourceMissing)
            Catch ex As ObjectDisposedException
                Return HtmlResourceResult.Refused(HtmlOutcome.Cancelled)
            Finally
                ReleaseGate()
            End Try
        End Using
    End Function

    Private Sub ReleaseGate()
        Try
            _gate.Release()
        Catch ex As ObjectDisposedException
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(_disposed, 1) <> 0 Then Return
        _cancel.Cancel()
        _lease?.Dispose()
    End Sub
End Class

''' <summary>An online page: the host fetcher supplies every byte, so the source itself reads nothing
''' and resolves no local key.</summary>
Friend NotInheritable Class OnlineHtmlDocumentSource
    Implements IHtmlDocumentSource

    Private ReadOnly _request As HtmlOpenRequest

    Friend Sub New(page As Uri)
        If page Is Nothing Then Throw New ArgumentNullException(NameOf(page))
        Dim locator = page.GetLeftPart(UriPartial.Query)
        _request = New HtmlOpenRequest With {
            .SourceId = Guid.NewGuid().ToString("N"),
            .SourceKind = HtmlSourceKind.OnlinePage,
            .DisplayName = page.IdnHost,
            .OriginalLocator = locator,
            .EntryResourceKey = locator,
            .Provenance = "online:" & HtmlAddressPolicy.OriginOf(page),
            .IsSecure = False,
            .IsReadOnly = True}
    End Sub

    Public ReadOnly Property Request As HtmlOpenRequest Implements IHtmlDocumentSource.Request
        Get
            Return _request
        End Get
    End Property

    Public Function ReadEntryAsync(token As CancellationToken) As Task(Of HtmlResourceResult) Implements IHtmlDocumentSource.ReadEntryAsync
        Return Task.FromResult(HtmlResourceResult.Refused(HtmlOutcome.NetworkBlocked))
    End Function

    Public Function ResolveKey(key As String) As HtmlKeyResolution Implements IHtmlDocumentSource.ResolveKey
        Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)
    End Function

    Public Function KindOf(key As String) As HtmlResourceKind Implements IHtmlDocumentSource.KindOf
        Return HtmlResourceKind.Other
    End Function

    Public Function ReadResourceAsync(key As String, token As CancellationToken) As Task(Of HtmlResourceResult) Implements IHtmlDocumentSource.ReadResourceAsync
        Return Task.FromResult(HtmlResourceResult.Refused(HtmlOutcome.NetworkBlocked))
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
    End Sub
End Class

''' <summary>The 276 seam: the converter alone knows which output root belongs to which job. Until it
''' answers Ok with a root that contains the entry, the output is not opened at all - a marker inside
''' the HTML never counts as verification.</summary>
Friend Interface IHtmlProducerVerification
    Function Verify(jobId As String, ByRef outputRoot As String, ByRef entryPath As String) As HtmlOutcome
End Interface

Friend Module HtmlDocumentAdapters
    Friend Const MaxPageAddressLength As Integer = 2048

    ''' <summary>The typed address of an online page (section 13): absolute http(s), no user-info, the
    ''' scheme's own port, no local or private destination by name or literal. The fetcher re-checks the
    ''' resolved addresses at connect time.</summary>
    Friend Function TryParseOnlinePage(text As String, ByRef page As Uri) As Boolean
        page = Nothing
        If String.IsNullOrWhiteSpace(text) OrElse text.Length > MaxPageAddressLength Then Return False
        Dim candidate As Uri = Nothing
        If Not Uri.TryCreate(text.Trim(), UriKind.Absolute, candidate) Then Return False
        If Not HtmlNetworkFetcher.ValidateShape(candidate) Then Return False
        page = candidate
        Return True
    End Function

    ''' <summary>The EPUB media types a chapter or asset may be served as.</summary>
    Friend Function KindOfMediaType(mediaType As String) As HtmlResourceKind
        Dim mime = If(mediaType, "").Trim().ToLowerInvariant()
        Select Case mime
            Case "application/xhtml+xml", "text/html"
                Return HtmlResourceKind.Document
            Case "text/css"
                Return HtmlResourceKind.Stylesheet
            Case "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml", "image/avif", "image/bmp"
                Return HtmlResourceKind.Image
            Case "application/vnd.ms-opentype", "application/font-sfnt", "application/font-woff", "application/x-font-ttf", "application/x-font-otf"
                Return HtmlResourceKind.Font
        End Select
        If mime.StartsWith("font/", StringComparison.Ordinal) Then Return HtmlResourceKind.Font
        Return HtmlResourceKind.Other
    End Function

    ''' <summary>A 274 package as a host source: the manifest is the key set, the chapter is the entry,
    ''' the snapshot is read through <paramref name="read"/> and released with the source.</summary>
    Friend Function FromEpubPackage(book As EpubPackage, chapterKey As String, identity As String,
                                    read As Func(Of String, CancellationToken, Byte()), Optional lease As IDisposable = Nothing) As PackageHtmlDocumentSource
        If book Is Nothing Then Throw New ArgumentNullException(NameOf(book))
        Dim entries As New Dictionary(Of String, HtmlPackageEntry)(StringComparer.Ordinal)
        For Each resource In book.Resources.Values
            Dim mime = If(resource.MediaType, "")
            Dim kind = KindOfMediaType(mime)
            If kind = HtmlResourceKind.Other Then Continue For
            If kind = HtmlResourceKind.Document Then mime = "text/html; charset=utf-8"
            entries(resource.Key) = New HtmlPackageEntry(kind, mime)
        Next
        Dim request As New HtmlOpenRequest With {
            .SourceId = Guid.NewGuid().ToString("N"),
            .SourceKind = HtmlSourceKind.EpubPackage,
            .DisplayName = If(String.IsNullOrWhiteSpace(book.Title), chapterKey, book.Title),
            .OriginalLocator = If(identity, ""),
            .EntryResourceKey = chapterKey,
            .Provenance = "epub:" & chapterKey,
            .IsSecure = False,
            .IsReadOnly = True}
        Return New PackageHtmlDocumentSource(request, entries, read, lease)
    End Function

    ''' <summary>A materialized archive or provider entry: only that one file, never its siblings in the
    ''' shared materialization folder (section 10.1).</summary>
    Friend Function SingleEntry(path As String, kind As HtmlSourceKind, provenance As String, identity As String) As PackageHtmlDocumentSource
        Dim name = IO.Path.GetFileName(path)
        Dim key = If(HtmlDocumentPolicy.ValidateKey(name).IsAllowed, name, "index.html")
        Dim entries As New Dictionary(Of String, HtmlPackageEntry)(StringComparer.Ordinal) From {
            {key, New HtmlPackageEntry(HtmlResourceKind.Document, "text/html; charset=utf-8")}}
        Dim request As New HtmlOpenRequest With {
            .SourceId = Guid.NewGuid().ToString("N"),
            .SourceKind = kind,
            .DisplayName = name,
            .OriginalLocator = If(identity, path),
            .EntryResourceKey = key,
            .Provenance = If(provenance, ""),
            .IsSecure = False,
            .IsReadOnly = True}
        Return New PackageHtmlDocumentSource(request, entries,
            Function(requested, token)
                Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 81920, FileOptions.SequentialScan)
                    If stream.Length > HtmlDocumentPolicy.MaxEntryBytes Then Return New Byte(CInt(HtmlDocumentPolicy.MaxEntryBytes)) {}
                    Dim buffer(CInt(stream.Length) - 1) As Byte
                    Dim offset = 0
                    While offset < buffer.Length
                        token.ThrowIfCancellationRequested()
                        Dim count = stream.Read(buffer, offset, buffer.Length - offset)
                        If count = 0 Then Exit While
                        offset += count
                    End While
                    If offset < buffer.Length Then Array.Resize(buffer, offset)
                    Return buffer
                End Using
            End Function)
    End Function

    ''' <summary>A 276 output, opened only after the producer verified the job's root; it keeps the
    ''' converter provenance and the same restricted local profile as any other document.</summary>
    Friend Function OpenConverterOutput(verification As IHtmlProducerVerification, jobId As String, snapshotCacheBytes As Long,
                                        ByRef source As IHtmlDocumentSource) As HtmlOutcome
        source = Nothing
        If verification Is Nothing OrElse String.IsNullOrWhiteSpace(jobId) Then Return HtmlOutcome.UnsupportedResource
        Dim root = ""
        Dim entry = ""
        Dim status As HtmlOutcome
        Try
            status = verification.Verify(jobId, root, entry)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is InvalidOperationException
            Return HtmlOutcome.UnsupportedResource
        End Try
        If status <> HtmlOutcome.Ok Then Return status
        If String.IsNullOrWhiteSpace(root) OrElse String.IsNullOrWhiteSpace(entry) Then Return HtmlOutcome.UnsupportedResource
        Dim local As LocalHtmlDocumentSource = Nothing
        status = LocalHtmlDocumentSource.Open(entry, root, False, snapshotCacheBytes, local)
        If status <> HtmlOutcome.Ok Then Return status
        local.Request.SourceKind = HtmlSourceKind.ConverterOutput
        local.Request.Provenance = "converter:" & jobId
        source = local
        Return HtmlOutcome.Ok
    End Function
End Module
#End If
