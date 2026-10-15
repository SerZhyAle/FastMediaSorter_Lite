#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Threading
Imports System.Threading.Tasks

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 6.2, 6.4, 7 and contract-sync work item 2.
'
' The loop that keeps ONE live listener alive: connect, hand the body to the player, wait for the end,
' close everything, back off, connect again at the live edge. It knows neither WinForms nor LibVLC - the
' player is reached through ILiveRunnerHost - so what the specification demands of it (one request at a
' time, the old response closed before the next is opened, a cancel during a backoff opens nothing, a
' bounded budget) is asserted in Lite.Tests against a mock device.

''' <summary>How a run ended.</summary>
Friend Enum LiveRunEnd
    ''' <summary>Stopped by the caller: Stop, another media, the window closing. Says nothing.</summary>
    UserStopped = 0
    ''' <summary>Over: a refusal no retry would change, or the budget is spent. The policy says which.</summary>
    Failed = 1
End Enum

''' <summary>What the runner needs from the program around it.</summary>
Friend Interface ILiveRunnerHost

    ''' <summary>
    ''' The connection is open. Hand <see cref="LiveHttpPlaybackSession.Stream"/> to the player and start it.
    ''' Called once per connection.
    ''' </summary>
    Function StartPlaybackAsync(session As LiveHttpPlaybackSession) As Task

    ''' <summary>
    ''' Stop the player and wait until it has let go. The session's stream is ALREADY closed when this is
    ''' called, so a read blocked inside the player has been released.
    ''' </summary>
    Function StopPlaybackAsync() As Task

    ''' <summary>A state transition, with the milliseconds since the run began (section 7: logged with a monotonic time).</summary>
    Sub OnState(state As LiveSessionState, retryCount As Integer, elapsedMs As Long)

End Interface

Friend NotInheritable Class LiveBroadcastRunner

    Private Enum EndCause
        Cancelled = 0
        StreamEnded = 1
        PlayerEnded = 2
        Rejoin = 3
    End Enum

    Private ReadOnly endpoint As LiveBroadcastEndpoint
    Private ReadOnly candidates As IReadOnlyList(Of LiveBroadcastEndpoint)
    Private ReadOnly policyValue As LiveReconnectPolicy
    Private ReadOnly host As ILiveRunnerHost
    Private ReadOnly options As LivePlaybackOptions
    Private ReadOnly sessionFactory As Func(Of LiveBroadcastEndpoint, LivePlaybackOptions, LiveHttpPlaybackSession)
    Private ReadOnly delayAsync As Func(Of Integer, CancellationToken, Task)
    Private ReadOnly driftGuard As LiveDriftGuard
    Private ReadOnly watch As Stopwatch = Stopwatch.StartNew()

    Private activeSession As LiveHttpPlaybackSession
    Private lastOpenResult As LiveOpenResult
    Private playerEnded As TaskCompletionSource(Of Boolean)
    Private rejoinRequested As TaskCompletionSource(Of Boolean)

    Public Sub New(endpoint As LiveBroadcastEndpoint, policy As LiveReconnectPolicy, host As ILiveRunnerHost,
                   Optional options As LivePlaybackOptions = Nothing,
                   Optional targetLatencyMs As Long? = Nothing,
                   Optional sessionFactory As Func(Of LiveBroadcastEndpoint, LivePlaybackOptions, LiveHttpPlaybackSession) = Nothing,
                   Optional delayAsync As Func(Of Integer, CancellationToken, Task) = Nothing,
                   Optional endpoints As IReadOnlyList(Of LiveBroadcastEndpoint) = Nothing)
        If endpoint Is Nothing Then Throw New ArgumentNullException(NameOf(endpoint))
        If policy Is Nothing Then Throw New ArgumentNullException(NameOf(policy))
        If host Is Nothing Then Throw New ArgumentNullException(NameOf(host))

        Me.endpoint = endpoint
        Me.candidates = If(endpoints, New LiveBroadcastEndpoint() {endpoint})
        If Me.candidates.Count = 0 Then Throw New ArgumentException("At least one endpoint is required.", NameOf(endpoints))
        Me.policyValue = policy
        Me.host = host
        Me.options = If(options, New LivePlaybackOptions())
        If sessionFactory Is Nothing Then
            Me.sessionFactory = Function(e As LiveBroadcastEndpoint, o As LivePlaybackOptions) New LiveHttpPlaybackSession(e, o)
        Else
            Me.sessionFactory = sessionFactory
        End If
        If delayAsync Is Nothing Then
            Me.delayAsync = Function(ms As Integer, token As CancellationToken) Task.Delay(ms, token)
        Else
            Me.delayAsync = delayAsync
        End If
        Me.driftGuard = New LiveDriftGuard(LivePlaybackOptions.EffectiveTargetMs(targetLatencyMs))
    End Sub

    Public ReadOnly Property Policy As LiveReconnectPolicy
        Get
            Return policyValue
        End Get
    End Property

    Public ReadOnly Property Drift As LiveDriftGuard
        Get
            Return driftGuard
        End Get
    End Property

    ''' <summary>The connection being listened to now, Nothing between connections.</summary>
    Public ReadOnly Property CurrentSession As LiveHttpPlaybackSession
        Get
            Return Volatile.Read(activeSession)
        End Get
    End Property

    ''' <summary>What the last attempt to connect came to (Nothing before the first). For the log: a state alone does not say why.</summary>
    Public ReadOnly Property LastOpen As LiveOpenResult
        Get
            Return Volatile.Read(lastOpenResult)
        End Get
    End Property

    ''' <summary>The player reported the end of its media or an error (EndReached / EncounteredError).</summary>
    Public Sub NotifyPlayerEnded()
        Dim pending As TaskCompletionSource(Of Boolean) = Volatile.Read(playerEnded)
        If pending IsNot Nothing Then pending.TrySetResult(True)
    End Sub

    ''' <summary>Asks the loop to drop the connection and rejoin the live edge at once (no retry is spent).</summary>
    Public Sub RequestRejoin()
        Dim pending As TaskCompletionSource(Of Boolean) = Volatile.Read(rejoinRequested)
        If pending IsNot Nothing Then pending.TrySetResult(True)
    End Sub

    ''' <summary>
    ''' One look at the steady-state delay (called a few times a second by the UI timer). When the guard says
    ''' so, the session rejoins the live edge exactly as a reconnect does; the caller logs the returned action.
    ''' </summary>
    Public Function SampleDrift(playedMs As Long, isPlaying As Boolean) As LiveDriftAction
        Return SampleDrift(watch.ElapsedMilliseconds, playedMs, isPlaying)
    End Function

    ''' <summary>The same with the time given, so a test can walk the guard through minutes in an instant.</summary>
    Friend Function SampleDrift(nowMs As Long, playedMs As Long, isPlaying As Boolean) As LiveDriftAction
        Dim session As LiveHttpPlaybackSession = CurrentSession
        If session Is Nothing OrElse session.IsMpegTs Then Return LiveDriftAction.None
        Dim action As LiveDriftAction = driftGuard.Evaluate(nowMs, session.Frames.DurationMs, playedMs, isPlaying)
        If action = LiveDriftAction.Rejoin Then RequestRejoin()
        Return action
    End Function

    ''' <summary>How long a connection must play before the outage is over and the budget starts afresh (10 s; a test shortens it).</summary>
    Friend Property StableDelayMs As Integer = CInt(LiveReconnectPolicy.StablePlayingResetThresholdMs)

    Private Sub NoteState(state As LiveSessionState)
        host.OnState(state, policyValue.RetryCount, watch.ElapsedMilliseconds)
    End Sub

    ''' <summary>
    ''' Runs until the caller cancels, a refusal ends it, or the budget is spent. Never throws for any of those;
    ''' what it did is in <see cref="Policy"/>. Awaits resume on the caller's context, so the host is always
    ''' called on the thread that started the run.
    ''' </summary>
    Public Async Function RunAsync(cancellationToken As CancellationToken) As Task(Of LiveRunEnd)
        Do
            If cancellationToken.IsCancellationRequested Then Return Stopped()

            policyValue.NoteConnecting()
            NoteState(LiveSessionState.Connecting)

            Dim session As LiveHttpPlaybackSession = Nothing
            Dim result As LiveOpenResult = Nothing
            For index As Integer = 0 To candidates.Count - 1
                session = sessionFactory(candidates(index), options)
                Volatile.Write(activeSession, session)
                Using attempt As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    attempt.CancelAfter(CInt(Math.Min(Integer.MaxValue, Math.Max(1L, policyValue.RemainingBudgetMs))))
                    result = Await session.OpenAsync(attempt.Token)
                End Using
                If result.IsConnected OrElse result.IsFatal OrElse cancellationToken.IsCancellationRequested Then Exit For
                If index < candidates.Count - 1 Then
                    session.Dispose()
                    Volatile.Write(activeSession, Nothing)
                End If
            Next
            Volatile.Write(lastOpenResult, result)

            If cancellationToken.IsCancellationRequested Then
                Await CloseAsync(session)
                Return Stopped()
            End If

            ' The attempt token also carries the outage budget: a cancel that did not come from the caller is the
            ' budget cutting an attempt that never answered - a timeout, to be judged like any other.
            If result.Outcome = LiveOpenOutcome.Cancelled Then result = New LiveOpenResult(LiveOpenOutcome.TimedOut)

            Dim endCause As EndCause = EndCause.StreamEnded
            If result.IsConnected Then
                endCause = Await ListenAsync(session, cancellationToken)
                ' A stop closes the connection too, so the end of the stream can win the race against the
                ' cancellation: whoever asked to stop has the last word.
                If cancellationToken.IsCancellationRequested Then endCause = EndCause.Cancelled
                If endCause = EndCause.Cancelled Then
                    Await CloseAsync(session)
                    Return Stopped()
                End If
                ' Closed BEFORE the player is stopped (its read is blocked inside the stream) and BEFORE the
                ' next connection exists: one request, never two, even in the instant between them.
                Await CloseAsync(session)
                If endCause = EndCause.Rejoin Then
                    policyValue.NoteRejoin()
                    NoteState(LiveSessionState.Reconnecting)
                    Continue Do
                End If
            Else
                Await CloseAsync(session)
                If result.IsFatal Then
                    policyValue.NoteFatalTermination(result.Termination, result.HttpStatus)
                    NoteState(LiveSessionState.Failed)
                    Return LiveRunEnd.Failed
                End If
                If Not result.IsRetryable Then Return Stopped()
            End If

            Dim wait As Integer? = policyValue.GetNextBackoffDelayMs()
            If Not wait.HasValue Then
                NoteState(LiveSessionState.Failed)
                Return LiveRunEnd.Failed
            End If

            NoteState(LiveSessionState.Reconnecting)
            Try
                Await delayAsync(wait.Value, cancellationToken)
            Catch ex As OperationCanceledException
                Return Stopped()
            End Try
        Loop
    End Function

    Private Function Stopped() As LiveRunEnd
        policyValue.NoteUserStopped()
        NoteState(LiveSessionState.Ended)
        Return LiveRunEnd.UserStopped
    End Function

    ''' <summary>Closes the connection first, then stops the player, in that order, and forgets the session.</summary>
    Private Async Function CloseAsync(session As LiveHttpPlaybackSession) As Task
        session.Dispose()
        Volatile.Write(activeSession, Nothing)
        Try
            Await host.StopPlaybackAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Live playback: stopping the player", ex)
        End Try
    End Function

    Private Shared Function CauseOf(ending As Task, session As LiveHttpPlaybackSession,
                                    endedByPlayer As TaskCompletionSource(Of Boolean),
                                    rejoin As TaskCompletionSource(Of Boolean)) As EndCause
        If ending Is rejoin.Task Then Return EndCause.Rejoin
        If ending Is endedByPlayer.Task Then Return EndCause.PlayerEnded
        If ending Is session.Ended Then Return EndCause.StreamEnded
        Return EndCause.Cancelled
    End Function

    ''' <summary>Plays one connection until something ends it.</summary>
    Private Async Function ListenAsync(session As LiveHttpPlaybackSession, cancellationToken As CancellationToken) As Task(Of EndCause)
        Dim endedByPlayer As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim rejoin As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim cancelled As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Volatile.Write(playerEnded, endedByPlayer)
        Volatile.Write(rejoinRequested, rejoin)

        Using registration As CancellationTokenRegistration = cancellationToken.Register(Sub() cancelled.TrySetResult(True))
            Try
                Await host.StartPlaybackAsync(session)
            Catch ex As Exception
                AppFileLogger.LogException("Live playback: starting the player", ex)
                Return EndCause.StreamEnded
            End Try

            ' Connected, nothing heard yet. The watchdog bounds this wait: no first byte within the stall
            ' timeout ends the stream.
            NoteState(LiveSessionState.Buffering)
            Dim firstBytes As Task = session.FirstByte
            Dim phase As Task = Await Task.WhenAny(New Task() {firstBytes, session.Ended, endedByPlayer.Task, rejoin.Task, cancelled.Task})
            If phase IsNot firstBytes Then Return CauseOf(phase, session, endedByPlayer, rejoin)

            policyValue.NotePlaying()
            driftGuard.NoteSessionStarted(watch.ElapsedMilliseconds)
            NoteState(LiveSessionState.Playing)

            Using stable As CancellationTokenSource = New CancellationTokenSource()
                Dim stability As Task = Task.Delay(StableDelayMs, stable.Token)
                Dim ending As Task = Await Task.WhenAny(New Task() {session.Ended, endedByPlayer.Task, rejoin.Task, cancelled.Task, stability})

                If ending Is stability Then
                    ' Ten seconds of sound: the outage is over, the next one starts from a full budget.
                    policyValue.ResetRetries()
                    ending = Await Task.WhenAny(New Task() {session.Ended, endedByPlayer.Task, rejoin.Task, cancelled.Task})
                End If
                stable.Cancel()

                If ending Is cancelled.Task Then Return EndCause.Cancelled
                Return CauseOf(ending, session, endedByPlayer, rejoin)
            End Using
        End Using
    End Function

End Class
#End If
