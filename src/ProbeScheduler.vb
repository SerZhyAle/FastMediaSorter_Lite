Option Strict On

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>How one scheduled probe ended. Anything other than <see cref="Completed"/> means
''' the work produced NO answer - callers must never read it as "the file does not exist".</summary>
Friend Enum ProbeStatus
    ''' <summary>The work ran and returned a value.</summary>
    Completed = 0
    ''' <summary>The work threw; the exception is in <see cref="ProbeResult(Of T).Failure"/>.</summary>
    Faulted = 1
    ''' <summary>A newer request of the same owner replaced this one before it started.</summary>
    Superseded = 2
    ''' <summary>The request waited in the queue longer than its budget and never ran.</summary>
    TimedOut = 3
    ''' <summary>The queue was full; nothing was scheduled.</summary>
    Rejected = 4
    ''' <summary>Its owner went away (dialog closed, window closed) before it started.</summary>
    Cancelled = 5
End Enum

Friend NotInheritable Class ProbeResult(Of T)
    Friend ReadOnly Property Status As ProbeStatus
    Friend ReadOnly Property Value As T
    Friend ReadOnly Property Failure As Exception

    Private Sub New(status As ProbeStatus, value As T, failure As Exception)
        Me.Status = status
        Me.Value = value
        Me.Failure = failure
    End Sub

    Friend Shared Function Done(value As T) As ProbeResult(Of T)
        Return New ProbeResult(Of T)(ProbeStatus.Completed, value, Nothing)
    End Function

    Friend Shared Function Fault(ex As Exception) As ProbeResult(Of T)
        Return New ProbeResult(Of T)(ProbeStatus.Faulted, Nothing, ex)
    End Function

    Friend Shared Function Without(status As ProbeStatus) As ProbeResult(Of T)
        Return New ProbeResult(Of T)(status, Nothing, Nothing)
    End Function
End Class

''' <summary>The knobs of one probe family (105 L01). The numbers are the callers' choice.</summary>
Friend NotInheritable Class ProbePolicy
    ''' <summary>Actual filesystem calls allowed at once. A permit is held until the call
    ''' really returns - never released because a waiter gave up.</summary>
    Friend Property MaxActive As Integer = 2
    ''' <summary>Requests that may wait for a permit across all owners.</summary>
    Friend Property MaxPending As Integer = 64
    ''' <summary>A newer request of the same owner replaces the owner's queued one, which
    ''' completes as Superseded (the "latest open wins" family).</summary>
    Friend Property ReplacePendingOfSameOwner As Boolean
    ''' <summary>The same (owner, key) already queued or running is shared, not repeated.</summary>
    Friend Property CoalesceByKey As Boolean
    ''' <summary>A queued request older than this never starts; 0 = no limit.</summary>
    Friend Property PendingExpiryMs As Integer
    ''' <summary>Test seam for the expiry clock; production is Task.Delay.</summary>
    Friend Property Delay As Func(Of Integer, Task) = Function(ms) Task.Delay(ms)
End Class

