Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

' Specification 275 H03: the pure URI / root / kind / navigation / header decisions. Runs on both
' legs (the policy compiles on net48 under DOCUMENT_PREVIEW_TESTS), so nothing here touches the
' filesystem or a Windows-only API.
Public Class HtmlDocumentPolicyTests
    Private Const Host As String = "s0123456789abcdef0123456789abcdef.fms-doc.invalid"
    Private Const OriginText As String = "https://" & Host
    Private Shared ReadOnly Origin As New Uri(OriginText & "/")

    Private Shared Function Parse(path As String, Optional method As String = "GET") As HtmlKeyResolution
        Return HtmlDocumentPolicy.ParseRequestKey(OriginText & path, Origin, method)
    End Function

    Private Shared Sub AssertKey(expected As String, path As String)
        Dim result As HtmlKeyResolution = Parse(path)
        Assert.True(result.IsAllowed, path & " was refused: " & result.Status.ToString())
        Assert.Equal(expected, result.Key)
    End Sub

    Private Shared Sub AssertRefused(expected As HtmlOutcome, path As String)
        Dim result As HtmlKeyResolution = Parse(path)
        Assert.False(result.IsAllowed, path & " was allowed as " & result.Key)
        Assert.Equal(expected, result.Status)
    End Sub

    ' ---- session origin -------------------------------------------------------------------

    <Fact>
    Public Sub Session_origin_is_32_lowercase_hex_under_the_invalid_suffix_and_unique()
        Dim a As Uri = HtmlDocumentPolicy.NewSessionOrigin()
        Dim b As Uri = HtmlDocumentPolicy.NewSessionOrigin()
        Assert.Matches("^https://s[0-9a-f]{32}\.fms-doc\.invalid/$", a.AbsoluteUri)
        Assert.NotEqual(a.AbsoluteUri, b.AbsoluteUri)
        Assert.True(HtmlDocumentPolicy.IsSessionOrigin(a, a))
        Assert.False(HtmlDocumentPolicy.IsSessionOrigin(a, b))
    End Sub

    <Theory>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html", True)>
    <InlineData("HTTPS://S0123456789ABCDEF0123456789ABCDEF.FMS-DOC.INVALID/a.html", True)>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid:443/a.html", True)>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid:8443/a.html", False)>
    <InlineData("http://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html", False)>
    <InlineData("https://user@s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html", False)>
    <InlineData("https://user:pw@s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html", False)>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid.evil.example/a.html", False)>
    <InlineData("https://sffffffffffffffffffffffffffffffff.fms-doc.invalid/a.html", False)>
    <InlineData("https://example.com/a.html", False)>
    <InlineData("file:///C:/a.html", False)>
    Public Sub IsSessionOrigin_requires_scheme_host_port_and_no_userinfo(candidate As String, expected As Boolean)
        Assert.Equal(expected, HtmlDocumentPolicy.IsSessionOrigin(New Uri(candidate), Origin))
    End Sub

    <Fact>
    Public Sub IsSessionOrigin_null_arguments_are_false()
        Assert.False(HtmlDocumentPolicy.IsSessionOrigin(Nothing, Origin))
        Assert.False(HtmlDocumentPolicy.IsSessionOrigin(Origin, Nothing))
    End Sub

    ' ---- one-pass decoding and round trip -------------------------------------------------

    <Fact>
    Public Sub Special_and_unicode_names_round_trip_through_EntryUrl_and_ParseRequestKey()
        Dim cyrillic As String = ChrW(&H43A) & ChrW(&H43D) & ChrW(&H438) & ChrW(&H433) & ChrW(&H430)
        Dim emoji As String = ChrW(&HD83D) & ChrW(&HDE00)
        Dim names As String() = {
            "a#b%c&d e.html",
            "chapter # % & " & cyrillic & ".html",
            "100%25.html",
            "literal%2e%2e.html",
            "%2e%2e",
            "plus+sign=eq;semi,comma.css",
            "dir one/sub dir/" & cyrillic & "#1.css",
            ChrW(&H65E5) & ChrW(&H672C) & ChrW(&H8A9E) & "/" & emoji & ".html",
            "caf" & ChrW(&HE9) & ".xhtml",
            "(parens)'quote'!.png"}
        For Each name As String In names
            Dim url As String = HtmlDocumentPolicy.EntryUrl(Origin, name)
            Assert.StartsWith(OriginText & "/", url)
            Assert.DoesNotContain("#", url.Substring(OriginText.Length))
            Dim result As HtmlKeyResolution = HtmlDocumentPolicy.ParseRequestKey(url, Origin, "GET")
            Assert.True(result.IsAllowed, name & " -> " & result.Status.ToString())
            Assert.Equal(name, result.Key)
        Next
    End Sub

    <Fact>
    Public Sub Double_encoding_decodes_once_and_stays_literal()
        AssertKey("literal%2e%2e.html", "/literal%252e%252e.html")
        AssertKey("%2e%2e", "/%252e%252e")
        AssertKey("a%2Fb.html", "/a%252Fb.html")
        AssertKey("a%5Cb.html", "/a%255Cb.html")
        AssertKey("100%.html", "/100%25.html")
    End Sub

    <Theory>
    <InlineData("/a%2Fb.html")>
    <InlineData("/a%2fb.html")>
    <InlineData("/a%5Cb.html")>
    <InlineData("/a%5cb.html")>
    <InlineData("/dir%2F..%2Foutside.css")>
    <InlineData("/a\b.html")>
    <InlineData("/..\outside.css")>
    Public Sub Encoded_or_literal_slash_and_backslash_inside_a_segment_are_refused(path As String)
        AssertRefused(HtmlOutcome.RootEscape, path)
    End Sub

    <Theory>
    <InlineData("/..")>
    <InlineData("/../outside.css")>
    <InlineData("/a/../b.css")>
    <InlineData("/a/%2e%2e/b.css")>
    <InlineData("/a/%2E%2e/b.css")>
    <InlineData("/%2e%2e/b.css")>
    <InlineData("/book/%2e%2e/outside.txt")>
    <InlineData("/.")>
    <InlineData("/./a.css")>
    <InlineData("/a/%2e/b.css")>
    Public Sub Dot_segments_are_refused_even_when_a_URI_class_would_normalize_them_away(path As String)
        AssertRefused(HtmlOutcome.RootEscape, path)
    End Sub

    <Theory>
    <InlineData("//server/share/a.css")>
    <InlineData("/a//b.css")>
    <InlineData("/C:/Windows/win.ini")>
    <InlineData("/C%3A/Windows/win.ini")>
    <InlineData("/c:")>
    <InlineData("/a.html:stream")>
    <InlineData("/a.html%3A%3A%24DATA")>
    <InlineData("/dir/a.css:hidden")>
    Public Sub Rooted_unc_drive_and_stream_syntax_is_refused(path As String)
        AssertRefused(HtmlOutcome.RootEscape, path)
    End Sub

    <Theory>
    <InlineData("/con")>
    <InlineData("/CON.html")>
    <InlineData("/nul.txt")>
    <InlineData("/Aux.css")>
    <InlineData("/PRN.png")>
    <InlineData("/com1.css")>
    <InlineData("/COM0.css")>
    <InlineData("/LPT9.html")>
    <InlineData("/lpt1")>
    <InlineData("/dir/CON/x.css")>
    <InlineData("/con.tar.gz")>
    <InlineData("/CONIN%24")>
    <InlineData("/COM%C2%B9.css")>
    <InlineData("/LPT%C2%B2")>
    <InlineData("/com%C2%B3.png")>
    Public Sub Windows_device_names_are_refused_with_or_without_extension(path As String)
        AssertRefused(HtmlOutcome.RootEscape, path)
    End Sub

    <Theory>
    <InlineData("console.html", False)>
    <InlineData("com10.css", False)>
    <InlineData("comma", False)>
    <InlineData("lpt", False)>
    <InlineData("nul1", False)>
    <InlineData("aux-1", False)>
    <InlineData("NUL", True)>
    <InlineData("nul.", True)>
    <InlineData("nul .txt", True)>
    <InlineData("COM5", True)>
    <InlineData("Lpt3.log", True)>
    Public Sub IsReservedDeviceName_matches_only_the_reserved_names(segment As String, expected As Boolean)
        Assert.Equal(expected, HtmlDocumentPolicy.IsReservedDeviceName(segment))
    End Sub

    <Theory>
    <InlineData("/a.html.")>
    <InlineData("/a.html%20")>
    <InlineData("/a%20")>
    <InlineData("/dir./x.css")>
    <InlineData("/dir%20/x.css")>
    Public Sub Trailing_dot_or_space_in_a_segment_is_refused(path As String)
        AssertRefused(HtmlOutcome.RootEscape, path)
    End Sub

    <Theory>
    <InlineData("/a%00b.css", HtmlOutcome.RootEscape)>
    <InlineData("/a%01b.css", HtmlOutcome.RootEscape)>
    <InlineData("/a%0Ab.css", HtmlOutcome.RootEscape)>
    <InlineData("/a%7Fb.css", HtmlOutcome.RootEscape)>
    <InlineData("/a%3Cb.css", HtmlOutcome.UnsupportedResource)>
    <InlineData("/a%3Eb.css", HtmlOutcome.UnsupportedResource)>
    <InlineData("/a%22b.css", HtmlOutcome.UnsupportedResource)>
    <InlineData("/a%7Cb.css", HtmlOutcome.UnsupportedResource)>
    <InlineData("/a%3Fb.css", HtmlOutcome.UnsupportedResource)>
    <InlineData("/a%2Ab.css", HtmlOutcome.UnsupportedResource)>
    Public Sub Control_and_invalid_filename_characters_are_refused(path As String, expected As HtmlOutcome)
        AssertRefused(expected, path)
    End Sub

    <Theory>
    <InlineData("/a%zz.css")>
    <InlineData("/a%2")>
    <InlineData("/a%")>
    <InlineData("/%")>
    <InlineData("/a%G0b")>
    <InlineData("/%FF.html")>
    <InlineData("/%C3.html")>
    <InlineData("/%C0%AF.html")>
    <InlineData("/%ED%A0%80.html")>
    Public Sub Malformed_escapes_and_invalid_utf8_are_refused(path As String)
        AssertRefused(HtmlOutcome.RootEscape, path)
    End Sub

    <Fact>
    Public Sub Segment_over_255_characters_is_unsupported()
        AssertRefused(HtmlOutcome.UnsupportedResource, "/" & New String("a"c, 256) & ".css")
        AssertKey(New String("a"c, 251) & ".css", "/" & New String("a"c, 251) & ".css")
    End Sub

    ' ---- origin, credentials, method ------------------------------------------------------

    <Theory>
    <InlineData("https://other.fms-doc.invalid/a.html")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid:8443/a.html")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid:80/a.html")>
    <InlineData("http://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html")>
    <InlineData("https://user@s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html")>
    <InlineData("https://user:pw@s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid./a.html")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid@evil.example/a.html")>
    <InlineData("https://example.com/a.html")>
    <InlineData("ftp://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html")>
    <InlineData("file:///C:/a.html")>
    <InlineData("//s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html")>
    <InlineData("/a.html")>
    <InlineData("a.html")>
    <InlineData("")>
    <InlineData(" https://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html ")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a" & vbLf & "b.html")>
    <InlineData("https://")>
    Public Sub Wrong_origin_port_scheme_credentials_or_malformed_text_is_refused(requestUri As String)
        Dim result As HtmlKeyResolution = HtmlDocumentPolicy.ParseRequestKey(requestUri, Origin, "GET")
        Assert.False(result.IsAllowed)
        Assert.Equal(HtmlOutcome.RootEscape, result.Status)
    End Sub

    <Fact>
    Public Sub Null_request_or_origin_is_refused()
        Assert.False(HtmlDocumentPolicy.ParseRequestKey(Nothing, Origin, "GET").IsAllowed)
        Assert.False(HtmlDocumentPolicy.ParseRequestKey(OriginText & "/a.html", Nothing, "GET").IsAllowed)
    End Sub

    <Theory>
    <InlineData("GET", True)>
    <InlineData("HEAD", True)>
    <InlineData("POST", False)>
    <InlineData("PUT", False)>
    <InlineData("DELETE", False)>
    <InlineData("OPTIONS", False)>
    <InlineData("PATCH", False)>
    <InlineData("get", False)>
    <InlineData("", False)>
    Public Sub Only_GET_and_HEAD_are_served(method As String, allowed As Boolean)
        Dim result As HtmlKeyResolution = Parse("/a.css", method)
        Assert.Equal(allowed, result.IsAllowed)
        If Not allowed Then Assert.Equal(HtmlOutcome.UnsupportedResource, result.Status)
    End Sub

    <Fact>
    Public Sub Method_Nothing_is_refused()
        Assert.False(Parse("/a.css", Nothing).IsAllowed)
    End Sub

    ' ---- query, fragment, directories ------------------------------------------------------

    <Fact>
    Public Sub Query_and_fragment_are_ignored()
        AssertKey("a/b.css", "/a/b.css?v=1")
        AssertKey("a/b.css", "/a/b.css#frag")
        AssertKey("a/b.css", "/a/b.css?v=1#frag")
        AssertKey("a.html", "/a.html?")
        AssertKey("a.html", "/a.html?x=%2e%2e/..%2f")
        AssertKey("a.html", "/a.html#/../..")
        AssertKey("a.html", ":443/a.html")
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("/")>
    <InlineData("/?x=1")>
    <InlineData("/#top")>
    <InlineData("/dir/")>
    <InlineData("/dir/sub/")>
    Public Sub Directory_and_empty_paths_have_no_index(path As String)
        AssertRefused(HtmlOutcome.UnsupportedResource, path)
    End Sub

    <Fact>
    Public Sub Root_relative_paths_resolve_against_the_declared_root()
        AssertKey("index.html", "/index.html")
        AssertKey("css/site.css", "/css/site.css")
        AssertKey("A/B/Mixed.Case.PNG", "/A/B/Mixed.Case.PNG")
        Dim upperHost As HtmlKeyResolution = HtmlDocumentPolicy.ParseRequestKey("https://" & Host.ToUpperInvariant() & "/Up.css", Origin, "GET")
        Assert.True(upperHost.IsAllowed)
        Assert.Equal("Up.css", upperHost.Key)
    End Sub

    ' ---- ValidateKey ----------------------------------------------------------------------

    <Theory>
    <InlineData("index.html", True)>
    <InlineData("a/b/c.css", True)>
    <InlineData("a b/c#d%e.css", True)>
    <InlineData("%2e%2e", True)>
    <InlineData("", False)>
    <InlineData("/a.css", False)>
    <InlineData("a/", False)>
    <InlineData("a//b.css", False)>
    <InlineData("../x.css", False)>
    <InlineData("a/../b.css", False)>
    <InlineData("a/./b.css", False)>
    <InlineData("a\b.css", False)>
    <InlineData("C:\x.css", False)>
    <InlineData("a:b.css", False)>
    <InlineData("con.css", False)>
    <InlineData("dir/NUL", False)>
    <InlineData("a.css.", False)>
    <InlineData("a.css ", False)>
    Public Sub ValidateKey_applies_the_parser_segment_rules_to_a_key_that_did_not_come_from_the_parser(key As String, expected As Boolean)
        Dim result As HtmlKeyResolution = HtmlDocumentPolicy.ValidateKey(key)
        Assert.Equal(expected, result.IsAllowed)
        If expected Then Assert.Equal(key, result.Key)
    End Sub

    <Fact>
    Public Sub ValidateKey_null_is_refused()
        Assert.False(HtmlDocumentPolicy.ValidateKey(Nothing).IsAllowed)
    End Sub

    ' ---- kinds, MIME, contexts, limits ----------------------------------------------------

    <Theory>
    <InlineData("a.html", HtmlResourceKind.Document)>
    <InlineData("a.HTM", HtmlResourceKind.Document)>
    <InlineData("dir/a.xhtml", HtmlResourceKind.Document)>
    <InlineData("a.css", HtmlResourceKind.Stylesheet)>
    <InlineData("a.CSS", HtmlResourceKind.Stylesheet)>
    <InlineData("a.png", HtmlResourceKind.Image)>
    <InlineData("a.jpg", HtmlResourceKind.Image)>
    <InlineData("a.jpeg", HtmlResourceKind.Image)>
    <InlineData("a.gif", HtmlResourceKind.Image)>
    <InlineData("a.webp", HtmlResourceKind.Image)>
    <InlineData("a.bmp", HtmlResourceKind.Image)>
    <InlineData("a.avif", HtmlResourceKind.Image)>
    <InlineData("a.svg", HtmlResourceKind.Image)>
    <InlineData("a.ico", HtmlResourceKind.Image)>
    <InlineData("a.woff", HtmlResourceKind.Font)>
    <InlineData("a.woff2", HtmlResourceKind.Font)>
    <InlineData("a.ttf", HtmlResourceKind.Font)>
    <InlineData("a.otf", HtmlResourceKind.Font)>
    <InlineData("a.js", HtmlResourceKind.Other)>
    <InlineData("a.pdf", HtmlResourceKind.Other)>
    <InlineData("a.mp4", HtmlResourceKind.Other)>
    <InlineData("a.exe", HtmlResourceKind.Other)>
    <InlineData("noextension", HtmlResourceKind.Other)>
    <InlineData("trailing.", HtmlResourceKind.Other)>
    <InlineData("dir.css/readme", HtmlResourceKind.Other)>
    <InlineData("", HtmlResourceKind.Other)>
    Public Sub KindOfExtension_follows_the_extension_of_the_last_segment(key As String, expected As HtmlResourceKind)
        Assert.Equal(expected, HtmlDocumentPolicy.KindOfExtension(key))
    End Sub

    <Theory>
    <InlineData("a.html", "text/html; charset=utf-8")>
    <InlineData("a.htm", "text/html; charset=utf-8")>
    <InlineData("a.xhtml", "application/xhtml+xml")>
    <InlineData("a.css", "text/css")>
    <InlineData("a.png", "image/png")>
    <InlineData("a.jpg", "image/jpeg")>
    <InlineData("a.JPEG", "image/jpeg")>
    <InlineData("a.gif", "image/gif")>
    <InlineData("a.webp", "image/webp")>
    <InlineData("a.bmp", "image/bmp")>
    <InlineData("a.avif", "image/avif")>
    <InlineData("a.svg", "image/svg+xml")>
    <InlineData("a.woff", "font/woff")>
    <InlineData("a.woff2", "font/woff2")>
    <InlineData("a.ttf", "font/ttf")>
    <InlineData("a.otf", "font/otf")>
    <InlineData("a.bin", "application/octet-stream")>
    <InlineData("a", "application/octet-stream")>
    Public Sub MimeTypeOf_is_decided_by_the_extension(key As String, expected As String)
        Assert.Equal(expected, HtmlDocumentPolicy.MimeTypeOf(key))
    End Sub

    <Fact>
    Public Sub Only_the_matching_kind_and_context_pair_is_allowed()
        Dim kinds As HtmlResourceKind() = {HtmlResourceKind.Document, HtmlResourceKind.Stylesheet, HtmlResourceKind.Image, HtmlResourceKind.Font}
        Dim contexts As HtmlRequestContext() = {HtmlRequestContext.Document, HtmlRequestContext.Stylesheet, HtmlRequestContext.Image, HtmlRequestContext.Font}
        For i As Integer = 0 To kinds.Length - 1
            For j As Integer = 0 To contexts.Length - 1
                Assert.Equal(i = j, HtmlDocumentPolicy.IsAllowedInContext(kinds(i), contexts(j)))
            Next
            Assert.False(HtmlDocumentPolicy.IsAllowedInContext(kinds(i), HtmlRequestContext.Other))
        Next
        For Each context As HtmlRequestContext In contexts
            Assert.False(HtmlDocumentPolicy.IsAllowedInContext(HtmlResourceKind.Other, context))
        Next
        Assert.False(HtmlDocumentPolicy.IsAllowedInContext(HtmlResourceKind.Other, HtmlRequestContext.Other))
        ' A stylesheet requested as a document can never become active HTML.
        Assert.False(HtmlDocumentPolicy.IsAllowedInContext(HtmlDocumentPolicy.KindOfExtension("evil.css"), HtmlRequestContext.Document))
    End Sub

    <Fact>
    Public Sub Limits_follow_section_14()
        Assert.Equal(16L * 1024L * 1024L, HtmlDocumentPolicy.MaxEntryBytes)
        Assert.Equal(2L * 1024L * 1024L, HtmlDocumentPolicy.MaxStylesheetBytes)
        Assert.Equal(8L * 1024L * 1024L, HtmlDocumentPolicy.MaxFontBytes)
        Assert.Equal(32L * 1024L * 1024L, HtmlDocumentPolicy.MaxImageBytes)
        Assert.Equal(4L * 1024L * 1024L, HtmlDocumentPolicy.MaxDataImageBytes)
        Assert.Equal(100000, HtmlDocumentPolicy.MaxCreatedElements)
        Assert.Equal(250000, HtmlDocumentPolicy.MaxTokens)
        Assert.Equal(2048, HtmlDocumentPolicy.MaxUniqueResources)
        Assert.Equal(4, HtmlDocumentPolicy.MaxOutstandingReads)
        Assert.Equal(10000, HtmlDocumentPolicy.LocalResourceDeadlineMs)
        Assert.Equal(15000, HtmlDocumentPolicy.FirstNavigationDeadlineMs)
        Assert.Equal(4096, HtmlDocumentPolicy.MaxFindTermLength)
        Assert.Equal(100, HtmlDocumentPolicy.MaxNavigationEntries)
        Assert.Equal(".fms-doc.invalid", HtmlDocumentPolicy.OriginHostSuffix)
        Assert.Equal(HtmlDocumentPolicy.MaxEntryBytes, HtmlDocumentPolicy.MaxBytesFor(HtmlResourceKind.Document))
        Assert.Equal(HtmlDocumentPolicy.MaxStylesheetBytes, HtmlDocumentPolicy.MaxBytesFor(HtmlResourceKind.Stylesheet))
        Assert.Equal(HtmlDocumentPolicy.MaxImageBytes, HtmlDocumentPolicy.MaxBytesFor(HtmlResourceKind.Image))
        Assert.Equal(HtmlDocumentPolicy.MaxFontBytes, HtmlDocumentPolicy.MaxBytesFor(HtmlResourceKind.Font))
        Assert.Equal(0L, HtmlDocumentPolicy.MaxBytesFor(HtmlResourceKind.Other))
    End Sub

    ' ---- data images ----------------------------------------------------------------------

    <Theory>
    <InlineData("data:image/png;base64,iVBORw0KGgo=")>
    <InlineData("data:image/jpeg;base64,/9j/4AAQSkZJRg==")>
    <InlineData("data:image/gif;base64,R0lGODlhAQABAAAAACw=")>
    <InlineData("data:image/webp;base64,UklGRhoAAABXRUJQ")>
    <InlineData("data:image/bmp;base64,Qk0=")>
    <InlineData("data:image/avif;base64,AAAAHGZ0eXBhdmlm")>
    <InlineData("data:image/png;charset=utf-8;base64,iVBORw0KGgo=")>
    <InlineData("DATA:image/PNG;BASE64,iVBORw0KGgo=")>
    <InlineData("data:image/png;base64,iVBO Rw0K" & vbLf & "Ggo=")>
    Public Sub Raster_base64_data_images_are_allowed(value As String)
        Assert.True(HtmlDocumentPolicy.IsAllowedDataImage(value))
    End Sub

    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")>
    <InlineData("data:image/svg+xml,<svg onload=alert(1)>")>
    <InlineData("data:text/html;base64,PHNjcmlwdD4=")>
    <InlineData("data:application/javascript;base64,YWxlcnQoMSk=")>
    <InlineData("data:image/png,iVBORw0KGgo=")>
    <InlineData("data:image/png;utf8,iVBORw0KGgo=")>
    <InlineData("data:image/png;base64,")>
    <InlineData("data:image/png;base64")>
    <InlineData("data:image/jpg;base64,AAAA")>
    <InlineData("data:image/x-icon;base64,AAAA")>
    <InlineData("data:image/png;base64,AA$A")>
    <InlineData("data:image/png;base64,AA=A")>
    <InlineData("data:image/png;base64,A===")>
    <InlineData("data:image/png;base64,AAAA%41")>
    <InlineData("data:image/png;;base64,AAAA")>
    <InlineData("data:image/png;a b;base64,AAAA")>
    <InlineData("data:image/;base64,AAAA")>
    <InlineData("https://example.com/a.png")>
    <InlineData("javascript:alert(1)")>
    Public Sub Everything_but_a_bounded_raster_base64_image_is_refused(value As String)
        Assert.False(HtmlDocumentPolicy.IsAllowedDataImage(value))
    End Sub

    <Fact>
    Public Sub Data_image_payload_is_bounded_at_four_mebibytes_encoded()
        Dim limit As Integer = CInt(HtmlDocumentPolicy.MaxDataImageBytes)
        Assert.True(HtmlDocumentPolicy.IsAllowedDataImage("data:image/png;base64," & New String("A"c, limit)))
        Assert.False(HtmlDocumentPolicy.IsAllowedDataImage("data:image/png;base64," & New String("A"c, limit + 1)))
    End Sub

    ' ---- navigation -----------------------------------------------------------------------

    Private Shared Function Nav(target As String, Optional currentKey As String = "index.html") As HtmlNavigationDecision
        Return HtmlDocumentPolicy.ClassifyNavigation(target, Origin, currentKey)
    End Function

    <Fact>
    Public Sub Same_origin_html_is_internal_navigation_with_its_fragment()
        Dim plain As HtmlNavigationDecision = Nav(OriginText & "/chapter2.html")
        Assert.Equal(HtmlNavigationAction.Internal, plain.Action)
        Assert.Equal("chapter2.html", plain.Key)
        Assert.Equal("", plain.Fragment)
        Assert.Equal("", plain.ExternalUrl)

        Dim nested As HtmlNavigationDecision = Nav(OriginText & "/sub/page.xhtml#sec-1")
        Assert.Equal(HtmlNavigationAction.Internal, nested.Action)
        Assert.Equal("sub/page.xhtml", nested.Key)
        Assert.Equal("sec-1", nested.Fragment)

        Dim escaped As HtmlNavigationDecision = Nav(HtmlDocumentPolicy.EntryUrl(Origin, "a#b%c&d e.html"))
        Assert.Equal(HtmlNavigationAction.Internal, escaped.Action)
        Assert.Equal("a#b%c&d e.html", escaped.Key)
    End Sub

    <Fact>
    Public Sub Same_key_with_only_a_fragment_is_an_anchor()
        Dim anchor As HtmlNavigationDecision = Nav(OriginText & "/index.html#top")
        Assert.Equal(HtmlNavigationAction.Anchor, anchor.Action)
        Assert.Equal("index.html", anchor.Key)
        Assert.Equal("top", anchor.Fragment)

        Assert.Equal(HtmlNavigationAction.Anchor, Nav(OriginText & "/INDEX.HTML#top").Action)
        Assert.Equal(HtmlNavigationAction.Anchor, Nav(OriginText & "/index.html#").Action)

        Dim bare As HtmlNavigationDecision = Nav("#chapter")
        Assert.Equal(HtmlNavigationAction.Anchor, bare.Action)
        Assert.Equal("index.html", bare.Key)
        Assert.Equal("chapter", bare.Fragment)

        ' The same key without a fragment, or with a query, is a real navigation again.
        Assert.Equal(HtmlNavigationAction.Internal, Nav(OriginText & "/index.html").Action)
        Assert.Equal(HtmlNavigationAction.Internal, Nav(OriginText & "/index.html?x=1#top").Action)
        Assert.Equal(HtmlNavigationAction.Internal, Nav(OriginText & "/other.html#top").Action)
    End Sub

    <Theory>
    <InlineData("/img/a.png")>
    <InlineData("/doc/file.pdf")>
    <InlineData("/style.css")>
    <InlineData("/a/b.mp4")>
    <InlineData("/font.woff2")>
    Public Sub Same_origin_non_html_goes_to_the_typed_opener(path As String)
        Dim decision As HtmlNavigationDecision = Nav(OriginText & path)
        Assert.Equal(HtmlNavigationAction.OpenTyped, decision.Action)
        Assert.Equal(path.Substring(1), decision.Key)
    End Sub

    <Theory>
    <InlineData("https://example.com/page?x=1&y=2", "https://example.com/page?x=1&y=2")>
    <InlineData("http://example.com/", "http://example.com/")>
    <InlineData("http://Example.COM", "http://example.com/")>
    <InlineData("https://example.com/a b", "https://example.com/a%20b")>
    <InlineData("https://example.com:8443/x#frag", "https://example.com:8443/x#frag")>
    Public Sub Other_http_origins_are_offered_to_the_system_browser(target As String, expected As String)
        Dim decision As HtmlNavigationDecision = Nav(target)
        Assert.Equal(HtmlNavigationAction.External, decision.Action)
        Assert.Equal(expected, decision.ExternalUrl)
        Assert.Equal("", decision.Key)
    End Sub

    <Fact>
    Public Sub Mailto_is_a_mail_action()
        Dim decision As HtmlNavigationDecision = Nav("mailto:someone@example.com?subject=Hello")
        Assert.Equal(HtmlNavigationAction.Mail, decision.Action)
        Assert.StartsWith("mailto:someone@example.com", decision.ExternalUrl)
    End Sub

    <Theory>
    <InlineData("javascript:alert(1)")>
    <InlineData("JavaScript:alert(1)")>
    <InlineData("vbscript:msgbox(1)")>
    <InlineData("data:text/html,<script>alert(1)</script>")>
    <InlineData("data:image/png;base64,AAAA")>
    <InlineData("file:///C:/Windows/notepad.exe")>
    <InlineData("file://server/share/a.html")>
    <InlineData("blob:https://example.com/0000-1111")>
    <InlineData("about:blank")>
    <InlineData("about:srcdoc")>
    <InlineData("ftp://example.com/a.txt")>
    <InlineData("ms-settings:privacy")>
    <InlineData("custom-proto://run")>
    <InlineData("java" & vbTab & "script:alert(1)")>
    <InlineData(" javascript:alert(1)")>
    <InlineData("C:\Windows\System32\cmd.exe")>
    <InlineData("\\server\share\a.html")>
    <InlineData("//example.com/a.html")>
    <InlineData("page.html")>
    <InlineData("../page.html")>
    <InlineData("")>
    <InlineData(Nothing)>
    Public Sub Script_data_file_and_unknown_schemes_are_blocked(target As String)
        Assert.Equal(HtmlNavigationAction.Blocked, Nav(target).Action)
    End Sub

    <Theory>
    <InlineData("https://example.com/setup.exe")>
    <InlineData("http://example.com/tool.MSI")>
    <InlineData("https://example.com/x.ps1")>
    <InlineData("https://example.com/run.bat")>
    <InlineData("https://example.com/run.cmd")>
    <InlineData("https://example.com/a.scr")>
    <InlineData("https://example.com/a.vbs")>
    <InlineData("https://example.com/a.js")>
    <InlineData("https://example.com/a.lnk")>
    <InlineData("https://example.com/a.url")>
    <InlineData("https://example.com/a.com")>
    <InlineData("https://example.com/setup.exe?download=1")>
    <InlineData("https://example.com/setup.exe.")>
    <InlineData("https://example.com/dir/setup%2Eexe")>
    Public Sub Executable_looking_targets_are_blocked_on_any_origin(target As String)
        Assert.Equal(HtmlNavigationAction.Blocked, Nav(target).Action)
    End Sub

    <Theory>
    <InlineData("/run.exe")>
    <InlineData("/sub/setup.msi")>
    <InlineData("/a/../b.html")>
    <InlineData("/%2e%2e/b.html")>
    <InlineData("/a%2Fb.html")>
    <InlineData("/con.html")>
    <InlineData("/a.html:stream")>
    <InlineData("/")>
    <InlineData("/dir/")>
    <InlineData("")>
    Public Sub Same_origin_keys_the_resolver_would_refuse_are_blocked(path As String)
        Assert.Equal(HtmlNavigationAction.Blocked, Nav(OriginText & path).Action)
    End Sub

    <Theory>
    <InlineData("https://user:pw@example.com/")>
    <InlineData("https://user@example.com/")>
    <InlineData("https://other.fms-doc.invalid/a.html")>
    <InlineData("http://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid:8443/a.html")>
    Public Sub Credentials_and_foreign_session_origins_are_never_offered_to_the_browser(target As String)
        Assert.Equal(HtmlNavigationAction.Blocked, Nav(target).Action)
    End Sub

    ' ---- external URL ---------------------------------------------------------------------

    <Theory>
    <InlineData("https://example.com/a b", "https://example.com/a%20b")>
    <InlineData("http://Example.COM", "http://example.com/")>
    <InlineData("  https://example.com/x  ", "https://example.com/x")>
    <InlineData("https://example.com/x?q=1#f", "https://example.com/x?q=1#f")>
    <InlineData("ftp://example.com/x", "")>
    <InlineData("javascript:alert(1)", "")>
    <InlineData("mailto:a@b.example", "")>
    <InlineData("file:///C:/x", "")>
    <InlineData("data:text/html,x", "")>
    <InlineData("https://user:pw@example.com/", "")>
    <InlineData("https://user@example.com/", "")>
    <InlineData("example.com/x", "")>
    <InlineData("/relative/path", "")>
    <InlineData("https://", "")>
    <InlineData("https://s0123456789abcdef0123456789abcdef.fms-doc.invalid/a.html", "")>
    <InlineData("https://example.com/a" & vbCr & vbLf & "b", "")>
    <InlineData("", "")>
    <InlineData(Nothing, "")>
    Public Sub NormalizeExternalUrl_accepts_only_absolute_http_and_https_without_userinfo(value As String, expected As String)
        Assert.Equal(expected, HtmlDocumentPolicy.NormalizeExternalUrl(value))
    End Sub

    ' ---- response policy ------------------------------------------------------------------

    <Fact>
    Public Sub Content_security_policy_is_the_section_10_4_text()
        Assert.Equal("default-src 'none'; script-src 'none'; connect-src 'none'; style-src 'self' 'unsafe-inline'; " &
                     "img-src 'self' data:; font-src 'self'; frame-src 'none'; child-src 'none'; worker-src 'none'; " &
                     "object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'; media-src 'none'",
                     HtmlDocumentPolicy.LocalContentSecurityPolicy())
        Assert.DoesNotContain(vbCr, HtmlDocumentPolicy.LocalContentSecurityPolicy())
        Assert.DoesNotContain(vbLf, HtmlDocumentPolicy.LocalContentSecurityPolicy())
    End Sub

    Private Shared Function HeaderLines(block As String) As String()
        Return block.Split(New String() {vbCrLf}, StringSplitOptions.None)
    End Function

    <Fact>
    Public Sub Document_response_carries_the_full_header_set_with_the_csp()
        Dim lines As String() = HeaderLines(HtmlDocumentPolicy.ResponseHeaders("text/html; charset=utf-8", True))
        Assert.Equal(7, lines.Length)
        Assert.Equal("Content-Type: text/html; charset=utf-8", lines(0))
        Assert.Contains("X-Content-Type-Options: nosniff", lines)
        Assert.Contains("Cache-Control: no-store", lines)
        Assert.Contains("Referrer-Policy: no-referrer", lines)
        Assert.Contains("X-DNS-Prefetch-Control: off", lines)
        Assert.Contains("Content-Security-Policy: " & HtmlDocumentPolicy.LocalContentSecurityPolicy(), lines)
        Dim permissions As String = Array.Find(lines, Function(l) l.StartsWith("Permissions-Policy: ", StringComparison.Ordinal))
        Assert.NotNull(permissions)
        For Each feature As String In {"camera", "microphone", "geolocation", "usb", "serial", "hid", "bluetooth", "payment",
                                       "clipboard-read", "clipboard-write", "display-capture", "fullscreen", "autoplay"}
            Assert.Contains(feature & "=()", permissions)
        Next
        Assert.True(permissions.Contains("interest-cohort=()") OrElse permissions.Contains("browsing-topics=()"))
    End Sub

    <Fact>
    Public Sub Resource_response_has_no_csp_and_a_custom_csp_replaces_the_default_for_documents_only()
        Dim resource As String() = HeaderLines(HtmlDocumentPolicy.ResponseHeaders("text/css", False))
        Assert.Equal(6, resource.Length)
        Assert.DoesNotContain(resource, Function(l) l.StartsWith("Content-Security-Policy", StringComparison.Ordinal))
        Assert.Equal("Content-Type: text/css", resource(0))

        Dim ignored As String() = HeaderLines(HtmlDocumentPolicy.ResponseHeaders("text/css", False, "default-src 'self'"))
        Assert.Equal(6, ignored.Length)

        Dim custom As String() = HeaderLines(HtmlDocumentPolicy.ResponseHeaders("text/html", True, "default-src 'none'; img-src 'self'"))
        Assert.Contains("Content-Security-Policy: default-src 'none'; img-src 'self'", custom)
        Assert.Equal(7, custom.Length)
    End Sub

    <Theory>
    <InlineData("text/html" & vbCrLf & "Set-Cookie: a=1")>
    <InlineData("text/html" & vbLf & "X-Evil: 1")>
    <InlineData("text/html" & vbCr & "X-Evil: 1")>
    <InlineData("text/html" & vbNullChar)>
    <InlineData("")>
    <InlineData(Nothing)>
    Public Sub Header_values_with_line_breaks_cannot_inject_a_header(mimeType As String)
        Dim block As String = HtmlDocumentPolicy.ResponseHeaders(mimeType, True)
        Dim lines As String() = HeaderLines(block)
        Assert.Equal(7, lines.Length)
        Assert.Equal("Content-Type: application/octet-stream", lines(0))
        Assert.DoesNotContain("Set-Cookie", block)
        Assert.DoesNotContain("X-Evil", block)
    End Sub

    <Fact>
    Public Sub A_csp_with_a_line_break_falls_back_to_the_local_default()
        Dim block As String = HtmlDocumentPolicy.ResponseHeaders("text/html", True, "default-src 'none'" & vbCrLf & "X-Evil: 1")
        Assert.DoesNotContain("X-Evil", block)
        Assert.Contains("Content-Security-Policy: " & HtmlDocumentPolicy.LocalContentSecurityPolicy(), HeaderLines(block))
        Assert.Equal(7, HeaderLines(block).Length)
    End Sub

    ' ---- find term ------------------------------------------------------------------------

    <Fact>
    Public Sub Find_term_is_bounded_without_splitting_a_surrogate_pair()
        Assert.Equal("", HtmlDocumentPolicy.NormalizeFindTerm(Nothing))
        Assert.Equal("", HtmlDocumentPolicy.NormalizeFindTerm(""))
        Assert.Equal("word", HtmlDocumentPolicy.NormalizeFindTerm("word"))
        Dim exact As String = New String("a"c, 4096)
        Assert.Equal(exact, HtmlDocumentPolicy.NormalizeFindTerm(exact))
        Assert.Equal(4096, HtmlDocumentPolicy.NormalizeFindTerm(New String("a"c, 5000)).Length)

        Dim pair As String = ChrW(&HD83D) & ChrW(&HDE00)
        ' The pair would straddle the limit (index 4095 is its first half): it is dropped whole.
        Dim straddling As String = HtmlDocumentPolicy.NormalizeFindTerm(New String("a"c, 4095) & pair & "tail")
        Assert.Equal(4095, straddling.Length)
        Assert.False(Char.IsHighSurrogate(straddling(straddling.Length - 1)))
        ' A pair that ends exactly at the limit is kept.
        Dim fitting As String = HtmlDocumentPolicy.NormalizeFindTerm(New String("a"c, 4094) & pair & "tail")
        Assert.Equal(4096, fitting.Length)
        Assert.True(Char.IsLowSurrogate(fitting(fitting.Length - 1)))
    End Sub
End Class
