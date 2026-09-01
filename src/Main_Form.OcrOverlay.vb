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
#If Not NETFRAMEWORK Then
        PaintAudioSurface(e.Graphics, Picture_Box_1.ClientRectangle)
#End If
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

        ' Every plate up front, before any of them is drawn: the growth rung of the ladder
        ' asks where the plate below it starts, and a plate that will not be painted (no
        ' text, or too small to read) must not block a neighbour's growth - hence the
        ' Empty placeholder rather than a shorter list.
        Dim plates(doc.Blocks.Count - 1) As Rectangle
        For i As Integer = 0 To doc.Blocks.Count - 1
            Dim block As OcrBlock = doc.Blocks(i)
            Dim text As String = OverlayTextOf(block)
            If String.IsNullOrWhiteSpace(text) Then Continue For
            Dim r As Rectangle = MapBoxToClient(block.Box, fit, scale)
            If r.Width < 4 OrElse r.Height < 4 Then Continue For
            plates(i) = r
        Next

        Using sf As New StringFormat()
            sf.Alignment = StringAlignment.Center
            sf.LineAlignment = StringAlignment.Center
            ' Last rung of the ladder only - by the time trimming can bite, the font has
            ' already gone down to its floor and the plate has grown as far as it may.
            sf.Trimming = StringTrimming.EllipsisWord
            sf.FormatFlags = CType(0, StringFormatFlags)

            Using fillBrush As New SolidBrush(fillColor)
                Using textBrush As New SolidBrush(Color.Black)
                    For i As Integer = 0 To doc.Blocks.Count - 1
                        Dim block As OcrBlock = doc.Blocks(i)
                        Dim r As Rectangle = plates(i)
                        If r.Width <= 0 Then Continue For

                        Dim plate As Rectangle = DrawOverlayBlock(g, block, r, plates, fit, scale, sf, fillBrush, textBrush, alpha)

                        ' Report the geometry the renderer really used, in image pixels, so
                        ' diagnostics never recomputes its own version of it.
                        block.RenderPlateHeight = CInt(Math.Round(plate.Height / scale))
#If Not NETFRAMEWORK Then
                        OcrDiagnostics.DrawBlockBoxes(g, i, r, plate)
#End If
                    Next
                End Using
            End Using
        End Using

#If Not NETFRAMEWORK Then
        ' After painting, never before: the dump reports the plate rectangles and font sizes
        ' the renderer settled on. Once per document, not once per repaint.
        OcrDiagnostics.DumpDocument(doc)
#End If
    End Sub

    Private Shared Function OverlayTextOf(block As OcrBlock) As String
        Return If(Not String.IsNullOrWhiteSpace(block.TranslatedText), block.TranslatedText, block.SourceText)
    End Function

    ''' <summary>
    ''' Draws one block and returns the plate it actually painted.
    '''
    ''' The plate and the text rectangle are ONE rectangle. They used to be two - the fill
    ''' was the mapped box, the text was drawn in max(box height, 18) centred on it - so on
    ''' any block shorter than 18 px the text spilled above and below its own fill, straight
    ''' onto the picture. Filling exactly what is drawn in removes that by construction.
    '''
    ''' The ladder: shrink to the floor, then grow downwards within the budget, then trim.
    ''' The top edge does not move on any rung.
    ''' </summary>
    Private Shared Function DrawOverlayBlock(g As Graphics, block As OcrBlock, r As Rectangle, plates() As Rectangle,
                                             fit As Rectangle, scale As Double, sf As StringFormat,
                                             fillBrush As Brush, textBrush As Brush, alpha As Integer) As Rectangle
        Dim text As String = OverlayTextOf(block)

        ' Anchor the overlay font to the ORIGINAL text size: the median source line height
        ' (image px) scaled to display px, so the translation reads at roughly the size of
        ' the text it replaced instead of being squeezed to a tiny capped font.
        Dim origLineH As Double = OcrOverlayFit.MedianLineHeight(block) * scale

        Dim budget As Integer = OcrOverlayFit.GrowthBudget(r, plates, fit.Bottom)
        Dim maxHeight As Integer = r.Height + budget
        Dim textWidth As Integer = Math.Max(1, r.Width - 2)

        Dim candidates As List(Of Double) = OcrOverlayFit.FontCandidates(origLineH, MinOverlayFont, MaxOverlayFont)
        Dim chosen As Double = candidates(candidates.Count - 1)
        Dim neededHeight As Double = maxHeight
        Dim truncated As Boolean = True

        For Each candidate As Double In candidates
            Using probe As New Font("Segoe UI", CSng(candidate), FontStyle.Regular, GraphicsUnit.Pixel)
                Dim measured As SizeF = g.MeasureString(text, probe, textWidth, sf)
                If measured.Width <= textWidth + 1 AndAlso measured.Height <= maxHeight Then
                    chosen = candidate
                    neededHeight = measured.Height
                    truncated = False
                    Exit For
                End If
            End Using
        Next

        ' Grow only as far as this text needs, never to the whole budget.
        Dim finalHeight As Integer = Math.Min(maxHeight, Math.Max(r.Height, CInt(Math.Ceiling(neededHeight))))
        Dim plate As New Rectangle(r.X, r.Y, r.Width, finalHeight)

        ' Filled box only - no border, so the overlay reads as clean text.
        Dim fill As Brush = fillBrush
        Dim pen As Brush = textBrush
        Dim sampledFill As SolidBrush = Nothing
        Dim sampledText As SolidBrush = Nothing
#If Not NETFRAMEWORK Then
        ' Sampled colours, when this document has them. Two brushes per coloured block per
        ' frame - far below what this stage removed from the same loop (the old fitter walked
        ' the font size down 1 px at a time, measuring each step). A block with 0 in the
        ' fields was never sampled - by the x86 viewer, or by a build older than this one -
        ' and keeps the constant near-white plate, so the fallback needs no branch of its own.
        If block.PlateBackgroundArgb <> 0 Then
            ' The user's overlay-opacity setting keeps working: the same alpha that was
            ' applied to the constant is applied to the sampled background.
            sampledFill = New SolidBrush(Color.FromArgb(alpha, Color.FromArgb(block.PlateBackgroundArgb)))
            sampledText = New SolidBrush(Color.FromArgb(block.PlateInkArgb))
            fill = sampledFill
            pen = sampledText
        End If
#End If
        Try
            g.FillRectangle(fill, plate)
            Using fnt As New Font("Segoe UI", CSng(chosen), FontStyle.Regular, GraphicsUnit.Pixel)
                g.DrawString(text, fnt, pen, New RectangleF(plate.X + 1, plate.Y, textWidth, finalHeight), sf)
            End Using
        Finally
            If sampledFill IsNot Nothing Then sampledFill.Dispose()
            If sampledText IsNot Nothing Then sampledText.Dispose()
        End Try

        block.RenderFontPx = CSng(chosen)
        block.Truncated = truncated
        Return plate
    End Function

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

End Class
