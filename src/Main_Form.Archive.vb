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
               web_specific_image_extensions.Contains(extension)
    End Function

    Friend Function IsArchiveMode() As Boolean
        Return archive_Session IsNot Nothing
    End Function

    ''' <summary>
    ''' The one place that refuses a file operation inside an archive (§7, invariant 6),
    ''' and says so - a key that silently does nothing reads as a broken program. Returns
    ''' True when the caller must stop.
    '''
    ''' v1 is view-only by the owner's decision: the archive is never modified, and
    ''' "extract this entry into a recipient folder" is a separate feature (§14, О-1),
    ''' not a side effect of browsing.
    ''' </summary>
    Friend Function ArchiveModeBlocksFileOperations() As Boolean
        If archive_Session Is Nothing Then Return False
        lbl_Status.Text = Localization.T("В архиве файловые операции недоступны")
        Return True
    End Function

    ''' <summary>
    ''' What may be written to the registry as "the current folder". Inside an archive that
    ''' is the folder the archive sits in - the archive path itself would come back on the
    ''' next start as a folder that cannot be listed.
    ''' </summary>
    Friend Function PersistableFolderPath() As String
        If archive_Session Is Nothing Then Return Current_Folder_Path
        Return archive_Return_Folder
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

            If session.IsEncrypted Then
                session.Dispose()
                ArchiveTempStore.DeleteSession(sessionDir)
                lbl_Status.Text = Localization.T("Архив защищён паролем - откройте его архиватором")
                Return
            End If

            If session.Entries.Count = 0 Then
                session.Dispose()
                ArchiveTempStore.DeleteSession(sessionDir)
                lbl_Status.Text = Localization.T("В архиве нет поддерживаемых файлов")
                Return
            End If

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

            ReadShowMediaFile(Mode_FolderAndFile)

            Dim opened As String = Localization.TF("Архив: {0} файлов", session.Entries.Count.ToString())
            If session.WasTruncated Then
                opened &= "  " & Localization.TF("Показаны первые {0} записей", maxEntries.ToString())
            End If
            lbl_Status.Text = opened
        Catch ex As Exception
            AppFileLogger.LogException("Archive: opening " & archivePath, ex)
            Dim failed As ArchiveSession = archive_Session
            archive_Session = Nothing
            If failed IsNot Nothing Then failed.Dispose()
            If sessionDir.Length > 0 Then ArchiveTempStore.DeleteSession(sessionDir)
            lbl_Status.Text = Localization.T("Не удалось прочитать архив")
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

        ArchiveTempStore.DeleteSession(sessionDir)
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
            If Not all_Supported_Extensions.Contains(Path.GetExtension(entry.EntryName).ToLowerInvariant()) Then Continue For
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

        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        Dim maxEntryBytes As Long = CLng(prefs.ArchiveMaxEntryMb) * 1024L * 1024L
        Dim maxCacheBytes As Long = CLng(prefs.ArchiveCacheMaxMb) * 1024L * 1024L

        Dim refusal As ArchiveSession.EntryRefusal
        If archive_Session.TryEnsureExtracted(index, refusal, maxEntryBytes, maxCacheBytes) Then Return

        lbl_Status.Text = ArchiveRefusalText(refusal, archive_Session.Entries(index))
    End Sub

    ''' <summary>Why an entry did not appear, in words (§10, invariant 9).</summary>
    Private Shared Function ArchiveRefusalText(refusal As ArchiveSession.EntryRefusal,
                                               entry As ArchiveEntryInfo) As String
        Select Case refusal
            Case ArchiveSession.EntryRefusal.TooLarge
                Return Localization.TF("Запись слишком большая для просмотра ({0} МБ)",
                                       (entry.Size \ (1024L * 1024L)).ToString())
            Case ArchiveSession.EntryRefusal.Bomb
                Return Localization.T("Похоже на архив-бомбу")
            Case ArchiveSession.EntryRefusal.Encrypted
                Return Localization.T("Архив защищён паролем - откройте его архиватором")
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
    ''' 7z/RAR/CBR are containers the viewer already recognizes but cannot read yet (Ф2),
    ''' so offering them in this dialog would be a filter that lies. Delegates to
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
            dialog.InitialDirectory = If(String.IsNullOrEmpty(Current_Folder_Path),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Current_Folder_Path)
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
