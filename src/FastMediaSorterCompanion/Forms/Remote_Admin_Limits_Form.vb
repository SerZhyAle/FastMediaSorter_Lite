Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>
''' The limits dialogs of the remote administration window (ticket L0278 5.6.2, 5.6.3), one class, two modes: the own limits of
''' one account, and the policy of the server (the cap on accounts and the limits a new account gets). A field holds a whole number;
''' an empty field is "not set" and never 0. The account's limits REPLACE its whole override object on the server, so empty fields
''' remove the limits - the form says so; for the policy an empty field leaves the server's value as it is. A negative or a non-number
''' is refused beside its field before anything is sent, and the cap on accounts below 1 is blocked there too.
''' </summary>
Friend NotInheritable Class Remote_Admin_Limits_Form
    Inherits Form

    Friend Enum DialogMode
        Account
        Policy
    End Enum

    Private Const FieldCount As Integer = 5
    Private Const Megabyte As Long = 1024L * 1024L

    Private ReadOnly _mode As DialogMode
    Private ReadOnly _login As String
    Private ReadOnly _start As RemoteAdminLimits
    Private ReadOnly _startMax As Integer?
    Private ReadOnly _boxes(FieldCount - 1) As TextBox
    Private ReadOnly _reasons(FieldCount - 1) As Label
    Private _maxBox As TextBox
    Private _maxReason As Label
    Private _answerRow As DialogButtonRow
    Private _limits As RemoteAdminLimits = RemoteAdminLimits.Empty
    Private _max As Integer?
    Private _startClipboardText As String = ""

    Friend Sub New(mode As DialogMode, login As String, start As RemoteAdminLimits, startMaxAccounts As Integer?)
        _mode = mode
        _login = If(login, "")
        _start = If(start, RemoteAdminLimits.Empty)
        _startMax = startMaxAccounts
        BuildUi()
    End Sub

    ''' <summary>The limits as typed (<see cref="RemoteAdminLimits.Empty"/> when every field is empty). Valid after OK.</summary>
    Friend ReadOnly Property LimitsValue As RemoteAdminLimits
        Get
            Return _limits
        End Get
    End Property

    ''' <summary>The cap on accounts as typed; Nothing when the field is empty (the policy mode then leaves it unchanged). Valid after OK.</summary>
    Friend ReadOnly Property MaxAccountsValue As Integer?
        Get
            Return _max
        End Get
    End Property

    Friend ReadOnly Property Box(index As Integer) As TextBox
        Get
            Return _boxes(index)
        End Get
    End Property

    Friend ReadOnly Property MaxAccountsBox As TextBox
        Get
            Return _maxBox
        End Get
    End Property

    Friend ReadOnly Property MaxAccountsReason As Label
        Get
            Return _maxReason
        End Get
    End Property

    Friend ReadOnly Property Reason(index As Integer) As Label
        Get
            Return _reasons(index)
        End Get
    End Property

    Private Shared Function ParseWhole(text As String, ByRef value As Long, ByRef isSet As Boolean) As Boolean
        Dim t As String = If(text, "").Trim()
        isSet = t.Length > 0
        value = 0
        If Not isSet Then Return True
        Return Long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, value)
    End Function

    ''' <summary>What OK does, without a window: reads every field, shows what is wrong beside its field and, when all is right, takes the
    ''' values. False leaves the dialog open with nothing taken.</summary>
    Friend Function TryAccept() As Boolean
        Dim bad As String = ExchangeAdminText.Failure(ExchangeAdminCodes.BadLimit)
        Dim ok As Boolean = True
        Dim values(FieldCount - 1) As Long
        Dim isSet(FieldCount - 1) As Boolean
        For i As Integer = 0 To FieldCount - 1
            Dim fine As Boolean = ParseWhole(_boxes(i).Text, values(i), isSet(i))
            If fine AndAlso isSet(i) Then
                fine = values(i) >= 0 AndAlso (i = FieldCount - 1 OrElse values(i) <= Integer.MaxValue)
                If fine AndAlso i = FieldCount - 1 Then
                    ' the clipboard field is whole megabytes; an untouched field keeps the exact bytes the server holds
                    fine = values(i) <= Long.MaxValue \ Megabyte
                End If
            End If
            RemoteAdminFormKit.SetReason(_reasons(i), If(fine, "", bad))
            If Not fine Then ok = False
        Next

        Dim maxValue As Long = 0
        Dim maxSet As Boolean = False
        If _maxBox IsNot Nothing Then
            Dim fine As Boolean = ParseWhole(_maxBox.Text, maxValue, maxSet)
            Dim sentence As String = ""
            If Not fine Then
                sentence = bad
            ElseIf maxSet AndAlso (maxValue < 1 OrElse maxValue > Integer.MaxValue) Then
                sentence = Localization.T("Число аккаунтов должно быть не меньше 1.")
            End If
            RemoteAdminFormKit.SetReason(_maxReason, sentence)
            If sentence.Length > 0 Then ok = False
        End If
        If Not ok Then Return False

        Dim clipBytes As Long? = Nothing
        If isSet(4) Then
            clipBytes = If(_startClipboardText.Length > 0 AndAlso String.Equals(_boxes(4).Text.Trim(), _startClipboardText, StringComparison.Ordinal) AndAlso _start.ClipboardMaxBytes.HasValue,
                           _start.ClipboardMaxBytes.Value, values(4) * Megabyte)
        End If
        _limits = New RemoteAdminLimits(
            If(isSet(0), CType(CInt(values(0)), Integer?), Nothing), If(isSet(1), CType(CInt(values(1)), Integer?), Nothing),
            If(isSet(2), CType(CInt(values(2)), Integer?), Nothing), If(isSet(3), CType(CInt(values(3)), Integer?), Nothing), clipBytes)
        _max = If(maxSet, CType(CInt(maxValue), Integer?), Nothing)
        If _limits.HasNegative Then Return False
        Return True
    End Function

    ''' <summary>A refusal of the server the form can name: beside the fields, the form stays open.</summary>
    Friend Sub ShowServerRefusal(code As String, sentence As String)
        Dim target As Label = If(_maxReason, _reasons(0))
        RemoteAdminFormKit.SetReason(target, sentence)
    End Sub

    Private Function AddField(body As TableLayoutPanel, caption As String, accessible As String, name As String, initial As String) As TextBox
        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = caption})
        Dim box As New TextBox With {.Name = name, .Width = 140, .Text = initial, .Margin = New Padding(0, 0, 0, 2), .AccessibleName = accessible}
        body.Controls.Add(box)
        Return box
    End Function

    Private Sub BuildUi()
        Dim title As String = If(_mode = DialogMode.Account, Localization.TF("Лимиты аккаунта «{0}»", _login), Localization.T("Лимиты и правила сервера"))
        UiLanguage.ApplyTo(Me)
        Dim body As TableLayoutPanel = RemoteAdminFormKit.Begin(Me, title)
        If _mode = DialogMode.Account Then
            body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Отдельные пределы этого аккаунта. Пустое поле - предел сервера по умолчанию.")))
            body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Пустые поля снимают ограничения."), muted:=False))
        Else
            body.Controls.Add(RemoteAdminFormKit.Note(Localization.T("Правила сервера. Пустое поле оставляет значение без изменений.")))
            _maxBox = AddField(body, Localization.T("Максимум аккаунтов на сервере:"), Localization.T("Максимум аккаунтов"), "ral_max",
                               If(_startMax.HasValue, _startMax.Value.ToString(CultureInfo.InvariantCulture), ""))
            _maxReason = RemoteAdminFormKit.Reason()
            body.Controls.Add(_maxReason)
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Font = New Font(Me.Font, FontStyle.Bold),
                .Text = Localization.T("Пределы нового аккаунта")})
        End If

        Dim clipMb As String = ""
        If _start.ClipboardMaxBytes.HasValue Then
            clipMb = (_start.ClipboardMaxBytes.Value \ Megabyte).ToString(CultureInfo.InvariantCulture)
            _startClipboardText = clipMb
        End If
        Dim captions As String() = {Localization.T("Устройств на аккаунт:"), Localization.T("Ресурсов на аккаунт:"), Localization.T("Трансляций на аккаунт:"),
                                    Localization.T("Трансляций на устройство:"), Localization.T("Буфер обмена, МБ:")}
        Dim names As String() = {Localization.T("Максимум устройств"), Localization.T("Максимум ресурсов"), Localization.T("Максимум трансляций"),
                                 Localization.T("Максимум трансляций на устройство"), Localization.T("Лимит буфера, МБ")}
        Dim initials As String() = {Text_Of(_start.DevicesPerAccount), Text_Of(_start.ResourcesPerAccount), Text_Of(_start.BroadcastsPerAccount),
                                    Text_Of(_start.BroadcastsPerDevice), clipMb}
        For i As Integer = 0 To FieldCount - 1
            _boxes(i) = AddField(body, captions(i), names(i), "ral_f" & i.ToString(CultureInfo.InvariantCulture), initials(i))
            _reasons(i) = RemoteAdminFormKit.Reason()
            body.Controls.Add(_reasons(i))
        Next

        Dim btnOk As Button = RemoteAdminFormKit.NewButton(Localization.T("Сохранить"), "action.confirm", "ral_Ok")
        AddHandler btnOk.Click, Sub() If TryAccept() Then Me.DialogResult = DialogResult.OK
        Dim btnCancel As Button = RemoteAdminFormKit.NewButton(Localization.T("Отмена"), "action.cancel", "ral_Cancel")
        btnCancel.DialogResult = DialogResult.Cancel
        _answerRow = RemoteAdminFormKit.NewRow("ral_Buttons")
        DpiLayout.AddAnswer(_answerRow, btnOk, DialogButtonRole.Affirmative, "action.confirm")
        DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing, "action.cancel")
        RemoteAdminFormKit.Finish(Me, body, _answerRow)
    End Sub

    Private Shared Function Text_Of(value As Integer?) As String
        Return If(value.HasValue, value.Value.ToString(CultureInfo.InvariantCulture), "")
    End Function

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        RemoteAdminFormKit.OnLoadFit(Me)
        If _maxBox IsNot Nothing Then _maxBox.Focus() Else _boxes(0).Focus()
    End Sub
End Class
