#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Linq
Imports Xunit

''' <summary>
''' The editor's undo history (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §8, §13).
'''
''' The class exists for one reason - twenty snapshots of a 6000x4000 photo are 1.9 GB -
''' so the tests that matter are the eviction ones. They run on small pictures against a
''' small budget rather than allocating the real one: the arithmetic is the same, and a
''' test that needs a gigabyte to prove it does not need a gigabyte is a poor trade.
'''
''' Modern-only, like the class it covers: on the net48 leg both compile to nothing.
''' </summary>
Public Class EditorUndoStackTests

    ''' <summary>A picture whose colour says which snapshot it is, so "the right one came
    ''' back" is checked against a pixel rather than against a reference.</summary>
    Private Shared Function NewPicture(width As Integer, height As Integer, tint As Color) As Bitmap
        Dim bitmap As New Bitmap(width, height, PixelFormat.Format32bppArgb)
        Using g As Graphics = Graphics.FromImage(bitmap)
            g.Clear(tint)
        End Using
        Return bitmap
    End Function

    <Fact>
    Public Sub An_empty_history_offers_no_undo()
        Using history As New EditorUndoStack()
            Assert.False(history.CanUndo)
            Assert.Equal(0, history.Count)
            Assert.Equal(0L, history.ByteSize)
            Assert.Null(history.Pop())
        End Using
    End Sub

    ''' <summary>The snapshot is a copy: drawing on the picture afterwards must not
    ''' change what undo will restore. This is the whole promise of snapshots over a
    ''' journal of operations.</summary>
    <Fact>
    Public Sub A_snapshot_is_detached_from_the_picture_it_was_taken_from()
        Using history As New EditorUndoStack()
            Using picture As Bitmap = NewPicture(8, 8, Color.Red)
                history.Push(picture)
                Using g As Graphics = Graphics.FromImage(picture)
                    g.Clear(Color.Blue)
                End Using
            End Using

            Using restored As Bitmap = history.Pop()
                Assert.Equal(Color.Red.ToArgb(), restored.GetPixel(0, 0).ToArgb())
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Undo_returns_the_steps_in_reverse_order()
        Using history As New EditorUndoStack()
            For Each tint As Color In New Color() {Color.Red, Color.Lime, Color.Blue}
                Using picture As Bitmap = NewPicture(4, 4, tint)
                    history.Push(picture)
                End Using
            Next

            For Each expected As Color In New Color() {Color.Blue, Color.Lime, Color.Red}
                Using restored As Bitmap = history.Pop()
                    Assert.Equal(expected.ToArgb(), restored.GetPixel(0, 0).ToArgb())
                End Using
            Next
            Assert.False(history.CanUndo)
        End Using
    End Sub

    ''' <summary>Past the step limit the oldest goes, not the newest: the step somebody
    ''' is about to undo is the one they just made.</summary>
    <Fact>
    Public Sub Past_the_step_limit_the_oldest_snapshot_is_dropped()
        Using history As New EditorUndoStack(maxSteps:=3, maxBytes:=EditorUndoStack.Default_Max_Bytes)
            For index As Integer = 0 To 4
                Using picture As Bitmap = NewPicture(4, 4, Color.FromArgb(255, index, 0, 0))
                    history.Push(picture)
                End Using
            Next

            Assert.Equal(3, history.Count)
            ' Pushed 0..4, kept 2, 3, 4 - so the first one back is 4 and the last is 2.
            Using newest As Bitmap = history.Pop()
                Assert.Equal(4, CInt(newest.GetPixel(0, 0).R))
            End Using
            history.Pop().Dispose()
            Using oldestKept As Bitmap = history.Pop()
                Assert.Equal(2, CInt(oldestKept.GetPixel(0, 0).R))
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' The limit the specification is actually about (§8): without it twenty steps on a
    ''' 24-megapixel photo take 1.9 GB and the editor dies of OutOfMemory on exactly the
    ''' machine where large photos are being sorted.
    ''' </summary>
    <Fact>
    Public Sub Past_the_byte_budget_the_oldest_snapshots_are_dropped()
        ' Room for two 40x40 snapshots (6 400 bytes each) and not a byte more.
        Dim budget As Long = EditorUndoStack.SnapshotBytes(40, 40) * 2

        Using history As New EditorUndoStack(maxSteps:=EditorUndoStack.Default_Max_Steps, maxBytes:=budget)
            For index As Integer = 0 To 5
                Using picture As Bitmap = NewPicture(40, 40, Color.FromArgb(255, index, 0, 0))
                    history.Push(picture)
                End Using
            Next

            Assert.Equal(2, history.Count)
            Assert.True(history.ByteSize <= budget,
                        "The history is holding " & history.ByteSize & " bytes against a budget of " & budget & ".")
        End Using
    End Sub

    ''' <summary>
    ''' One snapshot over the whole budget is kept anyway. On a picture that big the
    ''' alternative is an editor with no undo at all, and the live bitmap already costs
    ''' the same - the second copy is what one step back is worth.
    ''' </summary>
    <Fact>
    Public Sub A_single_snapshot_larger_than_the_budget_is_still_kept()
        Using history As New EditorUndoStack(maxSteps:=20, maxBytes:=16)
            Using picture As Bitmap = NewPicture(32, 32, Color.Red)
                history.Push(picture)
            End Using

            Assert.Equal(1, history.Count)
            Assert.True(history.CanUndo)
            Assert.True(history.ByteSize > 16, "The over-budget snapshot was not counted.")
            history.Pop().Dispose()
        End Using
    End Sub

    ''' <summary>The byte total has to come back down as steps are undone, or a long
    ''' session would evict against a budget it is no longer using.</summary>
    <Fact>
    Public Sub Undoing_gives_the_budget_back()
        Using history As New EditorUndoStack()
            Using picture As Bitmap = NewPicture(20, 20, Color.Red)
                history.Push(picture)
                history.Push(picture)
            End Using
            Assert.Equal(EditorUndoStack.SnapshotBytes(20, 20) * 2, history.ByteSize)

            history.Pop().Dispose()
            Assert.Equal(EditorUndoStack.SnapshotBytes(20, 20), history.ByteSize)

            history.Pop().Dispose()
            Assert.Equal(0L, history.ByteSize)
        End Using
    End Sub

    <Fact>
    Public Sub Clearing_empties_the_history()
        Using history As New EditorUndoStack()
            Using picture As Bitmap = NewPicture(8, 8, Color.Red)
                history.Push(picture)
                history.Push(picture)
            End Using

            history.Clear()

            Assert.Equal(0, history.Count)
            Assert.Equal(0L, history.ByteSize)
            Assert.False(history.CanUndo)
        End Using
    End Sub

    <Fact>
    Public Sub Pushing_nothing_is_not_a_step()
        Using history As New EditorUndoStack()
            history.Push(Nothing)

            Assert.Equal(0, history.Count)
            Assert.False(history.CanUndo)
        End Using
    End Sub

    ''' <summary>Four bytes a pixel, because the copy is 32bpp whatever the original was.
    ''' Long arithmetic on purpose: a 24-megapixel snapshot is 96 MB and twenty of them
    ''' overflow an Integer.</summary>
    <Fact>
    Public Sub A_snapshot_costs_four_bytes_a_pixel()
        Assert.Equal(96000000L, EditorUndoStack.SnapshotBytes(6000, 4000))
        Assert.Equal(1920000000L, EditorUndoStack.SnapshotBytes(6000, 4000) * 20)
        Assert.Equal(0L, EditorUndoStack.SnapshotBytes(0, 4000))
    End Sub

    ' -------------------------------------------------------------------- redo ----
    '
    ' 126 Ф-2: the states an undo walks away from come back, share the budget with the
    ' undo side, and die the moment an edit commits. Undo and redo here are
    ' transactions - Undo(current)/Redo(current) take ownership of the picture being
    ' left - so the tests hand pictures over and dispose only what comes back.

    <Fact>
    Public Sub An_empty_history_offers_no_redo()
        Using history As New EditorUndoStack()
            Assert.False(history.CanRedo)
            Assert.Equal(0, history.RedoCount)
            Assert.Null(history.Redo(Nothing))
        End Using
    End Sub

    <Fact>
    Public Sub Undo_puts_the_state_it_leaves_on_the_redo_side()
        Using history As New EditorUndoStack()
            Using restored As Bitmap = NewPicture(6, 6, Color.Red)
                history.Push(restored)
            End Using

            Using leaving As Bitmap = NewPicture(6, 6, Color.Blue)
                Using history.Undo(leaving)
                End Using
                Assert.True(history.CanRedo)
                Assert.Equal(1, history.RedoCount)
            End Using
        End Using
    End Sub

    ''' <summary>The promise redo exists for: undo then redo lands on the same pixels the
    ''' eye last saw.</summary>
    <Fact>
    Public Sub Redo_restores_exactly_what_undo_took_away()
        Using history As New EditorUndoStack()
            Using pushed As Bitmap = NewPicture(4, 4, Color.Lime)
                history.Push(pushed)
            End Using

            ' The stack takes ownership of `leaving`; the redo transaction hands it back
            ' to the undo side, and what comes back is the state the user walked from.
            Dim leaving As Bitmap = NewPicture(4, 4, Color.Blue)
            Using history.Undo(leaving)
            End Using
            Assert.True(history.CanRedo)

            Using redone As Bitmap = history.Redo(leaving)
                Assert.Equal(Color.Blue.ToArgb(), redone.GetPixel(0, 0).ToArgb())
            End Using
            Assert.False(history.CanRedo)
            Assert.True(history.CanUndo)
        End Using
    End Sub

    <Fact>
    Public Sub Undo_and_redo_walk_inversely_over_several_steps()
        Using history As New EditorUndoStack()
            Dim tints As Color() = New Color() {Color.Red, Color.Lime, Color.Blue}
            For Each tint As Color In tints
                Using pushed As Bitmap = NewPicture(4, 4, tint)
                    history.Push(pushed)
                End Using
            Next

            Dim walking As Bitmap = NewPicture(4, 4, Color.FromArgb(255, 9, 9, 9))
            Dim colours_Seen As New List(Of Color)()
            While history.CanUndo
                walking = history.Undo(walking)
                colours_Seen.Add(walking.GetPixel(0, 0))
            End While
            ' Newest first: Blue, Lime, Red.
            Assert.Equal(tints.Reverse().Select(Function(c) c.ToArgb()).ToList(),
                         colours_Seen.Select(Function(c) c.ToArgb()).ToList())

            While history.CanRedo
                walking = history.Redo(walking)
            End While
            Assert.Equal(Color.FromArgb(255, 9, 9, 9).ToArgb(), walking.GetPixel(0, 0).ToArgb())
            walking.Dispose()
        End Using
    End Sub

    ''' <summary>A committed edit kills the redo stack - but at the window's CALL, not on
    ''' Push: a gesture that ends as a stray click must not wipe a redo it never earned.</summary>
    <Fact>
    Public Sub ClearRedo_is_what_kills_the_redo_side()
        Using history As New EditorUndoStack()
            Using pushed As Bitmap = NewPicture(4, 4, Color.Red)
                history.Push(pushed)
            End Using
            Using leaving As Bitmap = NewPicture(4, 4, Color.Lime)
                Using history.Undo(leaving)
                End Using
            End Using
            Assert.True(history.CanRedo)

            ' A stray click: pushed, popped back, no edit - redo survives.
            Using stray As Bitmap = NewPicture(4, 4, Color.White)
                history.Push(stray)
            End Using
            history.Pop().Dispose()
            Assert.True(history.CanRedo)

            ' The edit commits.
            history.ClearRedo()
            Assert.False(history.CanRedo)
            Assert.Equal(0L, history.ByteSize)
        End Using
    End Sub

    <Fact>
    Public Sub Clearing_empties_both_sides()
        Using history As New EditorUndoStack()
            Using pushed As Bitmap = NewPicture(8, 8, Color.Red)
                history.Push(pushed)
            End Using
            Using leaving As Bitmap = NewPicture(8, 8, Color.Lime)
                Using history.Undo(leaving)
                End Using
            End Using

            history.Clear()

            Assert.Equal(0, history.Count)
            Assert.Equal(0, history.RedoCount)
            Assert.Equal(0L, history.ByteSize)
            Assert.False(history.CanUndo)
            Assert.False(history.CanRedo)
        End Using
    End Sub

    ''' <summary>Q126-1: the two stacks share one budget. The step the user just walked
    ''' back to is the newest redo and is never dropped - the oldest redo state goes
    ''' first, and the drop is reported.</summary>
    <Fact>
    Public Sub The_redo_side_shares_the_byte_budget()
        ' Room for three 20x20 snapshots and not a byte more.
        Dim budget As Long = EditorUndoStack.SnapshotBytes(20, 20) * 3

        Using history As New EditorUndoStack(maxSteps:=EditorUndoStack.Default_Max_Steps, maxBytes:=budget)
            For index As Integer = 0 To 1
                Using pushed As Bitmap = NewPicture(20, 20, Color.FromArgb(255, index, 0, 0))
                    history.Push(pushed)
                End Using
            Next

            ' First undo: a 40x40 state moves onto the redo side - over the budget
            ' together with the undo snapshot, but it is the newest redo, so it stays.
            Dim first As Bitmap = NewPicture(40, 40, Color.Lime)
            Using history.Undo(first)
            End Using
            Assert.Equal(1, history.RedoCount)
            Assert.Equal(EditorUndoStack.SnapshotBytes(20, 20) + EditorUndoStack.SnapshotBytes(40, 40), history.ByteSize)

            ' Second undo: a second 40x40 joins the redo side and breaches the budget -
            ' the OLDEST redo state goes instead.
            Dim second As Bitmap = NewPicture(40, 40, Color.Blue)
            Using history.Undo(second)
            End Using

            Assert.Equal(1, history.RedoCount)
            Assert.Equal(EditorUndoStack.SnapshotBytes(40, 40), history.ByteSize)
            Assert.True(history.StepsWereDropped)
        End Using
    End Sub

    <Fact>
    Public Sub The_step_limit_covers_both_sides_together()
        Using history As New EditorUndoStack(maxSteps:=3, maxBytes:=EditorUndoStack.Default_Max_Bytes)
            For index As Integer = 0 To 2
                Using pushed As Bitmap = NewPicture(4, 4, Color.FromArgb(255, index, 0, 0))
                    history.Push(pushed)
                End Using
            Next

            ' Two undos: one undo snapshot left, two redo states - three steps in total,
            ' at the cap but not over it.
            Using first As Bitmap = NewPicture(4, 4, Color.White)
                Using history.Undo(first)
                End Using
            End Using
            Using second As Bitmap = NewPicture(4, 4, Color.Black)
                Using history.Undo(second)
                End Using
            End Using

            Assert.Equal(1, history.Count)
            Assert.Equal(2, history.RedoCount)

            ' One more undo would breach the step cap - with nothing on the redo side to
            ' evict, the oldest UNDO snapshot goes instead, and the redo side keeps the
            ' states the user can still walk forward to.
            Using pushed As Bitmap = NewPicture(4, 4, Color.Gray)
                history.Push(pushed)
            End Using
            Assert.Equal(3, history.Count + history.RedoCount)
            Assert.Equal(2, history.RedoCount)
            Assert.True(history.StepsWereDropped)
        End Using
    End Sub

    <Fact>
    Public Sub Dropping_a_step_is_reported_and_sticks()
        Using history As New EditorUndoStack(maxSteps:=1, maxBytes:=EditorUndoStack.Default_Max_Bytes)
            Assert.False(history.StepsWereDropped)
            Using pushed As Bitmap = NewPicture(4, 4, Color.Red)
                history.Push(pushed)
            End Using
            Using pushed As Bitmap = NewPicture(4, 4, Color.Lime)
                history.Push(pushed)
            End Using
            Assert.True(history.StepsWereDropped)
            ' Sticky: the status line keeps saying so for the rest of the session.
            Using pushed As Bitmap = NewPicture(4, 4, Color.Blue)
                history.Push(pushed)
            End Using
            Assert.True(history.StepsWereDropped)
        End Using
    End Sub

End Class
#End If
