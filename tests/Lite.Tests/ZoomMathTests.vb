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

End Class
#End If
