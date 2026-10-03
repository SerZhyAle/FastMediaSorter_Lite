Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' 108 H02 (057.S22-4, Q108-2): the one decision behind the QR code, the saved file and the
''' e-mail. Every sequence the ticket names is driven through the tracker; "may it be handed out"
''' is asserted, not "is a button enabled". Pending, timed-out, failed and cancelled are never
''' permission, and there is no override.
''' </summary>
Public Class PreflightTrackerTests

    <Fact>
    Public Sub An_edit_event_hides_the_old_payload_before_the_debounce_runs()
        Dim tracker As New PreflightTracker()
        tracker.Complete(Ready(tracker, KeyA), PreflightOutcome.Allowed)
        Assert.True(tracker.CanOffer())
        tracker.InvalidatePayload()
        Assert.False(tracker.CanOffer())
        tracker.Cancel()
        Assert.Equal(PreflightState.Cancelled, tracker.State)
        Assert.False(tracker.CanOffer())
    End Sub

    <Fact>
    Public Async Function Timed_out_scans_keep_actual_permits_and_expired_pending_calls_never_start() As Task
        Dim scheduler As New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 1, .MaxPending = 8, .ReplacePendingOfSameOwner = True})
        Dim clock As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim runner As New PreflightScanRunner(Of Integer)(scheduler, Function(budget) clock.Task)
        Using entered As New ManualResetEventSlim(), release As New ManualResetEventSlim()
            Dim lateFault As New TaskCompletionSource(Of Exception)(TaskCreationOptions.RunContinuationsAsynchronously)
            Dim active As Task(Of ProbeResult(Of Integer)) = runner.Run(New Object(), "active",
                Function() As Integer
                    entered.Set()
                    If Not release.Wait(10000) Then Throw New TimeoutException("test gate")
                    Throw New IO.IOException("late failure")
                End Function, TimeSpan.FromSeconds(2), Function() True, Sub(ex) lateFault.TrySetResult(ex))
            Try
                Assert.True(entered.Wait(10000))
                Dim queuedOwner As New Object()
                Dim calls As Integer
                Dim latest As Task(Of ProbeResult(Of Integer)) = Nothing
                For i As Integer = 1 To 100
                    latest = runner.Run(queuedOwner, i.ToString(), Function() Interlocked.Increment(calls),
                                        TimeSpan.FromSeconds(2), Function() True, Sub(ex) Return)
                Next
                Assert.Equal(1, scheduler.PendingCount)
                clock.SetResult(True)
                Assert.Equal(ProbeStatus.TimedOut, (Await active.WaitAsync(TimeSpan.FromSeconds(10))).Status)
                Assert.Equal(ProbeStatus.TimedOut, (Await latest.WaitAsync(TimeSpan.FromSeconds(10))).Status)
                Assert.Equal(1, scheduler.ActiveCount)
                Assert.Equal(0, scheduler.PendingCount)
                Assert.Equal(0, calls)
                release.Set()
                Assert.IsType(Of IO.IOException)(Await lateFault.Task.WaitAsync(TimeSpan.FromSeconds(10)))
                Assert.Equal(0, scheduler.ActiveCount)
            Finally
                release.Set()
            End Try
        End Using
    End Function

    <Fact>
    Public Async Function An_older_UI_timeout_does_not_cancel_a_newer_queued_revision() As Task
        Dim scheduler As New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 1, .MaxPending = 8})
        Dim oldClock As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim newClock As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim delays As Integer
        Dim runner As New PreflightScanRunner(Of Integer)(scheduler,
            Function(budget) If(Interlocked.Increment(delays) = 1, oldClock.Task, newClock.Task))
        Dim owner As New Object()
        Using entered As New ManualResetEventSlim(), release As New ManualResetEventSlim()
            Dim first As Task(Of ProbeResult(Of Integer)) = runner.Run(owner, "old", Function()
                entered.Set()
                release.Wait(10000)
                Return 1
            End Function, TimeSpan.FromSeconds(2), Function() True, Sub(ex) Return)
            Try
                Assert.True(entered.Wait(10000))
                Dim latest As Task(Of ProbeResult(Of Integer)) = runner.Run(owner, "new", Function() 2,
                    TimeSpan.FromSeconds(2), Function() True, Sub(ex) Return)
                oldClock.SetResult(True)
                Assert.Equal(ProbeStatus.TimedOut, (Await first.WaitAsync(TimeSpan.FromSeconds(10))).Status)
                Assert.Equal(1, scheduler.PendingCount)
                release.Set()
                Dim result As ProbeResult(Of Integer) = Await latest.WaitAsync(TimeSpan.FromSeconds(10))
                Assert.Equal(ProbeStatus.Completed, result.Status)
                Assert.Equal(2, result.Value)
            Finally
                release.Set()
            End Try
        End Using
    End Function

    Private Const KeyA As String = "mode=User" & vbLf & "D:\a|rw" & vbLf
    Private Const KeyB As String = "mode=User" & vbLf & "D:\b|ro" & vbLf

    Private Shared Function Ready(tracker As PreflightTracker, key As String) As Integer
        tracker.Edit(key)
        tracker.PayloadBuilt()
        Return tracker.Begin()
    End Function

    <Fact>
    Public Sub Nothing_is_offered_before_any_check_or_payload()
        Dim t As New PreflightTracker()
        Assert.False(t.CanOffer())
        t.Edit(KeyA)
        Assert.False(t.CanOffer())
    End Sub

    <Fact>
    Public Sub An_allowed_verdict_for_the_built_revision_offers_the_payload()
        Dim t As New PreflightTracker()
        Dim id As Integer = Ready(t, KeyA)
        Assert.True(id > 0)
        Assert.Equal(PreflightState.Checking, t.State)
        Assert.False(t.CanOffer()) ' pending is not permission

        Assert.True(t.Complete(id, PreflightOutcome.Allowed))
        Assert.True(t.CanOffer())
    End Sub

    <Fact>
    Public Sub A_same_selection_rebuild_while_checking_stays_gated_and_joins_the_scan()
        Dim t As New PreflightTracker()
        Dim id As Integer = Ready(t, KeyA)
        ' A toggle rebuilds the payload with the same roots while the scan is still running.
        Dim joined As Integer = Ready(t, KeyA)
        Assert.Equal(0, joined) ' no second scan
        Assert.Equal(PreflightState.Checking, t.State)
        Assert.False(t.CanOffer()) ' ShowQr() must not re-enable anything

        Assert.True(t.Complete(id, PreflightOutcome.Allowed))
        Assert.True(t.CanOffer()) ' the verdict is for these roots, the payload is for the current revision
    End Sub

    <Fact>
    Public Sub An_edit_voids_the_payload_at_once_even_when_the_verdict_stands()
        Dim t As New PreflightTracker()
        Dim id As Integer = Ready(t, KeyA)
        t.Complete(id, PreflightOutcome.Allowed)
        Assert.True(t.CanOffer())

        t.Edit(KeyA) ' an option changed; the payload is being rebuilt
        Assert.False(t.CanOffer())
        t.PayloadBuilt()
        Assert.True(t.CanOffer()) ' same roots: the verdict is reused for the new revision
        Assert.Equal(0, t.Begin()) ' ...and nothing is re-walked
    End Sub

    <Fact>
    Public Sub A_changed_selection_drops_the_verdict_and_a_late_answer_of_the_old_scan_changes_nothing()
        Dim t As New PreflightTracker()
        Dim first As Integer = Ready(t, KeyA)
        Dim second As Integer = Ready(t, KeyB) ' the user switched folders mid-scan
        Assert.NotEqual(first, second)
        Assert.False(t.Complete(first, PreflightOutcome.Allowed)) ' stale: ignored
        Assert.Equal(PreflightState.Checking, t.State)
        Assert.False(t.CanOffer())

        Assert.True(t.Complete(second, PreflightOutcome.Allowed))
        Assert.True(t.CanOffer())
    End Sub

    <Fact>
    Public Sub A_stale_success_arriving_after_a_newer_failure_cannot_unlock_the_newer_payload()
        Dim t As New PreflightTracker()
        Dim older As Integer = Ready(t, KeyA)
        Dim newer As Integer = Ready(t, KeyB)
        Assert.True(t.Complete(newer, PreflightOutcome.Failed))
        Assert.False(t.Complete(older, PreflightOutcome.Allowed))
        Assert.Equal(PreflightState.Failed, t.State)
        Assert.False(t.CanOffer())
    End Sub

    <Theory>
    <InlineData(1, 3)>
    <InlineData(2, 4)>
    <InlineData(3, 6)>
    Public Sub Blocked_timed_out_and_failed_are_never_permission_and_may_be_retried(outcomeCode As Integer, expectedCode As Integer)
        Dim outcome As PreflightOutcome = CType(outcomeCode, PreflightOutcome)
        Dim expected As PreflightState = CType(expectedCode, PreflightState)
        Dim t As New PreflightTracker()
        Dim id As Integer = Ready(t, KeyA)
        Assert.True(t.Complete(id, outcome))
        Assert.Equal(expected, t.State)
        Assert.False(t.CanOffer())
        Assert.True(t.CanRetry)

        Dim retry As Integer = t.Retry()
        Assert.True(retry > 0)
        Assert.Equal(PreflightState.Checking, t.State)
        Assert.True(t.Complete(retry, PreflightOutcome.Allowed)) ' a repaired folder, a longer budget
        Assert.True(t.CanOffer())
    End Sub

    <Fact>
    Public Sub Cancelling_a_scan_in_flight_ignores_its_late_answer_and_is_not_permission()
        Dim t As New PreflightTracker()
        Dim id As Integer = Ready(t, KeyA)
        t.Cancel()
        Assert.Equal(PreflightState.Cancelled, t.State)
        Assert.False(t.Complete(id, PreflightOutcome.Allowed))
        Assert.False(t.CanOffer())
        Assert.True(t.CanRetry)
    End Sub

    <Fact>
    Public Sub A_definitive_blocked_verdict_is_remembered_for_the_same_roots_until_retry()
        Dim t As New PreflightTracker()
        Dim id As Integer = Ready(t, KeyA)
        t.Complete(id, PreflightOutcome.Blocked)
        Assert.Equal(0, Ready(t, KeyA)) ' same roots, same verdict, no re-walk
        Assert.Equal(PreflightState.Blocked, t.State)
        Assert.False(t.CanOffer())
    End Sub

    <Fact>
    Public Sub A_timeout_is_not_cached_so_the_next_edit_checks_again()
        Dim t As New PreflightTracker()
        t.Complete(Ready(t, KeyA), PreflightOutcome.TimedOut)
        Dim again As Integer = Ready(t, KeyA)
        Assert.True(again > 0)
        Assert.Equal(PreflightState.Checking, t.State)
    End Sub

    <Fact>
    Public Sub A_flush_that_starts_a_fresh_scan_leaves_the_code_unavailable_at_the_click()
        ' Click "Show QR code" within 250 ms of an edit: the flush rebuilds with a changed selection.
        Dim t As New PreflightTracker()
        t.Complete(Ready(t, KeyA), PreflightOutcome.Allowed)
        Assert.True(t.CanOffer()) ' what the button looked like before the edit

        Dim fresh As Integer = Ready(t, KeyB) ' FlushPendingRebuild()
        Assert.True(fresh > 0)
        Assert.False(t.CanOffer()) ' OnShowQr asks again AFTER the flush and is refused
    End Sub

    <Fact>
    Public Sub A_result_after_the_wizard_closed_lands_nowhere()
        Dim t As New PreflightTracker()
        Dim id As Integer = Ready(t, KeyA)
        t.Cancel() ' HandleFormClosed
        Assert.False(t.Complete(id, PreflightOutcome.Allowed))
        Assert.False(t.CanOffer())
    End Sub

    <Theory>
    <InlineData(False, True, 0, False, 0)>
    <InlineData(False, True, 3, False, 0)>
    <InlineData(False, False, 0, False, 2)>
    <InlineData(False, False, 2, False, 2)>
    <InlineData(False, True, 5, True, 1)>
    <InlineData(False, False, 5, True, 1)>
    <InlineData(True, True, 0, False, 3)>
    <InlineData(True, False, 0, True, 3)>
    Public Sub A_scan_is_judged_only_complete_allows_and_a_fault_is_a_failure(faulted As Boolean, completed As Boolean, blocked As Integer, allBlocked As Boolean, expectedCode As Integer)
        Assert.Equal(CType(expectedCode, PreflightOutcome), PreflightLogic.Judge(faulted, completed, blocked, allBlocked))
    End Sub

End Class
