#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Sockets
Imports System.Text

' Ticket 199 section 8: the shared builders and fakes of the producer tests. No test needs a device:
' ADTS frames, MPEG-TS packets, an encoder and an audio source are all made here, byte for byte in
' the shapes the wire carries.

''' <summary>Builds real ADTS AAC-LC frames (7-byte headers, no CRC), the bytes of the 2.1 payload.</summary>
Public Module LiveAdtsBuilder

    ''' <summary>One ADTS frame: header (sync, MPEG-4, layer none, protection absent), the sample-rate
    ''' index, the channel count, and a payload of the length the header declares.</summary>
    Public Function AdtsFrame(sampleRate As Integer, channels As Integer, payloadLength As Integer) As Byte()
        Dim rateIndex As Integer = RateIndexOf(sampleRate)
        Dim frameLength As Integer = 7 + payloadLength
        Dim f(frameLength - 1) As Byte
        f(0) = &HFF
        f(1) = &HF1                                    ' MPEG-4, layer 0, protection absent (7-byte header)
        f(2) = CByte(((rateIndex And &HF) << 2) Or ((channels >> 2) And &H1))
        f(3) = CByte(((channels And &H3) << 6) Or ((frameLength >> 11) And &H3))
        f(4) = CByte((frameLength >> 3) And &HFF)
        f(5) = CByte(((frameLength And &H7) << 5) Or &H1F) ' buffer fullness high bits
        f(6) = &HFC                                    ' buffer fullness low + one raw data block
        For i As Integer = 7 To frameLength - 1
            f(i) = CByte((i * 31) And &HFF)
        Next
        Return f
    End Function

    Private Function RateIndexOf(sampleRate As Integer) As Integer
        Select Case sampleRate
            Case 96000 : Return 0
            Case 88200 : Return 1
            Case 64000 : Return 2
            Case 48000 : Return 3
            Case 44100 : Return 4
            Case 32000 : Return 5
            Case 24000 : Return 6
            Case 22050 : Return 7
            Case 16000 : Return 8
            Case 12000 : Return 9
            Case 11025 : Return 10
            Case 8000 : Return 11
            Case 7350 : Return 12
            Case Else : Return 4
        End Select
    End Function

    ''' <summary>N frames of the shape, concatenated - a whole segment of the audio stream.</summary>
    Public Function AdtsStream(sampleRate As Integer, channels As Integer, frames As Integer, payloadLength As Integer) As Byte()
        Dim parts As New List(Of Byte())()
        For i As Integer = 1 To frames
            parts.Add(AdtsFrame(sampleRate, channels, payloadLength))
        Next
        Dim total As Integer = 0
        For Each p As Byte() In parts
            total += p.Length
        Next
        Dim out(total - 1) As Byte
        Dim offset As Integer = 0
        For Each p As Byte() In parts
            Array.Copy(p, 0, out, offset, p.Length)
            offset += p.Length
        Next
        Return out
    End Function

End Module

