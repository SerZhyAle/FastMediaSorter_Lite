Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Implements IContextHelpHost

    Private _settingsHeader As TableLayoutPanel
    Friend ReadOnly Destination As SettingsDestination
    Friend Event NavigateRequested(destination As SettingsDestination)
    Friend Event SettingsChanged(destination As SettingsDestination)
    Private _applyingPort As Boolean
    Private _closeAfterPort As Boolean
    Private _applyingExchangeDrafts As Boolean
    Friend ConfirmDraftClose As Func(Of Boolean, DialogResult)

    Private Function AskDraftClose(exchange As Boolean) As DialogResult
        If ConfirmDraftClose IsNot Nothing Then Return ConfirmDraftClose(exchange)
        Dim prompt = If(exchange, Localization.T("Применить несохранённые изменения перед закрытием?"),
            Localization.T("Применить изменение порта перед закрытием? Да - применить, Нет - отбросить, Отмена - продолжить редактирование."))
        Return ManagerBox.Show(Me, prompt, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3)
    End Function

    Private Function ExchangeDraftPending() As Boolean
        Return Destination = SettingsDestination.ExchangeServer AndAlso
            (PortPendingX() OrElse EndpointPendingX() OrElse RangePendingX() OrElse
             (_chkXAwake IsNot Nothing AndAlso XHostPending()))
    End Function

    Private Async Sub ApplyExchangeDraftsAndClose()
        _applyingExchangeDrafts = True
        Try
            If PortPendingX() Then Await ApplyExchangePortAsync()
            If IsDisposed OrElse PortPendingX() Then Return
            If EndpointPendingX() Then Await ApplyExchangeEndpointAsync()
            If IsDisposed OrElse EndpointPendingX() Then Return
            If RangePendingX() Then Await ApplyExchangeRangeAsync()
            If IsDisposed OrElse RangePendingX() Then Return
            If _chkXAwake IsNot Nothing AndAlso XHostPending() Then Await ApplyExchangeHostAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange settings: applying pending groups", ex)
        Finally
            _applyingExchangeDrafts = False
        End Try
        If Not IsDisposed AndAlso Not ExchangeDraftPending() Then Close()
    End Sub

    Private ReadOnly Property CanHostHelpButton As Boolean Implements IContextHelpHost.CanHostHelpButton
        Get
            Return _settingsHeader IsNot Nothing
        End Get
    End Property

    Private Sub HostHelpButton(button As Button) Implements IContextHelpHost.HostHelpButton
        button.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        button.Margin = Padding.Empty
        ButtonGlyphs.Apply(button, "app.help")
        _settingsHeader.Controls.Add(button, 1, 0)
    End Sub

    Private Function DestinationCaption() As String
        Select Case Destination
            Case SettingsDestination.ExchangeServer : Return Localization.T("Настройки сервера обмена")
            Case Else : Return Localization.T("Настройки менеджера")
        End Select
    End Function

    Private Sub NotifySettingsChanged()
        If Not _loading AndAlso Not IsDisposed Then RaiseEvent SettingsChanged(Destination)
    End Sub

    ''' <summary>The Network page follows the worker's answer whenever the page exists (253 R-5), whatever window holds it.</summary>
    Friend Sub RefreshWorkerSnapshot(status As WorkerStatus)
        _status = status
        If _secNetwork Is Nothing OrElse IsDisposed Then Return
        If Not PortPending() AndAlso Not _applyingPort Then LoadPortState()
        UpdatePortNote()
        UpdateLanOnlyNote()
        UpdateSummaries()
    End Sub

    Private Sub BuildDestinationLinks()
        Dim links As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
        For Each target In {SettingsDestination.Manager, SettingsDestination.ExchangeServer}
            If target = Destination Then Continue For
            Dim caption As String
            Select Case target
                Case SettingsDestination.ExchangeServer : caption = Localization.T("Настройки сервера обмена")
                Case Else : caption = Localization.T("Настройки менеджера")
            End Select
            Dim route = target
            Dim link As New LinkLabel With {.Text = caption, .AutoSize = True,
                .MaximumSize = New Drawing.Size(230, 0), .Margin = New Padding(4, 6, 4, 6)}
            Dim refresh As Action = Sub() link.Visible = SettingsRoutes.Available(route, ExchangeProcess.IsAvailable())
            refresh()
            AddHandler Activated, Sub() refresh()
            AddHandler link.LinkClicked, Sub() RaiseEvent NavigateRequested(route)
            links.Controls.Add(link)
        Next
        _root.Controls.Add(links, 0, 1)
        _root.RowCount = 2
        _root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ' The opt-in belongs to the SFTP component, so it is offered only where that component's pages are.
        If _secNetwork IsNot Nothing AndAlso Not ServerFeatures.IsEnabled() Then
            Dim enable As New Button With {.Text = ShareText.ServerEnableTitle(), .AutoSize = True}
            AddHandler enable.Click, Sub()
                Using dialog As New Share_Enable_Form()
                    If dialog.ShowDialog(Me) = DialogResult.OK Then
                        enable.Visible = False
                        NotifySettingsChanged()
                    End If
                End Using
            End Sub
            links.Controls.Add(enable)
        End If
    End Sub

    Private Function CanCloseDraft(e As FormClosingEventArgs) As Boolean
        If _applyingPort OrElse _exBusy OrElse _applyingExchangeDrafts Then
            e.Cancel = True
            Return False
        End If
        If ExchangeDraftPending() Then
            Dim exchangeAnswer = AskDraftClose(True)
            If exchangeAnswer = DialogResult.No Then Return True
            e.Cancel = True
            If exchangeAnswer = DialogResult.Yes Then ApplyExchangeDraftsAndClose()
            Return False
        End If
        If Not PortPending() Then Return True
        Dim answer = AskDraftClose(False)
        If answer = DialogResult.No Then Return True
        e.Cancel = True
        If answer = DialogResult.Yes Then
            _closeAfterPort = True
            OnApplyPort(Me, EventArgs.Empty)
        End If
        Return False
    End Function
End Class
