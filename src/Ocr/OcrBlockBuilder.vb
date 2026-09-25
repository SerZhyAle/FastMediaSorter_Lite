Option Strict On

Imports System.Drawing

''' <summary>
''' Groups OCR text lines into translation blocks. Comics/manga rarely produce
''' clean engine-level blocks, so we start from lines (the reliable unit) and
''' merge vertically-adjacent, horizontally-overlapping lines into a block.
''' Each block's source text is the joined line text, sent to the translator as
''' one unit so the model has sentence context.
'''
''' Four rules decide whether a line continues the current block, and the fourth
''' is the only one that looks at the page rather than at the line's neighbours:
'''   1. column   - the boxes overlap horizontally by at least a quarter
'''                 of the narrower one;
'''   2. pitch    - top-to-top distance within <see cref="PitchFactor"/> of the
'''                 page's median line pitch. Pitch, not the gap BETWEEN ink
'''                 boxes: a line of small caps has no descenders, so its ink box
'''                 is visibly shorter than the line, the gap measured to the next
'''                 one comes out large, and one speech balloon used to break into
'''                 three plates - which cost the translator the sentence, not
'''                 just the geometry. There is no ink-gap fallback any more
'''                 (`OCR-OVERLAY` rule 6 forbids the ink gap outright): a page
'''                 with too few pairs to corroborate a median widens the
'''                 tolerance instead, and a page with NO pitch at all is not
'''                 grouped, because there is nothing to group by;
'''   3. type size - the line's height is within <see cref="TypeSizeRatio"/> of
'''                 the median height of the block SO FAR. A heading sits on the
'''                 same pitch as the paragraph under it, so no pitch threshold
'''                 separates the two - only the type size does;
'''   4. dissolve  - a finished block that covers most of the frame AND is mostly
'''                 gaps is handed back one-block-per-line. A form, a list or an
'''                 application window sets separate regions in ONE size at ONE
'''                 pitch in ONE column, so rules 1-3 see no seam anywhere and the
'''                 whole page arrives as a single plate.
'''
''' The constants come from a measured corpus where one exists - the REFERENCE implementation's
''' (`OCR-PIPELINE` section 2.5, and its section 5 table with the status column of amendment 1.2
''' item M) - and this repository's archived report
''' (docs/specifications/done/SPECIFICATION_OCR_OVERLAY_ACCURACY.md sections 4, 13.7 and 14.1)
''' records the same brackets as the reference measured them, on the reference's material.
'''
''' Constant status (`OCR-OVERLAY` rule 13). *Derived here* would name a report of THIS product
''' and the two measurements the number sits between - no row below can, so none claims it;
''' *inherited* names the source and the ticket that owns deriving it here; *chosen here, NOT
''' derived* gives the reason (the third status the reference proposed as `policy`, see
''' `OCR-OVERLAY` PROPOSAL-2026-09-25-constant-status-and-exchange). Ticket 033 owns every
''' derivation below (it replaced ticket 025, which is closed and owns nothing).
''' | Constant | Status | Source / reason |
''' | --- | --- | --- |
''' | <see cref="PitchFactor"/> | inherited | `OCR-PIPELINE` section 2.5 and section 5 (1.2); the source itself marks it `policy` - anchored on two balloon scenes, never bracketed. Ticket 033. |
''' | <see cref="MaxLeadingRatio"/> | inherited | `OCR-PIPELINE` section 5 (3); `policy` at the source - typographic leading sits near 2.9 line heights. Ticket 033. |
''' | <see cref="MinPitchPairs"/> | chosen here, NOT derived | arithmetic, not a corpus: on one pair the median IS that pair, so one misread line would set the page threshold. Two is the smallest count at which a median can outvote a single outlier. |
''' | <see cref="UncorroboratedPitchFactor"/> | chosen here, NOT derived | chosen so a two-line block that grouped under the removed ink-gap fallback still groups: that rule allowed a top-to-top delta of roughly 1.9 median heights, this one allows 1.5 pitches. No external source. Ticket 033 owns measuring it. |
''' | <see cref="TypeSizeRatio"/> | inherited | `OCR-PIPELINE` section 2.5 and section 5, `derived` THERE: 1.42x the widest spread within one inscription, 1.86x the narrowest step between two texts, measured on the reference's corpus (archived report section 13.7 records it). The bracket is why the value is trusted; it is not this product's measurement. Ticket 033. |
''' | <see cref="MaxPlateCoverage"/> | inherited | `OCR-PIPELINE` section 2.5 and section 5, `derived` THERE (`ocr_plate_coverage_2026-08-13`): 0.4004 the largest legitimate plate, 0.6829 the application-window defect (archived report section 14.1). Ticket 033. |
''' | <see cref="MinPlateLineFill"/> | inherited | same source and report: 0.7921 legitimate, 0.6608 the same defect. The rejected alternative (area rather than height) is recorded on the constant itself, per rule 15. Ticket 033. |
''' </summary>
Public Module OcrBlockBuilder

    ''' <summary>How far past the median line pitch a line may sit and still continue
    ''' the block.</summary>
    Public Const PitchFactor As Double = 1.2

    ''' <summary>Pairs further apart than this many median line heights are a section
    ''' break, not leading, and do not contribute to the median pitch.</summary>
    Public Const MaxLeadingRatio As Double = 3.0

    ''' <summary>Fewest qualifying pairs (so: three lines in a column) before the median
    ''' pitch is trusted. On a single pair the median IS that pair, and one misread line
    ''' would set the threshold for the whole image.
    '''
    ''' It does NOT protect against the other failure of a median - a column whose lines
    ''' partly failed the confidence threshold still yields plenty of pairs, and the
    ''' median is then an honest median of an incomplete set. That case is what rule 3
    ''' is for.</summary>
    Public Const MinPitchPairs As Integer = 2

    ''' <summary>Tolerance applied to a pitch that fewer than <see cref="MinPitchPairs"/>
    ''' pairs corroborated - one pair, whose "median" is simply that pair. Wider than
    ''' <see cref="PitchFactor"/> because the number it multiplies is uncorroborated in BOTH
    ''' directions: a single pair that came out short would otherwise make the threshold
    ''' stricter than any measured page.
    '''
    ''' INHERITED, not derived - see the constant status table on the module. It replaces the
    ''' ink-gap fallback that `OCR-OVERLAY` rule 6 forbids, and it is set so the replacement
    ''' does not quietly become stricter than what shipped: the old rule allowed a top-to-top
    ''' delta of about 1.9 median heights on median-sized boxes.</summary>
    Public Const UncorroboratedPitchFactor As Double = 1.5

    ''' <summary>Largest height ratio, in either direction, between a line and the median
    ''' of the block it wants to join. Bracketed by measurement: 1.42x is the widest spread
    ''' WITHIN one hand-annotated inscription, 1.86x the narrowest step BETWEEN two separate
    ''' texts; 1.6 is the geometric mean of 1.42 and a recognized 1.81.</summary>
    Public Const TypeSizeRatio As Double = 1.6

    ''' <summary>Fraction of the image a single block may cover before it is a candidate
    ''' for dissolving. Bracketed by 0.4004 (the largest legitimate plate measured - a
    ''' single inscription) and 0.6829 (an application-window screenshot whose six list
    ''' rows arrived as one plate).</summary>
    Public Const MaxPlateCoverage As Double = 0.52

    ''' <summary>Fraction of a block's height its own lines must fill to be considered a
    ''' real text body. Below it, almost all of the height is the space BETWEEN separate
    ''' regions. Bracketed by 0.7921 (legitimate) and 0.6608 (the same screenshot).
    '''
    ''' Deliberately measured along the VERTICAL axis. The obvious alternative - what
    ''' fraction of the block's AREA its lines occupy - was measured and rejected: it puts
    ''' the defect (0.5891) ABOVE three scenes that must not be torn (0.3621, 0.4582,
    ''' 0.5185), because area mixes leading with the ragged right edge of centred text.</summary>
    Public Const MinPlateLineFill As Double = 0.72

    ''' <param name="imageSize">Pixel grid the line boxes are expressed in. Needed by the
    ''' dissolve rule, which is the one rule stated as a fraction of the page. An empty
    ''' size disables that rule only.</param>
    Public Function BuildBlocks(lines As List(Of OcrLine), imageSize As Size) As List(Of OcrBlock)
        Return BuildBlocks(lines, imageSize, Nothing)
    End Function

    ''' <param name="dropped">Receives every block the translatability filter refused, with
    ''' the rule that refused it (section 16.1). Optional - pass Nothing when nobody is
    ''' collecting - but the record comes out of the SAME call that decides, so it cannot
    ''' describe a threshold that is no longer the one being applied.</param>
    ''' <param name="preserveOrder">Take the incoming order as reading order instead of
    ''' sorting. Passed only when <see cref="OcrLineSplitter"/> cut a stitched line on this
    ''' page and already arranged the runs column by column
    ''' (<see cref="OcrResult.LinesRegrouped"/>): the sort below is top-then-left, which is
    ''' precisely what interleaves two columns, so re-running it would undo the regrouping and
    ''' hand this single-pass walk a page it closes a plate on at every second line.
    ''' Deliberately NOT the default - on a page with no stitch the engine's order is the one
    ''' this walk has always been fed, and a stage that touches nothing cannot regress
    ''' anything.</param>
    Public Function BuildBlocks(lines As List(Of OcrLine), imageSize As Size,
                                dropped As List(Of OcrDroppedLine),
                                Optional preserveOrder As Boolean = False) As List(Of OcrBlock)
        Dim blocks As New List(Of OcrBlock)
        If lines Is Nothing OrElse lines.Count = 0 Then Return blocks

        ' Top-to-bottom, then left-to-right. The engine hands lines back in its own
        ' order - a sparse-text pass returns them out of reading order entirely - so
        ' the walk below cannot rely on the input sequence.
        Dim ordered As List(Of OcrLine) = If(preserveOrder,
            lines,
            lines.
                OrderBy(Function(l) l.Box.Top).
                ThenBy(Function(l) l.Box.Left).ToList())

        Dim medianHeight As Integer = MedianLineHeight(ordered)
        ' ONE number decides rule 2 now: the largest top-to-top step that still continues a
        ' block. 0 means the page yielded no line pitch at all, and rule 6 leaves exactly one
        ' honest answer for that - do not group.
        Dim maxStep As Integer = MaxTopDelta(ordered, medianHeight)

        Dim current As New List(Of OcrLine)
        For Each line As OcrLine In ordered
            If current.Count = 0 Then
                current.Add(line)
            ElseIf ContinuesBlock(current, line, maxStep) Then
                current.Add(line)
            Else
                blocks.Add(MakeBlock(current))
                current = New List(Of OcrLine) From {line}
            End If
        Next
        If current.Count > 0 Then blocks.Add(MakeBlock(current))

        ' Drop blocks whose text is not worth translating (OCR noise, a bare URL,
        ' a single stray word from a texture).
        Dim kept As New List(Of OcrBlock)
        For Each b As OcrBlock In blocks
            Dim rejection As String = OcrTextFilter.RejectionReason(b.SourceText, b.Lines.Count)
            If rejection.Length = 0 Then
                kept.Add(b)
            ElseIf dropped IsNot Nothing Then
                dropped.Add(New OcrDroppedLine With {
                    .Text = b.SourceText,
                    .Box = b.Box,
                    .Confidence = -1.0F,
                    .Rule = rejection
                })
            End If
        Next

        ' Dissolve AFTER the filter, never before: the assembled block text has already
        ' passed it, and re-running the check on each short line on its own would throw
        ' part of it away - turning a composition fix into lost translation. Nothing is
        ' discarded here, so a scene that reads today cannot lose a plate.
        Dim result As New List(Of OcrBlock)
        For Each b As OcrBlock In kept
            If ShouldDissolve(b, imageSize) Then
                For Each l As OcrLine In b.Lines
                    result.Add(MakeBlock(New List(Of OcrLine) From {l}))
                Next
            Else
                result.Add(b)
            End If
        Next
        Return result
    End Function

    ''' <summary>
    ''' Rules 1-3: does this line continue the block being assembled?
    '''
    ''' <paramref name="maxTopDelta"/> already carries its tolerance (see
    ''' <see cref="MaxTopDelta"/>), so there is one comparison here and no second place where
    ''' a factor could be applied twice. Zero means the page has no pitch: rule 6 permits no
    ''' ink-gap fallback, and a page with nothing to group BY is handed back one block per
    ''' line rather than grouped by a different measurement wearing pitch's name.
    ''' </summary>
    Private Function ContinuesBlock(block As List(Of OcrLine), line As OcrLine, maxTopDelta As Integer) As Boolean
        Dim prev As OcrLine = block(block.Count - 1)
        If Not SharesColumn(prev.Box, line.Box) Then Return False
        If Not SameTypeSize(block, line) Then Return False
        If maxTopDelta <= 0 Then Return False

        ' Top-to-top, so an ink box shorter than its line cannot inflate the distance.
        Return (line.Box.Top - prev.Box.Top) <= maxTopDelta
    End Function

    ''' <summary>
    ''' Rule 3. Compared against the block's MEDIAN height rather than the previous line's,
    ''' or one atypical box - a line holding a single short word, a line of small caps with
    ''' no descenders - would close the block by itself. A height of zero on either side
    ''' means "not measured" and joins: clustering must not get stricter because a value is
    ''' missing.
    ''' </summary>
    Private Function SameTypeSize(block As List(Of OcrLine), line As OcrLine) As Boolean
        Dim h As Integer = line.Box.Height
        If h <= 0 Then Return True
        Dim median As Integer = MedianLineHeight(block)
        If median <= 0 Then Return True
        Dim ratio As Double = If(h >= median, h / CDbl(median), median / CDbl(h))
        Return ratio <= TypeSizeRatio
    End Function

    ''' <summary>
    ''' Rule 4. Both fractions are required, and that is measured rather than cautious:
    ''' coverage alone would dissolve a hand-annotated inscription covering 0.6087 of its
    ''' frame, and looseness alone would dissolve three lines of ordinary prose whose
    ''' 0.6667 is simply paragraph leading.
    ''' </summary>
    Private Function ShouldDissolve(b As OcrBlock, imageSize As Size) As Boolean
        ' A single-line block cannot be dissolved: its box IS its line.
        If b.Lines Is Nothing OrElse b.Lines.Count < 2 Then Return False
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 Then Return False
        If b.Box.Height <= 0 OrElse b.Box.Width <= 0 Then Return False

        Dim coverage As Double = (b.Box.Width * CDbl(b.Box.Height)) / (imageSize.Width * CDbl(imageSize.Height))
        If coverage <= MaxPlateCoverage Then Return False

        Dim inked As Integer = 0
        For Each l As OcrLine In b.Lines
            inked += Math.Max(0, l.Box.Height)
        Next
        Return (inked / CDbl(b.Box.Height)) < MinPlateLineFill
    End Function

    Private Function MakeBlock(lines As List(Of OcrLine)) As OcrBlock
        Dim box As Rectangle = lines(0).Box
        For i As Integer = 1 To lines.Count - 1
            box = Rectangle.Union(box, lines(i).Box)
        Next

        Dim text As String = String.Join(" ", lines.Select(Function(l) l.Text)).Trim()

        Return New OcrBlock With {
            .Lines = lines,
            .Box = box,
            .SourceText = text,
            .TranslatedText = ""
        }
    End Function

    ''' <summary>
    ''' Do the two boxes belong to the same column? At least ~25 % of the narrower box has
    ''' to overlap. Public because the overlay growth budget (S2) has to ask the SAME
    ''' question - a plate may only grow down into space no other plate in its column
    ''' claims.
    ''' </summary>
    Public Function SharesColumn(a As Rectangle, b As Rectangle) As Boolean
        Dim left As Integer = Math.Max(a.Left, b.Left)
        Dim right As Integer = Math.Min(a.Right, b.Right)
        Dim overlap As Integer = right - left
        Dim minWidth As Integer = Math.Max(1, Math.Min(a.Width, b.Width))
        Return overlap > 0 AndAlso (overlap >= minWidth * 0.25)
    End Function

    ''' <summary>
    ''' The largest top-to-top step that still continues a block, tolerance included.
    '''
    ''' Built from the median top-to-top distance over adjacent line pairs that share a
    ''' column, run forwards, and sit within <see cref="MaxLeadingRatio"/> median heights of
    ''' each other. Fewer than <see cref="MinPitchPairs"/> such pairs means the median is
    ''' simply that one pair, so it is widened by <see cref="UncorroboratedPitchFactor"/>
    ''' instead of being thrown away: `OCR-OVERLAY` rule 6 forbids measuring the gap between
    ''' ink boxes at all, which is what this used to fall back to, and an uncorroborated pitch
    ''' is still a pitch.
    '''
    ''' Returns 0 when NO pair qualified. That is not a pitch with a wide tolerance, it is the
    ''' absence of one, and the caller declines to group rather than inventing a measurement.
    ''' </summary>
    Private Function MaxTopDelta(ordered As List(Of OcrLine), medianHeight As Integer) As Integer
        Dim maxLeading As Double = Math.Max(1, medianHeight) * MaxLeadingRatio
        Dim pitches As New List(Of Integer)
        For i As Integer = 1 To ordered.Count - 1
            Dim previous As Rectangle = ordered(i - 1).Box
            Dim box As Rectangle = ordered(i).Box
            Dim delta As Integer = box.Top - previous.Top
            If delta <= 0 Then Continue For
            If delta > maxLeading Then Continue For
            If Not SharesColumn(previous, box) Then Continue For
            pitches.Add(delta)
        Next
        If pitches.Count = 0 Then Return 0
        pitches.Sort()
        Dim pitch As Integer = pitches(pitches.Count \ 2)
        Dim factor As Double = If(pitches.Count >= MinPitchPairs, PitchFactor, UncorroboratedPitchFactor)
        Return CInt(Math.Ceiling(pitch * factor))
    End Function

    Private Function MedianLineHeight(lines As List(Of OcrLine)) As Integer
        Dim heights As List(Of Integer) = lines.Select(Function(l) l.Box.Height).OrderBy(Function(h) h).ToList()
        If heights.Count = 0 Then Return 12
        Return heights(heights.Count \ 2)
    End Function

End Module
