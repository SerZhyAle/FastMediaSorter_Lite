Option Strict On

Imports System.Drawing

''' <summary>
''' S7: cuts a recognizer line that carries two separate texts, and puts the pieces back
''' into reading order. Runs on the engine's output, BEFORE the clustering
''' (<see cref="OcrBlockBuilder"/>) - see SPECIFICATION_OCR_OVERLAY_ACCURACY.md section 18.
'''
''' The defect it repairs is not ours. At PSM 3 Tesseract's layout analysis walks across a
''' picture and hands back a phrase from the left and a phrase from the right as ONE text
''' line, so the overlay draws one plate across the whole frame - artwork covered by a plate
''' that should not exist - and the translator receives the two texts run into one sentence,
''' which carries the damage into every language.
'''
''' Nothing downstream can recover from it, and each rule that looks like it should fails for
''' its own reason. Measured on this repository's own corpus (section 18.2):
'''   - the column test (<see cref="OcrBlockBuilder.SharesColumn"/>) sees a GENUINE overlap,
'''     because the stitched box honestly spans both regions;
'''   - the dissolve rule does not fire, because the bar is wide but SHORT - 1574x189 px on a
'''     2048x1400 scene is 0.10 of it, against a 0.52 bound;
'''   - the pitch and type-size rules compare a line with its neighbours, and by then the two
'''     texts are not neighbours - they are one line.
''' So the repair belongs here, on the recognizer's output, and nowhere later.
'''
''' Both builds: this is a defect fix, not a mainline refinement.
''' </summary>
Public Module OcrLineSplitter

    ''' <summary>
    ''' How many median WORD heights of blank may stand between two consecutive words of one
    ''' recognized line before they are two texts rather than one line.
    '''
    ''' 3.5, and the number is shared with the sibling product doc-html-translate rather than
    ''' coincidentally equal to it: both run the same engine family (Tesseract 5) at the same
    ''' page-segmentation mode (PSM 3), which is the condition for carrying the constant over
    ''' at all. It was re-measured here anyway, because a constant that is only inherited is
    ''' not evidence - SPECIFICATION_OCR_OVERLAY_ACCURACY.md section 18.2 has the corpus:
    '''   - widest gap inside a line that really IS one line: 2.06x (a chapter rule set with a
    '''     wide internal space);
    '''   - narrowest cross-region stitch: 5.29x (two balloons either side of a figure);
    '''   - geometric middle of that bracket: 3.30. 3.5 sits 1.70x above the widest genuine
    '''     gap and 1.51x below the narrowest stitch, and one number across the two products
    '''     is worth the 0.2.
    '''
    ''' The band between roughly 2.1x and 5.3x is recorded as UNDECIDED, not as safe: a title
    ''' set with a very wide space and two captions standing side by side produce the same
    ''' geometry, and no ratio separates them. Telling those apart needs evidence from the
    ''' pixels BETWEEN the two words - an outline, a change of ground - which this stage does
    ''' not look at and must not pretend to have.
    ''' </summary>
    Public Const MaxWordGapRatio As Double = 3.5

    ''' <summary>
    ''' Cuts every stitched line in <paramref name="lines"/> and, if anything was cut, puts
    ''' the result back into reading order.
    '''
    ''' Cutting ALONE makes things worse, which is why the two halves are one call: the runs
    ''' then interleave left, right, left, right down the page, and the clustering is a single
    ''' pass that closes its open plate on the first line not belonging to it. So a cut is
    ''' always followed by <see cref="RegroupIntoColumns"/>.
    '''
    ''' When nothing was cut the input list is returned BY IDENTITY and nothing is reordered:
    ''' a page with no stitch on it cannot move at all, which is what keeps this stage from
    ''' being able to regress a scene it has no business touching.
    ''' </summary>
    ''' <param name="regrouped">True when the returned order is this stage's column order
    ''' rather than the engine's. The clustering must then NOT re-sort - see
    ''' <see cref="OcrBlockBuilder.BuildBlocks"/>'s preserveOrder.</param>
    Public Function SplitStitchedLines(lines As List(Of OcrLine), ByRef regrouped As Boolean) As List(Of OcrLine)
        regrouped = False
        If lines Is Nothing OrElse lines.Count = 0 Then Return lines

        Dim anyCut As Boolean = False
        Dim result As New List(Of OcrLine)(lines.Count)
        For Each line As OcrLine In lines
            Dim runs As List(Of OcrLine) = SplitLine(line)
            If runs.Count > 1 Then anyCut = True
            result.AddRange(runs)
        Next

        If Not anyCut Then Return lines

        regrouped = True
        Return RegroupIntoColumns(result)
    End Function

    ''' <summary>
    ''' One line in, one or more out. Returns a single-element list holding the SAME instance
    ''' when the line is not cut - the common path must not rebuild anything, or every page
    ''' would pay for a defect that appears on a few.
    '''
    ''' Three details carry the rule, and each was a defect in the sibling product before it
    ''' was one:
    ''' <list type="bullet">
    ''' <item>the gap is measured BETWEEN the two boxes
    ''' (<c>max(next.Left - prev.Right, prev.Left - next.Right)</c>), never left-to-right: a
    ''' right-to-left line would otherwise yield a negative gap on every pair and silently
    ''' never cut;</item>
    ''' <item>it is weighed against the median WORD height, not the line box. The line box is
    ''' the union of its words, so one tall artefact - a balloon outline read as "|" - sets it
    ''' for the whole line (the same reason section 16.2 gives for the type-size rule);</item>
    ''' <item>each run gets a box that is the union of ITS OWN words and its own mean
    ''' confidence. Handing both halves the stitch's box back would leave the clustering
    ''' exactly where it started.</item>
    ''' </list>
    ''' Overlapping boxes give a negative gap and never cut.
    ''' </summary>
    Public Function SplitLine(line As OcrLine) As List(Of OcrLine)
        Dim unchanged As New List(Of OcrLine) From {line}
        If line Is Nothing Then Return unchanged

        Dim words As List(Of OcrWord) = line.Words
        If words Is Nothing OrElse words.Count < 2 Then Return unchanged

        Dim median As Integer = MedianWordHeight(line)
        ' No word geometry - the engine gave none, or every box is degenerate. The rule then
        ' has nothing to weigh the gap against and must not become a guess.
        If median <= 0 Then Return unchanged

        Dim limit As Double = median * MaxWordGapRatio
        Dim cutAfter As New List(Of Integer)
        For i As Integer = 1 To words.Count - 1
            If GapBetween(words(i - 1).Box, words(i).Box) > limit Then cutAfter.Add(i - 1)
        Next
        If cutAfter.Count = 0 Then Return unchanged

        Dim runs As New List(Of OcrLine)
        Dim start As Integer = 0
        For Each cut As Integer In cutAfter
            runs.Add(MakeRun(words, start, cut))
            start = cut + 1
        Next
        runs.Add(MakeRun(words, start, words.Count - 1))
        Return runs
    End Function

    ''' <summary>
    ''' Blank between two word boxes, in pixels. Negative when they overlap - which is why the
    ''' comparison above is a strict "greater than" against a positive limit.
    ''' </summary>
    Public Function GapBetween(previous As Rectangle, nxt As Rectangle) As Integer
        Return Math.Max(nxt.Left - previous.Right, previous.Left - nxt.Right)
    End Function

    ''' <summary>
    ''' Median height of the line's own words, or 0 when it has no usable word geometry.
    ''' Public because it is the quantity section 16.2 wants the type-size rule measured on
    ''' too, and a second implementation of it would be free to drift from this one.
    ''' </summary>
    Public Function MedianWordHeight(line As OcrLine) As Integer
        If line Is Nothing OrElse line.Words Is Nothing OrElse line.Words.Count = 0 Then Return 0
        Dim heights As List(Of Integer) = line.Words.
            Select(Function(w) w.Box.Height).
            Where(Function(h) h > 0).
            OrderBy(Function(h) h).ToList()
        If heights.Count = 0 Then Return 0
        Return heights(heights.Count \ 2)
    End Function

    ''' <summary>
    ''' Reading order for a page whose lines have just been cut: columns left to right, each
    ''' column top to bottom. Two lines belong to one column when their x-ranges overlap by
    ''' the clustering's OWN test (<see cref="OcrBlockBuilder.SharesColumn"/>) - a second
    ''' overlap test would be free to disagree with the one the plates are actually built by.
    '''
    ''' Two traps, both of which are invisible until a corpus is run:
    ''' <list type="bullet">
    ''' <item><b>Scope is the page, not the recognizer paragraph.</b> Tesseract invents
    ''' paragraph boundaries mid-column, so regrouping per paragraph still hands the
    ''' clustering an interleaved page.</item>
    ''' <item><b>A line the confidence floor will drop must not form a column.</b> The engine
    ''' also returns empty full-page "lines" - measured here at 1800x1200 px on an 1800x1200
    ''' scene, and 258x500 over a figure. Such a line reaches no plate, but as a column it
    ''' overlaps both real ones and chains them into one, which sorts the page straight back
    ''' into the interleaving. So columns are formed only from lines that clear the floor -
    ''' the SAME predicate the engine drops them by, <see cref="OcrTextFilter.LineRejection"/>
    ''' - and the rest are parked at the end, where only the discard record reads them.</item>
    ''' </list>
    ''' </summary>
    Public Function RegroupIntoColumns(lines As List(Of OcrLine)) As List(Of OcrLine)
        If lines Is Nothing OrElse lines.Count < 2 Then Return lines

        Dim eligible As New List(Of OcrLine)
        Dim parked As New List(Of OcrLine)
        For Each l As OcrLine In lines
            If OcrTextFilter.LineRejection(l.Text, l.Confidence).Length = 0 Then
                eligible.Add(l)
            Else
                parked.Add(l)
            End If
        Next
        If eligible.Count < 2 Then Return lines

        ' Union-find over "shares a column". Done as a closure rather than pairwise greedy
        ' assignment because column membership is transitive through a middle line: A and C
        ' may not overlap while both overlap B, and they are still one column.
        Dim parent(eligible.Count - 1) As Integer
        For i As Integer = 0 To eligible.Count - 1
            parent(i) = i
        Next
        For i As Integer = 0 To eligible.Count - 2
            For j As Integer = i + 1 To eligible.Count - 1
                If OcrBlockBuilder.SharesColumn(eligible(i).Box, eligible(j).Box) Then
                    Union(parent, i, j)
                End If
            Next
        Next

        Dim columns As New Dictionary(Of Integer, List(Of OcrLine))
        Dim order As New List(Of Integer)
        For i As Integer = 0 To eligible.Count - 1
            Dim root As Integer = Find(parent, i)
            If Not columns.ContainsKey(root) Then
                columns(root) = New List(Of OcrLine)
                order.Add(root)
            End If
            columns(root).Add(eligible(i))
        Next

        Dim result As New List(Of OcrLine)(lines.Count)
        Dim sortedColumns As List(Of Integer) = order.
            OrderBy(Function(root) columns(root).Min(Function(l) l.Box.Left)).
            ThenBy(Function(root) columns(root).Min(Function(l) l.Box.Top)).ToList()
        For Each root As Integer In sortedColumns
            result.AddRange(columns(root).
                OrderBy(Function(l) l.Box.Top).
                ThenBy(Function(l) l.Box.Left))
        Next
        result.AddRange(parked)
        Return result
    End Function

    Private Function MakeRun(words As List(Of OcrWord), first As Integer, last As Integer) As OcrLine
        Dim own As New List(Of OcrWord)(last - first + 1)
        Dim box As Rectangle = words(first).Box
        Dim confSum As Double = 0.0
        For i As Integer = first To last
            own.Add(words(i))
            box = Rectangle.Union(box, words(i).Box)
            confSum += words(i).Confidence
        Next

        Return New OcrLine With {
            .Words = own,
            .Box = box,
            .Confidence = CSng(confSum / own.Count),
            .Text = String.Join(" ", own.Select(Function(w) w.Text)).Trim()
        }
    End Function

    Private Function Find(parent As Integer(), i As Integer) As Integer
        Dim root As Integer = i
        While parent(root) <> root
            root = parent(root)
        End While
        ' Path compression: the loop above runs inside an O(n^2) pairwise scan.
        Dim node As Integer = i
        While parent(node) <> root
            Dim next_ As Integer = parent(node)
            parent(node) = root
            node = next_
        End While
        Return root
    End Function

    Private Sub Union(parent As Integer(), a As Integer, b As Integer)
        Dim ra As Integer = Find(parent, a)
        Dim rb As Integer = Find(parent, b)
        If ra <> rb Then parent(rb) = ra
    End Sub

End Module
