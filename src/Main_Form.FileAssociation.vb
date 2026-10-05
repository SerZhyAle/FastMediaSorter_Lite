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

    Private Sub CheckAndOfferImageAssociations()
        ' First-run startup must stay non-blocking for packaged installs and
        ' validation environments. File associations remain available through the
        ' explicit settings action instead of an automatic prompt.
        SaveSetting(App_name, Second_App_Name, "UserAlreadyAskedForAssociations", "1")
    End Sub

#If Not NETFRAMEWORK Then
    ' GetCurrentPackageFullName answers this when the process has no package identity.
    Private Const APPMODEL_ERROR_NO_PACKAGE As Integer = 15700

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, EntryPoint:="GetCurrentPackageFullName")>
    Private Shared Function GetCurrentPackageFullName(ByRef packageFullNameLength As Integer, packageFullName As Char()) As Integer
    End Function

    ''' <summary>True when this is the Store (MSIX) build. Only the mainline is ever
    ''' packaged, so this lives behind the seam.</summary>
    Friend Shared Function IsPackagedApp() As Boolean
        Try
            Dim length As Integer = 0
            Return GetCurrentPackageFullName(length, Nothing) <> APPMODEL_ERROR_NO_PACKAGE
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Under MSIX the writes below land in the package's private hive and change nothing
    ''' Explorer sees - yet the dialog said "registered N formats" (audit 047, C-22). The
    ''' packaged build already declares its file associations in its manifest; what is left
    ''' for the user is to pick the default, which only Windows Settings can do. Returns
    ''' True when it handled the request that way.
    ''' </summary>
    Private Function OpenDefaultAppsSettingsWhenPackaged() As Boolean
        If AssociationUx.ModeFor(IsPackagedApp()) <> AssociationMode.OpenWindowsSettings Then Return False
        Try
            Process.Start(New ProcessStartInfo("ms-settings:defaultapps") With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Opening Windows default-apps settings", ex)
        End Try
        Return True
    End Function
#End If

    Public Sub AssociateAllImageFormatsWithThisApp()
#If Not NETFRAMEWORK Then
        If OpenDefaultAppsSettingsWhenPackaged() Then Return
#End If
        ' Only formats this app can actually DISPLAY. The modern build claims
        ' avif/heic/heif too (Magick.NET decodes them, epic O-3); on x86 they stay
        ' unclaimed - no decoder there, so becoming their default handler would
        ' mean a double-click opens a blank window. svg is claimed by neither.
        ' Unclaimed formats stay scannable/sortable inside a folder
        ' (see web_specific_image_extensions) - we just don't claim them.
#If NETFRAMEWORK Then
        Dim all_Image_Extensions() As String = {
            ".jpg", ".jpeg", ".gif", ".png", ".bmp", ".tif", ".tiff",
            ".ico", ".wmf", ".emf", ".exif", ".webp"
        }
#Else
        Dim all_Image_Extensions() As String = {
            ".jpg", ".jpeg", ".gif", ".png", ".bmp", ".tif", ".tiff",
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
                Dim description As String = clean.ToUpper() & " Image - " & ProductNames.Compact

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
                AppFileLogger.LogException("Registering image association " & ext, ex)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2501: Error registering " & ext & ": " & ex.Message)
            End Try
        Next

        ' Уведомить shell об изменении ассоциаций
        SHChangeNotify(&H8000000, &H1000, IntPtr.Zero, IntPtr.Zero)

        Dim registered_Count As Integer = all_Image_Extensions.Length - failed.Count
        If failed.Count = 0 Then
            ViewerBox.Show(Me,
                Localization.TF("Успешно зарегистрировано {0} форматов:" & vbCrLf &
                                "{1}" & vbCrLf & vbCrLf &
                                "Изменения применены для текущего пользователя.",
                                registered_Count, String.Join("  ", all_Image_Extensions)),
                Localization.T("Регистрация завершена"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            ViewerBox.Show(Me,
                Localization.TF("Зарегистрировано: {0}" & vbCrLf & "Ошибок: {1} ({2})", registered_Count.ToString(), failed.Count.ToString(), String.Join(", ", failed)),
                Localization.T("Регистрация"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    ' Mirrors AssociateAllImageFormatsWithThisApp for the video/audio formats the
    ' app can play (the same set the player accepts via WebBrowser/LibVLC). Writes
    ' to HKCU\Software\Classes so no admin rights are needed.
    Public Sub AssociateAllVideoFormatsWithThisApp()
#If Not NETFRAMEWORK Then
        If OpenDefaultAppsSettingsWhenPackaged() Then Return
#End If
        Dim all_Video_Extensions() As String = video_File_Extensions.ToArray()

        Dim failed As New List(Of String)
        Dim exe_Path As String = Application.ExecutablePath

        For Each ext In all_Video_Extensions
            Try
                Dim clean As String = ext.TrimStart("."c)
                Dim prog_Id As String = "FastMediaSorter." & clean
                Dim description As String = clean.ToUpper() & " Video - " & ProductNames.Compact

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
                AppFileLogger.LogException("Registering video association " & ext, ex)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2502: Error registering " & ext & ": " & ex.Message)
            End Try
        Next

        ' Уведомить shell об изменении ассоциаций
        SHChangeNotify(&H8000000, &H1000, IntPtr.Zero, IntPtr.Zero)

        Dim registered_Count As Integer = all_Video_Extensions.Length - failed.Count
        If failed.Count = 0 Then
            ViewerBox.Show(Me,
                Localization.TF("Успешно зарегистрировано {0} форматов:" & vbCrLf &
                                "{1}" & vbCrLf & vbCrLf &
                                "Изменения применены для текущего пользователя.",
                                registered_Count, String.Join("  ", all_Video_Extensions)),
                Localization.T("Регистрация завершена"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            ViewerBox.Show(Me,
                Localization.TF("Зарегистрировано: {0}" & vbCrLf & "Ошибок: {1} ({2})", registered_Count.ToString(), failed.Count.ToString(), String.Join(", ", failed)),
                Localization.T("Регистрация"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

#If Not NETFRAMEWORK Then

    ' Secure containers, and the ONE way this program may appear in Explorer for them
    ' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §8, FDSEC-BEHAVIOUR §14.2).
    '
    ' Registering a file association is an explicit, reversible, user-invoked act - never
    ' a side effect of installation, never silent, per-user by default. The installer
    ' registers NOTHING for .fd-sec, in any component.
    '
    ' And .fd-sec is not ours to take: FileDO already registers the ProgID
    ' FileDO.SecureContainer for it, recording whatever owned the extension before. A
    ' silent swap by us would fight a product that behaves correctly. So the opt-in below
    ' is ADDITIVE - it puts this viewer in Explorer's "Open with" list and takes nothing
    ' away. Becoming the DEFAULT handler is a separate act and is deliberately not offered
    ' here: it displaces a named owner, and Windows 10 and 11 will not let a program make
    ' itself the default from code anyway - only the user can, through Explorer.

    Private Const Secure_Prog_Id As String = "FastMediaSorter.fd-sec"

    ''' <summary>Is this viewer currently offered for .fd-sec in Explorer's Open with?</summary>
    Friend Shared Function IsSecureContainerOpenWithRegistered() As Boolean
        Try
            Using key = Registry.CurrentUser.OpenSubKey("Software\Classes\" & FdSecFormat.Extension & "\OpenWithProgids")
                If key Is Nothing Then Return False
                Return key.GetValue(Secure_Prog_Id) IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Adds this viewer to Explorer's "Open with" list for .fd-sec, per user, without
    ''' touching whatever owns the extension. Returns True when the registry accepted it.
    ''' </summary>
    Friend Shared Function RegisterSecureContainerOpenWith() As Boolean
        Try
            Dim exePath As String = Application.ExecutablePath
            Using classesKey = Registry.CurrentUser.CreateSubKey("Software\Classes")
                Using progKey = classesKey.CreateSubKey(Secure_Prog_Id)
                    ' The ProgID's own name says what the file is, not what is inside it -
                    ' nothing outside the container may ever claim to know that.
                    progKey.SetValue("", "Secure container - " & ProductNames.Compact)
                    Using shellKey = progKey.CreateSubKey("shell\open\command")
                        shellKey.SetValue("", """" & exePath & """ ""%1""")
                    End Using
                    Using iconKey = progKey.CreateSubKey("DefaultIcon")
                        iconKey.SetValue("", """" & exePath & """,0")
                    End Using
                End Using
                ' OpenWithProgids, NOT the extension's default value: this offers the
                ' viewer and displaces nobody.
                Using openWithKey = classesKey.CreateSubKey(FdSecFormat.Extension & "\OpenWithProgids")
                    openWithKey.SetValue(Secure_Prog_Id, Array.Empty(Of Byte)(), RegistryValueKind.None)
                End Using
            End Using
            SHChangeNotify(&H8000000, &H1000, IntPtr.Zero, IntPtr.Zero)
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Secure container: registering the Open-with entry", ex)
            Return False
        End Try
    End Function

    ''' <summary>Takes it back out again, leaving the extension exactly as it was found -
    ''' both directions from the same place (§8).</summary>
    Friend Shared Function UnregisterSecureContainerOpenWith() As Boolean
        Try
            Using openWithKey = Registry.CurrentUser.OpenSubKey("Software\Classes\" & FdSecFormat.Extension & "\OpenWithProgids", True)
                If openWithKey IsNot Nothing AndAlso openWithKey.GetValue(Secure_Prog_Id) IsNot Nothing Then
                    openWithKey.DeleteValue(Secure_Prog_Id, False)
                End If
            End Using
            Using classesKey = Registry.CurrentUser.OpenSubKey("Software\Classes", True)
                If classesKey IsNot Nothing Then classesKey.DeleteSubKeyTree(Secure_Prog_Id, False)
            End Using
            SHChangeNotify(&H8000000, &H1000, IntPtr.Zero, IntPtr.Zero)
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Secure container: removing the Open-with entry", ex)
            Return False
        End Try
    End Function

#End If

End Class
