#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md, contract-sync work item 2 (LIVE-BROADCAST rule 2:
' "never let the delay grow", in steady state too).
'
' The producer's clock and this PC's audio clock are two different crystals, so a session that never
' stalls can still fall behind. To notice it the viewer needs "how much audio has ARRIVED": this counts
' ADTS frames in the bytes that pass through the response stream. Pure, no allocation per byte, and the
' totals are readable from another thread (the UI timer samples them while LibVLC's thread feeds them).

''' <summary>
''' Counts ADTS AAC frames in a byte stream and sums their duration. It never decodes anything; it
''' reads the 7-byte header of each frame (sync word, sampling-frequency index, frame length, number of
''' raw data blocks) and skips the payload. Garbage between frames is skipped one byte at a time until
''' the next sync word, so a late joiner or a damaged stretch costs bytes, not the count.
''' </summary>
Friend NotInheritable Class AdtsFrameCounter

    Private Const HeaderLength As Integer = 7
    Private Const SamplesPerBlock As Integer = 1024

    Private Shared ReadOnly SampleRates As Integer() = {
        96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350
    }

    Private ReadOnly header(HeaderLength - 1) As Byte
    Private headerFill As Integer
    Private payloadLeft As Integer
    Private frames As Long
    Private skipped As Long
    ' Microseconds of audio carried by the frames seen so far. A Long so it is read and written atomically.
    Private durationMicros As Long

    ''' <summary>Frames whose header was read completely.</summary>
    Public ReadOnly Property FrameCount As Long
        Get
            Return Interlocked.Read(frames)
        End Get
    End Property

    ''' <summary>Audio that has arrived, in milliseconds.</summary>
    Public ReadOnly Property DurationMs As Double
        Get
            Return Interlocked.Read(durationMicros) / 1000.0R
        End Get
    End Property

    ''' <summary>Bytes thrown away while looking for a sync word.</summary>
    Public ReadOnly Property SkippedBytes As Long
        Get
            Return Interlocked.Read(skipped)
        End Get
    End Property

    ''' <summary>Feeds the next block of the stream. Blocks may split a frame (or its header) anywhere.</summary>
    Public Sub Feed(buffer As Byte(), offset As Integer, count As Integer)
        If buffer Is Nothing OrElse count <= 0 Then Return

        Dim index As Integer = offset
        Dim [end] As Integer = offset + count
        While index < [end]
            If payloadLeft > 0 Then
                Dim take As Integer = Math.Min(payloadLeft, [end] - index)
                payloadLeft -= take
                index += take
                Continue While
            End If

            ' Looking for / collecting a header.
            Dim b As Byte = buffer(index)
            index += 1
            If headerFill = 0 Then
                If b = &HFF Then
                    header(0) = b
                    headerFill = 1
                Else
                    Interlocked.Increment(skipped)
                End If
                Continue While
            End If

            If headerFill = 1 AndAlso (b And &HF6) <> &HF0 Then
                ' Not a sync word (layer must be 00): this byte may itself start one.
                Interlocked.Increment(skipped)
                If b = &HFF Then
                    header(0) = b
                    headerFill = 1
                Else
                    headerFill = 0
                End If
                Continue While
            End If

            header(headerFill) = b
            headerFill += 1
            If headerFill < HeaderLength Then Continue While

            Dim frameLength As Integer = ParseHeader()
            If frameLength < 0 Then
                ' A false sync: drop its first byte, rescan the six that follow it.
                Interlocked.Increment(skipped)
                ReSync()
                Continue While
            End If

            headerFill = 0
            payloadLeft = frameLength - HeaderLength
        End While
    End Sub

    ''' <summary>Returns the frame length, or -1 when these seven bytes are not an ADTS header.</summary>
    Private Function ParseHeader() As Integer
        ' Widened to Integer first: a VB shift on a Byte masks the count to 0..7 and stays a Byte.
        Dim rateIndex As Integer = (CInt(header(2)) >> 2) And &HF
        If rateIndex >= SampleRates.Length Then Return -1

        Dim frameLength As Integer = ((CInt(header(3)) And &H3) << 11) Or (CInt(header(4)) << 3) Or (CInt(header(5)) >> 5)
        If frameLength < HeaderLength Then Return -1

        Dim blocks As Integer = (CInt(header(6)) And &H3) + 1
        Interlocked.Add(durationMicros, CLng(blocks) * SamplesPerBlock * 1000000L \ SampleRates(rateIndex))
        Interlocked.Increment(frames)
        Return frameLength
    End Function

    ''' <summary>Drops the first header byte and keeps scanning inside the bytes already collected.</summary>
    Private Sub ReSync()
        Dim kept As Integer = 0
        For i As Integer = 1 To HeaderLength - 1
            Dim b As Byte = header(i)
            If kept = 0 Then
                If b = &HFF Then
                    header(0) = b
                    kept = 1
                Else
                    Interlocked.Increment(skipped)
                End If
            ElseIf kept = 1 AndAlso (b And &HF6) <> &HF0 Then
                Interlocked.Increment(skipped)
                If b = &HFF Then
                    header(0) = b
                    kept = 1
                Else
                    kept = 0
                End If
            Else
                header(kept) = b
                kept += 1
            End If
        Next
        headerFill = kept
    End Sub

End Class
#End If
