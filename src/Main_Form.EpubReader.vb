#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Main_Form
    Private epub_Reader_File As String = ""
    Private epub_Reader_Snapshot As DocumentSnapshot
    Private epub_Reader_Book As EpubPackage
    Private epub_Reader_Host As HtmlRestrictedHost
    Private epub_Reader_Cts As CancellationTokenSource
    Private epub_Reader_SearchCts As CancellationTokenSource
    Private epub_Reader_Pane As TabControl
    ''' <summary>The context readout of the document panel (280): which side the keys are on, or the search result count.</summary>
    Private epub_Reader_ContextText As String = ""
    Private epub_Reader_Query As TextBox
    Private epub_Reader_Results As ListBox
    Private epub_Reader_Bookmarks As ListBox
    Private epub_Reader_Preferences As ModernViewerPreferences
    Private epub_Reader_Position As New DocumentReadingPosition With {.Format = "epub"}
    Private epub_Reader_Restore As DocumentReadingPosition
    Private epub_Reader_Interaction As Boolean
    Private epub_Reader_PositionBusy As Boolean
    Private epub_Reader_SearchQuery As String = ""
    Private epub_Reader_Voice As DocumentSpeech
    ''' <summary>Samples the reading position only; the strip it used to hide is the document panel now (280 T-0 correction 2).</summary>
    Private epub_Reader_Timer As System.Windows.Forms.Timer
    Private ReadOnly epub_Reader_Back As New Stack(Of DocumentReadingPosition)()
    Private epub_Reader_Navigation As Integer

    Private Function IsEpubReaderShown() As Boolean
        Return epub_Reader_Host IsNot Nothing AndAlso Not epub_Reader_Host.IsDisposed AndAlso epub_Reader_Book IsNot Nothing
    End Function

    Private Async Sub LoadEpubReader(filePath As String)
        ReleaseEpubReader()
        If IsSecureContentOnScreen() Then
            ShowContentPlaceholder(filePath)
            Notify(Localization.T("Чтение EPUB недоступно"), NotifyLevel.Warning)
            Return
        End If
        epub_Reader_File = filePath
        BlankActiveMediaSurface()
        current_Loaded_File_Name = filePath
        Dim generation = media_Generation
        epub_Reader_Cts = New CancellationTokenSource()
        Dim token = epub_Reader_Cts.Token
        Dim snapshot As DocumentSnapshot = Nothing
        Try
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(token)
                deadline.CancelAfter(TimeSpan.FromSeconds(30))
                snapshot = Await DocumentSnapshot.CreateAsync(filePath, DocumentSourceProvenance(filePath), deadline.Token)
                Dim identity = DocumentReaderIdentity(filePath, snapshot.Identity.LastWriteUtcTicks, snapshot.Identity.Length)
                snapshot.Identity.Path = identity.Path
                snapshot.Identity.Provenance = identity.Provenance
                snapshot.Identity.LastWriteUtcTicks = identity.LastWriteUtcTicks
                snapshot.Identity.Length = identity.Length
                Dim book = Await Task.Run(Function() snapshot.Read(Function(stream) EpubPackage.Read(stream, deadline.Token), deadline.Token), deadline.Token)
                If token.IsCancellationRequested OrElse generation <> media_Generation OrElse IsDisposed Then Return
                epub_Reader_Snapshot = snapshot
                snapshot = Nothing
                epub_Reader_Book = book
                epub_Reader_Preferences = modern_Preferences.EffectiveDocumentPreferences(epub_Reader_Snapshot.Identity)
                epub_Reader_Position = If(Is_slide_show_mode, New DocumentReadingPosition With {.Format = "epub"}, modern_Preferences.RememberedDocumentPosition(epub_Reader_Snapshot.Identity))
                epub_Reader_Position.Format = "epub"
                If Not book.Spine.Any(Function(item) item.Key = epub_Reader_Position.SpineKey) Then epub_Reader_Position.SpineKey = book.Spine(0).Key
                epub_Reader_Host = New HtmlRestrictedHost(AddressOf ReadEpubReaderResource)
                panel_Media.Controls.Add(epub_Reader_Host)
                Await epub_Reader_Host.InitializeAsync(deadline.Token)
                If token.IsCancellationRequested OrElse generation <> media_Generation OrElse IsDisposed Then Return
            End Using
            SetMediaDisplayKind(MediaKind.Book)
            BuildEpubReaderUi()
            epub_Reader_Restore = epub_Reader_Position
            epub_Reader_Host.Navigate(epub_Reader_Position.SpineKey, epub_Reader_Position.Anchor)
        Catch ex As OperationCanceledException
            If Not token.IsCancellationRequested AndAlso generation = media_Generation Then FailEpubReader(filePath, Localization.T("Чтение EPUB недоступно"))
        Catch ex As NotSupportedException
            If Not token.IsCancellationRequested AndAlso generation = media_Generation Then FailEpubReader(filePath, Localization.T("Книга защищена DRM. Откройте её в авторизованной программе."))
        Catch ex As Exception
            ' Do not log an engine exception message containing document text, passwords or URLs.
            If Not token.IsCancellationRequested AndAlso generation = media_Generation Then
                Dim message = If(epub_Reader_Book Is Nothing, Localization.T("Файл EPUB повреждён или имеет неподдерживаемый формат."),
                    Localization.T("Для чтения EPUB нужен установленный Microsoft Edge WebView2 Runtime. Проверьте рантайм и восстановите установку приложения."))
                FailEpubReader(filePath, message)
            End If
        Finally
            snapshot?.Dispose()
        End Try
    End Sub

    Private Sub FailEpubReader(path As String, message As String)
        ReleaseEpubReader()
        ShowContentPlaceholder(path)
        Notify(message, NotifyLevel.Warning)
    End Sub

    Private Function ReadEpubReaderResource(key As String, token As CancellationToken) As Task(Of HtmlReaderResource)
        Dim book = epub_Reader_Book
        Dim snapshot = epub_Reader_Snapshot
        Dim preferences = epub_Reader_Preferences.Snapshot()
        Dim dark = ThemeService.Palette.IsDark
        Dim resource = book.Resources.Values.FirstOrDefault(Function(item) item.Key = key)
        If resource Is Nothing Then Return Task.FromResult(Of HtmlReaderResource)(Nothing)
        Dim mime = resource.MediaType
        If mime.StartsWith("font/", StringComparison.Ordinal) OrElse {"application/vnd.ms-opentype", "application/font-sfnt", "application/font-woff"}.Contains(mime) Then
            If Not preferences.EpubUseEmbeddedFonts Then Return Task.FromResult(Of HtmlReaderResource)(Nothing)
        ElseIf Not {"application/xhtml+xml", "text/html", "text/css", "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml", "image/avif"}.Contains(mime) Then
            Return Task.FromResult(Of HtmlReaderResource)(Nothing)
        End If
        Return Task.Run(Function()
            Dim bytes = snapshot.Read(Function(stream) EpubPackage.ReadResource(stream, key, token), token)
            If mime = "application/xhtml+xml" OrElse mime = "text/html" OrElse (mime = "image/svg+xml" AndAlso book.Spine.Contains(resource)) Then
                bytes = HtmlReaderContent.Sanitize(bytes, preferences, resource.FixedLayout, dark)
                mime = "text/html; charset=utf-8"
            End If
            Return New HtmlReaderResource With {.Bytes = bytes, .MimeType = mime}
        End Function, token)
    End Function

    ''' <summary>The 1-based chapter the reader is on (the spine position of the current reading position).</summary>
    Private Function EpubReaderChapterNumber() As Integer
        If epub_Reader_Book Is Nothing Then Return 1
        Dim key = epub_Reader_Position.SpineKey
        Return Math.Max(1, epub_Reader_Book.Spine.FindIndex(Function(item) item.Key = key) + 1)
    End Function

    Private Function EpubReaderChapterCount() As Integer
        Return If(epub_Reader_Book Is Nothing, 0, epub_Reader_Book.Spine.Count)
    End Function

    Private Sub EpubReaderGoBack()
        If epub_Reader_Back.Count > 0 Then NavigateEpubReader(epub_Reader_Back.Pop(), False)
    End Sub

    Private Sub ResetEpubReaderAppearance()
        If Not IsEpubReaderShown() Then Return
        modern_Preferences.ClearDocumentAppearance(epub_Reader_Snapshot.Identity)
        ApplyEpubReaderPreferences()
        MarkSettingsDirty()
    End Sub

    ''' <summary>What the EPUB reader puts on the document panel, by item id (the order is the panel's: <see cref="DocumentPanelRules"/>).
    ''' Previous / next file and the settings button are not here - the rows carry them.</summary>
    Private Function EpubReaderCommands() As Dictionary(Of String, DocumentCommand)
        Dim commands As New Dictionary(Of String, DocumentCommand)(StringComparer.Ordinal)
        commands(DocumentPanelRules.IdBack) = New DocumentCommand With {.Action = AddressOf EpubReaderGoBack}
        commands(DocumentPanelRules.IdPrevChapter) = New DocumentCommand With {.Action = Sub() StepEpubReader(-1)}
        commands(DocumentPanelRules.IdChapterPosition) = New DocumentCommand With {
            .Action = Sub() BeginDocumentPositionEdit(DocumentPanelRules.IdChapterPosition, EpubReaderChapterNumber(), EpubReaderChapterCount(),
                Sub(chapter)
                    If Not IsEpubReaderShown() OrElse chapter = EpubReaderChapterNumber() Then Return
                    NavigateEpubReader(New DocumentReadingPosition With {.Format = "epub", .SpineKey = epub_Reader_Book.Spine(chapter - 1).Key})
                End Sub),
            .Text = Function() DocumentPanelRules.PositionReadout(EpubReaderChapterNumber(), EpubReaderChapterCount(), DocumentPanelRules.PositionReadoutFormat),
            .Reserved = Function() New String() {DocumentPanelRules.PositionReadoutReserved(EpubReaderChapterCount(), DocumentPanelRules.PositionReadoutFormat)}}
        commands(DocumentPanelRules.IdNextChapter) = New DocumentCommand With {.Action = Sub() StepEpubReader(1)}
        For Each pane As KeyValuePair(Of String, Integer) In New KeyValuePair(Of String, Integer)() {
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneOutline, 0),
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneSearch, 1),
                New KeyValuePair(Of String, Integer)(DocumentPanelRules.IdPaneBookmarks, 2)}
            Dim index As Integer = pane.Value
            commands(pane.Key) = New DocumentCommand With {
                .Action = Sub() ToggleEpubReaderPane(index),
                .IsChecked = Function() epub_Reader_Pane IsNot Nothing AndAlso epub_Reader_Pane.Visible AndAlso epub_Reader_Pane.SelectedIndex = index}
        Next
        commands(DocumentPanelRules.IdAddBookmark) = New DocumentCommand With {.Action = AddressOf AddEpubReaderBookmark}
        commands(DocumentPanelRules.IdCopy) = New DocumentCommand With {.Action = AddressOf CopyEpubReaderSelection}
        commands(DocumentPanelRules.IdSpeak) = New DocumentCommand With {.Action = AddressOf SpeakEpubReader}
        commands(DocumentPanelRules.IdStopSpeech) = New DocumentCommand With {.Action = Sub() epub_Reader_Voice?.Stop()}
        commands(DocumentPanelRules.IdResetAppearance) = New DocumentCommand With {.Action = AddressOf ResetEpubReaderAppearance}
        commands(DocumentPanelRules.IdOpenWith) = New DocumentCommand With {.Action = Sub() ManagerShellOpen.OpenWithPicker(Handle, epub_Reader_File)}
        commands(DocumentPanelRules.IdTranslate) = New DocumentCommand With {.Action = Sub() OpenDocumentConversion(epub_Reader_File)}
        commands(DocumentPanelRules.IdContext) = New DocumentCommand With {
            .Text = Function() epub_Reader_ContextText,
            .Reserved = Function() New String() {Localization.T("Просмотр: клавиши сортируют файлы"), Localization.T("Документ: клавиши работают с текстом")}}
        Return commands
    End Function

    Private Sub BuildEpubReaderUi()
        epub_Reader_Pane = New TabControl With {.Dock = DockStyle.None, .Width = CInt(240 * GetCurrentDpiScale()), .Visible = False}
        For Each title In {"Оглавление", "Поиск", "Закладки"}
            epub_Reader_Pane.TabPages.Add(New TabPage(Localization.T(title)))
        Next
        Dim toc As New TreeView With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Оглавление")}
        Dim addNodes As Action(Of TreeNodeCollection, IEnumerable(Of EpubTocItem), Integer) = Nothing
        addNodes = Sub(nodes, items, depth)
            If depth >= epub_Reader_Preferences.EpubTocDepth Then Return
            For Each item In items
                Dim node = nodes.Add(item.Label)
                node.Tag = item
                addNodes(node.Nodes, item.Children, depth + 1)
            Next
        End Sub
        addNodes(toc.Nodes, epub_Reader_Book.Toc, 0)
        AddHandler toc.AfterSelect, Sub(sender, e)
            Dim item = TryCast(e.Node.Tag, EpubTocItem)
            If item IsNot Nothing Then NavigateEpubReader(New DocumentReadingPosition With {.Format = "epub", .SpineKey = item.Key, .Anchor = item.Anchor})
        End Sub
        epub_Reader_Pane.TabPages(0).Controls.Add(toc)
        epub_Reader_Query = New TextBox With {.Dock = DockStyle.Top, .AccessibleName = Localization.T("Поиск в документе")}
        epub_Reader_Results = New ListBox With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Результаты поиска")}
        Dim search As New Button With {.Dock = DockStyle.Top, .Text = Localization.T("Найти")}
        Dim cancel As New Button With {.Dock = DockStyle.Bottom, .Text = Localization.T("Отмена")}
        AddHandler search.Click, Sub() SearchEpubReader()
        AddHandler cancel.Click, Sub() epub_Reader_SearchCts?.Cancel()
        AddHandler epub_Reader_Query.KeyDown, Sub(sender, e)
            If e.KeyCode = Keys.Enter Then
                SearchEpubReader()
                e.SuppressKeyPress = True
            End If
        End Sub
        AddHandler epub_Reader_Results.SelectedIndexChanged, Sub()
            Dim item = TryCast(epub_Reader_Results.SelectedItem, EpubSearchDisplay)
            If item IsNot Nothing Then NavigateEpubReader(New DocumentReadingPosition With {.Format = "epub", .SpineKey = item.Hit.SpineKey, .TextOffset = item.Hit.TextOffset})
        End Sub
        epub_Reader_Pane.TabPages(1).Controls.AddRange({epub_Reader_Results, search, epub_Reader_Query, cancel})
        epub_Reader_Bookmarks = New ListBox With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Закладки")}
        Dim rename As New Button With {.Dock = DockStyle.Bottom, .Text = Localization.T("Переименовать")}
        Dim remove As New Button With {.Dock = DockStyle.Bottom, .Text = Localization.T("Удалить")}
        AddHandler rename.Click, Sub() EditEpubReaderBookmark(False)
        AddHandler remove.Click, Sub() EditEpubReaderBookmark(True)
        AddHandler epub_Reader_Bookmarks.SelectedIndexChanged, Sub()
            Dim item = TryCast(epub_Reader_Bookmarks.SelectedItem, PdfBookmarkDisplay)
            If item IsNot Nothing Then NavigateEpubReader(item.Bookmark.Position)
        End Sub
        AddHandler epub_Reader_Bookmarks.KeyDown, Sub(sender, e)
            If e.KeyCode = Keys.Delete OrElse e.KeyCode = Keys.F2 Then
                EditEpubReaderBookmark(e.KeyCode = Keys.Delete)
                e.SuppressKeyPress = True
            End If
        End Sub
        epub_Reader_Pane.TabPages(2).Controls.AddRange({epub_Reader_Bookmarks, remove, rename})
        panel_Media.Controls.Add(epub_Reader_Pane)
        AddHandler panel_Media.SizeChanged, AddressOf LayoutEpubReader
        AddHandler epub_Reader_Host.LinkRequested, Sub(key, anchor) NavigateEpubReader(New DocumentReadingPosition With {.Format = "epub", .SpineKey = key, .Anchor = anchor})
        AddHandler epub_Reader_Host.ExternalLinkRequested, Sub(uri)
            SlideShowStop()
            If ViewerBox.Show(Me, uri.AbsoluteUri, Localization.T("Открыть"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) = DialogResult.OK Then
                Try
                    Process.Start(New ProcessStartInfo(uri.AbsoluteUri) With {.UseShellExecute = True})
                Catch ex As Exception
                    Notify(Localization.T("Чтение EPUB недоступно"), NotifyLevel.Warning)
                End Try
            End If
        End Sub
        AddHandler epub_Reader_Host.ContentReady, AddressOf EpubReaderContentReady
        AddHandler epub_Reader_Host.ExitRequested, Sub() EpubReaderOwnsInput(New KeyEventArgs(Keys.Escape))
        AddHandler epub_Reader_Host.HostFailed, Sub() FailEpubReader(epub_Reader_File, Localization.T("Чтение EPUB недоступно"))
        AddHandler epub_Reader_Host.Enter, Sub()
            epub_Reader_Interaction = True
            SlideShowStop()
            UpdateEpubReaderContext()
        End Sub
        RefreshEpubReaderBookmarks()
        ThemeApplier.ApplyTree(epub_Reader_Pane, ThemeService.Palette)
        epub_Reader_Timer = New System.Windows.Forms.Timer With {.Interval = 500}
        AddHandler epub_Reader_Timer.Tick, AddressOf TickEpubReader
        epub_Reader_Timer.Start()
        LayoutEpubReader(Me, EventArgs.Empty)
        epub_Reader_ContextText = Localization.T("Просмотр: клавиши сортируют файлы")
        ShowDocumentCommands(DocumentReaderKind.Epub, EpubReaderCommands())
    End Sub

    Private Sub LayoutEpubReader(sender As Object, e As EventArgs)
        If Not IsEpubReaderShown() OrElse epub_Reader_Pane Is Nothing Then Return
        ' The page fills the whole area and the document panel floats over it; the side pane starts below the lowest
        ' pinned top panel so its tabs are never hidden (280 Q280-1 default).
        document_Pane_Top = DocumentPaneTop()
        Dim paneTop = Math.Min(document_Pane_Top, Math.Max(0, panel_Media.Height - 1))
        epub_Reader_Pane.Bounds = New Rectangle(0, paneTop, epub_Reader_Pane.Width, Math.Max(1, panel_Media.Height - paneTop))
        Dim left = If(epub_Reader_Pane.Visible, epub_Reader_Pane.Width, 0)
        epub_Reader_Host.Dock = DockStyle.None
        epub_Reader_Host.Bounds = New Rectangle(left, 0, Math.Max(1, panel_Media.Width - left), Math.Max(1, panel_Media.Height))
        epub_Reader_Host.BringToFront()
        epub_Reader_Pane.BringToFront()
    End Sub

    Private Sub ToggleEpubReaderPane(index As Integer)
        SlideShowStop()
        epub_Reader_Pane.Visible = Not epub_Reader_Pane.Visible OrElse epub_Reader_Pane.SelectedIndex <> index
        epub_Reader_Pane.SelectedIndex = index
        epub_Reader_Interaction = True
        LayoutEpubReader(Me, EventArgs.Empty)
        If epub_Reader_Pane.Visible Then epub_Reader_Pane.Focus()
        UpdateEpubReaderContext()
    End Sub

    Private Sub UpdateEpubReaderContext()
        If epub_Reader_Pane Is Nothing Then Return
        epub_Reader_ContextText = Localization.T(If(epub_Reader_Interaction, "Документ: клавиши работают с текстом", "Просмотр: клавиши сортируют файлы"))
        SyncDocumentPanel()
    End Sub

    Private Async Sub TickEpubReader(sender As Object, e As EventArgs)
        If Not IsEpubReaderShown() Then Return
        Dim host = epub_Reader_Host
        If epub_Reader_PositionBusy OrElse epub_Reader_Restore IsNot Nothing Then Return
        epub_Reader_PositionBusy = True
        Dim navigation = epub_Reader_Navigation
        Try
            Dim position = Await host.ReadPositionAsync()
            If host Is epub_Reader_Host AndAlso navigation = epub_Reader_Navigation Then
                epub_Reader_Position = position
                If Not Is_slide_show_mode Then modern_Preferences.RecordDocumentPosition(epub_Reader_Snapshot.Identity, position)
            End If
        Catch ex As Exception
        Finally
            If host Is epub_Reader_Host Then epub_Reader_PositionBusy = False
        End Try
    End Sub

    Private Async Sub EpubReaderContentReady()
        Dim host = epub_Reader_Host
        Dim navigation = epub_Reader_Navigation
        Dim position = epub_Reader_Restore
        ' The chapter readout of the panel follows the chapter that has just loaded.
        SyncDocumentPanel()
        Try
            If position IsNot Nothing AndAlso position.HasViewport AndAlso position.Anchor.Length = 0 Then Await host.RestoreAsync(position)
            If host IsNot epub_Reader_Host OrElse navigation <> epub_Reader_Navigation Then Return
            epub_Reader_Restore = Nothing
            If epub_Reader_SearchQuery.Length > 0 Then Await host.FindAsync(epub_Reader_SearchQuery)
        Catch ex As Exception
        End Try
    End Sub

    Private Async Sub NavigateEpubReader(position As DocumentReadingPosition, Optional rememberBack As Boolean = True)
        If Not IsEpubReaderShown() OrElse Not epub_Reader_Book.Resources.Values.Any(Function(item) item.Key = position.SpineKey AndAlso {"application/xhtml+xml", "image/svg+xml"}.Contains(item.MediaType)) Then Return
        Dim host = epub_Reader_Host
        Dim token = epub_Reader_Cts.Token
        Dim navigation = Threading.Interlocked.Increment(epub_Reader_Navigation)
        epub_Reader_Voice?.Stop()
        Try
            Dim previous = Await host.ReadPositionAsync()
            If token.IsCancellationRequested OrElse host IsNot epub_Reader_Host OrElse navigation <> epub_Reader_Navigation Then Return
            If rememberBack Then
                If epub_Reader_Back.Count >= 100 Then epub_Reader_Back.Clear()
                epub_Reader_Back.Push(previous)
            End If
            If Not Is_slide_show_mode Then modern_Preferences.RecordDocumentPosition(epub_Reader_Snapshot.Identity, previous)
            epub_Reader_Position = position
            epub_Reader_Restore = position
            host.Navigate(position.SpineKey, position.Anchor)
        Catch ex As Exception
            If Not token.IsCancellationRequested Then Notify(Localization.T("Чтение EPUB недоступно"), NotifyLevel.Warning)
        End Try
    End Sub

    Private Sub StepEpubReader(delta As Integer)
        If Not IsEpubReaderShown() Then Return
        Dim index = epub_Reader_Book.Spine.FindIndex(Function(item) item.Key = epub_Reader_Position.SpineKey) + delta
        If index >= 0 AndAlso index < epub_Reader_Book.Spine.Count Then
            NavigateEpubReader(New DocumentReadingPosition With {.Format = "epub", .SpineKey = epub_Reader_Book.Spine(index).Key})
        ElseIf modern_Preferences.DocumentBoundaryNavigation Then
            ReadShowMediaFile(If(delta < 0, Mode_Prev, Mode_Next))
        End If
    End Sub

    Private NotInheritable Class EpubSearchDisplay
        Public Hit As EpubSearchHit
        Public Overrides Function ToString() As String
            Return Hit.Snippet
        End Function
    End Class

    Private Async Sub SearchEpubReader()
        If Not IsEpubReaderShown() OrElse String.IsNullOrWhiteSpace(epub_Reader_Query.Text) Then Return
        SlideShowStop()
        epub_Reader_SearchCts?.Cancel()
        epub_Reader_SearchCts?.Dispose()
        epub_Reader_SearchCts = CancellationTokenSource.CreateLinkedTokenSource(epub_Reader_Cts.Token)
        Dim token = epub_Reader_SearchCts.Token
        Dim host = epub_Reader_Host
        Dim book = epub_Reader_Book
        Dim snapshot = epub_Reader_Snapshot
        Dim chapter = If(epub_Reader_Preferences.EpubSearchScope = "chapter", epub_Reader_Position.SpineKey, Nothing)
        Dim query = epub_Reader_Query.Text
        epub_Reader_SearchQuery = query
        epub_Reader_Results.Items.Clear()
        Try
            Dim result = Await Task.Run(Function() snapshot.Read(Function(stream) book.Search(stream, query, False, chapter, token), token), token)
            If token.IsCancellationRequested OrElse host IsNot epub_Reader_Host Then Return
            For Each hit In result
                epub_Reader_Results.Items.Add(New EpubSearchDisplay With {.Hit = hit})
            Next
            epub_Reader_ContextText = Localization.TF("Найдено: {0}", result.Count)
            SyncDocumentPanel()
            Await host.FindAsync(query)
        Catch ex As OperationCanceledException
            If host Is epub_Reader_Host Then
                epub_Reader_ContextText = Localization.T("Поиск отменён")
                SyncDocumentPanel()
            End If
        Catch ex As Exception
            If host Is epub_Reader_Host AndAlso Not token.IsCancellationRequested Then Notify(Localization.T("Не удалось прочитать текст страницы"), NotifyLevel.Warning)
        End Try
    End Sub

    Private Sub RefreshEpubReaderBookmarks()
        epub_Reader_Bookmarks.Items.Clear()
        For Each item In modern_Preferences.ReadDocumentBookmarks().Where(Function(bookmark) bookmark.Identity.Matches(epub_Reader_Snapshot.Identity))
            epub_Reader_Bookmarks.Items.Add(New PdfBookmarkDisplay With {.Bookmark = item})
        Next
    End Sub

    Private Async Sub AddEpubReaderBookmark()
        If Not IsEpubReaderShown() Then Return
        Dim host = epub_Reader_Host
        Dim name = PdfBookmarkName(epub_Reader_Book.Title)
        If name Is Nothing Then Return
        Try
            Dim position = Await host.ReadPositionAsync()
            If host IsNot epub_Reader_Host Then Return
            modern_Preferences.AddDocumentBookmark(epub_Reader_Snapshot.Identity, position, name)
            MarkSettingsDirty()
            RefreshEpubReaderBookmarks()
        Catch ex As Exception
        End Try
    End Sub

    Private Sub EditEpubReaderBookmark(remove As Boolean)
        Dim item = TryCast(epub_Reader_Bookmarks.SelectedItem, PdfBookmarkDisplay)
        If item Is Nothing Then Return
        Dim label = If(remove, Nothing, PdfBookmarkName(item.Bookmark.Label))
        If Not remove AndAlso label Is Nothing Then Return
        Dim entries = modern_Preferences.ReadDocumentBookmarks()
        Dim entry = entries.FirstOrDefault(Function(value) value.Id = item.Bookmark.Id)
        If entry Is Nothing Then Return
        If remove Then entries.Remove(entry) Else entry.Label = label
        modern_Preferences.DocumentBookmarks = JsonSerializer.Serialize(entries)
        MarkSettingsDirty()
        RefreshEpubReaderBookmarks()
    End Sub

    Private Async Sub CopyEpubReaderSelection()
        Dim host = epub_Reader_Host
        Try
            Dim text = Await host.ReadTextAsync(True)
            If host Is epub_Reader_Host AndAlso text.Length > 0 Then Clipboard.SetText(text)
        Catch ex As Exception
        End Try
    End Sub

    Private Async Sub SpeakEpubReader()
        If Not IsEpubReaderShown() OrElse Not modern_Preferences.DocumentTtsEnabled Then Return
        Dim host = epub_Reader_Host
        Dim navigation = epub_Reader_Navigation
        Try
            Dim text = Await host.ReadTextAsync(True)
            If text.Length = 0 Then text = Await host.ReadTextAsync(False)
            If host IsNot epub_Reader_Host OrElse navigation <> epub_Reader_Navigation Then Return
            If epub_Reader_Voice Is Nothing Then epub_Reader_Voice = New DocumentSpeech()
            epub_Reader_Voice.Speak(text, modern_Preferences.DocumentTtsVoice, modern_Preferences.DocumentTtsRate)
        Catch ex As Exception
            If host Is epub_Reader_Host Then Notify(Localization.T("Установленный голос недоступен"), NotifyLevel.Warning)
        End Try
    End Sub

    Private Sub ApplyEpubReaderPreferences()
        If Not IsEpubReaderShown() Then Return
        If Not modern_Preferences.FullEpubReader Then
            Dim path = epub_Reader_File
            ReleaseEpubReader()
            ShowContentPlaceholder(path)
            Return
        End If
        epub_Reader_Preferences = modern_Preferences.EffectiveDocumentPreferences(epub_Reader_Snapshot.Identity)
        If Not modern_Preferences.DocumentTtsEnabled Then epub_Reader_Voice?.Stop()
        NavigateEpubReader(epub_Reader_Position, False)
    End Sub

    Private Function EpubReaderOwnsInput(e As KeyEventArgs) As Boolean
        If Not IsEpubReaderShown() Then Return False
        ' A key counts as activity for the document panel through the chrome's own stamp; the F10 ring on the panel is the
        ' reader's input, as the old strip's focus was.
        ' The pane does not exist yet while the web view starts: the book is shown from the moment it was read.
        Dim paneReady As Boolean = epub_Reader_Pane IsNot Nothing
        Dim owns = epub_Reader_Interaction OrElse epub_Reader_Host.ContainsFocus OrElse ChromeKeyboardOnDocumentPanel() OrElse (paneReady AndAlso epub_Reader_Pane.ContainsFocus)
        If e.KeyCode = Keys.Escape AndAlso owns Then
            If paneReady AndAlso epub_Reader_Pane.Visible Then
                epub_Reader_Pane.Visible = False
                LayoutEpubReader(Me, EventArgs.Empty)
            Else
                epub_Reader_Interaction = False
            End If
            ActiveControl = Nothing
            Focus()
            UpdateEpubReaderContext()
            e.SuppressKeyPress = True
            Return True
        End If
        If Not owns Then
            If e.KeyCode = Keys.PageUp OrElse e.KeyCode = Keys.PageDown Then
                ScrollEpubReader(e.KeyCode = Keys.PageDown)
                e.SuppressKeyPress = True
                Return True
            End If
            Return False
        End If
        SlideShowStop()
        Return True
    End Function

    Private Async Sub ScrollEpubReader(forward As Boolean)
        Try
            Await epub_Reader_Host.ScrollAsync(forward)
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>The wheel over the page (or over the document panel that floats on it) scrolls the chapter; it never also
    ''' flips the file. Without the strip the wheel reaches the window over the panel, where it would page files (280 T-0
    ''' correction 3). The native web view takes the wheel over the page itself, so only the panel's share comes here.</summary>
    Private Function EpubReaderOwnsWheel(e As MouseEventArgs) As Boolean
        If Not IsEpubReaderShown() Then Return False
        Dim host = epub_Reader_Host
        ' The panel can float over an open side pane, beside the page: the wheel over any part of it is still the book's.
        Dim overPanel As Boolean = DocumentPanelPresent() AndAlso chrome_Document_Panel.Host.Visible AndAlso chrome_Document_Panel.Host.Bounds.Contains(Cursor.Position)
        If Not overPanel AndAlso Not host.RectangleToScreen(host.ClientRectangle).Contains(Cursor.Position) Then Return False
        ScrollEpubReaderBy(-e.Delta)
        Return True
    End Function

    Private Async Sub ScrollEpubReaderBy(pixels As Integer)
        Dim host = epub_Reader_Host
        Try
            Await host.ScrollByAsync(pixels)
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
    End Sub

    Private Sub ReleaseEpubReader()
        If epub_Reader_Snapshot IsNot Nothing AndAlso modern_Preferences IsNot Nothing AndAlso Not Is_slide_show_mode Then
            modern_Preferences.RecordDocumentPosition(epub_Reader_Snapshot.Identity, epub_Reader_Position)
            MarkSettingsDirty()
        End If
        epub_Reader_Navigation += 1
        ' Other readers' load paths release this one too: the panel goes only when it is this reader's.
        If document_Panel_Kind = DocumentReaderKind.Epub OrElse document_Wanted_Kind = DocumentReaderKind.Epub Then HideDocumentPanel()
        epub_Reader_Cts?.Cancel()
        epub_Reader_SearchCts?.Cancel()
        epub_Reader_Timer?.Dispose()
        epub_Reader_Timer = Nothing
        RemoveHandler panel_Media.SizeChanged, AddressOf LayoutEpubReader
        epub_Reader_Voice?.Dispose()
        epub_Reader_Voice = Nothing
        epub_Reader_Host?.Dispose()
        epub_Reader_Host = Nothing
        epub_Reader_Pane?.Dispose()
        epub_Reader_Pane = Nothing
        epub_Reader_Snapshot?.Dispose()
        epub_Reader_Snapshot = Nothing
        epub_Reader_Book = Nothing
        epub_Reader_File = ""
        epub_Reader_Interaction = False
        epub_Reader_ContextText = ""
        epub_Reader_PositionBusy = False
        epub_Reader_Back.Clear()
        epub_Reader_SearchQuery = ""
        epub_Reader_Cts?.Dispose()
        epub_Reader_Cts = Nothing
        epub_Reader_SearchCts?.Dispose()
        epub_Reader_SearchCts = Nothing
    End Sub
End Class
#End If
