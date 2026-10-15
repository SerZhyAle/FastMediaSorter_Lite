Option Strict On

Imports System.IO
Imports System.Text.Json.Nodes
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>The store, the journal and the machine switches (ticket 222, sections 4, 6, 7.5 and 8).</summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledStoreTests

    Private Shared Function Sample(id As String) As ScheduledOperation
        Dim op As New ScheduledOperation With {.Id = id, .Name = "Nightly backup", .Action = ScheduledAction.Move,
            .Conflict = ScheduledConflict.KeepBoth, .AnchorMinutes = 3 * 60 + 30, .IntervalMinutes = 6 * 60,
            .StartDate = New DateTime(2026, 10, 1), .CreatedAt = New DateTime(2026, 10, 1, 9, 0, 0)}
        op.Source.Path = "D:\Photos"
        op.Source.Recursive = False
        op.Destination.Path = "\\nas\backup\photos"
        op.Conditions.TypeMask = ScheduledTypeMask.Images Or ScheduledTypeMask.Video
        op.Conditions.TimeFilter = ScheduledTimeFilter.SinceLastRun
        op.Conditions.NameMask = "*.jpg;*.mp4"
        op.Conditions.ExcludeMask = "*_thumb*"
        op.Conditions.MinAgeHours = 24
        op.Conditions.MaxSizeBytes = 5L * 1024 * 1024 * 1024
        op.Conditions.IncludeHidden = True
        op.Record.LastRunAt = New DateTime(2026, 10, 12, 3, 30, 0)
        op.Record.LastSuccessAt = New DateTime(2026, 10, 11, 3, 30, 0)
        op.Record.LastOutcome = ScheduledRunOutcome.Failed
        op.Record.LastError = "a.jpg: locked"
        op.Record.NextRunAt = New DateTime(2026, 10, 13, 9, 30, 0)
        Return op
    End Function

    <Fact>
    Public Sub An_operation_round_trips_through_its_file()
        Using tree As New ScratchTree()
            Dim store As New ScheduledStore(tree.Dir("ops"))
            store.SaveDefinition(Sample("aaa111"))
            ' Saving a NEW definition carries its own record (there is none on disk to keep).
            Dim loaded As ScheduledOperation = store.TryLoad("aaa111")
            Assert.NotNull(loaded)
            Assert.Equal("Nightly backup", loaded.Name)
            Assert.Equal(ScheduledAction.Move, loaded.Action)
            Assert.Equal(ScheduledConflict.KeepBoth, loaded.Conflict)
            Assert.Equal("D:\Photos", loaded.Source.Path)
            Assert.False(loaded.Source.Recursive)
            Assert.Equal("\\nas\backup\photos", loaded.Destination.Path)
            Assert.Equal(ScheduledTypeMask.Images Or ScheduledTypeMask.Video, loaded.Conditions.TypeMask)
            Assert.Equal(ScheduledTimeFilter.SinceLastRun, loaded.Conditions.TimeFilter)
            Assert.Equal("*.jpg;*.mp4", loaded.Conditions.NameMask)
            Assert.Equal("*_thumb*", loaded.Conditions.ExcludeMask)
            Assert.Equal(24L, loaded.Conditions.MinAgeHours)
            Assert.Null(loaded.Conditions.MaxAgeHours)
            Assert.Equal(5L * 1024 * 1024 * 1024, loaded.Conditions.MaxSizeBytes)
            Assert.True(loaded.Conditions.IncludeHidden)
            Assert.Equal(3 * 60 + 30, loaded.AnchorMinutes)
            Assert.Equal(6 * 60, loaded.IntervalMinutes)
            Assert.Equal(New DateTime(2026, 10, 1), loaded.StartDate)
        End Using
    End Sub

    <Fact>
    Public Sub Last_run_and_last_success_are_kept_apart()
        Using tree As New ScratchTree()
            Dim store As New ScheduledStore(tree.Dir("ops"))
            Dim op As ScheduledOperation = Sample("bbb222")
            store.SaveDefinition(op)
            ' SaveDefinition keeps the run state that is ON DISK; the first save has none, so patch it in.
            Assert.True(store.SaveRunState("bbb222", op.Record, ScheduledHalt.None, ""))
            Dim loaded As ScheduledOperation = store.TryLoad("bbb222")
            Assert.Equal(New DateTime(2026, 10, 12, 3, 30, 0), loaded.Record.LastRunAt)
            Assert.Equal(New DateTime(2026, 10, 11, 3, 30, 0), loaded.Record.LastSuccessAt)
            Assert.Equal(ScheduledRunOutcome.Failed, loaded.Record.LastOutcome)
            Assert.Equal("a.jpg: locked", loaded.Record.LastError)
            Assert.Equal(New DateTime(2026, 10, 13, 9, 30, 0), loaded.Record.NextRunAt)
        End Using
    End Sub

    <Fact>
    Public Sub An_edit_does_not_undo_what_a_run_wrote_in_between()
        Using tree As New ScratchTree()
            Dim store As New ScheduledStore(tree.Dir("ops"))
            Dim op As ScheduledOperation = Sample("ccc333")
            store.SaveDefinition(op)
            store.SaveRunState("ccc333", op.Record, ScheduledHalt.None, "")

            ' The editor opened the operation, then a run finished and wrote its state...
            Dim editing As ScheduledOperation = store.TryLoad("ccc333")
            Dim record As ScheduledRunRecord = store.TryLoad("ccc333").Record
            record.LastSuccessAt = New DateTime(2026, 10, 13, 3, 30, 0)
            record.LastOutcome = ScheduledRunOutcome.Ok
            store.SaveRunState("ccc333", record, ScheduledHalt.None, "")

            ' ... and the user saved a renamed definition built from the older copy.
            editing.Name = "Renamed"
            store.SaveDefinition(editing)

            Dim loaded As ScheduledOperation = store.TryLoad("ccc333")
            Assert.Equal("Renamed", loaded.Name)
            Assert.Equal(New DateTime(2026, 10, 13, 3, 30, 0), loaded.Record.LastSuccessAt)
            Assert.Equal(ScheduledRunOutcome.Ok, loaded.Record.LastOutcome)
        End Using
    End Sub

    <Fact>
    Public Sub A_run_patches_its_state_without_rewriting_the_definition()
        Using tree As New ScratchTree()
            Dim folder As String = tree.Dir("ops")
            Dim store As New ScheduledStore(folder)
            store.SaveDefinition(Sample("ddd444"))
            Dim jsonFile As String = Path.Combine(folder, "ddd444.json")
            ' A newer build added a field this one has never heard of.
            Dim node As JsonObject = DirectCast(JsonNode.Parse(File.ReadAllText(jsonFile)), JsonObject)
            node("futureField") = "keep me"
            File.WriteAllText(jsonFile, node.ToJsonString())

            store.SaveRunState("ddd444", New ScheduledRunRecord With {.LastOutcome = ScheduledRunOutcome.Ok}, ScheduledHalt.Wall, "drive gone")

            Dim after As JsonObject = DirectCast(JsonNode.Parse(File.ReadAllText(jsonFile)), JsonObject)
            Assert.Equal("keep me", after("futureField").GetValue(Of String)())
            Assert.Equal("wall", after("halt").GetValue(Of String)())
            Dim loaded As ScheduledOperation = store.TryLoad("ddd444")
            Assert.Equal(ScheduledHalt.Wall, loaded.Halt)
            Assert.Equal("drive gone", loaded.HaltDetail)
        End Using
    End Sub

    <Fact>
    Public Sub An_unknown_action_is_a_halted_definition_never_another_action()
        Using tree As New ScratchTree()
            Dim folder As String = tree.Dir("ops")
            Dim store As New ScheduledStore(folder)
            store.SaveDefinition(Sample("eee555"))
            Dim jsonFile As String = Path.Combine(folder, "eee555.json")
            Dim node As JsonObject = DirectCast(JsonNode.Parse(File.ReadAllText(jsonFile)), JsonObject)
            node("action") = "shred"
            File.WriteAllText(jsonFile, node.ToJsonString())

            Dim loaded As ScheduledOperation = store.TryLoad("eee555")
            Assert.NotNull(loaded)
            Assert.Equal(ScheduledHalt.InvalidDefinition, loaded.Halt)
            Assert.False(loaded.IsArmable)
            ' A run's state patch must not rewrite the unknown action or lift the halt.
            store.SaveRunState("eee555", loaded.Record, ScheduledHalt.None, "")
            Dim after As JsonObject = DirectCast(JsonNode.Parse(File.ReadAllText(jsonFile)), JsonObject)
            Assert.Equal("shred", after("action").GetValue(Of String)())
        End Using
    End Sub

    <Fact>
    Public Sub An_unreadable_file_is_skipped_and_the_rest_load()
        Using tree As New ScratchTree()
            Dim folder As String = tree.Dir("ops")
            Dim store As New ScheduledStore(folder)
            store.SaveDefinition(Sample("fff666"))
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ this is not json")
            File.WriteAllText(Path.Combine(folder, "empty-id.json"), "{""id"":""""}")
            Dim all As List(Of ScheduledOperation) = store.LoadAll()
            Assert.Single(all)
            Assert.Equal("fff666", all(0).Id)
        End Using
    End Sub

    <Fact>
    Public Sub An_id_that_names_a_path_is_refused()
        Using tree As New ScratchTree()
            Dim store As New ScheduledStore(tree.Dir("ops"))
            Assert.Null(store.TryLoad("..\..\evil"))
            Assert.Throws(Of ArgumentException)(Sub() store.SaveRunState("..\..\evil", New ScheduledRunRecord(), ScheduledHalt.None, ""))
            Assert.Null(store.TryLoad("not-there"))
        End Using
    End Sub

    <Fact>
    Public Sub Delete_removes_the_file()
        Using tree As New ScratchTree()
            Dim store As New ScheduledStore(tree.Dir("ops"))
            store.SaveDefinition(Sample("ggg777"))
            store.Delete("ggg777")
            Assert.Null(store.TryLoad("ggg777"))
            store.Delete("ggg777")
        End Using
    End Sub

    <Fact>
    Public Sub No_secret_is_written_for_a_resource()
        Using tree As New ScratchTree()
            Dim store As New ScheduledStore(tree.Dir("ops"))
            Dim op As ScheduledOperation = Sample("hhh888")
            op.Source.Kind = ScheduledLocationKind.Resource
            op.Source.ResourceId = "res-1"
            op.Source.ResourceName = "NAS"
            op.Source.Path = "/photos"
            store.SaveDefinition(op)
            Dim text As String = File.ReadAllText(Path.Combine(tree.Dir("ops"), "hhh888.json"))
            Assert.Contains("res-1", text)
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase)
            Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase)
        End Using
    End Sub

    ' --- journal ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub History_reads_newest_first_and_survives_odd_characters()
        Using tree As New ScratchTree()
            Dim journal As New ScheduledJournal(tree.Dir("ops"))
            journal.Append(New JournalEntry With {.Timestamp = New DateTime(2026, 10, 13, 3, 0, 0), .Kind = "run", .Code = "ok",
                .OperationId = "a", .OperationName = "first", .Count = 4})
            journal.Append(New JournalEntry With {.Timestamp = New DateTime(2026, 10, 14, 3, 0, 0), .Kind = "run", .Code = "failed",
                .OperationId = "a", .OperationName = "tab" & vbTab & "and" & vbLf & "newline \ backslash",
                .Detail = "x" & vbCr & "y", .Failed = 2, .Bytes = 123456789012L})
            Dim read As List(Of JournalEntry) = journal.ReadRecent()
            Assert.Equal(2, read.Count)
            Assert.Equal("failed", read(0).Code)
            Assert.Equal("tab" & vbTab & "and" & vbLf & "newline \ backslash", read(0).OperationName)
            Assert.Equal("x" & vbCr & "y", read(0).Detail)
            Assert.Equal(123456789012L, read(0).Bytes)
            Assert.Equal("first", read(1).OperationName)
            ' The persisted line is one line and holds no sentence.
            Dim lines As String() = File.ReadAllLines(Path.Combine(tree.Dir("ops"), "history.log"))
            Assert.Equal(2, lines.Length)
            Assert.StartsWith("v1" & vbTab & "2026-10-13T03:00:00" & vbTab & "run", lines(0), StringComparison.Ordinal)
        End Using
    End Sub

    <Fact>
    Public Sub History_trims_to_the_last_500_lines_past_the_cap()
        Using tree As New ScratchTree()
            Dim folder As String = tree.Dir("ops")
            Dim journal As New ScheduledJournal(folder)
            Dim padding As String = New String("p"c, 4000)
            ' ~1.2 MB of lines in one go, written straight so the test stays fast.
            Dim jsonFile As String = Path.Combine(folder, "history.log")
            Using writer As New StreamWriter(jsonFile, False)
                For i As Integer = 1 To 300
                    writer.WriteLine("v1" & vbTab & "2026-10-13T03:00:00" & vbTab & "run" & vbTab & "ok" & vbTab & "a" & vbTab & "schedule" & vbTab & "0" & vbTab &
                                     "0" & vbTab & "0" & vbTab & "0" & vbTab & "0" & vbTab & "" & vbTab & "n" & vbTab & "" & vbTab & "" & vbTab & padding)
                Next
            End Using
            Assert.True(New FileInfo(jsonFile).Length > ScheduledJournal.HistoryMaxBytes)
            ' Fewer than 500 lines: trimming leaves all of them (it keeps the LAST 500, never fewer).
            journal.Append(New JournalEntry With {.Timestamp = DateTime.Now, .OperationId = "z", .OperationName = "last", .Code = "ok"})
            Assert.Equal(301, File.ReadAllLines(jsonFile).Length)

            ' Now well past 500 lines and the cap.
            Using writer As New StreamWriter(jsonFile, True)
                For i As Integer = 1 To 400
                    writer.WriteLine("v1" & vbTab & "2026-10-13T03:00:00" & vbTab & "run" & vbTab & "ok" & vbTab & "a" & vbTab & "schedule" & vbTab & "0" & vbTab &
                                     "0" & vbTab & "0" & vbTab & "0" & vbTab & "0" & vbTab & "" & vbTab & "n" & vbTab & "" & vbTab & "" & vbTab & padding)
                Next
            End Using
            journal.Append(New JournalEntry With {.Timestamp = DateTime.Now, .OperationId = "z", .OperationName = "newest", .Code = "ok"})
            Dim lines As String() = File.ReadAllLines(jsonFile)
            Assert.Equal(ScheduledJournal.HistoryKeepLines, lines.Length)
            Assert.Equal("newest", journal.ReadRecent(1)(0).OperationName)
        End Using
    End Sub

    <Fact>
    Public Sub Clear_history_leaves_the_mutation_journal()
        Using tree As New ScratchTree()
            Dim journal As New ScheduledJournal(tree.Dir("ops"))
            journal.Append(New JournalEntry With {.Timestamp = DateTime.Now, .OperationId = "a", .Code = "ok"})
            journal.AppendItem(New ItemJournalEntry With {.Timestamp = DateTime.Now, .OperationId = "a", .Verb = "delete", .Source = "D:\x.tmp", .Verdict = "recycle"})
            journal.ClearHistory()
            Assert.Empty(journal.ReadRecent())
            Dim items As List(Of ItemJournalEntry) = journal.ReadItems()
            Assert.Single(items)
            Assert.Equal("delete", items(0).Verb)
            Assert.Equal("recycle", items(0).Verdict)
        End Using
    End Sub

    ' --- machine switches ------------------------------------------------------------------------------

    <Fact>
    Public Sub Machine_switches_default_and_persist()
        Using hive As New ScratchHive()
            Assert.True(ScheduledSettings.FeatureEnabled)
            Assert.False(ScheduledSettings.Paused)
            Assert.Equal(MissedRunPolicy.RunOnceAtStart, ScheduledSettings.MissedRuns)
            Assert.True(ScheduledSettings.DeleteToRecycleBin)

            ScheduledSettings.Paused = True
            ScheduledSettings.MissedRuns = MissedRunPolicy.Skip
            ScheduledSettings.FeatureEnabled = False
            Assert.True(ScheduledSettings.Paused)
            Assert.Equal(MissedRunPolicy.Skip, ScheduledSettings.MissedRuns)
            Assert.False(ScheduledSettings.FeatureEnabled)
        End Using
    End Sub

    <Fact>
    Public Sub The_feature_switch_follows_the_list()
        Using hive As New ScratchHive()
            ScheduledSettings.FollowList(0)
            Assert.False(ScheduledSettings.FeatureEnabled)
            ScheduledSettings.FollowList(1)
            Assert.True(ScheduledSettings.FeatureEnabled)
            ScheduledSettings.FollowList(0)
            Assert.False(ScheduledSettings.FeatureEnabled)
        End Using
    End Sub

End Class
