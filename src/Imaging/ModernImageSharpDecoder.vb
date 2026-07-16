#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO

''' <summary>
''' .NET 10 decoder: SixLabors.ImageSharp 3.x, fully managed - no OS codec, so
''' WEBP (including animated) works on Server editions without the Store "WebP
''' Image Extensions" (the original Server 2025 bug that triggered the epic).
''' Static frames re-encode to PNG and detach; animated images transcode to an
''' in-memory GIF so the existing GDI+ GIF playback pipeline animates them
''' without any UI changes (256-color cost accepted for v1 - see progress doc).
''' This whole file compiles only in the modern build.
''' </summary>
Friend NotInheritable Class ModernImageSharpDecoder
    Implements IImageDecoder

    Public Function DecodeToImage(stream As MemoryStream) As Image Implements IImageDecoder.DecodeToImage
        stream.Position = 0

        Using sharpImage As SixLabors.ImageSharp.Image = SixLabors.ImageSharp.Image.Load(stream)
            If sharpImage.Frames.Count > 1 Then
                Return TranscodeAnimationToGif(sharpImage)
            End If

            Using pngStream As New MemoryStream()
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(sharpImage, pngStream)
                pngStream.Position = 0

                Using pngImage As Image = Image.FromStream(pngStream)
                    Return New Bitmap(pngImage)
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Animated WEBP/APNG -> GIF in memory. GDI+ animates GIF natively
    ''' (RawFormat = Gif), so Main_Form.GifPlayback picks the result up as if the
    ''' file had been a GIF. The GIF stream is intentionally NOT disposed here:
    ''' GDI+ streams later frames from it for the lifetime of the Image (the
    ''' caller-side file stream in LoadImageWithStream stays open the same way).
    ''' </summary>
    Private Function TranscodeAnimationToGif(sharpImage As SixLabors.ImageSharp.Image) As Image
        CopyWebpFrameDelaysToGif(sharpImage)

        Dim gifStream As New MemoryStream()
        SixLabors.ImageSharp.ImageExtensions.SaveAsGif(sharpImage, gifStream)
        gifStream.Position = 0
        Return Image.FromStream(gifStream)
    End Function

    ''' <summary>
    ''' The GIF encoder does not read another format's frame timing, so an
    ''' animated WEBP would play at the default (fast) rate - copy the per-frame
    ''' durations across explicitly. Defensive: any metadata surprise leaves the
    ''' default timing rather than failing the load.
    ''' </summary>
    Private Sub CopyWebpFrameDelaysToGif(sharpImage As SixLabors.ImageSharp.Image)
        Try
            For Each frame In sharpImage.Frames
                Dim webpMeta = frame.Metadata.GetFormatMetadata(SixLabors.ImageSharp.Formats.Webp.WebpFormat.Instance)
                If webpMeta Is Nothing OrElse webpMeta.FrameDelay = 0 Then Continue For

                ' Webp FrameDelay is in milliseconds, GIF FrameDelay in centiseconds.
                Dim delayCentiseconds As Integer = CInt(Math.Max(1L, CLng(webpMeta.FrameDelay) \ 10L))
                frame.Metadata.GetFormatMetadata(SixLabors.ImageSharp.Formats.Gif.GifFormat.Instance).FrameDelay = delayCentiseconds
            Next
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0039: webp->gif frame timing copy skipped: " & ex.Message)
        End Try
    End Sub

    Public Function TryGetPixelSize(stream As Stream) As Size Implements IImageDecoder.TryGetPixelSize
        Try
            Dim info = SixLabors.ImageSharp.Image.Identify(stream)
            If info Is Nothing OrElse info.Width <= 0 OrElse info.Height <= 0 Then Return Size.Empty
            Return New Size(info.Width, info.Height)
        Catch
            Return Size.Empty
        End Try
    End Function

End Class
#End If
