#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' The selection behind the F3 panel's grid AND its details list
' (004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md PB-1, consumed by 005 PG-4).
'
' Pure: no control, no file system, no clock. Both renderers are views of this one object,
' which is the whole point - a gesture is selection logic exactly once, and "the selection
' survives the switch from grid to list" stops being a feature somebody has to remember.
' Modern build only: the x86 panel keeps its own PictureBox list (maintenance policy).

''' <summary>Where the keyboard focus goes next. The caller supplies the geometry (how many
''' columns, how many rows make a page); the model only does the arithmetic and the
''' selection that follows it.</summary>
Friend Enum FocusMove
    Left
    Right
    Up
    Down
    Home
    [End]
    PageUp
    PageDown
End Enum

''' <summary>
''' An ordered list of paths and a set of selected ones.
'''
''' Two rules that exist because they are the bugs this class prevents:
'''   1. Selection is by PATH, order is by LIST. An index-keyed selection breaks on every
'''      re-scan, and the re-scan is not rare here: it happens on a sort change, on every
'''      completed operation and when the main window's list changes under an open panel.
'''   2. SelectedPaths() is in LIST order, never in click order, so a report and an undo
'''      built from it are reproducible.
''' Paths compare case-insensitively: this is a Windows file system and the list and the
''' selection come from the same strings anyway.
''' </summary>
Friend NotInheritable Class SelectionModel

    Private ReadOnly paths As New List(Of String)()
    Private ReadOnly identity_Comparer As IEqualityComparer(Of String)
    Private ReadOnly position As Dictionary(Of String, Integer)
    Private ReadOnly chosen As HashSet(Of String)

    ''' <summary>The triage panel's model: paths, compared case-insensitively as ever.</summary>
    Friend Sub New()
        Me.New(Nothing)
    End Sub

    ''' <param name="comparer">How two identities are told apart. Nothing = case-insensitive, the triage default. The
    ''' file manager passes an ordinal comparer (257): its identities are opaque ids and two remote names that differ
    ''' only by case are two entries. EVERY set the model keeps - the list index, the selection, a rubber band's
    ''' baseline - uses this one comparer.</param>
    Friend Sub New(comparer As IEqualityComparer(Of String))
        identity_Comparer = If(comparer, StringComparer.OrdinalIgnoreCase)
        position = New Dictionary(Of String, Integer)(identity_Comparer)
        chosen = New HashSet(Of String)(identity_Comparer)
    End Sub

    ''' <summary>The path a Shift-range starts from. By path, not index, so a re-scan that
    ''' inserts a row above it does not move the range.</summary>
    Private anchor_Path As String = Nothing

    ''' <summary>The card or row the keyboard is on.</summary>
    Private focus_Path As String = Nothing

    ''' <summary>The selection as it stood when a rubber band began (for Ctrl+band).</summary>
    Private band_Baseline As HashSet(Of String) = Nothing

    ' ----------------------------------------------------------------- the list ----

    Friend ReadOnly Property Count As Integer
        Get
            Return paths.Count
        End Get
    End Property

    Friend ReadOnly Property SelectedCount As Integer
        Get
            Return chosen.Count
        End Get
    End Property

    Friend Function PathAt(index As Integer) As String
        If index < 0 OrElse index >= paths.Count Then Return Nothing
        Return paths(index)
    End Function

    ''' <summary>-1 when the path is not in the list.</summary>
    Friend Function IndexOf(path As String) As Integer
        If path Is Nothing Then Return -1
        Dim at As Integer
        If position.TryGetValue(path, at) Then Return at
        Return -1
    End Function

    ''' <summary>
    ''' The list was re-read. Selection, anchor and focus survive BY PATH; a path that is no
    ''' longer in the list is dropped. The list is copied - a later change to the caller's
    ''' collection must not edit the model behind its back.
    ''' </summary>
    Friend Sub SetItems(newPaths As IEnumerable(Of String))
        paths.Clear()
        position.Clear()
        If newPaths IsNot Nothing Then
            For Each p As String In newPaths
                If p Is Nothing Then Continue For
                ' A duplicate would make "the Nth row" ambiguous; the first one wins.
                If position.ContainsKey(p) Then Continue For
                position(p) = paths.Count
                paths.Add(p)
            Next
        End If

        chosen.RemoveWhere(Function(p) Not position.ContainsKey(p))
        If anchor_Path IsNot Nothing AndAlso Not position.ContainsKey(anchor_Path) Then anchor_Path = Nothing
        If focus_Path IsNot Nothing AndAlso Not position.ContainsKey(focus_Path) Then focus_Path = Nothing
        band_Baseline = Nothing
    End Sub

    ''' <summary>
    ''' Takes finished paths out of the list (a move or a delete completed). What is left
    ''' stays selected, and the focus moves to the row that took the removed one's place -
    ''' the same thing the main window does when the file on screen goes.
    ''' </summary>
    Friend Sub RemovePaths(gone As IEnumerable(Of String))
        If gone Is Nothing Then Return
        Dim drop As New HashSet(Of String)(identity_Comparer)
        For Each p As String In gone
            If p IsNot Nothing AndAlso position.ContainsKey(p) Then drop.Add(p)
        Next
        If drop.Count = 0 Then Return

        Dim focus_Index As Integer = IndexOf(focus_Path)
        Dim kept As New List(Of String)(paths.Count)
        Dim next_Focus As String = Nothing
        For i As Integer = 0 To paths.Count - 1
            If drop.Contains(paths(i)) Then Continue For
            kept.Add(paths(i))
            If next_Focus Is Nothing AndAlso focus_Index >= 0 AndAlso i > focus_Index Then next_Focus = paths(i)
        Next
        Dim anchor_Was_Dropped As Boolean = anchor_Path IsNot Nothing AndAlso drop.Contains(anchor_Path)
        Dim focus_Was_Dropped As Boolean = focus_Path IsNot Nothing AndAlso drop.Contains(focus_Path)

        SetItems(kept)
        If anchor_Was_Dropped Then anchor_Path = Nothing
        If focus_Was_Dropped Then
            ' The row after it, else the last one: never "nothing" while rows remain.
            focus_Path = If(next_Focus, If(paths.Count > 0, paths(paths.Count - 1), Nothing))
        End If
    End Sub

    ' ------------------------------------------------------------- the selection ----

    Friend Function IsSelected(index As Integer) As Boolean
        Dim p As String = PathAt(index)
        Return p IsNot Nothing AndAlso chosen.Contains(p)
    End Function

    Friend Function IsSelectedPath(path As String) As Boolean
        Return path IsNot Nothing AndAlso chosen.Contains(path)
    End Function

    Friend ReadOnly Property AnchorIndex As Integer
        Get
            Return IndexOf(anchor_Path)
        End Get
    End Property

    Friend ReadOnly Property FocusIndex As Integer
        Get
            Return IndexOf(focus_Path)
        End Get
    End Property

    ''' <summary>The selected paths in LIST order.</summary>
    Friend Function SelectedPaths() As List(Of String)
        Dim result As New List(Of String)(chosen.Count)
        If chosen.Count = 0 Then Return result
        For Each p As String In paths
            If chosen.Contains(p) Then result.Add(p)
        Next
        Return result
    End Function

    ''' <summary>A plain click: that one item, and the anchor moves to it.</summary>
    Friend Sub Click(index As Integer)
        Dim p As String = PathAt(index)
        If p Is Nothing Then Return
        chosen.Clear()
        chosen.Add(p)
        anchor_Path = p
        focus_Path = p
    End Sub

    ''' <summary>Ctrl+click: toggle that item, the anchor moves to it.</summary>
    Friend Sub CtrlClick(index As Integer)
        Dim p As String = PathAt(index)
        If p Is Nothing Then Return
        If Not chosen.Remove(p) Then chosen.Add(p)
        anchor_Path = p
        focus_Path = p
    End Sub

    ''' <summary>Shift+click: the closed range anchor..index REPLACES the selection; the
    ''' anchor stays. With no anchor yet it behaves like a plain click.</summary>
    Friend Sub ShiftClick(index As Integer)
        If PathAt(index) Is Nothing Then Return
        Dim from As Integer = AnchorIndex
        If from < 0 Then
            Click(index)
            Return
        End If
        chosen.Clear()
        AddRange(from, index)
        focus_Path = paths(index)
    End Sub

    ''' <summary>Ctrl+Shift+click: the range is ADDED to what is already selected.</summary>
    Friend Sub CtrlShiftClick(index As Integer)
        If PathAt(index) Is Nothing Then Return
        Dim from As Integer = AnchorIndex
        If from < 0 Then
            CtrlClick(index)
            Return
        End If
        AddRange(from, index)
        focus_Path = paths(index)
    End Sub

    Friend Sub SelectAll()
        For Each p As String In paths
            chosen.Add(p)
        Next
    End Sub

    Friend Sub SelectNone()
        chosen.Clear()
    End Sub

    ''' <summary>A different directory (257): nothing is selected, and the anchor and the focus go too, so nothing of
    ''' the old location leaks into the new one even if an identity happens to repeat.</summary>
    Friend Sub Reset()
        chosen.Clear()
        anchor_Path = Nothing
        focus_Path = Nothing
        band_Baseline = Nothing
    End Sub

    Friend Sub InvertSelection()
        Dim flipped As New HashSet(Of String)(identity_Comparer)
        For Each p As String In paths
            If Not chosen.Contains(p) Then flipped.Add(p)
        Next
        chosen.Clear()
        For Each p As String In flipped
            chosen.Add(p)
        Next
    End Sub

    ''' <summary>Space: toggle whatever has the keyboard focus.</summary>
    Friend Sub ToggleFocused()
        Dim at As Integer = FocusIndex
        If at < 0 Then Return
        CtrlClick(at)
    End Sub

    Private Sub AddRange(a As Integer, b As Integer)
        Dim lo As Integer = Math.Min(a, b)
        Dim hi As Integer = Math.Max(a, b)
        For i As Integer = lo To hi
            chosen.Add(paths(i))
        Next
    End Sub

    ''' <summary>
    ''' Arrow, Home/End and Page keys. Without Shift the selection becomes the focused item
    ''' (and the anchor moves); with Shift it becomes the range anchor..focus.
    ''' </summary>
    ''' <param name="columns">Cards per row in the grid; 1 in the list.</param>
    ''' <param name="pageRows">Rows that fit in the view, for Page Up/Down.</param>
    Friend Sub MoveFocus(move As FocusMove, shift As Boolean, columns As Integer, pageRows As Integer)
        If paths.Count = 0 Then Return
        Dim cols As Integer = Math.Max(1, columns)
        Dim page As Integer = Math.Max(1, pageRows) * cols

        Dim from As Integer = FocusIndex
        Dim target As Integer
        If from < 0 Then
            ' Nowhere yet: the first key press lands on the first item, whatever it was.
            target = 0
        Else
            Select Case move
                Case FocusMove.Left : target = from - 1
                Case FocusMove.Right : target = from + 1
                Case FocusMove.Up : target = from - cols
                Case FocusMove.Down : target = from + cols
                Case FocusMove.Home : target = 0
                Case FocusMove.End : target = paths.Count - 1
                Case FocusMove.PageUp : target = from - page
                Case Else : target = from + page
            End Select
        End If
        target = Math.Max(0, Math.Min(paths.Count - 1, target))

        If shift Then
            If AnchorIndex < 0 Then anchor_Path = paths(If(from >= 0, from, target))
            chosen.Clear()
            AddRange(AnchorIndex, target)
            focus_Path = paths(target)
        Else
            Click(target)
        End If
    End Sub

    ' ---------------------------------------------------------------- the band ----

    ''' <summary>A rubber band starts. With <paramref name="additive"/> (Ctrl held) the
    ''' selection as it is now is kept underneath the band.</summary>
    Friend Sub BeginBand(additive As Boolean)
        band_Baseline = If(additive,
                           New HashSet(Of String)(chosen, identity_Comparer),
                           New HashSet(Of String)(identity_Comparer))
    End Sub

    ''' <summary>The band now covers exactly these rows: the live preview, redone from the
    ''' baseline each time so a band that shrinks un-selects what it let go of.</summary>
    Friend Sub UpdateBand(covered As IEnumerable(Of Integer))
        If band_Baseline Is Nothing Then BeginBand(False)
        chosen.Clear()
        For Each p As String In band_Baseline
            chosen.Add(p)
        Next
        Dim first As Integer = -1
        If covered IsNot Nothing Then
            For Each i As Integer In covered
                Dim p As String = PathAt(i)
                If p Is Nothing Then Continue For
                chosen.Add(p)
                If first < 0 OrElse i < first Then first = i
            Next
        End If
        If first >= 0 Then
            anchor_Path = paths(first)
            focus_Path = paths(first)
        End If
    End Sub

    Friend Sub EndBand()
        band_Baseline = Nothing
    End Sub

End Class
#End If
