#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
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

    ' ------------------------------------------------------------------ the rows (125 UX-332) ----

    <Theory>
    <InlineData(0, 24, 0)>
    <InlineData(1, 24, 12)>
    <InlineData(10, 31, 15)>
    <InlineData(3, 1, 1)>
    <InlineData(3, 0, 0)>
    Public Sub The_delete_row_stands_half_a_row_apart_from_the_folders_above_it(folders As Integer, rowHeight As Integer, expected As Integer)
        ' Nothing above it, nothing to be apart from; otherwise half a row of air, never less than a pixel.
        Assert.Equal(expected, RecipientsOverlayRows.DeleteRowGap(folders, rowHeight))
    End Sub

    <Fact>
    Public Sub The_move_verb_is_drawn_only_while_the_folder_keeps_its_room()
        ' A row 300 wide, the caption starting at 28 after the glyph, 6 px of padding at the far end, a verb
        ' of 60 px with its space, and a folder that needs 96 px to stay readable.
        Assert.True(RecipientsOverlayRows.ShowsMoveVerb(300, 28, 6, 60, 96))
        Assert.True(RecipientsOverlayRows.ShowsMoveVerb(190, 28, 6, 60, 96))
        Assert.False(RecipientsOverlayRows.ShowsMoveVerb(189, 28, 6, 60, 96))
        ' No verb at all (an empty word) is never drawn.
        Assert.False(RecipientsOverlayRows.ShowsMoveVerb(300, 28, 6, 0, 96))
        Assert.False(RecipientsOverlayRows.ShowsMoveVerb(300, 28, 6, -5, 96))
    End Sub

    <Theory>
    <InlineData(24, 17)>
    <InlineData(71, 50)>
    <InlineData(14, 12)>
    <InlineData(13, 11)>
    <InlineData(3, 1)>
    <InlineData(0, 0)>
    <InlineData(-5, 0)>
    Public Sub A_row_picture_is_seven_tenths_of_the_row_never_below_the_floor_and_never_touching_the_edges(rowHeight As Integer, expected As Integer)
        ' 24 px (the default font at 100 %) lands where the old fixed 16 px picture did; the floor of 12 px keeps a
        ' drawing from becoming a smudge, but a row too short for it wins (two pixels of air); no row, no picture.
        Assert.Equal(expected, RecipientsOverlayRows.GlyphSide(rowHeight))
    End Sub

    <Fact>
    Public Sub A_row_picture_follows_the_row_when_the_table_font_grows()
        ' The font is a setting and the row follows it; a picture with a size of its own stayed a stamp beside a
        ' large caption (the delete row's bin at 225 % was a third of its row).
        Dim previous As Integer = 0
        For rowHeight As Integer = 20 To 120 Step 4
            Dim side As Integer = RecipientsOverlayRows.GlyphSide(rowHeight)
            Assert.True(side >= previous, "the picture shrank while the row grew at " & rowHeight)
            Assert.True(side <= rowHeight - 2, "the picture touches the row's edge at " & rowHeight)
            previous = side
        Next
        Assert.True(RecipientsOverlayRows.GlyphSide(96) >= 2 * RecipientsOverlayRows.GlyphSide(40) - 2)
    End Sub

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

    ' ----------------------------------------------- the shared place (L0281 T-6, owner answer Q281-3) ----
    ' The recovery-clamp facts above (PullInside / HeaderInside) are 219 §6 arithmetic; since T-6 the free table is
    ' placed by the shared model instead (live clamp, intended place kept across resizes, corner representation), and
    ' PullInside is only the wrapper of the same Fit. The facts below are what 219's repair-on-drop is replaced by.

    <Fact>
    Public Sub A_valid_stored_place_wins_over_the_old_triple()
        Dim place As PanelPlace
        For Each hasLegacy As Boolean In {True, False}
            Assert.True(RecipientsOverlayPlacement.TryReadPlace("BR:24,36", hasLegacy, 100, 200, place))
            Assert.Equal(PanelCorner.BottomRight, place.Corner)
            Assert.Equal(24, place.Dx)
            Assert.Equal(36, place.Dy)
        Next
    End Sub

    <Fact>
    Public Sub An_old_triple_alone_is_read_as_a_top_left_place()
        ' 219 stored a logical offset from the media area's top-left: the special case TL:x,y of the shared model.
        Dim place As PanelPlace
        Assert.True(RecipientsOverlayPlacement.TryReadPlace("", True, 237, 19, place))
        Assert.Equal(PanelCorner.TopLeft, place.Corner)
        Assert.Equal(237, place.Dx)
        Assert.Equal(19, place.Dy)
        Assert.Equal("TL:237,19", FloatingPanelPlacement.Encode(place))
    End Sub

    <Fact>
    Public Sub A_negative_old_coordinate_reads_as_zero()
        ' 219 allowed a drop outside the media area (negative offsets); the shared place never has a negative distance.
        Dim place As PanelPlace
        Assert.True(RecipientsOverlayPlacement.TryReadPlace(Nothing, True, -137, 245, place))
        Assert.Equal("TL:0,245", FloatingPanelPlacement.Encode(place))
        Assert.True(RecipientsOverlayPlacement.TryReadPlace(Nothing, True, 12, -3, place))
        Assert.Equal("TL:12,0", FloatingPanelPlacement.Encode(place))
    End Sub

    <Fact>
    Public Sub A_garbled_stored_place_falls_back_to_the_old_triple_and_then_to_the_corner()
        Dim garbled As String() = {"XX:1,2", "TL:-1,2", "TL:1,2,3", "TL:1", "tl:1,2", "TL:20001,0", "Z9:1,2;future", "BR:5", "  "}
        Dim place As PanelPlace
        For Each text As String In garbled
            ' with an old triple the table is read from it; without one it stands at its corner choice
            Assert.True(RecipientsOverlayPlacement.TryReadPlace(text, True, 40, 50, place), text)
            Assert.Equal("TL:40,50", FloatingPanelPlacement.Encode(place))
            Assert.False(RecipientsOverlayPlacement.TryReadPlace(text, False, 40, 50, place), text)
        Next
    End Sub

    <Fact>
    Public Sub Nothing_stored_means_the_corner_choice_and_the_flag_alone_says_so()
        ' The flag alone says "anchored": numbers without HasCustomPosition (an imported old file, a reset) are not a place.
        Dim place As PanelPlace
        Assert.False(RecipientsOverlayPlacement.TryReadPlace("", False, 0, 0, place))
        Assert.False(RecipientsOverlayPlacement.TryReadPlace(Nothing, False, 300, 200, place))
        Assert.Equal("TL:0,0", FloatingPanelPlacement.Encode(place))
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(2.0F)>
    Public Sub An_old_point_reads_at_the_very_visual_spot_219_placed_it(factor As Single)
        ' The allowed area of a free table starts where the media area does (inset 0), so TL:x,y puts the table exactly
        ' where 219 put the stored point: media origin + the logical offset through the same scale seam.
        Dim media As New Rectangle(100, 50, 800, 600)
        Dim place As PanelPlace
        Assert.True(RecipientsOverlayPlacement.TryReadPlace("", True, 40, 30, place))
        Dim rect As Rectangle = FloatingPanelPlacement.Effective(place, media, New Size(200, 120), factor, False)
        Dim old As Point = RecipientsOverlayPlacement.ToDeviceOffset(New Point(40, 30), factor)
        Assert.Equal(New Point(media.X + old.X, media.Y + old.Y), rect.Location)
    End Sub

    <Fact>
    Public Sub A_resize_never_writes_the_intended_place_back_and_the_table_returns_where_it_was_left()
        ' Q281-3: the intended place is kept across resizes. Placement is a pure function of (place, area, size): the same
        ' place in a shrunken area is carried inside, and in the original area again it is exactly where it was.
        Dim place As PanelPlace
        Assert.True(RecipientsOverlayPlacement.TryReadPlace("BR:24,36", False, 0, 0, place))
        Dim panel As New Size(200, 300)
        Dim wide As New Rectangle(0, 0, 1000, 800)
        Dim original As Rectangle = FloatingPanelPlacement.Effective(place, wide, panel, 1.0F, False)
        Assert.Equal(New Rectangle(776, 464, 200, 300), original)
        For Each smaller As Rectangle In {New Rectangle(0, 0, 500, 400), New Rectangle(0, 0, 150, 250), New Rectangle(30, 20, 220, 310)}
            Dim carried As Rectangle = FloatingPanelPlacement.Effective(place, smaller, panel, 1.0F, False)
            If panel.Width <= smaller.Width AndAlso panel.Height <= smaller.Height Then
                Assert.True(smaller.Contains(carried), "the table must be carried inside " & smaller.ToString())
            End If
            Assert.Equal("BR:24,36", FloatingPanelPlacement.Encode(place))   ' nothing was written back
            Assert.Equal(original, FloatingPanelPlacement.Effective(place, wide, panel, 1.0F, False))
        Next
    End Sub

    <Fact>
    Public Sub No_proposed_rectangle_of_a_drag_ever_leaves_the_allowed_area()
        ' R2 (no pixel outside, even for a frame): WM_MOVING hands every proposed rectangle to Fit before the window moves.
        Dim allowed As New Rectangle(20, 30, 760, 540)
        Dim size As New Size(280, 300)
        For x As Integer = -600 To 1500 Step 37
            For y As Integer = -600 To 1300 Step 41
                For Each startIsRight As Boolean In {False, True}
                    Dim fitted As Rectangle = FloatingPanelPlacement.Fit(New Rectangle(x, y, size.Width, size.Height), allowed, startIsRight)
                    Assert.True(allowed.Contains(fitted), "outside at " & x.ToString() & "," & y.ToString() & ": " & fitted.ToString())
                    Assert.Equal(size, fitted.Size)
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub A_proposed_rectangle_already_inside_is_left_alone_so_the_pointer_keeps_its_grab_point()
        Dim allowed As New Rectangle(20, 30, 760, 540)
        Dim inside As New Rectangle(300, 200, 280, 300)
        Assert.Equal(inside, FloatingPanelPlacement.Fit(inside, allowed, False))
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(2.0F)>
    Public Sub A_dropped_table_is_stored_as_a_place_and_comes_back_at_the_same_spot(factor As Single)
        Dim allowed As New Rectangle(100, 50, 800, 600)
        Dim size As New Size(CInt(280 * factor), CInt(300 * factor))
        Dim started As New Rectangle(allowed.X + CInt(8 * factor), allowed.Y + CInt(8 * factor), size.Width, size.Height)
        Dim dropped As New Rectangle(allowed.X + CInt(210 * factor), allowed.Y + CInt(90 * factor), size.Width, size.Height)
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(started, dropped, started, allowed, New List(Of Rectangle)(),
                                                                          CInt(12 * factor), False, result)
        Assert.Equal(PanelDropOutcome.Placed, outcome)
        Dim text As String = FloatingPanelPlacement.Encode(FloatingPanelPlacement.FromRectangle(result, allowed, factor))
        Dim place As PanelPlace
        Assert.True(RecipientsOverlayPlacement.TryReadPlace(text, False, 0, 0, place))
        Assert.Equal(result, FloatingPanelPlacement.Effective(place, allowed, size, factor, False))
        ' The triple an older build reads: the dropped top-left in logical px from the media area. Read alone (the new key
        ' absent) it puts the table at the same spot - the triple stays meaningful.
        Dim legacy As Point = RecipientsOverlayPlacement.ToLogicalPoint(New Point(result.X - allowed.X, result.Y - allowed.Y), factor)
        Dim fromTriple As PanelPlace
        Assert.True(RecipientsOverlayPlacement.TryReadPlace("", True, legacy.X, legacy.Y, fromTriple))
        Assert.Equal(result, FloatingPanelPlacement.Effective(fromTriple, allowed, size, factor, False))
    End Sub

    <Fact>
    Public Sub A_drop_where_the_drag_began_is_unchanged_and_a_drop_next_to_the_anchor_clears_the_place()
        Dim allowed As New Rectangle(0, 0, 800, 600)
        Dim anchored As New Rectangle(8, 8, 280, 300)
        Dim result As Rectangle
        Assert.Equal(PanelDropOutcome.Unchanged, FloatingPanelPlacement.DecideDrop(anchored, anchored, anchored, allowed,
                     New List(Of Rectangle)(), 12, False, result))
        Assert.Equal(anchored, result)
        Dim free As New Rectangle(400, 200, 280, 300)
        Assert.Equal(PanelDropOutcome.Anchor, FloatingPanelPlacement.DecideDrop(free, New Rectangle(15, 17, 280, 300), anchored, allowed,
                     New List(Of Rectangle)(), 12, False, result))
        Assert.Equal(anchored, result)
        Assert.Equal(PanelDropOutcome.Placed, FloatingPanelPlacement.DecideDrop(free, New Rectangle(25, 17, 280, 300), anchored, allowed,
                     New List(Of Rectangle)(), 12, False, result))
    End Sub

    <Fact>
    Public Sub A_table_dropped_on_a_protected_surface_is_pushed_out_or_the_move_is_refused()
        Dim allowed As New Rectangle(0, 0, 800, 600)
        Dim start As New Rectangle(8, 8, 280, 300)
        Dim controls As New Rectangle(700, 0, 100, 40)        ' the window commands in the corner
        Dim result As Rectangle
        Dim outcome As PanelDropOutcome = FloatingPanelPlacement.DecideDrop(start, New Rectangle(500, 10, 280, 300), start, allowed,
                                                                          New List(Of Rectangle) From {controls}, 12, False, result)
        Assert.Equal(PanelDropOutcome.Placed, outcome)
        Assert.False(result.IntersectsWith(controls), "the table must not cover the window commands")
        Assert.True(allowed.Contains(result))
        ' A protected surface that leaves no free fit refuses the move: nothing is stored, the table returns to the start.
        Dim everything As New Rectangle(0, 0, 800, 600)
        Assert.Equal(PanelDropOutcome.Refused, FloatingPanelPlacement.DecideDrop(start, New Rectangle(300, 200, 280, 300), start, allowed,
                     New List(Of Rectangle) From {everything}, 12, False, result))
        Assert.Equal(start, result)
    End Sub

End Class
#End If
