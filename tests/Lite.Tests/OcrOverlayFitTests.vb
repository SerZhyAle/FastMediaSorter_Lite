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

    ' --- the last rung: release, never an ellipsis (OCR-OVERLAY rule 9) --------

    <Fact>
    Public Sub Release_GivesTheTextTheHeightItNeeds_PastTheBudget()
        ' The rung that replaced StringTrimming.EllipsisWord. The growth budget stopped this
        ' plate at the neighbour below; releasing it ignores that bound and takes what the text
        ' measured at the font floor actually needs.
        Dim plate As New Rectangle(100, 100, 200, 50)
        Assert.Equal(260, OcrOverlayFit.ReleasedHeight(plate, 260.0, 900))
    End Sub

    <Fact>
    Public Sub Release_StopsAtTheBottomOfTheDisplayedImage()
        ' The one bound left. Past the image edge a plate is drawn on the window background,
        ' where nothing anchors it to the text it replaces.
        Dim plate As New Rectangle(100, 100, 200, 50)
        Assert.Equal(500, OcrOverlayFit.ReleasedHeight(plate, 4000.0, 600))
    End Sub

    <Fact>
    Public Sub Release_NeverShrinksThePlate()
        ' Every other rung promises the mapped box is a floor; this one may not break it -
        ' including for a plate that already extends past the bottom of the image, where the
        ' available height is negative.
        Dim plate As New Rectangle(100, 100, 200, 120)
        Assert.Equal(120, OcrOverlayFit.ReleasedHeight(plate, 10.0, 900))

        Dim offImage As New Rectangle(100, 700, 200, 150)
        Assert.Equal(150, OcrOverlayFit.ReleasedHeight(offImage, 400.0, 600))
    End Sub

    <Fact>
    Public Sub Release_DoesNotMoveTheTopEdge()
        ' Stated as a test because it is the half of rule 9 that is easy to lose: the anchor
        ' point of the source block does not move while the box grows. The function only ever
        ' returns a HEIGHT, so the only way to break this is to start returning something else.
        Dim plate As New Rectangle(100, 250, 200, 40)
        Dim h As Integer = OcrOverlayFit.ReleasedHeight(plate, 300.0, 900)
        Assert.Equal(300, h)
        Assert.Equal(250, New Rectangle(plate.X, plate.Y, plate.Width, h).Top)
    End Sub

    ' --- type size comes from WORD heights (OCR-OVERLAY rule 5) ---------------

    Private Shared Function Word(h As Integer) As OcrWord
        Return New OcrWord With {.Text = "w", .Box = New Rectangle(0, 0, 20, h), .Confidence = 0.9F}
    End Function

    Private Shared Function LineOf(boxHeight As Integer, ParamArray wordHeights As Integer()) As OcrLine
        Dim l As New OcrLine With {.Text = "line", .Box = New Rectangle(0, 0, 200, boxHeight)}
        For Each h As Integer In wordHeights
            l.Words.Add(Word(h))
        Next
        Return l
    End Function

    <Fact>
    Public Sub TypeSize_IsTheMedianOfWordHeights_NotOfLineBoxes()
        ' The defect the rule names: a line box is the union of its words, so one artifact
        ' inside it - a speck of the drawing read as a glyph with a tall box - inflates the
        ' whole line and through it the type size of every plate in the block.
        Dim b As New OcrBlock With {.Box = New Rectangle(0, 0, 200, 120)}
        b.Lines.Add(LineOf(120, 30, 30, 30))   ' box inflated to 120 by one artifact
        b.Lines.Add(LineOf(32, 30, 30, 32))

        Assert.Equal(30, OcrOverlayFit.MedianLineHeight(b))
    End Sub

    <Fact>
    Public Sub TypeSize_FallsBackToLineBoxes_WhenTheLinesCarryNoWords()
        ' A document built before the engine gave lines their real words, or a pass that
        ' reported none. The fallback is the same median over the only boxes there are - not a
        ' second algorithm, and never zero.
        Dim b As New OcrBlock With {.Box = New Rectangle(0, 0, 200, 90)}
        b.Lines.Add(LineOf(40))
        b.Lines.Add(LineOf(44))
        b.Lines.Add(LineOf(48))

        Assert.Equal(44, OcrOverlayFit.MedianLineHeight(b))
    End Sub

    <Fact>
    Public Sub TypeSize_IgnoresZeroSizedWordBoxes()
        Dim b As New OcrBlock With {.Box = New Rectangle(0, 0, 200, 60)}
        b.Lines.Add(LineOf(30, 0, 30, 0, 30))

        Assert.Equal(30, OcrOverlayFit.MedianLineHeight(b))
    End Sub

    <Fact>
    Public Sub TypeSize_DegenerateBlocks_FallBackToTheBlockBox()
        Assert.Equal(0, OcrOverlayFit.MedianLineHeight(Nothing))

        Dim noLines As New OcrBlock With {.Box = New Rectangle(0, 0, 200, 77)}
        Assert.Equal(77, OcrOverlayFit.MedianLineHeight(noLines))

        Dim noBoxes As New OcrBlock With {.Box = New Rectangle(0, 0, 200, 55)}
        noBoxes.Lines.Add(LineOf(0))
        Assert.Equal(55, OcrOverlayFit.MedianLineHeight(noBoxes))
    End Sub

End Class
