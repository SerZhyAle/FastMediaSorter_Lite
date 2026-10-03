Option Strict On

Imports System.Threading.Tasks

''' <summary>
''' Coordinates the close-time file-operation drain sequence (ticket 107).
'''
''' Separates "a drain is actively waiting on the queue" from "the close has been
''' approved to proceed". A second Alt+F4 or close click while draining is re-entrant:
''' it must be cancelled without starting another drain or bypassing the active one.
''' Final close is permitted only once the queue has drained or the user has explicitly
''' chosen to discard remaining operations. If the drain faults, the coordinator resets
''' so subsequent close attempts still protect the queue.
''' </summary>
Public NotInheritable Class CloseDrainCoordinator

    Private _isDraining As Boolean = False
    Private _isApprovedToClose As Boolean = False

    ''' <summary>True while an asynchronous drain wait/prompt loop is actively running.</summary>
    Public ReadOnly Property IsDraining As Boolean
        Get
            Return _isDraining
        End Get
    End Property

    ''' <summary>True once the queue has drained or the user explicitly chose to close without waiting.</summary>
    Public ReadOnly Property IsApprovedToClose As Boolean
        Get
            Return _isApprovedToClose
        End Get
    End Property

    ''' <summary>
    ''' Decides what action FormClosing should take.
    ''' </summary>
    ''' <param name="isWindowsShutdown">True if CloseReason is WindowsShutDown (query/logoff path).</param>
    ''' <param name="hasPendingOperations">True if the file-operation queue has pending/running operations.</param>
    Public Function EvaluateClosing(isWindowsShutdown As Boolean, hasPendingOperations As Boolean) As CloseDecision
        If isWindowsShutdown Then
            Return CloseDecision.WindowsShutDownQuery
        End If

        If _isApprovedToClose Then
            Return CloseDecision.ProceedWithClose
        End If

        If _isDraining Then
            Return CloseDecision.CancelReentrant
        End If

        If hasPendingOperations Then
            Return CloseDecision.StartDrain
        End If

        Return CloseDecision.ProceedWithClose
    End Function

    ''' <summary>
    ''' Marks the coordinator as draining. Returns True if draining was successfully started,
    ''' or False if already draining or already approved to close.
    ''' </summary>
    Public Function BeginDrain() As Boolean
        If _isDraining OrElse _isApprovedToClose Then
            Return False
        End If
        _isDraining = True
        Return True
    End Function

    ''' <summary>
    ''' Grants final permission to close the form (called after queue drain completion or explicit user discard).
    ''' </summary>
    Public Sub ApproveClose()
        _isDraining = False
        _isApprovedToClose = True
    End Sub

    ''' <summary>
    ''' Resets state (e.g. when a drain operation fails or when closing is aborted).
    ''' </summary>
    Public Sub Reset()
        _isDraining = False
        _isApprovedToClose = False
    End Sub

    ''' <summary>Runs the wait/prompt loop. Only completion or explicit discard approves
    ''' closing; an abandoned wait or exception leaves the next close protected.</summary>
    Public Async Function WaitForApprovalAsync(waitForDrain As Func(Of Task(Of Boolean)),
                                               confirmWait As Func(Of Boolean),
                                               canContinue As Func(Of Boolean)) As Task(Of Boolean)
        If Not _isDraining Then Throw New InvalidOperationException("No close drain is active.")
        Try
            While canContinue()
                Dim drained As Boolean = Await waitForDrain()
                If Not canContinue() Then Return False
                Dim discard As Boolean = Not drained AndAlso Not confirmWait()
                If Not canContinue() Then Return False
                If drained OrElse discard Then
                    ApproveClose()
                    Return True
                End If
            End While
            Return False
        Finally
            If Not _isApprovedToClose Then Reset()
        End Try
    End Function

End Class

Public Enum CloseDecision
    ''' <summary>Proceed with normal close and persist settings without cancelling the event.</summary>
    ProceedWithClose
    ''' <summary>Windows shutdown/logoff question: persist settings, leave teardown to FormClosed, do not cancel or drain.</summary>
    WindowsShutDownQuery
    ''' <summary>Re-entrant close while drain is already in progress: cancel the close event and do nothing.</summary>
    CancelReentrant
    ''' <summary>First close with pending operations: cancel the close event and start async drain.</summary>
    StartDrain
End Enum
