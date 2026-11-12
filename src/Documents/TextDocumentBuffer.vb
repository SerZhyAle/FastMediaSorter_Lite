#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Text

' Specification 277 section 7.2 (decision D04): what the file's lines looked like when it was opened.
' The editing surface holds plain text with one kind of line break, so the buffer keeps each line's own
' terminator beside it and puts them back when the text is saved: an untouched line keeps its exact
' terminator, a changed line keeps the one of the line it replaced, a new line takes the dominant one.
' A line never gains or loses the final newline by itself: the last piece has no terminator, and an empty
' last piece is what a final newline looks like, so the text as edited decides. No WinForms reference;
' undo and redo belong to the editing surface (decision D01).
Public NotInheritable Class TextDocumentBuffer

    ' Past this many changed lines the middle of the document is matched by position: the alignment costs
    ' the square of this number in memory, and a rewrite that large has no untouched lines to speak of.
    Private Const MaxEditDistance As Integer = 1024

    Private ReadOnly _lines As String()
    Private ReadOnly _terminators As String()
    Private ReadOnly _dominant As String
    Private ReadOnly _lineEnding As LineEndingKind

    ''' <param name="text">The decoded text with its terminators, without a byte-order mark.</param>
    Public Sub New(text As String)
        Dim source As String = If(text, "")
        Dim lines As New List(Of String)()
        Dim terminators As New List(Of String)()
        Dim start As Integer = 0
        Dim i As Integer = 0
        While i < source.Length
            Dim c As Char = source(i)
            If c = ControlChars.Cr OrElse c = ControlChars.Lf Then
                Dim isPair As Boolean = c = ControlChars.Cr AndAlso i + 1 < source.Length AndAlso source(i + 1) = ControlChars.Lf
                lines.Add(source.Substring(start, i - start))
                terminators.Add(If(isPair, ControlChars.CrLf, c.ToString()))
                If isPair Then i += 1
                start = i + 1
            End If
            i += 1
        End While
        lines.Add(source.Substring(start))
        terminators.Add("")
        _lines = lines.ToArray()
        _terminators = terminators.ToArray()
        _dominant = TextExcerpt.DominantTerminator(source)
        _lineEnding = KindOf(_terminators)
    End Sub

    ''' <summary>The pieces of the text between terminators; the last one is empty when the text ends in a line break.</summary>
    Public ReadOnly Property LineCount As Integer
        Get
            Return _lines.Length
        End Get
    End Property

    Public ReadOnly Property HasTrailingNewline As Boolean
        Get
            Return _lines.Length > 1 AndAlso _lines(_lines.Length - 1).Length = 0
        End Get
    End Property

    ''' <summary>The terminator a new line takes: the one used most, CRLF on a tie or when the file has none.</summary>
    Public ReadOnly Property DominantTerminator As String
        Get
            Return _dominant
        End Get
    End Property

    Public ReadOnly Property LineEnding As LineEndingKind
        Get
            Return _lineEnding
        End Get
    End Property

    Public Function LineAt(index As Integer) As String
        Return _lines(index)
    End Function

    Public Function TerminatorAt(index As Integer) As String
        Return _terminators(index)
    End Function

    ''' <summary>The text for the editing surface: the same lines, one line break between them.</summary>
    Public Function EditorText() As String
        Return String.Join(ControlChars.Lf, _lines)
    End Function

    ''' <summary>True when the surface still holds the lines the file was opened with, whatever its line break.</summary>
    Public Function IsUnchanged(editedText As String) As Boolean
        Dim edited As String() = TextExcerpt.SplitRawLines(editedText, False)
        If edited.Length <> _lines.Length Then Return False
        For index As Integer = 0 To edited.Length - 1
            If Not String.Equals(edited(index), _lines(index), StringComparison.Ordinal) Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' The text to save for what the surface holds, with the file's own terminators back on its lines.
    ''' <paramref name="normalizedTerminator"/> is the explicit Normalize Line Endings command: every line takes it.
    ''' </summary>
    Public Function ComposeText(editedText As String, Optional normalizedTerminator As String = Nothing) As String
        Dim edited As String() = TextExcerpt.SplitRawLines(editedText, False)
        Dim builder As New StringBuilder(If(editedText, "").Length + edited.Length)
        Dim inherited As Integer() = If(normalizedTerminator Is Nothing, AlignLines(_lines, edited), Nothing)
        For index As Integer = 0 To edited.Length - 1
            builder.Append(edited(index))
            If index = edited.Length - 1 Then Exit For
            If normalizedTerminator IsNot Nothing Then
                builder.Append(normalizedTerminator)
            Else
                Dim terminator As String = If(inherited(index) >= 0, _terminators(inherited(index)), "")
                builder.Append(If(terminator.Length > 0, terminator, _dominant))
            End If
        Next
        Return builder.ToString()
    End Function

    Private Shared Function KindOf(terminators As String()) As LineEndingKind
        Dim found As LineEndingKind = LineEndingKind.None
        For Each terminator As String In terminators
            If terminator.Length = 0 Then Continue For
            Dim kind As LineEndingKind = If(terminator = ControlChars.CrLf, LineEndingKind.Crlf,
                If(terminator = ControlChars.Lf, LineEndingKind.Lf, LineEndingKind.Cr))
            If found = LineEndingKind.None Then
                found = kind
            ElseIf found <> kind Then
                Return LineEndingKind.Mixed
            End If
        Next
        Return found
    End Function

    ''' <summary>
    ''' For each edited line the index of the old line whose terminator it inherits, or -1 for a new line: equal
    ''' lines are matched in order (the common start and end first, a shortest edit script between them), and
    ''' inside a changed stretch the lines are paired by position, so an edited line keeps its terminator.
    ''' </summary>
    Friend Shared Function AlignLines(oldLines As String(), newLines As String()) As Integer()
        Dim map(newLines.Length - 1) As Integer
        Dim prefix As Integer = 0
        Dim shortest As Integer = Math.Min(oldLines.Length, newLines.Length)
        While prefix < shortest AndAlso String.Equals(oldLines(prefix), newLines(prefix), StringComparison.Ordinal)
            map(prefix) = prefix
            prefix += 1
        End While
        Dim suffix As Integer = 0
        While suffix < shortest - prefix AndAlso
              String.Equals(oldLines(oldLines.Length - 1 - suffix), newLines(newLines.Length - 1 - suffix), StringComparison.Ordinal)
            map(newLines.Length - 1 - suffix) = oldLines.Length - 1 - suffix
            suffix += 1
        End While
        Dim oldCount As Integer = oldLines.Length - prefix - suffix
        Dim newCount As Integer = newLines.Length - prefix - suffix
        If newCount = 0 Then Return map
        For index As Integer = 0 To newCount - 1
            map(prefix + index) = -1
        Next
        If oldCount = 0 Then Return map

        Dim ids As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        Dim oldIds As Integer() = InternLines(oldLines, prefix, oldCount, ids)
        Dim newIds As Integer() = InternLines(newLines, prefix, newCount, ids)
        Dim matches As Integer() = ShortestEditMatches(oldIds, newIds)
        If matches Is Nothing Then
            PairByPosition(map, prefix, 0, oldCount, 0, newCount)
            Return map
        End If

        ' Between two matched lines, pair what was replaced by position; the rest of the new lines stay new.
        Dim lastOld As Integer = 0
        Dim lastNew As Integer = 0
        For index As Integer = 0 To newCount - 1
            If matches(index) < 0 Then Continue For
            PairByPosition(map, prefix, lastOld, matches(index), lastNew, index)
            map(prefix + index) = prefix + matches(index)
            lastOld = matches(index) + 1
            lastNew = index + 1
        Next
        PairByPosition(map, prefix, lastOld, oldCount, lastNew, newCount)
        Return map
    End Function

    Private Shared Sub PairByPosition(map As Integer(), prefix As Integer, oldFrom As Integer, oldTo As Integer, newFrom As Integer, newTo As Integer)
        Dim pairs As Integer = Math.Min(oldTo - oldFrom, newTo - newFrom)
        For index As Integer = 0 To pairs - 1
            map(prefix + newFrom + index) = prefix + oldFrom + index
        Next
    End Sub

    Private Shared Function InternLines(lines As String(), first As Integer, count As Integer, ids As Dictionary(Of String, Integer)) As Integer()
        Dim result(count - 1) As Integer
        For index As Integer = 0 To count - 1
            Dim line As String = lines(first + index)
            Dim id As Integer
            If Not ids.TryGetValue(line, id) Then
                id = ids.Count
                ids(line) = id
            End If
            result(index) = id
        Next
        Return result
    End Function

    ''' <summary>
    ''' Myers' greedy shortest edit script over line ids. For each new line the old line it equals, or -1;
    ''' Nothing when the two differ by more than <see cref="MaxEditDistance"/> lines.
    ''' </summary>
    Private Shared Function ShortestEditMatches(a As Integer(), b As Integer()) As Integer()
        Dim n As Integer = a.Length
        Dim m As Integer = b.Length
        Dim limit As Integer = Math.Min(n + m, MaxEditDistance)
        Dim center As Integer = limit + 1
        Dim v(2 * limit + 2) As Integer
        ' trace(d) holds v before round d, for the diagonals -(d + 1) .. d + 1.
        Dim trace As New List(Of Integer())()
        Dim found As Integer = -1
        For d As Integer = 0 To limit
            Dim snapshot(2 * d + 2) As Integer
            Array.Copy(v, center - d - 1, snapshot, 0, snapshot.Length)
            trace.Add(snapshot)
            For k As Integer = -d To d Step 2
                Dim x As Integer = If(k = -d OrElse (k <> d AndAlso v(center + k - 1) < v(center + k + 1)),
                    v(center + k + 1), v(center + k - 1) + 1)
                Dim y As Integer = x - k
                While x < n AndAlso y < m AndAlso a(x) = b(y)
                    x += 1
                    y += 1
                End While
                v(center + k) = x
                If x >= n AndAlso y >= m Then
                    found = d
                    Exit For
                End If
            Next
            If found >= 0 Then Exit For
        Next
        If found < 0 Then Return Nothing

        Dim matches(m - 1) As Integer
        For index As Integer = 0 To m - 1
            matches(index) = -1
        Next
        Dim cx As Integer = n
        Dim cy As Integer = m
        For d As Integer = found To 0 Step -1
            Dim before As Integer() = trace(d)
            Dim zero As Integer = d + 1
            Dim k As Integer = cx - cy
            Dim previousK As Integer = If(k = -d OrElse (k <> d AndAlso before(zero + k - 1) < before(zero + k + 1)), k + 1, k - 1)
            Dim previousX As Integer = before(zero + previousK)
            Dim previousY As Integer = previousX - previousK
            While cx > previousX AndAlso cy > previousY
                cx -= 1
                cy -= 1
                If cy >= 0 Then matches(cy) = cx
            End While
            cx = previousX
            cy = previousY
        Next
        Return matches
    End Function

End Class
#End If
