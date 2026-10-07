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

    Public Sub New(Optional initialTargetPath As String = "")
        _initialTargetPath = If(initialTargetPath, "").Trim()
        BuildUi()
    End Sub

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Трансляции для StreamsPlayer")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New Size(700, 480)
        Me.Size = New Size(760, 520)

        Dim pnlMain As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .Padding = New Padding(16),
            .ColumnCount = 1,
            .RowCount = 5
        }
        pnlMain.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        pnlMain.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        ' Header & Gateway Toggle
        chkEnableGateway = New CheckBox With {
            .AutoSize = True,
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size + 1.0F, FontStyle.Bold),
            .Text = Localization.T("Включить HTTP-шлюз для трансляций"),
            .Margin = New Padding(0, 0, 0, 4)
        }
        AddHandler chkEnableGateway.CheckedChanged, AddressOf OnGatewayToggleChanged
        pnlMain.Controls.Add(chkEnableGateway, 0, 0)

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
        pnlDesc.Controls.Add(lblGatewayDesc)
        pnlDesc.Controls.Add(lblGatewayStatus)
        pnlDesc.Controls.Add(lblNotice)
        pnlMain.Controls.Add(pnlDesc, 0, 1)
        lblGatewayDesc.Margin = Padding.Empty
        CheckBoxLayout.BindHint(chkEnableGateway, pnlDesc)

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
        AddHandler btnCreate.Click, AddressOf OnCreateClick

        btnCopyLink = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Копировать ссылку"),
            .Enabled = False
        }
        AddHandler btnCopyLink.Click, AddressOf OnCopyLinkClick

        btnSaveSpres = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Сохранить .spres.."),
            .Enabled = False
        }
        AddHandler btnSaveSpres.Click, AddressOf OnSaveSpresClick

        btnShowQr = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Показать QR"),
            .Enabled = False
        }
        AddHandler btnShowQr.Click, AddressOf OnShowQrClick

        btnRefresh = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .Text = Localization.T("Обновить"),
            .Enabled = False
        }
        AddHandler btnRefresh.Click, AddressOf OnRefreshClick

        btnRevoke = New Button With {
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Margin = New Padding(0, 0, 6, 0),
            .ForeColor = Color.Firebrick,
            .Text = Localization.T("Отозвать"),
            .Enabled = False
        }
        AddHandler btnRevoke.Click, AddressOf OnRevokeClick

        pnlActions.Controls.AddRange(New Control() {btnCreate, btnCopyLink, btnSaveSpres, btnShowQr, btnRefresh, btnRevoke})
        pnlMain.Controls.Add(pnlActions, 0, 3)

        ' Bottom close row
        Dim pnlBottom As New FlowLayoutPanel With {
            .AutoSize = True,
            .Anchor = AnchorStyles.Right,
            .FlowDirection = FlowDirection.LeftToRight,
            .Margin = New Padding(0)
        }
        btnClose = New Button With {
            .AutoSize = True,
            .Padding = New Padding(14, 5, 14, 5),
            .Text = Localization.T("Закрыть"),
            .DialogResult = DialogResult.OK
        }
        AddHandler btnClose.Click, Sub() Me.Close()
        pnlBottom.Controls.Add(btnClose)
        pnlMain.Controls.Add(pnlBottom, 0, 4)

        Me.Controls.Add(pnlMain)
        Me.CancelButton = btnClose
        DpiLayout.ApplyAutoScale(Me)

        AddHandler Me.Shown, AddressOf OnShownAsync
    End Sub

    Private Async Sub OnShownAsync(sender As Object, e As EventArgs)
        Await RefreshStatusAndListAsync()

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
        Try
            Dim statusResp As WorkerStatus = Await ShareController.GetStatusAsync()
            _status = statusResp
            If _status Is Nothing Then
                ' The worker did not answer (not running, or it refused): nothing on this page can work and
                ' the page does not know why - so it says the one true generic thing and keeps the detail in the log.
                AppFileLogger.WriteLine("Broadcasts: the worker's status is unavailable")
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
                Dim port As Integer = If(_status.BroadcastPort > 0, _status.BroadcastPort, _status.ListenPort)
                lblGatewayStatus.Text = Localization.TF("Шлюз трансляций включён (порт {0})", port)
                lblGatewayStatus.ForeColor = Color.DarkGreen
            Else
                lblGatewayStatus.Text = Localization.T("Шлюз трансляций выключен")
                lblGatewayStatus.ForeColor = Color.DarkSlateGray
            End If

            Dim listResp As WorkerResponse = Await BroadcastController.ListBroadcastsAsync()
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
        End Try
    End Function

    Private Sub PopulateListView()
        lvBroadcasts.BeginUpdate()
        lvBroadcasts.Items.Clear()
        For Each b As WorkerBroadcast In _broadcasts
            Dim item As New ListViewItem(b.Title)
            Dim isFolder As Boolean = String.Equals(b.Kind, "folder", StringComparison.OrdinalIgnoreCase)
            Dim typeStr As String = If(isFolder, Localization.TF("Папка ({0})", b.ItemCount), Localization.T("Файл"))
            item.SubItems.Add(typeStr)

            Dim expStr As String = If(String.IsNullOrEmpty(b.ExpiresAt), Localization.T("До отзыва"), b.ExpiresAt)
            item.SubItems.Add(expStr)
            item.SubItems.Add(b.Hits.ToString())

            Dim tokPrefix As String = If(b.Token.Length > 6, b.Token.Substring(0, 6) & "...", b.Token)
            item.SubItems.Add(tokPrefix)
            item.Tag = b
            lvBroadcasts.Items.Add(item)
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
        btnCopyLink.Enabled = hasSel
        btnSaveSpres.Enabled = hasSel
        btnShowQr.Enabled = hasSel
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
                    Await RefreshStatusAndListAsync()

                    ' Build .spres and config result
                    Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(createResp.Broadcast, _status, createDlg.IncludeExternal)
                    If config IsNot Nothing Then
                        ' Copy SPCH1 quick link to clipboard
                        Try
                            SecureClipboard.SetSecret(config.ClipboardLink)
                        Catch
                        End Try

                        MessageBox.Show(Me,
                            Localization.TF("Трансляция успешно создана!" & vbCrLf & vbCrLf &
                                           "Ссылка для быстрого воспроизведения в StreamsPlayer скопирована в буфер обмена:" & vbCrLf & "{0}", config.ClipboardLink),
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

        Dim settings As New ShareSettings()
        Try : settings.Load() : Catch : End Try
        Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(b, _status, Not settings.LanOnlyExport)
        If config IsNot Nothing AndAlso Not String.IsNullOrEmpty(config.ClipboardLink) Then
            Try
                SecureClipboard.SetSecret(config.ClipboardLink)
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

        Dim settings As New ShareSettings()
        Try : settings.Load() : Catch : End Try
        Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(b, _status, Not settings.LanOnlyExport)
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

        Dim settings As New ShareSettings()
        Try : settings.Load() : Catch : End Try
        Dim config As BroadcastConfigResult = BroadcastConfigBuilder.Build(b, _status, Not settings.LanOnlyExport)
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
            MessageBox.Show(Me, Localization.T("Не удалось обновить трансляцию."),
                            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    Private Async Sub OnRevokeClick(sender As Object, e As EventArgs)
        Dim b As WorkerBroadcast = SelectedBroadcast()
        If b Is Nothing Then Return

        Dim answer As DialogResult = MessageBox.Show(Me,
            Localization.TF("Отозвать трансляцию {0}? Ссылка перестанет работать немедленно.", b.Title),
            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If answer <> DialogResult.Yes Then Return

        Dim resp As WorkerResponse = Await BroadcastController.RevokeBroadcastAsync(b.Id)
        If resp IsNot Nothing AndAlso resp.Ok Then
            Await RefreshStatusAndListAsync()
            MessageBox.Show(Me, Localization.T("Трансляция отозвана."),
                            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show(Me, Localization.T("Не удалось отозвать трансляцию."),
                            Localization.T("Трансляции для StreamsPlayer"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

End Class
