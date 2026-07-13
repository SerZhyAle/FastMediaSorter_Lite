Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Entry point for Fast Media Sorter: Share Manager. Owns single-instance
''' enforcement and the LITE -&gt; Companion wake handshake, then hands off to the
''' tray-resident <see cref="TrayContext"/>. This is the Companion analogue of
''' LITE's <c>Application_Events.vb</c> (mutex + WM_COPYDATA), with the roles
''' reversed: LITE is the sender, Companion the receiver.
''' </summary>
Friend Module Program

    ''' <summary>Frozen technical anchor (CLAUDE.md light-rebrand rule) - never renamed.</summary>
    Public Const MutexName As String = "FastMediaSorterCompanionSingleInstanceMutex"

    ''' <summary>WM_COPYDATA payload meaning "just show your window" - no folder to share.</summary>
    Public Const ShowWindowCommand As String = "::fms-show-window::"

    ''' <summary>Title of the hidden receiver window, used by a second instance to find the first.</summary>
    Friend Const MessageWindowTitle As String = "FastMediaSorterCompanionMessageWindow_{2f6a1c94}"

    <STAThread>
    Friend Sub Main(args As String())
        Dim payload As String = If(args IsNot Nothing AndAlso args.Length > 0 AndAlso Not String.IsNullOrWhiteSpace(args(0)),
                                   args(0), ShowWindowCommand)

        Dim createdNew As Boolean
        Dim mtx As Mutex = Nothing
        Try
            mtx = New Mutex(True, MutexName, createdNew)
        Catch
            createdNew = True
        End Try

        Try
            If Not createdNew Then
                ' Another instance already owns the tray - forward our intent to it and exit.
                ForwardToRunningInstance(payload)
                Return
            End If

            ' Read the shared UI-language flag before any Share text is built, so
            ' Companion matches LITE's language (invariant 9).
            CompanionGlobals.LoadLanguage()

            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Try
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Catch
                ' Older shells may reject PerMonitorV2 at runtime - the manifest still covers it.
            End Try

            ' A bare launch (double-click / autostart / tray) passes the show-window
            ' marker, which TrayContext treats as "no initial folder".
            Dim initialFolder As String = If(String.Equals(payload, ShowWindowCommand, StringComparison.Ordinal), Nothing, payload)
            Using ctx As New TrayContext(initialFolder)
                Application.Run(ctx)
            End Using
        Finally
            If mtx IsNot Nothing Then
                Try : mtx.ReleaseMutex() : Catch : End Try
                mtx.Dispose()
            End If
        End Try
    End Sub

    ' --- WM_COPYDATA forward to the already-running instance ------------------

    Private Sub ForwardToRunningInstance(payload As String)
        Dim hwnd As IntPtr = FindWindowW(Nothing, MessageWindowTitle)
        If hwnd = IntPtr.Zero Then Return

        Dim bytes As Byte() = Encoding.UTF8.GetBytes(payload)
        Dim unmanaged As IntPtr = Marshal.AllocHGlobal(bytes.Length)
        Try
            Marshal.Copy(bytes, 0, unmanaged, bytes.Length)
            Dim cds As New COPYDATASTRUCT With {
                .dwData = IntPtr.Zero,
                .cbData = bytes.Length,
                .lpData = unmanaged
            }
            SendMessageTimeoutW(hwnd, WM_COPYDATA, IntPtr.Zero, cds,
                                SMTO_ABORTIFHUNG, 3000, Nothing)
        Finally
            Marshal.FreeHGlobal(unmanaged)
        End Try
    End Sub

    Friend Const WM_COPYDATA As Integer = &H4A
    Private Const SMTO_ABORTIFHUNG As UInteger = &H2

    <StructLayout(LayoutKind.Sequential)>
    Friend Structure COPYDATASTRUCT
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function FindWindowW(lpClassName As String, lpWindowName As String) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function SendMessageTimeoutW(hWnd As IntPtr, Msg As Integer, wParam As IntPtr,
                                         ByRef lParam As COPYDATASTRUCT, fuFlags As UInteger,
                                         uTimeout As UInteger, ByRef lpdwResult As IntPtr) As IntPtr
    End Function

End Module
