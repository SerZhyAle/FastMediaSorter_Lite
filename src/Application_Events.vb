Imports Microsoft.VisualBasic.ApplicationServices
Imports System.Windows.Forms
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Diagnostics

Namespace My
    Partial Friend Class MyApplication

        Shared Sub New()
            RuntimeBootstrap.Initialize()
            AppFileLogger.Initialize()
        End Sub

        Private Const WM_COPYDATA_LOCAL As Integer = &H4A

        <DllImport("user32.dll", CharSet:=CharSet.Auto)>
        Private Shared Function SendMessageCopyData(hWnd As IntPtr, msg As Integer, wParam As IntPtr, ByRef lParam As Main_Form.COPYDATASTRUCT) As Integer
        End Function

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

            ' VB's IsSingleInstance only works for same exe-path launches.
            ' Use the named mutex for a path-independent check (catches debug vs release, XFile, etc.)
            Dim existing_Mutex As Mutex = Nothing
            If Mutex.TryOpenExisting("FastMediaSorterSingleInstanceMutex", existing_Mutex) Then
                existing_Mutex.Close()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-2: Another instance detected via mutex, forwarding args")

                Dim file_Path As String = String.Join(" ", e.CommandLine.ToArray()).Trim()

                ' No file argument = the user simply re-ran the exe. Forward the
                ' "show your window" command instead, so a tray-resident/minimized
                ' instance brings the window (with its current content) back.
                If String.IsNullOrEmpty(file_Path) Then
                    file_Path = Main_Form.Show_Window_Command
                End If

                Dim current_Id As Integer = Process.GetCurrentProcess().Id
                Dim current_Name As String = Process.GetCurrentProcess().ProcessName

                For Each proc As Process In Process.GetProcessesByName(current_Name)
                    If proc.Id = current_Id Then Continue For

                    ' A tray-resident instance hides its window, so MainWindowHandle
                    ' is IntPtr.Zero - fall back to enumerating the process's
                    ' top-level windows. Sending to all of them is safe: only
                    ' Main_Form's WndProc reacts to WM_COPYDATA.
                    Dim target_Handles As New List(Of IntPtr)
                    If proc.MainWindowHandle <> IntPtr.Zero Then
                        target_Handles.Add(proc.MainWindowHandle)
                    Else
                        target_Handles.AddRange(GetProcessTopLevelWindows(proc.Id))
                    End If
                    If target_Handles.Count = 0 Then Continue For

                    Try
                        Dim bytes() As Byte = System.Text.Encoding.UTF8.GetBytes(file_Path)
                        Dim ptr As IntPtr = Marshal.AllocHGlobal(bytes.Length + 1)
                        Marshal.Copy(bytes, 0, ptr, bytes.Length)
                        Marshal.WriteByte(ptr, bytes.Length, 0)

                        Dim cds As New Main_Form.COPYDATASTRUCT()
                        cds.dwData = IntPtr.Zero
                        cds.cbData = bytes.Length
                        cds.lpData = ptr

                        For Each target_Handle As IntPtr In target_Handles
                            SendMessageCopyData(target_Handle, WM_COPYDATA_LOCAL, IntPtr.Zero, cds)
                        Next
                        Marshal.FreeHGlobal(ptr)

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-3: Args sent via WM_COPYDATA to PID " & proc.Id.ToString() & " (" & target_Handles.Count.ToString() & " window(s))")
                    Catch ex As Exception
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-4: Error sending to existing instance: " & ex.Message)
                    End Try
                    Exit For
                Next

                e.Cancel = True
                Return
            End If

        End Sub
    End Class
End Namespace
