#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports System.Text
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
        Using image As New ImageMagick.MagickImage(ImageMagick.MagickColors.Gray, 24UI, 24UI)
            ' Attached as plain profiles: nothing is converted, the donor carries these bytes.
            image.SetProfile(New ImageMagick.ImageProfile("icc", iccBytes))
            image.SetProfile(New ImageMagick.XmpProfile(Encoding.UTF8.GetBytes(xmpText)))
            Dim iptc As New ImageMagick.IptcProfile()
            iptc.SetValue(ImageMagick.IptcTag.Caption, "a caption that must survive")
            iptc.SetValue(ImageMagick.IptcTag.Keyword, "sorted")
            image.SetProfile(iptc)
            donor = image.ToByteArray(ImageMagick.MagickFormat.Jpeg)
        End Using

        Using bitmap As New Bitmap(24, 24, PixelFormat.Format32bppArgb)
            Dim target As String = Path.Combine(workDir, "kept.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreRaw)

            Using saved As New ImageMagick.MagickImage()
                saved.Ping(File.ReadAllBytes(target))
                ' The colour profile byte for byte: carried, never converted or rebuilt (270 R3).
                Dim icc As ImageMagick.IImageProfile = saved.GetProfile("icc")
                Assert.NotNull(icc)
                Assert.Equal(iccBytes, icc.ToByteArray())

                Dim xmp As ImageMagick.IXmpProfile = saved.GetXmpProfile()
                Assert.NotNull(xmp)
                Assert.Contains("xmp:Rating=""4""", Encoding.UTF8.GetString(xmp.ToByteArray()))

                Dim iptcRead As ImageMagick.IIptcProfile = saved.GetIptcProfile()
                Assert.NotNull(iptcRead)
                Dim captions = iptcRead.GetAllValues(ImageMagick.IptcTag.Caption).ToList()
                Assert.Single(captions)
                Assert.Equal("a caption that must survive", captions(0).Value)
            End Using
        End Using
    End Sub

    ' --------------------------------------------------------------- G-12 ----

    ''' <summary>An AVIF donor (the HEIF family) keeps its capture date through a "Save as" to
    ''' JPEG - read through Magick.NET, as the decoder does.</summary>
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

        Dim read = ModernMagickEncoder.ReadDonorMetadata(donor)
        Assert.NotNull(read.Exif)

        Using bitmap As New Bitmap(16, 16, PixelFormat.Format32bppArgb)
            Dim target As String = Path.Combine(workDir, "from-heif.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreUpright)

            Using saved As New ImageMagick.MagickImage()
                saved.Ping(File.ReadAllBytes(target))
                Dim profile As ImageMagick.IExifProfile = saved.GetExifProfile()
                Assert.NotNull(profile)
                Dim taken As ImageMagick.IExifValue(Of String) = profile.GetValue(ImageMagick.ExifTag.DateTimeOriginal)
                Assert.True(taken IsNot Nothing, "DateTimeOriginal was lost.")
                Assert.Equal("2021:05:06 07:08:09", taken.Value)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_tiff_structure_is_found_behind_any_exif_prefix()
        Dim tiff As Byte() = {&H49, &H49, &H2A, 0, 8, 0, 0, 0, 0, 0}
        Assert.Equal(tiff, ModernMagickEncoder.TiffHeaderOnwards(tiff))

        Dim prefixed As Byte() = Encoding.ASCII.GetBytes("Exif" & ChrW(0) & ChrW(0)).Concat(tiff).ToArray()
        Assert.Equal(tiff, ModernMagickEncoder.TiffHeaderOnwards(prefixed))

        Assert.Null(ModernMagickEncoder.TiffHeaderOnwards(Encoding.ASCII.GetBytes("no tiff here at all")))
    End Sub

End Class
#End If
