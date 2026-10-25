#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports ImageMagick

''' <summary>
''' The .NET 10 encoder: Magick.NET (ImageMagick) - the same engine the decoder reads with, so a
''' format the viewer can open is a format this can write, with no OS codec in either direction
''' (specification 270 replaced the managed encoder this class was built on; its behaviour is the
''' contract, specification 270 R3 and R5).
'''
''' The GDI+ -> ImageMagick bridge hands the bitmap's pixels over directly (BGRA, 8 bits a
''' channel, the layout GDI+ keeps them in), so nothing is spent on the crossing but a copy.
''' </summary>
Friend NotInheritable Class ModernMagickEncoder
    Implements IImageEncoder

    ''' <summary>
    ''' Fixed by SPECIFICATION_IMAGE_EDITOR_DOTNET10.md section 9.5, with no setting in v1: high
    ''' enough that one re-save is not visible, low enough that a photo does not grow.
    ''' </summary>
    Private Const JpegQuality As Integer = 92
    Friend Const Jpeg_Sampling_Factor As String = "2x2,1x1,1x1"

    ''' <summary>Section 9.1. Six of the eleven extensions the viewer reads - see the interface.</summary>
    Private Shared ReadOnly Writable As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp"}

    ''' <summary>
    ''' PNG chunks a saved file does not get: the background colour, the gamma and chromaticity
    ''' ImageMagick derives for an untagged picture, and the save time - the managed encoder wrote
    ''' none of them, and GDI+ corrects the colours of a PNG that carries gAMA. sRGB/iCCP stay:
    ''' ImageMagick writes an sRGB colour profile as the sRGB chunk.
    ''' </summary>
    Private Const Saved_Png_Excluded_Chunks As String = "bKGD,cHRM,gAMA,date,tIME,oFFs,vpAg,caNv"

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

        MagickResourceLimits.EnsureApplied()
        Using image As MagickImage = FromBitmap(bitmap)
            Dim donor As DonorMetadata = ReadDonorMetadata(exifSource)
            ApplyExifProfile(image, donor.Exif, orientation, bitmap.Width, bitmap.Height)
            ApplyOtherProfiles(image, donor)
            Save(image, output, extension)
        End Using
    End Sub

    ' ----------------------------------------------------------------- the donor ----

    ''' <summary>What the original carries besides its pixels: EXIF, and - audit 052 G-08 -
    ''' the colour profile, XMP and IPTC, which used to be dropped on every save.</summary>
    Friend NotInheritable Class DonorMetadata
        Friend Property Exif As IExifProfile
        Friend Property Icc As IImageProfile
        Friend Property Xmp As IImageProfile
        Friend Property Iptc As IImageProfile
    End Class

    ''' <summary>
    ''' TIFF tags that describe a TIFF file's own pixel storage. A TIFF donor's EXIF is read from
    ''' its first directory (see <see cref="ExifFromTiffStructure"/>), and these must not travel
    ''' into another file, where they would point at nothing. XMP travels as its own profile, so
    ''' it leaves the EXIF too; the IPTC (33723) and ICC (34675) tags have no Magick EXIF tag at
    ''' all - its reader sets them aside as invalid, so they never reach the profile.
    ''' </summary>
    Private Shared ReadOnly Tiff_Storage_Tags As ExifTag() = {
        ExifTag.ImageWidth, ExifTag.ImageLength, ExifTag.BitsPerSample, ExifTag.Compression,
        ExifTag.PhotometricInterpretation, ExifTag.StripOffsets, ExifTag.SamplesPerPixel, ExifTag.RowsPerStrip,
        ExifTag.StripByteCounts, ExifTag.PlanarConfiguration, ExifTag.TileWidth, ExifTag.TileLength,
        ExifTag.TileOffsets, ExifTag.TileByteCounts, ExifTag.Predictor, ExifTag.ExtraSamples, ExifTag.SampleFormat,
        ExifTag.ColorMap, ExifTag.FillOrder, ExifTag.SubfileType, ExifTag.OldSubfileType, ExifTag.JPEGTables,
        ExifTag.XMP}

    ''' <summary>
    ''' Reads the donor's profiles without decoding its pixels - which matters when the donor is
    ''' a 24-megapixel photo on a network share. The container is read from the bytes and the
    ''' coder pinned (<see cref="MagickReadGate"/>): a Magick ping for most formats; for a WEBP
    ''' the metadata chunks themselves, because ImageMagick's ping of a WEBP returns no profile;
    ''' for a TIFF the EXIF from the file's own first directory, because ImageMagick keeps a
    ''' TIFF's EXIF as properties, not as a profile. Never throws: a donor that cannot be read
    ''' gives an empty set, and the save goes on (audit 052 G-12, spec 236: a HEIC or JPEG XL
    ''' photo keeps its capture date and GPS through "Save as").
    ''' </summary>
    Friend Shared Function ReadDonorMetadata(source As Byte()) As DonorMetadata
        Dim donor As New DonorMetadata()
        If source Is Nothing OrElse source.Length = 0 Then Return donor
        Try
            Using stream As New MemoryStream(source, writable:=False)
                Dim container As ImageContainer = MagickReadGate.Sniff(stream)
                Select Case container
                    Case ImageContainer.Unknown
                        AppFileLogger.WriteLine(" w0041: donor metadata not read: unrecognised image format")
                    Case ImageContainer.Webp
                        Dim exif As Byte() = TiffHeaderOnwards(AnimationHeader.ReadWebpChunk(stream, "EXIF"))
                        If exif IsNot Nothing Then donor.Exif = New ExifProfile(exif)
                        Dim icc As Byte() = AnimationHeader.ReadWebpChunk(stream, "ICCP")
                        If icc IsNot Nothing AndAlso icc.Length > 0 Then donor.Icc = New ImageProfile("icc", icc)
                        Dim xmp As Byte() = AnimationHeader.ReadWebpChunk(stream, "XMP ")
                        If xmp IsNot Nothing AndAlso xmp.Length > 0 Then donor.Xmp = New ImageProfile("xmp", xmp)
                    Case Else
                        MagickResourceLimits.EnsureApplied()
                        Using probe As New MagickImage()
                            probe.Ping(source, MagickReadGate.SettingsFor(container))
                            donor.Exif = probe.GetExifProfile()
                            donor.Icc = NonEmpty(probe.GetProfile("icc"))
                            donor.Xmp = NonEmpty(probe.GetProfile("xmp"))
                            donor.Iptc = NonEmpty(probe.GetProfile("iptc"))
                        End Using
                        If donor.Exif Is Nothing AndAlso container = ImageContainer.Tiff Then donor.Exif = ExifFromTiffStructure(source)
                End Select
            End Using
        Catch ex As Exception
            ' Losing metadata is bad; losing the edit is worse.
            AppFileLogger.WriteLine(" w0041: donor metadata not read: " & ex.Message)
        End Try
        Return donor
    End Function

    Private Shared Function NonEmpty(profile As IImageProfile) As IImageProfile
        If profile Is Nothing Then Return Nothing
        Dim data As Byte() = profile.ToByteArray()
        Return If(data Is Nothing OrElse data.Length = 0, Nothing, profile)
    End Function

    ''' <summary>
    ''' A TIFF file IS the structure EXIF borrows, so its first directory, with the Exif and GPS
    ''' directories it points to, reads as an EXIF profile; the tags that describe the TIFF's own
    ''' pixel storage are removed. Nothing for a TIFF without any other tag (or a BigTIFF, which
    ''' the EXIF reader does not parse).
    ''' </summary>
    Private Shared Function ExifFromTiffStructure(source As Byte()) As IExifProfile
        Dim profile As New ExifProfile(source)
        For Each tag As ExifTag In Tiff_Storage_Tags
            profile.RemoveValue(tag)
        Next
        Return If(profile.Values.Any(), profile, Nothing)
    End Function

    ''' <summary>
    ''' The EXIF bytes from the TIFF header on. HEIF and WEBP store the block behind an offset
    ''' and often an "Exif\0\0" marker, and which of the two survives depends on the file; the
    ''' bare TIFF structure is found by its byte-order mark ("II*\0" or "MM\0*") rather than by
    ''' assuming a fixed prefix.
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
    ''' The colour profile is attached, never applied: the pixels are the original's values.
    ''' </summary>
    Private Shared Sub ApplyOtherProfiles(image As MagickImage, donor As DonorMetadata)
        Try
            If donor.Icc IsNot Nothing Then image.SetProfile(New ImageProfile("icc", donor.Icc.ToByteArray()))
        Catch ex As Exception
            AppFileLogger.WriteLine(" w0042: ICC transfer skipped: " & ex.Message)
        End Try
        Try
            If donor.Xmp IsNot Nothing Then image.SetProfile(New ImageProfile("xmp", donor.Xmp.ToByteArray()))
        Catch ex As Exception
            AppFileLogger.WriteLine(" w0043: XMP transfer skipped: " & ex.Message)
        End Try
        Try
            If donor.Iptc IsNot Nothing Then image.SetProfile(New ImageProfile("iptc", donor.Iptc.ToByteArray()))
        Catch ex As Exception
            AppFileLogger.WriteLine(" w0044: IPTC transfer skipped: " & ex.Message)
        End Try
    End Sub

    ' ------------------------------------------------------------------- bridge ----

    ''' <summary>
    ''' GDI+ Bitmap -> ImageMagick image: the pixels as 32-bit BGRA (GDI+ converts any other
    ''' layout while locking), the resolution as density. A bitmap without an alpha channel
    ''' gives an image without one, so a JPEG source saved as PNG stays RGB.
    ''' </summary>
    Private Shared Function FromBitmap(bitmap As Bitmap) As MagickImage
        Dim width As Integer = bitmap.Width
        Dim height As Integer = bitmap.Height
        Dim rowBytes As Integer = width * 4
        Dim pixels(rowBytes * height - 1) As Byte
        Dim data As BitmapData = bitmap.LockBits(New Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            For y As Integer = 0 To height - 1
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * rowBytes, rowBytes)
            Next
        Finally
            bitmap.UnlockBits(data)
        End Try

        Dim image As New MagickImage()
        Try
            image.ReadPixels(pixels, New PixelReadSettings(CUInt(width), CUInt(height), StorageType.Char, PixelMapping.BGRA))
            If Not System.Drawing.Image.IsAlphaPixelFormat(bitmap.PixelFormat) Then image.Alpha(AlphaAction.Off)
            If bitmap.HorizontalResolution > 0 AndAlso bitmap.VerticalResolution > 0 Then
                image.Density = New Density(bitmap.HorizontalResolution, bitmap.VerticalResolution, DensityUnit.PixelsPerInch)
            End If
            Return image
        Catch
            image.Dispose()
            Throw
        End Try
    End Function

    ''' <summary>Writes the image in the format the extension names; the stream stays the caller's.</summary>
    Private Shared Sub Save(image As MagickImage, output As Stream, extension As String)
        Select Case extension
            Case ".jpg", ".jpeg"
                image.Quality = CUInt(JpegQuality)
                ' 4:2:0, as the replaced encoder wrote: ImageMagick switches to 4:4:4 from quality
                ' 90 up, which made the same save 45 % larger (specification 270 R5).
                image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", Jpeg_Sampling_Factor)
                image.Write(output, MagickFormat.Jpeg)
            Case ".webp"
                ' Lossless: an edit is already a new file, and re-encoding a photo lossily
                ' on every save would compound. Section 14.4 accepts that this can come out
                ' larger than a lossy source.
                image.Settings.SetDefine(MagickFormat.WebP, "lossless", True)
                image.Write(output, MagickFormat.WebP)
            Case ".png"
                image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", Saved_Png_Excluded_Chunks)
                image.Write(output, MagickFormat.Png)
            Case ".bmp"
                image.Write(output, MagickFormat.Bmp)
            Case ".tiff"
                image.Write(output, MagickFormat.Tiff)
            Case Else
                Throw New NotSupportedException("Cannot write " & extension)
        End Select
    End Sub

    ' --------------------------------------------------------------------- EXIF ----

    ''' <summary>
    ''' Carries the original's EXIF across, then corrects the three things that would
    ''' otherwise be lies about the file just written (section 9.5):
    '''
    '''   * <b>Orientation</b> - removed or kept, per <paramref name="orientation"/>. A tag
    '''     copied blindly is the "photo turns twice" bug. ImageMagick rewrites the EXIF
    '''     orientation from the image's own orientation when it writes, so a kept tag is set
    '''     on the image as well.
    '''   * <b>The embedded thumbnail</b> - always dropped. It is a picture of the file
    '''     BEFORE the edit, and Explorer and phones prefer it to the real pixels, so the
    '''     edited file would keep looking unedited. That is the nastiest shape this class
    '''     of bug takes, because nothing looks broken.
    '''   * <b>PixelXDimension / PixelYDimension</b> - rewritten from the bitmap. Crop
    '''     changes the size, and section 14.2 names this as the field that goes stale.
    '''
    ''' A malformed profile in the source must never cost the user their save: the whole
    ''' transfer is best-effort, and failing it writes the pixels with no EXIF rather than
    ''' throwing.
    ''' </summary>
    Private Shared Sub ApplyExifProfile(image As MagickImage, source As IExifProfile,
                                        orientation As ExifOrientationPolicy, width As Integer, height As Integer)
        If source Is Nothing Then Return

        Try
            Dim profile As New ExifProfile(source.ToByteArray())

            If orientation = ExifOrientationPolicy.PixelsAreUpright Then
                ' The pixels were rotated upright and the viewer stripped the tag; saying
                ' "6" here would turn them again.
                profile.RemoveValue(ExifTag.Orientation)
            Else
                Dim kept As IExifValue(Of UShort) = profile.GetValue(ExifTag.Orientation)
                If kept IsNot Nothing AndAlso kept.Value >= 1US AndAlso kept.Value <= 8US Then
                    image.Orientation = CType(CInt(kept.Value), ImageMagick.Orientation)
                End If
            End If

            ' IFD1 holds the thumbnail through this pointer pair; without them there is
            ' nothing for a reader to find.
            profile.RemoveThumbnail()
            profile.RemoveValue(ExifTag.JPEGInterchangeFormat)
            profile.RemoveValue(ExifTag.JPEGInterchangeFormatLength)

            profile.SetValue(ExifTag.PixelXDimension, New Number(CUInt(width)))
            profile.SetValue(ExifTag.PixelYDimension, New Number(CUInt(height)))

            image.SetProfile(profile)
        Catch ex As Exception
            ' Losing metadata is bad; losing the edit is worse.
            AppFileLogger.WriteLine(" w0040: EXIF transfer skipped: " & ex.Message)
        End Try
    End Sub

End Class
#End If
