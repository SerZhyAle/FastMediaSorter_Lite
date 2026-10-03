Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.Json
Imports Microsoft.Win32

''' <summary>A user-session DOS drive alias. The worker still owns its no-letter mount.
''' Never overwrite a drive, nor remove a mapping whose exact target differs.</summary>
Public Module DiskDriveLetters
    Private Const RegistryPath As String = "Software\SZA\FastMediaSorter\ShareDiskLetters"
    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function DefineDosDevice(flags As UInteger, deviceName As String, target As String) As Boolean
    End Function
    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function QueryDosDevice(deviceName As String, target As StringBuilder, size As Integer) As UInteger
    End Function

    Public NotInheritable Class AliasRecord
        Public Property ContainerId As String
        Public Property Target As String
    End Class

    Friend Function TargetFor(path As String) As String
        If String.IsNullOrWhiteSpace(path) OrElse Not IO.Path.IsPathFullyQualified(path) Then Throw New ArgumentException("An absolute mount path is required.")
        Return "\??\" & If(path.StartsWith("\\?\", StringComparison.Ordinal), path.Substring(4), path).TrimEnd("\"c)
    End Function

    Friend Function CurrentTarget(letter As String) As String
        Dim result As New StringBuilder(32768)
        If QueryDosDevice(letter, result, result.Capacity) = 0 Then
            Dim code As Integer = Marshal.GetLastWin32Error()
            If code <> 2 Then Throw New Win32Exception(code)
            Return ""
        End If
        Return result.ToString().Split(ChrW(0))(0)
    End Function

    Public Function FreeLetters() As List(Of String)
        Dim result As New List(Of String)()
        For c As Integer = AscW("D"c) To AscW("Z"c)
            Dim letter As String = ChrW(c) & ":"
            If CurrentTarget(letter).Length = 0 Then result.Add(letter)
        Next
        Return result
    End Function

    Public Function HasMappings() As Boolean
        Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(RegistryPath)
            Return key IsNot Nothing AndAlso key.ValueCount > 0
        End Using
    End Function

    Public Sub Attach(disk As SharedDisk, letter As String)
        Attach(disk, letter, RegistryPath)
    End Sub

    Friend Sub Attach(disk As SharedDisk, letter As String, storePath As String)
        If disk Is Nothing OrElse disk.State <> "open" OrElse String.IsNullOrEmpty(disk.ContainerId) Then Throw New InvalidOperationException("Disk is not open.")
        If letter.Length <> 2 OrElse letter(1) <> ":"c OrElse letter(0) < "D"c OrElse letter(0) > "Z"c Then Throw New ArgumentException("Invalid drive letter.")
        Dim target As String = TargetFor(disk.MountPath)
        If CurrentTarget(letter).Length > 0 Then Throw New IOException("Drive letter is already in use.")
        If Not Directory.Exists(disk.MountPath) Then Throw New IOException("The mount is unavailable in this Windows session.")
        If Not DefineDosDevice(1UI, letter, target) Then Throw New Win32Exception(Marshal.GetLastWin32Error())
        Try
            Using key As RegistryKey = Registry.CurrentUser.CreateSubKey(storePath)
                key.SetValue(letter, JsonSerializer.Serialize(New AliasRecord With {.ContainerId = disk.ContainerId, .Target = target}))
            End Using
        Catch
            DefineDosDevice(7UI, letter, target)
            Throw
        End Try
    End Sub

    Public Sub Reconcile(disks As IEnumerable(Of SharedDisk))
        Reconcile(disks, RegistryPath)
    End Sub

    Friend Sub Reconcile(disks As IEnumerable(Of SharedDisk), storePath As String)
        Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(storePath, True)
            If key Is Nothing Then Return
            For Each letter As String In key.GetValueNames()
                Dim record As AliasRecord
                Try
                    record = JsonSerializer.Deserialize(Of AliasRecord)(CStr(key.GetValue(letter)))
                Catch ex As JsonException
                    Continue For
                End Try
                If record Is Nothing Then Continue For
                If letter.Length <> 2 OrElse letter(1) <> ":"c OrElse letter(0) < "D"c OrElse letter(0) > "Z"c OrElse String.IsNullOrEmpty(record.Target) Then Continue For
                If disks.Any(Function(d) d.ContainerId = record.ContainerId AndAlso d.State = "open" AndAlso
                             TargetFor(d.MountPath) = record.Target) Then Continue For
                If CurrentTarget(letter) = record.Target AndAlso Not DefineDosDevice(7UI, letter, record.Target) Then
                    Throw New Win32Exception(Marshal.GetLastWin32Error())
                End If
                key.DeleteValue(letter, False)
            Next
        End Using
    End Sub
End Module
