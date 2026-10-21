Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Window shown ONCE when a new invitation code is generated (ticket 224 rule 1).
''' Displays the capability code, the connection string carrying the invitation, and offers QR / copy buttons.
''' Wipes the code and string on close so they do not outlive the window.
''' </summary>
Friend NotInheritable Class Exchange_Invitation_Created_Form
    Inherits Form

    Private Const ContentWidth As Integer = 460

    Private _code As String
    Private _connString As String
    Private _kind As String
    Private _txtCode As TextBox
    Private _txtConnString As TextBox
    Private _lblResult As Label
    Private _answerRow As DialogButtonRow
    Private _iconHandle As IntPtr

    Friend Sub New(code As String, connString As String, kind As String)
        _code = If(code, "")
        _connString = If(connString, "")
        _kind = If(kind, "member")
        BuildUi()
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
        Me.Text = Localization.T("Приглашение создано")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                _code = ""
                _connString = ""
                ' the one-time code also sits in the boxes' own text: a closed window must not keep it
                If _txtCode IsNot Nothing Then _txtCode.Clear()
                If _txtConnString IsNot Nothing Then _txtConnString.Clear()
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New FlowLayoutPanel With {.Dock = DockStyle.Top, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 10, 14, 8)}

        Dim kindText As String = If(String.Equals(_kind, "guest", StringComparison.OrdinalIgnoreCase), Localization.T("Гость"), Localization.T("Своё устройство или семья"))
        body.Controls.Add(ExchangeMark.Heading(Text, 32, AddressOf CompanionAssets.OpenBundledAsset))
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 4),
            .Font = New Font(Me.Font, FontStyle.Bold), .Text = Localization.TF("Тип приглашения: {0}", kindText)})

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = CollapsibleSection.AttentionColor,
            .Margin = New Padding(0, 0, 0, 8), .Text = Localization.T("Приглашение показывается один раз. Сохраните код или передайте строку подключения пользователю.")})

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 4, 0, 2),
            .Text = Localization.T("Код приглашения:")})
        _txtCode = New TextBox With {.ReadOnly = True, .Width = ContentWidth, .Text = _code, .Margin = New Padding(0, 0, 0, 4),
            .AccessibleName = Localization.T("Код приглашения")}
        body.Controls.Add(_txtCode)

        Dim codeBtnRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 0, 0, 6)}
        Dim btnCopyCode As Button = NewButton(Localization.T("Копировать код"), AddressOf OnCopyCode, "action.copy")
        codeBtnRow.Controls.Add(btnCopyCode)
        body.Controls.Add(codeBtnRow)

        If _connString.Length > 0 Then
            body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 4, 0, 2),
                .Text = Localization.T("Строка подключения с приглашением:")})
            _txtConnString = New TextBox With {.ReadOnly = True, .Multiline = True, .WordWrap = True, .ScrollBars = ScrollBars.Vertical,
                .Width = ContentWidth, .Height = 64, .Text = _connString, .Margin = New Padding(0, 0, 0, 4),
                .AccessibleName = Localization.T("Строка подключения")}
            body.Controls.Add(_txtConnString)

            Dim connBtnRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 0, 0, 6)}
            Dim btnCopyConn As Button = NewButton(Localization.T("Копировать строку"), AddressOf OnCopyConnString, "action.copy")
            Dim btnQr As Button = NewButton(Localization.T("Показать QR-код"), AddressOf OnShowQr, "action.show-code")
            connBtnRow.Controls.Add(btnCopyConn)
            connBtnRow.Controls.Add(btnQr)
            body.Controls.Add(connBtnRow)
        End If

        _lblResult = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 4, 0, 4), .Visible = False}
        body.Controls.Add(_lblResult)

        ' Close is the window's one answer and rides the shared answer row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4);
        ' Enter and Escape both reach it, as before. The copy buttons above stay beside the field each one copies.
        Dim btnClose As Button = NewButton(Localization.T("Закрыть"), Sub() Me.Close(), "nav.close", ButtonGlyphRole.Dismissing)
        btnClose.Name = "xinvd_Close"
        btnClose.DialogResult = DialogResult.OK
        _answerRow = New DialogButtonRow() With {.Name = "xinvd_Buttons", .Margin = New Padding(0, 6, 0, 2), .Size = New Size(ContentWidth, UiSizing.PointerTarget)}
        DpiLayout.AddAnswer(_answerRow, btnClose, DialogButtonRole.Dismissing, "nav.close")
        body.Controls.Add(_answerRow)
        _answerRow.Bind(Me)
        Me.Controls.Add(body)
        Me.AutoScroll = True
        Me.ClientSize = body.PreferredSize

        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
    End Sub

    Private Sub Say(text As String, ok As Boolean)
        _lblResult.ForeColor = If(ok, ThemeService.Palette.TextMuted, CollapsibleSection.AttentionColor)
        _lblResult.Text = text
        _lblResult.Visible = text.Length > 0
    End Sub

    Private Sub OnCopyCode()
        If _code.Length = 0 Then Return
        If SecureClipboard.SetSecret(_code) Then
            Say(Localization.T("Код приглашения скопирован в буфер обмена."), True)
        Else
            Say(Localization.T("Не удалось скопировать код в буфер обмена."), False)
        End If
    End Sub

    Private Sub OnCopyConnString()
        If _connString.Length = 0 Then Return
        If SecureClipboard.SetSecret(_connString) Then
            Say(Localization.T("Строка подключения скопирована в буфер обмена."), True)
        Else
            Say(Localization.T("Не удалось скопировать строку в буфер обмена."), False)
        End If
    End Sub

    Private Sub OnShowQr()
        If _connString.Length = 0 Then Return
        Dim png As Byte() = ShareConfigBuilder.QrPngForText(_connString)
        If png Is Nothing Then
            Say(ExchangePublishText.NoQr(), False)
            Return
        End If
        Try
            Using ms As New MemoryStream(png)
                Using img As Image = Image.FromStream(ms)
                    Qr_Zoom_Form.ShowImage(Me, img, "exchange-invitation", carriesAccess:=False)
                End Using
            End Using
        Catch ex As ArgumentException
            Say(ExchangePublishText.NoQr(), False)
        End Try
    End Sub
End Class
