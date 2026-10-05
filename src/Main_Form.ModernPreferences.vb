#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text.Json

Partial Public Class Main_Form

    ' The shuffle-without-repeats state, one object for every caller (SlideshowOrder.vb,
    ' audit 048 A-04).
    Private ReadOnly shuffle_Cycle As New ShuffleCycle()

    ''' <summary>Set while the display path or the scale-mode below moves the zoom itself,
    ''' so an automatic fit is never mistaken for the user choosing one and written into
    ''' the per-folder history (§4.2: the remembered value must not be overwritten by the
    ''' very act of restoring it).</summary>
    Private is_Zoom_Applied_By_Program As Boolean

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SystemParametersInfo(action As UInteger, param As UInteger,
                                                 ByRef value As Boolean, winIni As UInteger) As Boolean
    End Function

    Private Const spi_Get_Client_Area_Animation As UInteger = &H1042UI

    ''' <summary>-1 until the first read, then 0 (animations off) or 1 (on). It used to be taken once per process, so
    ''' toggling Windows' "Animation effects" was ignored until the next start while high contrast was polled; now
    ''' any user-preference change from the system throws it away (124 UX-220, 125 UX-335) and the next ask re-reads.</summary>
    Private system_Animations_State As Integer = -1

    ''' <summary>Windows told us a user preference changed (Animation effects among them): forget the cached answer.
    ''' Raised on a system-events thread, so it only stores an integer.</summary>
    Private Sub OnSystemPreferenceChanged(sender As Object, e As Microsoft.Win32.UserPreferenceChangedEventArgs)
        Threading.Volatile.Write(system_Animations_State, -1)
    End Sub

    ' Save() intentionally re-reads this cross-process setting. A failed import write
    ' must retry the explicit value before Save() can re-read the old registry value.
    Private pending_Allow_New_Windows_Persistence As Boolean
    Private last_Applied_Preferences As ModernViewerPreferences

    Private Function RecentFilesLimit() As Integer
        Return If(modern_Preferences Is Nothing, 50, modern_Preferences.RecentFilesLimit)
    End Function

    Friend Function GetModernPreferences() As ModernViewerPreferences
        If modern_Preferences Is Nothing Then modern_Preferences = ModernViewerPreferences.Load()
        If last_Applied_Preferences Is Nothing Then last_Applied_Preferences = modern_Preferences.Snapshot()
        Return modern_Preferences
    End Function

    ''' <summary>Applies an imported profile to the live object. True when it was also written
    ''' to the registry; False when the write failed - the profile IS applied, the unsaved state
    ''' is kept and retried by the settings flush (105 L09), and the caller says so.</summary>
    Friend Function ReplaceModernPreferences(imported As ModernViewerPreferences) As Boolean
        If imported Is Nothing Then Throw New ArgumentNullException(NameOf(imported))
        imported.Normalize()
        ' Into the existing object, never a new reference: the settings window's rows
        ' write into the instance they were built from (053 F-02).
        GetModernPreferences().CopyFrom(imported)
        Is_Video_Loop = False
        pending_Allow_New_Windows_Persistence = True
        Dim persisted As Boolean = True
        Try
            ' Save() alone can never change this one key - it re-reads it first (053 F-18).
            modern_Preferences.PersistAllowNewWindows()
            pending_Allow_New_Windows_Persistence = False
        Catch ex As Exception
            persisted = False
            AppFileLogger.LogException("Settings import: persisting AllowNewWindows", ex)
            MarkSettingsDirty()
        End Try
        If Not ApplyModernPreferencesFromSettings(persistImmediately:=True) Then persisted = False
        Return persisted
    End Function

    ''' <summary>Applies changed domains and schedules a bounded save. An explicit import writes immediately;
    ''' a failed immediate write keeps the block dirty for retry (105 L09).</summary>
    Friend Function ApplyModernPreferencesFromSettings(Optional persistImmediately As Boolean = False) As Boolean
        If modern_Preferences Is Nothing Then Return True
        modern_Preferences.Normalize()
        Dim previous As ModernViewerPreferences = last_Applied_Preferences
        If previous Is Nothing OrElse previous.FolderEnumerationKey() <> modern_Preferences.FolderEnumerationKey() Then
            InitializeExtensionLists()
            folder_List_Loaded_For = String.Empty
        End If
        If previous Is Nothing OrElse previous.SlideshowRandomOrder <> modern_Preferences.SlideshowRandomOrder Then ResetShuffleCycle()
        If previous Is Nothing OrElse previous.OverlayLayoutKey() <> modern_Preferences.OverlayLayoutKey() Then ApplyRecipientsOverlay()
        If previous Is Nothing OrElse previous.ChromeLayoutKey() <> modern_Preferences.ChromeLayoutKey() Then ApplyModernChrome()
        ' The two track languages have a live copy in the player layer - it is what a
        ' pick from the Tracks menu writes into - so a change made here has to reach it
        ' now, not at the next start (§6.3).
        preferred_Audio_Language = modern_Preferences.PreferredAudioLanguage
        preferred_Subtitle_Language = modern_Preferences.PreferredSubtitleLanguage
        If video_Controls IsNot Nothing Then video_Controls_Hide_Timer.Interval = VideoControlsHideDelayMilliseconds()
        ' 119 Ф-6: the cursor-hiding length (or its switch to 0) takes effect at once.
        UpdateCursorHideTimer()
        ' Mirrored rather than read: the decode cache is consulted from pool threads.
        DecodeCacheStore.BudgetMb = modern_Preferences.DecodeCacheMaxMb
        ' And the OCR cache's, which was read once at start-up: lowering it from 250 to 20
        ' kept trimming to 250 for the rest of the session (audit 051 E-07).
        ApplyOcrCacheBudget()
        If previous IsNot Nothing AndAlso (previous.VideoEndAction <> modern_Preferences.VideoEndAction OrElse
            previous.AudioEndAction <> modern_Preferences.AudioEndAction) Then RestartCurrentVideoPreservingPosition()
        last_Applied_Preferences = modern_Preferences.Snapshot()
        If Not persistImmediately Then
            MarkSettingsDirty()
            Return True
        End If
        Try
            If pending_Allow_New_Windows_Persistence Then
                modern_Preferences.PersistAllowNewWindows()
                pending_Allow_New_Windows_Persistence = False
            End If
            modern_Preferences.Save()
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Settings: persisting the preferences", ex)
            ' The values are live; only the registry copy is behind. The main settings block
            ' includes this profile, so marking it dirty schedules the bounded retries.
            MarkSettingsDirty()
            Return False
        End Try
    End Function

    ''' <summary>Applies a video/audio navigation toggle as a visible folder refresh.
    ''' The list is never mutated below a running slideshow: it is stopped first and the
    ''' ordinary load path then rebuilds list, counter, grid and prefetch together.</summary>
    Friend Sub ApplyMediaKindNavigationPreferenceChange()
        If modern_Preferences Is Nothing Then Return
        Dim stoppedSlideshow As Boolean = Is_slide_show_mode
        If stoppedSlideshow Then SlideShowStop()
        ApplyModernPreferencesFromSettings()
        If String.IsNullOrEmpty(Current_Folder_Path) Then Return
        ReadShowMediaFile(Mode_FolderAndFile)
        lbl_Status.Text = Localization.T(If(stoppedSlideshow, "Слайд-шоу остановлено; папка перечитана", "Папка перечитана"))
    End Sub

    Private Function RecentFoldersLimit() As Integer
        Return If(modern_Preferences Is Nothing, 100, modern_Preferences.RecentFoldersLimit)
    End Function

    Private Function StartupOpenMode() As String
        Return If(modern_Preferences Is Nothing, "lastFolder", modern_Preferences.StartupOpenMode)
    End Function

    ''' <summary>§3.1 branch 2 of SPECIFICATION_RESUME_LAST_PLAYBACK_DOTNET10 - reopen the
    ''' exact file that was on screen. Off unless the user asked for it.</summary>
    Private Function ResumeLastPlaybackEnabled() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.ResumeLastPlayback
    End Function

    ''' <summary>Restricts the already-supported extension set, never adds an
    ''' unknown extension merely because it appears in a hand-edited profile.</summary>
    Private Sub ApplyConfiguredExtensionFilter()
        If modern_Preferences Is Nothing OrElse String.IsNullOrWhiteSpace(modern_Preferences.IncludedExtensions) Then Return

        Dim requested As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            Using doc As JsonDocument = JsonDocument.Parse(modern_Preferences.IncludedExtensions)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return
                For Each item As JsonElement In doc.RootElement.EnumerateArray()
                    If item.ValueKind = JsonValueKind.String Then requested.Add(item.GetString().ToLowerInvariant())
                Next
            End Using
        Catch
            Return
        End Try
        If requested.Count = 0 Then Return
        all_Supported_Extensions.IntersectWith(requested)
    End Sub

    ''' <summary>Audio containers, which the viewer keeps in the video list because LibVLC
    ''' plays them the same way - but §3.4's dialog offers them as their own group, because
    ''' "show me the pictures and the videos but not the podcasts" is a real thing to want.</summary>

    ''' <summary>
    ''' Every extension the app can open, in the four groups §3.4 names. Built from the
    ''' live lists rather than a copy, so a format added to the viewer appears here on its
    ''' own - a second list would be a way for the dialog to offer something the scanner
    ''' does not accept.
    ''' </summary>
    Friend Function SupportedExtensionGroups() As List(Of KeyValuePair(Of String, String()))
        Dim p As ModernViewerPreferences = GetModernPreferences()
        Dim groups As New List(Of KeyValuePair(Of String, String()))() From {
            New KeyValuePair(Of String, String())(Localization.T("Изображения"), Image_File_Extensions.OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())(Localization.T("Видео"), video_File_Extensions.Where(Function(e) KindOf(e) = MediaKind.Video).OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())(Localization.T("Аудио"), video_File_Extensions.Where(Function(e) KindOf(e) = MediaKind.Audio).OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())(Localization.T("Другие поддерживаемые"), web_specific_image_extensions.OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())(Localization.T("Документы"), p.DocumentExtensionSet().OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())(Localization.T("Другие"), p.OtherExtensionSet().OrderBy(Function(e) e).ToArray())}
        Return groups.Where(Function(group) group.Value.Length > 0).ToList()
    End Function

    Friend Function RecentFilesSnapshot() As List(Of String)
        Return New List(Of String)(recent_Media_File_List)
    End Function

    Friend Function RecentFoldersSnapshot() As List(Of String)
        Return New List(Of String)(recent_Folder_List)
    End Function

    ''' <summary>Takes back what §7.2's dialog left after removals, and rebuilds the folder
    ''' drop-down from it so the two never disagree.</summary>
    Friend Sub ReplaceRecentHistory(files As List(Of String), folders As List(Of String))
        ' "Clear the history" has to reach the remembered last file too, or a cleared-out
        ' viewer reopens on the next start exactly what was just cleared
        ' (SPECIFICATION_RESUME_LAST_PLAYBACK_DOTNET10 §4). Only when the entry really left
        ' the list: with a recent-files limit of 0 it was never in it, and that must not be
        ' read as a removal.
        Dim was_Listed As Boolean = ListedInRecentFiles(recent_Media_File_List, last_Played_File)

        recent_Media_File_List = New List(Of String)(files)
        recent_Folder_List = New List(Of String)(folders)

        If was_Listed AndAlso Not ListedInRecentFiles(recent_Media_File_List, last_Played_File) Then ForgetLastPlayedFile()

        Dim previous As String = cmbox_Media_Folder.Text
        cmbox_Media_Folder.Items.Clear()
        For Each folder As String In recent_Folder_List
            If cmbox_Media_Folder.Items.Count >= RecentFoldersLimit() Then Exit For
            cmbox_Media_Folder.Items.Add(folder)
        Next
        cmbox_Media_Folder.Text = previous
    End Sub

    Private Shared Function ListedInRecentFiles(entries As List(Of String), path As String) As Boolean
        If entries Is Nothing OrElse String.IsNullOrEmpty(path) Then Return False
        Return entries.FindIndex(Function(item) String.Equals(item, path, StringComparison.OrdinalIgnoreCase)) >= 0
    End Function

    ''' <summary>Opens a history entry - a file or a folder - through the same door a
    ''' command line or a drag-and-drop uses.</summary>
    Friend Sub OpenHistoryEntry(entry As String)
        If String.IsNullOrEmpty(entry) Then Return
        ProcessArgument(entry)
    End Sub

    Private Function GetConfiguredSearchOption() As SearchOption
        Return If(modern_Preferences IsNot Nothing AndAlso modern_Preferences.IncludeSubfolders,
                  SearchOption.AllDirectories,
                  SearchOption.TopDirectoryOnly)
    End Function

    ''' <summary>Whether a copy moves the view on to the next file. Default (and the value
    ''' when preferences are not loaded yet) is True: the historical behaviour, and the one
    ''' the sorting run needs.</summary>
    Private Function AdvanceAfterCopy() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.AdvanceAfterCopy
    End Function

    Private Function StopSlideShowForManualNavigation() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.StopSlideshowOnManualNavigation
    End Function

    ''' <summary>
    ''' Whether decorative motion is switched off right now (§4.3).
    '''
    ''' Two independent sources, OR-ed: the user's own checkbox, and Windows' own
    ''' "play animations" switch. The system answer is only ever READ - a machine with
    ''' animations turned off must not rewrite the user's preference, or turning the
    ''' system setting back on would leave the app permanently still.
    ''' </summary>
    Friend Function ReduceMotionActive() As Boolean
        If modern_Preferences IsNot Nothing AndAlso modern_Preferences.ReduceMotion Then Return True
        Return Not SystemAnimationsEnabled()
    End Function

    Private Function SystemAnimationsEnabled() As Boolean
        Dim cached As Integer = Threading.Volatile.Read(system_Animations_State)
        If cached >= 0 Then Return cached = 1

        Dim enabled As Boolean = True
        Try
            ' A failed query is not "the user asked for stillness" - keep the animation.
            If Not SystemParametersInfo(spi_Get_Client_Area_Animation, 0UI, enabled, 0UI) Then enabled = True
        Catch
            enabled = True
        End Try
        Threading.Volatile.Write(system_Animations_State, If(enabled, 1, 0))
        Return enabled
    End Function

    ''' <summary>How a newly opened image is scaled (§4.2). Runs AFTER the picture is on
    ''' screen and before the perspective background is drawn, so the bars are computed
    ''' for the geometry the user actually ends up looking at.</summary>
    Private Sub ApplyNewImageScaleMode()
        If modern_Preferences Is Nothing Then Return
        If Not IsZoomableMediaShown() Then Return

        is_Zoom_Applied_By_Program = True
        Try
            Select Case modern_Preferences.NewImageScaleMode
                Case "actual"
                    ZoomToActualSize(PanelCentreAnchor())
                Case "perFolder"
                    ' No record for this folder yet is not an error - §4.2 says Fit, and
                    ' the display path has already fitted the picture.
                    Dim factor As Double = modern_Preferences.RememberedFolderZoom(PersistableFolderPath())
                    If factor > 0.0R Then ApplyZoomFactor(factor, PanelCentreAnchor())
            End Select
        Catch ex As Exception
            AppFileLogger.LogException("Applying the new-image scale", ex)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1142: new-image scale skipped: [" & ex.GetType().Name & "] " & ex.Message)
        Finally
            is_Zoom_Applied_By_Program = False
        End Try
    End Sub

    ''' <summary>Records the scale the user just chose against the current folder. Only in
    ''' the mode that reads them back, so the other two modes never accumulate a list of
    ''' folder paths nobody asked to keep.</summary>
    Private Sub RememberFolderZoom(factor As Double)
        If is_Zoom_Applied_By_Program Then Return
        If modern_Preferences Is Nothing OrElse modern_Preferences.NewImageScaleMode <> "perFolder" Then Return
        modern_Preferences.StoreFolderZoom(PersistableFolderPath(), factor)
        MarkSettingsDirty()
    End Sub

    ''' <summary>The user asked for Fit, so this folder has no scale to restore.</summary>
    Private Sub ForgetFolderZoom()
        If is_Zoom_Applied_By_Program Then Return
        If modern_Preferences Is Nothing OrElse modern_Preferences.NewImageScaleMode <> "perFolder" Then Return
        modern_Preferences.ForgetFolderZoom(PersistableFolderPath())
        MarkSettingsDirty()
    End Sub

    ''' <summary>Chrome the running slideshow hides (§5.2). Both answers are False unless a
    ''' slideshow is actually running and the user is not being shown the chrome on
    ''' request, so nothing here can hide a toolbar outside a slideshow.</summary>
    Private Function SlideshowHidesToolbar() As Boolean
        If Not Is_slide_show_mode OrElse slideshow_Chrome_Revealed Then Return False
        If modern_Preferences Is Nothing Then Return False
        Return modern_Preferences.SlideshowUiMode <> "none"
    End Function

    Private Function SlideshowHidesStatus() As Boolean
        If Not Is_slide_show_mode OrElse slideshow_Chrome_Revealed Then Return False
        If modern_Preferences Is Nothing Then Return False
        Return modern_Preferences.SlideshowUiMode = "toolbarAndStatus"
    End Function

    Private Function RecipientsOverlayWidth() As Integer
        Return If(modern_Preferences Is Nothing, 280, modern_Preferences.RecipientsOverlayWidth)
    End Function

    Private Function RecipientsOverlayFontSize() As Single
        Return CSng(If(modern_Preferences Is Nothing, 9, modern_Preferences.RecipientsOverlayFontSize))
    End Function

    Private Function RecipientsOverlayAlpha() As Integer
        Dim opacity As Integer = If(modern_Preferences Is Nothing, 88, modern_Preferences.RecipientsOverlayOpacity)
        Return CInt(Math.Round(opacity * 255.0R / 100.0R))
    End Function

    Private Function RecipientsOverlayVisibleRows() As Integer
        Return If(modern_Preferences Is Nothing, 11, modern_Preferences.RecipientsOverlayVisibleRows)
    End Function

    Private Function RecipientsOverlayPosition() As String
        Return If(modern_Preferences Is Nothing, "topLeft", modern_Preferences.RecipientsOverlayPosition)
    End Function

    Private Function VideoControlsHideDelayMilliseconds() As Integer
        Return If(modern_Preferences Is Nothing, 3000, modern_Preferences.VideoControlsHideDelaySec * 1000)
    End Function

    Private Function KeepVideoControlsVisibleWhilePaused() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.ShowVideoControlsWhenPaused
    End Function

    Private Function VideoClickMovesToNextFile() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.VideoSingleClickAction = "nextFile"
    End Function

    Private Function VideoShouldAutoplay() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.VideoAutoplay
    End Function

    Private Function VideoEndAction() As String
        Return If(modern_Preferences Is Nothing, "stay", modern_Preferences.VideoEndAction)
    End Function

    Private Function AudioEndAction() As String
        Return If(modern_Preferences Is Nothing, "next", modern_Preferences.AudioEndAction)
    End Function

    Private Sub QueueRememberedVideoPosition(filePath As String)
        If modern_Preferences Is Nothing OrElse Not modern_Preferences.RememberVideoPosition Then Return
        Try
            Dim info As New FileInfo(filePath)
            pending_Video_Seek = modern_Preferences.RememberedVideoPosition(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length)
        Catch ex As Exception
            AppFileLogger.LogException("Reading the remembered playback position", ex)
            ClearPendingVideoPosition()
        End Try
    End Sub

    ''' <summary>Stores where the playing file is. Keyed by <c>vlc_Playing_Path</c> - what
    ''' VLC was actually told to play - not current_Loaded_File_Name, which the display
    ''' path clears before VLC is stopped (audit 050 D-01).</summary>
    Private Sub RememberCurrentVideoPosition()
        If vlc_Media_Player Is Nothing OrElse Not CanRememberPositionFor(vlc_Playing_Path) Then Return
        Try
            If Not vlc_Media_Player.IsSeekable Then Return
            Dim info As New FileInfo(vlc_Playing_Path)
            modern_Preferences.StoreVideoPosition(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length, vlc_Media_Player.Position)
            modern_Preferences.Save()
        Catch ex As Exception
            AppFileLogger.LogException("Saving the playback position", ex)
        End Try
    End Sub

    ''' <summary>A file played to its end opens from the start next time: its entry goes
    ''' (RESUME spec §3.3, audit 050 D-11).</summary>
    Private Sub RememberVideoFinished(filePath As String)
        If Not CanRememberPositionFor(filePath) Then Return
        Try
            Dim info As New FileInfo(filePath)
            modern_Preferences.StoreVideoPosition(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length, 1.0F)
            modern_Preferences.Save()
        Catch ex As Exception
            AppFileLogger.LogException("Clearing the finished playback position", ex)
        End Try
    End Sub

    Private Function CanRememberPositionFor(filePath As String) As Boolean
        If modern_Preferences Is Nothing OrElse Not modern_Preferences.RememberVideoPosition Then Return False
        ' A stream has no file to key a position by.
        If String.IsNullOrEmpty(filePath) OrElse IsRemoteMediaUrl(filePath) Then Return False
        ' An archive entry's temporary copy is keyed by a random session token: its position
        ' could never be found again, and 010 §4.6 keeps the temporary path out of every
        ' persisted history (audit 052 A-05).
        If ArchivePaths.IsUnderArchiveCache(filePath) Then Return False
        ' The same holds for a file fetched from a remote resource: its copy sits in a per-session
        ' directory that is deleted when the resource is left, so a position stored under it could
        ' never match again - and the cache path must not enter a persisted history (007 section 7).
        If RemoteCachePaths.IsSessionCopy(filePath) Then Return False
        Return True
    End Function

    Private Sub ResetShuffleCycle()
        shuffle_Cycle.Reset()
    End Sub

    Private Function NextShuffleCycleIndex(total As Integer, current As Integer) As Integer
        Return shuffle_Cycle.NextIndex(total, current, slideshow_Rng)
    End Function

    ''' <summary>True when the user chose "shuffle without repeats" for random order.</summary>
    Private Function IsShuffleCycleOrder() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.SlideshowRandomOrder = "shuffleCycle"
    End Function
End Class
#End If

#If NETFRAMEWORK Then
Partial Public Class Main_Form
    ' Keep the shared loading code buildable for the frozen x86 target.  These
    ' values preserve its historical behaviour; the settings themselves are
    ' deliberately a .NET 10 feature.
    Private Function RecentFilesLimit() As Integer
        Return 50
    End Function

    Private Function RecentFoldersLimit() As Integer
        Return 100
    End Function

    ''' <summary>"lastFile", not "lastFolder": this build has always reopened the last
    ''' folder AND the file inside it that was on screen, and §7.1 is the setting that
    ''' names those two behaviours apart - the frozen target keeps the one it shipped.</summary>
    Private Function StartupOpenMode() As String
        Return "lastFile"
    End Function

    ''' <summary>The resume option is a .NET 10 feature (§2.2): the frozen target keeps
    ''' the start it shipped with, and never records a last-played path.</summary>
    Private Function ResumeLastPlaybackEnabled() As Boolean
        Return False
    End Function

    Private Function GetConfiguredSearchOption() As IO.SearchOption
        Return IO.SearchOption.TopDirectoryOnly
    End Function

    Private Function RecipientsOverlayWidth() As Integer
        Return 280
    End Function

    Private Function RecipientsOverlayFontSize() As Single
        Return 11.0F
    End Function

    Private Function RecipientsOverlayAlpha() As Integer
        Return 224
    End Function

    Private Function RecipientsOverlayVisibleRows() As Integer
        Return 11   ' ten slots + Delete: the Delete row is never below the fold (audit B-13)
    End Function

    Private Function RecipientsOverlayPosition() As String
        Return "topLeft"
    End Function

    Private Function VideoControlsHideDelayMilliseconds() As Integer
        Return 2500
    End Function

    Private Function KeepVideoControlsVisibleWhilePaused() As Boolean
        Return True
    End Function

    Private Function VideoClickMovesToNextFile() As Boolean
        Return False
    End Function

    Private Function VideoShouldAutoplay() As Boolean
        Return True
    End Function

    Private Function VideoEndAction() As String
        Return "stay"
    End Function

    ''' <summary>The slideshow never hides chrome on the frozen x86 target - the setting
    ''' that would ask for it is a .NET 10 feature (§5.2).</summary>
    Private Function SlideshowHidesToolbar() As Boolean
        Return False
    End Function

    Private Function SlideshowHidesStatus() As Boolean
        Return False
    End Function
End Class
#End If
