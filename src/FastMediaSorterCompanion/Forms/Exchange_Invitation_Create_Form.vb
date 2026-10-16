Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Dialog for creating an invitation to the exchange server (ticket 224 rule 1 and rule 3).
''' Offers a choice of family (full account) or guest account (announces nothing, sees only granted resources).
''' Ticket 239 CR-13 (Q239-2): the guest choice is shown as not available, with the reason, while
''' <see cref="OutwardAccessFeatures.Offered"/> is False; the dialog then creates a family invitation only.
''' </summary>
Friend NotInheritable Class Exchange_Invitation_Create_Form
    Inherits Form

    Private Const ContentWidth As Integer = 380

    Private _rbFamily As RadioButton
    Private _rbGuest As RadioButton
    Private _iconHandle As IntPtr

    Friend Sub New()
        BuildUi()
    End Sub

    Public ReadOnly Property Kind As String
        Get
            Return If(OutwardAccessFeatures.IsOffered() AndAlso _rbGuest IsNot Nothing AndAlso _rbGuest.Checked, "guest", "member")
        End Get
    End Property

    Private Function NewButton(text As String, onClick As Action, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Создать приглашение")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 10, 14, 8)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 8),
            .Text = Localization.T("Выберите тип создаваемого приглашения для регистрации пользователя:")})

        _rbFamily = New RadioButton With {.AutoSize = True, .Checked = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Своё устройство или семья")}
        body.Controls.Add(_rbFamily)
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(20, 0, 0, 6), .Text = Localization.T("Создаёт полноценный аккаунт с возможностью публикации ресурсов.")})

        _rbGuest = New RadioButton With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2),
            .Text = Localization.T("Гость"), .Enabled = OutwardAccessFeatures.IsOffered()}
        RefreshGuestGlyph()
        AddHandler _rbGuest.ForeColorChanged, Sub() RefreshGuestGlyph()
        AddHandler _rbGuest.DpiChangedAfterParent, Sub() RefreshGuestGlyph()
        _rbGuest.ImageAlign = ContentAlignment.MiddleLeft
        _rbGuest.TextImageRelation = TextImageRelation.ImageBeforeText
        AddHandler Me.Disposed, Sub() _rbGuest.Image.Dispose()
        body.Controls.Add(_rbGuest)
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(20, 0, 0, 10),
            .Text = If(OutwardAccessFeatures.IsOffered(), Localization.T("Видит только явно предоставленные ему ресурсы и ничего не публикует."), OutwardAccessFeatures.Reason())})

        Dim btnRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 8, 0, 2)}
        Dim btnOk As Button = NewButton(Localization.T("Создать"), Sub() Me.DialogResult = DialogResult.OK, "action.confirm", ButtonGlyphRole.Affirmative)
        btnOk.DialogResult = DialogResult.OK
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

    Private Sub RefreshGuestGlyph()
        Dim oldImage As Image = _rbGuest.Image
        _rbGuest.Image = GlyphRenderer.Render(IconMap.GetMeaning("_rbGuest").GlyphId,
            _rbGuest.LogicalToDeviceUnits(20), _rbGuest.ForeColor, AddressOf CompanionAssets.OpenBundledAsset)
        oldImage?.Dispose()
    End Sub
End Class
