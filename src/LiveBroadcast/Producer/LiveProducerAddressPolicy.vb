#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets

' Ticket 199 section 3.3: the address. The published address is chosen by this program, never loopback,
' 0.0.0.0 or a mobile-carrier interface; the listener is bound to that address, not to every
' interface. With no reachable LAN address and no exchange (173) the broadcast does not start, neither
' source opens and no descriptor exists (producer rule 1).
'
' The scored pick itself (VPN and virtual adapters) is NetworkInfo.LocalIPv4's job in the Share
' Manager; this class is the pure rule the pick must pass and the guard the tests drive. The machine's
' candidates are injected: the Share Manager enumerates them, the tests hand in made-up lists, and no
' test needs a device.

''' <summary>One candidate the enumerator offers.</summary>
Friend Class LiveAddressCandidate
    Public Property Address As String = ""
    ''' <summary>The NIC type of the adapter that owns the address (the carrier rule).</summary>
    Public Property NicType As NetworkInterfaceType = NetworkInterfaceType.Ethernet
    ''' <summary>True when the adapter looks virtual or VPN (NetworkInfo's heuristic result).</summary>
    Public Property LooksVirtual As Boolean = False
    ''' <summary>True when the adapter carries a real IPv4 default gateway - the strongest "true LAN
    ''' interface" signal of NetworkInfo's score.</summary>
    Public Property HasIpv4Gateway As Boolean = False
End Class

Friend NotInheritable Class LiveProducerAddressPolicy

    Private Sub New()
    End Sub

    ''' <summary>True when an address may be PUBLISHED: a real IPv4, never loopback, never unspecified,
    ''' never link-local (APIPA), never a documentation or reserved block. The carrier rule is the
    ''' candidate's NIC type, not the address: a PC on a phone's hotspot has a private LAN address and
    ''' may publish it; a WWAN modem's own address may not.</summary>
    Public Shared Function IsPublishable(candidate As LiveAddressCandidate) As Boolean
        If candidate Is Nothing Then Return False
        If IsCarrierInterface(candidate.NicType) Then Return False

        Dim ip As IPAddress = Nothing
        If Not IPAddress.TryParse(candidate.Address, ip) Then Return False
        If ip.AddressFamily <> AddressFamily.InterNetwork Then Return False

        Dim b As Byte() = ip.GetAddressBytes()
        If b.Length <> 4 Then Return False
        If b(0) = 127 Then Return False                                   ' loopback
        If b(0) = 0 Then Return False                                     ' 0.0.0.0 / this network
        If b(0) = 169 AndAlso b(1) = 254 Then Return False                ' APIPA link-local
        If b(0) = 224 Then Return False                                   ' multicast
        If b(0) >= 240 Then Return False                                  ' reserved / broadcast
        If b(0) = 192 AndAlso b(1) = 0 AndAlso b(2) = 2 Then Return False ' TEST-NET-1 (also item J's
        ' documentation host, never a published address)
        Return True
    End Function

    ''' <summary>The one address this broadcast publishes, or "" when none is publishable - in which
    ''' case the start is refused (producer rule 1). Scoring mirrors NetworkInfo.LocalIPv4's: a real
    ''' IPv4 gateway is the strongest true-LAN signal, a private RFC1918 address and a physical NIC
    ''' raise the score, a virtual/VPN adapter lowers it; equal scores keep the first order, so the
    ''' enumerator's order (NetworkInfo's) wins ties.</summary>
    Public Shared Function ChooseAddress(candidates As System.Collections.Generic.IEnumerable(Of LiveAddressCandidate)) As String
        If candidates Is Nothing Then Return ""
        Dim best As String = ""
        Dim bestScore As Integer = Integer.MinValue
        For Each c As LiveAddressCandidate In candidates
            If Not IsPublishable(c) Then Continue For
            Dim ip As IPAddress = IPAddress.Parse(c.Address)
            Dim score As Integer = 0
            If c.HasIpv4Gateway Then score += 1000
            If IsPrivateIPv4(ip) Then score += 400
            If IsPhysicalNic(c.NicType) Then score += 200
            If c.LooksVirtual Then score -= 800
            If score > bestScore Then
                bestScore = score
                best = c.Address
            End If
        Next
        Return best
    End Function

    ''' <summary>The controller's second line of defense behind the host's scored pick: an address that
    ''' could never be published - loopback, unspecified, link-local, documentation, multicast - refuses
    ''' the start (producer rule 1). The NIC-type carrier rule needs the candidate list and is judged by
    ''' <see cref="ChooseAddress"/> in the host's wiring; this checks the string alone.</summary>
    Public Shared Function IsPublishablePicked(address As String) As Boolean
        If String.IsNullOrWhiteSpace(address) Then Return False
        Dim ip As System.Net.IPAddress = Nothing
        If Not System.Net.IPAddress.TryParse(address.Trim(), ip) Then Return False
        If ip.AddressFamily <> AddressFamily.InterNetwork Then Return False
        Return IsPublishable(New LiveAddressCandidate With {.Address = address.Trim(), .NicType = NetworkInterfaceType.Ethernet})
    End Function

    ''' <summary>A mobile-carrier interface: WWAN (a cellular modem's own adapter, the "mobile-carrier
    ''' interface" of producer rule 1) and PPP (a dial-up carrier link). A phone's hotspot the PC sits
    ''' on is a normal Wi-Fi adapter with a private LAN address and is NOT this.</summary>
    Private Shared Function IsCarrierInterface(nicType As NetworkInterfaceType) As Boolean
        Select Case nicType
            Case NetworkInterfaceType.Wman, NetworkInterfaceType.WwanPP, NetworkInterfaceType.WwanPP2, NetworkInterfaceType.Ppp
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function IsPhysicalNic(nicType As NetworkInterfaceType) As Boolean
        Select Case nicType
            Case NetworkInterfaceType.Ethernet, NetworkInterfaceType.GigabitEthernet,
                 NetworkInterfaceType.FastEthernetT, NetworkInterfaceType.FastEthernetFx,
                 NetworkInterfaceType.Wireless80211
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function IsPrivateIPv4(ip As IPAddress) As Boolean
        Dim b As Byte() = ip.GetAddressBytes()
        If b(0) = 10 Then Return True
        If b(0) = 172 AndAlso b(1) >= 16 AndAlso b(1) <= 31 Then Return True
        If b(0) = 192 AndAlso b(1) = 168 Then Return True
        Return False
    End Function

    ''' <summary>The LAN URL the descriptor and the window show: http://&lt;address&gt;:&lt;port&gt;&lt;path&gt;. The
    ''' address is the chosen, publishable one - never loopback, never 0.0.0.0.</summary>
    Public Shared Function BuildUrl(address As String, port As Integer, path As String) As String
        Return "http://" & address & ":" & port.ToString(Globalization.CultureInfo.InvariantCulture) & path
    End Function

End Class
#End If
