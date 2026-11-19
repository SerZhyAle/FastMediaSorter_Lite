Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion.AppCommands

''' <summary>
''' The three observe verbs of the Share Manager (ticket L0319, APP-COMMANDS 0.1 rules 1, 9, 12, 13): <c>share list</c>,
''' <c>schedule list</c> and <c>exchange status</c>. Each answer is a documented object whose member names a test fixes, built
''' by a pure function from the program's own data and written here and nowhere else - so the one place that decides what may
''' leave the program in an answer is this file.
'''
''' What an answer NEVER carries: a password, a PIN, a token, a key, a fingerprint, the address of a peer, a server address, a
''' user name, a folder an operation works on, a file name, an error text. The builders therefore take only the few members they
''' publish (a shared folder's path and writable flag, an operation's own name and tokens, the two facts of an exchange status)
''' and never serialise a source object: the worker's status and an exchange status carry exactly the members that must stay out.
''' All three only READ: no window, no network call, no operation run, no scheduler started.
''' </summary>
Friend Module ShareManagerObserve

    ' --- share list -------------------------------------------------------------------------------------

    ''' <summary>One object per folder this program shares (the ones the window lists): the folder's path, whether the server
    ''' serves it now and whether it accepts uploads. Nothing else of the worker's record is read.</summary>
    Friend Function ShareListData(roots As IEnumerable(Of ShareFolder), serving As Boolean) As JsonObject
        Dim shares As New List(Of Object)()
        If roots IsNot Nothing Then
            For Each root As ShareFolder In roots
                If root Is Nothing OrElse Not root.IsFmsOwned Then Continue For
                Dim host As String = If(root.hostPath, "")
                If host.Length = 0 Then Continue For
                Dim row As New JsonObject()
                row.Add("folder", host)
                row.Add("state", If(serving, ShareManagerCommandTable.ShareServing, ShareManagerCommandTable.ShareStopped))
                row.Add("writable", Not root.[readOnly])
                shares.Add(row)
            Next
        End If
        Dim data As New JsonObject()
        data.Add("shares", shares)
        Return data
    End Function

    ''' <summary>The answer of <c>share list</c> in the running manager: the worker's status read without ever starting a worker.
    ''' With the sharing payload not installed nothing is shared (an empty list, the truth); a worker that is installed and does
    ''' not answer is a failure, never an empty list that would read as "nothing shared".</summary>
    Friend Async Function ShareListResponseAsync(requestId As String) As Task(Of CommandResponse)
        If Not WorkerProcess.IsAvailable() Then
            Return CommandResponse.Done(requestId, ShareManagerCommandText.SharedFolders(0), ShareListData(Nothing, False))
        End If
        Dim status As WorkerStatus = Await ShareController.GetStatusAsync()
        If status Is Nothing Then
            Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed,
                                             .ExitCode = CommandExit.Failed, .Message = ShareManagerCommandText.WorkerSilent()}
        End If
        Dim data As JsonObject = ShareListData(status.Roots, status.Running)
        Dim count As Integer = CountOf(data, "shares")
        Return CommandResponse.Done(requestId, ShareManagerCommandText.SharedFolders(count), data)
    End Function

    ' --- schedule list ----------------------------------------------------------------------------------

    Friend Function ActionToken(action As ScheduledAction) As String
        Select Case action
            Case ScheduledAction.Copy : Return ShareManagerCommandTable.ActionCopy
            Case ScheduledAction.Move : Return ShareManagerCommandTable.ActionMove
            Case ScheduledAction.Delete : Return ShareManagerCommandTable.ActionDelete
            Case ScheduledAction.Archive : Return ShareManagerCommandTable.ActionArchive
            Case ScheduledAction.FillFreeSpace : Return ShareManagerCommandTable.ActionFillFreeSpace
            Case ScheduledAction.Backup : Return ShareManagerCommandTable.ActionBackup
            Case Else : Return ShareManagerCommandTable.ActionUnknown
        End Select
    End Function

    Friend Function ResultToken(outcome As ScheduledRunOutcome) As String
        Select Case outcome
            Case ScheduledRunOutcome.None : Return ShareManagerCommandTable.ResultNone
            Case ScheduledRunOutcome.Ok : Return ShareManagerCommandTable.ResultOk
            Case ScheduledRunOutcome.Failed : Return ShareManagerCommandTable.ResultFailed
            Case ScheduledRunOutcome.Refused : Return ShareManagerCommandTable.ResultRefused
            Case ScheduledRunOutcome.Wall : Return ShareManagerCommandTable.ResultWall
            Case ScheduledRunOutcome.Cancelled : Return ShareManagerCommandTable.ResultCancelled
            Case ScheduledRunOutcome.Skipped : Return ShareManagerCommandTable.ResultSkipped
            Case Else : Return ShareManagerCommandTable.ResultUnknown
        End Select
    End Function

    ''' <summary>The next run as ISO 8601 with the zone's offset, or Nothing while the scheduler does not arm the operation (it
    ''' is off, or parked until someone edits it). Computed by the one next-slot function the arming and the editor use, from the
    ''' clock - never read from the record, which is only as fresh as the last time a manager ran.</summary>
    Friend Function NextRunText(op As ScheduledOperation, now As DateTime, zone As TimeZoneInfo) As String
        If op Is Nothing OrElse Not op.IsArmable Then Return Nothing
        Dim tz As TimeZoneInfo = If(zone, TimeZoneInfo.Local)
        Dim slot As DateTime = DateTime.SpecifyKind(ScheduleMath.NextSlot(op, now, tz), DateTimeKind.Unspecified)
        Return New DateTimeOffset(slot, tz.GetUtcOffset(slot)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)
    End Function

    Friend Function RetentionToken(op As ScheduledOperation) As String
        If op Is Nothing OrElse op.BackupRetention <> BackupRetentionMode.Dated Then
            Return ShareManagerCommandTable.RetentionGenerations
        End If
        Return If(op.BackupKeepMonthly, ShareManagerCommandTable.RetentionKeepDaysMonthly, ShareManagerCommandTable.RetentionKeepDays)
    End Function

    ''' <summary>One object per operation: its id, the name its owner gave it, the action and last result as closed tokens, the
    ''' switch, the next run and, for backups, retention settings. No folder, no resource, no file name, no error text of the record is read.</summary>
    Friend Function ScheduleListData(operations As IEnumerable(Of ScheduledOperation), now As DateTime, zone As TimeZoneInfo) As JsonObject
        Dim list As New List(Of Object)()
        If operations IsNot Nothing Then
            For Each op As ScheduledOperation In operations
                If op Is Nothing Then Continue For
                Dim row As New JsonObject()
                row.Add("id", If(op.Id, ""))
                row.Add("name", If(op.Name, "").Trim())
                row.Add("action", ActionToken(op.Action))
                row.Add("enabled", op.Enabled)
                row.Add("nextRun", NextRunText(op, now, zone))
                row.Add("lastResult", ResultToken(If(op.Record Is Nothing, ScheduledRunOutcome.None, op.Record.LastOutcome)))
                If op.Action = ScheduledAction.Backup Then
                    row.Add("retention", RetentionToken(op))
                    If op.BackupRetention = BackupRetentionMode.Dated Then
                        row.Add("keepDays", CLng(op.BackupKeepDays))
                    Else
                        row.Add("keep", CLng(op.BackupKeepCount))
                    End If
                    row.Add("keepMonthly", op.BackupKeepMonthly)
                    row.Add("skipLinks", op.BackupSkipLinks)
                End If
                list.Add(row)
            Next
        End If
        Dim data As New JsonObject()
        data.Add("operations", list)
        Return data
    End Function

    ''' <summary>The answer of <c>schedule list</c>, the same in a running manager and in the one-shot process that has no manager:
    ''' read the operation files and answer. It constructs no engine, arms no timer and runs nothing.</summary>
    Friend Function ScheduleListResponse(requestId As String) As CommandResponse
        Try
            Dim operations As List(Of ScheduledOperation) = New ScheduledStore(ScheduledStore.DefaultFolder()).LoadAll()
            Dim data As JsonObject = ScheduleListData(operations, DateTime.Now, TimeZoneInfo.Local)
            Return CommandResponse.Done(requestId, ShareManagerCommandText.ScheduledOperations(operations.Count), data)
        Catch ex As Exception When TypeOf ex Is System.IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("App command: reading the scheduled operations", ex)
            Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed,
                                             .ExitCode = CommandExit.Failed, .Message = ShareManagerCommandText.Failed()}
        End Try
    End Function

    ' --- exchange status --------------------------------------------------------------------------------

    Friend Function ExchangeStateToken(configured As Boolean, state As ExchangeAgentState) As String
        If Not configured Then Return ShareManagerCommandTable.ExchangeNotConfigured
        Select Case state
            Case ExchangeAgentState.Connecting : Return ShareManagerCommandTable.ExchangeConnecting
            Case ExchangeAgentState.Online : Return ShareManagerCommandTable.ExchangeOnline
            Case ExchangeAgentState.Reconnecting : Return ShareManagerCommandTable.ExchangeReconnecting
            Case ExchangeAgentState.SignInAgain : Return ShareManagerCommandTable.ExchangeSignInAgain
            Case ExchangeAgentState.PinMismatch : Return ShareManagerCommandTable.ExchangePinMismatch
            Case ExchangeAgentState.UpdateNeeded : Return ShareManagerCommandTable.ExchangeUpdateNeeded
            Case ExchangeAgentState.Defect : Return ShareManagerCommandTable.ExchangeDefect
            ' Stopped, and the one combination that cannot be reached (configured yet "not configured"): an account that is there
            ' and not running reads as stopped.
            Case Else : Return ShareManagerCommandTable.ExchangeStopped
        End Select
    End Function

    ''' <summary>The three members of the answer. Only the two facts the caller names are taken: the configured flag and the state.</summary>
    Friend Function ExchangeStatusData(configured As Boolean, state As ExchangeAgentState, pendingRequests As Integer) As JsonObject
        Dim data As New JsonObject()
        data.Add("accountConfigured", configured)
        data.Add("state", ExchangeStateToken(configured, state))
        data.Add("pendingRequests", Math.Max(0, pendingRequests))
        Return data
    End Function

    ''' <summary>The answer built from a status of the exchange agent. The status carries the server, the login, the device id and
    ''' the pinned fingerprint; only its <c>Configured</c> and <c>State</c> are read here, so nothing else can reach the data.</summary>
    Friend Function ExchangeStatusData(status As ExchangeDeviceStatus, pendingRequests As Integer) As JsonObject
        If status Is Nothing Then Return ExchangeStatusData(False, ExchangeAgentState.NotConfigured, pendingRequests)
        Return ExchangeStatusData(status.Configured, status.State, pendingRequests)
    End Function

    ''' <summary>What the exchange client knows now, without a network call and without creating anything: Nothing when the client
    ''' is not offered or no account is set up (reading the account is a pure read, rule of the dormant state); the running agent's
    ''' own status otherwise; an account on disk with no agent reads as stopped.</summary>
    Friend Function CurrentExchangeStatus() As ExchangeDeviceStatus
        If Not ExchangeFeatures.IsEnabled() Then Return Nothing
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If agent IsNot Nothing Then Return agent.GetStatus()
        If Not ExchangeAccountStore.CreateDefault().IsConfigured() Then Return Nothing
        Return New ExchangeDeviceStatus(True, ExchangeAgentState.Stopped, "", Array.Empty(Of String)(), "", "", "", "", ExchangeCertificateKind.None, "")
    End Function

    Friend Function ExchangeStatusResponse(requestId As String, pendingRequests As Integer) As CommandResponse
        Dim status As ExchangeDeviceStatus = CurrentExchangeStatus()
        Dim data As JsonObject = ExchangeStatusData(status, pendingRequests)
        Dim configured As Boolean = status IsNot Nothing AndAlso status.Configured
        Dim message As String = If(configured,
            ShareManagerCommandText.ExchangeState(data.GetString("state"), Math.Max(0, pendingRequests)),
            ShareManagerCommandText.ExchangeNotSetUp())
        Return CommandResponse.Done(requestId, message, data)
    End Function

    ' --- shared -----------------------------------------------------------------------------------------

    Private Function CountOf(data As JsonObject, member As String) As Integer
        Dim value As Object = Nothing
        Dim items As List(Of Object) = Nothing
        If data.TryGet(member, value) Then items = TryCast(value, List(Of Object))
        Return If(items Is Nothing, 0, items.Count)
    End Function

End Module
