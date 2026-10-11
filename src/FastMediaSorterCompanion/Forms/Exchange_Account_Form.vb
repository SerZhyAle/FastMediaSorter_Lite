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
    Private _chkShow As CheckBox
    Private _txtName As TextBox
    Private _btnCheck As Button
    Private _pnlCert As FlowLayoutPanel
    Private _txtFingerprint As TextBox
    Private _lblCertNote As Label
    Private _btnTrust As Button
    Private _chkStart As CheckBox
    Private _txtString As TextBox
    Private _lblString As Label
    Private _chkPublish As CheckBox
    ''' <summary>The fingerprint the pasted string named: the trust anchor, no prompt. "" when the string had none (or there was none).</summary>
    Private _pinnedFromString As String = ""
    Private ReadOnly _publishNow As Boolean
    Private _lblResult As Label
    Private _btnSignIn As Button
    Private _btnCancel As Button

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
        body.Controls.Add(Caption(ExchangeJoinText.StringLabel()))
        _txtString = New TextBox With {.Width = ContentWidth, .Margin = New Padding(0, 0, 0, 2), .AccessibleName = ExchangeJoinText.StringLabel()}
        AddHandler _txtString.TextChanged, AddressOf OnStringChanged
        body.Controls.Add(_txtString)
        Dim stringButtons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Margin = New Padding(0)}
        Dim paste As Button = NewButton(ExchangeJoinText.PasteButton(), "action.paste")
        AddHandler paste.Click, AddressOf OnPasteString
        Dim openFile As Button = NewButton(ExchangeJoinText.OpenFileButton(), "action.choose-file")
        AddHandler openFile.Click, AddressOf OnOpenStringFile
        stringButtons.Controls.Add(paste)
        stringButtons.Controls.Add(openFile)
        body.Controls.Add(stringButtons)
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
        AddHandler _chkShow.CheckedChanged, Sub() _txtSecret.UseSystemPasswordChar = Not _chkShow.Checked
        body.Controls.Add(_chkShow)

        body.Controls.Add(Caption(Localization.T("Имя этого устройства:")))
        _txtName = New TextBox With {.Width = ContentWidth, .Text = Environment.MachineName, .MaxLength = 64, .Margin = New Padding(0, 0, 0, 2)}
        body.Controls.Add(_txtName)

        ' publishing is an explicit choice (170 rule 1): in words that say what it does, unticked unless the dialog was opened to publish
        _chkPublish = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 0), .Checked = _publishNow, .Text = ExchangeJoinText.PublishCheckbox()}
        body.Controls.Add(_chkPublish)
        body.Controls.Add(Note(ExchangePublishText.PublishNote()))

        ' the autostart offer: default off
        _chkStart = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 0), .Checked = False,
            .Text = Localization.TF("Запускать {0} при входе в Windows", ProductNames.ShareManager)}
        body.Controls.Add(_chkStart)
        body.Controls.Add(Note(Localization.T("Без этого другие устройства не смогут отправить сюда трансляцию, пока менеджер не запущен.")))

        _lblResult = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = CollapsibleSection.AttentionColor,
            .Margin = New Padding(0, 4, 0, 2), .Visible = False}
        body.Controls.Add(_lblResult)

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Margin = New Padding(0, 8, 0, 4)}
        _btnSignIn = NewButton(Localization.T("Войти"), "action.confirm", ButtonGlyphRole.Affirmative)
        _btnSignIn.Visible = False   ' hidden until a certificate has been shown and accepted (or the system trusts it)
        AddHandler _btnSignIn.Click, AddressOf OnSignInClicked
        _btnCancel = NewButton(Localization.T("Отмена"), "action.cancel", ButtonGlyphRole.Dismissing)
        _btnCancel.DialogResult = DialogResult.Cancel
        buttons.Controls.Add(_btnSignIn)
        buttons.Controls.Add(_btnCancel)
        body.Controls.Add(buttons)

        Me.Controls.Add(body)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ' The default is Cancel: no accept button exists until a certificate has been shown.
        Me.AcceptButton = Nothing
        Me.CancelButton = _btnCancel

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
        _txtServer.Focus()
    End Sub

    ' --- state ---

    Private Sub ShowResult(text As String)
        _lblResult.Text = text
        _lblResult.Visible = text.Length > 0
    End Sub

    Private Sub SetBusy(busy As Boolean)
        _busy = busy
        _btnCheck.Enabled = Not busy
        _btnSignIn.Enabled = Not busy
        _btnTrust.Enabled = Not busy
    End Sub

    ''' <summary>A new server text forgets the certificate that was shown for the old one.</summary>
    Private Sub OnServerChanged(sender As Object, e As EventArgs)
        _probe = Nothing
        _trusted = False
        _pnlCert.Visible = False
        _btnSignIn.Visible = False
        _btnCheck.Visible = True
        ShowResult("")
    End Sub

    ' --- the connection string ---

    Private Sub OnStringChanged(sender As Object, e As EventArgs)
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(_txtString.Text)
        _pinnedFromString = ""
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
                If input.Value.Login.Length > 0 AndAlso _txtLogin.Text.Trim().Length = 0 Then _txtLogin.Text = input.Value.Login
                _lblString.ForeColor = ThemeService.Palette.TextMuted
                _lblString.Text = ExchangeJoinText.StringRead(input.Value.Endpoint)
                _lblString.Visible = True
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
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Sub OnPasteString(sender As Object, e As EventArgs)
        Try
            If Clipboard.ContainsText() Then _txtString.Text = Clipboard.GetText().Trim()
        Catch ex As Exception When TypeOf ex Is System.Runtime.InteropServices.ExternalException OrElse TypeOf ex Is Threading.ThreadStateException
            AppFileLogger.WriteLine("Exchange sign-in: the clipboard could not be read")
        End Try
    End Sub

    Private Sub OnOpenStringFile(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = ExchangeJoinText.OpenFileFilter(), .CheckFileExists = True, .Multiselect = False}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
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

    Private Sub OnModeChanged(sender As Object, e As EventArgs)
        Dim byPassword As Boolean = _rbPassword.Checked
        _lblSecret.Text = If(byPassword, Localization.T("Пароль:"), Localization.T("Код сопряжения:"))
        _txtSecret.Text = ""
        _txtSecret.UseSystemPasswordChar = byPassword AndAlso Not _chkShow.Checked
        _chkShow.Visible = byPassword
    End Sub

    ' --- first contact ---

    Private Async Sub OnCheckClicked(sender As Object, e As EventArgs)
        If _busy Then Return
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(_txtServer.Text, address) Then
            ShowResult(Localization.T("Проверьте адрес (хост:порт), логин и пароль или код."))
            Return
        End If
        ShowResult(Localization.T("Подключение.."))
        _lblResult.ForeColor = ThemeService.Palette.TextMuted
        SetBusy(True)
        Try
            _probe = Await _agent.ProbeCertificateAsync(_txtServer.Text, _cts.Token)
            ShowCertificate()
        Catch ex As ExchangeConnectException
            _probe = Nothing
            _lblResult.ForeColor = CollapsibleSection.AttentionColor
            ShowResult(ConnectFailureText(ex.Failure))
        Catch ex As OperationCanceledException
            ' the window closed
        Finally
            If Not IsDisposed Then SetBusy(False)
        End Try
    End Sub

    Private Shared Function ConnectFailureText(failure As ExchangeConnectFailure) As String
        Select Case failure
            Case ExchangeConnectFailure.TlsFailed
                Return Localization.T("Не удалось установить защищённое соединение. Проверьте адрес и порт: сервер обмена принимает только TLS.")
            Case ExchangeConnectFailure.TimedOut
                Return Localization.T("Сервер не отвечает.")
            Case Else
                Return Localization.T("Не удалось подключиться к серверу.")
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
        Else
            _lblCertNote.Text = Localization.T("Сверьте его с отпечатком, который дал вам оператор сервера.")
            _btnTrust.Visible = True
            _trusted = False
        End If
        _btnSignIn.Visible = _trusted
        ShowResult("")
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>The explicit act: only this accepts a certificate the system does not know.</summary>
    Private Sub OnTrustClicked(sender As Object, e As EventArgs)
        If _probe Is Nothing Then Return
        _trusted = True
        _btnTrust.Visible = False
        _btnSignIn.Visible = True
        _btnSignIn.Focus()
    End Sub

    ' --- sign in ---

    Private Async Sub OnSignInClicked(sender As Object, e As EventArgs)
        If _busy OrElse _probe Is Nothing OrElse Not _trusted Then Return

        ' one publisher (rule 9): a join while another server is held asks to replace it, and replacing leaves the first one cleanly
        Dim held As ExchangeDeviceStatus = _agent.GetStatus()
        If held.Configured AndAlso Not (String.Equals(held.Server, _txtServer.Text.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
                                        String.Equals(held.Login, _txtLogin.Text.Trim(), StringComparison.OrdinalIgnoreCase)) Then
            If MessageBox.Show(Me, ExchangeJoinText.ReplaceQuestion(held.Server, held.Login, _txtServer.Text.Trim()), ExchangeDeviceText.GroupTitle(),
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

        Dim request As New ExchangeEnrollRequest With {
            .Server = _txtServer.Text.Trim(), .Login = _txtLogin.Text.Trim(), .DeviceName = _txtName.Text,
            .PlatformValid = _probe.PlatformValid, .AcceptedFingerprint = _probe.Fingerprint}
        If _rbPassword.Checked Then request.Password = _txtSecret.Text Else request.PairingCode = _txtSecret.Text
        ' the field is cleared as soon as the value has left it - whatever the outcome
        _txtSecret.Text = ""
        _lblResult.ForeColor = ThemeService.Palette.TextMuted
        ShowResult(Localization.T("Подключение.."))
        SetBusy(True)
        Dim result As ExchangeEnrollResult = Nothing
        Try
            result = Await _agent.EnrollAsync(request, _cts.Token)
        Catch ex As OperationCanceledException
            Return
        Finally
            request.Wipe()
            If Not IsDisposed Then SetBusy(False)
        End Try

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
            _btnSignIn.Visible = False
        End If
        _lblResult.ForeColor = CollapsibleSection.AttentionColor
        ShowResult(ExchangeDeviceText.EnrollFailure(result))
        _txtSecret.Focus()
    End Sub

End Class
