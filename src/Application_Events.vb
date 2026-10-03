Imports Microsoft.VisualBasic.ApplicationServices
Imports System.Windows.Forms
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Diagnostics

Namespace My
    Partial Friend Class MyApplication

        Shared Sub New()
            RuntimeBootstrap.Initialize()
#If NETFRAMEWORK Then
            ' TLS 1.2 for every HTTPS call this process makes (050 D-07 = E-09). net48 uses
            ' SystemDefault, and on Windows 7 SP1 SChannel's client default is TLS 1.0 unless
            ' the registry was changed by hand - so nuget.org (the VLC and OCR runtimes),
            ' LibreTranslate over https and the Ollama installer all failed on exactly the
            ' machines the x86 exe exists for. It used to be OR-ed in only inside the tessdata
            ' downloader, so it depended on a language pack having downloaded first. OR-ed,
            ' never assigned: whatever the system already allows stays allowed.
            Try
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.ServicePointManager.SecurityProtocol Or System.Net.SecurityProtocolType.Tls12
            Catch
                ' A system without the TLS 1.2 client refuses the value; the old default stays.
            End Try
#End If
            ' The logger is set up in Startup, but the application framework can fail
            ' before Startup runs (its own single-instance pipe, 053 F-03). Without this a
            ' crash there left nothing in current.log.
            AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf EarlyUnhandledException
        End Sub

        ' System.UnhandledExceptionEventArgs: inside Namespace My the bare name is the
        ' application framework's own type of the same name.
        Private Shared Sub EarlyUnhandledException(sender As Object, e As System.UnhandledExceptionEventArgs)
            ' Once Startup has initialised the logger, its own handler reports this.
            If AppFileLogger.IsReady Then Return
            Try
                AppFileLogger.Initialize()
                Dim ex As Exception = TryCast(e.ExceptionObject, Exception)
                If ex IsNot Nothing Then
                    AppFileLogger.LogException("Unhandled exception before Startup; IsTerminating=" & e.IsTerminating.ToString(), ex)
                End If
            Catch
                ' Logging must never add a second failure to the first one.
            End Try
        End Sub

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function AllowSetForegroundWindow(dwProcessId As Integer) As Boolean
        End Function

        ''' <summary>How long a launch keeps trying to hand its file to a viewer that is
        ''' still starting (its window not created, or busy in Form1_Load) before it gives
        ''' up and opens the file itself.</summary>
        Private Const Forward_Deadline_Ms As Integer = 6000
        Private Const Forward_Retry_Pause_Ms As Integer = 250

#If Not NETFRAMEWORK Then
        ''' <summary>
        ''' Modern floating chrome derives metrics from DeviceDpi (002 phase 7).
        ''' PerMonitorV2 keeps it crisp and repositions hosts when crossing monitors.
        ''' The net48 application retains its shipped DPI-unaware behavior.
        ''' </summary>
        Private Sub MyApplication_ApplyApplicationDefaults(sender As Object, e As ApplyApplicationDefaultsEventArgs) Handles Me.ApplyApplicationDefaults
            e.HighDpiMode = System.Windows.Forms.HighDpiMode.PerMonitorV2
            e.Font = New System.Drawing.Font("Microsoft Sans Serif", 8.25F)
        End Sub
