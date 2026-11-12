#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Text

' Specification 277 step P3.7 (decision D07): the pure half of the rendered Markdown view. A Markdown source
' becomes ONE static HTML document with the reader theme as its only CSS; Main_Form.TextMarkdown.vb hands that
' page to the HTML host of ticket 275 (its sanitizer and its request filter are the second line of defence).
' No WinForms reference and no dependency: a hand-written CommonMark-common subset.
'
' What the page can contain is decided here, by construction, not by a filter afterwards:
'  - every piece of source text goes through EscapeHtml; raw HTML in the source is shown as text, never passed;
'  - a link keeps only http, https and mailto (and a same-page #fragment); anything else is shown as its text;
'  - an image is its alt text (a link to the picture when the address is http or https), never an img element;
'  - the only elements written are the ones in this file, with the attributes dir, charset, id, class, href,
'    title, rel and start - no script, no style attribute, no event handler, no form;
'  - font names are checked against a short allow-list of characters and the colours are computed hex values.
' A pathological input stays bounded: the source is cut at MaxSourceChars, block nesting stops at MaxBlockDepth,
' every look-ahead draws on one step budget per document, and the number of emphasis marks is capped per
' paragraph (so the element tree never nears the 512 levels the 275 sanitizer accepts).

''' <summary>What the rendered page is drawn with: the reader theme, the reader fonts and the title.</summary>
Public NotInheritable Class TextMarkdownOptions
    ''' <summary>A stored TextTheme: system, light, dark, sepia or black (anything else reads as system).</summary>
    Public Property Theme As String = "system"
    ''' <summary>The colours "system" follows - the viewer's own surface and its contrasting ink.</summary>
    Public Property SystemBack As Color = Color.White
    Public Property SystemInk As Color = Color.Black
    Public Property Title As String = ""
    Public Property FontFamily As String = "Segoe UI"
    Public Property CodeFontFamily As String = "Consolas"
    Public Property FontSizePoints As Single = 12.0F
    Public Property LineSpacing As Single = 1.2F
End Class

Public Module TextMarkdownRenderer

    ''' <summary>The source is cut here; <see cref="IsRenderable"/> says a file is over it.</summary>
    Public Const MaxSourceChars As Integer = 1000000
    Public Const MaxBlockDepth As Integer = 12
    Public Const MaxInlineDepth As Integer = 4
    Public Const MaxDelimiters As Integer = 2000
    Public Const MaxEmphasisMatches As Integer = 64
    ''' <summary>Look-ahead steps (bracket, code span, destination scans) one document may spend.</summary>
    Public Const MaxWorkSteps As Integer = 3000000
    Public Const MaxUrlChars As Integer = 2048
    Public Const MaxHeadingIds As Integer = 5000
    Public Const MaxDefinitions As Integer = 5000
    Public Const MaxTableColumns As Integer = 64
    ''' <summary>The rendered body stops here, about the 16 MB the HTML host takes: a small source can ask for far more (a
    ''' table pads every short row), and only the size of the page says so.</summary>
    Public Const MaxPageChars As Integer = 16000000
    Private Const MaxTitleChars As Integer = 200
    Private Const MaxLinkTextChars As Integer = 10000
    Private Const MaxSlugChars As Integer = 80

    Private NotInheritable Class RenderState
        Public Work As Integer = MaxWorkSteps
        Public PageTooLarge As Boolean
        Public ReadOnly Definitions As New Dictionary(Of String, LinkDefinition)(StringComparer.Ordinal)
        Public ReadOnly Ids As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    End Class

    Private NotInheritable Class LinkDefinition
        Public Target As String = ""
        Public Title As String = ""
    End Class

    Private NotInheritable Class ListMarker
        Public IsOrdered As Boolean
        Public Mark As Char
        Public Number As Integer
        Public ContentIndent As Integer
        Public First As String = ""
    End Class

    Private NotInheritable Class Tok
        Public Html As String = ""
        Public IsDelim As Boolean
        Public Ch As Char
        Public Count As Integer
        Public OrigCount As Integer
        Public CanOpen As Boolean
        Public CanClose As Boolean
        Public Dead As Boolean
        Public Before As String = ""
        Public After As String = ""
    End Class

    Private NotInheritable Class PageColors
        Public Back As String = ""
        Public Ink As String = ""
        Public Muted As String = ""
        Public Link As String = ""
        Public CodeBack As String = ""
        Public Rule As String = ""
        Public IsDark As Boolean
    End Class

    ' ---- the public surface ---------------------------------------------------------------------

    ''' <summary>.md and .markdown, the two extensions the rendered view is offered for (leading dot, any case).</summary>
    Public Function IsMarkdownExtension(extension As String) As Boolean
        If String.IsNullOrEmpty(extension) Then Return False
        Return String.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>False when the source is over <see cref="MaxSourceChars"/>: the caller keeps the source reader.</summary>
    Public Function IsRenderable(markdown As String) As Boolean
        Return markdown Is Nothing OrElse markdown.Length <= MaxSourceChars
    End Function

    Public Function Render(markdown As String, theme As String) As String
        Return Render(markdown, New TextMarkdownOptions With {.Theme = theme})
    End Function

    ''' <summary>The whole page: doctype, head with the theme CSS, and the article holding the rendered blocks. Nothing when
    ''' the body would pass <see cref="MaxPageChars"/>: the caller keeps the source reader.</summary>
    Public Function Render(markdown As String, options As TextMarkdownOptions) As String
        Dim opts As TextMarkdownOptions = If(options, New TextMarkdownOptions())
        Dim state As New RenderState()
        Dim lines As List(Of String) = PrepareLines(markdown)
        CollectDefinitions(lines, state)
        Dim body As New StringBuilder()
        ParseBlocks(lines, 0, state, body, False)
        If state.PageTooLarge Then Return Nothing
        Return Page(opts, body.ToString())
    End Function

    ''' <summary>
    ''' The target a link may carry, or "" when it may not: http and https with a host and no user-info, mailto,
    ''' and a same-page #fragment. Anything with a control character, a space or a backslash, a relative path,
    ''' a protocol-relative address and every other scheme (javascript, data, file, vbscript ..) gives "".
    ''' The result is NOT attribute-escaped; it goes through <see cref="EscapeHtml"/> where it is written.
    ''' </summary>
    Public Function SafeLinkTarget(target As String) As String
        If target Is Nothing Then Return ""
        Dim t As String = target.Trim()
        If t.Length = 0 OrElse t.Length > MaxUrlChars Then Return ""
        For Each ch As Char In t
            Dim code As Integer = AscW(ch)
            If code <= &H20 OrElse (code >= &H7F AndAlso code <= &H9F) OrElse ch = "\"c OrElse code = &H2028 OrElse code = &H2029 Then Return ""
        Next
        If t(0) = "#"c Then Return If(t.Length > 1, t, "")
        Dim colon As Integer = t.IndexOf(":"c)
        If colon <= 0 Then Return ""
        Dim scheme As String = t.Substring(0, colon)
        If AsciiEquals(scheme, "http") OrElse AsciiEquals(scheme, "https") Then
            If t.Length < colon + 4 OrElse t(colon + 1) <> "/"c OrElse t(colon + 2) <> "/"c Then Return ""
            Dim authorityStart As Integer = colon + 3
            Dim authorityEnd As Integer = t.IndexOfAny(New Char() {"/"c, "?"c, "#"c}, authorityStart)
            If authorityEnd < 0 Then authorityEnd = t.Length
            If authorityEnd = authorityStart Then Return ""
            If t.IndexOf("@"c, authorityStart, authorityEnd - authorityStart) >= 0 Then Return ""
            Return t
        End If
        If AsciiEquals(scheme, "mailto") Then Return If(t.Length > colon + 1, t, "")
        Return ""
    End Function

    ''' <summary>Text for an HTML text node or a double-quoted attribute value: &amp; &lt; &gt; &quot; and the apostrophe.</summary>
    Public Function EscapeHtml(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Dim sb As New StringBuilder(text.Length + 16)
        AppendEscaped(sb, text)
        Return sb.ToString()
    End Function

    ' ---- text helpers ---------------------------------------------------------------------------

    Private Sub AppendEscaped(sb As StringBuilder, text As String)
        For i As Integer = 0 To text.Length - 1
            AppendEscapedChar(sb, text(i))
        Next
    End Sub

    Private Sub AppendEscapedChar(sb As StringBuilder, c As Char)
        Select Case c
            Case "&"c
                sb.Append("&amp;")
            Case "<"c
                sb.Append("&lt;")
            Case ">"c
                sb.Append("&gt;")
            Case """"c
                sb.Append("&quot;")
            Case "'"c
                sb.Append("&#39;")
            Case Else
                sb.Append(c)
        End Select
    End Sub

    Private Function AsciiEquals(text As String, lowerWord As String) As Boolean
        If text Is Nothing OrElse text.Length <> lowerWord.Length Then Return False
        For i As Integer = 0 To text.Length - 1
            Dim c As Char = text(i)
            If c >= "A"c AndAlso c <= "Z"c Then c = ChrW(AscW(c) + 32)
            If c <> lowerWord(i) Then Return False
        Next
        Return True
    End Function

    Private Function IsAsciiPunctuation(c As Char) As Boolean
        Dim code As Integer = AscW(c)
        Return (code >= 33 AndAlso code <= 47) OrElse (code >= 58 AndAlso code <= 64) OrElse
               (code >= 91 AndAlso code <= 96) OrElse (code >= 123 AndAlso code <= 126)
    End Function

    Private Function IsAsciiDigit(c As Char) As Boolean
        Return c >= "0"c AndAlso c <= "9"c
    End Function

    Private Function IsPunctuation(c As Char) As Boolean
        Return Char.IsPunctuation(c) OrElse Char.IsSymbol(c)
    End Function

    Private Function LeadingSpaces(line As String) As Integer
        Dim n As Integer = 0
        While n < line.Length AndAlso line(n) = " "c
            n += 1
        End While
        Return n
    End Function

    Private Function IsBlank(line As String) As Boolean
        For i As Integer = 0 To line.Length - 1
            If line(i) <> " "c AndAlso line(i) <> ControlChars.Tab Then Return False
        Next
        Return True
    End Function

    Private Function CountRun(text As String, start As Integer, c As Char) As Integer
        Dim n As Integer = 0
        While start + n < text.Length AndAlso text(start + n) = c
            n += 1
        End While
        Return n
    End Function

    Private Function NormalizeLabel(label As String) As String
        Dim sb As New StringBuilder(label.Length)
        Dim pendingSpace As Boolean = False
        For Each c As Char In label.Trim()
            If Char.IsWhiteSpace(c) Then
                pendingSpace = True
            Else
                If pendingSpace AndAlso sb.Length > 0 Then sb.Append(" "c)
                pendingSpace = False
                sb.Append(c)
            End If
        Next
        Return sb.ToString().ToLowerInvariant()
    End Function

    ' ---- entities -------------------------------------------------------------------------------

    Private ReadOnly NamedEntities As New Dictionary(Of String, Integer)(StringComparer.Ordinal) From {
        {"amp", &H26}, {"lt", &H3C}, {"gt", &H3E}, {"quot", &H22}, {"apos", &H27}, {"nbsp", &HA0},
        {"copy", &HA9}, {"reg", &HAE}, {"trade", &H2122}, {"hellip", &H2026}, {"mdash", &H2014},
        {"ndash", &H2013}, {"laquo", &HAB}, {"raquo", &HBB}, {"lsquo", &H2018}, {"rsquo", &H2019},
        {"ldquo", &H201C}, {"rdquo", &H201D}, {"bull", &H2022}, {"middot", &HB7}, {"times", &HD7},
        {"deg", &HB0}, {"euro", &H20AC}, {"larr", &H2190}, {"rarr", &H2192}, {"uarr", &H2191},
        {"darr", &H2193}, {"ne", &H2260}, {"le", &H2264}, {"ge", &H2265}, {"plusmn", &HB1}}

    ''' <summary>One character reference at <paramref name="p"/> (a numeric one or one of the named ones above).</summary>
    Private Function TryDecodeEntity(text As String, p As Integer, ByRef decoded As String, ByRef nextIndex As Integer) As Boolean
        decoded = ""
        nextIndex = p + 1
        Dim limit As Integer = Math.Min(text.Length - 1, p + 12)
        Dim semi As Integer = -1
        For q As Integer = p + 1 To limit
            If text(q) = ";"c Then
                semi = q
                Exit For
            End If
        Next
        If semi < p + 2 Then Return False
        Dim name As String = text.Substring(p + 1, semi - p - 1)
        Dim code As Integer
        If name(0) = "#"c Then
            If name.Length < 2 Then Return False
            Dim hex As Boolean = name(1) = "x"c OrElse name(1) = "X"c
            Dim digits As String = name.Substring(If(hex, 2, 1))
            If digits.Length = 0 OrElse digits.Length > If(hex, 6, 7) Then Return False
            If Not Integer.TryParse(digits, If(hex, NumberStyles.AllowHexSpecifier, NumberStyles.None), CultureInfo.InvariantCulture, code) Then Return False
        ElseIf Not NamedEntities.TryGetValue(name, code) Then
            Return False
        End If
        decoded = CodePointText(code)
        nextIndex = semi + 1
        Return True
    End Function

    Private Function CodePointText(code As Integer) As String
        If code <= 0 OrElse code > &H10FFFF OrElse (code >= &HD800 AndAlso code <= &HDFFF) Then Return ChrW(&HFFFD)
        If code < &H20 AndAlso code <> 9 AndAlso code <> 10 Then Return ChrW(&HFFFD)
        If code >= &H7F AndAlso code <= &H9F Then Return ChrW(&HFFFD)
        Return Char.ConvertFromUtf32(code)
    End Function

    ''' <summary>Backslash escapes and character references of a link destination or title.</summary>
    Private Function UnescapeAndDecode(text As String) As String
        If text.IndexOf("\"c) < 0 AndAlso text.IndexOf("&"c) < 0 Then Return text
        Dim sb As New StringBuilder(text.Length)
        Dim p As Integer = 0
        While p < text.Length
            Dim c As Char = text(p)
            If c = "\"c AndAlso p + 1 < text.Length AndAlso IsAsciiPunctuation(text(p + 1)) Then
                sb.Append(text(p + 1))
                p += 2
            ElseIf c = "&"c Then
                Dim decoded As String = ""
                Dim nextIndex As Integer = 0
                If TryDecodeEntity(text, p, decoded, nextIndex) Then
                    sb.Append(decoded)
                    p = nextIndex
                Else
                    sb.Append(c)
                    p += 1
                End If
            Else
                sb.Append(c)
                p += 1
            End If
        End While
        Return sb.ToString()
    End Function

    ''' <summary>The tags of already-built inline HTML removed, the text still escaped (an image's alt).</summary>
    Private Function StripTags(html As String) As String
        If html.IndexOf("<"c) < 0 Then Return html
        Dim sb As New StringBuilder(html.Length)
        Dim inTag As Boolean = False
        For Each c As Char In html
            If inTag Then
                If c = ">"c Then inTag = False
            ElseIf c = "<"c Then
                inTag = True
            Else
                sb.Append(c)
            End If
        Next
        Return sb.ToString()
    End Function

    Private Function DecodeBasic(escaped As String) As String
        Return escaped.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", """").Replace("&#39;", "'").Replace("&amp;", "&")
    End Function

    ' ---- preparation ----------------------------------------------------------------------------

    Private Function PrepareLines(markdown As String) As List(Of String)
        Dim text As String = If(markdown, "")
        If text.Length > MaxSourceChars Then text = text.Substring(0, MaxSourceChars)
        If text.Length > 0 AndAlso text(0) = ChrW(&HFEFF) Then text = text.Substring(1)
        text = text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Replace(ChrW(0), ChrW(&HFFFD))
        Dim result As New List(Of String)()
        For Each raw As String In text.Split(ControlChars.Lf)
            result.Add(ExpandTabs(raw))
        Next
        Return result
    End Function

    Private Function ExpandTabs(line As String) As String
        If line.IndexOf(ControlChars.Tab) < 0 Then Return line
        Dim sb As New StringBuilder(line.Length + 8)
        For Each c As Char In line
            If c = ControlChars.Tab Then
                sb.Append(" "c, 4 - (sb.Length Mod 4))
            Else
                sb.Append(c)
            End If
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Link reference definitions outside code fences, taken out of the line list (their lines go blank).</summary>
    Private Sub CollectDefinitions(lines As List(Of String), state As RenderState)
        Dim fenceChar As Char = ControlChars.NullChar
        Dim fenceLen As Integer = 0
        Dim afterBlock As Boolean = True
        For i As Integer = 0 To lines.Count - 1
            Dim line As String = lines(i)
            If fenceLen > 0 Then
                If IsFenceClose(line, fenceChar, fenceLen) Then fenceLen = 0
                afterBlock = False
                Continue For
            End If
            Dim fenceIndent As Integer = 0
            Dim openChar As Char = ControlChars.NullChar
            Dim openLen As Integer = 0
            Dim info As String = ""
            If TryFenceOpen(line, fenceIndent, openChar, openLen, info) Then
                fenceChar = openChar
                fenceLen = openLen
                afterBlock = False
                Continue For
            End If
            If IsBlank(line) Then
                afterBlock = True
                Continue For
            End If
            Dim label As String = ""
            Dim target As String = ""
            Dim title As String = ""
            ' A definition cannot interrupt a paragraph: it starts a block or follows another definition.
            If afterBlock AndAlso state.Definitions.Count < MaxDefinitions AndAlso TryDefinition(line, label, target, title) Then
                Dim key As String = NormalizeLabel(label)
                If key.Length > 0 AndAlso Not state.Definitions.ContainsKey(key) Then
                    state.Definitions(key) = New LinkDefinition With {.Target = target, .Title = title}
                End If
                lines(i) = ""
                Continue For
            End If
            afterBlock = False
        Next
    End Sub

    Private Function TryDefinition(line As String, ByRef label As String, ByRef target As String, ByRef title As String) As Boolean
        Dim indent As Integer = LeadingSpaces(line)
        If indent > 3 OrElse indent >= line.Length OrElse line(indent) <> "["c Then Return False
        Dim p As Integer = indent + 1
        Dim labelStart As Integer = p
        While p < line.Length AndAlso line(p) <> "]"c
            If line(p) = "["c Then Return False
            If line(p) = "\"c Then p += 1
            p += 1
        End While
        If p >= line.Length OrElse p = labelStart Then Return False
        label = line.Substring(labelStart, p - labelStart)
        If label.Trim().Length = 0 Then Return False
        p += 1
        If p >= line.Length OrElse line(p) <> ":"c Then Return False
        p += 1
        While p < line.Length AndAlso line(p) = " "c
            p += 1
        End While
        If p >= line.Length Then Return False
        Dim dest As String
        If line(p) = "<"c Then
            Dim close As Integer = line.IndexOf(">"c, p + 1)
            If close < 0 Then Return False
            dest = line.Substring(p + 1, close - p - 1)
            p = close + 1
        Else
            Dim start As Integer = p
            While p < line.Length AndAlso line(p) <> " "c
                p += 1
            End While
            dest = line.Substring(start, p - start)
        End If
        If dest.Length = 0 Then Return False
        While p < line.Length AndAlso line(p) = " "c
            p += 1
        End While
        title = ""
        If p < line.Length Then
            Dim quote As Char = line(p)
            Dim closing As Char
            If quote = """"c Then
                closing = """"c
            ElseIf quote = "'"c Then
                closing = "'"c
            ElseIf quote = "("c Then
                closing = ")"c
            Else
                Return False
            End If
            Dim last As Integer = line.LastIndexOf(closing)
            If last <= p Then Return False
            If Not IsBlank(line.Substring(last + 1)) Then Return False
            title = UnescapeAndDecode(line.Substring(p + 1, last - p - 1))
        End If
        target = UnescapeAndDecode(dest)
        Return True
    End Function

    ' ---- block structure ------------------------------------------------------------------------

    Private Sub ParseBlocks(lines As List(Of String), depth As Integer, state As RenderState, sb As StringBuilder, tight As Boolean)
        If depth > MaxBlockDepth Then
            AppendPlainBlock(lines, sb)
            Return
        End If
        Dim i As Integer = 0
        While i < lines.Count
            If state.PageTooLarge OrElse sb.Length > MaxPageChars Then
                state.PageTooLarge = True
                Return
            End If
            Dim line As String = lines(i)
            If IsBlank(line) Then
                i += 1
                Continue While
            End If
            Dim indent As Integer = LeadingSpaces(line)
            If indent >= 4 Then
                i = ParseIndentedCode(lines, i, sb)
                Continue While
            End If
            Dim fenceChar As Char = ControlChars.NullChar
            Dim fenceLen As Integer = 0
            Dim fenceIndent As Integer = 0
            Dim info As String = ""
            If TryFenceOpen(line, fenceIndent, fenceChar, fenceLen, info) Then
                i = ParseFence(lines, i, fenceChar, fenceLen, fenceIndent, info, sb)
                Continue While
            End If
            Dim level As Integer = 0
            Dim content As String = ""
            If TryAtx(line, level, content) Then
                AppendHeading(sb, level, content, state)
                i += 1
                Continue While
            End If
            If IsThematicBreak(line) Then
                sb.Append("<hr>").Append(vbLf)
                i += 1
                Continue While
            End If
            If line(indent) = ">"c Then
                i = ParseQuote(lines, i, depth, state, sb)
                Continue While
            End If
            Dim marker As ListMarker = TryListMarker(line)
            If marker IsNot Nothing Then
                i = ParseList(lines, i, marker, depth, state, sb)
                Continue While
            End If
            If line.IndexOf("|"c) >= 0 AndAlso i + 1 < lines.Count Then
                Dim consumed As Integer = TryParseTable(lines, i, state, sb)
                If consumed > 0 Then
                    i += consumed
                    Continue While
                End If
            End If
            i = ParseParagraph(lines, i, state, sb, tight)
        End While
    End Sub

    ''' <summary>Past the nesting bound the remaining lines are text, not structure.</summary>
    Private Sub AppendPlainBlock(lines As List(Of String), sb As StringBuilder)
        Dim wrote As Boolean = False
        For Each line As String In lines
            If IsBlank(line) Then Continue For
            If Not wrote Then
                sb.Append("<p>")
                wrote = True
            Else
                sb.Append("<br>").Append(vbLf)
            End If
            AppendEscaped(sb, line.Trim())
        Next
        If wrote Then sb.Append("</p>").Append(vbLf)
    End Sub

    Private Function TryFenceOpen(line As String, ByRef indent As Integer, ByRef fenceChar As Char, ByRef fenceLen As Integer, ByRef info As String) As Boolean
        indent = LeadingSpaces(line)
        If indent > 3 OrElse indent >= line.Length Then Return False
        Dim c As Char = line(indent)
        If c <> "`"c AndAlso c <> "~"c Then Return False
        Dim n As Integer = CountRun(line, indent, c)
        If n < 3 Then Return False
        Dim rest As String = line.Substring(indent + n).Trim()
        If c = "`"c AndAlso rest.IndexOf("`"c) >= 0 Then Return False
        fenceChar = c
        fenceLen = n
        info = rest
        Return True
    End Function

    Private Function IsFenceClose(line As String, fenceChar As Char, fenceLen As Integer) As Boolean
        Dim indent As Integer = LeadingSpaces(line)
        If indent > 3 OrElse indent >= line.Length OrElse line(indent) <> fenceChar Then Return False
        Dim n As Integer = CountRun(line, indent, fenceChar)
        If n < fenceLen Then Return False
        Return IsBlank(line.Substring(indent + n))
    End Function

    Private Function ParseFence(lines As List(Of String), start As Integer, fenceChar As Char, fenceLen As Integer,
                                fenceIndent As Integer, info As String, sb As StringBuilder) As Integer
        Dim j As Integer = start + 1
        Dim body As New StringBuilder()
        While j < lines.Count
            If IsFenceClose(lines(j), fenceChar, fenceLen) Then
                j += 1
                Exit While
            End If
            Dim l As String = lines(j)
            AppendEscaped(body, l.Substring(Math.Min(fenceIndent, LeadingSpaces(l))))
            body.Append(vbLf)
            j += 1
        End While
        Dim language As String = FenceLanguage(info)
        sb.Append("<pre><code")
        If language.Length > 0 Then sb.Append(" class=""language-").Append(language).Append("""")
        sb.Append(">").Append(body.ToString()).Append("</code></pre>").Append(vbLf)
        Return j
    End Function

    ''' <summary>The first word of the info string when it is a plain language name, else "".</summary>
    Private Function FenceLanguage(info As String) As String
        If info.Length = 0 Then Return ""
        Dim space As Integer = info.IndexOf(" "c)
        Dim word As String = If(space >= 0, info.Substring(0, space), info)
        If word.Length = 0 OrElse word.Length > 32 Then Return ""
        For Each c As Char In word
            Dim ok As Boolean = (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse IsAsciiDigit(c) OrElse
                                c = "+"c OrElse c = "-"c OrElse c = "_"c OrElse c = "."c OrElse c = "#"c
            If Not ok Then Return ""
        Next
        Return word
    End Function

    Private Function ParseIndentedCode(lines As List(Of String), start As Integer, sb As StringBuilder) As Integer
        Dim last As Integer = start
        Dim j As Integer = start
        While j < lines.Count
            If IsBlank(lines(j)) Then
                j += 1
            ElseIf LeadingSpaces(lines(j)) >= 4 Then
                last = j
                j += 1
            Else
                Exit While
            End If
        End While
        sb.Append("<pre><code>")
        For k As Integer = start To last
            Dim l As String = lines(k)
            If IsBlank(l) Then
                If l.Length > 4 Then AppendEscaped(sb, l.Substring(4))
            Else
                AppendEscaped(sb, l.Substring(4))
            End If
            sb.Append(vbLf)
        Next
        sb.Append("</code></pre>").Append(vbLf)
        Return last + 1
    End Function

    Private Function TryAtx(line As String, ByRef level As Integer, ByRef content As String) As Boolean
        Dim indent As Integer = LeadingSpaces(line)
        If indent > 3 OrElse indent >= line.Length OrElse line(indent) <> "#"c Then Return False
        Dim n As Integer = CountRun(line, indent, "#"c)
        If n > 6 Then Return False
        Dim p As Integer = indent + n
        If p < line.Length AndAlso line(p) <> " "c Then Return False
        Dim rest As String = line.Substring(p).Trim()
        Dim e As Integer = rest.Length
        While e > 0 AndAlso rest(e - 1) = "#"c
            e -= 1
        End While
        If e = 0 Then
            rest = ""
        ElseIf e < rest.Length AndAlso rest(e - 1) = " "c Then
            rest = rest.Substring(0, e).TrimEnd()
        End If
        level = n
        content = rest
        Return True
    End Function

    Private Function IsThematicBreak(line As String) As Boolean
        Dim indent As Integer = LeadingSpaces(line)
        If indent > 3 OrElse indent >= line.Length Then Return False
        Dim c As Char = line(indent)
        If c <> "-"c AndAlso c <> "*"c AndAlso c <> "_"c Then Return False
        Dim count As Integer = 0
        For k As Integer = indent To line.Length - 1
            If line(k) = c Then
                count += 1
            ElseIf line(k) <> " "c Then
                Return False
            End If
        Next
        Return count >= 3
    End Function

    ''' <summary>1 for a line of = (a level-1 underline), 2 for a line of - (level 2), else 0.</summary>
    Private Function SetextLevel(line As String) As Integer
        Dim indent As Integer = LeadingSpaces(line)
        If indent > 3 OrElse indent >= line.Length Then Return 0
        Dim c As Char = line(indent)
        If c <> "="c AndAlso c <> "-"c Then Return 0
        Dim n As Integer = CountRun(line, indent, c)
        If Not IsBlank(line.Substring(indent + n)) Then Return 0
        Return If(c = "="c, 1, 2)
    End Function

    ''' <summary>A line that ends a paragraph: blank, a fence, a heading, a rule, a quote, a non-empty list item (an ordered one only from 1).</summary>
    Private Function InterruptsParagraph(line As String) As Boolean
        If IsBlank(line) Then Return True
        Dim indent As Integer = LeadingSpaces(line)
        If indent >= 4 Then Return False
        Dim fenceChar As Char = ControlChars.NullChar
        Dim fenceLen As Integer = 0
        Dim fenceIndent As Integer = 0
        Dim info As String = ""
        If TryFenceOpen(line, fenceIndent, fenceChar, fenceLen, info) Then Return True
        Dim level As Integer = 0
        Dim content As String = ""
        If TryAtx(line, level, content) Then Return True
        If IsThematicBreak(line) Then Return True
        If line(indent) = ">"c Then Return True
        Dim marker As ListMarker = TryListMarker(line)
        If marker IsNot Nothing AndAlso marker.First.Trim().Length > 0 AndAlso (Not marker.IsOrdered OrElse marker.Number = 1) Then Return True
        Return False
    End Function

    ''' <summary>A line a paragraph can go on from (not blank, not itself a fence, heading, rule or indented code).</summary>
    Private Function CanBeParagraphText(line As String) As Boolean
        If IsBlank(line) OrElse LeadingSpaces(line) >= 4 Then Return False
        Dim fenceChar As Char = ControlChars.NullChar
        Dim fenceLen As Integer = 0
        Dim fenceIndent As Integer = 0
        Dim info As String = ""
        If TryFenceOpen(line, fenceIndent, fenceChar, fenceLen, info) Then Return False
        Dim level As Integer = 0
        Dim content As String = ""
        If TryAtx(line, level, content) Then Return False
        Return Not IsThematicBreak(line)
    End Function

    Private Function ParseQuote(lines As List(Of String), start As Integer, depth As Integer, state As RenderState, sb As StringBuilder) As Integer
        Dim inner As New List(Of String)()
        Dim j As Integer = start
        Dim paragraphOpen As Boolean = False
        While j < lines.Count
            Dim l As String = lines(j)
            Dim indent As Integer = LeadingSpaces(l)
            If indent <= 3 AndAlso indent < l.Length AndAlso l(indent) = ">"c Then
                Dim p As Integer = indent + 1
                If p < l.Length AndAlso l(p) = " "c Then p += 1
                Dim stripped As String = l.Substring(p)
                inner.Add(stripped)
                paragraphOpen = CanBeParagraphText(stripped)
            ElseIf paragraphOpen AndAlso Not IsBlank(l) AndAlso Not InterruptsParagraph(l) Then
                ' A lazy continuation: the paragraph of the quote goes on without its marker.
                inner.Add(l)
            Else
                Exit While
            End If
            j += 1
        End While
        sb.Append("<blockquote>").Append(vbLf)
        ParseBlocks(inner, depth + 1, state, sb, False)
        sb.Append("</blockquote>").Append(vbLf)
        Return j
    End Function

    Private Function TryListMarker(line As String) As ListMarker
        Dim indent As Integer = LeadingSpaces(line)
        If indent > 3 OrElse indent >= line.Length Then Return Nothing
        Dim c As Char = line(indent)
        Dim marker As New ListMarker()
        Dim p As Integer = indent
        If c = "-"c OrElse c = "+"c OrElse c = "*"c Then
            marker.Mark = c
            p += 1
        ElseIf IsAsciiDigit(c) Then
            Dim digits As Integer = CountDigits(line, indent)
            If digits > 9 Then Return Nothing
            p = indent + digits
            If p >= line.Length OrElse (line(p) <> "."c AndAlso line(p) <> ")"c) Then Return Nothing
            marker.IsOrdered = True
            marker.Mark = line(p)
            marker.Number = Integer.Parse(line.Substring(indent, digits), CultureInfo.InvariantCulture)
            p += 1
        Else
            Return Nothing
        End If
        If p < line.Length AndAlso line(p) <> " "c Then Return Nothing
        Dim spaces As Integer = 0
        While p + spaces < line.Length AndAlso line(p + spaces) = " "c
            spaces += 1
        End While
        If p + spaces >= line.Length Then
            marker.ContentIndent = p + 1
            marker.First = ""
        ElseIf spaces >= 5 Then
            marker.ContentIndent = p + 1
            marker.First = line.Substring(p + 1)
        Else
            marker.ContentIndent = p + spaces
            marker.First = line.Substring(p + spaces)
        End If
        Return marker
    End Function

    Private Function CountDigits(line As String, start As Integer) As Integer
        Dim n As Integer = 0
        While start + n < line.Length AndAlso IsAsciiDigit(line(start + n))
            n += 1
        End While
        Return n
    End Function

    Private Function SameList(a As ListMarker, b As ListMarker) As Boolean
        Return a.IsOrdered = b.IsOrdered AndAlso a.Mark = b.Mark
    End Function

    Private Function ParseList(lines As List(Of String), start As Integer, first As ListMarker, depth As Integer,
                               state As RenderState, sb As StringBuilder) As Integer
        Dim items As New List(Of List(Of String))()
        Dim loose As Boolean = False
        Dim j As Integer = start
        Dim marker As ListMarker = first
        While marker IsNot Nothing
            Dim itemLines As New List(Of String)()
            itemLines.Add(marker.First)
            Dim lastLine As String = marker.First
            Dim contentIndent As Integer = marker.ContentIndent
            Dim pendingBlank As Integer = 0
            j += 1
            While j < lines.Count
                Dim l As String = lines(j)
                If IsBlank(l) Then
                    pendingBlank += 1
                    j += 1
                    Continue While
                End If
                Dim indent As Integer = LeadingSpaces(l)
                If indent >= contentIndent Then
                    For b As Integer = 1 To pendingBlank
                        itemLines.Add("")
                    Next
                    pendingBlank = 0
                    lastLine = l.Substring(contentIndent)
                    itemLines.Add(lastLine)
                    j += 1
                ElseIf pendingBlank = 0 AndAlso CanBeParagraphText(lastLine) AndAlso Not InterruptsParagraph(l) AndAlso TryListMarker(l) Is Nothing Then
                    ' A lazy continuation of the item's paragraph.
                    lastLine = l.TrimStart(" "c)
                    itemLines.Add(lastLine)
                    j += 1
                Else
                    Exit While
                End If
            End While
            items.Add(itemLines)
            If ItemHasLooseBlank(itemLines) Then loose = True
            marker = Nothing
            If j < lines.Count AndAlso Not IsThematicBreak(lines(j)) Then
                Dim candidate As ListMarker = TryListMarker(lines(j))
                If candidate IsNot Nothing AndAlso SameList(candidate, first) Then
                    If pendingBlank > 0 Then loose = True
                    marker = candidate
                End If
            End If
        End While
        If first.IsOrdered Then
            sb.Append("<ol")
            If first.Number <> 1 Then sb.Append(" start=""").Append(first.Number.ToString(CultureInfo.InvariantCulture)).Append("""")
            sb.Append(">").Append(vbLf)
        Else
            sb.Append("<ul>").Append(vbLf)
        End If
        For Each item As List(Of String) In items
            sb.Append("<li>")
            ParseBlocks(item, depth + 1, state, sb, Not loose)
            sb.Append("</li>").Append(vbLf)
        Next
        sb.Append(If(first.IsOrdered, "</ol>", "</ul>")).Append(vbLf)
        Return j
    End Function

    ''' <summary>A blank line between two parts of one item (outside a code fence) makes the list loose.</summary>
    Private Function ItemHasLooseBlank(itemLines As List(Of String)) As Boolean
        Dim fenceChar As Char = ControlChars.NullChar
        Dim fenceLen As Integer = 0
        For k As Integer = 0 To itemLines.Count - 1
            Dim l As String = itemLines(k)
            If fenceLen > 0 Then
                If IsFenceClose(l, fenceChar, fenceLen) Then fenceLen = 0
                Continue For
            End If
            Dim fenceIndent As Integer = 0
            Dim openChar As Char = ControlChars.NullChar
            Dim openLen As Integer = 0
            Dim info As String = ""
            If TryFenceOpen(l, fenceIndent, openChar, openLen, info) Then
                fenceChar = openChar
                fenceLen = openLen
            ElseIf k > 0 AndAlso l.Length = 0 Then
                Return True
            End If
        Next
        Return False
    End Function

    Private Function ParseParagraph(lines As List(Of String), start As Integer, state As RenderState, sb As StringBuilder, tight As Boolean) As Integer
        Dim j As Integer = start + 1
        Dim setext As Integer = 0
        While j < lines.Count
            setext = SetextLevel(lines(j))
            If setext > 0 Then Exit While
            If InterruptsParagraph(lines(j)) Then Exit While
            j += 1
        End While
        Dim text As New StringBuilder()
        For k As Integer = start To j - 1
            If k > start Then text.Append(vbLf)
            text.Append(lines(k).TrimStart(" "c))
        Next
        Dim raw As String = text.ToString().TrimEnd()
        If setext > 0 Then
            AppendHeading(sb, setext, raw, state)
            Return j + 1
        End If
        Dim html As String = RenderInline(raw, state, 0, False)
        If tight Then
            sb.Append(html).Append(vbLf)
        Else
            sb.Append("<p>").Append(html).Append("</p>").Append(vbLf)
        End If
        Return j
    End Function

    Private Sub AppendHeading(sb As StringBuilder, level As Integer, content As String, state As RenderState)
        Dim html As String = RenderInline(content.Trim(), state, 0, False)
        Dim id As String = HeadingId(html, state)
        Dim tag As String = "h" & level.ToString(CultureInfo.InvariantCulture)
        sb.Append("<").Append(tag)
        If id.Length > 0 Then sb.Append(" id=""").Append(id).Append("""")
        sb.Append(">").Append(html).Append("</").Append(tag).Append(">").Append(vbLf)
    End Sub

    ''' <summary>The GitHub-style anchor of a heading (letters, digits, - and _; spaces become -), made unique in the document.</summary>
    Private Function HeadingId(html As String, state As RenderState) As String
        If state.Ids.Count >= MaxHeadingIds Then Return ""
        Dim plain As String = DecodeBasic(StripTags(html)).ToLowerInvariant()
        Dim sb As New StringBuilder()
        For Each c As Char In plain
            If sb.Length >= MaxSlugChars Then Exit For
            If Char.IsLetterOrDigit(c) OrElse c = "_"c OrElse c = "-"c Then
                sb.Append(c)
            ElseIf Char.IsWhiteSpace(c) Then
                sb.Append("-"c)
            End If
        Next
        Dim slug As String = If(sb.Length = 0, "section", sb.ToString())
        If slug.StartsWith("fms-", StringComparison.Ordinal) Then slug = "user-" & slug
        Dim seen As Integer
        If state.Ids.TryGetValue(slug, seen) Then
            state.Ids(slug) = seen + 1
            Dim candidate As String = slug & "-" & (seen + 1).ToString(CultureInfo.InvariantCulture)
            While state.Ids.ContainsKey(candidate)
                seen += 1
                candidate = slug & "-" & (seen + 1).ToString(CultureInfo.InvariantCulture)
            End While
            state.Ids(candidate) = 0
            Return candidate
        End If
        state.Ids(slug) = 0
        Return slug
    End Function

    ' ---- tables ---------------------------------------------------------------------------------

    ''' <summary>Lines used by a table starting at <paramref name="start"/>, or 0 when it is not one.</summary>
    Private Function TryParseTable(lines As List(Of String), start As Integer, state As RenderState, sb As StringBuilder) As Integer
        Dim delimiterLine As String = lines(start + 1)
        If delimiterLine.IndexOf("|"c) < 0 OrElse delimiterLine.IndexOf("-"c) < 0 OrElse LeadingSpaces(delimiterLine) >= 4 Then Return 0
        Dim header As List(Of String) = SplitRow(lines(start))
        If header Is Nothing Then Return 0
        Dim aligns As List(Of Char) = ParseDelimiterRow(delimiterLine)
        If aligns Is Nothing OrElse aligns.Count <> header.Count Then Return 0
        Dim count As Integer = header.Count
        sb.Append("<table>").Append(vbLf).Append("<thead>").Append(vbLf)
        AppendTableRow(sb, header, aligns, "th", state)
        sb.Append("</thead>").Append(vbLf)
        Dim j As Integer = start + 2
        Dim body As New StringBuilder()
        While j < lines.Count AndAlso Not InterruptsParagraph(lines(j))
            If sb.Length + body.Length > MaxPageChars Then
                state.PageTooLarge = True
                Exit While
            End If
            Dim cells As List(Of String) = SplitRow(lines(j))
            If cells Is Nothing Then Exit While
            While cells.Count < count
                cells.Add("")
            End While
            If cells.Count > count Then cells.RemoveRange(count, cells.Count - count)
            AppendTableRow(body, cells, aligns, "td", state)
            j += 1
        End While
        If body.Length > 0 Then sb.Append("<tbody>").Append(vbLf).Append(body.ToString()).Append("</tbody>").Append(vbLf)
        sb.Append("</table>").Append(vbLf)
        Return j - start
    End Function

    Private Sub AppendTableRow(sb As StringBuilder, cells As List(Of String), aligns As List(Of Char), tag As String, state As RenderState)
        sb.Append("<tr>")
        For k As Integer = 0 To cells.Count - 1
            sb.Append("<").Append(tag)
            Select Case aligns(k)
                Case "l"c
                    sb.Append(" class=""al-l""")
                Case "c"c
                    sb.Append(" class=""al-c""")
                Case "r"c
                    sb.Append(" class=""al-r""")
            End Select
            sb.Append(">").Append(RenderInline(cells(k).Trim(), state, 0, False)).Append("</").Append(tag).Append(">")
        Next
        sb.Append("</tr>").Append(vbLf)
    End Sub

    ''' <summary>The cells of a pipe row: one outer pipe each side dropped, \| a literal pipe; Nothing past MaxTableColumns.</summary>
    Private Function SplitRow(line As String) As List(Of String)
        Dim s As String = line.Trim()
        If s.StartsWith("|", StringComparison.Ordinal) Then s = s.Substring(1)
        If s.Length > 0 AndAlso s(s.Length - 1) = "|"c Then
            Dim slashes As Integer = 0
            While s.Length - 2 - slashes >= 0 AndAlso s(s.Length - 2 - slashes) = "\"c
                slashes += 1
            End While
            If slashes Mod 2 = 0 Then s = s.Substring(0, s.Length - 1)
        End If
        Dim cells As New List(Of String)()
        Dim cell As New StringBuilder()
        Dim p As Integer = 0
        While p < s.Length
            Dim c As Char = s(p)
            If c = "\"c AndAlso p + 1 < s.Length AndAlso s(p + 1) = "|"c Then
                cell.Append("|"c)
                p += 2
            ElseIf c = "|"c Then
                cells.Add(cell.ToString())
                cell.Clear()
                If cells.Count > MaxTableColumns Then Return Nothing
                p += 1
            Else
                cell.Append(c)
                p += 1
            End If
        End While
        cells.Add(cell.ToString())
        If cells.Count > MaxTableColumns Then Return Nothing
        Return cells
    End Function

    ''' <summary>The alignments (l, c, r, or the NUL char for none) of a delimiter row, Nothing when the row is not one.</summary>
    Private Function ParseDelimiterRow(line As String) As List(Of Char)
        Dim cells As List(Of String) = SplitRow(line)
        If cells Is Nothing Then Return Nothing
        Dim aligns As New List(Of Char)()
        For Each cell As String In cells
            Dim t As String = cell.Trim()
            If t.Length = 0 Then Return Nothing
            Dim left As Boolean = t(0) = ":"c
            Dim right As Boolean = t.Length > 1 AndAlso t(t.Length - 1) = ":"c
            Dim core As String = t.Substring(If(left, 1, 0), t.Length - If(left, 1, 0) - If(right, 1, 0))
            If core.Length = 0 Then Return Nothing
            For Each c As Char In core
                If c <> "-"c Then Return Nothing
            Next
            aligns.Add(If(left AndAlso right, "c"c, If(left, "l"c, If(right, "r"c, ControlChars.NullChar))))
        Next
        Return aligns
    End Function

    ' ---- inline ---------------------------------------------------------------------------------

    Private Function RenderInline(text As String, state As RenderState, depth As Integer, inLink As Boolean) As String
        If text.Length = 0 Then Return ""
        Dim tokens As New List(Of Tok)()
        Dim lit As New StringBuilder()
        Dim delimiters As Integer = 0
        Dim p As Integer = 0
        While p < text.Length
            Dim c As Char = text(p)
            Select Case c
                Case "\"c
                    If p + 1 < text.Length Then
                        Dim d As Char = text(p + 1)
                        If d = ControlChars.Lf Then
                            TrimEndSpaces(lit)
                            lit.Append("<br>").Append(vbLf)
                            p += 2
                            Continue While
                        ElseIf IsAsciiPunctuation(d) Then
                            AppendEscapedChar(lit, d)
                            p += 2
                            Continue While
                        End If
                    End If
                    lit.Append(c)
                    p += 1
                Case "`"c
                    Dim run As Integer = CountRun(text, p, "`"c)
                    Dim close As Integer = FindBacktickClose(text, p + run, run, state)
                    If close >= 0 Then
                        Dim code As String = text.Substring(p + run, close - p - run).Replace(vbLf, " ")
                        If code.Length >= 2 AndAlso code(0) = " "c AndAlso code(code.Length - 1) = " "c AndAlso code.Trim(" "c).Length > 0 Then
                            code = code.Substring(1, code.Length - 2)
                        End If
                        FlushLiteral(tokens, lit)
                        tokens.Add(New Tok With {.Html = "<code>" & EscapeHtml(code) & "</code>"})
                        p = close + run
                    Else
                        lit.Append("`"c, run)
                        p += run
                    End If
                Case "<"c
                    Dim autoHtml As String = ""
                    Dim autoNext As Integer = 0
                    If TryAutolink(text, p, autoHtml, autoNext) Then
                        FlushLiteral(tokens, lit)
                        tokens.Add(New Tok With {.Html = autoHtml})
                        p = autoNext
                    Else
                        lit.Append("&lt;")
                        p += 1
                    End If
                Case "!"c, "["c
                    Dim isImage As Boolean = c = "!"c
                    If isImage AndAlso (p + 1 >= text.Length OrElse text(p + 1) <> "["c) Then
                        lit.Append(c)
                        p += 1
                    ElseIf Not isImage AndAlso inLink Then
                        lit.Append(c)
                        p += 1
                    Else
                        Dim linkHtml As String = ""
                        Dim linkNext As Integer = 0
                        If TryLink(text, If(isImage, p + 1, p), isImage, state, depth, inLink, linkHtml, linkNext) Then
                            FlushLiteral(tokens, lit)
                            tokens.Add(New Tok With {.Html = linkHtml})
                            p = linkNext
                        Else
                            lit.Append(c)
                            p += 1
                        End If
                    End If
                Case "*"c, "_"c, "~"c
                    Dim run As Integer = CountRun(text, p, c)
                    If delimiters >= MaxDelimiters OrElse (c = "~"c AndAlso run <> 2) Then
                        lit.Append(c, run)
                    Else
                        Dim before As Char = If(p = 0, " "c, text(p - 1))
                        Dim after As Char = If(p + run >= text.Length, " "c, text(p + run))
                        Dim beforeSpace As Boolean = Char.IsWhiteSpace(before)
                        Dim afterSpace As Boolean = Char.IsWhiteSpace(after)
                        Dim beforePunct As Boolean = IsPunctuation(before)
                        Dim afterPunct As Boolean = IsPunctuation(after)
                        Dim leftFlanking As Boolean = Not afterSpace AndAlso (Not afterPunct OrElse beforeSpace OrElse beforePunct)
                        Dim rightFlanking As Boolean = Not beforeSpace AndAlso (Not beforePunct OrElse afterSpace OrElse afterPunct)
                        Dim canOpen As Boolean = leftFlanking
                        Dim canClose As Boolean = rightFlanking
                        If c = "_"c Then
                            canOpen = leftFlanking AndAlso (Not rightFlanking OrElse beforePunct)
                            canClose = rightFlanking AndAlso (Not leftFlanking OrElse afterPunct)
                        End If
                        If canOpen OrElse canClose Then
                            FlushLiteral(tokens, lit)
                            tokens.Add(New Tok With {.IsDelim = True, .Ch = c, .Count = run, .OrigCount = run, .CanOpen = canOpen, .CanClose = canClose})
                            delimiters += 1
                        Else
                            lit.Append(c, run)
                        End If
                    End If
                    p += run
                Case "&"c
                    Dim decoded As String = ""
                    Dim entityNext As Integer = 0
                    If TryDecodeEntity(text, p, decoded, entityNext) Then
                        AppendEscaped(lit, decoded)
                        p = entityNext
                    Else
                        lit.Append("&amp;")
                        p += 1
                    End If
                Case ControlChars.Lf
                    Dim hard As Boolean = TrailingSpaces(lit) >= 2
                    TrimEndSpaces(lit)
                    If hard Then lit.Append("<br>")
                    lit.Append(vbLf)
                    p += 1
                Case Else
                    AppendEscapedChar(lit, c)
                    p += 1
            End Select
        End While
        FlushLiteral(tokens, lit)
        ProcessEmphasis(tokens, state)
        Dim result As New StringBuilder()
        For Each t As Tok In tokens
            If t.IsDelim Then
                result.Append(t.Before)
                If t.Count > 0 Then result.Append(t.Ch, t.Count)
                result.Append(t.After)
            Else
                result.Append(t.Html)
            End If
        Next
        Return result.ToString()
    End Function

    Private Sub FlushLiteral(tokens As List(Of Tok), lit As StringBuilder)
        If lit.Length = 0 Then Return
        tokens.Add(New Tok With {.Html = lit.ToString()})
        lit.Clear()
    End Sub

    Private Function TrailingSpaces(sb As StringBuilder) As Integer
        Dim n As Integer = 0
        While n < sb.Length AndAlso sb(sb.Length - 1 - n) = " "c
            n += 1
        End While
        Return n
    End Function

    Private Sub TrimEndSpaces(sb As StringBuilder)
        While sb.Length > 0 AndAlso sb(sb.Length - 1) = " "c
            sb.Length -= 1
        End While
    End Sub

    ''' <summary>The start of the next backtick run of exactly <paramref name="run"/> characters, or -1.</summary>
    Private Function FindBacktickClose(text As String, startAt As Integer, run As Integer, state As RenderState) As Integer
        Dim q As Integer = startAt
        While q < text.Length
            state.Work -= 1
            If state.Work <= 0 Then Return -1
            If text(q) = "`"c Then
                Dim n As Integer = CountRun(text, q, "`"c)
                If n = run Then Return q
                q += n
            Else
                q += 1
            End If
        End While
        Return -1
    End Function

    ''' <summary>&lt;http://..&gt;, &lt;https://..&gt;, &lt;mailto:..&gt; and &lt;name@host.tld&gt; as links; anything else stays text.</summary>
    Private Function TryAutolink(text As String, p As Integer, ByRef html As String, ByRef nextIndex As Integer) As Boolean
        Dim limit As Integer = Math.Min(text.Length, p + 2 + MaxUrlChars)
        Dim close As Integer = -1
        For q As Integer = p + 1 To limit - 1
            Dim c As Char = text(q)
            If c = ">"c Then
                close = q
                Exit For
            End If
            If c = "<"c OrElse Char.IsWhiteSpace(c) Then Return False
        Next
        If close < p + 2 Then Return False
        Dim inner As String = text.Substring(p + 1, close - p - 1)
        Dim href As String = ""
        Dim colon As Integer = inner.IndexOf(":"c)
        If colon > 0 Then
            href = SafeLinkTarget(inner)
        ElseIf IsEmailAddress(inner) Then
            href = SafeLinkTarget("mailto:" & inner)
        End If
        If href.Length = 0 Then Return False
        html = "<a href=""" & EscapeHtml(href) & """ rel=""noopener noreferrer"">" & EscapeHtml(inner) & "</a>"
        nextIndex = close + 1
        Return True
    End Function

    Private Function IsEmailAddress(text As String) As Boolean
        Dim at As Integer = text.IndexOf("@"c)
        If at <= 0 OrElse at <> text.LastIndexOf("@"c) OrElse at = text.Length - 1 Then Return False
        Dim domain As String = text.Substring(at + 1)
        If domain.IndexOf("."c) <= 0 OrElse domain(domain.Length - 1) = "."c Then Return False
        For Each c As Char In text
            Dim ok As Boolean = (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse IsAsciiDigit(c) OrElse
                                c = "."c OrElse c = "_"c OrElse c = "%"c OrElse c = "+"c OrElse c = "-"c OrElse c = "@"c
            If Not ok Then Return False
        Next
        Return True
    End Function

    Private Function FindBracketClose(text As String, openIndex As Integer, state As RenderState) As Integer
        Dim level As Integer = 1
        Dim q As Integer = openIndex + 1
        Dim limit As Integer = Math.Min(text.Length, openIndex + 1 + MaxLinkTextChars)
        While q < limit
            state.Work -= 1
            If state.Work <= 0 Then Return -1
            Dim c As Char = text(q)
            If c = "\"c Then
                q += 2
                Continue While
            End If
            If c = "["c Then
                level += 1
            ElseIf c = "]"c Then
                level -= 1
                If level = 0 Then Return q
            End If
            q += 1
        End While
        Return -1
    End Function

    ''' <summary>A link or an image whose text opens at <paramref name="openIndex"/> (the [): inline, full reference, collapsed or shortcut.</summary>
    Private Function TryLink(text As String, openIndex As Integer, isImage As Boolean, state As RenderState, depth As Integer,
                             inLink As Boolean, ByRef html As String, ByRef nextIndex As Integer) As Boolean
        If state.Work <= 0 Then Return False
        Dim close As Integer = FindBracketClose(text, openIndex, state)
        If close < 0 Then Return False
        Dim inner As String = text.Substring(openIndex + 1, close - openIndex - 1)
        Dim after As Integer = close + 1
        Dim target As String = ""
        Dim title As String = ""
        nextIndex = after
        If after < text.Length AndAlso text(after) = "("c Then
            If Not TryInlineDestination(text, after + 1, target, title, nextIndex, state) Then Return False
        Else
            Dim label As String = inner
            If after < text.Length AndAlso text(after) = "["c Then
                Dim close2 As Integer = FindBracketClose(text, after, state)
                If close2 > after Then
                    Dim given As String = text.Substring(after + 1, close2 - after - 1)
                    If given.Trim().Length > 0 Then label = given
                    nextIndex = close2 + 1
                End If
            End If
            Dim definition As LinkDefinition = Nothing
            If Not state.Definitions.TryGetValue(NormalizeLabel(label), definition) Then Return False
            target = definition.Target
            title = definition.Title
        End If
        Dim safe As String = SafeLinkTarget(target)
        Dim titleAttr As String = ""
        If title.Length > 0 Then titleAttr = " title=""" & EscapeHtml(If(title.Length > MaxTitleChars, title.Substring(0, MaxTitleChars), title)) & """"
        If isImage Then
            Dim alt As String = StripTags(If(depth >= MaxInlineDepth, EscapeHtml(inner), RenderInline(inner, state, depth + 1, True)))
            Dim picture As Boolean = safe.Length > 0 AndAlso Not AsciiEquals(safe.Substring(0, Math.Min(7, safe.Length)), "mailto:") AndAlso safe(0) <> "#"c
            If picture AndAlso Not inLink Then
                html = "<a href=""" & EscapeHtml(safe) & """" & titleAttr & " rel=""noopener noreferrer"" class=""img"">" &
                       If(alt.Length > 0, alt, EscapeHtml(safe)) & "</a>"
            ElseIf alt.Length > 0 Then
                html = "<span class=""img"">" & alt & "</span>"
            Else
                html = ""
            End If
        Else
            Dim textHtml As String = If(depth >= MaxInlineDepth, EscapeHtml(inner), RenderInline(inner, state, depth + 1, True))
            If safe.Length > 0 Then
                html = "<a href=""" & EscapeHtml(safe) & """" & titleAttr & " rel=""noopener noreferrer"">" & textHtml & "</a>"
            Else
                html = textHtml
            End If
        End If
        Return True
    End Function

    ''' <summary>The "(destination title)" of an inline link, starting just after the (.</summary>
    Private Function TryInlineDestination(text As String, start As Integer, ByRef target As String, ByRef title As String,
                                          ByRef nextIndex As Integer, state As RenderState) As Boolean
        If state.Work <= 0 Then Return False
        Dim q As Integer = start
        q = SkipWhitespace(text, q)
        Dim dest As String
        If q < text.Length AndAlso text(q) = "<"c Then
            Dim r As Integer = q + 1
            While r < text.Length AndAlso text(r) <> ">"c AndAlso text(r) <> ControlChars.Lf AndAlso text(r) <> "<"c
                If text(r) = "\"c Then r += 1
                r += 1
            End While
            state.Work -= (r - q)
            If r >= text.Length OrElse text(r) <> ">"c Then Return False
            dest = text.Substring(q + 1, r - q - 1)
            q = r + 1
        Else
            Dim parens As Integer = 0
            Dim r As Integer = q
            While r < text.Length
                Dim c As Char = text(r)
                If c = "\"c AndAlso r + 1 < text.Length Then
                    r += 2
                    Continue While
                End If
                If c = " "c OrElse c = ControlChars.Lf OrElse AscW(c) < &H20 Then Exit While
                If c = "("c Then
                    parens += 1
                    If parens > 32 Then Return False
                ElseIf c = ")"c Then
                    If parens = 0 Then Exit While
                    parens -= 1
                End If
                r += 1
            End While
            state.Work -= (r - q)
            If r > text.Length Then r = text.Length
            dest = text.Substring(q, r - q)
            q = r
        End If
        Dim beforeTitle As Integer = q
        q = SkipWhitespace(text, q)
        title = ""
        If q > beforeTitle AndAlso q < text.Length AndAlso (text(q) = """"c OrElse text(q) = "'"c OrElse text(q) = "("c) Then
            Dim closing As Char = If(text(q) = "("c, ")"c, text(q))
            Dim r As Integer = q + 1
            While r < text.Length AndAlso text(r) <> closing
                If text(r) = "\"c Then r += 1
                r += 1
            End While
            state.Work -= (r - q)
            If r >= text.Length Then Return False
            title = UnescapeAndDecode(text.Substring(q + 1, r - q - 1))
            q = SkipWhitespace(text, r + 1)
        End If
        If q >= text.Length OrElse text(q) <> ")"c Then Return False
        target = UnescapeAndDecode(dest)
        nextIndex = q + 1
        Return True
    End Function

    Private Function SkipWhitespace(text As String, q As Integer) As Integer
        While q < text.Length AndAlso (text(q) = " "c OrElse text(q) = ControlChars.Lf)
            q += 1
        End While
        Return q
    End Function

    ''' <summary>
    ''' Emphasis by the CommonMark delimiter-run rules: * and _ (one mark em, two strong) and ~~ (strikethrough). A
    ''' matched pair puts its tags on the two delimiter tokens; whatever stays unmatched is written as it was. Looking
    ''' back for an opener draws on the step budget: closers that never find one are quadratic.
    ''' </summary>
    Private Sub ProcessEmphasis(tokens As List(Of Tok), state As RenderState)
        Dim matchesLeft As Integer = MaxEmphasisMatches
        Dim closer As Integer = 0
        While closer < tokens.Count
            Dim c As Tok = tokens(closer)
            If Not c.IsDelim OrElse Not c.CanClose OrElse c.Dead OrElse c.Count <= 0 OrElse matchesLeft <= 0 Then
                closer += 1
                Continue While
            End If
            Dim opener As Integer = closer - 1
            Dim found As Boolean = False
            While opener >= 0 AndAlso state.Work > 0
                state.Work -= 1
                Dim o As Tok = tokens(opener)
                If o.IsDelim AndAlso Not o.Dead AndAlso o.Count > 0 AndAlso o.Ch = c.Ch AndAlso o.CanOpen Then
                    If c.Ch = "~"c AndAlso (o.Count < 2 OrElse c.Count < 2) Then
                        ' only a pair of two tildes strikes through
                    ElseIf (o.CanClose OrElse c.CanOpen) AndAlso (o.OrigCount + c.OrigCount) Mod 3 = 0 AndAlso (o.OrigCount Mod 3 <> 0 OrElse c.OrigCount Mod 3 <> 0) Then
                        ' the rule of three
                    Else
                        found = True
                        Exit While
                    End If
                End If
                opener -= 1
            End While
            If Not found Then
                closer += 1
                Continue While
            End If
            Dim openTok As Tok = tokens(opener)
            Dim used As Integer = If(c.Ch = "~"c, 2, If(openTok.Count >= 2 AndAlso c.Count >= 2, 2, 1))
            openTok.Count -= used
            c.Count -= used
            Dim openTag As String
            Dim closeTag As String
            If c.Ch = "~"c Then
                openTag = "<del>"
                closeTag = "</del>"
            ElseIf used = 2 Then
                openTag = "<strong>"
                closeTag = "</strong>"
            Else
                openTag = "<em>"
                closeTag = "</em>"
            End If
            openTok.After = openTag & openTok.After
            c.Before = c.Before & closeTag
            For k As Integer = opener + 1 To closer - 1
                If tokens(k).IsDelim Then tokens(k).Dead = True
            Next
            matchesLeft -= 1
            If c.Count = 0 Then closer += 1
        End While
    End Sub

    ' ---- the page and its theme -----------------------------------------------------------------

    Private Function Page(options As TextMarkdownOptions, body As String) As String
        Dim title As String = If(options.Title, "")
        If title.Length > MaxTitleChars Then title = title.Substring(0, MaxTitleChars)
        Dim sb As New StringBuilder(body.Length + 2048)
        sb.Append("<!DOCTYPE html>").Append(vbLf)
        sb.Append("<html dir=""auto""><head><meta charset=""utf-8""><title>").Append(EscapeHtml(title)).Append("</title><style>")
        sb.Append(ThemeCss(options))
        sb.Append("</style></head><body><article class=""md"">").Append(vbLf)
        sb.Append(body)
        sb.Append("</article></body></html>").Append(vbLf)
        Return sb.ToString()
    End Function

    ''' <summary>The reader theme as CSS: only computed hex colours, clamped numbers and checked font names go in.</summary>
    Public Function ThemeCss(options As TextMarkdownOptions) As String
        Dim opts As TextMarkdownOptions = If(options, New TextMarkdownOptions())
        Dim scheme As PageColors = BuildColors(opts)
        ' The bounds of the TextFontSize (6..48 pt) and TextLineSpacing (1..3) preferences, kept here so this file needs no preferences class.
        Dim size As Double = Math.Max(6.0, Math.Min(48.0, CDbl(opts.FontSizePoints)))
        If Double.IsNaN(size) Then size = 12.0
        Dim spacing As Double = Math.Max(1.0, Math.Min(3.0, CDbl(opts.LineSpacing)))
        If Double.IsNaN(spacing) Then spacing = 1.2
        Dim css As New StringBuilder(CssTemplate)
        css.Replace("@BACK@", scheme.Back).Replace("@INK@", scheme.Ink).Replace("@MUTED@", scheme.Muted).Replace("@LINK@", scheme.Link)
        css.Replace("@CODE@", scheme.CodeBack).Replace("@RULE@", scheme.Rule)
        css.Replace("@SCHEME@", If(scheme.IsDark, "dark", "light"))
        css.Replace("@FONT@", FontStack(opts.FontFamily, "Segoe UI", "sans-serif"))
        css.Replace("@MONO@", FontStack(opts.CodeFontFamily, "Consolas", "monospace"))
        css.Replace("@SIZE@", size.ToString("0.##", CultureInfo.InvariantCulture))
        css.Replace("@LINE@", spacing.ToString("0.##", CultureInfo.InvariantCulture))
        Return css.ToString()
    End Function

    Private Const CssTemplate As String =
        "html{background:@BACK@;color:@INK@;color-scheme:@SCHEME@}" &
        "body{margin:0;background:@BACK@;color:@INK@;font-family:@FONT@;font-size:@SIZE@pt;line-height:@LINE@}" &
        ".md{max-width:50em;margin:0 auto;padding:1.2em 1.6em 3em}" &
        ".md h1,.md h2,.md h3,.md h4,.md h5,.md h6{line-height:1.25;margin:1.4em 0 .5em}" &
        ".md h1{font-size:1.8em;border-bottom:1px solid @RULE@;padding-bottom:.2em}" &
        ".md h2{font-size:1.45em;border-bottom:1px solid @RULE@;padding-bottom:.2em}" &
        ".md h3{font-size:1.2em}.md h4{font-size:1.05em}.md h5,.md h6{font-size:1em;color:@MUTED@}" &
        ".md p,.md ul,.md ol,.md blockquote,.md table,.md pre{margin:0 0 1em}" &
        ".md ul,.md ol{padding-inline-start:2em}.md li p{margin:0 0 .5em}" &
        ".md a{color:@LINK@}" &
        ".md code{font-family:@MONO@;font-size:.92em;background:@CODE@;padding:.1em .3em;border-radius:3px}" &
        ".md pre{background:@CODE@;padding:.8em 1em;overflow-x:auto;border-radius:4px}" &
        ".md pre code{background:none;padding:0}" &
        ".md blockquote{margin-inline-start:0;padding:.1em 1em;border-inline-start:4px solid @RULE@;color:@MUTED@}" &
        ".md hr{border:0;border-top:1px solid @RULE@;margin:1.5em 0}" &
        ".md table{border-collapse:collapse;display:block;overflow-x:auto;max-width:100%}" &
        ".md th,.md td{border:1px solid @RULE@;padding:.35em .7em}" &
        ".md th{background:@CODE@}" &
        ".md .al-l{text-align:left}.md .al-c{text-align:center}.md .al-r{text-align:right}" &
        ".md span.img{color:@MUTED@;font-style:italic}"

    ''' <summary>One CSS font stack: the name when it is plain (letters, digits, space, - . _), else the fallback.</summary>
    Private Function FontStack(name As String, fallbackName As String, generic As String) As String
        Dim chosen As String = fallbackName
        If Not String.IsNullOrWhiteSpace(name) AndAlso name.Trim().Length <= 64 Then
            Dim ok As Boolean = True
            For Each c As Char In name.Trim()
                If Not (Char.IsLetterOrDigit(c) OrElse c = " "c OrElse c = "-"c OrElse c = "."c OrElse c = "_"c) Then ok = False
            Next
            If ok Then chosen = name.Trim()
        End If
        Return """" & chosen & """," & generic
    End Function

    Private Function NormalizeTheme(theme As String) As String
        Select Case theme
            Case "light", "dark", "sepia", "black"
                Return theme
            Case Else
                Return "system"
        End Select
    End Function

    Private Function BuildColors(options As TextMarkdownOptions) As PageColors
        Dim theme As String = NormalizeTheme(options.Theme)
        Dim palette As TextReaderPalette = TextReaderLayout.Palette(theme, options.SystemBack, options.SystemInk)
        Dim back As Color = Opaque(palette.Back, Color.White)
        Dim ink As Color = Opaque(palette.Ink, back)
        Dim muted As Color = Opaque(palette.Muted, back)
        Dim dark As Boolean = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) / 255.0 < 0.5
        Dim result As New PageColors()
        result.IsDark = dark
        result.Back = HexColor(back)
        result.Ink = HexColor(ink)
        result.Muted = HexColor(muted)
        result.Link = If(theme = "sepia", "#7a4b00", If(dark, "#8ab4f8", "#0b57d0"))
        result.CodeBack = HexColor(Mix(back, ink, 0.08))
        result.Rule = HexColor(Mix(back, ink, 0.25))
        Return result
    End Function

    ''' <summary>A colour with transparency laid over a background, so the page never relies on alpha.</summary>
    Private Function Opaque(c As Color, back As Color) As Color
        If c.A = 255 Then Return c
        Return Mix(back, Color.FromArgb(c.R, c.G, c.B), c.A / 255.0)
    End Function

    Private Function Mix(a As Color, b As Color, amount As Double) As Color
        Return Color.FromArgb(Channel(a.R, b.R, amount), Channel(a.G, b.G, amount), Channel(a.B, b.B, amount))
    End Function

    Private Function Channel(a As Integer, b As Integer, amount As Double) As Integer
        Return CInt(Math.Round(a + (b - a) * amount))
    End Function

    Private Function HexColor(c As Color) As String
        Return "#" & c.R.ToString("x2", CultureInfo.InvariantCulture) & c.G.ToString("x2", CultureInfo.InvariantCulture) & c.B.ToString("x2", CultureInfo.InvariantCulture)
    End Function

End Module
#End If
