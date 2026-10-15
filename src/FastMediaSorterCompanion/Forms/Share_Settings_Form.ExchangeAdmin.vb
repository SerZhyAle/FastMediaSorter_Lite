Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The administration rows of the "Media Exchange Server" group (ticket 168), a partial of its own beside
''' Share_Settings_Form.Exchange.vb: the "use this PC as my server" wizard entry, the address clients use, the
''' third-party port range, the certificate (kind, fingerprint, replace, import), the accounts with their devices and
''' the statement of what the operator can see.
'''
''' Rules held here, the group's rules:
'''   * a row that cannot apply is not drawn (<see cref="ExchangeGroupModel"/> and <see cref="ExchangeAccountsModel"/>
'''     decide), never disabled; the group never claims more than it checked;
'''   * every action runs off the UI thread behind the group's one busy flag and ends in one result line;
'''   * a destructive or disruptive action asks YesNo with the default on No and names the consequence;
'''   * a password is made or typed in a dialog, travels over the admin channel and is shown once when generated; it is
'''     kept on this PC ONLY when the person turned keeping on (<see cref="ExchangeAccountPasswordStore"/>, a DPAPI file),
'''     never logged and never in the registry. This file holds no password field of its own and writes no file itself;
'''   * every value is written THROUGH the binary (it validates with the serve rules), never by editing exchange.json;
'''   * no elevation is added here: the admin channel and a config write are all it takes (168 rule 3).
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _pnlXConfig As FlowLayoutPanel
    Private _txtXEndpoint As TextBox
    Private _btnXApplyEndpoint As Button
    Private _lblXEndpointNote As Label
    Private _lblXEndpointProblem As Label
    Private _lnkXSuggest As LinkLabel
    Private _exBaselineEndpoint As String = ""
    Private _exLoadingEndpoint As Boolean
    Private _exSuggestion As String
    Private _exSuggestionRead As Boolean

    Private numXRangeLow As NumericUpDown
    Private numXRangeHigh As NumericUpDown
    Private _btnXApplyRange As Button
    Private _lblXRangeNote As Label
    Private _lblXRangeProblem As Label
    Private _lblXRangeCount As Label
    Private _exBaselineLow As Integer
    Private _exBaselineHigh As Integer
    Private _exLoadingRange As Boolean

    Private _lblXCertKind As Label
    Private _rowXCert As FlowLayoutPanel
    Private _btnXReplaceCert As Button
    Private _btnXImportCert As Button
    Private _lblXCertService As Label

    Private _pnlXAccounts As FlowLayoutPanel
    Private _lblXAccTotals As Label
    Private _lblXAccHint As Label
    Private _lblXAccOffline As Label
    Private _lvXAccounts As ListView
    Private _rowXAccButtons As FlowLayoutPanel
    Private _btnXAddAccount As Button
    Private _btnXResetPw As Button
    Private _btnXAccountLimits As Button
    Private _btnXRemoveAccount As Button
    Private _btnXShowPw As Button
    Private _chkXKeepPasswords As CheckBox
    Private _lblXKeepNote As Label
    Private _exKeepLoading As Boolean
    Private _lblXDevTitle As Label
    Private _lvXDevices As ListView
    Private _rowXRevoke As FlowLayoutPanel
    Private _btnXRevoke As Button
    Private _lblXInvTitle As Label
    Private _chkXSharedInv As CheckBox
    Private _btnXSetSharedInv As Button
    Private _lblXSharedInvStatus As Label
    Private _rowXSharedInv As FlowLayoutPanel
    Private _loadingXSharedInv As Boolean
    Private _lvXInvitations As ListView
    Private _btnXCreateInvitation As Button
    Private _btnXWithdrawInvitation As Button
    Private _rowXInvButtons As FlowLayoutPanel
    Private _exInvFilling As Boolean
    Private _lnkXStatement As LinkLabel
    Private _txtXStatement As TextBox
    Private _exAccTimer As System.Windows.Forms.Timer
    Private _exAccView As ExchangeAccountsView
    Private _exAccReading As Boolean
    Private _exAccRereadRequested As Boolean
    Private _exAccFilling As Boolean

    ''' <summary>The refresh of the account lists while the group is unfolded and a server runs.</summary>
    Private Const ExchangeAccountsPollMs As Integer = 5000

    ''' <summary>The room of the administration rows, 96-dpi units: the section's body is indented and the window carries a scrollbar while it scrolls,
    ''' so a row the width of the hosting prose (520) was cut at its right edge.</summary>
    Private Const ExchangeAdminWidth As Integer = 480

    Private Shared Function AdminNote(text As String) As Label
        Dim l As Label = NoteLabel(text)
        l.MaximumSize = New Size(ExchangeAdminWidth, 0)
        Return l
    End Function

    Private Shared Function AdminFlow() As FlowLayoutPanel
        Return New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = True, .Margin = New Padding(0, 2, 0, 2), .MaximumSize = New Size(ExchangeAdminWidth, 0)}
    End Function

    Private Shared Function XColumn() As FlowLayoutPanel
        Return New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0)}
    End Function

    ''' <summary>The bold fonts of the sub-headings, kept so the window disposes them with itself.</summary>
    Private ReadOnly _xBoldFonts As New List(Of Font)()

    Private Function XBold(text As String) As Label
        Dim bold As New Font(Me.Font, FontStyle.Bold)
        _xBoldFonts.Add(bold)
        Return New Label With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 2), .MaximumSize = New Size(ExchangeAdminWidth, 0),
            .Font = bold, .Text = text}
    End Function

    ''' <summary>Every button that an action disables while it runs (the Hosting precedent): one list, so a new button is
    ''' not forgotten by <see cref="SetXBusy"/>.</summary>
    Private Function ExchangeBusyButtons() As Button()
        Return New Button() {_btnXApplyHost, _btnXRenewCert, _btnXApplyPort, _btnXApplyEndpoint, _btnXApplyRange, _btnXWizard, _btnXCopyFp, _btnXReplaceCert, _btnXImportCert,
                             _btnXAddAccount, _btnXResetPw, _btnXAccountLimits, _btnXRemoveAccount, _btnXCreateInvitation, _btnXWithdrawInvitation, _btnXSetSharedInv,
                             _btnXRevoke, _btnXStart, _btnXStop,
                             _btnXFwEnable, _btnXFwRemove, _btnXSvcStart, _btnXSvcStop, _btnXRegister, _btnXRemove}
    End Function

    ' --- building -----------------------------------------------------------------------------------

    ''' <summary>The address clients use, the third-party ports and the certificate: one column that is drawn only while
    ''' the binary answered (a server that cannot be asked has nothing to configure from here).</summary>
    Private Sub BuildExchangeAdminRows(sec As CollapsibleSection)
        _pnlXConfig = XColumn()
        _pnlXConfig.Visible = False

        ' the address clients use (168 section 3.3)
        _txtXEndpoint = New TextBox With {.Width = 300, .Margin = New Padding(0, 2, 0, 0), .AccessibleName = ExchangeAdminText.EndpointLabel()}
        AddHandler _txtXEndpoint.TextChanged, AddressOf OnXEndpointChanged
        Dim endpointRow As FlowLayoutPanel = AdminFlow()
        Dim endpointSetting = DirectCast(SettingsEditorRow(XBold(ExchangeAdminText.EndpointLabel()), _txtXEndpoint,
            New Padding(0, 2, 0, 2)), ManagerSettingRow)
        endpointSetting.SetHint(ExchangeAdminText.EndpointNote())
        _pnlXConfig.Controls.Add(endpointSetting)
        _btnXApplyEndpoint = XButton(ExchangeAdminText.EndpointApply(), AddressOf OnXApplyEndpoint, "action.confirm")
        _btnXApplyEndpoint.Visible = False
        endpointRow.Controls.Add(_btnXApplyEndpoint)
        _pnlXConfig.Controls.Add(endpointRow)
        _lnkXSuggest = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .MaximumSize = New Size(ExchangeAdminWidth, 0), .Visible = False}
        AddHandler _lnkXSuggest.LinkClicked, Sub() OnXSuggestClicked()
        _pnlXConfig.Controls.Add(_lnkXSuggest)
        _lblXEndpointProblem = New Label With {.AutoSize = True, .MaximumSize = New Size(ExchangeAdminWidth, 0), .Margin = New Padding(0, 0, 0, 2),
            .ForeColor = CollapsibleSection.AttentionColor, .Visible = False}
        _pnlXConfig.Controls.Add(_lblXEndpointProblem)
        _lblXEndpointNote = endpointSetting.Hint

        ' the third-party port range (168 section 3.5)
        numXRangeLow = New NumericUpDown With {.Minimum = ExchangeSettings.MinPort, .Maximum = ExchangeSettings.MaxPort, .Value = 40000, .Width = 84, .Margin = New Padding(0, 2, 6, 0)}
        numXRangeHigh = New NumericUpDown With {.Minimum = ExchangeSettings.MinPort, .Maximum = ExchangeSettings.MaxPort, .Value = 40100, .Width = 84, .Margin = New Padding(0, 2, 6, 0)}
        AddHandler numXRangeLow.ValueChanged, AddressOf OnXRangeChanged
        AddHandler numXRangeHigh.ValueChanged, AddressOf OnXRangeChanged
        Dim rangeRow As FlowLayoutPanel = AdminFlow()
        For Each pair In {New With {.Caption = ExchangeAdminText.RangeFrom(), .Editor = numXRangeLow},
                          New With {.Caption = ExchangeAdminText.RangeTo(), .Editor = numXRangeHigh}}
            Dim rangePart As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .WrapContents = False, .Margin = Padding.Empty}
            rangePart.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0), .Text = pair.Caption})
            rangePart.Controls.Add(pair.Editor)
            rangeRow.Controls.Add(rangePart)
        Next
        _exBaselineLow = CInt(numXRangeLow.Value)
        _exBaselineHigh = CInt(numXRangeHigh.Value)
        _btnXApplyRange = XButton(ExchangeAdminText.RangeApply(), AddressOf OnXApplyRange, "action.confirm")
        _btnXApplyRange.Visible = False
        Dim rangeSetting = DirectCast(SettingsEditorRow(XBold(ExchangeAdminText.RangeLabel()), rangeRow,
            New Padding(0, 8, 0, 2)), ManagerSettingRow)
        rangeSetting.SetHint(ExchangeAdminText.RangeNote())
        _pnlXConfig.Controls.Add(rangeSetting)
        _pnlXConfig.Controls.Add(_btnXApplyRange)
        _lblXRangeCount = AdminNote("")
        _lblXRangeCount.Margin = New Padding(0, 0, 0, 2)
        _pnlXConfig.Controls.Add(_lblXRangeCount)
        _lblXRangeProblem = New Label With {.AutoSize = True, .MaximumSize = New Size(ExchangeAdminWidth, 0), .Margin = New Padding(0, 0, 0, 2),
            .ForeColor = CollapsibleSection.AttentionColor, .Visible = False}
        _pnlXConfig.Controls.Add(_lblXRangeProblem)
        _lblXRangeNote = rangeSetting.Hint

        BuildXHostSettings()

        ' the certificate (168 section 3.4): its kind, the fingerprint to hand out, replace, import
        _lblXCertKind = New Label With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 2), .MaximumSize = New Size(ExchangeAdminWidth, 0)}
        _pnlXConfig.Controls.Add(_lblXCertKind)
        _rowXFp = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = True, .Margin = New Padding(0, 2, 0, 2), .MaximumSize = New Size(ExchangeAdminWidth, 0)}

        _txtXFp = New TextBox With {.ReadOnly = True, .Width = 380, .Margin = New Padding(0, 2, 6, 0), .AccessibleName = ExchangeText.FingerprintLabel()}
        Dim fingerprintSetting = DirectCast(SettingsEditorRow(New Label With {.AutoSize = True, .Text = ExchangeText.FingerprintLabel()},
            _txtXFp, New Padding(0, 2, 0, 2)), ManagerSettingRow)
        fingerprintSetting.SetHint(ExchangeText.FingerprintNote())
        _rowXFp.Controls.Add(fingerprintSetting)
        _btnXCopyFp = XButton(ExchangeText.CopyFingerprintButton(), AddressOf OnXCopyFingerprint, "action.copy")
        _rowXFp.Controls.Add(_btnXCopyFp)
        ' the connection string (ticket 187 T-2): built by the binary from the configured address and the real certificate
        _btnXConnString = XButton(ExchangeConnectionStringText.GetButton(), AddressOf OnXGetConnectionString, "action.export")
        _btnXConnString.Enabled = False
        _rowXFp.Controls.Add(_btnXConnString)
        _pnlXConfig.Controls.Add(_rowXFp)
        _lblXFpNote = AdminNote("")
        _pnlXConfig.Controls.Add(_lblXFpNote)
        _lblXConnNote = AdminNote(ExchangeConnectionStringText.NoEndpoint())
        _lblXConnNote.Visible = False
        _pnlXConfig.Controls.Add(_lblXConnNote)
        _btnXReplaceCert = XButton(ExchangeAdminText.ReplaceCertificateButton(), AddressOf OnXReplaceCertificate, "action.replace")
        _btnXImportCert = XButton(ExchangeAdminText.ImportCertificateButton(), AddressOf OnXImportCertificate, "action.import")
        _rowXCert = AdminFlow()
        _btnXRenewCert = XButton(Localization.T("Продлить сертификат с тем же ключом"), AddressOf OnXRenewCertificate, "action.confirm")
        _rowXCert.Controls.Add(_btnXRenewCert)
        _rowXCert.Controls.Add(_btnXImportCert)
        _rowXCert.Controls.Add(_btnXReplaceCert)
        _pnlXConfig.Controls.Add(_rowXCert)
        _lblXCertService = AdminNote(ExchangeAdminText.CertificateServiceNote())
        _pnlXConfig.Controls.Add(_lblXCertService)
        sec.AddBodyRow(_pnlXConfig)
    End Sub

    ''' <summary>The accounts and their devices (168 sections 3.1-3.2) and the statement (3.6).</summary>
    Private Sub BuildExchangeAccountRows(sec As CollapsibleSection)
        _pnlXAccounts = XColumn()
        _pnlXAccounts.Visible = False
        _pnlXAccounts.Controls.Add(XBold(ExchangeAdminText.AccountsTitle()))
        _pnlXAccounts.Controls.Add(AdminNote(ExchangeAdminText.AccountsIntro()))
        _lblXAccTotals = AdminNote("")
        _lblXAccTotals.Margin = New Padding(0, 0, 0, 2)
        _pnlXAccounts.Controls.Add(_lblXAccTotals)
        _lblXAccHint = New Label With {.AutoSize = True, .MaximumSize = New Size(ExchangeAdminWidth, 0), .Margin = New Padding(0, 0, 0, 4),
            .ForeColor = CollapsibleSection.AttentionColor, .Visible = False}
        _pnlXAccounts.Controls.Add(_lblXAccHint)
        _lblXAccOffline = AdminNote(ExchangeAdminText.OfflineNote())
        _lblXAccOffline.Visible = False
        _pnlXAccounts.Controls.Add(_lblXAccOffline)

        ' The control's own size is in 96-dpi design units (DpiLayout.ApplyAutoScale scales it as the last line of the builder);
        ' the COLUMN widths are not scaled by that, so they are device units (LogicalToDeviceUnits) - the Companion DPI recipe.
        _lvXAccounts = New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .Width = ExchangeAdminWidth, .Height = 110, .Margin = New Padding(0, 2, 0, 2), .Visible = False,
            .AccessibleName = ExchangeAdminText.AccountsTitle()}
        _lvXAccounts.Columns.Add(ExchangeAdminText.ColumnLogin(), Me.LogicalToDeviceUnits(160))
        _lvXAccounts.Columns.Add(Localization.T("Тип"), Me.LogicalToDeviceUnits(70))
        _lvXAccounts.Columns.Add(ExchangeAdminText.ColumnDevices(), Me.LogicalToDeviceUnits(70))
        _lvXAccounts.Columns.Add(ExchangeAdminText.ColumnCreated(), Me.LogicalToDeviceUnits(160))
        AddHandler _lvXAccounts.SelectedIndexChanged, AddressOf OnXAccountSelected
        _pnlXAccounts.Controls.Add(_lvXAccounts)

        _btnXAddAccount = XButton(ExchangeAdminText.AddAccountButton(), AddressOf OnXAddAccount, "action.add")
        _btnXResetPw = XButton(ExchangeAdminText.ResetPasswordButton(), AddressOf OnXResetPassword, "action.change-password")
        _btnXAccountLimits = XButton(Localization.T("Лимиты.."), AddressOf OnXAccountLimits, "action.edit")
        _btnXAccountLimits.Visible = False
        _btnXRemoveAccount = XButton(ExchangeAdminText.RemoveAccountButton(), AddressOf OnXRemoveAccount, "action.delete")
        _btnXShowPw = XButton(ExchangeAdminText.ShowPasswordButton(), AddressOf OnXShowPassword, "action.secure")
        _btnXShowPw.Visible = False
        _rowXAccButtons = AdminFlow()
        _rowXAccButtons.Controls.Add(_btnXAddAccount)
        _rowXAccButtons.Controls.Add(_btnXResetPw)
        _rowXAccButtons.Controls.Add(_btnXAccountLimits)
        _rowXAccButtons.Controls.Add(_btnXShowPw)
        _rowXAccButtons.Controls.Add(_btnXRemoveAccount)
        _pnlXAccounts.Controls.Add(_rowXAccButtons)

        ' keeping the passwords on this PC (off by default; the switch asks first, both ways)
        _chkXKeepPasswords = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 0), .Text = ExchangeAdminText.KeepPasswordsCheckbox(),
            .Checked = _exSettings.KeepAccountPasswords, .AccessibleName = ExchangeAdminText.KeepPasswordsCheckbox()}
        AddHandler _chkXKeepPasswords.CheckedChanged, AddressOf OnXKeepPasswordsChanged
        _pnlXAccounts.Controls.Add(_chkXKeepPasswords)
        _lblXKeepNote = AdminNote(ExchangeAdminText.KeepPasswordsNote())
        _pnlXAccounts.Controls.Add(_lblXKeepNote)

        _lblXDevTitle = XBold("")
        _lblXDevTitle.Visible = False
        _pnlXAccounts.Controls.Add(_lblXDevTitle)
        _lvXDevices = New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .Width = ExchangeAdminWidth, .Height = 110, .Margin = New Padding(0, 2, 0, 2), .Visible = False,
            .AccessibleName = Localization.T("Устройства")}
        _lvXDevices.Columns.Add(Localization.T("Устройство"), Me.LogicalToDeviceUnits(105))
        _lvXDevices.Columns.Add(ExchangeAdminText.ColumnProduct(), Me.LogicalToDeviceUnits(90))
        _lvXDevices.Columns.Add(ExchangeAdminText.ColumnPlatform(), Me.LogicalToDeviceUnits(85))
        _lvXDevices.Columns.Add(Localization.T("Состояние"), Me.LogicalToDeviceUnits(70))
        _lvXDevices.Columns.Add(Localization.T("Последний раз в сети"), Me.LogicalToDeviceUnits(110))
        AddHandler _lvXDevices.SelectedIndexChanged, Sub() ApplyAccountsView(refill:=False)
        _pnlXAccounts.Controls.Add(_lvXDevices)
        _btnXRevoke = XButton(Localization.T("Отозвать"), AddressOf OnXRevokeDevice, "action.remove")
        _rowXRevoke = AdminFlow()
        _rowXRevoke.Controls.Add(_btnXRevoke)
        _pnlXAccounts.Controls.Add(_rowXRevoke)

        ' Invitations (ticket 224)
        _lblXInvTitle = XBold(Localization.T("Приглашения"))
        _lblXInvTitle.Visible = False
        _pnlXAccounts.Controls.Add(_lblXInvTitle)
        _pnlXAccounts.Controls.Add(AdminNote(Localization.T("Одноразовые приглашения для регистрации новых пользователей на сервере.")))

        _chkXSharedInv = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 0),
            .Text = Localization.T("Общий пароль приглашения")}
        AddHandler _chkXSharedInv.CheckedChanged, AddressOf OnXSharedInvCheckChanged
        _btnXSetSharedInv = XButton(Localization.T("Задать общий пароль.."), AddressOf OnXSetSharedInvitation, "action.edit")
        _rowXSharedInv = AdminFlow()
        _rowXSharedInv.Controls.Add(_chkXSharedInv)
        _rowXSharedInv.Controls.Add(_btnXSetSharedInv)
        _pnlXAccounts.Controls.Add(_rowXSharedInv)
        _lblXSharedInvStatus = AdminNote("")
        _pnlXAccounts.Controls.Add(_lblXSharedInvStatus)

        _lvXInvitations = New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .Width = ExchangeAdminWidth, .Height = 110, .Margin = New Padding(0, 4, 0, 2), .Visible = False,
            .AccessibleName = Localization.T("Приглашения")}
        _lvXInvitations.Columns.Add(Localization.T("Тип"), Me.LogicalToDeviceUnits(80))
        _lvXInvitations.Columns.Add(ExchangeAdminText.ColumnCreated(), Me.LogicalToDeviceUnits(140))
        _lvXInvitations.Columns.Add(Localization.T("Состояние"), Me.LogicalToDeviceUnits(110))
        _lvXInvitations.Columns.Add(Localization.T("Хеш"), Me.LogicalToDeviceUnits(110))
        AddHandler _lvXInvitations.SelectedIndexChanged, Sub() ApplyAccountsView(refill:=False)
        _pnlXAccounts.Controls.Add(_lvXInvitations)

        _btnXCreateInvitation = XButton(Localization.T("Создать приглашение.."), AddressOf OnXCreateInvitation, "action.add")
        _btnXWithdrawInvitation = XButton(Localization.T("Отозвать"), AddressOf OnXWithdrawInvitation, "action.remove")
        _btnXWithdrawInvitation.Visible = False
        _rowXInvButtons = AdminFlow()
        _rowXInvButtons.Controls.Add(_btnXCreateInvitation)
        _rowXInvButtons.Controls.Add(_btnXWithdrawInvitation)
        _pnlXAccounts.Controls.Add(_rowXInvButtons)

        ' The statement of what the operator can see (DEVICE-EXCHANGE 7.9): always part of this group, never dismissible;
        ' only folded. It is shown once more at the first account (Exchange_Account_Admin_Form).
        _lnkXStatement = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 2), .Text = ExchangeAdminText.StatementShow()}
        AddHandler _lnkXStatement.LinkClicked, Sub() ToggleXStatement()
        _pnlXAccounts.Controls.Add(_lnkXStatement)
        _txtXStatement = New TextBox With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Width = ExchangeAdminWidth, .Height = 170,
            .Margin = New Padding(0, 2, 0, 6), .Visible = False, .Text = String.Join(vbCrLf & vbCrLf, ExchangeStatement.Paragraphs()),
            .AccessibleName = ExchangeAdminText.StatementTitle()}
        _pnlXAccounts.Controls.Add(_txtXStatement)
        sec.AddBodyRow(_pnlXAccounts)

        _exAccTimer = New System.Windows.Forms.Timer With {.Interval = ExchangeAccountsPollMs}
        AddHandler _exAccTimer.Tick, AddressOf OnXAccountsTick
        _exAccTimer.Start()
    End Sub

    Private Sub ToggleXStatement()
        _txtXStatement.Visible = Not _txtXStatement.Visible
        _lnkXStatement.Text = If(_txtXStatement.Visible, ExchangeAdminText.StatementHide(), ExchangeAdminText.StatementShow())
        If _built Then _root.PerformLayout()
    End Sub

    Private Sub DisposeExchangeAdminResources()
        Try
            If _exAccTimer IsNot Nothing Then
                _exAccTimer.Stop()
                _exAccTimer.Dispose()
            End If
        Catch
        End Try
        For Each f As Font In _xBoldFonts
            Try : f.Dispose() : Catch : End Try
        Next
        _xBoldFonts.Clear()
    End Sub

    ' --- painting the state -------------------------------------------------------------------------

    ''' <summary>Paints the administration rows from one snapshot and its model. Which rows EXIST is decided by visibility.</summary>
    Private Sub ApplyExchangeAdminState(snap As ExchangeSnapshot, m As ExchangeGroupModel)
        If _pnlXConfig Is Nothing Then Return
        _rowXWizard.Visible = m.ShowWizard
        _pnlXConfig.Visible = m.ShowConfigRows
        _pnlXAccounts.Visible = m.ShowAccounts

        ' the address: the server's own value, unless the person is mid-edit
        If Not EndpointPendingX() Then
            _exLoadingEndpoint = True
            Try
                _txtXEndpoint.Text = m.PublicEndpoint
            Finally
                _exLoadingEndpoint = False
            End Try
            _exBaselineEndpoint = m.PublicEndpoint
        End If
        UpdateXEndpointFeedback()

        ' the range: likewise
        Dim lo As Integer, hi As Integer
        If snap.Report IsNot Nothing AndAlso snap.Report.Answered AndAlso ExchangePortRangeRules.TryParse(snap.Report.PortRange, lo, hi) AndAlso Not RangePendingX() Then
            _exLoadingRange = True
            Try
                numXRangeLow.Value = Math.Max(numXRangeLow.Minimum, Math.Min(numXRangeLow.Maximum, lo))
                numXRangeHigh.Value = Math.Max(numXRangeHigh.Minimum, Math.Min(numXRangeHigh.Maximum, hi))
            Finally
                _exLoadingRange = False
            End Try
            _exBaselineLow = CInt(numXRangeLow.Value)
            _exBaselineHigh = CInt(numXRangeHigh.Value)
        End If
        UpdateXRangeFeedback()

        ApplyXHostSettings(snap)
        _btnXRenewCert.Visible = Not m.CertImported

        ' the certificate
        _lblXCertKind.Text = If(m.ShowFingerprint, ExchangeAdminText.CertificateKind(m.CertImported), "")
        _lblXCertKind.Visible = m.ShowFingerprint
        _rowXCert.Visible = m.ShowCertificateActions
        _lblXCertService.Visible = m.ShowCertificateServiceNote

        If m.ShowAccounts Then
            Dim t As Task = RefreshAccountsAsync()
        Else
            _exAccView = Nothing
            ApplyAccountsView()
        End If
    End Sub

    ' --- the address ----------------------------------------------------------------------------------

    Private Function EndpointPendingX() As Boolean
        Return _txtXEndpoint IsNot Nothing AndAlso Not String.Equals(_txtXEndpoint.Text.Trim(), _exBaselineEndpoint, StringComparison.Ordinal)
    End Function

    Private Sub OnXEndpointChanged(sender As Object, e As EventArgs)
        If _exLoadingEndpoint Then Return
        UpdateXEndpointFeedback()
    End Sub

    ''' <summary>Live feedback beside the field: the reason it cannot be used, the Apply button when it can, and the suggestion
    ''' while the field is empty (from facts the Share Manager already has - no external lookup, 142 Q142-3).</summary>
    Private Sub UpdateXEndpointFeedback()
        If _txtXEndpoint Is Nothing Then Return
        Dim verdict As ExchangeOperatorEndpoint.Verdict = ExchangeOperatorEndpoint.Check(_txtXEndpoint.Text)
        Dim usable As Boolean = verdict = ExchangeOperatorEndpoint.Verdict.Accepted OrElse verdict = ExchangeOperatorEndpoint.Verdict.Empty
        Dim problem As String = If(_txtXEndpoint.TextLength > 0, ExchangeAdminText.EndpointVerdict(verdict), "")
        _lblXEndpointProblem.Text = problem
        _lblXEndpointProblem.Visible = problem.Length > 0
        _btnXApplyEndpoint.Visible = EndpointPendingX() AndAlso usable
        If _txtXEndpoint.TextLength = 0 Then
            Dim suggestion As String = EndpointSuggestion()
            _lnkXSuggest.Text = If(suggestion.Length > 0, ExchangeAdminText.EndpointSuggest(suggestion), "")
            _lnkXSuggest.Tag = suggestion
            _lnkXSuggest.Visible = suggestion.Length > 0
        Else
            _lnkXSuggest.Visible = False
        End If
        _lblXEndpointNote.Text = If(EndpointPendingX() AndAlso usable, ExchangeAdminText.EndpointPending(), ExchangeAdminText.EndpointNote())
    End Sub

    ''' <summary>Read once: the worker's known public host, else this PC's LAN address, with the server's own port.</summary>
    Private Function EndpointSuggestion() As String
        If Not _exSuggestionRead Then
            _exSuggestionRead = True
            Try
                Dim external As String = If(_status IsNot Nothing AndAlso _status.Reachability IsNot Nothing, _status.Reachability.ExternalHost, "")
                _exSuggestion = ExchangeOperatorEndpoint.Suggest(external, NetworkInfo.LocalIPv4(), If(_exModel IsNot Nothing, _exModel.ShownPort(), ExchangeSettings.DefaultPort))
            Catch ex As Exception When TypeOf ex Is System.Net.NetworkInformation.NetworkInformationException OrElse TypeOf ex Is InvalidOperationException
                _exSuggestion = ""
            End Try
        End If
        Return If(_exSuggestion, "")
    End Function

    Private Sub OnXSuggestClicked()
        Dim suggestion As String = TryCast(_lnkXSuggest.Tag, String)
        If String.IsNullOrEmpty(suggestion) Then Return
        _txtXEndpoint.Text = suggestion
    End Sub

    Private Async Sub OnXApplyEndpoint()
        If _exBusy OrElse Not EndpointPendingX() Then Return
        Dim text As String = _txtXEndpoint.Text.Trim()
        Dim verdict As ExchangeOperatorEndpoint.Verdict = ExchangeOperatorEndpoint.Check(text)
        If verdict <> ExchangeOperatorEndpoint.Verdict.Accepted AndAlso verdict <> ExchangeOperatorEndpoint.Verdict.Empty Then Return
        SetXBusy(True)
        Try
            Dim outcome As ExchangeProcess.ConfigOutcome = Await Task.Run(Function() ExchangeProcess.SetEndpoint(text))
            If IsDisposed OrElse Disposing Then Return
            If outcome = ExchangeProcess.ConfigOutcome.Saved Then _exBaselineEndpoint = text
            ShowXResult(ExchangeAdminText.ConfigResult(outcome, If(text.Length = 0, ExchangeAdminText.EndpointCleared(), ExchangeAdminText.EndpointSaved())),
                        outcome = ExchangeProcess.ConfigOutcome.Saved)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: applying the public address", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    ' --- the third-party ports --------------------------------------------------------------------------

    Private Function RangePendingX() As Boolean
        Return numXRangeLow IsNot Nothing AndAlso (CInt(numXRangeLow.Value) <> _exBaselineLow OrElse CInt(numXRangeHigh.Value) <> _exBaselineHigh)
    End Function

    Private Sub OnXRangeChanged(sender As Object, e As EventArgs)
        If _exLoadingRange Then Return
        UpdateXRangeFeedback()
    End Sub

    Private Function RangeVerdictX() As ExchangePortRangeRules.Verdict
        Dim listen As Integer = If(_exModel IsNot Nothing, _exModel.ShownPort(), ExchangeSettings.DefaultPort)
        Return ExchangePortRangeRules.Check(CInt(numXRangeLow.Value), CInt(numXRangeHigh.Value), listen)
    End Function

    Private Sub UpdateXRangeFeedback()
        If numXRangeLow Is Nothing Then Return
        Dim verdict As ExchangePortRangeRules.Verdict = RangeVerdictX()
        Dim problem As String = ExchangeAdminText.RangeVerdict(verdict)
        _lblXRangeProblem.Text = problem
        _lblXRangeProblem.Visible = problem.Length > 0
        _lblXRangeCount.Text = ExchangeAdminText.RangeCount(ExchangePortRangeRules.Count(CInt(numXRangeLow.Value), CInt(numXRangeHigh.Value)))
        _btnXApplyRange.Visible = RangePendingX() AndAlso verdict = ExchangePortRangeRules.Verdict.Accepted
        _lblXRangeNote.Text = If(_btnXApplyRange.Visible, ExchangeAdminText.RangePending(), ExchangeAdminText.RangeNote())
    End Sub

    Private Async Sub OnXApplyRange()
        If _exBusy OrElse Not RangePendingX() OrElse RangeVerdictX() <> ExchangePortRangeRules.Verdict.Accepted Then Return
        Dim low As Integer = CInt(numXRangeLow.Value)
        Dim high As Integer = CInt(numXRangeHigh.Value)
        SetXBusy(True)
        Try
            Dim outcome As ExchangeProcess.ConfigOutcome = Await Task.Run(Function() ExchangeProcess.SetPortRange(ExchangePortRangeRules.Format(low, high)))
            If IsDisposed OrElse Disposing Then Return
            If outcome = ExchangeProcess.ConfigOutcome.Saved Then
                _exBaselineLow = low
                _exBaselineHigh = high
            End If
            ShowXResult(ExchangeAdminText.ConfigResult(outcome, ExchangeAdminText.RangeSaved()), outcome = ExchangeProcess.ConfigOutcome.Saved)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: applying the port range", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    ' --- the certificate ----------------------------------------------------------------------------------

    ''' <summary>Replacing the identity asks TWICE and says what will happen to every enrolled device; both defaults are No.</summary>
    Private Async Sub OnXReplaceCertificate()
        If _exBusy Then Return
        If MessageBox.Show(Me, ExchangeAdminText.ReplaceCertificateQuestion(), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        If MessageBox.Show(Me, ExchangeAdminText.ReplaceCertificateSure(), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await RunXCertificate(Function() ExchangeProcess.ReplaceCertificate())
    End Sub

    ''' <summary>Two file pickers (the certificate, then its key), one confirmation, then the binary validates the pair
    ''' (a pair, not expired, the key matches) and refuses a bad one before it writes anything. The key is never read,
    ''' shown, logged or copied by this program.</summary>
    Private Async Sub OnXImportCertificate()
        If _exBusy Then Return
        Dim certPath As String
        Dim keyPath As String
        Using ofd As New OpenFileDialog With {.Title = ExchangeAdminText.PickCertificateTitle(), .Filter = ExchangeAdminText.PemFilter(), .CheckFileExists = True}
            If ofd.ShowDialog(Me) <> DialogResult.OK Then Return
            certPath = ofd.FileName
        End Using
        Using ofd As New OpenFileDialog With {.Title = ExchangeAdminText.PickKeyTitle(), .Filter = ExchangeAdminText.PemFilter(), .CheckFileExists = True}
            If ofd.ShowDialog(Me) <> DialogResult.OK Then Return
            keyPath = ofd.FileName
        End Using
        If MessageBox.Show(Me, ExchangeAdminText.ImportCertificateQuestion(), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await RunXCertificate(Function() ExchangeProcess.ImportCertificate(certPath, keyPath))
    End Sub

    Private Async Function RunXCertificate(action As Func(Of ExchangeProcess.CertificateResult)) As Task
        SetXBusy(True)
        Try
            Dim result As ExchangeProcess.CertificateResult = Await Task.Run(action)
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(ExchangeAdminText.CertificateResult(result), result.Outcome = ExchangeProcess.CertificateOutcome.Done)
            Await RefreshExchangeAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: changing the certificate", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Function

    ' --- the wizard ---------------------------------------------------------------------------------------

    Private Async Sub OnXWizard()
        If _exBusy Then Return
        Using dlg As New Exchange_Server_Wizard_Form()
            dlg.ShowDialog(Me)
        End Using
        If IsDisposed OrElse Disposing Then Return
        ShowXResult("", True, neutral:=True)
        Try
            Await RefreshExchangeAsync()
            ' the wizard may have enrolled this PC's own agent: the client group reads it
            If _secAccount IsNot Nothing Then ApplyAccountState()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: after the wizard", ex)
        End Try
    End Sub

    ' --- accounts and devices ----------------------------------------------------------------------------

    Private Function SelectedXAccountLogin() As String
        If _lvXAccounts Is Nothing OrElse _lvXAccounts.SelectedItems.Count = 0 Then Return ""
        Return If(TryCast(_lvXAccounts.SelectedItems(0).Tag, String), "")
    End Function

    Private Function SelectedXDeviceId() As String
        If _lvXDevices Is Nothing OrElse _lvXDevices.SelectedItems.Count = 0 Then Return ""
        Return If(TryCast(_lvXDevices.SelectedItems(0).Tag, String), "")
    End Function

    ''' <summary>The lists refresh on a timer only while the group is unfolded, the window is up and a server RUNS (the
    ''' pipe is cheap; the offline path would spawn a process every few seconds for nothing).</summary>
    Private Sub OnXAccountsTick(sender As Object, e As EventArgs)
        If _exBusy OrElse _exAccReading OrElse _secExchange Is Nothing OrElse Not _secExchange.Expanded OrElse Not Visible Then Return
        If _exModel Is Nothing Then Return
        Dim serving As Boolean = _exModel.Kind = ExchangeRunKind.RunningConsole OrElse _exModel.Kind = ExchangeRunKind.ServiceRunning OrElse
                                 _exModel.Kind = ExchangeRunKind.ServiceStarting
        If Not serving Then Return
        Dim t As Task = RefreshAccountsAsync()
    End Sub

    ''' <summary>A selection changed. The lists are NOT rebuilt here: clearing a ListView's items from inside its own selection event leaves the
    ''' item that raised it without a list (a NullReferenceException in ListViewItem.Selected). Only which buttons exist follows, and the
    ''' devices of the newly selected account are read next.</summary>
    Private Sub OnXAccountSelected(sender As Object, e As EventArgs)
        If _exAccFilling Then Return
        ApplyAccountsView(refill:=False)
        Dim t As Task = RefreshAccountsAsync()
    End Sub

    ''' <summary>One read of the accounts and of the selected account's devices, off the UI thread (awaited).</summary>
    Private Async Function RefreshAccountsAsync() As Task
        If _lvXAccounts Is Nothing OrElse IsDisposed OrElse Disposing Then Return
        If _exAccReading Then
            ' a read is in flight (possibly for another selection): ask for one more when it ends, so a click is never lost
            _exAccRereadRequested = True
            Return
        End If
        _exAccReading = True
        Try
            Do
                _exAccRereadRequested = False
                Dim view As ExchangeAccountsView = Await ExchangeAccountsView.ReadAsync(SelectedXAccountLogin(), CancellationToken.None)
                If IsDisposed OrElse Disposing Then Return
                ' The continuation resumes on the window's own thread through the message loop's synchronization context. Should it ever resume on a
                ' pool thread instead (a host with no such context), the controls are still touched only from their own thread.
                If InvokeRequired Then
                    If Not IsHandleCreated Then Return
                    Invoke(New Action(Sub() ApplyReadAccounts(view)))
                Else
                    ApplyReadAccounts(view)
                End If
            Loop While _exAccRereadRequested
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: reading the accounts", ex)
        Finally
            _exAccReading = False
        End Try
    End Function

    Private Sub ApplyReadAccounts(view As ExchangeAccountsView)
        If IsDisposed OrElse Disposing Then Return
        _exAccView = view
        If _lblXAwakeState IsNot Nothing Then
            Dim stopped As Boolean = _exModel IsNot Nothing AndAlso (_exModel.Kind = ExchangeRunKind.Stopped OrElse _exModel.Kind = ExchangeRunKind.ServiceStopped OrElse _exModel.Kind = ExchangeRunKind.NotConfigured)
            _lblXAwakeState.Text = If(view.KeepAwakeKnown OrElse stopped, If(view.KeepAwakeActive, Localization.T("Запрос удержания от сна действует"), Localization.T("Запрос удержания от сна не действует")), ExchangeAdminText.NotReached())
        End If
        ApplyAccountsView()
    End Sub

    ''' <summary>Redraws the lists and which buttons exist from the last view and the selection, through the pure model.</summary>
    Private Sub ApplyAccountsView(Optional refill As Boolean = True)
        If _lvXAccounts Is Nothing Then Return
        Dim m As ExchangeAccountsModel = ExchangeAccountsModel.Build(_exAccView, SelectedXAccountLogin(), SelectedXDeviceId(), SelectedXInvitationHash())

        _lblXAccTotals.Text = If(m.Reached, ExchangeAdminText.Totals(m.AccountCount, m.DeviceTotal), "")
        _lblXAccTotals.Visible = m.Reached
        _lblXAccHint.Text = If(m.ShowNotReached, ExchangeAdminText.NotReached(), If(m.ShowFirstAccountHint, ExchangeAdminText.FirstAccountHint(), ""))
        _lblXAccHint.Visible = m.ShowNotReached OrElse m.ShowFirstAccountHint
        _lblXAccOffline.Visible = m.ShowOfflineNote
        _lvXAccounts.Visible = m.ShowAccountsList
        _rowXAccButtons.Visible = m.ShowAdd
        _btnXAddAccount.Visible = m.ShowAdd
        _btnXResetPw.Visible = m.ShowReset
        _btnXAccountLimits.Visible = m.ShowLimits
        _btnXShowPw.Visible = m.ShowReset AndAlso _exSettings.KeepAccountPasswords
        _btnXRemoveAccount.Visible = m.ShowRemove

        If refill AndAlso _exAccView IsNot Nothing AndAlso _exAccView.Reached Then FillXAccounts(_exAccView)
        _lblXDevTitle.Text = If(m.ShowDevices, ExchangeAdminText.DevicesTitle(SelectedXAccountLogin()), "")
        _lblXDevTitle.Visible = m.ShowDevices
        _lvXDevices.Visible = m.ShowDevices
        If refill AndAlso m.ShowDevices Then FillXDevices(_exAccView)
        _btnXRevoke.Visible = m.ShowRevoke
        _rowXRevoke.Visible = m.ShowRevoke

        ' Invitations
        _lblXInvTitle.Visible = m.Reached
        _rowXSharedInv.Visible = m.Reached
        _lblXSharedInvStatus.Visible = m.Reached
        _lvXInvitations.Visible = m.Reached
        _rowXInvButtons.Visible = m.Reached
        _btnXCreateInvitation.Visible = m.Reached
        _btnXWithdrawInvitation.Visible = m.ShowWithdrawInvitation

        If refill AndAlso _exAccView IsNot Nothing AndAlso _exAccView.Reached Then
            FillXInvitations(_exAccView)
            ApplySharedInvState(_exAccView.SharedInvitation)
        End If

        If _built Then _root.PerformLayout()
    End Sub

    ''' <summary>True when the list already holds exactly these keys (its items' tags), in this order: a refresh that changed no row is then a text
    ''' update in place, so the selection, the scroll position and the focus are never disturbed by a poll.</summary>
    Private Shared Function SameKeys(list As ListView, keys As List(Of String)) As Boolean
        If list.Items.Count <> keys.Count Then Return False
        For i As Integer = 0 To keys.Count - 1
            If Not String.Equals(TryCast(list.Items(i).Tag, String), keys(i), StringComparison.Ordinal) Then Return False
        Next
        Return True
    End Function

    Private Sub FillXAccounts(view As ExchangeAccountsView)
        Dim keys As List(Of String) = view.Accounts.Select(Function(a) a.Login).ToList()
        If SameKeys(_lvXAccounts, keys) Then
            For i As Integer = 0 To keys.Count - 1
                Dim a As ExchangeAdminAccount = view.Accounts(i)
                SetSubItem(_lvXAccounts.Items(i), 1, If(a.Kind = "guest", Localization.T("Гость"), Localization.T("Семья")))
                SetSubItem(_lvXAccounts.Items(i), 2, a.Devices.ToString(CultureInfo.CurrentCulture))
                SetSubItem(_lvXAccounts.Items(i), 3, FormatLastSeen(a.CreatedAt))
            Next
            Return
        End If
        ' the set of accounts changed (one was added or removed): rebuild, then put the selection back once the list is whole again
        Dim selected As String = SelectedXAccountLogin()
        Dim restore As ListViewItem = Nothing
        _exAccFilling = True
        _lvXAccounts.BeginUpdate()
        Try
            _lvXAccounts.Items.Clear()
            For Each a As ExchangeAdminAccount In view.Accounts
                Dim item As New ListViewItem(a.Login) With {.Tag = a.Login}
                item.SubItems.Add(If(a.Kind = "guest", Localization.T("Гость"), Localization.T("Семья")))
                item.SubItems.Add(a.Devices.ToString(CultureInfo.CurrentCulture))
                item.SubItems.Add(FormatLastSeen(a.CreatedAt))
                _lvXAccounts.Items.Add(item)
                If String.Equals(a.Login, selected, StringComparison.Ordinal) Then restore = item
            Next
        Finally
            _lvXAccounts.EndUpdate()
        End Try
        Try
            If restore IsNot Nothing Then restore.Selected = True
        Finally
            _exAccFilling = False
        End Try
    End Sub

    Private Shared Sub SetSubItem(item As ListViewItem, index As Integer, text As String)
        If Not String.Equals(item.SubItems(index).Text, text, StringComparison.Ordinal) Then item.SubItems(index).Text = text
    End Sub

    Private Sub FillXDevices(view As ExchangeAccountsView)
        Dim keys As List(Of String) = view.Devices.Select(Function(d) d.DeviceId).ToList()
        If SameKeys(_lvXDevices, keys) Then
            For i As Integer = 0 To keys.Count - 1
                Dim d As ExchangeAdminDevice = view.Devices(i)
                Dim item As ListViewItem = _lvXDevices.Items(i)
                SetSubItem(item, 3, ExchangeDeviceText.PresenceText(d.Online))
                SetSubItem(item, 4, FormatLastSeen(d.LastSeen))
            Next
            Return
        End If
        Dim selected As String = SelectedXDeviceId()
        Dim restore As ListViewItem = Nothing
        _lvXDevices.BeginUpdate()
        Try
            _lvXDevices.Items.Clear()
            For Each d As ExchangeAdminDevice In view.Devices
                Dim item As New ListViewItem(If(d.Name.Length > 0, d.Name, d.Product)) With {.Tag = d.DeviceId}
                item.SubItems.Add(If(d.ProductVersion.Length > 0, d.Product & " " & d.ProductVersion, d.Product))
                item.SubItems.Add(d.Platform)
                item.SubItems.Add(ExchangeDeviceText.PresenceText(d.Online))
                item.SubItems.Add(FormatLastSeen(d.LastSeen))
                _lvXDevices.Items.Add(item)
                If String.Equals(d.DeviceId, selected, StringComparison.Ordinal) Then restore = item
            Next
        Finally
            _lvXDevices.EndUpdate()
        End Try
        If restore IsNot Nothing Then restore.Selected = True
    End Sub

    ''' <summary>Add an account: the dialog (with the statement at the first one), then the admin channel, then - for a
    ''' generated password - the one and only showing of it.</summary>
    Private Async Sub OnXAddAccount()
        If _exBusy Then Return
        Dim first As Boolean = _exAccView IsNot Nothing AndAlso _exAccView.Reached AndAlso _exAccView.Accounts.Count = 0
        Dim login As String
        Dim password As String
        Dim generated As Boolean
        Using dlg As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add, "", first)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            login = dlg.Login
            password = dlg.Password
            generated = dlg.Generated
            dlg.WipePassword()
        End Using
        Dim askOwn As Boolean = False
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.AddAccountAsync(login, password, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            If reply.Ok Then
                Dim kept As Boolean = KeepAccountPassword(login, password)
                Dim keepFailed As Boolean = _exSettings.KeepAccountPasswords AndAlso Not kept
                Dim thisPc As (Text As String, Ok As Boolean) = Await EnrollThisPcAsFirstDeviceAsync(login, password)
                If IsDisposed OrElse Disposing Then Return
                ShowXResult(ExchangeAdminText.AccountAdded(login) & If(keepFailed, " " & ExchangeAdminText.KeepFailed(), "") &
                            If(thisPc.Text.Length > 0, " " & thisPc.Text, ""), Not keepFailed AndAlso thisPc.Ok)
                If generated Then
                    Dim shownOnce As String = password
                    password = Nothing
                    askOwn = ShowPasswordOnce(shownOnce, kept, login)
                End If
            Else
                ShowXResult(ExchangeAdminText.Failure(reply.Code), False)
            End If
            password = Nothing
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: adding an account", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
        ' "set my own password" in the generated dialog: the same account, reset with a password the person types
        If askOwn AndAlso Not (IsDisposed OrElse Disposing) Then Await ResetPasswordFor(login, True)
    End Sub

    Private Async Sub OnXResetPassword()
        If _exBusy Then Return
        Dim login As String = SelectedXAccountLogin()
        If login.Length = 0 Then Return
        Await ResetPasswordFor(login, False)
    End Sub

    ''' <summary>The reset of one account's password. The "set my own password" way out of the generated dialog comes back here with
    ''' <paramref name="preferTyped"/>, so the reset opens on TYPED.</summary>
    Private Async Function ResetPasswordFor(login As String, preferTyped As Boolean) As Task
        Dim password As String
        Dim generated As Boolean
        Dim signOut As Boolean
        Using dlg As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, login, False, preferTyped)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Exit Function
            password = dlg.Password
            generated = dlg.Generated
            signOut = dlg.SignOutEveryDevice
            dlg.WipePassword()
        End Using
        Dim askOwn As Boolean = False
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.ResetPasswordAsync(login, password, signOut, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Exit Function
            If reply.Ok Then
                Dim kept As Boolean = KeepAccountPassword(login, password)
                Dim keepFailed As Boolean = _exSettings.KeepAccountPasswords AndAlso Not kept
                Dim thisPc As (Text As String, Ok As Boolean) = Await EnrollThisPcAsFirstDeviceAsync(login, password)
                If IsDisposed OrElse Disposing Then Exit Function
                ShowXResult(ExchangeAdminText.PasswordReset(login, signOut) & If(keepFailed, " " & ExchangeAdminText.KeepFailed(), "") &
                            If(thisPc.Text.Length > 0, " " & thisPc.Text, ""), Not keepFailed AndAlso thisPc.Ok)
                If generated Then
                    Dim shownOnce As String = password
                    password = Nothing
                    askOwn = ShowPasswordOnce(shownOnce, kept, login)
                End If
            Else
                ShowXResult(ExchangeAdminText.Failure(reply.Code), False)
            End If
            password = Nothing
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: resetting an account", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
        If askOwn AndAlso Not (IsDisposed OrElse Disposing) Then Await ResetPasswordFor(login, True)
    End Function

    ''' <summary>The generated password, once, in its own dialog; the reference is dropped the moment the dialog is up. True when the person
    ''' chose "set my own password" in it instead. A kept password is not asked about on close: it can be shown again from the account row.</summary>
    Private Function ShowPasswordOnce(shownOnce As String, kept As Boolean, login As String) As Boolean
        Dim ownChosen As Boolean
        Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, shownOnce, login, kept)
            shownOnce = Nothing
            dlg.ShowDialog(Me)
            ownChosen = dlg.ChoseOwnPassword
        End Using
        Return ownChosen
    End Function

    ''' <summary>
    ''' The first device: an account was just added (or its password reset) HERE, on the PC that serves it, so the password is in hand and
    ''' this PC can sign in with it at once - the same steps as the "use this PC as my server" wizard, without creating anything.
    ''' Only when the client features are offered and this PC is in no account yet (an account it already belongs to is never replaced).
    ''' Text is "" when nothing applied; Ok is False when it applied and failed. The password is not kept: the request wipes it.
    ''' </summary>
    Private Async Function EnrollThisPcAsFirstDeviceAsync(login As String, password As String) As Task(Of (Text As String, Ok As Boolean))
        Try
            If Not ExchangeFeatures.IsEnabled() Then Return ("", True)
            If ExchangeDeviceHost.ProductionPorts().Accounts.IsConfigured() Then Return ("", True)
            Dim result As ExchangeWizardResult = Await ExchangeServerWizard.EnrollThisPcAsync(login, password, New ExchangeWizardPorts(), Nothing, CancellationToken.None)
            Return (ExchangeAdminText.ThisPcEnrolment(result), result.Outcome = ExchangeWizardOutcome.Done OrElse result.Outcome = ExchangeWizardOutcome.DoneNoAgent)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException OrElse TypeOf ex Is OperationCanceledException
            AppFileLogger.LogException("Exchange: enrolling this PC as the first device", ex)
            Return ("", True)
        End Try
    End Function

    ''' <summary>Keeps the password on this PC, but only when the person turned keeping on. True only when it was kept.</summary>
    Private Function KeepAccountPassword(login As String, password As String) As Boolean
        Return _exSettings.KeepAccountPasswords AndAlso ExchangeAccountPasswordStore.CreateDefault().Save(login, password)
    End Function

    ''' <summary>The kept password of the selected account, in its own dialog. This only reads: nothing is written here.</summary>
    Private Sub OnXShowPassword()
        Dim login As String = SelectedXAccountLogin()
        If login.Length = 0 Then Return
        Dim kept As String = ""
        If Not ExchangeAccountPasswordStore.CreateDefault().TryLoad(login, kept) Then
            ShowXResult(ExchangeAdminText.NotKept(login), False)
            Return
        End If
        Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Kept, kept, login, True)
            kept = Nothing
            dlg.ShowDialog(Me)
        End Using
    End Sub

    ''' <summary>The keeping switch. Turning it ON asks first with the default on No; turning it OFF asks first too, and then deletes
    ''' what was kept. A refused question puts the box back where it was: the answer is the person's, never the code's.</summary>
    Private Sub OnXKeepPasswordsChanged(sender As Object, e As EventArgs)
        If _exKeepLoading Then Return
        Dim wanted As Boolean = _chkXKeepPasswords.Checked
        If wanted = _exSettings.KeepAccountPasswords Then Return
        Dim question As String = If(wanted, ExchangeAdminText.KeepPasswordsConfirmOn(), ExchangeAdminText.KeepPasswordsConfirmOff())
        If MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
            _exKeepLoading = True
            _chkXKeepPasswords.Checked = Not wanted
            _exKeepLoading = False
            Return
        End If
        If Not wanted Then ExchangeAccountPasswordStore.CreateDefault().Clear()
        _exSettings.KeepAccountPasswords = wanted
        Try : _exSettings.Save() : Catch : End Try
        ApplyAccountsView(refill:=False)
    End Sub

    ''' <summary>Removing an account signs every device out and deletes their records: the question names that, default No.</summary>
    Private Async Sub OnXRemoveAccount()
        If _exBusy Then Return
        Dim login As String = SelectedXAccountLogin()
        If login.Length = 0 Then Return
        If MessageBox.Show(Me, ExchangeAdminText.RemoveAccountQuestion(login), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.RemoveAccountAsync(login, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(If(reply.Ok, ExchangeAdminText.AccountRemoved(login), ExchangeAdminText.Failure(reply.Code)), reply.Ok)
            ' a removed account keeps no password on this PC either (a no-op when nothing was ever kept)
            If reply.Ok Then ExchangeAccountPasswordStore.CreateDefault().Remove(login)
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: removing an account", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXRevokeDevice()
        If _exBusy Then Return
        Dim login As String = SelectedXAccountLogin()
        Dim id As String = SelectedXDeviceId()
        If login.Length = 0 OrElse id.Length = 0 Then Return
        Dim name As String = _lvXDevices.SelectedItems(0).Text
        If MessageBox.Show(Me, ExchangeAdminText.RevokeDeviceQuestion(name), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.RevokeDeviceAsync(login, id, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(If(reply.Ok, ExchangeAdminText.DeviceRevoked(name), ExchangeAdminText.Failure(reply.Code)), reply.Ok)
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: revoking a device", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Function SelectedXInvitationHash() As String
        If _lvXInvitations Is Nothing OrElse _lvXInvitations.SelectedItems.Count = 0 Then Return ""
        Return If(TryCast(_lvXInvitations.SelectedItems(0).Tag, String), "")
    End Function

    Private Sub ApplySharedInvState(sharedInv As ExchangeAdminSharedInvitation)
        If _chkXSharedInv Is Nothing Then Return
        _loadingXSharedInv = True
        Try
            Dim enabled As Boolean = sharedInv IsNot Nothing AndAlso sharedInv.Enabled
            _chkXSharedInv.Checked = enabled
            If enabled Then
                Dim kindText As String = If(String.Equals(sharedInv.Kind, "guest", StringComparison.OrdinalIgnoreCase), Localization.T("Гость"), Localization.T("Член семьи"))
                _lblXSharedInvStatus.Text = Localization.TF("Общий пароль включён (тип аккаунта: {0}).", kindText)
            Else
                _lblXSharedInvStatus.Text = Localization.T("Общий пароль выключен.")
            End If
        Finally
            _loadingXSharedInv = False
        End Try
    End Sub

    Private Sub FillXInvitations(view As ExchangeAccountsView)
        If _lvXInvitations Is Nothing Then Return
        Dim keys As List(Of String) = view.Invitations.Select(Function(inv) inv.CodeHash).ToList()
        If SameKeys(_lvXInvitations, keys) Then
            For i As Integer = 0 To keys.Count - 1
                Dim inv As ExchangeAdminInvitation = view.Invitations(i)
                Dim item As ListViewItem = _lvXInvitations.Items(i)
                SetSubItem(item, 0, If(String.Equals(inv.Kind, "guest", StringComparison.OrdinalIgnoreCase), Localization.T("Гость"), Localization.T("Семья")))
                SetSubItem(item, 1, FormatLastSeen(inv.CreatedAt))
                SetSubItem(item, 2, If(inv.Used, Localization.T("Использовано"), Localization.T("Активно")))
                SetSubItem(item, 3, If(inv.CodeHash.Length >= 8, inv.CodeHash.Substring(0, 8), inv.CodeHash))
            Next
            Return
        End If
        Dim selected As String = SelectedXInvitationHash()
        Dim restore As ListViewItem = Nothing
        _exInvFilling = True
        _lvXInvitations.BeginUpdate()
        Try
            _lvXInvitations.Items.Clear()
            For Each inv As ExchangeAdminInvitation In view.Invitations
                Dim item As New ListViewItem(If(String.Equals(inv.Kind, "guest", StringComparison.OrdinalIgnoreCase), Localization.T("Гость"), Localization.T("Семья"))) With {.Tag = inv.CodeHash}
                item.SubItems.Add(FormatLastSeen(inv.CreatedAt))
                item.SubItems.Add(If(inv.Used, Localization.T("Использовано"), Localization.T("Активно")))
                item.SubItems.Add(If(inv.CodeHash.Length >= 8, inv.CodeHash.Substring(0, 8), inv.CodeHash))
                _lvXInvitations.Items.Add(item)
                If String.Equals(inv.CodeHash, selected, StringComparison.Ordinal) Then restore = item
            Next
        Finally
            _lvXInvitations.EndUpdate()
        End Try
        If restore IsNot Nothing Then restore.Selected = True
        _exInvFilling = False
    End Sub

    Private Async Sub OnXAccountLimits()
        If _exBusy Then Return
        Dim login As String = SelectedXAccountLogin()
        If login.Length = 0 OrElse _exAccView Is Nothing Then Return
        Dim acc As ExchangeAdminAccount = _exAccView.Accounts.FirstOrDefault(Function(a) String.Equals(a.Login, login, StringComparison.Ordinal))
        If acc Is Nothing Then Return
        Dim newLimits As ExchangeAccountLimits
        Using dlg As New Exchange_Account_Limits_Form(login, acc.Limits)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            newLimits = dlg.Limits
        End Using
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.SetAccountLimitsAsync(login, newLimits, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(If(reply.Ok, Localization.TF("Лимиты аккаунта {0} сохранены.", login), ExchangeAdminText.Failure(reply.Code)), reply.Ok)
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: updating account limits", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXCreateInvitation()
        If _exBusy Then Return
        Dim kind As String = "member"
        Using dlg As New Exchange_Invitation_Create_Form()
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            kind = dlg.Kind
        End Using
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.CreateInvitationAsync(kind, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            If reply.Ok Then
                Dim created As ExchangeAdminInvitationCreated = ExchangeAdminParser.InvitationCreated(reply.DataJson)
                If created Is Nothing OrElse created.Code.Length = 0 Then
                    ' the invitation exists but its code did not arrive: it can only be withdrawn and made again
                    ShowXResult(ExchangeAdminText.Failure(ExchangeAdminCodes.Internal), False)
                    Await RefreshExchangeAsync()
                    Return
                End If
                Dim connStrResult As ExchangeProcess.ConnectionStringResult = Await Task.Run(Function() ExchangeProcess.ConnectionString("", "", created.Code))
                Dim connStrText As String = If(connStrResult.Outcome = ExchangeProcess.ConnectionStringOutcome.Made, connStrResult.Text, "")
                ShowXResult(Localization.T("Приглашение успешно создано."), True)
                Using showDlg As New Exchange_Invitation_Created_Form(created.Code, connStrText, kind)
                    showDlg.ShowDialog(Me)
                End Using
            Else
                ShowXResult(ExchangeAdminText.Failure(reply.Code), False)
            End If
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: creating invitation", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXWithdrawInvitation()
        If _exBusy Then Return
        Dim hash As String = SelectedXInvitationHash()
        If hash.Length = 0 Then Return
        If MessageBox.Show(Me, Localization.T("Отозвать выбранное приглашение? Код перестанет действовать."), Me.Text,
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.WithdrawInvitationAsync(hash, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(If(reply.Ok, Localization.T("Приглашение отозвано."), ExchangeAdminText.Failure(reply.Code)), reply.Ok)
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: withdrawing invitation", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Sub OnXSharedInvCheckChanged(sender As Object, e As EventArgs)
        If _loadingXSharedInv OrElse _exBusy Then Return
        Dim wanted As Boolean = _chkXSharedInv.Checked
        If Not wanted Then
            If MessageBox.Show(Me, Localization.T("Отключить общий пароль приглашения? Новые регистрации по нему станут невозможны."),
                               Me.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                _loadingXSharedInv = True
                _chkXSharedInv.Checked = True
                _loadingXSharedInv = False
                Return
            End If
            DisableSharedInvitation()
        Else
            OnXSetSharedInvitation()
        End If
    End Sub

    Private Async Sub DisableSharedInvitation()
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.SetSharedInvitationAsync("", "", False, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            ShowXResult(If(reply.Ok, Localization.T("Общий пароль приглашения отключён."), ExchangeAdminText.Failure(reply.Code)), reply.Ok)
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: disabling shared invitation", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

    Private Async Sub OnXSetSharedInvitation()
        If _exBusy Then Return
        Dim curKind As String = If(_exAccView IsNot Nothing AndAlso _exAccView.SharedInvitation IsNot Nothing, _exAccView.SharedInvitation.Kind, "member")
        Dim pw As String
        Dim kind As String
        Dim generated As Boolean
        Using dlg As New Exchange_Shared_Invitation_Form(curKind)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then
                If _exAccView IsNot Nothing AndAlso _exAccView.SharedInvitation IsNot Nothing Then
                    _loadingXSharedInv = True
                    _chkXSharedInv.Checked = _exAccView.SharedInvitation.Enabled
                    _loadingXSharedInv = False
                End If
                Return
            End If
            pw = dlg.Password
            kind = dlg.Kind
            generated = dlg.Generated
            dlg.WipePassword()
        End Using
        SetXBusy(True)
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.SetSharedInvitationAsync(pw, kind, True, CancellationToken.None)
            If IsDisposed OrElse Disposing Then Return
            If reply.Ok Then
                ShowXResult(Localization.T("Общий пароль приглашения сохранён и активирован."), True)
                If generated Then
                    Dim shownOnce As String = pw
                    pw = Nothing
                    ShowPasswordOnce(shownOnce, False, Localization.T("общий пароль"))
                End If
            Else
                ShowXResult(ExchangeAdminText.Failure(reply.Code), False)
            End If
            pw = Nothing
            Await RefreshExchangeAsync()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange: setting shared invitation", ex)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetXBusy(False)
        End Try
    End Sub

End Class
