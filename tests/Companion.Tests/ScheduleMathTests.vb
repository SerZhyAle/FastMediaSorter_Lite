Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>The next-slot function and the missed-run rule (ticket 222, sections 4.2 and 5): pure.</summary>
Public Class ScheduleMathTests

    Private Shared Function At(day As Integer, hour As Integer, minute As Integer, Optional second As Integer = 0) As DateTime
        Return New DateTime(2026, 10, day, hour, minute, second, DateTimeKind.Unspecified)
    End Function

    Private Shared ReadOnly StartDay As New DateTime(2026, 10, 13)

    <Fact>
    Public Sub Anchor_in_the_future_is_the_first_slot()
        ' Saved at 10:00 with a 18:00 anchor and a 6 h interval: it first runs at 18:00, not at 12:00 of a
        ' grid extended backwards.
        Assert.Equal(At(13, 18, 0), ScheduleMath.NextSlot(StartDay, 18 * 60, 6 * 60, At(13, 10, 0)))
    End Sub

    <Fact>
    Public Sub Anchor_today_in_the_past_gives_the_next_whole_step()
        Assert.Equal(At(14, 3, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, 24 * 60, At(13, 10, 0)))
        Assert.Equal(At(13, 15, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, 6 * 60, At(13, 10, 0)))
        Assert.Equal(At(13, 21, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, 6 * 60, At(13, 15, 0)))
    End Sub

    <Fact>
    Public Sub The_slot_is_strictly_after_now()
        Assert.Equal(At(14, 3, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, 24 * 60, At(13, 3, 0)))
        Assert.Equal(At(13, 3, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, 24 * 60, At(13, 2, 59, 59)))
    End Sub

    <Fact>
    Public Sub A_long_absence_lands_on_the_first_grid_point_after_now_not_a_stale_one()
        ' Missed five days: one next slot, computed from now.
        Assert.Equal(At(19, 3, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, 24 * 60, At(18, 12, 0)))
        ' ... and the grid kept its phase: a 5 h interval from 03:00 is still 03:00 + 5k hours.
        Dim after As DateTime = ScheduleMath.NextSlot(StartDay, 3 * 60, 5 * 60, At(18, 12, 0))
        Dim first As DateTime = ScheduleMath.FirstSlot(StartDay, 3 * 60)
        Assert.Equal(0, CInt((after - first).TotalMinutes) Mod (5 * 60))
        Assert.True(after > At(18, 12, 0))
        Assert.True(after - At(18, 12, 0) <= TimeSpan.FromHours(5))
    End Sub

    <Fact>
    Public Sub A_run_that_took_long_does_not_move_the_grid()
        ' 03:00 daily; the run started at 03:00 and ended at 03:40. The next slot is tomorrow 03:00 whichever
        ' of those two moments it is asked from - there is no drift.
        Dim fromStart As DateTime = ScheduleMath.NextSlot(StartDay, 3 * 60, 24 * 60, At(13, 3, 0))
        Dim fromEnd As DateTime = ScheduleMath.NextSlot(StartDay, 3 * 60, 24 * 60, At(13, 3, 40))
        Assert.Equal(At(14, 3, 0), fromStart)
        Assert.Equal(fromStart, fromEnd)
        ' ... and for a 1 h interval the slot after a 40-minute run is the next whole hour.
        Assert.Equal(At(13, 4, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, 60, At(13, 3, 40)))
    End Sub

    <Fact>
    Public Sub Interval_floor_and_zero_correction()
        Assert.Equal(60, ScheduleMath.NormalizeInterval(0))
        Assert.Equal(60, ScheduleMath.NormalizeInterval(-5))
        Assert.Equal(15, ScheduleMath.NormalizeInterval(1))
        Assert.Equal(15, ScheduleMath.NormalizeInterval(14))
        Assert.Equal(15, ScheduleMath.NormalizeInterval(15))
        Assert.Equal(90, ScheduleMath.NormalizeInterval(90))
        Assert.Equal(ScheduleMath.MaxIntervalMinutes, ScheduleMath.NormalizeInterval(Integer.MaxValue))
        ' The slot function applies the same correction, so an uncorrected 0 can never mean "run constantly".
        Assert.Equal(At(13, 11, 0), ScheduleMath.NextSlot(StartDay, 0, 0, At(13, 10, 0, 1)))
    End Sub

    <Fact>
    Public Sub Multi_day_intervals_count_from_the_start_date()
        ' Weekly from Tuesday 13th at 03:00.
        Dim weekly As Integer = 7 * 24 * 60
        Assert.Equal(At(20, 3, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, weekly, At(14, 12, 0)))
        Assert.Equal(New DateTime(2026, 10, 27, 3, 0, 0), ScheduleMath.NextSlot(StartDay, 3 * 60, weekly, At(20, 3, 0)))
    End Sub

    <Fact>
    Public Sub Last_slot_at_or_before_now()
        Assert.Null(ScheduleMath.LastSlotAtOrBefore(StartDay, 3 * 60, 24 * 60, At(13, 2, 0)))
        Assert.Equal(At(13, 3, 0), ScheduleMath.LastSlotAtOrBefore(StartDay, 3 * 60, 24 * 60, At(13, 3, 0)))
        Assert.Equal(At(13, 3, 0), ScheduleMath.LastSlotAtOrBefore(StartDay, 3 * 60, 24 * 60, At(13, 23, 0)))
    End Sub

    ' --- the missed-run rule ---------------------------------------------------------------------------

    Private Shared Function Armed(nextRun As DateTime?, Optional enabled As Boolean = True, Optional halt As ScheduledHalt = ScheduledHalt.None) As ScheduledOperation
        Dim op As New ScheduledOperation With {.Id = "a", .Enabled = enabled, .Halt = halt}
        op.Record.NextRunAt = nextRun
        Return op
    End Function

    <Fact>
    Public Sub A_slot_in_the_future_or_none_just_arms()
        Dim now As DateTime = At(13, 12, 0)
        Assert.Equal(StartupDecision.Arm, MissedRunRules.DecideAtStart(Armed(At(13, 15, 0)), now, MissedRunPolicy.Skip, False))
        Assert.Equal(StartupDecision.Arm, MissedRunRules.DecideAtStart(Armed(Nothing), now, MissedRunPolicy.RunOnceAtStart, False))
    End Sub

    <Fact>
    Public Sub A_slot_that_passed_follows_the_policy()
        Dim now As DateTime = At(13, 12, 0)
        Assert.Equal(StartupDecision.RunLate, MissedRunRules.DecideAtStart(Armed(At(13, 3, 0)), now, MissedRunPolicy.RunOnceAtStart, False))
        Assert.Equal(StartupDecision.Missed, MissedRunRules.DecideAtStart(Armed(At(13, 3, 0)), now, MissedRunPolicy.Skip, False))
        ' Exactly due counts as due.
        Assert.Equal(StartupDecision.Missed, MissedRunRules.DecideAtStart(Armed(now), now, MissedRunPolicy.Skip, False))
    End Sub

    <Fact>
    Public Sub A_paused_scheduler_has_no_missed_runs()
        Dim now As DateTime = At(13, 12, 0)
        Assert.Equal(StartupDecision.Idle, MissedRunRules.DecideAtStart(Armed(At(13, 3, 0)), now, MissedRunPolicy.RunOnceAtStart, standDown:=True))
    End Sub

    <Fact>
    Public Sub Disabled_and_halted_operations_have_no_missed_runs()
        Dim now As DateTime = At(13, 12, 0)
        Assert.Equal(StartupDecision.Idle, MissedRunRules.DecideAtStart(Armed(At(13, 3, 0), enabled:=False), now, MissedRunPolicy.RunOnceAtStart, False))
        Assert.Equal(StartupDecision.Idle, MissedRunRules.DecideAtStart(Armed(At(13, 3, 0), halt:=ScheduledHalt.ResourceRemoved), now, MissedRunPolicy.RunOnceAtStart, False))
        ' A walled one catches up with nothing: it waits for its cause to go.
        Assert.Equal(StartupDecision.Arm, MissedRunRules.DecideAtStart(Armed(At(13, 3, 0), halt:=ScheduledHalt.Wall), now, MissedRunPolicy.RunOnceAtStart, False))
    End Sub

End Class
