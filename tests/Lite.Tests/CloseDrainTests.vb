Option Strict On

Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Guards close-drain re-entrancy and state separation (ticket 107,
''' 107_SPECIFICATION_CLOSE_DRAIN_REENTRANCY.md).
'''
''' Proves:
''' - Deterministic state machine transitions (idle -> draining -> approved, and reset on failure).
''' - Re-entrant close attempts while draining are cancelled and never bypass the drain.
''' - Windows shutdown/logoff query path remains non-destructive.
''' - Drain timeout confirmation (Yes waits again, No explicitly discards).
''' - Injected drain failure resets state so subsequent close does not bypass.
''' - Queue refuses new work once closed for drain.
''' </summary>
Public Class CloseDrainTests

    ' ---------------------------------------------------------------- deterministic state tests ----

    <Fact>
    Public Sub Initial_state_is_not_draining_and_not_approved()
        Dim coordinator As New CloseDrainCoordinator()
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)
    End Sub

    <Fact>
    Public Sub Windows_shutdown_query_is_non_destructive_and_leaves_state_intact()
        Dim coordinator As New CloseDrainCoordinator()

        ' Without pending operations
        Dim decision1 As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=True, hasPendingOperations:=False)
        Assert.Equal(CloseDecision.WindowsShutDownQuery, decision1)
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)

        ' With pending operations
        Dim decision2 As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=True, hasPendingOperations:=True)
        Assert.Equal(CloseDecision.WindowsShutDownQuery, decision2)
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)
    End Sub

    <Fact>
    Public Sub No_pending_operations_allows_immediate_close()
        Dim coordinator As New CloseDrainCoordinator()
        Dim decision As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=False)
        Assert.Equal(CloseDecision.ProceedWithClose, decision)
    End Sub

    <Fact>
    Public Sub Pending_operations_when_idle_initiates_drain()
        Dim coordinator As New CloseDrainCoordinator()
        Dim decision As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=True)
        Assert.Equal(CloseDecision.StartDrain, decision)
    End Sub

    <Fact>
    Public Sub BeginDrain_transitions_to_draining_state()
        Dim coordinator As New CloseDrainCoordinator()
        Assert.True(coordinator.BeginDrain())
        Assert.True(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)

        ' Cannot start another drain while already draining
        Assert.False(coordinator.BeginDrain())
    End Sub

    <Fact>
    Public Sub Repeated_close_while_draining_is_cancelled_as_reentrant()
        Dim coordinator As New CloseDrainCoordinator()
        Assert.True(coordinator.BeginDrain())

        ' Another Alt+F4 or close click while drain is pending
        Dim decision1 As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=True)
        Assert.Equal(CloseDecision.CancelReentrant, decision1)

        ' Even if pending ops reached 0 before ApproveClose is called, re-entrant close is cancelled
        Dim decision2 As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=False)
        Assert.Equal(CloseDecision.CancelReentrant, decision2)
    End Sub

    <Fact>
    Public Sub ApproveClose_transitions_from_draining_to_approved()
        Dim coordinator As New CloseDrainCoordinator()
        coordinator.BeginDrain()
        coordinator.ApproveClose()

        Assert.False(coordinator.IsDraining)
        Assert.True(coordinator.IsApprovedToClose)

        ' Next close attempt proceeds to close
        Dim decision As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=False)
        Assert.Equal(CloseDecision.ProceedWithClose, decision)

        ' Cannot begin drain once approved
        Assert.False(coordinator.BeginDrain())
    End Sub

    <Fact>
    Public Sub Reset_clears_draining_and_approved_states()
        Dim coordinator As New CloseDrainCoordinator()

        ' Reset from draining
        coordinator.BeginDrain()
        coordinator.Reset()
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)
        Assert.Equal(CloseDecision.StartDrain, coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=True))

        ' Reset from approved
        coordinator.BeginDrain()
        coordinator.ApproveClose()
        Assert.True(coordinator.IsApprovedToClose)
        coordinator.Reset()
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)
    End Sub

    <Fact>
    Public Async Function Wait_loop_keeps_reentrant_closes_cancelled_until_completion() As Task
        Dim coordinator As New CloseDrainCoordinator()
        coordinator.BeginDrain()
        Dim waits As Integer = 0
        Dim prompts As Integer = 0
        Dim approved As Boolean = Await coordinator.WaitForApprovalAsync(
            Function()
                waits += 1
                Assert.Equal(CloseDecision.CancelReentrant, coordinator.EvaluateClosing(False, True))
                Return Task.FromResult(waits = 2)
            End Function,
            Function()
                prompts += 1
                Assert.False(coordinator.IsApprovedToClose)
                Assert.Equal(CloseDecision.CancelReentrant, coordinator.EvaluateClosing(False, False))
                Return True
            End Function,
            Function() True)
        Assert.True(approved)
        Assert.Equal(2, waits)
        Assert.Equal(1, prompts)
        Assert.True(coordinator.IsApprovedToClose)
    End Function

    <Fact>
    Public Async Function Explicit_discard_approves_close_after_timeout() As Task
        Dim coordinator As New CloseDrainCoordinator()
        coordinator.BeginDrain()
        Assert.True(Await coordinator.WaitForApprovalAsync(Function() Task.FromResult(False),
                                                          Function() False, Function() True))
        Assert.Equal(CloseDecision.ProceedWithClose, coordinator.EvaluateClosing(False, True))
    End Function

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function Wait_or_prompt_failure_resets_state(promptFails As Boolean) As Task
        Dim coordinator As New CloseDrainCoordinator()
        coordinator.BeginDrain()
        Dim failure As New InvalidOperationException("Injected drain failure")
        Dim wait As Func(Of Task(Of Boolean)) =
            Function()
                If Not promptFails Then Throw failure
                Return Task.FromResult(False)
            End Function
        Dim actual As Exception = Await Assert.ThrowsAsync(Of InvalidOperationException)(
            Async Function()
                Await coordinator.WaitForApprovalAsync(wait, Function() ThrowPromptFailure(failure), Function() True)
            End Function)
        Assert.Same(failure, actual)
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)
        Assert.Equal(CloseDecision.StartDrain, coordinator.EvaluateClosing(False, True))
    End Function

    Private Shared Function ThrowPromptFailure(failure As Exception) As Boolean
        Throw failure
    End Function

    <Fact>
    Public Async Function Abandoned_wait_does_not_approve_close() As Task
        Dim coordinator As New CloseDrainCoordinator()
        coordinator.BeginDrain()
        Dim alive As Boolean = True
        Assert.False(Await coordinator.WaitForApprovalAsync(
            Function()
                alive = False
                Return Task.FromResult(True)
            End Function,
            Function() ThrowPromptFailure(New InvalidOperationException("Must not prompt")),
            Function() alive))
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)
    End Function

    <Fact>
    Public Sub Cancelled_shutdown_during_drain_preserves_reentrant_protection()
        Dim coordinator As New CloseDrainCoordinator()
        coordinator.BeginDrain()
        Assert.Equal(CloseDecision.WindowsShutDownQuery, coordinator.EvaluateClosing(True, True))
        Assert.True(coordinator.IsDraining)
        Assert.Equal(CloseDecision.CancelReentrant, coordinator.EvaluateClosing(False, True))
    End Sub

