Option Strict On

Imports System.Windows.Forms
Imports FastMediaSorterCompanion.AppCommands

' The running manager's side of APP-COMMANDS (ticket L0316; rules 7, 10, 12, 13, 16): a request arrives as WM_COPYDATA with
' the "FMSC" tag, is answered 1 at once (taken - see TrayContext.MessageWindow), decided here by the SAME strict parser a
' cold start uses, run on the UI thread, and answered by writing the response line into the reply file the request names -
' only ever inside the user's own %TEMP% reply folder of this program.
'
' Every verb that opens or does something the frozen spellings already did goes through the ONE handler those spellings use
' (OnPayloadReceived), so a verb and its old alias can never drift apart; what is new here is only the decision before it and
' the answer after it.
Partial Friend NotInheritable Class TrayContext

    ''' <summary>The last sharing state the tray learnt (<see cref="RefreshTrayState"/>); what "status" reports as <c>sharing</c>.</summary>
    Private _sharingNow As Boolean

    ''' <summary>Busy (rule 10): the manager is closing, or a modal window is open (a folder proposal waiting for its answer
    ''' is one). A window that is only open does not make the manager busy.</summary>
    Private Function AppCommandBusy() As Boolean
        If _disposed Then Return True
        For Each f As Form In Application.OpenForms
            If Not f.IsDisposed AndAlso f.Modal Then Return True
        Next
        Return False
    End Function

    Private Async Sub RunAppCommand(request As CommandRequest)
        If _disposed Then Return
        Dim response As CommandResponse
        Try
            Dim manifest As CommandManifest = ShareManagerCommandTable.Build()
            Dim busy As Boolean = AppCommandBusy() AndAlso Not ShareManagerCommandTable.IgnoresBusy(request.Command)
            Dim decision As DispatchDecision = CommandDispatch.Decide(request, manifest, AppCommandsSetting.IsAllowed(),
                                                                      busy, SingleInstanceProtocol.IsProcessElevated())
            If decision.RefusalReason IsNot Nothing Then
                Dim message As String = If(decision.Refusal IsNot Nothing, ShareManagerCommandText.ForRefusal(decision.Refusal),
                                           ShareManagerCommandText.ForReason(decision.RefusalReason))
                response = CommandResponse.ForReason(request.RequestId, decision.RefusalReason, message)
            Else
                response = Await ExecuteAppCommandAsync(request, decision.Parsed)
            End If
        Catch ex As Exception
            AppFileLogger.LogException("App command", ex)
            response = CommandResponse.ForReason(request.RequestId, CommandReasons.OperationFailed,
                                                 ShareManagerCommandText.ForReason(CommandReasons.OperationFailed))
        End Try

        AppFileLogger.WriteLine(CommandDispatch.DescribeForLog(request, response))
        Try
            CommandFiles.WriteWholeText(request.ReplyPath,
                response.ToBoundedLine(ShareManagerCommandText.ForReason(CommandReasons.ResponseTooLarge)) & ControlChars.Lf)
        Catch ex As Exception
            AppFileLogger.LogException("App command: writing the reply", ex)
        End Try
        ' The answer is on disk before the manager goes: the sender is waiting for it.
        If String.Equals(request.Command, "quit", StringComparison.Ordinal) AndAlso response.ExitCode = CommandExit.Done Then
            ExitApplication()
        End If
    End Sub

    Private Async Function ExecuteAppCommandAsync(request As CommandRequest, parsed As ParsedCommand) As Task(Of CommandResponse)
        Dim badToken As String = Nothing
        Dim target As String = LaunchArguments.TargetOf(parsed, request.WorkingDir, badToken)
        If badToken IsNot Nothing Then
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue(badToken))
        End If
        Select Case parsed.Spec.Name
            Case "status"
                Dim data As New JsonObject()
                data.Add("version", Application.ProductVersion)
                data.Add("pid", Environment.ProcessId)
                data.Add("exe", IO.Path.GetFileName(If(Environment.ProcessPath, ShareManagerCommandTable.ExeName)))
                data.Add("ready", True)
                data.Add("workerAvailable", WorkerProcess.IsAvailable())
                data.Add("sharing", _sharingNow)
                data.Add("acceptsRestrictedCommands", AppCommandsSetting.IsAllowed())
                Return CommandResponse.Done(request.RequestId, ShareManagerCommandText.Ready(Application.ProductVersion), data)

            Case "show"
                OnPayloadReceived(CompanionCommand.Parse(Program.ShowWindowCommand))
                Return CommandResponse.Done(request.RequestId, ShareManagerCommandText.WindowShown())

            Case "start"
                ' The startup option decides, exactly as for a launch with nothing attached; --silent shows nothing at all.
                If Not parsed.Flag("silent") Then OnPayloadReceived(CompanionCommand.Parse(Program.PlainStartCommand))
                Return CommandResponse.Done(request.RequestId, ShareManagerCommandText.Ready(Application.ProductVersion))

            Case "quit"
                Return CommandResponse.Done(request.RequestId, ShareManagerCommandText.Closed())

            Case "share add"
                Return OfferFolder(request, target)

            Case "resource add"
                OnPayloadReceived(CompanionCommand.Parse(If(parsed.Flag("camera"), Program.AddResourceCameraCommand, Program.AddResourceCommand)))
                Return CommandResponse.Done(request.RequestId, ShareManagerCommandText.WindowShown())

            Case "schedule open"
                OnPayloadReceived(CompanionCommand.Parse(CompanionCommand.ScheduleCommandPrefix & target))
                Return CommandResponse.Done(request.RequestId, ShareManagerCommandText.WindowShown())

            Case "broadcast open"
                If target.Length > 0 Then
                    OnPayloadReceived(CompanionCommand.Parse(CompanionCommand.BroadcastCommandPrefix & target))
                Else
                    OnPayloadReceived(CompanionCommand.Parse(Program.ShowWindowCommand))
                End If
                Return CommandResponse.Done(request.RequestId, ShareManagerCommandText.WindowShown())

            Case "clipboard send", "clipboard get"
                Dim result As ClipboardRunResult = Await _clipboard.RunAsync(String.Equals(parsed.Spec.Name, "clipboard send", StringComparison.Ordinal))
                Dim outcome As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.Of(result, _clipboard.LastBytes)
                Return outcome.ToResponse(request.RequestId, outcome.Text)

            Case "share list"
                Return Await ShareManagerObserve.ShareListResponseAsync(request.RequestId)

            Case "schedule list"
                Return ShareManagerObserve.ScheduleListResponse(request.RequestId)

            Case "schedule add"
                Return ShareManagerScheduleCli.ScheduleAdd(ScheduledHost.Engine, parsed, request.WorkingDir, request.RequestId)

            Case "schedule remove"
                Return ShareManagerScheduleCli.ScheduleRemove(ScheduledHost.Engine, parsed, request.RequestId)

            Case "schedule run"
                Return Await ShareManagerScheduleCli.ScheduleRunAsync(ScheduledHost.Engine, parsed, request.RequestId)

            Case "schedule history"
                Return ShareManagerScheduleCli.ScheduleHistoryResponse(request.RequestId, parsed)

            Case "exchange status"
                Return ShareManagerObserve.ExchangeStatusResponse(request.RequestId, _proposals.OutstandingCount)

            Case Else
                Return CommandResponse.ForReason(request.RequestId, CommandReasons.UnknownVerb, ShareManagerCommandText.ForReason(CommandReasons.UnknownVerb))
        End Select
    End Function

    ''' <summary><c>share add</c>: the folder only ever becomes a PROPOSAL the person answers in the manager (queue of 32, the
    ''' same 10-second de-duplication as a folder handed over by the viewer); nothing is shared by the command. The answer is
    ''' "accepted" and says which of the four things became of it.</summary>
    Private Function OfferFolder(request As CommandRequest, folder As String) As CommandResponse
        Dim command As CompanionCommand = CompanionCommand.Parse(folder)
        If command.Kind <> CompanionCommandKind.ShareFolder Then
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("folder"))
        End If
        Dim verdict As ProposalVerdict = _proposals.Offer(command.Folder, AddressOf IsAlreadyShared)
        AppFileLogger.WriteLine("Companion: folder proposal " & verdict.ToString())
        Dim token As String = ShareManagerCommandTable.ProposalToken(verdict = ProposalVerdict.Queued,
                                                                      verdict = ProposalVerdict.Duplicate,
                                                                      verdict = ProposalVerdict.AlreadyShared)
        If verdict = ProposalVerdict.Full Then
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.Busy, ShareManagerCommandText.ForProposal(token))
        End If
        ' The one place a staged folder is put to the person (or, when it is already shared, shown).
        OnPayloadReceived(command)
        Dim data As New JsonObject()
        data.Add("proposal", token)
        ' "accepted" (the verb's wait): the proposal is taken, the person's answer is theirs to give; exit 0.
        Return New CommandResponse With {.RequestId = request.RequestId, .Status = CommandStatus.Accepted,
                                         .ExitCode = CommandExit.Done, .Message = ShareManagerCommandText.ForProposal(token), .Data = data}
    End Function

End Class
