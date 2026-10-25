Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Text.Json.Nodes
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Specification 203 section 5.11 - what this PC writes into the Drive appDataFolder (its device record and one resource record per
''' shared folder, never an `access`), what a second PC reads back, and how the Drive listing joins the directory the viewer reads.
''' The pure halves (<see cref="DriveShareRecords"/>, <see cref="DriveDirectoryMerge"/>) are pinned without I/O; the publisher runs
''' against the in-memory Drive.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class DriveChannelPublisherTests

    Private Const ClientId As String = "publisher-test-client.apps.example"
    Private Const ClientSecret As String = "publisher-test-secret-value"
    Private Const Seed As String = "seed-refresh-token-for-the-publisher-tests"
    Private Const DeviceA As String = "DDDDDDDDDDDDDDDDDDDDDA"
    Private Const DeviceB As String = "EEEEEEEEEEEEEEEEEEEEEA"
    Private Const IdPhotos As String = "PPPPPPPPPPPPPPPPPPPPPA"
    Private Const IdVideos As String = "VVVVVVVVVVVVVVVVVVVVVA"
    Private Const IdNew As String = "NNNNNNNNNNNNNNNNNNNNNA"
    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const WorkerPassword As String = "Zk7PmQ2wXr9TzS4vGnHb3JdL"

    ' ------------------------------------------------------------------------------------------ fixtures

    Private NotInheritable Class AllRoles
        Implements IExchangeRoleFacts

        Public Function SharesPublished() As Boolean Implements IExchangeRoleFacts.SharesPublished
            Return True
        End Function

        Public Function ViewerAttaches() As Boolean Implements IExchangeRoleFacts.ViewerAttaches
            Return True
        End Function

        Public Function CanReceive() As Boolean Implements IExchangeRoleFacts.CanReceive
            Return False
        End Function

        Public Function LiveBroadcastEnabled() As Boolean Implements IExchangeRoleFacts.LiveBroadcastEnabled
            Return False
        End Function

        Public Function TakesPartInClipboard() As Boolean Implements IExchangeRoleFacts.TakesPartInClipboard
            Return False
        End Function
    End Class

    ''' <summary>One PC on the shared fake Drive: its token, its ids file, its service, and the publisher over them.</summary>
    Private NotInheritable Class Pc
        Implements IDisposable

        Public ReadOnly Folder As String
        Public ReadOnly ExchangeFolder As String
        Public ReadOnly Http As HttpClient
        Public ReadOnly Tokens As ExchangeDriveTokenStore
        Public ReadOnly Config As New ExchangeDriveClientConfig(ClientId, ClientSecret)
        Public ReadOnly Producer As ExchangeProducerStore
        Public ReadOnly Options As DriveChannelPublisherOptions
        Public ReadOnly Service As ExchangeDriveService
        Public ReadOnly Publisher As DriveChannelPublisher
        Public ReadOnly DeviceId As String
        Private ReadOnly _notes As New List(Of String)
        Private _statusReads As Integer

        ''' <summary>What the worker answers; a test replaces it between passes. Nothing = the status could not be read.</summary>
        Public Status As WorkerStatus
        Public ServerAccount As Boolean
        Public StatusFails As Boolean
        Public LockedPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Public NewIds As New Queue(Of String)

        Public Sub New(drive As FakeDriveHandler, deviceId As String, Optional intervalMs As Integer = 15000)
            Me.DeviceId = deviceId
            Folder = Directory.CreateTempSubdirectory("fmsdrvpub").FullName
            ExchangeFolder = Path.Combine(Folder, "exchange")
            Http = New HttpClient(drive, False)
            drive.AcceptRefreshToken(Seed)
            Tokens = New ExchangeDriveTokenStore(ExchangeFolder, Nothing)
            Assert.True(Tokens.Save(ClientId, Seed))
            Producer = New ExchangeProducerStore(Path.Combine(Folder, "ids"), New ExchangeDeviceAgentTests.XorCipher())
            Service = NewService(deviceId, "Office PC")
            Options = New DriveChannelPublisherOptions With {
                .GetStatus = AddressOf ReadStatus,
                .Store = Producer,
                .RoleFacts = New AllRoles(),
                .ProductVersion = "46",
                .Platform = Function() "windows",
                .HasServerAccount = Function() ServerAccount,
                .HasPin = Function(hostPath As String) LockedPaths.Contains(hostPath),
                .NewId = AddressOf NextId,
                .NowUtc = Function() New DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc),
                .Log = AddressOf Note,
                .IntervalMs = intervalMs,
                .StatusTimeoutMs = 2000,
                .StopWaitMs = 4000}
            Publisher = New DriveChannelPublisher(Service, deviceId, "Office PC", Options)
        End Sub

        Public Function NewService(id As String, name As String) As ExchangeDriveService
            Dim session As New ExchangeDriveSession(Http, Tokens, Config)
            Dim client As New ExchangeDriveClient(Http, session, Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
            Return New ExchangeDriveService(session, client, New ExchangeDriveDirectory(client, id),
                                            New ExchangeDriveQueue(client, id, name, New ExchangeDriveMemoryHandledStore()),
                                            Function(span As TimeSpan, token As CancellationToken) Task.Delay(Timeout.Infinite, token),
                                            Function() 0.5)
        End Function

        Private Function ReadStatus() As Task(Of WorkerStatus)
            Interlocked.Increment(_statusReads)
            If StatusFails Then Throw New InvalidOperationException("the worker pipe is closed: secret-detail-must-not-be-logged")
            Return Task.FromResult(Status)
        End Function

        Private Function NextId() As String
            SyncLock NewIds
                Return If(NewIds.Count > 0, NewIds.Dequeue(), ExchangeIds.NewId())
            End SyncLock
        End Function

        Private Sub Note(line As String)
            SyncLock _notes
                _notes.Add(line)
            End SyncLock
        End Sub

        Public Function Notes() As List(Of String)
            SyncLock _notes
                Return _notes.ToList()
            End SyncLock
        End Function

        Public ReadOnly Property StatusReads As Integer
            Get
                Return Volatile.Read(_statusReads)
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            Publisher.Dispose()
            Service.Dispose()
            Http.Dispose()
            Try
                Directory.Delete(Folder, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Sub
    End Class

    Private Shared Function StatusOf(ParamArray names As String()) As WorkerStatus
        Dim roots As New List(Of ShareFolder)
        For Each n As String In names
            roots.Add(New ShareFolder With {.name = n, .hostPath = "C:\__fms_test__\" & n})
        Next
        Return New WorkerStatus With {.Running = True, .ListenPort = 2222, .Username = "fms", .Password = WorkerPassword, .Fingerprint = Fp, .Roots = roots}
    End Function

    Private Shared Function StateOf(ParamArray pairs As String()) As ExchangeProducerState
        Dim s As ExchangeProducerState = ExchangeProducerState.Empty
        For i As Integer = 0 To pairs.Length - 2 Step 2
            s = s.WithEntry(pairs(i), New ExchangeProducerEntry(pairs(i + 1), ""))
        Next
        Return s
    End Function

    Private Shared Function PlanOf(status As WorkerStatus, state As ExchangeProducerState, mayCreate As Boolean,
                                 Optional locked As Func(Of String, Boolean) = Nothing, Optional ids As Queue(Of String) = Nothing) As DriveSharePlan
        Dim queue As Queue(Of String) = If(ids, New Queue(Of String)(New String() {IdNew}))
        Dim isLocked As Func(Of String, Boolean) = If(locked, New Func(Of String, Boolean)(Function(p As String) False))
        Return DriveShareRecords.Plan(status, state, DeviceA, mayCreate, Function() queue.Dequeue(), isLocked,
                                      New DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc))
    End Function

    Private Shared Function Obj(json As String) As JsonObject
        Return CType(JsonNode.Parse(json), JsonObject)
    End Function

    Private Shared Async Function Until(condition As Func(Of Task(Of Boolean))) As Task
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
        Do While Not Await condition()
            If DateTime.UtcNow > deadline Then Throw New TimeoutException("the condition never held")
            Await Task.Delay(20)
        Loop
    End Function

    ''' <summary>What a second PC on the same account reads: the other devices and their folders.</summary>
    Private Shared Async Function ReadAs(reader As Pc) As Task(Of ExchangeDirectorySnapshot)
        Dim result As ExchangeDriveDirectoryResult = Await reader.Service.Records.ReadAsync(CancellationToken.None)
        Return result.Snapshot
    End Function

    Private Shared Function Names(snapshot As ExchangeDirectorySnapshot) As List(Of String)
        Return snapshot.Resources.Select(Function(r) r.Name).OrderBy(Function(n) n, StringComparer.Ordinal).ToList()
    End Function

    ' ------------------------------------------------------------------------------------------ the device record

    <Fact>
    Public Sub The_device_record_names_the_pc_its_product_and_its_roles_and_no_presence()
        Dim json As String = DriveShareRecords.BuildDevice(DeviceA, "Office PC", "46", "windows", New String() {"resourceProducer", "resourceConsumer"})
        Dim o As JsonObject = Obj(json)
        Assert.Equal(DeviceA, CStr(o("deviceId")))
        Assert.Equal("Office PC", CStr(o("deviceName")))
        Assert.Equal(ExchangeWire.ProductWindows, CStr(o("product")))
        Assert.Equal("46", CStr(o("productVersion")))
        Assert.Equal("windows", CStr(o("platform")))
        Assert.Equal(2, CType(o("roles"), JsonArray).Count)
        ' presence and lastSeen are the reader's to derive from Drive's clock
        Assert.False(o.ContainsKey("presence"))
        Assert.False(o.ContainsKey("lastSeen"))
    End Sub

    <Fact>
    Public Sub A_device_record_with_a_bad_id_is_nothing_and_a_blank_name_is_a_generic_one()
        Assert.Equal("", DriveShareRecords.BuildDevice("not an id", "x", "46", "windows", Nothing))
        Assert.Equal("Windows PC", CStr(Obj(DriveShareRecords.BuildDevice(DeviceA, "  ", "46", "windows", Nothing))("deviceName")))
        Assert.Empty(CType(Obj(DriveShareRecords.BuildDevice(DeviceA, "x", "46", "windows", Nothing))("roles"), JsonArray))
    End Sub

    ' ------------------------------------------------------------------------------------------ the plan

    <Fact>
    Public Sub Nothing_is_listed_while_the_worker_is_down_unknown_or_the_device_id_is_bad()
        Assert.Empty(PlanOf(Nothing, ExchangeProducerState.Empty, True).Resources)
        Dim down As WorkerStatus = StatusOf("Photos")
        down.Running = False
        Assert.Empty(PlanOf(down, ExchangeProducerState.Empty, True).Resources)
        Assert.Empty(DriveShareRecords.Plan(StatusOf("Photos"), ExchangeProducerState.Empty, "bad id", True, Nothing, Nothing, DateTime.UtcNow).Resources)
    End Sub

    <Fact>
    Public Sub A_listed_folder_carries_its_identity_in_clear_and_never_an_access_a_share_id_or_a_port()
        Dim plan As DriveSharePlan = PlanOf(StatusOf("Photos"), ExchangeProducerState.Empty, True)
        Dim item As DrivePlannedResource = Assert.Single(plan.Resources)
        Assert.Equal("Photos", item.Name)
        Assert.Equal(IdNew, item.ResourceId)
        Dim o As JsonObject = Obj(item.Json)
        Assert.Equal(IdNew, CStr(o("resourceId")))
        Assert.Equal(DeviceA, CStr(o("deviceId")))
        Assert.Equal("Photos", CStr(o("name")))
        Assert.Equal(Fp, CStr(o("hostKeyFingerprint")))
        Assert.Equal("/Photos", CStr(o("root")))
        For Each secret As String In New String() {"access", "sealedAccess", "shareId", "publicPort", "credentials", "protection"}
            Assert.False(o.ContainsKey(secret), secret & " must not be in a Drive resource record")
        Next
        Assert.DoesNotContain(WorkerPassword, item.Json, StringComparison.Ordinal)
        Assert.True(plan.StateChanged)
        Assert.Equal(IdNew, plan.State.Get("Photos").ResourceId)
    End Sub

    <Fact>
    Public Sub Kept_ids_are_used_and_a_plan_that_only_reads_changes_nothing()
        Dim kept As ExchangeProducerState = StateOf("Photos", IdPhotos, "Videos", IdVideos)
        Dim plan As DriveSharePlan = PlanOf(StatusOf("Photos", "Videos"), kept, True)
        Assert.Equal(New String() {IdPhotos, IdVideos}, plan.Resources.Select(Function(r) r.ResourceId).ToArray())
        Assert.False(plan.StateChanged)
        Assert.Same(kept, plan.State)
        ' the same inputs, the same signature: a pass with no change writes nothing
        Assert.Equal(plan.Resources(0).Signature, PlanOf(StatusOf("Photos", "Videos"), kept, True).Resources(0).Signature)
    End Sub

    <Fact>
    Public Sub A_pc_with_an_exchange_server_account_never_creates_an_id_it_waits_for_the_server_publisher()
        Dim kept As ExchangeProducerState = StateOf("Photos", IdPhotos)
        Dim plan As DriveSharePlan = PlanOf(StatusOf("Photos", "Videos"), kept, False)
        Assert.Equal("Photos", Assert.Single(plan.Resources).Name)
        Assert.Equal(1, plan.WaitingForIds)
        Assert.False(plan.StateChanged)
        Assert.Null(plan.State.Get("Videos"))
    End Sub

    <Fact>
    Public Sub A_folder_name_seen_twice_is_listed_once()
        Dim plan As DriveSharePlan = PlanOf(StatusOf("Photos", "Photos"), ExchangeProducerState.Empty, True)
        Assert.Single(plan.Resources)
    End Sub

    <Fact>
    Public Sub A_pin_locked_folder_is_listed_only_when_the_lock_can_be_declared()
        Dim status As WorkerStatus = StatusOf("Photos", "Videos")
        Dim locked As Func(Of String, Boolean) = Function(path As String) path.EndsWith("Photos", StringComparison.Ordinal)
        Dim kept As ExchangeProducerState = StateOf("Photos", IdPhotos, "Videos", IdVideos)

        ' the worker cannot carry the lock: the folder is left out, the other one stands
        Assert.Equal("Videos", Assert.Single(PlanOf(status, kept, True, locked).Resources).Name)

        ' it can: the record declares the scheme and nothing else about the PIN
        status.ResourcePinSupported = True
        status.Roots(0).pinProtected = True
        Dim both As DriveSharePlan = PlanOf(status, kept, True, locked)
        Assert.Equal(2, both.Resources.Count)
        Dim photos As JsonObject = Obj(both.Resources.Single(Function(r) r.Name = "Photos").Json)
        Assert.Equal("pin-v1", CStr(photos("protection")("scheme")))
        Assert.DoesNotContain("pin", both.Resources.Single(Function(r) r.Name = "Videos").Json, StringComparison.OrdinalIgnoreCase)
    End Sub

    ' ------------------------------------------------------------------------------------------ the merge

    Private Shared ReadOnly NoDevices As ExchangeDeviceRecord() = Array.Empty(Of ExchangeDeviceRecord)()
    Private Shared ReadOnly NoResources As ExchangeResourceRecord() = Array.Empty(Of ExchangeResourceRecord)()

    Private Shared Function DeviceRec(id As String, name As String, presence As String) As ExchangeDeviceRecord
        Dim record As ExchangeDeviceRecord = Nothing
        Dim raw As String = "{""deviceId"":""" & id & """,""deviceName"":""" & name & """,""product"":""FastMediaSorter"",""productVersion"":""46"",""platform"":""windows"",""roles"":[""resourceProducer""],""presence"":""" & presence & """}"
        Assert.True(ExchangeRecordReader.TryReadDevice(raw, record))
        Return record
    End Function

    Private Shared Function ResourceRec(id As String, deviceId As String, name As String, presence As String, Optional withAccess As Boolean = False) As ExchangeResourceRecord
        Dim record As ExchangeResourceRecord = Nothing
        Dim raw As String = "{""resourceId"":""" & id & """,""deviceId"":""" & deviceId & """,""kind"":""" & ExchangeWire.KindSftpShare & """,""name"":""" & name & """,""presence"":""" & presence & """" &
                            If(withAccess, ",""access"":{""host"":""203.0.113.9"",""port"":2222}", "") & "}"
        Assert.True(ExchangeRecordReader.TryReadResource(raw, record))
        Return record
    End Function

    Private Shared Function Snap(revision As Long, devices As IEnumerable(Of ExchangeDeviceRecord), resources As IEnumerable(Of ExchangeResourceRecord),
                                 Optional broadcasts As IEnumerable(Of ExchangeBroadcastRecord) = Nothing) As ExchangeDirectorySnapshot
        Return New ExchangeDirectorySnapshot(revision, devices.ToList(), resources.ToList(), If(broadcasts, New List(Of ExchangeBroadcastRecord)).ToList())
    End Function

    <Fact>
    Public Sub No_Drive_listing_leaves_the_server_directory_as_it_is()
        Dim server As ExchangeDirectorySnapshot = Snap(7, {DeviceRec(DeviceA, "A", "online")}, {ResourceRec(IdPhotos, DeviceA, "Photos", "online", True)})
        Assert.Same(server, DriveDirectoryMerge.Union(server, Nothing))
        Assert.Same(server, DriveDirectoryMerge.Union(server, ExchangeDirectorySnapshot.Empty))
        Dim only As ExchangeDirectorySnapshot = Snap(3, {DeviceRec(DeviceB, "B", "online")}, NoResources)
        Assert.Equal(3L, DriveDirectoryMerge.Union(Nothing, only).Revision)
    End Sub

    <Fact>
    Public Sub A_Drive_only_row_is_added_without_an_access_and_the_revisions_add_up()
        Dim server As ExchangeDirectorySnapshot = Snap(7, {DeviceRec(DeviceA, "A", "online")}, {ResourceRec(IdPhotos, DeviceA, "Photos", "online", True)})
        Dim drive As ExchangeDirectorySnapshot = Snap(4, {DeviceRec(DeviceB, "B", "online")}, {ResourceRec(IdVideos, DeviceB, "Videos", "online")})
        Dim merged As ExchangeDirectorySnapshot = DriveDirectoryMerge.Union(server, drive)
        Assert.Equal(11L, merged.Revision)
        Assert.Equal(New String() {DeviceA, DeviceB}, merged.Devices.Select(Function(d) d.DeviceId).ToArray())
        Assert.Equal(New String() {"Photos", "Videos"}, merged.Resources.Select(Function(r) r.Name).ToArray())
        Assert.True(merged.Resources.Single(Function(r) r.Name = "Photos").HasAccess)
        Assert.False(merged.Resources.Single(Function(r) r.Name = "Videos").HasAccess)
    End Sub

    <Fact>
    Public Sub The_server_row_wins_a_shared_id_unless_it_is_not_online_and_Drive_says_it_is()
        Dim serverOnline As ExchangeDirectorySnapshot = Snap(1, {DeviceRec(DeviceA, "ServerName", "online")}, {ResourceRec(IdPhotos, DeviceA, "ServerPhotos", "online", True)})
        Dim serverOffline As ExchangeDirectorySnapshot = Snap(1, {DeviceRec(DeviceA, "ServerName", "offline")}, {ResourceRec(IdPhotos, DeviceA, "ServerPhotos", "offline", True)})
        Dim driveOnline As ExchangeDirectorySnapshot = Snap(1, {DeviceRec(DeviceA, "DriveName", "online")}, {ResourceRec(IdPhotos, DeviceA, "DrivePhotos", "online")})
        Dim driveOffline As ExchangeDirectorySnapshot = Snap(1, {DeviceRec(DeviceA, "DriveName", "offline")}, {ResourceRec(IdPhotos, DeviceA, "DrivePhotos", "offline")})

        Dim a As ExchangeDirectorySnapshot = DriveDirectoryMerge.Union(serverOnline, driveOnline)
        Assert.Equal("ServerName", Assert.Single(a.Devices).DeviceName)
        Assert.Equal("ServerPhotos", Assert.Single(a.Resources).Name)

        Dim b As ExchangeDirectorySnapshot = DriveDirectoryMerge.Union(serverOffline, driveOnline)
        Assert.Equal("DriveName", Assert.Single(b.Devices).DeviceName)
        Assert.Equal("DrivePhotos", Assert.Single(b.Resources).Name)

        Dim c As ExchangeDirectorySnapshot = DriveDirectoryMerge.Union(serverOffline, driveOffline)
        Assert.Equal("ServerName", Assert.Single(c.Devices).DeviceName)
        Dim d As ExchangeDirectorySnapshot = DriveDirectoryMerge.Union(serverOnline, driveOffline)
        Assert.Equal("ServerPhotos", Assert.Single(d.Resources).Name)
    End Sub

    <Fact>
    Public Sub Drive_broadcasts_and_grants_are_not_merged_only_the_servers_are_kept()
        Dim broadcast As ExchangeBroadcastRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadBroadcast("{""broadcastId"":""" & IdNew & """,""deviceId"":""" & DeviceB & """,""mode"":""live"",""title"":""t""}", broadcast))
        Dim server As ExchangeDirectorySnapshot = Snap(1, NoDevices, NoResources)
        Dim drive As ExchangeDirectorySnapshot = Snap(1, {DeviceRec(DeviceB, "B", "online")}, NoResources, {broadcast})
        Dim merged As ExchangeDirectorySnapshot = DriveDirectoryMerge.Union(server, drive)
        Assert.Empty(merged.Broadcasts)
        Assert.Single(merged.Devices)
    End Sub

    ' ------------------------------------------------------------------------------------------ the publisher on the fake Drive

    <Fact>
    Public Async Function A_pass_lists_the_pc_and_its_folders_and_a_second_pc_reads_them_back() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA), b As New Pc(drive, DeviceB)
            a.Status = StatusOf("Photos", "Videos")
            Await a.Publisher.PassAsync(CancellationToken.None)

            Dim seen As ExchangeDirectorySnapshot = Await ReadAs(b)
            Dim device As ExchangeDeviceRecord = Assert.Single(seen.Devices)
            Assert.Equal(DeviceA, device.DeviceId)
            Assert.Contains(ExchangeWire.RoleResourceProducer, device.Roles)
            Assert.Equal(New String() {"Photos", "Videos"}, Names(seen).ToArray())
            For Each r As ExchangeResourceRecord In seen.Resources
                Assert.False(r.HasAccess, "a Drive resource record carries no access")
                Assert.Equal(Fp, r.HostKeyFingerprint)
                Assert.Equal(DeviceA, r.DeviceId)
            Next
            Assert.Equal(2, a.Publisher.ListedFolders)

            ' the ids were stored before they were written: the next start lists the same resources
            Dim kept As ExchangeProducerState = a.Producer.Load(DeviceA)
            Assert.Equal(2, kept.Entries.Count)
            Assert.Equal(seen.Resources.Single(Function(r) r.Name = "Photos").ResourceId, kept.Get("Photos").ResourceId)
        End Using
    End Function

    <Fact>
    Public Async Function A_pass_with_nothing_changed_writes_nothing() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA)
            a.Status = StatusOf("Photos")
            Await a.Publisher.PassAsync(CancellationToken.None)
            Dim before As Integer = drive.RequestCount
            Await a.Publisher.PassAsync(CancellationToken.None)
            Await a.Publisher.PassAsync(CancellationToken.None)
            Assert.Equal(before, drive.RequestCount)
        End Using
    End Function

    <Fact>
    Public Async Function A_folder_that_goes_away_is_withdrawn_and_a_stopped_worker_leaves_only_the_device() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA), b As New Pc(drive, DeviceB)
            a.Status = StatusOf("Photos", "Videos")
            Await a.Publisher.PassAsync(CancellationToken.None)
            Assert.Equal(2, (Await ReadAs(b)).Resources.Count)

            a.Status = StatusOf("Photos")
            Await a.Publisher.PassAsync(CancellationToken.None)
            Assert.Equal(New String() {"Photos"}, Names(Await ReadAs(b)).ToArray())
            Assert.Equal(1, a.Publisher.ListedFolders)

            a.Status.Running = False
            Await a.Publisher.PassAsync(CancellationToken.None)
            Dim seen As ExchangeDirectorySnapshot = Await ReadAs(b)
            Assert.Empty(seen.Resources)
            Assert.Single(seen.Devices)
            Assert.Equal(0, a.Publisher.ListedFolders)

            ' the folder comes back under the id it had
            a.Status = StatusOf("Photos")
            Await a.Publisher.PassAsync(CancellationToken.None)
            Assert.Equal(a.Producer.Load(DeviceA).Get("Photos").ResourceId, Assert.Single((Await ReadAs(b)).Resources).ResourceId)
        End Using
    End Function

    <Fact>
    Public Async Function A_pc_with_a_server_account_lists_the_device_and_only_the_folders_whose_ids_exist() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA), b As New Pc(drive, DeviceB)
            a.ServerAccount = True
            a.Status = StatusOf("Photos")
            Await a.Publisher.PassAsync(CancellationToken.None)
            Dim first As ExchangeDirectorySnapshot = Await ReadAs(b)
            Assert.Single(first.Devices)
            Assert.Empty(first.Resources)
            Assert.False(a.Producer.Exists(), "this class must not write the ids file next to the server publisher")

            ' the server publisher has since kept an id for it
            Assert.True(a.Producer.Save(DeviceA, StateOf("Photos", IdPhotos)))
            Await a.Publisher.PassAsync(CancellationToken.None)
            Assert.Equal(IdPhotos, Assert.Single((Await ReadAs(b)).Resources).ResourceId)
        End Using
    End Function

    <Fact>
    Public Async Function An_unreadable_worker_status_lists_the_device_logs_a_kind_once_and_never_the_detail() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA), b As New Pc(drive, DeviceB)
            a.StatusFails = True
            Await a.Publisher.PassAsync(CancellationToken.None)
            Await a.Publisher.PassAsync(CancellationToken.None)
            Dim seen As ExchangeDirectorySnapshot = Await ReadAs(b)
            Assert.Single(seen.Devices)
            Assert.Empty(seen.Resources)
            Dim notes As List(Of String) = a.Notes()
            Assert.Single(notes)
            Assert.DoesNotContain("secret-detail", notes(0), StringComparison.Ordinal)
        End Using
    End Function

    <Fact>
    Public Async Function A_signed_out_service_writes_nothing() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA)
            drive.RefreshRevoked = True
            a.Status = StatusOf("Photos")
            a.Service.Start()
            Await Until(Function() Task.FromResult(a.Service.State = ExchangeDriveServiceState.SignedOut))
            Dim before As Integer = drive.RequestCount
            Await a.Publisher.PassAsync(CancellationToken.None)
            Assert.Equal(before, drive.RequestCount)
            Assert.Equal(0, a.StatusReads)
        End Using
    End Function

    <Fact>
    Public Async Function The_loop_follows_the_folders_and_a_stopped_publisher_writes_nothing_more() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA, 100), b As New Pc(drive, DeviceB)
            a.Status = StatusOf("Photos")
            a.Publisher.Start()
            Await Until(Async Function() (Await ReadAs(b)).Resources.Count = 1)

            a.Status = StatusOf("Photos", "Videos")
            a.Publisher.Nudge()
            Await Until(Async Function() (Await ReadAs(b)).Resources.Count = 2)

            Await a.Publisher.StopAsync()
            a.Status = StatusOf("Photos", "Videos", "Music")
            a.Publisher.Nudge()
            Await Task.Delay(400)
            Dim count As Integer = drive.RequestCount
            Await Task.Delay(300)
            Assert.Equal(count, drive.RequestCount)
            Assert.Equal(2, (Await ReadAs(b)).Resources.Count)
        End Using
    End Function

    <Fact>
    Public Async Function Signing_out_stops_the_publisher_first_so_no_record_is_written_behind_the_deletion() As Task
        Dim drive As New FakeDriveHandler()
        Using a As New Pc(drive, DeviceA, 100), b As New Pc(drive, DeviceB)
            a.Status = StatusOf("Photos")
            Using host As New DriveChannelHost(a.Http, a.Tokens, a.Config, New DriveDeviceId(a.ExchangeFolder, Function() "", Function() DeviceA), "Office PC",
                                               Function(url As String) False, Function() "<html>ok</html>",
                                               AddressOf a.NewService,
                                               Function(service As ExchangeDriveService, id As String) New DriveChannelPublisher(service, id, "Office PC", a.Options))
                host.StartIfSignedIn()
                Await Until(Async Function() (Await ReadAs(b)).Resources.Count = 1)

                Dim result As ExchangeDriveSignOutResult = Await host.SignOutAsync()
                Assert.True(result.RecordsDeleted)
                Dim seen As ExchangeDirectorySnapshot = Await ReadAs(b)
                Assert.Empty(seen.Devices)
                Assert.Empty(seen.Resources)

                ' nudged and waited out: nothing comes back
                a.Publisher.Nudge()
                Await Task.Delay(500)
                Dim after As ExchangeDirectorySnapshot = Await ReadAs(b)
                Assert.Empty(after.Devices)
                Assert.Empty(after.Resources)
            End Using
        End Using
    End Function

End Class
