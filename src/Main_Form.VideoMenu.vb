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
    ''' ToggleVideoLoop. -1 = nothing pending.</summary>
    Private pending_Video_Seek As Single = -1.0F
    Private pending_Video_Pause As Boolean = False

    ''' <summary>Opens the menu at a screen point. No video, no menu.</summary>
    Private Sub ShowVideoContextMenu(at_Screen As Point)
        If Not is_Vlc_Playing OrElse vlc_Media_Player Is Nothing Then Return

        ' Otherwise the slideshow flips the file out from under the open menu, and the
        ' item the user finally picks acts on a video that is no longer there.
        SlideShowStop()

        If video_Menu Is Nothing Then video_Menu = New ContextMenuStrip()
        BuildVideoMenu()
        If video_Menu.Items.Count = 0 Then Return
        video_Menu.Show(at_Screen)
    End Sub

    Private Sub BuildVideoMenu()
        video_Menu.Items.Clear()

        Dim rus As Boolean = Is_Russian_Language

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
                    End Sub)

        AddMenuItem(video_Menu.Items, Localization.T("Без звука"),
                    Sub()
                        SetVideoAudioState(video_Volume_Level, Not is_Video_Muted)
                        RefreshVideoControlsState()
                        ShowVideoControls()
                    End Sub,
                    checked:=is_Video_Muted)

        AddMenuItem(video_Menu.Items, Localization.T("Повторять"), AddressOf ToggleVideoLoop, checked:=Is_Video_Loop)

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

        ' --- the file itself
        If is_Local_File Then
            video_Menu.Items.Add(New ToolStripSeparator())
            AddFileOperationItems(video_Menu.Items)
        End If

        ' --- hand it elsewhere
        video_Menu.Items.Add(New ToolStripSeparator())
        If is_Local_File Then
            AddMenuItem(video_Menu.Items, Localization.T("Открыть во внешнем плеере"),
                        Sub() TryOpenVideoWithDefaultPlayer(current_Loaded_File_Name))
        End If
        AddMenuItem(video_Menu.Items, Localization.T("Открыть URL.."), Sub() ShowOpenUrlPrompt())
    End Sub

    ''' <summary>
    ''' Repeat is a MEDIA option (":input-repeat"), baked in when Play() is called - so
    ''' flipping the flag alone would leave the video playing right now on the old setting
    ''' until it was reopened, and a tick that only takes effect on the NEXT file reads as
    ''' broken. Reopen it here, at the same second, and let the Settings checkbox keep the
    ''' choice from then on (it is the same global).
    ''' </summary>
    Private Sub ToggleVideoLoop()
        Is_Video_Loop = Not Is_Video_Loop
        lbl_Status.Text = Localization.T(If(Is_Video_Loop, "Повтор включён", "Повтор выключен"))
        RestartCurrentVideoPreservingPosition()
        ShowVideoControls()
    End Sub

    Private Sub RestartCurrentVideoPreservingPosition()
        If vlc_Media_Player Is Nothing OrElse Not is_Vlc_Playing Then Return
        Dim file_Path As String = current_Loaded_File_Name
        If String.IsNullOrEmpty(file_Path) Then Return

        Try
            ' A live stream has no position to come back to - reopening one restarts it at
            ' "now", which is the only thing it could mean anyway.
            pending_Video_Seek = If(vlc_Media_Player.IsSeekable, vlc_Media_Player.Position, -1.0F)
            pending_Video_Pause = Not vlc_Media_Player.IsPlaying

            Dim media As LibVLCSharp.Shared.Media = CreateVlcMedia(file_Path)
            If Is_Video_Loop Then media.AddOption(":input-repeat=65535")
            vlc_Media_Player.Play(media)
            media.Dispose()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2510: video reopen failed: " & ex.Message)
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
