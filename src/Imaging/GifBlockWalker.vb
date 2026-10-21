#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text

''' <summary>
''' What a walk of a GIF's block structure found (spec 242 rule 7). Immutable. A walk never fails:
''' a file that is not a GIF, or that ends or breaks half way, still answers with what was read.
''' </summary>
Friend NotInheritable Class GifWalkResult

    ''' <summary>The answer for a stream that does not start with a GIF signature.</summary>
    Friend Shared ReadOnly NotGif As New GifWalkResult(False, 0, 0, 0, False)

    ''' <summary>True when the stream starts with <c>GIF87a</c> or <c>GIF89a</c>.</summary>
    Friend ReadOnly IsGif As Boolean

    ''' <summary>Canvas size from the logical-screen descriptor; 0 when the stream ended before it (or
    ''' the file says 0, which some old encoders do).</summary>
    Friend ReadOnly Width As Integer
    Friend ReadOnly Height As Integer

    ''' <summary>Frames whose image descriptor AND data were read to their terminator. A frame cut off by
    ''' the end of the data is not counted, so on a truncated file this is a lower bound.</summary>
    Friend ReadOnly Frames As Integer

    ''' <summary>True when the trailer (<c>;</c>) was reached. False for a truncated file, for a block
    ''' that is not a block, and for the (legal in practice) file that simply has no trailer.</summary>
    Friend ReadOnly IsComplete As Boolean

    Friend Sub New(isGif As Boolean, width As Integer, height As Integer, frames As Integer, isComplete As Boolean)
        Me.IsGif = isGif
        Me.Width = width
        Me.Height = height
        Me.Frames = frames
        Me.IsComplete = isComplete
    End Sub

End Class

