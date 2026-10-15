#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' Ticket 199, the producer's audio half of section 8. Walks the ADTS framing of the
' AUDIO_ONLY payload (LIVE-BROADCAST 2.1): every frame carries its own header, so a late joiner
' decodes from the next frame - the audio join policy of the hub is exactly "the next frame boundary",
' and this class finds it. Pure: no device, no network.

Friend NotInheritable Class LiveAdtsFrames

    Private Sub New()
    End Sub

    ''' <summary>Index of (sample rate, channels) as ADTS writes them (ISO/IEC 13818-7 table 8 and 9).</summary>
    Private Shared ReadOnly SampleRateTable As Integer() = {
        96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050,
        16000, 12000, 11025, 8000, 7350, 0, 0, 0
    }

    ''' <summary>One parsed ADTS header. Valid only when <see cref="IsValid"/> is True.</summary>
    Friend Structure AdtsHeader
        Public IsValid As Boolean
        ''' <summary>Frame length INCLUDING the 7- or 9-byte header.</summary>
        Public FrameLengthBytes As Integer
        Public SampleRate As Integer
        Public Channels As Integer
        ''' <summary>True when the header carries CRC (9 bytes instead of 7).</summary>
        Public HasCrc As Boolean
    End Structure

    ''' <summary>Parses the ADTS header at <paramref name="offset"/>. A negative or out-of-range
    ''' <see cref="AdtsHeader.FrameLengthBytes"/> makes the header invalid.</summary>
    Public Shared Function ReadHeader(bytes As Byte(), offset As Integer) As AdtsHeader
        Dim h As New AdtsHeader With {.IsValid = False, .FrameLengthBytes = -1}
        If bytes Is Nothing OrElse offset < 0 OrElse offset + 7 > bytes.Length Then Return h
        If bytes(offset) <> &HFF OrElse (bytes(offset + 1) And &HF0) <> &HF0 Then Return h
        ' MPEG version bit 3: 1 = MPEG-4, 0 = MPEG-2; both are valid AAC-LC carries.
        Dim layer As Integer = (bytes(offset + 1) >> 1) And &H3
        If layer <> 0 Then Return h                     ' 0 = layer "none", what ADTS always writes
        Dim protectionAbsent As Integer = bytes(offset + 1) And &H1
        Dim rateIndex As Integer = (bytes(offset + 2) >> 2) And &HF
        If rateIndex >= SampleRateTable.Length OrElse SampleRateTable(rateIndex) = 0 Then Return h
        h.SampleRate = SampleRateTable(rateIndex)
        h.Channels = ((bytes(offset + 2) And &H1) << 2) Or ((bytes(offset + 3) >> 6) And &H3)
        If h.Channels = 0 Then Return h                  ' channel config 0 is defined, not used here
        h.HasCrc = protectionAbsent = 0
        h.FrameLengthBytes = ((bytes(offset + 3) And &H3) << 11) Or
                              (bytes(offset + 4) << 3) Or
                              ((bytes(offset + 5) And &HE0) >> 5)
        h.IsValid = h.FrameLengthBytes >= If(h.HasCrc, 9, 7) + 1 AndAlso
                    offset + h.FrameLengthBytes <= bytes.Length
        Return h
    End Function

    ''' <summary>The offset of the first ADTS frame at or after <paramref name="from"/> - the joinable
    ''' start a late listener takes (2.1: "a late joiner decodes from the next frame"). -1 when no frame
    ''' head is found; the scan is capped because a payload of junk must not walk the whole buffer.</summary>
    Public Shared Function FirstFrameAt(bytes As Byte(), from As Integer) As Integer
        If bytes Is Nothing Then Return -1
        Dim max As Integer = bytes.Length - 7
        Dim i As Integer = Math.Max(0, from)
        While i <= max
            If bytes(i) = &HFF AndAlso (bytes(i + 1) And &HF0) = &HF0 Then
                Dim h As AdtsHeader = ReadHeader(bytes, i)
                If h.IsValid Then Return i
            End If
            i += 1
        End While
        Return -1
    End Function

    ''' <summary>Counts whole, well-formed ADTS frames in the buffer. A frame whose header parses but
    ''' whose declared length runs past the buffer is NOT counted - the producer never cuts a frame in
    ''' half, and a test that says "frames decode" must not pass on a truncated one.</summary>
    Public Shared Function CountFrames(bytes As Byte()) As Integer
        Return CountFrames(bytes, 0, If(bytes Is Nothing, 0, bytes.Length))
    End Function

    Public Shared Function CountFrames(bytes As Byte(), offset As Integer, count As Integer) As Integer
        Dim frames As Integer = 0
        If bytes Is Nothing OrElse count <= 0 Then Return 0
        Dim [end] As Integer = offset + count
        Dim i As Integer = offset
        While i + 7 <= [end]
            If bytes(i) = &HFF AndAlso (bytes(i + 1) And &HF0) = &HF0 Then
                Dim h As AdtsHeader = ReadHeader(bytes, i)
                If h.IsValid AndAlso i + h.FrameLengthBytes <= [end] Then
                    frames += 1
                    i += h.FrameLengthBytes
                    Continue While
                End If
            End If
            i += 1
        End While
        Return frames
    End Function

    ''' <summary>The frame rate of AAC-LC at a sample rate: every ADTS frame carries 1024 samples, so a
    ''' 44 100 Hz stream lands at 43.07 frames per second - the watch's measured figure (contract 5.5).</summary>
    Public Shared Function FramesPerSecond(sampleRate As Integer) As Double
        If sampleRate <= 0 Then Return 0.0
        Return sampleRate / 1024.0
    End Function

End Class

''' <summary>Pipe reads are arbitrary bytes, not TS packets or AAC frames. Retain only the unfinished tail.</summary>
Friend NotInheritable Class LiveEncodedFramer
    Private ReadOnly _video As Boolean
    Private _pending As Byte() = Array.Empty(Of Byte)()

    Public Sub New(video As Boolean)
        _video = video
    End Sub

    Public Function Feed(bytes As Byte()) As Byte()
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Array.Empty(Of Byte)()
        Dim combined(_pending.Length + bytes.Length - 1) As Byte
        Array.Copy(_pending, combined, _pending.Length)
        Array.Copy(bytes, 0, combined, _pending.Length, bytes.Length)
        Dim length As Integer = 0
        If _video Then
            length = combined.Length - (combined.Length Mod LiveProducerContract.TsPacketSize)
        Else
            While length + 7 <= combined.Length
                Dim header As LiveAdtsFrames.AdtsHeader = LiveAdtsFrames.ReadHeader(combined, length)
                If Not header.IsValid Then Exit While
                length += header.FrameLengthBytes
            End While
            If combined.Length - length > 8191 Then Throw New IO.InvalidDataException("Invalid encoder ADTS output")
        End If
        Dim result As Byte() = combined.AsSpan(0, length).ToArray()
        _pending = combined.AsSpan(length).ToArray()
        Return result
    End Function
End Class
#End If
