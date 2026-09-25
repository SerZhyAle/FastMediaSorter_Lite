#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports System.Text
Imports SixLabors.ImageSharp.Metadata.Profiles.Exif
Imports SixLabors.ImageSharp.Metadata.Profiles.Icc
Imports SixLabors.ImageSharp.Metadata.Profiles.Iptc
Imports SixLabors.ImageSharp.Metadata.Profiles.Xmp
Imports Xunit

''' <summary>
''' The image-editor rows of audit 052 (docs/specifications/052_SPECIFICATION_AUDIT_ARCHIVE_SECURE_EDITOR.md
''' §5): the colour profile, XMP and IPTC survive a save (G-08), "Save as" from a HEIF-family
''' donor keeps its EXIF (G-12), and a freshly drawn crop frame can take the last column and
''' row (G-11). Modern-only, like the editor.
''' </summary>
Public Class EditorAuditTests
    Implements IDisposable

    Private ReadOnly workDir As String

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-editor-audit-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(workDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    ' --------------------------------------------------------------- G-11 ----

    <Fact>
    Public Sub A_frame_dragged_past_the_bottom_right_takes_the_whole_picture()
        Dim image As New Size(200, 100)
        Dim fit As New Rectangle(10, 10, 400, 200)
        Dim anchor As Point = EditorGeometry.CanvasToImage(New Point(0, 0), fit, image)
        Dim corner As Point = EditorGeometry.CanvasToImage(New Point(5000, 5000), fit, image)

        Assert.Equal(New Rectangle(0, 0, 200, 100), EditorGeometry.CropFromDrag(anchor, corner, image))
    End Sub

    <Fact>
    Public Sub A_drawn_frame_includes_both_dragged_pixels_in_either_direction()
        Dim image As New Size(50, 50)
        Assert.Equal(New Rectangle(10, 20, 6, 11), EditorGeometry.CropFromDrag(New Point(10, 20), New Point(15, 30), image))
        Assert.Equal(New Rectangle(10, 20, 6, 11), EditorGeometry.CropFromDrag(New Point(15, 30), New Point(10, 20), image))
        ' A frame across the full width keeps the last column: the "trim the bottom half" case.
        Assert.Equal(New Rectangle(0, 25, 50, 25), EditorGeometry.CropFromDrag(New Point(0, 25), New Point(49, 49), image))
    End Sub

    ''' <summary>The drawn frame and a resize to the same corner now agree to the pixel.</summary>
    <Fact>
    Public Sub Drawing_and_resizing_agree_on_the_corner_pixel()
        Dim image As New Size(80, 60)
        Dim drawn As Rectangle = EditorGeometry.CropFromDrag(New Point(5, 5), New Point(40, 30), image)
        Dim resized As Rectangle = EditorGeometry.ResizeCrop(New Rectangle(5, 5, 10, 10), EditorGeometry.CropHandle.BottomRight,
                                                            New Point(40, 30), image)
        Assert.Equal(drawn, resized)
    End Sub

    ' --------------------------------------------------------------- G-08 ----

    Private Const SrgbProfilePath As String = "C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm"

    <Fact>
    Public Sub Icc_xmp_and_iptc_survive_a_save()
        Assert.True(File.Exists(SrgbProfilePath), "The Windows sRGB profile is the ICC fixture.")
        Dim iccBytes As Byte() = File.ReadAllBytes(SrgbProfilePath)
        Dim xmpText As String = "<?xpacket begin="""" id=""W5M0MpCehiHzreSzNTczkc9d""?>" &
                                "<x:xmpmeta xmlns:x=""adobe:ns:meta/""><rdf:RDF xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#"">" &
                                "<rdf:Description xmlns:xmp=""http://ns.adobe.com/xap/1.0/"" xmp:Rating=""4""/>" &
                                "</rdf:RDF></x:xmpmeta><?xpacket end=""w""?>"

        Dim donor As Byte()
        Using image As New SixLabors.ImageSharp.Image(Of SixLabors.ImageSharp.PixelFormats.Rgba32)(24, 24)
            image.Metadata.IccProfile = New IccProfile(iccBytes)
            image.Metadata.XmpProfile = New XmpProfile(Encoding.UTF8.GetBytes(xmpText))
            Dim iptc As New IptcProfile()
            iptc.SetValue(IptcTag.Caption, "a caption that must survive")
            iptc.SetValue(IptcTag.Keywords, "sorted")
            image.Metadata.IptcProfile = iptc
            Using stream As New MemoryStream()
                SixLabors.ImageSharp.ImageExtensions.SaveAsJpeg(image, stream)
                donor = stream.ToArray()
            End Using
        End Using

        Using bitmap As New Bitmap(24, 24, PixelFormat.Format32bppArgb)
            Dim target As String = Path.Combine(workDir, "kept.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreRaw)

            Using stream As New MemoryStream(File.ReadAllBytes(target), writable:=False)
                Dim info = SixLabors.ImageSharp.Image.Identify(stream)
                Assert.NotNull(info.Metadata.IccProfile)
                Assert.Equal(New IccProfile(iccBytes).Header.ProfileConnectionSpace, info.Metadata.IccProfile.Header.ProfileConnectionSpace)
                Assert.Equal(New IccProfile(iccBytes).Entries.Length, info.Metadata.IccProfile.Entries.Length)

                Assert.NotNull(info.Metadata.XmpProfile)
                Assert.Contains("xmp:Rating=""4""", Encoding.UTF8.GetString(info.Metadata.XmpProfile.ToByteArray()))

                Assert.NotNull(info.Metadata.IptcProfile)
                Dim captions = info.Metadata.IptcProfile.GetValues(IptcTag.Caption)
                Assert.Single(captions)
                Assert.Equal("a caption that must survive", captions(0).Value)
            End Using
        End Using
    End Sub

    ' --------------------------------------------------------------- G-12 ----

    ''' <summary>An AVIF donor (the HEIF family ImageSharp cannot read) keeps its capture
    ''' date through a "Save as" to JPEG - read through Magick.NET, as the decoder does.</summary>
    <Fact>
    Public Sub A_heif_family_donor_keeps_its_capture_date()
        Dim donor As Byte()
        Using image As New ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 16UI, 16UI)
            Dim exif As New ImageMagick.ExifProfile()
            exif.SetValue(ImageMagick.ExifTag.DateTimeOriginal, "2021:05:06 07:08:09")
            exif.SetValue(ImageMagick.ExifTag.Make, "HEIF test camera")
            image.SetProfile(exif)
            image.Format = ImageMagick.MagickFormat.Avif
            donor = image.ToByteArray()
        End Using
        Assert.True(donor.Length > 12 AndAlso Encoding.ASCII.GetString(donor, 4, 4) = "ftyp", "The fixture is not ISO-BMFF.")

        Dim read = ModernImageSharpEncoder.ReadDonorMetadata(donor)
        Assert.NotNull(read.Exif)

        Using bitmap As New Bitmap(16, 16, PixelFormat.Format32bppArgb)
            Dim target As String = Path.Combine(workDir, "from-heif.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreUpright)

            Using stream As New MemoryStream(File.ReadAllBytes(target), writable:=False)
                Dim profile As ExifProfile = SixLabors.ImageSharp.Image.Identify(stream).Metadata.ExifProfile
                Assert.NotNull(profile)
                Dim taken As IExifValue(Of String) = Nothing
                Assert.True(profile.TryGetValue(ExifTag.DateTimeOriginal, taken), "DateTimeOriginal was lost.")
                Assert.Equal("2021:05:06 07:08:09", taken.Value)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_tiff_structure_is_found_behind_any_exif_prefix()
        Dim tiff As Byte() = {&H49, &H49, &H2A, 0, 8, 0, 0, 0, 0, 0}
        Assert.Equal(tiff, ModernImageSharpEncoder.TiffHeaderOnwards(tiff))

        Dim prefixed As Byte() = Encoding.ASCII.GetBytes("Exif" & ChrW(0) & ChrW(0)).Concat(tiff).ToArray()
        Assert.Equal(tiff, ModernImageSharpEncoder.TiffHeaderOnwards(prefixed))

        Assert.Null(ModernImageSharpEncoder.TiffHeaderOnwards(Encoding.ASCII.GetBytes("no tiff here at all")))
    End Sub

End Class
#End If
