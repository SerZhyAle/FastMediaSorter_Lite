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
''' AVIF/HEIC/HEIF (epic O-3) go to Magick.NET instead - ImageSharp has no
''' AV1/HEVC decoder - again with no OS codec involved, so iPhone photos open
''' without the paid Store "HEVC Video Extensions".
''' This whole file compiles only in the modern build.
''' </summary>
Friend NotInheritable Class ModernImageSharpDecoder
    Implements IImageDecoder

    Public Function DecodeToImage(stream As MemoryStream) As Image Implements IImageDecoder.DecodeToImage
        stream.Position = 0

        ' AVIF/HEIC/HEIF are ISO-BMFF containers ("ftyp" box); ImageSharp would
        ' throw UnknownImageFormatException on them, so route by signature - the
        ' MemoryStream has no file name to key on.
        If IsIsoBmff(stream) Then Return LoadViaMagick(stream)

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
            If IsIsoBmff(stream) Then
                stream.Position = 0
                Dim magickInfo As New ImageMagick.MagickImageInfo(stream)
                If magickInfo.Width <= 0 OrElse magickInfo.Height <= 0 Then Return Size.Empty
                Return New Size(CInt(magickInfo.Width), CInt(magickInfo.Height))
            End If

            Dim info = SixLabors.ImageSharp.Image.Identify(stream)
            If info Is Nothing OrElse info.Width <= 0 OrElse info.Height <= 0 Then Return Size.Empty
            Return New Size(info.Width, info.Height)
        Catch
            Return Size.Empty
        End Try
    End Function

    ''' <summary>
    ''' True when the stream starts with an ISO-BMFF "ftyp" box (AVIF/HEIC/HEIF).
    ''' No other image format this decoder sees uses that container, so the brand
    ''' does not need parsing. Leaves the position at 0.
    ''' </summary>
    Private Shared Function IsIsoBmff(stream As Stream) As Boolean
        Dim head(11) As Byte
        stream.Position = 0
        Dim read As Integer = stream.Read(head, 0, head.Length)
        stream.Position = 0
        If read < 12 Then Return False
        Return head(4) = Asc("f"c) AndAlso head(5) = Asc("t"c) AndAlso head(6) = Asc("y"c) AndAlso head(7) = Asc("p"c)
    End Function

    ''' <summary>
    ''' Magick.NET decode for the ISO-BMFF formats: first frame, re-encoded to a
    ''' detached PNG the same way the ImageSharp path does. Honors the EXIF
    ''' auto-rotate setting here because the PNG re-encode drops the Orientation
    ''' tag before FileManager.ApplyExifOrientation could see it (iPhone HEICs
    ''' rely on that tag).
    ''' </summary>
    Private Shared Function LoadViaMagick(stream As MemoryStream) As Image
        stream.Position = 0

        Using magickImage As New ImageMagick.MagickImage(stream)
            If Is_Exif_AutoRotate Then magickImage.AutoOrient()

            Using pngStream As New MemoryStream()
                magickImage.Write(pngStream, ImageMagick.MagickFormat.Png)
                pngStream.Position = 0

                Using pngImage As Image = Image.FromStream(pngStream)
                    Return New Bitmap(pngImage)
                End Using
            End Using
        End Using
    End Function

End Class
#End If
