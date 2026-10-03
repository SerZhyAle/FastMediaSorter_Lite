#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' 004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md §9 points 1-2, and the gestures of
''' 005 PG-4 that delegate to the selection model. Everything here is a function of a list of
''' paths and a few indices - no control, no mouse - which is the point of having a model.
'''
''' Modern-only, like the engine: on the net48 leg this file and BatchSelection.vb both compile
''' to nothing (the x86 panel keeps its own PictureBox list).
''' </summary>
Public Class BatchSelectionTests

    Private Shared Function Names(count As Integer) As List(Of String)
        Return Enumerable.Range(0, count).Select(Function(i) "D:\in\f" & i.ToString("00") & ".jpg").ToList()
    End Function

    Private Shared Function Model(count As Integer) As SelectionModel
        Dim m As New SelectionModel()
        m.SetItems(Names(count))
        Return m
    End Function

    Private Shared Function Indices(m As SelectionModel) As List(Of Integer)
        Return m.SelectedPaths().Select(Function(p) m.IndexOf(p)).ToList()
    End Function

    ' ------------------------------------------------------------ click, range ----

    <Fact>
    Public Sub Click_selects_exactly_one_item_and_moves_the_anchor()
        Dim m As SelectionModel = Model(10)
        m.Click(2)
        m.Click(5)
        Assert.Equal(New Integer() {5}, Indices(m).ToArray())
        Assert.Equal(5, m.AnchorIndex)
    End Sub

    ''' <summary>Acceptance 1: ShiftClick after Click selects the inclusive range - in both directions.</summary>
    <Fact>
    Public Sub ShiftClick_selects_the_inclusive_range_in_both_directions()
        Dim m As SelectionModel = Model(10)
        m.Click(3)
        m.ShiftClick(6)
        Assert.Equal(New Integer() {3, 4, 5, 6}, Indices(m).ToArray())
        Assert.Equal(3, m.AnchorIndex)   ' the anchor stays

        m.Click(6)
        m.ShiftClick(3)
        Assert.Equal(New Integer() {3, 4, 5, 6}, Indices(m).ToArray())
        Assert.Equal(6, m.AnchorIndex)
    End Sub

    <Fact>
    Public Sub ShiftClick_replaces_the_previous_selection_and_a_second_one_pivots_on_the_same_anchor()
        Dim m As SelectionModel = Model(10)
        m.Click(2)
        m.ShiftClick(4)
        m.ShiftClick(7)
        Assert.Equal(New Integer() {2, 3, 4, 5, 6, 7}, Indices(m).ToArray())
        m.ShiftClick(0)
        Assert.Equal(New Integer() {0, 1, 2}, Indices(m).ToArray())
    End Sub

    ''' <summary>Acceptance 1: CtrlShiftClick adds the range without dropping the earlier selection.</summary>
    <Fact>
    Public Sub CtrlShiftClick_adds_the_range_and_keeps_the_earlier_selection()
        Dim m As SelectionModel = Model(20)
        m.Click(1)
        m.CtrlClick(10)           ' anchor is now 10
        m.CtrlShiftClick(13)
        Assert.Equal(New Integer() {1, 10, 11, 12, 13}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub CtrlClick_toggles_and_moves_the_anchor()
        Dim m As SelectionModel = Model(6)
        m.Click(0)
        m.CtrlClick(3)
        Assert.Equal(New Integer() {0, 3}, Indices(m).ToArray())
        m.CtrlClick(3)
        Assert.Equal(New Integer() {0}, Indices(m).ToArray())
        Assert.Equal(3, m.AnchorIndex)
    End Sub

    <Fact>
    Public Sub ShiftClick_with_no_anchor_behaves_like_a_click()
        Dim m As SelectionModel = Model(6)
        m.ShiftClick(4)
        Assert.Equal(New Integer() {4}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub Out_of_range_indices_are_ignored()
        Dim m As SelectionModel = Model(3)
        m.Click(1)
        m.Click(-1)
        m.Click(3)
        m.CtrlClick(99)
        m.ShiftClick(99)
        Assert.Equal(New Integer() {1}, Indices(m).ToArray())
    End Sub

    ' -------------------------------------------------------- all, none, invert ----

    <Fact>
    Public Sub SelectAll_SelectNone_and_Invert()
        Dim m As SelectionModel = Model(5)
        m.SelectAll()
        Assert.Equal(5, m.SelectedCount)
        m.SelectNone()
        Assert.Equal(0, m.SelectedCount)
        m.Click(1)
        m.CtrlClick(3)
        m.InvertSelection()
        Assert.Equal(New Integer() {0, 2, 4}, Indices(m).ToArray())
    End Sub

    ' ------------------------------------------------------------- re-scan -------

    ''' <summary>Acceptance 2: a re-ordered list keeps the same PATHS selected and returns them in the new LIST order.</summary>
    <Fact>
    Public Sub SetItems_keeps_selection_by_path_and_returns_it_in_the_new_list_order()
        Dim m As SelectionModel = Model(6)
        m.Click(1)
        m.CtrlClick(4)

        Dim reordered As List(Of String) = Names(6)
        reordered.Reverse()
        m.SetItems(reordered)

        Dim expected As New List(Of String) From {"D:\in\f04.jpg", "D:\in\f01.jpg"}
        Assert.Equal(expected, m.SelectedPaths())
        Assert.Equal(New Integer() {1, 4}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub A_file_inserted_above_does_not_shift_what_is_selected()
        Dim m As SelectionModel = Model(4)
        m.Click(2)
        Dim grown As List(Of String) = Names(4)
        grown.Insert(0, "D:\in\new.jpg")
        m.SetItems(grown)
        Assert.Equal(New List(Of String) From {"D:\in\f02.jpg"}, m.SelectedPaths())
        Assert.Equal(3, m.IndexOf("D:\in\f02.jpg"))
        Assert.Equal(3, m.AnchorIndex)   ' the anchor followed the path, not the index
    End Sub

    <Fact>
    Public Sub SetItems_drops_selected_paths_that_are_gone()
        Dim m As SelectionModel = Model(5)
        m.SelectAll()
        m.SetItems(Names(5).Take(3))
        Assert.Equal(3, m.SelectedCount)
        Assert.Equal(3, m.Count)
    End Sub

    <Fact>
    Public Sub SelectedPaths_are_in_list_order_not_click_order()
        Dim m As SelectionModel = Model(8)
        m.Click(6)
        m.CtrlClick(1)
        m.CtrlClick(4)
        Assert.Equal(New Integer() {1, 4, 6}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub Paths_compare_case_insensitively()
        Dim m As New SelectionModel()
        m.SetItems(New List(Of String) From {"D:\In\A.jpg", "D:\In\B.jpg"})
        m.Click(0)
        Assert.True(m.IsSelectedPath("d:\in\a.JPG"))
        Assert.Equal(1, m.IndexOf("D:\IN\b.jpg"))
    End Sub

    ''' <summary>Finished operations leave the list; the rest stays selected and the focus
    ''' moves to the row that took the removed one's place.</summary>
    <Fact>
    Public Sub RemovePaths_keeps_the_rest_selected_and_moves_the_focus_on()
        Dim m As SelectionModel = Model(6)
        m.Click(2)
        m.CtrlClick(4)
        m.RemovePaths(New String() {"D:\in\f02.jpg"})
        Assert.Equal(5, m.Count)
        Assert.Equal(New List(Of String) From {"D:\in\f04.jpg"}, m.SelectedPaths())
        ' The focus was on f04 (last CtrlClick) and was not removed.
        Assert.Equal(m.IndexOf("D:\in\f04.jpg"), m.FocusIndex)

        Dim n As SelectionModel = Model(6)
        n.Click(2)
        n.RemovePaths(New String() {"D:\in\f02.jpg"})
        Assert.Equal("D:\in\f03.jpg", n.PathAt(n.FocusIndex))
        Assert.Equal(0, n.SelectedCount)
    End Sub

    ' --------------------------------------------------------------- keyboard ----

    <Fact>
    Public Sub Arrows_move_the_focus_and_select_it()
        Dim m As SelectionModel = Model(12)
        m.Click(5)
        m.MoveFocus(FocusMove.Right, shift:=False, columns:=4, pageRows:=3)
        Assert.Equal(New Integer() {6}, Indices(m).ToArray())
        m.MoveFocus(FocusMove.Down, False, 4, 3)
        Assert.Equal(New Integer() {10}, Indices(m).ToArray())
        m.MoveFocus(FocusMove.Left, False, 4, 3)
        m.MoveFocus(FocusMove.Up, False, 4, 3)
        Assert.Equal(New Integer() {5}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub Shift_arrows_extend_the_range_from_the_anchor()
        Dim m As SelectionModel = Model(12)
        m.Click(4)
        m.MoveFocus(FocusMove.Right, True, 4, 3)
        m.MoveFocus(FocusMove.Right, True, 4, 3)
        Assert.Equal(New Integer() {4, 5, 6}, Indices(m).ToArray())
        m.MoveFocus(FocusMove.Left, True, 4, 3)
        Assert.Equal(New Integer() {4, 5}, Indices(m).ToArray())
        m.MoveFocus(FocusMove.Down, True, 4, 3)
        Assert.Equal(New Integer() {4, 5, 6, 7, 8, 9}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub Home_End_and_pages_clamp_to_the_list()
        Dim m As SelectionModel = Model(10)
        m.Click(5)
        m.MoveFocus(FocusMove.Home, False, 3, 2)
        Assert.Equal(0, m.FocusIndex)
        m.MoveFocus(FocusMove.End, False, 3, 2)
        Assert.Equal(9, m.FocusIndex)
        m.MoveFocus(FocusMove.PageDown, False, 3, 2)
        Assert.Equal(9, m.FocusIndex)            ' clamped, not wrapped
        m.MoveFocus(FocusMove.PageUp, False, 3, 2)
        Assert.Equal(3, m.FocusIndex)            ' 2 rows x 3 columns up
        m.MoveFocus(FocusMove.Home, True, 3, 2)
        Assert.Equal(New Integer() {0, 1, 2, 3}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub The_first_key_press_with_no_focus_lands_on_the_first_item()
        Dim m As SelectionModel = Model(5)
        m.MoveFocus(FocusMove.Down, False, 3, 2)
        Assert.Equal(New Integer() {0}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub Space_toggles_the_focused_item()
        Dim m As SelectionModel = Model(5)
        m.Click(2)
        m.ToggleFocused()
        Assert.Equal(0, m.SelectedCount)
        m.ToggleFocused()
        Assert.Equal(New Integer() {2}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub An_empty_list_survives_every_operation()
        Dim m As New SelectionModel()
        m.SetItems(Nothing)
        m.Click(0)
        m.ShiftClick(2)
        m.MoveFocus(FocusMove.End, True, 3, 2)
        m.SelectAll()
        m.InvertSelection()
        m.ToggleFocused()
        m.RemovePaths(New String() {"x"})
        Assert.Equal(0, m.Count)
        Assert.Empty(m.SelectedPaths())
    End Sub

    ' ------------------------------------------------------------ rubber band ----

    <Fact>
    Public Sub A_band_previews_live_and_a_shrinking_band_lets_go()
        Dim m As SelectionModel = Model(10)
        m.BeginBand(additive:=False)
        m.UpdateBand(New Integer() {2, 3, 4, 5})
        Assert.Equal(New Integer() {2, 3, 4, 5}, Indices(m).ToArray())
        m.UpdateBand(New Integer() {2, 3})
        Assert.Equal(New Integer() {2, 3}, Indices(m).ToArray())
        m.EndBand()
        Assert.Equal(New Integer() {2, 3}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub A_Ctrl_band_adds_to_what_was_selected_before_it_began()
        Dim m As SelectionModel = Model(10)
        m.Click(0)
        m.CtrlClick(9)
        m.BeginBand(additive:=True)
        m.UpdateBand(New Integer() {4, 5})
        Assert.Equal(New Integer() {0, 4, 5, 9}, Indices(m).ToArray())
        m.UpdateBand(New Integer() {})
        Assert.Equal(New Integer() {0, 9}, Indices(m).ToArray())
    End Sub

    <Fact>
    Public Sub A_plain_band_replaces_the_selection()
        Dim m As SelectionModel = Model(10)
        m.Click(0)
        m.BeginBand(additive:=False)
        m.UpdateBand(New Integer() {6, 7})
        Assert.Equal(New Integer() {6, 7}, Indices(m).ToArray())
    End Sub

End Class
#End If
