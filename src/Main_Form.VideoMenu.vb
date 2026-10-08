#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

' The video's own right-click menu - modern build only.
'
' WHY MODERN ONLY, and like the tracks button this one is not arbitrary: on net48 a video
' goes to the IE WebBrowser first and only reaches LibVLC when IE refuses it, so the same
' menu would offer tracks and repeat for an MKV and silently nothing for an MP4 - a menu
' whose contents depend on which decoder happened to win is worse than no menu. Modern has
' one engine, so the answer is always the same.
'
' WHY IT EXISTS: LibVLC renders the picture and nothing else. Main_Form.VideoControls.vb
' gave back the transport IE used to draw (seek, clock, volume, play), but everything else
' this app can do to a video was scattered - tracks behind a toolbar button that only
' appears sometimes, repeat behind the Settings window, full screen and the file
' operations behind hotkeys a first-time user has no reason to guess. On a video the mouse
' now says all of it: left click plays/pauses, right click opens this.
'
' The menu OWNS no behaviour. Every item calls the very method its hotkey calls, so the
' two cannot drift apart. It is rebuilt on each open because most of it - which tracks
' exist, whether we are paused, whether this is a file or a stream - belongs to the video
' playing right now, not to the menu.
Partial Public Class Main_Form

    Private video_Menu As ContextMenuStrip

    ''' <summary>Set while a reopened video is on its way back to where it was - see
    ''' RestartCurrentVideoPreservingPosition. -1 = nothing pending.</summary>
    Private pending_Video_Seek As Single = -1.0F
    Private pending_Video_Pause As Boolean = False

    ''' <summary>The media playing now was opened with <c>:input-repeat</c> (the native loop). libvlc cannot change that
    ''' on a running media, which is why switching repeat OFF still has to reopen it (121 UX-413).</summary>
    Private video_Started_With_Repeat As Boolean = False

    ''' <summary>Opens the menu at a screen point. No video, no menu. <paramref name="anchorTop"/> is the top edge of the
    ''' control the menu opens from (the transport bar's "more" button): a menu that does not fit below it opens above it.</summary>
    Private Sub ShowVideoContextMenu(at_Screen As Point, Optional fromKeyboard As Boolean = False, Optional anchorTop As Integer = Integer.MinValue)
        If Not is_Vlc_Playing OrElse vlc_Media_Player Is Nothing Then Return

        ' Otherwise the slideshow flips the file out from under the open menu, and the
        ' item the user finally picks acts on a video that is no longer there.
        SlideShowStop()

        If video_Menu Is Nothing Then video_Menu = New ContextMenuStrip()
        BuildVideoMenu()
        If video_Menu.Items.Count = 0 Then Return
        ChromeShowToolStripMenu(video_Menu.Items, at_Screen, fromKeyboard, anchorTop)
    End Sub

    Private Sub BuildVideoMenu()
        ClearAndDisposeItems(video_Menu.Items)

        ' A stream belongs to no folder and has no file to rename, move or hand to another
        ' player - those items are left out rather than shown dead (Main_Form.OpenUrl.vb).
        Dim is_Local_File As Boolean = Not String.IsNullOrEmpty(current_Loaded_File_Name) AndAlso
                                       Not IsRemoteMediaUrl(current_Loaded_File_Name)

        ' --- playback
        AddMenuItem(video_Menu.Items,
                    If(IsVlcActuallyPlaying(), Localization.T("Пауза"), Localization.T("Продолжить")),
                    Sub()
                        TogglePlayPause()
                        RefreshVideoControlsState()
                        ShowVideoControls()
                    End Sub, actionId:="playPause")

        AddMenuItem(video_Menu.Items, Localization.T("Без звука"),
                    Sub()
                        ToggleVideoMute()
                        RefreshVideoControlsState()
                        ShowVideoControls()
                    End Sub,
                    checked:=is_Video_Muted, actionId:="mute")

        ' What happens when the file ends, as one group - "repeat" is one of its three modes, not a switch of its own.
        video_Menu.Items.Add(CreateEndModeSubmenu())

        ' --- the playback extras of 119, in the playback group after the end mode --------
        AddSpeedMenuItems(video_Menu.Items)
        AddAbLoopMenuItem(video_Menu.Items)
        ' Both rows are drawn disabled, with the reason, where the keys' own gate says no: an audio file has no
        ' frames, and a live broadcast is nobody's playback experiment (125 UX-328).
        Dim frame_Step_Applicable As Boolean = PlaybackActionApplicable("frameStep")
        AddMenuItem(video_Menu.Items, Localization.T("Следующий кадр"), Sub() RunFrameStep(), actionId:="frameStep",
                    enabled:=frame_Step_Applicable,
                    disabledReason:=Localization.T("Недоступно для аудио и прямых трансляций"))

        ' --- tracks
        Dim audio_Item As ToolStripMenuItem = CreateTrackSubmenu(audio:=True)
        Dim subtitle_Item As ToolStripMenuItem = CreateTrackSubmenu(audio:=False)
        If audio_Item IsNot Nothing OrElse subtitle_Item IsNot Nothing Then
            video_Menu.Items.Add(New ToolStripSeparator())
            If audio_Item IsNot Nothing Then video_Menu.Items.Add(audio_Item)
            If subtitle_Item IsNot Nothing Then video_Menu.Items.Add(subtitle_Item)
        End If

        ' --- view
        video_Menu.Items.Add(New ToolStripSeparator())
        AddViewItems(video_Menu.Items)

        ' --- navigation
        video_Menu.Items.Add(New ToolStripSeparator())
        AddNavigationItems(video_Menu.Items)
        ' The key Y has a row here too (121 Ф-3). Only where a folder navigates: a single opened file has nowhere
        ' random to go.
        If files_List IsNot Nothing AndAlso files_List.Count > 1 Then
            AddMenuItem(video_Menu.Items, Localization.T("Случайный файл (Y)"), Sub() ReadShowMediaFile(Mode_ForRandom), actionId:="randomFile")
        End If

        ' --- the file itself: the same block as the picture menu (125 UX-330), gathered under one submenu
        If is_Local_File Then
            video_Menu.Items.Add(New ToolStripSeparator())
            Dim file_Menu As New ToolStripMenuItem(Localization.T("Операции с файлами"))
            AddFileBlock(file_Menu.DropDownItems)
            video_Menu.Items.Add(file_Menu)
        End If

        ' --- hand it elsewhere
        video_Menu.Items.Add(New ToolStripSeparator())
        If is_Local_File Then
            AddMenuItem(video_Menu.Items, Localization.T("Открыть во внешнем плеере"),
                        Sub() TryOpenVideoWithDefaultPlayer(current_Loaded_File_Name, automatic:=False))
            AddMenuItem(video_Menu.Items, Localization.T("Транслировать этот файл.."),
                        Sub() ActivateBroadcastEntryPoint(current_Loaded_File_Name))
        End If
        AddMenuItem(video_Menu.Items, Localization.T("Открыть URL.."), Sub() ShowOpenUrlPrompt())
    End Sub

    ''' <summary>The end-of-file modes as ONE radio-style group: the row that is ticked is what happens when the file
    ''' on screen ends. Rebuilt on every opening, so the tick is always what is stored now. The rows are the same
    ''' three the Settings window offers, in the same order.</summary>
    Private Function CreateEndModeSubmenu() As ToolStripMenuItem
        Dim is_Audio As Boolean = IsAudioNowPlaying()
        Dim current As String = PlaybackRules.EndModeOf(If(is_Audio, AudioEndAction(), VideoEndAction()), is_Audio, Is_Video_Loop)

        Dim root As New ToolStripMenuItem(Localization.T("Режим воспроизведения"))
        For Each mode As String In {PlaybackRules.End_Mode_Stay, PlaybackRules.End_Mode_Advance, PlaybackRules.End_Mode_Repeat}
            Dim picked As String = mode
            Dim item As New ToolStripMenuItem(EndModeCaption(picked)) With {.Checked = (picked = current)}
            AddHandler item.Click, Sub() SetEndMode(picked)
            root.DropDownItems.Add(item)
        Next
        Return root
    End Function

    Private Shared Function EndModeCaption(mode As String) As String
        Select Case mode
            Case PlaybackRules.End_Mode_Advance : Return Localization.T("Автопереход к следующему")
            Case PlaybackRules.End_Mode_Repeat : Return Localization.T("По кругу")
            Case Else : Return Localization.T("Проиграть и стоп")
        End Select
    End Function

    ''' <summary>Makes <paramref name="mode"/> what happens when the file on screen ends - the one place the menu writes
    ''' the end action. Repeat is a MEDIA option (":input-repeat"), baked in when Play() is called, so the stored value
    ''' alone would leave the video playing on the old setting until it was reopened. Leaving repeat therefore reopens
    ''' the video here, at the same second (121 UX-413); going TO repeat does not - the end of the pass reopens it once
    ''' (Vlc_Media_Player_EndReached). The loop flag of the Settings checkbox is cleared, or it would keep repeating
    ''' whatever the menu chose.</summary>
    Private Sub SetEndMode(mode As String)
        Dim is_Audio As Boolean = IsAudioNowPlaying()
        Is_Video_Loop = False
        If is_Audio Then
            GetModernPreferences().AudioEndAction = PlaybackRules.StoredEndAction(mode, True)
        Else
            GetModernPreferences().VideoEndAction = PlaybackRules.StoredEndAction(mode, False)
        End If
        MarkSettingsDirty()
        lbl_Status.Text = EndModeCaption(mode)
        If TransportRules.RepeatToggleNeedsReopen(video_Started_With_Repeat, CurrentEffectiveRepeat()) Then RestartCurrentVideoPreservingPosition()
        ShowVideoControls()
    End Sub

    ''' <summary>Whether what is playing repeats: the loop flag or the end action of the
    ''' kind on screen (PlaybackRules.EffectiveRepeat).</summary>
    Friend Function CurrentEffectiveRepeat() As Boolean
        Return PlaybackRules.EffectiveRepeat(Is_Video_Loop, IsAudioNowPlaying(), AudioEndAction(), VideoEndAction(), Is_slide_show_mode)
    End Function

    ''' <summary>The Speed submenu of 119 Ф-1: the ladder as radio-checked items (checked
    ''' = the nearest step to the rate the user asked for), keys and button stay one
    ''' truth through ApplyPlaybackRate. The rows are BuildSpeedLadderItems' - the speed button's own menu is the same list.</summary>
    Private Sub AddSpeedMenuItems(target As ToolStripItemCollection)
        Dim speed_Item As New ToolStripMenuItem(Localization.T("Скорость воспроизведения"))
        speed_Item.DropDownItems.AddRange(BuildSpeedLadderItems().ToArray())
        target.Add(speed_Item)
    End Sub

    ''' <summary>The A-B menu item of 119 Ф-4: one item whose caption follows the phase
    ''' - mark A, mark B, clear - the same Cycle the key drives.</summary>
    Private Sub AddAbLoopMenuItem(target As ToolStripItemCollection)
        Dim caption As String
        Select Case ab_Loop.Phase
            Case AbPhase.MarkedA : caption = Localization.T("Повтор A-B: отметить B")
            Case AbPhase.Armed : caption = Localization.T("Повтор A-B: сбросить")
            Case Else : caption = Localization.T("Повтор A-B: отметить A")
        End Select

        AddMenuItem(target, caption, Sub() RunAbLoop(), actionId:="abLoop",
                    enabled:=PlaybackActionApplicable("abLoop"),
                    disabledReason:=Localization.T("Недоступно для аудио и прямых трансляций"))
    End Sub

    Private Sub RestartCurrentVideoPreservingPosition()
        RestartCurrentVideoPreservingPosition(-1.0F)
    End Sub

    ''' <summary>
    ''' Reopens the current file at the same second. The optional seek is the A-B end
    ''' guard's way back into the span (119 Ф-4, Q119-2): the caller knows the fraction
    ''' it wants, because at EndReached the player's own IsPlaying can no longer be
    ''' trusted to say whether the reopened media should resume paused.
    ''' </summary>
    Private Sub RestartCurrentVideoPreservingPosition(at_Position As Single)
        If vlc_Media_Player Is Nothing OrElse Not is_Vlc_Playing Then Return
        Dim file_Path As String = current_Loaded_File_Name
        If String.IsNullOrEmpty(file_Path) Then Return

        Try
            ' A live stream has no position to come back to - reopening one restarts it at
            ' "now", which is the only thing it could mean anyway.
            pending_Video_Seek = If(at_Position >= 0.0F, at_Position,
                                    If(vlc_Media_Player.IsSeekable, vlc_Media_Player.Position, -1.0F))
            pending_Video_Pause = If(at_Position >= 0.0F, False, Not vlc_Media_Player.IsPlaying)

            Dim media As LibVLCSharp.Shared.Media = CreateVlcMedia(file_Path)
            video_Started_With_Repeat = CurrentEffectiveRepeat()
            If video_Started_With_Repeat Then media.AddOption(":input-repeat=65535")
            Dim keep_Media_For_Art_Watch As Boolean = WatchSecureMediaArt(file_Path, media)
            vlc_Media_Player.Play(media)
            If Not keep_Media_For_Art_Watch Then media.Dispose()
            ' A reopened media starts on VLC's own track pick again (050 D-10).
            vlc_Play_Serial += 1
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2510: video reopen failed: " & ex.Message)
            ' The status already says the new mode is on; this is the only trace (057.S09-5).
            AppFileLogger.WriteLine("Video menu: reopening the video to apply Repeat failed (" & ex.GetType().Name & "): " & ex.Message)
            ClearPendingVideoPosition()
        End Try
    End Sub

    ''' <summary>A new file cancels any restore still owed to the old one - otherwise the
    ''' seek lands on whatever started next.</summary>
    Friend Sub ClearPendingVideoPosition()
        pending_Video_Seek = -1.0F
        pending_Video_Pause = False
    End Sub

    ''' <summary>
    ''' Puts a reopened video back where it was. Called from VLC's Playing event - the
    ''' first moment the new media will accept a seek at all.
    ''' </summary>
    Friend Sub ApplyPendingVideoPosition()
        If vlc_Media_Player Is Nothing Then Return

        Dim at As Single = pending_Video_Seek
        Dim pause As Boolean = pending_Video_Pause
        ClearPendingVideoPosition()
        If at < 0.0F AndAlso Not pause Then Return

        Try
            If at > 0.0F AndAlso at < 1.0F Then vlc_Media_Player.Position = at
            ' SetPause, not Pause: libvlc's Pause() TOGGLES, and this must not depend on
            ' what state the fresh media happens to be in.
            If pause Then vlc_Media_Player.SetPause(True)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2511: video position restore failed: " & ex.Message)
        End Try
    End Sub

End Class
#End If
