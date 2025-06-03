Imports Microsoft.VisualBasic.ApplicationServices
Imports System.Windows.Forms

Namespace My
    Partial Friend Class MyApplication

        Private Sub MyApplication_StartupNextInstance(sender As Object, e As StartupNextInstanceEventArgs) Handles Me.StartupNextInstance
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0004: MyApplication_StartupNextInstance called")

            e.BringToForeground = False

            Dim mainFormInstance As the_Main_Form = Nothing
            If Application.MainForm IsNot Nothing AndAlso TypeOf Application.MainForm Is the_Main_Form Then
                mainFormInstance = DirectCast(Application.MainForm, the_Main_Form)
            End If

            If mainFormInstance IsNot Nothing AndAlso e.CommandLine.Count > 0 Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0005: Processing argument in existing instance")

                Dim wasMinimized As Boolean = (mainFormInstance.WindowState = FormWindowState.Minimized)
                Dim previousForegroundWindowHandle As IntPtr = IntPtr.Zero

                If Not wasMinimized Then
                    previousForegroundWindowHandle = Module1.GetForegroundWindow()
                    If previousForegroundWindowHandle = mainFormInstance.Handle Then
                        previousForegroundWindowHandle = IntPtr.Zero
                    End If
                End If

                Dim fullCommandLine As String = String.Join(" ", e.CommandLine.ToArray())
                mainFormInstance.ProcessArgument(fullCommandLine)

                If wasMinimized Then
                    Module1.ShowWindow(mainFormInstance.Handle, Module1.SW_SHOWNOACTIVATE)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " AppEvents: unwraped")
                ElseIf previousForegroundWindowHandle <> IntPtr.Zero Then
                    Dim currentForegroundHandle As IntPtr = Module1.GetForegroundWindow()
                    If currentForegroundHandle = mainFormInstance.Handle Then
                        Module1.SetForegroundWindow(previousForegroundWindowHandle)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " AppEvents: try to focus back: " & previousForegroundWindowHandle.ToString())
                    End If
                End If
            End If
        End Sub

        Private Sub MyApplication_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0001: MyApplication_Startup")
        End Sub
    End Class
End Namespace