Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' "Change the password" of the Share Manager's own exchange account (ticket 226 rule 4): the old password and the new one
''' twice, the server's password policy in words beside the field (the person reads it before the first refusal), and
''' nothing else. The window sends nothing: it returns what was typed, and the caller holds the agent that speaks.
'''
''' The passwords live in this window's fields and in the one frame the agent builds; the fields are cleared on close.
''' The policy is checked locally for IMMEDIATE feedback only - the server has the last word, and a wrong old password is
''' the common refusal of the table.
''' </summary>
Friend NotInheritable Class Exchange_Password_Change_Form
    Inherits Form

    Private Const ContentWidth As Integer = 400

    Private _txtOld As TextBox
    Private _txtNew As TextBox
    Private _txtRepeat As TextBox
    Private _chkShow As CheckBox
    Private _lblStatus As Label
    Private _iconHandle As IntPtr
    Private _old As String = ""
    Private _new As String = ""

    ''' <summary>The old password, taken at the moment of OK (the fields are cleared on close).</summary>
    Friend ReadOnly Property OldPassword As String
        Get
            Return _old
        End Get
    End Property

    ''' <summary>The new password, or "" when the window closed without one.</summary>
    Friend ReadOnly Property NewPassword As String
        Get
            Return _new
        End Get
    End Property

    Friend Sub New(accountName As String)
        BuildUi(accountName)
    End Sub

    Private Function NewButton(text As String, onClick As Action, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildUi(accountName As String)
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.TF("Смена пароля: {0}", accountName)
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub()
                                      ShareIcons.FreeIcon(Me.Icon, _iconHandle)
                                      If _txtOld IsNot Nothing Then _txtOld.Text = ""
                                      If _txtNew IsNot Nothing Then _txtNew.Text = ""
                                      If _txtRepeat IsNot Nothing Then _txtRepeat.Text = ""
                                  End Sub
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 10, 14, 8)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 2),
            .Text = Localization.T("Старый пароль:")})
        _txtOld = New TextBox With {.Width = ContentWidth, .UseSystemPasswordChar = True, .Margin = New Padding(0, 0, 0, 8)}
        body.Controls.Add(_txtOld)

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 2),
            .Text = Localization.T("Новый пароль:")})
        _txtNew = New TextBox With {.Width = ContentWidth, .UseSystemPasswordChar = True, .Margin = New Padding(0, 0, 0, 8)}
        body.Controls.Add(_txtNew)

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 2),
            .Text = Localization.T("Новый пароль ещё раз:")})
        _txtRepeat = New TextBox With {.Width = ContentWidth, .UseSystemPasswordChar = True, .Margin = New Padding(0, 0, 0, 2)}
        body.Controls.Add(_txtRepeat)

        _chkShow = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 4), .Text = Localization.T("Показать пароль")}
        AddHandler _chkShow.CheckedChanged, Sub()
                                               _txtOld.UseSystemPasswordChar = Not _chkShow.Checked
                                               _txtNew.UseSystemPasswordChar = Not _chkShow.Checked
                                               _txtRepeat.UseSystemPasswordChar = Not _chkShow.Checked
                                           End Sub
        body.Controls.Add(_chkShow)

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 0, 0, 6), .Text = Localization.T("Пароль: не короче 10 символов, с заглавной латинской буквой, со строчной латинской буквой и с цифрой.")})

        _lblStatus = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = CollapsibleSection.AttentionColor,
            .Margin = New Padding(0, 0, 0, 4), .Text = ""}
        body.Controls.Add(_lblStatus)

        Dim btnRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 4, 0, 2)}
        Dim btnOk As Button = NewButton(Localization.T("Сменить пароль.."), AddressOf OnOk, "action.confirm", ButtonGlyphRole.Affirmative)
        Dim btnCancel As Button = NewButton(Localization.T("Отмена"), Sub() Me.DialogResult = DialogResult.Cancel, "nav.close", ButtonGlyphRole.Dismissing)
        btnCancel.DialogResult = DialogResult.Cancel
        btnRow.Controls.Add(btnOk)
        btnRow.Controls.Add(btnCancel)
        body.Controls.Add(btnRow)

        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel
        Me.Controls.Add(body)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If _txtOld IsNot Nothing Then _txtOld.Focus()
    End Sub

    Private Sub OnOk()
        _lblStatus.Text = ""
        If _txtOld.Text.Length = 0 Then
            _lblStatus.Text = Localization.T("Укажите старый пароль.")
            _txtOld.Focus()
            Return
        End If
        If Not String.Equals(_txtNew.Text, _txtRepeat.Text, StringComparison.Ordinal) Then
            _lblStatus.Text = Localization.T("Пароли в двух полях не совпали.")
            _txtNew.Focus()
            Return
        End If
        Dim policy As ExchangePasswordPolicy.Verdict = ExchangePasswordPolicy.Check(_txtNew.Text)
        If policy <> ExchangePasswordPolicy.Verdict.Accepted Then
            Select Case policy
                Case ExchangePasswordPolicy.Verdict.TooShort : _lblStatus.Text = Localization.T("Пароль короче 10 символов.")
                Case ExchangePasswordPolicy.Verdict.NoUpper : _lblStatus.Text = Localization.T("В пароле нет заглавной латинской буквы.")
                Case ExchangePasswordPolicy.Verdict.NoLower : _lblStatus.Text = Localization.T("В пароле нет строчной латинской буквы.")
                Case Else : _lblStatus.Text = Localization.T("В пароле нет цифры.")
            End Select
            _txtNew.Focus()
            Return
        End If
        _old = _txtOld.Text
        _new = _txtNew.Text
        DialogResult = DialogResult.OK
    End Sub
End Class
