#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading.Tasks

Friend Enum CloseFlowResult
    ''' <summary>Approved and the window really closed.</summary>
    Closed = 0
    ''' <summary>Approved, but another FormClosing subscriber vetoed: the window survives and the
    ''' queue is back in service.</summary>
    Vetoed = 1
    ''' <summary>The wait was abandoned (the window went away or the user's choice ended it): the
    ''' close is off and the queue is back in service.</summary>
    Abandoned = 2
    ''' <summary>Waiting or prompting failed (a consumer fault included): the close is off, the queue is
    ''' back in service - replaced if its consumer died.</summary>
    Failed = 3
End Enum

''' <summary>
''' The close transaction of 107 (C02), with the window behind delegates so that the whole sequence -
''' pause, wait, prompt, close, veto, recover - is driven by tests rather than argued about. The
''' coordinator owns "draining" versus "approved"; the queue owns admission; this ties them: whatever
''' happens, a close that does not complete leaves a viewer that can accept its next operation.
''' </summary>
Friend NotInheritable Class FileOpCloseFlow(Of T)

    Private ReadOnly coordinator As CloseDrainCoordinator
    Private ReadOnly getQueue As Func(Of FileOpQueue(Of T))
    Private ReadOnly replaceQueue As Action
    Private ReadOnly confirmWait As Func(Of Integer, Boolean)
    Private ReadOnly canContinue As Func(Of Boolean)
    Private ReadOnly requestClose As Func(Of Boolean)
    Private ReadOnly log As Action(Of String, Exception)
    Private ReadOnly waitSlice As TimeSpan

    ''' <param name="requestClose">Asks the window to close; returns True when the window survived
    ''' (a veto), False when it is gone.</param>
    Friend Sub New(coordinator As CloseDrainCoordinator, getQueue As Func(Of FileOpQueue(Of T)), replaceQueue As Action,
                   confirmWait As Func(Of Integer, Boolean), canContinue As Func(Of Boolean),
                   requestClose As Func(Of Boolean), log As Action(Of String, Exception),
                   Optional waitSlice As TimeSpan = Nothing)
        Me.coordinator = coordinator
        Me.getQueue = getQueue
        Me.replaceQueue = replaceQueue
        Me.confirmWait = confirmWait
        Me.canContinue = canContinue
        Me.requestClose = requestClose
        Me.log = log
        Me.waitSlice = If(waitSlice = TimeSpan.Zero, TimeSpan.FromSeconds(15), waitSlice)
    End Sub

    Friend Async Function RunAsync(admissionEpoch As Integer) As Task(Of CloseFlowResult)
        Dim approved As Boolean = False
        Dim failed As Boolean = False
        Try
            approved = Await coordinator.WaitForApprovalAsync(
                Function()
                    Dim queue As FileOpQueue(Of T) = getQueue()
                    Return If(queue Is Nothing, Task.FromResult(True), queue.WaitForQuiescenceAsync(waitSlice))
                End Function,
                Function()
                    Dim queue As FileOpQueue(Of T) = getQueue()
                    Return confirmWait(If(queue Is Nothing, 0, queue.OutstandingCount))
                End Function,
                canContinue)
        Catch ex As Exception
            failed = True
            log("Closing: waiting for the file operations", ex)
        End Try

        If approved AndAlso canContinue() Then
            If Not requestClose() Then Return CloseFlowResult.Closed
            ' Another FormClosing subscriber vetoed the approved close.
            coordinator.Reset()
            Restore(admissionEpoch)
            Return CloseFlowResult.Vetoed
        End If

        If canContinue() Then Restore(admissionEpoch)
        Return If(failed, CloseFlowResult.Failed, CloseFlowResult.Abandoned)
    End Function

    ''' <summary>A healthy paused queue reopens for exactly this pause; one whose consumer died is
    ''' replaced once that consumer has ended - never reused.</summary>
    Private Sub Restore(admissionEpoch As Integer)
        Dim queue As FileOpQueue(Of T) = getQueue()
        If queue Is Nothing Then Return
        If queue.ResumeAdmission(admissionEpoch) Then Return
        If queue.Admission = QueueAdmission.Faulted AndAlso queue.ConsumerIsTerminal Then
            log("FileOpQueue: replacing the queue after its consumer stopped", Nothing)
            replaceQueue()
        End If
    End Sub

End Class
#End If
