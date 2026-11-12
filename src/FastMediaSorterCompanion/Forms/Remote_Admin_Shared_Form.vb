Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The dialog that turns the shared invitation ON on the remote server (ticket L0278 5.6.2, 5.6.3): the type of account it registers
''' and the shared password. Turning it off is its own button on the card and has no dialog. The password is typed by hand only
''' (Q278-17): a masked field with a reveal box, checked against the policy before anything is sent, no way to make one here and none
''' to copy; an empty one is refused (the server reads an empty password as "off"). The control is emptied when the window closes.
''' </summary>
Friend NotInheritable Class Remote_Admin_Shared_Form
    Inherits Form

    Private ReadOnly _startKind As String
    Private _rbMember As RadioButton
    Private _rbGuest As RadioButton
    Private _password As RemoteAdminFormKit.PasswordBlock
    Private _lblKindReason As Label
    Private _okButton As Button
    Private _answerRow As DialogButtonRow
    Private _enteredPassword As String = ""

    Friend Sub New(currentKind As String)
        _startKind = If(String.Equals(currentKind, "guest", StringComparison.OrdinalIgnoreCase), "guest", "member")
        BuildUi()
    End Sub

    Friend ReadOnly Property KindValue As String
        Get
            Return If(_rbGuest IsNot Nothing AndAlso _rbGuest.Checked, "guest", "member")
        End Get
    End Property

    ''' <summary>The typed password, valid from OK until <see cref="ForgetPassword"/>; not read from the field, which is emptied at close.</summary>
    Friend ReadOnly Property PasswordValue As String
        Get
            Return _enteredPassword
        End Get
    End Property

    Friend Sub ForgetPassword()
        _enteredPassword = ""
    End Sub

    Friend ReadOnly Property PasswordControl As TextBox
        Get
            Return _password.Field
        End Get
    End Property

    Friend ReadOnly Property RevealControl As CheckBox
        Get
            Return _password.Reveal
        End Get
    End Property

    Friend ReadOnly Property PasswordReasonLabel As Label
        Get
            Return _password.ReasonLabel
        End Get
    End Property

    ''' <summary>What OK does, without a window: the policy is checked and a refusal is named beside the field. False sends nothing.</summary>
    Friend Function TryAccept() As Boolean
        If Not _password.Passes() Then Return False
        _enteredPassword = _password.Field.Text
        Return True
    End Function

    ''' <summary>A refusal of the server the form can name: beside its field, the form stays open.</summary>
    Friend Sub ShowServerRefusal(code As String, sentence As String)
        If code = ExchangeAdminCodes.BadKind Then
            RemoteAdminFormKit.SetReason(_lblKindReason, sentence)
        Else
            RemoteAdminFormKit.SetReason(_password.ReasonLabel, sentence)
        End If
    End Sub

    Private Sub BuildUi()
        Dim offered As Boolean = OutwardAccessFeatures.IsOffered()
        Dim isGuest As Boolean = _startKind = "guest"
        UiLanguage.ApplyTo(Me)
        Dim body As TableLayoutPanel = RemoteAdminFormKit.Begin(Me, Localization.T("Общий пароль приглашения"))
        body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Пользователи смогут регистрироваться на сервере, указав этот общий пароль вместо одноразового кода приглашения."), muted:=False))

        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Тип создаваемого аккаунта:")})
        _rbMember = New RadioButton With {.Name = "ras_member", .AutoSize = True, .Checked = Not isGuest, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Своё устройство или семья")}
        body.Controls.Add(_rbMember)
        _rbGuest = New RadioButton With {.Name = "ras_guest", .AutoSize = True, .Checked = isGuest, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Гость"), .Enabled = offered OrElse isGuest}
        body.Controls.Add(_rbGuest)
        If Not offered Then
            body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
                .Margin = New Padding(20, 0, 0, 6), .Text = OutwardAccessFeatures.Reason()})
        End If
        _lblKindReason = RemoteAdminFormKit.Reason()
        body.Controls.Add(_lblKindReason)

        _password = New RemoteAdminFormKit.PasswordBlock(body, Me, Localization.T("Пароль:"), Localization.T("Пароль"))

        _okButton = RemoteAdminFormKit.NewButton(Localization.T("Установить"), "action.confirm", "ras_Ok")
        AddHandler _okButton.Click, Sub() If TryAccept() Then Me.DialogResult = DialogResult.OK
        Dim btnCancel As Button = RemoteAdminFormKit.NewButton(Localization.T("Отмена"), "action.cancel", "ras_Cancel")
        btnCancel.DialogResult = DialogResult.Cancel
        _answerRow = RemoteAdminFormKit.NewRow("ras_Buttons")
        DpiLayout.AddAnswer(_answerRow, _okButton, DialogButtonRole.Affirmative, "action.confirm")
        DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing, "action.cancel")
        RemoteAdminFormKit.Finish(Me, body, _answerRow)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        RemoteAdminFormKit.OnLoadFit(Me)
        _password.Field.Focus()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _enteredPassword = ""
        MyBase.Dispose(disposing)
    End Sub
End Class
