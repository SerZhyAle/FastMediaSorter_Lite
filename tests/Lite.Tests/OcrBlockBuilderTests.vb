Option Strict On

Imports System.Drawing
Imports Xunit

' Line clustering for the OCR + translation overlay
' (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, stage S1). Both legs: a broken plate is a
' DEFECT, not a cosmetic difference, so the fix ships in both viewers.
'
' Every fixture below is boxes, never an image. That is not a shortcut - OcrBlockBuilder
' takes boxes, and the two scenes that produced the constants cannot be shipped as pixels
' anyway (one is an application owner's private material, the other a crop whose rectangle
' was never recorded). The numbers ARE the way those scenes were handed over.
Public Class OcrBlockBuilderTests

    ' --- fixtures --------------------------------------------------------------

    ''' <summary>Line from an x0,y0,x1,y1 box - the shape both measured scenes were
    ''' handed over in.</summary>
    Private Shared Function L(text As String, x0 As Integer, y0 As Integer, x1 As Integer, y1 As Integer) As OcrLine
        Return New OcrLine With {
            .Text = text,
            .Box = Rectangle.FromLTRB(x0, y0, x1, y1)
        }
    End Function

    Private Shared Function Lines(ParamArray items As OcrLine()) As List(Of OcrLine)
        Return New List(Of OcrLine)(items)
    End Function

    ''' <summary>A column of identical lines: same width, same height, constant pitch.</summary>
    Private Shared Function Column(count As Integer, top As Integer, pitch As Integer, height As Integer,
                                   Optional left As Integer = 100, Optional width As Integer = 400) As List(Of OcrLine)
        Dim result As New List(Of OcrLine)
        For i As Integer = 0 To count - 1
            ' Real words, not "line 1": the translatability filter counts LETTERS, so a
            ' fixture whose line is four letters plus a digit would be testing the filter
            ' rather than the clustering.
            result.Add(New OcrLine With {
                .Text = "sample text " & (i + 1).ToString(),
                .Box = New Rectangle(left, top + i * pitch, width, height)
            })
        Next
        Return result
    End Function

    Private Shared Function Coverage(b As OcrBlock, frame As Size) As Double
        Return (b.Box.Width * CDbl(b.Box.Height)) / (frame.Width * CDbl(frame.Height))
    End Function

    ' --- degenerate input ------------------------------------------------------

    <Fact>
    Public Sub EmptyInput_YieldsNoBlocks()
        Assert.Empty(OcrBlockBuilder.BuildBlocks(New List(Of OcrLine)(), New Size(800, 600)))
        Assert.Empty(OcrBlockBuilder.BuildBlocks(Nothing, New Size(800, 600)))
    End Sub

    <Fact>
    Public Sub SingleLine_YieldsOneBlock()
        Dim blocks = OcrBlockBuilder.BuildBlocks(Lines(L("Hello there", 10, 10, 300, 40)), New Size(800, 600))
        Assert.Single(blocks)
        Assert.Equal("Hello there", blocks(0).SourceText)
    End Sub

    ' --- rule 2: pitch ---------------------------------------------------------

    <Fact>
    Public Sub SmallCapsColumn_CollapsesIntoOneBlock()
        ' The defect this stage exists for. Small caps have no descenders, so each ink box
        ' is much shorter than its line: with a 60 px pitch the ink is 26 px tall, leaving a
        ' 34 px gap where the OLD rule allowed max(6, 0.9 * 26) = 23. One balloon used to
        ' come out as several plates. Measured top-to-top, the pitch is uniform.
        Dim blocks = OcrBlockBuilder.BuildBlocks(Column(5, 100, 60, 26), New Size(800, 600))

        Assert.Single(blocks)
        Assert.Equal(5, blocks(0).Lines.Count)
    End Sub

    <Fact>
    Public Sub SeparateBalloons_StayTwoBlocks()
        ' Two speech balloons more than MaxLeadingRatio median heights apart. The far pair
        ' does not contribute to the median pitch, so it cannot widen the threshold that
        ' would then merge them.
        Dim all As New List(Of OcrLine)(Column(3, 100, 60, 40))
        all.AddRange(Column(3, 900, 60, 40))

        Dim blocks = OcrBlockBuilder.BuildBlocks(all, New Size(800, 1400))

        Assert.Equal(2, blocks.Count)
        Assert.All(blocks, Sub(b) Assert.Equal(3, b.Lines.Count))
    End Sub

    <Fact>
    Public Sub TwoLinesInAColumn_UseTheGapFallback_NotThePitch()
        ' One pair is one pitch, and its median IS that pair - so a single misread line
        ' would set the threshold for the whole image. Below MinPitchPairs the builder
        ' falls back to the historical ink-gap rule instead: gap 14 <= max(6, 0.9*40) = 36.
        Dim blocks = OcrBlockBuilder.BuildBlocks(Column(2, 100, 54, 40), New Size(800, 600))
        Assert.Single(blocks)

        ' Same two lines, gap now well past the fallback threshold: two blocks. Had the
        ' single pair been trusted as a median, ANY spacing would have merged them.
        Dim far = OcrBlockBuilder.BuildBlocks(Column(2, 100, 200, 40), New Size(800, 600))
        Assert.Equal(2, far.Count)
    End Sub

    ' --- rule 1: column --------------------------------------------------------

    <Fact>
    Public Sub LinesInDifferentColumns_NeverShareABlock()
        ' Two columns, interleaved vertically - the layout the ported algorithm is known to
        ' handle poorly (the other project's `synth-two-columns` is their one open grouping
        ' defect, held by an acceptance threshold rather than fixed). What must hold for us
        ' regardless of how many blocks come out: no block ever mixes the two columns, so a
        ' plate is never painted across the gutter.
        Dim all As New List(Of OcrLine)(Column(4, 100, 60, 40, left:=50, width:=300))
        all.AddRange(Column(4, 130, 60, 40, left:=500, width:=300))

        Dim blocks = OcrBlockBuilder.BuildBlocks(all, New Size(900, 600))

        For Each b As OcrBlock In blocks
            Dim leftLines As Integer = b.Lines.Where(Function(l) l.Box.Left < 400).Count()
            Assert.True(leftLines = 0 OrElse leftLines = b.Lines.Count,
                        "a block mixed lines from both columns: " & b.Box.ToString())
        Next
    End Sub

    ' --- rule 3: type size -----------------------------------------------------

    ''' <summary>
    ''' The poster scene of section 13.7, as the other project's engine really returned it
    ''' (grey copy, sparse pass, `rus`). Two lines fell below their confidence threshold and
    ''' are NOT here - that is the point: the median pitch is computed from what survived.
    ''' The order is the engine's own, out of reading order, which the builder has to
    ''' tolerate.
    ''' </summary>
    Private Shared Function PosterLines() As List(Of OcrLine)
        Return Lines(
            L("ТРАХАТЬСЯ:", 74, 415, 1328, 696),
            L("МЫ ЖЕ", 79, 750, 521, 905),
            L("ЛЮДИ,", 76, 1131, 456, 1302),
            L("МОЖЕМ", 79, 1319, 538, 1472),
            L("ПРОСТО", 80, 1509, 477, 1658),
            L("ПОГОВОРИТЬ", 80, 1872, 644, 2009))
    End Function

    <Fact>
    Public Sub HeadingOverText_SplitsByTypeSize()
        ' No pitch threshold cuts this page in the right place, and that is the finding
        ' behind the rule: heading -> text is 335 px while text -> text ACROSS a dropped
        ' line is 381 px. Cutting by pitch would tear the paragraph before it separated the
        ' heading. The type size does separate them: 281 px of ink against a median of 155.
        Dim blocks = OcrBlockBuilder.BuildBlocks(PosterLines(), New Size(1400, 2100))

        Assert.Equal(2, blocks.Count)
        Assert.Equal(Rectangle.FromLTRB(74, 415, 1328, 696), blocks(0).Box)
        Assert.Equal(Rectangle.FromLTRB(76, 750, 644, 2009), blocks(1).Box)
        Assert.Equal("МЫ ЖЕ ЛЮДИ, МОЖЕМ ПРОСТО ПОГОВОРИТЬ", blocks(1).SourceText)
    End Sub

    <Fact>
    Public Sub LongInscription_WithUnevenLines_StaysOneBlock()
        ' The price of the rule above, and the reason the two tests are written as a pair:
        ' without this one TypeSizeRatio could be quietly tightened until real inscriptions
        ' tore apart. 19 lines of ONE hand-annotated inscription, ink boxes 23-34 px - the
        ' widest spread measured inside a single text (1.42x its own median).
        Dim heights() As Integer = {28, 31, 23, 30, 34, 27, 29, 33, 24, 30, 28, 26, 32, 29, 25, 31, 27, 34, 28}
        Dim lines As New List(Of OcrLine)
        For i As Integer = 0 To heights.Length - 1
            lines.Add(New OcrLine With {
                .Text = "word " & (i + 1).ToString(),
                .Box = New Rectangle(120, 200 + i * 44, 360, heights(i))
            })
        Next

        Dim blocks = OcrBlockBuilder.BuildBlocks(lines, New Size(1200, 1400))

        Assert.Single(blocks)
        Assert.Equal(19, blocks(0).Lines.Count)
    End Sub

    <Fact>
    Public Sub DroppedLine_SplitsTheColumn_RecordedBehaviour()
        ' Fixates behaviour rather than asserting a wish. A uniform column with one line
        ' removed - as the confidence threshold would remove it - makes the median pitch an
        ' honest median of an incomplete set, and the doubled step falls outside it. The
        ' block splits at the hole.
        '
        ' That is the safe direction: the other project's version of this failure merged a
        ' heading into the text below and painted one plate over the picture between them.
        ' Here the geometry stays correct and only the sentence context is lost. If a red
        ' case ever shows up on our own material, the fix is a rule of its own - not a wider
        ' PitchFactor, which is tuned on comic balloons.
        Dim lines As New List(Of OcrLine)(Column(5, 100, 100, 40))
        lines.RemoveAt(2)

        Dim blocks = OcrBlockBuilder.BuildBlocks(lines, New Size(800, 800))

        Assert.Equal(2, blocks.Count)
        Assert.Equal(2, blocks(0).Lines.Count)
        Assert.Equal(2, blocks(1).Lines.Count)
    End Sub

    <Fact>
    Public Sub LargeParagraphNextToSmallText_RecordedBehaviour()
        ' The case an earlier revision of the spec worried about (a large paragraph coming
        ' apart because its own pitch exceeds the page median) with the correction it
        ' proposed withdrawn - measurement showed it would have made the opposite failure
        ' worse. This test records what actually happens, and a red result here is the
        ' signal that a rule of its own is needed.
        Dim all As New List(Of OcrLine)(Column(6, 100, 40, 26, left:=60, width:=300))   ' small body text
        all.AddRange(Column(3, 700, 120, 90, left:=60, width:=300))                     ' large paragraph

        Dim blocks = OcrBlockBuilder.BuildBlocks(all, New Size(800, 1200))

        ' RECORDED, AND IT IS A LIMITATION, not the outcome we would have picked. The page
        ' median pitch is 40 px (six body pairs); the large paragraph's own 120 px pitch is
        ' excluded from that median by MaxLeadingRatio (120 > 3 * 26), so the paragraph is
        ' then measured against a threshold of 48 px and comes apart line by line.
        '
        ' Left as is on purpose. The correction an earlier revision proposed -
        ' max(median pitch, previous line height * 1.2) - was WITHDRAWN on measurement: it
        ' widens the threshold, and the failure that actually occurs in the field is
        ' over-merging (a heading glued to the text below, one plate over the picture
        ' between them), which a wider threshold makes worse. Whatever fixes this case has
        ' to be its own rule, decided on our own material.
        '
        ' The consolation is the direction: the body text stays one block, and the large
        ' paragraph loses sentence context but keeps correct geometry - each plate still
        ' sits on its own line.
        Assert.Equal(4, blocks.Count)
        Assert.Equal(6, blocks(0).Lines.Count)
        Assert.All(blocks.Skip(1), Sub(b) Assert.Single(b.Lines))
    End Sub

    ' --- rule 4: dissolve ------------------------------------------------------

    ''' <summary>
    ''' The application-window screenshot of section 14.1, in a 640x563 frame, as their
    ''' recognizer boxed it. Line texts are placeholders of the same shape on purpose: the
    ''' real ones are a private account list, and the rule is measured on boxes - the text
    ''' takes no part in it.
    ''' </summary>
    Private Shared Function AccountsWindowLines() As List(Of OcrLine)
        Return Lines(
            L("Accounts", 15, 17, 290, 38),
            L("Signed in on this device", 15, 51, 339, 66),
            L("First account entry", 13, 99, 527, 151),
            L("Second account entry", 22, 182, 467, 231),
            L("Third account entry", 15, 262, 479, 310),
            L("Fourth account entry", 13, 341, 479, 393),
            L("Fifth account entry", 13, 423, 555, 474),
            L("Sixth account entry", 15, 505, 479, 553))
    End Function

    <Fact>
    Public Sub ApplicationWindow_DissolvesIntoOnePlatePerRow()
        Dim frame As New Size(640, 563)

        Dim blocks = OcrBlockBuilder.BuildBlocks(AccountsWindowLines(), frame)

        ' One column, one size, one pitch: rules 1-3 see no seam in the list at all. Before
        ' the dissolve rule those six rows were a single plate over 68.3 % of the frame,
        ' with the image behind it invisible.
        Assert.True(blocks.Count >= 6, "expected at least six plates, got " & blocks.Count.ToString())
        Assert.All(blocks, Sub(b) Assert.True(Coverage(b, frame) <= OcrBlockBuilder.MaxPlateCoverage,
                                              "a plate still covers " & Coverage(b, frame).ToString("F4") & " of the frame"))
    End Sub

    <Fact>
    Public Sub Dissolving_LosesNoText()
        ' The half that matters. "More plates" is easy to see and easy to fake by dropping
        ' rows; this asserts every recognized line still reaches a plate.
        Dim frame As New Size(640, 563)
        Dim source = AccountsWindowLines()

        Dim blocks = OcrBlockBuilder.BuildBlocks(source, frame)

        Dim reached As New List(Of String)
        For Each b As OcrBlock In blocks
            reached.AddRange(b.Lines.Select(Function(l) l.Text))
        Next
        Assert.Equal(source.Count, reached.Count)
        For Each l As OcrLine In source
            Assert.Contains(l.Text, reached)
        Next
    End Sub

    <Fact>
    Public Sub DenseInscription_OverTheAreaThreshold_StaysOnePlate()
        ' First half of the price of rule 4. A hand-annotated inscription covering 0.61 of
        ' its frame passes the coverage test - only the second fraction saves it: its own
        ' lines fill 0.79 of its height, well over MinPlateLineFill.
        Dim lines As New List(Of OcrLine)
        For i As Integer = 0 To 9
            lines.Add(New OcrLine With {
                .Text = "inscription line " & (i + 1).ToString(),
                .Box = New Rectangle(60, 40 + i * 76, 680, 62)
            })
        Next
        Dim frame As New Size(800, 900)

        Dim blocks = OcrBlockBuilder.BuildBlocks(lines, frame)

        Assert.Single(blocks)
        Assert.True(Coverage(blocks(0), frame) > OcrBlockBuilder.MaxPlateCoverage,
                    "fixture no longer exercises the rule: coverage is only " & Coverage(blocks(0), frame).ToString("F4"))
    End Sub

    <Fact>
    Public Sub OrdinaryParagraph_OnALargePage_StaysOnePlate()
        ' Second half. Three lines of prose whose lines fill only 0.67 of the block height -
        ' that is paragraph leading, not separated regions - and looseness alone would have
        ' dissolved them. The coverage test is what holds them together.
        Dim lines As New List(Of OcrLine)
        For i As Integer = 0 To 2
            lines.Add(New OcrLine With {
                .Text = "paragraph line " & (i + 1).ToString(),
                .Box = New Rectangle(40, 40 + i * 60, 300, 40)
            })
        Next
        Dim frame As New Size(1600, 1200)

        Dim blocks = OcrBlockBuilder.BuildBlocks(lines, frame)

        Assert.Single(blocks)
        Assert.True(Coverage(blocks(0), frame) <= OcrBlockBuilder.MaxPlateCoverage)
    End Sub

    <Fact>
    Public Sub EmptyImageSize_DisablesTheDissolveRuleOnly()
        ' The dissolve rule is the one rule stated as a fraction of the page, so without a
        ' page it does not run - and nothing else changes.
        Dim blocks = OcrBlockBuilder.BuildBlocks(AccountsWindowLines(), Size.Empty)

        Assert.Equal(2, blocks.Count)
        Assert.Equal(6, blocks(1).Lines.Count)
    End Sub

End Class
