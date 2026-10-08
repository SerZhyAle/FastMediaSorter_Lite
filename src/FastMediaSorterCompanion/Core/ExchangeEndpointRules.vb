Option Strict On

Imports System.Globalization
Imports System.Net

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

''' <summary>The verdict on one typed endpoint.</summary>
Public Enum ExchangeEndpointVerdict
    ''' <summary>Nothing typed.</summary>
    Empty
    Valid
    ''' <summary>Not <c>host:port</c>: a scheme, a path, a blank or missing port, a port outside 1..65535, whitespace inside.</summary>
    Malformed
    ''' <summary>Loopback or the unspecified address: another device cannot reach this PC's loopback, so it is never an endpoint
    ''' that is handed to one.</summary>
    Loopback
End Enum

''' <summary>
''' The pure rules for an exchange endpoint (kept from ticket 148 by ticket 170 section 5 and re-fed): what the server calls its
''' public endpoint (<c>welcome.publicEndpoint</c>) is validated here before it goes into an FMSSFTP2 code or a .fmscfg tunnel path.
''' No registry, no network, no worker.
''' </summary>
Public NotInheritable Class ExchangeEndpointRules

    Private Sub New()
    End Sub

    ''' <summary>Parses <c>host:port</c> or <c>[ipv6]:port</c>. A DNS name, an IPv4 or an IPv6 literal is a host; a scheme
    ''' (<c>://</c>), a path, a space or a missing/zero/oversized port is not.</summary>
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

    ''' <summary>Letters, digits, dots, hyphens and underscores; labels not empty and not hyphen-led. An all-numeric dotted form must be
    ''' a real IPv4 address, so <c>999.1.1.1</c> is refused rather than passed on as a DNS name.</summary>
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

    ''' <summary>True for <c>localhost</c> (and any <c>*.localhost</c>), 127.0.0.0/8, ::1, 0.0.0.0 and ::, plus the IPv4-mapped forms of
    ''' those.</summary>
    Public Shared Function IsLoopbackOrUnspecified(host As String) As Boolean
        Dim h As String = If(host, "").Trim().Trim("["c, "]"c)
        If h.Length = 0 Then Return False
        If h.Equals("localhost", StringComparison.OrdinalIgnoreCase) OrElse h.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) Then Return True
        Dim ip As IPAddress = Nothing
        If Not IPAddress.TryParse(h, ip) Then Return False
        If ip.IsIPv4MappedToIPv6 Then ip = ip.MapToIPv4()
        Return IPAddress.IsLoopback(ip) OrElse ip.Equals(IPAddress.Any) OrElse ip.Equals(IPAddress.IPv6Any)
    End Function

    ''' <summary>The verdict for an endpoint that is handed to another device: empty is legal (nothing is published), loopback never is.</summary>
    Public Shared Function CheckClientEndpoint(text As String) As ExchangeEndpointVerdict
        If String.IsNullOrWhiteSpace(text) Then Return ExchangeEndpointVerdict.Empty
        Dim ep As ExchangeEndpoint = Nothing
        If Not TryParse(text, ep) Then Return ExchangeEndpointVerdict.Malformed
        If IsLoopbackOrUnspecified(ep.Host) Then Return ExchangeEndpointVerdict.Loopback
        Return ExchangeEndpointVerdict.Valid
    End Function

End Class
