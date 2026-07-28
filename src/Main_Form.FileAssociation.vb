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
                progKey.SetValue("", "JPEG Image - Fast Media Sorter")
                Using shellKey = progKey.CreateSubKey("shell\open\command")
                    shellKey.SetValue("", """" & exePath & """ ""%1""")
                End Using
            End Using
            ' Set .jpg default
            Using extKey = Registry.ClassesRoot.CreateSubKey(".jpg")
                extKey.SetValue("", progId)
            End Using
        Catch ex As Exception
            MessageBox.Show(Localization.T("Ошибка ассоциации: ") & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ERR with JPG associacion.." & ex.Message)
        End Try
    End Sub

    Private Sub CheckAndOfferJpgAssociation()
        If IsRunningAsAdministrator() AndAlso Not IsJpgAssociatedWithThisApp() Then
            Dim msg = Localization.T("Ассоциировать .JPG файлы с этой программой?")
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
        AssociateExtensionWithThisApp(".jpg", "FastMediaSorter.jpg", "JPEG Image - Fast Media Sorter")
        AssociateExtensionWithThisApp(".png", "FastMediaSorter.png", "PNG Image - Fast Media Sorter")
        AssociateExtensionWithThisApp(".gif", "FastMediaSorter.gif", "GIF Image - Fast Media Sorter")

        MessageBox.Show(Localization.T("Ассоциации установлены. Возможно потребуется перезапустить Проводник или Windows."), "Association", MessageBoxButtons.OK, MessageBoxIcon.Information)
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
            MessageBox.Show(Localization.T("Ошибка ассоциации: ") & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ERR Ext associaciated: " & ex.Message)
        End Try
    End Sub

    Private Sub CheckAndOfferImageAssociations()
        ' First-run startup must stay non-blocking for packaged installs and
        ' validation environments. File associations remain available through the
        ' explicit settings action instead of an automatic prompt.
        SaveSetting(App_name, Second_App_Name, "UserAlreadyAskedForAssociations", "1")
    End Sub

    Public Sub AssociateAllImageFormatsWithThisApp()
        ' Only formats this app can actually DISPLAY. The modern build claims
        ' avif/heic/heif too (Magick.NET decodes them, epic O-3); on x86 they stay
        ' unclaimed - no decoder there, so becoming their default handler would
        ' mean a double-click opens a blank window. svg is claimed by neither.
        ' Unclaimed formats stay scannable/sortable inside a folder
        ' (see web_specific_image_extensions) - we just don't claim them.
#If NETFRAMEWORK Then
        Dim all_Image_Extensions() As String = {
            ".jpg", ".jpeg", ".gif", ".png", ".bmp", ".tiff",
            ".ico", ".wmf", ".emf", ".exif", ".webp"
        }
#Else
        Dim all_Image_Extensions() As String = {
            ".jpg", ".jpeg", ".gif", ".png", ".bmp", ".tiff",
            ".ico", ".wmf", ".emf", ".exif", ".webp",
            ".avif", ".heic", ".heif"
        }
#End If

        Dim failed As New List(Of String)
        Dim exe_Path As String = Application.ExecutablePath

        For Each ext In all_Image_Extensions
            Try
                Dim clean As String = ext.TrimStart("."c)
                Dim prog_Id As String = "FastMediaSorter." & clean
                Dim description As String = clean.ToUpper() & " Image - Fast Media Sorter"

                ' HKCU\Software\Classes - не требует прав администратора, работает для текущего пользователя
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
                Localization.TF("Успешно зарегистрировано {0} форматов:" & vbCrLf &
                                "{1}" & vbCrLf & vbCrLf &
                                "Изменения применены для текущего пользователя.",
                                registered_Count, String.Join("  ", all_Image_Extensions)),
                Localization.T("Регистрация завершена"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show(
                Localization.TF("Зарегистрировано: {0}" & vbCrLf & "Ошибок: {1} ({2})", registered_Count.ToString(), failed.Count.ToString(), String.Join(", ", failed)),
                Localization.T("Регистрация"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    ' Mirrors AssociateAllImageFormatsWithThisApp for the video/audio formats the
    ' app can play (the same set the player accepts via WebBrowser/LibVLC). Writes
    ' to HKCU\Software\Classes so no admin rights are needed.
    Public Sub AssociateAllVideoFormatsWithThisApp()
        Dim all_Video_Extensions() As String = video_File_Extensions.ToArray()

        Dim failed As New List(Of String)
        Dim exe_Path As String = Application.ExecutablePath

        For Each ext In all_Video_Extensions
            Try
                Dim clean As String = ext.TrimStart("."c)
                Dim prog_Id As String = "FastMediaSorter." & clean
                Dim description As String = clean.ToUpper() & " Video - Fast Media Sorter"

                ' HKCU\Software\Classes - не требует прав администратора, работает для текущего пользователя
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
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2502: Error registering " & ext & ": " & ex.Message)
            End Try
        Next

        ' Уведомить shell об изменении ассоциаций
        SHChangeNotify(&H8000000, &H1000, IntPtr.Zero, IntPtr.Zero)

        Dim registered_Count As Integer = all_Video_Extensions.Length - failed.Count
        If failed.Count = 0 Then
            MessageBox.Show(
                Localization.TF("Успешно зарегистрировано {0} форматов:" & vbCrLf &
                                "{1}" & vbCrLf & vbCrLf &
                                "Изменения применены для текущего пользователя.",
                                registered_Count, String.Join("  ", all_Video_Extensions)),
                Localization.T("Регистрация завершена"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show(
                Localization.TF("Зарегистрировано: {0}" & vbCrLf & "Ошибок: {1} ({2})", registered_Count.ToString(), failed.Count.ToString(), String.Join(", ", failed)),
                Localization.T("Регистрация"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

End Class
