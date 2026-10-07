#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text.Json
Imports Microsoft.Win32

''' <summary>
''' The real answers behind <see cref="HostingReport"/> (ticket 182 L-4 and L-8): read-only, no elevation,
''' no network, nothing started and nothing written. Every service question is an SCM query with the
''' narrowest mask that answers it (SERVICE_QUERY_STATUS), the registration facts come from the HKLM
''' services key every user can read, the firewall is read through its COM policy object, the sockets
''' from the kernel's own table, and a program's version from its file's version resource - the
''' programs themselves are never run.
'''
''' Each function answers for itself and swallows its own failure into "unknown": the caller wraps
''' every probe again, so a machine that refuses a question costs one line, never the report.
''' </summary>
Friend Module HostingProbesLive

    ' --- wiring ------------------------------------------------------------------------------

    ''' <summary>The probes of this process on this machine. The firewall policy is read once per gather
    ''' (it lists a few hundred rules and is asked about five programs).</summary>
    Friend Function Live() As HostingProbes
        Dim rules As New Lazy(Of List(Of FirewallRule))(AddressOf ReadFirewallRules)
        Return New HostingProbes With {
            .Os = AddressOf ReadOs,
            .Session = AddressOf ReadSession,
            .ServiceInfo = AddressOf ReadService,
            .HostMode = Function() If(HostingProbes.HostModeProvider Is Nothing, "", HostingProbes.HostModeProvider()),
            .ProgramPaths = AddressOf ProgramPaths,
            .FileVersion = AddressOf FileVersionOf,
            .FileStamp = AddressOf FileStampOf,
            .FirewallRules = Function(path) RuleNamesFor(rules.Value, path),
            .Ports = AddressOf ReadPorts,
            .Sockets = AddressOf ReadSockets,
            .Components = AddressOf ReadComponents,
            .Directories = AddressOf ReadDirectories,
            .MailHandler = AddressOf MailSender.HasMailHandler,
            .Reachability = Function() If(HostingProbes.ReachabilityProvider Is Nothing, "", HostingProbes.ReachabilityProvider())}
    End Function

    Private ReadOnly Invariant As CultureInfo = CultureInfo.InvariantCulture

    ' --- OS, memory, disk, session -------------------------------------------------------------

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MemoryStatusEx
        Public dwLength As UInteger
        Public dwMemoryLoad As UInteger
        Public ullTotalPhys As ULong
        Public ullAvailPhys As ULong
        Public ullTotalPageFile As ULong
        Public ullAvailPageFile As ULong
        Public ullTotalVirtual As ULong
        Public ullAvailVirtual As ULong
        Public ullAvailExtendedVirtual As ULong
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function GlobalMemoryStatusEx(ByRef status As MemoryStatusEx) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function GetSystemMetrics(index As Integer) As Integer
    End Function

    Private Const SM_REMOTESESSION As Integer = &H1000

    Friend Function ReadOs() As OsFacts
        Dim os As New OsFacts With {
            .Known = True, .ProductName = "", .Version = "", .Build = "", .EditionId = "", .InstallationType = "",
            .SystemDrive = "", .SystemDriveFreeMb = -1, .SystemDriveTotalMb = 0,
            .ProcessorCount = Environment.ProcessorCount, .Is64BitOs = Environment.Is64BitOperatingSystem}
        Try
            Dim v As Version = Environment.OSVersion.Version
            os.Version = v.Major.ToString(Invariant) & "." & v.Minor.ToString(Invariant) & "." & v.Build.ToString(Invariant)
            os.Build = v.Build.ToString(Invariant)
        Catch
        End Try
        Try
            Using key As RegistryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\Microsoft\Windows NT\CurrentVersion", False)
                If key IsNot Nothing Then
                    os.ProductName = Str(key.GetValue("ProductName"))
                    os.EditionId = Str(key.GetValue("EditionID"))
                    os.InstallationType = Str(key.GetValue("InstallationType"))
                    Dim build As String = Str(key.GetValue("CurrentBuildNumber"))
                    Dim revision As String = Str(key.GetValue("UBR"))
                    If build.Length > 0 Then os.Build = If(revision.Length > 0, build & "." & revision, build)
                End If
            End Using
        Catch
        End Try
        os.IsServer = IsServerEdition(os.InstallationType, os.ProductName)

        Try
            Dim status As New MemoryStatusEx With {.dwLength = CUInt(Marshal.SizeOf(GetType(MemoryStatusEx)))}
            If GlobalMemoryStatusEx(status) Then os.TotalMemoryMb = CLng(status.ullTotalPhys \ (1024UL * 1024UL))
        Catch
        End Try
        Try
            Dim root As String = Path.GetPathRoot(Environment.SystemDirectory)
            If Not String.IsNullOrEmpty(root) Then
                Dim drive As New DriveInfo(root)
                os.SystemDrive = root.TrimEnd("\"c)
                os.SystemDriveFreeMb = drive.AvailableFreeSpace \ (1024L * 1024L)
                os.SystemDriveTotalMb = drive.TotalSize \ (1024L * 1024L)
            End If
        Catch
        End Try
        Return os
    End Function

    Private Function Str(value As Object) As String
        If value Is Nothing Then Return ""
        Return Convert.ToString(value, Invariant).Trim()
    End Function

    ''' <summary>Pure: a Server install says so in InstallationType (Server, Server Core, Nano Server);
    ''' an old or odd registry falls back to the product name.</summary>
    Friend Function IsServerEdition(installationType As String, productName As String) As Boolean
        If Not String.IsNullOrEmpty(installationType) Then
            Return installationType.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0
        End If
        Return Not String.IsNullOrEmpty(productName) AndAlso productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Friend Function ReadSession() As SessionFacts
        Dim session As New SessionFacts With {.Known = False}
        Try
            Using current As Process = Process.GetCurrentProcess()
                session.SessionId = current.SessionId
            End Using
            session.IsRemoteDesktop = GetSystemMetrics(SM_REMOTESESSION) <> 0
            session.Known = True
        Catch
        End Try
        Return session
    End Function

    ' --- services ----------------------------------------------------------------------------------

    Private Const SC_MANAGER_CONNECT As UInteger = &H1UI
    Private Const SERVICE_QUERY_STATUS As UInteger = &H4UI
    Private Const SC_STATUS_PROCESS_INFO As Integer = 0
    Private Const ERROR_SERVICE_DOES_NOT_EXIST As Integer = 1060

    <StructLayout(LayoutKind.Sequential)>
    Private Structure ServiceStatusProcess
        Public dwServiceType As UInteger
        Public dwCurrentState As UInteger
        Public dwControlsAccepted As UInteger
        Public dwWin32ExitCode As UInteger
        Public dwServiceSpecificExitCode As UInteger
        Public dwCheckPoint As UInteger
        Public dwWaitHint As UInteger
        Public dwProcessId As UInteger
        Public dwServiceFlags As UInteger
    End Structure

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function OpenSCManagerW(machineName As String, databaseName As String, desiredAccess As UInteger) As IntPtr
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function OpenServiceW(scManager As IntPtr, serviceName As String, desiredAccess As UInteger) As IntPtr
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function QueryServiceStatusEx(service As IntPtr, infoLevel As Integer, ByRef buffer As ServiceStatusProcess,
                                          bufSize As Integer, ByRef bytesNeeded As Integer) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function CloseServiceHandle(handle As IntPtr) As Boolean
    End Function

    ''' <summary>One service: the registration from the services key (readable by every user), the live state
    ''' and exit codes from the SCM with SERVICE_QUERY_STATUS only. An SCM that refuses the question leaves the
    ''' state "unknown" but does not lose the registration.</summary>
    Friend Function ReadService(name As String) As ServiceFacts
        Dim fact As New ServiceFacts With {.Name = name, .State = "", .StartType = "", .Account = "", .Registered = False}
        Dim inRegistry As Boolean = False
        Try
            Using key As RegistryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Services\" & name, False)
                If key IsNot Nothing Then
                    inRegistry = True
                    fact.Account = Str(key.GetValue("ObjectName"))
                    Dim start As Integer = -1
                    Integer.TryParse(Str(key.GetValue("Start")), NumberStyles.Integer, Invariant, start)
                    Dim delayed As Boolean = Str(key.GetValue("DelayedAutostart")) = "1"
                    fact.StartType = StartTypeName(start, delayed)
                End If
            End Using
        Catch
        End Try

        Dim scm As IntPtr = IntPtr.Zero
        Dim svc As IntPtr = IntPtr.Zero
        Try
            scm = OpenSCManagerW(Nothing, Nothing, SC_MANAGER_CONNECT)
            If scm = IntPtr.Zero Then
                fact.Registered = inRegistry
                fact.QueryFailed = Not inRegistry
                fact.State = "unknown"
                Return fact
            End If
            svc = OpenServiceW(scm, name, SERVICE_QUERY_STATUS)
            If svc = IntPtr.Zero Then
                If Marshal.GetLastWin32Error() = ERROR_SERVICE_DOES_NOT_EXIST Then
                    fact.Registered = False
                Else
                    ' Access denied or similar: the key tells us it exists, the state stays unknown.
                    fact.Registered = inRegistry
                    fact.QueryFailed = Not inRegistry
                    fact.State = "unknown"
                End If
                Return fact
            End If
            fact.Registered = True
            Dim status As New ServiceStatusProcess()
            Dim needed As Integer
            If QueryServiceStatusEx(svc, SC_STATUS_PROCESS_INFO, status, Marshal.SizeOf(GetType(ServiceStatusProcess)), needed) Then
                fact.State = StateName(status.dwCurrentState)
                fact.Win32ExitCode = CInt(status.dwWin32ExitCode)
                fact.ServiceSpecificExitCode = CInt(status.dwServiceSpecificExitCode)
            Else
                fact.State = "unknown"
            End If
        Catch
            fact.State = If(fact.State.Length = 0, "unknown", fact.State)
            fact.Registered = fact.Registered OrElse inRegistry
        Finally
            If svc <> IntPtr.Zero Then CloseServiceHandle(svc)
            If scm <> IntPtr.Zero Then CloseServiceHandle(scm)
        End Try
        Return fact
    End Function

    Friend Function StateName(code As UInteger) As String
        Select Case code
            Case 1UI : Return "Stopped"
            Case 2UI : Return "Starting"
            Case 3UI : Return "Stopping"
            Case 4UI : Return "Running"
            Case 5UI : Return "Continue pending"
            Case 6UI : Return "Pause pending"
            Case 7UI : Return "Paused"
            Case Else : Return "unknown"
        End Select
    End Function

    ''' <summary>The services key's Start value as a word (0 boot, 1 system, 2 automatic, 3 manual, 4 disabled).</summary>
    Friend Function StartTypeName(start As Integer, delayedAutostart As Boolean) As String
        Select Case start
            Case 0 : Return "Boot"
            Case 1 : Return "System"
            Case 2 : Return If(delayedAutostart, "Automatic (delayed)", "Automatic")
            Case 3 : Return "Manual"
            Case 4 : Return "Disabled"
            Case Else : Return ""
        End Select
    End Function

    ' --- programs, versions, components ---------------------------------------------------------------

    Friend Function AppBaseDirectory() As String
        Try
            Return If(AppContext.BaseDirectory, "")
        Catch
            Return ""
        End Try
    End Function

    Private Function CommonStore(name As String) As String
        Try
            Dim common As String = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            Return If(String.IsNullOrEmpty(common), "", Path.Combine(common, name))
        Catch
            Return ""
        End Try
    End Function

    Private Function UserStore(name As String) As String
        Try
            Dim local As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            Return If(String.IsNullOrEmpty(local), "", Path.Combine(local, name))
        Catch
            Return ""
        End Try
    End Function

    Friend Function ProgramPaths() As List(Of KeyValuePair(Of String, String))
        Dim list As New List(Of KeyValuePair(Of String, String))()
        Dim baseDir As String = AppBaseDirectory()
        If baseDir.Length > 0 Then
            list.Add(New KeyValuePair(Of String, String)("Share Manager", Path.Combine(baseDir, "FastMediaSorterCompanion.exe")))
            list.Add(New KeyValuePair(Of String, String)("share worker, installed", Path.Combine(baseDir, "companion", "fms-share-worker.exe")))
            list.Add(New KeyValuePair(Of String, String)("exchange server, installed", Path.Combine(baseDir, "exchange", "fms-exchange-server.exe")))
        End If
        Dim shareStore As String = CommonStore("FastMediaSorterCompanion")
        If shareStore.Length > 0 Then list.Add(New KeyValuePair(Of String, String)("share worker, service copy", Path.Combine(shareStore, "bin", "fms-share-worker.exe")))
        Dim exchangeStore As String = CommonStore("FastMediaSorterExchange")
        If exchangeStore.Length > 0 Then list.Add(New KeyValuePair(Of String, String)("exchange server, service copy", Path.Combine(exchangeStore, "bin", "fms-exchange-server.exe")))
        Return list
    End Function

    ''' <summary>Nothing when the file is absent, "" when present with no readable version resource.</summary>
    Friend Function FileVersionOf(path As String) As String
        Try
            If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then Return Nothing
        Catch
            Return Nothing
        End Try
        Try
            Dim info As FileVersionInfo = FileVersionInfo.GetVersionInfo(path)
            If Not String.IsNullOrEmpty(info.FileVersion) Then Return info.FileVersion
            If Not String.IsNullOrEmpty(info.ProductVersion) Then Return info.ProductVersion
            Return ""
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Size and last-write time (UTC) of a file, as one phrase; Nothing when it is not there. What tells two
    ''' builds of a program with no version resource apart.</summary>
    Friend Function FileStampOf(path As String) As String
        Try
            Dim info As New FileInfo(path)
            If Not info.Exists Then Return Nothing
            Return info.Length.ToString(Invariant) & " bytes, modified " & info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm", Invariant) & " UTC"
        Catch
            Return Nothing
        End Try
    End Function

    Friend Function ReadComponents() As List(Of KeyValuePair(Of String, Boolean))
        Dim baseDir As String = AppBaseDirectory()
        Dim list As New List(Of KeyValuePair(Of String, Boolean))()
        Dim viewer As Boolean = False
        Dim manager As Boolean = False
        Dim worker As Boolean = False
        Dim exchange As Boolean = False
        Try
            If baseDir.Length > 0 Then
                viewer = File.Exists(Path.Combine(baseDir, "FastMediaSorter_LITE.exe")) OrElse File.Exists(Path.Combine(baseDir, "FastMediaSorter_x86.exe"))
                manager = File.Exists(Path.Combine(baseDir, "FastMediaSorterCompanion.exe"))
                worker = File.Exists(Path.Combine(baseDir, "companion", "fms-share-worker.exe"))
                exchange = File.Exists(Path.Combine(baseDir, "exchange", "fms-exchange-server.exe"))
            End If
        Catch
        End Try
        list.Add(New KeyValuePair(Of String, Boolean)("viewer", viewer))
        list.Add(New KeyValuePair(Of String, Boolean)("Share Manager", manager))
        list.Add(New KeyValuePair(Of String, Boolean)("share worker", worker))
        list.Add(New KeyValuePair(Of String, Boolean)("exchange server (exchange\ tree)", exchange))
        Return list
    End Function

    ' --- ports -------------------------------------------------------------------------------------

    Private Const ExchangeDefaultPort As Integer = 22022

    ''' <summary>The exchange server's configured numbers - from its own config file when this user can read
    ''' it (the service's enrolled user can; a console run's store is theirs), nothing else of that file is
    ''' read - and the Share listen port from the Share Manager's mirror of it.</summary>
    Friend Function ReadPorts() As PortFacts
        Dim ports As New PortFacts()
        For Each dir As String In ExchangeDataDirs()
            Dim config As String = Path.Combine(dir, "exchange.json")
            Dim listen As Integer = 0, low As Integer = 0, high As Integer = 0
            If TryReadExchangeNumbers(config, listen, low, high) Then
                ports.ExchangeListen = listen
                ports.ExchangeRangeLow = low
                ports.ExchangeRangeHigh = high
                Exit For
            End If
        Next
        Try
            Dim raw As String = GetSetting(App_name, Second_App_Name, "Share_ListenPort", "0")
            Dim parsed As Integer
            If Integer.TryParse(raw, NumberStyles.Integer, Invariant, parsed) AndAlso parsed > 0 AndAlso parsed <= 65535 Then ports.ShareListen = parsed
        Catch
        End Try
        Return ports
    End Function

    ''' <summary>The exchange data directories in the order the server itself prefers them: an explicit
    ''' override, the machine store, then the per-user store.</summary>
    Friend Function ExchangeDataDirs() As List(Of String)
        Dim list As New List(Of String)()
        Try
            Dim overrideDir As String = Environment.GetEnvironmentVariable("FMS_EXCHANGE_DATA_DIR")
            If Not String.IsNullOrWhiteSpace(overrideDir) Then list.Add(overrideDir)
        Catch
        End Try
        Dim machine As String = CommonStore("FastMediaSorterExchange")
        If machine.Length > 0 Then list.Add(machine)
        Dim user As String = UserStore("FastMediaSorterExchange")
        If user.Length > 0 Then list.Add(user)
        Return list
    End Function

    ''' <summary>Reads ONLY "listen" (":22022") and "portRange" ("40000-40100") out of the server's config.
    ''' Every other member - the password record above all - is parsed past and dropped, never kept.</summary>
    Friend Function TryReadExchangeNumbers(configPath As String, ByRef listen As Integer, ByRef low As Integer, ByRef high As Integer) As Boolean
        listen = 0
        low = 0
        high = 0
        Try
            If String.IsNullOrEmpty(configPath) OrElse Not File.Exists(configPath) Then Return False
            Dim bytes As Byte()
            Using stream As New FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                If stream.Length > 1024L * 1024L Then Return False
                ReDim bytes(CInt(stream.Length) - 1)
                Dim read As Integer = 0
                While read < bytes.Length
                    Dim n As Integer = stream.Read(bytes, read, bytes.Length - read)
                    If n <= 0 Then Exit While
                    read += n
                End While
            End Using
            Using doc As JsonDocument = JsonDocument.Parse(bytes, New JsonDocumentOptions With {.AllowTrailingCommas = True, .CommentHandling = JsonCommentHandling.Skip})
                Dim element As JsonElement
                If doc.RootElement.ValueKind = JsonValueKind.Object AndAlso doc.RootElement.TryGetProperty("listen", element) AndAlso element.ValueKind = JsonValueKind.String Then
                    listen = PortOfListenAddress(element.GetString())
                End If
                If doc.RootElement.ValueKind = JsonValueKind.Object AndAlso doc.RootElement.TryGetProperty("portRange", element) AndAlso element.ValueKind = JsonValueKind.String Then
                    ParsePortRange(element.GetString(), low, high)
                End If
            End Using
            ' A config that exists but names neither number still means "the server's defaults apply".
            If listen <= 0 Then listen = ExchangeDefaultPort
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>"host:port" or ":port" to the port, 0 when it is not one. Pure.</summary>
    Friend Function PortOfListenAddress(listen As String) As Integer
        If String.IsNullOrWhiteSpace(listen) Then Return 0
        Dim colon As Integer = listen.LastIndexOf(":"c)
        If colon < 0 Then Return 0
        Dim port As Integer
        If Integer.TryParse(listen.Substring(colon + 1).Trim(), NumberStyles.Integer, Invariant, port) AndAlso port > 0 AndAlso port <= 65535 Then Return port
        Return 0
    End Function

    ''' <summary>"lo-hi" to two numbers; both stay 0 when it is not a range. Pure.</summary>
    Friend Sub ParsePortRange(text As String, ByRef low As Integer, ByRef high As Integer)
        low = 0
        high = 0
        If String.IsNullOrWhiteSpace(text) Then Return
        Dim dash As Integer = text.IndexOf("-"c)
        If dash <= 0 Then Return
        Dim a As Integer, b As Integer
        If Integer.TryParse(text.Substring(0, dash).Trim(), NumberStyles.Integer, Invariant, a) AndAlso
           Integer.TryParse(text.Substring(dash + 1).Trim(), NumberStyles.Integer, Invariant, b) AndAlso
           a >= 1 AndAlso b <= 65535 AndAlso a <= b Then
            low = a
            high = b
        End If
    End Sub

    ' --- sockets ---------------------------------------------------------------------------------

    <DllImport("iphlpapi.dll", SetLastError:=True)>
    Private Function GetExtendedTcpTable(table As IntPtr, ByRef size As Integer, sort As Boolean,
                                         family As Integer, tableClass As Integer, reserved As UInteger) As UInteger
    End Function

    Private Const AF_INET As Integer = 2
    Private Const AF_INET6 As Integer = 23
    Private Const TCP_TABLE_OWNER_PID_LISTENER As Integer = 3
    Private Const ERROR_INSUFFICIENT_BUFFER As UInteger = 122UI

    ''' <summary>The processes whose sockets the block names: the Share Manager, the share worker, the
    ''' exchange server. Names are matched case-insensitively, without ".exe".</summary>
    Friend ReadOnly OurProcessNames As String() = {"FastMediaSorterCompanion", "fms-share-worker", "fms-exchange-server"}

    Friend Function ReadSockets() As List(Of SocketFacts)
        Dim names As Dictionary(Of Integer, String) = OurProcessesById()
        Dim list As New List(Of SocketFacts)()
        If names.Count = 0 Then Return list
        For Each family As Integer In {AF_INET, AF_INET6}
            ReadListeners(family, names, list)
        Next
        Return list
    End Function

    Private Function OurProcessesById() As Dictionary(Of Integer, String)
        Dim names As New Dictionary(Of Integer, String)()
        Dim all As Process() = Process.GetProcesses()
        For Each p As Process In all
            Try
                For Each ours As String In OurProcessNames
                    If String.Equals(p.ProcessName, ours, StringComparison.OrdinalIgnoreCase) Then
                        names(p.Id) = ours
                        Exit For
                    End If
                Next
            Catch
            Finally
                p.Dispose()
            End Try
        Next
        Return names
    End Function

    Private Sub ReadListeners(family As Integer, names As Dictionary(Of Integer, String), into As List(Of SocketFacts))
        Dim size As Integer = 0
        Dim result As UInteger = GetExtendedTcpTable(IntPtr.Zero, size, False, family, TCP_TABLE_OWNER_PID_LISTENER, 0UI)
        If result <> ERROR_INSUFFICIENT_BUFFER AndAlso result <> 0UI Then Return
        For attempt As Integer = 1 To 4
            size += 4096 * attempt
            Dim buffer As IntPtr = Marshal.AllocHGlobal(size)
            Try
                result = GetExtendedTcpTable(buffer, size, False, family, TCP_TABLE_OWNER_PID_LISTENER, 0UI)
                If result = ERROR_INSUFFICIENT_BUFFER Then Continue For
                If result <> 0UI Then Return
                Dim count As Integer = Marshal.ReadInt32(buffer, 0)
                Dim rowSize As Integer = If(family = AF_INET, 24, 56)
                For i As Integer = 0 To count - 1
                    Dim row As IntPtr = IntPtr.Add(buffer, 4 + i * rowSize)
                    Dim pid As Integer
                    Dim port As Integer
                    Dim scope As String
                    If family = AF_INET Then
                        Dim address As Integer = Marshal.ReadInt32(row, 4)
                        port = PortOfNetworkOrder(Marshal.ReadInt32(row, 8))
                        pid = Marshal.ReadInt32(row, 20)
                        scope = ScopeOfIPv4(address)
                    Else
                        Dim address(15) As Byte
                        Marshal.Copy(row, address, 0, 16)
                        port = PortOfNetworkOrder(Marshal.ReadInt32(row, 20))
                        pid = Marshal.ReadInt32(row, 52)
                        scope = ScopeOfIPv6(address)
                    End If
                    Dim name As String = Nothing
                    If names.TryGetValue(pid, name) Then
                        into.Add(New SocketFacts With {.Process = name, .ProcessId = pid, .Port = port, .Scope = scope})
                    End If
                Next
                Return
            Finally
                Marshal.FreeHGlobal(buffer)
            End Try
        Next
    End Sub

    ''' <summary>The table stores the port in network byte order in the low 16 bits of a DWORD. Pure.</summary>
    Friend Function PortOfNetworkOrder(raw As Integer) As Integer
        Dim low As Integer = raw And &HFFFF
        Return ((low And &HFF) << 8) Or ((low >> 8) And &HFF)
    End Function

    ''' <summary>"any IPv4", "loopback" or "one address" - the address itself is never kept. Pure.
    ''' The DWORD is the address in network order read as a little-endian integer.</summary>
    Friend Function ScopeOfIPv4(address As Integer) As String
        If address = 0 Then Return "any IPv4"
        If (address And &HFF) = 127 Then Return "loopback"
        Return "one address"
    End Function

    Friend Function ScopeOfIPv6(address As Byte()) As String
        If address Is Nothing OrElse address.Length <> 16 Then Return "one address"
        If address.All(Function(b) b = 0) Then Return "any IPv6"
        If address.Take(15).All(Function(b) b = 0) AndAlso address(15) = 1 Then Return "loopback"
        Return "one address"
    End Function

    ' --- firewall --------------------------------------------------------------------------------------

    Friend Structure FirewallRule
        Public Name As String
        Public Program As String
        Public Description As String
    End Structure

    ''' <summary>Every rule that names a program, read through the firewall's COM policy object (readable
    ''' without elevation). Late-bound on purpose: there is no interop assembly to ship for one read.
    ''' Throws when the policy object is unavailable - the caller records the probe as failed.</summary>
    Friend Function ReadFirewallRules() As List(Of FirewallRule)
        Dim list As New List(Of FirewallRule)()
        Dim type As Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
        If type Is Nothing Then Throw New InvalidOperationException("the firewall policy object is not registered")
        Dim policy As Object = Activator.CreateInstance(type)
        Try
            Dim rules As Object = CallByName(policy, "Rules", CallType.Get)
            Dim enumerable As System.Collections.IEnumerable = TryCast(rules, System.Collections.IEnumerable)
            If enumerable Is Nothing Then Throw New InvalidOperationException("the firewall rules cannot be enumerated")
            For Each rule As Object In enumerable
                Try
                    Dim program As String = Convert.ToString(CallByName(rule, "ApplicationName", CallType.Get), Invariant)
                    If String.IsNullOrEmpty(program) Then Continue For
                    Dim name As String = Convert.ToString(CallByName(rule, "Name", CallType.Get), Invariant)
                    Dim enabled As Boolean = Convert.ToBoolean(CallByName(rule, "Enabled", CallType.Get), Invariant)
                    Dim direction As Integer = Convert.ToInt32(CallByName(rule, "Direction", CallType.Get), Invariant)
                    Dim action As Integer = Convert.ToInt32(CallByName(rule, "Action", CallType.Get), Invariant)
                    list.Add(New FirewallRule With {
                        .Name = If(name, ""), .Program = program,
                        .Description = DescribeRuleFlags(direction, action, enabled)})
                Catch
                    ' One unreadable rule is not a reason to lose the others.
                End Try
            Next
        Finally
            Try
                If Marshal.IsComObject(policy) Then Marshal.FinalReleaseComObject(policy)
            Catch
            End Try
        End Try
        Return list
    End Function

    ''' <summary>"in allow, enabled" - the three facts that decide whether a rule lets a connection in.
    ''' Pure (NET_FW_RULE_DIRECTION: 1 in, 2 out; NET_FW_ACTION: 0 block, 1 allow).</summary>
    Friend Function DescribeRuleFlags(direction As Integer, action As Integer, enabled As Boolean) As String
        Return If(direction = 1, "in", If(direction = 2, "out", "dir?")) & " " &
               If(action = 1, "allow", If(action = 0, "block", "action?")) & ", " & If(enabled, "enabled", "disabled")
    End Function

    ''' <summary>The rule names aimed at exactly this program file (case-insensitive, same full path),
    ''' each with its direction, action and enabled flag - and never a port, an address or a profile.</summary>
    Friend Function RuleNamesFor(rules As List(Of FirewallRule), programPath As String) As List(Of String)
        Dim names As New List(Of String)()
        If rules Is Nothing OrElse String.IsNullOrEmpty(programPath) Then Return names
        Dim wanted As String = NormalizeProgramPath(programPath)
        For Each rule As FirewallRule In rules
            If String.Equals(NormalizeProgramPath(rule.Program), wanted, StringComparison.OrdinalIgnoreCase) Then
                names.Add(rule.Name & " [" & rule.Description & "]")
            End If
        Next
        Return names
    End Function

    Private Function NormalizeProgramPath(programPath As String) As String
        Try
            Return Path.GetFullPath(Environment.ExpandEnvironmentVariables(programPath.Trim()))
        Catch
            Return If(programPath, "")
        End Try
    End Function

    ' --- data directories ---------------------------------------------------------------------------

    Friend Function ReadDirectories() As List(Of DirectoryFacts)
        Dim list As New List(Of DirectoryFacts)()
        Dim exchangeMachine As String = CommonStore("FastMediaSorterExchange")
        If exchangeMachine.Length > 0 Then
            list.Add(ProbeDirectory("exchange data directory, machine store", exchangeMachine))
            list.Add(ProbeDirectory("exchange logs directory, machine store", Path.Combine(exchangeMachine, "logs")))
        End If
        Dim exchangeUser As String = UserStore("FastMediaSorterExchange")
        If exchangeUser.Length > 0 Then list.Add(ProbeDirectory("exchange data directory, this user's store", exchangeUser))
        Dim shareMachine As String = CommonStore("FastMediaSorterCompanion")
        If shareMachine.Length > 0 Then list.Add(ProbeDirectory("share data directory, machine store", shareMachine))
        Dim shareUser As String = UserStore("FastMediaSorterCompanion")
        If shareUser.Length > 0 Then list.Add(ProbeDirectory("share data directory, this user's store", shareUser))
        Return list
    End Function

    ''' <summary>Exists, and whether THIS user can list it. Listing is the honest test: it is what "read the
    ''' logs" needs of the directory, and it fails with a plain access-denied where the ACL gives nothing.</summary>
    Friend Function ProbeDirectory(label As String, path As String) As DirectoryFacts
        Dim fact As New DirectoryFacts With {.Label = label, .Exists = False, .Readable = False, .Reason = ""}
        Try
            If Not Directory.Exists(path) Then Return fact
            fact.Exists = True
            Using entries As IEnumerator(Of String) = Directory.EnumerateFileSystemEntries(path).GetEnumerator()
                entries.MoveNext()
            End Using
            fact.Readable = True
        Catch ex As UnauthorizedAccessException
            fact.Reason = "access denied"
        Catch ex As Exception
            fact.Reason = "could not be listed (" & ex.GetType().Name & ")"
        End Try
        Return fact
    End Function

End Module
#End If
