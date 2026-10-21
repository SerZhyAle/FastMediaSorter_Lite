Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The two small password dialogs of the "Media Exchange Server" group (ticket 142 T-4), one class, two
''' modes:
'''   * <see cref="DialogMode.Enter"/> - a masked field with the policy pre-check beside it. The reason
'''     for a refusal (too short, no upper-case letter ..) shows under the field BEFORE anything is sent;
'''     only a password that passes is returned, and the binary (which owns the policy) still has the last
'''     word.
'''   * <see cref="DialogMode.Generated"/> - the plaintext the binary just generated, shown ONCE. Masked
'''     until the person reveals it (per dialog, never remembered), copied only through
'''     <see cref="SecureClipboard.SetSecret"/> (the CLIPBOARD-GUARD privacy markers), and wiped from the
'''     field when the window closes. Closing without having copied or revealed it asks first - there is no
'''     second chance to read it, UNLESS the caller kept it (<paramref name="retrievable"/>): then the account's
'''     own "show password" button brings it back and nothing is asked. The button "set my own password" closes
'''     the dialog with <see cref="ChoseOwnPassword"/> set, and the caller opens the reset with a typed password.
'''   * <see cref="DialogMode.Kept"/> - the password the Share Manager kept on this PC (the person turned keeping
'''     on): the same masked, revealable, copyable field, read-only, with the login in the title. Nothing is
'''     asked when it closes, because it can be shown again.
'''
''' Where the plaintext lives: in memory, in this dialog, until it closes. Never the registry, never a log,
''' never a command line. A .NET string cannot be zeroed, so what is wiped is the control's copy; the
''' caller drops its own reference as soon as the dialog is shown.
'''
''' Hand-built, so it follows the Companion recipe: language direction first, DPI scaling as the LAST line
''' of the builder, the clamp to the working area in OnLoad.
''' </summary>
Friend NotInheritable Class Exchange_Password_Form
    Inherits Form

    Friend Enum DialogMode
        Enter
        Generated
        Kept
    End Enum

    Private ReadOnly _mode As DialogMode
    Private ReadOnly _login As String
    Private ReadOnly _retrievable As Boolean
    Private _iconHandle As IntPtr
    Private _txt As TextBox
    Private _chkShow As CheckBox
    Private _lblReason As Label
    Private _lblStatus As Label
    Private _btnOk As Button
    Private _btnCopy As Button
    Private _btnOwn As Button
    Private _answerRow As DialogButtonRow
    Private _copied As Boolean
    Private _revealed As Boolean
    Private _choseOwn As Boolean
    Private _entered As String = ""

    Private Const ContentWidth As Integer = 420

    ''' <summary>The password a person typed and the policy accepted, taken at the moment of OK. It is NOT read from the field:
    ''' the field is emptied when the window closes, and a caller reads this after <c>ShowDialog</c> has returned - which is
    ''' after the close. Reading the field there handed the server an empty password (the test host, 2026-10-12: every typed
    ''' reset answered weak-password). It is dropped when the dialog is disposed.</summary>
    Friend ReadOnly Property EnteredPassword As String
        Get
            Return _entered
        End Get
    End Property

    ''' <summary>True once the generated password reached the clipboard in this dialog.</summary>
    Friend ReadOnly Property WasCopied As Boolean
        Get
            Return _copied
        End Get
    End Property

    ''' <summary>The person chose "set my own password" instead: the caller opens the reset with a typed password. Valid after the dialog closed.</summary>
    Friend ReadOnly Property ChoseOwnPassword As Boolean
        Get
            Return _choseOwn
        End Get
    End Property

    ''' <param name="login">The account the password belongs to (the title of a kept password).</param>
    ''' <param name="retrievable">The password is kept on this PC, so closing it unseen-and-uncopied is not a loss.</param>
    Friend Sub New(mode As DialogMode, Optional generatedPassword As String = "", Optional login As String = "", Optional retrievable As Boolean = False)
        _mode = mode
        _login = If(login, "")
        _retrievable = retrievable
        BuildUi(generatedPassword)
    End Sub

    Private Sub BuildUi(generatedPassword As String)
        UiLanguage.ApplyTo(Me)
        Me.Text = If(_mode = DialogMode.Enter, ExchangeText.SetPasswordTitle(),
                     If(_mode = DialogMode.Kept, ExchangeAdminText.KeptTitle(_login), ExchangeText.GenerateTitle()))
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                ' The control's copy goes first; nothing of the plaintext outlives the window here.
                Try
                    If _txt IsNot Nothing Then _txt.Text = ""
                Catch
                End Try
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 12, 14, 6)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        body.Controls.Add(ExchangeMark.Heading(Text, 32, AddressOf CompanionAssets.OpenBundledAsset))

        If _mode <> DialogMode.Enter Then
            Dim intro As String = If(_mode = DialogMode.Kept, ExchangeAdminText.KeptIntro(),
                                     If(_retrievable, ExchangeText.GenerateIntroKept(), ExchangeText.GenerateIntro()))
            body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
                .Margin = New Padding(0, 0, 0, 8), .Text = intro})
        Else
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
                .Text = Localization.T("Пароль:")})
        End If

        _txt = New TextBox With {.Width = ContentWidth, .UseSystemPasswordChar = True, .Margin = New Padding(0, 0, 0, 4)}
        If _mode <> DialogMode.Enter Then
            _txt.ReadOnly = True
            _txt.Text = If(generatedPassword, "")
        Else
            AddHandler _txt.TextChanged, AddressOf OnPasswordChanged
        End If
        body.Controls.Add(_txt)

        _chkShow = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 4),
            .Text = Localization.T("Показать пароль")}
        AddHandler _chkShow.CheckedChanged, AddressOf OnShowChanged
        ' A boolean row (BOOLEAN-OPTION 2.0, ticket 243 T-11c-b2a): the check box keeps its field, default and handlers.
        body.Controls.Add(OptionRows.InFlow(_chkShow))

        If _mode = DialogMode.Enter Then
            body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
                .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 4, 0, 2),
                .Text = ExchangeText.PolicyHint()})
        End If

        ' The reason for a refusal, beside the field it is about. Empty (and gone) until there is one.
        _lblReason = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
            .ForeColor = CollapsibleSection.AttentionColor, .Margin = New Padding(0, 2, 0, 2), .Visible = False}
        body.Controls.Add(_lblReason)

        _lblStatus = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 2, 0, 2), .Visible = False}
        body.Controls.Add(_lblStatus)

        ' The answers ride the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4). Entering a password: Cancel
        ' first, Save last, Enter saves and Escape cancels, as before. Showing one (generated, or kept on this PC): Copy and
        ' "set my own" are commands of the window and Close is its one answer, so the commands read first, set apart, then
        ' Close; Escape and now Enter reach Close - Enter used to copy, and a command is never the default. A generated
        ' password that was neither copied nor revealed is still asked about when Close is pressed (OnFormClosing).
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xpw_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        If _mode = DialogMode.Enter Then
            _btnOk = New Button With {.Name = "xpw_Ok", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .Padding = New Padding(12, 4, 12, 4), .Text = ExchangeText.SavePasswordButton()}
            ButtonGlyphs.Apply(_btnOk, "action.save", ButtonGlyphRole.Affirmative)
            AddHandler _btnOk.Click, AddressOf OnSaveClicked
            Dim btnCancel As New Button With {.Name = "xpw_Cancel", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .Padding = New Padding(12, 4, 12, 4), .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}
            ButtonGlyphs.Apply(btnCancel, "action.cancel", ButtonGlyphRole.Dismissing)
            DpiLayout.AddAnswer(_answerRow, _btnOk, DialogButtonRole.Affirmative)
            DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing)
        Else
            _btnCopy = New Button With {.Name = "xpw_Copy", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .Padding = New Padding(12, 4, 12, 4), .Text = ExchangeText.CopyPasswordButton()}
            ButtonGlyphs.Apply(_btnCopy, "action.copy")
            AddHandler _btnCopy.Click, AddressOf OnCopyClicked
            Dim btnClose As New Button With {.Name = "xpw_Close", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .Padding = New Padding(12, 4, 12, 4), .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.OK}
            ButtonGlyphs.Apply(btnClose, "nav.close", ButtonGlyphRole.Dismissing)
            DpiLayout.AddAnswer(_answerRow, _btnCopy, DialogButtonRole.Command)
            If _mode = DialogMode.Generated Then
                ' the one way to a password of the person's own choosing, from the same screen as the generated one
                _btnOwn = New Button With {.Name = "xpw_Own", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    .Padding = New Padding(12, 4, 12, 4), .Text = ExchangeAdminText.OwnPasswordButton()}
                ButtonGlyphs.Apply(_btnOwn, "action.change-password")
                AddHandler _btnOwn.Click, AddressOf OnOwnClicked
                DpiLayout.AddAnswer(_answerRow, _btnOwn, DialogButtonRole.Command)
            End If
            DpiLayout.AddAnswer(_answerRow, btnClose, DialogButtonRole.Dismissing)
        End If

        Me.Controls.Add(body)
        ' Last in the collection: the docked row takes its edge first and reads after the field in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        OptionRows.ArrangeAll(Me)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
        _txt.Focus()
    End Sub

    ''' <summary>Reveal is per dialog and never remembered: a new dialog always opens masked.</summary>
    Private Sub OnShowChanged(sender As Object, e As EventArgs)
        _txt.UseSystemPasswordChar = Not _chkShow.Checked
        If _chkShow.Checked Then _revealed = True
    End Sub

    Private Sub OnPasswordChanged(sender As Object, e As EventArgs)
        ' Live feedback once the person has typed something; an empty field says nothing yet.
        If _txt.TextLength = 0 Then
            ShowReason("")
            Return
        End If
        ShowReason(ExchangeText.PolicyReason(ExchangePasswordPolicy.Check(_txt.Text)))
    End Sub

    Private Sub ShowReason(text As String)
        _lblReason.Text = text
        _lblReason.Visible = text.Length > 0
    End Sub

    Private Sub OnSaveClicked(sender As Object, e As EventArgs)
        Dim verdict As ExchangePasswordPolicy.Verdict = ExchangePasswordPolicy.Check(_txt.Text)
        If verdict <> ExchangePasswordPolicy.Verdict.Accepted Then
            ' Refused at entry, with the reason beside the field; the dialog stays open and nothing is sent.
            ShowReason(ExchangeText.PolicyReason(verdict))
            _txt.Focus()
            Return
        End If
        _entered = _txt.Text
        Me.DialogResult = DialogResult.OK
    End Sub

    ''' <summary>Closes the generated dialog and says so: the password it shows is then not used, the reset takes a typed one.</summary>
    Private Sub OnOwnClicked(sender As Object, e As EventArgs)
        _choseOwn = True
        Me.DialogResult = DialogResult.Retry
    End Sub

    Private Sub OnCopyClicked(sender As Object, e As EventArgs)
        ' ONLY through the guarded door: the privacy markers keep it out of Win+V history and the cloud
        ' clipboard (CLIPBOARD-GUARD rules 2 and 6).
        If SecureClipboard.SetSecret(_txt.Text) Then
            _copied = True
            _lblStatus.Text = ExchangeText.PasswordCopied()
            _lblStatus.ForeColor = ThemeService.Palette.TextMuted
        Else
            _lblStatus.Text = ExchangeText.CopyFailed()
            _lblStatus.ForeColor = CollapsibleSection.AttentionColor
        End If
        _lblStatus.Visible = True
    End Sub

    ''' <summary>A generated password that was neither copied nor even revealed would be lost for good, so
    ''' closing then is asked about (the safe answer is the default).</summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If _mode = DialogMode.Generated AndAlso Not e.Cancel AndAlso Not _copied AndAlso Not _revealed AndAlso
           Not _retrievable AndAlso e.CloseReason = CloseReason.UserClosing Then
            If ManagerBox.Show(Me, ExchangeText.CloseWithoutCopyQuestion(), Me.Text, MessageBoxButtons.YesNo,
                               MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                e.Cancel = True
                Return
            End If
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _entered = ""
        MyBase.Dispose(disposing)
    End Sub

End Class
