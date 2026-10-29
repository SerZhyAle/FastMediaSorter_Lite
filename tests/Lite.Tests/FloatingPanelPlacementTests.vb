#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports Xunit

''' <summary>
''' The pure geometry of a user-movable floating panel (L0281 §3): the place a drop is stored as, the rectangle it
''' resolves to in an area of any size, the fit that keeps every pixel inside, the push-out from a protected surface,
''' the snap back to the anchor, the keyboard step and the tolerant reader. Rectangles only, no screen.
''' </summary>
Public Class FloatingPanelPlacementTests

    Private Shared ReadOnly Area As New Rectangle(100, 50, 1000, 600)
    Private Shared ReadOnly PanelSize As New Size(200, 40)

    ' ------------------------------------------------------------------ corners and places ----

    <Theory>
    <InlineData(110, 60, 0)>
    <InlineData(850, 60, 1)>
    <InlineData(110, 600, 2)>
    <InlineData(850, 600, 3)>
    Public Sub The_nearest_corner_is_the_quarter_the_panel_centre_is_in(x As Integer, y As Integer, expected As Integer)
        ' 0..3 = TopLeft, TopRight, BottomLeft, BottomRight (a Friend enum cannot be a public test's parameter).
        Assert.Equal(CType(expected, PanelCorner), FloatingPanelPlacement.NearestCorner(New Rectangle(New Point(x, y), PanelSize), Area))
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(1)>
    <InlineData(2)>
    <InlineData(3)>
    Public Sub A_place_measured_from_every_corner_resolves_back_to_the_dropped_rectangle(cornerIndex As Integer)
        Dim drops As Rectangle() = {
            New Rectangle(124, 86, 200, 40), New Rectangle(876, 86, 200, 40),
            New Rectangle(124, 574, 200, 40), New Rectangle(876, 574, 200, 40)}
        Dim drop As Rectangle = drops(cornerIndex)
        Dim place As PanelPlace = FloatingPanelPlacement.FromRectangle(drop, Area, 1.0F)
        Assert.Equal(CType(cornerIndex, PanelCorner), place.Corner)
        Assert.Equal(24, place.Dx)
        Assert.Equal(36, place.Dy)
        Assert.Equal(drop, FloatingPanelPlacement.Resolve(place, Area, PanelSize, 1.0F))
    End Sub

    <Fact>
    Public Sub A_panel_standing_past_an_edge_counts_as_flush_never_as_a_negative_distance()
        Dim place As PanelPlace = FloatingPanelPlacement.FromRectangle(New Rectangle(80, 30, 200, 40), Area, 1.0F)
        Assert.Equal(PanelCorner.TopLeft, place.Corner)
        Assert.Equal(0, place.Dx)
        Assert.Equal(0, place.Dy)
        Assert.Equal(0, New PanelPlace(PanelCorner.BottomRight, -5, -7).Dx)
        Assert.Equal(0, New PanelPlace(PanelCorner.BottomRight, -5, -7).Dy)
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(2.0F)>
    Public Sub A_place_lands_on_the_same_visual_spot_at_every_scale(factor As Single)
        ' Stored in logical px: the device distance from the corner is the logical one times the scale, and a drop
        ' read back at the same scale gives the same logical numbers (no compounding across a DPI change).
        Dim place As New PanelPlace(PanelCorner.BottomRight, 24, 36)
        Dim size As New Size(CInt(200 * factor), CInt(40 * factor))
        Dim r As Rectangle = FloatingPanelPlacement.Resolve(place, Area, size, factor)
        Assert.Equal(CInt(Math.Round(24 * factor)), Area.Right - r.Right)
        Assert.Equal(CInt(Math.Round(36 * factor)), Area.Bottom - r.Bottom)
        Dim back As PanelPlace = FloatingPanelPlacement.FromRectangle(r, Area, factor)
        Assert.Equal(place.Corner, back.Corner)
        Assert.Equal(24, back.Dx)
        Assert.Equal(36, back.Dy)
    End Sub

    <Fact>
    Public Sub A_place_near_the_bottom_right_stays_near_it_when_the_area_grows()
        Dim place As New PanelPlace(PanelCorner.BottomRight, 24, 36)
        Dim bigger As New Rectangle(100, 50, 1600, 900)
        Dim r As Rectangle = FloatingPanelPlacement.Effective(place, bigger, PanelSize, 1.0F, False)
        Assert.Equal(24, bigger.Right - r.Right)
        Assert.Equal(36, bigger.Bottom - r.Bottom)
    End Sub

    ' ------------------------------------------------------------------ the fit ----

    <Theory>
    <InlineData(-500, -500)>
    <InlineData(5000, -500)>
    <InlineData(-500, 5000)>
    <InlineData(5000, 5000)>
    <InlineData(400, 300)>
    Public Sub No_pixel_of_a_fitted_panel_is_outside_the_allowed_area(x As Integer, y As Integer)
        Dim fitted As Rectangle = FloatingPanelPlacement.Fit(New Rectangle(New Point(x, y), PanelSize), Area, False)
        Assert.True(Area.Contains(fitted))
        Assert.Equal(PanelSize, fitted.Size)
    End Sub

    <Fact>
    Public Sub The_fit_is_the_shortest_translation_and_moves_only_the_offending_axis()
        Dim fitted As Rectangle = FloatingPanelPlacement.Fit(New Rectangle(1000, 300, 200, 40), Area, False)
        Assert.Equal(New Point(900, 300), fitted.Location)
    End Sub

    <Fact>
    Public Sub A_panel_larger_than_the_area_aligns_to_the_top_and_the_start_edge()
        Dim tiny As New Rectangle(100, 50, 150, 30)
        Dim ltr As Rectangle = FloatingPanelPlacement.Fit(New Rectangle(400, 300, 200, 40), tiny, False)
        Assert.Equal(New Point(100, 50), ltr.Location)
        Dim rtl As Rectangle = FloatingPanelPlacement.Fit(New Rectangle(400, 300, 200, 40), tiny, True)
        Assert.Equal(New Point(250 - 200, 50), rtl.Location)
    End Sub

    <Fact>
    Public Sub A_full_width_panel_moves_along_the_vertical_axis_only()
        Dim wide As New Size(Area.Width, 30)
        Dim fitted As Rectangle = FloatingPanelPlacement.Fit(New Rectangle(New Point(300, 200), wide), Area, False)
        Assert.Equal(Area.X, fitted.X)
        Assert.Equal(200, fitted.Y)
    End Sub

    <Fact>
    Public Sub A_resize_never_changes_the_place_and_the_panel_returns_when_the_area_does()
        Dim place As New PanelPlace(PanelCorner.TopLeft, 700, 400)
        Dim original As Rectangle = FloatingPanelPlacement.Effective(place, Area, PanelSize, 1.0F, False)
        Dim shrunk As New Rectangle(100, 50, 500, 300)
        Dim carried As Rectangle = FloatingPanelPlacement.Effective(place, shrunk, PanelSize, 1.0F, False)
        Assert.True(shrunk.Contains(carried))
        Assert.NotEqual(original, carried)
        Assert.Equal(original, FloatingPanelPlacement.Effective(place, Area, PanelSize, 1.0F, False))
        Assert.Equal(700, place.Dx)
        Assert.Equal(400, place.Dy)
    End Sub

    <Fact>
    Public Sub A_place_does_not_mirror_with_the_reading_direction()
        ' The reading direction decides only where an OVERSIZE panel aligns; a panel that fits stands where its
        ' physical corner puts it in either language.
        Dim place As New PanelPlace(PanelCorner.BottomLeft, 24, 36)
        Assert.Equal(FloatingPanelPlacement.Effective(place, Area, PanelSize, 1.0F, False),
                     FloatingPanelPlacement.Effective(place, Area, PanelSize, 1.0F, True))
    End Sub

    ' ------------------------------------------------------------------ protected surfaces, snap, keys ----

    <Fact>
    Public Sub A_drop_on_a_protected_surface_is_pushed_out_along_the_shortest_axis()
        Dim controls As New Rectangle(900, 50, 200, 44)
        Dim result As Rectangle
        Dim ok As Boolean = FloatingPanelPlacement.TryPushOut(New Rectangle(880, 60, 200, 40), Area,
                                                               New List(Of Rectangle) From {controls}, False, result)
        Assert.True(ok)
        Assert.False(result.IntersectsWith(controls))
        Assert.True(Area.Contains(result))
        Assert.Equal(New Point(880, 94), result.Location)
    End Sub

    <Fact>
    Public Sub A_drop_clear_of_every_protected_surface_is_its_own_result()
        Dim drop As New Rectangle(300, 300, 200, 40)
        Dim result As Rectangle
        Assert.True(FloatingPanelPlacement.TryPushOut(drop, Area, New List(Of Rectangle) From {New Rectangle(900, 50, 200, 44)}, False, result))
        Assert.Equal(drop, result)
    End Sub

    <Fact>
    Public Sub A_drop_with_no_free_fit_is_refused_and_the_panel_is_unchanged()
        Dim drop As New Rectangle(150, 70, 200, 40)
        Dim result As Rectangle
        Dim everything As New List(Of Rectangle) From {Area}
        Assert.False(FloatingPanelPlacement.TryPushOut(drop, Area, everything, False, result))
        Assert.Equal(drop, result)
    End Sub

    <Fact>
    Public Sub A_push_out_that_would_land_on_a_second_protected_surface_takes_another_way()
        Dim controls As New Rectangle(900, 50, 200, 44)
        Dim transport As New Rectangle(100, 94, 1000, 40)
        Dim result As Rectangle
        Assert.True(FloatingPanelPlacement.TryPushOut(New Rectangle(880, 60, 200, 40), Area,
                                                      New List(Of Rectangle) From {controls, transport}, False, result))
        Assert.False(result.IntersectsWith(controls))
        Assert.False(result.IntersectsWith(transport))
        Assert.True(Area.Contains(result))
    End Sub

    <Theory>
    <InlineData(0, 0, True)>
    <InlineData(12, -12, True)>
    <InlineData(13, 0, False)>
    <InlineData(0, -13, False)>
    Public Sub A_drop_within_the_snap_distance_of_the_anchor_snaps_back(dx As Integer, dy As Integer, expected As Boolean)
        Dim anchored As New Rectangle(100, 50, 200, 40)
        Dim drop As Rectangle = anchored
        drop.Offset(dx, dy)
        Assert.Equal(expected, FloatingPanelPlacement.SnapsToAnchor(drop, anchored, 12))
    End Sub

    ' ------------------------------------------------------------------ the allowed area and the drop ----

    <Fact>
    Public Sub The_allowed_area_is_the_media_inside_the_window_inside_the_screen_inset_on_every_side()
        Dim media As New Rectangle(0, 40, 1200, 700)
        Dim client As New Rectangle(10, 0, 1180, 740)
        Dim screenArea As New Rectangle(0, 0, 1100, 720)
        Assert.Equal(New Rectangle(10, 40, 1090, 680), FloatingPanelPlacement.AllowedArea(media, client, screenArea, 0))
        Assert.Equal(New Rectangle(18, 48, 1074, 664), FloatingPanelPlacement.AllowedArea(media, client, screenArea, 8))
    End Sub

    <Theory>
    <InlineData(0, 0, 0)>
    <InlineData(50, 0, 0)>
    <InlineData(60, 70, 0)>
    <InlineData(60, 70, 500)>
    <InlineData(-5, -5, 3)>
    Public Sub The_allowed_area_never_has_a_negative_size_or_a_stranded_origin(width As Integer, height As Integer, inset As Integer)
        Dim media As New Rectangle(300, 200, width, height)
        Dim area As Rectangle = FloatingPanelPlacement.AllowedArea(media, New Rectangle(0, 0, 2000, 2000), New Rectangle(0, 0, 2000, 2000), inset)
        Assert.True(area.Width >= 0)
        Assert.True(area.Height >= 0)
        Assert.True(media.Contains(area.Location) OrElse area.Location = media.Location)
    End Sub

    <Fact>
    Public Sub A_window_with_no_part_on_the_screen_falls_back_to_the_window_not_to_the_origin()
        Dim media As New Rectangle(5000, 5000, 800, 500)
        Dim area As Rectangle = FloatingPanelPlacement.AllowedArea(media, media, New Rectangle(0, 0, 1920, 1080), 0)
        Assert.Equal(media, area)
    End Sub

    <Fact>
    Public Sub A_drop_where_the_gesture_began_writes_nothing()
        Dim start As New Rectangle(400, 300, 200, 40)
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(start, start, New Rectangle(100, 50, 200, 40), Area,
                                                                            New List(Of Rectangle)(), 12, False, result)
        Assert.Equal(PanelDropOutcome.Unchanged, outcome)
        Assert.Equal(start, result)
    End Sub

    <Fact>
    Public Sub A_drop_within_the_snap_distance_of_the_anchor_clears_the_place_and_lands_on_the_anchor()
        Dim anchored As New Rectangle(100, 50, 200, 40)
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(New Rectangle(500, 300, 200, 40), New Rectangle(108, 58, 200, 40),
                                                                            anchored, Area, New List(Of Rectangle)(), 12, False, result)
        Assert.Equal(PanelDropOutcome.Anchor, outcome)
        Assert.Equal(anchored, result)
    End Sub

    <Fact>
    Public Sub A_drop_clear_of_everything_is_a_new_place_inside_the_allowed_area()
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(New Rectangle(100, 50, 200, 40), New Rectangle(500, 300, 200, 40),
                                                                            New Rectangle(100, 50, 200, 40), Area,
                                                                            New List(Of Rectangle) From {New Rectangle(900, 50, 200, 44)}, 12, False, result)
        Assert.Equal(PanelDropOutcome.Placed, outcome)
        Assert.Equal(New Rectangle(500, 300, 200, 40), result)
    End Sub

    <Fact>
    Public Sub A_drop_past_an_edge_is_stored_fitted_never_outside()
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(New Rectangle(100, 50, 200, 40), New Rectangle(1500, 900, 200, 40),
                                                                            New Rectangle(100, 50, 200, 40), Area,
                                                                            New List(Of Rectangle)(), 12, False, result)
        Assert.Equal(PanelDropOutcome.Placed, outcome)
        Assert.True(Area.Contains(result))
        Assert.Equal(New Point(900, 610), result.Location)
    End Sub

    <Fact>
    Public Sub A_drop_on_a_protected_surface_is_pushed_out_and_stored_clear_of_it()
        Dim controls As New Rectangle(900, 50, 200, 44)
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(New Rectangle(300, 300, 200, 40), New Rectangle(880, 60, 200, 40),
                                                                            New Rectangle(100, 50, 200, 40), Area,
                                                                            New List(Of Rectangle) From {controls}, 12, False, result)
        Assert.Equal(PanelDropOutcome.Placed, outcome)
        Assert.False(result.IntersectsWith(controls))
        Assert.True(Area.Contains(result))
    End Sub

    <Fact>
    Public Sub A_drop_with_no_free_fit_is_refused_and_the_panel_goes_back_to_where_it_began()
        Dim start As New Rectangle(300, 300, 200, 40)
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(start, New Rectangle(150, 70, 200, 40),
                                                                            New Rectangle(100, 50, 200, 40), Area,
                                                                            New List(Of Rectangle) From {Area}, 12, False, result)
        Assert.Equal(PanelDropOutcome.Refused, outcome)
        Assert.Equal(start, result)
    End Sub

    <Fact>
    Public Sub A_push_out_that_ends_exactly_where_the_gesture_began_is_unchanged()
        Dim controls As New Rectangle(900, 50, 200, 44)
        Dim start As New Rectangle(880, 94, 200, 40)
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(start, New Rectangle(880, 60, 200, 40),
                                                                            New Rectangle(100, 50, 200, 40), Area,
                                                                            New List(Of Rectangle) From {controls}, 12, False, result)
        Assert.Equal(PanelDropOutcome.Unchanged, outcome)
        Assert.Equal(start, result)
    End Sub

    <Fact>
    Public Sub A_keyboard_step_moves_by_whole_steps_through_the_fit()
        Dim start As New Rectangle(400, 300, 200, 40)
        Assert.Equal(New Point(428, 300), FloatingPanelPlacement.Nudge(start, 1, 0, 28, Area, False).Location)
        Assert.Equal(New Point(400, 160), FloatingPanelPlacement.Nudge(start, 0, -5, 28, Area, False).Location)
        Assert.Equal(New Point(100, 300), FloatingPanelPlacement.Nudge(start, -50, 0, 28, Area, False).Location)
    End Sub

    <Fact>
    Public Sub The_reveal_zone_grows_toward_the_nearest_corner()
        Dim panel As New Rectangle(876, 574, 200, 40)
        Assert.Equal(New Rectangle(876, 574, 248, 88), FloatingPanelPlacement.HotZone(panel, Area, 48))
        Dim topLeft As New Rectangle(124, 86, 200, 40)
        Assert.Equal(New Rectangle(76, 38, 248, 88), FloatingPanelPlacement.HotZone(topLeft, Area, 48))
    End Sub

    ' ------------------------------------------------------------------ the stored text ----

    <Theory>
    <InlineData(0, 0, 0, "TL:0,0")>
    <InlineData(1, 7, 300, "TR:7,300")>
    <InlineData(2, 24, 36, "BL:24,36")>
    <InlineData(3, 20000, 1, "BR:20000,1")>
    Public Sub A_place_round_trips_through_its_stored_text(cornerIndex As Integer, dx As Integer, dy As Integer, text As String)
        Dim corner As PanelCorner = CType(cornerIndex, PanelCorner)
        Dim place As New PanelPlace(corner, dx, dy)
        Assert.Equal(text, FloatingPanelPlacement.Encode(place))
        Dim back As PanelPlace
        Assert.True(FloatingPanelPlacement.TryDecode(text, back))
        Assert.Equal(corner, back.Corner)
        Assert.Equal(dx, back.Dx)
        Assert.Equal(dy, back.Dy)
    End Sub

    <Fact>
    Public Sub A_missing_value_reads_as_anchored()
        ' Its own fact: a VB InlineData(Nothing) passes a null ARGUMENT ARRAY, not one null argument.
        Dim place As PanelPlace
        Assert.False(FloatingPanelPlacement.TryDecode(Nothing, place))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData(" ")>
    <InlineData("XX:1,2")>
    <InlineData("tl:1,2")>
    <InlineData("TL")>
    <InlineData("TL:")>
    <InlineData("TL:1")>
    <InlineData("TL:1,")>
    <InlineData("TL:-1,2")>
    <InlineData("TL:+1,2")>
    <InlineData("TL: 1,2")>
    <InlineData("TL:1,2,3")>
    <InlineData("TL:1.5,2")>
    <InlineData("TL:20001,2")>
    <InlineData("TL:99999999999,2")>
    <InlineData("TLX:1,2")>
    <InlineData("v2|TL:1,2")>
    Public Sub A_garbled_or_future_value_reads_as_anchored(text As String)
        Dim place As PanelPlace
        Assert.False(FloatingPanelPlacement.TryDecode(text, place))
    End Sub

    <Fact>
    Public Sub An_old_recipients_point_reads_as_a_top_left_place()
        Dim place As PanelPlace = FloatingPanelPlacement.FromTopLeftPoint(New Point(40, 60))
        Assert.Equal("TL:40,60", FloatingPanelPlacement.Encode(place))
        Assert.Equal("TL:0,0", FloatingPanelPlacement.Encode(FloatingPanelPlacement.FromTopLeftPoint(New Point(-30, -4))))
    End Sub

End Class
#End If
