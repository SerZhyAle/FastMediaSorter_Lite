Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' The scheduler of scheduled file operations (ticket 222, sections 4.4 and 5): the part that lives
' inside the Share Manager's tray process and turns stored definitions into runs.
'
' ONE TIMER PER ENABLED OPERATION, armed from the one next-slot function and re-armed AFTER each run,
' outside the run itself (re-arming from inside the worker races). A timer fires on a pool thread,
' never on the UI thread. A single timer never sleeps longer than <see cref="MaxTimerSpan"/>: it wakes,
' asks the clock, and re-arms - which keeps a laptop that slept through a slot, a clock that was
' changed and a daylight-saving jump from silently moving a run by hours.
'
' ONE LANE. At most one operation runs at a time; a second one due while one runs waits its turn. A
' trigger for an operation that is already running or already waiting is REFUSED with a note - no queue
' of copies, no replacement (12.2 default 5).
'
' THE STATE LIVES IN THE STORE. The engine keeps an in-memory copy for reading, but every run state it
' changes - the two timestamps, the armed slot, a halt - is written to the operation's file when it
' changes, so a restart, a kill or a crash arms from what is on disk.

Friend Interface IScheduleTimerFactory
    ''' <summary>A one-shot timer: <paramref name="callback"/> once, after <paramref name="due"/>.</summary>
    Function Start(due As TimeSpan, callback As Action) As IDisposable
End Interface

Friend NotInheritable Class ThreadingTimerFactory
    Implements IScheduleTimerFactory

    Public Function Start(due As TimeSpan, callback As Action) As IDisposable Implements IScheduleTimerFactory.Start
        Return New Timer(Sub(state As Object) callback(), Nothing, due, Timeout.InfiniteTimeSpan)
    End Function
End Class

''' <summary>The last run that did not end well, as the tray and the page show it.</summary>
Friend NotInheritable Class ScheduledFailureInfo
    Friend ReadOnly OperationId As String
    Friend ReadOnly OperationName As String
    Friend ReadOnly Outcome As ScheduledRunOutcome
    ''' <summary>The machine text of the run's record: "code|argument".</summary>
    Friend ReadOnly Detail As String

    Friend Sub New(operationId As String, operationName As String, outcome As ScheduledRunOutcome, detail As String)
        Me.OperationId = operationId
        Me.OperationName = operationName
        Me.Outcome = outcome
        Me.Detail = detail
    End Sub
End Class

''' <summary>What a manual trigger came to.</summary>
Friend Enum TriggerOutcome
    Started = 0
    NotFound = 1
    ''' <summary>That operation is running or waiting its turn.</summary>
    AlreadyRunning = 2
    ''' <summary>Run all now while the scheduler is paused.</summary>
    Paused = 3
    NothingToRun = 4
End Enum

