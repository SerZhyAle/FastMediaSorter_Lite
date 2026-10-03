Option Strict On

Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' PictureBox that scales the image with high-quality (bicubic) interpolation
''' when <see cref="Is_HighQuality_Scaling"/> is on, and (modern build) can draw
''' only a viewport of the image instead of the whole one.
'''
''' The interpolation mode has to be set on the Graphics object BEFORE the image is
''' drawn. The control's Paint event fires only AFTER the image is already painted,
''' so it is too late there - hence this subclass overrides OnPaint and configures
''' the Graphics before anything is drawn.
'''
''' VIEWPORT (spec 012 §5): when <see cref="SourceRect"/> and <see cref="DestRect"/>
''' are both set, OnPaint draws exactly that part of the image
''' (<see cref="SourceRect"/>, native pixels) into <see cref="DestRect"/> (panel
''' pixels) instead of letting the base draw the whole image per SizeMode. This is
''' how zoom/pan work without resizing the control. When the rects are empty the
''' base path runs, unchanged.
'''
''' MEDIA PAINT: the overlays (OCR, info, PDF, audio surface) hang on
''' <see cref="MediaPaint"/> rather than Control.Paint, because in the viewport path
''' the base OnPaint never runs and Control.Paint cannot be raised by hand. It fires
''' at the end of OnPaint on every path, after the image is on the surface - the
''' same moment the base chain used to raise Paint.
''' </summary>
Public Class HqPictureBox
    Inherits PictureBox

    Public Custom Event MediaPaint As PaintEventHandler
        AddHandler(value As PaintEventHandler)
            MyBase.Events.AddHandler(media_Paint_Key, value)
        End AddHandler
        RemoveHandler(value As PaintEventHandler)
            MyBase.Events.RemoveHandler(media_Paint_Key, value)
        End RemoveHandler
        RaiseEvent(sender As Object, e As PaintEventArgs)
            CType(MyBase.Events(media_Paint_Key), PaintEventHandler)?.Invoke(sender, e)
        End RaiseEvent
    End Event
    Private Shared ReadOnly media_Paint_Key As New Object()

    ''' <summary>The visible strip of the native image, in image pixels. Empty = no
    ''' viewport, the base SizeMode drawing applies.</summary>
    Public Property SourceRect As Rectangle = Rectangle.Empty

    ''' <summary>Where the visible strip is painted, in control-client pixels.</summary>
    Public Property DestRect As Rectangle = Rectangle.Empty

    Protected Overrides Sub OnPaint(pe As PaintEventArgs)
        If Is_HighQuality_Scaling AndAlso Me.Image IsNot Nothing Then
            pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality
            pe.Graphics.SmoothingMode = SmoothingMode.HighQuality
        End If

        Dim drew_Viewport As Boolean = False
        If Me.Image IsNot Nothing AndAlso Not SourceRect.IsEmpty AndAlso Not DestRect.IsEmpty Then
            ' TileFlipXY stops GDI+ from blending the outermost source row/column with
            ' the off-image (transparent) area during rescaling - the same fix the
            ' perspective sampler documents; without it the zoomed edge darkens.
            Using attr As New System.Drawing.Imaging.ImageAttributes()
                attr.SetWrapMode(Drawing2D.WrapMode.TileFlipXY)
                pe.Graphics.DrawImage(Me.Image, DestRect, SourceRect.X, SourceRect.Y,
                                      SourceRect.Width, SourceRect.Height, GraphicsUnit.Pixel, attr)
            End Using
            drew_Viewport = True
        End If

        If Not drew_Viewport Then
            MyBase.OnPaint(pe)
        End If

        RaiseEvent MediaPaint(Me, pe)
    End Sub

End Class
