Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Tray-resident application host. Companion is ALWAYS tray-resident (its normal
''' state, not the close-to-tray crutch LITE needed) - so there is no visible main
''' window at startup, just the notify icon and a hidden receiver window for the
''' LITE -&gt; Companion wake handshake (WM_COPYDATA).
''' Ф0 skeleton: a placeholder icon + Open/Exit menu; received payloads surface as
''' a balloon. The real tray menu, wizards and worker control arrive in Ф2/Ф3.
''' </summary>
Friend NotInheritable Class TrayContext
    Inherits ApplicationContext

    Private ReadOnly _notifyIcon As NotifyIcon
    Private ReadOnly _messageWindow As MessageWindow
    Private ReadOnly _trayIcon As Icon
    Private _disposed As Boolean

    Friend Sub New(initialFolder As String)
        _trayIcon = BuildPlaceholderIcon()

        _notifyIcon = New NotifyIcon() With {
            .Icon = _trayIcon,
            .Text = "Fast Media Sorter: Share Manager",
            .Visible = True
        }
        _notifyIcon.ContextMenuStrip = BuildMenu()
        AddHandler _notifyIcon.DoubleClick, Sub() ShowPlaceholder("Open the Share Manager window (Ф2).")

        _messageWindow = New MessageWindow()
        AddHandler _messageWindow.PayloadReceived, AddressOf OnPayloadReceived

        If Not String.IsNullOrEmpty(initialFolder) Then
            ' Ф2 will open the "share this folder" wizard prefilled; Ф0 just proves the arg arrived.
            ShowPlaceholder("Initial folder: " & initialFolder)
        End If
    End Sub

    Private Function BuildMenu() As ContextMenuStrip
        Dim menu As New ContextMenuStrip()

        Dim miOpen As New ToolStripMenuItem("Open Fast Media Sorter: Share Manager")
        miOpen.Font = New Font(menu.Font, FontStyle.Bold)
        AddHandler miOpen.Click, Sub() ShowPlaceholder("Open the Share Manager window (Ф2).")
        menu.Items.Add(miOpen)

        menu.Items.Add(New ToolStripSeparator())

        Dim miExit As New ToolStripMenuItem("Exit")
        AddHandler miExit.Click, Sub() ExitApplication()
        menu.Items.Add(miExit)

        Return menu
    End Function

    Private Sub OnPayloadReceived(payload As String)
        ' Marshal onto the UI thread - WndProc already runs there, but keep the
        ' contract explicit for when Ф2 does real work here.
        If String.Equals(payload, Program.ShowWindowCommand, StringComparison.Ordinal) Then
            ShowPlaceholder("Show window request received.")
        Else
            ShowPlaceholder("Share folder request: " & payload)
        End If
    End Sub

    Private Sub ShowPlaceholder(message As String)
        Try
            _notifyIcon.BalloonTipTitle = "Share Manager"
            _notifyIcon.BalloonTipText = message
            _notifyIcon.ShowBalloonTip(3000)
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
                _notifyIcon.Visible = False
                _notifyIcon.Dispose()
            Catch
            End Try
            Try
                _messageWindow.DestroyHandle()
            Catch
            End Try
            Try
                If _trayIcon IsNot Nothing Then
                    Dim h As IntPtr = _trayIcon.Handle
                    _trayIcon.Dispose()
                    If h <> IntPtr.Zero Then DestroyIcon(h)
                End If
            Catch
            End Try
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>Placeholder blue "share" glyph until the real drawn icon is ported in Ф3.</summary>
    Private Shared Function BuildPlaceholderIcon() As Icon
        Using bmp As New Bitmap(32, 32)
            Using g As Graphics = Graphics.FromImage(bmp)
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.Clear(Color.Transparent)
                Using b As New SolidBrush(Color.FromArgb(30, 120, 220))
                    g.FillEllipse(b, 2, 2, 27, 27)
                End Using
                Using p As New Pen(Color.White, 3)
                    ' A crude four-way arrow suggestion.
                    g.DrawLine(p, 16, 6, 16, 26)
                    g.DrawLine(p, 6, 16, 26, 16)
                End Using
            End Using
            Dim hIcon As IntPtr = bmp.GetHicon()
            ' Clone so the returned Icon owns managed data independent of hIcon;
            ' hIcon is destroyed by the caller in Dispose.
            Using tmp As Icon = Icon.FromHandle(hIcon)
                Return CType(tmp.Clone(), Icon)
            End Using
        End Using
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function DestroyIcon(hIcon As IntPtr) As Boolean
    End Function

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
