#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' The video control bar - modern build only, and this one is not a matter of taste.
'
' On net48 a video goes to the IE WebBrowser as an HTML5 <video controls> element, so
' Internet Explorer draws the whole transport for us: seek bar, clock, volume, play and
' pause, appearing on mouse move and fading away again. The modern build has a single
' engine - LibVLC - which renders nothing but the picture: it is a decoder with a
' window, not a player UI. So the .NET 10 viewer showed bare video with no way to seek
' or change the volume (a right-click paused, and that was the lot).
'
' This file gives it back, natively: a strip along the bottom of the media panel that
' shows itself when the mouse moves over the video and hides a couple of seconds later
' while playback runs - the same rhythm the IE controls had. It is deliberately NOT a
' port of VLC's own interface: only what the old bar offered.
'
' Structure: the bar is a child of panel_Media (the same pattern the recipients overlay
' uses, and the reason KeepRecipientsOverlayOnTop exists) - a sibling of the VideoView
' standing above it in z-order, not a child of it. Sibling clipping is what keeps it
' visible over VLC's output window.
Partial Public Class Main_Form

    Private video_Controls As Panel
    Private WithEvents btn_Video_Play As NonFocusButton
    Private WithEvents btn_Video_Mute As NonFocusButton
    Private WithEvents video_Seek As FlatSlider
    Private WithEvents video_Volume As FlatSlider
    Private lbl_Video_Time As Label

    ''' <summary>Drives the seek bar and the clock while a video plays.</summary>
    Private WithEvents video_Controls_Timer As New Timer() With {.Interval = 250}

    ''' <summary>Counts down to hiding the bar after the mouse stops moving - the same
    ''' behaviour the IE controls had.</summary>
    Private WithEvents video_Controls_Hide_Timer As New Timer() With {.Interval = 2500}

    ''' <summary>Segoe MDL2 Assets is Windows' own icon font and has shipped since
    ''' Windows 10 RTM - below the modern build's floor (1607), so it is always there.
    ''' The app pins Microsoft Sans Serif 8.25 for its Designer layout, and that font
    ''' has no transport glyphs at all, hence an explicit font here.</summary>
    Private Const Glyph_Font As String = "Segoe MDL2 Assets"
    Private Const Glyph_Play As String = ChrW(&HE768)
    Private Const Glyph_Pause As String = ChrW(&HE769)
    Private Const Glyph_Volume As String = ChrW(&HE767)
    Private Const Glyph_Mute As String = ChrW(&HE74F)

    ''' <summary>Builds the bar on first use and makes sure it is parented. Lazy on
    ''' purpose: most sessions are images only and never pay for it.</summary>
    Private Sub EnsureVideoControls()
        If panel_Media Is Nothing Then Return

        If video_Controls Is Nothing Then
            Try
                BuildVideoControls()
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2500: video controls build failed: " & ex.Message)
                video_Controls = Nothing
                Return
            End Try
        End If

        If video_Controls IsNot Nothing AndAlso video_Controls.Parent Is Nothing Then
            panel_Media.Controls.Add(video_Controls)
        End If
    End Sub

    Private Sub BuildVideoControls()
        Dim pad As Integer = LogicalToDeviceUnits(8)
        Dim bar_H As Integer = LogicalToDeviceUnits(38)
        Dim btn_W As Integer = LogicalToDeviceUnits(34)
        Dim glyph As New Font(Glyph_Font, 10.0F)

        video_Controls = New Panel With {
            .Name = "video_Controls",
            .BackColor = Color.FromArgb(28, 28, 28),
            .Height = bar_H,
            .Visible = False
        }

        ' Every part is named: WinForms passes Control.Name through as the UIA
        ' AutomationId, which is what a screen reader - and the UI test that drives this
        ' bar - uses to find it.
        btn_Video_Play = NewBarButton("btn_Video_Play", Glyph_Play, glyph, btn_W, bar_H)
        btn_Video_Play.Location = New Point(0, 0)
        video_Controls.Controls.Add(btn_Video_Play)

        btn_Video_Mute = NewBarButton("btn_Video_Mute", Glyph_Volume, glyph, btn_W, bar_H)
        video_Controls.Controls.Add(btn_Video_Mute)

        video_Seek = New FlatSlider With {
            .Name = "video_Seek",
            .AccessibleRole = AccessibleRole.Slider,
            .Height = LogicalToDeviceUnits(18),
            .BackColor = video_Controls.BackColor
        }
        video_Controls.Controls.Add(video_Seek)

        video_Volume = New FlatSlider With {
            .Name = "video_Volume",
            .AccessibleRole = AccessibleRole.Slider,
            .Height = LogicalToDeviceUnits(18),
            .Width = LogicalToDeviceUnits(80),
            .BackColor = video_Controls.BackColor,
            .FillColor = Color.FromArgb(160, 160, 160)
        }
        video_Volume.Value = video_Volume_Level
        video_Controls.Controls.Add(video_Volume)

        lbl_Video_Time = New Label With {
            .Name = "lbl_Video_Time",
            .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleCenter,
            .ForeColor = Color.Gainsboro,
            .BackColor = video_Controls.BackColor,
            .Width = LogicalToDeviceUnits(96),
            .Height = bar_H,
            .Text = "--:-- / --:--"
        }
        video_Controls.Controls.Add(lbl_Video_Time)

        ' Any mouse activity anywhere on the bar keeps it up - otherwise it would slide
        ' out from under the hand that is reaching for it.
        AddHandler video_Controls.MouseMove, AddressOf VideoControls_MouseMove
        For Each c As Control In video_Controls.Controls
            AddHandler c.MouseMove, AddressOf VideoControls_MouseMove
        Next

        LayoutVideoControlsRow(pad)
        LocalizeVideoControls()
    End Sub

    Private Function NewBarButton(name As String, text As String, glyph_Font As Font, w As Integer, h As Integer) As NonFocusButton
        Dim b As New NonFocusButton() With {
            .Name = name,
            .Text = text,
            .Font = glyph_Font,
            .FlatStyle = FlatStyle.Flat,
            .ForeColor = Color.Gainsboro,
            .BackColor = Color.FromArgb(28, 28, 28),
            .UseVisualStyleBackColor = False,
            .Size = New Size(w, h),
            .TabStop = False
        }
        b.FlatAppearance.BorderSize = 0
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60)
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(88, 88, 88)
        Return b
    End Function

    ''' <summary>Places the row: play | seek (stretches) | clock | mute | volume.</summary>
    Private Sub LayoutVideoControlsRow(pad As Integer)
        If video_Controls Is Nothing Then Return

        Dim h As Integer = video_Controls.Height
        Dim x As Integer = 0
        btn_Video_Play.Location = New Point(x, 0)
        x += btn_Video_Play.Width + pad

        Dim right As Integer = video_Controls.Width
        video_Volume.Location = New Point(right - video_Volume.Width - pad, (h - video_Volume.Height) \ 2)
        btn_Video_Mute.Location = New Point(video_Volume.Left - btn_Video_Mute.Width, 0)
        lbl_Video_Time.Location = New Point(btn_Video_Mute.Left - lbl_Video_Time.Width - pad, 0)

        Dim seek_W As Integer = lbl_Video_Time.Left - pad - x
        If seek_W < LogicalToDeviceUnits(40) Then seek_W = LogicalToDeviceUnits(40)
        video_Seek.Location = New Point(x, (h - video_Seek.Height) \ 2)
        video_Seek.Width = seek_W
    End Sub

    ''' <summary>Pins the bar to the bottom of the media panel. Called from
    ''' SyncMediaSurface, so it follows every resize and full-screen switch.</summary>
    Friend Sub PositionVideoControls()
        If video_Controls Is Nothing OrElse panel_Media Is Nothing Then Return
        Dim margin As Integer = LogicalToDeviceUnits(10)
        Dim w As Integer = panel_Media.ClientSize.Width - margin * 2
        If w < LogicalToDeviceUnits(200) Then w = Math.Max(0, panel_Media.ClientSize.Width)
        video_Controls.Width = w
        video_Controls.Location = New Point((panel_Media.ClientSize.Width - w) \ 2,
                                            panel_Media.ClientSize.Height - video_Controls.Height - margin)
        LayoutVideoControlsRow(LogicalToDeviceUnits(8))
        If video_Controls.Visible Then video_Controls.BringToFront()
    End Sub

    Friend Sub LocalizeVideoControls()
        If video_Controls Is Nothing Then Return

        Dim seek_Text As String = Localization.T("Перемотка")
        Dim volume_Text As String = Localization.T("Громкость")
        Dim play_Text As String = Localization.T("Пауза / продолжить (клик по видео - то же самое; правая кнопка - меню)")
        Dim mute_Text As String = Localization.T("Звук вкл/выкл")

        If toolTip IsNot Nothing Then
            toolTip.SetToolTip(btn_Video_Play, play_Text)
            toolTip.SetToolTip(btn_Video_Mute, mute_Text)
            toolTip.SetToolTip(video_Seek, seek_Text)
            toolTip.SetToolTip(video_Volume, volume_Text)
        End If

        ' The buttons' captions are icon-font glyphs from the private use area - a screen
        ' reader would announce garbage. These names are what it says instead (and what
        ' the UI test finds them by).
        btn_Video_Play.AccessibleName = Localization.T("Пауза / продолжить")
        btn_Video_Mute.AccessibleName = mute_Text
        video_Seek.AccessibleName = seek_Text
        video_Volume.AccessibleName = volume_Text
    End Sub

    ''' <summary>Brings the bar up and restarts the countdown to hiding it.</summary>
    Friend Sub ShowVideoControls()
        If Not is_Vlc_Playing Then Return
        EnsureVideoControls()
        If video_Controls Is Nothing Then Return

        PositionVideoControls()
        If Not video_Controls.Visible Then
            video_Controls.Visible = True
            RefreshVideoControlsState()
        End If
        video_Controls.BringToFront()
        ' The recipients overlay must stay clickable above us.
        KeepRecipientsOverlayOnTop()

        video_Controls_Timer.Start()
        video_Controls_Hide_Timer.Stop()
        video_Controls_Hide_Timer.Interval = VideoControlsHideDelayMilliseconds()
        video_Controls_Hide_Timer.Start()
    End Sub

    Friend Sub HideVideoControls()
        video_Controls_Hide_Timer.Stop()
        video_Controls_Timer.Stop()
        If video_Controls IsNot Nothing Then video_Controls.Visible = False
    End Sub

    Private Sub Video_Controls_Hide_Timer_Tick(sender As Object, e As EventArgs) Handles video_Controls_Hide_Timer.Tick
        video_Controls_Hide_Timer.Stop()
        If video_Controls Is Nothing OrElse Not video_Controls.Visible Then Return

        ' Never disappear while the user is holding a slider, while the pointer rests on
        ' the bar, or while playback is paused - a paused video with no transport is
        ' exactly the dead end this file exists to remove.
        If video_Seek.IsDragging OrElse video_Volume.IsDragging Then
            video_Controls_Hide_Timer.Start()
            Return
        End If
        If video_Controls.Bounds.Contains(panel_Media.PointToClient(Cursor.Position)) Then
            video_Controls_Hide_Timer.Start()
            Return
        End If
        If KeepVideoControlsVisibleWhilePaused() AndAlso Not IsVlcActuallyPlaying() Then
            video_Controls_Hide_Timer.Start()
            Return
        End If

        video_Controls.Visible = False
    End Sub

    Private Sub VideoControls_MouseMove(sender As Object, e As MouseEventArgs)
        ' Restart the countdown, but do not rebuild/reposition on every pixel.
        If video_Controls IsNot Nothing AndAlso video_Controls.Visible Then
            video_Controls_Hide_Timer.Stop()
            video_Controls_Hide_Timer.Start()
        End If
    End Sub

    ''' <summary>Hooked onto the VLC surface: moving the mouse over the picture is what
    ''' summons the bar, exactly as it did in the IE player.</summary>
    Private Sub Vlc_Video_View_MouseMove(sender As Object, e As MouseEventArgs)
        RevealSlideshowChromeTemporarily()
        ShowVideoControls()
    End Sub

    Private Function IsVlcActuallyPlaying() As Boolean
        If vlc_Media_Player Is Nothing Then Return False
        Try
            Return vlc_Media_Player.IsPlaying
        Catch
            Return False
        End Try
    End Function

    Private Sub Video_Controls_Timer_Tick(sender As Object, e As EventArgs) Handles video_Controls_Timer.Tick
        If Not is_Vlc_Playing OrElse vlc_Media_Player Is Nothing Then
            video_Controls_Timer.Stop()
            Return
        End If
        If video_Controls Is Nothing OrElse Not video_Controls.Visible Then Return
        RefreshVideoControlsState()
    End Sub

    ''' <summary>Pulls the live state out of the player and into the bar.</summary>
    Private Sub RefreshVideoControlsState()
        If video_Controls Is Nothing OrElse vlc_Media_Player Is Nothing Then Return

        Try
            ' Not while the user is holding it - the tick would fight the drag.
            If Not video_Seek.IsDragging Then video_Seek.Value = vlc_Media_Player.Position

            Dim pos_Ms As Long = vlc_Media_Player.Time
            Dim len_Ms As Long = vlc_Media_Player.Length
            lbl_Video_Time.Text = FormatMediaTime(pos_Ms) & " / " & FormatMediaTime(len_Ms)

            btn_Video_Play.Text = If(IsVlcActuallyPlaying(), Glyph_Pause, Glyph_Play)
            btn_Video_Mute.Text = If(is_Video_Muted, Glyph_Mute, Glyph_Volume)
            If Not video_Volume.IsDragging Then video_Volume.Value = If(is_Video_Muted, 0.0, video_Volume_Level)
        Catch ex As Exception
            ' The player can be torn down under us between the check and the call.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2501: video controls refresh skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>mm:ss, or h:mm:ss once it is worth it. A stream of unknown length
    ''' reports -1 - show dashes rather than a bogus clock.</summary>
    Private Shared Function FormatMediaTime(ms As Long) As String
        If ms < 0 Then Return "--:--"
        Dim t As TimeSpan = TimeSpan.FromMilliseconds(ms)
        If t.TotalHours >= 1 Then Return CInt(Math.Floor(t.TotalHours)).ToString() & ":" & t.Minutes.ToString("00") & ":" & t.Seconds.ToString("00")
        Return t.Minutes.ToString("00") & ":" & t.Seconds.ToString("00")
    End Function

    Private Sub btn_Video_Play_Click(sender As Object, e As EventArgs) Handles btn_Video_Play.Click
        TogglePlayPause()
        RefreshVideoControlsState()
        ShowVideoControls()
    End Sub

    ''' <summary>The one place that flips playback, shared with the click on the video
    ''' surface and the menu item. Returns True when it PAUSED - the click handler needs to
    ''' know which way it went, so a double-click can put it back.</summary>
    Friend Function TogglePlayPause() As Boolean
        If vlc_Media_Player Is Nothing Then Return False
        Try
            If vlc_Media_Player.IsPlaying Then
                vlc_Media_Player.Pause()
                Return True
            End If
            vlc_Media_Player.Play()
            Return False
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2502: play/pause failed: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Which way the last click on the video flipped playback.</summary>
    Private video_Click_Paused As Boolean = False

    ''' <summary>
    ''' Undoes exactly what the click that started a double-click did. Explicitly, via
    ''' SetPause, rather than by toggling a second time: libvlc's own IsPlaying has not
    ''' necessarily caught up with the first click a few milliseconds ago, and a toggle
    ''' reading that stale value would pause twice and leave the video dead on screen.
    ''' </summary>
    Private Sub RevertClickPlayPause()
        If vlc_Media_Player Is Nothing Then Return
        Try
            vlc_Media_Player.SetPause(Not video_Click_Paused)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2504: play/pause revert failed: " & ex.Message)
        End Try
        RefreshVideoControlsState()
    End Sub

    Private Sub btn_Video_Mute_Click(sender As Object, e As EventArgs) Handles btn_Video_Mute.Click
        ' Through SetVideoAudioState, so the bar and the settings window stay one truth
        ' (and the choice is saved on exit like any other).
        SetVideoAudioState(video_Volume_Level, Not is_Video_Muted)
        RefreshVideoControlsState()
        ShowVideoControls()
    End Sub

    ''' <summary>Seeking is committed on release, and on a plain click anywhere on the
    ''' track. Following every pixel of the drag would have VLC re-buffering all the way
    ''' across the file.</summary>
    Private Sub video_Seek_Scrubbed(sender As Object, e As EventArgs) Handles video_Seek.Scrubbed
        If vlc_Media_Player Is Nothing Then Return
        Try
            vlc_Media_Player.Position = CSng(video_Seek.Value)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2503: seek failed: " & ex.Message)
        End Try
        ShowVideoControls()
    End Sub

    ''' <summary>While dragging, the clock follows the knob - that is what makes it
    ''' possible to aim.</summary>
    Private Sub video_Seek_Scrubbing(sender As Object, e As EventArgs) Handles video_Seek.Scrubbing
        If vlc_Media_Player Is Nothing OrElse lbl_Video_Time Is Nothing Then Return
        Try
            Dim len_Ms As Long = vlc_Media_Player.Length
            If len_Ms > 0 Then
                lbl_Video_Time.Text = FormatMediaTime(CLng(len_Ms * video_Seek.Value)) & " / " & FormatMediaTime(len_Ms)
            End If
        Catch
        End Try
    End Sub

    Private Sub video_Volume_Scrubbing(sender As Object, e As EventArgs) Handles video_Volume.Scrubbing
        ' Volume follows the finger live: it is cheap and the point is to hear it.
        SetVideoAudioState(video_Volume.Value, False)
        btn_Video_Mute.Text = Glyph_Volume
    End Sub

    ''' <summary>
    ''' A flat slider - the track, the part played, and a knob. Used for both seeking and
    ''' volume. A WinForms TrackBar would look like a 1998 dialog bolted onto a video and
    ''' cannot be restyled; this is ~60 lines and matches the rest of the chrome.
    ''' </summary>
    Private NotInheritable Class FlatSlider
        Inherits Control

        Private _value As Double = 0.0
        Private _dragging As Boolean = False

        ''' <summary>The user moved the knob and is still holding it.</summary>
        Public Event Scrubbing As EventHandler
        ''' <summary>The user let go - this is the moment to act on the value.</summary>
        Public Event Scrubbed As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
            ' Never take focus: the app is keyboard-driven and the media surface must
            ' keep it (same reason as NonFocusButton).
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
        End Sub

        ''' <summary>0..1.</summary>
        Public Property Value As Double
            Get
                Return _value
            End Get
            Set(v As Double)
                Dim clamped As Double = Math.Max(0.0, Math.Min(1.0, v))
                If Math.Abs(clamped - _value) < 0.0000001 Then Return
                _value = clamped
                Invalidate()
            End Set
        End Property

        Public ReadOnly Property IsDragging As Boolean
            Get
                Return _dragging
            End Get
        End Property

        Public Property TrackColor As Color = Color.FromArgb(80, 80, 80)
        Public Property FillColor As Color = Color.FromArgb(0, 150, 215)

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(BackColor)
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

            Dim track_H As Int32 = Math.Max(3, LogicalToDeviceUnits(4))
            Dim y As Int32 = (ClientSize.Height - track_H) \ 2
            Dim w As Int32 = ClientSize.Width
            If w <= 0 Then Return

            Using b As New SolidBrush(TrackColor)
                e.Graphics.FillRectangle(b, 0, y, w, track_H)
            End Using

            Dim fill As Int32 = CInt(w * _value)
            If fill > 0 Then
                Using b As New SolidBrush(FillColor)
                    e.Graphics.FillRectangle(b, 0, y, fill, track_H)
                End Using
            End If

            Dim r As Int32 = Math.Max(4, LogicalToDeviceUnits(5))
            Dim cx As Int32 = Math.Max(r, Math.Min(w - r, fill))
            Using b As New SolidBrush(Color.White)
                e.Graphics.FillEllipse(b, cx - r, ClientSize.Height \ 2 - r, r * 2, r * 2)
            End Using
        End Sub

        Private Sub SetFromMouse(x As Integer)
            If ClientSize.Width <= 0 Then Return
            Value = x / CDbl(ClientSize.Width)
            RaiseEvent Scrubbing(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            If e.Button = MouseButtons.Left Then
                _dragging = True
                Capture = True
                SetFromMouse(e.X)
            End If
            MyBase.OnMouseDown(e)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            If _dragging Then SetFromMouse(e.X)
            MyBase.OnMouseMove(e)
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            If _dragging AndAlso e.Button = MouseButtons.Left Then
                _dragging = False
                Capture = False
                SetFromMouse(e.X)
                RaiseEvent Scrubbed(Me, EventArgs.Empty)
            End If
            MyBase.OnMouseUp(e)
        End Sub

    End Class

End Class
#End If
