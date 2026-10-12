#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Security.Cryptography
Imports System.Text

' Ticket 180 T-1, the client library of the device exchange (contract DEVICE-EXCHANGE, read at 0.17).
'
' This file holds the numbers and names of the wire in ONE place. Nothing else in the library spells a
' frame limit, a reason, a role or a feature: a changed owner decision costs an edit here. The Go module
' (exchange-server/internal/wire) is the other implementation; the two are held to the same generated
' vectors (ExchangeVectorReplayTests), never to each other's source.
'
' No user-facing text lives in src/Exchange: the library is linked into the viewer and the Share Manager,
' and the words belong to the program that shows them (the localization coverage tests scan these files).

''' <summary>The numbers of the wire (DEVICE-EXCHANGE 7.1, 6, 7.6 and item T of section 15).</summary>
Friend NotInheritable Class ExchangeWire

    Private Sub New()
    End Sub

    ''' <summary>The integer every envelope carries (7.1).</summary>
    Public Const SchemaVersion As Integer = 2

    ''' <summary>The most JSON a frame may carry until the stream is authenticated, in both directions: every
    ''' frame up to and including <c>welcome</c> or <c>enrolled</c>, and every frame of a stream that never
    ''' authenticates (15 item T).</summary>
    Public Const MaxFramePreAuth As Integer = 16 * 1024

    ''' <summary>The most JSON a frame may carry after <c>welcome</c> or <c>enrolled</c>, in both directions
    ''' (15 item T, the owner's answer of 2026-10-07: no paging).</summary>
    Public Const MaxFrameAuthenticated As Integer = 256 * 1024

    ''' <summary>The most one record (a device, a resource, a broadcast, a receiver block) may hold (6).</summary>
    Public Const MaxRecordBytes As Integer = 16 * 1024

    ''' <summary>A relay chunk's length field counts the flag byte plus the payload and is at most 1 MiB (15 item U).</summary>
    Public Const MaxChunkLengthField As Integer = 1048576

    ''' <summary>What this client proposes for the keepalive (7.3); the server clamps it to 10..120 and echoes it.</summary>
    Public Const ProposedKeepaliveSeconds As Integer = 30

    ''' <summary>Three missed intervals are a dead stream (7.3).</summary>
    Public Const KeepaliveWindowIntervals As Integer = 3

    ''' <summary>The id form: 16 random bytes, base64url without padding (2).</summary>
    Public Const IdLength As Integer = 22

    ''' <summary>The token form: 32 random bytes, base64url without padding (5.2).</summary>
    Public Const TokenLength As Integer = 43

    ' --- the product ids of 6.1 ---
    Public Const ProductWindows As String = "fms-windows"

    ' --- the four roles of 6.1 ---
    Public Const RoleResourceProducer As String = "resource-producer"
    Public Const RoleResourceConsumer As String = "resource-consumer"
    Public Const RoleBroadcaster As String = "broadcaster"
    Public Const RoleReceiver As String = "receiver"

    ' --- the features of `welcome` (7.2) ---
    Public Const FeatureDirectory As String = "directory"
    Public Const FeatureTunnel As String = "tunnel"
    Public Const FeatureRelay As String = "relay"
    Public Const FeatureCast As String = "cast"
    Public Const FeaturePairing As String = "pairing"

    ' --- presence (6.1) ---
    Public Const PresenceOnline As String = "online"
    Public Const PresenceOffline As String = "offline"

    ' --- the schemes of a broadcast tunnel (7.5) ---
    Public Const SchemeHttp As String = "http"
    Public Const SchemeRtsp As String = "rtsp"

    ''' <summary>The one resource kind of v1 (6.2).</summary>
    Public Const KindSftpShare As String = "sftp-share"

End Class

