#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' The route core of spec 242 T-6b (src\Video\AnimationVideoRoute.vb), driven with FAKE steps and a REAL store:
''' every failure the route can meet is injected, and the promises of rules 14, 23 and 27 are asserted -
''' one job at a time, the fall-back exactly once and only after the encoder is gone, nothing on a cancel,
''' the plaintext wiped before the player starts, no lease left behind.
''' </summary>
Public Class AnimationVideoRouteTests
    Implements IDisposable

    Private Shared ReadOnly Patience As TimeSpan = TimeSpan.FromSeconds(20)

    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-avroute-test-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly store As AnimationVideoStore
    Private ReadOnly route As New AnimationVideoRoute()

    Public Sub New()
        store = New AnimationVideoStore(root, 4243)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            route.Dispose()
        Catch
        End Try
        Try
            store.Dispose()
        Catch
        End Try
        Try
            If Directory.Exists(root) Then Directory.Delete(root, True)
        Catch
        End Try
    End Sub

    ' --- the rig --------------------------------------------------------------------------------------

    Private Shared Function Gate() As TaskCompletionSource(Of Boolean)
        Return New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
    End Function

    Private Shared Function Request(path As String) As AnimationVideoRequest
        Return New AnimationVideoRequest(path, 7, 25, False, 0, 0, &H202020, 4000)
    End Function

    Private Shared Function Cancelled() As EncodedVideo
        Return New EncodedVideo(AnimationVideoOutcome.Cancelled, Nothing, 0, 0, -1, -1, -1, "cancelled")
    End Function

    ''' <summary>One run's worth of fake steps and everything they saw.</summary>
    Private NotInheritable Class Rig
        Private ReadOnly store As AnimationVideoStore
        Friend ReadOnly Log As New ConcurrentQueue(Of String)()
        Friend ReadOnly FallbackReasons As New ConcurrentQueue(Of String)()
        Friend ReadOnly Percents As New ConcurrentQueue(Of Integer)()
        Friend ReadOnly PlayedPaths As New ConcurrentQueue(Of String)()

        Friend EncodeBody As Func(Of AnimationVideoRequest, IProgress(Of Integer), CancellationToken, Task(Of EncodedVideo))
        Friend SealBody As Func(Of ReadOnlyMemory(Of Byte), CancellationToken, Task(Of AnimationVideoLease))
        Friend PlayBody As Func(Of AnimationVideoRequest, AnimationVideoLease, Action(Of String), Task(Of Boolean))
        Friend FallbackBody As Action(Of AnimationVideoRequest, String)

        Friend EncodeCalls As Integer
        Friend SealCalls As Integer
        Friend PlayCalls As Integer
        Friend EncodersRunning As Integer
        Friend MaxEncodersRunning As Integer
        Friend FallbackSawEncoder As Boolean
        ''' <summary>The array of the last good video this rig made, to see it wiped.</summary>
        Friend Buffer As Byte()
        Friend WipedWhenPlayStarted As Boolean
        Friend OnFailed As Action(Of String)
        Friend LastLease As AnimationVideoLease

        Friend Sub New(store As AnimationVideoStore)
            Me.store = store
            EncodeBody = Function(r, p, t) Task.FromResult(GoodVideo())
            SealBody = Function(b, t) Task.FromResult(store.Seal(b, t))
            PlayBody = Function(r, l, f) Task.FromResult(True)
        End Sub

        Friend Function GoodVideo() As EncodedVideo
            Dim bytes(8191) As Byte
            Call New Random(7).NextBytes(bytes)
            bytes(0) = 1
            Buffer = bytes
            Return New EncodedVideo(AnimationVideoOutcome.Succeeded, bytes, bytes.Length, bytes.Length, 0, 12, 1000, "")
        End Function

        Friend Function Steps() As AnimationVideoRouteSteps
            Return New AnimationVideoRouteSteps(
                AddressOf EncodeAsync, AddressOf SealAsync, AddressOf PlayAsync, AddressOf FallbackNow,
                Sub(message As String) Log.Enqueue(message),
                Sub(percent As Integer) Percents.Enqueue(percent))
        End Function

        Friend ReadOnly Property FailedLines As String()
            Get
                Return Log.Where(Function(l) l.StartsWith("Animation video failed:", StringComparison.Ordinal)).ToArray()
            End Get
        End Property

        Private Async Function EncodeAsync(request As AnimationVideoRequest, progress As IProgress(Of Integer),
                                           token As CancellationToken) As Task(Of EncodedVideo)
            Interlocked.Increment(EncodeCalls)
            Dim running As Integer = Interlocked.Increment(EncodersRunning)
            SyncLock Log
                If running > MaxEncodersRunning Then MaxEncodersRunning = running
            End SyncLock
            Try
                Return Await EncodeBody(request, progress, token).ConfigureAwait(False)
            Finally
                Interlocked.Decrement(EncodersRunning)
            End Try
        End Function

        Private Function SealAsync(bytes As ReadOnlyMemory(Of Byte), token As CancellationToken) As Task(Of AnimationVideoLease)
            Interlocked.Increment(SealCalls)
            Return SealBody(bytes, token)
        End Function

        Private Function PlayAsync(request As AnimationVideoRequest, lease As AnimationVideoLease,
                                   onFailed As Action(Of String)) As Task(Of Boolean)
            Interlocked.Increment(PlayCalls)
            PlayedPaths.Enqueue(request.SourcePath)
            LastLease = lease
            Me.OnFailed = onFailed
            WipedWhenPlayStarted = Buffer IsNot Nothing AndAlso Buffer.All(Function(b) b = 0)
            Return PlayBody(request, lease, onFailed)
        End Function

        Private Sub FallbackNow(request As AnimationVideoRequest, reason As String)
            FallbackReasons.Enqueue(reason)
            If Volatile.Read(EncodersRunning) > 0 Then FallbackSawEncoder = True
            If FallbackBody IsNot Nothing Then FallbackBody(request, reason)
        End Sub
    End Class

    Private Function NewRig() As Rig
        Return New Rig(store)
    End Function

    ' --- the good run -----------------------------------------------------------------------------------

    <Fact>
    Public Async Function A_good_run_seals_wipes_the_plaintext_then_plays_and_never_falls_back() As Task
        Dim rig As Rig = NewRig()

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal(1, rig.EncodeCalls)
        Assert.Equal(1, rig.SealCalls)
        Assert.Equal(1, rig.PlayCalls)
        Assert.Equal({"C:\anim\a.webp"}, rig.PlayedPaths.ToArray())
        Assert.Empty(rig.FallbackReasons)
        Assert.Empty(rig.FailedLines)
        ' rule 27 / header: the plaintext is gone BEFORE the player is asked to start
        Assert.True(rig.WipedWhenPlayStarted)
        ' the lease went to the (fake) surface, which keeps it
        Assert.Equal(1, store.LiveCount)
        Assert.NotNull(rig.LastLease)
        Assert.False(route.IsBusy)
    End Function

    <Fact>
    Public Async Function The_success_line_names_the_path_the_size_and_the_time() As Task
        Dim rig As Rig = NewRig()

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Dim line As String = Assert.Single(rig.Log.Where(Function(l) l.StartsWith("Animation video prepared:", StringComparison.Ordinal)))
        Assert.Matches("^Animation video prepared: C:\\anim\\a\.webp \(\d+ bytes, \d+ ms\)$", line)
        ' the size is the sealed video's: the 8192 bytes of the fake
        Assert.Contains("(8192 bytes,", line, StringComparison.Ordinal)
    End Function

    <Fact>
    Public Async Function Progress_reaches_the_sink_clamped_to_zero_to_one_hundred() As Task
        Dim rig As Rig = NewRig()
        rig.EncodeBody = Function(r, p, t)
                             p.Report(10)
                             p.Report(55)
                             p.Report(150)
                             p.Report(-3)
                             Return Task.FromResult(rig.GoodVideo())
                         End Function

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({10, 55, 100, 0}, rig.Percents.ToArray())
    End Function

    <Fact>
    Public Async Function Progress_after_a_cancel_is_not_forwarded() As Task
        Dim rig As Rig = NewRig()
        Dim started As TaskCompletionSource(Of Boolean) = Gate()
        Dim cancelled As TaskCompletionSource(Of Boolean) = Gate()
        rig.EncodeBody = Async Function(r, p, t)
                             p.Report(20)
                             started.TrySetResult(True)
                             Await cancelled.Task.ConfigureAwait(False)
                             p.Report(30)
                             Return Route_Cancelled()
                         End Function

        Dim job As Task = route.Start(Request("C:\anim\a.webp"), rig.Steps())
        Await started.Task.WaitAsync(Patience)
        Dim stage As Task = route.CancelCurrent()
        cancelled.TrySetResult(True)
        Await stage.WaitAsync(Patience)
        Await job.WaitAsync(Patience)

        Assert.Equal({20}, rig.Percents.ToArray())
    End Function

    Private Shared Function Route_Cancelled() As EncodedVideo
        Return Cancelled()
    End Function

    ' --- encoder failures ---------------------------------------------------------------------------

    <Theory>
    <InlineData("TimedOut", "encode-timeout")>
    <InlineData("OutputTooLarge", "too-large")>
    <InlineData("EncoderFailed", "encoder")>
    <InlineData("EmptyOutput", "empty")>
    Public Async Function An_encoder_that_did_not_succeed_falls_back_once_with_its_reason(outcomeName As String, reason As String) As Task
        Dim outcome As AnimationVideoOutcome = CType([Enum].Parse(GetType(AnimationVideoOutcome), outcomeName), AnimationVideoOutcome)
        Dim rig As Rig = NewRig()
        rig.EncodeBody = Function(r, p, t) Task.FromResult(New EncodedVideo(outcome, Nothing, 0, 12345, 1, 3, 100, "ffmpeg said no"))

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({reason}, rig.FallbackReasons.ToArray())
        Assert.Equal({"Animation video failed: " & reason & ": C:\anim\a.webp"}, rig.FailedLines)
        Assert.Equal(0, rig.SealCalls)
        Assert.Equal(0, rig.PlayCalls)
        Assert.Equal(0, store.LiveCount)
        ' the encoder's own English reason is kept for the log
        Assert.Contains(rig.Log, Function(l) l.Contains("ffmpeg said no", StringComparison.Ordinal))
    End Function

    <Fact>
    Public Async Function An_encoder_that_throws_is_the_encoder_reason_and_one_fallback() As Task
        Dim rig As Rig = NewRig()
        rig.EncodeBody = Function(r, p, t) Task.FromException(Of EncodedVideo)(New InvalidOperationException("boom"))

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"encoder"}, rig.FallbackReasons.ToArray())
        Assert.Single(rig.FailedLines)
        Assert.Equal(0, rig.SealCalls)
        Assert.Equal(0, rig.PlayCalls)
    End Function

    <Fact>
    Public Async Function An_encoder_that_returns_nothing_is_the_encoder_reason() As Task
        Dim rig As Rig = NewRig()
        rig.EncodeBody = Function(r, p, t) Task.FromResult(CType(Nothing, EncodedVideo))

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"encoder"}, rig.FallbackReasons.ToArray())
    End Function

    <Fact>
    Public Async Function A_cancelled_outcome_the_route_did_not_ask_for_is_a_failure_not_a_silent_leave() As Task
        Dim rig As Rig = NewRig()
        rig.EncodeBody = Function(r, p, t) Task.FromResult(Cancelled())

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"encoder"}, rig.FallbackReasons.ToArray())
    End Function

    ' --- seal failures ------------------------------------------------------------------------------

    <Fact>
    Public Async Function A_seal_that_throws_wipes_the_plaintext_and_falls_back_once() As Task
        Dim rig As Rig = NewRig()
        rig.SealBody = Function(b, t) Task.FromException(Of AnimationVideoLease)(New IOException("disk full"))

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"seal"}, rig.FallbackReasons.ToArray())
        Assert.Equal({"Animation video failed: seal: C:\anim\a.webp"}, rig.FailedLines)
        Assert.True(rig.Buffer.All(Function(b) b = 0))
        Assert.Equal(0, rig.PlayCalls)
        Assert.Equal(0, store.LiveCount)
        ' the exception text is not what the log says about the failure itself
        Assert.DoesNotContain(rig.FailedLines, Function(l) l.Contains("disk full", StringComparison.Ordinal))
    End Function

    <Fact>
    Public Async Function A_seal_that_returns_nothing_is_a_seal_failure() As Task
        Dim rig As Rig = NewRig()
        rig.SealBody = Function(b, t) Task.FromResult(CType(Nothing, AnimationVideoLease))

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"seal"}, rig.FallbackReasons.ToArray())
        Assert.Equal(0, rig.PlayCalls)
    End Function

    <Fact>
    Public Async Function A_seal_that_reports_cancellation_without_a_cancel_is_a_failure() As Task
        Dim rig As Rig = NewRig()
        rig.SealBody = Function(b, t) Task.FromException(Of AnimationVideoLease)(New OperationCanceledException())

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"seal"}, rig.FallbackReasons.ToArray())
    End Function

    <Fact>
    Public Async Function The_real_stores_ceiling_is_a_seal_failure_not_a_crash() As Task
        Dim rig As Rig = NewRig()
        ' two leases already held: the third seal is refused by the store
        Dim bytes As Byte() = {1, 2, 3, 4, 5, 6, 7, 8}
        Dim held1 As AnimationVideoLease = store.Seal(bytes)
        Dim held2 As AnimationVideoLease = store.Seal(bytes)
        Try
            Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

            Assert.Equal({"seal"}, rig.FallbackReasons.ToArray())
            Assert.Equal(0, rig.PlayCalls)
            Assert.Equal(2, store.LiveCount)
        Finally
            held1.Dispose()
            held2.Dispose()
        End Try
    End Function

    ' --- the player -----------------------------------------------------------------------------------

    <Fact>
    Public Async Function A_player_that_reports_a_failure_falls_back_once_without_a_second_log_line() As Task
        Dim rig As Rig = NewRig()
        rig.PlayBody = Function(r, lease, onFailed)
                           lease.Dispose()
                           onFailed("vlc-open")
                           Return Task.FromResult(False)
                       End Function

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"vlc-open"}, rig.FallbackReasons.ToArray())
        ' the surface wrote that line itself; the route does not repeat it
        Assert.Empty(rig.FailedLines)
        Assert.Equal(0, store.LiveCount)
    End Function

    <Fact>
    Public Async Function A_stale_play_is_neither_a_failure_nor_a_fallback() As Task
        Dim rig As Rig = NewRig()
        rig.PlayBody = Function(r, lease, onFailed)
                           lease.Dispose()
                           Return Task.FromResult(False)
                       End Function

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Empty(rig.FallbackReasons)
        Assert.Empty(rig.FailedLines)
        Assert.Equal(0, store.LiveCount)
    End Function

    <Fact>
    Public Async Function A_play_that_throws_releases_the_lease_and_falls_back_once() As Task
        Dim rig As Rig = NewRig()
        rig.PlayBody = Function(r, lease, onFailed) Task.FromException(Of Boolean)(New InvalidOperationException("no window"))

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"play"}, rig.FallbackReasons.ToArray())
        Assert.Equal({"Animation video failed: play: C:\anim\a.webp"}, rig.FailedLines)
        Assert.Equal(0, store.LiveCount)
    End Function

    <Fact>
    Public Async Function A_play_that_throws_after_taking_the_lease_does_not_break_on_the_second_release() As Task
        Dim rig As Rig = NewRig()
        rig.PlayBody = Function(r, lease, onFailed)
                           lease.Dispose()
                           Throw New InvalidOperationException("late")
                       End Function

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"play"}, rig.FallbackReasons.ToArray())
        Assert.Equal(0, store.LiveCount)
    End Function

    <Fact>
    Public Async Function A_player_that_fails_later_falls_back_once_however_often_it_reports() As Task
        Dim rig As Rig = NewRig()

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)
        Assert.Empty(rig.FallbackReasons)

        rig.OnFailed("vlc")
        rig.OnFailed("vlc")
        rig.OnFailed("timeout")

        Assert.Equal({"vlc"}, rig.FallbackReasons.ToArray())
    End Function

    <Fact>
    Public Async Function A_player_failure_after_the_user_left_does_not_fall_back() As Task
        Dim rig As Rig = NewRig()

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)
        Await route.CancelCurrent().WaitAsync(Patience)
        rig.OnFailed("vlc")

        Assert.Empty(rig.FallbackReasons)
    End Function

    <Fact>
    Public Async Function A_player_failure_after_a_newer_request_does_not_fall_back_the_old_one() As Task
        Dim rig As Rig = NewRig()
        Dim other As Rig = NewRig()

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)
        Dim oldFailed As Action(Of String) = rig.OnFailed
        Await route.Start(Request("C:\anim\b.webp"), other.Steps()).WaitAsync(Patience)
        oldFailed("vlc")

        Assert.Empty(rig.FallbackReasons)
        Assert.Empty(other.FallbackReasons)
    End Function

    ' --- the fall-back itself -----------------------------------------------------------------------

    <Fact>
    Public Async Function A_fallback_that_throws_does_not_escape_and_is_not_called_again() As Task
        Dim rig As Rig = NewRig()
        rig.FallbackBody = Sub(r, reason) Throw New InvalidOperationException("the fall-back broke")
        rig.PlayBody = Function(r, lease, onFailed)
                           lease.Dispose()
                           onFailed("vlc")
                           onFailed("vlc")
                           Return Task.FromResult(False)
                       End Function

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"vlc"}, rig.FallbackReasons.ToArray())
        Assert.Contains(rig.Log, Function(l) l.StartsWith("Animation video fall-back failed:", StringComparison.Ordinal))
    End Function

    <Fact>
    Public Async Function A_fallback_that_throws_after_an_encoder_failure_does_not_fault_the_job() As Task
        Dim rig As Rig = NewRig()
        rig.FallbackBody = Sub(r, reason) Throw New InvalidOperationException("the fall-back broke")
        rig.EncodeBody = Function(r, p, t) Task.FromResult(New EncodedVideo(AnimationVideoOutcome.EncoderFailed, Nothing, 0, 0, 1, -1, -1, "x"))

        Dim job As Task = route.Start(Request("C:\anim\a.webp"), rig.Steps())
        Await job.WaitAsync(Patience)

        Assert.Equal(TaskStatus.RanToCompletion, job.Status)
        Assert.Equal({"encoder"}, rig.FallbackReasons.ToArray())
    End Function

    <Fact>
    Public Async Function The_fallback_runs_only_after_the_encoder_has_ended() As Task
        Dim rig As Rig = NewRig()
        rig.EncodeBody = Async Function(r, p, t)
                             Await Task.Delay(150).ConfigureAwait(False)
                             Return New EncodedVideo(AnimationVideoOutcome.TimedOut, Nothing, 0, 0, -1, -1, -1, "slow")
                         End Function

        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"encode-timeout"}, rig.FallbackReasons.ToArray())
        Assert.False(rig.FallbackSawEncoder)
        Assert.Equal(0, rig.EncodersRunning)
    End Function

    ' --- cancel ---------------------------------------------------------------------------------------

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Async Function A_cancel_during_the_encode_kills_it_and_leaves_nothing(returnsCancelledOutcome As Boolean) As Task
        Dim rig As Rig = NewRig()
        Dim started As TaskCompletionSource(Of Boolean) = Gate()
        rig.EncodeBody = Async Function(r, p, t)
                             started.TrySetResult(True)
                             Try
                                 Await Task.Delay(Timeout.Infinite, t).ConfigureAwait(False)
                             Catch ex As OperationCanceledException
                                 If Not returnsCancelledOutcome Then Throw
                             End Try
                             Await Task.Delay(50).ConfigureAwait(False)
                             Return Cancelled()
                         End Function

        Dim job As Task = route.Start(Request("C:\anim\a.webp"), rig.Steps())
        Await started.Task.WaitAsync(Patience)
        Assert.True(route.IsBusy)

        Await route.CancelCurrent().WaitAsync(Patience)

        ' the task of the cancel completes only when the encoder is really gone
        Assert.Equal(0, rig.EncodersRunning)
        Await job.WaitAsync(Patience)
        Assert.Empty(rig.FallbackReasons)
        Assert.Empty(rig.FailedLines)
        Assert.Equal(0, rig.SealCalls)
        Assert.Equal(0, rig.PlayCalls)
        Assert.Equal(0, store.LiveCount)
        Assert.False(route.IsBusy)
    End Function

    <Fact>
    Public Async Function A_video_that_finished_just_as_the_user_left_is_dropped_and_wiped() As Task
        Dim rig As Rig = NewRig()
        Dim started As TaskCompletionSource(Of Boolean) = Gate()
        Dim release As TaskCompletionSource(Of Boolean) = Gate()
        rig.EncodeBody = Async Function(r, p, t)
                             started.TrySetResult(True)
                             Await release.Task.ConfigureAwait(False)
                             Return rig.GoodVideo()
                         End Function

        Dim job As Task = route.Start(Request("C:\anim\a.webp"), rig.Steps())
        Await started.Task.WaitAsync(Patience)
        Dim stage As Task = route.CancelCurrent()
        release.TrySetResult(True)
        Await stage.WaitAsync(Patience)
        Await job.WaitAsync(Patience)

        Assert.Equal(0, rig.SealCalls)
        Assert.Equal(0, rig.PlayCalls)
        Assert.Empty(rig.FallbackReasons)
        Assert.True(rig.Buffer.All(Function(b) b = 0))
    End Function

    <Fact>
    Public Async Function A_cancel_during_the_seal_leaves_no_lease_and_no_fallback() As Task
        Dim rig As Rig = NewRig()
        Dim sealing As TaskCompletionSource(Of Boolean) = Gate()
        rig.SealBody = Async Function(b, t)
                           sealing.TrySetResult(True)
                           Await Task.Delay(Timeout.Infinite, t).ConfigureAwait(False)
                           Return Nothing
                       End Function

        Dim job As Task = route.Start(Request("C:\anim\a.webp"), rig.Steps())
        Await sealing.Task.WaitAsync(Patience)
        Await route.CancelCurrent().WaitAsync(Patience)
        Await job.WaitAsync(Patience)

        Assert.Empty(rig.FallbackReasons)
        Assert.Empty(rig.FailedLines)
        Assert.Equal(0, rig.PlayCalls)
        Assert.Equal(0, store.LiveCount)
        Assert.True(rig.Buffer.All(Function(b) b = 0))
    End Function

    <Fact>
    Public Async Function A_lease_sealed_after_the_cancel_is_disposed_and_never_played() As Task
        Dim rig As Rig = NewRig()
        Dim sealing As TaskCompletionSource(Of Boolean) = Gate()
        Dim release As TaskCompletionSource(Of Boolean) = Gate()
        rig.SealBody = Async Function(b, t)
                           sealing.TrySetResult(True)
                           Await release.Task.ConfigureAwait(False)
                           Return store.Seal(b)
                       End Function

        Dim job As Task = route.Start(Request("C:\anim\a.webp"), rig.Steps())
        Await sealing.Task.WaitAsync(Patience)
        Dim stage As Task = route.CancelCurrent()
        release.TrySetResult(True)
        Await stage.WaitAsync(Patience)
        Await job.WaitAsync(Patience)

        Assert.Equal(0, rig.PlayCalls)
        Assert.Empty(rig.FallbackReasons)
        Assert.Equal(0, store.LiveCount)
    End Function

    <Fact>
    Public Async Function A_cancel_with_nothing_running_is_harmless() As Task
        Await route.CancelCurrent().WaitAsync(Patience)

        Dim rig As Rig = NewRig()
        Await route.Start(Request("C:\anim\a.webp"), rig.Steps()).WaitAsync(Patience)
        Await route.CancelCurrent().WaitAsync(Patience)
        Await route.CancelCurrent().WaitAsync(Patience)

        Assert.Empty(rig.FallbackReasons)
    End Function

    ' --- one job at a time ----------------------------------------------------------------------------

    <Fact>
    Public Async Function A_new_request_waits_for_the_old_encoder_to_be_gone() As Task
        Dim rig As Rig = NewRig()
        Dim firstStarted As TaskCompletionSource(Of Boolean) = Gate()
        rig.EncodeBody = Async Function(r, p, t)
                             If r.SourcePath.EndsWith("first.webp", StringComparison.Ordinal) Then
                                 firstStarted.TrySetResult(True)
                                 Try
                                     Await Task.Delay(Timeout.Infinite, t).ConfigureAwait(False)
                                 Catch ex As OperationCanceledException
                                 End Try
                                 ' a killed process takes a moment to be gone
                                 Await Task.Delay(150).ConfigureAwait(False)
                                 Return Cancelled()
                             End If
                             Return rig.GoodVideo()
                         End Function

        Dim first As Task = route.Start(Request("C:\a\first.webp"), rig.Steps())
        Await firstStarted.Task.WaitAsync(Patience)
        Dim second As Task = route.Start(Request("C:\a\second.webp"), rig.Steps())
        Await second.WaitAsync(Patience)
        Await first.WaitAsync(Patience)

        Assert.Equal(1, rig.MaxEncodersRunning)
        Assert.Equal(2, rig.EncodeCalls)
        Assert.Equal({"C:\a\second.webp"}, rig.PlayedPaths.ToArray())
        Assert.Empty(rig.FallbackReasons)
        Assert.Equal(1, store.LiveCount)
    End Function

    <Fact>
    Public Async Function A_request_replaced_before_its_turn_never_starts_an_encoder() As Task
        Dim rig As Rig = NewRig()
        Dim firstStarted As TaskCompletionSource(Of Boolean) = Gate()
        rig.EncodeBody = Async Function(r, p, t)
                             If r.SourcePath.EndsWith("first.webp", StringComparison.Ordinal) Then
                                 firstStarted.TrySetResult(True)
                                 Try
                                     Await Task.Delay(Timeout.Infinite, t).ConfigureAwait(False)
                                 Catch ex As OperationCanceledException
                                 End Try
                                 Await Task.Delay(100).ConfigureAwait(False)
                                 Return Cancelled()
                             End If
                             Return rig.GoodVideo()
                         End Function

        Dim first As Task = route.Start(Request("C:\a\first.webp"), rig.Steps())
        Await firstStarted.Task.WaitAsync(Patience)
        Dim second As Task = route.Start(Request("C:\a\second.webp"), rig.Steps())
        Dim third As Task = route.Start(Request("C:\a\third.webp"), rig.Steps())
        Await Task.WhenAll(first, second, third).WaitAsync(Patience)

        Assert.Equal(2, rig.EncodeCalls)
        Assert.Equal(1, rig.MaxEncodersRunning)
        Assert.Equal({"C:\a\third.webp"}, rig.PlayedPaths.ToArray())
        Assert.Empty(rig.FallbackReasons)
    End Function

    <Fact>
    Public Async Function A_new_request_does_not_wait_for_the_old_ones_player() As Task
        Dim rig As Rig = NewRig()
        Dim playing As TaskCompletionSource(Of Boolean) = Gate()
        Dim releasePlay As TaskCompletionSource(Of Boolean) = Gate()
        rig.PlayBody = Async Function(r, lease, onFailed)
                           If r.SourcePath.EndsWith("first.webp", StringComparison.Ordinal) Then
                               playing.TrySetResult(True)
                               Await releasePlay.Task.ConfigureAwait(False)
                               lease.Dispose()
                               Return False
                           End If
                           Return True
                       End Function

        Dim first As Task = route.Start(Request("C:\a\first.webp"), rig.Steps())
        Await playing.Task.WaitAsync(Patience)
        Dim second As Task = route.Start(Request("C:\a\second.webp"), rig.Steps())
        Await second.WaitAsync(Patience)
        Assert.False(first.IsCompleted)

        releasePlay.TrySetResult(True)
        Await first.WaitAsync(Patience)

        Assert.Equal({"C:\a\first.webp", "C:\a\second.webp"}, rig.PlayedPaths.ToArray())
        Assert.Empty(rig.FallbackReasons)
    End Function

    <Fact>
    Public Async Function A_failed_job_does_not_hold_back_the_next_request() As Task
        Dim rig As Rig = NewRig()
        rig.EncodeBody = Function(r, p, t)
                             If r.SourcePath.EndsWith("bad.webp", StringComparison.Ordinal) Then
                                 Return Task.FromException(Of EncodedVideo)(New InvalidOperationException("x"))
                             End If
                             Return Task.FromResult(rig.GoodVideo())
                         End Function

        Await route.Start(Request("C:\a\bad.webp"), rig.Steps()).WaitAsync(Patience)
        Await route.Start(Request("C:\a\good.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal({"encoder"}, rig.FallbackReasons.ToArray())
        Assert.Equal({"C:\a\good.webp"}, rig.PlayedPaths.ToArray())
    End Function

    ' --- the route itself -----------------------------------------------------------------------------

    <Fact>
    Public Async Function A_disposed_route_cancels_the_running_job_and_starts_nothing_new() As Task
        Dim rig As Rig = NewRig()
        Dim started As TaskCompletionSource(Of Boolean) = Gate()
        rig.EncodeBody = Async Function(r, p, t)
                             started.TrySetResult(True)
                             Try
                                 Await Task.Delay(Timeout.Infinite, t).ConfigureAwait(False)
                             Catch ex As OperationCanceledException
                             End Try
                             Return Cancelled()
                         End Function

        Dim job As Task = route.Start(Request("C:\a\a.webp"), rig.Steps())
        Await started.Task.WaitAsync(Patience)
        route.Dispose()
        Await job.WaitAsync(Patience)
        Await route.Start(Request("C:\a\b.webp"), rig.Steps()).WaitAsync(Patience)

        Assert.Equal(1, rig.EncodeCalls)
        Assert.Empty(rig.FallbackReasons)
    End Function

    <Fact>
    Public Sub Start_refuses_missing_arguments()
        Dim rig As Rig = NewRig()
        Assert.Throws(Of ArgumentNullException)(Sub() route.Start(Nothing, rig.Steps()))
        Assert.Throws(Of ArgumentNullException)(Sub() route.Start(Request("C:\a\a.webp"), Nothing))
        Assert.Throws(Of ArgumentNullException)(Function() New AnimationVideoRouteSteps(Nothing, rig.Steps().Seal, rig.Steps().Play, rig.Steps().Fallback))
        Assert.Throws(Of ArgumentException)(Function() New AnimationVideoRequest("", 1, 25, False, 0, 0, 0, 0))
    End Sub

    <Fact>
    Public Sub A_negative_duration_is_unknown()
        Assert.Equal(0, New AnimationVideoRequest("C:\a\a.webp", 1, 25, False, 0, 0, 0, -5).ExpectedDurationMs)
    End Sub

    ' --- the codes ------------------------------------------------------------------------------------

    <Theory>
    <InlineData("encoder", "Converter")>
    <InlineData("empty", "Converter")>
    <InlineData("encode-timeout", "Limit")>
    <InlineData("too-large", "Limit")>
    <InlineData("seal", "Store")>
    <InlineData("play", "Player")>
    <InlineData("vlc-unavailable", "Player")>
    <InlineData("vlc-open", "Player")>
    <InlineData("vlc", "Player")>
    <InlineData("timeout", "Player")>
    <InlineData("no-preview", "Player")>
    <InlineData("vlc-busy", "Player")>
    <InlineData("something-new", "Player")>
    <InlineData("", "Player")>
    Public Sub Every_reason_code_maps_to_one_of_the_four_phrases(reason As String, kind As String)
        Assert.Equal(kind, AnimationVideoRouteRules.ReasonKindOf(reason).ToString())
    End Sub

    <Fact>
    Public Sub The_surface_codes_the_player_can_report_are_all_known_to_the_mapping()
        Dim codes As String() = {
            AnimationVideoSurfaceRules.Reason_Vlc_Unavailable, AnimationVideoSurfaceRules.Reason_Open,
            AnimationVideoSurfaceRules.Reason_Player_Error, AnimationVideoSurfaceRules.Reason_Timeout,
            AnimationVideoSurfaceRules.Reason_No_Preview, AnimationVideoSurfaceRules.Reason_Busy}
        For Each code As String In codes
            Assert.Equal("Player", AnimationVideoRouteRules.ReasonKindOf(code).ToString())
        Next
    End Sub

    <Fact>
    Public Sub The_route_codes_are_distinct_and_the_outcomes_map_onto_them()
        Dim routeCodes As String() = {
            AnimationVideoRouteRules.Reason_Encoder_Failed, AnimationVideoRouteRules.Reason_Encode_Timeout,
            AnimationVideoRouteRules.Reason_Too_Large, AnimationVideoRouteRules.Reason_Empty,
            AnimationVideoRouteRules.Reason_Seal_Failed, AnimationVideoRouteRules.Reason_Play_Failed}
        Assert.Equal(routeCodes.Length, routeCodes.Distinct().Count())

        Assert.Equal("encode-timeout", AnimationVideoRouteRules.ReasonOfOutcome(AnimationVideoOutcome.TimedOut))
        Assert.Equal("too-large", AnimationVideoRouteRules.ReasonOfOutcome(AnimationVideoOutcome.OutputTooLarge))
        Assert.Equal("empty", AnimationVideoRouteRules.ReasonOfOutcome(AnimationVideoOutcome.EmptyOutput))
        Assert.Equal("encoder", AnimationVideoRouteRules.ReasonOfOutcome(AnimationVideoOutcome.EncoderFailed))
        Assert.Equal("encoder", AnimationVideoRouteRules.ReasonOfOutcome(AnimationVideoOutcome.Cancelled))
    End Sub

    ' --- structure ------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_route_core_depends_on_no_form_no_logger_and_no_disk_of_its_own()
        Dim source As String = RouteSource()
        For Each forbidden As String In {"AppFileLogger", "Main_Form", "Common_Module", "File.Write", "FileStream", "Path.GetTempPath", "ex.Message", ".Message"}
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal)
        Next
        ' the fall-back is reached through the one latch only
        Assert.Single(Regex.Matches(source, "\.Fallback\(job\.Request"))
    End Sub

    Private Shared Function RouteSource() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Video", "AnimationVideoRoute.vb")
            If File.Exists(candidate) Then
                ' Code only: the header comments name the glue file on purpose.
                Return String.Join(vbLf, File.ReadAllLines(candidate).Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal)))
            End If
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\Video\AnimationVideoRoute.vb not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
