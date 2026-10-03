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

    ''' <summary>
    ''' One listing and its local-path index, published together and never changed after:
    ''' LoadListing and Rekey build a new pair under fetch_Gate and swap it in. A reader off
    ''' the gate - the routing on the UI thread, the prefetch worker - therefore never pairs
    ''' the index of one listing with the entries of another, and never reads a dictionary
    ''' while it is being written (057.S07-1). The entries' own fields are the one thing
    ''' Rekey still changes in place; each is a single reference write.
    ''' </summary>
    Private NotInheritable Class Listing
        Friend Sub New(entries As List(Of ResourceEntry), byLocalPath As Dictionary(Of String, Integer))
            Me.Entries = entries
            Me.ByLocalPath = byLocalPath
        End Sub
        Friend ReadOnly Property Entries As List(Of ResourceEntry)
        Friend ReadOnly Property ByLocalPath As Dictionary(Of String, Integer)
    End Class

    Private current_Listing As New Listing(New List(Of ResourceEntry)(),
                                           New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase))

    ' The LRU accounting, as in the archive session: sizes of what is on disk, recency
    ' oldest first, and the total. Touched only under fetch_Gate.
    Private ReadOnly local_Sizes As New Dictionary(Of Integer, Long)()
    Private ReadOnly lru_Recency As New List(Of Integer)()
    Private ReadOnly stale_Entries As New HashSet(Of Integer)()
    Private ReadOnly retained_Files As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly active_Fetches As New HashSet(Of CancellationTokenSource)()
    Private revision As Long
    Private session_Bytes As Long
    Private disposed As Boolean
    Private provider_Users As Integer
    Private provider_Disposed As Boolean

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
            Return Snapshot().Entries
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
        SyncLock fetch_Gate
            If disposed Then Throw New OperationCanceledException(Cancellation)
            provider_Users += 1
        End SyncLock
        Try
            Dim listed As IReadOnlyList(Of ResourceEntry) = Provider.ListEntries(remoteDir, ct)
            ct.ThrowIfCancellationRequested()
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
                If disposed Then Throw New OperationCanceledException(Cancellation)
                revision += 1
                ForgetMaterialisedLocked()
                Volatile.Write(current_Listing, New Listing(files, map))
                CurrentDirectory = remoteDir
            End SyncLock
        Finally
            SyncLock fetch_Gate
                provider_Users -= 1
                DisposeProviderIfIdleLocked()
                If disposed Then RemoteCacheStore.DeleteSession(SessionDir)
            End SyncLock
        End Try
    End Sub

    Private Function Snapshot() As Listing
        Return Volatile.Read(current_Listing)
    End Function

    Private Shared Function IndexIn(listing As Listing, localPath As String) As Integer
        If String.IsNullOrEmpty(localPath) Then Return -1
        Dim found As Integer
        If listing.ByLocalPath.TryGetValue(localPath, found) Then Return found
        Return -1
    End Function

    ''' <summary>Which entry a local path belongs to, or -1.</summary>
    Friend Function IndexOfLocalPath(localPath As String) As Integer
        Return IndexIn(Snapshot(), localPath)
    End Function

    Friend Function OwnsLocalPath(localPath As String) As Boolean
        Return IndexOfLocalPath(localPath) >= 0
    End Function

    ''' <summary>Background F3 cards use the remote cache too, without preempting the file
    ''' being displayed. Session cancellation stops the download when its resource is left.</summary>
    Friend Function MaterializeForThumbnail(localPath As String) As Boolean
        If Not OwnsLocalPath(localPath) Then Return False
        Return TryEnsureLocal(localPath, prefetch:=True) = EnsureLocalOutcome.Ready
    End Function

    ''' <summary>The entry behind a local path, or Nothing.</summary>
    Friend Function EntryAt(localPath As String) As ResourceEntry
        Dim listing As Listing = Snapshot()
        Dim index As Integer = IndexIn(listing, localPath)
        Return If(index < 0, Nothing, listing.Entries(index))
    End Function

    ''' <summary>
    ''' Makes sure the entry behind <paramref name="localPath"/> is on disk, complete. Must
    ''' run BEFORE anything asks whether the file exists: until it is fetched it does not,
    ''' and the display path drops a missing file from the list.
    '''
    ''' The display path preempts prefetch without waiting on its download. The download
    ''' goes to "name.fms-part" and is renamed into place only once its size matches the
    ''' listing, so a truncated file is never handed to a decoder under the real name.
    ''' </summary>
    ''' <param name="protectAround">How many neighbours either side of this entry the
    ''' eviction must leave alone (at least 1).</param>
    Friend Function TryEnsureLocal(localPath As String,
                                   Optional ct As CancellationToken = Nothing,
                                   Optional progress As IProgress(Of Long) = Nothing,
                                   Optional protectAround As Integer = 1,
                                   Optional prefetch As Boolean = False) As EnsureLocalOutcome
        If IndexOfLocalPath(localPath) < 0 Then Return EnsureLocalOutcome.NotOurs
        If Not NeedsMaterialisation Then Return EnsureLocalOutcome.Ready

        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel_Source.Token, ct)
            Dim index As Integer
            Dim entry As ResourceEntry
            Dim remotePath As String
            Dim expected As Long
            Dim version As Long
            SyncLock fetch_Gate
                If disposed OrElse linked.IsCancellationRequested Then Return EnsureLocalOutcome.Cancelled
                index = IndexOfLocalPath(localPath)
                If index < 0 Then Return EnsureLocalOutcome.Cancelled
                entry = current_Listing.Entries(index)
                If Not prefetch Then
                    ' Navigation supersedes speculative work (and an older display request).
                    For Each fetching As CancellationTokenSource In active_Fetches
                        fetching.CancelAsync()
                    Next
                End If
                Try
                    Dim onDisk As New FileInfo(localPath)
                    If Not stale_Entries.Contains(index) AndAlso Not retained_Files.ContainsKey(localPath) AndAlso onDisk.Exists AndAlso
                       (entry.Length < 0 OrElse onDisk.Length = entry.Length) Then
                        Touch(index, onDisk.Length)
                        EvictOverBudget(index, Math.Max(1, protectAround))
                        Return EnsureLocalOutcome.Ready
                    End If
                Catch
                    ' Fetch again.
                End Try
                remotePath = entry.FullPath
                expected = entry.Length
                version = revision
                active_Fetches.Add(linked)
            End SyncLock

            ' No provider call under the state gate. Unique staging files also let a
            ' cancelled provider unwind without touching a newer request's download.
            Dim part As String = localPath & "." & Guid.NewGuid().ToString("N") & FileManager.Partial_Move_Suffix
            Try
                Directory.CreateDirectory(SessionDir)
                Provider.Download(remotePath, part, progress, linked.Token)
                linked.Token.ThrowIfCancellationRequested()
                Dim arrived As Long = New FileInfo(part).Length
                If expected >= 0 AndAlso arrived <> expected Then
                    Throw New IOException("Downloaded " & arrived.ToString() & " of " & expected.ToString() & " bytes.")
                End If
                SyncLock fetch_Gate
                    If disposed OrElse linked.IsCancellationRequested OrElse version <> revision Then
                        Return EnsureLocalOutcome.Cancelled
                    End If
                    If File.Exists(localPath) Then File.Delete(localPath)
                    File.Move(part, localPath)
                    stale_Entries.Remove(index)
                    Touch(index, arrived)
                    EvictOverBudget(index, Math.Max(1, protectAround))
                End SyncLock
                Return EnsureLocalOutcome.Ready
            Catch ex As OperationCanceledException
                Return EnsureLocalOutcome.Cancelled
            Catch ex As Exception
                AppFileLogger.WriteLine("Resource: entry not fetched: " & remotePath & " - " & ex.Message)
                Return EnsureLocalOutcome.Failed
            Finally
                DeleteQuietly(part)
                SyncLock fetch_Gate
                    active_Fetches.Remove(linked)
                    DisposeProviderIfIdleLocked()
                    ' Leave may have swept while this provider was still unwinding.
                    If disposed Then RemoteCacheStore.DeleteSession(SessionDir)
                End SyncLock
            End Try
        End Using
    End Function

    ''' <summary>
    ''' Forgets the local copy of an entry after a write changed or removed the remote file
    ''' (PV-5): the next look fetches it again instead of showing the old bytes.
    ''' </summary>
    Friend Sub Invalidate(localPath As String)
        SyncLock fetch_Gate
            Dim index As Integer = IndexOfLocalPath(localPath)
            If index < 0 Then Return
            revision += 1
            stale_Entries.Add(index)
            DropLocalLocked(index)
        End SyncLock
    End Sub

    ''' <summary>
    ''' Records that an entry now lives under a new remote name (a rename or a move within
    ''' the resource), and returns its new local path. The cached copy follows it when there
    ''' is one; the index - and so the place in the list - stays.
    ''' </summary>
    Friend Function Rekey(localPath As String, newRemotePath As String, newName As String) As String
        SyncLock fetch_Gate
            revision += 1
            Dim listing As Listing = current_Listing
            Dim index As Integer = IndexIn(listing, localPath)
            If index < 0 Then Return localPath
            Dim entry As ResourceEntry = listing.Entries(index)
            Dim oldLocal As String = entry.LocalPath
            Dim newLocal As String = If(NeedsMaterialisation,
                Path.Combine(SessionDir, RemoteCachePaths.EntryFileName(index, newName)),
                newRemotePath)
            If NeedsMaterialisation AndAlso Not String.Equals(oldLocal, newLocal, StringComparison.OrdinalIgnoreCase) Then
                Try
                    If File.Exists(oldLocal) Then File.Move(oldLocal, newLocal, True)
                Catch
                    stale_Entries.Add(index)
                    DropLocalLocked(index)
                    RetainLocked(index, oldLocal)
                End Try
            End If
            entry.FullPath = newRemotePath
            entry.Name = newName
            entry.LocalPath = newLocal
            Dim map As New Dictionary(Of String, Integer)(listing.ByLocalPath, StringComparer.OrdinalIgnoreCase)
            map.Remove(oldLocal)
            map(newLocal) = index
            Volatile.Write(current_Listing, New Listing(listing.Entries, map))
            Return newLocal
        End SyncLock
    End Function

    ' ----------------------------------------------------------------- the budget ----

    Private Sub Touch(index As Integer, size As Long)
        Dim retained As Long
        Dim localPath As String = current_Listing.Entries(index).LocalPath
        If retained_Files.TryGetValue(localPath, retained) Then
            session_Bytes -= retained
            retained_Files.Remove(localPath)
        End If
        Dim previous As Long
        local_Sizes.TryGetValue(index, previous)
        local_Sizes(index) = size
        session_Bytes += size - previous
        lru_Recency.Remove(index)
        lru_Recency.Add(index)
    End Sub

    Private Sub EvictOverBudget(justTouched As Integer, protectAround As Integer)
        ' The distance wraps, as the prefetch does: after the last entry it fetches the
        ' first, and a straight distance left the file on screen unprotected (057.S07-2).
        Dim count As Integer = current_Listing.Entries.Count
        Dim victims As List(Of Integer) = RemoteCacheStore.ChooseEvictions(
            lru_Recency, local_Sizes, session_Bytes, MaxCacheBytes,
            Function(candidate)
                Dim distance As Integer = Math.Abs(candidate - justTouched)
                Return Math.Min(distance, count - distance) <= protectAround
            End Function)
        For Each victim As Integer In victims
            DropLocalLocked(victim)
        Next
    End Sub

    Private Sub DropLocalLocked(index As Integer)
        Dim entries As List(Of ResourceEntry) = current_Listing.Entries
        If index >= 0 AndAlso index < entries.Count Then
            If Not DeleteQuietly(entries(index).LocalPath) Then
                ' Keep the size in the accounting when delete fails (057.S07-10)
                ' and ensure it will be re-fetched by marking it stale
                stale_Entries.Add(index)
                Return
            End If
        End If
        Dim size As Long
        If local_Sizes.TryGetValue(index, size) Then
            local_Sizes.Remove(index)
            session_Bytes -= size
        End If
        lru_Recency.Remove(index)
    End Sub

    Private Sub RetainLocked(index As Integer, localPath As String)
        Dim size As Long
        If local_Sizes.TryGetValue(index, size) Then
            retained_Files(localPath) = size
            local_Sizes.Remove(index)
            lru_Recency.Remove(index)
        End If
    End Sub

    Private Sub ForgetMaterialisedLocked()
        For Each filePath As String In New List(Of String)(retained_Files.Keys)
            If DeleteQuietly(filePath) Then
                session_Bytes -= retained_Files(filePath)
                retained_Files.Remove(filePath)
            End If
        Next
        If NeedsMaterialisation Then
            For Each index As Integer In New List(Of Integer)(local_Sizes.Keys)
                DropLocalLocked(index)
                RetainLocked(index, current_Listing.Entries(index).LocalPath)
            Next
        End If
        stale_Entries.Clear()
        lru_Recency.Clear()
    End Sub

    ''' <summary>Bytes currently materialised - for tests and for the log.</summary>
    Friend ReadOnly Property MaterialisedBytes As Long
        Get
            SyncLock fetch_Gate
                Return session_Bytes
            End SyncLock
        End Get
    End Property

    Private Shared Function DeleteQuietly(filePath As String) As Boolean
        Try
            If Not String.IsNullOrEmpty(filePath) AndAlso File.Exists(filePath) Then File.Delete(filePath)
            Return True
        Catch
            ' Keep held files in the budget until deletion actually succeeds.
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Acquires a lease for provider operations / cache reads (100 §15.4 item 4).
    ''' Returns True and gives an IDisposable lease while the session is open;
    ''' returns False once the session is disposed or cancelling (stopping admission).
    ''' Disposing the lease decrements the user count and triggers provider cleanup if the session was disposed.
    ''' </summary>
    Friend Function TryAcquireLease(ByRef lease As IDisposable) As Boolean
        lease = Nothing
        SyncLock fetch_Gate
            If disposed OrElse cancel_Source.IsCancellationRequested Then Return False
            provider_Users += 1
            lease = New SessionLease(Me)
            Return True
        End SyncLock
    End Function

    Friend Function TryAcquireLease() As IDisposable
        Dim lease As IDisposable = Nothing
        If TryAcquireLease(lease) Then Return lease
        Return Nothing
    End Function

    Private NotInheritable Class SessionLease
        Implements IDisposable

        Private ReadOnly session As ResourceSession
        Private isDisposed As Integer

        Friend Sub New(session As ResourceSession)
            Me.session = session
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Interlocked.Exchange(isDisposed, 1) = 0 Then
                session.ReleaseLease()
            End If
        End Sub
    End Class

    Private Sub ReleaseLease()
        SyncLock fetch_Gate
            provider_Users -= 1
            DisposeProviderIfIdleLocked()
            If disposed AndAlso provider_Users <= 0 AndAlso active_Fetches.Count = 0 Then
                RemoteCacheStore.DeleteSession(SessionDir)
            End If
        End SyncLock
    End Sub

    ''' <summary>Cancels every fetch in flight, then lets the provider go. Does NOT delete
    ''' the session directory: whatever shows a materialised file must be released first,
    ''' and only the owner knows when that has happened (ResourceSessionSlot.Leave).</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        If disposed Then Return
        Try
            cancel_Source.CancelAsync()
        Catch
        End Try
        SyncLock fetch_Gate
            disposed = True
            DisposeProviderIfIdleLocked()
        End SyncLock
        ' The source is deliberately not disposed: a late caller still reads its Token, and
        ' a cancelled source with no timer holds nothing worth releasing early.
    End Sub

    Private Sub DisposeProviderIfIdleLocked()
        If Not disposed OrElse provider_Disposed OrElse provider_Users > 0 OrElse active_Fetches.Count > 0 Then Return
        provider_Disposed = True
        Try
            TryCast(Provider, IDisposable)?.Dispose()
        Catch ex As Exception
            AppFileLogger.WriteLine("Resource: closing the provider failed - " & ex.Message)
        End Try
    End Sub

End Class

''' <summary>
''' The one open resource, and the ordering rule for letting it go (006 PV-2): whatever
''' shows a materialised file is released FIRST, then the session closes, then its
''' directory is deleted - and whatever still refuses to go is left to the sweep, never
''' reported. Main_Form owns one of these; a test drives it without a form.
''' </summary>
Friend NotInheritable Class ResourceSessionSlot

    ' Folder-state seam: the UI supplies the actual assignments after closing a failed open.
    Friend Shared Sub RestoreFolderAfterFailure(folder As String, box As String, apply As Action(Of String, String))
        If ResourceLocator.IsLocator(folder) OrElse RemoteCachePaths.IsSessionCopy(folder) Then
            apply("", "")
        Else
            apply(If(folder, ""), If(box, ""))
        End If
    End Sub

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
