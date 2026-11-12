Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The standalone Scheduled file operations window (L0297): one modeless management window that
''' embeds <see cref="Scheduled_Manager_Panel"/>, reusing the engine and the existing editor and
''' history dialogs. It is reached on its own - from the tray menu, from the Manager settings'
''' scheduled page and through the viewer's wake - and states the same facts the settings page
''' states; it holds no scheduler data of its own.
''' One instance per process: every entry point lands on the same window.
''' </summary>
Friend NotInheritable Class Scheduled_Manager_Form
    Inherits Form

    ''' <summary>Its window bounds in <c>SZA\FastMediaSorter</c> - the settings windows' own shape
    ''' (logical width and height at 96 DPI, device position), no page context to keep.</summary>
    Private Const BoundsKey As String = "Share_ScheduledWindowBounds"

    Private Shared _instance As Scheduled_Manager_Form

    ''' <summary>Installed by the tray host - the only owner of the Manager settings window - so the
    ''' window's autostart link lands on that window's startup page. Nothing while no host is
    ''' installed: the link then does nothing (the tray process installs it before it can open this
    ''' window).</summary>
    Friend Shared OpenManagerSettingsPage As Action(Of String)

    Private ReadOnly _panel As Scheduled_Manager_Panel
    Private _iconHandle As IntPtr
    Private _boundsRestored As Boolean

    Private Sub New()
        ' Script font + text direction for the active language, before any control exists -
        ' children inherit both (the settings window's own rule).
        UiLanguage.ApplyTo(Me)
        Text = Share_Settings_Form.ScheduledPageCaption()
        Icon = ShareIcons.CreateIcon(_iconHandle)
        StartPosition = FormStartPosition.CenterScreen
        MinimumSize = New Size(560, 380)
        ClientSize = New Size(760, 560)

        _panel = New Scheduled_Manager_Panel() With {.Dock = DockStyle.Fill}
        AddHandler _panel.AutostartLinkRequested, Sub() RaiseAutostartLink()
        Controls.Add(_panel)

        AddHandler FormClosed, Sub(sender As Object, e As FormClosedEventArgs)
                                   SaveBounds()
                                   _instance = Nothing
                               End Sub
        AddHandler Disposed, Sub()
                                 ShareIcons.FreeIcon(Icon, _iconHandle)
                                 _iconHandle = IntPtr.Zero
                             End Sub
    End Sub

    ''' <summary>The one entry point: opens (or brings to front) the single scheduling window. A
    ''' folder starts a new operation with that source in the editor, once the window is up. Returns
    ''' the window so a caller that must force itself to the foreground (the tray) can reach it.</summary>
    Friend Shared Function OpenScheduledManager(folder As String, owner As IWin32Window) As Scheduled_Manager_Form
        If _instance Is Nothing OrElse _instance.IsDisposed Then
            _instance = New Scheduled_Manager_Form()
        End If
        Dim window As Scheduled_Manager_Form = _instance
        If folder IsNot Nothing AndAlso folder.Length > 0 Then window._panel.PrefillFromFolder(folder)
        If Not window.Visible Then
            If owner IsNot Nothing Then
                window.Show(owner)
            Else
                window.Show()
            End If
        End If
        If window.WindowState = FormWindowState.Minimized Then window.WindowState = FormWindowState.Normal
        window.BringToFront()
        window.Activate()
        Return window
    End Function

    Private Sub RaiseAutostartLink()
        Dim route As Action(Of String) = OpenManagerSettingsPage
        If route IsNot Nothing Then route("startup")
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        RestoreSavedBounds()
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        ThemeService.Attach(Me)
        _panel.HandleScheduledPrefill()
    End Sub

    ''' <summary>The settings windows' own restore shape: logical size at 96 DPI, device position,
    ''' clamped to the working area.</summary>
    Private Sub RestoreSavedBounds()
        If _boundsRestored Then Return
        _boundsRestored = True
        Try
            Dim raw = GetSetting(App_name, Second_App_Name, BoundsKey, String.Empty)
            If String.IsNullOrEmpty(raw) Then Return
            Dim values = System.Text.Json.JsonSerializer.Deserialize(Of Integer())(raw)
            If values Is Nothing OrElse values.Length <> 4 OrElse values(2) < 300 OrElse values(3) < 200 OrElse
               values(2) > 8192 OrElse values(3) > 8192 Then Return
            Dim factor = DeviceDpi / 96.0R
            Size = New Size(CInt(values(2) * factor), CInt(values(3) * factor))
            Location = New Point(values(0), values(1))
            DpiLayout.ClampToWorkingArea(Me)
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: restoring window bounds", ex)
        End Try
    End Sub

    Private Sub SaveBounds()
        Try
            Dim savedBounds = If(WindowState = FormWindowState.Normal, Bounds, RestoreBounds)
            SaveSetting(App_name, Second_App_Name, BoundsKey, System.Text.Json.JsonSerializer.Serialize(
                {savedBounds.X, savedBounds.Y, CInt(savedBounds.Width * 96.0R / DeviceDpi), CInt(savedBounds.Height * 96.0R / DeviceDpi)}))
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: saving window bounds", ex)
        End Try
    End Sub

End Class
