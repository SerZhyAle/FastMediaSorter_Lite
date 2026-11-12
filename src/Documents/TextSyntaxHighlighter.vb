#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text.RegularExpressions

' Specification 277 step P3.8 (section 4.2, setting `TextSyntaxHighlighting`): the pure half of the reader's
' syntax colouring. It decides WHICH stretch of a line is a key, a string, a number, a comment .. for JSON,
' XML, YAML, Markdown, INI and logs, chosen by the file extension - and nothing else: the colour of a kind is
' the reader palette's business (TextReaderLayout.vb, TextTokenKind), and Main_Form.TextReader.vb only paints.
' No WinForms and no colour is decided here.
'
' Bounded on every side, because a reader must never wait for a colour:
'  * a 50 ms Stopwatch budget over the document - what is finished stays, the rest is plain text;
'  * off above 1 MiB of text (the status says TooLarge) and a line over MaxHighlightLineChars is left plain;
'  * every Regex carries a match timeout, a timeout means a plain line, and no exception leaves the module.

''' <summary>The languages the highlighter knows; <see cref="TextSyntaxHighlighter.LanguageOf"/> picks one by extension.</summary>
Public Enum TextSyntaxLanguage
    None = 0
    Json
    Xml
    Yaml
    Markdown
    Ini
    Log
End Enum

''' <summary>How a highlighting run ended.</summary>
Public Enum TextSyntaxStatus
    ''' <summary>Every line was looked at.</summary>
    Done = 0
    ''' <summary>The budget ran out: the lines before <see cref="TextSyntaxResult.LinesDone"/> carry their spans, the rest is plain.</summary>
    OutOfTime
    ''' <summary>The extension names no language: nothing to colour.</summary>
    NoLanguage
    ''' <summary>The text is over <see cref="TextSyntaxHighlighter.MaxHighlightChars"/>: nothing is coloured.</summary>
    TooLarge
End Enum

''' <summary>One coloured stretch: a line of the window, the column it starts at in that line, its length and kind.
''' Columns are those of the line's text as given (the reader's copy lines and its display lines agree column by column).</summary>
Public Structure TextTokenSpan
    Public ReadOnly Line As Integer
    Public ReadOnly Start As Integer
    Public ReadOnly Length As Integer
    Public ReadOnly Kind As TextTokenKind

    Public Sub New(line As Integer, start As Integer, length As Integer, kind As TextTokenKind)
        Me.Line = line
        Me.Start = start
        Me.Length = length
        Me.Kind = kind
    End Sub
End Structure

