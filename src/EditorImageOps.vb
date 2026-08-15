#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' The editor's one operation that changes the SIZE of the picture
''' (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §6.1).
'''
''' It is here rather than inside the window for the reason <see cref="EditorGeometry"/> is:
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

End Module
#End If
