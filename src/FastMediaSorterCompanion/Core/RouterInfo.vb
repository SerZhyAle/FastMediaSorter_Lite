Option Strict On

Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>One router's identity, best-effort.</summary>
Public NotInheritable Class RouterIdentity
    Public Property Vendor As String = ""
    Public Property Model As String = ""
    Public Property FriendlyName As String = ""
    Public Property Source As String = ""   ' "upnp" | "http" | ""

    Public Function IsKnown() As Boolean
        Return Vendor.Length > 0 OrElse Model.Length > 0
    End Function

    ''' <summary>"Vendor Model" for display / search, de-duplicated.</summary>
    Public Function DisplayName() As String
        Dim parts As New List(Of String)
        If Vendor.Length > 0 Then parts.Add(Vendor)
        If Model.Length > 0 AndAlso Model.IndexOf(Vendor, StringComparison.OrdinalIgnoreCase) < 0 Then parts.Add(Model)
        If parts.Count = 0 AndAlso FriendlyName.Length > 0 Then parts.Add(FriendlyName)
        Return String.Join(" ", parts).Trim()
    End Function
End Class

''' <summary>
''' Best-effort detection of the home router's make/model, so we can send the user
''' to a model-specific "how to port forward" search. Primary source: the router's
''' UPnP IGD device-description XML (manufacturer + modelName) - reliable, and UPnP
''' is already the app's port-mapping path. Fallback: the gateway's HTTP response
''' headers (Server, WWW-Authenticate realm) and page &lt;title&gt;. All network work
''' is short-timeout and swallowed - detection never blocks or throws.
''' </summary>
Public Module RouterInfo

    ' Canonical vendor names matched (case-insensitively) in UPnP XML / HTTP text.
    Private ReadOnly VendorKeywords As String() = {
        "TP-Link", "TP-LINK", "Mercusys", "ASUS", "Keenetic", "NETGEAR", "D-Link", "DLink",
        "Huawei", "Xiaomi", "Redmi", "ZTE", "Zyxel", "Tenda", "MikroTik", "Ubiquiti", "UniFi",
        "Netis", "Sagemcom", "Technicolor", "Sercomm", "Arris", "Linksys", "TotoLink",
        "Ruijie", "Cisco", "AVM", "FRITZ", "Sagem", "Eltex", "Rostelecom", "Mikrotik"}

    ''' <summary>Detects the router. Blocking (~up to ~2.5s worst case) - call via
    ''' Task.Run. Returns an empty identity when nothing could be determined.</summary>
    Public Function Detect() As RouterIdentity
        Dim viaUpnp As RouterIdentity = TryDetectViaUpnp()
        If viaUpnp IsNot Nothing AndAlso viaUpnp.IsKnown() Then Return viaUpnp
        Dim viaHttp As RouterIdentity = TryDetectViaHttp()
        If viaHttp IsNot Nothing AndAlso viaHttp.IsKnown() Then Return viaHttp
        Return If(viaUpnp, If(viaHttp, New RouterIdentity()))
    End Function

    ''' <summary>Google "how to port forward &lt;model&gt;" URL (generic when unknown).</summary>
    Public Function SearchUrl(id As RouterIdentity) As String
        Dim name As String = If(id IsNot Nothing, id.DisplayName(), "")
        Dim q As String = If(name.Length > 0, "how to port forward " & name,
                                              "how to set up port forwarding on my router")
        Return "https://www.google.com/search?q=" & Uri.EscapeDataString(q)
    End Function

    ' --- UPnP (SSDP -> device description XML) ----------------------------------

    Private Function TryDetectViaUpnp() As RouterIdentity
        Try
            Dim location As String = SsdpDiscoverIgdLocation()
            If String.IsNullOrEmpty(location) Then Return Nothing
            Dim xml As String = HttpGet(location, 1500)
            If String.IsNullOrEmpty(xml) Then Return Nothing
            ' Final guard: the description we fetched must actually be an Internet
            ' Gateway Device. A misbehaving media box (e.g. a Vestel smart-TV) that
            ' echoed our IGD search target in its SSDP reply still serves a
            ' MediaRenderer/MediaServer XML - reject it so its manufacturer is never
            ' mistaken for the router.
            If xml.IndexOf("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase) < 0 Then Return Nothing
            Dim id As RouterIdentity = ParseDeviceXml(xml)
            If id IsNot Nothing AndAlso id.IsKnown() Then id.Source = "upnp"
            Return id
        Catch
            Return Nothing
        End Try
    End Function

    Private Function SsdpDiscoverIgdLocation() As String
        Dim targets As String() = {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1"}
        ' The router's IGD description is served from the default gateway itself, so
        ' the responder whose LOCATION host == the gateway IP is authoritatively the
        ' router. A chatty smart-TV / DLNA box (e.g. Vestel) answers every M-SEARCH
        ' with its own root device and would otherwise win the race - so we prefer
        ' the gateway, and only ever fall back to a responder that actually claims to
        ' be an InternetGatewayDevice (ST/USN), never just "the first thing to reply".
        Dim gatewayIp As String = NetworkInfo.DefaultGatewayIp()
        Dim igdFallback As String = ""
        Try
            Dim localIp As String = NetworkInfo.LocalIPv4()
            Dim bind As IPEndPoint = If(String.IsNullOrEmpty(localIp), New IPEndPoint(IPAddress.Any, 0), New IPEndPoint(IPAddress.Parse(localIp), 0))
            Using udp As New UdpClient(bind)
                udp.Client.ReceiveTimeout = 1500
                Dim mcast As New IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900)
                For Each st As String In targets
                    Dim req As String =
                        "M-SEARCH * HTTP/1.1" & vbCrLf &
                        "HOST: 239.255.255.250:1900" & vbCrLf &
                        "MAN: ""ssdp:discover""" & vbCrLf &
                        "MX: 1" & vbCrLf &
                        "ST: " & st & vbCrLf & vbCrLf
                    Dim b As Byte() = Encoding.ASCII.GetBytes(req)
                    udp.Send(b, b.Length, mcast)
                Next

                Dim sw As Stopwatch = Stopwatch.StartNew()
                Do While sw.ElapsedMilliseconds < 1500
                    Try
                        Dim from As IPEndPoint = Nothing
                        Dim data As Byte() = udp.Receive(from)
                        Dim resp As String = Encoding.ASCII.GetString(data)
                        Dim loc As String = ExtractHeader(resp, "LOCATION")
                        If String.IsNullOrEmpty(loc) Then Continue Do
                        ' Responder IS the gateway -> it is the router, take it now.
                        If gatewayIp.Length > 0 AndAlso String.Equals(UriHost(loc), gatewayIp, StringComparison.OrdinalIgnoreCase) Then
                            Return loc
                        End If
                        ' Otherwise keep it only if it self-identifies as an IGD.
                        If igdFallback.Length = 0 Then
                            Dim tag As String = ExtractHeader(resp, "ST") & " " & ExtractHeader(resp, "USN")
                            If tag.IndexOf("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase) >= 0 Then igdFallback = loc
                        End If
                    Catch
                        Exit Do ' receive timeout / socket closed
                    End Try
                Loop
            End Using
        Catch
        End Try
        Return igdFallback
    End Function

    ''' <summary>Host of a URL (IP or name), or "" if it will not parse.</summary>
    Private Function UriHost(url As String) As String
        Try
            Return New Uri(url).Host
        Catch
            Return ""
        End Try
    End Function

    Friend Function ParseDeviceXml(xml As String) As RouterIdentity
        Dim id As New RouterIdentity()
        id.Vendor = CleanVendor(FirstTag(xml, "manufacturer"))
        id.Model = CleanText(FirstTag(xml, "modelName"))
        Dim number As String = CleanText(FirstTag(xml, "modelNumber"))
        If number.Length > 0 AndAlso id.Model.IndexOf(number, StringComparison.OrdinalIgnoreCase) < 0 Then
            id.Model = (id.Model & " " & number).Trim()
        End If
        id.FriendlyName = CleanText(FirstTag(xml, "friendlyName"))
        ' Some ISPs put a bland "manufacturer" (e.g. the chipset vendor) - if the
        ' friendlyName carries a known brand, prefer it as the vendor.
        If id.Vendor.Length = 0 Then id.Vendor = MatchVendor(id.FriendlyName & " " & id.Model)
        Return id
    End Function

    Private Function FirstTag(xml As String, tag As String) As String
        Try
            Dim m As Match = Regex.Match(xml, "<" & tag & "[^>]*>(.*?)</" & tag & ">",
                                         RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            If m.Success Then Return m.Groups(1).Value
        Catch
        End Try
        Return ""
    End Function

    ' --- HTTP fallback (gateway page) -------------------------------------------

    Private Function TryDetectViaHttp() As RouterIdentity
        Dim ip As String = NetworkInfo.DefaultGatewayIp()
        If ip.Length = 0 Then Return Nothing
        Dim id As New RouterIdentity() With {.Source = "http"}
        Dim scanText As New StringBuilder()

        ' A 401 page still carries the headers - the fetch keeps any status.
        Dim page As FetchResult = Fetch("http://" & ip & "/", 1200)
        If page IsNot Nothing Then
            scanText.Append(" ").Append(page.Server)
            scanText.Append(" ").Append(page.Authenticate)
            Dim tm As Match = Regex.Match(page.Body, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            If tm.Success Then scanText.Append(" ").Append(tm.Groups(1).Value)
        End If

        id.Vendor = MatchVendor(scanText.ToString())
        Return id
    End Function

    ' --- helpers ----------------------------------------------------------------

    ''' <summary>
    ''' Everything read from a LAN device is read on its terms: whatever answers the SSDP
    ''' search, or sits at the gateway address, decides what comes back (ticket 054 H-17). So
    ''' a read is capped, the whole request has one deadline (a per-read timeout let an
    ''' endless trickle pin a thread and its memory for as long as the device liked), and no
    ''' redirect is followed - one would turn opening a window into a request to any host the
    ''' device names, which the privacy text never mentions.
    ''' </summary>
    Friend Const Max_Body_Bytes As Integer = 64 * 1024

    Private NotInheritable Class FetchResult
        Public Server As String = ""
        Public Authenticate As String = ""
        Public Body As String = ""
    End Class

    Private Function HttpGet(url As String, timeoutMs As Integer) As String
        Dim r As FetchResult = Fetch(url, timeoutMs)
        Return If(r Is Nothing, "", r.Body)
    End Function

    Private Function Fetch(url As String, timeoutMs As Integer) As FetchResult
        Try
            Return FetchAsync(url, timeoutMs).GetAwaiter().GetResult()
        Catch
            Return Nothing
        End Try
    End Function

    Private Async Function FetchAsync(url As String, timeoutMs As Integer) As Task(Of FetchResult)
        Using handler As New Http.SocketsHttpHandler With {
                .AllowAutoRedirect = False,
                .UseProxy = False,
                .ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs)}
            Using client As New Http.HttpClient(handler) With {.Timeout = System.Threading.Timeout.InfiniteTimeSpan}
                Using deadline As New System.Threading.CancellationTokenSource(timeoutMs)
                    Using req As New Http.HttpRequestMessage(Http.HttpMethod.Get, url)
                        req.Headers.TryAddWithoutValidation("User-Agent", "FastMediaSorter")
                        Using resp As Http.HttpResponseMessage = Await client.SendAsync(req, Http.HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(False)
                            Dim result As New FetchResult()
                            Dim values As IEnumerable(Of String) = Nothing
                            If resp.Headers.TryGetValues("Server", values) Then result.Server = String.Join(" ", values)
                            If resp.Headers.TryGetValues("WWW-Authenticate", values) Then result.Authenticate = String.Join(" ", values)
                            Using body As IO.Stream = Await resp.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(False)
                                Using ms As New IO.MemoryStream()
                                    Dim buf(8191) As Byte
                                    Do While ms.Length < Max_Body_Bytes
                                        Dim want As Integer = CInt(Math.Min(buf.Length, Max_Body_Bytes - ms.Length))
                                        Dim n As Integer = Await body.ReadAsync(buf, 0, want, deadline.Token).ConfigureAwait(False)
                                        If n <= 0 Then Exit Do
                                        ms.Write(buf, 0, n)
                                    Loop
                                    result.Body = Encoding.UTF8.GetString(ms.ToArray())
                                End Using
                            End Using
                            Return result
                        End Using
                    End Using
                End Using
            End Using
        End Using
    End Function

    Private Function ExtractHeader(httpResponse As String, header As String) As String
        Try
            For Each line As String In httpResponse.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)
                Dim idx As Integer = line.IndexOf(":"c)
                If idx > 0 AndAlso String.Equals(line.Substring(0, idx).Trim(), header, StringComparison.OrdinalIgnoreCase) Then
                    Return line.Substring(idx + 1).Trim()
                End If
            Next
        Catch
        End Try
        Return ""
    End Function

    Private Function MatchVendor(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        For Each kw As String In VendorKeywords
            If text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 Then Return CanonicalVendor(kw)
        Next
        Return ""
    End Function

    Private Function CleanVendor(raw As String) As String
        Dim v As String = CleanText(raw)
        Dim canon As String = MatchVendor(v)
        Return If(canon.Length > 0, canon, v)
    End Function

    Private Function CanonicalVendor(kw As String) As String
        Select Case kw.ToUpperInvariant()
            Case "TP-LINK" : Return "TP-Link"
            Case "DLINK" : Return "D-Link"
            Case "FRITZ", "AVM" : Return "AVM FRITZ!Box"
            Case "UNIFI", "UBIQUITI" : Return "Ubiquiti"
            Case "REDMI", "XIAOMI" : Return "Xiaomi"
            Case "MIKROTIK" : Return "MikroTik"
            Case Else : Return kw
        End Select
    End Function

    ''' <summary>A device-supplied name, made plain text. Decoded FIRST and stripped after:
    ''' the other way round, "&amp;lt;img onerror=..&amp;gt;" survived the tag strip and was
    ''' decoded into live markup (ticket 054 H-17). Then only printable characters are kept,
    ''' minus the few that mean something to HTML or a shell.</summary>
    Friend Function CleanText(raw As String) As String
        If String.IsNullOrEmpty(raw) Then Return ""
        Dim s As String = raw
        Try
            s = System.Net.WebUtility.HtmlDecode(s)
            s = Regex.Replace(s, "<[^>]*>", " ")           ' strip tags, including decoded ones
        Catch
        End Try
        Dim kept As New StringBuilder(s.Length)
        For Each c As Char In s
            If Char.IsControl(c) Then
                kept.Append(" "c)
            ElseIf "<>""'`&".IndexOf(c) < 0 Then
                kept.Append(c)
            End If
        Next
        s = Regex.Replace(kept.ToString(), "\s+", " ").Trim()
        If s.Length > 60 Then s = s.Substring(0, 60).Trim()
        Return s
    End Function

End Module
