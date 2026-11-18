Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public NotInheritable Class MainWindow
    Private ReadOnly _settingsWindows As New Dictionary(Of SettingsDestination, Share_Settings_Form)()
    Private _settingsRefreshRunning As Boolean
    Private _settingsRefreshPending As Boolean

    Friend Sub OpenSettings(destination As SettingsDestination, Optional page As String = "", Optional folder As String = "")
        If IsDisposed OrElse Disposing Then Return
        ' The opt-in FMS_UI_TIMING=1 line (L0326, WINDOWS-UI sections 5 and 9): from the request to the first idle moment
        ' after the window is shown (0 when the switch is off).
        Dim started As Long = UiTiming.Begin()
        ' Availability belongs to the component that owns the page: a deep link to an SFTP page of a Manager window whose
        ' component is not installed is refused here and never opens an empty group (253 R-4).
        Dim installed As Boolean = If(page.Length > 0, SettingsRoutes.PageAvailable(page, WorkerProcess.IsAvailable(), ExchangeProcess.IsAvailable()),
                                      SettingsRoutes.Available(destination, ExchangeProcess.IsAvailable()))
        If Not installed Then
            ManagerBox.Show(Me, Localization.T("Компонент недоступен. Проверьте установку приложения."),
                ProductNames.ShareManager, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim window As Share_Settings_Form = Nothing
        Dim reused As Boolean = True
        If Not _settingsWindows.TryGetValue(destination, window) OrElse window.IsDisposed Then
            reused = False
            window = New Share_Settings_Form(_status, destination)
            _settingsWindows(destination) = window
            If destination = SettingsDestination.Manager Then _settingsWindow = window
            AddHandler window.NavigateRequested, Sub(target) OpenSettings(target)
            AddHandler window.SettingsChanged, AddressOf OnSettingsStateChanged
            AddHandler window.FormClosed, AddressOf OnSettingsClosed
            ' A hidden main window must remain alive while its independent settings are open.
        End If
        If page.Length > 0 Then window.SelectPage(page)
        If page = "scheduled" AndAlso folder.Length > 0 Then window.FocusScheduledPage(folder)
        ' A new window is timed to its Shown event; a window that was already open raises none, so it is timed from here.
        If reused Then UiTiming.CompleteAtIdle("Share_Settings_Form", UiTimingKind.Dialog, started) Else UiTiming.WatchWindow(window, started)
        If Not window.Visible Then window.Show(Me)
        ThemeService.Attach(window)
        If window.WindowState = FormWindowState.Minimized Then window.WindowState = FormWindowState.Normal
        If reused Then BringToRequestingMonitor(window)
        window.BringToFront()
        window.Activate()
    End Sub

    ''' <summary>
    ''' APP-SETTINGS rule 1: a settings window that already exists is brought to the monitor the user is working on - the
    ''' one of the application window that has the focus (a click inside this program), otherwise the one under the pointer
    ''' (the tray, a hotkey). A window already on that monitor keeps its place; a maximized one is left alone. The
    ''' arithmetic is <see cref="ManagerPlacement.ForRequestingMonitor"/>, held without a display.
    ''' </summary>
    Private Sub BringToRequestingMonitor(window As Share_Settings_Form)
        Try
            If window.IsDisposed OrElse window.WindowState <> FormWindowState.Normal Then Return
            Dim active As Form = Form.ActiveForm
            Dim area As Rectangle = If(active IsNot Nothing AndAlso Not active.IsDisposed,
                                       Screen.FromControl(active).WorkingArea,
                                       Screen.FromPoint(Cursor.Position).WorkingArea)
            Dim target As Rectangle = ManagerPlacement.ForRequestingMonitor(window.Bounds, area)
            If target <> window.Bounds Then window.Bounds = target
        Catch ex As Exception
            AppFileLogger.LogException("Settings: move to the requesting monitor", ex)
        End Try
    End Sub

    Private Async Sub OnSettingsStateChanged(destination As SettingsDestination)
        _settingsRefreshPending = True
        If _settingsRefreshRunning Then Return
        _settingsRefreshRunning = True
        Try
            While _settingsRefreshPending AndAlso Not IsDisposed
                _settingsRefreshPending = False
                LoadLocalState()
                ' Read only: GetStatusAsync never ensures/spawns a worker.
                If ServerFeatures.IsEnabled() AndAlso WorkerProcess.IsAvailable() Then
                    _status = Await ShareController.GetStatusAsync()
                End If
                If IsDisposed OrElse Disposing Then Return
                ApplyGate()
                ApplyStatusToUi()
            End While
        Catch ex As Exception
            AppFileLogger.LogException("Settings: refresh manager", ex)
        Finally
            _settingsRefreshRunning = False
        End Try
    End Sub

    Private Async Sub OnSettingsClosed(sender As Object, e As FormClosedEventArgs)
        Dim window = DirectCast(sender, Share_Settings_Form)
        _settingsWindows.Remove(window.Destination)
        If window Is _settingsWindow Then _settingsWindow = Nothing
        RemoveHandler window.SettingsChanged, AddressOf OnSettingsStateChanged
        OnSettingsStateChanged(window.Destination)
        If IsDisposed OrElse Disposing Then Return
        If window.ResourceAddRequested Then Await OpenResourceAddAsync(Me, ResourceAddEntry.Exchange)
        If window.ReplayManagerRequested Then Await ReplayManagerWelcomeAsync()
        If window.ReplayExchangeRequested Then Await Server_Welcome_Form.ReplayExchangeAsync(Me)
    End Sub
End Class
