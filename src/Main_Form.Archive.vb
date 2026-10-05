#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

' The viewer's side of archive browsing (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md).
'
' An archive is shown as a folder: the file list is filled with paths inside the session's
' temporary directory, and each file appears there the moment something is about to show
' it. Everything downstream - the decoder, the prefetch, zoom, the perspective background,
' OCR, LibVLC - keeps working on plain paths and never learns that an archive is involved.
'
' What this partial owns: entering and leaving, the entry list, putting an entry on disk,
' and the single point that says "not inside an archive" for file operations (§7,
' invariant 6). What it deliberately does NOT own: the cache directory itself
' (ArchiveTempStore) and the reading (ArchiveSession).
'
' Modern-only: on Windows 7/8.1 a dropped .zip still answers "Unsupported format".
Partial Public Class Main_Form

    Private archive_Session As ArchiveSession

    ''' <summary>
    ''' The folder the archive itself lives in. Two jobs: it is where "close the archive"
    ''' returns to, and it is what gets written to the registry instead of the archive path
    ''' - invariant 5 says no temporary path and no container path may outlive the session,
    ''' because the next start would open something that is not a folder.
    ''' </summary>
    Private archive_Return_Folder As String = ""

    ''' <summary>Ceiling on how many entries become files (§5.3, §9) - the settings
    ''' default, overridden by ModernViewerPreferences.ArchiveMaxEntries.</summary>
    Private Const Archive_Max_Entries As Integer = 20000

    ''' <summary>The broad archive admission set. Navigation toggles are intentionally
    ''' not part of this decision: ArchiveFileEntries applies them later, allowing a
    ''' disabled kind to be re-enabled in the current archive without reopening it.</summary>
    Private Function ArchiveSupportsExtension(extension As String) As Boolean
        Return Image_File_Extensions.Contains(extension) OrElse
               video_File_Extensions.Contains(extension) OrElse
               web_specific_image_extensions.Contains(extension) OrElse
               GetModernPreferences().DocumentExtensionSet().Contains(extension) OrElse
               GetModernPreferences().OtherExtensionSet().Contains(extension)
    End Function

    Friend Function IsArchiveMode() As Boolean
        Return archive_Session IsNot Nothing
    End Function

    ''' <summary>The open archive's own path, or "" - what "copy the path" hands out inside
    ''' an archive instead of a temporary copy (G-17).</summary>
    Friend Function OpenArchivePath() As String
        If archive_Session Is Nothing Then Return ""
        Return archive_Session.ArchiveFilePath
    End Function

    ' The refusal of a file operation inside an archive (§7, invariant 6) is no longer
    ' here: it is one branch of ResourceBlocksFileOperations (Main_Form.Resource.vb), the
    ' single refusal point that answers for archives and read-only resources alike
    ' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-7). v1 of archives is still
    ' view-only by the owner's decision: every verb is refused there, copy included.

    ''' <summary>
    ''' What may be written to the registry as "the current folder". Inside an archive that
    ''' is the folder the archive sits in - the archive path itself would come back on the
    ''' next start as a folder that cannot be listed. Inside a remote resource it is the
    ''' resource locator, which the next start can re-open - never the cache directory.
    ''' </summary>
    Friend Function PersistableFolderPath() As String
        If archive_Session IsNot Nothing Then Return archive_Return_Folder
        Dim resource As ResourceSession = ActiveResourceSession()
        If resource IsNot Nothing Then Return resource.Locator
        Return Current_Folder_Path
    End Function

    ' ------------------------------------------------------------------ entering ----

    ''' <summary>
    ''' Opens an archive as if it were a folder. Every entry point - drag and drop, a
    ''' command line, a second instance - arrives here through ProcessArgument.
    ''' </summary>
    Friend Sub EnterArchive(archivePath As String)
        LeaveArchive()
        SlideShowStop()

        Dim sessionDir As String = ""
        Try
            ' Only now, not at startup: someone who never opens an archive should not pay
            ' for a directory walk (§4.5).
            ArchiveTempStore.SweepOrphans()

            sessionDir = ArchiveTempStore.CreateSession()
            Dim maxEntries As Integer = GetModernPreferences().ArchiveMaxEntries
            ' Keep every built-in media entry in the session. The navigation kind filter
            ' is applied when rows are built, so turning video or audio back on can reveal
            ' entries without reopening the archive.
            Dim session As New ArchiveSession(archivePath, sessionDir,
                                              AddressOf ArchiveSupportsExtension,
                                              maxEntries)

            If session.UnsupportedKind.Length > 0 Then
                ' A TAR/GZip/.. wearing an archive name (audit 052 G-15).
                session.Dispose()
                ArchiveTempStore.DeleteSession(sessionDir)
                lbl_Status.Text = Localization.TF("Формат архива не поддерживается ({0})", session.UnsupportedKind)
                Return
            End If

            If session.IsEncrypted Then
                session.Dispose()
                ArchiveTempStore.DeleteSession(sessionDir)
                Notify(Localization.T("Архив защищён паролем, а программа такие не открывает. Распакуйте его архиватором и откройте папку."), NotifyLevel.Warning)
                Return
            End If

            If session.Entries.Count = 0 Then
                session.Dispose()
                ArchiveTempStore.DeleteSession(sessionDir)
                lbl_Status.Text = Localization.T("В архиве нет поддерживаемых файлов")
                Return
            End If

            ' A solid 7z/RAR is read in ONE pass (§5.2), and that pass can take seconds to
            ' minutes - so it runs off the UI thread with progress, and the archive opens
            ' when it ends. Everything else opens now.
            If session.NeedsSequentialPass Then
                BeginSequentialOpen(session, sessionDir, archivePath, maxEntries)
                Return
            End If

            CompleteEnterArchive(session, sessionDir, archivePath, maxEntries)
        Catch ex As Exception
            AppFileLogger.LogException("Archive: opening " & archivePath, ex)
            If sessionDir.Length > 0 Then ArchiveTempStore.DeleteSession(sessionDir)
            ' The cause and the next step, from the one place that reads an exception (124 UX-208).
            Notify(Localization.TF("Не удалось открыть архив: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
        End Try
    End Sub

    ''' <summary>The second half of opening: the session is ready (its entries are listable
    ''' and, for a solid archive, stored) - make it THE open archive and show the first
    ''' entry. Runs on the UI thread, straight from <see cref="EnterArchive"/> or, after the
    ''' sequential pass, from <see cref="BeginSequentialOpen"/>.</summary>
    Private Sub CompleteEnterArchive(session As ArchiveSession, sessionDir As String,
                                     archivePath As String, maxEntries As Integer)
        Try
            archive_Session = session
            archive_Return_Folder = If(Path.GetDirectoryName(archivePath), "")

            ' The folder box shows the ARCHIVE, never the temporary directory (§2.2) - the
            ' user is browsing D:\scans\1998.cbz and that is what the address must say.
            Current_Folder_Path = archivePath
            is_TextBox_Editing = True
            cmbox_Media_Folder.Text = archivePath
            is_TextBox_Editing = False

            current_File_Index = 0
            Current_File_Name = ""
            Current_Image_Path = ""
            is_External_Input_Received = False
            was_External_Input_Previously = False

            ApplyArchiveModeUi()
            ReadShowMediaFile(Mode_FolderAndFile)

            Dim opened As String = Localization.TF("Архив: {0} файлов", session.Entries.Count.ToString())
            If session.WasTruncated Then
                opened &= "  " & Localization.TF("Показаны первые {0} записей", maxEntries.ToString())
            End If
            lbl_Status.Text = opened
            If IsArchiveEntryRefused(Current_File_Name) Then
                lbl_Status.Text = archive_Refused_Text & "  " & opened
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Archive: opening " & archivePath, ex)
            Dim failed As ArchiveSession = archive_Session
            archive_Session = Nothing
            If failed IsNot Nothing Then failed.Dispose() Else session.Dispose()
            If sessionDir.Length > 0 Then ArchiveTempStore.DeleteSession(sessionDir)
            ApplyArchiveModeUi()
            Notify(Localization.TF("Не удалось открыть архив: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
        End Try
    End Sub

    ' ------------------------------------------------- the sequential pass (Ф2) ----

    ''' <summary>The solid archive being unpacked right now, before it becomes the open one.
    ''' Held here (not in a local) so that ANY change of context can reach it and stop it -
    ''' <see cref="LeaveArchive"/> does, which is how opening something else, closing the
    ''' window and Esc all cancel the pass without each knowing about it.</summary>
    Private archive_Pending_Session As ArchiveSession
    Private archive_Pending_Dir As String = ""
    Private archive_Pending_Path As String = ""

    ''' <summary>Bumped whenever a pending open is cancelled or replaced. A pass that ends
    ''' after that finds its number stale and drops its result - it was already cleaned up by
    ''' whoever cancelled it.</summary>
    Private archive_Open_Generation As Integer

    ''' <summary>True while a solid archive is being unpacked and is not yet the open one.
    ''' File operations are refused in that window and the keyboard is held (Esc cancels):
    ''' the picture on screen still belongs to the PREVIOUS folder, and a key meant for the
    ''' archive must not act on it.</summary>
    Friend Function IsArchiveOpenPending() As Boolean
        Return archive_Pending_Session IsNot Nothing
    End Function

    Private Async Sub BeginSequentialOpen(session As ArchiveSession, sessionDir As String,
                                          archivePath As String, maxEntries As Integer)
        ' Async Sub: nothing may escape it, or an exception here is an unhandled crash.
        Dim generation As Integer = Threading.Interlocked.Increment(archive_Open_Generation)
        Try
            archive_Pending_Session = session
            archive_Pending_Dir = sessionDir
            archive_Pending_Path = archivePath
            lbl_Status.Text = Localization.TF("Распаковываю архив: {0} %", 0.ToString())

            Dim prefs As ModernViewerPreferences = GetModernPreferences()
            Dim maxEntryBytes As Long = CLng(prefs.ArchiveMaxEntryMb) * 1024L * 1024L
            Dim maxCacheBytes As Long = CLng(prefs.ArchiveCacheMaxMb) * 1024L * 1024L

            Dim outcome As ArchiveSession.PassOutcome = ArchiveSession.PassOutcome.Failed
            Try
                outcome = Await Task.Run(
                    Function() session.RunSequentialPass(maxEntryBytes, maxCacheBytes,
                        Sub(percent As Integer) PostArchivePassProgress(generation, percent)))
            Catch ex As Exception
                AppFileLogger.LogException("Archive: sequential pass of " & archivePath, ex)
            End Try

            ' Back on the UI thread. A newer open (or Leave) has already disposed this session
            ' and removed its directory.
            If generation <> Threading.Volatile.Read(archive_Open_Generation) Then Return

            archive_Pending_Session = Nothing
            archive_Pending_Dir = ""
            archive_Pending_Path = ""

            If outcome = ArchiveSession.PassOutcome.Completed Then
                CompleteEnterArchive(session, sessionDir, archivePath, maxEntries)
            Else
                session.Dispose()
                ArchiveTempStore.DeleteSession(sessionDir)
                Notify(Localization.T("Архив не распакован до конца: он повреждён или неполон. Попробуйте открыть его архиватором."), NotifyLevel.Warning)
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Archive: opening " & archivePath, ex)
            If generation = Threading.Volatile.Read(archive_Open_Generation) Then
                archive_Pending_Session = Nothing
                archive_Pending_Dir = ""
                archive_Pending_Path = ""
                Try : session.Dispose() : Catch : End Try
                ArchiveTempStore.DeleteSession(sessionDir)
                Notify(Localization.TF("Не удалось открыть архив: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
            End If
        End Try
    End Sub

    ''' <summary>Progress arrives on the pass's thread; the label belongs to the UI thread.
    ''' Posted, never invoked: the pass must not wait for the window.</summary>
    Private Sub PostArchivePassProgress(generation As Integer, percent As Integer)
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Return
            BeginInvoke(New MethodInvoker(
                Sub()
                    If generation <> Threading.Volatile.Read(archive_Open_Generation) Then Return
                    lbl_Status.Text = Localization.TF("Распаковываю архив: {0} %", percent.ToString())
                End Sub))
        Catch ex As Exception
            ' The window is going away; the pass is about to be cancelled with it.
            Debug.WriteLine("Archive: progress post dropped - " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Stops a solid archive that is still being unpacked and removes what it had stored.
    ''' Safe when nothing is pending. <paramref name="announce"/> says so on the status line -
    ''' wanted for the user's own Esc, not when the pass is dropped because something else
    ''' was opened (that has its own status).
    ''' </summary>
    Friend Sub CancelPendingArchiveOpen(Optional announce As Boolean = False)
        Dim session As ArchiveSession = archive_Pending_Session
        If session Is Nothing Then Return

        Dim sessionDir As String = archive_Pending_Dir
        Threading.Interlocked.Increment(archive_Open_Generation)
        archive_Pending_Session = Nothing
        archive_Pending_Dir = ""
        archive_Pending_Path = ""

        Try
            session.Dispose()   ' waits for at most one entry's write, then nothing more is written
        Catch ex As Exception
            AppFileLogger.LogException("Archive: cancelling the pending open", ex)
        End Try
        ArchiveTempStore.DeleteSession(sessionDir)
        If announce Then lbl_Status.Text = Localization.T("Открытие архива отменено")
    End Sub

    ' ------------------------------------------------ what archive mode changes ----

    ''' <summary>
    ''' The parts of the window that follow "an archive is open" (§2.2, §7): the buttons that
    ''' only make sense for real files are hidden through the toolbar's own mechanism (a bare
    ''' Visible = False would be undone by the next layout), and the recipient list is redrawn
    ''' muted. Called on entering AND leaving, so the two can never drift apart.
    ''' </summary>
    Private Sub ApplyArchiveModeUi()
        Try
            Dim inArchive As Boolean = IsArchiveMode()
            SetToolbarItemHidden(btn_Rename, inArchive)
            SetToolbarItemHidden(bt_Delete, inArchive)
            ' The «Ресурс» button hides with the folder menu's resource entries (ticket 118).
            RefreshResourceButton()
            LayoutToolbar()
            ApplyRecipientsOverlayHealth()
        Catch ex As Exception
            ' Leaving an archive is also what closing the window does; a control that is
            ' already gone must not turn that into a failure.
            AppFileLogger.LogException("Archive: refreshing the archive-mode UI", ex)
        End Try
    End Sub

    ' ------------------------------------------------------------------- leaving ----

    ''' <summary>
    ''' Closes the archive and takes its temporary directory with it (§4.3, invariant 4).
    ''' Called before ANY change of context - another archive, a folder, a file, shutdown -
    ''' and safe to call when no archive is open.
    '''
    ''' Order matters: playback and the picture boxes hold the extracted file open (VLC
    ''' locks what it plays, and a decoded image keeps its MemoryStream), so they are let
    ''' go before the directory is deleted. Whatever still refuses to go is left to the
    ''' orphan sweep rather than reported - the disk gets clean either way.
    ''' </summary>
    Friend Sub LeaveArchive()
        ' A solid archive still being unpacked is not "open" yet, but it is on its way to
        ' being so: any change of context cancels it first (Esc, another file, closing).
        CancelPendingArchiveOpen()
        If archive_Session Is Nothing Then Return

        Dim sessionDir As String = archive_Session.TempRoot
        Try
            SlideShowStop()
            ReleaseActiveMedia()
        Catch ex As Exception
            AppFileLogger.LogException("Archive: releasing media before leaving", ex)
        End Try

        Try
            archive_Session.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("Archive: closing the session", ex)
        End Try
        archive_Session = Nothing
        archive_Return_Folder = ""
        archive_Refused_Path = ""

        ArchiveTempStore.DeleteSession(sessionDir)
        ApplyArchiveModeUi()
    End Sub

    ''' <summary>
    ''' Leaves the open archive unless <paramref name="typedPath"/> names that very archive
    ''' (the folder box shows the archive path, so Enter on it unchanged is "stay"). The
    ''' folder box's Enter is the one change of folder that did not leave (audit 052
    ''' A-13/G-06): the typed folder was then listed with the ARCHIVE's pages in it.
    ''' </summary>
    Friend Sub LeaveArchiveUnlessShowing(typedPath As String)
        Dim current As String = If(archive_Session IsNot Nothing, archive_Session.ArchiveFilePath, archive_Pending_Path)
        If String.IsNullOrEmpty(current) Then Return
        If String.Equals(If(typedPath, "").Trim(), current, StringComparison.OrdinalIgnoreCase) Then Return
        LeaveArchive()
    End Sub

    ' ------------------------------------------------------------- the file list ----

    ''' <summary>
    ''' The open archive's entries as file-list rows, or Nothing when this is an ordinary
    ''' folder. Built from the archive's own metadata - never by walking the temporary
    ''' directory, which holds only what has been looked at so far (invariant 10).
    '''
    ''' The name carried into the sort is the entry's FULL name inside the archive, so
    ''' "chapter2/page01" sorts after "chapter1/page09" the way a reader expects.
    ''' </summary>
    Private Function ArchiveFileEntries() As List(Of FileEntry)
        If archive_Session Is Nothing Then Return Nothing

        Dim rows As New List(Of FileEntry)(archive_Session.Entries.Count)
        For Each entry As ArchiveEntryInfo In archive_Session.Entries
            If Not MediaBrowsing.IsEligible(entry.EntryName, all_Supported_Extensions) Then Continue For
            rows.Add(New FileEntry With {
                .FilePath = entry.TempPath,
                .FileSize = entry.Size,
                .FileName = entry.EntryName,
                .FileDate = entry.LastWrite
            })
        Next
        Return rows
    End Function

    ''' <summary>
    ''' Puts the entry behind <paramref name="filePath"/> on disk, if that path belongs to
    ''' the open archive. A no-op for an ordinary file, so callers do not have to ask
    ''' whether an archive is open.
    '''
    ''' This must run BEFORE anything checks whether the file exists: inside an archive it
    ''' does not exist until we put it there, and the display path treats a missing file as
    ''' a stale list entry and drops it.
    ''' </summary>
    Friend Sub EnsureArchiveEntryOnDisk(filePath As String)
        If archive_Session Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return

        Dim index As Integer = archive_Session.IndexOfTempPath(filePath)
        If index < 0 Then Return

        ' Sorting or shuffling makes "the entry index next to this one" something other than
        ' the page next to it on screen - so the rows actually shown and their navigation
        ' neighbours are pinned against the budget eviction a prefetch may trigger.
        Dim protectedPaths As New List(Of String) From {filePath}
        Dim navigation As IList(Of String) = If(is_Files_Array_Active,
            DirectCast(files_Array, IList(Of String)), files_List)
        If navigation IsNot Nothing AndAlso navigation.Count > 0 Then
            Dim row As Integer = navigation.IndexOf(filePath)
            If row >= 0 Then
                protectedPaths.Add(navigation((row + navigation.Count - 1) Mod navigation.Count))
                protectedPaths.Add(navigation((row + 1) Mod navigation.Count))
            End If
        End If
        archive_Session.ProtectDisplayedEntries(protectedPaths)

        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        Dim maxEntryBytes As Long = CLng(prefs.ArchiveMaxEntryMb) * 1024L * 1024L
        Dim maxCacheBytes As Long = CLng(prefs.ArchiveCacheMaxMb) * 1024L * 1024L

        Dim refusal As ArchiveSession.EntryRefusal
        If archive_Session.TryEnsureExtracted(index, refusal, maxEntryBytes, maxCacheBytes) Then
            If String.Equals(archive_Refused_Path, filePath, StringComparison.OrdinalIgnoreCase) Then archive_Refused_Path = ""
            Return
        End If

        archive_Refused_Path = filePath
        archive_Refused_Text = ArchiveRefusalText(refusal, archive_Session.Entries(index))
        AppFileLogger.WriteLine("Archive: " & archive_Session.Entries(index).EntryName & " - " & archive_Refused_Text)
        lbl_Status.Text = archive_Refused_Text
    End Sub

    ''' <summary>The entry the last extraction refused, and why - so the display path can
    ''' keep the entry and its reason instead of taking a refusal for a vanished file
    ''' (audit 052 A-12/G-05).</summary>
    Private archive_Refused_Path As String = ""
    Private archive_Refused_Text As String = ""

    ''' <summary>
    ''' Was <paramref name="filePath"/> refused by the archive a moment ago? Then it is not a
    ''' stale list entry: it stays in the list, N/total stays what it was, and the refusal
    ''' stays on the status line (010 invariant 9). Before this, the display path saw only
    ''' "the file does not exist", dropped the entry with "file not found", and took the
    ''' next entry WITHOUT extracting it - so it dropped that one too.
    ''' </summary>
    Friend Function IsArchiveEntryRefused(filePath As String) As Boolean
        If archive_Session Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return False
        If Not String.Equals(archive_Refused_Path, filePath, StringComparison.OrdinalIgnoreCase) Then Return False
        Return Not File.Exists(filePath)
    End Function

    ''' <summary>A refused entry on screen: the blank surface an unsupported file gets, with
    ''' the refusal - not "unsupported format" - on the status line.</summary>
    Friend Sub ShowArchiveRefusal(filePath As String)
        ShowUnsupportedFormat(filePath)
        lbl_Status.Text = archive_Refused_Text
    End Sub

    ''' <summary>Why an entry did not appear, in words (§10, invariant 9).</summary>
    Private Shared Function ArchiveRefusalText(refusal As ArchiveSession.EntryRefusal,
                                               entry As ArchiveEntryInfo) As String
        Select Case refusal
            Case ArchiveSession.EntryRefusal.TooLarge
                Return Localization.TF("Запись слишком большая для просмотра ({0} МБ)",
                                       (entry.Size \ (1024L * 1024L)).ToString())
            Case ArchiveSession.EntryRefusal.Bomb
                Return Localization.T("Запись сжата слишком сильно, чтобы открыть её безопасно. Распакуйте архив архиватором и проверьте файл.")
            Case ArchiveSession.EntryRefusal.Encrypted
                Return Localization.T("Архив защищён паролем, а программа такие не открывает. Распакуйте его архиватором и откройте папку.")
            Case Else
                Return Localization.T("Не удалось распаковать запись")
        End Select
    End Function

    ''' <summary>
    ''' What to call this file on screen. Inside an archive that is the entry's own name
    ''' relative to the archive root ("1998/03/foto12.jpg") - the temporary path is never
    ''' shown to the user, anywhere (§2.2).
    ''' </summary>
    Friend Function ArchiveDisplayName(filePath As String) As String
        If archive_Session Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return ""
        Dim index As Integer = archive_Session.IndexOfTempPath(filePath)
        If index < 0 Then Return ""
        Return archive_Session.Entries(index).EntryName
    End Function

    ' ---------------------------------------------------------- entry points (Ф4) ----

    ''' <summary>
    ''' The folder box menu's "Open archive.." entry (§2.1 point 4, §12 Ф4). Filtered to
    ''' what the viewer can actually open today (ArchiveEntryFilter.Openable_Extensions) -
    ''' ZIP/CBZ, 7z and RAR/CBR since Ф2. Delegates to
    ''' ProcessArgument, the one ingress for a concrete path (invariant already relied on
    ''' by Choose_file()): it already recognizes an archive extension and routes to
    ''' EnterArchive, so picking one here is exactly like picking a picture.
    ''' </summary>
    Friend Sub OpenArchiveViaDialog()
        SlideShowStop()
        Using dialog As New OpenFileDialog()
            Dim archiveExtensions As String = String.Join(";",
                ArchiveEntryFilter.Openable_Extensions.Select(Function(ext) "*" & ext))
            dialog.Filter = Localization.T("Архивы") & "|" & archiveExtensions
            ' Inside an archive the "folder" is the archive file itself - start next to it.
            Dim folder As String = If(IsArchiveMode(), archive_Return_Folder, Current_Folder_Path)
            dialog.InitialDirectory = If(String.IsNullOrEmpty(folder),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), folder)
            dialog.Title = Localization.T("Открыть архив..")
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                ProcessArgument(dialog.FileName)
            End If
        End Using
    End Sub

    ''' <summary>
    ''' The folder box menu's "Close archive" entry (§2.3, §12 Ф4). Returns to the folder
    ''' the archive itself lives in - the same "open a folder" path SelectFolderViaDialog
    ''' uses.
    '''
    ''' Simplification worth naming: §2.3 also asks for the archive to be left selected as
    ''' the current entry. The file list here is always built from
    ''' all_Supported_Extensions, and an archive is deliberately never a member of that set
    ''' (§1) - there is no row in an ordinary folder listing to put a cursor on without
    ''' teaching the folder view about a file kind it otherwise never shows. Returning to
    ''' the right folder is the part every acceptance case (§13.2) actually checks.
    ''' </summary>
    Friend Sub CloseArchiveAndReturn()
        If archive_Session Is Nothing Then Return
        Dim returnFolder As String = archive_Return_Folder
        LeaveArchive()
        If String.IsNullOrEmpty(returnFolder) Then Return

        Current_Folder_Path = returnFolder
        lbl_Status.Text = Localization.T("Архив закрыт")
        Is_No_Background_Tasks = False
        ReadShowMediaFile(Mode_FolderAndFile)
    End Sub

End Class
#End If
