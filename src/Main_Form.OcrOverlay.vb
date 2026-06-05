Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text
Imports System.Windows.Forms

' OCR + Translation overlay rendering.
'
' The overlay is NEVER baked into the bitmap; it is painted in the PictureBox
' Paint handlers, on top of the image. Because the app expresses zoom/pan by
' resizing/moving the PictureBox itself (SizeMode = Zoom), the displayed image
' rectangle is exactly GetZoomedImageRectangle(image, pb.ClientSize) — so the
' overlay re-derives its geometry from the live ClientSize on every paint and
' therefore stays aligned through fullscreen, super-fullscreen, Ctrl+wheel zoom,
' Shift+wheel 1:1, drag and resize, with no separate pan model.
Partial Public Class Main_Form

    Private Sub Picture_Box_1_Paint(sender As Object, e As PaintEventArgs) Handles Picture_Box_1.Paint
        PaintOcrOverlay(Picture_Box_1, e)
    End Sub

    Private Sub Picture_Box_2_Paint(sender As Object, e As PaintEventArgs) Handles Picture_Box_2.Paint
        PaintOcrOverlay(Picture_Box_2, e)
    End Sub

    Private Sub PaintOcrOverlay(pb As PictureBox, e As PaintEventArgs)
        If Not ocr_Overlay_Visible Then Return
        If ocr_Settings Is Nothing Then Return

        Dim doc As OcrOverlayDocument = current_Overlay_Document
        If doc Is Nothing OrElse doc.Blocks.Count = 0 Then Return

        ' Only paint over the file the document was computed for.
        If Not String.Equals(doc.FilePath, Current_File_Name, StringComparison.Ordinal) Then Return

        Dim img As Image = pb.Image
        If img Is Nothing Then Return
        If doc.ImageSize.Width <= 0 OrElse doc.ImageSize.Height <= 0 Then Return

        Dim fit As Rectangle = GetZoomedImageRectangle(doc.ImageSize.Width, doc.ImageSize.Height, pb.ClientSize.Width, pb.ClientSize.Height)
        If fit.IsEmpty Then Return
        Dim scale As Double = fit.Width / CDbl(doc.ImageSize.Width)

        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit

        Dim alpha As Integer = OcrTranslateSettings.ClampOpacity(ocr_Settings.OverlayOpacity)
        Dim fillColor As Color = Color.FromArgb(alpha, 250, 250, 250)
        Dim borderColor As Color = Color.FromArgb(Math.Min(255, alpha), 60, 60, 60)

        Using sf As New StringFormat()
            sf.Alignment = StringAlignment.Center
            sf.LineAlignment = StringAlignment.Center
            sf.Trimming = StringTrimming.EllipsisWord
            sf.FormatFlags = CType(0, StringFormatFlags)

            Using fillBrush As New SolidBrush(fillColor)
                Using borderPen As New Pen(borderColor, 1.0F)
                    Using textBrush As New SolidBrush(Color.Black)
                        For Each block As OcrBlock In doc.Blocks
                            Dim text As String = If(Not String.IsNullOrWhiteSpace(block.TranslatedText), block.TranslatedText, block.SourceText)
                            If String.IsNullOrWhiteSpace(text) Then Continue For

                            Dim r As Rectangle = MapBoxToClient(block.Box, fit, scale)
                            If r.Width < 4 OrElse r.Height < 4 Then Continue For

                            g.FillRectangle(fillBrush, r)
                            g.DrawRectangle(borderPen, r)

                            Dim padded As New RectangleF(r.X + 2, r.Y + 1, Math.Max(1, r.Width - 4), Math.Max(1, r.Height - 2))
                            Using fnt As Font = FitFont(g, text, padded, sf)
                                g.DrawString(text, fnt, textBrush, padded, sf)
                            End Using
                        Next
                    End Using
                End Using
            End Using
        End Using
    End Sub

    Private Shared Function MapBoxToClient(box As Rectangle, fit As Rectangle, scale As Double) As Rectangle
        Dim x As Integer = fit.Left + CInt(Math.Round(box.X * scale))
        Dim y As Integer = fit.Top + CInt(Math.Round(box.Y * scale))
        Dim w As Integer = CInt(Math.Round(box.Width * scale))
        Dim h As Integer = CInt(Math.Round(box.Height * scale))
        Return New Rectangle(x, y, w, h)
    End Function

    ''' <summary>Largest font (down to a 7px floor) whose wrapped text fits the box.</summary>
    Private Shared Function FitFont(g As Graphics, text As String, rect As RectangleF, sf As StringFormat) As Font
        Dim maxSize As Single = Math.Max(7.0F, Math.Min(CSng(rect.Height * 0.85), 26.0F))
        Dim size As Single = maxSize
        While size > 7.0F
            Using probe As New Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel)
                Dim measured As SizeF = g.MeasureString(text, probe, CInt(Math.Ceiling(rect.Width)), sf)
                If measured.Height <= rect.Height AndAlso measured.Width <= rect.Width + 1 Then
                    Return New Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel)
                End If
            End Using
            size -= 1.0F
        End While
        Return New Font("Segoe UI", 7.0F, FontStyle.Regular, GraphicsUnit.Pixel)
    End Function

End Class
