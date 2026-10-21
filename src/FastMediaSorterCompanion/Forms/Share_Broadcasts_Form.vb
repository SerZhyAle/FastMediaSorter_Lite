Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Form for managing live StreamsPlayer broadcasts and configuring the HTTP gateway.
''' </summary>
Public NotInheritable Class Share_Broadcasts_Form
    Inherits Form

    Private _iconHandle As IntPtr
    Private _status As WorkerStatus
    Private _broadcasts As New List(Of WorkerBroadcast)()
    Private _initialTargetPath As String
    ''' <summary>The worker under this page stopped serving the broadcast verbs (it was swapped, or never had
    ''' them): the toggle and every action are off and one inline line says why (spec 159 section 3.2).</summary>
    Private _degraded As Boolean
    Private _refreshInFlight As Boolean
    Private ReadOnly _refreshTimer As New Timer With {.Interval = 5000}

    Private chkEnableGateway As CheckBox
    Private lblGatewayDesc As Label
    Private lblGatewayStatus As Label
    Private lblNotice As Label
    Private lvBroadcasts As ListView
    Private btnCreate As Button
    Private btnCopyLink As Button
    Private btnSaveSpres As Button
    Private btnShowQr As Button
    Private btnRefresh As Button
    Private btnRevoke As Button
    Private btnClose As Button
    Private _answerRow As DialogButtonRow

    Public Sub New(Optional initialTargetPath As String = "")
        _initialTargetPath = If(initialTargetPath, "").Trim()
        BuildUi()
    End Sub

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Трансляции для StreamsPlayer")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        AddHandler Me.FormClosed, Sub()
                                     _refreshTimer.Stop()
                                     _refreshTimer.Dispose()
                                 End Sub
        AddHandler _refreshTimer.Tick, Async Sub() Await RefreshStatusAndListAsync()
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New Size(700, 480)
        Me.Size = New Size(760, 520)

        Dim pnlMain As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .Padding = New Padding(16, 16, 16, 0),
            .ColumnCount = 1,
            .RowCount = 4
        }
        ' The column follows the window (it used to follow the widest content, the one-line actions row): a window clamped to a
        ' small monitor or opened at a high scale wraps the actions instead of pushing the list past its edge.
        pnlMain.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        ' Header & Gateway Toggle
        chkEnableGateway = New CheckBox With {
            .AutoSize = True,
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size + 1.0F, FontStyle.Bold),
            .Text = Localization.T("Включить HTTP-шлюз для трансляций"),
            .Margin = New Padding(0, 0, 0, 4)
        }
        AddHandler chkEnableGateway.CheckedChanged, AddressOf OnGatewayToggleChanged

        ' The gateway switch is a boolean row (BOOLEAN-OPTION 2.0, ticket 243 T-11c-b2a): the check box keeps its field, handler and
        ' state; its description is the row's explanation and the status and notice lines stay under the row, indented to the caption.
        Dim pnlDesc As New FlowLayoutPanel With {
            .AutoSize = True,
            .FlowDirection = FlowDirection.TopDown,
            .Margin = New Padding(0, 0, 0, 10)
        }
        lblGatewayDesc = New Label With {
            .AutoSize = True,
            .ForeColor = Color.DimGray,
            .MaximumSize = New Size(700, 0),
            .Text = Localization.T("Ваш ПК будет отвечать на HTTP-запросы к транслируемым файлам, пока действует трансляция.")
        }
        lblGatewayStatus = New Label With {
            .AutoSize = True,
            .Margin = New Padding(0, 4, 0, 0),
            .ForeColor = Color.DarkSlateGray,
            .Text = Localization.T("Шлюз трансляций выключен")
        }
        ' The page's one place for a refusal or a failure: an inline line, never a dialog (spec 159 section 3.3).
        lblNotice = New Label With {
            .AutoSize = True,
            .Margin = New Padding(0, 4, 0, 0),
            .ForeColor = Color.Firebrick,
            .MaximumSize = New Size(700, 0),
            .Visible = False
        }
        Dim gatewayRow As OptionRowHost = OptionRows.InFlow(chkEnableGateway, lblGatewayDesc)
        gatewayRow.Caption.Font = chkEnableGateway.Font
        gatewayRow.Margin = New Padding(0, 0, 0, 4)
        pnlMain.Controls.Add(gatewayRow, 0, 0)
        pnlDesc.Controls.Add(lblGatewayStatus)
        pnlDesc.Controls.Add(lblNotice)
        pnlMain.Controls.Add(pnlDesc, 0, 1)
        ' The two lines under the row start where its caption starts (the edge inset, the box and the gap; 96-dpi px, scaled
        ' with the window), on the reading-start side. A table cell mirrors its children under RightToLeft, margin included, so
        ' Margin.Left is the reading-start side in both directions.
        Dim captionInset As Integer = OptionRows.FlowEdgeInsetLogical + SettingsCheckBox.BoxSideLogical + SettingsCheckBox.CaptionGapLogical
        pnlDesc.Margin = New Padding(captionInset, 0, 0, 10)

        ' Broadcasts ListView
        lvBroadcasts = New ListView With {
            .Dock = DockStyle.Fill,
            .View = View.Details,
            .FullRowSelect = True,
            .MultiSelect = False,
            .HideSelection = False
        }
        lvBroadcasts.Columns.Add(Localization.T("Название"), 220)
        lvBroadcasts.Columns.Add(Localization.T("Тип"), 90)
        lvBroadcasts.Columns.Add(Localization.T("Создана"), 140)
        lvBroadcasts.Columns.Add(Localization.T("Срок действия"), 140)
        lvBroadcasts.Columns.Add(Localization.T("Запросов"), 70)
        lvBroadcasts.Columns.Add(Localization.T("Токен"), 90)
        AddHandler lvBroadcasts.SelectedIndexChanged, AddressOf OnSelectionChanged
        AddHandler lvBroadcasts.DoubleClick, AddressOf OnCopyLinkClick
        pnlMain.Controls.Add(lvBroadcasts, 0, 2)

        ' Per-row actions row
        Dim pnlActions As New FlowLayoutPanel With {
            .AutoSize = True,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = True,
            .Margin = New Padding(0, 10, 0, 10)
        }

        btnCreate = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Создать трансляцию..")
        }
        ButtonGlyphs.Apply(btnCreate, "action.add")
        AddHandler btnCreate.Click, AddressOf OnCreateClick

        btnCopyLink = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Копировать ссылку"),
            .Enabled = False
        }
        ButtonGlyphs.Apply(btnCopyLink, "action.copy")
        AddHandler btnCopyLink.Click, AddressOf OnCopyLinkClick

        btnSaveSpres = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Сохранить .spres.."),
            .Enabled = False
        }
        ButtonGlyphs.Apply(btnSaveSpres, "action.export")
        AddHandler btnSaveSpres.Click, AddressOf OnSaveSpresClick

        btnShowQr = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Показать QR"),
            .Enabled = False
        }
        ButtonGlyphs.Apply(btnShowQr, "action.show-code")
        AddHandler btnShowQr.Click, AddressOf OnShowQrClick

        btnRefresh = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Обновить"),
            .Enabled = False
        }
        ButtonGlyphs.Apply(btnRefresh, "action.refresh")
        AddHandler btnRefresh.Click, AddressOf OnRefreshClick

        btnRevoke = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .ForeColor = Color.Firebrick,
            .Text = Localization.T("Отозвать"),
            .Enabled = False
        }
        ButtonGlyphs.Apply(btnRevoke, "action.remove")
        AddHandler btnRevoke.Click, AddressOf OnRevokeClick

        pnlActions.Controls.AddRange(New Control() {btnCreate, btnCopyLink, btnSaveSpres, btnShowQr, btnRefresh, btnRevoke})
        pnlMain.Controls.Add(pnlActions, 0, 3)

        ' Close is the window's one answer and rides the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4):
        ' the row sizes it from its caption and glyph and binds Escape and Enter to it. The actions row above (Create, Copy
        ' link, Save, QR, Refresh, Revoke) works on the selected broadcast of the list - per-row commands, not answers - and
        ' stays where it is.
        btnClose = New Button With {
            .Name = "xsb_Close",
            .AutoSize = True,
            .Text = Localization.T("Закрыть"),
            .DialogResult = DialogResult.OK
        }
        AddHandler btnClose.Click, Sub() Me.Close()
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xsb_Buttons", .Padding = New Padding(16, 4, 16, 12)}
        DpiLayout.AddAnswer(_answerRow, btnClose, DialogButtonRole.Dismissing, "nav.close")

        Me.Controls.Add(pnlMain)
        ' Last in the collection: the docked row takes its edge first and reads after the list in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)
        DpiLayout.ApplyAutoScale(Me)

        AddHandler Me.Shown, AddressOf OnShownAsync
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        OptionRows.ArrangeAll(Me)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Async Sub OnShownAsync(sender As Object, e As EventArgs)
        Await RefreshStatusAndListAsync()
        If IsDisposed Then Return
        _refreshTimer.Start()

        If Not String.IsNullOrEmpty(_initialTargetPath) Then
            Dim path As String = _initialTargetPath
            _initialTargetPath = ""
            If _degraded Then Return
            Await PromptCreateBroadcastAsync(path)
        End If
    End Sub

    ''' <summary>Shows one inline line (empty hides it). The only surface a refusal or failure uses.</summary>
    Private Sub ShowNotice(text As String)
        lblNotice.Text = If(text, "")
        lblNotice.Visible = lblNotice.Text.Length > 0
    End Sub

    ''' <summary>Turns the page off in place, or back on: the toggle, Create and the per-row actions.</summary>
    Private Sub SetDegraded(degraded As Boolean)
        _degraded = degraded
        chkEnableGateway.Enabled = Not degraded
        btnCreate.Enabled = Not degraded
        UpdateActionButtons()
    End Sub

    ''' <summary>
    ''' One failed request, one inline sentence, details in the log. A worker that says it does not know the
    ''' verb turns the page off in place (the secondary guard of spec 159 section 3.1); any other failure
    ''' leaves the page usable, since a refused port or one lost answer is not a reason to lock it.
    ''' </summary>
    Private Sub ReportFailure(resp As WorkerResponse, gatewayRequest As Boolean, action As String)
        Dim reason As BroadcastNoticeReason = BroadcastMessages.ReasonFor(resp, gatewayRequest)
        Dim detail As String = "no answer"
        If resp IsNot Nothing Then
            detail = If(resp.IsProblem, "exchange problem " & resp.Problem.ToString(), If(resp.Error, ""))
        End If
        AppFileLogger.WriteLine("Broadcasts: " & action & " failed (" & reason.ToString() & "): " & detail)
        If reason = BroadcastNoticeReason.WorkerTooOld Then SetDegraded(True)
        ShowNotice(BroadcastMessages.Text(reason, If(resp Is Nothing, "", resp.Error)))
    End Sub

    Public Async Function RefreshStatusAndListAsync() As Task
        If IsDisposed OrElse _refreshInFlight Then Return
        _refreshInFlight = True
        Try
            Dim statusResp As WorkerStatus = Await ShareController.GetStatusAsync()
            If IsDisposed Then Return
            _status = statusResp
            If _status Is Nothing Then
                ' The worker did not answer (not running, or it refused): nothing on this page can work and
                ' the page does not know why - so it says the one true generic thing and keeps the detail in the log.
                AppFileLogger.WriteLine("Broadcasts: the worker's status is unavailable")
                SetDegraded(True)
                ShowNotice(BroadcastMessages.Text(BroadcastNoticeReason.Failed))
                Return
            End If

            ' The worker states what it can do; the page never guesses it from the wording of a refusal.
            If Not BroadcastController.IsSupported(_status) Then
                AppFileLogger.WriteLine("Broadcasts: the worker does not report broadcastSupported")
                SetDegraded(True)
                ShowNotice(BroadcastMessages.Text(BroadcastNoticeReason.WorkerTooOld))
                Return
            End If

            RemoveHandler chkEnableGateway.CheckedChanged, AddressOf OnGatewayToggleChanged
            chkEnableGateway.Checked = _status.BroadcastEnabled
            AddHandler chkEnableGateway.CheckedChanged, AddressOf OnGatewayToggleChanged

            If _status.BroadcastEnabled Then
                Dim port As Integer = _status.BroadcastPort
                lblGatewayStatus.Text = Localization.TF("Шлюз трансляций включён (порт {0})", port)
                lblGatewayStatus.ForeColor = Color.DarkGreen
            Else
                lblGatewayStatus.Text = Localization.T("Шлюз трансляций выключен")
                lblGatewayStatus.ForeColor = Color.DarkSlateGray
            End If

            Dim listResp As WorkerResponse = Await BroadcastController.ListBroadcastsAsync()
            If IsDisposed Then Return
            If listResp IsNot Nothing AndAlso listResp.Ok Then
                _broadcasts = If(listResp.Broadcasts, New List(Of WorkerBroadcast)())
                SetDegraded(False)
                ShowNotice("")
                PopulateListView()
            Else
                ReportFailure(listResp, gatewayRequest:=False, action:="ListBroadcasts")
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Share_Broadcasts_Form.RefreshStatusAndListAsync", ex)
        Finally
            _refreshInFlight = False
        End Try
    End Function

    Private Sub PopulateListView()
        Dim selected = SelectedBroadcast()
        Dim selectedId As String = If(selected Is Nothing, "", selected.Id)
        lvBroadcasts.BeginUpdate()
        lvBroadcasts.Items.Clear()
        For Each b As WorkerBroadcast In _broadcasts
            Dim item As New ListViewItem(b.Title)
            Dim isFolder As Boolean = String.Equals(b.Kind, "folder", StringComparison.OrdinalIgnoreCase)
            Dim typeStr As String = If(isFolder, Localization.TF("Папка ({0})", b.ItemCount), Localization.T("Файл"))
            item.SubItems.Add(typeStr)
            item.SubItems.Add(b.CreatedAt)

            item.SubItems.Add(BroadcastMessages.ExpiryCell(b))
            item.SubItems.Add(b.Hits.ToString())

            Dim tokPrefix As String = If(b.Token.Length > 6, b.Token.Substring(0, 6) & "..", b.Token)
            item.SubItems.Add(tokPrefix)
            item.Tag = b
            lvBroadcasts.Items.Add(item)
            item.Selected = String.Equals(b.Id, selectedId, StringComparison.Ordinal)
        Next
        lvBroadcasts.EndUpdate()
        UpdateActionButtons()
    End Sub

    Private Sub OnSelectionChanged(sender As Object, e As EventArgs)
        UpdateActionButtons()
    End Sub

    Private Sub UpdateActionButtons()
        Dim sel As WorkerBroadcast = SelectedBroadcast()
        Dim hasSel As Boolean = sel IsNot Nothing AndAlso Not _degraded
        Dim canExport As Boolean = hasSel AndAlso _status IsNot Nothing AndAlso
            _status.BroadcastEnabled AndAlso _status.BroadcastPort > 0
        btnCopyLink.Enabled = canExport
        btnSaveSpres.Enabled = canExport
        btnShowQr.Enabled = canExport
        btnRefresh.Enabled = hasSel AndAlso String.Equals(sel.Kind, "folder", StringComparison.OrdinalIgnoreCase)
        btnRevoke.Enabled = hasSel
    End Sub

    Private Function SelectedBroadcast() As WorkerBroadcast
        If lvBroadcasts.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(lvBroadcasts.SelectedItems(0).Tag, WorkerBroadcast)
    End Function

    Private Async Sub OnGatewayToggleChanged(sender As Object, e As EventArgs)
        If _degraded Then Return
        Dim enable As Boolean = chkEnableGateway.Checked
        ShowNotice("")
        Dim resp As WorkerResponse = Await BroadcastController.SetBroadcastPolicyAsync(enable)
        If resp IsNot Nothing AndAlso resp.Ok Then
            Await RefreshStatusAndListAsync()
        Else
            ' The box goes back to what is true BEFORE the line is shown, so the one reason on screen
            ' (a refused port, the firewall) is never next to a checkbox that says it worked.
            RemoveHandler chkEnableGateway.CheckedChanged, AddressOf OnGatewayToggleChanged
            chkEnableGateway.Checked = Not enable
            AddHandler chkEnableGateway.CheckedChanged, AddressOf OnGatewayToggleChanged
            ReportFailure(resp, gatewayRequest:=True, action:="SetBroadcastPolicy")
        End If
    End Sub

    Private Async Sub OnCreateClick(sender As Object, e As EventArgs)
        If _degraded Then Return
        Using dlg As New OpenFileDialog With {
            .Title = Localization.T("Выбрать медиафайл для трансляции"),
            .Filter = Localization.T("Медиафайлы|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.mp3;*.flac;*.wav;*.m4a;*.ogg;*.opus|Все файлы (*.*)|*.*")
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Await PromptCreateBroadcastAsync(dlg.FileName)
            End If
        End Using
    End Sub

    Public Async Function PromptCreateBroadcastAsync(targetPath As String) As Task
        If String.IsNullOrWhiteSpace(targetPath) OrElse _degraded Then Return
        Using createDlg As New Broadcast_Create_Form(targetPath)
            If createDlg.ShowDialog(Me) = DialogResult.OK Then
                ShowNotice("")
                ' Ensure gateway is enabled if not already. A gateway that will not start is not a place to
                ' create a link that cannot be served: the worker's own refusal (port, firewall) is the answer.
                If _status Is Nothing OrElse Not _status.BroadcastEnabled Then
                    Dim policy As WorkerResponse = Await BroadcastController.SetBroadcastPolicyAsync(True)
                    If policy Is Nothing OrElse Not policy.Ok Then
                        ReportFailure(policy, gatewayRequest:=True, action:="SetBroadcastPolicy")
                        Return
                    End If
                End If

                Dim kind As String = If(createDlg.IsFolderKind, "folder", "file")
                Dim createResp As WorkerResponse = Await BroadcastController.CreateBroadcastAsync(
                    kind, createDlg.TargetPath, createDlg.Recursive, createDlg.BroadcastTitle, createDlg.TtlSeconds)

                If createResp IsNot Nothing AndAlso createResp.Ok AndAlso createResp.Broadcast IsNot Nothing Then
                    BroadcastExportPreferences.Remember(createResp.Broadcast.Id, createDlg.IncludeExternal)
                    Await RefreshStatusAndListAsync()

                    ' Build .spres and config result
                    Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(createResp.Broadcast, _status, createDlg.IncludeExternal)
                    If config IsNot Nothing Then
                        ' Copy SPCH1 quick link to clipboard
                        If Not SecureClipboard.SetSecret(config.ClipboardLink) Then
                            ShowNotice(Localization.T("Не удалось скопировать ссылку в буфер обмена."))
                            Return
                        End If

                        MessageBox.Show(Me,
                            Localization.T("Ссылка на трансляцию скопирована в буфер обмена."),
                            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                    End If
                Else
                    ReportFailure(createResp, gatewayRequest:=False, action:="CreateBroadcast")
                End If
            End If
        End Using
    End Function

    Private Sub OnCopyLinkClick(sender As Object, e As EventArgs)
        Dim b As WorkerBroadcast = SelectedBroadcast()
        If b Is Nothing OrElse _status Is Nothing Then Return

        Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(b, _status, BroadcastExportPreferences.IncludeExternal(b.Id))
        If config IsNot Nothing AndAlso Not String.IsNullOrEmpty(config.ClipboardLink) Then
            Try
                If Not SecureClipboard.SetSecret(config.ClipboardLink) Then
                    ShowNotice(Localization.T("Не удалось скопировать ссылку в буфер обмена."))
                    Return
                End If
                MessageBox.Show(Me, Localization.T("Ссылка на трансляцию скопирована в буфер обмена."),
                                Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As Exception
                AppFileLogger.LogException("Share_Broadcasts_Form.OnCopyLinkClick", ex)
                MessageBox.Show(Me, Localization.T("Не удалось скопировать ссылку в буфер обмена."),
                                Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End If
    End Sub

    Private Sub OnSaveSpresClick(sender As Object, e As EventArgs)
        Dim b As WorkerBroadcast = SelectedBroadcast()
        If b Is Nothing OrElse _status Is Nothing Then Return

        Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(b, _status, BroadcastExportPreferences.IncludeExternal(b.Id))
        If config Is Nothing OrElse String.IsNullOrEmpty(config.ConfigJson) Then Return

        Dim safeName As String = "broadcast"
        If Not String.IsNullOrWhiteSpace(b.Title) Then
            safeName = String.Join("_", b.Title.Split(Path.GetInvalidFileNameChars()))
        End If

        Using dlg As New SaveFileDialog With {
            .Title = Localization.T("Сохранить ресурс трансляции StreamsPlayer"),
            .Filter = Localization.T("Файл трансляции StreamsPlayer (*.spres)|*.spres"),
            .FileName = safeName & ".spres"
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Try
                    File.WriteAllText(dlg.FileName, config.ConfigJson, New UTF8Encoding(False))
                    MessageBox.Show(Me, Localization.T("Файл .spres успешно сохранён."),
                                    Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                Catch ex As Exception
                    AppFileLogger.LogException("Share_Broadcasts_Form.OnSaveSpresClick", ex)
                    MessageBox.Show(Me, Localization.T("Не удалось сохранить файл .spres."),
                                    Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End Try
            End If
        End Using
    End Sub

    Private Sub OnShowQrClick(sender As Object, e As EventArgs)
        Dim b As WorkerBroadcast = SelectedBroadcast()
        If b Is Nothing OrElse _status Is Nothing Then Return

        Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(b, _status, BroadcastExportPreferences.IncludeExternal(b.Id))
        If config IsNot Nothing AndAlso config.QrPng IsNot Nothing Then
            Using ms As New MemoryStream(config.QrPng)
                Using img As Image = Image.FromStream(ms)
                    Qr_Zoom_Form.ShowImage(Me, img, b.Title)
                End Using
            End Using
        Else
            MessageBox.Show(Me, Localization.T("Не удалось сгенерировать QR-код."),
                            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    Private Async Sub OnRefreshClick(sender As Object, e As EventArgs)
        Dim b As WorkerBroadcast = SelectedBroadcast()
        If b Is Nothing Then Return
        Dim resp As WorkerResponse = Await BroadcastController.RefreshBroadcastAsync(b.Id)
        If resp IsNot Nothing AndAlso resp.Ok Then
            Await RefreshStatusAndListAsync()
            MessageBox.Show(Me, Localization.T("Трансляция обновлена."),
                            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            ReportFailure(resp, gatewayRequest:=False, action:="RefreshBroadcast")
        End If
    End Sub

    Private Async Sub OnRevokeClick(sender As Object, e As EventArgs)
        Dim b As WorkerBroadcast = SelectedBroadcast()
        If b Is Nothing Then Return

        Dim answer As DialogResult = ManagerBox.Show(Me,
            Localization.TF("Отозвать трансляцию {0}? Ссылка перестанет работать немедленно.", b.Title),
            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If answer <> DialogResult.Yes Then Return

        Dim resp As WorkerResponse = Await BroadcastController.RevokeBroadcastAsync(b.Id)
        If resp IsNot Nothing AndAlso resp.Ok Then
            Await RefreshStatusAndListAsync()
            MessageBox.Show(Me, Localization.T("Трансляция отозвана."),
                            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            ReportFailure(resp, gatewayRequest:=False, action:="RevokeBroadcast")
        End If
    End Sub

End Class
