Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The server welcome of spec 185: one window per half, one step at a time. Every step has the three named parts
''' (what it is, why it exists, how it will work) above its input, and the acts are the existing ones - the share
''' server's enable dialog for A-1, the exchange wizard for B-4 (which carries the fingerprint and the agent). A step
''' writes its value on Next, never on paint. "Later" records the half as dismissed; closing the window without it
''' does not, so the welcome comes back until the half is configured. The rules are in <see cref="ServerWelcomeModel"/>.
''' </summary>
Friend NotInheritable Class Server_Welcome_Form
    Inherits Form

    ''' <summary>Text column width in design units; every paragraph wraps inside it.</summary>
    Private Const ContentWidth As Integer = 460

    ''' <summary>Spec 185 rule 8: the 44 px pointer target for buttons.</summary>
    Private Const TargetHeight As Integer = 44

    ''' <summary>The step badge's diameter in design units.</summary>
    Private Const BadgeSize As Integer = 64

    Private ReadOnly _half As WelcomeHalf
    Private ReadOnly _steps As IReadOnlyList(Of WelcomeStepKind)
    Private ReadOnly _settings As New ShareSettings()
    Private ReadOnly _panels As New List(Of FlowLayoutPanel)()
    Private ReadOnly _fonts As New List(Of Font)()
    Private ReadOnly _accentLabels As New List(Of Label)()

    Private _position As Integer
    Private _accountCreated As Boolean

    Private _heading As Label
    Private _counter As Label
    Private _badge As StepBadge
    Private _dots As StepDots
    Private _enableStatus As Label
    Private _enableButton As Button
    Private _systemRadio As RadioButton
    Private _fixedRadio As RadioButton
    Private _portNumber As NumericUpDown
    Private _doneStatus As Label
    Private _statement As TextBox
    Private _wizardButton As Button
    Private _wizardStatus As Label
    Private _later As Button
    Private _back As Button
    Private _next As Button

    Private Sub New(half As WelcomeHalf)
        _half = half
        _steps = ServerWelcomeModel.StepsFor(half)
        _settings.Load()
        BuildUi()
    End Sub

    ''' <summary>
    ''' Opens each half that is due, share first, and answers whether any window was shown. Called by the Share Manager
    ''' once its window is on screen (spec 185 section 3.1); the exchange half needs the shipping gate open.
    ''' </summary>
    Friend Shared Function OpenDueHalves(owner As Form) As Boolean
        Dim settings As New ShareSettings()
        settings.Load()
        Dim opened As Boolean = False

        If ServerWelcomeModel.ShouldOpen(WelcomeHalf.Share, ServerFeatures.IsEnabled(), settings.WelcomeShareDismissed, True, True) Then
            ShowHalf(owner, WelcomeHalf.Share)
            opened = True
        End If

        Dim gateOpen As Boolean = ExchangeFeatures.Offered
        Dim componentPresent As Boolean = gateOpen AndAlso ExchangeProcess.IsAvailable()
        Dim exchangeConfigured As Boolean = componentPresent AndAlso ExchangeAccountsExist()
        If ServerWelcomeModel.ShouldOpen(WelcomeHalf.Exchange, exchangeConfigured, settings.WelcomeExchangeDismissed, componentPresent, gateOpen) Then
            ShowHalf(owner, WelcomeHalf.Exchange)
            opened = True
        End If
        Return opened
    End Function

    Private Shared Function ExchangeAccountsExist() As Boolean
        Try
            Return ExchangeProcess.Inspect().Accounts > 0
        Catch
            ' An exchange server that does not answer counts as not configured: the welcome may show, and it says so.
            Return False
        End Try
    End Function

    Private Shared Sub ShowHalf(owner As Form, half As WelcomeHalf)
        Using dlg As New Server_Welcome_Form(half)
            dlg.ShowDialog(owner)
        End Using
    End Sub

    ' ---------------------------------------------------------------- the frame ----

    Private Sub BuildUi()
        ' The script font and reading direction of the active language before any control exists (013 block A').
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Первый запуск сервера")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        AddHandler FormClosed, AddressOf DisposeFonts

        _counter = New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 2)}
        _heading = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth - BadgeSize - 16, 0), .Margin = New Padding(0),
            .Font = BoldFont(1.5F)}
        _badge = New StepBadge(AddressOf CompanionAssets.OpenBundledAsset, BadgeSize) With {.Margin = New Padding(0, 0, 16, 0)}
        _dots = New StepDots() With {.Count = _steps.Count}

        ' The header: the step's round badge beside "Step N of M" and the step's title.
        Dim titleColumn As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Anchor = AnchorStyles.Left, .Margin = Padding.Empty}
        titleColumn.Controls.Add(_counter)
        titleColumn.Controls.Add(_heading)
        Dim header As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Margin = New Padding(0, 0, 0, 12)}
        header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        header.Controls.Add(_badge, 0, 0)
        header.Controls.Add(titleColumn, 1, 0)

        Dim body As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1, .Margin = New Padding(0)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        For Each kind As WelcomeStepKind In _steps
            Dim panel As FlowLayoutPanel = BuildPanel(kind)
            _panels.Add(panel)
            body.Controls.Add(panel, 0, _panels.Count - 1)
        Next
        body.RowCount = _panels.Count

        Dim root As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1, .Padding = New Padding(16, 16, 16, 12)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        root.Controls.Add(header, 0, 0)
        root.Controls.Add(_dots, 0, 1)
        root.Controls.Add(body, 0, 2)
        root.Controls.Add(BuildFooter(), 0, 3)
        Controls.Add(root)
        AddHandler ThemeService.Changed, AddressOf OnThemeChanged

        ShowStep(0)

        ' LAST statement of the builder: scaling reaches every control, because all of them exist now.
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Private Function BuildFooter() As TableLayoutPanel
        Dim footer As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 3, .Margin = New Padding(0, 12, 0, 0)}
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        _later = New Button With {.Text = Localization.T("Позже")}
        StyleButton(_later)
        ButtonGlyphs.Apply(_later, "action.cancel", ButtonGlyphRole.Dismissing)
        AddHandler _later.Click, AddressOf OnLater

        _back = New Button With {.Text = Localization.T("Назад")}
        StyleButton(_back)
        ButtonGlyphs.Apply(_back, "nav.back")
        AddHandler _back.Click, Sub() ShowStep(_position - 1)

        _next = New Button With {.Text = Localization.T("Далее")}
        StyleButton(_next)
        ButtonGlyphs.Apply(_next, "nav.forward", ButtonGlyphRole.Affirmative)
        AddHandler _next.Click, AddressOf OnNext

        Dim navigation As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False, .Margin = Padding.Empty}
        navigation.Controls.Add(_back)
        navigation.Controls.Add(_next)

        footer.Controls.Add(_later, 0, 0)
        footer.Controls.Add(navigation, 2, 0)
        Return footer
    End Function

    Private Shared Sub StyleButton(button As Button)
        button.AutoSize = True
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink
        button.MinimumSize = New Size(120, TargetHeight)
        button.Padding = New Padding(10, 0, 10, 0)
        button.Margin = New Padding(8, 0, 0, 0)
    End Sub

    ' ---------------------------------------------------------------- the steps ----

    Private Function BuildPanel(kind As WelcomeStepKind) As FlowLayoutPanel
        Dim panel As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0)}
        Select Case kind
            Case WelcomeStepKind.ShareEnable : BuildShareEnable(panel)
            Case WelcomeStepKind.SharePort : BuildSharePort(panel)
            Case WelcomeStepKind.ShareDone : BuildShareDone(panel)
            Case WelcomeStepKind.ExchangeStatement : BuildStatement(panel)
            Case WelcomeStepKind.ExchangeAccount : BuildAccount(panel)
            Case WelcomeStepKind.ExchangeDone : BuildExchangeDone(panel)
        End Select
        Return panel
    End Function

    Private Sub BuildShareEnable(panel As FlowLayoutPanel)
        AddExplanation(panel,
            Localization.T("SFTP-сервер для выбранных папок: телефон с приложением или любой SFTP-клиент откроет их по сети."),
            Localization.T("Телефон не видит папки на компьютере, пока сервер не включён. Включённый сервер ничего не открывает, пока вы не выбрали папки."),
            Localization.T("Сервер выключен до этого шага. Кнопка «Включить» запросит права администратора один раз и добавит одно правило брандмауэра Windows для этой программы. Телефоны подключаются сканированием QR-кода, который показывает окно программы."))
        ' Phone, over the network, to the folders this computer shares.
        panel.Controls.Add(Chips("@device.phone", "->", "@system.wifi", "->", "@content.folder"))
        _enableStatus = AddText(panel, String.Empty, New Padding(0, 12, 0, 0))
        _enableButton = New Button With {.Text = Localization.T("Включить сервер")}
        StyleButton(_enableButton)
        ButtonGlyphs.Apply(_enableButton, "action.run", ButtonGlyphRole.Caution)
        AddHandler _enableButton.Click, AddressOf OnEnableServer
        panel.Controls.Add(_enableButton)
        AddText(panel, Localization.T("Этот шаг можно пропустить: сервер останется выключенным."), New Padding(0, 12, 0, 0))
    End Sub

    Private Sub BuildSharePort(panel As FlowLayoutPanel)
        AddExplanation(panel,
            Localization.T("Номер порта, на котором сервер принимает подключения."),
            Localization.T("Номер записан в каждый QR-код и файл .fmscfg. Если его поменять, уже выданные коды перестанут работать."),
            Localization.TF("«Выбирает система» - порт назначается один раз и больше не меняется. «Фиксированный» - вы задаёте номер сами, например для проброса порта на роутере. По умолчанию предлагается {0}.",
                ServerWelcomeModel.SuggestedFixedPort))

        _systemRadio = New RadioButton With {.Text = Localization.T("Выбирает система"), .AutoSize = True,
            .MinimumSize = New Size(0, TargetHeight), .Margin = New Padding(0, 10, 0, 0)}
        _fixedRadio = New RadioButton With {.Text = Localization.T("Фиксированный порт:"), .AutoSize = True,
            .MinimumSize = New Size(0, TargetHeight)}
        _portNumber = New NumericUpDown With {.Minimum = ServerWelcomeModel.MinFixedPort,
            .Maximum = ServerWelcomeModel.MaxFixedPort, .Width = 120, .Margin = New Padding(24, 0, 0, 0)}

        Dim storedPort As Integer = _settings.ListenPort
        _systemRadio.Checked = Not ServerWelcomeModel.IsFixedPortValid(storedPort)
        _fixedRadio.Checked = Not _systemRadio.Checked
        _portNumber.Value = If(_systemRadio.Checked, ServerWelcomeModel.SuggestedFixedPort, storedPort)
        _portNumber.Enabled = _fixedRadio.Checked
        AddHandler _fixedRadio.CheckedChanged, Sub() _portNumber.Enabled = _fixedRadio.Checked

        panel.Controls.Add(_systemRadio)
        panel.Controls.Add(_fixedRadio)
        panel.Controls.Add(_portNumber)
        AddText(panel, Localization.T("Правило брандмауэра относится к программе, а не к порту, поэтому смена порта его не требует."),
                New Padding(0, 12, 0, 0))
    End Sub

    Private Sub BuildShareDone(panel As FlowLayoutPanel)
        AddExplanation(panel,
            Localization.T("Здесь вы сделали то, что нужно для первого подключения. Всё остальное живёт в настройках программы: порт, папки, подключённые устройства."),
            Localization.T("Выключить сервер, сменить порт или открыть папки можно в любой момент - в группах настроек."),
            Localization.T("Для подключения телефона дальше: отсканируйте QR-код в окне программы, затем опубликуйте первую папку."))
        _doneStatus = AddText(panel, String.Empty, New Padding(0, 12, 0, 0))
    End Sub

    Private Sub BuildStatement(panel As FlowLayoutPanel)
        AddExplanation(panel,
            Localization.T("Учётные записи, устройства и опубликованные ресурсы: вход по логину и паролю или по коду сопряжения."),
            Localization.T("Устройство доверяет серверу только после того, как проверит отпечаток его сертификата при первом подключении."),
            Localization.T("Ниже - заявление оператора в полном виде. Его нельзя пропустить: оно описывает, кто и что видит на сервере."))
        ' A device checks the certificate fingerprint before it trusts the folders behind it.
        panel.Controls.Add(Chips("@device.phone", "->", "@status.locked", "->", "@content.folder"))
        AddText(panel, Localization.T("Заявление оператора"), New Padding(0, 12, 0, 4))
        _statement = New TextBox With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical,
            .Width = ContentWidth, .Height = 150, .TabStop = False,
            .Text = String.Join(Environment.NewLine & Environment.NewLine, ExchangeStatement.Paragraphs())}
        panel.Controls.Add(_statement)
    End Sub

    Private Sub BuildAccount(panel As FlowLayoutPanel)
        AddExplanation(panel,
            Localization.T("Учётная запись, с которой войдёт первое устройство."),
            Localization.T("Без неё сервер ничего не принимает, а пароль показывается один раз."),
            Localization.T("Откроется мастер: он создаст учётную запись, запустит сервер, покажет отпечаток сертификата и, если нужно, подключит агент этого компьютера. Пароль показывается один раз; «Скопировать» кладёт его в буфер обмена, который очищается сам."))
        _wizardButton = New Button With {.Text = Localization.T("Открыть мастер")}
        StyleButton(_wizardButton)
        ButtonGlyphs.Apply(_wizardButton, "action.run", ButtonGlyphRole.Affirmative)
        AddHandler _wizardButton.Click, AddressOf OnOpenWizard
        panel.Controls.Add(_wizardButton)
        _wizardStatus = AddText(panel, String.Empty, New Padding(0, 12, 0, 0))
    End Sub

    Private Sub BuildExchangeDone(panel As FlowLayoutPanel)
        AddExplanation(panel,
            Localization.TF("Адрес для этого компьютера и для других устройств, логин и отпечаток сертификата записаны в группе «{0}».", ProductNames.ExchangeServer),
            Localization.T("Пароль больше нигде не показывается. Устройство подключается, пройдя проверку отпечатка при первом подключении."),
            Localization.T("Для Android-приложения следующий шаг - QR-код в окне программы. Остальные параметры - в группах настроек."))
    End Sub

    Private Sub AddExplanation(panel As FlowLayoutPanel, what As String, why As String, how As String)
        AddText(panel, Localization.T("Что это"), New Padding(0, 10, 0, 0), True)
        AddText(panel, what, New Padding(0, 2, 0, 0))
        AddText(panel, Localization.T("Зачем"), New Padding(0, 10, 0, 0), True)
        AddText(panel, why, New Padding(0, 2, 0, 0))
        AddText(panel, Localization.T("Как это будет работать"), New Padding(0, 10, 0, 0), True)
        AddText(panel, how, New Padding(0, 2, 0, 0))
    End Sub

    Private Function AddText(panel As FlowLayoutPanel, text As String, margin As Padding, Optional bold As Boolean = False) As Label
        Dim label As New Label With {.Text = text, .AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
            .UseMnemonic = False, .Margin = margin}
        If bold Then
            label.Font = BoldFont()
            label.ForeColor = ThemeService.Palette.Accent
            _accentLabels.Add(label)
        End If
        panel.Controls.Add(label)
        Return label
    End Function

    ''' <summary>A row of catalog glyphs, arrows and key caps (see <see cref="StepChips"/>); it carries no words.</summary>
    Private Shared Function Chips(ParamArray items As String()) As StepChips
        Return New StepChips(AddressOf CompanionAssets.OpenBundledAsset, items)
    End Function

    Private Sub OnThemeChanged(sender As Object, e As EventArgs)
        If IsDisposed Then Return
        For Each label As Label In _accentLabels
            label.ForeColor = ThemeService.Palette.Accent
        Next
        _badge.Invalidate()
        _dots.Invalidate()
    End Sub

    ''' <summary>The catalog glyph that stands for a step, drawn in the header's round badge.</summary>
    Private Shared Function StepGlyph(kind As WelcomeStepKind) As String
        Select Case kind
            Case WelcomeStepKind.ShareEnable : Return "action.share"
            Case WelcomeStepKind.SharePort : Return "system.wifi"
            Case WelcomeStepKind.ExchangeStatement : Return "status.locked"
            Case WelcomeStepKind.ExchangeAccount : Return "device.phone"
            Case Else : Return "status.ok"
        End Select
    End Function

    ' ---------------------------------------------------------------- navigation ----

    Private Sub ShowStep(index As Integer)
        _position = index
        For i As Integer = 0 To _panels.Count - 1
            _panels(i).Visible = (i = index)
        Next
        Dim kind As WelcomeStepKind = _steps(index)
        _heading.Text = StepTitle(kind)
        _badge.GlyphId = StepGlyph(kind)
        _dots.Index = index
        _counter.Text = Localization.TF("Шаг {0} из {1}", index + 1, _steps.Count)
        _back.Enabled = index > 0
        _next.Text = If(index = _steps.Count - 1, Localization.T("Готово"), Localization.T("Далее"))
        ' The answer on the last step is a plain confirmation, on the others it moves on: the glyph follows the caption.
        Dim nextGlyph As String = If(index = _steps.Count - 1, "action.confirm", "nav.forward")
        If ButtonGlyphs.GlyphOf(_next) <> nextGlyph Then ButtonGlyphs.Apply(_next, nextGlyph, ButtonGlyphRole.Affirmative)
        RefreshStep(kind)
    End Sub

    ''' <summary>Refreshes what a step shows from the live state (the enable dialog or the wizard may have changed it).</summary>
    Private Sub RefreshStep(kind As WelcomeStepKind)
        Dim enabled As Boolean = ServerFeatures.IsEnabled()
        If kind = WelcomeStepKind.ShareEnable Then
            _enableStatus.Text = If(enabled, Localization.T("Сервер включён."),
                                    Localization.T("Сервер пока выключен. Это не ошибка: включить его можно позже в настройках."))
            _enableButton.Enabled = Not enabled
        ElseIf kind = WelcomeStepKind.ShareDone Then
            _doneStatus.Text = If(enabled, Localization.T("Сервер включён."), Localization.T("Сервер выключен."))
        End If
        _next.Enabled = ServerWelcomeModel.CanAdvance(kind, _accountCreated)
    End Sub

    Private Shared Function StepTitle(kind As WelcomeStepKind) As String
        Select Case kind
            Case WelcomeStepKind.ShareEnable : Return Localization.T("Что станет этим компьютером")
            Case WelcomeStepKind.SharePort : Return Localization.T("Порт")
            Case WelcomeStepKind.ShareDone : Return Localization.T("Сервер настроен")
            Case WelcomeStepKind.ExchangeStatement : Return ExchangeText.Title()
            Case WelcomeStepKind.ExchangeAccount : Return Localization.T("Первая учётная запись")
            Case Else : Return Localization.T("Сервер готов")
        End Select
    End Function

    Private Async Sub OnNext(sender As Object, e As EventArgs)
        Dim kind As WelcomeStepKind = _steps(_position)
        If Not ServerWelcomeModel.CanAdvance(kind, _accountCreated) Then Return
        If kind = WelcomeStepKind.SharePort Then Await SavePortChoice()
        If _position = _steps.Count - 1 Then
            MarkDismissed()
            Close()
            Return
        End If
        ShowStep(_position + 1)
    End Sub

    Private Function SavePortChoice() As Task
        ' The value is written on Next, through the same settings object the Share Manager reads (spec 185 rule 4).
        _settings.Load()
        _settings.ListenPort = ServerWelcomeModel.PortToStore(_systemRadio.Checked, CInt(_portNumber.Value))
        _settings.Save()
        If Not ServerFeatures.IsEnabled() Then Return Task.CompletedTask
        Return PushPolicyQuietly()
    End Function

    Private Async Function PushPolicyQuietly() As Task
        Try
            Await ShareController.PushNetworkPolicyAsync()
        Catch
            ' The saved port is the one that counts: a worker that is not running takes it at its next start.
        End Try
    End Function

    Private Sub OnLater(sender As Object, e As EventArgs)
        MarkDismissed()
        Close()
    End Sub

    Private Sub MarkDismissed()
        _settings.Load()
        If _half = WelcomeHalf.Share Then
            _settings.WelcomeShareDismissed = True
        Else
            _settings.WelcomeExchangeDismissed = True
        End If
        _settings.Save()
    End Sub

    Private Sub OnEnableServer(sender As Object, e As EventArgs)
        Using dlg As New Share_Enable_Form()
            dlg.ShowDialog(Me)
        End Using
        RefreshStep(WelcomeStepKind.ShareEnable)
    End Sub

    Private Sub OnOpenWizard(sender As Object, e As EventArgs)
        Dim result As ExchangeWizardResult = Nothing
        Using dlg As New Exchange_Server_Wizard_Form()
            dlg.ShowDialog(Me)
            result = dlg.Result
        End Using
        If result IsNot Nothing Then
            _accountCreated = _accountCreated OrElse result.AccountCreated
            result.Wipe()
        End If
        _wizardStatus.Text = If(_accountCreated, Localization.T("Учётная запись создана."),
                                Localization.T("Учётная запись ещё не создана. Этот шаг обязательный: откройте мастер снова."))
        RefreshStep(WelcomeStepKind.ExchangeAccount)
    End Sub

    ' ---------------------------------------------------------------- fonts and window events ----

    Private Function BoldFont(Optional scale As Single = 1.0F) As Font
        Dim font As New Font(Me.Font.FontFamily, Me.Font.Size * scale, FontStyle.Bold)
        _fonts.Add(font)
        Return font
    End Function

    Private Sub DisposeFonts(sender As Object, e As EventArgs)
        RemoveHandler ThemeService.Changed, AddressOf OnThemeChanged
        For Each font As Font In _fonts
            font.Dispose()
        Next
        _fonts.Clear()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

End Class
