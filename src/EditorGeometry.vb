#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing

''' <summary>
''' Where the picture lies on the editor's canvas (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §5).
'''
''' Separate from the editor window for the same reason <see cref="ZoomMath"/> is separate
''' from Main_Form.Zoom.vb: this is a pure function of its arguments, and every pixel the
''' editor will eventually draw is placed relative to the rectangle it returns - so it is
''' worth proving in a test rather than by eye.
'''
''' Modern-only, like the editor: the whole file compiles to nothing in the x86 viewer.
''' </summary>
Friend Module EditorGeometry

    ''' <summary>
    ''' The image rectangle inside a canvas of <paramref name="canvasSize"/>: fitted and
    ''' centred, never enlarged.
    '''
    ''' Fit comes from <see cref="ZoomMath.FitFactor"/> - the very function the viewer
    ''' computes "fit in window" with, and one that is already under test - so the editor
    ''' and the viewer frame the same picture identically.
    '''
    ''' <b>A small picture is not stretched</b> (the factor is capped at 1.0). Editing is
    ''' about pixels, and a 200x150 icon blown up to fill a 1400-pixel canvas would be
    ''' drawn on at seven screen pixels per image pixel - every stroke landing somewhere
    ''' other than where it looked.
    ''' </summary>
    Friend Function FitRect(imageSize As Size, canvasSize As Size) As Rectangle
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 Then Return Rectangle.Empty
        If canvasSize.Width <= 0 OrElse canvasSize.Height <= 0 Then Return Rectangle.Empty

        Dim factor As Double = ZoomMath.FitFactor(imageSize.Width, imageSize.Height,
                                                  canvasSize.Width, canvasSize.Height)
        If factor > 1.0 Then factor = 1.0

        ' At least one pixel each way: a zero-sized rectangle is a GDI+ exception waiting
        ' for the first draw, and a canvas this small has no useful geometry anyway.
        Dim width As Integer = Math.Max(1, CInt(Math.Round(imageSize.Width * factor)))
        Dim height As Integer = Math.Max(1, CInt(Math.Round(imageSize.Height * factor)))

        Return New Rectangle((canvasSize.Width - width) \ 2, (canvasSize.Height - height) \ 2,
                             width, height)
    End Function

    ''' <summary>
    ''' A point on the canvas -> the pixel of the image under it (§5). The reverse of
    ''' <see cref="FitRect"/>, and the one mapping the project did not have: the OCR
    ''' overlay only ever needed image -> screen.
    '''
    ''' <b>Clamped into the picture</b>, deliberately. The fitted image is surrounded by
    ''' margin on two sides, the mouse crosses onto it constantly during a drag, and a
    ''' stroke placed at a negative coordinate is either an exception or a silent
    ''' nothing. Clamping means a gesture that leaves the picture keeps drawing along
    ''' its edge - which is what a hand dragging a rectangle "past the corner" means.
    ''' </summary>
    Friend Function CanvasToImage(canvasPoint As Point, fit As Rectangle, imageSize As Size) As Point
        If fit.Width <= 0 OrElse fit.Height <= 0 Then Return Point.Empty
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 Then Return Point.Empty

        Dim x As Double = (canvasPoint.X - fit.Left) * imageSize.Width / CDbl(fit.Width)
        Dim y As Double = (canvasPoint.Y - fit.Top) * imageSize.Height / CDbl(fit.Height)

        Return New Point(ClampToRange(x, imageSize.Width - 1), ClampToRange(y, imageSize.Height - 1))
    End Function

    Private Function ClampToRange(value As Double, upper As Integer) As Integer
        If value <= 0 Then Return 0
        If value >= upper Then Return upper
        Return CInt(Math.Round(value))
    End Function

    ''' <summary>
    ''' The rectangle two dragged points describe (§6). Dragging right-to-left or
    ''' bottom-to-top is the normal way to draw one, and it produces a negative width -
    ''' which GDI+ draws as nothing at all rather than complaining.
    ''' </summary>
    Friend Function NormalizeDrag(anchor As Point, current As Point) As Rectangle
        Return Rectangle.FromLTRB(Math.Min(anchor.X, current.X), Math.Min(anchor.Y, current.Y),
                                  Math.Max(anchor.X, current.X), Math.Max(anchor.Y, current.Y))
    End Function

    ''' <summary>
    ''' Where the dragged corner goes when Shift is held (§6): a square, or a circle once
    ''' the ellipse is inscribed in it.
    '''
    ''' The shorter side wins rather than the longer one, so the shape stays inside the
    ''' gesture the hand made - growing it past the cursor is the surprising answer, and
    ''' near the edge of the picture it would also grow past the edge.
    ''' </summary>
    Friend Function ConstrainToSquare(anchor As Point, current As Point) As Point
        Dim dx As Integer = current.X - anchor.X
        Dim dy As Integer = current.Y - anchor.Y
        Dim side As Integer = Math.Min(Math.Abs(dx), Math.Abs(dy))

        Return New Point(anchor.X + If(dx < 0, -side, side),
                         anchor.Y + If(dy < 0, -side, side))
    End Function

    ' --- the crop frame (Ф-4, §6.1) ------------------------------------------------
    '
    ' The frame lives in IMAGE coordinates, like every other tool, and is drawn through the
    ' canvas transform. Its HANDLES are the one thing that cannot: an 8-pixel grip measured
    ' in image pixels is 1.6 screen pixels on a 24-megapixel photo - impossible to hit -
    ' so hit-testing happens in canvas coordinates, which is why ImageToCanvas exists.

    ''' <summary>Which part of the crop frame a point is on. <c>Inside</c> drags the whole
    ''' frame, the eight named ones resize it, <c>None</c> starts a new one.</summary>
    Friend Enum CropHandle
        None
        Inside
        TopLeft
        Top
        TopRight
        Right
        BottomRight
        Bottom
        BottomLeft
        Left
    End Enum

    ''' <summary>
    ''' The frame, forced inside the picture and never smaller than one pixel.
    '''
    ''' Both halves are load-bearing. A frame that hangs over the edge would crop in
    ''' transparent nothing along that side; a zero-sized one is <c>New Bitmap(0, 0)</c>,
    ''' which is a GDI+ exception rather than an empty picture.
    ''' </summary>
    Friend Function ClampCropRect(rect As Rectangle, imageSize As Size) As Rectangle
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 Then Return Rectangle.Empty

        Dim left As Integer = Math.Max(0, Math.Min(rect.Left, imageSize.Width - 1))
        Dim top As Integer = Math.Max(0, Math.Min(rect.Top, imageSize.Height - 1))
        Dim right As Integer = Math.Min(imageSize.Width, Math.Max(rect.Right, left + 1))
        Dim bottom As Integer = Math.Min(imageSize.Height, Math.Max(rect.Bottom, top + 1))

        Return Rectangle.FromLTRB(left, top, right, bottom)
    End Function

    ''' <summary>
    ''' An image rectangle expressed on the canvas - the forward direction of
    ''' <see cref="CanvasToImage"/>, needed because the frame is hit-tested and its grips
    ''' are drawn in screen pixels while the frame itself is stored in image pixels.
    ''' </summary>
    Friend Function ImageToCanvas(rect As Rectangle, fit As Rectangle, imageSize As Size) As Rectangle
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 Then Return Rectangle.Empty
        If fit.Width <= 0 OrElse fit.Height <= 0 Then Return Rectangle.Empty

        Dim scale_X As Double = fit.Width / CDbl(imageSize.Width)
        Dim scale_Y As Double = fit.Height / CDbl(imageSize.Height)

        Dim left As Integer = fit.Left + CInt(Math.Round(rect.Left * scale_X))
        Dim top As Integer = fit.Top + CInt(Math.Round(rect.Top * scale_Y))
        Dim right As Integer = fit.Left + CInt(Math.Round(rect.Right * scale_X))
        Dim bottom As Integer = fit.Top + CInt(Math.Round(rect.Bottom * scale_Y))

        Return Rectangle.FromLTRB(left, top, Math.Max(right, left + 1), Math.Max(bottom, top + 1))
    End Function

    ''' <summary>
    ''' What the mouse is over, in canvas coordinates. Corners are tested before sides
    ''' because they overlap: on a frame narrower than two tolerances every point is near
    ''' both vertical edges, and "resize the corner" is the answer a hand at a corner means.
    ''' </summary>
    Friend Function CropHandleAt(canvasRect As Rectangle, canvasPoint As Point, tolerance As Integer) As CropHandle
        If canvasRect.Width <= 0 OrElse canvasRect.Height <= 0 Then Return CropHandle.None

        Dim grip As Integer = Math.Max(1, tolerance)
        If canvasPoint.X < canvasRect.Left - grip OrElse canvasPoint.X > canvasRect.Right + grip Then Return CropHandle.None
        If canvasPoint.Y < canvasRect.Top - grip OrElse canvasPoint.Y > canvasRect.Bottom + grip Then Return CropHandle.None

        Dim near_Left As Boolean = Math.Abs(canvasPoint.X - canvasRect.Left) <= grip
        Dim near_Right As Boolean = Math.Abs(canvasPoint.X - canvasRect.Right) <= grip
        Dim near_Top As Boolean = Math.Abs(canvasPoint.Y - canvasRect.Top) <= grip
        Dim near_Bottom As Boolean = Math.Abs(canvasPoint.Y - canvasRect.Bottom) <= grip

        If near_Left AndAlso near_Top Then Return CropHandle.TopLeft
        If near_Right AndAlso near_Top Then Return CropHandle.TopRight
        If near_Left AndAlso near_Bottom Then Return CropHandle.BottomLeft
        If near_Right AndAlso near_Bottom Then Return CropHandle.BottomRight
        If near_Left Then Return CropHandle.Left
        If near_Right Then Return CropHandle.Right
        If near_Top Then Return CropHandle.Top
        If near_Bottom Then Return CropHandle.Bottom

        Return CropHandle.Inside
    End Function

    ''' <summary>
    ''' The frame after a grip has been dragged to <paramref name="imagePoint"/>.
    '''
    ''' Dragging one edge PAST the opposite one is normal - a hand shrinking a frame
    ''' overshoots - so the result is normalised rather than refused, exactly as a drawn
    ''' rectangle is. The right and bottom edges are exclusive, hence the +1: the pixel the
    ''' cursor is on has to end up inside the crop, not just outside it.
    ''' </summary>
    Friend Function ResizeCrop(rect As Rectangle, handle As CropHandle, imagePoint As Point, imageSize As Size) As Rectangle
        Dim left As Integer = rect.Left
        Dim top As Integer = rect.Top
        Dim right As Integer = rect.Right
        Dim bottom As Integer = rect.Bottom

        Select Case handle
            Case CropHandle.TopLeft
                left = imagePoint.X : top = imagePoint.Y
            Case CropHandle.Top
                top = imagePoint.Y
            Case CropHandle.TopRight
                right = imagePoint.X + 1 : top = imagePoint.Y
            Case CropHandle.Right
                right = imagePoint.X + 1
            Case CropHandle.BottomRight
                right = imagePoint.X + 1 : bottom = imagePoint.Y + 1
            Case CropHandle.Bottom
                bottom = imagePoint.Y + 1
            Case CropHandle.BottomLeft
                left = imagePoint.X : bottom = imagePoint.Y + 1
            Case CropHandle.Left
                left = imagePoint.X
            Case Else
                Return ClampCropRect(rect, imageSize)
        End Select

        Return ClampCropRect(Rectangle.FromLTRB(Math.Min(left, right), Math.Min(top, bottom),
                                                Math.Max(left, right), Math.Max(top, bottom)), imageSize)
    End Function

    ''' <summary>
    ''' The frame moved as a whole, keeping its size. It stops at the picture's edge
    ''' instead of being clipped by it: a frame that shrank as it was dragged into a corner
    ''' would silently change what is about to be cut.
    ''' </summary>
    Friend Function MoveCrop(rect As Rectangle, dx As Integer, dy As Integer, imageSize As Size) As Rectangle
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 Then Return Rectangle.Empty

        Dim left As Integer = Math.Max(0, Math.Min(rect.Left + dx, imageSize.Width - rect.Width))
        Dim top As Integer = Math.Max(0, Math.Min(rect.Top + dy, imageSize.Height - rect.Height))

        ' A frame wider than the picture (it cannot be made here, but it can arrive from a
        ' crop undone into a smaller bitmap) falls back to the clamp, which shrinks it.
        Return ClampCropRect(New Rectangle(left, top, rect.Width, rect.Height), imageSize)
    End Function

End Module
#End If
