Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Dialog for editing per-account limits overrides (ticket 224 rule 5 and rule 14).
''' Allows overriding devices, resources, broadcasts (per account and per device) and the clipboard byte cap for one
''' account. A value of 0 means using the host default. The result REPLACES the account's own limits as a whole, so
''' every limit the account has must have a field here - one without it would be dropped by the save.
''' </summary>
Friend NotInheritable Class Exchange_Account_Limits_Form
    Inherits Form

    Private Const ContentWidth As Integer = 420

    Private ReadOnly _login As String
    Private _numDevices As NumericUpDown
    Private _numResources As NumericUpDown
    Private _numBroadcasts As NumericUpDown
    Private _numBroadcastsPerDevice As NumericUpDown
    Private _numClipboardMB As NumericUpDown
    Private _answerRow As DialogButtonRow
    Private _iconHandle As IntPtr
    Private _limits As ExchangeAccountLimits
    ' the clipboard limit is edited in whole megabytes, but the store keeps bytes: a value the field cannot show
    ' (a CLI-set 512 KB, or a size that is not a whole number of MB) is kept as it was while the field is left alone
    Private _originalClipboardBytes As Long
    Private _originalClipboardMB As Integer

    Friend Sub New(login As String, currentLimits As ExchangeAccountLimits)
        _login = If(login, "")
        _limits = currentLimits
        BuildUi()
    End Sub

    Public ReadOnly Property Limits As ExchangeAccountLimits
        Get
            Return _limits
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
        Me.Text = Localization.TF("Лимиты аккаунта: {0}", _login)
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 12, 14, 6)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        body.Controls.Add(ExchangeMark.Heading(Text, 32, AddressOf CompanionAssets.OpenBundledAsset))
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 8),
            .Text = Localization.T("Индивидуальные ограничения для данного аккаунта. Значение 0 означает использование настроек сервера по умолчанию.")})

        Dim devVal As Integer = If(_limits IsNot Nothing AndAlso _limits.DevicesPerAccount.HasValue, _limits.DevicesPerAccount.Value, 0)
        Dim resVal As Integer = If(_limits IsNot Nothing AndAlso _limits.ResourcesPerAccount.HasValue, _limits.ResourcesPerAccount.Value, 0)
        Dim bcastVal As Integer = If(_limits IsNot Nothing AndAlso _limits.BroadcastsPerAccount.HasValue, _limits.BroadcastsPerAccount.Value, 0)
        Dim bcastDevVal As Integer = If(_limits IsNot Nothing AndAlso _limits.BroadcastsPerDevice.HasValue, _limits.BroadcastsPerDevice.Value, 0)
        Dim clipMbVal As Integer = 0
        If _limits IsNot Nothing AndAlso _limits.ClipboardMaxBytes.HasValue Then
            _originalClipboardBytes = _limits.ClipboardMaxBytes.Value
            clipMbVal = CInt(Math.Min(Integer.MaxValue, Math.Max(0L, _originalClipboardBytes \ (1024L * 1024L))))
        End If
        _originalClipboardMB = clipMbVal

        ' the store accepts any number of at least zero: a field never refuses (nor throws on) a limit already set
        _numDevices = New NumericUpDown With {.Minimum = 0, .Maximum = Math.Max(1000, devVal), .Value = devVal, .Width = 120,
            .AccessibleName = Localization.T("Максимум устройств")}
        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = Localization.T("Максимум устройств (0 = по умолчанию):")})
        body.Controls.Add(_numDevices)

        _numResources = New NumericUpDown With {.Minimum = 0, .Maximum = Math.Max(10000, resVal), .Value = resVal, .Width = 120,
            .AccessibleName = Localization.T("Максимум ресурсов")}
        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Text = Localization.T("Максимум опубликованных ресурсов (0 = по умолчанию):")})
        body.Controls.Add(_numResources)

        _numBroadcasts = New NumericUpDown With {.Minimum = 0, .Maximum = Math.Max(1000, bcastVal), .Value = bcastVal, .Width = 120,
            .AccessibleName = Localization.T("Максимум трансляций")}
        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Text = Localization.T("Максимум трансляций (0 = по умолчанию):")})
        body.Controls.Add(_numBroadcasts)

        _numBroadcastsPerDevice = New NumericUpDown With {.Minimum = 0, .Maximum = Math.Max(1000, bcastDevVal), .Value = bcastDevVal, .Width = 120,
            .AccessibleName = Localization.T("Максимум трансляций на устройство")}
        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Text = Localization.T("Максимум трансляций на одно устройство (0 = по умолчанию):")})
        body.Controls.Add(_numBroadcastsPerDevice)

        _numClipboardMB = New NumericUpDown With {.Minimum = 0, .Maximum = Math.Max(1048576, clipMbVal), .Value = clipMbVal, .Width = 120,
            .AccessibleName = Localization.T("Лимит буфера, МБ")}
        body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Text = Localization.T("Лимит буфера, МБ (0 = по умолчанию 5 ГБ):")})
        body.Controls.Add(_numClipboardMB)

        ' The answers ride the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): Cancel first, Save last
        ' and the default (the limits can be changed again), Escape = Cancel.
        Dim btnOk As Button = NewButton(Localization.T("Сохранить"), AddressOf OnOkClicked, "action.confirm", ButtonGlyphRole.Affirmative)
        btnOk.Name = "xlim_Ok"
        Dim btnCancel As Button = NewButton(Localization.T("Отмена"), Sub() Me.DialogResult = DialogResult.Cancel, "nav.close", ButtonGlyphRole.Dismissing)
        btnCancel.Name = "xlim_Cancel"
        btnCancel.DialogResult = DialogResult.Cancel
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xlim_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        DpiLayout.AddAnswer(_answerRow, btnOk, DialogButtonRole.Affirmative, "action.confirm")
        DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing, "nav.close")

        Me.Controls.Add(body)
        ' Last in the collection: the docked row takes its edge first and reads after the content in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)
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
        Dim d As Integer = CInt(_numDevices.Value)
        Dim r As Integer = CInt(_numResources.Value)
        Dim b As Integer = CInt(_numBroadcasts.Value)
        Dim bd As Integer = CInt(_numBroadcastsPerDevice.Value)
        Dim clipMb As Integer = CInt(_numClipboardMB.Value)
        Dim c As Long = CLng(clipMb) * 1024L * 1024L
        ' the field is untouched: keep the exact bytes the store holds (a 512 KB limit shows as 0 MB)
        If clipMb = _originalClipboardMB AndAlso _originalClipboardBytes > 0 Then c = _originalClipboardBytes

        Dim res As New ExchangeAccountLimits()
        Dim hasAny As Boolean = False
        If d > 0 Then
            res.DevicesPerAccount = d
            hasAny = True
        End If
        If r > 0 Then
            res.ResourcesPerAccount = r
            hasAny = True
        End If
        If b > 0 Then
            res.BroadcastsPerAccount = b
            hasAny = True
        End If
        If bd > 0 Then
            res.BroadcastsPerDevice = bd
            hasAny = True
        End If
        If c > 0 Then
            res.ClipboardMaxBytes = c
            hasAny = True
        End If

        _limits = If(hasAny, res, Nothing)
        Me.DialogResult = DialogResult.OK
    End Sub
End Class
