Option Strict On

Imports System.Globalization
Imports System.Net

''' <summary>Which exchange server the share service registers its shares on (ticket 148 section 2.1).</summary>
Public Enum ExchangeMode
    ''' <summary>Nothing registered, nothing exported: byte-identical to a build without the feature
    ''' (contract ANYWHERE-ACCESS section 8.5). The default.</summary>
    Off = 0
    ''' <summary>This PC's own exchange server (ticket 142); the registration endpoint is its loopback
    ''' address and the port is read from the local server, never typed.</summary>
    ThisPc = 1
    ''' <summary>A server somewhere else; the user types its address.</summary>
    External = 2
End Enum

''' <summary>A parsed <c>host:port</c> (an IPv6 literal is written in brackets).</summary>
Public Structure ExchangeEndpoint
    Public ReadOnly Host As String
    Public ReadOnly Port As Integer

    Public Sub New(host As String, port As Integer)
        Me.Host = host
        Me.Port = port
    End Sub

    ''' <summary>Round-trips <see cref="ExchangeEndpointRules.TryParse"/>: an IPv6 literal gets its brackets back.</summary>
    Public Overrides Function ToString() As String
        Dim h As String = If(Host, "")
        If h.IndexOf(":"c) >= 0 Then h = "[" & h & "]"
        Return h & ":" & Port.ToString(CultureInfo.InvariantCulture)
    End Function
End Structure

