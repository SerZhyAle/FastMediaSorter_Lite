#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Main_Form
    Private frame_Playback_Mode As Boolean
    Private frame_Auto_Running As Boolean
    Private frame_Session As VideoFrameSession
    Private frame_Surface As PictureBox
    Private frame_Busy As Boolean
    Private frame_Request As Integer
    Private frame_Requested_Time As Long = -1
    Private frame_Ffmpeg_Path As String = ""
    Private WithEvents frame_Playback_Timer As New Timer() With {.Interval = VideoFrameSession.IntervalMs}

    Private Function CanUseFramePlayback() As Boolean
        Return CanUsePlaybackExtras() AndAlso Not IsAudioNowPlaying() AndAlso Not IsAnimationVideoMedia() AndAlso
               File.Exists(current_Loaded_File_Name)
    End Function

    Private Sub EnsureFrameSurface()
        If frame_Surface IsNot Nothing Then Return
        frame_Surface = New PictureBox With {.Dock = DockStyle.Fill, .BackColor = Color.Black,
            .SizeMode = PictureBoxSizeMode.Zoom, .Visible = False, .AllowDrop = True}
        AddHandler frame_Surface.MouseClick, AddressOf Vlc_Video_View_MouseClick
        AddHandler frame_Surface.MouseDoubleClick, AddressOf Vlc_Video_View_MouseDoubleClick
        AddHandler frame_Surface.MouseMove, AddressOf Vlc_Video_View_MouseMove
        AddHandler frame_Surface.MouseWheel, Sub(sender, args) MouseUse(args)
        AddHandler frame_Surface.DragEnter, AddressOf Form1_DragEnter
        AddHandler frame_Surface.DragDrop, AddressOf Form1_DragDrop
        panel_Media.Controls.Add(frame_Surface)
        AdmitLowerIntegrityMessages(frame_Surface)
    End Sub

    Private Async Function OpenFrameSessionAsync(previous As Boolean, request As Integer) As Task(Of Boolean)
        If frame_Session IsNot Nothing Then Return True
        Dim generation As Integer = media_Generation
        Dim path As String = current_Loaded_File_Name
        Dim timeMs As Long = If(frame_Requested_Time >= 0, frame_Requested_Time, Math.Max(0, PlayerTimeMs()))
        frame_Requested_Time = -1
        vlc_Media_Player.SetPause(True)
        If frame_Ffmpeg_Path.Length = 0 Then
            If Not Await OptionalRuntimeManager.EnsureFfmpegRuntimeInteractiveAsync(Me) Then Return False
            frame_Ffmpeg_Path = OptionalRuntimeManager.GetFfmpegPath()
        End If
        If request <> frame_Request OrElse generation <> media_Generation OrElse IsDisposed Then Return False
        Dim session As New VideoFrameSession(frame_Ffmpeg_Path, path)
        frame_Session = session
        Try
            Await session.OpenAsync(timeMs, previous)
            If request <> frame_Request OrElse generation <> media_Generation OrElse IsDisposed OrElse frame_Session IsNot session Then
                If frame_Session Is session Then frame_Session = Nothing
                session.Dispose()
                Return False
            End If
            PresentFrame()
            Return True
        Catch
            If frame_Session Is session Then frame_Session = Nothing
            session.Dispose()
            Throw
        End Try
    End Function

    Private Sub PresentFrame()
        If frame_Session Is Nothing OrElse frame_Session.Current Is Nothing Then Return
        EnsureFrameSurface()
        frame_Surface.Image = frame_Session.Current.Image
        frame_Surface.Visible = True
        frame_Surface.BringToFront()
        If vlc_Video_View IsNot Nothing Then vlc_Video_View.Visible = False
        frame_Session.ReleaseRetired()
        KeepRecipientsOverlayOnTop()
        RefreshVideoControlsState()
    End Sub

    Private Async Sub StartFramePlayback()
        If Not CanUseFramePlayback() OrElse frame_Busy Then Return
        frame_Request += 1
        Dim request As Integer = frame_Request
        frame_Busy = True
        SlideShowStop()
        Try
            If Not Await OpenFrameSessionAsync(previous:=False, request:=request) Then Return
            frame_Playback_Mode = True
            frame_Auto_Running = True
            vlc_Media_Player.Mute = True
            frame_Playback_Timer.Start()
            RefreshPlaybackSpeedButton()
            ShowVideoControls()
            lbl_Status.Text = Localization.T("Покадрово: 1 кадр в секунду, без звука")
        Catch ex As Exception
            If request = frame_Request Then
                AppFileLogger.LogException("Frame playback could not start", ex)
                LeaveFramePlayback()
                lbl_Status.Text = Localization.T("Не удалось прочитать кадры видео")
            End If
        Finally
            If request = frame_Request Then frame_Busy = False
        End Try
    End Sub

    ''' <summary>Retire the borrowed preview before disposing its owner. The native
    ''' player stays on the same media, so tracks, volume and rate survive the inspection.</summary>
    Private Sub LeaveFramePlayback(Optional resumePlayback As Boolean = False)
        frame_Request += 1
        frame_Playback_Timer.Stop()
        frame_Busy = False
        Dim timeMs As Long = If(frame_Session IsNot Nothing AndAlso frame_Session.Current IsNot Nothing,
                               frame_Session.Current.TimeMs, -1L)
        frame_Playback_Mode = False
        frame_Auto_Running = False
        frame_Requested_Time = -1
        If frame_Surface IsNot Nothing Then
            frame_Surface.Image = Nothing
            frame_Surface.Visible = False
        End If
        If frame_Session IsNot Nothing Then frame_Session.Dispose() : frame_Session = Nothing
        If vlc_Video_View IsNot Nothing AndAlso Not vlc_Video_View.IsDisposed AndAlso is_Vlc_Playing AndAlso Not IsAudioNowPlaying() Then vlc_Video_View.Visible = True
        If vlc_Media_Player Is Nothing Then Return
        Try
            If timeMs >= 0 Then vlc_Media_Player.Time = timeMs
            ApplyVideoAudioStateToVlc()
            If resumePlayback Then vlc_Media_Player.SetPause(False)
        Catch ex As Exception
            AppFileLogger.LogException("Native playback could not be restored after frame inspection", ex)
        End Try
    End Sub

    Private Async Sub RunPreviousFrame()
        Await StepDecodedFrameAsync(forward:=False)
    End Sub

    Private Async Sub RunDecodedNextFrame()
        Await StepDecodedFrameAsync(forward:=True)
    End Sub

    Private Async Function StepDecodedFrameAsync(forward As Boolean) As Task
        If Not CanUseFramePlayback() OrElse frame_Busy Then Return
        frame_Playback_Timer.Stop()
        frame_Auto_Running = False
        frame_Request += 1
        Dim request As Integer = frame_Request
        frame_Busy = True
        SlideShowStop()
        Try
            If frame_Session Is Nothing Then
                If Not Await OpenFrameSessionAsync(previous:=Not forward, request:=request) Then Return
                If Not forward Then Return
            End If
            Dim session As VideoFrameSession = frame_Session
            Await session.MoveAsync(forward)
            If request = frame_Request AndAlso frame_Session Is session Then PresentFrame()
        Catch ex As Exception
            If request = frame_Request Then
                AppFileLogger.LogException("Video frame step failed", ex)
                lbl_Status.Text = Localization.T("Не удалось прочитать кадры видео")
            End If
        Finally
            If request = frame_Request Then
                frame_Busy = False
                ShowVideoControls()
            End If
        End Try
    End Function

    Private Async Sub Frame_Playback_Timer_Tick(sender As Object, e As EventArgs) Handles frame_Playback_Timer.Tick
        If Not frame_Playback_Mode OrElse Not frame_Auto_Running OrElse frame_Busy OrElse frame_Session Is Nothing Then Return
        frame_Playback_Timer.Stop()
        Dim session As VideoFrameSession = frame_Session
        Dim request As Integer = frame_Request
        frame_Busy = True
        Try
            Dim moved As Boolean = Await session.MoveAsync(forward:=True)
            If request <> frame_Request OrElse frame_Session IsNot session Then Return
            If Not moved Then frame_Auto_Running = False
            PresentFrame()
            ' Start the next one-second hold only after the requested image is ready.
            If moved AndAlso frame_Auto_Running Then frame_Playback_Timer.Start()
        Catch ex As Exception
            If request = frame_Request Then
                frame_Auto_Running = False
                AppFileLogger.LogException("Frame playback stopped", ex)
                lbl_Status.Text = Localization.T("Не удалось прочитать кадры видео")
                RefreshVideoControlsState()
            End If
        Finally
            If request = frame_Request Then frame_Busy = False
        End Try
    End Sub

    Private Function FramePlaybackCaption() As String
        Return If(frame_Playback_Mode, Localization.T("Покадрово"), PlaybackRules.FormatRate(playback_Rate))
    End Function
End Class
#End If
