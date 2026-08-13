Option Strict On

Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
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
'''
''' The SAME left click also hands the code over as a picture
''' (SPECIFICATION_QR_SAVE_AND_COPY.md): it writes a PNG into
''' Pictures\Fast Media Sorter\ and refreshes the clipboard, so Ctrl+V in a chat
''' pastes the code as an image and Ctrl+V in Explorer drops the file. Not a second
''' gesture and not a button to find - one click does all three. Ctrl+C copies
''' without writing a file, Ctrl+S writes without touching the clipboard.
''' </summary>
Public Class Qr_Zoom_Form
    Inherits Form

    ''' <summary>Smallest client square the window may be dragged/stepped down to.</summary>
    Private Const MinClientSide As Integer = 160
    ''' <summary>Breathing room kept between the maximised-by-click frame and the
    ''' working-area edges (the window is still a normal, movable frame there).</summary>
    Private Const ScreenMargin As Integer = 24

    ''' <summary>Floor for the SAVED picture: a code smaller than this is upscaled by a
    ''' whole-number nearest-neighbour factor, because a messenger's own re-compression
    ''' on top of a small code is what makes it unscannable.</summary>
    Private Const MinSavedSide As Integer = 512
    ''' <summary>Sub-folder created under the user's Pictures folder.</summary>
    Private Const OutputFolderName As String = "Fast Media Sorter"
    ''' <summary>Sub-folder of %TEMP% used when Pictures cannot be written.</summary>
    Private Const FallbackFolderName As String = "FastMediaSorter"
    ''' <summary>How long the result line holds the title bar before it goes back.</summary>
    Private Const FeedbackMs As Integer = 2000

    Private ReadOnly _pic As PictureBox
    Private ReadOnly _baseSide As Integer   ' entry size - the click cycle wraps back to it
    Private ReadOnly _titleText As String   ' restored after a result line has had its 2 seconds
    Private ReadOnly _baseName As String    ' optional caller-supplied name part of the file
    Private ReadOnly _tip As New ToolTip()
    Private ReadOnly _feedbackTimer As New Timer()
    Private _iconHandle As IntPtr

    ''' <summary>File name decided ONCE per window, at the first save: every later click in
    ''' the same window overwrites that same file, so five clicks to enlarge the code leave
    ''' one PNG and not five. A new window starts a new (newly timestamped) name.</summary>
    Private _fileName As String
    ''' <summary>Full path of the file this window wrote - the <c>CF_HDROP</c> entry. Nothing
    ''' until a save actually succeeded: <c>CF_HDROP</c> naming a file that does not exist
    ''' yet is a broken paste.</summary>
    Private _savedPath As String

    ''' <summary>The QR is a credential and Pictures is commonly synchronised, so the user is
    ''' told - once per run of the program, not on every click of a flow whose whole shape is
    ''' "click, click, click".</summary>
    Private Shared _sessionWarned As Boolean

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

    Private Sub New(img As Image, clientSide As Integer, baseName As String)
        _baseSide = clientSide
        _baseName = If(baseName, "")
        _titleText = Localization.T("QR-код - клик увеличивает, сохраняет и копирует; Esc закрывает")
        Me.Text = _titleText
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

        _feedbackTimer.Interval = FeedbackMs
        AddHandler _feedbackTimer.Tick, Sub(sender As Object, e As EventArgs)
                                            _feedbackTimer.Stop()
                                            Me.Text = _titleText
                                        End Sub

        AddHandler Me.KeyDown, AddressOf OnFormKeyDown
        AddHandler Me.FormClosed, Sub(sender As Object, e As FormClosedEventArgs)
                                      _feedbackTimer.Stop()
                                      _feedbackTimer.Dispose()
                                      _tip.Dispose()
                                      Dim old As Image = _pic.Image
                                      _pic.Image = Nothing
                                      If old IsNot Nothing Then old.Dispose()
                                      ShareIcons.FreeIcon(Me.Icon, _iconHandle)
                                  End Sub
    End Sub

    ''' <summary>Esc/Enter close; Ctrl+C copies without writing a file and Ctrl+S writes
    ''' without touching the clipboard - the two halves of what a left click does at once,
    ''' for anyone who wants only one of them.</summary>
    Private Sub OnFormKeyDown(sender As Object, e As KeyEventArgs)
        If e Is Nothing Then Return
        If e.KeyCode = Keys.Escape OrElse e.KeyCode = Keys.Enter Then
            Me.Close()
        ElseIf e.Control AndAlso e.KeyCode = Keys.C Then
            HandOff(saveFile:=False, copyClipboard:=True)
            e.Handled = True
        ElseIf e.Control AndAlso e.KeyCode = Keys.S Then
            HandOff(saveFile:=True, copyClipboard:=False)
            e.Handled = True
        End If
    End Sub

#Region "Click zoom"

    Private Sub OnImageMouseClick(sender As Object, e As MouseEventArgs)
        If e Is Nothing Then Return
        If e.Button = MouseButtons.Left Then
            StepZoom()
            HandOff(saveFile:=True, copyClipboard:=True)
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

#Region "Save and clipboard"

    ''' <summary>Hands the code over as a picture. Every failure is caught here: a modal
    ''' window over a tray-resident app must not die on a full disk, and feedback is a
    ''' tooltip plus a title line - never a MessageBox in a click-click-click flow.</summary>
    Private Sub HandOff(saveFile As Boolean, copyClipboard As Boolean)
        Dim bmp As Bitmap = Nothing
        Try
            bmp = BuildOutput()
            If bmp Is Nothing Then Return

            Dim lines As New List(Of String)()
            Dim savedName As String = Nothing
            Dim saveFailed As Boolean = False
            Dim fallbackDir As String = Nothing

            If saveFile Then
                Try
                    Dim fellBack As Boolean = False
                    Dim written As String = SavePng(bmp, fellBack)
                    _savedPath = written
                    savedName = Path.GetFileName(written)
                    If fellBack Then fallbackDir = Path.GetDirectoryName(written)
                    ' The file name only - never the payload, never the password (spec §7).
                    AppFileLogger.WriteLine("QR image saved: " & savedName)
                Catch
                    saveFailed = True
                End Try
            End If

            ' The file is written BEFORE the clipboard is set, so the CF_HDROP entry always
            ' names a file that exists.
            Dim copied As Boolean = True
            If copyClipboard Then copied = CopyToClipboard(bmp, _savedPath)

            If saveFile AndAlso Not saveFailed Then
                If copyClipboard AndAlso copied Then
                    lines.Add(Localization.TF("Сохранено и скопировано: {0}", savedName))
                Else
                    lines.Add(Localization.TF("Сохранено: {0}", savedName))
                End If
            ElseIf copyClipboard AndAlso copied Then
                lines.Add(Localization.T("Скопировано в буфер обмена"))
            End If
            If saveFailed Then lines.Add(Localization.T("Не удалось сохранить изображение"))
            If copyClipboard AndAlso Not copied Then lines.Add(Localization.T("Не удалось скопировать в буфер обмена"))
            If fallbackDir IsNot Nothing Then
                lines.Add(Localization.TF("Папка «Изображения» недоступна - сохранено в {0}", fallbackDir))
            End If
            If saveFile AndAlso Not saveFailed AndAlso Not _sessionWarned Then
                _sessionWarned = True
                lines.Add(Localization.T("Изображение содержит доступ к вашим папкам - не публикуйте его."))
            End If

            Announce(lines)
        Catch
        Finally
            If bmp IsNot Nothing Then bmp.Dispose()
        End Try
    End Sub

    ''' <summary>The picture that leaves this window: the window's OWN clone of the code (never
    ''' the caller's image, which the async status poll may rebuild under the modal loop), and
    ''' the code's own pixels rather than the zoomed view - the file must not depend on how far
    ''' the user happened to have zoomed.</summary>
    Private Function BuildOutput() As Bitmap
        Return RenderForOutput(_pic.Image)
    End Function

    ''' <summary>Renders the code at its saved size: drawn opaque on white (a DIB carrying an
    ''' alpha channel is what pastes as a black rectangle in some receivers) and upscaled to
    ''' the <see cref="MinSavedSide"/> floor by a WHOLE-NUMBER nearest-neighbour factor - the
    ''' same reason <see cref="QrBox"/> overrides OnPaint: smoothing turns hard modules into
    ''' grey gradients, and a messenger's re-compression on top of that is what makes a code
    ''' unreadable. Never downscales; the quiet zone comes from the generator and is never
    ''' cropped. Nothing when there is no usable source.</summary>
    Friend Shared Function RenderForOutput(src As Image) As Bitmap
        If src Is Nothing OrElse src.Width <= 0 OrElse src.Height <= 0 Then Return Nothing

        Dim shortest As Integer = Math.Min(src.Width, src.Height)
        Dim factor As Integer = 1
        If shortest < MinSavedSide Then factor = CInt(Math.Ceiling(MinSavedSide / CDbl(shortest)))

        Dim outBmp As New Bitmap(src.Width * factor, src.Height * factor,
                                 Imaging.PixelFormat.Format24bppRgb)
        Try
            outBmp.SetResolution(96.0F, 96.0F)
            Using g As Graphics = Graphics.FromImage(outBmp)
                g.InterpolationMode = InterpolationMode.NearestNeighbor
                g.PixelOffsetMode = PixelOffsetMode.Half
                g.Clear(Color.White)
                g.DrawImage(src, New Rectangle(0, 0, outBmp.Width, outBmp.Height))
            End Using
        Catch
            outBmp.Dispose()
            Return Nothing
        End Try
        Return outBmp
    End Function

    ''' <summary>Writes the PNG, preferring Pictures\Fast Media Sorter\ and falling back to
    ''' %TEMP%\FastMediaSorter\ when that folder cannot be created or written. Throws only
    ''' when the fallback fails too.</summary>
    Private Function SavePng(bmp As Bitmap, ByRef usedFallback As Boolean) As String
        Dim name As String = EnsureFileName()
        usedFallback = False
        Try
            Dim pics As String = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            If Not String.IsNullOrEmpty(pics) Then
                Dim dir As String = Path.Combine(pics, OutputFolderName)
                Directory.CreateDirectory(dir)
                Dim target As String = Path.Combine(dir, name)
                bmp.Save(target, Imaging.ImageFormat.Png)
                Return target
            End If
        Catch
        End Try

        usedFallback = True
        Dim tmp As String = Path.Combine(Path.GetTempPath(), FallbackFolderName)
        Directory.CreateDirectory(tmp)
        Dim fallback As String = Path.Combine(tmp, name)
        bmp.Save(fallback, Imaging.ImageFormat.Png)
        Return fallback
    End Function

    ''' <summary>One DataObject carrying every form the receiving application might want:
    ''' a plain opaque bitmap (chats, Word), the exact PNG bytes (applications that prefer
    ''' lossless), and - once a file exists - the file itself (Explorer, mail attachments).
    ''' A clipboard held by another process is a real and common Windows failure, so one
    ''' retry, then report and keep the saved file, which is still useful.</summary>
    Private Shared Function CopyToClipboard(bmp As Bitmap, filePath As String) As Boolean
        Dim data As New DataObject()
        Try
            data.SetData(DataFormats.Bitmap, True, bmp)
            Dim png As New MemoryStream()
            bmp.Save(png, Imaging.ImageFormat.Png)
            png.Position = 0
            data.SetData("PNG", False, png)
            If Not String.IsNullOrEmpty(filePath) AndAlso File.Exists(filePath) Then
                Dim files As New StringCollection()
                files.Add(filePath)
                data.SetFileDropList(files)
            End If
        Catch
            Return False
        End Try

        For attempt As Integer = 1 To 2
            Try
                ' copy:=True flushes the data to the OLE clipboard, so it survives the
                ' Share Manager closing - and lets the bitmap be disposed right after.
                Clipboard.SetDataObject(data, True)
                Return True
            Catch
                If attempt = 1 Then Threading.Thread.Sleep(120)
            End Try
        Next
        Return False
    End Function

    ''' <summary>Latches the per-window file name at the first save (see <see cref="_fileName"/>).</summary>
    Private Function EnsureFileName() As String
        If _fileName Is Nothing Then _fileName = OutputFileName(_baseName, DateTime.Now)
        Return _fileName
    End Function

    ''' <summary><c>fms-qr-&lt;yyyyMMdd-HHmm&gt;.png</c>, in local time, with an optional
    ''' sanitized base name in front of the stamp.</summary>
    Friend Shared Function OutputFileName(baseName As String, whenLocal As DateTime) As String
        Dim stamp As String = whenLocal.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)
        Dim part As String = SanitizeBase(baseName)
        Return If(part.Length = 0, "fms-qr-" & stamp & ".png", "fms-qr-" & part & "-" & stamp & ".png")
    End Function

    ''' <summary>Reduces a caller-supplied name to a safe, short file-name part; an empty
    ''' result falls back to the plain form.</summary>
    Friend Shared Function SanitizeBase(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then Return ""
        Dim sb As New StringBuilder()
        For Each ch As Char In name.Trim()
            If Char.IsLetterOrDigit(ch) Then
                sb.Append(Char.ToLowerInvariant(ch))
            ElseIf sb.Length > 0 AndAlso sb(sb.Length - 1) <> "-"c Then
                sb.Append("-"c)
            End If
            If sb.Length >= 32 Then Exit For
        Next
        Return sb.ToString().Trim("-"c)
    End Function

    ''' <summary>Feedback: a short-lived tooltip by the cursor plus the title bar carrying the
    ''' first line for two seconds. Never a MessageBox.</summary>
    Private Sub Announce(lines As List(Of String))
        If lines Is Nothing OrElse lines.Count = 0 Then Return
        Try
            Me.Text = lines(0)
            _feedbackTimer.Stop()
            _feedbackTimer.Start()

            Dim at As Point = _pic.PointToClient(Cursor.Position)
            If Not _pic.ClientRectangle.Contains(at) Then
                at = New Point(_pic.ClientSize.Width \ 2, _pic.ClientSize.Height \ 2)
            End If
            _tip.Show(String.Join(Environment.NewLine, lines), _pic, at.X + 12, at.Y + 20, FeedbackMs)
        Catch
        End Try
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
    ''' No-op when the box is empty. <paramref name="baseName"/> is an optional, meaningful
    ''' name part for the saved PNG (see <see cref="ShowImage"/>).</summary>
    Public Shared Sub ShowZoomed(owner As Form, source As PictureBox, Optional baseName As String = Nothing)
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

        Using dlg As New Qr_Zoom_Form(img, side, baseName)
            dlg.ShowDialog(owner)
        End Using
    End Sub

    ''' <summary>Opens a QR image directly (no source PictureBox) - used by the tray
    ''' "Показать штрихкод" item and the package wizard, which build the code on demand.
    ''' Sized to a large share of the owner's screen so a phone camera grabs it easily;
    ''' a click on the code enlarges it further. The image is cloned; the caller keeps
    ''' ownership of the one it passes in - and the save path uses THIS window's clone, never
    ''' the caller's image, which the async status poll may rebuild under the modal loop.
    ''' <paramref name="baseName"/> is an optional meaningful name part for the saved PNG
    ''' ("dune" -> fms-qr-dune-20260808-2153.png); empty gives the plain form.</summary>
    Public Shared Sub ShowImage(owner As Form, img As Image, Optional baseName As String = Nothing)
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

        Using dlg As New Qr_Zoom_Form(clone, side, baseName)
            If owner IsNot Nothing AndAlso owner.Visible AndAlso owner.IsHandleCreated Then
                dlg.ShowDialog(owner)
            Else
                dlg.ShowDialog()   ' owner hidden (tray-resident) - stand-alone modal
            End If
        End Using
    End Sub

End Class
