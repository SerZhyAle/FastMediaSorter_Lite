Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Main_Form

    Private Function ClampVideoVolume(volume As Double) As Double
        Return Math.Max(0.0, Math.Min(1.0, volume))
    End Function

    Private Function ParseVideoVolumeSetting(value As String, fallback As Double) As Double
        Dim parsed_Value As Double

        If Double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, parsed_Value) OrElse
            Double.TryParse(value, parsed_Value) Then

            Return ClampVideoVolume(parsed_Value)
        End If

        Return ClampVideoVolume(fallback)
    End Function

    Private Sub ApplyVideoAudioStateToVlc()
        If vlc_Media_Player Is Nothing Then Return

        Try
            vlc_Media_Player.Volume = CInt(Math.Round(video_Volume_Level * 100))
            vlc_Media_Player.Mute = is_Video_Muted
        Catch
        End Try
    End Sub

    Public Sub SetVolume(volume As Double)
        SetVideoAudioState(volume, is_Video_Muted)
    End Sub

    ''' <summary>Current default video volume as a 0..100 percent (settings UI).</summary>
    Public ReadOnly Property CurrentVideoVolumePercent As Integer
        Get
            Return CInt(Math.Round(video_Volume_Level * 100))
        End Get
    End Property

    ''' <summary>Whether video starts muted by default (settings UI).</summary>
    Public ReadOnly Property CurrentVideoMuted As Boolean
        Get
            Return is_Video_Muted
        End Get
    End Property

    Public Sub SetVideoAudioState(volume As Double, muted As Boolean)
        video_Volume_Level = ClampVideoVolume(volume)
        is_Video_Muted = muted
        ApplyVideoAudioStateToVlc()
    End Sub

    Public Sub HandleWebBrowserDoubleClick()
        If Me.InvokeRequired Then
            Me.Invoke(New Action(AddressOf HandleWebBrowserDoubleClick))
        Else
            If is_Full_Screen_Mode Then
                is_Full_Screen_Mode = False
                is_Super_Full_Screen_Mode = False
                SetViewSizes()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0842: HandleWebBrowserDoubleClick: (WebBrowser)")
            End If
        End If
    End Sub

    Public Sub HandleVideoError(errorMessage As String)
        If Me.InvokeRequired Then
            Me.Invoke(New Action(Of String)(AddressOf HandleVideoError), errorMessage)
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0865: Video error detected: " & errorMessage)

            If errorMessage.Contains("Unsupported video type") OrElse errorMessage.Contains("invalid file path") Then
                PlayVideoWithVlcAsync(Current_File_Name)
            End If
        End If
    End Sub

    ''' <summary>Hands the file to whatever Windows plays it with. Takes the path as an
    ''' argument: it used to read Current_File_Name, which by the time a slow VLC init
    ''' gave up could be a completely different file.</summary>
    Private Sub TryOpenVideoWithDefaultPlayer(video_File_Path As String)
        Try
            If Not String.IsNullOrEmpty(video_File_Path) AndAlso File.Exists(video_File_Path) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0866: Opening video with default player: " & video_File_Path)

#If NETFRAMEWORK Then
                Web_Browser.DocumentText = "<html><body style='background:" &
                                        If(Form_Color_Scheme = 0, "black", "white") &
                                        "; color:" & If(Form_Color_Scheme = 0, "white", "black") &
                                        "; text-align:center; font-family:Arial;'>" &
                                        "<h3>" & Localization.T("Видео открыто во внешнем плеере") & "</h3>" &
                                        "<p>" & Path.GetFileName(video_File_Path) & "</p>" &
                                        "<p style='font-size:12px; color:gray;'>" &
                                        Localization.T("Нажмите стрелки для перехода к следующему файлу") &
                                        "</p></body></html>"
#End If

                ' Explicit UseShellExecute: opening a document needs the shell; net48
                ' defaulted to True, .NET defaults to False. On the modern build this
                ' is the ONLY video fallback when LibVLC is unavailable.
                Process.Start(New ProcessStartInfo(video_File_Path) With {.UseShellExecute = True})

                lbl_Status.Text = Localization.TF("Видео открыто во внешнем плеере: {0}", Path.GetFileName(video_File_Path))

            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0867: Error opening video with default player: " & ex.Message)
            lbl_Status.Text = Localization.TF("Ошибка запуска внешнего плеера: {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' The single entry point to initialisation: everyone awaits the SAME task.
    '''
    ''' Handing out a fresh task per call was a race with a long fuse - while the first
    ''' call sat in the runtime download, libVlc was still Nothing, so a flip to another
    ''' video walked straight past the guard below and built a second LibVLC and a second
    ''' MediaPlayer. The first pair was then unreachable but alive: native resources
    ''' leaked, its file stayed locked (so Delete/Move on it failed), an orphaned
    ''' VideoView sat in panel_Media for ever, and both players played.
    ''' </summary>
    Private Function EnsureVlcInitializedAsync() As Task(Of Boolean)
        If libVlc IsNot Nothing AndAlso vlc_Media_Player IsNot Nothing Then Return Task.FromResult(True)

        ' Called on the UI thread only, so this needs no lock. A failed attempt is not
        ' cached - the user may install the runtime and try again.
        If vlc_Init_Task Is Nothing OrElse (vlc_Init_Task.IsCompleted AndAlso Not vlc_Init_Task.Result) Then
            vlc_Init_Task = InitializeVlcCoreAsync()
        End If
        Return vlc_Init_Task
    End Function

    ''' <summary>Async so a first-run VLC download runs with the UI thread free to
    ''' pump messages (window stays responsive, repaints, isn't "Not Responding")
    ''' instead of blocking synchronously for the whole download - see w0868 fix
    ''' notes: the old sync wrapper deadlocked outright rather than just looking slow.</summary>
    Private Async Function InitializeVlcCoreAsync() As Task(Of Boolean)
        If Not OptionalRuntimeManager.HasVlcRuntime() Then
            lbl_Status.Text = Localization.T("Установка поддержки VLC..")
        End If

        If Not Await OptionalRuntimeManager.EnsureVlcRuntimeInteractiveAsync(Me) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0868: LibVLC runtime unavailable")
            Return False
        End If

        Try
            Dim vlcDir As String = OptionalRuntimeManager.GetVlcRuntimeDir()
            If vlcDir.Length = 0 Then Return False

            LibVLCSharp.Shared.Core.Initialize(vlcDir)
            libVlc = New LibVLCSharp.Shared.LibVLC("--no-video-title-show", "--no-osd", "--quiet")
            vlc_Media_Player = New LibVLCSharp.Shared.MediaPlayer(libVlc) With {
                .EnableMouseInput = False,
                .EnableKeyInput = False
            }
            AddHandler vlc_Media_Player.Playing, AddressOf Vlc_Media_Player_Playing
#If Not NETFRAMEWORK Then
            AddHandler vlc_Media_Player.EndReached, AddressOf Vlc_Media_Player_EndReached
#End If
            vlc_Video_View = New LibVLCSharp.WinForms.VideoView() With {
                .MediaPlayer = vlc_Media_Player,
                .Visible = False,
                .BackColor = System.Drawing.Color.Black,
                .Location = Picture_Box_1.Location,
                .Size = Picture_Box_1.Size
            }
            AddHandler vlc_Video_View.MouseDoubleClick, AddressOf Vlc_Video_View_MouseDoubleClick
            AddHandler vlc_Video_View.MouseClick, AddressOf Vlc_Video_View_MouseClick
#If Not NETFRAMEWORK Then
            ' Moving the mouse over the picture summons the control bar - the modern
            ' build's stand-in for the transport IE used to draw around a video.
            AddHandler vlc_Video_View.MouseMove, AddressOf Vlc_Video_View_MouseMove
#End If
            ' Host the VLC surface inside the media panel so it shares the same
            ' (panel-relative) coordinate space as the picture boxes.
            If panel_Media IsNot Nothing Then
                panel_Media.Controls.Add(vlc_Video_View)
            Else
                Me.Controls.Add(vlc_Video_View)
            End If
            vlc_Video_View.BringToFront()
            WireVlcSurfaceDragDrop()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0868: LibVLC initialized")
            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0869: LibVLC init failed: " & ex.Message)
            libVlc = Nothing
            vlc_Media_Player = Nothing
            vlc_Video_View = Nothing
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Can VLC be expected to reach this media? For anything on disk (including a UNC
    ''' share, which Windows resolves) File.Exists is the honest answer. A remote MRL
    ''' has no file to stat - only VLC's access plugins can say - so asking File.Exists
    ''' would answer False and drop the request silently. Modern only: the x86 viewer
    ''' has no way to enter a URL (see Main_Form.OpenUrl.vb).
    ''' </summary>
    Private Function CanVlcReachMedia(file_Path As String) As Boolean
        If String.IsNullOrEmpty(file_Path) Then Return False
