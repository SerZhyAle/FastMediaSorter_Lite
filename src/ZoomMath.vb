#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing

' The geometry behind the classic zoom model - modern build only, like the rest of
' the feature (012_SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md), so this whole file
' compiles to nothing in the x86 viewer.
'
' WHY IT IS SEPARATE FROM Main_Form.Zoom.vb: everything here is a pure function of
' its arguments - no control state, no side effects. That is what lets the rules
' (fit / clamp / snap / step / anchor) be proven in tests instead of by eye, and it
' keeps the form partial down to the WinForms glue that reads panel_Media and moves
' the boxes.
Friend Module ZoomMath

    ' --- tuning (spec 4.1, defaults O-Z-6 / O-Z-7) ---
    Friend Const Step_Normal As Double = 1.25       ' one wheel notch / NumPad +-
    Friend Const Step_Fast As Double = 1.5          ' Ctrl + wheel
    Friend Const Factor_Max As Double = 40.0        ' 4000 %
    Friend Const Factor_Floor As Double = 0.05      ' 5 %, unless Fit is smaller
    Friend Const Snap_Tolerance As Double = 0.03    ' +-3 % pull onto Fit / 100 %
    Friend Const Keep_Visible_Px As Integer = 100   ' grabbable strip left on screen

    ''' <summary>
    ''' Scale the image gets at Fit: it is letterboxed inside the panel, so the smaller
    ''' of the two ratios wins. This is what "Fit" means numerically.
    ''' </summary>
    Friend Function FitFactor(imageWidth As Integer, imageHeight As Integer,
                              panelWidth As Integer, panelHeight As Integer) As Double
        If imageWidth <= 0 OrElse imageHeight <= 0 OrElse panelWidth <= 0 OrElse panelHeight <= 0 Then Return 1.0
        Return Math.Min(panelWidth / CDbl(imageWidth), panelHeight / CDbl(imageHeight))
    End Function

    ''' <summary>
    ''' Never force the user above 4000 %, and never below 5 % - except when Fit itself
    ''' is smaller than 5 % (a huge image in a small window), where Fit is the honest
    ''' floor: refusing to show the whole picture would be the bug, not the limit.
    ''' </summary>
    Friend Function Clamp(value As Double, fitFactor As Double) As Double
        Dim lower As Double = Math.Min(Factor_Floor, fitFactor)
        Return Math.Max(lower, Math.Min(Factor_Max, value))
    End Function

    ''' <summary>
    ''' Pull a nearly-Fit / nearly-100 % scale exactly onto that anchor, so stepping
    ''' past them actually lands on them (spec 4.1, O-Z-6). Fit uses a relative
    ''' tolerance because Fit itself can be any scale; at 100 % the two coincide.
    ''' </summary>
    Friend Function Snap(value As Double, fitFactor As Double) As Double
        If fitFactor > 0 AndAlso Math.Abs(value - fitFactor) / fitFactor <= Snap_Tolerance Then Return fitFactor
        If Math.Abs(value - 1.0) <= Snap_Tolerance Then Return 1.0
        Return value
    End Function

    ''' <summary>One notch from the current scale. In and out are exact inverses, so a
    ''' wheel down after a wheel up returns to where you were.</summary>
    Friend Function StepFrom(current As Double, zoomIn As Boolean, fast As Boolean) As Double
        Dim multiplier As Double = If(fast, Step_Fast, Step_Normal)
        Return If(zoomIn, current * multiplier, current / multiplier)
    End Function

    ''' <summary>
    ''' Where the anchor sits inside the displayed image, as a 0..1 fraction of it.
    ''' An anchor outside the image (the cursor is on a perspective bar) has no
    ''' meaningful fraction - zoom about the image centre instead of flinging it away.
    ''' </summary>
    Friend Function AnchorFraction(anchorOnPanel As Point, shown As Rectangle) As PointF
        If shown.Width <= 0 OrElse shown.Height <= 0 Then Return New PointF(0.5F, 0.5F)

        Dim relX As Double = (anchorOnPanel.X - shown.X) / CDbl(shown.Width)
        Dim relY As Double = (anchorOnPanel.Y - shown.Y) / CDbl(shown.Height)
        If relX < 0 OrElse relX > 1 Then relX = 0.5
        If relY < 0 OrElse relY > 1 Then relY = 0.5
        Return New PointF(CSng(relX), CSng(relY))
    End Function

    ''' <summary>
    ''' The box the image gets at <paramref name="factor"/>, placed so the pixel under
    ''' <paramref name="anchorOnPanel"/> does not move - the whole point of zooming at
    ''' the cursor. Then nudged so a grabbable strip stays on screen (a large image can
    ''' otherwise be panned entirely out of the panel and become unreachable).
    ''' </summary>
    Friend Function AnchoredBounds(imageWidth As Integer, imageHeight As Integer, factor As Double,
                                   anchorOnPanel As Point, fraction As PointF, panelSize As Size) As Rectangle
        Dim newWidth As Integer = Math.Max(1, CInt(Math.Round(imageWidth * factor)))
        Dim newHeight As Integer = Math.Max(1, CInt(Math.Round(imageHeight * factor)))

        Dim newLeft As Integer = CInt(Math.Round(anchorOnPanel.X - fraction.X * newWidth))
        Dim newTop As Integer = CInt(Math.Round(anchorOnPanel.Y - fraction.Y * newHeight))

        ' The strip can never exceed the box itself: an image zoomed down to a handful
        ' of pixels would otherwise be shoved away from the panel edge to satisfy a
        ' 100 px promise it is too small to keep.
        Dim keepX As Integer = Math.Min(Keep_Visible_Px, newWidth)
        Dim keepY As Integer = Math.Min(Keep_Visible_Px, newHeight)
        newLeft = Math.Max(Math.Min(newLeft, panelSize.Width - keepX), -newWidth + keepX)
        newTop = Math.Max(Math.Min(newTop, panelSize.Height - keepY), -newHeight + keepY)

        Return New Rectangle(newLeft, newTop, newWidth, newHeight)
    End Function

End Module
#End If
