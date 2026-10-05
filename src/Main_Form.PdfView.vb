#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Main_Form
    Private pdf_Current_Source As PdfDocumentSource
    Private pdf_Current_File_Path As String = ""
    Private pdf_Current_Page As Integer = 1
    Private pdf_Total_Pages As Integer
    ' UI owns the cache; workers return private bitmaps and never touch controls or cache.
    Private ReadOnly pdf_Page_Cache As New Dictionary(Of Integer, Bitmap)()
    Private pdf_Render_Cts As CancellationTokenSource
    Private pdf_Session As Integer
    Private pdf_Last_Render_Width As Integer
    Private pdf_Last_Render_Height As Integer
    Private pdf_Page_Error As String = ""
    Private pdf_Indicator_Bounds As Rectangle
    Private pdf_Write_Ticks As Long
    Private pdf_File_Length As Long

    Friend Function IsPdfDocumentShown() As Boolean
        Return pdf_Current_File_Path.Length > 0 AndAlso
            String.Equals(pdf_Current_File_Path, Current_File_Name, StringComparison.OrdinalIgnoreCase) AndAlso
            pdf_Current_Source IsNot Nothing AndAlso pdf_Current_Source.IsOpen
    End Function

    Friend Function IsPdfDocumentPageOnScreen() As Boolean
        Return IsPdfDocumentShown() AndAlso pdf_Page_Error.Length = 0 AndAlso ActiveMediaImage() IsNot Nothing
    End Function

    Friend Async Sub LoadPdfDocument(filePath As String)
        BlankActiveMediaSurface()
        current_Loaded_File_Name = filePath
        pdf_Current_File_Path = filePath
        Dim session As Integer = pdf_Session
        Dim generation As Integer = media_Generation
        Dim source As New PdfDocumentSource()
        Try
            Await Task.Run(Sub() source.Open(filePath))
            If IsDisposed OrElse session <> pdf_Session OrElse generation <> media_Generation Then
                source.Dispose()
                Return
            End If
            If Not source.IsOpen Then
                Dim message As String = source.ErrorMessage
                source.Dispose()
                ReleasePdfDocument()
                ShowContentPlaceholder(filePath)
                lbl_Status.Text = Localization.TF("{0} - {1}", Path.GetFileName(filePath), message)
                Return
            End If
            pdf_Current_Source = source
            pdf_Total_Pages = source.PageCount
            Dim fi As New FileInfo(filePath)
            pdf_Write_Ticks = fi.LastWriteTimeUtc.Ticks
            pdf_File_Length = fi.Length
            Dim page As Integer = 1
            If Not Is_slide_show_mode AndAlso modern_Preferences IsNot Nothing Then
                page = modern_Preferences.RememberedDocumentPage(filePath, pdf_Write_Ticks, pdf_File_Length)
            End If
            SetMediaDisplayKind(MediaKind.Book)
            ResetZoomViewport()
            SetViewSizes()
            DisplayPdfPage(Math.Min(page, pdf_Total_Pages))
        Catch ex As Exception
            source.Dispose()
            If Not IsDisposed AndAlso session = pdf_Session AndAlso generation = media_Generation Then
                ReleasePdfDocument()
                ShowContentPlaceholder(filePath)
                lbl_Status.Text = Localization.TF("{0} - {1}", Path.GetFileName(filePath), FailureText.Describe(ex))
            End If
        End Try
    End Sub

    Private Sub CancelPdfRender()
        If pdf_Render_Cts Is Nothing Then Return
        pdf_Render_Cts.Cancel()
        pdf_Render_Cts.Dispose()
        pdf_Render_Cts = Nothing
    End Sub

    Friend Async Sub DisplayPdfPage(pageNumber As Integer, Optional preserveZoom As Boolean = False)
        If Not IsPdfDocumentShown() Then Return
        CancelPdfRender()
        pdf_Render_Cts = New CancellationTokenSource(TimeSpan.FromSeconds(30))
        Dim token As CancellationToken = pdf_Render_Cts.Token
        Dim source As PdfDocumentSource = pdf_Current_Source
        Dim session As Integer = pdf_Session
        Dim generation As Integer = media_Generation
        pdf_Current_Page = Math.Max(1, Math.Min(pageNumber, pdf_Total_Pages))
        pageNumber = pdf_Current_Page
        CancelOcrJob()
        current_Overlay_Document = Nothing
        ocr_Auto_Timer.Stop()
        Dim factor As Double = If(preserveZoom, Math.Max(1.0, CurrentZoomFactor() / FitFactorFor(ActiveMediaImage())), 1.0)
        Dim width As Integer = Math.Min(PdfDocumentSource.MaxRenderDimension, Math.Max(1, CInt(panel_Media.ClientSize.Width * factor)))
        Dim height As Integer = Math.Min(PdfDocumentSource.MaxRenderDimension, Math.Max(1, CInt(panel_Media.ClientSize.Height * factor)))
        Dim oldWidth As Integer = If(ActiveMediaImage() Is Nothing, 0, ActiveMediaImage().Width)
        Dim oldFactor As Double = CurrentZoomFactor()
        If Not preserveZoom Then
            Dim box As PictureBox = GetActivePictureBox()
            If box IsNot Nothing AndAlso box.Image IsNot Nothing Then
                box.Image.Dispose()
                box.Image = Nothing
            End If
            ZoomToFit()
        End If
        pdf_Page_Error = ""
        If modern_Preferences IsNot Nothing AndAlso Not Is_slide_show_mode Then
            modern_Preferences.RecordDocumentPage(pdf_Current_File_Path, pdf_Write_Ticks, pdf_File_Length, pageNumber)
            MarkSettingsDirty()
        End If
        Dim rendered As Bitmap = Nothing
        Try
            If width <> pdf_Last_Render_Width OrElse height <> pdf_Last_Render_Height Then ClearPdfCache()
            pdf_Last_Render_Width = width
            pdf_Last_Render_Height = height
            If Not pdf_Page_Cache.TryGetValue(pageNumber, rendered) Then
                rendered = Await RenderPdfPageAsync(source, pageNumber, width, height, token)
                If IsDisposed OrElse session <> pdf_Session OrElse generation <> media_Generation OrElse token.IsCancellationRequested Then
                    rendered?.Dispose()
                    Return
                End If
                If rendered IsNot Nothing Then pdf_Page_Cache(pageNumber) = rendered
            End If
            If token.IsCancellationRequested Then Return
            Dim box As PictureBox = GetActivePictureBox()
            If rendered Is Nothing Then
                If box.Image IsNot Nothing Then box.Image.Dispose()
                box.Image = Nothing
                pdf_Page_Error = Localization.TF("{0} - ошибка отображения страницы {1}", Path.GetFileName(pdf_Current_File_Path), pageNumber)
                lbl_Status.Text = pdf_Page_Error
                ApplyTranslateButtonsVisibility()
                box.Invalidate()
                Return
            End If
            ' The box owns its copy: ordinary image disposal can never invalidate the cache.
            oldWidth = If(box.Image Is Nothing, 0, box.Image.Width)
            oldFactor = CurrentZoomFactor()
            Dim image As New Bitmap(rendered)
            Dim previous As Image = box.Image
            box.Image = image
            previous?.Dispose()
            box.SizeMode = PictureBoxSizeMode.Zoom
            If preserveZoom AndAlso oldWidth > 0 AndAlso Not MediaIsAtFit() Then
                zoom_Factor = oldFactor * oldWidth / image.Width
                Dim dest As Rectangle = Rectangle.Empty
                Dim src As Rectangle = Rectangle.Empty
                ZoomMath.ComputeViewport(current_ImageBounds, image.Width, image.Height, panel_Media.ClientSize, dest, src)
                CType(box, HqPictureBox).SourceRect = src
                CType(box, HqPictureBox).DestRect = dest
            Else
                ZoomToFit()
            End If
            Draw_Perspective()
            lbl_Status.Text = Localization.TF("{0} - страница {1} из {2}", Path.GetFileName(pdf_Current_File_Path), pageNumber, pdf_Total_Pages)
            UpdateContentWindowTitle()
            OnMediaDisplayed()
            box.Invalidate()
            Await PrefetchAdjacentPdfPages(source, session, generation, pageNumber, width, height, token)
        Catch ex As OperationCanceledException
            If Not IsDisposed AndAlso session = pdf_Session AndAlso generation = media_Generation AndAlso
                pdf_Render_Cts IsNot Nothing AndAlso pdf_Render_Cts.Token = token AndAlso ActiveMediaImage() Is Nothing Then
                pdf_Page_Error = Localization.TF("{0} - ошибка отображения страницы {1}", Path.GetFileName(pdf_Current_File_Path), pageNumber)
                lbl_Status.Text = pdf_Page_Error
                GetActivePictureBox()?.Invalidate()
            End If
        Catch ex As Exception
            If Not IsDisposed AndAlso session = pdf_Session AndAlso Not token.IsCancellationRequested Then
                pdf_Page_Error = Localization.TF("{0} - ошибка отображения страницы {1}", Path.GetFileName(pdf_Current_File_Path), pageNumber)
                lbl_Status.Text = pdf_Page_Error
                GetActivePictureBox()?.Invalidate()
                AppFileLogger.LogException("Rendering PDF page", ex)
            End If
        End Try
    End Sub

    Private Shared Async Function RenderPdfPageAsync(source As PdfDocumentSource, page As Integer,
                                                     width As Integer, height As Integer,
                                                     token As CancellationToken) As Task(Of Bitmap)
        Dim render As Task(Of Bitmap) = Task.Run(Function() source.RenderPage(page, width, height, token), token)
        Try
            ' A slow native call cannot delay the UI's timeout/refusal surface.
            Return Await render.WaitAsync(token)
        Catch ex As OperationCanceledException
            Dim cleanup As Task = render.ContinueWith(Sub(completed)
                If completed.Status = TaskStatus.RanToCompletion Then
                    completed.Result?.Dispose()
                ElseIf completed.IsFaulted Then
                    Dim observed As AggregateException = completed.Exception
                End If
            End Sub, TaskScheduler.Default)
            Throw
        End Try
    End Function

    Private Async Function PrefetchAdjacentPdfPages(source As PdfDocumentSource, session As Integer, generation As Integer,
                                                    page As Integer, width As Integer, height As Integer,
                                                    token As CancellationToken) As Task
        For Each adjacent As Integer In New Integer() {page - 1, page + 1}
            If adjacent < 1 OrElse adjacent > pdf_Total_Pages OrElse pdf_Page_Cache.ContainsKey(adjacent) Then Continue For
            Dim image As Bitmap = Await RenderPdfPageAsync(source, adjacent, width, height, token)
            If IsDisposed OrElse session <> pdf_Session OrElse generation <> media_Generation OrElse token.IsCancellationRequested Then
                image?.Dispose()
                Return
            End If
            If image IsNot Nothing Then pdf_Page_Cache(adjacent) = image
        Next
        For Each key As Integer In New List(Of Integer)(pdf_Page_Cache.Keys)
            If Math.Abs(key - page) <= 1 Then Continue For
            pdf_Page_Cache(key).Dispose()
            pdf_Page_Cache.Remove(key)
        Next
    End Function

    Private Sub ClearPdfCache()
        For Each image As Bitmap In pdf_Page_Cache.Values
            image.Dispose()
        Next
        pdf_Page_Cache.Clear()
    End Sub

    Private Sub RefreshPdfResolution()
        If Not IsPdfDocumentPageOnScreen() OrElse CurrentZoomFactor() <= 1.5 Then Return
        If pdf_Render_Cts IsNot Nothing AndAlso Not pdf_Render_Cts.IsCancellationRequested AndAlso
            pdf_Last_Render_Width > panel_Media.ClientSize.Width * CurrentZoomFactor() / FitFactorFor(ActiveMediaImage()) * 0.9 Then Return
        If pdf_Last_Render_Width >= PdfDocumentSource.MaxRenderDimension OrElse pdf_Last_Render_Height >= PdfDocumentSource.MaxRenderDimension Then Return
        DisplayPdfPage(pdf_Current_Page, preserveZoom:=True)
    End Sub

    Friend Sub PdfNextPage()
        If IsPdfDocumentShown() AndAlso pdf_Current_Page < pdf_Total_Pages Then DisplayPdfPage(pdf_Current_Page + 1)
    End Sub
    Friend Sub PdfPrevPage()
        If IsPdfDocumentShown() AndAlso pdf_Current_Page > 1 Then DisplayPdfPage(pdf_Current_Page - 1)
    End Sub
    Friend Sub PdfFirstPage()
        If IsPdfDocumentShown() Then DisplayPdfPage(1)
    End Sub
    Friend Sub PdfLastPage()
        If IsPdfDocumentShown() Then DisplayPdfPage(pdf_Total_Pages)
    End Sub

    Friend Sub ReleasePdfDocument()
        pdf_Session += 1
        CancelPdfRender()
        pdf_Current_File_Path = ""
        pdf_Current_Page = 1
        pdf_Total_Pages = 0
        pdf_Last_Render_Width = 0
        pdf_Last_Render_Height = 0
        pdf_Page_Error = ""
        pdf_Indicator_Bounds = Rectangle.Empty
        ClearPdfCache()
        pdf_Current_Source?.Dispose()
        pdf_Current_Source = Nothing
    End Sub

    Private Function TryHandlePdfIndicatorClick(box As PictureBox, location As Point) As Boolean
        If Not IsPdfDocumentShown() OrElse box IsNot GetActivePictureBox() OrElse Not pdf_Indicator_Bounds.Contains(location) Then Return False
        If location.X < pdf_Indicator_Bounds.Left + pdf_Indicator_Bounds.Width \ 2 Then
            PdfPrevPage()
        Else
            PdfNextPage()
        End If
        Return True
    End Function

    Friend Sub PaintPdfOverlay(box As PictureBox, e As PaintEventArgs)
        If Not IsPdfDocumentShown() OrElse box IsNot GetActivePictureBox() Then Return
        If pdf_Page_Error.Length > 0 Then
            e.Graphics.Clear(NeutralBackColor())
            TextRenderer.DrawText(e.Graphics, pdf_Page_Error, Me.Font, box.ClientRectangle,
                GetOppositeColor(NeutralBackColor()), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
        End If
        If pdf_Total_Pages <= 1 Then Return

        ' On-surface page indicator (3 / 48) in bottom-right corner (§3.3)
        Dim indicatorText As String = pdf_Current_Page.ToString() & " / " & pdf_Total_Pages.ToString()
        Dim scale As Single = GetCurrentDpiScale()
        Dim font As New Font(ScriptFonts.Family("Segoe UI"), Math.Max(9.0F, 11.0F * scale), FontStyle.Bold)
        Dim textSize As Size = TextRenderer.MeasureText(e.Graphics, indicatorText, font)

        Dim padX As Integer = CInt(10 * scale)
        Dim padY As Integer = CInt(6 * scale)
        Dim rectW As Integer = textSize.Width + padX * 2
        Dim rectH As Integer = textSize.Height + padY * 2
        Dim rectX As Integer = box.ClientSize.Width - rectW - CInt(16 * scale)
        Dim rectY As Integer = box.ClientSize.Height - rectH - CInt(16 * scale)

        If rectX < 0 OrElse rectY < 0 Then
            font.Dispose()
            Return
        End If

        Dim badgeRect As New Rectangle(rectX, rectY, rectW, rectH)
        pdf_Indicator_Bounds = badgeRect
        Using bgBrush As New SolidBrush(Color.FromArgb(170, 0, 0, 0)),
              borderPen As New Pen(Color.FromArgb(80, 255, 255, 255), 1.0F),
              textBrush As New SolidBrush(Color.White)

            e.Graphics.FillRectangle(bgBrush, badgeRect)
            e.Graphics.DrawRectangle(borderPen, badgeRect)

            Dim flags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix
            TextRenderer.DrawText(e.Graphics, indicatorText, font, badgeRect, Color.White, flags)
        End Using
        font.Dispose()
    End Sub

End Class
#End If
