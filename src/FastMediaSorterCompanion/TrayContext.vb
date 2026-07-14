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

    Friend Sub New(initialFolder As String, Optional showWindowOnStart As Boolean = True)
        _trayIcon = ShareIcons.CreateIcon(_trayHIcon)

        _notifyIcon = New NotifyIcon() With {
            .Icon = _trayIcon,
            .Text = "Fast Media Sorter: Share Manager",
            .Visible = True
        }
        _notifyIcon.ContextMenuStrip = BuildMenu()
        AddHandler _notifyIcon.DoubleClick, Sub() ShowMainWindow(Nothing)

        _messageWindow = New MessageWindow()
        AddHandler _messageWindow.PayloadReceived, AddressOf OnPayloadReceived

        _statsTimer = New Timer With {.Interval = 30000}
        AddHandler _statsTimer.Tick, AddressOf OnStatsTimerTick

        ' Resume sharing silently if it was ever started - covers the "dedicated
        ' server" scenario where Companion autostarts into the tray with no window.
        ResumeShareIfEnabled()

        ' A folder argument is the explicit "share this folder" gesture: open the
        ' window (which jumps to the package wizard). Otherwise a manual launch
        ' (showWindowOnStart=True) opens the main window, while a silent autostart
        ' (--tray) stays in the tray with no window (spec §4.5.1 / §4.3).
        If Not String.IsNullOrEmpty(initialFolder) Then
            ShowMainWindow(initialFolder)
        ElseIf showWindowOnStart Then
            ShowMainWindow(Nothing)
        End If
    End Sub

    Private Function BuildMenu() As ContextMenuStrip
        Dim rus As Boolean = Is_Russian_Language
        Dim menu As New ContextMenuStrip()

        ' Primary action (bold) - open the Share Manager window.
        Dim miOpen As New ToolStripMenuItem(If(rus, "Открыть менеджер общего доступа", "Open Share Manager"))
        miOpen.Font = New Font(menu.Font, FontStyle.Bold)
        AddHandler miOpen.Click, Sub() ShowMainWindow(Nothing)
        menu.Items.Add(miOpen)

        ' "Share..": open the package wizard so the user first picks WHAT to share and
        ' with WHICH settings, then gets the QR - instead of dumping one fixed QR.
        Dim miShare As New ToolStripMenuItem(If(rus, "Поделиться..", "Share.."))
        AddHandler miShare.Click, Sub() TrayShare()
        menu.Items.Add(miShare)

        ' Current state + local usage counters (last connection, totals, files served).
        Dim miStatus As New ToolStripMenuItem(If(rus, "Текущее состояние..", "Status.."))
        AddHandler miStatus.Click, Sub() TrayStatus()
        menu.Items.Add(miStatus)

        ' Online "publish your folders for Android" guide.
        Dim miDesc As New ToolStripMenuItem(If(rus, "Описание..", "Description.."))
        AddHandler miDesc.Click, Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl)
        menu.Items.Add(miDesc)

        ' Stop the SFTP server (worker process keeps running).
        Dim miStop As New ToolStripMenuItem(If(rus, "Выключить общий доступ", "Turn off sharing"))
        AddHandler miStop.Click, Sub() TrayStop()
        menu.Items.Add(miStop)

        menu.Items.Add(New ToolStripSeparator())

        ' Suite cross-link: open the LITE viewer.
        Dim miViewer As New ToolStripMenuItem(If(rus, "Открыть Fast Media Sorter", "Open Fast Media Sorter"))
        AddHandler miViewer.Click, Sub() LaunchViewer()
        menu.Items.Add(miViewer)

        Dim miExit As New ToolStripMenuItem(If(rus, "Выход", "Exit"))
        AddHandler miExit.Click, Sub() ExitApplication()
        menu.Items.Add(miExit)

        Return menu
    End Function

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

    Private Sub OnStatsTimerTick(sender As Object, e As EventArgs)
        Dim t As Task = UpdateTooltipStatsAsync()
    End Sub

    ''' <summary>Enriches the tray tooltip with a concise usage line while serving
    ''' (NotifyIcon.Text is capped ~63 chars). Fire-and-forget; never blocks the UI.</summary>
    Private Async Function UpdateTooltipStatsAsync() As Task
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If _disposed OrElse _notifyIcon Is Nothing Then Return
        ' If sharing was turned off while this fetch was in flight, RefreshTrayState(False)
        ' already stopped the poll timer and set the idle tooltip - don't clobber it back.
        If Not _statsTimer.Enabled Then Return
        Dim rus As Boolean = Is_Russian_Language
        Dim txt As String
        If st IsNot Nothing AndAlso st.Running Then
            txt = If(rus, "Общий доступ включён", "Sharing on")
            If st.Stats IsNot Nothing Then
                txt &= (If(rus, " · подключений: ", " · conns: ")) & st.Stats.TotalConnections.ToString()
                Dim last As String = Share_Status_Form.ShortTime(st.Stats.LastConnectionAt)
                If last.Length > 0 Then txt &= (If(rus, " · посл.: ", " · last: ")) & last
            End If
        Else
            txt = "Fast Media Sorter: Share Manager"
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
    ''' flow; when a window already exists it is just activated (folder routing is
    ''' refined with the wake protocol in Ф4).</summary>
    Private Sub ShowMainWindow(initialFolder As String)
        Try
            If _mainWindow Is Nothing OrElse _mainWindow.IsDisposed Then
                _mainWindow = New MainWindow(initialFolder)
                AddHandler _mainWindow.ServerStateChanged, AddressOf RefreshTrayState
            End If
            If Not _mainWindow.Visible Then _mainWindow.Show()
            If _mainWindow.WindowState = FormWindowState.Minimized Then _mainWindow.WindowState = FormWindowState.Normal
            _mainWindow.Activate()
            _mainWindow.BringToFront()
            ForceToForeground(_mainWindow)
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
                If(Is_Russian_Language, "Общий доступ включён", "Sharing on"),
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
        Else
            ShowMainWindow(payload)
        End If
    End Sub

    Private Sub LaunchViewer()
        Try
            Dim dir As String = IO.Path.GetDirectoryName(Application.ExecutablePath)
            Dim exe As String = IO.Path.Combine(dir, "FastMediaSorter_LITE.exe")
            If IO.File.Exists(exe) Then
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