''' <summary>The verdict on one typed endpoint, in the words the UI must say beside the field.</summary>
Public Enum ExchangeEndpointVerdict
    ''' <summary>Nothing typed. For the client-facing endpoint that is legal and means "publish no
    ''' tunnel path" - the UI says why (ticket 148 section 2.1).</summary>
    Empty
    Valid
    ''' <summary>Not <c>host:port</c>: a scheme, a path, a blank or missing port, a port outside 1..65535,
    ''' whitespace inside.</summary>
    Malformed
    ''' <summary>Loopback or the unspecified address: a phone cannot reach this PC's loopback, so it is
    ''' never a client-facing endpoint (never published, never accepted there).</summary>
    Loopback
End Enum

''' <summary>
''' Pure rules for the producer's two endpoints and the password the user types for an external server
''' (ticket 148). No registry, no network, no worker: the settings class below stores what these accept.
''' </summary>
Public NotInheritable Class ExchangeEndpointRules

    Private Sub New()
    End Sub

    ''' <summary>Parses <c>host:port</c> or <c>[ipv6]:port</c>. A DNS name, an IPv4 or an IPv6 literal is a
    ''' host; a scheme (<c>://</c>), a path, a space or a missing/zero/oversized port is not.</summary>
    Public Shared Function TryParse(text As String, ByRef endpoint As ExchangeEndpoint) As Boolean
        endpoint = Nothing
        Dim t As String = If(text, "").Trim()
        If t.Length = 0 OrElse t.Length > 255 Then Return False
        For Each c As Char In t
            If Char.IsWhiteSpace(c) OrElse c = "/"c OrElse c = "\"c OrElse c = "@"c OrElse c = "?"c OrElse c = "#"c Then Return False
        Next

        Dim host As String
        Dim portText As String
        If t.StartsWith("[", StringComparison.Ordinal) Then
            Dim close As Integer = t.IndexOf("]"c)
            If close < 2 OrElse close + 1 >= t.Length OrElse t(close + 1) <> ":"c Then Return False
            host = t.Substring(1, close - 1)
            portText = t.Substring(close + 2)
            Dim ip As IPAddress = Nothing
            If Not IPAddress.TryParse(host, ip) OrElse ip.AddressFamily <> Sockets.AddressFamily.InterNetworkV6 Then Return False
        Else
            Dim colon As Integer = t.LastIndexOf(":"c)
            If colon <= 0 OrElse colon <> t.IndexOf(":"c) Then Return False   ' none, leading, or a bare IPv6
            host = t.Substring(0, colon)
            portText = t.Substring(colon + 1)
            If Not IsPlausibleHostName(host) Then Return False
        End If

        Dim port As Integer
        If Not Integer.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, port) Then Return False
        If port < 1 OrElse port > 65535 Then Return False
        endpoint = New ExchangeEndpoint(host, port)
        Return True
    End Function

    ''' <summary>Letters, digits, dots, hyphens and underscores; labels not empty and not hyphen-led. An
    ''' all-numeric dotted form must be a real IPv4 address, so <c>999.1.1.1</c> is refused rather than
    ''' passed on as a DNS name.</summary>
    Private Shared Function IsPlausibleHostName(host As String) As Boolean
        If host.Length = 0 Then Return False
        Dim allNumeric As Boolean = True
        For Each label As String In host.Split("."c)
            If label.Length = 0 OrElse label.Length > 63 OrElse label(0) = "-"c Then Return False
            For Each c As Char In label
                Dim ok As Boolean = (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse
                                    (c >= "0"c AndAlso c <= "9"c) OrElse c = "-"c OrElse c = "_"c
                If Not ok Then Return False
                If Not (c >= "0"c AndAlso c <= "9"c) Then allNumeric = False
            Next
        Next
        If allNumeric Then
            Dim ip As IPAddress = Nothing
            Return host.Split("."c).Length = 4 AndAlso IPAddress.TryParse(host, ip)
        End If
        Return True
    End Function

    ''' <summary>True for <c>localhost</c> (and any <c>*.localhost</c>), 127.0.0.0/8, ::1, 0.0.0.0 and ::, plus the
    ''' IPv4-mapped forms of those.</summary>
    Public Shared Function IsLoopbackOrUnspecified(host As String) As Boolean
        Dim h As String = If(host, "").Trim().Trim("["c, "]"c)
        If h.Length = 0 Then Return False
        If h.Equals("localhost", StringComparison.OrdinalIgnoreCase) OrElse h.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) Then Return True
        Dim ip As IPAddress = Nothing
        If Not IPAddress.TryParse(h, ip) Then Return False
        If ip.IsIPv4MappedToIPv6 Then ip = ip.MapToIPv4()
        Return IPAddress.IsLoopback(ip) OrElse ip.Equals(IPAddress.Any) OrElse ip.Equals(IPAddress.IPv6Any)
    End Function

    ''' <summary>The verdict for the CLIENT-FACING endpoint: empty is legal (no tunnel path is published),
    ''' loopback is never.</summary>
    Public Shared Function CheckClientEndpoint(text As String) As ExchangeEndpointVerdict
        If String.IsNullOrWhiteSpace(text) Then Return ExchangeEndpointVerdict.Empty
        Dim ep As ExchangeEndpoint = Nothing
        If Not TryParse(text, ep) Then Return ExchangeEndpointVerdict.Malformed
        If IsLoopbackOrUnspecified(ep.Host) Then Return ExchangeEndpointVerdict.Loopback
        Return ExchangeEndpointVerdict.Valid
    End Function

    ''' <summary>The verdict for the REGISTRATION endpoint of an external server. It may not be empty (the
    ''' worker has nowhere to connect), so empty reads as malformed here. Loopback is accepted - it is how a
    ''' second program on this PC or a tunnel to a remote box looks from here.</summary>
    Public Shared Function CheckRegistrationEndpoint(text As String) As ExchangeEndpointVerdict
        Dim ep As ExchangeEndpoint = Nothing
        Return If(TryParse(text, ep), ExchangeEndpointVerdict.Valid, ExchangeEndpointVerdict.Malformed)
    End Function

    ''' <summary>The endpoint the worker connects to in <see cref="ExchangeMode.ThisPc"/>: loopback and the
    ''' port of the local server (read from its config by the caller, or the server's default when it
    ''' cannot be read). Never typed by the user.</summary>
    Public Shared Function ThisPcRegistrationEndpoint(localPort As Integer) As ExchangeEndpoint
        Dim port As Integer = If(localPort >= 1 AndAlso localPort <= 65535, localPort, ExchangeSettings.DefaultPort)
        Return New ExchangeEndpoint("127.0.0.1", port)
    End Function

    ''' <summary>The password typed for an external server meets the contract floor (section 5.2) - the same
    ''' pre-check the local server's dialog uses, against the same shared vectors.</summary>
    Friend Shared Function CheckServerPassword(password As String) As ExchangePasswordPolicy.Verdict
        Return ExchangePasswordPolicy.Check(password)
    End Function
