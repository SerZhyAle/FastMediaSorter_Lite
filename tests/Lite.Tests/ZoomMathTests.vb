#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

' The zoom geometry of the .NET 10 viewer (012_SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md).
' Modern-only, exactly like the feature: ZoomMath.vb is whole-file "#If Not NETFRAMEWORK",
' so on the net48 leg of this project both it and this file compile to nothing.
'
' These cover the rules a user would otherwise have to notice by eye - "the pixel under
' the cursor must not move", "stepping past 100 % must land on it", "a huge image may go
' below the 5 % floor because Fit is the honest limit there".
Public Class ZoomMathTests

    Private Const Tolerance As Double = 0.000001

    ' --- Fit -----------------------------------------------------------------

    <Fact>
    Public Sub FitFactor_WideImage_IsLimitedByWidth()
        ' 4000x1000 into 800x600: width needs 0.2, height allows 0.6 - the picture must
        ' fit whole, so the smaller wins.
        Assert.Equal(0.2, ZoomMath.FitFactor(4000, 1000, 800, 600), 6)
    End Sub

    <Fact>
    Public Sub FitFactor_TallImage_IsLimitedByHeight()
        Assert.Equal(0.15, ZoomMath.FitFactor(1000, 4000, 800, 600), 6)
    End Sub

    <Fact>
    Public Sub FitFactor_SmallImage_ScalesUpToThePanel()
        ' Fit is not capped at 100 % - a 200x150 thumbnail in a 800x600 panel fits at 4x.
        Assert.Equal(4.0, ZoomMath.FitFactor(200, 150, 800, 600), 6)
    End Sub

    <Theory>
    <InlineData(0, 100, 800, 600)>
    <InlineData(100, 0, 800, 600)>
    <InlineData(100, 100, 0, 600)>
    <InlineData(100, 100, 800, 0)>
    <InlineData(-5, 100, 800, 600)>
    Public Sub FitFactor_DegenerateInput_FallsBackToOneHundredPercent(imgW As Integer, imgH As Integer, panelW As Integer, panelH As Integer)
        ' A zero-sized panel happens for real - the form is laid out before it is shown.
        ' Dividing by it must not produce Infinity/NaN and poison every later factor.
        Assert.Equal(1.0, ZoomMath.FitFactor(imgW, imgH, panelW, panelH), 6)
    End Sub

    ' --- Clamp ---------------------------------------------------------------

    <Fact>
    Public Sub Clamp_CapsAtFourThousandPercent()
        Assert.Equal(ZoomMath.Factor_Max, ZoomMath.Clamp(1000.0, 0.5), 6)
    End Sub

    <Fact>
    Public Sub Clamp_FloorsAtFivePercent()
        Assert.Equal(ZoomMath.Factor_Floor, ZoomMath.Clamp(0.001, 0.5), 6)
    End Sub

    <Fact>
    Public Sub Clamp_WhenFitIsBelowTheFloor_FitBecomesTheFloor()
        ' A 40 MP panorama in a small window fits at ~2 %. Refusing to go below 5 % would
        ' mean never showing the whole picture - so Fit itself is the limit there.
        Dim fit As Double = 0.02
        Assert.Equal(fit, ZoomMath.Clamp(0.001, fit), 6)
        Assert.Equal(0.03, ZoomMath.Clamp(0.03, fit), 6)   ' between Fit and 5 % is legal
    End Sub

    <Fact>
    Public Sub Clamp_WhenFitIsAboveTheCeiling_ZoomingInNeverShrinks()
        ' Audit B-09: a 16x16 icon in a 1080p panel fits at 67.5x. One notch in used to
        ' be clamped to 40x - "zoom in" made the picture smaller.
        Dim fit As Double = 67.5
        Assert.True(ZoomMath.Clamp(84.4, fit) >= fit)
        Dim stepped As Double = ZoomMath.Clamp(ZoomMath.Snap(ZoomMath.StepFrom(fit, True, False), fit), fit)
        Assert.True(stepped >= fit, "stepped to " & stepped.ToString())
    End Sub

    <Fact>
    Public Sub Clamp_LeavesAnOrdinaryScaleAlone()
        Assert.Equal(2.5, ZoomMath.Clamp(2.5, 0.5), 6)
    End Sub

    ' --- Snap ----------------------------------------------------------------

    <Fact>
    Public Sub Snap_NearlyOneHundredPercent_LandsExactlyOnIt()
        Assert.Equal(1.0, ZoomMath.Snap(1.02, 0.4), 6)
        Assert.Equal(1.0, ZoomMath.Snap(0.98, 0.4), 6)
    End Sub

    <Fact>
    Public Sub Snap_NearlyFit_LandsExactlyOnFit()
        ' Fit uses a RELATIVE tolerance: at fit = 0.4 a 3 % pull is +-0.012, so 0.405 is
        ' inside it but 0.44 is a deliberate zoom the user asked for.
        Assert.Equal(0.4, ZoomMath.Snap(0.405, 0.4), 6)
        Assert.Equal(0.44, ZoomMath.Snap(0.44, 0.4), 6)
    End Sub

    <Fact>
    Public Sub Snap_LeavesAClearZoomAlone()
        Assert.Equal(2.5, ZoomMath.Snap(2.5, 0.4), 6)
    End Sub

    <Fact>
    Public Sub Snap_WhenFitIsNearOneHundredPercent_FitWins()
        ' Both anchors are in reach; Fit is checked first, and it is the state the rest
        ' of the app can actually represent (zoom_Scale = 1).
        Assert.Equal(0.99, ZoomMath.Snap(1.0, 0.99), 6)
    End Sub

    <Fact>
    Public Sub Snap_DegenerateFit_StillSnapsToOneHundredPercent()
        Assert.Equal(1.0, ZoomMath.Snap(1.01, 0.0), 6)
    End Sub

    ' --- Step ----------------------------------------------------------------

    <Fact>
    Public Sub StepFrom_InThenOut_ReturnsToTheStart()
        ' A wheel notch down after a notch up must land back where you were - otherwise
        ' the scale drifts on every pass.
        Dim start As Double = 0.37
        Dim zoomed As Double = ZoomMath.StepFrom(start, zoomIn:=True, fast:=False)
        Assert.Equal(start, ZoomMath.StepFrom(zoomed, zoomIn:=False, fast:=False), 6)
    End Sub

    <Fact>
    Public Sub StepFrom_FastMovesFurtherThanNormal()
        Dim normal As Double = ZoomMath.StepFrom(1.0, zoomIn:=True, fast:=False)
        Dim fast As Double = ZoomMath.StepFrom(1.0, zoomIn:=True, fast:=True)
        Assert.True(fast > normal, $"Ctrl+wheel ({fast}) must out-step a plain notch ({normal})")
    End Sub

    <Fact>
    Public Sub StepFrom_ZoomingOut_NeverGoesNegativeOrToZero()
        Dim value As Double = 1.0
        For i As Integer = 1 To 50
            value = ZoomMath.StepFrom(value, zoomIn:=False, fast:=True)
        Next
        Assert.True(value > 0, "dividing must approach zero, never reach or cross it")
    End Sub

    ' --- Anchor fraction -----------------------------------------------------

    <Fact>
    Public Sub AnchorFraction_MapsAPointInsideTheImage()
        Dim shown As New Rectangle(0, 100, 800, 400)
        Dim f As PointF = ZoomMath.AnchorFraction(New Point(200, 200), shown)
        Assert.Equal(0.25F, f.X, 4)
        Assert.Equal(0.25F, f.Y, 4)
    End Sub

    <Fact>
    Public Sub AnchorFraction_PointOnAPerspectiveBar_FallsBackToTheCentre()
        ' The cursor is above the letterboxed image, in the bar. There is no image pixel
        ' under it, so zoom about the centre instead of flinging the picture away.
        Dim shown As New Rectangle(0, 100, 800, 400)
        Dim f As PointF = ZoomMath.AnchorFraction(New Point(200, 20), shown)
        Assert.Equal(0.25F, f.X, 4)
        Assert.Equal(0.5F, f.Y, 4)
    End Sub

    <Fact>
    Public Sub AnchorFraction_EmptyRectangle_IsTheCentre()
        Dim f As PointF = ZoomMath.AnchorFraction(New Point(200, 200), Rectangle.Empty)
        Assert.Equal(0.5F, f.X, 4)
        Assert.Equal(0.5F, f.Y, 4)
    End Sub

    ' --- Anchored bounds -----------------------------------------------------

    <Fact>
    Public Sub AnchoredBounds_ScalesTheBoxToTheImageAspect()
        Dim r As Rectangle = ZoomMath.AnchoredBounds(1000, 500, 2.0, New Point(400, 300),
                                                     New PointF(0.5F, 0.5F), New Size(800, 600))
        Assert.Equal(2000, r.Width)
        Assert.Equal(1000, r.Height)
    End Sub

    <Fact>
    Public Sub AnchoredBounds_KeepsThePixelUnderTheCursorStill()
        ' The rule the whole feature is judged by: zoom at the cursor, and the thing you
        ' were pointing at stays where it was.
        Dim panel As New Size(800, 600)
        Dim anchor As New Point(200, 200)
        Dim fraction As New PointF(0.25F, 0.25F)

        Dim r As Rectangle = ZoomMath.AnchoredBounds(1000, 500, 2.0, anchor, fraction, panel)

        ' Where that same image fraction ended up on the panel.
        Assert.Equal(anchor.X, r.X + CInt(Math.Round(fraction.X * r.Width)))
        Assert.Equal(anchor.Y, r.Y + CInt(Math.Round(fraction.Y * r.Height)))
    End Sub

    <Theory>
    <InlineData(1.5)>
    <InlineData(3.0)>
    <InlineData(7.25)>
    Public Sub AnchoredBounds_HoldsTheAnchorAcrossFactors(factor As Double)
        Dim panel As New Size(800, 600)
        Dim anchor As New Point(410, 305)          ' near the centre: no edge clamping
        Dim fraction As New PointF(0.4F, 0.6F)

        Dim r As Rectangle = ZoomMath.AnchoredBounds(1200, 900, factor, anchor, fraction, panel)

        Assert.InRange(r.X + fraction.X * r.Width, anchor.X - 1, anchor.X + 1)
        Assert.InRange(r.Y + fraction.Y * r.Height, anchor.Y - 1, anchor.Y + 1)
    End Sub

    <Fact>
    Public Sub AnchoredBounds_LeavesAGrabbableStripOnScreen()
        ' Anchoring alone would put this box far off the panel; a big image could then be
        ' panned out of reach entirely, with nothing left to grab.
        Dim panel As New Size(800, 600)
        Dim r As Rectangle = ZoomMath.AnchoredBounds(4000, 3000, 4.0, New Point(790, 590),
                                                     New PointF(0.99F, 0.99F), panel)

        Assert.True(r.Right >= ZoomMath.Keep_Visible_Px, $"box ends at {r.Right}, off the left edge")
        Assert.True(r.Bottom >= ZoomMath.Keep_Visible_Px, $"box ends at {r.Bottom}, above the top edge")
        Assert.True(r.X <= panel.Width - ZoomMath.Keep_Visible_Px, $"box starts at {r.X}, past the right edge")
        Assert.True(r.Y <= panel.Height - ZoomMath.Keep_Visible_Px, $"box starts at {r.Y}, past the bottom edge")
    End Sub

    <Fact>
    Public Sub AnchoredBounds_TinyImage_StaysFullyOnScreen()
        ' A box smaller than the 100 px strip cannot keep 100 px on screen. The strip must
        ' shrink to the box, or the guard would shove a tiny image away from the panel
        ' edge to satisfy a promise it is too small to keep.
        Dim panel As New Size(800, 600)
        Dim r As Rectangle = ZoomMath.AnchoredBounds(200, 200, 0.05, New Point(10, 10),
                                                     New PointF(0.5F, 0.5F), panel)

        Assert.Equal(10, r.Width)
        Assert.True(r.X >= 0, $"a {r.Width} px box was pushed to x={r.X}")
        Assert.True(r.Y >= 0, $"a {r.Height} px box was pushed to y={r.Y}")
    End Sub

    <Fact>
    Public Sub AnchoredBounds_NeverCollapsesToZero()
        ' Rounding an extreme zoom-out down to a 0 px box would make the picture box
        ' vanish and take the media surface with it.
        Dim r As Rectangle = ZoomMath.AnchoredBounds(10, 10, 0.001, New Point(400, 300),
                                                     New PointF(0.5F, 0.5F), New Size(800, 600))
        Assert.True(r.Width >= 1 AndAlso r.Height >= 1)
    End Sub

    ' --- The rules together --------------------------------------------------

    <Fact>
    Public Sub SteppingOutFromOneHundredPercent_LandsOnFit_WhenFitIsNear()
        ' Fit = 0.8, one notch out of 100 % = 0.8 exactly. It must be recognised as Fit
        ' rather than a free zoom that merely looks identical - only Fit re-enables
        ' click-to-navigate.
        Dim fit As Double = 0.8
        Dim stepped As Double = ZoomMath.StepFrom(1.0, zoomIn:=False, fast:=False)
        Assert.Equal(fit, ZoomMath.Clamp(ZoomMath.Snap(stepped, fit), fit), 6)
    End Sub

    <Fact>
    Public Sub ClampRunsAfterSnap_SoASnappedFitBelowTheFloorSurvives()
        ' Order matters: Snap can legitimately produce a scale under 5 % (Fit on a huge
        ' image), and Clamp must not then drag it back up to the floor.
        Dim fit As Double = 0.02
        Dim result As Double = ZoomMath.Clamp(ZoomMath.Snap(0.0201, fit), fit)
        Assert.Equal(fit, result, 6)
    End Sub

    ' --- FitBounds (the viewport's Fit state, spec §5) -----------------------

    <Fact>
    Public Sub FitBounds_WideImage_LetterboxesTopAndBottom()
        ' 4000x1000 into 800x600 fits at 0.2: an 800x200 strip, 200 px of bar above
        ' and below - exactly what SizeMode = Zoom paints.
        Dim r As Rectangle = ZoomMath.FitBounds(4000, 1000, New Size(800, 600))
        Assert.Equal(800, r.Width)
        Assert.Equal(200, r.Height)
        Assert.Equal(0, r.X)
        Assert.Equal(200, r.Y)
    End Sub

    <Fact>
    Public Sub FitBounds_SmallImage_IsCentered()
        Dim r As Rectangle = ZoomMath.FitBounds(400, 300, New Size(800, 600))
        Assert.Equal(New Rectangle(200, 150, 400, 300), r)
    End Sub

    <Fact>
    Public Sub PictureBoxZoomBounds_SmallPortrait_EnlargesToTheDisplayedEdges()
        Dim r As Rectangle = ZoomMath.PictureBoxZoomBounds(400, 500, New Size(1200, 800))
        Assert.Equal(New Rectangle(280, 0, 640, 800), r)
    End Sub

    <Theory>
    <InlineData(0, 100, 800, 600)>
    <InlineData(100, 100, 0, 600)>
    <InlineData(-5, 100, 800, 600)>
    Public Sub FitBounds_DegenerateInput_IsEmpty(imgW As Integer, imgH As Integer, panelW As Integer, panelH As Integer)
        Assert.True(ZoomMath.FitBounds(imgW, imgH, New Size(panelW, panelH)).IsEmpty)
    End Sub

    ' --- ComputeViewport (the paint pair, spec §5) ---------------------------

    <Fact>
    Public Sub ComputeViewport_AtFit_ShowsTheWholeImageInTheLetterbox()
        Dim bounds As Rectangle = ZoomMath.FitBounds(1000, 500, New Size(800, 600))
        Dim dest As Rectangle = Rectangle.Empty
        Dim src As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(bounds, 1000, 500, New Size(800, 600), dest, src)

        ' The panel sees all of the image, and only where the letterbox sits.
        Assert.Equal(New Rectangle(0, 0, 1000, 500), src)
        Assert.Equal(bounds, dest)
    End Sub

    <Fact>
    Public Sub ComputeViewport_Zoomed_ShowsOnlyTheVisibleStrip()
        ' 2x zoom of an 800x600 image into the same panel: the bounds are double and
        ' shifted 100 px up-left, so the visible strip starts at image pixel (50, 50).
        Dim bounds As New Rectangle(-100, -100, 1600, 1200)
        Dim dest As Rectangle = Rectangle.Empty
        Dim src As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(bounds, 800, 600, New Size(800, 600), dest, src)

        Assert.Equal(New Rectangle(0, 0, 800, 600), dest)   ' the panel is filled
        Assert.Equal(New Rectangle(50, 50, 400, 300), src)
    End Sub

    <Fact>
    Public Sub ComputeViewport_PannedRight_ShowsTheStripThePanelCovers()
        ' Image pushed 100 px to the right: the panel's left 100 px show nothing, and
        ' what remains is the LEFT part of the image, drawn from panel x=100 onward.
        Dim bounds As New Rectangle(100, 0, 1600, 1200)
        Dim dest As Rectangle = Rectangle.Empty
        Dim src As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(bounds, 800, 600, New Size(800, 600), dest, src)

        Assert.Equal(New Rectangle(100, 0, 700, 600), dest)
        Assert.Equal(New Rectangle(0, 0, 350, 300), src)
    End Sub

    <Fact>
    Public Sub ComputeViewport_SourceAndDestinationMapBackOntoEachOther()
        ' The pair's whole contract: the image pixel at fraction f of the source strip
        ' is painted at fraction f of the destination strip. This is what keeps the
        ' pixel under the cursor still and the OCR overlay aligned with the picture.
        Dim bounds As New Rectangle(-333, -97, 1600, 1200)
        Dim dest As Rectangle = Rectangle.Empty
        Dim src As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(bounds, 800, 600, New Size(800, 600), dest, src)

        Assert.False(src.IsEmpty)
        Dim scaleX As Double = 800 / CDbl(bounds.Width)
        Dim scaleY As Double = 600 / CDbl(bounds.Height)

        For Each f As Double In {0.0, 0.37, 0.62, 1.0}
            Dim panelX As Double = dest.X + f * dest.Width
            Dim expectedSrcX As Double = (panelX - bounds.X) * scaleX
            Assert.InRange(src.X + f * src.Width, expectedSrcX - 1.0, expectedSrcX + 1.0)

            Dim panelY As Double = dest.Y + f * dest.Height
            Dim expectedSrcY As Double = (panelY - bounds.Y) * scaleY
            Assert.InRange(src.Y + f * src.Height, expectedSrcY - 1.0, expectedSrcY + 1.0)
        Next
    End Sub

    <Theory>
    <InlineData(0, 600)>
    <InlineData(800, 0)>
    Public Sub ComputeViewport_DegeneratePanel_IsEmpty(panelW As Integer, panelH As Integer)
        Dim dest As Rectangle = Rectangle.Empty
        Dim src As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(New Rectangle(-100, -100, 1600, 1200), 800, 600, New Size(panelW, panelH), dest, src)
        Assert.True(dest.IsEmpty AndAlso src.IsEmpty)
    End Sub

    <Fact>
    Public Sub ComputeViewport_ImageOffThePanel_IsEmpty()
        ' ClampToKeepVisible makes this unreachable in the app, but the pure function
        ' still answers honestly: nothing of the image is over the panel, nothing draws.
        Dim dest As Rectangle = Rectangle.Empty
        Dim src As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(New Rectangle(2000, 2000, 800, 600), 800, 600, New Size(800, 600), dest, src)
        Assert.True(dest.IsEmpty AndAlso src.IsEmpty)
    End Sub

    ' --- ClampPan (У-14) -----------------------------------------------------

    <Fact>
    Public Sub ClampPan_WithinBounds_LeavesPositionUnchanged()
        Dim panel As New Size(800, 600)
        Dim box As New Size(400, 300)
        Dim pt As New Point(100, 150)
        Dim clamped As Point = ZoomMath.ClampPan(pt, box, panel)
        Assert.Equal(pt, clamped)
    End Sub

    <Fact>
    Public Sub ClampPan_DraggedTooFarRightOrDown_LeavesKeepVisiblePx()
        Dim panel As New Size(800, 600)
        Dim box As New Size(1200, 900)
        ' Dragged way past the right / bottom edges: location (2000, 2000)
        Dim clamped As Point = ZoomMath.ClampPan(New Point(2000, 2000), box, panel)
        Assert.Equal(panel.Width - ZoomMath.Keep_Visible_Px, clamped.X)
        Assert.Equal(panel.Height - ZoomMath.Keep_Visible_Px, clamped.Y)
    End Sub

    <Fact>
    Public Sub ClampPan_DraggedTooFarLeftOrUp_LeavesKeepVisiblePx()
        Dim panel As New Size(800, 600)
        Dim box As New Size(1200, 900)
        ' Dragged way past top-left: location (-5000, -5000)
        Dim clamped As Point = ZoomMath.ClampPan(New Point(-5000, -5000), box, panel)
        Assert.Equal(-box.Width + ZoomMath.Keep_Visible_Px, clamped.X)
        Assert.Equal(-box.Height + ZoomMath.Keep_Visible_Px, clamped.Y)
    End Sub

    <Fact>
    Public Sub ClampPan_BoxSmallerThanKeepVisible_KeepsEntireBoxInPanel()
        Dim panel As New Size(800, 600)
        Dim box As New Size(50, 40)
        ' When box is 50x40 (< 100 px), keepVisible shrinks to 50x40
        Dim clampedMax As Point = ZoomMath.ClampPan(New Point(2000, 2000), box, panel)
        Assert.Equal(panel.Width - 50, clampedMax.X)
        Assert.Equal(panel.Height - 40, clampedMax.Y)

        Dim clampedMin As Point = ZoomMath.ClampPan(New Point(-2000, -2000), box, panel)
        Assert.Equal(0, clampedMin.X)
        Assert.Equal(0, clampedMin.Y)
    End Sub

    ' --- Viewport calculation (more shapes) ----------------------------------

    <Fact>
    Public Sub ComputeViewport_CenteredZoomIn_ExtractsCenteredSourceSlice()
        Dim imgW As Integer = 1920
        Dim imgH As Integer = 1080
        Dim panelSize As New Size(800, 600)
        ' 2x zoom centered in panel:
        Dim factor As Double = 2.0
        Dim fraction As New PointF(0.5F, 0.5F)
        Dim anchor As New Point(400, 300)
        Dim bounds As Rectangle = ZoomMath.AnchoredBounds(imgW, imgH, factor, anchor, fraction, panelSize)

        Dim destRect As Rectangle = Rectangle.Empty
        Dim srcRect As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(bounds, imgW, imgH, panelSize, destRect, srcRect)

        Assert.Equal(New Rectangle(0, 0, 800, 600), destRect)
        Assert.Equal(400, srcRect.Width)
        Assert.Equal(300, srcRect.Height)
        Assert.Equal((imgW - 400) \ 2, srcRect.X)
        Assert.Equal((imgH - 300) \ 2, srcRect.Y)
    End Sub

    <Fact>
    Public Sub ComputeViewport_ZoomedOut_DrawsIntoLetterboxedDest()
        Dim imgW As Integer = 1000
        Dim imgH As Integer = 500
        Dim panelSize As New Size(800, 600)
        Dim bounds As New Rectangle(150, 175, 500, 250)

        Dim destRect As Rectangle = Rectangle.Empty
        Dim srcRect As Rectangle = Rectangle.Empty
        ZoomMath.ComputeViewport(bounds, imgW, imgH, panelSize, destRect, srcRect)

        Assert.Equal(bounds, destRect)
        Assert.Equal(New Rectangle(0, 0, imgW, imgH), srcRect)
    End Sub

End Class
#End If
