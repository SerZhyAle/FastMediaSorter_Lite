Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Globalization
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The remote administration rows of the "Media Exchange Server" group (ticket 245, Rule 13, 14; DEVICE-EXCHANGE 1.4).
''' Contains the remote administration switch, password status and generation/manual entry dialog, allowed addresses,
''' session lifetime, honest text, and the "Manage in the browser" button with its validation.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _pnlXRemoteAdmin As FlowLayoutPanel
    Private _chkXRemoteAdminOn As CheckBox
    Private _lblXRemoteAdminPwStatus As Label
    Private _btnXRemoteAdminSetPw As Button
    Private _txtXRemoteAdminAllowed As TextBox
    Private _lblXRemoteAdminAllowedNote As Label
    Private _numXRemoteAdminLifetime As NumericUpDown
    Private _lblXRemoteAdminLifetimeNote As Label
    Private _btnXRemoteAdminApply As Button
    Private _btnXRemoteAdminBrowser As Button
    Private _lblXRemoteAdminBrowserNote As Label
    Private _exRemoteAdminLoading As Boolean

    Private Sub BuildExchangeRemoteAdminRows(sec As CollapsibleSection)
        _pnlXRemoteAdmin = XColumn()
        _pnlXRemoteAdmin.Controls.Add(XMarkedBold(Localization.T("Удалённое администрирование")))

        ' Honest text (Rule 14 and page-served-by-the-server 3)
        _pnlXRemoteAdmin.Controls.Add(AdminNote(Localization.T("Тот, у кого есть пароль администратора, управляет всеми учётными записями на этом сервере.")))
        _pnlXRemoteAdmin.Controls.Add(AdminNote(ExchangeAdminText.PageServedNotice()))

        ' Enable checkbox
        _chkXRemoteAdminOn = New CheckBox With {
            .AutoSize = True,
            .Margin = New Padding(0, 6, 0, 4),
            .Text = Localization.T("Включить удалённое администрирование")
        }
        AddHandler _chkXRemoteAdminOn.CheckedChanged, AddressOf OnXRemoteAdminCheckChanged
        _pnlXRemoteAdmin.Controls.Add(_chkXRemoteAdminOn)

        ' Password status and button
        _lblXRemoteAdminPwStatus = AdminNote("")
        _pnlXRemoteAdmin.Controls.Add(_lblXRemoteAdminPwStatus)

        _btnXRemoteAdminSetPw = XButton(Localization.T("Задать пароль администратора.."), AddressOf OnXRemoteAdminSetPassword, "action.edit")
        Dim rowPw As FlowLayoutPanel = AdminFlow()
        rowPw.Controls.Add(_btnXRemoteAdminSetPw)
        _pnlXRemoteAdmin.Controls.Add(rowPw)

        ' Allowed addresses
        Dim lblAllowed As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0), .Text = Localization.T("Разрешённые адреса (IP или подсети)")}
        _txtXRemoteAdminAllowed = New TextBox With {.Width = 260, .Margin = New Padding(0, 2, 0, 0)}
        Dim allowedSetting = DirectCast(SettingsEditorRow(lblAllowed, _txtXRemoteAdminAllowed, New Padding(0, 4, 0, 0)), ManagerSettingRow)
        allowedSetting.SetHint(Localization.T("Необязательно: список IP-адресов или подсетей через запятую. Если список пуст, доступ открыт с любого адреса."))
        _lblXRemoteAdminAllowedNote = allowedSetting.Hint
        _pnlXRemoteAdmin.Controls.Add(allowedSetting)

        ' Session lifetime
        Dim lblLifetime As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0), .Text = Localization.T("Время сеанса, минут")}
        _numXRemoteAdminLifetime = New NumericUpDown With {
            .Minimum = 1,
            .Maximum = 1440,
            .Value = 15,
            .Width = 84,
            .Margin = New Padding(0, 2, 0, 0)
        }
        Dim lifetimeSetting = DirectCast(SettingsEditorRow(lblLifetime, _numXRemoteAdminLifetime, New Padding(0, 4, 0, 0)), ManagerSettingRow)
        lifetimeSetting.SetHint(Localization.T("Время бездействия, после которого сеанс администрирования завершается (по умолчанию 15 минут)."))
        _lblXRemoteAdminLifetimeNote = lifetimeSetting.Hint
        _pnlXRemoteAdmin.Controls.Add(lifetimeSetting)

        ' Apply button
        _btnXRemoteAdminApply = XButton(Localization.T("Применить настройки удалённого администрирования"), AddressOf OnXRemoteAdminApply, "action.confirm")
        Dim rowApply As FlowLayoutPanel = AdminFlow()
        rowApply.Controls.Add(_btnXRemoteAdminApply)
        _pnlXRemoteAdmin.Controls.Add(rowApply)

        ' Manage in the browser button (Rule 12 & Rule 13)
        _btnXRemoteAdminBrowser = XButton(Localization.T("Управлять в браузере"), AddressOf OnXRemoteAdminOpenBrowser, "action.browse")
        Dim rowBrowser As FlowLayoutPanel = AdminFlow()
        rowBrowser.Controls.Add(_btnXRemoteAdminBrowser)
        _pnlXRemoteAdmin.Controls.Add(rowBrowser)

        _lblXRemoteAdminBrowserNote = AdminNote("")
        _pnlXRemoteAdmin.Controls.Add(_lblXRemoteAdminBrowserNote)

        sec.AddBodyRow(_pnlXRemoteAdmin)
    End Sub

    Private Sub ApplyExchangeRemoteAdminState(report As ExchangeProcess.InspectReport)
        If _pnlXRemoteAdmin Is Nothing OrElse report Is Nothing OrElse Not report.Answered Then Return
        _exRemoteAdminLoading = True
        Try
            _chkXRemoteAdminOn.Checked = report.AdminOn
            If report.AdminPasswordSet Then
                _lblXRemoteAdminPwStatus.Text = Localization.T("Пароль администратора задан.")
            Else
                _lblXRemoteAdminPwStatus.Text = Localization.T("Пароль администратора не задан.")
            End If
            _txtXRemoteAdminAllowed.Text = If(report.AdminAllowedAddresses IsNot Nothing, String.Join(", ", report.AdminAllowedAddresses), "")
            _numXRemoteAdminLifetime.Value = If(report.AdminSessionLifetimeMinutes >= 1 AndAlso report.AdminSessionLifetimeMinutes <= 1440, report.AdminSessionLifetimeMinutes, 15)

            ' Rule 12: Manage in the browser button is enabled only while web administration is on AND web directory is configured
            If Not report.AdminOn Then
                _btnXRemoteAdminBrowser.Enabled = False
                _lblXRemoteAdminBrowserNote.Text = Localization.T("Веб-администрирование выключено.")
                _lblXRemoteAdminBrowserNote.Visible = True
            ElseIf String.IsNullOrEmpty(report.WebDir) Then
                _btnXRemoteAdminBrowser.Enabled = False
                _lblXRemoteAdminBrowserNote.Text = Localization.T("Сервер не отдаёт веб-клиент (папка веб-клиента не настроена).")
                _lblXRemoteAdminBrowserNote.Visible = True
            Else
                _btnXRemoteAdminBrowser.Enabled = True
                _lblXRemoteAdminBrowserNote.Text = ""
                _lblXRemoteAdminBrowserNote.Visible = False
            End If
        Finally
            _exRemoteAdminLoading = False
        End Try
    End Sub

    Private Async Sub OnXRemoteAdminCheckChanged(sender As Object, e As EventArgs)
        If _exRemoteAdminLoading OrElse _exBusy Then Return
        If Not _chkXRemoteAdminOn.Checked Then
            SetXBusy(True)
            Try
                Dim outcome = Await Task.Run(Function() ExchangeProcess.AdminOff())
                If outcome = ExchangeProcess.ConfigOutcome.Saved Then
                    ShowXResult(Localization.T("Настройки сохранены. Применяются при следующем запуске сервера."), ok:=True)
                Else
                    ShowXResult(Localization.T("Серверу не удалось это сделать. Подробности - в журнале сервера."), ok:=False)
                End If
                Await RefreshExchangeAsync()
            Finally
                SetXBusy(False)
            End Try
            Return
        End If

        ' Turning ON: If password is not set yet, offer generated or manual password per Rule 5
        Dim generate As Boolean = False
        Dim enteredPassword As String = Nothing

        If _exReport IsNot Nothing AndAlso Not _exReport.AdminPasswordSet Then
            Dim choice As DialogResult = ManagerBox.Show(Me,
                Localization.T("Сгенерировать безопасный пароль (20 символов)"),
                Localization.T("Пароль администратора"),
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question)

            If choice = DialogResult.Cancel Then
                _exRemoteAdminLoading = True
                _chkXRemoteAdminOn.Checked = False
                _exRemoteAdminLoading = False
                Return
            ElseIf choice = DialogResult.Yes Then
                generate = True
            Else
                Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)
                    If dlg.ShowDialog(Me) <> DialogResult.OK OrElse String.IsNullOrEmpty(dlg.EnteredPassword) Then
                        _exRemoteAdminLoading = True
                        _chkXRemoteAdminOn.Checked = False
                        _exRemoteAdminLoading = False
                        Return
                    End If
                    enteredPassword = dlg.EnteredPassword
                End Using
            End If
        End If

        SetXBusy(True)
        Try
            Dim allowed As String = _txtXRemoteAdminAllowed.Text.Trim()
            Dim lifetime As Integer = CInt(_numXRemoteAdminLifetime.Value)
            Dim res As ExchangeProcess.AdminActionResult = Await Task.Run(Function() ExchangeProcess.AdminOn(generate, enteredPassword, allowed, lifetime))
            If res.Outcome = ExchangeProcess.ConfigOutcome.Saved Then
                If generate AndAlso Not String.IsNullOrEmpty(res.GeneratedPassword) Then
                    Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, res.GeneratedPassword)
                        dlg.ShowDialog(Me)
                    End Using
                End If
                ShowXResult(Localization.T("Настройки сохранены. Применяются при следующем запуске сервера."), ok:=True)
            Else
                ShowXResult(Localization.T("Серверу не удалось это сделать. Подробности - в журнале сервера."), ok:=False)
            End If
            Await RefreshExchangeAsync()
        Finally
            SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXRemoteAdminSetPassword()
        If _exBusy Then Return
        Dim choice As DialogResult = ManagerBox.Show(Me,
            Localization.T("Сгенерировать безопасный пароль (20 символов)"),
            Localization.T("Пароль администратора"),
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question)

        If choice = DialogResult.Cancel Then Return

        Dim generate As Boolean = (choice = DialogResult.Yes)
        Dim enteredPassword As String = Nothing

        If Not generate Then
            Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)
                If dlg.ShowDialog(Me) <> DialogResult.OK OrElse String.IsNullOrEmpty(dlg.EnteredPassword) Then
                    Return
                End If
                enteredPassword = dlg.EnteredPassword
            End Using
        End If

        SetXBusy(True)
        Try
            Dim res As ExchangeProcess.AdminActionResult = Await Task.Run(Function() ExchangeProcess.AdminPasswd(generate, enteredPassword))
            If res.Outcome = ExchangeProcess.ConfigOutcome.Saved Then
                If generate AndAlso Not String.IsNullOrEmpty(res.GeneratedPassword) Then
                    Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, res.GeneratedPassword)
                        dlg.ShowDialog(Me)
                    End Using
                End If
                ShowXResult(Localization.T("Настройки сохранены. Применяются при следующем запуске сервера."), ok:=True)
            Else
                ShowXResult(Localization.T("Серверу не удалось это сделать. Подробности - в журнале сервера."), ok:=False)
            End If
            Await RefreshExchangeAsync()
        Finally
            SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXRemoteAdminApply()
        If _exBusy Then Return
        SetXBusy(True)
        Try
            Dim allowed As String = _txtXRemoteAdminAllowed.Text.Trim()
            Dim lifetime As Integer = CInt(_numXRemoteAdminLifetime.Value)
            Dim outcome As ExchangeProcess.ConfigOutcome = Await Task.Run(Function() ExchangeProcess.AdminSet(allowed, lifetime))
            If outcome = ExchangeProcess.ConfigOutcome.Saved Then
                ShowXResult(Localization.T("Настройки сохранены. Применяются при следующем запуске сервера."), ok:=True)
            Else
                ShowXResult(Localization.T("Серверу не удалось это сделать. Подробности - в журнале сервера."), ok:=False)
            End If
            Await RefreshExchangeAsync()
        Finally
            SetXBusy(False)
        End Try
    End Sub

    Private Sub OnXRemoteAdminOpenBrowser()
        If _exReport Is Nothing Then Return
        Dim url As String = ExchangeProcess.BrowserAdminUrl(_exReport)
        Dim fpHex As String = ExchangeProcess.FormatFingerprintHex(_exReport.Fingerprint)
        Using dlg As New Exchange_Admin_Browser_Form(url, fpHex)
            dlg.ShowDialog(Me)
        End Using
    End Sub

End Class
