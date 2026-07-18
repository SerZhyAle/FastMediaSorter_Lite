#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

' Classic zoom model - modern build only (SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md).
' The x86 viewer keeps the historical mechanics untouched (the spec freezes it), so
' this whole file compiles to nothing there.
'
' WHY THIS IS A SEPARATE LAYER: the input handlers (wheel, NumPad) call the small API
' below - ZoomToFit / ZoomToActualSize / ZoomStepAt - and never touch geometry
' themselves. Zoom is still *expressed* the historical way (resizing the picture
' boxes; SizeMode = Zoom scales the image inside), because the perspective background
' and the OCR overlay both re-derive their geometry from that box rectangle and stay
' aligned for free. Swapping to a viewport/srcRect model (Ф-Z3) means reimplementing
' ApplyZoomFactor + CurrentZoomFactor ONLY - the input layer above does not move.
'
' STATE, and why zoom_Scale is set the way it is: the shared code checks
' `zoom_Scale = 1` for "fit" (left click navigates, no panning) and
' `zoom_Scale = 0 OrElse zoom_Scale > 1` for "zoomed" (drag pans, click does not
' navigate). So here zoom_Scale carries exactly two meanings - 1 = fit, 0 = zoomed -
' and the real scale lives in zoom_Factor. That also fixes a historical bug: the old
' Ctrl+wheel wrote the raw product into zoom_Scale, so zooming OUT (0.9, 0.81..)
' landed below 1 and silently killed panning and click-delegation.
'
' The arithmetic itself (fit / clamp / snap / step / anchor) lives in ZoomMath.vb as
' pure functions so it can be tested; what stays here is the WinForms glue that reads
' panel_Media and moves the boxes.
Partial Public Class Main_Form

    ''' <summary>Live scale relative to the image's native pixels (1.0 = 100 %).
    ''' Only meaningful while zoom_Scale = 0 (free zoom); at Fit the effective scale
    ''' is derived from the panel, see CurrentZoomFactor.</summary>
    Private zoom_Factor As Double = 1.0

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
        If zoom_Scale = 1 Then Return FitFactorFor(img)   ' at Fit the panel decides
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
    ''' Puts the media at an absolute scale, keeping the point under
    ''' <paramref name="anchorOnPanel"/> visually still. Geometry: the boxes are sized
    ''' to the image's own aspect (factor x native), so SizeMode = Zoom maps them 1:1
    ''' with no inner letterbox and the displayed scale IS the factor.
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
        ' it is correct both at Fit (image letterboxed inside a panel-sized box) and
        ' while zoomed (box == image rect).
        Dim fraction As PointF = ZoomMath.AnchorFraction(anchorOnPanel, DisplayedImageRectangleOnPanel(img))
        Dim bounds As Rectangle = ZoomMath.AnchoredBounds(img.Width, img.Height, target,
                                                          anchorOnPanel, fraction, panel_Media.ClientSize)

        Picture_Box_1.Bounds = bounds
        Picture_Box_2.Bounds = bounds

        ' The wheel can turn while the left button is still held: re-anchor the pan, or
        ' the next mouse move hauls the box back to the geometry the pan started from
        ' and this anchored position is lost.
        RebasePan(bounds.Location)

        zoom_Factor = target
        zoom_Scale = 0.0F   ' "not fit" - enables panning, stops click-navigation
        UpdateZoomLabel()
    End Sub

    ''' <summary>
    ''' The image's rectangle in panel coordinates as painted right now. At Fit the box
    ''' spans the panel and SizeMode = Zoom letterboxes inside it; when zoomed the box
    ''' IS the image. Same helper the OCR overlay and the perspective background use,
    ''' so all three agree.
    ''' </summary>
    Private Function DisplayedImageRectangleOnPanel(img As Image) As Rectangle
        If img Is Nothing Then Return Rectangle.Empty
        Dim box As Control = If(is_PictureBox2_Visible, CType(Picture_Box_2, Control), CType(Picture_Box_1, Control))
        Dim inner As Rectangle = GetZoomedImageRectangle(img.Width, img.Height, box.ClientSize.Width, box.ClientSize.Height)
        inner.Offset(box.Left, box.Top)
        Return inner
    End Function

    ''' <summary>Fit (the default on every load/resize). Delegates to the historical
    ''' reset so there is exactly one definition of "fit" in the app.</summary>
    Friend Sub ZoomToFit()
        SkipZoom()
        zoom_Factor = 1.0
        UpdateZoomLabel()
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
        If zoom_Scale = 1 Then
            ' At Fit the number is informative, not a state the user chose - say so.
            lbl_Zoom.Text = If(Is_Russian_Language, "Вписать " & percent.ToString() & " %", "Fit " & percent.ToString() & " %")
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
