#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Security.Authentication
Imports System.Threading
Imports System.Threading.Tasks

' Specification 275 section 13 (T-5): the only network path of the HTML host - DNS/IP policy,
' manual redirects, origin approvals and body limits. No UI and no credentials, and none of the
' application's other HTTP clients: the handler below carries no cookies, proxy, credentials or
' client certificates, and every connection goes through ConnectCallback.

' Resolves a host name. The fetcher asks once per NEW connection and never keeps the answer.
Friend Interface IHtmlDnsResolver
    Function ResolveAsync(host As String, token As CancellationToken) As Task(Of IPAddress())
End Interface

Friend NotInheritable Class SystemHtmlDnsResolver
    Implements IHtmlDnsResolver

    Public Function ResolveAsync(host As String, token As CancellationToken) As Task(Of IPAddress()) Implements IHtmlDnsResolver.ResolveAsync
        Return Dns.GetHostAddressesAsync(host, token)
    End Function
End Class

' Section 13 address policy: only globally routable unicast destinations are allowed.
Friend Module HtmlAddressPolicy
    ' IPv4-mapped IPv6 (::ffff:a.b.c.d) is judged, and connected to, as the plain IPv4 address.
    Public Function Normalize(address As IPAddress) As IPAddress
        If address Is Nothing Then Return Nothing
        If address.AddressFamily = AddressFamily.InterNetworkV6 AndAlso address.IsIPv4MappedToIPv6 Then Return address.MapToIPv4()
        Return address
    End Function

    Public Function IsPublicUnicast(address As IPAddress) As Boolean
        Dim ip As IPAddress = Normalize(address)
        If ip Is Nothing Then Return False
        Dim bytes As Byte() = ip.GetAddressBytes()
        If ip.AddressFamily = AddressFamily.InterNetwork Then Return bytes.Length = 4 AndAlso IsPublicV4(bytes, 0)
        If ip.AddressFamily = AddressFamily.InterNetworkV6 Then Return bytes.Length = 16 AndAlso IsPublicV6(bytes)
        Return False
    End Function

    ' A mixed answer (one forbidden destination among public ones) is refused as a whole.
    Public Function AllPublic(addresses As IEnumerable(Of IPAddress)) As Boolean
        If addresses Is Nothing Then Return False
        Dim any As Boolean = False
        For Each address As IPAddress In addresses
            If Not IsPublicUnicast(address) Then Return False
            any = True
        Next
        Return any
    End Function

    ' Lowercase scheme://host[:port], the port only when it is not the scheme default.
    Public Function OriginOf(uri As Uri) As String
        If uri Is Nothing OrElse Not uri.IsAbsoluteUri Then Return ""
        Dim host As String = If(uri.HostNameType = UriHostNameType.Dns, uri.IdnHost, uri.Host)
        Dim origin As String = uri.Scheme.ToLowerInvariant() & "://" & host.ToLowerInvariant()
        If Not uri.IsDefaultPort Then origin &= ":" & uri.Port.ToString(CultureInfo.InvariantCulture)
        Return origin
    End Function

    Private Function IsPublicV4(b As Byte(), offset As Integer) As Boolean
        Dim a As Integer = b(offset)
        Dim c As Integer = b(offset + 1)
        Dim d As Integer = b(offset + 2)
        If a = 0 OrElse a = 10 OrElse a = 127 Then Return False
        ' 224/4 multicast, 240/4 reserved and the 255.255.255.255 broadcast.
        If a >= 224 Then Return False
        If a = 100 AndAlso (c And &HC0) = 64 Then Return False
        If a = 169 AndAlso c = 254 Then Return False
        If a = 172 AndAlso (c And &HF0) = 16 Then Return False
        If a = 192 Then
            If c = 0 AndAlso (d = 0 OrElse d = 2) Then Return False
            If c = 168 Then Return False
            If c = 88 AndAlso d = 99 Then Return False
        End If
        If a = 198 Then
            If (c And &HFE) = 18 Then Return False
            If c = 51 AndAlso d = 100 Then Return False
        End If
        If a = 203 AndAlso c = 0 AndAlso d = 113 Then Return False
        Return True
    End Function

    Private Function IsPublicV6(b As Byte()) As Boolean
        ' Only the global unicast block 2000::/3 can be public; this alone drops ::, ::1, fc00::/7,
        ' fe80::/10, fec0::/10, ff00::/8, 100::/64 and the NAT64 prefix 64:ff9b::/96.
        If (b(0) And &HE0) <> &H20 Then Return False
        ' 2001::/23: IETF protocol assignments, which holds Teredo 2001::/32 and ORCHID.
        If b(0) = &H20 AndAlso b(1) = &H1 AndAlso (b(2) And &HFE) = 0 Then Return False
        ' 2001:db8::/32 documentation.
        If b(0) = &H20 AndAlso b(1) = &H1 AndAlso b(2) = &HD AndAlso b(3) = &HB8 Then Return False
        ' 3fff::/20 documentation.
        If b(0) = &H3F AndAlso b(1) = &HFF AndAlso (b(2) And &HF0) = 0 Then Return False
        ' 2002::/16 6to4: judged by the IPv4 address embedded in bytes 2..5.
        If b(0) = &H20 AndAlso b(1) = &H2 Then Return IsPublicV4(b, 2)
        Return True
    End Function
