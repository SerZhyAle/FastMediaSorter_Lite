#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.Text

' Ticket 170 rule 3 (written here) and ticket 171 rule 1 (read there): the `FMSSFTP1` / `FMSSFTP2` pairing code of ANYWHERE-ACCESS
' section 4.1, one module for both directions so that what the producer writes is exactly what the consumer reads.
'
' The rules, held to the catalog's rung 1 vectors (tests: FmsSftpCodeTests, with the vectors vendored beside a provenance file):
'   - prefix `FMSSFTP1:` or `FMSSFTP2:`, then `&`-separated `key=value` fields in the order h, p, u, pw, fp, x, sid, drv;
'   - values are application/x-www-form-urlencoded UTF-8; the host-list separator `,` and the share id are written raw;
'   - `x` is `host:port` (an IPv6 literal in brackets), port 1-65535; `x` and `sid` are both present or both absent; `sid` is the
'     id form exactly (22 base64url characters, no padding, 16 bytes, spare bits zero); `drv` is only `drv=1`;
'   - a code that carries none of x, sid, drv is written `FMSSFTP1:` byte for byte; a `FMSSFTP1:` code is read by the v1 rules only
'     and the v2 keys inside it are ignored;
'   - a code that breaks any rule is refused whole, never partly read.
'
' A password is a secret: nothing here prints a field (ToString names the class and nothing else), and the library holds no
' user-facing string.

