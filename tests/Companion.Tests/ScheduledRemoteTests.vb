#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

Namespace Companion.Tests

    Public Class ScheduledRemoteTests

        <Fact>
        Public Sub Ring_RecordsAndRetrievesAnswer()
            Dim ring As New ScheduledRemoteRing()
            Dim answerBox As String = "{""v"":1,""ct"":""test-result""}"

            ring.Record("dev-1", "req-100", answerBox)

            Dim retrieved As String = Nothing
            Dim found As Boolean = ring.TryGet("dev-1", "req-100", retrieved)

            Assert.True(found)
            Assert.Equal(answerBox, retrieved)

            ' Other requester device gets miss
            Assert.False(ring.TryGet("dev-2", "req-100", retrieved))
            ' Other request id gets miss
            Assert.False(ring.TryGet("dev-1", "req-999", retrieved))
        End Sub

        <Fact>
        Public Sub Ring_EvictsOldestWhenCapacityExceeded()
            Dim ring As New ScheduledRemoteRing()
            For i As Integer = 0 To 260
                ring.Record("dev-1", "req-" & i.ToString(), "{""box"":" & i.ToString() & "}")
            Next

            Dim dummy As String = Nothing
            ' Oldest items should be evicted (first 5 of 261 items)
            Assert.False(ring.TryGet("dev-1", "req-0", dummy))
            Assert.False(ring.TryGet("dev-1", "req-4", dummy))

            ' Newest items must be present
            Assert.True(ring.TryGet("dev-1", "req-260", dummy))
            Assert.True(ring.TryGet("dev-1", "req-100", dummy))
        End Sub

        Private Function CreateTestDispatcher(tempDir As String) As Tuple(Of ScheduledRemoteDispatcher, ScheduledEngine)
            Dim folder As String = Path.Combine(tempDir, "scheduled")
            Directory.CreateDirectory(folder)
            Dim journal As New ScheduledJournal(folder)
            Dim services As ScheduledServices = ScheduledServices.ForProduct(journal)
            Dim store As New ScheduledStore(folder)
            Dim engine As New ScheduledEngine(store, journal, services)
            Dim dispatcher As New ScheduledRemoteDispatcher(engine, "dev-local-1")
            Return Tuple.Create(dispatcher, engine)
        End Function

        <Fact>
        Public Sub Dispatcher_RefusesWhenConsentIsNever()
            Dim tempDir As String = Path.Combine(Path.GetTempPath(), "fms_sched_test_" & Guid.NewGuid().ToString("N"))
            Try
                Dim pair = CreateTestDispatcher(tempDir)
                Dim dispatcher = pair.Item1
                dispatcher.ConsentOverride = ScheduledRemoteConsent.Never

                Dim req As String = "{""v"":1,""verb"":""places""}"
                Dim res As String = dispatcher.Execute("dev-remote-1", "req-1", req)

                Using doc As JsonDocument = JsonDocument.Parse(res)
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("refused", root.GetProperty("outcome").GetString())
                    Assert.Equal("not-permitted", root.GetProperty("reason").GetString())
                End Using
            Finally
                If Directory.Exists(tempDir) Then Directory.Delete(tempDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub Dispatcher_DeclinesMutationsUnderAskConsent()
            Dim tempDir As String = Path.Combine(Path.GetTempPath(), "fms_sched_test_" & Guid.NewGuid().ToString("N"))
            Try
                Dim pair = CreateTestDispatcher(tempDir)
                Dim dispatcher = pair.Item1
                dispatcher.ConsentOverride = ScheduledRemoteConsent.Ask

                ' Read verbs work under Ask
                Dim placesRes As String = dispatcher.Execute("dev-remote-1", "req-1", "{""v"":1,""verb"":""places""}")
                Using doc As JsonDocument = JsonDocument.Parse(placesRes)
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                End Using

                ' Save is declined under Ask (unattended)
                Dim saveReq As String = "{""v"":1,""verb"":""save"",""definition"":{""action"":""copy""}}"
                Dim saveRes As String = dispatcher.Execute("dev-remote-1", "req-2", saveReq)
                Using doc As JsonDocument = JsonDocument.Parse(saveRes)
                    Assert.Equal("refused", doc.RootElement.GetProperty("outcome").GetString())
                    Assert.Equal("declined", doc.RootElement.GetProperty("reason").GetString())
                End Using
            Finally
                If Directory.Exists(tempDir) Then Directory.Delete(tempDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub Dispatcher_StrictItemFSRefusesUnknownDefinitionMembers()
            Dim tempDir As String = Path.Combine(Path.GetTempPath(), "fms_sched_test_" & Guid.NewGuid().ToString("N"))
            Try
                Dim pair = CreateTestDispatcher(tempDir)
                Dim dispatcher = pair.Item1
                dispatcher.ConsentOverride = ScheduledRemoteConsent.Always

                ' Unknown member "unknownFutureField" in definition
                Dim reqUnknown As String = "{""v"":1,""verb"":""save"",""definition"":{""action"":""copy"",""unknownFutureField"":true}}"
                Dim resUnknown As String = dispatcher.Execute("dev-remote-1", "req-3", reqUnknown)

                Using doc As JsonDocument = JsonDocument.Parse(resUnknown)
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("refused", root.GetProperty("outcome").GetString())
                    Assert.Equal("unsupported", root.GetProperty("reason").GetString())
                    Assert.Equal("definition.unknownFutureField", root.GetProperty("member").GetString())
                End Using

                ' Sealed carried in definition is refused unsupported
                Dim reqSealed As String = "{""v"":1,""verb"":""save"",""definition"":{""action"":""backup"",""sealed"":true}}"
                Dim resSealed As String = dispatcher.Execute("dev-remote-1", "req-4", reqSealed)

                Using doc As JsonDocument = JsonDocument.Parse(resSealed)
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("refused", root.GetProperty("outcome").GetString())
                    Assert.Equal("unsupported", root.GetProperty("reason").GetString())
                    Assert.Equal("definition.sealed", root.GetProperty("member").GetString())
                End Using
            Finally
                If Directory.Exists(tempDir) Then Directory.Delete(tempDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub Dispatcher_List_AnswersOkWithOperations()
            Dim tempDir As String = Path.Combine(Path.GetTempPath(), "fms_sched_test_" & Guid.NewGuid().ToString("N"))
            Try
                Dim pair = CreateTestDispatcher(tempDir)
                Dim dispatcher = pair.Item1
                dispatcher.ConsentOverride = ScheduledRemoteConsent.Always

                Dim res As String = dispatcher.Execute("dev-remote-1", "req-list-1", "{""v"":1,""verb"":""list""}")
                Using doc As JsonDocument = JsonDocument.Parse(res)
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("ok", root.GetProperty("outcome").GetString())
                    Assert.True(root.TryGetProperty("operations", Nothing))
                    Assert.False(root.GetProperty("truncated").GetBoolean())
                End Using
            Finally
                If Directory.Exists(tempDir) Then Directory.Delete(tempDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub Dispatcher_Delete_ReturnsNotFoundForMissingId()
            Dim tempDir As String = Path.Combine(Path.GetTempPath(), "fms_sched_test_" & Guid.NewGuid().ToString("N"))
            Try
                Dim pair = CreateTestDispatcher(tempDir)
                Dim dispatcher = pair.Item1
                dispatcher.ConsentOverride = ScheduledRemoteConsent.Always

                Dim res As String = dispatcher.Execute("dev-remote-1", "req-del-1", "{""v"":1,""verb"":""delete"",""operationId"":""non-existent-guid""}")
                Using doc As JsonDocument = JsonDocument.Parse(res)
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("refused", root.GetProperty("outcome").GetString())
                    Assert.Equal("not-found", root.GetProperty("reason").GetString())
                    Assert.Equal("operationId", root.GetProperty("member").GetString())
                End Using
            Finally
                If Directory.Exists(tempDir) Then Directory.Delete(tempDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub Dispatcher_Run_ReturnsNotFoundForMissingId()
            Dim tempDir As String = Path.Combine(Path.GetTempPath(), "fms_sched_test_" & Guid.NewGuid().ToString("N"))
            Try
                Dim pair = CreateTestDispatcher(tempDir)
                Dim dispatcher = pair.Item1
                dispatcher.ConsentOverride = ScheduledRemoteConsent.Always

                Dim res As String = dispatcher.Execute("dev-remote-1", "req-run-1", "{""v"":1,""verb"":""run"",""operationId"":""non-existent-guid""}")
                Using doc As JsonDocument = JsonDocument.Parse(res)
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("refused", root.GetProperty("outcome").GetString())
                    Assert.Equal("not-found", root.GetProperty("reason").GetString())
                    Assert.Equal("operationId", root.GetProperty("member").GetString())
                End Using
            Finally
                If Directory.Exists(tempDir) Then Directory.Delete(tempDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub Dispatcher_History_ReturnsRunsArray()
            Dim tempDir As String = Path.Combine(Path.GetTempPath(), "fms_sched_test_" & Guid.NewGuid().ToString("N"))
            Try
                Dim pair = CreateTestDispatcher(tempDir)
                Dim dispatcher = pair.Item1
                dispatcher.ConsentOverride = ScheduledRemoteConsent.Always

                Dim res As String = dispatcher.Execute("dev-remote-1", "req-hist-1", "{""v"":1,""verb"":""history"",""operationId"":""any-guid"",""limit"":10}")
                Using doc As JsonDocument = JsonDocument.Parse(res)
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("ok", root.GetProperty("outcome").GetString())
                    Assert.True(root.TryGetProperty("runs", Nothing))
                End Using
            Finally
                If Directory.Exists(tempDir) Then Directory.Delete(tempDir, True)
            End Try
        End Sub

    End Class

    ''' <summary>
    ''' Item FS through the dispatcher: a change carries exactly the members it changes and leaves every other as the
    ''' schedule has it. A real engine on a manual clock and recording timers, the resources answered by a stub.
    ''' </summary>
    <Collection(ScheduledCollection.Name)>
    Public Class ScheduledRemoteSaveTests

        Private NotInheritable Class Kit
            Implements IDisposable

            Friend ReadOnly Tree As New ScratchTree()
            Friend ReadOnly Hive As New ScratchHive()
            Friend ReadOnly Clock As New ManualClock(New DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Unspecified))
            Friend ReadOnly Timers As New RecordingTimers()
            Friend ReadOnly Engine As ScheduledEngine
            Friend ReadOnly Dispatcher As ScheduledRemoteDispatcher

            Friend Sub New()
                ' The registry stub carries roots: res-a is rooted at "/mov", res-b at "/share". The engine stores
                ' the folder in the provider's vocabulary, the wire path is relative to the root (item FS). smb-a and
                ' smb-b are SMB shares (L0342), rooted at UNC paths.
                Dim facts As Func(Of String, ScheduledResourceFacts) =
                    Function(id As String)
                        If id.StartsWith("smb-", StringComparison.Ordinal) Then
                            Return New ScheduledResourceFacts With {.Exists = True, .DisplayName = "Share", .IsSmb = True,
                                .RootPath = If(String.Equals(id, "smb-b", StringComparison.Ordinal), "\\nas\sets", "\\nas\photos")}
                        End If
                        Return New ScheduledResourceFacts With {.Exists = True, .DisplayName = "NAS",
                            .RootPath = If(String.Equals(id, "res-b", StringComparison.Ordinal), "/share", "/mov")}
                    End Function
                Dim folder As String = Tree.Dir("state")
                Dim services As New ScheduledServices With {.Clock = AddressOf Clock.Read, .ResourceFacts = facts}
                Engine = New ScheduledEngine(New ScheduledStore(folder), New ScheduledJournal(folder), services, Timers) With {.CatchUpDelay = TimeSpan.Zero}
                Dispatcher = New ScheduledRemoteDispatcher(Engine, "dev-local-1") With {.ConsentOverride = ScheduledRemoteConsent.Always, .ResourceFacts = facts}
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Engine.Dispose()
                Hive.Dispose()
                Tree.Dispose()
            End Sub
        End Class

        Private Const NightlyCopy As String = "{""v"":1,""verb"":""save"",""definition"":{""name"":""nightly"",""action"":""copy""," &
            """source"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""in""}," &
            """destination"":{""deviceId"":""dev-local-1"",""resourceId"":""res-b"",""path"":""out""}," &
            """at"":{""hour"":3,""minute"":30},""intervalMinutes"":1440}}"

        Private Shared Function Send(kit As Kit, json As String) As String
            Return kit.Dispatcher.Execute("dev-remote-1", "req-" & Guid.NewGuid().ToString("N"), json)
        End Function

        Private Shared Function Change(operationId As String, definitionJson As String) As String
            Return "{""v"":1,""verb"":""save"",""operationId"":""" & operationId & """,""definition"":" & definitionJson & "}"
        End Function

        Private Shared Function CreateNightlyCopy(kit As Kit) As String
            Using doc As JsonDocument = JsonDocument.Parse(Send(kit, NightlyCopy))
                Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                Return doc.RootElement.GetProperty("operationId").GetString()
            End Using
        End Function

        Private Shared Sub AssertRefused(answer As String, reason As String, member As String)
            Using doc As JsonDocument = JsonDocument.Parse(answer)
                Assert.Equal("refused", doc.RootElement.GetProperty("outcome").GetString())
                Assert.Equal(reason, doc.RootElement.GetProperty("reason").GetString())
                Assert.Equal(member, doc.RootElement.GetProperty("member").GetString())
            End Using
        End Sub

        <Fact>
        Public Sub Change_carrying_only_enabled_pauses_and_leaves_every_other_member()
            Using kit As New Kit()
                Dim id As String = CreateNightlyCopy(kit)

                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, Change(id, "{""enabled"":false}")))
                    Dim root As JsonElement = doc.RootElement
                    Assert.Equal("ok", root.GetProperty("outcome").GetString())
                    Assert.Equal(id, root.GetProperty("operationId").GetString())
                    Dim definition As JsonElement = root.GetProperty("operation").GetProperty("definition")
                    Assert.False(definition.GetProperty("enabled").GetBoolean())
                    Assert.Equal("nightly", definition.GetProperty("name").GetString())
                    Assert.Equal("copy", definition.GetProperty("action").GetString())
                    Assert.Equal("res-a", definition.GetProperty("source").GetProperty("resourceId").GetString())
                    Assert.Equal("in", definition.GetProperty("source").GetProperty("path").GetString())
                    Assert.Equal("res-b", definition.GetProperty("destination").GetProperty("resourceId").GetString())
                    Assert.Equal("out", definition.GetProperty("destination").GetProperty("path").GetString())
                    Assert.Equal(3, definition.GetProperty("at").GetProperty("hour").GetInt32())
                    Assert.Equal(30, definition.GetProperty("at").GetProperty("minute").GetInt32())
                    Assert.Equal(1440, definition.GetProperty("intervalMinutes").GetInt32())
                End Using

                Assert.False(kit.Engine.TryGet(id).Enabled)
            End Using
        End Sub

        <Fact>
        Public Sub Change_carrying_only_the_interval_keeps_the_places_the_anchor_and_the_name()
            Using kit As New Kit()
                Dim id As String = CreateNightlyCopy(kit)

                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, Change(id, "{""intervalMinutes"":60}")))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                End Using

                Dim stored As ScheduledOperation = kit.Engine.TryGet(id)
                Assert.Equal(60, stored.IntervalMinutes)
                Assert.Equal(3 * 60 + 30, stored.AnchorMinutes)
                Assert.True(stored.Enabled)
                Assert.Equal("nightly", stored.Name)
                Assert.Equal("res-a", stored.Source.ResourceId)
                ' The stored folder is in the provider's vocabulary, joined with the resource root (item FS).
                Assert.Equal("/mov/in", stored.Source.Path)
                Assert.Equal("res-b", stored.Destination.ResourceId)
                Assert.Equal("/share/out", stored.Destination.Path)
            End Using
        End Sub

        <Fact>
        Public Sub Change_naming_an_unknown_member_is_refused_and_changes_nothing()
            Using kit As New Kit()
                Dim id As String = CreateNightlyCopy(kit)

                AssertRefused(Send(kit, Change(id, "{""enabled"":false,""futureField"":1}")), "unsupported", "definition.futureField")

                Assert.True(kit.Engine.TryGet(id).Enabled)
            End Using
        End Sub

        <Fact>
        Public Sub Change_of_an_unknown_operation_is_not_found()
            Using kit As New Kit()
                AssertRefused(Send(kit, Change("no-such-id", "{""enabled"":false}")), "not-found", "operationId")

                Assert.Empty(kit.Engine.Snapshot())
            End Using
        End Sub

        <Fact>
        Public Sub Delete_removes_a_saved_schedule()
            Using kit As New Kit()
                Dim id As String = CreateNightlyCopy(kit)
                Assert.NotNull(kit.Engine.TryGet(id))

                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, "{""v"":1,""verb"":""delete"",""operationId"":""" & id & """}"))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                End Using

                Assert.Null(kit.Engine.TryGet(id))
                AssertRefused(Send(kit, Change(id, "{""enabled"":true}")), "not-found", "operationId")
            End Using
        End Sub

        <Fact>
        Public Sub Change_of_a_delete_into_a_copy_needs_a_destination()
            Using kit As New Kit()
                Dim createDelete As String = "{""v"":1,""verb"":""save"",""definition"":{""action"":""delete""," &
                    """source"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""old""}," &
                    """at"":{""hour"":2,""minute"":0},""intervalMinutes"":1440}}"
                Dim id As String = ""
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, createDelete))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    id = doc.RootElement.GetProperty("operationId").GetString()
                End Using

                AssertRefused(Send(kit, Change(id, "{""action"":""copy""}")), "invalid-payload", "definition.destination")

                Assert.Equal(ScheduledAction.Delete, kit.Engine.TryGet(id).Action)
            End Using
        End Sub

        <Fact>
        Public Sub Create_without_a_source_is_refused()
            Using kit As New Kit()
                AssertRefused(Send(kit, "{""v"":1,""verb"":""save"",""definition"":{""action"":""copy"",""at"":{""hour"":3,""minute"":0},""intervalMinutes"":60}}"),
                              "invalid-payload", "definition.source")

                Assert.Empty(kit.Engine.Snapshot())
            End Using
        End Sub

        <Fact>
        Public Sub Create_stores_the_wire_path_inside_the_resource_root_and_answers_the_root_as_an_empty_path()
            Using kit As New Kit()
                Dim create As String = "{""v"":1,""verb"":""save"",""definition"":{""name"":""rooted"",""action"":""copy""," &
                    """source"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""sub/deep""}," &
                    """destination"":{""deviceId"":""dev-local-1"",""resourceId"":""res-b"",""path"":""""}," &
                    """at"":{""hour"":1,""minute"":0},""intervalMinutes"":1440}}"
                Dim id As String = ""
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, create))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    id = doc.RootElement.GetProperty("operationId").GetString()
                    Dim definition As JsonElement = doc.RootElement.GetProperty("operation").GetProperty("definition")
                    ' The answer is in the wire's vocabulary: relative to the root, empty for the root itself.
                    Assert.Equal("sub/deep", definition.GetProperty("source").GetProperty("path").GetString())
                    Assert.Equal("", definition.GetProperty("destination").GetProperty("path").GetString())
                End Using

                ' The engine stores the provider's own folder, joined with the root the way the editor would.
                Dim stored As ScheduledOperation = kit.Engine.TryGet(id)
                Assert.Equal("/mov/sub/deep", stored.Source.Path)
                Assert.Equal("/share", stored.Destination.Path)
            End Using
        End Sub

        <Fact>
        Public Sub Create_of_an_archive_naming_two_directory_resources_is_stored()
            Using kit As New Kit()
                Dim create As String = "{""v"":1,""verb"":""save"",""definition"":{""name"":""monthly zip"",""action"":""archive""," &
                    """source"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""in""}," &
                    """destination"":{""deviceId"":""dev-local-1"",""resourceId"":""res-b"",""path"":""zips""}," &
                    """deleteOriginalsAfterVerify"":false,""at"":{""hour"":4,""minute"":0},""intervalMinutes"":1440}}"
                Dim id As String = ""
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, create))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    id = doc.RootElement.GetProperty("operationId").GetString()
                    Dim definition As JsonElement = doc.RootElement.GetProperty("operation").GetProperty("definition")
                    Assert.Equal("archive", definition.GetProperty("action").GetString())
                    Assert.Equal("zips", definition.GetProperty("destination").GetProperty("path").GetString())
                End Using

                Dim stored As ScheduledOperation = kit.Engine.TryGet(id)
                Assert.Equal(ScheduledAction.Archive, stored.Action)
                Assert.Equal(ScheduledLocationKind.Resource, stored.Source.Kind)
                Assert.Equal("/mov/in", stored.Source.Path)
                Assert.Equal(ScheduledLocationKind.Resource, stored.Destination.Kind)
                Assert.Equal("/share/zips", stored.Destination.Path)
            End Using
        End Sub

        <Fact>
        Public Sub Create_of_a_backup_naming_two_smb_resources_is_stored()
            Using kit As New Kit()
                Dim create As String = "{""v"":1,""verb"":""save"",""definition"":{""action"":""backup""," &
                    """source"":{""deviceId"":""dev-local-1"",""resourceId"":""smb-a"",""path"":""in""}," &
                    """destination"":{""deviceId"":""dev-local-1"",""resourceId"":""smb-b"",""path"":""""}," &
                    """keepCount"":3,""at"":{""hour"":4,""minute"":0},""intervalMinutes"":1440}}"
                Dim id As String = ""
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, create))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    id = doc.RootElement.GetProperty("operationId").GetString()
                End Using

                Dim stored As ScheduledOperation = kit.Engine.TryGet(id)
                Assert.Equal(ScheduledAction.Backup, stored.Action)
                Assert.Equal(ScheduledLocationKind.Resource, stored.Source.Kind)
                Assert.Equal("\\nas\photos\in", stored.Source.Path)
                Assert.Equal(ScheduledLocationKind.Resource, stored.Destination.Kind)
                Assert.Equal(3, stored.BackupKeepCount)
            End Using
        End Sub

        <Fact>
        Public Sub Create_of_a_backup_naming_sftp_resources_is_stored()
            Using kit As New Kit()
                Dim create As String = "{""v"":1,""verb"":""save"",""definition"":{""action"":""backup""," &
                    """source"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""in""}," &
                    """destination"":{""deviceId"":""dev-local-1"",""resourceId"":""res-b"",""path"":""sets""}," &
                    """keepCount"":3,""at"":{""hour"":4,""minute"":0},""intervalMinutes"":1440}}"

                Dim id As String = ""
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, create))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    id = doc.RootElement.GetProperty("operationId").GetString()
                End Using

                ' L0343: an SFTP resource is a backup side too - the run stages it; the wire did not change.
                Dim stored As ScheduledOperation = kit.Engine.TryGet(id)
                Assert.Equal(ScheduledAction.Backup, stored.Action)
                Assert.Equal(ScheduledLocationKind.Resource, stored.Source.Kind)
                Assert.Equal(ScheduledLocationKind.Resource, stored.Destination.Kind)
                Assert.Equal(3, stored.BackupKeepCount)
            End Using
        End Sub

    End Class

    ''' <summary>
    ''' The wire path and the engine's stored folder are two vocabularies (item FS): the wire is relative to the
    ''' resource root, the engine stores the folder starting at the root. A stored folder the registry's root can
    ''' no longer confirm - the resource was re-registered under another root - has no wire spelling: the place is
    ''' answered only as a label, and a change of that schedule is refused as not-found.
    ''' </summary>
    <Collection(ScheduledCollection.Name)>
    Public Class ScheduledRemoteRootTests

        Private NotInheritable Class Kit
            Implements IDisposable

            Friend ReadOnly Tree As New ScratchTree()
            Friend ReadOnly Hive As New ScratchHive()
            Friend ReadOnly Clock As New ManualClock(New DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Unspecified))
            Friend ReadOnly Timers As New RecordingTimers()
            ''' <summary>The registry's current fact. Mutable: a test moves the root the way a re-registered resource does.</summary>
            Friend ReadOnly Facts As New ScheduledResourceFacts With {.Exists = True, .DisplayName = "NAS", .RootPath = "/mov"}
            Friend ReadOnly Engine As ScheduledEngine
            Friend ReadOnly Dispatcher As ScheduledRemoteDispatcher

            Friend Sub New()
                Dim facts As Func(Of String, ScheduledResourceFacts) =
                    Function(id As String) New ScheduledResourceFacts With {.Exists = Facts.Exists,
                        .DisplayName = Facts.DisplayName, .IsReadOnly = Facts.IsReadOnly, .IsPinLocked = Facts.IsPinLocked,
                        .IsUnsupported = Facts.IsUnsupported, .RootPath = Facts.RootPath, .IsSmb = Facts.IsSmb}
                Dim folder As String = Tree.Dir("state")
                Dim services As New ScheduledServices With {.Clock = AddressOf Clock.Read, .ResourceFacts = facts}
                Engine = New ScheduledEngine(New ScheduledStore(folder), New ScheduledJournal(folder), services, Timers) With {.CatchUpDelay = TimeSpan.Zero}
                Dispatcher = New ScheduledRemoteDispatcher(Engine, "dev-local-1") With {.ConsentOverride = ScheduledRemoteConsent.Always, .ResourceFacts = facts}
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Engine.Dispose()
                Hive.Dispose()
                Tree.Dispose()
            End Sub
        End Class

        Private Shared Function Send(kit As Kit, json As String) As String
            Return kit.Dispatcher.Execute("dev-remote-1", "req-" & Guid.NewGuid().ToString("N"), json)
        End Function

        <Fact>
        Public Sub A_moved_root_leaves_the_stored_folder_unreachable_from_the_wire()
            Using kit As New Kit()
                Dim id As String = ""
                Dim create As String = "{""v"":1,""verb"":""save"",""definition"":{""name"":""nightly"",""action"":""copy""," &
                    """source"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""in""}," &
                    """destination"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""out""}," &
                    """at"":{""hour"":3,""minute"":0},""intervalMinutes"":1440}}"
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, create))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    id = doc.RootElement.GetProperty("operationId").GetString()
                End Using
                Assert.Equal("/mov/in", kit.Engine.TryGet(id).Source.Path)

                ' The resource was re-registered under another root: the stored folder lies outside it now.
                kit.Facts.RootPath = "/other"

                ' A change of that schedule is refused: the stored folder names a place the registry cannot confirm.
                Using doc As JsonDocument = JsonDocument.Parse(
                        Send(kit, "{""v"":1,""verb"":""save"",""operationId"":""" & id & """,""definition"":{""enabled"":false}}"))
                    Assert.Equal("refused", doc.RootElement.GetProperty("outcome").GetString())
                    Assert.Equal("not-found", doc.RootElement.GetProperty("reason").GetString())
                    Assert.Equal("definition.source", doc.RootElement.GetProperty("member").GetString())
                End Using
                Assert.True(kit.Engine.TryGet(id).Enabled, "the refused change stored nothing")

                ' list answers the place only as a label: a client reads it and cannot write it.
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, "{""v"":1,""verb"":""list""}"))
                    Dim definition As JsonElement = doc.RootElement.GetProperty("operations")(0).GetProperty("definition")
                    Assert.Equal("local", definition.GetProperty("source").GetProperty("kind").GetString())
                    Assert.Contains("/mov/in", definition.GetProperty("source").GetProperty("label").GetString())
                End Using
            End Using
        End Sub
    End Class

    ''' <summary>
    ''' The success path of a remote run (item FR): the dispatcher answers started, the engine's lane runs the
    ''' operation through the injected side seam, and the finished run lands in the journal where the history
    ''' verb reads it - the two gaps of the 2026-11-16 audit's action item 3.
    ''' </summary>
    <Collection(ScheduledCollection.Name)>
    Public Class ScheduledRemoteRunTests

        Private NotInheritable Class Kit
            Implements IDisposable

            Friend ReadOnly Tree As New ScratchTree()
            Friend ReadOnly Hive As New ScratchHive()
            Friend ReadOnly Clock As New ManualClock(New DateTime(2026, 11, 17, 11, 0, 0, DateTimeKind.Unspecified))
            Friend ReadOnly Timers As New RecordingTimers()
            Friend ReadOnly Engine As ScheduledEngine
            Friend ReadOnly Dispatcher As ScheduledRemoteDispatcher

            Friend Sub New()
                Dim facts As Func(Of String, ScheduledResourceFacts) =
                    Function(id As String) New ScheduledResourceFacts With {.Exists = True, .DisplayName = "NAS",
                        .RootPath = If(String.Equals(id, "res-b", StringComparison.Ordinal), "/share", "/mov")}
                ' The side seam the audit named: the product opens a provider session for a resource location; the
                ' run test serves plain scratch folders behind the resource ids.
                Dim opener As Func(Of ScheduledLocation, ScheduledSideOpen) =
                    Function(location As ScheduledLocation) ScheduledSideOpen.Opened(
                        LocalSide.ForLocation(New ScheduledLocation With {
                            .Path = If(String.Equals(location.ResourceId, "res-b", StringComparison.Ordinal),
                                       Tree.Dir("out"), Tree.Dir("in"))}))
                Dim folder As String = Tree.Dir("state")
                Dim services As New ScheduledServices With {
                    .Clock = AddressOf Clock.Read,
                    .ResourceFacts = facts,
                    .OpenSide = opener}
                Engine = New ScheduledEngine(New ScheduledStore(folder), New ScheduledJournal(folder), services, Timers) With {.CatchUpDelay = TimeSpan.Zero}
                Dispatcher = New ScheduledRemoteDispatcher(Engine, "dev-local-1") With {.ConsentOverride = ScheduledRemoteConsent.Always, .ResourceFacts = facts}
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Engine.Dispose()
                Hive.Dispose()
                Tree.Dispose()
            End Sub
        End Class

        Private Shared Function Send(kit As Kit, json As String) As String
            Return kit.Dispatcher.Execute("dev-remote-1", "req-" & Guid.NewGuid().ToString("N"), json)
        End Function

        <Fact>
        Public Sub Run_starts_the_operation_and_history_reads_the_finished_run()
            Using kit As New Kit()
                kit.Tree.Write("in\a.txt", "A")
                kit.Tree.Write("in\deep\b.txt", "BB")

                Dim id As String = ""
                Dim create As String = "{""v"":1,""verb"":""save"",""definition"":{""name"":""nightly"",""action"":""copy""," &
                    """source"":{""deviceId"":""dev-local-1"",""resourceId"":""res-a"",""path"":""in""}," &
                    """destination"":{""deviceId"":""dev-local-1"",""resourceId"":""res-b"",""path"":""""}," &
                    """at"":{""hour"":3,""minute"":30},""intervalMinutes"":1440}}"
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, create))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    id = doc.RootElement.GetProperty("operationId").GetString()
                End Using

                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, "{""v"":1,""verb"":""run"",""operationId"":""" & id & """}"))
                    Assert.Equal("ok", doc.RootElement.GetProperty("outcome").GetString())
                    Assert.True(doc.RootElement.GetProperty("started").GetBoolean())
                End Using

                ' The engine's lane finished the run: the files moved and the record keeps the outcome.
                kit.Engine.WhenIdleAsync().GetAwaiter().GetResult()
                Assert.Equal(ScheduledRunOutcome.Ok, kit.Engine.TryGet(id).Record.LastOutcome)
                Assert.Equal("A", kit.Tree.Read("out\a.txt"))
                Assert.Equal("BB", kit.Tree.Read("out\deep\b.txt"))

                ' history reads the journal over the remote wire: one run, started by run-now, both files kept.
                Using doc As JsonDocument = JsonDocument.Parse(Send(kit, "{""v"":1,""verb"":""history"",""operationId"":""" & id & """}"))
                    Dim runs As JsonElement = doc.RootElement.GetProperty("runs")
                    Assert.Equal(1, runs.GetArrayLength())
                    Dim run As JsonElement = runs(0)
                    Assert.Equal("ok", run.GetProperty("outcome").GetString())
                    Assert.Equal("run-now", run.GetProperty("trigger").GetString())
                    Assert.Equal(2, run.GetProperty("files").GetInt32())
                    Assert.Equal(0, run.GetProperty("failed").GetInt32())
                    Assert.False(String.IsNullOrEmpty(run.GetProperty("startedAt").GetString()))
                End Using
            End Using
        End Sub
    End Class

End Namespace
#End If
