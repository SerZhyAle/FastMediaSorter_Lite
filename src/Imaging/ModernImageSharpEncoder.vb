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

        Dim extension As String = Path.GetExtension(targetPath).ToLowerInvariant()
        If Not Writable.Contains(extension) Then
            Throw New NotSupportedException("Cannot write " & extension & " - writable formats are " & String.Join(", ", Writable) & ".")
        End If

        Using sharpImage As SixLabors.ImageSharp.Image = ToImageSharp(bitmap)
            ApplyExifProfile(sharpImage, exifSource, orientation, bitmap.Width, bitmap.Height)
            Save(sharpImage, targetPath, extension)
        End Using
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

    Private Shared Sub Save(sharpImage As SixLabors.ImageSharp.Image, targetPath As String, extension As String)
        Select Case extension
            Case ".jpg", ".jpeg"
                SixLabors.ImageSharp.ImageExtensions.Save(sharpImage, targetPath,
                    New SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder With {.Quality = JpegQuality})
            Case ".webp"
                ' Lossless: an edit is already a new file, and re-encoding a photo lossily
                ' on every save would compound. §14.4 accepts that this can come out larger
                ' than a lossy source.
                SixLabors.ImageSharp.ImageExtensions.Save(sharpImage, targetPath,
                    New SixLabors.ImageSharp.Formats.Webp.WebpEncoder With {
                        .FileFormat = SixLabors.ImageSharp.Formats.Webp.WebpFileFormatType.Lossless})
            Case Else
                ' PNG, BMP and TIFF need no options - all three are lossless, and ImageSharp
                ' picks the encoder from the extension.
                SixLabors.ImageSharp.ImageExtensions.Save(sharpImage, targetPath)
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
    Private Shared Sub ApplyExifProfile(sharpImage As SixLabors.ImageSharp.Image, exifSource As Byte(),
                                        orientation As ExifOrientationPolicy, width As Integer, height As Integer)
        If exifSource Is Nothing OrElse exifSource.Length = 0 Then Return

        Try
            Dim source As ExifProfile = ReadExifProfile(exifSource)
            If source Is Nothing Then Return

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
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0040: EXIF transfer skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Identify parses the header only - the donor's pixels are never decoded, which
    ''' matters when the donor is a 24-megapixel photo on a network share.
    ''' </summary>
    Private Shared Function ReadExifProfile(exifSource As Byte()) As ExifProfile
        Using stream As New MemoryStream(exifSource, writable:=False)
            Dim info As SixLabors.ImageSharp.ImageInfo = SixLabors.ImageSharp.Image.Identify(stream)
            If info Is Nothing OrElse info.Metadata Is Nothing Then Return Nothing
            Return info.Metadata.ExifProfile
        End Using
    End Function

End Class
#End If
