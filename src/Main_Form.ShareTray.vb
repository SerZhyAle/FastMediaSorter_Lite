Option Strict On

Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' System-tray indicator for the Android Folder Share service. The SFTP "service"
''' is the bundled headless Go worker (companion\fms-share-worker.exe) that runs as
''' a separate process; it has no UI of its own, so LITE (its only controller)
''' surfaces it in the notification area whenever it is actually serving.
'''
''' The icon is a recognizable blue four-way arrow ("shared in all directions"),
''' drawn in code (no asset file) so it scales cleanly and stands out on both light
''' and dark taskbars. Its context menu mirrors the user's request:
'''   * Описание / Description -> opens the online "publish your folders for Android"
'''     guide (ShareGuide.SiteGuideUrl) in the default browser.
'''   * Настроить / Configure  -> brings LITE forward and opens Settings on the
'''     Share tab (ShowShareSettings()).
'''   * Выключить / Turn off    -> stops the SFTP server (ShareController.StopServerAsync);
'''     the icon then hides on the next refresh.
'''
''' Visibility tracks the worker's real Running state via a light poll (a cheap
''' process-existence gate first, only then a pipe status query) plus an instant
''' RefreshShareTray() nudge from the Share tab / wizard after every start/stop.
''' </summary>
Partial Public Class Main_Form

    Private _shareTrayIcon As NotifyIcon
    Private _shareTrayImage As Icon
    Private _shareTrayHIcon As IntPtr = IntPtr.Zero
    Private _trayMenu As ContextMenuStrip
    Private _trayMiOpen As ToolStripMenuItem
    Private _trayMiQr As ToolStripMenuItem
    Private _trayMiDesc As ToolStripMenuItem
    Private _trayMiConfig As ToolStripMenuItem
    Private _trayMiStop As ToolStripMenuItem
    Private _trayMiExit As ToolStripMenuItem
    Private _shareTrayTimer As System.Windows.Forms.Timer
    Private _trayPolling As Boolean
    Private _trayShutdown As Boolean

    ' Close-to-tray state. While a share is active, closing the main window hides
    ' LITE to the tray (keeps the worker controllable) instead of quitting; the app
    ' really exits only on an explicit "Выход"/Quit, or once the share is stopped.
    Private _residentInTray As Boolean       ' window hidden, process kept alive for the tray
    Private _forceExit As Boolean            ' a real quit was requested - bypass the close-to-tray guard
    Private _lastKnownSharing As Boolean     ' last polled Running state (sync signal for FormClosing)
    Private _trayResidentBalloonShown As Boolean

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function DestroyIcon(handle As IntPtr) As Boolean
    End Function

    ''' <summary>Starts the background poll that shows/hides the tray indicator.
    ''' Called once from Form1_Load (after InitializeShareEntryPoints).</summary>
    Friend Sub InitializeShareTray()
        If _shareTrayTimer IsNot Nothing Then Return
        _shareTrayTimer = New System.Windows.Forms.Timer With {.Interval = 5000}
        AddHandler _shareTrayTimer.Tick, AddressOf ShareTrayTimer_Tick
        _shareTrayTimer.Start()
        ' Probe immediately so the icon appears without waiting a full interval when
        ' sharing is already running (e.g. autostart, or LITE reopened while serving).
        RefreshShareTray()
    End Sub

    Private Sub ShareTrayTimer_Tick(sender As Object, e As EventArgs)
        RefreshShareTray()
    End Sub

    ''' <summary>Resumes Android Folder Share on launch if the user has used the
    ''' feature before - without this, the worker (and its SFTP server) only ever
    ''' started once the user opened Settings -> Share or the wizard, even though
    ''' the worker itself auto-restores any previously-shared folders and their
    ''' server the moment it is spawned. Called once from Form1_Load, right after
    ''' InitializeShareTray (so the poll timer is already running and will pick up
    ''' the resumed state on its next tick). Best-effort and fire-and-forget: a
    ''' failure here just leaves sharing off, exactly as it was before this ran.</summary>
    Friend Sub ResumeShareIfEnabled()
        ' Never spawn the worker when server features are not consented to - the app
        ' must stay a pure viewer/sorter until the user opts in (§3.3).
        If Not ServerFeatures.IsEnabled() Then Return
        Dim settings As New ShareSettings()
        settings.Load()
        If Not settings.WorkerEverStarted Then Return
        ResumeShareAsync()
    End Sub

    Private Async Sub ResumeShareAsync()
        Try
            ' Reconcile: the worker autostarts its SFTP server from its own
            ' shares.json, whose per-root readOnly can lag behind what the .fmscfg
            ' now advertises (writable-by-default). Re-sync it at launch so the phone
            ' is never told a folder is writable while the server serves it read-only.
            Await ShareController.EnsureRunningReconciledAsync()
        Catch
        End Try
    End Sub

    ''' <summary>Re-checks the worker and updates the tray icon at once. Safe to call
    ''' from the Share tab / wizard after a start/stop for an instant update; also
    ''' called on the poll timer. No-op re-entrancy while a probe is in flight.</summary>
    Public Sub RefreshShareTray()
        If _trayShutdown OrElse _trayPolling Then Return
        PollShareTrayAsync()
    End Sub

    Private Async Sub PollShareTrayAsync()
        If _trayShutdown OrElse _trayPolling Then Return
        _trayPolling = True
        Try
            Dim state As ShareTrayState = Await Task.Run(Function() ProbeSharing())
            If _trayShutdown Then Return
            ApplyTrayState(state)
        Catch
            ' A transient probe failure must never crash the UI; leave the icon as-is.
        Finally
            _trayPolling = False
        End Try
    End Sub

    ''' <summary>Off-UI-thread probe: is the SFTP server actually serving, and at what
    ''' LAN address? A cheap process-existence check gates the (slower) pipe query so
    ''' the common "not sharing" case costs almost nothing.</summary>
    Private Shared Function ProbeSharing() As ShareTrayState
        Dim s As New ShareTrayState()
        Try
            ' Viewer-only mode: the worker is never spawned, so there is nothing to
            ' probe and no tray icon should ever appear.
            If Not ServerFeatures.IsEnabled() Then Return s
            If Not WorkerProcess.IsAvailable() Then Return s

            Dim procs As Process() = Process.GetProcessesByName(WorkerProcess.ProcessName)
            Dim anyProc As Boolean = procs IsNot Nothing AndAlso procs.Length > 0
            If procs IsNot Nothing Then
                For Each p As Process In procs
                    Try : p.Dispose() : Catch : End Try
                Next
            End If
            If Not anyProc Then Return s  ' worker not running -> nothing is shared

            Dim resp As WorkerResponse = WorkerProcess.TryGetStatus(1500)
            Dim st As WorkerStatus = If(resp IsNot Nothing, resp.Status, Nothing)
            If st IsNot Nothing AndAlso st.Running Then
                s.Running = True
                If st.Reachability IsNot Nothing AndAlso Not String.IsNullOrEmpty(st.Reachability.LanAddress) Then
                    s.Address = st.Reachability.LanAddress & ":" & st.ListenPort.ToString()
                End If
            End If
        Catch
        End Try
        Return s
    End Function

    Private Sub ApplyTrayState(state As ShareTrayState)
        If state Is Nothing Then Return
        If _lastKnownSharing <> state.Running Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " tray: sharing state -> " & state.Running.ToString() & " addr=" & If(state.Address, ""))
        End If
        _lastKnownSharing = state.Running

        If state.Running Then
            EnsureTrayIcon()
            If _shareTrayIcon Is Nothing Then Return

            Dim rus As Boolean = Is_Russian_Language
            Dim tip As String = If(rus, "Fast Media Sorter - общий доступ включён", "Fast Media Sorter - folder sharing on")
            If Not String.IsNullOrEmpty(state.Address) Then tip = If(rus, "Fast Media Sorter - доступ: ", "Fast Media Sorter - sharing: ") & state.Address
            _shareTrayIcon.Text = TruncateTip(tip)

            If Not _shareTrayIcon.Visible Then
                _shareTrayIcon.Visible = True
                Try
                    _shareTrayIcon.BalloonTipTitle = If(rus, "Общий доступ к папкам включён", "Folder sharing is on")
                    _shareTrayIcon.BalloonTipText = If(String.IsNullOrEmpty(state.Address),
                        If(rus, "Папки этого ПК доступны с телефона Android.", "This PC's folders are reachable from your Android phone."),
                        (If(rus, "Адрес: ", "Address: ")) & state.Address)
                    _shareTrayIcon.ShowBalloonTip(3000)
                Catch
                End Try
            End If
        Else
            If _shareTrayIcon IsNot Nothing AndAlso _shareTrayIcon.Visible Then _shareTrayIcon.Visible = False
            ' We only stayed alive (window hidden) to keep the share controllable; the
            ' share is gone now, so quit rather than leave an invisible process behind.
            If _residentInTray Then
                _residentInTray = False
                DoRealExit()
            End If
        End If
    End Sub

    ''' <summary>Creates the NotifyIcon + its menu on first need (icon still hidden).</summary>
    Private Sub EnsureTrayIcon()
        If _shareTrayIcon IsNot Nothing Then Return

        _trayMenu = New ContextMenuStrip()
        _trayMiOpen = New ToolStripMenuItem()
        _trayMiQr = New ToolStripMenuItem()
        _trayMiDesc = New ToolStripMenuItem()
        _trayMiConfig = New ToolStripMenuItem()
        _trayMiStop = New ToolStripMenuItem()
        _trayMiExit = New ToolStripMenuItem()
        AddHandler _trayMiOpen.Click, Sub() TrayOpen()
        AddHandler _trayMiQr.Click, Sub() TrayShowQr()
        AddHandler _trayMiDesc.Click, Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl)
        AddHandler _trayMiConfig.Click, Sub() TrayConfigure()
        AddHandler _trayMiStop.Click, Sub() TrayStop()
        AddHandler _trayMiExit.Click, Sub() TrayExit()
        _trayMenu.Items.Add(_trayMiOpen)
        _trayMenu.Items.Add(_trayMiQr)
        _trayMenu.Items.Add(_trayMiDesc)
        _trayMenu.Items.Add(_trayMiConfig)
        _trayMenu.Items.Add(_trayMiStop)
        _trayMenu.Items.Add(New ToolStripSeparator())
        _trayMenu.Items.Add(_trayMiExit)

        ' Bold "Open" as the primary action -> matches the double-click default.
        _trayMiOpen.Font = New Font(_trayMenu.Font, FontStyle.Bold)

        _shareTrayImage = BuildShareTrayIcon()

        _shareTrayIcon = New NotifyIcon With {
            .Icon = _shareTrayImage,
            .Text = "Fast Media Sorter",
            .ContextMenuStrip = _trayMenu,
            .Visible = False
        }
        AddHandler _shareTrayIcon.DoubleClick, Sub() TrayOpen()

        LocalizeShareTray()
    End Sub

    ''' <summary>Re-localizes the tray menu. Called from LocalizeShareEntryPoints (which
    ''' LngCh drives), so a language switch updates the menu live.</summary>
    Friend Sub LocalizeShareTray()
        If _trayMenu Is Nothing Then Return
        Dim rus As Boolean = Is_Russian_Language
        _trayMiOpen.Text = If(rus, "Открыть Fast Media Sorter", "Open Fast Media Sorter")
        _trayMiQr.Text = If(rus, "Показать штрихкод..", "Show QR code..")
        _trayMiDesc.Text = If(rus, "Описание..", "Description..")
        _trayMiConfig.Text = If(rus, "Настроить..", "Configure..")
        _trayMiStop.Text = If(rus, "Выключить общий доступ", "Turn off sharing")
        _trayMiExit.Text = If(rus, "Выход", "Quit")
    End Sub

    ''' <summary>Brings the main window back on screen with its current content -
    ''' leaves tray-resident mode, unminimizes and focuses it. The last shown
    ''' media stays on the picture box/player (the form was only hidden, never
    ''' torn down), so the window always reappears with content, not empty.
    ''' Shared by the tray menu and a bare second launch (the user re-ran the
    ''' exe with no file just to get the window back).</summary>
    Friend Sub RestoreMainWindow()
        Try
            _residentInTray = False
            Me.Show()
            If Me.WindowState = FormWindowState.Minimized Then Me.WindowState = FormWindowState.Normal
            Me.Activate()
            Me.BringToFront()
        Catch
        End Try
    End Sub

    ''' <summary>Menu "Открыть" / double-click: restore and focus the main window
    ''' (leaving tray-resident mode).</summary>
    Private Sub TrayOpen()
        RestoreMainWindow()
    End Sub

    ''' <summary>Menu "Настроить": bring LITE forward, open Settings on the Share tab.</summary>
    Private Sub TrayConfigure()
        RestoreMainWindow()
        Try
            ShowShareSettings()
        Catch
        End Try
    End Sub

    ''' <summary>Menu "Показать штрихкод": build the current share QR (the combined
    ''' [lan, portforward] config, S1006) from the live worker status and show it big
    ''' via <see cref="Qr_Zoom_Form"/> so a phone can scan it straight from the tray -
    ''' no need to open the full window. Falls back to the LAN-only config if the
    ''' combined one is too dense for a QR.</summary>
    Private Async Sub TrayShowQr()
        Dim rus As Boolean = Is_Russian_Language
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st Is Nothing OrElse Not st.Running Then
            Try : _shareTrayIcon?.ShowBalloonTip(2500, "Fast Media Sorter", If(rus, "Общий доступ не запущен.", "Sharing is not running."), ToolTipIcon.Info) : Catch : End Try
            Return
        End If

        Dim cfg As ShareConfigResult = ShareConfigBuilder.Build(st, True)
        If cfg Is Nothing OrElse cfg.QrPng Is Nothing OrElse cfg.QrPng.Length = 0 OrElse cfg.QrOverflow Then
            cfg = ShareConfigBuilder.Build(st, False)   ' LAN-only fallback (smaller QR)
        End If
        If cfg Is Nothing OrElse cfg.QrPng Is Nothing OrElse cfg.QrPng.Length = 0 Then
            Try : _shareTrayIcon?.ShowBalloonTip(3000, "Fast Media Sorter", If(rus, "Код слишком большой - сохраните файл .fmscfg в настройках.", "Code too large - save the .fmscfg file in settings."), ToolTipIcon.Warning) : Catch : End Try
            Return
        End If

        Try
            Using ms As New MemoryStream(cfg.QrPng)
                Using img As Image = Image.FromStream(ms)
                    Qr_Zoom_Form.ShowImage(Me, img)
                End Using
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>Menu "Выключить": stop the SFTP server (leaves the worker process, per
    ''' the app's existing Stop semantics), then refresh so the icon hides. If the
    ''' window is currently tray-resident, stopping the share is the cue to fully quit.</summary>
    Private Async Sub TrayStop()
        Try
            Await ShareController.StopServerAsync()
        Catch
        End Try
        RefreshShareTray()
    End Sub

    ''' <summary>Menu "Выход": really quit LITE (bypasses the close-to-tray guard). The
    ''' worker keeps serving, matching the app's existing "closing LITE leaves the
    ''' worker running" behaviour.</summary>
    Private Sub TrayExit()
        _forceExit = True
        _residentInTray = False
        Try
            Me.Close()
        Catch
        End Try
    End Sub

    ''' <summary>Hides the main window but keeps the process + tray icon alive so the
    ''' share stays controllable after the window is "closed". Called from
    ''' Form1_FormClosing when a user close happens while a share is active.</summary>
    Friend Sub EnterTrayResidentMode()
        _residentInTray = True
        Try
            Me.Hide()
        Catch
        End Try
        EnsureTrayIcon()
        If _shareTrayIcon IsNot Nothing Then
            _shareTrayIcon.Visible = True
            If Not _trayResidentBalloonShown Then
                _trayResidentBalloonShown = True
                Try
                    Dim rus As Boolean = Is_Russian_Language
                    _shareTrayIcon.BalloonTipTitle = If(rus, "Fast Media Sorter свёрнут в трей", "Fast Media Sorter is in the tray")
                    _shareTrayIcon.BalloonTipText = If(rus,
                        "Общий доступ к папкам продолжается. Управление - через этот значок.",
                        "Folder sharing continues. Manage it from this icon.")
                    _shareTrayIcon.ShowBalloonTip(4000)
                Catch
                End Try
            End If
        End If
        ' Re-confirm the share is really still running; if it isn't, don't linger
        ' invisibly - the next poll result will quit the app.
        RefreshShareTray()
    End Sub

    ''' <summary>Really exit, deferred one message cycle so we never tear the form down
    ''' from inside a poll continuation.</summary>
    Private Sub DoRealExit()
        _forceExit = True
        Try
            If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                Me.BeginInvoke(New MethodInvoker(Sub() Me.Close()))
            End If
        Catch
        End Try
    End Sub

    ''' <summary>Removes the tray icon and frees its GDI handle. Called from FormClosing
    ''' so the icon never lingers as a ghost after LITE exits.</summary>
    Friend Sub ShutdownShareTray()
        _trayShutdown = True
        Try
            If _shareTrayTimer IsNot Nothing Then
                _shareTrayTimer.Stop()
                _shareTrayTimer.Dispose()
                _shareTrayTimer = Nothing
            End If
        Catch
        End Try
        Try
            If _shareTrayIcon IsNot Nothing Then
                _shareTrayIcon.Visible = False
                _shareTrayIcon.Dispose()
                _shareTrayIcon = Nothing
            End If
        Catch
        End Try
        Try
            If _shareTrayImage IsNot Nothing Then
                _shareTrayImage.Dispose()
                _shareTrayImage = Nothing
            End If
        Catch
        End Try
        ' Icon.FromHandle does not own the HICON - free it ourselves.
        If _shareTrayHIcon <> IntPtr.Zero Then
            Try : DestroyIcon(_shareTrayHIcon) : Catch : End Try
            _shareTrayHIcon = IntPtr.Zero
        End If
    End Sub

    ''' <summary>Truncates a tooltip to the NotifyIcon.Text limit (63 chars).</summary>
    Private Shared Function TruncateTip(text As String) As String
        If String.IsNullOrEmpty(text) Then Return "Fast Media Sorter"
        If text.Length <= 63 Then Return text
        Return text.Substring(0, 62) & "…"
    End Function

    ' --- icon drawing ----------------------------------------------------------

    ''' <summary>Draws the blue four-way arrow tray icon (a classic "move in all
    ''' directions" glyph) at 32px with a white halo for dark-taskbar contrast.</summary>
    Private Function BuildShareTrayIcon() As Icon
        Const sz As Integer = 32
        Const c As Single = 16.0F     ' center
        Const tp As Single = 14.0F    ' center -> arrow tip
        Const hb As Single = 8.0F     ' center -> arrowhead base
        Const hw As Single = 5.5F     ' arrowhead half-width (barbs)
        Const sw As Single = 2.5F     ' shaft half-width

        ' One arm (pointing up), clockwise, 7 points. The leading + trailing
        ' (±sw, -sw) are the central-square corners where neighbouring arms join;
        ' without them the diagonal gaps close up and the glyph reads as a diamond
        ' instead of four arrows. Rotated 90° three times fills all four arms.
        Dim arm As PointF() = {
            New PointF(-sw, -sw), New PointF(-sw, -hb), New PointF(-hw, -hb),
            New PointF(0, -tp), New PointF(hw, -hb), New PointF(sw, -hb), New PointF(sw, -sw)}

        Dim pts As New List(Of PointF)()
        Dim cur As PointF() = arm
        For a As Integer = 0 To 3
            For Each p As PointF In cur
                pts.Add(New PointF(c + p.X, c + p.Y))
            Next
            Dim rot(cur.Length - 1) As PointF
            For i As Integer = 0 To cur.Length - 1
                rot(i) = New PointF(-cur(i).Y, cur(i).X)  ' rotate 90°
            Next
            cur = rot
        Next
        Dim poly As PointF() = pts.ToArray()

        Dim hicon As IntPtr = IntPtr.Zero
        Using bmp As New Bitmap(sz, sz)
            Using g As Graphics = Graphics.FromImage(bmp)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.Clear(Color.Transparent)

                Using halo As New Pen(Color.FromArgb(235, 255, 255, 255), 2.4F)
                    halo.LineJoin = LineJoin.Round
                    g.DrawPolygon(halo, poly)
                End Using
                Using fill As New SolidBrush(Color.FromArgb(30, 120, 220))
                    g.FillPolygon(fill, poly)
                End Using
                Using edge As New Pen(Color.FromArgb(18, 78, 150), 1.0F)
                    edge.LineJoin = LineJoin.Round
                    g.DrawPolygon(edge, poly)
                End Using
            End Using
            hicon = bmp.GetHicon()
        End Using

        _shareTrayHIcon = hicon
        Return Icon.FromHandle(hicon)
    End Function

    ''' <summary>Snapshot of what the tray needs to render (crosses the thread boundary).</summary>
    Private NotInheritable Class ShareTrayState
        Public Property Running As Boolean
        Public Property Address As String = ""
    End Class

End Class