''' <summary>Builds real MPEG-TS packets: PAT, PMT and H.264 PES with or without an IDR - the stream of
''' the item C shape, with PAT/PMT pids and a video pid a test can read back through the chunker.</summary>
Public Module LiveTsBuilder

    Public Const PmtPid As Integer = &H100
    Public Const VideoPid As Integer = &H101
    Public Const AudioPid As Integer = &H102

    Public Function PatPacket() As Byte()
        Dim payload As Byte() = {
            &H0,                                     ' pointer_field
            &H0, &HB0, &HD,                          ' table_id 0, section_length 13
            &H0, &H1,                                ' transport_stream_id
            &HC1, &H0, &H0,                          ' version/current, section 0, last 0
            &H0, &H1,                                ' program_number 1
            CByte(&HE0 Or ((PmtPid >> 8) And &H1F)), CByte(PmtPid And &HFF), ' PMT pid
            &H0, &H0, &H0, &H0                       ' CRC (not checked by the chunker; kept plausible)
        }
        Return Packet(0, True, payload)
    End Function

    Public Function PmtPacket() As Byte()
        Dim payload As Byte() = {
            &H0,                                     ' pointer_field
            &H2, &HB0, &H17,                         ' table_id 2, section_length 23
            &H0, &H1,                                ' program_number 1
            &HC1, &H0, &H0,                          ' version/current, section 0, last 0
            &HE0, &H0,                               ' PCR pid
            &HF0, &H0,                               ' program_info_length 0
            &H1B, &HE1, CByte(VideoPid And &HFF),    ' H.264 video pid
            &HF0, &H0,                               ' ES info length 0
            &HF, &HE1, CByte(AudioPid And &HFF),     ' AAC audio pid
            &HF0, &H0,                               ' ES info length 0
            &H0, &H0, &H0, &H0                       ' CRC
        }
        Return Packet(PmtPid, True, payload)
    End Function

    Public Function VideoPesPacket(withIdr As Boolean) As Byte()
        Dim payload As New List(Of Byte) From {
            &H0, &H0, &H1, &HE0,                      ' PES start, video stream id
            &H0, &H0,                                ' PES packet length (unbounded)
            &H80, &H0                                ' flags, no optional header
        }
        payload.AddRange(New Byte() {&H0, &H0, &H1, &H67}) ' SPS, NAL type 7
        payload.AddRange(New Byte() {&H0, &H0, &H1, &H68}) ' PPS, NAL type 8
        payload.AddRange(New Byte() {&H0, &H0, &H1, If(withIdr, CByte(&H65), CByte(&H41))}) ' IDR (5) or non-IDR (1)
        payload.AddRange(New Byte() {&H33, &H33, &H33})
        Return Packet(VideoPid, True, payload.ToArray())
    End Function

    Public Function NullPacket() As Byte()
        Return Packet(&H1FFF, False, New Byte() {&H0, &H0, &H0, &H0})
    End Function

    Private Function Packet(pid As Integer, payloadUnitStart As Boolean, payload As Byte()) As Byte()
        Dim p(187) As Byte
        p(0) = &H47
        p(1) = CByte(If(payloadUnitStart, &H40, 0) Or ((pid >> 8) And &H1F))
        p(2) = CByte(pid And &HFF)
        p(3) = &H10                                     ' adaptation field control: payload only
        Array.Copy(payload, 0, p, 4, Math.Min(payload.Length, 184))
        Return p
    End Function

    ''' <summary>Concatenates packets into one aligned segment.</summary>
    Public Function Segment(ParamArray packets As Byte()()) As Byte()
        Dim out(packets.Length * 188 - 1) As Byte
        For i As Integer = 0 To packets.Length - 1
            Array.Copy(packets(i), 0, out, i * 188, 188)
        Next
        Return out
    End Function

End Module

''' <summary>A deterministic fake audio source: the sink is stored, chunks are pushed by the test's
''' own hand, never by a timer - no test needs a device and none of them depends on wall time.</summary>
Friend Class FakeAudioSource
    Implements ILiveAudioSource

    Public Property PushedChunks As Integer
    Private _sink As Action(Of LivePcmChunk)

    Public ReadOnly Property DisplayName As String Implements ILiveAudioSource.DisplayName
        Get
            Return "fake microphone"
        End Get
    End Property

    Public ReadOnly Property SampleRate As Integer = 44100 Implements ILiveAudioSource.SampleRate

    Public ReadOnly Property Channels As Integer = 1 Implements ILiveAudioSource.Channels

    Public Property Muted As Boolean = False Implements ILiveAudioSource.Muted

    Public Property Gain As Double = 1.0 Implements ILiveAudioSource.Gain

    Public Function Start(sink As Action(Of LivePcmChunk), ByRef reason As String) As Boolean Implements ILiveAudioSource.Start
        If sink Is Nothing Then
            reason = "no sink"
            Return False
        End If
        _sink = sink
        Return True
    End Function

    Public Sub [Stop]() Implements ILiveAudioSource.Stop
        _sink = Nothing
    End Sub

    ''' <summary>Pushes n chunks of plain 16-bit PCM; the peak rides with the index.</summary>
    Public Sub Emit(chunks As Integer)
        If _sink Is Nothing Then Return
        For i As Integer = 1 To chunks
            Dim data(4410 - 1) As Byte
            Dim value As Short = CShort(Math.Min(Short.MaxValue, i * 2000))
            For j As Integer = 0 To data.Length - 2 Step 2
                data(j) = CByte(value And &HFF)
                data(j + 1) = CByte((value >> 8) And &HFF)
            Next
            PushedChunks += 1
            _sink(New LivePcmChunk With {.Data = data, .Frames = 2205, .Peak = Math.Min(1.0, i / 10.0)})
        Next
    End Sub

End Class

