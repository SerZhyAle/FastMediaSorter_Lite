Option Strict On

Imports System.Drawing
Imports Xunit

' The pure half of overlay text fitting (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, stage S2).
' Both legs: losing the tail of a translation is a DEFECT, not a cosmetic difference.
Public Class OcrOverlayFitTests

    Private Const MinFont As Double = 8.0
    Private Const MaxFont As Double = 200.0

    ' --- the candidate ladder --------------------------------------------------

    <Fact>
    Public Sub Candidates_StartJustBelowTheOriginalTextSize()
        Dim c = OcrOverlayFit.FontCandidates(50.0, MinFont, MaxFont)
        Assert.Equal(50.0 * OcrOverlayFit.StartFactor, c(0), 6)
    End Sub

    <Fact>
    Public Sub Candidates_StepDownByEightPercentAndNeverRise()
        Dim c = OcrOverlayFit.FontCandidates(60.0, MinFont, MaxFont)

        Assert.True(c.Count > 1)
        For i As Integer = 1 To c.Count - 1
            Assert.True(c(i) < c(i - 1), "candidate " & i.ToString() & " did not shrink")
        Next
        ' Every step except the last (which is clamped to the floor) is exactly the factor.
        For i As Integer = 1 To c.Count - 2
            Assert.Equal(c(i - 1) * OcrOverlayFit.StepFactor, c(i), 6)
        Next
    End Sub

    <Fact>
    Public Sub Candidates_StopAtHalfTheStart_NeverBelowTheHardFloor()
        Dim c = OcrOverlayFit.FontCandidates(60.0, MinFont, MaxFont)
        Dim start As Double = c(0)

        Assert.True(c(c.Count - 1) >= start * OcrOverlayFit.FloorFactor - 0.000001,
                    "the ladder went below half its start")
        Assert.All(c, Sub(v) Assert.True(v >= MinFont, "a candidate fell under the hard floor"))
    End Sub

    <Fact>
    Public Sub Candidates_AreAboutNine_AndNeverMoreThanTheGuard()
        ' 0.92^8 ~ 0.51, so start -> floor is about nine rungs. The old fitter walked down
        ' from 200 px in 1 px steps INSIDE every Paint - up to ~190 MeasureString calls per
        ' block per frame, paid again on every zoom and resize step.
        Dim c = OcrOverlayFit.FontCandidates(60.0, MinFont, MaxFont)
        Assert.InRange(c.Count, 8, 11)

        ' The guard is a guard, not a working number: it holds even when the floor cannot
        ' be reached by stepping (a start already at the hard floor stops at once).
        For Each h As Double In New Double() {1.0, 12.0, 400.0, 5000.0}
            Assert.True(OcrOverlayFit.FontCandidates(h, MinFont, MaxFont).Count <= OcrOverlayFit.MaxCandidates)
        Next
    End Sub

    <Fact>
    Public Sub Candidates_RespectTheCeiling()
        ' A block whose source text is taller than the ceiling still starts at the ceiling.
        Dim c = OcrOverlayFit.FontCandidates(4000.0, MinFont, MaxFont)
        Assert.Equal(MaxFont, c(0), 6)
    End Sub

    <Theory>
    <InlineData(0.0)>
    <InlineData(-30.0)>
    Public Sub Candidates_DegenerateLineHeight_StillYieldsALadder(lineHeight As Double)
        ' A block that never reported a line height must not produce an empty ladder - the
        ' renderer would have nothing to draw with.
        Dim c = OcrOverlayFit.FontCandidates(lineHeight, MinFont, MaxFont)
        Assert.NotEmpty(c)
        Assert.All(c, Sub(v) Assert.InRange(v, MinFont, MaxFont))
    End Sub

    <Fact>
    Public Sub Candidates_InvertedBounds_DoNotThrow()
        Dim c = OcrOverlayFit.FontCandidates(40.0, 120.0, 20.0)
        Assert.NotEmpty(c)
        Assert.All(c, Sub(v) Assert.True(v > 0))
    End Sub

    ' --- the growth budget -----------------------------------------------------

    <Fact>
    Public Sub Growth_WithNoNeighbour_ReachesTheBottomOfTheImage()
        Dim plate As New Rectangle(100, 100, 200, 50)
        Assert.Equal(450, OcrOverlayFit.GrowthBudget(plate, New List(Of Rectangle)(), 600))
    End Sub

    <Fact>
    Public Sub Growth_StopsAtTheTopOfThePlateBelowInTheSameColumn()
        Dim plate As New Rectangle(100, 100, 200, 50)
        Dim below As New Rectangle(110, 300, 200, 50)

        ' Bounded by the neighbour (300 - 150), not by the image bottom (600 - 150).
        Assert.Equal(150, OcrOverlayFit.GrowthBudget(plate, New List(Of Rectangle) From {plate, below}, 600))
    End Sub

    <Fact>
    Public Sub Growth_IgnoresAPlateInAnotherColumn()
        Dim plate As New Rectangle(100, 100, 200, 50)
        Dim other As New Rectangle(600, 300, 200, 50)   ' no horizontal overlap at all

        Assert.Equal(450, OcrOverlayFit.GrowthBudget(plate, New List(Of Rectangle) From {plate, other}, 600))
    End Sub

    <Fact>
    Public Sub Growth_IgnoresAPlateAboveIt()
        ' Only downwards: the top edge never moves, so a plate above can never be in the way.
        Dim plate As New Rectangle(100, 300, 200, 50)
        Dim above As New Rectangle(100, 100, 200, 50)

        Assert.Equal(250, OcrOverlayFit.GrowthBudget(plate, New List(Of Rectangle) From {plate, above}, 600))
    End Sub

    <Fact>
    Public Sub Growth_TakesTheNEARESTNeighbourBelow()
        Dim plate As New Rectangle(100, 100, 200, 50)
        Dim far As New Rectangle(100, 500, 200, 50)
        Dim near As New Rectangle(100, 220, 200, 50)

        Assert.Equal(70, OcrOverlayFit.GrowthBudget(plate, New List(Of Rectangle) From {plate, far, near}, 600))
    End Sub

    <Fact>
    Public Sub Growth_IsNeverNegative()
        ' A plate already at or past the bottom bound cannot grow - and must not report a
        ' negative budget, which would shrink it and move geometry the ladder promised not to.
        Dim plate As New Rectangle(100, 500, 200, 150)
        Assert.Equal(0, OcrOverlayFit.GrowthBudget(plate, New List(Of Rectangle)(), 600))

        Dim touching As New Rectangle(100, 100, 200, 50)
        Assert.Equal(0, OcrOverlayFit.GrowthBudget(touching, New List(Of Rectangle) From {New Rectangle(100, 150, 200, 40)}, 600))
    End Sub

    <Fact>
    Public Sub Growth_IgnoresEmptyPlaceholders()
        ' Blocks that will not be painted are kept in the array as Empty so the indices stay
        ' aligned with the document - they must not bound anybody's growth.
        Dim plate As New Rectangle(100, 100, 200, 50)
        Dim placeholders As New List(Of Rectangle) From {Rectangle.Empty, plate, Rectangle.Empty}

        Assert.Equal(450, OcrOverlayFit.GrowthBudget(plate, placeholders, 600))
    End Sub

    <Fact>
    Public Sub Growth_DegenerateInputs_DoNotThrow()
        Assert.Equal(0, OcrOverlayFit.GrowthBudget(Rectangle.Empty, Nothing, 600))
        Assert.Equal(450, OcrOverlayFit.GrowthBudget(New Rectangle(100, 100, 200, 50), Nothing, 600))
    End Sub

End Class
