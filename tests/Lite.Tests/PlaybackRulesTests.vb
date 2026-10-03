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
    Public Sub Turning_repeat_off_clears_a_repeat_end_action_and_nothing_else()
        Assert.Equal("stay", PlaybackRules.EndActionAfterRepeatOff("repeat"))
        Assert.Equal("nextFile", PlaybackRules.EndActionAfterRepeatOff("nextFile"))
        Assert.Equal("next", PlaybackRules.EndActionAfterRepeatOff("next"))
        Assert.Equal("stay", PlaybackRules.EndActionAfterRepeatOff("stay"))
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

    <Fact>
    Public Sub Leaving_audio_lets_the_next_slideshow_step_start_fresh()
        Dim sleep As New SleepDeadline()
        sleep.TrackStarted(0, 1, automatic:=False)
        Assert.True(sleep.IsArmed)
        sleep.Disarm()
        Assert.False(sleep.IsArmed)

        ' Later automatic start (slideshow or end-action) arms fresh from nowMs
        sleep.TrackStarted(120000, 1, automatic:=True)
        Assert.True(sleep.IsArmed)
        Assert.False(sleep.IsDue(120000 + 59000, 1))
        Assert.True(sleep.IsDue(120000 + 60000, 1))
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

End Class
#End If
