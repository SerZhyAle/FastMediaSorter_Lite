Option Strict On

Imports FastMediaSorterCompanion.AppCommands

''' <summary>
''' What the two clipboard verbs return (ticket L0316, APP-COMMANDS rule 9): a closed outcome token, the byte count
''' and the exit code of the contract's vocabulary (0, 1, 11, 13, 15; 12 is the gate's and 2 the client's). One pure
''' mapping from the result line's code, used alike by the one-shot with no manager and by the running manager's
''' answer, so the two never disagree. Words come from <see cref="ClipboardMessages"/>, never from here.
''' </summary>
Friend NotInheritable Class ShareManagerClipboardOutcome

    Friend ReadOnly Property Token As String
    Friend ReadOnly Property ExitCode As Integer
    Friend ReadOnly Property Bytes As Long
    ''' <summary>The result paragraph in the language shown now - the sentence the command prints.</summary>
    Friend ReadOnly Property Text As String
        Get
            Return _text
        End Get
    End Property

    Private _text As String = ""

    Private Sub New(token As String, exitCode As Integer, bytes As Long)
        Me.Token = token
        Me.ExitCode = exitCode
        Me.Bytes = bytes
    End Sub

    Private Function WithText(sentence As String) As ShareManagerClipboardOutcome
        Dim copy As New ShareManagerClipboardOutcome(Token, ExitCode, Bytes)
        copy._text = If(sentence, "")
        Return copy
    End Function

    ''' <summary>The outcome of a finished run: the first line that is not a success when none stored or placed the
    ''' value, else the success (a send to several servers is done when at least one stored it). Its sentence is the
    ''' whole result paragraph, the one the window and the notification show.</summary>
    Friend Shared Function [Of](result As ClipboardRunResult, bytes As Long) As ShareManagerClipboardOutcome
        If result Is Nothing OrElse result.Lines Is Nothing OrElse result.Lines.Count = 0 Then
            Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeFailed, CommandExit.Failed, 0L).
                WithText(Localization.T("Команда не выполнена."))
        End If
        Dim picked As ShareManagerClipboardOutcome = Nothing
        If result.Succeeded Then
            For Each line As ClipboardLine In result.Lines
                Dim one As ShareManagerClipboardOutcome = OfLine(line, bytes)
                If one.ExitCode = CommandExit.Done Then
                    picked = one
                    Exit For
                End If
            Next
        End If
        If picked Is Nothing Then picked = OfLine(result.Lines(0), bytes)
        Return picked.WithText(ClipboardMessages.TextOf(result))
    End Function

    ''' <summary>The outcome of one result line, with that line's own sentence.</summary>
    Friend Shared Function OfLine(line As ClipboardLine, bytes As Long) As ShareManagerClipboardOutcome
        Return Classify(line, bytes).WithText(ClipboardMessages.TextOf(line))
    End Function

    Private Shared Function Classify(line As ClipboardLine, bytes As Long) As ShareManagerClipboardOutcome
        Dim n As Long = Math.Max(0L, bytes)
        Select Case line.Code
            Case ClipboardLineCode.Sent, ClipboardLineCode.SentViaDrive
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeSent, CommandExit.Done, n)
            Case ClipboardLineCode.ReceivedValue, ClipboardLineCode.ReceivedFile,
                 ClipboardLineCode.ReceivedValueViaDrive, ClipboardLineCode.ReceivedFileViaDrive
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeReceived, CommandExit.Done, n)
            Case ClipboardLineCode.NothingToSend, ClipboardLineCode.ServerEmpty, ClipboardLineCode.EmptyViaDrive,
                 ClipboardLineCode.JoinedLater
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeEmpty, CommandExit.NotReady, 0L)
            Case ClipboardLineCode.NoServer, ClipboardLineCode.NotOffered, ClipboardLineCode.DriveSignedOut
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeNoAccount, CommandExit.NotReady, 0L)
            Case ClipboardLineCode.OperationUnderWay, ClipboardLineCode.ClipboardBusy
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeBusy, CommandExit.NotReady, 0L)
            Case ClipboardLineCode.TooLarge, ClipboardLineCode.OverServerLimit, ClipboardLineCode.OverDriveLimit
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeTooLarge, CommandExit.Resources, 0L)
            Case ClipboardLineCode.DriveFull, ClipboardLineCode.DriveDailyLimit
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeNoRoom, CommandExit.Resources, 0L)
            Case ClipboardLineCode.DriveOffline, ClipboardLineCode.DriveUnavailable
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeNoAnswer, CommandExit.Network, 0L)
            Case ClipboardLineCode.Failure
                If String.Equals(line.Reason, ConnectionDiagnosisMessages.UnreachableServer.Code, StringComparison.Ordinal) OrElse
                   String.Equals(line.Reason, ConnectionDiagnosisMessages.Unanswered.Code, StringComparison.Ordinal) Then
                    Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeNoAnswer, CommandExit.Network, 0L)
                End If
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeFailed, CommandExit.Failed, 0L)
            Case ClipboardLineCode.Cancelled
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeCancelled, CommandExit.Failed, 0L)
            Case Else
                Return New ShareManagerClipboardOutcome(ShareManagerCommandTable.OutcomeFailed, CommandExit.Failed, 0L)
        End Select
    End Function

    ''' <summary>The returned data of the response: the token and the byte count.</summary>
    Friend Function ToData() As JsonObject
        Dim data As New JsonObject()
        data.Add("outcome", Token)
        data.Add("bytes", CInt(Math.Min(Bytes, Integer.MaxValue)))
        Return data
    End Function

    ''' <summary>The response of the verb: done with the result paragraph when the exit code is 0, else the failed
    ''' response with the same data so a script reading --result sees the token either way.</summary>
    Friend Function ToResponse(requestId As String, message As String) As CommandResponse
        Dim data As JsonObject = ToData()
        If ExitCode = CommandExit.Done Then Return CommandResponse.Done(requestId, message, data)
        ' Exit 11 is "not ready" (nothing to move, no account, an operation under way): status busy, the closed reason busy.
        Dim notReady As Boolean = ExitCode = CommandExit.NotReady
        Return New CommandResponse With {
            .RequestId = requestId, .Status = If(notReady, CommandStatus.Busy, CommandStatus.Failed), .ExitCode = ExitCode,
            .Reason = If(notReady, CommandReasons.Busy, Nothing), .Message = message, .Data = data}
    End Function

End Class
