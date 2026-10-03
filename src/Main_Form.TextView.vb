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
    Private text_Wrapped_Lines As New List(Of String)()
    Private text_Layout_Width As Integer = -1
    Private text_Layout_Font_Size As Single = -1

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
            lbl_Status.Text = Localization.TF("{0} — {1}", Path.GetFileName(filePath), If(text_Current_Result.IsBinary, Localization.T("Бинарный файл"), text_Current_Result.ErrorMessage))
            Return
        End If

        SetMediaDisplayKind(MediaKind.Document)
        zoom_Scale = 1
        SetViewSizes()

        If text_Current_Result.IsTruncated Then
            lbl_Status.Text = Localization.TF("{0} — показаны первые {1} из {2}",
                Path.GetFileName(filePath),
                FormatFileSize(text_Current_Result.ReadBytes),
                FormatFileSize(text_Current_Result.TotalBytes))
        Else
            lbl_Status.Text = Localization.TF("{0} — {1}",
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
        text_Wrapped_Lines.Clear()
        text_Layout_Width = -1
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
              footerFont As New Font("Segoe UI", Math.Max(8.0F, 9.5F * scale), FontStyle.Italic)

            Dim lineHeight As Integer = Math.Max(14, CInt(font.GetHeight(g) * 1.15F))
            Dim footerHeight As Integer = If(text_Current_Result.IsTruncated, CInt(footerFont.GetHeight(g) * 1.5F) + 8, 0)
            Dim availableHeight As Integer = bounds.Height - marginY * 2 - footerHeight
            Dim visibleLinesCount As Integer = Math.Max(1, availableHeight \ lineHeight)
            text_Visible_Lines_Cached = visibleLinesCount

            If text_Layout_Width <> contentWidth OrElse text_Layout_Font_Size <> fontSize Then
                text_Wrapped_Lines.Clear()
                Dim measureFlags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
                For Each logicalLine As String In text_Current_Result.Lines
                    If logicalLine.Length = 0 Then
                        text_Wrapped_Lines.Add("")
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
                        text_Wrapped_Lines.Add(logicalLine.Substring(offset, count))
                        offset += count
                    End While
                Next
                text_Layout_Width = contentWidth
                text_Layout_Font_Size = fontSize
            End If
            text_Total_Lines_Cached = text_Wrapped_Lines.Count
            text_Scroll_Offset = Math.Max(0, Math.Min(text_Scroll_Offset, Math.Max(0, text_Total_Lines_Cached - visibleLinesCount)))
            Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
            If Localization.IsRightToLeft() Then flags = flags Or TextFormatFlags.RightToLeft
            For row As Integer = 0 To visibleLinesCount - 1
                Dim index As Integer = text_Scroll_Offset + row
                If index >= text_Wrapped_Lines.Count Then Exit For
                TextRenderer.DrawText(g, text_Wrapped_Lines(index), font,
                    New Rectangle(marginX, marginY + row * lineHeight, contentWidth, lineHeight), textColor, flags)
            Next

            ' Draw footer if truncated
            If text_Current_Result.IsTruncated Then
                Dim footerText As String = Localization.TF("Показаны первые {0} из {1}. Enter — открыть файл целиком.",
                    FormatFileSize(text_Current_Result.ReadBytes),
                    FormatFileSize(text_Current_Result.TotalBytes))
                Dim footerRect As New Rectangle(marginX, bounds.Height - footerHeight - marginY \ 2, contentWidth, footerHeight)
                Dim footerFlags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix
                TextRenderer.DrawText(g, footerText, footerFont, footerRect, footerColor, footerFlags)
            End If
        End Using
    End Sub

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

    Private Function FormatFileSize(bytes As Long) As String
        If bytes < 1024L Then
            Return bytes.ToString() & " B"
        ElseIf bytes < 1048576L Then
            Return (bytes / 1024.0R).ToString("0.#") & " KB"
        ElseIf bytes < 1073741824L Then
            Return (bytes / 1048576.0R).ToString("0.#") & " MB"
        Else
            Return (bytes / 1073741824.0R).ToString("0.##") & " GB"
        End If
    End Function

    Private Function GetCurrentDpiScale() As Single
        Dim scale As Single = Math.Max(1.0F, DeviceDpi / 96.0F)
        If modern_Preferences IsNot Nothing AndAlso modern_Preferences.InterfaceScalePercent > 0 Then
            scale = modern_Preferences.InterfaceScalePercent / 100.0F
        End If
        Return Math.Max(0.5F, scale)
    End Function

End Class
#End If
