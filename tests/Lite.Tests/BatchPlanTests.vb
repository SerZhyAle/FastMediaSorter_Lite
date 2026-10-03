#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' 004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md §9 points 3-6. The plan is pure: it
''' is handed a "file system" that is a set of strings, so "the 22nd file has the same name as
''' the 4th" is a test and not a card full of IMG_0001.jpg.
''' </summary>
Public Class BatchPlanTests

    Private Const Slot As String = "E:\sorted"

    Private Shared Function Ctx(existing As IEnumerable(Of String), Optional policy As String = "ask") As BatchPlanContext
        Dim files As New HashSet(Of String)(existing, StringComparer.OrdinalIgnoreCase)
        Return New BatchPlanContext With {
            .DestinationFolder = Slot,
            .SlotKey = "3",
            .CollisionPolicy = policy,
            .FileExists = Function(p As String) files.Contains(p),
            .FileSize = Function(p As String) 100L}
    End Function

    Private Shared Function Thirty() As List(Of String)
        Return Enumerable.Range(1, 30).Select(Function(i) "D:\in\IMG_" & i.ToString("0000") & ".jpg").ToList()
    End Function

    ' ------------------------------------------------------------ the happy path ----

    ''' <summary>Acceptance 3.</summary>
    <Fact>
    Public Sub Thirty_files_into_an_empty_slot_are_thirty_operations()
        Dim files As List(Of String) = Thirty()
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, Ctx(files))

        Assert.Equal(30, plan.Count)
        Assert.Empty(plan.Skipped)
        Assert.Equal(3000L, plan.TotalBytes)
        Assert.All(plan.Operations, Sub(op) Assert.Equal(FileOpKind.Move, op.Kind))
        Assert.Equal("E:\sorted\IMG_0001.jpg", plan.Operations(0).Destination)
        Assert.Equal("3", plan.Operations(0).SlotKey)
        Assert.Empty(plan.NeedsCollisionAnswer)
        ' Selection order is kept: the report and the undo must be reproducible.
        Assert.Equal(files, plan.Operations.Select(Function(o) o.Source).ToList())
    End Sub

    <Fact>
    Public Sub A_copy_is_a_copy_and_a_missing_destination_folder_is_carried_to_the_worker()
        Dim files As List(Of String) = Thirty().Take(2).ToList()
        Dim c As BatchPlanContext = Ctx(files)
        c.CreateDestinationFolder = True
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.CopyToSlot, c)
        Assert.All(plan.Operations, Sub(op)
                                        Assert.Equal(FileOpKind.Copy, op.Kind)
                                        Assert.True(op.CreateDestinationFolder)
                                    End Sub)
    End Sub

    ' ------------------------------------------------------------- the refusals ----

    ''' <summary>Acceptance 4: never a silent overwrite, never a silent rename.</summary>
    <Fact>
    Public Sub Two_files_with_the_same_name_from_different_folders_are_one_operation_and_one_DuplicateName()
        Dim files As New List(Of String) From {"D:\in\a\IMG_1.jpg", "D:\in\b\IMG_1.jpg", "D:\in\a\IMG_2.jpg"}
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, Ctx(files))

        Assert.Equal(2, plan.Count)
        Assert.Equal("D:\in\a\IMG_1.jpg", plan.Operations(0).Source)   ' the first keeps the name
        Dim skip As BatchSkip = Assert.Single(plan.Skipped)
        Assert.Equal("D:\in\b\IMG_1.jpg", skip.Path)
        Assert.Equal(SkipReason.DuplicateName, skip.Reason)
    End Sub

    <Fact>
    Public Sub A_duplicate_name_is_reported_even_under_the_rename_policy()
        Dim files As New List(Of String) From {"D:\in\a\IMG_1.jpg", "D:\in\b\IMG_1.jpg"}
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, Ctx(files, "rename"))
        Assert.Equal(1, plan.Count)
        Assert.Equal(SkipReason.DuplicateName, Assert.Single(plan.Skipped).Reason)
    End Sub

    ''' <summary>Acceptance 5.</summary>
    <Fact>
    Public Sub A_slot_that_is_the_folder_the_files_are_in_yields_AlreadyInDestination_for_every_file()
        Dim files As List(Of String) = Thirty()
        Dim c As BatchPlanContext = Ctx(files)
        c.DestinationFolder = "D:\in"
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c)

        Assert.Equal(0, plan.Count)
        Assert.Equal(30, plan.Skipped.Count)
        Assert.All(plan.Skipped, Sub(s) Assert.Equal(SkipReason.AlreadyInDestination, s.Reason))
    End Sub

    <Fact>
    Public Sub A_trailing_separator_or_another_case_is_still_the_same_folder()
        Dim files As New List(Of String) From {"D:\in\a.jpg"}
        Dim c As BatchPlanContext = Ctx(files)
        c.DestinationFolder = "d:\IN\"
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.CopyToSlot, c)
        Assert.Equal(SkipReason.AlreadyInDestination, Assert.Single(plan.Skipped).Reason)
    End Sub

    <Fact>
    Public Sub A_file_that_vanished_is_skipped_by_name()
        Dim files As List(Of String) = Thirty().Take(3).ToList()
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, Ctx(files.Take(2)))
        Assert.Equal(2, plan.Count)
        Dim skip As BatchSkip = Assert.Single(plan.Skipped)
        Assert.Equal(files(2), skip.Path)
        Assert.Equal(SkipReason.VanishedBeforeStart, skip.Reason)
    End Sub

    <Fact>
    Public Sub Inside_an_archive_nothing_is_planned()
        Dim files As List(Of String) = Thirty().Take(4).ToList()
        Dim c As BatchPlanContext = Ctx(files)
        c.InsideArchive = True
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c)
        Assert.Equal(0, plan.Count)
        Assert.All(plan.Skipped, Sub(s) Assert.Equal(SkipReason.InsideArchive, s.Reason))
        Assert.Equal(4, plan.Skipped.Count)
    End Sub

    <Fact>
    Public Sub A_read_only_resource_allows_a_copy_out_but_not_a_move_or_a_delete()
        Dim files As List(Of String) = Thirty().Take(3).ToList()
        Dim c As BatchPlanContext = Ctx(files)
        c.SourceReadOnly = True

        Assert.Equal(3, BatchPlan.Build(files, BatchIntent.CopyToSlot, c).Count)

        Dim move As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c)
        Assert.Equal(0, move.Count)
        Assert.All(move.Skipped, Sub(s) Assert.Equal(SkipReason.NotWritableResource, s.Reason))

        Dim del As BatchPlan = BatchPlan.Build(files, BatchIntent.Delete, c)
        Assert.Equal(0, del.Count)
        Assert.All(del.Skipped, Sub(s) Assert.Equal(SkipReason.NotWritableResource, s.Reason))

        c.SourceReadOnly = False
        c.DestinationReadOnly = True
        Assert.Equal(0, BatchPlan.Build(files, BatchIntent.CopyToSlot, c).Count)
    End Sub

    <Fact>
    Public Sub The_same_file_under_another_spelling_is_SameFile()
        Dim files As New List(Of String) From {"Z:\in\a.jpg"}
        Dim c As BatchPlanContext = Ctx(files.Concat({"E:\sorted\a.jpg"}))
        c.IsSameFile = Function(a As String, b As String) True
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c)
        Assert.Equal(SkipReason.SameFile, Assert.Single(plan.Skipped).Reason)
        Assert.Equal(0, plan.Count)
    End Sub

    ' ------------------------------------------------------ the collision policy ----

    <Fact>
    Public Sub Ask_leaves_the_colliding_destinations_for_one_question()
        Dim files As List(Of String) = Thirty().Take(5).ToList()
        Dim existing As New List(Of String)(files) From {"E:\sorted\IMG_0002.jpg", "E:\sorted\IMG_0004.jpg"}
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, Ctx(existing))

        Assert.Equal(5, plan.Count)
        Assert.Equal(New String() {"E:\sorted\IMG_0002.jpg", "E:\sorted\IMG_0004.jpg"}, plan.NeedsCollisionAnswer.ToArray())
        Assert.Equal(2, plan.Operations.Where(Function(o) o.NameTaken).Count())
    End Sub

    <Fact>
    Public Sub Answer_replace_marks_only_the_colliding_operations()
        Dim files As List(Of String) = Thirty().Take(4).ToList()
        Dim existing As New List(Of String)(files) From {"E:\sorted\IMG_0003.jpg"}
        Dim c As BatchPlanContext = Ctx(existing)
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c).WithCollisionAnswer(CollisionAnswer.Replace, c)

        Assert.Equal(4, plan.Count)
        Assert.Empty(plan.NeedsCollisionAnswer)
        Assert.Equal(New String() {files(2)}, plan.Operations.Where(Function(o) o.ReplaceExisting).Select(Function(o) o.Source).ToArray())
    End Sub

    <Fact>
    Public Sub Answer_keep_both_gives_free_names_that_a_later_file_of_the_same_run_cannot_claim()
        ' IMG_1.jpg is taken in the slot, and the run ALSO carries a file called "IMG_1 (2).jpg".
        Dim files As New List(Of String) From {"D:\in\a\IMG_1.jpg", "D:\in\b\IMG_1 (2).jpg"}
        Dim existing As New List(Of String)(files) From {"E:\sorted\IMG_1.jpg"}
        Dim c As BatchPlanContext = Ctx(existing)
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c).WithCollisionAnswer(CollisionAnswer.KeepBoth, c)

        Assert.Equal(2, plan.Count)
        Dim destinations As List(Of String) = plan.Operations.Select(Function(o) o.Destination).ToList()
        Assert.Equal(destinations.Count, destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        Assert.Equal("E:\sorted\IMG_1 (3).jpg", plan.Operations(0).Destination)
        Assert.Equal("E:\sorted\IMG_1 (2).jpg", plan.Operations(1).Destination)
        Assert.False(plan.Operations(0).ReplaceExisting)
    End Sub

    <Fact>
    Public Sub Answer_skip_moves_the_colliding_files_to_the_skipped_list()
        Dim files As List(Of String) = Thirty().Take(4).ToList()
        Dim existing As New List(Of String)(files) From {"E:\sorted\IMG_0001.jpg"}
        Dim c As BatchPlanContext = Ctx(existing)
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c).WithCollisionAnswer(CollisionAnswer.Skip, c)

        Assert.Equal(3, plan.Count)
        Dim skip As BatchSkip = Assert.Single(plan.Skipped)
        Assert.Equal(files(0), skip.Path)
        Assert.Equal(SkipReason.NameTaken, skip.Reason)
    End Sub

    <Fact>
    Public Sub An_answer_returns_a_new_plan_and_leaves_the_original_alone()
        Dim files As List(Of String) = Thirty().Take(2).ToList()
        Dim existing As New List(Of String)(files) From {"E:\sorted\IMG_0001.jpg"}
        Dim c As BatchPlanContext = Ctx(existing)
        Dim asked As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c)
        Dim replaced As BatchPlan = asked.WithCollisionAnswer(CollisionAnswer.Replace, c)

        Assert.NotSame(asked, replaced)
        Assert.Single(asked.NeedsCollisionAnswer)
        Assert.True(asked.Operations(0).NameTaken)
        Assert.False(asked.Operations(0).ReplaceExisting)
    End Sub

    <Theory>
    <InlineData("rename")>
    <InlineData("skip")>
    <InlineData("replace")>
    Public Sub The_other_policies_decide_silently_and_ask_nothing(policy As String)
        Dim files As List(Of String) = Thirty().Take(3).ToList()
        Dim existing As New List(Of String)(files) From {"E:\sorted\IMG_0002.jpg"}
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, Ctx(existing, policy))

        Assert.Empty(plan.NeedsCollisionAnswer)
        Select Case policy
            Case "rename"
                Assert.Equal(3, plan.Count)
                Assert.Equal("E:\sorted\IMG_0002 (2).jpg", plan.Operations(1).Destination)
            Case "skip"
                Assert.Equal(2, plan.Count)
                Assert.Equal(SkipReason.NameTaken, Assert.Single(plan.Skipped).Reason)
            Case Else
                Assert.Equal(3, plan.Count)
                Assert.True(plan.Operations(1).ReplaceExisting)
        End Select
    End Sub

    ' ------------------------------------------------------------------ delete ----

    Private Shared Function Permanent(reason As PermanentReason) As DeleteDecision
        Return New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = reason}
    End Function

    Private Shared ReadOnly ToTheBin As New DeleteDecision With {.Outcome = DeleteOutcome.Recycle, .Reason = PermanentReason.NotPermanent}

    ''' <summary>Acceptance 6.</summary>
    <Fact>
    Public Sub A_batch_delete_with_no_recycle_bin_names_the_reason_and_asks_exactly_once()
        Dim files As List(Of String) = Thirty()
        Dim c As BatchPlanContext = Ctx(files)
        c.DeleteDecision = Permanent(PermanentReason.NoBinOnNetwork)
        c.ConfirmDeletePolicy = "permanentOnly"
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.Delete, c)

        Assert.Equal(30, plan.Count)
        Assert.All(plan.Operations, Sub(op) Assert.Equal(FileOpKind.Delete, op.Kind))
        Assert.Equal(PermanentReason.NoBinOnNetwork, plan.PermanentDeletionReason)
        Assert.True(plan.NeedsDeleteConfirmation)
    End Sub

    <Fact>
    Public Sub A_recycled_delete_carries_no_reason_and_passes_in_silence_under_permanentOnly()
        Dim files As List(Of String) = Thirty().Take(3).ToList()
        Dim c As BatchPlanContext = Ctx(files)
        c.DeleteDecision = ToTheBin
        c.ConfirmDeletePolicy = "permanentOnly"
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.Delete, c)

        Assert.All(plan.Operations, Sub(op) Assert.Equal(FileOpKind.RecycleDelete, op.Kind))
        Assert.Equal(PermanentReason.NotPermanent, plan.PermanentDeletionReason)
        Assert.False(plan.NeedsDeleteConfirmation)

        c.ConfirmDeletePolicy = "always"
        Assert.True(BatchPlan.Build(files, BatchIntent.Delete, c).NeedsDeleteConfirmation)
    End Sub

    ''' <summary>The setting the owner restored on 2026-09-27: "never" means never.</summary>
    <Fact>
    Public Sub Confirm_never_means_never_even_for_a_permanent_delete()
        Dim files As List(Of String) = Thirty().Take(3).ToList()
        Dim c As BatchPlanContext = Ctx(files)
        c.DeleteDecision = Permanent(PermanentReason.UserAsked)
        c.ConfirmDeletePolicy = "never"
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.Delete, c)
        Assert.False(plan.NeedsDeleteConfirmation)
        Assert.Equal(PermanentReason.UserAsked, plan.PermanentDeletionReason)
    End Sub

    <Fact>
    Public Sub No_delete_decision_counts_as_permanent_and_says_so()
        Dim files As List(Of String) = Thirty().Take(2).ToList()
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.Delete, Ctx(files))
        Assert.Equal(PermanentReason.VolumeUnknown, plan.PermanentDeletionReason)
        Assert.All(plan.Operations, Sub(op) Assert.Equal(FileOpKind.Delete, op.Kind))
    End Sub

    <Fact>
    Public Sub A_delete_of_nothing_asks_nothing()
        Dim c As BatchPlanContext = Ctx(New String() {})
        c.DeleteDecision = Permanent(PermanentReason.NoBinOnNetwork)
        Dim plan As BatchPlan = BatchPlan.Build(Thirty().Take(2), BatchIntent.Delete, c)
        Assert.Equal(0, plan.Count)
        Assert.False(plan.NeedsDeleteConfirmation)
        Assert.Equal(2, plan.Skipped.Count)
    End Sub

    ' -------------------------------------------------------------- odds and ends ----

    <Fact>
    Public Sub Empty_and_null_input_make_an_empty_plan()
        Assert.Equal(0, BatchPlan.Build(Nothing, BatchIntent.MoveToSlot, Ctx(New String() {})).Count)
        Assert.Equal(0, BatchPlan.Build(New String() {}, BatchIntent.CopyToSlot, Nothing).Count)
        Assert.Equal(0, BatchPlan.Build(New String() {"", Nothing}, BatchIntent.MoveToSlot, Ctx(New String() {})).Count)
    End Sub

    <Fact>
    Public Sub A_size_probe_that_throws_counts_as_zero_bytes()
        Dim files As List(Of String) = Thirty().Take(2).ToList()
        Dim c As BatchPlanContext = Ctx(files)
        c.FileSize = Function(p As String) As Long
                         Throw New System.IO.IOException("share went away")
                     End Function
        Dim plan As BatchPlan = BatchPlan.Build(files, BatchIntent.MoveToSlot, c)
        Assert.Equal(2, plan.Count)
        Assert.Equal(0L, plan.TotalBytes)
    End Sub

End Class
#End If
