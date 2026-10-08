#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

''' <summary>
''' The playback decisions audit 050 took out of Main_Form (D-03, D-11, D-14, D-16).
''' Modern-only, like PlaybackRules itself and the LibVLC paths that use it.
''' </summary>
Public Class PlaybackRulesTests

    ' --- D-14: one answer to "does it repeat" ------------------------------------------

    <Fact>
    Public Sub Repeat_is_the_loop_flag_or_the_end_action_of_the_kind_on_screen()
        ' flag, audio?, audio end, video end -> repeats
        Assert.True(PlaybackRules.EffectiveRepeat(True, False, "next", "stay"))
        Assert.True(PlaybackRules.EffectiveRepeat(True, True, "next", "stay"))
        Assert.True(PlaybackRules.EffectiveRepeat(False, False, "next", "repeat"))
        Assert.True(PlaybackRules.EffectiveRepeat(False, True, "repeat", "stay"))
        Assert.False(PlaybackRules.EffectiveRepeat(False, False, "repeat", "stay"))
        Assert.False(PlaybackRules.EffectiveRepeat(False, True, "next", "repeat"))
        Assert.False(PlaybackRules.EffectiveRepeat(False, False, "next", "nextFile"))
        Assert.False(PlaybackRules.EffectiveRepeat(False, True, "stay", "stay"))
        ' In slideshow mode, repeat is always disabled (У-09)
        Assert.False(PlaybackRules.EffectiveRepeat(True, False, "repeat", "repeat", isSlideshow:=True))
        Assert.False(PlaybackRules.EffectiveRepeat(False, True, "repeat", "repeat", isSlideshow:=True))
    End Sub

    <Fact>
    Public Sub Slideshow_video_fallback_timeout_is_at_least_60_seconds_and_5_seconds_past_duration()
        Assert.Equal(60000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(0))
        Assert.Equal(60000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(10000))
        Assert.Equal(60000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(55000))
        Assert.Equal(65000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(60000))
        Assert.Equal(185000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(180000))
    End Sub

    <Fact>
    Public Sub The_menu_ticks_the_mode_that_the_stored_end_action_names()
        Assert.Equal(PlaybackRules.End_Mode_Stay, PlaybackRules.EndModeOf("stay", False, False))
        Assert.Equal(PlaybackRules.End_Mode_Advance, PlaybackRules.EndModeOf("nextFile", False, False))
        Assert.Equal(PlaybackRules.End_Mode_Advance, PlaybackRules.EndModeOf("next", True, False))
        Assert.Equal(PlaybackRules.End_Mode_Repeat, PlaybackRules.EndModeOf("repeat", False, False))
        Assert.Equal(PlaybackRules.End_Mode_Repeat, PlaybackRules.EndModeOf("repeat", True, False))
        ' The Settings checkbox "repeat" wins over whatever the end action says.
        Assert.Equal(PlaybackRules.End_Mode_Repeat, PlaybackRules.EndModeOf("stay", False, True))
        ' A track's "next" is not a video's, and an unknown value is never shown as an advance.
        Assert.Equal(PlaybackRules.End_Mode_Stay, PlaybackRules.EndModeOf("next", False, False))
        Assert.Equal(PlaybackRules.End_Mode_Stay, PlaybackRules.EndModeOf("", False, False))
    End Sub

    <Fact>
    Public Sub Each_mode_stores_the_value_its_kind_reads_back()
        Assert.Equal("stay", PlaybackRules.StoredEndAction(PlaybackRules.End_Mode_Stay, False))
        Assert.Equal("stay", PlaybackRules.StoredEndAction(PlaybackRules.End_Mode_Stay, True))
        Assert.Equal("nextFile", PlaybackRules.StoredEndAction(PlaybackRules.End_Mode_Advance, False))
        Assert.Equal("next", PlaybackRules.StoredEndAction(PlaybackRules.End_Mode_Advance, True))
        Assert.Equal("repeat", PlaybackRules.StoredEndAction(PlaybackRules.End_Mode_Repeat, False))
        Assert.Equal("repeat", PlaybackRules.StoredEndAction(PlaybackRules.End_Mode_Repeat, True))
        For Each mode As String In {PlaybackRules.End_Mode_Stay, PlaybackRules.End_Mode_Advance, PlaybackRules.End_Mode_Repeat}
            For Each isAudio As Boolean In {False, True}
                Assert.Equal(mode, PlaybackRules.EndModeOf(PlaybackRules.StoredEndAction(mode, isAudio), isAudio, False))
            Next
        Next
    End Sub

    ' --- D-16: is there a cycle -------------------------------------------------------

    <Fact>
    Public Sub One_audio_track_is_not_a_cycle()
        Assert.Equal(PlaybackRules.No_Track, PlaybackRules.NextTrackInCycle({-1, 1}, 1, audio:=True))
        Assert.Equal(PlaybackRules.No_Track, PlaybackRules.NextTrackInCycle({-1}, -1, audio:=True))
        Assert.Equal(PlaybackRules.No_Track, PlaybackRules.NextTrackInCycle(Nothing, 1, audio:=True))
    End Sub

    <Fact>
    Public Sub Audio_cycles_real_tracks_and_never_lands_on_disable()
        Assert.Equal(2, PlaybackRules.NextTrackInCycle({-1, 1, 2}, 1, audio:=True))
        Assert.Equal(1, PlaybackRules.NextTrackInCycle({-1, 1, 2}, 2, audio:=True))
        ' From "disabled" (set elsewhere) the key brings the sound back.
        Assert.Equal(1, PlaybackRules.NextTrackInCycle({-1, 1, 2}, -1, audio:=True))
    End Sub

    <Fact>
    Public Sub One_subtitle_track_is_a_cycle_with_off_in_it()
        Assert.Equal(3, PlaybackRules.NextTrackInCycle({-1, 3}, -1, audio:=False))
        Assert.Equal(-1, PlaybackRules.NextTrackInCycle({-1, 3}, 3, audio:=False))
        Assert.Equal(PlaybackRules.No_Track, PlaybackRules.NextTrackInCycle({-1}, -1, audio:=False))
    End Sub

    ' --- D-03: the sleep deadline survives an album ----------------------------------

    <Fact>
    Public Sub An_album_played_through_stops_at_the_deadline()
        Dim sleep As New SleepDeadline()
        Const Minute As Long = 60000
        sleep.TrackStarted(0, 1, automatic:=False)
        ' Three 20-second tracks, each started by the end of the one before.
        sleep.TrackStarted(20000, 1, automatic:=True)
        sleep.TrackStarted(40000, 1, automatic:=True)
        Assert.False(sleep.IsDue(59000, 1))
        Assert.True(sleep.IsDue(Minute, 1))
    End Sub

    <Fact>
    Public Sub A_track_the_user_picks_starts_the_timer_again()
        Dim sleep As New SleepDeadline()
        sleep.TrackStarted(0, 1, automatic:=False)
        sleep.TrackStarted(50000, 1, automatic:=False)
        Assert.False(sleep.IsDue(60000, 1))
        Assert.True(sleep.IsDue(110000, 1))
    End Sub

    <Fact>
    Public Sub A_timer_switched_off_never_fires_and_a_changed_length_counts_from_the_change()
        Dim sleep As New SleepDeadline()
        sleep.TrackStarted(0, 1, automatic:=False)
        Assert.False(sleep.IsDue(120000, 0))
        Assert.False(sleep.IsArmed)

        sleep.TrackStarted(0, 1, automatic:=False)
        Assert.False(sleep.IsDue(30000, 2))       ' re-armed at 30 s for two minutes
        Assert.False(sleep.IsDue(120000, 2))
        Assert.True(sleep.IsDue(150000, 2))
    End Sub

    <Fact>
    Public Sub An_automatic_start_arms_a_timer_that_was_not_running()
        Dim sleep As New SleepDeadline()
        sleep.TrackStarted(1000, 1, automatic:=True)
        Assert.True(sleep.IsArmed)
        Assert.True(sleep.IsDue(61000, 1))
    End Sub

    ''' <summary>057.S08-6: the display path disarms the deadline when a non-audio file
    ''' is shown (EndAudioListening). After that - a track with a 30-minute timer, an
    ''' hour of photos - a slideshow step onto an audio file must arm a FRESH deadline
    ''' and play, not be cut off at the first tick by the long-passed one.</summary>
    <Fact>
    Public Sub Leaving_audio_lets_the_next_slideshow_step_start_fresh()
        Dim sleep As New SleepDeadline()
        sleep.TrackStarted(0, 30, automatic:=False)
        Assert.True(sleep.IsArmed)

        ' Two minutes of photos: the display path ends the listening session.
        sleep.Disarm()
        Assert.False(sleep.IsArmed)

        ' The slideshow step lands on an audio file.
        sleep.TrackStarted(120000, 30, automatic:=True)
        Assert.True(sleep.IsArmed)
        Assert.False(sleep.IsDue(121000, 30))
        Assert.True(sleep.IsDue(120000 + 30 * 60000L, 30))
    End Sub

    ' --- D-11: watched to the end opens from the start --------------------------------

    <Theory>
    <InlineData(1.0F)>
    <InlineData(0.995F)>
    <InlineData(0.97F)>
    Public Sub A_position_at_the_end_clears_the_resume_point(finalPosition As Single)
        Dim prefs As New ModernViewerPreferences() With {.RememberVideoPosition = True}
        prefs.StoreVideoPosition("C:\v\film.mkv", 10, 20, 0.5F)
        Assert.Equal(0.5F, prefs.RememberedVideoPosition("C:\v\film.mkv", 10, 20))

        prefs.StoreVideoPosition("C:\v\film.mkv", 10, 20, finalPosition)
        Assert.Equal(-1.0F, prefs.RememberedVideoPosition("C:\v\film.mkv", 10, 20))
    End Sub

    ''' <summary>Zero or less is a player that has not started or already stopped: it says
    ''' nothing about where the user was, so the entry stays.</summary>
    <Fact>
    Public Sub A_position_of_zero_leaves_the_resume_point_alone()
        Dim prefs As New ModernViewerPreferences() With {.RememberVideoPosition = True}
        prefs.StoreVideoPosition("C:\v\film.mkv", 10, 20, 0.5F)
        prefs.StoreVideoPosition("C:\v\film.mkv", 10, 20, 0.0F)
        prefs.StoreVideoPosition("C:\v\film.mkv", 10, 20, -1.0F)
        Assert.Equal(0.5F, prefs.RememberedVideoPosition("C:\v\film.mkv", 10, 20))
    End Sub

    ' --- ticket 119 Ф-1: the speed ladder ---------------------------------------------

    <Fact>
    Public Sub NextRate_walks_the_whole_ladder_in_both_directions_and_clamps_without_wrapping()
        ' Up from the bottom, one step at a time.
        Assert.Equal(0.75F, PlaybackRules.NextRate(0.5F, faster:=True))
        Assert.Equal(1.0F, PlaybackRules.NextRate(0.75F, faster:=True))
        Assert.Equal(1.25F, PlaybackRules.NextRate(1.0F, faster:=True))
        Assert.Equal(1.5F, PlaybackRules.NextRate(1.25F, faster:=True))
        Assert.Equal(2.0F, PlaybackRules.NextRate(1.5F, faster:=True))
        ' The top clamps - a held key must not wrap from 2x to 0.5x (119 §5.0.4).
        Assert.Equal(2.0F, PlaybackRules.NextRate(2.0F, faster:=True))
        ' And back down.
        Assert.Equal(1.5F, PlaybackRules.NextRate(2.0F, faster:=False))
        Assert.Equal(0.5F, PlaybackRules.NextRate(0.75F, faster:=False))
        Assert.Equal(0.5F, PlaybackRules.NextRate(0.5F, faster:=False))
    End Sub

    <Fact>
    Public Sub An_off_ladder_rate_snaps_to_the_nearest_step_ties_up()
        ' 1.1 is nearest 1.0: the next FASTER step is taken from there.
        Assert.Equal(1.25F, PlaybackRules.NextRate(1.1F, faster:=True))
        ' Exactly between two steps: ties up to 1.25.
        Assert.Equal(1.5F, PlaybackRules.NextRate(1.125F, faster:=True))
        Assert.Equal(1.0F, PlaybackRules.NextRate(1.125F, faster:=False))
    End Sub

    <Fact>
    Public Sub A_dead_rate_is_treated_as_normal()
        Assert.Equal(1.25F, PlaybackRules.NextRate(Single.NaN, faster:=True))
        Assert.Equal(0.75F, PlaybackRules.NextRate(Single.NaN, faster:=False))
        Assert.Equal(1.25F, PlaybackRules.NextRate(0.0F, faster:=True))
        Assert.Equal(1.25F, PlaybackRules.NextRate(-1.0F, faster:=True))
    End Sub

    <Theory>
    <InlineData("de-DE")>
    <InlineData("ar")>
    Public Sub FormatRate_is_culture_invariant(culture As String)
        Dim saved = System.Globalization.CultureInfo.CurrentCulture
        Dim saved_Ui = System.Globalization.CultureInfo.CurrentUICulture
        Try
            System.Globalization.CultureInfo.CurrentCulture = New System.Globalization.CultureInfo(culture)
            System.Globalization.CultureInfo.CurrentUICulture = New System.Globalization.CultureInfo(culture)
            Assert.Equal("1×", PlaybackRules.FormatRate(1.0F))
            Assert.Equal("1.25×", PlaybackRules.FormatRate(1.25F))
            Assert.Equal("0.5×", PlaybackRules.FormatRate(0.5F))
            Assert.Equal("2×", PlaybackRules.FormatRate(2.0F))
        Finally
            System.Globalization.CultureInfo.CurrentCulture = saved
            System.Globalization.CultureInfo.CurrentUICulture = saved_Ui
        End Try
    End Sub

    <Fact>
    Public Sub FormatRate_never_shows_trailing_zeros_or_a_dead_value()
        Assert.Equal("1×", PlaybackRules.FormatRate(1.0F))                  ' no "1.00×"
        Assert.Equal("1×", PlaybackRules.FormatRate(Single.NaN))            ' no "NaN×"
        Assert.Equal("1×", PlaybackRules.FormatRate(-3.0F))
    End Sub

    ' --- 119 Ф-1: the fallback timeout answers to the rate ----------------------------

    <Fact>
    Public Sub The_slideshow_fallback_waits_for_a_slowed_clip()
        Assert.Equal(245000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(120000, 0.5F))
        Assert.Equal(65000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(120000, 2.0F))
        ' The 60 s floor still holds, and an unknown duration keeps it.
        Assert.Equal(60000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(5000, 1.0F))
        Assert.Equal(60000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(0, 0.5F))
        ' A dead rate plays as 1.0 rather than dividing by zero.
        Assert.Equal(125000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(120000, 0.0F))
        Assert.Equal(125000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(120000, Single.NaN))
        ' A rate can never overflow the Integer the timer's Interval wants.
        Assert.Equal(Integer.MaxValue, PlaybackRules.SlideshowVideoFallbackTimeoutMs(Long.MaxValue, 0.5F))
    End Sub

    <Fact>
    Public Sub The_one_argument_fallback_keeps_todays_value()
        ' The original callers and tests call it without a rate.
        Assert.Equal(PlaybackRules.SlideshowVideoFallbackTimeoutMs(120000, 1.0F),
                     PlaybackRules.SlideshowVideoFallbackTimeoutMs(120000))
        Assert.Equal(60000, PlaybackRules.SlideshowVideoFallbackTimeoutMs(10000))
    End Sub

    ' --- 119 Ф-2: seek arithmetic -----------------------------------------------------

    <Theory>
    <InlineData(60000, 600000, 3, 63000)>
    <InlineData(60000, 600000, 10, 70000)>
    <InlineData(60000, 600000, 60, 120000)>
    <InlineData(30000, 600000, -3, 27000)>
    <InlineData(30000, 600000, -10, 20000)>
    <InlineData(30000, 600000, -60, 0)>
    Public Sub A_seek_step_moves_the_recorded_positions(timeMs As Long, lengthMs As Long, deltaSec As Integer, expectedMs As Long)
        Assert.Equal(expectedMs, PlaybackRules.SeekTargetMs(timeMs, lengthMs, deltaSec))
    End Sub

    <Fact>
    Public Sub A_back_step_never_goes_before_the_start()
        Assert.Equal(0, PlaybackRules.SeekTargetMs(2000, 600000, -3))
        Assert.Equal(0, PlaybackRules.SeekTargetMs(0, 600000, -10))
    End Sub

    <Fact>
    Public Sub A_forward_step_never_crosses_the_end_guard_and_never_moves_backwards()
        Assert.Equal(599000, PlaybackRules.SeekTargetMs(590000, 600000, 60))   ' clamps to length - 1000
        Assert.Equal(599500, PlaybackRules.SeekTargetMs(599500, 600000, 60))   ' already inside the guard: stays
        Assert.Equal(539500, PlaybackRules.SeekTargetMs(599500, 600000, -60))  ' a back step out of the guard is still free to move
    End Sub

    <Fact>
    Public Sub A_seek_without_a_known_length_refuses()
        Assert.Equal(-1, PlaybackRules.SeekTargetMs(1000, 0, 10))
        Assert.Equal(-1, PlaybackRules.SeekTargetMs(1000, -1, 10))
        Assert.Equal(-1, PlaybackRules.SeekTargetMs(-1, 600000, 10))
    End Sub

    <Theory>
    <InlineData(0, 1)>
    <InlineData(-5, 1)>
    <InlineData(601, 600)>
    <InlineData(99999, 600)>
    <InlineData(1, 1)>
    <InlineData(600, 600)>
    Public Sub A_seek_step_survives_a_hand_edited_value(value As Integer, expected As Integer)
        Assert.Equal(expected, PlaybackRules.ClampSeekStepSec(value))
    End Sub

    ' --- 119 Ф-4: the A-B state machine (every row of the Cycle table) ----------------

    <Fact>
    Public Sub The_first_press_marks_A_and_the_second_arms_the_span()
        Dim ab As New AbLoopState()
        Assert.Equal(AbCycleResult.MarkedA, ab.Cycle(10000, 600000))
        Assert.Equal(AbPhase.MarkedA, ab.Phase)
        Assert.Equal(10000, ab.AMs)
        Assert.Equal(AbCycleResult.Armed, ab.Cycle(30000, 600000))
        Assert.Equal(AbPhase.Armed, ab.Phase)
        Assert.Equal(30000, ab.BMs)
    End Sub

    <Fact>
    Public Sub Mark_B_before_A_or_within_one_second_of_A_is_refused_and_keeps_A()
        Dim ab As New AbLoopState()
        ab.Cycle(10000, 600000)
        Assert.Equal(AbCycleResult.RejectedBTooClose, ab.Cycle(9999, 600000))  ' B before A
        Assert.Equal(AbCycleResult.RejectedBTooClose, ab.Cycle(10999, 600000)) ' exactly A + 999 ms
        Assert.Equal(AbPhase.MarkedA, ab.Phase)
        Assert.Equal(10000, ab.AMs)
        ' Exactly A + 1000 ms is the minimum span - accepted.
        Assert.Equal(AbCycleResult.Armed, ab.Cycle(11000, 600000))
    End Sub

    <Fact>
    Public Sub Mark_B_in_the_last_half_second_is_refused()
        Dim ab As New AbLoopState()
        ab.Cycle(0, 600000)
        ' Exactly length - 500 ms is the last place B may sit.
        Assert.Equal(AbCycleResult.Armed, ab.Cycle(599500, 600000))
        Dim again As New AbLoopState()
        again.Cycle(0, 600000)
        Assert.Equal(AbCycleResult.RejectedBTooCloseToEnd, again.Cycle(599501, 600000))
        Assert.Equal(AbPhase.MarkedA, again.Phase)
    End Sub

    <Fact>
    Public Sub A_press_on_an_armed_loop_clears_it()
        Dim ab As New AbLoopState()
        ab.Cycle(10000, 600000)
        ab.Cycle(30000, 600000)
        Assert.Equal(AbCycleResult.Cleared, ab.Cycle(40000, 600000))
        Assert.Equal(AbPhase.None, ab.Phase)
    End Sub

    <Fact>
    Public Sub A_stream_or_an_unknown_time_cannot_loop_anything()
        Dim ab As New AbLoopState()
        Assert.Equal(AbCycleResult.RejectedNoLength, ab.Cycle(1000, 0))
        Assert.Equal(AbCycleResult.RejectedNoLength, ab.Cycle(1000, -1))
        Assert.Equal(AbCycleResult.RejectedNoLength, ab.Cycle(-1, 600000))
        Assert.Equal(AbPhase.None, ab.Phase)
    End Sub

    <Fact>
    Public Sub The_watcher_jumps_only_at_B_within_the_late_window_and_outside_the_cooldown()
        Dim ab As New AbLoopState()
        ab.Cycle(10000, 600000)
        ab.Cycle(30000, 600000)

        Assert.False(ab.IsDueToJump(29999, 100000))            ' before B
        Assert.True(ab.IsDueToJump(30000, 100000))             ' at B
        Assert.True(ab.IsDueToJump(32000, 100000))             ' inside the late window
        Assert.False(ab.IsDueToJump(33000, 100000))            ' past B + 3000: the span is behind us

        ' The cooldown makes "exactly once per pass" true even though Time = A is
        ' asynchronous and the next tick may still read a time past B.
        ab.RecordJump(100000)
        Assert.False(ab.IsDueToJump(30000, 100300))            ' 300 ms into the 400 ms cooldown
        Assert.True(ab.IsDueToJump(30000, 100400))             ' cooldown over

        ' A seek far past the span plays on.
        Assert.False(ab.IsDueToJump(500000, 101000))
    End Sub

    <Fact>
    Public Sub Clear_resets_everything_including_the_cooldown()
        Dim ab As New AbLoopState()
        ab.Cycle(10000, 600000)
        ab.Cycle(30000, 600000)
        ab.RecordJump(5000)
        ab.Clear()
        Assert.Equal(AbPhase.None, ab.Phase)
        Assert.Equal(0, ab.AMs)
        Assert.Equal(0, ab.BMs)
        ' The cooldown is part of the state: a fresh loop must be allowed its first jump.
        ab.Cycle(10000, 600000)
        ab.Cycle(30000, 600000)
        Assert.True(ab.IsDueToJump(30000, 6))
    End Sub

    ' --- 119 Ф-5: the frame-step plan -------------------------------------------------

    <Fact>
    Public Sub One_press_is_at_most_one_frame()
        Assert.Equal(PlaybackRules.FrameStepAction.None, PlaybackRules.PlanFrameStep(True, False, False))
        Assert.Equal(PlaybackRules.FrameStepAction.None, PlaybackRules.PlanFrameStep(False, False, False))
        Assert.Equal(PlaybackRules.FrameStepAction.PauseOnly, PlaybackRules.PlanFrameStep(True, True, False))
        Assert.Equal(PlaybackRules.FrameStepAction.StepOne, PlaybackRules.PlanFrameStep(True, True, True))
        Assert.Equal(PlaybackRules.FrameStepAction.StepOne, PlaybackRules.PlanFrameStep(False, True, False))
    End Sub

    ' --- 119 Ф-6: the cursor rule -----------------------------------------------------

    <Theory>
    <InlineData(-1, 0)>
    <InlineData(0, 0)>
    <InlineData(1, 1)>
    <InlineData(30, 30)>
    <InlineData(31, 30)>
    Public Sub The_hide_delay_clamps_to_never_or_one_to_thirty(value As Integer, expected As Integer)
        Assert.Equal(expected, PlaybackRules.ClampHideCursorSec(value))
    End Sub

    <Fact>
    Public Sub The_pointer_hides_only_when_asked_in_a_hiding_mode_off_the_chrome()
        ' 0 = never.
        Assert.False(PlaybackRules.ShouldHideCursor(0, True, True, 999999, False))
        ' Neither mode: a still pointer in the ordinary window is nobody's business.
        Assert.False(PlaybackRules.ShouldHideCursor(3, False, False, 999999, False))
        ' Over the chrome - the button the user is about to press.
        Assert.False(PlaybackRules.ShouldHideCursor(3, True, True, 999999, True))
        ' Still for shorter than the delay.
        Assert.False(PlaybackRules.ShouldHideCursor(3, True, False, 2999, False))
        ' Exactly the delay hides, and so does longer.
        Assert.True(PlaybackRules.ShouldHideCursor(3, True, False, 3000, False))
        Assert.True(PlaybackRules.ShouldHideCursor(3, False, True, 4000, False))
    End Sub

    <Theory>
    <InlineData(0, 0, 3, 0, False)>
    <InlineData(0, 0, 0, 3, False)>
    <InlineData(0, 0, -3, 0, False)>
    <InlineData(0, 0, 4, 0, True)>
    <InlineData(0, 0, 0, 4, True)>
    <InlineData(0, 0, -4, 0, True)>
    <InlineData(100, 100, 104, 103, True)>
    Public Sub Layout_jitter_is_not_a_move(oldX As Integer, oldY As Integer, newX As Integer, newY As Integer, moved As Boolean)
        Assert.Equal(moved, PlaybackRules.PointerMoved(oldX, oldY, newX, newY))
    End Sub

End Class
#End If
