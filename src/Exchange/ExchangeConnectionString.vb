#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text

' Ticket 187 T-1. The exchange connection string of the proposal PROPOSAL-2026-10-10-exchange-connection-string.md:
'
'     FMSEXCH1:x=host%3Aport&fp=SHA256%3A<43 base64>&n=<name>&u=<login>&i=<22 base64url>
'
' It names ONE server and the pin of its certificate; `i` (ticket 224, DEVICE-EXCHANGE item AR) carries the one-time invitation code when the
' string is an invitation - a secret holder: never logged, never stored after use. It is not a credential and carries
' touches a secret store, a log or the network: it is a pure codec, a second implementation of the Go package
' exchange-server/internal/connstr. The conformance artifact is the Go module's generated
' exchange-server/testdata/vectors/connection-string-v1.json, replayed by ExchangeConnectionStringVectorTests; a reading
' that differs from it is wrong here, whatever it looks like.
'
' Writer: x, fp, n, u in that order; every value through the WHATWG application/x-www-form-urlencoded byte serializer over
' UTF-8 (A-Z a-z 0-9 * - . _ raw, a space as +, any other byte %XX with upper-case hex). Reader: what any conforming or
' hand-made encoder produces (either hex case, + for a space, a raw ~, any key order, an upper-case host or login, a
' leading zero in the port); refuses the whole string - never a partial read - for a raw non-ASCII character, a bad
' escape, a decoded value that is not UTF-8, an empty or malformed field, a repeated or rule-breaking known key and
' white space inside. An unknown key is ignored whatever its value (a later MINOR adds keys).

''' <summary>The refusal vocabulary of the conformance vectors. A replay compares these by name; a UI maps each to its own
''' sentence (ExchangeResourceText).</summary>
Friend NotInheritable Class ExchangeConnectionStringReason

    Private Sub New()
    End Sub

    Public Const None As String = ""
    Public Const NotAString As String = "not-a-string"
    Public Const NewerMajor As String = "newer-major"
    Public Const TooLong As String = "too-long"
    Public Const Whitespace As String = "whitespace"
    Public Const NotAscii As String = "not-ascii"
    Public Const BadEscape As String = "bad-escape"
    Public Const BadUtf8 As String = "bad-utf8"
    Public Const MalformedField As String = "malformed-field"
    Public Const RepeatedKey As String = "repeated-key"
    Public Const MissingX As String = "missing-x"
    Public Const BadX As String = "bad-x"
    Public Const BadFingerprint As String = "bad-fp"
    Public Const BadName As String = "bad-n"
    Public Const BadLogin As String = "bad-u"
    ''' <summary>`i` is not a valid invitation capability id (ticket 224, DEVICE-EXCHANGE item AR).</summary>
    Public Const BadInv As String = "bad-i"
    Public Const FileTooLong As String = "file-too-long"
    Public Const FileLines As String = "file-lines"

    ''' <summary>What a not-a-string text resembles.</summary>
    Public Const LooksShareCode As String = "share-code"
    Public Const LooksUrl As String = "url"
End Class

