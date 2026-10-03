Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 106 / 105 L01 (O01-O03): the REAL ordering policy of "open this path" -
''' OpenCoordinator over the real scheduler - with the filesystem replaced by calls the test
''' releases in whatever order it likes, and the UI thread replaced by a queue it drains when
''' it chooses. Asserts what the host was asked to do (apply, report), not just a ticket.
''' </summary>
Public Class OpenCoordinationTests

    Private Const Watchdog_Ms As Integer = 15000

    ''' <summary>One path's scripted answer; the call blocks until the test releases it.</summary>
    Private NotInheritable Class FakeFs
        Implements IArgumentFileSystem

        Private ReadOnly gates As New ConcurrentDictionary(Of String, TaskCompletionSource(Of Boolean))()
        Private ReadOnly answers As New ConcurrentDictionary(Of String, Func(Of Boolean))()
        Friend ReadOnly Entered As New SemaphoreSlim(0)
        Friend ReadOnly Calls As New ConcurrentQueue(Of String)()

        Friend Sub Script(path As String, exists As Boolean)
            gates(path) = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            answers(path) = Function() exists
        End Sub

        Friend Sub ScriptFault(path As String)
            gates(path) = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            answers(path) = Function() As Boolean
                                Throw New IOException("the network name is no longer available")
                            End Function
        End Sub

        Friend Sub Release(path As String)
            gates(path).TrySetResult(True)
        End Sub

        Friend Sub WaitEntered(count As Integer)
            For i As Integer = 1 To count
                Assert.True(Entered.Wait(Watchdog_Ms), "a probe that should have started did not")
            Next
        End Sub

        Public Function DirectoryExists(path As String) As Boolean Implements IArgumentFileSystem.DirectoryExists
            Calls.Enqueue(path)
            Entered.Release()
            If Not gates(path).Task.Wait(Watchdog_Ms) Then Throw New TimeoutException("gate")
            Return False
        End Function

        Public Function FileExists(path As String) As Boolean Implements IArgumentFileSystem.FileExists
            Return answers(path)()
        End Function

        Public Sub GetAttributes(path As String) Implements IArgumentFileSystem.GetAttributes
            Throw New FileNotFoundException("gone", path)
        End Sub

        Public Sub Sleep(milliseconds As Integer) Implements IArgumentFileSystem.Sleep
        End Sub
    End Class

    Private NotInheritable Class FakeHost
        Implements IOpenHost

        Friend ReadOnly Posted As New ConcurrentQueue(Of Action)()
        Friend ReadOnly PostSignal As New SemaphoreSlim(0)
        Friend ReadOnly Applied As New List(Of String)()
        Friend ReadOnly Unavailable As New List(Of String)()
        Friend ReadOnly Checking As New List(Of String)()
        Friend ReadOnly Notes As New List(Of String)()
        Friend Closed As Boolean
        Friend Draining As Boolean
        Friend PostThrows As Boolean

        Public ReadOnly Property IsClosed As Boolean Implements IOpenHost.IsClosed
            Get
                Return Closed
            End Get
        End Property

        Public ReadOnly Property IsCloseDraining As Boolean Implements IOpenHost.IsCloseDraining
            Get
                Return Draining
            End Get
        End Property

        Public Sub Post(action As Action) Implements IOpenHost.Post
            If PostThrows Then Throw New ObjectDisposedException("form")
            Posted.Enqueue(action)
            PostSignal.Release()
        End Sub

        Public Sub ShowChecking(request As OpenRequest) Implements IOpenHost.ShowChecking
            Checking.Add(request.Path)
        End Sub

        Public Sub Apply(request As OpenRequest, probe As ArgumentProbe) Implements IOpenHost.Apply
            Applied.Add(request.Kind.ToString() & ":" & request.Path)
        End Sub

        Public Sub ReportUnavailable(request As OpenRequest, status As ProbeStatus) Implements IOpenHost.ReportUnavailable
            Unavailable.Add(request.Path & ":" & status.ToString())
        End Sub

        Public Sub Diagnostic(message As String) Implements IOpenHost.Diagnostic
            SyncLock Notes
                Notes.Add(message)
            End SyncLock
        End Sub

        ''' <summary>Waits until <paramref name="count"/> completions reached the UI queue, then runs them.</summary>
        Friend Sub RunPosted(count As Integer)
            For i As Integer = 1 To count
                Assert.True(PostSignal.Wait(Watchdog_Ms), "a completion that should have been posted was not")
            Next
            Dim action As Action = Nothing
            While Posted.TryDequeue(action)
                action()
            End While
        End Sub
    End Class

    Private NotInheritable Class Rig
        Friend ReadOnly Requests As New OpenRequestSequence()
        Friend ReadOnly Fs As New FakeFs()
        Friend ReadOnly Host As New FakeHost()
        Friend ReadOnly Scheduler As ProbeScheduler(Of ArgumentProbe)
        Friend ReadOnly Coordinator As OpenCoordinator

        Friend Sub New(Optional maxActive As Integer = 2)
            Scheduler = New ProbeScheduler(Of ArgumentProbe)(
                New ProbePolicy With {.MaxActive = maxActive, .MaxPending = 8, .ReplacePendingOfSameOwner = True, .PendingExpiryMs = 30000,
                                      .Delay = Function(ms) New TaskCompletionSource(Of Boolean)().Task})
            Coordinator = New OpenCoordinator(Requests, Scheduler, Host, Fs)
        End Sub
    End Class

    Private Shared Function Done(task As Task(Of OpenOutcome)) As OpenOutcome
        Assert.True(task.Wait(Watchdog_Ms), "the open never settled")
        Return task.Result
    End Function

    <Fact>
    Public Sub A_then_B_finishing_A_first_opens_only_B()
        Dim rig As New Rig()
        rig.Fs.Script("A", True) : rig.Fs.Script("B", True)
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1) ' A is inside the OS call before B supersedes it.
        Dim b As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("B", False)
        rig.Fs.WaitEntered(1)

        rig.Fs.Release("A") : rig.Fs.Release("B")
        rig.Host.RunPosted(2)

        Assert.Equal(OpenOutcome.Superseded, Done(a))
        Assert.Equal(OpenOutcome.Applied, Done(b))
        Assert.Equal(New String() {"Explicit:B"}, rig.Host.Applied.ToArray())
    End Sub

    <Fact>
    Public Sub A_then_B_finishing_B_first_opens_only_B()
        Dim rig As New Rig()
        rig.Fs.Script("A", True) : rig.Fs.Script("B", True)
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1) ' A is inside the OS call before B supersedes it.
        Dim b As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("B", False)
        rig.Fs.WaitEntered(1)

        rig.Fs.Release("B") : rig.Fs.Release("A")
        rig.Host.RunPosted(2)

        Assert.Equal(OpenOutcome.Superseded, Done(a))
        Assert.Equal(OpenOutcome.Applied, Done(b))
        Assert.Equal(New String() {"Explicit:B"}, rig.Host.Applied.ToArray())
    End Sub

    <Fact>
    Public Sub A_then_a_failing_B_reports_Bs_failure_and_never_resurrects_A()
        Dim rig As New Rig()
        rig.Fs.Script("A", True) : rig.Fs.ScriptFault("B")
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1) ' A is inside the OS call before B supersedes it.
        Dim b As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("B", False)
        rig.Fs.WaitEntered(1)

        rig.Fs.Release("A") : rig.Fs.Release("B")
        rig.Host.RunPosted(2)

        Assert.Equal(OpenOutcome.Superseded, Done(a))
        ' B's probe threw: that is B's outcome, reported for B alone - A never comes back.
        Assert.Equal(OpenOutcome.Unavailable, Done(b))
        Assert.Equal(New String() {"B:Faulted"}, rig.Host.Unavailable.ToArray())
        Assert.Empty(rig.Host.Applied)
    End Sub

    <Fact>
    Public Sub A_current_request_with_no_answer_says_so_and_a_stale_one_cannot_overwrite_the_status()
        ' One permit, held by a call stuck on a dead share: B waits behind it.
        Dim rig As New Rig(maxActive:=1)
        rig.Fs.Script("stuck", True) : rig.Fs.Script("B", True) : rig.Fs.Script("C", True)
        Dim stuck As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("stuck", False)
        rig.Fs.WaitEntered(1)
        Dim b As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("B", False)
        Dim c As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("C", False)

        ' B was superseded while queued: it is told so without running and without a status.
        rig.Host.RunPosted(1)
        Assert.Equal(OpenOutcome.Superseded, Done(b))
        Assert.Empty(rig.Host.Unavailable)

        rig.Fs.Release("stuck")
        rig.Fs.WaitEntered(1)
        rig.Fs.Release("C")
        rig.Host.RunPosted(2)
        Assert.Equal(OpenOutcome.Superseded, Done(stuck))
        Assert.Equal(OpenOutcome.Applied, Done(c))
        Assert.Equal(New String() {"Explicit:C"}, rig.Host.Applied.ToArray())
        Assert.DoesNotContain("B", rig.Fs.Calls.ToArray())
    End Sub

    <Fact>
    Public Sub A_current_request_that_expires_in_the_queue_reports_a_timeout_and_opens_no_older_path()
        Dim clock As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim requests As New OpenRequestSequence()
        Dim fs As New FakeFs()
        Dim host As New FakeHost()
        Dim scheduler As New ProbeScheduler(Of ArgumentProbe)(
            New ProbePolicy With {.MaxActive = 1, .MaxPending = 8, .ReplacePendingOfSameOwner = True, .PendingExpiryMs = 30000,
                                  .Delay = Function(ms) clock.Task})
        Dim coordinator As New OpenCoordinator(requests, scheduler, host, fs)
        fs.Script("stuck", True) : fs.Script("B", True)

        Dim stuck As Task(Of OpenOutcome) = coordinator.BeginExplicit("stuck", False)
        fs.WaitEntered(1)
        Dim b As Task(Of OpenOutcome) = coordinator.BeginExplicit("B", False)
        clock.SetResult(True) ' B's 30 s pending budget elapses
        host.RunPosted(1)

        Assert.Equal(OpenOutcome.Unavailable, Done(b))
        Assert.Equal(New String() {"B:TimedOut"}, host.Unavailable.ToArray())
        Assert.Empty(host.Applied)
        fs.Release("stuck")
        GC.KeepAlive(stuck)
    End Sub

    <Fact>
    Public Sub An_explicit_request_accepted_before_the_resume_is_scheduled_declines_the_resume()
        Dim rig As New Rig()
        rig.Fs.Script("explicit", True) : rig.Fs.Script("last", True)
        Dim explicitOpen As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("explicit", False)

        Dim resumed As Task(Of OpenOutcome) = rig.Coordinator.BeginResume("last")
        Assert.Equal(OpenOutcome.ResumeDeclined, Done(resumed))

        rig.Fs.WaitEntered(1)
        rig.Fs.Release("explicit")
        rig.Host.RunPosted(1)
        Assert.Equal(OpenOutcome.Applied, Done(explicitOpen))
        Assert.Equal(New String() {"Explicit:explicit"}, rig.Host.Applied.ToArray())
        Assert.DoesNotContain("last", rig.Fs.Calls.ToArray())
    End Sub

    <Fact>
    Public Sub An_explicit_request_during_the_resume_wins_whichever_probe_returns_first()
        For Each resumeFirst As Boolean In New Boolean() {True, False}
            Dim rig As New Rig()
            rig.Fs.Script("last", True) : rig.Fs.Script("explicit", True)
            Dim resumed As Task(Of OpenOutcome) = rig.Coordinator.BeginResume("last")
            rig.Fs.WaitEntered(1) ' The resume probe is running before the explicit open.
            Dim explicitOpen As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("explicit", False)
            rig.Fs.WaitEntered(1)

            If resumeFirst Then
                rig.Fs.Release("last") : rig.Fs.Release("explicit")
            Else
                rig.Fs.Release("explicit") : rig.Fs.Release("last")
            End If
            rig.Host.RunPosted(2)

            Assert.Equal(OpenOutcome.Superseded, Done(resumed))
            Assert.Equal(OpenOutcome.Applied, Done(explicitOpen))
            Assert.Equal(New String() {"Explicit:explicit"}, rig.Host.Applied.ToArray())
        Next
    End Sub

    <Fact>
    Public Sub A_lone_resume_is_applied_as_a_resume_and_never_reports_a_failure_status_of_its_own_kind()
        Dim rig As New Rig()
        rig.Fs.Script("last", True)
        Dim resumed As Task(Of OpenOutcome) = rig.Coordinator.BeginResume("last")
        rig.Fs.WaitEntered(1)
        rig.Fs.Release("last")
        rig.Host.RunPosted(1)
        Assert.Equal(OpenOutcome.Applied, Done(resumed))
        Assert.Equal(New String() {"LastFile:last"}, rig.Host.Applied.ToArray())
    End Sub

    <Fact>
    Public Sub Navigation_accepted_before_the_display_completes_drops_the_probe_and_blocks_a_later_resume()
        Dim rig As New Rig()
        rig.Fs.Script("A", True)
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1)

        rig.Coordinator.NoteNavigation() ' the navigation handler accepted - it may still fail to display
        rig.Fs.Release("A")
        rig.Host.RunPosted(1)

        Assert.Equal(OpenOutcome.Superseded, Done(a))
        Assert.Empty(rig.Host.Applied)
        Assert.Equal(OpenOutcome.ResumeDeclined, Done(rig.Coordinator.BeginResume("anything")))
    End Sub

    <Fact>
    Public Sub A_completion_dispatched_after_the_window_really_closed_changes_nothing()
        Dim rig As New Rig()
        rig.Fs.Script("A", True)
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1)
        rig.Fs.Release("A")
        SpinWait.SpinUntil(Function() Not rig.Host.Posted.IsEmpty, Watchdog_Ms)

        rig.Host.Closed = True
        rig.Coordinator.NoteClosed()
        rig.Host.RunPosted(1)

        Assert.Equal(OpenOutcome.Closed, Done(a))
        Assert.Empty(rig.Host.Applied)
        Assert.Empty(rig.Host.Unavailable)
    End Sub

    <Fact>
    Public Sub A_post_to_a_window_that_is_gone_settles_as_closed_instead_of_hanging()
        Dim rig As New Rig()
        rig.Fs.Script("A", True)
        rig.Host.PostThrows = True
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1)
        rig.Fs.Release("A")
        Assert.Equal(OpenOutcome.Closed, Done(a))
    End Sub

    <Fact>
    Public Sub A_completion_during_a_close_drain_waits_and_a_veto_applies_it_only_if_still_current()
        Dim rig As New Rig()
        rig.Fs.Script("A", True)
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1)
        rig.Host.Draining = True
        rig.Fs.Release("A")
        rig.Host.RunPosted(1)

        Assert.Equal(OpenOutcome.DeferredByClose, Done(a))
        Assert.Empty(rig.Host.Applied)

        rig.Host.Draining = False ' the close was vetoed
        rig.Coordinator.ResumeDeferred()
        Assert.Equal(New String() {"Explicit:A"}, rig.Host.Applied.ToArray())
    End Sub

    <Fact>
    Public Sub A_deferred_completion_that_went_obsolete_during_the_drain_does_nothing_after_the_veto()
        Dim rig As New Rig()
        rig.Fs.Script("A", True)
        Dim a As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("A", False)
        rig.Fs.WaitEntered(1)
        rig.Host.Draining = True
        rig.Fs.Release("A")
        rig.Host.RunPosted(1)
        Assert.Equal(OpenOutcome.DeferredByClose, Done(a))

        rig.Coordinator.NoteNavigation()
        rig.Host.Draining = False
        rig.Coordinator.ResumeDeferred()
        Assert.Empty(rig.Host.Applied)
    End Sub

    <Fact>
    Public Sub An_unlock_continuation_keeps_its_ticket_and_cannot_outrank_a_newer_request()
        Dim rig As New Rig()
        rig.Fs.Script("locked", True) : rig.Fs.Script("newer", True)
        Dim first As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("locked", False)
        rig.Fs.WaitEntered(1)
        rig.Fs.Release("locked")
        rig.Host.RunPosted(1)
        Done(first)

        Dim newer As Task(Of OpenOutcome) = rig.Coordinator.BeginExplicit("newer", False)
        ' The wait of the first request wakes up after the newer one was accepted:
        Dim stale As Task(Of OpenOutcome) = rig.Coordinator.ContinueUnlocked(1, "locked", False)
        Assert.Equal(OpenOutcome.Superseded, Done(stale))
        rig.Fs.WaitEntered(1)
        rig.Fs.Release("newer")
        rig.Host.RunPosted(1)
        Assert.Equal(OpenOutcome.Applied, Done(newer))
    End Sub

    <Fact>
    Public Sub Obsolete_requests_poll_the_obsolete_callback_so_a_retry_loop_stops()
        Dim sleeps As Integer
        Dim fs As New CountingFs(Sub() Interlocked.Increment(sleeps))
        Dim obsoleteAfter As Integer = 2
        Dim polls As Integer
        Dim result As ArgumentProbe = ArgumentProbing.Probe("x", Function()
                                                                     polls += 1
                                                                     Return polls > obsoleteAfter
                                                                 End Function, fs)
        Assert.False(result.Exists)
        Assert.True(sleeps <= 1) ' it did not run its eight retries
    End Sub

    Private NotInheritable Class CountingFs
        Implements IArgumentFileSystem

        Private ReadOnly onSleep As Action
        Friend Sub New(onSleep As Action)
            Me.onSleep = onSleep
        End Sub
        Public Function DirectoryExists(path As String) As Boolean Implements IArgumentFileSystem.DirectoryExists
            Return False
        End Function
        Public Function FileExists(path As String) As Boolean Implements IArgumentFileSystem.FileExists
            Return False
        End Function
        Public Sub GetAttributes(path As String) Implements IArgumentFileSystem.GetAttributes
            Throw New IOException("share dropped")
        End Sub
        Public Sub Sleep(milliseconds As Integer) Implements IArgumentFileSystem.Sleep
            onSleep()
        End Sub
    End Class

    ' ---------------------------------------------------------------- pending unlock ----

    <Fact>
    Public Sub Two_waits_for_the_same_path_are_two_waits_and_a_stale_capture_owns_nothing()
        Dim wait As New PendingUnlock()
        wait.Begin(1, "C:\a.mp4", 1000)
        Dim older As PendingUnlock.Capture = wait.Snapshot()
        Assert.True(wait.Owns(older))

        wait.Begin(2, "C:\a.mp4", 2000) ' the same path asked for again - a distinct request
        Assert.False(wait.Owns(older))
        Assert.True(wait.Owns(wait.Snapshot()))
    End Sub

    <Fact>
    Public Sub A_cleared_wait_is_owned_by_nobody_and_expires_by_the_monotonic_clock()
        Dim wait As New PendingUnlock()
        wait.Begin(5, "p", 100)
        Dim captured As PendingUnlock.Capture = wait.Snapshot()
        Assert.False(wait.Expired(100))
        Assert.True(wait.Expired(101))
        wait.Clear()
        Assert.False(wait.Owns(captured))
        Assert.False(wait.Expired(10_000))
    End Sub

End Class
