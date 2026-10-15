Option Strict On

Imports System.IO
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The scheduler (ticket 222, sections 4.4, 5 and 8): arming from the one next-slot function, the
''' missed-run policy, pause, the single lane, the operation-level wall - all on a manual clock and a
''' timer factory that fires only when told to.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledEngineTests

    Private NotInheritable Class Kit
        Implements IDisposable

        Friend ReadOnly Tree As New ScratchTree()
        Friend ReadOnly Hive As New ScratchHive()
        Friend ReadOnly Clock As ManualClock
        Friend ReadOnly Timers As New RecordingTimers()
        Friend ReadOnly Store As ScheduledStore
        Friend ReadOnly Journal As ScheduledJournal
        Friend ReadOnly Services As ScheduledServices
        Friend ReadOnly Engine As ScheduledEngine

        Friend Sub New(start As DateTime)
            Clock = New ManualClock(start)
            Dim folder As String = Tree.Dir("state")
            Store = New ScheduledStore(folder)
            Journal = New ScheduledJournal(folder)
            Services = New ScheduledServices With {
                .Clock = AddressOf Clock.Read,
                .VolumeFacts = Function(path As String) New DeleteVolumeFacts With {.Kind = DeleteVolumeKind.FixedDisk},
                .SendToBin = Function(path As String) As Boolean
                                 File.Delete(path)
                                 Return False
                             End Function}
            Engine = New ScheduledEngine(Store, Journal, Services, Timers) With {.CatchUpDelay = TimeSpan.Zero}
        End Sub

        ''' <summary>A copy operation over two scratch folders, due daily at 03:00 starting on the clock's day.</summary>
        Friend Function CopyOp(id As String, Optional name As String = "nightly") As ScheduledOperation
            Dim op As New ScheduledOperation With {.Id = id, .Name = name, .Action = ScheduledAction.Copy,
                .AnchorMinutes = 3 * 60, .IntervalMinutes = 24 * 60, .StartDate = Clock.Now.Date, .CreatedAt = Clock.Now.AddDays(-10)}
            op.Source.Path = Tree.Dir("src-" & id)
            op.Destination.Path = Tree.Dir("dst-" & id)
            Return op
        End Function

        ''' <summary>Stores an operation the way a previous session left it, with a persisted armed slot.</summary>
        Friend Sub Seed(op As ScheduledOperation)
            Store.SaveDefinition(op)
            Store.SaveRunState(op.Id, op.Record, op.Halt, op.HaltDetail)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Engine.Dispose()
            Hive.Dispose()
            Tree.Dispose()
        End Sub
    End Class

    Private Shared Function At(day As Integer, hour As Integer, minute As Integer, Optional second As Integer = 0) As DateTime
        Return New DateTime(2026, 10, day, hour, minute, second, DateTimeKind.Unspecified)
    End Function

    Private Shared Sub Idle(kit As Kit)
        kit.Engine.WhenIdleAsync().GetAwaiter().GetResult()
    End Sub

    Private Shared Function History(kit As Kit) As List(Of JournalEntry)
        Return kit.Journal.ReadRecent().AsEnumerable().Reverse().ToList()
    End Function

    ' --- arming ----------------------------------------------------------------------------------------

    <Fact>
    Public Sub Start_arms_every_enabled_operation_at_the_first_grid_point_after_now()
        Using kit As New Kit(At(13, 10, 0))
            kit.Seed(kit.CopyOp("aaa"))
            Dim off As ScheduledOperation = kit.CopyOp("bbb")
            off.Enabled = False
            kit.Seed(off)
            kit.Engine.Start()

            Assert.Single(kit.Timers.Live)
            Assert.Equal(At(14, 3, 0), kit.Engine.TryGet("aaa").Record.NextRunAt)
            Assert.Null(kit.Engine.TryGet("bbb").Record.NextRunAt)
            ' The persisted slot is what a restart reads.
            Assert.Equal(At(14, 3, 0), kit.Store.TryLoad("aaa").Record.NextRunAt)
            ' A timer never sleeps past its cap: it wakes, asks the clock, and re-arms.
            Assert.Equal(ScheduledEngine.MaxTimerSpan, kit.Timers.Live(0).Due)
        End Using
    End Sub

    <Fact>
    Public Sub A_due_slot_runs_and_the_next_one_is_counted_from_the_clock_not_the_end_of_the_run()
        Using kit As New Kit(At(13, 2, 59, 30))
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            File.WriteAllText(Path.Combine(op.Source.Path, "a.txt"), "A")
            kit.Seed(op)
            kit.Engine.Start()
            Assert.Equal(At(13, 3, 0), kit.Engine.TryGet("aaa").Record.NextRunAt)

            kit.Clock.Now = At(13, 3, 0, 5)
            Assert.Equal(1, kit.Timers.FireLive())
            Idle(kit)

            Dim after As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Equal(ScheduledRunOutcome.Ok, after.Record.LastOutcome)
            Assert.Equal(ScheduledTrigger.Schedule, after.Record.LastTrigger)
            Assert.Equal(1, after.Record.LastFileCount)
            Assert.Equal(At(13, 3, 0, 5), after.Record.LastRunAt)
            Assert.Equal(after.Record.LastRunAt, after.Record.LastSuccessAt)
            Assert.Equal(At(14, 3, 0), after.Record.NextRunAt)
            Assert.True(File.Exists(Path.Combine(op.Destination.Path, "a.txt")))
            Assert.Single(kit.Timers.Live)
        End Using
    End Sub

    <Fact>
    Public Sub A_timer_that_woke_early_only_rearms()
        Using kit As New Kit(At(13, 1, 0))
            kit.Seed(kit.CopyOp("aaa"))
            kit.Engine.Start()
            Assert.Equal(1, kit.Timers.FireLive())
            Idle(kit)
            Assert.Null(kit.Engine.TryGet("aaa").Record.LastRunAt)
            Assert.Single(kit.Timers.Live)
        End Using
    End Sub

    ' --- missed runs -----------------------------------------------------------------------------------

    <Fact>
    Public Sub Default_policy_runs_a_missed_slot_once_at_start_and_says_it_ran_late()
        Using kit As New Kit(At(13, 9, 0))
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            File.WriteAllText(Path.Combine(op.Source.Path, "a.txt"), "A")
            op.Record.NextRunAt = At(13, 3, 0)
            kit.Seed(op)
            kit.Engine.Start()
            Idle(kit)

            Dim after As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Equal(ScheduledTrigger.CatchUp, after.Record.LastTrigger)
            Assert.Equal(ScheduledRunOutcome.Ok, after.Record.LastOutcome)
            Assert.Equal(At(14, 3, 0), after.Record.NextRunAt)
            Dim lines As List(Of JournalEntry) = History(kit)
            Assert.Contains(lines, Function(e) e.Kind = "note" AndAlso e.Code = "late" AndAlso e.Detail = "2026-10-13T03:00:00")
            Assert.Single(lines, Function(e) e.Kind = "run")
        End Using
    End Sub

    <Fact>
    Public Sub Skip_policy_notes_the_missed_slot_and_runs_nothing()
        Using kit As New Kit(At(13, 9, 0))
            ScheduledSettings.MissedRuns = MissedRunPolicy.Skip
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            File.WriteAllText(Path.Combine(op.Source.Path, "a.txt"), "A")
            op.Record.NextRunAt = At(13, 3, 0)
            kit.Seed(op)
            kit.Engine.Start()
            Idle(kit)

            Assert.Null(kit.Engine.TryGet("aaa").Record.LastRunAt)
            Assert.Equal(At(14, 3, 0), kit.Engine.TryGet("aaa").Record.NextRunAt)
            Dim lines As List(Of JournalEntry) = History(kit)
            Assert.Single(lines)
            Assert.Equal("missed", lines(0).Code)
            Assert.Equal("2026-10-13T03:00:00", lines(0).Detail)
            Assert.False(File.Exists(Path.Combine(op.Destination.Path, "a.txt")))
        End Using
    End Sub

    <Fact>
    Public Sub A_paused_scheduler_has_no_missed_run_and_releasing_it_catches_up_with_nothing()
        Using kit As New Kit(At(13, 9, 0))
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            File.WriteAllText(Path.Combine(op.Source.Path, "a.txt"), "A")
            op.Record.NextRunAt = At(13, 3, 0)
            kit.Seed(op)
            ScheduledSettings.Paused = True
            kit.Engine.Start()
            Idle(kit)

            Assert.Empty(kit.Timers.Live)
            Assert.Null(kit.Engine.TryGet("aaa").Record.LastRunAt)
            Assert.Empty(kit.Journal.ReadRecent())

            kit.Engine.Paused = False
            Idle(kit)
            Assert.Single(kit.Timers.Live)
            Assert.Equal(At(14, 3, 0), kit.Engine.TryGet("aaa").Record.NextRunAt)
            Assert.Null(kit.Engine.TryGet("aaa").Record.LastRunAt)
            Assert.Empty(kit.Journal.ReadRecent())
        End Using
    End Sub

    ' --- manual triggers -------------------------------------------------------------------------------

    <Fact>
    Public Sub Run_now_does_not_move_the_schedule_and_works_while_paused()
        Using kit As New Kit(At(13, 10, 0))
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            File.WriteAllText(Path.Combine(op.Source.Path, "a.txt"), "A")
            kit.Seed(op)
            kit.Engine.Start()
            Dim slot As DateTime? = kit.Engine.TryGet("aaa").Record.NextRunAt
            kit.Engine.Paused = True

            Assert.Equal(TriggerOutcome.Started, kit.Engine.RunNow("aaa"))
            Idle(kit)

            Dim after As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Equal(ScheduledTrigger.RunNow, after.Record.LastTrigger)
            Assert.Equal(ScheduledRunOutcome.Ok, after.Record.LastOutcome)
            Assert.Equal(slot, after.Record.NextRunAt)
            Assert.True(File.Exists(Path.Combine(op.Destination.Path, "a.txt")))
        End Using
    End Sub

    <Fact>
    Public Sub Run_all_now_is_refused_while_paused_and_runs_every_enabled_operation_otherwise()
        Using kit As New Kit(At(13, 10, 0))
            Dim a As ScheduledOperation = kit.CopyOp("aaa")
            Dim b As ScheduledOperation = kit.CopyOp("bbb")
            Dim c As ScheduledOperation = kit.CopyOp("ccc")
            c.Enabled = False
            File.WriteAllText(Path.Combine(a.Source.Path, "a.txt"), "A")
            File.WriteAllText(Path.Combine(b.Source.Path, "b.txt"), "B")
            File.WriteAllText(Path.Combine(c.Source.Path, "c.txt"), "C")
            kit.Seed(a)
            kit.Seed(b)
            kit.Seed(c)
            kit.Engine.Start()

            kit.Engine.Paused = True
            Assert.Equal(TriggerOutcome.Paused, kit.Engine.RunAllNow())
            kit.Engine.Paused = False

            Assert.Equal(TriggerOutcome.Started, kit.Engine.RunAllNow())
            Idle(kit)
            Assert.True(File.Exists(Path.Combine(a.Destination.Path, "a.txt")))
            Assert.True(File.Exists(Path.Combine(b.Destination.Path, "b.txt")))
            Assert.False(File.Exists(Path.Combine(c.Destination.Path, "c.txt")))
            Assert.Equal(ScheduledTrigger.RunAll, kit.Engine.TryGet("aaa").Record.LastTrigger)
        End Using
    End Sub

    <Fact>
    Public Sub A_second_trigger_for_a_running_operation_is_refused_with_a_note()
        Using kit As New Kit(At(13, 10, 0))
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            File.WriteAllText(Path.Combine(op.Source.Path, "a.txt"), "A")
            kit.Seed(op)
            kit.Engine.Start()
            Dim reached As New ManualResetEventSlim(False)
            Dim release As New ManualResetEventSlim(False)
            kit.Services.BeforeItem = Sub(relative As String)
                                          reached.Set()
                                          release.Wait(TimeSpan.FromSeconds(20))
                                      End Sub

            Assert.Equal(TriggerOutcome.Started, kit.Engine.RunNow("aaa"))
            Assert.True(reached.Wait(TimeSpan.FromSeconds(20)))
            Assert.Equal(TriggerOutcome.AlreadyRunning, kit.Engine.RunNow("aaa"))
            Assert.True(kit.Engine.Snapshot().Single().IsRunning)
            release.Set()
            Idle(kit)

            Dim lines As List(Of JournalEntry) = History(kit)
            Assert.Single(lines, Function(e) e.Kind = "note" AndAlso e.Code = "busy")
            Assert.Single(lines, Function(e) e.Kind = "run")
        End Using
    End Sub

    ' --- the wall --------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_wall_stops_the_operation_once_and_it_comes_back_by_itself_when_the_cause_is_gone()
        Using kit As New Kit(At(13, 2, 59, 30))
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            Dim sourcePath As String = op.Source.Path
            Directory.Delete(sourcePath, True)
            kit.Seed(op)
            kit.Engine.Start()

            kit.Clock.Now = At(13, 3, 0, 5)
            kit.Timers.FireLive()
            Idle(kit)
            Dim walled As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Equal(ScheduledHalt.Wall, walled.Halt)
            Assert.Equal(ScheduledRunOutcome.Wall, walled.Record.LastOutcome)
            Assert.Single(History(kit))
            ' Still armed - only to look again.
            Assert.Single(kit.Timers.Live)

            ' The next slot: nothing has changed, so nothing is run and NOTHING is said.
            kit.Clock.Now = At(14, 3, 0, 5)
            kit.Timers.FireLive()
            Idle(kit)
            Assert.Single(History(kit))
            Assert.Equal(ScheduledHalt.Wall, kit.Engine.TryGet("aaa").Halt)

            ' The drive is back.
            Directory.CreateDirectory(sourcePath)
            File.WriteAllText(Path.Combine(sourcePath, "a.txt"), "A")
            kit.Clock.Now = At(15, 3, 0, 5)
            kit.Timers.FireLive()
            Idle(kit)
            Dim back As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Equal(ScheduledHalt.None, back.Halt)
            Assert.Equal(ScheduledRunOutcome.Ok, back.Record.LastOutcome)
            Assert.Contains(History(kit), Function(e) e.Kind = "note" AndAlso e.Code = "resumed")
        End Using
    End Sub

    ' --- editing ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub Upsert_refuses_a_contradictory_definition_and_arms_a_valid_one()
        Using kit As New Kit(At(13, 10, 0))
            kit.Engine.Start()
            Dim bad As ScheduledOperation = kit.CopyOp("")
            bad.Conditions.MinSizeBytes = 100
            bad.Conditions.MaxSizeBytes = 100
            Dim issues As List(Of ScheduledIssue) = kit.Engine.Upsert(bad)
            Assert.Contains(issues, Function(i) i.Code = ScheduledIssueCode.SizeBoundsContradict)
            Assert.Empty(kit.Engine.Snapshot())

            Dim good As ScheduledOperation = kit.CopyOp("")
            Assert.Empty(kit.Engine.Upsert(good))
            Dim stored As ScheduledOperationState = kit.Engine.Snapshot().Single()
            Assert.False(String.IsNullOrEmpty(stored.Operation.Id))
            Assert.Single(kit.Timers.Live)
            Assert.Equal(At(14, 3, 0), stored.Operation.Record.NextRunAt)
            Assert.True(ScheduledSettings.FeatureEnabled)
        End Using
    End Sub

    <Fact>
    Public Sub Changing_the_schedule_restarts_the_grid_from_today_and_a_delete_disarms()
        Using kit As New Kit(At(13, 10, 0))
            kit.Engine.Start()
            kit.Engine.Upsert(kit.CopyOp("aaa"))
            Dim edit As ScheduledOperation = kit.Engine.TryGet("aaa")
            edit.AnchorMinutes = 22 * 60
            kit.Engine.Upsert(edit)
            Assert.Equal(At(13, 22, 0), kit.Engine.TryGet("aaa").Record.NextRunAt)

            kit.Engine.Delete("aaa")
            Assert.Empty(kit.Timers.Live)
            Assert.Null(kit.Store.TryLoad("aaa"))
            Assert.False(ScheduledSettings.FeatureEnabled)
        End Using
    End Sub

    <Fact>
    Public Sub A_definition_this_build_cannot_read_is_never_rewritten()
        Using kit As New Kit(At(13, 10, 0))
            kit.Seed(kit.CopyOp("aaa"))
            Dim definition As String = Path.Combine(kit.Store.FolderPath, "aaa.json")
            File.WriteAllText(definition, File.ReadAllText(definition).Replace("""action"": ""copy""", """action"": ""shred"""))
            kit.Engine.Start()

            Dim state As ScheduledOperationState = kit.Engine.Snapshot().Single()
            Assert.Equal(ScheduledHalt.InvalidDefinition, state.Operation.Halt)
            Assert.Empty(kit.Timers.Live)
            ' Neither an edit nor the switch rewrites it as "copy".
            Dim edit As ScheduledOperation = state.Operation
            edit.Name = "renamed"
            Assert.Contains(kit.Engine.Upsert(edit), Function(i) i.Code = ScheduledIssueCode.DefinitionUnreadable)
            kit.Engine.SetEnabled("aaa", False)
            Assert.Contains("""shred""", File.ReadAllText(definition))
            Assert.Equal(TriggerOutcome.Started, kit.Engine.RunNow("aaa"))
            Idle(kit)
            ' A run of it is refused, whole.
            Assert.Equal(ScheduledRunOutcome.Refused, kit.Engine.TryGet("aaa").Record.LastOutcome)
            Assert.Contains("""shred""", File.ReadAllText(definition))
        End Using
    End Sub
    <Fact>
    Public Sub A_disabled_operation_is_not_armed_but_still_runs_by_hand()
        Using kit As New Kit(At(13, 10, 0))
            Dim op As ScheduledOperation = kit.CopyOp("aaa")
            File.WriteAllText(Path.Combine(op.Source.Path, "a.txt"), "A")
            op.Enabled = False
            kit.Seed(op)
            kit.Engine.Start()
            Assert.Empty(kit.Timers.Live)
            Assert.Equal(TriggerOutcome.Started, kit.Engine.RunNow("aaa"))
            Idle(kit)
            Assert.Equal(ScheduledRunOutcome.Ok, kit.Engine.TryGet("aaa").Record.LastOutcome)

            kit.Engine.SetEnabled("aaa", True)
            Assert.Single(kit.Timers.Live)
        End Using
    End Sub

    <Fact>
    Public Sub The_last_failure_is_kept_for_the_tray_hint_and_cleared_by_a_success()
        Using kit As New Kit(At(13, 10, 0))
            Dim op As ScheduledOperation = kit.CopyOp("aaa", "Backup")
            Dim sourcePath As String = op.Source.Path
            Directory.Delete(sourcePath, True)
            kit.Seed(op)
            kit.Engine.Start()
            kit.Engine.RunNow("aaa")
            Idle(kit)
            Assert.NotNull(kit.Engine.LastFailure)
            Assert.Equal("Backup", kit.Engine.LastFailure.OperationName)
            Assert.Equal(ScheduledRunOutcome.Wall, kit.Engine.LastFailure.Outcome)
            Assert.StartsWith("source-unreachable|", kit.Engine.LastFailure.Detail, StringComparison.Ordinal)

            Directory.CreateDirectory(sourcePath)
            kit.Engine.RunNow("aaa")
            Idle(kit)
            Assert.Null(kit.Engine.LastFailure)
        End Using
    End Sub

End Class
