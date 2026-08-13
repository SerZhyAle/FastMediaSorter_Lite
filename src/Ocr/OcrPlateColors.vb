#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing

''' <summary>
''' Picks the plate's paper and ink colours out of the image itself
''' (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, stage S4). The plate used to be a fixed
''' near-white rectangle with black text, which on a dark or coloured picture is the most
''' visible piece of sloppiness in the whole feature.
'''
''' Pure on purpose: it takes an ARGB array, not a Bitmap, so it can be proven by test and so
''' the caller can lock the bitmap ONCE for every block instead of once per block. It runs in
''' the background right after the blocks are built, while the pipeline's private snapshot is
''' still alive - by paint time those pixels are gone, and sampling them in Paint would cost
''' the frame anyway.
'''
''' Modern-only: this is a change of appearance, not a defect fix, and the x86 fallback does
''' not take those (CLAUDE.md, "Maintenance policy"). A block it produced carries 0 in both
''' fields, which the renderer reads as "not computed" and paints the old constant.
''' </summary>
Public Module OcrPlateColors

    ''' <summary>Roughly how many pixels to look at under a block. A sub-sample, never a full
    ''' pass: the invariant is that no stage makes OCR slower on an image that already worked.</summary>
    Public Const SampleBudget As Integer = 6000

    ''' <summary>Sum of per-channel distances from the background above which a pixel counts
    ''' as ink.</summary>
    Public Const InkDistance As Integer = 90

    ''' <summary>Below this share of ink samples there is no text to sample - a solid fill, a
    ''' photograph - and the ink falls back to near-black or near-white.</summary>
    Public Const MinInkFraction As Double = 0.015

    ''' <summary>Luma distance the pair must keep. Under it the sampled ink is replaced by the
    ''' fallback, because an unreadable plate is worse than a plate of the wrong colour.</summary>
    Public Const MinLumaContrast As Integer = 55

    ''' <summary>Fewest ring samples before the orientation test is allowed to decide at all.
    ''' Fewer votes than this and the pair is left as sampled.</summary>
    Public Const RingMinVotes As Integer = 40

    ''' <summary>The ring is a third of a LINE height wide (floor 2 px). A ring a whole line
    ''' wide stops reading the text's surroundings and starts reading the next object.</summary>
    Public Const RingBandDivisor As Integer = 3

    ''' <summary>Ink is sampled only in the top 1.3 line heights of the block.
    '''
    ''' This and <see cref="RingBandDivisor"/> are TWO separate constants over the same line
    ''' height, and keeping them separate is the point: the other project held both in one
    ''' variable, and one of their two implementations passed the ink band into the ring test.
    ''' The ring came out 0.43 of a line instead of 0.33 - same block, different vote, on
    ''' exactly the decision that protects the plate from coming out inverted.</summary>
    Public Const InkBandFactor As Double = 1.3

    ''' <summary>Paper and ink for one plate, ARGB. Both are 0 when nothing could be
    ''' computed - which the renderer reads as "not computed", never as black.</summary>
    Public Structure PlatePalette
        Public Background As Integer
        Public Ink As Integer
    End Structure

    ''' <param name="pixels">The whole image as ARGB, row-major, length width * height.</param>
    ''' <param name="lineHeight">Median source line height of the block, in image pixels.</param>
    Public Function Compute(pixels As Integer(), width As Integer, height As Integer,
                            box As Rectangle, lineHeight As Integer) As PlatePalette
        Dim empty As PlatePalette

        If pixels Is Nothing OrElse width <= 0 OrElse height <= 0 Then Return empty
        If pixels.Length < width * height Then Return empty

        Dim area As Rectangle = Rectangle.Intersect(box, New Rectangle(0, 0, width, height))
        If area.Width <= 0 OrElse area.Height <= 0 Then Return empty

        Dim line As Integer = If(lineHeight > 0, lineHeight, area.Height)

        ' --- background: median over the whole block ---------------------------
        Dim paper As New ChannelMedian()
        Dim step_ As Integer = SampleStep(area.Width, area.Height)
        For y As Integer = area.Top To area.Bottom - 1 Step step_
            Dim row As Integer = y * width
            For x As Integer = area.Left To area.Right - 1 Step step_
                paper.Add(pixels(row + x))
            Next
        Next
        If paper.Count = 0 Then Return empty
        Dim background As Integer = paper.Median()

        ' --- ink: median over what differs from the paper, top band only -------
        ' Median, not mean, and that is structural rather than a preference: the "differs
        ' from the background" test lets through nearly the whole anti-aliased edge of every
        ' glyph, so a mean sits BETWEEN ink and paper by construction. Measured on one
        ' signature: mean rgb(61,61,61), median rgb(7,7,7), against true ink rgb(17,17,17).
        Dim bandBottom As Integer = Math.Min(area.Bottom, area.Top + Math.Max(1, CInt(line * InkBandFactor)))
        Dim ink As New ChannelMedian()
        Dim bandSamples As Integer = 0
        For y As Integer = area.Top To bandBottom - 1 Step step_
            Dim row As Integer = y * width
            For x As Integer = area.Left To area.Right - 1 Step step_
                Dim argb As Integer = pixels(row + x)
                If IsTransparent(argb) Then Continue For
                bandSamples += 1
                If ChannelDistance(argb, background) > InkDistance Then ink.Add(argb)
            Next
        Next

        Dim inkColor As Integer
        If bandSamples > 0 AndAlso ink.Count >= bandSamples * MinInkFraction Then
            inkColor = ink.Median()
        Else
            inkColor = FallbackInk(background)
        End If

        ' A pair that is technically sampled but unreadable is worse than an honest fallback.
        If LumaDistance(background, inkColor) < MinLumaContrast Then inkColor = FallbackInk(background)

        ' --- orientation: decided OUTSIDE the block ----------------------------
        If RingSaysInverted(pixels, width, height, area, line, background, inkColor) Then
            Dim swap As Integer = background
            background = inkColor
            inkColor = swap
        End If

        Return New PlatePalette With {.Background = background, .Ink = inkColor}
    End Function

    ''' <summary>
    ''' Display lettering drawn inside a tight box covers more of that box with strokes than
    ''' with paper, so a median taken INSIDE would return an exact inversion of the two
    ''' colours. What surrounds the block does not have that problem, hence a ring outside it.
    ''' </summary>
    Private Function RingSaysInverted(pixels As Integer(), width As Integer, height As Integer,
                                      area As Rectangle, lineHeight As Integer,
                                      background As Integer, ink As Integer) As Boolean
        Dim band As Integer = Math.Max(2, lineHeight \ RingBandDivisor)
        Dim outer As Rectangle = Rectangle.Intersect(Rectangle.Inflate(area, band, band), New Rectangle(0, 0, width, height))
        If outer.Width <= 0 OrElse outer.Height <= 0 Then Return False

        Dim inkVotes As Integer = 0
        Dim paperVotes As Integer = 0
        For y As Integer = outer.Top To outer.Bottom - 1
            Dim row As Integer = y * width
            Dim insideRows As Boolean = (y >= area.Top AndAlso y < area.Bottom)
            For x As Integer = outer.Left To outer.Right - 1
                ' The ring only - everything strictly inside the block is skipped.
                If insideRows AndAlso x >= area.Left AndAlso x < area.Right Then Continue For
                Dim argb As Integer = pixels(row + x)
                If IsTransparent(argb) Then Continue For
                If ChannelDistance(argb, ink) < ChannelDistance(argb, background) Then
                    inkVotes += 1
                Else
                    paperVotes += 1
                End If
            Next
        Next

        If inkVotes + paperVotes < RingMinVotes Then Return False
        Return inkVotes > paperVotes
    End Function

    ''' <summary>Stride that brings the block down to about <see cref="SampleBudget"/>
    ''' samples, so a huge block costs the same as a small one.</summary>
    Private Function SampleStep(w As Integer, h As Integer) As Integer
        Dim total As Long = CLng(w) * h
        If total <= SampleBudget Then Return 1
        Return Math.Max(1, CInt(Math.Ceiling(Math.Sqrt(total / CDbl(SampleBudget)))))
    End Function

    Private Function IsTransparent(argb As Integer) As Boolean
        Return ((argb >> 24) And &HFF) < 128
    End Function

    Private Function ChannelDistance(a As Integer, b As Integer) As Integer
        Return Math.Abs(((a >> 16) And &HFF) - ((b >> 16) And &HFF)) +
               Math.Abs(((a >> 8) And &HFF) - ((b >> 8) And &HFF)) +
               Math.Abs((a And &HFF) - (b And &HFF))
    End Function

    Private Function Luma(argb As Integer) As Integer
        Dim r As Integer = (argb >> 16) And &HFF
        Dim g As Integer = (argb >> 8) And &HFF
        Dim b As Integer = argb And &HFF
        Return CInt((r * 299 + g * 587 + b * 114) \ 1000)
    End Function

    Private Function LumaDistance(a As Integer, b As Integer) As Integer
        Return Math.Abs(Luma(a) - Luma(b))
    End Function

    Private Function FallbackInk(background As Integer) As Integer
        Return If(Luma(background) >= 128, Pack(20, 20, 20), Pack(235, 235, 235))
    End Function

    Private Function Pack(r As Integer, g As Integer, b As Integer) As Integer
        ' Alpha is always 255, which also keeps a legitimate pure black from colliding with
        ' the "not computed" sentinel of 0.
        Return Color.FromArgb(255, r, g, b).ToArgb()
    End Function

    ''' <summary>Per-channel median via 256-bin histograms: one pass, no growing list, and no
    ''' sort - this runs over thousands of samples per block on a background thread.</summary>
    Private NotInheritable Class ChannelMedian
        Private ReadOnly red(255) As Integer
        Private ReadOnly green(255) As Integer
        Private ReadOnly blue(255) As Integer
        Private samples As Integer

        Public ReadOnly Property Count As Integer
            Get
                Return samples
            End Get
        End Property

        Public Sub Add(argb As Integer)
            red((argb >> 16) And &HFF) += 1
            green((argb >> 8) And &HFF) += 1
            blue(argb And &HFF) += 1
            samples += 1
        End Sub

        Public Function Median() As Integer
            If samples = 0 Then Return 0
            Return Pack(Middle(red), Middle(green), Middle(blue))
        End Function

        Private Function Middle(histogram() As Integer) As Integer
            Dim target As Integer = samples \ 2
            Dim seen As Integer = 0
            For value As Integer = 0 To 255
                seen += histogram(value)
                If seen > target Then Return value
            Next
            Return 255
        End Function
    End Class

End Module
#End If
