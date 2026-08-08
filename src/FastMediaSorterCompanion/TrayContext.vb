Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Tray-resident application host. Companion is ALWAYS tray-resident (its normal
''' state, not the close-to-tray crutch LITE needed) - so a bare launch shows no
''' window, just the notify icon and a hidden receiver window for the
''' LITE -&gt; Companion wake handshake (WM_COPYDATA). Owns the single reusable
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
    Private ReadOnly _statsTimer As Timer   ' keeps the tooltip's usage counters reasonably fresh while serving

    ''' <param name="windowRequested">The launch itself asked for the window (LITE's Share
    ''' Manager button, --show). Such a request always wins over the startup option.</param>
    ''' <param name="silentLaunch">The logon autostart (--tray). Suppresses even the
    ''' "I am in the tray" hint, so a boot stays completely quiet.</param>
    Friend Sub New(initialFolder As String, Optional windowRequested As Boolean = False, Optional silentLaunch As Boolean = False)
        _trayIcon = ShareIcons.CreateIcon(_trayHIcon)

        _notifyIcon = New NotifyIcon() With {
            .Icon = _trayIcon,
            .Text = "Fast Media Sorter: Share Manager",
            .Visible = True
        }
        _notifyIcon.ContextMenuStrip = BuildMenu()
        AddHandler _notifyIcon.DoubleClick, Sub() ShowMainWindow(Nothing)
        AddHandler UiLanguage.Changed, AddressOf OnLanguageChanged

        _messageWindow = New MessageWindow()
        AddHandler _messageWindow.PayloadReceived, AddressOf OnPayloadReceived

        _statsTimer = New Timer With {.Interval = 30000}
        AddHandler _statsTimer.Tick, AddressOf OnStatsTimerTick

        ' Resume sharing silently if it was ever started - covers the "dedicated
        ' server" scenario where Companion autostarts into the tray with no window.
        ResumeShareIfEnabled()

        ' Who gets a window. An EXPLICIT gesture always does: a folder argument ("share
        ' this folder", jumps to the package wizard) or windowRequested (LITE's Share
        ' Manager button / --show). Everything else - logon autostart, a double-click on
        ' the exe, a script - is a plain start and obeys the "Открывать окно менеджера
        ' при запуске" option, which is OFF by default: the program then lives in the
        ' tray exactly as it does at logon (spec §4.5.1 / §4.3). A plain start that is
        ' not the silent logon one leaves a short tray hint instead, so a user who
        ' double-clicked the exe is not left wondering whether anything started.
        If Not String.IsNullOrEmpty(initialFolder) Then
            ShowMainWindow(initialFolder)
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
                "Fast Media Sorter: Share Manager",
                Localization.T("Менеджер работает в трее. Двойной щелчок по значку открывает окно."),
                ToolTipIcon.Info)
        Catch
        End Try
    End Sub

    Private Function BuildMenu() As ContextMenuStrip
        Dim menu As New ContextMenuStrip()

        ' Primary action (bold) - open the Share Manager window.
        Dim miOpen As New ToolStripMenuItem(Localization.T("Открыть менеджер общего доступа"))
        miOpen.Font = New Font(menu.Font, FontStyle.Bold)
        AddHandler miOpen.Click, Sub() ShowMainWindow(Nothing)
        menu.Items.Add(miOpen)

        ' "Share..": open the package wizard so the user first picks WHAT to share and
        ' with WHICH settings, then gets the QR - instead of dumping one fixed QR.
        Dim miShare As New ToolStripMenuItem(Localization.T("Поделиться.."))
        AddHandler miShare.Click, Sub() TrayShare()
        menu.Items.Add(miShare)

        ' Current state + local usage counters (last connection, totals, files served).
        Dim miStatus As New ToolStripMenuItem(Localization.T("Текущее состояние.."))
        AddHandler miStatus.Click, Sub() TrayStatus()
        menu.Items.Add(miStatus)

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

        ' Suite cross-link: open the LITE viewer. Hidden when there is no viewer to
        ' open - the Server edition installs the Share Manager alone, so next to THAT
        ' exe there is none. Deciding on Opening rather than here: the menu is built
        ' once, and a viewer installed later would otherwise stay unreachable until
        ' the next restart. A menu item that silently does nothing is worse than one
        ' that is not there.
        Dim miViewer As New ToolStripMenuItem(Localization.T("Открыть Fast Media Sorter"))
        AddHandler miViewer.Click, Sub() LaunchViewer()
        menu.Items.Add(miViewer)
        AddHandler menu.Opening, Sub() miViewer.Visible = ViewerExePath().Length > 0

        Dim miExit As New ToolStripMenuItem(Localization.T("Выход"))
        AddHandler miExit.Click, Sub() ExitApplication()
        menu.Items.Add(miExit)

        Return menu
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
            If old IsNot Nothing Then old.Dispose()
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
            ' The worker is gone (stopped from elsewhere, crashed, or never came up). Stop
            ' polling it: this tick fires every 30 s for as long as Companion sits in the
            ' tray - weeks - and each one was a pointless pipe round-trip against something
            ' that is not there. RefreshTrayState(False) sets the idle tooltip and menu too.
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
    ''' BEHIND whoever woke us (LITE). LITE grants us the foreground right
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
        Catch
        End Try
        RefreshTrayState(running)
    End Function

    ''' <summary>Updates the tray tooltip to the current server state. Takes the
    ''' running flag directly (never re-fetches on the UI thread - a blocking
    ''' await there can deadlock).</summary>
    Private Sub RefreshTrayState(running As Boolean)
        Try
            If _notifyIcon Is Nothing Then Return
            _notifyIcon.Text = If(running,
                Localization.T("Общий доступ включён"),
                "Fast Media Sorter: Share Manager")
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

    Private Sub OnPayloadReceived(payload As String)
        If String.Equals(payload, Program.ShowWindowCommand, StringComparison.Ordinal) Then
            ShowMainWindow(Nothing)
        ElseIf String.Equals(payload, Program.PlainStartCommand, StringComparison.Ordinal) Then
            ' The exe was run again with nothing attached - same rule as a first plain
            ' start, and the running instance is the only one that can show the hint.
            If WantsWindowOnStartup() Then ShowMainWindow(Nothing) Else ShowTrayHint()
        Else
            ShowMainWindow(payload)
        End If
    End Sub

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
        ExitThread()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not _disposed Then
            _disposed = True
            Try
                _statsTimer.Stop()
                _statsTimer.Dispose()
            Catch
            End Try
            Try
                If _mainWindow IsNot Nothing AndAlso Not _mainWindow.IsDisposed Then _mainWindow.Dispose()
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
    ''' from LITE. Given a fixed caption so <c>FindWindowW</c> can locate it; never
    ''' shown, so it stays out of the taskbar and off-screen.
    ''' </summary>
    Private NotInheritable Class MessageWindow
        Inherits NativeWindow

        Public Event PayloadReceived(payload As String)

        Public Sub New()
            Dim cp As New CreateParams() With {.Caption = Program.MessageWindowTitle}
            CreateHandle(cp)
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = Program.WM_COPYDATA Then
                Try
                    Dim cds = CType(Marshal.PtrToStructure(m.LParam, GetType(Program.COPYDATASTRUCT)), Program.COPYDATASTRUCT)
                    If cds.cbData > 0 AndAlso cds.lpData <> IntPtr.Zero Then
                        Dim buf(cds.cbData - 1) As Byte
                        Marshal.Copy(cds.lpData, buf, 0, cds.cbData)
                        RaiseEvent PayloadReceived(Encoding.UTF8.GetString(buf))
                    End If
                Catch
                End Try
                m.Result = New IntPtr(1)
                Return
            End If
            MyBase.WndProc(m)
        End Sub
    End Class

End Class
