#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Text
Imports System.Text.RegularExpressions

' Specification 277 sections 4.2 and 5.1: the pure half of the full text reader - how a decoded
' window becomes lines, what is drawn for them, the reader palette and the in-memory search.
' No WinForms reference; Main_Form.TextReader.vb only measures and paints what this returns.

''' <summary>One search hit: a logical line of the window and a column range in its expanded text.</summary>
Public Structure TextMatch
    Public ReadOnly Line As Integer
    Public ReadOnly Column As Integer
    Public ReadOnly Length As Integer

    Public Sub New(line As Integer, column As Integer, length As Integer)
        Me.Line = line
        Me.Column = column
        Me.Length = length
    End Sub
End Structure

''' <summary>The colours of one reader theme: the surface, the text and the dimmer ink of the gutter and the
''' status line. The one table of reader colours (section 4.2, `TextTheme`).</summary>
Public Structure TextReaderPalette
    Public ReadOnly Back As Color
    Public ReadOnly Ink As Color
    Public ReadOnly Muted As Color

    Public Sub New(back As Color, ink As Color, muted As Color)
        Me.Back = back
        Me.Ink = ink
        Me.Muted = muted
    End Sub
End Structure

''' <summary>The lines of one window: what a copy takes (tabs expanded) and what is drawn (the same text with
''' the whitespace markers in place). Both arrays have the same length line by line and character by
''' character, so a column means the same place in either.</summary>
Public NotInheritable Class TextReaderLines
    Public Property CopyLines As String()
    Public Property DisplayLines As String()
    ''' <summary>Line terminators inside the window: what the next window's first line number adds.</summary>
    Public Property Terminators As Integer
End Class

Public Module TextReaderLayout

    Public Const SpaceMarker As Char = ChrW(&HB7)
    Public Const TabMarker As Char = ChrW(&H2192)
    Public Const LineEndMarker As String = ChrW(&HB6)
    ''' <summary>A line longer than this is cut into rows even without word wrap: one drawn string stays bounded.</summary>
    Public Const MaxUnwrappedRowChars As Integer = 4096
    ''' <summary>The search stops counting here; the count says so.</summary>
    Public Const MaxMatches As Integer = 100000
    Public ReadOnly RegexTimeout As TimeSpan = TimeSpan.FromSeconds(1)

    ''' <summary>
    ''' Splits a decoded window on CR, LF and CRLF, expands tabs to <paramref name="tabWidth"/> columns and,
    ''' when <paramref name="markWhitespace"/> is on, draws spaces as a middle dot and a tab as an arrow
    ''' followed by its padding. <paramref name="dropTrailingEmpty"/> drops the empty piece after a final line
    ''' break - set for every window but the last, whose next window starts on that line.
    ''' </summary>
    Public Function BuildLines(text As String, tabWidth As Integer, markWhitespace As Boolean,
                               dropTrailingEmpty As Boolean) As TextReaderLines
        Dim width As Integer = Math.Max(1, tabWidth)
        Dim copyLines As New List(Of String)()
        Dim displayLines As New List(Of String)()
        Dim terminators As Integer = 0
        Dim source As String = If(text, "")
        Dim start As Integer = 0
        Dim i As Integer = 0
        While i <= source.Length
            Dim atEnd As Boolean = i = source.Length
            Dim c As Char = If(atEnd, ControlChars.NullChar, source(i))
            If atEnd OrElse c = ControlChars.Cr OrElse c = ControlChars.Lf Then
                Dim raw As String = source.Substring(start, i - start)
                Dim copy As String = Nothing
                Dim display As String = Nothing
                ExpandLine(raw, width, markWhitespace, copy, display)
                copyLines.Add(copy)
                displayLines.Add(display)
                If atEnd Then Exit While
                terminators += 1
                If c = ControlChars.Cr AndAlso i + 1 < source.Length AndAlso source(i + 1) = ControlChars.Lf Then i += 1
                start = i + 1
            End If
            i += 1
        End While
        If dropTrailingEmpty AndAlso copyLines.Count > 1 AndAlso copyLines(copyLines.Count - 1).Length = 0 Then
            copyLines.RemoveAt(copyLines.Count - 1)
            displayLines.RemoveAt(displayLines.Count - 1)
        End If
        Return New TextReaderLines With {
            .CopyLines = copyLines.ToArray(),
            .DisplayLines = displayLines.ToArray(),
            .Terminators = terminators
        }
    End Function

    ''' <summary>One line: tabs to the next multiple of the tab width. The copy keeps spaces; the drawn text
    ''' carries the markers in the same columns.</summary>
    Public Sub ExpandLine(raw As String, tabWidth As Integer, markWhitespace As Boolean,
                          ByRef copy As String, ByRef display As String)
        If raw.IndexOf(ControlChars.Tab) < 0 Then
            copy = raw
            display = If(markWhitespace, raw.Replace(" "c, SpaceMarker), raw)
            Return
        End If
        Dim copyText As New StringBuilder(raw.Length + 16)
        Dim displayText As New StringBuilder(raw.Length + 16)
        For Each c As Char In raw
            If c = ControlChars.Tab Then
                Dim pad As Integer = tabWidth - (copyText.Length Mod tabWidth)
                copyText.Append(" "c, pad)
                If markWhitespace Then
                    displayText.Append(TabMarker)
                    If pad > 1 Then displayText.Append(" "c, pad - 1)
                Else
                    displayText.Append(" "c, pad)
                End If
            Else
                copyText.Append(c)
                displayText.Append(If(markWhitespace AndAlso c = " "c, SpaceMarker, c))
            End If
        Next
        copy = copyText.ToString()
        display = displayText.ToString()
    End Sub

    ''' <summary>The reader palette of a stored `TextTheme`; "system" follows the viewer's own scheme, which
    ''' the caller passes as its surface colour and ink.</summary>
    Public Function Palette(theme As String, systemBack As Color, systemInk As Color) As TextReaderPalette
        Select Case theme
            Case "light"
                Return New TextReaderPalette(Color.FromArgb(250, 250, 250), Color.FromArgb(32, 32, 32), Color.FromArgb(118, 118, 118))
            Case "dark"
                Return New TextReaderPalette(Color.FromArgb(30, 30, 30), Color.FromArgb(220, 220, 220), Color.FromArgb(140, 140, 140))
            Case "sepia"
                Return New TextReaderPalette(Color.FromArgb(244, 236, 216), Color.FromArgb(91, 70, 54), Color.FromArgb(140, 118, 96))
            Case "black"
                Return New TextReaderPalette(Color.Black, Color.FromArgb(204, 204, 204), Color.FromArgb(128, 128, 128))
            Case Else
                Return New TextReaderPalette(systemBack, systemInk, Color.FromArgb(150, systemInk))
        End Select
    End Function

    ''' <summary>The tint under a search hit; the current hit is stronger. Translucent like the selection band,
    ''' so the theme's ink stays readable on it.</summary>
    Public Function MatchBandColor(isCurrent As Boolean) As Color
        Return If(isCurrent, Color.FromArgb(150, 255, 150, 0), Color.FromArgb(80, 255, 200, 0))
    End Function

    ''' <summary>Digits the gutter needs for the largest line number on screen.</summary>
    Public Function GutterDigits(lastLineNumber As Long) As Integer
        Return Math.Max(2, Math.Max(1L, lastLineNumber).ToString(Globalization.CultureInfo.InvariantCulture).Length)
    End Function

    ''' <summary>
    ''' Every hit of <paramref name="query"/> in the lines, in reading order. Literal by default; a regular
    ''' expression only when <paramref name="useRegex"/>, bounded by <see cref="RegexTimeout"/> per line. An
    ''' invalid pattern or a timeout ends the search with what was found and sets <paramref name="problem"/>.
    ''' Hits never overlap and an empty hit is skipped.
    ''' </summary>
    Public Function FindMatches(lines As String(), query As String, matchCase As Boolean, wholeWord As Boolean,
                                useRegex As Boolean, ByRef problem As String) As List(Of TextMatch)
        problem = Nothing
        Dim matches As New List(Of TextMatch)()
        If lines Is Nothing OrElse String.IsNullOrEmpty(query) Then Return matches
        If useRegex OrElse wholeWord Then
            Dim pattern As String = If(useRegex, query, Regex.Escape(query))
            If wholeWord Then pattern = "(?<![\p{L}\p{N}_])(?:" & pattern & ")(?![\p{L}\p{N}_])"
            Dim options As RegexOptions = RegexOptions.CultureInvariant
            If Not matchCase Then options = options Or RegexOptions.IgnoreCase
            Dim expression As Regex
            Try
                expression = New Regex(pattern, options, RegexTimeout)
            Catch ex As ArgumentException
                problem = "pattern"
                Return matches
            End Try
            For line As Integer = 0 To lines.Length - 1
                Try
                    Dim hit As Match = expression.Match(lines(line))
                    While hit.Success
                        If hit.Length > 0 Then
                            matches.Add(New TextMatch(line, hit.Index, hit.Length))
                            If matches.Count >= MaxMatches Then
                                problem = "limit"
                                Return matches
                            End If
                        End If
                        hit = hit.NextMatch()
                    End While
                Catch ex As RegexMatchTimeoutException
                    problem = "timeout"
                    Return matches
                End Try
            Next
            Return matches
        End If

        Dim comparison As StringComparison = If(matchCase, StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase)
        For line As Integer = 0 To lines.Length - 1
            Dim text As String = lines(line)
            Dim index As Integer = text.IndexOf(query, comparison)
            While index >= 0
                matches.Add(New TextMatch(line, index, query.Length))
                If matches.Count >= MaxMatches Then
                    problem = "limit"
                    Return matches
                End If
                index = text.IndexOf(query, index + query.Length, comparison)
            End While
        Next
        Return matches
    End Function

    ''' <summary>The first hit at or after a line, for a new search that starts where the reader is; wraps to
    ''' the first hit. -1 when there is none.</summary>
    Public Function FirstMatchFrom(matches As IList(Of TextMatch), line As Integer) As Integer
        If matches Is Nothing OrElse matches.Count = 0 Then Return -1
        For i As Integer = 0 To matches.Count - 1
            If matches(i).Line >= line Then Return i
        Next
        Return 0
    End Function

    ''' <summary>The next or previous hit index, wrapping round.</summary>
    Public Function StepMatch(count As Integer, current As Integer, forward As Boolean) As Integer
        If count <= 0 Then Return -1
        If current < 0 Then Return If(forward, 0, count - 1)
        If forward Then Return (current + 1) Mod count
        Return (current - 1 + count) Mod count
    End Function

    ''' <summary>The encoding's short name for the status line: UTF-8, UTF-16 LE, windows-1251, ..</summary>
    Public Function EncodingName(encoding As Encoding, hasBom As Boolean) As String
        If encoding Is Nothing Then Return ""
        Dim name As String
        Select Case encoding.CodePage
            Case 65001 : name = "UTF-8"
            Case 1200 : name = "UTF-16 LE"
            Case 1201 : name = "UTF-16 BE"
            Case 12000 : name = "UTF-32 LE"
            Case 12001 : name = "UTF-32 BE"
            Case Else : name = encoding.WebName
        End Select
        Return If(hasBom, name & " BOM", name)
    End Function

    ''' <summary>The terminator name for the status line; empty for a window without one. Mixed is a word the
    ''' caller translates, so it is not named here.</summary>
    Public Function LineEndingName(kind As LineEndingKind) As String
        Select Case kind
            Case LineEndingKind.Crlf : Return "CRLF"
            Case LineEndingKind.Lf : Return "LF"
            Case LineEndingKind.Cr : Return "CR"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>
    ''' The characters of <paramref name="line"/> from <paramref name="start"/> that fit into
    ''' <paramref name="width"/> pixels, cut after the last space when the line goes on, never between a
    ''' surrogate pair. At least one character, so a too narrow surface still makes progress.
    ''' </summary>
    Public Function FitRow(line As String, start As Integer, width As Integer, charWidth As Func(Of String, Integer, Integer)) As Integer
        Dim used As Integer = 0
        Dim count As Integer = 0
        Dim i As Integer = start
        While i < line.Length
            Dim units As Integer = If(Char.IsHighSurrogate(line(i)) AndAlso i + 1 < line.Length, 2, 1)
            Dim w As Integer = charWidth(line, i)
            If count > 0 AndAlso used + w > width Then Exit While
            used += w
            count += units
            i += units
        End While
        If start + count < line.Length Then
            Dim space As Integer = line.LastIndexOf(" "c, start + count - 1, count)
            If space > start Then count = space - start + 1
        End If
        Return Math.Max(1, count)
    End Function

End Module
#End If
