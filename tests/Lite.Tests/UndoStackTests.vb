#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

' The history behind U (SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.5). Modern-only,
' like the feature: UndoStack.vb is whole-file "#If Not NETFRAMEWORK", so on the net48 leg
' this file compiles to nothing - the x86 viewer keeps its one-operation-deep undo.
'
' Two things here are worth a test rather than a reading. The bound is one: the design this
' came from specified a Stack with a depth cap, which cannot drop its oldest element, so
' the cap was unreachable and the "history" grew for the whole session. The table is the
' other: "an undo is never itself undoable" has to hold for every kind that exists today
' AND for every kind added later, which is why the default is "not recorded".
Public Class UndoStackTests

    ' --- the bound ------------------------------------------------------------

    <Fact>
    Public Sub Push_BeyondTheCap_DropsTheOldest()
        Dim stack As New UndoStack(Of String)(3)
        stack.Push("a")
        stack.Push("b")
        stack.Push("c")
        stack.Push("d")

        Assert.Equal(3, stack.Count)
        ' d, c, b - and "a" is gone, which is the whole point.
        Assert.Equal("d", stack.Pop())
        Assert.Equal("c", stack.Pop())
        Assert.Equal("b", stack.Pop())
        Assert.True(stack.IsEmpty)
    End Sub

    <Fact>
    Public Sub Push_ManyTimes_NeverGrowsPastTheCap()
        Dim stack As New UndoStack(Of String)(50)
        For i As Integer = 1 To 500
            stack.Push("op" & i.ToString())
        Next
        Assert.Equal(50, stack.Count)
        Assert.Equal("op500", stack.Peek())
    End Sub

    <Fact>
    Public Sub ADepthBelowOne_StillRecordsOne()
        ' A cap of zero would make Push a silent no-op - a history that says "there is
        ' nothing to undo" right after an operation is worse than no history at all.
        Dim stack As New UndoStack(Of String)(0)
        stack.Push("a")
        Assert.Equal(1, stack.Count)
    End Sub

    ' --- order and emptiness --------------------------------------------------

    <Fact>
    Public Sub Pop_ReturnsInReverseOrder()
        Dim stack As New UndoStack(Of String)(10)
        stack.Push("first")
        stack.Push("second")
        stack.Push("third")

        Assert.Equal("third", stack.Pop())
        Assert.Equal("second", stack.Pop())
        Assert.Equal("first", stack.Pop())
    End Sub

    <Fact>
    Public Sub Pop_OnAnEmptyStack_ReturnsNothing()
        Dim stack As New UndoStack(Of String)(10)
        Assert.Null(stack.Pop())
        Assert.Null(stack.Peek())
        Assert.True(stack.IsEmpty)
    End Sub

    <Fact>
    Public Sub Peek_DoesNotConsume()
        Dim stack As New UndoStack(Of String)(10)
        stack.Push("a")
        Assert.Equal("a", stack.Peek())
        Assert.Equal(1, stack.Count)
        Assert.Equal("a", stack.Pop())
        Assert.Equal(0, stack.Count)
    End Sub

    <Fact>
    Public Sub Push_Nothing_IsIgnored()
        ' An empty slot would come back out of Pop as "there is history" and then do
        ' nothing, which invariant 7 forbids.
        Dim stack As New UndoStack(Of String)(10)
        stack.Push(Nothing)
        Assert.True(stack.IsEmpty)
    End Sub

    <Fact>
    Public Sub Clear_EmptiesTheHistory()
        Dim stack As New UndoStack(Of String)(10)
        stack.Push("a")
        stack.Push("b")
        stack.Clear()
        Assert.True(stack.IsEmpty)
    End Sub

    ' --- the table: what U can promise ----------------------------------------

    <Fact>
    Public Sub AMove_IsPutBack()
        Assert.Equal(UndoPlan.MoveBack, UndoPolicy.PlanFor(FileOpKind.Move))
    End Sub

    <Fact>
    Public Sub ACopy_IsDeleted()
        Assert.Equal(UndoPlan.DeleteTheCopy, UndoPolicy.PlanFor(FileOpKind.Copy))
    End Sub

    <Fact>
    Public Sub ARename_IsRenamedBack()
        Assert.Equal(UndoPlan.RenameBack, UndoPolicy.PlanFor(FileOpKind.Rename))
    End Sub

    <Fact>
    Public Sub ARecycledDelete_ComesBackOutOfTheBin()
        Assert.Equal(UndoPlan.RestoreFromBin, UndoPolicy.PlanFor(FileOpKind.RecycleDelete))
    End Sub

    <Fact>
    Public Sub APermanentDelete_IsRecordedSoThatUCanExplainItself()
        ' NOT UndoPlan.None: the difference between "there is no history" and "that file is
        ' beyond returning" is the reason this entry is kept at all.
        Assert.Equal(UndoPlan.RefusePermanent, UndoPolicy.PlanFor(FileOpKind.Delete))
        Assert.True(UndoPolicy.IsRecorded(FileOpKind.Delete))
        Assert.False(UndoPolicy.RestoresAFile(UndoPlan.RefusePermanent))
    End Sub

    <Theory>
    <InlineData(CInt(FileOpKind.DeleteUndo))>
    <InlineData(CInt(FileOpKind.MoveUndo))>
    <InlineData(CInt(FileOpKind.RenameUndo))>
    Public Sub AnUndoIsNeverItselfRecorded(kind As Integer)
        ' No ping-pong, no redo, and no reentrancy flag to forget: the inverses simply have
        ' no plan. Integer rather than the enum - FileOpKind is Friend, and a Public test
        ' method cannot expose it.
        Assert.Equal(UndoPlan.None, UndoPolicy.PlanFor(CType(kind, FileOpKind)))
        Assert.False(UndoPolicy.IsRecorded(CType(kind, FileOpKind)))
    End Sub

    <Fact>
    Public Sub EveryPlanThatRestoresAFileSaysSo()
        Assert.True(UndoPolicy.RestoresAFile(UndoPlan.MoveBack))
        Assert.True(UndoPolicy.RestoresAFile(UndoPlan.DeleteTheCopy))
        Assert.True(UndoPolicy.RestoresAFile(UndoPlan.RenameBack))
        Assert.True(UndoPolicy.RestoresAFile(UndoPlan.RestoreFromBin))
        Assert.False(UndoPolicy.RestoresAFile(UndoPlan.None))
    End Sub

End Class
#End If
