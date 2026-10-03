#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Channels
Imports System.Threading.Tasks

''' <summary>Who may put work on the file-operation queue right now (107 C01).</summary>
Friend Enum QueueAdmission
    ''' <summary>Accepting operations.</summary>
    Open = 0
    ''' <summary>A close is being negotiated: nothing new is admitted, what is accepted keeps running,
    ''' and a veto or a failed drain puts the queue back to Open. Reversible.</summary>
    PausedForClose = 1
    ''' <summary>The window is closing for real. Permanent.</summary>
    Closed = 2
    ''' <summary>The consumer stopped on a fault. Admission stays shut until the owner replaces the
    ''' queue - the old consumer is never silently reused.</summary>
    Faulted = 3
End Enum

''' <summary>The pending operation was not run because the consumer faulted before reaching it.
''' It travels the ordinary failure path, so the list rolls back and the user is told.</summary>
Friend NotInheritable Class OperationNotExecutedException
    Inherits InvalidOperationException

    Friend Sub New()
        MyBase.New("The file operation was not run: the queue stopped before reaching it.")
    End Sub
End Class

''' <summary>
''' One queue of file operations with a single consumer - modern build only.
'''
''' Why it exists, in numbers: the owner's destination slot points at \\p7\down while the
''' folder being sorted is \\p7\_i\output. Those are two different SHARES, so Windows
''' cannot rename across them - every move copies the file over the wire. The folder is
''' 26.5 GB of video, 1-212 MB per file. With the old single BackgroundWorker there were
''' only two outcomes, both bad: with "independent thread" off (the default, and the
''' owner's setting) the whole transfer ran on the UI thread and the window stood still
''' for seconds; with it on, the next hotkey press hit a busy worker and was refused.
'''
''' A queue removes the choice: presses are accepted at any rate, run in order on a
''' worker, and report back on the UI thread. Sorting goes as fast as the hand.
'''
''' Closing (107): admission is a state, not the permanent completion of the channel writer.
''' <see cref="PauseAdmissionForClose"/> stops new work and keeps the consumer alive, so a
''' cancellable FormClosing can wait for the queue to go quiet and, on a veto or a failure,
''' <see cref="ResumeAdmission"/> reopens it. Only <see cref="CompleteAdmission"/> - at the
''' actual FormClosed - is irreversible. "Outstanding" means accepted but not yet FINALIZED: the
''' operation has run AND its completion has been delivered to the UI thread, not merely that the
''' channel is empty.
''' </summary>
Friend NotInheritable Class FileOpQueue(Of T)

    ''' <summary>What one operation did. Raised on the UI thread.</summary>
    Friend NotInheritable Class Completion
        Public Property Op As T
        ''' <summary>Nothing on success.</summary>
        Public Property Error_Info As Exception
    End Class

    Private ReadOnly _channel As Channel(Of T)
    Private ReadOnly _run As Action(Of T)
    Private ReadOnly _ui As SynchronizationContext
    Private ReadOnly _consumer As Task
    Private ReadOnly _gate As New Object()
    Private _pending As Integer = 0
    Private _unfinalized As Integer = 0
    Private _admission As QueueAdmission = QueueAdmission.Open
    Private _epoch As Integer = 0
    Private _fault As Exception
    Private _idle As TaskCompletionSource(Of Boolean)

    ''' <summary>Fired once per operation, on the UI thread, whatever the outcome.</summary>
    Public Event OpCompleted As EventHandler(Of Completion)

    ''' <param name="run">Does the actual work. Called on a worker thread: it must not
    ''' touch the form.</param>
    Friend Sub New(run As Action(Of T))
        _run = run
        ' Captured at construction, on the UI thread - that is how results get marshalled
        ' back without the queue knowing anything about WinForms.
        _ui = SynchronizationContext.Current
        _channel = Channel.CreateUnbounded(Of T)(New UnboundedChannelOptions With {
            .SingleReader = True,
            .SingleWriter = False
        })
        _consumer = Task.Run(AddressOf ConsumeAsync)
    End Sub

    ''' <summary>How many operations are queued or running. Shown to the user: a tail of
    ''' pending transfers must be visible, not a mystery.</summary>
    Friend ReadOnly Property PendingCount As Integer
        Get
            Return Volatile.Read(_pending)
        End Get
    End Property

    ''' <summary>Accepted operations whose completion has not been delivered yet - what a close
    ''' has to wait for. Zero is "quiet"; an empty channel alone is not.</summary>
    Friend ReadOnly Property OutstandingCount As Integer
        Get
            SyncLock _gate
                Return _unfinalized
            End SyncLock
        End Get
    End Property

    Friend ReadOnly Property Admission As QueueAdmission
        Get
            SyncLock _gate
                Return _admission
            End SyncLock
        End Get
    End Property

    ''' <summary>The exception that stopped the consumer, or Nothing.</summary>
    Friend ReadOnly Property Fault As Exception
        Get
            SyncLock _gate
                Return _fault
            End SyncLock
        End Get
    End Property

    ''' <summary>True once the consumer has ended (cleanly or on a fault) - the precondition for
    ''' replacing the queue.</summary>
    Friend ReadOnly Property ConsumerIsTerminal As Boolean
        Get
            Return _consumer.IsCompleted
        End Get
    End Property

    ''' <summary>
    ''' Hands an operation to the queue. Never blocks. Returns False when admission is not Open
    ''' and the operation will NOT be executed - the caller rolls its optimistic list edit back,
    ''' the same rollback a genuine operation failure gets. The state check and the acceptance
    ''' share one lock with PauseAdmissionForClose: an enqueue racing a pause is either accepted
    ''' and counted, or refused visibly - never lost.
    ''' </summary>
    Friend Function Enqueue(op As T) As Boolean
        SyncLock _gate
            If _admission <> QueueAdmission.Open Then Return False
            If Not _channel.Writer.TryWrite(op) Then Return False
            _unfinalized += 1
            Interlocked.Increment(_pending)
            Return True
        End SyncLock
    End Function

    ''' <summary>True while new operations are not admitted (a close in progress, the final close, or
    ''' a faulted consumer). Callers use it to refuse an action up front instead of accepting a
    ''' keypress they cannot honour.</summary>
    Friend ReadOnly Property IsClosed As Boolean
        Get
            Return Admission <> QueueAdmission.Open
        End Get
    End Property

    ''' <summary>Stops admitting work for a close that can still be cancelled. Returns the epoch
    ''' of this pause - the only value <see cref="ResumeAdmission"/> accepts - or 0 when the queue
    ''' is not Open (already paused, closed or faulted: there is nothing to resume).</summary>
    Friend Function PauseAdmissionForClose() As Integer
        SyncLock _gate
            If _admission <> QueueAdmission.Open Then Return 0
            _admission = QueueAdmission.PausedForClose
            _epoch += 1
            Return _epoch
        End SyncLock
    End Function

    ''' <summary>Reopens admission after a veto or a failed drain. Only for the CURRENT pause epoch
    ''' and only with a healthy consumer; a stale token, a closed or faulted queue says no.</summary>
    Friend Function ResumeAdmission(epoch As Integer) As Boolean
        SyncLock _gate
            If _admission <> QueueAdmission.PausedForClose OrElse epoch <> _epoch OrElse epoch = 0 Then Return False
            If _fault IsNot Nothing OrElse _consumer.IsCompleted Then Return False
            _admission = QueueAdmission.Open
            Return True
        End SyncLock
    End Function

    ''' <summary>The window is really closing. Permanent: nothing is admitted again, and the
    ''' consumer finishes what it has accepted and ends.</summary>
    Friend Sub CompleteAdmission()
        SyncLock _gate
            If _admission <> QueueAdmission.Faulted Then _admission = QueueAdmission.Closed
        End SyncLock
        _channel.Writer.TryComplete()
    End Sub

    ''' <summary>Waits until every accepted operation has been finalized, or the timeout. Does not
    ''' complete the writer and does not change admission - it is safe to call from a close that
    ''' may still be cancelled, as often as needed. True = quiet. A consumer fault ends the wait
    ''' with that fault, never with "quiet".</summary>
    Friend Async Function WaitForQuiescenceAsync(timeout As TimeSpan) As Task(Of Boolean)
        Dim idle As Task(Of Boolean)
        SyncLock _gate
            If _fault IsNot Nothing Then Throw New InvalidOperationException("The file-operation queue stopped on a fault.", _fault)
            If _unfinalized = 0 Then Return True
            If _idle Is Nothing OrElse _idle.Task.IsCompleted Then
                _idle = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            End If
            idle = _idle.Task
        End SyncLock
        Dim finished As Task = Await Task.WhenAny(idle, Task.Delay(timeout)).ConfigureAwait(True)
        If Not ReferenceEquals(finished, idle) Then Return False
        SyncLock _gate
            If _fault IsNot Nothing Then Throw New InvalidOperationException("The file-operation queue stopped on a fault.", _fault)
        End SyncLock
        Return Await idle.ConfigureAwait(True)
    End Function

    Private Async Function ConsumeAsync() As Task
        Try
            While Await _channel.Reader.WaitToReadAsync().ConfigureAwait(False)
                Dim op As T = Nothing
                While _channel.Reader.TryRead(op)
                    Dim failure As Exception = Nothing
                    Try
                        _run(op)
                    Catch ex As Exception
                        ' The operation's own failure is data, not a crash: it travels to
                        ' the UI thread, which rolls the list back.
                        failure = ex
                    End Try

                    Interlocked.Decrement(_pending)
                    ReportCompletion(op, failure)
                End While
            End While
        Catch ex As Exception
            AppFileLogger.LogException("FileOpQueue consumer stopped", ex)
            EnterFault(ex)
        End Try
    End Function

    ''' <summary>The consumer died. Admission shuts, every operation that never started is reported
    ''' as NOT executed (so each rolls back through the ordinary failure path - none is replayed, none
    ''' is silently kept), and the waiters are released with the fault.</summary>
    Private Sub EnterFault(ex As Exception)
        SyncLock _gate
            _fault = ex
            _admission = QueueAdmission.Faulted
        End SyncLock
        Dim unstarted As T = Nothing
        While _channel.Reader.TryRead(unstarted)
            Interlocked.Decrement(_pending)
            Try
                ReportCompletion(unstarted, New OperationNotExecutedException())
            Catch inner As Exception
                ' ReportCompletion has already finalized it (its Finally); nothing to count twice.
                AppFileLogger.LogException("FileOpQueue: reporting an unstarted operation", inner)
            End Try
        End While
        _channel.Writer.TryComplete()
        Dim waiting As TaskCompletionSource(Of Boolean)
        SyncLock _gate
            waiting = _idle
        End SyncLock
        If waiting IsNot Nothing Then waiting.TrySetResult(False)
    End Sub

    Private Sub ReportCompletion(op As T, failure As Exception)
        Dim done As New Completion With {.Op = op, .Error_Info = failure}
        If _ui IsNot Nothing Then
            Try
                _ui.Post(Sub()
                             Try
                                 RaiseEvent OpCompleted(Me, done)
                             Finally
                                 ' Finalized: the result has been delivered, whatever the handler did.
                                 Finalize_One()
                             End Try
                         End Sub, Nothing)
            Catch
                ' The UI thread cannot take the completion (a dead context). The operation is no
                ' longer outstanding - nothing will ever deliver it - and the consumer fault follows.
                Finalize_One()
                Throw
            End Try
        Else
            Try
                RaiseEvent OpCompleted(Me, done)
            Finally
                Finalize_One()
            End Try
        End If
    End Sub

    Private Sub Finalize_One()
        Dim signal As TaskCompletionSource(Of Boolean) = Nothing
        SyncLock _gate
            If _unfinalized > 0 Then _unfinalized -= 1
            If _unfinalized = 0 Then signal = _idle
        End SyncLock
        If signal IsNot Nothing Then signal.TrySetResult(True)
    End Sub

End Class
#End If
