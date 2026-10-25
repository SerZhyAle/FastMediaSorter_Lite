#If Not NETFRAMEWORK Then
Option Strict On

' The history behind U, and the table that says what U can actually promise.
' 017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.5. Modern build only - the x86 viewer
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
            TryCast(entries(0), IDisposable)?.Dispose()
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
        For Each entry As T In entries
            TryCast(entry, IDisposable)?.Dispose()
        Next
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
    ''' <summary>A whole batch: every completed operation of it, taken back in REVERSE order
    ''' as one gesture (004_SPECIFICATION_BATCH_OPERATIONS_ENGINE PB-5).</summary>
    ReverseBatch
End Enum

''' <summary>One completed operation of a batch, as the undo needs to remember it.</summary>
Friend NotInheritable Class BatchUndoItem
    Public Property ManagerUndo As ManagerUndoData
    Public Property Kind As FileOpKind
    Public Property Source As String = ""
    Public Property Destination As String = ""
    ''' <summary>Where the file sat in the main window's list when it left it; -1 when it
    ''' was not in the list on screen.</summary>
    Public Property ListIndex As Integer = -1
    Public Property DeletedAtUtc As DateTime
    Public Property PermanentDeleteReason As PermanentReason
End Class

''' <summary>
''' A batch is ONE history entry, not N. The stack is bounded at fifty
''' (UndoPolicy.Max_Undo_Depth): 200 entries from one gesture would erase the whole history,
''' and U would then walk back one file at a time through something the user did once.
''' </summary>
Friend NotInheritable Class BatchUndoEntry
    Implements IDisposable
    Public Property IsManager As Boolean

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each item As BatchUndoItem In Items
            item.ManagerUndo?.Dispose()
        Next
    End Sub
    ''' <summary>The operations that actually completed, in the order they completed.</summary>
    Public ReadOnly Property Items As New List(Of BatchUndoItem)()
    ''' <summary>The generation of the main window's file list the batch started from (the
    ''' same stamp a single FileOp carries) - an undo touches the list only while it matches.</summary>
    Public Property ListGeneration As Integer
End Class

''' <summary>What U will do with a batch entry.</summary>
Friend NotInheritable Class BatchUndoPlan
    ''' <summary>The inverse operations, already in REVERSE order of the batch.</summary>
    Public ReadOnly Property Inverse As New List(Of BatchOp)()
    ''' <summary>Items that cannot come back: permanent deletions. Recorded on purpose so the
    ''' report can say "N deleted for good" instead of pretending they were never part of it.</summary>
    Public ReadOnly Property NotReturnable As New List(Of BatchUndoItem)()
End Class

Friend Module BatchUndoPlanner

    ''' <summary>
    ''' The batch taken back: every item reversed through the per-kind inverse that already
    ''' exists (move -> move back, copy -> delete the copy, recycled delete -> restore from
    ''' the bin), last first. Inverse kinds are never in an entry (UndoPolicy.IsRecorded), so
    ''' "an undo is never itself undoable" holds here as it does for a single file.
    ''' </summary>
    ''' <param name="resolveFreeName">For a restore: the name the file will come back under
    ''' when its old name has been taken meanwhile. Nothing means "the old name, as is".</param>
    Friend Function Build(entry As BatchUndoEntry, resolveFreeName As Func(Of String, String)) As BatchUndoPlan
        Dim plan As New BatchUndoPlan()
        If entry Is Nothing Then Return plan

        For i As Integer = entry.Items.Count - 1 To 0 Step -1
            Dim item As BatchUndoItem = entry.Items(i)
            If item.ManagerUndo IsNot Nothing Then
                plan.Inverse.Add(ManagerOperation.ForUndo(item.ManagerUndo))
                Continue For
            ElseIf entry.IsManager Then
                plan.NotReturnable.Add(item)
                Continue For
            End If
            Select Case UndoPolicy.PlanFor(item.Kind)
                Case UndoPlan.MoveBack
                    plan.Inverse.Add(New BatchOp With {
                        .Kind = FileOpKind.MoveUndo,
                        .Source = item.Destination,
                        .Destination = item.Source,
                        .ListIndex = item.ListIndex})
                Case UndoPlan.DeleteTheCopy
                    plan.Inverse.Add(New BatchOp With {
                        .Kind = FileOpKind.DeleteUndo,
                        .Source = item.Destination,
                        .OriginalSource = item.Source,
                        .ListIndex = item.ListIndex})
                Case UndoPlan.RestoreFromBin
                    plan.Inverse.Add(New BatchOp With {
                        .Kind = FileOpKind.RestoreFromBin,
                        .Source = item.Source,
                        .Destination = If(resolveFreeName Is Nothing, item.Source, resolveFreeName(item.Source)),
                        .DeletedAtUtc = item.DeletedAtUtc,
                        .ListIndex = item.ListIndex})
                Case UndoPlan.RefusePermanent
                    plan.NotReturnable.Add(item)
                Case Else
                    ' RenameBack never belongs to a batch; anything unlisted is not recorded.
            End Select
        Next
        Return plan
    End Function

End Module

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
            Case FileOpKind.Batch
                Return UndoPlan.ReverseBatch
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
            Case UndoPlan.MoveBack, UndoPlan.DeleteTheCopy, UndoPlan.RenameBack, UndoPlan.RestoreFromBin, UndoPlan.ReverseBatch
                Return True
            Case Else
                Return False
        End Select
    End Function

End Module
#End If
