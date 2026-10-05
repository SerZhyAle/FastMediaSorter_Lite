Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text
Imports System.Windows.Forms

' OCR + Translation overlay rendering.
'
' The overlay is NEVER baked into the bitmap; it is painted in the PictureBox Paint
' handlers, on top of the image. The displayed-image rectangle comes from one place:
' DisplayedImageRectangleOnPanel - the SizeMode = Zoom letterbox at Fit, and the
' viewport bounds (spec 012 §5) while zoomed - so the overlay re-derives its geometry
' from that rectangle on every paint and therefore stays aligned through fullscreen,
' super-fullscreen, zoom, drag and resize, with no separate pan model.
' The handlers hang on HqPictureBox.MediaPaint (raised at the end of OnPaint on every
' path, viewport or base) rather than Control.Paint, which the viewport path cannot
' raise because its OnPaint skips the base drawing.
'
' THE OVERFLOW RULE (OCR-OVERLAY rule 9, which requires this product to write one down
' rather than decide it per case).
'
' The ladder is: shrink the font to its floor, grow the plate downwards within a budget
' that stops at the next plate in the same column, then RELEASE the plate - it grows past
' that budget, taking the height the text needs, bounded only by the bottom edge of the
' displayed image. There is no ellipsis and no crop at any rung, and the top edge never
' moves.
'
' A released plate may therefore overlap the plate below it, and what happens then is this
' rule: plates are painted in READING ORDER, top to bottom, so where two overlap the LOWER
' one is painted last and wins. Three reasons, in order of weight:
'   - The lower block is the innocent party. It fits its own box; the released one is the
'     block whose translation came out longer than the text it replaces. Losing part of the
'     overflow is a cost paid by the block that caused it.
'   - Nothing is deleted. Every character of the released block is rendered, inside a plate
'     that is as tall as the text needs; only the part that a neighbour's opaque plate
'     covers is hidden. An ellipsis, by contrast, removed characters from the string.
'   - It needs no second pass and no z-order model. Blocks arrive from OcrBlockBuilder in
'     reading order already, so the loop below IS the rule.
Partial Public Class Main_Form

    Private Sub Picture_Box_1_Paint(sender As Object, e As PaintEventArgs) Handles Picture_Box_1.MediaPaint
#If Not NETFRAMEWORK Then
        PaintAudioSurface(e.Graphics, Picture_Box_1.ClientRectangle)
        PaintContentPlaceholder(Picture_Box_1, e)
        PaintTextDocument(Picture_Box_1, e)
        PaintPdfOverlay(Picture_Box_1, e)
#End If
        PaintOcrOverlay(Picture_Box_1, e)
        PaintInfoOverlay(Picture_Box_1, e)
    End Sub

    Private Sub Picture_Box_2_Paint(sender As Object, e As PaintEventArgs) Handles Picture_Box_2.MediaPaint
#If Not NETFRAMEWORK Then
        PaintContentPlaceholder(Picture_Box_2, e)
        PaintTextDocument(Picture_Box_2, e)
        PaintPdfOverlay(Picture_Box_2, e)
#End If
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

        Dim shown_Name As String = IO.Path.GetFileName(Current_File_Name)
#If Not NETFRAMEWORK Then
        ' An open container shows the sealed TRUE name here, while SecureShowTrueName is on
        ' - that setting turns it off on every surface at once, which is what somebody
        ' sharing a screen wants (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §9,
        ' O-1). Empty for every ordinary file, so this is one line rather than a branch.
        Dim sealed_Name As String = SecureDisplayName(Current_File_Name)
        If sealed_Name.Length > 0 Then shown_Name = sealed_Name
#End If

        Dim caption As String = shown_Name & "    " &
                                (current_File_Index + 1).ToString() & "/" & total_File_Count.ToString()

        Dim g As Graphics = e.Graphics
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit

#If Not NETFRAMEWORK Then
        ' The HUD is UI: the script face of the interface language, and right to left in Arabic and Urdu - the card
        ' sits in the corner the language starts from and the text runs the way the language does (124 UX-216).
        Dim hud_Family As String = ScriptFonts.Family("Segoe UI")
        Dim hud_Rtl As Boolean = Localization.IsRightToLeft()
#Else
        Dim hud_Family As String = "Segoe UI"
        Dim hud_Rtl As Boolean = False
#End If
        Using fnt As New Font(hud_Family, 11.0F, FontStyle.Regular)
            Dim sz As SizeF = g.MeasureString(caption, fnt)
            Dim padX As Integer = 8
            Dim padY As Integer = 4
            Dim box As New Rectangle(8, 8, CInt(Math.Ceiling(sz.Width)) + padX * 2, CInt(Math.Ceiling(sz.Height)) + padY * 2)
            If hud_Rtl Then box.X = Math.Max(0, pb.ClientSize.Width - 8 - box.Width)
            Using bg As New SolidBrush(Color.FromArgb(160, 0, 0, 0))
                g.FillRectangle(bg, box)
            End Using
            Using tb As New SolidBrush(Color.White)
                If hud_Rtl Then
                    Using fmt As New StringFormat(StringFormatFlags.DirectionRightToLeft)
                        g.DrawString(caption, fnt, tb, New RectangleF(box.X + padX, box.Y + padY, box.Width - padX * 2, box.Height - padY * 2), fmt)
                    End Using
                Else
                    g.DrawString(caption, fnt, tb, box.X + padX, box.Y + padY)
                End If
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
        ' Last line of defence for OCR-OVERLAY rule 4: plates measured on another pixel grid
        ' (the picture was turned, or re-decoded at another size) are not drawn at all rather
        ' than drawn in the wrong place (audit 051 E-01, fix d).
        If doc.ImageSize.Width <> img.Width OrElse doc.ImageSize.Height <> img.Height Then Return

        Dim fit As Rectangle
