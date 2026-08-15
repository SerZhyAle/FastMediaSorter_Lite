#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports SixLabors.ImageSharp.Metadata.Profiles.Exif
Imports Xunit

''' <summary>
''' Acceptance for Ф-1 of SPECIFICATION_IMAGE_EDITOR_DOTNET10.md - the phase the spec puts
''' first and alone precisely because it is the dangerous half: it writes over the user's
''' originals, and until it existed the application had no path that wrote pixels at all.
'''
''' The phase's own acceptance is one sentence - "open and save with no edits: the file is
''' equivalent to the original, EXIF intact, orientation unbroken" - and it is the only
''' part of the whole editor a machine can check. That is what this file does.
'''
''' Modern-only, like everything it covers: on the net48 leg the three source files under
''' test compile to nothing, and so does this.
''' </summary>
Public Class ImageEncoderTests
    Implements IDisposable

    Private ReadOnly workDir As String

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-encoder-tests-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(workDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    ' ------------------------------------------------------- the writable set ----

    ''' <summary>
    ''' §9.1's table is a decision, not a capability list: ImageSharp would happily write a
    ''' GIF, and we refuse because a 256-entry palette would quietly wreck the colours of an
    ''' edited photo. A table that drifts from the code is how that decision gets lost.
    ''' </summary>
    <Fact>
    Public Sub The_writable_set_is_exactly_the_six_formats_the_specification_lists()
        Dim writable = ImageEncoderProvider.Current.WritableExtensions()
        Assert.Equal({".bmp", ".jpeg", ".jpg", ".png", ".tiff", ".webp"},
                     writable.OrderBy(Function(e) e, StringComparer.Ordinal).ToArray())
    End Sub

    <Theory>
    <InlineData("photo.jpg", True)>
    <InlineData("photo.JPG", True)>
    <InlineData("shot.png", True)>
    <InlineData("scan.tiff", True)>
    <InlineData("pic.webp", True)>
    <InlineData("art.gif", False)>
    <InlineData("icon.ico", False)>
    <InlineData("drawing.wmf", False)>
    <InlineData("legacy.exif", False)>
    <InlineData("noextension", False)>
    <InlineData("", False)>
    Public Sub Writability_follows_the_extension(name As String, expected As Boolean)
        Assert.Equal(expected, ImageEncoderProvider.IsWritableExtension(name))
    End Sub

    <Fact>
    Public Sub An_unwritable_format_is_refused_rather_than_written_as_something_else()
        Using bitmap As New Bitmap(4, 4)
            Dim target = Path.Combine(workDir, "refused.gif")
            Assert.Throws(Of NotSupportedException)(
                Sub() ImageEncoderProvider.Current.EncodeToFile(bitmap, target, Nothing, ExifOrientationPolicy.PixelsAreUpright))
            Assert.False(File.Exists(target), "A refused format must not leave a file behind.")
        End Using
    End Sub

    ' ------------------------------------------------------------- round trip ----

    ''' <summary>
    ''' The phase's acceptance in its strictest form: PNG is lossless on both legs of the
    ''' GDI+ &lt;-&gt; ImageSharp bridge, so a save with no edits has to come back bit for bit
    ''' in the pixels. If the bridge ever quantises, this is where it shows.
    ''' </summary>
    <Fact>
    Public Sub A_png_saved_with_no_edits_comes_back_pixel_for_pixel()
        Using original As Bitmap = MakeGradient(37, 23)
            Dim target = Path.Combine(workDir, "round-trip.png")
            ImageFileWriter.Save(original, target, Nothing, ExifOrientationPolicy.PixelsAreUpright)

            Using written As New Bitmap(target)
                Assert.Equal(original.Width, written.Width)
                Assert.Equal(original.Height, written.Height)
                For y = 0 To original.Height - 1
                    For x = 0 To original.Width - 1
                        Assert.Equal(original.GetPixel(x, y).ToArgb(), written.GetPixel(x, y).ToArgb())
                    Next
                Next
            End Using
        End Using
    End Sub

    <Theory>
    <InlineData(".png")>
    <InlineData(".jpg")>
    <InlineData(".bmp")>
    <InlineData(".tiff")>
    <InlineData(".webp")>
    Public Sub Every_writable_format_produces_a_file_of_the_right_size(extension As String)
        Using original As Bitmap = MakeGradient(20, 12)
            Dim target = Path.Combine(workDir, "sized" & extension)
            ImageFileWriter.Save(original, target, Nothing, ExifOrientationPolicy.PixelsAreUpright)

            Assert.True(File.Exists(target), extension & " was not written.")
            Assert.True(New FileInfo(target).Length > 0, extension & " came out empty.")

            ' Read it back through the shipped decoder rather than GDI+ - .webp is exactly
            ' the format GDI+ cannot open, and that is why the seam exists.
            Using stream As New MemoryStream(File.ReadAllBytes(target))
                Dim size = ImageDecoderProvider.Current.TryGetPixelSize(stream)
                Assert.Equal(New Size(20, 12), size)
            End Using
        End Using
    End Sub

    ' ------------------------------------------------------------------ EXIF ----

    ''' <summary>
    ''' §9.5. Re-saving a JPEG without carrying the profile silently erases the capture
    ''' date, the camera and the coordinates - in a photo sorter that is data loss, and it
    ''' is invisible until somebody sorts by date months later.
    ''' </summary>
    <Fact>
    Public Sub Jpeg_keeps_the_originals_exif()
        Dim donor As Byte() = MakeJpegWithExif(orientation:=1, withThumbnail:=False)
        Using bitmap As Bitmap = MakeGradient(16, 16)
            Dim target = Path.Combine(workDir, "kept.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreRaw)

            Dim profile = ReadProfile(target)
            Assert.NotNull(profile)
            Assert.Equal("FastMediaSorter test camera", ReadString(profile, ExifTag.Make))
            Assert.Equal("2019:04:07 12:34:56", ReadString(profile, ExifTag.DateTimeOriginal))
        End Using
    End Sub

    <Fact>
    Public Sub Without_a_donor_nothing_is_invented()
        Using bitmap As Bitmap = MakeGradient(16, 16)
            Dim target = Path.Combine(workDir, "bare.jpg")
            ImageFileWriter.Save(bitmap, target, Nothing, ExifOrientationPolicy.PixelsAreUpright)
            Assert.Null(ReadProfile(target))
        End Using
    End Sub

    ''' <summary>
    ''' The invariant of §9.2, stated as a test: the tag always describes the pixels that
    ''' were written. Auto-rotate on means the viewer already turned them upright and
    ''' stripped the tag - copying "6" across would turn the photo a second time in the next
    ''' program that opens it.
    ''' </summary>
    <Fact>
    Public Sub Upright_pixels_are_written_without_an_orientation_tag()
        Dim donor As Byte() = MakeJpegWithExif(orientation:=6, withThumbnail:=False)
        Using bitmap As Bitmap = MakeGradient(16, 16)
            Dim target = Path.Combine(workDir, "upright.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreUpright)

            Dim profile = ReadProfile(target)
            Assert.NotNull(profile)
            Assert.False(TryOrientation(profile).HasValue,
                         "Pixels were already upright, so an Orientation tag would turn them twice.")
            ' The rest of the profile has to survive the one removal.
            Assert.Equal("FastMediaSorter test camera", ReadString(profile, ExifTag.Make))
        End Using
    End Sub

    <Fact>
    Public Sub Raw_pixels_keep_the_originals_orientation_tag()
        Dim donor As Byte() = MakeJpegWithExif(orientation:=6, withThumbnail:=False)
        Using bitmap As Bitmap = MakeGradient(16, 16)
            Dim target = Path.Combine(workDir, "raw.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreRaw)

            Assert.Equal(CUShort(6), TryOrientation(ReadProfile(target)))
        End Using
    End Sub

    ''' <summary>
    ''' §9.5 again, and the nastiest shape this family of bug takes: the embedded thumbnail
    ''' is a picture of the file BEFORE the edit, and Explorer and phones prefer it to the
    ''' real pixels - so the edited file goes on looking unedited and nothing appears broken.
    ''' </summary>
    <Fact>
    Public Sub The_pre_edit_thumbnail_is_dropped()
        Dim donor As Byte() = MakeJpegWithExif(orientation:=1, withThumbnail:=True)
        Assert.True(HasThumbnailPointer(ReadProfileFromBytes(donor)),
                    "The donor was supposed to carry a thumbnail - the test proves nothing without one.")

        Using bitmap As Bitmap = MakeGradient(16, 16)
            Dim target = Path.Combine(workDir, "nothumb.jpg")
            ImageFileWriter.Save(bitmap, target, donor, ExifOrientationPolicy.PixelsAreUpright)

            Assert.False(HasThumbnailPointer(ReadProfile(target)),
                         "The thumbnail shows the image as it was before the edit.")
        End Using
    End Sub

    ''' <summary>
    ''' Risk §14.2, and not hypothetical - crop changes the size. A profile copied whole
    ''' would keep claiming the original dimensions, so the metadata would lie about the
    ''' picture it is attached to.
    ''' </summary>
    <Fact>
    Public Sub The_recorded_dimensions_follow_the_pixels_that_were_written()
        Dim donor As Byte() = MakeJpegWithExif(orientation:=1, withThumbnail:=False, width:=64, height:=64)
        Using cropped As Bitmap = MakeGradient(21, 13)
            Dim target = Path.Combine(workDir, "cropped.jpg")
            ImageFileWriter.Save(cropped, target, donor, ExifOrientationPolicy.PixelsAreUpright)

            Dim profile = ReadProfile(target)
            Assert.Equal(21, CInt(ReadNumber(profile, ExifTag.PixelXDimension)))
            Assert.Equal(13, CInt(ReadNumber(profile, ExifTag.PixelYDimension)))
        End Using
    End Sub

    ''' <summary>Losing metadata is bad; losing the edit is worse.</summary>
    <Fact>
    Public Sub A_corrupt_donor_costs_the_metadata_and_not_the_save()
        Dim rubbish = New Byte() {1, 2, 3, 4, 5, 6, 7, 8}
        Using bitmap As Bitmap = MakeGradient(8, 8)
            Dim target = Path.Combine(workDir, "rubbish-donor.jpg")
            ImageFileWriter.Save(bitmap, target, rubbish, ExifOrientationPolicy.PixelsAreUpright)
            Assert.True(File.Exists(target), "A donor that cannot be parsed must not stop the write.")
        End Using
    End Sub

    ' ------------------------------------------------- atomicity and the probe ----

    ''' <summary>
    ''' The marker goes BEFORE the extension, and this is a regression test rather than a
    ''' style preference: the encoder picks its format from the extension of the path it is
    ''' handed, so the specification's own sketch - appending ".fms-tmp" - produced
    ''' "photo.jpg.fms-tmp", a format nothing knows, and failed EVERY save in EVERY format.
    ''' It was caught by the first acceptance run; nothing but a test keeps it caught.
    ''' </summary>
    <Theory>
    <InlineData("C:\photos\photo.jpg", "C:\photos\photo.fms-tmp.jpg")>
    <InlineData("C:\photos\holiday.2019.png", "C:\photos\holiday.2019.fms-tmp.png")>
    <InlineData("\\server\share\scan.tiff", "\\server\share\scan.fms-tmp.tiff")>
    <InlineData("photo.webp", "photo.fms-tmp.webp")>
    Public Sub The_temporary_file_keeps_the_real_extension_last(target As String, expected As String)
        Assert.Equal(expected, ImageFileWriter.TempPathFor(target))
    End Sub

    ''' <summary>
    ''' Invariant 2 - the original is never damaged. The write goes to a neighbour and the
    ''' files are swapped, so an interruption at any step leaves the photo whole.
    ''' </summary>
    <Fact>
    Public Sub A_failed_write_leaves_the_original_untouched_and_no_leftovers()
        Dim target = Path.Combine(workDir, "existing.png")
        Dim before As Byte() = {1, 2, 3, 4, 5}
        File.WriteAllBytes(target, before)

        ' A disposed bitmap makes the ENCODER throw, i.e. the failure lands mid-write -
        ' the case that would truncate the photo if the target were opened directly.
        Dim broken As New Bitmap(4, 4)
        broken.Dispose()

        Assert.ThrowsAny(Of Exception)(
            Sub() ImageFileWriter.Save(broken, target, Nothing, ExifOrientationPolicy.PixelsAreUpright))

        Assert.Equal(before, File.ReadAllBytes(target))
        Assert.False(File.Exists(ImageFileWriter.TempPathFor(target)), "The temporary file was left behind.")
    End Sub

    <Fact>
    Public Sub A_successful_write_replaces_the_file_and_leaves_no_temporary()
        Dim target = Path.Combine(workDir, "replaced.png")
        File.WriteAllBytes(target, New Byte() {9, 9, 9})

        Using bitmap As Bitmap = MakeGradient(6, 6)
            ImageFileWriter.Save(bitmap, target, Nothing, ExifOrientationPolicy.PixelsAreUpright)
        End Using

        Assert.False(File.Exists(ImageFileWriter.TempPathFor(target)))
        Using written As New Bitmap(target)
            Assert.Equal(New Size(6, 6), written.Size)
        End Using
    End Sub

    <Fact>
    Public Sub The_probe_names_the_reason_the_original_cannot_be_replaced()
        Assert.Equal(ImageFileWriter.ReplaceRefusal.Missing, ImageFileWriter.CanReplaceOriginal(""))
        Assert.Equal(ImageFileWriter.ReplaceRefusal.Missing,
                     ImageFileWriter.CanReplaceOriginal(Path.Combine(workDir, "not-here.png")))

        Dim animated = Path.Combine(workDir, "animation.gif")
        File.WriteAllBytes(animated, New Byte() {0})
        Assert.Equal(ImageFileWriter.ReplaceRefusal.FormatNotWritable, ImageFileWriter.CanReplaceOriginal(animated))

        Dim writable = Path.Combine(workDir, "fine.png")
        Using bitmap As Bitmap = MakeGradient(4, 4)
            ImageFileWriter.Save(bitmap, writable, Nothing, ExifOrientationPolicy.PixelsAreUpright)
        End Using
        Assert.Equal(ImageFileWriter.ReplaceRefusal.None, ImageFileWriter.CanReplaceOriginal(writable))

        File.SetAttributes(writable, FileAttributes.ReadOnly)
        Try
            Assert.Equal(ImageFileWriter.ReplaceRefusal.ReadOnlyAttribute, ImageFileWriter.CanReplaceOriginal(writable))
        Finally
            File.SetAttributes(writable, FileAttributes.Normal)
        End Try
    End Sub

    ''' <summary>
    ''' The probe exists to catch what the attributes cannot see. A file another program
    ''' holds open reports nothing unusual until somebody tries to write it.
    ''' </summary>
    <Fact>
    Public Sub The_probe_sees_a_lock_that_the_attributes_do_not()
        Dim locked = Path.Combine(workDir, "locked.png")
        Using bitmap As Bitmap = MakeGradient(4, 4)
            ImageFileWriter.Save(bitmap, locked, Nothing, ExifOrientationPolicy.PixelsAreUpright)
        End Using

        Using holder As New FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read)
            Assert.Equal(ImageFileWriter.ReplaceRefusal.Locked, ImageFileWriter.CanReplaceOriginal(locked))
        End Using

        Assert.Equal(ImageFileWriter.ReplaceRefusal.None, ImageFileWriter.CanReplaceOriginal(locked))
    End Sub

    ' ---------------------------------------------------------------- helpers ----

    ''' <summary>Distinct colour per pixel, so an off-by-one in the bridge cannot hide.</summary>
    Private Shared Function MakeGradient(width As Integer, height As Integer) As Bitmap
        Dim bitmap As New Bitmap(width, height, PixelFormat.Format32bppArgb)
        For y = 0 To height - 1
            For x = 0 To width - 1
                bitmap.SetPixel(x, y, Color.FromArgb(255, (x * 7) Mod 256, (y * 11) Mod 256, (x + y) Mod 256))
            Next
        Next
        Return bitmap
    End Function

    ''' <summary>
    ''' A real JPEG carrying a real EXIF profile, built with the same library the encoder
    ''' writes with - a hand-assembled byte array would only prove the test's own idea of
    ''' the format.
    ''' </summary>
    Private Function MakeJpegWithExif(orientation As UShort, withThumbnail As Boolean,
                                      Optional width As Integer = 32, Optional height As Integer = 32) As Byte()
        Using image As New SixLabors.ImageSharp.Image(Of SixLabors.ImageSharp.PixelFormats.Rgba32)(width, height)
            Dim profile As New ExifProfile()
            profile.SetValue(ExifTag.Make, "FastMediaSorter test camera")
            profile.SetValue(ExifTag.DateTimeOriginal, "2019:04:07 12:34:56")
            profile.SetValue(ExifTag.Orientation, orientation)
            profile.SetValue(ExifTag.PixelXDimension, CType(width, SixLabors.ImageSharp.Number))
            profile.SetValue(ExifTag.PixelYDimension, CType(height, SixLabors.ImageSharp.Number))
            If withThumbnail Then
                ' The pointer pair IS the thumbnail as far as a reader is concerned; the
                ' encoder removes exactly these two, so this is the honest thing to plant.
                profile.SetValue(ExifTag.JPEGInterchangeFormat, CUInt(1024))
                profile.SetValue(ExifTag.JPEGInterchangeFormatLength, CUInt(256))
            End If
            image.Metadata.ExifProfile = profile

            Using stream As New MemoryStream()
                SixLabors.ImageSharp.ImageExtensions.SaveAsJpeg(image, stream)
                Return stream.ToArray()
            End Using
        End Using
    End Function

    Private Shared Function ReadProfile(filePath As String) As ExifProfile
        Return ReadProfileFromBytes(File.ReadAllBytes(filePath))
    End Function

    Private Shared Function ReadProfileFromBytes(bytes As Byte()) As ExifProfile
        Using stream As New MemoryStream(bytes, writable:=False)
            Dim info = SixLabors.ImageSharp.Image.Identify(stream)
            Return If(info?.Metadata?.ExifProfile, Nothing)
        End Using
    End Function

    Private Shared Function TryOrientation(profile As ExifProfile) As UShort?
        Dim value As IExifValue(Of UShort) = Nothing
        If profile Is Nothing OrElse Not profile.TryGetValue(ExifTag.Orientation, value) Then Return Nothing
        Return value.Value
    End Function

    Private Shared Function HasThumbnailPointer(profile As ExifProfile) As Boolean
        If profile Is Nothing Then Return False
        Dim pointer As IExifValue(Of UInteger) = Nothing
        Return profile.TryGetValue(ExifTag.JPEGInterchangeFormat, pointer)
    End Function

    Private Shared Function ReadString(profile As ExifProfile, tag As ExifTag(Of String)) As String
        Dim value As IExifValue(Of String) = Nothing
        If profile Is Nothing OrElse Not profile.TryGetValue(tag, value) Then Return Nothing
        Return value.Value
    End Function

    Private Shared Function ReadNumber(profile As ExifProfile, tag As ExifTag(Of SixLabors.ImageSharp.Number)) As SixLabors.ImageSharp.Number
        Dim value As IExifValue(Of SixLabors.ImageSharp.Number) = Nothing
        Assert.True(profile IsNot Nothing AndAlso profile.TryGetValue(tag, value), "Dimension tag missing.")
        Return value.Value
    End Function

End Class
#End If