''' <summary>
''' The canvas and the frame count of a GIF, read from its block structure WITHOUT decoding any
''' picture data (spec 242 rule 7): image descriptors are counted, every extension (graphic control,
''' comment, plain text, application such as NETSCAPE) and every run of data sub-blocks is skipped by
''' its declared length, global and local colour tables are stepped over. Linear in the file, one
''' pass, 64 KB of memory.
'''
''' Written for a hostile file. Every step consumes at least one byte, so the walk ends within the
''' length of the data whatever the file claims; a sub-block length that runs past the end ends the
''' walk (it never reads or seeks beyond the data); a failing stream ends the walk like a short one.
''' Nothing is thrown. Pure apart from reading the stream it is given.
''' </summary>
Friend NotInheritable Class GifBlockWalker

    Private Const Block_Trailer As Integer = &H3B
    Private Const Block_Extension As Integer = &H21
    Private Const Block_Image As Integer = &H2C

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Walks <paramref name="source"/> from its current position to the trailer or the end of the data.
    ''' The position afterwards is not defined. A stream that is Nothing or unreadable is
    ''' <see cref="GifWalkResult.NotGif"/>.
    ''' </summary>
    Friend Shared Function Walk(source As Stream) As GifWalkResult
        If source Is Nothing OrElse Not source.CanRead Then Return GifWalkResult.NotGif
        Dim bytes As New ByteSource(source)

        ' Signature "GIF87a" / "GIF89a", then the 7-byte logical-screen descriptor.
        Dim signature(5) As Byte
        If Not bytes.TryRead(signature, 6) Then Return GifWalkResult.NotGif
        Dim text As String = Encoding.ASCII.GetString(signature, 0, 6)
        If Not String.Equals(text, "GIF89a", StringComparison.Ordinal) AndAlso
           Not String.Equals(text, "GIF87a", StringComparison.Ordinal) Then Return GifWalkResult.NotGif

        Dim descriptor(6) As Byte
        If Not bytes.TryRead(descriptor, 7) Then Return New GifWalkResult(True, 0, 0, 0, False)
        ' CInt first: a shift on a Byte is a Byte shift.
        Dim width As Integer = CInt(descriptor(0)) Or (CInt(descriptor(1)) << 8)
        Dim height As Integer = CInt(descriptor(2)) Or (CInt(descriptor(3)) << 8)
        Dim packed As Integer = descriptor(4)
        If (packed And &H80) <> 0 AndAlso Not bytes.Skip(ColourTableBytes(packed)) Then
            Return New GifWalkResult(True, width, height, 0, False)
        End If

        Dim frames As Integer = 0
        Dim complete As Boolean = False
        Do
            Dim introducer As Integer = bytes.ReadByte()
            If introducer < 0 Then Exit Do                       ' the data ended: no trailer

            If introducer = Block_Trailer Then
                complete = True
                Exit Do
            ElseIf introducer = Block_Extension Then
                ' label, then data sub-blocks - the same shape for graphic control, comment, plain
                ' text and application extensions, so none needs its own case.
                If bytes.ReadByte() < 0 OrElse Not SkipSubBlocks(bytes) Then Exit Do
            ElseIf introducer = Block_Image Then
                ' left, top, width, height (8 bytes), packed flags, [local colour table],
                ' LZW minimum code size, data sub-blocks.
                If Not bytes.Skip(8) Then Exit Do
                Dim local As Integer = bytes.ReadByte()
                If local < 0 Then Exit Do
                If (local And &H80) <> 0 AndAlso Not bytes.Skip(ColourTableBytes(local)) Then Exit Do
                If bytes.ReadByte() < 0 OrElse Not SkipSubBlocks(bytes) Then Exit Do
                If frames < Integer.MaxValue Then frames += 1
            Else
                Exit Do                                          ' not a block: nothing after it can be trusted
            End If
        Loop

        Return New GifWalkResult(True, width, height, frames, complete)
    End Function

    ''' <summary>Bytes of a colour table whose size field is the low three bits of <paramref name="packed"/>:
    ''' 3 x 2^(n + 1), at most 768.</summary>
    Private Shared Function ColourTableBytes(packed As Integer) As Long
        Return 3L * (1L << ((packed And 7) + 1))
    End Function

    ''' <summary>Steps over a run of data sub-blocks (length byte, that many bytes) up to the zero-length
    ''' terminator. False when the data ends first, or a length runs past the end.</summary>
    Private Shared Function SkipSubBlocks(bytes As ByteSource) As Boolean
        Do
            Dim size As Integer = bytes.ReadByte()
            If size < 0 Then Return False
            If size = 0 Then Return True
            If Not bytes.Skip(size) Then Return False
        Loop
    End Function

    ''' <summary>
    ''' The stream as the walk needs it: one byte, or "skip n". Buffered, so a file of a million
    ''' sub-blocks is a few thousand reads; a seekable stream is skipped by moving its position, and
    ''' never past its length. Any failure of the stream is the end of the data.
    ''' </summary>
    Private NotInheritable Class ByteSource

        Private Const Buffer_Size As Integer = 64 * 1024

        Private ReadOnly source As Stream
        Private ReadOnly buffer As Byte() = New Byte(Buffer_Size - 1) {}
        Private filled As Integer
        Private cursor As Integer
        Private ended As Boolean

        Friend Sub New(source As Stream)
            Me.source = source
        End Sub

        ''' <summary>The next byte, or -1 at the end of the data.</summary>
        Friend Function ReadByte() As Integer
            If cursor >= filled AndAlso Not Refill() Then Return -1
            Dim value As Integer = buffer(cursor)
            cursor += 1
            Return value
        End Function

        ''' <summary>Fills <paramref name="target"/> with its first <paramref name="count"/> bytes; False if the data is shorter.</summary>
        Friend Function TryRead(target As Byte(), count As Integer) As Boolean
            For i As Integer = 0 To count - 1
                Dim value As Integer = ReadByte()
                If value < 0 Then Return False
                target(i) = CByte(value)
            Next
            Return True
        End Function

        ''' <summary>Moves past <paramref name="count"/> bytes. False when fewer than that remain (the
        ''' position is then at the end of the data).</summary>
        Friend Function Skip(count As Long) As Boolean
            If count <= 0 Then Return True
            Dim buffered As Integer = filled - cursor
            If count <= buffered Then
                cursor += CInt(count)
                Return True
            End If
            count -= buffered
            cursor = filled

            If source.CanSeek Then
                Try
                    Dim remaining As Long = source.Length - source.Position
                    If count > remaining Then
                        source.Position = source.Length
                        ended = True
                        Return False
                    End If
                    source.Position += count
                    Return True
                Catch ex As Exception
                    ' IOException, ObjectDisposedException, NotSupportedException: the data ends here.
                    ended = True
                    Return False
                End Try
            End If

            ' A stream that cannot seek is read and let go.
            Do While count > 0
                If Not Refill() Then Return False
                Dim take As Integer = CInt(Math.Min(count, CLng(filled)))
                cursor = take
                count -= take
            Loop
            Return True
        End Function

        Private Function Refill() As Boolean
            cursor = 0
            filled = 0
            If ended Then Return False
            Try
                filled = source.Read(buffer, 0, buffer.Length)
            Catch ex As Exception
                ' IOException, ObjectDisposedException, NotSupportedException, ...: the data ends here.
                filled = 0
            End Try
            If filled <= 0 Then
                filled = 0
                ended = True
                Return False
            End If
            Return True
        End Function

    End Class

End Class
#End If
