Option Strict On

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The "Media Exchange Server" group of the manager settings (ticket 142; the firewall row is 143, the
''' service rows 144). A fifth collapsible group between Hosting and Help, modelled on the Hosting group
''' (Share_Settings_Form.Hosting.vb).
'''
''' It exists ONLY when <c>exchange\fms-exchange-server.exe</c> is installed: without the component the
''' group is not built at all - no section, no persisted anchor, no row - so the Share Manager is exactly
''' what it was. Nothing in it runs without a click: no autostart, no probe at construction (the build pass
''' reads only local files; the live read - one short run of the binary's <c>inspect</c> - happens off the
''' UI thread once the dialog is shown, which is also what keeps the FOLDED summary live).
'''
''' Rules held here, the Hosting rules:
'''   * a control is shown or hidden by state, never disabled (<see cref="ExchangeGroupModel"/> decides);
'''   * a destructive or disruptive action asks YesNo with the default on No;
'''   * every action runs off the UI thread, behind a busy flag, and ends in one result line;
'''   * an elevated action is one UAC prompt through the bundled helper, with the trust-page link under it;
'''   * an account's password is made or typed in a dialog, goes to the server over the admin channel (ticket 168),
'''     and a generated one is shown once in its own dialog and copied only through SecureClipboard; the
'''     fingerprint is not a secret.
'''
''' The administration rows (the wizard, the public address, the third-party ports, the certificate, the accounts and
''' devices, the statement) are the partial Share_Settings_Form.ExchangeAdmin.vb.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _secExchange As CollapsibleSection
    Private _exLoaded As Boolean
    Private _exBusy As Boolean
    Private _exLoadingPort As Boolean
    Private _exBaselinePort As Integer
    Private _exModel As ExchangeGroupModel
    Private _exReport As ExchangeProcess.InspectReport
    Private ReadOnly _exSettings As New ExchangeSettings()

    Private _lblXState As Label
    Private _lblXReach As Label
    Private _lblXWins As Label
    Private _lblXPortNote As Label
    Private _lblXFw As Label
    Private _lblXFpNote As Label
    Private _btnXConnString As Button
    Private _lblXConnNote As Label
    Private _lblXSvcNote As Label
    Private _lblXResult As Label
    Private numXPort As NumericUpDown
    Private _btnXApplyPort As Button
    Private _btnXWizard As Button
    Private _btnXWelcome As Button
    Private _rowXWizard As FlowLayoutPanel
    Private _btnXCopyFp As Button
    Private _btnXStart As Button
    Private _btnXStop As Button
    Private _btnXFwEnable As Button
    Private _btnXFwRemove As Button
    Private _btnXSvcStart As Button
    Private _btnXSvcStop As Button
    Private _btnXRegister As Button
    Private _btnXRemove As Button
    Private _txtXFp As TextBox
    Private _rowXFp As FlowLayoutPanel
    Private _rowXFw As FlowLayoutPanel
    Private _rowXSvc As FlowLayoutPanel
    Private _lnkXTrust As LinkLabel
    Private _chkXKeep As CheckBox

    ''' <summary>Whether this window has the group at all - the component's presence, decided once at
    ''' construction (the file does not appear while a dialog is open).</summary>
    Private ReadOnly Property HasExchange As Boolean
        Get
            Return _secExchange IsNot Nothing
        End Get
    End Property

    ''' <summary>Every group of the window, in display order, with the exchange group only when it exists.
    ''' The ONE list the navigation, persisted page/anchor context and the summaries all
    ''' read - a group added to one of those four places and forgotten in another broke silently (R4 section
    ''' 1); a test holds that none of them keeps a list of its own.</summary>
    Private Function AllSections() As CollapsibleSection()
        Dim list As New List(Of CollapsibleSection) From {_secStartup, _secNetwork, _secHosting}
        If _secExchange IsNot Nothing Then list.Add(_secExchange)
        ' Scheduled file operations (ticket 222): a page of every Share Manager, after hosting and the exchange server
        ' and before Help - it is not behind the exchange component or the Share opt-in.
        If _secScheduled IsNot Nothing Then list.Add(_secScheduled)
        list.Add(_secHelp)
        Return list.ToArray()
    End Function

    ''' <summary>Builds the group when the component is installed. Called by the window's builder between
    ''' the hosting group and the help group; returns Nothing (and builds nothing) otherwise.</summary>
    Private Function CreateExchangeSection() As CollapsibleSection
        If Not ExchangeProcess.IsAvailable() Then Return Nothing
        Dim sec As CollapsibleSection = CreateSettingsSection("exchange", ExchangeText.Title())
        _secExchange = sec   ' before the build: ApplyExchangeState keys off it
        BuildExchangeSection(sec)
        Return sec
    End Function

    Private Function XButton(text As String, onClick As Action, Optional glyph As String = "") As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5), .Margin = New Padding(0, 2, 8, 2), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph)
        Return b
    End Function

    Private Shared Function XFlow() As FlowLayoutPanel
        Return New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = True, .Margin = New Padding(0, 2, 0, 2), .MaximumSize = New Size(HostingContentWidth, 0)}
    End Function

    Private Sub BuildExchangeSection(sec As CollapsibleSection)
        Try
            _exSettings.Load()
        Catch
        End Try

        ' state
        _lblXState = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
            .MaximumSize = New Size(HostingContentWidth, 0), .Font = New Font(Me.Font, FontStyle.Bold)}
        _lblXReach = NoteLabel("")
        _lblXWins = NoteLabel("")
        sec.AddBodyRow(_lblXState)
        sec.AddBodyRow(_lblXReach)
        sec.AddBodyRow(_lblXWins)
        sec.AddBodyRow(NoteLabel(ExchangeText.Intro()))
        ' Only here, inside the group that exists only with the component: the portal page that explains the
        ' server (the program's other surfaces never name it).
        Dim lnkXPortal As New LinkLabel With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .Margin = New Padding(0, 0, 0, 8), .Text = Localization.T("Как устроен сервер обмена (портал)")}
        AddHandler lnkXPortal.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareLinks.ExchangePortalUrl("exchange-server"))
        sec.AddBodyRow(lnkXPortal)
        If ExchangeFirstRunModel.GateOpen() Then
            Dim addResource = XButton(Localization.T("Добавить ресурс для публикации.."), Sub()
                ResourceAddRequested = True
                Close()
            End Sub, "action.add")
            addResource.MinimumSize = New Size(0, 44)
            sec.AddBodyRow(addResource)
            _btnXWelcome = XButton(Localization.T("Приветствие.."), Sub()
                ReplayExchangeRequested = True
                Close()
            End Sub, "app.help")
            _btnXWelcome.Name = "exchangeWelcomeReplay"
            _btnXWelcome.MinimumSize = New Size(0, 44)
            sec.AddBodyRow(_btnXWelcome)
        End If

        ' "use this PC as my server" (ticket 168 section 3.7): the one-person case in one step; drawn only while nothing exists yet
        _btnXWizard = XButton(ExchangeAdminText.WizardButton(), AddressOf OnXWizard, "action.adjust")
        _rowXWizard = XFlow()
        _rowXWizard.Controls.Add(_btnXWizard)
        _rowXWizard.Visible = False
        sec.AddBodyRow(_rowXWizard)

        ' start / stop - the console server
        _btnXStart = XButton(ExchangeText.StartButton(), AddressOf OnXStart, "action.run")
        _btnXStop = XButton(ExchangeText.StopButton(), AddressOf OnXStop, "media.stop")
        Dim runRow As FlowLayoutPanel = XFlow()
        runRow.Controls.Add(_btnXStart)
        runRow.Controls.Add(_btnXStop)
        sec.AddBodyRow(runRow)
        _chkXKeep = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = ExchangeText.KeepRunningCheckbox(), .Checked = _exSettings.KeepRunningOnExit}
        AddHandler _chkXKeep.CheckedChanged, AddressOf OnXKeepRunningChanged
        sec.AddBodyRow(_chkXKeep)

        ' the listen port
        Dim lblPort As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0), .Text = Localization.T("Порт:")}
        numXPort = New NumericUpDown With {.Minimum = ExchangeSettings.MinPort, .Maximum = ExchangeSettings.MaxPort,
            .Value = ExchangeSettings.DefaultPort, .Width = 84, .Margin = New Padding(0, 2, 0, 0)}
        _exBaselinePort = CInt(numXPort.Value)   ' the field starts at the number it shows: nothing is pending until the person edits it
        _btnXApplyPort = XButton(Localization.T("Применить порт"), AddressOf OnXApplyPort, "action.confirm")
        _btnXApplyPort.Visible = False
        AddHandler numXPort.ValueChanged, AddressOf OnXPortValueChanged
        Dim portSetting = DirectCast(SettingsEditorRow(lblPort, numXPort, New Padding(0, 6, 0, 0)), ManagerSettingRow)
        portSetting.SetHint(ExchangeText.PortNote())
        _lblXPortNote = portSetting.Hint
        sec.AddBodyRow(portSetting)
        Dim portRow As FlowLayoutPanel = XFlow()
        portRow.Controls.Add(_btnXApplyPort)
        sec.AddBodyRow(portRow)

        ' ticket 168: the public address, the third-party ports and the certificate with its fingerprint (not a secret)
        BuildExchangeAdminRows(sec)

        ' ticket 168: the accounts, their devices and the statement of what the operator can see
        BuildExchangeAccountRows(sec)

        ' the firewall (ticket 143)
        _lblXFw = New Label With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 2), .MaximumSize = New Size(HostingContentWidth, 0)}
        sec.AddBodyRow(_lblXFw)
        _btnXFwEnable = XButton(ExchangeText.FirewallEnableButton(), AddressOf OnXFirewallEnable, "action.share")
        _btnXFwRemove = XButton(ExchangeText.FirewallRemoveButton(), AddressOf OnXFirewallRemove, "action.remove")
        _rowXFw = XFlow()
        _rowXFw.Controls.Add(_btnXFwEnable)
        _rowXFw.Controls.Add(_btnXFwRemove)
        sec.AddBodyRow(_rowXFw)
        sec.AddBodyRow(NoteLabel(ExchangeText.FirewallNote()))

        ' the service (ticket 144) - rows exist only where they can act
        _btnXSvcStart = XButton(Localization.T("Запустить службу"), Sub() OnXServiceControl(True), "action.run")
        _btnXSvcStop = XButton(Localization.T("Остановить службу"), Sub() OnXServiceControl(False), "media.stop")
        _btnXRegister = XButton(ExchangeText.RegisterButton(), AddressOf OnXRegister, "action.install")
        _btnXRemove = XButton(ExchangeText.RemoveServiceButton(), AddressOf OnXRemoveService, "action.delete")
        _rowXSvc = XFlow()
        _rowXSvc.Controls.Add(_btnXSvcStart)
        _rowXSvc.Controls.Add(_btnXSvcStop)
        _rowXSvc.Controls.Add(_btnXRegister)
        _rowXSvc.Controls.Add(_btnXRemove)
        sec.AddBodyRow(_rowXSvc)
        _lblXSvcNote = NoteLabel("")
        sec.AddBodyRow(_lblXSvcNote)

        ' Every button here that raises a UAC prompt is answered on one page (INSTALL-TRUST rule 7).
        _lnkXTrust = New LinkLabel With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .Margin = New Padding(0, 0, 0, 10), .Text = ShareText.TrustLinkText()}
        AddHandler _lnkXTrust.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareText.TrustPageUrl())
        sec.AddBodyRow(_lnkXTrust)


        _lblXResult = New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 0),
            .MaximumSize = New Size(HostingContentWidth, 0), .Text = ""}
        sec.AddBodyRow(_lblXResult)

        AddHandler ExchangeProcess.ConsoleExited, AddressOf OnXConsoleExited
        ' Probe-free: the layout is decided from local files only; the live read follows the first show.
        ApplyExchangeState(ExchangeSnapshot.ReadCheap())
    End Sub

    ' --- refresh ------------------------------------------------------------------------

    ''' <summary>The live read, once the dialog is on screen: off the UI thread, then the repaint. Called from
    ''' OnShown whether or not the group is unfolded (a FOLDED summary must still be true) and again on the
    ''' first expand (a no-op by then).</summary>
    Private Async Sub EnsureExchangeLoaded()
        If Not HasExchange OrElse _exLoaded Then Return
        _exLoaded = True
        Try
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: ensure loaded", ex)
        End Try
    End Sub

    Private Async Function RefreshExchangeAsync() As Task
        If Not HasExchange Then Return
        Dim snap As ExchangeSnapshot = Await Task.Run(Function() ExchangeSnapshot.Read())
        If IsDisposed OrElse Disposing Then Return
        Await InvokeAsync(Sub()
                              If IsDisposed OrElse Disposing Then Return
                              ApplyExchangeState(snap)
                              UpdateSummaries()
                          End Sub)
    End Function

    Friend Sub FocusPublishPage()
        SelectSettingsPage("network")
    End Sub

    Friend Sub FocusHostingPage()
        SelectSettingsPage("hosting")
    End Sub

    Friend Sub FocusExchangePage()
        If HasExchange Then SelectSettingsPage("exchange")
    End Sub

    ''' <summary>The server this window started (or one found) ended by itself or was stopped: refresh, on
    ''' the UI thread. Raised on a pool thread.</summary>
    Private Sub OnXConsoleExited(sender As Object, e As EventArgs)
        Try
            If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(Sub()
                                       If IsDisposed OrElse Disposing OrElse _exBusy Then Return
                                       Dim t As Task = RefreshExchangeAsync()
                                   End Sub))
        Catch
        End Try
    End Sub

    ''' <summary>Paints the group from one snapshot. Which controls EXIST is decided here by visibility, never
    ''' by disabling, from the pure <see cref="ExchangeGroupModel"/>.</summary>
    Private Sub ApplyExchangeState(snap As ExchangeSnapshot)
        Using layout = HoldSettingsPage("exchange")
            _exchangeSnapshot = snap
            If Not HasExchange Then Return
            _exReport = snap.Report
            Dim m As ExchangeGroupModel = ExchangeGroupModel.Build(snap)
            _exModel = m

            _lblXState.Text = ExchangeText.StateLine(m)
            _lblXState.ForeColor = If(m.Kind = ExchangeRunKind.RunningConsole OrElse m.Kind = ExchangeRunKind.ServiceRunning,
                                      ThemeService.Palette.Success,
                                      If(m.Kind = ExchangeRunKind.NotConfigured OrElse m.Kind = ExchangeRunKind.Unreadable,
                                         CollapsibleSection.AttentionColor, ThemeService.Palette.TextPrimary))
            _lblXReach.Text = If(m.ShowReachabilityNote, ExchangeText.ReachabilityNote(), "")
            _lblXReach.Visible = m.ShowReachabilityNote
            _lblXWins.Text = If(m.ShowServiceWinsNote, ExchangeText.ServiceWinsNote(), "")
            _lblXWins.Visible = m.ShowServiceWinsNote

            ' The port: the binary's own number, unless the person is mid-edit.
            Dim shown As Integer = If(m.Port >= ExchangeSettings.MinPort, m.Port,
                                      If(_exSettings.ListenPort >= ExchangeSettings.MinPort, _exSettings.ListenPort, ExchangeSettings.DefaultPort))
            If Not PortPendingX() Then
                _exLoadingPort = True
                Try
                    numXPort.Value = Math.Max(numXPort.Minimum, Math.Min(numXPort.Maximum, shown))
                Finally
                    _exLoadingPort = False
                End Try
                _exBaselinePort = CInt(numXPort.Value)
            End If
            UpdateXPortApply()

            _rowXFp.Visible = m.ShowFingerprint
            _txtXFp.Text = m.Fingerprint
            _lblXFpNote.Text = If(m.ShowFingerprint, ExchangeText.FingerprintNote(), ExchangeText.FingerprintMissing())
            _btnXConnString.Enabled = Not _exBusy AndAlso ConnectionStringAvailable()
            _lblXConnNote.Visible = m.ShowFingerprint AndAlso m.PublicEndpoint.Length = 0

            _btnXStart.Visible = m.ShowStart
            _btnXStop.Visible = m.ShowStop
            _chkXKeep.Visible = m.ShowKeepRunning

            _lblXFw.Text = ExchangeText.FirewallState(snap.Firewall)
            _btnXFwEnable.Visible = m.ShowFirewallEnable
            _btnXFwRemove.Visible = m.ShowFirewallRemove
            _rowXFw.Visible = m.ShowFirewallEnable OrElse m.ShowFirewallRemove

            _btnXSvcStart.Visible = m.ShowServiceStart
            _btnXSvcStop.Visible = m.ShowServiceStop
            _btnXRegister.Visible = m.ShowRegisterService
            _btnXRemove.Visible = m.ShowRemoveService
            _rowXSvc.Visible = m.ShowServiceRows
            _lblXSvcNote.Text = If(m.ShowServiceRows, ExchangeText.ServiceNote(snap.ServiceExists), "")
            _lblXSvcNote.Visible = m.ShowServiceRows
            _lnkXTrust.Visible = m.ShowFirewallEnable OrElse m.ShowFirewallRemove OrElse m.ShowRegisterService OrElse m.ShowRemoveService

            ApplyExchangeAdminState(snap, m)
        End Using
    End Sub

    ' --- the port -----------------------------------------------------------------------

    Private Function PortPendingX() As Boolean
        Return numXPort IsNot Nothing AndAlso CInt(numXPort.Value) <> _exBaselinePort
    End Function

    Private Sub UpdateXPortApply()
        If _btnXApplyPort Is Nothing Then Return
        _btnXApplyPort.Visible = PortPendingX()
    End Sub

    Private Sub OnXPortValueChanged(sender As Object, e As EventArgs)
        If _exLoadingPort Then Return
        UpdateXPortApply()
        _lblXPortNote.Text = If(PortPendingX(), ExchangeText.PortPending(CInt(numXPort.Value)), ExchangeText.PortNote())
    End Sub

    Private Async Sub OnXApplyPort()
        If _exBusy OrElse Not PortPendingX() Then Return
        Dim newPort As Integer = CInt(numXPort.Value)
        Dim consoleRunning As Boolean = _exModel IsNot Nothing AndAlso _exModel.Kind = ExchangeRunKind.RunningConsole
        Dim serviceExists As Boolean = _exModel IsNot Nothing AndAlso
            (_exModel.Kind = ExchangeRunKind.ServiceRunning OrElse _exModel.Kind = ExchangeRunKind.ServiceStarting OrElse
             _exModel.Kind = ExchangeRunKind.ServiceStopped OrElse _exModel.Kind = ExchangeRunKind.ServiceStopping)
        If consoleRunning OrElse serviceExists Then
            If MessageBox.Show(Me, ExchangeText.PortQuestion(newPort, consoleRunning), Me.Text, MessageBoxButtons.YesNo,
                               MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        End If
        Dim listen As String = ExchangeProcess.WithPort(If(_exReport IsNot Nothing, _exReport.Listen, ""), newPort)
        SetXBusy(True)
        Try
            Dim outcome As ExchangeProcess.ListenOutcome = Await Task.Run(
                Function()
                    Return ExchangeProcess.SetListen(listen)
                End Function)
            If IsDisposed OrElse Disposing Then Return
            Dim restarted As Boolean = False
            If outcome = ExchangeProcess.ListenOutcome.Saved Then
                _exSettings.ListenPort = newPort
                Try : _exSettings.Save() : Catch : End Try
                _exBaselinePort = newPort
                If consoleRunning Then
                    Await Task.Run(Sub() ExchangeProcess.StopServer())
                    Dim started As ExchangeProcess.StartOutcome = Await Task.Run(Function() ExchangeProcess.Start())
                    restarted = started = ExchangeProcess.StartOutcome.Started
                End If
            End If
            ShowXResult(ExchangeText.PortResult(outcome, restarted), outcome = ExchangeProcess.ListenOutcome.Saved)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: applying the port", ex)
            If Not (IsDisposed OrElse Disposing) Then ShowXResult(ExchangeText.PortResult(ExchangeProcess.ListenOutcome.Failed, False), False)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    ' --- fingerprint ----------------------------------------------------------------------

    Private Sub OnXCopyFingerprint()
        Dim fp As String = _txtXFp.Text
        If fp.Length = 0 Then Return
        ' Not a secret: a plain copy, still through the one clipboard door (no privacy markers needed).
        Dim data As New DataObject()
        data.SetText(fp, TextDataFormat.UnicodeText)
        If SecureClipboard.TrySetDataObject(data) Then
            ShowXResult(ExchangeText.FingerprintCopied(), True)
        Else
            ShowXResult(ExchangeText.CopyFailed(), False)
        End If
    End Sub

    ' --- connection string (ticket 187 T-2) --------------------------------------------------

    ''' <summary>The string can be made: the server has a certificate to name and the address for clients is set. It is never
    ''' built from a bind address, so with the address empty the button is disabled and the hint says why.</summary>
    Private Function ConnectionStringAvailable() As Boolean
        Return _exModel IsNot Nothing AndAlso _exModel.ShowFingerprint AndAlso _exModel.PublicEndpoint.Length > 0
    End Function

    Private Sub OnXGetConnectionString()
        If _exBusy OrElse Not ConnectionStringAvailable() Then Return
        Using dlg As New Exchange_Connection_String_Form()
            dlg.ShowDialog(Me)
        End Using
    End Sub

    ' --- start / stop -----------------------------------------------------------------------

    Private Async Sub OnXStart()
        If _exBusy Then Return
        SetXBusy(True)
        Try
            Dim outcome As ExchangeProcess.StartOutcome = Await Task.Run(Function() ExchangeProcess.Start())
            If IsDisposed OrElse Disposing Then Return
            If outcome = ExchangeProcess.StartOutcome.Started Then
                _exSettings.EverStarted = True
                Try : _exSettings.Save() : Catch : End Try
            End If
            Dim port As Integer = If(_exModel IsNot Nothing, _exModel.ShownPort(), ExchangeSettings.DefaultPort)
            ShowXResult(ExchangeText.StartResult(outcome, port), outcome = ExchangeProcess.StartOutcome.Started OrElse outcome = ExchangeProcess.StartOutcome.AlreadyRunning)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: start", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXStop()
        If _exBusy Then Return
        SetXBusy(True)
        Try
            Dim outcome As ExchangeProcess.StopOutcome = Await Task.Run(Function() ExchangeProcess.StopServer())
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(ExchangeText.StopResult(outcome), outcome = ExchangeProcess.StopOutcome.Stopped OrElse outcome = ExchangeProcess.StopOutcome.NotRunning)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: stop", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Sub OnXKeepRunningChanged(sender As Object, e As EventArgs)
        _exSettings.KeepRunningOnExit = _chkXKeep.Checked
        Try : _exSettings.Save() : Catch : End Try
    End Sub

    ' --- firewall (143) --------------------------------------------------------------------

    ''' <summary>The ONLY place the firewall helper is launched from the window: the button handlers below.</summary>
    Private Async Sub OnXFirewallEnable()
        Await RunXElevated(Function() ExchangeFirewall.EnableViaElevation(), withdrew:=False)
    End Sub

    Private Async Sub OnXFirewallRemove()
        If MessageBox.Show(Me, ExchangeText.FirewallRemoveQuestion(), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await RunXElevated(Function() ExchangeFirewall.WithdrawViaElevation(), withdrew:=True)
    End Sub

    Private Async Function RunXElevated(action As Func(Of ExchangeElevation.ElevatedOutcome), withdrew As Boolean) As Task
        If _exBusy Then Return
        SetXBusy(True)
        ShowXResult(HostingText.ManageWorking(), True, neutral:=True)
        Try
            ' Off the UI thread: the helper blocks on its UAC prompt (ticket 054 H-15).
            Dim outcome As ExchangeElevation.ElevatedOutcome = Await Task.Run(action)
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(ExchangeText.FirewallResult(outcome, withdrew), outcome = ExchangeElevation.ElevatedOutcome.Succeeded)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: firewall", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Function

    ' --- service (144) ---------------------------------------------------------------------

    Private Async Sub OnXServiceControl(start As Boolean)
        If _exBusy Then Return
        SetXBusy(True)
        Try
            Dim r As ServiceControl.ManageResult = Await Task.Run(Function() ExchangeService.StartOrStop(start))
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(ExchangeText.ServiceResult(r), r = ServiceControl.ManageResult.Succeeded)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: service control", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXRegister()
        If _exBusy Then Return
        If MessageBox.Show(Me, ExchangeText.RegisterQuestion(), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await RunXManage(ExchangeService.RegisterVerb())
    End Sub

    Private Async Sub OnXRemoveService()
        If _exBusy Then Return
        If MessageBox.Show(Me, ExchangeText.RemoveServiceQuestion(), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await RunXManage(ExchangeService.Verb.Remove)
    End Sub

    Private Async Function RunXManage(verb As ExchangeService.Verb) As Task
        SetXBusy(True)
        ShowXResult(HostingText.ManageWorking(), True, neutral:=True)
        Try
            Dim r As ServiceControl.ManageResult = Await Task.Run(Function() ExchangeService.Manage(verb))
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(ExchangeText.ServiceResult(r), r = ServiceControl.ManageResult.Succeeded)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: service helper", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Function

    ' --- plumbing --------------------------------------------------------------------------

    Private Sub ShowXResult(text As String, ok As Boolean, Optional neutral As Boolean = False)
        _lblXResult.ForeColor = If(neutral, ThemeService.Palette.TextMuted, If(ok, ThemeService.Palette.Success, CollapsibleSection.AttentionColor))
        _lblXResult.Text = text
    End Sub

    ''' <summary>One action at a time: the buttons that start a second one are hidden-in-effect by the wait
    ''' state (disabled only while an action is out - the Hosting precedent), and the cursor says so.</summary>
    Private Sub SetXBusy(value As Boolean)
        _exBusy = value
        For Each b As Button In ExchangeBusyButtons()
            If b IsNot Nothing Then b.Enabled = Not value
        Next
        If _btnXConnString IsNot Nothing Then _btnXConnString.Enabled = Not value AndAlso ConnectionStringAvailable()
        Me.UseWaitCursor = value
    End Sub

    ''' <summary>Window teardown for the group: the bold font and the process event.</summary>
    Private Sub DisposeExchangeResources()
        Try : RemoveHandler ExchangeProcess.ConsoleExited, AddressOf OnXConsoleExited : Catch : End Try
        Try : _lblXState?.Font?.Dispose() : Catch : End Try
        DisposeExchangeAdminResources()
    End Sub

    ''' <summary>The folded summary: the same facts the body states.</summary>
    Private Sub UpdateExchangeSummary()
        If Not HasExchange Then Return
        If _exModel Is Nothing Then Return
        _secExchange.Summary = _exModel.SummaryText()
        _secExchange.SummaryColor = If(_exModel.Kind = ExchangeRunKind.NotConfigured OrElse _exModel.Kind = ExchangeRunKind.Unreadable,
                                       CollapsibleSection.AttentionColor, ThemeService.Palette.TextMuted)
    End Sub

End Class
