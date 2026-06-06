Option Strict On

Imports System.Drawing

''' <summary>
''' Groups OCR text lines into translation blocks. Comics/manga rarely produce
''' clean engine-level blocks, so we start from lines (the reliable unit) and
''' merge vertically-adjacent, horizontally-overlapping lines into a block.
''' Each block's source text is the joined line text, sent to the translator as
''' one unit so the model has sentence context.
''' </summary>
Public Module OcrBlockBuilder

    Public Function BuildBlocks(lines As List(Of OcrLine)) As List(Of OcrBlock)
        Dim blocks As New List(Of OcrBlock)
        If lines Is Nothing OrElse lines.Count = 0 Then Return blocks

        ' Top-to-bottom, then left-to-right.
        Dim ordered As List(Of OcrLine) = lines.
            OrderBy(Function(l) l.Box.Top).
            ThenBy(Function(l) l.Box.Left).ToList()

        Dim medianHeight As Integer = MedianLineHeight(ordered)
        Dim gapThreshold As Integer = Math.Max(6, CInt(medianHeight * 0.9))

        Dim current As New List(Of OcrLine)
        For Each line As OcrLine In ordered
            If current.Count = 0 Then
                current.Add(line)
            Else
                Dim prev As OcrLine = current(current.Count - 1)
                Dim verticalGap As Integer = line.Box.Top - prev.Box.Bottom
                Dim overlaps As Boolean = HorizontalOverlap(prev.Box, line.Box)

                If verticalGap <= gapThreshold AndAlso overlaps Then
                    current.Add(line)
                Else
                    blocks.Add(MakeBlock(current))
                    current = New List(Of OcrLine) From {line}
                End If
            End If
        Next
        If current.Count > 0 Then blocks.Add(MakeBlock(current))

        ' Drop isolated tiny blocks (single stray short word from texture noise);
        ' real speech blocks have several characters or multiple lines.
        Dim kept As New List(Of OcrBlock)
        For Each b As OcrBlock In blocks
            If BlockHasEnoughText(b) Then kept.Add(b)
        Next
        Return kept
    End Function

    Private Function BlockHasEnoughText(b As OcrBlock) As Boolean
        Dim useful As Integer = 0
        For Each ch As Char In b.SourceText
            If Char.IsLetterOrDigit(ch) Then useful += 1
        Next
        If b.Lines.Count >= 2 Then Return useful >= 4
        Return useful >= 5
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

    Private Function HorizontalOverlap(a As Rectangle, b As Rectangle) As Boolean
        Dim left As Integer = Math.Max(a.Left, b.Left)
        Dim right As Integer = Math.Min(a.Right, b.Right)
        Dim overlap As Integer = right - left
        Dim minWidth As Integer = Math.Max(1, Math.Min(a.Width, b.Width))
        ' Require at least ~25% overlap of the narrower line to count as the same column.
        Return overlap > 0 AndAlso (overlap >= minWidth * 0.25)
    End Function

    Private Function MedianLineHeight(lines As List(Of OcrLine)) As Integer
        Dim heights As List(Of Integer) = lines.Select(Function(l) l.Box.Height).OrderBy(Function(h) h).ToList()
        If heights.Count = 0 Then Return 12
        Return heights(heights.Count \ 2)
    End Function

End Module
