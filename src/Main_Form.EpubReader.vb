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
    Private epub_Reader_Toolbar As ToolStrip
    Private epub_Reader_Pane As TabControl
    Private epub_Reader_Context As ToolStripLabel
    Private epub_Reader_Query As TextBox
    Private epub_Reader_Results As ListBox
    Private epub_Reader_Bookmarks As ListBox
    Private epub_Reader_Chapter As NumericUpDown
    Private epub_Reader_Preferences As ModernViewerPreferences
    Private epub_Reader_Position As New DocumentReadingPosition With {.Format = "epub"}
    Private epub_Reader_Restore As DocumentReadingPosition
    Private epub_Reader_Interaction As Boolean
    Private epub_Reader_Updating As Boolean
    Private epub_Reader_PositionBusy As Boolean
    Private epub_Reader_SearchQuery As String = ""
    Private epub_Reader_Voice As DocumentSpeech
    Private epub_Reader_Timer As System.Windows.Forms.Timer
    Private epub_Reader_LastActivity As DateTime
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

    Private Sub EpubReaderButton(key As String, action As Action)
        Dim button As New ToolStripButton(Localization.T(key)) With {.Tag = key, .DisplayStyle = ToolStripItemDisplayStyle.Text}
        AddHandler button.Click, Sub() action()
        epub_Reader_Toolbar.Items.Add(button)
    End Sub

    Private Sub BuildEpubReaderUi()
        epub_Reader_Toolbar = New ToolStrip With {.Dock = DockStyle.None, .AutoSize = False, .Height = CInt(32 * GetCurrentDpiScale()), .GripStyle = ToolStripGripStyle.Hidden, .TabStop = True,
            .AccessibleName = Localization.T("Полное чтение EPUB"), .RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)}
        EpubReaderButton("Предыдущий файл", Sub() ReadShowMediaFile(Mode_Prev))
        EpubReaderButton("Следующий файл", Sub() ReadShowMediaFile(Mode_Next))
        EpubReaderButton("Назад", Sub()
            If epub_Reader_Back.Count > 0 Then NavigateEpubReader(epub_Reader_Back.Pop(), False)
        End Sub)
        EpubReaderButton("Предыдущая глава", Sub() StepEpubReader(-1))
        epub_Reader_Chapter = New NumericUpDown With {.Minimum = 1, .Maximum = epub_Reader_Book.Spine.Count, .Width = CInt(80 * GetCurrentDpiScale()), .AccessibleName = Localization.T("Номер главы")}
        epub_Reader_Toolbar.Items.Add(New ToolStripControlHost(epub_Reader_Chapter))
        AddHandler epub_Reader_Chapter.ValueChanged, Sub()
            If Not epub_Reader_Updating Then NavigateEpubReader(New DocumentReadingPosition With {.Format = "epub", .SpineKey = epub_Reader_Book.Spine(CInt(epub_Reader_Chapter.Value) - 1).Key})
        End Sub
        EpubReaderButton("Следующая глава", Sub() StepEpubReader(1))
        EpubReaderButton("Оглавление", Sub() ToggleEpubReaderPane(0))
        EpubReaderButton("Поиск", Sub() ToggleEpubReaderPane(1))
        EpubReaderButton("Закладки", Sub() ToggleEpubReaderPane(2))
        EpubReaderButton("Добавить закладку", AddressOf AddEpubReaderBookmark)
        EpubReaderButton("Копировать", AddressOf CopyEpubReaderSelection)
        EpubReaderButton("Чтение вслух", AddressOf SpeakEpubReader)
        EpubReaderButton("Остановить", Sub() epub_Reader_Voice?.Stop())
        EpubReaderButton("doc-html-translate", Sub() OpenDocumentConversion(epub_Reader_File))
        EpubReaderButton("Открыть с помощью", Sub() ManagerShellOpen.OpenWithPicker(Handle, epub_Reader_File))
        EpubReaderButton("Настройки", AddressOf ShowSettingsWindow)
        EpubReaderButton("Сбросить оформление", Sub()
            modern_Preferences.ClearDocumentAppearance(epub_Reader_Snapshot.Identity)
            ApplyEpubReaderPreferences()
            MarkSettingsDirty()
        End Sub)
        epub_Reader_Context = New ToolStripLabel With {.Alignment = ToolStripItemAlignment.Right, .Overflow = ToolStripItemOverflow.Never}
        epub_Reader_Toolbar.Items.Add(epub_Reader_Context)
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
        panel_Media.Controls.Add(epub_Reader_Toolbar)
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
        epub_Reader_Toolbar.AllowDrop = True
        AddHandler epub_Reader_Toolbar.DragEnter, AddressOf Form1_DragEnter
        AddHandler epub_Reader_Toolbar.DragDrop, AddressOf Form1_DragDrop
        RefreshEpubReaderBookmarks()
        ThemeApplier.ApplyTree(epub_Reader_Toolbar, ThemeService.Palette)
        ThemeApplier.ApplyTree(epub_Reader_Pane, ThemeService.Palette)
        epub_Reader_Timer = New System.Windows.Forms.Timer With {.Interval = 500}
        AddHandler epub_Reader_Timer.Tick, AddressOf TickEpubReader
        epub_Reader_LastActivity = DateTime.UtcNow
        epub_Reader_Timer.Start()
        LayoutEpubReader(Me, EventArgs.Empty)
        UpdateEpubReaderContext()
    End Sub

    Private Sub LayoutEpubReader(sender As Object, e As EventArgs)
        If Not IsEpubReaderShown() OrElse epub_Reader_Toolbar Is Nothing Then Return
        Dim top = If(epub_Reader_Toolbar.Visible, epub_Reader_Toolbar.Height, 0)
        epub_Reader_Toolbar.Bounds = New Rectangle(0, 0, panel_Media.Width, epub_Reader_Toolbar.Height)
        epub_Reader_Pane.Bounds = New Rectangle(0, top, epub_Reader_Pane.Width, Math.Max(1, panel_Media.Height - top))
        Dim left = If(epub_Reader_Pane.Visible, epub_Reader_Pane.Width, 0)
        epub_Reader_Host.Dock = DockStyle.None
        epub_Reader_Host.Bounds = New Rectangle(left, top, Math.Max(1, panel_Media.Width - left), Math.Max(1, panel_Media.Height - top))
        epub_Reader_Host.BringToFront()
        epub_Reader_Pane.BringToFront()
        epub_Reader_Toolbar.BringToFront()
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
        If epub_Reader_Context IsNot Nothing Then epub_Reader_Context.Text = Localization.T(If(epub_Reader_Interaction, "Документ: клавиши работают с текстом", "Просмотр: клавиши сортируют файлы"))
    End Sub

    Private Async Sub TickEpubReader(sender As Object, e As EventArgs)
        If Not IsEpubReaderShown() Then Return
        Dim host = epub_Reader_Host
        Dim pointer = panel_Media.PointToClient(Cursor.Position)
        Dim visible = Not (is_Full_Screen_Mode OrElse is_Super_Full_Screen_Mode) OrElse modern_Preferences.DocumentFullscreenChrome = "visible" OrElse
            epub_Reader_Toolbar.ContainsFocus OrElse epub_Reader_Pane.Visible OrElse (pointer.X >= 0 AndAlso pointer.X < panel_Media.Width AndAlso pointer.Y >= 0 AndAlso pointer.Y < epub_Reader_Toolbar.Height)
        If visible Then epub_Reader_LastActivity = DateTime.UtcNow
        Dim wanted = visible OrElse (DateTime.UtcNow - epub_Reader_LastActivity).TotalSeconds < 3
        If epub_Reader_Toolbar.Visible <> wanted Then
            epub_Reader_Toolbar.Visible = wanted
            LayoutEpubReader(Me, EventArgs.Empty)
        End If
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
        epub_Reader_Updating = True
        epub_Reader_Chapter.Value = Math.Max(1, epub_Reader_Book.Spine.FindIndex(Function(item) item.Key = epub_Reader_Position.SpineKey) + 1)
        epub_Reader_Updating = False
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
            epub_Reader_Context.Text = Localization.TF("Найдено: {0}", result.Count)
            Await host.FindAsync(query)
        Catch ex As OperationCanceledException
            If host Is epub_Reader_Host Then epub_Reader_Context.Text = Localization.T("Поиск отменён")
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
        epub_Reader_LastActivity = DateTime.UtcNow
        If Not epub_Reader_Toolbar.Visible Then
            epub_Reader_Toolbar.Visible = True
            LayoutEpubReader(Me, EventArgs.Empty)
        End If
        Dim owns = epub_Reader_Interaction OrElse epub_Reader_Host.ContainsFocus OrElse epub_Reader_Toolbar.ContainsFocus OrElse epub_Reader_Pane.ContainsFocus
        If e.KeyCode = Keys.Escape AndAlso owns Then
            If epub_Reader_Pane.Visible Then
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

    Private Sub ReleaseEpubReader()
        If epub_Reader_Snapshot IsNot Nothing AndAlso modern_Preferences IsNot Nothing AndAlso Not Is_slide_show_mode Then
            modern_Preferences.RecordDocumentPosition(epub_Reader_Snapshot.Identity, epub_Reader_Position)
            MarkSettingsDirty()
        End If
        epub_Reader_Navigation += 1
        epub_Reader_Cts?.Cancel()
        epub_Reader_SearchCts?.Cancel()
        epub_Reader_Timer?.Dispose()
        epub_Reader_Timer = Nothing
        RemoveHandler panel_Media.SizeChanged, AddressOf LayoutEpubReader
        epub_Reader_Voice?.Dispose()
        epub_Reader_Voice = Nothing
        epub_Reader_Host?.Dispose()
        epub_Reader_Host = Nothing
        epub_Reader_Toolbar?.Dispose()
        epub_Reader_Toolbar = Nothing
        epub_Reader_Pane?.Dispose()
        epub_Reader_Pane = Nothing
        epub_Reader_Snapshot?.Dispose()
        epub_Reader_Snapshot = Nothing
        epub_Reader_Book = Nothing
        epub_Reader_File = ""
        epub_Reader_Interaction = False
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
