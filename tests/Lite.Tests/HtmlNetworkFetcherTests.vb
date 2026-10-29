#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' Specification 275 section 13 / H08: the online fetcher against fakes only - a fake resolver and a
' loopback HTTP/1.1 server reached through the connect test seam. Nothing here touches the internet.
Public Class HtmlNetworkFetcherTests
    Private Shared ReadOnly PublicIp As IPAddress = IPAddress.Parse("93.184.216.34")

    ' ---- address policy ----

    <Theory>
    <InlineData("0.0.0.0")>
    <InlineData("0.1.2.3")>
    <InlineData("10.0.0.1")>
    <InlineData("10.255.255.255")>
    <InlineData("100.64.0.1")>
    <InlineData("100.127.255.255")>
    <InlineData("127.0.0.1")>
    <InlineData("127.255.255.254")>
    <InlineData("169.254.169.254")>
    <InlineData("172.16.0.1")>
    <InlineData("172.31.255.255")>
    <InlineData("192.0.0.1")>
    <InlineData("192.0.2.1")>
    <InlineData("192.88.99.1")>
    <InlineData("192.168.1.1")>
    <InlineData("198.18.0.1")>
    <InlineData("198.19.255.255")>
    <InlineData("198.51.100.7")>
    <InlineData("203.0.113.9")>
    <InlineData("224.0.0.1")>
    <InlineData("239.255.255.250")>
    <InlineData("240.0.0.1")>
    <InlineData("255.255.255.255")>
    <InlineData("::")>
    <InlineData("::1")>
    <InlineData("fc00::1")>
    <InlineData("fd12:3456:789a::1")>
    <InlineData("fe80::1")>
    <InlineData("fec0::1")>
    <InlineData("ff02::1")>
    <InlineData("2001:db8::1")>
    <InlineData("3fff::1")>
    <InlineData("2001::1")>
    <InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")>
    <InlineData("2001:2::1")>
    <InlineData("2001:10::1")>
    <InlineData("100::1")>
    <InlineData("1::1")>
    <InlineData("64:ff9b::7f00:1")>
    <InlineData("64:ff9b::5db8:d822")>
    <InlineData("2002:7f00:1::")>
    <InlineData("2002:c0a8:101::")>
    <InlineData("2002:a00:1::")>
    <InlineData("::ffff:127.0.0.1")>
    <InlineData("::ffff:10.0.0.1")>
    <InlineData("::ffff:192.168.0.1")>
    <InlineData("::ffff:169.254.1.1")>
    Public Sub Forbidden_addresses_are_not_public(text As String)
        Assert.False(HtmlAddressPolicy.IsPublicUnicast(IPAddress.Parse(text)), text)
    End Sub

    <Theory>
    <InlineData("93.184.216.34")>
    <InlineData("8.8.8.8")>
    <InlineData("1.1.1.1")>
    <InlineData("100.63.255.255")>
    <InlineData("100.128.0.1")>
    <InlineData("172.15.255.255")>
    <InlineData("172.32.0.1")>
    <InlineData("198.17.255.255")>
    <InlineData("198.20.0.1")>
    <InlineData("223.255.255.255")>
    <InlineData("2606:2800:220:1::")>
    <InlineData("2001:4860:4860::8888")>
    <InlineData("2001:200::1")>
    <InlineData("2a00:1450:4001:80b::200e")>
    <InlineData("2002:5db8:d822::1")>
    <InlineData("::ffff:8.8.8.8")>
    <InlineData("::ffff:93.184.216.34")>
    Public Sub Public_addresses_are_accepted(text As String)
        Assert.True(HtmlAddressPolicy.IsPublicUnicast(IPAddress.Parse(text)), text)
    End Sub

    <Fact>
    Public Sub Null_address_is_not_public_and_mapped_addresses_normalize()
        Assert.False(HtmlAddressPolicy.IsPublicUnicast(Nothing))
        Assert.Null(HtmlAddressPolicy.Normalize(Nothing))
        Dim mapped = HtmlAddressPolicy.Normalize(IPAddress.Parse("::ffff:93.184.216.34"))
        Assert.Equal(AddressFamily.InterNetwork, mapped.AddressFamily)
        Assert.Equal("93.184.216.34", mapped.ToString())
        Dim plain6 = IPAddress.Parse("2606:2800:220:1::")
        Assert.Equal(plain6, HtmlAddressPolicy.Normalize(plain6))
    End Sub

    <Fact>
    Public Sub A_mixed_or_empty_answer_is_refused_as_a_whole()
        Assert.False(HtmlAddressPolicy.AllPublic(Nothing))
        Assert.False(HtmlAddressPolicy.AllPublic(New IPAddress() {}))
        Assert.True(HtmlAddressPolicy.AllPublic(New IPAddress() {PublicIp}))
        Assert.True(HtmlAddressPolicy.AllPublic(New IPAddress() {PublicIp, IPAddress.Parse("2606:2800:220:1::")}))
        Assert.True(HtmlAddressPolicy.AllPublic(New IPAddress() {IPAddress.Parse("::ffff:93.184.216.34")}))
        Assert.False(HtmlAddressPolicy.AllPublic(New IPAddress() {PublicIp, IPAddress.Parse("10.0.0.5")}))
        Assert.False(HtmlAddressPolicy.AllPublic(New IPAddress() {IPAddress.Parse("10.0.0.5"), PublicIp}))
        Assert.False(HtmlAddressPolicy.AllPublic(New IPAddress() {PublicIp, IPAddress.Parse("::ffff:127.0.0.1")}))
        Assert.False(HtmlAddressPolicy.AllPublic(New IPAddress() {PublicIp, Nothing}))
    End Sub

    <Theory>
    <InlineData("HTTP://Example.TEST/a?b=1", "http://example.test")>
    <InlineData("https://example.test:443/x", "https://example.test")>
    <InlineData("https://example.test:8443/x", "https://example.test:8443")>
    <InlineData("http://example.test:80/", "http://example.test")>
    <InlineData("http://93.184.216.34/", "http://93.184.216.34")>
    Public Sub Origin_is_scheme_host_and_non_default_port(url As String, expected As String)
        Assert.Equal(expected, HtmlAddressPolicy.OriginOf(New Uri(url)))
    End Sub

    <Fact>
    Public Sub Origin_of_nothing_or_a_relative_uri_is_empty()
        Assert.Equal("", HtmlAddressPolicy.OriginOf(Nothing))
        Assert.Equal("", HtmlAddressPolicy.OriginOf(New Uri("/relative", UriKind.Relative)))
    End Sub

    ' ---- budget ----

    <Fact>
    Public Sub Budget_takes_whole_requests_only_and_never_overshoots()
        Dim budget As New HtmlFetchBudget(100L)
        Assert.True(budget.TryConsume(60L))
        Assert.False(budget.TryConsume(41L))
        Assert.Equal(60L, budget.Consumed)
        Assert.True(budget.TryConsume(40L))
        Assert.False(budget.TryConsume(1L))
        Assert.False(budget.TryConsume(-1L))
        Assert.Equal(100L, budget.Consumed)
        Assert.Equal(128L * 1024L * 1024L, New HtmlFetchBudget().MaxBytes)
    End Sub

    <Fact>
    Public Sub Budget_is_exact_under_concurrent_consumers()
        Dim budget As New HtmlFetchBudget(1000L)
        Dim accepted As Integer = 0
        Parallel.For(0, 4000, Sub(i As Integer)
                                  If budget.TryConsume(1L) Then Interlocked.Increment(accepted)
                              End Sub)
        Assert.Equal(1000, accepted)
        Assert.Equal(1000L, budget.Consumed)
    End Sub

    ' ---- refusals before any I/O ----

    <Theory>
    <InlineData("http://user:pw@example.test/")>
    <InlineData("http://example.test:8080/")>
    <InlineData("https://example.test:80/")>
    <InlineData("http://example.test:443/")>
    <InlineData("http://localhost/")>
    <InlineData("http://localhost./")>
    <InlineData("http://app.localhost/")>
    <InlineData("http://printer.local/")>
    <InlineData("http://db.internal/")>
    <InlineData("http://x.invalid/")>
    <InlineData("http://intranet/")>
    <InlineData("http://127.0.0.1/")>
    <InlineData("http://10.1.2.3/")>
    <InlineData("http://192.168.0.10/")>
    <InlineData("http://169.254.169.254/latest/meta-data")>
    <InlineData("http://0.0.0.0/")>
    <InlineData("http://2130706433/")>
    <InlineData("http://[::1]/")>
    <InlineData("http://[::ffff:127.0.0.1]/")>
    <InlineData("http://[fe80::1]/")>
    <InlineData("ftp://example.test/")>
    <InlineData("file:///C:/secret.html")>
    <InlineData("file://server/share/page.html")>
    Public Async Function Hostile_urls_are_refused_before_any_resolve_or_connect(url As String) As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))))
            Dim result = Await rig.Fetcher.FetchAsync(Req(url), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Assert.Equal(0, rig.Resolver.Calls)
            Assert.Empty(rig.Connects)
            Assert.Empty(rig.Server.Requests)
        End Using
    End Function

    <Fact>
    Public Async Function Plain_http_needs_the_acknowledgement_and_the_origin_needs_the_approval() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))))
            Dim noAck = Req("http://example.test/", insecure:=New String() {})
            Assert.Equal(HtmlOutcome.NetworkBlocked, (Await rig.Fetcher.FetchAsync(noAck, CancellationToken.None)).Status)

            Dim noApproval = Req("http://example.test/", approved:=New String() {"http://other.test"})
            Assert.Equal(HtmlOutcome.NetworkBlocked, (Await rig.Fetcher.FetchAsync(noApproval, CancellationToken.None)).Status)

            Dim noSets = Req("http://example.test/")
            noSets.ApprovedOrigins = Nothing
            Assert.Equal(HtmlOutcome.NetworkBlocked, (Await rig.Fetcher.FetchAsync(noSets, CancellationToken.None)).Status)

            Assert.Equal(0, rig.Resolver.Calls)
            Assert.Empty(rig.Connects)
        End Using
    End Function

    <Fact>
    Public Async Function Only_get_and_head_and_only_known_contexts_are_accepted() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))))
            For Each method As String In New String() {"POST", "PUT", "DELETE", "CONNECT", ""}
                Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/", method:=method), CancellationToken.None)
                Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Next
            Dim other = Await rig.Fetcher.FetchAsync(Req("http://example.test/", HtmlRequestContext.Other), CancellationToken.None)
            Assert.Equal(HtmlOutcome.UnsupportedResource, other.Status)
            Assert.Empty(rig.Connects)
        End Using
    End Function

    <Fact>
    Public Async Function A_public_ip_literal_is_judged_directly_and_never_resolved() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("<p>ip</p>"))))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://93.184.216.34/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Assert.Equal(0, rig.Resolver.Calls)
            Dim endpoint = Assert.Single(rig.Connects)
            Assert.Equal(PublicIp, endpoint.Address)
            Assert.Equal(80, endpoint.Port)
        End Using
    End Function

    ' ---- a normal fetch ----

    <Fact>
    Public Async Function A_html_page_comes_back_with_charset_csp_and_a_clean_request() As Task
        Dim body = Encoding.UTF8.GetBytes("<html><body>h" & ChrW(&HE9) & "llo</body></html>")
        Using rig As New Rig(Always(Reply("200 OK", "text/html; charset=utf-8", body, "Content-Security-Policy: default-src 'none'", "Set-Cookie: sid=1; Path=/")))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Assert.True(result.IsOk)
            Assert.Equal(200, result.StatusCode)
            Assert.Equal("text/html", result.MimeType)
            Assert.Equal("utf-8", result.Charset)
            Assert.Equal(body, result.Bytes)
            Assert.Equal("default-src 'none'", result.ContentSecurityPolicy)
            Assert.Equal("http://example.test/", result.FinalUrl.AbsoluteUri)
            Assert.Equal(0, result.RedirectCount)
            Assert.Null(result.RedirectTarget)

            Assert.Equal("example.test", Assert.Single(rig.Resolver.Hosts))
            Dim endpoint = Assert.Single(rig.Connects)
            Assert.Equal(PublicIp, endpoint.Address)

            Dim raw = Assert.Single(rig.Server.Requests)
            Assert.StartsWith("GET / HTTP/1.1", raw, StringComparison.Ordinal)
            Assert.Contains("Host: example.test" & vbCrLf, raw)
            Assert.Contains("Accept: text/html", raw)
            Assert.Contains("Accept-Encoding: gzip, deflate, br", raw)
            Assert.Contains("User-Agent: Mozilla/5.0", raw)
            Assert.DoesNotContain("FastMediaSorter", raw, StringComparison.OrdinalIgnoreCase)
        End Using
    End Function

    <Fact>
    Public Async Function The_head_method_returns_status_and_type_without_a_body() As Task
        Dim head = "HTTP/1.1 200 OK" & vbCrLf & "Connection: close" & vbCrLf & "Content-Type: text/html; charset=windows-1251" & vbCrLf & "Content-Length: 5" & vbCrLf & vbCrLf
        Using rig As New Rig(Always(Encoding.Latin1.GetBytes(head)))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/p", method:="head"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Assert.Equal("windows-1251", result.Charset)
            Assert.Empty(result.Bytes)
            Assert.StartsWith("HEAD /p HTTP/1.1", Assert.Single(rig.Server.Requests), StringComparison.Ordinal)
        End Using
    End Function

    <Theory>
    <InlineData(HtmlRequestContext.Document, "application/pdf", "/", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Document, "text/plain", "/", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Document, "application/octet-stream", "/", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Document, "text/html; charset=iso-8859-1", "/", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Document, "application/xhtml+xml; charset=utf-8", "/", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Document, "TEXT/HTML", "/", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Stylesheet, "text/css", "/a.css", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Stylesheet, "text/html", "/a.css", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Stylesheet, "text/plain", "/a.css", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Image, "image/png", "/a.png", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Image, "image/svg+xml", "/a.svg", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Image, "image/x-icon", "/favicon.ico", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Image, "image/vnd.microsoft.icon", "/favicon.ico", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Image, "image/avif", "/a.avif", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Image, "image/tiff", "/a.tif", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Image, "text/html", "/a.png", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Font, "font/woff2", "/f", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Font, "application/font-woff", "/f", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Font, "application/x-font-ttf", "/f", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Font, "application/octet-stream", "/f.woff2", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Font, "application/octet-stream", "/f.OTF", HtmlOutcome.Ok)>
    <InlineData(HtmlRequestContext.Font, "application/octet-stream", "/f.bin", HtmlOutcome.UnsupportedResource)>
    <InlineData(HtmlRequestContext.Font, "text/html", "/f.woff", HtmlOutcome.UnsupportedResource)>
    Public Async Function Unexpected_mime_is_refused_by_context(context As HtmlRequestContext, contentType As String, path As String, expected As HtmlOutcome) As Task
        Using rig As New Rig(Always(Reply("200 OK", contentType, Encoding.UTF8.GetBytes("abc"))))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test" & path, context), CancellationToken.None)
            Assert.Equal(expected, result.Status)
            If expected = HtmlOutcome.Ok Then
                Assert.Equal(3, result.Bytes.Length)
            Else
                Assert.Empty(result.Bytes)
            End If
        End Using
    End Function

    <Fact>
    Public Async Function A_response_without_content_type_is_refused() As Task
        Using rig As New Rig(Always(Reply("200 OK", Nothing, Encoding.UTF8.GetBytes("abc"))))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.UnsupportedResource, result.Status)
        End Using
    End Function

    ' ---- redirects ----

    <Fact>
    Public Async Function A_same_origin_redirect_is_followed_relative_and_absolute() As Task
        Dim handler As Func(Of Integer, String, Stream, CancellationToken, Task) =
            Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) As Task
                Dim bytes As Byte() = Select3(index, Reply("302 Found", Nothing, Array.Empty(Of Byte)(), "Location: /b"),
                                              Reply("301 Moved Permanently", Nothing, Array.Empty(Of Byte)(), "Location: http://example.test/c"),
                                              Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("<p>end</p>")))
                Return stream.WriteAsync(bytes, 0, bytes.Length, ct)
            End Function
        Using rig As New Rig(handler)
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/a"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Assert.Equal("/c", result.FinalUrl.AbsolutePath)
            Assert.Equal(2, result.RedirectCount)
            Assert.Equal(3, rig.Server.Requests.Count)
            Assert.Equal(3, rig.Resolver.Calls)
        End Using
    End Function

    <Fact>
    Public Async Function A_cross_origin_redirect_waits_for_approval_and_is_not_fetched() As Task
        Using rig As New Rig(Always(Reply("302 Found", Nothing, Array.Empty(Of Byte)(), "Location: http://other.test/x?q=1")))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/a"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.RedirectNeedsApproval, result.Status)
            Assert.Equal("http://other.test/x?q=1", result.RedirectTarget.AbsoluteUri)
            Assert.Equal(302, result.StatusCode)
            Assert.Equal(0, result.RedirectCount)
            Assert.Single(rig.Server.Requests)
            Assert.Equal(New String() {"example.test"}, rig.Resolver.Hosts.ToArray())
        End Using
    End Function

    <Fact>
    Public Async Function An_approved_cross_origin_redirect_is_followed_but_plain_http_still_needs_its_acknowledgement() As Task
        Dim handler As Func(Of Integer, String, Stream, CancellationToken, Task) =
            Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) As Task
                Dim bytes As Byte() = If(index = 1,
                    Reply("307 Temporary Redirect", Nothing, Array.Empty(Of Byte)(), "Location: http://other.test/x"),
                    Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("<p>other</p>")))
                Return stream.WriteAsync(bytes, 0, bytes.Length, ct)
            End Function
        Dim both = New String() {"http://example.test", "http://other.test"}
        Using rig As New Rig(handler)
            Dim ok = Await rig.Fetcher.FetchAsync(Req("http://example.test/a", approved:=both, insecure:=both), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, ok.Status)
            Assert.Equal("other.test", ok.FinalUrl.Host)
            Assert.Equal(1, ok.RedirectCount)
        End Using
        Using rig As New Rig(Always(Reply("307 Temporary Redirect", Nothing, Array.Empty(Of Byte)(), "Location: http://other.test/x")))
            Dim blocked = Await rig.Fetcher.FetchAsync(Req("http://example.test/a", approved:=both, insecure:=New String() {"http://example.test"}), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, blocked.Status)
            Assert.Single(rig.Server.Requests)
        End Using
    End Function

    <Theory>
    <InlineData("Location: http://10.0.0.5/admin")>
    <InlineData("Location: http://127.0.0.1/")>
    <InlineData("Location: http://localhost/")>
    <InlineData("Location: http://[::1]/")>
    <InlineData("Location: http://example.test:8080/")>
    <InlineData("Location: http://user:pw@other.test/")>
    <InlineData("Location: file:///C:/secret.html")>
    <InlineData("Location: ftp://other.test/x")>
    <InlineData("Location: javascript:alert(1)")>
    Public Async Function A_redirect_to_a_forbidden_target_is_refused_without_asking(locationLine As String) As Task
        Using rig As New Rig(Always(Reply("302 Found", Nothing, Array.Empty(Of Byte)(), locationLine)))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/a"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Assert.Null(result.RedirectTarget)
            Assert.Single(rig.Server.Requests)
        End Using
    End Function

    <Fact>
    Public Async Function A_redirect_without_a_location_is_a_failure() As Task
        Using rig As New Rig(Always(Reply("301 Moved Permanently", Nothing, Array.Empty(Of Byte)())))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/a"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
        End Using
    End Function

    <Theory>
    <InlineData("https://a.example/", "http://a.example/", "https://a.example|http://a.example", "http://a.example", HtmlOutcome.NetworkBlocked)>
    <InlineData("https://a.example/", "http://b.example/", "https://a.example|http://b.example", "http://b.example", HtmlOutcome.NetworkBlocked)>
    <InlineData("https://a.example/", "https://a.example/x", "https://a.example", "", HtmlOutcome.Ok)>
    <InlineData("http://a.example/", "https://a.example/", "http://a.example|https://a.example", "http://a.example", HtmlOutcome.Ok)>
    <InlineData("http://a.example/", "http://a.example/x", "http://a.example", "http://a.example", HtmlOutcome.Ok)>
    <InlineData("http://a.example/", "http://a.example/x", "http://a.example", "", HtmlOutcome.NetworkBlocked)>
    <InlineData("https://a.example/", "https://b.example/", "https://a.example", "", HtmlOutcome.RedirectNeedsApproval)>
    <InlineData("https://a.example/", "https://b.example/", "https://a.example|https://b.example", "", HtmlOutcome.Ok)>
    <InlineData("https://a.example/", "https://127.0.0.1/", "https://a.example|https://127.0.0.1", "", HtmlOutcome.NetworkBlocked)>
    <InlineData("https://a.example/", "https://a.example:8443/", "https://a.example|https://a.example:8443", "", HtmlOutcome.NetworkBlocked)>
    <InlineData("https://a.example/", "https://u@a.example/", "https://a.example", "", HtmlOutcome.NetworkBlocked)>
    <InlineData("https://a.example/", "https://localhost/", "https://a.example|https://localhost", "", HtmlOutcome.NetworkBlocked)>
    Public Sub Redirect_verdicts_cover_downgrade_approval_and_policy(current As String, target As String, approvedCsv As String, insecureCsv As String, expected As HtmlOutcome)
        Dim request As New HtmlFetchRequest With {.Url = New Uri(current)}
        For Each origin As String In approvedCsv.Split("|"c)
            request.ApprovedOrigins.Add(origin)
        Next
        For Each origin As String In insecureCsv.Split(New Char() {"|"c}, StringSplitOptions.RemoveEmptyEntries)
            request.InsecureOrigins.Add(origin)
        Next
        Assert.Equal(expected, HtmlNetworkFetcher.EvaluateRedirect(New Uri(current), New Uri(target), request))
    End Sub

    <Fact>
    Public Async Function More_than_five_redirects_are_refused() As Task
        Dim handler As Func(Of Integer, String, Stream, CancellationToken, Task) =
            Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) As Task
                Dim bytes As Byte() = Reply("302 Found", Nothing, Array.Empty(Of Byte)(), "Location: /n" & index.ToString())
                Return stream.WriteAsync(bytes, 0, bytes.Length, ct)
            End Function
        Using rig As New Rig(handler)
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/start"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Assert.Equal(HtmlNetworkFetcher.MaxRedirects, result.RedirectCount)
            Assert.Equal(6, rig.Server.Requests.Count)
        End Using
    End Function

    ' ---- status codes ----

    <Theory>
    <InlineData(401, HtmlOutcome.AccessDenied)>
    <InlineData(407, HtmlOutcome.AccessDenied)>
    <InlineData(404, HtmlOutcome.SourceMissing)>
    <InlineData(410, HtmlOutcome.SourceMissing)>
    <InlineData(403, HtmlOutcome.NetworkBlocked)>
    <InlineData(429, HtmlOutcome.NetworkBlocked)>
    <InlineData(500, HtmlOutcome.NetworkBlocked)>
    <InlineData(503, HtmlOutcome.NetworkBlocked)>
    <InlineData(304, HtmlOutcome.NetworkBlocked)>
    Public Async Function Non_success_statuses_map_to_typed_outcomes_without_an_auth_flow(code As Integer, expected As HtmlOutcome) As Task
        Dim bytes = Reply(code.ToString() & " Status", "text/html", Encoding.UTF8.GetBytes("denied"), "WWW-Authenticate: Basic realm=""x""", "Proxy-Authenticate: Basic realm=""p""")
        Using rig As New Rig(Always(bytes))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/p"), CancellationToken.None)
            Assert.Equal(expected, result.Status)
            Assert.Equal(code, result.StatusCode)
            Assert.Single(rig.Server.Requests)
            Assert.Empty(result.Bytes)
        End Using
    End Function

    ' ---- nothing leaks to the server ----

    <Fact>
    Public Async Function Cookies_credentials_and_referer_are_never_sent() As Task
        Dim handler As Func(Of Integer, String, Stream, CancellationToken, Task) =
            Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) As Task
                Dim bytes As Byte() = If(index = 1,
                    Reply("302 Found", Nothing, Array.Empty(Of Byte)(), "Location: /next", "Set-Cookie: sid=abc; Path=/; HttpOnly", "Set-Cookie2: old=1"),
                    Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("<p>ok</p>"), "Set-Cookie: more=1"))
                Return stream.WriteAsync(bytes, 0, bytes.Length, ct)
            End Function
        Using rig As New Rig(handler)
            Dim first = Await rig.Fetcher.FetchAsync(Req("http://example.test/start"), CancellationToken.None)
            Dim second = Await rig.Fetcher.FetchAsync(Req("http://example.test/again"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, first.Status)
            Assert.Equal(HtmlOutcome.Ok, second.Status)
            Assert.Equal(3, rig.Server.Requests.Count)
            For Each raw As String In rig.Server.Requests
                Dim lower = raw.ToLowerInvariant()
                Assert.DoesNotContain(vbLf & "cookie:", lower)
                Assert.DoesNotContain("referer:", lower)
                Assert.DoesNotContain("authorization:", lower)
                Assert.DoesNotContain("proxy-", lower)
                Assert.DoesNotContain("origin:", lower)
                Assert.DoesNotContain("content-length:", lower)
            Next
        End Using
    End Function

    ' ---- body limits ----

    <Theory>
    <InlineData(HtmlRequestContext.Stylesheet, 2097153L)>
    <InlineData(HtmlRequestContext.Document, 16777217L)>
    <InlineData(HtmlRequestContext.Font, 8388609L)>
    <InlineData(HtmlRequestContext.Image, 33554433L)>
    Public Async Function A_declared_length_over_the_limit_stops_before_the_body(context As HtmlRequestContext, declared As Long) As Task
        Dim contentType = If(context = HtmlRequestContext.Stylesheet, "text/css", If(context = HtmlRequestContext.Document, "text/html", If(context = HtmlRequestContext.Font, "font/woff2", "image/png")))
        Dim head = "HTTP/1.1 200 OK" & vbCrLf & "Connection: close" & vbCrLf & "Content-Type: " & contentType & vbCrLf & "Content-Length: " & declared.ToString() & vbCrLf & vbCrLf
        Using rig As New Rig(Always(Encoding.Latin1.GetBytes(head)))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/big", context), CancellationToken.None)
            Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
            Assert.Empty(result.Bytes)
        End Using
    End Function

    <Fact>
    Public Async Function A_chunked_body_without_length_stops_at_the_limit() As Task
        Dim handler As Func(Of Integer, String, Stream, CancellationToken, Task) =
            Async Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) As Task
                Dim head = "HTTP/1.1 200 OK" & vbCrLf & "Connection: close" & vbCrLf & "Content-Type: text/css" & vbCrLf & "Transfer-Encoding: chunked" & vbCrLf & vbCrLf
                Dim headBytes = Encoding.Latin1.GetBytes(head)
                Await stream.WriteAsync(headBytes, 0, headBytes.Length, ct)
                Dim chunk(65535) As Byte
                Array.Fill(chunk, CByte(AscW("a"c)))
                Dim prefix = Encoding.Latin1.GetBytes("10000" & vbCrLf)
                Dim suffix = Encoding.Latin1.GetBytes(vbCrLf)
                For i As Integer = 0 To 255
                    Await stream.WriteAsync(prefix, 0, prefix.Length, ct)
                    Await stream.WriteAsync(chunk, 0, chunk.Length, ct)
                    Await stream.WriteAsync(suffix, 0, suffix.Length, ct)
                Next
            End Function
        Using rig As New Rig(handler)
            Dim budget As New HtmlFetchBudget()
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/s.css", HtmlRequestContext.Stylesheet, budget), CancellationToken.None)
            Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
            Assert.Empty(result.Bytes)
            Assert.True(budget.Consumed <= 2L * 1024L * 1024L)
        End Using
    End Function

    <Fact>
    Public Async Function A_gzip_bomb_is_stopped_at_the_expanded_limit() As Task
        Dim bomb = Compress(New Byte(5 * 1024 * 1024 - 1) {}, "gzip")
        Assert.True(bomb.Length < 100 * 1024)
        Using rig As New Rig(Always(Reply("200 OK", "text/css", bomb, "Content-Encoding: gzip")))
            Dim budget As New HtmlFetchBudget()
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/s.css", HtmlRequestContext.Stylesheet, budget), CancellationToken.None)
            Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
            Assert.Empty(result.Bytes)
            Assert.True(budget.Consumed <= 2L * 1024L * 1024L)
        End Using
    End Function

    <Theory>
    <InlineData("gzip", "gzip")>
    <InlineData("zlib", "deflate")>
    <InlineData("raw", "deflate")>
    <InlineData("br", "br")>
    <InlineData("gzip", "GZIP")>
    Public Async Function Compressed_bodies_are_expanded_and_the_budget_counts_the_expanded_size(kind As String, header As String) As Task
        Dim plain = Encoding.UTF8.GetBytes(String.Concat(Enumerable.Repeat("body { color: red; }" & vbLf, 200)))
        Dim packed = Compress(plain, kind)
        Assert.True(packed.Length < plain.Length)
        Using rig As New Rig(Always(Reply("200 OK", "text/css; charset=utf-8", packed, "Content-Encoding: " & header)))
            Dim budget As New HtmlFetchBudget()
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/s.css", HtmlRequestContext.Stylesheet, budget), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Assert.Equal(plain, result.Bytes)
            Assert.Equal(CLng(plain.Length), budget.Consumed)
        End Using
    End Function

    <Theory>
    <InlineData("zstd")>
    <InlineData("gzip, br")>
    <InlineData("compress")>
    Public Async Function Unknown_or_stacked_content_codings_are_refused(coding As String) As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/css", Encoding.UTF8.GetBytes("abc"), "Content-Encoding: " & coding)))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/s.css", HtmlRequestContext.Stylesheet), CancellationToken.None)
            Assert.Equal(HtmlOutcome.UnsupportedResource, result.Status)
        End Using
    End Function

    <Fact>
    Public Async Function A_corrupt_compressed_body_is_a_failure_not_a_partial_page() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/css", Encoding.UTF8.GetBytes("this is not gzip at all"), "Content-Encoding: gzip")))
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/s.css", HtmlRequestContext.Stylesheet), CancellationToken.None)
            Assert.NotEqual(HtmlOutcome.Ok, result.Status)
            Assert.Empty(result.Bytes)
        End Using
    End Function

    <Fact>
    Public Async Function An_exhausted_budget_stops_the_fetch_and_is_shared_between_fetches() As Task
        Dim body = New Byte(999) {}
        Array.Fill(body, CByte(AscW("x"c)))
        Using rig As New Rig(Always(Reply("200 OK", "text/html", body)))
            Dim small As New HtmlFetchBudget(100L)
            Dim tooSmall = Await rig.Fetcher.FetchAsync(Req("http://example.test/", HtmlRequestContext.Document, small), CancellationToken.None)
            Assert.Equal(HtmlOutcome.LimitExceeded, tooSmall.Status)
            Assert.Empty(tooSmall.Bytes)
            Assert.True(small.Consumed <= 100L)

            Dim shared1 As New HtmlFetchBudget(1500L)
            Dim first = Await rig.Fetcher.FetchAsync(Req("http://example.test/a", HtmlRequestContext.Document, shared1), CancellationToken.None)
            Dim second = Await rig.Fetcher.FetchAsync(Req("http://example.test/b", HtmlRequestContext.Document, shared1), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, first.Status)
            Assert.Equal(HtmlOutcome.LimitExceeded, second.Status)
            Assert.Equal(1000L, shared1.Consumed)
        End Using
    End Function

    ' ---- timeouts and cancellation ----

    <Fact>
    Public Async Function A_server_that_never_answers_times_out() As Task
        Using rig As New Rig(Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) Task.Delay(Timeout.Infinite, ct))
            rig.Fetcher.ResponseTimeout = TimeSpan.FromMilliseconds(400)
            Dim watch = Stopwatch.StartNew()
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.TimedOut, result.Status)
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "timeout took " & watch.Elapsed.ToString())
        End Using
    End Function

    <Fact>
    Public Async Function A_body_that_stalls_after_the_headers_times_out() As Task
        Dim handler As Func(Of Integer, String, Stream, CancellationToken, Task) =
            Async Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) As Task
                Dim head = "HTTP/1.1 200 OK" & vbCrLf & "Connection: close" & vbCrLf & "Content-Type: text/html" & vbCrLf & "Content-Length: 1000" & vbCrLf & vbCrLf & "0123456789"
                Dim bytes = Encoding.Latin1.GetBytes(head)
                Await stream.WriteAsync(bytes, 0, bytes.Length, ct)
                Await stream.FlushAsync(ct)
                Await Task.Delay(Timeout.Infinite, ct)
            End Function
        Using rig As New Rig(handler)
            rig.Fetcher.ResponseTimeout = TimeSpan.FromMilliseconds(400)
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.TimedOut, result.Status)
            Assert.Empty(result.Bytes)
        End Using
    End Function

    <Fact>
    Public Async Function Caller_cancellation_is_reported_as_cancelled_not_timed_out() As Task
        Using rig As New Rig(Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) Task.Delay(Timeout.Infinite, ct))
            Using cts As New CancellationTokenSource()
                cts.CancelAfter(300)
                Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), cts.Token)
                Assert.Equal(HtmlOutcome.Cancelled, result.Status)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_token_cancelled_up_front_does_no_work() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))))
            Using cts As New CancellationTokenSource()
                cts.Cancel()
                Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), cts.Token)
                Assert.Equal(HtmlOutcome.Cancelled, result.Status)
                Assert.Equal(0, rig.Resolver.Calls)
                Assert.Empty(rig.Connects)
            End Using
        End Using
    End Function

    ' ---- DNS: validation, rebinding, failover ----

    <Fact>
    Public Async Function A_rebound_second_answer_never_reaches_a_socket() As Task
        Dim answer As Func(Of Integer, String, IPAddress()) =
            Function(n As Integer, host As String) If(n = 1, New IPAddress() {PublicIp}, New IPAddress() {IPAddress.Loopback})
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("<p>one</p>"))), answer)
            Dim first = Await rig.Fetcher.FetchAsync(Req("http://example.test/a"), CancellationToken.None)
            Dim second = Await rig.Fetcher.FetchAsync(Req("http://example.test/b"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, first.Status)
            Assert.Equal(HtmlOutcome.NetworkBlocked, second.Status)
            Assert.Equal(2, rig.Resolver.Calls)
            Assert.Single(rig.Server.Requests)
            Dim endpoint = Assert.Single(rig.Connects)
            Assert.Equal(PublicIp, endpoint.Address)
            Assert.All(rig.Connects, Sub(e As IPEndPoint) Assert.True(HtmlAddressPolicy.IsPublicUnicast(e.Address)))
        End Using
    End Function

    <Fact>
    Public Async Function Every_new_connection_resolves_again_and_a_redirect_hop_is_validated_on_its_own() As Task
        Dim answer As Func(Of Integer, String, IPAddress()) =
            Function(n As Integer, host As String) If(host = "other.test", New IPAddress() {IPAddress.Parse("10.9.8.7")}, New IPAddress() {PublicIp})
        Dim both = New String() {"http://example.test", "http://other.test"}
        Using rig As New Rig(Always(Reply("302 Found", Nothing, Array.Empty(Of Byte)(), "Location: http://other.test/x")), answer)
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/a", approved:=both, insecure:=both), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Assert.Equal(New String() {"example.test", "other.test"}, rig.Resolver.Hosts.ToArray())
            Assert.Single(rig.Connects)
            Assert.Single(rig.Server.Requests)
        End Using
    End Function

    <Theory>
    <InlineData("93.184.216.34,10.0.0.5")>
    <InlineData("10.0.0.5,93.184.216.34")>
    <InlineData("93.184.216.34,::ffff:127.0.0.1")>
    <InlineData("93.184.216.34,2001:db8::1")>
    <InlineData("127.0.0.1")>
    <InlineData("::ffff:192.168.1.1")>
    <InlineData("")>
    Public Async Function A_mixed_forbidden_or_empty_answer_fails_the_connection(csv As String) As Task
        Dim addresses = csv.Split(New Char() {","c}, StringSplitOptions.RemoveEmptyEntries).Select(Function(s) IPAddress.Parse(s)).ToArray()
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))), Function(n As Integer, host As String) addresses)
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Assert.Empty(rig.Connects)
            Assert.Empty(rig.Server.Requests)
        End Using
    End Function

    <Fact>
    Public Async Function A_resolver_failure_is_a_network_failure() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))),
                             Function(n As Integer, host As String) As IPAddress()
                                 Throw New SocketException(CInt(SocketError.HostNotFound))
                             End Function)
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Assert.Empty(rig.Connects)
        End Using
    End Function

    <Fact>
    Public Async Function A_mapped_public_answer_is_connected_as_plain_ipv4() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("<p>m</p>"))),
                             Function(n As Integer, host As String) New IPAddress() {IPAddress.Parse("::ffff:93.184.216.34")})
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Dim endpoint = Assert.Single(rig.Connects)
            Assert.Equal(AddressFamily.InterNetwork, endpoint.AddressFamily)
            Assert.Equal(PublicIp, endpoint.Address)
        End Using
    End Function

    <Fact>
    Public Async Function The_next_validated_address_is_tried_when_the_first_fails() As Task
        Dim second = IPAddress.Parse("93.184.216.35")
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("<p>f</p>"))),
                             Function(n As Integer, host As String) New IPAddress() {PublicIp, second})
            rig.FailAddresses.Add(PublicIp.ToString())
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Assert.Equal(New IPAddress() {PublicIp, second}, rig.Connects.Select(Function(e) e.Address).ToArray())
            Assert.Equal(1, rig.Resolver.Calls)
        End Using
    End Function

    <Fact>
    Public Async Function When_every_validated_address_fails_the_fetch_fails() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))))
            rig.FailAddresses.Add(PublicIp.ToString())
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.NetworkBlocked, result.Status)
            Assert.Empty(rig.Server.Requests)
        End Using
    End Function

    ' ---- lifetime and concurrency ----

    <Fact>
    Public Async Function Dispose_is_idempotent_and_a_disposed_fetcher_reports_cancelled() As Task
        Using rig As New Rig(Always(Reply("200 OK", "text/html", Encoding.UTF8.GetBytes("x"))))
            rig.Fetcher.Dispose()
            rig.Fetcher.Dispose()
            Dim result = Await rig.Fetcher.FetchAsync(Req("http://example.test/"), CancellationToken.None)
            Assert.Equal(HtmlOutcome.Cancelled, result.Status)
        End Using
    End Function

    <Fact>
    Public Async Function Concurrent_fetches_on_one_instance_share_one_budget() As Task
        Dim body = Encoding.UTF8.GetBytes(New String("z"c, 500))
        Using rig As New Rig(Always(Reply("200 OK", "text/html", body)))
            Dim budget As New HtmlFetchBudget()
            Dim tasks = Enumerable.Range(0, 12).Select(Function(i) rig.Fetcher.FetchAsync(Req("http://example.test/p" & i.ToString(), HtmlRequestContext.Document, budget), CancellationToken.None)).ToArray()
            Dim results = Await Task.WhenAll(tasks)
            Assert.All(results, Sub(r As HtmlFetchResult) Assert.Equal(HtmlOutcome.Ok, r.Status))
            Assert.Equal(12 * 500L, budget.Consumed)
            Assert.Equal(12, rig.Server.Requests.Count)
        End Using
    End Function

    ' ---- test infrastructure ----

    Private Shared Function Req(url As String, Optional context As HtmlRequestContext = HtmlRequestContext.Document,
                                Optional budget As HtmlFetchBudget = Nothing, Optional method As String = "GET",
                                Optional approved As String() = Nothing, Optional insecure As String() = Nothing) As HtmlFetchRequest
        Dim uri As New Uri(url)
        Dim origin As String = HtmlAddressPolicy.OriginOf(uri)
        Dim request As New HtmlFetchRequest With {.Url = uri, .Context = context, .Method = method, .Budget = budget}
        For Each item As String In If(approved, New String() {origin})
            request.ApprovedOrigins.Add(item)
        Next
        For Each item As String In If(insecure, New String() {origin})
            request.InsecureOrigins.Add(item)
        Next
        Return request
    End Function

    Private Shared Function Reply(status As String, contentType As String, body As Byte(), ParamArray extra As String()) As Byte()
        Dim head As New StringBuilder()
        head.Append("HTTP/1.1 ").Append(status).Append(vbCrLf)
        head.Append("Connection: close").Append(vbCrLf)
        If contentType IsNot Nothing Then head.Append("Content-Type: ").Append(contentType).Append(vbCrLf)
        For Each line As String In extra
            head.Append(line).Append(vbCrLf)
        Next
        head.Append("Content-Length: ").Append(body.Length.ToString()).Append(vbCrLf).Append(vbCrLf)
        Dim headBytes = Encoding.Latin1.GetBytes(head.ToString())
        Dim all(headBytes.Length + body.Length - 1) As Byte
        Buffer.BlockCopy(headBytes, 0, all, 0, headBytes.Length)
        Buffer.BlockCopy(body, 0, all, headBytes.Length, body.Length)
        Return all
    End Function

    Private Shared Function Always(bytes As Byte()) As Func(Of Integer, String, Stream, CancellationToken, Task)
        Return Function(index As Integer, raw As String, stream As Stream, ct As CancellationToken) stream.WriteAsync(bytes, 0, bytes.Length, ct)
    End Function

    ' The reply for connection 1, 2 and every later one.
    Private Shared Function Select3(index As Integer, one As Byte(), two As Byte(), more As Byte()) As Byte()
        If index = 1 Then Return one
        If index = 2 Then Return two
        Return more
    End Function

    Private Shared Function Compress(data As Byte(), kind As String) As Byte()
        Using output As New MemoryStream()
            Using encoder As Stream = OpenEncoder(output, kind)
                encoder.Write(data, 0, data.Length)
            End Using
            Return output.ToArray()
        End Using
    End Function

    Private Shared Function OpenEncoder(output As Stream, kind As String) As Stream
        Select Case kind
            Case "gzip" : Return New GZipStream(output, CompressionLevel.Optimal, leaveOpen:=True)
            Case "zlib" : Return New ZLibStream(output, CompressionLevel.Optimal, leaveOpen:=True)
            Case "raw" : Return New DeflateStream(output, CompressionLevel.Optimal, leaveOpen:=True)
            Case Else : Return New BrotliStream(output, CompressionLevel.Optimal, leaveOpen:=True)
        End Select
    End Function

    Private NotInheritable Class FakeResolver
        Implements IHtmlDnsResolver

        Private ReadOnly _answer As Func(Of Integer, String, IPAddress())
        Private _calls As Integer
        Public ReadOnly Hosts As New ConcurrentQueue(Of String)()

        Public Sub New(answer As Func(Of Integer, String, IPAddress()))
            _answer = answer
        End Sub

        Public ReadOnly Property Calls As Integer
            Get
                Return Volatile.Read(_calls)
            End Get
        End Property

        Public Function ResolveAsync(host As String, token As CancellationToken) As Task(Of IPAddress()) Implements IHtmlDnsResolver.ResolveAsync
            Dim number As Integer = Interlocked.Increment(_calls)
            Hosts.Enqueue(host)
            Return Task.FromResult(_answer(number, host))
        End Function
    End Class

    ' A one-request-per-connection HTTP/1.1 server on the loopback interface.
    Private NotInheritable Class FakeServer
        Implements IDisposable

        Private ReadOnly _listener As TcpListener
        Private ReadOnly _stop As New CancellationTokenSource()
        Private ReadOnly _handler As Func(Of Integer, String, Stream, CancellationToken, Task)
        Private _connections As Integer
        Public ReadOnly Requests As New ConcurrentQueue(Of String)()

        Public Sub New(handler As Func(Of Integer, String, Stream, CancellationToken, Task))
            _handler = handler
            _listener = New TcpListener(IPAddress.Loopback, 0)
            _listener.Start()
            Dim loopTask As Task = Task.Run(Function() AcceptLoopAsync())
            GC.KeepAlive(loopTask)
        End Sub

        Public ReadOnly Property Port As Integer
            Get
                Return CType(_listener.LocalEndpoint, IPEndPoint).Port
            End Get
        End Property

        Private Async Function AcceptLoopAsync() As Task
            Try
                While Not _stop.IsCancellationRequested
                    Dim client As TcpClient = Await _listener.AcceptTcpClientAsync(_stop.Token)
                    Dim serial As Integer = Interlocked.Increment(_connections)
                    StartServing(client, serial)
                End While
            Catch ex As Exception
                ' The listener was stopped.
            End Try
        End Function

        Private Sub StartServing(client As TcpClient, serial As Integer)
            Dim served As Task = Task.Run(Function() ServeAsync(client, serial))
            GC.KeepAlive(served)
        End Sub

        Private Async Function ServeAsync(client As TcpClient, serial As Integer) As Task
            Using client
                Try
                    Dim stream As NetworkStream = client.GetStream()
                    Dim raw As String = Await ReadHeadersAsync(stream)
                    If raw Is Nothing Then Return
                    Requests.Enqueue(raw)
                    Await _handler(serial, raw, stream, _stop.Token)
                    Await stream.FlushAsync(_stop.Token)
                    client.Client.Shutdown(SocketShutdown.Send)
                Catch ex As Exception
                    ' The client went away (a limit or a timeout fired) or the server is stopping.
                End Try
            End Using
        End Function

        Private Async Function ReadHeadersAsync(stream As NetworkStream) As Task(Of String)
            Dim text As New StringBuilder()
            Dim buffer(4095) As Byte
            While text.Length <= 32768
                Dim count As Integer = Await stream.ReadAsync(buffer, 0, buffer.Length, _stop.Token)
                If count <= 0 Then Return Nothing
                text.Append(Encoding.Latin1.GetString(buffer, 0, count))
                If text.ToString().Contains(vbCrLf & vbCrLf) Then Return text.ToString()
            End While
            Return Nothing
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _stop.Cancel()
            _listener.Stop()
        End Sub
    End Class

    ' Fetcher + fake resolver + loopback server; every "connection" lands on the server whatever address was validated.
    Private NotInheritable Class Rig
        Implements IDisposable

        Public ReadOnly Server As FakeServer
        Public ReadOnly Resolver As FakeResolver
        Public ReadOnly Fetcher As HtmlNetworkFetcher
        Public ReadOnly Connects As New ConcurrentQueue(Of IPEndPoint)()
        Public ReadOnly FailAddresses As New HashSet(Of String)()

        Public Sub New(handler As Func(Of Integer, String, Stream, CancellationToken, Task), Optional answer As Func(Of Integer, String, IPAddress()) = Nothing)
            Server = New FakeServer(handler)
            Dim resolverAnswer As Func(Of Integer, String, IPAddress()) = answer
            If resolverAnswer Is Nothing Then resolverAnswer = Function(number As Integer, host As String) New IPAddress() {PublicIp}
            Resolver = New FakeResolver(resolverAnswer)
            Fetcher = New HtmlNetworkFetcher(Resolver, AddressOf ConnectAsync)
        End Sub

        Private Async Function ConnectAsync(endpoint As IPEndPoint, token As CancellationToken) As Task(Of Stream)
            Connects.Enqueue(endpoint)
            If FailAddresses.Contains(endpoint.Address.ToString()) Then Throw New SocketException(CInt(SocketError.ConnectionRefused))
            Dim socket As New Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            Try
                Await socket.ConnectAsync(New IPEndPoint(IPAddress.Loopback, Server.Port), token)
                Return New NetworkStream(socket, ownsSocket:=True)
            Catch
                socket.Dispose()
                Throw
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Fetcher.Dispose()
            Server.Dispose()
        End Sub
    End Class
End Class
#End If