#End If

        Private Const WM_COPYDATA_LOCAL As Integer = &H4A

        ''' <summary>
        ''' The viewer ships as TWO exes side by side - FastMediaSorter_LITE.exe
        ''' (the .NET 10 x64 mainline) and FastMediaSorter_x86.exe (the lean net48
        ''' viewer for old/32-bit Windows). They share one mutex and one settings
        ''' hive, i.e. they are ONE app: whichever is launched second forwards its
        ''' file to the running window instead of opening a rival one (which would
        ''' also race on saving settings at exit).
        ''' Process.GetProcessesByName matches on the exe name alone, so the
        ''' forwarding target must be searched under BOTH names - looking only for
        ''' our own name would find nothing when the sibling is the one running,
        ''' and the launch would cancel silently with no window and no file opened.
        '''
        ''' The list itself lives in Common_Module: the archive cache's orphan sweep asks
        ''' the same question from the other side - "does this session directory still
        ''' have an owner?" - and two copies would be one more place to forget when an exe
        ''' is renamed.
        ''' </summary>
        Private Shared ReadOnly Viewer_Process_Names As String() = Common_Module.Viewer_Process_Names

        Friend Shared RunningTargetsFunc As Func(Of Integer, List(Of ViewerProcessTarget)) = Nothing

        ''' <summary>The other viewer processes, under both exe names. The discovery itself (and
        ''' its guarantee that every Process object is disposed and a vanishing target costs only
        ''' itself) is ViewerProcessDiscovery, which the tests drive with fakes.</summary>
        Friend Shared Function GetRunningViewerTargets(current_Id As Integer) As List(Of ViewerProcessTarget)
            If RunningTargetsFunc IsNot Nothing Then
                Return RunningTargetsFunc(current_Id)
            End If

            Return ViewerProcessDiscovery.Discover(
                Viewer_Process_Names, current_Id,
                Function(viewer_Name As String) As IList(Of IViewerProcess)
                    Dim wrapped As New List(Of IViewerProcess)()
                    For Each proc As Process In Process.GetProcessesByName(viewer_Name)
                        wrapped.Add(New RealViewerProcess(proc))
                    Next
                    Return wrapped
                End Function,
                AddressOf GetProcessTopLevelWindows)
        End Function
        ''' <summary>SendMessage with a deadline. A plain SendMessage is synchronous and
        ''' has none: one hung viewer window and the NEW process hung with it, for ever,
        ''' showing nothing.</summary>
        <DllImport("user32.dll", CharSet:=CharSet.Auto)>
        Private Shared Function SendMessageTimeout(hWnd As IntPtr, msg As Integer, wParam As IntPtr,
                                                   ByRef lParam As Main_Form.COPYDATASTRUCT,
                                                   flags As Integer, timeout As Integer,
                                                   ByRef result As IntPtr) As IntPtr
        End Function

        Private Const SMTO_ABORTIFHUNG As Integer = &H2

        Private Delegate Function EnumWindowsProc(hWnd As IntPtr, lParam As IntPtr) As Boolean

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function EnumWindows(callback As EnumWindowsProc, lParam As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef processId As Integer) As Integer
        End Function

        ''' <summary>All top-level windows of a process, including hidden ones -
        ''' Process.MainWindowHandle returns IntPtr.Zero for a window hidden in the
        ''' tray, so WM_COPYDATA forwarding needs this fallback to find a target.</summary>
        Private Shared Function GetProcessTopLevelWindows(process_Id As Integer) As List(Of IntPtr)
            Dim result As New List(Of IntPtr)
            Try
                EnumWindows(Function(hWnd As IntPtr, lParam As IntPtr) As Boolean
                                Dim window_Pid As Integer = 0
                                GetWindowThreadProcessId(hWnd, window_Pid)
                                If window_Pid = process_Id Then result.Add(hWnd)
                                Return True
                            End Function, IntPtr.Zero)
            Catch
            End Try
            Return result
        End Function

        ''' <summary>Counts the top-level viewer windows already owned by other processes.
        ''' Secondary windows use it to cascade their restore position instead of landing
        ''' directly on top of the primary window.</summary>
        Friend Shared Function GetRunningViewerWindowCount() As Integer
            Dim currentId As Integer = 0
            Try
                currentId = Process.GetCurrentProcess().Id
            Catch
            End Try
            Dim count As Integer = 0
            For Each target As ViewerProcessTarget In GetRunningViewerTargets(currentId)
                If target.MainWindowHandle <> IntPtr.Zero Then
                    count += 1
                Else
                    count += target.TopLevelHandles.Count
                End If
            Next
            Return count
        End Function

        Private Sub MyApplication_StartupNextInstance(sender As Object, e As StartupNextInstanceEventArgs) Handles Me.StartupNextInstance
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0004: MyApplication_StartupNextInstance called")

            e.BringToForeground = False

            Dim mainFormInstance As Main_Form = Nothing
            If Application.MainForm IsNot Nothing AndAlso TypeOf Application.MainForm Is Main_Form Then
                mainFormInstance = DirectCast(Application.MainForm, Main_Form)
            End If

            If mainFormInstance Is Nothing Then Return

            Dim fullCommandLine As String = String.Join(" ", e.CommandLine.ToArray()).Trim()

            ' Bare relaunch (no file): the user re-ran the exe just to get the window
            ' back (e.g. it is hidden in the tray or minimized). Restore it with its
            ' current content and focus it - here focus stealing is the whole point.
            If String.IsNullOrEmpty(fullCommandLine) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0006: Bare relaunch - restoring window")
                mainFormInstance.RestoreMainWindow()
                Return
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0005: Processing argument in existing instance")

            Dim wasMinimized As Boolean = (mainFormInstance.WindowState = FormWindowState.Minimized)
            Dim previousForegroundWindowHandle As IntPtr = IntPtr.Zero

            If Not wasMinimized Then
                previousForegroundWindowHandle = Common_Module.GetForegroundWindow()
                If previousForegroundWindowHandle = mainFormInstance.Handle Then
                    previousForegroundWindowHandle = IntPtr.Zero
                End If
            End If

            mainFormInstance.ProcessArgument(fullCommandLine)

            If wasMinimized Then
                Common_Module.ShowWindow(mainFormInstance.Handle, Common_Module.SW_SHOWNOACTIVATE)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " AppEvents: unwraped")
            ElseIf previousForegroundWindowHandle <> IntPtr.Zero Then
                Dim currentForegroundHandle As IntPtr = Common_Module.GetForegroundWindow()
                If currentForegroundHandle = mainFormInstance.Handle Then
                    Common_Module.SetForegroundWindow(previousForegroundWindowHandle)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " AppEvents: try to focus back: " & previousForegroundWindowHandle.ToString())
                End If
            End If
        End Sub

        Private Sub MyApplication_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-1: MyApplication_Startup")

            ' Spec 022 §7.1: Intercept stream catalog CLI switches BEFORE the single-instance
            ' mutex is created or inspected, so CLI operations never forward to a running viewer
            ' window or disturb running GUI instances.
            Dim rawCmdArgs As String() = e.CommandLine.ToArray()
#If NETFRAMEWORK Then
            If Streams.StreamCatalogCli.IsCatalogCommandLine(rawCmdArgs) Then
                Streams.StreamCatalogCli.RefuseNetFramework()
                e.Cancel = True
                Return
            End If
#Else
            If Streams.StreamCatalogCli.IsCatalogCommandLine(rawCmdArgs) Then
                Dim exitCode As Integer = Streams.StreamCatalogCli.Run(rawCmdArgs)
                Environment.ExitCode = exitCode
                e.Cancel = True
                Return
            End If
#End If

            ' VB's IsSingleInstance only works for same exe-path launches. The named
            ' mutex is path-independent (catches debug vs release, a renamed copy, and
            ' the mainline/x86 pair).
            '
            ' Created HERE, as the first thing the process does. It used to be created
            ' in Form1_Load instead, and the check below only opened an existing one: in
            ' the hundreds of milliseconds a self-contained .NET 10 exe needs to reach
            ' its form, a second launch found nothing and became a second full instance.
            ' Both then wrote the same registry hive at exit, whoever was last winning.
            Dim created_New As Boolean = False
            Dim mutex_Error As Exception = Nothing
            Try
                Main_Form.Single_Instance_Mutex = New Mutex(True, Main_Form.app_Mutex_Name, created_New)
            Catch ex As UnauthorizedAccessException
                ' The mutex exists but belongs to an ELEVATED viewer: its token's default
                ' DACL grants only Administrators and SYSTEM, so an ordinary launch may not
                ' open it. That IS the answer "another instance exists" - forward to it; an
                ' elevated primary admits WM_COPYDATA from us (Main_Form.DragDrop.vb). It
                ' used to escape Startup and crash the new process (053 F-03).
                created_New = False
                mutex_Error = ex
            End Try
            MultiWindowPolicy.MarkInstanceRole(created_New)
            AppFileLogger.Initialize()
            If mutex_Error IsNot Nothing Then
                AppFileLogger.WriteLine("Single instance: the mutex belongs to an elevated viewer - forwarding to it")
            End If

            If Not created_New AndAlso Not MultiWindowPolicy.AllowNewWindows() Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-2: Another instance detected via mutex, forwarding args")

                Dim file_Path As String = String.Join(" ", e.CommandLine.ToArray()).Trim()

                ' No file argument = the user simply re-ran the exe. Forward the
                ' "show your window" command instead, so a tray-resident/minimized
                ' instance brings the window (with its current content) back.
                If String.IsNullOrEmpty(file_Path) Then
                    file_Path = Main_Form.Show_Window_Command
                End If

                Dim current_Id As Integer = Process.GetCurrentProcess().Id
                Dim delivered As Boolean = False

                ' Refused, never truncated: a path cut to fit can name a different folder. A
                ' payload that cannot be sent whole is simply not forwarded, and this process
                ' opens the argument itself, as it does whenever nobody takes it.
                Dim payload_Bytes As Byte() = Nothing
                Dim refusal As PayloadRefusal
                If Not CopyDataTransport.TryEncode(file_Path, payload_Bytes, refusal) Then
                    AppFileLogger.WriteLine("Single instance: the argument cannot be forwarded (" & refusal.ToString() & ")")
                Else
                    ' A viewer that is still starting has no window yet, or is busy inside
                    ' Form1_Load and cannot answer: try again for a few seconds rather than
                    ' opening a rival instance at the first silence (S6 Б-38, which 053 F-06
                    ' found defeated). One monotonic deadline for the whole attempt, each send
                    ' bounded by what remains of it; nobody running under either name ends the
                    ' wait at once.
                    Dim forward_Outcome As ForwardOutcome = CopyDataTransport.RunForward(
                        AddressOf MonotonicClock.NowMs,
                        Sub(pause_Ms) Thread.Sleep(pause_Ms),
                        Function(send_Timeout_Ms) TryForwardOnce(payload_Bytes, current_Id, send_Timeout_Ms))
                    delivered = forward_Outcome = ForwardOutcome.Delivered
                End If
                ' Cancel ONLY if the argument really reached someone. It used to be
                ' unconditional, so a renamed exe (no process under either known name),
                ' a first instance whose window did not exist yet, or a hung receiver
                ' all ended the same way: no window, no file, no message. If nobody took
                ' it, carry on and open it ourselves.
                If delivered Then
                    e.Cancel = True
                    Return
                End If

                AppFileLogger.WriteLine("Single instance: no running viewer took the argument - starting normally")
            End If

#If Not NETFRAMEWORK Then
            ' The application theme (spec 060): before the main window exists, so every window
            ' and menu of this process is themed from its first paint. After the forwarding
            ' above - a launch that only hands its file on never shows a window.
            ThemeService.Initialize()
#End If
        End Sub

        ''' <summary>
        ''' One pass over every running viewer process. Acknowledged only when a Main_Form
        ''' answered the tagged forward as handled - a helper window of the process (GDI+,
        ''' broadcast) answers WM_COPYDATA with 0 and no longer counts (053 F-06). NoTarget when
        ''' no process runs under either name: there is nothing to wait for. Each send is
        ''' bounded by <paramref name="send_Timeout_Ms"/>, and the process objects are released
        ''' by GetRunningViewerTargets whatever happens to a target meanwhile.
        ''' </summary>
        Private Shared Function TryForwardOnce(payload_Bytes As Byte(), current_Id As Integer, send_Timeout_Ms As Integer) As ForwardAttempt
            Dim viewer_Found As Boolean = False
            For Each target As ViewerProcessTarget In GetRunningViewerTargets(current_Id)
                viewer_Found = True
                ' A hidden or minimised instance can report MainWindowHandle = 0 - fall
                ' back to the process's top-level windows. Sending to all of them is safe:
                ' only Main_Form's WndProc acts on the tagged message.
                Dim target_Handles As New List(Of IntPtr)()
                If target.MainWindowHandle <> IntPtr.Zero Then
                    target_Handles.Add(target.MainWindowHandle)
                Else
                    target_Handles.AddRange(target.TopLevelHandles)
                End If
                If target_Handles.Count = 0 Then Continue For

                ' The receiver restores and activates its window for a bare relaunch;
                ' without this grant the foreground lock refuses it and only the taskbar
                ' button flashes. We are the foreground process right now, so we may.
                Try
                    AllowSetForegroundWindow(target.Pid)
                Catch ex As Exception
                    AppFileLogger.LogException("Single instance: AllowSetForegroundWindow", ex)
                End Try

                Dim ptr As IntPtr = Marshal.AllocHGlobal(payload_Bytes.Length + 1)
                Try
                    Marshal.Copy(payload_Bytes, 0, ptr, payload_Bytes.Length)
                    Marshal.WriteByte(ptr, payload_Bytes.Length, 0)

                    Dim cds As New Main_Form.COPYDATASTRUCT()
                    cds.dwData = SingleInstanceProtocol.Tag_Pointer
                    cds.cbData = payload_Bytes.Length
                    cds.lpData = ptr

                    For Each target_Handle As IntPtr In target_Handles
                        Dim send_Result As IntPtr = IntPtr.Zero
                        Dim sent As Boolean = SendMessageTimeout(target_Handle, WM_COPYDATA_LOCAL, IntPtr.Zero, cds,
                                                                 SMTO_ABORTIFHUNG, send_Timeout_Ms, send_Result) <> IntPtr.Zero
                        If SingleInstanceProtocol.IsDelivered(sent, send_Result) Then
                            AppFileLogger.WriteLine("Single instance: argument forwarded to a running viewer")
                            Return ForwardAttempt.Acknowledged
                        End If
                    Next
                Catch ex As Exception
                    AppFileLogger.LogException("Single instance: forwarding to a running viewer", ex)
                Finally
                    Marshal.FreeHGlobal(ptr)
                End Try
            Next
            Return If(viewer_Found, ForwardAttempt.NotAcknowledged, ForwardAttempt.NoTarget)
        End Function
    End Class
End Namespace
