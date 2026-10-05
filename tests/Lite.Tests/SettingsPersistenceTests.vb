Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' 105 L09 (057.S13-11): "saved" means every write took AND no newer edit is waiting; a failed
''' flush is retried 3 times at 1/4/10 s, never in a hot loop; flushes do not overlap; exhaustion
''' keeps the state dirty and says so once; the next edit starts over. The schedule is asserted
''' against a recording timer - no test waits for time to pass.
''' </summary>
Public Class SettingsPersistenceTests
    <Fact>
    Public Sub Short_live_settings_debounce_keeps_the_same_failure_retry_guarantees()
        Dim delay As Integer
        Dim writes As Integer
        Dim scheduler As New SettingsFlushScheduler(Function()
                                                        writes += 1
                                                        Return writes > 1
                                                    End Function,
                                                    Sub(ms) delay = ms, Sub()
                                                                           End Sub,
                                                    Sub(message)
                                                    End Sub, 1500)
        scheduler.MarkDirty()
        Assert.Equal(1500, delay)
        scheduler.OnTimerTick()
        Assert.True(scheduler.IsDirty)
        Assert.Equal(SettingsFlushScheduler.RetryDelaysMs(0), delay)
        scheduler.OnTimerTick()
        Assert.False(scheduler.IsDirty)
        Assert.Equal(2, writes)
    End Sub

    Private NotInheritable Class Rig
        Friend ReadOnly Scheduler As SettingsFlushScheduler
        Friend ReadOnly TimerStarts As New List(Of Integer)()
        Friend ReadOnly Log As New List(Of String)()
        Friend TimerStops As Integer
        Friend FlushCalls As Integer
        Friend Outcomes As New Queue(Of Boolean)()
        Friend DuringFlush As Action = Nothing

        Friend Sub New()
            Scheduler = New SettingsFlushScheduler(
                Function()
                    FlushCalls += 1
                    If DuringFlush IsNot Nothing Then DuringFlush()
                    Return If(Outcomes.Count > 0, Outcomes.Dequeue(), True)
                End Function,
                Sub(ms) TimerStarts.Add(ms),
                Sub() TimerStops += 1,
                Sub(message) Log.Add(message))
        End Sub
    End Class

    <Fact>
    Public Sub An_edit_schedules_the_idle_flush_and_a_successful_flush_clears_dirty()
        Dim rig As New Rig()
        Assert.False(rig.Scheduler.IsDirty)
        rig.Scheduler.MarkDirty()
        Assert.True(rig.Scheduler.IsDirty)
        Assert.Equal(New Integer() {SettingsFlushScheduler.IdleDelayMs}, rig.TimerStarts.ToArray())

        rig.Scheduler.OnTimerTick()
        Assert.Equal(1, rig.FlushCalls)
        Assert.False(rig.Scheduler.IsDirty)
    End Sub

    <Fact>
    Public Sub A_tick_with_nothing_dirty_does_not_write()
        Dim rig As New Rig()
        rig.Scheduler.OnTimerTick()
        Assert.Equal(0, rig.FlushCalls)
    End Sub

    <Fact>
    Public Sub A_failed_flush_keeps_dirty_and_retries_at_1_4_and_10_seconds_then_stops_and_says_so_once()
        Dim rig As New Rig()
        For i As Integer = 1 To 10
            rig.Outcomes.Enqueue(False)
        Next
        rig.Scheduler.MarkDirty()
        rig.TimerStarts.Clear()

        ' One tick per timer the scheduler actually started: the first attempt + its 3 retries. After
        ' the last retry no timer is running, so no further tick can come.
        For i As Integer = 1 To 4
            rig.Scheduler.OnTimerTick()
        Next

        Assert.Equal(New Integer() {1000, 4000, 10000}, rig.TimerStarts.ToArray())
        Assert.True(rig.Scheduler.IsDirty)
        Assert.Equal(4, rig.FlushCalls) ' the first attempt + 3 retries, then silence
        Assert.Single(rig.Log, Function(m) m.Contains("still incomplete"))
    End Sub

    <Fact>
    Public Sub A_retry_that_succeeds_clears_dirty_and_resets_the_series()
        Dim rig As New Rig()
        rig.Outcomes.Enqueue(False)
        rig.Outcomes.Enqueue(True)
        rig.Scheduler.MarkDirty()
        rig.Scheduler.OnTimerTick()
        Assert.True(rig.Scheduler.IsDirty)
        Assert.Equal(1, rig.Scheduler.RetriesUsed)
        rig.Scheduler.OnTimerTick()
        Assert.False(rig.Scheduler.IsDirty)
        Assert.Equal(0, rig.Scheduler.RetriesUsed)
    End Sub

    <Fact>
    Public Sub A_new_edit_after_exhaustion_starts_a_fresh_series()
        Dim rig As New Rig()
        For i As Integer = 1 To 4
            rig.Outcomes.Enqueue(False)
        Next
        rig.Scheduler.MarkDirty()
        For i As Integer = 1 To 4
            rig.Scheduler.OnTimerTick()
        Next
        rig.TimerStarts.Clear()
        rig.Outcomes.Enqueue(False)

        rig.Scheduler.MarkDirty()
        Assert.Equal(0, rig.Scheduler.RetriesUsed)
        rig.Scheduler.OnTimerTick()
        Assert.Equal(New Integer() {SettingsFlushScheduler.IdleDelayMs, 1000}, rig.TimerStarts.ToArray())
    End Sub

    <Fact>
    Public Sub An_edit_made_during_a_flush_is_not_lost_by_that_flush_succeeding()
        Dim rig As New Rig()
        rig.Scheduler.MarkDirty()
        rig.DuringFlush = Sub()
                              rig.DuringFlush = Nothing
                              rig.Scheduler.MarkDirty() ' a newer generation arrives mid-write
                          End Sub
        rig.Scheduler.OnTimerTick()
        Assert.True(rig.Scheduler.IsDirty) ' the generation that was flushed is not the generation now
        Assert.Equal(SettingsFlushScheduler.IdleDelayMs, rig.TimerStarts.Last()) ' and its own flush is scheduled

        rig.Scheduler.OnTimerTick()
        Assert.False(rig.Scheduler.IsDirty)
        Assert.Equal(2, rig.FlushCalls)
    End Sub

    <Fact>
    Public Sub A_failed_flush_does_not_override_the_schedule_of_an_edit_made_during_it()
        Dim rig As New Rig()
        rig.Outcomes.Enqueue(False)
        rig.Scheduler.MarkDirty()
        rig.DuringFlush = Sub()
                              rig.DuringFlush = Nothing
                              rig.Scheduler.MarkDirty()
                          End Sub
        rig.TimerStarts.Clear()
        rig.Scheduler.OnTimerTick()
        Assert.Equal(New Integer() {SettingsFlushScheduler.IdleDelayMs}, rig.TimerStarts.ToArray()) ' no 1 s retry on top
    End Sub

    <Fact>
    Public Sub A_timer_tick_arriving_during_a_flush_does_not_start_a_second_one()
        Dim rig As New Rig()
        rig.Scheduler.MarkDirty()
        rig.DuringFlush = Sub()
                              rig.DuringFlush = Nothing
                              rig.Scheduler.OnTimerTick() ' re-entry
                          End Sub
        rig.Scheduler.OnTimerTick()
        Assert.Equal(1, rig.FlushCalls)
    End Sub

    <Fact>
    Public Sub A_close_time_save_that_fails_reports_it_and_schedules_no_retry()
        Dim rig As New Rig()
        rig.Outcomes.Enqueue(False)
        rig.Scheduler.MarkDirty()
        rig.TimerStarts.Clear()
        Assert.False(rig.Scheduler.SaveNow())
        Assert.True(rig.Scheduler.IsDirty)
        Assert.Empty(rig.TimerStarts) ' a close does not wait out retry delays
        Assert.Contains(rig.Log, Function(m) m.Contains("incomplete"))
    End Sub

    <Fact>
    Public Sub A_flush_that_throws_is_a_failed_flush_not_an_escaping_exception()
        Dim thrown As Boolean
        Dim scheduler As New SettingsFlushScheduler(
            Function() As Boolean
                thrown = True
                Throw New InvalidOperationException("registry gone")
            End Function, Sub(ms) Return, Sub() Return, Sub(m) Return)
        scheduler.MarkDirty()
        scheduler.OnTimerTick()
        Assert.True(thrown)
        Assert.True(scheduler.IsDirty)
    End Sub

    ' ------------------------------------------------------------------ the writes ----

    <Fact>
    Public Sub A_failed_deliberate_save_of_a_clean_block_becomes_dirty_and_recovers_on_retry()
        Dim rig As New Rig()
        rig.Outcomes.Enqueue(False)
        rig.Outcomes.Enqueue(True)
        Assert.False(rig.Scheduler.SaveNow(scheduleRetries:=True))
        Assert.True(rig.Scheduler.IsDirty)
        Assert.Equal(New Integer() {1000}, rig.TimerStarts.ToArray())
        rig.Scheduler.OnTimerTick()
        Assert.False(rig.Scheduler.IsDirty)
        Assert.Equal(2, rig.FlushCalls)
    End Sub

    <Fact>
    Public Sub A_failed_close_save_of_a_clean_block_retains_pending_state_without_starting_a_timer()
        Dim rig As New Rig()
        rig.Outcomes.Enqueue(False)
        Assert.False(rig.Scheduler.SaveNow())
        Assert.True(rig.Scheduler.IsDirty)
        Assert.Empty(rig.TimerStarts)
    End Sub

    <Fact>
    Public Sub A_reentrant_tick_does_not_stop_the_timer_of_a_newer_edit()
        Dim rig As New Rig()
        rig.Scheduler.MarkDirty()
        rig.DuringFlush = Sub()
                              rig.DuringFlush = Nothing
                              rig.Scheduler.MarkDirty()
                              Dim stops As Integer = rig.TimerStops
                              rig.Scheduler.OnTimerTick()
                              Assert.Equal(stops, rig.TimerStops)
                          End Sub
        rig.Scheduler.OnTimerTick()
        Assert.True(rig.Scheduler.IsDirty)
        rig.Scheduler.OnTimerTick()
        Assert.False(rig.Scheduler.IsDirty)
    End Sub

    Private Shared Function Block() As List(Of KeyValuePair(Of String, String))
        Dim list As New List(Of KeyValuePair(Of String, String))()
        list.Add(New KeyValuePair(Of String, String)("ImageFolder", "D:\Photos"))
        For z As Integer = 1 To 9
            list.Add(New KeyValuePair(Of String, String)("MoveOn" & z.ToString(), "E:\Slot" & z.ToString()))
        Next
        list.Add(New KeyValuePair(Of String, String)("MoveOn0", "E:\Slot0"))
        list.Add(New KeyValuePair(Of String, String)("RecentFolders", "a|b"))
        Return list
    End Function

    <Theory>
    <InlineData("ImageFolder")>
    <InlineData("MoveOn1")>
    <InlineData("MoveOn0")>
    <InlineData("RecentFolders")>
    Public Sub One_failing_write_is_named_and_every_other_value_is_still_written(failing As String)
        Dim store As New Dictionary(Of String, String)()
        Dim result As SettingsWriteResult = SettingsWriter.WriteAll(
            Block(), Sub(name, value)
                         If name = failing Then Throw New UnauthorizedAccessException("denied")
                         store(name) = value
                     End Sub)
        Assert.False(result.Succeeded)
        Assert.Equal(New String() {failing}, result.Failed.ToArray())
        Assert.Equal(Block().Count - 1, store.Count)
        Assert.False(store.ContainsKey(failing))
        ' The live slot destinations the user configured are written, whole and correct.
        If failing <> "MoveOn5" Then Assert.Equal("E:\Slot5", store("MoveOn5"))
    End Sub

    <Fact>
    Public Sub A_retry_rewrites_the_whole_block_and_the_final_store_equals_the_snapshot()
        Dim store As New Dictionary(Of String, String)()
        Dim failFirstTime As Boolean = True
        Dim writer As Action(Of String, String) =
            Sub(name, value)
                If name = "MoveOn9" AndAlso failFirstTime Then Throw New IO.IOException("transient")
                store(name) = value
            End Sub

        Assert.False(SettingsWriter.WriteAll(Block(), writer).Succeeded)
        failFirstTime = False
        Assert.True(SettingsWriter.WriteAll(Block(), writer).Succeeded)

        For Each entry As KeyValuePair(Of String, String) In Block()
            Assert.Equal(entry.Value, store(entry.Key))
        Next
    End Sub

    <Fact>
    Public Sub Every_failure_is_reported_to_the_diagnostic_callback_with_its_name()
        Dim reported As New List(Of String)()
        SettingsWriter.WriteAll(Block(), Sub(name, value) Throw New IO.IOException("x"),
                                Sub(name, ex) reported.Add(name))
        Assert.Equal(Block().Count, reported.Count)
    End Sub

End Class
