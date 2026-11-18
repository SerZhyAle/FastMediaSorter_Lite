Option Strict On
Option Explicit On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports FastMediaSorterCompanion.AppCommands
Imports Xunit

''' <summary>
''' Ticket L0345: <c>schedule add</c>, <c>schedule remove</c>, <c>schedule run</c> and <c>schedule history</c> -
''' parser rules, upsert idempotence, editor-parity refusals, non-destructive remove, single/all/wait execution
''' and journal projection.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ShareManagerScheduleCliTests

    Private Shared ReadOnly FixedZone As TimeZoneInfo = TimeZoneInfo.CreateCustomTimeZone("fms-sched-cli-test", TimeSpan.FromHours(1), "fms-sched-cli-test", "fms-sched-cli-test")

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
        Friend Property FileDoExe As String
        Friend Property FileDoSupportsGenerations As Boolean = True
        Friend Property FileDoSupportsDatedRetention As Boolean = True
        Friend Property FileDoSupportsSkipLinks As Boolean = True

        Friend Sub New(start As DateTime)
            Clock = New ManualClock(start)
            Dim folder As String = Tree.Dir("state")
            FileDoExe = Tree.Write("tools\filedo.exe", "stub")
            Store = New ScheduledStore(folder)
            Journal = New ScheduledJournal(folder)
            Services = New ScheduledServices With {
                .Clock = AddressOf Clock.Read,
                .TimeZone = Function() FixedZone,
                .Protection = ProtectedFolders.ForThisMachine(),
                .LocateFileDo = Function() FileDoExe,
                .FileDoHasBackupGenerations = Function(exe) FileDoSupportsGenerations,
                .FileDoHasBackupDatedRetention = Function(exe) FileDoSupportsDatedRetention,
                .FileDoHasBackupSkipLinks = Function(exe) FileDoSupportsSkipLinks,
                .BackupRunFolder = Function() Tree.Dir("backup-runs")}
            Engine = New ScheduledEngine(Store, Journal, Services, Timers) With {.CatchUpDelay = TimeSpan.Zero}
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Engine.Dispose()
            Hive.Dispose()
            Tree.Dispose()
        End Sub
    End Class

    Private Shared Function ParseOk(ParamArray words As String()) As ParsedCommand
        Dim parsed As ParsedCommand = Nothing
        Dim refusal As ParseRefusal = CommandLineParser.Parse(words, ShareManagerCommandTable.Build(), parsed)
        Assert.True(refusal Is Nothing, "refused: " & If(refusal Is Nothing, "", refusal.Detail & " " & refusal.Token))
        Return parsed
    End Function

    Private Shared Function ParseFail(ParamArray words As String()) As ParseRefusal
        Dim parsed As ParsedCommand = Nothing
        Dim refusal As ParseRefusal = CommandLineParser.Parse(words, ShareManagerCommandTable.Build(), parsed)
        Assert.NotNull(refusal)
        Return refusal
    End Function

    Private Shared Function RoundTrip(resp As CommandResponse) As CommandResponse
        Dim back As CommandResponse = Nothing
        Assert.True(CommandResponse.TryParse(resp.ToLine(), back))
        Return back
    End Function

    ' --- schedule add --------------------------------------------------------------------------------------

    <Fact>
    Public Sub Schedule_add_creates_daily_backup_at_1300_and_schedule_list_shows_it()
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Engine.Start()
            Dim src As String = kit.Tree.Dir("src", "ProjA")
            Dim dst As String = kit.Tree.Dir("dst", "Backups")

            Dim parsed As ParsedCommand = ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep", "14", "--name", "ProjA")
            Dim response As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(kit.Engine, parsed, kit.Tree.Root, "req-1")

            Assert.Equal(CommandStatus.Done, response.Status)
            Assert.Equal(CommandExit.Done, response.ExitCode)
            Assert.NotNull(response.Data)
            Dim id As String = response.Data.GetString("id")
            Assert.False(String.IsNullOrEmpty(id))
            Assert.True(response.Data.GetBool("created", False))
            Assert.Equal("2026-11-17T13:00:00+01:00", response.Data.GetString("nextRun"))

            Dim op As ScheduledOperation = kit.Engine.TryGet(id)
            Assert.NotNull(op)
            Assert.True(op.Enabled)
            Assert.Equal(ScheduledAction.Backup, op.Action)
            Assert.Equal(13 * 60, op.AnchorMinutes)
            Assert.Equal(ScheduleMath.MinutesPerDay, op.IntervalMinutes)
            Assert.Equal(14, op.BackupKeepCount)
            Assert.Equal("ProjA", op.Name)

            Dim listed As JsonObject = ShareManagerObserve.ScheduleListData(kit.Store.LoadAll(), kit.Clock.Now, FixedZone)
            Dim rows As List(Of Object) = DirectCast(listed.First(Function(p) p.Key = "operations").Value, List(Of Object))
            Assert.Single(rows)
            Dim row As JsonObject = DirectCast(rows(0), JsonObject)
            Assert.Equal(id, row.GetString("id"))
            Assert.Equal("backup", row.GetString("action"))
            Assert.Equal("2026-11-17T13:00:00+01:00", row.GetString("nextRun"))
        End Using
    End Sub

    <Fact>
    Public Sub Schedule_add_twice_with_same_action_source_and_destination_updates_existing_id()
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Engine.Start()
            Dim src As String = kit.Tree.Dir("src", "ProjA")
            Dim dst As String = kit.Tree.Dir("dst", "Backups")

            Dim first As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00"),
                kit.Tree.Root, "req-1")
            Dim firstId As String = first.Data.GetString("id")
            Assert.True(first.Data.GetBool("created", False))

            ' Trailing slash and different casing still match the same identity.
            Dim second As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src.ToUpperInvariant() & "\", "--destination", dst & "\", "--daily", "14:30", "--keep", "30"),
                kit.Tree.Root, "req-2")
            Assert.Equal(CommandExit.Done, second.ExitCode)
            Assert.Equal(firstId, second.Data.GetString("id"))
            Assert.False(second.Data.GetBool("created", True))
            Assert.Single(kit.Engine.Snapshot())
            Assert.Equal(14 * 60 + 30, kit.Engine.TryGet(firstId).AnchorMinutes)
            Assert.Equal(30, kit.Engine.TryGet(firstId).BackupKeepCount)
        End Using
    End Sub

    <Fact>
    Public Sub Schedule_add_refuses_editor_invalid_definitions_and_bad_filedo_with_exit_10()
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Engine.Start()
            Dim src As String = kit.Tree.Dir("src", "ProjA")
            Dim inside As String = kit.Tree.Dir("src", "ProjA", "sub")
            Dim dst As String = kit.Tree.Dir("dst", "Backups")

            ' Missing required options -> parser exit 10
            Assert.Equal(CommandReasons.BadArgument, ParseFail("schedule", "add", "--action", "backup").Reason)

            ' Unsupported action -> parser exit 10
            Assert.Equal(CommandReasons.BadArgument, ParseFail("schedule", "add", "--action", "copy", "--source", src, "--destination", dst, "--daily", "13:00").Reason)

            ' Destination inside source -> editor refusal exit 10
            Dim insideResp As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", inside, "--daily", "13:00"),
                kit.Tree.Root, "req-inside")
            Assert.Equal(CommandExit.Usage, insideResp.ExitCode)
            Assert.Equal(CommandReasons.BadArgument, insideResp.Reason)
            Assert.Empty(kit.Engine.Snapshot())

            ' Keep count out of 1..999 -> exit 10
            Dim keepZero As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep", "0"),
                kit.Tree.Root, "req-keep0")
            Assert.Equal(CommandExit.Usage, keepZero.ExitCode)
            Dim keepOver As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep", "1000"),
                kit.Tree.Root, "req-keep1000")
            Assert.Equal(CommandExit.Usage, keepOver.ExitCode)

            ' Bad --daily time -> exit 10
            Dim badTime As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "25:00"),
                kit.Tree.Root, "req-time")
            Assert.Equal(CommandExit.Usage, badTime.ExitCode)

            ' FileDO without numbered generation support -> exit 10, nothing saved
            kit.FileDoSupportsGenerations = False
            Dim noGen As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00"),
                kit.Tree.Root, "req-nogen")
            Assert.Equal(CommandExit.Usage, noGen.ExitCode)
            Assert.Contains("FileDO", noGen.Message)
            Assert.Empty(kit.Engine.Snapshot())

            ' Missing FileDO -> exit 10, nothing saved
            kit.FileDoExe = ""
            Dim noExe As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00"),
                kit.Tree.Root, "req-noexe")
            Assert.Equal(CommandExit.Usage, noExe.ExitCode)
            Assert.Empty(kit.Engine.Snapshot())
        End Using
    End Sub

    ' --- schedule remove -----------------------------------------------------------------------------------

    <Fact>
    Public Sub Schedule_remove_deletes_definition_only_and_keeps_backup_files()
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Engine.Start()
            Dim src As String = kit.Tree.Dir("src", "ProjA")
            Dim dst As String = kit.Tree.Dir("dst", "Backups")
            Dim backupZip As String = kit.Tree.Write("dst\Backups\ProjA_001.zip", "archive-bytes")

            Dim added As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--name", "ProjA"),
                kit.Tree.Root, "req-add")
            Dim id As String = added.Data.GetString("id")

            Dim removed As CommandResponse = ShareManagerScheduleCli.ScheduleRemove(kit.Engine, ParseOk("schedule", "remove", "ProjA"), "req-rm")
            Assert.Equal(CommandExit.Done, removed.ExitCode)
            Assert.Equal(id, removed.Data.GetString("id"))
            Assert.True(removed.Data.GetBool("removed", False))
            Assert.Empty(kit.Engine.Snapshot())
            Assert.Null(kit.Store.TryLoad(id))
            Assert.True(File.Exists(backupZip))

            Dim second As CommandResponse = ShareManagerScheduleCli.ScheduleRemove(kit.Engine, ParseOk("schedule", "remove", id), "req-rm2")
            Assert.Equal(CommandExit.NotReady, second.ExitCode)
            Assert.Equal(CommandReasons.TargetMissing, second.Reason)
        End Using
    End Sub

    <Fact>
    Public Sub Schedule_remove_refuses_ambiguous_name_with_exit_10()
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Engine.Start()
            Dim dst As String = kit.Tree.Dir("dst", "Backups")
            ShareManagerScheduleCli.ScheduleAdd(kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", kit.Tree.Dir("src", "A"), "--destination", dst, "--daily", "13:00", "--name", "Same"), kit.Tree.Root, "1")
            ShareManagerScheduleCli.ScheduleAdd(kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", kit.Tree.Dir("src", "B"), "--destination", dst, "--daily", "13:00", "--name", "Same"), kit.Tree.Root, "2")

            Dim resp As CommandResponse = ShareManagerScheduleCli.ScheduleRemove(kit.Engine, ParseOk("schedule", "remove", "Same"), "req-amb")
            Assert.Equal(CommandExit.Usage, resp.ExitCode)
            Assert.Equal(CommandReasons.BadArgument, resp.Reason)
            Assert.Equal(2, kit.Engine.Snapshot().Count)
        End Using
    End Sub

    ' --- schedule run --------------------------------------------------------------------------------------

    <Fact>
    Public Sub Schedule_run_requires_target_or_all_and_never_both()
        Assert.Equal(CommandReasons.BadArgument, ParseFail("schedule", "run").Reason)
        Assert.Equal(CommandReasons.BadArgument, ParseFail("schedule", "run", "op1", "--all").Reason)
    End Sub

    <Fact>
    Public Async Function Schedule_run_queues_at_once_or_waits_for_outcome() As Task
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Services.LaunchFill = Function(launch, ct) As FillLaunchOutcome
                                          Dim eventsPath As String = launch.Arguments(launch.Arguments.IndexOf("--events") + 1)
                                          File.WriteAllText(eventsPath, "{""event"":""result"",""verdict"":""Passed"",""numbers"":{""generation"":3,""retiredGeneration"":1,""keepCount"":7,""files"":12,""bytes"":4096}}" & vbLf)
                                          Return New FillLaunchOutcome With {.Started = True, .ExitCode = 0}
                                      End Function
            kit.Engine.Start()
            Dim src As String = kit.Tree.Dir("src", "ProjA")
            Dim dst As String = kit.Tree.Dir("dst", "Backups")
            ShareManagerScheduleCli.ScheduleAdd(kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--name", "ProjA"), kit.Tree.Root, "add")

            ' Wait mode returns the run's outcome (0 on Passed)
            Dim waited As CommandResponse = RoundTrip(Await ShareManagerScheduleCli.ScheduleRunAsync(kit.Engine, ParseOk("schedule", "run", "ProjA", "--wait"), "run-w"))
            Assert.Equal(CommandStatus.Done, waited.Status)
            Assert.Equal(CommandExit.Done, waited.ExitCode)
            Assert.True(waited.Data.GetBool("completed", False))
            Assert.Equal(1, waited.Data.GetInt("succeeded", 0))
            Assert.Equal(0, waited.Data.GetInt("failed", -1))
            Assert.Equal("ok", waited.Data.GetString("outcome"))

            ' Non-wait mode queues and answers accepted immediately
            Dim queued As CommandResponse = RoundTrip(Await ShareManagerScheduleCli.ScheduleRunAsync(kit.Engine, ParseOk("schedule", "run", "--all"), "run-q"))
            Assert.Equal(CommandStatus.Accepted, queued.Status)
            Assert.Equal(CommandExit.Done, queued.ExitCode)
            Assert.Equal(1, queued.Data.GetInt("queued", 0))
            Assert.False(queued.Data.GetBool("completed", True))
            Assert.Equal("queued", queued.Data.GetString("outcome"))
            Await kit.Engine.WhenIdleAsync()
        End Using
    End Function

    <Fact>
    Public Async Function Schedule_run_wait_propagates_failure_exit_code_and_handles_paused_or_missing() As Task
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Services.LaunchFill = Function(launch, ct) As FillLaunchOutcome
                                          Dim eventsPath As String = launch.Arguments(launch.Arguments.IndexOf("--events") + 1)
                                          File.WriteAllText(eventsPath, "{""event"":""result"",""verdict"":""Not proven"",""reasons"":[""disk-full""]}" & vbLf)
                                          Return New FillLaunchOutcome With {.Started = True, .ExitCode = 13}
                                      End Function
            kit.Engine.Start()
            Dim src As String = kit.Tree.Dir("src", "ProjA")
            Dim dst As String = kit.Tree.Dir("dst", "Backups")
            ShareManagerScheduleCli.ScheduleAdd(kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--name", "ProjA"), kit.Tree.Root, "add")

            Dim fullResp As CommandResponse = RoundTrip(Await ShareManagerScheduleCli.ScheduleRunAsync(kit.Engine, ParseOk("schedule", "run", "--all", "--wait"), "run-fail"))
            Assert.Equal(CommandExit.Resources, fullResp.ExitCode)
            Assert.Equal(CommandReasons.NoSpace, fullResp.Reason)
            Assert.Equal(1, fullResp.Data.GetInt("failed", 0))

            kit.Engine.Paused = True
            Dim pausedResp As CommandResponse = Await ShareManagerScheduleCli.ScheduleRunAsync(kit.Engine, ParseOk("schedule", "run", "--all"), "run-paused")
            Assert.Equal(CommandExit.NotReady, pausedResp.ExitCode)
            Assert.Equal(CommandReasons.Busy, pausedResp.Reason)

            Dim missingResp As CommandResponse = Await ShareManagerScheduleCli.ScheduleRunAsync(kit.Engine, ParseOk("schedule", "run", "NoSuchOp"), "run-missing")
            Assert.Equal(CommandExit.NotReady, missingResp.ExitCode)
            Assert.Equal(CommandReasons.TargetMissing, missingResp.Reason)
        End Using
    End Function

    ' --- schedule history ----------------------------------------------------------------------------------

    <Fact>
    Public Sub Schedule_history_prints_runs_generations_and_filters_by_operation_and_last()
        Using kit As New Kit(New DateTime(2026, 11, 17, 13, 0, 0))
            kit.Journal.Append(New JournalEntry With {
                .Timestamp = New DateTime(2026, 11, 16, 13, 0, 0),
                .Kind = "run", .Code = "ok", .OperationId = "id-a", .OperationName = "ProjA",
                .Trigger = "schedule", .Count = 10, .Bytes = 2048, .DurationMs = 500,
                .Verdict = "backup:gen=8;retired=1;keep=7;files=10;bytes=2048;exit=0", .Detail = ""})
            kit.Journal.Append(New JournalEntry With {
                .Timestamp = New DateTime(2026, 11, 17, 13, 0, 0),
                .Kind = "run", .Code = "wall", .OperationId = "id-b", .OperationName = "ProjB",
                .Trigger = "runNow", .Count = 0, .Bytes = 0, .DurationMs = 20,
                .Verdict = "", .Detail = "locked|P:\ProjB"})

            Dim allResp As CommandResponse = RoundTrip(ShareManagerScheduleCli.ScheduleHistoryResponse(
                kit.Journal, kit.Store, ParseOk("schedule", "history", "--last", "10"), FixedZone, "h-1"))
            Assert.Equal(CommandExit.Done, allResp.ExitCode)
            Dim rows As List(Of Object) = DirectCast(allResp.Data.First(Function(p) p.Key = "entries").Value, List(Of Object))
            Assert.Equal(2, rows.Count)
            ' Newest first
            Dim firstRow As JsonObject = DirectCast(rows(0), JsonObject)
            Assert.Equal("2026-11-17T13:00:00+01:00", firstRow.GetString("time"))
            Assert.Equal("ProjB", firstRow.GetString("operation"))
            Assert.Equal("runNow", firstRow.GetString("trigger"))
            Assert.Equal("wall", firstRow.GetString("outcome"))
            Assert.Equal("locked|P:\ProjB", firstRow.GetString("detail"))

            Dim secondRow As JsonObject = DirectCast(rows(1), JsonObject)
            Assert.Equal("ProjA", secondRow.GetString("operation"))
            Assert.Equal(8, secondRow.GetInt("generation", -1))
            Assert.Equal(1, secondRow.GetInt("retiredGeneration", -1))
            Assert.Contains("gen=8 retired=1", allResp.Message)

            Dim filtered As CommandResponse = ShareManagerScheduleCli.ScheduleHistoryResponse(
                kit.Journal, kit.Store, ParseOk("schedule", "history", "--operation", "ProjA", "--last", "1"), FixedZone, "h-2")
            Dim fRows As List(Of Object) = DirectCast(filtered.Data.First(Function(p) p.Key = "entries").Value, List(Of Object))
            Assert.Single(fRows)
            Assert.Equal("ProjA", DirectCast(fRows(0), JsonObject).GetString("operation"))

            Dim badLast As CommandResponse = ShareManagerScheduleCli.ScheduleHistoryResponse(
                kit.Journal, kit.Store, ParseOk("schedule", "history", "--last", "0"), FixedZone, "h-3")
            Assert.Equal(CommandExit.Usage, badLast.ExitCode)
            Assert.Equal(CommandReasons.BadArgument, badLast.Reason)
        End Using
    End Sub

    <Fact>
    Public Sub Schedule_add_dated_retention_and_skip_links_updates_store_list_and_validates_conflicts()
        Using kit As New Kit(New DateTime(2026, 11, 17, 10, 0, 0))
            kit.Engine.Start()
            Dim src As String = kit.Tree.Dir("src", "FileDo")
            Dim dst As String = kit.Tree.Dir("dst", "Backups")

            ' --keep and --keep-days together, or --keep-monthly without --keep-days -> exit 10
            Dim bothResp As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep", "7", "--keep-days", "7"),
                kit.Tree.Root, "req-both")
            Assert.Equal(CommandExit.Usage, bothResp.ExitCode)

            Dim monthlyOnly As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep-monthly"),
                kit.Tree.Root, "req-monthly")
            Assert.Equal(CommandExit.Usage, monthlyOnly.ExitCode)

            ' FileDO lacking dated retention or skip-links -> exit 10 without saving
            kit.FileDoSupportsDatedRetention = False
            Dim noDated As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep-days", "7", "--keep-monthly"),
                kit.Tree.Root, "req-nodated")
            Assert.Equal(CommandExit.Usage, noDated.ExitCode)
            Assert.Empty(kit.Engine.Snapshot())
            kit.FileDoSupportsDatedRetention = True

            kit.FileDoSupportsSkipLinks = False
            Dim noSkip As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep-days", "7", "--skip-links"),
                kit.Tree.Root, "req-noskip")
            Assert.Equal(CommandExit.Usage, noSkip.ExitCode)
            kit.FileDoSupportsSkipLinks = True

            Dim okResp As CommandResponse = ShareManagerScheduleCli.ScheduleAdd(
                kit.Engine, ParseOk("schedule", "add", "--action", "backup", "--source", src, "--destination", dst, "--daily", "13:00", "--keep-days", "7", "--keep-monthly", "--skip-links", "--name", "FileDo"),
                kit.Tree.Root, "req-ok")
            Assert.Equal(CommandExit.Done, okResp.ExitCode)
            Dim op As ScheduledOperation = kit.Engine.TryGet(okResp.Data.GetString("id"))
            Assert.Equal(BackupRetentionMode.Dated, op.BackupRetention)
            Assert.Equal(7, op.BackupKeepDays)
            Assert.True(op.BackupKeepMonthly)
            Assert.True(op.BackupSkipLinks)

            Dim listed As JsonObject = ShareManagerObserve.ScheduleListData(kit.Store.LoadAll(), kit.Clock.Now, FixedZone)
            Dim rows As List(Of Object) = DirectCast(listed.First(Function(p) p.Key = "operations").Value, List(Of Object))
            Dim row As JsonObject = DirectCast(rows(0), JsonObject)
            Assert.Equal("keep-days+keep-monthly", row.GetString("retention"))
            Assert.Equal(7, row.GetInt("keepDays", 0))
            Assert.True(row.GetBool("keepMonthly", False))
            Assert.True(row.GetBool("skipLinks", False))
        End Using
    End Sub

    <Fact>
    Public Sub Schedule_history_includes_dated_retention_numbers_and_skipped_links()
        Using kit As New Kit(New DateTime(2026, 11, 17, 13, 0, 0))
            kit.Journal.Append(New JournalEntry With {
                .Timestamp = New DateTime(2026, 11, 17, 13, 0, 0),
                .Kind = "run", .Code = "ok", .OperationId = "id-d", .OperationName = "FileDo",
                .Trigger = "schedule", .Count = 42, .Bytes = 1048576, .DurationMs = 900,
                .Verdict = "backup:mode=dated;retiredCount=2;keptCount=10;keepDays=7;monthlyKept=3;files=42;bytes=1048576;skippedLinks=1;exit=0",
                .Detail = ""})
            Dim resp As CommandResponse = RoundTrip(ShareManagerScheduleCli.ScheduleHistoryResponse(
                kit.Journal, kit.Store, ParseOk("schedule", "history", "--last", "5"), FixedZone, "hd-1"))
            Assert.Equal(CommandExit.Done, resp.ExitCode)
            Dim rows As List(Of Object) = DirectCast(resp.Data.First(Function(p) p.Key = "entries").Value, List(Of Object))
            Dim row As JsonObject = DirectCast(rows(0), JsonObject)
            Assert.Equal(2, row.GetInt("retiredCount", -1))
            Assert.Equal(10, row.GetInt("keptCount", -1))
            Assert.Equal(7, row.GetInt("keepDays", -1))
            Assert.Equal(3, row.GetInt("monthlyKept", -1))
            Assert.Equal(1, row.GetInt("skippedLinks", -1))
            Assert.Contains("keepDays=7 kept=10 retired=2 monthly=3", resp.Message)
            Assert.Contains("skippedLinks=1", resp.Message)
        End Using
    End Sub

End Class
