#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Security.Authentication
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 180 T-1. The only place this library opens a socket to an exchange server, and it opens TLS only.
' There is no plain stream anywhere in the client (DEVICE-EXCHANGE 7.1 and item V of section 15): not for an
' outsider `connect`, not on a failed handshake, not when an endpoint states no fingerprint.
'
' Trust is item D of section 14.3 with the paired pin of section 19 AY:
'   - the platform trust store judges the chain for the host name first; a leaf it accepts is NOT pinned (a
'     renewal on the CA's schedule must never lock the device out);
'   - a leaf it rejects (self-signed, a private CA, a name mismatch) is accepted only when its fingerprint
'     (SHA256: plus base64 without padding, over the DER leaf) equals the pin the user accepted, so the pin is
'     checked on the very connection that carries the secret and not on an earlier probe;
'   - an entry that already knows the accepted leaf's key accepts a renewed leaf with that key and updates
'     its stored leaf; a changed key is a named stop (PinMismatch), never silently accepted.

''' <summary>What the trust decision came to.</summary>
Friend Enum ExchangeTrustVerdict
    ''' <summary>The platform validated the chain for the host name: nothing is pinned or recorded.</summary>
    PlatformValid = 0
    ''' <summary>The platform did not accept the leaf, and it is the pinned one.</summary>
    PinMatch = 1
    ''' <summary>A pin exists and this leaf is another one.</summary>
    PinMismatch = 2
    ''' <summary>The platform did not accept the leaf and there is no pin: only the user can accept its fingerprint.</summary>
    Unpinned = 3
End Enum

''' <summary>Why a connection could not be made.</summary>
Friend Enum ExchangeConnectFailure
    ''' <summary>No TCP connection: refused, unreachable, reset, a name that does not resolve.</summary>
    Network = 1
    ''' <summary>The handshake failed for a reason other than trust (a peer that does not speak TLS, a protocol floor).</summary>
    TlsFailed = 2
    ''' <summary>A pin exists and the certificate presented differs.</summary>
    PinMismatch = 3
    ''' <summary>No pin and the platform rejects the certificate: it needs the user's acceptance first.</summary>
    Untrusted = 4
    ''' <summary>The caller cancelled.</summary>
    Cancelled = 5
    ''' <summary>The attempt ran out of time.</summary>
    TimedOut = 6
End Enum

Friend NotInheritable Class ExchangeConnectException
    Inherits Exception

    Public ReadOnly Property Failure As ExchangeConnectFailure
    ''' <summary>The fingerprint of the certificate presented, when the handshake got that far ("" otherwise).</summary>
    Public ReadOnly Property Fingerprint As String

    Public Sub New(failure As ExchangeConnectFailure, fingerprint As String, message As String, Optional inner As Exception = Nothing)
        MyBase.New(message, inner)
        Me.Failure = failure
        Me.Fingerprint = If(fingerprint, "")
    End Sub
End Class

''' <summary>What a handshake showed, with no byte of envelope sent: the first-contact fetch of the enrollment dialog.</summary>
Friend NotInheritable Class ExchangeCertificateInfo
    ''' <summary>"SHA256:" plus base64 without padding, over the DER leaf.</summary>
    Public ReadOnly Fingerprint As String
    ''' <summary>The platform trust store accepted the chain for the host name (nothing to pin).</summary>
    Public ReadOnly PlatformValid As Boolean

    Public Sub New(fingerprint As String, platformValid As Boolean)
        Me.Fingerprint = fingerprint
        Me.PlatformValid = platformValid
    End Sub
End Class

