Option Strict On

Imports System.Collections.Generic
Imports System.Threading.Tasks

''' <summary>What <see cref="InFlightDecodeRegistry(Of T).Acquire"/> did with a request.</summary>
Public Enum DecodeAdmission
    ''' <summary>A new decode was started; the caller owns it.</summary>
    Started
    ''' <summary>An ABANDONED decode of the same path was still running; the caller has taken
    ''' it over and owns its result - nothing new was started.</summary>
    Joined
    ''' <summary>The group already has as many abandoned decodes running as the cap allows;
    ''' nothing was started.</summary>
    Refused
    ''' <summary>A decode of the same path is running and someone still owns it. Unreachable
    ''' from the one UI thread that asks (its wait does not pump messages); answered rather
    ''' than duplicated, so two owners can never dispose one result.</summary>
    Busy
End Enum

''' <summary>
''' The decodes of the display path that are still running, keyed by file path (audit 057,
''' S01-6).
'''
''' A decode that outruns its deadline cannot be cancelled - nothing cancels a blocking read
''' on a dead SMB session, nor ImageSharp half-way through a 300 MP HEIC - so it is abandoned
''' and its result released whenever it lands. The file stays in the list (Transport /
''' SlowDecode), so each return to it used to start ANOTHER full decode beside the first:
''' three visits, three ~1.2 GB decodes; on a dead share one more pool thread parked in a
''' read per visit, until pool starvation pushed a healthy file's decode into its own
''' deadline. Here a second request for the path JOINS the running decode, and the number
''' of abandoned decodes per group (a volume root) is capped.
'''
''' OWNERSHIP - the reason this is not a plain dictionary of tasks. The result (an Image and
''' its MemoryStream) is disposable and must have exactly one owner:
'''   * the caller that Started or Joined an entry owns it until it either takes the result
'''     (the task completed while it waited) or calls <see cref="Lease.Abandon"/>;
'''   * an abandoned entry has no owner: if it completes that way, the registry releases the
'''     result itself; if a new request for the same path arrives first, that request takes
'''     the entry over (Joined) and the registry will NOT release it.
''' So a joined decode never hands one Image to two callers - it is handed over, not shared,
''' and the hand-over and the completion are decided under one lock. The only caller today
''' is the UI thread, which waits synchronously, so at most one owner exists at any time.
'''
''' Pure: no I/O, no form, no clock - the decode is whatever task the caller's factory makes,
''' which is what lets the tests drive it with TaskCompletionSource.
''' </summary>
Public NotInheritable Class InFlightDecodeRegistry(Of T As Class)

    Friend NotInheritable Class Entry
        Public Key As String
        Public Group As String
        Public Task As Task(Of T)
        Public Abandoned As Boolean
        Public Completed As Boolean
    End Class

    ''' <summary>One caller's hold on a decode.</summary>
    Public NotInheritable Class Lease
        Private ReadOnly owner As InFlightDecodeRegistry(Of T)
        Private ReadOnly entry As Entry

        Friend Sub New(owner As InFlightDecodeRegistry(Of T), admission As DecodeAdmission, entry As Entry)
            Me.owner = owner
            Me.Admission = admission
            Me.entry = entry
        End Sub

        Public ReadOnly Property Admission As DecodeAdmission

        ''' <summary>The decode to wait on; Nothing when Refused or Busy.</summary>
        Public ReadOnly Property Task As Task(Of T)
            Get
                Return If(entry Is Nothing, Nothing, entry.Task)
            End Get
        End Property

        ''' <summary>Lets go of the decode without taking its result. Whatever it produces is
        ''' released by the registry - unless a later request for the path joins it first.</summary>
        Public Sub Abandon()
            If entry IsNot Nothing Then owner.AbandonEntry(entry)
        End Sub
    End Class

    Private ReadOnly gate As New Object()
    Private ReadOnly entries As New Dictionary(Of String, Entry)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly release As Action(Of T)
    Private ReadOnly abandonedCap As Integer

    ''' <param name="release">Disposes a result nobody owns. Called at most once per result.</param>
    ''' <param name="abandonedCap">How many abandoned decodes one group may have running
    ''' before a request for a NEW path in it is refused.</param>
    Public Sub New(release As Action(Of T), abandonedCap As Integer)
        If release Is Nothing Then Throw New ArgumentNullException(NameOf(release))
        Me.release = release
        Me.abandonedCap = Math.Max(1, abandonedCap)
    End Sub

    ''' <summary>
    ''' A decode of <paramref name="key"/>: the running abandoned one taken over, or a new one
    ''' made by <paramref name="start"/> - never a second one beside a running one.
    ''' </summary>
    ''' <param name="group">What the cap counts by (the volume root); "" groups everything.</param>
    Public Function Acquire(key As String, group As String, start As Func(Of Task(Of T))) As Lease
        If key Is Nothing Then Throw New ArgumentNullException(NameOf(key))
        If start Is Nothing Then Throw New ArgumentNullException(NameOf(start))
        Dim safe_Group As String = If(group, "")
        Dim created As Entry

        SyncLock gate
            Dim existing As Entry = Nothing
            If entries.TryGetValue(key, existing) Then
                If existing.Abandoned Then
                    ' Taken over: from here on the completion is ours to take, not the
                    ' registry's to release.
                    existing.Abandoned = False
                    Return New Lease(Me, DecodeAdmission.Joined, existing)
                End If
                If Not existing.Task.IsCompleted Then
                    Return New Lease(Me, DecodeAdmission.Busy, Nothing)
                End If
                ' Completed and owned: its owner has its result, the completion callback just
                ' has not run yet. This is a new request - start a new decode.
                entries.Remove(key)
            End If

            If CountAbandonedLocked(safe_Group) >= abandonedCap Then
                Return New Lease(Me, DecodeAdmission.Refused, Nothing)
            End If

            created = New Entry With {.Key = key, .Group = safe_Group, .Task = start()}
            If created.Task Is Nothing Then Throw New InvalidOperationException("The decode factory returned no task.")
            entries(key) = created
        End SyncLock

        ' Outside the lock only for tidiness - OnCompleted takes the lock itself, and a task
        ' that has already finished runs it right here, on this thread.
        created.Task.ContinueWith(Sub(done) OnCompleted(created),
                                  TaskContinuationOptions.ExecuteSynchronously)
        Return New Lease(Me, DecodeAdmission.Started, created)
    End Function

    ''' <summary>Abandoned decodes still running, in all groups.</summary>
    Public ReadOnly Property AbandonedCount As Integer
        Get
            SyncLock gate
                Dim count As Integer = 0
                For Each e As Entry In entries.Values
                    If e.Abandoned Then count += 1
                Next
                Return count
            End SyncLock
        End Get
    End Property

    ''' <summary>Decodes still running (owned or abandoned).</summary>
    Public ReadOnly Property InFlightCount As Integer
        Get
            SyncLock gate
                Return entries.Count
            End SyncLock
        End Get
    End Property

    Private Function CountAbandonedLocked(group As String) As Integer
        Dim count As Integer = 0
        For Each e As Entry In entries.Values
            If e.Abandoned AndAlso String.Equals(e.Group, group, StringComparison.OrdinalIgnoreCase) Then count += 1
        Next
        Return count
    End Function

    Private Sub AbandonEntry(e As Entry)
        Dim release_Now As Boolean = False
        SyncLock gate
            If e.Abandoned Then Return
            ' Marked in both branches, so a second Abandon can never release twice.
            e.Abandoned = True
            ' Still running: OnCompleted will see the mark and release. Already completed:
            ' it finished after the owner stopped waiting but before it said so - the
            ' completion found an owner and left the result alone, nobody can join it any
            ' more (it has left the table), so it is released here.
            release_Now = e.Completed
        End SyncLock
        If release_Now Then ReleaseResult(e.Task)
    End Sub

    Private Sub OnCompleted(e As Entry)
        Dim release_Now As Boolean
        SyncLock gate
            Dim current As Entry = Nothing
            If entries.TryGetValue(e.Key, current) AndAlso current Is e Then entries.Remove(e.Key)
            e.Completed = True
            release_Now = e.Abandoned
        End SyncLock
        If release_Now Then ReleaseResult(e.Task)
    End Sub

    Private Sub ReleaseResult(task As Task(Of T))
        Try
            If task.Status <> TaskStatus.RanToCompletion Then Return
            Dim result As T = task.Result
            If result Is Nothing Then Return
            release(result)
        Catch
            ' A release that throws must not take the pool thread's continuation down with it.
        End Try
    End Sub

End Class
