Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes
Imports Microsoft.VisualBasic

' The store of scheduled operations (ticket 222, section 4 "Storage"): one JSON file per operation under
' the user's own %LOCALAPPDATA%\SZA\FastMediaSorter\ tree - writable inside the MSIX package - and the
' machine-level switches in the shared SZA\FastMediaSorter hive.
'
' The worker's data directory (%LOCALAPPDATA%\FastMediaSorterCompanion\) is NEVER used or touched: it
' holds the host key every paired phone has pinned.
'
' TWO WRITERS, ONE FILE. The editor changes the DEFINITION on the UI thread; the run changes the RUN
' STATE (the record, the halt) on a pool thread. A whole-object write from either would silently undo
' the other's, so each writes only its own half: the definition is saved with the run state read back
' from disk, and the run state is patched into the stored document in place - which also means a file
' written by a newer build (a field, an action this one does not know) is never rewritten into
' something it did not say.
Friend NotInheritable Class ScheduledStore

    Private ReadOnly folder As String
    Private ReadOnly gate As New Object()

    Friend Sub New(folder As String)
        Me.folder = folder
    End Sub

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\scheduled-operations.</summary>
    Friend Shared Function DefaultFolder() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "SZA", "FastMediaSorter", "scheduled-operations")
    End Function

    Friend ReadOnly Property FolderPath As String
        Get
            Return folder
        End Get
    End Property

    Private Function FileFor(id As String) As String
        ' An id is a GUID this program wrote; anything else could name a path outside the folder.
        Dim safe As String = New String(id.Where(Function(c) Char.IsLetterOrDigit(c) OrElse c = "-"c).ToArray())
        If safe.Length = 0 OrElse safe <> id Then Throw New ArgumentException("Not an operation id.", NameOf(id))
        Return Path.Combine(folder, safe & ".json")
    End Function

    ''' <summary>Every readable operation, oldest first. A file that cannot be read is logged and skipped:
    ''' one damaged definition must not hide the rest.</summary>
    Friend Function LoadAll() As List(Of ScheduledOperation)
        Dim result As New List(Of ScheduledOperation)()
        Dim legacyPolicy As MissedRunPolicy = ScheduledSettings.MissedRuns
        Dim migratedAny As Boolean
        SyncLock gate
            If Not Directory.Exists(folder) Then Return result
            For Each file As String In Directory.EnumerateFiles(folder, "*.json")
                Try
                    Dim op As ScheduledOperation = ScheduledJson.Deserialize(System.IO.File.ReadAllText(file, Encoding.UTF8))
                    If op Is Nothing Then
                        AppFileLogger.WriteLine("Scheduled operations: unreadable definition skipped: " & Path.GetFileName(file))
                    Else
                        If Not op.HasExplicitRetryMissedRuns Then
                            op.RetryMissedRunsAtStart = (legacyPolicy = MissedRunPolicy.RunOnceAtStart)
                            migratedAny = True
                            If op.Halt <> ScheduledHalt.InvalidDefinition Then
                                Try : WriteAtomic(file, ScheduledJson.Serialize(op)) : Catch : End Try
                            End If
                        End If
                        result.Add(op)
                    End If
                Catch ex As Exception
                    AppFileLogger.LogException("Scheduled operations: reading " & Path.GetFileName(file), ex)
                End Try
            Next
        End SyncLock
        If migratedAny Then ScheduledSettings.MissedRunsMigrated = True
        result.Sort(Function(a, b) a.CreatedAt.CompareTo(b.CreatedAt))
        Return result
    End Function

    Friend Function TryLoad(id As String) As ScheduledOperation
        SyncLock gate
            Try
                Dim file As String = FileFor(id)
                If Not System.IO.File.Exists(file) Then Return Nothing
                Return ScheduledJson.Deserialize(System.IO.File.ReadAllText(file, Encoding.UTF8))
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: loading " & id, ex)
                Return Nothing
            End Try
        End SyncLock
    End Function

    ''' <summary>
    ''' Writes the definition. The run state (record, halt) is read back from disk when the operation is
    ''' already stored, so an edit never undoes what a run wrote in between - except that an edit is the
    ''' user fixing the operation: <paramref name="clearHalt"/> lifts a wall.
    ''' </summary>
    Friend Sub SaveDefinition(op As ScheduledOperation, Optional clearHalt As Boolean = False)
        SyncLock gate
            Directory.CreateDirectory(folder)
            Dim file As String = FileFor(op.Id)
            Dim toWrite As ScheduledOperation = op.Clone()
            If System.IO.File.Exists(file) Then
                Dim stored As ScheduledOperation = ScheduledJson.Deserialize(System.IO.File.ReadAllText(file, Encoding.UTF8))
                If stored IsNot Nothing Then
                    toWrite.Record = stored.Record
                    If Not clearHalt Then
                        toWrite.Halt = stored.Halt
                        toWrite.HaltDetail = stored.HaltDetail
                    End If
                End If
            End If
            If clearHalt Then
                toWrite.Halt = ScheduledHalt.None
                toWrite.HaltDetail = ""
            End If
            WriteAtomic(file, ScheduledJson.Serialize(toWrite))
        End SyncLock
    End Sub

    ''' <summary>Writes an imported operation whole (with its fresh record and imported halt state).</summary>
    Friend Sub SaveImported(op As ScheduledOperation)
        SyncLock gate
            Directory.CreateDirectory(folder)
            Dim file As String = FileFor(op.Id)
            WriteAtomic(file, ScheduledJson.Serialize(op))
        End SyncLock
    End Sub

    ''' <summary>Patches the run state into the stored document, leaving every other field as written.
    ''' Returns False when the operation is no longer stored (deleted while it ran).</summary>
    Friend Function SaveRunState(id As String, record As ScheduledRunRecord, halt As ScheduledHalt, haltDetail As String) As Boolean
        SyncLock gate
            Dim file As String = FileFor(id)
            If Not System.IO.File.Exists(file) Then Return False
            Dim node As JsonNode = JsonNode.Parse(System.IO.File.ReadAllText(file, Encoding.UTF8))
            Dim root As JsonObject = TryCast(node, JsonObject)
            If root Is Nothing Then Return False
            root("record") = JsonNode.Parse(ScheduledJson.SerializeRecord(record))
            ' The halt of a definition this build cannot read stays as its writer left it.
            If Not (root.ContainsKey("action") AndAlso Not IsKnownAction(root("action"))) Then
                root("halt") = ScheduledJson.HaltText(halt)
                root("haltDetail") = If(haltDetail, "")
            End If
            WriteAtomic(file, root.ToJsonString(New System.Text.Json.JsonSerializerOptions With {.WriteIndented = True}))
            Return True
        End SyncLock
    End Function

    Private Shared Function IsKnownAction(node As JsonNode) As Boolean
        Dim text As String = Nothing
        Try
            text = node?.GetValue(Of String)()
        Catch
        End Try
        Dim action As ScheduledAction
        Return ScheduledJson.TryParseAction(text, action)
    End Function

    Friend Sub Delete(id As String)
        SyncLock gate
            Dim file As String = FileFor(id)
            If System.IO.File.Exists(file) Then System.IO.File.Delete(file)
        End SyncLock
    End Sub

    ''' <summary>tmp + swap: no failure leaves half a definition (the same rule as the image writer).</summary>
    Private Shared Sub WriteAtomic(target As String, text As String)
        Dim temp As String = target & ".tmp"
        System.IO.File.WriteAllText(temp, text, New UTF8Encoding(False))
        If System.IO.File.Exists(target) Then
            System.IO.File.Replace(temp, target, Nothing)
        Else
            System.IO.File.Move(temp, target)
        End If
    End Sub

End Class

''' <summary>
''' The machine-level switches of 4.4: this PC, not one operation. Kept in the shared hive, each read
''' fresh, each written the moment it changes - so the tray, the page and a second window agree and a
''' restart changes nothing.
''' </summary>
Friend NotInheritable Class ScheduledSettings

    Private Const FeatureKey As String = "Scheduled_FeatureEnabled"
    Private Const PausedKey As String = "Scheduled_Paused"
    Private Const MissedKey As String = "Scheduled_MissedRuns"
    Private Const MissedMigratedKey As String = "Scheduled_MissedRunsMigrated"
    ''' <summary>The viewer's own setting, read - never written - here (D3): the bin, where one exists.</summary>
    Private Const DeleteToBinKey As String = "DeleteToRecycleBin"

    ''' <summary>Master switch. Follows list emptiness: switched on when an operation is added and off
    ''' when the list becomes empty (<see cref="FollowList"/>); the user may also switch it off while
    ''' operations exist - the timers are then not armed.</summary>
    Friend Shared Property FeatureEnabled As Boolean
        Get
            Return Read(FeatureKey, "1") = "1"
        End Get
        Set(value As Boolean)
            Write(FeatureKey, If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>Durable Pause all: no scheduled run and no Run all now while set; nothing is deleted.</summary>
    Friend Shared Property Paused As Boolean
        Get
            Return Read(PausedKey, "0") = "1"
        End Get
        Set(value As Boolean)
            Write(PausedKey, If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>Legacy machine-level missed-run policy (ticket 222); migrated once into each operation's
    ''' <see cref="ScheduledOperation.RetryMissedRunsAtStart"/> on first load (ticket 231, 3.4).</summary>
    Friend Shared Property MissedRuns As MissedRunPolicy
        Get
            Return If(Read(MissedKey, "start") = "skip", MissedRunPolicy.Skip, MissedRunPolicy.RunOnceAtStart)
        End Get
        Set(value As MissedRunPolicy)
            Write(MissedKey, If(value = MissedRunPolicy.Skip, "skip", "start"))
        End Set
    End Property

    Friend Shared Property MissedRunsMigrated As Boolean
        Get
            Return Read(MissedMigratedKey, "0") = "1"
        End Get
        Set(value As Boolean)
            Write(MissedMigratedKey, If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>The viewer's global "delete to the Recycle Bin" preference, default on, as the viewer
    ''' defaults it. Read at EVERY run (D3): a later change of the setting changes later runs.</summary>
    Friend Shared ReadOnly Property DeleteToRecycleBin As Boolean
        Get
            Return Read(DeleteToBinKey, "1") <> "0"
        End Get
    End Property

    ''' <summary>The feature switch follows the list: on with the first operation, off with the last.</summary>
    Friend Shared Sub FollowList(operationCount As Integer)
        Dim want As Boolean = operationCount > 0
        If FeatureEnabled <> want Then FeatureEnabled = want
    End Sub

    Private Shared Function Read(key As String, fallback As String) As String
        Try
            Return GetSetting(App_name, Second_App_Name, key, fallback)
        Catch
            Return fallback
        End Try
    End Function

    Private Shared Sub Write(key As String, value As String)
        Try
            SaveSetting(App_name, Second_App_Name, key, value)
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: writing " & key, ex)
        End Try
    End Sub

End Class

''' <summary>
''' Share Manager settings export and import (ticket 231, 3.8): carries the scheduled operations
''' (definitions, enable flags, per-operation options, no run state, no secret; resources by id and
''' last-known name). On import, an operation whose resource id is absent on the target PC is imported
''' disabled with <see cref="ScheduledHalt.ResourceRemoved"/> until the user re-points it.
''' </summary>
Friend NotInheritable Class ShareSettingsPackageDto
    Public Property Version As Integer = 1
    Public Property Operations As New List(Of ScheduledOperationDto)()
End Class

''' <summary>What an import did: whether the file read as an export at all, and how many operations went in or were left out.</summary>
Friend NotInheritable Class ShareSettingsImportResult
    Friend Property Readable As Boolean
    Friend Property Imported As Integer
    Friend Property NotImported As Integer
End Class

Friend Module ShareSettingsTransfer

    Private ReadOnly ExportJsonOptions As New System.Text.Json.JsonSerializerOptions With {
        .WriteIndented = True,
        .PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        .PropertyNameCaseInsensitive = True,
        .DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull}

    Friend Function ExportToJson(store As ScheduledStore) As String
        Dim package As New ShareSettingsPackageDto With {.Version = 1}
        If store IsNot Nothing Then
            For Each op As ScheduledOperation In store.LoadAll()
                Dim dto As ScheduledOperationDto = ScheduledJson.ToDto(op)
                ' Ticket 231, 3.8: no run state, no halt state, and no secret in an exported settings file.
                dto.Record = Nothing
                dto.Halt = "none"
                dto.HaltDetail = ""
                package.Operations.Add(dto)
            Next
        End If
        Return System.Text.Json.JsonSerializer.Serialize(package, ExportJsonOptions)
    End Function

    Friend Sub ExportToFile(store As ScheduledStore, filePath As String)
        Dim json As String = ExportToJson(store)
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)))
        File.WriteAllText(filePath, json, New UTF8Encoding(False))
    End Sub

    ''' <summary>Imports the operations of an export. One operation that cannot be saved is counted and skipped, never
    ''' the end of the rest. No machine state is taken from the file: the pause and the feature switch stay this PC's own.</summary>
    Friend Function ImportFromJson(json As String, store As ScheduledStore,
                                   Optional resourceFacts As Func(Of String, ScheduledResourceFacts) = Nothing,
                                   Optional engine As ScheduledEngine = Nothing) As ShareSettingsImportResult
        Dim result As New ShareSettingsImportResult()
        If String.IsNullOrWhiteSpace(json) OrElse store Is Nothing Then Return result
        Dim package As ShareSettingsPackageDto
        Try
            package = System.Text.Json.JsonSerializer.Deserialize(Of ShareSettingsPackageDto)(json, ExportJsonOptions)
        Catch ex As System.Text.Json.JsonException
            Return result
        End Try
        If package Is Nothing OrElse package.Operations Is Nothing Then Return result
        result.Readable = True
        Dim lookup As Func(Of String, ScheduledResourceFacts) = If(resourceFacts, New Func(Of String, ScheduledResourceFacts)(AddressOf ScheduledResourceRegistry.Lookup))
        For Each dto As ScheduledOperationDto In package.Operations
            If dto Is Nothing OrElse String.IsNullOrWhiteSpace(dto.Id) Then
                result.NotImported += 1
                Continue For
            End If
            Try
                dto.Record = New ScheduledRecordDto()
                Dim op As ScheduledOperation = ScheduledJson.FromDto(dto)
                op.Record = New ScheduledRunRecord()
                ' An action this build does not know stays an invalid definition (it must never become a runnable copy); any
                ' other halt of the writer's PC means nothing here.
                If op.Halt <> ScheduledHalt.InvalidDefinition Then
                    op.Halt = ScheduledHalt.None
                    op.HaltDetail = ""
                End If
                Dim missingResourceName As String = FindMissingResourceName(op, lookup)
                If missingResourceName.Length > 0 Then
                    op.Enabled = False
                    op.Halt = ScheduledHalt.ResourceRemoved
                    op.HaltDetail = missingResourceName
                End If
                store.SaveImported(op)
                engine?.ReloadImported(op)
                result.Imported += 1
            Catch ex As Exception
                ' The class only: an id or a path from a foreign file does not belong in a log.
                AppFileLogger.WriteLine("Scheduled operations: one imported operation was not saved (" & ex.GetType().Name & ")")
                result.NotImported += 1
            End Try
        Next
        ScheduledSettings.FollowList(store.LoadAll().Count)
        Return result
    End Function

    Friend Function ImportFromFile(filePath As String, store As ScheduledStore,
                                   Optional resourceFacts As Func(Of String, ScheduledResourceFacts) = Nothing,
                                   Optional engine As ScheduledEngine = Nothing) As ShareSettingsImportResult
        If Not File.Exists(filePath) Then Return New ShareSettingsImportResult()
        Return ImportFromJson(File.ReadAllText(filePath, Encoding.UTF8), store, resourceFacts, engine)
    End Function

    Private Function FindMissingResourceName(op As ScheduledOperation, lookup As Func(Of String, ScheduledResourceFacts)) As String
        If op.Source.Kind = ScheduledLocationKind.Resource Then
            Dim facts As ScheduledResourceFacts = lookup.Invoke(op.Source.ResourceId)
            If facts Is Nothing OrElse Not facts.Exists Then
                Return If(String.IsNullOrEmpty(op.Source.ResourceName), op.Source.ResourceId, op.Source.ResourceName)
            End If
        End If
        If op.NeedsDestination AndAlso op.Destination.Kind = ScheduledLocationKind.Resource Then
            Dim facts As ScheduledResourceFacts = lookup.Invoke(op.Destination.ResourceId)
            If facts Is Nothing OrElse Not facts.Exists Then
                Return If(String.IsNullOrEmpty(op.Destination.ResourceName), op.Destination.ResourceId, op.Destination.ResourceName)
            End If
        End If
        Return ""
    End Function

End Module
