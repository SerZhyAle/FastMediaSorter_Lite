Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit
Imports Xunit.Abstractions

''' <summary>
''' Ticket 263 T-3 (CR-14), the first END-TO-END run of the listener verifiers: the VB producer side (<c>LiveCredentialPairs</c>, the
''' device agent's <c>broadcast-start</c> / <c>broadcast-update</c> with <c>verifiers</c> and <c>maxListeners</c>) against the REAL
''' <c>fms-exchange-server.exe</c>, over real TLS, with a VB TLS client as the listener. Each side had only been tested alone (the Go
''' relay against a Go producer, the agent against a stub account); the risk this file closes is the interop of the two: the JSON shape,
''' the base64url of salt and hash, and the PBKDF2-HMAC-SHA256 at 600000 rounds that .NET writes and Go recomputes.
'''
''' Which binary: the one named by <c>FMS_EXCHANGE_TEST_EXE</c>, else <c>bin\Release\exchange\fms-exchange-server.exe</c>, built fresh
''' (<c>go build -o %TEMP%\fms-it\fms-exchange-server.exe .\cmd\fms-exchange-server</c> in <c>exchange-server</c>). Like the neighbouring
''' <see cref="ExchangeBinaryIntegrationTests"/>, a test RETURNS without asserting when there is no usable binary (a clone without Go);
''' unlike it, <c>FMS263_REQUIRE_BINARY=1</c> turns that into a FAILURE, and a test that really ran ends in one stdout line
''' <c>FMS263-EVIDENCE ..</c> per proof, so a run can be told from a silent return. A skipped test is not proof.
'''
''' Observation, no mock of the server: the broadcast is "accepted" when the admin pipe's <c>broadcast.list</c> shows it with the
''' <c>maxListeners</c> the agent sent and no <c>refused</c> reached the agent; the verifiers are "held" when the relay answers 401 to a
''' request with no credentials; a pair is "accepted" when the relay lets the request PAST the check - here with <c>503 Retry-After: 1</c>,
''' the relay's own answer for a live broadcast whose upload is not attached (no producer uploads in this test: a protected
''' broadcast reaches the relay only when sealed, and the sealed upload is not what this file judges). 503 and 401 never mix up here,
''' because the same sequential requests give the wrong pairs 401 and the right ones 503.
'''
''' The relay answers a SECOND credentialed check from the same source address for the same broadcast within one second with 401 whatever
''' the pair is (one token per second, taken before any hash and never refunded; a request with no usable credentials is a free challenge).
''' Every credentialed request below therefore waits <see cref="CheckPauseMs"/> after the previous one's answer.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class LiveRelayVerifiersBinaryTests
    Implements IDisposable

    Private Const Good As String = "Abcdefghi1"
    Private Const Login As String = "alice"
    Private Const LinkPassword As String = "link-secret-0042"
    Private Const MaxListenersSent As Integer = 7
    ''' <summary>The relay's challenge, byte for byte (DEVICE-EXCHANGE item BF).</summary>
    Private Const Challenge As String = "Basic realm=""fms-broadcast"""
    ''' <summary>More than the relay's one-second refill, counted from the end of the previous credentialed answer.</summary>
    Private Const CheckPauseMs As Integer = 1200

    Private ReadOnly _output As ITestOutputHelper
    Private ReadOnly _exeDir As String
    Private ReadOnly _dataDir As String
    Private ReadOnly _hasBinary As Boolean
    Private ReadOnly _refusals As New ConcurrentQueue(Of String)()
    Private _rig As ExchangeDeviceAgentTests.Rig
    Private _agent As ExchangeDeviceAgent
    Private _port As Integer
    Private _fingerprint As String = ""
    Private _lastCheckEndUtc As DateTime = DateTime.MinValue

    Public Sub New(output As ITestOutputHelper)
        _output = output
        ExchangeTestSupport.Reset()
        Dim source As String = Environment.GetEnvironmentVariable("FMS_EXCHANGE_TEST_EXE")
        If String.IsNullOrEmpty(source) OrElse Not File.Exists(source) Then
            source = Path.Combine(ExchangeTestSupport.RepoRoot(), "bin", "Release", "exchange", "fms-exchange-server.exe")
        End If
        _exeDir = Path.Combine(Path.GetTempPath(), "fms-live-relay-it-" & Guid.NewGuid().ToString("N"))
        _dataDir = Path.Combine(_exeDir, "data")
        If File.Exists(source) Then
            Directory.CreateDirectory(_exeDir)
            File.Copy(source, Path.Combine(_exeDir, ExchangeProcess.ExeName))
            ExchangeProcess.ExeDirectoryOverride = _exeDir
            ExchangeProcess.DataDirOverride = _dataDir
            ' A build older than the accounts model and the 168 verbs has nothing to drive here.
            Dim probe As ExchangeProcess.RunResult = ExchangeProcess.Run({"set-port-range"}, Nothing, 5000)
            _hasBinary = probe.Started AndAlso Not If(probe.StdErr, "").Contains("unknown command")
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If _agent IsNot Nothing Then
            Try : _agent.StopAsync().GetAwaiter().GetResult() : Catch : End Try
            Try : _agent.Dispose() : Catch : End Try
        End If
        Try
            ExchangeProcess.StopServer()
        Catch
        End Try
        If _rig IsNot Nothing Then _rig.Dispose()
        ExchangeTestSupport.Reset()
        ExchangeTestSupport.DeleteQuietly(_exeDir)
    End Sub

    ' ------------------------------------------------------------------------------------------------ plumbing

    ''' <summary>True when there is a usable binary. Without one the test returns, except under <c>FMS263_REQUIRE_BINARY=1</c>, where
    ''' it fails: a run that was asked to prove something must not pass by not running.</summary>
    Private Function BinaryAvailable() As Boolean
        If _hasBinary Then Return True
        If String.Equals(Environment.GetEnvironmentVariable("FMS263_REQUIRE_BINARY"), "1", StringComparison.Ordinal) Then
            Assert.True(False, "FMS263_REQUIRE_BINARY=1, but there is no usable fms-exchange-server.exe (set FMS_EXCHANGE_TEST_EXE to a fresh build)")
        End If
        Return False
    End Function

    ''' <summary>One line on stdout (and on the xUnit output) that only a test which ran to its assertions writes.</summary>
    Private Sub Evidence(proven As String)
        Dim line As String = "FMS263-EVIDENCE " & proven
        Console.WriteLine(line)
        _output.WriteLine(line)
    End Sub

    Private Shared Function FreePort() As Integer
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Try
            Return CType(l.LocalEndpoint, IPEndPoint).Port
        Finally
            l.Stop()
        End Try
    End Function

    Private Sub OnStartRefused(broadcastId As String, reason As ExchangeRefusal)
        _refusals.Enqueue(reason.ToString())
    End Sub

    ''' <summary>The server's certificate fingerprint as the first-contact probe shows it, asked until the just-started server accepts.
    ''' Nothing when it never does.</summary>
    Private Async Function ProbeFingerprintAsync() As Task(Of String)
        For attempt As Integer = 1 To 40
            Dim fingerprint As String = Nothing
            Try
                Dim info As ExchangeCertificateInfo = Await ExchangeTlsTransport.ProbeAsync("127.0.0.1", _port, CancellationToken.None)
                fingerprint = info.Fingerprint
            Catch ex As ExchangeConnectException
                fingerprint = Nothing
            End Try
            If fingerprint IsNot Nothing Then Return fingerprint
            Await Task.Delay(250)
        Next
        Return Nothing
    End Function

    ''' <summary>The server, an account, an enrolled agent that is Online: what the wizard of 168 leaves behind, made step by step.</summary>
    Private Async Function SetUpAsync() As Task
        Assert.True(ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, Login, Good)).Ok)
        _port = FreePort()
        Assert.Equal(ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.SetEndpoint("203.0.113.7:" & _port.ToString()))
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & _port.ToString()))
        Assert.Equal(ExchangeProcess.StartOutcome.Started, ExchangeProcess.Start())
        _fingerprint = Await ProbeFingerprintAsync()
        Assert.False(String.IsNullOrEmpty(_fingerprint), "the server did not accept a TLS connection")

        _rig = New ExchangeDeviceAgentTests.Rig()
        _agent = New ExchangeDeviceAgent(_rig.Ports)
        AddHandler _agent.BroadcastStartRefused, AddressOf OnStartRefused
        Dim request As New ExchangeEnrollRequest With {
            .Server = "127.0.0.1:" & _port.ToString(), .Login = Login, .Password = Good,
            .PlatformValid = False, .AcceptedFingerprint = _fingerprint, .DeviceName = "TESTPC"}
        Dim result As ExchangeEnrollResult
        Try
            result = Await _agent.EnrollAsync(request, CancellationToken.None)
        Finally
            request.Wipe()
        End Try
        Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() _agent.GetStatus().State = ExchangeAgentState.Online, 15))
    End Function

    ''' <summary>The state the producer hands the agent for a password-protected, NOT sealed broadcast: a LAN endpoint, the link and own
    ''' pairs at the contract's own round count, a listener cap. The credential set is the one object the producer's Digest and the
    ''' agent's `verifiers` both read.</summary>
    Private Shared Function NewState(broadcastId As String, credentials As LiveCredentialPairs) As LiveProducerState
        Dim input As New LiveDescriptorInput With {
            .Url = "http://127.0.0.1:1/live.aac", .Mode = "AUDIO_ONLY", .Title = "t", .SourceId = "src", .AudioCodec = "aac",
            .SampleRateHz = 44100, .Channels = 1, .AllowLanViewers = True, .AuthUser = LiveCredentialPairs.LinkUser}
        Return New LiveProducerState With {
            .Running = True, .Mode = "AUDIO_ONLY", .Url = input.Url, .Port = 1, .ListenerAddress = "127.0.0.1", .SourceName = "src",
            .BroadcastId = broadcastId, .Password = LinkPassword, .AuthUser = input.AuthUser, .Credentials = credentials,
            .MaxListeners = MaxListenersSent, .DescriptorJson = LiveProducerDescriptorWriter.BuildJson(input)}
    End Function

    Private Shared Function NewHub() As LiveBroadcastHub
        Return New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() 0L)
    End Function

    ''' <summary>The broadcast as the server's own local admin channel lists it, polled until it is there. Nothing on a timeout.</summary>
    Private Async Function WaitListedAsync(broadcastId As String, Optional seconds As Double = 25) As Task(Of ExchangeAdminBroadcast)
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(seconds)
        While DateTime.UtcNow < deadline
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.ListBroadcastsAsync(Login, CancellationToken.None)
            If reply.Ok Then
                Dim found As ExchangeAdminBroadcast = ExchangeAdminParser.Broadcasts(reply.DataJson).FirstOrDefault(Function(b) String.Equals(b.BroadcastId, broadcastId, StringComparison.Ordinal))
                If found IsNot Nothing Then Return found
            End If
            Await Task.Delay(200)
        End While
        Return Nothing
    End Function

    ' ------------------------------------------------------------------------------------------------ the listener

    ''' <summary>What a listener sees of the relay's answer: the status line, the challenges and the retry hint.</summary>
    Private NotInheritable Class ListenAnswer
        Public Status As Integer
        Public RetryAfter As String = ""
        Public ReadOnly Challenges As New List(Of String)()
    End Class

    ''' <summary>One <c>GET /v2/b/&lt;id&gt;/stream</c> over a TLS stream pinned to the server's fingerprint (the way the agent's own
    ''' transport opens one), with <c>Authorization: Basic</c> when a user is given. Only the answer's head is read.
    ''' A request with credentials waits out the relay's one-second check bucket first.</summary>
    Private Async Function ListenAsync(broadcastId As String, user As String, password As String) As Task(Of ListenAnswer)
        Dim credentialed As Boolean = user IsNot Nothing
        If credentialed Then
            Dim wait As Double = CheckPauseMs - (DateTime.UtcNow - _lastCheckEndUtc).TotalMilliseconds
            If wait > 0 Then Await Task.Delay(CInt(wait))
        End If
        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(20))
            Using conn As ExchangeTlsConnection = Await ExchangeTlsTransport.Instance.ConnectAsync("127.0.0.1", _port, _fingerprint, cts.Token)
                Dim request As New StringBuilder()
                request.Append("GET /v2/b/").Append(broadcastId).Append("/stream HTTP/1.1").Append(vbCrLf)
                request.Append("Host: 127.0.0.1:").Append(_port.ToString()).Append(vbCrLf)
                If credentialed Then
                    request.Append("Authorization: Basic ").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(user & ":" & password))).Append(vbCrLf)
                End If
                request.Append(vbCrLf)
                Dim bytes As Byte() = Encoding.ASCII.GetBytes(request.ToString())
                Await conn.Stream.WriteAsync(bytes, 0, bytes.Length, cts.Token)
                Await conn.Stream.FlushAsync(cts.Token)
                Dim head As String = Await ReadHeadAsync(conn.Stream, cts.Token)
                If credentialed Then _lastCheckEndUtc = DateTime.UtcNow
                Return ParseHead(head)
            End Using
        End Using
    End Function

    Private Shared Async Function ReadHeadAsync(stream As Stream, ct As CancellationToken) As Task(Of String)
        Dim buffer(1023) As Byte
        Dim head As New StringBuilder()
        While head.Length < 8192
            Dim n As Integer = Await stream.ReadAsync(buffer, 0, buffer.Length, ct)
            If n <= 0 Then Exit While
            head.Append(Encoding.Latin1.GetString(buffer, 0, n))
            If head.ToString().Contains(vbCrLf & vbCrLf) Then Exit While
        End While
        Dim text As String = head.ToString()
        Dim cut As Integer = text.IndexOf(vbCrLf & vbCrLf, StringComparison.Ordinal)
        Return If(cut >= 0, text.Substring(0, cut), text)
    End Function

    Private Shared Function ParseHead(head As String) As ListenAnswer
        Dim answer As New ListenAnswer()
        Dim lines As String() = head.Split({vbCrLf}, StringSplitOptions.None)
        Dim status As String() = lines(0).Split(" "c)
        If status.Length >= 2 Then Integer.TryParse(status(1), answer.Status)
        For i As Integer = 1 To lines.Length - 1
            Dim colon As Integer = lines(i).IndexOf(":"c)
            If colon <= 0 Then Continue For
            Dim name As String = lines(i).Substring(0, colon).Trim()
            Dim value As String = lines(i).Substring(colon + 1).Trim()
            If String.Equals(name, "WWW-Authenticate", StringComparison.OrdinalIgnoreCase) Then answer.Challenges.Add(value)
            If String.Equals(name, "Retry-After", StringComparison.OrdinalIgnoreCase) Then answer.RetryAfter = value
        Next
        Return answer
    End Function

    ''' <summary>401 with exactly one challenge, `Basic realm="fms-broadcast"`.</summary>
    Private Shared Sub AssertChallenged(what As String, a As ListenAnswer)
        Assert.True(a.Status = 401, what & ": status " & a.Status.ToString())
        Assert.True(a.Challenges.Count = 1 AndAlso String.Equals(a.Challenges(0), Challenge, StringComparison.Ordinal), what & ": WWW-Authenticate")
    End Sub

    ''' <summary>Past the check: not 401, no challenge, and the relay's own "live, but no upload attached" answer (503, Retry-After 1).</summary>
    Private Shared Sub AssertPassedTheCheck(what As String, a As ListenAnswer)
        Assert.True(a.Status <> 401, what & ": answered 401")
        Assert.True(a.Status = 503 AndAlso a.RetryAfter = "1", what & ": status " & a.Status.ToString() & " retry-after " & a.RetryAfter)
        Assert.Empty(a.Challenges)
    End Sub

    ' ------------------------------------------------------------------------------------------------ the proofs

    ''' <summary>Proofs 1 to 3 on one server, one enrolled agent and one broadcast the agent starts through its own hooks.</summary>
    <Fact>
    Public Async Function A_protected_broadcast_started_by_the_agent_is_checked_by_the_real_relay_and_one_pair_is_revoked_alone() As Task
        If Not BinaryAvailable() Then Return
        Await SetUpAsync()

        Dim id As String = LiveProducerTokens.NewId()
        ' the contract's 600000 rounds (the default): the relay recomputes the hash with that count, so a smaller test count would never match
        Dim credentials As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword)
        Dim ownPassword As String = credentials.PasswordFor(LiveCredentialPairs.OwnUser)
        Assert.False(String.IsNullOrEmpty(ownPassword))
        Dim hub As LiveBroadcastHub = NewHub()
        _agent.OnBroadcastStarted(NewState(id, credentials), hub, hub)
        Try
            ' 1. the server accepts the start with `verifiers` and `maxListeners`: listed by its own admin channel, nothing refused, and the
            ' relay holds a non-empty list (a start whose member the server read as absent or empty would admit everybody)
            Dim listed As ExchangeAdminBroadcast = Await WaitListedAsync(id)
            Assert.True(listed IsNot Nothing, "the server did not list the broadcast the agent started")
            Assert.Equal(MaxListenersSent, listed.MaxListeners)
            Assert.Empty(_refusals)
            AssertChallenged("no Authorization", Await ListenAsync(id, Nothing, Nothing))
            Evidence("broadcast-start with verifiers and maxListeners=" & MaxListenersSent.ToString() & " accepted by the real server (listed, nothing refused, relay challenges with no Authorization)")

            ' 2. wrong user and wrong password: the same 401 and the same challenge; the right pairs of the producer's own set pass the check
            AssertChallenged("an unknown user", Await ListenAsync(id, "nobody", LinkPassword))
            AssertChallenged("a wrong password", Await ListenAsync(id, LiveCredentialPairs.LinkUser, "not-the-password"))
            AssertPassedTheCheck("the listener pair", Await ListenAsync(id, LiveCredentialPairs.LinkUser, LinkPassword))
            AssertPassedTheCheck("the own pair", Await ListenAsync(id, LiveCredentialPairs.OwnUser, ownPassword))
            Assert.Empty(_refusals)
            Evidence("relay: no Authorization, unknown user and wrong password -> 401 Basic realm=""fms-broadcast""; the listener and own pairs the producer made pass the check (503 no upload, not 401)")

            ' 3. one pair dropped alone: the producer's set loses it at once, the update carries the full list, the relay refuses the dropped
            ' pair and still admits the one that remains. The update reaches the server a moment after the call returns, so the dropped pair
            ' is asked until the relay says 401 (it can only go 503 -> 401 once).
            Assert.True(Await _agent.RevokeBroadcastPairAsync(LiveCredentialPairs.LinkUser))
            Assert.Null(credentials.PasswordFor(LiveCredentialPairs.LinkUser))
            Assert.Equal(ownPassword, credentials.PasswordFor(LiveCredentialPairs.OwnUser))
            Dim dropped As ListenAnswer = Nothing
            Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
            Do
                dropped = Await ListenAsync(id, LiveCredentialPairs.LinkUser, LinkPassword)
            Loop While dropped.Status <> 401 AndAlso DateTime.UtcNow < deadline
            AssertChallenged("the dropped link pair", dropped)
            AssertPassedTheCheck("the remaining own pair", Await ListenAsync(id, LiveCredentialPairs.OwnUser, ownPassword))
            AssertChallenged("the dropped link pair, asked again", Await ListenAsync(id, LiveCredentialPairs.LinkUser, LinkPassword))
            AssertChallenged("no Authorization after the update", Await ListenAsync(id, Nothing, Nothing))
            Assert.Empty(_refusals)
            Evidence("broadcast-update without the listener pair: its password -> 401, the own pair still passes the check, the broadcast stays protected")
        Finally
            _agent.OnBroadcastStopped(id)
        End Try
    End Function

    ''' <summary>Proof 4: the server is stopped and started again (it keeps broadcasts in memory only), the agent reconnects and repeats its
    ''' `broadcast-start`, and the repeated start still carries the verifiers - without them the relay would check nobody.</summary>
    <Fact>
    Public Async Function After_the_server_restarts_the_repeated_broadcast_start_still_carries_the_verifiers() As Task
        If Not BinaryAvailable() Then Return
        Await SetUpAsync()

        Dim id As String = LiveProducerTokens.NewId()
        Dim credentials As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword)
        Dim ownPassword As String = credentials.PasswordFor(LiveCredentialPairs.OwnUser)
        Dim hub As LiveBroadcastHub = NewHub()
        _agent.OnBroadcastStarted(NewState(id, credentials), hub, hub)
        Try
            Assert.True(Await WaitListedAsync(id) IsNot Nothing, "the server did not list the broadcast the agent started")
            AssertChallenged("before the restart, no Authorization", Await ListenAsync(id, Nothing, Nothing))

            ' the cut: the server process goes away and with it the directory (broadcasts are never stored)
            Assert.NotEqual(ExchangeProcess.StopOutcome.NotRunning, ExchangeProcess.StopServer())
            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() _agent.GetStatus().State <> ExchangeAgentState.Online, 15), "the agent did not notice the cut")
            Dim started As Boolean = False
            For attempt As Integer = 1 To 5
                If ExchangeProcess.Start() = ExchangeProcess.StartOutcome.Started Then
                    started = True
                    Exit For
                End If
                Await Task.Delay(1000)
            Next
            Assert.True(started, "the server did not start again")
            Assert.Equal(_fingerprint, Await ProbeFingerprintAsync())
            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() _agent.GetStatus().State = ExchangeAgentState.Online, 30), "the agent did not come back online")

            ' the fresh server lists the broadcast only because the agent repeated its start
            Dim listed As ExchangeAdminBroadcast = Await WaitListedAsync(id, 30)
            Assert.True(listed IsNot Nothing, "the repeated start did not reach the restarted server")
            Assert.Equal(MaxListenersSent, listed.MaxListeners)
            Assert.Empty(_refusals)

            ' still enforced: a start repeated without `verifiers` would check nobody (no 401 for anyone, every request 503)
            AssertChallenged("after the restart, no Authorization", Await ListenAsync(id, Nothing, Nothing))
            AssertChallenged("after the restart, an unknown user", Await ListenAsync(id, "nobody", LinkPassword))
            AssertChallenged("after the restart, a wrong password", Await ListenAsync(id, LiveCredentialPairs.LinkUser, "not-the-password"))
            AssertPassedTheCheck("after the restart, the listener pair", Await ListenAsync(id, LiveCredentialPairs.LinkUser, LinkPassword))
            AssertPassedTheCheck("after the restart, the own pair", Await ListenAsync(id, LiveCredentialPairs.OwnUser, ownPassword))
            Assert.Empty(_refusals)
            Evidence("server restarted, agent reconnected and repeated broadcast-start: verifiers still enforced (no Authorization, unknown user and wrong password -> 401; both pairs pass the check)")
        Finally
            _agent.OnBroadcastStopped(id)
        End Try
    End Function

End Class
