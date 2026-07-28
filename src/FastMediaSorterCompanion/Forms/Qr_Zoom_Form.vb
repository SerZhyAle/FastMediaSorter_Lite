Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>
''' "QR-код крупно" - click on any QR PictureBox (Share tab LAN/Internet pages,
''' share wizard) opens the code in a separate window at 4x the source box size
''' (clamped to the screen working area), so a phone camera grabs it from
''' further away. The image is CLONED: async status polls keep pumping under the
''' modal loop and may rebuild/dispose the original while this window is open.
'''
''' The entry size is not always enough (a dim camera, a phone held across the
''' room), so the window can grow two ways and they compose:
''' - LEFT CLICK on the code steps the size: x2, x2, .. then one last step to the
'''   full working area, then back to the entry size. A click is therefore never a
'''   dead action, and the cycle needs no extra chrome.
''' - The frame is SIZABLE and stays SQUARE while dragged (see <c>WM_SIZING</c>
'''   below), so a free resize can never letterbox the code inside white bars.
''' Dismiss with Esc/Enter, the close box, or a right click on the image (left
''' click zooms now, so the historical click-to-close moved to the right button).
''' </summary>
Public Class Qr_Zoom_Form
    Inherits Form

    ''' <summary>Smallest client square the window may be dragged/stepped down to.</summary>
    Private Const MinClientSide As Integer = 160
    ''' <summary>Breathing room kept between the maximised-by-click frame and the
    ''' working-area edges (the window is still a normal, movable frame there).</summary>
    Private Const ScreenMargin As Integer = 24

    Private ReadOnly _pic As PictureBox
    Private ReadOnly _baseSide As Integer   ' entry size - the click cycle wraps back to it
    Private _iconHandle As IntPtr

    ''' <summary>PictureBox that upscales the code with NEAREST-NEIGHBOUR interpolation:
    ''' a QR is hard black-and-white modules, and the default smoothing turns their edges
    ''' into grey gradients exactly when the window is blown up for a distant camera.
    ''' The mode must be set on the <c>Graphics</c> inside <c>OnPaint</c> BEFORE the base
    ''' draws the zoomed image - the control's Paint event fires afterwards, too late.</summary>
    Private NotInheritable Class QrBox
        Inherits PictureBox

        Protected Overrides Sub OnPaint(pe As PaintEventArgs)
            If pe IsNot Nothing AndAlso pe.Graphics IsNot Nothing Then
                pe.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor
                pe.Graphics.PixelOffsetMode = PixelOffsetMode.Half
            End If
            MyBase.OnPaint(pe)
        End Sub
    End Class

    Private Sub New(img As Image, clientSide As Integer)
        _baseSide = clientSide
        Me.Text = Localization.T("QR-код - клик увеличивает, Esc закрывает")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MaximizeBox = True
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.BackColor = Color.White
        Me.ClientSize = New Size(clientSide, clientSide)
        Me.KeyPreview = True

        _pic = New QrBox With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom,
            .Image = img, .BackColor = Color.White, .Cursor = Cursors.Hand}
        AddHandler _pic.MouseClick, AddressOf OnImageMouseClick
        Controls.Add(_pic)

        AddHandler Me.KeyDown, Sub(sender As Object, e As KeyEventArgs)
                                   If e.KeyCode = Keys.Escape OrElse e.KeyCode = Keys.Enter Then Me.Close()
                               End Sub
        AddHandler Me.FormClosed, Sub(sender As Object, e As FormClosedEventArgs)
                                      Dim old As Image = _pic.Image
                                      _pic.Image = Nothing
                                      If old IsNot Nothing Then old.Dispose()
                                      ShareIcons.FreeIcon(Me.Icon, _iconHandle)
                                  End Sub
    End Sub

#Region "Click zoom"

    Private Sub OnImageMouseClick(sender As Object, e As MouseEventArgs)
        If e Is Nothing Then Return
        If e.Button = MouseButtons.Left Then
            StepZoom()
        ElseIf e.Button = MouseButtons.Right OrElse e.Button = MouseButtons.Middle Then
            Me.Close()
        End If
    End Sub

    ''' <summary>One step of the click cycle: double the square, then (when doubling would
    ''' overshoot) one final step to the whole working area, then back to the entry size.</summary>
    Private Sub StepZoom()
        If Me.WindowState <> FormWindowState.Normal Then Me.WindowState = FormWindowState.Normal

        Dim maxSide As Integer = MaxClientSide()
        Dim current As Integer = Math.Max(Me.ClientSize.Width, Me.ClientSize.Height)
        Dim target As Integer
        If current * 2 <= maxSide Then
            target = current * 2
        ElseIf current < maxSide Then
            target = maxSide
        Else
            target = Math.Min(_baseSide, maxSide)   ' at the ceiling - wrap back to the entry size
        End If

        If target <> current Then ResizeSquare(target)
    End Sub

    ''' <summary>Largest square client area whose whole frame still fits the monitor.</summary>
    Private Function MaxClientSide() As Integer
        Dim wa As Rectangle = DpiLayout.WorkingAreaFor(Me)
        Dim chrome As Size = ChromeSize()
        Dim side As Integer = Math.Min(wa.Width - chrome.Width, wa.Height - chrome.Height) - ScreenMargin
        Return Math.Max(side, MinClientSide)
    End Function

    ''' <summary>Non-client overhead (border + title bar) of the current frame.</summary>
    Private Function ChromeSize() As Size
        Return New Size(Math.Max(Me.Width - Me.ClientSize.Width, 0), Math.Max(Me.Height - Me.ClientSize.Height, 0))
    End Function

    ''' <summary>Applies a square client size while keeping the window's centre put, then
    ''' nudges the frame fully back on-screen (growing off the edge would hide the code).</summary>
    Private Sub ResizeSquare(side As Integer)
        Dim centre As New Point(Me.Left + Me.Width \ 2, Me.Top + Me.Height \ 2)
        Me.ClientSize = New Size(side, side)
        Me.Location = New Point(centre.X - Me.Width \ 2, centre.Y - Me.Height \ 2)
        DpiLayout.NudgeOnScreen(Me, DpiLayout.WorkingAreaFor(Me))
    End Sub

