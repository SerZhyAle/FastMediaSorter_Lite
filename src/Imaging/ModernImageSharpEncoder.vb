#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports SixLabors.ImageSharp.Metadata.Profiles.Exif

''' <summary>
''' The .NET 10 encoder: SixLabors.ImageSharp 3, fully managed - the same engine the
''' decoder already reads with, so a format the viewer can open is a format this can
''' write, with no OS codec in either direction.
'''
''' The GDI+ -> ImageSharp bridge goes through a PNG in memory, symmetrically to the way
''' <see cref="ModernImageSharpDecoder"/> already crosses in the other direction. PNG is
''' lossless, so the only thing spent on the crossing is time.
''' </summary>
Friend NotInheritable Class ModernImageSharpEncoder
    Implements IImageEncoder

    ''' <summary>
    ''' Fixed by SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §9.5, with no setting in v1: high
    ''' enough that one re-save is not visible, low enough that a photo does not grow.
    ''' </summary>
    Private Const JpegQuality As Integer = 92

    ''' <summary>§9.1. Six of the eleven extensions the viewer reads - see the interface.</summary>
    Private Shared ReadOnly Writable As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp"}

    Public Function WritableExtensions() As IReadOnlyCollection(Of String) Implements IImageEncoder.WritableExtensions
        Return Writable
    End Function

    Public Sub EncodeToFile(bitmap As Bitmap, targetPath As String, exifSource As Byte(),
                            orientation As ExifOrientationPolicy) Implements IImageEncoder.EncodeToFile
        If bitmap Is Nothing Then Throw New ArgumentNullException(NameOf(bitmap))
        If String.IsNullOrEmpty(targetPath) Then Throw New ArgumentException("Target path is empty.", NameOf(targetPath))

        If Not Writable.Contains(Path.GetExtension(targetPath).ToLowerInvariant()) Then Throw New NotSupportedException("Cannot write " & Path.GetExtension(targetPath))
        Using output As New FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None)
            EncodeToStream(bitmap, output, Path.GetExtension(targetPath), exifSource, orientation)
        End Using
    End Sub

    Public Sub EncodeToStream(bitmap As Bitmap, output As Stream, extension As String,
                              exifSource As Byte(), orientation As ExifOrientationPolicy) Implements IImageEncoder.EncodeToStream
        If bitmap Is Nothing Then Throw New ArgumentNullException(NameOf(bitmap))
        If output Is Nothing Then Throw New ArgumentNullException(NameOf(output))
        extension = extension.ToLowerInvariant()
        If Not Writable.Contains(extension) Then
            Throw New NotSupportedException("Cannot write " & extension & " - writable formats are " & String.Join(", ", Writable) & ".")
        End If

        Using sharpImage As SixLabors.ImageSharp.Image = ToImageSharp(bitmap)
            Dim donor As DonorMetadata = ReadDonorMetadata(exifSource)
            ApplyExifProfile(sharpImage, donor.Exif, orientation, bitmap.Width, bitmap.Height)
            ApplyOtherProfiles(sharpImage, donor)
            Save(sharpImage, output, extension)
        End Using
    End Sub

    ' ----------------------------------------------------------------- the donor ----

    ''' <summary>What the original carries besides its pixels: EXIF, and - audit 052 G-08 -
    ''' the colour profile, XMP and IPTC, which used to be dropped on every save.</summary>
    Friend NotInheritable Class DonorMetadata
        Friend Property Exif As ExifProfile
        Friend Property Icc As SixLabors.ImageSharp.Metadata.Profiles.Icc.IccProfile
        Friend Property Xmp As SixLabors.ImageSharp.Metadata.Profiles.Xmp.XmpProfile
        Friend Property Iptc As SixLabors.ImageSharp.Metadata.Profiles.Iptc.IptcProfile
    End Class

    ''' <summary>
    ''' Reads the donor's profiles. ImageSharp knows no HEIF/AVIF, so an ISO-BMFF donor is
    ''' read through Magick.NET instead - the same routing by signature the decoder uses
    ''' (audit 052 G-12: "Save as" from an iPhone HEIC lost its capture date and GPS). Never
    ''' throws: a donor that cannot be read gives an empty set, and the save goes on.
    ''' </summary>
    Friend Shared Function ReadDonorMetadata(source As Byte()) As DonorMetadata
        Dim donor As New DonorMetadata()
        If source Is Nothing OrElse source.Length = 0 Then Return donor
        Try
            If IsIsoBmff(source) Then
                ReadDonorViaMagick(source, donor)
            Else
                ' Identify parses the header only - the donor's pixels are never decoded,
                ' which matters when the donor is a 24-megapixel photo on a network share.
                Using stream As New MemoryStream(source, writable:=False)
                    Dim info As SixLabors.ImageSharp.ImageInfo = SixLabors.ImageSharp.Image.Identify(stream)
                    If info IsNot Nothing AndAlso info.Metadata IsNot Nothing Then
                        donor.Exif = info.Metadata.ExifProfile
                        donor.Icc = info.Metadata.IccProfile
                        donor.Xmp = info.Metadata.XmpProfile
                        donor.Iptc = info.Metadata.IptcProfile
                    End If
                End Using
            End If
        Catch ex As Exception
            ' Losing metadata is bad; losing the edit is worse.
            AppFileLogger.WriteLine(" w0041: donor metadata not read: " & ex.Message)
        End Try
        Return donor
    End Function

    Private Shared Function IsIsoBmff(bytes As Byte()) As Boolean
        Return bytes.Length >= 12 AndAlso bytes(4) = AscW("f"c) AndAlso bytes(5) = AscW("t"c) AndAlso
               bytes(6) = AscW("y"c) AndAlso bytes(7) = AscW("p"c)
    End Function

    ''' <summary>Ping, not Read: the header and the profiles, never the pixels.</summary>
    Private Shared Sub ReadDonorViaMagick(source As Byte(), donor As DonorMetadata)
        MagickResourceLimits.EnsureApplied()
        Using probe As New ImageMagick.MagickImage()
            probe.Ping(source)

            Dim exif As ImageMagick.IExifProfile = probe.GetExifProfile()
            If exif IsNot Nothing Then
                Dim raw As Byte() = TiffHeaderOnwards(exif.ToByteArray())
                If raw IsNot Nothing Then donor.Exif = New ExifProfile(raw)
            End If

            Dim icc As ImageMagick.IColorProfile = probe.GetColorProfile()
            If icc IsNot Nothing Then
                Dim raw As Byte() = icc.ToByteArray()
                If raw IsNot Nothing AndAlso raw.Length > 0 Then
                    donor.Icc = New SixLabors.ImageSharp.Metadata.Profiles.Icc.IccProfile(raw)
                End If
            End If

            Dim xmp As ImageMagick.IXmpProfile = probe.GetXmpProfile()
            If xmp IsNot Nothing Then
                Dim raw As Byte() = xmp.ToByteArray()
                If raw IsNot Nothing AndAlso raw.Length > 0 Then
                    donor.Xmp = New SixLabors.ImageSharp.Metadata.Profiles.Xmp.XmpProfile(raw)
                End If
            End If
        End Using
    End Sub

    ''' <summary>
    ''' The EXIF bytes from the TIFF header on. HEIF stores the block behind an offset and
    ''' often an "Exif\0\0" marker, and which of the two survives into Magick's profile
    ''' depends on the file; ImageSharp wants the bare TIFF structure, so it is found by its
    ''' byte-order mark ("II*\0" or "MM\0*") rather than by assuming a fixed prefix.
    ''' </summary>
    Friend Shared Function TiffHeaderOnwards(bytes As Byte()) As Byte()
        If bytes Is Nothing OrElse bytes.Length < 8 Then Return Nothing
        Dim limit As Integer = Math.Min(bytes.Length - 4, 64)
        For i As Integer = 0 To limit
            Dim intel As Boolean = bytes(i) = &H49 AndAlso bytes(i + 1) = &H49 AndAlso bytes(i + 2) = &H2A AndAlso bytes(i + 3) = 0
            Dim motorola As Boolean = bytes(i) = &H4D AndAlso bytes(i + 1) = &H4D AndAlso bytes(i + 2) = 0 AndAlso bytes(i + 3) = &H2A
            If intel OrElse motorola Then
                If i = 0 Then Return bytes
                Dim trimmed(bytes.Length - i - 1) As Byte
                Array.Copy(bytes, i, trimmed, 0, trimmed.Length)
                Return trimmed
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' G-08: the colour profile, XMP and IPTC go across as they are. Without the ICC
    ''' profile a Display-P3 or Adobe RGB photo is re-saved with its raw values and no
    ''' profile, and every colour-managed viewer shows it washed out; without XMP and IPTC
    ''' the ratings, keywords, captions and copyright are gone. Each is best-effort on its
    ''' own: one malformed profile costs that profile, not the other two and not the save.
    ''' </summary>
    Private Shared Sub ApplyOtherProfiles(sharpImage As SixLabors.ImageSharp.Image, donor As DonorMetadata)
        Try
            If donor.Icc IsNot Nothing Then sharpImage.Metadata.IccProfile = donor.Icc.DeepClone()
        Catch ex As Exception
            AppFileLogger.WriteLine(" w0042: ICC transfer skipped: " & ex.Message)
        End Try
        Try
            If donor.Xmp IsNot Nothing Then sharpImage.Metadata.XmpProfile = donor.Xmp.DeepClone()
        Catch ex As Exception
            AppFileLogger.WriteLine(" w0043: XMP transfer skipped: " & ex.Message)
        End Try
        Try
            If donor.Iptc IsNot Nothing Then sharpImage.Metadata.IptcProfile = donor.Iptc.DeepClone()
        Catch ex As Exception
            AppFileLogger.WriteLine(" w0044: IPTC transfer skipped: " & ex.Message)
        End Try
    End Sub

    ' ------------------------------------------------------------------- bridge ----

    ''' <summary>
    ''' GDI+ Bitmap -> ImageSharp Image through a lossless PNG in memory. The stream is
    ''' fully consumed by Load before it is disposed, so unlike the decoder's animation
    ''' path nothing has to outlive this method.
    ''' </summary>
    Private Shared Function ToImageSharp(bitmap As Bitmap) As SixLabors.ImageSharp.Image
        Using bridge As New MemoryStream()
            bitmap.Save(bridge, ImageFormat.Png)
            bridge.Position = 0
            Return SixLabors.ImageSharp.Image.Load(bridge)
        End Using
    End Function

    Private Shared Sub Save(sharpImage As SixLabors.ImageSharp.Image, output As Stream, extension As String)
        Select Case extension
            Case ".jpg", ".jpeg"
                sharpImage.Save(output,
                    New SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder With {.Quality = JpegQuality})
            Case ".webp"
                ' Lossless: an edit is already a new file, and re-encoding a photo lossily
                ' on every save would compound. §14.4 accepts that this can come out larger
                ' than a lossy source.
                sharpImage.Save(output,
                    New SixLabors.ImageSharp.Formats.Webp.WebpEncoder With {
                        .FileFormat = SixLabors.ImageSharp.Formats.Webp.WebpFileFormatType.Lossless})
            Case Else
                ' Stream output requires an explicit encoder; all three are lossless.
                Dim encoder As SixLabors.ImageSharp.Formats.IImageEncoder
                Select Case extension
                    Case ".png"
                        encoder = New SixLabors.ImageSharp.Formats.Png.PngEncoder()
                    Case ".bmp"
                        encoder = New SixLabors.ImageSharp.Formats.Bmp.BmpEncoder()
                    Case ".tiff"
                        encoder = New SixLabors.ImageSharp.Formats.Tiff.TiffEncoder()
                    Case Else
                        Throw New NotSupportedException("Cannot write " & extension)
                End Select
                sharpImage.Save(output, encoder)
        End Select
    End Sub

    ' --------------------------------------------------------------------- EXIF ----

    ''' <summary>
    ''' Carries the original's EXIF across, then corrects the three things that would
    ''' otherwise be lies about the file just written (§9.5):
    '''
    '''   * <b>Orientation</b> - removed or kept, per <paramref name="orientation"/>. A tag
    '''     copied blindly is the "photo turns twice" bug.
    '''   * <b>The embedded thumbnail</b> - always dropped. It is a picture of the file
    '''     BEFORE the edit, and Explorer and phones prefer it to the real pixels, so the
    '''     edited file would keep looking unedited. That is the nastiest shape this class
    '''     of bug takes, because nothing looks broken.
    '''   * <b>PixelXDimension / PixelYDimension</b> - rewritten from the bitmap. Crop
    '''     changes the size, and §14.2 names this as the field that goes stale.
    '''
    ''' A malformed profile in the source must never cost the user their save: the whole
    ''' transfer is best-effort, and failing it writes the pixels with no EXIF rather than
    ''' throwing.
    ''' </summary>
    Private Shared Sub ApplyExifProfile(sharpImage As SixLabors.ImageSharp.Image, source As ExifProfile,
                                        orientation As ExifOrientationPolicy, width As Integer, height As Integer)
        If source Is Nothing Then Return

        Try
            Dim profile As ExifProfile = source.DeepClone()

            If orientation = ExifOrientationPolicy.PixelsAreUpright Then
                ' The pixels were rotated upright and the viewer stripped the tag; saying
                ' "6" here would turn them again.
                profile.RemoveValue(ExifTag.Orientation)
            End If

            ' IFD1 holds the thumbnail through this pointer pair; without them there is
            ' nothing for a reader to find.
            profile.RemoveValue(ExifTag.JPEGInterchangeFormat)
            profile.RemoveValue(ExifTag.JPEGInterchangeFormatLength)

            profile.SetValue(ExifTag.PixelXDimension, CType(width, SixLabors.ImageSharp.Number))
            profile.SetValue(ExifTag.PixelYDimension, CType(height, SixLabors.ImageSharp.Number))

            sharpImage.Metadata.ExifProfile = profile
        Catch ex As Exception
            ' Losing metadata is bad; losing the edit is worse.
            AppFileLogger.WriteLine(" w0040: EXIF transfer skipped: " & ex.Message)
        End Try
    End Sub

End Class
#End If
