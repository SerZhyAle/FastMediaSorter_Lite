Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text
Imports Xunit
Imports fmsl

''' <summary>
''' Spec 236 (JPEG XL). The mainline decodes .jxl through Magick.NET behind the IImageDecoder
''' seam; the net48 fallback keeps it in web_specific_image_extensions and says "unsupported".
''' This class runs under BOTH target frameworks: the fenced part proves the mainline, the
''' unfenced part proves the x86 build did not grow a decoder it is not allowed to have.
'''
''' Fixtures are written by Magick.NET's own JPEG XL encoder at run time rather than typed as
''' byte tables: a hand-typed codestream cannot be checked by eye, and a fixture that is
''' valid by construction can only fail for a reason that is in the decoder.
''' </summary>
<Collection("DecodeCacheBudget")>
Public Class JpegXlTests

    ' ------------------------------------------------------------ both runtimes ----

    ''' <summary>The bytes of a bare JPEG XL codestream header and of the container's signature box.</summary>
    Private Shared ReadOnly BareHead As Byte() = {&HFF, &HA, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0}
    Private Shared ReadOnly ContainerHead As Byte() = {0, 0, 0, &HC, &H4A, &H58, &H4C, &H20, &HD, &HA, &H87, &HA}

    <Fact>
    Public Sub The_seam_claims_jxl_on_the_mainline_only()
#If NETFRAMEWORK Then
        Assert.False(ImageDecoderProvider.Decoder_Backed_Extensions.Contains(".jxl"))
        ' The x86 build decodes no JPEG XL, so a JPEG XL under any name stays unsupported there.
        Assert.False(ImageDecoderProvider.LooksDecoderBacked(BareHead))
        Assert.False(ImageDecoderProvider.LooksDecoderBacked(ContainerHead))
#Else
        Assert.True(ImageDecoderProvider.Decoder_Backed_Extensions.Contains(".jxl"))
        Assert.True(ImageDecoderProvider.Decoder_Backed_Extensions.Contains(".JXL"))
        Assert.True(ImageDecoderProvider.LooksDecoderBacked(BareHead))
        Assert.True(ImageDecoderProvider.LooksDecoderBacked(ContainerHead))
#End If
    End Sub

    ''' <summary>
    ''' The two files are scanned on both builds (the same set), only their handling differs: the
    ''' mainline lists .jxl as a displayable image, the x86 build as a "web specific" one that is
    ''' sortable and moved but not drawn - the precedent of .avif/.heic/.heif.
    ''' </summary>
    <Fact>
    Public Sub The_viewer_lists_carry_jxl_on_the_right_side_of_each_seam()
        Dim source As String = ReadSource("Main_Form.vb")
        Dim lines As String() = source.Split(ControlChars.Lf)

        Dim legacyLists As String = FirstLineWith(lines, "web_specific_image_extensions As New HashSet", """.heic""")
        Assert.Contains("""" & ".jxl" & """", legacyLists)

        Dim modernImages As String = FirstLineWith(lines, "Public Image_File_Extensions As String()", """.avif""")
        Assert.Contains("""" & ".jxl" & """", modernImages)

        Dim legacyImages As String = FirstLineWith(lines, "Public Image_File_Extensions As String()", """.webp""}")
        Assert.DoesNotContain("jxl", legacyImages)

        ' The Open dialog offers the file on both builds, as it does for .avif.
        Assert.Contains("*.avif;*.jxl;*.svg", source)
        Assert.Contains("|JPEG XL Files|*.jxl|", source)
    End Sub

    ' ----------------------------------------------------------------- mainline ----

#If Not NETFRAMEWORK Then

    <Fact>
    Public Sub The_signature_test_knows_both_encodings_and_nothing_else()
        Assert.True(ImageDecoderProvider.LooksJxl(BareHead))
        Assert.True(ImageDecoderProvider.LooksJxl(ContainerHead))
        ' Two bytes are all a bare codestream needs; a shorter head says nothing.
        Assert.True(ImageDecoderProvider.LooksJxl(New Byte() {&HFF, &HA}))
        Assert.False(ImageDecoderProvider.LooksJxl(New Byte() {&HFF}))
        Assert.False(ImageDecoderProvider.LooksJxl(Nothing))
        Assert.False(ImageDecoderProvider.LooksJxl(New Byte() {}))
        ' A JPEG starts FF D8, not FF 0A.
        Assert.False(ImageDecoderProvider.LooksJxl(New Byte() {&HFF, &HD8, &HFF, &HE0, 0, 0, 0, 0, 0, 0, 0, 0}))
        ' An ISO-BMFF image (AVIF/HEIC) is a different decoder branch: JPEG XL's own "ftyp" comes
        ' after the signature box, never at offset 4.
        Dim heic As Byte() = {0, 0, 0, &H18, &H66, &H74, &H79, &H70, &H68, &H65, &H69, &H63}
        Assert.False(ImageDecoderProvider.LooksJxl(heic))
        ' The container's signature box is twelve bytes; eight are not enough to claim it.
        Assert.False(ImageDecoderProvider.LooksJxl(New Byte() {0, 0, 0, &HC, &H4A, &H58, &H4C, &H20}))
        ' ... and the magic bytes after "JXL " have to be the right ones.
        Assert.False(ImageDecoderProvider.LooksJxl(New Byte() {0, 0, 0, &HC, &H4A, &H58, &H4C, &H20, 1, 2, 3, 4}))
    End Sub

    <Fact>
    Public Sub A_jxl_is_classified_as_an_image()
        Assert.Equal(MediaKind.Image, MediaKindClassifier.KindOf(".jxl"))
        Assert.Equal(MediaKind.Image, MediaKindClassifier.KindOf("C:\Photos\scan.JXL"))
    End Sub

    <Fact>
    Public Sub A_bare_codestream_decodes_to_the_pictures_size_and_colour()
        Using ms As New MemoryStream(StillJxl(16UI, 8UI))
            Using img As Image = ImageDecoderProvider.Current.DecodeToImage(ms)
                Assert.NotNull(img)
                Assert.Equal(16, img.Width)
                Assert.Equal(8, img.Height)
                Assert.NotEqual(ImageFormat.Gif, img.RawFormat)
                Dim pixel As Color = CType(img, Bitmap).GetPixel(8, 4)
                ' Lossy by default, so "red" is near red.
                Assert.True(pixel.R > 200 AndAlso pixel.G < 60 AndAlso pixel.B < 60, "Expected red, got " & pixel.ToString())
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_container_encoding_decodes_like_the_bare_one()
        Dim container As Byte() = InContainer(StillJxl(16UI, 8UI))
        Assert.True(ImageDecoderProvider.LooksJxl(container))
        Using ms As New MemoryStream(container)
            Using img As Image = ImageDecoderProvider.Current.DecodeToImage(ms)
                Assert.NotNull(img)
                Assert.Equal(New Size(16, 8), img.Size)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_still_is_a_png_payload_and_not_an_animation()
        Using ms As New MemoryStream(StillJxl(16UI, 8UI))
            Dim payload As DecodedPayload = CType(ImageDecoderProvider.Current, IImageDecoderPayload).DecodeToPayload(ms)
            Assert.NotNull(payload)
            Assert.Equal(DecodedPayloadKind.Png, payload.Kind)
            Assert.False(payload.IsAnimation)
        End Using
    End Sub

    ''' <summary>
    ''' The size label comes from the header, through the seam, with no pixel decode and no GDI+
    ''' (the background worker asks it while the UI thread paints) - for both encodings.
    ''' </summary>
    <Fact>
    Public Sub The_size_is_read_from_the_header_for_both_encodings()
        Dim bare As Byte() = StillJxl(24UI, 10UI)
        Using ms As New MemoryStream(bare)
            Assert.Equal(New Size(24, 10), ImageDecoderProvider.Current.TryGetPixelSize(ms))
        End Using
        Using ms As New MemoryStream(InContainer(bare))
            Assert.Equal(New Size(24, 10), ImageDecoderProvider.Current.TryGetPixelSize(ms))
        End Using

        Dim path As String = WriteTemp(bare, ".jxl")
        Try
            Assert.Equal(New Size(24, 10), Utils.GetImageDimensions(path))
        Finally
            File.Delete(path)
        End Try
    End Sub

    ''' <summary>
    ''' A JPEG XL saved under a .jpg name is what a browser or a messenger hands over. The
    ''' header decides, not the name (audit 048 A-16): the size label and the display both work.
    ''' </summary>
    <Fact>
    Public Sub A_jxl_under_a_jpg_name_is_found_by_its_header()
        Dim path As String = WriteTemp(StillJxl(16UI, 8UI), ".jpg")
        Dim saved As Integer = DecodeCacheStore.BudgetMb
        DecodeCacheStore.BudgetMb = 0 ' cache off: this is the decode itself
        Try
            Assert.Equal(New Size(16, 8), Utils.GetImageDimensions(path))

            Dim failure As PathFailureKind = PathFailureKind.None
            Dim loaded As Tuple(Of Image, MemoryStream) = FileManager.LoadImageWithStream(path, failure)
            Assert.NotNull(loaded)
            Try
                Assert.Equal(New Size(16, 8), loaded.Item1.Size)
                Assert.Equal(PathFailureKind.None, failure)
            Finally
                loaded.Item1.Dispose()
                loaded.Item2?.Dispose()
            End Try
        Finally
            DecodeCacheStore.BudgetMb = saved
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub A_jxl_with_its_own_extension_loads_through_the_file_manager()
        Dim path As String = WriteTemp(StillJxl(16UI, 8UI), ".jxl")
        Dim saved As Integer = DecodeCacheStore.BudgetMb
        DecodeCacheStore.BudgetMb = 0
        Try
            Dim failure As PathFailureKind = PathFailureKind.None
            Dim loaded As Tuple(Of Image, MemoryStream) = FileManager.LoadImageWithStream(path, failure)
            Assert.NotNull(loaded)
            Try
                Assert.Equal(New Size(16, 8), loaded.Item1.Size)
                ' A detached bitmap: nothing holds the file's bytes while the picture is on screen.
                Assert.Null(loaded.Item2)
            Finally
                loaded.Item1.Dispose()
            End Try
        Finally
            DecodeCacheStore.BudgetMb = saved
            File.Delete(path)
        End Try
    End Sub

    ''' <summary>
    ''' The label must say what the decode draws (audit 048 A-21): with EXIF auto-rotate on, a
    ''' quarter-turn Orientation swaps the size in BOTH. The default of the setting is on; the
    ''' test does not flip the process-wide flag, since other tests decode in parallel.
    ''' </summary>
    <Fact>
    Public Sub A_quarter_turn_orientation_swaps_the_label_and_the_decode_together()
        Assume_AutoRotate_Is_On()
        Dim rotated As Byte() = StillJxl(16UI, 8UI, ImageMagick.Orientation.RightTop)

        Using ms As New MemoryStream(rotated)
            Assert.Equal(New Size(8, 16), ImageDecoderProvider.Current.TryGetPixelSize(ms))
        End Using
        Using ms As New MemoryStream(rotated)
            Using img As Image = ImageDecoderProvider.Current.DecodeToImage(ms)
                Assert.Equal(New Size(8, 16), img.Size)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' Q236-2: a JPEG XL animation becomes an in-memory GIF, as an animated WEBP does, so the
    ''' GDI+ playback that already animates GIFs shows it; the per-frame delays are carried.
    ''' </summary>
    <Fact>
    Public Sub An_animation_becomes_a_gif_with_its_frames_and_delays()
        ' 100 ticks/s * 12 = 120 ms per frame = 12 hundredths of a second in the GIF.
        Using ms As New MemoryStream(AnimatedJxl(3, 12UI))
            Dim payload As DecodedPayload = CType(ImageDecoderProvider.Current, IImageDecoderPayload).DecodeToPayload(ms)
            Assert.NotNull(payload)
            Assert.Equal(DecodedPayloadKind.Gif, payload.Kind)
            Assert.True(payload.IsAnimation)
        End Using

        Using ms As New MemoryStream(AnimatedJxl(3, 12UI))
            Using img As Image = ImageDecoderProvider.Current.DecodeToImage(ms)
                Assert.NotNull(img)
                Assert.Equal(ImageFormat.Gif, img.RawFormat)
                Assert.Equal(New Size(20, 10), img.Size)
                Assert.Equal(3, img.GetFrameCount(FrameDimension.Time))
                Dim delays As Byte() = img.GetPropertyItem(&H5100).Value ' PropertyTagFrameDelay, Int32 per frame
                Assert.Equal(3 * 4, delays.Length)
                For frame As Integer = 0 To 2
                    Assert.Equal(12, BitConverter.ToInt32(delays, frame * 4))
                Next
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Zero_duration_frames_are_layers_of_one_picture_not_an_animation()
        ' JPEG XL composites frames of zero duration into the frame that follows them, so the
        ' decoder reports a single displayed frame and the picture must come out as a still.
        Using ms As New MemoryStream(AnimatedJxl(2, 0UI))
            Using img As Image = ImageDecoderProvider.Current.DecodeToImage(ms)
                Assert.NotNull(img)
                Assert.Equal(New Size(20, 10), img.Size)
                Assert.Equal(1, img.GetFrameCount(FrameDimension.Time))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub An_animation_reports_the_first_frames_size_from_the_header()
        Using ms As New MemoryStream(AnimatedJxl(3, 10UI))
            Assert.Equal(New Size(20, 10), ImageDecoderProvider.Current.TryGetPixelSize(ms))
        End Using
    End Sub

    ''' <summary>
    ''' Not a JPEG XL, whatever the name says: the decoder fails and the display path reports the
    ''' content failure, as for a damaged JPEG. Nothing here may throw out of the size label.
    ''' </summary>
    <Fact>
    Public Sub A_damaged_jxl_degrades_to_an_empty_size_and_a_failed_load()
        ' The signature and nothing after it: the header cannot be read.
        Dim damaged As Byte() = {&HFF, &HA, 0, 0, 0}
        Dim path As String = WriteTemp(damaged, ".jxl")
        Dim saved As Integer = DecodeCacheStore.BudgetMb
        DecodeCacheStore.BudgetMb = 0
        Try
            Assert.Equal(Size.Empty, Utils.GetImageDimensions(path))
            Dim failure As PathFailureKind = PathFailureKind.None
            Assert.Null(FileManager.LoadImageWithStream(path, failure))
            Assert.Equal(PathFailureKind.Content, failure)
        Finally
            DecodeCacheStore.BudgetMb = saved
            File.Delete(path)
        End Try
    End Sub

    ''' <summary>
    ''' The editor opens a .jxl and saves it as a PNG/JPEG/WEBP (Q236-1: no JPEG XL writer in
    ''' phase 1). The capture date must travel with the pixels, as it does for HEIC (audit 052
    ''' G-12): the donor is read through Magick.NET like every other format (specification 270).
    ''' </summary>
    <Fact>
    Public Sub A_jxl_donor_keeps_its_capture_date_through_a_save_as()
        Dim donor As Byte()
        Using image As New ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 16UI, 16UI)
            Dim exif As New ImageMagick.ExifProfile()
            exif.SetValue(ImageMagick.ExifTag.DateTimeOriginal, "2022:07:08 09:10:11")
            image.SetProfile(exif)
            Using ms As New MemoryStream()
                image.Write(ms, ImageMagick.MagickFormat.Jxl)
                donor = ms.ToArray()
            End Using
        End Using
        Assert.True(ImageDecoderProvider.LooksJxl(donor), "The fixture is not JPEG XL.")

        Dim read = ModernMagickEncoder.ReadDonorMetadata(donor)
        Assert.NotNull(read.Exif)
        Dim taken As ImageMagick.IExifValue(Of String) = read.Exif.GetValue(ImageMagick.ExifTag.DateTimeOriginal)
        Assert.True(taken IsNot Nothing, "DateTimeOriginal was lost.")
        Assert.Equal("2022:07:08 09:10:11", taken.Value)
    End Sub

    ' -------------------------------------------------------------------- helpers ----

    Private Shared Sub Assume_AutoRotate_Is_On()
        Assert.True(Is_Exif_AutoRotate, "This test expects the default (EXIF auto-rotate on).")
    End Sub

    Private Shared Function StillJxl(width As UInteger, height As UInteger,
                                     Optional orientation As ImageMagick.Orientation = ImageMagick.Orientation.TopLeft) As Byte()
        Using image As New ImageMagick.MagickImage(ImageMagick.MagickColors.Red, width, height)
            image.Orientation = orientation
            Using ms As New MemoryStream()
                image.Write(ms, ImageMagick.MagickFormat.Jxl)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    Private Shared Function AnimatedJxl(frameCount As Integer, delayTicks As UInteger) As Byte()
        Dim colours As ImageMagick.MagickColor() = {ImageMagick.MagickColors.Red, ImageMagick.MagickColors.Green, ImageMagick.MagickColors.Blue}
        Using frames As New ImageMagick.MagickImageCollection()
            For i As Integer = 0 To frameCount - 1
                Dim frame As New ImageMagick.MagickImage(colours(i Mod colours.Length), 20UI, 10UI)
                frame.AnimationTicksPerSecond = 100UI
                frame.AnimationDelay = delayTicks
                frames.Add(frame)
            Next
            Using ms As New MemoryStream()
                frames.Write(ms, ImageMagick.MagickFormat.Jxl)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    ''' <summary>
    ''' A bare codestream inside the ISO/IEC 18181-2 box container: the 12-byte signature box, an
    ''' "ftyp" box with the "jxl " brand, then one "jxlc" box holding the codestream.
    ''' </summary>
    Private Shared Function InContainer(codestream As Byte()) As Byte()
        Using ms As New MemoryStream()
            ms.Write(ContainerHead, 0, ContainerHead.Length)
            Dim ftyp As Byte() = {0, 0, 0, &H14, &H66, &H74, &H79, &H70, &H6A, &H78, &H6C, &H20, 0, 0, 0, 0, &H6A, &H78, &H6C, &H20}
            ms.Write(ftyp, 0, ftyp.Length)
            Dim size As Integer = codestream.Length + 8
            ms.WriteByte(CByte((size >> 24) And &HFF))
            ms.WriteByte(CByte((size >> 16) And &HFF))
            ms.WriteByte(CByte((size >> 8) And &HFF))
            ms.WriteByte(CByte(size And &HFF))
            Dim boxType As Byte() = Encoding.ASCII.GetBytes("jxlc")
            ms.Write(boxType, 0, boxType.Length)
            ms.Write(codestream, 0, codestream.Length)
            Return ms.ToArray()
        End Using
    End Function

    Private Shared Function WriteTemp(bytes As Byte(), ext As String) As String
        Dim path As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms_jxltest_" & Guid.NewGuid().ToString("N") & ext)
        File.WriteAllBytes(path, bytes)
        Return path
    End Function

#End If

    ' ------------------------------------------------------------ shared helpers ----

    Private Shared Function FirstLineWith(lines As String(), marker As String, mustAlsoHave As String) As String
        For Each line As String In lines
            If line.Contains(marker) AndAlso line.Contains(mustAlsoHave) Then Return line
        Next
        Throw New InvalidOperationException("No line with '" & marker & "' and '" & mustAlsoHave & "' in Main_Form.vb.")
    End Function

    Private Shared Function ReadSource(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = System.IO.Path.Combine(dir.FullName, "src", fileName)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate).Replace(vbCr, "")
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/" & fileName & " not found above " & AppContext.BaseDirectory)
    End Function

End Class
