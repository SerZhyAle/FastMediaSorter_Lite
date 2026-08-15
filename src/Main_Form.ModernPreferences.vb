#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text.Json

Partial Public Class Main_Form

    Private shuffle_Cycle As New Queue(Of Integer)()
    Private shuffle_Cycle_Count As Integer

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

    ''' <summary>Nothing until the first read - the system answer is taken once per
    ''' session, which is exactly as long as §4.3 says it applies.</summary>
    Private system_Animations_Enabled As Boolean?

    Private Function RecentFilesLimit() As Integer
        Return If(modern_Preferences Is Nothing, 50, modern_Preferences.RecentFilesLimit)
    End Function

    Friend Function GetModernPreferences() As ModernViewerPreferences
        If modern_Preferences Is Nothing Then modern_Preferences = ModernViewerPreferences.Load()
        Return modern_Preferences
    End Function

    Friend Sub ReplaceModernPreferences(imported As ModernViewerPreferences)
        If imported Is Nothing Then Throw New ArgumentNullException(NameOf(imported))
        imported.Normalize()
        modern_Preferences = imported
        ApplyModernPreferencesFromSettings()
    End Sub

    Friend Sub ApplyModernPreferencesFromSettings()
        If modern_Preferences Is Nothing Then Return
        modern_Preferences.Normalize()
        InitializeExtensionLists()
        folder_List_Loaded_For = String.Empty
        ResetShuffleCycle()
        ApplyRecipientsOverlay()
        ' The two track languages have a live copy in the player layer - it is what a
        ' pick from the Tracks menu writes into - so a change made here has to reach it
        ' now, not at the next start (§6.3).
        preferred_Audio_Language = modern_Preferences.PreferredAudioLanguage
        preferred_Subtitle_Language = modern_Preferences.PreferredSubtitleLanguage
        If video_Controls IsNot Nothing Then video_Controls_Hide_Timer.Interval = VideoControlsHideDelayMilliseconds()
        ' Expanded settings are live settings. Persist after normalisation so a change
        ' made in the settings window survives a crash and is visible to the next
        ' process without waiting for the main form to close.
        modern_Preferences.Save()
    End Sub

    Private Function RecentFoldersLimit() As Integer
        Return If(modern_Preferences Is Nothing, 100, modern_Preferences.RecentFoldersLimit)
    End Function

    Private Function StartupOpenMode() As String
        Return If(modern_Preferences Is Nothing, "lastFolder", modern_Preferences.StartupOpenMode)
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
    Private Shared ReadOnly audio_Only_Extensions As String() = {".mp3", ".wav", ".wma", ".m4a", ".ogg"}

    ''' <summary>
    ''' Every extension the app can open, in the four groups §3.4 names. Built from the
    ''' live lists rather than a copy, so a format added to the viewer appears here on its
    ''' own - a second list would be a way for the dialog to offer something the scanner
    ''' does not accept.
    ''' </summary>
    Friend Function SupportedExtensionGroups() As List(Of KeyValuePair(Of String, String()))
        Dim audio As New HashSet(Of String)(audio_Only_Extensions, StringComparer.OrdinalIgnoreCase)
        Dim groups As New List(Of KeyValuePair(Of String, String()))() From {
            New KeyValuePair(Of String, String())("Изображения", Image_File_Extensions.OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())("Видео", video_File_Extensions.Where(Function(e) Not audio.Contains(e)).OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())("Аудио", video_File_Extensions.Where(Function(e) audio.Contains(e)).OrderBy(Function(e) e).ToArray()),
            New KeyValuePair(Of String, String())("Другие поддерживаемые", web_specific_image_extensions.OrderBy(Function(e) e).ToArray())}
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
        recent_Media_File_List = New List(Of String)(files)
        recent_Folder_List = New List(Of String)(folders)

        Dim previous As String = cmbox_Media_Folder.Text
        cmbox_Media_Folder.Items.Clear()
        For Each folder As String In recent_Folder_List
            If cmbox_Media_Folder.Items.Count >= RecentFoldersLimit() Then Exit For
            cmbox_Media_Folder.Items.Add(folder)
        Next
        cmbox_Media_Folder.Text = previous
    End Sub

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
        If system_Animations_Enabled.HasValue Then Return system_Animations_Enabled.Value

        Dim enabled As Boolean = True
        Try
            ' A failed query is not "the user asked for stillness" - keep the animation.
            If Not SystemParametersInfo(spi_Get_Client_Area_Animation, 0UI, enabled, 0UI) Then enabled = True
        Catch
            enabled = True
        End Try
        system_Animations_Enabled = enabled
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
    End Sub

    ''' <summary>The user asked for Fit, so this folder has no scale to restore.</summary>
    Private Sub ForgetFolderZoom()
        If is_Zoom_Applied_By_Program Then Return
        If modern_Preferences Is Nothing OrElse modern_Preferences.NewImageScaleMode <> "perFolder" Then Return
        modern_Preferences.ForgetFolderZoom(PersistableFolderPath())
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
        Return CSng(If(modern_Preferences Is Nothing, 11, modern_Preferences.RecipientsOverlayFontSize))
    End Function

    Private Function RecipientsOverlayAlpha() As Integer
        Dim opacity As Integer = If(modern_Preferences Is Nothing, 88, modern_Preferences.RecipientsOverlayOpacity)
        Return CInt(Math.Round(opacity * 255.0R / 100.0R))
    End Function

    Private Function RecipientsOverlayVisibleRows() As Integer
        Return If(modern_Preferences Is Nothing, 10, modern_Preferences.RecipientsOverlayVisibleRows)
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

    Private Sub QueueRememberedVideoPosition(filePath As String)
        If modern_Preferences Is Nothing OrElse Not modern_Preferences.RememberVideoPosition Then Return
        Try
            Dim info As New FileInfo(filePath)
            pending_Video_Seek = modern_Preferences.RememberedVideoPosition(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length)
        Catch
            ClearPendingVideoPosition()
        End Try
    End Sub

    Private Sub RememberCurrentVideoPosition()
        If modern_Preferences Is Nothing OrElse Not modern_Preferences.RememberVideoPosition OrElse
           vlc_Media_Player Is Nothing OrElse String.IsNullOrEmpty(current_Loaded_File_Name) Then Return
        Try
            If Not vlc_Media_Player.IsSeekable Then Return
            Dim info As New FileInfo(current_Loaded_File_Name)
            modern_Preferences.StoreVideoPosition(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length, vlc_Media_Player.Position)
            modern_Preferences.Save()
        Catch
        End Try
    End Sub

    Private Sub ResetShuffleCycle()
        shuffle_Cycle.Clear()
        shuffle_Cycle_Count = 0
    End Sub

    Private Function NextShuffleCycleIndex(total As Integer, current As Integer) As Integer
        If total <= 1 Then Return 0
        If shuffle_Cycle_Count <> total OrElse shuffle_Cycle.Count = 0 Then
            Dim values As New List(Of Integer)()
            For i As Integer = 0 To total - 1
                If i <> current Then values.Add(i)
            Next
            For i As Integer = values.Count - 1 To 1 Step -1
                Dim j As Integer = slideshow_Rng.Next(i + 1)
                Dim swap As Integer = values(i)
                values(i) = values(j)
                values(j) = swap
            Next
            If values.Count = 0 Then values.Add(0)
            shuffle_Cycle = New Queue(Of Integer)(values)
            shuffle_Cycle_Count = total
        End If
        Return shuffle_Cycle.Dequeue()
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
        Return 10
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