''' <summary>The spans of one document or window, in reading order (line, then column), never overlapping, each
''' inside its line. Immutable once built, so the reader may keep one and paint from it without a lock.</summary>
Public NotInheritable Class TextSyntaxResult
    Private Shared ReadOnly NoSpans As TextTokenSpan() = New TextTokenSpan() {}

    Private ReadOnly lineFirst As Integer()

    Public ReadOnly Language As TextSyntaxLanguage
    Public ReadOnly Status As TextSyntaxStatus
    Public ReadOnly Spans As TextTokenSpan()
    ''' <summary>How many lines from the top were looked at; the budget ends a run at a line boundary.</summary>
    Public ReadOnly LinesDone As Integer
    Public ReadOnly LineCount As Integer

    Public Sub New(language As TextSyntaxLanguage, status As TextSyntaxStatus, spans As TextTokenSpan(),
                   lineFirst As Integer(), linesDone As Integer, lineCount As Integer)
        Me.Language = language
        Me.Status = status
        Me.Spans = If(spans, NoSpans)
        Me.lineFirst = lineFirst
        Me.LinesDone = linesDone
        Me.LineCount = lineCount
    End Sub

    ''' <summary>Every line was looked at.</summary>
    Public ReadOnly Property Complete As Boolean
        Get
            Return Status = TextSyntaxStatus.Done
        End Get
    End Property

    ''' <summary>Index into <see cref="Spans"/> of the first span of a line (meaningful with <see cref="SpanCountOf"/>).</summary>
    Public Function FirstSpanOf(line As Integer) As Integer
        If lineFirst Is Nothing OrElse line < 0 OrElse line >= LineCount Then Return 0
        Return lineFirst(line)
    End Function

    ''' <summary>How many spans a line has; 0 for a plain line, an unfinished one or a line out of range.</summary>
    Public Function SpanCountOf(line As Integer) As Integer
        If lineFirst Is Nothing OrElse line < 0 OrElse line >= LineCount Then Return 0
        Return lineFirst(line + 1) - lineFirst(line)
    End Function

    ''' <summary>Nothing was coloured: <paramref name="status"/> says why.</summary>
    Public Shared Function Skipped(language As TextSyntaxLanguage, status As TextSyntaxStatus, lineCount As Integer) As TextSyntaxResult
        Return New TextSyntaxResult(language, status, NoSpans, Nothing, 0, lineCount)
    End Function
End Class

Public Module TextSyntaxHighlighter

    ''' <summary>Above this many characters of text nothing is highlighted (a MiB, the spec's "structured files under 1 MiB").</summary>
    Public Const MaxHighlightChars As Integer = 1048576
    ''' <summary>A line longer than this is left plain: minified JSON and one-line logs are not worth a colour.</summary>
    Public Const MaxHighlightLineChars As Integer = 4000
    ''' <summary>The budget of one run over a document; what is finished stays, the rest is plain.</summary>
    Public ReadOnly HighlightBudget As TimeSpan = TimeSpan.FromMilliseconds(50)
    ''' <summary>Every Regex of this module carries it; a timeout makes the line plain. Not smaller: the framework
    ''' reads the timeout off a tick counter that lags real time by up to ~16 ms, so 10 ms made about one line in two
    ''' hundred time out for nothing (measured on a 1 MiB document); at 40 ms a line must really take 24 ms to lose its colour.</summary>
    Public ReadOnly HighlightRegexTimeout As TimeSpan = TimeSpan.FromMilliseconds(40)

    ' Lexer states carried from one line to the next (a comment, a fenced block, a tag that goes on).
    Private Const ModeText As Integer = 0
    Private Const ModeFence As Integer = 1
    Private Const ModeBlockScalar As Integer = 1
    Private Const XmlComment As Integer = 1
    Private Const XmlCdata As Integer = 2
    Private Const XmlPi As Integer = 3
    Private Const XmlDecl As Integer = 4
    Private Const XmlTag As Integer = 5
    Private Const XmlValue As Integer = 6

    Private NotInheritable Class LexState
        Public Mode As Integer
        Public Quote As Char
        Public FenceChar As Char
        Public FenceLength As Integer
        Public Indent As Integer
    End Class

    ' key | string | number | literal | comment. The string alternatives have disjoint first characters, so the
    ' pattern cannot backtrack exponentially; the 4000-character line cap and the timeout bound the rest.
    Private ReadOnly JsonToken As New Regex(
        "(?<key>""(?:[^""\\]|\\.)*""(?=[ \t]*:))|(?<str>""(?:[^""\\]|\\.)*(?:""|$))" &
        "|(?<![\w.])(?<num>-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)(?![\w.])|(?<kw>\b(?:true|false|null)\b)" &
        "|(?<cm>//.*|/\*(?:[^*]|\*(?!/))*(?:\*/|$))",
        RegexOptions.CultureInvariant, HighlightRegexTimeout)

    ' A log line's timestamp: ISO with a T or a space, day-first with dots or slashes, a bare time, a syslog date.
    Private ReadOnly LogStamp As New Regex(
        "^[\[(]?(?<ts>\d{4}[-/]\d{2}[-/]\d{2}[T ]\d{2}:\d{2}:\d{2}(?:[.,]\d{1,9})?(?:Z|[+-]\d{2}:?\d{2})?" &
        "|\d{2}[-/.]\d{2}[-/.]\d{4}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d{1,9})?" &
        "|\d{2}:\d{2}:\d{2}(?:[.,]\d{1,9})?" &
        "|[A-Z][a-z]{2} [ \d]\d \d{2}:\d{2}:\d{2})",
        RegexOptions.CultureInvariant, HighlightRegexTimeout)

    Private ReadOnly LogLevel As New Regex(
        "(?<![A-Za-z0-9_])(?<lvl>TRACE|DEBUG|INFO|NOTICE|WARNING|WARN|ERROR|FATAL|CRITICAL|SEVERE|INF|DBG|VRB|WRN|ERR|FTL" &
        "|Trace|Debug|Info|Notice|Warning|Warn|Error|Fatal|Critical|Severe)(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant, HighlightRegexTimeout)

    ''' <summary>The language of a file by its extension (with or without the dot, any case); None for anything else.</summary>
    Public Function LanguageOf(extension As String) As TextSyntaxLanguage
        If String.IsNullOrEmpty(extension) Then Return TextSyntaxLanguage.None
        Select Case extension.Trim().TrimStart("."c).ToLowerInvariant()
            Case "json", "jsonc", "jsonl", "ndjson", "geojson"
                Return TextSyntaxLanguage.Json
            Case "xml", "xaml", "xsd", "xsl", "xslt", "svg", "plist", "resx", "csproj", "vbproj", "props", "targets",
                 "config", "manifest", "rss", "atom", "wsdl", "kml", "html", "htm", "xhtml"
                Return TextSyntaxLanguage.Xml
            Case "yaml", "yml"
                Return TextSyntaxLanguage.Yaml
            Case "md", "markdown", "mdown"
                Return TextSyntaxLanguage.Markdown
            Case "ini", "conf", "cfg", "toml", "properties", "inf", "desktop", "editorconfig", "gitconfig"
                Return TextSyntaxLanguage.Ini
            Case "log"
                Return TextSyntaxLanguage.Log
            Case Else
                Return TextSyntaxLanguage.None
        End Select
    End Function

    ''' <summary>The spans of a document by its file extension, within <see cref="HighlightBudget"/> of wall time.</summary>
    Public Function Highlight(lines As String(), extension As String) As TextSyntaxResult
        Return Highlight(lines, LanguageOf(extension))
    End Function

    ''' <summary>The spans of a document as <paramref name="language"/>, within <see cref="HighlightBudget"/> of wall time.</summary>
    Public Function Highlight(lines As String(), language As TextSyntaxLanguage) As TextSyntaxResult
        Dim clock As Stopwatch = Stopwatch.StartNew()
        Return Highlight(lines, language, HighlightBudget, Function() clock.Elapsed)
    End Function

    ''' <summary>
    ''' The spans of <paramref name="lines"/> as <paramref name="language"/>. <paramref name="elapsed"/> is the clock
    ''' the budget is read from, asked once before every line: when it has reached <paramref name="budget"/> the run
    ''' ends at that line boundary with <see cref="TextSyntaxStatus.OutOfTime"/>, and the lines done keep their
    ''' spans. A document over <see cref="MaxHighlightChars"/> is not looked at; a line over
    ''' <see cref="MaxHighlightLineChars"/>, a null line, or a line whose lexing failed or timed out stays plain.
    ''' Never throws.
    ''' </summary>
    Public Function Highlight(lines As String(), language As TextSyntaxLanguage, budget As TimeSpan,
                              elapsed As Func(Of TimeSpan)) As TextSyntaxResult
        Dim count As Integer = If(lines Is Nothing, 0, lines.Length)
        If language = TextSyntaxLanguage.None Then Return TextSyntaxResult.Skipped(language, TextSyntaxStatus.NoLanguage, count)
        If count = 0 Then Return New TextSyntaxResult(language, TextSyntaxStatus.Done, Nothing, New Integer() {0}, 0, 0)

        Dim total As Long = count - 1
        For i As Integer = 0 To count - 1
            If lines(i) IsNot Nothing Then total += lines(i).Length
            If total > MaxHighlightChars Then Return TextSyntaxResult.Skipped(language, TextSyntaxStatus.TooLarge, count)
        Next

        Dim spans As New List(Of TextTokenSpan)()
        Dim lineFirst(count) As Integer
        Dim state As New LexState()
        Dim status As TextSyntaxStatus = TextSyntaxStatus.Done
        Dim done As Integer = 0
        For index As Integer = 0 To count - 1
            If elapsed() >= budget Then
                status = TextSyntaxStatus.OutOfTime
                Exit For
            End If
            lineFirst(index) = spans.Count
            Dim line As String = lines(index)
            If line IsNot Nothing AndAlso line.Length > 0 AndAlso line.Length <= MaxHighlightLineChars Then
                Dim mark As Integer = spans.Count
                Try
                    LexLine(language, line, index, state, spans)
                Catch ex As Exception
                    ' A regex timeout or anything unforeseen: colour is cosmetic, the line is plain and the run goes on.
                    spans.RemoveRange(mark, spans.Count - mark)
                End Try
            End If
            done = index + 1
        Next
        For index As Integer = done To count
            lineFirst(index) = spans.Count
        Next
        Return New TextSyntaxResult(language, status, spans.ToArray(), lineFirst, done, count)
    End Function

    ''' <summary>
    ''' Every match of <paramref name="expression"/> in <paramref name="text"/> found before its timeout. When the
    ''' timeout hits, <paramref name="timedOut"/> is set and the matches so far come back: the caller treats the line
    ''' as plain. The one way this module (and a test with a hostile pattern) runs a Regex.
    ''' </summary>
    Public Function SafeMatches(expression As Regex, text As String, ByRef timedOut As Boolean) As List(Of Match)
        timedOut = False
        Dim hits As New List(Of Match)()
        Try
            Dim hit As Match = expression.Match(text)
            While hit.Success
                hits.Add(hit)
                hit = hit.NextMatch()
            End While
        Catch ex As RegexMatchTimeoutException
            timedOut = True
        End Try
        Return hits
    End Function

    ''' <summary>The first match from <paramref name="startAt"/> on, or Nothing; <paramref name="timedOut"/> as in <see cref="SafeMatches"/>.</summary>
    Private Function SafeFirst(expression As Regex, text As String, startAt As Integer, ByRef timedOut As Boolean) As Match
        timedOut = False
        Try
            Dim hit As Match = expression.Match(text, startAt)
            Return If(hit.Success, hit, Nothing)
        Catch ex As RegexMatchTimeoutException
            timedOut = True
            Return Nothing
        End Try
    End Function

    Private Sub LexLine(language As TextSyntaxLanguage, line As String, index As Integer, state As LexState,
                        spans As List(Of TextTokenSpan))
        Select Case language
            Case TextSyntaxLanguage.Json : LexJson(line, index, spans)
            Case TextSyntaxLanguage.Xml : LexXml(line, index, state, spans)
            Case TextSyntaxLanguage.Yaml : LexYaml(line, index, state, spans)
            Case TextSyntaxLanguage.Markdown : LexMarkdown(line, index, state, spans)
            Case TextSyntaxLanguage.Ini : LexIni(line, index, spans)
            Case TextSyntaxLanguage.Log : LexLog(line, index, spans)
        End Select
    End Sub

    ' --- small helpers --------------------------------------------------------------------------------

    Private Sub Add(spans As List(Of TextTokenSpan), line As Integer, start As Integer, length As Integer, kind As TextTokenKind)
        If length > 0 Then spans.Add(New TextTokenSpan(line, start, length, kind))
    End Sub

    ''' <summary>The span of a whole line without its leading and trailing blanks.</summary>
    Private Sub AddTrimmed(spans As List(Of TextTokenSpan), line As Integer, text As String, kind As TextTokenKind)
        Dim first As Integer = SkipBlanks(text, 0)
        Dim last As Integer = text.Length
        While last > first AndAlso IsBlank(text(last - 1))
            last -= 1
        End While
        Add(spans, line, first, last - first, kind)
    End Sub

    Private Function IsBlank(c As Char) As Boolean
        Return c = " "c OrElse c = ControlChars.Tab
    End Function

    Private Function SkipBlanks(text As String, start As Integer) As Integer
        Dim i As Integer = start
        While i < text.Length AndAlso IsBlank(text(i))
            i += 1
        End While
        Return i
    End Function

    Private Function At(text As String, index As Integer, token As String) As Boolean
        Return index >= 0 AndAlso index + token.Length <= text.Length AndAlso String.CompareOrdinal(text, index, token, 0, token.Length) = 0
    End Function

    Private Function CountRun(text As String, start As Integer, c As Char) As Integer
        Dim i As Integer = start
        While i < text.Length AndAlso text(i) = c
            i += 1
        End While
        Return i - start
    End Function

    Private Function IsDigit(c As Char) As Boolean
        Return c >= "0"c AndAlso c <= "9"c
    End Function

    ''' <summary>An integer, a decimal, an exponent form or a 0x hex number; nothing else.</summary>
    Private Function LooksNumeric(text As String) As Boolean
        If text.Length = 0 Then Return False
        Dim i As Integer = 0
        If text(0) = "+"c OrElse text(0) = "-"c Then i = 1
        If i >= text.Length Then Return False
        If text.Length - i > 2 AndAlso text(i) = "0"c AndAlso (text(i + 1) = "x"c OrElse text(i + 1) = "X"c) Then
            For j As Integer = i + 2 To text.Length - 1
                If Not Uri.IsHexDigit(text(j)) Then Return False
            Next
            Return True
        End If
        Dim digits As Integer = 0
        While i < text.Length AndAlso (IsDigit(text(i)) OrElse (text(i) = "_"c AndAlso digits > 0))
            If text(i) <> "_"c Then digits += 1
            i += 1
        End While
        If digits = 0 Then Return False
        If i < text.Length AndAlso text(i) = "."c Then
            i += 1
            While i < text.Length AndAlso IsDigit(text(i))
                i += 1
            End While
        End If
        If i < text.Length AndAlso (text(i) = "e"c OrElse text(i) = "E"c) Then
            i += 1
            If i < text.Length AndAlso (text(i) = "+"c OrElse text(i) = "-"c) Then i += 1
            Dim exponent As Integer = 0
            While i < text.Length AndAlso IsDigit(text(i))
                exponent += 1
                i += 1
            End While
            If exponent = 0 Then Return False
        End If
        Return i = text.Length
    End Function

    Private Function IsLiteralWord(text As String) As Boolean
        Select Case text.ToLowerInvariant()
            Case "true", "false", "null", "yes", "no", "on", "off", "~"
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>The index just after the closing quote of the quoted scalar that opens at <paramref name="start"/>,
    ''' or the line's end when it is not closed. A double quote escapes with a backslash, a single quote by doubling.</summary>
    Private Function EndOfQuoted(text As String, start As Integer) As Integer
        Dim quote As Char = text(start)
        Dim i As Integer = start + 1
        While i < text.Length
            Dim c As Char = text(i)
            If quote = """"c AndAlso c = "\"c Then
                i += 2
            ElseIf c = quote Then
                If quote = "'"c AndAlso i + 1 < text.Length AndAlso text(i + 1) = "'"c Then
                    i += 2
                Else
                    Return i + 1
                End If
            Else
                i += 1
            End If
        End While
        Return text.Length
    End Function

    ' --- JSON -------------------------------------------------------------------------------------------

    Private Sub LexJson(line As String, index As Integer, spans As List(Of TextTokenSpan))
        Dim timedOut As Boolean
        Dim hits As List(Of Match) = SafeMatches(JsonToken, line, timedOut)
        If timedOut Then Return
        For Each hit As Match In hits
            Dim kind As TextTokenKind
            If hit.Groups("key").Success Then
                kind = TextTokenKind.Key
            ElseIf hit.Groups("str").Success Then
                kind = TextTokenKind.StringValue
            ElseIf hit.Groups("num").Success Then
                kind = TextTokenKind.Number
            ElseIf hit.Groups("kw").Success Then
                kind = TextTokenKind.Keyword
            Else
                kind = TextTokenKind.Comment
            End If
            Add(spans, index, hit.Index, hit.Length, kind)
        Next
    End Sub

    ' --- XML / HTML ---------------------------------------------------------------------------------------

    Private Function IsXmlNameChar(c As Char) As Boolean
        Return Char.IsLetterOrDigit(c) OrElse c = "_"c OrElse c = ":"c OrElse c = "-"c OrElse c = "."c
    End Function

    ''' <summary>One block that runs to a terminator (a comment, CDATA, a declaration): the span from
    ''' <paramref name="start"/> to the terminator, or to the line's end with the block left open. Returns the next column.</summary>
    Private Function ConsumeBlock(line As String, index As Integer, start As Integer, searchFrom As Integer, terminator As String,
                                  kind As TextTokenKind, mode As Integer, state As LexState, spans As List(Of TextTokenSpan)) As Integer
        Dim e As Integer = If(searchFrom > line.Length, -1, line.IndexOf(terminator, searchFrom, StringComparison.Ordinal))
        If e < 0 Then
            Add(spans, index, start, line.Length - start, kind)
            state.Mode = mode
            Return line.Length
        End If
        Add(spans, index, start, e + terminator.Length - start, kind)
        state.Mode = ModeText
        Return e + terminator.Length
    End Function

    ''' <summary>A quoted attribute value from <paramref name="start"/> (its first column on this line); closes at
    ''' <see cref="LexState.Quote"/> or stays open to the next line.</summary>
    Private Function ConsumeQuotedValue(line As String, index As Integer, start As Integer, searchFrom As Integer,
                                        state As LexState, spans As List(Of TextTokenSpan)) As Integer
        Dim closeAt As Integer = If(searchFrom > line.Length, -1, line.IndexOf(state.Quote, searchFrom))
        If closeAt < 0 Then
            Add(spans, index, start, line.Length - start, TextTokenKind.StringValue)
            state.Mode = XmlValue
            Return line.Length
        End If
        Add(spans, index, start, closeAt + 1 - start, TextTokenKind.StringValue)
        state.Mode = XmlTag
        Return closeAt + 1
    End Function

    Private Sub LexXml(line As String, index As Integer, state As LexState, spans As List(Of TextTokenSpan))
        Dim n As Integer = line.Length
        Dim i As Integer = 0
        While i < n
            Select Case state.Mode
                Case XmlComment
                    i = ConsumeBlock(line, index, i, i, "-->", TextTokenKind.Comment, XmlComment, state, spans)
                Case XmlCdata
                    i = ConsumeBlock(line, index, i, i, "]]>", TextTokenKind.StringValue, XmlCdata, state, spans)
                Case XmlPi
                    i = ConsumeBlock(line, index, i, i, "?>", TextTokenKind.Keyword, XmlPi, state, spans)
                Case XmlDecl
                    i = ConsumeBlock(line, index, i, i, ">", TextTokenKind.Keyword, XmlDecl, state, spans)
                Case XmlValue
                    i = ConsumeQuotedValue(line, index, i, i, state, spans)
                Case XmlTag
                    i = SkipBlanks(line, i)
                    If i >= n Then Return
                    Dim c As Char = line(i)
                    If c = ">"c Then
                        state.Mode = ModeText
                        i += 1
                    ElseIf c = """"c OrElse c = "'"c Then
                        state.Quote = c
                        i = ConsumeQuotedValue(line, index, i, i + 1, state, spans)
                    ElseIf c = "="c OrElse c = "/"c OrElse c = "?"c Then
                        i += 1
                    Else
                        Dim j As Integer = i
                        While j < n AndAlso Not IsBlank(line(j)) AndAlso line(j) <> "="c AndAlso line(j) <> ">"c AndAlso
                              line(j) <> "/"c AndAlso line(j) <> """"c AndAlso line(j) <> "'"c
                            j += 1
                        End While
                        If j = i Then
                            i += 1
                        Else
                            Add(spans, index, i, j - i, TextTokenKind.Key)
                            i = j
                        End If
                    End If
                Case Else
                    Dim lt As Integer = line.IndexOf("<"c, i)
                    If lt < 0 Then Return
                    If At(line, lt, "<!--") Then
                        i = ConsumeBlock(line, index, lt, lt + 4, "-->", TextTokenKind.Comment, XmlComment, state, spans)
                    ElseIf At(line, lt, "<![CDATA[") Then
                        i = ConsumeBlock(line, index, lt, lt + 9, "]]>", TextTokenKind.StringValue, XmlCdata, state, spans)
                    ElseIf At(line, lt, "<?") Then
                        i = ConsumeBlock(line, index, lt, lt + 2, "?>", TextTokenKind.Keyword, XmlPi, state, spans)
                    ElseIf At(line, lt, "<!") Then
                        i = ConsumeBlock(line, index, lt, lt + 2, ">", TextTokenKind.Keyword, XmlDecl, state, spans)
                    Else
                        Dim nameStart As Integer = lt + 1
                        If nameStart < n AndAlso line(nameStart) = "/"c Then nameStart += 1
                        Dim nameEnd As Integer = nameStart
                        While nameEnd < n AndAlso IsXmlNameChar(line(nameEnd))
                            nameEnd += 1
                        End While
                        If nameEnd = nameStart Then
                            i = lt + 1
                        Else
                            Add(spans, index, lt, nameEnd - lt, TextTokenKind.Keyword)
                            state.Mode = XmlTag
                            i = nameEnd
                        End If
                    End If
            End Select
        End While
    End Sub

    ' --- YAML ---------------------------------------------------------------------------------------------

    ''' <summary>The first `#` of a comment: one that starts the text or follows a blank. -1 when there is none.</summary>
    Private Function FindCommentStart(text As String, start As Integer) As Integer
        For i As Integer = start To text.Length - 1
            If text(i) = "#"c AndAlso (i = 0 OrElse IsBlank(text(i - 1))) Then Return i
        Next
        Return -1
    End Function

    ''' <summary>The column of the colon that ends a mapping key starting at <paramref name="start"/>, or -1 when this
    ''' is not a "key: value" line. A colon counts only before a blank or the line's end, so a URL is not a key.</summary>
    Private Function FindYamlKeyColon(line As String, start As Integer) As Integer
        Dim n As Integer = line.Length
        Dim c As Char = line(start)
        If c = "["c OrElse c = "{"c OrElse c = "|"c OrElse c = ">"c OrElse c = "#"c Then Return -1
        If c = """"c OrElse c = "'"c Then
            Dim after As Integer = SkipBlanks(line, EndOfQuoted(line, start))
            If after < n AndAlso line(after) = ":"c AndAlso (after + 1 >= n OrElse IsBlank(line(after + 1))) Then Return after
            Return -1
        End If
        For i As Integer = start To n - 1
            Dim ch As Char = line(i)
            If ch = "#"c AndAlso i > start AndAlso IsBlank(line(i - 1)) Then Return -1
            If ch = ":"c AndAlso (i + 1 >= n OrElse IsBlank(line(i + 1))) Then Return If(i > start, i, -1)
        Next
        Return -1
    End Function

    Private Sub LexYaml(line As String, index As Integer, state As LexState, spans As List(Of TextTokenSpan))
        Dim n As Integer = line.Length
        Dim indent As Integer = SkipBlanks(line, 0)
        If indent >= n Then Return
        If state.Mode = ModeBlockScalar Then
            If indent > state.Indent Then
                AddTrimmed(spans, index, line, TextTokenKind.StringValue)
                Return
            End If
            state.Mode = ModeText
        End If
        If line(indent) = "#"c Then
            Add(spans, index, indent, n - indent, TextTokenKind.Comment)
            Return
        End If
        If indent = 0 AndAlso (At(line, 0, "---") OrElse At(line, 0, "...")) AndAlso (n = 3 OrElse IsBlank(line(3))) Then
            Add(spans, index, 0, 3, TextTokenKind.Keyword)
            LexYamlValue(line, index, 3, 0, state, spans)
            Return
        End If
        Dim pos As Integer = indent
        While pos < n AndAlso line(pos) = "-"c AndAlso (pos + 1 >= n OrElse IsBlank(line(pos + 1)))
            Add(spans, index, pos, 1, TextTokenKind.Keyword)
            pos = SkipBlanks(line, pos + 1)
        End While
        If pos >= n Then Return
        Dim colon As Integer = FindYamlKeyColon(line, pos)
        If colon >= 0 Then
            Dim keyEnd As Integer = colon
            While keyEnd > pos AndAlso IsBlank(line(keyEnd - 1))
                keyEnd -= 1
            End While
            Add(spans, index, pos, keyEnd - pos, TextTokenKind.Key)
            LexYamlValue(line, index, colon + 1, pos, state, spans)
        Else
            LexYamlValue(line, index, pos, indent, state, spans)
        End If
    End Sub

    ''' <summary>The value part of a YAML line from <paramref name="start"/>: quoted scalars, block-scalar headers,
    ''' anchors, aliases and tags, flow collections (left plain), numbers, literals and a trailing comment.
    ''' <paramref name="parentIndent"/> is the column the block scalar's lines must be indented past.</summary>
    Private Sub LexYamlValue(line As String, index As Integer, start As Integer, parentIndent As Integer,
                             state As LexState, spans As List(Of TextTokenSpan))
        Dim n As Integer = line.Length
        Dim p As Integer = SkipBlanks(line, start)
        While p < n
            Dim c As Char = line(p)
            If c = "#"c Then
                Add(spans, index, p, n - p, TextTokenKind.Comment)
                Return
            End If
            If c = """"c OrElse c = "'"c Then
                Dim e As Integer = EndOfQuoted(line, p)
                Add(spans, index, p, e - p, TextTokenKind.StringValue)
                p = SkipBlanks(line, e)
                If p < n AndAlso line(p) <> "#"c Then Return
                Continue While
            End If
            If c = "|"c OrElse c = ">"c Then
                Dim e As Integer = p
                While e < n AndAlso Not IsBlank(line(e))
                    e += 1
                End While
                Add(spans, index, p, e - p, TextTokenKind.Keyword)
                state.Mode = ModeBlockScalar
                state.Indent = parentIndent
                p = SkipBlanks(line, e)
                Continue While
            End If
            If c = "&"c OrElse c = "*"c OrElse c = "!"c Then
                Dim e As Integer = p
                While e < n AndAlso Not IsBlank(line(e))
                    e += 1
                End While
                Add(spans, index, p, e - p, TextTokenKind.Keyword)
                p = SkipBlanks(line, e)
                Continue While
            End If
            If c = "["c OrElse c = "{"c Then
                Dim flowComment As Integer = FindCommentStart(line, p)
                If flowComment >= 0 Then Add(spans, index, flowComment, n - flowComment, TextTokenKind.Comment)
                Return
            End If
            Dim commentAt As Integer = FindCommentStart(line, p)
            Dim valueEnd As Integer = If(commentAt >= 0, commentAt, n)
            While valueEnd > p AndAlso IsBlank(line(valueEnd - 1))
                valueEnd -= 1
            End While
            Dim text As String = line.Substring(p, valueEnd - p)
            Dim kind As TextTokenKind = If(LooksNumeric(text), TextTokenKind.Number,
                If(IsLiteralWord(text), TextTokenKind.Keyword, TextTokenKind.StringValue))
            Add(spans, index, p, valueEnd - p, kind)
            If commentAt >= 0 Then Add(spans, index, commentAt, n - commentAt, TextTokenKind.Comment)
            Return
        End While
    End Sub

    ' --- Markdown -----------------------------------------------------------------------------------------

    Private Function IsBlankFrom(text As String, start As Integer) As Boolean
        Return SkipBlanks(text, start) >= text.Length
    End Function

    Private Function IsThematicBreak(line As String, start As Integer) As Boolean
        Dim ch As Char = line(start)
        If ch <> "-"c AndAlso ch <> "*"c AndAlso ch <> "_"c Then Return False
        Dim count As Integer = 0
        For i As Integer = start To line.Length - 1
            If line(i) = ch Then
                count += 1
            ElseIf Not IsBlank(line(i)) Then
                Return False
            End If
        Next
        Return count >= 3
    End Function

    ''' <summary>The start of the next backtick run of exactly <paramref name="run"/> characters, or -1.</summary>
    Private Function FindBacktickClose(line As String, fromIndex As Integer, run As Integer) As Integer
        Dim i As Integer = fromIndex
        While i < line.Length
            If line(i) = "`"c Then
                Dim length As Integer = CountRun(line, i, "`"c)
                If length = run Then Return i
                i += length
            Else
                i = line.IndexOf("`"c, i)
                If i < 0 Then Return -1
            End If
        End While
        Return -1
    End Function

    Private Sub LexMarkdown(line As String, index As Integer, state As LexState, spans As List(Of TextTokenSpan))
        Dim n As Integer = line.Length
        Dim p As Integer = 0
        While p < n AndAlso p < 4 AndAlso line(p) = " "c
            p += 1
        End While
        If state.Mode = ModeFence Then
            Dim closing As Integer = CountRun(line, p, state.FenceChar)
            If p < 4 AndAlso closing >= state.FenceLength AndAlso IsBlankFrom(line, p + closing) Then
                Add(spans, index, p, closing, TextTokenKind.Keyword)
                state.Mode = ModeText
            Else
                AddTrimmed(spans, index, line, TextTokenKind.StringValue)
            End If
            Return
        End If
        If p >= n Then Return
        Dim c As Char = line(p)
        If p < 4 Then
            If c = "`"c OrElse c = "~"c Then
                Dim run As Integer = CountRun(line, p, c)
                If run >= 3 AndAlso (c = "~"c OrElse line.IndexOf("`"c, p + run) < 0) Then
                    Add(spans, index, p, n - p, TextTokenKind.Keyword)
                    state.Mode = ModeFence
                    state.FenceChar = c
                    state.FenceLength = run
                    Return
                End If
            End If
            If c = "#"c Then
                Dim hashes As Integer = CountRun(line, p, "#"c)
                If hashes <= 6 AndAlso (p + hashes >= n OrElse IsBlank(line(p + hashes))) Then
                    AddTrimmed(spans, index, line, TextTokenKind.Heading)
                    Return
                End If
            End If
            If IsThematicBreak(line, p) Then
                AddTrimmed(spans, index, line, TextTokenKind.Keyword)
                Return
            End If
        End If
        While p < n AndAlso line(p) = ">"c
            Add(spans, index, p, 1, TextTokenKind.Keyword)
            p += 1
            If p < n AndAlso line(p) = " "c Then p += 1
        End While
        If p < n Then
            c = line(p)
            If (c = "-"c OrElse c = "*"c OrElse c = "+"c) AndAlso (p + 1 >= n OrElse IsBlank(line(p + 1))) Then
                Add(spans, index, p, 1, TextTokenKind.Keyword)
                p += 1
            ElseIf IsDigit(c) Then
                Dim digits As Integer = 0
                While p + digits < n AndAlso IsDigit(line(p + digits)) AndAlso digits < 10
                    digits += 1
                End While
                Dim mark As Integer = p + digits
                If digits < 10 AndAlso mark < n AndAlso (line(mark) = "."c OrElse line(mark) = ")"c) AndAlso
                   (mark + 1 >= n OrElse IsBlank(line(mark + 1))) Then
                    Add(spans, index, p, digits + 1, TextTokenKind.Keyword)
                    p = mark + 1
                End If
            End If
        End If
        LexMarkdownInline(line, index, p, spans)
    End Sub

    ''' <summary>Code spans, links (text, then address), images and autolinks of one line. A search that fails once
    ''' fails for every later position of the line, so it is not repeated (the scan stays linear-ish).</summary>
    Private Sub LexMarkdownInline(line As String, index As Integer, start As Integer, spans As List(Of TextTokenSpan))
        Dim n As Integer = line.Length
        Dim i As Integer = start
        Dim failedRuns As Integer = 0
        Dim noBracket As Boolean = False
        Dim noParen As Boolean = False
        While i < n
            Dim c As Char = line(i)
            If c = "\"c Then
                i += 2
            ElseIf c = "`"c Then
                Dim run As Integer = CountRun(line, i, "`"c)
                Dim bit As Integer = 1 << Math.Min(run, 30)
                Dim closeAt As Integer = -1
                If (failedRuns And bit) = 0 Then
                    closeAt = FindBacktickClose(line, i + run, run)
                    If closeAt < 0 Then failedRuns = failedRuns Or bit
                End If
                If closeAt >= 0 Then
                    Add(spans, index, i, closeAt + run - i, TextTokenKind.StringValue)
                    i = closeAt + run
                Else
                    i += run
                End If
            ElseIf Not noBracket AndAlso (c = "["c OrElse (c = "!"c AndAlso i + 1 < n AndAlso line(i + 1) = "["c)) Then
                Dim openAt As Integer = If(c = "["c, i, i + 1)
                Dim closeBracket As Integer = line.IndexOf("]"c, openAt + 1)
                If closeBracket < 0 Then
                    noBracket = True
                    i += 1
                ElseIf Not noParen AndAlso closeBracket + 1 < n AndAlso line(closeBracket + 1) = "("c Then
                    Dim closeParen As Integer = line.IndexOf(")"c, closeBracket + 2)
                    If closeParen < 0 Then
                        noParen = True
                        i += 1
                    Else
                        Add(spans, index, i, closeBracket + 1 - i, TextTokenKind.Key)
                        Add(spans, index, closeBracket + 1, closeParen + 1 - (closeBracket + 1), TextTokenKind.Comment)
                        i = closeParen + 1
                    End If
                Else
                    i += 1
                End If
            ElseIf c = "<"c AndAlso (At(line, i + 1, "http://") OrElse At(line, i + 1, "https://") OrElse At(line, i + 1, "mailto:")) Then
                Dim gt As Integer = line.IndexOf(">"c, i + 1)
                If gt < 0 Then
                    i += 1
                Else
                    Add(spans, index, i, gt + 1 - i, TextTokenKind.Key)
                    i = gt + 1
                End If
            Else
                i += 1
            End If
        End While
    End Sub

    ' --- INI / TOML / conf --------------------------------------------------------------------------------

    Private Sub LexIni(line As String, index As Integer, spans As List(Of TextTokenSpan))
        Dim n As Integer = line.Length
        Dim p As Integer = SkipBlanks(line, 0)
        If p >= n Then Return
        Dim c As Char = line(p)
        If c = ";"c OrElse c = "#"c Then
            Add(spans, index, p, n - p, TextTokenKind.Comment)
            Return
        End If
        If c = "["c Then
            Dim closeAt As Integer = line.IndexOf("]"c, p + 1)
            If closeAt >= 0 Then
                While closeAt + 1 < n AndAlso line(closeAt + 1) = "]"c
                    closeAt += 1
                End While
                Add(spans, index, p, closeAt + 1 - p, TextTokenKind.Heading)
                Dim rest As Integer = SkipBlanks(line, closeAt + 1)
                If rest < n AndAlso (line(rest) = ";"c OrElse line(rest) = "#"c) Then Add(spans, index, rest, n - rest, TextTokenKind.Comment)
                Return
            End If
        End If
        Dim sep As Integer = line.IndexOf("="c, p)
        If sep < 0 Then
            Dim colon As Integer = line.IndexOf(":"c, p)
            If colon > p AndAlso line.IndexOfAny(New Char() {" "c, ControlChars.Tab}, p, colon - p) < 0 Then sep = colon
        End If
        If sep <= p Then Return
        Dim keyEnd As Integer = sep
        While keyEnd > p AndAlso IsBlank(line(keyEnd - 1))
            keyEnd -= 1
        End While
        Add(spans, index, p, keyEnd - p, TextTokenKind.Key)

        Dim v As Integer = SkipBlanks(line, sep + 1)
        If v >= n Then Return
        If line(v) = """"c OrElse line(v) = "'"c Then
            Dim e As Integer = EndOfQuoted(line, v)
            Add(spans, index, v, e - v, TextTokenKind.StringValue)
            Dim after As Integer = SkipBlanks(line, e)
            If after < n AndAlso (line(after) = ";"c OrElse line(after) = "#"c) Then Add(spans, index, after, n - after, TextTokenKind.Comment)
            Return
        End If
        Dim commentAt As Integer = -1
        For i As Integer = v To n - 1
            If (line(i) = ";"c OrElse line(i) = "#"c) AndAlso (i = v OrElse IsBlank(line(i - 1))) Then
                commentAt = i
                Exit For
            End If
        Next
        Dim valueEnd As Integer = If(commentAt >= 0, commentAt, n)
        While valueEnd > v AndAlso IsBlank(line(valueEnd - 1))
            valueEnd -= 1
        End While
        If valueEnd > v Then
            Dim text As String = line.Substring(v, valueEnd - v)
            If LooksNumeric(text) Then
                Add(spans, index, v, valueEnd - v, TextTokenKind.Number)
            ElseIf IsLiteralWord(text) Then
                Add(spans, index, v, valueEnd - v, TextTokenKind.Keyword)
            End If
        End If
        If commentAt >= 0 Then Add(spans, index, commentAt, n - commentAt, TextTokenKind.Comment)
    End Sub

    ' --- logs ---------------------------------------------------------------------------------------------

    Private Function LevelKind(word As String) As TextTokenKind
        Select Case word.ToUpperInvariant()
            Case "ERROR", "ERR", "FATAL", "FTL", "CRITICAL", "SEVERE"
                Return TextTokenKind.ErrorLevel
            Case "WARN", "WARNING", "WRN"
                Return TextTokenKind.WarningLevel
            Case Else
                Return TextTokenKind.Keyword
        End Select
    End Function

    Private Sub LexLog(line As String, index As Integer, spans As List(Of TextTokenSpan))
        Dim n As Integer = line.Length
        Dim p As Integer = SkipBlanks(line, 0)
        If p > 0 AndAlso At(line, p, "at ") Then
            Add(spans, index, p, n - p, TextTokenKind.Comment)
            Return
        End If
        Dim timedOut As Boolean
        Dim stampEnd As Integer = 0
        Dim stamp As Match = SafeFirst(LogStamp, line, 0, timedOut)
        If timedOut Then Return
        Dim levelFrom As Integer = 0
        If stamp IsNot Nothing Then
            Dim ts As Group = stamp.Groups("ts")
            Add(spans, index, ts.Index, ts.Length, TextTokenKind.Number)
            stampEnd = ts.Index + ts.Length
            levelFrom = stampEnd
        End If
        Dim level As Match = SafeFirst(LogLevel, line, levelFrom, timedOut)
        If timedOut Then
            If spans.Count > 0 AndAlso spans(spans.Count - 1).Line = index Then spans.RemoveAt(spans.Count - 1)
            Return
        End If
        If level IsNot Nothing AndAlso level.Index <= stampEnd + 160 Then
            Dim word As Group = level.Groups("lvl")
            Add(spans, index, word.Index, word.Length, LevelKind(word.Value))
        End If
    End Sub

End Module
#End If
