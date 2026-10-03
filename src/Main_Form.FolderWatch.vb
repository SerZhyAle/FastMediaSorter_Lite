#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' The live folder list (020_SPECIFICATION_VIEWER_CORE_S9_MODERN_FOLDER.md, U-04). Modern-only.
'
' The list is a snapshot of the folder, so a file that arrives while the folder is open - the
' typical sorter's folder is one that things are still being copied or downloaded into - was
' invisible until the user re-read it by hand. A FileSystemWatcher on the open folder says WHICH
' paths were touched; the viewer then asks the disk what is true of each and edits the list in
' place (FolderLiveList), so a new file takes its place under the sort that is active and the
' file on screen does not move.
'
' Rules this partial keeps:
'   * Events are only a SET of touched paths. They arrive on pool threads, so they go into a
'     concurrent set and ONE debounce timer (500 ms after the first event, not restarted by the
'     next ones, so a long copy cannot postpone the update for ever) drains it on the UI thread.
'   * The asking - one stat per touched path - happens on the pool; the UI thread only edits the
'     list. A share is never asked from the UI thread, and neither is the watcher created there.
'   * Nothing is applied while the list is being changed by something else: a queued file
'     operation, a batch, a folder scan. The batch waits (and is re-armed) rather than racing them.
'   * It degrades, never breaks: a watcher that reports an error (a dropped session, an overflowed
'     buffer - both routine on a share) is dropped, once, with one line in the log, and the viewer
'     is exactly what it was before this existed: a snapshot. A rescan starts a new attempt.
'   * Where it does not run at all: subfolders included (a delete of a directory reports one event
'     for the directory, not its files), archives, remote resources, "-NoBack", the one-file list of
'     an external open (until the folder is read), and the array mode of a folder past the limit.
'   * The list is never touched while it is empty - a folder that was empty when opened has no
'     loaded list, so nothing starts the watcher for it.
Partial Public Class Main_Form

    ''' <summary>First event to the update. Short enough that "I copied three files and they appeared"
    ''' reads as immediate, long enough that a burst of Created/Changed pairs for one file is one stat.</summary>
    Private Const folder_Watch_Debounce_Ms As Integer = 500

    ''' <summary>More distinct touched paths than this in one window is not "a few files arrived" but a
    ''' flood, and the honest answer is the old behaviour: stop watching and say so once.</summary>
    Private Const folder_Watch_Touched_Cap As Integer = 20000

    ''' <summary>Above this many files a size/time sort does not keep the table of file facts a
    ''' newcomer is placed against (a hundred bytes a file); newcomers then go to the end.</summary>
    Private Const folder_Watch_Facts_Cap As Integer = 500000

    Private folder_Watcher As FileSystemWatcher
    ''' <summary>The folder the watcher is on, as a full path - the form every event path is built from.</summary>
    Private folder_Watch_Folder As String = ""
    ''' <summary>Moved by every start and stop, so a watcher still being created on the pool for a
    ''' folder the user has left is thrown away when it arrives.</summary>
    Private folder_Watch_Ticket As Integer
    ''' <summary>Set once the watcher has failed for the folder on screen.</summary>
    Private folder_Watch_Degraded As Boolean
    Private folder_Watch_Applying As Boolean
    Private folder_Watch_Kick_Pending As Integer
    Private folder_Watch_Overflow As Integer
    Private folder_Watch_Timer As System.Windows.Forms.Timer
    Private ReadOnly folder_Watch_Touched As New ConcurrentDictionary(Of String, Byte)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Size and time of every listed file, kept ONLY for a size/time sort - the one case a
    ''' newcomer cannot be placed from its name. Filled by GetFiles, adopted when the list is.</summary>
    Private list_Sort_Facts As Dictionary(Of String, LiveFileFact)
    Private pending_Sort_Facts As Dictionary(Of String, LiveFileFact)
    Private list_Sort_Kind As LiveSortKind = LiveSortKind.Unordered
    Private pending_Sort_Kind As LiveSortKind = LiveSortKind.Unordered

    ''' <summary>
    ''' A fresh list has just been accepted for <c>Current_Folder_Path</c> (MarkFolderListLoaded):
    ''' adopt the facts its scan collected and put the watcher on that folder. A watcher that is
    ''' already on it and healthy stays - the events it holds are about a list that is gone, but
    ''' they are reconciled against the disk, so a stale one is harmless.
    ''' </summary>
    Private Sub SyncFolderWatcher()
        list_Sort_Facts = pending_Sort_Facts
        list_Sort_Kind = pending_Sort_Kind
        pending_Sort_Facts = Nothing

        Dim full As String = FolderToWatch()
        If full.Length = 0 Then
            StopFolderWatcher()
            Return
        End If

        If folder_Watcher IsNot Nothing AndAlso Not folder_Watch_Degraded AndAlso
           String.Equals(folder_Watch_Folder, full, StringComparison.OrdinalIgnoreCase) Then
            Return
        End If

        StopFolderWatcher()
        folder_Watch_Folder = full
        folder_Watch_Degraded = False
        folder_Watch_Overflow = 0
        StartFolderWatcherAsync(full, Interlocked.Increment(folder_Watch_Ticket))
    End Sub

    ''' <summary>The full path of the folder worth watching, or "" when there is none: see the rules above.</summary>
    Private Function FolderToWatch() As String
        If String.IsNullOrEmpty(Current_Folder_Path) Then Return ""
        If Is_No_Background_Tasks Then Return ""
        If is_Files_Array_Active Then Return ""
        If archive_Session IsNot Nothing OrElse ActiveResourceSession() IsNot Nothing Then Return ""
        If modern_Preferences IsNot Nothing AndAlso modern_Preferences.IncludeSubfolders Then Return ""
        Try
            Return Path.GetFullPath(Current_Folder_Path)
        Catch ex As Exception
            Return ""
        End Try
    End Function

    ''' <summary>The watcher is created on the pool: opening the directory handle is a round trip to the
    ''' share, and a share that is asleep must not hold the window for it.</summary>
    Private Async Sub StartFolderWatcherAsync(full As String, ticket As Integer)
        Dim created As FileSystemWatcher = Nothing
        Dim failure As Exception = Nothing
        Try
            created = Await Task.Run(Function() BuildFolderWatcher(full))
        Catch ex As Exception
            failure = ex
        End Try

        If ticket <> folder_Watch_Ticket OrElse IsDisposed OrElse Disposing Then
            DisposeWatcher(created)
            Return
        End If
        If failure IsNot Nothing Then
            DegradeFolderWatch("could not start: " & FailureText.Describe(failure))
            Return
        End If
        folder_Watcher = created
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1111: folder watcher started on " & full)
    End Sub

    ''' <summary>Pool thread. Handlers first, then the switch - an event must never find nobody home.</summary>
    Private Function BuildFolderWatcher(full As String) As FileSystemWatcher
        Dim watcher As New FileSystemWatcher(full) With {
            .IncludeSubdirectories = False,
            .NotifyFilter = NotifyFilters.FileName Or NotifyFilters.LastWrite Or NotifyFilters.Size,
            .InternalBufferSize = 65536
        }
        AddHandler watcher.Created, AddressOf OnFolderWatchEvent
        AddHandler watcher.Changed, AddressOf OnFolderWatchEvent
        AddHandler watcher.Deleted, AddressOf OnFolderWatchEvent
        AddHandler watcher.Renamed, AddressOf OnFolderWatchRenamed
        AddHandler watcher.Error, AddressOf OnFolderWatchError
        watcher.EnableRaisingEvents = True
        Return watcher
    End Function

    Private Shared Sub DisposeWatcher(watcher As FileSystemWatcher)
        If watcher Is Nothing Then Return
        Try
            watcher.EnableRaisingEvents = False
            watcher.Dispose()
        Catch ex As Exception
            ' Already gone - nothing left to stop.
        End Try
    End Sub

    ''' <summary>Stops watching and forgets everything the watcher told us. Safe at any time, in any state.</summary>
    Private Sub StopFolderWatcher()
        Interlocked.Increment(folder_Watch_Ticket)
        Dim watcher As FileSystemWatcher = folder_Watcher
        folder_Watcher = Nothing
        DisposeWatcher(watcher)
        folder_Watch_Folder = ""
        folder_Watch_Touched.Clear()
        folder_Watch_Overflow = 0
        If folder_Watch_Timer IsNot Nothing Then folder_Watch_Timer.Stop()
    End Sub

    ''' <summary>The watcher failed for the folder on screen: back to the snapshot the viewer always
    ''' was, with one line in the log. The next time a list is accepted for a folder, it tries again.</summary>
    Private Sub DegradeFolderWatch(reason As String)
        Dim folder As String = folder_Watch_Folder
        Dim already As Boolean = folder_Watch_Degraded
        StopFolderWatcher()
        folder_Watch_Folder = folder
        folder_Watch_Degraded = True
        If Not already Then AppFileLogger.WriteLine("Live folder updates stopped (" & reason & "): " & folder)
    End Sub

    ' ------------------------------------------------------------------ pool-thread side ----

    Private Sub OnFolderWatchEvent(sender As Object, e As FileSystemEventArgs)
        NoteTouched(sender, e.Name)
    End Sub

    Private Sub OnFolderWatchRenamed(sender As Object, e As RenamedEventArgs)
        NoteTouched(sender, e.OldName)
        NoteTouched(sender, e.Name)
    End Sub

    Private Sub NoteTouched(sender As Object, name As String)
        Dim watcher As FileSystemWatcher = TryCast(sender, FileSystemWatcher)
        If watcher Is Nothing OrElse String.IsNullOrEmpty(name) Then Return
        If folder_Watch_Touched.Count >= folder_Watch_Touched_Cap Then
            Interlocked.Exchange(folder_Watch_Overflow, 1)
        Else
            folder_Watch_Touched.TryAdd(Path.Combine(watcher.Path, name), 0)
        End If
        KickFolderWatch()
    End Sub

    Private Sub OnFolderWatchError(sender As Object, e As ErrorEventArgs)
        Dim reason As String = If(e.GetException()?.Message, "error")
        Try
            BeginInvoke(New Action(Sub() DegradeFolderWatch(reason)))
        Catch ex As Exception
            ' The window is closing; there is nothing left to degrade.
        End Try
    End Sub

    ''' <summary>One marshalled call per burst, not one per event: a copy of a thousand files raises
    ''' thousands of them and the UI thread has no use for the repeats.</summary>
    Private Sub KickFolderWatch()
        If Interlocked.Exchange(folder_Watch_Kick_Pending, 1) <> 0 Then Return
        Try
            BeginInvoke(New Action(AddressOf ArmFolderWatchTimer))
        Catch ex As Exception
            Interlocked.Exchange(folder_Watch_Kick_Pending, 0)
        End Try
    End Sub

    ' ------------------------------------------------------------------------- UI side ----

    Private Sub ArmFolderWatchTimer()
        Interlocked.Exchange(folder_Watch_Kick_Pending, 0)
        If folder_Watch_Timer Is Nothing Then
            folder_Watch_Timer = New System.Windows.Forms.Timer With {.Interval = folder_Watch_Debounce_Ms}
            AddHandler folder_Watch_Timer.Tick, AddressOf OnFolderWatchTick
        End If
        ' Not restarted when it is already counting: the first event sets the deadline.
        If Not folder_Watch_Timer.Enabled Then folder_Watch_Timer.Start()
    End Sub

    Private Sub OnFolderWatchTick(sender As Object, e As EventArgs)
        folder_Watch_Timer.Stop()
        If IsDisposed OrElse Disposing Then Return
        If folder_Watcher Is Nothing Then Return

        ' The list on screen is not (or no longer) the scan of the watched folder: another folder, a
        ' one-file list of an external open, an emptied folder, the array mode. Nothing to edit.
        If Not WatchedListIsOnScreen() Then
            StopFolderWatcher()
            Return
        End If

        If Interlocked.Exchange(folder_Watch_Overflow, 0) <> 0 Then
            DegradeFolderWatch("too many changes at once")
            Return
        End If

        ' Something else is changing the list, or about to: the update waits for it.
        If folder_Watch_Applying OrElse
           (file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.PendingCount > 0) Then
            If Not folder_Watch_Touched.IsEmpty Then folder_Watch_Timer.Start()
            Return
        End If

        ApplyFolderChangesAsync()
    End Sub

    Private Function WatchedListIsOnScreen() As Boolean
        If was_External_Input_Previously OrElse is_Files_Array_Active OrElse files_List Is Nothing Then Return False
        If Not IsFolderListLoaded() Then Return False
        Dim full As String = FolderToWatch()
        Return full.Length > 0 AndAlso String.Equals(full, folder_Watch_Folder, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Async Sub ApplyFolderChangesAsync()
        Dim touched As List(Of String) = DrainTouchedPaths()
        If touched.Count = 0 Then Return

        Dim folder As String = folder_Watch_Folder
        Dim list_Stamp As Integer = file_List_Generation
        Dim ticket As Integer = folder_Watch_Ticket
        Dim supported As String() = all_Supported_Extensions.ToArray()
        Dim present As New List(Of LiveFileFact)()
        Dim absent As New List(Of String)()
        Dim verdict_Valid As Boolean = True

        folder_Watch_Applying = True
        Try
            Await Task.Run(
                Sub()
                    FolderLiveList.Classify(touched, supported, present, absent)
                    ' A share that has dropped answers "not found" for every file in it. Believing
                    ' that would empty the list; a folder that is itself unreachable is no verdict.
                    If absent.Count > 0 AndAlso Not Directory.Exists(folder) Then verdict_Valid = False
                End Sub)
        Catch ex As Exception
            verdict_Valid = False
            AppFileLogger.LogException("Live folder update: classifying " & folder, ex)
        Finally
            folder_Watch_Applying = False
        End Try

        If IsDisposed OrElse Disposing Then Return

        ' Whatever happened while the disk was being asked decides whether the answers still
        ' describe THIS list: another folder, a rescan, or something that started editing it.
        Dim stale As Boolean = ticket <> folder_Watch_Ticket OrElse list_Stamp <> file_List_Generation OrElse
                               Not WatchedListIsOnScreen() OrElse
                               (file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.PendingCount > 0)
        If stale OrElse Not verdict_Valid Then
            ' Not lost: put back and tried again (a stale list is gone - its paths are harmless to reconcile).
            If ticket = folder_Watch_Ticket AndAlso folder_Watcher IsNot Nothing Then
                For Each path As String In touched
                    folder_Watch_Touched.TryAdd(path, 0)
                Next
                If Not verdict_Valid Then DegradeFolderWatch("folder not answering") Else ArmFolderWatchTimer()
            End If
            Return
        End If

        ApplyLiveChanges(present, absent)
    End Sub

    ''' <summary>Takes the touched set out, keeping only paths that sit directly in the watched folder.</summary>
    Private Function DrainTouchedPaths() As List(Of String)
        Dim taken As New List(Of String)()
        For Each key As String In folder_Watch_Touched.Keys.ToArray()
            Dim dummy As Byte
            If Not folder_Watch_Touched.TryRemove(key, dummy) Then Continue For
            Dim parent As String = Path.GetDirectoryName(key)
            If parent IsNot Nothing AndAlso String.Equals(parent.TrimEnd("\"c), folder_Watch_Folder.TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then
                taken.Add(key)
            End If
        Next
        Return taken
    End Function

    ''' <summary>The edit itself - memory only. Replaces the positions the rest of the window may have
    ''' remembered, so the list generation moves (an operation queued against the old one is told so).</summary>
    Private Sub ApplyLiveChanges(present As List(Of LiveFileFact), absent As List(Of String))
        If present.Count = 0 AndAlso absent.Count = 0 Then Return

        Dim kind As LiveSortKind = list_Sort_Kind
        Dim lookup As Func(Of String, LiveFileFact) = Nothing
        If FolderLiveList.NeedsMetadata(kind) Then
            If list_Sort_Facts Is Nothing Then
                ' No table of sizes and times to place a newcomer against (a folder past the cap):
                ' the honest fallback is the end of the list.
                kind = LiveSortKind.Unordered
            Else
                lookup = Function(path As String)
                             Dim fact As LiveFileFact = Nothing
                             Return If(list_Sort_Facts.TryGetValue(path, fact), fact, Nothing)
                         End Function
            End If
        End If

        Dim shown As String = Current_File_Name
        Dim change As LiveListChange = FolderLiveList.Apply(files_List, current_File_Index, present, absent, kind, lookup)
        If change.Added = 0 AndAlso change.Removed = 0 Then Return

        If list_Sort_Facts IsNot Nothing Then
            For Each path As String In absent
                If Not String.Equals(path, shown, StringComparison.OrdinalIgnoreCase) Then list_Sort_Facts.Remove(path)
            Next
            For Each fact As LiveFileFact In present
                list_Sort_Facts(fact.Path) = fact
            Next
        End If

        current_File_Index = change.CurrentIndex
        total_File_Count = files_List.Count

        ' The index followed the shifts; this only guards against a list that was already out of
        ' step with the screen before the batch came in.
        If Not String.IsNullOrEmpty(shown) AndAlso
           (current_File_Index < 0 OrElse current_File_Index >= files_List.Count OrElse
            Not String.Equals(files_List(current_File_Index), shown, StringComparison.OrdinalIgnoreCase)) Then
            Dim at As Integer = files_List.FindIndex(Function(item) String.Equals(item, shown, StringComparison.OrdinalIgnoreCase))
            If at >= 0 Then current_File_Index = at
        End If

        NoteFileListReplaced()
        ResetShuffleCycle()
        If total_File_Count > 0 Then
            lbl_File_Number.Text = Localization.TF("{0} из {1}", current_File_Index + 1, total_File_Count)
        End If
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1112: live folder list +" & change.Added.ToString() &
                        " -" & change.Removed.ToString() & ", now " & total_File_Count.ToString())
    End Sub

End Class
#End If
