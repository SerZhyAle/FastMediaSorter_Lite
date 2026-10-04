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
    Private inkPlain As Bitmap
    Private inkOpacityValue As Double = 1.0R
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
            If inkPlain Is Nothing OrElse inkPlain.Width <> inkBitmap.Width OrElse inkPlain.Height <> inkBitmap.Height Then
                If inkPlain IsNot Nothing Then inkPlain.Dispose()
                inkPlain = New Bitmap(inkBitmap.Width, inkBitmap.Height, PixelFormat.Format32bppPArgb)
            End If
            Using g As Graphics = Graphics.FromImage(inkPlain)
                g.Clear(Color.Transparent)
                ' The surface may be taller than the window (a scrolled menu): the window clips it.
                g.SetClip(New Rectangle(Point.Empty, ClientSize))
                g.TranslateTransform(panelSurface.Left, panelSurface.Top)
                panelSurface.PaintInk(g)
            End Using
            ' The ink is drawn over whatever picture lies under a translucent panel, so every glyph and
            ' caption carries an outline in the opposite tone and reads on a light or a dark picture alike.
            ChromeInkOutline.Compose(inkPlain, inkBitmap, ChromeInkOutline.OutlineFor(panelSurface.Theme.Foreground),
                                     ChromeInkOutline.RadiusFor(panelSurface.ScaleFactor))
            inkWindow.Present(Me, inkBitmap, PointToScreen(Point.Empty), CByte(Math.Round(inkOpacityValue * 255.0R)))
        Catch ex As Exception
            ' The ink is an enhancement over the panel; a failure here must not take the panel down.
            Debug.WriteLine("w2602: chrome ink layer failed: " & ex.Message)
        Finally
            inkRefreshing = False
        End Try
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
            If inkPlain IsNot Nothing Then inkPlain.Dispose() : inkPlain = Nothing
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
''' The outline that keeps a floating panel's glyphs and captions readable on any picture: the ink's own
''' silhouette, in the tone opposite to the ink, grown by a pixel or two on every side and put under it.
''' Pure bitmap work (no window), so it is tested on bitmaps.
''' </summary>
Friend NotInheritable Class ChromeInkOutline

    Private Sub New()
    End Sub

    ''' <summary>Black around light ink, white around dark ink.</summary>
    Public Shared Function OutlineFor(ink As Color) As Color
        Return If(ChromeTheme.IsDark(ink), Color.White, Color.Black)
    End Function

    ''' <summary>How far the outline reaches, in device pixels: one at 100 %, growing with the panel scale.</summary>
    Public Shared Function RadiusFor(scale As Single) As Integer
        Return Math.Max(1, Math.Min(3, CInt(Math.Round(scale))))
    End Function

    ''' <summary>Replaces <paramref name="target"/> with the outline of <paramref name="ink"/> under the ink
    ''' itself. Both are premultiplied ARGB of one size; the target is cleared first.</summary>
    Public Shared Sub Compose(ink As Bitmap, target As Bitmap, outline As Color, radius As Integer)
        Using mask As Bitmap = OutlineMask(ink, outline)
            Using g As Graphics = Graphics.FromImage(target)
                g.Clear(Color.Transparent)
                g.CompositingMode = CompositingMode.SourceOver
                g.InterpolationMode = InterpolationMode.NearestNeighbor
                g.PixelOffsetMode = PixelOffsetMode.Half
                For dy As Integer = -radius To radius
                    For dx As Integer = -radius To radius
                        If (dx <> 0 OrElse dy <> 0) AndAlso dx * dx + dy * dy <= radius * radius + 1 Then
                            g.DrawImageUnscaled(mask, dx, dy)
                        End If
                    Next
                Next
                g.DrawImageUnscaled(ink, 0, 0)
            End Using
        End Using
    End Sub

    ''' <summary>The ink's alpha channel filled with one colour (premultiplied, so the colour channels follow the alpha).</summary>
    Friend Shared Function OutlineMask(ink As Bitmap, color As Color) As Bitmap
        Dim result As New Bitmap(ink.Width, ink.Height, PixelFormat.Format32bppPArgb)
        Dim area As New Rectangle(0, 0, ink.Width, ink.Height)
        Dim source As BitmapData = ink.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb)
        Dim destination As BitmapData = result.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb)
        Try
            Dim bytes(Math.Abs(source.Stride) * source.Height - 1) As Byte
            Marshal.Copy(source.Scan0, bytes, 0, bytes.Length)
            For index As Integer = 0 To bytes.Length - 4 Step 4
                Dim a As Integer = bytes(index + 3)
                bytes(index) = CByte(color.B * a \ 255)
                bytes(index + 1) = CByte(color.G * a \ 255)
                bytes(index + 2) = CByte(color.R * a \ 255)
            Next
            Marshal.Copy(bytes, 0, destination.Scan0, bytes.Length)
        Finally
            ink.UnlockBits(source)
            result.UnlockBits(destination)
        End Try
        Return result
    End Function

    ''' <summary>Text with the same outline, for a caption that is drawn by a control of its own
    ''' (the recipients table's buttons) instead of by the ink layer.</summary>
    Public Shared Sub DrawText(g As Graphics, text As String, font As Font, bounds As RectangleF, ink As Color,
                               format As StringFormat, radius As Integer)
        Dim state As Drawing.Text.TextRenderingHint = g.TextRenderingHint
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
        Try
            Dim outline As Color = OutlineFor(ink)
            Using brush As New SolidBrush(outline)
                For dy As Integer = -radius To radius
                    For dx As Integer = -radius To radius
                        If (dx <> 0 OrElse dy <> 0) AndAlso dx * dx + dy * dy <= radius * radius + 1 Then
                            g.DrawString(text, font, brush, New RectangleF(bounds.X + dx, bounds.Y + dy, bounds.Width, bounds.Height), format)
                        End If
                    Next
                Next
            End Using
            Using brush As New SolidBrush(ink)
                g.DrawString(text, font, brush, bounds, format)
            End Using
        Finally
            g.TextRenderingHint = state
        End Try
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
        If Owner IsNot host Then Owner = host
        If TopMost <> host.TopMost Then TopMost = host.TopMost
        If Not Visible Then Show()

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
