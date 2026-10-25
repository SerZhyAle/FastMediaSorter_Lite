#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' Runs a plan one operation at a time and records what happened to each
' (004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md PB-3).
'
' It holds NO UI reference and NO queue: it is handed a function that puts one operation on
' the application's file-operation queue, and it is told when that operation has finished.
' That is what lets "cancel at item 12 of 30" and "three dead-destination failures abort the
' rest" be proven with a fake queue in a unit test, and it is what lets the real queue
' (FileOpQueue, the one consumer that already serialises every transfer and that the
' shutdown drain already waits for) stay the only place work runs.
'
' ONE AT A TIME, on purpose: the next operation is enqueued only when the previous one has
' reported. That is the whole of cancellation (stop enqueuing; the one in flight is allowed to
' finish and the .part convention in FileManager already makes it safe) and the whole of
' "the undo history records a strict order". Parallel execution is refused (§5).
'
' Threading: every member is called on ONE thread - the UI thread - and OpCompleted is
' called after Enqueue has returned, never from inside it. Both hold for FileOpQueue, whose
' completions are posted back to the thread that created it.

Friend NotInheritable Class BatchProgressEventArgs
    Inherits EventArgs

    ''' <summary>1-based position of the operation that is about to start.</summary>
    Public ReadOnly Property Index As Integer
    Public ReadOnly Property Total As Integer
    Public ReadOnly Property Name As String
    Public ReadOnly Property BytesDone As Long
    Public ReadOnly Property TotalBytes As Long

    Friend Sub New(index As Integer, total As Integer, name As String, bytesDone As Long, totalBytes As Long)
        Me.Index = index
        Me.Total = total
        Me.Name = name
        Me.BytesDone = bytesDone
        Me.TotalBytes = totalBytes
    End Sub
End Class

Friend NotInheritable Class BatchItemEventArgs
    Inherits EventArgs

    Public ReadOnly Property Result As BatchItemResult

    Friend Sub New(result As BatchItemResult)
        Me.Result = result
    End Sub
End Class

Friend NotInheritable Class BatchFinishedEventArgs
    Inherits EventArgs

    Public ReadOnly Property Report As BatchReport

    Friend Sub New(report As BatchReport)
        Me.Report = report
    End Sub
End Class

Friend NotInheritable Class BatchRunner

    ''' <summary>After this many transport failures in a row the destination is gone, and
    ''' the rest of the run would be that many timeouts (PB-3). The batch-scale version of
    ''' the rule SlotHealth set for keypresses.</summary>
    Friend Const Transport_Abort_Threshold As Integer = 3

    Private ReadOnly enqueue As Func(Of BatchOp, Boolean)

    Private ops As IReadOnlyList(Of BatchOp)
    Private report As BatchReport
    Private results_By_Op As Dictionary(Of BatchOp, BatchItemResult)
    Private position As Integer = -1
    Private is_Running As Boolean
    Private cancel_Requested As Boolean
    Private transport_Streak As Integer
    Private bytes_Done As Long
    Private total_Bytes As Long

    ''' <summary>Raised on the UI thread just BEFORE each operation is enqueued.</summary>
    Friend Event Progress As EventHandler(Of BatchProgressEventArgs)

    ''' <summary>Raised once per operation, after it finished - the grid and the list update
    ''' as items complete rather than at the end.</summary>
    Friend Event ItemFinished As EventHandler(Of BatchItemEventArgs)

    ''' <summary>Raised once, when the run is over for any reason. The runner is idle again
    ''' by then, so a handler may start the next run.</summary>
    Friend Event Finished As EventHandler(Of BatchFinishedEventArgs)

    ''' <param name="enqueue">Puts ONE operation on the application's queue. False means the
    ''' queue is closed (shutdown) and the operation will not run.</param>
    Friend Sub New(enqueue As Func(Of BatchOp, Boolean))
        Me.enqueue = enqueue
    End Sub

    Friend ReadOnly Property IsRunning As Boolean
        Get
            Return is_Running
        End Get
    End Property

    Friend ReadOnly Property Total As Integer
        Get
            Return If(ops Is Nothing, 0, ops.Count)
        End Get
    End Property

    ''' <summary>
    ''' Starts a run. Returns False - and does nothing - while another run is in progress:
    ''' one batch at a time. The skipped files go straight into the report; the operations
    ''' are run in order.
    ''' </summary>
    Friend Function Start(kind As BatchRunKind, operations As IReadOnlyList(Of BatchOp),
                          skipped As IReadOnlyList(Of BatchSkip), notReturnable As Integer) As Boolean
        If is_Running Then Return False

        ops = If(operations, New List(Of BatchOp)())
        report = New BatchReport With {.Kind = kind, .NotReturnableCount = notReturnable}
        results_By_Op = New Dictionary(Of BatchOp, BatchItemResult)()
        position = -1
        cancel_Requested = False
        transport_Streak = 0
        bytes_Done = 0
        total_Bytes = 0

        If skipped IsNot Nothing Then
            For Each s As BatchSkip In skipped
                report.Results.Add(New BatchItemResult With {
                    .Path = s.Path, .State = BatchItemState.Skipped, .SkipReason = s.Reason})
            Next
        End If
        For Each op As BatchOp In ops
            Dim pending As New BatchItemResult With {.Path = If(op.ManagerOperation?.ReportPath, op.Source), .State = BatchItemState.NotStarted, .Op = op}
            report.Results.Add(pending)
            results_By_Op(op) = pending
            total_Bytes += Math.Max(0L, op.SizeBytes)
        Next

        is_Running = True
        If ops.Count = 0 Then
            Finish()
            Return True
        End If
        EnqueueNext()
        Return True
    End Function

    ''' <summary>Stops the run from enqueuing anything further. The operation in flight is
    ''' allowed to finish - it cannot be taken back half-done, and the staged-copy convention
    ''' already makes it safe.</summary>
    Friend Sub Cancel()
        If is_Running Then cancel_Requested = True
    End Sub

    ''' <summary>
    ''' The queue reports an operation finished. Anything that is not the operation in
    ''' flight is ignored - the queue is shared with the single-file path, and a stray
    ''' completion must never be mistaken for ours.
    ''' </summary>
    Friend Sub OpCompleted(op As BatchOp, failure As Exception)
        If Not is_Running OrElse op Is Nothing Then Return
        If position < 0 OrElse position >= ops.Count OrElse Not Object.ReferenceEquals(ops(position), op) Then Return

        Dim result As BatchItemResult = results_By_Op(op)
        If failure Is Nothing AndAlso Not String.IsNullOrEmpty(op.Refusal) Then
            ' Finished cleanly and did nothing: the file is reported with the reason, and the
            ' destination is clearly alive, so this never feeds the transport streak.
            result.State = BatchItemState.Failed
            result.FailureKind = PathFailureKind.None
            result.Detail = op.Refusal
            transport_Streak = 0
        ElseIf failure Is Nothing Then
            result.State = BatchItemState.Done
            bytes_Done += Math.Max(0L, op.SizeBytes)
            transport_Streak = 0
        Else
            result.State = BatchItemState.Failed
            result.FailureKind = PathFailure.Classify(failure)
            result.Detail = FailureText.Describe(failure)
            AppFileLogger.LogException("Batch: " & op.Kind.ToString() & " " & op.Source, failure)
            If result.FailureKind = PathFailureKind.Transport Then
                transport_Streak += 1
            Else
                transport_Streak = 0
            End If
        End If
        RaiseEvent ItemFinished(Me, New BatchItemEventArgs(result))

        If transport_Streak >= Transport_Abort_Threshold Then
            report.AbortedForTransport = True
            Finish()
        ElseIf cancel_Requested Then
            report.Cancelled = True
            Finish()
        ElseIf position + 1 >= ops.Count Then
            Finish()
        Else
            EnqueueNext()
        End If
    End Sub

    Private Sub EnqueueNext()
        position += 1
        Dim op As BatchOp = ops(position)
        RaiseEvent Progress(Me, New BatchProgressEventArgs(position + 1, ops.Count, op.DisplayName, bytes_Done, total_Bytes))

        Dim accepted As Boolean
        Try
            accepted = enqueue(op)
        Catch ex As Exception
            AppFileLogger.LogException("Batch: could not enqueue " & op.Source, ex)
            accepted = False
        End Try

        If Not accepted Then
            ' The queue is closed (the program is shutting down): this one will not run, and
            ' neither will the rest. It is a failure of this file, named, not a silent stop.
            Dim result As BatchItemResult = results_By_Op(op)
            result.State = BatchItemState.Failed
            result.Detail = Localization.T("очередь закрыта: программа завершает работу")
            RaiseEvent ItemFinished(Me, New BatchItemEventArgs(result))
            report.Cancelled = True
            Finish()
        End If
    End Sub

    Private Sub Finish()
        report.BytesDone = bytes_Done
        Dim done As BatchReport = report
        is_Running = False
        ops = Nothing
        results_By_Op = Nothing
        position = -1
        RaiseEvent Finished(Me, New BatchFinishedEventArgs(done))
    End Sub

End Class
#End If
