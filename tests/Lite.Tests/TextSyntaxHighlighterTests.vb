#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

' Specification 277 step P3.8 (sections 4.2 and 12.1): the bounded syntax highlighter and the token inks of the
' reader palette. Each language gets one document whose spans are asserted by kind and text; the bounds (the 50 ms
' budget, the 1 MiB ceiling, the long line, hostile input, the regex timeout) and the 4.5 : 1 contrast of every ink
' on every theme are held by name.
Public Class TextSyntaxHighlighterTests

    ''' <summary>A double quote written as a single one, so JSON and YAML samples stay readable.</summary>
    Private Shared Function Q(text As String) As String
        Return text.Replace("'"c, """"c)
    End Function

    Private Shared Function Repeat(unit As String, count As Integer) As String
        Dim builder As New StringBuilder(unit.Length * count)
        For i As Integer = 1 To count
            builder.Append(unit)
        Next
        Return builder.ToString()
    End Function

    ''' <summary>The spans of one line as "Kind:text | Kind:text".</summary>
    Private Shared Function Tokens(lines As String(), result As TextSyntaxResult, line As Integer) As String
        Dim parts As New List(Of String)()
        Dim first As Integer = result.FirstSpanOf(line)
        For k As Integer = 0 To result.SpanCountOf(line) - 1
            Dim span As TextTokenSpan = result.Spans(first + k)
            parts.Add(span.Kind.ToString() & ":" & lines(line).Substring(span.Start, span.Length))
        Next
        Return String.Join(" | ", parts)
    End Function

    Private Shared Function Zero() As TimeSpan
        Return TimeSpan.Zero
    End Function

    ''' <summary>Spans ascend, never overlap, stay inside their line, and the per-line index agrees with them.</summary>
    Private Shared Sub AssertWellFormed(lines As String(), result As TextSyntaxResult)
        Dim previousLine As Integer = -1
        Dim previousEnd As Integer = 0
        For Each span As TextTokenSpan In result.Spans
            Assert.True(span.Length > 0, "an empty span")
            Assert.True(span.Line >= 0 AndAlso span.Line < lines.Length, "a span outside the lines: " & span.Line)
            Assert.True(span.Start >= 0 AndAlso span.Start + span.Length <= lines(span.Line).Length,
                        "a span outside its line " & span.Line & ": " & span.Start & "+" & span.Length)
            If span.Line = previousLine Then
                Assert.True(span.Start >= previousEnd, "overlapping spans on line " & span.Line)
            Else
                Assert.True(span.Line > previousLine, "spans out of order at line " & span.Line)
            End If
            previousLine = span.Line
            previousEnd = span.Start + span.Length
        Next
        Dim seen As Integer = 0
        For line As Integer = 0 To lines.Length - 1
            Dim first As Integer = result.FirstSpanOf(line)
            Assert.Equal(seen, first)
            For k As Integer = 0 To result.SpanCountOf(line) - 1
                Assert.Equal(line, result.Spans(first + k).Line)
            Next
            seen += result.SpanCountOf(line)
        Next
        Assert.Equal(result.Spans.Length, seen)
    End Sub

    Private Shared Function Run(lines As String(), language As TextSyntaxLanguage) As TextSyntaxResult
        Dim result As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, language, TimeSpan.FromSeconds(60), AddressOf Zero)
        Assert.Equal(TextSyntaxStatus.Done, result.Status)
        Assert.True(result.Complete)
        AssertWellFormed(lines, result)
        Return result
    End Function

    ' --- language by extension -----------------------------------------------------------------------

    <Fact>
    Public Sub The_language_is_chosen_by_the_file_extension()
        Assert.Equal(TextSyntaxLanguage.Json, TextSyntaxHighlighter.LanguageOf("json"))
        Assert.Equal(TextSyntaxLanguage.Json, TextSyntaxHighlighter.LanguageOf(".JSON"))
        Assert.Equal(TextSyntaxLanguage.Xml, TextSyntaxHighlighter.LanguageOf(".xml"))
        Assert.Equal(TextSyntaxLanguage.Xml, TextSyntaxHighlighter.LanguageOf("svg"))
        Assert.Equal(TextSyntaxLanguage.Yaml, TextSyntaxHighlighter.LanguageOf(".yml"))
        Assert.Equal(TextSyntaxLanguage.Yaml, TextSyntaxHighlighter.LanguageOf("yaml"))
        Assert.Equal(TextSyntaxLanguage.Markdown, TextSyntaxHighlighter.LanguageOf(".md"))
        Assert.Equal(TextSyntaxLanguage.Ini, TextSyntaxHighlighter.LanguageOf(".ini"))
        Assert.Equal(TextSyntaxLanguage.Ini, TextSyntaxHighlighter.LanguageOf("toml"))
        Assert.Equal(TextSyntaxLanguage.Ini, TextSyntaxHighlighter.LanguageOf("conf"))
        Assert.Equal(TextSyntaxLanguage.Log, TextSyntaxHighlighter.LanguageOf(".log"))
    End Sub

    <Fact>
    Public Sub An_unknown_extension_gives_no_spans()
        Dim lines As String() = {Q("{'a': 1}"), "plain"}
        For Each extension As String In {"txt", ".csv", "cs", "py", "", Nothing, "   "}
            Assert.Equal(TextSyntaxLanguage.None, TextSyntaxHighlighter.LanguageOf(extension))
            Dim result As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, extension)
            Assert.Equal(TextSyntaxStatus.NoLanguage, result.Status)
            Assert.False(result.Complete)
            Assert.Empty(result.Spans)
            Assert.Equal(0, result.SpanCountOf(0))
            Assert.Equal(0, result.SpanCountOf(1))
        Next
    End Sub

    <Fact>
    Public Sub An_empty_or_null_document_is_done_without_spans()
        Dim none As TextSyntaxResult = TextSyntaxHighlighter.Highlight(Nothing, "json")
        Assert.Equal(TextSyntaxStatus.Done, none.Status)
        Assert.Empty(none.Spans)
        Dim lines As String() = {Nothing, "", Q("{'a': 1}")}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Json)
        Assert.Equal(0, result.SpanCountOf(0))
        Assert.Equal(0, result.SpanCountOf(1))
        Assert.Equal(2, result.SpanCountOf(2))
        Assert.Equal(0, result.SpanCountOf(-1))
        Assert.Equal(0, result.SpanCountOf(3))
    End Sub

    ' --- the six languages ---------------------------------------------------------------------------

    <Fact>
    Public Sub Json_keys_strings_numbers_literals_and_comments()
        Dim lines As String() = {
            Q("{'name': 'fms', 'n': -1.5e3, 'ok': true, 'x': null}"),
            Q("  'a': 1 // note"),
            Q("{'q': 'say \'hi\''}"),
            "[1, 2.5, false]"}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Json)
        Assert.Equal(Q("Key:'name' | StringValue:'fms' | Key:'n' | Number:-1.5e3 | Key:'ok' | Keyword:true | Key:'x' | Keyword:null"),
                     Tokens(lines, result, 0))
        Assert.Equal(Q("Key:'a' | Number:1 | Comment:// note"), Tokens(lines, result, 1))
        Assert.Equal(Q("Key:'q' | StringValue:'say \'hi\''"), Tokens(lines, result, 2))
        Assert.Equal("Number:1 | Number:2.5 | Keyword:false", Tokens(lines, result, 3))
    End Sub

    <Fact>
    Public Sub Xml_tags_attributes_values_comments_and_declarations()
        Dim lines As String() = {
            Q("<?xml version='1.0'?>"),
            "<!-- a comment -->",
            Q("<root id='1'>"),
            "  <item name='x'/>",
            "  text and more",
            "</root>",
            "<![CDATA[ raw ]]>"}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Xml)
        Assert.Equal(Q("Keyword:<?xml version='1.0'?>"), Tokens(lines, result, 0))
        Assert.Equal("Comment:<!-- a comment -->", Tokens(lines, result, 1))
        Assert.Equal(Q("Keyword:<root | Key:id | StringValue:'1'"), Tokens(lines, result, 2))
        Assert.Equal("Keyword:<item | Key:name | StringValue:'x'", Tokens(lines, result, 3))
        Assert.Equal("", Tokens(lines, result, 4))
        Assert.Equal("Keyword:</root", Tokens(lines, result, 5))
        Assert.Equal("StringValue:<![CDATA[ raw ]]>", Tokens(lines, result, 6))
    End Sub

    <Fact>
    Public Sub Xml_comments_and_tags_carry_over_lines()
        Dim lines As String() = {
            "<!-- a",
            "b",
            "c --><a",
            "  x='1'",
            "  y=" & Q("'two") ,
            Q("three' />"),
            "after"}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Xml)
        Assert.Equal("Comment:<!-- a", Tokens(lines, result, 0))
        Assert.Equal("Comment:b", Tokens(lines, result, 1))
        Assert.Equal("Comment:c --> | Keyword:<a", Tokens(lines, result, 2))
        Assert.Equal("Key:x | StringValue:'1'", Tokens(lines, result, 3))
        Assert.Equal(Q("Key:y | StringValue:'two"), Tokens(lines, result, 4))
        Assert.Equal(Q("StringValue:three'"), Tokens(lines, result, 5))
        Assert.Equal("", Tokens(lines, result, 6))
    End Sub

    <Fact>
    Public Sub Yaml_keys_values_comments_lists_and_block_scalars()
        Dim lines As String() = {
            "# config",
            "name: fms",
            "count: 42",
            "enabled: true",
            "items:",
            "  - one",
            Q("  - 'two' # note"),
            "text: |",
            "  block line",
            "",
            "  another: not a key",
            "after: x",
            "url: http://example.org/a",
            "---"}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Yaml)
        Assert.Equal("Comment:# config", Tokens(lines, result, 0))
        Assert.Equal("Key:name | StringValue:fms", Tokens(lines, result, 1))
        Assert.Equal("Key:count | Number:42", Tokens(lines, result, 2))
        Assert.Equal("Key:enabled | Keyword:true", Tokens(lines, result, 3))
        Assert.Equal("Key:items", Tokens(lines, result, 4))
        Assert.Equal("Keyword:- | StringValue:one", Tokens(lines, result, 5))
        Assert.Equal(Q("Keyword:- | StringValue:'two' | Comment:# note"), Tokens(lines, result, 6))
        Assert.Equal("Key:text | Keyword:|", Tokens(lines, result, 7))
        Assert.Equal("StringValue:block line", Tokens(lines, result, 8))
        Assert.Equal("", Tokens(lines, result, 9))
        Assert.Equal("StringValue:another: not a key", Tokens(lines, result, 10))
        Assert.Equal("Key:after | StringValue:x", Tokens(lines, result, 11))
        Assert.Equal("Key:url | StringValue:http://example.org/a", Tokens(lines, result, 12))
        Assert.Equal("Keyword:---", Tokens(lines, result, 13))
    End Sub

    <Fact>
    Public Sub Markdown_headings_code_links_lists_quotes_and_fences()
        Dim lines As String() = {
            "# Title",
            "text with `code` and [link](http://x.y) end",
            "- item",
            "1. first",
            "> quote",
            "```vb",
            "  code line [not](a link)",
            "```",
            "after",
            "---",
            "![pic](a.png) <https://x.y>"}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Markdown)
        Assert.Equal("Heading:# Title", Tokens(lines, result, 0))
        Assert.Equal("StringValue:`code` | Key:[link] | Comment:(http://x.y)", Tokens(lines, result, 1))
        Assert.Equal("Keyword:-", Tokens(lines, result, 2))
        Assert.Equal("Keyword:1.", Tokens(lines, result, 3))
        Assert.Equal("Keyword:>", Tokens(lines, result, 4))
        Assert.Equal("Keyword:```vb", Tokens(lines, result, 5))
        Assert.Equal("StringValue:code line [not](a link)", Tokens(lines, result, 6))
        Assert.Equal("Keyword:```", Tokens(lines, result, 7))
        Assert.Equal("", Tokens(lines, result, 8))
        Assert.Equal("Keyword:---", Tokens(lines, result, 9))
        Assert.Equal("Key:![pic] | Comment:(a.png) | Key:<https://x.y>", Tokens(lines, result, 10))
    End Sub

    <Fact>
    Public Sub Ini_sections_keys_values_and_comments()
        Dim lines As String() = {
            "; comment",
            "[Section]",
            "key = value",
            "count=42 ; trailing",
            "flag = true",
            Q("name = 'quoted' # c"),
            "[[table]]",
            "  indented = 1.5",
            "no separator here",
            "path: C:\temp"}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Ini)
        Assert.Equal("Comment:; comment", Tokens(lines, result, 0))
        Assert.Equal("Heading:[Section]", Tokens(lines, result, 1))
        Assert.Equal("Key:key", Tokens(lines, result, 2))
        Assert.Equal("Key:count | Number:42 | Comment:; trailing", Tokens(lines, result, 3))
        Assert.Equal("Key:flag | Keyword:true", Tokens(lines, result, 4))
        Assert.Equal(Q("Key:name | StringValue:'quoted' | Comment:# c"), Tokens(lines, result, 5))
        Assert.Equal("Heading:[[table]]", Tokens(lines, result, 6))
        Assert.Equal("Key:indented | Number:1.5", Tokens(lines, result, 7))
        Assert.Equal("", Tokens(lines, result, 8))
        Assert.Equal("Key:path", Tokens(lines, result, 9))
    End Sub

    <Fact>
    Public Sub Log_timestamps_levels_and_stack_frames()
        Dim lines As String() = {
            "2026-11-08 12:00:01,123 ERROR something failed",
            "2026-11-08T12:00:02Z [WARN] careful",
            "   at Foo.Bar() in file.cs:line 3",
            "12:00:03 INFO ok",
            "plain text without anything",
            "[ERROR] boom",
            "Nov  8 12:00:04 host DEBUG tick"}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Log)
        Assert.Equal("Number:2026-11-08 12:00:01,123 | ErrorLevel:ERROR", Tokens(lines, result, 0))
        Assert.Equal("Number:2026-11-08T12:00:02Z | WarningLevel:WARN", Tokens(lines, result, 1))
        Assert.Equal("Comment:at Foo.Bar() in file.cs:line 3", Tokens(lines, result, 2))
        Assert.Equal("Number:12:00:03 | Keyword:INFO", Tokens(lines, result, 3))
        Assert.Equal("", Tokens(lines, result, 4))
        Assert.Equal("ErrorLevel:ERROR", Tokens(lines, result, 5))
        Assert.Equal("Number:Nov  8 12:00:04 | Keyword:DEBUG", Tokens(lines, result, 6))
    End Sub

    ' --- the bounds ----------------------------------------------------------------------------------

    <Fact>
    Public Sub The_budget_is_fifty_milliseconds()
        Assert.Equal(TimeSpan.FromMilliseconds(50), TextSyntaxHighlighter.HighlightBudget)
        Assert.Equal(1048576, TextSyntaxHighlighter.MaxHighlightChars)
        Assert.True(TextSyntaxHighlighter.HighlightRegexTimeout > TimeSpan.Zero)
        Dim small As String() = {Q("{'a': 1}"), Q("{'b': 2}")}
        Dim result As TextSyntaxResult = TextSyntaxHighlighter.Highlight(small, "json")
        Assert.Equal(TextSyntaxStatus.Done, result.Status)
        Assert.Equal(4, result.Spans.Length)
    End Sub

    <Fact>
    Public Sub An_exhausted_budget_returns_what_was_finished_and_the_rest_is_plain()
        Dim lines(19) As String
        For i As Integer = 0 To lines.Length - 1
            lines(i) = Q("{'k" & i & "': " & i & "}")
        Next
        ' A clock that moves 10 ms every time it is asked: with 50 ms the lines asked at 0, 10, 20, 30 and 40 ms are done.
        Dim asked As Integer = 0
        Dim clock As Func(Of TimeSpan) =
            Function()
                Dim now As TimeSpan = TimeSpan.FromMilliseconds(10 * asked)
                asked += 1
                Return now
            End Function
        Dim result As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, TextSyntaxLanguage.Json, TimeSpan.FromMilliseconds(50), clock)
        Assert.Equal(TextSyntaxStatus.OutOfTime, result.Status)
        Assert.False(result.Complete)
        Assert.Equal(5, result.LinesDone)
        Assert.Equal(20, result.LineCount)
        AssertWellFormed(lines, result)
        For i As Integer = 0 To 4
            Assert.Equal(2, result.SpanCountOf(i))
        Next
        For i As Integer = 5 To 19
            Assert.Equal(0, result.SpanCountOf(i))
        Next
        Assert.Equal(10, result.Spans.Length)
        Assert.Equal(6, asked)

        Dim none As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, TextSyntaxLanguage.Json, TimeSpan.Zero, AddressOf Zero)
        Assert.Equal(TextSyntaxStatus.OutOfTime, none.Status)
        Assert.Equal(0, none.LinesDone)
        Assert.Empty(none.Spans)
    End Sub

    <Fact>
    Public Sub Text_over_a_mebibyte_is_not_highlighted_and_at_the_ceiling_it_is()
        Dim body As String = Repeat("x", 1023 - 8)
        Dim lines(1023) As String
        For i As Integer = 0 To lines.Length - 1
            lines(i) = Q("{'k':'" & body & "'}")
        Next
        ' 1024 lines of 1023 characters and 1023 separators: one character below the ceiling.
        Dim below As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, TextSyntaxLanguage.Json, TimeSpan.FromSeconds(60), AddressOf Zero)
        Assert.Equal(TextSyntaxStatus.Done, below.Status)
        Assert.Equal(2 * lines.Length, below.Spans.Length)
        AssertWellFormed(lines, below)

        lines(0) &= "x"
        Dim atCeiling As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, TextSyntaxLanguage.Json, TimeSpan.FromSeconds(60), AddressOf Zero)
        Assert.Equal(TextSyntaxStatus.Done, atCeiling.Status)

        lines(0) &= "x"
        Dim overCeiling As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, TextSyntaxLanguage.Json, TimeSpan.FromSeconds(60), AddressOf Zero)
        Assert.Equal(TextSyntaxStatus.TooLarge, overCeiling.Status)
        Assert.False(overCeiling.Complete)
        Assert.Empty(overCeiling.Spans)
        Assert.Equal(0, overCeiling.SpanCountOf(0))
    End Sub

    <Fact>
    Public Sub A_line_over_the_limit_stays_plain_and_its_neighbours_are_coloured()
        Dim limit As Integer = TextSyntaxHighlighter.MaxHighlightLineChars
        Dim atLimit As String = Q("{'k':'") & Repeat("x", limit - 8) & Q("'}")
        Assert.Equal(limit, atLimit.Length)
        Dim lines As String() = {Q("{'a': 1}"), Q("{'b': '") & Repeat("x", 5000) & Q("'}"), Q("{'c': 2}"), atLimit, atLimit & " "}
        Dim result As TextSyntaxResult = Run(lines, TextSyntaxLanguage.Json)
        Assert.Equal(2, result.SpanCountOf(0))
        Assert.Equal(0, result.SpanCountOf(1))
        Assert.Equal(2, result.SpanCountOf(2))
        Assert.True(result.SpanCountOf(3) > 0)
        Assert.Equal(0, result.SpanCountOf(4))
    End Sub

    <Fact>
    Public Sub Hostile_lines_stay_within_bounds_in_every_language()
        Dim hostile As New List(Of String) From {
            Repeat("a", 3999) & "!",
            "1" & Repeat("1", 3990) & "x",
            Q("'") & Repeat("\""", 1998),
            Q("'x") & Repeat("\""x", 1300),
            Repeat("<a ", 1300),
            Repeat("<!-", 1300),
            "<!--" & Repeat("-", 3980),
            Repeat("[", 3990),
            Repeat("[a](", 990),
            Repeat("`", 3990),
            Repeat("`a", 1900),
            Repeat("![", 1900),
            Repeat(" ", 3990) & "x",
            "a" & Repeat(" ", 3990),
            Repeat("a:", 1990),
            Repeat(": ", 1990),
            Repeat("- ", 1990),
            Repeat("-", 3990),
            Repeat("> ", 1990),
            Repeat("&a ", 1300),
            Repeat("[ERROR]", 570),
            Repeat("2026-11-08 ", 360),
            Repeat("12:00:00 ", 440),
            "/*" & Repeat("*", 3990),
            Repeat("=", 3990),
            Repeat("= ;", 1300)}
        For Each line As String In hostile
            Assert.True(line.Length <= TextSyntaxHighlighter.MaxHighlightLineChars, "test line too long: " & line.Length)
        Next
        Dim lines As String() = hostile.ToArray()
        Dim watch As Stopwatch = Stopwatch.StartNew()
        For Each language As TextSyntaxLanguage In [Enum].GetValues(GetType(TextSyntaxLanguage))
            If language = TextSyntaxLanguage.None Then Continue For
            Dim result As TextSyntaxResult = TextSyntaxHighlighter.Highlight(lines, language, TimeSpan.FromSeconds(120), AddressOf Zero)
            Assert.Equal(TextSyntaxStatus.Done, result.Status)
            AssertWellFormed(lines, result)
        Next
        watch.Stop()
        Assert.True(watch.ElapsedMilliseconds < 8000, "hostile lines took " & watch.ElapsedMilliseconds & " ms")
    End Sub

    <Fact>
    Public Sub A_regex_timeout_is_a_plain_line_and_never_an_exception()
        Dim hostileExpression As New Regex("(a+)+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20))
        Dim timedOut As Boolean
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim hits As List(Of Match) = TextSyntaxHighlighter.SafeMatches(hostileExpression, Repeat("a", 45) & "!", timedOut)
        watch.Stop()
        Assert.True(timedOut)
        Assert.Empty(hits)
        Assert.True(watch.ElapsedMilliseconds < 4000, "the timeout took " & watch.ElapsedMilliseconds & " ms")

        Dim fine As List(Of Match) = TextSyntaxHighlighter.SafeMatches(New Regex("a+", RegexOptions.None, TimeSpan.FromSeconds(1)), "aa b aaa", timedOut)
        Assert.False(timedOut)
        Assert.Equal(2, fine.Count)
    End Sub

    <Fact>
    Public Sub Random_structural_noise_never_breaks_the_span_invariants()
        Dim alphabet As Char() = ("""'[]{}<>-:#!&*|`\=,;/ " & ControlChars.Tab & ".0123456789abcxyz").ToCharArray()
        Dim words As String() = {"true", "null", "ERROR", "WARN", "http://", "```", "<!--", "-->", "]]>", "<![CDATA[", "2026-11-08 12:00:00", "at "}
        Dim random As New Random(20261108)
        Dim lines(399) As String
        For i As Integer = 0 To lines.Length - 1
            Dim builder As New StringBuilder()
            Dim length As Integer = random.Next(0, 120)
            While builder.Length < length
                If random.Next(0, 6) = 0 Then
                    builder.Append(words(random.Next(0, words.Length)))
                Else
                    builder.Append(alphabet(random.Next(0, alphabet.Length)))
                End If
            End While
            lines(i) = builder.ToString()
        Next
        lines(7) = Nothing
        For Each language As TextSyntaxLanguage In [Enum].GetValues(GetType(TextSyntaxLanguage))
            If language = TextSyntaxLanguage.None Then Continue For
            Run(lines, language)
        Next
    End Sub

    ' --- the token inks of the palette ---------------------------------------------------------------

    Private Shared Sub AssertEveryInkReads(theme As String, systemBack As Color)
        Dim palette As TextReaderPalette = TextReaderLayout.Palette(theme, systemBack, If(TextReaderLayout.TokenLuminance(systemBack) > 0.179R, Color.Black, Color.White))
        For Each kind As TextTokenKind In [Enum].GetValues(GetType(TextTokenKind))
            Dim ink As Color = palette.TokenInk(kind)
            Dim ratio As Double = TextReaderLayout.TokenContrast(ink, palette.Back)
            Assert.True(ratio >= TextReaderLayout.TokenContrastFloor,
                        theme & " on " & palette.Back.ToString() & ": " & kind.ToString() & " " & ink.ToString() & " is " & ratio.ToString("0.00") & " : 1")
        Next
    End Sub

    <Fact>
    Public Sub Every_token_ink_reads_at_four_and_a_half_to_one_on_all_five_themes()
        AssertEveryInkReads("light", Color.White)
        AssertEveryInkReads("dark", Color.White)
        AssertEveryInkReads("sepia", Color.White)
        AssertEveryInkReads("black", Color.White)
        ' "system" follows the viewer's own surface: pure white or pure black in the viewer, anything in principle.
        AssertEveryInkReads("system", Color.White)
        AssertEveryInkReads("system", Color.Black)
        For Each back As Color In {Color.FromArgb(118, 118, 118), Color.FromArgb(128, 128, 128), Color.FromArgb(0, 0, 128),
                                   Color.FromArgb(200, 160, 255), Color.FromArgb(30, 30, 30), Color.FromArgb(250, 240, 200),
                                   Color.FromArgb(46, 125, 50), Color.FromArgb(211, 47, 47), Color.FromArgb(255, 255, 0)}
            AssertEveryInkReads("system", back)
        Next
    End Sub

    <Fact>
    Public Sub Every_fixed_theme_has_an_ink_of_its_own_for_every_kind()
        For Each theme As String In {"light", "dark", "sepia", "black"}
            Dim palette As TextReaderPalette = TextReaderLayout.Palette(theme, Color.White, Color.Black)
            Dim seen As New HashSet(Of Integer)()
            For Each kind As TextTokenKind In [Enum].GetValues(GetType(TextTokenKind))
                Dim ink As Color = palette.TokenInk(kind)
                Assert.NotEqual(palette.Ink.ToArgb(), ink.ToArgb())
                seen.Add(ink.ToArgb())
            Next
            Assert.Equal([Enum].GetValues(GetType(TextTokenKind)).Length, seen.Count)
        Next
    End Sub

    <Fact>
    Public Sub A_palette_without_a_token_table_answers_with_its_plain_ink()
        Dim palette As New TextReaderPalette(Color.White, Color.Black, Color.Gray)
        For Each kind As TextTokenKind In [Enum].GetValues(GetType(TextTokenKind))
            Assert.Equal(Color.Black.ToArgb(), palette.TokenInk(kind).ToArgb())
        Next
        Assert.Equal(Color.Black.ToArgb(), palette.TokenInk(CType(99, TextTokenKind)).ToArgb())
    End Sub

    <Fact>
    Public Sub Pulling_an_ink_to_the_floor_keeps_a_good_ink_and_fixes_a_weak_one()
        Dim good As Color = Color.FromArgb(0, 0, 0)
        Assert.Equal(good.ToArgb(), TextReaderLayout.EnsureTokenContrast(good, Color.White, 4.5R).ToArgb())
        Dim random As New Random(7)
        For i As Integer = 1 To 200
            Dim ink As Color = Color.FromArgb(random.Next(256), random.Next(256), random.Next(256))
            Dim back As Color = Color.FromArgb(random.Next(256), random.Next(256), random.Next(256))
            Dim fixedInk As Color = TextReaderLayout.EnsureTokenContrast(ink, back, 4.5R)
            Assert.True(TextReaderLayout.TokenContrast(fixedInk, back) >= 4.5R,
                        ink.ToString() & " on " & back.ToString() & " became " & fixedInk.ToString())
        Next
        Assert.Equal(21.0R, TextReaderLayout.TokenContrast(Color.Black, Color.White), 2)
        Assert.Equal(1.0R, TextReaderLayout.TokenContrast(Color.Gray, Color.Gray), 3)
    End Sub

End Class
#End If
