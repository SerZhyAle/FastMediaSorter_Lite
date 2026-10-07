#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

''' <summary>
''' Ticket 182 L-4, L-6 and L-8: the "[hosting]" block of report.txt, the one state line of a log, and the
''' decision "is there a mail client at all". Every case is answered by STUB probes - no service, no
''' firewall, no elevation, no machine state - so a test run on any developer PC says the same thing; one
''' smoke test at the end runs the live probes to prove they never throw on the machine they run on.
'''
''' Compiled into Lite.Tests (the viewer links these modules) and into Companion.Tests (the Share Manager
''' links them too) - <c>COMPANION_TESTS</c> selects the namespace import.
''' </summary>
Public Class HostingReportTests

    Private Shared Function RunningService(name As String) As HostingReport.ServiceFacts
        Return New HostingReport.ServiceFacts With {
            .Name = name, .Registered = True, .State = "Running", .StartType = "Automatic (delayed)",
            .Account = "NT AUTHORITY\LocalService", .Win32ExitCode = 0, .ServiceSpecificExitCode = 0}
    End Function

    Private Shared Function ServerOs() As HostingReport.OsFacts
        Return New HostingReport.OsFacts With {
            .Known = True, .ProductName = "Windows Server 2025 Datacenter", .Version = "10.0.26100", .Build = "26100.4061",
            .EditionId = "ServerDatacenter", .InstallationType = "Server", .IsServer = True, .Is64BitOs = True,
            .TotalMemoryMb = 16384, .ProcessorCount = 8, .SystemDrive = "C:", .SystemDriveFreeMb = 120000, .SystemDriveTotalMb = 476000}
    End Function

    ' ------------------------------------------------------------------ the block itself ------

    <Fact>
    Public Sub An_empty_machine_renders_without_throwing_and_says_that_no_service_exists()
        Dim text As String = HostingReport.Build(HostingReport.HostingProbes.Absent())
        Assert.Contains("Service FastMediaSorterCompanionSFTP: not registered", text)
        Assert.Contains("Service FastMediaSorterExchangeServer: not registered", text)
        Assert.Contains("Neither service is registered on this PC", text)
        Assert.Contains("Listening sockets of our processes: none", text)
        Assert.Contains("Mail client for Send the log: none found", text)
        Assert.Contains("OS: not available", text)
        Assert.DoesNotContain("Probes that failed", text)
    End Sub

    <Fact>
    Public Sub A_server_host_with_both_services_running_is_described_in_full()
        Dim probes As HostingReport.HostingProbes = HostingReport.HostingProbes.Absent()
        probes.Os = Function() ServerOs()
        probes.Session = Function() New HostingReport.SessionFacts With {.Known = True, .SessionId = 2, .IsRemoteDesktop = True}
        probes.ServiceInfo = Function(name) RunningService(name)
        probes.HostMode = Function() "SystemService"
        probes.Components = Function() New List(Of KeyValuePair(Of String, Boolean)) From {
            New KeyValuePair(Of String, Boolean)("viewer", True), New KeyValuePair(Of String, Boolean)("exchange server (exchange\ tree)", True)}
        probes.ProgramPaths = Function() New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("exchange server, installed", "C:\app\exchange\fms-exchange-server.exe"),
            New KeyValuePair(Of String, String)("share worker, installed", "C:\app\companion\fms-share-worker.exe")}
        probes.FileVersion = Function(path) If(path.Contains("exchange"), "26.10.7.1200", If(path.Contains("worker"), "", Nothing))
        probes.FileStamp = Function(path) If(path.Contains("exchange") OrElse path.Contains("worker"), "5000 bytes, modified 2026-10-07 11:22 UTC", Nothing)
        probes.FirewallRules = Function(path) New List(Of String) From {"FastMediaSorter Media Exchange Server [in allow, enabled]"}
        probes.Ports = Function() New HostingReport.PortFacts With {.ExchangeListen = 22022, .ExchangeRangeLow = 40000, .ExchangeRangeHigh = 40100, .ShareListen = 22023}
        probes.Sockets = Function() New List(Of HostingReport.SocketFacts) From {
            New HostingReport.SocketFacts With {.Process = "fms-exchange-server", .ProcessId = 4321, .Port = 22022, .Scope = "any IPv4"},
            New HostingReport.SocketFacts With {.Process = "fms-exchange-server", .ProcessId = 4321, .Port = 22022, .Scope = "any IPv6"}}
        probes.Directories = Function() New List(Of HostingReport.DirectoryFacts) From {
            New HostingReport.DirectoryFacts With {.Label = "exchange data directory, machine store", .Exists = True, .Readable = False, .Reason = "access denied"},
            New HostingReport.DirectoryFacts With {.Label = "exchange logs directory, machine store", .Exists = True, .Readable = True, .Reason = ""}}
        probes.MailHandler = Function() False
        probes.Reachability = Function() "server running; external port checked yes; external port open yes"

        Dim text As String = HostingReport.Build(probes)
        Assert.Contains("OS: Windows Server 2025 Datacenter, version 10.0.26100, build 26100.4061, edition ServerDatacenter, Server, 64-bit", text)
        Assert.Contains("Memory and processors: 16384 MB RAM, 8 logical processors", text)
        Assert.Contains("System drive: C: 120000 MB free of 476000 MB", text)
        Assert.Contains("Session: id 2, remote desktop (RDP) session", text)
        Assert.Contains("Host mode (the Share Manager's own reading): SystemService", text)
        Assert.Contains("Service FastMediaSorterCompanionSFTP: registered; state Running; start type Automatic (delayed); account NT AUTHORITY\LocalService; last exit code 0", text)
        Assert.Contains("Service FastMediaSorterExchangeServer: registered; state Running", text)
        Assert.DoesNotContain("Neither service is registered", text)
        Assert.Contains("Ports: exchange listen 22022; exchange third-party range 40000-40100; share listen 22023", text)
        Assert.Contains("exchange server, installed: version 26.10.7.1200 (5000 bytes, modified 2026-10-07 11:22 UTC); firewall rules: FastMediaSorter Media Exchange Server [in allow, enabled]", text)
        ' A Go program carries no version resource: size and time say which build it is.
        Assert.Contains("share worker, installed: no version resource (5000 bytes, modified 2026-10-07 11:22 UTC); firewall rules:", text)
        Assert.Contains("fms-exchange-server (pid 4321): tcp/22022, any IPv4", text)
        Assert.Contains("fms-exchange-server (pid 4321): tcp/22022, any IPv6", text)
        Assert.Contains("Components installed: viewer yes; exchange server (exchange\ tree) yes", text)
        Assert.Contains("exchange data directory, machine store: present, readable by this user: no (access denied)", text)
        Assert.Contains("exchange logs directory, machine store: present, readable by this user: yes", text)
        Assert.Contains("none found - the archive is shown in its folder and its full path is printed", text)
        Assert.Contains("Share reachability: server running; external port checked yes; external port open yes", text)
    End Sub

    <Fact>
    Public Sub A_service_that_failed_names_its_exit_code_and_the_specific_one_behind_1066()
        Dim probes As HostingReport.HostingProbes = HostingReport.HostingProbes.Absent()
        probes.ServiceInfo = Function(name) New HostingReport.ServiceFacts With {
            .Name = name, .Registered = True, .State = "Stopped", .StartType = "Automatic",
            .Account = "NT AUTHORITY\LocalService", .Win32ExitCode = 1066, .ServiceSpecificExitCode = 3}
        Dim text As String = HostingReport.Build(probes)
        Assert.Contains("state Stopped; start type Automatic; account NT AUTHORITY\LocalService; last exit code 1066 (service-specific 3)", text)
    End Sub

    <Fact>
    Public Sub A_service_that_could_not_be_queried_says_so_and_is_not_called_absent()
        Dim probes As HostingReport.HostingProbes = HostingReport.HostingProbes.Absent()
        probes.ServiceInfo = Function(name) New HostingReport.ServiceFacts With {.Name = name, .QueryFailed = True, .State = "unknown"}
        Dim text As String = HostingReport.Build(probes)
        Assert.Contains("Service FastMediaSorterExchangeServer: could not be queried", text)
        Assert.DoesNotContain("Neither service is registered", text)
        Assert.DoesNotContain("not registered", text)
    End Sub

    <Fact>
    Public Sub A_probe_that_throws_costs_its_own_line_and_nothing_else()
        Dim probes As HostingReport.HostingProbes = HostingReport.HostingProbes.Absent()
        probes.Os = Function() ServerOs()
        probes.Sockets = Function() As List(Of HostingReport.SocketFacts)
                             Throw New InvalidOperationException("the socket table refused")
                         End Function
        probes.ServiceInfo = Function(name As String) As HostingReport.ServiceFacts
                                 Throw New UnauthorizedAccessException("scm")
                             End Function
        Dim text As String = HostingReport.Build(probes)
        Assert.Contains("Listening sockets of our processes: not available", text)
        Assert.Contains("Probes that failed (their lines above say so): service FastMediaSorterCompanionSFTP, service FastMediaSorterExchangeServer, sockets", text)
        Assert.Contains("OS: Windows Server 2025 Datacenter", text)
        ' The failed service probes fall back to "could not be queried", never to "absent".
        Assert.Contains("Service FastMediaSorterCompanionSFTP: could not be queried", text)
        Assert.DoesNotContain("the socket table refused", text)
    End Sub

    ' ------------------------------------------------------------ what it must never say -------

    <Fact>
    Public Sub Only_the_three_windows_service_accounts_are_named_and_a_login_never_is()
        Assert.Equal("NT AUTHORITY\LocalService", HostingReport.AccountLabel("NT AUTHORITY\LocalService"))
        Assert.Equal("LocalSystem", HostingReport.AccountLabel("localsystem"))
        Assert.Equal("NT AUTHORITY\NetworkService", HostingReport.AccountLabel("NT AUTHORITY\NetworkService"))
        Assert.Equal("a user account (name withheld)", HostingReport.AccountLabel("CONTOSO\alice"))
        Assert.Equal("a user account (name withheld)", HostingReport.AccountLabel(".\bob"))
        Assert.Equal("unknown", HostingReport.AccountLabel(""))
    End Sub

    <Fact>
    Public Sub A_service_registered_under_a_users_login_does_not_print_the_login()
        Dim probes As HostingReport.HostingProbes = HostingReport.HostingProbes.Absent()
        probes.ServiceInfo = Function(name) New HostingReport.ServiceFacts With {
            .Name = name, .Registered = True, .State = "Running", .StartType = "Manual", .Account = "CONTOSO\alice.k", .Win32ExitCode = 0}
        Dim text As String = HostingReport.Build(probes)
        Assert.DoesNotContain("alice", text)
        Assert.DoesNotContain("CONTOSO", text)
        Assert.Contains("account a user account (name withheld)", text)
    End Sub

    <Fact>
    Public Sub A_socket_is_described_by_its_scope_never_by_the_address_it_is_bound_to()
        Assert.Equal("any IPv4", HostingProbesLive.ScopeOfIPv4(0))
        Assert.Equal("loopback", HostingProbesLive.ScopeOfIPv4(&H100007F))        ' 127.0.0.1, network order read as a little-endian int
        Assert.Equal("one address", HostingProbesLive.ScopeOfIPv4(&H501A8C0))     ' 192.168.1.5
        Assert.Equal("any IPv6", HostingProbesLive.ScopeOfIPv6(New Byte(15) {}))
        Dim loop6(15) As Byte
        loop6(15) = 1
        Assert.Equal("loopback", HostingProbesLive.ScopeOfIPv6(loop6))
        Dim some(15) As Byte
        some(0) = &H20 : some(1) = &H1 : some(15) = 9
        Assert.Equal("one address", HostingProbesLive.ScopeOfIPv6(some))
        ' The port field of the kernel table is in network byte order in the low word.
        Assert.Equal(22022, HostingProbesLive.PortOfNetworkOrder(&H656))
        Assert.Equal(80, HostingProbesLive.PortOfNetworkOrder(&H5000))
        Assert.Equal(3389, HostingProbesLive.PortOfNetworkOrder(&H3D0D))
    End Sub

    <Fact>
    Public Sub Firewall_rules_are_matched_by_the_exact_program_and_reported_by_name_and_three_flags_only()
        Dim rules As New List(Of HostingProbesLive.FirewallRule) From {
            New HostingProbesLive.FirewallRule With {.Name = "FastMediaSorter Media Exchange Server", .Program = "C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe", .Description = HostingProbesLive.DescribeRuleFlags(1, 1, True)},
            New HostingProbesLive.FirewallRule With {.Name = "fms-exchange-server.exe", .Program = "c:\programdata\fastmediasorterexchange\bin\FMS-EXCHANGE-SERVER.EXE", .Description = HostingProbesLive.DescribeRuleFlags(1, 0, False)},
            New HostingProbesLive.FirewallRule With {.Name = "Somebody else's rule", .Program = "C:\Other\fms-exchange-server.exe", .Description = HostingProbesLive.DescribeRuleFlags(1, 1, True)}}
        Dim names As List(Of String) = HostingProbesLive.RuleNamesFor(rules, "C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe")
        Assert.Equal({"FastMediaSorter Media Exchange Server [in allow, enabled]", "fms-exchange-server.exe [in block, disabled]"}, names.ToArray())
        Assert.Empty(HostingProbesLive.RuleNamesFor(rules, "C:\nowhere\x.exe"))
        Assert.Empty(HostingProbesLive.RuleNamesFor(Nothing, "C:\x.exe"))
    End Sub

    ' ------------------------------------------------------------------- small pure parts -----

    <Theory>
    <InlineData("Windows 10 Pro", "19045.4170", "Windows 10 Pro")>
    <InlineData("Windows 10 Pro", "26100.4061", "Windows 11 Pro")>
    <InlineData("Windows 10 Pro", "22000", "Windows 11 Pro")>
    <InlineData("Windows Server 2025 Datacenter", "26100.4061", "Windows Server 2025 Datacenter")>
    <InlineData("", "26100", "")>
    Public Sub Windows_11_is_told_from_Windows_10_by_the_build_number(product As String, build As String, expected As String)
        Assert.Equal(expected, HostingReport.OsDisplayName(product, build))
    End Sub

    <Theory>
    <InlineData("Server", "Windows 10 Pro", True)>
    <InlineData("Server Core", "", True)>
    <InlineData("Client", "Windows Server 2019", False)>
    <InlineData("", "Windows Server 2016 Standard", True)>
    <InlineData("", "Windows 10 Pro", False)>
    <InlineData("", "", False)>
    Public Sub Server_is_told_from_client_by_the_installation_type_then_the_product_name(installationType As String, product As String, expected As Boolean)
        Assert.Equal(expected, HostingProbesLive.IsServerEdition(installationType, product))
    End Sub

    <Theory>
    <InlineData(2, False, "Automatic")>
    <InlineData(2, True, "Automatic (delayed)")>
    <InlineData(3, False, "Manual")>
    <InlineData(4, False, "Disabled")>
    <InlineData(0, False, "Boot")>
    <InlineData(1, False, "System")>
    <InlineData(9, False, "")>
    Public Sub The_services_start_value_is_a_word(start As Integer, delayed As Boolean, expected As String)
        Assert.Equal(expected, HostingProbesLive.StartTypeName(start, delayed))
    End Sub

    <Fact>
    Public Sub The_service_states_have_the_names_the_scm_documents()
        Assert.Equal({"Stopped", "Starting", "Stopping", "Running", "Continue pending", "Pause pending", "Paused", "unknown"},
                     New UInteger() {1UI, 2UI, 3UI, 4UI, 5UI, 6UI, 7UI, 0UI}.Select(Function(c) HostingProbesLive.StateName(c)).ToArray())
    End Sub

    ' ------------------------------------------------------------------- the state line -------

    <Fact>
    Public Sub The_state_line_is_one_line_with_the_facts_a_log_needs_and_no_secret()
        Dim probes As HostingReport.HostingProbes = HostingReport.HostingProbes.Absent()
        probes.Os = Function() ServerOs()
        probes.Session = Function() New HostingReport.SessionFacts With {.Known = True, .SessionId = 2, .IsRemoteDesktop = True}
        probes.HostMode = Function() "SystemService"
        probes.Components = Function() New List(Of KeyValuePair(Of String, Boolean)) From {
            New KeyValuePair(Of String, Boolean)("viewer", False), New KeyValuePair(Of String, Boolean)("Share Manager", True),
            New KeyValuePair(Of String, Boolean)("exchange server (exchange\ tree)", True)}
        probes.ServiceInfo = Function(name) If(name = HostingReport.ShareServiceName, RunningService(name),
                                               New HostingReport.ServiceFacts With {.Name = name, .Registered = False, .State = "", .StartType = "", .Account = "x\secret-login"})
        Dim line As String = HostingReport.BuildStateLine("Share Manager", "26.10.7.1200", HostingReport.Gather(probes, cheapOnly:=True))
        Assert.DoesNotContain(vbLf, line)
        Assert.DoesNotContain(vbCr, line)
        Assert.Equal("State: Share Manager 26.10.7.1200; os=Windows Server 2025 Datacenter build 26100.4061 (Server); session=2 rdp=yes; " &
                     "hostMode=SystemService; components=Share Manager,exchange server (exchange\ tree); shareService=registered(Running); exchangeService=absent", line)
        Assert.DoesNotContain("secret-login", line)
    End Sub

    <Fact>
    Public Sub The_state_line_of_a_machine_that_answers_nothing_still_reads_and_never_throws()
        Dim line As String = HostingReport.BuildStateLine("viewer", "1.0", HostingReport.Gather(HostingReport.HostingProbes.Absent(), cheapOnly:=True))
        Assert.StartsWith("State: viewer 1.0; os=unknown; session=unknown; hostMode=n/a; components=unknown", line)
        Assert.Contains("shareService=absent", line)
        Assert.StartsWith("State: ", HostingReport.BuildStateLine(Nothing, Nothing, Nothing))
    End Sub

    ''' <summary>The live probes on whatever machine runs the tests: they must answer, fast, and never throw -
    ''' a diagnostics page that dies on the first odd machine is worth nothing exactly where it is needed.</summary>
    <Fact>
    Public Sub The_live_probes_answer_on_this_machine_without_throwing_and_without_a_secret_in_the_text()
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim text As String = HostingReport.Build(HostingProbesLive.Live())
        watch.Stop()
        Assert.Contains("OS: ", text)
        Assert.Contains("Service FastMediaSorterCompanionSFTP: ", text)
        Assert.Contains("Service FastMediaSorterExchangeServer: ", text)
        Assert.Contains("Ports: exchange listen ", text)
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), "the live probes took " & watch.Elapsed.ToString())
        Dim line As String = HostingReport.BuildLiveStateLine("test", "0.0")
        Assert.StartsWith("State: test 0.0; os=", line)
        Assert.DoesNotContain(vbLf, line)
        ' Labels and numbers only: no path of the machine, least of all one under a user profile.
        Assert.DoesNotContain("\Users\", text)
    End Sub

    ' ------------------------------------------------------------------- no mail client (L-6) -----

    <Fact>
    Public Sub The_manual_route_prints_the_path_and_skips_the_mailto_when_no_handler_is_registered()
        Assert.Equal(MailSender.ManualRoute.PrintPathOnly, MailSender.ManualRouteFor(False))
        Assert.Equal(MailSender.ManualRoute.TryMailto, MailSender.ManualRouteFor(True))
    End Sub

    <Fact>
    Public Sub A_mail_handler_is_registered_by_the_users_choice_a_command_that_exists_or_a_mapi_client()
        Dim exists As Func(Of String, Boolean) = Function(p) p = "C:\Mail\client.exe"
        Dim none As String() = {}
        ' Nothing at all: a clean Windows Server.
        Assert.False(MailSender.IsMailHandlerRegistered(Nothing, Nothing, none, exists))
        Assert.False(MailSender.IsMailHandlerRegistered("", "", Nothing, exists))
        ' The user's own choice for mailto.
        Assert.True(MailSender.IsMailHandlerRegistered("Outlook.URL.mailto.15", Nothing, none, exists))
        ' An open command whose program is really there - quoted or not.
        Assert.True(MailSender.IsMailHandlerRegistered(Nothing, """C:\Mail\client.exe"" ""%1""", none, exists))
        Assert.True(MailSender.IsMailHandlerRegistered(Nothing, "C:\Mail\client.exe %1", none, exists))
        ' A registration left behind by a removed program points at nothing: not a handler.
        Assert.False(MailSender.IsMailHandlerRegistered(Nothing, """C:\Gone\wab.exe"" /mailto %1", none, exists))
        ' A MAPI client under HKLM\SOFTWARE\Clients\Mail.
        Assert.True(MailSender.IsMailHandlerRegistered(Nothing, Nothing, {"Microsoft Outlook"}, exists))
        Assert.False(MailSender.IsMailHandlerRegistered(Nothing, Nothing, {"", "  "}, exists))
    End Sub

    <Theory>
    <InlineData("""C:\Program Files\Mail\m.exe"" ""%1""", "C:\Program Files\Mail\m.exe")>
    <InlineData("C:\Mail\m.exe %1", "C:\Mail\m.exe")>
    <InlineData("C:\Mail\m.exe", "C:\Mail\m.exe")>
    <InlineData("  ""C:\a b\m.exe""  ", "C:\a b\m.exe")>
    <InlineData("", "")>
    <InlineData(Nothing, "")>
    Public Sub The_program_of_an_open_command_is_its_quoted_path_or_its_first_word(command As String, expected As String)
        Assert.Equal(expected, MailSender.ProgramOfCommand(command))
    End Sub

    <Fact>
    Public Sub The_live_registry_answer_is_a_boolean_and_never_throws()
        Dim answer As Boolean = MailSender.HasMailHandler()
        Assert.True(answer OrElse Not answer)
    End Sub

End Class
#End If
