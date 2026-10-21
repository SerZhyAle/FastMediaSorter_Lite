#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Drawing
Imports System.Threading
Imports Xunit

''' <summary>
''' Long animated WEBP, fast open (specification 241), phase B: the pure halves of the two-stage
''' open - which animations are staged, the picture-to-animation side table, the state machine of
''' the stand-in picture and the single-slot runner that prepares the whole animation.
''' No WinForms, no decoder: the runner is driven with a fake job.
''' </summary>
Public Class AnimationUpgradeTests

    Private Shared ReadOnly Wait As TimeSpan = TimeSpan.FromSeconds(10)

    ' ---------------------------------------------------------------------------------------------
    ' AnimationStaging.ShouldStage / IsStaged (rule 9)
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData(960L, 540L, 711L, True)>        ' the measured sample A: 368 Mpx
    <InlineData(64L, 48L, 12L, False)>          ' a small animation keeps today's single step
    <InlineData(1000L, 1000L, 10L, False)>      ' exactly 10 Mpx in total: not over the threshold
    <InlineData(1000L, 1000L, 11L, True)>       ' one frame over it
    <InlineData(1000L, 1000L, 1L, False)>       ' one frame is a still, never staged
    <InlineData(1000L, 1000L, 0L, False)>
    <InlineData(0L, 1000L, 50L, False)>
    <InlineData(1000L, -5L, 50L, False)>
    <InlineData(400000000L, 1L, 2L, True)>      ' a frame at the budget's edge is still shown
    <InlineData(20001L, 20000L, 2L, False)>     ' a frame over the budget is refused, staged or not
    <InlineData(Long.MaxValue, Long.MaxValue, Long.MaxValue, False)> ' a crafted header: no overflow
    <InlineData(Long.MaxValue, 2L, 10L, False)>
    Public Sub ShouldStage_follows_the_threshold(width As Long, height As Long, frames As Long, expected As Boolean)
        Assert.Equal(expected, AnimationStaging.ShouldStage(width, height, frames))
    End Sub

    Private Shared Function Preview(width As Integer, height As Integer, frames As Integer, tooLong As Boolean, Optional isPreview As Boolean = True) As DecodedPayload
        Return New DecodedPayload With {
            .Bytes = New Byte() {1},
            .Kind = DecodedPayloadKind.Png,
            .IsPreview = isPreview,
            .Width = width,
            .Height = height,
            .Frames = frames,
            .IsTooLong = tooLong}
    End Function

    <Fact>
    Public Sub IsStaged_needs_a_preview_that_is_long_or_over_budget()
        Assert.True(AnimationStaging.IsStaged(Preview(960, 540, 711, False)), "over the threshold")
        Assert.True(AnimationStaging.IsStaged(Preview(960, 540, 100000, True)), "over the budget")
        Assert.False(AnimationStaging.IsStaged(Preview(64, 48, 12, False)), "a small animation is opened whole")
        Assert.False(AnimationStaging.IsStaged(Preview(960, 540, 711, False, isPreview:=False)), "a payload that is not a first frame")
        Assert.False(AnimationStaging.IsStaged(Nothing))
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' AnimationStaging side table (rule 10: the preview rides on the picture object)
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_side_table_follows_the_picture_object_and_nothing_else()
        Dim payload As DecodedPayload = Preview(960, 540, 711, False)
        payload.DurationMs = 28440
        payload.HasAlpha = True
        Using picture As New Bitmap(2, 2)
            Using copy As New Bitmap(picture)
                Assert.Null(AnimationStaging.InfoOf(picture))

                AnimationStaging.Mark(picture, New StagedInfo(payload))

                Dim info As StagedInfo = AnimationStaging.InfoOf(picture)
                Assert.NotNull(info)
                Assert.Equal(960, info.Width)
                Assert.Equal(540, info.Height)
                Assert.Equal(711, info.Frames)
                Assert.Equal(28440L, info.DurationMs)
                Assert.True(info.HasAlpha)
                Assert.False(info.IsTooLong)
                Assert.Null(AnimationStaging.InfoOf(copy))      ' a copy is a different picture and knows nothing

                AnimationStaging.Unmark(picture)
                Assert.Null(AnimationStaging.InfoOf(picture))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_side_table_tolerates_nothing()
        AnimationStaging.Mark(Nothing, New StagedInfo(Preview(1, 1, 1, False)))
        Using picture As New Bitmap(2, 2)
            AnimationStaging.Mark(picture, Nothing)
            Assert.Null(AnimationStaging.InfoOf(picture))
        End Using
        Assert.Null(AnimationStaging.InfoOf(Nothing))
        AnimationStaging.Unmark(Nothing)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' AnimationUpgradeState (rule 13)
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_fresh_state_shows_no_preview()
        Dim state As New AnimationUpgradeState()
        Assert.Equal(AnimationUpgradePhase.None, state.Phase)
        Assert.False(state.IsPreviewShown)
        Assert.False(state.TryLand(0))
        Assert.False(state.TryFail(0))
    End Sub

    <Fact>
    Public Sub The_prepared_animation_lands_once_for_the_showing_that_asked()
        Dim state As New AnimationUpgradeState()
        state.ShowPreview(7, "a.webp", tooLong:=False)
        Assert.Equal(AnimationUpgradePhase.Preparing, state.Phase)
        Assert.True(state.IsPreviewShown)
        Assert.Equal("a.webp", state.FilePath)

        Assert.True(state.TryLand(7))
        Assert.Equal(AnimationUpgradePhase.Landed, state.Phase)
        Assert.False(state.IsPreviewShown)
        Assert.False(state.TryLand(7), "a second landing is refused")
        Assert.False(state.TryFail(7), "a landed animation cannot fail afterwards")
    End Sub

    <Fact>
    Public Sub A_result_for_a_file_the_user_has_left_cannot_land()
        Dim state As New AnimationUpgradeState()
        state.ShowPreview(7, "a.webp", tooLong:=False)
        state.ShowPreview(8, "b.webp", tooLong:=False)   ' the next long animation took the screen

        Assert.False(state.TryLand(7), "the old showing's result")
        Assert.False(state.TryFail(7), "the old showing's failure")
        Assert.Equal(AnimationUpgradePhase.Preparing, state.Phase)
        Assert.True(state.TryLand(8))
    End Sub

    <Fact>
    Public Sub A_result_after_leaving_cannot_land()
        Dim state As New AnimationUpgradeState()
        state.ShowPreview(3, "a.webp", tooLong:=False)
        state.Leave()

        Assert.Equal(AnimationUpgradePhase.None, state.Phase)
        Assert.Equal("", state.FilePath)
        Assert.False(state.IsPreviewShown)
        Assert.False(state.TryLand(3))
        Assert.False(state.TryFail(3))
    End Sub

    <Fact>
    Public Sub A_failed_preparation_keeps_the_first_frame_and_never_lands_later()
        Dim state As New AnimationUpgradeState()
        state.ShowPreview(4, "a.webp", tooLong:=False)

        Assert.True(state.TryFail(4))
        Assert.Equal(AnimationUpgradePhase.Failed, state.Phase)
        Assert.True(state.IsPreviewShown, "the first frame is still the picture on screen")
        Assert.False(state.TryLand(4))
        Assert.False(state.TryFail(4))
    End Sub

    <Fact>
    Public Sub A_too_long_animation_keeps_its_first_frame_and_never_lands()
        Dim state As New AnimationUpgradeState()
        state.ShowPreview(5, "long.webp", tooLong:=True)

        Assert.Equal(AnimationUpgradePhase.TooLong, state.Phase)
        Assert.True(state.IsPreviewShown)
        Assert.False(state.TryLand(5))
        Assert.False(state.TryFail(5))
        state.Hold()
        Assert.Equal(AnimationUpgradePhase.TooLong, state.Phase)
        Assert.True(state.IsPreviewShown)
    End Sub

    <Fact>
    Public Sub A_turned_first_frame_is_held_and_the_animation_is_not_swapped_in()
        Dim state As New AnimationUpgradeState()
        state.ShowPreview(6, "a.webp", tooLong:=False)

        state.Hold()
        Assert.Equal(AnimationUpgradePhase.Held, state.Phase)
        Assert.True(state.IsPreviewShown)
        Assert.False(state.TryLand(6), "landing would undo the user's turn")
        Assert.False(state.TryFail(6))
    End Sub

    <Fact>
    Public Sub Hold_changes_nothing_when_no_preparation_is_waiting()
        Dim state As New AnimationUpgradeState()
        state.Hold()
        Assert.Equal(AnimationUpgradePhase.None, state.Phase)

        state.ShowPreview(1, "a.webp", tooLong:=False)
        Assert.True(state.TryLand(1))
        state.Hold()
        Assert.Equal(AnimationUpgradePhase.Landed, state.Phase)
    End Sub

    <Fact>
    Public Sub A_null_path_is_an_empty_one()
        Dim state As New AnimationUpgradeState()
        state.ShowPreview(1, Nothing, tooLong:=False)
        Assert.Equal("", state.FilePath)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' AnimationUpgradeRunner (rule 11)
    ' ---------------------------------------------------------------------------------------------

    Private NotInheritable Class FakeJob
        Friend ReadOnly Id As Integer
        Friend Wanted As Boolean = True
        Friend Sub New(id As Integer)
            Me.Id = id
        End Sub
    End Class

    Private NotInheritable Class FakeResult
        Friend ReadOnly Id As Integer
        Friend ReadOnly ThreadName As String
        Friend Sub New(id As Integer, threadName As String)
            Me.Id = id
            Me.ThreadName = threadName
        End Sub
    End Class

    ''' <summary>One runner with a work function the test can hold, and a record of everything that happened.</summary>
    Private NotInheritable Class Rig
        Friend ReadOnly Runner As AnimationUpgradeRunner(Of FakeJob, FakeResult)
        Friend ReadOnly Ran As New ConcurrentQueue(Of Integer)
        Friend ReadOnly Finished As New ConcurrentQueue(Of Tuple(Of Integer, FakeResult, Exception))
        Friend ReadOnly Started As New SemaphoreSlim(0)
        Friend ReadOnly Release As New SemaphoreSlim(0)
        Friend ReadOnly Done As New SemaphoreSlim(0)
        Friend HoldJobs As Boolean
        Friend ThrowOnId As Integer = -1
        Friend ThrowInFinishedOnId As Integer = -1
        Friend ThrowInWantedOnId As Integer = -1
        Private running As Integer
        Friend MaxRunning As Integer

        Friend Sub New()
            Runner = New AnimationUpgradeRunner(Of FakeJob, FakeResult)(
                AddressOf Work, AddressOf IsWanted, AddressOf OnFinished)
        End Sub

        Private Function Work(job As FakeJob) As FakeResult
            Dim now As Integer = Interlocked.Increment(running)
            Dim seen As Integer
            Do
                seen = MaxRunning
            Loop While now > seen AndAlso Interlocked.CompareExchange(MaxRunning, now, seen) <> seen
            Try
                Ran.Enqueue(job.Id)
                Started.Release()
                If HoldJobs Then Release.Wait(Wait)
                If job.Id = ThrowOnId Then Throw New InvalidOperationException("job " & job.Id.ToString() & " failed")
                Return New FakeResult(job.Id, Thread.CurrentThread.Name)
            Finally
                Interlocked.Decrement(running)
            End Try
        End Function

        Private Function IsWanted(job As FakeJob) As Boolean
            If job.Id = ThrowInWantedOnId Then Throw New InvalidOperationException("isWanted failed")
            Return job.Wanted
        End Function

        Private Sub OnFinished(job As FakeJob, result As FakeResult, failure As Exception)
            Finished.Enqueue(Tuple.Create(job.Id, result, failure))
            Try
                If job.Id = ThrowInFinishedOnId Then Throw New InvalidOperationException("finished failed")
            Finally
                Done.Release()
            End Try
        End Sub

        Friend Function Idle() As Boolean
            Return SpinWait.SpinUntil(Function() Not Runner.IsBusy, Wait)
        End Function
    End Class

    Private Shared Function Drain(queue As ConcurrentQueue(Of Integer)) As Integer()
        Return queue.ToArray()
    End Function

    <Fact>
    Public Sub A_job_runs_on_the_dedicated_worker_and_its_result_reaches_finished()
        Dim rig As New Rig()
        rig.Runner.Submit(New FakeJob(1))

        Assert.True(rig.Done.Wait(Wait))
        Dim item As Tuple(Of Integer, FakeResult, Exception) = Nothing
        Assert.True(rig.Finished.TryDequeue(item))
        Assert.Equal(1, item.Item1)
        Assert.NotNull(item.Item2)
        Assert.Null(item.Item3)
        Assert.Equal("FMS animation upgrade", item.Item2.ThreadName)
        Assert.True(rig.Idle())
    End Sub

    <Fact>
    Public Sub Only_one_job_runs_and_the_newest_waiting_one_wins()
        Dim rig As New Rig() With {.HoldJobs = True}

        rig.Runner.Submit(New FakeJob(1))
        Assert.True(rig.Started.Wait(Wait), "job 1 started")
        rig.Runner.Submit(New FakeJob(2))
        rig.Runner.Submit(New FakeJob(3))
        rig.Runner.Submit(New FakeJob(4))
        Assert.True(rig.Runner.IsBusy)

        rig.Release.Release(10)                      ' let everything that will run go
        Assert.True(rig.Done.Wait(Wait))             ' job 1
        Assert.True(rig.Done.Wait(Wait))             ' job 4, the newest
        Assert.True(rig.Idle())

        Assert.Equal(New Integer() {1, 4}, Drain(rig.Ran))
        Assert.Equal(1, rig.MaxRunning)
    End Sub

    <Fact>
    Public Sub A_waiting_job_nobody_wants_any_more_is_dropped_without_running()
        Dim rig As New Rig() With {.HoldJobs = True}

        rig.Runner.Submit(New FakeJob(1))
        Assert.True(rig.Started.Wait(Wait))
        Dim stale As New FakeJob(2)
        rig.Runner.Submit(stale)
        stale.Wanted = False                          ' the user turned the page meanwhile

        rig.Release.Release(10)
        Assert.True(rig.Done.Wait(Wait))
        Assert.True(rig.Idle())

        Assert.Equal(New Integer() {1}, Drain(rig.Ran))
        Assert.Equal(1, rig.Finished.Count)            ' finished is never called for a dropped job
    End Sub

    <Fact>
    Public Sub A_running_job_is_not_interrupted_when_it_stops_being_wanted()
        Dim rig As New Rig() With {.HoldJobs = True}
        Dim job As New FakeJob(1)

        rig.Runner.Submit(job)
        Assert.True(rig.Started.Wait(Wait))
        job.Wanted = False
        rig.Release.Release(10)

        Assert.True(rig.Done.Wait(Wait), "the job finishes and its result is handed over to be disposed")
        Assert.True(rig.Idle())
        Assert.Equal(1, rig.Finished.Count)
    End Sub

    <Fact>
    Public Sub A_failing_job_reports_its_exception_and_the_worker_goes_on()
        Dim rig As New Rig() With {.ThrowOnId = 1}

        rig.Runner.Submit(New FakeJob(1))
        Assert.True(rig.Done.Wait(Wait))
        Assert.True(rig.Idle())

        Dim item As Tuple(Of Integer, FakeResult, Exception) = Nothing
        Assert.True(rig.Finished.TryDequeue(item))
        Assert.Equal(1, item.Item1)
        Assert.Null(item.Item2)
        Assert.IsType(Of InvalidOperationException)(item.Item3)

        rig.Runner.Submit(New FakeJob(2))
        Assert.True(rig.Done.Wait(Wait), "the next job still runs")
        Assert.True(rig.Idle())
        Assert.Equal(New Integer() {1, 2}, Drain(rig.Ran))
    End Sub

    <Fact>
    Public Sub A_finished_handler_that_throws_does_not_stop_the_queue()
        Dim rig As New Rig() With {.HoldJobs = True, .ThrowInFinishedOnId = 1}

        rig.Runner.Submit(New FakeJob(1))
        Assert.True(rig.Started.Wait(Wait))
        rig.Runner.Submit(New FakeJob(2))
        rig.Release.Release(10)

        Assert.True(rig.Done.Wait(Wait))
        Assert.True(rig.Done.Wait(Wait))
        Assert.True(rig.Idle())
        Assert.Equal(New Integer() {1, 2}, Drain(rig.Ran))
    End Sub

    <Fact>
    Public Sub A_wanted_check_that_throws_drops_the_job_and_the_worker_goes_on()
        Dim rig As New Rig() With {.HoldJobs = True, .ThrowInWantedOnId = 2}

        rig.Runner.Submit(New FakeJob(1))
        Assert.True(rig.Started.Wait(Wait))
        rig.Runner.Submit(New FakeJob(2))
        rig.Release.Release(10)
        Assert.True(rig.Done.Wait(Wait))
        Assert.True(rig.Idle())

        rig.Runner.Submit(New FakeJob(3))
        Assert.True(rig.Done.Wait(Wait))
        Assert.True(rig.Idle())
        Assert.Equal(New Integer() {1, 3}, Drain(rig.Ran))
    End Sub

    <Fact>
    Public Sub A_null_job_is_ignored()
        Dim rig As New Rig()
        rig.Runner.Submit(Nothing)
        Assert.False(rig.Runner.IsBusy)
        Assert.Empty(rig.Ran)
    End Sub

End Class
#End If
