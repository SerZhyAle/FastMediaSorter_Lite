Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' "Use this PC as my server" (ticket 168 section 3.7, Q168-1) with stand-in ports that count every call: the order of the
''' steps, that nothing secret is sent before the certificate was compared with the one the binary reports, that an account
''' that was made is never rolled back silently (its password is kept to be shown once), that a step that fails stops the
''' flow and says which, and the dialog around it. The real binary is driven by <see cref="ExchangeBinaryIntegrationTests"/>.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeServerWizardTests

    ''' <summary>Stand-in ports: each call is counted, each answer is a field the test sets.</summary>
    Friend NotInheritable Class StubPorts
        Implements IExchangeWizardPorts

        Public Report As New ExchangeProcess.InspectReport With {.Answered = True, .Listen = ":22022", .Fingerprint = "SHA256:real", .CertPresent = True}
        Public StartException As Exception
        Public HasService As Boolean
        Public ConsoleRunning As Boolean
        Public Password As String = "Generated16Chars9"
        Public AddReply As ExchangeAdminReply = ExchangeAdminReply.Success("", ExchangeAdminRoute.Offline)
        Public StartResult As ExchangeProcess.StartOutcome = ExchangeProcess.StartOutcome.Started
        Public Offered As Boolean = True
        ''' <summary>The probe fails this many times (a server that is still opening its socket) before it answers.</summary>
        Public ProbeFailures As Integer
        Public ProbeFingerprint As String = "SHA256:real"
        Public ProbePlatformValid As Boolean
        Public EnrollResult As New ExchangeEnrollResult(ExchangeEnrollOutcome.Enrolled, ExchangeRefusal.Unknown, "")

        Public ReadOnly Calls As New List(Of String)()
        Public LastEnroll As ExchangeEnrollRequest
        Public EnrollPasswordAtCall As String = ""
        Public AddedLogin As String = ""
        Public AddedPassword As String = ""
        Public Probes As Integer
        Public Delays As Integer
        Public CancelOn As String = ""
        Public Cts As CancellationTokenSource

        Private Sub Note(name As String)
            SyncLock Calls
                Calls.Add(name)
            End SyncLock
            If name = CancelOn AndAlso Cts IsNot Nothing Then Cts.Cancel()
        End Sub

        Public Function Inspect() As ExchangeProcess.InspectReport Implements IExchangeWizardPorts.Inspect
            Note("inspect")
            Return Report
        End Function

        Public Function ServiceExists() As Boolean Implements IExchangeWizardPorts.ServiceExists
            Note("service")
            Return HasService
        End Function

        Public Function IsConsoleRunning() As Boolean Implements IExchangeWizardPorts.IsConsoleRunning
            Note("running")
            Return ConsoleRunning
        End Function

        Public Function GeneratePassword() As String Implements IExchangeWizardPorts.GeneratePassword
            Note("generate")
            Return Password
        End Function

        Public Function AddAccountAsync(login As String, password As String, ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeWizardPorts.AddAccountAsync
            Note("add")
            AddedLogin = login
            AddedPassword = password
            Return Task.FromResult(AddReply)
        End Function

        Public Function Start() As ExchangeProcess.StartOutcome Implements IExchangeWizardPorts.Start
            Note("start")
            If StartException IsNot Nothing Then Throw StartException
            Return StartResult
        End Function

        Public Function AgentOffered() As Boolean Implements IExchangeWizardPorts.AgentOffered
            Note("offered")
            Return Offered
        End Function

        Public Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeWizardPorts.ProbeAsync
            Note("probe " & server)
            Probes += 1
            If Probes <= ProbeFailures Then Throw New ExchangeConnectException(ExchangeConnectFailure.Network, "", "refused")
            Return Task.FromResult(New ExchangeCertificateInfo(ProbeFingerprint, ProbePlatformValid))
        End Function

        Public Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult) Implements IExchangeWizardPorts.EnrollAsync
            Note("enroll")
            LastEnroll = request
            EnrollPasswordAtCall = request.Password
            Return Task.FromResult(EnrollResult)
        End Function

        Public Function MachineName() As String Implements IExchangeWizardPorts.MachineName
            Return "TESTPC"
        End Function

        Public Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task Implements IExchangeWizardPorts.DelayAsync
            Delays += 1
            Return Task.CompletedTask
        End Function

        Public Function CalledOnly(ParamArray names As String()) As Boolean
            Return Calls.All(Function(c) names.Any(Function(n) c.StartsWith(n, StringComparison.Ordinal)))
        End Function
    End Class

    Private Shared Function Run(ports As StubPorts, Optional login As String = "Owner", Optional progress As Action(Of ExchangeWizardStep) = Nothing,
                                Optional ct As CancellationToken = Nothing) As ExchangeWizardResult
        Return ExchangeServerWizard.RunAsync(login, ports, progress, ct).GetAwaiter().GetResult()
    End Function

    ' --- the address of the local server -----------------------------------------------------------------

    <Theory>
    <InlineData(":22022", "127.0.0.1:22022")>
    <InlineData("0.0.0.0:2222", "127.0.0.1:2222")>
    <InlineData("[::]:2222", "127.0.0.1:2222")>
    <InlineData("127.0.0.1:3333", "127.0.0.1:3333")>
    <InlineData("192.168.1.5:4444", "192.168.1.5:4444")>
    <InlineData("[::1]:5555", "[::1]:5555")>
    <InlineData("", "127.0.0.1:22022")>
    <InlineData("garbage", "127.0.0.1:22022")>
    Public Sub This_pc_reaches_its_own_server_on_the_listen_port_by_loopback_unless_the_listen_host_is_one_address(listen As String, expected As String)
        Assert.Equal(expected, ExchangeServerWizard.LocalAddressOf(listen))
    End Sub

    ' --- the happy path ----------------------------------------------------------------------------------

    <Fact>
    Public Sub The_steps_run_in_order_account_start_certificate_enrol_and_the_facts_come_back()
        Dim p As New StubPorts()
        Dim steps As New List(Of ExchangeWizardStep)()
        Dim r As ExchangeWizardResult = Run(p, "  Owner ", Sub(s) steps.Add(s))
        Assert.Equal(ExchangeWizardOutcome.Done, r.Outcome)
        Assert.Equal({ExchangeWizardStep.CreatingAccount, ExchangeWizardStep.StartingServer, ExchangeWizardStep.CheckingCertificate, ExchangeWizardStep.EnrollingThisPc}, steps.ToArray())
        Assert.Equal({"inspect", "service", "generate", "add", "running", "start", "inspect", "offered", "probe 127.0.0.1:22022", "enroll"}, p.Calls.ToArray())
        Assert.Equal("owner", r.Login)
        Assert.Equal("owner", p.AddedLogin)
        Assert.Equal("Generated16Chars9", r.Password)
        Assert.Equal(p.Password, p.AddedPassword)
        Assert.True(r.AccountCreated)
        Assert.Equal("127.0.0.1:22022", r.LocalAddress)
        Assert.Equal("SHA256:real", r.Fingerprint)
    End Sub

    <Fact>
    Public Sub The_agent_enrols_with_the_generated_password_pinned_to_the_fingerprint_the_binary_reported_and_the_request_is_wiped()
        Dim p As New StubPorts()
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal("Generated16Chars9", p.EnrollPasswordAtCall)
        Dim req As ExchangeEnrollRequest = p.LastEnroll
        Assert.Equal("127.0.0.1:22022", req.Server)
        Assert.Equal("owner", req.Login)
        Assert.Equal("SHA256:real", req.AcceptedFingerprint)
        Assert.False(req.PlatformValid)
        Assert.Equal("TESTPC", req.DeviceName)
        ' the password is forgotten by the request the moment the enrolment returned; only the result keeps it, to be shown once
        Assert.Equal("", req.Password)
        Assert.Equal("", req.PairingCode)
        Assert.Equal("Generated16Chars9", r.Password)
        r.Wipe()
        Assert.Equal("", r.Password)
        Assert.DoesNotContain("Generated16Chars9", r.ToString())
    End Sub

    <Fact>
    Public Sub An_already_running_console_server_is_not_started_again_and_the_public_endpoint_is_reported()
        Dim p As New StubPorts With {.ConsoleRunning = True}
        p.Report.PublicEndpoint = "203.0.113.7:22022"
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.Done, r.Outcome)
        Assert.DoesNotContain("start", p.Calls)
        Assert.Equal("203.0.113.7:22022", r.PublicEndpoint)
    End Sub

    <Fact>
    Public Sub A_server_that_is_still_opening_its_socket_is_asked_again_a_bounded_number_of_times()
        Dim p As New StubPorts With {.ProbeFailures = 3}
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.Done, r.Outcome)
        Assert.Equal(4, p.Probes)
        Assert.Equal(3, p.Delays)
    End Sub

    ' --- this PC as the first device of an account that was just added or reset (no account, no start, no service check) ---

    Private Shared Function EnrollOnly(ports As StubPorts, Optional login As String = "Owner", Optional password As String = "TypedPassword12") As ExchangeWizardResult
        Return ExchangeServerWizard.EnrollThisPcAsync(login, password, ports, Nothing, CancellationToken.None).GetAwaiter().GetResult()
    End Function

    <Fact>
    Public Sub Enrolling_this_pc_alone_creates_nothing_starts_nothing_and_ignores_a_registered_service()
        Dim p As New StubPorts With {.HasService = True}
        Dim r As ExchangeWizardResult = EnrollOnly(p)
        Assert.Equal(ExchangeWizardOutcome.Done, r.Outcome)
        Assert.Equal({"inspect", "offered", "probe 127.0.0.1:22022", "enroll"}, p.Calls.ToArray())
        Assert.Equal("TypedPassword12", p.EnrollPasswordAtCall)
        Assert.Equal("owner", p.LastEnroll.Login)
        Assert.Equal("SHA256:real", p.LastEnroll.AcceptedFingerprint)
        Assert.False(r.AccountCreated)
        ' the wizard keeps its password to show it once; this flow was given one and keeps nothing
        Assert.Equal("", r.Password)
        Assert.Equal("", p.LastEnroll.Password)
    End Sub

    <Fact>
    Public Sub Enrolling_this_pc_alone_sends_nothing_secret_to_a_server_whose_certificate_is_not_the_reported_one()
        Dim p As New StubPorts With {.ProbeFingerprint = "SHA256:someone-else"}
        Assert.Equal(ExchangeWizardOutcome.CertificateMismatch, EnrollOnly(p).Outcome)
        Assert.DoesNotContain("enroll", p.Calls)
    End Sub

    <Fact>
    Public Sub Enrolling_this_pc_alone_stops_when_the_binary_cannot_be_asked()
        Dim p As New StubPorts()
        p.Report = New ExchangeProcess.InspectReport With {.Answered = False}
        Assert.Equal(ExchangeWizardOutcome.NotAvailable, EnrollOnly(p).Outcome)
        Assert.Equal({"inspect"}, p.Calls.ToArray())
    End Sub

    <Fact>
    Public Sub A_refused_enrolment_of_this_pc_says_so_in_the_first_device_sentence()
        Dim p As New StubPorts With {.EnrollResult = New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.BadCredentials, "")}
        Dim r As ExchangeWizardResult = EnrollOnly(p)
        Assert.Equal(ExchangeWizardOutcome.EnrollFailed, r.Outcome)
        Assert.NotEqual("", ExchangeAdminText.ThisPcEnrolment(r))
    End Sub

    ' --- nothing is done when nothing can be done --------------------------------------------------------------

    <Theory>
    <InlineData("")>
    <InlineData("ab")>
    <InlineData("has space")>
    <InlineData("alice@home")>
    Public Sub A_login_that_cannot_be_one_stops_before_anything_is_touched(login As String)
        Dim p As New StubPorts()
        Dim r As ExchangeWizardResult = Run(p, login)
        Assert.Equal(ExchangeWizardOutcome.LoginRefused, r.Outcome)
        Assert.Empty(p.Calls)
        Assert.False(r.AccountCreated)
    End Sub

    <Fact>
    Public Sub A_registered_service_or_an_unreadable_binary_stops_before_an_account_is_made()
        Dim service As New StubPorts With {.HasService = True}
        Assert.Equal(ExchangeWizardOutcome.NotAvailable, Run(service).Outcome)
        Assert.True(service.CalledOnly("inspect", "service"))

        Dim unreadable As New StubPorts()
        unreadable.Report = New ExchangeProcess.InspectReport()
        Assert.Equal(ExchangeWizardOutcome.NotAvailable, Run(unreadable).Outcome)
        Assert.DoesNotContain("add", unreadable.Calls)
    End Sub

    <Fact>
    Public Sub An_existing_login_changes_nothing_else_and_keeps_no_password()
        Dim p As New StubPorts With {.AddReply = ExchangeAdminReply.Failure(ExchangeAdminCodes.Exists, ExchangeAdminRoute.Offline)}
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.AccountExists, r.Outcome)
        Assert.False(r.AccountCreated)
        Assert.Equal("", r.Password)
        Assert.True(p.CalledOnly("inspect", "service", "generate", "add"))
    End Sub

    <Theory>
    <InlineData(ExchangeAdminCodes.NotReached)>
    <InlineData(ExchangeAdminCodes.Internal)>
    <InlineData(ExchangeAdminCodes.WeakPassword)>
    <InlineData(ExchangeAdminCodes.BadLogin)>
    Public Sub Any_other_refusal_of_the_account_is_a_failure_with_nothing_done_after_it(code As String)
        Dim p As New StubPorts With {.AddReply = ExchangeAdminReply.Failure(code, ExchangeAdminRoute.Offline)}
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.AccountFailed, r.Outcome)
        Assert.False(r.AccountCreated)
        Assert.Equal("", r.Password)
        Assert.True(p.CalledOnly("inspect", "service", "generate", "add"))
    End Sub

    ' --- a step that fails stops the flow, and an account that exists is never lost -----------------------------

    <Theory>
    <InlineData("Failed")>
    <InlineData("ConfigProblem")>
    <InlineData("BesideService")>
    <InlineData("NoAccount")>
    <InlineData("Unavailable")>
    Public Sub A_server_that_does_not_start_leaves_the_account_and_keeps_its_password_to_be_shown_once(name As String)
        Dim outcome As ExchangeProcess.StartOutcome = DirectCast([Enum].Parse(GetType(ExchangeProcess.StartOutcome), name), ExchangeProcess.StartOutcome)
        Dim p As New StubPorts With {.StartResult = outcome}
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.StartFailed, r.Outcome)
        Assert.Equal(outcome, r.StartOutcome)
        Assert.True(r.AccountCreated)
        Assert.Equal("Generated16Chars9", r.Password)
        Assert.DoesNotContain("enroll", p.Calls)
        Assert.False(p.Calls.Any(Function(c) c.StartsWith("probe", StringComparison.Ordinal)))
    End Sub

    <Fact>
    Public Sub A_server_that_never_answers_ends_the_flow_without_a_secret_sent_anywhere()
        Dim p As New StubPorts With {.ProbeFailures = Integer.MaxValue}
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.ServerNotAnswering, r.Outcome)
        Assert.Equal(ExchangeServerWizard.AnswerAttempts, p.Probes)
        Assert.True(r.AccountCreated)
        Assert.Equal("Generated16Chars9", r.Password)
        Assert.DoesNotContain("enroll", p.Calls)
    End Sub

    ''' <summary>What answers on the port must be the server the binary reports. A different certificate - another program on the port, a
    ''' server of someone else - gets nothing: the password is not sent.</summary>
    <Theory>
    <InlineData("SHA256:someoneelse", "SHA256:real")>
    <InlineData("SHA256:real", "")>
    Public Sub A_certificate_that_is_not_the_one_the_binary_reports_gets_no_password(shown As String, reported As String)
        Dim p As New StubPorts With {.ProbeFingerprint = shown}
        p.Report.Fingerprint = reported
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.CertificateMismatch, r.Outcome)
        Assert.DoesNotContain("enroll", p.Calls)
        Assert.Null(p.LastEnroll)
        Assert.True(r.AccountCreated)
    End Sub

    <Fact>
    Public Sub Without_the_client_features_the_server_and_the_account_are_made_and_nothing_is_probed_or_enrolled()
        Dim p As New StubPorts With {.Offered = False}
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.DoneNoAgent, r.Outcome)
        Assert.True(r.AccountCreated)
        Assert.Equal("SHA256:real", r.Fingerprint)
        Assert.False(p.Calls.Any(Function(c) c.StartsWith("probe", StringComparison.Ordinal)))
        Assert.DoesNotContain("enroll", p.Calls)
    End Sub

    <Theory>
    <InlineData("Refused")>
    <InlineData("PinMismatch")>
    <InlineData("TimedOut")>
    <InlineData("StoreFailed")>
    Public Sub A_refused_enrolment_keeps_the_account_and_the_password_and_carries_the_agents_result(name As String)
        Dim outcome As ExchangeEnrollOutcome = DirectCast([Enum].Parse(GetType(ExchangeEnrollOutcome), name), ExchangeEnrollOutcome)
        Dim p As New StubPorts With {.EnrollResult = New ExchangeEnrollResult(outcome, ExchangeRefusal.BadCredentials, "")}
        Dim r As ExchangeWizardResult = Run(p)
        Assert.Equal(ExchangeWizardOutcome.EnrollFailed, r.Outcome)
        Assert.Equal(outcome, r.EnrollResult.Outcome)
        Assert.True(r.AccountCreated)
        Assert.Equal("Generated16Chars9", r.Password)
        Assert.Equal("", p.LastEnroll.Password)
    End Sub

    <Fact>
    Public Sub A_cancellation_between_steps_stops_the_flow_there()
        Dim p As New StubPorts()
        Using cts As New CancellationTokenSource()
            p.Cts = cts
            p.CancelOn = "start"
            Assert.ThrowsAny(Of OperationCanceledException)(Sub() Run(p, ct:=cts.Token))
        End Using
        Assert.DoesNotContain("enroll", p.Calls)
        Assert.False(p.Calls.Any(Function(c) c.StartsWith("probe", StringComparison.Ordinal)))
    End Sub

    ''' <summary>The wizard reaches the device agent only behind the client gate (180 rule 2): its production ports read the gate, and the flow
    ''' asks them before it probes or enrols anything (proved above with the gate stood in as shut).</summary>
    <Fact>
    Public Sub The_production_ports_read_the_client_gate_before_the_agent_is_touched()
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ExchangeServerWizard.vb"))
        Assert.Contains("Return ExchangeFeatures.IsEnabled()", ExchangeTestSupport.MethodBody(code, "Public Function AgentOffered() As Boolean"))
        ' the shared tail asks AgentOffered first and only then probes - and both flows (the wizard and "this PC alone") go through it
        Dim flow As String = ExchangeTestSupport.MethodBody(code, "Private Shared Async Function ProbeAndEnrollAsync(")
        Assert.True(flow.IndexOf("ports.AgentOffered()", StringComparison.Ordinal) < flow.IndexOf("ProbeWithRetriesAsync(", StringComparison.Ordinal))
        Assert.True(flow.IndexOf("ports.AgentOffered()", StringComparison.Ordinal) < flow.IndexOf("ports.EnrollAsync(", StringComparison.Ordinal))
        Assert.DoesNotContain("ports.EnrollAsync(", ExchangeTestSupport.MethodBody(code, "Friend Shared Async Function RunAsync("))
        Assert.DoesNotContain("ports.EnrollAsync(", ExchangeTestSupport.MethodBody(code, "Friend Shared Async Function EnrollThisPcAsync("))
        Assert.Contains("ProbeAndEnrollAsync(", ExchangeTestSupport.MethodBody(code, "Friend Shared Async Function RunAsync("))
        Assert.Contains("ProbeAndEnrollAsync(", ExchangeTestSupport.MethodBody(code, "Friend Shared Async Function EnrollThisPcAsync("))
        ' and it is the host's gated door, never a second agent, that the ports use
        Assert.DoesNotContain("New ExchangeDeviceAgent(", code)
        Assert.Contains("ExchangeDeviceHost.GetOrCreate()", code)
    End Sub

    ' --- the dialog ----------------------------------------------------------------------------------------------

    Private Shared Function Field(Of T)(obj As Object, name As String) As T
        Return DirectCast(obj.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(obj), T)
    End Function

    ''' <summary>Presses Start on a dialog that was never shown and pumps messages until the flow has ended and the facts are drawn.</summary>
    Private Shared Sub PressStart(dlg As Exchange_Server_Wizard_Form)
        Dim button As Button = Field(Of Button)(dlg, "_btnStart")
        Dim facts As Control = Field(Of Control)(dlg, "_pnlFacts")
        Dim outcome As Label = Field(Of Label)(dlg, "_lblOutcome")
        GetType(Exchange_Server_Wizard_Form).GetMethod("OnStartClicked", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(dlg, New Object() {Nothing, EventArgs.Empty})
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(10)
        While DateTime.UtcNow < deadline AndAlso outcome.Text.Length = 0
            Application.DoEvents()
            Thread.Sleep(10)
        End While
        Assert.True(outcome.Text.Length > 0, "the flow did not end")
    End Sub

    <Fact>
    Public Sub The_dialog_shows_the_facts_with_the_password_masked_once_and_wipes_it_when_it_closes()
        ExchangeTestSupport.OnUi(Sub()
                                     Dim p As New StubPorts()
                                     p.Report.PublicEndpoint = "203.0.113.7:22022"
                                     Dim dlg As New Exchange_Server_Wizard_Form(p, "Owner")
                                     Try
                                         dlg.Location = New System.Drawing.Point(-5000, -5000)
                                         dlg.StartPosition = FormStartPosition.Manual
                                         dlg.ShowInTaskbar = False
                                         dlg.Show()
                                         Assert.False(Field(Of Control)(dlg, "_pnlFacts").Visible)
                                         PressStart(dlg)
                                         Assert.True(Field(Of Control)(dlg, "_pnlFacts").Visible)
                                         Assert.False(Field(Of Control)(dlg, "_pnlInput").Visible, "the flow does not run twice: the account exists")
                                         Assert.False(Field(Of Control)(dlg, "_btnStart").Visible)
                                         Assert.Equal("127.0.0.1:22022", Field(Of TextBox)(dlg, "_txtThisPc").Text)
                                         Assert.Equal("203.0.113.7:22022", Field(Of TextBox)(dlg, "_txtOthers").Text)
                                         Assert.Equal("owner", Field(Of TextBox)(dlg, "_txtLogin2").Text)
                                         Assert.Equal("SHA256:real", Field(Of TextBox)(dlg, "_txtFingerprint").Text)
                                         Dim pw As TextBox = Field(Of TextBox)(dlg, "_txtPassword")
                                         Assert.Equal("Generated16Chars9", pw.Text)
                                         Assert.True(pw.UseSystemPasswordChar, "masked until revealed")
                                         Assert.True(pw.ReadOnly)
                                         Assert.Equal(ExchangeAdminText.WizardOutcome(dlg.Result), Field(Of Label)(dlg, "_lblOutcome").Text)
                                         ' the result no longer holds the password: the field is its only copy
                                         Assert.Equal("", dlg.Result.Password)
                                         ' revealing it counts: closing afterwards does not ask
                                         Field(Of CheckBox)(dlg, "_chkShow").Checked = True
                                         Assert.False(pw.UseSystemPasswordChar)
                                         dlg.Close()
                                         Assert.Equal("", pw.Text)
                                     Finally
                                         dlg.Dispose()
                                     End Try
                                 End Sub)
    End Sub

    <Fact>
    Public Sub First_setup_keeps_the_login_start_and_generated_credentials_reachable()
        ExchangeTestSupport.OnUi(Sub()
                                     Using dlg As New Exchange_Server_Wizard_Form(New StubPorts(), "Owner")
                                         dlg.Show()
                                         Application.DoEvents()
                                         Assert.True(Field(Of TextBox)(dlg, "_txtLogin").Visible)
                                         Assert.True(Field(Of Button)(dlg, "_btnStart").Visible)
                                         PressStart(dlg)
                                         Application.DoEvents()
                                         Assert.Equal(ExchangeWizardOutcome.Done, dlg.Result.Outcome)
                                         Assert.True(Field(Of TextBox)(dlg, "_txtPassword").UseSystemPasswordChar)
                                         dlg.AutoSize = False
                                         dlg.ClientSize = New System.Drawing.Size(dlg.ClientSize.Width, dlg.LogicalToDeviceUnits(400))
                                         Dim closeButton = Field(Of Button)(dlg, "_btnClose")
                                         dlg.ScrollControlIntoView(closeButton)
                                         Application.DoEvents()
                                         Dim bounds = dlg.RectangleToClient(closeButton.RectangleToScreen(closeButton.ClientRectangle))
                                         Assert.True(dlg.ClientRectangle.Contains(bounds), "Setup result cannot reach its last action: " & bounds.ToString())
                                         dlg.Hide()
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub A_flow_that_made_no_account_lets_the_person_fix_the_login_and_go_again()
        ExchangeTestSupport.OnUi(Sub()
                                     Dim p As New StubPorts With {.AddReply = ExchangeAdminReply.Failure(ExchangeAdminCodes.Exists, ExchangeAdminRoute.Offline)}
                                     Using dlg As New Exchange_Server_Wizard_Form(p, "owner")
                                         dlg.Location = New System.Drawing.Point(-5000, -5000)
                                         dlg.StartPosition = FormStartPosition.Manual
                                         dlg.ShowInTaskbar = False
                                         dlg.Show()
                                         PressStart(dlg)
                                         Assert.False(Field(Of Control)(dlg, "_pnlFacts").Visible)
                                         Assert.True(Field(Of Control)(dlg, "_pnlInput").Visible)
                                         Assert.True(Field(Of Button)(dlg, "_btnStart").Enabled)
                                         Assert.True(Field(Of TextBox)(dlg, "_txtLogin").Enabled)
                                         Assert.Equal(ExchangeWizardOutcome.AccountExists, dlg.Result.Outcome)
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub An_invalid_login_is_not_started_and_asks_nothing_of_the_ports()
        ExchangeTestSupport.OnUi(Sub()
                                     Dim p As New StubPorts()
                                     Using dlg As New Exchange_Server_Wizard_Form(p, "x")
                                         dlg.Location = New System.Drawing.Point(-5000, -5000)
                                         dlg.StartPosition = FormStartPosition.Manual
                                         dlg.ShowInTaskbar = False
                                         dlg.Show()
                                         GetType(Exchange_Server_Wizard_Form).GetMethod("OnStartClicked", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(dlg, New Object() {Nothing, EventArgs.Empty})
                                         Application.DoEvents()
                                         Assert.Empty(p.Calls)
                                         Assert.Equal("", Field(Of Label)(dlg, "_lblOutcome").Text)
                                     End Using
                                 End Sub)
    End Sub

    ''' <summary>The dialog is built in every language (the facts, the statement, the buttons) and keeps the Companion recipe: language direction
    ''' first, DPI scaling as the LAST line of the builder, the clamp in OnLoad; a flow in progress is never interrupted by closing.</summary>
    <Fact>
    Public Sub The_dialogs_are_built_in_every_language_and_follow_the_companion_recipe()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                ExchangeTestSupport.OnUi(Sub()
                                             Using wizard As New Exchange_Server_Wizard_Form(New StubPorts(), "owner")
                                                 Assert.False(String.IsNullOrWhiteSpace(wizard.Text), code)
                                             End Using
                                             Using add As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add, "", True)
                                                 Assert.False(String.IsNullOrWhiteSpace(add.Text), code)
                                             End Using
                                             Using reset As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, "alice")
                                                 Assert.Contains("alice", reset.Text)
                                             End Using
                                         End Sub)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
        For Each file As String In {"Forms\Exchange_Server_Wizard_Form.vb", "Forms\Exchange_Account_Admin_Form.vb"}
            Dim src As String = ExchangeTestSupport.ReadSource(file)
            Dim build As String = ExchangeTestSupport.MethodBody(src, "Private Sub BuildUi(")
            Assert.EndsWith("DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout", build.TrimEnd())
            Assert.StartsWith("Private Sub BuildUi(", build.TrimStart())
            Assert.Contains("UiLanguage.ApplyTo(Me)", build)
            Assert.Contains("DpiLayout.ClampToWorkingArea(Me)", src)
        Next
        Dim body As String = ExchangeTestSupport.MethodBody(ExchangeTestSupport.ReadSource("Forms\Exchange_Server_Wizard_Form.vb"), "Protected Overrides Sub OnFormClosing(")
        Assert.Contains("_busy", body)
        Assert.Contains("MessageBoxDefaultButton.Button2", body)
        Assert.Contains("Not _copied AndAlso Not _revealed", body)
    End Sub

    <Fact>
    Public Sub The_add_dialog_refuses_a_bad_login_and_makes_a_generated_password_that_meets_the_floor()
        ExchangeTestSupport.OnUi(Sub()
                                     Using dlg As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add)
                                         Dim login As TextBox = Field(Of TextBox)(dlg, "_txtLogin")
                                         Dim hint As Label = Field(Of Label)(dlg, "_lblLoginHint")
                                         Dim ok As Action = Sub() GetType(Exchange_Account_Admin_Form).GetMethod("OnOkClicked", BindingFlags.Instance Or BindingFlags.NonPublic).
                                             Invoke(dlg, New Object() {Nothing, EventArgs.Empty})
                                         login.Text = "x"
                                         ok()
                                         Assert.Equal(DialogResult.None, dlg.DialogResult)
                                         Assert.Equal("", dlg.Password)
                                         Assert.Equal(CollapsibleSection.AttentionColor, hint.ForeColor)

                                         login.Text = "  Alice.W "
                                         ok()
                                         Assert.Equal(DialogResult.OK, dlg.DialogResult)
                                         Assert.Equal("alice.w", dlg.Login)
                                         Assert.True(dlg.Generated)
                                         Assert.True(ExchangePasswordPolicy.IsAccepted(dlg.Password))
                                         dlg.WipePassword()
                                         Assert.Equal("", dlg.Password)
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_first_account_shows_the_statement_once_more_and_a_later_one_does_not()
        ExchangeTestSupport.OnUi(Sub()
                                     Dim saved As String = Localization.CurrentCode
                                     Try
                                         Localization.CurrentCode = "en"
                                         Using first As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add, "", True)
                                             Dim boxes As List(Of TextBox) = AllOf(Of TextBox)(first).Where(Function(t) t.Multiline).ToList()
                                             Assert.Single(boxes)
                                             Assert.Equal(String.Join(vbCrLf & vbCrLf, ExchangeStatement.Paragraphs()), boxes(0).Text)
                                             Assert.True(boxes(0).ReadOnly)
                                         End Using
                                         Using later As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add, "", False)
                                             Assert.Empty(AllOf(Of TextBox)(later).Where(Function(t) t.Multiline))
                                         End Using
                                         ' a reset never carries the statement, whatever is asked
                                         Using reset As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, "alice", True)
                                             Assert.Empty(AllOf(Of TextBox)(reset).Where(Function(t) t.Multiline))
                                             Assert.Single(AllOf(Of CheckBox)(reset))
                                         End Using
                                     Finally
                                         Localization.CurrentCode = saved
                                     End Try
                                 End Sub)
    End Sub

    Private Shared Function AllOf(Of T As Control)(root As Control) As List(Of T)
        Dim list As New List(Of T)()
        For Each c As Control In root.Controls
            If TypeOf c Is T Then list.Add(DirectCast(c, T))
            list.AddRange(AllOf(Of T)(c))
        Next
        Return list
    End Function

End Class
