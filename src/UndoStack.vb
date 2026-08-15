#If Not NETFRAMEWORK Then
Option Strict On

' The history behind U, and the table that says what U can actually promise.
' SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.5. Modern build only - the x86 viewer
' keeps its three history fields and its one-operation-deep undo (invariant 10).
'
' Both halves are here rather than inside Main_Form for the same reason ZoomMath and
' DeletePolicy are: they are pure, and they are exactly the sort of rule that rots. "Does
' the fiftieth operation really evict the first" and "can an undo be undone" are questions
' for a test, not for whoever adds the next FileOpKind.

''' <summary>
''' A bounded history, oldest dropped first.
'''
''' A LIST, NOT A STACK, and that is the correction this type exists for: the design it
''' comes from (S8 §4) specifies Stack(Of FileOp) with max_Undo_Depth = 50, and a Stack
''' cannot drop its oldest element - the cap as written is unreachable, so the "history"
''' would have grown for the whole session. Push is Add plus RemoveAt(0) past the cap.
''' </summary>
Friend NotInheritable Class UndoStack(Of T As Class)

    Private ReadOnly entries As New List(Of T)()
    Private ReadOnly max_Depth As Integer

    Friend Sub New(maxDepth As Integer)
        ' A depth below one would make Push a silent no-op, which is the one behaviour a
        ' history must never have.
        max_Depth = Math.Max(1, maxDepth)
    End Sub

    Friend ReadOnly Property Count As Integer
        Get
            Return entries.Count
        End Get
    End Property

    Friend ReadOnly Property IsEmpty As Boolean
        Get
            Return entries.Count = 0
        End Get
    End Property

    ''' <summary>Records one completed operation. Nothing is never recorded - an empty slot
    ''' in the history would come back out of Pop as "there is history" and then do nothing.</summary>
    Friend Sub Push(entry As T)
        If entry Is Nothing Then Return
        entries.Add(entry)
        While entries.Count > max_Depth
            entries.RemoveAt(0)
        End While
    End Sub

    ''' <summary>The most recent entry, removed. Nothing when the history is empty.</summary>
    Friend Function Pop() As T
        If entries.Count = 0 Then Return Nothing
        Dim last As T = entries(entries.Count - 1)
        entries.RemoveAt(entries.Count - 1)
        Return last
    End Function

    ''' <summary>The most recent entry, left in place - for asking "what would U do?"
    ''' without committing to it.</summary>
    Friend Function Peek() As T
        If entries.Count = 0 Then Return Nothing
        Return entries(entries.Count - 1)
    End Function

    Friend Sub Clear()
        entries.Clear()
    End Sub

End Class

''' <summary>What U does with a recorded operation.</summary>
Friend Enum UndoPlan
    ''' <summary>Not recorded at all. Every inverse operation answers this, which is what
    ''' makes "an undo is never itself undoable" structural rather than a flag.</summary>
    None
    ''' <summary>A move: put the file back where it came from.</summary>
    MoveBack
    ''' <summary>A copy: delete the copy that was made.</summary>
    DeleteTheCopy
    ''' <summary>A rename: the old name back.</summary>
    RenameBack
    ''' <summary>A recycled delete: bring the file back out of the Recycle Bin.</summary>
    RestoreFromBin
    ''' <summary>A permanent delete. Recorded ON PURPOSE, precisely so that U can say what
    ''' happened to that file instead of "there is no history" - the file is not missing
    ''' from the history, it is beyond returning, and those are different sentences.</summary>
    RefusePermanent
End Enum

Friend Module UndoPolicy

    ''' <summary>How deep the history goes. Fifty operations is the number the design
    ''' carried; what matters more is that it is bounded at all, since every entry holds
    ''' paths for the whole session.</summary>
    Friend Const Max_Undo_Depth As Integer = 50

    ''' <summary>
    ''' The whole table, in one place. A new FileOpKind that is not listed here is silently
    ''' NOT recorded, which is the safe default: an operation nobody taught U to reverse
    ''' must not sit in the history pretending it can be.
    ''' </summary>
    Friend Function PlanFor(kind As FileOpKind) As UndoPlan
        Select Case kind
            Case FileOpKind.Move
                Return UndoPlan.MoveBack
            Case FileOpKind.Copy
                Return UndoPlan.DeleteTheCopy
            Case FileOpKind.Rename
                Return UndoPlan.RenameBack
            Case FileOpKind.RecycleDelete
                Return UndoPlan.RestoreFromBin
            Case FileOpKind.Delete
                Return UndoPlan.RefusePermanent
            Case Else
                ' DeleteUndo, MoveUndo, RenameUndo, RestoreFromBin - the inverses themselves.
                Return UndoPlan.None
        End Select
    End Function

    ''' <summary>Is this worth remembering? Everything with a plan is, including the
    ''' permanent delete whose "plan" is to explain itself.</summary>
    Friend Function IsRecorded(kind As FileOpKind) As Boolean
        Return PlanFor(kind) <> UndoPlan.None
    End Function

    ''' <summary>Does this plan actually put a file back? RefusePermanent does not, and the
    ''' caller has to know the difference before it touches the file list.</summary>
    Friend Function RestoresAFile(plan As UndoPlan) As Boolean
        Select Case plan
            Case UndoPlan.MoveBack, UndoPlan.DeleteTheCopy, UndoPlan.RenameBack, UndoPlan.RestoreFromBin
                Return True
            Case Else
                Return False
        End Select
    End Function

End Module
#End If
