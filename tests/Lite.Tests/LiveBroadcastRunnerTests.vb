#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 7, 10.2, Phi-L5: the loop that keeps ONE live
' listener alive - one request at a time, the old response closed before the next is opened, a bounded
' budget, a cancel that opens nothing - against a mock device and a recording stand-in for the player.

Public Class LiveBroadcastRunnerTests

    ''' <summary>The player side: records what the runner asks of it, and reads each stream like LibVLC would.</summary>
    Private NotInheritable Class RecordingHost
        Implements ILiveRunnerHost

        Public ReadOnly Events As ConcurrentQueue(Of String)
        Private ReadOnly players As New ConcurrentQueue(Of LiveMockPlayer)()
        Private starts As Integer
        Private stops As Integer

        Public Sub New(events As ConcurrentQueue(Of String))
            Me.Events = events
        End Sub

        Public ReadOnly Property StartCount As Integer
            Get
                Return Volatile.Read(starts)
            End Get
        End Property

        Public ReadOnly Property StopCount As Integer
            Get
                Return Volatile.Read(stops)
            End Get
        End Property

        Public Function StartPlaybackAsync(session As LiveHttpPlaybackSession) As Task Implements ILiveRunnerHost.StartPlaybackAsync
            Interlocked.Increment(starts)
            Events.Enqueue("start")
            players.Enqueue(New LiveMockPlayer(session.Stream))
            Return Task.CompletedTask
        End Function

        Public Async Function StopPlaybackAsync() As Task Implements ILiveRunnerHost.StopPlaybackAsync
            Interlocked.Increment(stops)
            Events.Enqueue("stop")
            ' The stream was closed before this call: every read the "player" had blocked must be released.
            Await Task.WhenAll(players.Select(Function(p) p.Completion)).WaitAsync(TimeSpan.FromSeconds(5))
        End Function

        Public Sub OnState(state As LiveSessionState, retryCount As Integer, elapsedMs As Long) Implements ILiveRunnerHost.OnState
            Events.Enqueue("state:" & state.ToString())
        End Sub
    End Class

    Private Shared Function Quick() As LivePlaybackOptions
        Return New LivePlaybackOptions With {.ConnectTimeoutMs = 500, .StallTimeoutMs = 400, .WatchdogPeriodMs = 50}
    End Function

    <Fact>
    Public Async Function Failed_Lan_Falls_Through_And_Every_Reconnect_Starts_With_Lan() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using lan As LiveMockDevice = ResettingDevice(),
              relay As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 3, 0, t))
            Dim first As LiveBroadcastEndpoint = EndpointOf(lan.Url())
            Dim second As LiveBroadcastEndpoint = EndpointOf(relay.Url())
            Dim tried As New List(Of String)()
            Dim host As New RecordingHost(events)
            Dim runner As New LiveBroadcastRunner(first, New LiveReconnectPolicy(), host, Quick(),
                sessionFactory:=Function(ep, options)
                                    tried.Add(ep.Url)
                                    Return New LiveHttpPlaybackSession(ep, options)
                                End Function,
                delayAsync:=AddressOf NoDelay, endpoints:=New LiveBroadcastEndpoint() {first, second})
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(5))
                Dim running As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount >= 2)
                cts.Cancel()
                Await running.WaitAsync(TimeSpan.FromSeconds(5))
            End Using
            Assert.True(tried.Count >= 4)
            Assert.Equal(New String() {first.Url, second.Url, first.Url, second.Url}, tried.Take(4).ToArray())
        End Using
    End Function

    Private Shared Function EndpointOf(url As String) As LiveBroadcastEndpoint
        Return New LiveBroadcastEndpoint With {.Url = url, .Mode = "AUDIO_ONLY", .ModeValue = LiveBroadcastMode.AudioOnly, .Transport = "HTTP"}
    End Function

    ''' <summary>
    ''' A device that is there and hangs up the moment it has read the request - a fast, deterministic network
    ''' error. (A genuinely closed port on Windows loopback takes about two seconds to be refused.)
    ''' </summary>
    Private Shared Function ResettingDevice() As LiveMockDevice
        Return New LiveMockDevice(Function(c, t)
                                      c.Reset()
                                      Return Task.CompletedTask
                                  End Function)
    End Function

    Private Shared Async Function WaitUntil(condition As Func(Of Boolean), Optional timeoutMs As Integer = 5000) As Task
        Dim watch As Stopwatch = Stopwatch.StartNew()
        While Not condition()
            If watch.ElapsedMilliseconds > timeoutMs Then Throw New TimeoutException("The condition did not hold within " & timeoutMs & " ms.")
            Await Task.Delay(10)
        End While
    End Function

    Private Shared Function IndexOfEvent(events As ConcurrentQueue(Of String), name As String) As Integer
        Return events.ToList().IndexOf(name)
    End Function

    Private Shared Function NoDelay(ms As Integer, token As CancellationToken) As Task
        token.ThrowIfCancellationRequested()
        Return Task.CompletedTask
    End Function

    ''' <summary>
    ''' Creates the runner's sessions and notes, at the moment each new one is created, whether an earlier one is still
    ''' open - the client-side proof that there is never a second request while a first is alive.
    ''' </summary>
    Private NotInheritable Class SessionTracker
        Public ReadOnly Created As New List(Of LiveHttpPlaybackSession)()
        Public OverlapSeen As Boolean

        Public Function Create(endpoint As LiveBroadcastEndpoint, options As LivePlaybackOptions) As LiveHttpPlaybackSession
            SyncLock Created
                If Created.Any(Function(s) Not s.IsDisposed) Then OverlapSeen = True
                Dim session As New LiveHttpPlaybackSession(endpoint, options)
                Created.Add(session)
                Return session
            End SyncLock
        End Function
    End Class

    ''' <summary>A "delay" that costs nothing and records what was asked.</summary>
    Private NotInheritable Class InstantDelays
        Public ReadOnly Asked As New ConcurrentQueue(Of Integer)()
        Public Function Delay(ms As Integer, token As CancellationToken) As Task
            Asked.Enqueue(ms)
            token.ThrowIfCancellationRequested()
            Return Task.CompletedTask
        End Function
    End Class

    <Fact>
    Public Async Function A_Clean_End_Reconnects_To_A_New_Request_After_The_Old_One_Is_Closed() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using device As New LiveMockDevice(Function(c, t)
                                               If c.Sequence = 1 Then Return LiveMockDevice.PhoneChunked(c, 3, 0, t)   ' the body ends: EOF
                                               Return LiveMockDevice.PhoneChunked(c, 100000, 20, t, endCleanly:=False)
                                           End Function, events)
            Dim host As New RecordingHost(events)
            Dim delays As New InstantDelays()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Quick(),
                                                  delayAsync:=AddressOf delays.Delay)
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount >= 2)
                cts.Cancel()
                Assert.Equal(LiveRunEnd.UserStopped, Await run.WaitAsync(TimeSpan.FromSeconds(5)))
            End Using

            Assert.True(device.AcceptedCount >= 2)
            ' The player was stopped (after the old stream was closed) BEFORE the second request reached the device.
            Assert.True(IndexOfEvent(events, "stop") < IndexOfEvent(events, "accept#2"),
                        String.Join(",", events))
            ' The first retry is immediate (backoff 0, 500, 1000, 2000, 4000).
            Assert.Equal(0, delays.Asked.First())
        End Using
    End Function

    <Fact>
    Public Async Function A_Stalled_Stream_Is_Closed_Before_The_Next_Request_So_The_Device_Never_Sees_Two() As Task
        Dim events As New ConcurrentQueue(Of String)()
        ' Connection 1 sends three frames and goes silent, and the device keeps it open until WE hang up -
        ' so "closed#1" is the instant the listener let go.
        Using device As New LiveMockDevice(Async Function(c, t)
                                               If c.Sequence = 1 Then
                                                   Await LiveMockDevice.PhoneChunked(c, 3, 0, t, endCleanly:=False)
                                                   Await LiveMockDevice.UntilPeerCloses(c, t)
                                               Else
                                                   Await LiveMockDevice.PhoneChunked(c, 100000, 20, t, endCleanly:=False)
                                               End If
                                           End Function, events)
            Dim host As New RecordingHost(events)
            Dim tracker As New SessionTracker()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Quick(),
                                                  sessionFactory:=AddressOf tracker.Create,
                                                  delayAsync:=AddressOf NoDelay)
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount >= 2)
                cts.Cancel()
                Await run.WaitAsync(TimeSpan.FromSeconds(5))
            End Using

            ' The client-side truth: when the second session was created the first was already closed. (The device's own
            ' "closed#1" can be logged a moment after the next "accept#2" - it notices a hang-up on its own schedule.)
            Assert.False(tracker.OverlapSeen, "a session was created while another was still open")
            Assert.True(tracker.Created.Count >= 2)
            Assert.True(IndexOfEvent(events, "stop") < IndexOfEvent(events, "accept#2"), String.Join(",", events))
            Await WaitUntil(Function() IndexOfEvent(events, "closed#1") >= 0, 3000)    ' and the device does see the hang-up
        End Using
    End Function

    <Fact>
    Public Async Function Backoff_Is_0_500_1000_2000_4000_Then_The_Run_Fails_After_Five_Retries() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Dim host As New RecordingHost(events)
        Dim delays As New InstantDelays()
        Dim policy As New LiveReconnectPolicy(Function() 0L)     ' a clock that never moves: only the retry count can end it
        Dim tracker As New SessionTracker()
        Using device As LiveMockDevice = ResettingDevice()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, host, Quick(),
                                                  sessionFactory:=AddressOf tracker.Create,
                                                  delayAsync:=AddressOf delays.Delay)

            Dim end1 As LiveRunEnd = Await runner.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20))

            Assert.Equal(LiveRunEnd.Failed, end1)
            Assert.Equal(LiveTerminationKind.BudgetExhausted, policy.TerminationReason)
            Assert.Equal(New Integer() {0, 500, 1000, 2000, 4000}, delays.Asked.ToArray())
            Assert.Equal(6, tracker.Created.Count)    ' the first connect and five retries
            Assert.Equal(6, device.AcceptedCount)     ' one TCP connection each, never the handler's own retry
            Assert.False(tracker.OverlapSeen)
            Assert.Equal(0, host.StartCount)          ' the player was never given a stream
            Assert.Equal(LiveSessionState.Failed, policy.CurrentState)
            ' Sound never started, so the message is the "could not connect" one (section 7).
            Assert.False(policy.HasEverPlayed)
        End Using
    End Function

    <Fact>
    Public Async Function Cancelling_During_A_Backoff_Opens_No_Further_Request() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Dim host As New RecordingHost(events)
        Dim attempts As Integer
        Dim waiting As New TaskCompletionSource(Of Boolean)()
        Using device As LiveMockDevice = ResettingDevice()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Quick(),
                                                  sessionFactory:=Function(e, o)
                                                                      Interlocked.Increment(attempts)
                                                                      Return New LiveHttpPlaybackSession(e, o)
                                                                  End Function,
                                                  delayAsync:=Function(ms, token)
                                                                  waiting.TrySetResult(True)
                                                                  Return Task.Delay(Timeout.Infinite, token)
                                                              End Function)
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10))
                Assert.Equal(1, attempts)
                cts.Cancel()

                Assert.Equal(LiveRunEnd.UserStopped, Await run.WaitAsync(TimeSpan.FromSeconds(5)))
            End Using
            Await Task.Delay(300)
            Assert.Equal(1, attempts)
            Assert.Equal(1, device.AcceptedCount)       ' nothing reached the device after the cancel
            Assert.Equal(LiveSessionState.Ended, runner.Policy.CurrentState)
            Assert.Equal(LiveTerminationKind.UserStopped, runner.Policy.TerminationReason)
        End Using
    End Function

    <Fact>
    Public Async Function A_503_Ends_The_Run_At_Once_With_Exactly_One_Request_And_No_Retry() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using device As New LiveMockDevice(Function(c, t)
                                               LiveMockDevice.WatchBusy(c)
                                               Return Task.CompletedTask
                                           End Function, events)
            Dim host As New RecordingHost(events)
            Dim delays As New InstantDelays()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url("/listen")), New LiveReconnectPolicy(), host, Quick(),
                                                  delayAsync:=AddressOf delays.Delay)

            Dim ended As LiveRunEnd = Await runner.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10))
            Await Task.Delay(300)

            Assert.Equal(LiveRunEnd.Failed, ended)
            Assert.Equal(LiveTerminationKind.MaxListeners503, runner.Policy.TerminationReason)
            Assert.Equal(1, device.AcceptedCount)
            Assert.Empty(delays.Asked)
            Assert.Equal(0, host.StartCount)
        End Using
    End Function

    <Fact>
    Public Async Function Another_Non_Retryable_Status_Ends_The_Run_And_Names_The_Status() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               LiveMockDevice.Status(c, 418, "I'm a teapot")
                                               Return Task.CompletedTask
                                           End Function)
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(),
                                                  New RecordingHost(New ConcurrentQueue(Of String)()), Quick(),
                                                  delayAsync:=AddressOf NoDelay)
            Assert.Equal(LiveRunEnd.Failed, Await runner.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)))
            Assert.Equal(LiveTerminationKind.HttpStatus, runner.Policy.TerminationReason)
            Assert.Equal(418, runner.Policy.HttpStatusCode)
            Assert.Equal(1, device.AcceptedCount)
        End Using
    End Function

    <Fact>
    Public Async Function A_Retryable_Status_Is_Tried_Again_Within_The_Budget() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using device As New LiveMockDevice(Function(c, t)
                                               If c.Sequence = 1 Then
                                                   LiveMockDevice.Status(c, 502, "Bad Gateway")
                                                   Return Task.CompletedTask
                                               End If
                                               Return LiveMockDevice.PhoneChunked(c, 100000, 20, t, endCleanly:=False)
                                           End Function, events)
            Dim host As New RecordingHost(events)
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Quick(),
                                                  delayAsync:=AddressOf NoDelay)
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount = 1)
                cts.Cancel()
                Await run.WaitAsync(TimeSpan.FromSeconds(5))
            End Using
            Assert.Equal(2, device.AcceptedCount)
        End Using
    End Function

    <Fact>
    Public Async Function The_Budget_Is_A_Wall_Clock_Bound_That_Cuts_The_Attempt_In_Flight() As Task
        ' The device accepts and never answers; the per-attempt deadline is far away. Only the outage budget
        ' (here 600 ms) can end this - and it must end the attempt in flight, not merely refuse a new one.
        Using device As New LiveMockDevice(Function(c, t) Task.Delay(Timeout.Infinite, t))
            Dim policy As New LiveReconnectPolicy() With {.TotalBudgetMs = 600}
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, New RecordingHost(New ConcurrentQueue(Of String)()),
                                                  New LivePlaybackOptions With {.ConnectTimeoutMs = 20000},
                                                  delayAsync:=AddressOf NoDelay)
            Dim watch As Stopwatch = Stopwatch.StartNew()
            Dim ended As LiveRunEnd = Await runner.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15))
            watch.Stop()

            Assert.Equal(LiveRunEnd.Failed, ended)
            Assert.Equal(LiveTerminationKind.BudgetExhausted, policy.TerminationReason)
            Assert.True(watch.ElapsedMilliseconds < 4000, "took " & watch.ElapsedMilliseconds & " ms")
        End Using
    End Function

    <Fact>
    Public Async Function The_Budget_Ends_The_Run_Before_Five_Retries_When_Attempts_Are_Slow() As Task
        Dim now As Long
        Dim policy As New LiveReconnectPolicy(Function() Interlocked.Read(now))
        Using device As LiveMockDevice = ResettingDevice()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), policy, New RecordingHost(New ConcurrentQueue(Of String)()), Quick(),
                                                  sessionFactory:=Function(e, o)
                                                                      Interlocked.Add(now, 2000L)        ' each attempt costs a 2 s timeout
                                                                      Return New LiveHttpPlaybackSession(e, o)
                                                                  End Function,
                                                  delayAsync:=Function(ms, token)
                                                                  Interlocked.Add(now, ms)
                                                                  Return Task.CompletedTask
                                                              End Function)
            Assert.Equal(LiveRunEnd.Failed, Await runner.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)))
            Assert.Equal(LiveTerminationKind.BudgetExhausted, policy.TerminationReason)
            Assert.True(policy.RetryCount < LiveReconnectPolicy.MaxRetries, "the 12 s budget must end it first; retries = " & policy.RetryCount)
        End Using
    End Function

    <Fact>
    Public Async Function A_Drift_Rejoin_Closes_The_Old_Connection_Opens_A_New_One_And_Spends_No_Retry() As Task
        Dim events As New ConcurrentQueue(Of String)()
        ' 300 frames (about 7 s of audio) arrive at once and the connection stays up: the player, with only 1 s
        ' played, is 6 s behind what arrived.
        Using device As New LiveMockDevice(Async Function(c, t)
                                               Await LiveMockDevice.PhoneChunked(c, 300, 0, t, endCleanly:=False)
                                               Await LiveMockDevice.UntilPeerCloses(c, t)
                                           End Function, events)
            Dim host As New RecordingHost(events)
            Dim tracker As New SessionTracker()
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Quick(),
                                                  sessionFactory:=AddressOf tracker.Create,
                                                  delayAsync:=AddressOf NoDelay)
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount = 1 AndAlso runner.CurrentSession IsNot Nothing AndAlso runner.CurrentSession.Frames.FrameCount >= 300)

                ' 6.96 s of audio has arrived. The first position the player reports (6.0 s) is a healthy lag of 0.96 s; later
                ' it has fallen back to 5.0 s - the lag has GROWN to 1.96 s, over the limit, and stays there.
                Assert.Equal(LiveDriftAction.None, runner.SampleDrift(100000L, 6000L, True))
                Assert.Equal(LiveDriftAction.None, runner.SampleDrift(106000L, 5000L, True))
                Assert.Equal(LiveDriftAction.Rejoin, runner.SampleDrift(108500L, 5000L, True))

                Await WaitUntil(Function() host.StartCount >= 2)
                Assert.Equal(0, runner.Policy.RetryCount)
                cts.Cancel()
                Await run.WaitAsync(TimeSpan.FromSeconds(5))
            End Using

            Assert.Equal(1, runner.Drift.RejoinCount)
            Assert.False(tracker.OverlapSeen, "the rejoin opened a connection while the old one was still open")
            Assert.True(IndexOfEvent(events, "stop") < IndexOfEvent(events, "accept#2"), String.Join(",", events))
        End Using
    End Function

    <Fact>
    Public Async Function Ten_Seconds_Of_Sound_Make_The_Outage_Over_And_Refill_The_Budget() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using device As New LiveMockDevice(Function(c, t)
                                               If c.Sequence = 1 Then
                                                   LiveMockDevice.Status(c, 502, "Bad Gateway")
                                                   Return Task.CompletedTask
                                               End If
                                               Return LiveMockDevice.PhoneChunked(c, 100000, 20, t, endCleanly:=False)
                                           End Function, events)
            Dim host As New RecordingHost(events)
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Quick(),
                                                  delayAsync:=AddressOf NoDelay)
            runner.StableDelayMs = 200          ' the real value is 10 s
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount = 1)
                Assert.Equal(1, runner.Policy.RetryCount)               ' the 502 cost a retry
                Await WaitUntil(Function() runner.Policy.RetryCount = 0, 3000)
                cts.Cancel()
                Await run.WaitAsync(TimeSpan.FromSeconds(5))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_Player_Reporting_The_End_Starts_A_Reconnect() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 100000, 20, t, endCleanly:=False), events)
            Dim host As New RecordingHost(events)
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Quick(),
                                                  delayAsync:=AddressOf NoDelay)
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount = 1 AndAlso runner.CurrentSession IsNot Nothing AndAlso runner.CurrentSession.Frames.FrameCount > 0)
                runner.NotifyPlayerEnded()
                Await WaitUntil(Function() host.StartCount >= 2)
                cts.Cancel()
                Await run.WaitAsync(TimeSpan.FromSeconds(5))
            End Using
            Assert.True(IndexOfEvent(events, "stop") < IndexOfEvent(events, "accept#2"), String.Join(",", events))
        End Using
    End Function

    <Fact>
    Public Async Function Stopping_Frees_The_Players_Blocked_Read_Before_Anything_Else() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using device As New LiveMockDevice(Async Function(c, t)
                                               Await LiveMockDevice.PhoneChunked(c, 2, 0, t, endCleanly:=False)
                                               Await LiveMockDevice.UntilPeerCloses(c, t)
                                           End Function, events)
            Dim host As New RecordingHost(events)
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host,
                                                  New LivePlaybackOptions With {.StallTimeoutMs = 60000, .WatchdogPeriodMs = 1000})
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() host.StartCount = 1 AndAlso runner.CurrentSession IsNot Nothing AndAlso runner.CurrentSession.Frames.FrameCount = 2)
                Await Task.Delay(200)             ' the "player" is now blocked in a read on a silent stream
                cts.Cancel()
                ' StopPlaybackAsync waits for the player's read to return, and only the closed stream lets it.
                Assert.Equal(LiveRunEnd.UserStopped, Await run.WaitAsync(TimeSpan.FromSeconds(5)))
            End Using
            Assert.Equal(1, host.StopCount)
            Await WaitUntil(Function() IndexOfEvent(events, "closed#1") >= 0, 3000)    ' the device saw the hang-up
        End Using
    End Function

    ''' <summary>
    ''' For a test whose property is an ORDER and not a timeout: the connect and stall budgets are far above
    ''' anything a loaded machine produces. <see cref="Quick"/>'s 400 ms stall window is right for the tests that
    ''' provoke a stall on purpose, and wrong for one that merely needs the stream to be left alone: with the
    ''' thread pool starved by the rest of a full run (a frame is due every 20 ms and the first one can arrive
    ''' late), the watchdog took a slow first byte for a dead stream and the run reported Reconnecting where
    ''' Playing was expected.
    ''' </summary>
    Private Shared Function Patient() As LivePlaybackOptions
        Return New LivePlaybackOptions With {.ConnectTimeoutMs = 30000, .StallTimeoutMs = 30000, .WatchdogPeriodMs = 50}
    End Function

    <Fact>
    Public Async Function States_Are_Reported_In_Order_Connecting_Buffering_Playing() As Task
        Dim events As New ConcurrentQueue(Of String)()
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 100000, 20, t, endCleanly:=False), events)
            Dim host As New RecordingHost(events)
            Dim runner As New LiveBroadcastRunner(EndpointOf(device.Url()), New LiveReconnectPolicy(), host, Patient(),
                                                  delayAsync:=AddressOf NoDelay)
            Using cts As New CancellationTokenSource()
                Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                Await WaitUntil(Function() events.Contains("state:Playing"))
                cts.Cancel()
                Await run.WaitAsync(TimeSpan.FromSeconds(5))
            End Using
            Dim list As List(Of String) = events.Where(Function(e) e.StartsWith("state:", StringComparison.Ordinal)).ToList()
            Assert.Equal("state:Connecting", list(0))
            Assert.Equal("state:Buffering", list(1))
            Assert.Equal("state:Playing", list(2))
            Assert.Equal("state:Ended", list(list.Count - 1))
        End Using
    End Function
End Class
#End If