End Class

''' <summary>
''' The share service's exchange-server preferences (ticket 148 T-3), in the same HKCU store as
''' <see cref="ShareSettings"/> (SZA\FastMediaSorter, values named <c>Share_Exchange*</c>).
'''
''' What is NOT here, on purpose: the server password (it travels to the worker through the one
''' <c>SetExchange</c> request and is kept only in the worker's secret store - this class has no field for
''' it, so no code path can write it to the registry), the pinned certificate fingerprint (the worker owns
''' the pin), and any share id (the worker persists those in <c>shares.json</c>). Only <see cref="Mode"/> is
''' safe for a log package; the endpoints are withheld from it by the allow-list.
''' </summary>
Public Class ExchangeProducerSettings

    Public Property Mode As ExchangeMode = ExchangeMode.Off                    ' Share_ExchangeMode
    ''' <summary>Where the worker connects, in <see cref="ExchangeMode.External"/>. Ignored for ThisPc.</summary>
    Public Property RegistrationEndpoint As String = ""                        ' Share_ExchangeRegistrationEndpoint
    ''' <summary>What QR and .fmscfg carry for other devices. Empty = publish no tunnel path.</summary>
    Public Property ClientEndpoint As String = ""                              ' Share_ExchangeClientEndpoint

    Friend Const ModeName As String = "Share_ExchangeMode"
    Friend Const RegistrationEndpointName As String = "Share_ExchangeRegistrationEndpoint"
    Friend Const ClientEndpointName As String = "Share_ExchangeClientEndpoint"

    Public Sub Load()
        Mode = ClampMode(ReadInt(ModeName, CInt(ExchangeMode.Off)))
        RegistrationEndpoint = SanitizedEndpoint(GetSetting(App_name, Second_App_Name, RegistrationEndpointName, ""))
        ClientEndpoint = SanitizedEndpoint(GetSetting(App_name, Second_App_Name, ClientEndpointName, ""))
    End Sub

    Public Sub Save()
        SaveSetting(App_name, Second_App_Name, ModeName, CInt(Mode).ToString(CultureInfo.InvariantCulture))
        SaveSetting(App_name, Second_App_Name, RegistrationEndpointName, SanitizedEndpoint(RegistrationEndpoint))
        SaveSetting(App_name, Second_App_Name, ClientEndpointName, SanitizedEndpoint(ClientEndpoint))
    End Sub

    ''' <summary>An unknown stored number is Off - the feature never switches itself on from a damaged value.</summary>
    Public Shared Function ClampMode(value As Integer) As ExchangeMode
        Select Case value
            Case CInt(ExchangeMode.ThisPc) : Return ExchangeMode.ThisPc
            Case CInt(ExchangeMode.External) : Return ExchangeMode.External
            Case Else : Return ExchangeMode.Off
        End Select
    End Function

    ''' <summary>A stored endpoint that no longer parses (or a client endpoint that is loopback) reads as empty,
    ''' so a hand-edited registry value can never reach a QR or the worker.</summary>
    Friend Shared Function SanitizedEndpoint(text As String) As String
        Dim ep As ExchangeEndpoint = Nothing
        If Not ExchangeEndpointRules.TryParse(text, ep) Then Return ""
        Return ep.ToString()
    End Function

    ''' <summary>The endpoint to publish to clients, or Nothing when none may be: mode off, nothing typed, or a
    ''' loopback address. The export builder takes only this - never the raw string.</summary>
    Public Function PublishableClientEndpoint() As ExchangeEndpoint?
        If Mode = ExchangeMode.Off Then Return Nothing
        If ExchangeEndpointRules.CheckClientEndpoint(ClientEndpoint) <> ExchangeEndpointVerdict.Valid Then Return Nothing
        Dim ep As ExchangeEndpoint = Nothing
        If Not ExchangeEndpointRules.TryParse(ClientEndpoint, ep) Then Return Nothing
        Return ep
    End Function

    Private Shared Function ReadInt(key As String, def As Integer) As Integer
        Dim raw As String = GetSetting(App_name, Second_App_Name, key, def.ToString(CultureInfo.InvariantCulture))
        Dim parsed As Integer
        If Integer.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, parsed) Then Return parsed
        Return def
    End Function

End Class
