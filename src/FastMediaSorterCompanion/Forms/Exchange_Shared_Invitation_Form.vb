Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Dialog for setting a shared invitation password (ticket 224 rule 2).
''' Offers a choice of password generation or typing, and account kind (family or guest).
''' Ticket 239 CR-13 (Q239-2): the guest kind is shown as not available, with the reason, while
''' <see cref="OutwardAccessFeatures.Offered"/> is False. A guest-kind shared invitation that already exists is shown as it is and
''' cannot be set again from here (it is never silently turned into a family one); it can still be switched off by the caller.
''' </summary>
Friend NotInheritable Class Exchange_Shared_Invitation_Form
    Inherits Form

    Private Const ContentWidth As Integer = 420

    Private _rbGenerate As RadioButton
    Private _rbType As RadioButton
    Private _rbFamily As RadioButton
    Private _rbGuest As RadioButton
    Private _lblGuestReason As Label
    Private _iconHandle As IntPtr
    Private _password As String = ""
    Private _generated As Boolean

    Friend Sub New(Optional currentKind As String = "member")
        BuildUi(currentKind)
    End Sub

    Public ReadOnly Property Password As String
        Get
            Return _password
        End Get
    End Property

    Public ReadOnly Property Generated As Boolean
        Get
            Return _generated
        End Get
    End Property

    Public ReadOnly Property Kind As String
        Get
            Return If(OutwardAccessFeatures.IsOffered() AndAlso _rbGuest IsNot Nothing AndAlso _rbGuest.Checked, "guest", "member")
        End Get
    End Property

    Public Sub WipePassword()
        _password = ""
    End Sub

    Private Function NewButton(text As String, onClick As Action, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildUi(currentKind As String)
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Общий пароль приглашения")
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

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 6),
            .Text = Localization.T("Пользователи смогут регистрироваться на сервере, указав этот общий пароль вместо одноразового кода приглашения.")})

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Font = New Font(Me.Font, FontStyle.Bold),
            .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Тип создаваемого аккаунта:")})
        Dim isGuest As Boolean = String.Equals(currentKind, "guest", StringComparison.OrdinalIgnoreCase)
        _rbFamily = New RadioButton With {.AutoSize = True, .Checked = Not isGuest, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Своё устройство или семья")}
        Dim offered As Boolean = OutwardAccessFeatures.IsOffered()
        _rbGuest = New RadioButton With {.AutoSize = True, .Checked = isGuest, .Margin = New Padding(0, 0, 0, 8),
            .Text = Localization.T("Гость"), .Enabled = offered}
        body.Controls.Add(_rbFamily)
        body.Controls.Add(_rbGuest)
        If Not offered Then
            _lblGuestReason = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
                .Margin = New Padding(20, 0, 0, 8), .Text = OutwardAccessFeatures.Reason()}
            body.Controls.Add(_lblGuestReason)
        End If

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Font = New Font(Me.Font, FontStyle.Bold),
            .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Пароль:")})
        _rbGenerate = New RadioButton With {.AutoSize = True, .Checked = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Сгенерировать случайный безопасный пароль")}
        _rbType = New RadioButton With {.AutoSize = True, .Checked = False, .Margin = New Padding(0, 0, 0, 6),
            .Text = Localization.T("Ввести пароль вручную")}
        body.Controls.Add(_rbGenerate)
        body.Controls.Add(_rbType)

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Margin = New Padding(0, 10, 0, 6)}
        Dim btnOk As Button = NewButton(Localization.T("Установить"), AddressOf OnOkClicked, "action.confirm", ButtonGlyphRole.Affirmative)
        ' a guest-kind invitation that already exists cannot be set again while guests are not offered
        btnOk.Enabled = offered OrElse Not isGuest
        Dim btnCancel As Button = NewButton(Localization.T("Отмена"), Sub() Me.DialogResult = DialogResult.Cancel, "nav.close", ButtonGlyphRole.Dismissing)
        btnCancel.DialogResult = DialogResult.Cancel
        buttons.Controls.Add(btnOk)
        buttons.Controls.Add(btnCancel)
        body.Controls.Add(buttons)

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

    Private Sub OnOkClicked()
        If Not OutwardAccessFeatures.IsOffered() AndAlso _rbGuest.Checked Then Return
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
