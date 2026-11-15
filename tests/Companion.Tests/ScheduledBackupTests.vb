Option Strict On

Imports System.IO
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Backup of a folder (ticket L0298): the definition and its JSON, the seam an older build parks it at, the per-operation
''' DPAPI password that never reaches the JSON or the export, the `numbers` of CLI-EVENT-STREAM 0.17's `result`, and the wall a
''' run stops at while FileDO has no generation set.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledBackupTests

    Private Shared Function BackupOp(Optional id As String = "0123456789abcdef0123456789abcdef") As ScheduledOperation
        Dim o As New ScheduledOperation With {.Id = id, .Name = "backup", .Action = ScheduledAction.Backup}
        o.Source.Path = "D:\Photos"
        o.Destination.Path = "E:\Backups"
        Return o
    End Function

    Private Shared Function TempFolder() As String
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-l0298-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Return folder
    End Function

    ' --- the definition --------------------------------------------------------------------------------

    <Fact>
    Public Sub Json_keeps_the_keep_count_and_the_seal_and_never_a_password()
        Dim op As ScheduledOperation = BackupOp()
        op.BackupKeepCount = 30
        op.BackupSealed = True
        Dim text As String = ScheduledJson.Serialize(op)
        Assert.Contains("""backup""", text)
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase)
        Dim read As ScheduledOperation = ScheduledJson.Deserialize(text)
        Assert.Equal(ScheduledAction.Backup, read.Action)
        Assert.Equal(30, read.BackupKeepCount)
        Assert.True(read.BackupSealed)
        Assert.Equal(ScheduledHalt.None, read.Halt)
    End Sub

    <Fact>
    Public Sub Json_of_another_action_carries_no_backup_option()
        Dim copy As New ScheduledOperation With {.Id = "c1", .Action = ScheduledAction.Copy, .BackupSealed = True}
        Dim text As String = ScheduledJson.Serialize(copy)
        Assert.DoesNotContain("backupKeepCount", text)
        Assert.DoesNotContain("backupSealed", text)
        Dim read As ScheduledOperation = ScheduledJson.Deserialize("{""id"":""c2"",""action"":""copy"",""backupSealed"":true,""backupKeepCount"":5}")
        Assert.False(read.BackupSealed)
        Assert.Equal(ScheduledOperation.DefaultBackupKeepCount, read.BackupKeepCount)
    End Sub

    <Fact>
    Public Sub An_action_this_build_does_not_know_still_parks_the_definition()
        Dim read As ScheduledOperation = ScheduledJson.Deserialize("{""id"":""n1"",""action"":""backup_v2""}")
        Assert.Equal(ScheduledHalt.InvalidDefinition, read.Halt)
    End Sub

    <Fact>
    Public Sub Keep_count_is_clamped_into_one_to_999()
        Assert.Equal(1, ScheduledValidation.NormalizeKeepCount(0))
        Assert.Equal(999, ScheduledValidation.NormalizeKeepCount(5000))
        Dim read As ScheduledOperation = ScheduledJson.Deserialize("{""id"":""k1"",""action"":""backup"",""backupKeepCount"":-3}")
        Assert.Equal(1, read.BackupKeepCount)
    End Sub

    <Fact>
    Public Sub Normalize_takes_the_whole_folder_and_clears_the_selection()
        Dim op As ScheduledOperation = BackupOp()
        op.Source.Recursive = False
        op.Conditions.NameMask = "*.jpg"
        op.Conflict = ScheduledConflict.Overwrite
        op.DeleteOriginalsAfterVerify = True
        ScheduledValidation.Normalize(op)
        Assert.True(op.Source.Recursive)
        Assert.Equal("", op.Conditions.NameMask)
        Assert.Equal(ScheduledConflict.Skip, op.Conflict)
        Assert.False(op.DeleteOriginalsAfterVerify)
        Assert.False(op.IsDestructive)
        Assert.True(op.NeedsDestination)
        Assert.False(op.SelectsFiles)
    End Sub

    <Fact>
    Public Sub Stem_is_one_file_name_part_and_differs_per_operation()
        Dim a As ScheduledOperation = BackupOp("aaaaaaaa11112222")
        Dim b As ScheduledOperation = BackupOp("bbbbbbbb11112222")
        a.Source.Path = "D:\My Photos_2026"
        b.Source.Path = "D:\My Photos_2026"
        Assert.Equal("My-Photos-2026-aaaaaaaa", a.BackupStem)
        Assert.NotEqual(a.BackupStem, b.BackupStem)
        Assert.DoesNotContain("_", a.BackupStem)
        Assert.Equal(-1, a.BackupStem.IndexOfAny(Path.GetInvalidFileNameChars()))
    End Sub

    <Fact>
    Public Sub Validate_refuses_resources_a_set_inside_its_source_and_a_seal_without_a_password()
        Dim resource As ScheduledOperation = BackupOp()
        resource.Destination = New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = "r1"}
        Assert.Contains(ScheduledValidation.Validate(resource, Nothing), Function(i) i.Code = ScheduledIssueCode.BackupNeedsPaths)

        Dim inside As ScheduledOperation = BackupOp()
        inside.Destination.Path = "D:\Photos\backups"
        Assert.Contains(ScheduledValidation.Validate(inside, Nothing), Function(i) i.Code = ScheduledIssueCode.DestinationInsideSource)

        Dim sealedOp As ScheduledOperation = BackupOp()
        sealedOp.BackupSealed = True
        Assert.Contains(ScheduledValidation.Validate(sealedOp, Nothing, Nothing, Function(id) False), Function(i) i.Code = ScheduledIssueCode.BackupPasswordMissing)
        Assert.Empty(ScheduledValidation.Validate(sealedOp, Nothing, Nothing, Function(id) True))
    End Sub

    ' --- the password ----------------------------------------------------------------------------------

    <Fact>
    Public Sub Password_round_trips_under_DPAPI_and_goes_with_its_operation()
        Dim folder As String = TempFolder()
        Try
            Dim store As New ScheduledStore(folder)
            Dim op As ScheduledOperation = BackupOp()
            op.BackupSealed = True
            store.SaveDefinition(op)
            Assert.True(store.BackupCredentials.Write(op.Id, "correct horse battery"))
            Assert.Equal("correct horse battery", store.BackupCredentials.Read(op.Id))
            Dim blob As String = Path.Combine(folder, ScheduledBackupCredentialStore.FolderName, op.Id & ".dpapi")
            Assert.DoesNotContain("correct horse", File.ReadAllText(blob))
            Assert.DoesNotContain("correct horse", File.ReadAllText(Path.Combine(folder, op.Id & ".json")))
            Assert.Single(store.LoadAll())
            store.Delete(op.Id)
            Assert.False(store.BackupCredentials.Has(op.Id))
        Finally
            Directory.Delete(folder, recursive:=True)
        End Try
    End Sub

    <Fact>
    Public Sub Trivial_passwords_are_refused()
        Assert.True(ScheduledBackupCredentialStore.IsTrivial(""))
        Assert.True(ScheduledBackupCredentialStore.IsTrivial("1234567"))
        Assert.True(ScheduledBackupCredentialStore.IsTrivial("aaaaaaaaaa"))
        Assert.False(ScheduledBackupCredentialStore.IsTrivial("12345678"))
    End Sub

    <Fact>
    Public Sub Export_carries_no_password_and_import_parks_a_sealed_backup_without_one()
        Dim source As String = TempFolder()
        Dim target As String = TempFolder()
        Try
            Dim from As New ScheduledStore(source)
            Dim op As ScheduledOperation = BackupOp()
            op.BackupSealed = True
            from.SaveDefinition(op)
            Assert.True(from.BackupCredentials.Write(op.Id, "correct horse battery"))
            Dim exported As String = ShareSettingsTransfer.ExportToJson(from)
            Assert.DoesNotContain("correct horse", exported)
            Assert.DoesNotContain("password", exported, StringComparison.OrdinalIgnoreCase)

            Dim [to] As New ScheduledStore(target)
            Dim result As ShareSettingsImportResult = ShareSettingsTransfer.ImportFromJson(exported, [to], Function(id) Nothing)
            Assert.Equal(1, result.Imported)
            Dim parked As ScheduledOperation = [to].TryLoad(op.Id)
            Assert.Equal(ScheduledHalt.Wall, parked.Halt)
            Assert.Equal(ShareSettingsTransfer.BackupNoPasswordCode, parked.HaltDetail)
            Assert.True(parked.BackupSealed)
        Finally
            Directory.Delete(source, recursive:=True)
            Directory.Delete(target, recursive:=True)
        End Try
    End Sub

    ' --- the event stream (CLI-EVENT-STREAM 0.17, rule 16) -------------------------------------------------

    <Fact>
    Public Sub Result_numbers_are_read_and_an_absent_key_is_not_reported()
        Dim text As String =
            "{""schemaVersion"":1,""kind"":""run"",""data"":{}}" & vbLf &
            "{""schemaVersion"":1,""kind"":""result"",""data"":{""verdict"":""Done"",""numbers"":{""files"":12,""generation"":3,""retiredGeneration"":0,""keepCount"":7,""note"":""x""}}}" & vbLf
        Dim events As FillEvents = FillEvents.Parse(text)
        Assert.Equal(3L, events.Number("generation"))
        Assert.Equal(0L, events.Number("retiredGeneration"))
        Assert.Equal(7L, events.Number("keepCount"))
        Assert.Null(events.Number("archiveBytes"))
        Assert.Null(events.Number("note"))
        Assert.Equal(FillJudgement.Passed, events.Judge(0, False))
    End Sub

    ' --- the run ---------------------------------------------------------------------------------------

    Private Shared Function Services(present As Boolean, password As String) As ScheduledServices
        Return New ScheduledServices With {
            .Clock = Function() New DateTime(2026, 11, 12, 3, 0, 0),
            .LocateFileDo = Function() If(present, "C:\stub\filedo.exe", ""),
            .BackupPassword = Function(id As String) password,
            .LaunchFill = Function(launch As FillLaunch, ct As CancellationToken) As FillLaunchOutcome
                              Throw New InvalidOperationException("a backup must not start FileDO before its options are published")
                          End Function}
    End Function

    <Fact>
    Public Sub A_run_without_FileDO_or_without_its_generation_set_walls_and_starts_nothing()
        Dim source As String = TempFolder()
        Dim destination As String = TempFolder()
        Try
            Dim op As ScheduledOperation = BackupOp()
            op.Source.Path = source
            op.Destination.Path = destination
            Dim missing As ScheduledRunResult = New ScheduledExecutor(Services(False, "")).Run(op, CancellationToken.None)
            Assert.Equal(ScheduledRunOutcome.Wall, missing.Outcome)
            Assert.Equal("filedo-missing", missing.Detail)

            Dim unsupported As ScheduledRunResult = New ScheduledExecutor(Services(True, "")).Run(op, CancellationToken.None)
            Assert.Equal(ScheduledRunOutcome.Wall, unsupported.Outcome)
            Assert.Equal("backup-filedo-unsupported", unsupported.Detail)
            Assert.Empty(Directory.GetFiles(destination))
        Finally
            Directory.Delete(source, recursive:=True)
            Directory.Delete(destination, recursive:=True)
        End Try
    End Sub

    <Fact>
    Public Sub A_sealed_run_without_a_password_waits_at_the_wall()
        Dim op As ScheduledOperation = BackupOp()
        op.BackupSealed = True
        Dim result As ScheduledRunResult = New ScheduledExecutor(Services(True, "")).Run(op, CancellationToken.None)
        Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
        Assert.Equal(ShareSettingsTransfer.BackupNoPasswordCode, result.Detail)
    End Sub

    ' --- the FileDO run (P4.1, FileDO SP-0208 spelling) --------------------------------------------------------

    <Fact>
    Public Sub Arguments_name_the_set_and_never_the_password()
        Dim args As List(Of String) = BackupCommandLine.Arguments("D:\Photos", "E:\Bak", 7, "Photos-01234567", True, "ev.jsonl", "stop")
        Assert.Equal(New String() {"backup", "D:\Photos", "to", "E:\Bak", "gen", "7", "stem", "Photos-01234567", "sec",
                                   "pe:" & BackupCommandLine.PasswordVariable, "y", "--no-ui", "--events", "ev.jsonl", "--stop-file", "stop"},
                     args.ToArray())
        Dim plain As List(Of String) = BackupCommandLine.Arguments("D:\Photos", "E:\Bak", 3, "s", False, "e", "s")
        Assert.DoesNotContain("sec", plain)
        Assert.DoesNotContain(plain, Function(a) a.StartsWith("pe:", StringComparison.Ordinal))
        Assert.Equal("Photos-01234567_003.fd-sec", BackupCommandLine.GenerationName("Photos-01234567", 3, True))
        Assert.Equal("s_120.zip", BackupCommandLine.GenerationName("s", 120, False))
    End Sub

    <Fact>
    Public Sub Help_probe_needs_both_gen_and_stem()
        Assert.True(FileDoBackupProbe.HelpNamesGenerations("filedo.exe backup D:\Photos to E:\Bak gen 7 stem Docs sec"))
        Assert.False(FileDoBackupProbe.HelpNamesGenerations("filedo.exe backup D:\Photos to E:\Bak level 9 sec"))
        Assert.False(FileDoBackupProbe.HelpNamesGenerations(""))
        Assert.False(FileDoBackupProbe.HasGenerations("C:\no-such-folder\filedo.exe"))
    End Sub

    Private Shared Function Ended(verdict As String, Optional reason As String = "") As FillEvents
        Dim text As String = ""
        If reason.Length > 0 Then
            text &= "{""schemaVersion"":1,""kind"":""finding"",""data"":{""type"":""error"",""message"":""m"",""details"":{""reason"":""" & reason & """}}}" & vbLf
        End If
        text &= "{""schemaVersion"":1,""kind"":""result"",""data"":{""verdict"":""" & verdict & """}}" & vbLf
        Return FillEvents.Parse(text)
    End Function

    <Fact>
    Public Sub Endings_follow_the_exit_classes_of_item_F()
        Assert.Equal(BackupEnding.Done, BackupCommandLine.Classify(Ended("Done"), 0, False))
        Assert.Equal(BackupEnding.NotProven, BackupCommandLine.Classify(Ended("Done"), 2, False))
        Assert.Equal(BackupEnding.Failed, BackupCommandLine.Classify(Ended("Failed"), 1, True))
        Assert.Equal(BackupEnding.DefinitionRefused, BackupCommandLine.Classify(Ended("Not proven", "backup-source-refused"), 10, False))
        Assert.Equal(BackupEnding.Obstacle, BackupCommandLine.Classify(Ended("Not proven"), 11, False))
        Assert.Equal(BackupEnding.Obstacle, BackupCommandLine.Classify(Ended("Not proven", "volume-full"), 13, False))
        Assert.Equal(BackupEnding.Obstacle, BackupCommandLine.Classify(Ended("Not proven"), 15, False))
        Assert.Equal(BackupEnding.Obstacle, BackupCommandLine.Classify(Ended("Not proven", "file-in-use"), 2, False))
        Assert.Equal(BackupEnding.NotProven, BackupCommandLine.Classify(Ended("Not proven"), 2, False))
        Assert.Equal(BackupEnding.NotProven, BackupCommandLine.Classify(Ended("Not proven"), 12, False))
        Assert.Equal(BackupEnding.Stopped, BackupCommandLine.Classify(Ended("Stopped"), 16, True))
        Assert.Equal(BackupEnding.NotProven, BackupCommandLine.Classify(Ended("Stopped"), 16, False))
        Assert.Equal(BackupEnding.NotProven, BackupCommandLine.Classify(FillEvents.Parse(""), 0, False))
        Dim refused As FillEvents = Ended("Not proven", "backup-destination-refused")
        Assert.Equal("backup-destination-refused|E:\Bak", BackupCommandLine.WallDetail(BackupEnding.DefinitionRefused, refused, 10, "D:\P", "E:\Bak", True))
        Assert.Equal("disk-full|E:\Bak", BackupCommandLine.WallDetail(BackupEnding.Obstacle, Ended("Not proven"), 13, "D:\P", "E:\Bak", True))
        Assert.Equal("source-unreachable|D:\P", BackupCommandLine.WallDetail(BackupEnding.Obstacle, Ended("Not proven"), 11, "D:\P", "E:\Bak", False))
    End Sub

    Private Shared Function Launching(runFolder As String, password As String, events As String, exitCode As Integer,
                                      seen As List(Of FillLaunch)) As ScheduledServices
        Return New ScheduledServices With {
            .Clock = Function() New DateTime(2026, 11, 13, 3, 0, 0),
            .LocateFileDo = Function() "C:\stub\filedo.exe",
            .FileDoHasBackupGenerations = Function(exe As String) True,
            .BackupPassword = Function(id As String) password,
            .BackupRunFolder = Function() runFolder,
            .LaunchFill = Function(launch As FillLaunch, ct As CancellationToken) As FillLaunchOutcome
                              seen.Add(launch)
                              File.WriteAllText(launch.Arguments(launch.Arguments.IndexOf("--events") + 1), events)
                              Return New FillLaunchOutcome With {.Started = True, .ExitCode = exitCode}
                          End Function}
    End Function

    <Fact>
    Public Sub A_done_run_journals_the_generation_and_the_retired_slot_and_passes_the_password_by_environment()
        Dim source As String = TempFolder()
        Dim destination As String = TempFolder()
        Dim runs As String = TempFolder()
        Try
            Dim op As ScheduledOperation = BackupOp()
            op.Source.Path = source
            op.Destination.Path = destination
            op.BackupSealed = True
            op.BackupKeepCount = 3
            Dim seen As New List(Of FillLaunch)()
            Dim items As New List(Of ItemJournalEntry)()
            Dim events As String =
                "{""schemaVersion"":1,""kind"":""result"",""data"":{""verdict"":""Done"",""numbers"":{""files"":4,""bytes"":100,""generation"":2,""retiredGeneration"":2,""keepCount"":3}}}" & vbLf
            Dim services As ScheduledServices = Launching(runs, "correct horse battery", events, 0, seen)
            services.ItemJournal = Sub(e As ItemJournalEntry) items.Add(e)
            Dim result As ScheduledRunResult = New ScheduledExecutor(services).Run(op, CancellationToken.None)
            Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
            Assert.Equal(4, result.Files)
            Assert.Equal(100L, result.Bytes)
            Assert.EndsWith(op.BackupStem & "_002.fd-sec", result.ArchivePath)
            Assert.Single(seen)
            Assert.DoesNotContain(seen(0).Arguments, Function(a) a.Contains("correct horse"))
            Assert.Equal("correct horse battery", seen(0).ChildEnvironment(BackupCommandLine.PasswordVariable))
            Assert.Contains(items, Function(i) i.Verb = "backup-retire" AndAlso i.Verdict = "replaced")
            Assert.Contains(items, Function(i) i.Verb = "backup" AndAlso i.Verdict = "done")
            Assert.DoesNotContain("correct horse", result.Verdict)
        Finally
            Directory.Delete(source, recursive:=True)
            Directory.Delete(destination, recursive:=True)
            Directory.Delete(runs, recursive:=True)
        End Try
    End Sub

    <Fact>
    Public Sub A_refused_definition_is_parked_and_does_not_come_back_at_its_slots()
        Dim source As String = TempFolder()
        Dim destination As String = TempFolder()
        Dim runs As String = TempFolder()
        Try
            Dim op As ScheduledOperation = BackupOp()
            op.Source.Path = source
            op.Destination.Path = destination
            Dim events As String =
                "{""schemaVersion"":1,""kind"":""finding"",""data"":{""type"":""error"",""message"":""m"",""details"":{""reason"":""backup-source-refused""}}}" & vbLf &
                "{""schemaVersion"":1,""kind"":""result"",""data"":{""verdict"":""Not proven""}}" & vbLf
            Dim executor As New ScheduledExecutor(Launching(runs, "", events, 10, New List(Of FillLaunch)()))
            Dim result As ScheduledRunResult = executor.Run(op, CancellationToken.None)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-source-refused|", result.Detail)
            op.Halt = result.Halt
            op.HaltDetail = result.Detail
            Assert.True(executor.Preflight(op).IsProblem)
        Finally
            Directory.Delete(source, recursive:=True)
            Directory.Delete(destination, recursive:=True)
            Directory.Delete(runs, recursive:=True)
        End Try
    End Sub

    <Fact>
    Public Sub Backup_outcomes_are_worded_from_their_codes()
        Assert.NotEqual(ScheduledText.DescribeDetail("backup-x|y"), ScheduledText.DescribeDetail("backup-source-refused|y"))
        Assert.NotEqual(ScheduledText.DescribeDetail("backup-x|y"), ScheduledText.DescribeDetail("backup-not-proven|y"))
        Assert.NotEqual(ScheduledText.DescribeDetail("backup-x|y"), ScheduledText.DescribeDetail("backup-filedo-unsupported"))
        Assert.NotEqual(ScheduledText.DescribeDetail("backup-x|y"), ScheduledText.DescribeDetail(ShareSettingsTransfer.BackupNoPasswordCode))
        Assert.NotEqual(ScheduledText.ActionName(ScheduledAction.Copy), ScheduledText.ActionName(ScheduledAction.Backup))
    End Sub

End Class
