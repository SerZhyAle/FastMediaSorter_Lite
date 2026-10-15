#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "Add from an exchange server.." (ticket 187 T-4): one window that takes a connection string (pasted, or opened from a .fmsexch file) or a
''' typed host:port, then a login and a password, and joins the account as one of the viewer's own entries - with no other program on the
''' machine (rule 5). Everything it decides is <see cref="ExchangeConnectFlow"/>'s; this window is its skin.
'''
''' Rules held here, from the proposal and its grammar companion:
'''   * the destination (host:port) is on screen BEFORE the password field takes focus; the string's name `n` is a label of unverified origin,
'''     never the title and never in place of the address;
'''   * a string that arrives from outside the window (a dropped file, a pasted line) FILLS the form and stops: no connection is made and
'''     no byte is sent until the person pressed Connect; the login hint `u` pre-fills the login and never submits;
'''   * the password lives in this window's field and in the one `enroll` frame; the field is cleared on every outcome and on close;
'''   * an unknown certificate asks once, with the fingerprint shown, and the default answer is No.
''' Built in code from 96-dpi positions; DialogLayout scales it (last line of the builder) and reflows wrapped captions in OnLoad.
''' </summary>
Friend NotInheritable Class Exchange_Connect_Form
    Inherits Form

    Private ReadOnly intro As Label
    Private ReadOnly inputLabel As Label
    Private ReadOnly inputBox As TextBox
    Private ReadOnly pasteButton As Button
    Private ReadOnly openButton As Button
    Private ReadOnly destination As Label
    Private ReadOnly loginLabel As Label
    Private ReadOnly loginBox As TextBox
    Private ReadOnly passwordLabel As Label
    Private ReadOnly passwordBox As TextBox
    Private ReadOnly revealBox As CheckBox
    ''' <summary>226 rule 1: registration by invitation asks for the password TWICE and states the server's policy in words
    ''' before the first refusal. Both live only in the register mode.</summary>
    Private ReadOnly password2Label As Label
    Private ReadOnly password2Box As TextBox
    Private ReadOnly policyNote As Label
    Private ReadOnly statusLabel As Label
    Private ReadOnly connectButton As Button
    Private ReadOnly cancelButton_ As Button
    Private ReadOnly _ports As ExchangeConsumerPorts
    Private ReadOnly _pinOfEntry As String
    Private ReadOnly _cts As New CancellationTokenSource()
    Private script_Font As Font
    Private _busy As Boolean
    Private _added As ExchangeEntry
    Private _loginHintApplied As String = ""
    ''' <summary>True while the input is an INVITATION string (226 rule 1): the window asks for a login and a password typed
    ''' twice and registers instead of signing in.</summary>
    Private _register As Boolean
    Private ReadOnly layoutBaseline As New Dictionary(Of Control, Rectangle)()

    ''' <summary>The entry that was added (or signed in again); Nothing when the window was closed without one.</summary>
    Friend ReadOnly Property Added As ExchangeEntry
        Get
            Return _added
        End Get
    End Property

    ''' <param name="initialText">What arrived from outside (a dropped file's text, a pasted string): it fills the field and nothing is sent.</param>
    ''' <param name="signInAgain">An existing entry: its address and login are filled and its own pin is the trust anchor.</param>
    Friend Sub New(initialText As String, signInAgain As ExchangeEntry, Optional ports As ExchangeConsumerPorts = Nothing)
        _ports = If(ports, ExchangeConsumerPortsFactory.Create())
        _pinOfEntry = If(signInAgain Is Nothing, "", signInAgain.Pin)
        Me.Text = ExchangeEntryText.ConnectTitle()
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(520, 330)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        intro = New Label With {.Name = "xc_Intro", .AutoSize = False, .Location = New Point(14, 12), .Size = New Size(492, 50),
                                .Text = ExchangeEntryText.ConnectIntro()}
        inputLabel = New Label With {.Name = "xc_InputLabel", .AutoSize = False, .Location = New Point(14, 68), .Size = New Size(492, 16),
                                     .Text = ExchangeEntryText.InputLabel()}
        inputBox = New TextBox With {.Name = "xc_Input", .Location = New Point(14, 86), .Size = New Size(492, 24), .AccessibleName = ExchangeEntryText.InputLabel()}
        AddHandler inputBox.TextChanged, Sub() OnInputChanged()
        pasteButton = New Button With {.Name = "xc_Paste", .Location = New Point(14, 114), .Size = New Size(90, 26), .Text = ExchangeEntryText.PasteButton()}
        AddHandler pasteButton.Click, AddressOf OnPaste
        ButtonGlyphs.Apply(pasteButton, "action.paste")
        openButton = New Button With {.Name = "xc_OpenFile", .Location = New Point(110, 114), .Size = New Size(130, 26), .Text = ExchangeEntryText.OpenFileButton()}
        AddHandler openButton.Click, AddressOf OnOpenFile
        ButtonGlyphs.Apply(openButton, "action.choose-file")

        destination = New Label With {.Name = "xc_Destination", .AutoSize = False, .Location = New Point(14, 146), .Size = New Size(492, 34),
                                      .ForeColor = SystemColors.GrayText, .Text = ""}
        loginLabel = New Label With {.Name = "xc_LoginLabel", .AutoSize = False, .Location = New Point(14, 186), .Size = New Size(110, 16), .Text = ExchangeEntryText.LoginLabel()}
        loginBox = New TextBox With {.Name = "xc_Login", .Location = New Point(130, 183), .Size = New Size(376, 24), .AccessibleName = ExchangeEntryText.LoginLabel()}
        passwordLabel = New Label With {.Name = "xc_PasswordLabel", .AutoSize = False, .Location = New Point(14, 216), .Size = New Size(110, 16), .Text = Localization.T("Пароль:")}
        passwordBox = New TextBox With {.Name = "xc_Password", .Location = New Point(130, 213), .Size = New Size(376, 24), .UseSystemPasswordChar = True,
                                        .AccessibleName = Localization.T("Пароль:")}
        revealBox = New CheckBox With {.Name = "xc_Reveal", .AutoSize = True, .Location = New Point(130, 241), .Text = Localization.T("Показать пароль")}
        AddHandler revealBox.CheckedChanged, Sub()
                                                  passwordBox.UseSystemPasswordChar = Not revealBox.Checked
                                                  password2Box.UseSystemPasswordChar = Not revealBox.Checked
                                              End Sub
        password2Label = New Label With {.Name = "xc_Password2Label", .AutoSize = False, .Location = New Point(14, 267), .Size = New Size(110, 16),
                                         .Text = ExchangeEntryText.PasswordTwiceLabel(), .Visible = False}
        password2Box = New TextBox With {.Name = "xc_Password2", .Location = New Point(130, 264), .Size = New Size(376, 24), .UseSystemPasswordChar = True,
                                         .AccessibleName = ExchangeEntryText.PasswordTwiceLabel(), .Visible = False}
        AddHandler password2Box.TextChanged, Sub() statusLabel.Text = ""
        policyNote = New Label With {.Name = "xc_Policy", .AutoSize = False, .Location = New Point(14, 291), .Size = New Size(492, 18),
                                     .ForeColor = SystemColors.GrayText, .Text = ExchangeEntryText.PasswordPolicyNote(), .Visible = False}

        statusLabel = New Label With {.Name = "xc_Status", .AutoSize = False, .Location = New Point(14, 268), .Size = New Size(492, 18),
                                      .ForeColor = Color.Firebrick, .Text = ""}
        connectButton = New Button With {.Name = "xc_Connect", .Location = New Point(296, 292), .Size = New Size(120, 28), .Text = Localization.T("Подключиться")}
        AddHandler connectButton.Click, AddressOf OnConnect
        ButtonGlyphs.Apply(connectButton, "action.confirm", ButtonGlyphRole.Affirmative)
        cancelButton_ = New Button With {.Name = "xc_Cancel", .Location = New Point(424, 292), .Size = New Size(82, 28), .Text = Localization.T("Отмена"),
                                         .DialogResult = DialogResult.Cancel}
        ButtonGlyphs.Apply(cancelButton_, "action.cancel", ButtonGlyphRole.Dismissing)

        Me.Controls.AddRange(New Control() {intro, inputLabel, inputBox, pasteButton, openButton, destination, loginLabel, loginBox,
                                            passwordLabel, passwordBox, revealBox, password2Label, password2Box, policyNote, statusLabel, connectButton, cancelButton_})
        Me.AcceptButton = connectButton
        Me.CancelButton = cancelButton_
        For Each child As Control In Me.Controls
            layoutBaseline(child) = child.Bounds
        Next

        If signInAgain IsNot Nothing Then
            inputBox.Text = signInAgain.Server
            inputBox.ReadOnly = True
            pasteButton.Enabled = False
            openButton.Enabled = False
            loginBox.Text = signInAgain.Login
            loginBox.ReadOnly = True
        ElseIf Not String.IsNullOrWhiteSpace(initialText) Then
            inputBox.Text = initialText.Trim()
        End If
        OnInputChanged()

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        ' the destination is on screen first; focus goes to the field that is still empty, the password last
        If inputBox.Text.Length = 0 Then
            inputBox.Focus()
        ElseIf loginBox.Text.Length = 0 AndAlso Not loginBox.ReadOnly Then
            loginBox.Focus()
        Else
            passwordBox.Focus()
        End If
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        passwordBox.Text = ""
        password2Box.Text = ""
        Try : _cts.Cancel() : Catch ex As ObjectDisposedException : End Try
        MyBase.OnFormClosed(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            script_Font?.Dispose()
            _cts.Dispose()
        End If
    End Sub

    ' ---- the input -----------------------------------------------------------------------------------------

    ''' <summary>The destination and the hint are redrawn from what is in the field; nothing is sent.</summary>
    Private Sub OnInputChanged()
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(inputBox.Text)
        statusLabel.Text = ""
        Select Case input.Kind
            Case ExchangeConnectInputKind.ConnectionString, ExchangeConnectInputKind.Address
                Dim text As String = ExchangeEntryText.Destination(input.Value.Endpoint)
                If input.Value.Name.Length > 0 Then text &= vbLf & ExchangeEntryText.ServerLabel(input.Value.Name)
                destination.ForeColor = SystemColors.GrayText
                destination.Text = text
                ' `u` pre-fills the login and nothing else; the person's own typing is never overwritten
                If input.Value.Login.Length > 0 AndAlso Not loginBox.ReadOnly AndAlso
                   (loginBox.Text.Length = 0 OrElse String.Equals(loginBox.Text, _loginHintApplied, StringComparison.Ordinal)) Then
                    loginBox.Text = input.Value.Login
                    _loginHintApplied = input.Value.Login
                End If
            Case ExchangeConnectInputKind.Refused
                destination.ForeColor = Color.Firebrick
                destination.Text = ExchangeEntryText.InputRefusal(input.Reason, input.Looks)
            Case Else
                destination.Text = ""
        End Select
        ApplyMode(input.HasInvitation)
    End Sub

    ''' <summary>226 rule 1: the registration is offered only where the string is an invitation. The mode changes the
    ''' intro, asks for the password twice, states the server's policy in words and renames the button; the layout is
    ''' the window's own in both modes (nothing is sent in either until the button is pressed).</summary>
    Private Sub ApplyMode([register] As Boolean)
        If _register = register AndAlso _laidOut Then Return
        _register = register
        If _laidOut Then layoutScale = CSng(inputBox.Width) / layoutBaseline(inputBox).Width
        ' Reflow starts from authored bounds. Reusing a previous language/mode's expanded rows accumulates offsets.
        For Each row As KeyValuePair(Of Control, Rectangle) In layoutBaseline
            Dim bounds As Rectangle = row.Value
            row.Key.SetBounds(LayoutUnit(bounds.X), LayoutUnit(bounds.Y), LayoutUnit(bounds.Width), LayoutUnit(bounds.Height))
        Next
        password2Label.Visible = register
        password2Box.Visible = register
        policyNote.Visible = register
        intro.Text = If(register, ExchangeEntryText.RegisterIntro(), ExchangeEntryText.ConnectIntro())
        connectButton.Text = If(register, ExchangeEntryText.RegisterButton(), ExchangeEntryText.ConnectButton())
        If register Then
            statusLabel.SetBounds(LayoutUnit(14), LayoutUnit(313), LayoutUnit(492), LayoutUnit(18))
            connectButton.SetBounds(LayoutUnit(296), LayoutUnit(337), LayoutUnit(120), LayoutUnit(28))
            cancelButton_.SetBounds(LayoutUnit(424), LayoutUnit(337), LayoutUnit(82), LayoutUnit(28))
            ClientSize = New Size(LayoutUnit(520), LayoutUnit(375))
        Else
            statusLabel.SetBounds(LayoutUnit(14), LayoutUnit(268), LayoutUnit(492), LayoutUnit(18))
            connectButton.SetBounds(LayoutUnit(296), LayoutUnit(292), LayoutUnit(120), LayoutUnit(28))
            cancelButton_.SetBounds(LayoutUnit(424), LayoutUnit(292), LayoutUnit(82), LayoutUnit(28))
            ClientSize = New Size(LayoutUnit(520), LayoutUnit(330))
        End If
        DialogLayout.FitContent(Me)
    End Sub

    Private _laidOut As Boolean
    Private layoutScale As Single = 1.0F

    Private Function LayoutUnit(value As Integer) As Integer
        Return CInt(Math.Round(value * layoutScale))
    End Function

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        _laidOut = True
        DialogLayout.FitContent(Me)
    End Sub

    Private Sub OnPaste(sender As Object, e As EventArgs)
        Try
            Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeClipboard()
            If decoded.Succeeded Then
                inputBox.Text = decoded.Text
            Else
                destination.ForeColor = Color.Firebrick
                destination.Text = decoded.FailureMessage
            End If
        Catch ex As Exception When TypeOf ex Is System.Runtime.InteropServices.ExternalException OrElse TypeOf ex Is Threading.ThreadStateException
            statusLabel.Text = Localization.T("Не удалось прочитать буфер обмена.")
        End Try
    End Sub

    Private Sub OnOpenFile(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = ExchangeEntryText.OpenFileFilter(), .CheckFileExists = True, .Multiselect = False}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If ExchangeQrDecoder.IsImageFile(dlg.FileName) Then
                Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeFile(dlg.FileName)
                If decoded.Succeeded Then
                    inputBox.Text = decoded.Text
                    Return
                Else
                    destination.ForeColor = Color.Firebrick
                    destination.Text = decoded.FailureMessage
                    Return
                End If
            End If
            Dim bytes As Byte() = ReadBounded(dlg.FileName)
            Dim input As ExchangeConnectInput = ExchangeConnectInput.ClassifyFile(bytes)
            If input.Kind = ExchangeConnectInputKind.Refused Then
                destination.ForeColor = Color.Firebrick
                destination.Text = ExchangeEntryText.InputRefusal(input.Reason, input.Looks)
                Return
            End If
            ' a file's content and a pasted text are one thing (the contract's file form): the field takes the line
            inputBox.Text = Encoding.UTF8.GetString(bytes).Trim().TrimStart(ChrW(65279)).Trim()
        End Using
    End Sub

    ''' <summary>At most MaxFile + 1 bytes are read: a file of any size is refused as too long without reading it all.</summary>
    Friend Shared Function ReadBounded(path As String) As Byte()
        Try
            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim buffer(ExchangeConnectionString.MaxFile) As Byte
                Dim total As Integer = 0
                While total < buffer.Length
                    Dim n As Integer = fs.Read(buffer, total, buffer.Length - total)
                    If n <= 0 Then Exit While
                    total += n
                End While
                ReDim Preserve buffer(Math.Max(0, total - 1))
                Return If(total = 0, New Byte() {}, buffer)
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return New Byte() {}
        End Try
    End Function

    ' ---- connecting -----------------------------------------------------------------------------------------

    Private Sub SetBusy(value As Boolean)
        _busy = value
        connectButton.Enabled = Not value
        inputBox.Enabled = Not value
        passwordBox.Enabled = Not value
        password2Box.Enabled = Not value
        loginBox.Enabled = Not value
        pasteButton.Enabled = Not value AndAlso Not inputBox.ReadOnly
        openButton.Enabled = Not value AndAlso Not inputBox.ReadOnly
        revealBox.Enabled = Not value
        If value Then statusLabel.ForeColor = SystemColors.GrayText : statusLabel.Text = ExchangeEntryText.Connecting()
    End Sub

    Private Async Sub OnConnect(sender As Object, e As EventArgs)
        If _busy Then Return
        SetBusy(True)
        Dim result As ExchangeConnectResult = Nothing
        Try
            Dim typed As String = passwordBox.Text
            Dim typedAgain As String = password2Box.Text
            passwordBox.Text = ""                                     ' the fields are empty before the first byte leaves
            password2Box.Text = ""
            If _register Then
                ' the twins must match and the policy must pass before anything is sent (226 rule 1)
                If Not String.Equals(typed, typedAgain, StringComparison.Ordinal) Then
                    statusLabel.ForeColor = Color.Firebrick
                    statusLabel.Text = ExchangeEntryText.PasswordsDiffer()
                    passwordBox.Focus()
                    Return
                End If
                Dim verdict As Integer = ExchangePasswordVerdict.Of(typed)
                If verdict >= 0 Then
                    statusLabel.ForeColor = Color.Firebrick
                    statusLabel.Text = ExchangeEntryText.PasswordPolicyFailure(verdict)
                    passwordBox.Focus()
                    Return
                End If
                result = Await ExchangeConnectFlow.RegisterAsync(inputBox.Text, loginBox.Text, typed, "", "", _ports,
                                                                AddressOf ConfirmTrust, _cts.Token)
            Else
                result = Await ExchangeConnectFlow.RunAsync(inputBox.Text, loginBox.Text, typed, _pinOfEntry, "", _ports,
                                                            AddressOf ConfirmTrust, _cts.Token)
            End If
        Catch ex As OperationCanceledException
            Return
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException
            AppFileLogger.WriteLine("Exchange: adding a server failed - " & ex.GetType().Name)
        Finally
            If Not IsDisposed Then SetBusy(False)
        End Try
        If result Is Nothing OrElse IsDisposed Then Return

        statusLabel.ForeColor = Color.Firebrick
        Select Case result.Kind
            Case ExchangeConnectResultKind.Added
                _added = result.Entry
                DialogResult = DialogResult.OK
                Close()
            Case ExchangeConnectResultKind.InputRefused
                statusLabel.Text = ExchangeEntryText.InputRefusal(result.Input.Reason, result.Input.Looks)
            Case ExchangeConnectResultKind.PinMismatch
                ' both fingerprints, nothing pinned, no password sent: a message that must be read, not a line that fades
                ViewerBox.Show(Me, ExchangeEntryText.PinMismatch(result.Input.Value.Endpoint, result.Expected, result.Seen), ExchangeEntryText.ConnectTitle(),
                               MessageBoxButtons.OK, MessageBoxIcon.Warning)
                statusLabel.Text = ""
            Case ExchangeConnectResultKind.TrustDeclined
                statusLabel.Text = ExchangeEntryText.EnrollFailure(ExchangeConsumerEnrollOutcome.Untrusted, ExchangeRefusal.Unknown)
            Case ExchangeConnectResultKind.Cancelled
                statusLabel.Text = ""
            Case Else
                statusLabel.Text = If(_register, ExchangeEntryText.RegisterFailure(result.Enroll, result.Refusal),
                                       ExchangeEntryText.EnrollFailure(result.Enroll, result.Refusal))
        End Select
        If Not IsDisposed AndAlso passwordBox.Enabled Then passwordBox.Focus()
    End Sub

    ''' <summary>The question of an unknown certificate: the fingerprint is shown, the answer starts at No.</summary>
    Private Function ConfirmTrust(hostPort As String, fingerprint As String) As Boolean
        If InvokeRequired Then Return CBool(Invoke(New Func(Of String, String, Boolean)(AddressOf ConfirmTrust), hostPort, fingerprint))
        Return ViewerBox.Show(Me, ExchangeEntryText.TrustQuestion(hostPort, fingerprint), ExchangeEntryText.TrustTitle(),
                              MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.Yes
    End Function

End Class

''' <summary>The production ports of the viewer's own joins.</summary>
Friend NotInheritable Class ExchangeConsumerPortsFactory
    Private Sub New()
    End Sub

    ''' <summary>The default transport, the registry's entry store, this program's version, the file logger. The log lines are fixed
    ''' sentences with an id in its log form - never a server, a login or a fingerprint.</summary>
    Friend Shared Function Create() As ExchangeConsumerPorts
        Dim version As String = ""
        Try
            Dim v As Version = Reflection.Assembly.GetExecutingAssembly().GetName().Version
            If v IsNot Nothing Then version = v.ToString()
        Catch ex As InvalidOperationException
        End Try
        Return New ExchangeConsumerPorts With {
            .Entries = ExchangeEntryStore.CreateDefault(), .ProductVersion = version,
            .DeviceNameSuffix = Localization.T("(просмотрщик)"),
            .Log = Sub(line) AppFileLogger.WriteLine(line)}
    End Function
End Class
#End If
