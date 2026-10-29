#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

' Specification 277 step P3.3: the full text reader behind FullTextReaderEnabled. It reuses the
' preview's rows, selection, hit test and scroll state of Main_Form.TextView.vb and replaces only
' what the preview cannot do: the detected encoding, windows of the pager, the reader font and
' palette, the line-number gutter, wrap off with a horizontal offset, whitespace markers, the
' search (in the window at once, and for a paged file over every window in the background) and the
' status line. With the opt-in off nothing here runs.
Partial Public Class Main_Form

    ''' <summary>The document on screen was opened by the full reader (decided once, at the load).</summary>
    Private text_Full_Reader As Boolean
    Private text_Pager As TextFilePager
    Private text_Detection As EncodingDetectionResult
    ''' <summary>The file is larger than the editing ceiling: read in windows, never editable.</summary>
    Private text_Virtual As Boolean
    Private text_Display_Lines As String()
    Private text_Line_Ending As LineEndingKind = LineEndingKind.None
    ''' <summary>Line number (0-based) of the first line of each window whose place is known: window 0, and
    ''' every window reached from a known neighbour. A window jumped to (Ctrl+End) has no number.</summary>
    Private ReadOnly text_Window_First_Line As New Dictionary(Of Integer, Long)()
    Private ReadOnly text_Window_Terminators As New Dictionary(Of Integer, Integer)()
    Private text_Horizontal_Offset As Integer
    Private text_Layout_Key As String = ""
    Private text_Paint_Text_Left As Integer
    Private text_Search_Query As String = ""
    Private text_Search_Matches As New List(Of TextMatch)()
    Private text_Search_Index As Integer = -1
    ''' <summary>The hits of the finished full-file search of a paged file (section 5.1 item 4), in reading order;
    ''' Nothing while there is none, and then the search covers the window on screen only and says so.</summary>
    Private text_File_Hits As List(Of TextFileHit)
    Private text_File_Hit_Index As Integer = -1
    ''' <summary>The full-file search stopped at <see cref="TextReaderLayout.MaxMatches"/>: the hits are the first ones.</summary>
    Private text_File_Hits_Capped As Boolean
    ''' <summary>The full-file search running on the pool; Nothing when none runs.</summary>
    Private text_Scan_Cancel As System.Threading.CancellationTokenSource
    Private text_Scan_Done As Integer
    Private text_Scan_Total As Integer
    ''' <summary>A place to bring into view at the next paint, once the rows of the current width exist.</summary>
    Private text_Pending_Reveal As TextPosition?
    ''' <summary>The window was entered from below: its end is shown once the rows exist.</summary>
    Private text_Pending_End As Boolean
    ''' <summary>Re-open with Encoding for the next load only: -1 none, 0 detect again, else the code page.</summary>
    Private text_Encoding_Request As Integer = -1
    ''' <summary>An excerpt is being written on the pool: a second Ctrl+Shift+S is ignored until it ends.</summary>
    Private text_Excerpt_Saving As Boolean

    Private Function FullTextReaderOn() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.FullTextReaderEnabled
    End Function

    Private Function FullTextReaderShown() As Boolean
        Return text_Full_Reader AndAlso text_Pager IsNot Nothing AndAlso IsTextDocumentShown()
    End Function

    ''' <summary>
    ''' Opens a file for the full reader: the encoding from the detector over the first 64 KiB, one window of
    ''' the whole file up to the editing ceiling and windows of <c>TextReadWindowMb</c> above it. A binary file
    ''' is refused, never shown as text.
    ''' </summary>
    Private Function ReadFullTextDocument(filePath As String) As TextReadResult
        Dim result As New TextReadResult()
        Dim request As Integer = text_Encoding_Request
        text_Encoding_Request = -1
        Try
            Dim info As New FileInfo(filePath)
            Dim size As Long = info.Length
            result.TotalBytes = size
            ' Section 6.1 tier 1: the choice just made, else the one remembered for this very version of the file.
            Dim overrideCodePage As Integer = Math.Max(0, request)
            If request < 0 AndAlso modern_Preferences.TextRememberEncoding Then
                overrideCodePage = TextEncodingMemory.Current.Lookup(TextDocumentIdentity(filePath, info))
            End If
            Dim probe(CInt(Math.Min(size, TextEncodingDetector.ProbeBytes)) - 1) As Byte
            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Dim filled As Integer = 0
                While filled < probe.Length
                    Dim count As Integer = stream.Read(probe, filled, probe.Length - filled)
                    If count <= 0 Then Exit While
                    filled += count
                End While
                If filled < probe.Length Then Array.Resize(probe, filled)
            End Using
            Dim detection As EncodingDetectionResult = TextEncodingDetector.Detect(probe, probe.Length, size > probe.Length,
                modern_Preferences.TextEncodingFallbackCodePage, overrideCodePage)
            If detection.IsBinary Then
                result.IsBinary = True
                Return result
            End If

            Dim ceiling As Long = CLng(modern_Preferences.TextEditingMaxMb) * 1048576L
            text_Virtual = size > ceiling
            Dim windowBytes As Integer = If(text_Virtual,
                TextFilePager.WindowBytesFromMb(modern_Preferences.TextReadWindowMb),
                CInt(Math.Min(Integer.MaxValue - 8L, size + 8L)))
            text_Detection = detection
            text_Pager = New TextFilePager(filePath, detection.Encoding, detection.BomLength, windowBytes)
            text_Window_First_Line.Clear()
            text_Window_Terminators.Clear()
            text_Window_First_Line(0) = 0L
            Dim lines As TextReaderLines = ReadTextWindowLines(0)

            result.Success = True
            result.Lines = lines.CopyLines
            result.ReadBytes = size
            result.DetectedEncoding = detection.Encoding
            result.IsMonospace = TextFileReader.IsMonospace(filePath)
        Catch ex As Exception
            text_Pager = Nothing
            text_Detection = Nothing
            result.Success = False
            result.ErrorMessage = FailureText.Describe(ex)
        End Try
        Return result
    End Function

    ''' <summary>Reads one window and remembers what numbering and the status line need about it.</summary>
    Private Function ReadTextWindowLines(index As Integer) As TextReaderLines
        Dim previous As Integer = text_Pager.CurrentWindowIndex
        Dim text As String = text_Pager.ReadWindow(index)
        index = text_Pager.CurrentWindowIndex
        Dim lines As TextReaderLines = TextReaderLayout.BuildLines(text, modern_Preferences.TextTabWidth,
            modern_Preferences.TextWhitespace, index < text_Pager.TotalWindows - 1)
        text_Window_Terminators(index) = lines.Terminators
        If Not text_Window_First_Line.ContainsKey(index) Then
            Dim known As Long
            Dim count As Integer
            If index = previous + 1 AndAlso text_Window_First_Line.TryGetValue(previous, known) AndAlso
               text_Window_Terminators.TryGetValue(previous, count) Then
                text_Window_First_Line(index) = known + count
            ElseIf index = previous - 1 AndAlso text_Window_First_Line.TryGetValue(previous, known) Then
                text_Window_First_Line(index) = known - lines.Terminators
            End If
        End If
        Dim mixed As Boolean
        text_Line_Ending = TextEncodingDetector.DetectLineEnding(text, mixed)
        text_Display_Lines = lines.DisplayLines
        Return lines
    End Function

    ''' <summary>Puts another window of a paged file on screen, at its top or at its end.</summary>
    Private Sub ShowTextWindow(index As Integer, atEnd As Boolean)
        If Not FullTextReaderShown() Then Return
        Dim lines As TextReaderLines
        Try
            lines = ReadTextWindowLines(index)
        Catch ex As Exception
            Notify(Localization.TF("Не удалось прочитать часть файла: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
            Return
        End Try
        text_Current_Result.Lines = lines.CopyLines
        text_Rows.Clear()
        text_Layout_Key = ""
        text_Selection = Nothing
        text_Press = Nothing
        text_Selecting = False
        text_Horizontal_Offset = 0
        ' Until the next paint lays the rows out, the line count stands in for the row count.
        text_Total_Lines_Cached = lines.CopyLines.Length
        text_Scroll_Offset = If(atEnd, lines.CopyLines.Length, 0)
        text_Pending_End = atEnd
        If text_File_Hits IsNot Nothing Then
            Dim first As Integer
            text_Search_Matches = TextFileSearch.WindowMatches(text_File_Hits, text_Pager.CurrentWindowIndex, first)
            text_Search_Index = -1
        ElseIf text_Search_Query.Length > 0 Then
            RunTextSearch(False)
        End If
        GetActivePictureBox()?.Invalidate()
    End Sub

    ''' <summary>A scroll past the end of a window continues into the next one, past its start into the
    ''' previous one. Never while a drag selects: a selection lives inside one window.</summary>
    Private Function TryCrossTextWindow(deltaLines As Integer) As Boolean
        If Not FullTextReaderShown() OrElse text_Selecting OrElse text_Pager.TotalWindows < 2 Then Return False
        Dim maxScroll As Integer = Math.Max(0, text_Total_Lines_Cached - text_Visible_Lines_Cached)
        Dim index As Integer = text_Pager.CurrentWindowIndex
        If deltaLines > 0 AndAlso text_Scroll_Offset >= maxScroll AndAlso index < text_Pager.TotalWindows - 1 Then
            ShowTextWindow(index + 1, False)
            Return True
        End If
        If deltaLines < 0 AndAlso text_Scroll_Offset <= 0 AndAlso index > 0 Then
            ShowTextWindow(index - 1, True)
            Return True
        End If
        Return False
    End Function

    Private Sub ReleaseFullTextReader()
        text_Full_Reader = False
        text_Pager = Nothing
        text_Detection = Nothing
        text_Virtual = False
        text_Display_Lines = Nothing
        text_Line_Ending = LineEndingKind.None
        text_Window_First_Line.Clear()
        text_Window_Terminators.Clear()
        text_Horizontal_Offset = 0
        text_Layout_Key = ""
        text_Search_Query = ""
        text_Search_Matches = New List(Of TextMatch)()
        text_Search_Index = -1
        CancelFullTextScan()
        text_File_Hits = Nothing
        text_File_Hit_Index = -1
        text_File_Hits_Capped = False
        text_Pending_Reveal = Nothing
        text_Pending_End = False
    End Sub

    ''' <summary>The status line under the file name right after the load; a paged file also says why it cannot
    ''' be edited (section 5.1 item 3).</summary>
    Private Sub AnnounceFullTextDocument(filePath As String)
        lbl_Status.Text = Localization.TF("{0} - {1}", Path.GetFileName(filePath), FormatFileSize(text_Current_Result.TotalBytes))
        If text_Virtual Then
            Notify(Localization.TF("Размер файла ({0}) больше предела правки ({1}). Файл открыт только для чтения, по частям.",
                FormatFileSize(text_Current_Result.TotalBytes),
                FormatFileSize(CLng(modern_Preferences.TextEditingMaxMb) * 1048576L)), NotifyLevel.Info)
        End If
    End Sub

    ' --- paint -----------------------------------------------------------------------------------

    Private Sub PaintFullTextDocument(box As PictureBox, e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        Dim bounds As Rectangle = box.ClientRectangle
        Dim prefs As ModernViewerPreferences = modern_Preferences
        Dim systemBack As Color = NeutralBackColor()
        Dim colors As TextReaderPalette = TextReaderLayout.Palette(prefs.TextTheme, systemBack, GetOppositeColor(systemBack))
        g.Clear(colors.Back)

        Dim scale As Single = GetCurrentDpiScale()
        Dim points As Single = Math.Max(ModernViewerPreferences.TextFontSizeMin,
            Math.Min(ModernViewerPreferences.TextFontSizeMax, prefs.TextFontSize * text_Font_Scale))
        Dim fontSize As Single = points * scale
        Dim fontFamily As String = If(text_Current_Result.IsMonospace, prefs.TextFontFamilyMonospace, prefs.TextFontFamilyProportional)
        Dim wrap As Boolean = prefs.TextWrap
        Dim measureFlags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine

        Using font As New Font(fontFamily, fontSize, FontStyle.Regular),
              statusFont As New Font(ScriptFonts.Family("Segoe UI"), Math.Max(8.0F, 9.0F * scale), FontStyle.Regular)

            Dim rowHeight As Integer = Math.Max(14, CInt(font.GetHeight(g) * prefs.TextLineSpacing))
            Dim statusHeight As Integer = If(prefs.TextStatusBar, CInt(statusFont.GetHeight(g) * 1.6F) + 4, 0)
            Dim marginX As Integer = CInt(Math.Max(12, 24 * scale))
            Dim marginY As Integer = CInt(Math.Max(12, 20 * scale))

            Dim firstLine As Long
            Dim numbered As Boolean = text_Window_First_Line.TryGetValue(text_Pager.CurrentWindowIndex, firstLine)
            Dim gutterWidth As Integer = 0
            If prefs.TextLineNumbers Then
                Dim digits As Integer = TextReaderLayout.GutterDigits(If(numbered, firstLine, 0L) + text_Current_Result.Lines.Length)
                gutterWidth = TextRenderer.MeasureText(g, New String("0"c, digits), font, New Size(Integer.MaxValue, rowHeight), measureFlags).Width +
                    CInt(Math.Max(8, 14 * scale))
            End If
            Dim textLeft As Integer = marginX + gutterWidth
            Dim contentWidth As Integer = Math.Max(100, bounds.Width - textLeft - marginX)
            Dim availableHeight As Integer = bounds.Height - marginY * 2 - statusHeight
            Dim visibleRows As Integer = Math.Max(1, availableHeight \ rowHeight)
            text_Visible_Lines_Cached = visibleRows

            Dim layoutKey As String = String.Join("|", contentWidth, fontSize, fontFamily, wrap, text_Current_Result.Lines.Length)
            If layoutKey <> text_Layout_Key Then
                BuildFullTextRows(g, font, rowHeight, contentWidth, wrap)
                text_Layout_Key = layoutKey
                text_Layout_Width = contentWidth
                text_Layout_Font_Size = fontSize
            End If
            text_Total_Lines_Cached = text_Rows.Count
            If text_Pending_End Then
                text_Pending_End = False
                text_Scroll_Offset = Math.Max(0, text_Rows.Count - visibleRows)
            End If
            RevealPendingTextPosition(visibleRows)
            text_Scroll_Offset = Math.Max(0, Math.Min(text_Scroll_Offset, Math.Max(0, text_Total_Lines_Cached - visibleRows)))

            ' Wrap off: the widest row in view bounds the horizontal offset.
            If wrap Then
                text_Horizontal_Offset = 0
            Else
                Dim widest As Integer = 0
                For row As Integer = 0 To visibleRows - 1
                    Dim index As Integer = text_Scroll_Offset + row
                    If index >= text_Rows.Count Then Exit For
                    widest = Math.Max(widest, TextRenderer.MeasureText(g, text_Rows(index).Text, font, New Size(Integer.MaxValue, rowHeight), measureFlags).Width)
                Next
                text_Horizontal_Offset = Math.Max(0, Math.Min(text_Horizontal_Offset, widest - contentWidth))
            End If

            Dim rightToLeft As Boolean = Localization.IsRightToLeft()
            Dim flags As TextFormatFlags = measureFlags Or TextFormatFlags.VerticalCenter
            If rightToLeft Then flags = flags Or TextFormatFlags.RightToLeft
            Dim originX As Integer = textLeft - text_Horizontal_Offset
            text_Paint_Font_Family = fontFamily
            text_Paint_Font_Size = fontSize
            text_Paint_Margin_X = originX
            text_Paint_Margin_Y = marginY
            text_Paint_Row_Height = rowHeight
            text_Paint_Right_To_Left = rightToLeft
            text_Paint_Text_Left = textLeft

            Dim matchIndex As Integer = -1
            If text_Search_Matches.Count > 0 AndAlso text_Scroll_Offset < text_Rows.Count Then
                matchIndex = TextReaderLayout.FirstMatchFrom(text_Search_Matches, text_Rows(text_Scroll_Offset).Line)
                If matchIndex >= 0 AndAlso text_Search_Matches(matchIndex).Line < text_Rows(text_Scroll_Offset).Line Then matchIndex = -1
            End If

            Dim saved As Drawing2D.GraphicsState = g.Save()
            g.SetClip(New Rectangle(textLeft, marginY, Math.Max(1, bounds.Width - textLeft), Math.Max(1, visibleRows * rowHeight)))
            For row As Integer = 0 To visibleRows - 1
                Dim index As Integer = text_Scroll_Offset + row
                If index >= text_Rows.Count Then Exit For
                Dim rowTop As Integer = marginY + row * rowHeight
                Dim rowData As TextRow = text_Rows(index)

                While matchIndex >= 0 AndAlso matchIndex < text_Search_Matches.Count AndAlso
                      (text_Search_Matches(matchIndex).Line < rowData.Line OrElse
                       (text_Search_Matches(matchIndex).Line = rowData.Line AndAlso
                        text_Search_Matches(matchIndex).Column + text_Search_Matches(matchIndex).Length <= rowData.Start))
                    matchIndex += 1
                End While
                Dim probe As Integer = matchIndex
                While probe >= 0 AndAlso probe < text_Search_Matches.Count AndAlso text_Search_Matches(probe).Line = rowData.Line AndAlso
                      text_Search_Matches(probe).Column < rowData.End
                    Dim hit As TextMatch = text_Search_Matches(probe)
                    Dim fromColumn As Integer = Math.Max(0, hit.Column - rowData.Start)
                    Dim toColumn As Integer = Math.Min(rowData.Text.Length, hit.Column + hit.Length - rowData.Start)
                    If toColumn > fromColumn Then
                        PaintTextBand(g, font, rowData.Text, fromColumn, toColumn, originX, rowTop, rowHeight,
                            TextReaderLayout.MatchBandColor(probe = text_Search_Index))
                    End If
                    probe += 1
                End While

                Dim span As TextRowSpan = TextViewRules.SpanOnRow(text_Selection, rowData)
                If span.HasSpan Then
                    If span.IsBlankLine Then
                        Using band As New SolidBrush(SelectionBandColor())
                            g.FillRectangle(band, originX, rowTop, Math.Max(4, rowHeight \ 3), rowHeight)
                        End Using
                    Else
                        PaintTextBand(g, font, rowData.Text, span.FromColumn, span.ToColumn, originX, rowTop, rowHeight, SelectionBandColor())
                    End If
                End If

                TextRenderer.DrawText(g, rowData.Text, font,
                    New Rectangle(originX, rowTop, contentWidth + text_Horizontal_Offset + marginX, rowHeight), colors.Ink, flags)

                If prefs.TextWhitespace AndAlso (index + 1 >= text_Rows.Count OrElse text_Rows(index + 1).Line <> rowData.Line) Then
                    Dim width As Integer = If(rowData.Text.Length = 0, 0,
                        TextRenderer.MeasureText(g, rowData.Text, font, New Size(Integer.MaxValue, rowHeight), measureFlags).Width)
                    TextRenderer.DrawText(g, TextReaderLayout.LineEndMarker, font,
                        New Rectangle(originX + width, rowTop, rowHeight * 2, rowHeight), colors.Muted, flags)
                End If
            Next
            g.Restore(saved)

            If gutterWidth > 0 AndAlso numbered Then
                Dim numberFlags As TextFormatFlags = measureFlags Or TextFormatFlags.Right Or TextFormatFlags.VerticalCenter
                Dim numberWidth As Integer = gutterWidth - CInt(Math.Max(6, 10 * scale))
                For row As Integer = 0 To visibleRows - 1
                    Dim index As Integer = text_Scroll_Offset + row
                    If index >= text_Rows.Count Then Exit For
                    If text_Rows(index).Start <> 0 Then Continue For
                    Dim number As Long = firstLine + text_Rows(index).Line + 1
                    TextRenderer.DrawText(g, number.ToString(Globalization.CultureInfo.CurrentCulture), font,
                        New Rectangle(marginX, marginY + row * rowHeight, numberWidth, rowHeight), colors.Muted, numberFlags)
                Next
            End If

            PaintTextScrollCue(g, bounds, marginY, rowHeight, visibleRows, scale, colors.Ink)

            If statusHeight > 0 Then
                Dim statusRect As New Rectangle(0, bounds.Height - statusHeight, bounds.Width, statusHeight)
                Using strip As New SolidBrush(Color.FromArgb(24, colors.Ink))
                    g.FillRectangle(strip, statusRect)
                End Using
                Dim statusFlags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or
                    TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis
                If rightToLeft Then statusFlags = statusFlags Or TextFormatFlags.RightToLeft Or TextFormatFlags.Right
                TextRenderer.DrawText(g, FullTextStatusLine(numbered, firstLine), statusFont,
                    New Rectangle(marginX, statusRect.Top, Math.Max(1, bounds.Width - marginX * 2), statusHeight), colors.Muted, statusFlags)
            End If
        End Using
    End Sub

    ''' <summary>A tint under the columns [fromColumn, toColumn) of a row, measured with the row's font.</summary>
    Private Shared Sub PaintTextBand(g As Graphics, font As Font, rowText As String, fromColumn As Integer, toColumn As Integer,
                                     originX As Integer, rowTop As Integer, rowHeight As Integer, tint As Color)
        Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
        Dim left As Integer = originX
        If fromColumn > 0 Then
            left += TextRenderer.MeasureText(g, rowText.Substring(0, fromColumn), font, New Size(Integer.MaxValue, rowHeight), flags).Width
        End If
        Dim right As Integer = originX + TextRenderer.MeasureText(g, rowText.Substring(0, toColumn), font, New Size(Integer.MaxValue, rowHeight), flags).Width
        If right <= left Then Return
        Using band As New SolidBrush(tint)
            g.FillRectangle(band, left, rowTop, right - left, rowHeight)
        End Using
    End Sub

    ''' <summary>
    ''' The rows of the window: wrapped at the content width with a per-character width table (measuring every
    ''' row prefix, as the bounded preview does, would take seconds for a 16 MiB file), or one row per line
    ''' without wrap, cut only past <see cref="TextReaderLayout.MaxUnwrappedRowChars"/>.
    ''' </summary>
    Private Sub BuildFullTextRows(g As Graphics, font As Font, rowHeight As Integer, contentWidth As Integer, wrap As Boolean)
        text_Rows.Clear()
        Dim lines As String() = If(text_Display_Lines, text_Current_Result.Lines)
        Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
        Dim widths As New Dictionary(Of Integer, Integer)()
        Dim charWidth As Func(Of String, Integer, Integer) =
            Function(line As String, at As Integer)
                Dim isPair As Boolean = Char.IsHighSurrogate(line(at)) AndAlso at + 1 < line.Length
                Dim key As Integer = If(isPair, Char.ConvertToUtf32(line(at), line(at + 1)), AscW(line(at)))
                Dim width As Integer
                If Not widths.TryGetValue(key, width) Then
                    width = TextRenderer.MeasureText(g, line.Substring(at, If(isPair, 2, 1)), font, New Size(Integer.MaxValue, rowHeight), flags).Width
                    widths(key) = width
                End If
                Return width
            End Function
        ' A small reserve for kerning the per-character sum does not see.
        Dim fitWidth As Integer = Math.Max(16, contentWidth - Math.Max(2, contentWidth \ 50))
        For lineIndex As Integer = 0 To lines.Length - 1
            Dim line As String = lines(lineIndex)
            If line.Length = 0 Then
                text_Rows.Add(New TextRow(lineIndex, 0, ""))
                Continue For
            End If
            Dim offset As Integer = 0
            While offset < line.Length
                Dim count As Integer
                If wrap Then
                    count = TextReaderLayout.FitRow(line, offset, fitWidth, charWidth)
                Else
                    count = Math.Min(TextReaderLayout.MaxUnwrappedRowChars, line.Length - offset)
                    If offset + count < line.Length AndAlso Char.IsHighSurrogate(line(offset + count - 1)) Then count -= 1
                End If
                text_Rows.Add(New TextRow(lineIndex, offset, line.Substring(offset, count)))
                offset += count
            End While
        Next
    End Sub

    ''' <summary>Scrolls so a place asked for before the rows existed (a search hit, a line) is in view, a third
    ''' of the way down.</summary>
    Private Sub RevealPendingTextPosition(visibleRows As Integer)
        If Not text_Pending_Reveal.HasValue Then Return
        Dim target As TextPosition = text_Pending_Reveal.Value
        text_Pending_Reveal = Nothing
        For index As Integer = 0 To text_Rows.Count - 1
            Dim row As TextRow = text_Rows(index)
            If row.Line > target.Line OrElse (row.Line = target.Line AndAlso target.Column < row.End) OrElse
               (row.Line = target.Line AndAlso index + 1 < text_Rows.Count AndAlso text_Rows(index + 1).Line <> row.Line) Then
                If index < text_Scroll_Offset OrElse index >= text_Scroll_Offset + visibleRows Then
                    text_Scroll_Offset = Math.Max(0, index - visibleRows \ 3)
                End If
                Exit For
            End If
        Next
    End Sub

    ''' <summary>Line and column, selection, size, encoding, line endings, mode and search - section 4.2 item 2.</summary>
    Private Function FullTextStatusLine(numbered As Boolean, firstLine As Long) As String
        Dim parts As New List(Of String)()
        Dim place As New TextPosition(0, 0)
        If text_Selection IsNot Nothing Then
            place = text_Selection.Focus
        ElseIf text_Scroll_Offset < text_Rows.Count Then
            place = New TextPosition(text_Rows(text_Scroll_Offset).Line, text_Rows(text_Scroll_Offset).Start)
        End If
        parts.Add(Localization.TF("Стр. {0}, стб. {1}",
            If(numbered, (firstLine + place.Line + 1).ToString(Globalization.CultureInfo.CurrentCulture), "-"),
            (place.Column + 1).ToString(Globalization.CultureInfo.CurrentCulture)))
        If HasTextSelection() Then
            parts.Add(Localization.TF("Выделено: {0}", TextViewRules.CharacterCount(text_Current_Result.Lines, text_Selection).ToString()))
        End If
        parts.Add(FormatFileSize(text_Current_Result.TotalBytes))
        If text_Detection IsNot Nothing Then
            Dim name As String = TextReaderLayout.EncodingName(text_Detection.Encoding, text_Detection.HasBom)
            If text_Detection.Evidence = EncodingEvidence.HeuristicCandidate OrElse text_Detection.Evidence = EncodingEvidence.SystemAnsiFallback Then
                name = Localization.TF("{0} (определена по тексту)", name)
            ElseIf text_Detection.Evidence = EncodingEvidence.UserOverride Then
                name = Localization.TF("{0} (выбрана вручную)", name)
            End If
            parts.Add(name)
        End If
        If text_Line_Ending = LineEndingKind.Mixed Then
            parts.Add(Localization.T("Разные концы строк"))
        ElseIf text_Line_Ending <> LineEndingKind.None Then
            parts.Add(TextReaderLayout.LineEndingName(text_Line_Ending))
        End If
        If text_Pager.TotalWindows > 1 Then
            Dim startOffset As Long
            Dim endOffset As Long
            text_Pager.GetWindowBounds(text_Pager.CurrentWindowIndex, startOffset, endOffset)
            parts.Add(Localization.TF("Только чтение, часть {0} из {1}: {2} - {3}",
                (text_Pager.CurrentWindowIndex + 1).ToString(), text_Pager.TotalWindows.ToString(),
                FormatFileSize(startOffset), FormatFileSize(endOffset)))
        ElseIf text_Virtual Then
            parts.Add(Localization.T("Только чтение"))
        Else
            parts.Add(Localization.T("Чтение"))
        End If
        If text_Search_Matches.Count > 0 OrElse (text_File_Hits IsNot Nothing AndAlso text_File_Hits.Count > 0) Then
            parts.Add(TextSearchCountText())
        End If
        If text_Scan_Total > 0 Then
            parts.Add(Localization.TF("Поиск во всём файле: часть {0} из {1}", text_Scan_Done.ToString(), text_Scan_Total.ToString()))
        End If
        Return String.Join("  |  ", parts)
    End Function

    ' --- keys -----------------------------------------------------------------------------------

    ''' <summary>
    ''' The reader's own keys, asked only while a full-reader document is on screen: Ctrl+F finds, F3 and
    ''' Shift+F3 step through the hits, Ctrl+G goes to a line, Ctrl+PageUp/PageDown move between the windows of a
    ''' paged file and Ctrl+Home/End go to its first and last window, Ctrl+Shift+E re-opens the file in another
    ''' encoding, Ctrl+Shift+S saves the selection as a new file. Shift+wheel scrolls sideways (wrap off). Nothing else is taken, so sorting and navigation keys keep
    ''' their meaning.
    ''' </summary>
    Private Function TryHandleFullReaderKey(e As KeyEventArgs) As Boolean
        If Not FullTextReaderShown() Then Return False
        Select Case e.KeyData
            Case Keys.Control Or Keys.Shift Or Keys.E
                ReopenTextWithEncoding()
            Case Keys.Control Or Keys.Shift Or Keys.S
                SaveTextExcerpt()
            Case Keys.Control Or Keys.F
                PromptTextSearch()
            Case Keys.F3
                StepTextSearch(True)
            Case Keys.Shift Or Keys.F3
                StepTextSearch(False)
            Case Keys.Control Or Keys.G
                PromptGoToTextLine()
            Case Keys.Control Or Keys.PageDown
                If text_Pager.CurrentWindowIndex < text_Pager.TotalWindows - 1 Then ShowTextWindow(text_Pager.CurrentWindowIndex + 1, False)
            Case Keys.Control Or Keys.PageUp
                If text_Pager.CurrentWindowIndex > 0 Then ShowTextWindow(text_Pager.CurrentWindowIndex - 1, False)
            Case Keys.Control Or Keys.Home
                If text_Pager.CurrentWindowIndex <> 0 Then ShowTextWindow(0, False) Else ScrollTextToTop()
            Case Keys.Control Or Keys.End
                If text_Pager.CurrentWindowIndex <> text_Pager.TotalWindows - 1 Then
                    ShowTextWindow(text_Pager.TotalWindows - 1, True)
                Else
                    ScrollTextToBottom()
                End If
            Case Else
                Return False
        End Select
        Return True
    End Function

    ''' <summary>Shift+wheel with wrap off: the text moves sideways. False when there is nothing to move.</summary>
    Friend Function TryScrollTextHorizontally(delta As Integer) As Boolean
        If Not FullTextReaderShown() OrElse modern_Preferences.TextWrap OrElse delta = 0 Then Return False
        Dim stepPixels As Integer = Math.Max(16, text_Paint_Row_Height * 3)
        text_Horizontal_Offset = Math.Max(0, text_Horizontal_Offset + If(delta > 0, -stepPixels, stepPixels))
        GetActivePictureBox()?.Invalidate()
        Return True
    End Function

    ' --- search and go to line ------------------------------------------------------------------

    Private Sub PromptTextSearch()
        Dim prompt As New PromptSpec(Localization.T("Поиск в тексте"), Localization.T("Что найти"), text_Search_Query, Localization.T("Найти"),
            Function(value)
                If String.IsNullOrEmpty(value) Then Return Localization.T("Не задано")
                If modern_Preferences.TextRegexSearch Then
                    Try
                        System.Text.RegularExpressions.Regex.IsMatch("", value, System.Text.RegularExpressions.RegexOptions.None, TextReaderLayout.RegexTimeout)
                    Catch ex As ArgumentException
                        Return Localization.T("Неверное регулярное выражение")
                    End Try
                End If
                Return Nothing
            End Function)
        Dim query As String
        Using dialog As New Ask_Dialog(prompt)
            DialogLayer.ShowViewerDialog(dialog, Me)
            If dialog.Answer <> 0 Then Return
            query = dialog.TypedText
        End Using
        If Not FullTextReaderShown() Then Return
        text_Search_Query = query
        RunTextSearch(True)
    End Sub

    ''' <summary>Searches the window on screen at once. <paramref name="announce"/> (a new query) brings the first hit
    ''' from the reading position into view, says the count and, for a paged file, starts the full-file search of
    ''' section 5.1 item 4 in the background; until it ends the count says it is of this part only.</summary>
    Private Sub RunTextSearch(announce As Boolean)
        Dim problem As String = Nothing
        text_Search_Matches = TextReaderLayout.FindMatches(text_Current_Result.Lines, text_Search_Query,
            modern_Preferences.TextSearchCase, modern_Preferences.TextSearchWholeWord, modern_Preferences.TextRegexSearch, problem)
        text_Search_Index = TextReaderLayout.FirstMatchFrom(text_Search_Matches, TopTextLine())
        If problem = "timeout" Then
            Notify(Localization.T("Поиск остановлен: выражение проверяется слишком долго"), NotifyLevel.Warning)
        ElseIf problem = "limit" Then
            Notify(Localization.TF("Совпадений больше {0}; показаны первые из них", TextReaderLayout.MaxMatches.ToString()), NotifyLevel.Info)
        End If
        If Not announce Then Return
        Dim scanFile As Boolean = text_Pager.TotalWindows > 1 AndAlso problem <> "timeout" AndAlso problem <> "pattern"
        If scanFile Then StartFullTextScan(text_Search_Index < 0)
        If text_Search_Index >= 0 Then
            RevealTextMatch()
        ElseIf scanFile Then
            Notify(Localization.T("В этой части файла не найдено - ищу во всём файле.."), NotifyLevel.Info)
        Else
            Notify(Localization.T("Не найдено"), NotifyLevel.Info)
        End If
        GetActivePictureBox()?.Invalidate()
    End Sub

    ''' <summary>The logical line at the top of the reader, where a new search starts.</summary>
    Private Function TopTextLine() As Integer
        Return If(text_Scroll_Offset < text_Rows.Count AndAlso text_Rows.Count > 0, text_Rows(text_Scroll_Offset).Line, 0)
    End Function

    Private Sub StepTextSearch(forward As Boolean)
        If text_Search_Query.Length = 0 Then
            PromptTextSearch()
            Return
        End If
        If text_File_Hits IsNot Nothing Then
            If text_File_Hits.Count = 0 Then
                Notify(Localization.T("Не найдено во всём файле"), NotifyLevel.Info)
                Return
            End If
            GoToFileHit(TextFileSearch.StepFrom(text_File_Hits, text_File_Hit_Index, text_Pager.CurrentWindowIndex, TopTextLine(), forward))
            Return
        End If
        If text_Search_Matches.Count = 0 Then
            Notify(Localization.T("Не найдено"), NotifyLevel.Info)
            Return
        End If
        text_Search_Index = TextReaderLayout.StepMatch(text_Search_Matches.Count, text_Search_Index, forward)
        RevealTextMatch()
        GetActivePictureBox()?.Invalidate()
    End Sub

    Private Sub RevealTextMatch()
        Dim hit As TextMatch = text_Search_Matches(text_Search_Index)
        text_Pending_Reveal = New TextPosition(hit.Line, hit.Column)
        Notify(TextSearchCountText(), NotifyLevel.Info)
    End Sub

    ''' <summary>Brings a hit of the full-file search into view, putting its window on screen first.</summary>
    Private Sub GoToFileHit(index As Integer)
        If index < 0 OrElse index >= text_File_Hits.Count Then Return
        Dim hit As TextFileHit = text_File_Hits(index)
        If hit.Window <> text_Pager.CurrentWindowIndex Then
            ShowTextWindow(hit.Window, False)
            If text_File_Hits Is Nothing OrElse text_Pager.CurrentWindowIndex <> hit.Window Then Return
        End If
        Dim first As Integer
        text_Search_Matches = TextFileSearch.WindowMatches(text_File_Hits, hit.Window, first)
        text_File_Hit_Index = index
        text_Search_Index = index - first
        RevealTextMatch()
        GetActivePictureBox()?.Invalidate()
    End Sub

    ''' <summary>"Match 4 of 28". For a paged file the count says its scope (section 5.1 item 4): this part only until
    ''' the full-file search has ended, then the whole file - or its first hits when there were too many.</summary>
    Private Function TextSearchCountText() As String
        If text_File_Hits IsNot Nothing Then
            Dim filePosition As String = If(text_File_Hit_Index < 0, "-", (text_File_Hit_Index + 1).ToString())
            If text_File_Hits_Capped Then
                Return Localization.TF("Совпадение {0} из первых {1} в файле", filePosition, text_File_Hits.Count.ToString())
            End If
            Return Localization.TF("Совпадение {0} из {1} во всём файле", filePosition, text_File_Hits.Count.ToString())
        End If
        Dim position As String = (Math.Max(0, text_Search_Index) + 1).ToString()
        Dim total As String = text_Search_Matches.Count.ToString()
        If text_Pager IsNot Nothing AndAlso text_Pager.TotalWindows > 1 Then
            Return Localization.TF("Совпадение {0} из {1} в этой части файла", position, total)
        End If
        Return Localization.TF("Совпадение {0} из {1}", position, total)
    End Function

    ''' <summary>
    ''' The full-file search of a paged file (section 5.1 item 4): every window is read and searched on the pool
    ''' through a pager of its own, the status line shows how far it is, and a new query, another file or a closed
    ''' reader cancels it. When it ends its hits replace the window's own. <paramref name="revealFirst"/>: the window on
    ''' screen had no hit, so the first one from the reading position is brought into view, in whatever window.
    ''' </summary>
    Private Async Sub StartFullTextScan(revealFirst As Boolean)
        CancelFullTextScan()
        text_File_Hits = Nothing
        text_File_Hit_Index = -1
        text_File_Hits_Capped = False
        Dim pager As TextFilePager = text_Pager
        Dim request As New TextFileSearchRequest With {
            .SourcePath = text_Current_File_Path,
            .SourceEncoding = text_Detection.Encoding,
            .BomLength = text_Detection.BomLength,
            .WindowBytes = pager.WindowSize,
            .ExpectedFileSize = pager.FileSize,
            .TabWidth = modern_Preferences.TextTabWidth,
            .Query = text_Search_Query,
            .MatchCase = modern_Preferences.TextSearchCase,
            .WholeWord = modern_Preferences.TextSearchWholeWord,
            .UseRegex = modern_Preferences.TextRegexSearch
        }
        Dim cancel As New System.Threading.CancellationTokenSource()
        text_Scan_Cancel = cancel
        text_Scan_Done = 0
        text_Scan_Total = pager.TotalWindows
        Dim reporter As IProgress(Of Integer) = New Progress(Of Integer)(
            Sub(done)
                If text_Scan_Cancel IsNot cancel OrElse IsDisposed Then Return
                text_Scan_Done = done
                GetActivePictureBox()?.Invalidate()
            End Sub)
        Dim token As System.Threading.CancellationToken = cancel.Token
        Dim result As TextFileSearchResult = Nothing
        Dim failure As Exception = Nothing
        Try
            result = Await Task.Run(Function() TextFileSearch.Scan(request, token, Sub(done) reporter.Report(done)))
        Catch ex As OperationCanceledException
            ' Cancelled by a new query or by the release of the reader, which already cleared the field.
        Catch ex As Exception
            failure = ex
            AppFileLogger.LogMessage("Text full-file search failed: " & ex.GetType().Name & ": " & ex.Message)
        End Try
        ' Only the search still on record ends the progress; a cancelled one was replaced before it got here.
        Dim current As Boolean = text_Scan_Cancel Is cancel
        If current Then EndFullTextScan()
        cancel.Dispose()
        If Not current OrElse (result Is Nothing AndAlso failure Is Nothing) OrElse IsDisposed Then Return
        If Not FullTextReaderShown() OrElse text_Pager IsNot pager Then Return
        If failure IsNot Nothing Then
            Notify(Localization.TF("Не удалось искать во всём файле: {0}", FailureText.Describe(failure)), NotifyLevel.Error)
            GetActivePictureBox()?.Invalidate()
            Return
        End If
        Select Case result.Problem
            Case "changed"
                Notify(Localization.T("Файл изменился после открытия - откройте его заново, чтобы искать во всём файле"), NotifyLevel.Warning)
                Return
            Case "timeout"
                Notify(Localization.T("Поиск во всём файле остановлен: выражение проверяется слишком долго"), NotifyLevel.Warning)
                Return
            Case "pattern"
                Return
        End Select
        text_File_Hits = result.Hits
        text_File_Hits_Capped = result.Problem = "limit"
        Dim first As Integer
        text_Search_Matches = TextFileSearch.WindowMatches(text_File_Hits, pager.CurrentWindowIndex, first)
        If text_Search_Index >= text_Search_Matches.Count Then text_Search_Index = -1
        text_File_Hit_Index = If(text_Search_Index >= 0, first + text_Search_Index, -1)
        If text_File_Hits_Capped Then
            Notify(Localization.TF("Совпадений больше {0}; показаны первые из них", TextReaderLayout.MaxMatches.ToString()), NotifyLevel.Info)
        End If
        If revealFirst Then
            If text_File_Hits.Count = 0 Then
                Notify(Localization.T("Не найдено во всём файле"), NotifyLevel.Info)
            Else
                GoToFileHit(TextFileSearch.StepFrom(text_File_Hits, -1, pager.CurrentWindowIndex, TopTextLine(), True))
                Return
            End If
        End If
        GetActivePictureBox()?.Invalidate()
    End Sub

    ''' <summary>Stops a running full-file search; its late result is dropped. The search disposes its own token
    ''' source when it ends, which is after this, since only its continuation does that.</summary>
    Private Sub CancelFullTextScan()
        Dim running As System.Threading.CancellationTokenSource = text_Scan_Cancel
        EndFullTextScan()
        running?.Cancel()
    End Sub

    Private Sub EndFullTextScan()
        text_Scan_Cancel = Nothing
        text_Scan_Done = 0
        text_Scan_Total = 0
    End Sub

    Private Sub PromptGoToTextLine()
        Dim firstLine As Long
        If Not text_Window_First_Line.TryGetValue(text_Pager.CurrentWindowIndex, firstLine) Then
            Notify(Localization.T("Номера строк этой части файла неизвестны: откройте её по порядку от начала файла"), NotifyLevel.Info)
            Return
        End If
        Dim lowest As Long = firstLine + 1
        Dim highest As Long = firstLine + text_Current_Result.Lines.Length
        Dim range As String = Localization.TF("Номер строки от {0} до {1}", lowest.ToString(), highest.ToString())
        Dim prompt As New PromptSpec(Localization.T("Перейти к строке"), range, "", Localization.T("Перейти"),
            Function(value)
                Dim number As Long
                If Not Long.TryParse(value.Trim(), number) OrElse number < lowest OrElse number > highest Then Return range
                Return Nothing
            End Function)
        Dim answer As Long
        Using dialog As New Ask_Dialog(prompt)
            DialogLayer.ShowViewerDialog(dialog, Me)
            If dialog.Answer <> 0 OrElse Not Long.TryParse(dialog.TypedText.Trim(), answer) Then Return
        End Using
        If Not FullTextReaderShown() Then Return
        Dim lineIndex As Integer = CInt(Math.Max(0L, Math.Min(text_Current_Result.Lines.Length - 1, answer - lowest)))
        text_Selection = New TextSelection(New TextPosition(lineIndex, 0), New TextPosition(lineIndex, text_Current_Result.Lines(lineIndex).Length))
        text_Pending_Reveal = New TextPosition(lineIndex, 0)
        GetActivePictureBox()?.Invalidate()
    End Sub

    ' --- save the selection as a new file (section 5.1 item 3) ---------------------------------------------

    ''' <summary>
    ''' "Save Selected Text as New File..": the selection, in the file's own characters, written in the source's
    ''' encoding and dominant line ending to a derived file offered as "&lt;stem&gt; (excerpt).&lt;ext&gt;" beside the
    ''' source (CAPTURE-OUTPUT 0.7 section 5) - beside the archive for an archive entry, in Documents for a file
    ''' of a server, never in a cache folder. The name and folder typed in the dialog win; a collision is the
    ''' dialog's overwrite prompt. The source is never touched. The window is read again, encoded and written on the
    ''' pool (a window can be the whole file up to the editing ceiling), through a pager of its own.
    ''' </summary>
    Private Async Sub SaveTextExcerpt()
        If text_Excerpt_Saving OrElse SecureBlocksWritingAction() Then Return
        If Not HasTextSelection() Then
            Notify(Localization.T("Нечего сохранять - выделите текст мышью или нажмите Ctrl+A"), NotifyLevel.Info)
            Return
        End If
        Dim filePath As String = text_Current_File_Path
        Dim first As TextPosition = text_Selection.StartPosition
        Dim last As TextPosition = text_Selection.EndPosition
        Dim detection As EncodingDetectionResult = text_Detection
        Dim request As New TextExcerptRequest With {
            .SourcePath = filePath,
            .SourceEncoding = detection.Encoding,
            .HasBom = detection.HasBom,
            .BomLength = detection.BomLength,
            .WindowBytes = text_Pager.WindowSize,
            .WindowIndex = text_Pager.CurrentWindowIndex,
            .ShownLines = text_Current_Result.Lines,
            .TabWidth = modern_Preferences.TextTabWidth,
            .StartLine = first.Line, .StartColumn = first.Column,
            .EndLine = last.Line, .EndColumn = last.Column
        }

        Dim folder As String = Path.GetDirectoryName(filePath)
        Dim sourceName As String = Path.GetFileName(filePath)
        If IsArchiveMode() Then
            folder = archive_Return_Folder
            sourceName = ArchiveDisplayName(filePath)
        ElseIf IsResourceMode() OrElse RemoteCachePaths.IsUnderRemoteCache(filePath) Then
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            sourceName = TransferNameOf(filePath)
        End If
        sourceName = Path.GetFileName(sourceName.Replace("/"c, Path.DirectorySeparatorChar))
        Dim extension As String = Path.GetExtension(sourceName)
        Dim target As String
        Using dialog As New SaveFileDialog()
            dialog.Title = Localization.T("Сохранить выделенное как новый файл")
            If Not String.IsNullOrEmpty(folder) AndAlso Directory.Exists(folder) Then dialog.InitialDirectory = folder
            dialog.FileName = Localization.TF("{0} (выдержка){1}", Path.GetFileNameWithoutExtension(sourceName), extension)
            dialog.OverwritePrompt = True
            dialog.AddExtension = extension.Length > 0
            If extension.Length > 0 Then dialog.DefaultExt = extension.TrimStart("."c)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            target = dialog.FileName
        End Using
        If ArchivePaths.IsUnderArchiveCache(target) OrElse RemoteCachePaths.IsUnderRemoteCache(target) Then
            Notify(Localization.T("Сюда сохранить нельзя: это временная папка"), NotifyLevel.Warning)
            Return
        End If
        If String.Equals(Path.GetFullPath(target), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase) Then
            Notify(Localization.T("Выдержку нельзя сохранить поверх самого файла"), NotifyLevel.Warning)
            Return
        End If
        request.TargetPath = target
        Dim outcome As TextExcerptOutcome
        text_Excerpt_Saving = True
        Try
            outcome = Await Task.Run(Function() TextExcerpt.Save(request))
        Catch ex As Exception
            AppFileLogger.LogMessage("Text excerpt not written: " & ex.GetType().Name & ": " & ex.Message)
            If Not IsDisposed Then Notify(Localization.TF("Не удалось сохранить файл: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
            Return
        Finally
            text_Excerpt_Saving = False
        End Try
        If IsDisposed Then Return
        Select Case outcome.Problem
            Case TextExcerptProblem.None
                Notify(Localization.TF("Выделенное сохранено: {0}", Path.GetFileName(target)), NotifyLevel.Info)
            Case TextExcerptProblem.SourceChanged
                Notify(Localization.T("Файл изменился после открытия - откройте его заново и выделите текст ещё раз"), NotifyLevel.Warning)
            Case TextExcerptProblem.UnreadableCharacter
                Notify(Localization.T("В выделении есть символы, не прочитанные в этой кодировке - выберите другую кодировку (Ctrl+Shift+E)"), NotifyLevel.Warning)
            Case TextExcerptProblem.UnencodableCharacter
                Notify(Localization.TF("Символ {0} выделения нельзя записать в кодировке {1}",
                    (outcome.FailedAt + 1).ToString(), TextReaderLayout.EncodingName(detection.Encoding, detection.HasBom)), NotifyLevel.Warning)
        End Select
    End Sub

    ' --- re-open with encoding (section 6.2) ---------------------------------------------------------------

    Private Function TextDocumentIdentity(filePath As String, info As FileInfo) As DocumentIdentity
        Return DocumentReaderIdentity(filePath, info.LastWriteTimeUtc.Ticks, info.Length)
    End Function

    ''' <summary>
    ''' Asks which encoding to read the file on screen with, previewing the start of the file in each, and reads it
    ''' again from its bytes - the file itself is never touched. The choice is remembered for this version of the
    ''' file while <c>TextRememberEncoding</c> is on; "detect automatically" forgets it. The window and the scroll
    ''' place stay where they were.
    ''' </summary>
    Private Sub ReopenTextWithEncoding()
        Dim filePath As String = text_Current_File_Path
        Dim probe(TextEncodingChoice.PreviewProbeBytes - 1) As Byte
        Dim filled As Integer = 0
        Dim info As FileInfo = Nothing
        Try
            info = New FileInfo(filePath)
            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                While filled < probe.Length
                    Dim count As Integer = stream.Read(probe, filled, probe.Length - filled)
                    If count <= 0 Then Exit While
                    filled += count
                End While
            End Using
        Catch ex As Exception
            Notify(Localization.TF("Не удалось прочитать файл: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
            Return
        End Try

        Dim chosen As Integer
        Dim current As Integer = TextEncodingChoice.InitialChoice(text_Detection?.Encoding)
        Dim chosenByHand As Boolean = text_Detection IsNot Nothing AndAlso text_Detection.Evidence = EncodingEvidence.UserOverride
        Using dialog As New Text_Encoding_Form(Path.GetFileName(filePath), probe, filled, info.Length > filled, current, chosenByHand)
            If DialogLayer.ShowViewerDialog(dialog, Me) <> DialogResult.OK Then Return
            chosen = dialog.SelectedCodePage
        End Using
        If Not FullTextReaderShown() OrElse Not String.Equals(text_Current_File_Path, filePath, StringComparison.OrdinalIgnoreCase) Then Return

        If modern_Preferences.TextRememberEncoding Then
            Dim identity As DocumentIdentity = TextDocumentIdentity(filePath, info)
            If chosen = 0 Then
                TextEncodingMemory.Current.Forget(identity)
            Else
                TextEncodingMemory.Current.Remember(identity, chosen)
            End If
            If TextEncodingMemory.Current.Diagnostic.Length > 0 Then AppFileLogger.LogMessage("Text encoding memory: " & TextEncodingMemory.Current.Diagnostic)
        End If

        Dim windowIndex As Integer = text_Pager.CurrentWindowIndex
        Dim scrollOffset As Integer = text_Scroll_Offset
        text_Encoding_Request = chosen
        LoadTextDocument(filePath)
        If Not FullTextReaderShown() Then Return
        If windowIndex > 0 AndAlso windowIndex < text_Pager.TotalWindows Then ShowTextWindow(windowIndex, False)
        text_Scroll_Offset = scrollOffset
        GetActivePictureBox()?.Invalidate()
        If chosen = 0 Then
            Notify(Localization.T("Кодировка снова определяется автоматически"), NotifyLevel.Info)
        Else
            Notify(Localization.TF("Файл прочитан в кодировке {0}", TextReaderLayout.EncodingName(text_Detection.Encoding, text_Detection.HasBom)), NotifyLevel.Info)
        End If
    End Sub

End Class
#End If
