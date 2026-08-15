#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
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

End Class
#End If
