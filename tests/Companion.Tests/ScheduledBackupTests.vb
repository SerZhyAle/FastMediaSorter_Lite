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

    <Fact>
    Public Sub Backup_outcomes_are_worded_from_their_codes()
        Assert.NotEqual(ScheduledText.DescribeDetail("backup-x|y"), ScheduledText.DescribeDetail("backup-filedo-unsupported"))
        Assert.NotEqual(ScheduledText.DescribeDetail("backup-x|y"), ScheduledText.DescribeDetail(ShareSettingsTransfer.BackupNoPasswordCode))
        Assert.NotEqual(ScheduledText.ActionName(ScheduledAction.Copy), ScheduledText.ActionName(ScheduledAction.Backup))
    End Sub

End Class
