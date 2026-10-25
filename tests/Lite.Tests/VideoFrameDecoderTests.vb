#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Public Class VideoFrameDecoderTests
    <Fact>
    Public Async Function Concatenated_frames_are_split_at_PNG_chunk_boundaries() As Task
        Dim png As Byte()
        Using picture As New Bitmap(2, 2), output As New MemoryStream()
            picture.SetPixel(0, 0, Color.Red)
            picture.Save(output, Imaging.ImageFormat.Png)
            png = output.ToArray()
        End Using
        Using input As New MemoryStream()
            input.Write(png)
            input.Write(png)
            input.Position = 0
            Assert.Equal(png, Await VideoFrameDecoder.ReadPngAsync(input, CancellationToken.None))
            Assert.Equal(png, Await VideoFrameDecoder.ReadPngAsync(input, CancellationToken.None))
            Assert.Null(Await VideoFrameDecoder.ReadPngAsync(input, CancellationToken.None))
        End Using
        Assert.Equal(1000, VideoFrameSession.IntervalMs)
    End Function

    <Fact>
    Public Async Function Truncated_frames_are_failures_rather_than_clean_end_of_video() As Task
        Using input As New MemoryStream(New Byte() {137, 80, 78})
            Await Assert.ThrowsAsync(Of EndOfStreamException)(Async Function() Await VideoFrameDecoder.ReadPngAsync(input, CancellationToken.None))
        End Using
    End Function

    <Fact>
    Public Async Function An_unbounded_PNG_chunk_is_refused_before_allocating_its_payload() As Task
        Dim bytes As Byte() = {137, 80, 78, 71, 13, 10, 26, 10, 127, 255, 255, 255, 73, 68, 65, 84}
        Using input As New MemoryStream(bytes)
            Await Assert.ThrowsAsync(Of InvalidDataException)(Async Function() Await VideoFrameDecoder.ReadPngAsync(input, CancellationToken.None))
        End Using
    End Function
End Class
#End If
