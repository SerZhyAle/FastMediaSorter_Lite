Option Strict On

Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Spec 228 rule 4: "Give access outside.." offers two things and nothing else:
''' 1. A guest invitation (spec 224 guest kind). It is NOT bound to this folder: a guest account sees what is granted to it on the
'''    exchange server, and the Share Manager's file server cannot yet limit a login to one folder.
''' 2. A public link with a password, no account.
''' Ticket 239 CR-13 (decision Q239-2): both are shown as NOT AVAILABLE while <see cref="OutwardAccessFeatures.Offered"/> is False
''' (ANYWHERE-ACCESS amendment L) - the reason is on screen and nothing can be created. Withdrawing what already exists stays
''' possible; withdrawing ends the open sessions it allowed.
''' </summary>
Friend NotInheritable Class Folder_Outside_Access_Form
    Inherits Form

    Private Const ContentWidth As Integer = 500

    Private ReadOnly _shareName As String
    Private ReadOnly _hostPath As String
    Private ReadOnly _publisher As ExchangePublisher
    Private _iconHandle As IntPtr

    Private _lblFolderHeader As Label
    Private _lblGuestReason As Label
    Private _lblLinkReason As Label
    Private _lblLinkStatus As Label
    Private _txtLinkPassword As TextBox
    Private _btnGeneratePassword As Button
    Private _btnCreateLink As Button
    Private _btnWithdrawLink As Button
    Private _btnShowLinkQr As Button
    Private _btnCreateGuest As Button
    Private _lvGuests As ListView
    Private _btnWithdrawGuest As Button
    Private _lblStatusResult As Label
    Private _answerRow As DialogButtonRow
    Private _body As TableLayoutPanel

    Friend Sub New(shareName As String, hostPath As String, publisher As ExchangePublisher)
        _shareName = If(shareName, "")
        _hostPath = If(hostPath, "")
        _publisher = publisher
        BuildUi()
        RefreshState()
    End Sub

    Private Function NewButton(text As String, onClick As Action, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Дать доступ наружу..")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 10, 14, 8), .AutoScroll = True}
        _body = body
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _lblFolderHeader = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 12),
            .Font = New Font(Me.Font, FontStyle.Bold), .Text = Localization.TF("Папка: «{0}» ({1})", _shareName, _hostPath)}
        body.Controls.Add(_lblFolderHeader)

        ' --- Section 1: Guest invitation for this folder ---
        Dim grpGuest As New GroupBox With {.Text = Localization.T("Гостевое приглашение"), .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Dock = DockStyle.Top, .Padding = New Padding(10, 8, 10, 8), .Margin = New Padding(0, 0, 0, 12)}
        Dim pnlGuest As New FlowLayoutPanel With {.Dock = DockStyle.Top, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}

        ' The old line here said the guest account "can only access this folder"; the server does not enforce that (CR-13), so the
        ' line is the reason the invitation is closed - nothing about the folder is promised.
        _lblGuestReason = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth - 30, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Text = OutwardAccessFeatures.Reason()}
        pnlGuest.Controls.Add(_lblGuestReason)

        Dim rowGuestButtons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 6, 0, 6)}
        _btnCreateGuest = NewButton(Localization.T("Создать гостевое приглашение.."), AddressOf OnCreateGuestClicked, "action.add")
        _btnWithdrawGuest = NewButton(Localization.T("Отозвать выбранное"), AddressOf OnWithdrawGuestClicked, "action.remove", ButtonGlyphRole.Irreversible)
        _btnWithdrawGuest.Enabled = False
        rowGuestButtons.Controls.AddRange(New Control() {_btnCreateGuest, _btnWithdrawGuest})
        pnlGuest.Controls.Add(rowGuestButtons)

        _lvGuests = New ListView With {.View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False,
            .Width = ContentWidth - 30, .Height = 100}
        _lvGuests.Columns.Add(Localization.T("Тип"), 80)
        _lvGuests.Columns.Add(Localization.T("Создано"), 130)
        _lvGuests.Columns.Add(Localization.T("Использовано"), 100)
        AddHandler _lvGuests.SelectedIndexChanged, Sub() _btnWithdrawGuest.Enabled = _lvGuests.SelectedItems.Count > 0
        pnlGuest.Controls.Add(_lvGuests)

        grpGuest.Controls.Add(pnlGuest)
        body.Controls.Add(grpGuest)

        ' --- Section 2: Public link with a password, no account ---
        Dim grpLink As New GroupBox With {.Text = Localization.T("Ссылка с паролем (без аккаунта)"), .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Dock = DockStyle.Top, .Padding = New Padding(10, 8, 10, 8), .Margin = New Padding(0, 0, 0, 12)}
        Dim pnlLink As New FlowLayoutPanel With {.Dock = DockStyle.Top, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}

        _lblLinkReason = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth - 30, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 0, 0, 6), .Text = OutwardAccessFeatures.Reason()}
        pnlLink.Controls.Add(_lblLinkReason)
        _lblLinkStatus = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth - 30, 0), .Margin = New Padding(0, 0, 0, 6)}
        pnlLink.Controls.Add(_lblLinkStatus)

        Dim pnlPassword As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 4, 0, 6)}
        pnlPassword.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Пароль ссылки:"), .Margin = New Padding(0, 6, 8, 0)})
        _txtLinkPassword = New TextBox With {.Width = 180, .UseSystemPasswordChar = True}
        pnlPassword.Controls.Add(_txtLinkPassword)
        _btnGeneratePassword = NewButton(Localization.T("Сгенерировать"), AddressOf OnGeneratePasswordClicked)
        pnlPassword.Controls.Add(_btnGeneratePassword)
        pnlLink.Controls.Add(pnlPassword)

        Dim rowLinkButtons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 6, 0, 6)}
        _btnCreateLink = NewButton(Localization.T("Создать ссылку"), AddressOf OnCreateLinkClicked, "action.share")
        _btnWithdrawLink = NewButton(Localization.T("Отозвать ссылку"), AddressOf OnWithdrawLinkClicked, "action.remove", ButtonGlyphRole.Irreversible)
        _btnShowLinkQr = NewButton(Localization.T("Показать QR / Код.."), AddressOf OnShowLinkQrClicked, "content.qr-code")
        rowLinkButtons.Controls.AddRange(New Control() {_btnCreateLink, _btnWithdrawLink, _btnShowLinkQr})
        pnlLink.Controls.Add(rowLinkButtons)

        grpLink.Controls.Add(pnlLink)
        body.Controls.Add(grpLink)

        _lblStatusResult = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 0, 0, 8)}
        body.Controls.Add(_lblStatusResult)

        ' Close is the window's one answer and rides the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4).
        ' The buttons inside the two groups stay where they are: they are the commands of their own list, not answers.
        ' Escape = Close. Enter is NOT bound (bindAccept:=False): as before, the window has no default button, so Enter in
        ' the link-password field cannot close the window and drop what was typed.
        Dim btnClose As Button = NewButton(Localization.T("Закрыть"), Sub() Me.Close())
        btnClose.Name = "xfo_Close"
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xfo_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        DpiLayout.AddAnswer(_answerRow, btnClose, DialogButtonRole.Dismissing, "nav.close")

        Me.Controls.Add(body)
        ' Last in the collection: the docked row takes its edge first and reads after the content in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me, bindAccept:=False)
        Me.ClientSize = New Size(ContentWidth + 30, body.PreferredSize.Height + 20 + _answerRow.GetPreferredSize(Size.Empty).Height)

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' The client height was authored from the body's preferred height at 96 dpi. Once scaled, a caption that wraps one
        ' line more (a longer language, a bigger scale) makes the body taller than the scaled value, and the Fill body would
        ' then run under the answer row; grow the window by exactly the missing amount (never shrink it). When the monitor
        ' cannot hold that height (this window is tall at 175 %), ClampToWorkingArea caps it and the body scrolls instead of
        ' leaving its last lines hidden behind the row.
        PerformAutoScale()
        Dim need As Integer = _body.GetPreferredSize(New Size(ClientSize.Width, 0)).Height + _answerRow.GetPreferredSize(New Size(ClientSize.Width, 0)).Height
        If need > ClientSize.Height Then ClientSize = New Size(ClientSize.Width, need)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
    End Sub

    ''' <summary>CR-13: the reason is on screen and every control that CREATES something (a guest invitation, a link, its password)
    ''' is off while the outward-access gate is closed. Taking access back (the Withdraw buttons) is decided by what exists, in
    ''' <see cref="RefreshState"/> and the list's selection, never by the gate.</summary>
    Private Sub ApplyAvailability()
        Dim offered As Boolean = OutwardAccessFeatures.IsOffered()
        _lblGuestReason.Visible = Not offered
        _lblLinkReason.Visible = Not offered
        _btnCreateGuest.Enabled = offered
        _txtLinkPassword.Enabled = offered
        _btnGeneratePassword.Enabled = offered
        _btnCreateLink.Enabled = offered
    End Sub

    Private Sub OnGeneratePasswordClicked()
        If Not OutwardAccessFeatures.IsOffered() Then Return
        Dim bytes As Byte() = New Byte(11) {}
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes)
        _txtLinkPassword.Text = Convert.ToBase64String(bytes).Replace("+"c, "x"c).Replace("/"c, "y"c).TrimEnd("="c)
        _txtLinkPassword.UseSystemPasswordChar = False
    End Sub

    Private Sub RefreshState()
        Dim pub As ExchangePublisher = _publisher
        ApplyAvailability()
        If pub Is Nothing Then
            _lblLinkStatus.Text = Localization.T("Служба публикаций недоступна.")
            _btnCreateLink.Enabled = False
            _btnWithdrawLink.Enabled = False
            _btnShowLinkQr.Enabled = False
            _btnCreateGuest.Enabled = False
            Return
        End If

        Dim code As String = ""
        Dim hasLink As Boolean = pub.TryGetLinkCode(_shareName, code)
        If hasLink Then
            _lblLinkStatus.Text = Localization.T("Ссылка активна.")
            _btnCreateLink.Text = Localization.T("Обновить ссылку")
            _btnWithdrawLink.Enabled = True
            ' showing the code or the QR hands the link out again: that is outward access too
            _btnShowLinkQr.Enabled = OutwardAccessFeatures.IsOffered()
        Else
            _lblLinkStatus.Text = Localization.T("Ссылка не создана.")
            _btnCreateLink.Text = Localization.T("Создать ссылку")
            _btnWithdrawLink.Enabled = False
            _btnShowLinkQr.Enabled = False
        End If

        LoadGuestInvitationsAsync()
    End Sub

    Private Async Sub LoadGuestInvitationsAsync()
        _lvGuests.Items.Clear()
        Try
            Dim rep As ExchangeAdminReply = Await ExchangeAdmin.ListInvitationsAsync(CancellationToken.None).ConfigureAwait(True)
            If rep.Ok Then
                Dim list As List(Of ExchangeAdminInvitation) = ExchangeAdminParser.Invitations(rep.DataJson)
                For Each inv In list
                    If String.Equals(inv.Kind, "guest", StringComparison.OrdinalIgnoreCase) Then
                        Dim item As New ListViewItem(Localization.T("гость")) With {.Tag = inv.CodeHash}
                        item.SubItems.Add(inv.CreatedAt)
                        item.SubItems.Add(If(inv.Used, Localization.T("Да"), Localization.T("Нет")))
                        _lvGuests.Items.Add(item)
                    End If
                Next
            End If
        Catch ex As Exception
            ' Offline or admin pipe not reached
        End Try
    End Sub

    Private Async Sub OnCreateLinkClicked()
        If _publisher Is Nothing OrElse Not OutwardAccessFeatures.IsOffered() Then Return
        Dim pw As String = _txtLinkPassword.Text.Trim()
        _lblStatusResult.Text = Localization.T("Создание ссылки..")
        Dim res As ExchangeLinkResult = Await _publisher.CreateLinkAsync(_shareName, CancellationToken.None, pw).ConfigureAwait(True)
        If res.Outcome = ExchangeLinkOutcome.Done Then
            _lblStatusResult.Text = Localization.T("Ссылка успешно создана.")
            RefreshState()
        Else
            _lblStatusResult.Text = Localization.T("Не удалось создать ссылку.")
        End If
    End Sub

    Private Async Sub OnWithdrawLinkClicked()
        If _publisher Is Nothing Then Return
        If ManagerBox.Show(Me, Localization.T("Отозвать ссылку наружу для этой папки? Все активные сессии по ней будут завершены."),
                           Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) <> DialogResult.OK Then Return
        _lblStatusResult.Text = Localization.T("Отзыв ссылки..")
        Dim res As ExchangeLinkResult = Await _publisher.RemoveLinkAsync(_shareName, CancellationToken.None).ConfigureAwait(True)
        If res.Outcome = ExchangeLinkOutcome.Done Then
            _lblStatusResult.Text = Localization.T("Ссылка отозвана.")
            RefreshState()
        Else
            _lblStatusResult.Text = Localization.T("Не удалось отозвать ссылку.")
        End If
    End Sub

    Private Async Sub OnShowLinkQrClicked()
        If _publisher Is Nothing OrElse Not OutwardAccessFeatures.IsOffered() Then Return
        Dim code As String = ""
        If _publisher.TryGetLinkCode(_shareName, code) Then
            Dim configJson As String = ""
            Dim qr As Byte() = Nothing
            Try
                Dim status As WorkerStatus = Await ShareController.GetStatusAsync()
                Dim built As ShareConfigResult = _publisher.BuildLinkExport(_shareName, status)
                If built IsNot Nothing Then configJson = built.ConfigJson
            Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            End Try
            qr = ShareConfigBuilder.QrPngForText(code)
            Using dlg As New Exchange_Link_Form(_shareName, code, configJson, qr)
                dlg.ShowDialog(Me)
            End Using
        End If
    End Sub

    Private Async Sub OnCreateGuestClicked()
        If Not OutwardAccessFeatures.IsOffered() Then Return
        _lblStatusResult.Text = Localization.T("Создание гостевого приглашения..")
        Try
            Dim rep As ExchangeAdminReply = Await ExchangeAdmin.CreateInvitationAsync("guest", CancellationToken.None).ConfigureAwait(True)
            If rep.Ok Then
                Dim created As ExchangeAdminInvitationCreated = ExchangeAdminParser.InvitationCreated(rep.DataJson)
                If created IsNot Nothing AndAlso Not String.IsNullOrEmpty(created.Code) Then
                    _lblStatusResult.Text = Localization.T("Гостевое приглашение создано.")
                    Using dlg As New Exchange_Invitation_Created_Form(created.Code, "", "guest")
                        dlg.ShowDialog(Me)
                    End Using
                    RefreshState()
                    Return
                End If
            End If
            _lblStatusResult.Text = Localization.T("Не удалось создать приглашение на сервере.")
        Catch ex As Exception
            AppFileLogger.LogException("Folder_Outside_Access_Form.CreateGuest", ex)
            _lblStatusResult.Text = Localization.T("Не удалось создать приглашение на сервере.")
        End Try
    End Sub

    Private Async Sub OnWithdrawGuestClicked()
        If _lvGuests.SelectedItems.Count = 0 Then Return
        Dim codeHash As String = Convert.ToString(_lvGuests.SelectedItems(0).Tag)
        If String.IsNullOrEmpty(codeHash) Then Return
        If ManagerBox.Show(Me, Localization.T("Отозвать это гостевое приглашение?"), Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) <> DialogResult.OK Then Return
        _lblStatusResult.Text = Localization.T("Отзыв приглашения..")
        Try
            Dim rep As ExchangeAdminReply = Await ExchangeAdmin.WithdrawInvitationAsync(codeHash, CancellationToken.None).ConfigureAwait(True)
            If rep.Ok Then
                _lblStatusResult.Text = Localization.T("Приглашение отозвано.")
                RefreshState()
            Else
                _lblStatusResult.Text = Localization.T("Не удалось отозвать приглашение.")
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Folder_Outside_Access_Form.WithdrawGuest", ex)
            _lblStatusResult.Text = Localization.T("Не удалось отозвать приглашение.")
        End Try
    End Sub

End Class
