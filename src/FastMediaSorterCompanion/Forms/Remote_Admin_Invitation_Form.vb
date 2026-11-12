Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The invitation dialogs of the remote administration window (ticket L0278 5.6.2, 6), one class, three modes made by a factory:
''' the form that creates an invitation (its type and the optional login it is written for), the result shown ONCE (the code and, when
''' the server has a public address, the connection string) and the one-time pairing code of an account. A secret is copied only
''' through <see cref="SecureClipboard.SetSecret"/> (the three CLIPBOARD-GUARD flags) and never logged; the boxes are emptied when the
''' window closes. A connection string the server left empty is said so in words and has no Copy.
''' </summary>
Friend NotInheritable Class Remote_Admin_Invitation_Form
    Inherits Form

    Friend Enum DialogMode
        Create
        Created
        PairingCode
    End Enum

    Private ReadOnly _mode As DialogMode
    Private _code As String
    Private _connectionString As String
    Private ReadOnly _kind As String
    Private ReadOnly _expiresAt As String
    Private ReadOnly _login As String
    Private _rbMember As RadioButton
    Private _rbGuest As RadioButton
    Private _txtExpected As TextBox
    Private _txtCode As TextBox
    Private _txtString As TextBox
    Private _lblExpectedReason As Label
    Private _lblKindReason As Label
    Private _lblResult As Label
    Private _btnCopyCode As Button
    Private _btnCopyString As Button
    Private _answerRow As DialogButtonRow

    Private Sub New(mode As DialogMode, code As String, connectionString As String, kind As String, expiresAt As String, login As String)
        _mode = mode
        _code = If(code, "")
        _connectionString = If(connectionString, "")
        _kind = If(kind, "member")
        _expiresAt = If(expiresAt, "")
        _login = If(login, "")
        BuildUi()
    End Sub

    ''' <summary>The form that creates an invitation.</summary>
    Friend Shared Function ForCreate() As Remote_Admin_Invitation_Form
        Return New Remote_Admin_Invitation_Form(DialogMode.Create, "", "", "member", "", "")
    End Function

    ''' <summary>The result of a creation, shown once.</summary>
    Friend Shared Function ForCreated(code As String, connectionString As String, kind As String) As Remote_Admin_Invitation_Form
        Return New Remote_Admin_Invitation_Form(DialogMode.Created, code, connectionString, kind, "", "")
    End Function

    ''' <summary>A pairing code of an account, shown once.</summary>
    Friend Shared Function ForPairingCode(code As String, expiresAt As String, login As String) As Remote_Admin_Invitation_Form
        Return New Remote_Admin_Invitation_Form(DialogMode.PairingCode, code, "", "member", expiresAt, login)
    End Function

    ''' <summary>"member" or "guest". Valid for the create form.</summary>
    Friend ReadOnly Property KindValue As String
        Get
            Return If(OutwardAccessFeatures.IsOffered() AndAlso _rbGuest IsNot Nothing AndAlso _rbGuest.Checked, "guest", "member")
        End Get
    End Property

    ''' <summary>The login the invitation is written for, "" when none was typed.</summary>
    Friend ReadOnly Property ExpectedLoginValue As String
        Get
            Return If(_txtExpected Is Nothing, "", _txtExpected.Text.Trim())
        End Get
    End Property

    Friend ReadOnly Property CodeBox As TextBox
        Get
            Return _txtCode
        End Get
    End Property

    Friend ReadOnly Property ConnectionStringBox As TextBox
        Get
            Return _txtString
        End Get
    End Property

    ''' <summary>Copies the code with the clipboard flags; the sentence of the outcome is shown in the window. False when it failed.</summary>
    Friend Function CopyCode() As Boolean
        Return CopySecret(_code, Localization.T("Код приглашения скопирован в буфер обмена."), Localization.T("Не удалось скопировать код в буфер обмена."))
    End Function

    Friend Function CopyConnectionString() As Boolean
        If _connectionString.Length = 0 Then Return False
        Return CopySecret(_connectionString, Localization.T("Строка подключения скопирована в буфер обмена."), Localization.T("Не удалось скопировать строку в буфер обмена."))
    End Function

    Friend ReadOnly Property CopyCodeButton As Button
        Get
            Return _btnCopyCode
        End Get
    End Property

    Friend ReadOnly Property CopyStringButton As Button
        Get
            Return _btnCopyString
        End Get
    End Property

    ''' <summary>A refusal of the server the form can name: beside its field, the form stays open.</summary>
    Friend Sub ShowServerRefusal(code As String, sentence As String)
        If code = ExchangeAdminCodes.BadKind Then
            RemoteAdminFormKit.SetReason(_lblKindReason, sentence)
        Else
            RemoteAdminFormKit.SetReason(_lblExpectedReason, sentence)
        End If
    End Sub

    Private Function CopySecret(text As String, done As String, failed As String) As Boolean
        If String.IsNullOrEmpty(text) Then Return False
        Dim ok As Boolean = SecureClipboard.SetSecret(text)
        _lblResult.ForeColor = If(ok, ThemeService.Palette.TextMuted, CollapsibleSection.AttentionColor)
        _lblResult.Text = If(ok, done, failed)
        _lblResult.Visible = True
        Return ok
    End Function

    Private Sub BuildUi()
        Dim title As String
        Select Case _mode
            Case DialogMode.Create : title = Localization.T("Создать приглашение")
            Case DialogMode.Created : title = Localization.T("Приглашение создано")
            Case Else : title = Localization.T("Код привязки устройства")
        End Select
        UiLanguage.ApplyTo(Me)
        Dim body As TableLayoutPanel = RemoteAdminFormKit.Begin(Me, title)

        If _mode = DialogMode.Create Then
            BuildCreate(body)
        Else
            BuildShownOnce(body)
        End If

        _answerRow = RemoteAdminFormKit.NewRow("rai_Buttons")
        If _mode = DialogMode.Create Then
            Dim btnOk As Button = RemoteAdminFormKit.NewButton(Localization.T("Создать"), "action.confirm", "rai_Ok")
            AddHandler btnOk.Click, Sub() Me.DialogResult = DialogResult.OK
            Dim btnCancel As Button = RemoteAdminFormKit.NewButton(Localization.T("Отмена"), "action.cancel", "rai_Cancel")
            btnCancel.DialogResult = DialogResult.Cancel
            DpiLayout.AddAnswer(_answerRow, btnOk, DialogButtonRole.Affirmative, "action.confirm")
            DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing, "action.cancel")
        Else
            ' Close is the window's one answer; the copy buttons stay beside the field each one copies.
            Dim btnClose As Button = RemoteAdminFormKit.NewButton(Localization.T("Закрыть"), "nav.close", "rai_Close")
            btnClose.DialogResult = DialogResult.OK
            DpiLayout.AddAnswer(_answerRow, btnClose, DialogButtonRole.Dismissing, "nav.close")
            AddHandler Me.FormClosed,
                Sub()
                    _code = ""
                    _connectionString = ""
                    If _txtCode IsNot Nothing Then _txtCode.Clear()
                    If _txtString IsNot Nothing Then _txtString.Clear()
                End Sub
        End If
        RemoteAdminFormKit.Finish(Me, body, _answerRow)
    End Sub

    Private Sub BuildCreate(body As TableLayoutPanel)
        Dim offered As Boolean = OutwardAccessFeatures.IsOffered()
        body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Выберите тип создаваемого приглашения для регистрации пользователя:"), muted:=False))
        _rbMember = New RadioButton With {.Name = "rai_member", .AutoSize = True, .Checked = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Своё устройство или семья")}
        body.Controls.Add(_rbMember)
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(20, 0, 0, 6), .Text = Localization.T("Создаёт полноценный аккаунт с возможностью публикации ресурсов.")})
        _rbGuest = New RadioButton With {.Name = "rai_guest", .AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Гость"), .Enabled = offered}
        body.Controls.Add(_rbGuest)
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(20, 0, 0, 6),
            .Text = If(offered, Localization.T("Видит только явно предоставленные ему ресурсы и ничего не публикует."), OutwardAccessFeatures.Reason())})
        _lblKindReason = RemoteAdminFormKit.Reason()
        body.Controls.Add(_lblKindReason)

        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Для логина (необязательно):")})
        _txtExpected = New TextBox With {.Name = "rai_expected", .Width = RemoteAdminFormKit.ContentWidth, .Margin = New Padding(0, 0, 0, 4),
            .AccessibleName = Localization.T("Логин")}
        body.Controls.Add(_txtExpected)
        body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Только подсказка для того, кто вводит код: сервер её не проверяет.")))
        _lblExpectedReason = RemoteAdminFormKit.Reason()
        body.Controls.Add(_lblExpectedReason)
    End Sub

    Private Sub BuildShownOnce(body As TableLayoutPanel)
        If _mode = DialogMode.Created Then
            body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0), .Font = New Font(Me.Font, FontStyle.Bold),
                .Margin = New Padding(0, 0, 0, 4), .Text = Localization.TF("Тип приглашения: {0}",
                    If(String.Equals(_kind, "guest", StringComparison.OrdinalIgnoreCase), Localization.T("Гость"), Localization.T("Своё устройство или семья")))})
            body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Приглашение показывается один раз. После закрытия этого окна его нельзя показать снова - только отозвать."), muted:=False))
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Код приглашения:")})
        Else
            body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Введите этот код на новом устройстве вместо пароля. Он одноразовый.")))
            If _expiresAt.Length > 0 Then
                body.Controls.Add(RemoteAdminFormKit.Note(Localization.TF("Код действует до {0}.", Localization.LtrValue(_expiresAt))))
            End If
            body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Код показывается один раз. После закрытия этого окна его нельзя показать снова."), muted:=False))
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.TF("Код привязки для «{0}»", _login)})
        End If

        _txtCode = New TextBox With {.Name = "rai_code", .ReadOnly = True, .Width = RemoteAdminFormKit.ContentWidth, .Text = _code,
            .Margin = New Padding(0, 0, 0, 4), .AccessibleName = Localization.T("Код приглашения")}
        body.Controls.Add(_txtCode)
        If _code.Length > 0 Then
            _btnCopyCode = RemoteAdminFormKit.NewButton(Localization.T("Копировать код"), "action.copy", "rai_CopyCode")
            AddHandler _btnCopyCode.Click, Sub() CopyCode()
            body.Controls.Add(_btnCopyCode)
        End If

        If _mode = DialogMode.Created Then
            If _connectionString.Length > 0 Then
                body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Text = Localization.T("Строка подключения с приглашением:")})
                _txtString = New TextBox With {.Name = "rai_string", .ReadOnly = True, .Multiline = True, .WordWrap = True, .ScrollBars = ScrollBars.Vertical,
                    .Width = RemoteAdminFormKit.ContentWidth, .Height = 64, .Text = _connectionString, .Margin = New Padding(0, 0, 0, 4),
                    .AccessibleName = Localization.T("Строка подключения")}
                body.Controls.Add(_txtString)
                _btnCopyString = RemoteAdminFormKit.NewButton(Localization.T("Копировать строку"), "action.copy", "rai_CopyString")
                AddHandler _btnCopyString.Click, Sub() CopyConnectionString()
                body.Controls.Add(_btnCopyString)
            Else
                ' no public address on the server: said in words, and there is nothing to copy
                body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("У сервера не задан публичный адрес, поэтому строки подключения нет. Передайте код вместе с адресом сервера."), muted:=False))
            End If
        End If

        _lblResult = New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 4, 0, 4), .Visible = False}
        body.Controls.Add(_lblResult)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        RemoteAdminFormKit.OnLoadFit(Me)
    End Sub
End Class
