#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' 004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md PB-5 and acceptance 7: a batch is ONE
''' history entry, U reverses it last-first through the inverses that already exist, and a
''' second U is "nothing to undo", never a re-application.
''' </summary>
Public Class BatchUndoTests

    Private Shared Function MoveItems(count As Integer) As BatchUndoEntry
        Dim entry As New BatchUndoEntry With {.ListGeneration = 7}
        For i As Integer = 1 To count
            entry.Items.Add(New BatchUndoItem With {
                .Kind = FileOpKind.Move,
                .Source = "D:\in\f" & i.ToString("000") & ".jpg",
                .Destination = "E:\sorted\f" & i.ToString("000") & ".jpg",
                .ListIndex = i - 1})
        Next
        Return entry
    End Function

    ''' <summary>Acceptance 7, first half: 200 files are one entry, and the history behind it survives.</summary>
    <Fact>
    Public Sub A_batch_of_200_is_one_history_entry_and_does_not_push_the_rest_out()
        Dim history As New UndoStack(Of BatchUndoEntry)(UndoPolicy.Max_Undo_Depth)
        For i As Integer = 1 To 10
            history.Push(MoveItems(1))
        Next

        history.Push(MoveItems(200))
        Assert.Equal(11, history.Count)                                 ' not 210, not 50

        ' The contrast that justifies the type: 200 SEPARATE entries would be capped at the
        ' depth and the ten before them would be gone.
        Dim flooded As New UndoStack(Of BatchUndoEntry)(UndoPolicy.Max_Undo_Depth)
        For i As Integer = 1 To 10
            flooded.Push(MoveItems(1))
        Next
        For i As Integer = 1 To 200
            flooded.Push(MoveItems(1))
        Next
        Assert.Equal(UndoPolicy.Max_Undo_Depth, flooded.Count)
    End Sub

    ''' <summary>Acceptance 7, second half.</summary>
    <Fact>
    Public Sub U_reverses_all_200_in_reverse_order_and_a_second_U_has_nothing_to_undo()
        Dim history As New UndoStack(Of BatchUndoEntry)(UndoPolicy.Max_Undo_Depth)
        history.Push(MoveItems(200))

        Dim entry As BatchUndoEntry = history.Pop()
        Dim plan As BatchUndoPlan = BatchUndoPlanner.Build(entry, Nothing)

        Assert.Equal(200, plan.Inverse.Count)
        Assert.Empty(plan.NotReturnable)
        Assert.All(plan.Inverse, Sub(op) Assert.Equal(FileOpKind.MoveUndo, op.Kind))
        ' The last file moved is the first one returned.
        Assert.Equal("E:\sorted\f200.jpg", plan.Inverse(0).Source)
        Assert.Equal("D:\in\f200.jpg", plan.Inverse(0).Destination)
        Assert.Equal("E:\sorted\f001.jpg", plan.Inverse(199).Source)
        Assert.Equal(entry.Items.Select(Function(i) i.Source).Reverse().ToList(),
                     plan.Inverse.Select(Function(o) o.Destination).ToList())

        Assert.Null(history.Pop())                                      ' nothing to undo, nothing re-applied
        Assert.True(history.IsEmpty)
    End Sub

    <Fact>
    Public Sub Each_kind_goes_back_through_its_own_inverse()
        Dim entry As New BatchUndoEntry()
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.Move, .Source = "D:\in\a.jpg", .Destination = "E:\s\a.jpg", .ListIndex = 3})
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.Copy, .Source = "D:\in\b.jpg", .Destination = "E:\s\b.jpg"})
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.RecycleDelete, .Source = "D:\in\c.jpg",
                                                .DeletedAtUtc = New DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), .ListIndex = 1})

        Dim plan As BatchUndoPlan = BatchUndoPlanner.Build(entry, Nothing)
        Assert.Equal(3, plan.Inverse.Count)

        ' Reverse order: the delete first.
        Assert.Equal(FileOpKind.RestoreFromBin, plan.Inverse(0).Kind)
        Assert.Equal("D:\in\c.jpg", plan.Inverse(0).Source)
        Assert.Equal(entry.Items(2).DeletedAtUtc, plan.Inverse(0).DeletedAtUtc)

        Assert.Equal(FileOpKind.DeleteUndo, plan.Inverse(1).Kind)
        Assert.Equal("E:\s\b.jpg", plan.Inverse(1).Source)               ' the copy is what gets deleted
        Assert.Equal("D:\in\b.jpg", plan.Inverse(1).OriginalSource)      ' and only while this still exists

        Assert.Equal(FileOpKind.MoveUndo, plan.Inverse(2).Kind)
        Assert.Equal("E:\s\a.jpg", plan.Inverse(2).Source)
        Assert.Equal("D:\in\a.jpg", plan.Inverse(2).Destination)
        Assert.Equal(3, plan.Inverse(2).ListIndex)
    End Sub

    ''' <summary>The Recycle Bin specification's rule, per file: a permanent deletion is
    ''' recorded precisely so the report can say it cannot come back.</summary>
    <Fact>
    Public Sub A_permanent_deletion_is_not_returnable_but_does_not_stop_the_others()
        Dim entry As New BatchUndoEntry()
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.Move, .Source = "D:\in\a.jpg", .Destination = "E:\s\a.jpg"})
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.Delete, .Source = "D:\in\gone.jpg",
                                                .PermanentDeleteReason = PermanentReason.NoBinOnNetwork})
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.Move, .Source = "D:\in\b.jpg", .Destination = "E:\s\b.jpg"})

        Dim plan As BatchUndoPlan = BatchUndoPlanner.Build(entry, Nothing)
        Assert.Equal(2, plan.Inverse.Count)
        Dim lost As BatchUndoItem = Assert.Single(plan.NotReturnable)
        Assert.Equal("D:\in\gone.jpg", lost.Source)
        Assert.Equal(PermanentReason.NoBinOnNetwork, lost.PermanentDeleteReason)
    End Sub

    <Fact>
    Public Sub A_restore_comes_back_under_the_free_name_the_resolver_picks()
        Dim entry As New BatchUndoEntry()
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.RecycleDelete, .Source = "D:\in\a.jpg"})
        Dim plan As BatchUndoPlan = BatchUndoPlanner.Build(entry, Function(old As String) "D:\in\a (2).jpg")
        Assert.Equal("D:\in\a.jpg", plan.Inverse(0).Source)              ' what the bin's record names
        Assert.Equal("D:\in\a (2).jpg", plan.Inverse(0).Destination)     ' where it lands
    End Sub

    <Fact>
    Public Sub An_empty_or_missing_entry_plans_nothing()
        Assert.Empty(BatchUndoPlanner.Build(Nothing, Nothing).Inverse)
        Assert.Empty(BatchUndoPlanner.Build(New BatchUndoEntry(), Nothing).Inverse)
    End Sub

    ' ----------------------------------------------------------- the table ----

    <Fact>
    Public Sub A_batch_is_recorded_and_its_plan_is_to_reverse_it()
        Assert.Equal(UndoPlan.ReverseBatch, UndoPolicy.PlanFor(FileOpKind.Batch))
        Assert.True(UndoPolicy.IsRecorded(FileOpKind.Batch))
        Assert.True(UndoPolicy.RestoresAFile(UndoPlan.ReverseBatch))
    End Sub

    ''' <summary>"An undo is never itself undoable" must hold for a batch's inverses too: none
    ''' of the kinds the planner emits is recorded, so no entry can ever hold one.</summary>
    <Fact>
    Public Sub Nothing_the_planner_emits_can_be_recorded_again()
        Dim entry As New BatchUndoEntry()
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.Move, .Source = "a", .Destination = "b"})
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.Copy, .Source = "c", .Destination = "d"})
        entry.Items.Add(New BatchUndoItem With {.Kind = FileOpKind.RecycleDelete, .Source = "e"})
        For Each op As BatchOp In BatchUndoPlanner.Build(entry, Nothing).Inverse
            Assert.False(UndoPolicy.IsRecorded(op.Kind), op.Kind.ToString() & " would be offered to U again")
        Next
    End Sub

    ''' <summary>An entry holds a BatchUndoEntry through the same bounded stack a single FileOp does.</summary>
    <Fact>
    Public Sub Nothing_is_never_recorded()
        Dim history As New UndoStack(Of BatchUndoEntry)(5)
        history.Push(Nothing)
        Assert.True(history.IsEmpty)
    End Sub

End Class
#End If
