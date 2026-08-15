Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The Hosting console (SPECIFICATION_SHARE_SYSTEM_SERVICE.md §2, §3.6, §4.3), as the third
''' collapsible group of the manager settings.
'''
''' Two jobs, and the second one is why it is a group of its own rather than a few more rows
''' under "Сеть":
'''   * say which host owns the worker and, separately, what the service and the SFTP
'''     server are each doing right now - "service installed", "service running", "SFTP
'''     serving" and "no folders configured" are four different states and the main window
'''     has no room to keep them apart;
'''   * gather every machine-affecting action in one place, each behind a visible UAC prompt
'''     via the elevated helper. Routine Start/Stop sharing stays on the main window as
'''     plain IPC and never elevates - only service lifecycle does.
'''
''' It was `Share_Hosting_Form`, its own modal, until 2026-08-15. Merging it in cost nothing
''' structurally - the settings window had become its only caller - and removed the fourth
''' level of nesting on the path to an elevated action. Nothing about WHAT elevates, or
''' about the single auditable helper it goes through, changed: the code below is the old
''' window's body, moved.
'''
''' It never downloads or launches an installer: the edition-change entries open the
''' documented page and stop there (spec §1.4).
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    ''' <summary>The notes here are long prose; this is the width they wrap at, and it is
    ''' what makes the settings dialog as wide as it is.</summary>
    Private Const HostingContentWidth As Integer = 520

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

    ''' <summary>The folder paths the grant action would cover, refreshed with the
    ''' state so the elevated call never works from a stale list.</summary>
    Private _grantRoots As New List(Of String)()

    ''' <summary>The subset of <see cref="_grantRoots"/> shared read-only - the rest is
    ''' granted read/write.</summary>
    Private _grantReadOnlyRoots As New List(Of String)()

    Private Sub BuildHostingSection(sec As CollapsibleSection)
        _lblHost = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 6),
            .MaximumSize = New Size(HostingContentWidth, 0),
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.15F, FontStyle.Bold)}
        _lblIntro = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 10),
            .MaximumSize = New Size(HostingContentWidth, 0)}
        ' The four facts, each on its own line and never merged: who is answering the
        ' pipe right now, what the SCM says about the service, whether SFTP is actually
        ' serving, and which state store both hosts are working on.
        _lblLive = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
            .MaximumSize = New Size(HostingContentWidth, 0), .Font = New Font(Me.Font, FontStyle.Bold)}
        _lblService = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
            .MaximumSize = New Size(HostingContentWidth, 0)}
        _lblServing = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 2),
            .MaximumSize = New Size(HostingContentWidth, 0), .ForeColor = SystemColors.GrayText}
        _lblStore = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 12),
            .MaximumSize = New Size(HostingContentWidth, 0), .ForeColor = SystemColors.GrayText}

        ' The group's header already names it, so the console's own bold title would be the
        ' same word twice; the mode line it used to carry is the group's live summary now.
        sec.AddBodyRow(_lblHost)
        sec.AddBodyRow(_lblIntro)
        sec.AddBodyRow(_lblLive)
        sec.AddBodyRow(_lblService)
        sec.AddBodyRow(_lblServing)
        sec.AddBodyRow(_lblStore)

        _btnSwitchToService = MakeAction(HostingText.SwitchToServiceButton(), AddressOf OnSwitchToService)
        _btnInstallServer = MakeAction(HostingText.InstallServerButton(), AddressOf OnOpenServerPage)
        _btnReturnToUser = MakeAction(HostingText.ReturnToUserButton(), AddressOf OnOpenServerPage)
        _btnStart = MakeAction(HostingText.StartServiceButton(), Sub() RunManage(ServiceControl.ManageAction.StartService))
        _btnStop = MakeAction(HostingText.StopServiceButton(), Sub() RunManage(ServiceControl.ManageAction.StopService))
        _btnRestart = MakeAction(HostingText.RestartServiceButton(), Sub() RunManage(ServiceControl.ManageAction.RestartService))
        _btnRepair = MakeAction(HostingText.RepairServiceButton(), Sub() RunManage(ServiceControl.ManageAction.Repair))
        _btnGrantRoots = MakeAction(HostingText.GrantRootsButton(), AddressOf OnGrantRoots)
        _btnRemove = MakeAction(HostingText.RemoveRoleButton(), Sub() RunManage(ServiceControl.ManageAction.Remove))

        sec.AddBodyRow(_btnSwitchToService)
        sec.AddBodyRow(_btnInstallServer)
        ' Kept as a field: this note explains the DOWNLOAD button, so it must disappear
        ' together with it when this installation can take the role on by itself, and it
        ' says something different in a Store build, which cannot.
        _noteDownload = NoteLabel(HostingText.DownloadNote())
        sec.AddBodyRow(_noteDownload)
        sec.AddBodyRow(_btnReturnToUser)
        sec.AddBodyRow(_btnStart)
        sec.AddBodyRow(_btnStop)
        sec.AddBodyRow(_btnRestart)
        sec.AddBodyRow(_btnRepair)
        sec.AddBodyRow(_btnGrantRoots)
        sec.AddBodyRow(NoteLabel(HostingText.AccountNote()))
        sec.AddBodyRow(_btnRemove)
        sec.AddBodyRow(NoteLabel(HostingText.RemoveNote()))

        _lblResult = New Label With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 0),
            .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = Color.DimGray, .Text = ""}
        sec.AddBodyRow(_lblResult)
    End Sub

    Private Function MakeAction(text As String, onClick As Action) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Dock = DockStyle.Fill, .Padding = New Padding(12, 6, 12, 6),
            .Margin = New Padding(0, 2, 0, 2), .Text = text}
        AddHandler b.Click, Sub() onClick()
        Return b
    End Function

    Private Shared Function NoteLabel(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 10), .Text = text}
    End Function

    ''' <summary>
    ''' Refreshes the group from a LIVE worker probe, the first time it is actually unfolded.
    '''
    ''' The structure was already applied without one while the dialog was being built (see
    ''' the <c>probeLive</c> parameter below), so this is not what decides the layout - it is
    ''' what makes the two live lines honest. Splitting it that way is what keeps the dialog
    ''' from opening at its worst-case height AND from paying a worker round trip every time
    ''' someone opens it to tick one checkbox.
    ''' </summary>
    Private Sub EnsureHostingLoaded()
        If _hostingLoaded Then Return
        _hostingLoaded = True
        ApplyHostingState(True)
        UpdateSummaries()
        FitToContent()
    End Sub

    ''' <summary>
    ''' Paints the console from the live host mode + SCM state. Which buttons exist
    ''' at all is decided here rather than by disabling them, so a User edition never
    ''' shows service lifecycle controls it has no helper to run - and the Server
    ''' edition never shows an "install the Server edition" it already is.
    '''
    ''' <paramref name="probeLive"/> False uses the status the caller handed in instead of
    ''' asking the worker. Everything that decides which controls EXIST comes from the SCM
    ''' and from whether the elevated helper is installed - both cheap and both local - so a
    ''' probe-free pass produces the same layout, which is all the build needs. Only the
    ''' "who is answering the pipe right now" line and the served-root list want the live
    ''' answer, and those are corrected the moment the tab is opened.
    ''' </summary>
    Private Sub ApplyHostingState(probeLive As Boolean)
        Dim mode As ServerFeatures.ServerHostMode = ServerFeatures.HostMode()
        Dim state As ServiceControl.ServiceState = ServiceControl.QueryState()
        Dim installed As Boolean = state <> ServiceControl.ServiceState.NotInstalled
        Dim serviceServing As Boolean = state = ServiceControl.ServiceState.Running OrElse
                                        state = ServiceControl.ServiceState.Starting

        _lblHost.Text = HostingText.HostModeLine(mode)
        _lblIntro.Text = HostingText.Intro(mode)

        ' Fresh probe rather than the status we were handed: this page is where a user
        ' comes when something looks wrong, and a cached snapshot is exactly what would
        ' hide a worker that died a minute ago.
        Dim live As WorkerResponse = If(probeLive, WorkerProcess.TryGetStatus(1200), Nothing)
        Dim answering As Boolean = If(probeLive, live IsNot Nothing, _status IsNot Nothing)
        Dim st As WorkerStatus = If(live IsNot Nothing, live.Status, _status)
        _lblLive.Text = HostingText.LiveHostLine(serviceServing, answering)
        _lblLive.ForeColor = If(answering, Color.ForestGreen, Color.Firebrick)

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
        SetHostingActionsEnabled(False)
        _lblResult.ForeColor = Color.DimGray
        _lblResult.Text = HostingText.ManageWorking()
        Me.Refresh()

        ' Blocks on the UAC prompt (modal, brief) - the app itself stays non-elevated.
        Dim res As ServiceControl.ManageResult = ServiceControl.Manage(action, roots, readOnlyRoots)

        If res = ServiceControl.ManageResult.Succeeded Then
            _changed = True
            ServerFeatures.RefreshHostMode()
        End If
        _lblResult.ForeColor = If(res = ServiceControl.ManageResult.Succeeded, Color.ForestGreen, Color.Firebrick)
        _lblResult.Text = HostingText.ManageResultLine(res)
        SetHostingActionsEnabled(True)
        ApplyHostingState(True)
        UpdateSummaries()
        FitToContent()
    End Sub

    Private Sub SetHostingActionsEnabled(value As Boolean)
        For Each b As Button In New Button() {_btnSwitchToService, _btnInstallServer, _btnReturnToUser, _btnStart, _btnStop,
                                              _btnRestart, _btnRepair, _btnGrantRoots, _btnRemove}
            If b IsNot Nothing Then b.Enabled = value
        Next
        Me.UseWaitCursor = Not value
    End Sub

End Class