#End Region

#Region "Square resize"

    Private Const WM_SIZING As Integer = &H214
    Private Const WMSZ_LEFT As Integer = 1
    Private Const WMSZ_RIGHT As Integer = 2
    Private Const WMSZ_TOP As Integer = 3
    Private Const WMSZ_TOPLEFT As Integer = 4
    Private Const WMSZ_TOPRIGHT As Integer = 5
    Private Const WMSZ_BOTTOM As Integer = 6
    Private Const WMSZ_BOTTOMLEFT As Integer = 7

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeRect
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    ''' <summary>Keeps the drag square. WinForms has no aspect-ratio lock, so the proportion is
    ''' held where Windows offers the drag rectangle for editing - <c>WM_SIZING</c>. The side the
    ''' user grabbed drives the new side (a corner takes the larger of the two), and only the
    ''' edges NOT being dragged move, so the grabbed corner stays under the cursor.</summary>
    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_SIZING AndAlso m.LParam <> IntPtr.Zero Then
            Dim r As NativeRect = CType(Marshal.PtrToStructure(m.LParam, GetType(NativeRect)), NativeRect)
            Dim chrome As Size = ChromeSize()
            Dim edge As Integer = m.WParam.ToInt32()

            Dim clientW As Integer = (r.Right - r.Left) - chrome.Width
            Dim clientH As Integer = (r.Bottom - r.Top) - chrome.Height
            Dim side As Integer
            Select Case edge
                Case WMSZ_LEFT, WMSZ_RIGHT : side = clientW
                Case WMSZ_TOP, WMSZ_BOTTOM : side = clientH
                Case Else : side = Math.Max(clientW, clientH)   ' corners
            End Select
            If side < MinClientSide Then side = MinClientSide

            Select Case edge
                Case WMSZ_LEFT, WMSZ_TOPLEFT, WMSZ_BOTTOMLEFT
                    r.Left = r.Right - (side + chrome.Width)
                Case Else
                    r.Right = r.Left + side + chrome.Width
            End Select
            Select Case edge
                Case WMSZ_TOP, WMSZ_TOPLEFT, WMSZ_TOPRIGHT
                    r.Top = r.Bottom - (side + chrome.Height)
                Case Else
                    r.Bottom = r.Top + side + chrome.Height
            End Select

            Marshal.StructureToPtr(r, m.LParam, True)
            m.Result = New IntPtr(1)
            Return
        End If
        MyBase.WndProc(m)
    End Sub

#End Region

    ''' <summary>Opens the QR shown in <paramref name="source"/> in a modal window
    ''' at 4x the box size, clamped to the screen (clicks grow it further from there).
    ''' No-op when the box is empty.</summary>
    Public Shared Sub ShowZoomed(owner As Form, source As PictureBox)
        If source Is Nothing OrElse source.Image Is Nothing Then Return
        Dim img As Image
        Try
            img = New Bitmap(source.Image)
        Catch
            Return ' the image was disposed under us mid-click - just skip
        End Try

        Dim side As Integer = Math.Max(source.ClientSize.Width, source.ClientSize.Height) * 4
        Try
            Dim wa As Rectangle = Screen.FromControl(source).WorkingArea
            side = Math.Min(side, Math.Min(wa.Width, wa.Height) - 80)
        Catch
        End Try
        If side < 120 Then side = 120

        Using dlg As New Qr_Zoom_Form(img, side)
            dlg.ShowDialog(owner)
        End Using
    End Sub

    ''' <summary>Opens a QR image directly (no source PictureBox) - used by the tray
    ''' "Показать штрихкод" item and the package wizard, which build the code on demand.
    ''' Sized to a large share of the owner's screen so a phone camera grabs it easily;
    ''' a click on the code enlarges it further. The image is cloned; the caller keeps
    ''' ownership of the one it passes in.</summary>
    Public Shared Sub ShowImage(owner As Form, img As Image)
        If img Is Nothing Then Return
        Dim clone As Image
        Try
            clone = New Bitmap(img)
        Catch
            Return
        End Try

        Dim side As Integer = 560
        Try
            Dim wa As Rectangle = If(owner IsNot Nothing AndAlso owner.IsHandleCreated,
                                     Screen.FromControl(owner).WorkingArea, Screen.PrimaryScreen.WorkingArea)
            side = Math.Min(Math.Min(wa.Width, wa.Height) - 80, 720)
        Catch
        End Try
        If side < 200 Then side = 200

        Using dlg As New Qr_Zoom_Form(clone, side)
            If owner IsNot Nothing AndAlso owner.Visible AndAlso owner.IsHandleCreated Then
                dlg.ShowDialog(owner)
            Else
                dlg.ShowDialog()   ' owner hidden (tray-resident) - stand-alone modal
            End If
        End Using
    End Sub

End Class