End Module

' One byte budget shared by every fetch of a top-level navigation (section 14, 128 MiB).
Friend NotInheritable Class HtmlFetchBudget
    Private ReadOnly _max As Long
    Private _consumed As Long

    Public Sub New(Optional maxBytes As Long = 128L * 1024L * 1024L)
        _max = Math.Max(0L, maxBytes)
    End Sub

    Public ReadOnly Property Consumed As Long
        Get
            Return Interlocked.Read(_consumed)
        End Get
    End Property

    Public ReadOnly Property MaxBytes As Long
        Get
            Return _max
        End Get
    End Property

    ' Takes count bytes, or takes nothing and returns False when they do not fit any more.
    Public Function TryConsume(count As Long) As Boolean
        If count < 0L Then Return False
        Dim taken As Boolean = False
        Dim finished As Boolean = False
        Do Until finished
            Dim current As Long = Interlocked.Read(_consumed)
            If count > _max - current Then
                finished = True
            ElseIf Interlocked.CompareExchange(_consumed, current + count, current) = current Then
                taken = True
                finished = True
            End If
        Loop
        Return taken
    End Function
End Class

Friend NotInheritable Class HtmlFetchRequest
    Public Property Url As Uri
    Public Property Context As HtmlRequestContext = HtmlRequestContext.Document
    Public Property Method As String = "GET"
    ' Origins (HtmlAddressPolicy.OriginOf form) this fetch may talk to.
    Public Property ApprovedOrigins As ISet(Of String) = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    ' Origins whose plain http:// use the user has acknowledged.
    Public Property InsecureOrigins As ISet(Of String) = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    ' Shared across the fetches of one navigation; a private default budget when Nothing.
    Public Property Budget As HtmlFetchBudget
End Class

Friend NotInheritable Class HtmlFetchResult
    Public Property Status As HtmlOutcome = HtmlOutcome.Ok
    Public Property FinalUrl As Uri
    Public Property StatusCode As Integer
    Public Property MimeType As String = ""
    Public Property Charset As String = ""
    Public Property Bytes As Byte() = Array.Empty(Of Byte)()
    ' Set together with RedirectNeedsApproval; nothing was fetched from it.
    Public Property RedirectTarget As Uri
    Public Property ContentSecurityPolicy As String = ""
    Public Property RedirectCount As Integer

    Public ReadOnly Property IsOk As Boolean
        Get
            Return Status = HtmlOutcome.Ok
        End Get
    End Property