''' <summary>A deterministic fake video source (the screen and camera seams, section 4): frames are
''' pushed by the test's own hand, no device is ever touched.</summary>
Friend Class FakeVideoSource
    Implements ILiveVideoSource

    Public Property Started As Boolean
    Public Property Stopped As Boolean
    Private _sink As Action(Of LiveVideoFrame)

    Public ReadOnly Property DisplayName As String = "fake camera" Implements ILiveVideoSource.DisplayName

    Public ReadOnly Property Width As Integer = 1280 Implements ILiveVideoSource.Width

    Public ReadOnly Property Height As Integer = 720 Implements ILiveVideoSource.Height

    Public ReadOnly Property FramesPerSecond As Integer = 25 Implements ILiveVideoSource.FramesPerSecond

    Public Function Start(sink As Action(Of LiveVideoFrame), ByRef reason As String) As Boolean Implements ILiveVideoSource.Start
        If sink Is Nothing Then
            reason = "no sink"
            Return False
        End If
        _sink = sink
        Started = True
        Return True
    End Function

    Public Sub [Stop]() Implements ILiveVideoSource.Stop
        Stopped = True
        Started = False
        _sink = Nothing
    End Sub

End Class

''' <summary>A deterministic fake encoder: every PCM chunk in becomes one synthetic ADTS frame out,
''' so a whole broadcast runs in a test with real bytes on the wire and no process.</summary>
Friend Class FakeEncoder
    Implements ILiveEncoder

    Public Property Started As Boolean
    Public Property Stopped As Boolean
    Public Property LastOptions As LiveEncoderOptions
    Public Property PcmIn As Integer
    Public Property FailAtStart As String = ""
    Private _output As Action(Of Byte())

    Public Function Start(options As LiveEncoderOptions, output As Action(Of Byte()), ByRef reason As String) As Boolean Implements ILiveEncoder.Start
        If FailAtStart.Length > 0 Then
            reason = FailAtStart
            Return False
        End If
        LastOptions = options
        _output = output
        Started = True
        Return True
    End Function

    Public Sub WritePcm(chunk As LivePcmChunk) Implements ILiveEncoder.WritePcm
        If Not Started OrElse _output Is Nothing OrElse chunk Is Nothing Then Return
        PcmIn += 1
        _output(LiveAdtsBuilder.AdtsFrame(LastOptions.SampleRate, LastOptions.Channels, 24))
    End Sub

    Public Sub WriteVideo(frame As LiveVideoFrame) Implements ILiveEncoder.WriteVideo
    End Sub

    Public Sub [Stop]() Implements ILiveEncoder.Stop
        Stopped = True
        Started = False
    End Sub

End Class

''' <summary>The machine's own publishable IPv4, for the controller tests that really bind a listener
''' (the fake address of the descriptor tests could never be bound). "" when this machine has none - the
''' test then prints NOT RUN and passes, the suite's hand-run pattern.</summary>
Public Module LiveTestMachine

    Public Function LanAddress() As String
        Try
            For Each nic As System.Net.NetworkInformation.NetworkInterface In System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus <> System.Net.NetworkInformation.OperationalStatus.Up Then Continue For
                Select Case nic.NetworkInterfaceType
                    Case System.Net.NetworkInformation.NetworkInterfaceType.Loopback,
                         System.Net.NetworkInformation.NetworkInterfaceType.Tunnel
                        Continue For
                End Select
                For Each ua As System.Net.NetworkInformation.UnicastIPAddressInformation In nic.GetIPProperties().UnicastAddresses
                    Dim addr As System.Net.IPAddress = ua.Address
                    If addr Is Nothing OrElse addr.AddressFamily <> System.Net.Sockets.AddressFamily.InterNetwork Then Continue For
                    If LiveProducerAddressPolicy.IsPublishablePicked(addr.ToString()) Then Return addr.ToString()
                Next
            Next
        Catch
        End Try
        Return ""
    End Function

End Module

''' <summary>Port finding for the server tests: a port that was free a moment ago (and usually still
''' is), or a way to hold one taken.</summary>
Public Module LiveTestPorts

    ''' <summary>Binds, reads the port, closes: a port that was free this instant. Races are retried by
    ''' the caller's assertion, and on a busy test machine a retry loop in the test absorbs them.</summary>
    Public Function FreePort() As Integer
        Dim listener As New TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        Dim port As Integer = (CType(listener.LocalEndpoint, IPEndPoint)).Port
        listener.Stop()
        Return port
    End Function

    ''' <summary>Holds a port open (the "taken" case of the no-fallback rule), on the address the
    ''' broadcast would bind - a port held on loopback does not conflict with a bind on the LAN
    ''' address, and the test must tell the truth about that.</summary>
    Public Class PortHolder
        Implements IDisposable

        Private ReadOnly _listener As TcpListener

        Public Sub New(Optional address As String = "127.0.0.1")
            _listener = New TcpListener(IPAddress.Parse(address), 0)
            _listener.Start()
        End Sub

        Public ReadOnly Property Port As Integer
            Get
                Return (CType(_listener.LocalEndpoint, IPEndPoint)).Port
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            _listener.Stop()
        End Sub
    End Class

End Module
#End If