''' <summary>The nine reasons of 7.10. An unknown value is <see cref="Unknown"/>, shown as a generic refusal.</summary>
Friend Enum ExchangeRefusal
    Unknown = 0
    BadCredentials = 1
    DeviceRevoked = 2
    VersionUnsupported = 3
    TlsRequired = 4
    IdCollision = 5
    Capacity = 6
    PortUnavailable = 7
    Unavailable = 8
    RateLimited = 9
    ProtectionUnsupported = 10
End Enum

Friend NotInheritable Class ExchangeRefusals

    Private Sub New()
    End Sub

    Private Shared ReadOnly Names As String() = {
        "", "bad-credentials", "device-revoked", "version-unsupported", "tls-required",
        "id-collision", "capacity", "port-unavailable", "unavailable", "rate-limited", "protection-unsupported"}

    ''' <summary>The enum of a wire reason; <see cref="ExchangeRefusal.Unknown"/> for anything else.</summary>
    Public Shared Function Parse(reason As String) As ExchangeRefusal
        If String.IsNullOrEmpty(reason) Then Return ExchangeRefusal.Unknown
        For i As Integer = 1 To Names.Length - 1
            If String.Equals(Names(i), reason, StringComparison.Ordinal) Then Return CType(i, ExchangeRefusal)
        Next
        Return ExchangeRefusal.Unknown
    End Function

    ''' <summary>The wire spelling of a known reason ("" for Unknown).</summary>
    Public Shared Function WireName(refusal As ExchangeRefusal) As String
        Dim i As Integer = CInt(refusal)
        If i < 0 OrElse i >= Names.Length Then Return ""
        Return Names(i)
    End Function

    ''' <summary>Every known reason, in the order of the contract.</summary>
    Public Shared Function All() As ExchangeRefusal()
        Return {ExchangeRefusal.BadCredentials, ExchangeRefusal.DeviceRevoked, ExchangeRefusal.VersionUnsupported,
                ExchangeRefusal.TlsRequired, ExchangeRefusal.IdCollision, ExchangeRefusal.Capacity,
                ExchangeRefusal.PortUnavailable, ExchangeRefusal.Unavailable, ExchangeRefusal.RateLimited, ExchangeRefusal.ProtectionUnsupported}
    End Function

End Class

''' <summary>The id and token forms (DEVICE-EXCHANGE 2, 5.2) and the one way an id is written to a log
''' (7.9: the first 8 hex characters of its SHA-256).</summary>
Friend NotInheritable Class ExchangeIds

    Private Sub New()
    End Sub

    Private Shared Function Base64Url(bytes As Byte()) As String
        Return Convert.ToBase64String(bytes).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
    End Function

    ''' <summary>16 random bytes from the system generator in the id form.</summary>
    Public Shared Function NewId() As String
        Dim b(15) As Byte
        RandomNumberGenerator.Fill(b)
        Return Base64Url(b)
    End Function

    ''' <summary>True for exactly 22 characters of base64url that decode to 16 bytes.</summary>
    Public Shared Function IsValidId(s As String) As Boolean
        Return Decodes(s, ExchangeWire.IdLength, 16)
    End Function

    ''' <summary>True for exactly 43 characters of base64url that decode to 32 bytes.</summary>
    Public Shared Function IsValidToken(s As String) As Boolean
        Return Decodes(s, ExchangeWire.TokenLength, 32)
    End Function

    Private Shared Function Decodes(s As String, length As Integer, bytes As Integer) As Boolean
        If s Is Nothing OrElse s.Length <> length Then Return False
        For Each c As Char In s
            Dim ok As Boolean = (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse c = "-"c OrElse c = "_"c
            If Not ok Then Return False
        Next
        ' The last character carries only the bits that fill the byte count; a non-canonical tail is not an id.
        Dim padded As String = s.Replace("-"c, "+"c).Replace("_"c, "/"c) & If(length Mod 4 = 2, "==", If(length Mod 4 = 3, "=", ""))
        Try
            Dim raw As Byte() = Convert.FromBase64String(padded)
            Return raw.Length = bytes AndAlso String.Equals(Base64Url(raw), s, StringComparison.Ordinal)
        Catch ex As FormatException
            Return False
        End Try
    End Function

    ''' <summary>The only form in which an id, a token's owner or a code may appear in a log: the first
    ''' 8 hex characters of the SHA-256 of its text.</summary>
    Public Shared Function ForLog(value As String) As String
        If String.IsNullOrEmpty(value) Then Return "-"
        Dim h As Byte() = SHA256.HashData(Encoding.UTF8.GetBytes(value))
        Return Convert.ToHexString(h, 0, 4).ToLowerInvariant()
    End Function

End Class
#End If
