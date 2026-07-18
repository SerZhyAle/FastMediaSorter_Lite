#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading
Imports System.Threading.Channels
Imports System.Threading.Tasks

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
    Private _pending As Integer = 0

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
            .SingleWriter = True
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

    ''' <summary>Hands an operation to the queue. Never blocks, never refuses.</summary>
    Friend Sub Enqueue(op As T)
        Interlocked.Increment(_pending)
        If Not _channel.Writer.TryWrite(op) Then
            ' Unbounded and not completed - the only way here is after Shutdown.
            Interlocked.Decrement(_pending)
        End If
    End Sub

    Private Async Function ConsumeAsync() As Task
        Try
            While Await _channel.Reader.WaitToReadAsync()
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
        End Try
    End Function

    Private Sub ReportCompletion(op As T, failure As Exception)
        Dim done As New Completion With {.Op = op, .Error_Info = failure}
        If _ui IsNot Nothing Then
            _ui.Post(Sub() RaiseEvent OpCompleted(Me, done), Nothing)
        Else
            RaiseEvent OpCompleted(Me, done)
        End If
    End Sub

    ''' <summary>Waits for the queue to run dry - for shutdown. Returns False on timeout,
    ''' and the caller closes anyway rather than hold the window hostage.</summary>
    Friend Async Function DrainAsync(timeout As TimeSpan) As Task(Of Boolean)
        _channel.Writer.TryComplete()
        Dim finished As Task = Await Task.WhenAny(_consumer, Task.Delay(timeout))
        Return finished Is _consumer
    End Function

End Class
#End If
