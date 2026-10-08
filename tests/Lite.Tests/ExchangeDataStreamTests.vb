#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 180 T-1: ExchangeDataStream - the authenticated opening (`hello` / `enroll`) and the three data streams of DEVICE-EXCHANGE
''' 7.5 and 7.6, against a scripted TLS server. What is held: the order of the envelopes, the frame limit of each stream state, that a
''' refusal is a named outcome (never a thrown surprise), and that after `connected` the stream is the service's, byte for byte.
''' </summary>
Public Class ExchangeDataStreamTests

    Private Shared ReadOnly Server As ExchangeServerAddress = New ExchangeServerAddress("127.0.0.1", 1)

    Private Shared Function Target(s As ExchangeStubTlsServer) As ExchangeServerAddress
        Return New ExchangeServerAddress("127.0.0.1", s.Port)
    End Function

    Private Shared Function Creds() As ExchangeDeviceCredentials
        Return New ExchangeDeviceCredentials(ExchangeWireTestKit.IdOf("D"c), ExchangeWireTestKit.TokenOf("T"c))
    End Function

    ''' <summary>The server end of a successful `hello`: reads it, checks its members, answers `welcome`.</summary>
    Private Shared Async Function AnswerHelloAsync(c As ExchangeStubConnection, seen As List(Of String)) As Task
        Dim hello As ExchangeEnvelope = Await c.ReadAsync()
        seen.Add(hello.Type)
        Assert.Equal(ExchangeWireTestKit.IdOf("D"c), hello.Text("deviceId"))
        Assert.Equal(ExchangeWireTestKit.TokenOf("T"c), hello.Text("deviceToken"))
        Await c.WriteAsync(c.Welcome())
        c.Authenticated()
    End Function

    ' ---------------------------------------------------------------- hello

    <Fact>
    Public Async Function Hello_then_welcome_gives_an_authenticated_stream_under_the_larger_limit() As Task
        Dim seen As New List(Of String)
        Using srv As New ExchangeStubTlsServer(Function(c) AnswerHelloAsync(c, seen))
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, Creds(), "26.10.7.1", 30, cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                    Assert.True(result.Welcome.Is("welcome"))
                    Assert.Equal("alice", result.Welcome.Text("account"))
                    Assert.Equal(ExchangeWire.MaxFrameAuthenticated, result.Reader.Limit)
                    Assert.Equal(ExchangeWire.MaxFrameAuthenticated, result.Writer.Limit)
                End Using
            End Using
        End Using
        Assert.Equal({"hello"}, seen.ToArray())
    End Function

    <Theory>
    <InlineData("bad-credentials", "BadCredentials")>
    <InlineData("device-revoked", "DeviceRevoked")>
    <InlineData("version-unsupported", "VersionUnsupported")>
    <InlineData("rate-limited", "RateLimited")>
    <InlineData("capacity", "Capacity")>
    <InlineData("tls-required", "TlsRequired")>
    <InlineData("some-new-reason", "Unknown")>
    Public Async Function A_refusal_of_a_hello_is_a_named_outcome_with_its_reason(reason As String, expectedName As String) As Task
        Dim expected As ExchangeRefusal = DirectCast([Enum].Parse(GetType(ExchangeRefusal), expectedName), ExchangeRefusal)
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Await c.ReadAsync()
                                                   Await c.Refuse(reason)
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, Creds(), "1", 30, cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Refused, result.Outcome)
                    Assert.Equal(expected, result.Refusal)
                    Assert.Null(result.Stream)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_unauthenticated_reply_above_16384_closes_the_stream_it_is_a_protocol_failure() As Task
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Await c.ReadAsync()
                                                   ' a 20000-byte body before authentication: over the pre-auth limit, so the client must not read it
                                                   Dim header As Byte() = {0, 0, &H4E, &H20}
                                                   Await c.Stream.WriteAsync(header, 0, 4)
                                                   Await c.Stream.WriteAsync(New Byte(19999) {}, 0, 20000)
                                                   Await c.Stream.FlushAsync()
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, Creds(), "1", 30, cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Protocol, result.Outcome)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function After_welcome_a_large_frame_is_accepted_and_one_above_262144_ends_the_stream() As Task
        Dim big As New StringBuilder()
        Dim resources As New List(Of String)
        For i As Integer = 0 To 14
            resources.Add("{""resourceId"":""r" & i.ToString() & """,""pad"":""" & New String("p"c, 12000) & """}")
        Next
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Await AnswerHelloAsync(c, New List(Of String))
                                                   Await c.WriteAsync(New ExchangeEnvelope("directory").SetNumber("revision", 3).SetRecords("resources", resources))
                                                   ' then a frame one byte over the authenticated limit
                                                   Dim n As Integer = ExchangeWire.MaxFrameAuthenticated + 1
                                                   Dim header As Byte() = {CByte((n >> 24) And &HFF), CByte((n >> 16) And &HFF), CByte((n >> 8) And &HFF), CByte(n And &HFF)}
                                                   Await c.Stream.WriteAsync(header, 0, 4)
                                                   Await c.Stream.FlushAsync()
                                                   Await Task.Delay(500)
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, Creds(), "1", 30, cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                    Dim directory As ExchangeEnvelope = Await result.Reader.ReadAsync(cts.Token)
                    Assert.Equal(15, directory.Records("resources").Count)   ' about 180 KB: far past 16384, within 262144
                    Dim ex As ExchangeWireException = Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() result.Reader.ReadAsync(cts.Token))
                    Assert.Equal(ExchangeWireError.FrameSize, ex.Error)
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- enroll

    <Fact>
    Public Async Function Enroll_then_enrolled_hands_over_the_authenticated_stream_with_the_token() As Task
        Dim seenPassword As String = ""
        Dim token As String = ExchangeWireTestKit.TokenOf("E"c)
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Dim enroll As ExchangeEnvelope = Await c.ReadAsync()
                                                   seenPassword = enroll.Text("password")
                                                   Await c.WriteAsync(New ExchangeEnvelope("enrolled").SetText("deviceToken", token).SetText("account", "alice").
                                                       SetText("publicEndpoint", "h:1").SetNumber("keepaliveSeconds", 30).SetTextList("features", {"directory"}).SetText("serverVersion", "1"))
                                                   c.Authenticated()
                                                   Dim next1 As ExchangeEnvelope = Await c.ReadAsync()
                                                   Assert.True(next1.Is("list"))
                                               End Function)
            Dim description As New ExchangeDeviceDescription(ExchangeWireTestKit.IdOf("D"c), "PC", ExchangeWire.ProductWindows, "1", "windows 10.0.1", Array.Empty(Of String)(), "")
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeSessions.EnrollAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint,
                    ExchangeMessages.Enroll("alice", "Correct-Horse-Battery-9", "", description, 30), cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                    Assert.True(result.Welcome.Is("enrolled"))
                    Assert.Equal(token, result.Welcome.Text("deviceToken"))
                    Assert.Equal(ExchangeWire.MaxFrameAuthenticated, result.Writer.Limit)
                    ' the stream stays open and framed: the very next envelope goes through it (the control stream is this stream)
                    Await result.Writer.WriteAsync(ExchangeMessages.Simple("list"), cts.Token)
                    Await Task.Delay(200)
                End Using
            End Using
        End Using
        Assert.Equal("Correct-Horse-Battery-9", seenPassword)
    End Function

    ' ---------------------------------------------------------------- connect

    <Fact>
    Public Async Function Connect_resource_is_hello_connect_connected_and_then_the_stream_is_the_services() As Task
        Dim seen As New List(Of String)
        Dim echoed As String = ""
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Await AnswerHelloAsync(c, seen)
                                                   Dim connect As ExchangeEnvelope = Await c.ReadAsync()
                                                   seen.Add(connect.Type & ":" & connect.Text("resourceId"))
                                                   Await c.WriteAsync(New ExchangeEnvelope("connected"))
                                                   ' the tunnelled service speaks first (an SSH banner) in the same TLS stream
                                                   Dim banner As Byte() = Encoding.ASCII.GetBytes("SSH-2.0-OpenSSH_test" & vbCrLf)
                                                   Await c.Stream.WriteAsync(banner, 0, banner.Length)
                                                   Await c.Stream.FlushAsync()
                                                   Dim buf(63) As Byte
                                                   Dim n As Integer = Await c.Stream.ReadAsync(buf, 0, buf.Length, c.Token)
                                                   echoed = Encoding.ASCII.GetString(buf, 0, n)
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeDataStreams.ConnectResourceAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint,
                                                                                                    Creds(), "1", ExchangeWireTestKit.IdOf("R"c), cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                    Dim buf(63) As Byte
                    Dim n As Integer = Await result.Stream.ReadAsync(buf, 0, buf.Length, cts.Token)
                    Assert.StartsWith("SSH-2.0-OpenSSH_test", Encoding.ASCII.GetString(buf, 0, n))   ' not swallowed by the frame reader
                    Dim hello As Byte() = Encoding.ASCII.GetBytes("client-bytes")
                    Await result.Stream.WriteAsync(hello, 0, hello.Length, cts.Token)
                    Await result.Stream.FlushAsync(cts.Token)
                    Await Task.Delay(300)
                End Using
            End Using
        End Using
        Assert.Equal({"hello", "connect:" & ExchangeWireTestKit.IdOf("R"c)}, seen.ToArray())
        Assert.Equal("client-bytes", echoed)
    End Function

    <Fact>
    Public Async Function An_outsider_connect_by_share_id_is_TLS_with_no_hello_and_stays_under_the_pre_auth_limit() As Task
        Dim first As ExchangeEnvelope = Nothing
        Dim readerLimit As Integer = 0
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   first = Await c.ReadAsync()
                                                   readerLimit = c.Reader.Limit
                                                   Await c.WriteAsync(New ExchangeEnvelope("connected"))
                                                   Await Task.Delay(100)
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeDataStreams.ConnectShareAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, ExchangeWireTestKit.IdOf("S"c), cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                    Assert.Equal(ExchangeWire.MaxFramePreAuth, result.Reader.Limit)   ' never authenticated: the pre-auth limit stays in force
                End Using
            End Using
        End Using
        Assert.True(first.Is("connect"))
        Assert.Equal(ExchangeWireTestKit.IdOf("S"c), first.Text("shareId"))
        Assert.False(first.Has("deviceToken"))
        Assert.Equal(ExchangeWire.MaxFramePreAuth, readerLimit)
    End Function

    <Fact>
    Public Async Function An_outsider_connect_by_broadcast_id_carries_the_scheme() As Task
        Dim first As ExchangeEnvelope = Nothing
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   first = Await c.ReadAsync()
                                                   Await c.WriteAsync(New ExchangeEnvelope("connected"))
                                                   Await Task.Delay(100)
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeDataStreams.ConnectBroadcastAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, ExchangeWireTestKit.IdOf("B"c), "rtsp", cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                End Using
            End Using
        End Using
        Assert.Equal(ExchangeWireTestKit.IdOf("B"c), first.Text("broadcastId"))
        Assert.Equal("rtsp", first.Text("scheme"))
    End Function

    <Fact>
    Public Async Function A_refused_connect_is_unavailable_and_closes_the_stream() As Task
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Await c.ReadAsync()
                                                   Await c.Refuse("unavailable")
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeDataStreams.ConnectShareAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, ExchangeWireTestKit.IdOf("S"c), cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Refused, result.Outcome)
                    Assert.Equal(ExchangeRefusal.Unavailable, result.Refusal)
                    Assert.Null(result.Stream)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_server_that_closes_without_answering_is_a_closed_outcome() As Task
        Using srv As New ExchangeStubTlsServer(Function(c) Task.CompletedTask)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeDataStreams.ConnectShareAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, ExchangeWireTestKit.IdOf("S"c), cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Closed, result.Outcome)
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- attach and upload

    <Fact>
    Public Async Function Attach_is_hello_then_attach_with_the_tunnel_id() As Task
        Dim seen As New List(Of String)
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Await AnswerHelloAsync(c, seen)
                                                   Dim attach As ExchangeEnvelope = Await c.ReadAsync()
                                                   seen.Add(attach.Type & ":" & attach.Text("tunnelId"))
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeDataStreams.AttachAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, Creds(), "1", ExchangeWireTestKit.IdOf("N"c), cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                    Await Task.Delay(300)
                End Using
            End Using
        End Using
        Assert.Equal({"hello", "attach:" & ExchangeWireTestKit.IdOf("N"c)}, seen.ToArray())
    End Function

    <Fact>
    Public Async Function Upload_is_hello_upload_upload_accepted_and_then_chunks() As Task
        Dim seen As New List(Of String)
        Dim chunk As ExchangeChunk = Nothing
        Using srv As New ExchangeStubTlsServer(Async Function(c)
                                                   Await AnswerHelloAsync(c, seen)
                                                   Dim up As ExchangeEnvelope = Await c.ReadAsync()
                                                   seen.Add(up.Type & ":" & up.Text("contentType"))
                                                   Await c.WriteAsync(New ExchangeEnvelope("upload-accepted"))
                                                   chunk = Await ExchangeChunks.ReadAsync(c.Stream, c.Token)
                                               End Function)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeDataStreams.UploadAsync(ExchangeTlsTransport.Instance, Target(srv), srv.Fingerprint, Creds(), "1",
                                                                                           ExchangeWireTestKit.IdOf("B"c), "audio/aac", cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Connected, result.Outcome)
                    Dim bytes As Byte() = ExchangeChunks.Build(ExchangeChunks.FlagJoinable Or ExchangeChunks.FlagHeader, {7, 8, 9})
                    Await result.Stream.WriteAsync(bytes, 0, bytes.Length, cts.Token)
                    Await result.Stream.FlushAsync(cts.Token)
                    Await Task.Delay(300)
                End Using
            End Using
        End Using
        Assert.Equal({"hello", "upload:audio/aac"}, seen.ToArray())
        Assert.NotNull(chunk)
        Assert.True(chunk.IsJoinable AndAlso chunk.IsHeader)
        Assert.Equal({CByte(7), CByte(8), CByte(9)}, chunk.Payload)
    End Function

    ' ---------------------------------------------------------------- failures map to outcomes

    <Fact>
    Public Async Function A_pin_mismatch_is_an_outcome_and_a_missing_server_is_a_network_outcome() As Task
        Using srv As New ExchangeStubTlsServer(Function(c) Task.CompletedTask)
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using result As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(ExchangeTlsTransport.Instance, Target(srv), ExchangeTrust.FingerprintOf(New Byte() {1}), Creds(), "1", 30, cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.PinMismatch, result.Outcome)
                    Assert.Equal(srv.Fingerprint, result.Fingerprint)
                End Using
                Using result2 As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(ExchangeTlsTransport.Instance, Target(srv), "", Creds(), "1", 30, cts.Token)
                    Assert.Equal(ExchangeOpenOutcome.Untrusted, result2.Outcome)
                End Using
            End Using
        End Using
        Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
            Using result As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(ExchangeTlsTransport.Instance, New ExchangeServerAddress("127.0.0.1", 1), "", Creds(), "1", 30, cts.Token)
                Assert.Equal(ExchangeOpenOutcome.Network, result.Outcome)
            End Using
        End Using
    End Function

End Class
#End If
