#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

Partial Public Class Main_Form
    Private content_Placeholder_Path As String = ""
    Private content_Base_Title As String
    Private content_Open_Pending As Boolean
    Private epub_Current_Info As EpubInfo
    Private epub_Cover_Bitmap As Bitmap

    Private Function IsContentPlaceholderShown() As Boolean
        Return content_Placeholder_Path.Length > 0 AndAlso
            String.Equals(content_Placeholder_Path, Current_File_Name, StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(content_Placeholder_Path, current_Loaded_File_Name, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function ContentKindLabel(filePath As String) As String
        Select Case KindOf(filePath)
            Case MediaKind.Document, MediaKind.Book
                Return Localization.T("Документы")
            Case MediaKind.Audio
                Return Localization.T("Аудио")
            Case Else
                Return Localization.T("Другие")
        End Select
    End Function

    Private Sub ShowContentPlaceholder(filePath As String)
        ClearEpubPlaceholderData()
        content_Placeholder_Path = filePath
        SetMediaDisplayKind(KindOf(filePath))
        ResetZoomViewport()
        SetViewSizes()

        Dim ext As String = MediaKindClassifier.ExtensionOf(filePath)
        If ext = ".epub" Then
            Try
                epub_Current_Info = EpubMetadata.Read(filePath)
                If epub_Current_Info IsNot Nothing AndAlso epub_Current_Info.HasCover AndAlso epub_Current_Info.CoverBytes IsNot Nothing Then
                    Using ms As New MemoryStream(epub_Current_Info.CoverBytes)
                        Using decoded As New Bitmap(ms)
                            epub_Cover_Bitmap = New Bitmap(decoded)
                        End Using
                    End Using
                End If
            Catch ex As Exception
                Debug.WriteLine("EPUB metadata read failed: " & ex.Message)
            End Try
        End If

        lbl_Status.Text = Localization.TF("{0} - {1}", Path.GetFileName(CurrentFileDisplayName(filePath)), ContentKindLabel(filePath))
        GetActivePictureBox()?.Invalidate()
    End Sub

    Private Sub ClearEpubPlaceholderData()
        epub_Current_Info = Nothing
        If epub_Cover_Bitmap IsNot Nothing Then
            Try
                epub_Cover_Bitmap.Dispose()
            Catch
            Finally
                epub_Cover_Bitmap = Nothing
            End Try
        End If
    End Sub

    Private Sub UpdateContentWindowTitle()
        If content_Base_Title Is Nothing Then content_Base_Title = Me.Text
        Dim kind As MediaKind = KindOf(Current_File_Name)
        If IsContentPlaceholderShown() OrElse (kind = MediaKind.Audio AndAlso IsAudioNowPlaying()) Then
            Me.Text = content_Base_Title & " - " & Localization.TF("{0} - {1}", Path.GetFileName(CurrentFileDisplayName(Current_File_Name)), ContentKindLabel(Current_File_Name))
        Else
            Me.Text = content_Base_Title
        End If
    End Sub

    Private Sub PaintContentPlaceholder(box As PictureBox, e As PaintEventArgs)
        If Not IsContentPlaceholderShown() OrElse box IsNot GetActivePictureBox() Then Return
        e.Graphics.Clear(NeutralBackColor())
        Dim bounds As Rectangle = box.ClientRectangle
        Dim scale As Single = Math.Min(1.0F, Math.Min(bounds.Width / 420.0F, bounds.Height / 250.0F))
        If scale <= 0 Then Return
        Dim centerX As Single = bounds.Width / 2.0F
        Dim top As Single = Math.Max(8.0F, (bounds.Height - 220.0F * scale) / 2.0F)
        Dim ink As Color = GetOppositeColor(NeutralBackColor())

        If epub_Cover_Bitmap IsNot Nothing Then
            ' Draw EPUB cover image
            Dim coverMaxH As Single = 120.0F * scale
            Dim coverMaxW As Single = 90.0F * scale
            Dim coverAspect As Single = epub_Cover_Bitmap.Width / CSng(Math.Max(1, epub_Cover_Bitmap.Height))
            Dim drawW As Single = coverMaxW
            Dim drawH As Single = drawW / Math.Max(0.1F, coverAspect)
            If drawH > coverMaxH Then
                drawH = coverMaxH
                drawW = drawH * coverAspect
            End If
            Dim coverX As Single = centerX - drawW / 2.0F
            Dim coverY As Single = top
            e.Graphics.DrawImage(epub_Cover_Bitmap, New RectangleF(coverX, coverY, drawW, drawH))
            Using coverPen As New Pen(Color.FromArgb(120, ink), 1.0F)
                e.Graphics.DrawRectangle(coverPen, coverX, coverY, drawW, drawH)
            End Using
        Else
            ' Draw generic document placeholder icon
            Using pen As New Pen(ink, 2.0F * scale)
                Dim x As Single = centerX - 23.0F * scale
                Dim w As Single = 46.0F * scale
                Dim h As Single = 60.0F * scale
                Dim fold As Single = 13.0F * scale
                e.Graphics.DrawLines(pen, New PointF() {
                    New PointF(x, top), New PointF(x + w - fold, top),
                    New PointF(x + w, top + fold), New PointF(x + w, top + h),
                    New PointF(x, top + h), New PointF(x, top)})
                e.Graphics.DrawLines(pen, New PointF() {
                    New PointF(x + w - fold, top), New PointF(x + w - fold, top + fold), New PointF(x + w, top + fold)})
                For i As Integer = 0 To 2
                    e.Graphics.DrawLine(pen, x + 9 * scale, top + (28 + i * 9) * scale, x + w - 9 * scale, top + (28 + i * 9) * scale)
                Next
            End Using
        End If

        Dim meta As FileMetaInfo
        Dim detail As String = NavigationRules.ExtensionKey(content_Placeholder_Path).TrimStart("."c).ToUpperInvariant()
        If file_Meta IsNot Nothing AndAlso file_Meta.TryGetValue(content_Placeholder_Path, meta) Then
            detail &= " · " & PanelText.FormatSize(meta.SizeBytes) & " · " & meta.Modified.ToString("g")
        End If

        ' If EPUB has title or author, enrich detail
        Dim titleText As String = Path.GetFileName(CurrentFileDisplayName(content_Placeholder_Path))
        If epub_Current_Info IsNot Nothing Then
            If Not String.IsNullOrEmpty(epub_Current_Info.Title) Then
                titleText = epub_Current_Info.Title
            End If
            If Not String.IsNullOrEmpty(epub_Current_Info.Author) Then
                detail = epub_Current_Info.Author & " · " & detail
            End If
        End If

        Dim flags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix
        If Localization.IsRightToLeft() Then flags = flags Or TextFormatFlags.RightToLeft

        Dim ext As String = MediaKindClassifier.ExtensionOf(content_Placeholder_Path)
        Dim isDocHtmlEbook As Boolean = (ext = ".epub" OrElse ext = ".fb2" OrElse ext = ".mobi")
        ' Names the key the open action answers to NOW (123 UX-112: it can be moved); with the shortcut taken
        ' away the line is left out - a double click on the placeholder still opens the file.
        Dim openKey As String = OpenContentKeyLabel()
        Dim enterActionText As String = ""
        If openKey.Length > 0 Then
            enterActionText = If(isDocHtmlEbook,
                Localization.TF("{0} - открыть в doc-html-translate", openKey),
                Localization.TF("{0} - открыть в программе по умолчанию", openKey))
        End If

        Using nameFont As New Font(ScriptFonts.Family("Segoe UI"), Math.Max(8.0F, 16.0F * scale), FontStyle.Bold),
              detailFont As New Font(ScriptFonts.Family("Segoe UI"), Math.Max(8.0F, 11.0F * scale))
            Dim y As Integer = CInt(top + If(epub_Cover_Bitmap IsNot Nothing, 128 * scale, 78 * scale))
            Dim line As Integer = Math.Max(24, CInt(36 * scale))
            TextRenderer.DrawText(e.Graphics, titleText, nameFont, New Rectangle(12, y, Math.Max(0, bounds.Width - 24), line), ink, flags)
            TextRenderer.DrawText(e.Graphics, detail, detailFont, New Rectangle(12, y + line, Math.Max(0, bounds.Width - 24), line), ink, flags)
            TextRenderer.DrawText(e.Graphics, enterActionText, detailFont, New Rectangle(12, y + line * 2, Math.Max(0, bounds.Width - 24), line), ink, flags)
        End Using
    End Sub

    Private Async Sub OpenContentInDefaultProgram()
        If Not (IsContentPlaceholderShown() OrElse IsTextDocumentShown() OrElse IsPdfDocumentShown()) OrElse content_Open_Pending Then Return
        If IsArchiveMode() Then
            lbl_Status.Text = Localization.T("Сначала извлеките файл из архива")
            Return
        End If
        Dim filePath As String = Current_File_Name
        Dim generation As Integer = media_Generation
        content_Open_Pending = True
        Try
            If Not Await EnsureEntryLocal(filePath) Then Return
            If generation <> media_Generation OrElse Not (IsContentPlaceholderShown() OrElse IsTextDocumentShown() OrElse IsPdfDocumentShown()) OrElse IsDisposed Then Return

            Dim ext As String = MediaKindClassifier.ExtensionOf(filePath)
            If ext = ".epub" OrElse ext = ".fb2" OrElse ext = ".mobi" Then
                ' Hand off to doc-html-translate (§4)
                Dim exe As String = DocHtmlTranslate.FindExe()
                If exe.Length > 0 AndAlso File.Exists(exe) Then
                    Process.Start(New ProcessStartInfo(exe, DocHtmlTranslate.BuildArguments("", False, "", filePath)) With {.UseShellExecute = False})
                Else
                    OfferInstallCompanion()
                End If
            Else
                Process.Start(New ProcessStartInfo(filePath) With {.UseShellExecute = True})
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Opening sortable content in the default program", ex)
            If generation = media_Generation AndAlso Not IsDisposed Then lbl_Status.Text = Localization.TF("Не удалось открыть файл: {0}", Path.GetFileName(CurrentFileDisplayName(filePath)))
        Finally
            content_Open_Pending = False
        End Try
    End Sub
End Class
#End If
