#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

''' <summary>
''' The editor canvas' geometry (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §5, §13).
'''
''' Everything the editor will ever draw is placed relative to this rectangle, so a
''' half-pixel here is a stroke landing somewhere other than where it looked. It is also
''' the one part of the canvas a test can drive without a window.
'''
''' Modern-only, like the module it covers: on the net48 leg both compile to nothing.
''' </summary>
Public Class EditorGeometryTests

    <Fact>
    Public Sub A_wide_picture_is_letterboxed_and_centred()
        Dim rect = EditorGeometry.FitRect(New Size(1000, 500), New Size(500, 500))

        Assert.Equal(500, rect.Width)
        Assert.Equal(250, rect.Height)
        Assert.Equal(0, rect.Left)
        Assert.Equal(125, rect.Top)
    End Sub

    <Fact>
    Public Sub A_tall_picture_is_pillarboxed_and_centred()
        Dim rect = EditorGeometry.FitRect(New Size(500, 1000), New Size(500, 500))

        Assert.Equal(250, rect.Width)
        Assert.Equal(500, rect.Height)
        Assert.Equal(125, rect.Left)
        Assert.Equal(0, rect.Top)
    End Sub

    ''' <summary>
    ''' The cap at 1.0 is the point of the whole function: a 200x150 icon stretched over
    ''' a 1400-pixel canvas would be edited at seven screen pixels per image pixel, and
    ''' every stroke would land on a different pixel than the one under the cursor.
    ''' </summary>
    <Fact>
    Public Sub A_picture_smaller_than_the_canvas_is_not_enlarged()
        Dim rect = EditorGeometry.FitRect(New Size(200, 150), New Size(1400, 900))

        Assert.Equal(200, rect.Width)
        Assert.Equal(150, rect.Height)
        Assert.Equal(600, rect.Left)
        Assert.Equal(375, rect.Top)
    End Sub

    <Fact>
    Public Sub An_exact_fit_fills_the_canvas()
        Dim rect = EditorGeometry.FitRect(New Size(800, 600), New Size(800, 600))

        Assert.Equal(New Rectangle(0, 0, 800, 600), rect)
    End Sub

    ''' <summary>
    ''' The real case the owner sorts: a 24-megapixel photo in a window. The scale is far
    ''' below 1, and the aspect ratio has to survive it - a rounded-off ratio is a
    ''' distorted photo, not a rounding detail.
    ''' </summary>
    <Fact>
    Public Sub A_large_photo_keeps_its_aspect_ratio_when_shrunk()
        Dim rect = EditorGeometry.FitRect(New Size(6000, 4000), New Size(1200, 900))

        Assert.Equal(1200, rect.Width)
        Assert.Equal(800, rect.Height)
        Assert.True(rect.Width <= 1200 AndAlso rect.Height <= 900, "The picture must stay inside the canvas.")
        Assert.Equal(1.5, rect.Width / CDbl(rect.Height), precision:=3)
    End Sub

    ''' <summary>
    ''' A canvas barely bigger than nothing still has to produce a drawable rectangle:
    ''' a zero-sized one is a GDI+ exception on the first paint, and the canvas passes
    ''' through this size while the window is being laid out.
    ''' </summary>
    <Fact>
    Public Sub A_tiny_canvas_still_yields_at_least_one_pixel()
        Dim rect = EditorGeometry.FitRect(New Size(6000, 4000), New Size(1, 1))

        Assert.True(rect.Width >= 1, "Width collapsed to " & rect.Width)
        Assert.True(rect.Height >= 1, "Height collapsed to " & rect.Height)
    End Sub

    <Theory>
    <InlineData(0, 100, 500, 500)>
    <InlineData(100, 0, 500, 500)>
    <InlineData(100, 100, 0, 500)>
    <InlineData(100, 100, 500, 0)>
    <InlineData(-10, 100, 500, 500)>
    Public Sub A_degenerate_size_yields_an_empty_rectangle(imageWidth As Integer, imageHeight As Integer,
                                                           canvasWidth As Integer, canvasHeight As Integer)
        Dim rect = EditorGeometry.FitRect(New Size(imageWidth, imageHeight),
                                          New Size(canvasWidth, canvasHeight))

        Assert.Equal(Rectangle.Empty, rect)
    End Sub

    ' ------------------------------------------------------- canvas -> image ----
    '
    ' The mapping the project did not have (the OCR overlay only ever needed the
    ' forward one), and the one every stroke of every tool goes through.

    ''' <summary>The round trip that has to hold: fit the picture, ask what is under the
    ''' middle of it, get the middle of the picture.</summary>
    <Fact>
    Public Sub The_centre_of_the_fitted_picture_is_the_centre_of_the_picture()
        Dim imageSize As New Size(6000, 4000)
        Dim fit = EditorGeometry.FitRect(imageSize, New Size(1200, 900))

        Dim middle = EditorGeometry.CanvasToImage(
            New Point(fit.Left + fit.Width \ 2, fit.Top + fit.Height \ 2), fit, imageSize)

        Assert.Equal(3000, middle.X)
        Assert.Equal(2000, middle.Y)
    End Sub

    <Fact>
    Public Sub The_corners_of_the_fitted_picture_map_to_the_corners_of_the_picture()
        Dim imageSize As New Size(800, 400)
        Dim fit As New Rectangle(100, 50, 400, 200)   ' scale 0.5

        Assert.Equal(New Point(0, 0), EditorGeometry.CanvasToImage(New Point(100, 50), fit, imageSize))
        Assert.Equal(New Point(799, 399), EditorGeometry.CanvasToImage(New Point(500, 250), fit, imageSize))
    End Sub

    ''' <summary>
    ''' The mouse spends much of a drag on the margin around the fitted picture, and a
    ''' negative or past-the-end pixel is either a GDI+ exception or a stroke that silently
    ''' goes nowhere. Clamping turns "dragged past the corner" into "along the edge",
    ''' which is what the hand meant.
    ''' </summary>
    <Theory>
    <InlineData(-500, -500, 0, 0)>
    <InlineData(99, 49, 0, 0)>
    <InlineData(5000, 5000, 799, 399)>
    <InlineData(300, -40, 400, 0)>
    Public Sub A_point_outside_the_picture_is_clamped_onto_it(canvasX As Integer, canvasY As Integer,
                                                              expectedX As Integer, expectedY As Integer)
        Dim mapped = EditorGeometry.CanvasToImage(New Point(canvasX, canvasY),
                                                  New Rectangle(100, 50, 400, 200),
                                                  New Size(800, 400))

        Assert.Equal(New Point(expectedX, expectedY), mapped)
    End Sub

    ''' <summary>A canvas mid-layout has a zero-sized fit, and the mouse can be over it.</summary>
    <Theory>
    <InlineData(0, 200, 800, 400)>
    <InlineData(400, 0, 800, 400)>
    <InlineData(400, 200, 0, 400)>
    <InlineData(400, 200, 800, 0)>
    Public Sub A_degenerate_fit_or_picture_maps_to_nothing(fitWidth As Integer, fitHeight As Integer,
                                                            imageWidth As Integer, imageHeight As Integer)
        Dim mapped = EditorGeometry.CanvasToImage(New Point(120, 60),
                                                  New Rectangle(100, 50, fitWidth, fitHeight),
                                                  New Size(imageWidth, imageHeight))

        Assert.Equal(Point.Empty, mapped)
    End Sub

    ' ------------------------------------------------------------ the gesture ----

    ''' <summary>
    ''' Dragging up and to the left is how half of all rectangles get drawn, and it
    ''' produces a negative width - which GDI+ renders as nothing at all rather than
    ''' complaining, so the bug would look like "the tool sometimes does not work".
    ''' </summary>
    <Theory>
    <InlineData(10, 10, 60, 40)>
    <InlineData(60, 40, 10, 10)>
    <InlineData(10, 40, 60, 10)>
    <InlineData(60, 10, 10, 40)>
    Public Sub A_drag_in_any_direction_yields_the_same_rectangle(anchorX As Integer, anchorY As Integer,
                                                                 currentX As Integer, currentY As Integer)
        Dim rect = EditorGeometry.NormalizeDrag(New Point(anchorX, anchorY), New Point(currentX, currentY))

        Assert.Equal(New Rectangle(10, 10, 50, 30), rect)
    End Sub

    <Fact>
    Public Sub A_drag_that_never_moved_is_an_empty_rectangle()
        Dim rect = EditorGeometry.NormalizeDrag(New Point(42, 17), New Point(42, 17))

        Assert.Equal(0, rect.Width)
        Assert.Equal(0, rect.Height)
        Assert.Equal(New Point(42, 17), rect.Location)
    End Sub

    ''' <summary>
    ''' Shift gives a square, and it is the SHORTER side that wins: growing the shape past
    ''' the cursor would both surprise and, near the edge of the picture, run off it.
    ''' </summary>
    <Theory>
    <InlineData(100, 100, 180, 130, 130, 130)>
    <InlineData(100, 100, 130, 180, 130, 130)>
    <InlineData(100, 100, 20, 60, 60, 60)>
    <InlineData(100, 100, 160, 40, 160, 40)>
    Public Sub Shift_squares_the_gesture_off_the_shorter_side(anchorX As Integer, anchorY As Integer,
                                                              currentX As Integer, currentY As Integer,
                                                              expectedX As Integer, expectedY As Integer)
        Dim squared = EditorGeometry.ConstrainToSquare(New Point(anchorX, anchorY),
                                                       New Point(currentX, currentY))

        Assert.Equal(New Point(expectedX, expectedY), squared)

        Dim rect = EditorGeometry.NormalizeDrag(New Point(anchorX, anchorY), squared)
        Assert.Equal(rect.Width, rect.Height)
    End Sub

    ' --- the crop frame (Ф-4, §6.1) ------------------------------------------------
    '
    ' Two things make these worth writing rather than checking by eye. A frame that leaves
    ' the picture crops in transparent nothing along that side, and a frame of zero size is
    ' New Bitmap(0, 0) - a GDI+ exception rather than an empty picture. Both are one
    ' overshooting drag away, and neither is visible until it happens.

    <Fact>
    Public Sub A_frame_hanging_over_the_edge_is_pulled_back_inside()
        Dim clamped = EditorGeometry.ClampCropRect(New Rectangle(-40, -30, 200, 200), New Size(100, 80))

        Assert.Equal(New Rectangle(0, 0, 100, 80), clamped)
    End Sub

    <Fact>
    Public Sub A_frame_of_no_size_becomes_one_pixel_rather_than_nothing()
        Dim clamped = EditorGeometry.ClampCropRect(New Rectangle(50, 40, 0, 0), New Size(100, 80))

        Assert.Equal(1, clamped.Width)
        Assert.Equal(1, clamped.Height)
    End Sub

    <Fact>
    Public Sub A_frame_starting_past_the_last_pixel_still_lands_inside()
        Dim clamped = EditorGeometry.ClampCropRect(New Rectangle(500, 500, 10, 10), New Size(100, 80))

        Assert.True(clamped.Right <= 100 AndAlso clamped.Bottom <= 80)
        Assert.True(clamped.Width >= 1 AndAlso clamped.Height >= 1)
    End Sub

    <Fact>
    Public Sub A_frame_in_image_pixels_maps_onto_the_canvas_it_is_drawn_in()
        ' A 1000x500 picture fitted into a 500x500 canvas: half size, offset 125 down.
        Dim fit = EditorGeometry.FitRect(New Size(1000, 500), New Size(500, 500))
        Dim onCanvas = EditorGeometry.ImageToCanvas(New Rectangle(100, 100, 200, 200), fit, New Size(1000, 500))

        Assert.Equal(50, onCanvas.Left)
        Assert.Equal(125 + 50, onCanvas.Top)
        Assert.Equal(100, onCanvas.Width)
        Assert.Equal(100, onCanvas.Height)
    End Sub

    ''' <summary>The round trip that matters: what is drawn has to be what can be grabbed.</summary>
    <Fact>
    Public Sub A_frame_maps_to_the_canvas_and_back_to_itself()
        Dim imageSize As New Size(1200, 900)
        Dim fit = EditorGeometry.FitRect(imageSize, New Size(600, 600))
        Dim frame As New Rectangle(300, 150, 600, 450)

        Dim onCanvas = EditorGeometry.ImageToCanvas(frame, fit, imageSize)
        Dim backTopLeft = EditorGeometry.CanvasToImage(New Point(onCanvas.Left, onCanvas.Top), fit, imageSize)

        Assert.Equal(frame.Left, backTopLeft.X)
        Assert.Equal(frame.Top, backTopLeft.Y)
    End Sub

    ''' <summary>All ten answers in one test rather than a Theory: CropHandle is Friend, and
    ''' an InlineData parameter of that type would have to be exposed by a Public test
    ''' method - which is exactly the kind of visibility widening a test must not force on
    ''' the code it covers.</summary>
    <Fact>
    Public Sub Every_grip_is_where_it_is_drawn()
        Dim frame As New Rectangle(100, 100, 200, 100)   ' 100..300 x 100..200
        Dim handleAt As Func(Of Integer, Integer, EditorGeometry.CropHandle) =
            Function(x, y) EditorGeometry.CropHandleAt(frame, New Point(x, y), 7)

        Assert.Equal(EditorGeometry.CropHandle.TopLeft, handleAt(100, 100))
        Assert.Equal(EditorGeometry.CropHandle.TopRight, handleAt(300, 100))
        Assert.Equal(EditorGeometry.CropHandle.BottomLeft, handleAt(100, 200))
        Assert.Equal(EditorGeometry.CropHandle.BottomRight, handleAt(300, 200))
        Assert.Equal(EditorGeometry.CropHandle.Top, handleAt(200, 100))
        Assert.Equal(EditorGeometry.CropHandle.Bottom, handleAt(200, 200))
        Assert.Equal(EditorGeometry.CropHandle.Left, handleAt(100, 150))
        Assert.Equal(EditorGeometry.CropHandle.Right, handleAt(300, 150))
        Assert.Equal(EditorGeometry.CropHandle.Inside, handleAt(200, 150))
        Assert.Equal(EditorGeometry.CropHandle.None, handleAt(500, 500))
    End Sub

    ''' <summary>A corner wins over the two sides that meet there. On a frame narrower than
    ''' two tolerances every point is near both vertical edges, and "resize the corner" is
    ''' what a hand at a corner means.</summary>
    <Fact>
    Public Sub A_corner_beats_the_sides_that_meet_there()
        Dim tiny As New Rectangle(100, 100, 4, 4)

        Assert.Equal(EditorGeometry.CropHandle.TopLeft,
                     EditorGeometry.CropHandleAt(tiny, New Point(100, 100), 7))
    End Sub

    <Fact>
    Public Sub Dragging_a_corner_moves_two_edges_and_leaves_the_others()
        Dim resized = EditorGeometry.ResizeCrop(New Rectangle(100, 100, 200, 100),
                                                EditorGeometry.CropHandle.TopLeft,
                                                New Point(150, 120), New Size(1000, 1000))

        Assert.Equal(Rectangle.FromLTRB(150, 120, 300, 200), resized)
    End Sub

    <Fact>
    Public Sub Dragging_an_edge_past_the_opposite_one_flips_instead_of_going_negative()
        ' The hand overshoots while shrinking - a normal gesture, and a negative width is
        ' something GDI+ draws as nothing at all rather than complaining about.
        Dim resized = EditorGeometry.ResizeCrop(New Rectangle(100, 100, 200, 100),
                                                EditorGeometry.CropHandle.Left,
                                                New Point(400, 150), New Size(1000, 1000))

        Assert.True(resized.Width >= 1)
        Assert.Equal(300, resized.Left)
    End Sub

    <Fact>
    Public Sub A_resized_frame_never_leaves_the_picture()
        Dim resized = EditorGeometry.ResizeCrop(New Rectangle(10, 10, 50, 50),
                                                EditorGeometry.CropHandle.BottomRight,
                                                New Point(9000, 9000), New Size(100, 80))

        Assert.Equal(Rectangle.FromLTRB(10, 10, 100, 80), resized)
    End Sub

    <Fact>
    Public Sub Moving_the_frame_keeps_its_size_and_stops_at_the_edge()
        Dim moved = EditorGeometry.MoveCrop(New Rectangle(10, 10, 50, 40), 9000, 9000, New Size(100, 80))

        ' Stopped, not clipped: a frame that shrank as it was dragged into a corner would
        ' silently change what is about to be cut off.
        Assert.Equal(50, moved.Width)
        Assert.Equal(40, moved.Height)
        Assert.Equal(50, moved.Left)
        Assert.Equal(40, moved.Top)
    End Sub

    <Fact>
    Public Sub Moving_the_frame_against_the_near_edge_stops_at_zero()
        Dim moved = EditorGeometry.MoveCrop(New Rectangle(10, 10, 50, 40), -9000, -9000, New Size(100, 80))

        Assert.Equal(New Rectangle(0, 0, 50, 40), moved)
    End Sub

End Class
#End If
