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
'''   3. only then trim - and say so, rather than dropping the tail in silence.
'''
''' A second, quieter effect: the old fitter walked DOWN FROM 200 px in 1 px steps and did it
''' inside every Paint, for every block - up to ~190 MeasureString calls per block per frame,
''' paid on each zoom and resize. Geometric steps from the original text size give ~9.
''' </summary>
Public Module OcrOverlayFit

    ''' <summary>Where the ladder starts, as a fraction of the source text's own size. Just
    ''' under 1.0 because the translation is drawn with padding inside a box the source text
    ''' filled edge to edge.</summary>
    Public Const StartFactor As Double = 0.92

    ''' <summary>Each rung is 8 % smaller than the one above.</summary>
    Public Const StepFactor As Double = 0.92

    ''' <summary>The ladder stops at half the starting size: below that the translation is no
    ''' longer a plausible stand-in for the text it replaces, and growing the plate (rung 2)
    ''' is the better answer.</summary>
    Public Const FloorFactor As Double = 0.5

    ''' <summary>Guard against a degenerate input, not a working number: from the start to the
    ''' floor is about nine rungs (0.92^8 ~ 0.51).</summary>
    Public Const MaxCandidates As Integer = 40

    ''' <summary>
    ''' Median glyph (line) height of a block's source lines, in image pixels. This is what
    ''' anchors the overlay font to the size of the text it replaces, and it lives here rather
    ''' than in the renderer so the diagnostics dump reports the value the renderer used
    ''' instead of a second implementation of the same idea.
    ''' </summary>
    Public Function MedianLineHeight(block As OcrBlock) As Integer
        If block Is Nothing Then Return 0
        If block.Lines Is Nothing OrElse block.Lines.Count = 0 Then Return block.Box.Height
        Dim heights As List(Of Integer) = block.Lines.
            Select(Function(l) l.Box.Height).
            Where(Function(h) h > 0).
            OrderBy(Function(h) h).ToList()
        If heights.Count = 0 Then Return block.Box.Height
        Return heights(heights.Count \ 2)
    End Function

    ''' <summary>
    ''' Font sizes to try, largest first, in display pixels.
    ''' </summary>
    ''' <param name="originalLineHeightPx">Median source line height of the block, already
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
    ''' Bounding by neighbours is what makes paint ORDER stop mattering: plates cannot end up
    ''' overlapping, so no plate can be drawn over another one's text.
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

End Module