''' <summary>One server, in canonical form: the host lower-cased (an IPv6 literal in its RFC 5952 form, no brackets), the
''' port a number, the login lower-cased. Immutable.</summary>
Friend NotInheritable Class ExchangeConnectionStringValue

    Public ReadOnly Host As String
    Public ReadOnly Port As Integer
    ''' <summary>"SHA256:" and 43 characters, or "" when the leaf is publicly trusted.</summary>
    Public ReadOnly Fingerprint As String
    Public ReadOnly Name As String
    Public ReadOnly Login As String
    ''' <summary>The one-time invitation code the string carries (ticket 224 rule 1), or "". A secret holder: it is shown
    ''' to nobody, logged never, and left behind nowhere once the registration is done.</summary>
    Public ReadOnly Invitation As String

    Public Sub New(host As String, port As Integer, fingerprint As String, name As String, login As String, Optional invitation As String = "")
        Me.Host = If(host, "")
        Me.Port = port
        Me.Fingerprint = If(fingerprint, "")
        Me.Name = If(name, "")
        Me.Login = If(login, "")
        Me.Invitation = If(invitation, "")
    End Sub

    ''' <summary>host:port, an IPv6 literal in brackets - the shape of the server's publicEndpoint.</summary>
    Public ReadOnly Property Endpoint As String
        Get
            Dim h As String = Host
            If h.IndexOf(":"c) >= 0 Then h = "[" & h & "]"
            Return h & ":" & Port.ToString(CultureInfo.InvariantCulture)
        End Get
    End Property

    ''' <summary>The same server (host and port), whatever the other members say.</summary>
    Public Function SameEndpoint(other As ExchangeConnectionStringValue) As Boolean
        Return other IsNot Nothing AndAlso Port = other.Port AndAlso String.Equals(Host, other.Host, StringComparison.Ordinal)
    End Function
End Class

''' <summary>The outcome of a read: a value, or a refusal. Never both.</summary>
Friend NotInheritable Class ExchangeConnectionStringResult

    Public ReadOnly Value As ExchangeConnectionStringValue
    ''' <summary>One of <see cref="ExchangeConnectionStringReason"/>; "" when the read succeeded.</summary>
    Public ReadOnly Reason As String
    ''' <summary>With NotAString: what the text resembles ("share-code", "url") or "".</summary>
    Public ReadOnly Looks As String
    ''' <summary>A developer's explanation. Never user text and never logged with the string.</summary>
    Public ReadOnly Detail As String

    Private Sub New(value As ExchangeConnectionStringValue, reason As String, looks As String, detail As String)
        Me.Value = value
        Me.Reason = reason
        Me.Looks = looks
        Me.Detail = detail
    End Sub

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Value IsNot Nothing
        End Get
    End Property

    Friend Shared Function Ok(value As ExchangeConnectionStringValue) As ExchangeConnectionStringResult
        Return New ExchangeConnectionStringResult(value, ExchangeConnectionStringReason.None, "", "")
    End Function

    Friend Shared Function Refused(reason As String, detail As String, Optional looks As String = "") As ExchangeConnectionStringResult
        Return New ExchangeConnectionStringResult(Nothing, reason, looks, detail)
    End Function
End Class

Friend NotInheritable Class ExchangeConnectionString

    Public Const Prefix As String = "FMSEXCH1:"
    Public Const MaxText As Integer = 512
    Public Const MaxName As Integer = 64
    Public Const MaxFile As Integer = 4096
    Public Const FileExtension As String = ".fmsexch"

    Private Const MinLogin As Integer = 3
    Private Const MaxLogin As Integer = 64
    Private Const MaxHost As Integer = 253
    Private Const MaxLabel As Integer = 63
    Private Const FingerprintPrefix As String = "SHA256:"
    Private Const FingerprintBase64Length As Integer = 43
    Private Const Base64Alphabet As String = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
    Private Const UpperHex As String = "0123456789ABCDEF"

    Private Shared ReadOnly StrictUtf8 As New UTF8Encoding(False, True)

    Private Sub New()
    End Sub

    ' ---- reading ------------------------------------------------------------------------------------------------

    ''' <summary>Reads the text of a connection string as it arrives from a clipboard, a chat or a QR: one leading
    ''' byte-order mark and the white space around it are dropped first.</summary>
    Public Shared Function Parse(text As String) As ExchangeConnectionStringResult
        Dim s As String = If(text, "")
        If s.Length > 0 AndAlso s(0) = ChrW(65279) Then s = s.Substring(1)
        s = s.Trim()
        If Not s.StartsWith(Prefix, StringComparison.Ordinal) Then Return NotAString(s)
        If ScalarCount(s) > MaxText Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.TooLong, "more than 512 characters")
        For Each ch As Char In s
            If Char.IsWhiteSpace(ch) Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.Whitespace, "white space inside the text")
            If AscW(ch) < &H21 OrElse AscW(ch) > &H7E Then
                Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.NotAscii, "a raw non-ASCII or control character; values are percent-encoded")
            End If
        Next
        Dim body As String = s.Substring(Prefix.Length)
        If body.Length = 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.MissingX, "no fields")

        Dim values As New Dictionary(Of String, String)(StringComparer.Ordinal)
        For Each field As String In body.Split("&"c)
            If field.Length = 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.MalformedField, "an empty field")
            Dim eq As Integer = field.IndexOf("="c)
            If eq <= 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.MalformedField, "a field with no key or no =")
            Dim key As String = field.Substring(0, eq)
            If key = "x" OrElse key = "fp" OrElse key = "n" OrElse key = "u" OrElse key = "i" Then
                If values.ContainsKey(key) Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.RepeatedKey, key & " given twice")
                Dim decoded As String = Nothing
                Dim failure As ExchangeConnectionStringResult = Unescape(field.Substring(eq + 1), decoded)
                If failure IsNot Nothing Then Return failure
                values(key) = decoded
            End If
            ' an unknown key is ignored whatever its value
        Next
        Dim x As String = Nothing
        If Not values.TryGetValue("x", x) Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.MissingX, "no x field")
        ' a known key that is present must carry a value: an empty one breaks its rule, it is not "absent"
        Dim fp As String = Nothing, n As String = Nothing, u As String = Nothing, iv As String = Nothing
        If values.TryGetValue("fp", fp) AndAlso fp.Length = 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadFingerprint, "fp has no value")
        If values.TryGetValue("n", n) AndAlso n.Length = 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadName, "n has no value")
        If values.TryGetValue("u", u) AndAlso u.Length = 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadLogin, "u has no value")
        If values.TryGetValue("i", iv) AndAlso iv.Length = 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadInv, "i has no value")
        Return Build(x, fp, n, u, iv)
    End Function

    ''' <summary>Reads the bytes of a .fmsexch file: UTF-8, at most 4096 bytes, one non-empty line, one optional
    ''' byte-order mark and line break. The caller reads at most MaxFile + 1 bytes.</summary>
    Public Shared Function ParseFile(bytes As Byte()) As ExchangeConnectionStringResult
        If bytes Is Nothing Then bytes = New Byte() {}
        If bytes.Length > MaxFile Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.FileTooLong, "more than 4096 bytes")
        Dim s As String
        Try
            s = StrictUtf8.GetString(bytes)
        Catch ex As ArgumentException
            Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadUtf8, "the file is not UTF-8")
        End Try
        If s.Length > 0 AndAlso s(0) = ChrW(65279) Then s = s.Substring(1)
        Dim content As String = ""
        For Each line As String In s.Split({ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
            If line.Trim().Length = 0 Then Continue For
            If content.Length > 0 Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.FileLines, "more than one non-empty line")
            content = line
        Next
        Return Parse(content)
    End Function

    Private Shared Function NotAString(s As String) As ExchangeConnectionStringResult
        If s.Length >= 9 AndAlso s.StartsWith("FMSEXCH", StringComparison.Ordinal) AndAlso s(7) >= "2"c AndAlso s(7) <= "9"c AndAlso s(8) = ":"c Then
            Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.NewerMajor, "a later major of the string")
        End If
        Dim looks As String = ""
        If s.StartsWith("FMSSFTP", StringComparison.Ordinal) Then
            looks = ExchangeConnectionStringReason.LooksShareCode
        ElseIf s.IndexOf("://", StringComparison.Ordinal) >= 0 Then
            looks = ExchangeConnectionStringReason.LooksUrl
        End If
        Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.NotAString, "", looks)
    End Function

    ' ---- the members --------------------------------------------------------------------------------------------

    ''' <summary>Validates the members an operator surface or an entry has and returns the canonical value. An empty
    ''' fingerprint, name or login is absent.</summary>
    Public Shared Function Build(endpoint As String, fingerprint As String, name As String, login As String) As ExchangeConnectionStringResult
        Return Build(endpoint, fingerprint, name, login, "")
    End Function

    ''' <summary>Ticket 224 rule 1: the invitation string is the connection string carrying the invitation - `i` is
    ''' the capability id of the one-time code.</summary>
    Public Shared Function Build(endpoint As String, fingerprint As String, name As String, login As String, invitation As String) As ExchangeConnectionStringResult
        Dim host As String = Nothing
        Dim port As Integer = 0
        Dim failure As ExchangeConnectionStringResult = SplitEndpoint(If(endpoint, ""), host, port)
        If failure IsNot Nothing Then Return failure
        Dim fp As String = ""
        If Not String.IsNullOrEmpty(fingerprint) Then
            If Not IsCanonicalFingerprint(fingerprint) Then
                Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadFingerprint, "SHA256: and 43 canonical standard base64 characters")
            End If
            fp = fingerprint
        End If
        Dim nm As String = ""
        If Not String.IsNullOrEmpty(name) Then
            failure = CheckName(name)
            If failure IsNot Nothing Then Return failure
            nm = name
        End If
        Dim lg As String = ""
        If Not String.IsNullOrEmpty(login) Then
            lg = login.ToLowerInvariant()
            If lg.Length < MinLogin OrElse lg.Length > MaxLogin Then
                Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadLogin, "a login is 3-64 characters")
            End If
            For Each ch As Char In lg
                If Not ((ch >= "a"c AndAlso ch <= "z"c) OrElse (ch >= "0"c AndAlso ch <= "9"c) OrElse ch = "."c OrElse ch = "_"c OrElse ch = "-"c) Then
                    Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadLogin, "a login is a-z, 0-9, dot, underscore and hyphen")
                End If
            Next
        End If
        Dim iv As String = ""
        If Not String.IsNullOrEmpty(invitation) Then
            If Not IsValidInvitation(invitation) Then
                Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadInv, "i is not a valid invitation id")
            End If
            iv = invitation
        End If
        Return ExchangeConnectionStringResult.Ok(New ExchangeConnectionStringValue(host, port, fp, nm, lg, iv))
    End Function

    ''' <summary>The invitation's form (ticket 224 rule 1): 22 characters of base64url that decode to 16 bytes - the
    ''' id form of the wire, the same spelling the Go reader accepts (its <c>checkInvitation</c>). The URL-safe alphabet
    ''' only: a '/' or '+' is not an id.</summary>
    Public Shared Function IsValidInvitation(text As String) As Boolean
        If text Is Nothing OrElse text.Length <> 22 Then Return False
        For Each c As Char In text
            Dim ok As Boolean = (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse c = "-"c OrElse c = "_"c
            If Not ok Then Return False
        Next
        Dim padded As String = text.Replace("-"c, "+"c).Replace("_"c, "/"c) & "=="
        Try
            Dim raw As Byte() = Convert.FromBase64String(padded)
            If raw.Length <> 16 Then Return False
            ' 22 characters carry 132 bits for 128: the last character's four low bits must be zero (the Go reader is
            ' strict, and .NET alone would accept a second spelling of the same id)
            Dim last As Char = text(21)
            Dim v As Integer = If(last >= "A"c AndAlso last <= "Z"c, AscW(last) - AscW("A"c),
                                If(last >= "a"c AndAlso last <= "z"c, AscW(last) - AscW("a"c) + 26,
                                If(last >= "0"c AndAlso last <= "9"c, AscW(last) - AscW("0"c) + 52,
                                If(last = "-"c, 62, 63))))
            Return (v And 15) = 0
        Catch ex As FormatException
            Return False
        End Try
    End Function

    ''' <summary>True for "SHA256:" and exactly 43 characters of the standard base64 alphabet, no padding, the last one
    ''' carrying no stray bits: one spelling per leaf.</summary>
    Public Shared Function IsCanonicalFingerprint(text As String) As Boolean
        If text Is Nothing OrElse text.Length <> FingerprintPrefix.Length + FingerprintBase64Length Then Return False
        If Not text.StartsWith(FingerprintPrefix, StringComparison.Ordinal) Then Return False
        For i As Integer = FingerprintPrefix.Length To text.Length - 1
            If Base64Alphabet.IndexOf(text(i)) < 0 Then Return False
        Next
        ' 43 characters carry 258 bits for 256: the last character's two low bits must be zero
        Return (Base64Alphabet.IndexOf(text(text.Length - 1)) And 3) = 0
    End Function

    Private Shared Function CheckName(name As String) As ExchangeConnectionStringResult
        Dim count As Integer = 0
        Dim i As Integer = 0
        While i < name.Length
            Dim ch As Char = name(i)
            If Char.IsHighSurrogate(ch) Then
                If i + 1 >= name.Length OrElse Not Char.IsLowSurrogate(name(i + 1)) Then
                    Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadName, "not valid text")
                End If
                i += 1
            ElseIf Char.IsLowSurrogate(ch) Then
                Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadName, "not valid text")
            ElseIf Char.GetUnicodeCategory(ch) = UnicodeCategory.Control Then
                Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadName, "a control character")
            ElseIf IsBidiControl(ch) Then
                ' DEVICE-EXCHANGE item BO: a name could reorder the text around it, so a person reading a pasted string
                ' cannot trust the order it shows; the zero-width joiners stay allowed (emoji sequences need them)
                Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadName, "a bidirectional formatting control")
            End If
            count += 1
            i += 1
        End While
        If count > MaxName Then Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadName, "more than 64 characters")
        Return Nothing
    End Function

    ''' <summary>U+061C, U+200E/F, U+202A..U+202E and U+2066..U+2069: the marks and the embedding, override and isolate
    ''' controls of the bidirectional algorithm.</summary>
    Private Shared Function IsBidiControl(ch As Char) As Boolean
        Dim v As Integer = AscW(ch)
        Return v = &H61C OrElse v = &H200E OrElse v = &H200F OrElse (v >= &H202A AndAlso v <= &H202E) OrElse (v >= &H2066 AndAlso v <= &H2069)
    End Function

    ' ---- the endpoint -------------------------------------------------------------------------------------------

    Private Shared Function BadX(why As String) As ExchangeConnectionStringResult
        Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadX, why)
    End Function

    Private Shared Function SplitEndpoint(s As String, ByRef host As String, ByRef port As Integer) As ExchangeConnectionStringResult
        If s.Length = 0 Then Return BadX("empty")
        Dim portText As String
        If s(0) = "["c Then
            Dim close As Integer = s.IndexOf("]"c)
            If close < 0 OrElse close + 1 >= s.Length OrElse s(close + 1) <> ":"c Then Return BadX("an IPv6 literal is [address]:port")
            Dim literal As String = s.Substring(1, close - 1)
            portText = s.Substring(close + 2)
            Dim canonical As String = Nothing
            If Not TryCanonicalIPv6(literal, canonical) Then Return BadX("the bracketed host is not an IPv6 address without a zone")
            host = canonical
        Else
            Dim colon As Integer = s.LastIndexOf(":"c)
            If colon < 0 Then Return BadX("no port")
            Dim h As String = s.Substring(0, colon)
            portText = s.Substring(colon + 1)
            If h.IndexOf(":"c) >= 0 Then Return BadX("an IPv6 literal needs brackets")
            Dim failure As ExchangeConnectionStringResult = CanonicalHost(h, host)
            If failure IsNot Nothing Then Return failure
        End If
        If portText.Length = 0 OrElse portText.Length > 5 Then Return BadX("the port is 1 to 5 digits")
        For Each ch As Char In portText
            If ch < "0"c OrElse ch > "9"c Then Return BadX("the port is digits only")
        Next
        port = Integer.Parse(portText, CultureInfo.InvariantCulture)
        If port < 1 OrElse port > 65535 Then Return BadX("the port is 1 to 65535")
        Return Nothing
    End Function

    ''' <summary>A DNS name (labels of letters, digits and hyphen, 1-63 characters, no trailing dot) or an IPv4 dotted
    ''' quad; a name of digits and dots is IPv4 or nothing.</summary>
    Private Shared Function CanonicalHost(h As String, ByRef canonical As String) As ExchangeConnectionStringResult
        If h.Length = 0 OrElse h.Length > MaxHost Then Return BadX("the host is empty or too long")
        Dim digitsAndDots As Boolean = True
        For Each ch As Char In h
            If Not ((ch >= "0"c AndAlso ch <= "9"c) OrElse ch = "."c) Then digitsAndDots = False
        Next
        If digitsAndDots Then
            If Not TryCanonicalIPv4(h, canonical) Then Return BadX("a name of digits and dots must be an IPv4 address")
            Return Nothing
        End If
        For Each label As String In h.Split("."c)
            If label.Length = 0 OrElse label.Length > MaxLabel Then Return BadX("a host label is 1 to 63 characters")
            If label(0) = "-"c OrElse label(label.Length - 1) = "-"c Then Return BadX("a host label does not begin or end with a hyphen")
            For Each ch As Char In label
                If Not ((ch >= "a"c AndAlso ch <= "z"c) OrElse (ch >= "A"c AndAlso ch <= "Z"c) OrElse (ch >= "0"c AndAlso ch <= "9"c) OrElse ch = "-"c) Then
                    Return BadX("a host holds only letters, digits and hyphens")
                End If
            Next
        Next
        canonical = h.ToLowerInvariant()
        Return Nothing
    End Function

    Private Shared Function TryCanonicalIPv4(s As String, ByRef canonical As String) As Boolean
        Dim parts() As String = s.Split("."c)
        If parts.Length <> 4 Then Return False
        Dim octets(3) As Integer
        For i As Integer = 0 To 3
            Dim p As String = parts(i)
            If p.Length = 0 OrElse p.Length > 3 Then Return False
            If p.Length > 1 AndAlso p(0) = "0"c Then Return False
            For Each ch As Char In p
                If ch < "0"c OrElse ch > "9"c Then Return False
            Next
            octets(i) = Integer.Parse(p, CultureInfo.InvariantCulture)
            If octets(i) > 255 Then Return False
        Next
        canonical = String.Join(".", octets.Select(Function(o) o.ToString(CultureInfo.InvariantCulture)))
        Return True
    End Function

    ''' <summary>An IPv6 literal without a zone, in its RFC 5952 form (lower case, no leading zeros, the longest run of
    ''' two or more zero groups compressed - the first on a tie; an IPv4-mapped address keeps its dotted tail).</summary>
    Private Shared Function TryCanonicalIPv6(s As String, ByRef canonical As String) As Boolean
        If s.Length < 2 OrElse s.IndexOf("%"c) >= 0 Then Return False
        Dim groups As New List(Of Integer)
        Dim ellipsis As Integer = -1
        Dim rest As String = s
        If rest.StartsWith("::", StringComparison.Ordinal) Then
            ellipsis = 0
            rest = rest.Substring(2)
            If rest.Length = 0 Then
                canonical = "::"
                Return True
            End If
        End If
        While rest.Length > 0
            ' a trailing dotted IPv4 takes two groups
            Dim dot As Integer = rest.IndexOf("."c)
            Dim colon As Integer = rest.IndexOf(":"c)
            If dot >= 0 AndAlso colon < 0 Then
                Dim v4 As String = Nothing
                If Not TryCanonicalIPv4(rest, v4) Then Return False
                Dim o() As String = v4.Split("."c)
                groups.Add(Integer.Parse(o(0), CultureInfo.InvariantCulture) * 256 + Integer.Parse(o(1), CultureInfo.InvariantCulture))
                groups.Add(Integer.Parse(o(2), CultureInfo.InvariantCulture) * 256 + Integer.Parse(o(3), CultureInfo.InvariantCulture))
                rest = ""
                Exit While
            End If
            Dim token As String = If(colon < 0, rest, rest.Substring(0, colon))
            If token.Length = 0 OrElse token.Length > 4 Then Return False
            Dim value As Integer = 0
            For Each ch As Char In token
                Dim d As Integer = "0123456789abcdef".IndexOf(Char.ToLowerInvariant(ch))
                If d < 0 Then Return False
                value = value * 16 + d
            Next
            groups.Add(value)
            If colon < 0 Then
                rest = ""
            Else
                rest = rest.Substring(colon + 1)
                If rest.Length = 0 Then Return False ' a trailing single colon
                If rest(0) = ":"c Then
                    If ellipsis >= 0 Then Return False ' a second ::
                    ellipsis = groups.Count
                    rest = rest.Substring(1)
                    If rest.Length = 0 Then Exit While
                End If
            End If
        End While
        If groups.Count > 8 Then Return False
        If ellipsis >= 0 Then
            If groups.Count = 8 Then Return False ' :: must stand for at least one group
            Dim full As New List(Of Integer)
            For i As Integer = 0 To ellipsis - 1
                full.Add(groups(i))
            Next
            For i As Integer = 0 To 8 - groups.Count - 1
                full.Add(0)
            Next
            For i As Integer = ellipsis To groups.Count - 1
                full.Add(groups(i))
            Next
            groups = full
        ElseIf groups.Count <> 8 Then
            Return False
        End If
        canonical = RenderIPv6(groups)
        Return True
    End Function

    Private Shared Function RenderIPv6(g As List(Of Integer)) As String
        ' IPv4-mapped (::ffff:a.b.c.d) is written with its dotted tail, as the Go side's netip does
        If g(0) = 0 AndAlso g(1) = 0 AndAlso g(2) = 0 AndAlso g(3) = 0 AndAlso g(4) = 0 AndAlso g(5) = &HFFFF Then
            Return "::ffff:" & (g(6) >> 8).ToString(CultureInfo.InvariantCulture) & "." & (g(6) And 255).ToString(CultureInfo.InvariantCulture) & "." &
                   (g(7) >> 8).ToString(CultureInfo.InvariantCulture) & "." & (g(7) And 255).ToString(CultureInfo.InvariantCulture)
        End If
        Dim bestStart As Integer = -1
        Dim bestLen As Integer = 0
        Dim i As Integer = 0
        While i < 8
            If g(i) = 0 Then
                Dim j As Integer = i
                While j < 8 AndAlso g(j) = 0
                    j += 1
                End While
                If j - i > bestLen AndAlso j - i >= 2 Then
                    bestStart = i
                    bestLen = j - i
                End If
                i = j
            Else
                i += 1
            End If
        End While
        Dim sb As New StringBuilder()
        i = 0
        While i < 8
            If i = bestStart Then
                sb.Append("::")
                i += bestLen
            Else
                If sb.Length > 0 AndAlso sb(sb.Length - 1) <> ":"c Then sb.Append(":"c)
                sb.Append(g(i).ToString("x", CultureInfo.InvariantCulture))
                i += 1
            End If
        End While
        Return sb.ToString()
    End Function

    ' ---- writing ------------------------------------------------------------------------------------------------

    ''' <summary>The canonical text: x, fp, n, u in that order, WHATWG-escaped. A display name that would push the text
    ''' over 512 characters is shortened on a scalar value, or left out when not even one fits (DEVICE-EXCHANGE item BO:
    ''' the name is decoration, the rest is the address). Null only when the text exceeds 512 characters even without
    ''' the name (<paramref name="refusal"/> says so).</summary>
    Public Shared Function Format(value As ExchangeConnectionStringValue, ByRef refusal As ExchangeConnectionStringResult) As String
        refusal = Nothing
        If value Is Nothing Then
            refusal = ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.MissingX, "no value")
            Return Nothing
        End If
        Dim check As ExchangeConnectionStringResult = Build(value.Endpoint, value.Fingerprint, value.Name, value.Login, value.Invitation)
        If Not check.Succeeded Then
            refusal = check
            Return Nothing
        End If
        Dim text As String = Compose(value, value.Name)
        If text.Length <= MaxText Then Return text
        If value.Name.Length > 0 Then
            Dim shortened As String = Compose(value, ShortenName(value))
            If shortened.Length <= MaxText Then Return shortened
        End If
        refusal = ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.TooLong, text.Length.ToString(CultureInfo.InvariantCulture) & " characters once encoded")
        Return Nothing
    End Function

    ''' <summary>The members in the one order, with <paramref name="name"/> as the display name ("" leaves n out).</summary>
    Private Shared Function Compose(value As ExchangeConnectionStringValue, name As String) As String
        Dim sb As New StringBuilder(Prefix)
        sb.Append("x=").Append(Escape(value.Endpoint))
        If value.Fingerprint.Length > 0 Then sb.Append("&fp=").Append(Escape(value.Fingerprint))
        If name.Length > 0 Then sb.Append("&n=").Append(Escape(name))
        If value.Login.Length > 0 Then sb.Append("&u=").Append(Escape(value.Login.ToLowerInvariant()))
        If value.Invitation.Length > 0 Then sb.Append("&i=").Append(Escape(value.Invitation))
        Return sb.ToString()
    End Function

    ''' <summary>The longest proper prefix of the name, cut on a scalar value, that lets the whole text fit in
    ''' <see cref="MaxText"/>; "" when not even one scalar value fits. A prefix never ends in a space or a joiner
    ''' (U+200C, U+200D): those would dangle. The Go side's <c>shortenName</c> does the same.</summary>
    Private Shared Function ShortenName(value As ExchangeConnectionStringValue) As String
        Dim name As String = value.Name
        Dim ends As New List(Of Integer)()
        Dim k As Integer = 0
        While k < name.Length
            k += If(Char.IsHighSurrogate(name(k)) AndAlso k + 1 < name.Length, 2, 1)
            ends.Add(k)
        End While
        For idx As Integer = ends.Count - 2 To 0 Step -1
            Dim candidate As String = name.Substring(0, ends(idx)).TrimEnd(" "c, ChrW(&H200C), ChrW(&H200D))
            If candidate.Length > 0 AndAlso Compose(value, candidate).Length <= MaxText Then Return candidate
        Next
        Return ""
    End Function

    ''' <summary>The WHATWG application/x-www-form-urlencoded byte serializer over UTF-8.</summary>
    Public Shared Function Escape(value As String) As String
        Dim sb As New StringBuilder()
        For Each b As Byte In Encoding.UTF8.GetBytes(If(value, ""))
            Dim c As Char = ChrW(b)
            If (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "0"c AndAlso c <= "9"c) OrElse c = "*"c OrElse c = "-"c OrElse c = "."c OrElse c = "_"c Then
                sb.Append(c)
            ElseIf b = 32 Then
                sb.Append("+"c)
            Else
                sb.Append("%"c).Append(UpperHex(b >> 4)).Append(UpperHex(b And 15))
            End If
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Form decoding: % with hex of either case, + as a space; the result must be UTF-8. Null on success is
    ''' the failure slot: <paramref name="decoded"/> holds the text and the return is Nothing, or the return is the
    ''' refusal.</summary>
    Private Shared Function Unescape(s As String, ByRef decoded As String) As ExchangeConnectionStringResult
        Dim bytes As New List(Of Byte)(s.Length)
        Dim i As Integer = 0
        While i < s.Length
            Dim c As Char = s(i)
            If c = "+"c Then
                bytes.Add(32)
            ElseIf c = "%"c Then
                If i + 2 >= s.Length Then
                    Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadEscape, "a percent sign without two hex digits after it")
                End If
                Dim hi As Integer = HexValue(s(i + 1))
                Dim lo As Integer = HexValue(s(i + 2))
                If hi < 0 OrElse lo < 0 Then
                    Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadEscape, "a percent sign not followed by two hex digits")
                End If
                bytes.Add(CByte(hi * 16 + lo))
                i += 2
            Else
                bytes.Add(CByte(AscW(c) And &H7F)) ' the text was checked to be printable ASCII
            End If
            i += 1
        End While
        Try
            decoded = StrictUtf8.GetString(bytes.ToArray())
        Catch ex As ArgumentException
            Return ExchangeConnectionStringResult.Refused(ExchangeConnectionStringReason.BadUtf8, "a decoded value is not UTF-8")
        End Try
        Return Nothing
    End Function

    Private Shared Function HexValue(c As Char) As Integer
        If c >= "0"c AndAlso c <= "9"c Then Return AscW(c) - AscW("0"c)
        If c >= "a"c AndAlso c <= "f"c Then Return AscW(c) - AscW("a"c) + 10
        If c >= "A"c AndAlso c <= "F"c Then Return AscW(c) - AscW("A"c) + 10
        Return -1
    End Function

    Private Shared Function ScalarCount(s As String) As Integer
        Dim n As Integer = 0
        For Each ch As Char In s
            If Not Char.IsLowSurrogate(ch) Then n += 1
        Next
        Return n
    End Function
End Class
#End If
