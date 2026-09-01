Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Companion main window - LEVEL 1 of the two-wizard model (§4.5): manage the shared
''' folders and see phone-access status. The big "Поделиться" action opens the one-shot
''' Package wizard (level 2).
'''
''' Shape, since SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md: a small, resizable window
''' whose only growing element is the folder list. A header strip answers "is anything
''' being shared right now" and carries the three actions; three COLLAPSIBLE SECTIONS hold
''' everything that is read once during setup (addresses and credentials, the internet
''' path, the counters), each keeping a live one-line summary while folded; a settings
''' window holds what is only interesting once (autostart, the connection cap, hosting).
''' Nothing was dropped in that move - §3.7 of the spec is the map, and §1 the inventory.
'''
''' The class is split by concern, LITE's Main_Form style; fields and methods are shared
''' across all four files, so edit the one matching the concern:
'''   MainWindow.vb          - state, constructor, lifecycle, gate, busy/hint, tray entry points
'''   MainWindow.Layout.vb   - BuildUi, header/status strips, sections, geometry, DPI
'''   MainWindow.Access.vb   - address rows, status -> UI, summaries, reachability, router
'''   MainWindow.Folders.vb  - the share list and the server operations behind it
''' </summary>
Partial Public NotInheritable Class MainWindow
    Inherits Form

    ' --- state ------------------------------------------------------------------
    Private ReadOnly _initialFolder As String
    Private _busy As Boolean
    Private _loading As Boolean
    Private _entered As Boolean
    Private _listPopulated As Boolean
    Private _reachPollGen As Integer
    Private _testing As Boolean
    Private _openShareWhenReady As Boolean
    Private _wizardOpen As Boolean
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus
    Private _suppressCheck As Boolean
    Private _router As RouterIdentity
    Private _routerRequested As Boolean
    ''' <summary>How many shared folders the service account cannot read, as of the last
    ''' status refresh. Drives the state line's colour: a share whose folders the server
    ''' cannot open is not a working share, however healthy the listener is.</summary>
    Private _rootsWithoutAccess As Integer
    ''' <summary>The grant offer is made once per window session. Declining is a real
    ''' answer (the console can still fix it later), and re-asking on every status
    ''' refresh would turn a choice into nagging.</summary>
    Private _grantOffered As Boolean
    ''' <summary>The folder set whose SUBTREES have already been walked. Same reason as
    ''' <see cref="_grantOffered"/>, plus a second one: the walk touches the disk, so
    ''' repeating it for an unchanged list would be a cost as well as a nag.</summary>
    Private _subtreeCheckedKey As String = ""
    ''' <summary>Host path of the row created or edited last, so the list can put the
    ''' selection back on it - the row a user has just been working with is the one the
    ''' next action is almost always meant for.</summary>
    Private _lastTouchedRoot As String = ""
    Private _iconHandle As IntPtr
    ' Code-drawn button glyphs; built in BuildUi at the display's DPI (see _shareGlyph there).
    Private _copyGlyph As Image
    Private _addGlyph As Image
    Private _shareGlyph As Image
    Private _gearGlyph As Image
    Private _eyeGlyph As Image

    ' --- layout state (see MainWindow.Layout.vb) --------------------------------
    ''' <summary>Guards the list-row height computation against the layout events its own
    ''' assignment raises.</summary>
    Private _relayouting As Boolean
    ''' <summary>Two columns (list | sections) instead of one stack - decision A of §10.
    ''' Switches at 1040 logical px and back at 1000, so a drag along the edge cannot
    ''' flicker between the two.</summary>
    Private _wideMode As Boolean
    Private _contentModeApplied As Boolean
    ''' <summary>Section state is restored once, from Share_ExpandedSections, and only
    ''' persisted after that - so the restore itself cannot be mistaken for user intent.</summary>
    Private _sectionsRestored As Boolean
    Private _passwordRevealed As Boolean

    ' --- controls ---------------------------------------------------------------
    Private pnlContent As Panel
    Private progressBar As ProgressBar
    Private lvFolders As ListView
    Private btnAdd As Button
    Private btnAddCurrent As Button
    Private btnRemove As Button
    Private btnParams As Button
    Private btnToggle As Button
    Private btnShare As Button
    Private btnSettings As Button
    Private btnGuide As Button
    Private btnTest As Button
    Private btnStatsDetails As Button
    Private btnRevealPassword As Button
    Private btnHelp As Button
    Private lblState As Label
    Private lblStateDot As Label
    Private lblIntro As Label
    Private lblAccessState As Label
    Private lblAccessNext As Label
    Private lnkRouter As LinkLabel
    Private lblHint As Label
    Private pnlStats As TableLayoutPanel
    Private lblStatLast As Label
    Private lblStatConns As Label
    Private lblStatFiles As Label
    Private _statsTimer As Timer   ' periodic status refresh so the usage block stays live while the window is open
    Private lblHosting As Label
    Private miAndroid As ToolStripMenuItem
    Private miSiteGuide As ToolStripMenuItem
    Private miRouterSearch As ToolStripMenuItem
    Private miOpenViewer As ToolStripMenuItem
    Private lnkLanguage As LinkLabel
    Private toolTip As ToolTip
    Private ReadOnly _serverRows As New List(Of ServerRow)()

    ' --- layout containers ------------------------------------------------------
    Private _root As TableLayoutPanel
    Private _pnlHeader As TableLayoutPanel
    Private _pnlList As Panel
    Private _pnlListButtons As FlowLayoutPanel
    Private _pnlSections As TableLayoutPanel
    Private _secAccess As CollapsibleSection
    Private _secInternet As CollapsibleSection
    Private _secStats As CollapsibleSection

    Private pnlEnable As Panel
    Private btnEnable As Button

    Public Event ServerStateChanged(running As Boolean)

    Public Sub New(Optional initialFolder As String = Nothing)
        _initialFolder = If(initialFolder, "")
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub

    ''' <summary>One caption|value|copy row of the server grid + how to fill it.</summary>
    Private NotInheritable Class ServerRow
        Public ReadOnly Cap As Label
        Public ReadOnly Value As Label
        Public ReadOnly Copy As Button
        Public ReadOnly ValueFunc As Func(Of String)
        Public ReadOnly CopyFunc As Func(Of String)
        Public ReadOnly AlwaysShow As Boolean
        ''' <summary>An extra control riding in the row's fourth column (the password
        ''' reveal toggle); hidden and shown with the row.</summary>
        Public ReadOnly Extra As Control
        Public Sub New(cap As Label, value As Label, copy As Button, valueFunc As Func(Of String), copyFunc As Func(Of String), alwaysShow As Boolean, extra As Control)
            Me.Cap = cap : Me.Value = value : Me.Copy = copy
            Me.ValueFunc = valueFunc : Me.CopyFunc = copyFunc : Me.AlwaysShow = alwaysShow
            Me.Extra = extra
        End Sub
    End Class

    ' --- lifecycle --------------------------------------------------------------

    ''' <summary>
    ''' Restores the remembered rectangle, then caps the window to the monitor working
    ''' area. The cap used to be load-bearing: a 980x700 MinimumSize scaled to 1715x1225 at
    ''' 175% display scaling, taller than the screen, so every session opened at its
    ''' minimum and could not be shrunk. With 560x420 the cap is what it was meant to be -
    ''' a safety net, here for the case where a size restored from a bigger monitor (or a
    ''' docking station that has since gone) would not fit this one.
    ''' </summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        RestoreGeometry()
        DpiLayout.ClampToWorkingArea(Me)
        ApplyMaximizedState()
        UpdateContentMode()
        RelayoutContent()
    End Sub

    ''' <summary>Dragged onto a monitor with different scaling: WinForms rescales the bounds,
    ''' paddings and fonts for us, but not the two things below, so they are re-derived here.</summary>
    Protected Overrides Sub OnDpiChanged(e As DpiChangedEventArgs)
        MyBase.OnDpiChanged(e)
        BuildGlyphs()
        ApplyDpiScaledAssets()
        UpdateContentMode()
        RelayoutContent()
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        UpdateContentMode()
        RelayoutContent()
    End Sub

    ''' <summary>A revealed password is per session AND per focus: leaving the window is
    ''' exactly the moment a screen share or a passer-by sees it (§3.6).</summary>
    Protected Overrides Sub OnDeactivate(e As EventArgs)
        MyBase.OnDeactivate(e)
        SetPasswordRevealed(False)
    End Sub

    ''' <summary>
    ''' Routes a folder into the already-open window - the "share this folder.." path from the
    ''' viewer when this window happens to be up.
    '''
    ''' It used to be dropped on the floor: TrayContext only passed the folder to the MainWindow
    ''' CONSTRUCTOR, so with the window already open the wake merely activated it. The user was
    ''' told nothing, the folder was never added, and the phone never saw it - and because it
    ''' worked whenever the window happened to be closed, the failure looked random.
    ''' </summary>
    Friend Async Function ShareFolderFromWakeAsync(folder As String) As Task
        If String.IsNullOrEmpty(folder) Then Return
        If Not ServerFeatures.IsEnabled() OrElse Not WorkerProcess.IsAvailable() Then Return
        If Not Directory.Exists(folder) Then Return

        ' Same three steps OnShownFirst takes for a folder handed to the constructor.
        If AddShareRow(folder) Then Await ApplySharedFoldersAsync()
        OnShareClicked(Me, EventArgs.Empty)
    End Function

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        If _entered Then Return
        _entered = True
        Await EnterAsync()
        _statsTimer.Start()   ' keep the usage-stats block live while the window is open
        If _initialFolder.Length > 0 AndAlso ServerFeatures.IsEnabled() AndAlso WorkerProcess.IsAvailable() Then
            If Directory.Exists(_initialFolder) AndAlso AddShareRow(_initialFolder) Then Await ApplySharedFoldersAsync()
            OnShareClicked(Me, EventArgs.Empty)
        ElseIf _openShareWhenReady Then
            ' Deferred tray "Поделиться.." - status is loaded now; open the wizard if the
            ' server came up, otherwise OnShareClicked will hint to start it.
            _openShareWhenReady = False
            OnShareClicked(Me, EventArgs.Empty)
        End If
    End Sub

    Private Sub HandleFormClosing(sender As Object, e As FormClosingEventArgs)
        Try
            ' Re-read first: Share_Settings_Form owns the autostart / connection-cap values
            ' now and writes them through its OWN ShareSettings instance, so saving this
            ' window's copy without refreshing would clobber a change made minutes ago.
            ' Only the geometry below is ours to write.
            _settings.Load()
            CaptureGeometry()
            _settings.Save()
        Catch
        End Try
        Try : _statsTimer.Stop() : _statsTimer.Dispose() : Catch : End Try
        ShareIcons.FreeIcon(Me.Icon, _iconHandle)
    End Sub

    ' --- gate + entry -----------------------------------------------------------

    Private Sub ApplyGate()
        Dim enabled As Boolean = ServerFeatures.IsEnabled()
        pnlContent.Visible = enabled
        pnlEnable.Visible = Not enabled
        If enabled Then pnlContent.BringToFront() Else pnlEnable.BringToFront()
    End Sub

    Private Async Function EnterAsync() As Task
        LoadLocalState()
        ApplyGate()
        If Not ServerFeatures.IsEnabled() Then Return

        If Not WorkerProcess.IsAvailable() Then
            SetHint(Localization.T("Компонент общего доступа не найден - переустановите приложение."))
            SetServerControlsEnabled(False)
            Return
        End If

        SetBusy(True, Localization.T("Запуск компаньона.."))
        Dim st As WorkerStatus = Await ShareController.EnsureRunningReconciledAsync()
        If st Is Nothing Then
            ' In Server mode "cannot reach the companion" is the wrong diagnosis and
            ' the wrong advice: nothing here may spawn a worker, so the actionable
            ' fact is what the SERVICE is doing - and the fix lives one button away.
            If ServerFeatures.IsSystemServiceHost() Then
                SetHint(HostingText.ServiceStateLine(ServiceControl.QueryState()))
            Else
                SetHint(Localization.T("Не удалось связаться с компаньоном."))
            End If
            SetBusy(False)
            Return
        End If

        _status = st
        If Not _listPopulated Then
            PopulateFolders(st.Roots)
            _listPopulated = True
        End If
        ApplyStatusToUi()
        SetBusy(False)
        SetHint("")

        ' The service can already be serving folders it has no access to - registered by
        ' something that had no folder list to pass it, or with the offer declined once.
        ' Opening this window is the first moment anyone could see that, so it is also
        ' the right moment to offer the fix rather than wait for a folder to be re-picked.
        Await OfferGrantForServedFoldersAsync()

        If st.Running AndAlso st.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Function

    Private Sub LoadLocalState()
        Dim prev As Boolean = _loading
        _loading = True
        Try
            _settings.Load()
            UpdateHostingBlock()
        Catch
        Finally
            _loading = prev
        End Try
    End Sub

    Private Sub OnEnableServer(sender As Object, e As EventArgs)
        Using dlg As New Share_Enable_Form()
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                ApplyGate()
                _entered = False
                _listPopulated = False
                Dim t As Task = EnterAsync()
            End If
        End Using
    End Sub

    ' --- package wizard + viewer launch ----------------------------------------

    ''' <summary>Tray "Поделиться.." entry point: open the package wizard now if the
    ''' server is already up, else defer until the first status load finishes (the tray
    ''' can call this before OnShownFirst/EnterAsync has populated _status).</summary>
    Friend Sub OpenShareWizardFromTray()
        If _status Is Nothing Then
            ' Fresh window - status not loaded yet. Defer ONLY here: OnShownFirst consumes
            ' the flag once EnterAsync finishes. (Deferring in any other state leaves the
            ' flag dead, since OnShownFirst runs exactly once - the review's dead-flag bug.)
            _openShareWhenReady = True
        Else
            ' Window was already open (status loaded). Open now; OnShareClicked shows the
            ' "start the server first" hint when the server is not running.
            OnShareClicked(Me, EventArgs.Empty)
        End If
    End Sub

    Private Sub OnShareClicked(sender As Object, e As EventArgs)
        If _wizardOpen Then Return   ' guard tray re-entry while the modal wizard is already up
        If _status Is Nothing OrElse Not _status.Running Then
            SetHint(Localization.T("Сначала запустите сервер."))
            Return
        End If
        Dim preselect As New List(Of String)()
        For Each it As ListViewItem In lvFolders.Items
            If it.Checked Then preselect.Add(Convert.ToString(it.Tag))
        Next
        _wizardOpen = True
        Try
            Using dlg As New PackageWizardForm(preselect)
                dlg.ShowDialog(Me)
            End Using
        Finally
            _wizardOpen = False
        End Try
    End Sub

    Private Sub OnOpenViewerClicked(sender As Object, e As EventArgs)
        Try
            Dim dir As String = Path.GetDirectoryName(Application.ExecutablePath)
            Dim exe As String = Path.Combine(dir, "FastMediaSorter_LITE.exe")
            If File.Exists(exe) Then
                Process.Start(New ProcessStartInfo(exe) With {.UseShellExecute = True})
            Else
                SetHint(Localization.T("Fast Media Sorter не найден рядом."))
            End If
        Catch
        End Try
    End Sub

    ''' <summary>Opens the manager settings (autostart, connection cap, hosting). Re-reads
    ''' status afterwards only when an elevated hosting action actually changed the machine
    ''' - the service may have just been installed, started or removed under us.</summary>
    Private Async Sub OnSettingsClicked(sender As Object, e As EventArgs)
        Dim changed As Boolean
        Using dlg As New Share_Settings_Form(_status)
            dlg.ShowDialog(Me)
            ' Either an elevated hosting action changed the machine, or a pinned port moved
            ' the running server - both leave the status snapshot here stale.
            changed = dlg.Changed OrElse dlg.WorkerStateChanged
        End Using
        LoadLocalState()
        If Not changed Then Return
        SetBusy(True, Localization.T("Минутку.."))
        _status = Await ShareController.GetStatusAsync()
        ApplyStatusToUi()
        SetBusy(False)
    End Sub

    ' --- hint / busy ------------------------------------------------------------

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

    Private Sub SetServerControlsEnabled(enabled As Boolean)
        For Each c As Control In New Control() {btnToggle, btnAdd, btnAddCurrent, btnRemove, btnParams, lvFolders, btnShare, btnGuide}
            If c IsNot Nothing Then c.Enabled = enabled
        Next
    End Sub

    Private Sub SetBusy(value As Boolean, Optional message As String = Nothing)
        _busy = value
        Dim avail As Boolean = WorkerProcess.IsAvailable()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        btnToggle.Enabled = Not value AndAlso avail
        btnAdd.Enabled = Not value AndAlso avail
        btnAddCurrent.Enabled = Not value AndAlso avail
        btnRemove.Enabled = Not value AndAlso avail
        lvFolders.Enabled = Not value AndAlso avail
        ' btnParams adds "and a row is selected" on top of the same rule - see
        ' RefreshRowActionButtons, which is the ONE place that decides it.
        RefreshRowActionButtons()
        btnShare.Enabled = Not value AndAlso avail AndAlso running
        btnGuide.Enabled = Not value AndAlso avail AndAlso running
        If progressBar IsNot Nothing Then progressBar.Visible = value
        If value AndAlso Not String.IsNullOrEmpty(message) Then SetHint(message)
        Me.UseWaitCursor = value
        ' btnTest is the one button whose state is computed elsewhere (status + busy),
        ' so listing it above would duplicate that rule. It still has to be re-asked
        ' here: every flow calls ApplyStatusToUi() INSIDE the busy window and then
        ' SetBusy(False), so the button kept the disabled state it was given while busy
        ' and nothing came along to clear it - the reachability poll that would have
        ' refreshed it only runs when the address is still unknown. The result was a
        ' permanently grey "check access from the internet" on a share that works.
        RefreshTestButton()
    End Sub

End Class
