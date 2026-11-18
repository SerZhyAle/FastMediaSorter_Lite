#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports Microsoft.VisualBasic

''' <summary>
''' Consent setting for remote schedule requests (DEVICE-EXCHANGE 1.15 item FT).
''' </summary>
Public Enum ScheduledRemoteConsent
    ''' <summary>Ask every time (default). In unattended execution mutations are declined.</summary>
    Ask = 0
    ''' <summary>Always allow remote schedules from devices of the account.</summary>
    Always = 1
    ''' <summary>Never allow remote schedules: all verbs refused not-permitted.</summary>
    Never = 2
End Enum

Public Module ScheduledRemoteSettings

    Friend Const ConsentSettingKey As String = "Share_RemoteScheduleConsent"

    ''' <summary>
    ''' Reads the consent setting from registry / settings. Default is Ask.
    ''' </summary>
    Public Function GetConsent() As ScheduledRemoteConsent
        Try
            Dim val As String = GetSetting("FastMediaSorter", "Settings", ConsentSettingKey, "ask").Trim().ToLowerInvariant()
            Select Case val
                Case "always" : Return ScheduledRemoteConsent.Always
                Case "never" : Return ScheduledRemoteConsent.Never
                Case Else : Return ScheduledRemoteConsent.Ask
            End Select
        Catch
            Return ScheduledRemoteConsent.Ask
        End Try
    End Function

    Public Sub SetConsent(value As ScheduledRemoteConsent)
        Try
            Dim val As String = "ask"
            Select Case value
                Case ScheduledRemoteConsent.Always : val = "always"
                Case ScheduledRemoteConsent.Never : val = "never"
                Case Else : val = "ask"
            End Select
            SaveSetting("FastMediaSorter", "Settings", ConsentSettingKey, val)
        Catch
        End Try
    End Sub

End Module

''' <summary>
''' Thread-safe memory ring buffer keeping the last 256 requests per requester (item FT).
''' Repeated requests with the same requestId return the cached answer box without running again.
''' </summary>
Friend NotInheritable Class ScheduledRemoteRing

    Private Const MaxPerRequester As Integer = 256
    Private ReadOnly _lock As New Object()
    Private ReadOnly _records As New Dictionary(Of String, LinkedList(Of Tuple(Of String, String)))(StringComparer.Ordinal)
    Private ReadOnly _lookup As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Private Shared Function Key(requesterDeviceId As String, requestId As String) As String
        Return requesterDeviceId & ":" & requestId
    End Function

    Public Function TryGet(requesterDeviceId As String, requestId As String, ByRef resultBoxJson As String) As Boolean
        SyncLock _lock
            Return _lookup.TryGetValue(Key(requesterDeviceId, requestId), resultBoxJson)
        End SyncLock
    End Function

    Public Sub Record(requesterDeviceId As String, requestId As String, resultBoxJson As String)
        SyncLock _lock
            Dim k As String = Key(requesterDeviceId, requestId)
            _lookup(k) = resultBoxJson

            Dim list As LinkedList(Of Tuple(Of String, String)) = Nothing
            If Not _records.TryGetValue(requesterDeviceId, list) Then
                list = New LinkedList(Of Tuple(Of String, String))()
                _records(requesterDeviceId) = list
            End If

            list.AddLast(Tuple.Create(requestId, resultBoxJson))
            While list.Count > MaxPerRequester
                Dim oldest As Tuple(Of String, String) = list.First.Value
                list.RemoveFirst()
                _lookup.Remove(Key(requesterDeviceId, oldest.Item1))
            End While
        End SyncLock
    End Sub

    Public Sub Clear()
        SyncLock _lock
            _records.Clear()
            _lookup.Clear()
        End SyncLock
    End Sub

End Class

''' <summary>
''' Dispatches remote schedule verbs and builds the response payload (DEVICE-EXCHANGE 1.15 items FR to FV).
''' </summary>
Friend NotInheritable Class ScheduledRemoteDispatcher

    Private ReadOnly _engine As ScheduledEngine
    Private ReadOnly _localDeviceId As String
    Private _consentOverride As Nullable(Of ScheduledRemoteConsent)

    Public Sub New(engine As ScheduledEngine, localDeviceId As String)
        _engine = engine
        _localDeviceId = If(localDeviceId, String.Empty)
    End Sub

    ''' <summary>Test hook to override consent mode without writing registry.</summary>
    Friend Property ConsentOverride As Nullable(Of ScheduledRemoteConsent)
        Get
            Return _consentOverride
        End Get
        Set(value As Nullable(Of ScheduledRemoteConsent))
            _consentOverride = value
        End Set
    End Property

    ''' <summary>The facts of a registered resource: the viewer's registry in the product, a stub in a test.</summary>
    Friend Property ResourceFacts As Func(Of String, ScheduledResourceFacts) = Function(id As String) ScheduledResourceRegistry.Lookup(id)

    Private Function EffectiveConsent() As ScheduledRemoteConsent
        If _consentOverride.HasValue Then Return _consentOverride.Value
        Return ScheduledRemoteSettings.GetConsent()
    End Function

    ''' <summary>
    ''' Executes the clear JSON request body and returns the clear JSON response body.
    ''' Never throws: an unhandled failure maps to invalid-payload or unsupported.
    ''' </summary>
    Public Function Execute(fromDeviceId As String, requestId As String, clearJson As String) As String
        Try
            Using doc As JsonDocument = JsonDocument.Parse(clearJson)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then
                    Return Refusal("invalid-payload", "definition")
                End If

                Dim vProp As JsonElement = Nothing
                If Not root.TryGetProperty("v", vProp) OrElse vProp.ValueKind <> JsonValueKind.Number OrElse vProp.GetInt32() <> 1 Then
                    Return Refusal("invalid-payload", "v")
                End If

                Dim verbProp As JsonElement = Nothing
                If Not root.TryGetProperty("verb", verbProp) OrElse verbProp.ValueKind <> JsonValueKind.String Then
                    Return Refusal("invalid-payload", "verb")
                End If
                Dim verb As String = verbProp.GetString().Trim().ToLowerInvariant()

                Dim consent As ScheduledRemoteConsent = EffectiveConsent()
                If consent = ScheduledRemoteConsent.Never Then
                    Return Refusal("not-permitted")
                End If

                Select Case verb
                    Case "places"
                        Return HandlePlaces()
                    Case "list"
                        Return HandleList()
                    Case "history"
                        Return HandleHistory(root)
                    Case "save"
                        If consent = ScheduledRemoteConsent.Ask Then
                            Return Refusal("declined")
                        End If
                        Return HandleSave(root)
                    Case "delete"
                        If consent = ScheduledRemoteConsent.Ask Then
                            Return Refusal("declined")
                        End If
                        Return HandleDelete(root)
                    Case "run"
                        If consent = ScheduledRemoteConsent.Ask Then
                            Return Refusal("declined")
                        End If
                        Return HandleRun(root)
                    Case Else
                        Return Refusal("unsupported", "verb")
                End Select
            End Using
        Catch ex As JsonException
            Return Refusal("invalid-payload", "definition")
        Catch ex As Exception
            Return Refusal("invalid-payload")
        End Try
    End Function

    Private Shared Function Refusal(reason As String, Optional member As String = "") As String
        Using ms As New MemoryStream()
            Using writer As New Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteNumber("v", 1)
                writer.WriteString("outcome", "refused")
                writer.WriteString("reason", reason)
                If Not String.IsNullOrEmpty(member) Then
                    writer.WriteString("member", member)
                End If
                writer.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    Private Function HandlePlaces() As String
        Dim placesList As New List(Of ResourceDescriptor)()
        Try
            Dim reg As IReadOnlyList(Of ResourceDescriptor) = ResourceRegistry.Load()
            If reg IsNot Nothing Then placesList.AddRange(reg)
        Catch
        End Try

        Dim truncated As Boolean = placesList.Count > 200
        Dim count As Integer = Math.Min(placesList.Count, 200)

        Using ms As New MemoryStream()
            Using writer As New Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteNumber("v", 1)
                writer.WriteString("outcome", "ok")
                writer.WriteStartArray("places")
                For i As Integer = 0 To count - 1
                    Dim r As ResourceDescriptor = placesList(i)
                    writer.WriteStartObject()
                    writer.WriteString("deviceId", _localDeviceId)
                    writer.WriteString("resourceId", r.Id)
                    writer.WriteString("name", r.DisplayName)
                    writer.WriteBoolean("readOnly", r.ReadOnlyHint)
                    writer.WriteEndObject()
                Next
                writer.WriteEndArray()
                writer.WriteBoolean("truncated", truncated)
                writer.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    Private Function HandleList() As String
        ' The engine's own view: the definitions it has armed and whether each one is running now. Actions the
        ' contract has no word for (fill free space) are left out of the answer instead of being named as another.
        Dim ops As New List(Of ScheduledOperationState)()
        Try
            For Each state As ScheduledOperationState In _engine.Snapshot()
                If IsContractAction(state.Operation.Action) Then ops.Add(state)
            Next
        Catch
            ops.Clear()
        End Try

        Dim truncated As Boolean = ops.Count > 100
        Dim count As Integer = Math.Min(ops.Count, 100)

        Using ms As New MemoryStream()
            Using writer As New Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteNumber("v", 1)
                writer.WriteString("outcome", "ok")
                writer.WriteStartArray("operations")
                For i As Integer = 0 To count - 1
                    WriteOperationObject(writer, ops(i).Operation, ops(i).IsRunning)
                Next
                writer.WriteEndArray()
                writer.WriteBoolean("truncated", truncated)
                writer.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    Private Shared Function IsContractAction(a As ScheduledAction) As Boolean
        Return a = ScheduledAction.Copy OrElse a = ScheduledAction.Move OrElse a = ScheduledAction.Delete OrElse
               a = ScheduledAction.Archive OrElse a = ScheduledAction.Backup
    End Function

    Private Function IsRunningNow(operationId As String) As Boolean
        Try
            For Each state As ScheduledOperationState In _engine.Snapshot()
                If String.Equals(state.Operation.Id, operationId, StringComparison.Ordinal) Then Return state.IsRunning
            Next
        Catch
        End Try
        Return False
    End Function

    Private Shared Function Rfc3339Utc(value As DateTime) As String
        Return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
    End Function

    Private Sub WriteOperationObject(writer As Utf8JsonWriter, op As ScheduledOperation, isRunning As Boolean)
        writer.WriteStartObject()
        writer.WriteString("operationId", op.Id)

        ' Definition
        writer.WriteStartObject("definition")
        writer.WriteString("name", op.Name)
        writer.WriteBoolean("enabled", op.Enabled)
        writer.WriteString("action", ActionToString(op.Action))

        ' Source location
        WriteLocation(writer, "source", op.Source)

        ' Destination location (for copy, move, archive, backup)
        If op.Action <> ScheduledAction.Delete Then
            WriteLocation(writer, "destination", op.Destination)
        End If

        ' File types, time filter and conditions select files; a backup takes its folder whole (item FS: not for backup)
        If op.Action <> ScheduledAction.Backup Then
            Dim c As ScheduledConditions = op.Conditions
            WriteFileTypes(writer, c.TypeMask)
            writer.WriteString("timeFilter", TimeFilterToString(c.TimeFilter))

            writer.WriteStartObject("conditions")
            If Not String.IsNullOrEmpty(c.NameMask) Then writer.WriteString("nameMask", c.NameMask)
            If Not String.IsNullOrEmpty(c.ExcludeMask) Then writer.WriteString("excludeMask", c.ExcludeMask)
            If c.MinAgeHours.HasValue Then writer.WriteNumber("minAgeHours", c.MinAgeHours.Value)
            If c.MaxAgeHours.HasValue Then writer.WriteNumber("maxAgeHours", c.MaxAgeHours.Value)
            If c.MinSizeBytes.HasValue Then writer.WriteNumber("minSizeBytes", c.MinSizeBytes.Value)
            If c.MaxSizeBytes.HasValue Then writer.WriteNumber("maxSizeBytes", c.MaxSizeBytes.Value)
            If c.IncludeHidden Then writer.WriteBoolean("includeHidden", True)
            writer.WriteEndObject()
        End If

        ' At: the daily anchor on this device's wall clock
        writer.WriteStartObject("at")
        writer.WriteNumber("hour", op.AnchorMinutes \ 60)
        writer.WriteNumber("minute", op.AnchorMinutes Mod 60)
        writer.WriteEndObject()

        ' Interval minutes
        writer.WriteNumber("intervalMinutes", Math.Max(15, op.IntervalMinutes))

        ' Conflict
        If op.Action = ScheduledAction.Copy OrElse op.Action = ScheduledAction.Move OrElse op.Action = ScheduledAction.Archive Then
            writer.WriteString("conflict", ConflictToString(op.Conflict))
        End If

        ' Archive verify
        If op.Action = ScheduledAction.Archive Then
            writer.WriteBoolean("deleteOriginalsAfterVerify", op.DeleteOriginalsAfterVerify)
        End If

        ' Backup keepCount and sealed
        If op.Action = ScheduledAction.Backup Then
            writer.WriteNumber("keepCount", Math.Max(ScheduledOperation.MinBackupKeepCount, Math.Min(ScheduledOperation.MaxBackupKeepCount, op.BackupKeepCount)))
            writer.WriteBoolean("sealed", op.BackupSealed)
        End If

        writer.WriteEndObject() ' definition

        ' State
        Dim rec As ScheduledRunRecord = If(op.Record, New ScheduledRunRecord())
        writer.WriteStartObject("state")
        If rec.LastRunAt.HasValue Then writer.WriteString("lastRunAt", Rfc3339Utc(rec.LastRunAt.Value))
        If rec.LastSuccessAt.HasValue Then writer.WriteString("lastSuccessAt", Rfc3339Utc(rec.LastSuccessAt.Value))
        If rec.NextRunAt.HasValue Then writer.WriteString("nextRunAt", Rfc3339Utc(rec.NextRunAt.Value))

        writer.WriteString("lastOutcome", OutcomeToString(rec.LastOutcome))
        writer.WriteNumber("lastFileCount", rec.LastFileCount)
        writer.WriteNumber("lastFailedCount", rec.LastFailedCount)
        writer.WriteBoolean("running", isRunning)
        writer.WriteString("halt", HaltToString(op.Halt))
        writer.WriteEndObject() ' state

        writer.WriteEndObject() ' operation
    End Sub

    ''' <summary>A resource side answers as a directory location: the wire path is relative to the resource root,
    ''' empty for the root itself (item FS). A path on this disk, Google Drive, or a stored folder a moved
    ''' resource root can no longer confirm has no wire spelling and is answered display only
    ''' (<c>{kind:"local", label}</c>, never writable by a request).</summary>
    Private Sub WriteLocation(writer As Utf8JsonWriter, propName As String, loc As ScheduledLocation)
        writer.WriteStartObject(propName)
        If loc IsNot Nothing AndAlso loc.Kind = ScheduledLocationKind.Resource AndAlso Not String.IsNullOrWhiteSpace(loc.ResourceId) Then
            Dim wirePath As String = WirePathOf(ResourceFacts.Invoke(loc.ResourceId), loc.Path)
            If wirePath IsNot Nothing Then
                writer.WriteString("deviceId", _localDeviceId)
                writer.WriteString("resourceId", loc.ResourceId)
                writer.WriteString("path", wirePath)
            Else
                writer.WriteString("kind", "local")
                writer.WriteString("label", loc.Describe())
            End If
        Else
            writer.WriteString("kind", "local")
            writer.WriteString("label", If(loc Is Nothing, "", loc.Describe()))
        End If
        writer.WriteEndObject()
    End Sub

    Private Shared Sub WriteFileTypes(writer As Utf8JsonWriter, mask As ScheduledTypeMask)
        If mask.HasFlag(ScheduledTypeMask.AllFiles) OrElse mask = ScheduledTypeMask.None Then
            writer.WriteString("fileTypes", "all")
            Return
        End If
        writer.WriteStartArray("fileTypes")
        If mask.HasFlag(ScheduledTypeMask.Images) Then writer.WriteStringValue("images")
        If mask.HasFlag(ScheduledTypeMask.Audio) Then writer.WriteStringValue("audio")
        If mask.HasFlag(ScheduledTypeMask.Video) Then writer.WriteStringValue("video")
        If mask.HasFlag(ScheduledTypeMask.Documents) Then writer.WriteStringValue("documents")
        writer.WriteEndArray()
    End Sub

    Private Shared Function ActionToString(a As ScheduledAction) As String
        Select Case a
            Case ScheduledAction.Copy : Return "copy"
            Case ScheduledAction.Move : Return "move"
            Case ScheduledAction.Delete : Return "delete"
            Case ScheduledAction.Archive : Return "archive"
            Case ScheduledAction.Backup : Return "backup"
            Case Else : Return "copy"
        End Select
    End Function

    Private Shared Function TimeFilterToString(f As ScheduledTimeFilter) As String
        Select Case f
            Case ScheduledTimeFilter.SinceLastRun : Return "since-last-run"
            Case ScheduledTimeFilter.LastHour : Return "last-hour"
            Case ScheduledTimeFilter.LastDay : Return "last-day"
            Case Else : Return "all"
        End Select
    End Function

    Private Shared Function ConflictToString(c As ScheduledConflict) As String
        Select Case c
            Case ScheduledConflict.Overwrite : Return "overwrite"
            Case ScheduledConflict.KeepBoth : Return "keep-both"
            Case Else : Return "skip"
        End Select
    End Function

    Private Shared Function OutcomeToString(o As ScheduledRunOutcome) As String
        Select Case o
            Case ScheduledRunOutcome.Ok : Return "ok"
            Case ScheduledRunOutcome.Failed : Return "failed"
            Case ScheduledRunOutcome.Refused : Return "refused"
            Case ScheduledRunOutcome.Skipped : Return "skipped"
            Case ScheduledRunOutcome.Cancelled : Return "cancelled"
            Case Else : Return "none"
        End Select
    End Function

    Private Shared Function HaltToString(h As ScheduledHalt) As String
        Select Case h
            Case ScheduledHalt.ResourceRemoved : Return "resource-removed"
            Case ScheduledHalt.Wall, ScheduledHalt.InvalidDefinition : Return "stopped"
            Case Else : Return "none"
        End Select
    End Function

    Private Function HandleSave(root As JsonElement) As String
        Dim defEl As JsonElement = Nothing
        If Not root.TryGetProperty("definition", defEl) OrElse defEl.ValueKind <> JsonValueKind.Object Then
            Return Refusal("invalid-payload", "definition")
        End If

        ' Strict Item FS rule: Never ignore unknown members of definition, location or conditions
        Dim allowedDefProps As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "name", "enabled", "action", "source", "destination", "fileTypes", "timeFilter",
            "conditions", "at", "intervalMinutes", "conflict", "deleteOriginalsAfterVerify", "keepCount", "sealed"
        }

        For Each prop As JsonProperty In defEl.EnumerateObject()
            If Not allowedDefProps.Contains(prop.Name) Then
                Return Refusal("unsupported", "definition." & prop.Name)
            End If
            If prop.NameEquals("sealed") Then
                ' A request that carries sealed is refused unsupported (item FS)
                Return Refusal("unsupported", "definition.sealed")
            End If
        Next

        ' A create has no operationId. A change names one and carries exactly the members it changes: every other member
        ' stays as the schedule has it (item FS). The change works on a copy, so a refusal anywhere below changes nothing.
        Dim opIdEl As JsonElement = Nothing
        Dim opId As String = ""
        If root.TryGetProperty("operationId", opIdEl) AndAlso opIdEl.ValueKind <> JsonValueKind.Null Then
            If opIdEl.ValueKind <> JsonValueKind.String Then Return Refusal("invalid-payload", "operationId")
            opId = opIdEl.GetString()
        End If
        Dim isCreate As Boolean = String.IsNullOrWhiteSpace(opId)

        Dim op As ScheduledOperation = Nothing
        If isCreate Then
            ' The engine assigns the id (a GUID, item FS operationId shape) and the creation time
            op = New ScheduledOperation With {.Id = ""}
        Else
            op = _engine.TryGet(opId)
            If op Is Nothing Then Return Refusal("not-found", "operationId")
            ' The contract describes five actions; a schedule of another one is not changed from here
            If Not IsContractAction(op.Action) Then Return Refusal("unsupported", "operationId")
        End If

        ' action
        Dim actionEl As JsonElement = Nothing
        If defEl.TryGetProperty("action", actionEl) Then
            If actionEl.ValueKind <> JsonValueKind.String Then Return Refusal("invalid-payload", "definition.action")
            Select Case actionEl.GetString().Trim().ToLowerInvariant()
                Case "copy" : op.Action = ScheduledAction.Copy
                Case "move" : op.Action = ScheduledAction.Move
                Case "delete" : op.Action = ScheduledAction.Delete
                Case "archive" : op.Action = ScheduledAction.Archive
                Case "backup" : op.Action = ScheduledAction.Backup
                Case Else
                    Return Refusal("unsupported", "definition.action")
            End Select
        ElseIf isCreate Then
            Return Refusal("invalid-payload", "definition.action")
        End If

        ' source: replaced whole when carried
        Dim srcEl As JsonElement = Nothing
        If defEl.TryGetProperty("source", srcEl) Then
            If srcEl.ValueKind <> JsonValueKind.Object Then Return Refusal("invalid-payload", "definition.source")
            Dim srcLocationRefusal As String = ValidateLocation(srcEl, "definition.source")
            If srcLocationRefusal IsNot Nothing Then Return srcLocationRefusal
            op.Source = ResourceLocation(srcEl.GetProperty("resourceId").GetString(), srcEl.GetProperty("path").GetString())
            op.Source.Recursive = True
        ElseIf isCreate Then
            Return Refusal("invalid-payload", "definition.source")
        End If

        ' destination: replaced whole when carried; absent for a delete, and required for every other action
        Dim dstEl As JsonElement = Nothing
        Dim hasDestination As Boolean = defEl.TryGetProperty("destination", dstEl)
        If op.Action = ScheduledAction.Delete Then
            If hasDestination Then Return Refusal("invalid-payload", "definition.destination")
            op.Destination = New ScheduledLocation()
        ElseIf hasDestination Then
            If dstEl.ValueKind <> JsonValueKind.Object Then Return Refusal("invalid-payload", "definition.destination")
            Dim dstLocationRefusal As String = ValidateLocation(dstEl, "definition.destination")
            If dstLocationRefusal IsNot Nothing Then Return dstLocationRefusal
            op.Destination = ResourceLocation(dstEl.GetProperty("resourceId").GetString(), dstEl.GetProperty("path").GetString())
        ElseIf op.Destination.IsEmpty Then
            ' A create, or the change of a delete into an action that needs one
            Return Refusal("invalid-payload", "definition.destination")
        End If

        ' conditions: validated when carried
        Dim condEl As JsonElement = Nothing
        If defEl.TryGetProperty("conditions", condEl) Then
            If condEl.ValueKind <> JsonValueKind.Object Then
                Return Refusal("invalid-payload", "definition.conditions")
            End If
            Dim allowedCondProps As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "nameMask", "excludeMask", "minAgeHours", "maxAgeHours", "minSizeBytes", "maxSizeBytes", "includeHidden"
            }
            For Each prop As JsonProperty In condEl.EnumerateObject()
                If Not allowedCondProps.Contains(prop.Name) Then
                    Return Refusal("unsupported", "definition.conditions." & prop.Name)
                End If
            Next
        End If

        ' at: replaced whole when carried
        Dim atEl As JsonElement = Nothing
        If defEl.TryGetProperty("at", atEl) Then
            If atEl.ValueKind <> JsonValueKind.Object Then Return Refusal("invalid-payload", "definition.at")
            Dim hourEl As JsonElement = Nothing
            Dim minEl As JsonElement = Nothing
            If Not atEl.TryGetProperty("hour", hourEl) OrElse Not atEl.TryGetProperty("minute", minEl) Then
                Return Refusal("invalid-payload", "definition.at")
            End If
            Dim hour As Integer = hourEl.GetInt32()
            Dim minute As Integer = minEl.GetInt32()
            If hour < 0 OrElse hour > 23 OrElse minute < 0 OrElse minute > 59 Then
                Return Refusal("invalid-payload", "definition.at")
            End If
            op.AnchorMinutes = hour * 60 + minute
        ElseIf isCreate Then
            Return Refusal("invalid-payload", "definition.at")
        End If

        ' intervalMinutes
        Dim intEl As JsonElement = Nothing
        If defEl.TryGetProperty("intervalMinutes", intEl) Then
            If intEl.ValueKind <> JsonValueKind.Number OrElse intEl.GetInt32() < 15 Then
                Return Refusal("invalid-payload", "definition.intervalMinutes")
            End If
            op.IntervalMinutes = intEl.GetInt32()
        ElseIf isCreate Then
            Return Refusal("invalid-payload", "definition.intervalMinutes")
        End If

        ' File types and time filter: parsed before anything is applied, so an unknown word refuses the whole request
        Dim typeMask As ScheduledTypeMask = ScheduledTypeMask.AllFiles
        Dim fileTypesEl As JsonElement = Nothing
        Dim hasFileTypes As Boolean = defEl.TryGetProperty("fileTypes", fileTypesEl)
        If hasFileTypes Then
            Dim maskRefusal As String = ParseFileTypes(fileTypesEl, typeMask)
            If maskRefusal IsNot Nothing Then Return maskRefusal
        End If
        Dim timeFilter As ScheduledTimeFilter = ScheduledTimeFilter.All
        Dim timeFilterEl As JsonElement = Nothing
        Dim hasTimeFilter As Boolean = defEl.TryGetProperty("timeFilter", timeFilterEl)
        If hasTimeFilter Then
            If timeFilterEl.ValueKind <> JsonValueKind.String Then Return Refusal("invalid-payload", "definition.timeFilter")
            Select Case timeFilterEl.GetString().Trim().ToLowerInvariant()
                Case "all" : timeFilter = ScheduledTimeFilter.All
                Case "since-last-run" : timeFilter = ScheduledTimeFilter.SinceLastRun
                Case "last-hour" : timeFilter = ScheduledTimeFilter.LastHour
                Case "last-day" : timeFilter = ScheduledTimeFilter.LastDay
                Case Else : Return Refusal("unsupported", "definition.timeFilter")
            End Select
        End If

        ' Apply the members the request carries
        Dim nameEl As JsonElement = Nothing
        If defEl.TryGetProperty("name", nameEl) Then op.Name = nameEl.GetString()
        Dim enabledEl As JsonElement = Nothing
        If defEl.TryGetProperty("enabled", enabledEl) Then op.Enabled = enabledEl.GetBoolean()

        ' Conditions are replaced whole when carried (item FS); type mask and time filter are their own members
        If isCreate OrElse condEl.ValueKind = JsonValueKind.Object Then
            Dim kept As ScheduledConditions = op.Conditions
            op.Conditions = New ScheduledConditions With {.TypeMask = kept.TypeMask, .TimeFilter = kept.TimeFilter}
        End If
        If hasFileTypes Then op.Conditions.TypeMask = typeMask
        If hasTimeFilter Then op.Conditions.TimeFilter = timeFilter

        If condEl.ValueKind = JsonValueKind.Object Then
            Dim c As ScheduledConditions = op.Conditions
            Dim maskEl As JsonElement = Nothing
            If condEl.TryGetProperty("nameMask", maskEl) Then c.NameMask = If(maskEl.GetString(), "")
            If condEl.TryGetProperty("excludeMask", maskEl) Then c.ExcludeMask = If(maskEl.GetString(), "")
            Dim hidEl As JsonElement = Nothing
            If condEl.TryGetProperty("includeHidden", hidEl) Then c.IncludeHidden = hidEl.GetBoolean()
            Dim numEl As JsonElement = Nothing
            If condEl.TryGetProperty("minAgeHours", numEl) Then c.MinAgeHours = numEl.GetInt64()
            If condEl.TryGetProperty("maxAgeHours", numEl) Then c.MaxAgeHours = numEl.GetInt64()
            If condEl.TryGetProperty("minSizeBytes", numEl) Then c.MinSizeBytes = numEl.GetInt64()
            If condEl.TryGetProperty("maxSizeBytes", numEl) Then c.MaxSizeBytes = numEl.GetInt64()
        End If

        Dim conflictEl As JsonElement = Nothing
        If defEl.TryGetProperty("conflict", conflictEl) Then
            Select Case conflictEl.GetString().Trim().ToLowerInvariant()
                Case "overwrite" : op.Conflict = ScheduledConflict.Overwrite
                Case "keep-both" : op.Conflict = ScheduledConflict.KeepBoth
                Case Else : op.Conflict = ScheduledConflict.Skip
            End Select
        End If

        Dim delOrigVerifyEl As JsonElement = Nothing
        If defEl.TryGetProperty("deleteOriginalsAfterVerify", delOrigVerifyEl) Then
            op.DeleteOriginalsAfterVerify = delOrigVerifyEl.GetBoolean()
        End If

        Dim keepCountEl As JsonElement = Nothing
        If defEl.TryGetProperty("keepCount", keepCountEl) Then
            Dim keep As Integer = 0
            If keepCountEl.ValueKind <> JsonValueKind.Number OrElse Not keepCountEl.TryGetInt32(keep) OrElse
               keep < ScheduledOperation.MinBackupKeepCount OrElse keep > ScheduledOperation.MaxBackupKeepCount Then
                Return Refusal("invalid-payload", "definition.keepCount")
            End If
            op.BackupKeepCount = keep
        End If

        ' A stored side outside the resource root - the root moved, or the schedule was stored by another build -
        ' names a folder the registry cannot confirm, and the wire has no spelling for it: a change of such a
        ' schedule is refused as not-found (a delete needs no location and still works).
        If op.Source.Kind = ScheduledLocationKind.Resource Then
            Dim srcRootFacts As ScheduledResourceFacts = ResourceFacts.Invoke(op.Source.ResourceId)
            If srcRootFacts IsNot Nothing AndAlso srcRootFacts.Exists AndAlso srcRootFacts.RootPath.Length > 0 AndAlso
               Not String.IsNullOrWhiteSpace(op.Source.Path) AndAlso
               Not ScheduledValidation.InsideRoot(op.Source.Path, srcRootFacts.RootPath) Then
                Return Refusal("not-found", "definition.source")
            End If
        End If
        If op.NeedsDestination AndAlso op.Destination.Kind = ScheduledLocationKind.Resource Then
            Dim dstRootFacts As ScheduledResourceFacts = ResourceFacts.Invoke(op.Destination.ResourceId)
            If dstRootFacts IsNot Nothing AndAlso dstRootFacts.Exists AndAlso dstRootFacts.RootPath.Length > 0 AndAlso
               Not String.IsNullOrWhiteSpace(op.Destination.Path) AndAlso
               Not ScheduledValidation.InsideRoot(op.Destination.Path, dstRootFacts.RootPath) Then
                Return Refusal("not-found", "definition.destination")
            End If
        End If

        ' Read-only places, judged on the sides as they stand after the change: a read-only root is copied out of, never
        ' moved from, deleted in, written to, or have its originals deleted after an archive
        If op.Source.Kind = ScheduledLocationKind.Resource Then
            Dim srcFacts As ScheduledResourceFacts = ResourceFacts.Invoke(op.Source.ResourceId)
            If srcFacts IsNot Nothing AndAlso srcFacts.IsReadOnly Then
                If op.Action = ScheduledAction.Move OrElse op.Action = ScheduledAction.Delete OrElse
                   (op.Action = ScheduledAction.Archive AndAlso op.DeleteOriginalsAfterVerify) Then
                    Return Refusal("invalid-payload", "definition.source")
                End If
            End If
        End If
        If op.NeedsDestination AndAlso op.Destination.Kind = ScheduledLocationKind.Resource Then
            Dim dstFacts As ScheduledResourceFacts = ResourceFacts.Invoke(op.Destination.ResourceId)
            If dstFacts IsNot Nothing AndAlso dstFacts.IsReadOnly Then
                Return Refusal("invalid-payload", "definition.destination")
            End If
        End If

        ' A backup takes a folder of this disk, an SMB resource reached as its UNC path while the run holds the share's
        ' connection (L0342), or an SFTP resource reached through the run's staging and a checked remote publication
        ' (L0343). Any other directory resource has no such path on this device, so a request that would store one is an
        ' honest refusal (item FS), never an invalid payload
        If op.Action = ScheduledAction.Backup Then
            If Not BackupSideOpensByPath(op.Source) Then Return Refusal("unsupported", "definition.source")
            If Not BackupSideOpensByPath(op.Destination) Then Return Refusal("unsupported", "definition.destination")
        End If

        ' Saved through the engine: the same corrections, refusals and arming as the editor's own save
        Dim savedId As String = ""
        Dim issues As List(Of ScheduledIssue) = _engine.Upsert(op, savedId)
        If issues IsNot Nothing AndAlso issues.Count > 0 Then
            Return Refusal("invalid-payload", "definition")
        End If
        Dim saved As ScheduledOperation = _engine.TryGet(savedId)
        If saved Is Nothing Then saved = op

        Using ms As New MemoryStream()
            Using writer As New Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteNumber("v", 1)
                writer.WriteString("outcome", "ok")
                writer.WriteString("operationId", saved.Id)
                writer.WritePropertyName("operation")
                WriteOperationObject(writer, saved, IsRunningNow(saved.Id))
                writer.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    ''' <summary>A side named by a directory resource and a folder inside it. The wire path is "<c>/</c>"-separated
    ''' and relative to the resource root, empty for the root itself (item FS); the stored form is the provider's
    ''' own spelling starting at that root - the folder the editor's picker would name.</summary>
    ''' <summary>A backup side names a folder this device can back up: a folder of this disk, an SMB resource reached as its
    ''' UNC path, or an SFTP resource backed up through the run's staging (L0342, L0343).</summary>
    Private Function BackupSideOpensByPath(location As ScheduledLocation) As Boolean
        If location.Kind = ScheduledLocationKind.Path Then Return True
        If location.Kind <> ScheduledLocationKind.Resource Then Return False
        Dim facts As ScheduledResourceFacts = ResourceFacts.Invoke(location.ResourceId)
        Return facts IsNot Nothing AndAlso facts.Exists AndAlso Not facts.IsUnsupported
    End Function

    Private Function ResourceLocation(resourceId As String, wirePath As String) As ScheduledLocation
        Dim name As String = ""
        Try
            Dim desc As ResourceDescriptor = ScheduledResourceRegistry.Find(resourceId)
            If desc IsNot Nothing Then name = If(desc.DisplayName, "")
        Catch
        End Try
        Return New ScheduledLocation With {
            .Kind = ScheduledLocationKind.Resource,
            .ResourceId = If(resourceId, ""),
            .ResourceName = name,
            .Path = StoredPathOf(ResourceFacts.Invoke(resourceId), wirePath)
        }
    End Function

    ''' <summary>The stored form of a wire path: the folder starting at the resource root, joined with the root's
    ''' own separator so the engine's "inside the root" rule and the provider see the same folder. A resource
    ''' without a root keeps the wire path as it came.</summary>
    Private Shared Function StoredPathOf(facts As ScheduledResourceFacts, wirePath As String) As String
        Dim relative As String = If(wirePath, "").Replace("\"c, "/"c).Trim("/"c)
        Dim root As String = If(facts Is Nothing, "", If(facts.RootPath, "").Trim())
        If relative.Length = 0 Then Return root
        If root.Length = 0 Then Return relative
        Dim separator As Char = If(root.Contains("\"c) AndAlso Not root.Contains("/"c), "\"c, "/"c)
        Return root.TrimEnd(separator) & separator & relative.Replace("/"c, separator)
    End Function

    ''' <summary>The wire form of a stored path: "<c>/</c>"-separated and relative to the resource root, empty for
    ''' the root itself - what a client sends back to name the same folder. Nothing when the stored folder is not
    ''' inside the root (the root moved, or the schedule was stored by another build): the wire has no word
    ''' for it, and the caller answers only a label.</summary>
    Private Shared Function WirePathOf(facts As ScheduledResourceFacts, storedPath As String) As String
        Dim stored As String = If(storedPath, "").Trim()
        If stored.Length = 0 Then Return ""
        Dim root As String = If(facts Is Nothing, "", If(facts.RootPath, "").Trim())
        Dim windows As Boolean = root.StartsWith("\\", StringComparison.Ordinal) OrElse root.Contains("\"c)
        Dim separator As Char = If(windows, "\"c, "/"c)
        stored = stored.Replace(If(windows, "/"c, "\"c), separator).TrimEnd(separator)
        If root.Length = 0 Then Return stored.Replace(separator, "/"c)
        Dim cleanedRoot As String = root.Replace(If(windows, "/"c, "\"c), separator).TrimEnd(separator)
        If cleanedRoot.Length = 0 Then Return stored.Replace(separator, "/"c)
        Dim comparison As StringComparison = If(windows, StringComparison.OrdinalIgnoreCase, StringComparison.Ordinal)
        If String.Equals(stored, cleanedRoot, comparison) Then Return ""
        If stored.StartsWith(cleanedRoot & separator, comparison) Then
            Return stored.Substring(cleanedRoot.Length + 1).Replace(separator, "/"c)
        End If
        Return Nothing
    End Function

    ''' <summary>"all" alone, or an array of the four type words (item FS). Anything else is refused, never ignored.</summary>
    Private Shared Function ParseFileTypes(el As JsonElement, ByRef mask As ScheduledTypeMask) As String
        If el.ValueKind = JsonValueKind.String Then
            If String.Equals(el.GetString(), "all", StringComparison.OrdinalIgnoreCase) Then
                mask = ScheduledTypeMask.AllFiles
                Return Nothing
            End If
            Return Refusal("unsupported", "definition.fileTypes")
        End If
        If el.ValueKind <> JsonValueKind.Array Then Return Refusal("invalid-payload", "definition.fileTypes")
        Dim result As ScheduledTypeMask = ScheduledTypeMask.None
        For Each item As JsonElement In el.EnumerateArray()
            If item.ValueKind <> JsonValueKind.String Then Return Refusal("invalid-payload", "definition.fileTypes")
            Select Case item.GetString().Trim().ToLowerInvariant()
                Case "images" : result = result Or ScheduledTypeMask.Images
                Case "audio" : result = result Or ScheduledTypeMask.Audio
                Case "video" : result = result Or ScheduledTypeMask.Video
                Case "documents" : result = result Or ScheduledTypeMask.Documents
                Case Else : Return Refusal("unsupported", "definition.fileTypes")
            End Select
        Next
        If result = ScheduledTypeMask.None Then Return Refusal("invalid-payload", "definition.fileTypes")
        mask = result
        Return Nothing
    End Function

    Private Function ValidateLocation(locEl As JsonElement, memberPath As String) As String
        Dim allowedLocProps As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "deviceId", "resourceId", "path"
        }
        For Each prop As JsonProperty In locEl.EnumerateObject()
            If Not allowedLocProps.Contains(prop.Name) Then
                Return Refusal("unsupported", memberPath & "." & prop.Name)
            End If
        Next

        Dim resIdEl As JsonElement = Nothing
        If Not locEl.TryGetProperty("resourceId", resIdEl) OrElse String.IsNullOrWhiteSpace(resIdEl.GetString()) Then
            Return Refusal("invalid-payload", memberPath & ".resourceId")
        End If
        Dim resId As String = resIdEl.GetString()

        Dim facts As ScheduledResourceFacts = ResourceFacts.Invoke(resId)
        If facts Is Nothing OrElse Not facts.Exists Then
            Return Refusal("not-found", memberPath)
        End If

        Dim pathEl As JsonElement = Nothing
        If Not locEl.TryGetProperty("path", pathEl) OrElse pathEl.ValueKind <> JsonValueKind.String Then
            Return Refusal("invalid-payload", memberPath & ".path")
        End If
        Dim p As String = pathEl.GetString()
        If p.StartsWith("/"c) OrElse p.Contains("..") Then
            Return Refusal("invalid-payload", memberPath & ".path")
        End If

        Return Nothing
    End Function

    Private Function HandleDelete(root As JsonElement) As String
        Dim opIdEl As JsonElement = Nothing
        If Not root.TryGetProperty("operationId", opIdEl) OrElse String.IsNullOrWhiteSpace(opIdEl.GetString()) Then
            Return Refusal("invalid-payload", "operationId")
        End If
        Dim opId As String = opIdEl.GetString()
        Dim op As ScheduledOperation = _engine.TryGet(opId)
        If op Is Nothing Then
            Return Refusal("not-found", "operationId")
        End If
        _engine.Delete(opId)

        Return "{""v"":1,""outcome"":""ok""}"
    End Function

    Private Function HandleRun(root As JsonElement) As String
        Dim opIdEl As JsonElement = Nothing
        If Not root.TryGetProperty("operationId", opIdEl) OrElse String.IsNullOrWhiteSpace(opIdEl.GetString()) Then
            Return Refusal("invalid-payload", "operationId")
        End If
        Dim opId As String = opIdEl.GetString()
        Dim op As ScheduledOperation = _engine.TryGet(opId)
        If op Is Nothing Then
            Return Refusal("not-found", "operationId")
        End If

        Dim outcome As TriggerOutcome = _engine.RunNow(opId)
        Dim started As Boolean = (outcome = TriggerOutcome.Started)

        Return "{""v"":1,""outcome"":""ok"",""started"":" & started.ToString().ToLowerInvariant() & "}"
    End Function

    Private Function HandleHistory(root As JsonElement) As String
        Dim opIdEl As JsonElement = Nothing
        If Not root.TryGetProperty("operationId", opIdEl) OrElse String.IsNullOrWhiteSpace(opIdEl.GetString()) Then
            Return Refusal("invalid-payload", "operationId")
        End If
        Dim opId As String = opIdEl.GetString()

        Dim limit As Integer = 20
        Dim limitEl As JsonElement = Nothing
        If root.TryGetProperty("limit", limitEl) AndAlso limitEl.ValueKind = JsonValueKind.Number Then
            limit = Math.Max(1, Math.Min(50, limitEl.GetInt32()))
        End If

        Dim entries As List(Of JournalEntry) = Nothing
        Try
            ' Newest first; the whole kept history (7.5 caps it at 500 lines), so an operation with few runs is still found
            Dim allEntries As List(Of JournalEntry) = _engine.Journal.ReadRecent(ScheduledJournal.HistoryKeepLines)
            entries = New List(Of JournalEntry)()
            For Each e As JournalEntry In allEntries
                If String.Equals(e.OperationId, opId, StringComparison.OrdinalIgnoreCase) AndAlso e.Kind = "run" Then
                    entries.Add(e)
                    If entries.Count >= limit Then Exit For
                End If
            Next
        Catch
            entries = New List(Of JournalEntry)()
        End Try

        Using ms As New MemoryStream()
            Using writer As New Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteNumber("v", 1)
                writer.WriteString("outcome", "ok")
                writer.WriteStartArray("runs")
                For Each e As JournalEntry In entries
                    writer.WriteStartObject()
                    Dim startUtc As DateTime = e.Timestamp.ToUniversalTime()
                    writer.WriteString("startedAt", startUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                    Dim finUtc As DateTime = startUtc.AddMilliseconds(e.DurationMs)
                    writer.WriteString("finishedAt", finUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                    writer.WriteString("trigger", MapJournalTrigger(e.Trigger))
                    writer.WriteString("outcome", MapJournalOutcome(e.Code))
                    writer.WriteNumber("files", e.Count)
                    writer.WriteNumber("failed", e.Failed)
                    writer.WriteEndObject()
                Next
                writer.WriteEndArray()
                writer.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    Private Shared Function MapJournalTrigger(t As String) As String
        Select Case If(t, "").ToLowerInvariant()
            Case "schedule" : Return "schedule"
            Case "runnow", "runall" : Return "run-now"
            Case "catchup" : Return "catch-up"
            Case Else : Return "schedule"
        End Select
    End Function

    Private Shared Function MapJournalOutcome(c As String) As String
        Select Case If(c, "").ToLowerInvariant()
            Case "ok" : Return "ok"
            Case "failed" : Return "failed"
            Case "refused" : Return "refused"
            Case "skipped" : Return "skipped"
            Case "cancelled" : Return "cancelled"
            Case "wall" : Return "refused"
            Case Else : Return "ok"
        End Select
    End Function

End Class
#End If
