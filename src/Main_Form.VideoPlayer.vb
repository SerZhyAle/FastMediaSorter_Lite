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

    Private Sub TryOpenVideoWithDefaultPlayer()
        Try
            If Not String.IsNullOrEmpty(Current_File_Name) AndAlso File.Exists(Current_File_Name) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0866: Opening video with default player: " & Current_File_Name)

#If NETFRAMEWORK Then
                Web_Browser.DocumentText = "<html><body style='background:" &
                                        If(Form_Color_Scheme = 0, "black", "white") &
                                        "; color:" & If(Form_Color_Scheme = 0, "white", "black") &
                                        "; text-align:center; font-family:Arial;'>" &
                                        "<h3>" & If(Is_Russian_Language, "Видео открыто во внешнем плеере", "Video opened in external player") & "</h3>" &
                                        "<p>" & Path.GetFileName(Current_File_Name) & "</p>" &
                                        "<p style='font-size:12px; color:gray;'>" &
                                        If(Is_Russian_Language, "Нажмите стрелки для перехода к следующему файлу", "Use arrow keys to navigate to next file") &
                                        "</p></body></html>"
#End If

                ' Explicit UseShellExecute: opening a document needs the shell; net48
                ' defaulted to True, .NET defaults to False. On the modern build this
                ' is the ONLY video fallback when LibVLC is unavailable.
                Process.Start(New ProcessStartInfo(Current_File_Name) With {.UseShellExecute = True})

                lbl_Status.Text = If(Is_Russian_Language, "Видео открыто во внешнем плеере: " & Path.GetFileName(Current_File_Name), "Video opened in external player: " & Path.GetFileName(Current_File_Name))

            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0867: Error opening video with default player: " & ex.Message)
            lbl_Status.Text = If(Is_Russian_Language, "Ошибка запуска внешнего плеера: " & ex.Message, "Error launching external player: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Async so a first-run VLC download runs with the UI thread free to
    ''' pump messages (window stays responsive, repaints, isn't "Not Responding")
    ''' instead of blocking synchronously for the whole download - see w0868 fix
    ''' notes: the old sync wrapper deadlocked outright rather than just looking slow.</summary>
    Private Async Function EnsureVlcInitializedAsync() As Task(Of Boolean)
        If libVlc IsNot Nothing AndAlso vlc_Media_Player IsNot Nothing Then Return True
        is_Vlc_Init_Attempted = True

        If Not OptionalRuntimeManager.HasVlcRuntime() Then
            lbl_Status.Text = If(Is_Russian_Language, "Установка поддержки VLC..", "Installing VLC support..")
        End If

        If Not Await OptionalRuntimeManager.EnsureVlcRuntimeInteractiveAsync(Me, Is_Russian_Language) Then
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
            vlc_Video_View = New LibVLCSharp.WinForms.VideoView() With {
                .MediaPlayer = vlc_Media_Player,
                .Visible = False,
                .BackColor = System.Drawing.Color.Black,
                .Location = Picture_Box_1.Location,
                .Size = Picture_Box_1.Size
            }
            AddHandler vlc_Video_View.MouseDoubleClick, AddressOf Vlc_Video_View_MouseDoubleClick
            AddHandler vlc_Video_View.MouseClick, AddressOf Vlc_Video_View_MouseClick
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

    Private Async Sub PlayVideoWithVlcAsync(file_Path As String)
        If String.IsNullOrEmpty(file_Path) OrElse Not File.Exists(file_Path) Then Return

        If Not Await EnsureVlcInitializedAsync() Then
            lbl_Status.Text = OptionalRuntimeManager.GetVlcUnavailableStatusText(Is_Russian_Language)
            TryOpenVideoWithDefaultPlayer()
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

            Dim media As New LibVLCSharp.Shared.Media(libVlc, New Uri(file_Path))
            If Is_Video_Loop Then media.AddOption(":input-repeat=65535")
            vlc_Media_Player.Play(media)
            media.Dispose()
            ApplyVideoAudioStateToVlc()

            is_Vlc_Playing = True
            current_Loaded_File_Name = file_Path
            lbl_Status.Text = If(Is_Russian_Language,
                                 "Видео воспроизводится через VLC: " & Path.GetFileName(file_Path),
                                 "Playing via VLC: " & Path.GetFileName(file_Path))
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0871: Playing via LibVLC: " & file_Path)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0872: LibVLC play failed: " & ex.Message)
            StopVlcPlayback()
            TryOpenVideoWithDefaultPlayer()
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
    End Sub

    Private Sub Vlc_Video_View_MouseDoubleClick(sender As Object, e As MouseEventArgs)
        HandleWebBrowserDoubleClick()
    End Sub

    Private Sub Vlc_Media_Player_Playing(sender As Object, e As EventArgs)
        ApplyVideoAudioStateToVlc()
    End Sub

    Private Sub Vlc_Video_View_MouseClick(sender As Object, e As MouseEventArgs)
        If e.Button = System.Windows.Forms.MouseButtons.Right AndAlso vlc_Media_Player IsNot Nothing Then
            Try
                If vlc_Media_Player.IsPlaying Then
                    vlc_Media_Player.Pause()
                Else
                    vlc_Media_Player.Play()
                End If
            Catch
            End Try
        End If
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
                            If(Is_Russian_Language, "Ваш браузер не поддерживает видео.", "Your browser does not support video.") & "</p>" &
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
            lbl_Status.Text = If(Is_Russian_Language, "Ошибка загрузки видео: " & ex.Message, "Error loading video: " & ex.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0860: Error loading video: " & ex.Message)
        End Try
    End Sub
#End If

End Class
