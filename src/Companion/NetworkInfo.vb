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
        If viaRoute.Length > 0 Then Return viaRoute
        Return LocalIPv4ViaAdapterScan()
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

    ''' <summary>Enumerates adapters for the same "real LAN adapter" signature
    ''' DefaultGatewayIp uses (Up, not loopback/tunnel, a real IPv4 gateway AND a
    ''' bound IPv4) and returns that adapter's own address. Unlike the outbound-
    ''' route trick, this needs no default route - only the adapter's own config.</summary>
    Private Function LocalIPv4ViaAdapterScan() As String
        Try
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus <> OperationalStatus.Up Then Continue For
                Select Case nic.NetworkInterfaceType
                    Case NetworkInterfaceType.Loopback, NetworkInterfaceType.Tunnel
                        Continue For
                End Select

                Dim props As IPInterfaceProperties = nic.GetIPProperties()
                If props Is Nothing Then Continue For

                Dim hasGateway As Boolean = False
                For Each gw As GatewayIPAddressInformation In props.GatewayAddresses
                    If gw Is Nothing OrElse gw.Address Is Nothing Then Continue For
                    If gw.Address.AddressFamily <> AddressFamily.InterNetwork Then Continue For
                    Dim s As String = gw.Address.ToString()
                    If String.IsNullOrEmpty(s) OrElse s = "0.0.0.0" Then Continue For
                    hasGateway = True
                    Exit For
                Next
                If Not hasGateway Then Continue For

                For Each ua As UnicastIPAddressInformation In props.UnicastAddresses
                    If ua.Address IsNot Nothing AndAlso ua.Address.AddressFamily = AddressFamily.InterNetwork Then
                        Return ua.Address.ToString()
                    End If
                Next
            Next
        Catch
        End Try
        Return ""
    End Function

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
