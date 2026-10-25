#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports ImageMagick

''' <summary>
''' .NET 10 decoder: Magick.NET (ImageMagick, Apache-2.0) with no OS codec, so WEBP (including
''' animated) works on Server editions without the Store "WebP Image Extensions" (the original
''' Server 2025 bug that triggered the epic), and AVIF/HEIC/HEIF (epic O-3) and JPEG XL (spec
''' 236) open without the paid Store "HEVC Video Extensions". Static frames re-encode to PNG and
''' detach; animated images transcode to an in-memory GIF so the existing GDI+ GIF playback
''' pipeline animates them without any UI changes (256-colour cost accepted).
''' This whole file compiles only in the modern build.
'''
''' Specification 270 replaced the managed decoder this class was built on; the behaviour it
''' had is the contract: the container is read from the bytes (<see cref="ImageSignature"/>) and
''' only the formats the seam always read are decoded - every read pins its coder through
''' <see cref="MagickReadGate"/>. The decode budget is asked from the header first: the
''' container's own chunk headers for WEBP, PNG/APNG and GIF (<see cref="AnimationHeader"/>,
''' <see cref="GifBlockWalker"/>), a Magick ping (no pixels) for the rest.
'''
''' There is exactly ONE decode implementation here, and it is <see cref="DecodeToPayload"/> -
''' the encoded bytes (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md section 3.1).
''' <see cref="DecodeToImage"/> is a wrapper that hands those bytes to GDI+, and the decode
''' cache stores the very same bytes. Two decode paths that could drift apart is precisely what
''' the payload interface exists to avoid.
''' </summary>
Friend NotInheritable Class ModernMagickDecoder
    Implements IImageDecoder, IImageDecoderPayload

    ''' <summary>What the header says before any pixel is decoded.</summary>
    Private NotInheritable Class HeaderFacts
        Friend Width As Long
        Friend Height As Long
        Friend Frames As Integer
        Friend DurationMs As Long
        ''' <summary>Nothing when the header does not say; the decoded frame then answers.</summary>
        Friend HasAlpha As Boolean?
    End Class

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
    ''' Nothing for a container this decoder does not read and for a refused budget.
    ''' </summary>
    Public Function DecodeToPayload(stream As MemoryStream, Optional sourceLabel As String = "") As DecodedPayload Implements IImageDecoderPayload.DecodeToPayload
        Dim watch As Stopwatch = Stopwatch.StartNew()
        stream.Position = 0
        Dim container As ImageContainer = MagickReadGate.Sniff(stream)
        If container = ImageContainer.Unknown Then
            AppFileLogger.WriteLine("Decode refused, unrecognised image format" & LabelSuffix(sourceLabel))
            Return Nothing
        End If

        ' Ask the header first (audit 048, A-17): a crafted animated WEBP of a few KB can
        ' demand tens of GB once decoded, and the prefetch would start that unasked.
        Dim facts As HeaderFacts = ReadHeaderFacts(stream, container)
        If facts Is Nothing Then Return Nothing
        If Not DecodeBudget.IsWithinBudget(facts.Width, facts.Height, facts.Frames) Then
            AppFileLogger.WriteLine(RefusalText(facts.Width, facts.Height, facts.Frames, sourceLabel))
            Return Nothing
        End If

        If facts.Frames > 1 Then Return TranscodeAnimationToGif(stream, container, watch)

        Using still As MagickImage = MagickReadGate.ReadFirstFrame(stream, container)
            ' The ISO-BMFF and JPEG XL routes honour the EXIF auto-rotate setting here because
            ' the PNG re-encode drops the Orientation tag before FileManager.ApplyExifOrientation
            ' could see it (iPhone HEICs rely on that tag) - which is also why Is_Exif_AutoRotate
            ' is part of the cache key. The other containers never were oriented here.
            If Is_Exif_AutoRotate AndAlso OrientsHere(container) Then still.AutoOrient()
            Return New DecodedPayload With {
                .Bytes = MagickReadGate.ToPngBytes(still),
                .Kind = DecodedPayloadKind.Png,
                .IsAnimation = False,
                .DecodeMs = watch.ElapsedMilliseconds
            }
        End Using
    End Function

    ''' <summary>
    ''' Stage 1 of a long animation (spec 241 rules 10 and 18): the first frame only, in the time it
    ''' takes to read it, never the whole animation. See <see cref="IImageDecoderPayload.DecodeFirstFramePayload"/>.
    ''' </summary>
    Public Function DecodeFirstFramePayload(stream As MemoryStream, Optional sourceLabel As String = "") As DecodedPayload Implements IImageDecoderPayload.DecodeFirstFramePayload
        Dim watch As Stopwatch = Stopwatch.StartNew()
        stream.Position = 0
        Dim container As ImageContainer = MagickReadGate.Sniff(stream)

        ' The JPEG XL and ISO-BMFF routes have their own budget and are never staged (spec 241
        ' section 3.8); an unknown container is refused by the whole decode.
        If container = ImageContainer.Unknown OrElse OrientsHere(container) Then Return DecodeToPayload(stream, sourceLabel)

        Dim facts As HeaderFacts = ReadHeaderFacts(stream, container)
        If facts Is Nothing Then Return Nothing
        Dim verdict As BudgetVerdict = DecodeBudget.Classify(facts.Width, facts.Height, facts.Frames)
        If verdict = BudgetVerdict.FrameTooLarge Then
            AppFileLogger.WriteLine(RefusalText(facts.Width, facts.Height, facts.Frames, sourceLabel))
            Return Nothing
        End If
        If facts.Frames <= 1 Then Return DecodeToPayload(stream, sourceLabel)
        ' Over the budget but one frame fits: the first frame is all the viewer will show (rule 18),
        ' and the file is named for the log all the same (rule 7).
        If verdict = BudgetVerdict.AnimationTooLong Then AppFileLogger.WriteLine(RefusalText(facts.Width, facts.Height, facts.Frames, sourceLabel))

        Using firstFrame As MagickImage = MagickReadGate.ReadFirstFrame(stream, container)
            Return New DecodedPayload With {
                .Bytes = MagickReadGate.ToPngBytes(firstFrame),
                .Kind = DecodedPayloadKind.Png,
                .IsAnimation = False,
                .IsPreview = True,
                .IsTooLong = (verdict = BudgetVerdict.AnimationTooLong),
                .DecodeMs = watch.ElapsedMilliseconds,
                .Width = CInt(facts.Width),
                .Height = CInt(facts.Height),
                .Frames = facts.Frames,
                .DurationMs = facts.DurationMs,
                .HasAlpha = If(facts.HasAlpha, firstFrame.HasAlpha)
            }
        End Using
    End Function

    ''' <summary>
    ''' The canvas, frame count, duration and transparency by the header, or Nothing (logged) when
    ''' the header cannot be read. The duration is the WEBP one only - the managed decoder this
    ''' replaced reported no other, and an unknown beats a guess, as
    ''' <c>SourceDurationMsAsFfmpegReadsIt</c> has it.
    ''' </summary>
    Private Shared Function ReadHeaderFacts(stream As MemoryStream, container As ImageContainer) As HeaderFacts
        Try
            Select Case container
                Case ImageContainer.Webp
                    Dim webp As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
                    If webp IsNot Nothing Then
                        Return New HeaderFacts With {.Width = webp.Width, .Height = webp.Height, .Frames = webp.Frames,
                                                     .DurationMs = webp.DurationMs, .HasAlpha = webp.HasAlpha}
                    End If
                Case ImageContainer.Png
                    Dim png As AnimationHeaderInfo = AnimationHeader.ReadPng(stream)
                    If png IsNot Nothing Then
                        Return New HeaderFacts With {.Width = png.Width, .Height = png.Height, .Frames = png.Frames, .HasAlpha = png.HasAlpha}
                    End If
                Case ImageContainer.Gif
                    stream.Position = 0
                    Dim gif As GifWalkResult = GifBlockWalker.Walk(stream)
                    stream.Position = 0
                    If gif.IsGif AndAlso gif.Width > 0 AndAlso gif.Height > 0 Then
                        Return New HeaderFacts With {.Width = gif.Width, .Height = gif.Height, .Frames = Math.Max(1, gif.Frames)}
                    End If
                Case Else
                    Dim headers As List(Of IMagickImageInfo) = MagickReadGate.PingAll(stream, container)
                    If headers.Count > 0 Then
                        ' The ISO-BMFF route decodes the primary image only (epic O-3), so its frame
                        ' count does not enter the budget.
                        Dim frames As Integer = If(container = ImageContainer.Avif OrElse container = ImageContainer.Heif, 1, headers.Count)
                        Return New HeaderFacts With {.Width = headers(0).Width, .Height = headers(0).Height, .Frames = frames}
                    End If
            End Select
        Catch ex As Exception
            AppFileLogger.WriteLine("Decode refused, unreadable image header (" & container.ToString() & "): " & ex.GetType().Name)
            Return Nothing
        Finally
            stream.Position = 0
        End Try
        AppFileLogger.WriteLine("Decode refused, unreadable image header (" & container.ToString() & ")")
        Return Nothing
    End Function

    ''' <summary>True for the containers whose decode orients the pixels itself (see DecodeToPayload).</summary>
    Private Shared Function OrientsHere(container As ImageContainer) As Boolean
        Return container = ImageContainer.Avif OrElse container = ImageContainer.Heif OrElse container = ImageContainer.Jxl
    End Function

    Private Shared Function LabelSuffix(sourceLabel As String) As String
        Return If(String.IsNullOrEmpty(sourceLabel), "", ": " & sourceLabel)
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
               " x " & frames.ToString() & " frames" & LabelSuffix(sourceLabel)
    End Function

    ''' <summary>
    ''' An animation -> GIF bytes. GDI+ animates GIF natively (RawFormat = Gif), so
    ''' Main_Form.GifPlayback picks the result up as if the file had been a GIF.
    ''' </summary>
    Private Shared Function TranscodeAnimationToGif(stream As MemoryStream, container As ImageContainer, watch As Stopwatch) As DecodedPayload
        Using frames As MagickImageCollection = MagickReadGate.ReadAll(stream, container)
            Dim width As Integer = CInt(frames(0).Width)
            Dim height As Integer = CInt(frames(0).Height)
            Dim count As Integer = frames.Count
            If container = ImageContainer.Jxl AndAlso Is_Exif_AutoRotate Then
                For Each frame As IMagickImage(Of Byte) In frames
                    frame.AutoOrient()
                Next
            End If

            ' Spec 241 rule 3: dither only where it is cheap.
            Dim dither As Boolean = GifTranscodePolicy.UseDither(width, height, count)
            Dim encodeWatch As Stopwatch = Stopwatch.StartNew()
            ' A zero duration in a JPEG XL is a layer of one picture shown for the 100 ms
            ' browsers give a zero-delay GIF (spec 236); elsewhere zero stays zero, as before.
            Dim gifBytes As Byte() = EncodeAnimationGif(frames, dither, If(container = ImageContainer.Jxl, 10, 0))
            encodeWatch.Stop()

            Return New DecodedPayload With {
                .Bytes = gifBytes,
                .Kind = DecodedPayloadKind.Gif,
                .IsAnimation = True,
                .DecodeMs = watch.ElapsedMilliseconds,
                .Width = width,
                .Height = height,
                .Frames = count,
                .EncodeMs = encodeWatch.ElapsedMilliseconds,
                .Dithered = dither
            }
        End Using
    End Function

    ''' <summary>
    ''' The GIF bytes of a decoded animation. Frames are coalesced to full canvases first, so each
    ''' GIF frame replaces the last (dispose = background keeps a transparent pixel from showing
    ''' the frame before it). GIF counts in hundredths of a second and ImageMagick keeps the
    ''' source's own tick rate, so each delay is converted through milliseconds and truncated, as
    ''' the WEBP delays always were; a zero delay becomes <paramref name="zeroDelayCs"/>. Each frame
    ''' gets its own 256-colour palette: Floyd-Steinberg with <paramref name="dither"/>, none
    ''' without (spec 241 rule 3). The loop count of the source is kept.
    ''' </summary>
    Friend Shared Function EncodeAnimationGif(frames As MagickImageCollection, dither As Boolean, Optional zeroDelayCs As Integer = 0) As Byte()
        frames.Coalesce()
        Dim quantize As New QuantizeSettings With {
            .Colors = 256UI,
            .DitherMethod = If(dither, DitherMethod.FloydSteinberg, DitherMethod.No)
        }
        For Each frame As IMagickImage(Of Byte) In frames
            frame.AnimationDelay = CUInt(GifDelayCs(frame.AnimationDelay, frame.AnimationTicksPerSecond, zeroDelayCs))
            frame.AnimationTicksPerSecond = 100
            frame.GifDisposeMethod = GifDisposeMethod.Background
        Next
        ' The Magick.NET Q8 build has no OpenMP (ResourceLimits.Thread = 1), so one frame is
        ' quantized on one core. After Coalesce every frame is a whole picture of its own, so
        ' the frames go side by side - the replaced decoder's quantizer used every core too, and
        ' serially a 90-frame 960x540 animation took twice its time (specification 270 section 6).
        ' By index: LINQ's ToArray goes through the collection's CopyTo, which hands out clones.
        Dim pictures(frames.Count - 1) As IMagickImage(Of Byte)
        For i As Integer = 0 To frames.Count - 1
            pictures(i) = frames(i)
        Next
        Threading.Tasks.Parallel.For(0, pictures.Length, Sub(i) pictures(i).Quantize(quantize))

        Using gifStream As New MemoryStream()
            frames.Write(gifStream, MagickFormat.Gif)
            Return gifStream.ToArray()
        End Using
    End Function

    ''' <summary>A frame delay in GIF centiseconds: through milliseconds, truncated, at least 1;
    ''' a zero (or negative) delay is <paramref name="zeroDelayCs"/>. Pure.</summary>
    Friend Shared Function GifDelayCs(delay As Long, ticksPerSecond As Long, zeroDelayCs As Integer) As Long
        Dim milliseconds As Long = delay * 1000L \ Math.Max(1L, ticksPerSecond)
        If milliseconds <= 0L Then Return zeroDelayCs
        Return Math.Max(1L, milliseconds \ 10L)
    End Function

    Public Function TryGetPixelSize(stream As Stream) As Size Implements IImageDecoder.TryGetPixelSize
        Try
            Dim container As ImageContainer = MagickReadGate.Sniff(stream)
            Select Case container
                Case ImageContainer.Unknown
                    Return Size.Empty
                Case ImageContainer.Webp
                    Dim webp As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
                    Return If(webp Is Nothing, Size.Empty, New Size(webp.Width, webp.Height))
                Case ImageContainer.Png
                    Dim png As AnimationHeaderInfo = AnimationHeader.ReadPng(stream)
                    Return If(png Is Nothing, Size.Empty, New Size(png.Width, png.Height))
            End Select

            ' The process-wide limits come first, as for the decode (audit 057.S01-3): the size
            ' label is often the FIRST Magick call of a session - the prefetch worker asks it
            ' before any decode. PingAll applies them.
            Dim headers As List(Of IMagickImageInfo) = MagickReadGate.PingAll(stream, container)
            If headers.Count = 0 Then Return Size.Empty
            Dim info As IMagickImageInfo = headers(0)
            If info.Width <= 0 OrElse info.Height <= 0 Then Return Size.Empty
            ' The size as DISPLAYED (audit 048, A-21): the decode auto-orients by this
            ' very tag when Is_Exif_AutoRotate is on, so the label must swap with it.
            If Is_Exif_AutoRotate AndAlso OrientsHere(container) AndAlso Utils.IsQuarterTurnOrientation(CInt(info.Orientation)) Then
                Return New Size(CInt(info.Height), CInt(info.Width))
            End If
            Return New Size(CInt(info.Width), CInt(info.Height))
        Catch
            Return Size.Empty
        Finally
            If stream.CanSeek Then stream.Position = 0
        End Try
    End Function

End Class
#End If
