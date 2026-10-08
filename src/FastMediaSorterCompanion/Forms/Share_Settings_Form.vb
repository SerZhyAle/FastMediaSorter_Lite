Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>Manager settings with a vertical task navigation and independently scrolling pages.
''' Reversible preferences retain their existing immediate-apply behavior; server operations
''' remain explicit commands. WINDOWS-UI sections 2, 3 and 5.</summary>
Partial Public NotInheritable Class Share_Settings_Form
    Inherits Form

    Private _iconHandle As IntPtr
    Private _loading As Boolean
    Private _changed As Boolean
    Private _built As Boolean
    Friend ReadOnly SettingsBuildTimings As New Dictionary(Of String, Long)()
    Private ReadOnly settingsBuildWatch As Stopwatch = Stopwatch.StartNew()
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus

    Private chkAutostart As CheckBox
    Private chkOpenOnStart As CheckBox
    Private cmbTheme As ComboBox
    Private cmbLanguage As ComboBox
    Private chkLanOnly As CheckBox
    Private numMaxConns As NumericUpDown
    Private numPort As NumericUpDown
    Private btnApplyPort As Button
    ''' <summary>The port that is actually in force - in the QR codes and router rules
    ''' already handed out - or 0 on a machine that has never recorded one. The field above
    ''' is only a pending value until "Apply" commits it (ticket 028 Ф-5).</summary>
    Private _committedPort As Integer
    Private lnkFreePort As LinkLabel
    Private lnkRouterSearch As LinkLabel
    Private lblPortNote As Label
    Private lblAutostartNote As Label
    Private lblLanOnlyNote As Label
    Private toolTip As ToolTip

    Private _root As TableLayoutPanel
    Private _secStartup As CollapsibleSection
    Private _secNetwork As CollapsibleSection
    Private _secHosting As CollapsibleSection
    Private _secHelp As CollapsibleSection
    ''' <summary>The hosting group runs a blocking worker probe, so it is refreshed the
    ''' first time its page is selected rather than on every open of a dialog most people
    ''' use to tick one checkbox.</summary>
    Private _hostingLoaded As Boolean

    ''' <summary>Design width of the page, 96-DPI units. Set by the hosting group's prose,
    ''' which wraps at <see cref="HostingContentWidth"/>; the other two are far narrower and
    ''' a settings dialog that changed width per group would be worse than a little air.</summary>
    Private Const PageWidth As Integer = 820

    ''' <summary>True when an elevated action actually changed the machine - the caller
    ''' re-reads status then, exactly as the main window did when it owned the Hosting
    ''' button (the OnHostingClicked pattern).</summary>
    Public ReadOnly Property Changed As Boolean
        Get
            Return _changed
        End Get
    End Property

    ''' <summary>True when something here changed what the WORKER is doing - today the
    ''' pinned port and the LAN-only switch, either of which restarts a running server on
    ''' the spot. Nothing elevated happened, so <see cref="Changed"/> stays false, but the
    ''' caller's status snapshot (the port it prints, the state line, the internet path) is
    ''' stale all the same and has to be re-read.</summary>
    Public ReadOnly Property WorkerStateChanged As Boolean
        Get
            Return _portPushed OrElse _policyPushed OrElse _pxPushed
        End Get
    End Property

    Private _portPushed As Boolean
    Private _policyPushed As Boolean

    Public Sub New(Optional status As WorkerStatus = Nothing)
        _status = status
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        Me.SuspendLayout()
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Настройки менеджера")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        ' The tooltip is disposed with the window (ticket 054 H-20): a live ToolTip keeps
        ' a native window and references to every control it serves, so a closed settings
        ' dialog would stay reachable from the tray process that outlives it.
        AddHandler Me.FormClosed,
            Sub()
                Try : toolTip.Dispose() : Catch : End Try
                Try : _lblHost?.Font?.Dispose() : Catch : End Try
                Try : _lblLive?.Font?.Dispose() : Catch : End Try
                DisposeExchangeResources()
                DisposeProducerResources()
                DisposeAccountResources()
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub
        ' WINDOWS-UI section 5: pages scroll inside a stable, user-resizable window.
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimumSize = New Size(640, 360)
        Me.ClientSize = New Size(PageWidth, 480)
        toolTip = New ToolTip()

        _secStartup = CreateSettingsSection("startup", Localization.T("Запуск"))
        _secNetwork = CreateSettingsSection("network", Localization.T("Сеть"))
        _secHosting = CreateSettingsSection("hosting", Localization.T("Хостинг"))
        _secHelp = CreateSettingsSection("help", Localization.T("Справка"))
        BuildStartupSection(_secStartup)
        SettingsBuildTimings("startup") = settingsBuildWatch.ElapsedMilliseconds
        BuildNetworkSection(_secNetwork)
        SettingsBuildTimings("network") = settingsBuildWatch.ElapsedMilliseconds
        BuildHostingSection(_secHosting)
        SettingsBuildTimings("hosting") = settingsBuildWatch.ElapsedMilliseconds
        ' Between Hosting and Help, and ONLY when the exchange server component is installed: without
        ' it there is no group at all (ticket 142), so the window is exactly what it was.
        _secExchange = CreateExchangeSection()
        SettingsBuildTimings("exchange") = settingsBuildWatch.ElapsedMilliseconds
        ' The client side of the device exchange (ticket 180): the account group, only while the client gate is open. It sits after the
        ' server-administration group and before Help (AllSettingsSections), and is a partial of its own.
        _secAccount = CreateAccountSection()
        SettingsBuildTimings("account") = settingsBuildWatch.ElapsedMilliseconds
        BuildHelpSection(_secHelp)
        BuildAboutRows(_secHelp)
        BuildSettingsPages()
        SettingsBuildTimings("pages") = settingsBuildWatch.ElapsedMilliseconds

        Me.Controls.Add(_root)

        HoldSettingsLayout()
        ConfigureSettingsContract()
        SettingsBuildTimings("editors") = settingsBuildWatch.ElapsedMilliseconds
        LoadState()
        SettingsBuildTimings("state") = settingsBuildWatch.ElapsedMilliseconds
        ' Initialize the hosting page without a worker probe; the first visit reads live state.
        ApplyHostingState(Nothing)
        SettingsBuildTimings("hosting-state") = settingsBuildWatch.ElapsedMilliseconds
        UpdateSummaries()
        RestoreSettingsUiContext()
        _built = True

        DpiLayout.ApplyAutoScale(Me)
        ReleaseSettingsLayout()
        Me.ResumeLayout(True)
        SettingsBuildTimings("layout") = settingsBuildWatch.ElapsedMilliseconds
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        If Not RestoreSettingsWindowBounds() Then
            FitInitialSettingsBounds()
            DpiLayout.CenterOnOwner(Me)
        End If
    End Sub

    ''' <summary>The Запуск group: whether the program starts, and whether it shows itself
    ''' when it does - two different questions, hence two independent checkboxes.</summary>
    Private Sub BuildStartupSection(sec As CollapsibleSection)
        chkAutostart = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Запускать при входе в Windows")}
        AddHandler chkAutostart.CheckedChanged, AddressOf OnAutostartChanged
        sec.AddBodyRow(chkAutostart)

        ' The one case an unticked checkbox cannot express: the entry IS registered and
        ' Windows refuses to run it (Task Manager's Startup page leaves the Run value in
        ' place and records a veto instead). Without this line that state is pixel-identical
        ' to "autostart was never switched on", which is how a machine sat for weeks with a
        ' running share and no console. Ticking the box repairs it - SetEnabled lifts the veto.
        lblAutostartNote = New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 2, 0, 2)}
        sec.AddBodyRow(lblAutostartNote)
        CheckBoxLayout.BindHint(chkAutostart, lblAutostartNote)

        ' Off by default, and it governs EVERY plain start - the logon one, a double-click on
        ' the exe, a script - so unticked really means "no window" (TrayContext). Only an
        ' explicit request still opens it: the viewer's Share buttons, the tray icon, a
        ' folder to share.
        chkOpenOnStart = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Открывать окно менеджера при запуске")}
        AddHandler chkOpenOnStart.CheckedChanged, AddressOf OnOpenOnStartChanged
        sec.AddBodyRow(chkOpenOnStart)
        toolTip.SetToolTip(chkOpenOnStart, Localization.T("Без галочки любой запуск программы оставляет только значок рядом с часами - окно открывается двойным щелчком по нему. С галочкой окно открывается сразу."))

        ' The application theme (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md §4). The same
        ' stored value the viewer's Settings - Viewing offers: a change here repaints every
        ' window of this program at once and reaches a running viewer through ThemeService.
        Dim themeRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = False, .Margin = New Padding(0, 6, 0, 2)}
        Dim lblTheme As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 8, 0),
            .Text = Localization.T("Тема оформления")}
        cmbTheme = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 180, .Margin = New Padding(0, 2, 0, 0)}
        cmbTheme.Items.AddRange(New Object() {Localization.T("Как в системе"), Localization.T("Светлая"), Localization.T("Тёмная")})
        cmbTheme.SelectedIndex = CInt(ThemeService.Mode)
        AddHandler cmbTheme.SelectedIndexChanged, AddressOf OnThemeChanged
        sec.AddBodyRow(SettingsEditorRow(lblTheme, cmbTheme, themeRow.Margin))
        toolTip.SetToolTip(cmbTheme, Localization.T("Светлая, тёмная или как в Windows - для всех окон просмотрщика и менеджера общего доступа."))

        ' The UI language, beside the theme - the two things that change how every window of
        ' the program reads. Moved here from the main window's status strip (owner decision,
        ' 2026-10-03). The combo lists endonyms, so it stays legible in the language being
        ' left. A switch updates open windows and the tray without closing this dialog.
        Dim langRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = False, .Margin = New Padding(0, 6, 0, 2)}
        Dim lblLanguage As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 8, 0),
            .Text = Localization.T("Язык интерфейса")}
        cmbLanguage = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 180, .Margin = New Padding(0, 2, 0, 0)}
        cmbLanguage.Items.AddRange(Localization.Names)
        cmbLanguage.SelectedIndex = Localization.CurrentIndex
        AddHandler cmbLanguage.SelectedIndexChanged, AddressOf OnUiLanguageChanged
        sec.AddBodyRow(SettingsEditorRow(lblLanguage, cmbLanguage, langRow.Margin))
    End Sub

    ''' <summary>The combo index maps to Localization.Codes; captions update in place.</summary>
    Private Sub OnUiLanguageChanged(sender As Object, e As EventArgs)
        If cmbLanguage.SelectedIndex < 0 Then Return
        Dim code As String = Localization.Codes(cmbLanguage.SelectedIndex)
        If String.Equals(code, Localization.CurrentCode, StringComparison.OrdinalIgnoreCase) Then Return
        UiLanguage.Switch(code)

    End Sub

    ''' <summary>The four Help links the main window's status strip used to fold into its
    ''' drop-down (owner decision, 2026-10-03): read-once material, which is what this page
    ''' is for. The router link answers through the group's live summary, the way the
    ''' status strip's hint line used to.</summary>
    Private Sub BuildHelpSection(sec As CollapsibleSection)
        AddHelpLink(sec, Localization.T("Приложение для Android"),
                    Sub() NetworkInfo.OpenInBrowser(ShareGuide.AndroidSite()))
        AddHelpLink(sec, Localization.T("Как публиковать папки (сайт)"),
                    Sub() NetworkInfo.OpenInBrowser(ShareLinks.Localized(ShareGuide.SiteGuideUrl)))
        lnkRouterSearch = AddHelpLink(sec, Localization.T("Инструкция для моей модели роутера"),
                                      AddressOf OnRouterSearchClicked)
        ' The search walks the port-forward instructions for the model it finds; without a
        ' running share there is no forwarding to instruct - the status strip disabled the
        ' item for the same reason.
        lnkRouterSearch.Enabled = _status IsNot Nothing AndAlso _status.Running
        AddHelpLink(sec, Localization.TF("Открыть {0}", ProductNames.ShortLegacy),
                    AddressOf OnOpenViewerClicked)
    End Sub

    Private Shared Function AddHelpLink(sec As CollapsibleSection, text As String, onClick As Action) As LinkLabel
        Dim lnk As New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = text}
        AddHandler lnk.LinkClicked, Sub(s, e) onClick()
        sec.AddBodyRow(lnk)
        Return lnk
    End Function

    Private Async Sub OnRouterSearchClicked()
        _secHelp.Summary = Localization.T("Определяем роутер..")
        Dim rt As RouterIdentity = Await Task.Run(Function() RouterInfo.Detect())
        If IsDisposed Then Return
        NetworkInfo.OpenInBrowser(RouterInfo.SearchUrl(rt))
        _secHelp.Summary = If(rt.DisplayName().Length > 0, Localization.TF("Роутер: {0}", rt.DisplayName()),
            Localization.T("Модель не определена - открыт общий поиск."))
    End Sub

    Private Sub OnOpenViewerClicked()
        Dim exe As String = IO.Path.Combine(IO.Path.GetDirectoryName(Application.ExecutablePath),
                                            "FastMediaSorter_LITE.exe")
        If IO.File.Exists(exe) Then
            Try
                Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(exe) With {.UseShellExecute = True})
            Catch
            End Try
        Else
            MessageBox.Show(Me, Localization.TF("{0} не найден рядом.", ProductNames.CompactForLabel),
                            ProductNames.ShareManager, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    ''' <summary>The combo's order IS the ThemeMode order: system, light, dark.</summary>
    Private Sub OnThemeChanged(sender As Object, e As EventArgs)
        If cmbTheme.SelectedIndex < 0 Then Return
        Dim mode As ThemeMode = CType(cmbTheme.SelectedIndex, ThemeMode)
        If mode <> ThemeService.Mode Then ThemeService.SetMode(mode)
    End Sub

    Private Sub BuildNetworkSection(sec As CollapsibleSection)
        ' Max simultaneous connections - the DoS-resilience knob (2026-07-15 review).
        ' Default 10; the user may set 1..99999 (their server, their call).
        Dim pnlConns As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = False, .Margin = New Padding(0, 2, 0, 0)}
        Dim lblConns As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
            .Text = Localization.T("Макс. одновременных подключений:")}
        numMaxConns = New NumericUpDown With {.Minimum = ShareSettings.MinMaxConnections,
            .Maximum = ShareSettings.MaxMaxConnections, .Value = ShareSettings.DefaultMaxConnections,
            .Width = 84, .Margin = New Padding(0, 2, 0, 0)}
        AddHandler numMaxConns.ValueChanged, AddressOf OnMaxConnsChanged
        AddHandler numMaxConns.Leave, AddressOf OnMaxConnsCommit
        sec.AddBodyRow(SettingsEditorRow(lblConns, numMaxConns, pnlConns.Margin))
        toolTip.SetToolTip(numMaxConns, Localization.T("Сколько устройств могут быть подключены одновременно. По умолчанию 10; можно от 1 до 99999. Значение меньше 2 может кратко отклонять переподключение телефона."))

        ' The listen port (015_SPECIFICATION_SHARE_MANUAL_PORT.md). A plain number, not a
        ' mode: this IS the port, the one in the router rule and in every QR handed out. It
        ' was briefly a "Fixed port" checkbox, which was wrong twice over - it implied the
        ' port otherwise drifts by design (it does not; that was a bug, now fixed), and it
        ' offered a second state nobody wants for a number that lives inside a printed code.
        Dim pnlPort As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = False, .Margin = New Padding(0, 8, 0, 0)}
        Dim lblPort As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
            .Text = Localization.T("Порт:")}
        numPort = New NumericUpDown With {.Minimum = ShareSettings.MinFixedPort,
            .Maximum = ShareSettings.MaxFixedPort, .Value = ShareSettings.SuggestedFixedPort,
            .Width = 84, .Margin = New Padding(0, 2, 0, 0)}
        ' The escape hatch for the one way a guaranteed port can still fail: somebody else
        ' took it. "Pick another" should not mean guessing a number.
        lnkFreePort = New LinkLabel With {.AutoSize = True, .Margin = New Padding(10, 6, 0, 0),
            .Text = Localization.T("Подобрать свободный")}
        ' A value control no longer restarts the server as focus leaves it (APP-BEHAVIOUR
        ' rule 12, ticket 028 Ф-5): the number is pending until this button, which asks
        ' first and names what stops working.
        btnApplyPort = New Button With {.AutoSize = True, .Margin = New Padding(6, 0, 0, 0),
            .Text = Localization.T("Применить порт"), .Enabled = False}
        AddHandler numPort.ValueChanged, AddressOf OnPortValueChanged
        AddHandler btnApplyPort.Click, AddressOf OnApplyPort
        AddHandler lnkFreePort.LinkClicked, AddressOf OnPickFreePort
        sec.AddBodyRow(SettingsEditorRow(lblPort, numPort, pnlPort.Margin))
        pnlPort.Controls.Add(btnApplyPort)
        pnlPort.Controls.Add(lnkFreePort)
        sec.AddBodyRow(pnlPort)
        toolTip.SetToolTip(numPort, ShareText.PortHint())

        sec.AddBodyRow(New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 4, 0, 0),
            .Text = ShareText.PortHint()})

        ' One live line under the control: why the port is not serving, or that the codes
        ' handed out earlier no longer match. A port that failed to bind must never read as
        ' a plain "sharing is off".
        lblPortNote = New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 4, 0, 0)}
        sec.AddBodyRow(lblPortNote)

        ' The SERVER-WIDE "LAN only" enforcement (ticket 054 H-06). This is the switch the
        ' consent text names: while it is off, the program asks the router to open the
        ' port (UPnP/NAT-PMP) and checks the external path. It applies to the whole share
        ' - every code, not one - and is pushed to the worker THE MOMENT it moves, not at
        ' the next resume. The wizard's per-code checkbox only shapes one export and never
        ' reaches this push.
        chkLanOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 2),
            .Text = Localization.T("Только локальная сеть (без доступа из интернета)")}
        AddHandler chkLanOnly.CheckedChanged, AddressOf OnLanOnlyChanged
        sec.AddBodyRow(chkLanOnly)
        lblLanOnlyNote = New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 2, 0, 0)}
        sec.AddBodyRow(lblLanOnlyNote)
        CheckBoxLayout.BindHint(chkLanOnly, lblLanOnlyNote)
        toolTip.SetToolTip(chkLanOnly, Localization.T("Относится ко всей раздаче, а не к одному коду: пока включено, программа не просит роутер открыть порт (UPnP/NAT-PMP) и не даёт адрес из интернета. Адрес из интернета, уже вписанный в выданные коды, перестаёт работать; по локальной сети всё работает как раньше."))

        ' Calm, factual reachability note (decision F): same-network devices can reach the
        ' share while it runs. It sits here rather than on the main window because it is
        ' the footnote of the setting above, not a state anybody reads per session.
        sec.AddBodyRow(New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 10, 0, 0),
            .Text = ShareText.NetworkReachNote()})

        ' Ticket 148: where the share service is told about an exchange server. Built only where the feature
        ' can exist (the shipping gate of 133 section 8), so a window without it is exactly what it was.
        BuildExchangeProducerRows(sec)

        ' The way back from the opt-in (ticket 028 Ф-6, APP-BEHAVIOUR rule 4): a standing
        ' consent is revocable in one action, from where the network settings live. Hidden
        ' where this user cannot withdraw it from here - a Store build, the Server edition.
        If ServerFeatures.CanWithdraw() Then
            Dim btnWithdraw As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .Margin = New Padding(0, 10, 0, 0), .Padding = New Padding(8, 2, 8, 2),
                .Text = Localization.T("Выключить функции сервера..")}
            AddHandler btnWithdraw.Click, AddressOf OnWithdrawServerFeatures
            sec.AddBodyRow(btnWithdraw)
        End If
    End Sub

    ''' <summary>Stops sharing, then removes the firewall rule and the consent with one UAC
    ''' prompt. Asked first: it disconnects every phone. On success the dialog closes and the
    ''' main window falls back to its enable panel.</summary>
    Private Async Sub OnWithdrawServerFeatures(sender As Object, e As EventArgs)
        If MessageBox.Show(Me, ShareText.ServerDisableQuestion(), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Try
            Await ShareController.StopServerAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Withdraw: stopping the server", ex)
        End Try
        ' Off the UI thread (ticket 054 H-15): the helper blocks on its UAC prompt, and
        ' this window is not the only thing that must stay alive while it does.
        Dim result As ServerFeatures.WithdrawResult = Await Task.Run(Function() ServerFeatures.WithdrawViaElevation())
        If IsDisposed OrElse Disposing Then Return
        Select Case result
            Case ServerFeatures.WithdrawResult.Declined
                MessageBox.Show(Me, Localization.T("Ничего не изменено: права администратора не получены. Раздача остановлена - её можно снова включить в главном окне."),
                                Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Case ServerFeatures.WithdrawResult.Failed
                MessageBox.Show(Me, Localization.T("Не удалось выполнить действие. Повторите попытку; если не выйдет - отправьте журнал автору: меню значка в трее - «Отправить журнал автору..»."),
                                Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Case ServerFeatures.WithdrawResult.StillEnabledByInstaller
                _changed = True
                MessageBox.Show(Me, Localization.T("Разрешение в брандмауэре удалено, но функции сервера включены установщиком для всего компьютера. Чтобы выключить их полностью, измените установку: Параметры Windows - Приложения."),
                                Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Case Else
                _changed = True
                Me.DialogResult = DialogResult.OK
        End Select
    End Sub

    ' --- layout helpers ---------------------------------------------------------

    Private Shared Function SettingsEditorRow(caption As Label, editor As Control, margin As Padding) As Control
        Return New ManagerSettingRow(caption, editor, margin)
    End Function

    Private Shared Sub AddRow(tlp As TableLayoutPanel, c As Control)
        Dim row As Integer = tlp.RowCount
        tlp.RowCount = row + 1
        tlp.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        tlp.Controls.Add(c, 0, row)
    End Sub

    ' --- state ------------------------------------------------------------------

    Private Sub LoadState()
        Dim prev As Boolean = _loading
        _loading = True
        Try
            chkAutostart.Checked = AutostartManager.IsEnabled()
            chkAutostart.Enabled = Not AutostartManager.IsPackaged()
            UpdateAutostartNote()
            If AutostartManager.IsPackaged() Then
                toolTip.SetToolTip(chkAutostart, Localization.T("Автозапуском управляет Windows (пакет из Store)."))
            End If
            chkOpenOnStart.Checked = _settings.OpenWindowOnStartup
            chkLanOnly.Checked = _settings.LanOnly
            UpdateLanOnlyNote()
            numMaxConns.Value = ShareSettings.ClampConnections(_settings.MaxConnections)
            LoadPortState()
            If ServerFeatures.IsSystemServiceHost() Then
                ' Autostart still governs whether the CONSOLE appears, but no longer
                ' whether the folders are reachable - say so where the confusion is.
                toolTip.SetToolTip(chkAutostart, HostingText.Intro(ServerFeatures.ServerHostMode.SystemService))
            End If
        Catch
        Finally
            _loading = prev
        End Try
    End Sub

    ''' <summary>
    ''' The live one-liners the folded groups show. Filled by the same code path that fills
    ''' the bodies, which is what makes "collapsed is not hidden" true here as well: a user
    ''' who opens this dialog to check whether autostart is on gets the answer without
    ''' unfolding anything.
    ''' </summary>
    Private Sub UpdateSummaries()
        If _secStartup Is Nothing Then Return
        ' A shell veto is its own summary: folded, "Автозапуск выключен" would be the
        ' second place this state hides behind a word that means something else.
        If AutostartManager.State() = AutostartState.BlockedByShell Then
            _secStartup.Summary = Localization.T("Автозапуск отключён в Windows")
            _secStartup.SummaryColor = CollapsibleSection.AttentionColor
        Else
            _secStartup.Summary = If(chkAutostart.Checked,
                                     Localization.T("Автозапуск включён"),
                                     Localization.T("Автозапуск выключен"))
            _secStartup.SummaryColor = ThemeService.Palette.TextMuted
        End If
        ' The port is exactly the kind of fact a folded section must still answer: someone
        ' opens this dialog to check which number their router rule has to name.
        _secNetwork.Summary = Localization.TF("до {0} подключений, порт {1}",
                                              CInt(numMaxConns.Value), If(_committedPort > 0, _committedPort, CInt(numPort.Value)))
        _secNetwork.SummaryColor = ThemeService.Palette.TextMuted
        ' Verbatim from HostingText - the same sentence the group's own first line shows,
        ' so the folded and unfolded states cannot drift apart.
        _secHosting.Summary = HostingText.HostModeLine(ServerFeatures.HostMode())
        _secHosting.SummaryColor = ThemeService.Palette.TextMuted
        UpdateExchangeSummary()
        UpdateAccountSummary()
    End Sub

    ' --- the pinned listen port -------------------------------------------------

    ''' <summary>
    ''' Fills the port row with the port this PC actually serves on - from the worker, which
    ''' reports it even while the server is down, because that number is already printed in
    ''' the QR codes people scanned. The suggestion is only for a machine that has never
    ''' started sharing at all.
    ''' </summary>
    Private Sub LoadPortState()
        Dim shown As Integer = ShareController.EffectivePort(_status)
        If shown < ShareSettings.MinFixedPort OrElse shown > ShareSettings.MaxFixedPort Then
            shown = ShareSettings.SuggestedFixedPort
            _committedPort = 0
        Else
            _committedPort = shown
        End If
        _baselinePort = shown
        numPort.Value = CDec(shown)
        UpdatePortApply()
        UpdatePortNote()
    End Sub

    ''' <summary>What the field held when the dialog opened, or after the last Apply: the
    ''' port in force, or on a machine that never shared, the suggestion - which the worker
    ''' may replace with its own first choice, so it is not something to "apply".</summary>
    Private _baselinePort As Integer

    Private Function PortPending() As Boolean
        Return numPort IsNot Nothing AndAlso CInt(numPort.Value) <> _baselinePort
    End Function

    ''' <summary>"Apply" is live only while the field holds a number that is not in force.</summary>
    Private Sub UpdatePortApply()
        If btnApplyPort Is Nothing Then Return
        btnApplyPort.Enabled = PortPending()
    End Sub

    ''' <summary>The port the server is actually on, or 0. A snapshot from when this dialog
    ''' opened - the main window owns the live line.</summary>
    Private Function LivePort() As Integer
        If _status Is Nothing OrElse Not _status.Running Then Return 0
        Return _status.ListenPort
    End Function

    ''' <summary>The line under the autostart checkbox. It exists for exactly two states -
    ''' the entry is registered and the shell vetoes it, or the entry names an exe that is
    ''' no longer on disk - and says nothing in every other, because a checkbox that already
    ''' reads "on" needs no second voice agreeing with it.
    ''' </summary>
    Private Sub UpdateAutostartNote()
        If lblAutostartNote Is Nothing Then Return
        If AutostartManager.State() = AutostartState.BlockedByShell Then
            lblAutostartNote.Text = Localization.T("Этот пункт автозагрузки отключён в Windows (Диспетчер задач - Автозагрузка приложений), поэтому значок не появляется при входе. Поставьте галочку заново, чтобы включить его.")
            lblAutostartNote.ForeColor = CollapsibleSection.AttentionColor
        ElseIf AutostartManager.IsTargetMissing() Then
            ' A Run value whose exe is gone (ticket 054 H-16): the entry exists, so the
            ' checkbox reads "on", and nothing will ever start. State() unticks it and this
            ' line says why - re-ticking repairs it by pointing the entry here.
            lblAutostartNote.Text = Localization.T("Автозапуск указывает на удалённый файл - значок не появится при входе. Поставьте галочку заново, чтобы вернуть его.")
            lblAutostartNote.ForeColor = CollapsibleSection.AttentionColor
        Else
            lblAutostartNote.Text = ""
            lblAutostartNote.ForeColor = ThemeService.Palette.TextMuted
        End If
        lblAutostartNote.Visible = lblAutostartNote.Text <> ""
    End Sub

    ''' <summary>Either the honest verdict on a port that is not serving, or the re-export
    ''' reminder once the number really moved. Never a reassurance nobody asked for: with
    ''' nothing to say the line disappears.</summary>
    Private Sub UpdatePortNote()
        If lblPortNote Is Nothing Then Return
        Dim warn As String = ShareController.PortWarning(_status)
        If warn <> "" Then
            lblPortNote.Text = warn
            lblPortNote.ForeColor = CollapsibleSection.AttentionColor
        ElseIf PortPending() Then
            ' A pending number must not look applied: closing the dialog now discards it.
            lblPortNote.Text = Localization.TF("Порт {0} ещё не применён - нажмите «Применить порт».", CInt(numPort.Value))
            lblPortNote.ForeColor = CollapsibleSection.AttentionColor
        ElseIf LivePort() > 0 AndAlso LivePort() <> _baselinePort Then
            lblPortNote.Text = ShareText.PortReexportHint()
            lblPortNote.ForeColor = CollapsibleSection.AttentionColor
        Else
            lblPortNote.Text = ""
            lblPortNote.ForeColor = ThemeService.Palette.TextMuted
        End If
        lblPortNote.Visible = lblPortNote.Text <> ""
    End Sub

    ''' <summary>Records the port on this side. The worker keeps its own copy and is the one
    ''' that guarantees it; this is what the window shows and what gets re-pushed on the next
    ''' resume, so the two must not drift.</summary>
    Private Sub SavePortChoice()
        _settings.ListenPort = ShareSettings.ClampPort(CInt(numPort.Value))
        _settings.Save()
    End Sub

    ''' <summary>Fills the field with a port that is free right now. Advisory - anything can
    ''' take it between the probe and the worker's bind - so it is committed through the same
    ''' path as a typed number and the worker still has the last word.</summary>
    Private Sub OnPickFreePort(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim found As Integer = FreePortFinder.FindFree(CInt(numPort.Value))
        If found = ShareSettings.UnsetPort Then Return
        ' Fills the field only - the number is committed by "Apply", like a typed one.
        numPort.Value = CDec(found)
        If btnApplyPort IsNot Nothing AndAlso btnApplyPort.Enabled Then btnApplyPort.Focus()
    End Sub

    ''' <summary>The field is a pending value: nothing is saved or pushed until "Apply".</summary>
    Private Sub OnPortValueChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        UpdatePortApply()
        UpdatePortNote()
    End Sub

    ''' <summary>
    ''' Commits the pending port (APP-BEHAVIOUR rules 5 and 12, ticket 028 Ф-5, owner
    ''' decision 2026-09-24). The port is what every phone is paired to (spec 015): once one
    ''' was in force, changing it makes every QR code and .fmscfg handed out, and the router
    ''' rule, stop working - so that is said BEFORE the change, not in a note afterwards.
    ''' </summary>
    Private Async Sub OnApplyPort(sender As Object, e As EventArgs)
        If Not PortPending() Then Return
        Dim newPort As Integer = CInt(numPort.Value)
        If _committedPort > 0 Then
            Dim question As String = Localization.TF(
                "Сменить порт {0} на {1}?" & vbCrLf & vbCrLf &
                "Все QR-коды и файлы .fmscfg, уже выданные телефонам, и правило проброса порта на роутере перестанут работать - их придётся создать заново. Работающая раздача перезапустится на новом порту.",
                _committedPort, newPort)
            If MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        End If
        Try
            SavePortChoice()
        Catch
        End Try
        _committedPort = newPort
        _baselinePort = newPort
        _portPushed = True
        UpdatePortApply()
        UpdatePortNote()
        UpdateSummaries()
        Try
            Await ShareController.PushNetworkPolicyAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Share settings: push the port", ex)
        End Try
        Await EnsurePortServedAsync(newPort)
        UpdatePortNote()
        UpdateSummaries()
    End Sub

    ''' <summary>Makes the new port the one being served, without a second step the user has to know
    ''' about. The worker is asked to rebind by the policy push; when it has not come up on the number
    ''' within 10 s, the Windows service is restarted - without UAC where the install granted the
    ''' management user that right, otherwise through the one elevated helper. The verdict is read back
    ''' from the worker each time, never assumed. Only the service host needs this: a user-session
    ''' worker is restarted by the push itself.</summary>
    Private Async Function EnsurePortServedAsync(newPort As Integer) As Task
        If ServerFeatures.HostMode() <> ServerFeatures.ServerHostMode.SystemService Then Return
        If Await WaitForServedPortAsync(newPort, TimeSpan.FromSeconds(10)) Then
            AppFileLogger.WriteLine("Share settings: port " & newPort.ToString() & " is served")
            Return
        End If
        AppFileLogger.WriteLine("Share settings: port " & newPort.ToString() & " not served after the push - restarting the service")
        ' The sc.exe calls and the UAC wait both block, so they run off the UI thread.
        If Not Await Task.Run(Function() ServiceControl.TryRestartWithoutElevation(TimeSpan.FromSeconds(30))) Then
            ' No grant on this install (or the service refused): the elevated helper, one UAC prompt.
            Dim outcome As ManageResult = Await Task.Run(Function() ServiceControl.Manage(ManageAction.RestartService))
            If outcome <> ManageResult.Succeeded Then
                AppFileLogger.WriteLine("Share settings: service restart did not happen (" & outcome.ToString() & ")")
            End If
        End If
        If Await WaitForServedPortAsync(newPort, TimeSpan.FromSeconds(20)) Then
            AppFileLogger.WriteLine("Share settings: port " & newPort.ToString() & " is served after the restart")
        Else
            AppFileLogger.WriteLine("Share settings: port " & newPort.ToString() & " still not served after the restart")
        End If
    End Function

    ''' <summary>Polls the worker until it reports Running on <paramref name="port"/>, or the time runs out.
    ''' Every poll refreshes the status the port note reads, so the note tells the same story.</summary>
    Private Async Function WaitForServedPortAsync(port As Integer, within As TimeSpan) As Task(Of Boolean)
        Dim deadline As DateTime = DateTime.UtcNow.Add(within)
        Do
            Try
                _status = Await ShareController.GetStatusAsync()
            Catch ex As Exception
                AppFileLogger.LogException("Share settings: read the status", ex)
            End Try
            If _status IsNot Nothing AndAlso _status.Running AndAlso _status.ListenPort = port Then Return True
            If DateTime.UtcNow >= deadline Then Return False
            Await Task.Delay(500)
        Loop
    End Function

    ' --- handlers (moved verbatim from MainWindow) ------------------------------

    ' Persist the connection cap on every change (cheap, local); the live push to the
    ' worker happens on Leave (OnMaxConnsCommit) so holding the spinner does not
    ' restart the running server on each intermediate value.
    Private Sub OnMaxConnsChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            _settings.MaxConnections = ShareSettings.ClampConnections(CInt(numMaxConns.Value))
            _settings.Save()
        Catch
        End Try
        UpdateSummaries()
    End Sub

    Private Async Sub OnMaxConnsCommit(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            _settings.MaxConnections = ShareSettings.ClampConnections(CInt(numMaxConns.Value))
            _settings.Save()
        Catch
        End Try
        Try
            Await ShareController.PushNetworkPolicyAsync()
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' The server-wide LAN-only switch (ticket 054 H-06): persisted AND pushed the moment
    ''' it moves, so the enforcing half follows the checkbox instead of waiting for the next
    ''' resume. Turning it on while a share is live is asked about first: it removes the
    ''' internet path from every code already handed out, which is not a side effect to
    ''' discover from a phone that no longer connects.
    ''' </summary>
    Private Async Sub OnLanOnlyChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        If chkLanOnly.Checked AndAlso LiveServerRunning() Then
            Dim question As String = Localization.T(
                "Включить режим «только локальная сеть»?" & vbCrLf & vbCrLf &
                "Программа перестанет просить роутер открыть порт (UPnP/NAT-PMP) и не будет выдавать адрес из интернета. Адрес из интернета в уже выданных кодах перестанет работать; по локальной сети всё продолжит работать. Работающая раздача перезапустится.")
            If MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                Dim prev As Boolean = _loading
                _loading = True
                chkLanOnly.Checked = False
                _loading = prev
                Return
            End If
        End If
        Try
            _settings.LanOnly = chkLanOnly.Checked
            _settings.Save()
        Catch
        End Try
        _policyPushed = True
        Try
            Await ShareController.PushNetworkPolicyAsync()
        Catch
        End Try
        ' The dialog may have closed while the push was out (ticket 054 H-11).
        If IsDisposed OrElse Disposing Then Return
        UpdateLanOnlyNote()
    End Sub

    ''' <summary>Whether the status this dialog was opened with showed a running server -
    ''' what decides that switching LAN-only on deserves the question above.</summary>
    Private Function LiveServerRunning() As Boolean
        Return _status IsNot Nothing AndAlso _status.Running
    End Function

    ''' <summary>The line under the LAN-only switch. Only the older-worker refusal has
    ''' anything to say here - a push the worker answered keeps quiet.</summary>
    Private Sub UpdateLanOnlyNote()
        If lblLanOnlyNote Is Nothing Then Return
        Dim warn As String = ShareController.PolicyWarning()
        lblLanOnlyNote.Text = warn
        lblLanOnlyNote.ForeColor = CollapsibleSection.AttentionColor
        lblLanOnlyNote.Visible = warn <> ""
    End Sub

    Private Sub OnAutostartChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        If AutostartManager.IsPackaged() Then Return
        Try
            AutostartManager.SetEnabled(chkAutostart.Checked)
            _settings.AutostartEnabled = AutostartManager.IsEnabled()
            _settings.Save()
        Catch
            Dim prev As Boolean = _loading
            _loading = True
            chkAutostart.Checked = AutostartManager.IsEnabled()
            _loading = prev
        End Try
        UpdateAutostartNote()
        UpdateSummaries()
        ' The note is two wrapped lines tall when it is there and nothing when it is not,
        ' so repairing the veto has to give the height back.
        If _built Then _pageHost.PerformLayout()
    End Sub

    ''' <summary>Persists the "open the window at startup" sub-option. Read back by
    ''' <see cref="TrayContext"/> on the next silent (--tray) launch.</summary>
    Private Sub OnOpenOnStartChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            _settings.OpenWindowOnStartup = chkOpenOnStart.Checked
            _settings.Save()
        Catch
        End Try
    End Sub

End Class
