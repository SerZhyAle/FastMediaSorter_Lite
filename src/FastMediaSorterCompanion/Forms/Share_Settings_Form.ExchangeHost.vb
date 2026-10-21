Option Strict On

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Private _chkXAwake As CheckBox
    Private _numXLogDays As NumericUpDown
    Private _numXMaxAccounts As NumericUpDown
    Private _btnXApplyHost As Button
    Private _btnXRenewCert As Button
    Private _lblXAwakeState As Label
    Private _lblXRetention As Label
    Private _loadingXHost As Boolean
    Private _baselineXAwake As Boolean
    Private _baselineXDays As Integer = 31
    Private _baselineXMaxAccounts As Integer = 10

    Private Sub BuildXHostSettings()
        _chkXAwake = New CheckBox With {.AutoSize = True, .MaximumSize = New Size(ExchangeAdminWidth, 0), .Text = Localization.T("Не давать ПК уснуть, пока сервер работает")}
        _numXLogDays = New NumericUpDown With {.Minimum = 1, .Maximum = 36500, .Value = 31, .Width = 100,
            .AccessibleName = Localization.T("Хранить журналы, дней")}
        _numXMaxAccounts = New NumericUpDown With {.Minimum = 1, .Maximum = 1000, .Value = 10, .Width = 100,
            .AccessibleName = Localization.T("Максимум аккаунтов")}
        _pnlXConfig.Controls.Add(_chkXAwake)
        _pnlXConfig.Controls.Add(AdminNote(Localization.T("Экран может выключаться. Настройки питания не меняются. Изменения применяются при следующем запуске сервера.")))
        _lblXAwakeState = AdminNote("")
        _pnlXConfig.Controls.Add(_lblXAwakeState)
        _pnlXConfig.Controls.Add(SettingsEditorRow(XBold(Localization.T("Хранить журналы, дней")), _numXLogDays, New Padding(0, 2, 0, 2)))
        _lblXRetention = AdminNote("")
        _pnlXConfig.Controls.Add(_lblXRetention)
        _pnlXConfig.Controls.Add(XButton(Localization.T("Открыть журнал сервера в редакторе"),
            Sub() OpenServerManagementLog(Me, False), "action.edit"))
        _pnlXConfig.Controls.Add(XButton(Localization.T("Папка журналов сервера.."),
            Sub() OpenServerManagementLog(Me, True), "content.folder"))
        _pnlXConfig.Controls.Add(XButton(ExchangeAdminText.AuditLogButton(),
            Sub()
                Try
                    ManagementLogAccess.OpenServerAuditLog(ExchangeProcess.DataDir())
                Catch ex As Exception
                    ShowManagementLogError(Me, ex)
                End Try
            End Sub, "action.view"))
        _pnlXConfig.Controls.Add(SettingsEditorRow(XBold(Localization.T("Максимум аккаунтов")), _numXMaxAccounts, New Padding(0, 2, 0, 2)))
        _pnlXConfig.Controls.Add(AdminNote(Localization.T("Лимит регистраций на сервере. Добавление аккаунтов оператором не ограничено.")))
        _btnXApplyHost = XButton(Localization.T("Применить настройки сервера"), AddressOf OnXApplyHost, "action.confirm")
        _btnXApplyHost.Visible = False
        _pnlXConfig.Controls.Add(_btnXApplyHost)
        _pnlXConfig.Controls.Add(AdminNote(ExchangeAdminText.CertificateRenewalNote()))
        AddHandler _chkXAwake.CheckedChanged, Sub() UpdateXHostPending()
        AddHandler _numXLogDays.ValueChanged, Sub() UpdateXHostPending()
        AddHandler _numXMaxAccounts.ValueChanged, Sub() UpdateXHostPending()
    End Sub

    Private Function XHostPending() As Boolean
        Return _chkXAwake.Checked <> _baselineXAwake OrElse CInt(_numXLogDays.Value) <> _baselineXDays OrElse CInt(_numXMaxAccounts.Value) <> _baselineXMaxAccounts
    End Function

    Private Sub UpdateXHostPending()
        If Not _loadingXHost Then _btnXApplyHost.Visible = XHostPending()
    End Sub

    Private Sub ApplyXHostSettings(snap As ExchangeSnapshot)
        If snap.Report Is Nothing OrElse Not snap.Report.Answered Then Return
        If Not XHostPending() Then
            _loadingXHost = True
            Try
                _baselineXAwake = snap.Report.KeepAwake
                _baselineXDays = Math.Max(1, Math.Min(36500, snap.Report.LogRetentionDays))
                _baselineXMaxAccounts = Math.Max(1, Math.Min(1000, snap.Report.MaxAccounts))
                _chkXAwake.Checked = _baselineXAwake
                _numXLogDays.Value = _baselineXDays
                _numXMaxAccounts.Value = _baselineXMaxAccounts
            Finally
                _loadingXHost = False
            End Try
        End If
        If _txtXStatement IsNot Nothing Then _txtXStatement.Text = String.Join(vbCrLf & vbCrLf, ExchangeStatement.Paragraphs(snap.Report.LogRetentionDays))
        _lblXRetention.Text = Localization.TF("Журналы хранятся {0} дней. Файлы создаются по дням UTC и очищаются ежедневно.", snap.Report.LogRetentionDays)
        UpdateXHostPending()
    End Sub

    Private Async Sub OnXApplyHost()
        Await ApplyExchangeHostAsync()
    End Sub

    Private Async Function ApplyExchangeHostAsync() As Task
        If _exBusy Then Return
        Dim awake As Boolean = _chkXAwake.Checked
        Dim days As Integer = CInt(_numXLogDays.Value)
        Dim maxAccounts As Integer = CInt(_numXMaxAccounts.Value)
        SetXBusy(True)
        Try
            Dim outcome As ExchangeProcess.ConfigOutcome
            If ExchangeAdmin.ServiceDataDir().Length > 0 OrElse ExchangeProcess.RunningPid() > 0 Then
                Dim request As New ExchangeAdminRequest("host.settings") With {.KeepAwake = awake, .LogRetentionDays = days}
                Dim reply As ExchangeAdminReply = Await ExchangeAdmin.SendAsync(request, Threading.CancellationToken.None)
                outcome = If(reply.Ok, ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.ConfigOutcome.Failed)
                If reply.Ok Then
                    _baselineXAwake = awake
                    _baselineXDays = days
                End If
            Else
                outcome = Await Task.Run(Function() ExchangeProcess.SetKeepAwake(awake))
                If outcome = ExchangeProcess.ConfigOutcome.Saved Then
                    _baselineXAwake = awake
                    outcome = Await Task.Run(Function() ExchangeProcess.SetLogRetention(days))
                    If outcome = ExchangeProcess.ConfigOutcome.Saved Then _baselineXDays = days
                End If
            End If
            ' the cap on accounts is a third setting of the same button: when it is the one that fails the result says
            ' so (a "saved" next to a cap that was not written would be a false answer)
            If outcome = ExchangeProcess.ConfigOutcome.Saved AndAlso maxAccounts <> _baselineXMaxAccounts Then
                Dim accOutcome As ExchangeProcess.ConfigOutcome = Await Task.Run(Function() ExchangeProcess.SetMaxAccounts(maxAccounts))
                If accOutcome = ExchangeProcess.ConfigOutcome.Saved Then
                    _baselineXMaxAccounts = maxAccounts
                Else
                    outcome = accOutcome
                End If
            End If
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(ExchangeAdminText.ConfigResult(outcome, Localization.T("Настройки сохранены. Применяются при следующем запуске сервера.")), outcome = ExchangeProcess.ConfigOutcome.Saved)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: host settings", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Function

    Private Async Sub OnXRenewCertificate()
        If _exBusy Then Return
        Await RunXCertificate(Function() ExchangeProcess.RenewCertificate())
    End Sub
End Class
