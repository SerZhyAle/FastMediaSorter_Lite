#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Specification 275: the one Main_Form partial tying the HTML host to media generation,
' visibility, provenance, reading state and the existing commands. The host owns the page;
' this file owns the window: toolbar, focus context, dialogs and the remembered state.
Partial Public Class Main_Form
    Private html_View_File As String = ""
    Private html_View_Identity As String = ""
    Private html_View_Host As HtmlDocumentHost
    Private html_View_Toolbar As ToolStrip
    Private html_View_Title As ToolStripLabel
    Private html_View_Context As ToolStripLabel
    Private html_View_Query As ToolStripTextBox
    Private html_View_Matches As ToolStripLabel
    Private html_View_ZoomLabel As ToolStripButton
    Private html_View_Reader As ToolStripButton
    Private html_View_Blocked As ToolStripDropDownButton
    Private html_View_BackButton As ToolStripButton
    Private html_View_ForwardButton As ToolStripButton
    Private html_View_Timer As System.Windows.Forms.Timer
    Private html_View_Entry As HtmlReaderEntry
    Private html_View_Position As HtmlPosition
    Private html_View_Restore As HtmlPosition
    Private html_View_Online As Uri
    Private html_View_Interaction As Boolean
    Private html_View_PositionBusy As Boolean
    Private html_View_StateTouched As Boolean
    Private html_View_LastActivity As DateTime
    Private html_View_Zoom As Integer = 100
    ' Session-only acknowledgements of plain-http origins (section 13); never stored.
    Private ReadOnly html_View_InsecureOrigins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private last_Html_Page_Url As String = ""

    Private Shared ReadOnly HtmlZoomSteps As Integer() = {25, 33, 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400, 500}

    Friend Function IsHtmlViewShown() As Boolean
        Return html_View_Host IsNot Nothing AndAlso Not html_View_Host.IsDisposed
    End Function

    Friend Shared Function IsHtmlExtension(extension As String) As Boolean
        Return String.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase)
    End Function

    ' Section 1: the rendered view only when switched on; otherwise the existing text preview stays.
    Private Function TryLoadHtmlView(path As String, preferences As ModernViewerPreferences) As Boolean
        If preferences Is Nothing OrElse Not preferences.HtmlViewerEnabled OrElse Not IsHtmlExtension(MediaKindClassifier.ExtensionOf(path)) Then Return False
        LoadHtmlView(path)
        Return True
    End Function

    Private Function OpenHtmlSource(filePath As String, preferences As ModernViewerPreferences, ByRef source As IHtmlDocumentSource) As HtmlOutcome
        source = Nothing
        Dim identity = HtmlViewIdentity(filePath)
        If IsArchiveMode() OrElse IsResourceMode() Then
            ' Only the materialized entry itself, never the shared materialization folder (section 10.1).
            Dim kind = If(IsArchiveMode(), HtmlSourceKind.ArchiveSnapshot, HtmlSourceKind.ResourceSnapshot)
            Try
                source = HtmlDocumentAdapters.SingleEntry(filePath, kind, DocumentSourceProvenance(filePath), identity)
            Catch ex As ArgumentException
                Return HtmlOutcome.UnsupportedResource
            End Try
            Return HtmlOutcome.Ok
        End If
        Dim local As LocalHtmlDocumentSource = Nothing
        Dim status = LocalHtmlDocumentSource.Open(filePath, "", False, CLng(preferences.HtmlSnapshotCacheMb) * 1024L * 1024L, local)
        If status = HtmlOutcome.Ok Then source = local
        Return status
    End Function

    Private Function HtmlViewIdentity(filePath As String) As String
        Try
            Dim info As New FileInfo(filePath)
            Return DocumentReaderIdentity(filePath, info.LastWriteTimeUtc.Ticks, If(info.Exists, info.Length, 0)).Path
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            Return filePath
        End Try
    End Function

    Private Async Sub LoadHtmlView(filePath As String)
        ReleaseHtmlView()
        If IsSecureContentOnScreen() Then
            ' Secure content gains no rendered copy, browser export or network access (section 3).
            LoadTextDocument(filePath)
            Notify(HtmlViewerText.Outcome(HtmlOutcome.SecureRefused), NotifyLevel.Warning)
            Return
        End If
        Dim preferences = GetModernPreferences()
        Dim generation = media_Generation
        Dim source As IHtmlDocumentSource = Nothing
        Dim status = OpenHtmlSource(filePath, preferences, source)
        If status <> HtmlOutcome.Ok Then
            FailHtmlView(filePath, status)
            Return
        End If
        html_View_File = filePath
        html_View_Identity = HtmlViewIdentity(filePath)
        html_View_Online = Nothing
        BlankActiveMediaSurface()
        current_Loaded_File_Name = filePath
        Await OpenHtmlHostAsync(source, Nothing, False, generation, filePath)
    End Sub

    ' Section 13: an explicitly opened online page; the folder's file stays underneath and every
    ' file verb refuses while the page is on screen, as for a stream.
    Private Async Sub LoadHtmlOnline(page As Uri, insecure As Boolean)
        If is_Vlc_Playing Then RememberCurrentVideoPosition()
        media_Generation += 1
        StopAnimationVideo()
        open_Coordinator.NoteNavigation()
        If IsSecureContentOnScreen() Then
            If is_Vlc_Playing Then StopVlcPlayback()
            SecureReleaseCurrent()
        End If
        ReleasePdfDocument()
        ReleaseEpubReader()
        ReleaseTextDocument()
        ReleaseHtmlView()
        ClearAudioSurface()
        BlankActiveMediaSurface()
        is_Stream_On_Screen = True
        Dim generation = media_Generation
        html_View_File = ""
        html_View_Identity = page.GetLeftPart(UriPartial.Query)
        html_View_Online = page
        Await OpenHtmlHostAsync(New OnlineHtmlDocumentSource(page), page, insecure, generation, "")
    End Sub

    Private Async Function OpenHtmlHostAsync(source As IHtmlDocumentSource, page As Uri, insecure As Boolean, generation As Integer, filePath As String) As Task
        Dim preferences = GetModernPreferences()
        html_View_Entry = LookupHtmlState(Nothing)
        html_View_Zoom = If(html_View_Entry IsNot Nothing AndAlso html_View_Entry.Zoom > 0, html_View_Entry.Zoom, preferences.HtmlZoom)
        Dim host As New HtmlDocumentHost()
        html_View_Host = host
        panel_Media.Controls.Add(host)
        BuildHtmlViewUi()
        SetMediaDisplayKind(MediaKind.Document)
        lbl_Status.Text = Localization.T("Подключение..")
        Dim outcome As HtmlOutcome
        Try
            outcome = If(page Is Nothing, Await host.OpenLocalAsync(source, generation, html_View_Zoom),
                Await host.OpenOnlineAsync(source, page, insecure, generation, html_View_Zoom))
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
            ' Never log an engine message: it can carry document text or a URL.
            outcome = HtmlRuntimeService.OutcomeOf(ex)
        End Try
        If host IsNot html_View_Host OrElse generation <> media_Generation OrElse IsDisposed Then Return
        If outcome <> HtmlOutcome.Ok Then
            If page Is Nothing Then
                FailHtmlView(filePath, outcome)
            Else
                ReleaseHtmlView()
                ShowContentPlaceholder("")
                If outcome <> HtmlOutcome.RedirectNeedsApproval Then Notify(HtmlViewerText.Outcome(outcome), NotifyLevel.Warning)
            End If
            Return
        End If
        ' Initial routing stays on the host canvas until the user enters the document (section 11.2).
        If Not html_View_Interaction Then ActiveControl = Nothing
        UpdateHtmlViewState()
    End Function

    ' Any failure shows the source preview and names what is missing; nothing is installed.
    Private Sub FailHtmlView(path As String, outcome As HtmlOutcome)
        ReleaseHtmlView()
        If outcome = HtmlOutcome.Cancelled Then Return
        If String.IsNullOrEmpty(path) Then
            ShowContentPlaceholder(path)
        Else
            LoadTextDocument(path)
        End If
        Notify(HtmlViewerText.Outcome(outcome), NotifyLevel.Warning)
    End Sub

    Private Function LookupHtmlState(version As HtmlSourceVersion) As HtmlReaderEntry
        Dim preferences = GetModernPreferences()
        If html_View_Identity.Length = 0 OrElse preferences.HtmlHistoryLimit <= 0 OrElse Is_slide_show_mode Then Return Nothing
        html_View_StateTouched = True
        Dim entry = HtmlReaderState.Current.Lookup(html_View_Identity, version)
        If entry IsNot Nothing AndAlso Not preferences.HtmlRememberAppearance Then entry.ClearAppearance()
        If entry IsNot Nothing AndAlso Not preferences.HtmlRememberPosition Then entry.ClearPosition()
        Return entry
    End Function

    Private Sub HtmlViewButton(key As String, action As Action, Optional ByRef created As ToolStripButton = Nothing)
        Dim button As New ToolStripButton(Localization.T(key)) With {.Tag = key, .DisplayStyle = ToolStripItemDisplayStyle.Text, .AccessibleName = Localization.T(key)}
        AddHandler button.Click, Sub() action()
        html_View_Toolbar.Items.Add(button)
        created = button
    End Sub

    Private Sub BuildHtmlViewUi()
        Dim scale = GetCurrentDpiScale()
        html_View_Toolbar = New ToolStrip With {.Dock = DockStyle.None, .AutoSize = False, .Height = CInt(32 * scale), .GripStyle = ToolStripGripStyle.Hidden, .TabStop = True,
            .AccessibleName = Localization.T("Просмотр HTML-страницы"), .RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)}
        HtmlViewButton("Предыдущий файл", Sub() ReadShowMediaFile(Mode_Prev))
        HtmlViewButton("Следующий файл", Sub() ReadShowMediaFile(Mode_Next))
        html_View_Toolbar.Items.Add(New ToolStripSeparator())
        HtmlViewButton("Назад", Sub() RunHtmlCommand(HtmlHostCommand.Back), html_View_BackButton)
        HtmlViewButton("Вперёд", Sub() RunHtmlCommand(HtmlHostCommand.Forward), html_View_ForwardButton)
        HtmlViewButton("Обновить", Sub() RunHtmlCommand(HtmlHostCommand.Reload))
        html_View_Toolbar.Items.Add(New ToolStripSeparator())
        html_View_Query = New ToolStripTextBox With {.AutoSize = False, .Width = CInt(160 * scale), .MaxLength = HtmlDocumentPolicy.MaxFindTermLength,
            .AccessibleName = Localization.T("Поиск на странице"), .ToolTipText = Localization.T("Поиск на странице")}
        AddHandler html_View_Query.KeyDown, AddressOf HtmlViewQueryKeyDown
        html_View_Toolbar.Items.Add(html_View_Query)
        HtmlViewButton("Предыдущее совпадение", Sub() RunHtmlCommand(HtmlHostCommand.FindPrevious))
        HtmlViewButton("Следующее совпадение", Sub() RunHtmlCommand(HtmlHostCommand.FindNext))
        html_View_Matches = New ToolStripLabel With {.AccessibleName = Localization.T("Совпадения")}
        html_View_Toolbar.Items.Add(html_View_Matches)
        html_View_Toolbar.Items.Add(New ToolStripSeparator())
        HtmlViewButton("Уменьшить", Sub() RunHtmlCommand(HtmlHostCommand.ZoomOut))
        html_View_ZoomLabel = New ToolStripButton("100 %") With {.DisplayStyle = ToolStripItemDisplayStyle.Text, .AccessibleName = Localization.T("Сбросить масштаб"), .ToolTipText = Localization.T("Сбросить масштаб")}
        AddHandler html_View_ZoomLabel.Click, Sub() RunHtmlCommand(HtmlHostCommand.ZoomReset)
        html_View_Toolbar.Items.Add(html_View_ZoomLabel)
        HtmlViewButton("Увеличить", Sub() RunHtmlCommand(HtmlHostCommand.ZoomIn))
        HtmlViewButton("По ширине", AddressOf FitHtmlViewWidth)
        html_View_Toolbar.Items.Add(New ToolStripSeparator())
        HtmlViewButton("Оформление для чтения", AddressOf ToggleHtmlReader, html_View_Reader)
        html_View_Reader.CheckOnClick = False
        HtmlViewButton("Сбросить оформление", AddressOf ResetHtmlViewAppearance)
        If html_View_Online Is Nothing Then HtmlViewButton("Исходный текст", AddressOf ShowHtmlViewSource)
        HtmlViewButton("Открыть в браузере", AddressOf OpenHtmlViewInBrowser)
        HtmlViewButton("Печать", Sub() RunHtmlCommand(HtmlHostCommand.Print))
        html_View_Blocked = New ToolStripDropDownButton With {.Visible = False, .DisplayStyle = ToolStripItemDisplayStyle.Text}
        AddHandler html_View_Blocked.DropDownOpening, AddressOf FillHtmlViewBlockedMenu
        html_View_Toolbar.Items.Add(html_View_Blocked)
        HtmlViewButton("Настройки", AddressOf ShowSettingsWindow)
        HtmlViewButton("Вернуться к файлам", AddressOf LeaveHtmlViewInteraction)
        ' AsNeeded, never Never: with an Overflow.Never item on the strip, a UI Automation walk of the open
        ' overflow drop-down (a screen reader, at high DPI where the commands overflow) loops on the UI thread
        ' and the viewer stops responding (275 scene S9, 2026-10-29; reproduced in a bare WinForms strip).
        ' Inserted first instead: a strip overflows from the end of its item list, so the label that says
        ' where the keys go is the last to leave the strip, and Right alignment still draws it on the right.
        html_View_Context = New ToolStripLabel With {.Alignment = ToolStripItemAlignment.Right, .Overflow = ToolStripItemOverflow.AsNeeded}
        html_View_Toolbar.Items.Insert(0, html_View_Context)
        html_View_Title = New ToolStripLabel With {.Alignment = ToolStripItemAlignment.Right, .Overflow = ToolStripItemOverflow.AsNeeded}
        html_View_Toolbar.Items.Add(html_View_Title)
        panel_Media.Controls.Add(html_View_Toolbar)
        AddHandler panel_Media.SizeChanged, AddressOf LayoutHtmlView
        Dim host = html_View_Host
        AddHandler host.ContentReady, AddressOf HtmlViewContentReady
        AddHandler host.Failed, AddressOf HtmlViewFailed
        AddHandler host.CommandRequested, AddressOf RunHtmlCommand
        AddHandler host.ExternalLinkRequested, AddressOf HtmlViewExternalLink
        AddHandler host.TypedOpenRequested, AddressOf HtmlViewTypedOpen
        AddHandler host.RedirectApprovalRequested, AddressOf HtmlViewRedirect
        AddHandler host.FindChanged, AddressOf HtmlViewFindChanged
        AddHandler host.ZoomChanged, AddressOf HtmlViewZoomChanged
        AddHandler host.StateChanged, AddressOf UpdateHtmlViewState
        AddHandler host.Enter, AddressOf EnterHtmlViewInteraction
        html_View_Toolbar.AllowDrop = True
        AddHandler html_View_Toolbar.DragEnter, AddressOf Form1_DragEnter
        AddHandler html_View_Toolbar.DragDrop, AddressOf Form1_DragDrop
        ThemeApplier.ApplyTree(html_View_Toolbar, ThemeService.Palette)
        html_View_Timer = New System.Windows.Forms.Timer With {.Interval = 500}
        AddHandler html_View_Timer.Tick, AddressOf TickHtmlView
        html_View_LastActivity = DateTime.UtcNow
        html_View_Timer.Start()
        LayoutHtmlView(Me, EventArgs.Empty)
        UpdateHtmlViewContext()
    End Sub

    Private Sub LayoutHtmlView(sender As Object, e As EventArgs)
        If Not IsHtmlViewShown() OrElse html_View_Toolbar Is Nothing Then Return
        Dim top = If(html_View_Toolbar.Visible, html_View_Toolbar.Height, 0)
        html_View_Toolbar.Bounds = New Rectangle(0, 0, panel_Media.ClientSize.Width, html_View_Toolbar.Height)
        html_View_Host.Dock = DockStyle.None
        html_View_Host.Bounds = New Rectangle(0, top, Math.Max(1, panel_Media.ClientSize.Width), Math.Max(1, panel_Media.ClientSize.Height - top))
        html_View_Host.BringToFront()
        html_View_Toolbar.BringToFront()
    End Sub

    Private Sub UpdateHtmlViewContext()
        If html_View_Context IsNot Nothing Then html_View_Context.Text = Localization.T(If(html_View_Interaction, "Документ: клавиши работают с текстом", "Просмотр: клавиши сортируют файлы"))
    End Sub

    Private Sub EnterHtmlViewInteraction(sender As Object, e As EventArgs)
        If html_View_Interaction Then Return
        html_View_Interaction = True
        SlideShowStop()
        UpdateHtmlViewContext()
    End Sub

    ' The visible "Return to file controls" action and the second Escape (section 11.2).
    Private Sub LeaveHtmlViewInteraction()
        html_View_Interaction = False
        ActiveControl = Nothing
        Focus()
        UpdateHtmlViewContext()
    End Sub

    Private Function HtmlViewModeName() As String
        Return Localization.T(If(HtmlViewAppearance().Reader, "Оформление для чтения", "Оригинальное оформление"))
    End Function

    Private Sub UpdateHtmlViewState()
        If Not IsHtmlViewShown() OrElse html_View_Toolbar Is Nothing Then Return
        Dim host = html_View_Host
        Dim provenance = If(html_View_Online IsNot Nothing, HtmlAddressPolicy.OriginOf(html_View_Online), Path.GetFileName(html_View_File))
        Dim title = host.Title
        html_View_Title.Text = If(String.IsNullOrWhiteSpace(title), Localization.TF("{0} - {1}", provenance, HtmlViewModeName()),
            Localization.TF("{0} - {1} - {2}", title.Trim(), provenance, HtmlViewModeName()))
        html_View_Title.RightToLeft = If(host.Direction = "rtl", RightToLeft.Yes, html_View_Toolbar.RightToLeft)
        html_View_BackButton.Enabled = host.CanGoBack
        html_View_ForwardButton.Enabled = host.CanGoForward
        html_View_Reader.Checked = HtmlViewAppearance().Reader
        Dim blocked = host.BlockedResources
        html_View_Blocked.Visible = blocked > 0
        If blocked > 0 Then html_View_Blocked.Text = Localization.TF("Заблокировано ресурсов: {0}", blocked)
        lbl_Status.Text = html_View_Title.Text
        UpdateContentWindowTitle()
    End Sub

    ' The blocked summary: categories and origins, never a filesystem path (section 11.1).
    Private Sub FillHtmlViewBlockedMenu(sender As Object, e As EventArgs)
        If Not IsHtmlViewShown() Then Return
        Dim host = html_View_Host
        html_View_Blocked.DropDownItems.Clear()
        Dim origins = host.BlockedOriginList
        html_View_Blocked.DropDownItems.Add(New ToolStripMenuItem(Localization.TF("Заблокировано ресурсов: {0}", host.BlockedResources)) With {.Enabled = False})
        For Each item In origins.Take(HtmlDocumentHost.MaxApprovedOrigins)
            html_View_Blocked.DropDownItems.Add(New ToolStripMenuItem(item) With {.Enabled = False})
        Next
        If origins.Count > 0 Then
            Dim approve As New ToolStripMenuItem(Localization.T("Разрешить ресурсы этих адресов на время просмотра.."))
            AddHandler approve.Click, Sub() ApproveHtmlViewOrigins(origins)
            html_View_Blocked.DropDownItems.Add(New ToolStripSeparator())
            html_View_Blocked.DropDownItems.Add(approve)
        End If
    End Sub

    ' Session-only approval: the URLs and the IP address go to those hosts; nothing is stored.
    Private Async Sub ApproveHtmlViewOrigins(origins As IReadOnlyList(Of String))
        If Not IsHtmlViewShown() OrElse origins.Count = 0 Then Return
        SlideShowStop()
        Dim host = html_View_Host
        Dim list = String.Join(vbLf, origins.Take(HtmlDocumentHost.MaxApprovedOrigins))
        Dim question = Localization.TF("Страница загрузит картинки, стили и шрифты с этих адресов:{0}{1}{0}Эти серверы увидят адреса ресурсов и ваш IP-адрес. Скрипты по-прежнему не выполняются. Разрешение действует до закрытия страницы.", vbLf & vbLf, list)
        If ViewerBox.Show(Me, question, Localization.T("Внешние ресурсы страницы"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        If host IsNot html_View_Host Then Return
        host.ApproveOrigins(origins)
        Try
            Dim outcome = Await host.ReloadAsync()
            If host Is html_View_Host AndAlso outcome <> HtmlOutcome.Ok AndAlso outcome <> HtmlOutcome.Cancelled Then Notify(HtmlViewerText.Outcome(outcome), NotifyLevel.Warning)
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
    End Sub

    Private Function HtmlViewAppearance() As HtmlAppearance
        Dim preferences = GetModernPreferences()
        Dim entry = html_View_Entry
        Dim presentation = If(entry IsNot Nothing AndAlso entry.Presentation.Length > 0, entry.Presentation, preferences.HtmlPresentation)
        Dim theme = If(entry IsNot Nothing AndAlso entry.Theme.Length > 0, entry.Theme, preferences.HtmlTheme)
        If theme = "system" OrElse theme.Length = 0 Then theme = If(ThemeService.Palette.IsDark, "dark", "light")
        Return New HtmlAppearance With {
            .Reader = presentation = "reader",
            .Theme = theme,
            .FontFamily = If(entry IsNot Nothing AndAlso entry.FontFamily.Length > 0, entry.FontFamily, preferences.HtmlFontFamily),
            .FontSize = If(entry IsNot Nothing AndAlso entry.FontSize >= 0, entry.FontSize, preferences.HtmlFontSize),
            .LineHeight = If(entry IsNot Nothing AndAlso entry.LineHeight > 0, entry.LineHeight, preferences.HtmlLineHeight),
            .ColumnWidth = If(entry IsNot Nothing AndAlso entry.ColumnWidth >= 0, entry.ColumnWidth, preferences.HtmlColumnWidth),
            .Margin = If(entry IsNot Nothing AndAlso entry.Margin >= 0, entry.Margin, preferences.HtmlMargin),
            .Wrap = If(entry IsNot Nothing AndAlso entry.WrapWideContent >= 0, entry.WrapWideContent = 1, preferences.HtmlWrapWideContent)}
    End Function

    Private Async Sub HtmlViewContentReady()
        If Not IsHtmlViewShown() Then Return
        Dim host = html_View_Host
        Dim token = host.Token
        Try
            If html_View_Restore Is Nothing AndAlso html_View_Position Is Nothing Then
                ' First page of the session: the place is trusted only for the same source version.
                Dim entry = LookupHtmlState(host.Source?.Request.SourceVersion)
                If entry IsNot Nothing Then
                    html_View_Entry = entry
                    If entry.HasPosition() AndAlso String.Equals(entry.PageKey, host.CurrentLocation, StringComparison.Ordinal) Then
                        html_View_Restore = New HtmlPosition With {.PageKey = entry.PageKey, .Anchor = entry.Anchor, .Ratio = entry.ScrollRatio}
                    End If
                End If
            End If
            Dim appearance = HtmlViewAppearance()
            If appearance.Reader Then Await host.ApplyAppearanceAsync(appearance)
            If host IsNot html_View_Host OrElse Not host.Token.SameSession(token) Then Return
            Dim restore = html_View_Restore
            html_View_Restore = Nothing
            If restore IsNot Nothing Then Await host.RestorePositionAsync(restore)
            If host IsNot html_View_Host Then Return
            If html_View_Query IsNot Nothing AndAlso html_View_Query.Text.Length > 0 AndAlso html_View_Matches.Text.Length > 0 Then StartHtmlViewFind()
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
        UpdateHtmlViewState()
    End Sub

    Private Sub HtmlViewFailed(outcome As HtmlOutcome)
        If Not IsHtmlViewShown() Then Return
        SlideShowStop()
        If outcome = HtmlOutcome.RendererExited OrElse outcome = HtmlOutcome.RendererUnresponsive Then
            ' Explicit choices after a renderer failure: retry, or the source / start page (section 11.3).
            Dim path = html_View_File
            Dim page = html_View_Online
            Dim answer = ViewerBox.Show(Me, HtmlViewerText.Outcome(outcome), Localization.T("Просмотр HTML-страницы"), MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning)
            If answer = DialogResult.Retry Then
                If page IsNot Nothing Then
                    LoadHtmlOnline(page, html_View_InsecureOrigins.Contains(HtmlAddressPolicy.OriginOf(page)))
                ElseIf path.Length > 0 Then
                    LoadHtmlView(path)
                End If
            Else
                FailHtmlView(path, HtmlOutcome.Cancelled)
                If path.Length > 0 Then LoadTextDocument(path)
            End If
            Return
        End If
        If outcome <> HtmlOutcome.RedirectNeedsApproval Then Notify(HtmlViewerText.Outcome(outcome), NotifyLevel.Warning)
        UpdateHtmlViewState()
    End Sub

    Private Sub RunHtmlCommand(command As HtmlHostCommand)
        If Not IsHtmlViewShown() Then Return
        Dim host = html_View_Host
        Dim preferences = GetModernPreferences()
        html_View_LastActivity = DateTime.UtcNow
        Select Case command
            Case HtmlHostCommand.FindStart
                RevealHtmlViewToolbar()
                html_View_Query.Focus()
                html_View_Query.SelectAll()
            Case HtmlHostCommand.FindNext, HtmlHostCommand.FindPrevious
                If html_View_Matches.Text.Length = 0 Then
                    StartHtmlViewFind()
                Else
                    host.FindStep(command = HtmlHostCommand.FindPrevious)
                End If
            Case HtmlHostCommand.LeaveDocument
                HtmlViewEscape()
            Case HtmlHostCommand.ChromeKeyboard
                If Not chrome_Keyboard_Mode Then ChromeHandleKey(Keys.F10)
            Case HtmlHostCommand.Help
                ShowFirstRunHelp()
            Case HtmlHostCommand.Print
                SlideShowStop()
                host.ShowPrint()
            Case HtmlHostCommand.ZoomIn
                host.SetZoom(HtmlZoomSteps.FirstOrDefault(Function(stepValue) stepValue > host.ZoomPercent, ModernViewerPreferences.HtmlZoomMax))
            Case HtmlHostCommand.ZoomOut
                host.SetZoom(HtmlZoomSteps.LastOrDefault(Function(stepValue) stepValue < host.ZoomPercent, ModernViewerPreferences.HtmlZoomMin))
            Case HtmlHostCommand.ZoomReset
                host.SetZoom(100)
            Case HtmlHostCommand.Back
                RunHtmlNavigation(host.BackAsync())
            Case HtmlHostCommand.Forward
                RunHtmlNavigation(host.ForwardAsync())
            Case HtmlHostCommand.Reload
                ReloadHtmlView()
        End Select
    End Sub

    Private Async Sub RunHtmlNavigation(work As Task)
        Try
            RecordHtmlViewState()
            html_View_Position = Nothing
            Await work
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
        UpdateHtmlViewState()
    End Sub

    ' Reload: a new source version; only matching appearance is re-applied (section 11.3).
    Private Async Sub ReloadHtmlView()
        Dim host = html_View_Host
        Try
            Dim position = Await host.ReadPositionAsync()
            If host IsNot html_View_Host Then Return
            html_View_Restore = position
            Dim outcome = Await host.ReloadAsync()
            If host Is html_View_Host AndAlso outcome <> HtmlOutcome.Ok AndAlso outcome <> HtmlOutcome.Cancelled Then
                html_View_Restore = Nothing
                Notify(HtmlViewerText.Outcome(outcome), NotifyLevel.Warning)
            End If
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
    End Sub

    Private Sub HtmlViewQueryKeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.KeyData
            Case Keys.Enter, Keys.F3
                If html_View_Matches.Text.Length = 0 Then StartHtmlViewFind() Else html_View_Host?.FindStep(False)
            Case Keys.Shift Or Keys.Enter, Keys.Shift Or Keys.F3
                html_View_Host?.FindStep(True)
            Case Keys.Escape
                HtmlViewEscape()
            Case Else
                Return
        End Select
        e.Handled = True
        e.SuppressKeyPress = True
    End Sub

    Private Async Sub StartHtmlViewFind()
        If Not IsHtmlViewShown() Then Return
        SlideShowStop()
        Dim host = html_View_Host
        Dim preferences = GetModernPreferences()
        Try
            Await host.FindAsync(html_View_Query.Text, preferences.HtmlFindCase, preferences.HtmlFindWholeWord)
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
            If host Is html_View_Host Then Notify(Localization.T("Не удалось выполнить поиск на странице"), NotifyLevel.Warning)
        End Try
    End Sub

    Private Sub HtmlViewFindChanged(activeIndex As Integer, count As Integer)
        If html_View_Matches Is Nothing Then Return
        If count <= 0 Then
            html_View_Matches.Text = If(html_View_Query.Text.Length = 0, "", Localization.T("Не найдено"))
        Else
            html_View_Matches.Text = Localization.TF("{0} из {1}", Math.Max(1, activeIndex + 1), count)
        End If
    End Sub

    Private Sub HtmlViewZoomChanged(percent As Integer)
        If html_View_ZoomLabel IsNot Nothing Then html_View_ZoomLabel.Text = Localization.TF("{0} %", percent)
        html_View_Zoom = percent
    End Sub

    Private Async Sub FitHtmlViewWidth()
        Try
            Await html_View_Host.FitWidthAsync()
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
    End Sub

    Private Sub ToggleHtmlReader()
        If Not IsHtmlViewShown() Then Return
        Dim reader = Not HtmlViewAppearance().Reader
        If html_View_Entry Is Nothing Then html_View_Entry = New HtmlReaderEntry With {.Identity = html_View_Identity}
        html_View_Entry.Presentation = If(reader, "reader", "original")
        ApplyHtmlViewAppearance()
        RecordHtmlViewState()
    End Sub

    Private Async Sub ApplyHtmlViewAppearance()
        If Not IsHtmlViewShown() Then Return
        Dim host = html_View_Host
        Try
            Await host.ApplyAppearanceAsync(HtmlViewAppearance())
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
        UpdateHtmlViewState()
    End Sub

    ' Reset removes this document's overrides and keeps its place (section 12).
    Private Sub ResetHtmlViewAppearance()
        If Not IsHtmlViewShown() Then Return
        html_View_Entry?.ClearAppearance()
        If html_View_Identity.Length > 0 Then
            html_View_StateTouched = True
            HtmlReaderState.Current.ResetAppearance(html_View_Identity)
        End If
        html_View_Host.SetZoom(GetModernPreferences().HtmlZoom)
        ApplyHtmlViewAppearance()
    End Sub

    ' Show source: the existing read-only text preview of the same file.
    Private Sub ShowHtmlViewSource()
        Dim path = html_View_File
        If path.Length = 0 Then Return
        ReleaseHtmlView()
        LoadTextDocument(path)
    End Sub

    Private Sub OpenHtmlViewInBrowser()
        If Not IsHtmlViewShown() Then Return
        SlideShowStop()
        Try
            If html_View_Online IsNot Nothing Then
                Dim current = If(html_View_Host.CurrentLocation.Length > 0, html_View_Host.CurrentLocation, html_View_Online.AbsoluteUri)
                If Not HtmlBrowserLauncher.OpenUrl(current) Then Notify(Localization.T("Не удалось открыть страницу в браузере"), NotifyLevel.Warning)
                Return
            End If
            If IsSecureContentOnScreen() OrElse IsArchiveMode() OrElse IsResourceMode() Then
                Notify(Localization.T("Сначала извлеките файл из архива"), NotifyLevel.Warning)
                Return
            End If
            Dim local = TryCast(html_View_Host.Source, LocalHtmlDocumentSource)
            Dim path = If(local IsNot Nothing AndAlso html_View_Host.CurrentLocation.Length > 0, local.LocalPathOf(html_View_Host.CurrentLocation), html_View_File)
            If path.Length = 0 Then path = html_View_File
            If Not HtmlBrowserLauncher.OpenLocalFile(path) Then
                ' No registered browser could be resolved: the user picks the program.
                ManagerShellOpen.OpenWithPicker(Handle, path)
            End If
        Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException
            Notify(Localization.T("Не удалось открыть страницу в браузере"), NotifyLevel.Warning)
        End Try
    End Sub

    ' A real click on an external link: ask (or refuse by setting), then the system browser.
    Private Sub HtmlViewExternalLink(url As String, isMail As Boolean)
        If Not IsHtmlViewShown() Then Return
        SlideShowStop()
        If GetModernPreferences().HtmlExternalLinks = "block" Then
            Notify(Localization.T("Внешние ссылки выключены в настройках просмотра HTML"), NotifyLevel.Info)
            Return
        End If
        Dim question = Localization.TF("Открыть ссылку во внешней программе?{0}{1}", vbLf & vbLf, url)
        If ViewerBox.Show(Me, question, Localization.T("Внешняя ссылка"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        Try
            Dim opened = If(isMail, HtmlBrowserLauncher.OpenMail(url), HtmlBrowserLauncher.OpenUrl(url))
            If Not opened Then Notify(Localization.T("Не удалось открыть страницу в браузере"), NotifyLevel.Warning)
        Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
            Notify(Localization.T("Не удалось открыть страницу в браузере"), NotifyLevel.Warning)
        End Try
    End Sub

    ' A same-root link to a file that is not HTML goes to the typed opener, after the user agrees.
    Private Sub HtmlViewTypedOpen(localPath As String)
        If Not IsHtmlViewShown() OrElse String.IsNullOrEmpty(localPath) Then Return
        SlideShowStop()
        Dim question = Localization.TF("Открыть файл в просмотрщике?{0}{1}", vbLf & vbLf, Path.GetFileName(localPath))
        If ViewerBox.Show(Me, question, Localization.T("Просмотр HTML-страницы"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        ProcessArgument(localPath)
    End Sub

    ' A cross-origin redirect of an online page: a queued decision before any new fetch (section 13).
    Private Sub HtmlViewRedirect(target As Uri)
        If target Is Nothing Then Return
        SlideShowStop()
        Dim question = Localization.TF("Страница перенаправляет на другой адрес:{0}{1}{0}Открыть его?", vbLf & vbLf, HtmlAddressPolicy.OriginOf(target))
        If ViewerBox.Show(Me, question, Localization.T("Открыть HTML-страницу по адресу"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        OpenHtmlPage(target.AbsoluteUri, confirmed:=True)
    End Sub

    Private Async Sub TickHtmlView(sender As Object, e As EventArgs)
        If Not IsHtmlViewShown() Then Return
        Dim host = html_View_Host
        If host.DocumentHasFocus AndAlso Not html_View_Interaction Then EnterHtmlViewInteraction(Me, EventArgs.Empty)
        Dim pointer = panel_Media.PointToClient(Cursor.Position)
        Dim visible = Not (is_Full_Screen_Mode OrElse is_Super_Full_Screen_Mode) OrElse GetModernPreferences().DocumentFullscreenChrome = "visible" OrElse
            html_View_Toolbar.ContainsFocus OrElse (pointer.X >= 0 AndAlso pointer.X < panel_Media.Width AndAlso pointer.Y >= 0 AndAlso pointer.Y < html_View_Toolbar.Height)
        If visible Then html_View_LastActivity = DateTime.UtcNow
        Dim wanted = visible OrElse (DateTime.UtcNow - html_View_LastActivity).TotalSeconds < 3
        If html_View_Toolbar.Visible <> wanted Then
            html_View_Toolbar.Visible = wanted
            LayoutHtmlView(Me, EventArgs.Empty)
        End If
        If html_View_PositionBusy OrElse html_View_Restore IsNot Nothing OrElse Not Visible OrElse WindowState = FormWindowState.Minimized Then Return
        html_View_PositionBusy = True
        Try
            Dim position = Await host.ReadPositionAsync()
            If host Is html_View_Host AndAlso position IsNot Nothing Then html_View_Position = position
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        Finally
            If host Is html_View_Host Then html_View_PositionBusy = False
        End Try
    End Sub

    Private Sub RevealHtmlViewToolbar()
        If html_View_Toolbar Is Nothing OrElse html_View_Toolbar.Visible Then Return
        html_View_Toolbar.Visible = True
        LayoutHtmlView(Me, EventArgs.Empty)
    End Sub

    ' Memory only; the file is written once, when the page is left.
    Private Sub RecordHtmlViewState()
        If Not IsHtmlViewShown() OrElse html_View_Identity.Length = 0 OrElse Is_slide_show_mode Then Return
        Dim preferences = GetModernPreferences()
        If preferences.HtmlHistoryLimit <= 0 Then Return
        Dim version = html_View_Host.Source?.Request.SourceVersion
        Dim entry = If(html_View_Entry?.Copy(), New HtmlReaderEntry())
        entry.Identity = html_View_Identity
        If version IsNot Nothing Then
            entry.Length = version.Length
            entry.LastWriteUtcTicks = version.LastWriteUtcTicks
            entry.Digest = version.Digest
        End If
        Dim position = html_View_Position
        If position IsNot Nothing Then
            entry.PageKey = position.PageKey
            entry.Anchor = position.Anchor
            entry.ScrollRatio = position.Ratio
        End If
        entry.Zoom = If(html_View_Zoom = preferences.HtmlZoom, 0, html_View_Zoom)
        html_View_StateTouched = True
        HtmlReaderState.Current.Record(entry, preferences.HtmlHistoryLimit, preferences.HtmlRememberPosition, preferences.HtmlRememberAppearance)
    End Sub

    Private Sub HtmlViewEscape()
        If Not IsHtmlViewShown() Then Return
        If html_View_Matches IsNot Nothing AndAlso html_View_Matches.Text.Length > 0 Then
            html_View_Host.StopFind()
            html_View_Matches.Text = ""
            If html_View_Query.Focused Then html_View_Host.FocusDocument()
            Return
        End If
        LeaveHtmlViewInteraction()
    End Sub

    ' Section 11.2: the document gate in front of every file command. Inside the document the page
    ' owns the keys (never sorting or delete); on the host canvas the existing map applies.
    Private Function HtmlViewOwnsInput(e As KeyEventArgs) As Boolean
        If Not IsHtmlViewShown() Then Return False
        html_View_LastActivity = DateTime.UtcNow
        RevealHtmlViewToolbar()
        Dim owns = html_View_Interaction OrElse html_View_Host.DocumentHasFocus OrElse html_View_Toolbar.ContainsFocus
        If e.KeyCode = Keys.Escape AndAlso owns Then
            HtmlViewEscape()
            e.SuppressKeyPress = True
            Return True
        End If
        If Not owns Then
            If e.KeyData = Keys.PageUp OrElse e.KeyData = Keys.PageDown Then
                ScrollHtmlView(e.KeyData = Keys.PageDown)
                e.SuppressKeyPress = True
                Return True
            End If
            Return False
        End If
        Select Case e.KeyData
            Case Keys.F1
                Return False
            Case Keys.Control Or Keys.F
                RunHtmlCommand(HtmlHostCommand.FindStart)
            Case Keys.F3, Keys.Shift Or Keys.F3
                RunHtmlCommand(If(e.Shift, HtmlHostCommand.FindPrevious, HtmlHostCommand.FindNext))
        End Select
        SlideShowStop()
        Return True
    End Function

    ' The wheel over the document scrolls it; it never also flips the file (section 11.2).
    Private Function HtmlViewOwnsWheel(e As MouseEventArgs) As Boolean
        If Not IsHtmlViewShown() Then Return False
        Dim host = html_View_Host
        If Not host.RectangleToScreen(host.ClientRectangle).Contains(Cursor.Position) Then Return False
        If (ModifierKeys And Keys.Control) = Keys.Control Then
            RunHtmlCommand(If(e.Delta > 0, HtmlHostCommand.ZoomIn, HtmlHostCommand.ZoomOut))
        Else
            ScrollHtmlViewBy(-e.Delta)
        End If
        Return True
    End Function

    Private Async Sub ScrollHtmlView(forward As Boolean)
        Try
            Await html_View_Host.ScrollAsync(forward)
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
    End Sub

    Private Async Sub ScrollHtmlViewBy(pixels As Integer)
        Try
            Await html_View_Host.ScrollByAsync(pixels)
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
        End Try
    End Sub

    ' Settings changed: switching off closes the page into the source preview, switching on
    ' renders the shown .html, appearance follows the explicit Reader choice.
    Private Sub ApplyHtmlViewPreferences(previous As ModernViewerPreferences)
        Dim preferences = GetModernPreferences()
        If IsHtmlViewShown() Then
            If Not preferences.HtmlViewerEnabled OrElse (html_View_Online IsNot Nothing AndAlso Not preferences.HtmlOnlinePagesEnabled) Then
                Dim path = html_View_File
                ReleaseHtmlView()
                If path.Length > 0 Then LoadTextDocument(path) Else ShowContentPlaceholder("")
                Return
            End If
            ApplyHtmlViewAppearance()
            Return
        End If
        If previous IsNot Nothing AndAlso Not previous.HtmlViewerEnabled AndAlso preferences.HtmlViewerEnabled AndAlso IsTextDocumentShown() AndAlso
           IsHtmlExtension(MediaKindClassifier.ExtensionOf(Current_File_Name)) Then
            LoadHtmlView(Current_File_Name)
        End If
    End Sub

    ' Section 13: Open HTML page URL - shows the origin before anything connects.
    Friend Sub ShowOpenHtmlPagePrompt()
        SlideShowStop()
        Dim preferences = GetModernPreferences()
        If Not preferences.HtmlViewerEnabled OrElse Not preferences.HtmlOnlinePagesEnabled Then
            Notify(HtmlViewerText.Outcome(HtmlOutcome.Disabled), NotifyLevel.Info)
            Return
        End If
        Dim validator As Func(Of String, String) =
            Function(text As String) As String
                Dim page As Uri = Nothing
                Return If(HtmlDocumentAdapters.TryParseOnlinePage(text, page), Nothing, Localization.T("Нужен адрес вида https://example.org/page.html"))
            End Function
        Dim spec As New PromptSpec(Localization.T("Открыть HTML-страницу по адресу"), Localization.T("Адрес страницы (https):"), last_Html_Page_Url,
            Localization.T("Далее"), validator, hint:=Localization.T("Страница откроется только для чтения: без скриптов, входа на сайт и сохранения cookies."), selectAll:=True)
        Dim entered As String = AskDialogs.Prompt(Me, spec)
        If entered Is Nothing Then Return
        OpenHtmlPage(entered.Trim(), confirmed:=False)
    End Sub

    Private Sub OpenHtmlPage(address As String, confirmed As Boolean)
        Dim page As Uri = Nothing
        If Not HtmlDocumentAdapters.TryParseOnlinePage(address, page) Then
            Notify(Localization.T("Нужен адрес вида https://example.org/page.html"), NotifyLevel.Warning)
            Return
        End If
        Dim preferences = GetModernPreferences()
        If Not preferences.HtmlViewerEnabled OrElse Not preferences.HtmlOnlinePagesEnabled Then
            Notify(HtmlViewerText.Outcome(HtmlOutcome.Disabled), NotifyLevel.Info)
            Return
        End If
        Dim origin = HtmlAddressPolicy.OriginOf(page)
        If Not confirmed Then
            Dim question = Localization.TF("Страница будет загружена с адреса:{0}{1}{0}Сервер увидит обычный запрос и ваш IP-адрес. Скрипты страницы не выполняются, cookies и данные входа не передаются.", vbLf & vbLf, origin)
            If ViewerBox.Show(Me, question, Localization.T("Открыть HTML-страницу по адресу"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        End If
        Dim insecure = page.Scheme = Uri.UriSchemeHttp
        If insecure AndAlso Not html_View_InsecureOrigins.Contains(origin) Then
            Dim warning = Localization.TF("Адрес {0} использует незащищённое соединение HTTP: страницу можно подсмотреть или подменить по дороге. Открыть всё равно?", origin)
            If ViewerBox.Show(Me, warning, Localization.T("Незащищённое соединение"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) <> DialogResult.OK Then Return
            html_View_InsecureOrigins.Add(origin)
        End If
        last_Html_Page_Url = page.GetLeftPart(UriPartial.Query)
        LoadHtmlOnline(page, insecure)
    End Sub

    Private Sub ReleaseHtmlView()
        If html_View_Host Is Nothing AndAlso html_View_Toolbar Is Nothing Then Return
        Try
            RecordHtmlViewState()
            If html_View_StateTouched Then HtmlReaderState.Current.Save()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is InvalidOperationException
        End Try
        html_View_Timer?.Dispose()
        html_View_Timer = Nothing
        RemoveHandler panel_Media.SizeChanged, AddressOf LayoutHtmlView
        Dim host = html_View_Host
        html_View_Host = Nothing
        host?.Dispose()
        html_View_Toolbar?.Dispose()
        html_View_Toolbar = Nothing
        html_View_File = ""
        html_View_Identity = ""
        html_View_Entry = Nothing
        html_View_Position = Nothing
        html_View_Restore = Nothing
        html_View_Online = Nothing
        html_View_Interaction = False
        html_View_PositionBusy = False
        html_View_StateTouched = False
    End Sub
End Class
#End If
