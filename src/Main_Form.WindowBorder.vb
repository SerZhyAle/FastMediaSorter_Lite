#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' The borderless main window of the modern x64 viewer (188 §6, 2026-10-12 owner revision).
'
' The native caption is removed by claiming the WHOLE window as client area in WM_NCCALCSIZE
' while keeping the Sizable window styles: no non-client strip remains for DWM to draw a caption
' or the min/max/close buttons into, no frame band remains unpainted at the window's edge - the
' media owns every pixel of the window (the owner's ruling: the resize frame is VIRTUAL, a
' colourless reaction zone, never a visible border). WS_THICKFRAME keeps the DWM shadow, the
' snap target and the animations; WS_SYSMENU keeps Alt+F4 and Alt+Space. FormBorderStyle.None
' (full screen) is untouched - there the whole window is the media anyway.
'
' With no real frame left, Windows itself can no longer answer the edges, so the band is virtual:
'   * the band's hit code (ChromeEdgeHitCode) classifies a client point against the invisible
'     WindowResizeBand at the window's edges and corners;
'   * WM_NCHITTEST on the form answers with it where the form itself is hit-tested;
'   * an application message filter (ChromeEdgeFilter) redirects a left press that lands in
'     the band - over the media panel AND its children, which swallow the form's own
'     WM_NCHITTEST - into the native WM_NCLBUTTONDOWN sizing loop, and shows the sizing
'     cursor while the pointer hovers there. Presses over the floating panels (toolbar rows,
'     status, window commands) are left to those panels: their buttons keep every pixel.
'
' Everything is called from Main_Form.WndProc; the single-instance and media-key handling
' there is untouched.

