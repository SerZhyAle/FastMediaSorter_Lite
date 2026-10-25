Option Strict On
Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms

' DEVICE-EXCHANGE item DJ. Only the local producing device offers its targets; the agent owns all sealing.
Friend NotInheritable Class Exchange_Guest_Grant_Form
    Inherits Form

    Private ReadOnly _agent As ExchangeDeviceAgent
    Private _iconHandle As IntPtr
    Private ReadOnly _target As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 420}
    Private ReadOnly _guest As New TextBox With {.Width = 420, .MaxLength = 128}
    Private ReadOnly _result As New Label With {.AutoSize = True, .MaximumSize = New Size(420, 0)}
    Private ReadOnly _cancel As New CancellationTokenSource()
    Private ReadOnly _give As New Button With {.AutoSize = True}
    Private ReadOnly _remove As New Button With {.AutoSize = True}

    Private NotInheritable Class Choice
        Public Id As String
        Public Broadcast As Boolean
        Public Caption As String
        Public Overrides Function ToString() As String
            Return Caption
        End Function
    End Class

    Public Sub New(agent As ExchangeDeviceAgent)
        _agent = agent
        Text = Localization.T("Гостевой доступ..")
        Icon = ShareIcons.CreateIcon(_iconHandle)
        UiLanguage.ApplyTo(Me)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Dim body As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Padding = New Padding(12)}
        body.Controls.Add(ExchangeMark.Heading(Text, 32, AddressOf CompanionAssets.OpenBundledAsset))
        body.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Ресурс")})
        body.Controls.Add(_target)
        body.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Гостевой аккаунт")})
        body.Controls.Add(_guest)
        Dim snapshot = agent.GetFullDirectory()
        Dim ownId = agent.GetStatus().DeviceId
        For Each resource In snapshot.Resources
            If OutwardAccessFeatures.IsOffered() AndAlso resource.DeviceId = ownId Then _target.Items.Add(New Choice With {.Id = resource.ResourceId, .Caption = resource.Name})
        Next
        If Not OutwardAccessFeatures.IsOffered() Then body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(420, 0), .Text = OutwardAccessFeatures.Reason()})
        For Each broadcast In snapshot.Broadcasts
            If broadcast.DeviceId = ownId Then _target.Items.Add(New Choice With {.Id = broadcast.BroadcastId, .Caption = broadcast.Title, .Broadcast = True})
        Next
        If _target.Items.Count > 0 Then _target.SelectedIndex = 0
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True}
        _give.Text = Localization.T("Предоставить доступ")
        _remove.Text = Localization.T("Отозвать")
        ButtonGlyphs.Apply(_give, "action.confirm")
        ButtonGlyphs.Apply(_remove, "action.remove")
        buttons.Controls.AddRange({_give, _remove})
        Dim close As New Button With {.AutoSize = True, .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.Cancel}
        ButtonGlyphs.Apply(close, "action.cancel", ButtonGlyphRole.Dismissing)
        buttons.Controls.Add(close)
        CancelButton = close
        body.Controls.Add(buttons)
        body.Controls.Add(_result)
        Controls.Add(body)
        AddHandler _give.Click, Sub() Send(False)
        AddHandler _remove.Click, Sub() Send(True)
        AddHandler FormClosed, Sub()
                                   _cancel.Cancel()
                                   _cancel.Dispose()
                                   ShareIcons.FreeIcon(Icon, _iconHandle)
                               End Sub
        DpiLayout.ApplyAutoScale(Me)
        ThemeService.Attach(Me)
    End Sub

    Private Async Sub Send(remove As Boolean)
        Dim selected = TryCast(_target.SelectedItem, Choice)
        If selected Is Nothing OrElse _guest.Text.Trim().Length = 0 Then Return
        _give.Enabled = False
        _remove.Enabled = False
        Try
            Dim code = Await _agent.GrantGuestAsync(_guest.Text, selected.Id, selected.Broadcast, remove, _cancel.Token)
            If IsDisposed Then Return
            _result.Text = If(code.Length = 0, Localization.T("Запрос отправлен"), ConnectionDiagnosisLocalization.GetMessageText(ExchangeAgentPipe.ErrorMessage(code).Code))
        Catch ex As OperationCanceledException
        Finally
            If Not IsDisposed Then
                _give.Enabled = True
                _remove.Enabled = True
            End If
        End Try
    End Sub
End Class
