#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 107 C01/C03: the queue's admission is a state that can be paused and resumed, not the permanent
''' completion of its channel. These tests drive the REAL queue with a fake UI context and gated
''' operations: an enqueue racing a pause is accepted-and-counted or visibly refused, a veto
''' reopens the queue and a new operation runs, "outstanding" means delivered not merely run, and a
''' dead consumer is replaced without replaying anything it already reported.
''' </summary>
Public Class FileOpQueueAdmissionTests

    Private Const Watchdog_Ms As Integer = 15000

    ''' <summary>A UI context that queues what is posted until the test pumps it.</summary>
    Private NotInheritable Class PumpedContext
        Inherits SynchronizationContext

        Friend ReadOnly Posted As New ConcurrentQueue(Of Tuple(Of SendOrPostCallback, Object))()
        Friend PostCount As Integer
        Friend ThrowOnPost As Func(Of Integer, Boolean) = Function(n) False
        Friend Inline As Boolean

        Public Overrides Sub Post(callback As SendOrPostCallback, state As Object)
            Dim n As Integer = Interlocked.Increment(PostCount)
            If ThrowOnPost(n) Then Throw New InvalidOperationException("UI thread gone")
            If Inline Then
                callback(state)
            Else
                Posted.Enqueue(Tuple.Create(callback, state))
            End If
        End Sub

        Friend Sub Pump()
            Dim item As Tuple(Of SendOrPostCallback, Object) = Nothing
            While Posted.TryDequeue(item)
                item.Item1.Invoke(item.Item2)
            End While
        End Sub
    End Class

    Private Shared Function NewQueue(Of T)(run As Action(Of T), context As SynchronizationContext) As FileOpQueue(Of T)
        Dim original As SynchronizationContext = SynchronizationContext.Current
        Try
            SynchronizationContext.SetSynchronizationContext(context)
            Return New FileOpQueue(Of T)(run)
        Finally
            SynchronizationContext.SetSynchronizationContext(original)
        End Try
    End Function

    <Fact>
    Public Sub Outstanding_means_delivered_not_merely_run()
        Dim ctx As New PumpedContext()
        Dim ran As New ManualResetEventSlim(False)
        Dim queue As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) ran.Set(), ctx)

        Assert.True(queue.Enqueue("a"))
        Assert.True(ran.Wait(Watchdog_Ms))
        SpinWait.SpinUntil(Function() queue.PendingCount = 0, Watchdog_Ms)

        Assert.Equal(1, queue.OutstandingCount) ' ran, but its result has not reached the UI yet
        Assert.False(queue.WaitForQuiescenceAsync(TimeSpan.FromMilliseconds(40)).Result)

        Dim wait As Task(Of Boolean) = queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(10))
        ctx.Pump()
        Assert.True(wait.Wait(Watchdog_Ms))
        Assert.True(wait.Result)
        Assert.Equal(0, queue.OutstandingCount)
    End Sub

    <Fact>
    Public Sub A_paused_queue_refuses_new_work_visibly_but_finishes_what_it_accepted()
        Dim ctx As New PumpedContext() With {.Inline = True}
        Dim gate As New ManualResetEventSlim(False)
        Dim executed As Integer
        Dim queue As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op)
                                                                      gate.Wait(Watchdog_Ms)
                                                                      Interlocked.Increment(executed)
                                                                  End Sub, ctx)
        Assert.True(queue.Enqueue("1"))
        Assert.True(queue.Enqueue("2"))
        Assert.True(queue.Enqueue("3"))

        Dim epoch As Integer = queue.PauseAdmissionForClose()
        Assert.NotEqual(0, epoch)
        Assert.Equal(QueueAdmission.PausedForClose, queue.Admission)
        Assert.True(queue.IsClosed)
        Assert.False(queue.Enqueue("refused")) ' never silently lost

        Dim wait As Task(Of Boolean) = queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(10))
        gate.Set()
        Assert.True(wait.Wait(Watchdog_Ms))
        Assert.True(wait.Result)
        Assert.Equal(3, executed)
    End Sub

    <Fact>
    Public Sub A_veto_reopens_the_queue_and_a_new_operation_executes()
        Dim ctx As New PumpedContext() With {.Inline = True}
        Dim executed As New ConcurrentQueue(Of String)()
        Dim queue As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) executed.Enqueue(op), ctx)

        Dim epoch As Integer = queue.PauseAdmissionForClose()
        Assert.True(queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)).Result)
        Assert.True(queue.ResumeAdmission(epoch)) ' the approved close was vetoed
        Assert.Equal(QueueAdmission.Open, queue.Admission)

        Assert.True(queue.Enqueue("copy-after-veto"))
        Assert.True(queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)).Result)
        Assert.Equal(New String() {"copy-after-veto"}, executed.ToArray())
    End Sub

    <Fact>
    Public Sub Only_the_current_pause_epoch_can_resume()
        Dim queue As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) Return, New PumpedContext() With {.Inline = True})
        Dim first As Integer = queue.PauseAdmissionForClose()
        Assert.True(queue.ResumeAdmission(first))
        Dim second As Integer = queue.PauseAdmissionForClose()
        Assert.NotEqual(first, second)
        Assert.False(queue.ResumeAdmission(first)) ' a stale token
        Assert.False(queue.ResumeAdmission(0))
        Assert.Equal(0, queue.PauseAdmissionForClose()) ' already paused: nothing new to resume
        Assert.True(queue.ResumeAdmission(second))
    End Sub

    <Fact>
    Public Sub Completing_admission_at_the_actual_close_is_permanent_and_the_consumer_ends()
        Dim queue As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) Return, New PumpedContext() With {.Inline = True})
        Dim epoch As Integer = queue.PauseAdmissionForClose()
        queue.CompleteAdmission()
        Assert.Equal(QueueAdmission.Closed, queue.Admission)
        Assert.False(queue.Enqueue("late"))
        Assert.False(queue.ResumeAdmission(epoch))
        SpinWait.SpinUntil(Function() queue.ConsumerIsTerminal, Watchdog_Ms)
        Assert.True(queue.ConsumerIsTerminal)
    End Sub

    <Fact>
    Public Sub A_completion_handler_cannot_enqueue_after_the_pause()
        Dim ctx As New PumpedContext() With {.Inline = True}
        Dim queue As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) Return, ctx)
        Dim refused As Boolean
        AddHandler queue.OpCompleted, Sub(sender, done) refused = Not queue.Enqueue("spawned-by-callback")

        Dim gateOpen As New ManualResetEventSlim(False)
        Dim slow As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) gateOpen.Wait(Watchdog_Ms), ctx)
        AddHandler slow.OpCompleted, Sub(sender, done) refused = Not slow.Enqueue("spawned-by-callback")
        Assert.True(slow.Enqueue("running"))
        slow.PauseAdmissionForClose()
        gateOpen.Set()
        Assert.True(slow.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)).Result)
        Assert.True(refused)
        Assert.Equal(0, slow.OutstandingCount)
        GC.KeepAlive(queue)
    End Sub

    <Fact>
    Public Sub An_enqueue_racing_a_pause_is_accepted_and_counted_or_refused_never_lost()
        For round As Integer = 1 To 25
            Dim executed As Integer
            Dim queue As FileOpQueue(Of Integer) = NewQueue(Of Integer)(Sub(op) Interlocked.Increment(executed), New PumpedContext() With {.Inline = True})
            Dim accepted As Integer
            Dim start As New ManualResetEventSlim(False)
            Dim producers As New List(Of Task)()
            For t As Integer = 0 To 3
                producers.Add(Task.Run(Sub()
                                           start.Wait()
                                           For i As Integer = 0 To 199
                                               If queue.Enqueue(i) Then Interlocked.Increment(accepted)
                                           Next
                                       End Sub))
            Next
            start.Set()
            queue.PauseAdmissionForClose()
            Task.WaitAll(producers.ToArray(), Watchdog_Ms)

            Assert.True(queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(10)).Result)
            Assert.Equal(accepted, executed) ' every accepted operation ran exactly once; none was swallowed
            Assert.Equal(0, queue.OutstandingCount)
        Next
    End Sub

    ' ------------------------------------------------------------------- consumer fault ----

    Private NotInheritable Class Observation
        Friend ReadOnly Ran As New ConcurrentQueue(Of String)()
        Friend ReadOnly Delivered As New ConcurrentQueue(Of String)()
        Friend ReadOnly Failures As New ConcurrentQueue(Of Exception)()
    End Class

    <Fact>
    Public Sub A_dead_consumer_reports_unstarted_work_as_not_executed_and_is_replaced_without_replay()
        Dim seen As New Observation()
        Dim context As New PumpedContext() With {.Inline = True, .ThrowOnPost = Function(n) n = 2}
        Dim first As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) seen.Ran.Enqueue(op), context)
        AddHandler first.OpCompleted, Sub(sender, done)
                                          seen.Delivered.Enqueue(done.Op)
                                          If done.Error_Info IsNot Nothing Then seen.Failures.Enqueue(done.Error_Info)
                                      End Sub

        ' Operation one completes, two's completion cannot be delivered (the consumer dies there),
        ' three never starts.
        Dim gate As New ManualResetEventSlim(False)
        Dim slowFirst As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op)
                                                                          gate.Wait(Watchdog_Ms)
                                                                          seen.Ran.Enqueue(op)
                                                                      End Sub, context)
        AddHandler slowFirst.OpCompleted, Sub(sender, done)
                                              seen.Delivered.Enqueue(done.Op)
                                              If done.Error_Info IsNot Nothing Then seen.Failures.Enqueue(done.Error_Info)
                                          End Sub
        Assert.True(slowFirst.Enqueue("one"))
        Assert.True(slowFirst.Enqueue("two"))
        Assert.True(slowFirst.Enqueue("three"))
        gate.Set()

        SpinWait.SpinUntil(Function() slowFirst.ConsumerIsTerminal, Watchdog_Ms)
        Assert.True(slowFirst.ConsumerIsTerminal)
        Assert.Equal(QueueAdmission.Faulted, slowFirst.Admission)
        Assert.NotNull(slowFirst.Fault)
        Assert.False(slowFirst.Enqueue("late")) ' admission stays shut: the old consumer is never reused
        Assert.False(slowFirst.ResumeAdmission(1))

        ' one and two ran, three did not; one was delivered and three came back as NOT executed.
        Assert.Equal(New String() {"one", "two"}, seen.Ran.ToArray())
        Assert.Contains("one", seen.Delivered)
        Assert.Contains("three", seen.Delivered)
        Assert.Single(seen.Failures)
        Assert.IsType(Of OperationNotExecutedException)(seen.Failures.First())
        Assert.Equal(0, slowFirst.OutstandingCount)

        ' The replacement accepts a fourth operation exactly once and replays nothing.
        Dim replacement As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) seen.Ran.Enqueue(op), New PumpedContext() With {.Inline = True})
        Assert.True(replacement.Enqueue("four"))
        Assert.True(replacement.WaitForQuiescenceAsync(TimeSpan.FromSeconds(5)).Result)
        Assert.Equal(New String() {"one", "two", "four"}, seen.Ran.ToArray())
        GC.KeepAlive(first)
    End Sub

    <Fact>
    Public Sub A_waiter_on_a_dead_queue_gets_the_fault_never_a_quiet_verdict()
        Dim context As New PumpedContext() With {.Inline = True, .ThrowOnPost = Function(n) True}
        Dim queue As FileOpQueue(Of String) = NewQueue(Of String)(Sub(op) Return, context)
        queue.Enqueue("x")
        SpinWait.SpinUntil(Function() queue.ConsumerIsTerminal, Watchdog_Ms)
        Dim ex As AggregateException = Assert.Throws(Of AggregateException)(Sub() queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(1)).Wait(Watchdog_Ms))
        Assert.IsType(Of InvalidOperationException)(ex.InnerException)
    End Sub

End Class
#End If
