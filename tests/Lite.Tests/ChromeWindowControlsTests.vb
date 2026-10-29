#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.Drawing
Imports Xunit

''' <summary>
''' The pure rules behind the borderless viewer's window-controls panel (188): the panel's own
''' pin inside the shared pin algebra, the corner panel's claim on the top rows' band, the floor
''' width below which a row must wrap, and the double-click rule of the drag grip.
''' </summary>
Public Class ChromeWindowControlsTests

    ' --- the pin algebra (188 §4) ---------------------------------------------------------------

    <Fact>
    Public Sub The_controls_panel_is_an_explicit_surface_not_the_status_fallback()
        ' Every switch on but the panel's own: the name must answer from ITS switch, never fall
        ' through to the status panel's.
        Dim state As New ChromeAutoHide.ChromePinState With {.StatusPinned = True}
        Assert.False(ChromeAutoHide.OwnPin(state, "chrome_WindowControls"))
        Assert.False(ChromeAutoHide.PanelPinned(state, "chrome_WindowControls"))
        state.WindowControlsPinned = True
        Assert.True(ChromeAutoHide.OwnPin(state, "chrome_WindowControls"))
        Assert.True(ChromeAutoHide.PanelPinned(state, "chrome_WindowControls"))
    End Sub

    <Fact>
    Public Sub The_global_switch_holds_the_controls_panel_up_like_any_surface()
        Dim state As New ChromeAutoHide.ChromePinState With {.GlobalPinned = True}
        Assert.True(ChromeAutoHide.PanelPinned(state, "chrome_WindowControls"))
    End Sub

    <Fact>
    Public Sub Unpinning_another_surface_while_global_keeps_the_controls_panel_pinned()
        Dim state As New ChromeAutoHide.ChromePinState With {.GlobalPinned = True}
        state = ChromeAutoHide.TogglePin(state, "chrome_Row1")
        ' The split turns the all-switch into every surface's own pin minus the clicked one -
        ' the controls panel included, so its effective pinned state survives the click.
        Assert.False(state.GlobalPinned)
        Assert.True(state.WindowControlsPinned)
        Assert.True(ChromeAutoHide.PanelPinned(state, "chrome_WindowControls"))
        Assert.False(ChromeAutoHide.PanelPinned(state, "chrome_Row1"))
    End Sub

    <Fact>
    Public Sub Unpinning_the_controls_panel_itself_leaves_the_other_pins_alone()
        Dim state As New ChromeAutoHide.ChromePinState With {.GlobalPinned = True}
        state = ChromeAutoHide.TogglePin(state, "chrome_WindowControls")
        Assert.False(state.WindowControlsPinned)
        Assert.False(ChromeAutoHide.PanelPinned(state, "chrome_WindowControls"))
        Assert.True(state.Row1Pinned)
        Assert.True(state.Row2Pinned)
        Assert.True(state.StatusPinned)
        ' Pinning it back is its own switch again.
        state = ChromeAutoHide.TogglePin(state, "chrome_WindowControls")
        Assert.True(ChromeAutoHide.PanelPinned(state, "chrome_WindowControls"))
    End Sub

    ' --- the floor width of a row (188 §3.5) ------------------------------------------------------

    Private Shared Function Slot(id As String, width As Integer, Optional minWidth As Integer = -1,
                                 Optional pinned As Boolean = False) As ChromeSlot
        Return New ChromeSlot(id, width, minWidth, pinned)
    End Function

    <Fact>
    Public Sub Pinned_core_floor_keeps_only_the_pinned_items_and_reserves_the_more_button()
        Dim slots As ChromeSlot() = {
            Slot("core", 120, minWidth:=120, pinned:=True),
            Slot("flex", 300, minWidth:=80),
            Slot("extra1", 40), Slot("extra2", 40)}
        ' The two extras collapse into the menu, whose room is then reserved; the flexible item
        ' counts at its minimum only if it survives - it does not, so it contributes nothing.
        Assert.Equal(120 + 30 + 4, ChromeLayout.PinnedCoreWidth(slots, 4, 30))
    End Sub

    <Fact>
    Public Sub Pinned_core_floor_includes_a_pinned_flexible_item_at_its_minimum()
        Dim slots As ChromeSlot() = {
            Slot("path", 320, minWidth:=120, pinned:=True),
            Slot("extra", 40)}
        Assert.Equal(120 + 30 + 4, ChromeLayout.PinnedCoreWidth(slots, 4, 30))
    End Sub

    <Fact>
    Public Sub Pinned_core_floor_with_nothing_to_drop_is_just_the_row()
        Dim slots As ChromeSlot() = {Slot("only", 90, pinned:=True)}
        Assert.Equal(90, ChromeLayout.PinnedCoreWidth(slots, 4, 30))
        Assert.Equal(0, ChromeLayout.PinnedCoreWidth(New ChromeSlot() {}, 4, 30))
    End Sub

    ' --- the top band beside the corner panel (188 §3.5) ------------------------------------------

    Private ReadOnly mediaWidth As Integer = 1600
    Private ReadOnly inset As Integer = 0
    Private ReadOnly gap As Integer = 4

    Private Function CornerPanel(width As Integer, height As Integer) As Rectangle
        ' The panel sits flush at the physical upper-right corner, media-relative.
        Return New Rectangle(mediaWidth - width, 0, width, height)
    End Function

    <Fact>
    Public Sub A_row_that_fits_left_of_the_panel_keeps_its_top_and_is_capped()
        Dim panel As Rectangle = CornerPanel(160, 52)
        Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
            0, 44, 700, panel, mediaWidth, gap, inset)
        Assert.False(placement.Wrapped)
        Assert.Equal(0, placement.Top)
        Assert.Equal(mediaWidth - panel.Width - gap, placement.MaxWidth)
        Assert.Equal(panel.Width + gap, placement.ReservedWidth)
    End Sub

    <Fact>
    Public Sub A_row_that_cannot_shrink_that_far_wraps_below_the_panel()
        Dim panel As Rectangle = CornerPanel(160, 52)
        ' One logical pixel past the shared width: the row no longer fits beside the panel.
        Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
            0, 44, mediaWidth - panel.Width - gap + 1, panel, mediaWidth, gap, inset)
        Assert.True(placement.Wrapped)
        Assert.Equal(panel.Bottom + gap, placement.Top)
        Assert.Equal(mediaWidth, placement.MaxWidth)
        Assert.Equal(0, placement.ReservedWidth)
    End Sub

    <Fact>
    Public Sub A_row_below_the_panel_never_overlaps_it()
        Dim panel As Rectangle = CornerPanel(160, 52)
        Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
            panel.Bottom - 4, 44, 400, panel, mediaWidth, gap, inset)
        Assert.False(placement.Wrapped)
        Assert.Equal(panel.Bottom - 4, placement.Top)
    End Sub

    <Fact>
    Public Sub The_wrapped_row_stays_below_both_its_natural_top_and_the_panel()
        Dim panel As Rectangle = CornerPanel(160, 120)
        Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
            60, 44, mediaWidth, panel, mediaWidth, gap, inset)
        Assert.True(placement.Wrapped)
        Assert.Equal(panel.Bottom + gap, placement.Top)
    End Sub

    <Fact>
    Public Sub Without_the_panel_a_row_takes_the_whole_band()
        Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
            0, 44, 0, Rectangle.Empty, mediaWidth, gap, inset)
        Assert.False(placement.Wrapped)
        Assert.Equal(mediaWidth, placement.MaxWidth)
        Assert.Equal(0, placement.ReservedWidth)
    End Sub

    <Fact>
    Public Sub A_band_narrower_than_the_row_is_not_shared_but_wrapped()
        ' The media is barely wider than the panel, and the row needs more than the sliver left:
        ' no shrunken band is offered, the row wraps.
        Dim panel As Rectangle = CornerPanel(1500, 52)
        Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
            0, 44, 100, panel, mediaWidth, gap, inset)
        Assert.True(placement.Wrapped)
        Assert.Equal(mediaWidth, placement.MaxWidth)
    End Sub

    ' --- the band against the panel's ACTUAL rectangle (L0281 §3.8, T-5) ------------------------------

    Private Function Band(naturalTop As Integer, rowHeight As Integer, minimumRowWidth As Integer, controls As Rectangle,
                          Optional bandInset As Integer = 0) As ChromeLayout.RowBandPlacement
        Return ChromeLayout.PlaceRowBesideCornerPanel(naturalTop, rowHeight, minimumRowWidth,
                                                      ChromeLayout.BandObstacle(controls, mediaWidth, bandInset), mediaWidth, gap, bandInset)
    End Function

    <Theory>
    <InlineData(0)>
    <InlineData(8)>
    Public Sub A_panel_in_the_corner_is_the_obstacle_it_always_was(cornerInset As Integer)
        ' The default placement is unchanged: the stretch to the far edge is nothing for a panel that touches it.
        Dim panel As New Rectangle(mediaWidth - cornerInset - 224, cornerInset, 224, 44)
        Assert.Equal(panel, ChromeLayout.BandObstacle(panel, mediaWidth, cornerInset))
        Dim direct As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(cornerInset, 44, 700, panel, mediaWidth, gap, cornerInset)
        Dim viaObstacle As ChromeLayout.RowBandPlacement = Band(cornerInset, 44, 700, panel, cornerInset)
        Assert.Equal(direct.Top, viaObstacle.Top)
        Assert.Equal(direct.MaxWidth, viaObstacle.MaxWidth)
        Assert.Equal(direct.ReservedWidth, viaObstacle.ReservedWidth)
        Assert.Equal(direct.Wrapped, viaObstacle.Wrapped)
    End Sub

    <Fact>
    Public Sub A_panel_in_the_middle_of_the_band_caps_the_row_before_its_near_edge()
        ' Put by the user at x = 700: the row, standing at the start edge, must end before the panel, not run under it.
        Dim panel As New Rectangle(700, 0, 224, 44)
        Dim obstacle As Rectangle = ChromeLayout.BandObstacle(panel, mediaWidth, 0)
        Assert.Equal(panel.Left, obstacle.Left)
        Assert.Equal(panel.Top, obstacle.Top)
        Assert.Equal(panel.Height, obstacle.Height)
        Assert.Equal(mediaWidth, obstacle.Right)
        Dim placement As ChromeLayout.RowBandPlacement = Band(0, 44, 300, panel)
        Assert.False(placement.Wrapped)
        Assert.True(placement.MaxWidth <= panel.Left - gap, "the row ends before the panel")
    End Sub

    <Fact>
    Public Sub A_panel_at_the_start_edge_of_the_band_sends_the_row_below_it()
        ' Nothing is left of a panel that stands where the row starts: the row wraps under it, whole.
        Dim panel As New Rectangle(0, 0, 224, 44)
        Dim placement As ChromeLayout.RowBandPlacement = Band(0, 44, 300, panel)
        Assert.True(placement.Wrapped)
        Assert.Equal(panel.Bottom + gap, placement.Top)
        Assert.Equal(mediaWidth, placement.MaxWidth)
    End Sub

    <Fact>
    Public Sub A_panel_outside_the_rows_band_reserves_nothing()
        ' Below the row (the user moved the window commands down the picture) or empty: the row keeps the whole band.
        Dim below As New Rectangle(mediaWidth - 224, 300, 224, 44)
        Dim placement As ChromeLayout.RowBandPlacement = Band(0, 44, 700, below)
        Assert.False(placement.Wrapped)
        Assert.Equal(0, placement.Top)
        Assert.Equal(mediaWidth, placement.MaxWidth)
        Assert.Equal(0, placement.ReservedWidth)
        ' Above the row's natural top.
        Dim above As New Rectangle(mediaWidth - 224, 0, 224, 44)
        Dim lower As ChromeLayout.RowBandPlacement = Band(60, 44, 700, above)
        Assert.False(lower.Wrapped)
        Assert.Equal(0, lower.ReservedWidth)
        Assert.Equal(mediaWidth, lower.MaxWidth)
        ' An empty or degenerate rectangle is no obstacle at all.
        Assert.True(ChromeLayout.BandObstacle(Rectangle.Empty, mediaWidth, 0).IsEmpty)
        Assert.True(ChromeLayout.BandObstacle(New Rectangle(10, 10, 0, 44), mediaWidth, 0).IsEmpty)
        Dim none As ChromeLayout.RowBandPlacement = Band(0, 44, 700, Rectangle.Empty)
        Assert.False(none.Wrapped)
        Assert.Equal(0, none.ReservedWidth)
    End Sub

    <Fact>
    Public Sub Two_stacked_rows_both_meeting_a_dropped_panel_each_wrap_below_it_and_the_second_follows_the_first()
        ' The window commands dropped across both rows' bands: row 1 wraps below them; row 2, stacked under row 1 where it
        ' REALLY stands (Main_Form.Chrome.vb: ChromeAnchoredRect(row 1).Bottom + gap), wraps no further than it must.
        Dim panel As New Rectangle(mediaWidth - 224, 20, 224, 44)
        Dim first As ChromeLayout.RowBandPlacement = Band(0, 44, mediaWidth, panel)
        Assert.True(first.Wrapped)
        Assert.Equal(panel.Bottom + gap, first.Top)
        Dim secondNaturalTop As Integer = first.Top + 44 + gap
        Dim second As ChromeLayout.RowBandPlacement = Band(secondNaturalTop, 44, mediaWidth, panel)
        Assert.False(second.Wrapped)
        Assert.Equal(secondNaturalTop, second.Top)
        Assert.True(second.Top >= first.Top + 44 + gap, "row 2 stands clear of row 1")
    End Sub

    ' --- the grip's double click (188 §5) ---------------------------------------------------------

    <Fact>
    Public Sub A_press_within_the_double_click_time_and_size_is_a_double_click()
        Dim limit As Integer = SystemInformation.DoubleClickTime
        Dim box As Size = SystemInformation.DoubleClickSize
        Assert.True(ChromeMenuRules.IsDoubleClickPress(1000, 1000 + limit, 0, 0))
        Assert.True(ChromeMenuRules.IsDoubleClickPress(1000, 1000, box.Width, box.Height))
        Assert.False(ChromeMenuRules.IsDoubleClickPress(1000, 1000 + limit + 1, 0, 0))
        Assert.False(ChromeMenuRules.IsDoubleClickPress(1000, 1000, box.Width + 1, 0))
        Assert.False(ChromeMenuRules.IsDoubleClickPress(1000, 1000, 0, box.Height + 1))
    End Sub

    <Fact>
    Public Sub A_press_before_the_previous_one_is_never_a_double_click()
        Assert.False(ChromeMenuRules.IsDoubleClickPress(2000, 1999, 0, 0))
        ' No previous press in memory.
        Assert.False(ChromeMenuRules.IsDoubleClickPress(Integer.MinValue, 5000, 0, 0))
    End Sub

    ' --- the panel's own footprint (188 §3.7) ------------------------------------------------------

    <Fact>
    Public Sub The_minimum_window_footprint_holds_the_pin_the_handle_three_commands_and_the_grip()
        ' Width: the panel's (horizontal) padding, the pin slot, the move handle's slot (L0281 T-5: the window
        ' commands are movable, so the panel gained one MinTarget + Gap slot beside the pin - the expectation moved
        ' deliberately, by exactly that slot, from 192 to 224 logical px), three command buttons, the drag grip
        ' (2026-10-12 owner revision: one virtual button wider) and their gaps. The height keeps the STANDARD
        ' padding: the panel shares the toolbar rows' height at the same top edge.
        Dim button As Integer = Math.Max(ChromeMetrics.ItemHeight, ChromeMetrics.MinTarget)
        Dim pad As Integer = ChromeMetrics.PanelPadding + ChromeMetrics.WindowControlsExtraPadding
        Dim slot As Integer = ChromeMetrics.MinTarget + ChromeMetrics.Gap
        Dim expectedWidth As Integer = 2 * pad + slot + slot + 4 * button + 3 * ChromeMetrics.Gap
        Assert.Equal(expectedWidth, ChromeMetrics.WindowControlsLogicalWidth())
        Assert.Equal(224, ChromeMetrics.WindowControlsLogicalWidth())
        Assert.Equal(button + 2 * ChromeMetrics.PanelPadding, ChromeMetrics.WindowControlsLogicalHeight())
    End Sub

End Class
#End If
