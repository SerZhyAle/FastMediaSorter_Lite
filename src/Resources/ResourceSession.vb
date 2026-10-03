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
    ''' One materialisation in flight (057.S07-6). The download itself runs OUTSIDE
    ''' <see cref="fetch_Gate"/> - only the accounting, the listing swap and the install
    ''' are under it - so the UI thread never waits behind a fetch it did not ask for, and
    ''' a UI request cancels the prefetch it would otherwise have waited behind. Exactly
    ''' one fetch owns an entry at a time: a second caller for the same entry cancels the
    ''' first and takes the index over when it has let go.
    ''' </summary>
    Private NotInheritable Class Fetch
        Friend Sub New(isPrefetch As Boolean)
            Me.IsPrefetch = isPrefetch
        End Sub

        ''' <summary>A prefetch yields to every UI request; a UI request yields to nothing.</summary>
        Friend ReadOnly IsPrefetch As Boolean
        Private ReadOnly source As New CancellationTokenSource()
        Friend ReadOnly Done As New ManualResetEventSlim()

        ''' <summary>Cancelled when this fetch is no longer wanted - preempted, invalidated,
        ''' or the session left. Linked into the provider call.</summary>
        Friend ReadOnly Property Token As CancellationToken
            Get
                Return source.Token
            End Get
        End Property

        Friend Sub Cancel()
            Try
                source.Cancel()
            Catch
            End Try
        End Sub

        Friend Sub Finish()
            Done.Set()
        End Sub
    End Class

    ''' <summary>
    ''' One listing and its local-path index, published together and never changed after:
    ''' LoadListing and Rekey build a new pair and swap it in. A reader off
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
    Private session_Bytes As Long
    ' Copies that would not delete (a player holds them) but belong to no entry of the
    ' current listing: still on disk, so still in the budget, but not evictable (057.S07-10).
    Private stranded_Bytes As Long
    ' Entries whose copy on disk must not be trusted by the size shortcut: invalidated but
    ' the delete failed, so the old bytes are still there under the final name (057.S07-10).
    Private ReadOnly stale_Indexes As New HashSet(Of Integer)()
    ' The fetch in flight per entry index, touched only under fetch_Gate.
    Private ReadOnly in_Flight As New Dictionary(Of Integer, Fetch)()
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
            ' files that now belong to different entries: forget it and empty the cache -
            ' except copies that would not delete, which stay on disk and in the budget,
            ' and are marked stale where the new listing reuses their name (057.S07-10).
            Dim survivors As List(Of KeyValuePair(Of String, Long)) = ForgetMaterialisedLocked()
            Volatile.Write(current_Listing, New Listing(files, map))
            CurrentDirectory = remoteDir
            AdoptSurvivorsLocked(survivors)
        End SyncLock
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
    ''' The download goes to "name.fms-part" and is renamed into place only once its size
    ''' matches the listing, so a truncated file is never handed to a decoder under the
    ''' real name. It runs OUTSIDE fetch_Gate (057.S07-6): a UI request
    ''' (<paramref name="prefetch"/> = False, the default) cancels every prefetch in flight
    ''' and then takes its own turn, so the window never freezes behind a 2 GB clip nobody
    ''' asked for - and a prefetch never displaces the file on screen.
    ''' </summary>
    ''' <param name="protectAround">How many neighbours either side of this entry the
    ''' eviction must leave alone (at least 1).</param>
    ''' <param name="prefetch">True from the prefetch worker only: yields to every UI
    ''' request, and gives way to any fetch already owning the entry.</param>
    Friend Function TryEnsureLocal(localPath As String,
                                   Optional ct As CancellationToken = Nothing,
                                   Optional progress As IProgress(Of Long) = Nothing,
                                   Optional protectAround As Integer = 1,
                                   Optional prefetch As Boolean = False) As EnsureLocalOutcome
        If IndexOfLocalPath(localPath) < 0 Then Return EnsureLocalOutcome.NotOurs
        If Not NeedsMaterialisation Then Return EnsureLocalOutcome.Ready

        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel_Source.Token, ct)
            Do
                Dim listing As Listing
                Dim index As Integer
                Dim mine As Fetch = Nothing
                Dim busy As Fetch = Nothing
                SyncLock fetch_Gate
                    If disposed Then Return EnsureLocalOutcome.Cancelled
                    ' Resolved under the gate: a fetch's wait can outlast a new listing,
                    ' whose indexes name other files.
                    listing = current_Listing
                    index = IndexIn(listing, localPath)
                    If index < 0 Then Return EnsureLocalOutcome.NotOurs

                    If Not prefetch Then
                        ' A UI request preempts every prefetch in flight (057.S07-6): the
                        ' file on screen is wanted now, the file behind it is not.
                        For Each flight As Fetch In in_Flight.Values
                            If flight.IsPrefetch Then flight.Cancel()
                        Next
                    End If
                    If in_Flight.TryGetValue(index, busy) Then
                        ' The index changes hands: the owner stops (its token is linked
                        ' into its download), and this caller takes over when it lets go.
                        busy.Cancel()
                    Else
                        mine = New Fetch(prefetch)
                        in_Flight(index) = mine
                    End If
                End SyncLock

                If busy IsNot Nothing Then
                    ' Outside the gate: Invalidate, Rekey and the next listing stay free
                    ' while this waits (057.S07-6).
                    busy.Done.Wait()
                    Continue Do
                End If

                Dim outcome As EnsureLocalOutcome =
                    EnsureEntry(listing, index, linked.Token, mine.Token, progress, Math.Max(1, protectAround))

                SyncLock fetch_Gate
                    Dim registered As Fetch = Nothing
                    If in_Flight.TryGetValue(index, registered) AndAlso ReferenceEquals(registered, mine) Then
                        in_Flight.Remove(index)
                    End If
                    mine.Finish()
                End SyncLock
                Return outcome
            Loop
        End Using
    End Function

    ''' <summary>Fetches one entry without holding <see cref="fetch_Gate"/> across the
    ''' download. Only the shortcut check and the install take the gate; everything between
    ''' is the provider's to take as long as it takes.</summary>
    Private Function EnsureEntry(listing As Listing, index As Integer, ct As CancellationToken,
                                 fetchToken As CancellationToken, progress As IProgress(Of Long),
                                 protectAround As Integer) As EnsureLocalOutcome
        Dim entry As ResourceEntry = listing.Entries(index)

        ' Already here and the right size - the common case on the way back through a
        ' folder. A copy that outlived its invalidation is never trusted by its size:
        ' the bytes under the name can be the old ones (057.S07-10).
        SyncLock fetch_Gate
            If Not stale_Indexes.Contains(index) Then
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
            End If
        End SyncLock

        Dim part As String = entry.LocalPath & FileManager.Partial_Move_Suffix
        Try
            Directory.CreateDirectory(SessionDir)
            Provider.Download(entry.FullPath, part, progress,
                              CancellationTokenSource.CreateLinkedTokenSource(ct, fetchToken).Token)
            ' A provider that pays no attention to its token can still return: cancelled
            ' means the bytes are not wanted - the entry was invalidated, or a UI request
            ' took the index over - and must not be installed (057.S07-6, 057.S07-10).
            ct.ThrowIfCancellationRequested()
            fetchToken.ThrowIfCancellationRequested()
            Dim arrived As Long = New FileInfo(part).Length
            If entry.Length >= 0 AndAlso arrived <> entry.Length Then
                Throw New IOException("Downloaded " & arrived.ToString() & " of " & entry.Length.ToString() & " bytes.")
            End If

            SyncLock fetch_Gate
                ' The install is the one moment the file appears under its final name, so
                ' it happens under the gate AND only if the world is still the one the
                ' fetch started in: the listing may have been replaced, or the entry
                ' renamed, while the bytes were on the wire.
                If disposed OrElse fetchToken.IsCancellationRequested OrElse
                   Not ReferenceEquals(Volatile.Read(current_Listing), listing) OrElse
                   Not ReferenceEquals(current_Listing.Entries(index), entry) Then
                    Throw New OperationCanceledException("The listing changed while the file was being fetched.")
                End If
                If File.Exists(entry.LocalPath) Then File.Delete(entry.LocalPath)
                File.Move(part, entry.LocalPath)
                stale_Indexes.Remove(index)
                Touch(index, arrived)
                EvictOverBudget(index, protectAround)
                Return EnsureLocalOutcome.Ready
            End SyncLock
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
    ''' (PV-5): the next look fetches it again instead of showing the old bytes. A fetch of
    ''' the entry still in flight is cancelled - what it is pulling is no longer current.
    ''' </summary>
    Friend Sub Invalidate(localPath As String)
        SyncLock fetch_Gate
            Dim index As Integer = IndexOfLocalPath(localPath)
            If index < 0 Then Return
            CancelFlightLocked(index)
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
            Dim listing As Listing = current_Listing
            Dim index As Integer = IndexIn(listing, localPath)
            If index < 0 Then Return localPath
            CancelFlightLocked(index)
            Dim entry As ResourceEntry = listing.Entries(index)
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
            Dim map As New Dictionary(Of String, Integer)(listing.ByLocalPath, StringComparer.OrdinalIgnoreCase)
            map.Remove(oldLocal)
            map(newLocal) = index
            Volatile.Write(current_Listing, New Listing(listing.Entries, map))
            Return newLocal
        End SyncLock
    End Function

    ' ----------------------------------------------------------------- the budget ----

    Private Sub CancelFlightLocked(index As Integer)
        Dim flight As Fetch = Nothing
        If in_Flight.TryGetValue(index, flight) Then flight.Cancel()
    End Sub

    Private Sub Touch(index As Integer, size As Long)
        If Not local_Sizes.ContainsKey(index) Then
            local_Sizes(index) = size
            session_Bytes += size
        End If
        lru_Recency.Remove(index)
        lru_Recency.Add(index)
    End Sub

    Private Sub EvictOverBudget(justTouched As Integer, protectAround As Integer)
        ' The distance wraps, as the prefetch does: after the last entry it fetches the
        ' first, and a straight distance left the file on screen unprotected (057.S07-2).
        Dim count As Integer = current_Listing.Entries.Count
        Dim victims As List(Of Integer) = RemoteCacheStore.ChooseEvictions(
            lru_Recency, local_Sizes, session_Bytes + stranded_Bytes, MaxCacheBytes,
            Function(candidate)
                Dim distance As Integer = Math.Abs(candidate - justTouched)
                Return Math.Min(distance, count - distance) <= protectAround
            End Function)
        For Each victim As Integer In victims
            DropLocalLocked(victim)
        Next
    End Sub

    ''' <summary>Forgets an entry's local copy. When the file will not delete - a player
    ''' holds it - the size STAYS in the budget, because the bytes are still on disk, and
    ''' the index is marked stale so the size shortcut never hands those bytes out as
    ''' current (057.S07-10). Returns whether the copy is gone.</summary>
    Private Function DropLocalLocked(index As Integer) As Boolean
        Dim entries As List(Of ResourceEntry) = current_Listing.Entries
        Dim deleted As Boolean = True
        If index >= 0 AndAlso index < entries.Count Then deleted = TryDeleteQuietly(entries(index).LocalPath)
        If deleted Then
            Dim size As Long
            If local_Sizes.TryGetValue(index, size) Then
                local_Sizes.Remove(index)
                session_Bytes -= size
            End If
            lru_Recency.Remove(index)
            stale_Indexes.Remove(index)
        Else
            stale_Indexes.Add(index)
        End If
        Return deleted
    End Function

    ''' <summary>Empties the accounting for a listing that is going away, and returns the
    ''' copies that would not delete, with their sizes: they stay on disk, so they stay in
    ''' the budget until the session directory is swept.</summary>
    Private Function ForgetMaterialisedLocked() As List(Of KeyValuePair(Of String, Long))
        Dim survivors As New List(Of KeyValuePair(Of String, Long))()
        For Each index As Integer In New List(Of Integer)(local_Sizes.Keys)
            Dim path As String = If(index >= 0 AndAlso index < current_Listing.Entries.Count,
                                    current_Listing.Entries(index).LocalPath, "")
            If path.Length > 0 AndAlso local_Sizes.ContainsKey(index) AndAlso Not TryDeleteQuietly(path) Then
                survivors.Add(New KeyValuePair(Of String, Long)(path, local_Sizes(index)))
            End If
            local_Sizes.Remove(index)
            lru_Recency.Remove(index)
        Next
        ' Every fetch in flight belongs to the listing being replaced: none of them may
        ' install into the new one (their install check would refuse anyway).
        For Each flight As Fetch In in_Flight.Values
            flight.Cancel()
        Next
        stale_Indexes.Clear()
        stranded_Bytes = 0
        session_Bytes = 0
        Return survivors
    End Function

    ''' <summary>A copy that would not delete keeps its place in the budget when the new
    ''' listing names the same local path again - marked stale, so the first look at it
    ''' fetches instead of trusting the old bytes by size. A path the new listing does not
    ''' name is stranded: counted, but nothing can evict it any more.</summary>
    Private Sub AdoptSurvivorsLocked(survivors As List(Of KeyValuePair(Of String, Long)))
        For Each survivor As KeyValuePair(Of String, Long) In survivors
            Dim index As Integer = IndexIn(current_Listing, survivor.Key)
            If NeedsMaterialisation AndAlso index >= 0 Then
                If Not local_Sizes.ContainsKey(index) Then
                    local_Sizes(index) = survivor.Value
                    session_Bytes += survivor.Value
                    lru_Recency.Add(index)
                End If
                stale_Indexes.Add(index)
            Else
                stranded_Bytes += survivor.Value
            End If
        Next
    End Sub

    ''' <summary>Bytes currently materialised - for tests and for the log.</summary>
    Friend ReadOnly Property MaterialisedBytes As Long
        Get
            SyncLock fetch_Gate
                Return session_Bytes + stranded_Bytes
            End SyncLock
        End Get
    End Property

    ''' <summary>Whether the file went. A copy held by a player or an antivirus does not
    ''' throw - the session's own cleanup or the sweep takes it later.</summary>
    Private Shared Function TryDeleteQuietly(filePath As String) As Boolean
        Try
            If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then Return True
            File.Delete(filePath)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Shared Sub DeleteQuietly(filePath As String)
        TryDeleteQuietly(filePath)
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

    ''' <summary>
    ''' What the folder box and the current folder go back to when opening a resource
    ''' failed with the session already closed (057.S07-5). A remembered plain folder is
    ''' restored exactly as it was; a remembered locator is NOT - the session it named is
    ''' closed, and putting it back would leave the box naming a session that no longer
    ''' exists, for the next listing to scan through LocalProvider. What remains is the
    ''' explicit empty state, with the failure message beside it.
    ''' </summary>
    Friend Shared Sub RestoreFolderAfterFailure(previousFolder As String, previousBox As String,
                                                apply As Action(Of String, String))
        If ResourceLocator.IsLocator(previousFolder) Then
            apply("", "")
        Else
            apply(If(previousFolder, ""), If(previousBox, ""))
        End If
    End Sub

End Class
#End If