#If Not NETFRAMEWORK Then
    ' ---------------------------------------------------------------- async queue integration tests ----

    <Fact>
    Public Async Function Repeated_close_during_a_delayed_queue_does_not_bypass_drain() As Task
        Dim slowOpStarted As New ManualResetEventSlim(False)
        Dim allowSlowOpFinish As New ManualResetEventSlim(False)
        Dim executedCount As Integer = 0

        Dim queue As New FileOpQueue(Of String)(Sub(op)
                                                    slowOpStarted.Set()
                                                    allowSlowOpFinish.Wait(TimeSpan.FromSeconds(10))
                                                    Interlocked.Increment(executedCount)
                                                End Sub)

        Dim coordinator As New CloseDrainCoordinator()

        ' 1. Enqueue work
        Assert.True(queue.Enqueue("slow_op_1"))
        Assert.Equal(1, queue.PendingCount)

        ' 2. First close arrives
        Assert.Equal(CloseDecision.StartDrain, coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=queue.PendingCount > 0))
        Assert.True(coordinator.BeginDrain())

        ' Start async drain
        Dim epoch As Integer = queue.PauseAdmissionForClose()
        Assert.NotEqual(0, epoch)
        Dim drainTask As Task(Of Boolean) = coordinator.WaitForApprovalAsync(
            Function() queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)),
            Function() ThrowPromptFailure(New InvalidOperationException("Unexpected timeout")),
            Function() True)
        Assert.True(queue.IsClosed)
        Assert.False(queue.Enqueue("refused_while_draining"))
        Assert.Equal(1, queue.PendingCount)

        ' Wait until the worker starts executing the operation
        Assert.True(slowOpStarted.Wait(TimeSpan.FromSeconds(5)))

        ' 3. Second close arrives while drain is in flight (Alt+F4 pressed again)
        Dim reentrantDecision As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=queue.PendingCount > 0)
        Assert.Equal(CloseDecision.CancelReentrant, reentrantDecision)
        Assert.True(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)

        ' 4. Third close arrives while drain is still in flight
        Dim reentrantDecision2 As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=queue.PendingCount > 0)
        Assert.Equal(CloseDecision.CancelReentrant, reentrantDecision2)

        ' 5. Allow operation to finish
        allowSlowOpFinish.Set()
        Dim drained As Boolean = Await drainTask
        Assert.True(drained)
        Assert.Equal(0, queue.PendingCount)
        Assert.Equal(1, executedCount)

        ' 6. Drain completed -> close is approved
        Assert.False(coordinator.IsDraining)
        Assert.True(coordinator.IsApprovedToClose)

        ' 7. Final close event (from Me.Close()) proceeds
        Dim finalDecision As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=queue.PendingCount > 0)
        Assert.Equal(CloseDecision.ProceedWithClose, finalDecision)
    End Function

    <Fact>
    Public Async Function Injected_drain_failure_resets_state_so_future_close_is_not_bypassed() As Task
        ' Fail the consumer's completion dispatch, rather than an individual operation
        ' (operation failures are reported as data and must not stop the consumer).
        Dim queue As FileOpQueue(Of String)
        Dim allowCompletion As New ManualResetEventSlim(False)
        Dim originalContext As SynchronizationContext = SynchronizationContext.Current
        Try
            SynchronizationContext.SetSynchronizationContext(New FailingCompletionContext())
            queue = New FileOpQueue(Of String)(Sub(op)
                                                  allowCompletion.Wait(TimeSpan.FromSeconds(5))
                                              End Sub)
        Finally
            SynchronizationContext.SetSynchronizationContext(originalContext)
        End Try
        Dim coordinator As New CloseDrainCoordinator()
        Assert.True(queue.Enqueue("op"))
        Assert.True(queue.Enqueue("still_pending"))
        coordinator.BeginDrain()
        queue.PauseAdmissionForClose()
        allowCompletion.Set()
        Await Assert.ThrowsAsync(Of InvalidOperationException)(
            Async Function()
                Await coordinator.WaitForApprovalAsync(
                    Function() queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)),
                    Function() ThrowPromptFailure(New Exception("Must not prompt")), Function() True)
            End Function)
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)
        ' The unstarted operation was reported as not executed, not left pending forever.
        Assert.Equal(QueueAdmission.Faulted, queue.Admission)
        Assert.Equal(0, queue.PendingCount)
        Assert.Equal(CloseDecision.ProceedWithClose, coordinator.EvaluateClosing(False, queue.OutstandingCount > 0))
    End Function

    Private NotInheritable Class FailingCompletionContext
        Inherits SynchronizationContext

        Public Overrides Sub Post(callback As SendOrPostCallback, state As Object)
            Throw New InvalidOperationException("Injected completion dispatch failure")
        End Sub
    End Class

    <Fact>
    Public Async Function Timeout_followed_by_yes_continues_waiting_and_approves_when_drained() As Task
        Dim allowFinish As New ManualResetEventSlim(False)
        Dim queue As New FileOpQueue(Of String)(Sub(op)
                                                    allowFinish.Wait(TimeSpan.FromSeconds(10))
                                                End Sub)

        Dim coordinator As New CloseDrainCoordinator()
        queue.Enqueue("op1")
        coordinator.BeginDrain()
        queue.PauseAdmissionForClose()

        ' Drain with short timeout to simulate timeout
        Dim prompts As Integer = 0
        Dim waits As Integer = 0
        Try
            Assert.True(Await coordinator.WaitForApprovalAsync(
                Function()
                    waits += 1
                    Return queue.WaitForQuiescenceAsync(If(waits = 1, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(5)))
                End Function,
                Function()
                    prompts += 1
                    Assert.True(coordinator.IsDraining)
                    allowFinish.Set()
                    Return True
                End Function, Function() True))
        Finally
            allowFinish.Set()
        End Try
        Assert.Equal(1, prompts)
        Assert.True(coordinator.IsApprovedToClose)
        Assert.Equal(CloseDecision.ProceedWithClose, coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=queue.OutstandingCount > 0))
    End Function

    <Fact>
    Public Async Function Timeout_followed_by_no_approves_close_with_explicit_discard() As Task
        Dim allowFinish As New ManualResetEventSlim(False)
        Dim queue As New FileOpQueue(Of String)(Sub(op)
                                                    allowFinish.Wait(TimeSpan.FromSeconds(10))
                                                End Sub)

        Dim coordinator As New CloseDrainCoordinator()
        queue.Enqueue("op1")
        coordinator.BeginDrain()
        queue.PauseAdmissionForClose()

        ' Drain times out
        Try
            Assert.True(Await coordinator.WaitForApprovalAsync(
                Function() queue.WaitForQuiescenceAsync(TimeSpan.FromMilliseconds(50)),
                Function() False, Function() True))
            Assert.Equal(1, queue.OutstandingCount) ' discard intent does not turn unfinished work into finished work
            Assert.Equal(CloseDecision.ProceedWithClose, coordinator.EvaluateClosing(False, True))
        Finally
            allowFinish.Set()
        End Try

        Assert.True(coordinator.IsApprovedToClose)
        Assert.False(coordinator.IsDraining)

        ' Close is now allowed even though queue still has unfinished items (explicit user discard)
        Assert.Equal(CloseDecision.ProceedWithClose, coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=queue.PendingCount > 0))

        ' Cleanup worker
        allowFinish.Set()
    End Function

    <Fact>
    Public Sub Windows_shutdown_cancellation_preserves_running_state_and_allows_subsequent_drain()
        Dim coordinator As New CloseDrainCoordinator()

        ' 1. Windows shutdown query arrives (WM_QUERYENDSESSION)
        Dim shutdownDecision As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=True, hasPendingOperations:=True)
        Assert.Equal(CloseDecision.WindowsShutDownQuery, shutdownDecision)

        ' State must remain completely clean
        Assert.False(coordinator.IsDraining)
        Assert.False(coordinator.IsApprovedToClose)

        ' 2. Shutdown is cancelled by another application or user. Form continues running.
        ' 3. Later, user clicks close with pending operations
        Dim userCloseDecision As CloseDecision = coordinator.EvaluateClosing(isWindowsShutdown:=False, hasPendingOperations:=True)
        Assert.Equal(CloseDecision.StartDrain, userCloseDecision)
    End Sub
#End If

End Class
