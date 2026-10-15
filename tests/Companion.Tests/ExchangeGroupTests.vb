Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The "Media Exchange Server" settings group and its tray item (tickets 142, 143, 144): what it decides
''' (a pure model), when it exists at all, that the four section lists agree, that nothing starts or
''' elevates outside a click, that the name comes only from ProductNames, and a headless layout check.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeGroupTests
    Implements IDisposable

    Private ReadOnly _dirs As New List(Of String)()

    Public Sub New()
        ExchangeTestSupport.Reset()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ExchangeTestSupport.Reset()
        For Each d As String In _dirs
            ExchangeTestSupport.DeleteQuietly(d)
        Next
    End Sub

    Private Function Component(withExe As Boolean) As String
        Dim d As String = ExchangeTestSupport.MakeFakeComponent(withExe, withExe, False)
        _dirs.Add(d)
        Return d
    End Function

    ''' <summary>An inspect answer of the accounts model (162): <paramref name="accounts"/> accounts, a listen address, optionally a certificate.</summary>
    Private Shared Function Report(accounts As Integer, Optional listen As String = ":22022", Optional cert As Boolean = False) As ExchangeProcess.InspectReport
        Return New ExchangeProcess.InspectReport With {.Answered = True, .Accounts = accounts, .Devices = accounts * 2,
            .Listen = listen, .CertPresent = cert, .Fingerprint = If(cert, "SHA256:abc", ""), .ConfigPresent = accounts > 0}
    End Function

    ' --- the model ----------------------------------------------------------------------------------

    <Fact>
    Public Sub No_account_is_not_configured_offers_no_start_and_offers_the_wizard()
        Dim m As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(0)})
        Assert.Equal(ExchangeRunKind.NotConfigured, m.Kind)
        Assert.False(m.ShowStart)
        Assert.False(m.ShowStop)
        Assert.False(m.ShowReachabilityNote)
        Assert.Equal(0, m.Accounts)
        ' the one-person case: one button, because the manual path is a dozen steps (168 Q168-1)
        Assert.True(m.ShowWizard)
        Assert.True(m.ShowAccounts)
        Assert.True(m.ShowConfigRows)
    End Sub

    <Fact>
    Public Sub The_wizard_is_offered_only_while_nothing_exists_nothing_runs_and_no_service_owns_the_server()
        Assert.False(ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1)}).ShowWizard, "an account exists")
        Assert.False(ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(0), .ConsolePid = 7}).ShowWizard, "a server runs")
        Assert.False(ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(0), .ServiceExists = True}).ShowWizard, "a service owns the server")
        Assert.False(ExchangeGroupModel.Build(New ExchangeSnapshot()).ShowWizard, "the binary was not asked")
    End Sub

    <Fact>
    Public Sub The_configuration_rows_exist_only_when_the_binary_answered_and_the_certificate_rows_never_for_a_service()
        Dim unread As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot())
        Assert.False(unread.ShowConfigRows)
        Assert.False(unread.ShowAccounts)
        Assert.False(unread.ShowCertificateActions)
        Dim console As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1, ":22022", True)})
        Assert.True(console.ShowCertificateActions)
        Assert.False(console.ShowCertificateServiceNote)
        ' a registered service keeps its own store, which this user cannot write - no new elevation is added for it (168 rule 3)
        Dim service As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1, ":22022", True), .ServiceExists = True})
        Assert.False(service.ShowCertificateActions)
        Assert.True(service.ShowCertificateServiceNote)
        Assert.True(service.ShowConfigRows)
    End Sub

    <Fact>
    Public Sub A_configured_stopped_server_offers_start_and_the_port()
        Dim m As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1, "127.0.0.1:2222")})
        Assert.Equal(ExchangeRunKind.Stopped, m.Kind)
        Assert.True(m.ShowStart)
        Assert.False(m.ShowStop)
        Assert.Equal(2222, m.Port)
        Assert.True(m.ShowKeepRunning)
    End Sub

    <Fact>
    Public Sub A_running_console_server_offers_stop_says_reachability_is_not_verified_and_shows_the_fingerprint()
        Dim m As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1, ":22022", True), .ConsolePid = 4242})
        Assert.Equal(ExchangeRunKind.RunningConsole, m.Kind)
        Assert.True(m.ShowStop)
        Assert.False(m.ShowStart)
        Assert.True(m.ShowReachabilityNote)
        Assert.True(m.ShowFingerprint)
        Assert.Equal(4242, m.Pid)
    End Sub

    <Fact>
    Public Sub The_service_wins_the_console_start_is_hidden_and_the_service_rows_follow_its_state()
        For Each state As ServiceControl.ServiceState In {ServiceControl.ServiceState.Stopped, ServiceControl.ServiceState.Running,
                                                          ServiceControl.ServiceState.Starting, ServiceControl.ServiceState.Stopping}
            Dim m As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {
                .Report = Report(1), .ServiceExists = True, .ServiceState = state, .CanRemoveService = True})
            Assert.False(m.ShowStart, "console Start must be hidden while the service exists (" & state.ToString() & ")")
            Assert.True(m.ShowServiceWinsNote)
            Assert.False(m.ShowKeepRunning)
            Assert.True(m.ShowRemoveService)
            Assert.False(m.ShowRegisterService)
            Assert.Equal(state = ServiceControl.ServiceState.Stopped, m.ShowServiceStart)
            Assert.Equal(state = ServiceControl.ServiceState.Running OrElse state = ServiceControl.ServiceState.Starting, m.ShowServiceStop)
        Next
    End Sub

    <Fact>
    Public Sub Register_is_offered_only_when_the_snapshot_allows_it_and_no_service_exists()
        Dim without As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1)})
        Assert.False(without.ShowRegisterService)
        Assert.False(without.ShowServiceRows)
        Dim withHelper As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1), .CanRegisterService = True})
        Assert.True(withHelper.ShowRegisterService)
        Assert.True(withHelper.ShowServiceRows)
    End Sub

    <Fact>
    Public Sub The_firewall_buttons_follow_the_real_rule_state()
        Dim none As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1), .CanChangeFirewall = True, .Firewall = ExchangeFirewall.RuleState.None})
        Assert.True(none.ShowFirewallEnable)
        Assert.False(none.ShowFirewallRemove)
        Dim present As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1), .CanChangeFirewall = True, .Firewall = ExchangeFirewall.RuleState.ThisProgram})
        Assert.False(present.ShowFirewallEnable)
        Assert.True(present.ShowFirewallRemove)
        Dim noHelper As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = Report(1), .CanChangeFirewall = False, .Firewall = ExchangeFirewall.RuleState.None})
        Assert.False(noHelper.ShowFirewallEnable OrElse noHelper.ShowFirewallRemove)
    End Sub

    <Fact>
    Public Sub An_unanswered_inspect_is_unreadable_never_no_account()
        Assert.Equal(ExchangeRunKind.Unreadable, ExchangeGroupModel.Build(New ExchangeSnapshot()).Kind)
        Assert.Equal(ExchangeRunKind.Unreadable, ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = New ExchangeProcess.InspectReport()}).Kind)
    End Sub

    <Fact>
    Public Sub Every_state_has_a_line_a_summary_and_a_tray_text_in_every_language_and_the_tray_carries_the_name()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                For Each kind As ExchangeRunKind In [Enum].GetValues(GetType(ExchangeRunKind))
                    Dim m As New ExchangeGroupModel With {.Kind = kind, .Port = 2222, .Pid = 7}
                    Assert.False(String.IsNullOrWhiteSpace(ExchangeText.StateLine(m)), code & " " & kind.ToString())
                    Assert.False(String.IsNullOrWhiteSpace(m.SummaryText()), code & " " & kind.ToString())
                    Assert.Contains(ProductNames.ExchangeServer, ExchangeText.TrayItem(kind))
                Next
                Assert.Contains("2222", New ExchangeGroupModel With {.Kind = ExchangeRunKind.RunningConsole, .Port = 2222}.SummaryText())
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_tray_kind_comes_from_two_cheap_reads()
        Assert.Equal(ExchangeRunKind.RunningConsole, ExchangeText.TrayKind(True, False, ServiceControl.ServiceState.NotInstalled))
        Assert.Equal(ExchangeRunKind.Stopped, ExchangeText.TrayKind(False, False, ServiceControl.ServiceState.NotInstalled))
        Assert.Equal(ExchangeRunKind.ServiceRunning, ExchangeText.TrayKind(False, True, ServiceControl.ServiceState.Running))
        Assert.Equal(ExchangeRunKind.ServiceStopped, ExchangeText.TrayKind(True, True, ServiceControl.ServiceState.Stopped))
    End Sub

    ' --- the group exists only with the component ---------------------------------------------------

    Private Shared Function Sections(form As Share_Settings_Form) As CollapsibleSection()
        ' AllSettingsSections is the ONE list (ticket 180): AllSections() plus the client account group, which exists only while the client gate is open
        Return DirectCast(GetType(Share_Settings_Form).GetMethod("AllSettingsSections", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, Nothing), CollapsibleSection())
    End Function

    Private Shared Function SettingsSections(form As Share_Settings_Form) As CollapsibleSection()
        Return DirectCast(GetType(Share_Settings_Form).GetMethod("SettingsSections", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, Nothing), CollapsibleSection())
    End Function

    Private Shared Function SectionField(form As Share_Settings_Form, name As String) As CollapsibleSection
        Return DirectCast(GetType(Share_Settings_Form).GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(form), CollapsibleSection)
    End Function

    <Fact>
    Public Sub Without_the_component_there_is_no_group_at_all()
        ExchangeProcess.ExeDirectoryOverride = Component(withExe:=False)
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Share_Settings_Form()
                                         Assert.Null(SectionField(form, "_secExchange"))
                                         Dim keys As String() = Sections(form).Select(Function(s) s.Key).ToArray()
                                         ' the CLIENT page is there on every install since ticket 223 T-3; the administration page is not
                                         Assert.Equal({"startup", "network", "hosting", "scheduled", "exchange-account", "help"}, keys)
                                         Assert.Equal(keys, SettingsSections(form).Select(Function(s) s.Key).ToArray())
                                         Assert.DoesNotContain("exchange", keys)
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub With_the_component_the_group_sits_between_hosting_and_help_in_every_list()
        ExchangeProcess.ExeDirectoryOverride = Component(withExe:=True)
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Share_Settings_Form()
                                         Dim keys As String() = Sections(form).Select(Function(s) s.Key).ToArray()
                                         Assert.Equal({"startup", "network", "hosting", "exchange", "scheduled", "exchange-account", "help"}, keys)
                                         ' The persisted expansion / anchor list is the same list.
                                         Assert.Equal(keys, SettingsSections(form).Select(Function(s) s.Key).ToArray())
                                         Dim exchange As CollapsibleSection = SectionField(form, "_secExchange")
                                         Assert.Equal(ProductNames.ExchangeServer, exchange.Title)
                                         Assert.False(String.IsNullOrEmpty(exchange.Summary), "the folded summary is filled by the build pass")
                                         ' Nothing probed or started at construction.
                                         Assert.Equal(0, ExchangeProcess.RunningPid())
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub Every_section_field_of_the_window_is_in_the_one_list()
        ExchangeProcess.ExeDirectoryOverride = Component(withExe:=True)
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Share_Settings_Form()
                                         Dim listed As CollapsibleSection() = Sections(form)
                                         Dim fields As List(Of FieldInfo) = GetType(Share_Settings_Form).GetFields(BindingFlags.Instance Or BindingFlags.NonPublic).
                                             Where(Function(f) f.FieldType Is GetType(CollapsibleSection)).ToList()
                                         Assert.True(fields.Count >= 5)
                                         For Each f As FieldInfo In fields
                                             Dim s As CollapsibleSection = DirectCast(f.GetValue(form), CollapsibleSection)
                                             Assert.True(s Is Nothing OrElse listed.Contains(s), f.Name & " is not in AllSections()")
                                         Next
                                     End Using
                                 End Sub)
    End Sub

    ''' <summary>R4 section 1: a group added to one of four hard-coded lists and forgotten in another broke
    ''' silently. There is one list now; no place may keep a literal array of the groups again.</summary>
    <Fact>
    Public Sub No_place_keeps_a_hard_coded_list_of_the_groups()
        For Each file As String In {"Forms\Share_Settings_Form.vb", "Forms\Share_Settings_Form.UiContext.vb"}
            Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(file))
            Assert.False(Regex.IsMatch(code, "\{\s*_secStartup\s*,"), file & " lists the groups by hand")
        Next
        Dim main As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.vb"))
        ' the one list is AllSettingsSections() (ticket 180): AllSections() with the client account group inserted before Help
        Dim navigation = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.Navigation.vb"))
        Assert.Contains("For Each sec As CollapsibleSection In AllSettingsSections()", navigation)
        Assert.Equal(1, Regex.Matches(navigation, "In AllSettingsSections\(\)").Count)
        Assert.Contains("Return AllSettingsSections()", ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.UiContext.vb"))
        Assert.Contains("UpdateExchangeSummary()", main)
    End Sub

    ' --- nothing runs, elevates or starts outside a click -----------------------------------------------

    <Fact>
    Public Sub The_server_is_started_only_from_the_group_s_own_handlers()
        Dim callers As New List(Of String)()
        For Each p As String In ExchangeTestSupport.CompanionSources()
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(p))
            If Regex.IsMatch(code, "ExchangeProcess\.Start\(") Then callers.Add(Path.GetFileName(p))
        Next
        ' the group's own handlers, and the one-click wizard (ticket 168 section 3.7: a dialog the person opened and a Start button they pressed)
        Assert.Equal({"ExchangeServerWizard.vb", "Share_Settings_Form.Exchange.vb"}, callers.OrderBy(Function(c) c, StringComparer.Ordinal).ToArray())
        Dim wizardCallers As New List(Of String)()
        For Each wp As String In ExchangeTestSupport.CompanionSources()
            If Regex.IsMatch(ExchangeTestSupport.CodeOnly(File.ReadAllText(wp)), "ExchangeServerWizard\.RunAsync\(") Then wizardCallers.Add(Path.GetFileName(wp))
        Next
        Assert.Equal({"Exchange_Server_Wizard_Form.vb"}, wizardCallers.ToArray())
        Assert.Contains("ExchangeServerWizard.RunAsync(", ExchangeTestSupport.MethodBody(ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Exchange_Server_Wizard_Form.vb")), "Private Async Sub OnStartClicked("))
        Dim form As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.Exchange.vb"))
        ' Exactly two callers inside it: the Start button and the restart after a port change.
        Assert.Equal(2, Regex.Matches(form, "ExchangeProcess\.Start\(").Count)
        Assert.Contains("ExchangeProcess.Start()", ExchangeTestSupport.MethodBody(form, "Private Async Sub OnXStart()"))
        Assert.Contains("ExchangeProcess.Start()", ExchangeTestSupport.MethodBody(form, "Private Async Sub OnXApplyPort()"))
    End Sub

    <Fact>
    Public Sub The_helpers_are_launched_only_from_button_handlers()
        Dim byFile As New Dictionary(Of String, Integer)()
        For Each p As String In ExchangeTestSupport.CompanionSources()
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(p))
            Dim n As Integer = Regex.Matches(code, "ExchangeFirewall\.(Enable|Withdraw)ViaElevation\(|ExchangeService\.(Manage|StartOrStop)\(|ExchangeElevation\.RunHelper\(").Count
            If n > 0 Then byFile(Path.GetFileName(p)) = n
        Next
        ' The form calls them; the Core modules call each other (service -> elevation, firewall -> elevation).
        Assert.Contains("Share_Settings_Form.Exchange.vb", byFile.Keys)
        For Each name As String In byFile.Keys
            Assert.Contains(name, {"Share_Settings_Form.Exchange.vb", "ExchangeFirewall.vb", "ExchangeService.vb", "ExchangeFirstRunActions.vb"})
        Next
        Dim form As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.Exchange.vb"))
        Assert.Contains("EnableViaElevation", ExchangeTestSupport.MethodBody(form, "Private Async Sub OnXFirewallEnable()"))
        Assert.Contains("WithdrawViaElevation", ExchangeTestSupport.MethodBody(form, "Private Async Sub OnXFirewallRemove()"))
        ' The disruptive ones ask first, with the default on No.
        Assert.Contains("MessageBoxDefaultButton.Button2", ExchangeTestSupport.MethodBody(form, "Private Async Sub OnXFirewallRemove()"))
        Assert.Contains("MessageBoxDefaultButton.Button2", ExchangeTestSupport.MethodBody(form, "Private Async Sub OnXRegister()"))
        Assert.Contains("MessageBoxDefaultButton.Button2", ExchangeTestSupport.MethodBody(form, "Private Async Sub OnXRemoveService()"))
        ' ticket 168: every destructive or disruptive act of the administration rows asks first, naming the consequence, default No
        Dim admin As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.ExchangeAdmin.vb"))
        For Each handler As String In {"Private Async Sub OnXRemoveAccount()", "Private Async Sub OnXRevokeDevice()", "Private Async Sub OnXReplaceCertificate()", "Private Async Sub OnXImportCertificate()"}
            Assert.Contains("MessageBoxDefaultButton.Button2", ExchangeTestSupport.MethodBody(admin, handler))
        Next
        ' replacing the identity asks TWICE
        Assert.Equal(2, Regex.Matches(ExchangeTestSupport.MethodBody(admin, "Private Async Sub OnXReplaceCertificate()"), "MessageBoxDefaultButton\.Button2").Count)
    End Sub

    <Fact>
    Public Sub Nothing_outside_the_group_and_the_tray_knows_the_exchange_server()
        Dim allowed As String() = {"ExchangeProcess.vb", "ExchangeSettings.vb", "ExchangePasswordPolicy.vb", "ExchangeElevation.vb",
                                   "ExchangeFirewall.vb", "ExchangeService.vb", "ExchangeGroupModel.vb", "ExchangeText.vb",
                                   "ExchangeProducerSettings.vb",   ' ticket 148: the producer's endpoint rules read the local server's default port
                                   "ExchangeProducerText.vb",       ' ticket 148: the producer's words reuse the server group's password-floor sentences
                                   "Share_Settings_Form.ExchangeProducer.vb",   ' ticket 148: the producer rows read this PC's server (port, pin, run state)
                                   "Exchange_Password_Form.vb", "Share_Settings_Form.Exchange.vb", "Share_Settings_Form.vb",
                                   "Share_Settings_Form.UiContext.vb", "Share_Settings_Form.Navigation.vb", "Share_Settings_Form.Contract.vb", "Share_Settings_Form.Discovery.vb", "TrayContext.vb"}
        ' ticket 168: the administration files are part of the group
        allowed = allowed.Concat({"ExchangeAdmin.vb", "ExchangeAdminModel.vb", "ExchangeAdminText.vb", "ExchangeServerWizard.vb", "Exchange_Account_Admin_Form.vb",
                                  "Exchange_Server_Wizard_Form.vb", "Share_Settings_Form.ExchangeAdmin.vb", "Share_Settings_Form.ExchangeHost.vb",
                                  "Server_Welcome_Form.vb", "MainWindow.Welcome.vb", "Server_Welcome_Form.ExchangeFirstRun.vb",
                                  "ExchangeFirstRunModel.vb", "ServerWelcomeModel.vb", "ExchangeFirstRunActions.vb"}).ToArray()   ' gated onboarding reuses the existing exchange acts
        ' ticket 187 T-2: the operator's connection string is built by the server binary, shown in its own window beside the fingerprint
        allowed = allowed.Concat({"ExchangeConnectionStringText.vb", "Exchange_Connection_String_Form.vb"}).ToArray()
        ' 2026-10-12 (owner): the local join - this PC enrols itself in the server that runs on it, so it asks the binary and the service for their address and certificate
        allowed = allowed.Concat({"ExchangeLocalJoin.vb"}).ToArray()
        For Each p As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = Path.GetFileName(p)
            If name.StartsWith("Localization.", StringComparison.Ordinal) OrElse allowed.Contains(name) Then Continue For
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(p))
            Assert.False(Regex.IsMatch(code, "\bExchange(Process|Service|Firewall|Settings|Text|Elevation)\b"),
                         name & " reaches into the exchange server - Program, the main window and autostart must not (no autostart, 133 section 3)")
        Next
    End Sub

    <Fact>
    Public Sub The_exchange_server_is_never_registered_for_autostart()
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\AutostartManager.vb"))
        Assert.DoesNotContain("exchange", code, StringComparison.OrdinalIgnoreCase)
    End Sub

    ' --- plaintext, clipboard, name ---------------------------------------------------------------------

    <Fact>
    Public Sub No_log_call_in_the_exchange_sources_can_carry_output_or_a_password()
        For Each p As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = Path.GetFileName(p)
            If Not name.StartsWith("Exchange", StringComparison.Ordinal) AndAlso Not name.StartsWith("Share_Settings_Form.Exchange", StringComparison.Ordinal) Then Continue For
            Dim lines As String() = ExchangeTestSupport.CodeOnly(File.ReadAllText(p)).Split({vbLf}, StringSplitOptions.None)
            For Each line As String In lines
                If Not line.Contains("AppFileLogger.") Then Continue For
                For Each secret As String In {"StdOut", "StdErr", "Plaintext", "stdin", "EnteredPassword", "pwd"}
                    Assert.False(line.IndexOf(secret, StringComparison.Ordinal) >= 0, name & ": a log call mentions " & secret & ": " & line.Trim())
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_clipboard_is_reached_only_through_the_one_door()
        For Each file As String In {"Forms\Exchange_Password_Form.vb", "Forms\Share_Settings_Form.Exchange.vb", "Forms\Share_Settings_Form.ExchangeAdmin.vb", "Forms\Exchange_Server_Wizard_Form.vb", "Forms\Exchange_Account_Admin_Form.vb"}
            Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(file))
            Assert.DoesNotContain("Clipboard.", code.Replace("SecureClipboard.", ""))
        Next
        Assert.Contains("SecureClipboard.SetSecret(_txt.Text)", ExchangeTestSupport.ReadSource("Forms\Exchange_Password_Form.vb"))
        Assert.Contains("SecureClipboard.TrySetDataObject", ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.Exchange.vb"))
        Assert.DoesNotContain("SetSecret", ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.Exchange.vb")))
        ' the wizard's password is a secret: only through SetSecret; its fingerprint is not, and goes through the plain door
        Dim wizard As String = ExchangeTestSupport.ReadSource("Forms\Exchange_Server_Wizard_Form.vb")
        Assert.Contains("SecureClipboard.SetSecret(_txtPassword.Text)", wizard)
        Assert.Contains("SecureClipboard.TrySetDataObject", wizard)
    End Sub

    ''' <summary>The name is Latin and comes from ProductNames only: no table row, no code literal spells it
    ''' (the firewall rule's frozen anchor is the one literal, and it is a rule name, not text a person reads).</summary>
    Private Const Anchor As String = "FastMediaSorter Media Exchange Server"

    <Fact>
    Public Sub The_name_is_never_spelled_in_a_table_or_a_caption_literal()
        Dim name As String = ProductNames.ExchangeServer
        Dim offenders As New List(Of String)()
        For Each entry In Localization.All
            Dim candidates As New List(Of String)()
            candidates.Add(entry.Key)
            If entry.Value IsNot Nothing Then candidates.AddRange(entry.Value)
            For Each text As String In candidates
                If text IsNot Nothing AndAlso text.Contains(name) Then offenders.Add("table: " & text.Substring(0, Math.Min(60, text.Length)))
            Next
        Next
        For Each p As String In ExchangeTestSupport.CompanionSources()
            If Path.GetFileName(p).StartsWith("Localization.", StringComparison.Ordinal) Then Continue For
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(p)).Replace(Anchor, "")
            If code.Contains(name) Then offenders.Add(Path.GetFileName(p))
        Next
        Assert.True(offenders.Count = 0, "The name is spelled by hand: " & String.Join("; ", offenders.Take(10)))
        Assert.Equal(name, ExchangeText.Title())
        Assert.Equal("FastMediaSorter " & name, ExchangeFirewall.RuleName)
    End Sub

    <Fact>
    Public Sub The_tray_item_follows_status_is_refreshed_on_opening_and_hidden_without_the_component()
        Dim tray As String = ExchangeTestSupport.ReadSource("TrayContext.vb")
        Dim status As Integer = tray.IndexOf("menu.Items.Add(miStatus)", StringComparison.Ordinal)
        Dim exchange As Integer = tray.IndexOf("menu.Items.Add(miExchange)", StringComparison.Ordinal)
        Dim description As Integer = tray.IndexOf("menu.Items.Add(miDesc)", StringComparison.Ordinal)
        Assert.True(status > 0 AndAlso exchange > status AndAlso description > exchange, "the exchange item is not right after Status")
        Assert.Contains("AddHandler menu.Opening, Sub() RefreshExchangeTrayItem(miExchange)", tray)
        Dim refresh As String = ExchangeTestSupport.MethodBody(tray, "Private Sub RefreshExchangeTrayItem(")
        Assert.Contains("item.Visible = ExchangeProcess.IsAvailable()", refresh)
        ' The text read is off the UI thread and starts nothing.
        Dim text As String = ExchangeTestSupport.MethodBody(tray, "Private Async Function RefreshExchangeTrayTextAsync(")
        Assert.Contains("Task.Run", text)
        Assert.DoesNotContain("ExchangeProcess.Start", text)
        ' Exit stops what this session started, unless the person asked to keep it.
        Assert.Contains("ExchangeProcess.StopStartedOnExit(exSettings.KeepRunningOnExit)", tray)
    End Sub

    ' --- the password dialog, headless ----------------------------------------------------------------------

    Private Shared Function Field(Of T)(obj As Object, name As String) As T
        Return DirectCast(obj.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(obj), T)
    End Function

    ''' <summary>The Save click, invoked directly: PerformClick does nothing on a form that was never shown.</summary>
    Private Shared Sub Save(dlg As Exchange_Password_Form)
        GetType(Exchange_Password_Form).GetMethod("OnSaveClicked", BindingFlags.Instance Or BindingFlags.NonPublic).
            Invoke(dlg, New Object() {Nothing, EventArgs.Empty})
    End Sub

    <Fact>
    Public Sub The_enter_dialog_refuses_a_weak_password_with_the_reason_beside_the_field_and_accepts_a_good_one()
        ExchangeTestSupport.OnUi(Sub()
                                     Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)
                                         Dim txt As TextBox = Field(Of TextBox)(dlg, "_txt")
                                         Dim reason As Label = Field(Of Label)(dlg, "_lblReason")
                                         Dim ok As Button = Field(Of Button)(dlg, "_btnOk")
                                         Assert.True(txt.UseSystemPasswordChar, "masked by default")
                                         Assert.Equal("", reason.Text)

                                         txt.Text = "Abcdefgh1"
                                         Assert.NotEqual("", reason.Text)
                                         Assert.Equal(ExchangeText.PolicyReason(ExchangePasswordPolicy.Verdict.TooShort), reason.Text)
                                         Save(dlg)
                                         Assert.Equal(DialogResult.None, dlg.DialogResult)

                                         txt.Text = "abcdefghij1"
                                         Assert.Equal(ExchangeText.PolicyReason(ExchangePasswordPolicy.Verdict.NoUpper), reason.Text)
                                         Save(dlg)
                                         Assert.Equal(DialogResult.None, dlg.DialogResult)

                                         txt.Text = "Abcdefghi1"
                                         Assert.Equal("", reason.Text)
                                         Save(dlg)
                                         Assert.Equal(DialogResult.OK, dlg.DialogResult)
                                         Assert.Equal("Abcdefghi1", dlg.EnteredPassword)
                                     End Using
                                 End Sub)
    End Sub

    ''' <summary>The order a real caller has: OK, the window closes (the field is emptied), and only THEN does the caller read
    ''' the password - <c>ShowDialog</c> has returned by then. Reading it from the field gave the server an empty password and a
    ''' typed reset answered weak-password every time (the test host, 2026-10-12); the dialog above never closed, so it did not see it.</summary>
    <Fact>
    Public Sub A_typed_password_is_still_there_when_the_caller_reads_it_after_the_dialog_closed()
        ExchangeTestSupport.OnUi(Sub()
                                     Dim dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)
                                     Try
                                         Dim txt As TextBox = Field(Of TextBox)(dlg, "_txt")
                                         txt.Text = "Qwerty7Zxcvbn"
                                         Save(dlg)
                                         ' A modal window is hidden, not disposed, when it closes: only FormClosed is raised (Close() on a
                                         ' modeless one would dispose it, which is not the order a ShowDialog caller sees).
                                         GetType(Form).GetMethod("OnFormClosed", BindingFlags.Instance Or BindingFlags.NonPublic).
                                             Invoke(dlg, New Object() {New FormClosedEventArgs(CloseReason.None)})
                                         Assert.Equal("", txt.Text)   ' the window's own copy is gone ..
                                         Assert.Equal("Qwerty7Zxcvbn", dlg.EnteredPassword)   ' .. the one the caller takes is not
                                     Finally
                                         dlg.Dispose()
                                     End Try
                                     Assert.Equal("", dlg.EnteredPassword)   ' dropped with the dialog
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_generated_dialog_is_masked_until_revealed_per_dialog_read_only_and_wiped_on_close()
        ExchangeTestSupport.OnUi(Sub()
                                     Dim dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, "Hq7Tm2Zk9Xv4Bn8Rc3Wd")
                                     Try
                                         Dim txt As TextBox = Field(Of TextBox)(dlg, "_txt")
                                         Dim show As CheckBox = Field(Of CheckBox)(dlg, "_chkShow")
                                         Assert.True(txt.ReadOnly)
                                         Assert.True(txt.UseSystemPasswordChar)
                                         Assert.False(dlg.WasCopied)
                                         show.Checked = True
                                         Assert.False(txt.UseSystemPasswordChar)
                                         dlg.Location = New Point(-5000, -5000)
                                         dlg.StartPosition = FormStartPosition.Manual
                                         dlg.ShowInTaskbar = False
                                         dlg.Show()
                                         dlg.Close()
                                         Assert.Equal("", txt.Text)
                                     Finally
                                         dlg.Dispose()
                                     End Try
                                     ' A new dialog opens masked again - reveal is never remembered.
                                     Using again As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, "Hq7Tm2Zk9Xv4Bn8Rc3Wd")
                                         Assert.True(Field(Of TextBox)(again, "_txt").UseSystemPasswordChar)
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub Closing_a_generated_password_unseen_and_uncopied_is_asked_about_with_the_safe_default()
        Dim src As String = ExchangeTestSupport.ReadSource("Forms\Exchange_Password_Form.vb")
        Dim body As String = ExchangeTestSupport.MethodBody(src, "Protected Overrides Sub OnFormClosing(")
        Assert.Contains("Not _copied AndAlso Not _revealed", body)
        Assert.Contains("MessageBoxDefaultButton.Button2", body)
        Assert.Contains("e.Cancel = True", body)
        Assert.Contains("DpiLayout.ApplyAutoScale(Me)", src)
        Assert.Contains("DpiLayout.ClampToWorkingArea(Me)", src)
        ' ApplyAutoScale is the LAST line of the builder.
        Dim build As String = ExchangeTestSupport.MethodBody(src, "Private Sub BuildUi(")
        Assert.EndsWith("DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout", build.TrimEnd())
    End Sub

    ' --- layout, headless --------------------------------------------------------------------------------------

    ''' <summary>The group, unfolded, in English, Russian and Arabic: no row asks for more width than the
    ''' page the dialog fits (the prose wraps at the content width), and the section reports a real height.
    ''' This is the headless half of the 142 T-7 acceptance; the 175 percent display, the dark theme and the
    ''' right-to-left rendering still need a person (not done here).</summary>
    <Fact>
    Public Sub The_unfolded_group_fits_the_page_in_english_russian_and_arabic()
        ExchangeProcess.ExeDirectoryOverride = Component(withExe:=True)
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In {"en", "ru", "ar"}
                Localization.CurrentCode = code
                ExchangeTestSupport.OnUi(Sub()
                                             Using form As New Share_Settings_Form()
                                                 Dim exchange As CollapsibleSection = SectionField(form, "_secExchange")
                                                 exchange.Expanded = True
                                                 form.PerformLayout()
                                                 Assert.True(exchange.Body.PreferredSize.Height > 100, code & ": the unfolded group has no height")
                                                 Dim limit As Integer = 560 + 40
                                                 For Each c As Control In AllControls(exchange.Body)
                                                     If Not c.Visible AndAlso c.Parent IsNot Nothing AndAlso c.Parent.Visible Then Continue For
                                                     ' a multi-line text box (the statement of ticket 168) has a fixed size and scrolls; its "preferred" width is the unwrapped text
                                                     If TypeOf c Is TextBox AndAlso DirectCast(c, TextBox).Multiline Then
                                                         Assert.True(c.Width <= limit, code & ": the statement box is " & c.Width & " px wide")
                                                         Continue For
                                                     End If
                                                     Assert.True(c.PreferredSize.Width <= limit, code & ": " & c.GetType().Name & " '" & c.Text & "' wants " & c.PreferredSize.Width & " px")
                                                 Next
                                             End Using
                                         End Sub)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Function AllControls(root As Control) As List(Of Control)
        Dim list As New List(Of Control)()
        For Each c As Control In root.Controls
            list.Add(c)
            list.AddRange(AllControls(c))
        Next
        Return list
    End Function

End Class
