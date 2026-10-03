#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

' Classic zoom model - modern build only (012_SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md).
' The x86 viewer keeps the historical mechanics untouched (the spec freezes it), so
' this whole file compiles to nothing there.
'
' RENDERING (spec §5): zoom and pan are a viewport. current_ImageBounds is the
' rectangle the WHOLE image occupies on panel_Media - letterboxed inside it at Fit,
' larger than it while zoomed. HqPictureBox paints only the visible strip:
' ZoomMath.ComputeViewport turns the bounds into a SourceRect (native image pixels)
' drawn into a DestRect (panel pixels). The picture boxes stay panel-sized always,
' so SyncMediaSurface never competes with the zoom, there is no box geometry to
' swap, and the perspective background and the OCR overlay re-derive their geometry
' from DisplayedImageRectangleOnPanel - the one rectangle everything agrees on.
'
' STATE: zoom_Mode_Free separates Fit from Free, and zoom_Factor carries the absolute
' scale against the image's native pixels (1.0 = 100 %). The historical zoom_Scale
' triple semantics (1 = fit / 0 = 1:1 flag / >1 = free) are gone from the modern
' build: zoom_Scale stays at 1 here and only the x86 build still writes it. Shared
' code that only needs "is this fitted?" reads MediaIsAtFit() (Main_Form.vb).
'
' The arithmetic itself (fit / clamp / snap / step / anchor / viewport) lives in
' ZoomMath.vb as pure functions so it can be tested; what stays here is the WinForms
' glue that reads panel_Media and feeds the boxes.
Partial Public Class Main_Form

    ''' <summary>Live scale relative to the image's native pixels (1.0 = 100 %).
    ''' At Fit the effective scale is derived from the panel, see CurrentZoomFactor.</summary>
    Private zoom_Factor As Double = 1.0

    ''' <summary>False at Fit; True once the user has zoomed or panned freely.</summary>
    Private zoom_Mode_Free As Boolean = False

    ''' <summary>Where the whole image sits on panel_Media right now (panel
    ''' coordinates). Empty while at Fit - Fit derives it from the panel on demand.</summary>
    Private current_ImageBounds As Rectangle = Rectangle.Empty

    ''' <summary>The image currently on screen, or Nothing when none/video.</summary>
    Private Function ActiveMediaImage() As Image
        If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then Return Picture_Box_1.Image
        If is_PictureBox2_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then Return Picture_Box_2.Image
        Return Nothing
    End Function

    Private Function IsZoomableMediaShown() As Boolean
        Return ActiveMediaImage() IsNot Nothing
    End Function

    ''' <summary>Scale the image would have at Fit, for the panel it is shown in.</summary>
    Private Function FitFactorFor(img As Image) As Double
        If img Is Nothing OrElse panel_Media Is Nothing Then Return 1.0
        Return ZoomMath.FitFactor(img.Width, img.Height, panel_Media.ClientSize.Width, panel_Media.ClientSize.Height)
    End Function

    ''' <summary>Scale the user is actually looking at right now.</summary>
    Private Function CurrentZoomFactor() As Double
        Dim img As Image = ActiveMediaImage()
        If img Is Nothing Then Return 1.0
        If Not zoom_Mode_Free Then Return FitFactorFor(img)   ' at Fit the panel decides
        Return zoom_Factor
    End Function

    ''' <summary>Anchor for a keyboard zoom: the mouse if it is over the media, else
    ''' the panel centre (a key press carries no coordinates - spec 4.3).</summary>
    Private Function CursorAnchorOnPanel() As Point
        If panel_Media Is Nothing Then Return Point.Empty
        Dim client As Point = panel_Media.PointToClient(Cursor.Position)
        If client.X < 0 OrElse client.Y < 0 OrElse
           client.X > panel_Media.ClientSize.Width OrElse client.Y > panel_Media.ClientSize.Height Then
            Return New Point(panel_Media.ClientSize.Width \ 2, panel_Media.ClientSize.Height \ 2)
        End If
        Return client
    End Function

    Private Function PanelCentreAnchor() As Point
        If panel_Media Is Nothing Then Return Point.Empty
        Return New Point(panel_Media.ClientSize.Width \ 2, panel_Media.ClientSize.Height \ 2)
    End Function

    ''' <summary>
    ''' The image's rectangle in panel coordinates as painted right now: the viewport
    ''' bounds while zoomed, the SizeMode = Zoom letterbox at Fit. The one rectangle
    ''' the zoom anchor, the OCR overlay and the perspective background all agree on.
    ''' </summary>
    Private Function DisplayedImageRectangleOnPanel(img As Image) As Rectangle
        If img Is Nothing OrElse panel_Media Is Nothing Then Return Rectangle.Empty
        If zoom_Mode_Free Then Return current_ImageBounds
        Return ZoomMath.FitBounds(img.Width, img.Height, panel_Media.ClientSize)
    End Function

    ''' <summary>
    ''' Puts the media at an absolute scale, keeping the point under
    ''' <paramref name="anchorOnPanel"/> visually still (spec 4.1).
    ''' </summary>
    Private Sub ApplyZoomFactor(factor As Double, anchorOnPanel As Point)
        Dim img As Image = ActiveMediaImage()
        If img Is Nothing OrElse panel_Media Is Nothing Then Return

        Dim fit As Double = FitFactorFor(img)
        Dim target As Double = ZoomMath.Clamp(ZoomMath.Snap(factor, fit), fit)

        ' Landing back on Fit is Fit - not a free zoom that merely looks like it.
        If Math.Abs(target - fit) < 0.0000001 Then
            ZoomToFit()
            Return
        End If

        ' The anchor's place inside the image is read from the DISPLAYED rectangle, so
        ' it is correct both at Fit (letterboxed) and while zoomed (bounds cover the panel).
        Dim fraction As PointF = ZoomMath.AnchorFraction(anchorOnPanel, DisplayedImageRectangleOnPanel(img))
        Dim bounds As Rectangle = ZoomMath.AnchoredBounds(img.Width, img.Height, target,
                                                          anchorOnPanel, fraction, panel_Media.ClientSize)

        SetFreeViewport(bounds, target, img)

        ' The wheel can turn while the left button is still held: re-anchor the pan, or
        ' the next mouse move hauls the image back to the geometry the pan started from
        ' and this anchored position is lost.
        RebasePan(bounds.Location)
    End Sub

    ''' <summary>Enters Free zoom: the image rect goes where the anchor math put it and
    ''' the boxes paint only what the panel can see of it.</summary>
    Private Sub SetFreeViewport(bounds As Rectangle, factor As Double, img As Image)
        current_ImageBounds = bounds
        zoom_Factor = factor
        zoom_Mode_Free = True
        ApplyViewportToBoxes(img)
        ' The fit-time bars are panel-sized: under an enlarged viewport they would show
        ' through around the image and tile across it (audit B-12). Zoomed has no bars.
        ClearPerspectiveBackground()
        UpdateZoomLabel()
        RememberFolderZoom(factor)
        ' A PDF page zoomed past its render resolution is re-rendered sharper.
        RefreshPdfResolution()
    End Sub

    ''' <summary>Feeds the viewport pair to both boxes of the swap pair and repaints
    ''' them. Degenerate panels come back with empty rects, which the box reads as
    ''' "draw the base way" for that paint.</summary>
    Private Sub ApplyViewportToBoxes(img As Image)
        If img Is Nothing OrElse panel_Media Is Nothing Then Return

        Dim dest As Rectangle = Rectangle.Empty
        Dim src As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(current_ImageBounds, img.Width, img.Height, panel_Media.ClientSize, dest, src)

        Picture_Box_1.SourceRect = src
        Picture_Box_1.DestRect = dest
        Picture_Box_1.Invalidate()
        Picture_Box_2.SourceRect = src
        Picture_Box_2.DestRect = dest
        Picture_Box_2.Invalidate()
    End Sub

    ''' <summary>Back to Fit: viewport dropped, boxes paint the whole image per
    ''' SizeMode = Zoom again. Called from SkipZoom, so it stays cheap enough to run
    ''' on every resize.</summary>
    Friend Sub ResetZoomViewport()
        zoom_Mode_Free = False
        zoom_Factor = 1.0
        current_ImageBounds = Rectangle.Empty
        If Picture_Box_1 IsNot Nothing Then
            Picture_Box_1.SourceRect = Rectangle.Empty
            Picture_Box_1.DestRect = Rectangle.Empty
            Picture_Box_1.Invalidate()
        End If
        If Picture_Box_2 IsNot Nothing Then
            Picture_Box_2.SourceRect = Rectangle.Empty
            Picture_Box_2.DestRect = Rectangle.Empty
            Picture_Box_2.Invalidate()
        End If
    End Sub

    ''' <summary>Fit (the default on every load/resize). Delegates to the shared reset
    ''' so there is exactly one definition of "fit" in the app.</summary>
    Friend Sub ZoomToFit()
        Dim was_Zoomed As Boolean = zoom_Mode_Free
        SkipZoom()
        UpdateZoomLabel()
        ForgetFolderZoom()
        ' Back at Fit the bars come back (a zoom took them down - audit B-12). Not when
        ' the display path fits on its own before a new photo: that one draws anyway.
        If was_Zoomed AndAlso Not is_Zoom_Applied_By_Program AndAlso is_form_shown Then Draw_Perspective()
    End Sub

    ''' <summary>100 % - one image pixel per screen pixel (NumPad *, Shift+wheel).</summary>
    Friend Sub ZoomToActualSize(anchorOnPanel As Point)
        If Not IsZoomableMediaShown() Then Return
        ApplyZoomFactor(1.0, anchorOnPanel)
    End Sub

    ''' <summary>One zoom notch about an anchor point.</summary>
    Friend Sub ZoomStepAt(zoomIn As Boolean, fast As Boolean, anchorOnPanel As Point)
        If Not IsZoomableMediaShown() Then Return
        ApplyZoomFactor(ZoomMath.StepFrom(CurrentZoomFactor(), zoomIn, fast), anchorOnPanel)
    End Sub

    ''' <summary>Percent readout (spec 4.1): "Fit 38 %" / "100 %" / "250 %".</summary>
    Private Sub UpdateZoomLabel()
        If lbl_Zoom Is Nothing Then Return
        Dim img As Image = ActiveMediaImage()
        If img Is Nothing Then
            lbl_Zoom.Text = ""
            Return
        End If

        Dim percent As Integer = CInt(Math.Round(CurrentZoomFactor() * 100))
        If Not zoom_Mode_Free Then
            ' At Fit the number is informative, not a state the user chose - say so.
            lbl_Zoom.Text = Localization.TF("Вписать {0} %", percent.ToString())
        Else
            lbl_Zoom.Text = percent.ToString() & " %"
        End If
    End Sub

    ''' <summary>
    ''' The single wheel decision (spec 4.2/4.4). Returns True when the wheel was spent
    ''' on zooming, so the caller must NOT also flip to the next file.
    ''' Default stays "the wheel navigates" - 14 years of muscle memory; zoom-on-wheel
    ''' is strictly opt-in. Modifiers keep their historical meaning either way, and the
    ''' wheel over video always navigates (VLC's surface is not ours to scale).
    ''' </summary>
    Private Function TryHandleWheelZoom(e As MouseEventArgs) As Boolean
        If e.Delta = 0 Then Return False
        If Not IsZoomableMediaShown() Then Return False

        Dim anchor As Point = If(panel_Media Is Nothing, Point.Empty, panel_Media.PointToClient(Me.PointToScreen(e.Location)))
        Dim zoomIn As Boolean = e.Delta > 0

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then Return False   ' handled as "reset to fit"
        If (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
            ZoomToActualSize(anchor)
            Return True
        End If
        If (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            ZoomStepAt(zoomIn, True, anchor)
            Return True
        End If

        If Zoom_Wheel_Zooms Then
            ZoomStepAt(zoomIn, False, anchor)
            Return True
        End If

        Return False   ' default: let the caller navigate
    End Function

    ''' <summary>NumPad zoom (spec 4.3). Only the grey block - every Ctrl combo is
    ''' already taken, and NumPad 0..9 belong to sorting. Returns True if consumed.</summary>
    Private Function TryHandleZoomKey(e As KeyEventArgs) As Boolean
        If IsTextDocumentShown() Then
            Select Case e.KeyCode
                Case Keys.Add : ZoomText(0.1F)
                Case Keys.Subtract : ZoomText(-0.1F)
                Case Keys.Divide : ResetTextZoom()
                Case Else : Return False
            End Select
            Return True
        End If
        Select Case e.KeyCode
            Case Keys.Add
                ZoomStepAt(True, False, CursorAnchorOnPanel())
            Case Keys.Subtract
                ZoomStepAt(False, False, CursorAnchorOnPanel())
            Case Keys.Divide
                ZoomToFit()
            Case Keys.Multiply
                ZoomToActualSize(CursorAnchorOnPanel())
            Case Else
                Return False
        End Select
        Return True
    End Function

End Class
#End If
