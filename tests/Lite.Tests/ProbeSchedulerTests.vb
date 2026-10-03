Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 105 L01 (057.S15-3 / S13-13): the number of ACTUAL filesystem calls is bounded, and a
''' request that is superseded, expired, cancelled or rejected never enters one. The works
''' here block on gates the test opens, so "stuck on a dead share" is a state, not a sleep.
''' </summary>
Public Class ProbeSchedulerTests

    Private Const Watchdog_Ms As Integer = 15000

    Private NotInheritable Class Gate
        Friend ReadOnly Entered As New SemaphoreSlim(0)
        Friend ReadOnly Release As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Friend Calls As Integer

        ''' <summary>The "filesystem call": announces it started, then blocks until released.</summary>
        Friend Function Block(Optional result As Integer = 1) As Func(Of Integer)
            Return Function()
                       Interlocked.Increment(Calls)
                       Entered.Release()
                       If Not Release.Task.Wait(Watchdog_Ms) Then Throw New TimeoutException("test gate never opened")
                       Return result
                   End Function
        End Function

        Friend Sub WaitEntered(count As Integer)
            For i As Integer = 1 To count
                Assert.True(Entered.Wait(Watchdog_Ms), "a call that should have started did not")
            Next
        End Sub
    End Class

    Private Shared Function ArgumentFamily(Optional delay As Func(Of Integer, Task) = Nothing) As ProbeScheduler(Of Integer)
        Dim policy As New ProbePolicy With {.MaxActive = 2, .MaxPending = 8, .ReplacePendingOfSameOwner = True, .PendingExpiryMs = 30000}
        If delay IsNot Nothing Then policy.Delay = delay
        Return New ProbeScheduler(Of Integer)(policy)
    End Function

    Private Shared Function HistoryFamily() As ProbeScheduler(Of Integer)
        Return New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 2, .MaxPending = 64, .CoalesceByKey = True})
    End Function

    <Fact>
    Public Sub A_hundred_requests_never_run_more_than_two_calls_at_once_and_the_permits_come_back()
        Dim scheduler As ProbeScheduler(Of Integer) = HistoryFamily()
        Dim gate As New Gate()
        Dim owner As New Object()
        Dim results As New List(Of Task(Of ProbeResult(Of Integer)))()

        For i As Integer = 0 To 99
            results.Add(scheduler.Submit(owner, "k" & i.ToString(), gate.Block(i)))
        Next

        gate.WaitEntered(2)
        Assert.Equal(2, scheduler.ActiveCount)
        Assert.True(scheduler.PendingCount <= 64)
        Assert.Equal(2, gate.Calls)

        gate.Release.SetResult(True)
        Dim started As List(Of Task(Of ProbeResult(Of Integer))) = results.Where(Function(t) t.IsCompleted AndAlso t.Result.Status <> ProbeStatus.Rejected).ToList()
        Task.WaitAll(results.Where(Function(t) Not (t.IsCompleted AndAlso t.Result.Status = ProbeStatus.Rejected)).ToArray(), Watchdog_Ms)

        Assert.True(scheduler.PeakActive <= 2)
        SpinWait.SpinUntil(Function() scheduler.ActiveCount = 0, Watchdog_Ms)
        Assert.Equal(0, scheduler.ActiveCount)
        ' 2 running + 64 queued are accepted; the rest were refused without a call.
        Assert.Equal(66, results.Where(Function(t) t.Result.Status = ProbeStatus.Completed).Count())
        Assert.Equal(34, results.Where(Function(t) t.Result.Status = ProbeStatus.Rejected).Count())
        Assert.Equal(66, gate.Calls)
        GC.KeepAlive(started)
    End Sub

    <Fact>
    Public Sub A_waiter_giving_up_does_not_release_the_permit_of_a_call_still_in_flight()
        Dim scheduler As ProbeScheduler(Of Integer) = HistoryFamily()
        Dim gate As New Gate()
        Dim owner As New Object()

        ' Two calls stuck on a dead share. Their callers' UI waits "time out" (the callers simply
        ' stop looking) - the permits must stay taken.
        scheduler.Submit(owner, "a", gate.Block())
        scheduler.Submit(owner, "b", gate.Block())
        gate.WaitEntered(2)

        Dim third As New Gate()
        Dim thirdTask As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(owner, "c", third.Block())
        Assert.Equal(2, scheduler.ActiveCount)
        Assert.Equal(1, scheduler.PendingCount)
        Assert.Equal(0, third.Calls)
        Assert.False(thirdTask.IsCompleted)

        gate.Release.SetResult(True)
        third.Release.SetResult(True)
        Assert.True(thirdTask.Wait(Watchdog_Ms))
        Assert.Equal(ProbeStatus.Completed, thirdTask.Result.Status)
    End Sub

    <Fact>
    Public Sub A_newer_request_of_the_same_owner_supersedes_the_queued_one_and_the_old_one_never_runs()
        Dim scheduler As ProbeScheduler(Of Integer) = ArgumentFamily()
        Dim busy As New Gate()
        Dim other As New Object()
        Dim viewer As New Object()
        scheduler.Submit(other, "x", busy.Block())
        scheduler.Submit(other, "y", busy.Block())
        busy.WaitEntered(2)

        Dim oldGate As New Gate()
        Dim newGate As New Gate()
        Dim oldTask As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(viewer, "A", oldGate.Block(1))
        Dim newTask As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(viewer, "B", newGate.Block(2))

        Assert.True(oldTask.Wait(Watchdog_Ms))
        Assert.Equal(ProbeStatus.Superseded, oldTask.Result.Status)
        Assert.Equal(1, scheduler.PendingCount)

        busy.Release.SetResult(True)
        newGate.Release.SetResult(True)
        Assert.True(newTask.Wait(Watchdog_Ms))
        Assert.Equal(2, newTask.Result.Value)
        Assert.Equal(0, oldGate.Calls)
    End Sub

    <Fact>
    Public Sub A_queued_request_past_its_budget_times_out_without_ever_running_and_a_running_one_is_not_expired()
        Dim clock As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim scheduler As ProbeScheduler(Of Integer) = ArgumentFamily(Function(ms) clock.Task)
        Dim busy As New Gate()
        Dim other As New Object()
        scheduler.Submit(other, "x", busy.Block())
        scheduler.Submit(other, "y", busy.Block())
        busy.WaitEntered(2)

        Dim queuedGate As New Gate()
        Dim queued As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(New Object(), "late", queuedGate.Block())
        clock.SetResult(True) ' the 30 s budget elapses

        Assert.True(queued.Wait(Watchdog_Ms))
        Assert.Equal(ProbeStatus.TimedOut, queued.Result.Status)
        busy.Release.SetResult(True)
        SpinWait.SpinUntil(Function() scheduler.ActiveCount = 0, Watchdog_Ms)
        Assert.Equal(0, queuedGate.Calls)
        Assert.Equal(0, scheduler.PendingCount)
    End Sub

    <Fact>
    Public Sub A_full_queue_rejects_at_once_and_requests_no_call()
        Dim scheduler As New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 1, .MaxPending = 2})
        Dim gate As New Gate()
        Dim owner As New Object()
        scheduler.Submit(owner, "run", gate.Block())
        gate.WaitEntered(1)
        scheduler.Submit(owner, "q1", Function() 1)
        scheduler.Submit(owner, "q2", Function() 2)

        Dim calls As Integer
        Dim rejected As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(owner, "q3", Function()
                                                                                              Interlocked.Increment(calls)
                                                                                              Return 3
                                                                                          End Function)
        Assert.True(rejected.IsCompleted)
        Assert.Equal(ProbeStatus.Rejected, rejected.Result.Status)
        Assert.Equal(0, calls)
        gate.Release.SetResult(True)
    End Sub

    <Fact>
    Public Sub The_same_key_of_the_same_owner_is_shared_not_repeated()
        Dim scheduler As ProbeScheduler(Of Integer) = HistoryFamily()
        Dim gate As New Gate()
        Dim owner As New Object()
        Dim first As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(owner, "D|share", gate.Block(7))
        gate.WaitEntered(1)
        Dim again As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(owner, "D|share", gate.Block(8))
        Dim otherDialog As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(New Object(), "D|share", gate.Block(9))

        Assert.Same(first, again)
        Assert.NotSame(first, otherDialog)
        gate.Release.SetResult(True)
        Assert.True(first.Wait(Watchdog_Ms))
        Assert.Equal(7, first.Result.Value)
    End Sub

    <Fact>
    Public Sub Cancelling_an_owner_drops_its_queue_but_leaves_running_calls_their_permits()
        Dim scheduler As New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 1, .MaxPending = 8})
        Dim gate As New Gate()
        Dim dialog As New Object()
        Dim running As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(dialog, "run", gate.Block())
        gate.WaitEntered(1)
        Dim queuedGate As New Gate()
        Dim queued As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(dialog, "queued", queuedGate.Block())

        Assert.Equal(1, scheduler.CancelOwner(dialog))
        Assert.Equal(ProbeStatus.Cancelled, queued.Result.Status)
        Assert.Equal(1, scheduler.ActiveCount) ' the blocked call still owns its permit

        gate.Release.SetResult(True)
        Assert.True(running.Wait(Watchdog_Ms))
        Assert.Equal(0, queuedGate.Calls)
    End Sub

    <Fact>
    Public Sub A_faulting_call_is_observed_and_gives_its_permit_back()
        Dim scheduler As New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 1, .MaxPending = 8})
        Dim owner As New Object()
        Dim faulted As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(owner, "bad", Function() As Integer
                                                                                            Throw New InvalidOperationException("share vanished")
                                                                                        End Function)
        Assert.True(faulted.Wait(Watchdog_Ms))
        Assert.Equal(ProbeStatus.Faulted, faulted.Result.Status)
        Assert.IsType(Of InvalidOperationException)(faulted.Result.Failure)

        Dim next_Task As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(owner, "good", Function() 5)
        Assert.True(next_Task.Wait(Watchdog_Ms))
        Assert.Equal(5, next_Task.Result.Value)
    End Sub

    <Fact>
    Public Sub Ownership_is_rechecked_on_the_worker_after_pool_admission()
        Dim scheduler As New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 1})
        Dim checks As Integer
        Dim calls As Integer
        Dim answer As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(New Object(), "stale",
            Function()
                Interlocked.Increment(calls)
                Return 1
            End Function,
            Function() Interlocked.Increment(checks) = 1)
        Assert.True(answer.Wait(Watchdog_Ms))
        Assert.Equal(ProbeStatus.Superseded, answer.Result.Status)
        Assert.Equal(0, calls)
        Assert.Equal(0, scheduler.ActiveCount)
    End Sub

    <Fact>
    Public Sub A_request_nobody_wants_any_more_is_dropped_right_before_the_call()
        Dim scheduler As New ProbeScheduler(Of Integer)(New ProbePolicy With {.MaxActive = 1, .MaxPending = 8})
        Dim gate As New Gate()
        Dim owner As New Object()
        scheduler.Submit(owner, "run", gate.Block())
        gate.WaitEntered(1)

        Dim wanted As Boolean = True
        Dim calls As Integer
        Dim stale As Task(Of ProbeResult(Of Integer)) = scheduler.Submit(owner, "stale", Function()
                                                                                              Interlocked.Increment(calls)
                                                                                              Return 1
                                                                                          End Function,
                                                                          Function() wanted)
        wanted = False ' the ticket went obsolete while it waited
        gate.Release.SetResult(True)

        Assert.True(stale.Wait(Watchdog_Ms))
        Assert.Equal(ProbeStatus.Superseded, stale.Result.Status)
        Assert.Equal(0, calls)
    End Sub

End Class
