Option Strict On

Imports System.IO
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Fill of free space (ticket L0299): the definition, the command line, CLI-EVENT-STREAM 0.13's verdict table (rules 4,
''' 10, 11 and 15), the run-owned work folders (Q299-4) and the run itself over a stub FileDO - so every verdict is
''' provable on any machine and no test writes a gigabyte.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledFillFreeSpaceTests

    Private Shared ReadOnly Noon As New DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Unspecified)
    Private Const FiveGiB As Long = 5L * 1024 * 1024 * 1024

    Private Shared Function FillOp(source As String, Optional id As String = "op1") As ScheduledOperation
        Dim o As New ScheduledOperation With {.Id = id, .Name = "fill", .Action = ScheduledAction.FillFreeSpace}
        o.Source.Path = source
        Return o
    End Function

    ' --- the definition --------------------------------------------------------------------------------

    <Fact>
    Public Sub Json_keeps_a_listed_chunk_size_and_reads_the_short_action_name()
        Dim op As ScheduledOperation = FillOp("D:\")
        op.ChunkSizeMb = 2000
        Dim read As ScheduledOperation = ScheduledJson.Deserialize(ScheduledJson.Serialize(op))
        Assert.Equal(ScheduledAction.FillFreeSpace, read.Action)
        Assert.Equal(2000, read.ChunkSizeMb)
        Assert.Contains("""fill_free_space""", ScheduledJson.Serialize(op))

        Dim shortName As ScheduledOperation = ScheduledJson.Deserialize("{""id"":""f2"",""action"":""fill"",""chunkSizeMb"":777}")
        Assert.Equal(ScheduledAction.FillFreeSpace, shortName.Action)
        Assert.Equal(ScheduledOperation.DefaultChunkSizeMb, shortName.ChunkSizeMb)
        Assert.Equal(ScheduledHalt.None, shortName.Halt)
    End Sub

    <Fact>
    Public Sub Json_of_another_action_carries_no_chunk_size()
        Dim copy As New ScheduledOperation With {.Id = "c1", .Action = ScheduledAction.Copy}
        Assert.DoesNotContain("chunkSizeMb", ScheduledJson.Serialize(copy))
    End Sub

    <Fact>
    Public Sub Normalize_clears_everything_a_fill_does_not_use()
        Dim op As ScheduledOperation = FillOp("D:\Work")
        op.Source.Recursive = True
        op.Destination.Path = "E:\Backup"
        op.Conditions.NameMask = "*.jpg"
        op.Conflict = ScheduledConflict.Overwrite
        op.ChunkSizeMb = 3
        ScheduledValidation.Normalize(op)
        Assert.True(op.Destination.IsEmpty)
        Assert.Equal("", op.Conditions.NameMask)
        Assert.Equal(ScheduledConflict.Skip, op.Conflict)
        Assert.False(op.Source.Recursive)
        Assert.Equal(ScheduledOperation.DefaultChunkSizeMb, op.ChunkSizeMb)
        Assert.False(op.NeedsDestination)
        Assert.False(op.SelectsFiles)
        Assert.True(op.IsDestructive)
    End Sub

    <Fact>
    Public Sub Validate_takes_a_local_folder_and_the_system_volume_and_refuses_the_rest()
        ' The drive root and the profile are exact protections of a copy source; a fill moves none of their files (Q299-2).
        Dim protect As New ProtectedFolders({"C:\Windows", "C:\Program Files"}, {"C:\", "C:\Users", "C:\Users\me"})
        Dim codes = Function(o As ScheduledOperation) ScheduledValidation.Validate(o, protect).Select(Function(i) i.Code).ToList()
        Assert.Empty(codes(FillOp("C:\")))
        Assert.Empty(codes(FillOp("C:\Users\me")))
        Assert.Empty(codes(FillOp("D:\Scratch")))
        Assert.Contains(ScheduledIssueCode.SourceProtected, codes(FillOp("C:\Windows\Temp")))
        Assert.Contains(ScheduledIssueCode.LocalStorageRequired, codes(FillOp("\\server\share\folder")))
        Assert.Contains(ScheduledIssueCode.SourceMissing, codes(FillOp("")))
        Assert.Contains(ScheduledIssueCode.SourceNotAbsolute, codes(FillOp("photos\2026")))
        Dim resource As ScheduledOperation = FillOp("")
        resource.Source.Kind = ScheduledLocationKind.Resource
        resource.Source.ResourceId = "r1"
        Assert.Equal({ScheduledIssueCode.LocalStorageRequired}, codes(resource).ToArray())
    End Sub

    ' --- the command line ------------------------------------------------------------------------------

    <Fact>
    Public Sub Command_line_is_the_folder_verb_with_both_channel_flags()
        Dim args As List(Of String) = FillCommandLine.Arguments("D:\FMS-FillFreeSpace-op1-20261110120000", 2000, "D:\w\e.jsonl", "D:\w\stop")
        Assert.Equal({"folder", "D:\FMS-FillFreeSpace-op1-20261110120000", "fill", "2000", "del", "--no-ui",
                      "--events", "D:\w\e.jsonl", "--stop-file", "D:\w\stop"}, args.ToArray())
    End Sub

    <Fact>
    Public Sub Verdict_text_reads_back_and_other_text_does_not()
        Dim free, written As Long
        Assert.True(FillCommandLine.TryReadVerdict(FillCommandLine.Verdict(FiveGiB, 1048576, 0), free, written))
        Assert.Equal(FiveGiB, free)
        Assert.Equal(1048576L, written)
        Assert.False(FillCommandLine.TryReadVerdict("recycle", free, written))
        Assert.Equal("fill:free=1;bytes=2;exit=none", FillCommandLine.Verdict(1, 2, Nothing))
    End Sub

    ' --- the event stream (CLI-EVENT-STREAM 0.13) ------------------------------------------------------

    Private Shared Function Line(kind As String, data As String, Optional schema As String = "1") As String
        Return "{""schemaVersion"":" & schema & ",""kind"":""" & kind & """,""timestamp"":""2026-11-10T12:00:00Z"",""data"":" & data & "}" & vbLf
    End Function

    Private Shared Function Stream(verdict As String) As String
        Dim text As String = Line("run", "{}") &
            Line("progress", "{""doneBytes"":1024,""doneItems"":1}") &
            Line("progress", "{""doneBytes"":2048,""doneItems"":2}")
        If verdict IsNot Nothing Then text &= Line("result", "{""verdict"":""" & verdict & """}")
        Return text
    End Function

    ''' <summary>Rule 10, 11 and 15 as a table; an exit code of -1 is a process that was ended rather than exiting.</summary>
    <Theory>
    <InlineData("Passed", 0, False, "Passed")>
    <InlineData("Done", 0, False, "Passed")>
    <InlineData("Passed", 1, False, "NotProven")>
    <InlineData("Passed", -1, False, "NotProven")>
    <InlineData("Failed", 1, False, "Failed")>
    <InlineData("Failed", 0, False, "NotProven")>
    <InlineData("Not proven", 2, False, "NotProven")>
    <InlineData("Not proven", 0, False, "NotProven")>
    <InlineData("Stopped", 0, False, "Stopped")>
    <InlineData("passed", 0, False, "NotProven")>
    <InlineData("Bogus", 0, False, "NotProven")>
    <InlineData(Nothing, 0, False, "NotProven")>
    <InlineData("Stopped", 0, True, "Stopped")>
    <InlineData("Passed", 0, True, "Stopped")>
    <InlineData(Nothing, -1, True, "Stopped")>
    <InlineData("Failed", 1, True, "Failed")>
    <InlineData("Failed", -1, True, "NotProven")>
    Public Sub Judge_follows_the_verdict_table(verdict As String, exitCode As Integer, stopRequested As Boolean, expected As String)
        Dim code As Integer? = If(exitCode < 0, CType(Nothing, Integer?), exitCode)
        Assert.Equal(expected, FillEvents.Parse(Stream(verdict)).Judge(code, stopRequested).ToString())
    End Sub

    <Fact>
    Public Sub Progress_numbers_are_the_last_ones()
        Dim events As FillEvents = FillEvents.Parse(Stream("Passed"))
        Assert.Equal(2048L, events.DoneBytes)
        Assert.Equal(2L, events.DoneItems)
    End Sub

    <Fact>
    Public Sub A_result_line_without_its_newline_is_not_read()
        Dim whole As String = Stream("Passed")
        Dim partialLine As String = whole.Substring(0, whole.Length - 1)
        Assert.False(FillEvents.Parse(partialLine).HasResult)
        Assert.Equal("NotProven", FillEvents.Parse(partialLine).Judge(0, False).ToString())
        Assert.Equal("Passed", FillEvents.Parse(whole).Judge(0, False).ToString())
    End Sub

    <Fact>
    Public Sub A_newer_major_or_a_text_schema_version_is_refused()
        Dim newer As String = Line("run", "{}", "2") & Line("result", "{""verdict"":""Passed""}", "2")
        Assert.True(FillEvents.Parse(newer).Refused)
        Assert.Equal("NotProven", FillEvents.Parse(newer).Judge(0, False).ToString())
        Dim text As String = Line("run", "{}", """1""") & Line("result", "{""verdict"":""Passed""}", """1""")
        Assert.True(FillEvents.Parse(text).Refused)
    End Sub

    <Fact>
    Public Sub Unreadable_lines_and_unknown_kinds_are_ignored()
        Dim text As String = "not json" & vbLf & Line("telemetry", "{""x"":1}") & Stream("Passed")
        Assert.Equal("Passed", FillEvents.Parse(text).Judge(0, False).ToString())
    End Sub

    ' --- the work folders (Q299-4) ---------------------------------------------------------------------

    <Fact>
    Public Sub Clear_removes_FileDO_shapes_the_run_files_and_the_folder()
        Using tree As New ScratchTree()
            Dim folder As String = FillWorkFolders.Create(tree.Root, "op1", Noon)
            Assert.True(FillWorkFolders.IsOwnName(Path.GetFileName(folder), "op1"))
            Assert.False(FillWorkFolders.IsOwnName(Path.GetFileName(folder), "op2"))
            File.WriteAllText(Path.Combine(folder, "FILL_001_10120000_0badf00d.tmp"), "x")
            File.WriteAllText(Path.Combine(folder, "FILL_00002_101200.tmp"), "x")
            File.WriteAllText(Path.Combine(folder, "__filedo_test_123.tmp"), "x")
            File.WriteAllText(Path.Combine(folder, FillWorkFolders.EventsName), "x")
            Directory.CreateDirectory(Path.Combine(folder, FillWorkFolders.FatSubfolder))
            File.WriteAllText(Path.Combine(folder, FillWorkFolders.FatSubfolder, "FILL_003_10120000_0badf00d.tmp"), "x")
            Assert.True(FillWorkFolders.IsClearable(folder, "op1"))
            Assert.True(FillWorkFolders.Clear(folder, "op1"))
            Assert.False(Directory.Exists(folder))
        End Using
    End Sub

    <Fact>
    Public Sub Clear_keeps_a_folder_with_a_file_FileDO_never_writes()
        Using tree As New ScratchTree()
            Dim folder As String = FillWorkFolders.Create(tree.Root, "op1", Noon)
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine")
            ' A FILL-like name of another shape is the user's file too.
            File.WriteAllText(Path.Combine(folder, "FILL_1.tmp"), "mine")
            Assert.False(FillWorkFolders.IsClearable(folder, "op1"))
            Assert.False(FillWorkFolders.Clear(folder, "op1"))
            Assert.True(File.Exists(Path.Combine(folder, "notes.txt")))
            Assert.True(File.Exists(Path.Combine(folder, "FILL_1.tmp")))
        End Using
    End Sub

    <Fact>
    Public Sub A_folder_whose_marker_names_another_operation_is_not_touched()
        Using tree As New ScratchTree()
            Dim theirs As String = FillWorkFolders.Create(tree.Root, "op2", Noon)
            File.WriteAllText(Path.Combine(theirs, "FILL_001_10120000_0badf00d.tmp"), "x")
            Dim renamed As String = Path.Combine(tree.Root, FillWorkFolders.FolderName("op1", Noon))
            Directory.Move(theirs, renamed)
            Assert.False(FillWorkFolders.Clear(renamed, "op1"))
            Assert.True(File.Exists(Path.Combine(renamed, "FILL_001_10120000_0badf00d.tmp")))
        End Using
    End Sub

    <Fact>
    Public Sub Recovery_looks_only_at_this_operations_folders()
        Using tree As New ScratchTree()
            Dim mine As String = FillWorkFolders.Create(tree.Root, "op1", Noon.AddHours(-1))
            File.WriteAllText(Path.Combine(mine, "FILL_001_10110000_0badf00d.tmp"), "x")
            Dim other As String = FillWorkFolders.Create(tree.Root, "op2", Noon.AddHours(-1))
            File.WriteAllText(Path.Combine(other, "FILL_001_10110000_0badf00d.tmp"), "x")
            Dim stranger As String = tree.Dir(FillWorkFolders.Prefix & "op1-notastamp")
            Assert.Empty(FillWorkFolders.RecoverEarlier(tree.Root, "op1"))
            Assert.False(Directory.Exists(mine))
            Assert.True(Directory.Exists(other))
            Assert.True(Directory.Exists(stranger))
        End Using
    End Sub

    ' --- the run, over a stub FileDO -------------------------------------------------------------------

    Private NotInheritable Class FakeFileDo
        Friend Verdict As String = "Passed"
        Friend ExitCode As Integer? = 0
        Friend StopRequested As Boolean
        Friend Started As Boolean = True
        Friend Network As Boolean
        Friend Present As Boolean = True
        Friend Launches As New List(Of FillLaunch)()
        Friend ReadOnly Items As New List(Of ItemJournalEntry)()

        Friend Function Services() As ScheduledServices
            Return New ScheduledServices With {
                .Clock = Function() Noon,
                .LocateFileDo = Function() If(Present, "C:\stub\filedo.exe", ""),
                .FillVolume = Function(place As String) New FillVolumeInfo With {.Known = True, .FreeBytes = FiveGiB, .IsNetwork = Network},
                .LaunchFill = AddressOf Fill,
                .ItemJournal = Sub(entry As ItemJournalEntry) Items.Add(entry)}
        End Function

        ''' <summary>Writes what FileDO would: a fill file left behind (as if it were ended) and the event stream.</summary>
        Private Function Fill(launch As FillLaunch, ct As CancellationToken) As FillLaunchOutcome
            Launches.Add(launch)
            If Not Started Then Return New FillLaunchOutcome()
            Dim folder As String = launch.Arguments(1)
            File.WriteAllText(Path.Combine(folder, "FILL_001_10120000_0badf00d.tmp"), "x")
            File.WriteAllText(launch.Arguments(7), Stream(Verdict))
            If StopRequested Then File.WriteAllText(launch.StopFile, "stop")
            Return New FillLaunchOutcome With {.Started = True, .ExitCode = ExitCode, .StopRequested = StopRequested}
        End Function
    End Class

    Private Shared Function Run(stub As FakeFileDo, op As ScheduledOperation) As ScheduledRunResult
        Return New ScheduledExecutor(stub.Services()).Run(op, CancellationToken.None)
    End Function

    Private Shared Function WorkFolders(root As String) As String()
        Return Directory.GetDirectories(root, FillWorkFolders.Prefix & "*")
    End Function

    <Fact>
    Public Sub A_passed_fill_is_ok_and_leaves_nothing_behind()
        Using tree As New ScratchTree()
            Dim stub As New FakeFileDo()
            Dim r As ScheduledRunResult = Run(stub, FillOp(tree.Root))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(2, r.Files)
            Assert.Equal(2048L, r.Bytes)
            Assert.StartsWith("fill:free=" & FiveGiB.ToString(Globalization.CultureInfo.InvariantCulture) & ";bytes=2048;", r.Verdict)
            Assert.Empty(WorkFolders(tree.Root))
            Dim launch As FillLaunch = Assert.Single(stub.Launches)
            Assert.Equal("folder", launch.Arguments(0))
            Assert.Equal(Path.Combine(tree.Root, FillWorkFolders.FolderName("op1", Noon)), launch.Arguments(1))
            Assert.Equal("1000", launch.Arguments(3))
            Dim item As ItemJournalEntry = Assert.Single(stub.Items)
            Assert.Equal("fill-free-space", item.Verb)
            Assert.Equal("Passed", item.Verdict)
        End Using
    End Sub

    <Fact>
    Public Sub A_failed_or_unproven_fill_fails_with_its_code()
        Using tree As New ScratchTree()
            Dim failed As ScheduledRunResult = Run(New FakeFileDo With {.Verdict = "Failed", .ExitCode = 1}, FillOp(tree.Root))
            Assert.Equal(ScheduledRunOutcome.Failed, failed.Outcome)
            Assert.StartsWith("fill-failed|", failed.FirstError)
            Dim silent As ScheduledRunResult = Run(New FakeFileDo With {.Verdict = Nothing, .ExitCode = 0}, FillOp(tree.Root))
            Assert.Equal(ScheduledRunOutcome.Failed, silent.Outcome)
            Assert.StartsWith("fill-not-proven|", silent.FirstError)
            Assert.Empty(WorkFolders(tree.Root))
        End Using
    End Sub

    <Fact>
    Public Sub A_stopped_fill_is_cancelled_and_cleared()
        Using tree As New ScratchTree()
            Dim r As ScheduledRunResult = Run(New FakeFileDo With {.Verdict = "Stopped", .StopRequested = True}, FillOp(tree.Root))
            Assert.Equal(ScheduledRunOutcome.Cancelled, r.Outcome)
            Assert.Empty(WorkFolders(tree.Root))
        End Using
    End Sub

    <Fact>
    Public Sub Missing_FileDO_a_network_drive_and_a_failed_start_are_walls()
        Using tree As New ScratchTree()
            Dim missing As New FakeFileDo With {.Present = False}
            Dim r As ScheduledRunResult = Run(missing, FillOp(tree.Root))
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal(ScheduledHalt.Wall, r.Halt)
            Assert.Equal("filedo-missing", r.Detail)
            Assert.Empty(missing.Launches)

            Dim network As ScheduledRunResult = Run(New FakeFileDo With {.Network = True}, FillOp(tree.Root))
            Assert.StartsWith("fill-network-drive|", network.Detail)

            Dim notStarted As ScheduledRunResult = Run(New FakeFileDo With {.Started = False}, FillOp(tree.Root))
            Assert.Equal(ScheduledRunOutcome.Wall, notStarted.Outcome)
            Assert.StartsWith("filedo-start|", notStarted.Detail)
            Assert.Empty(WorkFolders(tree.Root))
        End Using
    End Sub

    <Fact>
    Public Sub A_leftover_it_cannot_clear_walls_before_FileDO_starts_and_the_preflight_sees_it_go()
        Using tree As New ScratchTree()
            Dim earlier As String = FillWorkFolders.Create(tree.Root, "op1", Noon.AddDays(-1))
            File.WriteAllText(Path.Combine(earlier, "notes.txt"), "mine")
            Dim stub As New FakeFileDo()
            Dim op As ScheduledOperation = FillOp(tree.Root)
            Dim r As ScheduledRunResult = Run(stub, op)
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal("fill-leftovers|" & earlier, r.Detail)
            Assert.Empty(stub.Launches)

            Dim executor As New ScheduledExecutor(stub.Services())
            Assert.True(executor.Preflight(op).IsProblem)
            File.Delete(Path.Combine(earlier, "notes.txt"))
            Assert.False(executor.Preflight(op).IsProblem)
        End Using
    End Sub

    <Fact>
    Public Sub A_refused_definition_never_reaches_FileDO()
        Dim stub As New FakeFileDo()
        Dim r As ScheduledRunResult = Run(stub, FillOp("\\server\share"))
        Assert.Equal(ScheduledRunOutcome.Refused, r.Outcome)
        Assert.StartsWith("issue-LocalStorageRequired|", r.Detail)
        Assert.Empty(stub.Launches)
    End Sub

    ' --- the words --------------------------------------------------------------------------------------

    <Fact>
    Public Sub Every_fill_code_has_its_own_sentence()
        Dim fallback As String = ScheduledText.DescribeDetail("no-such-code|D:\x")
        For Each code As String In {"filedo-missing", "filedo-start|C:\x", "fill-failed|D:\", "fill-not-proven|D:\",
                                    "fill-leftovers|D:\FMS-FillFreeSpace-op1-20261110120000", "fill-network-drive|Z:\"}
            Dim text As String = ScheduledText.DescribeDetail(code)
            Assert.NotEqual(fallback, text)
            Assert.False(String.IsNullOrWhiteSpace(text))
        Next
        Assert.NotEqual(ScheduledText.IssueText(New ScheduledIssue(ScheduledIssueCode.SourceMissing)),
                        ScheduledText.IssueText(New ScheduledIssue(ScheduledIssueCode.LocalStorageRequired)))
        Assert.NotEqual(ScheduledText.ActionName(ScheduledAction.Copy), ScheduledText.ActionName(ScheduledAction.FillFreeSpace))
        Assert.Equal(Localization.TF("свободно перед запуском: {0}, перезаписано: {1}", ScheduledText.SizeText(FiveGiB), ScheduledText.SizeText(2048)),
                     ScheduledText.RecordedVerdictText(FillCommandLine.Verdict(FiveGiB, 2048, 0)))
    End Sub

    <Fact>
    Public Sub Sizes_read_in_gigabytes_from_one_and_in_megabytes_below()
        Assert.Equal(Localization.TF("{0} ГБ", 5.0.ToString("0.0", Globalization.CultureInfo.CurrentCulture)), ScheduledText.SizeText(FiveGiB))
        Assert.Equal(Localization.TF("{0} МБ", "512"), ScheduledText.SizeText(512L * 1024 * 1024))
        Assert.Equal(Localization.TF("{0} МБ", "0"), ScheduledText.SizeText(-5))
    End Sub

    <Fact>
    Public Sub Locator_takes_the_first_folder_that_holds_filedo_exe()
        Dim seen As New List(Of String)()
        Dim found As String = FileDoLocator.Locate({"", "C:\a", "C:\b", "C:\c"}, Function(p As String)
                                                                                    seen.Add(p)
                                                                                    Return p = "C:\b\filedo.exe"
                                                                                End Function)
        Assert.Equal("C:\b\filedo.exe", found)
        Assert.Equal({"C:\a\filedo.exe", "C:\b\filedo.exe"}, seen.ToArray())
        Assert.Equal("", FileDoLocator.Locate({"C:\a"}, Function(p As String) False))
    End Sub

End Class
