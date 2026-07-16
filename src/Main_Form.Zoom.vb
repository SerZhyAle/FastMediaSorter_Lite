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
Partial Public Class Main_Form

    ' --- tuning (spec 4.1, defaults O-Z-6 / O-Z-7) ---
    Private Const Zoom_Step_Normal As Double = 1.25      ' one wheel notch / NumPad +-
    Private Const Zoom_Step_Fast As Double = 1.5         ' Ctrl + wheel
    Private Const Zoom_Factor_Max As Double = 40.0       ' 4000 %
    Private Const Zoom_Factor_Floor As Double = 0.05     ' 5 %, unless Fit is smaller
    Private Const Zoom_Snap_Tolerance As Double = 0.03   ' +-3 % pull onto Fit / 100 %

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

    ''' <summary>
    ''' Scale the image would have at Fit: it is letterboxed inside the panel, so the
    ''' smaller of the two ratios wins. This is what "Fit" means numerically.
    ''' </summary>
    Private Function FitFactorFor(img As Image) As Double
        If img Is Nothing OrElse img.Width <= 0 OrElse img.Height <= 0 Then Return 1.0
        If panel_Media Is Nothing Then Return 1.0
        Dim panelSize As Size = panel_Media.ClientSize
        If panelSize.Width <= 0 OrElse panelSize.Height <= 0 Then Return 1.0
        Return Math.Min(panelSize.Width / CDbl(img.Width), panelSize.Height / CDbl(img.Height))
    End Function

    ''' <summary>Scale the user is actually looking at right now.</summary>
    Private Function CurrentZoomFactor() As Double
        Dim img As Image = ActiveMediaImage()
        If img Is Nothing Then Return 1.0
        If zoom_Scale = 1 Then Return FitFactorFor(img)   ' at Fit the panel decides
        Return zoom_Factor
    End Function

    Private Function ClampZoomFactor(value As Double, img As Image) As Double
        ' Never force the user above 4000 %, and never below 5 % - except when Fit
        ' itself is smaller than 5 % (a huge image in a small window), where Fit is
        ' the honest floor.
        Dim floor As Double = Math.Min(Zoom_Factor_Floor, FitFactorFor(img))
        Return Math.Max(floor, Math.Min(Zoom_Factor_Max, value))
    End Function

    ''' <summary>
    ''' Pull a nearly-Fit / nearly-100 % scale exactly onto that anchor, so scrolling
    ''' past them actually lands on them (spec 4.1, O-Z-6).
    ''' </summary>
    Private Function SnapZoomFactor(value As Double, img As Image) As Double
        Dim fit As Double = FitFactorFor(img)
        If fit > 0 AndAlso Math.Abs(value - fit) / fit <= Zoom_Snap_Tolerance Then Return fit
        If Math.Abs(value - 1.0) <= Zoom_Snap_Tolerance Then Return 1.0
        Return value
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

        Dim target As Double = ClampZoomFactor(SnapZoomFactor(factor, img), img)

        ' Landing back on Fit is Fit - not a free zoom that merely looks like it.
        If Math.Abs(target - FitFactorFor(img)) < 0.0000001 Then
            ZoomToFit()
            Return
        End If

        ' Where the anchor sits inside the image right now, as a 0..1 fraction. Taken
        ' from the DISPLAYED rectangle, so it is correct both at Fit (image letterboxed
        ' inside a panel-sized box) and while zoomed (box == image rect).
        Dim shown As Rectangle = DisplayedImageRectangleOnPanel(img)
        Dim relX As Double = 0.5
        Dim relY As Double = 0.5
        If shown.Width > 0 AndAlso shown.Height > 0 Then
            relX = (anchorOnPanel.X - shown.X) / CDbl(shown.Width)
            relY = (anchorOnPanel.Y - shown.Y) / CDbl(shown.Height)
            ' Anchor outside the image (on a perspective bar) - fall back to its centre.
            If relX < 0 OrElse relX > 1 Then relX = 0.5
            If relY < 0 OrElse relY > 1 Then relY = 0.5
        End If

        Dim newWidth As Integer = Math.Max(1, CInt(Math.Round(img.Width * target)))
        Dim newHeight As Integer = Math.Max(1, CInt(Math.Round(img.Height * target)))
        Dim newLeft As Integer = CInt(Math.Round(anchorOnPanel.X - relX * newWidth))
        Dim newTop As Integer = CInt(Math.Round(anchorOnPanel.Y - relY * newHeight))

        ' Keep a grabbable strip on screen (the historical guard, kept until the
        ' viewport model makes it unnecessary).
        Const keepVisible As Integer = 100
        newLeft = Math.Max(Math.Min(newLeft, panel_Media.ClientSize.Width - keepVisible), -newWidth + keepVisible)
        newTop = Math.Max(Math.Min(newTop, panel_Media.ClientSize.Height - keepVisible), -newHeight + keepVisible)

        Picture_Box_1.Bounds = New Rectangle(newLeft, newTop, newWidth, newHeight)
        Picture_Box_2.Bounds = Picture_Box_1.Bounds

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
        Dim step_Multiplier As Double = If(fast, Zoom_Step_Fast, Zoom_Step_Normal)
        Dim current As Double = CurrentZoomFactor()
        Dim target As Double = If(zoomIn, current * step_Multiplier, current / step_Multiplier)
        ApplyZoomFactor(target, anchorOnPanel)
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
