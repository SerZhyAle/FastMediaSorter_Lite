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
Imports Xunit

' Specification 277 step P3.7 (decision D07): the Markdown renderer behind the rendered view. Every construct of
' the supported subset, every way to smuggle script or a foreign link through it, the five themes, empty input and
' inputs built to make a renderer slow or deep. Most facts end in Validate: it walks the page the renderer wrote and
' fails on any element, attribute or link target outside the closed set the renderer is allowed to write.
Public Class TextMarkdownRendererTests

    Private Shared ReadOnly KnownTags As New HashSet(Of String)(StringComparer.Ordinal) From {
        "p", "br", "em", "strong", "del", "code", "pre", "a", "span", "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "blockquote", "hr", "table", "thead", "tbody", "tr", "th", "td"}

    Private Shared ReadOnly KnownAttributes As New HashSet(Of String)(StringComparer.Ordinal) From {
        "id", "class", "href", "title", "rel", "start"}

    Private Const ArticleStart As String = "<article class=""md"">"

    ' ---- helpers --------------------------------------------------------------------------------

    Private Shared Function Body(html As String) As String
        Dim marker As String = ArticleStart & vbLf
        Dim s As Integer = html.IndexOf(marker, StringComparison.Ordinal)
        Assert.True(s >= 0, "the page has no article")
        Dim e As Integer = html.LastIndexOf("</article>", StringComparison.Ordinal)
        Assert.True(e >= s + marker.Length, "the article never closes")
        Return html.Substring(s + marker.Length, e - s - marker.Length)
    End Function

    Private Shared Function BodyOf(markdown As String) As String
        Return Body(TextMarkdownRenderer.Render(markdown, "light"))
    End Function

    Private Shared Function CountOf(text As String, token As String) As Integer
        Dim n As Integer = 0
        Dim at As Integer = text.IndexOf(token, StringComparison.Ordinal)
        While at >= 0
            n += 1
            at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal)
        End While
        Return n
    End Function

    Private Shared Function StartsEntity(s As String, i As Integer) As Boolean
        For Each e As String In New String() {"&amp;", "&lt;", "&gt;", "&quot;", "&#39;"}
            If String.CompareOrdinal(s, i, e, 0, e.Length) = 0 Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' Walks the article of a page: every element is on the allow-list and balanced, every attribute is on the
    ''' allow-list with a quoted value, every href is http, https, mailto or a fragment, and no text node holds a raw
    ''' angle bracket, quote or ampersand. Returns the deepest element nesting.
    ''' </summary>
    Private Shared Function Validate(html As String) As Integer
        Dim page As String = Body(html)
        Dim stack As New Stack(Of String)()
        Dim maxDepth As Integer = 0
        Dim i As Integer = 0
        While i < page.Length
            Dim c As Char = page(i)
            If c = "<"c Then
                i += 1
                Dim closing As Boolean = i < page.Length AndAlso page(i) = "/"c
                If closing Then i += 1
                Dim nameStart As Integer = i
                While i < page.Length AndAlso ((page(i) >= "a"c AndAlso page(i) <= "z"c) OrElse (page(i) >= "0"c AndAlso page(i) <= "9"c))
                    i += 1
                End While
                Dim name As String = page.Substring(nameStart, i - nameStart)
                Assert.True(KnownTags.Contains(name), "unknown element <" & name & ">")
                If closing Then
                    Assert.True(i < page.Length AndAlso page(i) = ">"c, "a closing tag with attributes: </" & name)
                    i += 1
                    Assert.True(stack.Count > 0 AndAlso stack.Pop() = name, "unbalanced </" & name & ">")
                    Continue While
                End If
                Do
                    While i < page.Length AndAlso page(i) = " "c
                        i += 1
                    End While
                    Assert.True(i < page.Length, "an unterminated tag <" & name)
                    If page(i) = ">"c Then Exit Do
                    Dim attributeStart As Integer = i
                    While i < page.Length AndAlso ((page(i) >= "a"c AndAlso page(i) <= "z"c) OrElse page(i) = "-"c)
                        i += 1
                    End While
                    Dim attribute As String = page.Substring(attributeStart, i - attributeStart)
                    Assert.True(KnownAttributes.Contains(attribute), "unknown attribute '" & attribute & "' on <" & name & ">")
                    Assert.True(i + 1 < page.Length AndAlso page(i) = "="c AndAlso page(i + 1) = """"c, "attribute syntax on <" & name & ">")
                    i += 2
                    Dim valueStart As Integer = i
                    While i < page.Length AndAlso page(i) <> """"c
                        Assert.True(page(i) <> "<"c AndAlso page(i) <> ">"c, "a raw angle bracket inside an attribute of <" & name & ">")
                        i += 1
                    End While
                    Assert.True(i < page.Length, "an unterminated attribute value on <" & name & ">")
                    Dim value As String = page.Substring(valueStart, i - valueStart)
                    i += 1
                    If attribute = "href" Then
                        Dim okTarget As Boolean = value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) OrElse
                                                  value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
                                                  value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) OrElse
                                                  value.StartsWith("#", StringComparison.Ordinal)
                        Assert.True(okTarget, "a link target outside http, https, mailto and #fragment: " & value)
                    End If
                Loop
                i += 1
                If name <> "br" AndAlso name <> "hr" Then
                    stack.Push(name)
                    maxDepth = Math.Max(maxDepth, stack.Count)
                End If
            Else
                Assert.True(c <> ">"c AndAlso c <> """"c AndAlso c <> "'"c, "an unescaped '" & c & "' in a text node")
                If c = "&"c Then Assert.True(StartsEntity(page, i), "a bare ampersand in a text node")
                i += 1
            End If
        End While
        Assert.Equal(0, stack.Count)
        Return maxDepth
    End Function

    Private Shared Function Repeat(unit As String, times As Integer) As String
        Return String.Concat(Enumerable.Repeat(unit, times))
    End Function

    ' ---- the page -------------------------------------------------------------------------------

    <Fact>
    Public Sub A_paragraph_is_one_static_html_document()
        Dim html As String = TextMarkdownRenderer.Render("Hello", "light")
        Assert.StartsWith("<!DOCTYPE html>", html)
        Assert.Contains("<meta charset=""utf-8"">", html)
        Assert.Contains("<style>", html)
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase)
        Assert.Equal("<p>Hello</p>" & vbLf, Body(html))
        Validate(html)
    End Sub

    <Fact>
    Public Sub Empty_and_missing_input_give_an_empty_valid_page()
        For Each source As String In New String() {"", Nothing, "   ", vbLf & vbLf, vbCrLf}
            Dim html As String = TextMarkdownRenderer.Render(source, "dark")
            Assert.Equal("", Body(html))
            Assert.Contains("</html>", html)
        Next
        Assert.Contains("<html", TextMarkdownRenderer.Render("x", DirectCast(Nothing, TextMarkdownOptions)))
    End Sub

    <Fact>
    Public Sub The_title_is_text_in_the_head_and_cannot_close_it()
        Dim html As String = TextMarkdownRenderer.Render("x", New TextMarkdownOptions With {.Title = "a</title><script>alert(1)</script>"})
        Assert.Contains("<title>a&lt;/title&gt;&lt;script&gt;alert(1)&lt;/script&gt;</title>", html)
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase)
    End Sub

    ' ---- constructs -----------------------------------------------------------------------------

    <Fact>
    Public Sub Headings_atx_and_setext_with_anchors()
        Assert.Equal("<h1 id=""title"">Title</h1>" & vbLf, BodyOf("# Title"))
        Assert.Equal("<h2 id=""sub-title"">Sub <em>title</em></h2>" & vbLf, BodyOf("## Sub *title*"))
        Assert.Equal("<h1 id=""title"">Title</h1>" & vbLf, BodyOf("# Title #"))
        Assert.Equal("<h1 id=""setext"">Setext</h1>" & vbLf, BodyOf("Setext" & vbLf & "====="))
        Assert.Equal("<h2 id=""setext-two"">Setext two</h2>" & vbLf, BodyOf("Setext two" & vbLf & "-----"))
        Assert.Equal("<h6 id=""six"">Six</h6>" & vbLf, BodyOf("###### Six"))
        Assert.Equal("<p>####### seven</p>" & vbLf, BodyOf("####### seven"))
        Assert.Equal("<p>#hashtag</p>" & vbLf, BodyOf("#hashtag"))
    End Sub

    <Fact>
    Public Sub Equal_headings_get_distinct_anchors()
        Dim body As String = BodyOf("# A" & vbLf & vbLf & "# A" & vbLf & vbLf & "# A")
        Assert.Contains("<h1 id=""a"">A</h1>", body)
        Assert.Contains("<h1 id=""a-1"">A</h1>", body)
        Assert.Contains("<h1 id=""a-2"">A</h1>", body)
    End Sub

    <Fact>
    Public Sub Emphasis_strong_and_strikethrough()
        Assert.Equal("<p><em>a</em> <strong>b</strong> <em><strong>c</strong></em> <del>d</del> <code>e</code></p>" & vbLf,
                     BodyOf("*a* **b** ***c*** ~~d~~ `e`"))
        Assert.Equal("<p><em>a</em> <strong>b</strong></p>" & vbLf, BodyOf("_a_ __b__"))
    End Sub

    <Fact>
    Public Sub Emphasis_follows_the_flanking_rules()
        Assert.Equal("<p>snake_case_word</p>" & vbLf, BodyOf("snake_case_word"))
        Assert.Equal("<p>a<em>b</em>c</p>" & vbLf, BodyOf("a*b*c"))
        Assert.Equal("<p>2 * 3 * 4</p>" & vbLf, BodyOf("2 * 3 * 4"))
        Assert.Equal("<p>*<em>a</em></p>" & vbLf, BodyOf("**a*"))
        Assert.Equal("<p>~a~ ~~ b ~~</p>" & vbLf, BodyOf("~a~ ~~ b ~~"))
    End Sub

    <Fact>
    Public Sub Inline_code_is_escaped_and_keeps_its_markup_as_text()
        Assert.Equal("<p><code>a&lt;b&gt;&amp;*c*</code></p>" & vbLf, BodyOf("`a<b>&*c*`"))
        Assert.Equal("<p><code>a`b</code></p>" & vbLf, BodyOf("`` a`b ``"))
        Assert.Equal("<p>`` open</p>" & vbLf, BodyOf("`` open"))
        ' Three backticks at a line start open a fence: an unclosed one is a code block, not literal text.
        Assert.StartsWith("<pre><code", BodyOf("``` open"))
    End Sub

    <Fact>
    Public Sub Fenced_code_keeps_its_text_and_names_a_plain_language()
        Assert.Equal("<pre><code class=""language-vb"">x &lt; y" & vbLf & "  z" & vbLf & "</code></pre>" & vbLf,
                     BodyOf("```vb" & vbLf & "x < y" & vbLf & "  z" & vbLf & "```"))
        Assert.Equal("<pre><code>*not em*" & vbLf & "</code></pre>" & vbLf, BodyOf("~~~" & vbLf & "*not em*" & vbLf & "~~~"))
        ' Unclosed: the fence runs to the end of the document.
        Assert.Equal("<pre><code>a" & vbLf & "b" & vbLf & "</code></pre>" & vbLf, BodyOf("```" & vbLf & "a" & vbLf & "b"))
        ' A language word that is not plain gives no class at all.
        Dim hostile As String = BodyOf("```""><script>alert(1)</script>" & vbLf & "code" & vbLf & "```")
        Assert.DoesNotContain("class=", hostile)
        Assert.DoesNotContain("<script", hostile)
    End Sub

    <Fact>
    Public Sub Indented_code()
        Assert.Equal("<pre><code>code &lt;b&gt;" & vbLf & "more" & vbLf & "</code></pre>" & vbLf, BodyOf("    code <b>" & vbLf & "    more"))
        ' A tab counts to the next stop of four.
        Assert.Equal("<pre><code>tabbed" & vbLf & "</code></pre>" & vbLf, BodyOf(ControlChars.Tab & "tabbed"))
    End Sub

    <Fact>
    Public Sub Bullet_and_ordered_lists()
        Dim bullets As String = BodyOf("- a" & vbLf & "- b" & vbLf & "- c")
        Assert.Equal(3, CountOf(bullets, "<li>"))
        Assert.StartsWith("<ul>", bullets)
        Assert.DoesNotContain("<p>", bullets)
        Dim ordered As String = BodyOf("3. x" & vbLf & "4. y")
        Assert.StartsWith("<ol start=""3"">", ordered)
        Assert.Equal(2, CountOf(ordered, "<li>"))
        Assert.StartsWith("<ol>", BodyOf("1. only"))
        Assert.Equal("<p>-not a list</p>" & vbLf, BodyOf("-not a list"))
    End Sub

    <Fact>
    Public Sub Nested_lists_and_loose_lists()
        Dim nested As String = BodyOf("- a" & vbLf & "  - b" & vbLf & "  - c" & vbLf & "- d")
        Assert.Equal(2, CountOf(nested, "<ul>"))
        Assert.Equal(4, CountOf(nested, "<li>"))
        Dim loose As String = BodyOf("- a" & vbLf & vbLf & "- b")
        Assert.Contains("<li><p>a</p>", loose)
        Assert.Contains("<li><p>b</p>", loose)
        Dim mixed As String = BodyOf("- a" & vbLf & "+ b")
        Assert.Equal(2, CountOf(mixed, "<ul>"))
    End Sub

    <Fact>
    Public Sub A_list_item_can_hold_a_code_block_and_a_second_paragraph()
        Dim body As String = BodyOf("1. step" & vbLf & vbLf & "   more text" & vbLf & vbLf & "   ```" & vbLf & "   code" & vbLf & "   ```")
        Assert.Contains("<li><p>step</p>", body)
        Assert.Contains("<p>more text</p>", body)
        Assert.Contains("<pre><code>code" & vbLf & "</code></pre>", body)
        Validate(TextMarkdownRenderer.Render("1. step" & vbLf & vbLf & "   more text", "light"))
    End Sub

    <Fact>
    Public Sub Block_quotes_nest_and_continue_lazily()
        Dim body As String = BodyOf("> quoted" & vbLf & "> more" & vbLf & "lazy" & vbLf & vbLf & "after")
        Assert.StartsWith("<blockquote>", body)
        Assert.Contains("quoted" & vbLf & "more" & vbLf & "lazy", body)
        Assert.Contains("</blockquote>" & vbLf & "<p>after</p>", body)
        Dim deeper As String = BodyOf("> a" & vbLf & "> > b")
        Assert.Equal(2, CountOf(deeper, "<blockquote>"))
        Assert.Equal(2, CountOf(BodyOf(">> deep"), "<blockquote>"))
    End Sub

    <Fact>
    Public Sub Thematic_breaks()
        Assert.Equal("<hr>" & vbLf, BodyOf("---"))
        Assert.Equal("<hr>" & vbLf, BodyOf("***"))
        Assert.Equal("<hr>" & vbLf, BodyOf("_ _ _"))
        Assert.Equal("<p>a</p>" & vbLf & "<hr>" & vbLf & "<p>b</p>" & vbLf, BodyOf("a" & vbLf & vbLf & "---" & vbLf & vbLf & "b"))
    End Sub

    <Fact>
    Public Sub Line_breaks_hard_and_soft()
        Assert.Equal("<p>line1" & vbLf & "line2</p>" & vbLf, BodyOf("line1" & vbLf & "line2"))
        Assert.Equal("<p>a<br>" & vbLf & "b</p>" & vbLf, BodyOf("a  " & vbLf & "b"))
        Assert.Equal("<p>a<br>" & vbLf & "b</p>" & vbLf, BodyOf("a\" & vbLf & "b"))
        Assert.Equal("<p>a</p>" & vbLf & "<p>b</p>" & vbLf, BodyOf("a" & vbCrLf & vbCrLf & "b"))
        Assert.Equal("<p>a</p>" & vbLf & "<p>b</p>" & vbLf, BodyOf("a" & vbCr & vbCr & "b"))
    End Sub

    <Fact>
    Public Sub Links_inline_reference_collapsed_and_autolinks()
        Assert.Equal("<p><a href=""https://example.com"" title=""T"" rel=""noopener noreferrer"">text</a></p>" & vbLf,
                     BodyOf("[text](https://example.com ""T"")"))
        Assert.Equal("<p><a href=""https://example.com/x_(y)"" rel=""noopener noreferrer"">p</a></p>" & vbLf,
                     BodyOf("[p](https://example.com/x_(y))"))
        Assert.Equal("<p><a href=""mailto:me@example.com"" rel=""noopener noreferrer"">mail</a></p>" & vbLf,
                     BodyOf("[mail](mailto:me@example.com)"))
        Assert.Equal("<p><a href=""https://example.com/ref"" rel=""noopener noreferrer"">ref</a></p>" & vbLf,
                     BodyOf("[ref][1]" & vbLf & vbLf & "[1]: https://example.com/ref"))
        Assert.Equal("<p><a href=""https://example.com/s"" title=""Short"" rel=""noopener noreferrer"">Foo</a></p>" & vbLf,
                     BodyOf("[Foo]" & vbLf & vbLf & "[foo]: https://example.com/s ""Short"""))
        Assert.Equal("<p><a href=""https://example.com"" rel=""noopener noreferrer"">https://example.com</a></p>" & vbLf,
                     BodyOf("<https://example.com>"))
        Assert.Equal("<p><a href=""mailto:me@example.com"" rel=""noopener noreferrer"">me@example.com</a></p>" & vbLf,
                     BodyOf("<me@example.com>"))
        Assert.Equal("<p>[nope]</p>" & vbLf, BodyOf("[nope]"))
        Assert.Equal("<p><a href=""https://example.com"" rel=""noopener noreferrer""><em>em</em> <code>c</code></a></p>" & vbLf,
                     BodyOf("[*em* `c`](https://example.com)"))
    End Sub

    <Fact>
    Public Sub A_fragment_link_stays_for_a_table_of_contents()
        Dim body As String = BodyOf("[top](#intro)" & vbLf & vbLf & "# Intro")
        Assert.Contains("<a href=""#intro"" rel=""noopener noreferrer"">top</a>", body)
        Assert.Contains("<h1 id=""intro"">Intro</h1>", body)
        ' A fragment with nothing after the # points nowhere: its text stays, the target goes.
        Assert.Equal("<p>x</p>" & vbLf, BodyOf("[x](#)"))
    End Sub

    <Fact>
    Public Sub Images_are_links_or_alt_text_never_an_img_element()
        Assert.Equal("<p><a href=""https://example.com/a.png"" rel=""noopener noreferrer"" class=""img"">logo</a></p>" & vbLf,
                     BodyOf("![logo](https://example.com/a.png)"))
        Assert.Equal("<p><span class=""img"">alt</span></p>" & vbLf, BodyOf("![alt](pics/a.png)"))
        Assert.Equal("<p><span class=""img"">x</span></p>" & vbLf, BodyOf("![x](data:image/png;base64,AAAA)"))
        Assert.Equal("<p><span class=""img"">x</span></p>" & vbLf, BodyOf("![x](javascript:alert(1))"))
        Assert.DoesNotContain("<img", BodyOf("![a](https://example.com/a.png) ![b](c.png)"))
        ' An image inside a link is its alt text: a link never holds a second link.
        Dim nested As String = BodyOf("[![i](https://example.com/i.png)](https://example.com/page)")
        Assert.Equal(1, CountOf(nested, "<a "))
        Validate(TextMarkdownRenderer.Render("[![i](https://example.com/i.png)](https://example.com/page)", "light"))
    End Sub

    <Fact>
    Public Sub Backslash_escapes_and_character_references()
        Assert.Equal("<p>*not em* &lt;b&gt; [x]</p>" & vbLf, BodyOf("\*not em\* \<b\> \[x\]"))
        Assert.Equal("<p>a &lt; b &amp;&amp; c &gt; d</p>" & vbLf, BodyOf("a < b && c > d"))
        Assert.Equal("<p>&amp; " & ChrW(169) & " A &amp;bogus;</p>" & vbLf, BodyOf("&amp; &copy; &#65; &bogus;"))
        Assert.Equal("<p>&lt;script&gt;</p>" & vbLf, BodyOf("&lt;script&gt;"))
        Assert.Equal("<p>&lt;script&gt;</p>" & vbLf, BodyOf("&#60;script&#62;"))
        Assert.Equal("<p>" & ChrW(&HFFFD) & "</p>" & vbLf, BodyOf("&#0;"))
        Assert.Equal("<p>a\b</p>" & vbLf, BodyOf("a\b"))
    End Sub

    <Fact>
    Public Sub A_table_with_alignment_cells_and_escaped_pipes()
        Dim body As String = BodyOf("| a | b | c |" & vbLf & "|:--|--:|:-:|" & vbLf & "| 1 | **2** | x\|y |" & vbLf & "| short |")
        Assert.Contains("<table>", body)
        Assert.Contains("<th class=""al-l"">a</th>", body)
        Assert.Contains("<th class=""al-r"">b</th>", body)
        Assert.Contains("<th class=""al-c"">c</th>", body)
        Assert.Contains("<td class=""al-r""><strong>2</strong></td>", body)
        Assert.Contains("<td class=""al-c"">x|y</td>", body)
        ' A short row is padded to the header width.
        Assert.Contains("<tr><td class=""al-l"">short</td><td class=""al-r""></td><td class=""al-c""></td></tr>", body)
        Validate(TextMarkdownRenderer.Render("| a | b |" & vbLf & "|---|---|" & vbLf & "| 1 | 2 |", "light"))
        ' Not a table: the delimiter row does not match the header.
        Assert.DoesNotContain("<table", BodyOf("| a | b |" & vbLf & "|---|"))
        Assert.DoesNotContain("<table", BodyOf("a | b" & vbLf & "just text"))
    End Sub

    ' ---- link targets ---------------------------------------------------------------------------

    <Theory>
    <InlineData("https://example.com/a?b=c#d", "https://example.com/a?b=c#d")>
    <InlineData("HTTP://EXAMPLE.COM", "HTTP://EXAMPLE.COM")>
    <InlineData("mailto:a@b.example", "mailto:a@b.example")>
    <InlineData("#section", "#section")>
    <InlineData("  https://example.com  ", "https://example.com")>
    <InlineData("#", "")>
    <InlineData("javascript:alert(1)", "")>
    <InlineData("JaVaScRiPt:alert(1)", "")>
    <InlineData("vbscript:msgbox(1)", "")>
    <InlineData("data:text/html,x", "")>
    <InlineData("file:///c:/windows", "")>
    <InlineData("ftp://example.com/x", "")>
    <InlineData("//evil.example/x", "")>
    <InlineData("/relative/path", "")>
    <InlineData("relative.html", "")>
    <InlineData("https://user:pw@host.example/", "")>
    <InlineData("http://", "")>
    <InlineData("https:example.com", "")>
    <InlineData("https://exa mple.com", "")>
    <InlineData("https://a.example\evil", "")>
    <InlineData("mailto:", "")>
    <InlineData("", "")>
    Public Sub Only_http_https_mailto_and_fragments_are_link_targets(target As String, expected As String)
        Assert.Equal(expected, TextMarkdownRenderer.SafeLinkTarget(target))
    End Sub

    <Fact>
    Public Sub A_target_with_a_control_character_or_nothing_at_all_is_refused()
        Assert.Equal("", TextMarkdownRenderer.SafeLinkTarget(Nothing))
        Assert.Equal("", TextMarkdownRenderer.SafeLinkTarget("java" & ControlChars.Tab & "script:alert(1)"))
        Assert.Equal("", TextMarkdownRenderer.SafeLinkTarget("https://example.com/" & ControlChars.Lf & "x"))
        Assert.Equal("", TextMarkdownRenderer.SafeLinkTarget("https://example.com/" & ChrW(0) & "x"))
        Assert.Equal("", TextMarkdownRenderer.SafeLinkTarget("https://example.com/" & ChrW(&H85) & "x"))
        Assert.Equal("", TextMarkdownRenderer.SafeLinkTarget("https://example.com/" & ChrW(&H2028) & "x"))
        Assert.Equal("", TextMarkdownRenderer.SafeLinkTarget("https://example.com/" & New String("a"c, TextMarkdownRenderer.MaxUrlChars)))
    End Sub

    <Fact>
    Public Sub Escape_html_covers_the_five_characters()
        Assert.Equal("&amp;&lt;&gt;&quot;&#39;", TextMarkdownRenderer.EscapeHtml("&<>""'"))
        Assert.Equal("", TextMarkdownRenderer.EscapeHtml(Nothing))
        Assert.Equal("plain text", TextMarkdownRenderer.EscapeHtml("plain text"))
    End Sub

    <Fact>
    Public Sub Markdown_extensions_and_the_size_bound()
        Assert.True(TextMarkdownRenderer.IsMarkdownExtension(".md"))
        Assert.True(TextMarkdownRenderer.IsMarkdownExtension(".MD"))
        Assert.True(TextMarkdownRenderer.IsMarkdownExtension(".markdown"))
        Assert.False(TextMarkdownRenderer.IsMarkdownExtension(".txt"))
        Assert.False(TextMarkdownRenderer.IsMarkdownExtension("md"))
        Assert.False(TextMarkdownRenderer.IsMarkdownExtension(""))
        Assert.False(TextMarkdownRenderer.IsMarkdownExtension(Nothing))
        Assert.True(TextMarkdownRenderer.IsRenderable(Nothing))
        Assert.True(TextMarkdownRenderer.IsRenderable(New String("a"c, TextMarkdownRenderer.MaxSourceChars)))
        Assert.False(TextMarkdownRenderer.IsRenderable(New String("a"c, TextMarkdownRenderer.MaxSourceChars + 1)))
    End Sub

    ' ---- injection ------------------------------------------------------------------------------

    <Fact>
    Public Sub A_script_tag_and_raw_html_are_shown_as_text()
        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>" & vbLf, BodyOf("<script>alert(1)</script>"))
        Assert.Equal("<p>&lt;img src=x onerror=alert(1)&gt;</p>" & vbLf, BodyOf("<img src=x onerror=alert(1)>"))
        Assert.Equal("<p>&lt;a href=&quot;javascript:alert(1)&quot;&gt;x&lt;/a&gt;</p>" & vbLf, BodyOf("<a href=""javascript:alert(1)"">x</a>"))
        Assert.Equal("<p>&lt;iframe src=&quot;https://evil.example&quot;&gt;&lt;/iframe&gt;</p>" & vbLf, BodyOf("<iframe src=""https://evil.example""></iframe>"))
    End Sub

    <Fact>
    Public Sub A_javascript_link_keeps_its_text_and_loses_its_target()
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click](javascript:alert(1))"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click](JAVASCRIPT:alert(1))"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click](javascript&#58;alert(1))"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click](&#106;avascript:alert(1))"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click](jav&#x09;ascript:alert(1))"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click]( javascript:alert(1))"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click](<javascript:alert(1)>)"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click](data:text/html;base64,PHNjcmlwdD4=)"))
        Assert.Equal("<p>click</p>" & vbLf, BodyOf("[click]" & vbLf & vbLf & "[click]: javascript:alert(1)"))
        Assert.Equal("<p>&lt;javascript:alert(1)&gt;</p>" & vbLf, BodyOf("<javascript:alert(1)>"))
    End Sub

    <Fact>
    Public Sub A_quote_in_a_target_or_a_title_cannot_leave_its_attribute()
        Dim target As String = BodyOf("[x](https://a.example/""onmouseover=""alert(1))")
        Assert.Contains("href=""https://a.example/&quot;onmouseover=&quot;alert(1)""", target)
        Assert.DoesNotContain("""onmouseover", target)
        Dim title As String = BodyOf("[x](https://a.example ""a\""b"")")
        Assert.Contains("title=""a&quot;b""", title)
        Dim markup As String = BodyOf("[x](https://a.example ""<script>alert(1)</script>"")")
        Assert.Contains("title=""&lt;script&gt;alert(1)&lt;/script&gt;""", markup)
        Dim alt As String = BodyOf("![x"" onerror=""alert(1)](https://e.example/a.png)")
        Assert.DoesNotContain("<img", alt)
        Assert.DoesNotContain("""onerror", alt)
        Assert.DoesNotContain(" onerror=""", alt)
    End Sub

    <Fact>
    Public Sub Every_hostile_document_renders_only_the_closed_element_set()
        Dim documents As String() = {
            "<script>alert(1)</script>",
            "[x](javascript:alert(1))",
            "[x](JAVASCRIPT:alert(1))",
            "[x](javascript&#58;alert(1))",
            "[x](&#106;avascript:alert(1))",
            "[x]( javascript:alert(1))",
            "[x](https://a.example/""onmouseover=""alert(1))",
            "[x](https://a.example ""t"" onmouseover=""alert(1)"")",
            "<img src=x onerror=alert(1)>",
            "![x](javascript:alert(1))",
            "![x](https://a.example/a.png?""onerror=""alert(1))",
            "<a href=""javascript:alert(1)"">x</a>",
            "<javascript:alert(1)>",
            "[x]: javascript:alert(1)" & vbLf & vbLf & "[x]",
            "```""><script>alert(1)</script>" & vbLf & "code" & vbLf & "```",
            "# <script>alert(1)</script>",
            "| <img src=x onerror=alert(1)> | b |" & vbLf & "|---|---|" & vbLf & "| <svg onload=alert(1)> | y |",
            "<style>body{display:none}</style>",
            "<iframe src=""https://evil.example""></iframe>",
            "[x](https://a.example ""<script>alert(1)</script>"")",
            "&lt;script&gt;alert(1)&lt;/script&gt;",
            "&#60;script&#62;alert(1)&#60;/script&#62;",
            "<!-- comment --><script>x</script>",
            "<![CDATA[x]]>",
            "> <script>alert(1)</script>",
            "- <script>alert(1)</script>",
            "[<script>alert(1)</script>](https://a.example)",
            "[x](<javascript:alert(1)>)",
            "[x][y]" & vbLf & vbLf & "[y]: data:text/html;base64,PHNjcmlwdD4=",
            "<math><mi xlink:href=""javascript:alert(1)"">",
            "[a](https://a.example ""x"")[b](//evil.example)[c](https://u:p@evil.example/)",
            "***<b>*** __<i>__ ~~<u>~~ `<s>`"}
        For Each document As String In documents
            For Each theme As String In New String() {"system", "light", "dark", "sepia", "black"}
                Dim html As String = TextMarkdownRenderer.Render(document, theme)
                Validate(html)
                Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase)
                Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase)
                Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase)
            Next
        Next
    End Sub

    <Fact>
    Public Sub A_table_that_pads_a_small_source_into_a_huge_page_is_refused()
        Dim columns As String = String.Join("|", Enumerable.Repeat("a", TextMarkdownRenderer.MaxTableColumns))
        Dim dashes As String = String.Join("|", Enumerable.Repeat("-", TextMarkdownRenderer.MaxTableColumns))
        Dim source As New System.Text.StringBuilder("|" & columns & "|" & vbLf & "|" & dashes & "|" & vbLf)
        For row As Integer = 1 To 490000
            source.Append("a").Append(vbLf)
        Next
        Assert.True(TextMarkdownRenderer.IsRenderable(source.ToString()))
        Assert.Null(TextMarkdownRenderer.Render(source.ToString(), "light"))
    End Sub

    <Fact>
    Public Sub Closers_with_no_opener_stop_at_the_step_budget_and_stay_text()
        Dim paragraph As String = String.Concat(Enumerable.Repeat("a_ ", TextMarkdownRenderer.MaxDelimiters))
        Dim source As String = String.Join(vbLf & vbLf, Enumerable.Repeat(paragraph, 160))
        Assert.True(TextMarkdownRenderer.IsRenderable(source))
        Dim html As String = TextMarkdownRenderer.Render(source, "light")
        Validate(html)
        Assert.Contains("a_ a_", html)
        Assert.DoesNotContain("<em>", html)
    End Sub

    ' ---- themes ---------------------------------------------------------------------------------

    <Theory>
    <InlineData("light", "#fafafa", "#202020", "light")>
    <InlineData("dark", "#1e1e1e", "#dcdcdc", "dark")>
    <InlineData("sepia", "#f4ecd8", "#5b4636", "light")>
    <InlineData("black", "#000000", "#cccccc", "dark")>
    Public Sub Each_theme_writes_its_own_colours(theme As String, back As String, ink As String, scheme As String)
        Dim html As String = TextMarkdownRenderer.Render("x", theme)
        Assert.Contains("background:" & back, html)
        Assert.Contains("color:" & ink, html)
        Assert.Contains("color-scheme:" & scheme, html)
    End Sub

    <Fact>
    Public Sub The_system_theme_follows_the_colours_it_is_given()
        Dim options As New TextMarkdownOptions With {.Theme = "system", .SystemBack = Color.FromArgb(18, 52, 86), .SystemInk = Color.FromArgb(255, 238, 221)}
        Dim html As String = TextMarkdownRenderer.Render("x", options)
        Assert.Contains("background:#123456", html)
        Assert.Contains("color:#ffeedd", html)
        Assert.Contains("color-scheme:dark", html)
        Dim lightOptions As New TextMarkdownOptions With {.Theme = "system", .SystemBack = Color.White, .SystemInk = Color.Black}
        Assert.Contains("color-scheme:light", TextMarkdownRenderer.Render("x", lightOptions))
        ' An unknown stored theme reads as system.
        Dim unknown As New TextMarkdownOptions With {.Theme = "neon", .SystemBack = Color.FromArgb(18, 52, 86), .SystemInk = Color.FromArgb(255, 238, 221)}
        Assert.Contains("background:#123456", TextMarkdownRenderer.Render("x", unknown))
        ' The system muted ink is translucent in the palette: the page still carries opaque hex colours only.
        Assert.DoesNotContain("rgba", html)
    End Sub

    <Fact>
    Public Sub The_five_themes_write_five_different_stylesheets()
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each theme As String In New String() {"system", "light", "dark", "sepia", "black"}
            seen.Add(TextMarkdownRenderer.ThemeCss(New TextMarkdownOptions With {.Theme = theme, .SystemBack = Color.FromArgb(12, 34, 56), .SystemInk = Color.FromArgb(200, 210, 220)}))
        Next
        Assert.Equal(5, seen.Count)
    End Sub

    <Fact>
    Public Sub Fonts_size_and_spacing_are_clamped_and_checked()
        Dim css As String = TextMarkdownRenderer.ThemeCss(New TextMarkdownOptions With {.FontFamily = "Georgia", .CodeFontFamily = "Cascadia Code", .FontSizePoints = 14.0F, .LineSpacing = 1.5F})
        Assert.Contains("font-family:""Georgia"",sans-serif", css)
        Assert.Contains("font-family:""Cascadia Code"",monospace", css)
        Assert.Contains("font-size:14pt", css)
        Assert.Contains("line-height:1.5", css)
        Dim extreme As String = TextMarkdownRenderer.ThemeCss(New TextMarkdownOptions With {.FontSizePoints = 9999.0F, .LineSpacing = 99.0F})
        Assert.Contains("font-size:48pt", extreme)
        Assert.Contains("line-height:3", extreme)
        Dim tiny As String = TextMarkdownRenderer.ThemeCss(New TextMarkdownOptions With {.FontSizePoints = -5.0F, .LineSpacing = Single.NaN})
        Assert.Contains("font-size:6pt", tiny)
        Assert.Contains("line-height:1.2", tiny)
        Assert.Contains("font-size:12pt", TextMarkdownRenderer.ThemeCss(New TextMarkdownOptions With {.FontSizePoints = Single.NaN}))
    End Sub

    <Fact>
    Public Sub A_font_name_with_markup_falls_back_instead_of_reaching_the_stylesheet()
        Dim hostile As String = "Arial""; } body{background:url(//evil.example/x)} x{"
        Dim css As String = TextMarkdownRenderer.ThemeCss(New TextMarkdownOptions With {.FontFamily = hostile, .CodeFontFamily = "</style><script>alert(1)</script>"})
        Assert.DoesNotContain("evil", css)
        Assert.DoesNotContain("<", css)
        Assert.Contains("font-family:""Segoe UI"",sans-serif", css)
        Assert.Contains("font-family:""Consolas"",monospace", css)
        Assert.DoesNotContain("@", css)
    End Sub

    ' ---- bounds ---------------------------------------------------------------------------------

    Private Shared Sub AssertBounded(document As String, ByRef depth As Integer)
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim html As String = TextMarkdownRenderer.Render(document, "dark")
        watch.Stop()
        Assert.True(watch.ElapsedMilliseconds < 15000, "the render took " & watch.ElapsedMilliseconds.ToString() & " ms")
        depth = Validate(html)
        Assert.True(depth < 200, "element nesting " & depth.ToString())
    End Sub

    <Fact>
    Public Sub Unclosed_brackets_and_destinations_stay_linear()
        Dim depth As Integer
        AssertBounded(New String("["c, 200000), depth)
        AssertBounded(Repeat("[a](", 50000), depth)
        AssertBounded(Repeat("![", 50000), depth)
        AssertBounded("[a](" & New String("b"c, 900000), depth)
        AssertBounded(Repeat("[a][b] ", 100000), depth)
        AssertBounded(Repeat("[x](y ""z", 50000), depth)
        AssertBounded(Repeat("<a", 100000), depth)
        AssertBounded(Repeat("<http://", 50000), depth)
    End Sub

    <Fact>
    Public Sub A_ladder_of_backtick_runs_does_not_go_quadratic()
        Dim ladder As New System.Text.StringBuilder()
        For n As Integer = 1 To 1000
            ladder.Append("`"c, n).Append(" "c)
        Next
        Dim depth As Integer
        AssertBounded(ladder.ToString(), depth)
        AssertBounded(Repeat("` ``", 100000), depth)
    End Sub

    <Fact>
    Public Sub An_emphasis_soup_matches_a_bounded_number_of_pairs()
        Dim depth As Integer
        AssertBounded(Repeat("*a _b ", 100000), depth)
        AssertBounded(Repeat("*a ", 5000) & Repeat("a* ", 5000), depth)
        Assert.True(depth > 0)
        AssertBounded(Repeat("**a ", 5000) & Repeat("a** ", 5000), depth)
        AssertBounded(Repeat("~~a ", 5000) & Repeat("a~~ ", 5000), depth)
        AssertBounded(Repeat("*", 500000), depth)
        AssertBounded(Repeat("_", 500000), depth)
    End Sub

    <Fact>
    Public Sub Deep_nesting_of_quotes_and_lists_stops_at_the_depth_cap()
        Dim depth As Integer
        Dim quotes As String = TextMarkdownRenderer.Render(Repeat(">", 5000) & " deep", "light")
        Assert.True(CountOf(quotes, "<blockquote>") <= TextMarkdownRenderer.MaxBlockDepth + 1)
        Assert.Contains("deep", quotes)
        Validate(quotes)
        Dim bullets As String = TextMarkdownRenderer.Render(Repeat("- ", 5000) & "deep", "light")
        Assert.True(CountOf(bullets, "<ul>") <= TextMarkdownRenderer.MaxBlockDepth + 1)
        Assert.Contains("deep", bullets)
        Validate(bullets)
        AssertBounded(Repeat("1. ", 5000) & "deep", depth)
        AssertBounded(Repeat("> - ", 3000) & "deep", depth)
        Dim staircase As New System.Text.StringBuilder()
        For n As Integer = 0 To 499
            staircase.Append(New String(" "c, n * 2)).Append("- item ").Append(n).Append(vbLf)
        Next
        AssertBounded(staircase.ToString(), depth)
        Dim quoteLines As New System.Text.StringBuilder()
        For n As Integer = 1 To 400
            quoteLines.Append(Repeat(">", n)).Append(" line ").Append(n).Append(vbLf)
        Next
        AssertBounded(quoteLines.ToString(), depth)
    End Sub

    <Fact>
    Public Sub A_huge_source_is_cut_and_a_huge_line_is_one_paragraph()
        Dim source As String = New String("a"c, TextMarkdownRenderer.MaxSourceChars + 500000)
        Dim html As String = TextMarkdownRenderer.Render(source, "light")
        Validate(html)
        Assert.True(html.Length < TextMarkdownRenderer.MaxSourceChars + 20000)
        Assert.False(TextMarkdownRenderer.IsRenderable(source))
        Dim depth As Integer
        AssertBounded(Repeat("line" & vbLf, 150000), depth)
        AssertBounded(Repeat("# h" & vbLf, 6000), depth)
    End Sub

    <Fact>
    Public Sub Wide_tables_and_long_tables_stay_bounded()
        Dim depth As Integer
        Dim wideHeader As String = "|" & Repeat(" a |", 100)
        Dim wideDelimiter As String = "|" & Repeat("---|", 100)
        Dim wide As String = TextMarkdownRenderer.Render(wideHeader & vbLf & wideDelimiter & vbLf & wideHeader, "light")
        Assert.DoesNotContain("<table", wide)
        Validate(wide)
        Dim rows As New System.Text.StringBuilder()
        rows.Append("| a | b | c |").Append(vbLf).Append("|---|---|---|").Append(vbLf)
        For n As Integer = 1 To 5000
            rows.Append("| ").Append(n).Append(" | *x* | `y` |").Append(vbLf)
        Next
        AssertBounded(rows.ToString(), depth)
        Assert.Equal(5001, CountOf(TextMarkdownRenderer.Render(rows.ToString(), "light"), "<tr>"))
    End Sub

    <Fact>
    Public Sub Many_definitions_and_references_stay_bounded()
        Dim definitions As New System.Text.StringBuilder()
        For n As Integer = 1 To 8000
            definitions.Append("[r").Append(n).Append("]: https://example.com/").Append(n).Append(vbLf)
        Next
        Dim depth As Integer
        AssertBounded(definitions.ToString() & vbLf & Repeat("[r1][r2] [r7999] ", 20000), depth)
    End Sub

End Class
#End If
