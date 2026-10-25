#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports ImageMagick
Imports Xunit

''' <summary>
''' Specification 270: the pieces the move from ImageSharp to Magick.NET added to the mainline -
''' the signature table that pins every read's coder (R1 and the security section), the header
''' walker the decode budget and the first-frame preview read from (R4, R8), the resource
''' limits (R7) and the QR fallback's format reach (R1, R6).
''' </summary>
Public Class ImageBackendMigrationTests

    ' ------------------------------------------------------------- ImageSignature ----

    Public Shared Iterator Function KnownHeads() As IEnumerable(Of Object())
        Yield New Object() {"webp", Bytes("RIFF", 0, 0, 0, 0, "WEBP", "VP8 "), ImageContainer.Webp}
        Yield New Object() {"png", Bytes(&H89, "PNG", &HD, &HA, &H1A, &HA), ImageContainer.Png}
        Yield New Object() {"jpeg", Bytes(&HFF, &HD8, &HFF, &HE0), ImageContainer.Jpeg}
        Yield New Object() {"gif87a", Bytes("GIF87a"), ImageContainer.Gif}
        Yield New Object() {"gif89a", Bytes("GIF89a"), ImageContainer.Gif}
        Yield New Object() {"tiff-le", Bytes("II", &H2A, 0), ImageContainer.Tiff}
        Yield New Object() {"tiff-be", Bytes("MM", 0, &H2A), ImageContainer.Tiff}
        Yield New Object() {"bigtiff", Bytes("II", &H2B, 0, 8, 0, 0, 0), ImageContainer.Tiff}
        Yield New Object() {"bmp", BmpHead(40), ImageContainer.Bmp}
        Yield New Object() {"bmp-v5", BmpHead(124), ImageContainer.Bmp}
        Yield New Object() {"pbm-p6", Bytes("P6", &HA, "4 4", &HA), ImageContainer.Pbm}
        Yield New Object() {"pbm-p1", Bytes("P1 1 1"), ImageContainer.Pbm}
        Yield New Object() {"qoi", Bytes("qoif", 0, 0, 0, 4), ImageContainer.Qoi}
        Yield New Object() {"tga", TgaHead(), ImageContainer.Tga}
        Yield New Object() {"avif", Bytes(0, 0, 0, &H1C, "ftypavif"), ImageContainer.Avif}
        Yield New Object() {"avis", Bytes(0, 0, 0, &H1C, "ftypavis"), ImageContainer.Avif}
        Yield New Object() {"heic", Bytes(0, 0, 0, &H18, "ftypheic"), ImageContainer.Heif}
        Yield New Object() {"mif1", Bytes(0, 0, 0, &H18, "ftypmif1"), ImageContainer.Heif}
        Yield New Object() {"jxl-codestream", Bytes(&HFF, &HA, 0, 0), ImageContainer.Jxl}
        Yield New Object() {"jxl-container", Bytes(0, 0, 0, &HC, "JXL ", &HD, &HA, &H87, &HA), ImageContainer.Jxl}
    End Function

    <Theory>
    <MemberData(NameOf(KnownHeads))>
    Public Sub Every_container_the_old_decoder_read_is_still_recognised(name As String, head As Byte(), expected As Object)
        ' Object, not ImageContainer: the enum is Friend and a public test cannot expose it.
        Dim sniffed As ImageContainer = ImageSignature.Sniff(head)
        Assert.True(CType(expected, ImageContainer) = sniffed, name & " was sniffed as " & sniffed.ToString())
    End Sub

    ''' <summary>
    ''' The point of the table: what ImageMagick would recognise by itself - scripts, vector
    ''' formats that open other files or run delegates - is never handed to it.
    ''' </summary>
    Public Shared Iterator Function RefusedHeads() As IEnumerable(Of Object())
        Yield New Object() {"svg", Bytes("<svg xmlns='http://www.w3.org/2000/svg'/>")}
        Yield New Object() {"xml-svg", Bytes("<?xml version=""1.0""?><svg/>")}
        Yield New Object() {"mvg", Bytes("push graphic-context", &HA, "viewbox 0 0 1 1")}
        Yield New Object() {"msl", Bytes("<?xml version=""1.0""?><image><read filename=""x""/></image>")}
        Yield New Object() {"pdf", Bytes("%PDF-1.7", &HA)}
        Yield New Object() {"postscript", Bytes("%!PS-Adobe-3.0", &HA)}
        Yield New Object() {"text", Bytes("text:/etc/passwd")}
        Yield New Object() {"html", Bytes("<html><body></body></html>")}
        Yield New Object() {"bm-without-dib", Bytes("BM", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0)}
        Yield New Object() {"pbm-without-space", Bytes("P6x")}
        Yield New Object() {"one-byte", Bytes(&H89)}
        Yield New Object() {"empty", Array.Empty(Of Byte)()}
    End Function

    <Theory>
    <MemberData(NameOf(RefusedHeads))>
    Public Sub What_the_table_does_not_know_never_reaches_ImageMagick(name As String, head As Byte())
        Assert.True(ImageContainer.Unknown = ImageSignature.Sniff(head), name & " was sniffed as " & ImageSignature.Sniff(head).ToString())
        Assert.Throws(Of NotSupportedException)(Function() MagickReadGate.SettingsFor(ImageSignature.Sniff(head)))
    End Sub

    <Fact>
    Public Sub A_missing_head_is_unknown()
        Assert.Equal(ImageContainer.Unknown, ImageSignature.Sniff(Nothing))
    End Sub

    <Fact>
    Public Sub An_svg_given_to_the_decoder_is_refused_without_a_decode()
        Using stream As New MemoryStream(Bytes("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>"))
            Assert.Null(New ModernMagickDecoder().DecodeToPayload(stream))
        End Using
    End Sub

    <Fact>
    Public Sub Every_known_container_has_a_coder()
        For Each container As ImageContainer In [Enum].GetValues(GetType(ImageContainer))
            If container = ImageContainer.Unknown Then Continue For
            Assert.NotEqual(MagickFormat.Unknown, MagickReadGate.FormatOf(container))
        Next
    End Sub

    ' ------------------------------------------------------------ AnimationHeader ----

    <Fact>
    Public Sub An_animated_webp_header_gives_canvas_frames_and_duration()
        Dim data As Byte() = AnimatedWebp(3, 40UI, 30UI, delayTicks:=8UI)
        Using stream As New MemoryStream(data)
            stream.Position = 5
            Dim info As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
            Assert.Equal(5L, stream.Position)
            Assert.NotNull(info)
            Assert.Equal(40, info.Width)
            Assert.Equal(30, info.Height)
            Assert.Equal(3, info.Frames)
            Assert.Equal(240L, info.DurationMs)
            Assert.True(info.IsAnimated)
            Assert.True(info.FirstFrameOffset > 12)
        End Using
    End Sub

    <Fact>
    Public Sub A_still_webp_is_one_frame_and_not_animated()
        Using image As New MagickImage(MagickColors.Orange, 17UI, 9UI)
            Dim data As Byte() = image.ToByteArray(MagickFormat.WebP)
            Using stream As New MemoryStream(data)
                Dim info As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
                Assert.NotNull(info)
                Assert.Equal(17, info.Width)
                Assert.Equal(9, info.Height)
                Assert.Equal(1, info.Frames)
                Assert.False(info.IsAnimated)
                Assert.Equal(-1L, info.FirstFrameOffset)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_first_webp_frame_is_cut_out_without_decoding_the_animation()
        Dim data As Byte() = AnimatedWebp(3, 40UI, 30UI, delayTicks:=8UI)
        Using stream As New MemoryStream(data)
            Dim info As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
            Dim frame As WebpFirstFrame = AnimationHeader.ExtractFirstWebpFrame(stream, info)
            Assert.NotNull(frame)
            Using still As New MagickImageCollection(frame.Bytes, New MagickReadSettings With {.Format = MagickFormat.WebP})
                Assert.Single(still)
                Assert.Equal(CUInt(frame.Width), still(0).Width)
                Assert.Equal(CUInt(frame.Height), still(0).Height)
                Dim pixel As IPixel(Of Byte) = still(0).GetPixels().GetPixel(frame.Width \ 2, frame.Height \ 2)
                Assert.Equal(CByte(255), pixel.GetChannel(0))   ' frame 0 is red
                Assert.Equal(CByte(0), pixel.GetChannel(1))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_read_gate_first_frame_is_the_animation_first_frame()
        Dim data As Byte() = AnimatedWebp(3, 40UI, 30UI, delayTicks:=8UI)
        Using stream As New MemoryStream(data)
            Using first As MagickImage = MagickReadGate.ReadFirstFrame(stream, ImageContainer.Webp)
                Assert.Equal(40UI, first.Width)
                Assert.Equal(30UI, first.Height)
                Using all As MagickImageCollection = MagickReadGate.ReadAll(stream, ImageContainer.Webp)
                    all.Coalesce()
                    Assert.Equal(0.0, first.Compare(all(0), ErrorMetric.Absolute))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_truncated_webp_counts_only_the_frames_it_holds()
        Dim data As Byte() = AnimatedWebp(3, 40UI, 30UI, delayTicks:=8UI)
        For Each length As Integer In {data.Length - 2, data.Length * 2 \ 3, data.Length \ 2, 40, 21, 12, 5, 0}
            Dim cut(length - 1) As Byte
            Array.Copy(data, cut, length)
            Using stream As New MemoryStream(cut)
                Dim info As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
                If info IsNot Nothing Then Assert.True(info.Frames < 3, "a cut file still counted " & info.Frames.ToString() & " frames at " & length.ToString())
            End Using
        Next
    End Sub

    <Fact>
    Public Sub A_chunk_claiming_four_gigabytes_ends_the_walk_quietly()
        Dim data As Byte() = AnimatedWebp(2, 20UI, 20UI, delayTicks:=5UI)
        ' The first chunk after the RIFF header is VP8X; make the one after it (ANIM) claim 0xFFFFFFFF bytes.
        Dim vp8xSize As Integer = BitConverter.ToInt32(data, 16)
        Dim next4 As Integer = 12 + 8 + vp8xSize + 4
        data(next4) = &HFF : data(next4 + 1) = &HFF : data(next4 + 2) = &HFF : data(next4 + 3) = &HFF
        Using stream As New MemoryStream(data)
            Dim info As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
            Assert.NotNull(info)
            Assert.Equal(1, info.Frames)
            Assert.Null(AnimationHeader.ExtractFirstWebpFrame(stream, info))
        End Using
    End Sub

    <Fact>
    Public Sub Not_a_webp_or_not_a_png_is_nothing()
        Using png As New MemoryStream(Bytes(&H89, "PNG", &HD, &HA, &H1A, &HA))
            Assert.Null(AnimationHeader.ReadWebp(png))
        End Using
        Using webp As New MemoryStream(Bytes("RIFF", 0, 0, 0, 0, "WEBP"))
            Assert.Null(AnimationHeader.ReadPng(webp))
            Assert.Null(AnimationHeader.ReadWebp(webp))   ' no picture chunk at all
        End Using
    End Sub

    <Fact>
    Public Sub An_apng_header_gives_its_frames_and_a_plain_png_is_a_still()
        Using frames As New MagickImageCollection()
            For Each colour As MagickColor In {MagickColors.Red, MagickColors.Green}
                Dim frame As New MagickImage(colour, 12UI, 8UI)
                frame.AnimationTicksPerSecond = 100UI
                frame.AnimationDelay = 25UI
                frames.Add(frame)
            Next
            Using stream As New MemoryStream()
                frames.Write(stream, MagickFormat.APng)
                Dim info As AnimationHeaderInfo = AnimationHeader.ReadPng(stream)
                Assert.NotNull(info)
                Assert.Equal(12, info.Width)
                Assert.Equal(8, info.Height)
                Assert.Equal(2, info.Frames)
                Assert.Equal(500L, info.DurationMs)
                Assert.True(info.IsAnimated)
                Assert.True(info.DefaultImageIsFirstFrame)
            End Using
        End Using

        Using still As New MagickImage(MagickColors.Red, 12UI, 8UI)
            Using stream As New MemoryStream(still.ToByteArray(MagickFormat.Png))
                Dim info As AnimationHeaderInfo = AnimationHeader.ReadPng(stream)
                Assert.NotNull(info)
                Assert.Equal(1, info.Frames)
                Assert.False(info.IsAnimated)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Webp_metadata_chunks_are_read_without_a_decode()
        Using image As New MagickImage(MagickColors.Teal, 8UI, 8UI)
            Dim exif As New ExifProfile()
            exif.SetValue(ExifTag.Artist, "spec 270")
            image.SetProfile(exif)
            Using stream As New MemoryStream(image.ToByteArray(MagickFormat.WebP))
                Dim payload As Byte() = AnimationHeader.ReadWebpChunk(stream, "EXIF")
                Assert.NotNull(payload)
                Assert.Contains("spec 270", System.Text.Encoding.ASCII.GetString(payload))
                Assert.Null(AnimationHeader.ReadWebpChunk(stream, "ICCP"))
            End Using
        End Using
    End Sub

    ' ------------------------------------------------------------- resource limits ----

    <Fact>
    Public Sub The_magick_area_ceiling_is_the_viewer_decode_budget()
        Assert.Equal(DecodeBudget.Max_Frame_Pixels, MagickResourceLimits.Max_Area_Pixels)
    End Sub

    <Fact>
    Public Sub The_limits_are_in_force_after_a_gate_read()
        Using image As New MagickImage(MagickColors.Red, 4UI, 4UI)
            Using stream As New MemoryStream(image.ToByteArray(MagickFormat.Png))
                MagickReadGate.PingAll(stream, ImageContainer.Png)
            End Using
        End Using
        Assert.Equal(0UL, ImageMagick.ResourceLimits.Disk)
        Assert.Equal(CULng(MagickResourceLimits.Max_Area_Pixels), ImageMagick.ResourceLimits.Area)
    End Sub

    ' ---------------------------------------------------------- QR fallback reach ----

    <Fact>
    Public Sub The_qr_fallback_does_not_read_formats_its_old_decoder_never_read()
        Dim code As String = "FMS-270-reach"
        Using bmp As Bitmap = QrCodeRenderer.RenderBitmap(code, 4)
            Dim png As Byte()
            Using ms As New MemoryStream()
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png)
                png = ms.ToArray()
            End Using
            Using magick As New MagickImage(png)
                Dim webp As Byte() = magick.ToByteArray(MagickFormat.WebP)
                Dim viaWebp As QrDecodeResult = QrCodeDecoder.DecodeBytes(webp)
                Assert.True(viaWebp.Succeeded, "the WEBP control case must decode")

                Dim jxl As Byte() = magick.ToByteArray(MagickFormat.Jxl)
                Assert.Equal(ImageContainer.Jxl, ImageSignature.Sniff(jxl))
                Dim viaJxl As QrDecodeResult = QrCodeDecoder.DecodeBytes(jxl)
                Assert.False(viaJxl.Succeeded, "JPEG XL was never read by the QR fallback (specification 270 R1)")
            End Using
        End Using
    End Sub

    ' -------------------------------------------------------------------- helpers ----

    Private Shared Function AnimatedWebp(frameCount As Integer, width As UInteger, height As UInteger, delayTicks As UInteger) As Byte()
        Dim colours As MagickColor() = {MagickColors.Red, MagickColors.Lime, MagickColors.Blue}
        Using frames As New MagickImageCollection()
            For i As Integer = 0 To frameCount - 1
                Dim frame As New MagickImage(colours(i Mod colours.Length), width, height)
                frame.AnimationTicksPerSecond = 100UI
                frame.AnimationDelay = delayTicks
                frame.Settings.SetDefine(MagickFormat.WebP, "lossless", True)
                frames.Add(frame)
            Next
            Using ms As New MemoryStream()
                frames.Write(ms, MagickFormat.WebP)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    ''' <summary>Bytes from a mix of ASCII strings and byte values.</summary>
    Private Shared Function Bytes(ParamArray parts As Object()) As Byte()
        Dim list As New List(Of Byte)()
        For Each part As Object In parts
            Dim text As String = TryCast(part, String)
            If text IsNot Nothing Then
                list.AddRange(System.Text.Encoding.ASCII.GetBytes(text))
            Else
                list.Add(CByte(CInt(part)))
            End If
        Next
        Return list.ToArray()
    End Function

    Private Shared Function BmpHead(dibSize As Integer) As Byte()
        Dim head(25) As Byte
        head(0) = &H42 : head(1) = &H4D
        head(14) = CByte(dibSize)
        Return head
    End Function

    Private Shared Function TgaHead() As Byte()
        Dim head(17) As Byte
        head(2) = 2          ' uncompressed true colour
        head(12) = 16        ' width 16
        head(14) = 8         ' height 8
        head(16) = 24
        Return head
    End Function

End Class
#End If
