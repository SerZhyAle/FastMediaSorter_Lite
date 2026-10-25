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
'''     the network is touched by "Sign in", "Sign out" and the host's own start with a stored sign-in - never by this page.
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

    Private Const DriveContentWidth As Integer = 500

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

    Private Shared Function DrvButton(text As String, onClick As Action, glyph As String) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 2, 8, 2), .Text = text}
        AddHandler b.Click, Sub() onClick()
        ButtonGlyphs.Apply(b, glyph)
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

        ' the second group: what the app can see, and this PC's name and id in the channel
        _drvScope = DrvNote(DriveChannelText.ScopeNote())
        _drvPrivacy = DrvNote(DriveChannelText.PrivacyNote())
        _drvDevice = DrvNote("")
        _drvDevice.Visible = False
        sec.AddBodyRow(_drvScope)
        sec.AddBodyRow(_drvPrivacy)
        sec.AddBodyRow(_drvDevice)

        ApplyDriveState()
    End Sub

    ''' <summary>True for the rows of the page's second group (<see cref="BuildContractGroups"/>).</summary>
    Private Function IsDriveDeviceRow(row As Control) As Boolean
        Return Object.ReferenceEquals(row, _drvScope) OrElse Object.ReferenceEquals(row, _drvPrivacy) OrElse Object.ReferenceEquals(row, _drvDevice)
    End Function

    ''' <summary>Called when the window closes: the subscription and the font. A sign-in in progress is the host's, not the
    ''' window's - closing settings does not abandon a consent that is open in the browser.</summary>
    Private Sub DisposeDriveResources()
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
        If _built Then _root.PerformLayout()
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
        If Not IsDisposed Then ApplyDriveState()
    End Sub

End Class
