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
                        ' We only M-SEARCHed IGD targets, so any answer is a gateway.
                        Dim loc As String = ExtractHeader(resp, "LOCATION")
                        If Not String.IsNullOrEmpty(loc) Then Return loc
                    Catch
                        Exit Do ' receive timeout / socket closed
                    End Try
                Loop
            End Using
        Catch
        End Try
        Return ""
    End Function

    Private Function ParseDeviceXml(xml As String) As RouterIdentity
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

        Try
            Dim req As HttpWebRequest = CType(WebRequest.Create("http://" & ip & "/"), HttpWebRequest)
            req.Method = "GET"
            req.Timeout = 1200
            req.ReadWriteTimeout = 1200
            req.AllowAutoRedirect = True
            req.UserAgent = "FastMediaSorter"
            Dim resp As HttpWebResponse = Nothing
            Try
                resp = CType(req.GetResponse(), HttpWebResponse)
            Catch wex As WebException
                resp = TryCast(wex.Response, HttpWebResponse) ' 401 pages still carry headers
            End Try
            If resp IsNot Nothing Then
                Using resp
                    scanText.Append(" ").Append(If(resp.Headers("Server"), ""))
                    scanText.Append(" ").Append(If(resp.Headers("WWW-Authenticate"), ""))
                    Try
                        Using sr As New IO.StreamReader(resp.GetResponseStream(), Encoding.UTF8)
                            Dim buf(4095) As Char
                            Dim n As Integer = sr.Read(buf, 0, buf.Length)
                            If n > 0 Then
                                Dim body As New String(buf, 0, n)
                                Dim tm As Match = Regex.Match(body, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
                                If tm.Success Then scanText.Append(" ").Append(tm.Groups(1).Value)
                            End If
                        End Using
                    Catch
                    End Try
                End Using
            End If
        Catch
        End Try

        id.Vendor = MatchVendor(scanText.ToString())
        Return id
    End Function

    ' --- helpers ----------------------------------------------------------------

    Private Function HttpGet(url As String, timeoutMs As Integer) As String
        Try
            Dim req As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
            req.Method = "GET"
            req.Timeout = timeoutMs
            req.ReadWriteTimeout = timeoutMs
            req.UserAgent = "FastMediaSorter"
            Using resp As HttpWebResponse = CType(req.GetResponse(), HttpWebResponse)
                Using sr As New IO.StreamReader(resp.GetResponseStream(), Encoding.UTF8)
                    Return sr.ReadToEnd()
                End Using
            End Using
        Catch
            Return ""
        End Try
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

    Private Function CleanText(raw As String) As String
        If String.IsNullOrEmpty(raw) Then Return ""
        Dim s As String = raw
        Try
            s = Regex.Replace(s, "<[^>]+>", " ")           ' strip stray tags
            s = System.Net.WebUtility.HtmlDecode(s)
        Catch
        End Try
        s = Regex.Replace(s, "\s+", " ").Trim()
        If s.Length > 60 Then s = s.Substring(0, 60).Trim()
        Return s
    End Function

End Module
