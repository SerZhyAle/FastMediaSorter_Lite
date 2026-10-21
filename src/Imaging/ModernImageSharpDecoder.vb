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
'''
''' There is exactly ONE decode implementation here, and it is
''' <see cref="DecodeToPayload"/> - the encoded bytes ImageSharp/Magick produce
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §3.1).
''' <see cref="DecodeToImage"/> is a wrapper that hands those bytes to GDI+, and the
''' decode cache stores the very same bytes. Two decode paths that could drift apart is
''' precisely what the payload interface exists to avoid.
''' </summary>
Friend NotInheritable Class ModernImageSharpDecoder
    Implements IImageDecoder, IImageDecoderPayload

    Public ReadOnly Property ImageOwnsData As Boolean Implements IImageDecoder.ImageOwnsData
        Get
            Return True
        End Get
    End Property

    Public Function DecodeToImage(stream As MemoryStream) As Image Implements IImageDecoder.DecodeToImage
        Dim payload As DecodedPayload = DecodeToPayload(stream)
        If payload Is Nothing OrElse payload.Bytes Is Nothing Then Return Nothing

        Dim payloadStream As New MemoryStream(payload.Bytes)
        If payload.Kind = DecodedPayloadKind.Gif Then
            ' The GIF stream is intentionally NOT disposed: GDI+ streams later frames from
            ' it for the lifetime of the Image (the caller-side file stream in
            ' LoadImageWithStream stays open the same way).
            Return Image.FromStream(payloadStream)
        End If

        Using payloadStream
            Using pngImage As Image = Image.FromStream(payloadStream)
                Return New Bitmap(pngImage)
            End Using
        End Using
    End Function

    ''' <summary>
    ''' The decode itself. Returns the encoded bytes rather than a GDI+ image, so the
    ''' cache can keep exactly what was produced and the timing that justifies keeping it.
    ''' </summary>
    Public Function DecodeToPayload(stream As MemoryStream, Optional sourceLabel As String = "") As DecodedPayload Implements IImageDecoderPayload.DecodeToPayload
        Dim watch As Stopwatch = Stopwatch.StartNew()
        stream.Position = 0

        ' AVIF/HEIC/HEIF are ISO-BMFF containers ("ftyp" box) and JPEG XL has its own
        ' signature; ImageSharp would throw UnknownImageFormatException on all of them, so
        ' route by signature - the MemoryStream has no file name to key on.
        Dim head As Byte() = ReadHead(stream)
        If ImageDecoderProvider.LooksJxl(head) Then Return PayloadViaMagickJxl(stream, watch, sourceLabel)
        If IsIsoBmff(head) Then Return PayloadViaMagick(stream, watch, sourceLabel)

        ' Ask the header first (audit 048, A-17): a crafted animated WEBP of a few KB can
        ' demand tens of GB once decoded, and the prefetch would start that unasked.
        Dim info As SixLabors.ImageSharp.ImageInfo = SixLabors.ImageSharp.Image.Identify(stream)
        stream.Position = 0
        Dim frames As Integer = If(info.FrameMetadataCollection Is Nothing, 1, info.FrameMetadataCollection.Count)
        If Not DecodeBudget.IsWithinBudget(info.Width, info.Height, frames) Then
            AppFileLogger.WriteLine(RefusalText(info.Width, info.Height, frames, sourceLabel))
            Return Nothing
        End If

        Using sharpImage As SixLabors.ImageSharp.Image = SixLabors.ImageSharp.Image.Load(stream)
            If sharpImage.Frames.Count > 1 Then Return TranscodeAnimationToGif(sharpImage, watch)

            Using pngStream As New MemoryStream()
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(sharpImage, pngStream)
                Return New DecodedPayload With {
                    .Bytes = pngStream.ToArray(),
                    .Kind = DecodedPayloadKind.Png,
                    .IsAnimation = False,
                    .DecodeMs = watch.ElapsedMilliseconds
                }
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Stage 1 of a long animation (spec 241 rules 10 and 18): the first frame only, in the time it
    ''' takes to read it, never the whole animation. See <see cref="IImageDecoderPayload.DecodeFirstFramePayload"/>.
    ''' </summary>
    Public Function DecodeFirstFramePayload(stream As MemoryStream, Optional sourceLabel As String = "") As DecodedPayload Implements IImageDecoderPayload.DecodeFirstFramePayload
        Dim watch As Stopwatch = Stopwatch.StartNew()
        stream.Position = 0

        ' The Magick routes have their own budget and are never staged (spec 241 section 3.8).
        Dim head As Byte() = ReadHead(stream)
        If ImageDecoderProvider.LooksJxl(head) OrElse IsIsoBmff(head) Then Return DecodeToPayload(stream, sourceLabel)

        Dim info As SixLabors.ImageSharp.ImageInfo = SixLabors.ImageSharp.Image.Identify(stream)
        stream.Position = 0
        Dim frames As Integer = If(info.FrameMetadataCollection Is Nothing, 1, info.FrameMetadataCollection.Count)
        Dim verdict As BudgetVerdict = DecodeBudget.Classify(info.Width, info.Height, frames)
        If verdict = BudgetVerdict.FrameTooLarge Then
            AppFileLogger.WriteLine(RefusalText(info.Width, info.Height, frames, sourceLabel))
            Return Nothing
        End If
        If frames <= 1 Then Return DecodeToPayload(stream, sourceLabel)
        ' Over the budget but one frame fits: the first frame is all the viewer will show (rule 18),
        ' and the file is named for the log all the same (rule 7).
        If verdict = BudgetVerdict.AnimationTooLong Then AppFileLogger.WriteLine(RefusalText(info.Width, info.Height, frames, sourceLabel))

        Dim options As New SixLabors.ImageSharp.Formats.DecoderOptions With {.MaxFrames = 1}
        Using firstFrame As SixLabors.ImageSharp.Image = SixLabors.ImageSharp.Image.Load(options, stream)
            Using pngStream As New MemoryStream()
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(firstFrame, pngStream)
                Return New DecodedPayload With {
                    .Bytes = pngStream.ToArray(),
                    .Kind = DecodedPayloadKind.Png,
                    .IsAnimation = False,
                    .IsPreview = True,
                    .IsTooLong = (verdict = BudgetVerdict.AnimationTooLong),
                    .DecodeMs = watch.ElapsedMilliseconds,
                    .Width = info.Width,
                    .Height = info.Height,
                    .Frames = frames,
                    .DurationMs = HeaderDurationMs(info),
                    .HasAlpha = HeaderHasAlpha(stream, info)
                }
            End Using
        End Using
    End Function

    ''' <summary>The animation's length by its header: the sum of the frame delays, in ms. Only WEBP
    ''' carries a delay ImageSharp reports here; 0 for anything else (an unknown beats a guess, as
    ''' <c>SourceDurationMsAsFfmpegReadsIt</c> has it).</summary>
    Friend Shared Function HeaderDurationMs(info As SixLabors.ImageSharp.ImageInfo) As Long
        Try
            If info Is Nothing OrElse info.FrameMetadataCollection Is Nothing Then Return 0
            Dim total As Long = 0
            For Each frameMeta In info.FrameMetadataCollection
                Dim webpMeta = frameMeta.GetFormatMetadata(SixLabors.ImageSharp.Formats.Webp.WebpFormat.Instance)
                If webpMeta IsNot Nothing Then total += CLng(webpMeta.FrameDelay)
            Next
            Return total
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0039: animation duration from the header skipped: " & ex.Message)
            Return 0
        End Try
    End Function

    ''' <summary>Whether the header says the animation has transparency. A WEBP says it in the alpha
    ''' flag (0x10) of its VP8X chunk, which ImageSharp's Identify does not surface in the pixel type;
    ''' any other format is asked through the pixel type.</summary>
    Friend Shared Function HeaderHasAlpha(stream As Stream, info As SixLabors.ImageSharp.ImageInfo) As Boolean
        Try
            Dim riff(20) As Byte
            stream.Position = 0
            Dim read As Integer = stream.Read(riff, 0, riff.Length)
            stream.Position = 0
            If read = riff.Length AndAlso riff(0) = &H52 AndAlso riff(1) = &H49 AndAlso riff(2) = &H46 AndAlso riff(3) = &H46 AndAlso
               riff(8) = &H57 AndAlso riff(9) = &H45 AndAlso riff(10) = &H42 AndAlso riff(11) = &H50 AndAlso
               riff(12) = &H56 AndAlso riff(13) = &H50 AndAlso riff(14) = &H38 AndAlso riff(15) = &H58 Then
                Return (riff(20) And &H10) <> 0
            End If
            If info Is Nothing Then Return False
            Dim alpha = info.PixelType.AlphaRepresentation
            Return alpha.HasValue AndAlso alpha.Value <> SixLabors.ImageSharp.PixelFormats.PixelAlphaRepresentation.None
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0039: animation alpha from the header skipped: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' The "refused" log line (spec 241 rule 7): which limit, the header's figures and - when
    ''' the caller knows it - the file. The lines the prefetch writes for a neighbour were once
    ''' read as the open file's, because the old text carried no path.
    ''' </summary>
    Friend Shared Function RefusalText(width As Long, height As Long, frames As Long, sourceLabel As String) As String
        Dim reason As String = If(DecodeBudget.Classify(width, height, frames) = BudgetVerdict.AnimationTooLong,
                                  "animation too long", "frame too large")
        Return "Decode refused, over budget (" & reason & "): " & width.ToString() & "x" & height.ToString() &
               " x " & frames.ToString() & " frames" & If(String.IsNullOrEmpty(sourceLabel), "", ": " & sourceLabel)
    End Function

    ''' <summary>
    ''' Animated WEBP/APNG -> GIF bytes. GDI+ animates GIF natively (RawFormat = Gif), so
    ''' Main_Form.GifPlayback picks the result up as if the file had been a GIF.
    ''' </summary>
    Private Function TranscodeAnimationToGif(sharpImage As SixLabors.ImageSharp.Image, watch As Stopwatch) As DecodedPayload
        CopyWebpFrameDelaysToGif(sharpImage)

        ' Spec 241 rule 3: dither only where it is cheap. Up to the policy's threshold the
        ' encoder is exactly the default one, so the payload is byte-identical to what was
        ' produced before the policy existed.
        Dim dither As Boolean = GifTranscodePolicy.UseDither(sharpImage.Width, sharpImage.Height, sharpImage.Frames.Count)
        Dim encodeWatch As Stopwatch = Stopwatch.StartNew()
        Dim gifBytes As Byte() = EncodeAnimationGif(sharpImage, dither)
        encodeWatch.Stop()

        Return New DecodedPayload With {
            .Bytes = gifBytes,
            .Kind = DecodedPayloadKind.Gif,
            .IsAnimation = True,
            .DecodeMs = watch.ElapsedMilliseconds,
            .Width = sharpImage.Width,
            .Height = sharpImage.Height,
            .Frames = sharpImage.Frames.Count,
            .EncodeMs = encodeWatch.ElapsedMilliseconds,
            .Dithered = dither
        }
    End Function

    ''' <summary>
    ''' The GIF bytes of a loaded animation. With <paramref name="dither"/> the encoder is
    ''' exactly the default one; without it, an Octree quantizer with dithering switched off
    ''' (same time as Wu, a smaller file - measured, spec 241 rule 3).
    ''' </summary>
    Friend Shared Function EncodeAnimationGif(sharpImage As SixLabors.ImageSharp.Image, dither As Boolean) As Byte()
        Using gifStream As New MemoryStream()
            If dither Then
                SixLabors.ImageSharp.ImageExtensions.SaveAsGif(sharpImage, gifStream)
            Else
                Dim quantizerOptions As New SixLabors.ImageSharp.Processing.Processors.Quantization.QuantizerOptions With {.Dither = Nothing}
                Dim encoder As New SixLabors.ImageSharp.Formats.Gif.GifEncoder With {
                    .Quantizer = New SixLabors.ImageSharp.Processing.Processors.Quantization.OctreeQuantizer(quantizerOptions)
                }
                SixLabors.ImageSharp.ImageExtensions.SaveAsGif(sharpImage, gifStream, encoder)
            End If
            Return gifStream.ToArray()
        End Using
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
            Dim head As Byte() = ReadHead(stream)
            If IsIsoBmff(head) OrElse ImageDecoderProvider.LooksJxl(head) Then
                ' The process-wide limits first, as the decode does (audit 057.S01-3): the
                ' size label is often the FIRST Magick call of a session - the prefetch
                ' worker asks it before any decode - and it ran with Magick's defaults.
                MagickResourceLimits.EnsureApplied()
                stream.Position = 0
                Dim magickInfo As New ImageMagick.MagickImageInfo(stream)
                If magickInfo.Width <= 0 OrElse magickInfo.Height <= 0 Then Return Size.Empty
                ' The size as DISPLAYED (audit 048, A-21): the decode auto-orients by this
                ' very tag when Is_Exif_AutoRotate is on, so the label must swap with it.
                If Is_Exif_AutoRotate AndAlso Utils.IsQuarterTurnOrientation(CInt(magickInfo.Orientation)) Then
                    Return New Size(CInt(magickInfo.Height), CInt(magickInfo.Width))
                End If
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
    ''' The first twelve bytes (fewer for a shorter stream), for the signature tests. Leaves
    ''' the position at 0.
    ''' </summary>
    Private Shared Function ReadHead(stream As Stream) As Byte()
        Dim head(11) As Byte
        stream.Position = 0
        Dim read As Integer = stream.Read(head, 0, head.Length)
        stream.Position = 0
        If read < head.Length Then ReDim Preserve head(read - 1)
        Return head
    End Function

    ''' <summary>
    ''' True when the head is an ISO-BMFF "ftyp" box (AVIF/HEIC/HEIF). No other image format
    ''' this decoder sees uses that container (JPEG XL's own "ftyp" comes after its signature
    ''' box, so it never lands at offset 4), so the brand does not need parsing.
    ''' </summary>
    Private Shared Function IsIsoBmff(head As Byte()) As Boolean
        If head.Length < 12 Then Return False
        Return head(4) = Asc("f"c) AndAlso head(5) = Asc("t"c) AndAlso head(6) = Asc("y"c) AndAlso head(7) = Asc("p"c)
    End Function

    ''' <summary>
    ''' Magick.NET decode for the ISO-BMFF formats: first frame, re-encoded to PNG the same
    ''' way the ImageSharp path does. Honors the EXIF auto-rotate setting here because the
    ''' PNG re-encode drops the Orientation tag before FileManager.ApplyExifOrientation
    ''' could see it (iPhone HEICs rely on that tag) - which is also why Is_Exif_AutoRotate
    ''' is part of the cache key (§4).
    ''' </summary>
    Private Shared Function PayloadViaMagick(stream As MemoryStream, watch As Stopwatch, sourceLabel As String) As DecodedPayload
        stream.Position = 0

        ' The same budget as the ImageSharp path (audit 048, A-17), from the header. Only the
        ' first frame is decoded here, so the frame count does not enter into it. The
        ' process-wide Magick limits are applied first, from their one place.
        MagickResourceLimits.EnsureApplied()
        Dim header As New ImageMagick.MagickImageInfo(stream)
        stream.Position = 0
        If Not DecodeBudget.IsWithinBudget(CLng(header.Width), CLng(header.Height), 1) Then
            AppFileLogger.WriteLine(RefusalText(CLng(header.Width), CLng(header.Height), 1, sourceLabel))
            Return Nothing
        End If

        Return MagickStillToPng(stream, watch)
    End Function

    ''' <summary>
    ''' Magick.NET decode for JPEG XL (spec 236). A still goes the way an AVIF does; a file with
    ''' several frames becomes an in-memory GIF, as an animated WEBP does, so the GDI+ playback
    ''' that already animates GIFs shows it unchanged.
    ''' The budget is asked FIRST and from the headers of ALL frames (a ping, no pixels):
    ''' a crafted animation of a few KB can claim thousands of frames, and the still path's
    ''' "first frame only" budget would not see that.
    ''' </summary>
    Private Shared Function PayloadViaMagickJxl(stream As MemoryStream, watch As Stopwatch, sourceLabel As String) As DecodedPayload
        stream.Position = 0
        MagickResourceLimits.EnsureApplied()
        Dim headers As List(Of ImageMagick.IMagickImageInfo) = ImageMagick.MagickImageInfo.ReadCollection(stream).ToList()
        stream.Position = 0
        If headers.Count = 0 Then Return Nothing
        If Not DecodeBudget.IsWithinBudget(CLng(headers(0).Width), CLng(headers(0).Height), headers.Count) Then
            AppFileLogger.WriteLine(RefusalText(CLng(headers(0).Width), CLng(headers(0).Height), headers.Count, sourceLabel))
            Return Nothing
        End If

        If headers.Count = 1 Then Return MagickStillToPng(stream, watch)
        Return MagickAnimationToGif(stream, watch)
    End Function

    ''' <summary>The first frame of a Magick-readable file, oriented if asked, as a PNG payload.</summary>
    Private Shared Function MagickStillToPng(stream As MemoryStream, watch As Stopwatch) As DecodedPayload
        Using magickImage As New ImageMagick.MagickImage(stream)
            If Is_Exif_AutoRotate Then magickImage.AutoOrient()

            Using pngStream As New MemoryStream()
                magickImage.Write(pngStream, ImageMagick.MagickFormat.Png)
                Return New DecodedPayload With {
                    .Bytes = pngStream.ToArray(),
                    .Kind = DecodedPayloadKind.Png,
                    .IsAnimation = False,
                    .DecodeMs = watch.ElapsedMilliseconds
                }
            End Using
        End Using
    End Function

    ''' <summary>
    ''' A multi-frame Magick-readable file as GIF bytes. Frames are coalesced to full canvases
    ''' first, so each GIF frame replaces the last (dispose = background keeps a transparent
    ''' pixel from showing the frame before it). GIF counts in hundredths of a second and
    ''' Magick keeps the source's own tick rate, so each delay is converted through
    ''' milliseconds; a zero duration (a JPEG XL layer, not a shown frame) takes the 100 ms
    ''' browsers give a zero-delay GIF. 256 colours per frame, as for the animated WEBP.
    ''' </summary>
    Private Shared Function MagickAnimationToGif(stream As MemoryStream, watch As Stopwatch) As DecodedPayload
        Using frames As New ImageMagick.MagickImageCollection(stream)
            frames.Coalesce()
            For Each frame As ImageMagick.IMagickImage(Of Byte) In frames
                If Is_Exif_AutoRotate Then frame.AutoOrient()
                Dim ticksPerSecond As Long = Math.Max(1L, CLng(frame.AnimationTicksPerSecond))
                Dim milliseconds As Long = CLng(frame.AnimationDelay) * 1000L \ ticksPerSecond
                frame.AnimationTicksPerSecond = 100
                frame.AnimationDelay = CUInt(If(milliseconds <= 0L, 10L, Math.Max(1L, milliseconds \ 10L)))
                frame.GifDisposeMethod = ImageMagick.GifDisposeMethod.Background
            Next

            Using gifStream As New MemoryStream()
                frames.Write(gifStream, ImageMagick.MagickFormat.Gif)
                Return New DecodedPayload With {
                    .Bytes = gifStream.ToArray(),
                    .Kind = DecodedPayloadKind.Gif,
                    .IsAnimation = True,
                    .DecodeMs = watch.ElapsedMilliseconds
                }
            End Using
        End Using
    End Function

End Class
#End If
