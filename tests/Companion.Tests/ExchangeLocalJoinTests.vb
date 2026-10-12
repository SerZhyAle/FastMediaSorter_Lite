Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The local join (owner, 2026-10-12): the Share Manager of a PC that runs the exchange server becomes a device of that server by
''' itself and publishes. Stand-in ports count every call, so what is held is the ORDER and what is NOT done: nothing without the
''' running server's own channel, no guess between several accounts, no code before the certificate is compared, never a password,
''' and publishing only after an enrolment that succeeded.
''' </summary>
Public Class ExchangeLocalJoinTests

    Private NotInheritable Class Stub
        Implements IExchangeLocalJoinPorts

        Public OfferedValue As Boolean = True
        Public Configured As Boolean
        Public DeclinedValue As Boolean
        Public Component As Boolean = True
        Public Report As New ExchangeProcess.InspectReport With {.Answered = True, .Listen = ":22022", .Fingerprint = "SHA256:real", .CertPresent = True}
        Public ListReply As ExchangeAdminReply = ExchangeAdminReply.Success("[{""login"":""alice"",""createdAt"":""x"",""devices"":0}]", ExchangeAdminRoute.Pipe)
        Public CodeReply As ExchangeAdminReply = ExchangeAdminReply.Success("{""code"":""ABCD2345"",""expiresAt"":""2026-10-12T00:00:00Z""}", ExchangeAdminRoute.Pipe)
        Public ProbeFailures As Integer
        Public ProbeFingerprint As String = "SHA256:real"
        Public EnrollResult As New ExchangeEnrollResult(ExchangeEnrollOutcome.Enrolled, ExchangeRefusal.Unknown, "")

        Public ReadOnly Calls As New List(Of String)()
        Public LastEnroll As ExchangeEnrollRequest
        Public PasswordAtEnroll As String = "unset"
        Public CodeAtEnroll As String = ""
        Public CodeLogin As String = ""
        Public Delays As New List(Of Integer)()

        Private Sub Note(name As String)
            SyncLock Calls
                Calls.Add(name)
            End SyncLock
        End Sub

        Public Function Offered() As Boolean Implements IExchangeLocalJoinPorts.Offered
            Return OfferedValue
        End Function

        Public Function AlreadyConfigured() As Boolean Implements IExchangeLocalJoinPorts.AlreadyConfigured
            Return Configured
        End Function

        Public Function Declined() As Boolean Implements IExchangeLocalJoinPorts.Declined
            Return DeclinedValue
        End Function

        Public Function ComponentPresent() As Boolean Implements IExchangeLocalJoinPorts.ComponentPresent
            Return Component
        End Function

        Public Function Inspect() As ExchangeProcess.InspectReport Implements IExchangeLocalJoinPorts.Inspect
            Note("inspect")
            Return Report
        End Function

        Public Function ListAccountsAsync(ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeLocalJoinPorts.ListAccountsAsync
            Note("list")
            Return Task.FromResult(ListReply)
        End Function

        Public Function PairingCodeAsync(login As String, ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeLocalJoinPorts.PairingCodeAsync
            Note("code")
            CodeLogin = login
            Return Task.FromResult(CodeReply)
        End Function

        Public Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeLocalJoinPorts.ProbeAsync
            Note("probe " & server)
            If ProbeFailures > 0 Then
                ProbeFailures -= 1
                Throw New ExchangeConnectException(ExchangeConnectFailure.Network, "", "refused")
            End If
            Return Task.FromResult(New ExchangeCertificateInfo(ProbeFingerprint, False))
        End Function

        Public Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult) Implements IExchangeLocalJoinPorts.EnrollAsync
            Note("enroll")
            LastEnroll = request
            PasswordAtEnroll = request.Password
            CodeAtEnroll = request.PairingCode
            Return Task.FromResult(EnrollResult)
        End Function

        Public Sub EnablePublishing() Implements IExchangeLocalJoinPorts.EnablePublishing
            Note("publish-on")
        End Sub

        Public Function MachineName() As String Implements IExchangeLocalJoinPorts.MachineName
            Return "HOST"
        End Function

        Public Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task Implements IExchangeLocalJoinPorts.DelayAsync
            Delays.Add(milliseconds)
            Return Task.CompletedTask
        End Function
    End Class

    <Fact>
    Public Async Function A_running_server_with_one_account_is_joined_with_a_code_and_then_published_to() As Task
        Dim s As New Stub()
        Dim outcome As ExchangeLocalJoinOutcome = Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None)
        Assert.Equal(ExchangeLocalJoinOutcome.Joined, outcome)
        ' the order: the account from the server's channel, the certificate compared, then the code, then the enrolment, then publishing
        Assert.Equal({"list", "inspect", "probe 127.0.0.1:22022", "code", "enroll", "publish-on"}, s.Calls.ToArray())
        Assert.Equal("alice", s.CodeLogin)
        Assert.Equal("ABCD2345", s.CodeAtEnroll)
        Assert.Equal("", s.PasswordAtEnroll)   ' never a password
        Assert.Equal("alice", s.LastEnroll.Login)
        Assert.Equal("127.0.0.1:22022", s.LastEnroll.Server)
        Assert.Equal("SHA256:real", s.LastEnroll.AcceptedFingerprint)
        Assert.Equal("HOST", s.LastEnroll.DeviceName)
        Assert.Equal("", s.LastEnroll.PairingCode)   ' wiped once the agent has it
    End Function

    <Fact>
    Public Async Function Nothing_is_asked_of_anyone_when_the_features_are_off_an_account_exists_or_the_person_disconnected() As Task
        For Each configure As Action(Of Stub) In New Action(Of Stub)() {
                Sub(x) x.OfferedValue = False, Sub(x) x.Configured = True, Sub(x) x.DeclinedValue = True, Sub(x) x.Component = False}
            Dim s As New Stub()
            configure(s)
            Assert.Equal(ExchangeLocalJoinOutcome.NotNeeded, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
            Assert.Empty(s.Calls)
        Next
    End Function

    <Fact>
    Public Async Function A_server_that_is_not_running_is_never_started_or_trusted_to_the_offline_verbs() As Task
        Dim s As New Stub() With {.ListReply = ExchangeAdminReply.Success("[{""login"":""alice""}]", ExchangeAdminRoute.Offline)}
        Assert.Equal(ExchangeLocalJoinOutcome.ServerNotRunning, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
        Assert.Equal({"list"}, s.Calls.ToArray())

        s = New Stub() With {.ListReply = ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.NotReached)}
        Assert.Equal(ExchangeLocalJoinOutcome.ServerNotRunning, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
    End Function

    <Fact>
    Public Async Function No_account_or_several_is_not_a_guess_and_no_code_is_requested() As Task
        For Each json As String In {"[]", "[{""login"":""alice""},{""login"":""bob""}]"}
            Dim s As New Stub() With {.ListReply = ExchangeAdminReply.Success(json, ExchangeAdminRoute.Pipe)}
            Assert.Equal(ExchangeLocalJoinOutcome.NotOneAccount, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
            Assert.DoesNotContain("code", s.Calls)
            Assert.DoesNotContain("enroll", s.Calls)
        Next
        Assert.True(ExchangeLocalJoin.IsFinal(ExchangeLocalJoinOutcome.NotOneAccount))
    End Function

    <Fact>
    Public Async Function A_certificate_that_is_not_the_servers_gets_no_code() As Task
        Dim s As New Stub() With {.ProbeFingerprint = "SHA256:someone-else"}
        Assert.Equal(ExchangeLocalJoinOutcome.CertificateMismatch, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
        Assert.DoesNotContain("code", s.Calls)
        Assert.DoesNotContain("enroll", s.Calls)

        ' and a report with no fingerprint to compare with proves nothing
        s = New Stub()
        s.Report.Fingerprint = ""
        Assert.Equal(ExchangeLocalJoinOutcome.CertificateMismatch, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
        Assert.DoesNotContain("code", s.Calls)
    End Function

    <Fact>
    Public Async Function A_server_still_opening_its_socket_is_asked_again_a_bounded_number_of_times() As Task
        Dim s As New Stub() With {.ProbeFailures = ExchangeLocalJoin.ProbeAttempts - 1}
        Assert.Equal(ExchangeLocalJoinOutcome.Joined, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
        Assert.Equal(ExchangeLocalJoin.ProbeAttempts - 1, s.Delays.Count)

        s = New Stub() With {.ProbeFailures = 100}
        Assert.Equal(ExchangeLocalJoinOutcome.ServerNotRunning, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
        Assert.Equal(ExchangeLocalJoin.ProbeAttempts, s.Calls.Where(Function(c) c.StartsWith("probe", StringComparison.Ordinal)).Count())
        Assert.DoesNotContain("code", s.Calls)
    End Function

    <Fact>
    Public Async Function A_refused_code_or_enrolment_publishes_nothing_and_is_tried_again_later() As Task
        Dim s As New Stub() With {.CodeReply = ExchangeAdminReply.Failure(ExchangeAdminCodes.Capacity, ExchangeAdminRoute.Pipe)}
        Assert.Equal(ExchangeLocalJoinOutcome.CodeRefused, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
        Assert.DoesNotContain("enroll", s.Calls)
        Assert.False(ExchangeLocalJoin.IsFinal(ExchangeLocalJoinOutcome.CodeRefused))

        s = New Stub() With {.EnrollResult = New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.BadCredentials, "")}
        Assert.Equal(ExchangeLocalJoinOutcome.EnrollFailed, Await ExchangeLocalJoin.TryJoinAsync(s, CancellationToken.None))
        Assert.DoesNotContain("publish-on", s.Calls)
        Assert.Equal("", s.LastEnroll.PairingCode)   ' wiped on the failing path too
        Assert.False(ExchangeLocalJoin.IsFinal(ExchangeLocalJoinOutcome.EnrollFailed))
    End Function

    <Fact>
    Public Async Function The_schedule_stops_at_the_first_final_outcome_and_waits_the_gaps_between_attempts() As Task
        Dim s As New Stub() With {.ListReply = ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.NotReached)}
        Dim outcome As ExchangeLocalJoinOutcome = Await ExchangeLocalJoin.RunScheduleAsync(s, New Integer() {1, 3, 10}, CancellationToken.None)
        Assert.Equal(ExchangeLocalJoinOutcome.ServerNotRunning, outcome)
        Assert.Equal(3, s.Calls.Where(Function(c) c = "list").Count())
        Assert.Equal(New Integer() {1000, 2000, 7000}, s.Delays.ToArray())

        s = New Stub()
        outcome = Await ExchangeLocalJoin.RunScheduleAsync(s, New Integer() {1, 3, 10}, CancellationToken.None)
        Assert.Equal(ExchangeLocalJoinOutcome.Joined, outcome)
        Assert.Equal(1, s.Calls.Where(Function(c) c = "list").Count())   ' no further attempt after a final outcome
    End Function

    <Fact>
    Public Sub A_server_on_this_pc_is_recognised_by_its_loopback_address_and_a_remote_one_is_not()
        Assert.True(ExchangeLocalJoin.IsLocalServer("127.0.0.1:22022"))
        Assert.True(ExchangeLocalJoin.IsLocalServer("[::1]:22022"))
        Assert.False(ExchangeLocalJoin.IsLocalServer("193.178.50.43:22022"))
        Assert.False(ExchangeLocalJoin.IsLocalServer("host.example:22022"))
        Assert.False(ExchangeLocalJoin.IsLocalServer(""))
    End Sub

    <Fact>
    Public Sub The_main_window_line_is_empty_while_nothing_is_published_to_say_anything_about()
        ' no agent in this test process: no account, no line (the line never promises what it cannot prove)
        Assert.Equal("", ExchangeProducerSummary.Line())
    End Sub

    <Fact>
    Public Sub The_pairing_code_answer_is_read_and_anything_else_is_no_code()
        Assert.Equal("ABCD2345", ExchangeAdminParser.PairingCode("{""code"":""ABCD2345"",""expiresAt"":""x""}"))
        Assert.Equal("", ExchangeAdminParser.PairingCode(""))
        Assert.Equal("", ExchangeAdminParser.PairingCode("[]"))
        Assert.Equal("", ExchangeAdminParser.PairingCode("not json"))
        Assert.Equal("", ExchangeAdminParser.PairingCode("{""code"":5}"))
    End Sub

    <Fact>
    Public Sub The_offline_verbs_have_no_pairing_code_to_give()
        Dim reply As ExchangeAdminReply = ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpDevicePairingCode, "alice"))
        Assert.False(reply.Ok)
        Assert.Equal(ExchangeAdminCodes.NotReached, reply.Code)
    End Sub

    <Fact>
    Public Sub The_disconnect_choice_is_kept_and_an_explicit_sign_in_clears_it()
        Dim port As New Dictionary(Of String, String)()
        Dim store As New ExchangeAccountStore(New MapSettings(port))
        Assert.False(store.LocalJoinDeclined())
        store.SetLocalJoinDeclined(True)
        Assert.True(store.LocalJoinDeclined())
        store.Save(New ExchangeAccount("127.0.0.1:22022", "alice", "", ExchangeCertificateKind.None, ExchangeIds.NewId(), "HOST"))
        Assert.False(store.LocalJoinDeclined())
    End Sub

    Private NotInheritable Class MapSettings
        Implements IExchangeSettingsPort

        Private ReadOnly _map As Dictionary(Of String, String)

        Public Sub New(map As Dictionary(Of String, String))
            _map = map
        End Sub

        Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
            Dim v As String = Nothing
            Return If(_map.TryGetValue(name, v), v, "")
        End Function

        Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
            _map(name) = value
        End Sub

        Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
            _map.Remove(name)
        End Sub
    End Class

    <Fact>
    Public Sub The_server_component_beside_the_program_turns_the_client_features_on_and_its_absence_leaves_them_shut()
        Try
            ExchangeFeatures.ComponentProbeOverride = Function() True
            Assert.True(ExchangeFeatures.ComponentInstalled())
            ExchangeFeatures.ComponentProbeOverride = Function() False
            Assert.False(ExchangeFeatures.ComponentInstalled())
        Finally
            ExchangeFeatures.ComponentProbeOverride = Nothing
        End Try
        ' the reader overload, which the guard tests pin, does not look at folders: gate shut + no switch = off
        Assert.False(ExchangeFeatures.IsEnabled(Function(name, target) ""))
    End Sub
End Class
