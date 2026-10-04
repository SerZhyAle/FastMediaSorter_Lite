Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Windows.Forms
Imports Microsoft.Win32

''' <summary>Finds and starts FileDO's own Disk Manager (<c>filedo_win.exe --disks</c>). No fixed install
''' path is guessed: the bundle next to this program is tried first, then the folder FileDO's own
''' installer registered under HKLM Uninstall. Only the folder is taken from the registry - the file
''' name is ours - so a registry value can never make this launch an arbitrary program.</summary>
Public Module FileDoDiskManager
    Public Const ManagerExe As String = "filedo_win.exe"
    Public Const DisksArgument As String = "--disks"
    Friend Const BundleFolder As String = "filedo"
    Private Const UninstallKey As String = "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"

    ''' <summary>The Disk Manager's full path, or "" when neither the bundle nor a registered FileDO
    ''' installation has it. <paramref name="exists"/> is the file-system question, injected so the
    ''' order is testable.</summary>
    Public Function Locate(bundleDir As String, registeredDirs As IEnumerable(Of String), exists As Func(Of String, Boolean)) As String
        Dim candidates As New List(Of String)()
        If Not String.IsNullOrWhiteSpace(bundleDir) Then candidates.Add(bundleDir)
        If registeredDirs IsNot Nothing Then candidates.AddRange(registeredDirs.Where(Function(d) Not String.IsNullOrWhiteSpace(d)))
        For Each dir As String In candidates
            Dim path As String = Combine(dir)
            If path.Length > 0 AndAlso exists(path) Then Return path
        Next
        Return ""
    End Function

    Public Function Locate() As String
        Return Locate(BundleDirectory(), RegisteredInstallDirectories(), AddressOf File.Exists)
    End Function

    ''' <summary>The folder a FileDO Uninstall entry points at, "" when the entry is not FileDO's or names
    ''' no folder. <c>InstallLocation</c> wins; otherwise the folder of <c>DisplayIcon</c>
    ''' (<c>"C:\..\filedo_win.exe",0</c> or without quotes).</summary>
    Public Function InstallDirFromEntry(displayName As String, installLocation As String, displayIcon As String) As String
        Dim name As String = If(displayName, "").Trim()
        If Not (name.Equals("FileDO", StringComparison.OrdinalIgnoreCase) OrElse name.StartsWith("FileDO ", StringComparison.OrdinalIgnoreCase)) Then Return ""
        Dim location As String = If(installLocation, "").Trim().Trim(""""c)
        If location.Length > 0 Then Return location.TrimEnd("\"c)
        Dim icon As String = If(displayIcon, "").Trim()
        If icon.StartsWith("""", StringComparison.Ordinal) Then
            Dim close As Integer = icon.IndexOf(""""c, 1)
            If close > 1 Then icon = icon.Substring(1, close - 1)
        Else
            Dim comma As Integer = icon.LastIndexOf(","c)
            If comma > 0 AndAlso Integer.TryParse(icon.Substring(comma + 1).Trim(), Nothing) Then icon = icon.Substring(0, comma)
        End If
        Try
            Return If(icon.Length = 0, "", If(Path.GetDirectoryName(icon), ""))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Starts the Disk Manager; the reason it did not start is the result, the detail goes to the log.</summary>
    Public Function Launch() As FileDoLaunchResult
        Dim exe As String = Locate()
        If exe.Length = 0 Then Return FileDoLaunchResult.NotFound
        Try
            Process.Start(New ProcessStartInfo(exe, DisksArgument) With {
                .UseShellExecute = False, .WorkingDirectory = Path.GetDirectoryName(exe)})
            Return FileDoLaunchResult.Started
        Catch ex As Exception
            AppFileLogger.LogException("FileDO Disk Manager", ex)
            Return FileDoLaunchResult.Failed
        End Try
    End Function

    Private Function Combine(dir As String) As String
        Try
            Return Path.Combine(dir.Trim().Trim(""""c), ManagerExe)
        Catch
            Return ""
        End Try
    End Function

    Private Function BundleDirectory() As String
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            Return If(String.IsNullOrEmpty(baseDir), "", Path.Combine(baseDir, BundleFolder))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>The folder in FileDO's own <c>HKLM\SOFTWARE\FileDO\InstallPath</c> value, "" when it is empty.
    ''' FileDO's Uninstall entries carry no <c>InstallLocation</c> and their <c>DisplayIcon</c> points into the
    ''' installer's package cache, so this value is the one that names the real folder.</summary>
    Public Function InstallDirFromInstallPath(value As String) As String
        Return If(value, "").Trim().Trim(""""c).TrimEnd("\"c)
    End Function

    Private Iterator Function RegisteredInstallDirectories() As IEnumerable(Of String)
        For Each view As RegistryView In {RegistryView.Registry64, RegistryView.Registry32}
            Dim own As String = ""
            Try
                Using hive As RegistryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                    Using key As RegistryKey = hive.OpenSubKey("SOFTWARE\FileDO")
                        If key IsNot Nothing Then own = InstallDirFromInstallPath(TryCast(key.GetValue("InstallPath"), String))
                    End Using
                End Using
            Catch
            End Try
            If own.Length > 0 Then Yield own
        Next
        For Each view As RegistryView In {RegistryView.Registry64, RegistryView.Registry32}
            Dim names As String() = Array.Empty(Of String)()
            Dim root As RegistryKey = Nothing
            Try
                root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(UninstallKey)
                If root IsNot Nothing Then names = root.GetSubKeyNames()
            Catch
            End Try
            If root Is Nothing Then Continue For
            Using root
                For Each subName As String In names
                    Dim dir As String = ""
                    Try
                        Using entry As RegistryKey = root.OpenSubKey(subName)
                            If entry IsNot Nothing Then
                                dir = InstallDirFromEntry(TryCast(entry.GetValue("DisplayName"), String),
                                    TryCast(entry.GetValue("InstallLocation"), String), TryCast(entry.GetValue("DisplayIcon"), String))
                            End If
                        End Using
                    Catch
                    End Try
                    If dir.Length > 0 Then Yield dir
                Next
            End Using
        Next
    End Function
End Module

Public Enum FileDoLaunchResult
    Started
    NotFound
    Failed
End Enum