''' <summary>An open TLS stream and what was learned on the way.</summary>
Friend NotInheritable Class ExchangeTlsConnection
    Implements IDisposable

    Public ReadOnly Stream As Stream
    Public ReadOnly Fingerprint As String
    Public ReadOnly Verdict As ExchangeTrustVerdict
    Public ReadOnly KeyPin As String

    Public Sub New(stream As Stream, fingerprint As String, verdict As ExchangeTrustVerdict, Optional keyPin As String = "")
        Me.Stream = stream
        Me.Fingerprint = fingerprint
        Me.Verdict = verdict
        Me.KeyPin = keyPin
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Stream?.Dispose()
        Catch
        End Try
    End Sub
End Class

''' <summary>The way to a TLS stream, behind an interface so the stream-level tests can hand back a pipe of their own.</summary>
Friend Interface IExchangeTransport
    ''' <summary>Opens TLS to host:port. <paramref name="pin"/> is the fingerprint the user accepted ("" for none).
    ''' Throws <see cref="ExchangeConnectException"/>; never returns a stream whose certificate was not accepted.</summary>
    Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection)
End Interface

''' <summary>The fingerprint form and the trust rule, pure.</summary>
Friend NotInheritable Class ExchangeTrust

    Private Sub New()
    End Sub

    Public Const FingerprintPrefix As String = "SHA256:"

    ''' <summary>"SHA256:" plus base64 (standard alphabet, no padding) of the SHA-256 of the DER leaf (7.1).</summary>
    Public Shared Function FingerprintOf(derLeaf As Byte()) As String
        Return FingerprintPrefix & Convert.ToBase64String(SHA256.HashData(derLeaf)).TrimEnd("="c)
    End Function

    ''' <summary>True for the pin form: the prefix and 43 base64 characters (a 32-byte hash without padding).</summary>
    Public Shared Function IsFingerprint(text As String) As Boolean
        If text Is Nothing OrElse Not text.StartsWith(FingerprintPrefix, StringComparison.Ordinal) Then Return False
        Dim body As String = text.Substring(FingerprintPrefix.Length)
        If body.Length <> 43 Then Return False
        For Each c As Char In body
            Dim ok As Boolean = (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse c = "+"c OrElse c = "/"c
            If Not ok Then Return False
        Next
        Return True
    End Function

    ''' <summary>Item D: the platform first, then the pin. A pin that exists and differs wins over a platform acceptance,
    ''' because the pin is a property of the account entry and only the user may replace it.</summary>
    Public Shared Function Evaluate(platformAccepted As Boolean, fingerprint As String, pin As String,
                                    Optional presentedKey As String = "", Optional keyPin As String = "") As ExchangeTrustVerdict
        If Not String.IsNullOrEmpty(pin) Then
            Dim accepted As Boolean = String.Equals(pin, fingerprint, StringComparison.Ordinal) OrElse
                (IsFingerprint(keyPin) AndAlso String.Equals(keyPin, presentedKey, StringComparison.Ordinal))
            Return If(accepted, ExchangeTrustVerdict.PinMatch, ExchangeTrustVerdict.PinMismatch)
        End If
        Return If(platformAccepted, ExchangeTrustVerdict.PlatformValid, ExchangeTrustVerdict.Unpinned)
    End Function

End Class

''' <summary>"host:port" as the account entry holds it (an IPv6 literal in brackets).</summary>
Friend NotInheritable Class ExchangeServerAddress

    Public ReadOnly Host As String
    Public ReadOnly Port As Integer

    Public Sub New(host As String, port As Integer)
        Me.Host = host
        Me.Port = port
    End Sub

    Public Overrides Function ToString() As String
        Return If(Host.Contains(":"), "[" & Host & "]", Host) & ":" & Port.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    Public Shared Function TryParse(text As String, ByRef address As ExchangeServerAddress) As Boolean
        address = Nothing
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim t As String = text.Trim()
        Dim host As String
        Dim portText As String
        If t.StartsWith("[", StringComparison.Ordinal) Then
            Dim close As Integer = t.IndexOf("]"c)
            If close < 2 OrElse close + 2 >= t.Length OrElse t(close + 1) <> ":"c Then Return False
            host = t.Substring(1, close - 1)
            portText = t.Substring(close + 2)
        Else
            Dim colon As Integer = t.LastIndexOf(":"c)
            If colon <= 0 OrElse t.IndexOf(":"c) <> colon Then Return False
            host = t.Substring(0, colon)
            portText = t.Substring(colon + 1)
        End If
        Dim port As Integer
        If Not Integer.TryParse(portText, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, port) Then Return False
        If port < 1 OrElse port > 65535 Then Return False
        If host.Length = 0 OrElse host.Length > 253 OrElse host.IndexOfAny({" "c, "/"c, "\"c, "@"c, ControlChars.Tab}) >= 0 Then Return False
        address = New ExchangeServerAddress(host, port)
        Return True
    End Function

End Class

''' <summary>The TLS opening of the one port, over a real socket.</summary>
Friend NotInheritable Class ExchangeTlsTransport
    Implements IExchangeTransport

    Public Shared ReadOnly Instance As New ExchangeTlsTransport()

    ''' <summary>The whole attempt - connect and handshake - gets this long.</summary>
    Public Shared ReadOnly ConnectTimeout As TimeSpan = TimeSpan.FromSeconds(15)

    Private Sub New()
    End Sub

    Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
        Return OpenAsync(host, port, pin, False, ct, "")
    End Function

    ''' <summary>The handshake alone: what certificate does this server show, and does the platform accept it? Not a
    ''' byte of envelope is sent and the connection is closed. The fingerprint is only ever PRESENTED to the user by
    ''' this; trust is decided on the connection that carries the secret.</summary>
    Public Shared Async Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
        Using conn As ExchangeTlsConnection = Await OpenAsync(host, port, "", True, ct).ConfigureAwait(False)
            Return New ExchangeCertificateInfo(conn.Fingerprint, conn.Verdict = ExchangeTrustVerdict.PlatformValid)
        End Using
    End Function

    Friend Function ConnectPinnedAsync(host As String, port As Integer, pin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection)
        Return OpenAsync(host, port, pin, False, ct, keyPin)
    End Function

    Private Shared Async Function OpenAsync(host As String, port As Integer, pin As String, acceptAny As Boolean, ct As CancellationToken, Optional keyPin As String = "") As Task(Of ExchangeTlsConnection)
        Dim target As String = If(host.StartsWith("[", StringComparison.Ordinal) AndAlso host.EndsWith("]", StringComparison.Ordinal), host.Substring(1, host.Length - 2), host)
        Dim fingerprint As String = ""
        Dim verdict As ExchangeTrustVerdict = ExchangeTrustVerdict.Unpinned
        Dim sawCertificate As Boolean = False
        Dim presentedKey As String = ""

        Using timeout As New CancellationTokenSource(ConnectTimeout)
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token)
                Dim socket As New Socket(SocketType.Stream, ProtocolType.Tcp)
                Dim ssl As SslStream = Nothing
                Dim handedOver As Boolean = False
                Try
                    Await socket.ConnectAsync(target, port, linked.Token).ConfigureAwait(False)
                    socket.NoDelay = True
                    Dim net As New NetworkStream(socket, True)
                    ssl = New SslStream(net, False, Function(sender As Object, cert As X509Certificate, chain As X509Chain, errors As SslPolicyErrors) As Boolean
                                                        If cert Is Nothing Then Return False
                                                        fingerprint = ExchangeTrust.FingerprintOf(cert.GetRawCertData())
                                                        Using leaf As X509Certificate2 = X509CertificateLoader.LoadCertificate(cert.GetRawCertData())
                                                            presentedKey = ExchangeTrust.FingerprintOf(leaf.PublicKey.ExportSubjectPublicKeyInfo())
                                                        End Using
                                                        sawCertificate = True
                                                        verdict = ExchangeTrust.Evaluate(errors = SslPolicyErrors.None, fingerprint, pin, presentedKey, keyPin)
                                                        If acceptAny Then Return True
                                                        Return verdict = ExchangeTrustVerdict.PlatformValid OrElse verdict = ExchangeTrustVerdict.PinMatch
                                                    End Function)
                    Dim options As New SslClientAuthenticationOptions With {
                        .TargetHost = target,
                        .EnabledSslProtocols = SslProtocols.Tls12 Or SslProtocols.Tls13,
                        .CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }
                    Await ssl.AuthenticateAsClientAsync(options, linked.Token).ConfigureAwait(False)
                    Dim result As New ExchangeTlsConnection(ssl, fingerprint, verdict, presentedKey)
                    ssl = Nothing
                    handedOver = True
                    Return result
                Catch ex As OperationCanceledException
                    If ct.IsCancellationRequested Then Throw New ExchangeConnectException(ExchangeConnectFailure.Cancelled, fingerprint, "cancelled", ex)
                    Throw New ExchangeConnectException(ExchangeConnectFailure.TimedOut, fingerprint, "timed out", ex)
                Catch ex As AuthenticationException
                    If sawCertificate AndAlso verdict = ExchangeTrustVerdict.PinMismatch Then
                        Throw New ExchangeConnectException(ExchangeConnectFailure.PinMismatch, fingerprint, "the certificate is not the pinned one", ex)
                    End If
                    If sawCertificate AndAlso verdict = ExchangeTrustVerdict.Unpinned Then
                        Throw New ExchangeConnectException(ExchangeConnectFailure.Untrusted, fingerprint, "the certificate is not trusted and not pinned", ex)
                    End If
                    Throw New ExchangeConnectException(ExchangeConnectFailure.TlsFailed, fingerprint, "the TLS handshake failed", ex)
                Catch ex As IOException
                    Throw New ExchangeConnectException(If(sawCertificate, ExchangeConnectFailure.TlsFailed, ExchangeConnectFailure.Network), fingerprint, "the connection failed", ex)
                Catch ex As SocketException
                    Throw New ExchangeConnectException(ExchangeConnectFailure.Network, fingerprint, "the connection failed", ex)
                Finally
                    ' On success the stream was handed over (ssl is Nothing) and owns the socket; on any failure both go.
                    If ssl IsNot Nothing Then
                        Try : ssl.Dispose() : Catch : End Try
                        Try : socket.Dispose() : Catch : End Try
                    ElseIf Not handedOver Then
                        Try : socket.Dispose() : Catch : End Try
                    End If
                End Try
            End Using
        End Using
    End Function

End Class

''' <summary>Entry-scoped paired pins (DEVICE-EXCHANGE 19 AY). Legacy leaf-only entries learn the key
''' only after a matching TLS handshake. Probes and mismatches never change stored trust.</summary>
Friend NotInheritable Class ExchangeRenewalTransport
    Implements IExchangeTransport

    Private ReadOnly _inner As IExchangeTransport
    Private ReadOnly _save As Action(Of String, String)
    Private ReadOnly _gate As New Object()
    Private _key As String
    Private _leaf As String

    Public Sub New(inner As IExchangeTransport, leaf As String, key As String, save As Action(Of String, String))
        _inner = inner
        _leaf = leaf
        _key = key
        _save = save
    End Sub

    Public Async Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
        Dim leaf As String, key As String
        SyncLock _gate
            leaf = _leaf
            key = _key
        End SyncLock
        Dim tls As ExchangeTlsTransport = TryCast(_inner, ExchangeTlsTransport)
        Dim conn As ExchangeTlsConnection
        If tls Is Nothing Then
            conn = Await _inner.ConnectAsync(host, port, pin, ct).ConfigureAwait(False)
        Else
            conn = Await tls.ConnectPinnedAsync(host, port, leaf, key, ct).ConfigureAwait(False)
        End If
        If conn.Verdict = ExchangeTrustVerdict.PinMatch AndAlso ExchangeTrust.IsFingerprint(conn.KeyPin) Then
            Try
                SyncLock _gate
                    _save.Invoke(conn.Fingerprint, conn.KeyPin)
                    _leaf = conn.Fingerprint
                    _key = conn.KeyPin
                End SyncLock
            Catch
                conn.Dispose()
                Throw
            End Try
        End If
        Return conn
    End Function
End Class
#End If
