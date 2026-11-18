Option Strict On
Option Explicit On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion.AppCommands

''' <summary>
''' Command-line handlers for the Share Manager's schedule management verbs (ticket L0345, APP-COMMANDS 0.3):
''' <c>schedule add</c>, <c>schedule remove</c>, <c>schedule run</c> and <c>schedule history</c>.
''' Keeps validation identical to the editor (<see cref="ScheduledValidation"/> and <see cref="FileDoBackupProbe"/>)
''' and never writes the store directly when a manager is expected to own it.
''' </summary>
Friend Module ShareManagerScheduleCli

    Private Const DefaultHistoryLast As Integer = 50
    Private Const MaxOperationNameLength As Integer = 120

    ''' <summary>Checks command-line options that are purely syntactic before starting or contacting the manager
    ''' (APP-COMMANDS rule 3: bad options fail fast with exit 10 without launching anything).</summary>
    Friend Function ValidateClientArguments(parsed As ParsedCommand, workingDir As String, ByRef badToken As String) As Boolean
        badToken = Nothing
        If parsed Is Nothing OrElse parsed.Spec Is Nothing Then Return True
        Select Case parsed.Spec.Name
            Case "schedule add"
                If LaunchArguments.ResolveFolder(parsed.Value("source"), workingDir).Length = 0 Then
                    badToken = "--source"
                    Return False
                End If
                If LaunchArguments.ResolveFolder(parsed.Value("destination"), workingDir).Length = 0 Then
                    badToken = "--destination"
                    Return False
                End If
                Dim minutes As Integer
                If Not ResolveDailyMinutes(parsed.Value("daily"), minutes) Then
                    badToken = "--daily"
                    Return False
                End If
                If parsed.Has("keep") AndAlso parsed.Has("keep-days") Then
                    badToken = "--keep-days"
                    Return False
                End If
                If parsed.Has("keep") Then
                    Dim keepCount As Integer
                    If Not Integer.TryParse(parsed.Value("keep"), NumberStyles.Integer, CultureInfo.InvariantCulture, keepCount) OrElse
                       keepCount < ScheduledOperation.MinBackupKeepCount OrElse keepCount > ScheduledOperation.MaxBackupKeepCount Then
                        badToken = "--keep"
                        Return False
                    End If
                End If
                If parsed.Has("keep-days") Then
                    Dim keepDays As Integer
                    If Not Integer.TryParse(parsed.Value("keep-days"), NumberStyles.Integer, CultureInfo.InvariantCulture, keepDays) OrElse
                       keepDays < ScheduledOperation.MinBackupKeepDays OrElse keepDays > ScheduledOperation.MaxBackupKeepDays Then
                        badToken = "--keep-days"
                        Return False
                    End If
                End If
                If parsed.Flag("keep-monthly") AndAlso Not parsed.Has("keep-days") Then
                    badToken = "--keep-monthly"
                    Return False
                End If
            Case "schedule run"
                If Not parsed.Flag("all") AndAlso (parsed.Positionals.Count = 0 OrElse String.IsNullOrWhiteSpace(parsed.Positionals(0))) Then
                    badToken = "target"
                    Return False
                End If
            Case "schedule history"
                If parsed.Has("last") Then
                    Dim lastCount As Integer
                    If Not Integer.TryParse(parsed.Value("last"), NumberStyles.Integer, CultureInfo.InvariantCulture, lastCount) OrElse
                       lastCount < 1 OrElse lastCount > ScheduledJournal.HistoryKeepLines Then
                        badToken = "--last"
                        Return False
                    End If
                End If
        End Select
        Return True
    End Function

    ''' <summary>Parses a 24-hour <c>HH:mm</c> (or <c>H:mm</c>) time into minutes after midnight (0..1439).</summary>
    Friend Function ResolveDailyMinutes(text As String, ByRef minutes As Integer) As Boolean
        minutes = 0
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim parsed As DateTime
        Dim formats As String() = {"HH:mm", "H:mm"}
        If Not DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
            Return False
        End If
        minutes = parsed.Hour * 60 + parsed.Minute
        Return True
    End Function

    ''' <summary>Extracts the generation written and retired from <see cref="BackupCommandLine.Verdict"/> text.</summary>
    Friend Sub TryParseBackupVerdict(verdict As String, ByRef generation As Long?, ByRef retiredGeneration As Long?)
        Dim retiredCount, keptCount, keepDays, monthlyKept, skippedLinks As Long?
        TryParseBackupVerdictDetails(verdict, generation, retiredGeneration, retiredCount, keptCount, keepDays, monthlyKept, skippedLinks)
    End Sub

    Friend Sub TryParseBackupVerdictDetails(verdict As String, ByRef generation As Long?, ByRef retiredGeneration As Long?,
                                            ByRef retiredCount As Long?, ByRef keptCount As Long?, ByRef keepDays As Long?,
                                            ByRef monthlyKept As Long?, ByRef skippedLinks As Long?)
        Dim isDated As Boolean = False
        BackupCommandLine.TryParseVerdict(verdict, isDated, generation, retiredGeneration, retiredCount, keptCount, keepDays, monthlyKept, skippedLinks)
    End Sub

    ''' <summary>Resolves an operation by exact id (case-insensitive), then by trimmed Name, then by DisplayName.
    ''' Sets <paramref name="ambiguous"/> when more than one operation matches the name.</summary>
    Friend Function ResolveOperation(operations As IEnumerable(Of ScheduledOperation), target As String,
                                     ByRef resolved As ScheduledOperation, ByRef ambiguous As Boolean) As Boolean
        resolved = Nothing
        ambiguous = False
        Dim key As String = If(target, "").Trim()
        If key.Length = 0 OrElse operations Is Nothing Then Return False

        Dim list As New List(Of ScheduledOperation)()
        For Each op As ScheduledOperation In operations
            If op Is Nothing Then Continue For
            If String.Equals(op.Id, key, StringComparison.OrdinalIgnoreCase) Then
                resolved = op
                Return True
            End If
            list.Add(op)
        Next

        Dim byName As List(Of ScheduledOperation) = list.Where(
            Function(o) Not String.IsNullOrWhiteSpace(o.Name) AndAlso
                        String.Equals(o.Name.Trim(), key, StringComparison.OrdinalIgnoreCase)).ToList()
        If byName.Count = 1 Then
            resolved = byName(0)
            Return True
        End If
        If byName.Count > 1 Then
            ambiguous = True
            Return False
        End If

        Dim byDisplay As List(Of ScheduledOperation) = list.Where(
            Function(o) String.Equals(o.DisplayName(), key, StringComparison.OrdinalIgnoreCase)).ToList()
        If byDisplay.Count = 1 Then
            resolved = byDisplay(0)
            Return True
        End If
        If byDisplay.Count > 1 Then
            ambiguous = True
            Return False
        End If
        Return False
    End Function

    ''' <summary>Finds an existing Backup operation matching action + normalized source + normalized destination (Q345-2).</summary>
    Friend Function FindMatchingBackup(operations As IEnumerable(Of ScheduledOperation), sourceFolder As String,
                                       destinationFolder As String) As ScheduledOperation
        If operations Is Nothing Then Return Nothing
        Dim src As String = FolderPaths.Clean(sourceFolder)
        Dim dst As String = FolderPaths.Clean(destinationFolder)
        For Each op As ScheduledOperation In operations
            If op Is Nothing OrElse op.Action <> ScheduledAction.Backup Then Continue For
            If op.Source.Kind <> ScheduledLocationKind.Path OrElse op.Destination.Kind <> ScheduledLocationKind.Path Then Continue For
            If String.Equals(FolderPaths.Clean(op.Source.Path), src, StringComparison.OrdinalIgnoreCase) AndAlso
               String.Equals(FolderPaths.Clean(op.Destination.Path), dst, StringComparison.OrdinalIgnoreCase) Then
                Return op
            End If
        Next
        Return Nothing
    End Function

    Friend Function ScheduleAdd(engine As ScheduledEngine, parsed As ParsedCommand, workingDir As String,
                                requestId As String) As CommandResponse
        Dim actionRaw As String = If(parsed.Value("action"), "")
        If Not String.Equals(actionRaw, ShareManagerCommandTable.ActionBackup, StringComparison.Ordinal) Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--action"))
        End If

        Dim sourceFolder As String = LaunchArguments.ResolveFolder(parsed.Value("source"), workingDir)
        If sourceFolder.Length = 0 Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--source"))
        End If

        Dim destinationFolder As String = LaunchArguments.ResolveFolder(parsed.Value("destination"), workingDir)
        If destinationFolder.Length = 0 Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--destination"))
        End If

        Dim anchorMinutes As Integer = 0
        If Not ResolveDailyMinutes(parsed.Value("daily"), anchorMinutes) Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--daily"))
        End If

        Dim hasKeep As Boolean = parsed.Has("keep")
        Dim hasKeepDays As Boolean = parsed.Has("keep-days")
        If hasKeep AndAlso hasKeepDays Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--keep-days"))
        End If
        Dim keepCount As Integer = ScheduledOperation.DefaultBackupKeepCount
        If hasKeep Then
            If Not Integer.TryParse(parsed.Value("keep"), NumberStyles.Integer, CultureInfo.InvariantCulture, keepCount) OrElse
               keepCount < ScheduledOperation.MinBackupKeepCount OrElse keepCount > ScheduledOperation.MaxBackupKeepCount Then
                Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--keep"))
            End If
        End If
        Dim keepDays As Integer = ScheduledOperation.DefaultBackupKeepDays
        If hasKeepDays Then
            If Not Integer.TryParse(parsed.Value("keep-days"), NumberStyles.Integer, CultureInfo.InvariantCulture, keepDays) OrElse
               keepDays < ScheduledOperation.MinBackupKeepDays OrElse keepDays > ScheduledOperation.MaxBackupKeepDays Then
                Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--keep-days"))
            End If
        End If
        Dim keepMonthly As Boolean = parsed.Flag("keep-monthly")
        If keepMonthly AndAlso Not hasKeepDays Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--keep-monthly"))
        End If
        Dim skipLinks As Boolean = parsed.Flag("skip-links")

        ' Match the editor / executor requirements for Backup: FileDO must be installed and support the selected options.
        Dim services As ScheduledServices = engine.EngineServices
        Dim fileDoExe As String = services.LocateFileDo.Invoke()
        If String.IsNullOrEmpty(fileDoExe) Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ScheduledText.DescribeDetail("filedo-missing"))
        End If
        If hasKeepDays Then
            If Not services.FileDoHasBackupDatedRetention.Invoke(fileDoExe) Then
                Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ScheduledText.DescribeDetail("backup-filedo-unsupported"))
            End If
        ElseIf Not services.FileDoHasBackupGenerations.Invoke(fileDoExe) Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ScheduledText.DescribeDetail("backup-filedo-unsupported"))
        End If
        If skipLinks AndAlso Not services.FileDoHasBackupSkipLinks.Invoke(fileDoExe) Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ScheduledText.DescribeDetail("backup-filedo-unsupported"))
        End If

        Dim existing As ScheduledOperation = FindMatchingBackup(
            engine.Snapshot().Select(Function(s) s.Operation), sourceFolder, destinationFolder)
        Dim created As Boolean = (existing Is Nothing)
        Dim op As ScheduledOperation = If(existing IsNot Nothing, existing.Clone(), New ScheduledOperation With {.Action = ScheduledAction.Backup})
        op.Action = ScheduledAction.Backup
        op.Enabled = True
        op.Source = New ScheduledLocation With {.Kind = ScheduledLocationKind.Path, .Path = sourceFolder, .Recursive = True}
        op.Destination = New ScheduledLocation With {.Kind = ScheduledLocationKind.Path, .Path = destinationFolder}
        op.AnchorMinutes = anchorMinutes
        op.IntervalMinutes = ScheduleMath.MinutesPerDay
        op.RetryMissedRunsAtStart = True
        If hasKeepDays Then
            op.BackupRetention = BackupRetentionMode.Dated
            op.BackupKeepDays = keepDays
            op.BackupKeepMonthly = keepMonthly
        ElseIf hasKeep Then
            op.BackupRetention = BackupRetentionMode.Generations
            op.BackupKeepCount = keepCount
            op.BackupKeepMonthly = False
        ElseIf created Then
            op.BackupRetention = BackupRetentionMode.Generations
            op.BackupKeepCount = ScheduledOperation.DefaultBackupKeepCount
            op.BackupKeepMonthly = False
        End If
        If parsed.Has("skip-links") Then
            op.BackupSkipLinks = skipLinks
        ElseIf created Then
            op.BackupSkipLinks = False
        End If
        If parsed.Has("name") Then
            Dim trimmedName As String = If(parsed.Value("name"), "").Trim()
            If trimmedName.Length > MaxOperationNameLength Then trimmedName = trimmedName.Substring(0, MaxOperationNameLength)
            op.Name = trimmedName
        End If

        Dim savedId As String = ""
        Dim issues As List(Of ScheduledIssue) = engine.Upsert(op, savedId)
        If issues.Count > 0 Then
            Dim reasonText As String = String.Join(" ", issues.Select(Function(i) ScheduledText.IssueText(i)))
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, reasonText)
        End If

        Dim saved As ScheduledOperation = engine.TryGet(savedId)
        Dim now As DateTime = services.Clock.Invoke()
        Dim tz As TimeZoneInfo = If(services.TimeZone, Function() TimeZoneInfo.Local).Invoke()
        Dim nextRun As String = ShareManagerObserve.NextRunText(saved, now, tz)

        Dim data As New JsonObject()
        data.Add("id", savedId)
        data.Add("created", created)
        data.Add("nextRun", nextRun)
        Return CommandResponse.Done(requestId, ShareManagerCommandText.ScheduleSaved(saved.DisplayName(), created), data)
    End Function

    Friend Function ScheduleRemove(engine As ScheduledEngine, parsed As ParsedCommand, requestId As String) As CommandResponse
        Dim target As String = If(parsed.Positionals.Count > 0, parsed.Positionals(0), "")
        Dim op As ScheduledOperation = Nothing
        Dim ambiguous As Boolean = False
        If Not ResolveOperation(engine.Snapshot().Select(Function(s) s.Operation), target, op, ambiguous) Then
            If ambiguous Then
                Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.ScheduleAmbiguous(target))
            End If
            Return CommandResponse.ForReason(requestId, CommandReasons.TargetMissing, ShareManagerCommandText.ScheduleNotFound(target))
        End If

        Dim display As String = op.DisplayName()
        engine.Delete(op.Id)

        Dim data As New JsonObject()
        data.Add("id", op.Id)
        data.Add("removed", True)
        Return CommandResponse.Done(requestId, ShareManagerCommandText.ScheduleRemoved(display), data)
    End Function

    Friend Async Function ScheduleRunAsync(engine As ScheduledEngine, parsed As ParsedCommand, requestId As String) As Task(Of CommandResponse)
        Dim runAll As Boolean = parsed.Flag("all")
        Dim wait As Boolean = parsed.Flag("wait")
        Dim target As String = If(parsed.Positionals.Count > 0, parsed.Positionals(0), "")

        If Not runAll AndAlso String.IsNullOrWhiteSpace(target) Then
            Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("target"))
        End If

        If runAll Then
            Dim batchTask As Task(Of List(Of ScheduledRunResult)) = Nothing
            Dim queuedCount As Integer = 0
            Dim trigger As TriggerOutcome = engine.RunAllNowTracked(batchTask, queuedCount)
            Select Case trigger
                Case TriggerOutcome.Paused
                    Return CommandResponse.ForReason(requestId, CommandReasons.Busy, Localization.T("Расписание приостановлено: сначала возобновите его."))
                Case TriggerOutcome.NothingToRun
                    Dim anyEnabled As Boolean = engine.Snapshot().Any(Function(s) s.Operation IsNot Nothing AndAlso s.Operation.Enabled)
                    If anyEnabled Then
                        Return CommandResponse.ForReason(requestId, CommandReasons.Busy, ShareManagerCommandText.ScheduleAlreadyRunning(""))
                    End If
                    Return CommandResponse.ForReason(requestId, CommandReasons.TargetMissing, Localization.T("Нет включённых операций."))
            End Select

            If Not wait Then
                Dim queuedData As JsonObject = RunData(queuedCount, completed:=False, succeeded:=0, failed:=0, outcome:="queued")
                Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Accepted,
                                                 .ExitCode = CommandExit.Done,
                                                 .Message = ShareManagerCommandText.ScheduleQueued(queuedCount),
                                                 .Data = queuedData}
            End If

            Dim results As List(Of ScheduledRunResult) = Await batchTask
            Dim succeeded As Integer = results.Where(Function(r) r.Outcome = ScheduledRunOutcome.Ok).Count()
            Dim failed As Integer = results.Count - succeeded
            Dim outcomeToken As String = If(failed = 0, ShareManagerCommandTable.ResultOk,
                                            ShareManagerObserve.ResultToken(results.First(Function(r) r.Outcome <> ScheduledRunOutcome.Ok).Outcome))
            Dim doneData As JsonObject = RunData(queuedCount, completed:=True, succeeded:=succeeded, failed:=failed, outcome:=outcomeToken)
            If failed = 0 Then
                Return CommandResponse.Done(requestId, ShareManagerCommandText.ScheduleRunCompleted(succeeded, failed), doneData)
            End If
            Dim firstFailure As ScheduledRunResult = results.First(Function(r) r.Outcome <> ScheduledRunOutcome.Ok)
            Return ResponseForRunFailure(requestId, firstFailure, ShareManagerCommandText.ScheduleRunCompleted(succeeded, failed), doneData)
        End If

        Dim op As ScheduledOperation = Nothing
        Dim ambiguous As Boolean = False
        If Not ResolveOperation(engine.Snapshot().Select(Function(s) s.Operation), target, op, ambiguous) Then
            If ambiguous Then
                Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.ScheduleAmbiguous(target))
            End If
            Return CommandResponse.ForReason(requestId, CommandReasons.TargetMissing, ShareManagerCommandText.ScheduleNotFound(target))
        End If

        Dim runTask As Task(Of ScheduledRunResult) = Nothing
        Dim singleTrigger As TriggerOutcome = engine.RunNowTracked(op.Id, runTask)
        If singleTrigger = TriggerOutcome.NotFound Then
            Return CommandResponse.ForReason(requestId, CommandReasons.TargetMissing, ShareManagerCommandText.ScheduleNotFound(target))
        End If
        If singleTrigger = TriggerOutcome.AlreadyRunning Then
            Return CommandResponse.ForReason(requestId, CommandReasons.Busy, ShareManagerCommandText.ScheduleAlreadyRunning(op.DisplayName()))
        End If

        If Not wait Then
            Dim queuedData As JsonObject = RunData(1, completed:=False, succeeded:=0, failed:=0, outcome:="queued")
            Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Accepted,
                                             .ExitCode = CommandExit.Done,
                                             .Message = ShareManagerCommandText.ScheduleQueued(1),
                                             .Data = queuedData}
        End If

        Dim result As ScheduledRunResult = Await runTask
        If result Is Nothing Then
            Dim errData As JsonObject = RunData(1, completed:=True, succeeded:=0, failed:=1, outcome:=ShareManagerCommandTable.ResultCancelled)
            Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed,
                                             .ExitCode = CommandExit.Stopped, .Reason = CommandReasons.Cancelled,
                                             .Message = ShareManagerCommandText.ScheduleRunCompleted(0, 1), .Data = errData}
        End If

        Dim singleToken As String = ShareManagerObserve.ResultToken(result.Outcome)
        Dim okCount As Integer = If(result.Outcome = ScheduledRunOutcome.Ok, 1, 0)
        Dim failCount As Integer = 1 - okCount
        Dim singleData As JsonObject = RunData(1, completed:=True, succeeded:=okCount, failed:=failCount, outcome:=singleToken)
        If result.Outcome = ScheduledRunOutcome.Ok Then
            Return CommandResponse.Done(requestId, ShareManagerCommandText.ScheduleRunCompleted(1, 0), singleData)
        End If
        Dim detailText As String = ScheduledText.DescribeDetail(If(result.Outcome = ScheduledRunOutcome.Failed, result.FirstError, result.Detail))
        Dim message As String = If(String.IsNullOrEmpty(detailText), ShareManagerCommandText.ScheduleRunCompleted(0, 1), detailText)
        Return ResponseForRunFailure(requestId, result, message, singleData)
    End Function

    Private Function RunData(queued As Integer, completed As Boolean, succeeded As Integer, failed As Integer, outcome As String) As JsonObject
        Dim data As New JsonObject()
        data.Add("queued", queued)
        data.Add("completed", completed)
        data.Add("succeeded", succeeded)
        data.Add("failed", failed)
        data.Add("outcome", outcome)
        Return data
    End Function

    Private Function ResponseForRunFailure(requestId As String, result As ScheduledRunResult, message As String, data As JsonObject) As CommandResponse
        Dim reason As String = ReasonForRunResult(result)
        Return New CommandResponse With {
            .RequestId = requestId,
            .Status = CommandReasons.StatusOf(reason),
            .ExitCode = CommandReasons.ExitOf(reason),
            .Reason = reason,
            .Message = message,
            .Data = data}
    End Function

    Friend Function ReasonForRunResult(result As ScheduledRunResult) As String
        If result Is Nothing Then Return CommandReasons.OperationFailed
        Select Case result.Outcome
            Case ScheduledRunOutcome.Cancelled
                Return CommandReasons.Cancelled
            Case ScheduledRunOutcome.Skipped
                Return CommandReasons.Network
            Case ScheduledRunOutcome.Refused
                Return CommandReasons.OperationFailed
            Case ScheduledRunOutcome.Wall
                Dim code As String = ""
                Dim arg As String = ""
                ScheduledText.SplitDetail(result.Detail, code, arg)
                Select Case code
                    Case "disk-full", "source-drive-full", "destination-drive-full"
                        Return CommandReasons.NoSpace
                    Case "transport", "source-offline", "destination-offline", "source-drive-unavailable", "destination-drive-unavailable", "backup-remote-transport"
                        Return CommandReasons.Network
                    Case "source-unreachable", "destination-unreachable", "resource-removed"
                        Return CommandReasons.TargetMissing
                    Case Else
                        Return CommandReasons.OperationFailed
                End Select
            Case Else
                Return CommandReasons.OperationFailed
        End Select
    End Function

    ''' <summary>Pure builder for <c>schedule history</c> data and readable console text.</summary>
    Friend Function ScheduleHistoryData(entries As IEnumerable(Of JournalEntry), zone As TimeZoneInfo) As JsonObject
        Dim tz As TimeZoneInfo = If(zone, TimeZoneInfo.Local)
        Dim list As New List(Of Object)()
        If entries IsNot Nothing Then
            For Each entry As JournalEntry In entries
                If entry Is Nothing Then Continue For
                Dim gen As Long? = Nothing
                Dim retired As Long? = Nothing
                Dim retiredCount As Long? = Nothing
                Dim keptCount As Long? = Nothing
                Dim keepDays As Long? = Nothing
                Dim monthlyKept As Long? = Nothing
                Dim skippedLinks As Long? = Nothing
                TryParseBackupVerdictDetails(entry.Verdict, gen, retired, retiredCount, keptCount, keepDays, monthlyKept, skippedLinks)

                Dim stamp As DateTime = DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Unspecified)
                Dim timeText As String = New DateTimeOffset(stamp, tz.GetUtcOffset(stamp)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)

                Dim row As New JsonObject()
                row.Add("time", timeText)
                row.Add("operationId", If(entry.OperationId, ""))
                row.Add("operation", If(entry.OperationName, ""))
                row.Add("kind", If(entry.Kind, ""))
                row.Add("trigger", If(entry.Trigger, ""))
                row.Add("outcome", If(entry.Code, ""))
                row.Add("files", entry.Count)
                row.Add("failed", entry.Failed)
                row.Add("bytes", entry.Bytes)
                row.Add("durationMs", entry.DurationMs)
                row.Add("generation", If(gen.HasValue, CObj(gen.Value), Nothing))
                row.Add("retiredGeneration", If(retired.HasValue, CObj(retired.Value), Nothing))
                row.Add("retiredCount", If(retiredCount.HasValue, CObj(retiredCount.Value), Nothing))
                row.Add("keptCount", If(keptCount.HasValue, CObj(keptCount.Value), Nothing))
                row.Add("keepDays", If(keepDays.HasValue, CObj(keepDays.Value), Nothing))
                row.Add("monthlyKept", If(monthlyKept.HasValue, CObj(monthlyKept.Value), Nothing))
                row.Add("skippedLinks", If(skippedLinks.HasValue, CObj(skippedLinks.Value), Nothing))
                row.Add("detail", If(entry.Detail, ""))
                list.Add(row)
            Next
        End If
        Dim data As New JsonObject()
        data.Add("entries", list)
        Return data
    End Function

    Friend Function FormatHistoryLines(data As JsonObject) As String
        Dim value As Object = Nothing
        Dim rows As List(Of Object) = Nothing
        If data IsNot Nothing AndAlso data.TryGet("entries", value) Then rows = TryCast(value, List(Of Object))
        If rows Is Nothing OrElse rows.Count = 0 Then Return ShareManagerCommandText.ScheduleHistoryEmpty()
        Dim sb As New StringBuilder()
        For i As Integer = 0 To rows.Count - 1
            Dim row As JsonObject = TryCast(rows(i), JsonObject)
            If row Is Nothing Then Continue For
            If sb.Length > 0 Then sb.Append(Environment.NewLine)
            sb.Append(row.GetString("time")).Append(" | ")
            sb.Append(row.GetString("operation")).Append(" | ")
            Dim trigger As String = row.GetString("trigger")
            sb.Append(If(String.IsNullOrEmpty(trigger), "-", trigger)).Append(" | ")
            sb.Append(row.GetString("outcome")).Append(" | ")
            sb.Append("files=").Append(row.GetInt("files", 0).ToString(CultureInfo.InvariantCulture)).Append(" | ")
            sb.Append("bytes=").Append(GetLong(row, "bytes").ToString(CultureInfo.InvariantCulture))
            Dim genVal As Object = Nothing
            Dim retVal As Object = Nothing
            row.TryGet("generation", genVal)
            row.TryGet("retiredGeneration", retVal)
            If genVal IsNot Nothing OrElse retVal IsNot Nothing Then
                sb.Append(" | gen=").Append(If(genVal IsNot Nothing, Convert.ToString(genVal, CultureInfo.InvariantCulture), "-"))
                If retVal IsNot Nothing Then
                    sb.Append(" retired=").Append(Convert.ToString(retVal, CultureInfo.InvariantCulture))
                End If
            End If
            Dim keepDaysVal As Object = Nothing
            Dim keptCountVal As Object = Nothing
            Dim retiredCountVal As Object = Nothing
            Dim monthlyVal As Object = Nothing
            Dim skippedVal As Object = Nothing
            row.TryGet("keepDays", keepDaysVal)
            row.TryGet("keptCount", keptCountVal)
            row.TryGet("retiredCount", retiredCountVal)
            row.TryGet("monthlyKept", monthlyVal)
            row.TryGet("skippedLinks", skippedVal)
            If keepDaysVal IsNot Nothing OrElse keptCountVal IsNot Nothing OrElse retiredCountVal IsNot Nothing Then
                sb.Append(" | keepDays=").Append(If(keepDaysVal IsNot Nothing, Convert.ToString(keepDaysVal, CultureInfo.InvariantCulture), "-"))
                If keptCountVal IsNot Nothing Then sb.Append(" kept=").Append(Convert.ToString(keptCountVal, CultureInfo.InvariantCulture))
                If retiredCountVal IsNot Nothing Then sb.Append(" retired=").Append(Convert.ToString(retiredCountVal, CultureInfo.InvariantCulture))
                If monthlyVal IsNot Nothing Then sb.Append(" monthly=").Append(Convert.ToString(monthlyVal, CultureInfo.InvariantCulture))
            End If
            If skippedVal IsNot Nothing Then
                sb.Append(" | skippedLinks=").Append(Convert.ToString(skippedVal, CultureInfo.InvariantCulture))
            End If
            Dim detail As String = row.GetString("detail")
            If Not String.IsNullOrEmpty(detail) Then
                sb.Append(" | ").Append(detail)
            End If
        Next
        Return sb.ToString()
    End Function

    Private Function GetLong(obj As JsonObject, name As String) As Long
        Dim value As Object = Nothing
        If obj IsNot Nothing AndAlso obj.TryGet(name, value) AndAlso value IsNot Nothing Then
            If TypeOf value Is Long Then Return DirectCast(value, Long)
            If TypeOf value Is Integer Then Return CLng(DirectCast(value, Integer))
        End If
        Return 0L
    End Function

    Friend Function ScheduleHistoryResponse(journal As ScheduledJournal, store As ScheduledStore, parsed As ParsedCommand,
                                            zone As TimeZoneInfo, requestId As String) As CommandResponse
        Dim lastCount As Integer = DefaultHistoryLast
        If parsed.Has("last") Then
            If Not Integer.TryParse(parsed.Value("last"), NumberStyles.Integer, CultureInfo.InvariantCulture, lastCount) OrElse
               lastCount < 1 OrElse lastCount > ScheduledJournal.HistoryKeepLines Then
                Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue("--last"))
            End If
        End If

        Dim opFilter As String = If(parsed.Value("operation"), "").Trim()
        Dim matchedOpId As String = Nothing
        If opFilter.Length > 0 AndAlso store IsNot Nothing Then
            Dim ops As List(Of ScheduledOperation) = store.LoadAll()
            Dim resolved As ScheduledOperation = Nothing
            Dim ambiguous As Boolean = False
            If ResolveOperation(ops, opFilter, resolved, ambiguous) Then
                matchedOpId = resolved.Id
            ElseIf ambiguous Then
                Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.ScheduleAmbiguous(opFilter))
            End If
        End If

        Try
            Dim recent As List(Of JournalEntry) = journal.ReadRecent(ScheduledJournal.HistoryKeepLines)
            Dim filtered As IEnumerable(Of JournalEntry) = recent
            If opFilter.Length > 0 Then
                If matchedOpId IsNot Nothing Then
                    filtered = filtered.Where(Function(e) String.Equals(e.OperationId, matchedOpId, StringComparison.OrdinalIgnoreCase))
                Else
                    filtered = filtered.Where(Function(e) String.Equals(e.OperationId, opFilter, StringComparison.OrdinalIgnoreCase) OrElse
                                                          String.Equals(e.OperationName, opFilter, StringComparison.OrdinalIgnoreCase))
                End If
            End If
            Dim selected As List(Of JournalEntry) = filtered.Take(lastCount).ToList()
            Dim data As JsonObject = ScheduleHistoryData(selected, zone)
            Dim message As String = FormatHistoryLines(data)
            Return CommandResponse.Done(requestId, message, data)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("App command: reading the scheduled operations journal", ex)
            Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed,
                                             .ExitCode = CommandExit.Failed, .Message = ShareManagerCommandText.Failed()}
        End Try
    End Function

    Friend Function ScheduleHistoryResponse(requestId As String, parsed As ParsedCommand) As CommandResponse
        Dim folder As String = ScheduledStore.DefaultFolder()
        Return ScheduleHistoryResponse(New ScheduledJournal(folder), New ScheduledStore(folder), parsed, TimeZoneInfo.Local, requestId)
    End Function

End Module
