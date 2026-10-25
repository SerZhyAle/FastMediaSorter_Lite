Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The "Exchange account" group of the Share settings window (ticket 180 section 7, T-4): the account and server, one state line, "Add a device"
''' (a one-time pairing code with its countdown, copied under CLIPBOARD-GUARD rules), the account's devices with presence and last seen, this device
''' marked, "Revoke" (a confirmation naming the consequence; revoking THIS device is leaving the account), and "Disconnect" (forget the token and the
''' account entry, keep nothing).
'''
''' This is the CLIENT side of the device exchange. It is a separate group from the server-administration one (Share_Settings_Form.Exchange.vb, ticket
''' 168) and lives in its own partial, so neither file owns the other.
'''
''' Rules held here:
'''   * Gated: the group is built only while <see cref="ExchangeFeatures.IsEnabled"/> says so (the build's gate, or the developer switch); otherwise there
'''     is no group at all and the window is what it was (dormant rule, 177).
'''   * Hidden rows, not disabled ones: <see cref="ExchangeAccountGroupModel"/> decides which rows exist in a state.
'''   * Nothing runs at construction. The agent is only READ here (ExchangeDeviceHost.Current); it is created when someone signs in.
'''   * A pairing code is a secret until used: it lives in this window's memory only, is never logged, goes to the clipboard through SecureClipboard
'''     only, and is dropped when it expires, when the stream is not online any more, or when the window closes.
'''   * A confirmation whose answer destroys something has the safe answer as its default.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _secAccount As CollapsibleSection
    Private _accState As Label
    Private _accDetail As Label
    Private _accMoved As Label
    Private _accMovedRow As FlowLayoutPanel
    Private _btnAccUpdate As Button
    Private _accResult As Label
    Private _accSignInRow As FlowLayoutPanel
    Private _btnAccSignIn As Button
    Private _btnAccPublish As Button
    Private _btnAccAccept As Button
    Private _btnAccJoinLocal As Button
    Private _btnAccAdd As Button
    Private _accPairing As FlowLayoutPanel
    Private _accCode As Label
    Private _accValid As Label
    Private _btnAccCopy As Button
    Private _accPairNote As Label
    Private _accDevicesTitle As Label
    Private _accDevices As ListView
    Private _btnAccRevoke As Button
    ''' <summary>226 rule 4: change the account's password from this device's own control stream.</summary>
    Private _btnAccChangePassword As Button
    Private _btnAccDisconnect As Button
    ''' <summary>226 rule 7: link to the portal page 'how to delete an account' (FMS_A S4243).</summary>
    Private _lnkAccDelete As LinkLabel
    Private _accTimer As System.Windows.Forms.Timer
    Private _accBoldFont As Font
    Private _accAgent As ExchangeDeviceAgent
    Private _accBusy As Boolean
    ''' <summary>The pairing code on screen, while it is valid. Memory only.</summary>
    Private _accCodeText As String = ""
    Private _accCodeExpires As DateTimeOffset

    Private Const AccountContentWidth As Integer = 500

    ''' <summary>The window's sections in order: the existing ones, with this group before Help (and after the server-administration one).</summary>
    Private Function AllSettingsSections() As CollapsibleSection()
        Dim list As New List(Of CollapsibleSection)(AllSections())
        If _secAccount IsNot Nothing Then
            Dim at As Integer = If(_secHelp IsNot Nothing, list.IndexOf(_secHelp), -1)
            If at < 0 Then list.Add(_secAccount) Else list.Insert(at, _secAccount)
        End If
        ' The Google Drive page (Share_Settings_Form.Drive.vb) follows the account page, before Help.
        If _secDrive IsNot Nothing Then
            Dim driveAt As Integer = If(_secHelp IsNot Nothing, list.IndexOf(_secHelp), -1)
            If driveAt < 0 Then list.Add(_secDrive) Else list.Insert(driveAt, _secDrive)
        End If
        Return list.ToArray()
    End Function

    ''' <summary>Nothing when the gate is shut: no group, no change to the window.</summary>
    Private Function CreateAccountSection() As CollapsibleSection
        If Not ExchangeFeatures.IsEnabled() Then Return Nothing
        Dim sec As CollapsibleSection = CreateSettingsSection("exchange-account", ExchangeDeviceText.GroupTitle())
        _secAccount = sec
        BuildAccountSection(sec)
        Return sec
    End Function

    Private Shared Function AccNote(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(AccountContentWidth, 0), .Margin = New Padding(0, 2, 0, 4),
            .ForeColor = ThemeService.Palette.TextMuted, .Text = text}
    End Function

    Private Shared Function AccFlow() As FlowLayoutPanel
        Return New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True,
            .Margin = New Padding(0, 2, 0, 2), .MaximumSize = New Size(AccountContentWidth, 0)}
    End Function

    Private Function AccButton(text As String, onClick As Action, Optional glyph As String = "") As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 2, 8, 2), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph)
        Return b
    End Function

    Private Sub BuildAccountSection(sec As CollapsibleSection)
        _accBoldFont = New Font(Me.Font, FontStyle.Bold)
        _accState = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .MaximumSize = New Size(AccountContentWidth, 0), .Font = _accBoldFont}
        _accDetail = AccNote("")
        sec.AddBodyRow(_accState)
        sec.AddBodyRow(_accDetail)

        ' a welcome that names another address (the entries proposal's ask 2): the announced address is shown and the edit offered, never taken silently
        _accMoved = AccNote("")
        _accMoved.Visible = False
        sec.AddBodyRow(_accMoved)
        _btnAccUpdate = AccButton(Localization.T("Обновить адрес"), AddressOf OnAccountUpdateAddress, "action.refresh")
        _accMovedRow = AccFlow()
        _accMovedRow.Controls.Add(_btnAccUpdate)
        _accMovedRow.Visible = False
        sec.AddBodyRow(_accMovedRow)

        _accSignInRow = AccFlow()
        _btnAccSignIn = AccButton(Localization.T("Войти.."), AddressOf OnAccountSignIn, "action.confirm")
        _btnAccAccept = AccButton(Localization.T("Принять новый сертификат.."), AddressOf OnAccountAcceptCertificate, "action.confirm")
        _btnAccJoinLocal = AccButton(ExchangeJoinText.JoinLocalButton(), AddressOf OnAccountJoinLocal, "action.confirm")
        _accSignInRow.Controls.Add(_btnAccSignIn)
        ' ticket 187 T-5: the same dialog, opened to publish - its "publish now" box is ticked
        _btnAccPublish = AccButton(ExchangeJoinText.PublishButton(), AddressOf OnAccountPublish, "action.confirm")
        _accSignInRow.Controls.Add(_btnAccPublish)
        _accSignInRow.Controls.Add(_btnAccJoinLocal)
        _accSignInRow.Controls.Add(_btnAccAccept)
        sec.AddBodyRow(_accSignInRow)

        ' pairing: the button, and (while a code is valid) the code, its countdown, the copy button and the one-line how-to
        _btnAccAdd = AccButton(Localization.T("Добавить устройство"), AddressOf OnAccountAddDevice, IconMap.GetMeaning("_btnAccAdd").GlyphId)
        Dim addRow As FlowLayoutPanel = AccFlow()
        addRow.Controls.Add(_btnAccAdd)
        addRow.Controls.Add(AccButton(Localization.T("Гостевой доступ.."), Sub()
            Dim agent = ExchangeDeviceHost.Current
            If agent Is Nothing Then Return
            Using dialog As New Exchange_Guest_Grant_Form(agent)
                dialog.ShowDialog(Me)
            End Using
        End Sub, "action.add"))
        sec.AddBodyRow(addRow)
        _accPairing = AccFlow()
        _accPairing.FlowDirection = FlowDirection.TopDown
        _accPairing.WrapContents = False
        _accCode = New Label With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 0), .Font = _accBoldFont}
        _accValid = AccNote("")
        _btnAccCopy = AccButton(Localization.T("Копировать код"), AddressOf OnAccountCopyCode, "action.copy")
        _accPairNote = AccNote(Localization.T("Введите этот код на новом устройстве вместо пароля. Он одноразовый."))
        _accPairing.Controls.Add(_accCode)
        _accPairing.Controls.Add(_accValid)
        _accPairing.Controls.Add(_btnAccCopy)
        _accPairing.Controls.Add(_accPairNote)
        _accPairing.Visible = False
        sec.AddBodyRow(_accPairing)

        ' the account's devices
        _accDevicesTitle = New Label With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 2), .Font = _accBoldFont, .Text = Localization.T("Мои устройства")}
        sec.AddBodyRow(_accDevicesTitle)
        ' The control's own size is in 96-dpi design units (DpiLayout.ApplyAutoScale scales it as the last line of the builder); the COLUMN widths are
        ' not scaled by that, so they are device units (LogicalToDeviceUnits) - the Companion DPI recipe.
        _accDevices = New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .Width = AccountContentWidth, .Height = 130, .Margin = New Padding(0, 2, 0, 2)}
        _accDevices.Columns.Add(Localization.T("Устройство"), Me.LogicalToDeviceUnits(210))
        _accDevices.Columns.Add(Localization.T("Состояние"), Me.LogicalToDeviceUnits(90))
        _accDevices.Columns.Add(Localization.T("Последний раз в сети"), Me.LogicalToDeviceUnits(170))
        AddHandler _accDevices.SelectedIndexChanged, Sub() UpdateAccountRevokeVisibility()
        sec.AddBodyRow(_accDevices)
        _btnAccRevoke = AccButton(Localization.T("Отозвать"), AddressOf OnAccountRevoke, "action.remove")
        Dim revokeRow As FlowLayoutPanel = AccFlow()
        revokeRow.Controls.Add(_btnAccRevoke)
        sec.AddBodyRow(revokeRow)

        _btnAccChangePassword = AccButton(Localization.T("Сменить пароль.."), AddressOf OnAccountChangePassword, "action.edit")
        Dim changePasswordRow As FlowLayoutPanel = AccFlow()
        changePasswordRow.Controls.Add(_btnAccChangePassword)
        sec.AddBodyRow(changePasswordRow)

        ' the owner's М10 word (226 rule 5): the sign-out of THIS device - the entry and the key are forgotten here, the device record stays on the server
        _btnAccDisconnect = AccButton(Localization.T("Выйти на этом устройстве"), AddressOf OnAccountDisconnect, "system.network-off")
        _lnkAccDelete = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2),
            .LinkColor = ThemeService.Palette.Link, .ActiveLinkColor = ThemeService.Palette.Link, .VisitedLinkColor = ThemeService.Palette.Link,
            .Text = Localization.T("Как удалить учётную запись (портал)")}
        AddHandler _lnkAccDelete.LinkClicked, AddressOf OnAccountDeleteLinkClicked
        Dim disconnectRow As FlowLayoutPanel = AccFlow()
        disconnectRow.Controls.Add(_btnAccDisconnect)
        disconnectRow.Controls.Add(_lnkAccDelete)
        sec.AddBodyRow(disconnectRow)

        _accResult = AccNote("")
        _accResult.ForeColor = CollapsibleSection.AttentionColor
        _accResult.Visible = False
        sec.AddBodyRow(_accResult)

        ' Spec 230: the clipboard through the server - the shortcuts command and the received-files folder (own partial).
        BuildClipboardRows(sec)

        _accTimer = New System.Windows.Forms.Timer With {.Interval = 1000}
        AddHandler _accTimer.Tick, AddressOf OnAccountTimerTick

        ApplyAccountState()
    End Sub

    ''' <summary>Called when the window closes: the timer, the subscription, the font, and the code.</summary>
    Private Sub DisposeAccountResources()
        Try
            If _accTimer IsNot Nothing Then
                _accTimer.Stop()
                _accTimer.Dispose()
            End If
        Catch
        End Try
        UnsubscribeAccountAgent()
        _accCodeText = ""
        Try : _accBoldFont?.Dispose() : Catch : End Try
    End Sub

    ' --- reading the agent ---

    Private Sub SubscribeAccountAgent(agent As ExchangeDeviceAgent)
        If Object.ReferenceEquals(agent, _accAgent) Then Return
        UnsubscribeAccountAgent()
        _accAgent = agent
        If agent IsNot Nothing Then
            AddHandler agent.StatusChanged, AddressOf OnAccountAgentChanged
            AddHandler agent.DirectoryChanged, AddressOf OnAccountAgentChanged
        End If
    End Sub

    Private Sub UnsubscribeAccountAgent()
        If _accAgent IsNot Nothing Then
            RemoveHandler _accAgent.StatusChanged, AddressOf OnAccountAgentChanged
            RemoveHandler _accAgent.DirectoryChanged, AddressOf OnAccountAgentChanged
        End If
        _accAgent = Nothing
    End Sub

    ''' <summary>The agent's events arrive on its own threads: the window is only touched from its own.</summary>
    Private Sub OnAccountAgentChanged(sender As Object, e As EventArgs)
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(AddressOf ApplyAccountState))
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
            ' the window is closing
        End Try
    End Sub

    ''' <summary>Reads the agent (never creates one) and redraws the group from the model.</summary>
    Private Sub ApplyAccountState()
        If _secAccount Is Nothing OrElse IsDisposed Then Return
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        SubscribeAccountAgent(agent)
        Dim status As ExchangeDeviceStatus = If(agent Is Nothing, Nothing, agent.GetStatus())
        ' the group exists only because the gate was open when the window was built (CreateAccountSection), so here it is open by construction
        Dim model As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(status, True)

        _accState.Text = ExchangeDeviceText.StateLine(status)
        Dim detail As String = ExchangeDeviceText.StateDetail(status)
        _accDetail.Text = detail
        _accDetail.Visible = detail.Length > 0

        ' ask 2 of the entries proposal: the announced address is shown and the edit offered - never a silent switch
        Dim moved As String = If(status Is Nothing OrElse Not status.Configured, "", ExchangeEntryMove.Announced(status.Server, status.PublicEndpoint))
        ' a server on this very PC is reached by loopback whatever address it announces to other devices: nothing to update
        If moved.Length > 0 AndAlso ExchangeLocalJoin.IsLocalServer(status.Server) Then moved = ""
        _accMoved.Text = If(moved.Length = 0, "", ExchangeDeviceText.MovedAddress(moved))
        _accMoved.Visible = moved.Length > 0
        _accMovedRow.Visible = moved.Length > 0

        _btnAccSignIn.Visible = model.ShowSignIn
        _btnAccPublish.Visible = model.ShowSignIn AndAlso Not (status IsNot Nothing AndAlso status.Configured)
        _btnAccAccept.Visible = model.ShowAcceptCertificate
        Dim canLocalJoin As Boolean = model.ShowSignIn AndAlso Not (status IsNot Nothing AndAlso status.Configured) AndAlso ExchangeFeatures.ComponentInstalled()
        If _btnAccJoinLocal IsNot Nothing Then _btnAccJoinLocal.Visible = canLocalJoin
        _accSignInRow.Visible = model.ShowSignIn OrElse model.ShowAcceptCertificate OrElse canLocalJoin
        _btnAccAdd.Parent.Visible = model.ShowAddDevice
        _btnAccAdd.Visible = model.ShowAddDevice
        If Not model.ShowAddDevice Then ClearPairingCode()
        _accDevicesTitle.Visible = model.ShowDevices
        _accDevices.Visible = model.ShowDevices
        _btnAccRevoke.Parent.Visible = model.ShowRevoke
        _btnAccChangePassword.Parent.Visible = model.ShowChangePassword
        _btnAccChangePassword.Visible = model.ShowChangePassword
        _btnAccDisconnect.Parent.Visible = model.ShowDisconnect
        _btnAccDisconnect.Visible = model.ShowDisconnect

        If model.ShowDevices AndAlso agent IsNot Nothing Then FillAccountDevices(agent, status)
        UpdateAccountRevokeVisibility()
        ApplyClipboardRows()

        _secAccount.Summary = ExchangeDeviceText.StateLine(status)
        _secAccount.SummaryColor = If(status IsNot Nothing AndAlso status.Configured AndAlso status.State <> ExchangeAgentState.Online AndAlso status.State <> ExchangeAgentState.Connecting,
                                      CollapsibleSection.AttentionColor, ThemeService.Palette.TextMuted)
        If _built Then _root.PerformLayout()
    End Sub

    ''' <summary>The folded group's one line; the same text the body's first line shows.</summary>
    Private Sub UpdateAccountSummary()
        If _secAccount Is Nothing Then Return
        ApplyAccountState()
    End Sub

    Private Sub FillAccountDevices(agent As ExchangeDeviceAgent, status As ExchangeDeviceStatus)
        Dim selected As String = SelectedAccountDeviceId()
        Dim records As List(Of ExchangeDeviceRecord) = agent.GetFullDirectory().Devices.ToList()
        records.Sort(Function(a, b)
                         Dim ra As Integer = If(String.Equals(a.DeviceId, status.DeviceId, StringComparison.Ordinal), 0, If(a.IsOnline, 1, 2))
                         Dim rb As Integer = If(String.Equals(b.DeviceId, status.DeviceId, StringComparison.Ordinal), 0, If(b.IsOnline, 1, 2))
                         If ra <> rb Then Return ra.CompareTo(rb)
                         Return String.Compare(a.DeviceName, b.DeviceName, StringComparison.CurrentCultureIgnoreCase)
                     End Function)
        _accDevices.BeginUpdate()
        Try
            _accDevices.Items.Clear()
            For Each d As ExchangeDeviceRecord In records
                Dim isMe As Boolean = String.Equals(d.DeviceId, status.DeviceId, StringComparison.Ordinal)
                Dim name As String = If(d.DeviceName.Length > 0, d.DeviceName, d.Product)
                Dim item As New ListViewItem(ExchangeDeviceText.DeviceName(name, isMe)) With {.Tag = d.DeviceId}
                item.SubItems.Add(ExchangeDeviceText.PresenceText(d.IsOnline))
                item.SubItems.Add(FormatLastSeen(d.LastSeen))
                _accDevices.Items.Add(item)
                If String.Equals(d.DeviceId, selected, StringComparison.Ordinal) Then item.Selected = True
            Next
        Finally
            _accDevices.EndUpdate()
        End Try
    End Sub

    Private Shared Function FormatLastSeen(iso As String) As String
        Dim t As DateTimeOffset
        If String.IsNullOrEmpty(iso) OrElse Not DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, t) Then Return ""
        Return t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
    End Function

    Private Function SelectedAccountDeviceId() As String
        If _accDevices Is Nothing OrElse _accDevices.SelectedItems.Count = 0 Then Return ""
        Return If(TryCast(_accDevices.SelectedItems(0).Tag, String), "")
    End Function

    ''' <summary>Revoke needs a selected device: without one the button is not drawn.</summary>
    Private Sub UpdateAccountRevokeVisibility()
        If _btnAccRevoke Is Nothing Then Return
        Dim wanted As Boolean = _accDevices.Visible AndAlso SelectedAccountDeviceId().Length > 0
        _btnAccRevoke.Visible = wanted
        _btnAccRevoke.Parent.Visible = wanted
    End Sub

    Private Sub ShowAccountResult(text As String)
        _accResult.Text = text
        _accResult.Visible = text.Length > 0
    End Sub

    ' --- actions ---

    Private Sub OnAccountSignIn()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.GetOrCreate()
        If agent Is Nothing Then Return
        Dim status As ExchangeDeviceStatus = agent.GetStatus()
        Using dlg As New Exchange_Account_Form(agent, If(status.Configured, status.Server, ""), If(status.Configured, status.Login, ""))
            dlg.ShowDialog(Me)
        End Using
        ShowAccountResult("")
        ApplyAccountState()
    End Sub

    ''' <summary>"Publish through an exchange server..": the sign-in dialog with the connection-string input and its "publish my shared folders now"
    ''' box ticked. Publishing stays an explicit choice either way (170 rule 1).</summary>
    Private Sub OnAccountPublish()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.GetOrCreate()
        If agent Is Nothing Then Return
        Using dlg As New Exchange_Account_Form(agent, "", "", publishNow:=True)
            dlg.ShowDialog(Me)
        End Using
        ShowAccountResult("")
        ApplyAccountState()
    End Sub

    Private Async Sub OnAccountJoinLocal()
        Await PerformLocalJoinAsync(_accResult, _btnAccJoinLocal, Nothing)
    End Sub

    ''' <summary>The one explicit act that lets a changed certificate in: the new fingerprint is fetched, shown beside the old one, and accepted only on
    ''' a Yes (the default is No).</summary>
    Private Async Sub OnAccountAcceptCertificate()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If agent Is Nothing OrElse _accBusy Then Return
        Dim status As ExchangeDeviceStatus = agent.GetStatus()
        _accBusy = True
        Try
            Dim info As ExchangeCertificateInfo = Await agent.ProbeCertificateAsync(status.Server, Threading.CancellationToken.None)
            Dim text As String = Localization.T("Сертификат сервера изменился.") & vbLf & vbLf &
                If(status.Fingerprint.Length > 0, Localization.TF("Прежний отпечаток: {0}", status.Fingerprint) & vbLf, "") &
                Localization.TF("Новый отпечаток: {0}", info.Fingerprint) & vbLf & vbLf &
                Localization.T("Сверьте новый отпечаток с тем, что дал оператор сервера. Принимайте его, только если он совпадает.")
            If ManagerBox.Show(Me, text, ExchangeDeviceText.GroupTitle(), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) = DialogResult.Yes Then
                agent.ReplacePinAndRestart(info.Fingerprint, info.PlatformValid)
            End If
            ShowAccountResult("")
        Catch ex As ExchangeConnectException
            ShowAccountResult(ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(If(ex.Failure = ExchangeConnectFailure.TimedOut, ExchangeEnrollOutcome.TimedOut,
                                                                   If(ex.Failure = ExchangeConnectFailure.TlsFailed, ExchangeEnrollOutcome.TlsFailed, ExchangeEnrollOutcome.Network)), ExchangeRefusal.Unknown, "")))
        Finally
            _accBusy = False
        End Try
        If Not IsDisposed Then ApplyAccountState()
    End Sub

    ''' <summary>Ask 2 of the entries proposal: the welcome announced another address and the user takes it. The edit is in place -
    ''' the device id, the token, the pin decision and the published shares are kept; only the stored address changes and the stream
    ''' restarts with it (a different leaf at the new address is item D's explicit replacement, as everywhere).</summary>
    Private Sub OnAccountUpdateAddress()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If agent Is Nothing OrElse _accBusy Then Return
        Dim status As ExchangeDeviceStatus = agent.GetStatus()
        Dim moved As String = ExchangeEntryMove.Announced(status.Server, status.PublicEndpoint)
        If moved.Length = 0 Then
            ApplyAccountState()
            Return
        End If
        If ManagerBox.Show(Me, ExchangeDeviceText.UpdateAddressQuestion(status.Server, moved), ExchangeDeviceText.GroupTitle(),
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) <> DialogResult.Yes Then Return
        agent.ReplaceServerAndRestart(moved)
        ShowAccountResult(ExchangeDeviceText.AddressUpdated())
        ApplyAccountState()
    End Sub

    Private Async Sub OnAccountAddDevice()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If agent Is Nothing OrElse _accBusy Then Return
        _accBusy = True
        Try
            Dim result As ExchangePairingResult = Await agent.RequestPairingCodeAsync(Threading.CancellationToken.None)
            If result.Succeeded Then
                Dim expires As DateTimeOffset
                If Not DateTimeOffset.TryParse(result.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, expires) Then expires = DateTimeOffset.UtcNow.AddMinutes(10)
                _accCodeText = result.Code
                _accCodeExpires = expires
                ShowPairingCode()
                ShowAccountResult("")
            Else
                ClearPairingCode()
                ShowAccountResult(ExchangeDeviceText.PairingFailure(result))
            End If
        Finally
            _accBusy = False
        End Try
    End Sub

    Private Sub ShowPairingCode()
        _accCode.Text = ExchangeDeviceText.CodeLine(_accCodeText)
        _accPairing.Visible = True
        _btnAccCopy.Visible = True
        UpdateAccountCountdown()
        _accTimer.Start()
    End Sub

    ''' <summary>A code is dropped the moment it is not needed: expired, not online, disconnected, the window closing.</summary>
    Private Sub ClearPairingCode()
        _accCodeText = ""
        If _accPairing IsNot Nothing Then _accPairing.Visible = False
        If _accTimer IsNot Nothing Then _accTimer.Stop()
    End Sub

    Private Sub OnAccountTimerTick(sender As Object, e As EventArgs)
        UpdateAccountCountdown()
    End Sub

    Private Sub UpdateAccountCountdown()
        If _accCodeText.Length = 0 Then Return
        Dim remaining As TimeSpan = _accCodeExpires - DateTimeOffset.UtcNow
        If remaining <= TimeSpan.Zero Then
            ' expired: the code leaves the screen and the memory
            _accCodeText = ""
            _accCode.Text = ""
            _accValid.Text = Localization.T("Код истёк.")
            _btnAccCopy.Visible = False
            _accTimer.Stop()
        Else
            _accValid.Text = ExchangeDeviceText.ValidFor(remaining)
        End If
    End Sub

    Private Sub OnAccountCopyCode()
        If _accCodeText.Length = 0 Then Return
        ' ONLY through the guarded door: the privacy markers keep it out of Win+V history and the cloud clipboard (CLIPBOARD-GUARD)
        If SecureClipboard.SetSecret(_accCodeText) Then
            ShowAccountResult(Localization.T("Код скопирован."))
            _accResult.ForeColor = ThemeService.Palette.TextMuted
        Else
            ShowAccountResult(Localization.T("Не удалось скопировать код."))
            _accResult.ForeColor = CollapsibleSection.AttentionColor
        End If
    End Sub

    Private Async Sub OnAccountRevoke()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        Dim id As String = SelectedAccountDeviceId()
        If agent Is Nothing OrElse id.Length = 0 OrElse _accBusy Then Return
        Dim status As ExchangeDeviceStatus = agent.GetStatus()
        Dim isMe As Boolean = String.Equals(id, status.DeviceId, StringComparison.Ordinal)
        Dim name As String = _accDevices.SelectedItems(0).Text
        ' the confirmation names the consequence; the safe answer (No) is the default
        Dim question As String = If(isMe, Localization.T("Выйти из учётной записи на этом ПК? Это устройство будет отозвано, а токен удалён."),
                                    Localization.TF("Отозвать устройство «{0}»? Оно потеряет доступ к учётной записи, а его ресурсы исчезнут из списка.", name))
        If ManagerBox.Show(Me, question, ExchangeDeviceText.GroupTitle(), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        _accBusy = True
        Try
            Dim sent As Boolean = Await agent.RevokeAsync(id, Threading.CancellationToken.None)
            ShowAccountResult(If(sent OrElse isMe, "", ExchangeDeviceText.NoConnectionForAction()))
        Finally
            _accBusy = False
        End Try
        If Not IsDisposed Then ApplyAccountState()
    End Sub

    ''' <summary>226 rule 4: the old password and the new one twice; on success this device keeps working with the fresh key of the
    ''' answer while the account's OTHER devices sign in again (224 rule 9) - the sentence the page shows says so.</summary>
    Private Async Sub OnAccountChangePassword()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If agent Is Nothing OrElse _accBusy Then Return
        Dim status As ExchangeDeviceStatus = agent.GetStatus()
        If status Is Nothing OrElse Not status.Configured Then Return
        Dim accountName As String = status.Login & " @ " & status.Server
        Using dlg As New Exchange_Password_Change_Form(accountName)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.NewPassword.Length = 0 Then Return
            Dim oldPassword As String = dlg.OldPassword
            Dim newPassword As String = dlg.NewPassword
            _accBusy = True
            Try
                Dim result As ExchangePasswordChangeResult = Await agent.ChangePasswordAsync(oldPassword, newPassword, Threading.CancellationToken.None)
                Select Case result.Outcome
                    Case ExchangePasswordChangeOutcome.Changed
                        ShowAccountResult(ExchangePasswordChangeText.Success())
                        _accResult.ForeColor = ThemeService.Palette.TextMuted
                    Case ExchangePasswordChangeOutcome.Refused
                        ShowAccountResult(ExchangePasswordChangeText.Failure(result.Refusal))
                        _accResult.ForeColor = CollapsibleSection.AttentionColor
                    Case Else
                        ShowAccountResult(ExchangePasswordChangeText.NoStream())
                        _accResult.ForeColor = CollapsibleSection.AttentionColor
                End Select
            Finally
                _accBusy = False
            End Try
            If Not IsDisposed Then ApplyAccountState()
        End Using
    End Sub

    ''' <summary>226 rule 5: "Sign out on this device" forgets this PC's entry and token, leaves the device record
    ''' on the server (it shows offline until the operator deletes it), and leaves attached resources in place.</summary>
    Private Async Sub OnAccountDisconnect()
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If agent Is Nothing OrElse _accBusy Then Return
        If ManagerBox.Show(Me, ExchangeJoinText.DisconnectQuestion(),
                           ExchangeDeviceText.GroupTitle(), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        _accBusy = True
        Try
            ClearPairingCode()
            Await agent.DisconnectAsync()
            ShowAccountResult(ExchangeJoinText.LeftLocally())
        Finally
            _accBusy = False
        End Try
        If Not IsDisposed Then ApplyAccountState()
    End Sub

    ''' <summary>226 rule 7: opens the media exchange portal page 'how to delete an account' (FMS_A S4243).</summary>
    Private Sub OnAccountDeleteLinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Try
            Dim url As String = SitePages.ExchangePortalUrl("account-deletion", Localization.CurrentCode)
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.WriteLine("Exchange account deletion link error: " & ex.Message)
        End Try
    End Sub

End Class
