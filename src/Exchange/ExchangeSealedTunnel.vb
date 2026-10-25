#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net.Security
Imports System.Security.Authentication
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 234 slice E1b (DEVICE-EXCHANGE item DM, amends 7.5 and 7.7): the tunnel of a SEALED broadcast runs TLS inside the
' server's own TLS, so the exchange server carries bytes it can neither read nor alter.
'
'   - the producer makes ONE self-signed P-256 certificate per broadcast, in memory, used for nothing else, and answers each
'     TUNNEL connection with a TLS server on the attached stream before it bridges to its own HTTP server (<see cref="ServerStreamAsync"/>);
'   - the consumer's loopback forwarder runs the TLS client and accepts exactly the leaf whose `SHA256:` pin it was handed
'     (the `tunnelPin` of the opened box, item DK, or the outward descriptor's `seal.pin`): no name check, no chain check
'     (<see cref="ClientStreamAsync"/>). A different leaf ends the connection with the state `sealed-failed` and nothing is pumped;
'   - the pin form is the one of 7.1: `SHA256:` plus 43 characters of standard base64 without padding over the DER leaf
'     (<see cref="ExchangeTrust.FingerprintOf"/> is the one implementation), so a pin in the box, in a descriptor and in a vector
'     is the same text;
'   - TLS 1.2 at least (item AX); an SFTP tunnel gets no inner TLS (item DI) and never comes through here.
'
' The private key is never written anywhere: it is made in memory, loaded with the ephemeral key set, and wiped with the
' certificate on Dispose. Nothing in this file logs, prints or keeps a byte of the stream, a key or a pin.

''' <summary>What the consumer's pinned TLS handshake came to.</summary>
Friend Enum ExchangeSealedTunnelOutcome
    ''' <summary>The producer presented the pinned leaf and the handshake finished.</summary>
    Ok = 0
    ''' <summary>The producer presented another leaf (a swapped certificate: a man in the middle, or a stale pin).</summary>
    PinMismatch = 1
    ''' <summary>The pin handed over is not a `SHA256:` pin at all: nothing could be trusted, so nothing is opened.</summary>
    NoPin = 2
    ''' <summary>The handshake failed for another reason (a peer that does not speak TLS, a protocol floor, a reset).</summary>
    TlsFailed = 3
    ''' <summary>The caller's token ended the attempt.</summary>
    Cancelled = 4
    ''' <summary>The handshake did not finish in time.</summary>
    TimedOut = 5
End Enum

''' <summary>The consumer side result: an authenticated TLS stream, or the reason there is none.</summary>
Friend NotInheritable Class ExchangeSealedTunnelClientResult
    Implements IDisposable

    Public ReadOnly Outcome As ExchangeSealedTunnelOutcome
    ''' <summary>The TLS stream over the tunnel connection, only when <see cref="Outcome"/> is Ok. Dispose closes it and the tunnel.</summary>
    Public ReadOnly Stream As Stream

    Public Sub New(outcome As ExchangeSealedTunnelOutcome, stream As Stream)
        Me.Outcome = outcome
        Me.Stream = stream
    End Sub

    Public ReadOnly Property IsOk As Boolean
        Get
            Return Outcome = ExchangeSealedTunnelOutcome.Ok AndAlso Stream IsNot Nothing
        End Get
    End Property

    ''' <summary>The state of item DR this ends in: `sealed-failed` for a pin that does not match, a missing pin and a handshake that
    ''' failed (there is no clear fallback, item DP); "" for success, a cancel and a timeout, which say nothing about the sealing.</summary>
    Public ReadOnly Property StateCode As String
        Get
            Select Case Outcome
                Case ExchangeSealedTunnelOutcome.PinMismatch, ExchangeSealedTunnelOutcome.NoPin, ExchangeSealedTunnelOutcome.TlsFailed
                    Return ConnectionDiagnosisMessages.SealedFailed.Code
                Case Else
                    Return ""
            End Select
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Stream?.Dispose()
        Catch
        End Try
    End Sub

    ''' <summary>Never a member of the stream or of the pin.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedTunnelClientResult " & Outcome.ToString()
    End Function
End Class

