#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

' Sampling the plate colours out of the image (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, S4).
' Modern-only, exactly like the feature: OcrPlateColors.vb is whole-file
' "#If Not NETFRAMEWORK", so on the net48 leg both it and this file compile to nothing.
Public Class OcrPlateColorsTests

    Private Const Frame As Integer = 120

    Private Shared Function Pack(r As Integer, g As Integer, b As Integer) As Integer
        Return Color.FromArgb(255, r, g, b).ToArgb()
    End Function

    Private Shared Function R(argb As Integer) As Integer
        Return (argb >> 16) And &HFF
    End Function

    ''' <summary>
    ''' A frame filled with <paramref name="paper"/>, a block at (20,20)-(100,80), and inside
    ''' it <paramref name="inkRows"/> rows of ink starting at the top of the block - the
    ''' shape a line of text has, which is what the ink band is aimed at.
    ''' </summary>
    Private Shared Function Scene(paper As Integer, ink As Integer, inkRows As Integer,
                                  Optional inkColumnsOf As Integer = 2) As Integer()
        Dim pixels(Frame * Frame - 1) As Integer
        For i As Integer = 0 To pixels.Length - 1
            pixels(i) = paper
        Next
        For y As Integer = 20 To 20 + inkRows - 1
            For x As Integer = 20 To 99
                If (x Mod inkColumnsOf) = 0 Then pixels(y * Frame + x) = ink
            Next
        Next
        Return pixels
    End Function

    Private Shared ReadOnly Block As New Rectangle(20, 20, 80, 60)

    ' --- the ordinary case -----------------------------------------------------

    <Fact>
    Public Sub BlackTextOnWhite_GivesLightPaperAndDarkInk()
        Dim p = OcrPlateColors.Compute(Scene(Pack(250, 250, 250), Pack(15, 15, 15), 14), Frame, Frame, Block, 14)

        Assert.True(R(p.Background) > 200, "background came out dark: " & R(p.Background).ToString())
        Assert.True(R(p.Ink) < 60, "ink came out light: " & R(p.Ink).ToString())
    End Sub

    <Fact>
    Public Sub InvertingTheInput_InvertsTheResult()
        Dim p = OcrPlateColors.Compute(Scene(Pack(12, 12, 12), Pack(240, 240, 240), 14), Frame, Frame, Block, 14)

        Assert.True(R(p.Background) < 60, "background came out light: " & R(p.Background).ToString())
        Assert.True(R(p.Ink) > 200, "ink came out dark: " & R(p.Ink).ToString())
    End Sub

    <Fact>
    Public Sub SolidFillWithNoText_FallsBackByTheInkFractionRule()
        ' Nothing under the block differs from the background, so there is nothing to sample.
        ' The pair must still be readable, which is what the fallback is for.
        Dim p = OcrPlateColors.Compute(Scene(Pack(200, 120, 60), Pack(200, 120, 60), 0), Frame, Frame, Block, 14)

        Assert.Equal(Pack(200, 120, 60), p.Background)
        Assert.True(R(p.Ink) < 60, "a light background must fall back to near-black ink")
    End Sub

    <Fact>
    Public Sub LowContrastPair_IsReplacedByTheFallback()
        ' Grey on slightly different grey is technically sampled and practically unreadable.
        Dim p = OcrPlateColors.Compute(Scene(Pack(130, 130, 130), Pack(96, 96, 96), 14), Frame, Frame, Block, 14)

        Assert.True(Math.Abs(R(p.Background) - R(p.Ink)) >= 55,
                    "the pair stayed under the contrast floor: " & R(p.Background).ToString() & " vs " & R(p.Ink).ToString())
    End Sub

    ' --- ink is a median, not a mean -------------------------------------------

    <Fact>
    Public Sub Ink_IsAMedian_NotAMean()
        ' The trap the other project fell into and only found by eye. "Differs from the
        ' background" admits nearly the whole anti-aliased edge of a glyph, so a MEAN lands
        ' between ink and paper by construction. This scene is deliberately mostly ramp:
        ' a few true-ink pixels and many edge pixels. The median has to stay near the ink.
        Dim paper As Integer = Pack(253, 253, 253)
        Dim trueInk As Integer = Pack(17, 17, 17)
        Dim pixels(Frame * Frame - 1) As Integer
        For i As Integer = 0 To pixels.Length - 1
            pixels(i) = paper
        Next
        For y As Integer = 20 To 33
            For x As Integer = 20 To 99
                Select Case x Mod 4
                    Case 0 : pixels(y * Frame + x) = trueInk
                    Case 1 : pixels(y * Frame + x) = Pack(40, 40, 40)     ' inner edge
                    Case 2 : pixels(y * Frame + x) = Pack(90, 90, 90)     ' outer edge
                End Select
            Next
        Next

        Dim p = OcrPlateColors.Compute(pixels, Frame, Frame, Block, 14)

        ' A mean over the same samples would sit around 50-60; the median stays with the ink.
        Assert.True(R(p.Ink) <= 45, "the ink drifted towards the paper: " & R(p.Ink).ToString())
    End Sub

    ' --- the ring test ---------------------------------------------------------

    <Fact>
    Public Sub Ring_SwapsThePairOnDisplayLettering()
        ' Heavy lettering drawn inside a tight box covers more of that box with strokes than
        ' with paper, so a median taken INSIDE returns an exact inversion. What is outside the
        ' box is what tells the two apart: here the surroundings are the real paper colour of
        ' the letters, so the sampled pair has to be swapped back.
        Dim panel As Integer = Pack(20, 20, 20)      ' dark panel around the lettering
        Dim glyph As Integer = Pack(245, 245, 245)   ' light strokes, covering most of the box
        Dim pixels(Frame * Frame - 1) As Integer
        For i As Integer = 0 To pixels.Length - 1
            pixels(i) = panel
        Next
        For y As Integer = 20 To 79
            For x As Integer = 20 To 99
                If (x Mod 4) <> 0 Then pixels(y * Frame + x) = glyph   ' 3 of every 4 columns
            Next
        Next

        Dim p = OcrPlateColors.Compute(pixels, Frame, Frame, Block, 20)

        ' Sampled inside, the majority (the strokes) would have become the "background".
        ' The ring is the dark panel, so the pair comes back the right way round.
        Assert.True(R(p.Background) < 60, "the plate would have been painted in the glyph colour")
        Assert.True(R(p.Ink) > 200, "the text would have been painted in the panel colour")
    End Sub

    <Fact>
    Public Sub Ring_DoesNotSwapAnOrdinaryLineOfText()
        ' The price of the rule above: an ordinary line surrounded by its own paper must be
        ' left exactly as sampled.
        Dim p = OcrPlateColors.Compute(Scene(Pack(250, 250, 250), Pack(15, 15, 15), 14), Frame, Frame, Block, 14)

        Assert.True(R(p.Background) > 200)
        Assert.True(R(p.Ink) < 60)
    End Sub

    <Fact>
    Public Sub Ring_WithTooFewVotes_LeavesThePairAlone()
        ' A block filling the whole image has almost no outside left. Below RingMinVotes the
        ' test must decline to decide rather than decide on a handful of pixels.
        Dim panel As Integer = Pack(20, 20, 20)
        Dim glyph As Integer = Pack(245, 245, 245)
        Dim pixels(Frame * Frame - 1) As Integer
        For i As Integer = 0 To pixels.Length - 1
            pixels(i) = panel
        Next
        For y As Integer = 0 To Frame - 1
            For x As Integer = 0 To Frame - 1
                If (x Mod 4) <> 0 Then pixels(y * Frame + x) = glyph
            Next
        Next

        Dim whole As New Rectangle(0, 0, Frame, Frame)
        Dim p = OcrPlateColors.Compute(pixels, Frame, Frame, whole, 20)

        ' No ring, no swap: the glyph colour stays the majority and therefore the background.
        Assert.True(R(p.Background) > 200, "the pair was swapped without enough votes")
    End Sub

    ' --- the ring band comes from the LINE height, not the ink band -------------

    <Fact>
    Public Sub RingBand_IsDerivedFromTheLineHeight_NotTheInkBand()
        ' The defect that slipped past the other project's parity test: it pinned the vote
        ' threshold and the call shape, but the MEANING of an argument changed - one of their
        ' implementations passed the ink band (1.3 line heights) where the ring band (a third
        ' of a line height) belonged, and the ring came out 0.43 of a line instead of 0.33.
        '
        ' Measured here by consequence: a ring of a third of a 30 px line is 10 px, so an
        ' inverting collar 10 px thick reaches the whole ring and swaps the pair. Had the band
        ' been taken from the ink band instead (1.3 * 30 = 39 px), that same collar would have
        ' been a minority inside a much wider ring and the vote would have gone the other way.
        Dim paper As Integer = Pack(250, 250, 250)
        Dim ink As Integer = Pack(10, 10, 10)
        Dim pixels(Frame * Frame - 1) As Integer
        For i As Integer = 0 To pixels.Length - 1
            pixels(i) = paper
        Next
        ' The block itself: light, with a little ink so the pair is sampled rather than
        ' falling back.
        For y As Integer = 30 To 59
            For x As Integer = 30 To 89
                If (x Mod 6) = 0 Then pixels(y * Frame + x) = ink
            Next
        Next
        ' A dark collar exactly 10 px thick around the block - the width of a third of a line.
        For y As Integer = 20 To 69
            For x As Integer = 20 To 99
                Dim insideBlock As Boolean = (y >= 30 AndAlso y <= 59 AndAlso x >= 30 AndAlso x <= 89)
                If Not insideBlock Then pixels(y * Frame + x) = ink
            Next
        Next

        Dim block As New Rectangle(30, 30, 60, 30)
        Dim p = OcrPlateColors.Compute(pixels, Frame, Frame, block, 30)

        Assert.True(R(p.Background) < 60,
                    "the ring did not see the collar - its width is not a third of the line height")
    End Sub

    ' --- degenerate inputs -----------------------------------------------------

    <Fact>
    Public Sub DegenerateInputs_ReturnNotComputed()
        Dim pixels = Scene(Pack(250, 250, 250), Pack(15, 15, 15), 14)

        Assert.Equal(0, OcrPlateColors.Compute(Nothing, Frame, Frame, Block, 14).Background)
        Assert.Equal(0, OcrPlateColors.Compute(pixels, 0, 0, Block, 14).Background)
        Assert.Equal(0, OcrPlateColors.Compute(pixels, Frame, Frame, New Rectangle(500, 500, 40, 40), 14).Background)
        Assert.Equal(0, OcrPlateColors.Compute(pixels, Frame, Frame, Rectangle.Empty, 14).Background)
    End Sub

    <Fact>
    Public Sub ComputedColours_AreNeverZero()
        ' 0 is the "not computed" sentinel, so even a genuinely black plate has to come back
        ' non-zero - otherwise the renderer would silently fall back to the old constant.
        Dim p = OcrPlateColors.Compute(Scene(Pack(0, 0, 0), Pack(255, 255, 255), 14), Frame, Frame, Block, 14)

        Assert.NotEqual(0, p.Background)
        Assert.NotEqual(0, p.Ink)
    End Sub

End Class
#End If
