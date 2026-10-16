Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Globalization
Imports System.Text.Json
Imports System.Text.Json.Serialization

' The on-disk shape of one scheduled operation (ticket 222, section 4: "one JSON file per operation").
'
' A DTO of strings and numbers, not the model itself: the file outlives the build that wrote it, so
' an enum travels as its NAME ("move"), never its number; a name this build does not know is read as a
' halted definition (<see cref="ScheduledHalt.InvalidDefinition"/>) rather than as a crash or, worse, as
' some other action. Times are wall-clock ISO text without an offset. NO SECRET is ever written here: a
' resource is referenced by its registry id and its credential stays in the DPAPI store.
Friend NotInheritable Class ScheduledOperationDto
    Public Property Version As Integer = 1
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property Enabled As Boolean = True
    Public Property Action As String = "copy"
    Public Property Source As ScheduledLocationDto = New ScheduledLocationDto()
    Public Property Destination As ScheduledLocationDto = New ScheduledLocationDto()
    Public Property Conditions As ScheduledConditionsDto = New ScheduledConditionsDto()
    Public Property StartDate As String = ""
    Public Property AnchorMinutes As Integer
    Public Property IntervalMinutes As Integer = ScheduleMath.DefaultIntervalMinutes
    Public Property Conflict As String = "skip"
    Public Property DeleteOriginalsAfterVerify As Boolean
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property RetryMissedRunsAtStart As Boolean?
    Public Property ShowBalloonOnError As Boolean
    Public Property Halt As String = "none"
    Public Property HaltDetail As String = ""
    Public Property CreatedAt As String = ""
    Public Property Record As ScheduledRecordDto = New ScheduledRecordDto()
End Class

Friend NotInheritable Class ScheduledLocationDto
    Public Property Kind As String = "path"
    Public Property Path As String = ""
    Public Property ResourceId As String = ""
    Public Property ResourceName As String = ""
    Public Property Recursive As Boolean = True
End Class

Friend NotInheritable Class ScheduledConditionsDto
    Public Property Types As String = "all"
    Public Property Time As String = "all"
    Public Property NameMask As String = ""
    Public Property ExcludeMask As String = ""
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property MinAgeHours As Long?
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property MaxAgeHours As Long?
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property MinSizeBytes As Long?
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property MaxSizeBytes As Long?
    Public Property IncludeHidden As Boolean
End Class

Friend NotInheritable Class ScheduledRecordDto
    Public Property LastRunAt As String = ""
    Public Property LastSuccessAt As String = ""
    Public Property LastOutcome As String = "none"
    Public Property LastTrigger As String = "schedule"
    Public Property LastFileCount As Integer
    Public Property LastFailedCount As Integer
    Public Property LastError As String = ""
    Public Property NextRunAt As String = ""
    Public Property SkippedSinceSuccess As Boolean
End Class

