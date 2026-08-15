#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO

''' <summary>
''' The half of the imaging seam that writes (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §9.1).
'''
''' Until this file existed the application had NO path that put pixels on disk at all:
''' <see cref="IImageDecoder"/> has two members and both read, and every SaveAsPng /
''' SaveAsGif / .Save( in the tree transcodes into a MemoryStream (the decoder's own PNG
''' bridge, the BMP handed to Tesseract). That is why "R" rotates the display and lies
''' about the file, and why there is no crop, no compressed copy and no "save this frame".
'''
''' Modern-only, like its single consumer: whole file "#If Not NETFRAMEWORK". The
''' interface exists anyway, for the same reason the decoder's does - net48 can be given
''' its own implementation if it ever needs one, without touching a call site.
''' </summary>
Friend Interface IImageEncoder

    ''' <summary>
    ''' Extensions this implementation can write - lowercase, with the dot. Deliberately
    ''' NARROWER than the set the viewer can read (§9.1): writing .gif would quietly
    ''' wreck colours through a 256-entry palette, .ico holds several resolutions with no
    ''' obvious one to replace, .wmf/.emf are vector and rasterising them is not "saving",
    ''' and .exif is a legacy entry in the read list with no format behind it.
    ''' </summary>
    Function WritableExtensions() As IReadOnlyCollection(Of String)

    ''' <summary>
    ''' Writes <paramref name="bitmap"/> to <paramref name="targetPath"/> in the format its
    ''' extension names.
    '''
    ''' <paramref name="exifSource"/> is the ORIGINAL file's bytes, the donor of the EXIF
    ''' profile - Nothing carries nothing across. Re-saving a JPEG without this silently
    ''' erases the capture date, the camera and the coordinates, which in a photo sorter is
    ''' data loss (§9.5).
    '''
    ''' <paramref name="orientation"/> states what the pixels already are, so the written
    ''' Orientation tag can be made to agree with them - see <see cref="ExifOrientationPolicy"/>.
    '''
    ''' Throws on an unwritable extension and on any I/O failure; the caller writes to a
    ''' temporary name and swaps, so a throw never damages the original (§9.6).
    ''' </summary>
    Sub EncodeToFile(bitmap As Bitmap, targetPath As String, exifSource As Byte(),
                     orientation As ExifOrientationPolicy)

End Interface

''' <summary>
''' What the pixels handed to the encoder already are, which decides what the written
''' Orientation tag has to say (§9.2). The invariant is one sentence: <b>the orientation
''' tag always describes the pixels that were written</b>. Break it and the photo turns
''' twice in the next program that opens it.
'''
''' Which one applies follows the viewer's own <c>Is_Exif_AutoRotate</c> setting, because
''' the editor shows what the viewer shows - the caller passes it in rather than the
''' encoder reading a global, so this decision is testable without a running viewer.
''' </summary>
Friend Enum ExifOrientationPolicy
    ''' <summary>
    ''' Auto-rotate is on: FileManager.ApplyExifOrientation already turned the pixels
    ''' upright and stripped the tag. The written file must have NO Orientation.
    ''' </summary>
    PixelsAreUpright

    ''' <summary>
    ''' Auto-rotate is off: the pixels are as they were stored and the original's tag still
    ''' describes them. It is carried across unchanged.
    ''' </summary>
    PixelsAreRaw
End Enum

''' <summary>Picks the platform encoder once, by the shape of <see cref="ImageDecoderProvider"/>.</summary>
Friend Module ImageEncoderProvider

    Private ReadOnly platform_Encoder As IImageEncoder = New ModernImageSharpEncoder()

    Friend ReadOnly Property Current As IImageEncoder
        Get
            Return platform_Encoder
        End Get
    End Property

    ''' <summary>
    ''' Can the result be written back in this file's own format? False means the editor
    ''' offers "Save as.." only - and says so, rather than greying a button in silence.
    ''' </summary>
    ' NOTE: the parameter must not be called "path" - VB is case-insensitive, so the name
    ' would shadow System.IO.Path for the whole method.
    Friend Function IsWritableExtension(filePath As String) As Boolean
        If String.IsNullOrEmpty(filePath) Then Return False
        Dim extension As String = Path.GetExtension(filePath)
        If String.IsNullOrEmpty(extension) Then Return False
        Return Current.WritableExtensions().Contains(extension.ToLowerInvariant())
    End Function

End Module
#End If
