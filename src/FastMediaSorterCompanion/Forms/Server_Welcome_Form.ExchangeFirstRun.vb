Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Friend NotInheritable Class Server_Welcome_Form
    Private ReadOnly _exchangeActions As IExchangeFirstRunActions
    Private _exchangeSnapshot As ExchangeSnapshot
    Private _exchangeResult As ExchangeWizardResult
    Private _exchangeBusy As Boolean
    Private _exchangeOutcome As ExchangeWizardOutcome = ExchangeWizardOutcome.Cancelled
    Private _listen As NumericUpDown
    Private _rangeLow As NumericUpDown
    Private _rangeHigh As NumericUpDown
    Private _firewall As CheckBox
    Private _service As CheckBox
    Private _serviceChosen As Boolean
    Private _serviceAttempted As Boolean
    Private _endpoint As TextBox
    Private _endpointHint As Label
    Private _endpointSuggestion As Button
    Private _login As TextBox
    Private _generated As RadioButton
    Private _typed As RadioButton
    Private _enroll As CheckBox
    Private _accountRepair As Button
    Private _accountState As Label
    Private _certificate As TextBox
    Private _facts As Label
    Private _connectionString As Button
    Private _exchangeProblem As Label
    Private _listenBaseline As Integer
    Private _lowBaseline As Integer
    Private _highBaseline As Integer
    Private _endpointBaseline As String
    Private _firewallResult As String = ""
    Private _serviceResult As String = ""

    Private Sub ExplainExchange(panel As FlowLayoutPanel, what As String, why As String, how As String, where As String)
        AddExplanation(panel, what, why, how)
        AddText(panel, Localization.T("Где это менять"), New Padding(0, 10, 0, 0), True)
        AddText(panel, where, New Padding(0, 2, 0, 0))
    End Sub

    Private Function ExchangeNumber(value As Integer) As NumericUpDown
        Dim font As New Font(Me.Font.FontFamily, 26.0F, FontStyle.Regular)
        _fonts.Add(font)
        Return New NumericUpDown With {.Minimum = ExchangeSettings.MinPort, .Maximum = ExchangeSettings.MaxPort,
            .Value = Math.Max(ExchangeSettings.MinPort, Math.Min(ExchangeSettings.MaxPort, value)), .Width = 200, .Font = font}
    End Function

    Private Function ExchangeQuestion(panel As FlowLayoutPanel, text As String) As CheckBox
        Dim check As New CheckBox With {.Text = text, .AutoSize = True, .MinimumSize = New Size(0, TargetHeight),
            .MaximumSize = New Size(ContentWidth, 0), .Checked = False, .Margin = New Padding(0, 8, 0, 0)}
        panel.Controls.Add(check)
        Return check
    End Function

    Private Function ExchangeButton(panel As FlowLayoutPanel, text As String, glyph As String, action As Action) As Button
        Dim button As New Button With {.Text = text}
        StyleButton(button)
        ButtonGlyphs.Apply(button, glyph)
        AddHandler button.Click, Sub() action()
        panel.Controls.Add(button)
        Return button
    End Function

    Private Sub BuildDoors(panel As FlowLayoutPanel)
        ExplainExchange(panel, Localization.T("Порт встречи устройств и диапазон портов для сторонних клиентов."),
            Localization.T("Устройства должны знать порт сервера; сторонним подключениям нужен отдельный диапазон."),
            ExchangeText.PortNote() & Environment.NewLine & ExchangeAdminText.RangeNote() & Environment.NewLine & ExchangeText.FirewallNote(),
            Localization.T("Группа сервера обмена: порт, диапазон сторонних портов и брандмауэр."))
        Dim report As ExchangeProcess.InspectReport = _exchangeSnapshot.Report
        _listenBaseline = If(report IsNot Nothing AndAlso report.Port > 0, report.Port, ExchangeSettings.DefaultPort)
        _lowBaseline = 40000
        _highBaseline = 40100
        Dim parsedLow As Integer
        Dim parsedHigh As Integer
        If report IsNot Nothing AndAlso ExchangePortRangeRules.TryParse(report.PortRange, parsedLow, parsedHigh) Then
            _lowBaseline = parsedLow
            _highBaseline = parsedHigh
        End If
        AddText(panel, Localization.T("Порт"), New Padding(0, 8, 0, 0))
        _listen = ExchangeNumber(_listenBaseline)
        panel.Controls.Add(_listen)
        AddText(panel, ExchangeAdminText.RangeFrom(), New Padding(0, 8, 0, 0))
        _rangeLow = ExchangeNumber(_lowBaseline)
        panel.Controls.Add(_rangeLow)
        AddText(panel, ExchangeAdminText.RangeTo(), New Padding(0, 8, 0, 0))
        _rangeHigh = ExchangeNumber(_highBaseline)
        panel.Controls.Add(_rangeHigh)
        _firewall = ExchangeQuestion(panel, ExchangeText.FirewallEnableButton())
        _firewall.Enabled = _exchangeSnapshot.CanChangeFirewall AndAlso _exchangeSnapshot.Firewall <> ExchangeFirewall.RuleState.ThisProgram
        AddText(panel, ExchangeText.FirewallState(_exchangeSnapshot.Firewall), New Padding(0, 2, 0, 0))
        If _exchangeSnapshot.ServiceExists Then
            AddText(panel, ExchangeText.ServiceWinsNote(), New Padding(0, 8, 0, 0))
        End If
    End Sub

    Private Sub BuildService(panel As FlowLayoutPanel)
        ExplainExchange(panel, Localization.T("Служба Windows держит сервер доступным, когда никто не вошёл в систему."),
            Localization.T("Служба нужна постоянно работающему серверу; обычный запуск её не требует."),
            ExchangeText.ServiceNote(False) & Environment.NewLine & Localization.T("Галочка запоминает согласие. После создания первого аккаунта существующий помощник перенесёт настройки и зарегистрирует службу с одним запросом прав. До этого ничего не устанавливается."),
            Localization.T("Группа сервера обмена: регистрация, запуск, остановка и удаление службы."))
        _service = ExchangeQuestion(panel, Localization.T("Зарегистрировать как службу.."))
    End Sub

    Private Sub BuildAddress(panel As FlowLayoutPanel)
        ExplainExchange(panel, ExchangeAdminText.EndpointLabel(),
            Localization.T("Пустой адрес оставляет локальный первый запуск допустимым, но скрывает трансляции и доступ к папкам для других устройств."),
            ExchangeAdminText.EndpointNote(),
            Localization.T("Группа сервера обмена: адрес для других устройств. Подсказка заполняет поле только по нажатию."))
        _endpointBaseline = If(_exchangeSnapshot.Report?.PublicEndpoint, "")
        _endpoint = New TextBox With {.Text = _endpointBaseline, .Width = ContentWidth, .AccessibleName = ExchangeAdminText.EndpointLabel()}
        panel.Controls.Add(_endpoint)
        _endpointHint = AddText(panel, "", New Padding(0, 4, 0, 0))
        _endpointSuggestion = ExchangeButton(panel, Localization.T("Предложить локальный адрес"), "action.adjust", Sub()
            _endpoint.Text = _exchangeActions.Suggest(CInt(_listen.Value))
        End Sub)
        AddHandler _endpoint.TextChanged, Sub() UpdateEndpointHint()
        UpdateEndpointHint()
    End Sub

    Private Sub UpdateEndpointHint()
        Dim verdict As ExchangeOperatorEndpoint.Verdict = ExchangeOperatorEndpoint.Check(_endpoint.Text)
        _endpointHint.Text = If(verdict = ExchangeOperatorEndpoint.Verdict.Empty, ExchangeConnectionStringText.NoEndpoint(),
            ExchangeAdminText.EndpointVerdict(verdict))
        _endpointSuggestion.Visible = _endpoint.TextLength = 0
    End Sub

    Private Sub BuildFirstRunAccount(panel As FlowLayoutPanel)
        ExplainExchange(panel, Localization.T("Учётная запись, с которой войдёт первое устройство."),
            Localization.T("Без неё сервер ничего не принимает, а пароль показывается один раз."),
            Localization.T("Далее создаст аккаунт, запустит сервер и проверит сертификат. Пароль будет показан один раз в существующем окне и не сохранится на этом компьютере. Подключение этого компьютера выбирается галочкой."),
            Localization.T("Группа сервера обмена: учётные записи и устройства. Неудача после создания аккаунта не удаляет его."))
        _login = New TextBox With {.Text = ExchangeLoginRules.SuggestFrom(Environment.UserName), .Width = ContentWidth,
            .AccessibleName = ExchangeAdminText.WizardLoginLabel()}
        panel.Controls.Add(_login)
        AddText(panel, ExchangeAdminText.LoginHint(), New Padding(0, 2, 0, 0))
        _generated = New RadioButton With {.AutoSize = True, .MinimumSize = New Size(0, TargetHeight),
            .Checked = True, .Text = Localization.T("Создать пароль автоматически")}
        _typed = New RadioButton With {.AutoSize = True, .MinimumSize = New Size(0, TargetHeight),
            .Text = Localization.T("Ввести свой пароль")}
        panel.Controls.Add(_generated)
        panel.Controls.Add(_typed)
        _enroll = ExchangeQuestion(panel, Localization.T("Подключить этот компьютер к аккаунту"))
        _enroll.Checked = ExchangeFeatures.IsEnabled()
        _enroll.Enabled = ExchangeFeatures.IsEnabled()
        _accountState = AddText(panel, "", New Padding(0, 8, 0, 0))
        _accountRepair = ExchangeButton(panel, Localization.T("Настройки.."), "app.settings", Sub()
            If _exchangeBusy Then Return
            DialogResult = DialogResult.OK
            Close()
        End Sub)
        _accountRepair.Visible = False
    End Sub

    Private Sub BuildCertificate(panel As FlowLayoutPanel)
        ExplainExchange(panel, ExchangeText.FingerprintLabel(),
            Localization.T("Устройство доверяет серверу только после того, как проверит отпечаток его сертификата при первом подключении."),
            ExchangeText.FingerprintNote(), Localization.T("Группа сервера обмена: сертификат и его отпечаток."))
        _certificate = New TextBox With {.ReadOnly = True, .Width = ContentWidth, .Text = If(_exchangeSnapshot.Report?.Fingerprint, ""),
            .AccessibleName = ExchangeText.FingerprintLabel()}
        panel.Controls.Add(_certificate)
        ExchangeButton(panel, ExchangeText.CopyFingerprintButton(), "action.copy", Sub()
            If _certificate.TextLength = 0 Then Return
            Dim data As New DataObject()
            data.SetText(_certificate.Text, TextDataFormat.UnicodeText)
            If SecureClipboard.TrySetDataObject(data) Then _exchangeProblem.Text = ExchangeText.FingerprintCopied()
        End Sub)
    End Sub

    Private Sub BuildFirstRunDone(panel As FlowLayoutPanel)
        ExplainExchange(panel, Localization.TF("Адрес для этого компьютера и для других устройств, логин и отпечаток сертификата записаны в группе «{0}».", ProductNames.ExchangeServer),
            Localization.T("Пароль больше нигде не показывается. Устройство подключается, пройдя проверку отпечатка при первом подключении."),
            Localization.T("Готово откроет группу сервера обмена в локальном менеджере. Там находятся все дальнейшие действия."),
            Localization.T("Учётные записи и устройства; адрес для других устройств; диапазон сторонних портов; сертификат; запуск и остановка; служба Windows - в группе сервера обмена."))
        _facts = AddText(panel, "", New Padding(0, 10, 0, 0))
        _exchangeProblem = AddText(panel, "", New Padding(0, 8, 0, 0))
        _connectionString = ExchangeButton(panel, ExchangeConnectionStringText.GetButton(), "action.copy", Sub() _exchangeActions.ConnectionString(Me))
        ExchangeButton(panel, Localization.T("Подключить телефон (справка)"), "app.help",
            Sub() NetworkInfo.OpenInBrowser(ShareLinks.ExchangePortalUrl("exchange-server")))
        ExchangeButton(panel, Localization.T("Опубликовать первую папку (справка)"), "content.folder",
            Sub() NetworkInfo.OpenInBrowser(ShareLinks.ExchangePortalUrl("windows-servers")))
    End Sub

    Private Sub RefreshExchangeStep(kind As WelcomeStepKind)
        If kind = WelcomeStepKind.ExchangeAccount Then
            _next.Enabled = Not _exchangeBusy AndAlso (Not If(_exchangeResult?.AccountCreated, False) OrElse ExchangeFirstRunModel.Completed(_exchangeOutcome))
            _next.Text = If(ExchangeFirstRunModel.Completed(_exchangeOutcome), Localization.T("Далее"), ExchangeAdminText.WizardStart())
            Dim glyph As String = If(ExchangeFirstRunModel.Completed(_exchangeOutcome), "nav.forward", "action.confirm")
            If ButtonGlyphs.GlyphOf(_next) <> glyph Then ButtonGlyphs.Apply(_next, glyph, ButtonGlyphRole.Affirmative)
        Else
            _next.Enabled = Not _exchangeBusy
        End If
        _back.Enabled = Not _exchangeBusy AndAlso _position > 0
        _later.Enabled = Not _exchangeBusy
        If kind = WelcomeStepKind.ExchangeCertificate Then _certificate.Text = If(_exchangeSnapshot.Report?.Fingerprint, "")
        If kind = WelcomeStepKind.ExchangeDone Then RenderExchangeFacts()
    End Sub

    Private Sub RenderExchangeFacts()
        Dim report As ExchangeProcess.InspectReport = If(_exchangeSnapshot.Report, New ExchangeProcess.InspectReport())
        Dim running As Boolean = _exchangeSnapshot.ConsolePid > 0 OrElse _exchangeSnapshot.ServiceState = ServiceControl.ServiceState.Running
        Dim state As String = ExchangeText.StateLine(ExchangeGroupModel.Build(_exchangeSnapshot))
        _facts.Text = String.Join(Environment.NewLine, {
            state,
            Localization.TF("{0}: {1}", ExchangeAdminText.FactThisPc(), ExchangeServerWizard.LocalAddressOf(report.Listen)),
            Localization.TF("{0}: {1}", ExchangeAdminText.FactOthers(), If(report.PublicEndpoint = "", ExchangeAdminText.FactOthersUnset(), report.PublicEndpoint)),
            Localization.TF("{0}: {1}", ExchangeAdminText.FactLogin(), If(_exchangeResult?.Login, Localization.T("Учётные записи"))),
            Localization.TF("{0}: {1}", ExchangeText.FingerprintLabel(), If(report.Fingerprint = "", ExchangeText.FingerprintMissing(), report.Fingerprint))})
        _facts.ForeColor = ThemePalette.ReadableText(If(running, Color.ForestGreen, CollapsibleSection.AttentionColor), ThemeService.Palette.SurfaceWindow)
        _connectionString.Enabled = report.PublicEndpoint <> "" AndAlso report.Fingerprint <> ""
        _exchangeProblem.Text = String.Join(Environment.NewLine, {
            If(_connectionString.Enabled, "", ExchangeConnectionStringText.NoEndpoint()),
            If(_firewallResult <> "", _firewallResult, ExchangeText.FirewallState(_exchangeSnapshot.Firewall)),
            If(_serviceResult <> "", _serviceResult, ExchangeText.ServiceNote(_exchangeSnapshot.ServiceExists))})
    End Sub

    Private Async Function AdvanceExchangeAsync() As Task
        If _exchangeBusy Then Return
        Dim kind As WelcomeStepKind = _steps(_position)
        _exchangeBusy = True
        RefreshExchangeStep(kind)
        Try
            Select Case kind
                Case WelcomeStepKind.ExchangeDoors
                    If Not Await SaveDoorsAsync() Then Return
                Case WelcomeStepKind.ExchangeService
                    If _service.Checked AndAlso Not _serviceChosen Then _serviceAttempted = False
                    _serviceChosen = _service.Checked
                Case WelcomeStepKind.ExchangeAddress
                    Dim verdict As ExchangeOperatorEndpoint.Verdict = ExchangeOperatorEndpoint.Check(_endpoint.Text)
                    If verdict <> ExchangeOperatorEndpoint.Verdict.Accepted AndAlso verdict <> ExchangeOperatorEndpoint.Verdict.Empty Then
                        _endpoint.Focus()
                        Return
                    End If
                    Dim text As String = _endpoint.Text.Trim()
                    If text <> _endpointBaseline Then
                        Dim outcome = Await _exchangeActions.SetEndpointAsync(text)
                        If outcome <> ExchangeProcess.ConfigOutcome.Saved Then
                            ShowExchangeFailure(ExchangeAdminText.ConfigResult(outcome, ""))
                            Return
                        End If
                        _endpointBaseline = text
                    End If
                Case WelcomeStepKind.ExchangeAccount
                    If Not ExchangeFirstRunModel.Completed(_exchangeOutcome) AndAlso Not If(_exchangeResult?.AccountCreated, False) Then
                        If ExchangeLoginRules.Check(_login.Text) <> ExchangeLoginRules.Verdict.Accepted Then
                            _accountState.Text = ExchangeAdminText.LoginHint()
                            _login.Focus()
                            Return
                        End If
                        _exchangeResult = _exchangeActions.Account(Me, _login.Text, _typed.Checked, _enroll.Checked)
                        _exchangeResult.Wipe()
                        _accountRepair.Visible = _exchangeResult.AccountCreated AndAlso Not ExchangeFirstRunModel.Completed(_exchangeResult.Outcome)
                        _exchangeOutcome = _exchangeResult.Outcome
                        _accountState.Text = ExchangeAdminText.WizardOutcome(_exchangeResult, _enroll.Checked)
                    End If
                    If Not ExchangeFirstRunModel.CanAdvance(kind, _exchangeOutcome) Then Return
                    If _serviceChosen AndAlso Not _serviceAttempted Then
                        _serviceAttempted = True
                        Dim installed = Await _exchangeActions.ServiceAsync()
                        _serviceResult = ExchangeText.ServiceResult(installed)
                        If installed <> ServiceControl.ManageResult.Succeeded Then
                            _serviceChosen = False
                            If _service IsNot Nothing Then _service.Checked = False
                        End If
                    End If
                Case WelcomeStepKind.ExchangeDone
                    DialogResult = DialogResult.OK
                    _exchangeBusy = False
                    Close()
                    Return
            End Select
            _exchangeSnapshot = Await _exchangeActions.ReadAsync()
            If IsDisposed Then Return
            If kind = WelcomeStepKind.ExchangeAccount AndAlso
               (_exchangeSnapshot.ConsolePid = 0 AndAlso _exchangeSnapshot.ServiceState <> ServiceControl.ServiceState.Running) Then
                _accountState.Text = ExchangeText.StateLine(ExchangeGroupModel.Build(_exchangeSnapshot))
                _accountRepair.Visible = True
                Return
            End If
            ShowStep(_position + 1)
        Catch
            ShowExchangeFailure(Localization.T("Не удалось выполнить шаг. Уже подтверждённое сохранено; проверьте группу сервера обмена."))
        Finally
            _exchangeBusy = False
            If Not IsDisposed Then RefreshStep(_steps(_position))
        End Try
    End Function

    Private Async Function SaveDoorsAsync() As Task(Of Boolean)
        Dim port As Integer = CInt(_listen.Value)
        Dim low As Integer = CInt(_rangeLow.Value)
        Dim high As Integer = CInt(_rangeHigh.Value)
        Dim verdict = ExchangePortRangeRules.Check(low, high, port)
        If verdict <> ExchangePortRangeRules.Verdict.Accepted Then
            ShowExchangeFailure(ExchangeAdminText.RangeVerdict(verdict))
            Return False
        End If
        If port <> _listenBaseline Then
            Dim outcome = Await _exchangeActions.SetListenAsync(ExchangeProcess.WithPort(If(_exchangeSnapshot.Report?.Listen, ""), port))
            If outcome <> ExchangeProcess.ListenOutcome.Saved Then
                ShowExchangeFailure(ExchangeText.PortResult(outcome, False))
                Return False
            End If
            _listenBaseline = port
        End If
        If low <> _lowBaseline OrElse high <> _highBaseline Then
            Dim outcome = Await _exchangeActions.SetRangeAsync(ExchangePortRangeRules.Format(low, high))
            If outcome <> ExchangeProcess.ConfigOutcome.Saved Then
                ShowExchangeFailure(ExchangeAdminText.ConfigResult(outcome, ""))
                Return False
            End If
            _lowBaseline = low
            _highBaseline = high
        End If
        If _firewall.Checked Then
            Dim outcome = Await _exchangeActions.FirewallAsync()
            _firewallResult = ExchangeText.FirewallResult(outcome, False)
            _firewall.Checked = False
        End If
        Return True
    End Function

    Private Sub ShowExchangeFailure(text As String)
        MessageBox.Show(Me, text, Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub
End Class