''' <summary>
''' Bounds the filesystem probes that can block for a full SMB timeout (105 S15-3 / S13-13,
''' 106). The invariant is about ACTUAL calls: <see cref="ActiveCount"/> never exceeds the
''' policy, whatever happens to the people waiting for the answers. Superseded, expired,
''' cancelled and rejected requests never enter the filesystem call at all. Thread-safe.
''' </summary>
Friend NotInheritable Class ProbeScheduler(Of T)

    Private NotInheritable Class Item
        Friend Owner As Object
        Friend Key As String
        Friend Work As Func(Of T)
        Friend IsWanted As Func(Of Boolean)
        Friend Running As Boolean
        Friend ReadOnly Outcome As New TaskCompletionSource(Of ProbeResult(Of T))(TaskCreationOptions.RunContinuationsAsynchronously)
    End Class

    Private ReadOnly policy As ProbePolicy
    Private ReadOnly gate As New Object()
    Private ReadOnly pending As New LinkedList(Of Item)()
    Private ReadOnly running As New List(Of Item)()
    Private activeCalls As Integer
    Private peakActiveCalls As Integer

    Friend Sub New(policy As ProbePolicy)
        Me.policy = If(policy, New ProbePolicy())
        If Me.policy.MaxActive < 1 Then Me.policy.MaxActive = 1
    End Sub

    Friend ReadOnly Property ActiveCount As Integer
        Get
            SyncLock gate
                Return activeCalls
            End SyncLock
        End Get
    End Property

    Friend ReadOnly Property PeakActive As Integer
        Get
            SyncLock gate
                Return peakActiveCalls
            End SyncLock
        End Get
    End Property

    Friend ReadOnly Property PendingCount As Integer
        Get
            SyncLock gate
                Return pending.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>Schedules <paramref name="work"/>. The task completes when it ran, or at
    ''' once with a status that says it never will. <paramref name="isWanted"/> is asked
    ''' right before the call would start.</summary>
    Friend Function Submit(owner As Object, key As String, work As Func(Of T),
                           Optional isWanted As Func(Of Boolean) = Nothing) As Task(Of ProbeResult(Of T))
        If work Is Nothing Then Throw New ArgumentNullException(NameOf(work))
        Dim item As New Item With {.Owner = owner, .Key = If(key, ""), .Work = work, .IsWanted = isWanted}
        Dim superseded As Item = Nothing
        Dim expiryWanted As Boolean = False

        SyncLock gate
            If policy.CoalesceByKey Then
                For Each existing As Item In running
                    If SameRequest(existing, item) Then Return existing.Outcome.Task
                Next
                For Each existing As Item In pending
                    If SameRequest(existing, item) Then Return existing.Outcome.Task
                Next
            End If
            If policy.ReplacePendingOfSameOwner Then
                Dim node As LinkedListNode(Of Item) = pending.First
                While node IsNot Nothing
                    Dim nextNode As LinkedListNode(Of Item) = node.Next
                    If ReferenceEquals(node.Value.Owner, owner) Then
                        superseded = node.Value
                        pending.Remove(node)
                        Exit While
                    End If
                    node = nextNode
                End While
            End If
            If pending.Count >= policy.MaxPending Then
                If superseded IsNot Nothing Then superseded.Outcome.TrySetResult(ProbeResult(Of T).Without(ProbeStatus.Superseded))
                Return Task.FromResult(ProbeResult(Of T).Without(ProbeStatus.Rejected))
            End If
            pending.AddLast(item)
            expiryWanted = policy.PendingExpiryMs > 0
        End SyncLock

        If superseded IsNot Nothing Then superseded.Outcome.TrySetResult(ProbeResult(Of T).Without(ProbeStatus.Superseded))
        If expiryWanted Then ArmExpiry(item)
        Pump()
        Return item.Outcome.Task
    End Function

    ''' <summary>Owner went away: its queued requests never run. Calls already running keep
    ''' their permit until they return; whoever receives their result must check for itself.</summary>
    Friend Function CancelOwner(owner As Object) As Integer
        Dim dropped As New List(Of Item)()
        SyncLock gate
            Dim node As LinkedListNode(Of Item) = pending.First
            While node IsNot Nothing
                Dim nextNode As LinkedListNode(Of Item) = node.Next
                If ReferenceEquals(node.Value.Owner, owner) Then
                    dropped.Add(node.Value)
                    pending.Remove(node)
                End If
                node = nextNode
            End While
        End SyncLock
        For Each item As Item In dropped
            item.Outcome.TrySetResult(ProbeResult(Of T).Without(ProbeStatus.Cancelled))
        Next
        Return dropped.Count
    End Function

    Private Shared Function SameRequest(a As Item, b As Item) As Boolean
        Return ReferenceEquals(a.Owner, b.Owner) AndAlso String.Equals(a.Key, b.Key, StringComparison.Ordinal)
    End Function

    Private Sub ArmExpiry(item As Item)
        Dim timer As Task = policy.Delay(policy.PendingExpiryMs)
        timer.ContinueWith(Sub(t) Expire(item), TaskContinuationOptions.ExecuteSynchronously)
    End Sub

    Private Sub Expire(item As Item)
        Dim removed As Boolean = False
        SyncLock gate
            If Not item.Running Then removed = pending.Remove(item)
        End SyncLock
        If removed Then item.Outcome.TrySetResult(ProbeResult(Of T).Without(ProbeStatus.TimedOut))
    End Sub

    Private Sub Pump()
        Do
            Dim next_Item As Item = Nothing
            ' Check and reserve the permit in ONE critical section: two threads pumping at once
            ' (a submitter and a finishing call) must not both see a free slot.
            SyncLock gate
                If activeCalls >= policy.MaxActive OrElse pending.Count = 0 Then Return
                next_Item = pending.First.Value
                pending.RemoveFirst()
                next_Item.Running = True
                running.Add(next_Item)
                activeCalls += 1
                If activeCalls > peakActiveCalls Then peakActiveCalls = activeCalls
            End SyncLock

            ' Ownership is rechecked before the call, outside the lock (the callback is ours
            ' to run, not the scheduler's to hold a lock around).
            Dim wanted As Boolean = True
            If next_Item.IsWanted IsNot Nothing Then
                Try
                    wanted = next_Item.IsWanted()
                Catch
                    wanted = False
                End Try
            End If
            If Not wanted Then
                SyncLock gate
                    running.Remove(next_Item)
                    activeCalls -= 1
                End SyncLock
                next_Item.Outcome.TrySetResult(ProbeResult(Of T).Without(ProbeStatus.Superseded))
                Continue Do
            End If

            Dim started As Item = next_Item
            Task.Run(Sub() Execute(started))
        Loop
    End Sub
    Private Sub Execute(item As Item)
        Dim result As ProbeResult(Of T)
        Try
            result = ProbeResult(Of T).Done(item.Work())
        Catch ex As Exception
            ' Observed here, once, whoever is still waiting for it.
            result = ProbeResult(Of T).Fault(ex)
        End Try
        ' The permit goes back only now, after the call returned.
        SyncLock gate
            running.Remove(item)
            activeCalls -= 1
        End SyncLock
        item.Outcome.TrySetResult(result)
        Pump()
    End Sub

End Class
