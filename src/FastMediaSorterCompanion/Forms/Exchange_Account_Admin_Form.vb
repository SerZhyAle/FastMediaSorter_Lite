Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The dialog behind "Add an account" and "Reset password" of the exchange server administration (ticket 168
''' section 3.1). One class, two modes:
'''   * <see cref="DialogMode.Add"/> - a login (folded to lower case, checked against the account rule beside the
'''     field) and how the password is made: GENERATED (the default; shown once, in its own dialog, after the server
'''     accepted the account) or TYPED (the policy checked in <see cref="Exchange_Password_Form"/> before anything is
'''     sent). At the FIRST account the statement of what the operator can see is shown once more, in the dialog.
'''   * <see cref="DialogMode.Reset"/> - the same password choice, and "also sign every device out" beside it.
'''     <paramref name="preferTyped"/> opens it on TYPED: the way from the generated password's "set my own" button.
'''
''' The password lives in memory in this dialog until the caller has used it. The Share Manager keeps a copy on
''' this PC only when the person turned keeping on (ExchangeAccountPasswordStore, decided by the caller, never here);
''' otherwise it is never stored, never logged and never shown again. A .NET string cannot be zeroed, so what is
''' wiped is the controls' copy and <see cref="WipePassword"/> drops the reference.
'''
''' Hand-built, so it follows the Companion recipe: language direction first, DPI scaling as the LAST line of the
''' builder, the clamp to the working area in OnLoad.
''' </summary>
Friend NotInheritable Class Exchange_Account_Admin_Form
    Inherits Form

    Friend Enum DialogMode
        Add
        Reset
    End Enum

    Private Const ContentWidth As Integer = 440

    Private ReadOnly _mode As DialogMode
    Private ReadOnly _showStatement As Boolean
    Private ReadOnly _preferTyped As Boolean
    Private _iconHandle As IntPtr
    Private _txtLogin As TextBox
    Private _lblLoginHint As Label
    Private _rbGenerate As RadioButton
    Private _rbType As RadioButton
    Private _chkSignOut As CheckBox
    Private _txtStatement As TextBox
    Private _btnOk As Button
    Private _password As String = ""
    Private _generated As Boolean

    ''' <summary>The login, folded to lower case. Valid after OK.</summary>
    Friend ReadOnly Property Login As String
        Get
            Return ExchangeLoginRules.Normalize(If(_txtLogin Is Nothing, "", _txtLogin.Text))
        End Get
    End Property

    ''' <summary>The password to send, generated or typed. Valid after OK; the caller calls <see cref="WipePassword"/> when done.</summary>
    Friend ReadOnly Property Password As String
        Get
            Return _password
        End Get
    End Property

    ''' <summary>The password was made here (so the caller shows it once), not typed.</summary>
    Friend ReadOnly Property Generated As Boolean
        Get
            Return _generated
        End Get
    End Property

    Friend ReadOnly Property SignOutEveryDevice As Boolean
        Get
            Return _chkSignOut IsNot Nothing AndAlso _chkSignOut.Checked
        End Get
    End Property

    Friend Sub WipePassword()
        _password = ""
    End Sub

    Friend Sub New(mode As DialogMode, Optional login As String = "", Optional showStatement As Boolean = False, Optional preferTyped As Boolean = False)
        _mode = mode
        _showStatement = showStatement AndAlso mode = DialogMode.Add
        _preferTyped = preferTyped
        BuildUi(login)
    End Sub

    Private Sub BuildUi(login As String)
        UiLanguage.ApplyTo(Me)
        Me.Text = If(_mode = DialogMode.Add, ExchangeAdminText.AddAccountTitle(), ExchangeAdminText.ResetPasswordTitle(login))
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                _password = If(DialogResult = DialogResult.OK, _password, "")
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

        If _showStatement Then
            body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 4),
                .Text = ExchangeAdminText.StatementBeforeFirst()})
            _txtStatement = New TextBox With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Width = ContentWidth,
                .Height = 150, .Margin = New Padding(0, 0, 0, 8), .Text = String.Join(vbCrLf & vbCrLf, ExchangeStatement.Paragraphs()),
                .AccessibleName = ExchangeAdminText.StatementTitle()}
            body.Controls.Add(_txtStatement)
        End If

        If _mode = DialogMode.Add Then
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .Text = Localization.T("Логин:")})
            _txtLogin = New TextBox With {.Width = ContentWidth, .MaxLength = ExchangeLoginRules.MaxLength + 8, .Margin = New Padding(0, 0, 0, 2)}
            AddHandler _txtLogin.TextChanged, AddressOf OnLoginChanged
            body.Controls.Add(_txtLogin)
            _lblLoginHint = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
                .Margin = New Padding(0, 2, 0, 6), .Text = ExchangeAdminText.LoginHint()}
            body.Controls.Add(_lblLoginHint)
        End If

        _rbGenerate = New RadioButton With {.AutoSize = True, .Checked = Not _preferTyped, .Margin = New Padding(0, 4, 0, 0), .Text = ExchangeAdminText.GenerateChoice()}
        _rbType = New RadioButton With {.AutoSize = True, .Checked = _preferTyped, .Margin = New Padding(0, 0, 0, 0), .Text = ExchangeAdminText.TypeChoice()}
        body.Controls.Add(_rbGenerate)
        body.Controls.Add(_rbType)

        If _mode = DialogMode.Reset Then
            _chkSignOut = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 0), .Text = ExchangeAdminText.SignOutEverywhere()}
            body.Controls.Add(_chkSignOut)
        End If

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Margin = New Padding(0, 10, 0, 6)}
        _btnOk = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Text = If(_mode = DialogMode.Add, ExchangeAdminText.CreateButton(), ExchangeAdminText.ResetButton())}
        If _mode = DialogMode.Add Then
            ButtonGlyphs.Apply(_btnOk, "action.confirm", ButtonGlyphRole.Affirmative)
        Else
            ButtonGlyphs.Apply(_btnOk, "action.change-password", ButtonGlyphRole.Caution)
        End If
        AddHandler _btnOk.Click, AddressOf OnOkClicked
        Dim btnCancel As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}
        ButtonGlyphs.Apply(btnCancel, "action.cancel", ButtonGlyphRole.Dismissing)
        buttons.Controls.Add(_btnOk)
        buttons.Controls.Add(btnCancel)
        body.Controls.Add(buttons)
        Me.AcceptButton = _btnOk
        Me.CancelButton = btnCancel

        Me.Controls.Add(body)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
        If _txtLogin IsNot Nothing Then _txtLogin.Focus() Else _rbGenerate.Focus()
        ' a read-only multi-line box that is first in the tab order shows its whole text selected: nothing is selected until the person selects it
        If _txtStatement IsNot Nothing Then _txtStatement.Select(0, 0)
    End Sub

    ''' <summary>The hint turns into the warning colour while what is typed cannot be a login; an empty field says nothing yet.</summary>
    Private Sub OnLoginChanged(sender As Object, e As EventArgs)
        Dim bad As Boolean = _txtLogin.TextLength > 0 AndAlso ExchangeLoginRules.Check(_txtLogin.Text) <> ExchangeLoginRules.Verdict.Accepted
        _lblLoginHint.ForeColor = If(bad, CollapsibleSection.AttentionColor, ThemeService.Palette.TextMuted)
    End Sub

    Private Sub OnOkClicked(sender As Object, e As EventArgs)
        If _mode = DialogMode.Add AndAlso ExchangeLoginRules.Check(_txtLogin.Text) <> ExchangeLoginRules.Verdict.Accepted Then
            _lblLoginHint.ForeColor = CollapsibleSection.AttentionColor
            _txtLogin.Focus()
            Return
        End If
        If _rbType.Checked Then
            Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                _password = dlg.EnteredPassword
            End Using
            _generated = False
        Else
            _password = ExchangePasswordPolicy.Generate()
            _generated = True
        End If
        Me.DialogResult = DialogResult.OK
    End Sub

End Class
