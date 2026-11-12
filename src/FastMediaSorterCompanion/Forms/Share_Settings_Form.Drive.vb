Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The "Google Drive" page of Manager settings (specification 203 section 5.9, ticket T-UI-1), right after "Exchange account":
''' the state of the Google Drive channel, "Sign in" (PKCE loopback in the system browser, scope drive.appdata only), "Cancel" while
''' the browser is awaited, and "Sign out" (one plain question, then the records of this PC are deleted, the token is revoked and
''' the file is removed).
'''
''' The page needs no local exchange server and no account on one. All state lives in <see cref="DriveChannelHost"/>; this
''' partial only reads it and draws it.
'''
''' Rules held here:
'''   * Gated like the other client pages: built only while <see cref="ExchangeFeatures.IsEnabled"/> says so (the dormant rule).
'''   * Nothing runs at construction or on opening: the host is only READ (creating it constructs objects, no request, no file);
'''     the network is touched by "Sign in", "Sign out", "Delete packet" and the host's own start with a stored sign-in - never by
'''     this page. The queue group open raises a poller that already runs to its 10 s rhythm (item CQ); it starts none.
'''   * A build with no OAuth client shows a calm "not configured" state and a disabled "Sign in"; nothing else changes.
'''   * The host's events arrive on its own threads: the window is only touched from its own.
'''   * Words come from <see cref="DriveChannelText"/> (a state, a failure kind, an outcome): never an exception, a status code,
'''     the client id or a token.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _secDrive As CollapsibleSection
    Private _drvState As Label
    Private _drvDetail As Label
    Private _drvButtons As FlowLayoutPanel
    Private _btnDrvSignIn As Button
    Private _btnDrvCancel As Button
    Private _btnDrvSignOut As Button
    Private _drvResult As Label
    Private _drvScope As Label
    Private _drvPrivacy As Label
    Private _drvDevice As Label
    Private _drvBoldFont As Font
    Private _drvHost As DriveChannelHost
    Private _drvQueueNote As Label
    Private _drvQueueState As Label
    Private _lvDrvQueue As ListView
    Private _drvQueueButtons As FlowLayoutPanel
    Private _btnDrvQueueDelete As Button
    Private _drvQueueResult As Label
    Private _drvQueueShown As String = ""
    Private _drvQueueDeleting As Boolean
    Private ReadOnly _drvQueueCts As New Threading.CancellationTokenSource()

    Private Const DriveContentWidth As Integer = 500
    Private Const DriveQueueMaxRows As Integer = 8

    ''' <summary>Nothing when the gate is shut: no page, no change to the window.</summary>
    Private Function CreateDriveSection() As CollapsibleSection
        If Not ExchangeFeatures.IsEnabled() Then Return Nothing
        Dim sec As CollapsibleSection = CreateSettingsSection("drive", DriveChannelText.PageTitle())
        _secDrive = sec
        BuildDriveSection(sec)
        Return sec
    End Function

    Private Shared Function DrvNote(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(DriveContentWidth, 0), .Margin = New Padding(0, 2, 0, 4),
            .ForeColor = ThemeService.Palette.TextMuted, .Text = text}
    End Function

    Private Shared Function DrvFlow() As FlowLayoutPanel
        Return New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True,
            .Margin = New Padding(0, 2, 0, 2), .MaximumSize = New Size(DriveContentWidth, 0)}
    End Function

    Private Shared Function DrvButton(text As String, onClick As Action, glyph As String, Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 2, 8, 2), .Text = text}
        AddHandler b.Click, Sub() onClick()
        ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildDriveSection(sec As CollapsibleSection)
        _drvBoldFont = New Font(Me.Font, FontStyle.Bold)
        _drvState = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2), .MaximumSize = New Size(DriveContentWidth, 0), .Font = _drvBoldFont}
        _drvDetail = DrvNote("")
        sec.AddBodyRow(_drvState)
        sec.AddBodyRow(_drvDetail)

        _btnDrvSignIn = DrvButton(DriveChannelText.SignInButton(), AddressOf OnDriveSignIn, "action.confirm")
        _btnDrvCancel = DrvButton(DriveChannelText.CancelButton(), AddressOf OnDriveCancel, "action.cancel")
        _btnDrvSignOut = DrvButton(DriveChannelText.SignOutButton(), AddressOf OnDriveSignOut, "system.network-off")
        _drvButtons = DrvFlow()
        _drvButtons.Controls.Add(_btnDrvSignIn)
        _drvButtons.Controls.Add(_btnDrvCancel)
        _drvButtons.Controls.Add(_btnDrvSignOut)
        sec.AddBodyRow(_drvButtons)

        _drvResult = DrvNote("")
        _drvResult.ForeColor = CollapsibleSection.AttentionColor
        _drvResult.Visible = False
        sec.AddBodyRow(_drvResult)

        ' the second group: what the channel holds (item CS)
        _drvQueueNote = DrvNote(DriveChannelText.QueueNote())
        _drvQueueState = DrvNote("")
        _lvDrvQueue = New ListView With {.View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False,
            .HeaderStyle = ColumnHeaderStyle.Nonclickable, .Width = DriveContentWidth, .Height = 100, .Margin = New Padding(0, 2, 0, 4), .Visible = False}
        _lvDrvQueue.Columns.Add(DriveChannelText.QueueColumnState())
        _lvDrvQueue.Columns.Add(DriveChannelText.QueueColumnKind())
        _lvDrvQueue.Columns.Add(DriveChannelText.QueueColumnDevice())
        _lvDrvQueue.Columns.Add(DriveChannelText.QueueColumnSize())
        _lvDrvQueue.Columns.Add(DriveChannelText.QueueColumnCreated())
        AddHandler _lvDrvQueue.SelectedIndexChanged, Sub() ApplyDriveQueueCommand()
        ' The event comes before the list has its native columns and rows: the fit waits for the next message.
        AddHandler _lvDrvQueue.HandleCreated, Sub() _lvDrvQueue.BeginInvoke(New Action(AddressOf FitDriveQueueList))
        _btnDrvQueueDelete = DrvButton(DriveChannelText.QueueDeleteButton(), AddressOf OnDriveQueueDelete, "action.remove", ButtonGlyphRole.Irreversible)
        _btnDrvQueueDelete.Enabled = False
        _drvQueueButtons = DrvFlow()
        _drvQueueButtons.Controls.Add(_btnDrvQueueDelete)
        _drvQueueButtons.Visible = False
        _drvQueueResult = DrvNote("")
        _drvQueueResult.ForeColor = CollapsibleSection.AttentionColor
        _drvQueueResult.Visible = False
        sec.AddBodyRow(_drvQueueNote)
        sec.AddBodyRow(_drvQueueState)
        sec.AddBodyRow(_lvDrvQueue)
        sec.AddBodyRow(_drvQueueButtons)
        sec.AddBodyRow(_drvQueueResult)

        ' the third group: what the app can see, and this PC's name and id in the channel
        _drvScope = DrvNote(DriveChannelText.ScopeNote())
        _drvPrivacy = DrvNote(DriveChannelText.PrivacyNote())
        _drvDevice = DrvNote("")
        _drvDevice.Visible = False
        sec.AddBodyRow(_drvScope)
        sec.AddBodyRow(_drvPrivacy)
        sec.AddBodyRow(_drvDevice)

        ApplyDriveState()
    End Sub

    ''' <summary>True for the rows of the page's queue group (<see cref="BuildContractGroups"/>).</summary>
    Private Function IsDriveQueueRow(row As Control) As Boolean
        Return Object.ReferenceEquals(row, _drvQueueNote) OrElse Object.ReferenceEquals(row, _drvQueueState) OrElse Object.ReferenceEquals(row, _lvDrvQueue) OrElse
            Object.ReferenceEquals(row, _drvQueueButtons) OrElse Object.ReferenceEquals(row, _drvQueueResult)
    End Function

    ''' <summary>True for the rows of the page's last group (<see cref="BuildContractGroups"/>).</summary>
    Private Function IsDriveDeviceRow(row As Control) As Boolean
        Return Object.ReferenceEquals(row, _drvScope) OrElse Object.ReferenceEquals(row, _drvPrivacy) OrElse Object.ReferenceEquals(row, _drvDevice)
    End Function

    ''' <summary>Called when the window closes: the subscription and the font. A sign-in in progress is the host's, not the
    ''' window's - closing settings does not abandon a consent that is open in the browser. A delete in progress ends with the window.</summary>
    Private Sub DisposeDriveResources()
        If _drvHost IsNot Nothing Then _drvHost.SetActive(False)
        _drvQueueCts.Cancel()
        UnsubscribeDriveHost()
        Try : _drvBoldFont?.Dispose() : Catch : End Try
    End Sub

    ' --- reading the host ---

    Private Sub SubscribeDriveHost(host As DriveChannelHost)
        If Object.ReferenceEquals(host, _drvHost) Then Return
        UnsubscribeDriveHost()
        _drvHost = host
        If host IsNot Nothing Then AddHandler host.Changed, AddressOf OnDriveHostChanged
    End Sub

    Private Sub UnsubscribeDriveHost()
        If _drvHost IsNot Nothing Then RemoveHandler _drvHost.Changed, AddressOf OnDriveHostChanged
        _drvHost = Nothing
    End Sub

    ''' <summary>The host raises on its own threads (the poller, the sign-in's continuation): the window is only touched from its own.</summary>
    Private Sub OnDriveHostChanged(sender As Object, e As EventArgs)
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(AddressOf ApplyDriveState))
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
            ' the window is closing
        End Try
    End Sub

    ''' <summary>Reads the host (never starts a request) and redraws the page from its state.</summary>
    Private Sub ApplyDriveState()
        If _secDrive Is Nothing OrElse IsDisposed Then Return
        Dim host As DriveChannelHost = DriveChannelHosts.Current()
        SubscribeDriveHost(host)
        Dim state As DriveChannelState = host.State
        Dim failure As ExchangeDriveFailure = host.LastFailure

        _drvState.Text = DriveChannelText.StateLine(state)
        Dim detail As String = DriveChannelText.StateDetail(state, failure)
        _drvDetail.Text = detail
        _drvDetail.Visible = detail.Length > 0

        Dim waiting As Boolean = state = DriveChannelState.SigningIn
        Dim signedIn As Boolean = state = DriveChannelState.SignedIn OrElse state = DriveChannelState.Unavailable
        Dim canSignIn As Boolean = state = DriveChannelState.NotConfigured OrElse state = DriveChannelState.SignedOut
        _btnDrvSignIn.Visible = canSignIn
        _btnDrvSignIn.Enabled = host.IsConfigured
        _btnDrvCancel.Visible = waiting
        _btnDrvSignOut.Visible = signedIn
        _drvButtons.Visible = canSignIn OrElse waiting OrElse signedIn

        Dim result As String = DriveResultText(host, state)
        _drvResult.Text = result
        _drvResult.Visible = result.Length > 0

        _drvScope.Text = DriveChannelText.ScopeNote()
        _drvPrivacy.Text = DriveChannelText.PrivacyNote()
        Dim device As String = If(signedIn, DriveChannelText.DeviceLine(host.DeviceName, host.DeviceId), "")
        _drvDevice.Text = device
        _drvDevice.Visible = device.Length > 0

        _secDrive.Summary = DriveChannelText.StateLine(state)
        _secDrive.SummaryColor = If(state = DriveChannelState.Unavailable OrElse (state = DriveChannelState.SignedOut AndAlso failure = ExchangeDriveFailure.SignedOut),
                                    CollapsibleSection.AttentionColor, ThemeService.Palette.TextMuted)
        ApplyDriveQueue(host, state, failure)
        ApplyDriveQueueActivity()
        ' The clipboard block follows the sign-in (item CP): its words and its rows change with it.
        ApplyClipboardRows()
        If _built Then _root.PerformLayout()
    End Sub

    ' --- the queue screen (item CS) ---

    ''' <summary>The queue group open is what item CQ calls the queue screen open. Called from <see cref="LoadSelectedSettingsPage"/>
    ''' for every page and group, and on each redraw: a sign-in made while it is open brings a service that starts at 60 s.</summary>
    Private Sub ApplyDriveQueueActivity()
        If _secDrive Is Nothing OrElse Not DriveChannelHosts.IsCreated() Then Return
        DriveChannelHosts.Current().SetActive(_selectedPage = "drive" AndAlso SelectedItemId("drive") = "queue")
    End Sub

    ''' <summary>Draws what the host's last round found. The list is rebuilt only when its rows changed, so a selection survives the rounds.</summary>
    Private Sub ApplyDriveQueue(host As DriveChannelHost, state As DriveChannelState, failure As ExchangeDriveFailure)
        Dim view As DriveQueueView = host.QueueView
        Dim signedIn As Boolean = state = DriveChannelState.SignedIn OrElse state = DriveChannelState.Unavailable
        Dim rows As IReadOnlyList(Of DriveQueueRow) = If(view Is Nothing OrElse Not signedIn, Array.Empty(Of DriveQueueRow)(), view.Rows)
        Dim line As String = DriveChannelText.QueueState(state, failure, view)
        Dim extras As String = If(signedIn, DriveChannelText.QueueExtras(view), "")
        Dim stateText As String = If(line.Length > 0 AndAlso extras.Length > 0, line & " " & extras, line & extras)
        _drvQueueState.Text = stateText
        _drvQueueState.Visible = stateText.Length > 0

        Dim shown As String = String.Join("|", rows.Select(Function(r) r.Key))
        If Not String.Equals(shown, _drvQueueShown, StringComparison.Ordinal) Then
            _drvQueueShown = shown
            RefillDriveQueueList(rows)
        End If
        _lvDrvQueue.Visible = rows.Count > 0
        _drvQueueButtons.Visible = rows.Count > 0
        If Not signedIn Then _drvQueueResult.Visible = False
        ApplyDriveQueueCommand()
    End Sub

    Private Sub RefillDriveQueueList(rows As IReadOnlyList(Of DriveQueueRow))
        Dim keep As String = If(_lvDrvQueue.SelectedItems.Count > 0, DirectCast(_lvDrvQueue.SelectedItems(0).Tag, DriveQueueRow).Packet.FolderId, "")
        _lvDrvQueue.BeginUpdate()
        Try
            _lvDrvQueue.Items.Clear()
            For Each row As DriveQueueRow In rows
                Dim packet As ExchangeDrivePacket = row.Packet
                Dim created As String = If(packet.HasManifestCreatedAt, packet.ManifestCreatedAt.ToLocalTime().ToString("g", Globalization.CultureInfo.CurrentCulture), "-")
                Dim item As New ListViewItem(DriveChannelText.QueueRoleWord(row.Role)) With {.Tag = row}
                item.SubItems.Add(DriveChannelText.QueueKindWord(packet.Manifest.PayloadKind))
                item.SubItems.Add(DriveChannelText.QueuePeer(row.Role, packet.Manifest))
                item.SubItems.Add(FormatBytes(packet.DriveBytes))
                item.SubItems.Add(created)
                _lvDrvQueue.Items.Add(item)
                If String.Equals(packet.FolderId, keep, StringComparison.Ordinal) Then item.Selected = True
            Next
        Finally
            _lvDrvQueue.EndUpdate()
        End Try
        FitDriveQueueList()
    End Sub

    ''' <summary>The list is as tall as its rows, up to <see cref="DriveQueueMaxRows"/>, and its columns as wide as their words.</summary>
    Private Sub FitDriveQueueList()
        If _lvDrvQueue.IsHandleCreated Then _lvDrvQueue.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize)
        Dim rowHeight As Integer = Math.Max(_lvDrvQueue.Font.Height + LogicalToDeviceUnits(6), LogicalToDeviceUnits(20))
        If _lvDrvQueue.IsHandleCreated AndAlso _lvDrvQueue.Items.Count > 0 Then rowHeight = _lvDrvQueue.GetItemRect(0).Height
        Dim headerHeight As Integer = _lvDrvQueue.Font.Height + LogicalToDeviceUnits(12)
        Dim wide As Boolean = _lvDrvQueue.Columns.Cast(Of ColumnHeader)().Sum(Function(c) c.Width) > _lvDrvQueue.ClientSize.Width
        _lvDrvQueue.Height = headerHeight + Math.Min(DriveQueueMaxRows, _lvDrvQueue.Items.Count) * rowHeight +
            If(wide, SystemInformation.HorizontalScrollBarHeight, 0) + SystemInformation.Border3DSize.Height * 2
    End Sub

    Private Sub ApplyDriveQueueCommand()
        _btnDrvQueueDelete.Enabled = Not _drvQueueDeleting AndAlso _lvDrvQueue.SelectedItems.Count > 0
    End Sub

    Private Async Sub OnDriveQueueDelete()
        If _drvQueueDeleting OrElse _lvDrvQueue.SelectedItems.Count = 0 Then Return
        Dim row As DriveQueueRow = DirectCast(_lvDrvQueue.SelectedItems(0).Tag, DriveQueueRow)
        ' one plain question naming the consequence; the safe answer (No) is the default
        If ManagerBox.Show(Me, DriveChannelText.QueueDeleteQuestion(), DriveChannelText.QueueGroupTitle(),
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        _drvQueueDeleting = True
        ApplyDriveQueueCommand()
        Dim failure As ExchangeDriveFailure
        Try
            failure = Await DriveChannelHosts.Current().DeleteQueuedPacketAsync(row.Packet, _drvQueueCts.Token)
        Catch ex As OperationCanceledException
            ' the window closed under the delete: nobody is left to tell
            Return
        End Try
        If IsDisposed Then Return
        _drvQueueDeleting = False
        Dim result As String = DriveChannelText.QueueDeleteFailed(failure)
        _drvQueueResult.Text = result
        _drvQueueResult.Visible = result.Length > 0
        ApplyDriveState()
    End Sub

    ''' <summary>The line under the buttons: how the last sign-out or sign-in ended, while nothing is running.</summary>
    Private Shared Function DriveResultText(host As DriveChannelHost, state As DriveChannelState) As String
        If state = DriveChannelState.SigningIn OrElse state = DriveChannelState.SigningOut Then Return ""
        Dim signOut As ExchangeDriveSignOutResult = host.LastSignOut
        If signOut IsNot Nothing Then Return DriveChannelText.SignOutOutcome(signOut)
        Dim outcome As ExchangeDriveSignInOutcome? = host.LastOutcome
        If outcome.HasValue AndAlso state <> DriveChannelState.NotConfigured Then
            Return DriveChannelText.SignInOutcome(outcome.Value, host.LastOutcomeWasCancelled)
        End If
        Return ""
    End Function

    ' --- actions ---

    Private Async Sub OnDriveSignIn()
        Dim host As DriveChannelHost = DriveChannelHosts.Current()
        If Not host.IsConfigured Then Return
        Await host.SignInAsync()
        ' a sign-in that completed lets the agent serve the Drive listing even with no exchange account (ticket 269 P2.5)
        ExchangeDeviceHost.DriveSignInChanged()
        If Not IsDisposed Then ApplyDriveState()
    End Sub

    Private Sub OnDriveCancel()
        DriveChannelHosts.Current().CancelSignIn()
    End Sub

    Private Async Sub OnDriveSignOut()
        Dim host As DriveChannelHost = DriveChannelHosts.Current()
        ' one plain question naming the consequence; the safe answer (No) is the default
        If ManagerBox.Show(Me, DriveChannelText.SignOutQuestion(), DriveChannelText.PageTitle(),
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Await host.SignOutAsync()
        ' with no account the agent has nothing left to serve: its Drive-only channel closes (ticket 269 P2.5)
        ExchangeDeviceHost.DriveSignInChanged()
        If Not IsDisposed Then ApplyDriveState()
    End Sub

End Class
