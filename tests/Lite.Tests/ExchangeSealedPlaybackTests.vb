#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Public Class ExchangeSealedPlaybackTests
    Private Shared ReadOnly Bid As String = ExchangeWireTestKit.IdOf("B"c)

    ' At the splice boundary this records TLS records, never the stream above SslStream.
    Private NotInheritable Class WireTap
        Inherits Stream
        Private ReadOnly _inner As Stream
        Public ReadOnly Captured As New MemoryStream()
        Public Sub New(inner As Stream)
            _inner = inner
        End Sub
        Public Overrides ReadOnly Property CanRead As Boolean = True
        Public Overrides ReadOnly Property CanWrite As Boolean = True
        Public Overrides ReadOnly Property CanSeek As Boolean = False
        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property
        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException()
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property
        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function
        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub
        Public Overrides Sub Flush()
            _inner.Flush()
        End Sub
        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Return _inner.Read(buffer, offset, count)
        End Function
        Public Overrides Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer, ct As CancellationToken) As Task(Of Integer)
            Return _inner.ReadAsync(buffer, offset, count, ct)
        End Function
        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            Captured.Write(buffer, offset, count)
            _inner.Write(buffer, offset, count)
        End Sub
        Public Overrides Async Function WriteAsync(buffer As Byte(), offset As Integer, count As Integer, ct As CancellationToken) As Task
            Captured.Write(buffer, offset, count)
            Await _inner.WriteAsync(buffer, offset, count, ct)
        End Function
        Public Overrides Function WriteAsync(buffer As ReadOnlyMemory(Of Byte), Optional ct As CancellationToken = Nothing) As ValueTask
            Captured.Write(buffer.Span)
            Return _inner.WriteAsync(buffer, ct)
        End Function
        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _inner.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

    Private NotInheritable Class PlayerHost
        Implements ILiveRunnerHost
        Public ReadOnly Played As New MemoryStream()
        Private _read As Task = Task.CompletedTask
        Public Function StartPlaybackAsync(session As LiveHttpPlaybackSession) As Task Implements ILiveRunnerHost.StartPlaybackAsync
            _read = session.Stream.CopyToAsync(Played)
            Return Task.CompletedTask
        End Function
        Public Function StopPlaybackAsync() As Task Implements ILiveRunnerHost.StopPlaybackAsync
            Return _read
        End Function
        Public Sub OnState(state As LiveSessionState, retries As Integer, elapsed As Long) Implements ILiveRunnerHost.OnState
        End Sub
    End Class

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function Runner_bounds_unknown_epoch_reconnect_and_stops_on_a_bad_tag(badTag As Boolean) As Task
        Dim group = ExchangeRelayRecords.NewKey()
        Using writer As New ExchangeRelaySender(Bid, "audio/aac", 9UI, ExchangeRelayRecords.NewKey(), New Byte()() {group})
            Dim body = writer.SealMedia(Encoding.UTF8.GetBytes("private audio"), 1)
            If badTag Then
                body(body.Length - 1) = body.Last() Xor CByte(1)
                body = writer.BuildHeaderChunk(Array.Empty(Of Byte)()).Concat(body).ToArray()
            End If
            Using device As New LiveMockDevice(Function(c, t)
                                                  c.WriteAscii("HTTP/1.1 200 OK" & vbCrLf & "Content-Type: application/x-fmsx-sealed" & vbCrLf & "Content-Length: " & body.Length & vbCrLf & vbCrLf)
                                                  c.Write(body)
                                                  Return Task.CompletedTask
                                              End Function), deadline As New CancellationTokenSource(TimeSpan.FromSeconds(5))
                Dim ep As New LiveBroadcastEndpoint With {.Transport = "RELAY", .Url = device.Url(), .BroadcastId = Bid,
                    .IsSealed = True, .SealVersion = 1, .SealKey = ExchangeBase64Url.Encode(group)}
                Dim host As New PlayerHost()
                Dim runner As New LiveBroadcastRunner(ep, New LiveReconnectPolicy(), host)
                Assert.Equal(LiveRunEnd.Failed, Await runner.RunAsync(deadline.Token))
                Assert.Equal("sealed-failed", runner.LastOpen.SealedState)
                Assert.Equal(If(badTag, 1, 2), device.AcceptedCount)
                Assert.Equal(0, host.Played.Length)
            End Using
        End Using
    End Function

    <Fact>
    Public Sub Unknown_seal_version_is_skipped_while_a_supported_endpoint_remains_playable()
        Dim json = "{""schemaVersion"":1,""url"":""https://relay.example/v2/b/test/stream"",""mode"":""AUDIO_ONLY"",""endpoints"":[" &
            "{""url"":""https://relay.example/v2/b/test/stream"",""transport"":""RELAY"",""mode"":""AUDIO_ONLY"",""seal"":{""v"":99}}," &
            "{""url"":""http://192.168.1.9/live.aac"",""transport"":""HTTP"",""mode"":""AUDIO_ONLY""}]}"
        Dim result = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal("HTTP", Assert.Single(LiveEndpointSelector.Candidates(result.Descriptor)).Transport)
        Dim unsupported = json.Replace("99", "1")
        Assert.Equal(2, LiveEndpointSelector.Candidates(LiveBroadcastReader.ReadText(unsupported).Descriptor).Count)
    End Sub

    <Fact>
    Public Async Function Session_opens_the_ring_and_gives_only_clear_media_to_the_player() As Task
        Dim group As Byte() = ExchangeRelayRecords.NewKey()
        Dim clear As Byte() = LiveMockDevice.AdtsFrame()
        Using writer As New ExchangeRelaySender(Bid, "audio/aac", 1UI, ExchangeRelayRecords.NewKey(), New Byte()() {group})
            Dim body As Byte() = writer.BuildHeaderChunk(Array.Empty(Of Byte)()).Concat(writer.SealMedia(clear, 1)).ToArray()
            Assert.DoesNotContain(Encoding.ASCII.GetString(clear), Encoding.ASCII.GetString(body))
            Using device As New LiveMockDevice(Function(c, t)
                                                  c.WriteAscii("HTTP/1.1 200 OK" & vbCrLf & "Content-Type: application/x-fmsx-sealed" & vbCrLf & "Content-Length: " & body.Length & vbCrLf & vbCrLf)
                                                  c.Write(body)
                                                  Return Task.CompletedTask
                                              End Function)
                Dim ep As New LiveBroadcastEndpoint With {.Transport = "RELAY", .Url = device.Url(), .BroadcastId = Bid,
                    .IsSealed = True, .SealVersion = 1, .SealKey = ExchangeBase64Url.Encode(group)}
                Using session As New LiveHttpPlaybackSession(ep)
                    Assert.True((Await session.OpenAsync(CancellationToken.None)).IsConnected)
                    Assert.Equal("audio/aac", session.ContentType)
                    Using received As New MemoryStream()
                        Await session.Stream.CopyToAsync(received)
                        Assert.Equal(clear, received.ToArray())
                    End Using
                    Assert.Equal(1, session.RequestCount)
                    Assert.Equal(1L, session.Frames.FrameCount)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Sub A_bad_tag_never_reaches_the_media_stream_and_the_receiver_stays_failed()
        Dim group As Byte() = ExchangeRelayRecords.NewKey()
        Using writer As New ExchangeRelaySender(Bid, "video/mp2t", 3UI, ExchangeRelayRecords.NewKey(), New Byte()() {group}),
              receiver As New ExchangeSealedBroadcastReceiver(Bid, group)
            Dim media As Byte() = writer.SealMedia(Encoding.UTF8.GetBytes("recognisable private media"), 1)
            media(media.Length - 1) = media.Last() Xor CByte(1)
            Using source As New MemoryStream(writer.BuildHeaderChunk(Array.Empty(Of Byte)()).Concat(media).ToArray()), stream As ExchangeSealedMediaStream = receiver.Open(source)
                Assert.Equal(0, stream.Read(New Byte(63) {}, 0, 64))
                Assert.Equal(ExchangeSealedReceiveStatus.SealedFailed, stream.Status)
            End Using
            Assert.True(receiver.IsFailed)
        End Using
    End Sub

    <Fact>
    Public Sub Unknown_epoch_allows_one_reconnect_then_fails_without_a_ring()
        Dim group As Byte() = ExchangeRelayRecords.NewKey()
        Using writer As New ExchangeRelaySender(Bid, "audio/aac", 8UI, ExchangeRelayRecords.NewKey(), New Byte()() {group}),
              receiver As New ExchangeSealedBroadcastReceiver(Bid, group)
            Dim media As Byte() = writer.SealMedia(New Byte() {1, 2, 3}, 1)
            Using stream As ExchangeSealedMediaStream = receiver.Open(New MemoryStream(media))
                Assert.Equal(0, stream.Read(New Byte(7) {}, 0, 8))
                Assert.Equal(ExchangeSealedReceiveStatus.NeedReconnect, stream.Status)
            End Using
            Using stream As ExchangeSealedMediaStream = receiver.Open(New MemoryStream(media))
                Assert.Equal(0, stream.Read(New Byte(7) {}, 0, 8))
                Assert.Equal(ExchangeSealedReceiveStatus.SealedFailed, stream.Status)
            End Using
            Assert.Equal(1, receiver.ReconnectsUsed)
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function Inner_TLS_opens_only_the_pinned_leaf(wrongPin As Boolean) As Task
        Using identity As New ExchangeSealedTunnel(), other As New ExchangeSealedTunnel(), deadline As New CancellationTokenSource(TimeSpan.FromSeconds(8))
            Dim listener As New TcpListener(IPAddress.Loopback, 0)
            listener.Start()
            Try
                Dim port As Integer = DirectCast(listener.LocalEndpoint, IPEndPoint).Port
                Dim sent As Boolean = False
                Dim serverError As String = ""
                Dim capture As Byte() = Array.Empty(Of Byte)()
                Dim privateResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK" & vbCrLf & "private media content 234")
                Dim server As Task = Task.Run(Async Function()
                                                  Using accepted As TcpClient = Await listener.AcceptTcpClientAsync(deadline.Token), tap As New WireTap(accepted.GetStream())
                                                      Try
                                                          Using tls = Await identity.ServerStreamAsync(tap, deadline.Token)
                                                              Dim request(0) As Byte
                                                              If Await tls.ReadAsync(request, deadline.Token) = 1 Then
                                                                  sent = True
                                                                  Await tls.WriteAsync(privateResponse, deadline.Token)
                                                              End If
                                                          End Using
                                                      Catch ex As Exception
                                                          serverError = ex.ToString()
                                                      Finally
                                                          capture = tap.Captured.ToArray()
                                                      End Try
                                                  End Using
                                              End Function)
                Using client As New TcpClient()
                    Await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token)
                    Using opened = Await ExchangeSealedTunnel.ClientStreamAsync(client.GetStream(), If(wrongPin, other.Pin, identity.Pin), deadline.Token)
                        If Not opened.IsOk AndAlso Not wrongPin Then
                            Await server
                            Assert.True(opened.IsOk, opened.Outcome.ToString() & " " & serverError)
                        End If
                        Assert.Equal(Not wrongPin, opened.IsOk)
                        If opened.IsOk Then
                            Await opened.Stream.WriteAsync(New Byte() {1}, deadline.Token)
                            Dim response(privateResponse.Length - 1) As Byte
                            Await opened.Stream.ReadExactlyAsync(response, deadline.Token)
                            Assert.Equal(privateResponse, response)
                        Else
                            Assert.Equal("sealed-failed", opened.StateCode)
                        End If
                    End Using
                End Using
                Await server
                Assert.Equal(Not wrongPin, sent)
                If Not wrongPin Then
                    Assert.NotEmpty(capture)
                    Assert.DoesNotContain("HTTP/1.1 200 OK", Encoding.ASCII.GetString(capture))
                    Assert.DoesNotContain("private media content 234", Encoding.ASCII.GetString(capture))
                End If
            Finally
                listener.Stop()
            End Try
        End Using
    End Function
End Class
#End If