''' <summary>The tunnel identity of ONE sealed broadcast (producer side) and the pinned TLS client (consumer side).</summary>
Friend NotInheritable Class ExchangeSealedTunnel
    Implements IDisposable

    ''' <summary>The subject of the certificate and the TLS server name the client asks for: fixed, and naming neither a person, a
    ''' device nor a broadcast. The pin, not this text, is what the consumer trusts.</summary>
    Public Const SubjectName As String = "fms-broadcast"

    ''' <summary>TLS 1.2 at least (item AX); 1.3 where the platform has it.</summary>
    Friend Const Protocols As SslProtocols = SslProtocols.Tls12 Or SslProtocols.Tls13

    ''' <summary>How long a TLS handshake over a tunnel connection may take on either side.</summary>
    Public Shared ReadOnly HandshakeTimeout As TimeSpan = TimeSpan.FromSeconds(15)

    Private ReadOnly _gate As New Object()
    Private _certificate As X509Certificate2
    Private _disposed As Boolean

    ''' <summary>The `SHA256:` pin of the leaf, the value of `tunnelPin` (item DK) and of `seal.pin` of the outward descriptor. The
    ''' pin is public information once handed out; it does not reveal the key.</summary>
    Public ReadOnly Property Pin As String

    ''' <summary>Makes the certificate for one broadcast: ECDSA P-256, self-signed, valid from an hour ago to two days ahead (a
    ''' broadcast that runs longer keeps its pin: the consumer applies no validity check, the pin is the trust).</summary>
    Public Sub New()
        _certificate = CreateCertificate()
        Pin = PinOf(_certificate.RawData)
    End Sub

    ''' <summary>The pin of a DER-encoded leaf: `SHA256:` plus base64 without padding over its SHA-256 (7.1).</summary>
    Public Shared Function PinOf(derLeaf As Byte()) As String
        If derLeaf Is Nothing Then Return ""
        Return ExchangeTrust.FingerprintOf(derLeaf)
    End Function

    ''' <summary>True for the pin form: `SHA256:` and 43 base64 characters.</summary>
    Public Shared Function IsPin(text As String) As Boolean
        Return ExchangeTrust.IsFingerprint(text)
    End Function

    Private Shared Function CreateCertificate() As X509Certificate2
        Using key As ECDsa = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim request As New CertificateRequest("CN=" & SubjectName, key, HashAlgorithmName.SHA256)
            request.CertificateExtensions.Add(New X509BasicConstraintsExtension(False, False, 0, True))
            request.CertificateExtensions.Add(New X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, True))
            request.CertificateExtensions.Add(New X509EnhancedKeyUsageExtension(New OidCollection From {New Oid("1.3.6.1.5.5.7.3.1")}, False))
            Dim now As DateTimeOffset = DateTimeOffset.UtcNow
            Using selfSigned As X509Certificate2 = request.CreateSelfSigned(now.AddHours(-1), now.AddDays(2))
                ' SChannel cannot acquire an EphemeralKeySet server credential. Import with the default temporary key container
                ' on Windows (without PersistKeySet); disposal releases it. The PKCS#12 image is never written and is wiped here.
                Dim image As Byte() = selfSigned.Export(X509ContentType.Pfx)
                Try
                    Return X509CertificateLoader.LoadPkcs12(image, Nothing,
                        If(OperatingSystem.IsWindows(), X509KeyStorageFlags.DefaultKeySet, X509KeyStorageFlags.EphemeralKeySet))
                Finally
                    CryptographicOperations.ZeroMemory(image)
                End Try
            End Using
        End Using
    End Function

    ' --- producer ---

    ''' <summary>Answers one TUNNEL connection: a TLS server on the attached stream, no client certificate. The returned stream owns
    ''' <paramref name="tunnelConnection"/>. A handshake that fails, times out or is cancelled disposes the stream and throws; the
    ''' caller drops the connection and bridges nothing.</summary>
    Public Async Function ServerStreamAsync(tunnelConnection As Stream, ct As CancellationToken) As Task(Of SslStream)
        If tunnelConnection Is Nothing Then Throw New ArgumentNullException(NameOf(tunnelConnection))
        Dim cert As X509Certificate2
        SyncLock _gate
            If _disposed Then
                Try : tunnelConnection.Dispose() : Catch : End Try
                Throw New ObjectDisposedException(NameOf(ExchangeSealedTunnel))
            End If
            cert = _certificate
        End SyncLock

        Dim ssl As New SslStream(tunnelConnection, False)
        Try
            Dim options As New SslServerAuthenticationOptions With {
                .ServerCertificate = cert,
                .ClientCertificateRequired = False,
                .EnabledSslProtocols = Protocols,
                .CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }
            Using timeout As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                timeout.CancelAfter(HandshakeTimeout)
                Await ssl.AuthenticateAsServerAsync(options, timeout.Token).ConfigureAwait(False)
            End Using
            Return ssl
        Catch
            ssl.Dispose()
            Throw
        End Try
    End Function

    ' --- consumer ---

    ''' <summary>Runs the TLS client over <paramref name="tunnelConnection"/> and accepts exactly the leaf whose pin is
    ''' <paramref name="pin"/>. Fails closed: on every outcome but Ok the connection is disposed and no byte of the player's request
    ''' was written (the handshake is the only thing that crossed). Never throws for a bad peer.</summary>
    Public Shared Async Function ClientStreamAsync(tunnelConnection As Stream, pin As String, ct As CancellationToken) As Task(Of ExchangeSealedTunnelClientResult)
        If tunnelConnection Is Nothing Then Throw New ArgumentNullException(NameOf(tunnelConnection))
        If Not IsPin(pin) Then
            Try : tunnelConnection.Dispose() : Catch : End Try
            Return New ExchangeSealedTunnelClientResult(ExchangeSealedTunnelOutcome.NoPin, Nothing)
        End If

        Dim wanted As Byte() = Encoding.ASCII.GetBytes(pin)
        Dim sawLeaf As Boolean = False
        Dim accepted As Boolean = False
        Dim ssl As New SslStream(tunnelConnection, False)
        Try
            Dim options As New SslClientAuthenticationOptions With {
                .TargetHost = SubjectName,
                .EnabledSslProtocols = Protocols,
                .CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                .RemoteCertificateValidationCallback =
                    Function(sender As Object, certificate As X509Certificate, chain As X509Chain, errors As SslPolicyErrors) As Boolean
                        ' Item DM: the pin IS the trust. The certificate is self-signed on purpose and carries no name worth
                        ' checking, so the chain and name errors in `errors` are ignored by design; what is compared is the
                        ' SHA-256 of the DER leaf, in constant time. A different leaf (a man in the middle) is rejected here.
                        sawLeaf = True
                        If certificate Is Nothing Then Return False
                        Dim presented As Byte() = Encoding.ASCII.GetBytes(PinOf(certificate.GetRawCertData()))
                        accepted = CryptographicOperations.FixedTimeEquals(presented, wanted)
                        Return accepted
                    End Function
            }
            Using timeout As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                timeout.CancelAfter(HandshakeTimeout)
                Await ssl.AuthenticateAsClientAsync(options, timeout.Token).ConfigureAwait(False)
            End Using
            If Not accepted Then
                ' defence in depth: a handshake that finished without the callback accepting the pin is never used
                ssl.Dispose()
                Return New ExchangeSealedTunnelClientResult(ExchangeSealedTunnelOutcome.PinMismatch, Nothing)
            End If
            Return New ExchangeSealedTunnelClientResult(ExchangeSealedTunnelOutcome.Ok, ssl)
        Catch ex As OperationCanceledException
            ssl.Dispose()
            Return New ExchangeSealedTunnelClientResult(If(ct.IsCancellationRequested, ExchangeSealedTunnelOutcome.Cancelled, ExchangeSealedTunnelOutcome.TimedOut), Nothing)
        Catch ex As Exception When TypeOf ex Is AuthenticationException OrElse TypeOf ex Is IOException OrElse
                                   TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is InvalidOperationException OrElse
                                   TypeOf ex Is System.Net.Sockets.SocketException
            ssl.Dispose()
            Return New ExchangeSealedTunnelClientResult(If(sawLeaf AndAlso Not accepted, ExchangeSealedTunnelOutcome.PinMismatch, ExchangeSealedTunnelOutcome.TlsFailed), Nothing)
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim cert As X509Certificate2
        SyncLock _gate
            If _disposed Then Return
            _disposed = True
            cert = _certificate
            _certificate = Nothing
        End SyncLock
        Try
            cert?.Dispose()
        Catch
        End Try
    End Sub

    ''' <summary>Never prints the pin or the key.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedTunnel"
    End Function

End Class
#End If