Partial Public Class Main_Form

    Private Const WmNcCalcSize As Integer = &H83
    Private Const WmNCHitTest As Integer = &H84
    Private Const WmGetMinMaxInfo As Integer = &H24
    Private Const WmNcLButtonDown As Integer = &HA1
    Private Const WmMouseMove As Integer = &H200
    Private Const WmLeftButtonDown As Integer = &H201

    Private Const HtClient As Integer = 1
    Private Const HtCaption As Integer = 2
    Private Const HtLeft As Integer = 10
    Private Const HtRight As Integer = 11
    Private Const HtTop As Integer = 12
    Private Const HtTopLeft As Integer = 13
    Private Const HtTopRight As Integer = 14
    Private Const HtBottom As Integer = 15
    Private Const HtBottomLeft As Integer = 16
    Private Const HtBottomRight As Integer = 17

    ''' <summary>GetAncestor: the root window of the given window's parent chain.</summary>
    Private Const GaRoot As Integer = 2

    ''' <summary>DWMWA_BORDER_COLOR: the colour of the 1-px outline Windows 11 draws around a
    ''' window. DWMWA_COLOR_NONE asks for no outline at all: the window is visually frameless
    ''' on every side (the resize band is colourless, and a hairline is still a frame).</summary>
    Private Const DwmwaBorderColor As Integer = 34
    Private Const DwmwaColorNone As Integer = -2

    Private Const SmCxSizeFrame As Integer = 32
    Private Const SmCySizeFrame As Integer = 33
    Private Const SmCxPaddedBorder As Integer = 92

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeRect
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NcCalcSizeParams
        Public Rect0 As NativeRect
        Public Rect1 As NativeRect
        Public Rect2 As NativeRect
        Public WindowPos As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativePoint
        Public X As Integer
        Public Y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MinMaxInfo
        Public Reserved As NativePoint
        Public MaxSize As NativePoint
        Public MaxPosition As NativePoint
        Public MinTrackSize As NativePoint
        Public MaxTrackSize As NativePoint
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(index As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetricsForDpi(index As Integer, dpi As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetDpiForWindow(hwnd As IntPtr) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetAncestor(hwnd As IntPtr, flags As Integer) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetCursor(hCursor As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ReleaseCapture() As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private chrome_Edge_Filter As ChromeEdgeFilter
    Private chrome_Edge_Installed As Boolean
    Private chrome_Border_Color_Applied As Boolean

    ''' <summary>True when the message belongs to the borderless window and was fully handled
    ''' (the caller returns without the base class). Called first in Main_Form.WndProc.</summary>
    Friend Function ChromeWindowBorderWndProc(ByRef m As Message) As Boolean
        ChromeEnsureEdgeFilter()
        Select Case m.Msg
            Case WmNcCalcSize
                If m.WParam = IntPtr.Zero Then Return False
                ChromeWindowNcCalcSize(m)
                Return True
            Case WmNcHitTest
                Return ChromeWindowNcHitTest(m)
            Case WmGetMinMaxInfo
                ChromeWindowMinMax(m)
                Return True
        End Select
        Return False
    End Function

    ''' <summary>Installs the invisible resize band's message filter and asks DWM for no window
    ''' outline, once, on the first border message. Harmless before the handle exists.</summary>
    Private Sub ChromeEnsureEdgeFilter()
        If chrome_Edge_Installed OrElse Not IsHandleCreated Then Return
        chrome_Edge_Installed = True
        chrome_Edge_Filter = New ChromeEdgeFilter(Me)
        Application.AddMessageFilter(chrome_Edge_Filter)
        If Not chrome_Border_Color_Applied Then
            chrome_Border_Color_Applied = True
            Dim none As Integer = DwmwaColorNone
            Try
                ' Windows 10 answers E_INVALIDARG (no such attribute) - no outline is drawn there anyway.
                DwmSetWindowAttribute(Handle, DwmwaBorderColor, none, 4)
            Catch
            End Try
        End If
    End Sub

    ''' <summary>The client area IS the whole window in the normal state - the media owns every
    ''' pixel and DWM has no non-client strip left to draw a caption or the window buttons into.
    ''' A maximized window overhangs the work area by exactly the sizing frame, so there - and
    ''' only there - the client is inset back to fill the work area (the overhang is off-screen;
    ''' a maximized window is not resizable, so nothing is lost).</summary>
    Private Sub ChromeWindowNcCalcSize(ByRef m As Message)
        If FormBorderStyle = FormBorderStyle.Sizable Then
            Dim inset As Size = If(WindowState = FormWindowState.Maximized, ChromeFrameThickness(), Size.Empty)
            Dim params As NcCalcSizeParams = Marshal.PtrToStructure(Of NcCalcSizeParams)(m.LParam)
            params.Rect0 = New NativeRect With {
                .Left = params.Rect0.Left + inset.Width,
                .Top = params.Rect0.Top + inset.Height,
                .Right = params.Rect0.Right - inset.Width,
                .Bottom = params.Rect0.Bottom - inset.Height}
            Marshal.StructureToPtr(params, m.LParam, False)
        End If
        m.Result = IntPtr.Zero
    End Sub

    ''' <summary>The invisible resize band inside the client: WindowResizeBand at this window's
    ''' DPI, never below the frame Windows itself keeps.</summary>
    Private Function ChromeFrameBand() As Size
        Dim frame As Size = ChromeFrameThickness()
        Dim band As Integer = UiScale.Px(ChromeMetrics.WindowResizeBand, UiScale.Factor(0, CInt(GetDpiForWindow(Handle))))
        Return New Size(Math.Max(frame.Width, band), Math.Max(frame.Height, band))
    End Function

    ''' <summary>The hit code of a client point that falls in the invisible resize band at the
    ''' window's edges or corners, 0 when it does not. Pure geometry: the caller decides what
    ''' the code becomes - a hit-test answer, a redirected press, a sizing cursor. Never in any
    ''' state but the normal one (maximized and full-screen windows are not resizable).</summary>
    Friend Function ChromeEdgeHitCode(clientPoint As Point) As Integer
        If FormBorderStyle <> FormBorderStyle.Sizable OrElse WindowState <> FormWindowState.Normal OrElse Not IsHandleCreated Then Return 0
        Dim band As Size = ChromeFrameBand()
        Dim atLeft As Boolean = clientPoint.X < band.Width
        Dim atRight As Boolean = clientPoint.X >= ClientSize.Width - band.Width
        Dim atTop As Boolean = clientPoint.Y < band.Height
        Dim atBottom As Boolean = clientPoint.Y >= ClientSize.Height - band.Height
        If atLeft AndAlso atTop Then Return HtTopLeft
        If atRight AndAlso atTop Then Return HtTopRight
        If atLeft AndAlso atBottom Then Return HtBottomLeft
        If atRight AndAlso atBottom Then Return HtBottomRight
        If atLeft Then Return HtLeft
        If atRight Then Return HtRight
        If atTop Then Return HtTop
        If atBottom Then Return HtBottom
        Return 0
    End Function

    ''' <summary>Where the form itself is hit-tested (a point no child or floating panel covers),
    ''' the band answers as the frame it replaces. Elsewhere the base class's HTCLIENT stands.</summary>
    Private Function ChromeWindowNcHitTest(ByRef m As Message) As Boolean
        If FormBorderStyle <> FormBorderStyle.Sizable OrElse WindowState <> FormWindowState.Normal OrElse Not IsHandleCreated Then Return False
        Dim raw As Integer = m.LParam.ToInt32()
        ' Sign-extend both 16-bit words: a monitor left of or above the primary gives
        ' NEGATIVE screen coordinates, whose unsigned low word is above 32767 - and the
        ' naive CShort(raw And &HFFFF) then threw OverflowException on every WM_NCHITTEST,
        ' killing the resize band and flooding the status line (2026-10-12).
        Dim screenPoint As New Point((raw << 16) >> 16, raw >> 16)
        Dim code As Integer = ChromeEdgeHitCode(PointToClient(screenPoint))
        If code = 0 Then Return False
        m.Result = New IntPtr(code)
        Return True
    End Function

    ''' <summary>A left press that lands in the invisible band becomes the native sizing drag:
    ''' the system owns the whole loop - the live size, the cursor, the Esc cancel. The press is
    ''' swallowed, so the media surface never sees it. Only the main window's own tree qualifies
    ''' (the floating panels keep their presses: their buttons own every pixel); anything else -
    ''' dialogs, menus, other windows - passes untouched.</summary>
    Friend Function ChromeEdgePressRedirect(m As Message) As Boolean
        If m.HWnd = IntPtr.Zero OrElse GetAncestor(m.HWnd, GaRoot) <> Handle Then Return False
        Dim code As Integer = ChromeEdgeHitCode(PointToClient(Cursor.Position))
        If code = 0 Then Return False
        Dim p As Point = Cursor.Position
        Dim lParam As New IntPtr(((p.Y And &HFFFF) << 16) Or (p.X And &HFFFF))
        SendMessage(Handle, WmNcLButtonDown, New IntPtr(code), lParam)
        Return True
    End Function

    ''' <summary>The sizing cursor while the pointer hovers in the band, over any surface of the
    ''' window's tree (the media panel's children swallow WM_SETCURSOR before the form sees it).
    ''' The move itself is passed on; when the pointer leaves the band the next move lets the
    ''' child's own cursor answer again.</summary>
    Friend Sub ChromeEdgeHoverCursor(m As Message)
        If m.HWnd = IntPtr.Zero OrElse GetAncestor(m.HWnd, GaRoot) <> Handle Then Return
        Dim code As Integer = ChromeEdgeHitCode(PointToClient(Cursor.Position))
        If code = 0 Then Return
        Dim edgeCursor As Cursor =
            If(code = HtLeft OrElse code = HtRight, Cursors.SizeWE,
            If(code = HtTop OrElse code = HtBottom, Cursors.SizeNS,
            If(code = HtTopLeft OrElse code = HtBottomRight, Cursors.SizeNWSE, Cursors.SizeNESW)))
        SetCursor(edgeCursor.Handle)
    End Sub

    ''' <summary>Watches the thread's queue for the presses and hovers of the invisible resize
    ''' band (188 §6): the media surfaces cover the whole window and swallow the form's own
    ''' WM_NCHITTEST, so the band lives at the queue level instead. Installed once with the
    ''' first border message of the main window; inert once the window is disposed.</summary>
    Private NotInheritable Class ChromeEdgeFilter
        Implements IMessageFilter

        Private ReadOnly window As Main_Form

        Public Sub New(window As Main_Form)
            Me.window = window
        End Sub

        Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            Dim form As Main_Form = window
            If form Is Nothing OrElse form.IsDisposed OrElse Not form.IsHandleCreated Then Return False
            Select Case m.Msg
                Case WmLeftButtonDown
                    Return form.ChromeEdgePressRedirect(m)
                Case WmMouseMove
                    form.ChromeEdgeHoverCursor(m)
            End Select
            Return False
        End Function

    End Class

    ''' <summary>The window never shrinks below the floating window-controls panel (188 §3.7):
    ''' the minimum tracking size is the panel's own footprint plus a margin around it, at the
    ''' current scale. The base class fills the struct first; this only raises its floor.</summary>
    Private Sub ChromeWindowMinMax(ByRef m As Message)
        MyBase.WndProc(m)
        Dim info As MinMaxInfo = Marshal.PtrToStructure(Of MinMaxInfo)(m.LParam)
        Dim factor As Single = ChromeScale()
        Dim minWidth As Integer = UiScale.Px(ChromeMetrics.WindowControlsLogicalWidth() + 2 * ChromeMetrics.ScreenMargin, factor)
        Dim minHeight As Integer = UiScale.Px(ChromeMetrics.WindowControlsLogicalHeight() + 2 * ChromeMetrics.ScreenMargin, factor)
        Dim changed As Boolean = False
        If info.MinTrackSize.X < minWidth Then
            info.MinTrackSize.X = minWidth
            changed = True
        End If
        If info.MinTrackSize.Y < minHeight Then
            info.MinTrackSize.Y = minHeight
            changed = True
        End If
        If changed Then Marshal.StructureToPtr(info, m.LParam, False)
    End Sub

    ''' <summary>The sizing frame's thickness in this window's pixels: the visible border Windows
    ''' 10+ does not draw plus the padded border. Asked per window DPI where the OS knows how.</summary>
    Private Function ChromeFrameThickness() As Size
        Dim dpi As Integer = 0
        If IsHandleCreated Then dpi = CInt(GetDpiForWindow(Handle))
        If dpi > 0 Then
            Dim width As Integer = GetSystemMetricsForDpi(SmCxSizeFrame, dpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi)
            Dim height As Integer = GetSystemMetricsForDpi(SmCySizeFrame, dpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi)
            Return New Size(Math.Max(1, width), Math.Max(1, height))
        End If
        Return New Size(Math.Max(1, GetSystemMetrics(SmCxSizeFrame) + GetSystemMetrics(SmCxPaddedBorder)),
                        Math.Max(1, GetSystemMetrics(SmCySizeFrame) + GetSystemMetrics(SmCxPaddedBorder)))
    End Function

    ' --- the panel's window commands (188 §5) --------------------------------------------------

    ''' <summary>Drag the viewer through the native move loop from the controls panel's grip
    ''' (188 §5): the pointer keeps its grip point, edge snap works, another display can be
    ''' reached and dragging a maximized window restores it under the pointer - all Windows'
    ''' own behaviour. Off during full screen (there is nothing to move to) and while minimized.</summary>
    Friend Sub ChromeBeginWindowDrag()
        If is_Full_Screen_Mode OrElse WindowState = FormWindowState.Minimized OrElse Not IsHandleCreated Then Return
        ReleaseCapture()
        Dim p As Point = Cursor.Position
        Dim lParam As New IntPtr((p.Y << 16) Or (p.X And &HFFFF))
        SendMessage(Handle, WmNcLButtonDown, New IntPtr(HtCaption), lParam)
    End Sub

    ''' <summary>The grip's double click: toggle normal / maximized (188 §5). Never closes,
    ''' never touches full screen.</summary>
    Friend Sub ChromeToggleMaximized()
        If is_Full_Screen_Mode Then Return
        If WindowState = FormWindowState.Normal Then
            WindowState = FormWindowState.Maximized
        ElseIf WindowState = FormWindowState.Maximized Then
            WindowState = FormWindowState.Normal
        End If
    End Sub

End Class
#End If
