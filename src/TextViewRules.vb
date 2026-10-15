#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Text

' The pure half of the text document's selection and scroll cue (124 UX-216): where a point on screen lands in the
' text, which part of a row a selection covers, what goes to the clipboard, and where the scroll thumb sits.
' No Graphics and no control in here - the one thing that needs pixels, how wide a prefix of a row is, comes in as a
' function - so every rule is a test (TextViewRulesTests). Main_Form.TextView.vb only measures and paints.
'
' Whole-file "#If Not NETFRAMEWORK": the x86 fallback's text view is the historical one (maintenance policy).

''' <summary>A place in a document's text: a LOGICAL line (a line of the file, not a screen row) and a column in it.
''' Logical, so a selection survives a resize or a zoom, which re-wraps the rows under it.</summary>
Friend Structure TextPosition
    Implements IComparable(Of TextPosition), IEquatable(Of TextPosition)

    Public ReadOnly Line As Integer
    Public ReadOnly Column As Integer

    Public Sub New(line As Integer, column As Integer)
        Me.Line = line
        Me.Column = column
    End Sub

    Public Function CompareTo(other As TextPosition) As Integer Implements IComparable(Of TextPosition).CompareTo
        If Line <> other.Line Then Return Line.CompareTo(other.Line)
        Return Column.CompareTo(other.Column)
    End Function

    Public Overloads Function Equals(other As TextPosition) As Boolean Implements IEquatable(Of TextPosition).Equals
        Return Line = other.Line AndAlso Column = other.Column
    End Function

    Public Overrides Function Equals(obj As Object) As Boolean
        Return TypeOf obj Is TextPosition AndAlso Equals(DirectCast(obj, TextPosition))
    End Function

    Public Overrides Function GetHashCode() As Integer
        Return Line * 397 Xor Column
    End Function
End Structure

''' <summary>One screen row of a wrapped logical line: which line, where in it the row starts, and its text.</summary>
Friend Structure TextRow
    Public ReadOnly Line As Integer
    Public ReadOnly Start As Integer
    Public ReadOnly Text As String

    Public Sub New(line As Integer, start As Integer, text As String)
        Me.Line = line
        Me.Start = start
        Me.Text = If(text, String.Empty)
    End Sub

    Public ReadOnly Property [End] As Integer
        Get
            Return Start + Text.Length
        End Get
    End Property
End Structure

''' <summary>The part of one row a selection covers, in columns of the ROW's own text.</summary>
Friend Structure TextRowSpan
    ''' <summary>False when the selection does not touch this row at all.</summary>
    Public HasSpan As Boolean
    Public FromColumn As Integer
    Public ToColumn As Integer
    ''' <summary>An empty line the selection runs across: it has no characters to colour, and a selected blank line
    ''' that shows nothing reads as a hole in the selection.</summary>
    Public IsBlankLine As Boolean
End Structure

''' <summary>Where the scroll thumb is, in pixels along a track.</summary>
Friend Structure TextScrollThumb
    ''' <summary>False when everything fits and there is nothing to scroll.</summary>
    Public Visible As Boolean
    Public Top As Integer
    Public Length As Integer
End Structure

''' <summary>A selection: the point where it was started and the point it was dragged to - in either order.</summary>
Friend NotInheritable Class TextSelection

    Public ReadOnly Anchor As TextPosition
    Public ReadOnly Focus As TextPosition

    Public Sub New(anchor As TextPosition, focus As TextPosition)
        Me.Anchor = anchor
        Me.Focus = focus
    End Sub

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Anchor.Equals(Focus)
        End Get
    End Property

    ''' <summary>The earlier of the two ends.</summary>
    Public ReadOnly Property StartPosition As TextPosition
        Get
            Return If(Anchor.CompareTo(Focus) <= 0, Anchor, Focus)
        End Get
    End Property

    ''' <summary>The later of the two ends.</summary>
    Public ReadOnly Property EndPosition As TextPosition
        Get
            Return If(Anchor.CompareTo(Focus) <= 0, Focus, Anchor)
        End Get
    End Property

    Public Function WithFocus(newFocus As TextPosition) As TextSelection
        Return New TextSelection(Anchor, newFocus)
    End Function
