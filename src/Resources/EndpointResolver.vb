#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>Time the resolver reads and waits on - injectable so the budgets of 100 §15.3 are
''' asserted in tests without a real wait. NowMs is monotonic.</summary>
Friend Interface IResolutionClock
    ReadOnly Property NowMs As Long
    Function Delay(ms As Integer, ct As CancellationToken) As Task
End Interface

Friend NotInheritable Class SystemResolutionClock
    Implements IResolutionClock

    Public ReadOnly Property NowMs As Long Implements IResolutionClock.NowMs
        Get
            Return MonotonicClock.NowMs()
        End Get
    End Property

    Public Function Delay(ms As Integer, ct As CancellationToken) As Task Implements IResolutionClock.Delay
        Return Task.Delay(ms, ct)
    End Function
End Class

''' <summary>The Windows resource limits of 100 §15.3 - Windows defaults, not wire limits and
''' not Android constants. Changing one needs a documented reason and a boundary test.</summary>
Friend Module ResolutionLimits
    Friend Const MaxCandidates As Integer = 16
    Friend Const MaxLanes As Integer = 2
    Friend Const CandidateMs As Integer = 5000
    Friend Const RoundMs As Integer = 45000
    ''' <summary>Initial round + 3 retries, waiting these before each retry.</summary>
    Friend ReadOnly BackoffMs As Integer() = {1000, 4000, 10000}
    Friend Const ReconnectCeilingMs As Integer = 195000
End Module

''' <summary>A dial that ended: a VERIFIED SSH session (host key matched or accepted, before
''' authentication) or a failure. TCP reachability alone is never a success.</summary>
Friend NotInheritable Class DialResult
    Private Sub New(session As IDisposable, failure As ResourceFailure)
        Me.Session = session
        Me.Failure = failure
    End Sub
    Friend ReadOnly Property Session As IDisposable
    Friend ReadOnly Property Failure As ResourceFailure
    Friend Shared Function Verified(session As IDisposable) As DialResult
        If session Is Nothing Then Throw New ArgumentNullException(NameOf(session))
        Return New DialResult(session, Nothing)
    End Function
    Friend Shared Function Failed(failure As ResourceFailure) As DialResult
        Return New DialResult(Nothing, If(failure, New ResourceFailure(ResourceFailureKind.Transport)))
    End Function
End Class

Friend Interface IEndpointDialer
    ''' <summary>Connects, verifies the host key through the server's <see cref="HostKeyGate"/>
    ''' and authenticates. Must honour <paramref name="ct"/> where the library allows; a
    ''' library that cannot is allowed to keep running - its late result is discarded.</summary>
    Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult)
End Interface

Friend NotInheritable Class EndpointAttempt
    Friend Sub New(endpoint As FmsEndpoint, failure As ResourceFailure)
        Me.Endpoint = endpoint
        Me.Failure = failure
    End Sub
    Friend ReadOnly Property Endpoint As FmsEndpoint
    ''' <summary>Nothing for the attempt that won.</summary>
    Friend ReadOnly Property Failure As ResourceFailure
End Class

Friend NotInheritable Class ResolutionOutcome
    Friend Sub New(session As IDisposable, endpoint As FmsEndpoint, failure As ResourceFailure, attempts As IReadOnlyList(Of EndpointAttempt))
        Me.Session = session
        Me.Endpoint = endpoint
        Me.Failure = failure
        Me.Attempts = attempts
    End Sub
    Friend ReadOnly Property Session As IDisposable
    Friend ReadOnly Property Endpoint As FmsEndpoint
    Friend ReadOnly Property Failure As ResourceFailure
    Friend ReadOnly Property Attempts As IReadOnlyList(Of EndpointAttempt)
    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Session IsNot Nothing
        End Get
    End Property
End Class

