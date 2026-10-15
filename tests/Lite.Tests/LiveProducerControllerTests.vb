#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Text
Imports Xunit

' Ticket 199 sections 3, 5, 6 and 8: the controller. The gate guard (a shut gate reads nothing, opens
' nothing, writes nothing), producer rule 1 (no address, no broadcast, no descriptor), the port with
' no fallback, one broadcast at a time, and a whole fake broadcast whose hand-off round-trips through
' this product's own reader and whose bytes really cross a socket.

Public Class LiveProducerControllerTests
    Implements IDisposable

    Private ReadOnly _servers As New List(Of LiveBroadcastServer)()

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each s As LiveBroadcastServer In _servers
            s.Dispose()
        Next
    End Sub

    ''' <summary>The full port set, with everything fake: a fake encoder that turns PCM into real ADTS
    ''' frames, a fake microphone, a dictionary hive, a fixed clock and a free port.</summary>
    Private Class FakeHarness

        Public Property GateOn As Boolean = True
        Public ReadOnly Encoder As New FakeEncoder()
        Public ReadOnly Source As New FakeAudioSource()
        Public ReadOnly VideoSource As New FakeVideoSource()
        Public Property VideoKindSeen As LiveProducerSourceKind? = Nothing
        Public ReadOnly Store As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Public Property Port As Integer = LiveTestPorts.FreePort()
        Public Property FailEncoder As Boolean = False
        Public Property EnrolledId As String = ""
        ''' <summary>Section 3.4's tick: video starts only with it while the exchange half does not
        ''' exist, so the video tests turn it on the way the window's checkbox would.</summary>
        Public Property AllowVideo As Boolean = False
        ''' <summary>The address the controller binds and publishes. A fictional one for the refusal
        ''' tests; LiveTestMachine.LanAddress() for the ones that really bind.</summary>
        Public Property Address As String = "192.168.1.50"

        Public Function Ports() As LiveProducerPorts
            Dim harness As FakeHarness = Me
            Return New LiveProducerPorts With {
                .GateEnabled = Function() harness.GateOn,
                .PickAddress = Function() harness.Address,
                .LoadSettings = Function() New LiveProducerSettings With {
                    .AudioPort = harness.Port,
                    .AllowLanVideoViewers = harness.AllowVideo
                },
                .EnrolledDeviceId = Function() harness.EnrolledId,
                .ReadStored = Function(key)
                                  Dim value As String = Nothing
                                  If harness.Store.TryGetValue(key, value) Then Return value
                                  Return Nothing
                              End Function,
                .WriteStored = Sub(key, value) harness.Store(key) = value,
                .CreateEncoder = Function()
                                     harness.Encoder.FailAtStart = If(harness.FailEncoder, "no runtime", "")
                                     Return harness.Encoder
                                 End Function,
                .CreateAudioSource = Function(kind) harness.Source,
                .CreateVideoSource = Function(kind)
                                         harness.VideoKindSeen = kind
                                         Return harness.VideoSource
                                     End Function,
                .ClockMs = Function() 0L,
                .NewToken = Function() "aabbccdd"
            }
        End Function

    End Class

    Private Shared Function MicrophoneRequest(title As String) As LiveStartRequest
        Return New LiveStartRequest With {.Kind = LiveProducerSourceKind.Microphone, .Mode = "AUDIO_ONLY", .Title = title}
    End Function

    <Fact>
    Public Sub A_shut_gate_refuses_the_start_and_touches_nothing()
        Dim harness As New FakeHarness() With {.GateOn = False}
        Dim controller As New LiveBroadcastController(harness.Ports())

        Dim result As LiveStartResult = controller.Start(MicrophoneRequest(""))

        Assert.False(result.Ok)
        Assert.Equal(LiveStartRefusal.GateShut, result.Refusal)
        ' The dormant state (section 8): no listener, no process, no registry value while off.
        Assert.False(controller.State.Running)
        Assert.Empty(harness.Store)
        Assert.False(harness.Encoder.Started)
        Assert.Equal(0, harness.Source.PushedChunks)
        Assert.Equal("", controller.State.DescriptorJson)
    End Sub

    <Fact>
    Public Sub No_LAN_address_means_no_broadcast_and_no_descriptor()
        Dim harness As New FakeHarness()
        Dim ports As LiveProducerPorts = harness.Ports()
        ports.PickAddress = Function() ""
        Dim controller As New LiveBroadcastController(ports)

        Dim result As LiveStartResult = controller.Start(MicrophoneRequest(""))

        Assert.False(result.Ok)
        Assert.Equal(LiveStartRefusal.NoLanAddress, result.Refusal)
        ' Producer rule 1: NEITHER source opens and no descriptor exists.
        Assert.False(harness.Encoder.Started)
        Assert.Equal(0, harness.Source.PushedChunks)
        Assert.Equal("", controller.State.DescriptorJson)
        Assert.Empty(harness.Store)
    End Sub

    <Fact>
    Public Sub A_taken_port_refuses_the_start_with_no_fallback()
        Dim machine As String = LiveTestMachine.LanAddress()
        If machine.Length = 0 Then Return ' NOT RUN: this machine has no LAN address to bind
        Using holder As New LiveTestPorts.PortHolder(machine)
            Dim harness As New FakeHarness() With {.Port = holder.Port, .Address = machine}
            Dim controller As New LiveBroadcastController(harness.Ports())

            Dim result As LiveStartResult = controller.Start(MicrophoneRequest(""))

            Assert.False(result.Ok)
            Assert.Equal(LiveStartRefusal.PortTaken, result.Refusal)
            Assert.False(controller.State.Running)
            ' Nothing else was started either: the refusal undoes the encoder and the source.
            Assert.False(harness.Encoder.Started)
            Assert.Equal(0, harness.Source.PushedChunks)
        End Using
    End Sub

    <Fact>
    Public Sub A_failed_encoder_refuses_the_start_and_releases_everything_it_had_opened()
        Dim machine As String = LiveTestMachine.LanAddress()
        If machine.Length = 0 Then Return ' NOT RUN: this machine has no LAN address to bind
        Dim harness As New FakeHarness() With {.FailEncoder = True, .Address = machine}
        Dim controller As New LiveBroadcastController(harness.Ports())

        Dim result As LiveStartResult = controller.Start(MicrophoneRequest(""))

        Assert.False(result.Ok)
        Assert.Equal(LiveStartRefusal.EncoderUnavailable, result.Refusal)
        Assert.False(controller.State.Running)
        Assert.True(harness.Encoder.Stopped, "the source was opened first and must be released again")
    End Sub

    <Fact>
    Public Sub A_broadcast_runs_and_its_descriptor_round_trips()
        Dim machine As String = LiveTestMachine.LanAddress()
        If machine.Length = 0 Then Return ' NOT RUN: this machine has no LAN address to bind
        Dim harness As New FakeHarness() With {.EnrolledId = "device-7", .Address = machine}
        Dim controller As New LiveBroadcastController(harness.Ports())

        Dim result As LiveStartResult = controller.Start(MicrophoneRequest("Kitchen PC"))
        Assert.True(result.Ok)

        Dim state As LiveProducerState = controller.State
        Assert.True(state.Running)
        Assert.Equal("http://" & machine & ":" & harness.Port.ToString(Globalization.CultureInfo.InvariantCulture) & "/live-audio.aac", state.Url)
        Assert.Equal("device-7", JsonSourceId(state.DescriptorJson))

        ' The three channels carry the SAME description (producer rule 3), and the reader accepts it.
        Dim fromText As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(state.DescriptorJson)
        Dim fromBarcode As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(state.BarcodeText)
        Assert.Equal(LiveReadStatus.Ok, fromText.Status)
        Assert.Equal(LiveReadStatus.Ok, fromBarcode.Status)
        Assert.Equal(fromText.Descriptor.Url, fromBarcode.Descriptor.Url)
        Assert.Equal("Kitchen PC", fromBarcode.Descriptor.Title)
        Assert.Equal("device-7", fromBarcode.Descriptor.SourceId)

        ' The enrolled device id won, so nothing was drawn into the hive.
        Assert.Empty(harness.Store)

        controller.Stop()
        Assert.False(controller.State.Running)
        Assert.True(harness.Encoder.Stopped)
    End Sub

    Private Shared Function JsonSourceId(json As String) As String
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Return result.Descriptor.SourceId
    End Function

    <Fact>
    Public Sub An_unenrolled_device_draws_one_stable_sourceId_into_the_hive()
        Dim machine As String = LiveTestMachine.LanAddress()
        If machine.Length = 0 Then Return ' NOT RUN: this machine has no LAN address to bind
        Dim harness As New FakeHarness() With {.Address = machine}
        Dim controller As New LiveBroadcastController(harness.Ports())
        Assert.True(controller.Start(MicrophoneRequest("")).Ok)
        Assert.Single(harness.Store)
        Assert.True(harness.Store.ContainsKey(LiveProducerContract.SourceIdRegistryValue))
        Dim first As String = harness.Store(LiveProducerContract.SourceIdRegistryValue)
        Assert.StartsWith("fmsw-", first)
        controller.Stop()

        ' The next broadcast keeps it: stable per device.
        Assert.True(controller.Start(MicrophoneRequest("")).Ok)
        Assert.Equal(first, harness.Store(LiveProducerContract.SourceIdRegistryValue))
        controller.Stop()
    End Sub

    <Fact>
    Public Sub One_live_broadcast_at_a_time_the_second_start_is_refused()
        Dim machine As String = LiveTestMachine.LanAddress()
        If machine.Length = 0 Then Return ' NOT RUN: this machine has no LAN address to bind
        Dim harness As New FakeHarness() With {.Address = machine}
        Dim controller As New LiveBroadcastController(harness.Ports())
        Assert.True(controller.Start(MicrophoneRequest("one")).Ok)

        Dim second As LiveStartResult = controller.Start(MicrophoneRequest("two"))
        Assert.False(second.Ok)
        Assert.Equal(LiveStartRefusal.AlreadyRunning, second.Refusal)
        Assert.Equal("one", LiveBroadcastReader.ReadText(controller.State.DescriptorJson).Descriptor.Title)

        controller.Stop()
        ' After the stop a new broadcast may start.
        Assert.True(controller.Start(MicrophoneRequest("three")).Ok)
        controller.Stop()
    End Sub

    <Fact>
    Public Sub A_whole_fake_broadcast_crosses_a_real_socket_the_producer_side_of_acceptance_1()
        Dim machine As String = LiveTestMachine.LanAddress()
        If machine.Length = 0 Then Return ' NOT RUN: this machine has no LAN address to bind
        Dim harness As New FakeHarness() With {.Address = machine}
        Dim controller As New LiveBroadcastController(harness.Ports())
        Assert.True(controller.Start(MicrophoneRequest("")).Ok)

        ' The capture pushes, the encoder turns PCM into ADTS frames, the hub fans out, the listener
        ' drinks the bytes over a real TCP connection - every layer in place except the device.
        harness.Source.Emit(10)
        Dim port As Integer = controller.State.Port
        System.Threading.Thread.Sleep(150)

        Using client As New TcpClient()
            client.Connect(System.Net.IPAddress.Parse(machine), port)
            Using stream As NetworkStream = client.GetStream()
                Dim request As Byte() = Encoding.ASCII.GetBytes("GET /live-audio.aac HTTP/1.1" & ControlChars.CrLf & "Host: pc" & ControlChars.CrLf & ControlChars.CrLf)
                stream.Write(request, 0, request.Length)
                stream.ReadTimeout = 3000

                Dim head As New List(Of Byte)()
                Dim buffer(16383) As Byte
                Dim headDone As Boolean = False
                While Not headDone
                    Dim read As Integer = stream.Read(buffer, 0, buffer.Length)
                    If read <= 0 Then Exit While
                    For i As Integer = 0 To read - 1
                        head.Add(buffer(i))
                    Next
                    Dim text As String = Encoding.ASCII.GetString(head.ToArray())
                    If text.Contains(ControlChars.CrLf & ControlChars.CrLf) Then headDone = True
                End While
                Dim headText As String = Encoding.ASCII.GetString(head.ToArray())
                Assert.StartsWith("HTTP/1.1 200 OK", headText)
                Assert.Contains("Content-Type: audio/aac", headText)
                Assert.Contains("Transfer-Encoding: chunked", headText)
                Assert.Contains("Accept-Ranges: none", headText)
                Assert.Contains("icy-br: 128", headText)
                Assert.DoesNotContain("icy-metaint", headText, StringComparison.OrdinalIgnoreCase)

                ' More capture flows while the listener is attached: the listener receives it.
                Dim before As Integer = harness.Source.PushedChunks
                harness.Source.Emit(6)
                Dim body As New List(Of Byte)()
                Dim deadline As Date = Date.UtcNow.AddSeconds(5)
                While Date.UtcNow < deadline AndAlso body.Count < 300
                    Try
                        Dim read As Integer = stream.Read(buffer, 0, buffer.Length)
                        If read <= 0 Then Exit While
                        For i As Integer = 0 To read - 1
                            body.Add(buffer(i))
                        Next
                    Catch ex As Exception
                        Exit While
                    End Try
                End While

                Dim bodyText As String = Encoding.ASCII.GetString(body.ToArray())
                Assert.True(body.Count > 0, "the listener must receive bytes after the attach")

                ' Unwrap the chunked body and count whole ADTS frames: frames decode.
                Dim payload As New List(Of Byte)()
                Dim all As Byte() = body.ToArray()
                Dim i2 As Integer = 0
                While i2 < all.Length
                    Dim lineEnd As Integer = Array.IndexOf(all, CByte(10), i2)
                    If lineEnd < 0 Then Exit While
                    Dim lengthText As String = Encoding.ASCII.GetString(all, i2, lineEnd - i2 - 1).Trim()
                    Dim length As Integer
                    If Not Integer.TryParse(lengthText, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, length) Then Exit While
                    If length = 0 Then Exit While
                    Dim chunk(length - 1) As Byte
                    If lineEnd + 1 + length > all.Length Then Exit While
                    Array.Copy(all, lineEnd + 1, chunk, 0, length)
                    payload.AddRange(chunk)
                    i2 = lineEnd + 1 + length + 2
                End While
                Assert.True(payload.Count > 0, "at least one whole chunked frame reached the listener")
                Assert.True(LiveAdtsFrames.CountFrames(payload.ToArray()) > 0, "the payload decodes as ADTS frames")

                ' The listener count is visible to the window's state (safety rule 2).
                Dim counted As Integer = 0
                Dim waited As Integer = 0
                While counted = 0 AndAlso waited < 50
                    counted = controller.State.Listeners
                    System.Threading.Thread.Sleep(50)
                    waited += 1
                End While
                Assert.True(counted >= 1, "the controller counts the attached listener")
            End Using
        End Using

        controller.Stop()
        Assert.False(controller.State.Running)
        ' Acceptance 3: after the stop the port is gone.
        Dim refused As Boolean = False
        Try
            Using probe As New TcpClient()
                probe.Connect(System.Net.IPAddress.Parse(machine), port)
            End Using
        Catch ex As SocketException
            refused = True
        End Try
        Assert.True(refused, "no port remains after the stop")
    End Sub

    <Fact>
    Public Sub A_video_start_without_the_LAN_tick_is_refused_until_the_exchange_half_exists()
        ' Section 3.4: video starts exchange-only (173) unless the user ticks "Allow viewers on my
        ' network". With the tick off and 173 not built, there is no reachable endpoint of any
        ' transport, and producer rule 1 as amended says no start.
        Dim harness As New FakeHarness()
        Dim controller As New LiveBroadcastController(harness.Ports())
        Dim request As New LiveStartRequest With {.Kind = LiveProducerSourceKind.Screen, .Mode = "VIDEO_AUDIO"}

        Dim result As LiveStartResult = controller.Start(request)
        Assert.False(result.Ok)
        Assert.Equal(LiveStartRefusal.NoLanAddress, result.Refusal)
        Assert.False(harness.Encoder.Started)
    End Sub

    <Fact>
    Public Sub Exchange_only_video_uses_a_private_listener_and_cleans_up_after_publication_errors()
        Dim harness As New FakeHarness()
        harness.VideoSource.FramesPerSecond = 15
        harness.Encoder.EmitVideoOnStart = True
        Dim ports As LiveProducerPorts = harness.Ports()
        ports.ExchangeEndpointInfo = Function() Tuple.Create("relay.example:443", "sha256:" & New String("a"c, 64))
        ports.BroadcastStarted = Sub(state, audio, video)
                                     Assert.Equal(0, audio.RetainedSegments)
                                     Assert.Equal(1, video.RetainedSegments)
                                 End Sub
        Dim controller As New LiveBroadcastController(ports)
        Try
            Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Camera, .Mode = "VIDEO_ONLY"})
            Assert.True(result.Ok, result.SeamReason)
            Assert.Equal("127.0.0.1", controller.State.ListenerAddress)
            Assert.Equal(15, harness.Encoder.LastOptions.VideoFps)
            Dim descriptor As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(controller.State.DescriptorJson)
            Assert.DoesNotContain(descriptor.Descriptor.Endpoints, Function(endpoint) endpoint.Transport = "HTTP")
            Using client As New TcpClient()
                client.Connect(IPAddress.Loopback, harness.Port)
            End Using
            Using client As New TcpClient()
                Assert.Throws(Of SocketException)(Sub() client.Connect(IPAddress.Parse(harness.Address), harness.Port))
            End Using
            ports.BroadcastStopped = Sub(id)
                                         Throw New IO.IOException("publication unavailable")
                                     End Sub
        Finally
            controller.Stop()
        End Try
        Assert.True(harness.VideoSource.Stopped)
        Assert.True(harness.Encoder.Stopped)
        Assert.False(controller.State.Running)
        Using client As New TcpClient()
            Assert.Throws(Of SocketException)(Sub() client.Connect(IPAddress.Loopback, harness.Port))
        End Using
    End Sub

    <Fact>
    Public Sub A_protected_exchange_broadcast_never_advertises_the_unprotected_relay()
        Dim harness As New FakeHarness()
        Dim ports As LiveProducerPorts = harness.Ports()
        ports.ExchangeEndpointInfo = Function() Tuple.Create("relay.example:443", "sha256:" & New String("a"c, 64))
        Dim controller As New LiveBroadcastController(ports)
        Try
            Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Screen,
                .Mode = "VIDEO_ONLY", .Password = "separate-secret"})
            Assert.True(result.Ok, result.SeamReason)
            Dim descriptor As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(controller.State.DescriptorJson)
            Assert.Equal("password-v1", descriptor.Descriptor.AuthScheme)
            Assert.Equal("", controller.State.RelayUrl)
            Assert.Single(descriptor.Descriptor.Endpoints)
            Assert.Equal("TUNNEL", descriptor.Descriptor.Endpoints(0).Transport)
            Assert.DoesNotContain("separate-secret", controller.State.BarcodeText)
            Assert.DoesNotContain("separate-secret", controller.State.DescriptorJson)
        Finally
            controller.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub A_throwing_source_factory_does_not_leave_an_encoder_or_listener_open()
        Dim harness As New FakeHarness()
        Dim ports As LiveProducerPorts = harness.Ports()
        ports.ExchangeEndpointInfo = Function() Tuple.Create("relay.example:443", "sha256:" & New String("a"c, 64))
        ports.CreateVideoSource = Function(kind)
                                      Throw New InvalidOperationException("capture unavailable")
                                  End Function
        Dim controller As New LiveBroadcastController(ports)
        Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Screen, .Mode = "VIDEO_ONLY"})
        Assert.False(result.Ok)
        Assert.False(controller.State.Running)
        Assert.Equal("", controller.State.DescriptorJson)
        Assert.True(harness.Encoder.Stopped)
        Using client As New TcpClient()
            Assert.Throws(Of SocketException)(Sub() client.Connect(IPAddress.Loopback, harness.Port))
        End Using
    End Sub

    <Fact>
    Public Sub Exchange_only_start_without_a_lan_address_uses_the_documentation_host_in_inner()
        Dim harness As New FakeHarness With {.Address = ""}
        Dim ports As LiveProducerPorts = harness.Ports()
        ports.ExchangeEndpointInfo = Function() Tuple.Create("relay.example:443", "sha256:" & New String("a"c, 64))
        Dim controller As New LiveBroadcastController(ports)
        Try
            Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Screen, .Mode = "VIDEO_ONLY"})
            Assert.True(result.Ok, result.SeamReason)
            Assert.Equal("127.0.0.1", controller.State.ListenerAddress)
            Dim descriptor As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(controller.State.DescriptorJson)
            Assert.DoesNotContain(descriptor.Descriptor.Endpoints, Function(endpoint) endpoint.Transport = "HTTP")
            Assert.Contains("http://192.0.2.1:", controller.State.DescriptorJson)
            Assert.DoesNotContain("127.0.0.1", controller.State.DescriptorJson)
        Finally
            controller.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub A_camera_start_opens_the_camera_source_of_the_asked_kind()
        ' Phase B3: the camera is a video source like the screen - the same seam carries the KIND
        ' to the host's factory, so the source that opens is the camera's and nothing else, and
        ' VIDEO_ONLY opens no audio source at all.
        Dim machine As String = LiveTestMachine.LanAddress()
        If machine.Length = 0 Then Return ' NOT RUN: this machine has no LAN address to bind
        Dim harness As New FakeHarness() With {.AllowVideo = True, .Address = machine}
        Dim controller As New LiveBroadcastController(harness.Ports())
        Dim request As New LiveStartRequest With {
            .Kind = LiveProducerSourceKind.Camera, .Mode = "VIDEO_ONLY", .Title = "doorbell"
        }

        Dim result As LiveStartResult = controller.Start(request)
        Assert.True(result.Ok)

        ' The factory was asked for the CAMERA, and the camera's source really started.
        Assert.Equal(LiveProducerSourceKind.Camera, harness.VideoKindSeen)
        Assert.True(harness.VideoSource.Started)
        Assert.Equal("fake camera", controller.State.SourceName)

        ' VIDEO_ONLY: no audio source opened, the encoder was told the video shape.
        Assert.Equal(0, harness.Source.PushedChunks)
        Assert.True(harness.Encoder.LastOptions.WithVideo)
        Assert.False(harness.Encoder.LastOptions.WithAudio)

        ' The descriptor is the video mode's, and the video URL carries the opaque token (item H).
        Assert.Contains("/aabbccdd/live.ts", controller.State.Url)

        controller.Stop()
        Assert.False(controller.State.Running)
        Assert.True(harness.VideoSource.Stopped)
        Assert.True(harness.Encoder.Stopped)
    End Sub

End Class
#End If
