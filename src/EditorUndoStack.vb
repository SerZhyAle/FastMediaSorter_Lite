#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing

''' <summary>
''' The editor's own undo history (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §8).
'''
''' <b>Snapshots, not a journal of operations.</b> A journal has to be replayed to be
''' useful, and replaying is where a drawing editor grows bugs: every tool would need an
''' exact inverse, and the twentieth replay would have to land on the same pixels as the
''' first. A snapshot is <c>New Bitmap(image)</c> and it cannot be wrong.
'''
''' The price of that honesty is memory, which is why this class exists at all rather
''' than a plain <c>Stack(Of Bitmap)</c>: <b>twenty snapshots of a 6000x4000 photo are
''' 1.9 GB</b>, so the editor would die of OutOfMemory on exactly the machine where big
''' photos are sorted. The budget is therefore two limits at once - a step count and a
''' byte total - and the older half is dropped when either is exceeded.
'''
''' Not connected to the viewer's own <c>Undo()</c> (the <c>U</c> key), which undoes a
''' file operation - a different history of a different thing (§4, invariant 9).
'''
''' Modern-only, like the editor: the whole file compiles to nothing in the x86 viewer.
''' </summary>
Friend NotInheritable Class EditorUndoStack
    Implements IDisposable

    ''' <summary>Twenty steps back is far more than a "circle it and save" edit ever
    ''' needs, and it is the number the specification names.</summary>
    Friend Const Default_Max_Steps As Integer = 20

    ''' <summary>~512 MB of snapshots. Reached at 20 steps only once a picture is past
    ''' 6 megapixels; below that the step count is the binding limit, which is the
    ''' intended order.</summary>
    Friend Const Default_Max_Bytes As Long = 512L * 1024L * 1024L

    ''' <summary>What one snapshot costs: the copy is 32bpp regardless of what the
    ''' original was, so four bytes a pixel is the real figure, not an estimate.</summary>
    Private Const Bytes_Per_Pixel As Long = 4

    Private ReadOnly max_Steps As Integer
    Private ReadOnly max_Bytes As Long

    ''' <summary>Oldest first, so eviction takes from the front and undo takes from the
    ''' back. A List is enough at twenty entries and keeps the byte accounting readable.</summary>
    Private ReadOnly snapshots As New List(Of Bitmap)()

    Private total_Bytes As Long

    Friend Sub New()
        Me.New(Default_Max_Steps, Default_Max_Bytes)
    End Sub

    ''' <summary>The limits are arguments so a test can prove eviction with small
    ''' pictures instead of allocating the gigabyte the real budget describes.</summary>
    Friend Sub New(maxSteps As Integer, maxBytes As Long)
        max_Steps = Math.Max(1, maxSteps)
        max_Bytes = Math.Max(1, maxBytes)
    End Sub

    Friend ReadOnly Property Count As Integer
        Get
            Return snapshots.Count
        End Get
    End Property

    ''' <summary>What the history is holding right now. Exposed for the test that proves
    ''' the budget is real; the editor itself only asks <see cref="CanUndo"/>.</summary>
    Friend ReadOnly Property ByteSize As Long
        Get
            Return total_Bytes
        End Get
    End Property

    Friend ReadOnly Property CanUndo As Boolean
        Get
            Return snapshots.Count > 0
        End Get
    End Property

    ''' <summary>
    ''' Remembers the picture as it is now. Called on MouseDown - <b>before</b> the first
    ''' pixel of the gesture - so the state that comes back is the one the eye last saw.
    '''
    ''' Copying can itself run out of memory on a large photo. When it does the history is
    ''' <b>cleared</b> rather than left as it was: leaving it would put an older state on
    ''' top, and the next "Undo" would silently throw away more than the one step it
    ''' promises. No undo is honest; an undo that jumps two edits back is not.
    ''' </summary>
    Friend Sub Push(image As Bitmap)
        If image Is Nothing Then Return

        Dim snapshot As Bitmap
        Try
            snapshot = New Bitmap(image)
        Catch ex As Exception
            AppFileLogger.LogException("Image editor: taking an undo snapshot", ex)
            Clear()
            Return
        End Try

        snapshots.Add(snapshot)
        total_Bytes += SnapshotBytes(snapshot.Width, snapshot.Height)
        EvictWhileOverBudget()
    End Sub

    ''' <summary>
    ''' The most recent snapshot, removed from the history. <b>The caller owns the bitmap
    ''' it gets</b> and must dispose it once it has been swapped in - the alternative is
    ''' this class holding a reference to pixels the editor is actively drawing on.
    ''' Nothing when there is nothing to undo.
    ''' </summary>
    Friend Function Pop() As Bitmap
        If snapshots.Count = 0 Then Return Nothing

        Dim last As Integer = snapshots.Count - 1
        Dim snapshot As Bitmap = snapshots(last)
        snapshots.RemoveAt(last)
        total_Bytes -= SnapshotBytes(snapshot.Width, snapshot.Height)
        If total_Bytes < 0 Then total_Bytes = 0
        Return snapshot
    End Function

    Friend Sub Clear()
        For Each snapshot As Bitmap In snapshots
            snapshot.Dispose()
        Next
        snapshots.Clear()
        total_Bytes = 0
    End Sub

    ''' <summary>What a snapshot of this size costs. Public to the tests for the same
    ''' reason the budget is: the arithmetic is the whole point of the class.</summary>
    Friend Shared Function SnapshotBytes(width As Integer, height As Integer) As Long
        Return CLng(Math.Max(0, width)) * Math.Max(0, height) * Bytes_Per_Pixel
    End Function

    ''' <summary>
    ''' Drops the oldest snapshots until both limits are met.
    '''
    ''' <b>The newest is never dropped</b>, even when it alone is over the byte budget.
    ''' On a picture that big the alternative is an editor with no undo at all, and the
    ''' live bitmap already costs the same amount - one step back is worth the second copy.
    ''' </summary>
    Private Sub EvictWhileOverBudget()
        While snapshots.Count > 1 AndAlso (snapshots.Count > max_Steps OrElse total_Bytes > max_Bytes)
            Dim oldest As Bitmap = snapshots(0)
            snapshots.RemoveAt(0)
            total_Bytes -= SnapshotBytes(oldest.Width, oldest.Height)
            oldest.Dispose()
        End While
        If total_Bytes < 0 Then total_Bytes = 0
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Clear()
    End Sub

End Class
#End If
