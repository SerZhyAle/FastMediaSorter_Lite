#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 180 T-1: the pin-checking TLS stream - item D of DEVICE-EXCHANGE section 14.3 and item V of section 15. Every case runs
''' against a TLS server on 127.0.0.1 with a certificate made for the run; nothing leaves the machine.
''' </summary>
Public Class ExchangeTlsTests

    Private Shared Function Idle() As Func(Of ExchangeStubConnection, Task)
        Return Async Function(c As ExchangeStubConnection) As Task
                   ' hold the connection until the client leaves
                   Dim buf(15) As Byte
                   Await c.Stream.ReadAsync(buf, 0, buf.Length, c.Token)
               End Function
    End Function

    ' ---------------------------------------------------------------- the rule, pure

    <Fact>
    Public Sub The_fingerprint_is_sha256_colon_base64_without_padding_over_the_der_leaf()
        Dim der As Byte() = {1, 2, 3, 4}
        Dim fp As String = ExchangeTrust.FingerprintOf(der)
        Assert.StartsWith("SHA256:", fp)
        Assert.Equal(7 + 43, fp.Length)
        Assert.DoesNotContain("=", fp)
        Assert.Equal("SHA256:" & Convert.ToBase64String(SHA256.HashData(der)).TrimEnd("="c), fp)
        Assert.True(ExchangeTrust.IsFingerprint(fp))
        Assert.False(ExchangeTrust.IsFingerprint("SHA256:short"))
        Assert.False(ExchangeTrust.IsFingerprint("MD5:" & New String("A"c, 43)))
        Assert.False(ExchangeTrust.IsFingerprint(Nothing))
    End Sub

    <Theory>
    <InlineData(True, "", "PlatformValid")>
    <InlineData(False, "", "Unpinned")>
    <InlineData(False, "same", "PinMatch")>
    <InlineData(False, "other", "PinMismatch")>
    <InlineData(True, "other", "PinMismatch")>
    <InlineData(True, "same", "PinMatch")>
    Public Sub Item_D_the_platform_first_and_a_pin_that_exists_and_differs_always_wins(platformAccepted As Boolean, pin As String, expectedName As String)
        Dim expected As ExchangeTrustVerdict = DirectCast([Enum].Parse(GetType(ExchangeTrustVerdict), expectedName), ExchangeTrustVerdict)
        Assert.Equal(expected, ExchangeTrust.Evaluate(platformAccepted, "same", pin))
    End Sub

    ' ---------------------------------------------------------------- over a socket

    <Fact>
    Public Async Function A_pinned_self_signed_certificate_connects_and_the_connection_reports_its_fingerprint() As Task
        Using server As New ExchangeStubTlsServer(Idle())
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using conn As ExchangeTlsConnection = Await ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", server.Port, server.Fingerprint, cts.Token)
                    Assert.Equal(server.Fingerprint, conn.Fingerprint)
                    Assert.Equal(ExchangeTrustVerdict.PinMatch, conn.Verdict)
                    Assert.IsAssignableFrom(Of System.Net.Security.SslStream)(conn.Stream)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_pin_mismatch_is_refused_with_the_fingerprint_that_was_presented_and_nothing_is_sent() As Task
        Dim envelopeBytes As Integer = 0
        Dim handler As Func(Of ExchangeStubConnection, Task) =
            Async Function(c As ExchangeStubConnection) As Task
                Dim buf(15) As Byte
                Try
                    envelopeBytes += Await c.Stream.ReadAsync(buf, 0, buf.Length, c.Token)
                Catch ex As IOException
                    ' the client abandoned the handshake: nothing to read
                End Try
            End Function
        Using server As New ExchangeStubTlsServer(handler)
            Dim wrong As String = ExchangeTrust.FingerprintOf(New Byte() {9, 9, 9})
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
                    Function() ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", server.Port, wrong, cts.Token))
                Assert.Equal(ExchangeConnectFailure.PinMismatch, ex.Failure)
                Assert.Equal(server.Fingerprint, ex.Fingerprint)   ' the new fingerprint, so the user can compare it with the old
            End Using
            Await Task.Delay(300)
            Assert.Equal(0, envelopeBytes)   ' not one application byte reached the server
        End Using
    End Function

    <Fact>
    Public Async Function A_self_signed_certificate_without_a_pin_is_untrusted_the_user_has_not_accepted_it() As Task
        Using server As New ExchangeStubTlsServer(Idle())
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
                    Function() ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", server.Port, "", cts.Token))
                Assert.Equal(ExchangeConnectFailure.Untrusted, ex.Failure)
                Assert.Equal(server.Fingerprint, ex.Fingerprint)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_first_contact_probe_shows_the_fingerprint_and_sends_not_one_byte_of_envelope() As Task
        Dim receivedAfterHandshake As Integer = -1
        Dim handler As Func(Of ExchangeStubConnection, Task) =
            Async Function(c As ExchangeStubConnection) As Task
                Dim buf(15) As Byte
                ' the probe closes without writing: the first read ends (0 bytes, or a reset), never an envelope byte
                Try
                    receivedAfterHandshake = Await c.Stream.ReadAsync(buf, 0, buf.Length, c.Token)
                Catch ex As IOException
                    receivedAfterHandshake = 0
                End Try
            End Function
        Using server As New ExchangeStubTlsServer(handler)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Dim info As ExchangeCertificateInfo = Await ExchangeTlsTransport.ProbeAsync("127.0.0.1", server.Port, cts.Token)
                Assert.Equal(server.Fingerprint, info.Fingerprint)
                Assert.False(info.PlatformValid)   ' self-signed: the platform does not know it
            End Using
            Dim waited As Integer = 0
            While receivedAfterHandshake = -1 AndAlso waited < 100
                Await Task.Delay(50)
                waited += 1
            End While
            Assert.Equal(0, receivedAfterHandshake)
        End Using
    End Function

    <Fact>
    Public Async Function A_peer_that_does_not_speak_TLS_is_never_answered_in_plain() As Task
        Using sink As New ExchangePlainSink()
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
                    Function() ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", sink.Port, "", cts.Token))
                Assert.True(ex.Failure = ExchangeConnectFailure.TlsFailed OrElse ex.Failure = ExchangeConnectFailure.Network, ex.Failure.ToString())
            End Using
            Await Task.Delay(200)
            Dim bytes As Byte() = sink.Received()
            Assert.True(bytes.Length > 0)
            Assert.Equal(CByte(&H16), bytes(0))   ' a TLS handshake record: the first byte of the one port's TLS opening (7.1)
            Assert.Equal(1, sink.Connections)      ' and no second, plain attempt followed it
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_connection_is_a_network_failure_and_a_cancel_is_a_cancel() As Task
        ' a port nobody listens on
        Dim freePort As Integer
        Using sink As New ExchangePlainSink()
            freePort = sink.Port
        End Using
        Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
            Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
                Function() ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", freePort, "", cts.Token))
            Assert.Equal(ExchangeConnectFailure.Network, ex.Failure)
        End Using
        Using server As New ExchangeStubTlsServer(Idle())
            Using cancelled As New CancellationTokenSource()
                cancelled.Cancel()
                Dim ex2 As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
                    Function() ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", server.Port, server.Fingerprint, cancelled.Token))
                Assert.Equal(ExchangeConnectFailure.Cancelled, ex2.Failure)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_certificate_that_changes_between_connections_is_a_named_stop_on_the_second() As Task
        Using server As New ExchangeStubTlsServer(Idle())
            Dim pinned As String = server.Fingerprint
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using conn As ExchangeTlsConnection = Await ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", server.Port, pinned, cts.Token)
                End Using
                server.Certificate = ExchangeWireTestKit.NewSelfSignedCertificate()
                Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
                    Function() ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", server.Port, pinned, cts.Token))
                Assert.Equal(ExchangeConnectFailure.PinMismatch, ex.Failure)
                Assert.NotEqual(pinned, ex.Fingerprint)
            End Using
        End Using
    End Function

    <Fact>
    Public Sub The_transport_never_constructs_a_plain_stream_to_a_server()
        ' item V: no plain fallback anywhere in the client. The only file that opens a socket to an exchange server is ExchangeTls.vb, and it
        ' wraps the network stream in an SslStream before returning it; no other library file builds a NetworkStream (the loopback forwarder
        ' serves a local player, not a server).
        Dim dir As String = Path.Combine(ExchangeWireTestKit.RepoRoot(), "src", "Exchange")
        For Each file As String In Directory.GetFiles(dir, "*.vb")
            Dim name As String = Path.GetFileName(file)
            Dim text As String = System.IO.File.ReadAllText(file)
            If name = "ExchangeTls.vb" Then
                Assert.Contains("New SslStream(", text)
            ElseIf name <> "ExchangeForwarder.vb" AndAlso Not name.StartsWith("ConnectionDiagnosis", StringComparison.OrdinalIgnoreCase) Then
                Assert.DoesNotContain("New NetworkStream", text)
                Assert.DoesNotContain("New TcpClient", text)
                Assert.DoesNotContain("New Socket(", text)
            End If
        Next
    End Sub

End Class
#End If
