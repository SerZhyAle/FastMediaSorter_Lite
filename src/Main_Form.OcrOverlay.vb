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
' rectangle is exactly GetZoomedImageRectangle(image, pb.ClientSize) - so the
' overlay re-derives its geometry from the live ClientSize on every paint and
' therefore stays aligned through fullscreen, super-fullscreen, Ctrl+wheel zoom,
' Shift+wheel 1:1, drag and resize, with no separate pan model.
Partial Public Class Main_Form

    Private Sub Picture_Box_1_Paint(sender As Object, e As PaintEventArgs) Handles Picture_Box_1.Paint
        PaintOcrOverlay(Picture_Box_1, e)
        PaintInfoOverlay(Picture_Box_1, e)
    End Sub

    Private Sub Picture_Box_2_Paint(sender As Object, e As PaintEventArgs) Handles Picture_Box_2.Paint
        PaintOcrOverlay(Picture_Box_2, e)
        PaintInfoOverlay(Picture_Box_2, e)
    End Sub

    ''' <summary>
    ''' Optional HUD in the top-left of the media surface: the current file name
    ''' and its position (N/total). Painted in the PictureBox Paint handler (never
    ''' baked into the bitmap), so it costs nothing when the option is off and is
    ''' especially useful in full-screen where the status bar is hidden.
    ''' </summary>
    Private Sub PaintInfoOverlay(pb As PictureBox, e As PaintEventArgs)
        If Not Is_Show_Info_Overlay Then Return
        If pb.Image Is Nothing Then Return
        If String.IsNullOrEmpty(Current_File_Name) Then Return

        Dim caption As String = IO.Path.GetFileName(Current_File_Name) & "    " &
                                (current_File_Index + 1).ToString() & "/" & total_File_Count.ToString()

        Dim g As Graphics = e.Graphics
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit

        Using fnt As New Font("Segoe UI", 11.0F, FontStyle.Regular)
            Dim sz As SizeF = g.MeasureString(caption, fnt)
            Dim padX As Integer = 8
            Dim padY As Integer = 4
            Dim box As New Rectangle(8, 8, CInt(Math.Ceiling(sz.Width)) + padX * 2, CInt(Math.Ceiling(sz.Height)) + padY * 2)
            Using bg As New SolidBrush(Color.FromArgb(160, 0, 0, 0))
                g.FillRectangle(bg, box)
            End Using
            Using tb As New SolidBrush(Color.White)
                g.DrawString(caption, fnt, tb, box.X + padX, box.Y + padY)
            End Using
        End Using
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

        Using sf As New StringFormat()
            sf.Alignment = StringAlignment.Center
            sf.LineAlignment = StringAlignment.Center
            sf.Trimming = StringTrimming.EllipsisWord
            sf.FormatFlags = CType(0, StringFormatFlags)

            Using fillBrush As New SolidBrush(fillColor)
                Using textBrush As New SolidBrush(Color.Black)
                    For Each block As OcrBlock In doc.Blocks
                        Dim text As String = If(Not String.IsNullOrWhiteSpace(block.TranslatedText), block.TranslatedText, block.SourceText)
                        If String.IsNullOrWhiteSpace(text) Then Continue For

                        Dim r As Rectangle = MapBoxToClient(block.Box, fit, scale)
                        If r.Width < 4 OrElse r.Height < 4 Then Continue For

                        ' Filled box only - no border, so the overlay reads as clean text.
                        g.FillRectangle(fillBrush, r)

                        ' Anchor the overlay font to the ORIGINAL text size: the median
                        ' source line height (image px) scaled to display px. This makes
                        ' the translation read at roughly the same size as the text it
                        ' replaced, instead of being squeezed to a tiny capped font.
                        Dim origLineH As Single = CSng(MedianBlockLineHeight(block) * scale)

                        ' Give very short source boxes a minimum text height (centred
                        ' on the box) so the larger font isn't clipped to nothing.
                        Dim textH As Single = Math.Max(CSng(r.Height), 18.0F)
                        Dim textY As Single = r.Y + (CSng(r.Height) - textH) / 2.0F
                        Dim padded As New RectangleF(r.X + 1, textY, Math.Max(1, r.Width - 2), textH)
                        Using fnt As Font = FitFont(g, text, padded, sf, origLineH)
                            g.DrawString(text, fnt, textBrush, padded, sf)
                        End Using
                    Next
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

    ' Overlay text size bounds (px). Floor raised so translations stay readable;
    ' ceiling raised so large headings/paragraphs render near their original size
    ' instead of being clamped to a tiny font.
    Private Const MinOverlayFont As Single = 8.0F
    Private Const MaxOverlayFont As Single = 200.0F

    ''' <summary>Median glyph (line) height of a block's source lines, in image px.</summary>
    Private Shared Function MedianBlockLineHeight(block As OcrBlock) As Integer
        If block.Lines Is Nothing OrElse block.Lines.Count = 0 Then Return block.Box.Height
        Dim heights As List(Of Integer) = block.Lines.
            Select(Function(l) l.Box.Height).
            Where(Function(h) h > 0).
            OrderBy(Function(h) h).ToList()
        If heights.Count = 0 Then Return block.Box.Height
        Return heights(heights.Count \ 2)
    End Function

    ''' <summary>
    ''' Largest font (down to the floor) whose wrapped text fits the box, but never
    ''' larger than <paramref name="targetPx"/> - the original text size. We start
    ''' from the original size so the translation matches the text it replaced, and
    ''' only shrink when a longer translation would overflow the block.
    ''' </summary>
    Private Shared Function FitFont(g As Graphics, text As String, rect As RectangleF, sf As StringFormat, targetPx As Single) As Font
        Dim cap As Single = If(targetPx > 0, targetPx, CSng(rect.Height))
        Dim maxSize As Single = Math.Max(MinOverlayFont, Math.Min(cap, MaxOverlayFont))
        Dim size As Single = maxSize
        While size > MinOverlayFont
            Using probe As New Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel)
                Dim measured As SizeF = g.MeasureString(text, probe, CInt(Math.Ceiling(rect.Width)), sf)
                If measured.Height <= rect.Height AndAlso measured.Width <= rect.Width + 1 Then
                    Return New Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel)
                End If
            End Using
            size -= 1.0F
        End While
        Return New Font("Segoe UI", MinOverlayFont, FontStyle.Regular, GraphicsUnit.Pixel)
    End Function

End Class
