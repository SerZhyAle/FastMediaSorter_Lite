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
''' The class is split by concern, the viewer's Main_Form style; fields and methods are shared
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
    Private _fontBold As Font
    Private _fontShare As Font
    Private _fontEnableTitle As Font
    ' Glyph-only buttons' pictures, built in BuildGlyphs at the display's DPI. Captioned buttons
    ' get their glyph from ButtonGlyphs instead.
    Private _copyGlyph As Image
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
    Private btnDisks As Button
    Private btnAddCurrent As Button
    Private btnRemove As Button
    Private btnParams As Button
    Private btnBroadcasts As Button
    Private btnToggle As Button
    Private btnShare As Button
    Private btnSettings As Button
    Private btnGuide As Button
    Private btnTest As Button
    Private btnStatsDetails As Button
    Private btnRevealPassword As Button
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
        If Not Await wake_Ready.Task OrElse IsDisposed Then Return
        If String.IsNullOrEmpty(folder) Then Return
        If Not ServerFeatures.IsEnabled() OrElse Not WorkerProcess.IsAvailable() Then Return
        If Not Await Task.Run(Function() Directory.Exists(folder)) OrElse IsDisposed Then Return

        ' Same three steps OnShownFirst takes for a folder handed to the constructor.
        If AddShareRow(folder) Then Await ApplySharedFoldersAsync()
        OnShareClicked(Me, EventArgs.Empty)
    End Function

    ''' <summary>
    ''' Puts one line on the hint row once the window has finished its first entry. For a request another
    ''' process made that this build cannot serve (a broadcast launch, spec 159 section 3.2): the line is the
    ''' answer, instead of a window the user cannot use. Waits on the same gate as the wake handlers because
    ''' the last step of <see cref="EnterAsync"/> clears the hint row and would erase a line set earlier.
    ''' </summary>
    Friend Async Function ShowNoticeAsync(text As String) As Task
        If Not Await wake_Ready.Task OrElse IsDisposed Then Return
        SetHint(text)
    End Function

    Private ReadOnly wake_Ready As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        If _entered Then Return
        _entered = True
        Try
            Await EnterAsync()
            Dim managerRequested As Boolean = _managerWelcomeRequested
            Dim managerFinished As Boolean = Await OpenManagerWelcomeAsync()
            ' Spec 185: the server welcome, once the window is on screen. It may enable the share server or write the
            ' port, so the settings are read again and the window re-enters - the same steps as after OnEnableServer.
            If _serverWelcomeAllowed AndAlso (Not managerRequested OrElse managerFinished) AndAlso
               _initialFolder.Length = 0 AndAlso Await Server_Welcome_Form.OpenDueHalvesAsync(Me, includeShare:=False) Then
                _settings.Load()
                ApplyGate()
                _entered = False
                _listPopulated = False
                Await EnterAsync()
            End If
            wake_Ready.TrySetResult(True)
        Catch ex As Exception
            wake_Ready.TrySetResult(False)
            AppFileLogger.LogException("Share Manager: opening the manager window", ex)
            Return
        End Try
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
        ' What outlives the form is released here rather than left to finalization
        ' (ticket 054 H-20): the tooltip holds a native window and references back into
        ' this form, and the glyph bitmaps are plain GDI objects a
        ' tray process has no reason to keep.
        Try : toolTip.Dispose() : Catch : End Try
        Try
            If _fontBold IsNot Nothing Then _fontBold.Dispose()
        Catch
        End Try
        Try
            If _fontShare IsNot Nothing Then _fontShare.Dispose()
        Catch
        End Try
        Try
            If _fontEnableTitle IsNot Nothing Then _fontEnableTitle.Dispose()
        Catch
        End Try
        _fontBold = Nothing
        _fontShare = Nothing
        _fontEnableTitle = Nothing
        DisposeImage(_copyGlyph)
        DisposeImage(_eyeGlyph)
        _copyGlyph = Nothing
        _eyeGlyph = Nothing
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
        ' Up to 15 s in Server mode: the window may have been closed meanwhile (ticket 054 H-11).
        If IsDisposed Then Return
        If st Is Nothing Then
            ' A worker that answered with a newer protocol is running and said so - that
            ' is not "cannot reach", and the fix is an update (WORKER-IPC section 6 rule 3).
            ' In Server mode "cannot reach the companion" is the wrong diagnosis and
            ' the wrong advice: nothing here may spawn a worker, so the actionable
            ' fact is what the SERVICE is doing - and the fix lives one button away.
            Dim refusal As String = ShareController.RefusalMessage()
            If refusal <> "" Then
                SetHint(refusal)
                ApplyStatusToUi()
            ElseIf ServerFeatures.IsSystemServiceHost() Then
                SetHint(HostingText.ServiceStateLine(ServiceControl.QueryState()))
            Else
                SetHint(Localization.T("Не удалось связаться с компаньоном."))
            End If
            SetBusy(False)
            Return
        End If

        _status = st
        Await RefreshDiskSnapshotAsync()
        If IsDisposed Then Return
        If Not _listPopulated Then
            PopulateFolders(st.Roots)
            _listPopulated = True
        End If
        ApplyStatusToUi()
        SetBusy(False)
        SetHint("")

        ' The service can already be serving folders it has no access to - registered by
        ' something that had no folder list to pass it, or with the offer declined once.
        ' Opening this window is the first moment anyone could see that, so it is also the
        ' right moment to offer the fix rather than wait for a folder to be re-picked.
        ' Awaited here (not fire-and-forget as in ApplyStatusToUi): the offer's condition
        ' is the count this walk produces, and deciding it on the previous count is how
        ' the offer silently never happens.
        Await RefreshFolderAccessAsync()
        If IsDisposed Then Return
        Await OfferGrantForServedFoldersAsync()
        If IsDisposed Then Return

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
                If IsDisposed Then Return
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
                Try
                dlg.ShowDialog(Me)
            Finally
                _settingsWindow = Nothing
            End Try
            End Using
        Finally
            _wizardOpen = False
        End Try
    End Sub


    ''' <summary>Opens the manager settings (autostart, connection cap, hosting). Re-reads
    ''' status afterwards only when an elevated hosting action actually changed the machine
    ''' - the service may have just been installed, started or removed under us.</summary>
    Friend Sub RefreshInterfaceLanguage()
        If IsDisposed Then Return
        SuspendLayout()
        Try
            LoadLocalState()
            ApplyStatusToUi()
        Finally
            ResumeLayout(True)
        End Try
    End Sub

    Private _settingsWindow As Share_Settings_Form

    Private Async Sub OnSettingsClicked(sender As Object, e As EventArgs)
        If _settingsWindow IsNot Nothing AndAlso Not _settingsWindow.IsDisposed Then
            _settingsWindow.Activate()
            Return
        End If
        Dim changed As Boolean
        Dim replayManager As Boolean
        Dim replayExchange As Boolean
        Dim addResource As Boolean
        Using dlg As New Share_Settings_Form(_status)
            _settingsWindow = dlg
            If _focusExchangeSettings Then dlg.FocusExchangePage()
            If _focusHostingSettings Then dlg.FocusHostingPage()
            If _focusPublishSettings Then dlg.FocusPublishPage()
            _focusPublishSettings = False
            _focusHostingSettings = False
            _focusExchangeSettings = False
            dlg.ShowDialog(Me)
            ' Either an elevated hosting action changed the machine, or a pinned port moved
            ' the running server - both leave the status snapshot here stale.
            changed = dlg.Changed OrElse dlg.WorkerStateChanged
            replayManager = dlg.ReplayManagerRequested
            replayExchange = dlg.ReplayExchangeRequested
            addResource = dlg.ResourceAddRequested
        End Using
        LoadLocalState()
        If addResource Then Await OpenResourceAddAsync(Me, ResourceAddEntry.Exchange)
        If replayManager Then Await ReplayManagerWelcomeAsync()
        If replayExchange Then Await Server_Welcome_Form.ReplayExchangeAsync(Me)
        If Not changed Then Return
        ' The opt-in may just have been withdrawn (ticket 028 Ф-6): back to the enable panel.
        If Not ServerFeatures.IsEnabled() Then
            ApplyGate()
            Return
        End If
        SetBusy(True, Localization.T("Минутку.."))
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If IsDisposed Then Return
        _status = st
        ApplyStatusToUi()
        SetBusy(False)
    End Sub

    ' --- hint / busy ------------------------------------------------------------

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

    Private Sub OnBroadcastsClicked(sender As Object, e As EventArgs)
        Try
            If Not BroadcastPage.ShouldOffer(_status) Then Return
            Using dlg As New Share_Broadcasts_Form()
                dlg.ShowDialog(Me)
            End Using
        Catch ex As Exception
            AppFileLogger.LogException("MainWindow.OnBroadcastsClicked", ex)
        End Try
    End Sub

    Private Sub SetServerControlsEnabled(enabled As Boolean)
        For Each c As Control In New Control() {btnToggle, btnAdd, btnDisks, btnAddCurrent, btnRemove, btnParams, btnBroadcasts, lvFolders, btnShare, btnGuide}
            If c IsNot Nothing Then c.Enabled = enabled
        Next
    End Sub

    Private Sub SetBusy(value As Boolean, Optional message As String = Nothing)
        _busy = value
        Dim avail As Boolean = WorkerProcess.IsAvailable()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        btnToggle.Enabled = Not value AndAlso avail
        btnAdd.Enabled = Not value AndAlso avail
        RefreshEmptyResourceHint()
        btnDisks.Enabled = Not value AndAlso avail
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