End Class

Friend NotInheritable Class HtmlNetworkFetcher
    Implements IDisposable

    Friend Const MaxRedirects As Integer = 5
    Private Const DocumentLimit As Long = 16L * 1024L * 1024L
    Private Const StylesheetLimit As Long = 2L * 1024L * 1024L
    Private Const ImageLimit As Long = 32L * 1024L * 1024L
    Private Const FontLimit As Long = 8L * 1024L * 1024L
    Private Const UserAgent As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"

    Private Shared ReadOnly ImageTypes As New HashSet(Of String)(StringComparer.Ordinal) From {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp", "image/avif",
        "image/svg+xml", "image/x-icon", "image/vnd.microsoft.icon"}

    Private Shared ReadOnly FontTypes As New HashSet(Of String)(StringComparer.Ordinal) From {
        "application/font-woff", "application/font-woff2", "application/x-font-ttf", "application/x-font-otf"}

    Private Shared ReadOnly LocalSuffixes As String() = {".localhost", ".local", ".localdomain", ".internal", ".invalid", ".home.arpa"}

    Private ReadOnly _resolver As IHtmlDnsResolver
    Private ReadOnly _connectOverride As Func(Of IPEndPoint, CancellationToken, Task(Of Stream))
    Private ReadOnly _client As HttpClient
    Private _disposed As Integer

    ' Whole fetch including redirects and body; settable so a test does not wait 30 s.
    Friend Property ResponseTimeout As TimeSpan = TimeSpan.FromSeconds(30)
    ' With several validated addresses, one attempt is cut after this so the next can be tried.
    Friend Property AttemptTimeout As TimeSpan = TimeSpan.FromSeconds(4)

    ' connectOverride is a test seam: after the policy validated the DNS answer the connection to the
    ' validated endpoint goes through it. Production passes Nothing and connects a Socket to that IP.
    Public Sub New(resolver As IHtmlDnsResolver, Optional connectOverride As Func(Of IPEndPoint, CancellationToken, Task(Of Stream)) = Nothing)
        If resolver Is Nothing Then Throw New ArgumentNullException(NameOf(resolver))
        _resolver = resolver
        _connectOverride = connectOverride
        Dim handler As New SocketsHttpHandler With {
            .AllowAutoRedirect = False,
            .UseCookies = False,
            .UseProxy = False,
            .Proxy = Nothing,
            .Credentials = Nothing,
            .DefaultProxyCredentials = Nothing,
            .PreAuthenticate = False,
            .AutomaticDecompression = DecompressionMethods.None,
            .ConnectTimeout = TimeSpan.FromSeconds(10),
            .MaxConnectionsPerServer = 4,
            .PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            .PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
            .MaxResponseHeadersLength = 32,
            .ConnectCallback = AddressOf OnConnect,
            .SslOptions = New SslClientAuthenticationOptions With {
                .EnabledSslProtocols = SslProtocols.Tls12 Or SslProtocols.Tls13}}
        _client = New HttpClient(handler, disposeHandler:=True) With {.Timeout = System.Threading.Timeout.InfiniteTimeSpan}
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(_disposed, 1) = 0 Then _client.Dispose()
    End Sub

    Public Async Function FetchAsync(request As HtmlFetchRequest, token As CancellationToken) As Task(Of HtmlFetchResult)
        If request Is Nothing Then Throw New ArgumentNullException(NameOf(request))
        Dim result As New HtmlFetchResult With {.FinalUrl = request.Url}
        If Volatile.Read(_disposed) <> 0 OrElse token.IsCancellationRequested Then
            result.Status = HtmlOutcome.Cancelled
            Return result
        End If
        Dim early As HtmlOutcome = ValidateRequest(request)
        If early <> HtmlOutcome.Ok Then
            result.Status = early
            Return result
        End If

        Dim budget As HtmlFetchBudget = If(request.Budget, New HtmlFetchBudget())
        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token)
            linked.CancelAfter(ResponseTimeout)
            Try
                Await RunAsync(request, budget, result, linked.Token).ConfigureAwait(False)
            Catch ex As Exception
                result.Status = MapFailure(ex, token, linked)
                result.Bytes = Array.Empty(Of Byte)()
            End Try
        End Using
        Return result
    End Function

    ' ---- request validation (no I/O) ----

    Friend Shared Function ValidateRequest(request As HtmlFetchRequest) As HtmlOutcome
        If request.Context = HtmlRequestContext.Other Then Return HtmlOutcome.UnsupportedResource
        Dim method As String = If(request.Method, "").Trim().ToUpperInvariant()
        If method <> "GET" AndAlso method <> "HEAD" Then Return HtmlOutcome.NetworkBlocked
        If Not ValidateShape(request.Url) Then Return HtmlOutcome.NetworkBlocked
        If Not IsApproved(request, request.Url) Then Return HtmlOutcome.NetworkBlocked
        If request.Url.Scheme = Uri.UriSchemeHttp AndAlso Not IsInsecureAcknowledged(request, request.Url) Then Return HtmlOutcome.NetworkBlocked
        Return HtmlOutcome.Ok
    End Function

    ' Scheme, user info, port, host name and IP-literal checks shared by the first URL and every redirect.
    Friend Shared Function ValidateShape(url As Uri) As Boolean
        If url Is Nothing OrElse Not url.IsAbsoluteUri Then Return False
        Dim secure As Boolean = String.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        If Not secure AndAlso Not String.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) Then Return False
        If url.UserInfo.Length > 0 Then Return False
        If url.Port <> If(secure, 443, 80) Then Return False
        Select Case url.HostNameType
            Case UriHostNameType.IPv4, UriHostNameType.IPv6
                Dim literal As IPAddress = Nothing
                If Not IPAddress.TryParse(url.DnsSafeHost, literal) Then Return False
                Return HtmlAddressPolicy.IsPublicUnicast(literal)
            Case UriHostNameType.Dns
                Return IsAcceptableName(url.IdnHost)
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function IsAcceptableName(host As String) As Boolean
        Dim name As String = If(host, "").Trim().TrimEnd("."c).ToLowerInvariant()
        If name.Length = 0 OrElse name.Length > 253 Then Return False
        ' A single label is resolved through the local suffix list, LLMNR or NetBIOS, never the public DNS.
        If name.IndexOf("."c) < 0 Then Return False
        If String.Equals(name, "localhost", StringComparison.Ordinal) Then Return False
        For Each suffix As String In LocalSuffixes
            If name.EndsWith(suffix, StringComparison.Ordinal) Then Return False
        Next
        Return True
    End Function

    Private Shared Function IsApproved(request As HtmlFetchRequest, url As Uri) As Boolean
        Return request.ApprovedOrigins IsNot Nothing AndAlso request.ApprovedOrigins.Contains(HtmlAddressPolicy.OriginOf(url))
    End Function

    Private Shared Function IsInsecureAcknowledged(request As HtmlFetchRequest, url As Uri) As Boolean
        Return request.InsecureOrigins IsNot Nothing AndAlso request.InsecureOrigins.Contains(HtmlAddressPolicy.OriginOf(url))
    End Function

    ' Verdict for a redirect target: Ok = follow, RedirectNeedsApproval = ask the user, anything else = refuse.
    Friend Shared Function EvaluateRedirect(current As Uri, target As Uri, request As HtmlFetchRequest) As HtmlOutcome
        If current Is Nothing OrElse request Is Nothing Then Return HtmlOutcome.NetworkBlocked
        If Not ValidateShape(target) Then Return HtmlOutcome.NetworkBlocked
        If String.Equals(current.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) AndAlso
           String.Equals(target.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) Then Return HtmlOutcome.NetworkBlocked
        If Not IsApproved(request, target) Then Return HtmlOutcome.RedirectNeedsApproval
        If String.Equals(target.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) AndAlso
           Not IsInsecureAcknowledged(request, target) Then Return HtmlOutcome.NetworkBlocked
        Return HtmlOutcome.Ok
    End Function

    ' ---- the fetch itself ----

    Private Async Function RunAsync(request As HtmlFetchRequest, budget As HtmlFetchBudget, result As HtmlFetchResult, token As CancellationToken) As Task
        Dim method As String = request.Method.Trim().ToUpperInvariant()
        Dim current As Uri = request.Url
        For hop As Integer = 0 To MaxRedirects
            result.FinalUrl = current
            Using message As HttpRequestMessage = BuildRequest(method, current, request.Context)
                Using response As HttpResponseMessage = Await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(False)
                    Dim code As Integer = CInt(response.StatusCode)
                    result.StatusCode = code
                    If code = 301 OrElse code = 302 OrElse code = 303 OrElse code = 307 OrElse code = 308 Then
                        Dim target As Uri = ResolveLocation(current, response.Headers.Location)
                        If target Is Nothing OrElse hop >= MaxRedirects Then
                            result.Status = HtmlOutcome.NetworkBlocked
                            Return
                        End If
                        Dim verdict As HtmlOutcome = EvaluateRedirect(current, target, request)
                        If verdict = HtmlOutcome.RedirectNeedsApproval Then
                            result.RedirectTarget = target
                            result.Status = HtmlOutcome.RedirectNeedsApproval
                            Return
                        End If
                        If verdict <> HtmlOutcome.Ok Then
                            result.Status = verdict
                            Return
                        End If
                        result.RedirectCount += 1
                        current = target
                        Continue For
                    End If

                    If code < 200 OrElse code > 299 Then
                        result.Status = StatusOutcome(code)
                        Return
                    End If
                    Await ReadSuccessAsync(response, request.Context, current, method, budget, result, token).ConfigureAwait(False)
                    Return
                End Using
            End Using
        Next
        result.Status = HtmlOutcome.NetworkBlocked
    End Function

    Private Shared Function StatusOutcome(code As Integer) As HtmlOutcome
        Select Case code
            Case 401, 407 : Return HtmlOutcome.AccessDenied
            Case 404, 410 : Return HtmlOutcome.SourceMissing
            Case Else : Return HtmlOutcome.NetworkBlocked
        End Select
    End Function

    Private Shared Function ResolveLocation(current As Uri, location As Uri) As Uri
        If location Is Nothing Then Return Nothing
        If location.IsAbsoluteUri Then Return location
        Dim target As Uri = Nothing
        If Uri.TryCreate(current, location, target) Then Return target
        Return Nothing
    End Function

    Private Shared Function BuildRequest(method As String, url As Uri, context As HtmlRequestContext) As HttpRequestMessage
        Dim message As New HttpRequestMessage(If(method = "HEAD", HttpMethod.Head, HttpMethod.Get), url)
        ' HTTP/2 only where ALPN can offer it; never HTTP/3, which would bypass ConnectCallback.
        message.Version = If(String.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal), HttpVersion.Version20, HttpVersion.Version11)
        message.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        message.Headers.TryAddWithoutValidation("Accept", AcceptFor(context))
        message.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br")
        message.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5")
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent)
        Return message
    End Function

    Private Shared Function AcceptFor(context As HtmlRequestContext) As String
        Select Case context
            Case HtmlRequestContext.Document : Return "text/html,application/xhtml+xml;q=0.9"
            Case HtmlRequestContext.Stylesheet : Return "text/css,*/*;q=0.1"
            Case HtmlRequestContext.Image : Return "image/avif,image/webp,image/png,image/jpeg,image/gif,image/svg+xml,image/*;q=0.8"
            Case HtmlRequestContext.Font : Return "font/woff2,font/woff,font/ttf,font/otf;q=0.9,*/*;q=0.1"
            Case Else : Return "*/*;q=0.1"
        End Select
    End Function

    Friend Shared Function LimitFor(context As HtmlRequestContext) As Long
        Select Case context
            Case HtmlRequestContext.Document : Return DocumentLimit
            Case HtmlRequestContext.Stylesheet : Return StylesheetLimit
            Case HtmlRequestContext.Image : Return ImageLimit
            Case HtmlRequestContext.Font : Return FontLimit
            Case Else : Return 0L
        End Select
    End Function

    Friend Shared Function IsMimeAllowed(context As HtmlRequestContext, mime As String, url As Uri) As Boolean
        If String.IsNullOrEmpty(mime) Then Return False
        Select Case context
            Case HtmlRequestContext.Document
                Return mime = "text/html" OrElse mime = "application/xhtml+xml"
            Case HtmlRequestContext.Stylesheet
                Return mime = "text/css"
            Case HtmlRequestContext.Image
                Return ImageTypes.Contains(mime)
            Case HtmlRequestContext.Font
                If mime.StartsWith("font/", StringComparison.Ordinal) OrElse FontTypes.Contains(mime) Then Return True
                If mime = "application/octet-stream" AndAlso url IsNot Nothing Then
                    Dim path As String = url.AbsolutePath.ToLowerInvariant()
                    Return path.EndsWith(".woff", StringComparison.Ordinal) OrElse path.EndsWith(".woff2", StringComparison.Ordinal) OrElse
                           path.EndsWith(".ttf", StringComparison.Ordinal) OrElse path.EndsWith(".otf", StringComparison.Ordinal)
                End If
                Return False
            Case Else
                Return False
        End Select
    End Function

    Private Async Function ReadSuccessAsync(response As HttpResponseMessage, context As HtmlRequestContext, url As Uri, method As String,
                                            budget As HtmlFetchBudget, result As HtmlFetchResult, token As CancellationToken) As Task
        Dim mime As String = ""
        Dim charset As String = ""
        Dim contentType As System.Net.Http.Headers.MediaTypeHeaderValue = Nothing
        Try
            contentType = response.Content.Headers.ContentType
        Catch ex As FormatException
            contentType = Nothing
        End Try
        If contentType IsNot Nothing Then
            mime = If(contentType.MediaType, "").Trim().ToLowerInvariant()
            charset = If(contentType.CharSet, "").Trim().Trim(""""c)
        End If
        result.MimeType = mime
        result.Charset = charset
        result.ContentSecurityPolicy = HeaderText(response, "Content-Security-Policy")
        If Not IsMimeAllowed(context, mime, url) Then
            result.Status = HtmlOutcome.UnsupportedResource
            Return
        End If

        Dim limit As Long = LimitFor(context)
        Dim declared As Long? = response.Content.Headers.ContentLength
        If declared.HasValue AndAlso declared.Value > limit Then Throw New RefusedException(HtmlOutcome.LimitExceeded)
        If method = "HEAD" Then
            result.Status = HtmlOutcome.Ok
            Return
        End If

        Dim encoding As String = ContentEncodingOf(response)
        Using body As Stream = Await response.Content.ReadAsStreamAsync(token).ConfigureAwait(False)
            Using raw As MemoryStream = Await ReadBoundedAsync(body, limit, If(encoding.Length = 0, budget, Nothing), token).ConfigureAwait(False)
                If encoding.Length = 0 Then
                    result.Bytes = raw.ToArray()
                Else
                    result.Bytes = Await InflateAsync(raw, encoding, limit, budget, token).ConfigureAwait(False)
                End If
            End Using
        End Using
        result.Status = HtmlOutcome.Ok
    End Function

    Private Shared Function HeaderText(response As HttpResponseMessage, name As String) As String
        Dim values As IEnumerable(Of String) = Nothing
        If response.Headers.TryGetValues(name, values) Then Return String.Join(", ", values)
        Return ""
    End Function

    ' "" for identity/none, one lowercase coding otherwise; stacked or unknown codings are refused.
    Private Shared Function ContentEncodingOf(response As HttpResponseMessage) As String
        Dim names As New List(Of String)()
        For Each value As String In response.Content.Headers.ContentEncoding
            For Each part As String In value.Split(","c)
                Dim coding As String = part.Trim().ToLowerInvariant()
                If coding.Length > 0 AndAlso coding <> "identity" Then names.Add(coding)
            Next
        Next
        If names.Count = 0 Then Return ""
        If names.Count > 1 Then Throw New RefusedException(HtmlOutcome.UnsupportedResource)
        Select Case names(0)
            Case "gzip", "x-gzip" : Return "gzip"
            Case "deflate" : Return "deflate"
            Case "br" : Return "br"
            Case Else : Throw New RefusedException(HtmlOutcome.UnsupportedResource)
        End Select
    End Function

    ' Reads until EOF or until the limit is crossed, whatever Content-Length claims. A non-Nothing
    ' budget is charged for every byte (identity bodies); compressed bodies are charged once expanded.
    Private Shared Async Function ReadBoundedAsync(source As Stream, limit As Long, budget As HtmlFetchBudget, token As CancellationToken) As Task(Of MemoryStream)
        Dim output As New MemoryStream()
        Dim succeeded As Boolean = False
        Try
            Dim buffer(65535) As Byte
            Dim total As Long = 0L
            Do
                Dim read As Integer = Await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(False)
                If read <= 0 Then Exit Do
                total += read
                If total > limit Then Throw New RefusedException(HtmlOutcome.LimitExceeded)
                If budget IsNot Nothing AndAlso Not budget.TryConsume(read) Then Throw New RefusedException(HtmlOutcome.LimitExceeded)
                output.Write(buffer, 0, read)
            Loop
            output.Position = 0L
            succeeded = True
            Return output
        Finally
            If Not succeeded Then output.Dispose()
        End Try
    End Function

    Private Shared Async Function InflateAsync(raw As MemoryStream, encoding As String, limit As Long, budget As HtmlFetchBudget, token As CancellationToken) As Task(Of Byte())
        Using inflater As Stream = OpenDecoder(raw, encoding)
            Using expanded As MemoryStream = Await ReadBoundedAsync(inflater, limit, budget, token).ConfigureAwait(False)
                Return expanded.ToArray()
            End Using
        End Using
    End Function

    Private Shared Function OpenDecoder(raw As MemoryStream, encoding As String) As Stream
        Select Case encoding
            Case "gzip"
                Return New GZipStream(raw, CompressionMode.Decompress, leaveOpen:=True)
            Case "br"
                Return New BrotliStream(raw, CompressionMode.Decompress, leaveOpen:=True)
            Case Else
                ' HTTP "deflate" is zlib-wrapped, but some servers send the bare stream.
                Dim b0 As Integer = raw.ReadByte()
                Dim b1 As Integer = raw.ReadByte()
                raw.Position = 0L
                Dim zlib As Boolean = b0 >= 0 AndAlso b1 >= 0 AndAlso (b0 And &HF) = 8 AndAlso ((b0 << 8) Or b1) Mod 31 = 0
                If zlib Then Return New ZLibStream(raw, CompressionMode.Decompress, leaveOpen:=True)
                Return New DeflateStream(raw, CompressionMode.Decompress, leaveOpen:=True)
        End Select
    End Function

    ' ---- connecting ----

    Private Function OnConnect(context As SocketsHttpConnectionContext, token As CancellationToken) As ValueTask(Of Stream)
        Return New ValueTask(Of Stream)(ConnectValidatedAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, token))
    End Function

    ' Resolves for THIS connection, refuses the whole answer unless every address is public, and
    ' connects only to those validated addresses. This is the one DNS lookup of the connection.
    Private Async Function ConnectValidatedAsync(host As String, port As Integer, token As CancellationToken) As Task(Of Stream)
        If port <> 80 AndAlso port <> 443 Then Throw New RefusedException(HtmlOutcome.NetworkBlocked)
        Dim answer As IPAddress() = Nothing
        Dim literal As IPAddress = Nothing
        If IPAddress.TryParse(If(host, "").Trim("["c, "]"c), literal) Then
            answer = New IPAddress() {literal}
        Else
            answer = Await _resolver.ResolveAsync(host, token).ConfigureAwait(False)
        End If
        Dim candidates As New List(Of IPAddress)()
        If answer IsNot Nothing Then
            For Each address As IPAddress In answer
                candidates.Add(HtmlAddressPolicy.Normalize(address))
            Next
        End If
        If Not HtmlAddressPolicy.AllPublic(candidates) Then Throw New RefusedException(HtmlOutcome.NetworkBlocked)

        Dim ordered As List(Of IPAddress) = candidates.Distinct().Take(8).ToList()
        Dim lastError As Exception = Nothing
        For index As Integer = 0 To ordered.Count - 1
            token.ThrowIfCancellationRequested()
            Dim endpoint As New IPEndPoint(ordered(index), port)
            Using attempt As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token)
                If index < ordered.Count - 1 Then attempt.CancelAfter(AttemptTimeout)
                Try
                    If _connectOverride IsNot Nothing Then Return Await _connectOverride(endpoint, attempt.Token).ConfigureAwait(False)
                    Return Await ConnectSocketAsync(endpoint, attempt.Token).ConfigureAwait(False)
                Catch ex As Exception When Not token.IsCancellationRequested
                    lastError = ex
                End Try
            End Using
        Next
        If lastError IsNot Nothing Then Throw lastError
        Throw New RefusedException(HtmlOutcome.NetworkBlocked)
    End Function

    Private Shared Async Function ConnectSocketAsync(endpoint As IPEndPoint, token As CancellationToken) As Task(Of Stream)
        Dim socket As New Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) With {.NoDelay = True}
        Try
            Await socket.ConnectAsync(endpoint, token).ConfigureAwait(False)
            Return New NetworkStream(socket, ownsSocket:=True)
        Catch
            socket.Dispose()
            Throw
        End Try
    End Function

    ' ---- failure mapping ----

    Private Function MapFailure(ex As Exception, callerToken As CancellationToken, linked As CancellationTokenSource) As HtmlOutcome
        If callerToken.IsCancellationRequested Then Return HtmlOutcome.Cancelled
        Dim sawTimeout As Boolean = False
        Dim node As Exception = ex
        Dim depth As Integer = 0
        While node IsNot Nothing AndAlso depth < 8
            Dim refused As RefusedException = TryCast(node, RefusedException)
            If refused IsNot Nothing Then Return refused.Outcome
            If TypeOf node Is AuthenticationException Then Return HtmlOutcome.CertificateRejected
            If TypeOf node Is TimeoutException OrElse TypeOf node Is OperationCanceledException Then sawTimeout = True
            node = node.InnerException
            depth += 1
        End While
        If Volatile.Read(_disposed) <> 0 Then Return HtmlOutcome.Cancelled
        If sawTimeout OrElse linked.IsCancellationRequested Then Return HtmlOutcome.TimedOut
        Return HtmlOutcome.NetworkBlocked
    End Function

    ' A typed refusal raised below the fetch loop (connect policy, body limits, content coding).
    Private NotInheritable Class RefusedException
        Inherits Exception

        Private ReadOnly _outcome As HtmlOutcome

        Public Sub New(outcome As HtmlOutcome)
            MyBase.New("Fetch refused: " & outcome.ToString())
            _outcome = outcome
        End Sub

        Public ReadOnly Property Outcome As HtmlOutcome
            Get
                Return _outcome
            End Get
        End Property
    End Class
End Class
#End If
