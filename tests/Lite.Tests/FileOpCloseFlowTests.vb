#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 107 C02/C03: the whole close transaction - pause, wait, prompt, close, veto, recover - against the
''' real coordinator and the real queue, with the window reduced to delegates. The invariant under
''' test is the one the ticket is about: whatever happens to a close that does not complete, the
''' surviving viewer can accept its next operation, and nothing already reported is replayed.
''' </summary>
Public Class FileOpCloseFlowTests

    Private Const Watchdog_Ms As Integer = 15000

    Private NotInheritable Class InlineContext
        Inherits SynchronizationContext

        Friend ThrowOnPost As Func(Of Integer, Boolean) = Function(n) False
        Private posts As Integer

        Public Overrides Sub Post(callback As SendOrPostCallback, state As Object)
            Dim n As Integer = Interlocked.Increment(posts)
            If ThrowOnPost(n) Then Throw New InvalidOperationException("UI gone")
            callback(state)
        End Sub
    End Class

    Private NotInheritable Class Rig
        Friend ReadOnly Coordinator As New CloseDrainCoordinator()
        Friend ReadOnly Gate As New ManualResetEventSlim(False)
        Friend ReadOnly Executed As New ConcurrentQueue(Of String)()
        Friend ReadOnly Log As New List(Of String)()
        Friend Queue As FileOpQueue(Of String)
        Friend Replaced As Integer
        Friend Prompts As Integer
        Friend Answer As Boolean = True
        Friend PromptThrows As Boolean
        Friend WindowAlive As Boolean = True
        Friend VetoClose As Boolean
        Friend Context As New InlineContext()

        Friend Sub New(Optional blockFirstOp As Boolean = True)
            Queue = Build(blockFirstOp)
        End Sub

        Friend Function Build(blockFirstOp As Boolean) As FileOpQueue(Of String)
            Dim original As SynchronizationContext = SynchronizationContext.Current
            Try
                SynchronizationContext.SetSynchronizationContext(Context)
                Return New FileOpQueue(Of String)(Sub(op)
                                                      If blockFirstOp AndAlso op = "slow" Then Gate.Wait(Watchdog_Ms)
                                                      Executed.Enqueue(op)
                                                  End Sub)
            Finally
                SynchronizationContext.SetSynchronizationContext(original)
            End Try
        End Function

        Friend Function Flow(Optional slice As TimeSpan = Nothing) As FileOpCloseFlow(Of String)
            Return New FileOpCloseFlow(Of String)(
                Coordinator, Function() Queue,
                Sub()
                    Replaced += 1
                    Queue = Build(False)
                End Sub,
                Function(outstanding As Integer)
                    Prompts += 1
                    If PromptThrows Then Throw New InvalidOperationException("prompt failed")
                    Return Answer
                End Function,
                Function() WindowAlive,
                Function()
                    If VetoClose Then Return True
                    WindowAlive = False
                    Return False
                End Function,
                Sub(message, ex) Log.Add(message),
                slice)
        End Function

        ''' <summary>The first close arrives with work pending: coordinator draining, queue paused.</summary>
        Friend Function BeginClose() As Integer
            Assert.Equal(CloseDecision.StartDrain, Coordinator.EvaluateClosing(False, Queue.OutstandingCount > 0))
            Assert.True(Coordinator.BeginDrain())
            Return Queue.PauseAdmissionForClose()
        End Function
    End Class

    <Fact>
    Public Async Function Work_finishing_in_time_approves_and_the_close_goes_through() As Task
        Dim rig As New Rig()
        Assert.True(rig.Queue.Enqueue("slow"))
        Dim epoch As Integer = rig.BeginClose()

        Dim run As Task(Of CloseFlowResult) = rig.Flow().RunAsync(epoch)
        ' A repeated Alt+F4 during the wait is cancelled and starts nothing.
        Assert.Equal(CloseDecision.CancelReentrant, rig.Coordinator.EvaluateClosing(False, True))
        rig.Gate.Set()

        Assert.Equal(CloseFlowResult.Closed, Await run)
        Assert.Equal(0, rig.Prompts)
        Assert.Equal(New String() {"slow"}, rig.Executed.ToArray())
        Assert.True(rig.Queue.IsClosed) ' the window is gone; nothing reopens the queue
    End Function

    <Fact>
    Public Async Function A_veto_of_the_approved_close_reopens_the_queue_and_the_next_copy_runs() As Task
        Dim rig As New Rig(blockFirstOp:=False)
        Assert.True(rig.Queue.Enqueue("moved-before"))
        Dim epoch As Integer = rig.BeginClose()
        rig.VetoClose = True

        Assert.Equal(CloseFlowResult.Vetoed, Await rig.Flow().RunAsync(epoch))
        Assert.False(rig.Coordinator.IsApprovedToClose) ' a veto must not leave an approval behind
        Assert.False(rig.Coordinator.IsDraining)
        Assert.Equal(QueueAdmission.Open, rig.Queue.Admission)

        Assert.True(rig.Queue.Enqueue("copy-after-veto"))
        Assert.True(Await rig.Queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)))
        Assert.Equal(New String() {"moved-before", "copy-after-veto"}, rig.Executed.ToArray())
        ' ...and the next close with pending work is a fresh first close, not a bypass.
        Assert.Equal(CloseDecision.StartDrain, rig.Coordinator.EvaluateClosing(False, True))
    End Function

    <Fact>
    Public Async Function Timeout_then_yes_waits_again_and_timeout_then_no_closes_with_unfinished_work_still_unfinished() As Task
        Dim yes As New Rig()
        yes.Queue.Enqueue("slow")
        Dim yesEpoch As Integer = yes.BeginClose()
        Dim yesRun As Task(Of CloseFlowResult) = yes.Flow(TimeSpan.FromMilliseconds(60)).RunAsync(yesEpoch)
        SpinWait.SpinUntil(Function() yes.Prompts >= 1, Watchdog_Ms)
        yes.Gate.Set() ' the user said "wait" and the work finishes
        Assert.Equal(CloseFlowResult.Closed, Await yesRun)
        Assert.True(yes.Prompts >= 1)
        Assert.Equal(New String() {"slow"}, yes.Executed.ToArray())

        Dim no As New Rig()
        no.Answer = False
        no.Queue.Enqueue("slow")
        Dim noEpoch As Integer = no.BeginClose()
        Assert.Equal(CloseFlowResult.Closed, Await no.Flow(TimeSpan.FromMilliseconds(60)).RunAsync(noEpoch))
        Assert.Equal(1, no.Prompts)
        ' "No" is an explicit discard, not a success: the unfinished operation is still outstanding.
        Assert.Equal(1, no.Queue.OutstandingCount)
        no.Gate.Set()
    End Function

    <Fact>
    Public Async Function A_failing_prompt_cancels_the_close_and_puts_the_queue_back_in_service() As Task
        Dim rig As New Rig()
        rig.PromptThrows = True
        rig.Queue.Enqueue("slow")
        Dim epoch As Integer = rig.BeginClose()

        Dim result As CloseFlowResult = Await rig.Flow(TimeSpan.FromMilliseconds(40)).RunAsync(epoch)
        Assert.Equal(CloseFlowResult.Failed, result)
        Assert.False(rig.Coordinator.IsDraining)
        Assert.False(rig.Coordinator.IsApprovedToClose)
        Assert.Equal(QueueAdmission.Open, rig.Queue.Admission)
        Assert.True(rig.Queue.Enqueue("after-failure")) ' admission is back
        rig.Gate.Set()
        Assert.True(Await rig.Queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)))
    End Function

    <Fact>
    Public Async Function A_dead_consumer_during_the_wait_fails_the_close_and_the_queue_is_replaced_without_replay() As Task
        Dim rig As New Rig()
        ' The completion of the very first operation cannot be delivered: the consumer dies there.
        rig.Context.ThrowOnPost = Function(n) n = 1
        rig.Queue = rig.Build(blockFirstOp:=True)
        rig.Queue.Enqueue("slow")
        rig.Queue.Enqueue("never-started")
        Dim epoch As Integer = rig.BeginClose()
        Dim run As Task(Of CloseFlowResult) = rig.Flow().RunAsync(epoch)
        rig.Gate.Set()

        Assert.Equal(CloseFlowResult.Failed, Await run)
        Assert.False(rig.Coordinator.IsDraining) ' the coordinator reset: the next close is protected again
        Assert.Equal(1, rig.Replaced)
        Assert.Equal(QueueAdmission.Open, rig.Queue.Admission) ' the replacement is open for business

        Assert.True(rig.Queue.Enqueue("four"))
        Assert.True(Await rig.Queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)))
        ' "slow" ran once, "never-started" never ran, "four" ran once on the new queue.
        Assert.Equal(New String() {"slow", "four"}, rig.Executed.ToArray())
    End Function

    <Fact>
    Public Async Function A_window_that_disappears_during_the_wait_abandons_without_touching_the_queue() As Task
        Dim rig As New Rig()
        rig.Queue.Enqueue("slow")
        Dim epoch As Integer = rig.BeginClose()
        Dim run As Task(Of CloseFlowResult) = rig.Flow().RunAsync(epoch)
        rig.WindowAlive = False
        rig.Gate.Set()
        Assert.NotEqual(CloseFlowResult.Vetoed, Await run)
        Assert.Equal(0, rig.Replaced)
    End Function

    <Fact>
    Public Async Function Batch_in_flight_is_drained_and_cancelled_cleanly_on_close_and_recovers_for_subsequent_batch_after_veto() As Task
        Dim originalContext As SynchronizationContext = SynchronizationContext.Current
        Dim context As New InlineContext()
        Try
            SynchronizationContext.SetSynchronizationContext(context)

            Dim f02Started As New ManualResetEventSlim(False)
            Dim gate As New ManualResetEventSlim(False)
            Dim executedOps As New List(Of String)()
            Dim queue As FileOpQueue(Of BatchOp) = Nothing
            queue = New FileOpQueue(Of BatchOp)(
                Sub(op)
                    If op.Source = "D:\in\f02.jpg" Then
                        f02Started.Set()
                        gate.Wait(Watchdog_Ms)
                    End If
                    SyncLock executedOps
                        executedOps.Add(op.Source)
                    End SyncLock
                End Sub)

            Dim runner As New BatchRunner(Function(op) queue.Enqueue(op))
            AddHandler queue.OpCompleted, Sub(sender, done) runner.OpCompleted(done.Op, done.Error_Info)

            Dim ops1 As New List(Of BatchOp)()
            For i As Integer = 1 To 5
                ops1.Add(New BatchOp With {.Kind = FileOpKind.Move, .Source = "D:\in\f" & i.ToString("00") & ".jpg", .Destination = "E:\out\f" & i.ToString("00") & ".jpg", .SizeBytes = 100})
            Next

            Assert.True(runner.Start(BatchRunKind.Move, ops1, Nothing, 0))

            ' Wait until f02 is in flight and blocked on gate
            Assert.True(f02Started.Wait(Watchdog_Ms))
            Assert.True(runner.IsRunning)
            Assert.Equal(1, queue.OutstandingCount)

            ' Close event arrives:
            Dim coordinator As New CloseDrainCoordinator()
            Assert.Equal(CloseDecision.StartDrain, coordinator.EvaluateClosing(False, queue.OutstandingCount > 0 OrElse runner.IsRunning))
            Assert.True(coordinator.BeginDrain())
            runner.Cancel()
            Dim epoch As Integer = queue.PauseAdmissionForClose()
            Assert.NotEqual(0, epoch)

            ' Run close flow with veto:
            Dim flow As New FileOpCloseFlow(Of BatchOp)(
                coordinator, Function() queue, Sub() Return,
                Function(outstanding) True, Function() True,
                Function() True, ' Vetoed by another handler
                Sub(msg, ex) Return)

            Dim flowTask As Task(Of CloseFlowResult) = flow.RunAsync(epoch)

            ' Allow f02 to finish
            gate.Set()

            Dim flowResult As CloseFlowResult = Await flowTask
            Assert.Equal(CloseFlowResult.Vetoed, flowResult)
            Assert.False(runner.IsRunning)
            Assert.Equal(QueueAdmission.Open, queue.Admission)
            Assert.Equal(New String() {"D:\in\f01.jpg", "D:\in\f02.jpg"}, executedOps.ToArray())

            ' Now run a subsequent batch on the recovered queue:
            Dim ops2 As New List(Of BatchOp)()
            For i As Integer = 10 To 12
                ops2.Add(New BatchOp With {.Kind = FileOpKind.Copy, .Source = "D:\in\f" & i.ToString("00") & ".jpg", .Destination = "E:\out\f" & i.ToString("00") & ".jpg", .SizeBytes = 200})
            Next

            Dim secondFinishedReport As BatchReport = Nothing
            AddHandler runner.Finished, Sub(s, e) secondFinishedReport = e.Report

            Assert.True(runner.Start(BatchRunKind.Copy, ops2, Nothing, 0))
            Assert.True(Await queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)))

            Assert.NotNull(secondFinishedReport)
            Assert.Equal(3, secondFinishedReport.DoneCount)
            Assert.False(secondFinishedReport.Cancelled)
            Assert.Equal(5, executedOps.Count)
            Assert.Equal("D:\in\f12.jpg", executedOps.Last())
        Finally
            SynchronizationContext.SetSynchronizationContext(originalContext)
        End Try
    End Function

    <Fact>
    Public Async Function Operation_leases_and_resources_are_preserved_during_drain_and_survive_veto() As Task
        Dim rig As New Rig(blockFirstOp:=True)
        Dim leaseActive As Boolean = True

        Assert.True(rig.Queue.Enqueue("slow"))
        Dim epoch As Integer = rig.BeginClose()
        rig.VetoClose = True

        ' While draining, lease must remain active
        Dim run As Task(Of CloseFlowResult) = rig.Flow().RunAsync(epoch)
        Assert.True(leaseActive)

        ' Release slow op and wait for veto outcome
        rig.Gate.Set()
        Assert.Equal(CloseFlowResult.Vetoed, Await run)

        ' After veto, the lease was NOT disposed and subsequent work runs with the active lease
        Assert.True(leaseActive)
        Assert.True(rig.Queue.Enqueue("with-active-lease"))
        Assert.True(Await rig.Queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)))
        Assert.Equal(New String() {"slow", "with-active-lease"}, rig.Executed.ToArray())
    End Function

End Class
#End If
