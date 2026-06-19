Option Strict On

Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' PictureBox that scales the image with high-quality (bicubic) interpolation
''' when <see cref="Is_HighQuality_Scaling"/> is on.
'''
''' The interpolation mode has to be set on the Graphics object BEFORE the base
''' PictureBox draws the image (SizeMode = Zoom). The control's Paint event fires
''' only AFTER the image is already painted, so it is too late there - hence this
''' subclass overrides OnPaint and configures the Graphics before MyBase.OnPaint.
''' The existing Paint event handlers (OCR/info overlays) still run afterwards.
''' </summary>
Public Class HqPictureBox
    Inherits PictureBox

    Protected Overrides Sub OnPaint(pe As PaintEventArgs)
        If Is_HighQuality_Scaling AndAlso Me.Image IsNot Nothing Then
            pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality
            pe.Graphics.SmoothingMode = SmoothingMode.HighQuality
        End If
        MyBase.OnPaint(pe)
    End Sub

End Class