Friend Module ScheduledJson

    Private ReadOnly Options As New JsonSerializerOptions With {
        .WriteIndented = True,
        .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        .PropertyNameCaseInsensitive = True,
        .DefaultIgnoreCondition = JsonIgnoreCondition.Never}

    Private Const TimeFormat As String = "yyyy-MM-dd'T'HH:mm:ss"
    Private Const DateFormat As String = "yyyy-MM-dd"

    ' --- text <-> enum ---------------------------------------------------------------------------

    Friend Function ActionName(action As ScheduledAction) As String
        Select Case action
            Case ScheduledAction.Move : Return "move"
            Case ScheduledAction.Delete : Return "delete"
            Case ScheduledAction.Archive : Return "archive"
            Case Else : Return "copy"
        End Select
    End Function

    ''' <summary>False for a name this build does not know - the caller halts the definition.</summary>
    Friend Function TryParseAction(text As String, ByRef action As ScheduledAction) As Boolean
        Select Case If(text, "").Trim().ToLowerInvariant()
            Case "copy" : action = ScheduledAction.Copy : Return True
            Case "move" : action = ScheduledAction.Move : Return True
            Case "delete" : action = ScheduledAction.Delete : Return True
            Case "archive" : action = ScheduledAction.Archive : Return True
        End Select
        action = ScheduledAction.Copy
        Return False
    End Function

    Private Function ConflictName(mode As ScheduledConflict) As String
        Select Case mode
            Case ScheduledConflict.Overwrite : Return "overwrite"
            Case ScheduledConflict.KeepBoth : Return "keepBoth"
            Case Else : Return "skip"
        End Select
    End Function

    ''' <summary>An unknown conflict mode reads as the safe default, "skip".</summary>
    Private Function ParseConflict(text As String) As ScheduledConflict
        Select Case If(text, "").Trim().ToLowerInvariant()
            Case "overwrite" : Return ScheduledConflict.Overwrite
            Case "keepboth" : Return ScheduledConflict.KeepBoth
            Case Else : Return ScheduledConflict.Skip
        End Select
    End Function

    Private Function TimeFilterName(filter As ScheduledTimeFilter) As String
        Select Case filter
            Case ScheduledTimeFilter.SinceLastRun : Return "sinceLastRun"
            Case ScheduledTimeFilter.LastHour : Return "lastHour"
            Case ScheduledTimeFilter.LastDay : Return "lastDay"
            Case Else : Return "all"
        End Select
    End Function

    Private Function ParseTimeFilter(text As String) As ScheduledTimeFilter
        Select Case If(text, "").Trim().ToLowerInvariant()
            Case "sincelastrun" : Return ScheduledTimeFilter.SinceLastRun
            Case "lasthour" : Return ScheduledTimeFilter.LastHour
            Case "lastday" : Return ScheduledTimeFilter.LastDay
            Case Else : Return ScheduledTimeFilter.All
        End Select
    End Function

    ''' <summary>"all", or a comma list of "images,audio,video,documents".</summary>
    Private Function TypesName(mask As ScheduledTypeMask) As String
        If (mask And ScheduledTypeMask.AllFiles) <> 0 Then Return "all"
        Dim parts As New List(Of String)()
        If (mask And ScheduledTypeMask.Images) <> 0 Then parts.Add("images")
        If (mask And ScheduledTypeMask.Audio) <> 0 Then parts.Add("audio")
        If (mask And ScheduledTypeMask.Video) <> 0 Then parts.Add("video")
        If (mask And ScheduledTypeMask.Documents) <> 0 Then parts.Add("documents")
        Return String.Join(",", parts)
    End Function

    ''' <summary>An empty or unreadable list reads as "none selected" - refused at the next save,
    ''' and never as "everything", which would widen what a destructive operation touches.</summary>
    Private Function ParseTypes(text As String) As ScheduledTypeMask
        Dim mask As ScheduledTypeMask = ScheduledTypeMask.None
        For Each part As String In If(text, "").Split(","c)
            Select Case part.Trim().ToLowerInvariant()
                Case "all" : Return ScheduledTypeMask.AllFiles
                Case "images" : mask = mask Or ScheduledTypeMask.Images
                Case "audio" : mask = mask Or ScheduledTypeMask.Audio
                Case "video" : mask = mask Or ScheduledTypeMask.Video
                Case "documents" : mask = mask Or ScheduledTypeMask.Documents
            End Select
        Next
        Return mask
    End Function

    Friend Function HaltText(halt As ScheduledHalt) As String
        Select Case halt
            Case ScheduledHalt.Wall : Return "wall"
            Case ScheduledHalt.ResourceRemoved : Return "resourceRemoved"
            Case ScheduledHalt.InvalidDefinition : Return "invalidDefinition"
            Case Else : Return "none"
        End Select
    End Function

    Private Function ParseHalt(text As String) As ScheduledHalt
        Select Case If(text, "").Trim().ToLowerInvariant()
            Case "wall" : Return ScheduledHalt.Wall
            Case "resourceremoved" : Return ScheduledHalt.ResourceRemoved
            Case "invaliddefinition" : Return ScheduledHalt.InvalidDefinition
            Case Else : Return ScheduledHalt.None
        End Select
    End Function

    Private Function OutcomeName(outcome As ScheduledRunOutcome) As String
        Select Case outcome
            Case ScheduledRunOutcome.Ok : Return "ok"
            Case ScheduledRunOutcome.Failed : Return "failed"
            Case ScheduledRunOutcome.Refused : Return "refused"
            Case ScheduledRunOutcome.Wall : Return "wall"
            Case ScheduledRunOutcome.Cancelled : Return "cancelled"
            Case ScheduledRunOutcome.Skipped : Return "skipped"
            Case Else : Return "none"
        End Select
    End Function

    Friend Function ParseOutcome(text As String) As ScheduledRunOutcome
        Select Case If(text, "").Trim().ToLowerInvariant()
            Case "ok" : Return ScheduledRunOutcome.Ok
            Case "failed" : Return ScheduledRunOutcome.Failed
            Case "refused" : Return ScheduledRunOutcome.Refused
            Case "wall" : Return ScheduledRunOutcome.Wall
            Case "cancelled" : Return ScheduledRunOutcome.Cancelled
            Case "skipped" : Return ScheduledRunOutcome.Skipped
            Case Else : Return ScheduledRunOutcome.None
        End Select
    End Function

    Friend Function OutcomeText(outcome As ScheduledRunOutcome) As String
        Return OutcomeName(outcome)
    End Function

    Friend Function TriggerName(trigger As ScheduledTrigger) As String
        Select Case trigger
            Case ScheduledTrigger.RunNow : Return "runNow"
            Case ScheduledTrigger.RunAll : Return "runAll"
            Case ScheduledTrigger.CatchUp : Return "catchUp"
            Case Else : Return "schedule"
        End Select
    End Function

    Friend Function ParseTrigger(text As String) As ScheduledTrigger
        Select Case If(text, "").Trim().ToLowerInvariant()
            Case "runnow" : Return ScheduledTrigger.RunNow
            Case "runall" : Return ScheduledTrigger.RunAll
            Case "catchup" : Return ScheduledTrigger.CatchUp
            Case Else : Return ScheduledTrigger.Schedule
        End Select
    End Function

    ' --- times ----------------------------------------------------------------------------------

    Friend Function FormatTime(value As DateTime?) As String
        If Not value.HasValue Then Return ""
        Return value.Value.ToString(TimeFormat, CultureInfo.InvariantCulture)
    End Function

    Friend Function ParseTime(text As String) As DateTime?
        Dim parsed As DateTime
        If DateTime.TryParseExact(If(text, ""), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
            Return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
        End If
        Return Nothing
    End Function

    Private Function ParseDate(text As String, fallback As DateTime) As DateTime
        Dim parsed As DateTime
        If DateTime.TryParseExact(If(text, ""), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
            Return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
        End If
        Return fallback
    End Function

    ' --- model <-> DTO ---------------------------------------------------------------------------

    Friend Function ToDto(op As ScheduledOperation) As ScheduledOperationDto
        Return New ScheduledOperationDto With {
            .Id = op.Id, .Name = op.Name, .Enabled = op.Enabled, .Action = ActionName(op.Action),
            .Source = LocationToDto(op.Source), .Destination = LocationToDto(op.Destination),
            .Conditions = New ScheduledConditionsDto With {
                .Types = TypesName(op.Conditions.TypeMask), .Time = TimeFilterName(op.Conditions.TimeFilter),
                .NameMask = op.Conditions.NameMask, .ExcludeMask = op.Conditions.ExcludeMask,
                .MinAgeHours = op.Conditions.MinAgeHours, .MaxAgeHours = op.Conditions.MaxAgeHours,
                .MinSizeBytes = op.Conditions.MinSizeBytes, .MaxSizeBytes = op.Conditions.MaxSizeBytes,
                .IncludeHidden = op.Conditions.IncludeHidden},
            .StartDate = op.StartDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            .AnchorMinutes = op.AnchorMinutes, .IntervalMinutes = op.IntervalMinutes,
            .Conflict = ConflictName(op.Conflict), .DeleteOriginalsAfterVerify = op.DeleteOriginalsAfterVerify,
            .RetryMissedRunsAtStart = If(op.HasExplicitRetryMissedRuns, CType(op.RetryMissedRunsAtStart, Boolean?), Nothing),
            .ShowBalloonOnError = op.ShowBalloonOnError,
            .Halt = HaltText(op.Halt), .HaltDetail = op.HaltDetail,
            .CreatedAt = FormatTime(op.CreatedAt), .Record = RecordToDto(op.Record)}
    End Function

    Friend Function LocationToDto(location As ScheduledLocation) As ScheduledLocationDto
        Dim kindName As String = "path"
        If location.Kind = ScheduledLocationKind.Resource Then
            kindName = "resource"
        ElseIf location.Kind = ScheduledLocationKind.Drive Then
            kindName = "drive"
        End If
        Return New ScheduledLocationDto With {
            .Kind = kindName,
            .Path = location.Path, .ResourceId = location.ResourceId, .ResourceName = location.ResourceName,
            .Recursive = location.Recursive}
    End Function

    Friend Function RecordToDto(record As ScheduledRunRecord) As ScheduledRecordDto
        Return New ScheduledRecordDto With {
            .LastRunAt = FormatTime(record.LastRunAt), .LastSuccessAt = FormatTime(record.LastSuccessAt),
            .LastOutcome = OutcomeName(record.LastOutcome), .LastTrigger = TriggerName(record.LastTrigger),
            .LastFileCount = record.LastFileCount, .LastFailedCount = record.LastFailedCount,
            .LastError = record.LastError, .NextRunAt = FormatTime(record.NextRunAt),
            .SkippedSinceSuccess = record.SkippedSinceSuccess}
    End Function

    Friend Function FromDto(dto As ScheduledOperationDto) As ScheduledOperation
        Dim op As New ScheduledOperation()
        op.Id = If(dto.Id, "")
        op.Name = If(dto.Name, "")
        op.Enabled = dto.Enabled
        Dim action As ScheduledAction
        If Not TryParseAction(dto.Action, action) Then
            op.Halt = ScheduledHalt.InvalidDefinition
            op.HaltDetail = "unknown action: " & If(dto.Action, "")
        End If
        op.Action = action
        op.Source = LocationFromDto(dto.Source)
        op.Destination = LocationFromDto(dto.Destination)
        Dim c As ScheduledConditionsDto = If(dto.Conditions, New ScheduledConditionsDto())
        op.Conditions = New ScheduledConditions With {
            .TypeMask = ParseTypes(c.Types), .TimeFilter = ParseTimeFilter(c.Time),
            .NameMask = If(c.NameMask, ""), .ExcludeMask = If(c.ExcludeMask, ""),
            .MinAgeHours = c.MinAgeHours, .MaxAgeHours = c.MaxAgeHours,
            .MinSizeBytes = c.MinSizeBytes, .MaxSizeBytes = c.MaxSizeBytes, .IncludeHidden = c.IncludeHidden}
        op.CreatedAt = If(ParseTime(dto.CreatedAt), DateTime.Now)
        op.StartDate = ParseDate(dto.StartDate, op.CreatedAt.Date)
        op.AnchorMinutes = ScheduleMath.NormalizeAnchor(dto.AnchorMinutes)
        op.IntervalMinutes = ScheduleMath.NormalizeInterval(dto.IntervalMinutes)
        op.Conflict = ParseConflict(dto.Conflict)
        op.DeleteOriginalsAfterVerify = dto.DeleteOriginalsAfterVerify AndAlso action = ScheduledAction.Archive
        If dto.RetryMissedRunsAtStart.HasValue Then
            op.RetryMissedRunsAtStart = dto.RetryMissedRunsAtStart.Value
        End If
        op.ShowBalloonOnError = dto.ShowBalloonOnError
        If op.Halt = ScheduledHalt.None Then
            op.Halt = ParseHalt(dto.Halt)
            op.HaltDetail = If(dto.HaltDetail, "")
        End If
        op.Record = RecordFromDto(dto.Record)
        Return op
    End Function

    Friend Function LocationFromDto(dto As ScheduledLocationDto) As ScheduledLocation
        If dto Is Nothing Then Return New ScheduledLocation()
        Dim kind As ScheduledLocationKind = ScheduledLocationKind.Path
        If String.Equals(dto.Kind, "resource", StringComparison.OrdinalIgnoreCase) Then
            kind = ScheduledLocationKind.Resource
        ElseIf String.Equals(dto.Kind, "drive", StringComparison.OrdinalIgnoreCase) Then
            kind = ScheduledLocationKind.Drive
        End If
        Return New ScheduledLocation With {
            .Kind = kind,
            .Path = If(dto.Path, ""), .ResourceId = If(dto.ResourceId, ""), .ResourceName = If(dto.ResourceName, ""),
            .Recursive = dto.Recursive}
    End Function

    Friend Function RecordFromDto(dto As ScheduledRecordDto) As ScheduledRunRecord
        If dto Is Nothing Then Return New ScheduledRunRecord()
        Return New ScheduledRunRecord With {
            .LastRunAt = ParseTime(dto.LastRunAt), .LastSuccessAt = ParseTime(dto.LastSuccessAt),
            .LastOutcome = ParseOutcome(dto.LastOutcome), .LastTrigger = ParseTrigger(dto.LastTrigger),
            .LastFileCount = Math.Max(0, dto.LastFileCount), .LastFailedCount = Math.Max(0, dto.LastFailedCount),
            .LastError = If(dto.LastError, ""), .NextRunAt = ParseTime(dto.NextRunAt),
            .SkippedSinceSuccess = dto.SkippedSinceSuccess}
    End Function

    ' --- text ------------------------------------------------------------------------------------

    Friend Function Serialize(op As ScheduledOperation) As String
        Return JsonSerializer.Serialize(ToDto(op), Options)
    End Function

    ''' <summary>Nothing when the text is not a readable operation; the store logs and skips it.</summary>
    Friend Function Deserialize(text As String) As ScheduledOperation
        Try
            Dim dto As ScheduledOperationDto = JsonSerializer.Deserialize(Of ScheduledOperationDto)(text, Options)
            If dto Is Nothing OrElse String.IsNullOrWhiteSpace(dto.Id) Then Return Nothing
            Return FromDto(dto)
        Catch ex As JsonException
            Return Nothing
        Catch ex As NotSupportedException
            Return Nothing
        End Try
    End Function

    Friend Function SerializeRecord(record As ScheduledRunRecord) As String
        Return JsonSerializer.Serialize(RecordToDto(record), Options)
    End Function

End Module
