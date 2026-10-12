Option Strict On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The two elevated halves of the exchange group - the firewall rule (ticket 143) and the service (ticket
''' 144) - proven WITHOUT elevating: the launcher is a fake that records what would have run, the SCM and
''' netsh reads are fakes, and the helper scripts are only read as text. What is NOT executed here: a real
''' UAC prompt, a real firewall rule, a real service. Those are the hand-run scenarios of 143 section 3 and
''' 144 section 4.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeElevationTests
    Implements IDisposable

    Private ReadOnly _dir As String
    Private ReadOnly _calls As New List(Of Tuple(Of String, String))()

    Public Sub New()
        ExchangeTestSupport.Reset()
        _dir = ExchangeTestSupport.MakeFakeComponent(True, True, True)
        ExchangeProcess.ExeDirectoryOverride = _dir
        ' No service registered unless a test says so: the developer's registry is never read.
        ExchangeService.ImagePathOverride = Function() ""
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ExchangeTestSupport.Reset()
        ExchangeTestSupport.DeleteQuietly(_dir)
    End Sub

    Private Sub LaunchReturns(code As Integer)
        ExchangeElevation.Launcher = Function(file, args)
                                         _calls.Add(Tuple.Create(file, args))
                                         Return code
                                     End Function
    End Sub

    Private Sub LaunchThrows(nativeCode As Integer)
        ExchangeElevation.Launcher = Function(file, args)
                                         _calls.Add(Tuple.Create(file, args))
                                         Throw New Win32Exception(nativeCode)
                                     End Function
    End Sub

    ' --- firewall ----------------------------------------------------------------------------

    <Fact>
    Public Sub Enabling_runs_the_helper_with_the_exe_and_no_disable_switch_in_one_prompt()
        LaunchReturns(0)
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Succeeded, ExchangeFirewall.EnableViaElevation())
        Assert.Single(_calls)
        Assert.Equal("powershell.exe", _calls(0).Item1)
        Dim helper As String = Path.Combine(_dir, "enable-exchange-server.ps1")
        Assert.Contains("-NoProfile -ExecutionPolicy Bypass -File """ & helper & """", _calls(0).Item2)
        Assert.Contains("-ExePath """ & Path.Combine(_dir, "fms-exchange-server.exe") & """", _calls(0).Item2)
        Assert.DoesNotContain("-Disable", _calls(0).Item2)
    End Sub

    <Fact>
    Public Sub Withdrawing_passes_the_disable_switch()
        LaunchReturns(0)
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Succeeded, ExchangeFirewall.WithdrawViaElevation())
        Assert.EndsWith(" -Disable", _calls(0).Item2)
    End Sub

    <Theory>
    <InlineData(1)>
    <InlineData(2)>
    <InlineData(5)>
    <InlineData(8)>
    Public Sub Only_exit_zero_is_success(code As Integer)
        LaunchReturns(code)
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Failed, ExchangeFirewall.EnableViaElevation())
    End Sub

    <Fact>
    Public Sub A_declined_prompt_is_declined_and_other_launch_errors_are_failures()
        LaunchThrows(ExchangeElevation.ErrorCancelled)
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Declined, ExchangeFirewall.EnableViaElevation())
        LaunchThrows(2)
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Failed, ExchangeFirewall.WithdrawViaElevation())
    End Sub

    <Fact>
    Public Sub Without_the_helper_or_the_exe_nothing_is_launched()
        LaunchReturns(0)
        File.Delete(Path.Combine(_dir, ExchangeFirewall.HelperScriptName))
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Unavailable, ExchangeFirewall.EnableViaElevation())
        File.WriteAllText(Path.Combine(_dir, ExchangeFirewall.HelperScriptName), "# stand-in")
        File.Delete(Path.Combine(_dir, ExchangeProcess.ExeName))
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Unavailable, ExchangeFirewall.WithdrawViaElevation())
        Assert.Empty(_calls)
    End Sub

    <Fact>
    Public Sub A_path_that_could_end_the_quoted_argument_is_refused_before_launch()
        LaunchReturns(0)
        Assert.False(ExchangeElevation.IsSafeValue("C:\a""b"))
        Assert.False(ExchangeElevation.IsSafeValue("C:\a" & vbLf & "b"))
        Assert.False(ExchangeElevation.IsSafeValue(""))
        Assert.True(ExchangeElevation.IsSafeValue("C:\Program Files\FastMediaSorter\exchange\fms-exchange-server.exe"))
        Dim code As Integer
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Failed,
                     ExchangeElevation.RunHelper("t", Path.Combine(_dir, ExchangeFirewall.HelperScriptName), code, "-ExePath", "C:\x""y"))
        Assert.Empty(_calls)
    End Sub

    <Fact>
    Public Sub The_rule_read_finds_the_program_path_whatever_language_netsh_answers_in()
        Dim exe As String = "C:\Program Files\FMS\exchange\fms-exchange-server.exe"
        Dim english As String = "Rule Name: FastMediaSorter Media Exchange Server" & vbCrLf & "Program: " & exe & vbCrLf & "Action: Allow"
        Dim russian As String = "Имя правила: FastMediaSorter Media Exchange Server" & vbCrLf & "Программа: " & exe.ToLowerInvariant() & vbCrLf & "Действие: Разрешить"
        Assert.Equal(ExchangeFirewall.RuleState.ThisProgram, ExchangeFirewall.ParseRule(0, english, exe))
        Assert.Equal(ExchangeFirewall.RuleState.ThisProgram, ExchangeFirewall.ParseRule(0, russian, exe))
        ' The same name for another copy (the service's staged exe) is not THIS program's rule.
        Assert.Equal(ExchangeFirewall.RuleState.OtherProgram,
                     ExchangeFirewall.ParseRule(0, english.Replace(exe, "C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe"), exe))
        Assert.Equal(ExchangeFirewall.RuleState.None, ExchangeFirewall.ParseRule(1, "No rules match the specified criteria.", exe))
        Assert.Equal(ExchangeFirewall.RuleState.None, ExchangeFirewall.ParseRule(0, "   ", exe))
    End Sub

    <Fact>
    Public Sub The_state_shown_is_the_real_read_and_a_failed_read_is_unknown_not_none()
        ExchangeFirewall.ShowRuleOverride = Function() Tuple.Create(0, "Program: " & ExchangeProcess.ExePath())
        Assert.Equal(ExchangeFirewall.RuleState.ThisProgram, ExchangeFirewall.QueryRule())
        ExchangeFirewall.ShowRuleOverride = Function() Tuple.Create(1, "")
        Assert.Equal(ExchangeFirewall.RuleState.None, ExchangeFirewall.QueryRule())
        ExchangeFirewall.ShowRuleOverride = Function() CType(Nothing, Tuple(Of Integer, String))
        Assert.Equal(ExchangeFirewall.RuleState.Unknown, ExchangeFirewall.QueryRule())
        ExchangeFirewall.ShowRuleOverride = Function() As Tuple(Of Integer, String)
                                                Throw New InvalidOperationException("netsh missing")
                                            End Function
        Assert.Equal(ExchangeFirewall.RuleState.Unknown, ExchangeFirewall.QueryRule())
    End Sub

    <Fact>
    Public Sub The_service_command_line_names_its_program_quoted_or_not()
        Dim staged As String = "C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe"
        Assert.Equal(staged, ExchangeFirewall.ServiceExeOf("""" & staged & """ serve --service --config ""C:\x\exchange.json"""))
        Assert.Equal("C:\bin\fms-exchange-server.exe", ExchangeFirewall.ServiceExeOf("C:\bin\fms-exchange-server.exe serve --service"))
        Assert.Equal("", ExchangeFirewall.ServiceExeOf("""C:\other\tool.exe"" serve --service"))
        Assert.Equal("", ExchangeFirewall.ServiceExeOf("""C:\unclosed\fms-exchange-server.exe serve"))
        Assert.Equal("", ExchangeFirewall.ServiceExeOf(""))
    End Sub

    <Fact>
    Public Sub While_the_service_is_registered_the_rule_is_read_and_set_for_the_copy_it_starts()
        ' The test host, 2026-10-12: the rule named the application folder's copy, the service ran the
        ' staged one, the screen said "a rule exists" and nothing reached the port.
        Dim staged As String = "C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe"
        ExchangeService.ImagePathOverride = Function() """" & staged & """ serve --service --config ""C:\ProgramData\FastMediaSorterExchange\exchange.json"""
        Assert.Equal(staged, ExchangeFirewall.TargetExePath())
        ExchangeFirewall.ShowRuleOverride = Function() Tuple.Create(0, "Program: " & ExchangeProcess.ExePath())
        Assert.Equal(ExchangeFirewall.RuleState.OtherProgram, ExchangeFirewall.QueryRule())
        ExchangeFirewall.ShowRuleOverride = Function() Tuple.Create(0, "Program: " & staged)
        Assert.Equal(ExchangeFirewall.RuleState.ThisProgram, ExchangeFirewall.QueryRule())
        LaunchReturns(0)
        Assert.Equal(ExchangeElevation.ElevatedOutcome.Succeeded, ExchangeFirewall.EnableViaElevation())
        Assert.Contains("-ExePath """ & staged & """", _calls(0).Item2)
        ' A registration that is not ours (no "serve --service") leaves the console copy as the target.
        ExchangeService.ImagePathOverride = Function() """" & staged & """"
        Assert.Equal(ExchangeProcess.ExePath(), ExchangeFirewall.TargetExePath())
    End Sub

    ' --- the helper scripts, read as text (143 T-6) ----------------------------------------------

    Private Shared Function Helper(name As String) As String
        Dim full As String = Path.Combine(ExchangeTestSupport.RepoRoot(), "publishing", "installer", name)
        Assert.True(File.Exists(full), name & " is missing from publishing\installer")
        Return File.ReadAllText(full)
    End Function

    <Fact>
    Public Sub The_firewall_helper_has_exactly_the_agreed_parameters_scopes_by_program_and_opens_no_port()
        Dim script As String = Helper("enable-exchange-server.ps1")
        Dim param As Match = Regex.Match(script, "param\((?<body>.*?)\)\s*\r?\n", RegexOptions.Singleline)
        Assert.True(param.Success)
        Dim names As List(Of String) = Regex.Matches(param.Groups("body").Value, "\[(?:string|switch)\]\s*\$(?<n>[A-Za-z]+)").Cast(Of Match)().Select(Function(m) m.Groups("n").Value).ToList()
        Assert.Equal({"ExePath", "Disable"}, names.ToArray())
        Assert.Contains("program=", script)
        Assert.DoesNotContain("localport", script, StringComparison.OrdinalIgnoreCase)
        Assert.Contains("FastMediaSorter Media Exchange Server", script)
        Assert.Equal(ExchangeFirewall.RuleName, "FastMediaSorter Media Exchange Server")
    End Sub

    <Fact>
    Public Sub The_service_helper_takes_the_action_parameter_and_every_verb_this_program_sends()
        Dim script As String = Helper("install-exchange-service.ps1")
        Assert.Contains("[string]$Action", script)
        Dim validate As Match = Regex.Match(script, "ValidateSet\((?<set>[^)]*)\)")
        Assert.True(validate.Success)
        Dim verbs As List(Of String) = Regex.Matches(validate.Groups("set").Value, "'(?<v>[^']+)'").Cast(Of Match)().Select(Function(m) m.Groups("v").Value).ToList()
        For Each v As ExchangeService.Verb In [Enum].GetValues(GetType(ExchangeService.Verb))
            Assert.Contains(ExchangeService.VerbName(v), verbs)
        Next
        For Each parameter As String In {"[string]$ExePath", "[string]$DataDir", "[string]$UserDataDir", "[string]$ManageSid"}
            Assert.Contains(parameter, script)
        Next
        Assert.DoesNotContain("-Verb", ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ExchangeService.vb")))
    End Sub

    ' --- service ------------------------------------------------------------------------------------

    <Theory>
    <InlineData("""C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe"" serve --service --config ""C:\ProgramData\FastMediaSorterExchange\exchange.json""", True)>
    <InlineData("C:\x\fms-exchange-server.exe serve --service", True)>
    <InlineData("C:\x\fms-exchange-server.exe serve", False)>
    <InlineData("C:\x\other.exe serve --service", False)>
    <InlineData("", False)>
    Public Sub A_registration_is_ours_only_with_the_exe_name_and_serve_service(image As String, expected As Boolean)
        Assert.Equal(expected, ExchangeService.IsOurImagePath(image))
    End Sub

    <Fact>
    Public Sub The_service_exists_only_when_the_scm_has_it_and_the_registration_is_ours()
        ExchangeService.QueryStateOverride = Function() ServiceControl.ServiceState.Stopped
        ExchangeService.ImagePathOverride = Function() "x\fms-exchange-server.exe serve --service"
        Assert.True(ExchangeService.Exists())
        ExchangeService.ImagePathOverride = Function() "x\something-else.exe"
        Assert.False(ExchangeService.Exists())
        ExchangeService.ImagePathOverride = Function() "x\fms-exchange-server.exe serve --service"
        ExchangeService.QueryStateOverride = Function() ServiceControl.ServiceState.NotInstalled
        Assert.False(ExchangeService.Exists())
    End Sub

    <Fact>
    Public Sub Scm_state_codes()
        Assert.Equal(ServiceControl.ServiceState.Stopped, ExchangeService.StateOfCode(1UI))
        Assert.Equal(ServiceControl.ServiceState.Starting, ExchangeService.StateOfCode(2UI))
        Assert.Equal(ServiceControl.ServiceState.Stopping, ExchangeService.StateOfCode(3UI))
        Assert.Equal(ServiceControl.ServiceState.Running, ExchangeService.StateOfCode(4UI))
        Assert.Equal(ServiceControl.ServiceState.Unknown, ExchangeService.StateOfCode(7UI))
    End Sub

    <Fact>
    Public Sub Registering_is_offered_only_where_the_helper_shipped_and_no_service_exists()
        Assert.True(ExchangeService.CanRegister(serviceExists:=False))
        Assert.False(ExchangeService.CanRegister(serviceExists:=True))
        Assert.True(ExchangeService.CanRemove(serviceExists:=True))
        Assert.False(ExchangeService.CanRemove(serviceExists:=False))
        ' The viewer edition: the registration helper is not in the folder.
        File.Delete(Path.Combine(_dir, ExchangeService.HelperScriptName))
        Assert.False(ExchangeService.CanRegister(serviceExists:=False))
        Assert.False(ExchangeService.CanRemove(serviceExists:=True))
    End Sub

    <Fact>
    Public Sub Registering_with_a_console_store_to_carry_over_is_a_migration_else_a_plain_install()
        Dim user As String = Path.Combine(_dir, "user")
        Directory.CreateDirectory(user)
        ExchangeService.UserDataDirOverride = user
        Assert.Equal(ExchangeService.Verb.Install, ExchangeService.RegisterVerb())
        File.WriteAllText(Path.Combine(user, "exchange.json"), "{}")
        Assert.Equal(ExchangeService.Verb.MigrateToService, ExchangeService.RegisterVerb())
    End Sub

    <Fact>
    Public Sub The_helper_call_carries_action_exe_both_data_directories_and_the_enrolled_sid()
        LaunchReturns(0)
        ExchangeService.MachineDataDirOverride = "C:\ProgramData\FastMediaSorterExchange"
        ExchangeService.UserDataDirOverride = "C:\Users\a\AppData\Local\FastMediaSorterExchange"
        Assert.Equal(ServiceControl.ManageResult.Succeeded, ExchangeService.Manage(ExchangeService.Verb.MigrateToService))
        Dim args As String = _calls.Single().Item2
        Assert.Contains("-Action ""migrate-to-service""", args)
        Assert.Contains("-ExePath """ & Path.Combine(_dir, "fms-exchange-server.exe") & """", args)
        Assert.Contains("-DataDir ""C:\ProgramData\FastMediaSorterExchange""", args)
        Assert.Contains("-UserDataDir ""C:\Users\a\AppData\Local\FastMediaSorterExchange""", args)
        Dim sid As String = ServiceControl.CurrentUserSid()
        If sid.Length > 0 Then Assert.Contains("-ManageSid """ & sid & """", args)
        ' Only a validated SID shape can come from the process token, and the helper validates again.
        If sid.Length > 0 Then Assert.Matches("^S-1-\d+(-\d+)+$", sid)
    End Sub

    <Fact>
    Public Sub Without_the_registration_helper_the_service_actions_are_unavailable()
        LaunchReturns(0)
        File.Delete(Path.Combine(_dir, ExchangeService.HelperScriptName))
        Assert.Equal(ServiceControl.ManageResult.Unavailable, ExchangeService.Manage(ExchangeService.Verb.Install))
        Assert.Empty(_calls)
    End Sub

    <Fact>
    Public Sub Start_and_stop_need_no_prompt_when_the_scm_allows_them()
        LaunchReturns(0)
        ExchangeService.StateWaitMs = 600
        Dim asked As New List(Of Boolean)()
        ExchangeService.ControlOverride = Function(start)
                                              asked.Add(start)
                                              Return ExchangeService.ControlOutcome.Done
                                          End Function
        ExchangeService.QueryStateOverride = Function() ServiceControl.ServiceState.Running
        Assert.Equal(ServiceControl.ManageResult.Succeeded, ExchangeService.StartOrStop(start:=True))
        ExchangeService.QueryStateOverride = Function() ServiceControl.ServiceState.Stopped
        Assert.Equal(ServiceControl.ManageResult.Succeeded, ExchangeService.StartOrStop(start:=False))
        Assert.Equal({True, False}, asked.ToArray())
        Assert.Empty(_calls)   ' no helper, no UAC
    End Sub

    <Fact>
    Public Sub Access_denied_falls_back_to_the_helper_verb_and_a_service_that_never_arrives_is_a_failure()
        ExchangeService.StateWaitMs = 400
        LaunchReturns(0)
        ExchangeService.ControlOverride = Function(start) ExchangeService.ControlOutcome.AccessDenied
        Assert.Equal(ServiceControl.ManageResult.Succeeded, ExchangeService.StartOrStop(start:=True))
        Assert.Contains("-Action ""start""", _calls.Single().Item2)
        ExchangeService.StartOrStop(start:=False)
        Assert.Contains("-Action ""stop""", _calls(1).Item2)

        ExchangeService.ControlOverride = Function(start) ExchangeService.ControlOutcome.Done
        ExchangeService.QueryStateOverride = Function() ServiceControl.ServiceState.Starting
        Assert.Equal(ServiceControl.ManageResult.Failed, ExchangeService.StartOrStop(start:=True))
        ExchangeService.ControlOverride = Function(start) ExchangeService.ControlOutcome.Failed
        Assert.Equal(ServiceControl.ManageResult.Failed, ExchangeService.StartOrStop(start:=True))
    End Sub

End Class
