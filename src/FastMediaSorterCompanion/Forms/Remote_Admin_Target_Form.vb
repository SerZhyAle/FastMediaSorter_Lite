Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Ticket L0278 T-6 (specification 5.4, 5.6): adds a server to administer or edits one, in the Share Manager's remote administration
''' window. A name and a host:port; the store's refusals (<see cref="ExchangeAdminTargetResult"/>) are sentences beside the field. When
''' it edits, it also shows the accepted certificate, offers to forget it (the way back after a certificate stop, 5.4 rule 5) and to
''' remove the server from the list; both are confirmed through <see cref="ManagerBox"/> with No as the default. Nothing is sent to any
''' server from here, and the dialog writes only the target store.
''' </summary>
Friend NotInheritable Class Remote_Admin_Target_Form
    Inherits Form

    Private ReadOnly _targets As ExchangeAdminTargets
    Private ReadOnly _editId As String
    Private _savedId As String = ""
    Private _removed As Boolean
    Private _iconHandle As IntPtr

    Private _txtName As TextBox
    Private _txtAddress As TextBox
    Private _lblNameRefusal As Label
    Private _lblAddressRefusal As Label
    Private _lblPins As Label
    Private _txtLeafPin As TextBox
    Private _txtKeyPin As TextBox
    Private _btnForget As Button
    Private _btnRemove As Button
    Private _lblStatus As Label
    Private _answerRow As DialogButtonRow

    Private Const ContentWidth As Integer = 460

    ''' <summary>Adds a server.</summary>
    Friend Sub New(targets As ExchangeAdminTargets)
        Me.New(targets, Nothing)
    End Sub

    ''' <summary>Edits the server with this id (Nothing adds one).</summary>
    Friend Sub New(targets As ExchangeAdminTargets, editId As String)
        If targets Is Nothing Then Throw New ArgumentNullException(NameOf(targets))
        _targets = targets
        _editId = editId
        BuildUi()
    End Sub

    ''' <summary>The id of the record this dialog saved (a new one when it added), "" before a save. Valid after OK.</summary>
    Friend ReadOnly Property SavedId As String
        Get
            Return _savedId
        End Get
    End Property

    ''' <summary>True when the dialog removed the server it edited. The dialog then closes with OK so the window reloads its target.</summary>
    Friend ReadOnly Property Removed As Boolean
        Get
            Return _removed
        End Get
    End Property

    Private ReadOnly Property Editing As Boolean
        Get
            Return Not String.IsNullOrEmpty(_editId)
        End Get
    End Property

    ''' <summary>The sentence of one refusal of the store, in the current language; "" for Saved.</summary>
    Friend Shared Function RefusalSentence(result As ExchangeAdminTargetResult) As String
        Select Case result
            Case ExchangeAdminTargetResult.BadAddress
                Return Localization.T("Адрес должен быть в виде хост:порт, порт от 1 до 65535.")
            Case ExchangeAdminTargetResult.BadName
                Return Localization.T("Название не длиннее 64 знаков и без управляющих символов.")
            Case ExchangeAdminTargetResult.Duplicate
                Return Localization.T("Сервер с таким адресом уже есть в списке.")
            Case ExchangeAdminTargetResult.Full
                Return Localization.T("В списке уже максимальное число серверов.")
            Case ExchangeAdminTargetResult.NotFound
                Return Localization.T("Этого сервера уже нет в списке.")
            Case Else
                Return ""
        End Select
    End Function

    Private Shared Function Note(text As String) As Label
        Return New Label With {.AutoSize = True, .UseMnemonic = False, .MaximumSize = New Size(ContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 2, 0, 2), .Text = text}
    End Function

    Private Shared Function Refusal() As Label
        Return New Label With {.AutoSize = True, .UseMnemonic = False, .MaximumSize = New Size(ContentWidth, 0),
            .ForeColor = CollapsibleSection.AttentionColor, .Margin = New Padding(0, 2, 0, 2), .Visible = False}
    End Function

    Private Shared Function Caption(text As String) As Label
        Return New Label With {.AutoSize = True, .UseMnemonic = False, .Margin = New Padding(0, 6, 0, 2), .Text = text}
    End Function

    Private Function CommandButton(name As String, text As String, glyph As String, onClick As EventHandler,
                                   Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.Name = name, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 4, 12, 4), .Margin = New Padding(0, 4, 8, 4), .Text = text, .UseMnemonic = False}
        ButtonGlyphs.Apply(b, glyph, role)
        AddHandler b.Click, onClick
        Return b
    End Function

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = If(Editing, Localization.T("Изменить сервер"), Localization.T("Добавить сервер"))
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Name = "rat_Body", .Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 12, 14, 6)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        Dim heading As Label = ExchangeMark.Heading(Text, 32, AddressOf CompanionAssets.OpenBundledAsset)
        heading.MaximumSize = New Size(ContentWidth - ContextHelpUi.BadgeEdge, 0)
        body.Controls.Add(heading)

        Dim current As ExchangeAdminTarget = If(Editing, _targets.Find(_editId), Nothing)

        body.Controls.Add(Caption(Localization.T("Название:")))
        _txtName = New TextBox With {.Name = "rat_Name", .Width = ContentWidth, .Margin = New Padding(0, 0, 0, 2),
            .Text = If(current Is Nothing, "", current.Name), .AccessibleName = Localization.T("Название:").TrimEnd(":"c)}
        AddHandler _txtName.TextChanged, Sub(sender As Object, e As EventArgs) _lblNameRefusal.Visible = False
        body.Controls.Add(_txtName)
        _lblNameRefusal = Refusal()
        body.Controls.Add(_lblNameRefusal)

        body.Controls.Add(Caption(Localization.T("Адрес (хост:порт):")))
        _txtAddress = New TextBox With {.Name = "rat_Address", .Width = ContentWidth, .Margin = New Padding(0, 0, 0, 2),
            .Text = If(current Is Nothing, "", current.Address), .AccessibleName = Localization.T("Адрес (хост:порт):").TrimEnd(":"c)}
        AddHandler _txtAddress.TextChanged, Sub(sender As Object, e As EventArgs) _lblAddressRefusal.Visible = False
        body.Controls.Add(_txtAddress)
        _lblAddressRefusal = Refusal()
        body.Controls.Add(_lblAddressRefusal)

        If Editing Then
            _lblPins = Note("")
            body.Controls.Add(_lblPins)
            _txtLeafPin = New TextBox With {.Name = "rat_LeafPin", .Width = ContentWidth, .ReadOnly = True, .Margin = New Padding(0, 0, 0, 2),
                .AccessibleName = Localization.T("Отпечаток сертификата сервера:").TrimEnd(":"c)}
            body.Controls.Add(_txtLeafPin)
            _txtKeyPin = New TextBox With {.Name = "rat_KeyPin", .Width = ContentWidth, .ReadOnly = True, .Margin = New Padding(0, 0, 0, 2),
                .AccessibleName = Localization.T("Отпечаток сертификата сервера:").TrimEnd(":"c)}
            body.Controls.Add(_txtKeyPin)
            _btnForget = CommandButton("rat_Forget", Localization.T("Забыть принятый сертификат"), "action.refresh", AddressOf OnForgetClicked)
            body.Controls.Add(_btnForget)
            _btnRemove = CommandButton("rat_Remove", Localization.T("Удалить сервер из списка.."), "action.delete", AddressOf OnRemoveClicked, ButtonGlyphRole.Caution)
            body.Controls.Add(_btnRemove)
            ShowPins(current)
        End If

        _lblStatus = Note("")
        _lblStatus.Visible = False
        body.Controls.Add(_lblStatus)

        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "rat_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        Dim save As New Button With {.Name = "rat_Save", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 4, 12, 4), .Text = Localization.T("Сохранить")}
        ButtonGlyphs.Apply(save, "action.save", ButtonGlyphRole.Affirmative)
        AddHandler save.Click, AddressOf OnSaveClicked
        Dim cancel As New Button With {.Name = "rat_Cancel", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 4, 12, 4), .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}
        ButtonGlyphs.Apply(cancel, "action.cancel", ButtonGlyphRole.Dismissing)
        DpiLayout.AddAnswer(_answerRow, save, DialogButtonRole.Affirmative)
        DpiLayout.AddAnswer(_answerRow, cancel, DialogButtonRole.Dismissing)

        Me.Controls.Add(body)
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        For Each box As TextBox In {_txtName, _txtAddress, _txtLeafPin, _txtKeyPin}
            If box IsNot Nothing Then box.RightToLeft = RightToLeft.No
        Next
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ThemeService.Attach(Me)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
        _txtName.Focus()
    End Sub

    ''' <summary>The accepted fingerprints of the record, or the words that none is held.</summary>
    Private Sub ShowPins(target As ExchangeAdminTarget)
        Dim held As Boolean = target IsNot Nothing AndAlso (target.LeafPin.Length > 0 OrElse target.KeyPin.Length > 0)
        _lblPins.Text = If(held, Localization.T("Принятый сертификат сервера (отпечатки):"), Localization.T("Сертификат этого сервера ещё не принят."))
        _txtLeafPin.Text = If(target Is Nothing, "", target.LeafPin)
        _txtKeyPin.Text = If(target Is Nothing, "", target.KeyPin)
        _txtLeafPin.Visible = held AndAlso _txtLeafPin.Text.Length > 0
        _txtKeyPin.Visible = held AndAlso _txtKeyPin.Text.Length > 0
        _btnForget.Enabled = held
    End Sub

    Private Sub ShowRefusal(result As ExchangeAdminTargetResult)
        Dim sentence As String = RefusalSentence(result)
        _lblNameRefusal.Visible = False
        _lblAddressRefusal.Visible = False
        Select Case result
            Case ExchangeAdminTargetResult.BadName
                _lblNameRefusal.Text = sentence
                _lblNameRefusal.Visible = True
                _txtName.Focus()
            Case Else
                _lblAddressRefusal.Text = sentence
                _lblAddressRefusal.Visible = True
                _txtAddress.Focus()
        End Select
    End Sub

    Private Sub OnSaveClicked(sender As Object, e As EventArgs)
        Dim saved As ExchangeAdminTarget = Nothing
        Dim result As ExchangeAdminTargetResult
        If Editing Then
            result = _targets.Edit(_editId, _txtName.Text, _txtAddress.Text, saved)
        Else
            result = _targets.Add(_txtName.Text, _txtAddress.Text, saved)
        End If
        If result <> ExchangeAdminTargetResult.Saved Then
            ShowRefusal(result)
            Return
        End If
        _savedId = saved.Id
        Me.DialogResult = DialogResult.OK
    End Sub

    Private Sub OnForgetClicked(sender As Object, e As EventArgs)
        Dim current As ExchangeAdminTarget = _targets.Find(_editId)
        If current Is Nothing Then
            ShowRefusal(ExchangeAdminTargetResult.NotFound)
            Return
        End If
        Dim question As String = Localization.TF("Забыть принятый сертификат сервера «{0}» ({1})? При следующем входе вас снова спросят о доверии.",
                                                 current.Name, Localization.LtrValue(current.Address))
        If ManagerBox.Show(Me, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        If _targets.ForgetCertificate(_editId) Then
            ShowPins(_targets.Find(_editId))
            _lblStatus.Text = Localization.T("Принятый сертификат забыт. При следующем входе вы решите заново.")
            _lblStatus.Visible = True
        Else
            ShowRefusal(ExchangeAdminTargetResult.NotFound)
        End If
    End Sub

    Private Sub OnRemoveClicked(sender As Object, e As EventArgs)
        Dim current As ExchangeAdminTarget = _targets.Find(_editId)
        If current Is Nothing Then
            ShowRefusal(ExchangeAdminTargetResult.NotFound)
            Return
        End If
        Dim question As String = Localization.TF("Удалить сервер «{0}» ({1}) из списка? Сам сервер и его данные не изменятся.",
                                                 current.Name, Localization.LtrValue(current.Address))
        If ManagerBox.Show(Me, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        If _targets.Remove(_editId) Then
            _removed = True
            Me.DialogResult = DialogResult.OK
        Else
            ShowRefusal(ExchangeAdminTargetResult.NotFound)
        End If
    End Sub

End Class
