Option Strict On

Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "Use this PC as my server" (ticket 168 section 3.7, Q168-1): the one-person case in one dialog. A login, one button,
''' and the flow of <see cref="ExchangeServerWizard"/> - first account with a generated password, the local server
''' started, its certificate checked, this PC's own agent enrolled with that password - then the facts on screen: the
''' address, the login, the password (once) and the certificate's fingerprint.
'''
''' The password is shown once. It is masked until revealed (per dialog, never remembered), copied only through
''' <see cref="SecureClipboard.SetSecret"/> (CLIPBOARD-GUARD) and wiped from the field when the window closes; closing
''' with a password that was neither copied nor revealed asks first, the safe answer being the default - there is no
''' second chance to read it, UNLESS the person turned keeping on: then it is kept on this PC in the DPAPI file of
''' <see cref="ExchangeAccountPasswordStore"/> (never the registry, never a log), and the question is not asked.
'''
''' Hand-built, so it follows the Companion recipe: language direction first, DPI scaling as the LAST line of the
''' builder, the clamp to the working area in OnLoad.
''' </summary>
Friend NotInheritable Class Exchange_Server_Wizard_Form
    Inherits Form

    Private Const ContentWidth As Integer = 460

    Private ReadOnly _ports As IExchangeWizardPorts
    Private ReadOnly _cts As New CancellationTokenSource()
    Private _iconHandle As IntPtr
    Private _txtLogin As TextBox
    Private _lblLoginHint As Label
    Private _pnlInput As FlowLayoutPanel
    Private _txtStatement As TextBox
    Private _btnStart As Button
    Private _btnClose As Button
    Private _lblProgress As Label
    Private _lblOutcome As Label
    Private _pnlFacts As FlowLayoutPanel
    Private _txtThisPc As TextBox
    Private _txtOthers As TextBox
    Private _txtLogin2 As TextBox
    Private _txtPassword As TextBox
    Private _chkShow As CheckBox
    Private _btnCopyPassword As Button
    Private _txtFingerprint As TextBox
    Private _btnCopyFingerprint As Button
    Private _lblCopied As Label
    Private _busy As Boolean
    Private _copied As Boolean
    Private _revealed As Boolean
    Private _holdsPassword As Boolean
    Private _kept As Boolean

    ''' <summary>How the flow ended; Nothing until it has run.</summary>
    Friend ReadOnly Property Result As ExchangeWizardResult

    Friend Sub New(Optional ports As IExchangeWizardPorts = Nothing, Optional defaultLogin As String = Nothing)
        _ports = If(ports, New ExchangeWizardPorts())
        BuildUi(If(defaultLogin, ExchangeLoginRules.SuggestFrom(Environment.UserName)))
    End Sub

    Private Function Note(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 2, 0, 4), .Text = text}
    End Function

    Private Function Caption(text As String) As Label
        Return New Label With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 2), .Text = text}
    End Function

    Private Function ReadOnlyBox(Optional masked As Boolean = False) As TextBox
        Return New TextBox With {.ReadOnly = True, .Width = ContentWidth, .Margin = New Padding(0, 0, 0, 2), .UseSystemPasswordChar = masked}
    End Function

    Private Function NewButton(text As String) As Button
        Return New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
    End Function

    Private Sub BuildUi(defaultLogin As String)
        UiLanguage.ApplyTo(Me)
        Me.Text = ExchangeAdminText.WizardTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                ' nothing secret outlives the window
                Try
                    _txtPassword.Text = ""
                Catch
                End Try
                If _Result IsNot Nothing Then _Result.Wipe()
                Try : _cts.Cancel() : Catch : End Try
                Try : _cts.Dispose() : Catch : End Try
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New FlowLayoutPanel With {.Dock = DockStyle.Top, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 10, 14, 6)}

        body.Controls.Add(Note(ExchangeAdminText.WizardIntro()))

        ' what is asked: a login, and the statement the first account comes with
        _pnlInput = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0)}
        _pnlInput.Controls.Add(Caption(ExchangeAdminText.WizardLoginLabel()))
        _txtLogin = New TextBox With {.Width = ContentWidth, .Text = If(defaultLogin, ""), .Margin = New Padding(0, 0, 0, 2)}
        AddHandler _txtLogin.TextChanged, AddressOf OnLoginChanged
        _pnlInput.Controls.Add(_txtLogin)
        _lblLoginHint = Note(ExchangeAdminText.LoginHint())
        _pnlInput.Controls.Add(_lblLoginHint)
        _pnlInput.Controls.Add(Caption(ExchangeAdminText.StatementBeforeFirst()))
        _txtStatement = New TextBox With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Width = ContentWidth, .Height = 130,
            .Margin = New Padding(0, 0, 0, 4), .Text = String.Join(vbCrLf & vbCrLf, ExchangeStatement.Paragraphs()), .AccessibleName = ExchangeAdminText.StatementTitle()}
        _pnlInput.Controls.Add(_txtStatement)
        body.Controls.Add(_pnlInput)

        _lblProgress = Note("")
        _lblProgress.Visible = False
        body.Controls.Add(_lblProgress)
        _lblOutcome = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 4, 0, 4), .Visible = False}
        body.Controls.Add(_lblOutcome)

        ' the facts, once there are some
        _pnlFacts = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0), .Visible = False}
        _pnlFacts.Controls.Add(Caption(ExchangeAdminText.FactThisPc()))
        _txtThisPc = ReadOnlyBox()
        _pnlFacts.Controls.Add(_txtThisPc)
        _pnlFacts.Controls.Add(Caption(ExchangeAdminText.FactOthers()))
        _txtOthers = ReadOnlyBox()
        _pnlFacts.Controls.Add(_txtOthers)
        _pnlFacts.Controls.Add(Caption(ExchangeAdminText.FactLogin()))
        _txtLogin2 = ReadOnlyBox()
        _pnlFacts.Controls.Add(_txtLogin2)
        _pnlFacts.Controls.Add(Caption(ExchangeAdminText.FactPassword()))
        _txtPassword = ReadOnlyBox(masked:=True)
        _pnlFacts.Controls.Add(_txtPassword)
        _chkShow = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .Text = Localization.T("Показать пароль")}
        AddHandler _chkShow.CheckedChanged,
            Sub()
                _txtPassword.UseSystemPasswordChar = Not _chkShow.Checked
                If _chkShow.Checked Then _revealed = True
            End Sub
        _pnlFacts.Controls.Add(_chkShow)
        _btnCopyPassword = NewButton(ExchangeText.CopyPasswordButton())
        AddHandler _btnCopyPassword.Click, AddressOf OnCopyPassword
        _pnlFacts.Controls.Add(_btnCopyPassword)
        _pnlFacts.Controls.Add(Caption(ExchangeText.FingerprintLabel()))
        _txtFingerprint = ReadOnlyBox()
        _pnlFacts.Controls.Add(_txtFingerprint)
        _btnCopyFingerprint = NewButton(ExchangeText.CopyFingerprintButton())
        AddHandler _btnCopyFingerprint.Click, AddressOf OnCopyFingerprint
        _pnlFacts.Controls.Add(_btnCopyFingerprint)
        _lblCopied = Note("")
        _lblCopied.Visible = False
        _pnlFacts.Controls.Add(_lblCopied)
        _pnlFacts.Controls.Add(Note(ExchangeAdminText.FactFingerprintHint()))
        body.Controls.Add(_pnlFacts)

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Margin = New Padding(0, 8, 0, 4)}
        _btnStart = NewButton(ExchangeAdminText.WizardStart())
        AddHandler _btnStart.Click, AddressOf OnStartClicked
        _btnClose = NewButton(Localization.T("Закрыть"))
        AddHandler _btnClose.Click, AddressOf OnCloseClicked
        buttons.Controls.Add(_btnStart)
        buttons.Controls.Add(_btnClose)
        body.Controls.Add(buttons)
        Me.AcceptButton = _btnStart

        Me.Controls.Add(body)
        Me.AutoScroll = True
        Me.ClientSize = body.PreferredSize

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
        _txtLogin.Focus()
        _txtStatement.Select(0, 0)
    End Sub

    Private Sub OnLoginChanged(sender As Object, e As EventArgs)
        Dim bad As Boolean = _txtLogin.TextLength > 0 AndAlso ExchangeLoginRules.Check(_txtLogin.Text) <> ExchangeLoginRules.Verdict.Accepted
        _lblLoginHint.ForeColor = If(bad, CollapsibleSection.AttentionColor, ThemeService.Palette.TextMuted)
    End Sub

    ' --- the flow ---------------------------------------------------------------------------------

    Private Async Sub OnStartClicked(sender As Object, e As EventArgs)
        If _busy Then Return
        If ExchangeLoginRules.Check(_txtLogin.Text) <> ExchangeLoginRules.Verdict.Accepted Then
            _lblLoginHint.ForeColor = CollapsibleSection.AttentionColor
            _txtLogin.Focus()
            Return
        End If
        _busy = True
        _btnStart.Enabled = False
        _txtLogin.Enabled = False
        _lblOutcome.Visible = False
        _lblProgress.Visible = True
        Dim outcome As ExchangeWizardResult = Nothing
        Try
            outcome = Await ExchangeServerWizard.RunAsync(_txtLogin.Text, _ports,
                                                          Sub(s) ShowStep(s), _cts.Token)
        Catch ex As OperationCanceledException
            Return
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            AppFileLogger.LogException("Exchange wizard", ex)
            outcome = New ExchangeWizardResult With {.Outcome = ExchangeWizardOutcome.AccountFailed}
        Finally
            _busy = False
        End Try
        If IsDisposed Then Return
        ShowResult(outcome)
    End Sub

    ''' <summary>The progress callback runs on a pool thread: the window is only touched from its own.</summary>
    Private Sub ShowStep(s As ExchangeWizardStep)
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(Sub() _lblProgress.Text = ExchangeAdminText.WizardStep(s)))
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
            ' the window is closing
        End Try
    End Sub

    ''' <summary>The first account's password is kept on this PC only when the person turned keeping on. False when it was not kept
    ''' (off, or the file could not be written): the close question then stays, because the password is then gone after this window.</summary>
    Private Shared Function KeepIfChosen(login As String, password As String) As Boolean
        Dim settings As New ExchangeSettings()
        settings.Load()
        Return settings.KeepAccountPasswords AndAlso ExchangeAccountPasswordStore.CreateDefault().Save(login, password)
    End Function

    Private Sub ShowResult(r As ExchangeWizardResult)
        SuspendLayout()
        Controls(0).SuspendLayout()
        Try
            _Result = r
            _lblProgress.Visible = False
            _lblOutcome.Text = ExchangeAdminText.WizardOutcome(r)
            Dim good As Boolean = r.Outcome = ExchangeWizardOutcome.Done OrElse r.Outcome = ExchangeWizardOutcome.DoneNoAgent
            _lblOutcome.ForeColor = If(good, Color.ForestGreen, CollapsibleSection.AttentionColor)
            _lblOutcome.Visible = True
            If r.AccountCreated Then
                ' the flow does not run twice: the account exists, and its password is shown here once
                _pnlInput.Visible = False
                _btnStart.Visible = False
                _txtThisPc.Text = r.LocalAddress
                _txtOthers.Text = If(r.PublicEndpoint.Length > 0, r.PublicEndpoint, ExchangeAdminText.FactOthersUnset())
                _txtLogin2.Text = r.Login
                _txtPassword.Text = r.Password
                _holdsPassword = r.Password.Length > 0
                _kept = _holdsPassword AndAlso KeepIfChosen(r.Login, r.Password)
                _txtFingerprint.Text = r.Fingerprint
                _btnCopyFingerprint.Visible = r.Fingerprint.Length > 0
                _pnlFacts.Visible = True
                r.Wipe()
                _btnClose.Focus()
            Else
                ' nothing was created: the person can fix the login and go again
                _btnStart.Enabled = True
                _txtLogin.Enabled = True
                _txtLogin.Focus()
            End If
        Finally
            Controls(0).ResumeLayout(True)
            ResumeLayout(True)
        End Try
        ClientSize = Controls(0).PreferredSize
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    ' --- copying ---------------------------------------------------------------------------------

    Private Sub OnCopyPassword(sender As Object, e As EventArgs)
        ' ONLY through the guarded door: the privacy markers keep it out of Win+V history and the cloud clipboard
        If SecureClipboard.SetSecret(_txtPassword.Text) Then
            _copied = True
            ShowCopied(ExchangeText.PasswordCopied(), True)
        Else
            ShowCopied(ExchangeText.CopyFailed(), False)
        End If
    End Sub

    Private Sub OnCopyFingerprint(sender As Object, e As EventArgs)
        If _txtFingerprint.TextLength = 0 Then Return
        ' not a secret: a plain copy, still through the one clipboard door
        Dim data As New DataObject()
        data.SetText(_txtFingerprint.Text, TextDataFormat.UnicodeText)
        If SecureClipboard.TrySetDataObject(data) Then
            ShowCopied(ExchangeText.FingerprintCopied(), True)
        Else
            ShowCopied(ExchangeText.CopyFailed(), False)
        End If
    End Sub

    Private Sub ShowCopied(text As String, ok As Boolean)
        _lblCopied.Text = text
        _lblCopied.ForeColor = If(ok, ThemeService.Palette.TextMuted, CollapsibleSection.AttentionColor)
        _lblCopied.Visible = True
    End Sub

    ' --- closing -----------------------------------------------------------------------------------

    Private Sub OnCloseClicked(sender As Object, e As EventArgs)
        Me.Close()
    End Sub

    ''' <summary>A password that was neither copied nor even revealed would be lost for good, so closing then is asked
    ''' about (the safe answer is the default).</summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' The flow is short and bounded, and an account it already made has a password nobody has seen yet: it is not interrupted.
        If _busy AndAlso e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Return
        End If
        If _holdsPassword AndAlso Not _copied AndAlso Not _revealed AndAlso Not _kept AndAlso Not e.Cancel AndAlso e.CloseReason = CloseReason.UserClosing Then
            If MessageBox.Show(Me, ExchangeText.CloseWithoutCopyQuestion(), Me.Text, MessageBoxButtons.YesNo,
                               MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                e.Cancel = True
                Return
            End If
        End If
        MyBase.OnFormClosing(e)
    End Sub

End Class
