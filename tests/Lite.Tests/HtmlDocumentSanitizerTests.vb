#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports AngleSharp.Html.Dom
Imports AngleSharp.Html.Parser
Imports Xunit

' Specification 275 work package W02, part B: the sanitizer is judged on the DOM of its own
' output (re-parsed here), never on substrings alone.
Public Class HtmlDocumentSanitizerTests
    Private Shared ReadOnly Origin As New Uri("https://sabc.fms-doc.invalid/")
    ' U+041A U+043D U+0438 U+0433 U+0430 (Cyrillic "book").
    Private Shared ReadOnly Book As String = ChrW(&H41A) & ChrW(&H43D) & ChrW(&H438) & ChrW(&H433) & ChrW(&H430)

    Shared Sub New()
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
    End Sub

    Private Shared Function Options(Optional transport As String = "", Optional entry As String = "dir/page.html", Optional approved As String() = Nothing,
                                    Optional origin As Uri = Nothing, Optional profile As HtmlSourceKind = HtmlSourceKind.LocalFile) As HtmlSanitizeOptions
        Return New HtmlSanitizeOptions With {
            .Origin = If(origin, HtmlDocumentSanitizerTests.Origin), .EntryKey = entry, .TransportCharset = transport, .Profile = profile,
            .ApprovedOrigins = If(approved Is Nothing, Nothing, New HashSet(Of String)(approved, StringComparer.Ordinal))}
    End Function

    Private Shared Function Run(markup As String, Optional configured As HtmlSanitizeOptions = Nothing) As HtmlSanitizeResult
        Return HtmlDocumentSanitizer.Sanitize(Encoding.UTF8.GetBytes(markup), If(configured, Options()), CancellationToken.None)
    End Function

    Private Shared Function Dom(result As HtmlSanitizeResult) As IHtmlDocument
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Return New HtmlParser().ParseDocument(Encoding.UTF8.GetString(result.Bytes))
    End Function

    Private Shared Function Clean(markup As String, Optional configured As HtmlSanitizeOptions = Nothing) As IHtmlDocument
        Return Dom(Run(markup, configured))
    End Function

    Private Shared Function Join(ParamArray parts As Byte()()) As Byte()
        Return parts.SelectMany(Function(part) part).ToArray()
    End Function

    Private Shared Function AttributeNames(document As IHtmlDocument) As List(Of String)
        Return document.All.SelectMany(Function(e) e.Attributes.Select(Function(a) a.Name)).ToList()
    End Function

    Private Shared Function Sorted(values As IEnumerable(Of String)) As String()
        Return values.OrderBy(Function(v) v, StringComparer.Ordinal).ToArray()
    End Function

