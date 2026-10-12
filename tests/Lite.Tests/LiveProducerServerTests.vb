#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports Xunit

' Ticket 199 sections 3.1, 3.2, 3.4 and 8: the LAN listener, against a raw TCP client in the style of
' 023's mock device. The response of 2.1 byte for byte (status line, headers, chunked body, no
' icy-metaint, a client that asks for metadata gets none); the MPEG-TS path; the port rules; the
' bind to the chosen address only; the 404 of the unticked video listener; the clean stop that
' leaves no port behind.

Public Class LiveProducerWireTests

    <Fact>
    Public Sub The_audio_response_head_is_the_2_1_shape_byte_for_byte()
        Dim head As Byte() = LiveProducerWire.AudioResponseHead("Kitchen PC", "live", 128)
        Dim text As String = Encoding.ASCII.GetString(head)
        Dim expected As String =
            "HTTP/1.1 200 OK" & ControlChars.CrLf &
            "Content-Type: audio/aac" & ControlChars.CrLf &
            "Transfer-Encoding: chunked" & ControlChars.CrLf &
            "Accept-Ranges: none" & ControlChars.CrLf &
            "icy-name: Kitchen PC" & ControlChars.CrLf &
            "icy-genre: live" & ControlChars.CrLf &
            "icy-pub: 1" & ControlChars.CrLf &
            "icy-br: 128" & ControlChars.CrLf &
            ControlChars.CrLf
        Assert.Equal(expected, text)
        Assert.DoesNotContain("icy-metaint", text, StringComparison.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub The_video_response_head_is_the_item_C_shape()
        Dim text As String = Encoding.ASCII.GetString(LiveProducerWire.VideoResponseHead())
        Dim expected As String =
            "HTTP/1.1 200 OK" & ControlChars.CrLf &
            "Content-Type: video/mp2t" & ControlChars.CrLf &
            "Transfer-Encoding: chunked" & ControlChars.CrLf &
            "Cache-Control: no-cache" & ControlChars.CrLf &
            ControlChars.CrLf
        Assert.Equal(expected, text)
        Assert.DoesNotContain("icy-", text, StringComparison.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub A_chunk_carries_its_hex_length_and_the_final_chunk_ends_the_body()
        Dim body As Byte() = New Byte() {1, 2, 3, 4}
        Dim chunk As Byte() = LiveProducerWire.Chunk(body)
        Assert.Equal("4" & ControlChars.CrLf & "...." & ControlChars.CrLf, Encoding.ASCII.GetString(chunk).Replace(Chr(1), "."c).Replace(Chr(2), "."c).Replace(Chr(3), "."c).Replace(Chr(4), "."c))
        Assert.Equal("0" & ControlChars.CrLf & ControlChars.CrLf, Encoding.ASCII.GetString(LiveProducerWire.FinalChunk()))
    End Sub

    <Fact>
    Public Sub The_plain_refusals_carry_no_information()
        Dim text As String = Encoding.ASCII.GetString(LiveProducerWire.SimpleResponse(LiveProducerWire.NotFoundStatusLine))
        Assert.StartsWith("HTTP/1.1 404 Not Found" & ControlChars.CrLf, text)
        Assert.Contains("Content-Length: 0", text)
    End Sub

    <Fact>
    Public Sub A_chunked_payload_of_ADTS_frames_stays_whole()
        Dim stream As Byte() = LiveAdtsBuilder.AdtsStream(44100, 1, 6, 30)
        Dim framed As New List(Of Byte)()
        framed.AddRange(LiveProducerWire.Chunk(stream))
        framed.AddRange(LiveProducerWire.FinalChunk())
        Dim payload As New List(Of Byte)()
        Dim all As Byte() = framed.ToArray()
        Dim i As Integer = 0
        While i < all.Length
            Dim lineEnd As Integer = Array.IndexOf(all, CByte(10), i)
            Dim lengthText As String = Encoding.ASCII.GetString(all, i, lineEnd - i - 1)
            Dim length As Integer = Integer.Parse(lengthText, Globalization.NumberStyles.HexNumber)
            If length = 0 Then Exit While
            Dim buffer(length - 1) As Byte
            Array.Copy(all, lineEnd + 1, buffer, 0, length)
            payload.AddRange(buffer)
            i = lineEnd + 1 + length + 2
        End While
        Assert.Equal(stream.Length, payload.Count)
        Assert.Equal(6, LiveAdtsFrames.CountFrames(payload.ToArray()))
    End Sub

End Class

Public Class LiveBroadcastServerTests
    Implements IDisposable

    Private ReadOnly _servers As New List(Of LiveBroadcastServer)()

    Private Function StartServer(address As IPAddress, port As Integer, config As LiveServerConfig) As LiveBroadcastServer
        Dim server As New LiveBroadcastServer(address, port, config)
        _servers.Add(server)
        Return server
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each s As LiveBroadcastServer In _servers
            s.Dispose()
        Next
    End Sub

    Private Shared Function AudioConfig(hub As LiveBroadcastHub) As LiveServerConfig
        Return New LiveServerConfig With {
            .IcyName = "Kitchen PC",
            .IcyGenre = "live",
            .AudioBitrateKbps = 128,
            .AudioHub = hub
        }
    End Function

    ''' <summary>Connects, sends one request head, and returns the head bytes up to the empty line.</summary>
    Private Shared Async Function RequestResponseHeadAsync(port As Integer, path As String, extraHeaders As String) As Threading.Tasks.Task(Of String)
        Using client As New TcpClient()
            Await client.ConnectAsync(IPAddress.Loopback, port)
            Using stream As NetworkStream = client.GetStream()
                Dim request As String = "GET " & path & " HTTP/1.1" & ControlChars.CrLf &
                                        "Host: 127.0.0.1" & ControlChars.CrLf &
                                        extraHeaders &
                                        ControlChars.CrLf
                Dim bytes As Byte() = Encoding.ASCII.GetBytes(request)
                Await stream.WriteAsync(bytes, 0, bytes.Length)

                Dim head As New List(Of Byte)()
                Dim buffer(1023) As Byte
                While True
                    Dim read As Integer = Await stream.ReadAsync(buffer, 0, buffer.Length)
                    If read <= 0 Then Exit While
                    For i As Integer = 0 To read - 1
                        head.Add(buffer(i))
                    Next
                    Dim text As String = Encoding.ASCII.GetString(head.ToArray())
                    If text.Contains(ControlChars.CrLf & ControlChars.CrLf) Then
                        Return text.Substring(0, text.IndexOf(ControlChars.CrLf & ControlChars.CrLf))
                    End If
                    If head.Count > 8192 Then Exit While
                End While
                Return Encoding.ASCII.GetString(head.ToArray())
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_taken_port_refuses_the_start_with_no_fallback() As Threading.Tasks.Task
        Using holder As New LiveTestPorts.PortHolder()
            Dim now As Long = 0
            Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
            Dim server As LiveBroadcastServer = StartServer(IPAddress.Loopback, holder.Port, AudioConfig(hub))

            Assert.Equal(LiveServerStartResult.PortTaken, server.Start())
            Assert.False(server.IsRunning)
            ' No fallback port exists: nothing else of this server is listening anywhere.
            Assert.False(server.IsRunning)
        End Using
    End Function

    <Fact>
    Public Async Function The_audio_response_is_the_2_1_shape_and_the_client_can_be_anyone_who_asks() As Threading.Tasks.Task
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        hub.Append(LiveAdtsBuilder.AdtsStream(44100, 1, 4, 30))
        Dim port As Integer = LiveTestPorts.FreePort()
        Dim server As LiveBroadcastServer = StartServer(IPAddress.Loopback, port, AudioConfig(hub))
        Assert.Equal(LiveServerStartResult.Ok, server.Start())

        Dim head As String = Await RequestResponseHeadAsync(port, "/live-audio.aac", "")
        Assert.Equal(Encoding.ASCII.GetString(LiveProducerWire.AudioResponseHead("Kitchen PC", "live", 128)), head & ControlChars.CrLf & ControlChars.CrLf)

        ' A client that asks for in-stream metadata gets the same response with none: expected,
        ' not an error (2.1: no icy-metaint, ever).
        Dim askingHead As String = Await RequestResponseHeadAsync(port, "/live-audio.aac", "Icy-MetaData: 1" & ControlChars.CrLf)
        Assert.Equal(head, askingHead)
        Assert.DoesNotContain("icy-metaint", askingHead, StringComparison.OrdinalIgnoreCase)
    End Function

    <Fact>
    Public Async Function The_extension_less_path_is_accepted_and_the_unknown_path_is_not() As Threading.Tasks.Task
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        Dim port As Integer = LiveTestPorts.FreePort()
        Dim server As LiveBroadcastServer = StartServer(IPAddress.Loopback, port, AudioConfig(hub))
        Assert.Equal(LiveServerStartResult.Ok, server.Start())

        Dim aliasHead As String = Await RequestResponseHeadAsync(port, "/live-audio", "")
        Assert.StartsWith("HTTP/1.1 200 OK", aliasHead)
        Assert.Contains("Content-Type: audio/aac", aliasHead)

        Dim missingHead As String = Await RequestResponseHeadAsync(port, "/no-such-path", "")
        Assert.StartsWith("HTTP/1.1 404", missingHead)
    End Function

    <Fact>
    Public Async Function The_video_path_exists_only_when_the_user_allowed_LAN_viewers() As Threading.Tasks.Task
        Dim now As Long = 0
        Dim audioHub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        Dim videoHub As New LiveBroadcastHub(New LiveVideoJoinPolicy(), Function() now)
        videoHub.Append(LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.PmtPacket(), LiveTsBuilder.VideoPesPacket(True)))

        ' Unticked (the default, Q199-3): 404 exactly as if the path did not exist.
        Dim port As Integer = LiveTestPorts.FreePort()
        Dim offConfig As New LiveServerConfig With {.IcyName = "PC", .AudioHub = audioHub, .VideoHub = videoHub, .VideoAllowed = False, .VideoToken = "aabbccdd"}
        Dim offServer As LiveBroadcastServer = StartServer(IPAddress.Loopback, port, offConfig)
        Assert.Equal(LiveServerStartResult.Ok, offServer.Start())
        Dim offHead As String = Await RequestResponseHeadAsync(port, "/aabbccdd/live.ts", "")
        Assert.StartsWith("HTTP/1.1 404", offHead)

        ' Ticked: the token path answers the item C shape; the bare path stays unknown.
        Dim port2 As Integer = LiveTestPorts.FreePort()
        Dim onConfig As New LiveServerConfig With {.IcyName = "PC", .AudioHub = audioHub, .VideoHub = videoHub, .VideoAllowed = True, .VideoToken = "aabbccdd"}
        Dim onServer As LiveBroadcastServer = StartServer(IPAddress.Loopback, port2, onConfig)
        Assert.Equal(LiveServerStartResult.Ok, onServer.Start())
        Dim onHead As String = Await RequestResponseHeadAsync(port2, "/aabbccdd/live.ts", "")
        Assert.StartsWith("HTTP/1.1 200 OK", onHead)
        Assert.Contains("Content-Type: video/mp2t", onHead)
        Assert.Contains("Cache-Control: no-cache", onHead)
        Dim bareHead As String = Await RequestResponseHeadAsync(port2, "/live.ts", "")
        Assert.StartsWith("HTTP/1.1 404", bareHead)
    End Function

    <Fact>
    Public Async Function The_listener_is_bound_to_the_chosen_address_only() As Threading.Tasks.Task
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        Dim port As Integer = LiveTestPorts.FreePort()
        Dim server As LiveBroadcastServer = StartServer(IPAddress.Loopback, port, AudioConfig(hub))
        Assert.Equal(LiveServerStartResult.Ok, server.Start())

        ' The chosen address answers...
        Using reachable As New TcpClient()
            Await reachable.ConnectAsync(IPAddress.Loopback, port)
            Assert.True(reachable.Connected)
        End Using

        ' ...and a different address on the same machine does not: never a wildcard bind.
        Dim other As IPAddress = IPAddress.Parse("127.0.0.2")
        Using stranger As New TcpClient()
            Dim refused As Boolean = False
            Try
                Await stranger.ConnectAsync(other, port)
            Catch ex As SocketException
                refused = True
            End Try
            Assert.True(refused, "the listener must be bound to the one chosen address, not to every interface (section 3.3)")
        End Using
    End Function

    <Fact>
    Public Async Function Stop_ends_every_listener_and_leaves_no_port_behind() As Threading.Tasks.Task
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        Dim port As Integer = LiveTestPorts.FreePort()
        Dim server As LiveBroadcastServer = StartServer(IPAddress.Loopback, port, AudioConfig(hub))
        Assert.Equal(LiveServerStartResult.Ok, server.Start())

        server.Stop()
        Assert.Equal(0, server.ListenerCount)

        Dim refused As Boolean = False
        Try
            Using client As New TcpClient()
                Await client.ConnectAsync(IPAddress.Loopback, port)
            End Using
        Catch ex As SocketException
            refused = True
        End Try
        Assert.True(refused, "acceptance 3: after the stop no port remains")
    End Function

End Class
#End If
