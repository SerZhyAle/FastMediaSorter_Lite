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

        ' Show the current share's QR big, straight from the tray (no window needed).
        Dim miQr As New ToolStripMenuItem(If(rus, "Показать штрихкод..", "Show QR code.."))
        AddHandler miQr.Click, Sub() TrayShowQr()
        menu.Items.Add(miQr)

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

    ''' <summary>Tray "Показать штрихкод": build the current share's combined QR from the
    ''' live worker status and show it big via Qr_Zoom_Form (LAN-only fallback when the
    ''' combined code is too dense). Ported from LITE's Main_Form.ShareTray.TrayShowQr.</summary>
    Private Async Sub TrayShowQr()
        Dim rus As Boolean = Is_Russian_Language
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st Is Nothing OrElse Not st.Running Then
            Try : _notifyIcon.ShowBalloonTip(2500, "Fast Media Sorter", If(rus, "Общий доступ не запущен.", "Sharing is not running."), ToolTipIcon.Info) : Catch : End Try
            Return
        End If

        Dim cfg As ShareConfigResult = ShareConfigBuilder.Build(st, True)
        If cfg Is Nothing OrElse cfg.QrPng Is Nothing OrElse cfg.QrPng.Length = 0 OrElse cfg.QrOverflow Then
            cfg = ShareConfigBuilder.Build(st, False)   ' LAN-only fallback (smaller QR)
        End If
        If cfg Is Nothing OrElse cfg.QrPng Is Nothing OrElse cfg.QrPng.Length = 0 Then
            Try : _notifyIcon.ShowBalloonTip(3000, "Fast Media Sorter", If(rus, "Код слишком большой - сохраните файл .fmscfg в окне.", "Code too large - save the .fmscfg file in the window."), ToolTipIcon.Warning) : Catch : End Try
            Return
        End If

        Try
            Using ms As New IO.MemoryStream(cfg.QrPng)
                Using img As Image = Image.FromStream(ms)
                    Qr_Zoom_Form.ShowImage(Nothing, img)
                End Using
            End Using
        Catch
        End Try
    End Sub

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
