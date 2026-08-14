Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Windows.Forms
Imports Microsoft.Win32

''' <summary>
''' Everything that talks to the Windows Service Control Manager about the Server
''' edition worker host (SPECIFICATION_SHARE_SYSTEM_SERVICE.md §3.2, §4.3).
'''
''' Two halves, deliberately separated:
'''   * READ - a live SCM status query plus a validation of the registration
'''     itself. Both run unelevated: a standard user may query a service's state,
'''     and the registration lives in a readable HKLM key. This is what
'''     <see cref="ServerFeatures.HostMode"/> is built on, and why a Server
'''     installation cannot be claimed by an HKCU flag (invariant: server mode is
'''     machine state).
'''   * WRITE - every machine-affecting action (install, repair, start, stop,
'''     remove, migrate) goes through ONE short-lived elevated helper script with a
'''     visible UAC prompt. Nothing here creates a service, widens an ACL or opens
'''     a firewall hole on its own, and no phone request or ordinary IPC command
'''     can reach these paths at all.
''' </summary>
Public Module ServiceControl

    ''' <summary>The frozen SCM service name. It is the same string the Go worker
    ''' compiles in (internal/service/scm.go) and the installer registers; changing
    ''' it orphans every installed Server edition.</summary>
    Public Const ServiceName As String = "FastMediaSorterCompanionSFTP"

    ''' <summary>Elevated management helper, installed next to the exe by the Server
    ''' installer. Absent in a User-edition install - the UI then offers the download
    ''' page instead of an action it cannot perform.</summary>
    Private Const HelperScriptName As String = "install-share-service.ps1"

    ''' <summary>Machine-owned state directory of the Server edition. Mirrors
    ''' sftpserver.MachineDataDir() in the worker; both must name the same path or a
    ''' migration would copy the identity somewhere the service never reads.</summary>
    Public Function MachineDataDir() As String
        Try
            Dim base As String = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            If String.IsNullOrEmpty(base) Then Return ""
            Return Path.Combine(base, "FastMediaSorterCompanion")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Per-user state directory of the User edition (the worker's default).</summary>
    Public Function UserDataDir() As String
        Try
            Dim base As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            If String.IsNullOrEmpty(base) Then Return ""
            Return Path.Combine(base, "FastMediaSorterCompanion")
        Catch
            Return ""
        End Try
    End Function

    ' --- live SCM state ---------------------------------------------------------

    ''' <summary>The four states the UI must keep apart (spec §5): a service can be
    ''' installed but stopped, running but serving nothing, or absent entirely.
    ''' Unknown means the query itself failed - never treat it as "not installed".</summary>
    Public Enum ServiceState
        NotInstalled
        Stopped
        Starting
        Running
        Stopping
        Unknown
    End Enum

    Private Const SC_MANAGER_CONNECT As UInteger = &H1UI
    Private Const SERVICE_QUERY_STATUS As UInteger = &H4UI
    Private Const ERROR_SERVICE_DOES_NOT_EXIST As Integer = 1060

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SERVICE_STATUS
        Public dwServiceType As UInteger
        Public dwCurrentState As UInteger
        Public dwControlsAccepted As UInteger
        Public dwWin32ExitCode As UInteger
        Public dwServiceSpecificExitCode As UInteger
        Public dwCheckPoint As UInteger
        Public dwWaitHint As UInteger
    End Structure

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function OpenSCManagerW(machineName As String, databaseName As String, desiredAccess As UInteger) As IntPtr
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function OpenServiceW(scManager As IntPtr, serviceName As String, desiredAccess As UInteger) As IntPtr
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function QueryServiceStatus(service As IntPtr, ByRef status As SERVICE_STATUS) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function CloseServiceHandle(handle As IntPtr) As Boolean
    End Function

    ''' <summary>
    ''' Asks the SCM what the service is doing right now. This is the authoritative
    ''' answer the spec demands - a registry key can outlive a deleted service and a
    ''' cached flag can outlive both. Runs unelevated (SERVICE_QUERY_STATUS is granted
    ''' to standard users by the default service security descriptor).
    ''' </summary>
    Public Function QueryState() As ServiceState
        Dim scm As IntPtr = IntPtr.Zero
        Dim svc As IntPtr = IntPtr.Zero
        Try
            scm = OpenSCManagerW(Nothing, Nothing, SC_MANAGER_CONNECT)
            If scm = IntPtr.Zero Then Return ServiceState.Unknown
            svc = OpenServiceW(scm, ServiceName, SERVICE_QUERY_STATUS)
            If svc = IntPtr.Zero Then
                If Marshal.GetLastWin32Error() = ERROR_SERVICE_DOES_NOT_EXIST Then Return ServiceState.NotInstalled
                Return ServiceState.Unknown
            End If
            Dim st As SERVICE_STATUS = Nothing
            If Not QueryServiceStatus(svc, st) Then Return ServiceState.Unknown
            Select Case st.dwCurrentState
                Case 1UI : Return ServiceState.Stopped
                Case 2UI : Return ServiceState.Starting
                Case 3UI : Return ServiceState.Stopping
                Case 4UI : Return ServiceState.Running
                Case Else : Return ServiceState.Unknown
            End Select
        Catch
            Return ServiceState.Unknown
        Finally
            If svc <> IntPtr.Zero Then CloseServiceHandle(svc)
            If scm <> IntPtr.Zero Then CloseServiceHandle(scm)
        End Try
    End Function

    ''' <summary>
    ''' True while the service holds (or is about to hold) the share. This is the
    ''' suppression check: whoever asks must NOT start a foreground worker, because
    ''' the service already owns the control pipe, the listen port and the host key.
    ''' Deliberately based on the live state rather than on the registration, so even a
    ''' registration this build does not recognise still suppresses a second worker.
    ''' </summary>
    Public Function IsServiceServing() As Boolean
        Dim s As ServiceState = QueryState()
        Return s = ServiceState.Running OrElse s = ServiceState.Starting
    End Function

    ''' <summary>True when the service exists at all, whatever it is doing. The Stop /
    ''' Restart / Repair controls key off this - they make sense for a stopped service
    ''' too, and hiding them there is how a broken service becomes unfixable.</summary>
    Public Function IsServiceInstalled() As Boolean
        Return QueryState() <> ServiceState.NotInstalled
    End Function

    ''' <summary>The state directory BOTH hosts persist to on this PC: the machine one
    ''' once a Server edition created it (the two editions then share one shared-folder
    ''' list, one settings file and one host key), the per-user one otherwise. Mirrors
    ''' sftpserver.DataDir() in the worker - if these two ever disagree, the console
    ''' would report a store the worker does not use.</summary>
    Public Function ActiveDataDir() As String
        Dim machine As String = MachineDataDir()
        Try
            If machine.Length > 0 AndAlso Directory.Exists(machine) Then Return machine
        Catch
        End Try
        Return UserDataDir()
    End Function

    ''' <summary>The registered command line of the service, or "" when it is absent.
    ''' Readable by a standard user - the service registration lives in HKLM.</summary>
    Public Function RegisteredImagePath() As String
        Try
            Dim value As Object = Registry.GetValue(
                "HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" & ServiceName, "ImagePath", Nothing)
            Return If(value Is Nothing, "", Convert.ToString(value))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' True when a registered service really is OUR Server edition worker: the
    ''' command line has to name the worker exe next to this app AND carry the
    ''' --service switch. Without this check a leftover registration from another
    ''' install path (or the discontinued Wails GUI) would make the console silently
    ''' refuse to start a worker it does not actually manage.
    ''' </summary>
    Public Function IsOurRegistration() As Boolean
        Dim image As String = RegisteredImagePath()
        If image.Length = 0 Then Return False
        If image.IndexOf("--service", StringComparison.OrdinalIgnoreCase) < 0 Then Return False
        Return image.IndexOf(WorkerProcess.ExeName, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>The management identity to enrol in the control-pipe DACL: this
    ''' user's SID. In Session 0 the worker's own identity is LocalService, so this
    ''' is the only thing that can make the interactive console authorized.</summary>
    Public Function CurrentUserSid() As String
        Try
            Using id As WindowsIdentity = WindowsIdentity.GetCurrent()
                If id?.User Is Nothing Then Return ""
                Return id.User.Value
            End Using
        Catch
            Return ""
        End Try
    End Function

    ' --- elevated management ----------------------------------------------------

    ''' <summary>The machine-affecting actions, each a verb of the elevated helper.
    ''' Every one of them prompts for administrator approval - none is a side effect
    ''' of an ordinary sharing action.</summary>
    Public Enum ManageAction
        Install
        Repair
        Remove
        StartService
        StopService
        ''' <summary>Stop + start in one elevated step. The action a user actually
        ''' wants after changing something the service only reads at start-up.</summary>
        RestartService
        ''' <summary>Takes this installation from user-session hosting to the Windows
        ''' service, in ONE elevated step: the per-user state (folders, settings, and
        ''' above all the host key phones pinned) is copied to the machine store and
        ''' validated, the worker is staged where a service account can actually run it,
        ''' the service is registered and started, and the currently shared folders are
        ''' granted to the service account. Splitting any of that out would mean a second
        ''' UAC prompt, and a switch that "worked" but serves nothing.</summary>
        MigrateToServer
        MigrateToUser
        ''' <summary>Grants LOCAL SERVICE access on the currently shared folders - read
        ''' for a read-only root, read/write for a writable one. Needed because the
        ''' service account is not the user who picked them, so a folder under a user
        ''' profile is invisible to it until this runs.</summary>
        GrantRoots
    End Enum

    ''' <summary>Outcome of an elevated management attempt.</summary>
    Public Enum ManageResult
        Succeeded
        Declined      ' the user dismissed the UAC prompt
        Failed        ' the helper ran and reported a non-zero exit code
        Unavailable   ' no helper script / no worker payload - nothing to manage
    End Enum

    ''' <summary>True when the bundled elevated helper is present. It ships with the
    ''' Server installer and, since the Share component became part of every ordinary
    ''' installation, with the regular installer too - which is what lets a normal
    ''' install take the service role on instead of sending the user to a download
    ''' page. A packaged (Store) build has neither the helper nor permission to
    ''' register a service, so this is False there and the UI offers the page.</summary>
    Public Function CanManage() As Boolean
        Dim script As String = HelperScriptPath()
        Return script.Length > 0 AndAlso File.Exists(script) AndAlso WorkerProcess.IsAvailable()
    End Function

    ''' <summary>
    ''' Can this installation switch ITSELF into always-on hosting? Three conditions,
    ''' each for its own reason: no service may be registered yet (otherwise the
    ''' console offers Repair/Remove instead), the elevated helper and the worker must
    ''' both be on disk, and the build must not be packaged - a Store package cannot
    ''' register a Windows service, and its container would virtualize the attempt away
    ''' rather than fail honestly. When this is False and no service exists, the console
    ''' falls back to the Server edition download page.
    ''' </summary>
    Public Function CanSwitchToService() As Boolean
        If AutostartManager.IsPackaged() Then Return False
        If IsServiceInstalled() Then Return False
        Return CanManage()
    End Function

    ''' <summary>
    ''' Runs one machine-affecting action through the elevated helper and waits for
    ''' it. Blocks on the UAC prompt, so call it from a modal where the wait reads.
    ''' The app itself stays non-elevated - only the short-lived helper elevates.
    ''' </summary>
    ''' <param name="readOnlyRoots">The subset of <paramref name="roots"/> shared
    ''' read-only. Those get read access; every other root gets read/write, because a
    ''' folder the list promises as writable that the service account cannot write to
    ''' fails on the phone, as an SFTP permission error far from the promise.</param>
    Public Function Manage(action As ManageAction,
                           Optional roots As IEnumerable(Of String) = Nothing,
                           Optional readOnlyRoots As IEnumerable(Of String) = Nothing) As ManageResult
        If Not CanManage() Then Return ManageResult.Unavailable
        Dim script As String = HelperScriptPath()

        Dim args As String = "-NoProfile -ExecutionPolicy Bypass -File """ & script & """" &
                             " -Action " & VerbOf(action) &
                             " -ExePath """ & WorkerProcess.WorkerExePath() & """" &
                             " -DataDir """ & MachineDataDir() & """" &
                             " -UserDataDir """ & UserDataDir() & """"
        Dim sid As String = CurrentUserSid()
        If sid.Length > 0 Then args &= " -ManageSid """ & sid & """"
        Dim rootList As String = JoinRoots(roots)
        If rootList.Length > 0 Then
            args &= " -Roots """ & rootList & """"
            ' Passed even when empty (every root writable) - the helper reads the
            ' parameter's PRESENCE as "this caller knows about read-only roots" and
            ' falls back to read-only-for-everything when it is absent.
            args &= " -ReadOnlyRoots """ & JoinRoots(readOnlyRoots) & """"
        End If

        Dim psi As New ProcessStartInfo("powershell.exe", args) With {
            .UseShellExecute = True,
            .Verb = "runas",
            .WindowStyle = ProcessWindowStyle.Hidden}
        Try
            Using p As Process = Process.Start(psi)
                p.WaitForExit()
                Return If(p.ExitCode = 0, ManageResult.Succeeded, ManageResult.Failed)
            End Using
        Catch ex As System.ComponentModel.Win32Exception
            ' 1223 = ERROR_CANCELLED: the user said No to the UAC prompt.
            If ex.NativeErrorCode = 1223 Then Return ManageResult.Declined
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " share-service manage: " & ex.Message)
            Return ManageResult.Failed
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " share-service manage: " & ex.Message)
            Return ManageResult.Failed
        End Try
    End Function

    Private Function VerbOf(action As ManageAction) As String
        Select Case action
            Case ManageAction.Install : Return "install"
            Case ManageAction.Repair : Return "repair"
            Case ManageAction.Remove : Return "remove"
            Case ManageAction.StartService : Return "start"
            Case ManageAction.StopService : Return "stop"
            Case ManageAction.RestartService : Return "restart"
            Case ManageAction.MigrateToServer : Return "migrate-to-server"
            Case ManageAction.GrantRoots : Return "grant-roots"
            Case Else : Return "migrate-to-user"
        End Select
    End Function

    ''' <summary>Packs folder paths into the single -Roots argument the helper takes.
    ''' Pipe-separated because a path may contain a comma or a space but never "|".</summary>
    Private Function JoinRoots(roots As IEnumerable(Of String)) As String
        If roots Is Nothing Then Return ""
        Dim kept As New List(Of String)()
        For Each r As String In roots
            Dim v As String = If(r, "").Trim()
            If v.Length > 0 AndAlso v.IndexOf(""""c) < 0 AndAlso v.IndexOf("|"c) < 0 Then kept.Add(v)
        Next
        Return String.Join("|", kept)
    End Function

    Private Function HelperScriptPath() As String
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            If String.IsNullOrEmpty(baseDir) Then Return ""
            Return Path.Combine(baseDir, HelperScriptName)
        Catch
            Return ""
        End Try
    End Function

End Module