''' <summary>An operation as the page shows it: the definition and what the engine is doing with it.</summary>
Friend NotInheritable Class ScheduledOperationState
    Friend ReadOnly Operation As ScheduledOperation
    Friend ReadOnly IsRunning As Boolean
    Friend ReadOnly IsQueued As Boolean

    Friend Sub New(operation As ScheduledOperation, isRunning As Boolean, isQueued As Boolean)
        Me.Operation = operation
        Me.IsRunning = isRunning
        Me.IsQueued = isQueued
    End Sub
End Class

Friend NotInheritable Class ScheduledEngine
    Implements IDisposable

    ''' <summary>The longest a timer sleeps before it asks the clock again.</summary>
    Friend Shared ReadOnly MaxTimerSpan As TimeSpan = TimeSpan.FromMinutes(30)

    Private ReadOnly store As ScheduledStore
    Private ReadOnly journalLog As ScheduledJournal
    Private ReadOnly services As ScheduledServices
    Private ReadOnly executor As ScheduledExecutor
    Private ReadOnly timers As IScheduleTimerFactory
    Private ReadOnly gate As New Object()
    Private ReadOnly ops As New Dictionary(Of String, ScheduledOperation)(StringComparer.Ordinal)
    Private ReadOnly armed As New Dictionary(Of String, IDisposable)(StringComparer.Ordinal)
    ''' <summary>Running or waiting for its turn: a trigger for one of these is refused.</summary>
    Private ReadOnly inLane As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly lane As New SemaphoreSlim(1, 1)
    Private ReadOnly cancellation As New CancellationTokenSource()
    Private ReadOnly pending As New List(Of Task)()
    Private executing As String = ""
    Private started As Boolean
    Private disposed As Boolean
    Private lastFailureInfo As ScheduledFailureInfo
    Private lastTimeZoneId As String = ""

    ''' <summary>How long after start a missed slot waits before it runs (5): do not fight sign-in.</summary>
    Friend Property CatchUpDelay As TimeSpan = TimeSpan.FromSeconds(60)

    ''' <summary>Raised, on whatever thread, whenever what the page shows may have changed.</summary>
    Friend Event Changed As EventHandler

    Friend Sub New(store As ScheduledStore, journal As ScheduledJournal, services As ScheduledServices,
                   Optional timers As IScheduleTimerFactory = Nothing)
        Me.store = store
        Me.journalLog = journal
        Me.services = services
        Me.executor = New ScheduledExecutor(services)
        Me.timers = If(timers, New ThreadingTimerFactory())
    End Sub

    Friend ReadOnly Property Journal As ScheduledJournal
        Get
            Return journalLog
        End Get
    End Property

    Friend ReadOnly Property StoreInstance As ScheduledStore
        Get
            Return store
        End Get
    End Property

    Friend ReadOnly Property RunExecutor As ScheduledExecutor
        Get
            Return executor
        End Get
    End Property

    ''' <summary>"The last failure", for the tray item's hint: which operation and what went wrong (a code,
    ''' composed into words where it is shown), or Nothing when the last run of every operation was fine.</summary>
    Friend ReadOnly Property LastFailure As ScheduledFailureInfo
        Get
            SyncLock gate
                Return lastFailureInfo
            End SyncLock
        End Get
    End Property

    ' --- machine switches --------------------------------------------------------------------------

    Private ReadOnly Property StandDown As Boolean
        Get
            Return Not ScheduledSettings.FeatureEnabled OrElse ScheduledSettings.Paused
        End Get
    End Property

    Friend Property Paused As Boolean
        Get
            Return ScheduledSettings.Paused
        End Get
        Set(value As Boolean)
            If ScheduledSettings.Paused = value Then Return
            ScheduledSettings.Paused = value
            ReArmAll(fromResume:=Not value)
        End Set
    End Property

    Friend Property FeatureEnabled As Boolean
        Get
            Return ScheduledSettings.FeatureEnabled
        End Get
        Set(value As Boolean)
            If ScheduledSettings.FeatureEnabled = value Then Return
            ScheduledSettings.FeatureEnabled = value
            ReArmAll(fromResume:=value)
        End Set
    End Property

    Friend Property MissedRuns As MissedRunPolicy
        Get
            Return ScheduledSettings.MissedRuns
        End Get
        Set(value As MissedRunPolicy)
            ScheduledSettings.MissedRuns = value
            RaiseChanged()
        End Set
    End Property

    ''' <summary>The system woke from sleep, or the clock moved: a slot that came due meanwhile runs now (once),
    ''' everything else keeps its stored slot. Not a missed run - the program was here the whole time.
    ''' If the local time zone changed (ticket 231, 3.7), re-arms on the grid in the new zone.</summary>
    Friend Sub Wake()
        Dim due As New List(Of KeyValuePair(Of String, DateTime))()
        Dim zoneChanged As Boolean
        SyncLock gate
            If Not started OrElse disposed Then Return
            Dim now As DateTime = services.Clock.Invoke()
            Dim tz As TimeZoneInfo = CurrentTimeZone()
            zoneChanged = lastTimeZoneId.Length > 0 AndAlso Not String.Equals(lastTimeZoneId, If(tz?.Id, ""), StringComparison.Ordinal)
            If tz IsNot Nothing Then lastTimeZoneId = tz.Id
            For Each op As ScheduledOperation In ops.Values.ToList()
                If Not op.IsArmable OrElse StandDown Then Continue For
                If zoneChanged Then
                    ArmLocked(op, now)
                    Continue For
                End If
                Dim slot As DateTime? = op.Record.NextRunAt
                If slot.HasValue AndAlso slot.Value <= now AndAlso Not inLane.Contains(op.Id) Then
                    Disarm(op.Id)
                    due.Add(New KeyValuePair(Of String, DateTime)(op.Id, slot.Value))
                Else
                    ArmLocked(op, now)
                End If
            Next
        End SyncLock
        For Each item As KeyValuePair(Of String, DateTime) In due
            Track(Enqueue(item.Key, ScheduledTrigger.Schedule, item.Value))
        Next
        If zoneChanged Then RaiseChanged()
    End Sub

    Private Function CurrentTimeZone() As TimeZoneInfo
        Return If(services.TimeZone, Function() TimeZoneInfo.Local).Invoke()
    End Function

    ''' <summary>Stands every timer down or arms them again. Releasing a pause or the feature switch is
    ''' NOT a missed run (5): nothing catches up, the next slot is the first grid point after now.</summary>
    Private Sub ReArmAll(fromResume As Boolean)
        SyncLock gate
            If Not started Then Return
            Dim now As DateTime = services.Clock.Invoke()
            For Each op As ScheduledOperation In ops.Values.ToList()
                ArmLocked(op, now)
            Next
        End SyncLock
        RaiseChanged()
    End Sub

    ' --- lifetime ----------------------------------------------------------------------------------

    ''' <summary>Loads the stored operations and arms them; slots that came due while the program was not
    ''' running are handled by the missed-run policy (5).</summary>
    Friend Sub Start()
        Dim late As New List(Of KeyValuePair(Of String, DateTime))()
        SyncLock gate
            If started OrElse disposed Then Return
            started = True
            Dim tz As TimeZoneInfo = CurrentTimeZone()
            If tz IsNot Nothing Then lastTimeZoneId = tz.Id
            For Each op As ScheduledOperation In store.LoadAll()
                ops(op.Id) = op
            Next
            Dim now As DateTime = services.Clock.Invoke()
            Dim policy As MissedRunPolicy = ScheduledSettings.MissedRuns
            Dim stood As Boolean = StandDown
            For Each op As ScheduledOperation In ops.Values.OrderBy(Function(o As ScheduledOperation) o.CreatedAt).ToList()
                Dim due As DateTime? = op.Record.NextRunAt
                Select Case MissedRunRules.DecideAtStart(op, now, policy, stood)
                    Case StartupDecision.Missed
                        ' Rule 5: recorded as missed, in the history only. The exclamation mark is for a skipped RUN (rule 4):
                        ' the program was not there, which is neither the network nor the other device being off.
                        WriteNote(op, "missed", ScheduledTrigger.Schedule, ScheduledJson.FormatTime(due))
                    Case StartupDecision.RunLate
                        ' The slot that came due, or - for a skipped run whose next slot is still ahead - now.
                        late.Add(New KeyValuePair(Of String, DateTime)(op.Id, If(due.HasValue AndAlso due.Value <= now, due.Value, now)))
                End Select
                ArmLocked(op, now)
            Next
        End SyncLock
        If late.Count > 0 Then
            Dim token As CancellationToken = cancellation.Token
            Track(Task.Run(Async Function()
                               Try
                                   Await Task.Delay(CatchUpDelay, token)
                               Catch ex As OperationCanceledException
                                   Return
                               End Try
                               For Each item As KeyValuePair(Of String, DateTime) In late
                                   If token.IsCancellationRequested Then Return
                                   ' Not while paused since: pausing is the user's own act.
                                   If StandDown Then Return
                                   Await Enqueue(item.Key, ScheduledTrigger.CatchUp, item.Value)
                               Next
                           End Function))
        End If
        RaiseChanged()
    End Sub

    Friend Sub Dispose() Implements IDisposable.Dispose
        SyncLock gate
            If disposed Then Return
            disposed = True
            For Each timer As IDisposable In armed.Values
                Try : timer.Dispose() : Catch : End Try
            Next
            armed.Clear()
        End SyncLock
        Try : cancellation.Cancel() : Catch : End Try
    End Sub

    ''' <summary>Completes when nothing is running or waiting. For tests and for an orderly exit.</summary>
    Friend Async Function WhenIdleAsync() As Task
        While True
            Dim snapshot As Task()
            SyncLock gate
                pending.RemoveAll(Function(t As Task) t.IsCompleted)
                snapshot = pending.ToArray()
            End SyncLock
            If snapshot.Length = 0 Then Return
            Try
                Await Task.WhenAll(snapshot)
            Catch
            End Try
        End While
    End Function

    Private Sub Track(task As Task)
        SyncLock gate
            pending.Add(task)
        End SyncLock
    End Sub

    ' --- reading -----------------------------------------------------------------------------------

    Friend Function Snapshot() As List(Of ScheduledOperationState)
        SyncLock gate
            Return ops.Values.OrderBy(Function(o As ScheduledOperation) o.CreatedAt).
                Select(Function(o As ScheduledOperation) New ScheduledOperationState(o.Clone(), executing = o.Id,
                                                                                    inLane.Contains(o.Id) AndAlso executing <> o.Id)).
                ToList()
        End SyncLock
    End Function

    Friend Function TryGet(id As String) As ScheduledOperation
        SyncLock gate
            Dim op As ScheduledOperation = Nothing
            Return If(ops.TryGetValue(id, op), op.Clone(), Nothing)
        End SyncLock
    End Function

    Private Sub RaiseChanged()
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    ' --- editing -----------------------------------------------------------------------------------

    ''' <summary>
    ''' Saves a new or edited definition: corrections applied, refusals returned (the editor shows them
    ''' and keeps its dialog), then stored and armed. Editing is the user fixing the operation, so a halt
    ''' is lifted. A changed schedule restarts its grid from today.
    ''' </summary>
    Friend Function Upsert(operation As ScheduledOperation) As List(Of ScheduledIssue)
        Dim ignored As String = ""
        Return Upsert(operation, ignored)
    End Function

    ''' <param name="savedId">The id the operation is stored under (new ones get theirs here).</param>
    Friend Function Upsert(operation As ScheduledOperation, ByRef savedId As String) As List(Of ScheduledIssue)
        savedId = ""
        Dim op As ScheduledOperation = operation.Clone()
        ScheduledValidation.Normalize(op)
        Dim issues As List(Of ScheduledIssue) = ScheduledValidation.Validate(op, services.Protection, services.ResourceFacts)
        If issues.Count > 0 Then Return issues

        SyncLock gate
            Dim now As DateTime = services.Clock.Invoke()
            Dim existing As ScheduledOperation = Nothing
            If Not String.IsNullOrEmpty(op.Id) AndAlso ops.TryGetValue(op.Id, existing) AndAlso existing.Halt = ScheduledHalt.InvalidDefinition Then
                ' Saving would rewrite an action this build does not know as one it does - a newer build's
                ' definition is deleted or left alone, never "fixed" here.
                Return New List(Of ScheduledIssue) From {New ScheduledIssue(ScheduledIssueCode.DefinitionUnreadable)}
            End If
            If String.IsNullOrEmpty(op.Id) OrElse Not ops.TryGetValue(op.Id, existing) Then
                If String.IsNullOrEmpty(op.Id) Then op.Id = Guid.NewGuid().ToString("N")
                op.CreatedAt = now
                op.StartDate = now.Date
                op.Record = New ScheduledRunRecord()
            Else
                op.CreatedAt = existing.CreatedAt
                op.Record = existing.Record
                If op.AnchorMinutes <> existing.AnchorMinutes OrElse op.IntervalMinutes <> existing.IntervalMinutes Then
                    op.StartDate = now.Date
                Else
                    op.StartDate = existing.StartDate
                End If
            End If
            op.Halt = ScheduledHalt.None
            op.HaltDetail = ""
            store.SaveDefinition(op, clearHalt:=True)
            ops(op.Id) = op
            savedId = op.Id
            ScheduledSettings.FollowList(ops.Count)
            ArmLocked(op, now)
        End SyncLock
        RaiseChanged()
        Return issues
    End Function

    Friend Sub Delete(id As String)
        SyncLock gate
            Disarm(id)
            ops.Remove(id)
            Try
                store.Delete(id)
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: deleting " & id, ex)
            End Try
            ScheduledSettings.FollowList(ops.Count)
        End SyncLock
        RaiseChanged()
    End Sub

    ''' <summary>The per-operation switch. Turning it on is the user acting, so a halt is lifted.</summary>
    Friend Sub SetEnabled(id As String, enabled As Boolean)
        SyncLock gate
            Dim op As ScheduledOperation = Nothing
            If Not ops.TryGetValue(id, op) OrElse op.Halt = ScheduledHalt.InvalidDefinition Then Return
            op.Enabled = enabled
            If enabled Then
                op.Halt = ScheduledHalt.None
                op.HaltDetail = ""
            End If
            store.SaveDefinition(op, clearHalt:=enabled)
            ArmLocked(op, services.Clock.Invoke())
        End SyncLock
        RaiseChanged()
    End Sub

    ''' <summary>Adds or replaces an operation imported by <see cref="ShareSettingsTransfer"/>.</summary>
    Friend Sub ReloadImported(imported As ScheduledOperation)
        If imported Is Nothing OrElse String.IsNullOrEmpty(imported.Id) Then Return
        SyncLock gate
            Dim op As ScheduledOperation = imported.Clone()
            ops(op.Id) = op
            If started AndAlso Not disposed Then
                ArmLocked(op, services.Clock.Invoke())
            End If
        End SyncLock
        RaiseChanged()
    End Sub

    ' --- arming ------------------------------------------------------------------------------------

    Private Sub Disarm(id As String)
        Dim timer As IDisposable = Nothing
        If armed.TryGetValue(id, timer) Then
            armed.Remove(id)
            Try : timer.Dispose() : Catch : End Try
        End If
    End Sub

    ''' <summary>Arms the first grid point after <paramref name="now"/>. Caller holds the gate.</summary>
    Private Sub ArmLocked(op As ScheduledOperation, now As DateTime)
        Disarm(op.Id)
        If disposed Then Return
        If Not op.IsArmable Then
            SetNextRunLocked(op, Nothing)
            Return
        End If
        ' Stood down (paused or the feature off): the timers go, the stored slot stays - and a release
        ' re-arms from NOW, with no catch-up.
        If StandDown Then Return
        Dim slot As DateTime = ScheduleMath.NextSlot(op, now, CurrentTimeZone())
        SetNextRunLocked(op, slot)
        Dim wait As TimeSpan = slot - now
        If wait > MaxTimerSpan Then wait = MaxTimerSpan
        If wait < TimeSpan.Zero Then wait = TimeSpan.Zero
        Dim id As String = op.Id
        armed(id) = timers.Start(wait, Sub() OnTimer(id))
    End Sub

    Private Sub SetNextRunLocked(op As ScheduledOperation, value As DateTime?)
        If Nullable.Equals(op.Record.NextRunAt, value) Then Return
        op.Record.NextRunAt = value
        PersistLocked(op)
    End Sub

    Private Sub PersistLocked(op As ScheduledOperation)
        Try
            store.SaveRunState(op.Id, op.Record, op.Halt, op.HaltDetail)
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: saving the state of " & op.DisplayName(), ex)
        End Try
    End Sub

    ''' <summary>A timer woke. Runs on a pool thread.</summary>
    Private Sub OnTimer(id As String)
        Dim dueSlot As DateTime
        SyncLock gate
            If disposed Then Return
            armed.Remove(id)
            Dim op As ScheduledOperation = Nothing
            If Not ops.TryGetValue(id, op) OrElse Not op.IsArmable OrElse StandDown Then Return
            Dim now As DateTime = services.Clock.Invoke()
            Dim slot As DateTime? = op.Record.NextRunAt
            If Not slot.HasValue OrElse slot.Value > now Then
                ' Woke early (the cap, or the clock moved): ask again from here.
                ArmLocked(op, now)
                Return
            End If
            dueSlot = slot.Value
        End SyncLock
        Track(Enqueue(id, ScheduledTrigger.Schedule, dueSlot))
    End Sub

    ' --- running -----------------------------------------------------------------------------------

    ''' <summary>Run now: bypasses the due time and does not move the schedule. Allowed while paused -
    ''' pausing stops the SCHEDULE, and this is the user's own act.</summary>
    Friend Function RunNow(id As String) As TriggerOutcome
        SyncLock gate
            If Not ops.ContainsKey(id) Then Return TriggerOutcome.NotFound
            If inLane.Contains(id) Then
                WriteNote(ops(id), "busy", ScheduledTrigger.RunNow, "")
                Return TriggerOutcome.AlreadyRunning
            End If
            inLane.Add(id)
        End SyncLock
        Track(RunLane(id, ScheduledTrigger.RunNow, Nothing))
        RaiseChanged()
        Return TriggerOutcome.Started
    End Function

    ''' <summary>Run all now: one serialized chain over every enabled operation (5). Refused while paused.</summary>
    Friend Function RunAllNow() As TriggerOutcome
        Dim ids As List(Of String)
        SyncLock gate
            If ScheduledSettings.Paused Then Return TriggerOutcome.Paused
            ids = ops.Values.Where(Function(o As ScheduledOperation) o.Enabled AndAlso Not inLane.Contains(o.Id)).
                OrderBy(Function(o As ScheduledOperation) o.CreatedAt).Select(Function(o As ScheduledOperation) o.Id).ToList()
            If ids.Count = 0 Then Return TriggerOutcome.NothingToRun
            For Each id As String In ids
                inLane.Add(id)
            Next
        End SyncLock
        Track(Task.Run(Async Function()
                           For Each id As String In ids
                               Await RunLane(id, ScheduledTrigger.RunAll, Nothing)
                           Next
                       End Function))
        RaiseChanged()
        Return TriggerOutcome.Started
    End Function

    ''' <summary>A trigger that arrives from the schedule or the catch-up: refused while the same
    ''' operation is already in the lane.</summary>
    Private Async Function Enqueue(id As String, trigger As ScheduledTrigger, dueSlot As DateTime?) As Task
        SyncLock gate
            Dim op As ScheduledOperation = Nothing
            If Not ops.TryGetValue(id, op) Then Return
            If inLane.Contains(id) Then
                ' Still running (a run longer than its interval): refused with a note, and the schedule
                ' goes on from the next grid point - no second copy, no replacement.
                WriteNote(op, "busy", trigger, "")
                ArmLocked(op, services.Clock.Invoke())
                Return
            End If
            inLane.Add(id)
        End SyncLock
        Await RunLane(id, trigger, dueSlot)
    End Function

    Private Async Function RunLane(id As String, trigger As ScheduledTrigger, dueSlot As DateTime?) As Task
        Dim token As CancellationToken = cancellation.Token
        Try
            Await lane.WaitAsync(token)
            Try
                Dim op As ScheduledOperation = TryGet(id)
                If op Is Nothing Then Return
                SyncLock gate
                    executing = id
                End SyncLock
                RaiseChanged()

                If op.Halt = ScheduledHalt.Wall AndAlso (trigger = ScheduledTrigger.Schedule OrElse trigger = ScheduledTrigger.CatchUp) Then
                    ' A walled operation is only re-checked, silently, at its slots: no run, no note, until
                    ' what stopped it is gone - then it comes back by itself and says so once.
                    Dim problem As SideProblem = SideProblem.Ok
                    Try
                        problem = Await Task.Run(Function() executor.Preflight(op))
                    Catch ex As Exception
                        AppFileLogger.LogException("Scheduled operations: re-checking " & op.DisplayName(), ex)
                        problem = New SideProblem(SideProblemKind.Unreachable, op.Source.Describe())
                    End Try
                    If problem.IsProblem Then
                        SyncLock gate
                            Dim live As ScheduledOperation = Nothing
                            If ops.TryGetValue(id, live) Then ArmLocked(live, services.Clock.Invoke())
                        End SyncLock
                        Return
                    End If
                    SyncLock gate
                        Dim live As ScheduledOperation = Nothing
                        If ops.TryGetValue(id, live) Then
                            live.Halt = ScheduledHalt.None
                            live.HaltDetail = ""
                            PersistLocked(live)
                            WriteNote(live, "resumed", trigger, "")
                        End If
                    End SyncLock
                    op = TryGet(id)
                    If op Is Nothing Then Return
                End If

                If trigger = ScheduledTrigger.CatchUp AndAlso dueSlot.HasValue Then
                    WriteNote(op, "late", trigger, ScheduledJson.FormatTime(dueSlot))
                End If

                AppFileLogger.WriteLine(DateTimeOffset.UtcNow.ToString("O", Globalization.CultureInfo.InvariantCulture) &
                    " [SHARE-STATE] scheduled-trigger " & ScheduledJson.TriggerName(trigger))
                Dim result As ScheduledRunResult = Await Task.Run(Function() executor.Run(op, token))
                Apply(op, result, trigger)
            Finally
                SyncLock gate
                    executing = ""
                End SyncLock
                lane.Release()
            End Try
        Catch ex As OperationCanceledException
            ' Shutting down: the run ended early and nothing else is owed.
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: running " & id, ex)
        Finally
            SyncLock gate
                inLane.Remove(id)
            End SyncLock
            RaiseChanged()
        End Try
    End Function

    ''' <summary>Writes what a run left on the operation, journals it, and - for a scheduled run - re-arms
    ''' from the clock, so the next slot is the first grid point after the run, never the end of it
    ''' plus an interval.</summary>
    Private Sub Apply(op As ScheduledOperation, result As ScheduledRunResult, trigger As ScheduledTrigger)
        Dim notifySnapshot As ScheduledOperation = Nothing
        SyncLock gate
            Dim current As ScheduledOperation = Nothing
            If ops.TryGetValue(op.Id, current) Then
                Dim record As ScheduledRunRecord = current.Record
                record.LastRunAt = result.StartedAt
                record.LastTrigger = trigger
                If result.Outcome = ScheduledRunOutcome.Skipped Then
                    ' Ticket 231, 3.5: a skipped run marks SkippedSinceSuccess, never clears a prior failure
                    ' ("a failed run followed by a skipped run stays a failure"), and never halts the operation.
                    record.SkippedSinceSuccess = True
                    If record.LastOutcome <> ScheduledRunOutcome.Failed AndAlso record.LastOutcome <> ScheduledRunOutcome.Wall Then
                        record.LastOutcome = ScheduledRunOutcome.Skipped
                        record.LastFileCount = 0
                        record.LastFailedCount = 0
                        record.LastError = ErrorTextOf(result)
                    End If
                Else
                    record.LastOutcome = result.Outcome
                    record.LastFileCount = result.Files
                    record.LastFailedCount = result.Failed
                    record.LastError = ErrorTextOf(result)
                    If result.Outcome = ScheduledRunOutcome.Ok Then
                        record.LastSuccessAt = result.StartedAt
                        record.SkippedSinceSuccess = False
                        If current.Halt = ScheduledHalt.Wall Then
                            current.Halt = ScheduledHalt.None
                            current.HaltDetail = ""
                        End If
                    End If
                End If
                If result.Halt <> ScheduledHalt.None Then
                    current.Halt = result.Halt
                    current.HaltDetail = result.Detail
                End If
                If trigger = ScheduledTrigger.Schedule OrElse trigger = ScheduledTrigger.CatchUp Then
                    ArmLocked(current, services.Clock.Invoke())
                End If
                PersistLocked(current)
                If current.ShowBalloonOnError AndAlso
                   (result.Outcome = ScheduledRunOutcome.Failed OrElse result.Outcome = ScheduledRunOutcome.Wall) Then
                    notifySnapshot = current.Clone()
                End If
            End If

            If result.Outcome = ScheduledRunOutcome.Ok OrElse result.Outcome = ScheduledRunOutcome.None Then
                If lastFailureInfo IsNot Nothing AndAlso String.Equals(lastFailureInfo.OperationId, op.Id, StringComparison.Ordinal) Then
                    lastFailureInfo = Nothing
                End If
            ElseIf result.Outcome <> ScheduledRunOutcome.Cancelled AndAlso result.Outcome <> ScheduledRunOutcome.Skipped Then
                lastFailureInfo = New ScheduledFailureInfo(op.Id, op.DisplayName(), result.Outcome, ErrorTextOf(result))
            End If
        End SyncLock

        If notifySnapshot IsNot Nothing Then
            Try
                services.NotifyError?.Invoke(notifySnapshot, result)
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: error notification for " & notifySnapshot.DisplayName(), ex)
            End Try
        End If

        AppFileLogger.WriteLine(DateTimeOffset.UtcNow.ToString("O", Globalization.CultureInfo.InvariantCulture) &
            " [SHARE-STATE] scheduled-result " & ScheduledJson.OutcomeText(result.Outcome) &
            " processed=" & result.Files.ToString(Globalization.CultureInfo.InvariantCulture) &
            " failed=" & result.Failed.ToString(Globalization.CultureInfo.InvariantCulture))
        journalLog.Append(New JournalEntry With {
            .Timestamp = result.StartedAt, .Kind = "run", .Code = ScheduledJson.OutcomeText(result.Outcome),
            .OperationId = op.Id, .OperationName = op.DisplayName(), .Trigger = ScheduledJson.TriggerName(trigger),
            .Count = result.Files, .Failed = result.Failed, .Skipped = result.Skipped, .Bytes = result.Bytes,
            .DurationMs = CLng(result.Duration.TotalMilliseconds), .Verdict = result.Verdict,
            .Source = result.SourceDescription,
            .Destination = If(result.ArchivePath.Length > 0, result.ArchivePath, result.DestinationDescription),
            .Detail = ErrorTextOf(result)})
    End Sub



    Private Shared Function ErrorTextOf(result As ScheduledRunResult) As String
        Select Case result.Outcome
            Case ScheduledRunOutcome.Wall, ScheduledRunOutcome.Refused, ScheduledRunOutcome.Skipped
                Return result.Detail
            Case ScheduledRunOutcome.Failed
                Return result.FirstError
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>One note line of the history: a missed or late slot, a refused trigger, a wall lifted.</summary>
    Private Sub WriteNote(op As ScheduledOperation, code As String, trigger As ScheduledTrigger, detail As String)
        journalLog.Append(New JournalEntry With {
            .Timestamp = services.Clock.Invoke(), .Kind = "note", .Code = code, .OperationId = op.Id,
            .OperationName = op.DisplayName(), .Trigger = ScheduledJson.TriggerName(trigger),
            .Source = op.Source.Describe(), .Detail = If(detail, "")})
    End Sub

End Class