''' <summary>The fields of a pairing code. Immutable. A secret holder: never printed.</summary>
Friend NotInheritable Class FmsSftpCode

    Public Const PrefixV1 As String = "FMSSFTP1:"
    Public Const PrefixV2 As String = "FMSSFTP2:"

    ''' <summary>The host list in the producer's order (at least one).</summary>
    Public ReadOnly Hosts As IReadOnlyList(Of String)
    Public ReadOnly Port As Integer
    Public ReadOnly Username As String
    ''' <summary>Nothing in key-login mode (the field is absent).</summary>
    Public ReadOnly Password As String
    ''' <summary>"SHA256:" plus 43 base64 characters.</summary>
    Public ReadOnly Fingerprint As String
    ''' <summary>The exchange server `host:port` ("" when the code does not use the server channel).</summary>
    Public ReadOnly Exchange As String
    ''' <summary>The share id ("" when absent). A bearer capability: never logged.</summary>
    Public ReadOnly ShareId As String
    ''' <summary>The Drive marker (`drv=1`); it carries no secret.</summary>
    Public ReadOnly Drive As Boolean

    Public Sub New(hosts As IEnumerable(Of String), port As Integer, username As String, password As String, fingerprint As String,
                   exchange As String, shareId As String, drive As Boolean)
        Me.Hosts = New List(Of String)(If(hosts, Array.Empty(Of String)()))
        Me.Port = port
        Me.Username = If(username, "")
        Me.Password = password
        Me.Fingerprint = If(fingerprint, "")
        Me.Exchange = If(exchange, "")
        Me.ShareId = If(shareId, "")
        Me.Drive = drive
    End Sub

    ''' <summary>True when the code uses the exchange (`x` and `sid`) or the Drive marker: it is written with the v2 prefix.</summary>
    Public ReadOnly Property IsV2 As Boolean
        Get
            Return Exchange.Length > 0 OrElse ShareId.Length > 0 OrElse Drive
        End Get
    End Property

    ''' <summary>The same fields without the exchange pair (what a v1 reader keeps).</summary>
    Public Function WithoutExchange() As FmsSftpCode
        Return New FmsSftpCode(Hosts, Port, Username, Password, Fingerprint, "", "", Drive)
    End Function

    Public Overrides Function ToString() As String
        Return "FmsSftpCode"
    End Function

    ' --- writing ---------------------------------------------------------------------------------------------------------

    ''' <summary>The code text, or False when the fields break a rule (an unusable host or port, a fingerprint that is not the pin
    ''' form, `x` without `sid` or the reverse, a malformed `x` or `sid`). A refusal is the only other answer: nothing is
    ''' "fixed up".</summary>
    Public Function TryBuild(ByRef code As String) As Boolean
        code = ""
        If Not IsUsable() Then Return False
        Dim sb As New StringBuilder(If(IsV2, PrefixV2, PrefixV1))
        sb.Append("h=")
        For i As Integer = 0 To Hosts.Count - 1
            If i > 0 Then sb.Append(","c)
            sb.Append(Encode(Hosts(i)))
        Next
        sb.Append("&p=").Append(Port.ToString(CultureInfo.InvariantCulture))
        sb.Append("&u=").Append(Encode(Username))
        If Password IsNot Nothing Then sb.Append("&pw=").Append(Encode(Password))
        sb.Append("&fp=").Append(Encode(Fingerprint))
        If Exchange.Length > 0 Then
            sb.Append("&x=").Append(Encode(Exchange))
            sb.Append("&sid=").Append(ShareId)
        End If
        If Drive Then sb.Append("&drv=1")
        code = sb.ToString()
        Return True
    End Function

    ''' <summary>Whether the fields can be written at all (the same rules the reader applies).</summary>
    Public Function IsUsable() As Boolean
        If Hosts.Count = 0 Then Return False
        For Each h As String In Hosts
            If Not IsHost(h) Then Return False
        Next
        If Port < 1 OrElse Port > 65535 Then Return False
        If Username.Length = 0 OrElse HasControl(Username) Then Return False
        If Password IsNot Nothing AndAlso (Password.Length = 0 OrElse HasControl(Password)) Then Return False
        If Not ExchangeTrust.IsFingerprint(Fingerprint) Then Return False
        If (Exchange.Length > 0) <> (ShareId.Length > 0) Then Return False
        If Exchange.Length > 0 Then
            Dim address As ExchangeServerAddress = Nothing
            If Not ExchangeServerAddress.TryParse(Exchange, address) Then Return False
            If Not String.Equals(address.ToString(), Exchange, StringComparison.Ordinal) Then Return False
            If Not ExchangeIds.IsValidId(ShareId) Then Return False
        End If
        Return True
    End Function

    ' --- reading ---------------------------------------------------------------------------------------------------------

    Private Shared ReadOnly KeyOrder As String() = {"h", "p", "u", "pw", "fp", "x", "sid", "drv"}

    ''' <summary>Reads one code. False for anything that breaks a rule - the whole code is refused, nothing is partly read.</summary>
    Public Shared Function TryParse(text As String, ByRef code As FmsSftpCode) As Boolean
        code = Nothing
        If text Is Nothing Then Return False
        Dim t As String = text.Trim()
        Dim v2 As Boolean
        Dim body As String
        If t.StartsWith(PrefixV1, StringComparison.Ordinal) Then
            v2 = False
            body = t.Substring(PrefixV1.Length)
        ElseIf t.StartsWith(PrefixV2, StringComparison.Ordinal) Then
            v2 = True
            body = t.Substring(PrefixV2.Length)
        Else
            Return False
        End If
        If body.Length = 0 Then Return False

        Dim values As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim lastIndex As Integer = -1
        For Each part As String In body.Split("&"c)
            Dim eq As Integer = part.IndexOf("="c)
            If eq <= 0 Then Return False
            Dim key As String = part.Substring(0, eq)
            Dim index As Integer = Array.IndexOf(KeyOrder, key)
            If index < 0 OrElse index <= lastIndex Then Return False   ' an unknown key, a duplicate, or out of order
            lastIndex = index
            values(key) = part.Substring(eq + 1)
        Next

        ' a v1 code is read by the v1 rules only: the v2 keys inside it are dropped (after they were seen to be well ordered)
        If Not v2 Then
            values.Remove("x")
            values.Remove("sid")
            values.Remove("drv")
        End If

        Dim rawHosts As String = Nothing, rawPort As String = Nothing, rawUser As String = Nothing, rawFp As String = Nothing
        If Not values.TryGetValue("h", rawHosts) OrElse Not values.TryGetValue("p", rawPort) OrElse
           Not values.TryGetValue("u", rawUser) OrElse Not values.TryGetValue("fp", rawFp) Then Return False

        Dim hosts As New List(Of String)
        For Each rawHost As String In rawHosts.Split(","c)
            Dim host As String = Nothing
            If Not TryDecode(rawHost, host) OrElse Not IsHost(host) Then Return False
            hosts.Add(host)
        Next

        Dim port As Integer
        If Not Integer.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, port) OrElse port < 1 OrElse port > 65535 Then Return False
        ' no leading zeros or signs: "02022" is not what a producer writes
        If Not String.Equals(port.ToString(CultureInfo.InvariantCulture), rawPort, StringComparison.Ordinal) Then Return False

        Dim user As String = Nothing
        If Not TryDecode(rawUser, user) OrElse user.Length = 0 OrElse HasControl(user) Then Return False

        Dim password As String = Nothing
        Dim rawPassword As String = Nothing
        If values.TryGetValue("pw", rawPassword) Then
            If Not TryDecode(rawPassword, password) OrElse password.Length = 0 OrElse HasControl(password) Then Return False
        End If

        Dim fp As String = Nothing
        If Not TryDecode(rawFp, fp) OrElse Not ExchangeTrust.IsFingerprint(fp) Then Return False

        Dim exchange As String = ""
        Dim shareId As String = ""
        Dim rawX As String = Nothing, rawSid As String = Nothing
        Dim hasX As Boolean = values.TryGetValue("x", rawX)
        Dim hasSid As Boolean = values.TryGetValue("sid", rawSid)
        If hasX <> hasSid Then Return False
        If hasX Then
            If Not TryDecode(rawX, exchange) Then Return False
            Dim address As ExchangeServerAddress = Nothing
            ' ToString puts an IPv6 literal back into brackets, so a bare one ("2001:db8::7:44022") is not the same text
            If Not ExchangeServerAddress.TryParse(exchange, address) OrElse Not String.Equals(address.ToString(), exchange, StringComparison.Ordinal) Then Return False
            If Not ExchangeIds.IsValidId(rawSid) Then Return False
            shareId = rawSid
        End If

        Dim drive As Boolean = False
        Dim rawDrv As String = Nothing
        If values.TryGetValue("drv", rawDrv) Then
            If Not String.Equals(rawDrv, "1", StringComparison.Ordinal) Then Return False
            drive = True
        End If

        code = New FmsSftpCode(hosts, port, user, password, fp, exchange, shareId, drive)
        Return True
    End Function

    ' --- the two small rules ---------------------------------------------------------------------------------------------

    Private Shared Function IsHost(host As String) As Boolean
        If String.IsNullOrEmpty(host) OrElse host.Length > 253 Then Return False
        For Each c As Char In host
            If Char.IsWhiteSpace(c) OrElse Char.IsControl(c) OrElse c = "/"c OrElse c = "\"c OrElse c = "@"c OrElse c = ","c OrElse c = "&"c OrElse c = "="c Then Return False
        Next
        Return True
    End Function

    Private Shared Function HasControl(text As String) As Boolean
        For Each c As Char In text
            If Char.IsControl(c) Then Return True
        Next
        Return False
    End Function

    ''' <summary>application/x-www-form-urlencoded over UTF-8, the way Java's URLEncoder writes it: letters, digits and `.-*_` stay,
    ''' a space is `+`, everything else is `%XX` in capitals.</summary>
    Friend Shared Function Encode(value As String) As String
        Dim sb As New StringBuilder()
        For Each b As Byte In Encoding.UTF8.GetBytes(If(value, ""))
            Dim c As Char = ChrW(b)
            If (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "0"c AndAlso c <= "9"c) OrElse
               c = "."c OrElse c = "-"c OrElse c = "*"c OrElse c = "_"c Then
                sb.Append(c)
            ElseIf b = 32 Then
                sb.Append("+"c)
            Else
                sb.Append("%"c).Append(b.ToString("X2", CultureInfo.InvariantCulture))
            End If
        Next
        Return sb.ToString()
    End Function

    ''' <summary>The inverse; False for a `%` not followed by two hex digits or for bytes that are not valid UTF-8.</summary>
    Friend Shared Function TryDecode(raw As String, ByRef value As String) As Boolean
        value = ""
        Dim bytes As New List(Of Byte)(raw.Length)
        Dim i As Integer = 0
        While i < raw.Length
            Dim c As Char = raw(i)
            If c = "%"c Then
                If i + 2 > raw.Length - 1 Then Return False
                Dim h1 As Integer = HexValue(raw(i + 1))
                Dim h2 As Integer = HexValue(raw(i + 2))
                If h1 < 0 OrElse h2 < 0 Then Return False
                bytes.Add(CByte(h1 * 16 + h2))
                i += 3
            ElseIf c = "+"c Then
                bytes.Add(32)
                i += 1
            ElseIf AscW(c) < 128 Then
                bytes.Add(CByte(AscW(c)))
                i += 1
            Else
                ' a code is ASCII on the wire; a raw non-ASCII character is not what a producer writes
                Return False
            End If
        End While
        Try
            value = New UTF8Encoding(False, True).GetString(bytes.ToArray())
            Return True
        Catch ex As DecoderFallbackException
            Return False
        End Try
    End Function

    Private Shared Function HexValue(c As Char) As Integer
        If c >= "0"c AndAlso c <= "9"c Then Return AscW(c) - AscW("0"c)
        If c >= "A"c AndAlso c <= "F"c Then Return AscW(c) - AscW("A"c) + 10
        If c >= "a"c AndAlso c <= "f"c Then Return AscW(c) - AscW("a"c) + 10
        Return -1
    End Function

End Class
#End If
