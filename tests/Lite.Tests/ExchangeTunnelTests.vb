#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 171 rule 6 (phase C3) and rule 7 (C4): the tunnel as the LAST candidate of the endpoint race. Held here:
'''   - nothing is dialled - so nothing is opened on the exchange - while a direct address still answers: a staggered candidate waits for its
'''     head-start, or for every earlier candidate to fail, and a round a direct address wins never dials it;
'''   - the dial of the tunnel candidate is what creates the loopback forwarder, which is bound to loopback only and closes with the session and when idle;
'''   - the trust rule of item D (the platform first; only a rejected leaf after the user accepted its fingerprint; a changed one is a named stop);
'''   - no tunnel source exists for a resource of any other origin, nor while the exchange client features are not offered.
''' Every socket is on 127.0.0.1; a manual clock makes the head-start assertable without a real wait.
''' </summary>
<Collection("Exchange viewer gate")>
Public Class ExchangeTunnelTests

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const Fp2 As String = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    Private Const Rid As String = "EBESExQVFhcYGRobHB0eHw"
    Private Const Did As String = "AAECAwQFBgcICQoLDA0ODw"
    Private Const Sid As String = "q3Vb7YtK0xP2mN9sLfR4wA"
    Private Const Token As String = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

    ' ------------------------------------------------------------------ doubles ----

    Private NotInheritable Class ManualClock
        Implements IResolutionClock

        Private ReadOnly _timers As New List(Of Tuple(Of Long, TaskCompletionSource(Of Boolean)))
        Private ReadOnly _gate As New Object()
        Private _now As Long

        Public ReadOnly Property NowMs As Long Implements IResolutionClock.NowMs
            Get
                SyncLock _gate
                    Return _now
                End SyncLock
            End Get
        End Property

        Public Function Delay(ms As Integer, ct As CancellationToken) As Task Implements IResolutionClock.Delay
            Dim source As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            If ct.IsCancellationRequested Then
                source.TrySetCanceled(ct)
                Return source.Task
            End If
            SyncLock _gate
                _timers.Add(Tuple.Create(_now + ms, source))
            End SyncLock
            ct.Register(Sub()
                            SyncLock _gate
                                _timers.RemoveAll(Function(t) ReferenceEquals(t.Item2, source))
                            End SyncLock
                            source.TrySetCanceled(ct)
                        End Sub)
            Return source.Task
        End Function

        ''' <summary>Moves time forward in small steps so every timer that falls due fires in order and the awakened code runs before the next step.</summary>
        Public Async Function AdvanceAsync(ms As Long) As Task
            Dim target As Long
            SyncLock _gate
                target = _now + ms
            End SyncLock
            While True
                Dim fire As TaskCompletionSource(Of Boolean) = Nothing
                SyncLock _gate
                    Dim due As Tuple(Of Long, TaskCompletionSource(Of Boolean)) = _timers.Where(Function(t) t.Item1 <= target).OrderBy(Function(t) t.Item1).FirstOrDefault()
                    If due IsNot Nothing Then
                        _timers.Remove(due)
                        _now = Math.Max(_now, due.Item1)
                        fire = due.Item2
                    Else
                        _now = target
                    End If
                End SyncLock
                If fire Is Nothing Then Return
                fire.TrySetResult(True)
                Await Task.Delay(5)
            End While
        End Function
    End Class

    Private NotInheritable Class FakeSession
        Implements IDisposable

        Public Disposed As Boolean

        Public Sub Dispose() Implements IDisposable.Dispose
            Disposed = True
        End Sub
    End Class

    ''' <summary>A dialer with a script per endpoint kind: success, an immediate failure, or a dial that never finishes until the round cancels it.</summary>
    Private NotInheritable Class Script
        Implements IEndpointDialer

        Public ReadOnly Behaviour As New Dictionary(Of String, String)(StringComparer.Ordinal)      ' kind -> "ok" | "fail" | "hang"
        Public ReadOnly Dialled As New List(Of String)
        Public ReadOnly Sessions As New List(Of FakeSession)
        Public LastEndpoint As FmsEndpoint

        Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
            SyncLock Dialled
                Dialled.Add(endpoint.Kind)
                LastEndpoint = endpoint
            End SyncLock
            Dim how As String = Nothing
            If Not Behaviour.TryGetValue(endpoint.Kind, how) Then how = "fail"
            Select Case how
                Case "ok"
                    Dim s As New FakeSession()
                    Sessions.Add(s)
                    Return Task.FromResult(DialResult.Verified(s))
                Case "hang"
                    Dim tcs As New TaskCompletionSource(Of DialResult)(TaskCreationOptions.RunContinuationsAsynchronously)
                    ct.Register(Sub() tcs.TrySetResult(DialResult.Failed(New ResourceFailure(ResourceFailureKind.Cancelled))))
                    Return tcs.Task
                Case Else
                    Return Task.FromResult(DialResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)))
            End Select
        End Function

        Public Function CountOf(kind As String) As Integer
            SyncLock Dialled
                Return Dialled.Where(Function(k) k = kind).Count()
            End SyncLock
        End Function
    End Class

    Private Shared Function Direct(kind As String, host As String) As FmsEndpoint
        Return New FmsEndpoint With {.Kind = kind, .Host = host, .Port = 22}
    End Function

    Private Shared Async Function WaitUntilAsync(condition As Func(Of Boolean), Optional seconds As Double = 10) As Task(Of Boolean)
        Dim until As DateTime = DateTime.UtcNow.AddSeconds(seconds)
        While DateTime.UtcNow < until
            If condition() Then Return True
            Await Task.Delay(10)
        End While
        Return condition()
    End Function

    ' ------------------------------------------------------------------ the race ----

    <Fact>
    Public Sub The_tunnel_is_the_last_candidate_with_the_longest_head_start()
        Dim t As FmsEndpoint = ExchangeTunnelRules.Candidate()
        Assert.True(ExchangeTunnelRules.IsTunnel(t))
        Assert.Equal(ExchangeTunnelRules.StaggerMs, t.StartAfterMs)
        Assert.True(t.StartAfterMs > 0)
        Assert.False(ExchangeTunnelRules.IsTunnel(Direct("lan", "10.0.0.1")))
        Assert.Equal(0, Direct("lan", "10.0.0.1").StartAfterMs)       ' every other candidate is dialled as before
        ' ConnectAsync puts it after PlanCandidates: the source scan holds the order
        Dim src As String = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Resources", "ResourceConnect.vb"))
        Assert.True(src.IndexOf("candidates.Add(ExchangeTunnelRules.Candidate())", StringComparison.Ordinal) > src.IndexOf("EndpointResolver.PlanCandidates(CandidatesOf(descriptor))", StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Async Function A_direct_address_that_answers_first_never_dials_the_tunnel() As Task
        Dim script As New Script()
        script.Behaviour("lan") = "ok"
        Dim source As New CountingSource()
        Dim dialer As New ExchangeTunnelDialer(script, source)
        Dim clock As New ManualClock()
        Dim candidates As New List(Of FmsEndpoint) From {Direct("lan", "192.168.1.5"), Direct("portforward", "203.0.113.9"), ExchangeTunnelRules.Candidate()}
        Dim outcome As ResolutionOutcome = Await EndpointResolver.ResolveAsync(candidates, dialer, clock, CancellationToken.None, 2)
        Assert.True(outcome.Succeeded)
        Await clock.AdvanceAsync(10000)
        Assert.Equal(0, script.CountOf(ExchangeTunnelRules.TunnelKind))
        Assert.Equal(0, source.Opened)                                 ' nothing was opened on the exchange
    End Function

    Private NotInheritable Class CountingSource
        Implements IExchangeTunnelSource

        Public Opened As Integer
        Public Result As ExchangeTunnelOpen
        Public Leases As New List(Of ExchangeTunnelLease)

        Public Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeTunnelOpen) Implements IExchangeTunnelSource.OpenAsync
            Interlocked.Increment(Opened)
            If Result IsNot Nothing Then Return Task.FromResult(Result)
            Dim lease As New ExchangeTunnelLease(New FailingOpener(), 0)
            lease.Start()
            Leases.Add(lease)
            Return Task.FromResult(New ExchangeTunnelOpen(lease, ExchangeTunnelFailure.None))
        End Function
    End Class

    Private NotInheritable Class FailingOpener
        Implements IExchangeDataOpener

        Public Calls As Integer

        Public Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeOpenResult) Implements IExchangeDataOpener.OpenAsync
            Interlocked.Increment(Calls)
            Return Task.FromResult(ExchangeOpenResult.Failed(ExchangeOpenOutcome.Closed))
        End Function
    End Class

    <Fact>
    Public Async Function The_tunnel_waits_for_its_head_start_while_a_direct_address_is_still_pending() As Task
        Dim script As New Script()
        script.Behaviour("lan") = "hang"            ' answers nothing for now
        script.Behaviour("portforward") = "fail"    ' frees its lane at once
        script.Behaviour(ExchangeTunnelRules.TunnelKind) = "ok"
        Dim source As New CountingSource()
        Dim dialer As New ExchangeTunnelDialer(script, source)
        Dim clock As New ManualClock()
        Dim candidates As New List(Of FmsEndpoint) From {Direct("lan", "192.168.1.5"), Direct("portforward", "203.0.113.9"), ExchangeTunnelRules.Candidate()}
        Dim resolving As Task(Of ResolutionOutcome) = EndpointResolver.ResolveAsync(candidates, dialer, clock, CancellationToken.None, 2)

        Await Task.Delay(100)
        Assert.Equal(0, source.Opened)                                  ' the lan one is still pending: the exchange is not touched
        Await clock.AdvanceAsync(ExchangeTunnelRules.StaggerMs - 100)
        Assert.Equal(0, source.Opened)
        Await clock.AdvanceAsync(200)                                   ' the head-start has passed
        Dim outcome As ResolutionOutcome = Await resolving.WaitAsync(TimeSpan.FromSeconds(15))
        Assert.True(outcome.Succeeded)
        Assert.Equal(1, source.Opened)
        Assert.Equal(1, script.CountOf(ExchangeTunnelRules.TunnelKind))
        Assert.Equal("127.0.0.1", script.LastEndpoint.Host)             ' the inner dialer was pointed at the lease's loopback port
        Assert.Equal(source.Leases(0).Port, script.LastEndpoint.Port)
        outcome.Session.Dispose()
        Assert.True(source.Leases(0).IsDisposed)                        ' the lease dies with the session
    End Function

    <Fact>
    Public Async Function When_every_direct_address_has_failed_the_tunnel_is_dialled_at_once() As Task
        Dim script As New Script()
        script.Behaviour("lan") = "fail"
        script.Behaviour("portforward") = "fail"
        script.Behaviour(ExchangeTunnelRules.TunnelKind) = "ok"
        Dim source As New CountingSource()
        Dim clock As New ManualClock()
        Dim candidates As New List(Of FmsEndpoint) From {Direct("lan", "192.168.1.5"), Direct("portforward", "203.0.113.9"), ExchangeTunnelRules.Candidate()}
        Dim outcome As ResolutionOutcome = Await EndpointResolver.ResolveAsync(candidates, New ExchangeTunnelDialer(script, source), clock, CancellationToken.None, 1).WaitAsync(TimeSpan.FromSeconds(15))
        Assert.True(outcome.Succeeded)                                  ' no clock was advanced: nothing to wait for once the others are out
        Assert.Equal(1, source.Opened)
        outcome.Session.Dispose()
    End Function

    ' ------------------------------------------------------------------ the dialer ----

    <Fact>
    Public Async Function An_ordinary_candidate_goes_to_the_inner_dialer_untouched() As Task
        Dim script As New Script()
        script.Behaviour("lan") = "ok"
        Dim source As New CountingSource()
        Dim result As DialResult = Await New ExchangeTunnelDialer(script, source).DialAsync(Direct("lan", "192.168.1.5"), CancellationToken.None)
        Assert.NotNull(result.Session)
        Assert.Equal(0, source.Opened)
        Assert.Equal("192.168.1.5", script.LastEndpoint.Host)
    End Function

    <Theory>
    <InlineData("CertificateChanged", "KeyMismatch")>
    <InlineData("Declined", "Cancelled")>
    <InlineData("Cancelled", "Cancelled")>
    <InlineData("Unavailable", "Transport")>
    Public Async Function A_tunnel_that_cannot_open_fails_the_candidate_with_the_matching_class(failureName As String, expectedName As String) As Task
        Dim failure As ExchangeTunnelFailure = CType([Enum].Parse(GetType(ExchangeTunnelFailure), failureName), ExchangeTunnelFailure)
        Dim expected As ResourceFailureKind = CType([Enum].Parse(GetType(ResourceFailureKind), expectedName), ResourceFailureKind)
        Dim script As New Script()
        Dim source As New CountingSource() With {.Result = ExchangeTunnelOpen.Failed(failure)}
        Dim result As DialResult = Await New ExchangeTunnelDialer(script, source).DialAsync(ExchangeTunnelRules.Candidate(), CancellationToken.None)
        Assert.Null(result.Session)
        Assert.Equal(expected, result.Failure.Kind)
        Assert.Equal(0, script.CountOf(ExchangeTunnelRules.TunnelKind))      ' the inner dialer was never asked
        If failure = ExchangeTunnelFailure.CertificateChanged Then
            Assert.Equal(ExchangeTunnelRules.CertificateDetail, result.Failure.Detail)
            Assert.True(result.Failure.IsFatalForAttempt)                    ' a changed certificate ends the whole round, never raced past
            Assert.NotEqual(ResourceMessages.ForFailure(New ResourceFailure(ResourceFailureKind.KeyMismatch)), ResourceMessages.ForFailure(result.Failure))   ' its own sentence, not the SSH key one
        End If
    End Function

    <Fact>
    Public Async Function A_failed_inner_dial_closes_the_lease_at_once() As Task
        Dim script As New Script()                                           ' every kind fails by default
        Dim source As New CountingSource()
        Dim result As DialResult = Await New ExchangeTunnelDialer(script, source).DialAsync(ExchangeTunnelRules.Candidate(), CancellationToken.None)
        Assert.Null(result.Session)
        Assert.True(source.Leases(0).IsDisposed)
    End Function

    ' ------------------------------------------------------------------ the forwarder (lease) ----

    <Fact>
    Public Async Function The_forwarder_is_loopback_only_and_every_accepted_connection_is_one_connect() As Task
        Dim opener As New FailingOpener()
        Using lease As New ExchangeTunnelLease(opener, 0)
            lease.Start()
            Assert.True(lease.Port > 0)
            For i As Integer = 1 To 3
                Using c As New TcpClient()
                    Await c.ConnectAsync(IPAddress.Loopback, lease.Port)
                End Using
            Next
            Assert.True(Await WaitUntilAsync(Function() opener.Calls = 3))
            Await Task.Delay(100)
            Assert.Equal(3, opener.Calls)                                    ' one `connect` per connection, never a retry
            Assert.Equal(3, lease.ConnectsStarted)

            ' not reachable from another address of this machine: the listener is the library's loopback one
            Dim other As IPAddress = NetworkInterface.GetAllNetworkInterfaces().
                Where(Function(n) n.OperationalStatus = OperationalStatus.Up AndAlso n.NetworkInterfaceType <> NetworkInterfaceType.Loopback).
                SelectMany(Function(n) n.GetIPProperties().UnicastAddresses).
                Select(Function(a) a.Address).
                FirstOrDefault(Function(a) a.AddressFamily = AddressFamily.InterNetwork AndAlso Not IPAddress.IsLoopback(a))
            If other IsNot Nothing Then
                Using c As New TcpClient()
                    Dim refused As Boolean = False
                    Try
                        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(3))
                            Await c.ConnectAsync(other, lease.Port, cts.Token)
                        End Using
                    Catch ex As Exception When TypeOf ex Is SocketException OrElse TypeOf ex Is OperationCanceledException
                        refused = True
                    End Try
                    Assert.True(refused, "the forwarder answered on a non-loopback address")
                End Using
            End If
        End Using
    End Function

    <Fact>
    Public Async Function A_disposed_lease_stops_listening() As Task
        Dim lease As New ExchangeTunnelLease(New FailingOpener(), 0)
        lease.Start()
        Dim port As Integer = lease.Port
        lease.Dispose()
        Assert.True(lease.IsDisposed)
        Using c As New TcpClient()
            Dim refused As Boolean = False
            Try
                Await c.ConnectAsync(IPAddress.Loopback, port)
            Catch ex As SocketException
                refused = True
            End Try
            Assert.True(refused)
        End Using
    End Function

    <Fact>
    Public Async Function A_forwarder_nothing_uses_is_closed_after_the_idle_timeout() As Task
        Dim lease As New ExchangeTunnelLease(New FailingOpener(), 300)
        lease.Start()
        Assert.False(lease.IsDisposed)
        Assert.True(Await WaitUntilAsync(Function() lease.IsDisposed, 10))
    End Function

    <Fact>
    Public Sub A_forwarder_is_bound_to_one_resource_by_its_opener_and_to_nothing_else()
        ' the lease takes ONE opener and has no member that changes it: it cannot be pointed at a second resource
        Dim ctor As Reflection.ConstructorInfo = GetType(ExchangeTunnelLease).GetConstructors(Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Public).Single()
        Assert.Equal({GetType(IExchangeDataOpener), GetType(Integer)}, ctor.GetParameters().Select(Function(p) p.ParameterType).ToArray())
        Dim setters As String() = GetType(ExchangeTunnelLease).GetProperties(Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Public).
            Where(Function(p) p.SetMethod IsNot Nothing).Select(Function(p) p.Name).ToArray()
        Assert.Empty(setters)
    End Sub

    ' ------------------------------------------------------------------ trust and the sources ----

    Private NotInheritable Class FakeProbe
        Implements IExchangeCertificateProbe

        Public Info As ExchangeCertificateInfo = New ExchangeCertificateInfo(Fp, True)
        Public Failure As ExchangeConnectFailure?
        Public Calls As Integer

        Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeCertificateProbe.ProbeAsync
            Interlocked.Increment(Calls)
            If Failure.HasValue Then Throw New ExchangeConnectException(Failure.Value, "", "probe")
            Return Task.FromResult(Info)
        End Function
    End Class

    Private NotInheritable Class NoTransport
        Implements IExchangeTransport

        Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
            Throw New InvalidOperationException("the transport must not be used before a connection is accepted")
        End Function
    End Class

    Private NotInheritable Class Stores
        Implements IDisposable

        Public ReadOnly Port As New MemoryPort()
        Public ReadOnly Folder As String = Path.Combine(Path.GetTempPath(), "fms-tunnel-test-" & Guid.NewGuid().ToString("N"))
        Public ReadOnly Accounts As ExchangeAccountStore
        Public ReadOnly Credentials As ExchangeCredentialStore

        Public Sub New()
            Accounts = New ExchangeAccountStore(Port)
            Credentials = New ExchangeCredentialStore(Folder, New XorCipher())
        End Sub

        Public Sub SignIn(Optional pin As String = "", Optional kind As ExchangeCertificateKind = ExchangeCertificateKind.CaSigned, Optional withToken As Boolean = True)
            Accounts.Save(New ExchangeAccount("exchange.example.net:22022", "alice", pin, kind, Did, "This PC"))
            If withToken Then Assert.True(Credentials.Save(Did, Token))
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Directory.Exists(Folder) Then Directory.Delete(Folder, True)
            Catch ex As IOException
            End Try
        End Sub
    End Class

    Private NotInheritable Class MemoryPort
        Implements IExchangeSettingsPort

        Public ReadOnly Values As New Dictionary(Of String, String)(StringComparer.Ordinal)

        Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function

        Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
            Values(name) = value
        End Sub

        Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
            Values.Remove(name)
        End Sub
    End Class

    Private NotInheritable Class XorCipher
        Implements IExchangeSecretCipher

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            Return plain.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            Return blob.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function
    End Class

    Private Shared Function AccountSource(stores As Stores, probe As IExchangeCertificateProbe) As ExchangeAccountTunnelSource
        Return New ExchangeAccountTunnelSource(Rid, stores.Accounts, stores.Credentials, New NoTransport(), probe, "26.10.7.1", 0)
    End Function

    <Fact>
    Public Async Function The_account_tunnel_needs_an_account_and_a_token() As Task
        Using stores As New Stores()
            Dim probe As New FakeProbe()
            Assert.Equal(ExchangeTunnelFailure.Unavailable, (Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)).Failure)
            stores.SignIn(withToken:=False)
            Assert.Equal(ExchangeTunnelFailure.Unavailable, (Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)).Failure)
            Assert.Equal(0, probe.Calls)                                      ' nothing was even looked at on the network
        End Using
    End Function

    <Fact>
    Public Async Function The_account_tunnel_follows_item_D_platform_first_a_pin_only_when_it_exists_a_change_is_a_named_stop() As Task
        Using stores As New Stores()
            Dim probe As New FakeProbe()
            ' a CA-signed deployment: nothing pinned, the platform validates -> opens
            stores.SignIn(pin:="", kind:=ExchangeCertificateKind.CaSigned)
            Dim open As ExchangeTunnelOpen = Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)
            Assert.Equal(ExchangeTunnelFailure.None, open.Failure)
            Assert.True(open.Lease.Port > 0)
            open.Lease.Dispose()

            ' ... until the platform no longer validates it: a named stop, not a silent pin
            probe.Info = New ExchangeCertificateInfo(Fp2, False)
            Assert.Equal(ExchangeTunnelFailure.CertificateChanged, (Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)).Failure)

            ' a pinned deployment: the pin matches -> opens; another leaf -> a named stop, even when the platform would accept it
            stores.SignIn(pin:=Fp, kind:=ExchangeCertificateKind.Pinned)
            probe.Info = New ExchangeCertificateInfo(Fp, False)
            open = Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)
            Assert.Equal(ExchangeTunnelFailure.None, open.Failure)
            open.Lease.Dispose()
            probe.Info = New ExchangeCertificateInfo(Fp2, True)
            Assert.Equal(ExchangeTunnelFailure.CertificateChanged, (Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)).Failure)
        End Using
    End Function

    <Fact>
    Public Async Function A_server_that_does_not_answer_is_unavailable_and_a_cancel_is_a_cancel() As Task
        Using stores As New Stores()
            stores.SignIn()
            Dim probe As New FakeProbe() With {.Failure = ExchangeConnectFailure.Network}
            Assert.Equal(ExchangeTunnelFailure.Unavailable, (Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)).Failure)
            probe.Failure = ExchangeConnectFailure.Cancelled
            Assert.Equal(ExchangeTunnelFailure.Cancelled, (Await AccountSource(stores, probe).OpenAsync(CancellationToken.None)).Failure)
        End Using
    End Function

    Private Shared Function LinkSource(probe As FakeProbe, consent As Func(Of String, CancellationToken, Boolean), persist As Func(Of String, Boolean),
                                       Optional pin As String = "", Optional shareId As String = Sid) As ExchangeLinkTunnelSource
        Return New ExchangeLinkTunnelSource("exchange.example.net", 22022, Function() shareId, pin, consent, persist, New NoTransport(), probe, "26.10.7.1", 0)
    End Function

    <Fact>
    Public Async Function A_link_to_a_server_the_platform_trusts_asks_nothing() As Task
        Dim probe As New FakeProbe()
        Dim asked As Integer
        Dim open As ExchangeTunnelOpen = Await LinkSource(probe, Function(f, c)
                                                                      asked += 1
                                                                      Return True
                                                                  End Function, Function(f) True).OpenAsync(CancellationToken.None)
        Assert.Equal(ExchangeTunnelFailure.None, open.Failure)
        Assert.Equal(0, asked)
        open.Lease.Dispose()
    End Function

    <Fact>
    Public Async Function A_leaf_the_platform_rejects_is_shown_to_the_user_and_pinned_only_after_an_explicit_yes() As Task
        Dim probe As New FakeProbe() With {.Info = New ExchangeCertificateInfo(Fp2, False)}
        Dim shown As String = Nothing
        Dim persisted As String = Nothing

        Dim declined As ExchangeTunnelOpen = Await LinkSource(probe, Function(f, c)
                                                                         shown = f
                                                                         Return False
                                                                     End Function, Function(f)
                                                                                       persisted = f
                                                                                       Return True
                                                                                   End Function).OpenAsync(CancellationToken.None)
        Assert.Equal(ExchangeTunnelFailure.Declined, declined.Failure)
        Assert.Equal(Fp2, shown)
        Assert.Null(persisted)                                                ' a No pins nothing

        Dim notStored As ExchangeTunnelOpen = Await LinkSource(probe, Function(f, c) True, Function(f) False).OpenAsync(CancellationToken.None)
        Assert.Equal(ExchangeTunnelFailure.Declined, notStored.Failure)       ' the pin could not be stored: no "accept any certificate"

        Dim accepted As ExchangeTunnelOpen = Await LinkSource(probe, Function(f, c) True, Function(f)
                                                                                              persisted = f
                                                                                              Return True
                                                                                          End Function).OpenAsync(CancellationToken.None)
        Assert.Equal(ExchangeTunnelFailure.None, accepted.Failure)
        Assert.Equal(Fp2, persisted)
        accepted.Lease.Dispose()
    End Function

    <Fact>
    Public Async Function A_pinned_link_is_checked_against_its_pin_and_a_bad_share_id_opens_nothing() As Task
        Dim probe As New FakeProbe() With {.Info = New ExchangeCertificateInfo(Fp2, False)}
        Assert.Equal(ExchangeTunnelFailure.CertificateChanged, (Await LinkSource(probe, Function(f, c) True, Function(f) True, pin:=Fp).OpenAsync(CancellationToken.None)).Failure)
        Dim ok As ExchangeTunnelOpen = Await LinkSource(probe, Function(f, c) False, Function(f) False, pin:=Fp2).OpenAsync(CancellationToken.None)
        Assert.Equal(ExchangeTunnelFailure.None, ok.Failure)                  ' the pin matches: no prompt even though the platform rejects the leaf
        ok.Lease.Dispose()
        Assert.Equal(ExchangeTunnelFailure.Unavailable, (Await LinkSource(probe, Nothing, Nothing, shareId:="bad").OpenAsync(CancellationToken.None)).Failure)
        Assert.Equal(ExchangeTunnelFailure.Unavailable, (Await LinkSource(probe, Nothing, Nothing, shareId:="").OpenAsync(CancellationToken.None)).Failure)
    End Function

    ' ------------------------------------------------------------------ who gets a source ----

    Private Shared Function WithGate(open As Boolean, test As Action) As Boolean
        Dim before As String = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, EnvironmentVariableTarget.Process)
        Try
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, If(open, "1", Nothing), EnvironmentVariableTarget.Process)
            test()
            Return True
        Finally
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, before, EnvironmentVariableTarget.Process)
        End Try
    End Function

    Private Shared Function Resource(origin As String) As ResourceDescriptor
        Return New ResourceDescriptor With {.Id = "res:1", .Kind = ResourceKind.Sftp, .Host = "h", .Port = 22, .UserName = "u", .RootPath = "/", .Origin = origin,
            .ExchangeResourceId = If(origin = ResourceOrigin.Exchange, Rid, Nothing), .TunnelHost = If(origin = ResourceOrigin.Link, "exchange.example.net", Nothing),
            .TunnelPort = If(origin = ResourceOrigin.Link, 22022, CType(Nothing, Integer?)), .TunnelSecretHandle = If(origin = ResourceOrigin.Link, "cred:abc", Nothing)}
    End Function

    <Fact>
    Public Sub No_source_exists_while_the_features_are_not_offered_or_for_a_resource_of_another_origin()
        If ExchangeFeatures.Offered Then Return        ' the build offers it everywhere: this guard has nothing to prove
        WithGate(False, Sub()
                            Assert.Null(ExchangeTunnelSources.For(Resource(ResourceOrigin.Exchange), Nothing, Nothing, Nothing))
                            Assert.Null(ExchangeTunnelSources.For(Resource(ResourceOrigin.Link), Nothing, Nothing, Nothing))
                        End Sub)
        WithGate(True, Sub()
                           Assert.Null(ExchangeTunnelSources.For(Resource(Nothing), Nothing, Nothing, Nothing))           ' made by hand: never touched
                           Assert.Null(ExchangeTunnelSources.For(Nothing, Nothing, Nothing, Nothing))
                           Assert.IsType(Of ExchangeAccountTunnelSource)(ExchangeTunnelSources.For(Resource(ResourceOrigin.Exchange), Nothing, Nothing, Nothing))
                           Assert.IsType(Of ExchangeLinkTunnelSource)(ExchangeTunnelSources.For(Resource(ResourceOrigin.Link), Nothing, Nothing, Nothing))
                           Dim noHandle As ResourceDescriptor = Resource(ResourceOrigin.Link)
                           noHandle.TunnelSecretHandle = Nothing
                           Assert.Null(ExchangeTunnelSources.For(noHandle, Nothing, Nothing, Nothing))
                       End Sub)
    End Sub

    ' ------------------------------------------------------------------ the connect, end to end ----

    <Fact>
    Public Async Function ResourceConnect_adds_the_tunnel_only_when_it_is_given_a_source() As Task
        ' with no source the race is exactly what it was: the placeholder candidate never appears
        Dim script As New Script()
        script.Behaviour("primary") = "ok"
        Dim d As ResourceDescriptor = Resource(Nothing)
        Dim outcome As ConnectOutcome = Await ResourceConnect.ConnectAsync(d, "pw", Function(f, c) True, Function(f) True, Nothing, CancellationToken.None, script)
        Assert.Empty(script.Dialled.Where(Function(k) k = ExchangeTunnelRules.TunnelKind))
        Assert.NotNull(outcome)
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found")
    End Function

End Class
#End If
