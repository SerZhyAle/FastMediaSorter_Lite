#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading

''' <summary>What asking for a file's local copy came to.</summary>
Friend Enum EnsureLocalOutcome
    ''' <summary>The path does not belong to this session - nothing to do.</summary>
    NotOurs = 0
    ''' <summary>The file is at its local path, complete.</summary>
    Ready = 1
    ''' <summary>The fetch failed, or what arrived was not the whole file.</summary>
    Failed = 2
    ''' <summary>The session was left (or the caller gave up) while fetching.</summary>
    Cancelled = 3
End Enum

''' <summary>
''' The open resource: its provider, the directory being shown, that directory's listing,
''' the cache session directory and a cancellation source
''' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-2, PV-3, PV-6). Exactly one
''' is open at a time, like the archive session, and for the same reason: the viewer shows
''' one folder.
'''
''' The file list carries each entry's <see cref="ResourceEntry.LocalPath"/>, decided when
''' the listing is read and before anything is fetched - so the list is built from the
''' provider's metadata alone, never by walking the cache, which only holds what has been
''' looked at. The bytes appear at that path through <see cref="TryEnsureLocal"/>.
''' </summary>
Friend NotInheritable Class ResourceSession
    Implements IDisposable

    Private ReadOnly cancel_Source As New CancellationTokenSource()
    Private ReadOnly fetch_Gate As New Object()

    Private entry_List As IReadOnlyList(Of ResourceEntry) = New List(Of ResourceEntry)()
    Private by_Local_Path As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

    ' The LRU accounting, as in the archive session: sizes of what is on disk, recency
    ' oldest first, and the total. Touched only under fetch_Gate.
    Private ReadOnly local_Sizes As New Dictionary(Of Integer, Long)()
    Private ReadOnly lru_Recency As New List(Of Integer)()
    Private session_Bytes As Long
    Private disposed As Boolean

    Friend Sub New(resource As ResourceDescriptor, provider As IFileSystemProvider,
                   sessionDir As String, maxCacheBytes As Long)
        If provider Is Nothing Then Throw New ArgumentNullException(NameOf(provider))
        Me.Resource = resource
        Me.Provider = provider
        Me.SessionDir = If(sessionDir, "")
        Me.MaxCacheBytes = maxCacheBytes
        Me.CurrentDirectory = If(resource IsNot Nothing, resource.RootPath, "")
    End Sub

    Friend ReadOnly Property Resource As ResourceDescriptor
    Friend ReadOnly Property Provider As IFileSystemProvider
    Friend ReadOnly Property SessionDir As String
    Friend ReadOnly Property MaxCacheBytes As Long
    Friend Property CurrentDirectory As String

    ''' <summary>Cancelled when the session is left: every fetch in flight stops, so a
    ''' worker still pulling from a slow link cannot hold up navigation or shutdown.</summary>
    Friend ReadOnly Property Cancellation As CancellationToken
        Get
            Return cancel_Source.Token
        End Get
    End Property

    Friend ReadOnly Property Entries As IReadOnlyList(Of ResourceEntry)
        Get
            Return entry_List
        End Get
    End Property

    ''' <summary>What the folder box shows and the registry keeps for this directory.</summary>
    Friend ReadOnly Property Locator As String
        Get
            Return ResourceLocator.Build(Resource, CurrentDirectory)
        End Get
    End Property

    Friend ReadOnly Property NeedsMaterialisation As Boolean
        Get
            Return Provider.Capabilities.NeedsMaterialisation
        End Get
    End Property

    ''' <summary>
    ''' Reads <paramref name="remoteDir"/> and assigns every file its local path. For a
    ''' provider that materialises, that is <c>&lt;session&gt;\00042_name.ext</c>; otherwise
    ''' the file's own path.
    ''' </summary>
    Friend Sub LoadListing(remoteDir As String, ct As CancellationToken)
        Dim listed As IReadOnlyList(Of ResourceEntry) = Provider.ListEntries(remoteDir, ct)
        Dim files As New List(Of ResourceEntry)(listed.Count)
        Dim map As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        For Each item As ResourceEntry In listed
            If item Is Nothing OrElse item.IsDirectory Then Continue For
            Dim index As Integer = files.Count
            If NeedsMaterialisation Then
                item.LocalPath = Path.Combine(SessionDir, RemoteCachePaths.EntryFileName(index, item.Name))
            ElseIf String.IsNullOrEmpty(item.LocalPath) Then
                item.LocalPath = item.FullPath
            End If
            files.Add(item)
            map(item.LocalPath) = index
        Next

        SyncLock fetch_Gate
            ' A new directory's names reuse the indexes, so the old accounting describes
            ' files that now belong to different entries: forget it and empty the cache.
            ForgetMaterialisedLocked()
            entry_List = files
            by_Local_Path = map
            CurrentDirectory = remoteDir
        End SyncLock
    End Sub

    ''' <summary>Which entry a local path belongs to, or -1.</summary>
    Friend Function IndexOfLocalPath(localPath As String) As Integer
        If String.IsNullOrEmpty(localPath) Then Return -1
        Dim found As Integer
        If by_Local_Path.TryGetValue(localPath, found) Then Return found
        Return -1
    End Function

    Friend Function OwnsLocalPath(localPath As String) As Boolean
        Return IndexOfLocalPath(localPath) >= 0
    End Function

    ''' <summary>The entry behind a local path, or Nothing.</summary>
    Friend Function EntryAt(localPath As String) As ResourceEntry
        Dim index As Integer = IndexOfLocalPath(localPath)
        Return If(index < 0, Nothing, entry_List(index))
    End Function

    ''' <summary>
    ''' Makes sure the entry behind <paramref name="localPath"/> is on disk, complete. Must
    ''' run BEFORE anything asks whether the file exists: until it is fetched it does not,
    ''' and the display path drops a missing file from the list.
    '''
    ''' Serialized: the display path and the prefetch worker both call this. The download
    ''' goes to "name.fms-part" and is renamed into place only once its size matches the
    ''' listing, so a truncated file is never handed to a decoder under the real name.
    ''' </summary>
    ''' <param name="protectAround">How many neighbours either side of this entry the
    ''' eviction must leave alone (at least 1).</param>
    Friend Function TryEnsureLocal(localPath As String,
                                   Optional ct As CancellationToken = Nothing,
                                   Optional progress As IProgress(Of Long) = Nothing,
                                   Optional protectAround As Integer = 1) As EnsureLocalOutcome
        Dim index As Integer = IndexOfLocalPath(localPath)
        If index < 0 Then Return EnsureLocalOutcome.NotOurs
        If Not NeedsMaterialisation Then Return EnsureLocalOutcome.Ready

        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel_Source.Token, ct)
            SyncLock fetch_Gate
                If disposed Then Return EnsureLocalOutcome.Cancelled
                Return EnsureLocalLocked(index, linked.Token, progress, Math.Max(1, protectAround))
            End SyncLock
        End Using
    End Function

    Private Function EnsureLocalLocked(index As Integer, ct As CancellationToken,
                                       progress As IProgress(Of Long), protectAround As Integer) As EnsureLocalOutcome
        Dim entry As ResourceEntry = entry_List(index)

        ' Already here and the right size - the common case on the way back through a folder.
        Try
            Dim onDisk As New FileInfo(entry.LocalPath)
            If onDisk.Exists AndAlso (entry.Length < 0 OrElse onDisk.Length = entry.Length) Then
                Touch(index, onDisk.Length)
                EvictOverBudget(index, protectAround)
                Return EnsureLocalOutcome.Ready
            End If
        Catch
            ' Fall through and fetch again.
        End Try

        Dim part As String = entry.LocalPath & FileManager.Partial_Move_Suffix
        Try
            Directory.CreateDirectory(SessionDir)
            Provider.Download(entry.FullPath, part, progress, ct)
            Dim arrived As Long = New FileInfo(part).Length
            If entry.Length >= 0 AndAlso arrived <> entry.Length Then
                Throw New IOException("Downloaded " & arrived.ToString() & " of " & entry.Length.ToString() & " bytes.")
            End If
            If File.Exists(entry.LocalPath) Then File.Delete(entry.LocalPath)
            File.Move(part, entry.LocalPath)
            Touch(index, arrived)
            EvictOverBudget(index, protectAround)
            Return EnsureLocalOutcome.Ready
        Catch ex As OperationCanceledException
            DeleteQuietly(part)
            Return EnsureLocalOutcome.Cancelled
        Catch ex As Exception
            AppFileLogger.WriteLine("Resource: entry not fetched: " & entry.FullPath & " - " & ex.Message)
            DeleteQuietly(part)
            Return EnsureLocalOutcome.Failed
        End Try
    End Function

    ''' <summary>
    ''' Forgets the local copy of an entry after a write changed or removed the remote file
    ''' (PV-5): the next look fetches it again instead of showing the old bytes.
    ''' </summary>
    Friend Sub Invalidate(localPath As String)
        Dim index As Integer = IndexOfLocalPath(localPath)
        If index < 0 Then Return
        SyncLock fetch_Gate
            DropLocalLocked(index)
        End SyncLock
    End Sub

    ''' <summary>
    ''' Records that an entry now lives under a new remote name (a rename or a move within
    ''' the resource), and returns its new local path. The cached copy follows it when there
    ''' is one; the index - and so the place in the list - stays.
    ''' </summary>
    Friend Function Rekey(localPath As String, newRemotePath As String, newName As String) As String
        Dim index As Integer = IndexOfLocalPath(localPath)
        If index < 0 Then Return localPath
        SyncLock fetch_Gate
            Dim entry As ResourceEntry = entry_List(index)
            Dim oldLocal As String = entry.LocalPath
            Dim newLocal As String = If(NeedsMaterialisation,
                Path.Combine(SessionDir, RemoteCachePaths.EntryFileName(index, newName)),
                newRemotePath)
            If NeedsMaterialisation AndAlso Not String.Equals(oldLocal, newLocal, StringComparison.OrdinalIgnoreCase) Then
                Try
                    If File.Exists(oldLocal) Then File.Move(oldLocal, newLocal, True)
                Catch
                    DropLocalLocked(index)
                End Try
            End If
            entry.FullPath = newRemotePath
            entry.Name = newName
            entry.LocalPath = newLocal
            by_Local_Path.Remove(oldLocal)
            by_Local_Path(newLocal) = index
            Return newLocal
        End SyncLock
    End Function

    ' ----------------------------------------------------------------- the budget ----

    Private Sub Touch(index As Integer, size As Long)
        If Not local_Sizes.ContainsKey(index) Then
            local_Sizes(index) = size
            session_Bytes += size
        End If
        lru_Recency.Remove(index)
        lru_Recency.Add(index)
    End Sub

    Private Sub EvictOverBudget(justTouched As Integer, protectAround As Integer)
        Dim victims As List(Of Integer) = RemoteCacheStore.ChooseEvictions(
            lru_Recency, local_Sizes, session_Bytes, MaxCacheBytes,
            Function(candidate) Math.Abs(candidate - justTouched) <= protectAround)
        For Each victim As Integer In victims
            DropLocalLocked(victim)
        Next
    End Sub

    Private Sub DropLocalLocked(index As Integer)
        If index >= 0 AndAlso index < entry_List.Count Then DeleteQuietly(entry_List(index).LocalPath)
        Dim size As Long
        If local_Sizes.TryGetValue(index, size) Then
            local_Sizes.Remove(index)
            session_Bytes -= size
        End If
        lru_Recency.Remove(index)
    End Sub

    Private Sub ForgetMaterialisedLocked()
        If NeedsMaterialisation Then
            For Each index As Integer In New List(Of Integer)(local_Sizes.Keys)
                DropLocalLocked(index)
            Next
        End If
        local_Sizes.Clear()
        lru_Recency.Clear()
        session_Bytes = 0
    End Sub

    ''' <summary>Bytes currently materialised - for tests and for the log.</summary>
    Friend ReadOnly Property MaterialisedBytes As Long
        Get
            SyncLock fetch_Gate
                Return session_Bytes
            End SyncLock
        End Get
    End Property

    Private Shared Sub DeleteQuietly(filePath As String)
        Try
            If Not String.IsNullOrEmpty(filePath) AndAlso File.Exists(filePath) Then File.Delete(filePath)
        Catch
            ' Held by a player or an antivirus: the session's own cleanup or the sweep takes it.
        End Try
    End Sub

    ''' <summary>Cancels every fetch in flight, then lets the provider go. Does NOT delete
    ''' the session directory: whatever shows a materialised file must be released first,
    ''' and only the owner knows when that has happened (ResourceSessionSlot.Leave).</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        If disposed Then Return
        Try
            cancel_Source.Cancel()
        Catch
        End Try
        ' Taken after Cancel, so a fetch in flight sees the token and lets go of the gate.
        SyncLock fetch_Gate
            disposed = True
        End SyncLock
        Try
            TryCast(Provider, IDisposable)?.Dispose()
        Catch ex As Exception
            AppFileLogger.WriteLine("Resource: closing the provider failed - " & ex.Message)
        End Try
        ' The source is deliberately not disposed: a late caller still reads its Token, and
        ' a cancelled source with no timer holds nothing worth releasing early.
    End Sub

End Class

''' <summary>
''' The one open resource, and the ordering rule for letting it go (006 PV-2): whatever
''' shows a materialised file is released FIRST, then the session closes, then its
''' directory is deleted - and whatever still refuses to go is left to the sweep, never
''' reported. Main_Form owns one of these; a test drives it without a form.
''' </summary>
Friend NotInheritable Class ResourceSessionSlot

    Private current_Session As ResourceSession

    Friend ReadOnly Property Current As ResourceSession
        Get
            Return current_Session
        End Get
    End Property

    Friend ReadOnly Property IsOpen As Boolean
        Get
            Return current_Session IsNot Nothing
        End Get
    End Property

    ''' <summary>Opens <paramref name="session"/>, closing the previous one first.</summary>
    Friend Sub Enter(session As ResourceSession, Optional releaseMedia As Action = Nothing)
        Leave(releaseMedia)
        current_Session = session
        ResourceRouting.Attach(session)
    End Sub

    ''' <summary>Closes the open session, if any. Safe to call when none is open.</summary>
    Friend Sub Leave(Optional releaseMedia As Action = Nothing)
        Dim closing As ResourceSession = current_Session
        If closing Is Nothing Then Return

        ResourceRouting.Detach(closing)
        Try
            releaseMedia?.Invoke()
        Catch ex As Exception
            AppFileLogger.LogException("Resource: releasing media before leaving", ex)
        End Try

        current_Session = Nothing
        Try
            closing.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("Resource: closing the session", ex)
        End Try
        RemoteCacheStore.DeleteSession(closing.SessionDir)
    End Sub

End Class
#End If
