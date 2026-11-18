#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' LIVE-BROADCAST 1.5 items T and U (19_contract_amendment_2026-10-25.md, 20_contract_amendment_2026-10-26.md), ticket L0329:
' the receiver reads a DELIBERATE end - a relay 410, a tunnel `connect` refused `ended`, a clean end of the body followed by one
' reconnect that is refused or answered 404 / 410 - and shows the ended state (no further automatic attempt); a reset, a timeout,
' a 404 before anything played and `unavailable` on a tunnel stay what they were. Every test is an input and the reading the
' contract gives it. The directory's removal (item U's third bullet) is not here: the viewer follows no directory.

Public Class LiveBroadcastEndTests

    ''' <summary>The player side: counts starts and reads each stream like LibVLC would.</summary>
    Private NotInheritable Class EndHost
        Implements ILiveRunnerHost

        Public ReadOnly Events As New ConcurrentQueue(Of String)()
        Private ReadOnly players As New ConcurrentQueue(Of LiveMockPlayer)()
        Private starts As Integer

        Public ReadOnly Property StartCount As Integer
            Get
                Return Volatile.Read(starts)
            End Get
        End Property

        Public Function StartPlaybackAsync(session As LiveHttpPlaybackSession) As Task Implements ILiveRunnerHost.StartPlaybackAsync
            Interlocked.Increment(starts)
            players.Enqueue(New LiveMockPlayer(session.Stream))
            Return Task.CompletedTask
        End Function

        Public Async Function StopPlaybackAsync() As Task Implements ILiveRunnerHost.StopPlaybackAsync
            ' The stream was closed before this call: every read the "player" had blocked must be released.
            Await Task.WhenAll(players.Select(Function(p) p.Completion)).WaitAsync(TimeSpan.FromSeconds(5))
        End Function

        Public Sub OnState(state As LiveSessionState, retryCount As Integer, elapsedMs As Long) Implements ILiveRunnerHost.OnState
            Events.Enqueue("state:" & state.ToString())
        End Sub
    End Class

    Private Shared Function Quick() As LivePlaybackOptions
        Return New LivePlaybackOptions With {.ConnectTimeoutMs = 2000, .StallTimeoutMs = 5000, .WatchdogPeriodMs = 50}
    End Function

    Private Shared Function EndpointOf(url As String) As LiveBroadcastEndpoint
        Return New LiveBroadcastEndpoint With {.Url = url, .Mode = "AUDIO_ONLY", .ModeValue = LiveBroadcastMode.AudioOnly, .Transport = "HTTP"}
    End Function

    Private Shared Function NoDelay(ms As Integer, token As CancellationToken) As Task
        token.ThrowIfCancellationRequested()
        Return Task.CompletedTask
    End Function

    ''' <summary>Runs the loop to its own end. A loop that never ends by itself is cancelled after 20 s and so reads UserStopped.</summary>
    Private Shared Async Function RunToEndAsync(runner As LiveBroadcastRunner) As Task(Of LiveRunEnd)
        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(20))
            Return Await runner.RunAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(25))
        End Using
    End Function

    ' ------------------------------------------------------------------ item T, the runner against a device

    <Fact>
    Public Async Function A_410_Is_The_Ended_State_Not_A_Failure_And_Nothing_Is_Retried() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               LiveMockDevice.Status(c, 410, "Gone")
                                               Return Task.CompletedTask
                                           End Function)
            Dim host As New EndHost()
            Dim policy As New LiveReconnectPolicy()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, host, Quick(), delayAsync:=AddressOf NoDelay)

            Assert.Equal(LiveRunEnd.BroadcastEnded, Await RunToEndAsync(runner))

            Assert.Equal(LiveTerminationKind.BroadcastEnded, policy.TerminationReason)
            Assert.Equal(LiveSessionState.Ended, policy.CurrentState)
            Assert.Equal(1, device.AcceptedCount)
            Assert.Equal(0, host.StartCount)
            Assert.Equal(Localization.T("Трансляция завершена"), policy.GetTerminalStatusText())
            Assert.Equal("state:Ended", host.Events.Last())
        End Using
    End Function

    <Fact>
    Public Async Function A_404_Before_Anything_Played_Stays_Not_Found_And_Is_Never_Read_As_An_End() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               LiveMockDevice.Status(c, 404, "Not Found")
                                               Return Task.CompletedTask
                                           End Function)
            Dim host As New EndHost()
            Dim policy As New LiveReconnectPolicy()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, host, Quick(), delayAsync:=AddressOf NoDelay)

            Assert.Equal(LiveRunEnd.Failed, Await RunToEndAsync(runner))

            Assert.Equal(LiveTerminationKind.NotFoundOrGone, policy.TerminationReason)
            Assert.Equal(Localization.T("Трансляция больше недоступна"), policy.GetTerminalStatusText())
        End Using
    End Function

    <Fact>
    Public Async Function A_Clean_End_Followed_By_One_Reconnect_Answered_404_Is_The_Ended_State() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               If c.Sequence = 1 Then Return LiveMockDevice.PhoneChunked(c, 3, 0, t)   ' the body ends cleanly
                                               LiveMockDevice.Status(c, 404, "Not Found")
                                               Return Task.CompletedTask
                                           End Function)
            Dim host As New EndHost()
            Dim policy As New LiveReconnectPolicy()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, host, Quick(), delayAsync:=AddressOf NoDelay)

            Assert.Equal(LiveRunEnd.BroadcastEnded, Await RunToEndAsync(runner))

            Assert.Equal(LiveTerminationKind.BroadcastEnded, policy.TerminationReason)
            Assert.Equal(2, device.AcceptedCount)   ' one that played, ONE reconnect - no third attempt
            Assert.Equal(1, host.StartCount)
        End Using
    End Function

    <Fact>
    Public Async Function A_Clean_End_Followed_By_One_Reconnect_Answered_410_Is_The_Ended_State() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               If c.Sequence = 1 Then Return LiveMockDevice.PhoneChunked(c, 3, 0, t)
                                               LiveMockDevice.Status(c, 410, "Gone")
                                               Return Task.CompletedTask
                                           End Function)
            Dim policy As New LiveReconnectPolicy()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, New EndHost(), Quick(), delayAsync:=AddressOf NoDelay)

            Assert.Equal(LiveRunEnd.BroadcastEnded, Await RunToEndAsync(runner))
            Assert.Equal(2, device.AcceptedCount)
        End Using
    End Function

    <Fact>
    Public Async Function A_Reset_Followed_By_A_404_Stays_A_Failure_It_Is_Not_A_Clean_End() As Task
        Using device As New LiveMockDevice(Async Function(c, t)
                                               If c.Sequence = 1 Then
                                                   Await LiveMockDevice.PhoneChunked(c, 3, 0, t, endCleanly:=False)
                                                   c.Reset()
                                               Else
                                                   LiveMockDevice.Status(c, 404, "Not Found")
                                               End If
                                           End Function)
            Dim policy As New LiveReconnectPolicy()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, New EndHost(), Quick(), delayAsync:=AddressOf NoDelay)

            Assert.Equal(LiveRunEnd.Failed, Await RunToEndAsync(runner))

            Assert.Equal(LiveTerminationKind.NotFoundOrGone, policy.TerminationReason)
        End Using
    End Function

    <Fact>
    Public Async Function Only_The_First_Reconnect_After_A_Clean_End_Asks_The_Question() As Task
        ' Clean end, then a 502 (a reconnect that was neither refused nor 404/410), then a 404: the 404 is the SECOND attempt, so it is
        ' the ordinary "not found", not an end.
        Using device As New LiveMockDevice(Function(c, t)
                                               If c.Sequence = 1 Then Return LiveMockDevice.PhoneChunked(c, 3, 0, t)
                                               If c.Sequence = 2 Then
                                                   LiveMockDevice.Status(c, 502, "Bad Gateway")
                                               Else
                                                   LiveMockDevice.Status(c, 404, "Not Found")
                                               End If
                                               Return Task.CompletedTask
                                           End Function)
            Dim policy As New LiveReconnectPolicy()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, New EndHost(), Quick(), delayAsync:=AddressOf NoDelay)

            Assert.Equal(LiveRunEnd.Failed, Await RunToEndAsync(runner))

            Assert.Equal(LiveTerminationKind.NotFoundOrGone, policy.TerminationReason)
            Assert.Equal(3, device.AcceptedCount)
        End Using
    End Function

    ''' <summary>
    ''' The producer's server is stopped, so the port is closed and the reconnect is refused (item T: "a producer that stops its server
    ''' leaves the port closed"). The device is closed from inside the loop's own delay, which runs between the clean end and the
    ''' reconnect. A closed loopback port can take about two seconds to refuse on Windows, hence the long connect timeout.
    ''' </summary>
    <Fact>
    Public Async Function A_Clean_End_Followed_By_A_Refused_Connection_Is_The_Ended_State() As Task
        Dim device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 3, 0, t))
        Dim closed As Integer = 0
        Try
            Dim policy As New LiveReconnectPolicy()
            Dim options As New LivePlaybackOptions With {.ConnectTimeoutMs = 8000, .StallTimeoutMs = 30000, .WatchdogPeriodMs = 50}
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, New EndHost(), options,
                delayAsync:=Function(ms As Integer, token As CancellationToken) As Task
                                If Interlocked.Exchange(closed, 1) = 0 Then device.Dispose()
                                token.ThrowIfCancellationRequested()
                                Return Task.CompletedTask
                            End Function)

            Assert.Equal(LiveRunEnd.BroadcastEnded, Await RunToEndAsync(runner))

            Assert.Equal(LiveTerminationKind.BroadcastEnded, policy.TerminationReason)
            Assert.Equal(1, device.AcceptedCount)
        Finally
            If Interlocked.Exchange(closed, 1) = 0 Then device.Dispose()
        End Try
    End Function

    ' ------------------------------------------------------------------ item U, the tunnel's answer

    <Theory>
    <InlineData("ended", "Ended")>
    <InlineData("unavailable", "NetworkError")>
    <InlineData("some-new-reason", "NetworkError")>
    Public Async Function A_Tunnel_Connect_Refused_Ended_Is_The_Ended_Outcome_And_Nothing_Else_Is(reason As String, expectedName As String) As Task
        Dim expected As LiveOpenOutcome = DirectCast([Enum].Parse(GetType(LiveOpenOutcome), expectedName), LiveOpenOutcome)
        Using identity As New ExchangeSealedTunnel(), server As New ExchangeStubTlsServer(
            Async Function(connection)
                Await connection.ReadAsync()
                Await connection.Refuse(reason)
            End Function)
            Dim endpoint As New LiveBroadcastEndpoint With {.Url = "fmsx://" & server.Address & "/b/abc/http", .Transport = "TUNNEL",
                .Inner = "http://192.0.2.1:8768/live.ts", .ModeValue = LiveBroadcastMode.VideoOnly, .CertFingerprint = server.Fingerprint,
                .IsSealed = True, .SealVersion = 1, .SealPin = identity.Pin}
            Using session As New LiveHttpPlaybackSession(endpoint, New LivePlaybackOptions With {.ConnectTimeoutMs = 5000}),
                  cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Dim result As LiveOpenResult = Await session.OpenAsync(cts.Token)

                Assert.Equal(expected, result.Outcome)
                Assert.Equal(expected = LiveOpenOutcome.Ended, result.IsFatal)
                Assert.Equal(expected <> LiveOpenOutcome.Ended, result.IsRetryable)
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ the pure parts

    <Theory>
    <InlineData("Ended", 0, False, False, True)>
    <InlineData("Ended", 0, True, True, True)>
    <InlineData("NotFoundOrGone", 410, False, False, True)>
    <InlineData("NotFoundOrGone", 410, True, False, True)>
    <InlineData("NotFoundOrGone", 404, False, False, False)>
    <InlineData("NotFoundOrGone", 404, True, False, True)>
    <InlineData("Refused", 0, False, True, False)>
    <InlineData("Refused", 0, True, True, True)>
    <InlineData("Refused", 0, True, False, False)>
    <InlineData("NetworkError", 0, True, True, False)>
    <InlineData("TimedOut", 0, True, False, False)>
    <InlineData("HttpRetryable", 502, True, False, False)>
    <InlineData("AccessDenied", 401, True, False, False)>
    <InlineData("MaxListeners503", 503, True, False, False)>
    <InlineData("HttpRefused", 302, True, False, False)>
    Public Sub An_Attempt_Is_An_End_Only_By_The_Rows_Of_The_Contract(outcomeName As String, status As Integer, afterCleanEnd As Boolean,
                                                                        everyCandidateRefused As Boolean, expected As Boolean)
        Dim outcome As LiveOpenOutcome = DirectCast([Enum].Parse(GetType(LiveOpenOutcome), outcomeName), LiveOpenOutcome)

        Assert.Equal(expected, LiveBroadcastRunner.IsDeliberateEnd(New LiveOpenResult(outcome, status), afterCleanEnd, everyCandidateRefused))
    End Sub

    <Fact>
    Public Sub A_Refused_Socket_Is_Found_Anywhere_In_The_Exception_Chain()
        Dim refused As New SocketException(CInt(SocketError.ConnectionRefused))

        Assert.True(LiveHttpPlaybackSession.IsConnectionRefused(refused))
        Assert.True(LiveHttpPlaybackSession.IsConnectionRefused(New HttpRequestException("connect", refused)))
        Assert.True(LiveHttpPlaybackSession.IsConnectionRefused(New HttpRequestException("connect", New IOException("io", refused))))
    End Sub

    <Fact>
    Public Sub Other_Failures_Are_Not_A_Refused_Connection()
        Assert.False(LiveHttpPlaybackSession.IsConnectionRefused(New HttpRequestException("connect")))
        Assert.False(LiveHttpPlaybackSession.IsConnectionRefused(New HttpRequestException("connect", New SocketException(CInt(SocketError.TimedOut)))))
        Assert.False(LiveHttpPlaybackSession.IsConnectionRefused(New HttpRequestException("connect", New SocketException(CInt(SocketError.ConnectionReset)))))
        Assert.False(LiveHttpPlaybackSession.IsConnectionRefused(New IOException("Broadcast tunnel refused: Refused")))
    End Sub

    <Fact>
    Public Sub Ended_Is_Final_And_Refused_Is_Worth_Another_Try()
        Dim ended As New LiveOpenResult(LiveOpenOutcome.Ended)
        Assert.True(ended.IsFatal)
        Assert.False(ended.IsRetryable)
        Assert.Equal(LiveTerminationKind.BroadcastEnded, ended.Termination)

        Dim refused As New LiveOpenResult(LiveOpenOutcome.Refused)
        Assert.False(refused.IsFatal)
        Assert.True(refused.IsRetryable)
        Assert.Equal(LiveTerminationKind.None, refused.Termination)
    End Sub

    <Fact>
    Public Sub The_Ended_State_Has_Its_Own_Words_And_Is_Not_A_Failure()
        Dim policy As New LiveReconnectPolicy()
        policy.NoteBroadcastEnded()

        Assert.Equal(LiveSessionState.Ended, policy.CurrentState)
        Assert.Equal(LiveTerminationKind.BroadcastEnded, policy.TerminationReason)
        Assert.Equal(Localization.T("Трансляция завершена"), policy.GetTerminalStatusText())
        Assert.NotEqual(Localization.T("Трансляция завершена или устройство недоступно"), policy.GetTerminalStatusText())
    End Sub

End Class
#End If
