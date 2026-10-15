Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The executor of scheduled operations (ticket 222, sections 4, 6 and 8) over scratch folders, with a
''' fixed clock and a stub Recycle Bin: every verdict the log records is provable on any machine.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledExecutorTests

    Private Shared ReadOnly Noon As New DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified)

    ''' <summary>Services over a stub bin: "binned" lists what the shell was asked to take.</summary>
    Private NotInheritable Class Harness
        Friend ReadOnly Services As ScheduledServices
        Friend ReadOnly Binned As New List(Of String)()
        Friend ReadOnly Permanent As New List(Of String)()
        Friend ReadOnly Items As New List(Of ItemJournalEntry)()
        Friend BinOn As Boolean = True
        Friend Facts As New DeleteVolumeFacts With {.Kind = DeleteVolumeKind.FixedDisk}
        Friend HardDeleteInstead As Boolean

        Friend Sub New()
            Services = New ScheduledServices With {
                .Clock = Function() Noon,
                .DeleteToRecycleBin = Function() BinOn,
                .VolumeFacts = Function(path As String) Facts,
                .SendToBin = Function(path As String) As Boolean
                                 Binned.Add(path)
                                 File.Delete(path)
                                 Return HardDeleteInstead
                             End Function,
                .DeletePermanently = Sub(path As String)
                                         Permanent.Add(path)
                                         File.Delete(path)
                                     End Sub,
                .ItemJournal = Sub(entry As ItemJournalEntry) Items.Add(entry)}
        End Sub
    End Class

    Private Shared Function Op(action As ScheduledAction, source As String, Optional destination As String = "") As ScheduledOperation
        Dim o As New ScheduledOperation With {.Id = "op1", .Name = "test", .Action = action}
        o.Source.Path = source
        o.Destination.Path = destination
        Return o
    End Function

    Private Shared Function Run(h As Harness, o As ScheduledOperation) As ScheduledRunResult
        Return New ScheduledExecutor(h.Services).Run(o, CancellationToken.None)
    End Function

    ' --- copy ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub Copy_keeps_the_relative_structure_and_leaves_the_originals()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "A")
            tree.Write("src\sub\b.txt", "B")
            tree.Write("src\sub\deep\c.txt", "C")
            Dim h As New Harness()
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst")))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(3, r.Files)
            Assert.Equal("A", tree.Read("dst\a.txt"))
            Assert.Equal("B", tree.Read("dst\sub\b.txt"))
            Assert.Equal("C", tree.Read("dst\sub\deep\c.txt"))
            Assert.True(tree.Exists("src\sub\b.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Copy_without_recursion_stays_in_the_top_folder()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            tree.Write("src\sub\b.txt")
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            o.Source.Recursive = False
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(1, r.Files)
            Assert.True(tree.Exists("dst\a.txt"))
            Assert.False(tree.Exists("dst\sub\b.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Conflict_skip_counts_and_keeps_the_destination_file()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "new")
            tree.Write("src\b.txt", "bee")
            tree.Write("dst\a.txt", "old")
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            o.Conflict = ScheduledConflict.Skip
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(1, r.Files)
            Assert.Equal(1, r.Skipped)
            Assert.Equal(0, r.Failed)
            Assert.Equal("old", tree.Read("dst\a.txt"))
            Assert.Equal("bee", tree.Read("dst\b.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Conflict_overwrite_replaces()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "new")
            tree.Write("dst\a.txt", "old")
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            o.Conflict = ScheduledConflict.Overwrite
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(1, r.Files)
            Assert.Equal("new", tree.Read("dst\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Conflict_keep_both_uses_the_viewers_free_name_rule()
        Using tree As New ScratchTree()
            tree.Write("src\IMG_0001.jpg", "new")
            tree.Write("dst\IMG_0001.jpg", "old")
            tree.Write("dst\IMG_0001 (2).jpg", "older")
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            o.Conflict = ScheduledConflict.KeepBoth
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(1, r.Files)
            Assert.Equal("old", tree.Read("dst\IMG_0001.jpg"))
            Assert.Equal("older", tree.Read("dst\IMG_0001 (2).jpg"))
            Assert.Equal("new", tree.Read("dst\IMG_0001 (3).jpg"))
        End Using
    End Sub

    <Fact>
    Public Sub Keep_both_is_free_against_the_other_files_of_the_same_run()
        Using tree As New ScratchTree()
            ' Two different source files that map to one destination name once flattened by a filter-free copy
            ' into an existing name: both must survive under distinct names.
            tree.Write("src\a\x.txt", "one")
            tree.Write("src\b\x.txt", "two")
            tree.Write("dst\a\x.txt", "old-a")
            tree.Write("dst\b\x.txt", "old-b")
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            o.Conflict = ScheduledConflict.KeepBoth
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(2, r.Files)
            Assert.Equal("one", tree.Read("dst\a\x (2).txt"))
            Assert.Equal("two", tree.Read("dst\b\x (2).txt"))
        End Using
    End Sub

    ' --- the run's ends --------------------------------------------------------------------------------

    <Fact>
    Public Sub An_empty_filtered_set_is_a_successful_zero_file_run()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            o.Conditions.NameMask = "*.nomatch"
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(0, r.Files)
        End Using
    End Sub

    <Fact>
    Public Sub A_missing_source_hits_the_wall_and_touches_nothing()
        Using tree As New ScratchTree()
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, Path.Combine(tree.Root, "gone"), tree.Dir("dst"))
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal(ScheduledHalt.Wall, r.Halt)
            Assert.StartsWith("source-unreachable|", r.Detail, StringComparison.Ordinal)
        End Using
    End Sub

    <Fact>
    Public Sub A_target_that_cannot_exist_is_refused_before_anything_is_copied()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            ' The parent of the destination does not exist either: a drive that is not there is not conjured up.
            Dim missing As String = Path.Combine(tree.Root, "no-such-drive-root", "backup")
            Dim r As ScheduledRunResult = Run(New Harness(), Op(ScheduledAction.Copy, tree.Dir("src"), missing))
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal(0, r.Files)
            Assert.False(Directory.Exists(missing))
        End Using
    End Sub

    <Fact>
    Public Sub A_missing_destination_with_an_existing_parent_is_created()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            Dim dst As String = Path.Combine(tree.Root, "dst-new")
            Dim r As ScheduledRunResult = Run(New Harness(), Op(ScheduledAction.Copy, tree.Dir("src"), dst))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.True(tree.Exists("dst-new\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub One_locked_file_does_not_stop_the_run()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "A")
            Dim lockedPath As String = tree.Write("src\b.txt", "B")
            tree.Write("src\c.txt", "C")
            Dim h As New Harness()
            Using held As New FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst")))
                Assert.Equal(ScheduledRunOutcome.Failed, r.Outcome)
                Assert.Equal(2, r.Files)
                Assert.Equal(1, r.Failed)
                Assert.Equal("locked|b.txt", r.FirstError)
            End Using
            Assert.True(tree.Exists("dst\a.txt"))
            Assert.True(tree.Exists("dst\c.txt"))
            ' No half file under any name.
            Assert.False(Directory.EnumerateFiles(tree.Dir("dst"), "*" & LocalTransfer.PartSuffix, SearchOption.AllDirectories).Any())
        End Using
    End Sub

    ''' <summary>A transfer that fails every file with an IOException the classifier takes for a lost link.</summary>
    Private NotInheritable Class LinkLookingFailure
        Implements IScheduledTransfer
        Friend Calls As Integer
        Friend OnFirst As Action
        Private Sub Boom()
            Calls += 1
            If Calls = 1 AndAlso OnFirst IsNot Nothing Then OnFirst()
            ' ERROR_INVALID_NAME: an invalid file name raises it, and so does a link that dropped.
            Throw New IOException("syntax", &H8007007B)
        End Sub
        Public Function CopyFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide, targetRelative As String,
                                 overwrite As Boolean, ct As CancellationToken) As Long Implements IScheduledTransfer.CopyFile
            Boom()
            Return 0
        End Function
        Public Function MoveFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide, targetRelative As String,
                                 overwrite As Boolean, ct As CancellationToken) As MoveOutcome Implements IScheduledTransfer.MoveFile
            Boom()
            Return MoveOutcome.Moved
        End Function
        Public Function DeleteFile(side As IScheduledSide, entry As SideEntry, ct As CancellationToken) As String Implements IScheduledTransfer.DeleteFile
            Throw New NotSupportedException()
        End Function
    End Class

    <Fact>
    Public Sub An_error_that_only_looks_like_a_lost_link_is_one_bad_file_not_a_wall()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            tree.Write("src\b.txt")
            Dim h As New Harness()
            Dim failing As New LinkLookingFailure()
            h.Services.TransferFor = Function(s As IScheduledSide, d As IScheduledSide) failing
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst")))
            ' Both folders still answer, so both files were counted - the run was not stopped by the first.
            Assert.Equal(ScheduledRunOutcome.Failed, r.Outcome)
            Assert.Equal(2, failing.Calls)
            Assert.Equal(2, r.Failed)
            Assert.StartsWith("failed|", r.FirstError, StringComparison.Ordinal)
            Assert.Equal(ScheduledHalt.None, r.Halt)
        End Using
    End Sub

    <Fact>
    Public Sub The_same_error_is_a_wall_when_the_destination_really_went_away()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            tree.Write("src\b.txt")
            Dim dst As String = tree.Dir("drive", "dst")
            Dim h As New Harness()
            ' The whole "drive" goes: a destination whose parent is missing is not conjured up again.
            Dim failing As New LinkLookingFailure With {.OnFirst = Sub() Directory.Delete(tree.Dir("drive"), True)}
            h.Services.TransferFor = Function(s As IScheduledSide, d As IScheduledSide) failing
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Copy, tree.Dir("src"), dst))
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal(ScheduledHalt.Wall, r.Halt)
            Assert.Equal(1, failing.Calls)
            Assert.StartsWith("transport|", r.Detail, StringComparison.Ordinal)
        End Using
    End Sub
    <Fact>
    Public Sub A_refused_definition_touches_nothing()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            Dim r As ScheduledRunResult = Run(New Harness(), Op(ScheduledAction.Move, tree.Dir("src"), Path.Combine(tree.Dir("src"), "inside")))
            Assert.Equal(ScheduledRunOutcome.Refused, r.Outcome)
            Assert.Contains(ScheduledIssue_Code(ScheduledIssueCode.DestinationInsideSource), r.Detail)
            Assert.True(tree.Exists("src\a.txt"))
        End Using
    End Sub

    Private Shared Function ScheduledIssue_Code(code As ScheduledIssueCode) As String
        Return code.ToString()
    End Function

    ' --- move ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub Move_takes_the_source_away_and_journals_it()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "A")
            tree.Write("src\sub\b.txt", "B")
            Dim h As New Harness()
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Move, tree.Dir("src"), tree.Dir("dst")))
            Assert.Equal(2, r.Files)
            Assert.False(tree.Exists("src\a.txt"))
            Assert.False(tree.Exists("src\sub\b.txt"))
            Assert.Equal("B", tree.Read("dst\sub\b.txt"))
            Assert.Equal(2, h.Items.Where(Function(i) i.Verb = "move").Count())
        End Using
    End Sub

    <Fact>
    Public Sub A_source_that_changed_after_the_listing_is_left_where_it_is()
        Using tree As New ScratchTree()
            Dim path As String = tree.Write("src\a.txt", "first")
            Dim h As New Harness()
            h.Services.BeforeItem = Sub(relative As String)
                                        ' Someone edits the file between the listing and the action.
                                        File.WriteAllText(path, "edited meanwhile, and longer")
                                    End Sub
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Move, tree.Dir("src"), tree.Dir("dst")))
            Assert.Equal(0, r.Files)
            Assert.Equal(1, r.Skipped)
            Assert.True(tree.Exists("src\a.txt"))
            Assert.False(tree.Exists("dst\a.txt"))
        End Using
    End Sub

    ' --- delete ----------------------------------------------------------------------------------------

    <Fact>
    Public Sub Delete_goes_to_the_bin_where_the_setting_and_the_volume_allow_it()
        Using tree As New ScratchTree()
            tree.Write("src\a.tmp")
            tree.Write("src\b.tmp")
            Dim h As New Harness()
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Delete, tree.Dir("src")))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(2, r.Files)
            Assert.Equal("recycle", r.Verdict)
            Assert.Equal(2, h.Binned.Count)
            Assert.Empty(h.Permanent)
        End Using
    End Sub

    <Fact>
    Public Sub Delete_with_the_global_bin_switched_off_is_permanent_and_still_runs_without_consent()
        Using tree As New ScratchTree()
            tree.Write("src\a.tmp")
            Dim h As New Harness() With {.BinOn = False}
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Delete, tree.Dir("src")))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal("permanent:UserAsked", r.Verdict)
            Assert.Single(h.Permanent)
            Assert.False(tree.Exists("src\a.tmp"))
        End Using
    End Sub

    <Theory>
    <InlineData(1, "permanent:NoBinOnNetwork")>
    <InlineData(2, "permanent:NoBinOnRemovable")>
    <InlineData(3, "permanent:VolumeUnknown")>
    Public Sub Delete_records_the_volumes_own_reason_on_every_run(kind As Integer, expected As String)
        Using tree As New ScratchTree()
            tree.Write("src\a.tmp")
            Dim h As New Harness()
            h.Facts = New DeleteVolumeFacts With {.Kind = CType(kind, DeleteVolumeKind)}
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Delete, tree.Dir("src")))
            Assert.Equal(expected, r.Verdict)
            Assert.Equal(expected, h.Items.Single().Verdict)
        End Using
    End Sub

    <Fact>
    Public Sub A_bin_that_did_not_take_the_file_is_recorded_as_what_really_happened()
        Using tree As New ScratchTree()
            tree.Write("src\a.tmp")
            Dim h As New Harness() With {.HardDeleteInstead = True}
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Delete, tree.Dir("src")))
            Assert.Equal("permanent:BinDidNotTakeIt", r.Verdict)
        End Using
    End Sub

    <Fact>
    Public Sub Delete_verdict_for_the_editor_follows_the_same_policy()
        Dim h As New Harness()
        Dim location As New ScheduledLocation With {.Path = "D:\Temp"}
        Dim executor As New ScheduledExecutor(h.Services)
        Assert.False(executor.DeleteVerdictFor(location, 0).IsPermanent)
        h.BinOn = False
        Assert.Equal(PermanentReason.UserAsked, executor.DeleteVerdictFor(location, 0).Reason)
        h.BinOn = True
        h.Facts = New DeleteVolumeFacts With {.Kind = DeleteVolumeKind.Network}
        Assert.Equal(PermanentReason.NoBinOnNetwork, executor.DeleteVerdictFor(location, 0).Reason)
        Dim resource As New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = "r"}
        Assert.True(executor.DeleteVerdictFor(resource, 0).IsPermanent)
    End Sub

    ' --- archive ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub Archive_writes_one_timestamped_zip_with_relative_entries_and_keeps_the_originals()
        Using tree As New ScratchTree()
            tree.Write("photos\a.txt", "A")
            tree.Write("photos\sub\b.txt", "BB")
            Dim h As New Harness()
            Dim r As ScheduledRunResult = Run(h, Op(ScheduledAction.Archive, tree.Dir("photos"), tree.Dir("out")))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(2, r.Files)
            Assert.Equal("photos_20261013_1200.zip", Path.GetFileName(r.ArchivePath))
            Using zip As ZipArchive = ZipFile.OpenRead(r.ArchivePath)
                Dim names As List(Of String) = zip.Entries.Select(Function(e) e.FullName).OrderBy(Function(n) n).ToList()
                Assert.Equal({"a.txt", "sub/b.txt"}, names)
            End Using
            Assert.True(tree.Exists("photos\a.txt"))
            Assert.True(tree.Exists("photos\sub\b.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Archive_never_overwrites_an_existing_archive_of_the_same_minute()
        Using tree As New ScratchTree()
            tree.Write("photos\a.txt", "A")
            Dim h As New Harness()
            Dim first As ScheduledRunResult = Run(h, Op(ScheduledAction.Archive, tree.Dir("photos"), tree.Dir("out")))
            Dim second As ScheduledRunResult = Run(h, Op(ScheduledAction.Archive, tree.Dir("photos"), tree.Dir("out")))
            Assert.NotEqual(first.ArchivePath, second.ArchivePath)
            Assert.True(File.Exists(first.ArchivePath))
            Assert.True(File.Exists(second.ArchivePath))
        End Using
    End Sub

    <Fact>
    Public Sub Archive_with_delete_originals_removes_them_only_after_the_read_back()
        Using tree As New ScratchTree()
            tree.Write("photos\a.txt", "A")
            tree.Write("photos\b.txt", "B")
            Dim h As New Harness()
            Dim o As ScheduledOperation = Op(ScheduledAction.Archive, tree.Dir("photos"), tree.Dir("out"))
            o.DeleteOriginalsAfterVerify = True
            Dim r As ScheduledRunResult = Run(h, o)
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.False(tree.Exists("photos\a.txt"))
            Assert.False(tree.Exists("photos\b.txt"))
            Assert.True(File.Exists(r.ArchivePath))
            Assert.Equal("recycle", r.Verdict)
            Assert.Equal(2, h.Items.Where(Function(i) i.Verb = "archive-delete").Count())
        End Using
    End Sub

    <Fact>
    Public Sub A_corrupted_archive_leaves_every_original_and_says_so()
        Using tree As New ScratchTree()
            tree.Write("photos\a.txt", New String("A"c, 5000))
            tree.Write("photos\b.txt", New String("B"c, 5000))
            Dim h As New Harness()
            ' The archive is damaged between the write and the read-back.
            h.Services.AfterArchiveWritten = Sub(archive As String)
                                                 Dim bytes As Byte() = File.ReadAllBytes(archive)
                                                 For i As Integer = 30 To Math.Min(bytes.Length - 1, 60)
                                                     bytes(i) = CByte(bytes(i) Xor &HFF)
                                                 Next
                                                 File.WriteAllBytes(archive, bytes)
                                             End Sub
            Dim o As ScheduledOperation = Op(ScheduledAction.Archive, tree.Dir("photos"), tree.Dir("out"))
            o.DeleteOriginalsAfterVerify = True
            Dim r As ScheduledRunResult = Run(h, o)
            Assert.Equal(ScheduledRunOutcome.Failed, r.Outcome)
            Assert.StartsWith("archive-unverified|", r.FirstError, StringComparison.Ordinal)
            Assert.True(tree.Exists("photos\a.txt"))
            Assert.True(tree.Exists("photos\b.txt"))
            Assert.True(File.Exists(r.ArchivePath))
            Assert.Empty(h.Binned)
            Assert.Empty(h.Permanent)
        End Using
    End Sub

    <Fact>
    Public Sub An_empty_selection_writes_no_empty_archive()
        Using tree As New ScratchTree()
            tree.Write("photos\a.txt")
            Dim o As ScheduledOperation = Op(ScheduledAction.Archive, tree.Dir("photos"), tree.Dir("out"))
            o.Conditions.NameMask = "*.none"
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal("", r.ArchivePath)
            Assert.Empty(Directory.GetFiles(tree.Dir("out")))
        End Using
    End Sub

    ' --- preview ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub Preview_lists_what_would_run_and_changes_nothing()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt")
            tree.Write("src\b.txt")
            tree.Write("dst\a.txt")
            Dim o As ScheduledOperation = Op(ScheduledAction.Move, tree.Dir("src"), Path.Combine(tree.Root, "dst-not-yet"))
            Dim preview As ScheduledPreview = New ScheduledExecutor(New Harness().Services).Preview(o, CancellationToken.None)
            Assert.Equal(2, preview.WouldAct)
            Assert.Equal(0, preview.WouldSkip)
            Assert.Equal(2, preview.Lines.Count)
            ' Read-only: not even the destination folder was created.
            Assert.False(Directory.Exists(Path.Combine(tree.Root, "dst-not-yet")))
            Assert.True(tree.Exists("src\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Preview_is_capped()
        Using tree As New ScratchTree()
            For i As Integer = 1 To ScheduledExecutor.PreviewLineCap + 25
                tree.Write("src\f" & i & ".txt")
            Next
            Dim o As ScheduledOperation = Op(ScheduledAction.Delete, tree.Dir("src"))
            Dim preview As ScheduledPreview = New ScheduledExecutor(New Harness().Services).Preview(o, CancellationToken.None)
            Assert.Equal(ScheduledExecutor.PreviewLineCap + 25, preview.WouldAct)
            Assert.Equal(ScheduledExecutor.PreviewLineCap, preview.Lines.Count)
            Assert.True(preview.Truncated)
        End Using
    End Sub

    ' --- conditions inside a run -----------------------------------------------------------------------

    <Fact>
    Public Sub Since_last_run_reads_the_last_success_so_a_failed_run_loses_no_file()
        Using tree As New ScratchTree()
            tree.Write("src\old.txt", "o", Noon.AddHours(-30))
            tree.Write("src\new.txt", "n", Noon.AddHours(-2))
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            o.Conditions.TimeFilter = ScheduledTimeFilter.SinceLastRun
            ' The operation last SUCCEEDED 10 hours ago; the last run (failed) was an hour ago.
            o.Record.LastSuccessAt = Noon.AddHours(-10)
            o.Record.LastRunAt = Noon.AddHours(-1)
            Dim r As ScheduledRunResult = Run(New Harness(), o)
            Assert.Equal(1, r.Files)
            Assert.True(tree.Exists("dst\new.txt"))
            Assert.False(tree.Exists("dst\old.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Hidden_files_are_left_unless_the_operation_asks_for_them()
        Using tree As New ScratchTree()
            Dim hidden As String = tree.Write("src\h.txt", "h")
            tree.Write("src\v.txt", "v")
            File.SetAttributes(hidden, FileAttributes.Hidden)
            Dim o As ScheduledOperation = Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst"))
            Assert.Equal(1, Run(New Harness(), o).Files)
            Assert.False(tree.Exists("dst\h.txt"))
            o.Conditions.IncludeHidden = True
            Dim again As ScheduledRunResult = Run(New Harness(), o)
            Assert.True(tree.Exists("dst\h.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Files_under_a_hidden_folder_are_hidden_too()
        Using tree As New ScratchTree()
            tree.Write("src\.git\config", "c")
            File.SetAttributes(Path.Combine(tree.Root, "src", ".git"), FileAttributes.Hidden)
            tree.Write("src\code.txt", "x")
            Dim r As ScheduledRunResult = Run(New Harness(), Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst")))
            Assert.Equal(1, r.Files)
            Assert.False(tree.Exists("dst\.git\config"))
        End Using
    End Sub

    <Fact>
    Public Sub Cancellation_ends_the_run_as_cancelled()
        Using tree As New ScratchTree()
            For i As Integer = 1 To 5
                tree.Write("src\f" & i & ".txt")
            Next
            Dim h As New Harness()
            Dim cts As New CancellationTokenSource()
            Dim count As Integer = 0
            h.Services.BeforeItem = Sub(relative As String)
                                        count += 1
                                        If count = 2 Then cts.Cancel()
                                    End Sub
            Dim r As ScheduledRunResult = New ScheduledExecutor(h.Services).Run(
                Op(ScheduledAction.Copy, tree.Dir("src"), tree.Dir("dst")), cts.Token)
            Assert.Equal(ScheduledRunOutcome.Cancelled, r.Outcome)
        End Using
    End Sub

End Class
