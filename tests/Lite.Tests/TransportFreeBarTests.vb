#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports Xunit

''' <summary>
''' The movable video transport bar (L0281 T-5b, Q281-2): the strip a free bar stands in, its effective rectangle, the drag
''' and keyboard steps, the drop decision and the status strip's rule. The bar moves up and down only, never outside the
''' allowed area, and a resize never changes its stored place. Rectangles only, no screen.
''' </summary>
Public Class TransportFreeBarTests

    Private Shared ReadOnly Allowed As New Rectangle(0, 0, 1000, 600)
    ' Where the bar stands by the default rules: a 20 px margin, nearly full width, at the bottom.
    Private Shared ReadOnly Anchored As New Rectangle(20, 540, 960, 40)
    Private Shared ReadOnly NoProtected As New List(Of Rectangle)

    Private Shared Function Band() As Rectangle
        Return TransportRules.FreeBarBand(Anchored, Allowed)
    End Function

    ' --- the strip -----------------------------------------------------------------------------------

    <Fact>
    Public Sub The_strip_is_as_wide_as_the_bar_and_as_tall_as_the_area()
        Assert.Equal(New Rectangle(20, 0, 960, 600), Band())
    End Sub

    <Fact>
    Public Sub A_bar_wider_than_the_area_is_capped_to_it_and_starts_at_its_edge()
        Assert.Equal(New Rectangle(0, 0, 1000, 600), TransportRules.FreeBarBand(New Rectangle(-10, 540, 1200, 40), Allowed))
    End Sub

    <Fact>
    Public Sub A_bar_partly_outside_the_area_is_pulled_inside_horizontally()
        Assert.Equal(New Rectangle(600, 0, 400, 600), TransportRules.FreeBarBand(New Rectangle(900, 540, 400, 40), Allowed))
    End Sub

    ' --- the effective rectangle (section 3.4) ------------------------------------------------------------

    <Fact>
    Public Sub A_place_decides_the_height_only_the_width_and_horizontal_position_are_the_strips()
        Dim place As New PanelPlace(PanelCorner.TopLeft, 300, 100)
        Assert.Equal(New Rectangle(20, 100, 960, 40), TransportRules.FreeBarRect(place, Anchored, Allowed, 1.0F))
        Dim fromRight As New PanelPlace(PanelCorner.BottomRight, 0, 20)
        Assert.Equal(New Rectangle(20, 540, 960, 40), TransportRules.FreeBarRect(fromRight, Anchored, Allowed, 1.0F))
    End Sub

    <Fact>
    Public Sub A_resize_carries_the_bar_inside_and_returns_it_to_the_stored_place_when_the_window_grows_again()
        Dim place As New PanelPlace(PanelCorner.TopLeft, 0, 500)
        Dim before As Rectangle = TransportRules.FreeBarRect(place, Anchored, Allowed, 1.0F)
        Dim small As New Rectangle(0, 0, 1000, 300)
        Dim shrunk As Rectangle = TransportRules.FreeBarRect(place, New Rectangle(20, 240, 960, 40), small, 1.0F)
        Assert.True(small.Contains(shrunk), shrunk.ToString())
        Assert.Equal(260, shrunk.Y)
        Assert.Equal(before, TransportRules.FreeBarRect(place, Anchored, Allowed, 1.0F))
        Assert.Equal("TL:0,500", FloatingPanelPlacement.Encode(place))
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(2.0F)>
    Public Sub A_dropped_bar_reads_back_at_the_same_spot_at_every_scale(factor As Single)
        Dim dropped As New Rectangle(20, 201, 960, 40)
        Dim text As String = FloatingPanelPlacement.Encode(FloatingPanelPlacement.FromRectangle(dropped, Band(), factor))
        Dim place As PanelPlace
        Assert.True(FloatingPanelPlacement.TryDecode(text, place), text)
        Dim back As Rectangle = TransportRules.FreeBarRect(place, Anchored, Allowed, factor)
        Assert.Equal(dropped.X, back.X)
        Assert.True(Math.Abs(back.Y - dropped.Y) <= 1, text & " -> " & back.ToString())
    End Sub

    ' --- the drag and the keyboard step (R2, section 3.6) ------------------------------------------------------

    <Fact>
    Public Sub Every_drag_step_to_and_past_both_edges_keeps_the_whole_bar_inside_the_strip()
        Dim strip As Rectangle = Band()
        For travel As Integer = -1500 To 1500 Step 7
            Dim r As Rectangle = TransportRules.DragFreeBar(Anchored, travel, strip)
            Assert.True(strip.Contains(r), travel.ToString() & " -> " & r.ToString())
            Assert.Equal(strip.X, r.X)
            Assert.Equal(Anchored.Size, r.Size)
        Next
    End Sub

    <Fact>
    Public Sub A_drag_follows_the_vertical_travel_and_ignores_a_horizontal_offset()
        Dim r As Rectangle = TransportRules.DragFreeBar(New Rectangle(37, 540, 960, 40), -300, Band())
        Assert.Equal(New Rectangle(20, 240, 960, 40), r)
    End Sub

    <Fact>
    Public Sub The_keyboard_step_moves_by_whole_steps_and_stops_at_the_edges()
        Assert.Equal(508, TransportRules.NudgeFreeBar(Anchored, -1, 32, Band()).Y)
        Assert.Equal(380, TransportRules.NudgeFreeBar(Anchored, -5, 32, Band()).Y)
        Assert.Equal(560, TransportRules.NudgeFreeBar(Anchored, 5, 32, Band()).Y)
        Assert.Equal(0, TransportRules.NudgeFreeBar(New Rectangle(20, 20, 960, 40), -5, 32, Band()).Y)
    End Sub

    ' --- the drop (section 3.5) ---------------------------------------------------------------------

    <Fact>
    Public Sub A_drop_away_from_the_anchor_is_a_new_place_in_the_strip()
        Dim result As Rectangle
        Assert.Equal(PanelDropOutcome.Placed,
                     TransportRules.DropFreeBar(Anchored, New Rectangle(300, 200, 960, 40), Anchored, Allowed, NoProtected, 12, result))
        Assert.Equal(New Rectangle(20, 200, 960, 40), result)
    End Sub

    <Fact>
    Public Sub A_drop_within_the_snap_of_the_anchor_clears_the_place()
        Dim result As Rectangle
        Dim start As New Rectangle(20, 200, 960, 40)
        Assert.Equal(PanelDropOutcome.Anchor,
                     TransportRules.DropFreeBar(start, New Rectangle(20, 552, 960, 40), Anchored, Allowed, NoProtected, 12, result))
        Assert.Equal(Anchored, result)
    End Sub

    <Fact>
    Public Sub A_drop_where_the_move_began_writes_nothing()
        Dim result As Rectangle
        Dim start As New Rectangle(20, 200, 960, 40)
        Assert.Equal(PanelDropOutcome.Unchanged,
                     TransportRules.DropFreeBar(start, start, Anchored, Allowed, NoProtected, 12, result))
        Assert.Equal(start, result)
    End Sub

    <Fact>
    Public Sub A_drop_over_the_window_commands_is_pushed_below_them()
        Dim commands As New List(Of Rectangle) From {New Rectangle(800, 0, 200, 40)}
        Dim result As Rectangle
        Assert.Equal(PanelDropOutcome.Placed,
                     TransportRules.DropFreeBar(Anchored, New Rectangle(20, 10, 960, 40), Anchored, Allowed, commands, 12, result))
        Assert.Equal(New Rectangle(20, 40, 960, 40), result)
    End Sub

    <Fact>
    Public Sub A_drop_with_no_free_fit_is_refused_and_the_bar_returns_to_where_it_began()
        Dim everything As New List(Of Rectangle) From {New Rectangle(0, 0, 1000, 600)}
        Dim result As Rectangle
        Dim start As New Rectangle(20, 200, 960, 40)
        Assert.Equal(PanelDropOutcome.Refused,
                     TransportRules.DropFreeBar(start, New Rectangle(20, 300, 960, 40), Anchored, Allowed, everything, 12, result))
        Assert.Equal(start, result)
    End Sub

    ' --- the status strip (section 3.8) -----------------------------------------------------------------

    <Theory>
    <InlineData(True, False, False, True)>
    <InlineData(True, False, True, False)>
    <InlineData(False, False, False, False)>
    <InlineData(True, True, False, False)>
    Public Sub The_anchored_status_strip_reserves_the_bar_slot_only_while_the_bar_is_anchored(mediaOpen As Boolean, superFullScreen As Boolean,
                                                                                               barIsFree As Boolean, expected As Boolean)
        Assert.Equal(expected, TransportRules.StatusReservesBarSlot(mediaOpen, superFullScreen, barIsFree))
    End Sub

End Class
#End If
