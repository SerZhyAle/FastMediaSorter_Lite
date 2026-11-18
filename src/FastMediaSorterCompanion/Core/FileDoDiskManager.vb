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
    ''' order is testable. <paramref name="inspect"/> verifies that the manager at that path genuinely
    ''' answers an inspection, as required by DISK-SHARE 1.5.</summary>
    Public Function Locate(bundleDir As String, registeredDirs As IEnumerable(Of String), exists As Func(Of String, Boolean), Optional inspect As Func(Of String, Boolean) = Nothing) As String
        Dim candidates As New List(Of String)()
        If Not String.IsNullOrWhiteSpace(bundleDir) Then candidates.Add(bundleDir)
        If registeredDirs IsNot Nothing Then candidates.AddRange(registeredDirs.Where(Function(d) Not String.IsNullOrWhiteSpace(d)))
        For Each dir As String In candidates
            Dim path As String = Combine(dir)
            If path.Length > 0 AndAlso exists(path) AndAlso (inspect Is Nothing OrElse inspect(path)) Then Return path
        Next
        Return ""
    End Function

    Public Function Locate() As String
        Return Locate(BundleDirectory(), RegisteredInstallDirectories(), AddressOf File.Exists, AddressOf InspectManager)
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

    ''' <summary>Extracts the directory from a command line like '"C:\Tools\FileDO\filedo_win.exe" "%1"' or 'C:\Tools\FileDO\filedo_win.exe "%1"'.</summary>
    Public Function InstallDirFromCommand(commandLine As String) As String
        Dim cmd As String = If(commandLine, "").Trim()
        If cmd.Length = 0 Then Return ""
        Dim exe As String = ""
        If cmd.StartsWith(""""c) Then
            Dim closeQuote As Integer = cmd.IndexOf(""""c, 1)
            If closeQuote > 1 Then
                exe = cmd.Substring(1, closeQuote - 1)
            Else
                exe = cmd.Trim(""""c)
            End If
        Else
            Dim exeIndex As Integer = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase)
            If exeIndex > 0 Then
                exe = cmd.Substring(0, exeIndex + 4)
            Else
                Dim space As Integer = cmd.IndexOf(" "c)
                exe = If(space > 0, cmd.Substring(0, space), cmd)
            End If
        End If
        Try
            Return If(exe.Length = 0, "", If(Path.GetDirectoryName(exe), ""))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Verifies that an executable answers an identity inspection, satisfying DISK-SHARE 1.5
    ''' verified registered discovery.</summary>
    Public Function InspectManager(exePath As String) As Boolean
        If String.IsNullOrWhiteSpace(exePath) Then Return False
        Try
            If Not File.Exists(exePath) Then Return False
            Dim vi As Diagnostics.FileVersionInfo = Diagnostics.FileVersionInfo.GetVersionInfo(exePath)
            If vi IsNot Nothing Then
                Dim names As String = (If(vi.ProductName, "") & " " & If(vi.InternalName, "") & " " & If(vi.OriginalFilename, "") & " " & If(vi.FileDescription, "")).Trim()
                If names.IndexOf("filedo", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            End If
        Catch
        End Try
        Try
            Dim dir As String = Path.GetDirectoryName(exePath)
            If Not String.IsNullOrEmpty(dir) Then
                Dim cli As String = Path.Combine(dir, "filedo.exe")
                If File.Exists(cli) Then
                    Dim viCli As Diagnostics.FileVersionInfo = Diagnostics.FileVersionInfo.GetVersionInfo(cli)
                    If viCli IsNot Nothing Then
                        Dim names As String = (If(viCli.ProductName, "") & " " & If(viCli.InternalName, "") & " " & If(viCli.OriginalFilename, "")).Trim()
                        If names.IndexOf("filedo", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
                    End If
                End If
            End If
        Catch
        End Try
        Return False
    End Function

    Friend Iterator Function RegisteredInstallDirectories() As IEnumerable(Of String)
        ' 1. Registered ProgID identities (HKCU and HKLM Classes\FileDO.DiskContainer)
        For Each hive As RegistryHive In {RegistryHive.CurrentUser, RegistryHive.LocalMachine}
            For Each view As RegistryView In {RegistryView.Registry64, RegistryView.Registry32}
                Dim progDir As String = ""
                Try
                    Using baseKey As RegistryKey = RegistryKey.OpenBaseKey(hive, view)
                        Using key As RegistryKey = baseKey.OpenSubKey("Software\Classes\FileDO.DiskContainer\shell\mount\command")
                            If key IsNot Nothing Then
                                progDir = InstallDirFromCommand(TryCast(key.GetValue(""), String))
                            End If
                        End Using
                        If String.IsNullOrEmpty(progDir) Then
                            Using key As RegistryKey = baseKey.OpenSubKey("Software\Classes\FileDO.DiskContainer")
                                If key IsNot Nothing Then
                                    progDir = InstallDirFromCommand(TryCast(key.GetValue("FileDO.RegisteredBy"), String))
                                End If
                            End Using
                        End If
                    End Using
                Catch
                End Try
                If progDir.Length > 0 Then Yield progDir
            Next
        Next

        ' 2. FileDO's own registered installation key (HKLM\SOFTWARE\FileDO)
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

        ' 3. Uninstall entries (unverified hint fallback)
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

    ''' <summary>Classifies the FileDO installation and host state into the five states of DISK-SHARE 1.5:
    ''' absent, installed-stopped, missing disk payload, incompatible, and transport or identity failure.</summary>
    Public Function ClassifyState(status As WorkerStatus, Optional locateExe As Func(Of String) = Nothing) As FileDoInstallationState
        Dim locator As Func(Of String) = If(locateExe, AddressOf Locate)
        Dim managerPath As String = ""
        Try
            managerPath = locator()
        Catch
        End Try
        Dim isInstalled As Boolean = managerPath.Length > 0

        If status Is Nothing Then
            Return If(isInstalled, FileDoInstallationState.InstalledStopped, FileDoInstallationState.Absent)
        End If

        If DiskSharing.Supported(status) Then
            Return FileDoInstallationState.InstalledStopped
        End If

        Dim err As String = If(status.DiskSharingError, "").Trim()
        If err.Length = 0 Then
            Return If(isInstalled, FileDoInstallationState.InstalledStopped, FileDoInstallationState.Absent)
        End If

        If err.IndexOf("LocalSystem", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("LocalService", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("identity", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("pipe", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Return FileDoInstallationState.TransportOrIdentityFailure
        End If

        If err.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("payload", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("cannot mount", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("lacks disk-sharing metadata", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Return FileDoInstallationState.MissingDiskPayload
        End If

        If err.IndexOf("predates", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("incompatible", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("schema", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           err.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Return FileDoInstallationState.Incompatible
        End If

        Return If(isInstalled, FileDoInstallationState.InstalledStopped, FileDoInstallationState.Absent)
    End Function
End Module

''' <summary>The five availability states required by DISK-SHARE 1.5 amendment:
''' absent, installed-stopped, missing disk payload, incompatible schema/capability,
''' and transport or identity failure.</summary>
Public Enum FileDoInstallationState
    ''' <summary>No FileDO installation or bundle found on this machine.</summary>
    Absent

    ''' <summary>FileDO is installed (registered identity found), but the disk-sharing host or service is not running or stopped.</summary>
    InstalledStopped

    ''' <summary>FileDO is present, but lacks the disk payload (e.g. Store build that cannot mount, or missing filedo.exe).</summary>
    MissingDiskPayload

    ''' <summary>Incompatible schema version, capability, or obsolete binary predating disk sharing.</summary>
    Incompatible

    ''' <summary>Transport error, named pipe failure, or identity verification of the pipe server failed.</summary>
    TransportOrIdentityFailure
End Enum

Public Enum FileDoLaunchResult
    Started
    NotFound
    Failed
End Enum
