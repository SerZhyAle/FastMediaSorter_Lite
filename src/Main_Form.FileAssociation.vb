Option Strict On

Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.Win32
Imports System.Diagnostics

Partial Public Class Main_Form

    Function IsRunningAsAdministrator() As Boolean
        Dim identity = WindowsIdentity.GetCurrent()
        Dim principal = New WindowsPrincipal(identity)
        Return principal.IsInRole(WindowsBuiltInRole.Administrator)
    End Function

    ' Add this function to check .jpg association
    Private Function IsJpgAssociatedWithThisApp() As Boolean
        Try
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2410: check for JPG associacion")
            Using key = Registry.ClassesRoot.OpenSubKey(".jpg")
                If key Is Nothing Then Return False
                Dim progId = key.GetValue("")?.ToString()
                If String.IsNullOrEmpty(progId) Then Return False
                Using progKey = Registry.ClassesRoot.OpenSubKey(progId & "\shell\open\command")
                    If progKey Is Nothing Then Return False
                    Dim command = progKey.GetValue("")?.ToString()
                    If String.IsNullOrEmpty(command) Then Return False
                    Dim exePath = Application.ExecutablePath.ToLowerInvariant()
                    Return command.ToLowerInvariant().Contains(exePath)
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Sub AssociateJpgWithThisApp()
        Try
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2420: JPG associacion..")
            Dim exePath = Application.ExecutablePath
            Dim progId = "FastMediaSorter.jpg"
            ' Set ProgID
            Using progKey = Registry.ClassesRoot.CreateSubKey(progId)
                progKey.SetValue("", "JPEG Image - FastMediaSorter")
                Using shellKey = progKey.CreateSubKey("shell\open\command")
                    shellKey.SetValue("", """" & exePath & """ ""%1""")
                End Using
            End Using
            ' Set .jpg default
            Using extKey = Registry.ClassesRoot.CreateSubKey(".jpg")
                extKey.SetValue("", progId)
            End Using
        Catch ex As Exception
            MessageBox.Show(If(Is_Russian_Language, "Ошибка ассоциации: ", "Failed to set association: ") & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ERR with JPG associacion.." & ex.Message)
        End Try
    End Sub

    Private Sub CheckAndOfferJpgAssociation()
        If IsRunningAsAdministrator() AndAlso Not IsJpgAssociatedWithThisApp() Then
            Dim msg = If(Is_Russian_Language, "Ассоциировать .JPG файлы с этой программой?", "Associate .JPG files with this application?")
            If MessageBox.Show(msg, "Association", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                AssociateJpgWithThisApp()
            End If
        End If
    End Sub

    Private Function AreImageTypesAssociatedWithThisApp() As Boolean
        Return IsExtensionAssociatedWithThisApp(".jpg") AndAlso
           IsExtensionAssociatedWithThisApp(".png") AndAlso
           IsExtensionAssociatedWithThisApp(".gif")
    End Function

    Private Function IsExtensionAssociatedWithThisApp(ext As String) As Boolean
        Try
            Using key = Registry.ClassesRoot.OpenSubKey(ext)
                If key Is Nothing Then Return False
                Dim progId = key.GetValue("")?.ToString()
                If String.IsNullOrEmpty(progId) Then Return False
                Using progKey = Registry.ClassesRoot.OpenSubKey(progId & "\shell\open\command")
                    If progKey Is Nothing Then Return False
                    Dim command = progKey.GetValue("")?.ToString()
                    If String.IsNullOrEmpty(command) Then Return False
                    Dim exePath = Application.ExecutablePath.ToLowerInvariant()
                    Return command.ToLowerInvariant().Contains(exePath)
                End Using
            End Using
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2430: Ext associaciated")
        Catch
            Return False
        End Try
    End Function

    Private Sub AssociateImageTypesWithThisApp()
        AssociateExtensionWithThisApp(".jpg", "FastMediaSorter.jpg", "JPEG Image - FastMediaSorter")
        AssociateExtensionWithThisApp(".png", "FastMediaSorter.png", "PNG Image - FastMediaSorter")
        AssociateExtensionWithThisApp(".gif", "FastMediaSorter.gif", "GIF Image - FastMediaSorter")

        MessageBox.Show(If(Is_Russian_Language, "Ассоциации установлены. Возможно потребуется перезапустить Проводник или Windows.", "Associations set. You may need to restart Explorer or Windows for changes to take effect."), "Association", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub AssociateExtensionWithThisApp(ext As String, progId As String, description As String)
        Try
            Dim exePath = Application.ExecutablePath
            Using progKey = Registry.ClassesRoot.CreateSubKey(progId)
                progKey.SetValue("", description)
                Using shellKey = progKey.CreateSubKey("shell\open\command")
                    shellKey.SetValue("", """" & exePath & """ ""%1""")
                End Using
            End Using

            Using extKey = Registry.ClassesRoot.CreateSubKey(ext)
                extKey.SetValue("", progId)
            End Using
        Catch ex As Exception
            MessageBox.Show(If(Is_Russian_Language, "Ошибка ассоциации: ", "Failed to set association: ") & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ERR Ext associaciated: " & ex.Message)
        End Try
    End Sub

    Private Sub CheckAndOfferImageAssociations()
        If GetSetting(App_name, Second_App_Name, "UserAlreadyAskedForAssociations", "0") = "0" AndAlso
            IsRunningAsAdministrator() AndAlso
            Not AreImageTypesAssociatedWithThisApp() Then

            Dim msg = If(Is_Russian_Language, "Ассоциировать .JPG, .PNG, .GIF файлы с этой программой?", "Associate .JPG, .PNG, .GIF files with this application?")
            If MessageBox.Show(msg, "Association", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                AssociateImageTypesWithThisApp()
            End If

            SaveSetting(App_name, Second_App_Name, "UserAlreadyAskedForAssociations", "1")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2440: asked for association")
        End If
    End Sub

    Public Sub AssociateAllImageFormatsWithThisApp()
        Dim all_Image_Extensions() As String = {
            ".jpg", ".jpeg", ".gif", ".png", ".bmp", ".tiff",
            ".ico", ".wmf", ".emf", ".exif",
            ".webp", ".heic", ".avif", ".svg"
        }

        Dim failed As New List(Of String)
        Dim exe_Path As String = Application.ExecutablePath

        For Each ext In all_Image_Extensions
            Try
                Dim clean As String = ext.TrimStart("."c)
                Dim prog_Id As String = "FastMediaSorter." & clean
                Dim description As String = clean.ToUpper() & " Image - FastMediaSorter"

                ' HKCU\Software\Classes — не требует прав администратора, работает для текущего пользователя
                Using classes_Key = Registry.CurrentUser.OpenSubKey("Software\Classes", True)
                    Using prog_Key = classes_Key.CreateSubKey(prog_Id)
                        prog_Key.SetValue("", description)
                        Using shell_Key = prog_Key.CreateSubKey("shell\open\command")
                            shell_Key.SetValue("", """" & exe_Path & """ ""%1""")
                        End Using
                        Using icon_Key = prog_Key.CreateSubKey("DefaultIcon")
                            icon_Key.SetValue("", """" & exe_Path & """,0")
                        End Using
                    End Using
                    Using ext_Key = classes_Key.CreateSubKey(ext)
                        ext_Key.SetValue("", prog_Id)
                    End Using
                End Using
            Catch ex As Exception
                failed.Add(ext)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2501: Error registering " & ext & ": " & ex.Message)
            End Try
        Next

        ' Уведомить shell об изменении ассоциаций
        SHChangeNotify(&H8000000, &H1000, IntPtr.Zero, IntPtr.Zero)

        Dim registered_Count As Integer = all_Image_Extensions.Length - failed.Count
        If failed.Count = 0 Then
            MessageBox.Show(
                If(Is_Russian_Language,
                   "Успешно зарегистрировано " & registered_Count.ToString() & " форматов:" & vbCrLf &
                   String.Join("  ", all_Image_Extensions) & vbCrLf & vbCrLf &
                   "Изменения применены для текущего пользователя.",
                   registered_Count.ToString() & " formats registered:" & vbCrLf &
                   String.Join("  ", all_Image_Extensions) & vbCrLf & vbCrLf &
                   "Changes applied for current user."),
                If(Is_Russian_Language, "Регистрация завершена", "Registration complete"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show(
                If(Is_Russian_Language,
                   "Зарегистрировано: " & registered_Count.ToString() & vbCrLf &
                   "Ошибок: " & failed.Count.ToString() & " (" & String.Join(", ", failed) & ")",
                   "Registered: " & registered_Count.ToString() & vbCrLf &
                   "Errors: " & failed.Count.ToString() & " (" & String.Join(", ", failed) & ")"),
                If(Is_Russian_Language, "Регистрация", "Registration"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

End Class
