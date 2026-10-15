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
'
' Every DECISION of the bar (the row's arithmetic, the clock, the seek hold, what keeps it up,
' the buffering steps, the ring's order) is a pure rule in TransportRules.vb with a test; this
' file reads the live state, asks, and applies the answer (ticket 121, section 7).
Partial Public Class Main_Form

    Private video_Controls As Panel
    Private video_Chrome_Host As ChromeHost
    Private WithEvents btn_Video_Play As NonFocusButton
    Private WithEvents btn_Video_Mute As NonFocusButton
    ''' <summary>The speed button of 119 Ф-1 - a text button ("1x", "1.25x"), the only
    ''' caption on the bar; its click opens the ladder menu.</summary>
    Private WithEvents btn_Video_Speed As NonFocusButton
    ''' <summary>The "more operations" button (121 Ф-2): it opens the SAME menu the right click on the video opens.</summary>
    Private WithEvents btn_Video_Menu As NonFocusButton
    Private WithEvents video_Seek As FlatSlider
    Private WithEvents video_Volume As FlatSlider
    Private WithEvents lbl_Video_Time As BarLabel
    ''' <summary>"A" / "A-B" while a repeat span is marked (121 UX-412); empty and hidden otherwise.</summary>
    Private lbl_Video_Ab As BarLabel

    ''' <summary>Drives the seek bar and the clock while a video plays.</summary>
    Private WithEvents video_Controls_Timer As New Timer() With {.Interval = 250}

    ''' <summary>Counts down to hiding the bar after the mouse stops moving - the same
    ''' behaviour the IE controls had.</summary>
    Private WithEvents video_Controls_Hide_Timer As New Timer() With {.Interval = 2500}

    ''' <summary>The clock shows "-remaining / total" instead of "elapsed / total" (121 UX-410); a click toggles it.</summary>
    Private video_Clock_Remaining As Boolean

    ''' <summary>The widest clock text the label was last sized for (it follows the file's length).</summary>
    Private video_Clock_Sample As String = ""
    Private video_Clock_Width As Integer

    ''' <summary>The speed button's width, measured from its widest caption; 0 = measure again.</summary>
    Private video_Speed_Width As Integer

    ''' <summary>Until when (monotonic ms) the 250 ms tick may not write the player's position into the seek thumb.</summary>
    Private video_Seek_Hold_Until_Ms As Long

    ''' <summary>The control the F10 ring stands on inside the bar, Nothing when it is elsewhere.</summary>
    Private video_Ring_Control As Control

    Private video_Speed_Menu As ContextMenuStrip

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
            video_Chrome_Host = New ChromeHost(False) With {.Name = "chrome_Transport", .Owner = Me}
            video_Chrome_Host.Controls.Add(video_Controls)
            ConfigureFloatingMediaChrome()
        End If
    End Sub

    Private Sub BuildVideoControls()
        Dim pad As Integer = UiScale.Px(ChromeMetrics.ScreenMargin, ChromeScale())
        Dim bar_H As Integer = UiScale.Px(ChromeMetrics.TransportHeight, ChromeScale())
        Dim btn_W As Integer = UiScale.Px(ChromeMetrics.TransportButtonWidth, ChromeScale())

        ' Pinned left-to-right (APP-BEHAVIOUR rule 8, ticket 028 Ф-7). The main window takes
        ' RightToLeft from Arabic and Urdu and a child inherits it; a transport bar must not:
        ' time runs left to right on every player, and a mirrored Play triangle reads as
        ' Rewind. Declared here, on the bar, so the "12:34 / 56:78" label keeps its order too.
        ' The colours below are placeholders: ConfigureFloatingMediaChrome paints the bar from the
        ' chrome theme (121 UX-403) the moment the bar is parented.
        video_Controls = New Panel With {
            .Name = "video_Controls",
            .BackColor = Color.FromArgb(28, 28, 28),
            .Height = bar_H,
            .Visible = False,
            .RightToLeft = RightToLeft.No
        }

        ' Every part is named: WinForms passes Control.Name through as the UIA
        ' AutomationId, which is what a screen reader - and the UI test that drives this
        ' bar - uses to find it.
        btn_Video_Play = NewBarButton("btn_Video_Play", btn_W, bar_H)
        btn_Video_Play.Location = New Point(0, 0)
        video_Controls.Controls.Add(btn_Video_Play)

        btn_Video_Mute = NewBarButton("btn_Video_Mute", btn_W, bar_H)
        video_Controls.Controls.Add(btn_Video_Mute)
        SetVideoGlyph(btn_Video_Play, "media.play")
        SetVideoGlyph(btn_Video_Mute, "media.volume")

        ' The track and the slider's own hit zone take the bar's full height (UX-409): the painted
        ' track is a few pixels, the target the pointer has to find is not.
        video_Seek = New FlatSlider With {
            .Name = "video_Seek",
            .AccessibleRole = AccessibleRole.Slider,
            .Height = bar_H,
            .BackColor = video_Controls.BackColor
        }
        video_Seek.ValueTextProvider = AddressOf SeekValueText
        video_Controls.Controls.Add(video_Seek)

        video_Volume = New FlatSlider With {
            .Name = "video_Volume",
            .AccessibleRole = AccessibleRole.Slider,
            .Height = bar_H,
            .Width = UiScale.Px(ChromeMetrics.TransportVolumeWidth, ChromeScale()),
            .BackColor = video_Controls.BackColor
        }
        video_Volume.ValueTextProvider = AddressOf VolumeValueText
        video_Volume.Value = video_Volume_Level
        video_Controls.Controls.Add(video_Volume)

        ' The speed button sits between the clock and the mute toggle - after the time
        ' readout, where VLC and mpv put it (119 Ф-1). Its caption IS its state.
        btn_Video_Speed = NewBarButton("btn_Video_Speed", btn_W, bar_H)
        btn_Video_Speed.Text = PlaybackRules.FormatRate(playback_Rate)
        video_Controls.Controls.Add(btn_Video_Speed)

        ' The way into everything else the file can do (121 Ф-2). Its glyph is the catalog's own "more".
        btn_Video_Menu = NewBarButton("btn_Video_Menu", btn_W, bar_H)
        video_Controls.Controls.Add(btn_Video_Menu)
        SetVideoGlyph(btn_Video_Menu, "nav.more")

        lbl_Video_Time = New BarLabel With {
            .Name = "lbl_Video_Time",
            .BackColor = video_Controls.BackColor,
            .Width = UiScale.Px(ChromeMetrics.TransportTimeWidth, ChromeScale()),
            .Height = bar_H,
            .Text = "--:-- / --:--",
            .Cursor = Cursors.Hand
        }
        video_Controls.Controls.Add(lbl_Video_Time)

        lbl_Video_Ab = New BarLabel With {
            .Name = "lbl_Video_Ab",
            .BackColor = video_Controls.BackColor,
            .Height = bar_H,
            .Visible = False
        }
        video_Controls.Controls.Add(lbl_Video_Ab)

        ' Any mouse activity anywhere on the bar keeps it up - otherwise it would slide
        ' out from under the hand that is reaching for it.
        AddHandler video_Controls.MouseMove, AddressOf VideoControls_MouseMove
        For Each c As Control In video_Controls.Controls
            AddHandler c.MouseMove, AddressOf VideoControls_MouseMove
        Next

        LayoutVideoControlsRow(pad)
        LocalizeVideoControls()
    End Sub

    Private Function NewBarButton(name As String, w As Integer, h As Integer) As NonFocusButton
        Dim b As New NonFocusButton() With {
            .Name = name,
            .Text = "",
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

    Private Sub SetVideoGlyph(button As Button, glyphId As String)
        If String.Equals(TryCast(button.Tag, String), glyphId, StringComparison.Ordinal) Then Return
        Dim oldImage As Image = button.Image
        Dim rendered As Image = Nothing
        Try
            rendered = GlyphRenderer.Render(glyphId, UiScale.Px(ChromeMetrics.TransportGlyphSize, ChromeScale()), button.ForeColor, AddressOf RuntimeBootstrap.OpenBundledAsset)
        Catch ex As Exception
            ' One bad asset costs one icon, not the whole bar (121 §1.4): the button keeps its accessible
            ' name, the same policy ChromeSurface.PaintGlyph follows. Without this the exception reached
            ' EnsureVideoControls, which dropped the entire bar.
            Debug.WriteLine("w2600 transport glyph '" & glyphId & "' failed: " & ex.Message)
        End Try
        button.Image = rendered
        If oldImage IsNot Nothing Then oldImage.Dispose()
        button.ImageAlign = ContentAlignment.MiddleCenter
        button.Tag = glyphId
    End Sub

    ' --- the row ---------------------------------------------------------------------------------

    Private Function MeasureBarText(text As String, font As Font) As Integer
        If font Is Nothing OrElse text.Length = 0 Then Return 0
        Return TextRenderer.MeasureText(text, font, New Size(Integer.MaxValue, Integer.MaxValue),
                                        TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine).Width
    End Function

    ''' <summary>The speed button as wide as its widest caption needs (121 UX-411: "1.25x" clipped in a 34 px button),
    ''' never narrower than the other buttons.</summary>
    Private Function SpeedButtonWidth() As Integer
        If video_Speed_Width > 0 Then Return video_Speed_Width
        Dim factor As Single = ChromeScale()
        Dim widest As Integer = 0
        Dim font As Font = If(btn_Video_Speed.RowFont, btn_Video_Speed.Font)
        For Each rate As Single In PlaybackRules.Speed_Ladder
            widest = Math.Max(widest, MeasureBarText(PlaybackRules.FormatRate(rate), font))
        Next
        video_Speed_Width = Math.Max(UiScale.Px(ChromeMetrics.TransportButtonWidth, factor),
                                     widest + 2 * UiScale.Px(ChromeMetrics.CaptionPadding, factor))
        Return video_Speed_Width
    End Function

    ''' <summary>The clock as wide as the longest text it can show for this file (121 UX-410), never narrower than the
    ''' logical width the bar always had.</summary>
    Private Function ClockWidth() As Integer
        Dim factor As Single = ChromeScale()
        Dim minimum As Integer = UiScale.Px(ChromeMetrics.TransportTimeWidth, factor)
        If video_Clock_Width > 0 AndAlso String.Equals(video_Clock_Sample, video_Clock_Sample_Wanted, StringComparison.Ordinal) Then Return video_Clock_Width
        video_Clock_Sample = video_Clock_Sample_Wanted
        video_Clock_Width = Math.Max(minimum, MeasureBarText(video_Clock_Sample, lbl_Video_Time.Font) + 2 * UiScale.Px(ChromeMetrics.CaptionPadding, factor))
        Return video_Clock_Width
    End Function

    ''' <summary>The sample the clock is sized for: set from the file's length by the refresh.</summary>
    Private video_Clock_Sample_Wanted As String = TransportRules.ClockSample(0)

    Private Shared Sub PlaceBarPart(part As Control, bounds As Rectangle, manageVisible As Boolean)
        If Not bounds.IsEmpty AndAlso part.Bounds <> bounds Then part.Bounds = bounds
        If manageVisible Then part.Visible = Not bounds.IsEmpty
    End Sub

    ''' <summary>Places the row: play | seek (stretches) | clock | A-B | speed | mute | volume | more. The arithmetic is
    ''' <see cref="TransportRules.ArrangeRow"/>; this only applies it.</summary>
    Private Sub LayoutVideoControlsRow(pad As Integer)
        If video_Controls Is Nothing Then Return

        Dim factor As Single = ChromeScale()
        Dim badge As String = TransportRules.AbBadgeText(ab_Loop.Phase)
        Dim input As New TransportRules.RowInput With {
            .Width = video_Controls.Width,
            .Height = video_Controls.Height,
            .Pad = pad,
            .ButtonWidth = UiScale.Px(ChromeMetrics.TransportButtonWidth, factor),
            .SpeedWidth = SpeedButtonWidth(),
            .TimeWidth = ClockWidth(),
            .BadgeWidth = If(badge.Length = 0, 0, MeasureBarText(badge, lbl_Video_Ab.Font) + 2 * UiScale.Px(ChromeMetrics.CaptionPadding, factor)),
            .VolumeWidth = UiScale.Px(ChromeMetrics.TransportVolumeWidth, factor),
            .SeekMinimum = UiScale.Px(ChromeMetrics.TransportSeekMinimum, factor)}
        Dim row As TransportRules.RowLayout = TransportRules.ArrangeRow(input)

        PlaceBarPart(btn_Video_Play, row.Play, False)
        ' The seek bar's visibility belongs to the refresh (a live stream has none).
        PlaceBarPart(video_Seek, row.Seek, False)
        PlaceBarPart(lbl_Video_Time, row.Time, True)
        PlaceBarPart(lbl_Video_Ab, row.Badge, True)
        PlaceBarPart(btn_Video_Speed, row.Speed, True)
        PlaceBarPart(btn_Video_Mute, row.Mute, True)
        PlaceBarPart(video_Volume, row.Volume, True)
        PlaceBarPart(btn_Video_Menu, row.More, False)
    End Sub

    ''' <summary>Pins the bar to the bottom of the media panel. Called from
    ''' SyncMediaSurface, so it follows every resize and full-screen switch.</summary>
    Friend Sub PositionVideoControls()
        If is_Super_Full_Screen_Mode Then
            HideVideoControls()
            Return
        End If
        If video_Controls Is Nothing OrElse panel_Media Is Nothing Then Return
        Dim margin As Integer = UiScale.Px(ChromeMetrics.TransportMargin, ChromeScale())
        Dim w As Integer = panel_Media.ClientSize.Width - margin * 2
        If w < UiScale.Px(ChromeMetrics.TransportMinimumWidth, ChromeScale()) Then w = Math.Max(0, panel_Media.ClientSize.Width)
        video_Controls.Width = w
        video_Controls.Location = Point.Empty
        If video_Chrome_Host IsNot Nothing Then
            video_Chrome_Host.Bounds = New Rectangle(panel_Media.PointToScreen(New Point((panel_Media.ClientSize.Width - w) \ 2,
                                            panel_Media.ClientSize.Height - video_Controls.Height - margin)), video_Controls.Size)
        End If
        LayoutVideoControlsRow(UiScale.Px(ChromeMetrics.ScreenMargin, ChromeScale()))
        If video_Controls.Visible Then video_Controls.BringToFront()
    End Sub

    Friend Sub LocalizeVideoControls()
        If video_Controls Is Nothing Then Return

        Dim seek_Text As String = Localization.T("Перемотка")
        Dim volume_Text As String = Localization.T("Громкость")
        Dim play_Text As String = Localization.T("Пауза / продолжить (клик по видео - то же самое; правая кнопка - меню)")
        Dim mute_Text As String = Localization.T("Звук вкл/выкл")
        Dim speed_Text As String = Localization.T("Скорость воспроизведения")
        Dim more_Text As String = Localization.T("Ещё")
        Dim more_Tip As String = Localization.T("Скорость, дорожки, повтор и другие операции с файлом")
        Dim clock_Tip As String = Localization.T("Нажмите, чтобы показать оставшееся время")

        If toolTip IsNot Nothing Then
            toolTip.SetToolTip(btn_Video_Play, play_Text)
            toolTip.SetToolTip(btn_Video_Mute, mute_Text)
            toolTip.SetToolTip(video_Seek, seek_Text)
            toolTip.SetToolTip(video_Volume, volume_Text)
            toolTip.SetToolTip(btn_Video_Speed, speed_Text)
            toolTip.SetToolTip(btn_Video_Menu, more_Tip)
            toolTip.SetToolTip(lbl_Video_Time, clock_Tip)
        End If

        ' The buttons' captions are icon-font glyphs from the private use area - a screen
        ' reader would announce garbage. These names are what it says instead (and what
        ' the UI test finds them by).
        btn_Video_Play.AccessibleName = Localization.T("Воспроизвести")
        btn_Video_Menu.AccessibleName = more_Text
        btn_Video_Menu.AccessibleDescription = more_Tip
        video_Seek.AccessibleName = seek_Text
        video_Volume.AccessibleName = volume_Text
        lbl_Video_Time.AccessibleName = Localization.T("Время")
        lbl_Video_Time.AccessibleDescription = clock_Tip
        RefreshBarAccessibleNames()
    End Sub

    ''' <summary>The names that carry a state (121 UX-403): mute says whether the sound is on, speed says its value. The
    ''' play button's name follows playback in the refresh. Re-read after every change of either.</summary>
    Private Sub RefreshBarAccessibleNames()
        If video_Controls Is Nothing Then Return
        btn_Video_Mute.AccessibleName = Localization.TF("{0}: {1}", Localization.T("Звук вкл/выкл"),
                                                         Localization.T(If(is_Video_Muted, "Звук выключен", "Звук включён")))
        btn_Video_Speed.AccessibleName = Localization.TF("{0}: {1}", Localization.T("Скорость воспроизведения"),
                                                          PlaybackRules.FormatRate(playback_Rate))
    End Sub

    ''' <summary>What a screen reader says for the seek bar's value: the clock's own words.</summary>
    Private Function SeekValueText() As String
        If lbl_Video_Time Is Nothing Then Return ""
        Return lbl_Video_Time.Text
    End Function

    ''' <summary>What a screen reader says for the volume's value.</summary>
    Private Function VolumeValueText() As String
        Return TransportRules.VolumePercent(If(is_Video_Muted, 0.0R, video_Volume_Level)).ToString(Globalization.CultureInfo.InvariantCulture) & " %"
    End Function

    ''' <summary>Brings the bar up and restarts the countdown to hiding it.</summary>
    Friend Sub ShowVideoControls()
        If is_Super_Full_Screen_Mode Then Return
        If Not is_Vlc_Playing Then Return
        EnsureVideoControls()
        If video_Controls Is Nothing Then Return

        PositionVideoControls()
        If video_Chrome_Host IsNot Nothing AndAlso Not video_Chrome_Host.Visible Then
            PinToViewerBand(video_Chrome_Host)
            video_Chrome_Host.Show()
        End If
        If Not video_Controls.Visible Then
            video_Controls.Visible = True
            RefreshVideoControlsState()
        End If
        video_Controls.BringToFront()
        ' The recipients overlay must stay clickable above us.
        KeepRecipientsOverlayOnTop()

        video_Controls_Timer.Start()
        video_Controls_Hide_Timer.Stop()
        If IsAudioNowPlaying() AndAlso (modern_Preferences Is Nothing OrElse modern_Preferences.AudioControlsAlwaysVisible) Then Return
        video_Controls_Hide_Timer.Interval = VideoControlsHideDelayMilliseconds()
        video_Controls_Hide_Timer.Start()
    End Sub

    Friend Sub HideVideoControls()
        video_Controls_Hide_Timer.Stop()
        video_Controls_Timer.Stop()
        TransportRingLeave()
        If video_Controls IsNot Nothing Then video_Controls.Visible = False
        If video_Chrome_Host IsNot Nothing Then video_Chrome_Host.Hide()
    End Sub

    Private Sub Video_Controls_Hide_Timer_Tick(sender As Object, e As EventArgs) Handles video_Controls_Hide_Timer.Tick
        video_Controls_Hide_Timer.Stop()
        If video_Controls Is Nothing OrElse Not video_Controls.Visible Then Return

        If IsAudioNowPlaying() AndAlso (modern_Preferences Is Nothing OrElse modern_Preferences.AudioControlsAlwaysVisible) Then Return

        ' Never disappear while the user is holding a slider, while the pointer rests on the bar,
        ' while a menu opened from it (or from any panel) is up, while the F10 ring stands in it, or
        ' while playback is paused - a paused video with no transport is exactly the dead end this
        ' file exists to remove. One rule (TransportRules.ShouldKeepBarUp), so the speed menu and
        ' the video menu are covered by the same test (121 UX-402).
        Dim hold As New TransportRules.HoldState With {
            .SliderHeld = video_Seek.IsDragging OrElse video_Volume.IsDragging,
            .PointerOnBar = video_Chrome_Host IsNot Nothing AndAlso video_Chrome_Host.Bounds.Contains(Cursor.Position),
            .PopupOpen = ChromeMenu.PopupVisible,
            .KeyboardRingOn = chrome_Keyboard_Mode,
            .PausedAndKept = KeepVideoControlsVisibleWhilePaused() AndAlso Not IsVlcActuallyPlaying()}
        If TransportRules.ShouldKeepBarUp(hold) Then
            video_Controls_Hide_Timer.Start()
            Return
        End If

        TransportRingLeave()
        video_Controls.Visible = False
        If video_Chrome_Host IsNot Nothing Then video_Chrome_Host.Hide()
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
        OnPlaybackPointerActivity(e.X, e.Y)
        RevealSlideshowChromeTemporarily()
        ShowVideoControls()
    End Sub

    ''' <summary>Keeps the speed button's caption on the user's rate (119 Ф-1). The
    ''' button shows what the user ASKED for - never libVLC's own Rate, which the XML
    ''' warns may not be what the media actually plays.</summary>
    Private Sub RefreshPlaybackSpeedButton()
        If btn_Video_Speed Is Nothing Then Return
        Dim text As String = PlaybackRules.FormatRate(playback_Rate)
        If Not String.Equals(btn_Video_Speed.Text, text, StringComparison.Ordinal) Then
            btn_Video_Speed.Text = text
            btn_Video_Speed.Invalidate()
        End If
        RefreshBarAccessibleNames()
    End Sub

    ''' <summary>The speed ladder as menu rows: one item per step, the nearest step to the current rate checked,
    ''' "Обычная" as the reset. The ONE builder - the speed button's menu and the video menu's submenu both use it.
    ''' The step captions are language-neutral numbers.</summary>
    Private Function BuildSpeedLadderItems() As List(Of ToolStripItem)
        Dim items As New List(Of ToolStripItem)()
        Dim nearest As Single = PlaybackRules.Speed_Ladder(0)
        For Each step_Rate As Single In PlaybackRules.Speed_Ladder
            If Math.Abs(step_Rate - playback_Rate) < Math.Abs(nearest - playback_Rate) Then nearest = step_Rate
        Next

        For Each step_Rate As Single In PlaybackRules.Speed_Ladder
            Dim rate As Single = step_Rate
            Dim item As New ToolStripMenuItem(PlaybackRules.FormatRate(rate)) With {
                .Checked = Math.Abs(rate - nearest) < 0.001F}
            AddHandler item.Click, Sub()
                                       ApplyPlaybackRate(rate, announce:=True)
                                       RefreshVideoControlsState()
                                       ShowVideoControls()
                                   End Sub
            items.Add(item)
        Next

        Dim reset As New ToolStripMenuItem(Localization.T("Обычная"))
        AddHandler reset.Click, Sub()
                                    ApplyPlaybackRate(PlaybackRules.Normal_Rate, announce:=True)
                                    RefreshVideoControlsState()
                                    ShowVideoControls()
                                End Sub
        items.Add(reset)
        Return items
    End Function

    Private Sub btn_Video_Speed_Click(sender As Object, e As EventArgs) Handles btn_Video_Speed.Click
        ShowSpeedMenu(False)
    End Sub

    ''' <summary>The ladder menu of the speed button (119 Ф-1), above the bar when it does not fit below it. The strip
    ''' lives in a field and its rows are disposed with the next opening - it used to be built per click and dropped.</summary>
    Private Sub ShowSpeedMenu(fromKeyboard As Boolean)
        If Not CanUsePlaybackExtras() Then Return

        If video_Speed_Menu Is Nothing Then video_Speed_Menu = New ContextMenuStrip()
        ClearAndDisposeItems(video_Speed_Menu.Items)
        video_Speed_Menu.Items.AddRange(BuildSpeedLadderItems().ToArray())

        Dim at As Point = btn_Video_Speed.PointToScreen(New Point(0, btn_Video_Speed.Height))
        ChromeShowToolStripMenu(video_Speed_Menu.Items, at, fromKeyboard, btn_Video_Speed.PointToScreen(Point.Empty).Y)
        ShowVideoControls()
    End Sub

    Private Sub btn_Video_Menu_Click(sender As Object, e As EventArgs) Handles btn_Video_Menu.Click
        ShowVideoMenuFromBar(False)
    End Sub

    ''' <summary>The "more" button: the video menu - the very one the right click on the picture opens (121 Ф-2) - at
    ''' the button, above the bar when it does not fit below. Nothing here builds a menu of its own.</summary>
    Private Sub ShowVideoMenuFromBar(fromKeyboard As Boolean)
        Dim at As Point = btn_Video_Menu.PointToScreen(New Point(0, btn_Video_Menu.Height))
        ShowVideoContextMenu(at, fromKeyboard, btn_Video_Menu.PointToScreen(Point.Empty).Y)
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
        ' The sleep timer is not polled here any more: this tick stops with the bar, and the bar is never shown in
        ' the full screen without panels (121 UX-404). It has a timer of its own (Main_Form.AudioView.vb).
        If video_Controls Is Nothing OrElse Not video_Controls.Visible Then Return
        RefreshVideoControlsState()
        If IsAudioNowPlaying() AndAlso modern_Preferences IsNot Nothing AndAlso modern_Preferences.AudioVisualiser Then RepaintMedia()
    End Sub

    ''' <summary>Pulls the live state out of the player and into the bar.</summary>
    Private Sub RefreshVideoControlsState()
        If video_Controls Is Nothing OrElse vlc_Media_Player Is Nothing Then Return

        Try
            If IsLiveBroadcastActive() Then
                video_Seek.Visible = False
                video_Seek.Enabled = False
                lbl_Video_Time.Text = "LIVE"

                ' Stop while the loop is alive - connecting and reconnecting included, where there is no sound to
                ' pause - and "Connect again" once it is over (a live stream has no pause: 023 section 8).
                Dim livePlaying As Boolean = IsLiveBroadcastRunning()
                SetVideoGlyph(btn_Video_Play, If(livePlaying, "media.stop", "media.play"))
                btn_Video_Play.AccessibleName = Localization.T(If(livePlaying, "Остановить", "Подключиться снова"))
                RefreshMuteState()
                Return
            Else
                video_Seek.Visible = True
                video_Seek.Enabled = True
            End If

            Dim pos_Ms As Long = vlc_Media_Player.Time
            Dim len_Ms As Long = vlc_Media_Player.Length

            ' Not while the user is holding it - the tick would fight the drag - and not in the moment after the
            ' release, when the player has not yet caught up with the seek (121 UX-409): the thumb and the clock stay
            ' where the user put them.
            Dim holding As Boolean = Not TransportRules.SeekTickMayOverwrite(video_Seek.IsDragging, MonotonicClock.NowMs(), video_Seek_Hold_Until_Ms)
            If Not holding Then
                video_Seek.Value = vlc_Media_Player.Position
            ElseIf Not video_Seek.IsDragging AndAlso len_Ms > 0 Then
                pos_Ms = CLng(len_Ms * video_Seek.Value)
            End If

            ' The clock is sized for the longest text this file can show (121 UX-410); a new file of another length
            ' changes it.
            Dim wanted_Sample As String = TransportRules.ClockSample(len_Ms)
            If Not String.Equals(wanted_Sample, video_Clock_Sample_Wanted, StringComparison.Ordinal) Then
                video_Clock_Sample_Wanted = wanted_Sample
                LayoutVideoControlsRow(UiScale.Px(ChromeMetrics.ScreenMargin, ChromeScale()))
            End If
            If Not video_Seek.IsDragging Then lbl_Video_Time.Text = TransportRules.FormatClock(pos_Ms, len_Ms, video_Clock_Remaining)
            ApplyAbIndicators(len_Ms)

            Dim playing As Boolean = IsVlcActuallyPlaying()
            SetVideoGlyph(btn_Video_Play, If(playing, "media.pause", "media.play"))
            btn_Video_Play.AccessibleName = Localization.T(If(playing, "Пауза", "Воспроизвести"))
            RefreshMuteState()
        Catch ex As Exception
            ' The player can be torn down under us between the check and the call.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2501: video controls refresh skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>The mute button's glyph (crossed out while muted and at level zero, 121 UX-408), the volume slider
    ''' and the names that carry the state.</summary>
    Private Sub RefreshMuteState()
        SetVideoGlyph(btn_Video_Mute, TransportRules.VolumeGlyphId(is_Video_Muted, video_Volume_Level))
        If Not video_Volume.IsDragging Then video_Volume.Value = If(is_Video_Muted, 0.0, video_Volume_Level)
        RefreshBarAccessibleNames()
    End Sub

    ''' <summary>The A and B marks on the track and the "A" / "A-B" badge by the speed button (121 UX-412): the state
    ''' of the repeat span used to be a status line that faded after about five seconds.</summary>
    Private Sub ApplyAbIndicators(lengthMs As Long)
        If video_Controls Is Nothing Then Return
        Dim marks As TransportRules.AbMarks = TransportRules.AbMarksFor(ab_Loop.Phase, ab_Loop.AMs, ab_Loop.BMs, lengthMs)
        video_Seek.MarkA = marks.A
        video_Seek.MarkB = marks.B
        Dim badge As String = TransportRules.AbBadgeText(ab_Loop.Phase)
        If Not String.Equals(lbl_Video_Ab.Text, badge, StringComparison.Ordinal) Then
            lbl_Video_Ab.Text = badge
            lbl_Video_Ab.AccessibleName = If(badge.Length = 0, "", Localization.TF("{0}: {1}", Localization.T("Повтор A-B"), badge))
            LayoutVideoControlsRow(UiScale.Px(ChromeMetrics.ScreenMargin, ChromeScale()))
        End If
    End Sub

    ''' <summary>The marks go with the media they were set on (called from the reset of the playback extras).</summary>
    Private Sub ClearAbIndicators()
        If video_Controls Is Nothing Then Return
        ApplyAbIndicators(0)
    End Sub

    ''' <summary>mm:ss, or h:mm:ss once it is worth it. A stream of unknown length
    ''' reports -1 - show dashes rather than a bogus clock.</summary>
    Private Shared Function FormatMediaTime(ms As Long) As String
        Return TransportRules.FormatMediaTime(ms)
    End Function

    Private Sub btn_Video_Play_Click(sender As Object, e As EventArgs) Handles btn_Video_Play.Click
        PlayPauseFromUser()
        RefreshVideoControlsState()
        ShowVideoControls()
    End Sub

    ''' <summary>The play/pause button and the pause key (UX-102 of 123) are one implementation: a live
    ''' stream stops or reconnects, anything else flips playback. Refreshing the bar is the caller's.</summary>
    Private Sub PlayPauseFromUser()
        If IsLiveBroadcastActive() Then
            If IsLiveBroadcastRunning() Then
                StopLivePlayback(isUserStop:=True)
            Else
                ReconnectLiveBroadcast()
            End If
            Return
        End If

        TogglePlayPause()
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
            ' After EndReached libvlc keeps the exhausted input: Play() resumes a
            ' pause, but cannot restart that input. Reopen from the beginning through
            ' the same path as Repeat, retaining the selected rate and A-B marks.
            If vlc_Media_Player.State = LibVLCSharp.Shared.VLCState.Ended Then
                RestartCurrentVideoPreservingPosition(0.0F)
                Return False
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
        ToggleVideoMute()
        RefreshVideoControlsState()
        ShowVideoControls()
    End Sub

    ''' <summary>Mute on / off - the bar's button and the mute key share it (UX-102 of 123).</summary>
    Private Sub ToggleVideoMute()
        SetVideoAudioState(video_Volume_Level, Not is_Video_Muted)
    End Sub

    ''' <summary>The clock shows the remaining time instead of the elapsed one, and back (121 UX-410).</summary>
    Private Sub lbl_Video_Time_Click(sender As Object, e As EventArgs) Handles lbl_Video_Time.Click
        ToggleClockRemaining()
    End Sub

    Private Sub ToggleClockRemaining()
        video_Clock_Remaining = Not video_Clock_Remaining
        RefreshVideoControlsState()
        ShowVideoControls()
    End Sub

    ''' <summary>Seeking is committed on release, and on a plain click anywhere on the
    ''' track. Following every pixel of the drag would have VLC re-buffering all the way
    ''' across the file.</summary>
    Private Sub video_Seek_Scrubbed(sender As Object, e As EventArgs) Handles video_Seek.Scrubbed
        If vlc_Media_Player Is Nothing Then Return
        Try
            If TransportRules.CanSeekTo(vlc_Media_Player.IsSeekable, vlc_Media_Player.Length) Then
                vlc_Media_Player.Position = CSng(video_Seek.Value)
                video_Seek_Hold_Until_Ms = MonotonicClock.NowMs() + TransportRules.Seek_Hold_Ms
            Else
                ' A stream or a file of unknown length cannot be sought: put the thumb back where the player is.
                video_Seek.Value = vlc_Media_Player.Position
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2503: seek failed: " & ex.Message)
        End Try
        video_Seek.HoverText = ""
        ShowVideoControls()
    End Sub

    ''' <summary>While dragging, the clock follows the knob - that is what makes it
    ''' possible to aim.</summary>
    Private Sub video_Seek_Scrubbing(sender As Object, e As EventArgs) Handles video_Seek.Scrubbing
        If vlc_Media_Player Is Nothing OrElse lbl_Video_Time Is Nothing Then Return
        Try
            Dim len_Ms As Long = vlc_Media_Player.Length
            If len_Ms > 0 Then
                Dim at_Ms As Long = CLng(len_Ms * video_Seek.Value)
                lbl_Video_Time.Text = TransportRules.FormatClock(at_Ms, len_Ms, video_Clock_Remaining)
                video_Seek.HoverText = TransportRules.FormatMediaTime(at_Ms)
            End If
        Catch
        End Try
    End Sub

    ''' <summary>The time under the pointer, in a small bubble over the track (121 UX-409).</summary>
    Private Sub video_Seek_Hovered(x As Integer) Handles video_Seek.Hovered
        If vlc_Media_Player Is Nothing Then Return
        Try
            video_Seek.HoverText = TransportRules.SeekHoverText(x, video_Seek.Width, vlc_Media_Player.Length)
        Catch
            video_Seek.HoverText = ""
        End Try
    End Sub

    Private Sub video_Seek_HoverEnded() Handles video_Seek.HoverEnded
        video_Seek.HoverText = ""
    End Sub

    ''' <summary>The wheel over the seek track steps like the seek keys (the wheel over the picture still pages files).</summary>
    Private Sub video_Seek_Wheeled(up As Boolean) Handles video_Seek.Wheeled
        If PlaybackActionApplicable("seekFwdShort") Then RunPlaybackAction(TransportRules.RingSliderAction(True, up, False))
    End Sub

    Private Sub video_Volume_Scrubbing(sender As Object, e As EventArgs) Handles video_Volume.Scrubbing
        ' Volume follows the finger live: it is cheap and the point is to hear it. The speaker is crossed out at the
        ' left end (121 UX-408) and the status says the number.
        SetVideoAudioState(video_Volume.Value, False)
        SetVideoGlyph(btn_Video_Mute, TransportRules.VolumeGlyphId(False, video_Volume.Value))
        video_Volume.HoverText = TransportRules.VolumePercent(video_Volume.Value).ToString(Globalization.CultureInfo.InvariantCulture) & " %"
        lbl_Status.Text = Localization.TF("Громкость: {0} %", TransportRules.VolumePercent(video_Volume.Value).ToString(Globalization.CultureInfo.InvariantCulture))
    End Sub

    Private Sub video_Volume_Hovered(x As Integer) Handles video_Volume.Hovered
        video_Volume.HoverText = TransportRules.VolumePercent(TransportRules.FractionAt(x, video_Volume.Width)).ToString(Globalization.CultureInfo.InvariantCulture) & " %"
    End Sub

    Private Sub video_Volume_HoverEnded() Handles video_Volume.HoverEnded
        video_Volume.HoverText = ""
    End Sub

    ''' <summary>The wheel over the volume slider changes the volume (121 UX-408).</summary>
    Private Sub video_Volume_Wheeled(up As Boolean) Handles video_Volume.Wheeled
        If CanUseVideoBasics() Then RunPlaybackAction(TransportRules.RingSliderAction(False, up, False))
    End Sub

    ' --- the F10 ring inside the bar (121 UX-403) ---------------------------------------------

    ''' <summary>The bar's stops, left to right: whatever is on it and can be pressed or adjusted.</summary>
    Private Function TransportRingStops() As List(Of Control)
        Dim stops As New List(Of Control)()
        If video_Controls Is Nothing OrElse Not video_Controls.Visible Then Return stops
        For Each part As Control In New Control() {btn_Video_Play, video_Seek, lbl_Video_Time, btn_Video_Speed, btn_Video_Mute, video_Volume, btn_Video_Menu}
            If part.Visible AndAlso part.Enabled Then stops.Add(part)
        Next
        Return stops
    End Function

    ''' <summary>The bar has something for the ring to stand on right now.</summary>
    Private Function TransportRingAvailable() As Boolean
        Return TransportRingStops().Count > 0
    End Function

    Private Sub SetBarRing(part As Control, enabled As Boolean)
        If part Is Nothing Then Return
        Dim color As Color = If(enabled, chrome_Theme.FocusRing, Color.Empty)
        Dim width As Integer = UiScale.Px(ChromeMetrics.FocusRingWidth, ChromeScale())
        Dim button As NonFocusButton = TryCast(part, NonFocusButton)
        If button IsNot Nothing Then
            button.FocusRingWidth = width
            button.FocusRingColor = color
            If enabled Then button.NotifyFocused()
            Return
        End If
        Dim slider As FlatSlider = TryCast(part, FlatSlider)
        If slider IsNot Nothing Then
            slider.FocusRingWidth = width
            slider.FocusRingColor = color
            If enabled Then slider.NotifyFocused()
            Return
        End If
        Dim label As BarLabel = TryCast(part, BarLabel)
        If label IsNot Nothing Then
            label.FocusRingWidth = width
            label.FocusRingColor = color
            If enabled Then label.NotifyFocused()
        End If
    End Sub

    Private Sub SetRingControl(part As Control)
        If video_Ring_Control IsNot Nothing Then SetBarRing(video_Ring_Control, False)
        video_Ring_Control = part
        If part IsNot Nothing Then SetBarRing(part, True)
    End Sub

    ''' <summary>The ring arrives in the bar, at its first stop (or its last, coming from the other side).
    ''' False when the bar has none. Brings the bar up and keeps it up while the ring is on.</summary>
    Private Function TransportRingEnter(last As Boolean) As Boolean
        If video_Controls Is Nothing OrElse Not video_Controls.Visible Then ShowVideoControls()
        Dim stops As List(Of Control) = TransportRingStops()
        If stops.Count = 0 Then Return False
        SetRingControl(If(last, stops(stops.Count - 1), stops(0)))
        ShowVideoControls()
        Return True
    End Function

    ''' <summary>One step inside the bar; False when it would leave the bar (the caller carries on to the next panel).</summary>
    Private Function TransportRingMove(forward As Boolean) As Boolean
        Dim stops As List(Of Control) = TransportRingStops()
        Dim at As Integer = stops.IndexOf(video_Ring_Control)
        If at < 0 Then
            If stops.Count = 0 Then Return False
            SetRingControl(stops(0))
            Return True
        End If
        Dim target As Integer = TransportRules.RingStep(at, stops.Count, forward)
        If target < 0 Then Return False
        SetRingControl(stops(target))
        Return True
    End Function

    Private Sub TransportRingLeave()
        If video_Ring_Control Is Nothing Then Return
        SetRingControl(Nothing)
    End Sub

    ''' <summary>Enter / Space on the stop the ring stands on.</summary>
    Private Sub TransportRingActivate()
        Dim part As Control = video_Ring_Control
        If part Is Nothing Then Return
        If part Is lbl_Video_Time Then
            ToggleClockRemaining()
        ElseIf part Is btn_Video_Speed Then
            ShowSpeedMenu(True)
        ElseIf part Is btn_Video_Menu Then
            ShowVideoMenuFromBar(True)
        ElseIf TypeOf part Is NonFocusButton Then
            InvokeOnClick(part, EventArgs.Empty)
        End If
        ' A slider has nothing to press: its arrows adjust it.
    End Sub

    ''' <summary>An arrow or page key on a slider adjusts it (through the playback keys' own actions). True = consumed.</summary>
    Private Function TransportRingAdjust(code As Keys) As Boolean
        Dim is_Seek As Boolean = video_Ring_Control Is video_Seek
        If Not is_Seek AndAlso video_Ring_Control IsNot video_Volume Then Return False
        Dim up As Boolean
        Dim page As Boolean
        Select Case code
            Case Keys.Up : up = True
            Case Keys.Down : up = False
            Case Keys.PageUp : up = True : page = True
            Case Keys.PageDown : page = True
            Case Else : Return False
        End Select
        RunPlaybackAction(TransportRules.RingSliderAction(is_Seek, up, page))
        Return True
    End Function

    ' --- a text part of the bar ------------------------------------------------------------------

    ''' <summary>The clock and the A-B badge: a centred caption on the bar's fill, optionally on a chip, with the ring's
    ''' frame. Never takes focus.</summary>
    Private NotInheritable Class BarLabel
        Inherits Control

        Private ringColor As Color = Color.Empty
        Private chip As Color = Color.Empty

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            AccessibleRole = AccessibleRole.StaticText
        End Sub

        Public Property ChipColor As Color
            Get
                Return chip
            End Get
            Set(value As Color)
                If chip = value Then Return
                chip = value
                Invalidate()
            End Set
        End Property

        Public Property FocusRingColor As Color
            Get
                Return ringColor
            End Get
            Set(value As Color)
                If ringColor = value Then Return
                ringColor = value
                Invalidate()
            End Set
        End Property

        Public Property FocusRingWidth As Integer = 2

        Public Sub NotifyFocused()
            AccessibilityNotifyClients(AccessibleEvents.Focus, -1)
        End Sub

        Protected Overrides Sub OnTextChanged(e As EventArgs)
            MyBase.OnTextChanged(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnForeColorChanged(e As EventArgs)
            MyBase.OnForeColorChanged(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnFontChanged(e As EventArgs)
            MyBase.OnFontChanged(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(BackColor)
            Dim flags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or
                                           TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
            If chip <> Color.Empty AndAlso Text.Length > 0 Then
                Dim size As Size = TextRenderer.MeasureText(e.Graphics, Text, Font, New Size(Integer.MaxValue, Integer.MaxValue), flags)
                Dim pad As Integer = Math.Max(3, LogicalToDeviceUnits(4))
                Dim box As New Rectangle((Width - size.Width) \ 2 - pad, (Height - size.Height) \ 2 - pad \ 2, size.Width + 2 * pad, size.Height + pad)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As New GraphicsPath()
                    Dim d As Integer = Math.Max(2, Math.Min(box.Height, LogicalToDeviceUnits(8)))
                    path.AddArc(box.Left, box.Top, d, d, 180, 90)
                    path.AddArc(box.Right - d, box.Top, d, d, 270, 90)
                    path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90)
                    path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90)
                    path.CloseFigure()
                    Using brush As New SolidBrush(chip)
                        e.Graphics.FillPath(brush, path)
                    End Using
                End Using
            End If
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags)
            If ringColor <> Color.Empty Then
                Dim w As Integer = Math.Max(1, FocusRingWidth)
                Using pen As New Pen(ringColor, w)
                    e.Graphics.DrawRectangle(pen, w \ 2, w \ 2, Width - w, Height - w)
                End Using
            End If
        End Sub
    End Class

    ''' <summary>
    ''' A flat slider - the track, the part played, and a knob. Used for both seeking and
    ''' volume. A WinForms TrackBar would look like a 1998 dialog bolted onto a video and
    ''' cannot be restyled; this paints itself from the chrome theme and matches the rest of the chrome.
    ''' It takes the bar's full height as its target, can show a bubble over the track (the time or the percent under
    ''' the pointer), the A and B marks of a repeat span and the F10 ring's frame, and answers a screen reader with a
    ''' name, the slider role and a value text.
    ''' </summary>
    Private NotInheritable Class FlatSlider
        Inherits Control

        Private _value As Double = 0.0
        Private _dragging As Boolean = False
        Private _markA As Double = -1.0
        Private _markB As Double = -1.0
        Private _hoverText As String = ""
        Private _hoverX As Integer
        Private _ring As Color = Color.Empty
        Private _track As Color = Color.FromArgb(80, 80, 80)
        Private _fill As Color = Color.FromArgb(0, 150, 215)
        Private _thumb As Color = Color.White

        ''' <summary>The user moved the knob and is still holding it.</summary>
        Public Event Scrubbing As EventHandler
        ''' <summary>The user let go - this is the moment to act on the value.</summary>
        Public Event Scrubbed As EventHandler
        ''' <summary>The pointer is over the track at x (client coordinates).</summary>
        Public Event Hovered(x As Integer)
        Public Event HoverEnded()
        ''' <summary>The wheel turned over the slider: up = away from the user.</summary>
        Public Event Wheeled(up As Boolean)

        ''' <summary>What a screen reader reads as the value; supplied by the owner (the clock's words, "75 %").</summary>
        Public Property ValueTextProvider As Func(Of String)

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

        Public Property TrackColor As Color
            Get
                Return _track
            End Get
            Set(c As Color)
                If _track = c Then Return
                _track = c
                Invalidate()
            End Set
        End Property

        Public Property FillColor As Color
            Get
                Return _fill
            End Get
            Set(c As Color)
                If _fill = c Then Return
                _fill = c
                Invalidate()
            End Set
        End Property

        Public Property ThumbColor As Color
            Get
                Return _thumb
            End Get
            Set(c As Color)
                If _thumb = c Then Return
                _thumb = c
                Invalidate()
            End Set
        End Property

        ''' <summary>The start of a marked repeat span, 0..1; below 0 = none.</summary>
        Public Property MarkA As Double
            Get
                Return _markA
            End Get
            Set(v As Double)
                If Math.Abs(v - _markA) < 0.000001 Then Return
                _markA = v
                Invalidate()
            End Set
        End Property

        ''' <summary>The end of an armed repeat span, 0..1; below 0 = none.</summary>
        Public Property MarkB As Double
            Get
                Return _markB
            End Get
            Set(v As Double)
                If Math.Abs(v - _markB) < 0.000001 Then Return
                _markB = v
                Invalidate()
            End Set
        End Property

        ''' <summary>The bubble over the track at the pointer; empty = none.</summary>
        Public Property HoverText As String
            Get
                Return _hoverText
            End Get
            Set(text As String)
                text = If(text, "")
                If String.Equals(_hoverText, text, StringComparison.Ordinal) Then Return
                _hoverText = text
                Invalidate()
            End Set
        End Property

        Public Property FocusRingColor As Color
            Get
                Return _ring
            End Get
            Set(c As Color)
                If _ring = c Then Return
                _ring = c
                Invalidate()
            End Set
        End Property

        Public Property FocusRingWidth As Integer = 2

        Public Sub NotifyFocused()
            AccessibilityNotifyClients(AccessibleEvents.Focus, -1)
        End Sub

        Friend Function CurrentValueText() As String
            Return If(ValueTextProvider Is Nothing, "", ValueTextProvider.Invoke())
        End Function

        Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
            Return New SliderAccessibleObject(Me)
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(BackColor)
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

            Dim track_H As Int32 = Math.Max(3, LogicalToDeviceUnits(4))
            Dim y As Int32 = (ClientSize.Height - track_H) \ 2
            Dim w As Int32 = ClientSize.Width
            If w <= 0 Then Return

            Using b As New SolidBrush(_track)
                e.Graphics.FillRectangle(b, 0, y, w, track_H)
            End Using

            Dim fill As Int32 = CInt(w * _value)
            If fill > 0 Then
                Using b As New SolidBrush(_fill)
                    e.Graphics.FillRectangle(b, 0, y, fill, track_H)
                End Using
            End If

            ' The repeat span: a band over the track between A and B, and a tick at each mark (121 UX-412).
            If _markA >= 0.0 Then
                Dim ax As Int32 = CInt(w * _markA)
                Dim tick_W As Int32 = Math.Max(2, LogicalToDeviceUnits(2))
                Dim tick_H As Int32 = track_H * 3
                If _markB > _markA Then
                    Dim bx As Int32 = CInt(w * _markB)
                    Using b As New SolidBrush(Color.FromArgb(150, _thumb))
                        e.Graphics.FillRectangle(b, ax, y, Math.Max(1, bx - ax), track_H)
                    End Using
                    Using b As New SolidBrush(_thumb)
                        e.Graphics.FillRectangle(b, Math.Min(w - tick_W, bx), y - (tick_H - track_H) \ 2, tick_W, tick_H)
                    End Using
                End If
                Using b As New SolidBrush(_thumb)
                    e.Graphics.FillRectangle(b, Math.Min(w - tick_W, ax), y - (tick_H - track_H) \ 2, tick_W, tick_H)
                End Using
            End If

            Dim r As Int32 = Math.Max(4, LogicalToDeviceUnits(5))
            Dim cx As Int32 = Math.Max(r, Math.Min(w - r, fill))
            Using b As New SolidBrush(_thumb)
                e.Graphics.FillEllipse(b, cx - r, ClientSize.Height \ 2 - r, r * 2, r * 2)
            End Using

            If _hoverText.Length > 0 Then PaintBubble(e.Graphics, y, w)

            If _ring <> Color.Empty Then
                Dim rw As Integer = Math.Max(1, FocusRingWidth)
                Using pen As New Pen(_ring, rw)
                    e.Graphics.DrawRectangle(pen, rw \ 2, rw \ 2, Width - rw, Height - rw)
                End Using
            End If
        End Sub

        ''' <summary>The time or percent under the pointer, in a small chip above the track.</summary>
        Private Sub PaintBubble(g As Graphics, trackTop As Integer, w As Integer)
            Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or
                                           TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter
            Dim size As Size = TextRenderer.MeasureText(g, _hoverText, Font, New Size(Integer.MaxValue, Integer.MaxValue), flags)
            Dim pad As Integer = Math.Max(2, LogicalToDeviceUnits(3))
            Dim box_W As Integer = size.Width + 2 * pad
            Dim box_H As Integer = size.Height + pad
            Dim box_X As Integer = Math.Max(0, Math.Min(w - box_W, _hoverX - box_W \ 2))
            Dim box_Y As Integer = Math.Max(0, trackTop - box_H - Math.Max(2, LogicalToDeviceUnits(3)))
            Dim box As New Rectangle(box_X, box_Y, box_W, box_H)
            Using b As New SolidBrush(_thumb)
                g.FillRectangle(b, box)
            End Using
            TextRenderer.DrawText(g, _hoverText, Font, box, BackColor, flags)
        End Sub

        Private Sub SetFromMouse(x As Integer)
            If ClientSize.Width <= 0 Then Return
            Value = TransportRules.FractionAt(x, ClientSize.Width)
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
            _hoverX = e.X
            If _dragging Then SetFromMouse(e.X)
            RaiseEvent Hovered(e.X)
            MyBase.OnMouseMove(e)
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            If Not _dragging Then RaiseEvent HoverEnded()
            MyBase.OnMouseLeave(e)
        End Sub

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            If e.Delta <> 0 Then RaiseEvent Wheeled(e.Delta > 0)
            Dim handled As HandledMouseEventArgs = TryCast(e, HandledMouseEventArgs)
            If handled IsNot Nothing Then handled.Handled = True
            MyBase.OnMouseWheel(e)
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            If _dragging AndAlso e.Button = MouseButtons.Left Then
                _dragging = False
                Capture = False
                SetFromMouse(e.X)
                RaiseEvent Scrubbed(Me, EventArgs.Empty)
                ' Released with the pointer outside: no leave is coming, and the bubble must not stay.
                If Not ClientRectangle.Contains(e.Location) Then RaiseEvent HoverEnded()
            End If
            MyBase.OnMouseUp(e)
        End Sub

        ''' <summary>Windows can take the capture away without a MouseUp: Alt+Tab / Win
        ''' while holding the knob, or the bar hidden under the hand before the release.
        ''' Without this the drag never ended - the seek bar froze, the transport stopped
        ''' auto-hiding, and merely HOVERING the volume slider changed the volume. A
        ''' normal release clears _dragging BEFORE it lets go of the capture, so this
        ''' does not fire twice for it. No Scrubbed here: losing the window is not a
        ''' decision about where to seek.</summary>
        Protected Overrides Sub OnMouseCaptureChanged(e As EventArgs)
            MyBase.OnMouseCaptureChanged(e)
            If _dragging AndAlso Not Capture Then
                _dragging = False
                Invalidate()
            End If
        End Sub

        ''' <summary>The slider as assistive technology sees it: its name, the slider role and the owner's value text
        ''' (the clock's words or "75 %"). Value is read on demand, never announced on every tick.</summary>
        Private NotInheritable Class SliderAccessibleObject
            Inherits Control.ControlAccessibleObject

            Private ReadOnly slider As FlatSlider

            Public Sub New(owner As FlatSlider)
                MyBase.New(owner)
                slider = owner
            End Sub

            Public Overrides Property Value As String
                Get
                    Return slider.CurrentValueText()
                End Get
                Set(newValue As String)
                End Set
            End Property

            Public Overrides ReadOnly Property Role As AccessibleRole
                Get
                    Return AccessibleRole.Slider
                End Get
            End Property
        End Class

    End Class

End Class
#End If
