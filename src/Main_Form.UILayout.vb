Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Windows.Forms

' Layout entry points for Main_Form.
'
' Since the modernization (see Main_Form.ModernLayout.vb) the chrome lives in
' docked container panels, so layout no longer positions individual controls.
' What remains here is: switching the window border/state for full-screen,
' toggling chrome visibility for super-full-screen, and keeping the media
' surface (picture boxes / web browser / VLC view) sized to panel_Media.
Partial Public Class Main_Form

    Private Sub ISizeChanged()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1935: ISizeChanged")

        ' panel_Media is created by BuildModernLayout(); ignore any layout pass
        ' that fires before that (e.g. during InitializeComponent).
        If panel_Media Is Nothing Then Return

        ' Prevent the resize event from re-triggering the debounce timer.
        is_Programmatic_Resize = True

        Dim needs_Form_State_Change As Boolean = (is_Full_Screen_Mode <> is_Last_Full_Screen_State)
        If needs_Form_State_Change Then
            If is_Full_Screen_Mode Then
                FormBorderStyle = FormBorderStyle.None
                WindowState = FormWindowState.Maximized
            Else
                FormBorderStyle = FormBorderStyle.Sizable
                WindowState = FormWindowState.Normal
            End If
            is_Last_Full_Screen_State = is_Full_Screen_Mode
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1936: Form state changed, border/window updated")
        End If

        ' Leaving full-screen always clears super-full-screen.
        If Not is_Full_Screen_Mode Then is_Super_Full_Screen_Mode = False

        ' Chrome per mode:
        '   windowed          -> toolbar docked at top, status bar at bottom
        '   full-screen       -> image fills the screen, toolbar floats over it
        '   super-full-screen -> no chrome at all
        panel_Status.Visible = Not is_Full_Screen_Mode
        flow_Toolbar.Visible = Not is_Super_Full_Screen_Mode
        SetToolbarOverlay(is_Full_Screen_Mode AndAlso Not is_Super_Full_Screen_Mode)

        Me.Focus()
        Me.PerformLayout()

        SyncMediaSurface()

        If is_form_shown Then Draw_Perspective()
        SkipZoom()

        is_Programmatic_Resize = False
    End Sub

    ''' <summary>
    ''' Sizes every media element to fill panel_Media (panel-relative, so the
    ''' top-left is 0,0). Called on every layout and zoom reset.
    ''' </summary>
    Friend Sub SyncMediaSurface()
        If panel_Media Is Nothing Then Return

        Dim full As Size = panel_Media.ClientSize

        Picture_Box_1.Location = Point.Empty
        Picture_Box_1.Size = full
        Picture_Box_2.Location = Point.Empty
        Picture_Box_2.Size = full

        Web_Browser.Location = Point.Empty
        Web_Browser.Size = full

        If vlc_Video_View IsNot Nothing Then
            vlc_Video_View.Location = Point.Empty
            vlc_Video_View.Size = full
        End If

        lbl_Help_Info.Location = Point.Empty
        lbl_Help_Info.Size = full
    End Sub

    Private Sub SetViewSizes()
        ISizeChanged()
    End Sub

    Private Sub Form1_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        ' Don't start the debounce timer for our own programmatic resizes.
        If Not is_Programmatic_Resize Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1126: Form Resize (user-initiated)")
            ResizeDebounceTimer.Stop()
            ResizeDebounceTimer.Start()
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1127: Form Resize (programmatic - ignored)")
        End If
    End Sub

    Private Sub ResizeDebounceTimer_Tick(sender As Object, e As EventArgs) Handles ResizeDebounceTimer.Tick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1128: ResizeDebounceTimer_Tick")
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub

    ''' <summary>Resets zoom: media fills panel_Media at 1:1 fit.</summary>
    Private Sub SkipZoom()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1172: zoom reset")
        If panel_Media Is Nothing Then Return

        SyncMediaSurface()

        zoom_Scale = 1.0F
        lbl_Zoom.Text = ""
    End Sub

    Private Sub Button7_Click(sender As Object, e As EventArgs) Handles btn_Full_Screen.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1900: btn_Full_Screen")
        is_Full_Screen_Mode = Not is_Full_Screen_Mode
        SetViewSizes()
    End Sub

End Class
