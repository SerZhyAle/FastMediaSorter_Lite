Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The three account dialogs of the remote administration window (ticket L0278 5.6.2, 5.6.3), one class, three modes: a new
''' account (login, type, password), a new password for an account (the consequence is IN the form: every device signs in again) and
''' the type of an account. The password is typed by hand only (Q278-17): a masked field with a reveal box, checked against the
''' policy before anything is sent, no way to make one here and none to copy. The control is emptied when the window closes; the
''' typed text is held only from OK until the caller has read it (<see cref="ForgetPassword"/>, and Dispose).
''' </summary>
Friend NotInheritable Class Remote_Admin_Account_Form
    Inherits Form

    Friend Enum DialogMode
        Add
        Password
        Kind
    End Enum

    Private ReadOnly _mode As DialogMode
    Private ReadOnly _login As String
    Private _txtLogin As TextBox
    Private _rbMember As RadioButton
    Private _rbGuest As RadioButton
    Private _password As RemoteAdminFormKit.PasswordBlock
    Private _lblLoginReason As Label
    Private _lblKindReason As Label
    Private _answerRow As DialogButtonRow
    Private _okButton As Button
    Private _enteredLogin As String = ""
    Private _enteredPassword As String = ""

    Friend Sub New(mode As DialogMode, login As String, currentKind As String)
        _mode = mode
        _login = If(login, "")
        BuildUi(currentKind)
    End Sub

    ''' <summary>The login as typed (new account) or the account the dialog is about. Valid after OK.</summary>
    Friend ReadOnly Property LoginValue As String
        Get
            Return _enteredLogin
        End Get
    End Property

    ''' <summary>The typed password, valid from OK until <see cref="ForgetPassword"/>. It is not read from the field: the field is
    ''' emptied when the window closes, which is before the caller reads.</summary>
    Friend ReadOnly Property PasswordValue As String
        Get
            Return _enteredPassword
        End Get
    End Property

    Friend ReadOnly Property KindValue As String
        Get
            Return If(_rbGuest IsNot Nothing AndAlso _rbGuest.Checked, "guest", "member")
        End Get
    End Property

    Friend Sub ForgetPassword()
        _enteredPassword = ""
    End Sub

    Friend ReadOnly Property PasswordControl As TextBox
        Get
            Return If(_password Is Nothing, Nothing, _password.Field)
        End Get
    End Property

    Friend ReadOnly Property LoginControl As TextBox
        Get
            Return _txtLogin
        End Get
    End Property

    Friend ReadOnly Property RevealControl As CheckBox
        Get
            Return If(_password Is Nothing, Nothing, _password.Reveal)
        End Get
    End Property

    Friend ReadOnly Property PasswordReasonLabel As Label
        Get
            Return If(_password Is Nothing, Nothing, _password.ReasonLabel)
        End Get
    End Property

    Friend ReadOnly Property LoginReasonLabel As Label
        Get
            Return _lblLoginReason
        End Get
    End Property

    ''' <summary>What OK does, without a window: checks the fields, shows what is wrong beside its field, and on success takes the
    ''' values. False leaves the dialog open with nothing taken.</summary>
    Friend Function TryAccept() As Boolean
        Dim login As String = If(_mode = DialogMode.Add, _txtLogin.Text.Trim(), _login)
        If _mode = DialogMode.Add Then
            If login.Length = 0 Then
                RemoteAdminFormKit.SetReason(_lblLoginReason, ExchangeAdminText.Failure(ExchangeAdminCodes.BadLogin))
                _txtLogin.Focus()
                Return False
            End If
            RemoteAdminFormKit.SetReason(_lblLoginReason, "")
        End If
        If _mode <> DialogMode.Kind AndAlso Not _password.Passes() Then Return False
        _enteredLogin = login
        _enteredPassword = If(_mode = DialogMode.Kind, "", _password.Field.Text)
        Return True
    End Function

    ''' <summary>A refusal of the server the form can name: it stays beside the field it is about and the form stays open.</summary>
    Friend Sub ShowServerRefusal(code As String, sentence As String)
        Select Case code
            Case ExchangeAdminCodes.WeakPassword
                If _password IsNot Nothing Then RemoteAdminFormKit.SetReason(_password.ReasonLabel, sentence)
            Case ExchangeAdminCodes.BadKind
                RemoteAdminFormKit.SetReason(_lblKindReason, sentence)
            Case Else
                If _lblLoginReason IsNot Nothing Then
                    RemoteAdminFormKit.SetReason(_lblLoginReason, sentence)
                ElseIf _password IsNot Nothing Then
                    RemoteAdminFormKit.SetReason(_password.ReasonLabel, sentence)
                End If
        End Select
    End Sub

    Private Sub BuildUi(currentKind As String)
        Dim title As String = Select_Title()
        UiLanguage.ApplyTo(Me)
        Dim body As TableLayoutPanel = RemoteAdminFormKit.Begin(Me, title)

        If _mode = DialogMode.Add Then
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .Text = Localization.T("Логин:")})
            _txtLogin = New TextBox With {.Name = "raa_login", .Width = RemoteAdminFormKit.ContentWidth, .Margin = New Padding(0, 0, 0, 4),
                .AccessibleName = Localization.T("Логин")}
            body.Controls.Add(_txtLogin)
            body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Логин: от 3 до 64 символов - строчные латинские буквы, цифры, точка, подчёркивание и дефис.")))
            _lblLoginReason = RemoteAdminFormKit.Reason()
            body.Controls.Add(_lblLoginReason)
        End If

        If _mode <> DialogMode.Password Then BuildKind(body, currentKind)

        If _mode <> DialogMode.Kind Then
            If _mode = DialogMode.Password Then
                body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Каждое устройство этого аккаунта выйдет из аккаунта и войдёт снова с новым паролем."), muted:=False))
            End If
            _password = New RemoteAdminFormKit.PasswordBlock(body, Me, Localization.T("Пароль:"), Localization.T("Пароль"))
        End If

        _okButton = RemoteAdminFormKit.NewButton(If(_mode = DialogMode.Add, Localization.T("Создать"), Localization.T("Сохранить")), "action.confirm", "raa_Ok")
        AddHandler _okButton.Click, Sub() If TryAccept() Then Me.DialogResult = DialogResult.OK
        Dim btnCancel As Button = RemoteAdminFormKit.NewButton(Localization.T("Отмена"), "action.cancel", "raa_Cancel")
        btnCancel.DialogResult = DialogResult.Cancel
        _answerRow = RemoteAdminFormKit.NewRow("raa_Buttons")
        DpiLayout.AddAnswer(_answerRow, _okButton, DialogButtonRole.Affirmative, "action.confirm")
        DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing, "action.cancel")
        RemoteAdminFormKit.Finish(Me, body, _answerRow)
    End Sub

    Private Function Select_Title() As String
        Select Case _mode
            Case DialogMode.Add : Return Localization.T("Новый аккаунт")
            Case DialogMode.Password : Return Localization.TF("Новый пароль для «{0}»", _login)
            Case Else : Return Localization.TF("Тип аккаунта «{0}»", _login)
        End Select
    End Function

    Private Sub BuildKind(body As TableLayoutPanel, currentKind As String)
        Dim offered As Boolean = OutwardAccessFeatures.IsOffered()
        Dim isGuest As Boolean = String.Equals(currentKind, "guest", StringComparison.OrdinalIgnoreCase)
        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Тип создаваемого аккаунта:")})
        _rbMember = New RadioButton With {.Name = "raa_member", .AutoSize = True, .Checked = Not isGuest, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Своё устройство или семья")}
        body.Controls.Add(_rbMember)
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(20, 0, 0, 6), .Text = Localization.T("Создаёт полноценный аккаунт с возможностью публикации ресурсов.")})
        _rbGuest = New RadioButton With {.Name = "raa_guest", .AutoSize = True, .Checked = isGuest, .Margin = New Padding(0, 4, 0, 2),
            .Text = Localization.T("Гость"), .Enabled = offered OrElse isGuest}
        body.Controls.Add(_rbGuest)
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(20, 0, 0, 6),
            .Text = If(offered, Localization.T("Видит только явно предоставленные ему ресурсы и ничего не публикует."), OutwardAccessFeatures.Reason())})
        _lblKindReason = RemoteAdminFormKit.Reason()
        body.Controls.Add(_lblKindReason)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        RemoteAdminFormKit.OnLoadFit(Me)
        If _txtLogin IsNot Nothing Then
            _txtLogin.Focus()
        ElseIf _password IsNot Nothing Then
            _password.Field.Focus()
        End If
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _enteredPassword = ""
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
