Option Strict On

Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The producer rows of the device exchange (ticket 170 T-2, T-4, T-6), in the "Сеть" group: "Publish my shares to my exchange account",
''' the third-party port ("Let other SFTP programs.."), one line per share with its state, and the outsider-link actions. This REPLACES the
''' rows of ticket 148 (the worker no longer registers anything - section 5 of 170); the server's own controls are the separate group of
''' ticket 142/168 (Share_Settings_Form.Exchange.vb) and the account is the group of ticket 180 (Share_Settings_Form.ExchangeAccount.vb).
'''
''' Rules held here:
'''   * Gated: the rows are built only while <see cref="ExchangeFeatures.IsEnabled"/> says so (the build's gate, or the developer switch);
'''     otherwise there is no row at all and the window is what it was (the dormant rule, 177).
'''   * No account, no row: the group is HIDDEN until an account is configured - by <see cref="ExchangePublishModel"/>, never disabled.
'''   * Nothing runs at construction: the producer is only READ here (ExchangeDeviceHost.Current); this window never creates an agent.
'''   * Publication and the third-party port are the user's choices, off by default; a choice takes effect through the producer
'''     (<see cref="ExchangePublisher.ApplyNowAsync"/>) and is written by it (registry, "1" or absent).
'''   * A link action that destroys something (a new link, removing it) asks first, with the safe answer as the default.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    ''' <summary>A choice here changed what the main window may show (a share published or unpublished).</summary>
    Private _pxPushed As Boolean

    Private _pxGroup As FlowLayoutPanel
    Private _chkPxPublish As CheckBox
    Private _chkPxThird As CheckBox
    Private _lblPxPublishNote As Label
    Private _lblPxState As Label
    Private _pxThirdBlock As FlowLayoutPanel
    Private _lblPxThirdNote As Label
    Private _pxEndpointRow As FlowLayoutPanel
    Private _lblPxEndpoint As Label
    Private _btnPxCopy As Button
    Private _pxSharesBlock As FlowLayoutPanel
    Private _lvPxShares As ListView
    Private _pxLinkRow As FlowLayoutPanel
    Private _btnPxCreate As Button
    Private _btnPxRotate As Button
    Private _btnPxRemoveLink As Button
    Private _lblPxLinkNote As Label
    Private _lblPxResult As Label
    Private _pxTimer As System.Windows.Forms.Timer
    Private _pxPublisher As ExchangePublisher
    Private _pxAgent As ExchangeDeviceAgent
    Private _pxLoading As Boolean
    Private _pxBusy As Boolean
    Private _pxBoldFont As Font

    Private Const ProducerContentWidth As Integer = 500

    Private Shared Function PxNote(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(ProducerContentWidth, 0), .Margin = New Padding(0, 2, 0, 4),
            .ForeColor = ThemeService.Palette.TextMuted, .Text = text}
    End Function

    Private Shared Function PxFlow() As FlowLayoutPanel
        Return New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True,
            .Margin = New Padding(0, 2, 0, 2), .MaximumSize = New Size(ProducerContentWidth, 0)}
    End Function

    Private Shared Function PxButton(text As String, onClick As Action, Optional glyph As String = "") As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 2, 8, 2), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph)
        Return b
    End Function

    Private Function PxCheck(text As String, onChanged As EventHandler) As CheckBox
        Dim c As New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Text = text}
        AddHandler c.CheckedChanged, onChanged
        Return c
    End Function

    ''' <summary>Builds the rows into the network group. Nothing at all when the gate is shut.</summary>
    Private Sub BuildExchangeProducerRows(sec As CollapsibleSection)
        If Not ExchangeFeatures.IsEnabled() Then Return
        _pxBoldFont = New Font(Me.Font, FontStyle.Bold)
        _pxGroup = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0, 14, 0, 2), .Visible = False}
        _pxGroup.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .Font = _pxBoldFont, .Text = ExchangePublishText.GroupTitle()})

        _chkPxPublish = PxCheck(ExchangePublishText.PublishLabel(), AddressOf OnPxPublishChanged)
        _chkPxPublish.MaximumSize = New Size(ProducerContentWidth, 0)
        _pxGroup.Controls.Add(_chkPxPublish)
        _lblPxPublishNote = PxNote(ExchangePublishText.PublishNote())
        CheckBoxLayout.BindHint(_chkPxPublish, _lblPxPublishNote)
        _pxGroup.Controls.Add(_lblPxPublishNote)
        _lblPxState = PxNote("")
        _lblPxState.ForeColor = SystemColors.ControlText
        _pxGroup.Controls.Add(_lblPxState)

        ' the third-party port: offered only while publication is on, off by default
        _pxThirdBlock = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0)}
        _chkPxThird = PxCheck(ExchangePublishText.ThirdPartyLabel(), AddressOf OnPxThirdChanged)
        _chkPxThird.MaximumSize = New Size(ProducerContentWidth, 0)
        _pxThirdBlock.Controls.Add(_chkPxThird)
        _lblPxThirdNote = PxNote(ExchangePublishText.ThirdPartyNote())
        CheckBoxLayout.BindHint(_chkPxThird, _lblPxThirdNote)
        _pxThirdBlock.Controls.Add(_lblPxThirdNote)
        _pxEndpointRow = PxFlow()
        _lblPxEndpoint = New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 8, 0)}
        _btnPxCopy = PxButton(ExchangePublishText.CopyEndpointButton(), AddressOf OnPxCopyEndpoint, "action.copy")
        _pxEndpointRow.Controls.Add(_lblPxEndpoint)
        _pxEndpointRow.Controls.Add(_btnPxCopy)
        _pxThirdBlock.Controls.Add(_pxEndpointRow)
        _pxGroup.Controls.Add(_pxThirdBlock)

        ' one line per share, and the outsider-link actions
        _pxSharesBlock = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0, 6, 0, 0)}
        ' The control's own size is in 96-dpi design units (DpiLayout.ApplyAutoScale scales it); the COLUMN widths are not scaled by that, so
        ' they are device units (LogicalToDeviceUnits) - the Companion DPI recipe.
        _lvPxShares = New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .Width = ProducerContentWidth, .Height = 110, .Margin = New Padding(0, 2, 0, 2),
            .AccessibleName = ExchangePublishText.GroupTitle()}
        _lvPxShares.Columns.Add(ExchangePublishText.ColumnFolder(), Me.LogicalToDeviceUnits(170))
        _lvPxShares.Columns.Add(Localization.T("Состояние"), Me.LogicalToDeviceUnits(200))
        _lvPxShares.Columns.Add(ExchangePublishText.ColumnLink(), Me.LogicalToDeviceUnits(90))
        AddHandler _lvPxShares.SelectedIndexChanged, Sub() UpdateProducerLinkButtons()
        _pxSharesBlock.Controls.Add(_lvPxShares)

        _lblPxLinkNote = PxNote(ExchangePublishText.LinkNote())
        _pxSharesBlock.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 0), .Font = _pxBoldFont, .Text = ExchangePublishText.LinkTitle()})
        _pxSharesBlock.Controls.Add(_lblPxLinkNote)
        _pxLinkRow = PxFlow()
        _btnPxCreate = PxButton(ExchangePublishText.CreateLinkButton(), AddressOf OnPxCreateLink, "action.add")
        _btnPxRotate = PxButton(ExchangePublishText.NewLinkButton(), AddressOf OnPxRotateLink, "action.refresh")
        _btnPxRemoveLink = PxButton(ExchangePublishText.RemoveLinkButton(), AddressOf OnPxRemoveLink, "action.delete")
        _pxLinkRow.Controls.Add(_btnPxCreate)
        _pxLinkRow.Controls.Add(_btnPxRotate)
        _pxLinkRow.Controls.Add(_btnPxRemoveLink)
        _pxSharesBlock.Controls.Add(_pxLinkRow)
        _pxGroup.Controls.Add(_pxSharesBlock)

        _lblPxResult = PxNote("")
        _lblPxResult.Visible = False
        _pxGroup.Controls.Add(_lblPxResult)
        sec.AddBodyRow(_pxGroup)

        _pxTimer = New System.Windows.Forms.Timer With {.Interval = 1500}
        AddHandler _pxTimer.Tick, Sub() ApplyProducerState()
        AddHandler Me.Shown, Sub()
                                 ApplyProducerState()
                                 _pxTimer.Start()
                             End Sub
    End Sub

    ''' <summary>Called when the window closes: the timer, the subscription and the font.</summary>
    Private Sub DisposeProducerResources()
        Try
            If _pxTimer IsNot Nothing Then
                _pxTimer.Stop()
                _pxTimer.Dispose()
            End If
        Catch
        End Try
        UnsubscribeProducer()
        Try : _pxBoldFont?.Dispose() : Catch : End Try
    End Sub

    ' --- reading the producer ---

    Private Sub SubscribeProducer(agent As ExchangeDeviceAgent)
        Dim publisher As ExchangePublisher = agent?.Publisher
        If Object.ReferenceEquals(publisher, _pxPublisher) AndAlso Object.ReferenceEquals(agent, _pxAgent) Then Return
        UnsubscribeProducer()
        _pxAgent = agent
        _pxPublisher = publisher
        If publisher IsNot Nothing Then AddHandler publisher.Changed, AddressOf OnProducerChanged
        If agent IsNot Nothing Then AddHandler agent.StatusChanged, AddressOf OnProducerChanged
    End Sub

    Private Sub UnsubscribeProducer()
        If _pxPublisher IsNot Nothing Then RemoveHandler _pxPublisher.Changed, AddressOf OnProducerChanged
        If _pxAgent IsNot Nothing Then RemoveHandler _pxAgent.StatusChanged, AddressOf OnProducerChanged
        _pxPublisher = Nothing
        _pxAgent = Nothing
    End Sub

    ''' <summary>The producer's events arrive on its own threads: the window is only touched from its own.</summary>
    Private Sub OnProducerChanged(sender As Object, e As EventArgs)
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(AddressOf ApplyProducerState))
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
            ' the window is closing
        End Try
    End Sub

    ''' <summary>Reads the agent and its producer (never creates either) and redraws from the model.</summary>
    Private Sub ApplyProducerState()
        If _pxGroup Is Nothing OrElse IsDisposed Then Return
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        SubscribeProducer(agent)
        Dim status As ExchangeDeviceStatus = If(agent Is Nothing, Nothing, agent.GetStatus())
        Dim publisher As ExchangePublisher = _pxPublisher
        Dim configured As Boolean = status IsNot Nothing AndAlso status.Configured AndAlso publisher IsNot Nothing
        Dim online As Boolean = configured AndAlso status.State = ExchangeAgentState.Online
        Dim publish As Boolean = configured AndAlso publisher.Settings.PublishShares()
        Dim third As Boolean = configured AndAlso publisher.Settings.ThirdPartyPort()
        Dim snapshot As ExchangePublishSnapshot = If(publisher Is Nothing, ExchangePublishSnapshot.Empty, publisher.Snapshot)
        Dim model As ExchangePublishModel = ExchangePublishModel.Build(configured, online, publish, third, snapshot)

        _pxLoading = True
        Try
            _pxGroup.Visible = model.ShowGroup
            _chkPxPublish.Checked = model.PublishChecked
            _chkPxThird.Checked = model.ThirdPartyChecked
        Finally
            _pxLoading = False
        End Try
        _lblPxState.Text = model.StateLine
        _lblPxState.Visible = model.StateLine.Length > 0
        _pxThirdBlock.Visible = model.ShowThirdParty
        _pxEndpointRow.Visible = model.ShowEndpoint
        _lblPxEndpoint.Text = model.EndpointLine
        _btnPxCopy.Visible = model.ShowEndpoint AndAlso snapshot.ThirdPartyEndpoint.Length > 0
        _pxSharesBlock.Visible = model.ShowShares
        If model.ShowShares Then FillProducerShares(snapshot)
        UpdateProducerLinkButtons()
        If _built Then _root.PerformLayout()
    End Sub

    Private Sub FillProducerShares(snapshot As ExchangePublishSnapshot)
        Dim selected As String = SelectedProducerShare()
        _lvPxShares.BeginUpdate()
        Try
            _lvPxShares.Items.Clear()
            For Each row As ExchangePublishRow In snapshot.Rows
                Dim item As New ListViewItem(row.Name) With {.Tag = row.Name}
                item.SubItems.Add(ExchangePublishText.RowState(row))
                item.SubItems.Add(If(row.HasLink, ExchangePublishText.LinkCreatedCell(), ""))
                _lvPxShares.Items.Add(item)
                If String.Equals(row.Name, selected, StringComparison.Ordinal) Then item.Selected = True
            Next
        Finally
            _lvPxShares.EndUpdate()
        End Try
    End Sub

    Private Function SelectedProducerShare() As String
        If _lvPxShares Is Nothing OrElse _lvPxShares.SelectedItems.Count = 0 Then Return ""
        Return If(TryCast(_lvPxShares.SelectedItems(0).Tag, String), "")
    End Function

    ''' <summary>Which link buttons exist for the selected share (hidden, not disabled).</summary>
    Private Sub UpdateProducerLinkButtons()
        If _btnPxCreate Is Nothing Then Return
        Dim row As ExchangePublishRow = If(_pxPublisher Is Nothing, Nothing, _pxPublisher.Snapshot.RowFor(SelectedProducerShare()))
        Dim create As Boolean, rotate As Boolean, remove As Boolean
        ExchangePublishModel.LinkButtons(row, create, rotate, remove)
        _btnPxCreate.Visible = create
        _btnPxRotate.Visible = rotate
        _btnPxRemoveLink.Visible = remove
        _pxLinkRow.Visible = create OrElse rotate OrElse remove
        _lblPxLinkNote.Visible = _pxSharesBlock.Visible
    End Sub

    Private Sub ShowProducerResult(text As String, ok As Boolean)
        If _lblPxResult Is Nothing Then Return
        _lblPxResult.ForeColor = If(ok, ThemeService.Palette.TextMuted, CollapsibleSection.AttentionColor)
        _lblPxResult.Text = text
        _lblPxResult.Visible = text.Length > 0
    End Sub

    ' --- the two choices ---

    Private Async Sub OnPxPublishChanged(sender As Object, e As EventArgs)
        If _pxLoading OrElse _pxPublisher Is Nothing Then Return
        _pxPublisher.Settings.SetPublishShares(_chkPxPublish.Checked)
        _pxPushed = True
        ShowProducerResult("", True)
        Await ApplyProducerChoiceAsync()
    End Sub

    Private Async Sub OnPxThirdChanged(sender As Object, e As EventArgs)
        If _pxLoading OrElse _pxPublisher Is Nothing Then Return
        _pxPublisher.Settings.SetThirdPartyPort(_chkPxThird.Checked)
        _pxPushed = True
        ShowProducerResult("", True)
        Await ApplyProducerChoiceAsync()
    End Sub

    ''' <summary>The choice is already saved; the producer acts on it now (publish, or unpublish), off the UI thread's wait.</summary>
    Private Async Function ApplyProducerChoiceAsync() As Task
        Dim publisher As ExchangePublisher = _pxPublisher
        If publisher Is Nothing Then Return
        _pxBusy = True
        Try
            Await publisher.ApplyNowAsync(CancellationToken.None)
        Finally
            _pxBusy = False
        End Try
        If Not IsDisposed Then ApplyProducerState()
    End Function

    Private Sub OnPxCopyEndpoint()
        Dim publisher As ExchangePublisher = _pxPublisher
        If publisher Is Nothing Then Return
        Dim endpoint As String = publisher.Snapshot.ThirdPartyEndpoint
        If endpoint.Length = 0 Then Return
        ' Not a secret (a host and a port): a plain copy, through the one clipboard door.
        Dim data As New DataObject()
        data.SetText(endpoint, TextDataFormat.UnicodeText)
        If SecureClipboard.TrySetDataObject(data) Then ShowProducerResult(ExchangePublishText.EndpointCopied(), True)
    End Sub

    ' --- the outsider links ---

    Private Async Sub OnPxCreateLink()
        Dim name As String = SelectedProducerShare()
        If name.Length = 0 OrElse _pxBusy OrElse _pxPublisher Is Nothing Then Return
        Await RunLinkActionAsync(name, ExchangePublishText.LinkCreated(name), Function(p, n) p.CreateLinkAsync(n, CancellationToken.None), showWindow:=True)
    End Sub

    Private Async Sub OnPxRotateLink()
        Dim name As String = SelectedProducerShare()
        If name.Length = 0 OrElse _pxBusy OrElse _pxPublisher Is Nothing Then Return
        ' names the consequence; the safe answer (No) is the default
        If MessageBox.Show(Me, ExchangePublishText.RotateQuestion(name), ExchangePublishText.GroupTitle(), MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await RunLinkActionAsync(name, ExchangePublishText.LinkRotated(name), Function(p, n) p.RotateLinkAsync(n, CancellationToken.None), showWindow:=True)
    End Sub

    Private Async Sub OnPxRemoveLink()
        Dim name As String = SelectedProducerShare()
        If name.Length = 0 OrElse _pxBusy OrElse _pxPublisher Is Nothing Then Return
        If MessageBox.Show(Me, ExchangePublishText.RemoveQuestion(name), ExchangePublishText.GroupTitle(), MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await RunLinkActionAsync(name, ExchangePublishText.LinkRemoved(name), Function(p, n) p.RemoveLinkAsync(n, CancellationToken.None), showWindow:=False)
    End Sub

    Private Async Function RunLinkActionAsync(name As String, okText As String, action As Func(Of ExchangePublisher, String, Task(Of ExchangeLinkResult)),
                                              showWindow As Boolean) As Task
        Dim publisher As ExchangePublisher = _pxPublisher
        _pxBusy = True
        Dim result As ExchangeLinkResult
        Try
            result = Await action(publisher, name)
        Finally
            _pxBusy = False
        End Try
        If IsDisposed Then Return
        If result.Outcome <> ExchangeLinkOutcome.Done Then
            ShowProducerResult(ExchangePublishText.LinkFailure(result), False)
            ApplyProducerState()
            Return
        End If
        _pxPushed = True
        ShowProducerResult(okText, True)
        ApplyProducerState()
        If showWindow Then Await ShowLinkWindowAsync(name)
    End Function

    ''' <summary>The window that hands the link over: the code (copied through the guarded door), its QR and a .fmscfg of this share.</summary>
    Private Async Function ShowLinkWindowAsync(name As String) As Task
        Dim publisher As ExchangePublisher = _pxPublisher
        If publisher Is Nothing Then Return
        Dim codeText As String = ""
        publisher.TryGetLinkCode(name, codeText)
        Dim configJson As String = ""
        Dim qr As Byte() = Nothing
        Try
            Dim status As WorkerStatus = Await ShareController.GetStatusAsync()
            Dim built As ShareConfigResult = publisher.BuildLinkExport(name, status)
            If built IsNot Nothing Then configJson = built.ConfigJson
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            AppFileLogger.LogException("Exchange producer: building the link file", ex)
        End Try
        qr = ShareConfigBuilder.QrPngForText(codeText)
        If IsDisposed Then Return
        If codeText.Length = 0 AndAlso configJson.Length = 0 Then
            ShowProducerResult(ExchangePublishText.NoQr(), False)
            Return
        End If
        If qr Is Nothing AndAlso codeText.Length > 0 Then ShowProducerResult(ExchangePublishText.QrTooLarge(), False)
        Using dlg As New Exchange_Link_Form(name, codeText, configJson, qr)
            dlg.ShowDialog(Me)
        End Using
        codeText = ""
        configJson = ""
    End Function

End Class
