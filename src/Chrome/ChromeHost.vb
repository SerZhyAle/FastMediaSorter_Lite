#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>
''' Something inside a <see cref="ChromeSurface"/> that draws its own glyphs or text (the path box)
''' and so has to hand them to the ink layer when the panel is translucent.
''' </summary>
Friend Interface IChromeInkSource
    ''' <summary>Paints this control's glyphs and text, origin at its top-left corner, on a transparent
    ''' surface; its fills stay with the control's own painting.</summary>
    Sub PaintInk(g As Graphics)

    ''' <summary>True while the panel's ink is drawn by the ink layer instead of the control itself.</summary>
    Sub InkModeChanged(external As Boolean)

    ''' <summary>True while a native edit control inside draws its own text (the user is typing): that text
    ''' fades with the window, so the panel is raised to the whole-window floor (L0303 R-11) and no scrim
    ''' is laid over it.</summary>
    ReadOnly Property EditsNatively As Boolean
End Interface

''' <summary>
''' A floating panel's window (002 section 2.1): borderless, owned by the main window, never
''' activated. It is the only way WinForms allows real translucency over the media - a child control
''' cannot alpha-compose over a sibling PictureBox or the LibVLC view, and WS_EX_LAYERED on a child
''' window is not created at all - so it is the same shape as the recipients overlay's window, with
''' the one painted <see cref="ChromeSurface"/> inside it.
'''
''' Corners use a window region; Windows supplies the optional drop shadow. Keeping
''' compositor alpha on Form.Opacity preserves native edit, slider and accessibility behavior.
'''
''' Form.Opacity fades EVERYTHING in the window, glyphs and captions with the panel's fill, so a
''' translucent panel over a bright picture lost its ink. While the panel is translucent
''' (<see cref="UseInkLayer"/>) the fills stay here, at the panel's opacity, and the glyphs and
''' captions are drawn into a second, click-through, per-pixel-alpha window owned by this one
''' (<see cref="ChromeInkWindow"/>): the panel is as see-through as the user asked and its
''' content is fully legible at any setting. That window follows only the panel's own fade.
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
    Private inkWindow As ChromeInkWindow
    Private inkBitmap As Bitmap
    Private inkOpacityValue As Double = 1.0R
    Private lastScrimBase As Double = -1.0R
    Private inkRefreshing As Boolean

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
        AddHandler panelSurface.InkNeeded, Sub() RefreshInk()
    End Sub

    ''' <summary>True while this panel's glyphs and captions are drawn into the ink layer, so they do not
    ''' share the window's translucency. Off when the panel is opaque anyway (and under high contrast).</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property UseInkLayer As Boolean
        Get
            Return panelSurface.InkExternal
        End Get
        Set(value As Boolean)
            If panelSurface.InkExternal = value Then Return
            panelSurface.InkExternal = value
            If value Then RefreshInk() Else HideInk()
        End Set
    End Property

    ''' <summary>How visible the ink layer is, 0..1: the panel's own fade (show and hide), never its translucency.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property InkOpacity As Double
        Get
            Return inkOpacityValue
        End Get
        Set(value As Double)
            value = Math.Max(0.0R, Math.Min(1.0R, value))
            If value = inkOpacityValue Then Return
            inkOpacityValue = value
            RefreshInk()
        End Set
    End Property

    ''' <summary>Draws the panel's ink and presents it over the panel. Cheap: one transparent bitmap the
    ''' size of the window, painted from the same item list the fills come from.</summary>
    Friend Sub RefreshInk()
        If inkRefreshing Then Return
        If Not panelSurface.InkExternal OrElse IsDisposed OrElse Not IsHandleCreated OrElse Not Visible OrElse
           ClientSize.Width <= 0 OrElse ClientSize.Height <= 0 Then
            HideInk()
            Return
        End If
        inkRefreshing = True
        Try
            If inkWindow Is Nothing OrElse inkWindow.IsDisposed Then inkWindow = New ChromeInkWindow()
            If inkBitmap Is Nothing OrElse inkBitmap.Width <> ClientSize.Width OrElse inkBitmap.Height <> ClientSize.Height Then
                If inkBitmap IsNot Nothing Then inkBitmap.Dispose()
                inkBitmap = New Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb)
            End If
            ' L0303 R-10: under each ink the surface lays the item's own fill once more (the scrim), solved
            ' against the window's resting opacity, so the ink reads over any picture; no outline ring.
            lastScrimBase = ScrimBase()
            Using g As Graphics = Graphics.FromImage(inkBitmap)
                g.Clear(Color.Transparent)
                ' The surface may be taller than the window (a scrolled menu): the window clips it.
                g.SetClip(New Rectangle(Point.Empty, ClientSize))
                PaintInkLayer(g, lastScrimBase)
            End Using
            inkWindow.Present(Me, inkBitmap, PointToScreen(Point.Empty), CByte(Math.Round(inkOpacityValue * 255.0R)))
        Catch ex As Exception
            ' The ink is an enhancement over the panel; a failure here must not take the panel down.
            Debug.WriteLine("w2602: chrome ink layer failed: " & ex.Message)
        Finally
            inkRefreshing = False
        End Try
    End Sub

    ''' <summary>Paints the window's ink (origin at its client top-left, transparent elsewhere) with the scrims
    ''' solved for <paramref name="scrimBase"/>. The painted surface by default; a window built of its own controls
    ''' (the recipients table, L0303 R-12) paints theirs.</summary>
    Protected Overridable Sub PaintInkLayer(g As Graphics, scrimBase As Double)
        panelSurface.ScrimOpacity = scrimBase
        g.TranslateTransform(panelSurface.Left, panelSurface.Top)
        panelSurface.PaintInk(g)
    End Sub

    ''' <summary>The opacity the panel's fills rest at, its own fade taken out: the window's opacity over the
    ''' ink layer's (both carry the same show / hide fade), clamped to 0..1.</summary>
    Friend Function ScrimBase() As Double
        If inkOpacityValue <= 0.0R Then Return Math.Max(0.0R, Math.Min(1.0R, Opacity))
        Return Math.Max(0.0R, Math.Min(1.0R, Opacity / inkOpacityValue))
    End Function

    ''' <summary>Redraws the ink when the window's resting opacity moved without a change of the fade (an
    ''' edit raising the panel to its floor, R-11), so the scrim follows the opacity it is solved for.</summary>
    Friend Sub RefreshInkIfScrimMoved()
        If panelSurface.InkExternal AndAlso Visible AndAlso Math.Abs(ScrimBase() - lastScrimBase) > 0.002R Then RefreshInk()
    End Sub

    Private Sub HideInk()
        If inkWindow IsNot Nothing AndAlso Not inkWindow.IsDisposed AndAlso inkWindow.Visible Then inkWindow.Hide()
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Visible Then RefreshInk() Else HideInk()
    End Sub

    Protected Overrides Sub OnLocationChanged(e As EventArgs)
        MyBase.OnLocationChanged(e)
        If panelSurface.InkExternal AndAlso Visible Then RefreshInk()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            If inkWindow IsNot Nothing Then inkWindow.Dispose() : inkWindow = Nothing
            If inkBitmap IsNot Nothing Then inkBitmap.Dispose() : inkBitmap = Nothing
        End If
        MyBase.Dispose(disposing)
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
        If panelSurface.InkExternal AndAlso Visible Then RefreshInk()
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

''' <summary>
''' The ink layer of one translucent panel: a borderless, click-through, never-activated window with
''' per-pixel alpha, owned by the panel's <see cref="ChromeHost"/> so Windows keeps it above the
''' panel, hides it with the panel and moves it in the panel's z-order band. It shows exactly the
''' bitmap it is handed and nothing else; the panel keeps the fills, the clicks and the keyboard.
''' </summary>
Friend NotInheritable Class ChromeInkWindow
    Inherits Form
    Implements IThemeAware

    Private Const WS_EX_LAYERED As Integer = &H80000
    Private Const WS_EX_TRANSPARENT As Integer = &H20
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WS_EX_NOACTIVATE As Integer = &H8000000
    Private Const WM_NCHITTEST As Integer = &H84
    Private Const WM_MOUSEACTIVATE As Integer = &H21
    Private Const HTTRANSPARENT As Integer = -1
    Private Const MA_NOACTIVATE As Integer = 3
    Private Const ULW_ALPHA As Integer = 2
    Private Const AC_SRC_OVER As Byte = 0
    Private Const AC_SRC_ALPHA As Byte = 1

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativePoint
        Public X As Integer
        Public Y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeSize
        Public Width As Integer
        Public Height As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure BlendFunction
        Public BlendOp As Byte
        Public BlendFlags As Byte
        Public SourceConstantAlpha As Byte
        Public AlphaFormat As Byte
    End Structure

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UpdateLayeredWindow(hwnd As IntPtr, hdcDst As IntPtr, ByRef pptDst As NativePoint,
                                                ByRef psize As NativeSize, hdcSrc As IntPtr, ByRef pptSrc As NativePoint,
                                                crKey As Integer, ByRef pblend As BlendFunction, dwFlags As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetDC(hwnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(hwnd As IntPtr, hdc As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleDC(hdc As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(hdc As IntPtr, hgdiobj As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hobject As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteDC(hdc As IntPtr) As Boolean
    End Function

    Public Sub New()
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False
        ControlBox = False
        MinimizeBox = False
        MaximizeBox = False
        KeyPreview = False
        AutoScaleMode = AutoScaleMode.None
    End Sub

    ''' <summary>Painted entirely by the host from the colours of the picture, like the panel it covers.</summary>
    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return True
        End Get
    End Property

    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        ' Nothing: see ThemesItself.
    End Sub

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Dim parameters As CreateParams = MyBase.CreateParams
            parameters.ExStyle = parameters.ExStyle Or WS_EX_LAYERED Or WS_EX_TRANSPARENT Or WS_EX_TOOLWINDOW Or WS_EX_NOACTIVATE
            Return parameters
        End Get
    End Property

    ''' <summary>Never a target: the click belongs to the panel underneath.</summary>
    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_NCHITTEST Then
            m.Result = New IntPtr(HTTRANSPARENT)
            Return
        End If
        If m.Msg = WM_MOUSEACTIVATE Then
            m.Result = New IntPtr(MA_NOACTIVATE)
            Return
        End If
        MyBase.WndProc(m)
    End Sub

    ''' <summary>Shows <paramref name="source"/> (premultiplied ARGB) at <paramref name="screenLocation"/>, over
    ''' <paramref name="host"/>, faded to <paramref name="alpha"/> of 255.</summary>
    Public Sub Present(host As Form, source As Bitmap, screenLocation As Point, alpha As Byte)
        Dim reordered As Boolean = False
        If Owner IsNot host Then Owner = host
        If TopMost <> host.TopMost Then
            TopMost = host.TopMost
            reordered = True
        End If
        If Not Visible Then
            Show()
            reordered = True
        End If
        ' A panel's ink window that comes up or changes band would stand over an open list (L0302).
        If reordered AndAlso TypeOf host IsNot ChromeMenu Then ChromeMenu.RaiseOpenChain()

        Dim screenDc As IntPtr = GetDC(IntPtr.Zero)
        Dim memoryDc As IntPtr = CreateCompatibleDC(screenDc)
        Dim bitmapHandle As IntPtr = source.GetHbitmap(Color.FromArgb(0))
        Dim previous As IntPtr = SelectObject(memoryDc, bitmapHandle)
        Try
            Dim destination As New NativePoint With {.X = screenLocation.X, .Y = screenLocation.Y}
            Dim size As New NativeSize With {.Width = source.Width, .Height = source.Height}
            Dim origin As New NativePoint
            Dim blend As New BlendFunction With {.BlendOp = AC_SRC_OVER, .BlendFlags = 0, .SourceConstantAlpha = alpha, .AlphaFormat = AC_SRC_ALPHA}
            UpdateLayeredWindow(Handle, screenDc, destination, size, memoryDc, origin, 0, blend, ULW_ALPHA)
        Finally
            SelectObject(memoryDc, previous)
            DeleteObject(bitmapHandle)
            DeleteDC(memoryDc)
            ReleaseDC(IntPtr.Zero, screenDc)
        End Try
    End Sub

End Class
#End If