#If Not NETFRAMEWORK Then
        ' The rectangle the image is really painted into: the viewport bounds while
        ' zoomed, the SizeMode = Zoom letterbox at Fit - the plates ride the same
        ' image->screen transform as the picture (spec 012 §8).
        fit = DisplayedImageRectangleOnPanel(img)
#Else
        fit = GetZoomedImageRectangle(doc.ImageSize.Width, doc.ImageSize.Height, pb.ClientSize.Width, pb.ClientSize.Height)
#End If
        If fit.IsEmpty Then Return
        Dim scale As Double = fit.Width / CDbl(doc.ImageSize.Width)

        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit

        ' OCR-OVERLAY rule 7 (ticket 033 Ф-4, owner decision O-1, 2026-09-25): the plate is
        ' OPAQUE by default - OcrTranslateSettings.OverlayOpacity defaults to 255 - and a lower
        ' alpha exists only as the user's explicit choice on the Settings slider (30-100 %),
        ' recorded as a dated exception in the catalog registry. An install that saved the old
        ' default of 210 keeps it: a stored value is never rewritten behind the user's back.
        ' Padding (O-2): none - the plate is the mapped source box and the text sits 1 px in.
        ' Recorded, not measured yet; the local report's §14.7 says padding moves the
        ' concealment metric, so a change waits for a before/after run of the scene harness.
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
            ' No trimming, ever: OCR-OVERLAY rule 9 permits neither an ellipsis nor a crop.
            ' The last rung of the ladder releases the plate instead (see the overflow rule
            ' at the top of this file), so there is nothing left for trimming to do and
            ' leaving it on would silently re-introduce the behaviour the rule forbids.
            sf.Trimming = StringTrimming.None
            ' Paragraph direction is set per block in DrawOverlayBlock (audit 051 E-11).
            sf.FormatFlags = CType(0, StringFormatFlags)

            Using fillBrush As New SolidBrush(fillColor)
                Using textBrush As New SolidBrush(Color.Black)
                    For i As Integer = 0 To doc.Blocks.Count - 1
                        Dim block As OcrBlock = doc.Blocks(i)
                        Dim r As Rectangle = plates(i)
                        If r.Width <= 0 Then Continue For

                        Dim plate As Rectangle = DrawOverlayBlock(g, block, r, plates, fit, scale, sf, fillBrush, textBrush, alpha, doc.TargetLanguage)

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
        ' the renderer settled on. Once per document, not once per repaint. Never for a
        ' secure container's picture (audit 052 E-12): its text is the text of a secret.
        OcrDiagnostics.DumpDocument(doc, mayPersist:=SecureAllowsDiskCache())
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
    ''' The ladder: shrink to the floor, then grow downwards within the budget, then RELEASE -
    ''' grow past the budget to whatever the text needs, bounded by the bottom of the
    ''' displayed image. The top edge does not move on any rung, and nothing is ever trimmed;
    ''' the overflow rule at the top of this file says what a released plate does to the plate
    ''' below it.
    ''' </summary>
    Private Shared Function DrawOverlayBlock(g As Graphics, block As OcrBlock, r As Rectangle, plates() As Rectangle,
                                             fit As Rectangle, scale As Double, sf As StringFormat,
                                             fillBrush As Brush, textBrush As Brush, alpha As Integer,
                                             targetLanguage As String) As Rectangle
        Dim text As String = OverlayTextOf(block)

        ' The paragraph direction follows the text actually drawn - the translation, or the
        ' source where a partial document kept it - never the interface: an Arabic or Hebrew
        ' plate laid out left to right puts "!" and digits on the wrong end (audit 051 E-11).
        ' Set before measuring, because direction changes where lines break. Painting only:
        ' the document is unchanged, so no pipeline bump.
        sf.FormatFlags = If(TextScriptHeuristics.IsRightToLeftText(text, targetLanguage),
                            StringFormatFlags.DirectionRightToLeft, CType(0, StringFormatFlags))

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
        Dim released As Boolean = True

        For Each candidate As Double In candidates
            Using probe As New Font("Segoe UI", CSng(candidate), FontStyle.Regular, GraphicsUnit.Pixel)
                Dim measured As SizeF = g.MeasureString(text, probe, textWidth, sf)
                If measured.Width <= textWidth + 1 AndAlso measured.Height <= maxHeight Then
                    chosen = candidate
                    neededHeight = measured.Height
                    released = False
                    Exit For
                End If
            End Using
        Next

        Dim finalHeight As Integer
        If released Then
            ' Last rung. The font is already at its floor and the budget is spent, so measure
            ' what the text REALLY needs at that font - MeasureString bounded only by the
            ' width - and let the plate have it. This is where an ellipsis used to be.
            Using probe As New Font("Segoe UI", CSng(chosen), FontStyle.Regular, GraphicsUnit.Pixel)
                neededHeight = g.MeasureString(text, probe, textWidth, sf).Height
            End Using
            finalHeight = OcrOverlayFit.ReleasedHeight(r, neededHeight, fit.Bottom)
        Else
            ' Grow only as far as this text needs, never to the whole budget.
            finalHeight = Math.Min(maxHeight, Math.Max(r.Height, CInt(Math.Ceiling(neededHeight))))
        End If
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
        block.Released = released
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
