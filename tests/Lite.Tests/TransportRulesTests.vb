#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports Xunit

''' <summary>
''' The pure decisions of the video transport bar (ticket 121, section 7): the row's arithmetic at two scales, the clock,
''' the seek hold, what keeps the bar up, the buffering steps, the strip under the bar, the F10 ring inside it, and the
''' two layout helpers it leans on (the status cell split, the popup that opens above a low control). Modern-only, like
''' the bar.
''' </summary>
Public Class TransportRulesTests

    ' --- the row (UX-406, UX-410, UX-409) ------------------------------------------------------------

    Private Shared Function Input(width As Integer, Optional scale As Double = 1.0R, Optional badge As Integer = 0, Optional speed As Integer = 40) As TransportRules.RowInput
        Return New TransportRules.RowInput With {
            .Width = width, .Height = CInt(38 * scale), .Pad = CInt(8 * scale), .ButtonWidth = CInt(34 * scale),
            .SpeedWidth = CInt(speed * scale), .TimeWidth = CInt(96 * scale), .BadgeWidth = CInt(badge * scale),
            .VolumeWidth = CInt(80 * scale), .SeekMinimum = CInt(40 * scale)}
    End Function

    Private Shared Function Parts(row As TransportRules.RowLayout) As List(Of Rectangle)
        Return New List(Of Rectangle) From {row.Play, row.Seek, row.Time, row.Badge, row.Speed, row.Mute, row.Volume, row.More}
    End Function

    Private Shared Sub AssertNoOverlapInside(row As TransportRules.RowLayout, width As Integer)
        Dim placed As List(Of Rectangle) = Parts(row).Where(Function(r) Not r.IsEmpty).OrderBy(Function(r) r.Left).ToList()
        For i As Integer = 1 To placed.Count - 1
            Assert.True(placed(i - 1).Right <= placed(i).Left, "parts overlap: " & placed(i - 1).ToString() & " / " & placed(i).ToString())
        Next
    End Sub

    <Fact>
    Public Sub A_wide_bar_places_every_part_left_to_right_and_ends_flush_at_the_right_edge()
        Dim row As TransportRules.RowLayout = TransportRules.ArrangeRow(Input(900))
        Assert.True(row.Play.Right < row.Seek.Left)
        Assert.True(row.Seek.Right < row.Time.Left)
        Assert.True(row.Time.Right <= row.Speed.Left)
        Assert.True(row.Speed.Right <= row.Mute.Left)
        Assert.True(row.Mute.Right <= row.Volume.Left)
        Assert.True(row.Volume.Right < row.More.Left)
        Assert.Equal(900, row.More.Right)
        Assert.True(row.Badge.IsEmpty)
        AssertNoOverlapInside(row, 900)
    End Sub

    <Fact>
    Public Sub Every_part_takes_the_bars_full_height_so_the_hit_target_is_not_the_painted_track()
        Dim row As TransportRules.RowLayout = TransportRules.ArrangeRow(Input(900))
        For Each r As Rectangle In Parts(row).Where(Function(x) Not x.IsEmpty)
            Assert.Equal(0, r.Top)
            Assert.Equal(38, r.Height)
        Next
    End Sub

    <Fact>
    Public Sub The_speed_button_keeps_its_own_width_at_every_scale_and_never_overlaps_a_neighbour()
        ' UX-406: it used to keep the 96 DPI size while the others grew; here its width is an input like the others.
        For Each scale As Double In New Double() {1.0R, 1.25R, 1.75R, 2.5R}
            Dim width As Integer = CInt(1100 * scale)
            Dim row As TransportRules.RowLayout = TransportRules.ArrangeRow(Input(width, scale, speed:=52))
            Assert.Equal(CInt(52 * scale), row.Speed.Width)
            Assert.Equal(CInt(34 * scale), row.Mute.Width)
            AssertNoOverlapInside(row, width)
        Next
    End Sub

    <Fact>
    Public Sub The_badge_sits_between_the_clock_and_the_speed_button_only_while_a_span_is_marked()
        Dim row As TransportRules.RowLayout = TransportRules.ArrangeRow(Input(900, badge:=30))
        Assert.False(row.Badge.IsEmpty)
        Assert.Equal(30, row.Badge.Width)
        Assert.True(row.Time.Right <= row.Badge.Left)
        Assert.Equal(row.Speed.Left, row.Badge.Right)
    End Sub

    <Fact>
    Public Sub A_narrow_bar_drops_the_volume_slider_first_then_the_clock_and_keeps_play_seek_and_more()
        Dim narrow As TransportRules.RowLayout = TransportRules.ArrangeRow(Input(300))
        Assert.True(narrow.Volume.IsEmpty)
        Assert.False(narrow.Play.IsEmpty)
        Assert.False(narrow.More.IsEmpty)
        Assert.True(narrow.Seek.Width >= 40)
        AssertNoOverlapInside(narrow, 300)

        Dim tiny As TransportRules.RowLayout = TransportRules.ArrangeRow(Input(190))
        Assert.True(tiny.Volume.IsEmpty)
        Assert.True(tiny.Time.IsEmpty)
        Assert.False(tiny.Play.IsEmpty)
        Assert.False(tiny.Seek.IsEmpty)
        Assert.False(tiny.More.IsEmpty)
        Assert.True(tiny.Seek.Width >= 40)
    End Sub

    <Fact>
    Public Sub Nothing_is_dropped_while_everything_fits()
        Dim row As TransportRules.RowLayout = TransportRules.ArrangeRow(Input(520))
        Assert.False(row.Volume.IsEmpty)
        Assert.False(row.Time.IsEmpty)
        Assert.False(row.Speed.IsEmpty)
        Assert.False(row.Mute.IsEmpty)
    End Sub

    ' --- the clock (UX-410) ------------------------------------------------------------------------

    <Fact>
    Public Sub Media_time_is_mm_ss_until_an_hour_then_h_mm_ss_and_dashes_for_unknown()
        Assert.Equal("00:00", TransportRules.FormatMediaTime(0))
        Assert.Equal("01:05", TransportRules.FormatMediaTime(65000))
        Assert.Equal("59:59", TransportRules.FormatMediaTime(3599000))
        Assert.Equal("1:00:00", TransportRules.FormatMediaTime(3600000))
        Assert.Equal("26:03:01", TransportRules.FormatMediaTime((26L * 3600 + 3 * 60 + 1) * 1000))
        Assert.Equal("--:--", TransportRules.FormatMediaTime(-1))
    End Sub

    <Fact>
    Public Sub The_clock_shows_elapsed_over_total_or_minus_remaining_over_total()
        Assert.Equal("01:00 / 10:00", TransportRules.FormatClock(60000, 600000, False))
        Assert.Equal("-09:00 / 10:00", TransportRules.FormatClock(60000, 600000, True))
        ' A stream has no remaining time: elapsed stays.
        Assert.Equal("01:00 / --:--", TransportRules.FormatClock(60000, -1, True))
        Assert.Equal("--:-- / --:--", TransportRules.FormatClock(-1, -1, False))
        ' Past the end (a player a hair ahead of the length) never shows a negative remaining.
        Assert.Equal("-00:00 / 10:00", TransportRules.FormatClock(601000, 600000, True))
    End Sub

    <Fact>
    Public Sub The_clock_sample_is_the_widest_text_for_the_files_length()
        Assert.Equal("-88:88 / 88:88", TransportRules.ClockSample(59000))
        Assert.Equal("-88:88 / 88:88", TransportRules.ClockSample(-1))
        Assert.Equal("-8:88:88 / 8:88:88", TransportRules.ClockSample(3723000))
        Assert.Equal("-88:88:88 / 88:88:88", TransportRules.ClockSample((12L * 3600 + 5) * 1000))
        ' Every text the clock can show for that length is no longer than its sample.
        Assert.True(TransportRules.FormatClock(3723000, 3723000, True).Length <= TransportRules.ClockSample(3723000).Length)
    End Sub

    ' --- the seek track (UX-409) -------------------------------------------------------------------

    <Fact>
    Public Sub A_point_on_the_track_is_a_fraction_clamped_to_the_ends()
        Assert.Equal(0.0R, TransportRules.FractionAt(-5, 200))
        Assert.Equal(0.5R, TransportRules.FractionAt(100, 200))
        Assert.Equal(1.0R, TransportRules.FractionAt(999, 200))
        Assert.Equal(0.0R, TransportRules.FractionAt(10, 0))
    End Sub

    <Fact>
    Public Sub The_hover_time_is_the_position_under_the_pointer_and_empty_without_a_length()
        Assert.Equal("00:30", TransportRules.SeekHoverText(50, 200, 120000))
        Assert.Equal("02:00", TransportRules.SeekHoverText(500, 200, 120000))
        Assert.Equal("", TransportRules.SeekHoverText(50, 200, -1))
        Assert.Equal("", TransportRules.SeekHoverText(50, 200, 0))
    End Sub

    <Fact>
    Public Sub The_tick_may_not_move_the_thumb_while_it_is_held_or_in_the_moment_after_a_release()
        Assert.False(TransportRules.SeekTickMayOverwrite(True, 1000, 0))
        Assert.False(TransportRules.SeekTickMayOverwrite(False, 1000, 1000 + TransportRules.Seek_Hold_Ms))
        Assert.False(TransportRules.SeekTickMayOverwrite(False, 1699, 1700))
        Assert.True(TransportRules.SeekTickMayOverwrite(False, 1700, 1700))
        Assert.True(TransportRules.SeekTickMayOverwrite(False, 5, 0))
    End Sub

    <Fact>
    Public Sub Only_a_seekable_media_of_known_length_accepts_a_seek()
        Assert.True(TransportRules.CanSeekTo(True, 1000))
        Assert.False(TransportRules.CanSeekTo(False, 1000))
        Assert.False(TransportRules.CanSeekTo(True, 0))
        Assert.False(TransportRules.CanSeekTo(True, -1))
    End Sub

    ' --- the volume (UX-408) -----------------------------------------------------------------------

    <Fact>
    Public Sub The_speaker_is_crossed_out_while_muted_and_at_level_zero()
        Assert.Equal("media.volume", TransportRules.VolumeGlyphId(False, 0.5R))
        Assert.Equal("media.mute", TransportRules.VolumeGlyphId(True, 0.5R))
        Assert.Equal("media.mute", TransportRules.VolumeGlyphId(False, 0.0R))
        Assert.Equal("media.mute", TransportRules.VolumeGlyphId(False, 0.001R))
        Assert.Equal("media.volume", TransportRules.VolumeGlyphId(False, 0.05R))
        Assert.Equal("media.mute", TransportRules.VolumeGlyphId(False, Double.NaN))
    End Sub

    <Fact>
    Public Sub The_volume_percent_is_rounded_and_clamped()
        Assert.Equal(0, TransportRules.VolumePercent(0.0R))
        Assert.Equal(75, TransportRules.VolumePercent(0.75R))
        Assert.Equal(100, TransportRules.VolumePercent(1.4R))
        Assert.Equal(0, TransportRules.VolumePercent(-1.0R))
        Assert.Equal(0, TransportRules.VolumePercent(Double.NaN))
    End Sub

    ' --- A-B repeat (UX-412) -----------------------------------------------------------------------

    <Fact>
    Public Sub The_marks_sit_at_their_fractions_and_B_appears_only_once_the_loop_is_armed()
        Dim none As TransportRules.AbMarks = TransportRules.AbMarksFor(PlaybackRules.AbPhase.None, 0, 0, 100000)
        Assert.True(none.A < 0 AndAlso none.B < 0)

        Dim onlyA As TransportRules.AbMarks = TransportRules.AbMarksFor(PlaybackRules.AbPhase.MarkedA, 25000, 0, 100000)
        Assert.Equal(0.25R, onlyA.A, 6)
        Assert.True(onlyA.B < 0)

        Dim armed As TransportRules.AbMarks = TransportRules.AbMarksFor(PlaybackRules.AbPhase.Armed, 25000, 75000, 100000)
        Assert.Equal(0.25R, armed.A, 6)
        Assert.Equal(0.75R, armed.B, 6)

        Dim noLength As TransportRules.AbMarks = TransportRules.AbMarksFor(PlaybackRules.AbPhase.Armed, 25000, 75000, 0)
        Assert.True(noLength.A < 0 AndAlso noLength.B < 0)
    End Sub

    <Fact>
    Public Sub The_badge_says_A_then_A_dash_B_and_nothing_otherwise()
        Assert.Equal("", TransportRules.AbBadgeText(PlaybackRules.AbPhase.None))
        Assert.Equal("A", TransportRules.AbBadgeText(PlaybackRules.AbPhase.MarkedA))
        Assert.Equal("A-B", TransportRules.AbBadgeText(PlaybackRules.AbPhase.Armed))
    End Sub

    ' --- repeat (UX-413) ---------------------------------------------------------------------------

    <Fact>
    Public Sub Only_turning_repeat_off_under_a_natively_looping_media_reopens_it()
        Assert.True(TransportRules.RepeatToggleNeedsReopen(startedWithRepeat:=True, repeatNow:=False))
        Assert.False(TransportRules.RepeatToggleNeedsReopen(startedWithRepeat:=False, repeatNow:=True))
        Assert.False(TransportRules.RepeatToggleNeedsReopen(startedWithRepeat:=True, repeatNow:=True))
        Assert.False(TransportRules.RepeatToggleNeedsReopen(startedWithRepeat:=False, repeatNow:=False))
    End Sub

    <Fact>
    Public Sub The_end_of_a_pass_goes_round_only_when_repeat_is_on_and_the_media_has_no_native_loop()
        Assert.True(TransportRules.RepeatAtEnd(repeatNow:=True, startedWithRepeat:=False, isLive:=False))
        Assert.False(TransportRules.RepeatAtEnd(repeatNow:=False, startedWithRepeat:=False, isLive:=False))
        Assert.False(TransportRules.RepeatAtEnd(repeatNow:=True, startedWithRepeat:=True, isLive:=False))
        Assert.False(TransportRules.RepeatAtEnd(repeatNow:=True, startedWithRepeat:=False, isLive:=True))
    End Sub

    ' --- what keeps the bar up (UX-402) --------------------------------------------------------------

    <Fact>
    Public Sub Any_one_hold_keeps_the_bar_up_and_none_lets_it_go()
        Assert.False(TransportRules.ShouldKeepBarUp(New TransportRules.HoldState()))
        Assert.True(TransportRules.ShouldKeepBarUp(New TransportRules.HoldState With {.SliderHeld = True}))
        Assert.True(TransportRules.ShouldKeepBarUp(New TransportRules.HoldState With {.PointerOnBar = True}))
        ' The speed menu and the video menu are both chrome popups: one flag covers them (UX-402).
        Assert.True(TransportRules.ShouldKeepBarUp(New TransportRules.HoldState With {.PopupOpen = True}))
        Assert.True(TransportRules.ShouldKeepBarUp(New TransportRules.HoldState With {.KeyboardRingOn = True}))
        Assert.True(TransportRules.ShouldKeepBarUp(New TransportRules.HoldState With {.PausedAndKept = True}))
    End Sub

    <Fact>
    Public Sub The_sleep_timer_is_polled_exactly_while_audio_plays_and_has_not_been_stopped()
        Assert.True(TransportRules.SleepCheckNeeded(audioNowPlaying:=True, vlcPlaying:=True, alreadyStopped:=False))
        Assert.False(TransportRules.SleepCheckNeeded(audioNowPlaying:=False, vlcPlaying:=True, alreadyStopped:=False))
        Assert.False(TransportRules.SleepCheckNeeded(audioNowPlaying:=True, vlcPlaying:=False, alreadyStopped:=False))
        Assert.False(TransportRules.SleepCheckNeeded(audioNowPlaying:=True, vlcPlaying:=True, alreadyStopped:=True))
    End Sub

    ' --- buffering (UX-405) ------------------------------------------------------------------------

    <Fact>
    Public Sub Buffering_is_said_in_steps_of_five_never_twice_and_100_says_it_is_over_once()
        Assert.Equal(0, TransportRules.NextBufferingStep(0.0F, -1))
        Assert.Equal(-1, TransportRules.NextBufferingStep(3.0F, 0))
        Assert.Equal(5, TransportRules.NextBufferingStep(7.9F, 0))
        Assert.Equal(-1, TransportRules.NextBufferingStep(9.0F, 5))
        Assert.Equal(95, TransportRules.NextBufferingStep(99.0F, 90))
        Assert.Equal(100, TransportRules.NextBufferingStep(100.0F, 95))
        ' Full again without ever having been low: nothing to take back.
        Assert.Equal(-1, TransportRules.NextBufferingStep(100.0F, -1))
        Assert.Equal(-1, TransportRules.NextBufferingStep(100.0F, 100))
        Assert.Equal(-1, TransportRules.NextBufferingStep(Single.NaN, 10))
    End Sub

    ' --- the strip under the bar (UX-415) ----------------------------------------------------------

    <Fact>
    Public Sub The_status_strip_stays_above_the_bars_slot_while_a_media_plays_whether_the_bar_is_up_or_not()
        Dim reserved As Integer = TransportRules.StatusTop(1000, 30, 0, 10, 38, reserveBarSlot:=True)
        Assert.Equal(1000 - 38 - 30 - 20, reserved)
        ' The bar's top is margin below the media's edge less its height: the strip clears it by one margin.
        Dim barTop As Integer = 1000 - 38 - 10
        Assert.Equal(barTop - 10, reserved + 30)
        Assert.Equal(1000 - 30 - 0, TransportRules.StatusTop(1000, 30, 0, 10, 38, reserveBarSlot:=False))
    End Sub

    <Fact>
    Public Sub The_status_cell_leaves_the_name_its_own_width_up_to_half_the_strip()
        Assert.Equal(700, ChromeLayout.StatusCellWidth(1000, 300, 900))
        Assert.Equal(500, ChromeLayout.StatusCellWidth(1000, 800, 900))
        Assert.Equal(100, ChromeLayout.StatusCellWidth(1000, 300, 100))
        Assert.Equal(0, ChromeLayout.StatusCellWidth(0, 300, 100))
        Assert.Equal(900, ChromeLayout.StatusCellWidth(1000, 100, 900))
    End Sub

    ' --- the popup above a low control (Ф-2) ----------------------------------------------------------

    <Fact>
    Public Sub A_popup_that_fits_below_its_control_opens_where_asked()
        Dim work As New Rectangle(0, 0, 1920, 1040)
        Dim at As Point = ChromeMenuRules.PopupOrigin(New Point(500, 300), New Size(240, 400), work, 280)
        Assert.Equal(New Point(500, 300), at)
    End Sub

    <Fact>
    Public Sub A_popup_that_does_not_fit_below_opens_above_the_control_not_over_it()
        Dim work As New Rectangle(0, 0, 1920, 1040)
        Dim at As Point = ChromeMenuRules.PopupOrigin(New Point(500, 1000), New Size(240, 400), work, 960)
        Assert.Equal(960 - 400, at.Y)
        Assert.Equal(500, at.X)
    End Sub

    <Fact>
    Public Sub Without_an_anchor_the_popup_is_clamped_as_before_and_always_stays_inside_the_work_area()
        Dim work As New Rectangle(0, 0, 1920, 1040)
        Assert.Equal(New Point(500, 640), ChromeMenuRules.PopupOrigin(New Point(500, 1000), New Size(240, 400), work, Integer.MinValue))
        Assert.Equal(1920 - 240, ChromeMenuRules.PopupOrigin(New Point(1900, 100), New Size(240, 400), work, 80).X)
        ' Taller than the room above the control: pinned to the top of the work area.
        Assert.Equal(0, ChromeMenuRules.PopupOrigin(New Point(10, 300), New Size(240, 1000), work, 200).Y)
    End Sub

    ' --- the ring inside the bar (UX-403) -------------------------------------------------------------

    <Fact>
    Public Sub The_ring_steps_inside_the_bar_and_reports_when_it_leaves()
        Assert.Equal(1, TransportRules.RingStep(0, 7, forward:=True))
        Assert.Equal(5, TransportRules.RingStep(6, 7, forward:=False))
        Assert.Equal(-1, TransportRules.RingStep(6, 7, forward:=True))
        Assert.Equal(-1, TransportRules.RingStep(0, 7, forward:=False))
        Assert.Equal(-1, TransportRules.RingStep(0, 0, forward:=True))
    End Sub

    <Fact>
    Public Sub An_arrow_on_a_focused_slider_runs_the_same_action_the_playback_keys_run()
        Assert.Equal("seekFwdShort", TransportRules.RingSliderAction(isSeek:=True, up:=True, page:=False))
        Assert.Equal("seekBackShort", TransportRules.RingSliderAction(isSeek:=True, up:=False, page:=False))
        Assert.Equal("seekFwdMedium", TransportRules.RingSliderAction(isSeek:=True, up:=True, page:=True))
        Assert.Equal("seekBackMedium", TransportRules.RingSliderAction(isSeek:=True, up:=False, page:=True))
        Assert.Equal("volumeUp", TransportRules.RingSliderAction(isSeek:=False, up:=True, page:=False))
        Assert.Equal("volumeDown", TransportRules.RingSliderAction(isSeek:=False, up:=False, page:=True))
        ' ... and every one of them is a real playback action id.
        For Each id As String In New String() {"seekFwdShort", "seekBackShort", "seekFwdMedium", "seekBackMedium", "volumeUp", "volumeDown"}
            Assert.True(CustomHotkeys.IsPlaybackAction(id), id)
        Next
    End Sub

    ' --- no second menu (acceptance 5) ----------------------------------------------------------------

    <Fact>
    Public Sub The_more_button_reaches_the_one_video_menu_and_nothing_in_the_bar_builds_a_menu_of_its_own()
        Dim bar As String = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Main_Form.VideoControls.vb"))
        Assert.Contains("ShowVideoContextMenu(", bar)
        Assert.DoesNotContain("BuildVideoMenu", bar)
        ' The speed button's strip is the one other ContextMenuStrip in the file, and its rows are the shared ladder builder's.
        Assert.Equal(1, bar.Split(New String() {"New ContextMenuStrip"}, StringSplitOptions.None).Length - 1)
        Assert.Contains("BuildSpeedLadderItems()", bar)
        Dim menu As String = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Main_Form.VideoMenu.vb"))
        Assert.Contains("BuildSpeedLadderItems()", menu)
    End Sub

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Could not locate the repository root (FastMediaSorter.sln) from the test binary.")
    End Function

End Class
#End If
