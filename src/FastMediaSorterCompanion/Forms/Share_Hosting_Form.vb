Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The Hosting console (SPECIFICATION_SHARE_SYSTEM_SERVICE.md §2, §3.6, §4.3).
'''
''' Two jobs, and the second one is why it exists as its own window:
'''   * say which host owns the worker and, separately, what the service and the
'''     SFTP server are each doing right now - "service installed", "service
'''     running", "SFTP serving" and "no folders configured" are four different
'''     states and the main window has no room to keep them apart;
'''   * gather every machine-affecting action in one place, each behind a visible
'''     UAC prompt via the elevated helper. Routine Start/Stop sharing stays on the
'''     main window as plain IPC and never elevates - only service lifecycle does.
'''
''' It never downloads or launches an installer: the edition-change entries open
''' the documented page and stop there (spec §1.4).
''' Built entirely in code (no Designer), like the other Companion dialogs.
''' </summary>
Public NotInheritable Class Share_Hosting_Form
    Inherits Form

    Private ReadOnly _status As WorkerStatus
    Private _iconHandle As IntPtr

    Private _lblHost As Label
    Private _lblIntro As Label
    Private _lblLive As Label
    Private _lblService As Label
    Private _lblServing As Label
    Private _lblStore As Label
    Private _lblResult As Label
    Private _btnSwitchToService As Button
    Private _btnInstallServer As Button
    Private _noteDownload As Label
    Private _btnReturnToUser As Button
    Private _btnStart As Button
    Private _btnStop As Button
    Private _btnRestart As Button
    Private _btnRepair As Button
    Private _btnGrantRoots As Button
    Private _btnRemove As Button
    Private _btnClose As Button

    ''' <summary>True when an elevated action actually changed the machine, so the
    ''' caller re-reads status instead of showing a stale console.</summary>
    Public ReadOnly Property Changed As Boolean

    Public Sub New(status As WorkerStatus)
        _status = status
        BuildUi()
    End Sub

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = HostingText.Title()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' AutoSize + TableLayoutPanel, no absolute coordinates: the notes are long and
        ' the window must grow to fit them at any display scaling (see DpiLayout).
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        Const contentWidth As Integer = 520

        Dim tlp As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 1,
            .Padding = New Padding(16, 16, 16, 12), .GrowStyle = TableLayoutPanelGrowStyle.AddRows}
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        _lblHost = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 6),
            .MaximumSize = New Size(contentWidth, 0),
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.15F, FontStyle.Bold)}
        _lblIntro = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 10),
            .MaximumSize = New Size(contentWidth, 0)}
        ' The four facts, each on its own line and never merged: who is answering the
        ' pipe right now, what the SCM says about the service, whether SFTP is actually
        ' serving, and which state store both hosts are working on.
        _lblLive = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
            .MaximumSize = New Size(contentWidth, 0), .Font = New Font(Me.Font, FontStyle.Bold)}
        _lblService = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
            .MaximumSize = New Size(contentWidth, 0)}
        _lblServing = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
            .MaximumSize = New Size(contentWidth, 0), .ForeColor = SystemColors.GrayText}
        _lblStore = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 12),
            .MaximumSize = New Size(contentWidth, 0), .ForeColor = SystemColors.GrayText}

        tlp.Controls.Add(_lblHost)
        tlp.Controls.Add(_lblIntro)
        tlp.Controls.Add(_lblLive)
        tlp.Controls.Add(_lblService)
        tlp.Controls.Add(_lblServing)
        tlp.Controls.Add(_lblStore)

        _btnSwitchToService = MakeAction(HostingText.SwitchToServiceButton(), AddressOf OnSwitchToService)
        _btnInstallServer = MakeAction(HostingText.InstallServerButton(), AddressOf OnOpenServerPage)
        _btnReturnToUser = MakeAction(HostingText.ReturnToUserButton(), AddressOf OnOpenServerPage)
        _btnStart = MakeAction(HostingText.StartServiceButton(), Sub() RunManage(ServiceControl.ManageAction.StartService))
        _btnStop = MakeAction(HostingText.StopServiceButton(), Sub() RunManage(ServiceControl.ManageAction.StopService))
        _btnRestart = MakeAction(HostingText.RestartServiceButton(), Sub() RunManage(ServiceControl.ManageAction.RestartService))
        _btnRepair = MakeAction(HostingText.RepairServiceButton(), Sub() RunManage(ServiceControl.ManageAction.Repair))
        _btnGrantRoots = MakeAction(HostingText.GrantRootsButton(), AddressOf OnGrantRoots)
        _btnRemove = MakeAction(HostingText.RemoveRoleButton(), Sub() RunManage(ServiceControl.ManageAction.Remove))

        tlp.Controls.Add(_btnSwitchToService)
        tlp.Controls.Add(_btnInstallServer)
        ' Kept as a field: this note explains the DOWNLOAD button, so it must disappear
        ' together with it when this installation can take the role on by itself, and it
        ' says something different in a Store build, which cannot.
        _noteDownload = NoteLabel(HostingText.DownloadNote(), contentWidth)
        tlp.Controls.Add(_noteDownload)
        tlp.Controls.Add(_btnReturnToUser)
        tlp.Controls.Add(_btnStart)
        tlp.Controls.Add(_btnStop)
        tlp.Controls.Add(_btnRestart)
        tlp.Controls.Add(_btnRepair)
        tlp.Controls.Add(_btnGrantRoots)
        tlp.Controls.Add(NoteLabel(HostingText.AccountNote(), contentWidth))
        tlp.Controls.Add(_btnRemove)
        tlp.Controls.Add(NoteLabel(HostingText.RemoveNote(), contentWidth))

        _lblResult = New Label With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 4),
            .MaximumSize = New Size(contentWidth, 0), .MinimumSize = New Size(contentWidth, 0),
            .ForeColor = Color.DimGray, .Text = ""}
        tlp.Controls.Add(_lblResult)

        Dim btnFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Right, .Margin = New Padding(0, 4, 0, 0),
            .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False}
        _btnClose = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 6, 12, 6), .Margin = New Padding(0),
            .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.OK}
        btnFlow.Controls.Add(_btnClose)
        tlp.Controls.Add(btnFlow)

        Me.Controls.Add(tlp)
        Me.AcceptButton = _btnClose
        Me.CancelButton = _btnClose

        ApplyState()
        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Private Function MakeAction(text As String, onClick As Action) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Dock = DockStyle.Fill, .Padding = New Padding(12, 6, 12, 6),
            .Margin = New Padding(0, 2, 0, 2), .Text = text}
        AddHandler b.Click, Sub() onClick()
        Return b
    End Function

    Private Shared Function NoteLabel(text As String, width As Integer) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(width, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 10), .Text = text}
    End Function

    ''' <summary>
    ''' Paints the console from the live host mode + SCM state. Which buttons exist
    ''' at all is decided here rather than by disabling them, so a User edition never
    ''' shows service lifecycle controls it has no helper to run - and the Server
    ''' edition never shows an "install the Server edition" it already is.
    ''' </summary>
    Private Sub ApplyState()
        Dim mode As ServerFeatures.ServerHostMode = ServerFeatures.HostMode()
        Dim state As ServiceControl.ServiceState = ServiceControl.QueryState()
        Dim installed As Boolean = state <> ServiceControl.ServiceState.NotInstalled
        Dim serviceServing As Boolean = state = ServiceControl.ServiceState.Running OrElse
                                        state = ServiceControl.ServiceState.Starting

        _lblHost.Text = HostingText.HostModeLine(mode)
        _lblIntro.Text = HostingText.Intro(mode)

        ' Fresh probe rather than the status we were handed: this window is where a
        ' user comes when something looks wrong, and a cached snapshot is exactly what
        ' would hide a worker that died a minute ago.
        Dim live As WorkerResponse = WorkerProcess.TryGetStatus(1200)
        Dim st As WorkerStatus = If(live IsNot Nothing, live.Status, _status)
        _lblLive.Text = HostingText.LiveHostLine(serviceServing, live IsNot Nothing)
        _lblLive.ForeColor = If(live IsNot Nothing, Color.ForestGreen, Color.Firebrick)

        _lblService.Text = HostingText.ServiceStateLine(state)
        _lblService.ForeColor = If(state = ServiceControl.ServiceState.Running, Color.ForestGreen, SystemColors.ControlText)

        Dim running As Boolean = st IsNot Nothing AndAlso st.Running
        Dim roots As Integer = If(st IsNot Nothing AndAlso st.Roots IsNot Nothing, st.Roots.Count, 0)
        _lblServing.Text = HostingText.ServingLine(running, roots)
        _lblStore.Text = HostingText.StateStoreLine(ServiceControl.ActiveDataDir())

        ' Two ways to reach always-on hosting, and only one of them is offered at a
        ' time. An ordinary installation now carries the elevated helper and can take
        ' the role on itself (one UAC prompt, no second download); a Store package
        ' cannot register a service at all, so there the honest offer is still the page.
        Dim canSwitch As Boolean = ServiceControl.CanSwitchToService()
        _btnSwitchToService.Visible = canSwitch
        _btnInstallServer.Visible = (Not installed) AndAlso (Not canSwitch)
        If _noteDownload IsNot Nothing Then
            _noteDownload.Visible = _btnInstallServer.Visible
            _noteDownload.Text = If(AutostartManager.IsPackaged(),
                                    HostingText.SwitchUnavailablePackagedHint(),
                                    HostingText.DownloadNote())
        End If
        _btnReturnToUser.Visible = installed

        ' Lifecycle controls need the elevated helper, which only the Server installer
        ' lays down. They key off "installed", not off "running": a stopped or broken
        ' service is precisely when Start / Restart / Repair are needed, and hiding
        ' them there would leave it unfixable from here.
        Dim canManage As Boolean = installed AndAlso ServiceControl.CanManage()
        _btnStart.Visible = canManage AndAlso Not serviceServing
        _btnStop.Visible = canManage AndAlso serviceServing
        _btnRestart.Visible = canManage
        _btnRepair.Visible = canManage
        _btnRemove.Visible = canManage
        ' Granting the roots is only meaningful for the service account; in User mode
        ' the worker already runs as the person who picked the folders.
        _btnGrantRoots.Visible = canManage AndAlso roots > 0
        _grantRoots = New List(Of String)()
        _grantReadOnlyRoots = New List(Of String)()
        If st IsNot Nothing AndAlso st.Roots IsNot Nothing Then
            For Each r As ShareFolder In st.Roots
                If Not String.IsNullOrEmpty(r.hostPath) Then
                    _grantRoots.Add(r.hostPath)
                    ' A writable root needs write access for the account that actually
                    ' serves it, so the two lists must come from the SAME status the
                    ' folder list is showing - not from a default.
                    If r.readOnly Then _grantReadOnlyRoots.Add(r.hostPath)
                End If
            Next
        End If

        If installed AndAlso Not ServiceControl.CanManage() Then
            _lblResult.ForeColor = Color.DimGray
            _lblResult.Text = HostingText.ManageUnavailable()
        End If
    End Sub

    ''' <summary>The folder paths the grant action would cover, refreshed with the
    ''' state so the elevated call never works from a stale list.</summary>
    Private _grantRoots As New List(Of String)()

    ''' <summary>The subset of <see cref="_grantRoots"/> shared read-only - the rest is
    ''' granted read/write.</summary>
    Private _grantReadOnlyRoots As New List(Of String)()

    Private Sub OnGrantRoots()
        RunManage(ServiceControl.ManageAction.GrantRoots, _grantRoots, _grantReadOnlyRoots)
    End Sub

    ''' <summary>
    ''' Take this installation from user-session hosting to the Windows service. The
    ''' consequences are spelled out BEFORE the UAC prompt, because this is a
    ''' machine-wide role: a service that starts with Windows, a firewall rule, and a
    ''' service account granted access to the shared folders. The roots are handed to
    ''' the same elevated call so the switch does not end in a service that runs and
    ''' serves nothing - and does not need a second prompt to fix that.
    ''' </summary>
    Private Sub OnSwitchToService()
        If MessageBox.Show(Me, HostingText.SwitchToServicePrompt(), HostingText.Title(),
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        RunManage(ServiceControl.ManageAction.MigrateToServer, _grantRoots, _grantReadOnlyRoots)
    End Sub

    Private Sub OnOpenServerPage()
        ' Open the page, never fetch or run anything (spec §1.4 / §2).
        NetworkInfo.OpenInBrowser(HostingText.ServerEditionUrl)
    End Sub

    Private Sub RunManage(action As ServiceControl.ManageAction,
                          Optional roots As List(Of String) = Nothing,
                          Optional readOnlyRoots As List(Of String) = Nothing)
        SetActionsEnabled(False)
        _lblResult.ForeColor = Color.DimGray
        _lblResult.Text = HostingText.ManageWorking()
        Me.Refresh()

        ' Blocks on the UAC prompt (modal, brief) - the app itself stays non-elevated.
        Dim res As ServiceControl.ManageResult = ServiceControl.Manage(action, roots, readOnlyRoots)

        If res = ServiceControl.ManageResult.Succeeded Then
            _Changed = True
            ServerFeatures.RefreshHostMode()
        End If
        _lblResult.ForeColor = If(res = ServiceControl.ManageResult.Succeeded, Color.ForestGreen, Color.Firebrick)
        _lblResult.Text = HostingText.ManageResultLine(res)
        SetActionsEnabled(True)
        ApplyState()
    End Sub

    Private Sub SetActionsEnabled(value As Boolean)
        For Each b As Button In New Button() {_btnSwitchToService, _btnInstallServer, _btnReturnToUser, _btnStart, _btnStop,
                                              _btnRestart, _btnRepair, _btnGrantRoots, _btnRemove}
            If b IsNot Nothing Then b.Enabled = value
        Next
        Me.UseWaitCursor = Not value
    End Sub

End Class