#If Not NETFRAMEWORK Then
        If IsRemoteMediaUrl(file_Path) Then Return True
#End If
        Return File.Exists(file_Path)
    End Function

    ''' <summary>
    ''' Wraps the media for VLC. A local path becomes file:// via Uri; a remote MRL must
    ''' be passed as a STRING with FromType.FromLocation - New Uri would mangle it and
    ''' strip the very scheme that picks the access plugin.
    ''' </summary>
    Private Function CreateVlcMedia(file_Path As String) As LibVLCSharp.Shared.Media
#If Not NETFRAMEWORK Then
        If IsRemoteMediaUrl(file_Path) Then
            Return New LibVLCSharp.Shared.Media(libVlc, file_Path, LibVLCSharp.Shared.FromType.FromLocation)
        End If
#End If
        Return New LibVLCSharp.Shared.Media(libVlc, New Uri(file_Path))
    End Function

    Private Async Sub PlayVideoWithVlcAsync(file_Path As String)
        If Not CanVlcReachMedia(file_Path) Then Return

        ' Fire-and-forget async: nothing cancels this once it is running, so it has to
        ' notice for itself that it is no longer wanted. Initialising VLC takes a second
        ' (minutes if the runtime has to be downloaded), and the user goes on flipping.
        Dim generation As Integer = media_Generation

        If Not Await EnsureVlcInitializedAsync() Then
            If generation <> media_Generation Then Return
            lbl_Status.Text = OptionalRuntimeManager.GetVlcUnavailableStatusText()
            ' By path, not by the global: the user may be on a picture by now, and the
            ' external player would have opened THAT.
            TryOpenVideoWithDefaultPlayer(file_Path)
            Return
        End If

        ' Too late: an image (or another video) is on screen. Carrying on would raise the
        ' VideoView over it, play the old file, and leave current_Loaded_File_Name naming
        ' this video - so coming back to it later would be skipped as "already loaded".
        If generation <> media_Generation Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0870: stale video play dropped: " & file_Path)
            Return
        End If

        Try
            StopGifLoopPlayback()

            is_WebBrowser_Visible = False
            is_PictureBox1_Visible = False
            is_PictureBox2_Visible = False
            Web_Browser.Visible = False
            Picture_Box_1.Visible = False
            Picture_Box_2.Visible = False
#If NETFRAMEWORK Then
            ' Clear leftover WebBrowser content when VLC takes over. Modern never
            ' navigates the dormant WebBrowser, and even reading DocumentText
            ' would force its IE ActiveX host into existence - so net48 only.
            If Not Web_Browser.DocumentText = "" Then Web_Browser.DocumentText = ""
#End If

            vlc_Video_View.Location = Picture_Box_1.Location
            vlc_Video_View.Size = Picture_Box_1.Size
            vlc_Video_View.Visible = True
            vlc_Video_View.BringToFront()
            ' VLC just took the top of the z-order - reassert the recipients overlay.
            KeepRecipientsOverlayOnTop()

#If Not NETFRAMEWORK Then
            ' A different file starts here: whatever restore the previous one was still
            ' owed must not land on this one.
            ClearPendingVideoPosition()
#End If
            Dim media As LibVLCSharp.Shared.Media = CreateVlcMedia(file_Path)
            If Is_Video_Loop OrElse VideoEndAction() = "repeat" Then media.AddOption(":input-repeat=65535")
#If Not NETFRAMEWORK Then
            pause_New_Video_When_Ready = Not VideoShouldAutoplay()
#End If
            vlc_Media_Player.Play(media)
            media.Dispose()
            ApplyVideoAudioStateToVlc()

            is_Vlc_Playing = True
            current_Loaded_File_Name = file_Path
#If Not NETFRAMEWORK Then
            ' Up front, then it fades out on its own - so a new video announces that it
            ' HAS a transport, instead of leaving the user to discover it by waving the
            ' mouse about.
            ShowVideoControls()
#End If
            Dim shown_Name As String = Path.GetFileName(file_Path)
#If Not NETFRAMEWORK Then
            ' Path.GetFileName on an MRL drags the query string in and returns nothing
            ' at all for an address ending in "/".
            If IsRemoteMediaUrl(file_Path) Then shown_Name = DisplayNameForMrl(file_Path)
#End If
            lbl_Status.Text = Localization.TF("Видео воспроизводится через VLC: {0}", shown_Name)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0871: Playing via LibVLC: " & file_Path)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0872: LibVLC play failed: " & ex.Message)
            StopVlcPlayback()
            TryOpenVideoWithDefaultPlayer(file_Path)
        End Try
    End Sub

    Private Sub StopVlcPlayback()
        If vlc_Media_Player IsNot Nothing AndAlso is_Vlc_Playing Then
            Try
                vlc_Media_Player.Stop()
                vlc_Media_Player.Media = Nothing
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0873: LibVLC stop error: " & ex.Message)
            End Try
        End If
        is_Vlc_Playing = False
        If vlc_Video_View IsNot Nothing Then vlc_Video_View.Visible = False
#If Not NETFRAMEWORK Then
        ' No video, no transport: it must not linger over the next image.
        HideVideoControls()
        ' The track list belongs to the video that just stopped.  Explicitly run
        ' the same visibility rule here (rather than waiting for a future VLC
        ' Playing event), otherwise "Дорожки" remains in the toolbar over the
        ' image that follows it.
        ApplyVideoTracksButtonVisibility()
#End If
    End Sub

    Private Sub Vlc_Video_View_MouseDoubleClick(sender As Object, e As MouseEventArgs)
#If Not NETFRAMEWORK Then
        ' The first click of the pair has already flipped playback - WinForms raises
        ' MouseClick once, then MouseDoubleClick - and a double-click means "full screen",
        ' not "full screen, and also pause". Put it back.
        If e.Button = MouseButtons.Left Then RevertClickPlayPause()
#End If
        HandleWebBrowserDoubleClick()
    End Sub

    Private Sub Vlc_Media_Player_Playing(sender As Object, e As EventArgs)
        ' This event arrives on a libvlc thread. Calling the player's own methods from
        ' inside its callback risks a deadlock in the native layer (all the more so when
        ' the UI thread is doing Stop() at that moment during a fast flip), and
        ' video_Volume_Level / is_Video_Muted are read here as well. BeginInvoke, never
        ' Invoke: waiting for the UI thread from a VLC callback is the deadlock itself.
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(Sub()
                               ApplyVideoAudioStateToVlc()
#If Not NETFRAMEWORK Then
                               ' A video reopened to apply Repeat owes the user the second
                               ' it was at - and this is the first moment it will seek.
                               ApplyPendingVideoPosition()
                               ' Track lists do not exist until VLC is actually playing -
                               ' this event is the first moment they can be read.
                               ApplyPreferredTracks()
                               ApplyVideoTracksButtonVisibility()
                               If pause_New_Video_When_Ready Then
                                   pause_New_Video_When_Ready = False
                                   vlc_Media_Player.Pause()
                                   ShowVideoControls()
                               End If
#End If
                           End Sub)
        Catch
            ' The form can go away between the check and the post - nothing to do.
        End Try
    End Sub

#If Not NETFRAMEWORK Then
    Private Sub Vlc_Media_Player_EndReached(sender As Object, e As EventArgs)
        If VideoEndAction() <> "nextFile" Then Return
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(Sub()
                               If is_Vlc_Playing Then ReadShowMediaFile(Mode_Next)
                           End Sub)
        Catch
        End Try
    End Sub
#End If

    Private Sub Vlc_Video_View_MouseClick(sender As Object, e As MouseEventArgs)
        If vlc_Media_Player Is Nothing Then Return
#If NETFRAMEWORK Then
        If e.Button = System.Windows.Forms.MouseButtons.Right Then
            Try
                If vlc_Media_Player.IsPlaying Then
                    vlc_Media_Player.Pause()
                Else
                    vlc_Media_Player.Play()
                End If
            Catch
            End Try
        End If
#Else
        Select Case e.Button
            Case System.Windows.Forms.MouseButtons.Left
                ' Play/pause moved to the left button - where every video player has put
                ' it. It was on the right until now, and the right button has a menu to
                ' open (Main_Form.VideoMenu.vb). Remember which way it went: a double-click
                ' has to undo it (see Vlc_Video_View_MouseDoubleClick).
                If VideoClickMovesToNextFile() Then
                    ReadShowMediaFile(Mode_Next)
                Else
                    video_Click_Paused = TogglePlayPause()
                    RefreshVideoControlsState()
                    ShowVideoControls()
                End If
            Case System.Windows.Forms.MouseButtons.Right
                ShowVideoContextMenu(vlc_Video_View.PointToScreen(e.Location))
            End Select
#End If
    End Sub

#If NETFRAMEWORK Then
    ' net48 only: the modern build has a single video engine (LibVLC) and its
    ' dispatcher never routes here (SPECIFICATION_DOTNET10_MODERN_BUILD §6.2).
    Private Sub LoadVideoInWebBrowser(video_File_Path As String)
        Try
            StopGifLoopPlayback()
            is_WebBrowser_Visible = False
            is_PictureBox1_Visible = False
            is_PictureBox2_Visible = False

            Dim video_File_As_Uri As New Uri(video_File_Path)
            Dim video_File_Absolute_Uri As String = video_File_As_Uri.AbsoluteUri

            Dim loop_Attribute As String = If(Is_Video_Loop, " loop", "")
            Dim muted_Attribute As String = If(is_Video_Muted, " muted", "")
            Dim muted_Script_Value As String = If(is_Video_Muted, "true", "false")

            Dim text_Color As String = If(Form_Color_Scheme = 0, "white", "black")

            Dim video_Html_Content As String = "<video id='videoPlayer' controls autoplay" & loop_Attribute & muted_Attribute & " style='width:100%;height:calc(100% - " & height_For_instruments_on_WebPanel & "px);object-fit:fill;'" &
                            " onerror=""fmsReportVideoError('Error: Unsupported video type (video element error)');"">" &
                            "<source src='" & video_File_Absolute_Uri & "' onerror=""fmsReportVideoError('Error: Unsupported video type (source failed to load)');"">" &
                            "<track kind='captions' default>" &
                            "<p style='color: " & text_Color & "; text-align: center;'>" &
                            Localization.T("Ваш браузер не поддерживает видео.") & "</p>" &
                            "</video>"

            Dim html As String = "<html><head><meta http-equiv='X-UA-Compatible' content='IE=edge'>" &
                     "<style>" &
                     "body { margin: 0; overflow: hidden; background: " &
                            If(Form_Color_Scheme = 0, "black", "white") & "; }" &
                     "video { width: 100%; height: calc(100% - " & height_For_instruments_on_WebPanel & "px); object-fit: fill; position: absolute; top: 0; left: 0; }" &
                     "</style>" &
                     "<script>" &
                     "var fmsErrorReported = false;" &
                     "function fmsReportVideoError(msg) {" &
                     "  if (fmsErrorReported) return;" &
                     "  fmsErrorReported = true;" &
                     "  try { window.external.HandleVideoError(msg); }" &
                     "  catch(e) {" &
                     "    try {" &
                     "      var v = document.getElementById('videoPlayer');" &
                     "      var p = document.createElement('p');" &
                     "      p.style.color='" & text_Color & "'; p.style.textAlign='center';" &
                     "      p.innerText = msg;" &
                     "      if (v && v.parentNode) v.parentNode.insertBefore(p, v.nextSibling);" &
                     "    } catch(e2) {}" &
                     "  }" &
                     "}" &
                     "function handlePageDoubleClick() { try { window.external.HandleWebBrowserDoubleClick(); } catch(e) {} }" &
                     "</script>" &
                     "</head>" &
                     "<body oncontextmenu='return false;' ondblclick='handlePageDoubleClick();'>" & video_Html_Content &
                     "<script>" &
                     "var player = document.getElementById('videoPlayer');" &
                     "player.volume = " & video_Volume_Level.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) & ";" &
                     "player.muted = " & muted_Script_Value & ";" &
                     "player.oncontextmenu = function(e) { e.preventDefault(); if (this.paused) this.play(); else this.pause(); return false; };" &
                     "player.onvolumechange = function() { try { window.external.SetVideoAudioState(this.volume, this.muted); } catch(e) { } };" &
                     "player.addEventListener('error', function() { fmsReportVideoError('Error: Unsupported video type (media error)'); });" &
                     "player.addEventListener('stalled', function() { setTimeout(fmsWatchdog, 100); });" &
                     "function fmsWatchdog() {" &
                     "  if (fmsErrorReported) return;" &
                     "  if (player.error || player.networkState === 3 || (player.readyState < 1 && player.networkState !== 2)) {" &
                     "    fmsReportVideoError('Error: Unsupported video type or no playable data. Source: ' + (player.currentSrc || 'not found'));" &
                     "  }" &
                     "}" &
                     "setTimeout(fmsWatchdog, 2500);" &
                     "</script></body></html>"

            last_Loaded_Uri = ""
            Web_Browser.DocumentText = html
            current_Loaded_File_Name = Current_File_Name

            is_WebBrowser_Visible = True
            is_PictureBox1_Visible = False
            is_PictureBox2_Visible = False

            UpdateControlVisibility()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0850: WebBrowser is loaded with URI: " & video_File_Absolute_Uri)
        Catch ex As Exception
            Web_Browser.DocumentText = "<html><body style='background:black; color:" &
                            If(Form_Color_Scheme = 0, "black", "white") &
                            "; text-align:center;'><p>Error preparing video player: " & ex.Message & "</p></body></html>"
            is_WebBrowser_Visible = False

            UpdateControlVisibility()
            lbl_Status.Text = Localization.TF("Ошибка загрузки видео: {0}", ex.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0860: Error loading video: " & ex.Message)
        End Try
    End Sub
#End If

End Class
