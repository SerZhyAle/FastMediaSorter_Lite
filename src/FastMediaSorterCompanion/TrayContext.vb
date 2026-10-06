Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Tray-resident application host. Companion is ALWAYS tray-resident (its normal
''' state, not the close-to-tray crutch the viewer needed) - so a bare launch shows no
''' window, just the notify icon and a hidden receiver window for the
''' viewer -&gt; Companion wake handshake (WM_COPYDATA). Owns the single reusable
''' MainWindow and, on launch, silently resumes sharing if the user ever shared
''' before (ResumeShareIfEnabled analogue).
''' </summary>
Friend NotInheritable Class TrayContext
    Inherits ApplicationContext

    Private ReadOnly _notifyIcon As NotifyIcon
    Private ReadOnly _messageWindow As MessageWindow
    Private ReadOnly _trayIcon As Icon
    Private _trayHIcon As IntPtr
    Private _mainWindow As MainWindow
    Private _disposed As Boolean
    Private _pendingStartupProposals As Boolean
    Private ReadOnly _statsTimer As Timer   ' keeps the tooltip's usage counters reasonably fresh while serving
    Private ReadOnly _diskLetterTimer As Timer
    Private _diskLetterFetchInFlight As Boolean

    ''' <param name="windowRequested">The launch itself asked for the window (the viewer's Share
    ''' Manager button, --show). Such a request always wins over the startup option.</param>
    ''' <param name="silentLaunch">The logon autostart (--tray). Suppresses even the
    ''' "I am in the tray" hint, so a boot stays completely quiet.</param>
    Friend Sub New(initialFolder As String, Optional windowRequested As Boolean = False, Optional silentLaunch As Boolean = False)
        _trayIcon = ShareIcons.CreateIcon(_trayHIcon)

        _notifyIcon = New NotifyIcon() With {
            .Icon = _trayIcon,
            .Text = ProductNames.ShareManager,
            .Visible = True
        }
        _notifyIcon.ContextMenuStrip = BuildMenu()
        AddHandler _notifyIcon.DoubleClick, Sub() ShowMainWindow(Nothing)
        AddHandler UiLanguage.Changed, AddressOf OnLanguageChanged

        _messageWindow = New MessageWindow()
        _messageWindow.StageFolder = AddressOf StageFolder
        AddHandler _messageWindow.CommandAccepted, AddressOf OnPayloadReceived
        AddHandler Application.Idle, AddressOf OnFirstIdle

        _statsTimer = New Timer With {.Interval = 30000}
        AddHandler _statsTimer.Tick, AddressOf OnStatsTimerTick
        _diskLetterTimer = New Timer With {.Interval = 30000}
        AddHandler _diskLetterTimer.Tick, AddressOf OnDiskLetterTimerTick
        _diskLetterTimer.Start()

        ' Resume sharing silently if it was ever started - covers the "dedicated
        ' server" scenario where Companion autostarts into the tray with no window.
        ResumeShareIfEnabled()

        ' Who gets a window. An EXPLICIT gesture always does: a folder argument ("share
        ' this folder", jumps to the package wizard) or windowRequested (the viewer's Share
        ' Manager button / --show). Everything else - logon autostart, a double-click on
        ' the exe, a script - is a plain start and obeys the "Открывать окно менеджера
        ' при запуске" option, which is OFF by default: the program then lives in the
        ' tray exactly as it does at logon (spec §4.5.1 / §4.3). A plain start that is
        ' not the silent logon one leaves a short tray hint instead, so a user who
        ' double-clicked the exe is not left wondering whether anything started.
        If Not String.IsNullOrEmpty(initialFolder) Then
            ' A folder handed over on the command line is another process's request too: it is
            ' staged and put to the user like one that arrives over WM_COPYDATA.
            Dim startupCommand As CompanionCommand = CompanionCommand.Parse(initialFolder)
            If startupCommand.Kind = CompanionCommandKind.ShareFolder Then
                StageFolder(startupCommand)
                _pendingStartupProposals = True
            ElseIf windowRequested OrElse WantsWindowOnStartup() Then
                ShowMainWindow(Nothing)
            End If
        ElseIf windowRequested OrElse WantsWindowOnStartup() Then
            ShowMainWindow(Nothing)
        ElseIf Not silentLaunch Then
            ShowTrayHint()
        End If
    End Sub

    ''' <summary>The option that decides whether a plain start shows its window. Read fresh
    ''' from the settings store (the window may have flipped it in an earlier session);
    ''' any failure degrades to the default, off.</summary>
    Private Shared Function WantsWindowOnStartup() As Boolean
        Try
            Dim s As New ShareSettings()
            s.Load()
            Return s.OpenWindowOnStartup
        Catch
            Return False
        End Try
    End Function

    ''' <summary>"It did start, it is over here" - a brief balloon shown when a hand-made
    ''' launch keeps its window closed because the startup option is off. Never shown for
    ''' the logon autostart (that one must stay silent) and best-effort: a shell with
    ''' notifications suppressed simply drops it.</summary>
    Private Sub ShowTrayHint()
        Try
            _notifyIcon.ShowBalloonTip(4000,
                ProductNames.ShareManager,
                Localization.T("Менеджер работает в трее. Двойной щелчок по значку открывает окно."),
                ToolTipIcon.Info)
        Catch
        End Try
    End Sub

    Private Function BuildMenu() As ContextMenuStrip
        Dim menu As New ContextMenuStrip()

        ' Primary action (bold) - open the Share Manager window.
        Dim miOpen As New ToolStripMenuItem(Localization.T("Открыть менеджер общего доступа"))
        Dim boldFont As New Font(menu.Font, FontStyle.Bold)
        miOpen.Font = boldFont
        AddHandler menu.Disposed,
            Sub()
                Try
                    boldFont.Dispose()
                Catch
                End Try
            End Sub
        AddHandler miOpen.Click, Sub() ShowMainWindow(Nothing)
        menu.Items.Add(miOpen)

        ' "Share..": open the package wizard so the user first picks WHAT to share and
        ' with WHICH settings, then gets the QR - instead of dumping one fixed QR.
        Dim miShare As New ToolStripMenuItem(Localization.T("Поделиться.."))
        AddHandler miShare.Click, Sub() TrayShare()
        menu.Items.Add(miShare)

        ' Broadcasts for StreamsPlayer
        Dim miBroadcasts As New ToolStripMenuItem(Localization.T("Трансляции для StreamsPlayer.."))
        AddHandler miBroadcasts.Click, Sub() ShowBroadcastsWindow("")
        menu.Items.Add(miBroadcasts)

        ' Current state + local usage counters (last connection, totals, files served).
        Dim miStatus As New ToolStripMenuItem(Localization.T("Текущее состояние.."))
        AddHandler miStatus.Click, Sub() TrayStatus()
        menu.Items.Add(miStatus)

        ' The exchange server (ticket 142 T-6): one item right after Status - the name, then what it is
        ' doing. Present only while the component is installed: its visibility is decided on Opening, like
        ' the viewer item below, and its text is refreshed there off the UI thread (two cheap reads: the
        ' process list and the service state). Nothing is started by showing it; a click opens the window.
        Dim miExchange As New ToolStripMenuItem(ExchangeText.TrayItem(ExchangeRunKind.Stopped)) With {.Visible = False}
        AddHandler miExchange.Click, Sub() ShowMainWindow(Nothing)
        menu.Items.Add(miExchange)
        AddHandler menu.Opening, Sub() RefreshExchangeTrayItem(miExchange)

        ' Online "publish your folders for Android" guide.
        Dim miDesc As New ToolStripMenuItem(Localization.T("Описание.."))
        AddHandler miDesc.Click, Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl)
        menu.Items.Add(miDesc)

        ' Stop the SFTP server (worker process keeps running).
        Dim miStop As New ToolStripMenuItem(Localization.T("Выключить общий доступ"))
        AddHandler miStop.Click, Sub() TrayStop()
        menu.Items.Add(miStop)

        menu.Items.Add(New ToolStripSeparator())

        ' Interface language. It lives in the tray menu as well as in the window because
        ' Companion is usually tray-resident: someone who cannot read the tray menu must
        ' still be able to reach the picker without first finding the window.
        Dim miLang As New ToolStripMenuItem(Localization.T("Язык интерфейса"))
        UiLanguage.AddLanguageItems(miLang.DropDownItems)
        menu.Items.Add(miLang)

        ' Suite cross-link: open the viewer. Hidden when there is no viewer to
        ' open - the Server edition installs the Share Manager alone, so next to THAT
        ' exe there is none. Deciding on Opening rather than here: the menu is built
        ' once, and a viewer installed later would otherwise stay unreachable until
        ' the next restart. A menu item that silently does nothing is worse than one
        ' that is not there.
        Dim miViewer As New ToolStripMenuItem(Localization.TF("Открыть {0}", ProductNames.CompactForLabel))
        AddHandler miViewer.Click, Sub() LaunchViewer()
        menu.Items.Add(miViewer)
        AddHandler menu.Opening, Sub() miViewer.Visible = ViewerExePath().Length > 0

        ' The log the failure the user just saw was written to, sent from here - not from
        ' the viewer's settings, which a Share Manager user may never open (ticket 028 Ф-3,
        ' APP-BEHAVIOUR rule 6). Centred on the main window when it is on screen.
        Dim miSendLog As New ToolStripMenuItem(Localization.T("Отправить журнал автору.."))
        AddHandler miSendLog.Click,
            Async Sub()
                Dim owner As IWin32Window = Nothing
                If _mainWindow IsNot Nothing AndAlso Not _mainWindow.IsDisposed AndAlso _mainWindow.Visible Then owner = _mainWindow
                Await SendLogsFlow.RunAsync(owner)
            End Sub
        menu.Items.Add(miSendLog)

        Dim miExit As New ToolStripMenuItem(Localization.T("Выход"))
        AddHandler miExit.Click, Sub() ExitApplication()
        menu.Items.Add(miExit)

        Return menu
    End Function

    ''' <summary>Shows or hides the exchange item for the component's presence, then refreshes its text from a
    ''' background read. The read never starts, stops or changes anything.</summary>
    Private Sub RefreshExchangeTrayItem(item As ToolStripMenuItem)
        Try
            item.Visible = ExchangeProcess.IsAvailable()
            If Not item.Visible Then Return
            Dim t As Task = RefreshExchangeTrayTextAsync(item)
        Catch ex As Exception
            AppFileLogger.LogException("Tray: exchange item", ex)
        End Try
    End Sub

    Private Async Function RefreshExchangeTrayTextAsync(item As ToolStripMenuItem) As Task
        Dim kind As ExchangeRunKind = Await Task.Run(
            Function()
                Dim serviceExists As Boolean = ExchangeService.Exists()
                Dim state As ServiceControl.ServiceState = If(serviceExists, ExchangeService.QueryState(), ServiceControl.ServiceState.NotInstalled)
                Return ExchangeText.TrayKind(ExchangeProcess.RunningPid() > 0, serviceExists, state)
            End Function)
        If _disposed OrElse item.IsDisposed Then Return
        item.Text = ExchangeText.TrayItem(kind)
    End Function

    ''' <summary>
    ''' The interface language changed. Companion's windows resolve every caption once,
    ''' at construction, so there is nothing to re-label - the window is rebuilt instead,
    ''' and only if it was open. The tray menu is rebuilt too: it is created once in the
    ''' constructor and would otherwise keep the old language until the next start.
    ''' </summary>
    Private Sub OnLanguageChanged(sender As Object, e As EventArgs)
        If _disposed Then Return
        Try
            Dim old As ContextMenuStrip = _notifyIcon.ContextMenuStrip
            _notifyIcon.ContextMenuStrip = BuildMenu()
            ' Released POSTED, not here: the switch may have been chosen from this very
            ' menu (its "Язык интерфейса" submenu), whose click is still being dismissed -
            ' a synchronous Dispose pulls the strip out from under that dispatch and the
            ' dismissal crashes (ObjectDisposedException, the same defect the window
            ' language menu had - UiLanguage.ShowLanguageMenu, companion.log 2026-10-03).
            If old IsNot Nothing Then
                Dim sync As System.Threading.SynchronizationContext = System.Threading.SynchronizationContext.Current
                If sync IsNot Nothing Then
                    sync.Post(
                        Sub()
                            Try : old.Dispose() : Catch : End Try
                        End Sub, Nothing)
                End If
            End If
        Catch
        End Try
        Try
            RefreshTrayState(_statsTimer.Enabled)
        Catch
        End Try
        Try
            If _mainWindow Is Nothing OrElse _mainWindow.IsDisposed Then Return
            Dim wasVisible As Boolean = _mainWindow.Visible
            RemoveHandler _mainWindow.ServerStateChanged, AddressOf RefreshTrayState
            ' Close BEFORE Dispose: the window does its own teardown in FormClosing (its
            ' 10 s stats timer, its window icon). Disposing straight out from under it
            ' skipped that handler, so every language switch left the old window's poll
            ' timer running against the worker and leaked its HICON.
            _mainWindow.Close()
            _mainWindow.Dispose()
            _mainWindow = Nothing
            If wasVisible Then ShowMainWindow(Nothing)
        Catch
        End Try
    End Sub

    ''' <summary>Tray "Поделиться..": open the Share Manager window and route straight
    ''' into the package wizard, where the user chooses which folders + per-folder
    ''' settings to publish and then gets the QR. The window owns the running-server
    ''' check and folder list, so the wizard opens only once status is ready.</summary>
    Private Sub TrayShare()
        ShowMainWindow(Nothing)
        Try
            If _mainWindow IsNot Nothing AndAlso Not _mainWindow.IsDisposed Then _mainWindow.OpenShareWizardFromTray()
        Catch
        End Try
    End Sub

    ''' <summary>Tray "Текущее состояние..": open the status window with the local usage
    ''' counters (last connection, totals, files served). Fetches a fresh status first.</summary>
    Private Async Sub TrayStatus()
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        Try
            Using dlg As New Share_Status_Form(st)
                If _mainWindow IsNot Nothing AndAlso _mainWindow.Visible AndAlso Not _mainWindow.IsDisposed Then
                    dlg.ShowDialog(_mainWindow)
                Else
                    dlg.ShowDialog()
                End If
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>Set while a stats fetch is out. Without it a worker that stopped answering
    ''' collected one fire-and-forget request every 30 s - roughly 2900 a day, each holding a
    ''' thread and a pipe handle - because nothing marked the previous one as unfinished.</summary>
    Private _statsFetchInFlight As Boolean = False

    Private Sub OnStatsTimerTick(sender As Object, e As EventArgs)
        If _statsFetchInFlight Then Return
        Dim t As Task = UpdateTooltipStatsAsync()
    End Sub

    Private Async Sub OnDiskLetterTimerTick(sender As Object, e As EventArgs)
        If _disposed OrElse _diskLetterFetchInFlight Then Return
        _diskLetterFetchInFlight = True
        Try
            ' Poll only while this account has an alias. Never start a worker from a
            ' cleanup timer, nor treat an unavailable worker as a closed disk.
            If Not DiskDriveLetters.HasMappings() Then Return
            Dim status As WorkerStatus = Await ShareController.GetStatusAsync()
            If _disposed OrElse Not DiskSharing.Supported(status) Then Return
            Dim response As WorkerResponse = Await DiskSharing.SendAsync(New WorkerRequest With {.type = "disk-list"})
            If Not _disposed AndAlso response.Ok AndAlso response.Disks IsNot Nothing Then DiskDriveLetters.Reconcile(response.Disks)
        Catch ex As Exception
            AppFileLogger.LogException("Disk drive alias refresh", ex)
        Finally
            _diskLetterFetchInFlight = False
        End Try
    End Sub

    ''' <summary>Enriches the tray tooltip with a concise usage line while serving
    ''' (NotifyIcon.Text is capped ~63 chars). Fire-and-forget; never blocks the UI.</summary>
    Private Async Function UpdateTooltipStatsAsync() As Task
        _statsFetchInFlight = True
        Dim st As WorkerStatus
        Try
            st = Await ShareController.GetStatusAsync()
        Finally
            _statsFetchInFlight = False
        End Try
        If _disposed OrElse _notifyIcon Is Nothing Then Return
        ' If sharing was turned off while this fetch was in flight, RefreshTrayState(False)
        ' already stopped the poll timer and set the idle tooltip - don't clobber it back.
        If Not _statsTimer.Enabled Then Return
        Dim txt As String
        If st IsNot Nothing AndAlso st.Running Then
            txt = Localization.T("Общий доступ включён")
            If st.Stats IsNot Nothing Then
                txt &= Localization.TF(" · подключений: {0}", st.Stats.TotalConnections)
                Dim last As String = Share_Status_Form.ShortTime(st.Stats.LastConnectionAt)
                If last.Length > 0 Then txt &= Localization.TF(" · посл.: {0}", last)
            End If
        Else
            ' The worker is gone (stopped from elsewhere, crashed, or never came up) - or it
            ' was replaced by one on a newer protocol, which says so once. Stop
            ' polling it: this tick fires every 30 s for as long as Companion sits in the
            ' tray - weeks - and each one was a pointless pipe round-trip against something
            ' that is not there. RefreshTrayState(False) sets the idle tooltip and menu too.
            If st Is Nothing Then ShowRefusalOnce()
            RefreshTrayState(False)
            Return
        End If
        If txt.Length > 63 Then txt = txt.Substring(0, 63)
        Try
            _notifyIcon.Text = txt
        Catch
        End Try
    End Function

    ''' <summary>Tray "Выключить общий доступ": stop the SFTP server, then refresh the tray.</summary>
    Private Async Sub TrayStop()
        Try
            Await ShareController.StopServerAsync()
        Catch
        End Try
        RefreshTrayState(False)
        ' An open window kept "running" in its header after this (ticket 054 H-12).
        Try
            If _mainWindow IsNot Nothing AndAlso Not _mainWindow.IsDisposed Then
                Dim t As Task = _mainWindow.RefreshFromWorkerAsync()
            End If
        Catch
        End Try
    End Sub

    ''' <summary>Shows (creating/reusing) the main window and brings it to front. A
    ''' non-empty <paramref name="initialFolder"/> requests the "share this folder"
    ''' flow - handed to the constructor for a fresh window, routed into the live one
    ''' when a window is already up.</summary>
    Private Sub ShowMainWindow(initialFolder As String)
        Try
            Dim reused As Boolean = _mainWindow IsNot Nothing AndAlso Not _mainWindow.IsDisposed
            If Not reused Then
                _mainWindow = New MainWindow(initialFolder)
                AddHandler _mainWindow.ServerStateChanged, AddressOf RefreshTrayState
            End If
            If Not _mainWindow.Visible Then _mainWindow.Show()
            If _mainWindow.WindowState = FormWindowState.Minimized Then _mainWindow.WindowState = FormWindowState.Normal
            _mainWindow.Activate()
            _mainWindow.BringToFront()
            ForceToForeground(_mainWindow)

            ' The constructor only sees initialFolder for a NEW window. Reusing one used to
            ' throw the folder away silently - see MainWindow.ShareFolderFromWakeAsync.
            If reused AndAlso Not String.IsNullOrEmpty(initialFolder) Then
                Dim live As MainWindow = _mainWindow
                Dim folder As String = initialFolder
                ' Posted so the wake handler returns before the share flow (which awaits the
                ' worker and can open the wizard) starts.
                live.BeginInvoke(New Action(Sub()
                                                If live.IsDisposed Then Return
                                                Dim t As Task = live.ShareFolderFromWakeAsync(folder)
                                            End Sub))
            End If
        Catch
        End Try
    End Sub

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    ''' <summary>Pulls a just-shown window truly in front. Companion is a background
    ''' (tray) process, so <c>Show</c>/<c>Activate</c> alone often lands the window
    ''' BEHIND whoever woke us (the viewer). The viewer grants us the foreground right
    ''' (AllowSetForegroundWindow) right before the wake, so SetForegroundWindow now
    ''' succeeds; the brief TopMost toggle is a belt-and-suspenders fallback for when
    ''' that grant window was missed.</summary>
    Private Shared Sub ForceToForeground(f As Form)
        Try
            SetForegroundWindow(f.Handle)
            If GetForegroundWindow() <> f.Handle Then
                f.TopMost = True
                f.TopMost = False
                f.Activate()
            End If
        Catch
        End Try
    End Sub

    Private Sub ResumeShareIfEnabled()
        If Not ServerFeatures.IsEnabled() OrElse Not WorkerProcess.IsAvailable() Then Return
        Dim s As New ShareSettings()
        Try
            s.Load()
        Catch
        End Try
        If Not s.WorkerEverStarted Then Return
        ' Fire-and-forget: bring the worker up and reconcile its enforced readOnly
        ' with what the .fmscfg advertises, then refresh the tray.
        Dim t As Task = ResumeAsync()
    End Sub

    Private Async Function ResumeAsync() As Task
        Dim running As Boolean = False
        Try
            Dim st As WorkerStatus = Await ShareController.EnsureRunningReconciledAsync()
            running = st IsNot Nothing AndAlso st.Running
            If st Is Nothing Then ShowRefusalOnce()
        Catch
        End Try
        RefreshTrayState(running)
    End Function

    ''' <summary>The worker version this tray already told the user about, so a poll does not
    ''' repeat the balloon every tick. 0 = none shown.</summary>
    Private _refusalShownFor As Integer = 0

    ''' <summary>
    ''' WORKER-IPC section 6 rule 3 reaches the tray: a resident worker on a newer protocol
    ''' answers every request with something this app will not read, so sharing looks off for
    ''' no visible reason - the one failure the silent logon autostart must not keep quiet
    ''' about, because nothing else will ever say it. Once per worker version.
    ''' </summary>
    Private Sub ShowRefusalOnce()
        Dim refusal As WorkerVersionRefusedException = WorkerProcess.LastRefusal
        If refusal Is Nothing Then
            ShowProblemOnce()
            Return
        End If
        If refusal.WorkerVersion = _refusalShownFor Then Return
        _refusalShownFor = refusal.WorkerVersion
        Try
            _notifyIcon.ShowBalloonTip(8000,
                ProductNames.ShareManager,
                ShareController.RefusalMessage(refusal),
                ToolTipIcon.Warning)
        Catch
        End Try
    End Sub

    ''' <summary>The exchange problem this tray already told the user about. None = none shown.</summary>
    Private _problemShown As WorkerExchangeProblem = WorkerExchangeProblem.None

    ''' <summary>The same once-only balloon for a pipe that is held but unusable - above all a
    ''' pipe held by a process that is not our worker, which a silent logon start would
    ''' otherwise leave looking like "sharing is simply off" (ticket 054 H-10, H-26).</summary>
    Private Sub ShowProblemOnce()
        Dim problem As WorkerExchangeProblem = WorkerProcess.LastProblem
        If problem = WorkerExchangeProblem.None OrElse problem = _problemShown Then Return
        _problemShown = problem
        Try
            _notifyIcon.ShowBalloonTip(8000,
                ProductNames.ShareManager,
                ShareController.ProblemMessage(problem),
                ToolTipIcon.Warning)
        Catch
        End Try
    End Sub

    ''' <summary>Updates the tray tooltip to the current server state. Takes the
    ''' running flag directly (never re-fetches on the UI thread - a blocking
    ''' await there can deadlock).</summary>
    Private Sub RefreshTrayState(running As Boolean)
        Try
            If _notifyIcon Is Nothing Then Return
            _notifyIcon.Text = If(running,
                Localization.T("Общий доступ включён"),
                ProductNames.ShareManager)
        Catch
        End Try
        ' While serving, keep the tooltip's usage counters fresh (immediate + every 30s).
        Try
            If running Then
                Dim t As Task = UpdateTooltipStatsAsync()
                _statsTimer.Start()
            Else
                _statsTimer.Stop()
            End If
        Catch
        End Try
    End Sub

    ''' <summary>Folders other processes asked this one to share. Never shared from here: each
    ''' goes to the user first (108 H01).</summary>
    Private ReadOnly _proposals As New FolderProposalQueue(AddressOf MonotonicClock.NowMs)
    Private _askingUser As Boolean

    ''' <summary>
    ''' Runs on the UI thread, AFTER the receiver has validated transport and command and
    ''' acknowledged. A show-window request shows the window; a folder only ever becomes a
    ''' proposal, answered by the user before AddShareRow/ApplySharedFolders can run.
    ''' </summary>
    Private Sub OnPayloadReceived(command As CompanionCommand)
        Select Case command.Kind
            Case CompanionCommandKind.ShowWindow
                ShowMainWindow(Nothing)
            Case CompanionCommandKind.PlainStart
                ' The exe was run again with nothing attached - same rule as a first plain
                ' start, and the running instance is the only one that can show the hint.
                If WantsWindowOnStartup() Then ShowMainWindow(Nothing) Else ShowTrayHint()
            Case CompanionCommandKind.ShareFolder
                ' Already staged by the receiver (the verdict was part of the acknowledgement).
                If IsAlreadyShared(command.Folder) Then
                    ShowMainWindow(Nothing)
                    _mainWindow.SelectSharedFolder(command.Folder)
                    Return
                End If
                BeginInvokeProposals()
            Case CompanionCommandKind.Broadcast
                ShowBroadcastsWindow(command.TargetPath)
        End Select
    End Sub

    Private Sub ShowBroadcastsWindow(targetPath As String)
        Try
            If Not ServerFeatures.IsEnabled() OrElse Not WorkerProcess.IsAvailable() Then
                ShowMainWindow(Nothing)
                Return
            End If
            Using dlg As New Share_Broadcasts_Form(targetPath)
                If _mainWindow IsNot Nothing AndAlso _mainWindow.Visible AndAlso Not _mainWindow.IsDisposed Then
                    dlg.ShowDialog(_mainWindow)
                Else
                    dlg.ShowDialog()
                End If
            End Using
        Catch ex As Exception
            AppFileLogger.LogException("ShowBroadcastsWindow", ex)
        End Try
    End Sub

    ''' <summary>The message loop is running: a folder handed over on the command line is put to
    ''' the user now, not from inside the constructor.</summary>
    Private Sub OnFirstIdle(sender As Object, e As EventArgs)
        RemoveHandler Application.Idle, AddressOf OnFirstIdle
        If _pendingStartupProposals Then
            _pendingStartupProposals = False
            BeginInvokeProposals()
        End If
    End Sub
    ''' <summary>The receiver's decision for a validated folder request, taken synchronously so
    ''' that the acknowledgement reflects it: queued/duplicate/already-shared = accepted.</summary>
    Private Function StageFolder(command As CompanionCommand) As Boolean
        If _disposed Then Return False
        Dim verdict As ProposalVerdict = _proposals.Offer(command.Folder, AddressOf IsAlreadyShared)
        AppFileLogger.WriteLine("Companion: folder proposal " & verdict.ToString())
        Return verdict <> ProposalVerdict.Full
    End Function

    Private Function IsAlreadyShared(canonicalFolder As String) As Boolean
        Try
            If _mainWindow Is Nothing OrElse _mainWindow.IsDisposed Then Return False
            Return _mainWindow.HasShareRow(canonicalFolder)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Puts queued proposals to the user one at a time. Posted, so the wake handler has
    ''' already returned its acknowledgement and the dialog never runs inside WndProc.</summary>
    Friend Sub BeginInvokeProposals()
        If _disposed OrElse _askingUser Then Return
        _askingUser = True
        Dim t As Task = AskUserAboutProposalsAsync()
    End Sub

    Private Async Function AskUserAboutProposalsAsync() As Task
        Try
            Await Task.Yield()
            Dim folder As String = _proposals.TakeNext()
            While folder.Length > 0 AndAlso Not _disposed
                Try
                    Await PutProposalToUserAsync(folder)
                Finally
                    _proposals.Complete(folder)
                End Try
                folder = _proposals.TakeNext()
            End While
        Catch ex As Exception
            AppFileLogger.LogException("Share Manager: putting a folder request to the user", ex)
        Finally
            _askingUser = False
        End Try
    End Function

    Private Async Function PutProposalToUserAsync(folder As String) As Task
        ' The existence check can block on a share that stopped answering: never on the UI thread.
        Dim exists As Boolean = Await Task.Run(Function()
                                                   Try
                                                       Return IO.Directory.Exists(folder)
                                                   Catch
                                                       Return False
                                                   End Try
                                               End Function)
        If _disposed Then Return
        If Not exists Then
            AppFileLogger.WriteLine("Share Manager: a requested folder does not exist - dropped")
            Return
        End If
        If Not ServerFeatures.IsEnabled() OrElse Not WorkerProcess.IsAvailable() Then
            ' Sharing is not available here; the window explains that. Nothing is queued.
            ShowMainWindow(Nothing)
            Return
        End If
        Dim answer As DialogResult = MessageBox.Show(
            Localization.TF("Другая программа просит открыть папку на телефоне:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
                            "Телефоны с доступом к этому компьютеру смогут просматривать её файлы. Открыть папку?", folder),
            ProductNames.ShareManager, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)
        If answer <> DialogResult.OK Then Return
        If _disposed Then Return
        ShowMainWindow(Nothing)
        If _disposed OrElse _mainWindow Is Nothing OrElse _mainWindow.IsDisposed Then Return
        Await _mainWindow.ShareFolderFromWakeAsync(folder)
    End Function
    ''' <summary>
    ''' The viewer exe to open from the tray, or "" when this machine has none.
    '''
    ''' Next to us FIRST - that is the User edition, where both programs share one
    ''' folder. The Server edition installs the Share Manager on its own, into its own
    ''' directory, so there is no viewer beside it: the lookup then falls back to the
    ''' User edition's recorded install location. Without that fallback the tray item
    ''' did nothing at all on a Server machine that has the viewer installed normally -
    ''' silently, because the old code just skipped a missing file.
    ''' </summary>
    Private Function ViewerExePath() As String
        Try
            Dim beside As String = FirstViewerIn(IO.Path.GetDirectoryName(Application.ExecutablePath))
            If beside.Length > 0 Then Return beside
        Catch
        End Try

        ' The User edition's frozen Inno AppId - the same key its own installer writes
        ' and the Server installer reads to detect it. Per-user installs land in HKCU,
        ' machine-wide ones in HKLM (and under Wow6432Node on some upgrade paths).
        Const userEditionKey As String = "Software\Microsoft\Windows\CurrentVersion\Uninstall\{7371E7F1-B8A8-4786-8173-5F5B2B6E6AC9}_is1"
        For Each hive As Microsoft.Win32.RegistryKey In {Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine}
            For Each sub_key As String In {userEditionKey, userEditionKey.Replace("Software\", "Software\WOW6432Node\")}
                Try
                    Using k As Microsoft.Win32.RegistryKey = hive.OpenSubKey(sub_key, False)
                        If k Is Nothing Then Continue For
                        Dim loc As String = TryCast(k.GetValue("InstallLocation"), String)
                        If String.IsNullOrWhiteSpace(loc) Then Continue For
                        Dim found As String = FirstViewerIn(loc.Trim().TrimEnd(IO.Path.DirectorySeparatorChar))
                        If found.Length > 0 Then Return found
                    End Using
                Catch
                End Try
            Next
        Next
        Return ""
    End Function

    ''' <summary>The mainline exe in that folder, else its 32-bit sibling, else "".</summary>
    Private Function FirstViewerIn(dir As String) As String
        If String.IsNullOrEmpty(dir) Then Return ""
        For Each name As String In {"FastMediaSorter_LITE.exe", "FastMediaSorter_x86.exe"}
            Try
                Dim candidate As String = IO.Path.Combine(dir, name)
                If IO.File.Exists(candidate) Then Return candidate
            Catch
            End Try
        Next
        Return ""
    End Function

    Private Sub LaunchViewer()
        Try
            Dim exe As String = ViewerExePath()
            If exe.Length > 0 Then
                Process.Start(New ProcessStartInfo(exe) With {.UseShellExecute = True})
            End If
        Catch
        End Try
    End Sub

    Private Sub ExitApplication()
        ' Close the window while the message loop still runs, so its FormClosing writes
        ' the geometry; Dispose below repeats the Close as a backstop.
        Try
            If _mainWindow IsNot Nothing AndAlso Not _mainWindow.IsDisposed Then _mainWindow.Close()
        Catch
        End Try
        ExitThread()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not _disposed Then
            _disposed = True
            Try
                _diskLetterTimer.Stop()
                _diskLetterTimer.Dispose()
                DiskDriveLetters.Reconcile(Array.Empty(Of SharedDisk)())
            Catch ex As Exception
                AppFileLogger.LogException("Disk drive alias cleanup at exit", ex)
            End Try
            Try
                RemoveHandler UiLanguage.Changed, AddressOf OnLanguageChanged
            Catch
            End Try
            Try
                _statsTimer.Stop()
                _statsTimer.Dispose()
            Catch
            End Try
            ' A server THIS session started is stopped with it, unless the person asked to keep it running
            ' (Exchange_KeepRunningOnExit, default off). One that was already running is never touched.
            Try
                Dim exSettings As New ExchangeSettings()
                exSettings.Load()
                ExchangeProcess.StopStartedOnExit(exSettings.KeepRunningOnExit)
            Catch e As Exception
                AppFileLogger.LogException("Exchange: stopping the server at exit", e)
            End Try
            Try
                ' Close BEFORE Dispose, as the language switch above does: FormClosing is
                ' where the window writes its geometry, and a bare Dispose skipped it, so
                ' Exit from the tray forgot where the window was (spec 028 phase 1).
                If _mainWindow IsNot Nothing AndAlso Not _mainWindow.IsDisposed Then
                    _mainWindow.Close()
                    _mainWindow.Dispose()
                End If
            Catch
            End Try
            Try
                _notifyIcon.Visible = False
                _notifyIcon.Dispose()
            Catch
            End Try
            Try
                _messageWindow.DestroyHandle()
            Catch
            End Try
            ' Icon.FromHandle does not own the HICON - dispose the Icon AND free the
            ' handle (ShareIcons.FreeIcon does both).
            ShareIcons.FreeIcon(_trayIcon, _trayHIcon)
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>
    ''' Hidden top-level window that receives WM_COPYDATA from a second instance /
    ''' from the viewer. Given a fixed caption so <c>FindWindowW</c> can locate it; never
    ''' shown, so it stays out of the taskbar and off-screen.
    ''' </summary>
    Friend NotInheritable Class MessageWindow
        Inherits NativeWindow

        ''' <summary>A request that passed every check and was acknowledged.</summary>
        Public Event CommandAccepted(command As CompanionCommand)

        ''' <summary>Stages a validated folder request (the receiver's synchronous half of the
        ''' decision); False means the queue is full and the sender gets ack 0.</summary>
        Public Property StageFolder As Func(Of CompanionCommand, Boolean)

        Public Sub New()
            Dim cp As New CreateParams() With {.Caption = Program.MessageWindowTitle}
            CreateHandle(cp)
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = Program.WM_COPYDATA Then
                If m.LParam = IntPtr.Zero Then
                    m.Result = IntPtr.Zero
                    Return
                End If
                Try
                    Dim cds = CType(Marshal.PtrToStructure(m.LParam, GetType(Program.COPYDATASTRUCT)), Program.COPYDATASTRUCT)
                    Dim command As CompanionCommand = Nothing
                    ' Transport and command are validated BEFORE anything is acknowledged. The
                    ' acknowledgement means "accepted for handling" - it says nothing about the
                    ' folder being shared, which only the user can decide.
                    Dim ack As Integer = ActivationReceiver.Evaluate(cds.dwData, cds.cbData, cds.lpData, StageFolder, command)
                    m.Result = New IntPtr(ack)
                    If ack = Program.Handled_Result AndAlso command IsNot Nothing Then
                        RaiseEvent CommandAccepted(command)
                    End If
                Catch ex As Exception
                    AppFileLogger.LogException("Companion WM_COPYDATA processing", ex)
                    m.Result = New IntPtr(0)
                End Try
                Return
            End If
            MyBase.WndProc(m)
        End Sub
    End Class

End Class
