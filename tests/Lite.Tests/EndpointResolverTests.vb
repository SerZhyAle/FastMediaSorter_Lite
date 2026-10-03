#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 100 §15.3 / §15.5 item 4 (T07, the SSH-library-free half): candidate planning, the
''' two-lane race with its 5 s / 45 s budgets, the host-key gate (pin, TOFU, persist-before-
''' accept) and the reconnect schedule - all against a manual clock, so every budget is asserted
''' at its boundary without a real wait and without a network.
''' </summary>
Public Class EndpointResolverTests

    ' ------------------------------------------------------------------ doubles ----

    Private NotInheritable Class ManualClock
        Implements IResolutionClock

        Private ReadOnly timers As New List(Of (Due As Long, Source As TaskCompletionSource(Of Boolean)))()
        Private ReadOnly gate As New Object()
        Private now As Long

        Public ReadOnly Property NowMs As Long Implements IResolutionClock.NowMs
            Get
                SyncLock gate
                    Return now
                End SyncLock
            End Get
        End Property

        Friend ReadOnly Property Pending As Integer
            Get
                SyncLock gate
                    Return timers.Count
                End SyncLock
            End Get
        End Property

        Public Function Delay(ms As Integer, ct As CancellationToken) As Task Implements IResolutionClock.Delay
            Dim source As New TaskCompletionSource(Of Boolean)()
            If ct.IsCancellationRequested Then
                source.TrySetCanceled(ct)
                Return source.Task
            End If
            SyncLock gate
                timers.Add((now + ms, source))
            End SyncLock
            ct.Register(Sub()
                            SyncLock gate
                                timers.RemoveAll(Function(t) ReferenceEquals(t.Source, source))
                            End SyncLock
                            source.TrySetCanceled(ct)
                        End Sub)
            Return source.Task
        End Function

        ''' <summary>Moves time forward, firing every timer that falls due on the way, in order.</summary>
        Friend Sub Advance(ms As Long)
            Dim target As Long
            SyncLock gate
                target = now + ms
            End SyncLock
            Do
                Dim fire As (Due As Long, Source As TaskCompletionSource(Of Boolean))? = Nothing
                SyncLock gate
                    Dim due = timers.Where(Function(t) t.Due <= target).OrderBy(Function(t) t.Due).ToList()
                    If due.Count > 0 Then
                        fire = due(0)
                        timers.Remove(due(0))
                        now = Math.Max(now, due(0).Due)
                    Else
                        now = target
                    End If
                End SyncLock
                If fire Is Nothing Then Return
                fire.Value.Source.TrySetResult(True)
            Loop
        End Sub

        ''' <summary>Advances to the earliest pending timer. False if there is none.</summary>
        Friend Function AdvanceToNext() As Boolean
            Dim wait As Long
            SyncLock gate
                If timers.Count = 0 Then Return False
                wait = Math.Max(0L, timers.Min(Function(t) t.Due) - now)
            End SyncLock
            Advance(wait)
            Return True
        End Function
    End Class

    Private NotInheritable Class FakeSession
        Implements IDisposable
        Friend Disposed As Boolean
        Public Sub Dispose() Implements IDisposable.Dispose
            Disposed = True
        End Sub
    End Class

    ''' <summary>Every dial is a pending task the test settles by host name.</summary>
    Private NotInheritable Class ScriptedDialer
        Implements IEndpointDialer

        Friend ReadOnly Pending As New Dictionary(Of String, TaskCompletionSource(Of DialResult))()
        Friend ReadOnly Tokens As New Dictionary(Of String, CancellationToken)()
        Friend ReadOnly Started As New List(Of String)()
        Friend ThrowFor As String = ""
        Private ReadOnly gate As New Object()
        Private active As Integer
        Friend PeakActive As Integer

        Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
            If endpoint.Host = ThrowFor Then Throw New InvalidOperationException("dialer exploded")
            Dim source As New TaskCompletionSource(Of DialResult)()
            SyncLock gate
                Started.Add(endpoint.Host)
                Pending(endpoint.Host) = source
                Tokens(endpoint.Host) = ct
                active += 1
                PeakActive = Math.Max(PeakActive, active)
            End SyncLock
            source.Task.ContinueWith(
                Sub()
                    SyncLock gate
                        active -= 1
                    End SyncLock
                End Sub, TaskScheduler.Default)
            Return source.Task
        End Function

        Friend Function Session(host As String) As FakeSession
            Dim s As New FakeSession()
            Pending(host).TrySetResult(DialResult.Verified(s))
            Return s
        End Function

        Friend Sub Fail(host As String, kind As ResourceFailureKind)
            Pending(host).TrySetResult(DialResult.Failed(New ResourceFailure(kind)))
        End Sub
    End Class

    Private Shared Function Eps(ParamArray hosts As String()) As List(Of FmsEndpoint)
        Return hosts.Select(Function(h) New FmsEndpoint With {.Kind = If(h.StartsWith("lan"), "lan", "portforward"), .Host = h, .Port = 22}).ToList()
    End Function

    Private Shared Function Resolve(candidates As List(Of FmsEndpoint), dialer As ScriptedDialer, clock As ManualClock,
                                    Optional ct As CancellationToken = Nothing, Optional lanes As Integer = 2,
                                    Optional consent As Func(Of Long) = Nothing) As Task(Of ResolutionOutcome)
        Return EndpointResolver.ResolveAsync(candidates, dialer, clock, ct, lanes, consent)
    End Function

    Private Shared Sub WaitStarted(dialer As ScriptedDialer, count As Integer)
        Assert.True(SpinWait.SpinUntil(Function() dialer.Started.Count >= count, 2000), "the dial did not start")
    End Sub

    Private Shared Function Done(Of T)(task As Task(Of T)) As T
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "the task did not finish")
        Return task.Result
    End Function

    ' --------------------------------------------------------------------- plan ----

    <Fact>
    Public Sub Planning_deduplicates_by_host_and_port_ignoring_case_and_keeps_producer_order()
        Dim stored As List(Of FmsEndpoint) = Eps("lan.h", "Pf.example", "pf.EXAMPLE", "other")
        Dim plan As List(Of FmsEndpoint) = EndpointResolver.PlanCandidates(stored)
        Assert.Equal({"lan.h", "Pf.example", "other"}, plan.Select(Function(e) e.Host).ToArray())
        ' The same host on another port is another candidate.
        stored.Add(New FmsEndpoint With {.Kind = "portforward", .Host = "pf.example", .Port = 2022})
        Assert.Equal(4, EndpointResolver.PlanCandidates(stored).Count)
    End Sub

    <Fact>
    Public Sub A_discovered_candidate_goes_first_unless_it_is_already_stored_and_never_drops_a_stored_path()
        Dim found As New FmsEndpoint With {.Kind = "lan", .Host = "192.168.1.50", .Port = 22}
        Dim plan = EndpointResolver.PlanCandidates(Eps("lan.old", "pf"), found)
        Assert.Equal({"192.168.1.50", "lan.old", "pf"}, plan.Select(Function(e) e.Host).ToArray())

        Dim duplicate As New FmsEndpoint With {.Kind = "lan", .Host = "LAN.OLD", .Port = 22}
        Assert.Equal({"lan.old", "pf"}, EndpointResolver.PlanCandidates(Eps("lan.old", "pf"), duplicate).Select(Function(e) e.Host).ToArray())
        Assert.Equal(2, EndpointResolver.PlanCandidates(Eps("lan.old", "pf"), Nothing).Count)
    End Sub

    <Fact>
    Public Sub With_sixteen_stored_paths_discovery_replaces_the_one_stale_lan_entry_or_is_omitted()
        Dim found As New FmsEndpoint With {.Kind = "lan", .Host = "new.lan", .Port = 22}

        Dim oneLan As List(Of FmsEndpoint) = Eps(Enumerable.Range(1, 15).Select(Function(i) "pf" & i).Concat({"lan.stale"}).ToArray())
        Dim replaced = EndpointResolver.PlanCandidates(oneLan, found)
        Assert.Equal(16, replaced.Count)
        Assert.Equal("new.lan", replaced(15).Host)
        Assert.DoesNotContain(replaced, Function(e) e.Host = "lan.stale")

        Dim twoLan As List(Of FmsEndpoint) = Eps(Enumerable.Range(1, 14).Select(Function(i) "pf" & i).Concat({"lan.a", "lan.b"}).ToArray())
        Dim omitted = EndpointResolver.PlanCandidates(twoLan, found)
        Assert.Equal(16, omitted.Count)
        Assert.DoesNotContain(omitted, Function(e) e.Host = "new.lan")

        ' More than sixteen stored is cut to the budget.
        Assert.Equal(16, EndpointResolver.PlanCandidates(Eps(Enumerable.Range(1, 20).Select(Function(i) "h" & i).ToArray())).Count)
    End Sub

    ' ---------------------------------------------------------------------- race ----

    <Fact>
    Public Sub At_most_two_dials_run_at_once_and_candidates_start_in_producer_order()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim task = Resolve(Eps("a", "b", "c", "d"), dialer, clock)

        Assert.Equal({"a", "b"}, dialer.Started.ToArray())
        dialer.Fail("a", ResourceFailureKind.Transport)
        WaitStarted(dialer, 3)
        Assert.Equal({"a", "b", "c"}, dialer.Started.ToArray())
        dialer.Fail("b", ResourceFailureKind.Transport)
        WaitStarted(dialer, 4)
        Assert.Equal({"a", "b", "c", "d"}, dialer.Started.ToArray())
        Dim winner = dialer.Session("d")
        Dim outcome = Done(task)

        Assert.True(outcome.Succeeded)
        Assert.Same(winner, outcome.Session)
        Assert.Equal("d", outcome.Endpoint.Host)
        Assert.True(dialer.PeakActive <= 2)
    End Sub

    <Fact>
    Public Sub The_first_verified_session_wins_and_the_loser_is_cancelled_and_its_late_session_is_closed()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim task = Resolve(Eps("a", "b"), dialer, clock)

        Dim winner = dialer.Session("b")
        Dim outcome = Done(task)
        Assert.Same(winner, outcome.Session)
        Assert.True(dialer.Tokens("a").IsCancellationRequested) ' the loser was told to stop

        Dim late = dialer.Session("a") ' ... and finished anyway
        SpinWait.SpinUntil(Function() late.Disposed, 2000)
        Assert.True(late.Disposed)
        Assert.False(winner.Disposed)
    End Sub

    <Fact>
    Public Sub A_host_key_mismatch_ends_the_whole_round_and_nothing_else_is_dialled()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim task = Resolve(Eps("a", "b", "c", "d"), dialer, clock)
        dialer.Fail("a", ResourceFailureKind.KeyMismatch)
        Dim outcome = Done(task)

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.KeyMismatch, outcome.Failure.Kind)
        Assert.DoesNotContain("c", dialer.Started)
        Assert.DoesNotContain("d", dialer.Started)
        Assert.True(dialer.Tokens("b").IsCancellationRequested)
    End Sub

    <Fact>
    Public Sub An_auth_refusal_is_terminal_too_while_a_plain_transport_failure_is_not()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim task = Resolve(Eps("a", "b", "c"), dialer, clock, lanes:=1)
        dialer.Fail("a", ResourceFailureKind.Transport)
        WaitStarted(dialer, 2)
        Assert.Equal({"a", "b"}, dialer.Started.ToArray())
        dialer.Fail("b", ResourceFailureKind.Auth)
        Dim outcome = Done(task)
        Assert.Equal(ResourceFailureKind.Auth, outcome.Failure.Kind)
        Assert.DoesNotContain("c", dialer.Started)
    End Sub

    <Fact>
    Public Sub When_every_candidate_fails_the_last_failure_is_reported_with_every_attempt_listed()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim task = Resolve(Eps("a", "b"), dialer, clock)
        dialer.Fail("a", ResourceFailureKind.Transport)
        dialer.Fail("b", ResourceFailureKind.Timeout)
        Dim outcome = Done(task)
        Assert.False(outcome.Succeeded)
        Assert.Equal(2, outcome.Attempts.Count)
        Assert.All(outcome.Attempts, Sub(a) Assert.NotNull(a.Failure))
        Assert.True(outcome.Failure.IsRetryable)
    End Sub

    <Fact>
    Public Sub No_candidates_is_a_failure_not_a_hang()
        Dim outcome = Done(Resolve(New List(Of FmsEndpoint)(), New ScriptedDialer(), New ManualClock()))
        Assert.False(outcome.Succeeded)
        Assert.Empty(outcome.Attempts)
    End Sub

    <Fact>
    Public Sub A_dialer_that_throws_is_one_failed_candidate_and_the_next_is_tried()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer() With {.ThrowFor = "a"}
        Dim task = Resolve(Eps("a", "b"), dialer, clock, lanes:=1)
        Dim winner = dialer.Session("b")
        Dim outcome = Done(task)
        Assert.Same(winner, outcome.Session)
        Assert.Equal(2, outcome.Attempts.Count)
    End Sub

    <Fact>
    Public Sub A_candidate_gets_five_seconds_boundary_ok_and_one_millisecond_more_is_a_timeout()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim task = Resolve(Eps("a", "b"), dialer, clock, lanes:=1)

        clock.Advance(ResolutionLimits.CandidateMs - 1)
        Assert.False(dialer.Tokens("a").IsCancellationRequested)
        Assert.Equal({"a"}, dialer.Started.ToArray())

        clock.Advance(1)
        Assert.True(SpinWait.SpinUntil(Function() dialer.Tokens("a").IsCancellationRequested, 2000))
        Assert.False(task.IsCompleted)
        ' ... but a call that ignores cancellation still owns the lane: no replacement dial yet.
        Assert.Equal({"a"}, dialer.Started.ToArray())

        Dim lateSession As FakeSession = dialer.Session("a") ' it returns at last, verified - too late
        WaitStarted(dialer, 2)
        Assert.Equal({"a", "b"}, dialer.Started.ToArray())  ' the lane is free again
        SpinWait.SpinUntil(Function() lateSession.Disposed, 2000)
        Assert.True(lateSession.Disposed)                    ' the late result was closed, never used

        Dim winner = dialer.Session("b")
        Dim outcome = Done(task)
        Assert.Same(winner, outcome.Session)
        Assert.Equal(ResourceFailureKind.Timeout, outcome.Attempts(0).Failure.Kind)
    End Sub

    <Fact>
    Public Sub Hung_dials_cannot_cause_replacements_and_the_round_ends_at_forty_five_seconds()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim task = Resolve(Eps("a", "b", "c", "d"), dialer, clock)

        clock.Advance(ResolutionLimits.CandidateMs) ' both lanes' dials time out, neither returns
        Assert.Equal({"a", "b"}, dialer.Started.ToArray())
        Assert.False(task.IsCompleted)

        clock.Advance(ResolutionLimits.RoundMs - ResolutionLimits.CandidateMs - 1)
        Assert.False(task.IsCompleted)
        clock.Advance(1)
        Dim outcome = Done(task)
        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.Timeout, outcome.Failure.Kind)
        Assert.Equal({"a", "b"}, dialer.Started.ToArray()) ' c and d were never launched

        Dim s1 As FakeSession = dialer.Session("a")
        Dim s2 As FakeSession = dialer.Session("b")
        SpinWait.SpinUntil(Function() s1.Disposed AndAlso s2.Disposed, 2000)
        Assert.True(s1.Disposed AndAlso s2.Disposed)
    End Sub

    <Fact>
    Public Sub Time_spent_on_a_trust_prompt_is_not_counted_against_either_budget()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Dim consent As Long = 0
        Dim task = Resolve(Eps("a"), dialer, clock, lanes:=1, consent:=Function() Interlocked.Read(consent))

        ' The user takes 100 s over the prompt: the clock moves, and so does the consent total.
        For i As Integer = 1 To 20
            Interlocked.Add(consent, 5000)
            clock.Advance(5000)
        Next
        Assert.False(dialer.Tokens("a").IsCancellationRequested)
        Assert.False(task.IsCompleted)

        Dim winner = dialer.Session("a")
        Assert.Same(winner, Done(task).Session)
    End Sub

    <Fact>
    Public Sub The_users_cancel_ends_the_round_cancels_the_dials_and_closes_late_sessions()
        Dim clock As New ManualClock()
        Dim dialer As New ScriptedDialer()
        Using cts As New CancellationTokenSource()
            Dim task = Resolve(Eps("a", "b"), dialer, clock, cts.Token)
            cts.Cancel()
            Dim outcome = Done(task)
            Assert.Equal(ResourceFailureKind.Cancelled, outcome.Failure.Kind)
            Assert.True(dialer.Tokens("a").IsCancellationRequested)
            Dim late = dialer.Session("a")
            SpinWait.SpinUntil(Function() late.Disposed, 2000)
            Assert.True(late.Disposed)
        End Using
    End Sub

    ' ------------------------------------------------------------------ host key ----

    Private Shared Function Blob(seed As Integer) As Byte()
        Return Enumerable.Range(0, 51).Select(Function(i) CByte((i * 7 + seed) Mod 251)).ToArray()
    End Function

    Private NotInheritable Class GateProbe
        Friend ConsentCalls As Integer
        Friend PersistCalls As New List(Of String)()
        Friend Accept As Boolean = True
        Friend PersistResult As Boolean = True
        Friend PersistThrows As Boolean
        Friend ConsentThrowsCancel As Boolean
        Friend Now As Long
        Friend OnConsent As Action
        Friend Function Gate(pin As String) As HostKeyGate
            Return New HostKeyGate(pin,
                Function(fp As String, ct As CancellationToken) As Boolean
                    Interlocked.Increment(ConsentCalls)
                    OnConsent?.Invoke()
                    If ConsentThrowsCancel Then Throw New OperationCanceledException()
                    Return Accept
                End Function,
                Function(fp As String) As Boolean
                    SyncLock PersistCalls
                        PersistCalls.Add(fp)
                    End SyncLock
                    If PersistThrows Then Throw New System.IO.IOException("disk")
                    Return PersistResult
                End Function,
                Function() Interlocked.Read(Now))
        End Function
    End Class

    <Fact>
    Public Sub A_pinned_server_matches_silently_and_a_different_key_is_a_mismatch_with_both_hashes()
        Dim probe As New GateProbe()
        Dim pin As String = SshFingerprint.OfHostKey(Blob(1))
        Dim gate = probe.Gate(pin)

        Assert.True(gate.Verify(Blob(1), CancellationToken.None).IsAccepted)
        Dim wrong = gate.Verify(Blob(2), CancellationToken.None)
        Assert.Equal(HostKeyVerdict.Mismatch, wrong.Verdict)
        Assert.Equal(pin, wrong.Expected)
        Assert.Equal(SshFingerprint.OfHostKey(Blob(2)), wrong.Received)
        Assert.Equal(ResourceFailureKind.KeyMismatch, wrong.ToFailure().Kind)
        Assert.Equal(0, probe.ConsentCalls)  ' a pinned server never prompts
        Assert.Empty(probe.PersistCalls)
    End Sub

    <Fact>
    Public Sub An_unusable_pin_or_an_empty_key_fails_closed()
        Dim probe As New GateProbe()
        Assert.Equal(HostKeyVerdict.Malformed, probe.Gate("SHA256:short").Verify(Blob(1), CancellationToken.None).Verdict)
        Assert.Equal(HostKeyVerdict.Malformed, probe.Gate("").Verify(Array.Empty(Of Byte)(), CancellationToken.None).Verdict)
        Assert.Equal(0, probe.ConsentCalls)
    End Sub

    <Fact>
    Public Sub An_unpinned_server_declined_stays_unpinned_and_authentication_never_starts()
        Dim probe As New GateProbe() With {.Accept = False}
        Dim gate = probe.Gate("")
        Dim decision = gate.Verify(Blob(1), CancellationToken.None)
        Assert.Equal(HostKeyVerdict.ConsentDeclined, decision.Verdict)
        Assert.False(decision.IsAccepted)
        Assert.Equal("", gate.Pin)
        Assert.Empty(probe.PersistCalls)
        Assert.Equal(ResourceFailureKind.Cancelled, decision.ToFailure().Kind)
    End Sub

    <Fact>
    Public Sub An_accepted_pin_is_persisted_before_it_is_accepted_and_binds_every_later_endpoint()
        Dim probe As New GateProbe()
        Dim gate = probe.Gate("")
        Dim offered As String = SshFingerprint.OfHostKey(Blob(1))
        probe.OnConsent = Sub() Assert.Empty(probe.PersistCalls) ' nothing stored while the user is still deciding

        Assert.True(gate.Verify(Blob(1), CancellationToken.None).IsAccepted)
        Assert.Equal({offered}, probe.PersistCalls.ToArray())
        Assert.Equal(offered, gate.Pin)

        ' The next endpoint of the same server is matched strictly: no second prompt, no second pin.
        Assert.True(gate.Verify(Blob(1), CancellationToken.None).IsAccepted)
        Assert.Equal(HostKeyVerdict.Mismatch, gate.Verify(Blob(2), CancellationToken.None).Verdict)
        Assert.Equal(1, probe.ConsentCalls)
        Assert.Single(probe.PersistCalls)
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub A_pin_that_cannot_be_stored_blocks_authentication_and_never_falls_back_to_accept_any(throws As Boolean)
        Dim probe As New GateProbe() With {.PersistResult = False, .PersistThrows = throws}
        Dim gate = probe.Gate("")
        Dim decision = gate.Verify(Blob(1), CancellationToken.None)
        Assert.Equal(HostKeyVerdict.PersistFailed, decision.Verdict)
        Assert.Equal("", gate.Pin)
        ' The next attempt is asked again; it is not silently accepted because the first one was.
        probe.PersistResult = True : probe.PersistThrows = False
        Assert.True(gate.Verify(Blob(1), CancellationToken.None).IsAccepted)
        Assert.Equal(2, probe.ConsentCalls)
    End Sub

    <Fact>
    Public Sub Two_endpoints_of_an_unpinned_server_produce_one_prompt_and_the_loser_cannot_overwrite_the_pin()
        Dim probe As New GateProbe()
        Dim gate = probe.Gate("")
        Dim entered As New ManualResetEventSlim(False)
        Dim release As New ManualResetEventSlim(False)
        probe.OnConsent = Sub()
                              entered.Set()
                              release.Wait(5000)
                          End Sub

        Dim first = Task.Run(Function() gate.Verify(Blob(1), CancellationToken.None))
        Assert.True(entered.Wait(5000))
        Dim second = Task.Run(Function() gate.Verify(Blob(2), CancellationToken.None)) ' a different key, arriving mid-prompt
        Thread.Sleep(100)
        Assert.False(second.IsCompleted) ' waiting at the gate: no second prompt
        release.Set()

        Assert.True(first.Result.IsAccepted)
        Assert.Equal(HostKeyVerdict.Mismatch, second.Result.Verdict) ' strict against the accepted pin
        Assert.Equal(SshFingerprint.OfHostKey(Blob(1)), gate.Pin)
        Assert.Equal(1, probe.ConsentCalls)
    End Sub

    <Fact>
    Public Sub Consent_time_is_measured_including_a_prompt_that_is_still_open()
        Dim probe As New GateProbe()
        Dim gate = probe.Gate("")
        Dim midPrompt As Long = -1
        probe.OnConsent = Sub()
                              Interlocked.Exchange(probe.Now, 7000)
                              midPrompt = gate.ConsentMsSoFar()
                              Assert.True(gate.ConsentOutstanding)
                          End Sub
        Assert.True(gate.Verify(Blob(1), CancellationToken.None).IsAccepted)
        Assert.Equal(7000L, midPrompt)
        Assert.Equal(7000L, gate.ConsentMsSoFar())
        Assert.False(gate.ConsentOutstanding)
    End Sub

    <Fact>
    Public Sub Cancelling_a_prompt_or_the_wait_for_it_is_cancelled_and_leaves_no_pin()
        Dim probe As New GateProbe() With {.ConsentThrowsCancel = True}
        Dim gate = probe.Gate("")
        Assert.Equal(HostKeyVerdict.Cancelled, gate.Verify(Blob(1), CancellationToken.None).Verdict)
        Assert.Equal("", gate.Pin)

        Dim probe2 As New GateProbe()
        Dim gate2 = probe2.Gate("")
        Using cts As New CancellationTokenSource()
            cts.Cancel()
            Assert.Equal(HostKeyVerdict.Cancelled, gate2.Verify(Blob(1), cts.Token).Verdict)
        End Using
        Assert.Equal(0, probe2.ConsentCalls)
    End Sub

    ' ------------------------------------------------------------------ reconnect ----

    Private Shared Function Drive(Of T)(task As Task(Of T), clock As ManualClock) As T
        Dim guard As Integer = 0
        While Not task.IsCompleted AndAlso guard < 1000
            If Not clock.AdvanceToNext() Then Thread.Sleep(1)
            guard += 1
        End While
        Return Done(task)
    End Function

    <Fact>
    Public Sub Transport_failures_are_retried_after_1_4_and_10_seconds_then_given_up()
        Dim clock As New ManualClock()
        Dim stamps As New List(Of Long)()
        Dim run = ReconnectPolicy.RunAsync(Of Integer)(
            Function(ct)
                stamps.Add(clock.NowMs)
                Return Task.FromResult(ResourceResult(Of Integer).Fail(New ResourceFailure(ResourceFailureKind.Transport)))
            End Function, clock, CancellationToken.None)

        Dim result = Drive(run, clock)
        Assert.False(result.Succeeded)
        Assert.Equal({0L, 1000L, 5000L, 15000L}, stamps.ToArray())
    End Sub

    <Fact>
    Public Sub A_retry_that_succeeds_stops_the_schedule()
        Dim clock As New ManualClock()
        Dim calls As Integer = 0
        Dim run = ReconnectPolicy.RunAsync(Of String)(
            Function(ct)
                calls += 1
                Return Task.FromResult(If(calls < 3, ResourceResult(Of String).Fail(New ResourceFailure(ResourceFailureKind.Timeout)), ResourceResult(Of String).Ok("up")))
            End Function, clock, CancellationToken.None)
        Dim result = Drive(run, clock)
        Assert.True(result.Succeeded)
        Assert.Equal("up", result.Value)
        Assert.Equal(3, calls)
    End Sub

    <Theory>
    <InlineData(2)>
    <InlineData(3)>
    <InlineData(4)>
    <InlineData(5)>
    Public Sub Auth_key_status_and_local_failures_stop_at_once_without_a_wait(kindCode As Integer)
        Dim clock As New ManualClock()
        Dim calls As Integer = 0
        Dim kind As ResourceFailureKind = CType(kindCode, ResourceFailureKind)
        Dim run = ReconnectPolicy.RunAsync(Of Integer)(
            Function(ct)
                calls += 1
                Return Task.FromResult(ResourceResult(Of Integer).Fail(New ResourceFailure(kind)))
            End Function, clock, CancellationToken.None)
        Dim result = Done(run)
        Assert.Equal(kind, result.Failure.Kind)
        Assert.Equal(1, calls)
        Assert.Equal(0, clock.Pending)
    End Sub

    <Fact>
    Public Sub The_195_second_ceiling_stops_the_schedule_even_with_retries_left()
        Dim clock As New ManualClock()
        Dim calls As Integer = 0
        Dim run = ReconnectPolicy.RunAsync(Of Integer)(
            Function(ct)
                calls += 1
                clock.Advance(100000) ' each round burns 100 s
                Return Task.FromResult(ResourceResult(Of Integer).Fail(New ResourceFailure(ResourceFailureKind.Transport)))
            End Function, clock, CancellationToken.None)
        Dim result = Drive(run, clock)
        Assert.False(result.Succeeded)
        Assert.Equal(2, calls) ' 100 s + 1 s wait + 100 s = 201 s: the next 4 s wait would pass 195 s
    End Sub

    <Fact>
    Public Sub The_users_cancel_during_a_backoff_wait_ends_it_as_Cancelled()
        Dim clock As New ManualClock()
        Using cts As New CancellationTokenSource()
            Dim run = ReconnectPolicy.RunAsync(Of Integer)(
                Function(ct) Task.FromResult(ResourceResult(Of Integer).Fail(New ResourceFailure(ResourceFailureKind.Transport))),
                clock, cts.Token)
            Assert.Equal(1, clock.Pending) ' it is waiting out the first backoff
            cts.Cancel()
            Dim result = Done(run)
            Assert.Equal(ResourceFailureKind.Cancelled, result.Failure.Kind)
        End Using
    End Sub

    <Fact>
    Public Sub An_attempt_that_throws_is_classified_not_propagated()
        Dim clock As New ManualClock()
        Dim run = ReconnectPolicy.RunAsync(Of Integer)(
            Function(ct) As Task(Of ResourceResult(Of Integer))
                Throw New InvalidOperationException("bug")
            End Function, clock, CancellationToken.None)
        Dim result = Done(run)
        Assert.Equal(ResourceFailureKind.LocalIO, result.Failure.Kind) ' an unknown bug is never retried as a network blip
    End Sub

End Class
#End If
