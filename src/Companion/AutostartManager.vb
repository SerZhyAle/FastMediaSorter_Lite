Option Strict On

Imports System.Runtime.InteropServices
Imports Microsoft.Win32

''' <summary>
''' Channel-aware logon autostart for the Android-share worker.
'''
''' Unpackaged (portable ZIP / Inno / winget): opt-in HKCU Run value pointing at
''' the worker exe, written/removed at RUNTIME only (an install-time Run write can
''' land in the wrong hive when the install is elevated/machine-scoped).
'''
''' Packaged (Store MSIX): the manifest uap5:StartupTask is authoritative and an
''' HKCU Run write is virtualized and silently ignored - so the checkbox is
''' read-only/explanatory there and this module reports "packaged".
''' </summary>
Public Module AutostartManager

    Private Const RunKeyPath As String = "Software\Microsoft\Windows\CurrentVersion\Run"
    Private Const RunValueName As String = "FastMediaSorterShare"

    ' GetCurrentPackageFullName returns this when the process has no package identity.
    Private Const APPMODEL_ERROR_NO_PACKAGE As Integer = 15700

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=False)>
    Private Function GetCurrentPackageFullName(ByRef packageFullNameLength As Integer, packageFullName As Char()) As Integer
    End Function

    ''' <summary>
    ''' True when running inside an MSIX/AppX package (the Store build). Uses the
    ''' documented GetCurrentPackageFullName probe; on pre-Windows-8 the export is
    ''' absent (throws) and we treat the process as unpackaged.
    ''' </summary>
    Public Function IsPackaged() As Boolean
        Try
            Dim length As Integer = 0
            Dim rc As Integer = GetCurrentPackageFullName(length, Nothing)
            Return rc <> APPMODEL_ERROR_NO_PACKAGE
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Current autostart state. For packaged builds the manifest StartupTask owns
    ''' this, so we report True (the checkbox is read-only there anyway).
    ''' </summary>
    Public Function IsEnabled() As Boolean
        If IsPackaged() Then Return True
        Try
            Using k As RegistryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable:=False)
                If k Is Nothing Then Return False
                Return k.GetValue(RunValueName) IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Enables/disables logon autostart of the worker (unpackaged channels only).
    ''' Writes the quoted worker path to HKCU Run, or removes the value. Returns
    ''' False when not applicable (packaged) or on failure - the caller keeps the
    ''' checkbox in sync with the real state.
    ''' </summary>
    Public Function SetEnabled(enabled As Boolean) As Boolean
        If IsPackaged() Then Return False

        Dim exe As String = WorkerProcess.WorkerExePath()
        If enabled AndAlso (exe.Length = 0 OrElse Not IO.File.Exists(exe)) Then Return False

        Try
            Using k As RegistryKey = Registry.CurrentUser.CreateSubKey(RunKeyPath)
                If k Is Nothing Then Return False
                If enabled Then
                    k.SetValue(RunValueName, """" & exe & """", RegistryValueKind.String)
                ElseIf k.GetValue(RunValueName) IsNot Nothing Then
                    k.DeleteValue(RunValueName, throwOnMissingValue:=False)
                End If
            End Using
            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " companion autostart: " & ex.Message)
            Return False
        End Try
    End Function

End Module