''' <summary>
''' One resolution round (100 §15.3 / §14.1): the recognised candidates, tried in the
''' producer's order in at most two lanes, inside one 45 s deadline with a 5 s budget per
''' candidate. The first VERIFIED session wins and every other attempt is cancelled and
''' disposed; a host-key mismatch or an auth refusal ends the whole round and nothing else
''' is dialled. A call that ignores cancellation keeps its lane until it returns - expiry
''' only suppresses its late result - so a hung library never causes unbounded new dials.
''' Time a user spends answering a trust prompt is excluded from both budgets.
''' </summary>
Friend Module EndpointResolver

    ''' <summary>
    ''' The execution list for one round. Stored endpoints keep the producer's order and are
    ''' de-duplicated by host (case-insensitive) and port. A verified mDNS candidate is tried
    ''' first, de-duplicated, inside the 16-candidate cap without silently dropping a stored
    ''' path: when all 16 slots are stored paths it replaces the stale LAN one only if there
    ''' is exactly one, otherwise discovery is omitted for this round.
    ''' </summary>
    Friend Function PlanCandidates(stored As IEnumerable(Of FmsEndpoint), Optional discovered As FmsEndpoint = Nothing) As List(Of FmsEndpoint)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim list As New List(Of FmsEndpoint)()
        For Each e As FmsEndpoint In If(stored, Enumerable.Empty(Of FmsEndpoint)())
            If e IsNot Nothing AndAlso seen.Add(e.Key()) Then list.Add(e)
            If list.Count >= ResolutionLimits.MaxCandidates Then Exit For
        Next
        If discovered Is Nothing OrElse seen.Contains(discovered.Key()) Then Return list
        If list.Count < ResolutionLimits.MaxCandidates Then
            list.Insert(0, discovered)
        Else
            Dim lanIndexes As List(Of Integer) = Enumerable.Range(0, list.Count).Where(Function(i) String.Equals(list(i).Kind, "lan", StringComparison.Ordinal)).ToList()
            If lanIndexes.Count = 1 Then list(lanIndexes(0)) = discovered
        End If
        Return list
    End Function

    ' --------------------------------------------------------------- round state ----

    Private NotInheritable Class Round
        Friend ReadOnly Gate As New Object()
        Friend ReadOnly Cts As CancellationTokenSource
        Friend ReadOnly Candidates As IReadOnlyList(Of FmsEndpoint)
        Friend ReadOnly Attempts As New List(Of EndpointAttempt)()
        Friend ReadOnly Decision As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Friend NextIndex As Integer
        Friend Decided As Boolean
        Friend WinnerSession As IDisposable
        Friend WinnerEndpoint As FmsEndpoint
        Friend FailureOfRound As ResourceFailure
        ''' <summary>When the round began, on the resolver's clock (for the stagger of <see cref="FmsEndpoint.StartAfterMs"/>).</summary>
        Friend StartMs As Long

        Friend Sub New(candidates As IReadOnlyList(Of FmsEndpoint), cts As CancellationTokenSource)
            Me.Candidates = candidates
            Me.Cts = cts
        End Sub

        ''' <summary>True when every candidate before <paramref name="endpoint"/> has already failed: a staggered candidate then has nothing to
        ''' wait for.</summary>
        Friend Function EveryEarlierFailed(endpoint As FmsEndpoint) As Boolean
            SyncLock Gate
                Dim index As Integer = -1
                For i As Integer = 0 To Candidates.Count - 1
                    If ReferenceEquals(Candidates(i), endpoint) Then
                        index = i
                        Exit For
                    End If
                Next
                If index <= 0 Then Return True
                For i As Integer = 0 To index - 1
                    Dim failed As Boolean = False
                    For Each a As EndpointAttempt In Attempts
                        If ReferenceEquals(a.Endpoint, Candidates(i)) AndAlso a.Failure IsNot Nothing Then
                            failed = True
                            Exit For
                        End If
                    Next
                    If Not failed Then Return False
                Next
                Return True
            End SyncLock
        End Function

        Friend Function TakeNext() As FmsEndpoint
            SyncLock Gate
                If Decided OrElse Cts.IsCancellationRequested OrElse NextIndex >= Candidates.Count Then Return Nothing
                NextIndex += 1
                Return Candidates(NextIndex - 1)
            End SyncLock
        End Function

        Friend Sub Record(endpoint As FmsEndpoint, failure As ResourceFailure)
            SyncLock Gate
                Attempts.Add(New EndpointAttempt(endpoint, failure))
            End SyncLock
        End Sub

        ''' <summary>Settles the round once. Returns False if it was already settled.</summary>
        Friend Function Settle(session As IDisposable, endpoint As FmsEndpoint, failure As ResourceFailure) As Boolean
            SyncLock Gate
                If Decided Then Return False
                Decided = True
                WinnerSession = session
                WinnerEndpoint = endpoint
                FailureOfRound = failure
            End SyncLock
            Decision.TrySetResult(True)
            Try
                Cts.Cancel()
            Catch ex As ObjectDisposedException
            End Try
            Return True
        End Function

        Friend Function LastFailure() As ResourceFailure
            SyncLock Gate
                For i As Integer = Attempts.Count - 1 To 0 Step -1
                    If Attempts(i).Failure IsNot Nothing Then Return Attempts(i).Failure
                Next
            End SyncLock
            Return New ResourceFailure(ResourceFailureKind.Transport, Nothing, "no connection")
        End Function
    End Class

    ''' <summary>Completes once <paramref name="limitMs"/> of budget has been SPENT: wall time
    ''' on the clock minus the consent time that passed meanwhile. Also completes (without
    ''' meaning "expired") when <paramref name="ct"/> is cancelled - the caller knows which.</summary>
    Private Async Function BudgetSpentAsync(clock As IResolutionClock, limitMs As Long, consentMs As Func(Of Long), ct As CancellationToken) As Task
        Dim start As Long = clock.NowMs
        Dim startConsent As Long = consentMs()
        Do
            Dim spent As Long = (clock.NowMs - start) - (consentMs() - startConsent)
            Dim remaining As Long = limitMs - spent
            If remaining <= 0 Then Return
            Try
                Await clock.Delay(CInt(Math.Min(remaining, Integer.MaxValue)), ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            End Try
            If ct.IsCancellationRequested Then Return
        Loop
    End Function

    Private Async Function ObserveAsync(dial As Task(Of DialResult)) As Task(Of DialResult)
        Try
            Dim result As DialResult = Await dial.ConfigureAwait(False)
            Return If(result, DialResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)))
        Catch ex As Exception
            Return DialResult.Failed(ResourceFailure.FromException(ex))
        End Try
    End Function

    ''' <summary>A result that arrives after its attempt was given up on is closed, never used.</summary>
    Private Sub DisposeWhenItArrives(dial As Task(Of DialResult))
        dial.ContinueWith(
            Sub(t As Task(Of DialResult))
                Try
                    GC.KeepAlive(t.Exception) ' observes a fault; never rethrown
                    If t.Status = TaskStatus.RanToCompletion AndAlso t.Result IsNot Nothing AndAlso t.Result.Session IsNot Nothing Then
                        t.Result.Session.Dispose()
                    End If
                Catch ex As Exception
                End Try
            End Sub, TaskScheduler.Default)
    End Sub

    Private Sub Complete(round As Round, endpoint As FmsEndpoint, result As DialResult)
        If result.Session IsNot Nothing Then
            round.Record(endpoint, Nothing)
            If Not round.Settle(result.Session, endpoint, Nothing) Then result.Session.Dispose() ' lost the race
            Return
        End If
        round.Record(endpoint, result.Failure)
        ' A refused key or credential ends the whole round: racing on after it is the one thing
        ' an attacker-in-the-middle would want.
        If result.Failure.IsFatalForAttempt Then round.Settle(Nothing, endpoint, result.Failure)
    End Sub

    Private Async Function AttemptAsync(round As Round, endpoint As FmsEndpoint, dialer As IEndpointDialer,
                                        clock As IResolutionClock, consentMs As Func(Of Long)) As Task
        Dim dialCts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(round.Cts.Token)
        Dim dialTask As Task(Of DialResult)
        Try
            dialTask = dialer.DialAsync(endpoint, dialCts.Token)
        Catch ex As Exception
            round.Record(endpoint, ResourceFailure.FromException(ex))
            dialCts.Cancel()
            Return
        End Try

        Dim budget As Task = BudgetSpentAsync(clock, ResolutionLimits.CandidateMs, consentMs, dialCts.Token)
        Dim first As Task = Await Task.WhenAny(dialTask, budget).ConfigureAwait(False)
        If ReferenceEquals(first, dialTask) Then
            Dim result As DialResult = Await ObserveAsync(dialTask).ConfigureAwait(False)
            dialCts.Cancel() ' frees the budget wait
            Complete(round, endpoint, result)
            Return
        End If

        ' The candidate's budget ran out (or the round ended while it was dialling).
        dialCts.Cancel()
        DisposeWhenItArrives(dialTask)
        If round.Cts.IsCancellationRequested Then Return
        round.Record(endpoint, New ResourceFailure(ResourceFailureKind.Timeout))
        ' The lane stays this call's until it returns - or the round is over - so a library
        ' that ignores cancellation cannot cause a stream of replacement dials.
        Await Task.WhenAny(dialTask, round.Decision.Task).ConfigureAwait(False)
    End Function

    Private Async Function LaneAsync(round As Round, dialer As IEndpointDialer, clock As IResolutionClock,
                                     consentMs As Func(Of Long)) As Task
        Do
            Dim endpoint As FmsEndpoint = round.TakeNext()
            If endpoint Is Nothing Then Return
            If endpoint.StartAfterMs > 0 Then
                Await WaitForStaggerAsync(round, endpoint, clock).ConfigureAwait(False)
                If round.Decided OrElse round.Cts.IsCancellationRequested Then Return
            End If
            Await AttemptAsync(round, endpoint, dialer, clock, consentMs).ConfigureAwait(False)
        Loop
    End Function

    ''' <summary>A staggered candidate (the tunnel of ticket 171) waits for its head-start to pass, and stops waiting early when every earlier
    ''' candidate has failed or the round was decided. Nothing is dialled - and so nothing is opened on the exchange - while a direct address
    ''' is still answering.</summary>
    Private Async Function WaitForStaggerAsync(round As Round, endpoint As FmsEndpoint, clock As IResolutionClock) As Task
        Do
            If round.Decided OrElse round.Cts.IsCancellationRequested Then Return
            Dim remaining As Long = endpoint.StartAfterMs - (clock.NowMs - round.StartMs)
            If remaining <= 0 OrElse round.EveryEarlierFailed(endpoint) Then Return
            Try
                Await clock.Delay(CInt(Math.Min(remaining, 50)), round.Cts.Token).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            End Try
        Loop
    End Function

    ''' <summary>Runs one round. <paramref name="lanes"/> is clamped to 1..2; an UNPINNED server
    ''' passes 1, so only one handshake - and so one trust prompt - is ever in flight.</summary>
    ''' <param name="consentMs">Consent time so far (see <see cref="HostKeyGate.ConsentMsSoFar"/>);
    ''' Nothing when there is no prompt to exclude.</param>
    Friend Async Function ResolveAsync(candidates As IReadOnlyList(Of FmsEndpoint), dialer As IEndpointDialer,
                                       clock As IResolutionClock, ct As CancellationToken,
                                       Optional lanes As Integer = 2,
                                       Optional consentMs As Func(Of Long) = Nothing) As Task(Of ResolutionOutcome)
        Dim list As IReadOnlyList(Of FmsEndpoint) = If(candidates, CType(New List(Of FmsEndpoint)(), IReadOnlyList(Of FmsEndpoint)))
        If list.Count > ResolutionLimits.MaxCandidates Then list = list.Take(ResolutionLimits.MaxCandidates).ToList()
        Dim consent As Func(Of Long) = If(consentMs, Function() 0L)
        Dim round As New Round(list, New CancellationTokenSource())
        round.StartMs = clock.NowMs

        If list.Count = 0 Then
            Return New ResolutionOutcome(Nothing, Nothing, New ResourceFailure(ResourceFailureKind.Transport, Nothing, "no connection"), New List(Of EndpointAttempt)())
        End If

        Dim userCancel As CancellationTokenRegistration = ct.Register(Sub() round.Settle(Nothing, Nothing, New ResourceFailure(ResourceFailureKind.Cancelled)))
        Try
            Dim deadline As Task = BudgetSpentAsync(clock, ResolutionLimits.RoundMs, consent, round.Cts.Token)
            Dim deadlineWatch As Task = deadline.ContinueWith(
                Sub(t As Task)
                    If Not round.Cts.IsCancellationRequested Then
                        round.Settle(Nothing, Nothing, New ResourceFailure(ResourceFailureKind.Timeout, Nothing, "round deadline"))
                    End If
                End Sub, TaskScheduler.Default)

            Dim laneCount As Integer = Math.Max(1, Math.Min(Math.Min(lanes, ResolutionLimits.MaxLanes), list.Count))
            Dim running As New List(Of Task)()
            For i As Integer = 1 To laneCount
                running.Add(LaneAsync(round, dialer, clock, consent))
            Next
            Dim allLanes As Task = Task.WhenAll(running)
            Await Task.WhenAny(round.Decision.Task, allLanes).ConfigureAwait(False)
            If Not round.Decided Then round.Settle(Nothing, Nothing, round.LastFailure())
            Await round.Decision.Task.ConfigureAwait(False)
            GC.KeepAlive(deadlineWatch)

            SyncLock round.Gate
                Return New ResolutionOutcome(round.WinnerSession, round.WinnerEndpoint, round.FailureOfRound, round.Attempts.ToList())
            End SyncLock
        Finally
            userCancel.Dispose()
        End Try
    End Function

End Module

''' <summary>The transparent-reconnect schedule (100 §15.3): the initial round plus three
''' retries after 1, 4 and 10 s, inside a 195 s ceiling. Transport and timeout failures are
''' retried; auth, key and status failures stop at once; the user's cancel aborts a wait.</summary>
Friend Module ReconnectPolicy

    Friend Async Function RunAsync(Of T)(attempt As Func(Of CancellationToken, Task(Of ResourceResult(Of T))),
                                         clock As IResolutionClock, ct As CancellationToken) As Task(Of ResourceResult(Of T))
        Dim start As Long = clock.NowMs
        Dim last As ResourceResult(Of T) = Nothing
        For retry As Integer = 0 To ResolutionLimits.BackoffMs.Length
            If ct.IsCancellationRequested Then Return ResourceResult(Of T).Fail(New ResourceFailure(ResourceFailureKind.Cancelled))
            Try
                last = Await attempt(ct).ConfigureAwait(False)
            Catch ex As Exception
                last = ResourceResult(Of T).Fail(ResourceFailure.FromException(ex))
            End Try
            If last.Succeeded OrElse Not last.Failure.IsRetryable Then Return last
            If retry = ResolutionLimits.BackoffMs.Length Then Exit For
            Dim wait As Integer = ResolutionLimits.BackoffMs(retry)
            If (clock.NowMs - start) + wait >= ResolutionLimits.ReconnectCeilingMs Then Exit For
            Try
                Await clock.Delay(wait, ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return ResourceResult(Of T).Fail(New ResourceFailure(ResourceFailureKind.Cancelled))
            End Try
        Next
        Return last
    End Function

End Module
#End If
