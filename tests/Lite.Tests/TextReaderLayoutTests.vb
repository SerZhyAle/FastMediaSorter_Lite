#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Text
Imports Xunit

' Specification 277 section 4.2 (plan step P3.3): the pure half of the full reader - lines of a
' window, tab expansion and whitespace markers in the same columns, the in-window search and
' the row fit the wrapped layout uses.
Public Class TextReaderLayoutTests

    <Fact>
    Public Sub A_binary_view_shows_every_control_character_as_a_sign_of_the_same_length()
        Dim source As String = "a" & ChrW(0) & ChrW(1) & ChrW(31) & vbTab & "b" & vbCr & vbLf & ChrW(127) & ChrW(128) & ChrW(159) & ChrW(160) & "z"
        Dim shown As String = TextReaderLayout.EscapeControlCharacters(source)
        Assert.Equal(source.Length, shown.Length)
        Assert.Equal("a" & ChrW(&H2400) & ChrW(&H2401) & ChrW(&H241F) & vbTab & "b" & vbCr & vbLf & ChrW(&H2421) & ChrW(&HB7) & ChrW(&HB7) & ChrW(160) & "z", shown)
        Assert.Equal("", TextReaderLayout.EscapeControlCharacters(""))
        Assert.Null(TextReaderLayout.EscapeControlCharacters(Nothing))
    End Sub

    <Fact>
    Public Sub A_binary_view_reads_one_byte_as_one_character()
        Dim encoding = TextEncodingDetector.CreateEncoding(TextReaderLayout.BinaryViewCodePage, False)
        Dim bytes(255) As Byte
        For index As Integer = 0 To 255
            bytes(index) = CByte(index)
        Next
        Assert.Equal(256, encoding.GetString(bytes).Length)
        Assert.Equal(bytes, encoding.GetBytes(encoding.GetString(bytes)))
    End Sub

    <Fact>
    Public Sub Lines_split_on_every_terminator_and_count_them()
        Dim lines = TextReaderLayout.BuildLines("a" & vbCrLf & "b" & vbLf & "c" & vbCr & "d", 4, False, False)
        Assert.Equal(New String() {"a", "b", "c", "d"}, lines.CopyLines)
        Assert.Equal(3, lines.Terminators)
    End Sub

    <Fact>
    Public Sub A_final_break_keeps_its_empty_line_only_in_the_last_window()
        Dim last = TextReaderLayout.BuildLines("a" & vbLf, 4, False, False)
        Assert.Equal(New String() {"a", ""}, last.CopyLines)
        Dim inner = TextReaderLayout.BuildLines("a" & vbLf, 4, False, True)
        Assert.Equal(New String() {"a"}, inner.CopyLines)
        Assert.Equal(1, inner.Terminators)
    End Sub

    <Fact>
    Public Sub Empty_text_is_one_empty_line()
        Dim lines = TextReaderLayout.BuildLines("", 4, True, True)
        Assert.Equal(New String() {""}, lines.CopyLines)
        Assert.Equal(0, lines.Terminators)
    End Sub

    <Fact>
    Public Sub Tabs_expand_to_the_next_stop_and_markers_keep_the_columns()
        Dim copy As String = Nothing
        Dim display As String = Nothing
        TextReaderLayout.ExpandLine("ab" & vbTab & "c d", 4, True, copy, display)
        Assert.Equal("ab  c d", copy)
        Assert.Equal("ab" & TextReaderLayout.TabMarker & " c" & TextReaderLayout.SpaceMarker & "d", display)
        Assert.Equal(copy.Length, display.Length)
    End Sub

    <Fact>
    Public Sub Without_markers_the_drawn_text_is_the_copy()
        Dim lines = TextReaderLayout.BuildLines(vbTab & "x y", 2, False, False)
        Assert.Equal("  x y", lines.CopyLines(0))
        Assert.Equal(lines.CopyLines(0), lines.DisplayLines(0))
    End Sub

    <Fact>
    Public Sub Literal_search_finds_every_hit_without_overlap()
        Dim problem As String = Nothing
        Dim hits = TextReaderLayout.FindMatches(New String() {"aaaa", "xAa"}, "aa", False, False, False, problem)
        Assert.Null(problem)
        Assert.Equal(3, hits.Count)
        Assert.Equal(0, hits(0).Column)
        Assert.Equal(2, hits(1).Column)
        Assert.Equal(1, hits(2).Line)
        Assert.Equal(1, hits(2).Column)
    End Sub

    <Fact>
    Public Sub Case_and_whole_word_narrow_the_hits()
        Dim problem As String = Nothing
        Dim lines = New String() {"Word word words sword"}
        Assert.Equal(1, TextReaderLayout.FindMatches(lines, "word", True, True, False, problem).Count)
        Assert.Equal(2, TextReaderLayout.FindMatches(lines, "word", False, True, False, problem).Count)
        Assert.Equal(4, TextReaderLayout.FindMatches(lines, "word", False, False, False, problem).Count)
    End Sub

    <Fact>
    Public Sub An_invalid_pattern_is_reported_not_thrown()
        Dim problem As String = Nothing
        Dim hits = TextReaderLayout.FindMatches(New String() {"abc"}, "(", False, False, True, problem)
        Assert.Equal("pattern", problem)
        Assert.Empty(hits)
    End Sub

    <Fact>
    Public Sub Regex_search_skips_empty_hits()
        Dim problem As String = Nothing
        Dim hits = TextReaderLayout.FindMatches(New String() {"a1b22"}, "\d*", False, False, True, problem)
        Assert.Null(problem)
        Assert.Equal(2, hits.Count)
        Assert.Equal(2, hits(1).Length)
    End Sub

    <Fact>
    Public Sub Stepping_wraps_round_and_a_new_search_starts_at_the_reading_line()
        Dim problem As String = Nothing
        Dim hits = TextReaderLayout.FindMatches(New String() {"x", "", "x", "x"}, "x", False, False, False, problem)
        Assert.Equal(1, TextReaderLayout.FirstMatchFrom(hits, 1))
        Assert.Equal(0, TextReaderLayout.FirstMatchFrom(hits, 9))
        Assert.Equal(0, TextReaderLayout.StepMatch(3, 2, True))
        Assert.Equal(2, TextReaderLayout.StepMatch(3, 0, False))
        Assert.Equal(-1, TextReaderLayout.StepMatch(0, 0, True))
    End Sub

    <Fact>
    Public Sub A_row_breaks_after_the_last_space_that_fits_and_never_inside_a_pair()
        Dim width As Func(Of String, Integer, Integer) = Function(line, at) 10
        Assert.Equal(6, TextReaderLayout.FitRow("hello world", 0, 80, width))
        Dim pair As String = Char.ConvertFromUtf32(&H1F600)
        Assert.Equal(1, TextReaderLayout.FitRow("abc" & pair, 0, 10, width))
        Assert.Equal(2, TextReaderLayout.FitRow(pair & "abc", 0, 5, width))
        Assert.Equal(3, TextReaderLayout.FitRow("abc", 0, 1000, width))
    End Sub

    <Fact>
    Public Sub Status_names_of_encodings_and_endings()
        Assert.Equal("UTF-8 BOM", TextReaderLayout.EncodingName(New UTF8Encoding(True), True))
        Assert.Equal("UTF-16 BE", TextReaderLayout.EncodingName(Encoding.BigEndianUnicode, False))
        Assert.Equal("CRLF", TextReaderLayout.LineEndingName(LineEndingKind.Crlf))
        Assert.Equal("", TextReaderLayout.LineEndingName(LineEndingKind.Mixed))
        Assert.Equal(2, TextReaderLayout.GutterDigits(7))
        Assert.Equal(5, TextReaderLayout.GutterDigits(12345))
    End Sub

    <Fact>
    Public Sub Every_theme_has_its_own_surface_and_system_follows_the_viewer()
        Dim back = Drawing.Color.Navy
        Dim ink = Drawing.Color.Yellow
        Assert.Equal(back, TextReaderLayout.Palette("system", back, ink).Back)
        Assert.Equal(ink, TextReaderLayout.Palette("system", back, ink).Ink)
        Assert.NotEqual(TextReaderLayout.Palette("sepia", back, ink).Back, TextReaderLayout.Palette("dark", back, ink).Back)
        Assert.Equal(Drawing.Color.Black.ToArgb(), TextReaderLayout.Palette("black", back, ink).Back.ToArgb())
    End Sub

End Class
#End If
