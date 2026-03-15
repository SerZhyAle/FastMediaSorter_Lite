Imports Microsoft.VisualBasic.ApplicationServices
Imports System.Windows.Forms
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Diagnostics

Namespace My
    Partial Friend Class MyApplication

        Private Const WM_COPYDATA_LOCAL As Integer = &H4A

        <DllImport("user32.dll", CharSet:=CharSet.Auto)>
        Private Shared Function SendMessageCopyData(hWnd As IntPtr, msg As Integer, wParam As IntPtr, ByRef lParam As Main_Form.COPYDATASTRUCT) As Integer
        End Function

        Private Sub MyApplication_StartupNextInstance(sender As Object, e As StartupNextInstanceEventArgs) Handles Me.StartupNextInstance
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0004: MyApplication_StartupNextInstance called")

            e.BringToForeground = False

            Dim mainFormInstance As Main_Form = Nothing
            If Application.MainForm IsNot Nothing AndAlso TypeOf Application.MainForm Is Main_Form Then
                mainFormInstance = DirectCast(Application.MainForm, Main_Form)
            End If

            If mainFormInstance IsNot Nothing AndAlso e.CommandLine.Count > 0 Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0005: Processing argument in existing instance")

                Dim wasMinimized As Boolean = (mainFormInstance.WindowState = FormWindowState.Minimized)
                Dim previousForegroundWindowHandle As IntPtr = IntPtr.Zero

                If Not wasMinimized Then
                    previousForegroundWindowHandle = Common_Module.GetForegroundWindow()
                    If previousForegroundWindowHandle = mainFormInstance.Handle Then
                        previousForegroundWindowHandle = IntPtr.Zero
                    End If
                End If

                Dim fullCommandLine As String = String.Join(" ", e.CommandLine.ToArray())
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

                If Not String.IsNullOrEmpty(file_Path) Then
                    Dim current_Id As Integer = Process.GetCurrentProcess().Id
                    Dim current_Name As String = Process.GetCurrentProcess().ProcessName

                    For Each proc As Process In Process.GetProcessesByName(current_Name)
                        If proc.Id <> current_Id AndAlso proc.MainWindowHandle <> IntPtr.Zero Then
                            Try
                                Dim bytes() As Byte = System.Text.Encoding.UTF8.GetBytes(file_Path)
                                Dim ptr As IntPtr = Marshal.AllocHGlobal(bytes.Length + 1)
                                Marshal.Copy(bytes, 0, ptr, bytes.Length)
                                Marshal.WriteByte(ptr, bytes.Length, 0)

                                Dim cds As New Main_Form.COPYDATASTRUCT()
                                cds.dwData = IntPtr.Zero
                                cds.cbData = bytes.Length
                                cds.lpData = ptr

                                SendMessageCopyData(proc.MainWindowHandle, WM_COPYDATA_LOCAL, IntPtr.Zero, cds)
                                Marshal.FreeHGlobal(ptr)

                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-3: Args sent via WM_COPYDATA to PID " & proc.Id.ToString() & " HWND: " & proc.MainWindowHandle.ToString())
                            Catch ex As Exception
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-4: Error sending to existing instance: " & ex.Message)
                            End Try
                            Exit For
                        End If
                    Next
                End If

                e.Cancel = True
            End If
        End Sub
    End Class
End Namespace