#Region "H05 encoding and structure"

    <Theory>
    <InlineData("utf-8")>
    <InlineData("utf-16")>
    <InlineData("utf-16be")>
    Public Sub Bom_wins_over_a_conflicting_meta_and_transport_charset(kind As String)
        Dim enc As Encoding = If(kind = "utf-8", Encoding.UTF8, If(kind = "utf-16", Encoding.Unicode, Encoding.BigEndianUnicode))
        Dim body = enc.GetBytes("<meta charset=windows-1251><title>" & Book & "</title><p>" & Book)
        Dim result = HtmlDocumentSanitizer.Sanitize(Join(enc.GetPreamble(), body), Options(transport:="windows-1251"), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.Equal(Book, result.Title)
        Assert.Equal(enc.WebName, result.CharacterSet)
        Assert.Contains(Book, Dom(result).Body.TextContent)
    End Sub

    <Fact>
    Public Sub Transport_charset_beats_a_conflicting_meta()
        Dim cp1251 = Encoding.GetEncoding(1251)
        Dim legacy = HtmlDocumentSanitizer.Sanitize(cp1251.GetBytes("<meta charset=utf-8><title>" & Book & "</title>"), Options(transport:="windows-1251"), CancellationToken.None)
        Assert.Equal(Book, legacy.Title)
        Assert.Equal("windows-1251", legacy.CharacterSet)
        Dim modern = HtmlDocumentSanitizer.Sanitize(Encoding.UTF8.GetBytes("<meta charset=windows-1251><title>" & Book & "</title>"), Options(transport:="UTF-8"), CancellationToken.None)
        Assert.Equal(Book, modern.Title)
        Assert.Equal("utf-8", modern.CharacterSet)
    End Sub

    <Fact>
    Public Sub Unknown_transport_charset_falls_through_to_the_meta()
        Dim bytes = Encoding.GetEncoding(1251).GetBytes("<meta charset=windows-1251><title>" & Book & "</title>")
        Dim result = HtmlDocumentSanitizer.Sanitize(bytes, Options(transport:="no-such-charset"), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.Equal(Book, result.Title)
    End Sub

    <Theory>
    <InlineData("<meta charset=windows-1251>")>
    <InlineData("<meta charset=""WINDOWS-1251"">")>
    <InlineData("<META HTTP-EQUIV=""Content-Type"" CONTENT=""text/html; charset=windows-1251"">")>
    <InlineData("<meta http-equiv=content-type content='text/html;charset=windows-1251'>")>
    Public Sub Meta_declared_cp1251_cyrillic_decodes(meta As String)
        Dim bytes = Encoding.GetEncoding(1251).GetBytes("<html><head>" & meta & "<title>" & Book & "</title></head><body><p>" & Book & "</p></body></html>")
        Dim result = HtmlDocumentSanitizer.Sanitize(bytes, Options(), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.Equal("windows-1251", result.CharacterSet)
        Assert.Equal(Book, result.Title)
        Assert.Equal(Book, Dom(result).QuerySelector("p").TextContent)
    End Sub

    <Theory>
    <InlineData("iso-8859-1")>
    <InlineData("windows-1252")>
    <InlineData("latin1")>
    Public Sub Meta_declared_windows_1252_keeps_the_extended_punctuation(label As String)
        Dim text = "caf" & ChrW(&HE9) & " " & ChrW(&H2013) & " " & ChrW(&H201C) & "q" & ChrW(&H201D)
        Dim bytes = Encoding.GetEncoding(1252).GetBytes("<meta charset=" & label & "><p>" & text & "</p>")
        Dim result = HtmlDocumentSanitizer.Sanitize(bytes, Options(), CancellationToken.None)
        Assert.Equal("windows-1252", result.CharacterSet)
        Assert.Equal(text, Dom(result).QuerySelector("p").TextContent)
    End Sub

    <Theory>
    <InlineData("shift_jis", 932, "65E5 672C 8A9E 30C6 30AD 30B9 30C8")>
    <InlineData("gb18030", 54936, "4E2D 6587 6D4B 8BD5 20AC")>
    <InlineData("gb2312", 54936, "4E2D 6587 6D4B 8BD5")>
    <InlineData("big5", 950, "7E41 9AD4 4E2D 6587")>
    <InlineData("euc-kr", 51949, "D55C AE00")>
    Public Sub Meta_declared_cjk_encodings_decode(label As String, codePage As Integer, codePoints As String)
        Dim text = String.Concat(codePoints.Split(" "c).Select(Function(hex) ChrW(Integer.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture))))
        Dim bytes = Encoding.GetEncoding(codePage).GetBytes("<html><head><meta charset=" & label & "><title>" & text & "</title></head><body><p>" & text & "</p></body></html>")
        Dim result = HtmlDocumentSanitizer.Sanitize(bytes, Options(), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.Equal(text, result.Title)
        Assert.Equal(text, Dom(result).QuerySelector("p").TextContent)
    End Sub

    <Fact>
    Public Sub Unknown_declared_charset_is_not_a_failure_and_utf8_text_survives()
        Dim result = Run("<meta charset=""no-such-charset-9""><title>" & Book & "</title><p>" & Book)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.Equal("utf-8", result.CharacterSet)
        Assert.Equal(Book, result.Title)
    End Sub

    <Fact>
    Public Sub Utf7_and_utf32_labels_are_refused_like_a_browser()
        Dim result = Run("<meta charset=utf-7><title>+ADw-script+AD4-</title><p>" & Book)
        Assert.Equal("utf-8", result.CharacterSet)
        Assert.Equal(Book, Dom(result).QuerySelector("p").TextContent)
    End Sub

    <Fact>
    Public Sub Undeclared_non_utf8_bytes_fall_back_to_windows_1252()
        Dim result = HtmlDocumentSanitizer.Sanitize(Encoding.GetEncoding(1252).GetBytes("<p>caf" & ChrW(&HE9) & "</p>"), Options(), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.Equal("windows-1252", result.CharacterSet)
        Assert.Equal("caf" & ChrW(&HE9), Dom(result).QuerySelector("p").TextContent)
    End Sub

    <Fact>
    Public Sub Undeclared_valid_utf8_stays_utf8()
        Dim result = Run("<p>" & Book & "</p>")
        Assert.Equal("utf-8", result.CharacterSet)
        Assert.Equal(Book, Dom(result).QuerySelector("p").TextContent)
    End Sub

    <Fact>
    Public Sub A_meta_inside_a_comment_is_not_a_declaration()
        Dim result = Run("<!-- <meta charset=windows-1251> --><meta charset=utf-8><title>" & Book & "</title>")
        Assert.Equal("utf-8", result.CharacterSet)
        Assert.Equal(Book, result.Title)
    End Sub

    <Fact>
    Public Sub Utf16_without_a_bom_is_recognized_from_its_nul_pattern()
        Dim result = HtmlDocumentSanitizer.Sanitize(Encoding.Unicode.GetBytes("<html><body><p>" & Book & " and some more ascii text</p></body></html>"), Options(), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.Equal("utf-16", result.CharacterSet)
        Assert.Contains(Book, Dom(result).QuerySelector("p").TextContent)
    End Sub

    <Fact>
    Public Sub Binary_data_is_an_invalid_encoding_not_a_document()
        Dim result = HtmlDocumentSanitizer.Sanitize(New Byte(1999) {}, Options(), CancellationToken.None)
        Assert.Equal(HtmlOutcome.InvalidEncoding, result.Status)
        Assert.Empty(result.Bytes)
    End Sub

    <Fact>
    Public Sub Malformed_nesting_and_unclosed_tags_still_produce_a_well_formed_document()
        Dim document = Clean("<div><p>one<b>two</div><span>three<table><tr><td>cell</table><ul><li>a<li>b</ul><i>tail")
        Dim text = document.Body.TextContent
        For Each part In {"one", "two", "three", "cell", "tail"}
            Assert.Contains(part, text)
        Next
        Assert.Equal(2, document.QuerySelectorAll("li").Length)
        Assert.NotNull(document.QuerySelector("table td"))
    End Sub

    <Fact>
    Public Sub Malformed_svg_and_mathml_survive_and_stop_swallowing_the_rest()
        Dim document = Clean("<svg viewBox=""0 0 10 10""><g><circle r=3 /><text>t1</svg><p id=after>after</p><math><mfrac><mi>a<mi>b</math><p id=after2>x")
        Dim svg = document.QuerySelector("svg")
        Assert.NotNull(svg)
        Assert.Equal("0 0 10 10", svg.GetAttribute("viewBox"))
        Assert.NotNull(document.QuerySelector("svg circle"))
        Assert.Equal("t1", document.QuerySelector("svg text").TextContent)
        Assert.NotNull(document.QuerySelector("math mfrac"))
        Assert.Equal("after", document.GetElementById("after").TextContent)
        Assert.Equal("x", document.GetElementById("after2").TextContent)
    End Sub

    <Fact>
    Public Sub Anchors_ids_direction_language_tables_and_ruby_are_preserved()
        Dim result = Run("<!doctype html><html lang=""ar"" dir=""rtl""><head><title>  Title   here </title></head><body>" &
                         "<h1 id=""top"">H</h1><a id=""x"" href=""#top"">up</a><a href=""other.html#frag"">rel</a><ul><li>1</li></ul><table><tr><td>c</td></tr></table>" &
                         "<ruby>X<rt>zi</rt><rp>(</rp></ruby><details><summary>s</summary>d</details><p dir=""ltr"" lang=""en"">text</p><style>p{color:red}</style></body></html>")
        Assert.Equal("ar", result.Language)
        Assert.Equal("rtl", result.Direction)
        Assert.Equal("Title here", result.Title)
        Dim document = Dom(result)
        Assert.Equal("rtl", document.DocumentElement.GetAttribute("dir"))
        Assert.Equal("#top", document.GetElementById("x").GetAttribute("href"))
        Assert.Equal("other.html#frag", document.QuerySelectorAll("a")(1).GetAttribute("href"))
        Assert.Equal("zi", document.QuerySelector("ruby rt").TextContent)
        Assert.NotNull(document.QuerySelector("ruby rp"))
        Assert.NotNull(document.QuerySelector("details summary"))
        Assert.NotNull(document.QuerySelector("table td"))
        Assert.Equal("ltr", document.QuerySelector("p").GetAttribute("dir"))
        Assert.Equal("p{color:red}", document.QuerySelector("style").TextContent)
    End Sub

    <Fact>
    Public Sub Direction_falls_back_to_the_body_and_is_empty_when_unknown()
        Assert.Equal("rtl", Run("<body dir=RTL><p>x").Direction)
        Assert.Equal("", Run("<html dir=sideways><p>x").Direction)
        Assert.Equal("auto", Run("<html dir=auto><p>x").Direction)
        Assert.Equal("", Run("<p>x").Language)
    End Sub

    <Fact>
    Public Sub Output_is_utf8_without_bom_starting_with_the_html5_doctype_and_the_input_is_untouched()
        Dim input = Encoding.UTF8.GetBytes("<title>" & Book & "</title><p>x")
        Dim copy = CType(input.Clone(), Byte())
        Dim result = HtmlDocumentSanitizer.Sanitize(input, Options(), CancellationToken.None)
        Assert.Equal(copy, input)
        Assert.NotSame(input, result.Bytes)
        Assert.StartsWith("<!DOCTYPE html>", Encoding.ASCII.GetString(result.Bytes, 0, 15), StringComparison.Ordinal)
        Assert.NotEqual(CByte(&HEF), result.Bytes(0))
        Assert.Contains(Book, Encoding.UTF8.GetString(result.Bytes))
    End Sub

    <Fact>
    Public Sub Element_count_matches_the_reparsed_output()
        Dim result = Run("<title>t</title><div><p>a</p><p>b<br></p><img src=""x.png""></div>")
        Assert.True(result.ElementCount > 0)
        Assert.Equal(Dom(result).All.Length, result.ElementCount)
    End Sub

#End Region

#Region "H06 active content"

    <Fact>
    Public Sub Scripts_and_event_handlers_are_removed_everywhere()
        Dim result = Run("<html onload=""x()""><head><script src=""a.js""></script><script>alert(1)</script></head><body onclick=""x()"" ONLOAD=""y()"">" &
                         "<img src=""a.png"" onerror=""alert(1)""><p onmouseover=""x"" id=""keep"">t</p><a href=""p.html"" onfocus=""x"">l</a></body></html>")
        Dim document = Dom(result)
        Assert.Empty(document.QuerySelectorAll("script"))
        Assert.DoesNotContain(AttributeNames(document), Function(n) n.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        Assert.NotNull(document.GetElementById("keep"))
        Assert.Equal("a.png", document.QuerySelector("img").GetAttribute("src"))
        Assert.Equal(2, result.RemovedActiveElements)
    End Sub

    <Fact>
    Public Sub Svg_scripts_handlers_and_href_animation_are_removed_but_html_inside_foreignobject_is_cleaned()
        Dim document = Clean("<svg onload=""alert(1)"" width=""10""><script>alert(1)</script>" &
                             "<a xlink:href=""javascript:alert(1)"" href=""javascript:alert(2)"" target=""_blank""><text onclick=""x()"">label</text></a>" &
                             "<set attributeName=""href"" to=""javascript:alert(3)""/><animate attributeName=""xlink:href"" values=""javascript:alert(4)""/>" &
                             "<animate attributeName=""opacity"" from=""0"" to=""1""/>" &
                             "<foreignObject><div onclick=""x""><script>s()</script><p id=""fo"">html in svg</p></div></foreignObject></svg>")
        Assert.Empty(document.QuerySelectorAll("script"))
        Assert.Empty(document.QuerySelectorAll("set"))
        Assert.Equal(1, document.QuerySelectorAll("animate").Length)
        Dim names = AttributeNames(document)
        Assert.DoesNotContain(names, Function(n) n.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        Assert.DoesNotContain(names, Function(n) n.EndsWith("href", StringComparison.OrdinalIgnoreCase))
        Assert.DoesNotContain("target", names)
        Assert.Equal("label", document.QuerySelector("svg a text").TextContent)
        Assert.Equal("10", document.QuerySelector("svg").GetAttribute("width"))
        Assert.Equal("html in svg", document.GetElementById("fo").TextContent)
    End Sub

    <Theory>
    <InlineData("javascript:alert(1)")>
    <InlineData("  JaVaScRiPt:alert(1)")>
    <InlineData("java&#x09;script:alert(1)")>
    <InlineData("java&#10;script:alert(1)")>
    <InlineData("&#14;javascript:alert(1)")>
    <InlineData("vbscript:msgbox(1)")>
    <InlineData("data:text/html;base64,PHNjcmlwdD4=")>
    <InlineData("file:///C:/Windows/win.ini")>
    <InlineData("blob:https://sabc.fms-doc.invalid/1")>
    <InlineData("custom-app://open")>
    <InlineData("ms-settings:network")>
    <InlineData("C:\Windows\win.ini")>
    <InlineData("tel:123456")>
    Public Sub Dangerous_navigation_schemes_are_dropped_from_href(href As String)
        Dim document = Clean("<a id=""l"" href=""" & href & """>x</a><area shape=""rect"" href=""" & href & """><svg><a id=""s"" xlink:href=""" & href & """><text>t</text></a></svg>")
        Assert.False(document.GetElementById("l").HasAttribute("href"))
        Assert.False(document.QuerySelector("area").HasAttribute("href"))
        Assert.DoesNotContain(AttributeNames(document), Function(n) n.EndsWith("href", StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub Navigation_links_keep_relative_fragment_external_and_mail_targets()
        Dim result = Run("<a id=""a1"" href=""page.html"">1</a><a id=""a2"" href=""#top"">2</a><a id=""a3"" href=""https://other.example/page?q=1"">3</a>" &
                         "<a id=""a4"" href=""mailto:someone@example.org"">4</a><area id=""r1"" href=""mailto:x@example.org""><blockquote id=""q"" cite=""javascript:1"">q</blockquote>")
        Dim document = Dom(result)
        Assert.Equal("page.html", document.GetElementById("a1").GetAttribute("href"))
        Assert.Equal("#top", document.GetElementById("a2").GetAttribute("href"))
        Assert.Equal("https://other.example/page?q=1", document.GetElementById("a3").GetAttribute("href"))
        Assert.Equal("mailto:someone@example.org", document.GetElementById("a4").GetAttribute("href"))
        Assert.False(document.GetElementById("r1").HasAttribute("href"))
        Assert.False(document.GetElementById("q").HasAttribute("cite"))
        Assert.Equal(0, result.BlockedResources)
        Assert.Empty(result.ExternalOrigins)
    End Sub

    <Fact>
    Public Sub Targets_downloads_pings_and_authoring_hooks_are_removed()
        Dim document = Clean("<a id=""a"" href=""p.html"" target=""_blank"" download=""x"" ping=""https://t.example/"" rel=""noopener"" contenteditable=""true"" autofocus formaction=""x"" formtarget=""_blank"" nonce=""n"" integrity=""sha256-x"" popover popovertarget=""p"" srcdoc=""x"">l</a>" &
                             "<div id=""d"" contenteditable>e</div>")
        Dim a = document.GetElementById("a")
        Assert.Equal("p.html", a.GetAttribute("href"))
        Assert.Equal("noopener", a.GetAttribute("rel"))
        Assert.Equal(3, a.Attributes.Length)
        Assert.False(document.GetElementById("d").HasAttribute("contenteditable"))
    End Sub

    <Fact>
    Public Sub Meta_refresh_cookie_and_pragma_are_removed_and_csp_is_kept()
        Dim result = Run("<head><meta http-equiv=""refresh"" content=""0;url=https://evil.example/""><meta http-equiv=""Set-Cookie"" content=""a=b"">" &
                         "<meta http-equiv=""default-style"" content=""x""><meta http-equiv=""content-type"" content=""text/html; charset=windows-1251""><meta http-equiv=""X-UA-Compatible"" content=""IE=7"">" &
                         "<meta http-equiv=""Content-Security-Policy"" content=""default-src 'self'""><meta name=""referrer"" content=""unsafe-url""><meta name=""viewport"" content=""width=device-width""></head><p>x")
        Dim document = Dom(result)
        Dim equivs = document.QuerySelectorAll("meta[http-equiv]")
        Assert.Equal("Content-Security-Policy", Assert.Single(equivs).GetAttribute("http-equiv"))
        Assert.Equal("default-src 'self'", equivs(0).GetAttribute("content"))
        Assert.Empty(document.QuerySelectorAll("meta[name=referrer]"))
        Assert.Single(document.QuerySelectorAll("meta[name=viewport]"))
        Assert.Equal(2, result.RemovedActiveElements)
    End Sub

    <Fact>
    Public Sub Exactly_one_utf8_charset_meta_comes_first_in_head()
        Dim bytes = Encoding.GetEncoding(1252).GetBytes("<html><head><title>t</title><meta charset=iso-8859-1><meta http-equiv=""Content-Type"" content=""text/html; charset=iso-8859-1""><meta charset=""windows-1251""></head><body>x</body></html>")
        Dim document = Dom(HtmlDocumentSanitizer.Sanitize(bytes, Options(), CancellationToken.None))
        Dim metas = document.QuerySelectorAll("meta[charset]")
        Assert.Equal("utf-8", Assert.Single(metas).GetAttribute("charset"))
        Assert.Same(metas(0), document.Head.FirstElementChild)
        Assert.Empty(document.QuerySelectorAll("meta[http-equiv]"))
    End Sub

    <Fact>
    Public Sub A_document_without_head_or_charset_still_gets_the_single_charset_meta()
        Dim document = Clean("<p>plain")
        Assert.Equal("utf-8", Assert.Single(document.QuerySelectorAll("meta[charset]")).GetAttribute("charset"))
    End Sub

    <Fact>
    Public Sub Base_outside_the_session_origin_is_removed()
        For Each href In {"https://evil.example/", "http://sabc.fms-doc.invalid/", "//evil.example/x/", "javascript:alert(1)", "data:text/html,x", "file:///C:/", "https://sabc.fms-doc.invalid:8443/"}
            Dim document = Clean("<head><base href=""" & href & """ target=""_blank""></head><a href=""x.html"">x</a>")
            Assert.Empty(document.QuerySelectorAll("base"))
        Next
        Assert.Empty(Clean("<head><base target=""_blank""></head><p>x").QuerySelectorAll("base"))
    End Sub

    <Fact>
    Public Sub Base_inside_the_session_origin_becomes_a_root_relative_path()
        Dim absolute = Clean("<head><base href=""https://sabc.fms-doc.invalid/sub/dir/?q=1#f"" target=""_blank""></head><p>x")
        Dim base = Assert.Single(absolute.QuerySelectorAll("base"))
        Assert.Equal("/sub/dir/", base.GetAttribute("href"))
        Assert.Equal(1, base.Attributes.Length)
        Assert.Equal("/dir/sub/", Clean("<head><base href=""sub/""></head><p>x").QuerySelector("base").GetAttribute("href"))
        Assert.Equal("/dir/other/", Clean("<head><base href=""../other/""></head><p>x", Options(entry:="dir/deeper/page.html")).QuerySelector("base").GetAttribute("href"))
        Assert.Equal("/", Clean("<head><base href=""/""></head><p>x").QuerySelector("base").GetAttribute("href"))
        Assert.Equal("/chap%20ter/", Clean("<head><base href=""chap%20ter/""></head><p>x", Options(entry:="page.html")).QuerySelector("base").GetAttribute("href"))
    End Sub

    <Fact>
    Public Sub Only_the_first_valid_base_survives()
        Dim document = Clean("<head><base href=""https://evil.example/""><base href=""a/""><base href=""b/""></head><p>x")
        Assert.Equal("/dir/a/", Assert.Single(document.QuerySelectorAll("base")).GetAttribute("href"))
    End Sub

    <Fact>
    Public Sub Frames_objects_embeds_and_other_containers_are_removed()
        Dim result = Run("<iframe src=""x.html"" srcdoc=""&lt;script&gt;alert(1)&lt;/script&gt;""></iframe><object data=""a.swf""><param name=""x"" value=""y""><p>object fallback</p></object>" &
                         "<embed src=""a.swf""><applet code=""x""></applet><portal src=""x""></portal><template><script>1</script><p>tpl</p></template>" &
                         "<audio src=""a.mp3"" controls>audio fallback</audio><video poster=""p.png"" src=""v.mp4""><source src=""v.mp4""><track src=""t.vtt"">video fallback</video><p id=""after"">after</p>")
        Dim document = Dom(result)
        For Each selector In {"iframe", "object", "embed", "applet", "portal", "template", "param", "audio", "video", "source", "track", "script"}
            Assert.Empty(document.QuerySelectorAll(selector))
        Next
        Assert.DoesNotContain("srcdoc", AttributeNames(document))
        Dim text = document.Body.TextContent
        Assert.Contains("audio fallback", text)
        Assert.Contains("video fallback", text)
        Assert.DoesNotContain("tpl", text)
        Assert.NotNull(document.GetElementById("after"))
        Assert.Equal(8, result.RemovedActiveElements)
    End Sub

    <Fact>
    Public Sub A_frameset_document_loses_every_frame()
        Dim document = Clean("<html><head><title>f</title></head><frameset cols=""50%,50%""><frame src=""a.html""><frame src=""b.html""><noframes>no frames</noframes></frameset></html>")
        Assert.Empty(document.QuerySelectorAll("frameset"))
        Assert.Empty(document.QuerySelectorAll("frame"))
    End Sub

    <Fact>
    Public Sub Noscript_content_is_unwrapped_because_scripts_are_off()
        Dim result = Run("<head><noscript><link rel=""stylesheet"" href=""ns.css""></noscript></head><body><noscript><p id=""fb"">fallback</p><img src=""https://t.example/p.gif""></noscript>")
        Dim document = Dom(result)
        Assert.Empty(document.QuerySelectorAll("noscript"))
        Assert.Equal("fallback", document.GetElementById("fb").TextContent)
        Assert.Equal("ns.css", document.QuerySelector("link[rel=stylesheet]").GetAttribute("href"))
        Assert.Equal(1, result.BlockedResources)
    End Sub

    <Fact>
    Public Sub Forms_are_flattened_into_inert_readable_content()
        Dim result = Run("<form action=""https://evil.example/post"" method=""post"" target=""_blank"" id=""f"" class=""c""><input type=""text"" name=""n"" value=""Alice"" id=""t""><input type=""email"" value=""a@b.c"">" &
                         "<input type=""checkbox"" checked><input type=""radio""><input type=""password"" value=""secret""><input type=""hidden"" name=""t"" value=""tok""><input type=""file"">" &
                         "<button type=""submit"">Send <b>now</b></button><input type=""submit"" value=""Go""><select><option>one<option selected>two<option>three</select>" &
                         "<textarea>line1" & vbLf & "  line2</textarea><output>42</output><datalist id=""d""><option>x</datalist><keygen></form><p id=""after"">after</p>")
        Dim document = Dom(result)
        For Each selector In {"form", "input", "button", "select", "textarea", "output", "datalist", "keygen", "option"}
            Assert.Empty(document.QuerySelectorAll(selector))
        Next
        Dim container = document.GetElementById("f")
        Assert.Equal("div", container.LocalName)
        Assert.Equal("c", container.GetAttribute("class"))
        Assert.False(container.HasAttribute("action"))
        Assert.False(container.HasAttribute("method"))
        Assert.False(container.HasAttribute("target"))
        Dim spans = container.Children.Select(Function(c) c.TextContent).ToArray()
        Assert.Equal(New String() {"Alice", "a@b.c", "[x]", "[ ]", "Send now", "Go", "two", "line1" & vbLf & "  line2", "42"}, spans)
        Assert.Equal("span", document.GetElementById("t").LocalName)
        Assert.Contains("white-space:pre-wrap", container.Children(7).GetAttribute("style"))
        Assert.Equal("b", container.Children(4).Children(0).LocalName)
        Dim output = Encoding.UTF8.GetString(result.Bytes)
        Assert.DoesNotContain("secret", output)
        Assert.DoesNotContain("tok", output)
        Assert.Equal(15, result.RemovedActiveElements)
        Assert.NotNull(document.GetElementById("after"))
    End Sub

    <Fact>
    Public Sub An_image_input_becomes_a_picture_that_follows_the_image_rules()
        Dim result = Run("<input type=""image"" id=""ok"" src=""btn.png"" alt=""go""><input type=""image"" src=""https://btn.example.io/b.png"" alt=""ext"">")
        Dim document = Dom(result)
        Dim image = Assert.Single(document.QuerySelectorAll("img"))
        Assert.Equal("btn.png", image.GetAttribute("src"))
        Assert.Equal("go", image.GetAttribute("alt"))
        Assert.Empty(document.QuerySelectorAll("input"))
        Assert.Equal(1, result.BlockedResources)
        Assert.Equal(New String() {"https://btn.example.io"}, result.ExternalOrigins.ToArray())
    End Sub

    <Fact>
    Public Sub Speculative_and_non_stylesheet_links_are_removed()
        Dim rels = {"prefetch", "preconnect", "dns-prefetch", "preload", "modulepreload", "prerender", "manifest", "icon", "shortcut icon", "alternate", "pingback", "serviceworker", "import", "canonical", "stylesheet preload", "alternate stylesheet"}
        Dim markup = New StringBuilder("<head>")
        For Each rel In rels
            markup.Append("<link rel=""").Append(rel).Append(""" href=""https://x.example/r"" type=""application/rss+xml"">")
        Next
        markup.Append("<link rel=""stylesheet"" href=""css/site.css"" integrity=""sha256-x"" nonce=""n""><link rel=""prefetch""></head><p>x")
        Dim result = Run(markup.ToString())
        Dim document = Dom(result)
        Dim link = Assert.Single(document.QuerySelectorAll("link"))
        Assert.Equal("css/site.css", link.GetAttribute("href"))
        Assert.False(link.HasAttribute("integrity"))
        Assert.False(link.HasAttribute("nonce"))
        Assert.Equal(0, result.BlockedResources)
    End Sub

    <Fact>
    Public Sub Style_elements_and_attributes_are_kept_as_authored()
        Dim css = "@import url(https://x.example/a.css);p{background:url(img/a.png)}"
        Dim document = Clean("<style>" & css & "</style><p id=""p"" style=""color:blue;background:url(x.png)"">t</p>")
        Assert.Equal(css, document.QuerySelector("style").TextContent)
        Assert.Equal("color:blue;background:url(x.png)", document.GetElementById("p").GetAttribute("style"))
    End Sub

#End Region

#Region "H06 resource references"

    <Fact>
    Public Sub External_resources_are_blocked_counted_and_their_origins_listed()
        Dim result = Run("<body background=""https://bg.example/bg.png""><img id=""i1"" src=""https://cdn.example.com/a.png""><IMG id=""i2"" SRC=""//CDN2.Example.com/b.png"" ONERROR=""x""><img id=""i3"" src=""http://host.example:8080/c.png"">" &
                         "<link rel=""stylesheet"" href=""https://fonts.example.org/x.css""><svg><image id=""si"" href=""https://svg.example.net/i.png""/><use id=""su"" xlink:href=""https://svg.example.net/s.svg#a""/><feImage href=""https://svg2.example.net/f.png""/></svg>" &
                         "<input type=""image"" src=""https://btn.example.io/b.png""><img id=""i4"" srcset=""https://a.example/1x.png 1x, local.png 2x, https://b.example/3x.png 3x"">" &
                         "<img id=""i5"" src=""https://cdn.example.com/again.png""><video poster=""https://poster.example/p.png"" src=""https://v.example/v.mp4"">fb</video>")
        Dim document = Dom(result)
        Assert.Equal("", If(document.GetElementById("i1").GetAttribute("src"), ""))
        Assert.False(document.GetElementById("i2").HasAttribute("src"))
        Assert.False(document.GetElementById("i3").HasAttribute("src"))
        Assert.Equal("local.png 2x", document.GetElementById("i4").GetAttribute("srcset"))
        Assert.False(document.GetElementById("si").HasAttribute("href"))
        Assert.Empty(document.QuerySelectorAll("link"))
        Assert.False(document.Body.HasAttribute("background"))
        Assert.Equal(12, result.BlockedResources)
        Assert.Equal(Sorted({"https://bg.example", "https://cdn.example.com", "https://cdn2.example.com", "http://host.example:8080", "https://fonts.example.org",
                             "https://svg.example.net", "https://svg2.example.net", "https://btn.example.io", "https://a.example", "https://b.example"}),
                     Sorted(result.ExternalOrigins))
        Assert.Equal(result.ExternalOrigins.Count, result.ExternalOrigins.Distinct().Count())
    End Sub

    <Fact>
    Public Sub An_approved_origin_keeps_its_references_while_others_stay_blocked()
        Dim result = Run("<img id=""a"" src=""https://cdn.example.com/a.png""><img id=""b"" src=""https://other.example/b.png""><link rel=""stylesheet"" href=""https://CDN.example.com/s.css"">" &
                         "<img id=""c"" srcset=""https://cdn.example.com/1x.png 1x, https://other.example/2x.png 2x"">",
                         Options(approved:={"https://cdn.example.com"}))
        Dim document = Dom(result)
        Assert.Equal("https://cdn.example.com/a.png", document.GetElementById("a").GetAttribute("src"))
        Assert.False(document.GetElementById("b").HasAttribute("src"))
        Assert.Equal("https://CDN.example.com/s.css", document.QuerySelector("link").GetAttribute("href"))
        Assert.Equal("https://cdn.example.com/1x.png 1x", document.GetElementById("c").GetAttribute("srcset"))
        Assert.Equal(2, result.BlockedResources)
        Assert.Equal(New String() {"https://other.example"}, result.ExternalOrigins.ToArray())
    End Sub

    <Fact>
    Public Sub Same_origin_references_stay_for_the_resolver_to_authorize()
        Dim result = Run("<img id=""a"" src=""pic.png""><img id=""b"" src=""/a/b.png""><img id=""c"" src=""../up.png""><img id=""d"" src=""https://sabc.fms-doc.invalid/x.png""><img id=""e"" src=""//sabc.fms-doc.invalid/y.png"">" &
                         "<img id=""f"" src=""sub\win.png""><img id=""g"" src=""#frag""><img id=""h"" src=""""><link rel=""stylesheet"" href=""https://sabc.fms-doc.invalid/css/s.css"">")
        Dim document = Dom(result)
        For Each id In {"a", "b", "c", "d", "e", "f", "g"}
            Assert.True(document.GetElementById(id).HasAttribute("src"), id)
        Next
        Assert.Equal("../up.png", document.GetElementById("c").GetAttribute("src"))
        Assert.Equal("https://sabc.fms-doc.invalid/css/s.css", document.QuerySelector("link").GetAttribute("href"))
        Assert.Equal(0, result.BlockedResources)
        Assert.Empty(result.ExternalOrigins)
    End Sub

    <Fact>
    Public Sub Online_profile_treats_the_real_page_origin_as_same_origin()
        Dim page = New Uri("https://news.example.org/")
        Dim result = Run("<head><base href=""https://news.example.org/sub/""></head><img id=""a"" src=""https://news.example.org/a.png""><img id=""b"" src=""https://ads.example.net/x.gif"">",
                         Options(entry:="", origin:=page, profile:=HtmlSourceKind.OnlinePage, transport:="utf-8"))
        Dim document = Dom(result)
        Assert.Equal("/sub/", document.QuerySelector("base").GetAttribute("href"))
        Assert.True(document.GetElementById("a").HasAttribute("src"))
        Assert.False(document.GetElementById("b").HasAttribute("src"))
        Assert.Equal(New String() {"https://ads.example.net"}, result.ExternalOrigins.ToArray())
    End Sub

    <Fact>
    Public Sub Only_bounded_raster_data_images_survive()
        Dim big = "data:image/png;base64," & New String("A"c, 4 * 1024 * 1024)
        Dim document = Clean("<img id=""png"" src=""data:image/png;base64,iVBORw0KGgo="">" &
                             "<img id=""jpg"" src=""DATA:IMAGE/JPEG;base64,/9j/4AAQ"">" &
                             "<img id=""html"" src=""data:text/html;base64,PHNjcmlwdD4="">" &
                             "<img id=""svg"" src=""data:image/svg+xml;utf8,&lt;svg onload=alert(1)&gt;"">" &
                             "<img id=""big"" src=""" & big & """>" &
                             "<img id=""set"" srcset=""data:image/png;base64,AAAA 1x, https://e.example/x.png 2x, data:image/svg+xml;base64,AAAA 3x"">" &
                             "<a id=""link"" href=""data:image/png;base64,AAAA"">l</a><svg><use id=""use"" href=""data:image/png;base64,AAAA""/><image id=""img"" href=""data:image/png;base64,AAAA""/></svg>")
        Assert.True(document.GetElementById("png").HasAttribute("src"))
        Assert.True(document.GetElementById("jpg").HasAttribute("src"))
        Assert.False(document.GetElementById("html").HasAttribute("src"))
        Assert.False(document.GetElementById("svg").HasAttribute("src"))
        Assert.False(document.GetElementById("big").HasAttribute("src"))
        Assert.Equal("data:image/png;base64,AAAA 1x", document.GetElementById("set").GetAttribute("srcset"))
        Assert.False(document.GetElementById("link").HasAttribute("href"))
        Assert.False(document.GetElementById("use").HasAttribute("href"))
        Assert.True(document.GetElementById("img").HasAttribute("href"))
    End Sub

    <Fact>
    Public Sub Picture_sources_keep_a_filtered_srcset_but_media_sources_go()
        Dim result = Run("<picture><source srcset=""a.webp 1x, https://e.example/b.webp 2x"" type=""image/webp""><img src=""a.png""></picture>")
        Dim document = Dom(result)
        Assert.Equal("a.webp 1x", document.QuerySelector("picture source").GetAttribute("srcset"))
        Assert.Equal(1, result.BlockedResources)
    End Sub

    <Fact>
    Public Sub Usemap_must_be_a_fragment_and_longdesc_goes()
        Dim document = Clean("<img id=""a"" src=""a.png"" usemap=""#m"" longdesc=""d.html""><img id=""b"" src=""b.png"" usemap=""http://x.example/m"">")
        Assert.Equal("#m", document.GetElementById("a").GetAttribute("usemap"))
        Assert.False(document.GetElementById("a").HasAttribute("longdesc"))
        Assert.False(document.GetElementById("b").HasAttribute("usemap"))
    End Sub

#End Region

#Region "H06 budgets"

    <Fact>
    Public Sub Element_budget_aborts_a_hostile_document_during_the_parse()
        Dim bytes = Encoding.UTF8.GetBytes(String.Concat(Enumerable.Repeat("<p>x</p>", 120000)))
        Dim watch = Stopwatch.StartNew()
        Dim result = HtmlDocumentSanitizer.Sanitize(bytes, Options(), CancellationToken.None)
        watch.Stop()
        Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
        Assert.Empty(result.Bytes)
        Assert.True(watch.ElapsedMilliseconds < 5000, "element budget took " & watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) & " ms")
    End Sub

    <Fact>
    Public Sub A_document_just_inside_the_element_budget_is_accepted()
        ' html, head and body are created by the parser too.
        Dim bytes = Encoding.UTF8.GetBytes(String.Concat(Enumerable.Repeat("<p>x</p>", 20000)))
        Dim result = HtmlDocumentSanitizer.Sanitize(bytes, Options(), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, result.Status)
        Assert.True(result.ElementCount > 20000)
    End Sub

    <Fact>
    Public Sub Token_budget_counts_tokens_that_create_no_element()
        Dim bytes = Encoding.UTF8.GetBytes(String.Concat(Enumerable.Repeat("</b>", 260000)))
        Dim watch = Stopwatch.StartNew()
        Dim result = HtmlDocumentSanitizer.Sanitize(bytes, Options(), CancellationToken.None)
        watch.Stop()
        Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
        Assert.True(watch.ElapsedMilliseconds < 5000, "token budget took " & watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) & " ms")
    End Sub

    <Fact>
    Public Sub Input_over_16_MiB_is_refused_before_any_parsing()
        Dim watch = Stopwatch.StartNew()
        Dim result = HtmlDocumentSanitizer.Sanitize(New Byte(16 * 1024 * 1024) {}, Options(), CancellationToken.None)
        watch.Stop()
        Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
        Assert.True(watch.ElapsedMilliseconds < 1000)
    End Sub

    <Fact>
    Public Sub Absurd_nesting_is_refused_and_ordinary_nesting_is_not()
        Dim deep = Encoding.UTF8.GetBytes(String.Concat(Enumerable.Repeat("<div>", 600)) & "x")
        Assert.Equal(HtmlOutcome.LimitExceeded, HtmlDocumentSanitizer.Sanitize(deep, Options(), CancellationToken.None).Status)
        Dim shallow = Encoding.UTF8.GetBytes(String.Concat(Enumerable.Repeat("<div>", 100)) & "x")
        Assert.Equal(HtmlOutcome.Ok, HtmlDocumentSanitizer.Sanitize(shallow, Options(), CancellationToken.None).Status)
    End Sub

    <Fact>
    Public Sub A_cancelled_token_ends_the_parse_with_cancelled()
        Using source As New CancellationTokenSource()
            source.Cancel()
            Dim result = HtmlDocumentSanitizer.Sanitize(Encoding.UTF8.GetBytes("<p>x</p><p>y</p>"), Options(), source.Token)
            Assert.Equal(HtmlOutcome.Cancelled, result.Status)
            Assert.Empty(result.Bytes)
        End Using
    End Sub

    <Fact>
    Public Sub Null_input_and_empty_input_do_not_throw()
        Assert.Equal(HtmlOutcome.Ok, HtmlDocumentSanitizer.Sanitize(Nothing, Nothing, CancellationToken.None).Status)
        Dim empty = HtmlDocumentSanitizer.Sanitize(New Byte() {}, Options(), CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, empty.Status)
        Assert.Equal(1, Dom(empty).QuerySelectorAll("meta[charset]").Length)
    End Sub

#End Region
End Class
#End If
