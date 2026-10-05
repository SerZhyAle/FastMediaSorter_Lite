#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' The editor's whole-picture operations (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §6.1,
''' ticket 126 Ф-3): the crop that changes the picture's SIZE, and the rotate and flip
''' that re-address it.
'''
''' They are here rather than inside the window for the reason <see cref="EditorGeometry"/> is:
''' "the kept pixels arrive unchanged, at the right offset" is a claim a test can check
''' exactly and an eye cannot. A crop off by one pixel, or resampled through the default
''' interpolation, looks perfectly fine on screen and is only found later, in the file.
'''
''' Modern-only, like the editor.
''' </summary>
Friend Module EditorImageOps

    ''' <summary>
    ''' A new bitmap holding exactly <paramref name="rect"/> of <paramref name="source"/>.
    '''
    ''' Three deliberate choices, each of which is a way this can go subtly wrong:
    ''' <list type="bullet">
    ''' <item>32bppArgb always, like the editor's own surface - an indexed source would
    ''' otherwise produce an indexed crop, and Graphics.FromImage refuses those, so the
    ''' next brush stroke would throw.</item>
    ''' <item><see cref="Graphics.DrawImageUnscaled"/> at a NEGATIVE offset rather than the
    ''' source-rectangle overload: nothing is scaled, so no interpolation mode and no pixel
    ''' offset convention can shift the result by half a pixel. What falls outside the new
    ''' bitmap is simply not drawn.</item>
    ''' <item>SourceCopy, so the pixels are written rather than blended onto the transparent
    ''' surface underneath - a semi-transparent PNG would otherwise come back darker.</item>
    ''' </list>
    ''' </summary>
    Friend Function CropTo(source As Bitmap, rect As Rectangle) As Bitmap
        If source Is Nothing Then Return Nothing

        Dim area As Rectangle = EditorGeometry.ClampCropRect(rect, source.Size)
        If area.Width <= 0 OrElse area.Height <= 0 Then Return Nothing

        Dim cropped As New Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb)
        Try
            Using g As Graphics = Graphics.FromImage(cropped)
                g.CompositingMode = CompositingMode.SourceCopy
                g.DrawImageUnscaled(source, -area.Left, -area.Top)
            End Using
        Catch
            cropped.Dispose()
            Throw
        End Try

        Return cropped
    End Function

    ''' <summary>
    ''' The picture turned by exactly 90 degrees (126 Ф-3). The rotation is a whole copy
    ''' rotated in one step - <see cref="Bitmap.RotateFlip"/> on a copy of the source, so
    ''' the caller's bitmap stays intact as the undo snapshot and the W/H swap is exact:
    ''' no interpolation runs at a right angle, and what lands in the file is the same
    ''' pixels, re-addressed.
    '''
    ''' The EXIF <c>Orientation</c> rule of the encoder is untouched (126 Q126-3): the
    ''' policy describes the pixels the writer is handed, whatever turn they took here.
    ''' </summary>
    Friend Function Rotate90(source As Bitmap, clockwise As Boolean) As Bitmap
        If source Is Nothing Then Return Nothing

        Dim turned As New Bitmap(source)
        Try
            turned.RotateFlip(If(clockwise, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipNone))
            Return turned
        Catch
            turned.Dispose()
            Throw
        End Try
    End Function

    ''' <summary>
    ''' The picture mirrored (126 Ф-3): <paramref name="horizontal"/> is a reflection in a
    ''' VERTICAL axis - left and right swap, which is what "flip horizontally" means -
    ''' and False is a reflection in a horizontal one. A copy, like <see cref="Rotate90"/>,
    ''' so the undo step is the unmirrored original.
    ''' </summary>
    Friend Function Mirror(source As Bitmap, horizontal As Boolean) As Bitmap
        If source Is Nothing Then Return Nothing

        Dim mirrored As New Bitmap(source)
        Try
            mirrored.RotateFlip(If(horizontal, RotateFlipType.RotateNoneFlipX, RotateFlipType.RotateNoneFlipY))
            Return mirrored
        Catch
            mirrored.Dispose()
            Throw
        End Try
    End Function

End Module
#End If
