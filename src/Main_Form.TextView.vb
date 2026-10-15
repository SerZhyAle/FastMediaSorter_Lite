#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Drawing
Imports System.Collections.Generic
Imports System.IO
Imports System.Windows.Forms

Partial Public Class Main_Form

    Private text_Current_Result As TextReadResult
    Private text_Scroll_Offset As Integer = 0
    Private text_Font_Scale As Single = 1.0F
    Private text_Current_File_Path As String = ""
    Private text_Total_Lines_Cached As Integer = 0
    Private text_Visible_Lines_Cached As Integer = 1
    ''' <summary>The screen rows of the wrapped document, each knowing which line of the file it is part of and
    ''' where in it - what makes a selection a position in the TEXT and not on a row that the next resize
    ''' re-cuts (124 UX-216).</summary>
    Private ReadOnly text_Rows As New List(Of TextRow)()
    Private text_Layout_Width As Integer = -1
    Private text_Layout_Font_Size As Single = -1

    ' What the last paint measured, kept so a mouse position can be turned back into a place in the text with the
    ' same font, margins and row height the user is looking at.
    Private text_Paint_Font_Family As String = "Segoe UI"
    Private text_Paint_Font_Size As Single = 11.0F
    Private text_Paint_Margin_X As Integer
    Private text_Paint_Margin_Y As Integer
    Private text_Paint_Row_Height As Integer = 16
    Private text_Paint_Right_To_Left As Boolean

    ' The selection (124 UX-216). Nothing selected is Nothing, not an empty selection.
    Private text_Selection As TextSelection
    ''' <summary>A left press on the document that is neither a click nor a drag yet. A click is what the media
    ''' surface always meant (the next file, after the double-click wait); a drag is a selection, and must never
    ''' flip the file under the pointer - which is why the click is decided at the button's release.</summary>
    Private text_Press As MouseEventArgs
    Private text_Selecting As Boolean

    Private Function TryHandleDocumentKey(e As KeyEventArgs) As Boolean
        If Not IsTextDocumentShown() AndAlso Not IsPdfDocumentShown() Then Return False
        Select Case e.KeyCode
            Case Keys.PageUp
                If IsTextDocumentShown() Then ScrollTextPages(-1) Else PdfPrevPage()
            Case Keys.PageDown
                If IsTextDocumentShown() Then ScrollTextPages(1) Else PdfNextPage()
            Case Keys.Home
                If IsTextDocumentShown() Then ScrollTextToTop() Else PdfFirstPage()
            Case Keys.End
                If IsTextDocumentShown() Then ScrollTextToBottom() Else PdfLastPage()
            Case Else
                Return False
        End Select
        Return True
    End Function

    Friend Function IsTextDocumentShown() As Boolean
        Return Not String.IsNullOrEmpty(text_Current_File_Path) AndAlso
               String.Equals(text_Current_File_Path, Current_File_Name, StringComparison.OrdinalIgnoreCase) AndAlso
               String.Equals(text_Current_File_Path, current_Loaded_File_Name, StringComparison.OrdinalIgnoreCase)
    End Function

    Friend Sub LoadTextDocument(filePath As String)
        ReleaseTextDocument()
        BlankActiveMediaSurface()

        current_Loaded_File_Name = filePath
        text_Current_File_Path = filePath
        text_Scroll_Offset = 0
        text_Font_Scale = 1.0F

        Dim maxKb As Integer = If(modern_Preferences IsNot Nothing, modern_Preferences.TextPreviewMaxKb, 2048)
        text_Current_Result = TextFileReader.Read(filePath, maxKb)

        If Not text_Current_Result.Success Then
            ReleaseTextDocument()
            ShowContentPlaceholder(filePath)
            lbl_Status.Text = Localization.TF("{0} - {1}", Path.GetFileName(filePath), If(text_Current_Result.IsBinary, Localization.T("Бинарный файл"), text_Current_Result.ErrorMessage))
            Return
        End If

        SetMediaDisplayKind(MediaKind.Document)
        ResetZoomViewport()
        SetViewSizes()

        If text_Current_Result.IsTruncated Then
            lbl_Status.Text = Localization.TF("{0} - показаны первые {1} из {2}",
                Path.GetFileName(filePath),
                FormatFileSize(text_Current_Result.ReadBytes),
                FormatFileSize(text_Current_Result.TotalBytes))
        Else
            lbl_Status.Text = Localization.TF("{0} - {1}",
                Path.GetFileName(filePath),
                FormatFileSize(text_Current_Result.TotalBytes))
        End If

        UpdateContentWindowTitle()
        GetActivePictureBox()?.Invalidate()
    End Sub

    Friend Sub ReleaseTextDocument()
        text_Current_File_Path = ""
        text_Scroll_Offset = 0
        text_Font_Scale = 1.0F
        text_Current_Result = Nothing
        text_Rows.Clear()
        text_Layout_Width = -1
        text_Selection = Nothing
        text_Press = Nothing
        text_Selecting = False
    End Sub

    Friend Sub PaintTextDocument(box As PictureBox, e As PaintEventArgs)
        If Not IsTextDocumentShown() OrElse box IsNot GetActivePictureBox() Then Return
        If text_Current_Result.Lines Is Nothing OrElse text_Current_Result.Lines.Length = 0 Then Return

        Dim g As Graphics = e.Graphics
        Dim bounds As Rectangle = box.ClientRectangle
        g.Clear(NeutralBackColor())

        Dim scale As Single = GetCurrentDpiScale() * text_Font_Scale
        Dim baseFontSize As Single = If(text_Current_Result.IsMonospace, 10.0F, 11.0F)
        Dim fontSize As Single = Math.Max(6.0F, baseFontSize * scale)
        Dim fontFamily As String = If(text_Current_Result.IsMonospace, "Consolas", "Segoe UI")

        Dim textColor As Color = GetOppositeColor(NeutralBackColor())
        Dim footerColor As Color = Color.FromArgb(160, textColor)

        Dim marginX As Integer = CInt(Math.Max(12, 24 * scale))
        Dim marginY As Integer = CInt(Math.Max(12, 20 * scale))
        Dim contentWidth As Integer = Math.Max(100, bounds.Width - marginX * 2)

        Using font As New Font(fontFamily, fontSize, FontStyle.Regular),
              footerFont As New Font(ScriptFonts.Family("Segoe UI"), Math.Max(8.0F, 9.5F * scale), FontStyle.Italic)

            Dim lineHeight As Integer = Math.Max(14, CInt(font.GetHeight(g) * 1.15F))
            Dim footerHeight As Integer = If(text_Current_Result.IsTruncated, CInt(footerFont.GetHeight(g) * 1.5F) + 8, 0)
            Dim availableHeight As Integer = bounds.Height - marginY * 2 - footerHeight
            Dim visibleLinesCount As Integer = Math.Max(1, availableHeight \ lineHeight)
            text_Visible_Lines_Cached = visibleLinesCount

            If text_Layout_Width <> contentWidth OrElse text_Layout_Font_Size <> fontSize Then
                text_Rows.Clear()
                Dim measureFlags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
                Dim logicalIndex As Integer = 0
                For Each logicalLine As String In text_Current_Result.Lines
                    If logicalLine.Length = 0 Then
                        text_Rows.Add(New TextRow(logicalIndex, 0, ""))
                        logicalIndex += 1
                        Continue For
                    End If
                    Dim offset As Integer = 0
                    While offset < logicalLine.Length
                        ' Bound each measurement even for a multi-megabyte line.
                        Dim low As Integer = 1
                        Dim high As Integer = Math.Min(2048, logicalLine.Length - offset)
                        While low < high
                            Dim mid As Integer = (low + high + 1) \ 2
                            Dim width As Integer = TextRenderer.MeasureText(g, logicalLine.Substring(offset, mid), font,
                                New Size(Integer.MaxValue, lineHeight), measureFlags).Width
                            If width <= contentWidth Then
                                low = mid
                            Else
                                high = mid - 1
                            End If
                        End While
                        Dim count As Integer = low
                        If offset + count < logicalLine.Length Then
                            Dim space As Integer = logicalLine.LastIndexOf(" "c, offset + count - 1, count)
                            If space > offset Then count = space - offset + 1
                            If count > 1 AndAlso Char.IsHighSurrogate(logicalLine(offset + count - 1)) Then count -= 1
                        End If
                        text_Rows.Add(New TextRow(logicalIndex, offset, logicalLine.Substring(offset, count)))
                        offset += count
                    End While
                    logicalIndex += 1
                Next
                text_Layout_Width = contentWidth
                text_Layout_Font_Size = fontSize
            End If
            text_Total_Lines_Cached = text_Rows.Count
            text_Scroll_Offset = Math.Max(0, Math.Min(text_Scroll_Offset, Math.Max(0, text_Total_Lines_Cached - visibleLinesCount)))
            Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
            Dim rightToLeft As Boolean = Localization.IsRightToLeft()
            If rightToLeft Then flags = flags Or TextFormatFlags.RightToLeft

            text_Paint_Font_Family = fontFamily
            text_Paint_Font_Size = fontSize
            text_Paint_Margin_X = marginX
            text_Paint_Margin_Y = marginY
            text_Paint_Row_Height = lineHeight
            text_Paint_Right_To_Left = rightToLeft

            Dim measureWidth As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
            For row As Integer = 0 To visibleLinesCount - 1
                Dim index As Integer = text_Scroll_Offset + row
                If index >= text_Rows.Count Then Exit For
                Dim rowTop As Integer = marginY + row * lineHeight
                Dim rowData As TextRow = text_Rows(index)

                ' Under the text, so the glyphs stay in the ink the theme chose and the band is only a tint.
                Dim span As TextRowSpan = TextViewRules.SpanOnRow(text_Selection, rowData)
                If span.HasSpan Then
                    Dim left As Integer = marginX
                    Dim right As Integer = marginX
                    If span.IsBlankLine Then
                        right = marginX + Math.Max(4, lineHeight \ 3)
                    Else
                        If span.FromColumn > 0 Then
                            left = marginX + TextRenderer.MeasureText(g, rowData.Text.Substring(0, span.FromColumn), font,
                                New Size(Integer.MaxValue, lineHeight), measureWidth).Width
                        End If
                        right = marginX + TextRenderer.MeasureText(g, rowData.Text.Substring(0, span.ToColumn), font,
                            New Size(Integer.MaxValue, lineHeight), measureWidth).Width
                    End If
                    If right > left Then
                        Using band As New SolidBrush(SelectionBandColor())
                            g.FillRectangle(band, left, rowTop, right - left, lineHeight)
                        End Using
                    End If
                End If

                TextRenderer.DrawText(g, rowData.Text, font,
                    New Rectangle(marginX, rowTop, contentWidth, lineHeight), textColor, flags)
            Next

            PaintTextScrollCue(g, bounds, marginY, lineHeight, visibleLinesCount, scale, textColor)

            ' Draw footer if truncated
            If text_Current_Result.IsTruncated Then
                ' The key is the one the open action answers to now (123 UX-112), never a hard-coded Enter.
                Dim openKey As String = OpenContentKeyLabel()
                Dim footerText As String = If(openKey.Length = 0,
                    Localization.TF("Показаны первые {0} из {1}.",
                        FormatFileSize(text_Current_Result.ReadBytes),
                        FormatFileSize(text_Current_Result.TotalBytes)),
                    Localization.TF("Показаны первые {0} из {1}. {2} - открыть файл целиком.",
                        FormatFileSize(text_Current_Result.ReadBytes),
                        FormatFileSize(text_Current_Result.TotalBytes),
                        openKey))
                Dim footerRect As New Rectangle(marginX, bounds.Height - footerHeight - marginY \ 2, contentWidth, footerHeight)
                Dim footerFlags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix
                TextRenderer.DrawText(g, footerText, footerFont, footerRect, footerColor, footerFlags)
            End If
        End Using
    End Sub

    ''' <summary>The tint under selected text: the accent the chrome uses, translucent, so the ink the theme picked
    ''' for the text reads on it in a light surface and a dark one alike.</summary>
    Private Shared Function SelectionBandColor() As Color
        Return Color.FromArgb(96, AccentColors.ForChrome(SystemColors.Highlight))
    End Function

    ''' <summary>
    ''' The scroll-position cue (124 UX-216): a thin track along the edge of the text area with a thumb for the
    ''' part in view. Passive - it answers "how far into the file am I" and takes no input, so a click beside it
    ''' is still the media's click. Absent when the whole file fits. On the leading edge in a right-to-left
    ''' language, where a scroll bar is.
    ''' </summary>
    Private Sub PaintTextScrollCue(g As Graphics, bounds As Rectangle, marginY As Integer, rowHeight As Integer,
                                   visibleRows As Integer, scale As Single, ink As Color)
        Dim trackLength As Integer = visibleRows * rowHeight
        Dim thumb As TextScrollThumb = TextViewRules.ScrollThumb(text_Total_Lines_Cached, visibleRows, text_Scroll_Offset,
                                                                 trackLength, Math.Max(12, CInt(24 * scale)))
        If Not thumb.Visible Then Return
        Dim barWidth As Integer = Math.Max(3, CInt(4 * scale))
        Dim inset As Integer = Math.Max(3, CInt(5 * scale))
        Dim x As Integer = If(text_Paint_Right_To_Left, inset, bounds.Width - inset - barWidth)
        Using trackBrush As New SolidBrush(Color.FromArgb(26, ink)),
              thumbBrush As New SolidBrush(Color.FromArgb(120, ink))
            g.FillRectangle(trackBrush, x, marginY, barWidth, trackLength)
            g.FillRectangle(thumbBrush, x, marginY + thumb.Top, barWidth, thumb.Length)
        End Using
    End Sub

    ' --- scrolling -------------------------------------------------------------------------------

    Friend Sub ScrollTextLines(deltaLines As Integer)
        If Not IsTextDocumentShown() Then Return
        text_Scroll_Offset += deltaLines
        If text_Scroll_Offset < 0 Then text_Scroll_Offset = 0
        Dim maxScroll As Integer = Math.Max(0, text_Total_Lines_Cached - text_Visible_Lines_Cached)
        If text_Scroll_Offset > maxScroll Then text_Scroll_Offset = maxScroll
        GetActivePictureBox()?.Invalidate()
    End Sub

    Friend Sub ScrollTextPages(deltaPages As Integer)
        If Not IsTextDocumentShown() Then Return
        Dim pageSize As Integer = Math.Max(1, text_Visible_Lines_Cached - 2)
        ScrollTextLines(deltaPages * pageSize)
    End Sub

    Friend Sub ScrollTextToTop()
        If Not IsTextDocumentShown() Then Return
        text_Scroll_Offset = 0
        GetActivePictureBox()?.Invalidate()
    End Sub

    Friend Sub ScrollTextToBottom()
        If Not IsTextDocumentShown() Then Return
        text_Scroll_Offset = Math.Max(0, text_Total_Lines_Cached - text_Visible_Lines_Cached)
        GetActivePictureBox()?.Invalidate()
    End Sub

    Friend Sub ZoomText(delta As Single)
        If Not IsTextDocumentShown() Then Return
        Dim newScale As Single = text_Font_Scale + delta
        If newScale < 0.5F Then newScale = 0.5F
        If newScale > 3.0F Then newScale = 3.0F
        text_Font_Scale = newScale
        GetActivePictureBox()?.Invalidate()
    End Sub

    Friend Sub ResetTextZoom()
        If Not IsTextDocumentShown() Then Return
        text_Font_Scale = 1.0F
        GetActivePictureBox()?.Invalidate()
    End Sub

    ' --- selection and copy (124 UX-216) ---------------------------------------------------------

    ''' <summary>Is anything selected in the document on screen?</summary>
    Friend Function HasTextSelection() As Boolean
        Return IsTextDocumentShown() AndAlso text_Selection IsNot Nothing AndAlso Not text_Selection.IsEmpty
    End Function

    ''' <summary>Esc's rung: the selection goes, the file stays.</summary>
    Friend Sub ClearTextSelection()
        If text_Selection Is Nothing AndAlso Not text_Selecting Then Return
        text_Selection = Nothing
        text_Selecting = False
        text_Press = Nothing
        GetActivePictureBox()?.Invalidate()
    End Sub

    ''' <summary>
    ''' Ctrl+A and Ctrl+C on a text document - the two keys a person tries first. Asked BEFORE the historical map's
    ''' Ctrl/Alt gate (that gate hands every chord to Windows), and only while a text document is up, so a chord
    ''' that means something else elsewhere is not taken from anyone. Ctrl+C with nothing selected copies nothing
    ''' and says how to select: copying a whole file nobody pointed at would put it over what is on the clipboard.
    ''' </summary>
    Private Function TryHandleTextSelectionKey(e As KeyEventArgs) As Boolean
        If Not e.Control OrElse e.Alt OrElse e.Shift Then Return False
        If e.KeyCode <> Keys.A AndAlso e.KeyCode <> Keys.C Then Return False
        If Not IsTextDocumentShown() OrElse text_Current_Result.Lines Is Nothing Then Return False

        If e.KeyCode = Keys.A Then
            text_Selection = TextViewRules.SelectAll(text_Current_Result.Lines)
            GetActivePictureBox()?.Invalidate()
            SayTextSelection()
            Return True
        End If

        If Not HasTextSelection() Then
            Notify(Localization.T("Нечего копировать - выделите текст мышью или нажмите Ctrl+A"), NotifyLevel.Info)
            Return True
        End If
        Dim text As String = TextViewRules.Extract(text_Current_Result.Lines, text_Selection)
        Utils.CopyTextToClipboard(text, lbl_Status, Localization.TF("Скопировано символов: {0}", text.Length.ToString()))
        Return True
    End Function

    Private Sub SayTextSelection()
        If Not HasTextSelection() Then Return
        Dim count As Long = TextViewRules.CharacterCount(text_Current_Result.Lines, text_Selection)
        Notify(Localization.TF("Выделено символов: {0}", count.ToString()), NotifyLevel.Info)
    End Sub

    ''' <summary>
    ''' A left press on the document. Returns True when it is taken: the click it may turn out to be is then decided
    ''' at the release (<see cref="TextSelectionMouseUp"/>), and a drag becomes a selection instead.
    ''' </summary>
    Friend Function TextSelectionMouseDown(e As MouseEventArgs) As Boolean
        If e.Button <> MouseButtons.Left OrElse Not IsTextDocumentShown() Then Return False
        If text_Current_Result.Lines Is Nothing OrElse text_Current_Result.Lines.Length = 0 Then Return False
        text_Press = e
        text_Selecting = False
        Return True
    End Function

    ''' <summary>The pointer moved with the left button down. True while the document owns the gesture.</summary>
    Friend Function TextSelectionMouseMove(box As PictureBox, e As MouseEventArgs) As Boolean
        If box Is Nothing OrElse (text_Press Is Nothing AndAlso Not text_Selecting) Then Return False
        If Not IsTextDocumentShown() Then
            text_Press = Nothing
            text_Selecting = False
            Return False
        End If

        If Not text_Selecting Then
            Dim dx As Integer = e.X - text_Press.X
            Dim dy As Integer = e.Y - text_Press.Y
            Dim threshold As Integer = Math.Max(SystemInformation.DragSize.Width, 4) \ 2
            If dx * dx + dy * dy < threshold * threshold Then Return True
            Dim anchor As TextPosition = TextPositionAt(box, text_Press.Location)
            text_Selection = New TextSelection(anchor, anchor)
            text_Selecting = True
            text_Press = Nothing
        End If

        ' A drag past the first or the last visible row brings more of the text into view, one row per move.
        Dim bottomOfText As Integer = text_Paint_Margin_Y + text_Visible_Lines_Cached * text_Paint_Row_Height
        If e.Y < text_Paint_Margin_Y Then
            ScrollTextLines(-1)
        ElseIf e.Y >= bottomOfText Then
            ScrollTextLines(1)
        End If

        text_Selection = text_Selection.WithFocus(TextPositionAt(box, e.Location))
        box.Invalidate()
        Return True
    End Function

    ''' <summary>The button was released: a drag ends with its selection kept; anything else was a click, and does
    ''' what a left click on the media always did - the delayed "next file", so a double click still wins.</summary>
    Friend Sub TextSelectionMouseUp(e As MouseEventArgs)
        If text_Selecting Then
            text_Selecting = False
            If text_Selection IsNot Nothing AndAlso text_Selection.IsEmpty Then text_Selection = Nothing
            SayTextSelection()
            Return
        End If
        If text_Press Is Nothing Then Return
        Dim press As MouseEventArgs = text_Press
        text_Press = Nothing
        pending_Single_Click_Timer.Stop()
        pending_Single_Click_Event = press
        pending_Single_Click_Timer.Start()
    End Sub

    ''' <summary>Where in the text a point of the surface is: the row under it, then the character boundary nearest
    ''' to it along that row - measured with the font the row was drawn in.</summary>
    Private Function TextPositionAt(box As PictureBox, point As Point) As TextPosition
        If text_Rows.Count = 0 Then Return New TextPosition(0, 0)
        Dim rowIndex As Integer = TextViewRules.RowAt(point.Y, text_Paint_Margin_Y, text_Paint_Row_Height, text_Scroll_Offset, text_Rows.Count)
        Dim row As TextRow = text_Rows(rowIndex)
        Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
        Dim column As Integer
        Using font As New Font(text_Paint_Font_Family, text_Paint_Font_Size, FontStyle.Regular),
              g As Graphics = box.CreateGraphics()
            Dim rowHeight As Integer = text_Paint_Row_Height
            column = TextViewRules.ColumnAtX(row.Text, point.X - text_Paint_Margin_X,
                Function(count As Integer)
                    If count <= 0 Then Return 0
                    Return TextRenderer.MeasureText(g, row.Text.Substring(0, count), font, New Size(Integer.MaxValue, rowHeight), flags).Width
                End Function)
        End Using
        Return TextViewRules.Clamp(text_Current_Result.Lines, New TextPosition(row.Line, row.Start + column))
    End Function

    Private Function FormatFileSize(bytes As Long) As String
        ' Units in the interface language, each with its number in ONE sentence (124 UX-216/UX-217); the same
        ' binary units the loading badge uses, so two surfaces never print one size two ways.
        If bytes < 1024L Then
            Return Localization.TF("{0} Б", bytes.ToString())
        ElseIf bytes < 1048576L Then
            Return Localization.TF("{0} КиБ", (bytes / 1024.0R).ToString("0.#"))
        ElseIf bytes < 1073741824L Then
            Return Localization.TF("{0} МиБ", (bytes / 1048576.0R).ToString("0.#"))
        Else
            Return Localization.TF("{0} ГиБ", (bytes / 1073741824.0R).ToString("0.##"))
        End If
    End Function

    Private Function GetCurrentDpiScale() As Single
        Dim scale As Single = Math.Max(1.0F, DeviceDpi / 96.0F)
        If modern_Preferences IsNot Nothing AndAlso (modern_Preferences.InterfaceScalePercent > 0 OrElse modern_Preferences.InterfaceScaleRelativePercent > 0) Then
            scale = modern_Preferences.InterfaceFactor(DeviceDpi)
        End If
        Return Math.Max(0.5F, scale)
    End Function

End Class
#End If
