Option Strict On

Imports System.Drawing

''' <summary>
''' The pure half of overlay text fitting (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, stage S2):
''' which font sizes to try, and how far a plate may grow downwards. Measuring stays in
''' Main_Form, where a <c>Graphics</c> exists; everything decidable without one lives here so
''' it can be proven by test instead of by eye.
'''
''' The behaviour ladder, in order, and the top edge does not move on any rung:
'''   1. shrink the font down to the floor;
'''   2. grow the plate downwards, within its budget;
'''   3. RELEASE the plate - it grows past the budget, as far as the text needs
'''      (OCR-OVERLAY rule 9: never an ellipsis, never a crop).
'''
''' A second, quieter effect: the old fitter walked DOWN FROM 200 px in 1 px steps and did it
''' inside every Paint, for every block - up to ~190 MeasureString calls per block per frame,
''' paid on each zoom and resize. Geometric steps from the original text size give ~9.
'''
''' Constant status (OCR-OVERLAY rule 13). Every row says where its number comes from; a row
''' marked *inherited* names the ticket that owns deriving it here - ticket 033, which replaced
''' the closed ticket 025.
''' | Constant | Status | Where it comes from |
''' | --- | --- | --- |
''' | <see cref="StartFactor"/> | inherited | `OCR-PIPELINE` §3.1/§3.4 (font fit factor 0.92; `policy` at the source, amendment 1.2 item M), the reference implementation's plate fit. Not re-derived on this product's corpus - ticket 033 owns that. |
''' | <see cref="StepFactor"/> | inherited | `OCR-PIPELINE` §3.4 ("8% per iteration"; the fit ladder is `policy` at the source, item L). Ticket 033. |
''' | <see cref="FloorFactor"/> | inherited | `OCR-PIPELINE` §3.4 ("floor at 50% of base"). Ticket 033. |
''' | <see cref="MaxCandidates"/> | chosen here, NOT derived | arithmetic, not measurement: 0.92^8 ~ 0.51, so start -> floor is ~9 rungs and 40 is a guard an input would have to be degenerate to reach. Proven by `OcrOverlayFitTests.Candidates_AreAboutNine_AndNeverMoreThanTheGuard`. |
''' </summary>
Public Module OcrOverlayFit

    ''' <summary>Where the ladder starts, as a fraction of the source text's own size. Just
    ''' under 1.0, as the reference implementation's plate fit has it; this product draws with
    ''' no padding (ticket 033 O-2), which the factor was not re-derived for. INHERITED from `OCR-PIPELINE` §3.4 - see the constant status
    ''' table on the module.</summary>
    Public Const StartFactor As Double = 0.92

    ''' <summary>Each rung is 8 % smaller than the one above. INHERITED from
    ''' `OCR-PIPELINE` §3.4.</summary>
    Public Const StepFactor As Double = 0.92

    ''' <summary>The ladder stops at half the starting size: below that the translation is no
    ''' longer a plausible stand-in for the text it replaces, and growing the plate (rung 2)
    ''' is the better answer. INHERITED from `OCR-PIPELINE` §3.4.</summary>
    Public Const FloorFactor As Double = 0.5

    ''' <summary>Guard against a degenerate input, not a working number: from the start to the
    ''' floor is about nine rungs (0.92^8 ~ 0.51). DERIVED HERE, by arithmetic.</summary>
    Public Const MaxCandidates As Integer = 40

    ''' <summary>
    ''' Median glyph height of a block's source text, in image pixels. This is what anchors
    ''' the overlay font to the size of the text it replaces, and it lives here rather than in
    ''' the renderer so the diagnostics dump reports the value the renderer used instead of a
    ''' second implementation of the same idea.
    '''
    ''' The median is taken over WORD boxes, not line boxes - `OCR-OVERLAY` rule 5, second
    ''' half. A line box is the union of its words, so one artifact inside it (a descender
    ''' picked up as its own glyph, a speck of the drawing read as a comma) inflates the whole
    ''' line and through it the type size of every plate in the block. Word boxes also
    ''' outnumber line boxes several to one, so the median is better corroborated by the same
    ''' argument <see cref="OcrBlockBuilder.MinPitchPairs"/> makes about pitch.
    '''
    ''' The line boxes remain the fallback for a block whose lines carry no words - a document
    ''' built before S7 gave lines their real words, or an engine pass that reported none.
    ''' Falling back is not a second algorithm: it is the same median over the only boxes
    ''' there are.
    ''' </summary>
    Public Function MedianLineHeight(block As OcrBlock) As Integer
        If block Is Nothing Then Return 0
        If block.Lines Is Nothing OrElse block.Lines.Count = 0 Then Return block.Box.Height

        Dim heights As New List(Of Integer)
        For Each l As OcrLine In block.Lines
            If l Is Nothing OrElse l.Words Is Nothing Then Continue For
            For Each w As OcrWord In l.Words
                If w IsNot Nothing AndAlso w.Box.Height > 0 Then heights.Add(w.Box.Height)
            Next
        Next

        If heights.Count = 0 Then
            For Each l As OcrLine In block.Lines
                If l IsNot Nothing AndAlso l.Box.Height > 0 Then heights.Add(l.Box.Height)
            Next
        End If

        If heights.Count = 0 Then Return block.Box.Height
        heights.Sort()
        Return heights(heights.Count \ 2)
    End Function

    ''' <summary>
    ''' Font sizes to try, largest first, in display pixels.
    ''' </summary>
    ''' <param name="originalLineHeightPx">Median source glyph height of the block, already
    ''' scaled to display pixels. This is what anchors the overlay to the size of the text it
    ''' replaces instead of to some small capped font.</param>
    ''' <param name="minFontPx">Hard floor (the renderer's MinOverlayFont).</param>
    ''' <param name="maxFontPx">Hard ceiling (the renderer's MaxOverlayFont).</param>
    Public Function FontCandidates(originalLineHeightPx As Double, minFontPx As Double, maxFontPx As Double) As List(Of Double)
        Dim result As New List(Of Double)

        Dim floorBound As Double = Math.Max(1.0, minFontPx)
        Dim ceilingBound As Double = Math.Max(floorBound, maxFontPx)

        ' A non-positive line height means the block never reported one; fall back to the
        ' ceiling so the ladder still has somewhere to start.
        Dim start As Double = If(originalLineHeightPx > 0, originalLineHeightPx * StartFactor, ceilingBound)
        start = Math.Min(Math.Max(start, floorBound), ceilingBound)

        Dim stop_ As Double = Math.Max(start * FloorFactor, floorBound)

        Dim size As Double = start
        While result.Count < MaxCandidates
            result.Add(size)
            If size <= stop_ Then Exit While
            size = Math.Max(size * StepFactor, stop_)
        End While
        Return result
    End Function

    ''' <summary>
    ''' How far, in the same units as the rectangles, this plate may grow downwards before it
    ''' would leave the image or reach a plate below it in the same column. Both bounds are
    ''' geometric, which is why they belong here and not in the renderer.
    '''
    ''' Bounding by neighbours keeps the first two rungs clear of the plate below. Only a
    ''' RELEASED plate (<see cref="ReleasedHeight"/>, rung 3) grows past this budget and may
    ''' overlap it; paint order then decides - the overflow rule at the top of
    ''' Main_Form.OcrOverlay.vb.
    ''' </summary>
    ''' <param name="plate">The plate as it stands before growing.</param>
    ''' <param name="others">Every other plate rectangle, in the same space. The plate itself
    ''' may be in the list; an identical rectangle is ignored.</param>
    ''' <param name="bottomBound">Bottom edge of the displayed image.</param>
    Public Function GrowthBudget(plate As Rectangle, others As IEnumerable(Of Rectangle), bottomBound As Integer) As Integer
        If plate.Width <= 0 Then Return 0

        Dim limit As Integer = bottomBound
        If others IsNot Nothing Then
            For Each other As Rectangle In others
                If other = plate Then Continue For
                If other.Width <= 0 OrElse other.Height <= 0 Then Continue For
                ' Only a plate BELOW this one, and only one sharing the column - the same
                ' >= 25 % predicate the clustering uses, so "same column" means one thing in
                ' this feature.
                If other.Top < plate.Bottom Then Continue For
                If Not OcrBlockBuilder.SharesColumn(plate, other) Then Continue For
                If other.Top < limit Then limit = other.Top
            Next
        End If

        Return Math.Max(0, limit - plate.Bottom)
    End Function

    ''' <summary>
    ''' The last rung, and the whole of `OCR-OVERLAY` rule 9: the height a RELEASED plate
    ''' takes. The ladder reaches here only when the text still does not fit at the font floor
    ''' with the growth budget spent, and rule 9 permits no ellipsis and no crop - so the
    ''' plate grows past its budget, taking exactly the height the text needs.
    '''
    ''' The one bound left is the displayed image itself. A plate that ran off the bottom of
    ''' the picture would be drawn on the window's background, where nothing anchors it to the
    ''' text it replaces and where it would cover the toolbar area on a fitted image; stopping
    ''' at the image edge is the only bound that is still about the picture.
    '''
    ''' The top edge does not move, here or on any other rung: <paramref name="plate"/>.Top is
    ''' the source block's anchor.
    ''' </summary>
    ''' <param name="plate">The plate at its un-grown size (top and height as mapped).</param>
    ''' <param name="neededHeight">What the text measured at the floor font actually needs.</param>
    ''' <param name="bottomBound">Bottom edge of the displayed image.</param>
    Public Function ReleasedHeight(plate As Rectangle, neededHeight As Double, bottomBound As Integer) As Integer
        Dim wanted As Integer = plate.Height
        If neededHeight > wanted Then wanted = CInt(Math.Ceiling(neededHeight))
        ' Never shrink below the mapped box: that would move geometry every other rung
        ' promised to leave alone.
        Dim available As Integer = Math.Max(plate.Height, bottomBound - plate.Top)
        Return Math.Min(wanted, available)
    End Function

End Module
