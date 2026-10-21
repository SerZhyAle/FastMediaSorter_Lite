Option Strict On

Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The sign-in dialog of the device agent (ticket 180 section 4): server host:port, login, and EITHER a password OR a pairing code another
''' device issued. Three rules shape it:
'''
'''   * First contact shows the certificate before anything secret is sent. "Check the certificate" connects and shows the server's fingerprint
'''     beside "compare this with the one the operator gave you"; only the explicit "Trust this certificate" accepts it (the default button is
'''     Cancel - there is no accept button at all until a certificate has been shown). A certificate the system itself trusts is recorded as such
'''     and pins nothing (DEVICE-EXCHANGE item D). Changing the server text forgets the check.
'''   * The password is never kept: the field is cleared the moment the frame is built, whether the sign-in worked or not, and the agent
'''     wipes its copy. The device keeps only a token.
'''   * Every refusal is one localized sentence (ExchangeDeviceText.EnrollFailure) - never the server's raw reason.
'''
''' Ticket 187 T-5: the dialog also takes a CONNECTION STRING (pasted, or opened from a .fmsexch file). The string fills the server and the
''' login hint, and its `fp` is the trust anchor - no manual comparison, a different leaf is refused with both fingerprints and nothing sent
''' (rule 3). It FILLS the form and stops: nothing connects until the person pressed Sign in. The share service is at most ONE producer: a join
''' while another server is held asks to replace it, and replacing leaves the first one cleanly. "Publish my shared folders now" is an explicit
''' choice (ticked when the dialog was opened as "Publish through an exchange server..", unticked otherwise), with the words of 170 rule 1.
'''
''' Offers "Start the Share Manager when I sign in to Windows" (default off): a cast can only reach a PC whose agent runs.
'''
''' Hand-built, so it follows the Companion recipe: language direction first, DPI scaling as the LAST line of the builder, the clamp to the
''' working area in OnLoad.
''' </summary>
Friend NotInheritable Class Exchange_Account_Form
    Inherits Form

    Private Const ContentWidth As Integer = 440

    Private ReadOnly _agent As ExchangeDeviceAgent
    Private _iconHandle As IntPtr
    Private _txtServer As TextBox
    Private _txtLogin As TextBox
    Private _rbPassword As RadioButton
    Private _rbCode As RadioButton
    Private _lblSecret As Label
    Private _txtSecret As TextBox
    ''' <summary>226 rule 1: registration by invitation asks for the password twice and states the policy in words. Both live only in the register mode.</summary>
    Private _lblSecretRepeat As Label
    Private _txtSecretRepeat As TextBox
    Private _lblPolicy As Label
    Private _register As Boolean
    Private _invitationCode As String = ""
    Private _chkShow As CheckBox
    Private _txtName As TextBox
    Private _btnCheck As Button
    Private _pnlCert As FlowLayoutPanel
    Private _txtFingerprint As TextBox
    Private _lblCertNote As Label
    Private _btnTrust As Button
    Private _chkStart As CheckBox
    Private _txtString As TextBox
    Private _stringButtons As FlowLayoutPanel
    Private _loginHintApplied As String = ""
    Private _lblString As Label
    Private _chkPublish As CheckBox
    ''' <summary>The fingerprint the pasted string named: the trust anchor, no prompt. "" when the string had none (or there was none).</summary>
    Private _pinnedFromString As String = ""
    Private ReadOnly _publishNow As Boolean
    Private _lblResult As Label
    Private _btnSignIn As Button
    Private _btnDiagnose As Button
    Private _btnCancel As Button
    Private _answerRow As DialogButtonRow
    Private _lastDiagnosisMessage As ConnectionDiagnosisMessage = Nothing

    Private _probe As ExchangeCertificateInfo
    Private _trusted As Boolean
    Private _busy As Boolean
    Private ReadOnly _cts As New CancellationTokenSource()

    Friend Sub New(agent As ExchangeDeviceAgent, Optional presetServer As String = "", Optional presetLogin As String = "", Optional publishNow As Boolean = False)
        _agent = agent
        _publishNow = publishNow
        BuildUi(presetServer, presetLogin)
    End Sub

    Private Function Note(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 2, 0, 4), .Text = text}
    End Function

    Private Function Caption(text As String) As Label
        Return New Label With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 2), .Text = text}
    End Function

    Private Function NewButton(text As String, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildUi(presetServer As String, presetLogin As String)
        UiLanguage.ApplyTo(Me)
        Me.Text = ExchangeDeviceText.GroupTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                ' nothing secret outlives the window
                Try
                    _txtSecret.Text = ""
                    _txtSecretRepeat.Text = ""
                    _invitationCode = ""
                    _txtString.Text = ""
                Catch
                End Try
                Try : _cts.Cancel() : Catch : End Try
                Try : _cts.Dispose() : Catch : End Try
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 8, 14, 6)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        ' the connection string (187): optional, fills the form and stops
        body.Controls.Add(ExchangeMark.Heading(Text, 32, AddressOf CompanionAssets.OpenBundledAsset))
        body.Controls.Add(Caption(ExchangeJoinText.StringLabel()))
        _txtString = New TextBox With {.Width = ContentWidth, .Margin = New Padding(0, 0, 0, 2), .AccessibleName = ExchangeJoinText.StringLabel()}
        AddHandler _txtString.TextChanged, AddressOf OnStringChanged
        body.Controls.Add(_txtString)
        _stringButtons = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Margin = New Padding(0)}
        Dim paste As Button = NewButton(ExchangeJoinText.PasteButton(), "action.paste")
        AddHandler paste.Click, AddressOf OnPasteString
        Dim openFile As Button = NewButton(ExchangeJoinText.OpenFileButton(), "action.choose-file")
        AddHandler openFile.Click, AddressOf OnOpenStringFile
        _stringButtons.Controls.Add(paste)
        _stringButtons.Controls.Add(openFile)
        Dim scan As Button = NewButton(Localization.T("Сканировать камерой.."))
        AddHandler scan.Click, AddressOf OnScanString
        _stringButtons.Controls.Add(scan)
        body.Controls.Add(_stringButtons)
        _lblString = Note("")
        _lblString.Visible = False
        body.Controls.Add(_lblString)

        ' the server and the first-contact check
        body.Controls.Add(Caption(Localization.T("Сервер (хост:порт):")))
        _txtServer = New TextBox With {.Width = ContentWidth, .Text = presetServer, .Margin = New Padding(0, 0, 0, 2)}
        AddHandler _txtServer.TextChanged, AddressOf OnServerChanged
        body.Controls.Add(_txtServer)

        _btnCheck = NewButton(Localization.T("Проверить сертификат"), "action.verify")
        AddHandler _btnCheck.Click, AddressOf OnCheckClicked
        body.Controls.Add(_btnCheck)

        _pnlCert = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0), .Visible = False}
        _pnlCert.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Отпечаток сертификата сервера:")})
        _txtFingerprint = New TextBox With {.Width = ContentWidth, .ReadOnly = True, .Margin = New Padding(0, 0, 0, 2)}
        _pnlCert.Controls.Add(_txtFingerprint)
        _lblCertNote = Note("")
        _pnlCert.Controls.Add(_lblCertNote)
        _btnTrust = NewButton(Localization.T("Доверять этому сертификату"), "action.confirm", ButtonGlyphRole.Caution)
        AddHandler _btnTrust.Click, AddressOf OnTrustClicked
        _pnlCert.Controls.Add(_btnTrust)
        body.Controls.Add(_pnlCert)

        ' who
        body.Controls.Add(Caption(Localization.T("Логин:")))
        _txtLogin = New TextBox With {.Width = ContentWidth, .Text = presetLogin, .Margin = New Padding(0, 0, 0, 2)}
        _loginHintApplied = If(presetLogin, "")
        body.Controls.Add(_txtLogin)

        _rbPassword = New RadioButton With {.AutoSize = True, .Checked = True, .Margin = New Padding(0, 8, 0, 0), .Text = Localization.T("Войти с паролем")}
        _rbCode = New RadioButton With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 0), .Text = Localization.T("Войти по коду другого устройства")}
        AddHandler _rbPassword.CheckedChanged, AddressOf OnModeChanged
        body.Controls.Add(_rbPassword)
        body.Controls.Add(_rbCode)

        _lblSecret = Caption(Localization.T("Пароль:"))
        body.Controls.Add(_lblSecret)
        _txtSecret = New TextBox With {.Width = ContentWidth, .UseSystemPasswordChar = True, .Margin = New Padding(0, 0, 0, 2)}
        body.Controls.Add(_txtSecret)
        _chkShow = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .Text = Localization.T("Показать пароль")}
        AddHandler _chkShow.CheckedChanged, Sub()
                                                 _txtSecret.UseSystemPasswordChar = Not _chkShow.Checked
                                                 _txtSecretRepeat.UseSystemPasswordChar = Not _chkShow.Checked
                                             End Sub
        ' Each option is a boolean row (BOOLEAN-OPTION 2.0, ticket 243 T-11c-b2a): the check box keeps its field, default and
        ' handlers; the row around it is what the table holds (and what hides and shows).
        body.Controls.Add(OptionRows.InFlow(_chkShow))
        _lblSecretRepeat = Caption(Localization.T("Пароль ещё раз:"))
        _lblSecretRepeat.Visible = False
        body.Controls.Add(_lblSecretRepeat)
        _txtSecretRepeat = New TextBox With {.Width = ContentWidth, .UseSystemPasswordChar = True, .Margin = New Padding(0, 0, 0, 2), .Visible = False}
        body.Controls.Add(_txtSecretRepeat)
        _lblPolicy = Note(Localization.T("Пароль: не короче 10 символов, с заглавной латинской буквой, со строчной латинской буквой и с цифрой."))
        _lblPolicy.Visible = False
        body.Controls.Add(_lblPolicy)

        body.Controls.Add(Caption(Localization.T("Имя этого устройства:")))
        _txtName = New TextBox With {.Width = ContentWidth, .Text = Environment.MachineName, .MaxLength = 64, .Margin = New Padding(0, 0, 0, 2)}
        body.Controls.Add(_txtName)

        ' publishing is an explicit choice (170 rule 1): in words that say what it does, unticked unless the dialog was opened to publish
        ' (what each of the two options does stands under its caption as the row's explanation)
        _chkPublish = New CheckBox With {.AutoSize = True, .Checked = _publishNow, .Text = ExchangeJoinText.PublishCheckbox()}
        Dim publishRow As OptionRowHost = OptionRows.InFlow(_chkPublish, Note(ExchangePublishText.PublishNote()))
        publishRow.Margin = New Padding(0, 10, 0, 0)
        body.Controls.Add(publishRow)

        ' the autostart offer: default off
        _chkStart = New CheckBox With {.AutoSize = True, .Checked = False,
            .Text = Localization.TF("Запускать {0} при входе в Windows", ProductNames.ShareManager)}
        Dim startRow As OptionRowHost = OptionRows.InFlow(_chkStart, Note(Localization.T("Без этого другие устройства не смогут отправить сюда трансляцию, пока менеджер не запущен.")))
        startRow.Margin = New Padding(0, 10, 0, 0)
        body.Controls.Add(startRow)

        _lblResult = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = CollapsibleSection.AttentionColor,
            .Margin = New Padding(0, 4, 0, 2), .Visible = False}
        body.Controls.Add(_lblResult)

        ' The answers ride the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): the connection-test door (a
        ' command, shown only after a failed first contact) first and set apart, then Cancel, then Sign in last. Escape is
        ' Cancel. Enter is NOT bound by the row (bindAccept:=False): the window has no accept button until a certificate has
        ' been shown and trusted (ShowCertificate / OnTrustClicked set it, OnServerChanged takes it away), so Enter in the
        ' server or password field cannot send a password to a server nobody has vouched for.
        _btnSignIn = NewButton(Localization.T("Войти"), "action.confirm", ButtonGlyphRole.Affirmative)
        _btnSignIn.Name = "xacc_SignIn"
        _btnSignIn.Visible = True   ' visible so the user has the clear action button to apply / sign in
        AddHandler _btnSignIn.Click, AddressOf OnSignInClicked
        _btnCancel = NewButton(Localization.T("Отмена"), "action.cancel", ButtonGlyphRole.Dismissing)
        _btnCancel.Name = "xacc_Cancel"
        _btnCancel.DialogResult = DialogResult.Cancel
        _btnDiagnose = NewButton(ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCode), "action.test-connection")
        _btnDiagnose.Name = "xacc_Diagnose"
        _btnDiagnose.Visible = False
        AddHandler _btnDiagnose.Click, AddressOf OnDiagnoseClicked
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xacc_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        DpiLayout.AddAnswer(_answerRow, _btnDiagnose, DialogButtonRole.Command, "action.test-connection")
        DpiLayout.AddAnswer(_answerRow, _btnCancel, DialogButtonRole.Dismissing, "action.cancel")
        DpiLayout.AddAnswer(_answerRow, _btnSignIn, DialogButtonRole.Affirmative, "action.confirm")

        Me.Controls.Add(body)
        ' Last in the collection: the docked row takes its edge first and reads after the fields in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me, bindAccept:=False)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ' The default is Cancel: no accept button exists until a certificate has been shown.
        Me.AcceptButton = Nothing

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        OptionRows.ArrangeAll(Me)
        FitAccountToWorkingArea()
        DpiLayout.CenterOnOwner(Me)
        _txtServer.Focus()
    End Sub

    ' --- state ---

    Private Sub ShowResult(text As String)
        _lblResult.Text = text
        _lblResult.Visible = text.Length > 0
        If String.IsNullOrEmpty(text) AndAlso _btnDiagnose IsNot Nothing Then
            _btnDiagnose.Visible = False
        End If
    End Sub

    Private Sub FitAccountToWorkingArea()
        For Each child As Control In Controls
            Dim body As TableLayoutPanel = TryCast(child, TableLayoutPanel)
            If body Is Nothing Then Continue For
            Dim area As Rectangle = Screen.FromControl(Me).WorkingArea
            Dim content As Size = body.GetPreferredSize(Size.Empty)
            ' the answer row sits on the window's bottom edge under the fields: it is part of what the window must hold
            Dim rowHeight As Integer = _answerRow.GetPreferredSize(New Size(content.Width, 0)).Height
            Dim desired As Size = SizeFromClientSize(New Size(content.Width, content.Height + rowHeight))
            If body.AutoScroll OrElse desired.Height > area.Height OrElse desired.Width > area.Width Then
                ' what scrolls is the body of fields; the row never does, so the answers stay on screen
                AutoSize = False
                body.AutoScroll = True
                ' A table's own scroll extent is short of its rows (it leaves the padding out), so the tail of the last row - the
                ' autostart explanation - would stay out of reach: the extent follows the rows on every layout.
                OptionRows.KeepScrollExtent(body)
                AddHandler body.Layout, Sub() OptionRows.KeepScrollExtent(body)
                Dim bar As Integer = If(desired.Height > area.Height, SystemInformation.VerticalScrollBarWidth, 0)
                Size = New Size(Math.Min(desired.Width + bar, area.Width), Math.Min(desired.Height, area.Height))
            End If
            Exit For
        Next
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Sub SetBusy(busy As Boolean)
        _busy = busy
        _btnCheck.Enabled = Not busy
        _btnSignIn.Enabled = Not busy
        _btnTrust.Enabled = Not busy
        For Each input As Control In New Control() {_txtString, _stringButtons, _txtServer, _txtLogin, _txtSecret, _txtSecretRepeat,
                                                   _txtName, _rbPassword, _rbCode, _chkShow, _chkPublish, _chkStart}
            input.Enabled = Not busy
        Next
    End Sub

    ''' <summary>A new server text forgets the certificate that was shown for the old one.</summary>
    Private Sub OnServerChanged(sender As Object, e As EventArgs)
        _pinnedFromString = ""
        _probe = Nothing
        _trusted = False
        _pnlCert.Visible = False
        _btnCheck.Visible = True
        Me.AcceptButton = Nothing
        ShowResult("")
        ApplyRegisterMode("")
    End Sub

    ' --- the connection string ---

    Private Sub OnStringChanged(sender As Object, e As EventArgs)
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(_txtString.Text)
        ' A new hand-off is a new trust decision, even when its endpoint text is unchanged.
        OnServerChanged(Me, EventArgs.Empty)
        Select Case input.Kind
            Case ExchangeConnectInputKind.Empty
                _lblString.Visible = False
            Case ExchangeConnectInputKind.Refused
                _lblString.ForeColor = CollapsibleSection.AttentionColor
                _lblString.Text = ExchangeJoinText.InputRefusal(input.Reason, input.Looks)
                _lblString.Visible = True
            Case Else
                ' it FILLS the form and stops: no connection, no byte sent
                _txtServer.Text = input.Value.Endpoint
                If input.Value.Login.Length > 0 AndAlso (_txtLogin.Text.Length = 0 OrElse _txtLogin.Text = _loginHintApplied) Then
                    _txtLogin.Text = input.Value.Login
                    _loginHintApplied = input.Value.Login
                End If
                _lblString.ForeColor = ThemeService.Palette.TextMuted
                _lblString.Text = ExchangeJoinText.StringRead(input.Value.Endpoint)
                _lblString.Visible = True
                ' 226 rule 1: an invitation string offers the REGISTRATION - the third way beside a sign-in and a pairing code.
                ' The code is a secret holder: it lives in this window and in the one frame, and nowhere after that.
                If input.HasInvitation Then ApplyRegisterMode(input.Value.Invitation)
                If input.HasPin Then
                    ' the string's fp is the trust anchor: nothing to compare by hand, a different leaf is refused at enrollment
                    _pinnedFromString = input.Value.Fingerprint
                    _probe = New ExchangeCertificateInfo(_pinnedFromString, False)
                    _trusted = True
                    _pnlCert.Visible = True
                    _txtFingerprint.Text = _pinnedFromString
                    _lblCertNote.Text = ExchangeJoinText.PinNote()
                    _btnTrust.Visible = False
                    _btnCheck.Visible = False
                    _btnSignIn.Visible = True
                End If
        End Select
        FitAccountToWorkingArea()
    End Sub

    Private Sub OnPasteString(sender As Object, e As EventArgs)
        Try
            Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeClipboard()
            If decoded.Succeeded Then
                _txtString.Text = decoded.Text
            Else
                _lblString.ForeColor = CollapsibleSection.AttentionColor
                _lblString.Text = decoded.FailureMessage
                _lblString.Visible = True
            End If
        Catch ex As Exception When TypeOf ex Is System.Runtime.InteropServices.ExternalException OrElse TypeOf ex Is Threading.ThreadStateException
            AppFileLogger.WriteLine("Exchange sign-in: the clipboard could not be read")
        End Try
    End Sub

    Private Sub OnOpenStringFile(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = ExchangeJoinText.OpenFileFilter(), .CheckFileExists = True, .Multiselect = False}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If ExchangeQrDecoder.IsImageFile(dlg.FileName) Then
                Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeFile(dlg.FileName)
                If decoded.Succeeded Then
                    _txtString.Text = decoded.Text
                    Return
                Else
                    _lblString.ForeColor = CollapsibleSection.AttentionColor
                    _lblString.Text = decoded.FailureMessage
                    _lblString.Visible = True
                    Return
                End If
            End If
            Dim bytes As Byte()
            Try
                Using fs As New IO.FileStream(dlg.FileName, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                    Dim buffer(ExchangeConnectionString.MaxFile) As Byte
                    Dim total As Integer = 0
                    While total < buffer.Length
                        Dim n As Integer = fs.Read(buffer, total, buffer.Length - total)
                        If n <= 0 Then Exit While
                        total += n
                    End While
                    bytes = New Byte(total - 1) {}
                    Array.Copy(buffer, bytes, total)
                End Using
            Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
                bytes = New Byte() {}
            End Try
            Dim input As ExchangeConnectInput = ExchangeConnectInput.ClassifyFile(bytes)
            If input.Kind = ExchangeConnectInputKind.Refused Then
                _lblString.ForeColor = CollapsibleSection.AttentionColor
                _lblString.Text = ExchangeJoinText.InputRefusal(input.Reason, input.Looks)
                _lblString.Visible = True
                Return
            End If
            _txtString.Text = System.Text.Encoding.UTF8.GetString(bytes).Trim().TrimStart(ChrW(65279)).Trim()
        End Using
    End Sub

    Private Sub OnScanString(sender As Object, e As EventArgs)
        If _busy Then Return
        Using scan As New Camera_Scan_Form()
            If scan.ShowDialog(Me) <> DialogResult.OK OrElse String.IsNullOrWhiteSpace(scan.DecodedText) Then Return
            _txtString.Text = scan.DecodedText
        End Using
    End Sub

    Private Sub OnModeChanged(sender As Object, e As EventArgs)
        Dim byPassword As Boolean = _rbPassword.Checked
        _lblSecret.Text = If(byPassword, Localization.T("Пароль:"), Localization.T("Код сопряжения:"))
        _txtSecret.Text = ""
        _txtSecret.UseSystemPasswordChar = byPassword AndAlso Not _chkShow.Checked
        ' the whole row hides: a caption left behind by a hidden box would be the old bug in a new place
        OptionRows.RowOf(_chkShow).Visible = byPassword
    End Sub

    ''' <summary>The register mode: the password is the account's NEW one, typed twice, and the button says what happens. A
    ''' registration is offered only while the string carries the invitation - typing an address stays a sign-in.</summary>
    Private Sub ApplyRegisterMode(invitationCode As String)
        Dim registering As Boolean = invitationCode.Length > 0
        If _register = registering AndAlso String.Equals(_invitationCode, invitationCode, StringComparison.Ordinal) Then Return
        _register = registering
        _invitationCode = invitationCode
        _rbPassword.Checked = True
        _txtSecret.Text = ""
        _txtSecretRepeat.Text = ""
        _rbPassword.Visible = Not registering
        _rbCode.Visible = Not registering
        _lblSecret.Text = If(registering, Localization.T("Пароль учётной записи:"), Localization.T("Пароль:"))
        _lblSecretRepeat.Visible = registering
        _txtSecretRepeat.Visible = registering
        _lblPolicy.Visible = registering
        _btnSignIn.Text = If(registering, Localization.T("Создать учётную запись"), Localization.T("Войти"))
    End Sub

    ' --- first contact ---

    Private Async Function CheckCertificateInternalAsync() As Task(Of Boolean)
        If _busy Then Return False
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(_txtServer.Text, address) Then
            ShowResult(Localization.T("Проверьте адрес (хост:порт), логин и пароль или код."))
            Return False
        End If
        ShowResult(Localization.T("Подключение.."))
        _lblResult.ForeColor = ThemeService.Palette.TextMuted
        SetBusy(True)
        Try
            Dim target As String = _txtServer.Text.Trim()
            Dim probe As ExchangeCertificateInfo = Await _agent.ProbeCertificateAsync(target, _cts.Token)
            If IsDisposed OrElse Not String.Equals(target, _txtServer.Text.Trim(), StringComparison.OrdinalIgnoreCase) Then Return False
            _probe = probe
            ShowCertificate()
            Return True
        Catch ex As ExchangeConnectException
            _probe = Nothing
            _lblResult.ForeColor = CollapsibleSection.AttentionColor
            _lastDiagnosisMessage = ConnectFailureRow(ex.Failure)
            ShowResult(ExchangeDeviceText.TableText(_lastDiagnosisMessage))
            If _btnDiagnose IsNot Nothing Then
                _btnDiagnose.Text = ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCode)
                _btnDiagnose.Visible = True
            End If
            Return False
        Catch ex As OperationCanceledException
            ' the window closed
            Return False
        Finally
            If Not IsDisposed Then SetBusy(False)
        End Try
    End Function

    Private Async Sub OnCheckClicked(sender As Object, e As EventArgs)
        Await CheckCertificateInternalAsync()
    End Sub

    ''' <summary>The row of the common message table (item CZ) a failed first contact reads as: the server did not answer is `unanswered`
    ''' (the `timeout` row belongs to a cast and its other device), a handshake that failed is `tls-failed`, anything else cannot be reached.</summary>
    Friend Shared Function ConnectFailureRow(failure As ExchangeConnectFailure) As ConnectionDiagnosisMessage
        Select Case failure
            Case ExchangeConnectFailure.TlsFailed
                Return ConnectionDiagnosisMessages.TlsFailed
            Case ExchangeConnectFailure.TimedOut
                Return ConnectionDiagnosisMessages.Unanswered
            Case Else
                Return ConnectionDiagnosisMessages.UnreachableServer
        End Select
    End Function

    Private Sub ShowCertificate()
        _pnlCert.Visible = True
        _txtFingerprint.Text = _probe.Fingerprint
        If _probe.PlatformValid Then
            ' the system trusts it: recorded as such, nothing to pin, nothing to compare
            _lblCertNote.Text = Localization.T("Сертификат подтверждён системой, закреплять его не нужно.")
            _btnTrust.Visible = False
            _trusted = True
            Me.AcceptButton = _btnSignIn
        Else
            _lblCertNote.Text = Localization.T("Сверьте его с отпечатком, который дал вам оператор сервера.")
            _btnTrust.Visible = True
            _trusted = False
            Me.AcceptButton = Nothing
        End If
        _btnSignIn.Visible = True
        ShowResult("")
        FitAccountToWorkingArea()
    End Sub

    ''' <summary>The explicit act: only this accepts a certificate the system does not know.</summary>
    Private Sub OnTrustClicked(sender As Object, e As EventArgs)
        If _probe Is Nothing Then Return
        _trusted = True
        _btnTrust.Visible = False
        _btnSignIn.Visible = True
        Me.AcceptButton = _btnSignIn
        _btnSignIn.Focus()
        ShowResult("")
    End Sub

    ' --- sign in ---

    Private Async Sub OnSignInClicked(sender As Object, e As EventArgs)
        If _busy Then Return
        If _txtLogin.Text.Trim().Length = 0 OrElse _txtSecret.Text.Length = 0 Then
            ShowResult(Localization.T("Проверьте адрес (хост:порт), логин и пароль или код."))
            Return
        End If
        If _register AndAlso Not ValidateRegistrationPassword() Then Return

        If _probe Is Nothing Then
            Dim checked As Boolean = Await CheckCertificateInternalAsync()
            If Not checked OrElse IsDisposed Then Return
        End If

        If Not _trusted Then
            ShowResult(Localization.T("Сертификат сервера не принят. Проверьте его и подтвердите отпечаток."))
            If _btnTrust IsNot Nothing AndAlso _btnTrust.Visible Then
                _btnTrust.Focus()
            End If
            Return
        End If

        ' one publisher (rule 9): a join while another server is held asks to replace it, and replacing leaves the first one cleanly
        Dim held As ExchangeDeviceStatus = _agent.GetStatus()
        If held.Configured AndAlso Not (String.Equals(held.Server, _txtServer.Text.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
                                        String.Equals(held.Login, _txtLogin.Text.Trim(), StringComparison.OrdinalIgnoreCase)) Then
            If ManagerBox.Show(Me, ExchangeJoinText.ReplaceQuestion(held.Server, held.Login, _txtServer.Text.Trim()), ExchangeDeviceText.GroupTitle(),
                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
            SetBusy(True)
            Try
                Await _agent.LeaveAccountAsync(_cts.Token)
            Catch ex As OperationCanceledException
                Return
            Finally
                If Not IsDisposed Then SetBusy(False)
            End Try
        End If

        ' 226 rule 1: in the register mode the password is the account's NEW one, the twins must match and the policy must pass
        Dim result As ExchangeEnrollResult = Nothing
        If _register Then
            Dim register As New ExchangeRegisterRequest With {
                .Server = _txtServer.Text.Trim(), .Login = _txtLogin.Text.Trim(), .DeviceName = _txtName.Text,
                .PlatformValid = _probe.PlatformValid, .AcceptedFingerprint = _probe.Fingerprint,
                .InvitationCode = _invitationCode, .Password = _txtSecret.Text}
            _txtSecret.Text = ""
            _txtSecretRepeat.Text = ""
            _lblResult.ForeColor = ThemeService.Palette.TextMuted
            ShowResult(Localization.T("Подключение.."))
            SetBusy(True)
            Try
                result = Await _agent.RegisterAsync(register, _cts.Token)
            Catch ex As OperationCanceledException
                Return
            Finally
                register.Wipe()
                If Not IsDisposed Then SetBusy(False)
            End Try
        Else
            Dim request As New ExchangeEnrollRequest With {
                .Server = _txtServer.Text.Trim(), .Login = _txtLogin.Text.Trim(), .DeviceName = _txtName.Text,
                .PlatformValid = _probe.PlatformValid, .AcceptedFingerprint = _probe.Fingerprint}
            If _rbPassword.Checked Then request.Password = _txtSecret.Text Else request.PairingCode = _txtSecret.Text
            ' the field is cleared as soon as the value has left it - whatever the outcome
            _txtSecret.Text = ""
            _lblResult.ForeColor = ThemeService.Palette.TextMuted
            ShowResult(Localization.T("Подключение.."))
            SetBusy(True)
            Try
                result = Await _agent.EnrollAsync(request, _cts.Token)
            Catch ex As OperationCanceledException
                Return
            Finally
                request.Wipe()
                If Not IsDisposed Then SetBusy(False)
            End Try
        End If

        If result.Outcome = ExchangeEnrollOutcome.Enrolled Then
            ' publishing is the person's explicit choice, made in this dialog; it is recorded only now that there is an account
            If _chkPublish.Checked AndAlso _agent.Publisher IsNot Nothing Then
                _agent.Publisher.Settings.SetPublishShares(True)
                _agent.Publisher.Notify()
            End If
            If _chkStart.Checked Then
                Try
                    AutostartManager.SetEnabled(True)
                Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException
                    AppFileLogger.WriteLine("Exchange sign-in: the autostart choice could not be applied - " & ex.GetType().Name)
                End Try
            End If
            Me.DialogResult = DialogResult.OK
            Return
        End If

        ' the string named a leaf and the server presented another: both fingerprints, nothing pinned, no password sent (rule 3)
        If result.Outcome = ExchangeEnrollOutcome.PinMismatch AndAlso _pinnedFromString.Length > 0 Then
            MessageBox.Show(Me, ExchangeJoinText.PinMismatch(_pinnedFromString, result.Fingerprint), ExchangeDeviceText.GroupTitle(), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
        ' a certificate that changed between the check and the sign-in is a new first contact
        If result.Outcome = ExchangeEnrollOutcome.PinMismatch OrElse result.Outcome = ExchangeEnrollOutcome.Untrusted Then
            _pinnedFromString = ""
            _probe = Nothing
            _trusted = False
            _pnlCert.Visible = False
            Me.AcceptButton = Nothing
        End If
        ' the row of the common message table the failure reads as (item CZ): the diagnosis walks it, and the line below says its words
        Dim oneTimeCode As Boolean = Not _register AndAlso _rbCode.Checked
        _lastDiagnosisMessage = ExchangeDeviceText.EnrollRow(result, oneTimeCode)
        If _lastDiagnosisMessage Is Nothing Then
            _lastDiagnosisMessage = If(result.Outcome = ExchangeEnrollOutcome.Untrusted,
                                       ConnectionDiagnosisMessages.CertificateChanged, ConnectionDiagnosisMessages.UnreachableServer)
        End If

        _lblResult.ForeColor = CollapsibleSection.AttentionColor
        ShowResult(If(_register, ExchangeJoinText.RegisterFailure(result), ExchangeDeviceText.EnrollFailure(result, oneTimeCode)))
        If _btnDiagnose IsNot Nothing Then
            _btnDiagnose.Text = ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCode)
            _btnDiagnose.Visible = True
        End If
        _txtSecret.Focus()
    End Sub

    Private Sub OnDiagnoseClicked(sender As Object, e As EventArgs)
        Dim serverText As String = If(_txtServer IsNot Nothing, _txtServer.Text.Trim(), "")
        Dim host As String = serverText
        Dim port As Integer = 443
        If host.Contains(":"c) Then
            Dim parts = host.Split(":"c)
            host = parts(0)
            Integer.TryParse(parts(1), port)
        End If
        Dim msg As ConnectionDiagnosisMessage = If(_lastDiagnosisMessage, ConnectionDiagnosisMessages.UnreachableServer)
        Dim ctx As New ConnectionDiagnosisContext(host, port, ConnectionDiagnosisTarget.Server, msg)
        Dim ignored = Connection_Diagnosis_Dialog.RunAndShowAsync(Me, ctx)
    End Sub

    Private Function ValidateRegistrationPassword() As Boolean
        _lblResult.ForeColor = CollapsibleSection.AttentionColor
        If Not String.Equals(_txtSecret.Text, _txtSecretRepeat.Text, StringComparison.Ordinal) Then
            ShowResult(Localization.T("Пароли в двух полях не совпали."))
            _txtSecret.Focus()
            Return False
        End If
        Dim policy As ExchangePasswordPolicy.Verdict = ExchangePasswordPolicy.Check(_txtSecret.Text)
        If policy <> ExchangePasswordPolicy.Verdict.Accepted Then
            ShowResult(ExchangeJoinText.PolicyFailure(policy))
            _txtSecret.Focus()
            Return False
        End If
        Return True
    End Function

End Class
