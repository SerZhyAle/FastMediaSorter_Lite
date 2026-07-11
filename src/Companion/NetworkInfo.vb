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
    ''' worker's reachability.lanAddress is authoritative. Sends no packets (the
    ''' UDP "connect" to TEST-NET-1 only resolves the outbound interface).</summary>
    Public Function LocalIPv4() As String
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
