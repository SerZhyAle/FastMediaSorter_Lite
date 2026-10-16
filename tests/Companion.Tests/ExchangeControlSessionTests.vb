Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 180 T-2, section 5: the control stream and its state machine - hello, welcome, the republish hook, list, subscribe, the keepalive and
''' its three-interval window, reconnect with the bounded backoff, and the refusal table. Every case runs against ExchangeStubAccount (a TLS
''' stand-in on 127.0.0.1); the clock is the real one with a short keepalive unit, and waits of a second or more are recorded and skipped, so the
''' whole table is checked in seconds and the production delays are asserted by value.
''' </summary>
Public Class ExchangeControlSessionTests

    Private Shared ReadOnly DeviceId As String = ExchangeWireTestKit.IdOf("D"c)

    ''' <summary>A hook that records what it saw and can send something between `welcome` and `list`.</summary>
    Private NotInheritable Class Hooks
        Implements IExchangeSessionHooks

        Public Welcomes As Integer
        Public ReadOnly Messages As New ConcurrentQueue(Of ExchangeEnvelope)
        Public SendOnWelcome As Boolean

        Public Async Function OnWelcomeAsync(session As ExchangeControlSession, ct As CancellationToken) As Task Implements IExchangeSessionHooks.OnWelcomeAsync
            Interlocked.Increment(Welcomes)
            If SendOnWelcome Then Await session.SendAsync(ExchangeMessages.Unpublish("r-republish"), ct)
        End Function

        Public Sub OnMessage(session As ExchangeControlSession, message As ExchangeEnvelope) Implements IExchangeSessionHooks.OnMessage
            Messages.Enqueue(message)
        End Sub
    End Class

    ''' <summary>Waits and records: a wait of a second or more (a backoff, a retry after a refusal) returns at once and is noted.</summary>
    Private NotInheritable Class Waits
        Public ReadOnly Recorded As New ConcurrentQueue(Of TimeSpan)

        Public Function Delay(t As TimeSpan, ct As CancellationToken) As Task
            If t >= TimeSpan.FromSeconds(1) Then
                Recorded.Enqueue(t)
                Return Task.Delay(5, ct)
            End If
            Return Task.Delay(t, ct)
        End Function

        Public Function Seconds() As Double()
            Return Recorded.Select(Function(t) t.TotalSeconds).ToArray()
        End Function
    End Class

    Private Shared Function MakeOptions(acc As ExchangeStubAccount, token As String, waits As Waits, Optional pin As String = Nothing,
                                        Optional unitMs As Integer = 50) As ExchangeControlOptions
        Dim address As ExchangeServerAddress = Nothing
        Assert.True(ExchangeServerAddress.TryParse(acc.Address, address))
        Return New ExchangeControlOptions With {
            .Server = address,
            .Pin = If(pin, acc.Fingerprint),
            .Credentials = New ExchangeDeviceCredentials(DeviceId, token),
            .ProductVersion = "26.10.7.1",
            .KeepaliveUnitMs = unitMs,
            .Backoff = Function(attempt) TimeSpan.FromSeconds(ExchangeBackoff.BaseSeconds(attempt)),
            .Delay = AddressOf waits.Delay}
    End Function

    Private Shared Async Function WaitStateAsync(session As ExchangeControlSession, state As ExchangeAgentState, Optional seconds As Double = 10) As Task(Of Boolean)
        Return Await ExchangeStubAccount.WaitUntilAsync(Function() session.State = state, seconds)
    End Function

    ''' <summary>Starts a session and returns its task; the caller cancels and awaits it.</summary>
    Private Shared Function Run(session As ExchangeControlSession, cts As CancellationTokenSource) As Task
        Return Task.Run(Function() session.RunAsync(cts.Token))
    End Function

    ' ---------------------------------------------------------------- the opening

    <Fact>
    Public Async Function Hello_then_the_republish_hook_then_list_then_subscribe_and_the_state_is_Online() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim hooks As New Hooks() With {.SendOnWelcome = True}
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits()), hooks)
            acc.Devices.Add("{""deviceId"":""PH"",""deviceName"":""Phone"",""product"":""fms-android"",""presence"":""online""}")
            acc.Resources.Add("{""resourceId"":""R1"",""deviceId"":""PH"",""kind"":""sftp-share"",""name"":""Photos"",""access"":{""descriptor"":""FMSSFTP2.secret""}}")
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await WaitStateAsync(session, ExchangeAgentState.Online))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.HasSnapshot))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.ReceivedOn(1).Contains("subscribe")))
                Assert.Equal({"hello", "unpublish", "list", "subscribe"}, acc.ReceivedOn(1).Take(4).ToArray())   ' items G and H: publish again, then list, then subscribe
                Assert.Equal(1, hooks.Welcomes)
                Assert.Equal(7L, session.Model.Revision)
                Assert.Equal("Photos", session.Model.Snapshot().Resources.Single().Name)
                Assert.Equal({"directory", "tunnel", "pairing"}, session.Features.ToArray())
                Assert.Equal("203.0.113.5:22022", session.PublicEndpoint)
                cts.Cancel()
                Await task
            End Using
            Assert.Equal(ExchangeAgentState.Stopped, session.State)
        End Using
    End Function

    <Fact>
    Public Async Function A_change_with_the_next_revision_applies_and_a_gap_sends_list_again() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits()), New Hooks())
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.HasSnapshot))
                Dim up As New ExchangeRecordSets()
                up.Devices.Add("{""deviceId"":""PH"",""deviceName"":""Phone"",""presence"":""online""}")
                Await acc.PushAsync(New ExchangeEnvelope("changed").SetNumber("revision", 8).SetUpserts(up).SetRemovals(New ExchangeIdSets()))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.Revision = 8))
                Assert.Single(session.Model.Snapshot().Devices)
                Assert.Equal(1, acc.CountOf("list"))

                acc.Revision = 10
                Await acc.PushAsync(New ExchangeEnvelope("changed").SetNumber("revision", 10).SetUpserts(New ExchangeRecordSets()).SetRemovals(New ExchangeIdSets()))   ' 9 never arrived
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("list") = 2), "a gap in the revisions must send `list` again")
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.Revision = 10))
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_unknown_type_from_the_server_is_ignored_and_the_stream_goes_on() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim hooks As New Hooks()
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits()), hooks)
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.HasSnapshot))
                Await acc.PushBodyAsync("{""schemaVersion"":2,""type"":""from-the-future"",""x"":1}")
                Await acc.PushAsync(New ExchangeEnvelope("changed").SetNumber("revision", 8).SetUpserts(New ExchangeRecordSets()).SetRemovals(New ExchangeIdSets()))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.Revision = 8))
                Assert.Equal(ExchangeAgentState.Online, session.State)
                Assert.Empty(hooks.Messages)   ' an unknown type is not even handed to the hooks
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_known_envelope_the_session_does_not_handle_is_given_to_the_hooks() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim hooks As New Hooks()
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits()), hooks)
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.HasSnapshot))
                Await acc.PushAsync(New ExchangeEnvelope("open").SetText("tunnelId", ExchangeWireTestKit.IdOf("N"c)).SetText("resourceId", ExchangeWireTestKit.IdOf("R"c)))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() hooks.Messages.Count = 1))
                Dim m As ExchangeEnvelope = Nothing
                hooks.Messages.TryPeek(m)
                Assert.True(m.Is("open"))
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- keepalive

    <Fact>
    Public Async Function A_keepalive_is_sent_at_the_accepted_interval_and_the_echo_keeps_the_stream_alive() As Task
        Using acc As New ExchangeStubAccount()
            acc.KeepaliveSeconds = 1
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits(), unitMs:=60), New Hooks())
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("keepalive") >= 4, 10))
                Assert.Equal(ExchangeAgentState.Online, session.State)
                Assert.Equal(1, acc.Received().Where(Function(r) r = "1:hello").Count())   ' one connection the whole time: the echoes were seen
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Three_intervals_without_an_echo_make_a_dead_stream_and_the_session_reconnects() As Task
        Using acc As New ExchangeStubAccount()
            acc.KeepaliveSeconds = 1
            acc.EchoKeepalive = False
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim waits As New Waits()
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, waits, unitMs:=60), New Hooks())
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                ' the stub says nothing back: after three intervals the client drops the stream and says hello again on a second connection
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.Received().Contains("2:hello"), 15), "no reconnect after a silent stream")
                Assert.Contains(1.0, waits.Seconds())   ' and it waited the first step of the backoff, not nothing
                acc.EchoKeepalive = True
                Assert.True(Await WaitStateAsync(session, ExchangeAgentState.Online))
                Dim hellos As Integer = acc.Received().Where(Function(r) r.EndsWith(":hello")).Count()
                Await Task.Delay(600)
                Assert.Equal(hellos, acc.Received().Where(Function(r) r.EndsWith(":hello")).Count())   ' stable again: echoes are back
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- the refusal table

    <Theory>
    <InlineData("bad-credentials", "SignInAgain", False, 0)>
    <InlineData("device-revoked", "SignInAgain", True, 0)>
    <InlineData("signed-out", "SignInAgain", False, 0)>
    <InlineData("version-unsupported", "UpdateNeeded", False, 0)>
    <InlineData("tls-required", "Defect", False, 0)>
    Public Async Function A_refusal_that_is_not_retried_stops_the_session_with_a_named_state(reason As String, stateName As String, revoked As Boolean, retryAfter As Integer) As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            acc.RefuseNextHello(reason)
            Dim waits As New Waits()
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, waits), New Hooks())
            Dim revokedRaised As Integer = 0
            AddHandler session.DeviceRevoked, Sub(s, e) Interlocked.Increment(revokedRaised)
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Dim finished As Task = Await Task.WhenAny(task, Task.Delay(10000))
                Assert.Same(task, finished)   ' the session ended by itself: nothing retried
                Assert.Equal(DirectCast([Enum].Parse(GetType(ExchangeAgentState), stateName), ExchangeAgentState), session.State)
                Assert.Equal(reason, session.LastReason)
                Assert.Equal(If(revoked, 1, 0), revokedRaised)
                Await Task.Delay(300)
                Assert.Equal(1, acc.CountOf("hello"))   ' not retried
                Assert.Empty(waits.Seconds())
            End Using
        End Using
    End Function

    <Theory>
    <InlineData("rate-limited", 60.0)>
    <InlineData("capacity", 120.0)>
    <InlineData("id-collision", 60.0)>
    <InlineData("port-unavailable", 60.0)>
    <InlineData("invitation-invalid", 60.0)>
    <InlineData("login-taken", 60.0)>
    <InlineData("password-policy", 60.0)>
    <InlineData("a-reason-from-the-future", 60.0)>
    Public Async Function A_refusal_that_is_retried_waits_by_its_policy_and_then_connects(reason As String, seconds As Double) As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            acc.RefuseNextHello(reason)
            Dim waits As New Waits()
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, waits), New Hooks())
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await WaitStateAsync(session, ExchangeAgentState.Online))
                Assert.Equal({seconds}, waits.Seconds())   ' one wait, of exactly the policy's length
                Assert.Equal(2, acc.CountOf("hello"))
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- the certificate

    <Fact>
    Public Async Function A_pin_that_does_not_match_stops_the_session_with_a_named_state_and_never_re_pins() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim wrong As String = ExchangeTrust.FingerprintOf(New Byte() {1, 2, 3})
            Dim waits As New Waits()
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, waits, wrong), New Hooks())
            Using cts As New CancellationTokenSource()
                Dim finished As Task = Run(session, cts)
                Assert.Same(finished, Await Task.WhenAny(finished, Task.Delay(10000)))
                Assert.Equal(ExchangeAgentState.PinMismatch, session.State)
                Assert.Equal("pin-mismatch", session.LastReason)
                Assert.Empty(waits.Seconds())   ' no retry
                Assert.Equal(0, acc.CountOf("hello"))   ' and not one envelope reached the server
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_certificate_replaced_while_connected_is_refused_on_the_reconnect_and_the_pin_stays_the_old_one() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim waits As New Waits()
            Dim options As ExchangeControlOptions = MakeOptions(acc, token, waits)
            Dim oldPin As String = options.Pin
            Dim session As New ExchangeControlSession(options, New Hooks())
            Using cts As New CancellationTokenSource()
                Dim finished As Task = Run(session, cts)
                Assert.True(Await WaitStateAsync(session, ExchangeAgentState.Online))
                acc.Server.Certificate = ExchangeWireTestKit.NewSelfSignedCertificate()
                acc.DropAll()
                Assert.True(Await WaitStateAsync(session, ExchangeAgentState.PinMismatch))
                Assert.Same(finished, Await Task.WhenAny(finished, Task.Delay(10000)))
                Assert.Equal(oldPin, options.Pin)   ' never re-pinned by the session
                Assert.Equal(1, acc.CountOf("hello"))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_self_signed_certificate_with_no_pin_is_a_stop_not_a_silent_acceptance() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits(), ""), New Hooks())
            Using cts As New CancellationTokenSource()
                Dim finished As Task = Run(session, cts)
                Assert.Same(finished, Await Task.WhenAny(finished, Task.Delay(10000)))
                Assert.Equal(ExchangeAgentState.PinMismatch, session.State)
                Assert.Equal("untrusted", session.LastReason)
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- the backoff

    <Fact>
    Public Async Function A_server_that_cannot_be_reached_is_retried_on_the_bounded_backoff_1_2_4_8_16_30_30() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim waits As New Waits()
            Dim options As ExchangeControlOptions = MakeOptions(acc, token, waits)
            options.Server = New ExchangeServerAddress("127.0.0.1", 1)   ' nothing listens there
            Dim session As New ExchangeControlSession(options, New Hooks())
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() waits.Recorded.Count >= 7, 20))
                cts.Cancel()
                Await task
                Assert.Equal({1.0, 2.0, 4.0, 8.0, 16.0, 30.0, 30.0}, waits.Seconds().Take(7).ToArray())
            End Using
        End Using
    End Function

    ''' <summary>A transport that fails the first N attempts with a network error, then connects for real.</summary>
    Private NotInheritable Class FlakyTransport
        Implements IExchangeTransport

        Private _failures As Integer

        Public Sub New(failures As Integer)
            _failures = failures
        End Sub

        Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
            If Interlocked.Decrement(_failures) >= 0 Then
                Throw New ExchangeConnectException(ExchangeConnectFailure.Network, "", "down")
            End If
            Return ExchangeTlsTransport.Instance.ConnectAsync(host, port, pin, ct)
        End Function
    End Class

    <Fact>
    Public Async Function A_welcome_resets_the_backoff() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim waits As New Waits()
            Dim options As ExchangeControlOptions = MakeOptions(acc, token, waits)
            options.Transport = New FlakyTransport(2)
            Dim session As New ExchangeControlSession(options, New Hooks())
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await WaitStateAsync(session, ExchangeAgentState.Online))
                Assert.Equal({1.0, 2.0}, waits.Seconds())   ' two failures, two steps
                acc.DropAll()
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.Received().Contains("2:hello"), 10))
                Assert.True(Await WaitStateAsync(session, ExchangeAgentState.Online))
                Assert.Equal({1.0, 2.0, 1.0}, waits.Seconds())   ' after the welcome the first step again, not the third
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- requests of the agent

    <Fact>
    Public Async Function A_pairing_code_is_asked_for_over_the_control_stream_and_a_refusal_is_named() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits()), New Hooks())
            ' no stream yet: nothing was asked, and the answer says so
            Dim none As ExchangePairingResult = Await session.RequestPairingCodeAsync(CancellationToken.None)
            Assert.False(none.Answered)
            Assert.False(none.Succeeded)
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.HasSnapshot))
                Dim ok As ExchangePairingResult = Await session.RequestPairingCodeAsync(CancellationToken.None)
                Assert.True(ok.Succeeded)
                Assert.True(ok.Answered)
                Assert.Equal(ExchangeStubAccount.PairingCode, ok.Code)
                Assert.Equal("2099-01-01T00:10:00Z", ok.ExpiresAt)
                Assert.DoesNotContain(ExchangeStubAccount.PairingCode, ok.ToString())   ' a code is a secret until used

                acc.PairingRefusal = "rate-limited"
                Dim refused As ExchangePairingResult = Await session.RequestPairingCodeAsync(CancellationToken.None)
                Assert.False(refused.Succeeded)
                Assert.True(refused.Answered)
                Assert.Equal(ExchangeRefusal.RateLimited, refused.Refusal)
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Revoke_and_bye_are_sent_as_envelopes_with_no_answer() As Task
        Using acc As New ExchangeStubAccount()
            Dim token As String = acc.PreEnroll(DeviceId)
            Dim session As New ExchangeControlSession(MakeOptions(acc, token, New Waits()), New Hooks())
            Using cts As New CancellationTokenSource()
                Dim task As Task = Run(session, cts)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.HasSnapshot))
                Assert.True(Await session.RevokeAsync(ExchangeWireTestKit.IdOf("X"c), CancellationToken.None))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("revoke") = 1))
                Assert.Contains(ExchangeWireTestKit.IdOf("X"c), acc.RevokedDevices())
                Await session.SayByeAsync()
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("bye") = 1))
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Without_a_stream_a_send_is_false_not_an_exception() As Task
        Using acc As New ExchangeStubAccount()
            Dim session As New ExchangeControlSession(MakeOptions(acc, ExchangeWireTestKit.TokenOf("T"c), New Waits()), New Hooks())
            Assert.False(Await session.SendAsync(ExchangeMessages.Simple("list"), CancellationToken.None))
            Assert.False(Await session.RevokeAsync(DeviceId, CancellationToken.None))
            Await session.SayByeAsync()
        End Using
    End Function

    ' ---------------------------------------------------------------- adopting the enrolling stream

    <Fact>
    Public Async Function The_stream_that_enrolled_becomes_the_control_stream_with_no_second_connection() As Task
        Using acc As New ExchangeStubAccount()
            Dim description As New ExchangeDeviceDescription(DeviceId, "PC", ExchangeWire.ProductWindows, "1", "windows 10.0.1", Array.Empty(Of String)(), "")
            Dim address As ExchangeServerAddress = Nothing
            Assert.True(ExchangeServerAddress.TryParse(acc.Address, address))
            Using cts As New CancellationTokenSource()
                Dim opened As ExchangeOpenResult = Await ExchangeSessions.EnrollAsync(ExchangeTlsTransport.Instance, address, acc.Fingerprint,
                    ExchangeMessages.Enroll(ExchangeStubAccount.Login, ExchangeStubAccount.Password, "", description, 30), cts.Token)
                Assert.Equal(ExchangeOpenOutcome.Connected, opened.Outcome)
                Dim token As String = opened.Welcome.Text("deviceToken")
                Dim options As ExchangeControlOptions = MakeOptions(acc, token, New Waits())
                Dim session As New ExchangeControlSession(options, New Hooks())
                Dim task As Task = Task.Run(Function() session.RunAsync(cts.Token, opened))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.Model.HasSnapshot))
                Assert.Equal(ExchangeAgentState.Online, session.State)
                Assert.Equal({"enroll", "list", "subscribe"}, acc.ReceivedOn(1).Take(3).ToArray())
                Assert.Equal(1, acc.Server.Accepted)   ' one connection in all
                cts.Cancel()
                Await task
            End Using
        End Using
    End Function

End Class
