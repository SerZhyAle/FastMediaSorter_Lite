#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Main_Form
    Private pdf_Reader_Pane As TabControl
    ''' <summary>The page the reader shows: what the page box of the document panel reads.</summary>
    Private pdf_Reader_ShownPage As Integer = 1
    Private pdf_Reader_Text As RichTextBox
    Private pdf_Reader_Query As TextBox
    Private pdf_Reader_Results As ListBox
    Private pdf_Reader_Outline As TreeView
    Private pdf_Reader_Bookmarks As ListBox
    ''' <summary>The context readout of the document panel: which keys work now, or the state of a search.</summary>
    Private pdf_Reader_ContextText As String = ""
    Private pdf_Reader_Info As PdfReaderInfo
    Private pdf_Reader_TextPage As PdfTextPage
    Private pdf_Reader_Cts As CancellationTokenSource
    Private pdf_Reader_SearchCts As CancellationTokenSource
    Private pdf_Reader_Interaction As Boolean
    Private pdf_Reader_SelectionStart As Integer = -1
    Private pdf_Reader_SelectionEnd As Integer = -1
    Private ReadOnly pdf_Reader_Back As New Stack(Of Integer)()
    Private pdf_Reader_Voice As DocumentSpeech
    Private pdf_Reading_Canvas As PdfReadingCanvas
    Private pdf_Reader_Selecting As Boolean
    Private pdf_Reader_PressedLink As PdfReaderLink
    Private pdf_Reader_SearchQuery As String = ""
    Private pdf_Reader_SearchCase As Boolean
    Private pdf_Reader_Preferences As ModernViewerPreferences
    Private ReadOnly pdf_Reader_SessionAppearance As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private pdf_Reader_Arranging As Boolean
    Private pdf_Reader_Thumbnails As ListBox
    Private ReadOnly pdf_Reader_ThumbnailCache As New Dictionary(Of Integer, Bitmap)()
    Private pdf_Reader_ThumbnailBusy As Boolean
    Private pdf_Reader_Restore As DocumentReadingPosition
    Private pdf_Reader_TextReadSerial As Integer

    Private Function CapturePdfReaderPosition() As DocumentReadingPosition
        Dim position As New DocumentReadingPosition With {.HasViewport = True, .PageNumber = pdf_Current_Page,
            .ZoomMode = PdfReaderPreferences().PdfZoom, .ZoomPercent = PdfReaderPreferences().PdfZoomPercent}
        If pdf_Reading_Canvas IsNot Nothing Then
            position.Viewport = pdf_Reading_Canvas.ViewportFraction
        ElseIf ActiveMediaImage() IsNot Nothing AndAlso Not MediaIsAtFit() Then
            position.Viewport = Math.Max(0, Math.Min(1, (PdfReaderViewport().Top - current_ImageBounds.Top) / CDbl(Math.Max(1, current_ImageBounds.Height))))
            position.ZoomMode = "manual"
            Dim points = If(pdf_Reader_TextPage Is Nothing, ActiveMediaImage().Width, pdf_Reader_TextPage.Size.Width)
            position.ZoomPercent = CInt(100 * ActiveMediaImage().Width * CurrentZoomFactor() / (Math.Max(1, points) * DeviceDpi / 72.0))
        End If
        position.Normalize()
        Return position
    End Function

    Private Async Sub ReadPdfThumbnail(page As Integer)
        If pdf_Reader_ThumbnailBusy OrElse Not FullPdfReaderActive() Then Return
        pdf_Reader_ThumbnailBusy = True
        Dim token = pdf_Reader_Cts.Token
        Dim session = pdf_Session
        Dim source = pdf_Current_Source
        Dim image As Bitmap = Nothing
        Try
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(token)
                deadline.CancelAfter(TimeSpan.FromSeconds(30))
                image = Await RenderPdfPageAsync(source, page, 150, 200, deadline.Token, annotations:=True)
            End Using
            If session <> pdf_Session OrElse token.IsCancellationRequested OrElse Not FullPdfReaderActive() Then Return
            If image IsNot Nothing Then
                While pdf_Reader_ThumbnailCache.Count >= 16
                    Dim oldest = pdf_Reader_ThumbnailCache.Keys.OrderByDescending(Function(number) Math.Abs(number - page)).First()
                    pdf_Reader_ThumbnailCache(oldest).Dispose()
                    pdf_Reader_ThumbnailCache.Remove(oldest)
                End While
                pdf_Reader_ThumbnailCache(page) = image
                image = Nothing
            End If
        Catch ex As OperationCanceledException
        Catch ex As Exception
        Finally
            image?.Dispose()
            If session = pdf_Session Then
                pdf_Reader_ThumbnailBusy = False
                pdf_Reader_Thumbnails?.Invalidate()
            End If
        End Try
    End Sub

    Private Sub DrawPdfThumbnail(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 OrElse Not FullPdfReaderActive() Then Return
        e.DrawBackground()
        Dim page = e.Index + 1
        Dim image As Bitmap = Nothing
        Dim labelRect = e.Bounds
        labelRect.Y = labelRect.Bottom - Font.Height - 4
        labelRect.Height = Font.Height + 4
        TextRenderer.DrawText(e.Graphics, Localization.TF("Страница {0}", page), Font, labelRect, e.ForeColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        If pdf_Reader_ThumbnailCache.TryGetValue(page, image) Then
            Dim area = New Size(e.Bounds.Width - 8, e.Bounds.Height - labelRect.Height - 8)
            Dim rect = ZoomMath.FitBounds(image.Width, image.Height, area)
            rect.Offset(e.Bounds.Left + 4, e.Bounds.Top + 4)
            PdfReadingCanvas.DrawPage(e.Graphics, image, rect, PdfReaderPreferences().PdfColorMode)
        ElseIf Not pdf_Reader_ThumbnailBusy Then
            ReadPdfThumbnail(page)
        End If
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>The page fills the whole media area beside the side pane: the document panel floats over it and takes no room.</summary>
    Private Function PdfReaderViewport() As Rectangle
        Dim rect = panel_Media.ClientRectangle
        If Not FullPdfReaderActive() Then Return rect
        Dim left = If(pdf_Reader_Pane.Visible, pdf_Reader_Pane.Width, 0)
        Return New Rectangle(left, 0, Math.Max(1, rect.Width - left), Math.Max(1, rect.Height))
    End Function

    Private Sub LayoutPdfReader(sender As Object, e As EventArgs)
        If Not FullPdfReaderActive() OrElse pdf_Reader_Arranging Then Return
        pdf_Reader_Arranging = True
        Try
            Dim rect = PdfReaderViewport()
            ' The pane starts below the lowest pinned top panel so its tabs are never hidden (280 Q280-1 default).
            document_Pane_Top = DocumentPaneTop()
            Dim paneTop = Math.Min(document_Pane_Top, Math.Max(0, rect.Height - 1))
            pdf_Reader_Pane.Bounds = New Rectangle(0, paneTop, pdf_Reader_Pane.Width, Math.Max(1, rect.Height - paneTop))
            If pdf_Reading_Canvas IsNot Nothing Then pdf_Reading_Canvas.Bounds = rect
            ApplyPdfReaderFitViewport()
        Finally
            pdf_Reader_Arranging = False
        End Try
    End Sub

    Private Sub ApplyPdfReaderFitViewport()
        If Not FullPdfReaderActive() OrElse pdf_Reading_Canvas IsNot Nothing OrElse Not MediaIsAtFit() Then Return
        Dim image = ActiveMediaImage()
        Dim box = TryCast(GetActivePictureBox(), HqPictureBox)
        If image Is Nothing OrElse box Is Nothing Then Return
        box.SourceRect = New Rectangle(Point.Empty, image.Size)
        box.DestRect = DisplayedImageRectangleOnPanel(image)
        box.Invalidate()
    End Sub

    Private Sub ApplyPdfReaderSavedZoom()
        If Not FullPdfReaderActive() OrElse pdf_Reading_Canvas IsNot Nothing OrElse PdfReaderPreferences().PdfZoom = "fitPage" Then Return
        Dim image = ActiveMediaImage()
        If image Is Nothing Then Return
        Dim rect = PdfReaderViewport()
        Dim points = If(pdf_Reader_TextPage Is Nothing, New SizeF(image.Width, image.Height), pdf_Reader_TextPage.Size)
        Dim factor = If(PdfReaderPreferences().PdfZoom = "fitWidth", rect.Width / CDbl(image.Width),
            points.Width * DeviceDpi / 72.0 / image.Width * If(PdfReaderPreferences().PdfZoom = "manual", PdfReaderPreferences().PdfZoomPercent / 100.0, 1))
        Dim width = CInt(image.Width * factor)
        Dim height = CInt(image.Height * factor)
        Dim offset As Integer
        If pdf_Reader_Restore IsNot Nothing Then offset = Math.Max(0, Math.Min(Math.Max(0, height - rect.Height), CInt(height * pdf_Reader_Restore.Viewport)))
        SetFreeViewport(New Rectangle(rect.Left + (rect.Width - width) \ 2, rect.Top - offset, width, height), factor, image)
    End Sub

    Private Function PdfReaderPreferences() As ModernViewerPreferences
        Return If(pdf_Reader_Preferences, modern_Preferences)
    End Function

    Private Sub UpdatePdfAppearance()
        pdf_Reader_Preferences = modern_Preferences.EffectiveDocumentPreferences(PdfReaderIdentity())
        For Each entry In pdf_Reader_SessionAppearance
            Dim prop = GetType(ModernViewerPreferences).GetProperty(entry.Key)
            prop.SetValue(pdf_Reader_Preferences, Convert.ChangeType(entry.Value, prop.PropertyType, Globalization.CultureInfo.InvariantCulture))
        Next
        pdf_Reader_Preferences.NormalizeDocumentPreferences()
    End Sub

    ''' <summary>The stored value of an appearance choice (<c>PdfLayout</c>, <c>PdfColorMode</c>) for the document on screen.</summary>
    Private Function PdfAppearanceValue(key As String) As String
        Dim prop = GetType(ModernViewerPreferences).GetProperty(key)
        Return CStr(prop.GetValue(PdfReaderPreferences()))
    End Function

    ''' <summary>The layout and colour-mode menu buttons: the item shows the current choice, reserves the width of the widest, and its menu
    ''' lists the choices with the current one checked (the sort button's route).</summary>
    Private Function PdfAppearanceCommand(key As String, id As String) As DocumentCommand
        Dim choices = DocumentPanelRules.MenuChoices(id)
        Return New DocumentCommand With {
            .Text = Function() PdfAppearanceCaption(key, choices),
            .Reserved = Function() choices.Select(Function(choice) Localization.T(choice.Value)).ToArray(),
            .Menu = Function() PdfAppearanceMenu(key, choices)}
    End Function

    Private Function PdfAppearanceCaption(key As String, choices As KeyValuePair(Of String, String)()) As String
        Dim current = PdfAppearanceValue(key)
        For Each choice In choices
            If choice.Key = current Then Return Localization.T(choice.Value)
        Next
        Return Localization.T(choices(0).Value)
    End Function

    Private Function PdfAppearanceMenu(key As String, choices As KeyValuePair(Of String, String)()) As IEnumerable(Of ChromeMenuItem)
        Dim current = PdfAppearanceValue(key)
        Dim entries As New List(Of ChromeMenuItem)()
        For Each choice In choices
            Dim value As String = choice.Key
            entries.Add(New ChromeMenuItem(key & "_" & value) With {
                .Caption = Localization.T(choice.Value), .Checked = value = current,
                .Action = Sub() ApplyPdfAppearance(key, value)})
        Next
        Return entries
    End Function

    Private Sub ApplyPdfAppearance(key As String, value As String)
        If Not FullPdfReaderActive() Then Return
        pdf_Reader_SessionAppearance(key) = value
        If modern_Preferences.RememberDocumentAppearance Then modern_Preferences.OverrideDocumentAppearance(PdfReaderIdentity(), key, value)
        UpdatePdfAppearance()
        ConfigurePdfReadingCanvas()
        ClearPdfCache()
        DisplayPdfPage(pdf_Current_Page)
        MarkSettingsDirty()
        SyncDocumentPanel()
    End Sub

    Private Sub ResetPdfAppearance()
        If Not FullPdfReaderActive() Then Return
        modern_Preferences.ClearDocumentAppearance(PdfReaderIdentity())
        pdf_Reader_SessionAppearance.Clear()
        UpdatePdfAppearance()
        ConfigurePdfReadingCanvas()
        ClearPdfCache()
        DisplayPdfPage(pdf_Current_Page)
        MarkSettingsDirty()
        SyncDocumentPanel()
    End Sub

    Private Sub RotatePdfReaderPage()
        If Not FullPdfReaderActive() Then Return
        pdf_Current_Source.RotatePageClockwise(pdf_Current_Page)
        CancelPdfRender()
        ClearPdfCache()
        For Each image In pdf_Reader_ThumbnailCache.Values
            image.Dispose()
        Next
        pdf_Reader_ThumbnailCache.Clear()
        ConfigurePdfReadingCanvas()
        DisplayPdfPage(pdf_Current_Page)
    End Sub

    Private Sub PdfReaderGoBack()
        If pdf_Reader_Back.Count > 0 Then DisplayPdfPage(pdf_Reader_Back.Pop())
    End Sub

    ''' <summary>What the PDF reader puts on the document panel, by item id (the order is the panel's: <see cref="DocumentPanelRules"/>).
    ''' Previous / next file and the settings button are not here - the rows carry them.</summary>
    Private Function PdfReaderCommands() As Dictionary(Of String, DocumentCommand)
        Dim commands As New Dictionary(Of String, DocumentCommand)(StringComparer.Ordinal)
        commands(DocumentPanelRules.IdBack) = New DocumentCommand With {.Action = AddressOf PdfReaderGoBack}
        commands(DocumentPanelRules.IdPrevPage) = New DocumentCommand With {.Action = AddressOf PdfPrevPage}
        commands(DocumentPanelRules.IdPagePosition) = New DocumentCommand With {
            .Action = Sub() BeginDocumentPositionEdit(DocumentPanelRules.IdPagePosition, pdf_Reader_ShownPage, pdf_Total_Pages, Sub(page) DisplayPdfPage(page)),
            .Text = Function() DocumentPanelRules.PositionReadout(pdf_Reader_ShownPage, pdf_Total_Pages, DocumentPanelRules.PositionReadoutFormat),
            .Reserved = Function() New String() {DocumentPanelRules.PositionReadoutReserved(pdf_Total_Pages, DocumentPanelRules.PositionReadoutFormat)}}
        commands(DocumentPanelRules.IdNextPage) = New DocumentCommand With {.Action = AddressOf PdfNextPage}
        For Each pane As KeyValuePair(Of String, Integer) In New KeyValuePair(Of String, Integer)() {
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneOutline, 0),
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneSearch, 1),
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneText, 2),
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneBookmarks, 3),
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneThumbnails, 4)}
            Dim index As Integer = pane.Value
            commands(pane.Key) = New DocumentCommand With {
                .Action = Sub() TogglePdfReaderPane(index),
                .IsChecked = Function() pdf_Reader_Pane IsNot Nothing AndAlso pdf_Reader_Pane.Visible AndAlso pdf_Reader_Pane.SelectedIndex = index}
        Next
        commands(DocumentPanelRules.IdAddBookmark) = New DocumentCommand With {.Action = AddressOf AddPdfReaderBookmark}
        commands(DocumentPanelRules.IdSpeak) = New DocumentCommand With {.Action = AddressOf SpeakPdfReader}
        commands(DocumentPanelRules.IdStopSpeech) = New DocumentCommand With {.Action = Sub() pdf_Reader_Voice?.Stop()}
        commands(DocumentPanelRules.IdRotate) = New DocumentCommand With {.Action = AddressOf RotatePdfReaderPage}
        commands(DocumentPanelRules.IdLayout) = PdfAppearanceCommand("PdfLayout", DocumentPanelRules.IdLayout)
        commands(DocumentPanelRules.IdColorMode) = PdfAppearanceCommand("PdfColorMode", DocumentPanelRules.IdColorMode)
        commands(DocumentPanelRules.IdResetAppearance) = New DocumentCommand With {.Action = AddressOf ResetPdfAppearance}
        commands(DocumentPanelRules.IdOpenWith) = New DocumentCommand With {.Action = Sub() ManagerShellOpen.OpenWithPicker(Handle, pdf_Current_File_Path)}
        commands(DocumentPanelRules.IdTranslate) = New DocumentCommand With {.Action = Sub() OpenDocumentConversion(pdf_Current_File_Path)}
        commands(DocumentPanelRules.IdContext) = New DocumentCommand With {
            .Text = Function() pdf_Reader_ContextText,
            .Reserved = Function() New String() {Localization.T("Просмотр: клавиши сортируют файлы"), Localization.T("Документ: Esc возвращает сортировку")}}
        Return commands
    End Function

    Private Function PdfReaderPoint(box As PictureBox, location As Point) As PointF
        Dim rect = DisplayedImageRectangleOnPanel(ActiveMediaImage())
        If rect.Width <= 0 OrElse rect.Height <= 0 OrElse pdf_Reader_TextPage Is Nothing Then Return New PointF(-1, -1)
        Dim point = panel_Media.PointToClient(box.PointToScreen(location))
        Return New PointF(CSng((point.X - rect.X) * pdf_Reader_TextPage.Size.Width / rect.Width), CSng((point.Y - rect.Y) * pdf_Reader_TextPage.Size.Height / rect.Height))
    End Function

    Private Function PdfReaderMouseDown(box As PictureBox, e As MouseEventArgs) As Boolean
        If Not FullPdfReaderActive() OrElse box IsNot GetActivePictureBox() OrElse e.Button <> MouseButtons.Left OrElse pdf_Reader_TextPage Is Nothing Then Return False
        pending_Single_Click_Timer.Stop()
        pending_Single_Click_Event = Nothing
        SlideShowStop()
        pdf_Reader_Interaction = True
        pdf_Reader_Selecting = True
        Dim point = PdfReaderPoint(box, e.Location)
        pdf_Reader_PressedLink = pdf_Reader_TextPage.Links.FirstOrDefault(Function(link) link.Bounds.Contains(point))
        pdf_Reader_SelectionStart = pdf_Reader_TextPage.Boxes.FindIndex(Function(rect) rect.Contains(point))
        pdf_Reader_SelectionEnd = pdf_Reader_SelectionStart
        box.Capture = True
        box.Invalidate()
        UpdatePdfReaderContext()
        Return True
    End Function

    Private Function PdfReaderMouseMove(box As PictureBox, e As MouseEventArgs) As Boolean
        If Not FullPdfReaderActive() OrElse Not pdf_Reader_Selecting OrElse pdf_Reader_TextPage Is Nothing Then Return False
        Dim point = PdfReaderPoint(box, e.Location)
        Dim index = pdf_Reader_TextPage.Boxes.FindIndex(Function(rect) rect.Contains(point))
        If index >= 0 Then pdf_Reader_SelectionEnd = index
        box.Invalidate()
        Return True
    End Function

    Private Function PdfReaderMouseUp(box As PictureBox, e As MouseEventArgs) As Boolean
        If Not pdf_Reader_Selecting Then Return False
        pdf_Reader_Selecting = False
        box.Capture = False
        Dim link = pdf_Reader_PressedLink
        pdf_Reader_PressedLink = Nothing
        If link IsNot Nothing AndAlso pdf_Reader_SelectionStart = pdf_Reader_SelectionEnd AndAlso link.Bounds.Contains(PdfReaderPoint(box, e.Location)) Then
            OpenPdfReaderLink(link)
        End If
        Return True
    End Function

    Private Sub OpenPdfReaderLink(link As PdfReaderLink)
            If link.Page > 0 Then
                NavigatePdfReaderLink(link.Page)
            Else
                Dim target As Uri = Nothing
                If Uri.TryCreate(link.Uri, UriKind.Absolute, target) AndAlso (target.Scheme = Uri.UriSchemeHttps OrElse target.Scheme = Uri.UriSchemeHttp) Then
                    Try
                        Process.Start(New ProcessStartInfo(target.AbsoluteUri) With {.UseShellExecute = True})
                    Catch ex As Exception
                        Notify(Localization.T("Не удалось открыть PDF"), NotifyLevel.Warning)
                    End Try
                End If
            End If
    End Sub

    Private Sub PaintPdfReaderSelection(box As PictureBox, drawing As Graphics)
        If Not FullPdfReaderActive() OrElse pdf_Reader_TextPage Is Nothing OrElse pdf_Reader_SelectionStart < 0 OrElse pdf_Reader_SelectionEnd < 0 Then Return
        Dim rect = DisplayedImageRectangleOnPanel(ActiveMediaImage())
        If rect.IsEmpty Then Return
        Dim start = Math.Min(pdf_Reader_SelectionStart, pdf_Reader_SelectionEnd)
        Dim finish = Math.Min(pdf_Reader_TextPage.Boxes.Count - 1, Math.Max(pdf_Reader_SelectionStart, pdf_Reader_SelectionEnd))
        Using brush As New SolidBrush(Color.FromArgb(90, SystemColors.Highlight))
            For index = start To finish
                Dim glyph = pdf_Reader_TextPage.Boxes(index)
                Dim topLeft = box.PointToClient(panel_Media.PointToScreen(New Point(CInt(rect.Left + glyph.Left * rect.Width / pdf_Reader_TextPage.Size.Width), CInt(rect.Top + glyph.Top * rect.Height / pdf_Reader_TextPage.Size.Height))))
                drawing.FillRectangle(brush, New RectangleF(topLeft.X, topLeft.Y, glyph.Width * rect.Width / pdf_Reader_TextPage.Size.Width, glyph.Height * rect.Height / pdf_Reader_TextPage.Size.Height))
            Next
        End Using
    End Sub

    Private Function FullPdfReaderActive() As Boolean
        Return IsPdfDocumentShown() AndAlso pdf_Reader_Pane IsNot Nothing AndAlso Not pdf_Reader_Pane.IsDisposed
    End Function

    Private Function PdfReaderIdentity() As DocumentIdentity
        Return DocumentReaderIdentity(pdf_Current_File_Path, pdf_Write_Ticks, pdf_File_Length)
    End Function

    Friend Sub ApplyDocumentReaderPreferences(previous As ModernViewerPreferences)
        ApplyEpubReaderPreferences()
        ApplyHtmlViewPreferences(previous)
        If previous IsNot Nothing AndAlso Not previous.FullEpubReader AndAlso modern_Preferences.FullEpubReader AndAlso IO.Path.GetExtension(Current_File_Name).Equals(".epub", StringComparison.OrdinalIgnoreCase) Then LoadEpubReader(Current_File_Name)
        If previous IsNot Nothing AndAlso previous.FullPdfReader <> modern_Preferences.FullPdfReader AndAlso pdf_Current_File_Path.Length > 0 Then
            Dim path = pdf_Current_File_Path
            ReleasePdfDocument()
            LoadPdfDocument(path)
            Return
        End If
        If Not IsPdfDocumentShown() Then Return
        If FullPdfReaderActive() Then
            Dim position = CapturePdfReaderPosition()
            Dim oldAppearance = PdfReaderPreferences()
            UpdatePdfAppearance()
            If Not modern_Preferences.DocumentTtsEnabled Then pdf_Reader_Voice?.Stop()
            pdf_Reader_Pane.Width = CInt(PdfReaderPreferences().PdfPanelWidth * GetCurrentDpiScale())
            LayoutPdfReader(Me, EventArgs.Empty)
            Dim zoomChanged = oldAppearance.PdfZoom <> PdfReaderPreferences().PdfZoom OrElse oldAppearance.PdfZoomPercent <> PdfReaderPreferences().PdfZoomPercent
            Dim renderingChanged = oldAppearance.PdfLayout <> PdfReaderPreferences().PdfLayout OrElse zoomChanged OrElse
                oldAppearance.PdfZoomPercent <> PdfReaderPreferences().PdfZoomPercent OrElse oldAppearance.PdfFirstPageAlone <> PdfReaderPreferences().PdfFirstPageAlone OrElse
                oldAppearance.PdfSpreadDirection <> PdfReaderPreferences().PdfSpreadDirection OrElse oldAppearance.DocumentMemoryCacheMb <> modern_Preferences.DocumentMemoryCacheMb OrElse
                oldAppearance.PdfColorMode <> PdfReaderPreferences().PdfColorMode OrElse oldAppearance.PdfRenderQuality <> PdfReaderPreferences().PdfRenderQuality OrElse
                oldAppearance.PdfRenderCacheMb <> PdfReaderPreferences().PdfRenderCacheMb OrElse oldAppearance.PdfPrefetchPages <> PdfReaderPreferences().PdfPrefetchPages
            If Not renderingChanged Then Return
            pdf_Reader_Restore = position
            ConfigurePdfReadingCanvas()
            ClearPdfCache()
            If pdf_Reading_Canvas Is Nothing Then
                DisplayPdfPage(pdf_Current_Page, preserveZoom:=Not zoomChanged)
            End If
        End If
    End Sub

    Private Function PdfReaderSourceUnchanged() As Boolean
        Try
            Dim info As New FileInfo(pdf_Current_File_Path)
            Return info.Exists AndAlso info.Length = pdf_File_Length AndAlso info.LastWriteTimeUtc.Ticks = pdf_Write_Ticks
        Catch ex As IOException
            Return False
        Catch ex As UnauthorizedAccessException
            Return False
        End Try
    End Function

    Private Sub OpenPdfReaderUi()
        UpdatePdfAppearance()
        If pdf_Reader_Restore IsNot Nothing AndAlso pdf_Reader_Restore.HasViewport Then
            pdf_Reader_Preferences.PdfZoom = pdf_Reader_Restore.ZoomMode
            pdf_Reader_Preferences.PdfZoomPercent = pdf_Reader_Restore.ZoomPercent
        End If
        pdf_Reader_Cts = New CancellationTokenSource()
        pdf_Reader_ShownPage = pdf_Current_Page
        pdf_Reader_ContextText = Localization.T("Просмотр: клавиши сортируют файлы")
        pdf_Reader_Pane = New TabControl With {.Dock = DockStyle.None, .Width = CInt(PdfReaderPreferences().PdfPanelWidth * GetCurrentDpiScale()), .Visible = False}
        For Each title In {"Оглавление", "Поиск", "Текст", "Закладки", "Миниатюры страниц"}
            pdf_Reader_Pane.TabPages.Add(New TabPage(Localization.T(title)))
        Next
        pdf_Reader_Outline = New TreeView With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Оглавление")}
        pdf_Reader_Pane.TabPages(0).Controls.Add(pdf_Reader_Outline)
        AddHandler pdf_Reader_Outline.AfterSelect, Sub(sender, e)
                                                     Dim page = TryCast(e.Node.Tag, PdfOutlineItem)
                                                     If page IsNot Nothing AndAlso page.Page > 0 Then NavigatePdfReaderLink(page.Page)
                                                 End Sub
        Dim searchPanel As New Panel With {.Dock = DockStyle.Fill}
        pdf_Reader_Query = New TextBox With {.Dock = DockStyle.Top, .AccessibleName = Localization.T("Поиск в документе")}
        Dim search As New Button With {.Dock = DockStyle.Top, .Tag = "Найти", .Text = Localization.T("Найти")}
        Dim cancelSearch As New Button With {.Dock = DockStyle.Bottom, .Tag = "Отмена", .Text = Localization.T("Отмена")}
        pdf_Reader_Results = New ListBox With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Результаты поиска")}
        searchPanel.Controls.AddRange({pdf_Reader_Results, search, pdf_Reader_Query, cancelSearch})
        pdf_Reader_Pane.TabPages(1).Controls.Add(searchPanel)
        AddHandler search.Click, Sub() SearchPdfReader()
        AddHandler cancelSearch.Click, Sub() pdf_Reader_SearchCts?.Cancel()
        AddHandler pdf_Reader_Query.KeyDown, Sub(sender, e)
                                                If e.KeyCode = Keys.Enter Then
                                                    SearchPdfReader()
                                                    e.SuppressKeyPress = True
                                                End If
                                            End Sub
        AddHandler pdf_Reader_Results.SelectedIndexChanged, Sub()
                                                               Dim hit = TryCast(pdf_Reader_Results.SelectedItem, PdfSearchHit)
                                                               If hit IsNot Nothing Then NavigatePdfReaderLink(hit.Page)
                                                           End Sub
        pdf_Reader_Text = New RichTextBox With {.Dock = DockStyle.Fill, .ReadOnly = True, .DetectUrls = False,
            .AccessibleName = Localization.T("Текст страницы"), .HideSelection = False, .ShortcutsEnabled = False}
        AddHandler pdf_Reader_Text.KeyDown, Sub(sender, e)
                                               If e.KeyData = (Keys.Control Or Keys.C) Then
                                                   If pdf_Reader_Info IsNot Nothing AndAlso pdf_Reader_Info.CanCopy AndAlso pdf_Reader_Text.SelectionLength > 0 Then pdf_Reader_Text.Copy()
                                                   e.SuppressKeyPress = True
                                               End If
                                           End Sub
        pdf_Reader_Pane.TabPages(2).Controls.Add(pdf_Reader_Text)
        pdf_Reader_Bookmarks = New ListBox With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Закладки")}
        pdf_Reader_Pane.TabPages(3).Controls.Add(pdf_Reader_Bookmarks)
        Dim renameBookmark As New Button With {.Dock = DockStyle.Bottom, .Text = Localization.T("Переименовать")}
        AddHandler renameBookmark.Click, Sub() RenamePdfBookmark()
        Dim removeBookmark As New Button With {.Dock = DockStyle.Bottom, .Text = Localization.T("Удалить")}
        AddHandler removeBookmark.Click, Sub() RemovePdfReaderBookmark()
        pdf_Reader_Pane.TabPages(3).Controls.Add(removeBookmark)
        pdf_Reader_Pane.TabPages(3).Controls.Add(renameBookmark)
        AddHandler pdf_Reader_Bookmarks.SelectedIndexChanged, Sub()
                                                                 Dim item = TryCast(pdf_Reader_Bookmarks.SelectedItem, PdfBookmarkDisplay)
                                                                 If item IsNot Nothing Then NavigatePdfReaderPosition(item.Bookmark.Position)
                                                             End Sub
        AddHandler pdf_Reader_Bookmarks.KeyDown, Sub(sender, e)
                                                    If e.KeyCode = Keys.F2 Then
                                                        RenamePdfBookmark()
                                                        e.SuppressKeyPress = True
                                                        Return
                                                    End If
                                                    If e.KeyCode <> Keys.Delete Then Return
                                                    Dim item = TryCast(pdf_Reader_Bookmarks.SelectedItem, PdfBookmarkDisplay)
                                                    If item Is Nothing Then Return
                                                    Dim entries = modern_Preferences.ReadDocumentBookmarks()
                                                    entries.RemoveAll(Function(bookmark) bookmark.Id = item.Bookmark.Id)
                                                    modern_Preferences.DocumentBookmarks = JsonSerializer.Serialize(entries)
                                                    MarkSettingsDirty()
                                                    RefreshPdfBookmarks()
                                                    e.SuppressKeyPress = True
                                                End Sub
        pdf_Reader_Thumbnails = New ListBox With {.Dock = DockStyle.Fill, .DrawMode = DrawMode.OwnerDrawFixed,
            .ItemHeight = Math.Min(255, CInt(180 * GetCurrentDpiScale())), .AccessibleName = Localization.T("Миниатюры страниц")}
        pdf_Reader_Thumbnails.BeginUpdate()
        For page = 1 To pdf_Total_Pages
            pdf_Reader_Thumbnails.Items.Add(page)
        Next
        pdf_Reader_Thumbnails.EndUpdate()
        AddHandler pdf_Reader_Thumbnails.DrawItem, AddressOf DrawPdfThumbnail
        AddHandler pdf_Reader_Thumbnails.SelectedIndexChanged, Sub()
            If pdf_Reader_Thumbnails.SelectedIndex >= 0 Then NavigatePdfReaderLink(pdf_Reader_Thumbnails.SelectedIndex + 1)
        End Sub
        pdf_Reader_Pane.TabPages(4).Controls.Add(pdf_Reader_Thumbnails)
        panel_Media.Controls.Add(pdf_Reader_Pane)
        pdf_Reader_Pane.BringToFront()
        RefreshPdfBookmarks()
        ReadPdfReaderInfo()
        ConfigurePdfReadingCanvas()
        ApplyPdfReaderTheme(ThemeService.Palette)
        AddHandler panel_Media.SizeChanged, AddressOf LayoutPdfReader
        AddHandler pdf_Reader_Pane.VisibleChanged, Sub()
                                                      LayoutPdfReader(Me, EventArgs.Empty)
                                                      SyncDocumentPanel()
                                                  End Sub
        AddHandler pdf_Reader_Pane.SelectedIndexChanged, Sub() SyncDocumentPanel()
        AddHandler pdf_Reader_Pane.Enter, Sub() UpdatePdfReaderContext()
        LayoutPdfReader(Me, EventArgs.Empty)
        ShowDocumentCommands(DocumentReaderKind.Pdf, PdfReaderCommands())
        If PdfReaderPreferences().PdfThumbnailPanel Then TogglePdfReaderPane(4)
    End Sub

    Private Sub ConfigurePdfReadingCanvas()
        pdf_Reading_Canvas?.Dispose()
        pdf_Reading_Canvas = Nothing
        If PdfReaderPreferences().PdfLayout = "single" Then Return
        pdf_Reading_Canvas = New PdfReadingCanvas(pdf_Current_Source, PdfReaderPreferences())
        pdf_Reading_Canvas.DocumentRightToLeft = pdf_Reader_Info IsNot Nothing AndAlso pdf_Reader_Info.RightToLeft
        pdf_Reading_Canvas.Dock = DockStyle.None
        AddHandler pdf_Reading_Canvas.PageMouseDown, Sub(page, point)
            SlideShowStop()
            pdf_Reader_Interaction = True
            UpdatePdfReaderContext()
            If pdf_Reader_TextPage Is Nothing OrElse page <> pdf_Current_Page Then
                pdf_Current_Page = page
                ReadPdfReaderPage(page)
                Return
            End If
            pdf_Reader_Selecting = True
            pdf_Reader_PressedLink = pdf_Reader_TextPage.Links.FirstOrDefault(Function(link) link.Bounds.Contains(point))
            pdf_Reader_SelectionStart = pdf_Reader_TextPage.Boxes.FindIndex(Function(rect) rect.Contains(point))
            pdf_Reader_SelectionEnd = pdf_Reader_SelectionStart
            pdf_Reading_Canvas.Invalidate()
        End Sub
        AddHandler pdf_Reading_Canvas.PageMouseMove, Sub(page, point)
            If Not pdf_Reader_Selecting OrElse page <> pdf_Current_Page OrElse pdf_Reader_TextPage Is Nothing Then Return
            Dim index = pdf_Reader_TextPage.Boxes.FindIndex(Function(rect) rect.Contains(point))
            If index >= 0 Then pdf_Reader_SelectionEnd = index
            pdf_Reading_Canvas.Invalidate()
        End Sub
        AddHandler pdf_Reading_Canvas.PageMouseUp, Sub(page, point)
            pdf_Reader_Selecting = False
            Dim link = pdf_Reader_PressedLink
            pdf_Reader_PressedLink = Nothing
            If link IsNot Nothing AndAlso page = pdf_Current_Page AndAlso pdf_Reader_SelectionStart = pdf_Reader_SelectionEnd AndAlso link.Bounds.Contains(point) Then OpenPdfReaderLink(link)
        End Sub
        AddHandler pdf_Reading_Canvas.PageOverlay, Sub(page, graphics, rect)
            If page <> pdf_Current_Page OrElse pdf_Reader_TextPage Is Nothing OrElse pdf_Reader_SelectionStart < 0 OrElse pdf_Reader_SelectionEnd < 0 Then Return
            Using brush As New SolidBrush(Color.FromArgb(90, SystemColors.Highlight))
                For index = Math.Min(pdf_Reader_SelectionStart, pdf_Reader_SelectionEnd) To Math.Min(pdf_Reader_TextPage.Boxes.Count - 1, Math.Max(pdf_Reader_SelectionStart, pdf_Reader_SelectionEnd))
                    Dim glyph = pdf_Reader_TextPage.Boxes(index)
                    graphics.FillRectangle(brush, New RectangleF(rect.Left + glyph.Left * rect.Width / pdf_Reader_TextPage.Size.Width, rect.Top + glyph.Top * rect.Height / pdf_Reader_TextPage.Size.Height,
                        glyph.Width * rect.Width / pdf_Reader_TextPage.Size.Width, glyph.Height * rect.Height / pdf_Reader_TextPage.Size.Height))
                Next
            End Using
        End Sub
        AddHandler pdf_Reading_Canvas.PageChanged, Sub(page)
            If pdf_Current_Page = page Then Return
            pdf_Current_Page = page
            ReadPdfReaderPage(page)
            If Not Is_slide_show_mode Then
                modern_Preferences.RecordDocumentPosition(PdfReaderIdentity(), CapturePdfReaderPosition())
                MarkSettingsDirty()
            End If
        End Sub
        panel_Media.Controls.Add(pdf_Reading_Canvas)
        pdf_Reading_Canvas.BringToFront()
        pdf_Reader_Pane.BringToFront()
        pdf_Reading_Canvas.GoToPage(pdf_Current_Page, If(pdf_Reader_Restore Is Nothing, 0, pdf_Reader_Restore.Viewport))
        LayoutPdfReader(Me, EventArgs.Empty)
    End Sub

    Private Sub ApplyPdfReaderTheme(palette As ThemePalette)
        ThemeApplier.ApplyTree(pdf_Reader_Pane, palette)
        If pdf_Reader_Pane IsNot Nothing Then ThemeApplier.ApplyNativeTree(pdf_Reader_Pane, palette.IsDark)
    End Sub

    Private Sub LocalizePdfReader()
        If Not FullPdfReaderActive() Then Return
        pdf_Reader_Pane.RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        Dim captions = {"Оглавление", "Поиск", "Текст", "Закладки", "Миниатюры страниц"}
        For index = 0 To captions.Length - 1
            pdf_Reader_Pane.TabPages(index).Text = Localization.T(captions(index))
        Next
        Dim localizeControls As Action(Of Control) = Nothing
        localizeControls = Sub(parent)
            For Each child As Control In parent.Controls
                If TypeOf child.Tag Is String Then child.Text = Localization.T(CStr(child.Tag))
                localizeControls(child)
            Next
        End Sub
        localizeControls(pdf_Reader_Pane)
        pdf_Reader_Query.AccessibleName = Localization.T("Поиск в документе")
        pdf_Reader_Text.AccessibleName = Localization.T("Текст страницы")
        UpdatePdfReaderContext()
        LayoutPdfReader(Me, EventArgs.Empty)
    End Sub

    Private Sub TogglePdfReaderPane(index As Integer)
        If Not FullPdfReaderActive() Then Return
        pdf_Reader_Pane.Visible = Not pdf_Reader_Pane.Visible OrElse pdf_Reader_Pane.SelectedIndex <> index
        pdf_Reader_Pane.SelectedIndex = index
        If pdf_Reader_Pane.Visible Then
            SlideShowStop()
            pdf_Reader_Interaction = True
            pdf_Reader_Pane.Focus()
            If index = 1 Then pdf_Reader_Query.Focus()
        End If
        UpdatePdfReaderContext()
    End Sub

    Private Sub UpdatePdfReaderContext()
        If pdf_Reader_Pane Is Nothing Then Return
        pdf_Reader_ContextText = If(pdf_Reader_Interaction OrElse pdf_Reader_Pane.ContainsFocus,
            Localization.T("Документ: Esc возвращает сортировку"), Localization.T("Просмотр: клавиши сортируют файлы"))
        SyncDocumentPanel()
    End Sub

    Private Async Sub ReadPdfReaderInfo()
        Dim session = pdf_Session
        Dim source = pdf_Current_Source
        Dim token = pdf_Reader_Cts.Token
        Try
            Dim info = Await Task.Run(Function() source.ReadReaderInfo(token), token).WaitAsync(token)
            If session <> pdf_Session OrElse token.IsCancellationRequested OrElse Not FullPdfReaderActive() Then Return
            pdf_Reader_Info = info
            If pdf_Reading_Canvas IsNot Nothing Then
                pdf_Reading_Canvas.DocumentRightToLeft = info.RightToLeft
                pdf_Reading_Canvas.GoToPage(pdf_Current_Page, pdf_Reading_Canvas.ViewportFraction)
            End If
            Dim addNodes As Action(Of TreeNodeCollection, IEnumerable(Of PdfOutlineItem)) = Nothing
            addNodes = Sub(nodes, items)
                           For Each item In items
                               Dim node = nodes.Add(item.Title)
                               node.Tag = item
                               addNodes(node.Nodes, item.Children)
                           Next
                       End Sub
            addNodes(pdf_Reader_Outline.Nodes, info.Outline)
            If PdfReaderPreferences().PdfOutlinePanel Then TogglePdfReaderPane(0)
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If session = pdf_Session Then Notify(Localization.T("Не удалось открыть PDF"), NotifyLevel.Warning)
        End Try
    End Sub

    Private Async Sub ReadPdfReaderPage(page As Integer)
        If Not FullPdfReaderActive() Then Return
        Dim readSerial = Threading.Interlocked.Increment(pdf_Reader_TextReadSerial)
        pdf_Reader_Voice?.Stop()
        pdf_Reader_TextPage = Nothing
        pdf_Reader_SelectionStart = -1
        pdf_Reader_SelectionEnd = -1
        pdf_Reader_Text.Clear()
        pdf_Reader_ShownPage = Math.Max(1, Math.Min(pdf_Total_Pages, page))
        SyncDocumentPanel()
        Dim session = pdf_Session
        Dim source = pdf_Current_Source
        Dim token = pdf_Reader_Cts.Token
        Try
            Dim result = Await Task.Run(Function() source.ReadTextPage(page, token), token).WaitAsync(token)
            If session <> pdf_Session OrElse readSerial <> pdf_Reader_TextReadSerial OrElse page <> pdf_Current_Page OrElse token.IsCancellationRequested OrElse Not FullPdfReaderActive() Then Return
            pdf_Reader_TextPage = result
            pdf_Reader_Text.Text = If(result.Text.Length = 0, Localization.T("На странице нет извлекаемого текста. Используйте распознавание текста."), result.Text)
            Dim hit = TryCast(pdf_Reader_Results.SelectedItem, PdfSearchHit)
            If hit IsNot Nothing AndAlso hit.Page = page Then
                pdf_Reader_Text.Select(Math.Min(hit.Offset, pdf_Reader_Text.TextLength), Math.Min(pdf_Reader_SearchQuery.Length, Math.Max(0, pdf_Reader_Text.TextLength - hit.Offset)))
                pdf_Reader_Text.ScrollToCaret()
                pdf_Reader_SelectionStart = hit.Offset
                pdf_Reader_SelectionEnd = Math.Min(result.Text.Length - 1, hit.Offset + pdf_Reader_SearchQuery.Length - 1)
            End If
            GetActivePictureBox()?.Invalidate()
            pdf_Reading_Canvas?.Invalidate()
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If session = pdf_Session AndAlso page = pdf_Current_Page AndAlso FullPdfReaderActive() Then pdf_Reader_Text.Text = Localization.T("Не удалось прочитать текст страницы")
        End Try
    End Sub

    Private NotInheritable Class PdfSearchHit
        Public Page As Integer
        Public Offset As Integer
        Public Snippet As String
        Public Overrides Function ToString() As String
            Return Localization.TF("Страница {0}: {1}", Page, Snippet)
        End Function
    End Class

    Private Async Sub SearchPdfReader()
        If Not FullPdfReaderActive() OrElse String.IsNullOrWhiteSpace(pdf_Reader_Query.Text) Then Return
        pdf_Reader_SearchCts?.Cancel()
        pdf_Reader_SearchCts?.Dispose()
        pdf_Reader_SearchCts = CancellationTokenSource.CreateLinkedTokenSource(pdf_Reader_Cts.Token)
        Dim token = pdf_Reader_SearchCts.Token
        Dim session = pdf_Session
        Dim source = pdf_Current_Source
        Dim query = pdf_Reader_Query.Text
        Dim first = If(PdfReaderPreferences().PdfSearchScope = "page", pdf_Current_Page, 1)
        Dim last = If(PdfReaderPreferences().PdfSearchScope = "page", pdf_Current_Page, pdf_Total_Pages)
        Dim sensitive = PdfReaderPreferences().PdfSearchCase
        pdf_Reader_SearchQuery = query
        pdf_Reader_SearchCase = sensitive
        Dim pagesWithoutText As Integer
        pdf_Reader_Results.Items.Clear()
        Try
            For page = first To last
                Dim pageNumber = page
                Dim text = Await Task.Run(Function() source.ReadTextPage(pageNumber, token), token).WaitAsync(token)
                If session <> pdf_Session OrElse token.IsCancellationRequested OrElse Not FullPdfReaderActive() Then Return
                If String.IsNullOrWhiteSpace(text.Text) Then pagesWithoutText += 1
                For Each offset In DocumentReadingState.FindMatches(text.Text, query, sensitive, 500 - pdf_Reader_Results.Items.Count)
                    Dim start = Math.Max(0, offset - 30)
                    pdf_Reader_Results.Items.Add(New PdfSearchHit With {.Page = page, .Offset = offset, .Snippet = text.Text.Substring(start, Math.Min(100, text.Text.Length - start)).Replace(vbCr, " ").Replace(vbLf, " ")})
                Next
                pdf_Reader_ContextText = Localization.TF("Поиск: страница {0} из {1}", page, last)
                SyncDocumentPanel()
                If pdf_Reader_Results.Items.Count >= 500 Then Exit For
            Next
            pdf_Reader_ContextText = If(pagesWithoutText = 0, Localization.TF("Найдено: {0}", pdf_Reader_Results.Items.Count), Localization.TF("Найдено: {0}; страниц без текста: {1}", pdf_Reader_Results.Items.Count, pagesWithoutText))
            SyncDocumentPanel()
        Catch ex As OperationCanceledException
            If session = pdf_Session AndAlso FullPdfReaderActive() AndAlso pdf_Reader_SearchCts IsNot Nothing AndAlso pdf_Reader_SearchCts.Token = token Then
                pdf_Reader_ContextText = Localization.T("Поиск отменён")
                SyncDocumentPanel()
            End If
        Catch ex As Exception
            If session = pdf_Session AndAlso Not token.IsCancellationRequested Then Notify(Localization.T("Не удалось прочитать текст страницы"), NotifyLevel.Warning)
        End Try
    End Sub

    Private NotInheritable Class PdfBookmarkDisplay
        Public Bookmark As DocumentBookmark
        Public Overrides Function ToString() As String
            Return Bookmark.Label
        End Function
    End Class

    Private Sub RefreshPdfBookmarks()
        pdf_Reader_Bookmarks.Items.Clear()
        For Each item In modern_Preferences.ReadDocumentBookmarks().Where(Function(bookmark) bookmark.Identity.Matches(PdfReaderIdentity()))
            pdf_Reader_Bookmarks.Items.Add(New PdfBookmarkDisplay With {.Bookmark = item})
        Next
    End Sub

    Private Sub AddPdfReaderBookmark()
        If Not FullPdfReaderActive() OrElse IsSecureContentOnScreen() Then Return
        Dim name = PdfBookmarkName(Localization.TF("Страница {0}", pdf_Current_Page))
        If name Is Nothing Then Return
        modern_Preferences.AddDocumentBookmark(PdfReaderIdentity(), CapturePdfReaderPosition(),
            name)
        MarkSettingsDirty()
        RefreshPdfBookmarks()
    End Sub

    Private Function PdfBookmarkName(initial As String) As String
        Dim prompt As New PromptSpec(Localization.T("Закладки"), Localization.T("Закладки"), initial, Localization.T("Сохранить"),
            Function(value) If(String.IsNullOrWhiteSpace(value), Localization.T("Не задано"), Nothing))
        Using dialog As New Ask_Dialog(prompt)
            DialogLayer.ShowViewerDialog(dialog, Me)
            If dialog.Answer <> 0 Then Return Nothing
            Return dialog.TypedText.Trim()
        End Using
    End Function

    Private Sub RenamePdfBookmark()
        Dim item = TryCast(pdf_Reader_Bookmarks.SelectedItem, PdfBookmarkDisplay)
        If item Is Nothing Then Return
        Dim name = PdfBookmarkName(item.Bookmark.Label)
        If name Is Nothing Then Return
        Dim entries = modern_Preferences.ReadDocumentBookmarks()
        Dim bookmark = entries.FirstOrDefault(Function(value) value.Id = item.Bookmark.Id)
        If bookmark Is Nothing Then Return
        bookmark.Label = name
        modern_Preferences.DocumentBookmarks = JsonSerializer.Serialize(entries)
        MarkSettingsDirty()
        RefreshPdfBookmarks()
    End Sub

    Private Sub NavigatePdfReaderLink(page As Integer)
        If page < 1 OrElse page > pdf_Total_Pages Then Return
        If pdf_Reader_Back.Count = 100 Then pdf_Reader_Back.Clear()
        pdf_Reader_Back.Push(pdf_Current_Page)
        DisplayPdfPage(page)
    End Sub

    Private Sub NavigatePdfReaderPosition(position As DocumentReadingPosition)
        If position Is Nothing OrElse position.PageNumber < 1 OrElse position.PageNumber > pdf_Total_Pages Then Return
        position.Normalize()
        pdf_Reader_Restore = position
        PdfReaderPreferences().PdfZoom = position.ZoomMode
        PdfReaderPreferences().PdfZoomPercent = position.ZoomPercent
        pdf_Current_Page = position.PageNumber
        ConfigurePdfReadingCanvas()
        DisplayPdfPage(position.PageNumber)
    End Sub

    Private Sub RemovePdfReaderBookmark()
        Dim item = TryCast(pdf_Reader_Bookmarks.SelectedItem, PdfBookmarkDisplay)
        If item Is Nothing Then Return
        Dim entries = modern_Preferences.ReadDocumentBookmarks()
        entries.RemoveAll(Function(bookmark) bookmark.Id = item.Bookmark.Id)
        modern_Preferences.DocumentBookmarks = JsonSerializer.Serialize(entries)
        MarkSettingsDirty()
        RefreshPdfBookmarks()
    End Sub

    Private Sub SpeakPdfReader()
        If Not FullPdfReaderActive() OrElse Not modern_Preferences.DocumentTtsEnabled OrElse pdf_Reader_Info Is Nothing OrElse
            Not pdf_Reader_Info.CanReadAloud OrElse pdf_Reader_TextPage Is Nothing Then Return
        Try
            If pdf_Reader_Voice Is Nothing Then pdf_Reader_Voice = New DocumentSpeech()
            Dim text = pdf_Reader_Text.SelectedText
            If text.Length = 0 Then text = pdf_Reader_TextPage.Text
            pdf_Reader_Voice.Speak(text, modern_Preferences.DocumentTtsVoice, modern_Preferences.DocumentTtsRate)
        Catch ex As Exception
            Notify(Localization.T("Установленный голос недоступен"), NotifyLevel.Warning)
        End Try
    End Sub

    Private Function PdfReaderOwnsInput(e As KeyEventArgs) As Boolean
        If Not FullPdfReaderActive() Then Return False
        Dim onPanel = ChromeKeyboardOnDocumentPanel()
        Dim owns = pdf_Reader_Interaction OrElse onPanel OrElse pdf_Reader_Pane.ContainsFocus
        If Not owns Then Return False
        UpdatePdfReaderContext()
        SlideShowStop()
        If e.KeyCode = Keys.Escape Then
            If pdf_Reader_Pane.Visible Then
                pdf_Reader_Pane.Visible = False
            Else
                pdf_Reader_Interaction = False
            End If
            Me.ActiveControl = Nothing
            Me.Focus()
            e.Handled = True
            e.SuppressKeyPress = True
            UpdatePdfReaderContext()
        ElseIf Not onPanel AndAlso Not pdf_Reader_Pane.ContainsFocus Then
            If e.KeyCode = Keys.PageUp Then PdfPrevPage()
            If e.KeyCode = Keys.PageDown Then PdfNextPage()
            If e.KeyData = (Keys.Control Or Keys.C) AndAlso pdf_Reader_Info IsNot Nothing AndAlso pdf_Reader_Info.CanCopy AndAlso
                pdf_Reader_TextPage IsNot Nothing AndAlso pdf_Reader_SelectionStart >= 0 AndAlso pdf_Reader_SelectionEnd >= 0 Then
                Dim start = Math.Min(pdf_Reader_SelectionStart, pdf_Reader_SelectionEnd)
                Dim text = pdf_Reader_TextPage.Text.Substring(start, Math.Abs(pdf_Reader_SelectionEnd - pdf_Reader_SelectionStart) + 1)
                Utils.CopyTextToClipboard(text, lbl_Status, Localization.TF("Скопировано символов: {0}", text.Length))
            End If
            e.Handled = True
            e.SuppressKeyPress = True
        End If
        Return True
    End Function

    Private Sub ClosePdfReaderUi()
        If document_Panel_Kind = DocumentReaderKind.Pdf OrElse document_Wanted_Kind = DocumentReaderKind.Pdf Then HideDocumentPanel()
        If panel_Media IsNot Nothing Then RemoveHandler panel_Media.SizeChanged, AddressOf LayoutPdfReader
        pdf_Reading_Canvas?.Dispose()
        pdf_Reading_Canvas = Nothing
        pdf_Reader_Cts?.Cancel()
        pdf_Reader_SearchCts?.Cancel()
        pdf_Reader_SearchCts?.Dispose()
        pdf_Reader_SearchCts = Nothing
        pdf_Reader_Cts?.Dispose()
        pdf_Reader_Cts = Nothing
        For Each image In pdf_Reader_ThumbnailCache.Values
            image.Dispose()
        Next
        pdf_Reader_ThumbnailCache.Clear()
        pdf_Reader_ThumbnailBusy = False
        pdf_Reader_Voice?.Dispose()
        pdf_Reader_Voice = Nothing
        pdf_Reader_Pane?.Dispose()
        pdf_Reader_Pane = Nothing
        pdf_Reader_Info = Nothing
        pdf_Reader_TextPage = Nothing
        pdf_Reader_SearchQuery = ""
        pdf_Reader_Selecting = False
        pdf_Reader_Back.Clear()
        pdf_Reader_Preferences = Nothing
        pdf_Reader_Restore = Nothing
        pdf_Reader_SessionAppearance.Clear()
        pdf_Reader_Interaction = False
    End Sub
End Class
#End If
