#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>What <see cref="AnimationHeader"/> read from a WEBP or PNG/APNG header. Immutable.</summary>
Friend NotInheritable Class AnimationHeaderInfo

    ''' <summary>The canvas (the whole picture), not one frame's rectangle.</summary>
    Friend ReadOnly Width As Integer
    Friend ReadOnly Height As Integer

    ''' <summary>Frames the header declares and the data holds: 1 for a still. A frame whose
    ''' chunk is cut off by the end of the data is not counted.</summary>
    Friend ReadOnly Frames As Integer

    ''' <summary>The sum of the frame durations in milliseconds; 0 for a still or when unknown.</summary>
    Friend ReadOnly DurationMs As Long

    ''' <summary>True when the header says the picture carries transparency.</summary>
    Friend ReadOnly HasAlpha As Boolean

    ''' <summary>True for an animation of more than one frame.</summary>
    Friend ReadOnly IsAnimated As Boolean

    ''' <summary>APNG only: True when the default image (the IDAT data) is the animation's first
    ''' frame - an fcTL came before the first IDAT. A plain PNG reader then returns exactly the
    ''' first frame. False for a WEBP.</summary>
    Friend ReadOnly DefaultImageIsFirstFrame As Boolean

    ''' <summary>WEBP only: where the first ANMF chunk's payload starts and how long it is; -1
    ''' when there is none.</summary>
    Friend ReadOnly FirstFrameOffset As Long
    Friend ReadOnly FirstFrameLength As Long

    Friend Sub New(width As Integer, height As Integer, frames As Integer, durationMs As Long, hasAlpha As Boolean,
                   isAnimated As Boolean, defaultImageIsFirstFrame As Boolean, firstFrameOffset As Long, firstFrameLength As Long)
        Me.Width = width
        Me.Height = height
        Me.Frames = frames
        Me.DurationMs = durationMs
        Me.HasAlpha = hasAlpha
        Me.IsAnimated = isAnimated
        Me.DefaultImageIsFirstFrame = defaultImageIsFirstFrame
        Me.FirstFrameOffset = firstFrameOffset
        Me.FirstFrameLength = firstFrameLength
    End Sub

End Class

''' <summary>The first frame of an animated WEBP as a standalone still WEBP, and where it sits on
''' the canvas (specification 270 R4).</summary>
Friend NotInheritable Class WebpFirstFrame
    Friend ReadOnly Bytes As Byte()
    Friend ReadOnly X As Integer
    Friend ReadOnly Y As Integer
    Friend ReadOnly Width As Integer
    Friend ReadOnly Height As Integer

    Friend Sub New(bytes As Byte(), x As Integer, y As Integer, width As Integer, height As Integer)
        Me.Bytes = bytes
        Me.X = x
        Me.Y = y
        Me.Width = width
        Me.Height = height
    End Sub
End Class

''' <summary>
''' The canvas, frame count, durations and transparency of an animated WEBP or APNG, read from
''' the container's chunk headers WITHOUT decoding any picture data (specification 270 R4 and
''' R8): the decode budget is asked from these figures before a pixel is allocated, and the
''' first-frame preview of a long WEBP is cut out of the file instead of decoding the whole
''' animation, which ImageMagick's reader does even when asked for one frame.
'''
''' Written for a hostile file, like <c>GifBlockWalker</c>: every step moves forward by at
''' least one chunk header, a chunk that claims to run past the data ends the walk, a read that
''' fails ends it like a short one. Nothing is thrown; Nothing is returned for data that is not
''' the container asked for. Pure apart from reading the (seekable) stream it is given, whose
''' position is restored.
''' </summary>
Friend Module AnimationHeader

    ''' <summary>The header of a RIFF/WEBP file, or Nothing when <paramref name="source"/> is not one.</summary>
    Friend Function ReadWebp(source As Stream) As AnimationHeaderInfo
        If source Is Nothing OrElse Not source.CanRead OrElse Not source.CanSeek Then Return Nothing
        Dim start As Long = source.Position
        Try
            Return WalkWebp(source)
        Catch ex As IOException
            Return Nothing
        Finally
            source.Position = start
        End Try
    End Function

    ''' <summary>The header of a PNG (animated or not), or Nothing when <paramref name="source"/> is not one.</summary>
    Friend Function ReadPng(source As Stream) As AnimationHeaderInfo
        If source Is Nothing OrElse Not source.CanRead OrElse Not source.CanSeek Then Return Nothing
        Dim start As Long = source.Position
        Try
            Return WalkPng(source)
        Catch ex As IOException
            Return Nothing
        Finally
            source.Position = start
        End Try
    End Function

    ''' <summary>
    ''' The first frame of an animated WEBP rebuilt as a still WEBP file: the frame's own
    ''' bitstream chunks (VP8L, or VP8 with its ALPH) under a new RIFF header, with a VP8X chunk
    ''' when an ALPH needs one. No pixel is decoded. Nothing when the file has no usable first
    ''' frame - the caller then decodes the ordinary way.
    ''' </summary>
    Friend Function ExtractFirstWebpFrame(source As Stream, info As AnimationHeaderInfo) As WebpFirstFrame
        If source Is Nothing OrElse info Is Nothing OrElse info.FirstFrameOffset < 0 OrElse info.FirstFrameLength < 16 Then Return Nothing
        If info.FirstFrameLength > Integer.MaxValue Then Return Nothing
        Dim start As Long = source.Position
        Try
            source.Position = info.FirstFrameOffset
            Dim frame(CInt(info.FirstFrameLength) - 1) As Byte
            If Not ReadFully(source, frame, frame.Length) Then Return Nothing
            Return BuildStillFromFrame(frame)
        Catch ex As IOException
            Return Nothing
        Finally
            source.Position = start
        End Try
    End Function

    ''' <summary>
    ''' The payload of the first top-level chunk of a RIFF/WEBP file named
    ''' <paramref name="fourCc"/> ("EXIF", "ICCP", "XMP "), or Nothing - the metadata a "Save as"
    ''' carries over, read without decoding a pixel. A chunk over 64 MB is not read.
    ''' </summary>
    Friend Function ReadWebpChunk(source As Stream, fourCc As String) As Byte()
        If source Is Nothing OrElse Not source.CanRead OrElse Not source.CanSeek Then Return Nothing
        Dim start As Long = source.Position
        Try
            Dim riff(11) As Byte
            source.Position = 0
            If Not ReadFully(source, riff, 12) Then Return Nothing
            If Not (riff(0) = &H52 AndAlso riff(1) = &H49 AndAlso riff(2) = &H46 AndAlso riff(3) = &H46 AndAlso
                    riff(8) = &H57 AndAlso riff(9) = &H45 AndAlso riff(10) = &H42 AndAlso riff(11) = &H50) Then Return Nothing
            Dim riffEnd As Long = Math.Min(source.Length, 8L + LittleEndian32(riff, 4))
            Dim position As Long = 12
            Dim chunk(7) As Byte
            Do While position + 8 <= riffEnd
                source.Position = position
                If Not ReadFully(source, chunk, 8) Then Exit Do
                Dim size As Long = LittleEndian32(chunk, 4)
                If position + 8 + size > riffEnd Then Exit Do
                If String.Equals(System.Text.Encoding.ASCII.GetString(chunk, 0, 4), fourCc, StringComparison.Ordinal) Then
                    If size > Max_Metadata_Chunk_Bytes Then Return Nothing
                    Dim payload(CInt(size) - 1) As Byte
                    Return If(size = 0 OrElse ReadFully(source, payload, CInt(size)), payload, Nothing)
                End If
                position += 8 + size + (size And 1L)
            Loop
            Return Nothing
        Catch ex As IOException
            Return Nothing
        Finally
            source.Position = start
        End Try
    End Function

    Private Const Max_Metadata_Chunk_Bytes As Long = 64L * 1024L * 1024L

    Private Function WalkWebp(source As Stream) As AnimationHeaderInfo
        Dim riff(11) As Byte
        source.Position = 0
        If Not ReadFully(source, riff, 12) Then Return Nothing
        If Not (riff(0) = &H52 AndAlso riff(1) = &H49 AndAlso riff(2) = &H46 AndAlso riff(3) = &H46 AndAlso
                riff(8) = &H57 AndAlso riff(9) = &H45 AndAlso riff(10) = &H42 AndAlso riff(11) = &H50) Then Return Nothing
        ' The RIFF size counts from offset 8; a file shorter than it claims ends where its data does.
        Dim riffEnd As Long = Math.Min(source.Length, 8L + LittleEndian32(riff, 4))

        Dim width As Integer = 0, height As Integer = 0
        Dim frames As Integer = 0
        Dim duration As Long = 0
        Dim alpha As Boolean = False
        Dim animatedFlag As Boolean = False
        Dim firstOffset As Long = -1, firstLength As Long = 0

        Dim position As Long = 12
        Dim chunk(7) As Byte
        Do While position + 8 <= riffEnd
            source.Position = position
            If Not ReadFully(source, chunk, 8) Then Exit Do
            Dim fourCc As String = System.Text.Encoding.ASCII.GetString(chunk, 0, 4)
            Dim size As Long = LittleEndian32(chunk, 4)
            Dim payload As Long = position + 8
            If payload + size > riffEnd Then Exit Do                      ' cut off: nothing after it counts

            Select Case fourCc
                Case "VP8X"
                    Dim vp8x(9) As Byte
                    If size < 10 OrElse Not ReadFully(source, vp8x, 10) Then Exit Do
                    alpha = alpha OrElse (vp8x(0) And &H10) <> 0
                    animatedFlag = (vp8x(0) And &H2) <> 0
                    width = 1 + LittleEndian24(vp8x, 4)
                    height = 1 + LittleEndian24(vp8x, 7)
                Case "ANMF"
                    Dim anmf(15) As Byte
                    If size < 16 OrElse Not ReadFully(source, anmf, 16) Then Exit Do
                    duration += LittleEndian24(anmf, 12)
                    If frames < Integer.MaxValue Then frames += 1
                    If firstOffset < 0 Then
                        firstOffset = payload
                        firstLength = size
                    End If
                Case "ALPH"
                    alpha = True
                Case "VP8 "
                    Dim vp8(9) As Byte
                    If width = 0 AndAlso size >= 10 AndAlso ReadFully(source, vp8, 10) AndAlso
                       vp8(3) = &H9D AndAlso vp8(4) = &H1 AndAlso vp8(5) = &H2A Then
                        width = (CInt(vp8(6)) Or (CInt(vp8(7)) << 8)) And &H3FFF
                        height = (CInt(vp8(8)) Or (CInt(vp8(9)) << 8)) And &H3FFF
                    End If
                Case "VP8L"
                    Dim vp8l(4) As Byte
                    If width = 0 AndAlso size >= 5 AndAlso ReadFully(source, vp8l, 5) AndAlso vp8l(0) = &H2F Then
                        Dim bits As Long = LittleEndian32(vp8l, 1)
                        width = CInt(bits And &H3FFF) + 1
                        height = CInt((bits >> 14) And &H3FFF) + 1
                        alpha = alpha OrElse ((bits >> 28) And 1) <> 0
                    End If
            End Select

            ' Chunks are padded to an even length.
            position = payload + size + (size And 1L)
        Loop

        If width <= 0 OrElse height <= 0 Then Return Nothing
        Dim animated As Boolean = animatedFlag AndAlso frames > 1
        Return New AnimationHeaderInfo(width, height, Math.Max(1, frames), If(frames > 1, duration, 0L), alpha,
                                       animated, False, firstOffset, firstLength)
    End Function

    Private Function WalkPng(source As Stream) As AnimationHeaderInfo
        Dim signature(7) As Byte
        source.Position = 0
        If Not ReadFully(source, signature, 8) Then Return Nothing
        If Not (signature(0) = &H89 AndAlso signature(1) = &H50 AndAlso signature(2) = &H4E AndAlso signature(3) = &H47 AndAlso
                signature(4) = &HD AndAlso signature(5) = &HA AndAlso signature(6) = &H1A AndAlso signature(7) = &HA) Then Return Nothing

        Dim width As Integer = 0, height As Integer = 0
        Dim alpha As Boolean = False
        Dim hasActl As Boolean = False
        Dim fcTlCount As Integer = 0
        Dim duration As Long = 0
        Dim seenIdat As Boolean = False
        Dim defaultIsFirst As Boolean = False

        Dim length As Long = source.Length
        Dim position As Long = 8
        Dim chunk(7) As Byte
        Do While position + 12 <= length
            source.Position = position
            If Not ReadFully(source, chunk, 8) Then Exit Do
            Dim size As Long = BigEndian32(chunk, 0)
            Dim type As String = System.Text.Encoding.ASCII.GetString(chunk, 4, 4)
            If size > Integer.MaxValue OrElse position + 12 + size > length Then Exit Do

            Select Case type
                Case "IHDR"
                    Dim ihdr(12) As Byte
                    If size < 13 OrElse Not ReadFully(source, ihdr, 13) Then Exit Do
                    width = CInt(Math.Min(BigEndian32(ihdr, 0), Integer.MaxValue))
                    height = CInt(Math.Min(BigEndian32(ihdr, 4), Integer.MaxValue))
                    Dim colourType As Integer = ihdr(9)
                    alpha = colourType = 4 OrElse colourType = 6
                Case "tRNS"
                    alpha = True
                Case "acTL"
                    hasActl = True
                Case "fcTL"
                    Dim fctl(25) As Byte
                    If size < 26 OrElse Not ReadFully(source, fctl, 26) Then Exit Do
                    If Not seenIdat Then defaultIsFirst = True
                    Dim numerator As Long = (CLng(fctl(20)) << 8) Or fctl(21)
                    Dim denominator As Long = (CLng(fctl(22)) << 8) Or fctl(23)
                    ' A zero denominator means hundredths of a second (APNG specification).
                    If denominator = 0 Then denominator = 100
                    duration += numerator * 1000L \ denominator
                    If fcTlCount < Integer.MaxValue Then fcTlCount += 1
                Case "IDAT"
                    seenIdat = True
                Case "IEND"
                    Exit Do
            End Select

            position += 12 + size
        Loop

        If width <= 0 OrElse height <= 0 Then Return Nothing
        Dim animated As Boolean = hasActl AndAlso fcTlCount > 1
        Dim frames As Integer = If(animated, fcTlCount, 1)
        Return New AnimationHeaderInfo(width, height, frames, If(animated, duration, 0L), alpha, animated,
                                       animated AndAlso defaultIsFirst, -1, 0)
    End Function

    ''' <summary>
    ''' An ANMF payload (16 bytes of frame header, then the frame's chunks) as a still WEBP. The
    ''' frame's chunks are copied verbatim; only the RIFF and VP8X headers are new.
    ''' </summary>
    Private Function BuildStillFromFrame(frame As Byte()) As WebpFirstFrame
        Dim x As Integer = 2 * LittleEndian24(frame, 0)
        Dim y As Integer = 2 * LittleEndian24(frame, 3)
        Dim width As Integer = 1 + LittleEndian24(frame, 6)
        Dim height As Integer = 1 + LittleEndian24(frame, 9)

        Dim alphOffset As Integer = -1, alphLength As Integer = 0
        Dim bitstreamOffset As Integer = -1, bitstreamLength As Integer = 0
        Dim isLossless As Boolean = False
        Dim position As Integer = 16
        Do While position + 8 <= frame.Length
            Dim fourCc As String = System.Text.Encoding.ASCII.GetString(frame, position, 4)
            Dim size As Long = LittleEndian32(frame, position + 4)
            If position + 8L + size > frame.Length Then Exit Do
            Dim padded As Integer = CInt(8L + size + (size And 1L))
            Select Case fourCc
                Case "ALPH"
                    If alphOffset < 0 Then
                        alphOffset = position
                        alphLength = Math.Min(padded, frame.Length - position)
                    End If
                Case "VP8 ", "VP8L"
                    bitstreamOffset = position
                    bitstreamLength = Math.Min(padded, frame.Length - position)
                    isLossless = fourCc = "VP8L"
                    Exit Do
            End Select
            position += padded
        Loop
        If bitstreamOffset < 0 Then Return Nothing

        Using output As New MemoryStream(), body As New MemoryStream()
            If Not isLossless AndAlso alphOffset >= 0 Then
                ' VP8X: alpha flag, three reserved bytes, canvas width-1 and height-1 (24 bits each).
                body.Write(New Byte() {&H56, &H50, &H38, &H58, 10, 0, 0, 0, &H10, 0, 0, 0}, 0, 12)
                WriteLittleEndian24(body, width - 1)
                WriteLittleEndian24(body, height - 1)
                body.Write(frame, alphOffset, alphLength)
            End If
            body.Write(frame, bitstreamOffset, bitstreamLength)

            Dim riffSize As Long = 4L + body.Length
            output.Write(New Byte() {&H52, &H49, &H46, &H46}, 0, 4)
            output.Write(BitConverter.GetBytes(CUInt(riffSize)), 0, 4)
            output.Write(New Byte() {&H57, &H45, &H42, &H50}, 0, 4)
            body.Position = 0
            body.CopyTo(output)
            Return New WebpFirstFrame(output.ToArray(), x, y, width, height)
        End Using
    End Function

    Private Function ReadFully(source As Stream, buffer As Byte(), count As Integer) As Boolean
        Dim total As Integer = 0
        Do While total < count
            Dim read As Integer = source.Read(buffer, total, count - total)
            If read <= 0 Then Return False
            total += read
        Loop
        Return True
    End Function

    ' CInt/CLng first: a shift on a Byte is a Byte shift.
    Private Function LittleEndian24(data As Byte(), offset As Integer) As Integer
        Return CInt(data(offset)) Or (CInt(data(offset + 1)) << 8) Or (CInt(data(offset + 2)) << 16)
    End Function

    Private Function LittleEndian32(data As Byte(), offset As Integer) As Long
        Return CLng(data(offset)) Or (CLng(data(offset + 1)) << 8) Or (CLng(data(offset + 2)) << 16) Or (CLng(data(offset + 3)) << 24)
    End Function

    Private Function BigEndian32(data As Byte(), offset As Integer) As Long
        Return (CLng(data(offset)) << 24) Or (CLng(data(offset + 1)) << 16) Or (CLng(data(offset + 2)) << 8) Or CLng(data(offset + 3))
    End Function

    Private Sub WriteLittleEndian24(target As Stream, value As Integer)
        target.WriteByte(CByte(value And &HFF))
        target.WriteByte(CByte((value >> 8) And &HFF))
        target.WriteByte(CByte((value >> 16) And &HFF))
    End Sub

End Module
#End If
