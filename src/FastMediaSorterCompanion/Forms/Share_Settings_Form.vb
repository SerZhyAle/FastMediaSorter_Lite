Option Strict On

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "Настройки менеджера" - one page, three collapsible groups: Запуск, Сеть, Хостинг.
''' Same control and the same rules as the main window's sections
''' (SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md §3.2), so the two windows read as one
''' program: a folded group still answers its own question through a live summary, and
''' nothing is hidden - only folded.
'''
''' The first two groups hold what the main window used to carry between two consumer
''' checkboxes and a connection spinner (§3.5). The rule that puts them here is §2 rule 8:
''' nothing that is only interesting ONCE belongs on the window a user looks at every
''' session. Autostart is decided on the first run, the connection cap is a knob almost
''' nobody moves, and neither is read while answering "is anything being shared right now".
'''
''' The third group IS the former Hosting console (see Share_Settings_Form.Hosting.vb).
''' It was its own window until 2026-08-15, which made the path to an elevated action four
''' levels deep - main window, settings, console, UAC. Since the compact-window work left
''' the settings window as its ONLY caller, the console did not have to be a window at all.
''' It is the big one - four state lines, up to nine buttons, three paragraphs of notes,
''' against two checkboxes and a spinner - which is exactly why it starts folded: the
''' dialog opens at the size of the two small groups and grows only if you ask for it.
'''
''' The handlers moved verbatim, split semantics included: the connection cap persists on
''' every change (cheap, local) but is pushed to the worker on Leave, so holding the spinner
''' does not restart a running server on each intermediate value. The _loading guard came
''' with them.
'''
''' NOT here, on purpose: anything about a SPECIFIC share (Share_Root_Params_Form), the
''' server-features gate (Share_Enable_Form), and the UI language (the main window's status
''' strip owns it - it must stay reachable for someone who cannot read the rest).
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form
    Inherits Form

    Private _iconHandle As IntPtr
    Private _loading As Boolean
    Private _changed As Boolean
    Private _built As Boolean
    Private ReadOnly _settings As New ShareSettings()
    Private ReadOnly _status As WorkerStatus

    Private chkAutostart As CheckBox
    Private chkOpenOnStart As CheckBox
    Private numMaxConns As NumericUpDown
    Private numPort As NumericUpDown
    Private lnkFreePort As LinkLabel
    Private lblPortNote As Label
    Private toolTip As ToolTip

    Private _root As TableLayoutPanel
    Private _strip As Panel
    Private _secStartup As CollapsibleSection
    Private _secNetwork As CollapsibleSection
    Private _secHosting As CollapsibleSection
    ''' <summary>The hosting group runs a blocking worker probe, so it is refreshed the
    ''' first time it is actually unfolded rather than on every open of a dialog most people
    ''' use to tick one checkbox.</summary>
    Private _hostingLoaded As Boolean

    ''' <summary>Design width of the page, 96-DPI units. Set by the hosting group's prose,
    ''' which wraps at <see cref="HostingContentWidth"/>; the other two are far narrower and
    ''' a settings dialog that changed width per group would be worse than a little air.</summary>
    Private Const PageWidth As Integer = 560

    ''' <summary>True when an elevated action actually changed the machine - the caller
    ''' re-reads status then, exactly as the main window did when it owned the Hosting
    ''' button (the OnHostingClicked pattern).</summary>
    Public ReadOnly Property Changed As Boolean
        Get
            Return _changed
        End Get
    End Property

    ''' <summary>True when something here changed what the WORKER is doing - today only the
    ''' pinned port, which restarts a running server on another number. Nothing elevated
    ''' happened, so <see cref="Changed"/> stays false, but the caller's status snapshot (the
    ''' port it prints, the state line) is stale all the same and has to be re-read.</summary>
    Public ReadOnly Property WorkerStateChanged As Boolean
        Get
            Return _portPushed
        End Get
    End Property

    Private _portPushed As Boolean

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
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Настройки менеджера")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        ' Sizable rather than the usual FixedDialog: an accordion changes height by design,
        ' and a fixed dialog that outgrows the screen has no way to give the user the rest.
        ' The root panel AutoScrolls, so the worst case is a scrollbar, never a lost button.
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimumSize = New Size(420, 220)
        Me.ClientSize = New Size(PageWidth, 320)
        toolTip = New ToolTip()

        _root = New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1,
            .AutoScroll = True, .Padding = New Padding(12, 10, 12, 6)}
        _root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _secStartup = New CollapsibleSection("startup", Localization.T("Запуск"))
        _secNetwork = New CollapsibleSection("network", Localization.T("Сеть"))
        _secHosting = New CollapsibleSection("hosting", Localization.T("Хостинг"))
        BuildStartupSection(_secStartup)
        BuildNetworkSection(_secNetwork)
        BuildHostingSection(_secHosting)
        For Each sec As CollapsibleSection In {_secStartup, _secNetwork, _secHosting}
            sec.Dock = DockStyle.Fill
            AddHandler sec.ExpandedChanged, AddressOf OnSectionExpandedChanged
            AddRow(_root, sec)
        Next

        _strip = New Panel With {.Dock = DockStyle.Bottom, .Height = 44, .Padding = New Padding(12, 6, 12, 8)}
        Dim btnClose As New Button With {.Dock = DockStyle.Right, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(16, 5, 16, 5),
            .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.OK}
        _strip.Controls.Add(btnClose)

        ' Fill first, docked strip second - WinForms docks from the END of the collection.
        Me.Controls.Add(_root)
        Me.Controls.Add(_strip)
        Me.AcceptButton = btnClose
        Me.CancelButton = btnClose

        LoadState()
        ' Before the sizing below, and deliberately probe-free: the hosting group shows at
        ' most half of its nine actions in any given state, and measuring it with all of
        ' them visible sizes the dialog for a state it will never draw.
        ApplyHostingState(False)
        UpdateSummaries()
        ' The two small groups open, the big one folded (see the class remark).
        _secStartup.Expanded = True
        _secNetwork.Expanded = True
        _built = True

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        FitToContent()
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
    End Sub

    ''' <summary>The Запуск group: whether the program starts, and whether it shows itself
    ''' when it does - two different questions, hence two independent checkboxes.</summary>
    Private Sub BuildStartupSection(sec As CollapsibleSection)
        chkAutostart = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Запускать при входе в Windows")}
        AddHandler chkAutostart.CheckedChanged, AddressOf OnAutostartChanged
        sec.AddBodyRow(chkAutostart)

        ' Off by default, and it governs EVERY plain start - the logon one, a double-click on
        ' the exe, a script - so unticked really means "no window" (TrayContext). Only an
        ' explicit request still opens it: the viewer's Share buttons, the tray icon, a
        ' folder to share.
        chkOpenOnStart = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Открывать окно менеджера при запуске")}
        AddHandler chkOpenOnStart.CheckedChanged, AddressOf OnOpenOnStartChanged
        sec.AddBodyRow(chkOpenOnStart)
        toolTip.SetToolTip(chkOpenOnStart, Localization.T("Без галочки любой запуск программы оставляет только значок рядом с часами - окно открывается двойным щелчком по нему. С галочкой окно открывается сразу."))
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
        pnlConns.Controls.Add(lblConns)
        pnlConns.Controls.Add(numMaxConns)
        sec.AddBodyRow(pnlConns)
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
        AddHandler numPort.ValueChanged, AddressOf OnPortValueChanged
        AddHandler numPort.Leave, AddressOf OnPortCommit
        AddHandler lnkFreePort.LinkClicked, AddressOf OnPickFreePort
        pnlPort.Controls.Add(lblPort)
        pnlPort.Controls.Add(numPort)
        pnlPort.Controls.Add(lnkFreePort)
        sec.AddBodyRow(pnlPort)
        toolTip.SetToolTip(numPort, ShareText.PortHint())

        sec.AddBodyRow(New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 4, 0, 0),
            .Text = ShareText.PortHint()})

        ' One live line under the control: why the port is not serving, or that the codes
        ' handed out earlier no longer match. A port that failed to bind must never read as
        ' a plain "sharing is off".
        lblPortNote = New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 4, 0, 0)}
        sec.AddBodyRow(lblPortNote)

        ' Calm, factual reachability note (decision F): same-network devices can reach the
        ' share while it runs. It sits here rather than on the main window because it is
        ' the footnote of the setting above, not a state anybody reads per session.
        sec.AddBodyRow(New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 10, 0, 0),
            .Text = ShareText.NetworkReachNote()})
    End Sub

    ' --- layout helpers ---------------------------------------------------------

    Private Shared Sub AddRow(tlp As TableLayoutPanel, c As Control)
        Dim row As Integer = tlp.RowCount
        tlp.RowCount = row + 1
        tlp.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        tlp.Controls.Add(c, 0, row)
    End Sub

    ''' <summary>
    ''' Sizes the dialog to whatever the groups currently need, capped by the screen. Only
    ''' the HEIGHT follows the accordion: the width is set once by the hosting group's prose,
    ''' and a dialog that also changed width per group would be worse than a little air on
    ''' the two narrow ones. The top edge stays put, so the Close button moves with the
    ''' bottom edge instead of jumping out from under the cursor.
    ''' </summary>
    Private Sub FitToContent()
        If _root Is Nothing Then Return
        ' PreferredSize, and never Control.Visible: this runs from OnLoad, where the form is
        ' not on screen yet and EVERY child therefore reports Visible = False (it is an
        ' ancestor-aware property). Reading it there measured the page as empty and the
        ' dialog opened at its 220 px minimum with a scrollbar over content that fits.
        Dim content As Integer = 0
        For Each sec As CollapsibleSection In {_secStartup, _secNetwork, _secHosting}
            content += sec.PreferredSize.Height + sec.Margin.Vertical
        Next
        Dim needed As Integer = content + _root.Padding.Vertical + _strip.Height +
                                (Me.Height - Me.ClientSize.Height)
        Dim wa As Rectangle = DpiLayout.WorkingAreaFor(Me)
        Dim h As Integer = Math.Max(Me.MinimumSize.Height, Math.Min(needed, wa.Height))
        If h <> Me.Height Then Me.Height = h
        DpiLayout.NudgeOnScreen(Me, wa)
    End Sub

    ''' <summary>Unfolding the hosting group is the moment its live probe is worth paying
    ''' for - and the moment the dialog has to make room for it.</summary>
    Private Sub OnSectionExpandedChanged(sender As Object, e As EventArgs)
        If Not _built Then Return
        If _secHosting IsNot Nothing AndAlso _secHosting.Expanded Then EnsureHostingLoaded()
        FitToContent()
    End Sub

    ' --- state ------------------------------------------------------------------

    Private Sub LoadState()
        Dim prev As Boolean = _loading
        _loading = True
        Try
            chkAutostart.Checked = AutostartManager.IsEnabled()
            chkAutostart.Enabled = Not AutostartManager.IsPackaged()
            If AutostartManager.IsPackaged() Then
                toolTip.SetToolTip(chkAutostart, Localization.T("Автозапуском управляет Windows (пакет из Store)."))
            End If
            chkOpenOnStart.Checked = _settings.OpenWindowOnStartup
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
        _secStartup.Summary = If(chkAutostart.Checked,
                                 Localization.T("Автозапуск включён"),
                                 Localization.T("Автозапуск выключен"))
        _secStartup.SummaryColor = SystemColors.GrayText
        ' The port is exactly the kind of fact a folded section must still answer: someone
        ' opens this dialog to check which number their router rule has to name.
        _secNetwork.Summary = Localization.TF("до {0} подключений, порт {1}",
                                              CInt(numMaxConns.Value), CInt(numPort.Value))
        _secNetwork.SummaryColor = SystemColors.GrayText
        ' Verbatim from HostingText - the same sentence the group's own first line shows,
        ' so the folded and unfolded states cannot drift apart.
        _secHosting.Summary = HostingText.HostModeLine(ServerFeatures.HostMode())
        _secHosting.SummaryColor = SystemColors.GrayText
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
        End If
        numPort.Value = CDec(shown)
        UpdatePortNote()
    End Sub

    ''' <summary>The port the server is actually on, or 0. A snapshot from when this dialog
    ''' opened - the main window owns the live line.</summary>
    Private Function LivePort() As Integer
        If _status Is Nothing OrElse Not _status.Running Then Return 0
        Return _status.ListenPort
    End Function

    ''' <summary>Either the honest verdict on a port that is not serving, or the re-export
    ''' reminder once the number really moved. Never a reassurance nobody asked for: with
    ''' nothing to say the line disappears.</summary>
    Private Sub UpdatePortNote()
        If lblPortNote Is Nothing Then Return
        Dim warn As String = ShareController.PortWarning(_status)
        If warn <> "" Then
            lblPortNote.Text = warn
            lblPortNote.ForeColor = CollapsibleSection.AttentionColor
        ElseIf LivePort() > 0 AndAlso LivePort() <> CInt(numPort.Value) Then
            lblPortNote.Text = ShareText.PortReexportHint()
            lblPortNote.ForeColor = CollapsibleSection.AttentionColor
        Else
            lblPortNote.Text = ""
            lblPortNote.ForeColor = SystemColors.GrayText
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
    Private Async Sub OnPickFreePort(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim found As Integer = FreePortFinder.FindFree(CInt(numPort.Value))
        If found = ShareSettings.UnsetPort Then Return
        Dim prev As Boolean = _loading
        _loading = True
        numPort.Value = CDec(found)
        _loading = prev
        Try
            SavePortChoice()
        Catch
        End Try
        UpdatePortNote()
        UpdateSummaries()
        _portPushed = True
        Try
            Await ShareController.PushNetworkPolicyAsync()
        Catch
        End Try
    End Sub

    ''' <summary>Persist on every tick (cheap, local); the push waits for Leave, exactly as
    ''' the connection cap does - a spinner passing through 2221 must not restart a running
    ''' server on each intermediate value.</summary>
    Private Sub OnPortValueChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            SavePortChoice()
        Catch
        End Try
        UpdatePortNote()
        UpdateSummaries()
    End Sub

    Private Async Sub OnPortCommit(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            SavePortChoice()
        Catch
        End Try
        _portPushed = True
        Try
            Await ShareController.PushNetworkPolicyAsync()
        Catch
        End Try
        UpdatePortNote()
    End Sub

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
        UpdateSummaries()
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
