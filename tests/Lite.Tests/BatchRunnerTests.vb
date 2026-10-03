#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' 004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md PB-3, and acceptance 8-9 in their
''' pure form. The runner is handed a function that puts ONE operation on a queue and is
''' told when it finished, so "cancel at item 12 of 30" and "the destination died at file 18"
''' are exercised with a fake queue that the test completes by hand - no share, no timing.
''' </summary>
Public Class BatchRunnerTests

    ''' <summary>The application's queue, reduced to what the runner can observe.</summary>
    Private NotInheritable Class FakeQueue
        Public ReadOnly Enqueued As New List(Of BatchOp)()
        Public Accept As Boolean = True

        Public Function Enqueue(op As BatchOp) As Boolean
            If Not Accept Then Return False
            Enqueued.Add(op)
            Return True
        End Function

        Public ReadOnly Property InFlight As BatchOp
            Get
                Return If(Enqueued.Count = 0, Nothing, Enqueued(Enqueued.Count - 1))
            End Get
        End Property
    End Class

    Private NotInheritable Class Probe
        Public ReadOnly Queue As New FakeQueue()
        Public ReadOnly Runner As BatchRunner
        Public ReadOnly Progress As New List(Of Integer)()
        Public ReadOnly Finished As New List(Of BatchReport)()
        Public ReadOnly ItemsDone As New List(Of BatchItemResult)()

        Public Sub New()
            Runner = New BatchRunner(AddressOf Queue.Enqueue)
            AddHandler Runner.Progress, Sub(s, e) Progress.Add(e.Index)
            AddHandler Runner.Finished, Sub(s, e) Finished.Add(e.Report)
            AddHandler Runner.ItemFinished, Sub(s, e) ItemsDone.Add(e.Result)
        End Sub

        ''' <summary>The queue reports the operation in flight finished.</summary>
        Public Sub Complete(Optional failure As Exception = Nothing)
            Runner.OpCompleted(Queue.InFlight, failure)
        End Sub
    End Class

    Private Shared Function Ops(count As Integer) As List(Of BatchOp)
        Return Enumerable.Range(1, count).Select(Function(i) New BatchOp With {
            .Kind = FileOpKind.Move,
            .Source = "D:\in\f" & i.ToString("00") & ".jpg",
            .Destination = "E:\sorted\f" & i.ToString("00") & ".jpg",
            .SizeBytes = 10}).ToList()
    End Function

    Private Shared Function Dropped() As Exception
        Return New IOException("The specified network name is no longer available")
    End Function

    ' --------------------------------------------------------------- sequence ----

    <Fact>
    Public Sub Operations_run_one_at_a_time_and_in_order()
        Dim p As New Probe()
        Dim list As List(Of BatchOp) = Ops(5)
        Assert.True(p.Runner.Start(BatchRunKind.Move, list, Nothing, 0))

        For i As Integer = 1 To 5
            Assert.Equal(i, p.Queue.Enqueued.Count)                ' never two in flight
            Assert.Same(list(i - 1), p.Queue.InFlight)
            Assert.True(p.Runner.IsRunning)
            p.Complete()
        Next

        Assert.False(p.Runner.IsRunning)
        Assert.Equal(New Integer() {1, 2, 3, 4, 5}, p.Progress.ToArray())
        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.Equal(5, report.DoneCount)
        Assert.True(report.IsClean)
        Assert.Equal(50L, report.BytesDone)
        Assert.Equal(5, p.ItemsDone.Count)
    End Sub

    <Fact>
    Public Sub A_second_batch_is_refused_while_one_runs()
        Dim p As New Probe()
        Assert.True(p.Runner.Start(BatchRunKind.Move, Ops(3), Nothing, 0))
        Assert.False(p.Runner.Start(BatchRunKind.Copy, Ops(2), Nothing, 0))
        Assert.Equal(1, p.Queue.Enqueued.Count)
        Assert.Equal(3, p.Runner.Total)
    End Sub

    <Fact>
    Public Sub The_runner_is_idle_when_Finished_is_raised_so_a_handler_may_start_the_next_run()
        Dim p As New Probe()
        Dim second As Boolean = False
        AddHandler p.Runner.Finished, Sub(s, e) second = p.Runner.Start(BatchRunKind.Undo, Ops(1), Nothing, 0)
        p.Runner.Start(BatchRunKind.Move, Ops(1), Nothing, 0)
        p.Complete()
        Assert.True(second)
        Assert.True(p.Runner.IsRunning)
    End Sub

    <Fact>
    Public Sub A_completion_that_is_not_the_one_in_flight_is_ignored()
        Dim p As New Probe()
        Dim list As List(Of BatchOp) = Ops(3)
        p.Runner.Start(BatchRunKind.Move, list, Nothing, 0)

        p.Runner.OpCompleted(list(2), Nothing)                         ' not its turn
        p.Runner.OpCompleted(New BatchOp(), Nothing)                   ' not ours at all
        p.Runner.OpCompleted(Nothing, Nothing)
        Assert.Equal(1, p.Queue.Enqueued.Count)
        Assert.Empty(p.ItemsDone)
    End Sub

    <Fact>
    Public Sub A_run_with_no_operations_finishes_at_once_and_reports_its_skips()
        Dim p As New Probe()
        Dim skipped As New List(Of BatchSkip) From {
            New BatchSkip With {.Path = "D:\in\a.jpg", .Reason = SkipReason.AlreadyInDestination}}
        Assert.True(p.Runner.Start(BatchRunKind.Move, New List(Of BatchOp)(), skipped, 0))

        Assert.False(p.Runner.IsRunning)
        Assert.Empty(p.Queue.Enqueued)
        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.Equal(1, report.SkippedCount)
        Assert.Equal(0, report.DoneCount)
        Assert.False(report.IsClean)
    End Sub

    ' --------------------------------------------------------------- failures ----

    ''' <summary>Acceptance 8: one locked file, the batch finishes, the report names it.</summary>
    <Fact>
    Public Sub A_failure_is_recorded_and_the_batch_carries_on()
        Dim p As New Probe()
        Dim list As List(Of BatchOp) = Ops(30)
        p.Runner.Start(BatchRunKind.Move, list, Nothing, 0)

        For i As Integer = 1 To 30
            If i = 12 Then
                p.Complete(New IOException("locked", &H80070020))      ' sharing violation
            Else
                p.Complete()
            End If
        Next

        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.Equal(29, report.DoneCount)
        Assert.Equal(1, report.FailedCount)
        Assert.Equal(0, report.NotStartedCount)
        Dim bad As BatchItemResult = report.Results.Single(Function(r) r.State = BatchItemState.Failed)
        Assert.Equal(list(11).Source, bad.Path)
        Assert.Equal(PathFailureKind.Locked, bad.FailureKind)
        Assert.False(String.IsNullOrEmpty(bad.Detail))
    End Sub

    ''' <summary>The batch-scale version of the rule SlotHealth set for keypresses.</summary>
    <Fact>
    Public Sub Three_transport_failures_in_a_row_abort_the_rest()
        Dim p As New Probe()
        p.Runner.Start(BatchRunKind.Move, Ops(10), Nothing, 0)

        p.Complete()
        p.Complete()
        p.Complete(Dropped())
        p.Complete(Dropped())
        Assert.True(p.Runner.IsRunning)                                 ' two are not yet "gone"
        p.Complete(Dropped())

        Assert.False(p.Runner.IsRunning)
        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.True(report.AbortedForTransport)
        Assert.Equal(2, report.DoneCount)
        Assert.Equal(3, report.FailedCount)
        Assert.Equal(5, report.NotStartedCount)
        Assert.Equal(5, p.Queue.Enqueued.Count)                         ' 6..10 were never handed to the queue
    End Sub

    <Fact>
    Public Sub Transport_failures_that_are_not_consecutive_do_not_abort()
        Dim p As New Probe()
        p.Runner.Start(BatchRunKind.Move, Ops(8), Nothing, 0)
        p.Complete(Dropped())
        p.Complete(Dropped())
        p.Complete()                                                    ' it answered: the streak is over
        p.Complete(Dropped())
        p.Complete(Dropped())
        p.Complete(New IOException("locked", &H80070020))               ' a different failure resets it too
        p.Complete(Dropped())
        p.Complete(Dropped())

        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.False(report.AbortedForTransport)
        Assert.Equal(0, report.NotStartedCount)
        Assert.Equal(7, report.FailedCount)
    End Sub

    <Fact>
    Public Sub An_operation_that_finishes_cleanly_but_did_nothing_is_reported_and_is_not_a_transport_failure()
        Dim p As New Probe()
        Dim list As List(Of BatchOp) = Ops(5)
        p.Runner.Start(BatchRunKind.Undo, list, Nothing, 0)
        p.Complete(Dropped())
        p.Complete(Dropped())
        list(2).Refusal = "the bin was emptied"
        p.Complete()                                                    ' refused, no exception
        p.Complete(Dropped())
        p.Complete(Dropped())

        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.False(report.AbortedForTransport)
        Dim refused As BatchItemResult = report.Results(2)
        Assert.Equal(BatchItemState.Failed, refused.State)
        Assert.Equal("the bin was emptied", refused.Detail)
        Assert.Equal(PathFailureKind.None, refused.FailureKind)
        Assert.Equal(0, report.DoneCount)
    End Sub

    ' ------------------------------------------------------------- cancellation ----

    ''' <summary>Acceptance 9: cancel at item 12 of 30.</summary>
    <Fact>
    Public Sub Cancel_lets_the_one_in_flight_finish_and_starts_nothing_else()
        Dim p As New Probe()
        Dim list As List(Of BatchOp) = Ops(30)
        p.Runner.Start(BatchRunKind.Move, list, Nothing, 0)

        For i As Integer = 1 To 11
            p.Complete()
        Next
        Assert.Same(list(11), p.Queue.InFlight)                         ' item 12 is running
        p.Runner.Cancel()
        Assert.True(p.Runner.IsRunning)                                 ' it is allowed to finish
        p.Complete()

        Assert.False(p.Runner.IsRunning)
        Assert.Equal(12, p.Queue.Enqueued.Count)                        ' 13-30 never started
        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.True(report.Cancelled)
        Assert.False(report.AbortedForTransport)
        Assert.Equal(12, report.DoneCount)
        Assert.Equal(18, report.NotStartedCount)
        Assert.All(report.Results.Skip(12), Sub(r) Assert.Equal(BatchItemState.NotStarted, r.State))
    End Sub

    <Fact>
    Public Sub Cancel_with_nothing_running_does_nothing()
        Dim p As New Probe()
        p.Runner.Cancel()
        Assert.True(p.Runner.Start(BatchRunKind.Move, Ops(2), Nothing, 0))
        p.Complete()
        p.Complete()
        Assert.False(Assert.Single(p.Finished).Cancelled)
    End Sub

    <Fact>
    Public Sub A_closed_queue_fails_that_file_by_name_and_stops()
        Dim p As New Probe()
        p.Queue.Accept = False
        p.Runner.Start(BatchRunKind.Move, Ops(3), Nothing, 0)

        Assert.False(p.Runner.IsRunning)
        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.Equal(1, report.FailedCount)
        Assert.Equal(2, report.NotStartedCount)
        Assert.True(report.Cancelled)
    End Sub

    ' ------------------------------------------------------------------ report ----

    <Fact>
    Public Sub The_report_lists_every_file_the_run_was_given_skips_first()
        Dim p As New Probe()
        Dim skipped As New List(Of BatchSkip) From {
            New BatchSkip With {.Path = "D:\in\x.jpg", .Reason = SkipReason.DuplicateName}}
        p.Runner.Start(BatchRunKind.Copy, Ops(2), skipped, 0)
        p.Complete()
        p.Complete(Dropped())

        Dim report As BatchReport = Assert.Single(p.Finished)
        Assert.Equal(3, report.Results.Count)
        Assert.Equal(BatchItemState.Skipped, report.Results(0).State)
        Assert.Equal(SkipReason.DuplicateName, report.Results(0).SkipReason)
        Assert.Equal(1, report.DoneCount)
        Assert.Equal(1, report.FailedCount)
        Assert.Equal(1, report.SkippedCount)
        Assert.Equal(BatchRunKind.Copy, report.Kind)
    End Sub

    <Fact>
    Public Sub The_summary_line_carries_the_counts_that_are_not_zero()
        Dim report As New BatchReport With {.Kind = BatchRunKind.Move}
        For i As Integer = 1 To 29
            report.Results.Add(New BatchItemResult With {.State = BatchItemState.Done, .Path = "D:\in\" & i.ToString() & ".jpg"})
        Next
        report.Results.Add(New BatchItemResult With {.State = BatchItemState.Skipped, .Path = "D:\in\s.jpg"})

        Dim line As String = report.SummaryLine()
        Assert.Contains("29", line)
        Assert.Equal(1, line.Split(";"c).Length - 1)                    ' one tail (skipped), no zero-count clutter

        Dim clean As New BatchReport With {.Kind = BatchRunKind.Delete}
        clean.Results.Add(New BatchItemResult With {.State = BatchItemState.Done, .Path = "D:\in\a.jpg"})
        Assert.True(clean.IsClean)
        Assert.DoesNotContain(";", clean.SummaryLine())
    End Sub

    <Fact>
    Public Sub Every_skip_reason_has_its_own_sentence()
        Dim seen As New HashSet(Of String)()
        For Each reason As SkipReason In [Enum].GetValues(GetType(SkipReason))
            Dim text As String = BatchReport.SkipText(reason)
            Assert.False(String.IsNullOrWhiteSpace(text))
            Assert.True(seen.Add(text), reason.ToString() & " shares its sentence with another reason")
        Next
    End Sub

End Class
#End If