End Class

Friend NotInheritable Class TextViewRules

    Private Sub New()
    End Sub

    ''' <summary>A position pulled into the document: a line that exists, a column inside that line.</summary>
    Public Shared Function Clamp(lines As String(), position As TextPosition) As TextPosition
        If lines Is Nothing OrElse lines.Length = 0 Then Return New TextPosition(0, 0)
        Dim line As Integer = Math.Max(0, Math.Min(lines.Length - 1, position.Line))
        Dim length As Integer = If(lines(line), String.Empty).Length
        Return New TextPosition(line, Math.Max(0, Math.Min(length, position.Column)))
    End Function

    ''' <summary>Everything. Nothing for a document without lines - there is nothing to select.</summary>
    Public Shared Function SelectAll(lines As String()) As TextSelection
        If lines Is Nothing OrElse lines.Length = 0 Then Return Nothing
        Dim last As Integer = lines.Length - 1
        Return New TextSelection(New TextPosition(0, 0), New TextPosition(last, If(lines(last), String.Empty).Length))
    End Function

    ''' <summary>The selected text exactly as the file has it, lines joined by the platform's newline. A soft wrap
    ''' is never in it: the selection is in logical lines, and a row break is only where the window wrapped them.</summary>
    Public Shared Function Extract(lines As String(), selection As TextSelection) As String
        If lines Is Nothing OrElse lines.Length = 0 OrElse selection Is Nothing OrElse selection.IsEmpty Then Return String.Empty
        Dim first As TextPosition = Clamp(lines, selection.StartPosition)
        Dim last As TextPosition = Clamp(lines, selection.EndPosition)
        If first.Line = last.Line Then
            Return If(lines(first.Line), String.Empty).Substring(first.Column, Math.Max(0, last.Column - first.Column))
        End If
        Dim builder As New StringBuilder()
        builder.Append(If(lines(first.Line), String.Empty).Substring(first.Column))
        For index As Integer = first.Line + 1 To last.Line - 1
            builder.Append(Environment.NewLine).Append(If(lines(index), String.Empty))
        Next
        builder.Append(Environment.NewLine).Append(If(lines(last.Line), String.Empty).Substring(0, last.Column))
        Return builder.ToString()
    End Function

    ''' <summary>How many characters <see cref="Extract"/> would return, without building the string - a select-all
    ''' of a multi-megabyte file is asked this just to put a number on the status line.</summary>
    Public Shared Function CharacterCount(lines As String(), selection As TextSelection) As Long
        If lines Is Nothing OrElse lines.Length = 0 OrElse selection Is Nothing OrElse selection.IsEmpty Then Return 0
        Dim first As TextPosition = Clamp(lines, selection.StartPosition)
        Dim last As TextPosition = Clamp(lines, selection.EndPosition)
        If first.Line = last.Line Then Return Math.Max(0, last.Column - first.Column)
        Dim count As Long = If(lines(first.Line), String.Empty).Length - first.Column
        For index As Integer = first.Line + 1 To last.Line - 1
            count += Environment.NewLine.Length + If(lines(index), String.Empty).Length
        Next
        count += Environment.NewLine.Length + last.Column
        Return count
    End Function

    ''' <summary>The part of <paramref name="row"/> the selection covers, if any.</summary>
    Public Shared Function SpanOnRow(selection As TextSelection, row As TextRow) As TextRowSpan
        Dim none As New TextRowSpan()
        If selection Is Nothing OrElse selection.IsEmpty Then Return none
        Dim first As TextPosition = selection.StartPosition
        Dim last As TextPosition = selection.EndPosition
        If row.Line < first.Line OrElse row.Line > last.Line Then Return none

        Dim low As Integer = If(row.Line = first.Line, first.Column, 0)
        Dim high As Integer = If(row.Line = last.Line, last.Column, Integer.MaxValue)
        Dim fromColumn As Integer = Math.Max(low, row.Start)
        Dim toColumn As Integer = Math.Min(high, row.End)

        Dim span As New TextRowSpan()
        If toColumn > fromColumn Then
            span.HasSpan = True
            span.FromColumn = fromColumn - row.Start
            span.ToColumn = toColumn - row.Start
        ElseIf row.Text.Length = 0 AndAlso first.Line <= row.Line AndAlso row.Line < last.Line Then
            span.HasSpan = True
            span.IsBlankLine = True
        End If
        Return span
    End Function

    ''' <summary>
    ''' The boundary between two characters nearest to <paramref name="x"/> (pixels from the row's text origin):
    ''' the caret position a click or a drag means. <paramref name="widthOfPrefix"/> answers how wide the first
    ''' <c>n</c> characters are; it is expected to be non-decreasing. A pair of surrogates is never cut in two.
    ''' </summary>
    Public Shared Function ColumnAtX(rowText As String, x As Integer, widthOfPrefix As Func(Of Integer, Integer)) As Integer
        Dim text As String = If(rowText, String.Empty)
        If text.Length = 0 OrElse x <= 0 OrElse widthOfPrefix Is Nothing Then Return 0
        If x >= widthOfPrefix(text.Length) Then Return text.Length

        ' The smallest n whose prefix is wider than x: the boundary is n - 1 or n, whichever is nearer.
        Dim low As Integer = 1
        Dim high As Integer = text.Length
        While low < high
            Dim middle As Integer = (low + high) \ 2
            If widthOfPrefix(middle) > x Then
                high = middle
            Else
                low = middle + 1
            End If
        End While
        Dim column As Integer = low
        If x - widthOfPrefix(column - 1) <= widthOfPrefix(column) - x Then column -= 1

        If column > 0 AndAlso column < text.Length AndAlso Char.IsLowSurrogate(text(column)) AndAlso Char.IsHighSurrogate(text(column - 1)) Then
            column -= 1
        End If
        Return column
    End Function

    ''' <summary>The row under a vertical position, pulled to the rows there are. Above the first visible row is the
    ''' first visible row's predecessor (so a drag upward reaches the line scrolled out of sight); the caller scrolls.</summary>
    Public Shared Function RowAt(y As Integer, top As Integer, rowHeight As Integer, firstVisibleRow As Integer, rowCount As Integer) As Integer
        If rowCount <= 0 OrElse rowHeight <= 0 Then Return 0
        Dim offset As Integer = CInt(Math.Floor((y - top) / CDbl(rowHeight)))
        Return Math.Max(0, Math.Min(rowCount - 1, firstVisibleRow + offset))
    End Function

    ''' <summary>
    ''' Where the thumb of a vertical scroll cue goes for <paramref name="trackLength"/> pixels of track. The
    ''' thumb's length is the share of the document in view, never shorter than <paramref name="minimumThumb"/> so a
    ''' long file still has something to see; nothing at all when everything fits.
    ''' </summary>
    Public Shared Function ScrollThumb(totalRows As Integer, visibleRows As Integer, firstVisibleRow As Integer,
                                       trackLength As Integer, minimumThumb As Integer) As TextScrollThumb
        Dim thumb As New TextScrollThumb()
        If totalRows <= 0 OrElse visibleRows <= 0 OrElse totalRows <= visibleRows OrElse trackLength <= 0 Then Return thumb
        Dim length As Integer = CInt(Math.Round(trackLength * (visibleRows / CDbl(totalRows))))
        length = Math.Max(Math.Min(minimumThumb, trackLength), Math.Min(trackLength, length))
        Dim travel As Integer = trackLength - length
        Dim maxOffset As Integer = totalRows - visibleRows
        Dim offset As Integer = Math.Max(0, Math.Min(maxOffset, firstVisibleRow))
        thumb.Visible = True
        thumb.Length = length
        thumb.Top = CInt(Math.Round(travel * (offset / CDbl(maxOffset))))
        Return thumb
    End Function

End Class
#End If
