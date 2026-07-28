Imports System.Drawing
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

    ''' <summary>WM_COPYDATA payload meaning "just show your window" - no folder to share.
    ''' Frozen: LITE sends this exact string (Main_Form.ShareLauncher.vb).</summary>
    Public Const ShowWindowCommand As String = "::fms-show-window::"

    ''' <summary>WM_COPYDATA payload meaning "the exe was run again with no request attached".
    ''' Companion-internal: re-running a plain launch while an instance is already up is still
    ''' a plain start, so the running instance applies the same startup option rather than
    ''' popping its window unconditionally.</summary>
    Public Const PlainStartCommand As String = "::fms-plain-start::"

    ''' <summary>Title of the hidden receiver window, used by a second instance to find the first.</summary>
    Friend Const MessageWindowTitle As String = "FastMediaSorterCompanionMessageWindow_{2f6a1c94}"

    ''' <summary>Autostart passes this so a logon launch stays silently in the tray
    ''' (spec §4.5.1). A launch WITHOUT it is no longer "open the window" by itself -
    ''' see <see cref="ShowWindowFlag"/> and the startup option below.</summary>
    Public Const TrayFlag As String = "--tray"

    ''' <summary>Command line form of <see cref="ShowWindowCommand"/>: "the user asked for the
    ''' window", so it opens whatever the "Open the manager window at startup" option says.
    ''' LITE's Share Manager button passes it when it COLD-STARTS us (a running instance gets
    ''' the same intent over WM_COPYDATA instead).</summary>
    Public Const ShowWindowFlag As String = "--show"

    <STAThread>
    Friend Sub Main(args As String())
        ' Sort the arguments into: silent-tray flag, explicit show-window request, folder.
        Dim silentTray As Boolean = False
        Dim windowRequested As Boolean = False
        Dim folder As String = Nothing
        If args IsNot Nothing Then
            For Each a As String In args
                If String.Equals(a, TrayFlag, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase) Then
                    silentTray = True
                ElseIf String.Equals(a, ShowWindowFlag, StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, "/show", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, ShowWindowCommand, StringComparison.Ordinal) Then
                    ' Checked BEFORE the folder branch: the marker has no leading "-",
                    ' so it would otherwise be taken for a path to share.
                    windowRequested = True
                ElseIf folder Is Nothing AndAlso Not String.IsNullOrWhiteSpace(a) AndAlso Not a.StartsWith("-", StringComparison.Ordinal) Then
                    folder = a
                End If
            Next
        End If

        ' What a second instance forwards to the first: the folder, an explicit show-window
        ' request, or the plain-start marker (which the running instance answers per the
        ' startup option). A silent logon launch that finds us already up forwards NOTHING -
        ' there is nothing to do and a boot must stay quiet.
        Dim payload As String
        If Not String.IsNullOrEmpty(folder) Then
            payload = folder
        ElseIf windowRequested Then
            payload = ShowWindowCommand
        ElseIf silentTray Then
            payload = ""
        Else
            payload = PlainStartCommand
        End If

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
                If payload.Length > 0 Then ForwardToRunningInstance(payload)
                Return
            End If

            ' Read the shared UI-language flag before any Share text is built, so
            ' Companion matches LITE's language (invariant 9).
            CompanionGlobals.LoadLanguage()

            ' Upgrade migration (spec §9.3): if logon autostart still points at the
            ' bare worker (old LITE behavior), repoint it at Companion.exe so the
            ' tray icon appears at logon. No-op otherwise.
            AutostartManager.MigrateRunTargetIfNeeded()

            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Try
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Catch
                ' Older shells may reject PerMonitorV2 at runtime - the manifest still covers it.
            End Try
            ' Set the UI font ONCE, app-wide. Doing this (instead of Me.Font on each form)
            ' keeps the AutoScaleMode.Font baseline consistent, so forms scale correctly at
            ' 125/150/175% DPI instead of shrinking (the fixed-ClientSize-came-out-small bug).
            Try
                Application.SetDefaultFont(New Font("Segoe UI", 9.0F))
            Catch
            End Try

            ' Who decides whether the window shows: an EXPLICIT request always wins (a
            ' folder to share, or --show from LITE's Share Manager button), and every
            ' other launch - logon autostart, a double-click on the exe, a script -
            ' obeys the "Open the manager window at startup" option, off by default.
            Using ctx As New TrayContext(folder, windowRequested, silentTray)
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
