Option Strict On
Imports System.Collections.Generic
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection("manager-welcome")>
Public Class ExchangeFirstRunUiTests
    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
    Private Shared Function Field(Of T)(target As Object, name As String) As T
        Return DirectCast(target.GetType().GetField(name, Flags).GetValue(target), T)
    End Function
    Private Shared Function Invoke(target As Object, name As String, ParamArray args As Object()) As Object
        Return target.GetType().GetMethod(name, Flags).Invoke(target, args)
    End Function
    Private Shared Sub NextStep(form As Server_Welcome_Form)
        DirectCast(Invoke(form, "AdvanceExchangeAsync"), Task).GetAwaiter().GetResult()
    End Sub
    Private Shared Sub Isolated(action As Action)
        ExchangeTestSupport.OnUi(Sub()
            Dim app = CompanionGlobals.SettingsAppNameOverride
            Dim code = Localization.CurrentCode
            Dim gate = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName)
            Dim scratch = "FmsExchangeWelcomeTest_" & Guid.NewGuid().ToString("N")
            CompanionGlobals.SettingsAppNameOverride = scratch
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1")
            Try
                Localization.CurrentCode = "en"
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                action()
            Finally
                CompanionGlobals.SettingsAppNameOverride = app
                Localization.CurrentCode = code
                Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, gate)
                Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
            End Try
        End Sub)
    End Sub

    Friend Class Actions
        Implements IExchangeFirstRunActions
        Public ReadOnly Calls As New List(Of String)
        Public Snapshot As New ExchangeSnapshot With {.CanRegisterService = True, .CanChangeFirewall = True,
            .Report = New ExchangeProcess.InspectReport With {.Answered = True, .Listen = ":22022", .PortRange = "40000-40100"}}
        Public Result As New ExchangeWizardResult With {.Outcome = ExchangeWizardOutcome.Done, .AccountCreated = True,
            .Login = "operator", .Password = "TransientPassword9"}
        Public RegisterResult As ServiceControl.ManageResult = ServiceControl.ManageResult.Succeeded
        Public Function ReadAsync() As Task(Of ExchangeSnapshot) Implements IExchangeFirstRunActions.ReadAsync
            Calls.Add("read") : Return Task.FromResult(Snapshot)
        End Function
        Public Function SetListenAsync(value As String) As Task(Of ExchangeProcess.ListenOutcome) Implements IExchangeFirstRunActions.SetListenAsync
            Calls.Add("listen") : Snapshot.Report.Listen = value : Return Task.FromResult(ExchangeProcess.ListenOutcome.Saved)
        End Function
        Public Function SetRangeAsync(value As String) As Task(Of ExchangeProcess.ConfigOutcome) Implements IExchangeFirstRunActions.SetRangeAsync
            Calls.Add("range") : Snapshot.Report.PortRange = value : Return Task.FromResult(ExchangeProcess.ConfigOutcome.Saved)
        End Function
        Public Function SetEndpointAsync(value As String) As Task(Of ExchangeProcess.ConfigOutcome) Implements IExchangeFirstRunActions.SetEndpointAsync
            Calls.Add("endpoint") : Snapshot.Report.PublicEndpoint = value : Return Task.FromResult(ExchangeProcess.ConfigOutcome.Saved)
        End Function
        Public Function FirewallAsync() As Task(Of ExchangeElevation.ElevatedOutcome) Implements IExchangeFirstRunActions.FirewallAsync
            Calls.Add("firewall") : Return Task.FromResult(ExchangeElevation.ElevatedOutcome.Declined)
        End Function
        Public Function ServiceAsync() As Task(Of ServiceControl.ManageResult) Implements IExchangeFirstRunActions.ServiceAsync
            Calls.Add("service")
            Return Task.FromResult(RegisterResult)
        End Function
        Public Function Account(owner As Form, login As String, typed As Boolean, enroll As Boolean) As ExchangeWizardResult Implements IExchangeFirstRunActions.Account
            Calls.Add("account")
            If ExchangeFirstRunModel.Completed(Result.Outcome) Then
                Snapshot.ConsolePid = 123
                Snapshot.Report.Accounts = 1
                Snapshot.Report.Fingerprint = "SHA256:test"
            End If
            Return Result
        End Function
        Public Function Suggest(port As Integer) As String Implements IExchangeFirstRunActions.Suggest
            Calls.Add("suggest") : Return "192.168.1.10:" & port.ToString()
        End Function
        Public Sub ConnectionString(owner As Form) Implements IExchangeFirstRunActions.ConnectionString
            Calls.Add("connection")
        End Sub
    End Class

    <Fact>
    Public Sub First_step_Later_writes_only_dismissal_and_stale_settings_cannot_rearm_it()
        Isolated(Sub()
            Dim stale As New ShareSettings()
            stale.Load()
            Dim ports As New Actions()
            Using form As New Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot)
                Assert.Empty(ports.Calls)
                Assert.False(Field(Of CheckBox)(form, "_firewall").Checked)
                Assert.False(Field(Of CheckBox)(form, "_service").Checked)
                Invoke(form, "OnLater", Nothing, EventArgs.Empty)
                Assert.Empty(ports.Calls)
                Dim settings = Microsoft.VisualBasic.Interaction.GetAllSettings(CompanionGlobals.App_name, CompanionGlobals.Second_App_Name)
                Assert.Equal(1, settings.GetLength(0))
                Assert.Equal(ServerWelcomeModel.DismissExchangeValue, settings(0, 0))
                stale.Save()
                stale.Load()
                Assert.True(stale.WelcomeExchangeDismissed)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Painting_and_unchanged_Next_never_write_defaults_and_suggestion_requires_a_click()
        Isolated(Sub()
            Dim ports As New Actions()
            Using form As New Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot)
                Invoke(form, "ShowStep", 1)
                NextStep(form)
                Assert.Equal(New String() {"read"}, ports.Calls)
                Invoke(form, "ShowStep", 3)
                Assert.Equal("", Field(Of TextBox)(form, "_endpoint").Text)
                Invoke(Field(Of Button)(form, "_endpointSuggestion"), "OnClick", EventArgs.Empty)
                Assert.Equal("192.168.1.10:22022", Field(Of TextBox)(form, "_endpoint").Text)
                Assert.DoesNotContain("endpoint", ports.Calls)
                NextStep(form)
                Assert.Contains("endpoint", ports.Calls)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Explicit_port_and_range_changes_write_on_Next_and_declined_firewall_is_not_retried()
        Isolated(Sub()
            Dim ports As New Actions()
            Using form As New Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot)
                Invoke(form, "ShowStep", 1)
                Field(Of NumericUpDown)(form, "_listen").Value = 23000
                Field(Of NumericUpDown)(form, "_rangeLow").Value = 41000
                Field(Of NumericUpDown)(form, "_rangeHigh").Value = 41100
                Field(Of CheckBox)(form, "_firewall").Checked = True
                Assert.Empty(ports.Calls)
                NextStep(form)
                Assert.Equal(New String() {"listen", "range", "firewall", "read"}, ports.Calls)
                Assert.False(Field(Of CheckBox)(form, "_firewall").Checked)
                Invoke(form, "ShowStep", 1)
                ports.Calls.Clear()
                NextStep(form)
                Assert.Equal(New String() {"read"}, ports.Calls)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Service_consent_waits_for_the_account_and_a_decline_does_not_block_a_running_server()
        Isolated(Sub()
            Dim ports As New Actions With {.RegisterResult = ServiceControl.ManageResult.Declined}
            Using form As New Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot)
                Invoke(form, "ShowStep", 2)
                Field(Of CheckBox)(form, "_service").Checked = True
                NextStep(form)
                Assert.Equal(New String() {"read"}, ports.Calls)
                Invoke(form, "ShowStep", 4)
                Field(Of TextBox)(form, "_login").Text = "operator"
                ports.Calls.Clear()
                NextStep(form)
                Assert.Equal(New String() {"account", "service", "read"}, ports.Calls)
                Assert.Equal(5, Field(Of Integer)(form, "_position"))
                Assert.Equal("", ports.Result.Password)
                Invoke(form, "ShowStep", 6)
                Assert.Contains(ExchangeText.ServiceResult(ServiceControl.ManageResult.Declined), Field(Of Label)(form, "_exchangeProblem").Text)
                Assert.DoesNotContain("TransientPassword9", Field(Of Label)(form, "_facts").Text)
                Assert.False(Field(Of Button)(form, "_connectionString").Enabled)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub A_created_account_with_failed_start_stays_on_the_step_and_offers_group_repair_without_creating_again()
        Isolated(Sub()
            Dim ports As New Actions()
            ports.Result.Outcome = ExchangeWizardOutcome.StartFailed
            Using form As New Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot)
                Invoke(form, "ShowStep", 4)
                Field(Of TextBox)(form, "_login").Text = "operator"
                NextStep(form)
                Assert.Equal(4, Field(Of Integer)(form, "_position"))
                Assert.False(Field(Of Button)(form, "_next").Enabled)
                Assert.Equal("", ports.Result.Password)
                NextStep(form)
                Assert.Equal(New String() {"account"}, ports.Calls)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Shut_gate_rejects_the_exchange_frame_and_replay_opens_done_without_writes()
        Isolated(Sub()
            Using ExchangeFeatures.ForFlow(offered:=False, switchOn:=False)
                Assert.Throws(Of InvalidOperationException)(Function() New Server_Welcome_Form(WelcomeHalf.Exchange))
            End Using
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1")
            Dim ports As New Actions()
            Using form As New Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot, replay:=True)
                Assert.Equal(6, Field(Of Integer)(form, "_position"))
                Assert.Empty(ports.Calls)
            End Using
            For Each row In ExchangeFirstRunModel.HandoverRows()
                Assert.NotNull(GetType(Share_Settings_Form).GetField(row, Flags))
            Next
        End Sub)
    End Sub

    <Fact>
    Public Sub First_run_account_dialog_never_keeps_password_even_when_the_saved_keep_option_is_on()
        Isolated(Sub()
            Dim settings As New ExchangeSettings With {.KeepAccountPasswords = True}
            settings.Save()
            Using form As New Exchange_Server_Wizard_Form(New ExchangeServerWizardTests.StubPorts(), "operator", firstRun:=True)
                Dim result As New ExchangeWizardResult With {.Outcome = ExchangeWizardOutcome.Done, .AccountCreated = True,
                    .Login = "operator", .Password = "TransientPassword9"}
                Invoke(form, "ShowResult", result)
                Assert.Equal("", result.Password)
                Assert.False(Field(Of Boolean)(form, "_kept"))
                Assert.True(Field(Of TextBox)(form, "_txtPassword").UseSystemPasswordChar)
                Assert.Equal("TransientPassword9", Field(Of TextBox)(form, "_txtPassword").Text)
                Invoke(form, "OnFormClosed", New FormClosedEventArgs(CloseReason.UserClosing))
                Assert.Equal("", Field(Of TextBox)(form, "_txtPassword").Text)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Welcome_actions_use_the_existing_engine_and_clipboard_and_never_log_values()
        For Each source In {"Forms\Server_Welcome_Form.vb", "Forms\Server_Welcome_Form.ExchangeFirstRun.vb", "Core\ExchangeFirstRunActions.vb"}
            Dim code = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(source))
            Assert.DoesNotContain("AppFileLogger", code)
            Assert.DoesNotContain("Clipboard.Set", code)
            Assert.DoesNotMatch("\bProcess\.Start\(", code)
            Assert.DoesNotContain("ExchangeAccountPasswordStore", code)
        Next
        Dim actions = ExchangeTestSupport.ReadSource("Core\ExchangeFirstRunActions.vb")
        Assert.Contains("New Exchange_Server_Wizard_Form", actions)
        Assert.Contains("New Exchange_Password_Form", actions)
        Assert.Contains("New Exchange_Connection_String_Form", actions)
    End Sub

    <Fact>
    Public Sub Unchecked_enrollment_still_checks_certificate_and_never_sends_the_password()
        Dim ports As New ExchangeServerWizardTests.StubPorts()
        Dim result = ExchangeServerWizard.RunAsync("operator", ports, Nothing, CancellationToken.None,
            enrollThisPc:=False).GetAwaiter().GetResult()
        Assert.Equal(ExchangeWizardOutcome.DoneNoAgent, result.Outcome)
        Assert.Equal(Localization.T("Сервер работает, учётная запись создана. Подключение этого компьютера пропущено по вашему выбору."), ExchangeAdminText.WizardOutcome(result, False))
        Assert.True(ports.Probes > 0)
        Assert.DoesNotContain("enroll", ports.Calls)
        result.Wipe()
        ports = New ExchangeServerWizardTests.StubPorts With {.ProbeFingerprint = "SHA256:other"}
        result = ExchangeServerWizard.RunAsync("operator", ports, Nothing, CancellationToken.None,
            enrollThisPc:=False).GetAwaiter().GetResult()
        Assert.Equal(ExchangeWizardOutcome.CertificateMismatch, result.Outcome)
        Assert.DoesNotContain("enroll", ports.Calls)
        result.Wipe()
    End Sub

    <Fact>
    Public Sub First_run_cancellation_after_account_creation_preserves_the_one_time_password_and_partial_result()
        Using cts As New CancellationTokenSource()
            Dim ports As New ExchangeServerWizardTests.StubPorts With {.Cts = cts, .CancelOn = "add"}
            Dim result = ExchangeServerWizard.RunAsync("operator", ports, Nothing, cts.Token,
                allowManagedService:=True).GetAwaiter().GetResult()
            Assert.Equal(ExchangeWizardOutcome.Cancelled, result.Outcome)
            Assert.True(result.AccountCreated)
            Assert.Equal(ports.Password, result.Password)
            Assert.DoesNotContain("start", ports.Calls)
            Assert.DoesNotContain("enroll", ports.Calls)
            result.Wipe()
            Assert.Equal("", result.Password)
        End Using
    End Sub

    <Fact>
    Public Sub First_run_start_IO_failure_retains_the_created_account_without_changing_the_ordinary_engine()
        Dim ports As New ExchangeServerWizardTests.StubPorts With {.StartException = New IO.IOException("scripted failure")}
        Dim result = ExchangeServerWizard.RunAsync("operator", ports, Nothing, CancellationToken.None,
            allowManagedService:=True).GetAwaiter().GetResult()
        Assert.Equal(ExchangeWizardOutcome.StartFailed, result.Outcome)
        Assert.True(result.AccountCreated)
        Assert.Equal(ports.Password, result.Password)
        Assert.DoesNotContain("enroll", ports.Calls)
        result.Wipe()
        Assert.Throws(Of IO.IOException)(Sub() ExchangeServerWizard.RunAsync("operator", ports, Nothing,
            CancellationToken.None).GetAwaiter().GetResult())
    End Sub

    <Fact>
    Public Sub Service_support_is_opt_in_and_leaves_the_ordinary_engine_guard_intact()
        Dim ports As New ExchangeServerWizardTests.StubPorts With {.HasService = True}
        Dim result = ExchangeServerWizard.RunAsync("operator", ports, Nothing, CancellationToken.None).GetAwaiter().GetResult()
        Assert.Equal(ExchangeWizardOutcome.NotAvailable, result.Outcome)
        Assert.DoesNotContain("add", ports.Calls)
        ports.Calls.Clear()
        result = ExchangeServerWizard.RunAsync("operator", ports, Nothing, CancellationToken.None,
            allowManagedService:=True).GetAwaiter().GetResult()
        Assert.Equal(ExchangeWizardOutcome.Done, result.Outcome)
        Assert.Contains("add", ports.Calls)
        result.Wipe()
    End Sub
End Class
