Option Strict On

Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets

''' <summary>
''' Small local-network helpers for the Share feature: the default-gateway
''' (router admin) address to send the user to for manual port forwarding, a LAN
''' IPv4 fallback (the worker already reports lanAddress, which is authoritative),
''' and an elevation-safe browser opener. No external services are contacted here -
''' public-IP discovery is the worker's job (it uses IP-echo endpoints).
''' </summary>
Public Module NetworkInfo

    ''' <summary>Default-gateway IPv4 (the router's admin IP, e.g. "192.168.1.1"),
    ''' or "" if none is found. Only considers adapters that are Up, not
    ''' loopback/tunnel, carry a real (non 0.0.0.0) IPv4 gateway AND also have a
    ''' bound IPv4 - that combination is the real LAN adapter, not a VPN/virtual
    ''' bridge advertising a phantom gateway.</summary>
    Public Function DefaultGatewayIp() As String
        Dim fallback As String = ""
        Try
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus <> OperationalStatus.Up Then Continue For
                Select Case nic.NetworkInterfaceType
                    Case NetworkInterfaceType.Loopback, NetworkInterfaceType.Tunnel
                        Continue For
                End Select

                Dim props As IPInterfaceProperties = nic.GetIPProperties()
                If props Is Nothing Then Continue For

                Dim gwText As String = ""
                For Each gw As GatewayIPAddressInformation In props.GatewayAddresses
                    If gw Is Nothing OrElse gw.Address Is Nothing Then Continue For
                    If gw.Address.AddressFamily <> AddressFamily.InterNetwork Then Continue For
                    Dim s As String = gw.Address.ToString()
                    If String.IsNullOrEmpty(s) OrElse s = "0.0.0.0" Then Continue For
                    gwText = s
                    Exit For
                Next
                If gwText.Length = 0 Then Continue For

                ' Prefer a NIC that also owns a bound IPv4 (the true LAN interface).
                For Each ua As UnicastIPAddressInformation In props.UnicastAddresses
                    If ua.Address IsNot Nothing AndAlso ua.Address.AddressFamily = AddressFamily.InterNetwork Then
                        Return gwText
                    End If
                Next
                If fallback.Length = 0 Then fallback = gwText
            Next
        Catch
        End Try
        Return fallback
    End Function

    ''' <summary>Router admin URL ("http://&lt;gateway&gt;/"), or "" if unknown.</summary>
    Public Function DefaultGatewayUrl() As String
        Dim ip As String = DefaultGatewayIp()
        If ip.Length = 0 Then Return ""
        Return "http://" & ip & "/"
    End Function

    ''' <summary>Best-guess LAN IPv4 of this PC. Only a fallback for display - the
    ''' worker's reachability.lanAddress is authoritative, and uses the identical
    ''' outbound-route trick (Go's preferredOutboundIP), so it fails for the same
    ''' reason at the same time. Tries the outbound-route trick first (sends no
    ''' packets - the UDP "connect" to TEST-NET-1 only resolves the outbound
    ''' interface); if the OS has no default route to resolve (e.g. a VPN or a
    ''' broken/missing gateway lease blackholes it, even though the real LAN
    ''' adapter and its router are otherwise fine), falls back to enumerating
    ''' adapters directly. Confirmed root cause of a real pairing failure
    ''' (2026-07): the portforward path worked via LAN-only UPnP - which only
    ''' needs the router on the local subnet, not a default route - while this
    ''' trick and the worker's copy of it both came back empty, so the exported
    ''' config had no LAN entry at all and the phone was left dialing a dead
    ''' external port forever (Android trusts accessPaths[0] with no fallback).</summary>
    Public Function LocalIPv4() As String
        Dim viaRoute As String = LocalIPv4ViaOutboundRoute()
        ' A VPN/tunnel adapter that wins the default route makes the outbound-
        ' route trick succeed with ITS address, not the real LAN's - the scored
        ' adapter scan below already knows to exclude such adapters, so also
        ' apply that check here instead of trusting the trick's result verbatim.
        ' Mirrors the identical fix in the Go worker's netaccess/localip.go.
        If viaRoute.Length > 0 AndAlso Not IsVirtualAdapterAddress(viaRoute) Then Return viaRoute
        Dim viaScan As String = LocalIPv4ViaAdapterScan()
        If viaScan.Length > 0 Then Return viaScan
        ' Last resort: even a VPN/tunnel address beats nothing when no real LAN
        ' NIC exists at all.
        Return viaRoute
    End Function

    ''' <summary>True if <paramref name="ipText"/> is bound to an adapter whose
    ''' name matches a known virtual/VPN pattern (see <see cref="VirtualNicHints"/>).</summary>
    Private Function IsVirtualAdapterAddress(ipText As String) As Boolean
        Try
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                Dim props As IPInterfaceProperties = nic.GetIPProperties()
                If props Is Nothing Then Continue For
                For Each ua As UnicastIPAddressInformation In props.UnicastAddresses
                    If ua.Address IsNot Nothing AndAlso ua.Address.ToString() = ipText Then
                        Return LooksVirtualNic(nic)
                    End If
                Next
            Next
        Catch
        End Try
        Return False
    End Function

    Private Function LocalIPv4ViaOutboundRoute() As String
        Try
            Using u As New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                u.Connect("192.0.2.1", 9) ' TEST-NET-1 (RFC 5737): never routed
                Dim ep As IPEndPoint = TryCast(u.LocalEndPoint, IPEndPoint)
                If ep IsNot Nothing AndAlso ep.Address IsNot Nothing Then Return ep.Address.ToString()
            End Using
        Catch
        End Try
        Return ""
    End Function

    ''' <summary>Enumerates adapters and returns the best LAN IPv4 when the
    ''' outbound-route trick found nothing. Unlike the earlier scan it does NOT
    ''' require a detectable IPv4 gateway: a real Wi-Fi/Ethernet NIC whose default
    ''' gateway only surfaced over IPv6 (a dual-stack DHCP state seen in the field,
    ''' 2026-07) still owns a perfectly reachable private IPv4, and dropping it left
    ''' the exported .fmscfg with no "lan" access path at all - so the phone had
    ''' only the WAN entry and could not reach the PC on the home Wi-Fi (S1006).
    ''' Every candidate is scored so a genuinely present LAN adapter is never
    ''' silently dropped, yet a virtual/VPN adapter can never outrank it: owning an
    ''' IPv4 gateway is the strongest "true LAN interface" signal, a private
    ''' (RFC1918) address and a physical NIC type raise the score, and a
    ''' virtual/VPN adapter (name/description match) lowers it - so a VMware /
    ''' Hyper-V / VirtualBox host-only 192.168.x never wins over the real LAN.
    ''' APIPA (169.254) and loopback addresses are never returned, and a public
    ''' address is only taken when its NIC actually has an IPv4 gateway (a rare
    ''' no-NAT PC), never a stray public IP bound to some virtual adapter.</summary>
    Private Function LocalIPv4ViaAdapterScan() As String
        Dim best As String = ""
        Dim bestScore As Integer = Integer.MinValue
        Try
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus <> OperationalStatus.Up Then Continue For
                Select Case nic.NetworkInterfaceType
                    Case NetworkInterfaceType.Loopback, NetworkInterfaceType.Tunnel
                        Continue For
                End Select

                Dim props As IPInterfaceProperties = nic.GetIPProperties()
                If props Is Nothing Then Continue For

                Dim hasIpv4Gateway As Boolean = NicHasIpv4Gateway(props)
                Dim physical As Boolean = IsPhysicalNic(nic)
                Dim virtualNic As Boolean = LooksVirtualNic(nic)

                For Each ua As UnicastIPAddressInformation In props.UnicastAddresses
                    Dim addr As IPAddress = If(ua Is Nothing, Nothing, ua.Address)
                    If addr Is Nothing OrElse addr.AddressFamily <> AddressFamily.InterNetwork Then Continue For
                    If IsApipaOrLoopback(addr) Then Continue For
                    Dim priv As Boolean = IsPrivateIPv4(addr)
                    ' Only ever hand back a private IPv4, or a public one on a NIC
                    ' that has a real IPv4 gateway (a no-NAT box) - never a stray
                    ' public address parked on a virtual adapter.
                    If Not priv AndAlso Not hasIpv4Gateway Then Continue For

                    Dim score As Integer = 0
                    If hasIpv4Gateway Then score += 1000
                    If priv Then score += 400
                    If physical Then score += 200
                    If virtualNic Then score -= 800
                    ' Tie-break among gateway-less candidates (the dual-stack field
                    ' bug: the real LAN NIC's IPv4 gateway wasn't visible) toward a
                    ' typical home/corp subnet. Hyper-V's Default Switch, WSL and
                    ' Docker NAT adapters cluster in 172.16/12 with no gateway, while
                    ' real LANs are overwhelmingly 192.168/16 or 10/8 - nudge those up
                    ' without ever overriding the gateway signal above.
                    score += HomeLanBonus(addr)

                    If score > bestScore Then
                        bestScore = score
                        best = addr.ToString()
                    End If
                Next
            Next
        Catch
        End Try
        Return best
    End Function

    ''' <summary>True if the adapter carries a real (non 0.0.0.0) IPv4 default
    ''' gateway - the strongest signal it is the true LAN interface.</summary>
    Private Function NicHasIpv4Gateway(props As IPInterfaceProperties) As Boolean
        For Each gw As GatewayIPAddressInformation In props.GatewayAddresses
            If gw Is Nothing OrElse gw.Address Is Nothing Then Continue For
            If gw.Address.AddressFamily <> AddressFamily.InterNetwork Then Continue For
            Dim s As String = gw.Address.ToString()
            If String.IsNullOrEmpty(s) OrElse s = "0.0.0.0" Then Continue For
            Return True
        Next
        Return False
    End Function

    ''' <summary>Small preference for the subnets a real home/corp LAN almost
    ''' always uses (192.168/16, then 10/8) over 172.16/12, where Windows parks its
    ''' gateway-less virtual NAT switches. Only ever breaks ties between candidates
    ''' that scored equally on the stronger signals (gateway, private, physical).</summary>
    Private Function HomeLanBonus(addr As IPAddress) As Integer
        Dim b As Byte() = addr.GetAddressBytes()
        If b.Length <> 4 Then Return 0
        If b(0) = 192 AndAlso b(1) = 168 Then Return 50
        If b(0) = 10 Then Return 30
        Return 0
    End Function

    ''' <summary>RFC1918 private IPv4 (10/8, 172.16/12, 192.168/16).</summary>
    Private Function IsPrivateIPv4(addr As IPAddress) As Boolean
        Dim b As Byte() = addr.GetAddressBytes()
        If b.Length <> 4 Then Return False
        If b(0) = 10 Then Return True
        If b(0) = 172 AndAlso b(1) >= 16 AndAlso b(1) <= 31 Then Return True
        If b(0) = 192 AndAlso b(1) = 168 Then Return True
        Return False
    End Function

    ''' <summary>127/8 loopback or 169.254/16 APIPA link-local - never a usable
    ''' address to advertise to the phone.</summary>
    Private Function IsApipaOrLoopback(addr As IPAddress) As Boolean
        Dim b As Byte() = addr.GetAddressBytes()
        If b.Length <> 4 Then Return True
        If b(0) = 127 Then Return True
        If b(0) = 169 AndAlso b(1) = 254 Then Return True
        Return False
    End Function

    ''' <summary>A real physical NIC type (wired or Wi-Fi), as opposed to a
    ''' software adapter that also reports as Ethernet.</summary>
    Private Function IsPhysicalNic(nic As NetworkInterface) As Boolean
        Select Case nic.NetworkInterfaceType
            Case NetworkInterfaceType.Ethernet, NetworkInterfaceType.GigabitEthernet,
                 NetworkInterfaceType.FastEthernetT, NetworkInterfaceType.FastEthernetFx,
                 NetworkInterfaceType.Wireless80211
                Return True
        End Select
        Return False
    End Function

    ''' <summary>Heuristic for a virtual/VPN adapter that reports as Ethernet (so
    ''' NetworkInterfaceType alone cannot tell it apart from the real LAN) - matched
    ''' on the adapter name/description. Used only to lower its score, never to hard-
    ''' exclude it, so it can still be a last-resort address if nothing better exists.</summary>
    Private Function LooksVirtualNic(nic As NetworkInterface) As Boolean
        Dim hay As String = ((If(nic.Name, "")) & " " & (If(nic.Description, ""))).ToLowerInvariant()
        For Each h As String In VirtualNicHints
            If hay.IndexOf(h, StringComparison.Ordinal) >= 0 Then Return True
        Next
        Return False
    End Function

    Private ReadOnly VirtualNicHints As String() = {
        "virtual", "vmware", "virtualbox", "vbox", "hyper-v", "vethernet",
        "tap-", "tap adapter", "vpn", "tailscale", "wireguard", "zerotier",
        "pseudo", "wan miniport", "bluetooth", "docker", "hamachi", "loopback"
    }

    ''' <summary>Opens a URL in the default browser. Falls back through explorer.exe
    ''' (normal integrity) when the app runs elevated - a high-integrity
    ''' ShellExecute may fail to resolve the per-user default-browser association.
    ''' Returns False only if both attempts throw.</summary>
    Public Function OpenInBrowser(url As String) As Boolean
        If String.IsNullOrWhiteSpace(url) Then Return False
        Try
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
            Return True
        Catch
            Try
                Process.Start(New ProcessStartInfo("explorer.exe", url) With {.UseShellExecute = True})
                Return True
            Catch
                Return False
            End Try
        End Try
    End Function

End Module
