#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

''' <summary>
''' The pure geometry of the recipients overlay's free position
''' (219_SPECIFICATION_RECIPIENTS_OVERLAY_DRAG_BY_HEADER.md §3, §6, §7): the anchored corner
''' offsets, the logical-px storage of a dropped point, the out-of-window recovery clamp and
''' the hot-zone rule of a moved panel. Points and rectangles only, so the DPI scenes and the
''' lost-panel scenes are provable without a screen.
''' </summary>
Public Class RecipientsOverlayPlacementTests

    ' ------------------------------------------------------------------ the anchored corner ----

    <Theory>
    <InlineData("topLeft", 8, 40, 8, 40)>
    <InlineData("topRight", 292, 40, 8, 40)>
    <InlineData("bottomLeft", 8, 352, 8, 40)>
    <InlineData("bottomRight", 292, 352, 8, 40)>
    Public Sub The_corner_choice_places_the_panel_at_its_corner_with_the_margin(corner As String,
                                                                                expectedX As Integer, expectedY As Integer,
                                                                                margin As Integer, topOffset As Integer)
        ' A 400x400 media area, a 100x40 panel: the offsets the corner choice always produced.
        Dim offset As Point = RecipientsOverlayPlacement.CornerOffset(corner, New Size(400, 400), New Size(100, 40),
                                                                      margin, topOffset)
        Assert.Equal(New Point(expectedX, expectedY), offset)
    End Sub

    <Fact>
    Public Sub A_corner_never_places_the_panel_off_the_area_when_there_is_no_room()
        ' The area smaller than the panel plus two margins: the old Math.Max floor keeps the
        ' panel at the margin on both axes, never at a negative offset.
        Dim offset As Point = RecipientsOverlayPlacement.CornerOffset("bottomRight", New Size(60, 60), New Size(100, 60),
                                                                      8, 8)
        Assert.Equal(8, offset.X)
        Assert.Equal(8, offset.Y)
    End Sub

    <Fact>
    Public Sub The_bottom_corners_measure_from_the_bottom_not_from_the_chrome_top_offset()
        ' Only the top corners sit under the toolbar/chrome stack; the bottom ones hug the
        ' media area's own bottom edge.
        Dim offset As Point = RecipientsOverlayPlacement.CornerOffset("bottomLeft", New Size(400, 400), New Size(100, 40),
                                                                      8, 120)
        Assert.Equal(8, offset.X)
        Assert.Equal(352, offset.Y)
    End Sub

    ' ------------------------------------------------------- the logical-px storage seam ----

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(2.0F)>
    Public Sub A_dropped_position_survives_the_scale_round_trip_at_the_same_visual_spot(factor As Single)
        ' Two monitors, two scales, one sane answer (219 §3): a logical point converts to
        ' device pixels and back to the same logical point.
        Dim dropped As New Point(237, -19)
        Dim device As Point = RecipientsOverlayPlacement.ToDeviceOffset(dropped, factor)
        Dim back As Point = RecipientsOverlayPlacement.ToLogicalPoint(device, factor)
        Assert.Equal(dropped.X, back.X)
        Assert.Equal(dropped.Y, back.Y)
    End Sub

    <Fact>
    Public Sub A_positive_coordinate_agrees_with_the_size_seam_pixel_for_pixel()
        ' For positive coordinates the position seam is the same arithmetic UiScale.Px runs;
        ' only the sign handling differs (a position is not a size).
        For Each factor As Single In {1.0F, 1.5F, 2.0F}
            Dim device As Point = RecipientsOverlayPlacement.ToDeviceOffset(New Point(237, 19), factor)
            Assert.Equal(UiScale.Px(237, factor), device.X)
            Assert.Equal(UiScale.Px(19, factor), device.Y)
        Next
    End Sub

    <Fact>
    Public Sub The_same_logical_point_grows_with_the_scale_so_the_visual_spot_is_kept()
        ' 100 / 150 / 200 % DPI: the device offset follows the factor, which is exactly what
        ' re-places the panel at the same spot on a different monitor scale.
        Dim logical As New Point(200, 150)
        Assert.Equal(200, RecipientsOverlayPlacement.ToDeviceOffset(logical, 1.0F).X)
        Assert.Equal(300, RecipientsOverlayPlacement.ToDeviceOffset(logical, 1.5F).X)
        Assert.Equal(400, RecipientsOverlayPlacement.ToDeviceOffset(logical, 2.0F).X)
        Assert.Equal(150, RecipientsOverlayPlacement.ToDeviceOffset(logical, 1.0F).Y)
        Assert.Equal(225, RecipientsOverlayPlacement.ToDeviceOffset(logical, 1.5F).Y)
        Assert.Equal(300, RecipientsOverlayPlacement.ToDeviceOffset(logical, 2.0F).Y)
    End Sub

    <Fact>
    Public Sub A_damaged_scale_falls_back_to_identity_instead_of_a_lost_panel()
        Assert.Equal(New Point(120, 80), RecipientsOverlayPlacement.ToDeviceOffset(New Point(120, 80), 0.0F))
        Assert.Equal(New Point(120, 80), RecipientsOverlayPlacement.ToLogicalPoint(New Point(120, 80), Single.NaN))
    End Sub

    ' ------------------------------------------------------------- the recovery clamp ----

    <Fact>
    Public Sub A_panel_fully_outside_is_pulled_back_fully_inside()
        ' "Утащил и потерял": the panel was dragged past the right and bottom edges.
        Dim area As New Rectangle(0, 0, 800, 600)
        Dim pulled As Point = RecipientsOverlayPlacement.PullInside(New Point(900, 700), New Size(200, 120), area)
        Assert.Equal(600, pulled.X)   ' 800 - 200
        Assert.Equal(480, pulled.Y)   ' 600 - 120
    End Sub

    <Fact>
    Public Sub The_pull_is_along_the_axis_it_left_and_never_touches_the_other_one()
        ' Half out on the left, comfortably inside vertically: only X moves.
        Dim area As New Rectangle(100, 100, 800, 600)
        Dim pulled As Point = RecipientsOverlayPlacement.PullInside(New Point(20, 300), New Size(200, 120), area)
        Assert.Equal(100, pulled.X)
        Assert.Equal(300, pulled.Y)
    End Sub

    <Fact>
    Public Sub An_inside_panel_is_not_moved_by_the_clamp()
        Dim area As New Rectangle(0, 0, 800, 600)
        Dim inside As New Point(317, 240)
        Assert.Equal(inside, RecipientsOverlayPlacement.PullInside(inside, New Size(200, 120), area))
    End Sub

    <Fact>
    Public Sub A_panel_larger_than_the_area_keeps_its_header_reachable()
        ' Nothing fits the whole panel; the clamp pins its top-left corner to the area's, so
        ' the grip stays inside and can bring it back.
        Dim area As New Rectangle(10, 10, 100, 80)
        Dim pulled As Point = RecipientsOverlayPlacement.PullInside(New Point(400, 400), New Size(300, 200), area)
        Assert.Equal(New Point(10, 10), pulled)
    End Sub

    <Fact>
    Public Sub A_stranded_panel_is_detected_by_its_header_not_by_its_whole_body()
        ' 219 §6: out of bounds means the header strip - the grip - is not fully inside the
        ' media area. A panel whose body hangs over the bottom edge but whose header is inside
        ' is not stranded, and one dragged mostly outside is.
        Dim area As New Rectangle(0, 0, 800, 600)
        Assert.True(RecipientsOverlayPlacement.HeaderInside(New Point(300, 480), New Size(200, 300), 44, area),
                    "a header inside the area keeps the panel recoverable by hand")
        Assert.False(RecipientsOverlayPlacement.HeaderInside(New Point(300, 580), New Size(200, 300), 44, area),
                     "a header past the bottom edge is lost")
        Assert.False(RecipientsOverlayPlacement.HeaderInside(New Point(700, 100), New Size(200, 300), 44, area),
                     "a header past the right edge is lost")
    End Sub

    ' ---------------------------------------------------------------- the reveal hot zone ----

    <Theory>
    <InlineData("topLeft", -40, -40, 248, 188)>
    <InlineData("topRight", 592, -40, 248, 188)>
    <InlineData("bottomLeft", -40, 452, 248, 188)>
    <InlineData("bottomRight", 592, 452, 248, 188)>
    Public Sub The_four_anchored_corners_reproduce_the_zones_of_the_pin_spec(corner As String,
                                                                             expectedX As Integer, expectedY As Integer,
                                                                             expectedWidth As Integer, expectedHeight As Integer)
        ' A panel placed by the corner choice keeps exactly the corner zone 117 Ф-2 grew
        ' (the pure rule reads the panel's own rect, which the corner anchored).
        Dim area As New Rectangle(0, 0, 800, 600)
        Dim panel As New Rectangle(RecipientsOverlayPlacement.CornerOffset(corner, area.Size, New Size(200, 140), 8, 8),
                                   New Size(200, 140))
        Dim zone As Rectangle = RecipientsOverlayPlacement.HotZone(panel, area, 48)
        Assert.Equal(expectedX, zone.X)
        Assert.Equal(expectedY, zone.Y)
        Assert.Equal(expectedWidth, zone.Width)
        Assert.Equal(expectedHeight, zone.Height)
    End Sub

    <Fact>
    Public Sub A_moved_panel_carries_its_zone_toward_the_nearest_edges()
        ' Q219-4: the configured corner is a position, not a compass - a panel dragged to the
        ' lower right of the media area reveals from the lower right, wherever the corner
        ' choice says.
        Dim area As New Rectangle(0, 0, 800, 600)
        Dim panel As New Rectangle(520, 420, 200, 140)
        Dim zone As Rectangle = RecipientsOverlayPlacement.HotZone(panel, area, 48)
        Assert.Equal(520, zone.X)               ' grows right, not left
        Assert.Equal(420, zone.Y)               ' grows down, not up
        Assert.Equal(200 + 48, zone.Width)
        Assert.Equal(140 + 48, zone.Height)
    End Sub

    <Fact>
    Public Sub The_zone_always_contains_the_panel_itself()
        Dim area As New Rectangle(0, 0, 800, 600)
        For Each location As Point In {New Point(0, 0), New Point(790, 580), New Point(300, 200), New Point(-40, 300)}
            Dim panel As New Rectangle(location, New Size(200, 140))
            Dim zone As Rectangle = RecipientsOverlayPlacement.HotZone(panel, area, 48)
            Assert.True(zone.Contains(panel), "the zone of " & location.ToString() & " must contain the panel")
        Next
    End Sub

    <Fact>
    Public Sub A_zero_reach_zone_is_the_panel_itself()
        Dim area As New Rectangle(0, 0, 800, 600)
        Dim panel As New Rectangle(120, 90, 200, 140)
        Assert.Equal(panel, RecipientsOverlayPlacement.HotZone(panel, area, 0))
    End Sub

End Class
#End If
