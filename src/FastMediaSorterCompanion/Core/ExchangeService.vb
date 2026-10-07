Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports Microsoft.Win32

''' <summary>
''' The exchange server as a Windows service (ticket 144, 133 section 6.1): the Share Manager's side.
'''
''' The service <c>FastMediaSorterExchangeServer</c> exists ONLY after an explicit act (the Server
''' installer's task, or one UAC prompt from the settings group), never from a default install, a Store
''' package, a phone request or an IPC command. This module reads its state without elevation, starts and
''' stops it WITHOUT a UAC prompt when the enrolled user's rights allow it, and runs every machine-affecting
''' verb (register, remove, repair, migrate) through the bundled helper
''' <c>exchange\install-exchange-service.ps1</c> via <see cref="ExchangeElevation"/>.
'''
''' The unelevated start/stop is the thing this repository had not done before: the helper grants the
''' enrolled user <c>SERVICE_START | SERVICE_STOP | SERVICE_QUERY_STATUS | SERVICE_INTERROGATE</c> in the
''' service's security descriptor, so <c>OpenServiceW(SERVICE_START | SERVICE_STOP | SERVICE_QUERY_STATUS)</c>
''' succeeds for that user alone. A machine whose descriptor was reset answers ACCESS_DENIED and the call
''' falls back to the helper's <c>start</c> / <c>stop</c> verbs - with a prompt, but never stuck.
'''
''' Where this installation is the "Server edition" is not a flag: the registration helper ships only
''' with the Server installer, so <see cref="CanRegister"/> keys off its presence, exactly as
''' <see cref="ServiceControl.CanManage"/> does for the worker's service.
''' </summary>
Friend Module ExchangeService

    ''' <summary>The frozen SCM service name (ticket 144; the Go side compiles in the same string).</summary>
    Friend Const ServiceName As String = "FastMediaSorterExchangeServer"

    Friend Const HelperScriptName As String = "install-exchange-service.ps1"

    Private Const SC_MANAGER_CONNECT As UInteger = &H1UI
    Private Const SERVICE_QUERY_STATUS As UInteger = &H4UI
    Private Const SERVICE_START As UInteger = &H10UI
    Private Const SERVICE_STOP As UInteger = &H20UI
    Private Const SERVICE_CONTROL_STOP As UInteger = 1UI
    Private Const ERROR_ACCESS_DENIED As Integer = 5
    Private Const ERROR_SERVICE_DOES_NOT_EXIST As Integer = 1060
    Private Const ERROR_SERVICE_ALREADY_RUNNING As Integer = 1056
    Private Const ERROR_SERVICE_NOT_ACTIVE As Integer = 1062

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
    Private Function OpenServiceW(scManager As IntPtr, name As String, desiredAccess As UInteger) As IntPtr
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function QueryServiceStatus(service As IntPtr, ByRef status As SERVICE_STATUS) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function StartServiceW(service As IntPtr, numArgs As UInteger, args As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function ControlService(service As IntPtr, control As UInteger, ByRef status As SERVICE_STATUS) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function CloseServiceHandle(handle As IntPtr) As Boolean
    End Function

    ' --- data directories ---------------------------------------------------------------

    ''' <summary>Tests point the directories somewhere else. Nothing else sets them.</summary>
    Friend MachineDataDirOverride As String
    Friend UserDataDirOverride As String

    ''' <summary>The machine store of the service (the binary prefers it whenever it exists).</summary>
    Friend Function MachineDataDir() As String
        If MachineDataDirOverride IsNot Nothing Then Return MachineDataDirOverride
        Try
            Dim b As String = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            Return If(String.IsNullOrEmpty(b), "", Path.Combine(b, "FastMediaSorterExchange"))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>The per-user store of a console run.</summary>
    Friend Function UserDataDir() As String
        If UserDataDirOverride IsNot Nothing Then Return UserDataDirOverride
        Try
            Dim b As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            Return If(String.IsNullOrEmpty(b), "", Path.Combine(b, "FastMediaSorterExchange"))
        Catch
            Return ""
        End Try
    End Function

    ' --- read: live SCM state and the registration --------------------------------------

    ''' <summary>Tests replace the SCM read. Nothing else sets it.</summary>
    Friend QueryStateOverride As Func(Of ServiceControl.ServiceState)

    ''' <summary>Tests replace the registration read (the service's ImagePath). Nothing else sets it.</summary>
    Friend ImagePathOverride As Func(Of String)

    ''' <summary>The live SCM answer, unelevated. Unknown means the query itself failed - never "not installed".</summary>
    Friend Function QueryState() As ServiceControl.ServiceState
        If QueryStateOverride IsNot Nothing Then Return QueryStateOverride()
        Dim scm As IntPtr = IntPtr.Zero
        Dim svc As IntPtr = IntPtr.Zero
        Try
            scm = OpenSCManagerW(Nothing, Nothing, SC_MANAGER_CONNECT)
            If scm = IntPtr.Zero Then Return ServiceControl.ServiceState.Unknown
            svc = OpenServiceW(scm, ServiceName, SERVICE_QUERY_STATUS)
            If svc = IntPtr.Zero Then
                Return If(Marshal.GetLastWin32Error() = ERROR_SERVICE_DOES_NOT_EXIST, ServiceControl.ServiceState.NotInstalled, ServiceControl.ServiceState.Unknown)
            End If
            Dim st As SERVICE_STATUS = Nothing
            If Not QueryServiceStatus(svc, st) Then Return ServiceControl.ServiceState.Unknown
            Return StateOfCode(st.dwCurrentState)
        Catch
            Return ServiceControl.ServiceState.Unknown
        Finally
            If svc <> IntPtr.Zero Then CloseServiceHandle(svc)
            If scm <> IntPtr.Zero Then CloseServiceHandle(scm)
        End Try
    End Function

    ''' <summary>SERVICE_STATUS.dwCurrentState to the four states the UI keeps apart.</summary>
    Friend Function StateOfCode(code As UInteger) As ServiceControl.ServiceState
        Select Case code
            Case 1UI : Return ServiceControl.ServiceState.Stopped
            Case 2UI : Return ServiceControl.ServiceState.Starting
            Case 3UI : Return ServiceControl.ServiceState.Stopping
            Case 4UI : Return ServiceControl.ServiceState.Running
            Case Else : Return ServiceControl.ServiceState.Unknown
        End Select
    End Function

    ''' <summary>The registered command line of the service, or "". Readable by a standard user.</summary>
    Friend Function RegisteredImagePath() As String
        If ImagePathOverride IsNot Nothing Then Return ImagePathOverride()
        Try
            Dim value As Object = Registry.GetValue("HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" & ServiceName, "ImagePath", Nothing)
            Return If(value Is Nothing, "", Convert.ToString(value))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Pure: a registration is ours when its command line names the exchange exe AND carries
    ''' <c>serve --service</c>. A leftover of another product under the same name must not make the
    ''' console refuse to start something it does not actually manage.</summary>
    Friend Function IsOurImagePath(image As String) As Boolean
        If String.IsNullOrEmpty(image) Then Return False
        If image.IndexOf("serve --service", StringComparison.OrdinalIgnoreCase) < 0 Then Return False
        Return image.IndexOf(ExchangeProcess.ExeName, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>The service exists and is ours - the condition under which "the service wins" (133 section
    ''' 6.1): the console Start is hidden and the binary itself refuses a console run beside it.</summary>
    Friend Function Exists() As Boolean
        Dim state As ServiceControl.ServiceState = QueryState()
        If state = ServiceControl.ServiceState.NotInstalled Then Return False
        Return IsOurImagePath(RegisteredImagePath())
    End Function

    ' --- capabilities -------------------------------------------------------------------

    Friend Function HelperScriptPath() As String
        Dim dir As String = ExchangeProcess.ExeDirectory()
        Return If(dir.Length = 0, "", Path.Combine(dir, HelperScriptName))
    End Function

    ''' <summary>The registration helper and the exe are on disk (the Server installer lays the helper down).</summary>
    Friend Function HelperPresent() As Boolean
        Dim script As String = HelperScriptPath()
        Return script.Length > 0 AndAlso File.Exists(script) AndAlso ExchangeProcess.IsAvailable()
    End Function

    ''' <summary>"Register as a service.." is offered only where the helper shipped (the Server edition), not in
    ''' a Store package (its container cannot register a service), and only while no service exists.</summary>
    Friend Function CanRegister(serviceExists As Boolean) As Boolean
        If serviceExists Then Return False
        Try
            If AutostartManager.IsPackaged() Then Return False
        Catch
        End Try
        Return HelperPresent()
    End Function

    ''' <summary>Removing a registered service needs the helper.</summary>
    Friend Function CanRemove(serviceExists As Boolean) As Boolean
        Return serviceExists AndAlso HelperPresent()
    End Function

    ' --- start / stop without UAC ---------------------------------------------------------

    Friend Enum ControlOutcome
        Done
        ''' <summary>ERROR_ACCESS_DENIED: this user holds no start/stop right on the service.</summary>
        AccessDenied
        Failed
    End Enum

    ''' <summary>Tests replace the SCM write. Nothing else sets it.</summary>
    Friend ControlOverride As Func(Of Boolean, ControlOutcome)

    ''' <summary>StartServiceW / ControlService(STOP) with the narrow access mask only - no admin rights.</summary>
    Friend Function TryControl(start As Boolean) As ControlOutcome
        If ControlOverride IsNot Nothing Then Return ControlOverride(start)
        Dim scm As IntPtr = IntPtr.Zero
        Dim svc As IntPtr = IntPtr.Zero
        Try
            scm = OpenSCManagerW(Nothing, Nothing, SC_MANAGER_CONNECT)
            If scm = IntPtr.Zero Then Return ControlOutcome.Failed
            svc = OpenServiceW(scm, ServiceName, SERVICE_START Or SERVICE_STOP Or SERVICE_QUERY_STATUS)
            If svc = IntPtr.Zero Then
                Return If(Marshal.GetLastWin32Error() = ERROR_ACCESS_DENIED, ControlOutcome.AccessDenied, ControlOutcome.Failed)
            End If
            If start Then
                If StartServiceW(svc, 0UI, IntPtr.Zero) Then Return ControlOutcome.Done
                Dim e As Integer = Marshal.GetLastWin32Error()
                Return If(e = ERROR_SERVICE_ALREADY_RUNNING, ControlOutcome.Done,
                          If(e = ERROR_ACCESS_DENIED, ControlOutcome.AccessDenied, ControlOutcome.Failed))
            End If
            Dim st As SERVICE_STATUS = Nothing
            If ControlService(svc, SERVICE_CONTROL_STOP, st) Then Return ControlOutcome.Done
            Dim e2 As Integer = Marshal.GetLastWin32Error()
            Return If(e2 = ERROR_SERVICE_NOT_ACTIVE, ControlOutcome.Done,
                      If(e2 = ERROR_ACCESS_DENIED, ControlOutcome.AccessDenied, ControlOutcome.Failed))
        Catch
            Return ControlOutcome.Failed
        Finally
            If svc <> IntPtr.Zero Then CloseServiceHandle(svc)
            If scm <> IntPtr.Zero Then CloseServiceHandle(scm)
        End Try
    End Function

    ''' <summary>The helper's -Action values (ticket 144 T-2; param block of install-exchange-service.ps1).</summary>
    Friend Enum Verb
        Install
        Repair
        Remove
        StartService
        StopService
        Restart
        MigrateToService
    End Enum

    Friend Function VerbName(v As Verb) As String
        Select Case v
            Case Verb.Install : Return "install"
            Case Verb.Repair : Return "repair"
            Case Verb.Remove : Return "remove"
            Case Verb.StartService : Return "start"
            Case Verb.StopService : Return "stop"
            Case Verb.Restart : Return "restart"
            Case Else : Return "migrate-to-service"
        End Select
    End Function

    ''' <summary>Registering with a console store already on this account is a MIGRATION: the certificate and
    ''' settings move to the machine store (only when it holds no identity) and the fingerprint is compared
    ''' before and after, so a pinned producer keeps trusting the server. With nothing to carry over it is a
    ''' plain install.</summary>
    Friend Function RegisterVerb() As Verb
        Try
            Dim user As String = UserDataDir()
            If user.Length > 0 AndAlso File.Exists(Path.Combine(user, "exchange.json")) Then Return Verb.MigrateToService
        Catch
        End Try
        Return Verb.Install
    End Function

    ''' <summary>Runs one helper verb elevated (one UAC prompt). The same shape as
    ''' <see cref="ServiceControl.Manage"/>: the exe, the two data directories and the management SID.</summary>
    Friend Function Manage(v As Verb) As ServiceControl.ManageResult
        If Not HelperPresent() Then Return ServiceControl.ManageResult.Unavailable
        Dim pairs As New List(Of String) From {
            "-Action", VerbName(v),
            "-ExePath", ExchangeProcess.ExePath(),
            "-DataDir", MachineDataDir(),
            "-UserDataDir", UserDataDir()}
        Dim sid As String = ServiceControl.CurrentUserSid()
        If sid.Length > 0 Then
            pairs.Add("-ManageSid")
            pairs.Add(sid)
        End If
        Dim code As Integer
        Dim outcome As ExchangeElevation.ElevatedOutcome =
            ExchangeElevation.RunHelper("Exchange service: " & VerbName(v), HelperScriptPath(), code, pairs.ToArray())
        If code <> 0 Then AppFileLogger.WriteLine("Exchange service: " & VerbName(v) & " - " & HelperExitMeaning(code))
        Select Case outcome
            Case ExchangeElevation.ElevatedOutcome.Succeeded : Return ServiceControl.ManageResult.Succeeded
            Case ExchangeElevation.ElevatedOutcome.Declined : Return ServiceControl.ManageResult.Declined
            Case ExchangeElevation.ElevatedOutcome.Unavailable : Return ServiceControl.ManageResult.Unavailable
            Case Else : Return ServiceControl.ManageResult.Failed
        End Select
    End Function

    ''' <summary>Start or stop WITHOUT a prompt where the user's rights allow it; when the SCM says access
    ''' denied, the helper verb does it with one prompt. Waits for the state the caller asked for.</summary>
    Friend Function StartOrStop(start As Boolean) As ServiceControl.ManageResult
        Dim o As ControlOutcome = TryControl(start)
        If o = ControlOutcome.AccessDenied Then
            Return Manage(If(start, Verb.StartService, Verb.StopService))
        End If
        If o <> ControlOutcome.Done Then Return ServiceControl.ManageResult.Failed
        Dim target As ServiceControl.ServiceState = If(start, ServiceControl.ServiceState.Running, ServiceControl.ServiceState.Stopped)
        Return If(WaitForState(target, StateWaitMs), ServiceControl.ManageResult.Succeeded, ServiceControl.ManageResult.Failed)
    End Function

    ''' <summary>How long a start or stop waits for the state it asked for. Tests shorten it.</summary>
    Friend StateWaitMs As Integer = 15000

    Private Function WaitForState(target As ServiceControl.ServiceState, timeoutMs As Integer) As Boolean
        Dim deadline As DateTime = DateTime.UtcNow.AddMilliseconds(timeoutMs)
        Do
            If QueryState() = target Then Return True
            If DateTime.UtcNow >= deadline Then Return False
            Thread.Sleep(250)
        Loop
    End Function

    ''' <summary>What a helper exit code means, for companion.log (ticket 144 T-2 table).</summary>
    Friend Function HelperExitMeaning(code As Integer) As String
        Select Case code
            Case 2 : Return "missing argument or exe"
            Case 3 : Return "the service did not start"
            Case 4 : Return "an ACL could not be set"
            Case 5 : Return "not elevated"
            Case 6 : Return "the state did not validate"
            Case 7 : Return "certificate mismatch - nothing was registered"
            Case 8 : Return "firewall"
            Case 9 : Return "the service is not installed"
            Case 10 : Return "several identities - the migration source is ambiguous"
            Case 11 : Return "the state directory cannot be trusted - see install-exchange-service.log in the administrator's %TEMP% (the data directory's logs\ is not opened until it is trusted)"
            Case 13 : Return "the service account is not LocalService"
            Case 14 : Return "the old registration is still marked for deletion"
            Case 16 : Return "another run of the helper holds the state lock - nothing was changed"
            Case Else : Return "unexpected error - see " & HelperLogHint()
        End Select
    End Function

    ''' <summary>Where the helper's own log is: <c>logs\install-exchange-service.log</c> in the machine data
    ''' directory once that directory was verified (ticket 182 L-1: the one place the enrolled user can
    ''' read, so "Send the log to the author" carries it), else the elevated account's %TEMP% copy.</summary>
    Friend Function HelperLogHint() As String
        Return "install-exchange-service.log in the logs folder of the exchange data directory (or in the administrator's %TEMP%)"
    End Function

End Module
