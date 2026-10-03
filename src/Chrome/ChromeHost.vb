#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' A floating panel's window (002 section 2.1): borderless, owned by the main window, never
''' activated. It is the only way WinForms allows real translucency over the media - a child control
''' cannot alpha-compose over a sibling PictureBox or the LibVLC view, and WS_EX_LAYERED on a child
''' window is not created at all - so it is the same shape as the recipients overlay's window, with
''' the one painted <see cref="ChromeSurface"/> inside it.
'''
''' Corners use a window region; Windows supplies the optional drop shadow. Keeping
''' compositor alpha on Form.Opacity preserves native edit, slider and accessibility behavior.
''' </summary>
Friend Class ChromeHost
    Inherits Form
    Implements IThemeAware

    Private Const WS_EX_NOACTIVATE As Integer = &H8000000
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WM_MOUSEACTIVATE As Integer = &H21
    Private Const MA_NOACTIVATE As Integer = 3
    Private Const CS_DROPSHADOW As Integer = &H20000

    Private ReadOnly panelSurface As New ChromeSurface()
    Private cornerRadius As Integer

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property AllowActivation As Boolean

    Public Sub New(Optional paintedSurface As Boolean = True)
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False
        ControlBox = False
        MinimizeBox = False
        MaximizeBox = False
        KeyPreview = False
        AutoScaleMode = AutoScaleMode.None
        Padding = New Padding(0)
        If paintedSurface Then Controls.Add(panelSurface)
    End Sub

    ''' <summary>Content-coloured, like the window it floats over (APP-STYLE rule 5, amendment 0.10): the
    ''' panel takes its colours from the picture through the background scheme, so the application
    ''' theme must neither walk it nor repaint it.</summary>
    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return True
        End Get
    End Property

    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        ' Nothing: see ThemesItself.
    End Sub

    Public ReadOnly Property Surface As ChromeSurface
        Get
            Return panelSurface
        End Get
    End Property

    ''' <summary>The corner radius of the region, in device pixels.</summary>
    Public Sub SetCornerRadius(radius As Integer)
        cornerRadius = Math.Max(0, radius)
        ApplyRoundedRegion()
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        MyBase.OnSizeChanged(e)
        ApplyRoundedRegion()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        ApplyRoundedRegion()
    End Sub

    Private Sub ApplyRoundedRegion()
        If Not IsHandleCreated OrElse Width <= 0 OrElse Height <= 0 Then Return
        Dim previous As Region = Region
        If cornerRadius <= 0 Then
            If previous IsNot Nothing Then
                Region = Nothing
                previous.Dispose()
            End If
            Return
        End If
        Using path As GraphicsPath = ChromeSurface.RoundedRect(New RectangleF(0, 0, Width, Height), cornerRadius)
            Region = New Region(path)
        End Using
        If previous IsNot Nothing Then previous.Dispose()
    End Sub

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return Not AllowActivation
        End Get
    End Property

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Dim parameters As CreateParams = MyBase.CreateParams
            If SystemInformation.IsDropShadowEnabled AndAlso Not SystemInformation.HighContrast Then parameters.ClassStyle = parameters.ClassStyle Or CS_DROPSHADOW
            parameters.ExStyle = parameters.ExStyle Or WS_EX_TOOLWINDOW
            If Not AllowActivation Then parameters.ExStyle = parameters.ExStyle Or WS_EX_NOACTIVATE
            Return parameters
        End Get
    End Property

    ''' <summary>A click on the panel must never make it the active window: the keyboard stays with
    ''' the viewer (invariant 2).</summary>
    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_MOUSEACTIVATE AndAlso Not AllowActivation Then
            m.Result = New IntPtr(MA_NOACTIVATE)
            Return
        End If
        MyBase.WndProc(m)
    End Sub

End Class
#End If
