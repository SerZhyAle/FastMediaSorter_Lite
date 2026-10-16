Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 170 T-2, T-3, T-4 against ExchangeStubAccount: the producer inside the agent publishes what the worker serves as `sftp-share`
''' resources, keeps the ids across reconnects, follows the status (debounced), unpublishes when a share goes or sharing stops, asks for the
''' third-party port on one resource, makes and rotates and removes outsider links, and splices a tunnel to the worker's loopback listener
''' (bytes both ways, a cap per resource, an unknown share or a worker that is down answered by closing). Every socket is on 127.0.0.1.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangePublisherTests

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const WorkerPassword As String = "Zk7PmQ2wXr9TzS4vGnHb3JdL"

    ''' <summary>A stand-in for the share worker's SFTP listener: accepts on 127.0.0.1 and echoes every byte back.</summary>
    Friend NotInheritable Class FakeWorker
        Implements IDisposable

        Private ReadOnly _listener As TcpListener
        Private ReadOnly _cts As New CancellationTokenSource()
        Private ReadOnly _loop As Task
        Public Accepted As Integer

        Public Sub New()
            _listener = New TcpListener(IPAddress.Loopback, 0)
            _listener.Start()
            _loop = Task.Run(AddressOf RunAsync)
        End Sub

        Public ReadOnly Property Port As Integer
            Get
                Return DirectCast(_listener.LocalEndpoint, IPEndPoint).Port
            End Get
        End Property

        Private Async Function RunAsync() As Task
            While Not _cts.IsCancellationRequested
                Dim client As TcpClient
                Try
                    client = Await _listener.AcceptTcpClientAsync(_cts.Token)
                Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is SocketException
                    Return
                End Try
                Interlocked.Increment(Accepted)
                Dim t As Task = Task.Run(Function() EchoAsync(client))
            End While
        End Function

        Private Async Function EchoAsync(client As TcpClient) As Task
            Using client
                Dim s As NetworkStream = client.GetStream()
                Dim buf(4095) As Byte
                Try
                    While True
                        Dim n As Integer = Await s.ReadAsync(buf, 0, buf.Length, _cts.Token)
                        If n = 0 Then Return
                        Await s.WriteAsync(buf, 0, n, _cts.Token)
                    End While
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException
                End Try
            End Using
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Try : _cts.Cancel() : Catch : End Try
            Try : _listener.Stop() : Catch : End Try
            Try : _loop.Wait(1000) : Catch : End Try
            Try : _cts.Dispose() : Catch : End Try
        End Sub
    End Class

    ''' <summary>One test's world: the agent rig, a worker status the test edits, a settings port, an ids store beside the token file.</summary>
    Friend NotInheritable Class Kit
        Implements IDisposable

        Public ReadOnly Rig As New ExchangeDeviceAgentTests.Rig()
        Public ReadOnly SettingsPort As New ExchangeDeviceAgentTests.MemoryPort()
        Public ReadOnly Settings As ExchangePublishSettings
        Public ReadOnly Store As ExchangeProducerStore
        Public ReadOnly Worker As New FakeWorker()
        ''' <summary>The device keys of the agent, in memory: a test opens the boxes the producer made.</summary>
        Public ReadOnly Keys As New ExchangeMemoryDeviceKeyStore()
        Public ReadOnly Publisher As ExchangePublisher
        Public Status As WorkerStatus
        Public StatusCalls As Integer
        Public Invalidations As Integer
        Public PinLocked As Boolean
        Public WorkerDialFails As Boolean

        Public Sub New(Optional maxTunnels As Integer = 16, Optional idle As TimeSpan = Nothing)
            Settings = New ExchangePublishSettings(SettingsPort)
            Store = New ExchangeProducerStore(Rig.Folder, New ExchangeDeviceAgentTests.XorCipher())
            Status = StatusOf("Photos", "Videos")
            Dim options As New ExchangePublisherOptions With {
                .Settings = Settings, .Store = Store,
                .InvalidatePinSessions = Function()
                                             Interlocked.Increment(Invalidations)
                                             Return Task.CompletedTask
                                         End Function,
                .GetStatus = Function()
                                 Interlocked.Increment(StatusCalls)
                                 Return Task.FromResult(Status)
                             End Function,
                .LanOnly = Function() False, .LanFallback = Function() "10.0.0.2",
                .HasPin = Function(hostPath) PinLocked,
                .Log = Sub(line) Rig.Log.Enqueue(line),
                .DebounceMs = 40, .PollMs = 60, .StatusTimeoutMs = 2000, .PublishTimeoutMs = 4000,
                .MaxTunnelsPerResource = maxTunnels, .WorkerDialTimeoutMs = 1500,
                .DialWorker = Function(port, ct)
                                  If WorkerDialFails Then Throw New SocketException(10061)
                                  Return ExchangePublisher.DialWorkerLoopbackAsync(Worker.Port, ct)
                              End Function}
            If idle > TimeSpan.Zero Then options.TunnelIdleTimeout = idle
            Publisher = New ExchangePublisher(options)
            Rig.Ports.Producer = Publisher
            Rig.Ports.DeviceKeys = Keys
        End Sub

        Public Shared Function StatusOf(ParamArray names As String()) As WorkerStatus
            Dim roots As New List(Of ShareFolder)
            For Each n As String In names
                roots.Add(New ShareFolder With {.name = n, .hostPath = "C:\__fms_test__\" & n})
            Next
            Return New WorkerStatus With {
                .Running = True, .ListenPort = 2222, .Username = "fms", .Password = WorkerPassword, .Fingerprint = Fp, .Roots = roots,
                .Reachability = New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.9", .ExternalPort = 2222}}
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Publisher.Dispose()
            Worker.Dispose()
            Rig.Dispose()
        End Sub
    End Class

    Private Shared Function IdOf(raw As String, member As String) As String
        Using doc As JsonDocument = JsonDocument.Parse(raw)
            Dim el As JsonElement = Nothing
            Return If(doc.RootElement.TryGetProperty(member, el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
        End Using
    End Function

    Private Shared Function NameOf_(raw As String) As String
        Return IdOf(raw, "name")
    End Function

    Friend Shared Function Latest(acc As ExchangeStubAccount, name As String) As String
        Return acc.PublishLog().LastOrDefault(Function(r) NameOf_(r) = name)
    End Function

    ''' <summary>The `access` JSON inside the `sealedAccess` box of a published record, opened as the producer itself (it is a recipient of
    ''' its own entry), under the context the READER builds from the record's resourceId.</summary>
    Friend Shared Function OpenOwnBox(kit As Kit, deviceId As String, recordJson As String) As String
        Using doc As JsonDocument = JsonDocument.Parse(recordJson)
            Dim resourceId As String = doc.RootElement.GetProperty("resourceId").GetString()
            Dim box As ExchangeSealedBox = Nothing
            Assert.True(ExchangeSealedBox.TryParse(doc.RootElement.GetProperty("sealedAccess").GetRawText(), box))
            Using key As ExchangeDeviceKey = kit.Keys.TryLoad(deviceId)
                Dim ownKem As String = key.Kem
                Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, deviceId, key, ExchangeSealedContext.Access(resourceId), Function(fromDevice) ownKem)
                Assert.True(opened.Succeeded)
                Try
                    Return Encoding.UTF8.GetString(opened.Payload)
                Finally
                    opened.Wipe()
                End Try
            End Using
        End Using
    End Function

    ''' <summary>Spec 234 item DP: a folder goes through the server sealed or not at all, so a publishing test needs a server that lists
    ''' `sealed` and a directory that lists this device with its key. <paramref name="sealedServer"/> False is the older server.</summary>
    Friend Shared Async Function EnrollOnlineAsync(kit As Kit, acc As ExchangeStubAccount, Optional sealedServer As Boolean = True) As Task(Of ExchangeDeviceAgent)
        If sealedServer Then
            acc.Features = {"directory", "tunnel", "pairing", ExchangeSealedNames.FeatureSealed}
            acc.ListEnrolledDevices = True
        End If
        Dim agent As ExchangeDeviceAgent = kit.Rig.NewAgent()
        Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(kit.Rig.Request(acc), CancellationToken.None)
        Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
        Return agent
    End Function

    ' ------------------------------------------------------------------------------------------------ rule 1 and dormancy

    <Fact>
    Public Async Function With_publication_off_nothing_is_published_and_the_worker_is_never_asked() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Await Task.Delay(400)    ' several polls
                    Assert.Empty(acc.PublishLog())
                    Assert.Equal(0, acc.CountOf("publish"))
                    Assert.Equal(0, kit.StatusCalls)
                    Assert.Equal(ExchangePublishBlock.Off, kit.Publisher.Snapshot.Block)
                    Assert.Equal(0, kit.SettingsPort.Writes)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Sub Constructing_a_publisher_reads_and_writes_nothing()
        Dim port As New ExchangeDeviceAgentTests.MemoryPort()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-publisher-ctor-" & Guid.NewGuid().ToString("N"))
        Dim calls As Integer
        Dim p As New ExchangePublisher(New ExchangePublisherOptions With {
            .Settings = New ExchangePublishSettings(port), .Store = New ExchangeProducerStore(folder, New ExchangeDeviceAgentTests.XorCipher()),
            .GetStatus = Function()
                             Interlocked.Increment(calls)
                             Return Task.FromResult(Of WorkerStatus)(Nothing)
                         End Function})
        Assert.Equal(0, calls)
        Assert.Equal(0, port.Writes)
        Assert.False(Directory.Exists(folder))
        Assert.Equal(ExchangePublishBlock.Off, p.Snapshot.Block)
        p.Dispose()
    End Sub

    ' ------------------------------------------------------------------------------------------------ publishing

    <Fact>
    Public Async Function Publication_on_publishes_every_share_after_welcome_with_a_record_that_follows_the_catalog() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim photos As String = Latest(acc, "Photos")
                    Assert.Equal(ExchangeWire.KindSftpShare, IdOf(photos, "kind"))
                    Assert.Equal(agent.GetStatus().DeviceId, IdOf(photos, "deviceId"))
                    Assert.True(ExchangeIds.IsValidId(IdOf(photos, "resourceId")))
                    Assert.False(photos.Contains("presence", StringComparison.Ordinal))     ' the server's to stamp
                    Assert.False(photos.Contains("shareId", StringComparison.Ordinal))      ' nothing is made by publishing alone
                    Assert.False(photos.Contains("publicPort", StringComparison.Ordinal))
                    ' spec 234 item DI: the record carries the box and no `access`; the box opens on this device (it is a recipient of its own entry)
                    Dim info As ExchangeAccessInfo = Nothing
                    Using doc As JsonDocument = JsonDocument.Parse(photos)
                        Dim member As JsonElement
                        Assert.False(doc.RootElement.TryGetProperty("access", member))
                        Assert.True(doc.RootElement.TryGetProperty("sealedAccess", member))
                    End Using
                    Assert.True(ExchangeResourceAccess.TryRead(OpenOwnBox(kit, agent.GetStatus().DeviceId, photos), "Photos", info))
                    Assert.Equal("/Photos", info.Root)
                    Assert.Equal({"192.168.1.50", "203.0.113.9"}, info.Code.Hosts.ToArray())
                    Assert.Equal(WorkerPassword, info.Code.Password)
                    Assert.Equal(Fp, info.Code.Fingerprint)
                    Assert.Equal(ExchangePublishBlock.None, kit.Publisher.Snapshot.Block)
                    Assert.All(kit.Publisher.Snapshot.Rows, Sub(r) Assert.Equal(ExchangePublishState.Published, r.State))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Protected_publication_has_a_lock_and_unpublishing_invalidates_worker_proofs() As Task
        Using account As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Status.ResourcePinSupported = True
                kit.Status.Roots(0).pinProtected = True
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, account)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() account.PublishedNow().Count = 2))
                    Using record As JsonDocument = JsonDocument.Parse(Latest(account, "Photos"))
                        Assert.Equal("pin-v1", record.RootElement.GetProperty("protection").GetProperty("scheme").GetString())
                        Dim pinField As JsonElement
                        Assert.False(record.RootElement.TryGetProperty("accessPin", pinField))
                    End Using
                    Assert.True(kit.Publisher.Snapshot.Rows.Single(Function(r) r.Name = "Photos").PinProtected)
                    Dim code As String = Nothing
                    Assert.False(kit.Publisher.TryGetLinkCode("Photos", code))
                    kit.Settings.SetPublishShares(False)
                    kit.Publisher.Notify()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() account.PublishedNow().Count = 0))
                    Assert.Equal(1, kit.Invalidations)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_reconnect_publishes_everything_again_under_the_same_ids() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim before As String = IdOf(Latest(acc, "Photos"), "resourceId")
                    Dim beforeVideos As String = IdOf(Latest(acc, "Videos"), "resourceId")
                    acc.DropAll()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishLog().Count >= 4, 15))
                    Assert.Equal(before, IdOf(Latest(acc, "Photos"), "resourceId"))
                    Assert.Equal(beforeVideos, IdOf(Latest(acc, "Videos"), "resourceId"))
                    Assert.Equal(2, kit.Store.Load(agent.GetStatus().DeviceId).Entries.Count)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_change_of_the_share_list_is_published_once_and_a_removed_share_is_unpublished() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim videosId As String = IdOf(Latest(acc, "Videos"), "resourceId")
                    Dim publishes As Integer = acc.PublishLog().Count

                    kit.Status = Kit.StatusOf("Photos", "Videos", "Music")
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 3))
                    Await Task.Delay(300)
                    Assert.Equal(publishes + 1, acc.PublishLog().Count)      ' only the new share: the others did not change

                    kit.Status = Kit.StatusOf("Photos", "Music")
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.Unpublished().Contains(videosId)))
                    Assert.Equal(2, acc.PublishedNow().Count)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Switching_publication_off_unpublishes_everything_and_on_again_restores_the_same_ids() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim photosId As String = IdOf(Latest(acc, "Photos"), "resourceId")

                    kit.Settings.SetPublishShares(False)
                    kit.Publisher.Notify()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 0))
                    Assert.Equal(ExchangePublishBlock.Off, kit.Publisher.Snapshot.Block)

                    kit.Settings.SetPublishShares(True)
                    kit.Publisher.Notify()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal(photosId, IdOf(Latest(acc, "Photos"), "resourceId"))      ' 9 rule 3: an attachment elsewhere resumes
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Stopping_the_share_unpublishes_it_and_starting_it_again_restores_the_same_id() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim id As String = IdOf(Latest(acc, "Photos"), "resourceId")

                    kit.Status.Running = False
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 0))
                    Assert.Equal(ExchangePublishBlock.ServerDown, kit.Publisher.Snapshot.Block)

                    kit.Status.Running = True
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal(id, IdOf(Latest(acc, "Photos"), "resourceId"))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_PIN_locked_root_publishes_nothing_and_says_why() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                kit.PinLocked = True
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Block = ExchangePublishBlock.PinLocked))
                    Await Task.Delay(200)
                    Assert.Empty(acc.PublishLog())
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_publish_is_shown_per_share_and_never_retried_in_a_loop() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                acc.RefuseNextPublish("capacity")
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Rows.Count = 2 AndAlso
                                                                            kit.Publisher.Snapshot.Rows.Any(Function(r) r.State = ExchangePublishState.Refused)))
                    Dim refused As ExchangePublishRow = kit.Publisher.Snapshot.Rows.First(Function(r) r.State = ExchangePublishState.Refused)
                    Assert.Equal(ExchangeRefusal.Capacity, refused.Refusal)
                    Await Task.Delay(300)
                    Assert.Equal(1, acc.PublishedNow().Count)         ' the other one went through; the refused one was not hammered
                    Assert.Equal(2, acc.CountOf("publish"))
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ the third-party port

    <Fact>
    Public Async Function The_third_party_port_is_asked_on_one_resource_remembered_and_asked_for_again_after_a_reconnect() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                kit.Settings.SetThirdPartyPort(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal("203.0.113.5:40007", kit.Publisher.Snapshot.ThirdPartyEndpoint)
                    Dim asked As List(Of String) = acc.PublishLog().Where(Function(r) r.Contains("publicPort", StringComparison.Ordinal)).ToList()
                    Assert.Single(asked)
                    Assert.Equal("Photos", NameOf_(asked(0)))
                    Assert.Equal(0, ReadInt(asked(0), "publicPort"))                 ' "any" the first time
                    Dim state As ExchangeProducerState = kit.Store.Load(agent.GetStatus().DeviceId)
                    Assert.Equal("Photos", state.CarrierName)
                    Assert.Equal(40007, state.CarrierPort)

                    acc.GrantedPort = 40999                                           ' a server that would now hand out another number for "any"
                    acc.DropAll()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishLog().Count >= 4, 15))
                    Dim again As String = acc.PublishLog().Last(Function(r) NameOf_(r) = "Photos")
                    Assert.Equal(40007, ReadInt(again, "publicPort"))                 ' the remembered number, so a router rule for it stays valid
                End Using
            End Using
        End Using
    End Function

    Private Shared Function ReadInt(raw As String, member As String) As Integer
        Using doc As JsonDocument = JsonDocument.Parse(raw)
            Return doc.RootElement.GetProperty(member).GetInt32()
        End Using
    End Function

    <Fact>
    Public Async Function A_remembered_port_the_server_cannot_give_is_asked_as_any_once() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                kit.Settings.SetThirdPartyPort(True)
                ' the first enrollment writes the device id; pre-seed the carrier for it after enrolling, then force a republish
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim deviceId As String = agent.GetStatus().DeviceId
                    kit.Store.Save(deviceId, kit.Store.Load(deviceId).WithCarrier("Photos", 41000))
                    acc.RefuseNextPublish("port-unavailable")
                    acc.DropAll()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishLog().Count >= 4, 15))
                    Dim again As String = acc.PublishLog().Last(Function(r) NameOf_(r) = "Photos")
                    Assert.Equal(0, ReadInt(again, "publicPort"))                     ' 41000 was refused, "any" was granted
                    Assert.Equal(40007, kit.Store.Load(deviceId).CarrierPort)
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ outsider links

    <Fact>
    Public Async Function A_link_is_made_on_demand_rotated_and_removed_and_the_record_follows() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.False(Latest(acc, "Photos").Contains("shareId", StringComparison.Ordinal))

                    Dim created As ExchangeLinkResult = Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)
                    Assert.Equal(ExchangeLinkOutcome.Done, created.Outcome)
                    Dim first As String = IdOf(Latest(acc, "Photos"), "shareId")
                    Assert.True(ExchangeIds.IsValidId(first))
                    Assert.False(Latest(acc, "Videos").Contains("shareId", StringComparison.Ordinal))   ' only the share that was asked for

                    Dim text As String = Nothing
                    Assert.True(kit.Publisher.TryGetLinkCode("Photos", text))
                    Dim code As FmsSftpCode = Nothing
                    Assert.True(FmsSftpCode.TryParse(text, code))
                    Assert.Equal("203.0.113.5:22022", code.Exchange)
                    Assert.Equal(first, code.ShareId)
                    Dim export As ExchangeExport = kit.Publisher.GetExport("Photos")
                    Assert.Equal("203.0.113.5", export.ClientHost)
                    Assert.Equal(22022, export.ClientPort)
                    Assert.Equal(first, export.ShareId)
                    Assert.Equal(0, export.RelayPort)
                    Assert.Null(kit.Publisher.GetExport("Videos"))

                    Dim rotated As ExchangeLinkResult = Await kit.Publisher.RotateLinkAsync("Photos", CancellationToken.None)
                    Assert.Equal(ExchangeLinkOutcome.Done, rotated.Outcome)
                    Dim second As String = IdOf(Latest(acc, "Photos"), "shareId")
                    Assert.NotEqual(first, second)

                    Dim removed As ExchangeLinkResult = Await kit.Publisher.RemoveLinkAsync("Photos", CancellationToken.None)
                    Assert.Equal(ExchangeLinkOutcome.Done, removed.Outcome)
                    Dim after As String = Latest(acc, "Photos")
                    Assert.False(after.Contains("shareId", StringComparison.Ordinal))
                    Assert.False(kit.Publisher.TryGetLinkCode("Photos", text))
                    Assert.Null(kit.Publisher.GetExport("Photos"))
                    Assert.Equal("", kit.Store.Load(agent.GetStatus().DeviceId).Get("Photos").ShareId)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_link_survives_a_reconnect_with_its_id() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal(ExchangeLinkOutcome.Done, (Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)).Outcome)
                    Dim sid As String = IdOf(Latest(acc, "Photos"), "shareId")
                    Dim count As Integer = acc.PublishLog().Count
                    acc.DropAll()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishLog().Count >= count + 2, 15))
                    Assert.Equal(sid, IdOf(Latest(acc, "Photos"), "shareId"))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Link_actions_say_why_they_cannot_be_done() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                ' not connected at all
                Assert.Equal(ExchangeLinkOutcome.NotConnected, (Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)).Outcome)
                kit.Settings.SetPublishShares(True)
                kit.PinLocked = True
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Block = ExchangePublishBlock.PinLocked))
                    Assert.Equal(ExchangeLinkOutcome.NotPublished, (Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)).Outcome)
                    kit.PinLocked = False
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal(ExchangeLinkOutcome.NotPublished, (Await kit.Publisher.CreateLinkAsync("Nope", CancellationToken.None)).Outcome)
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ tunnels

    <Fact>
    Public Async Function An_open_is_answered_by_attaching_and_pumping_bytes_both_ways_to_the_worker() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim photosId As String = IdOf(Latest(acc, "Photos"), "resourceId")
                    Dim a As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=photosId)
                    Assert.NotNull(a)
                    Dim payload As Byte() = Encoding.ASCII.GetBytes("SSH-2.0-test hello")
                    Await a.Stream.WriteAsync(payload, 0, payload.Length)
                    Await a.Stream.FlushAsync()
                    Dim echoed As Byte() = Await ReadExactlyAsync(a.Stream, payload.Length)
                    Assert.Equal(payload, echoed)                                      ' consumer -> worker -> consumer
                    Assert.Equal(1, kit.Worker.Accepted)
                    Assert.Equal(1, kit.Publisher.ActiveTunnels)
                    a.Release()
                End Using
            End Using
        End Using
    End Function

    Private Shared Async Function ReadExactlyAsync(s As Stream, count As Integer) As Task(Of Byte())
        Dim buf(count - 1) As Byte
        Dim got As Integer = 0
        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
            While got < count
                Dim n As Integer = Await s.ReadAsync(buf, got, count - got, cts.Token)
                If n = 0 Then Throw New EndOfStreamException()
                got += n
            End While
        End Using
        Return buf
    End Function

    <Fact>
    Public Async Function A_tunnel_by_share_id_reaches_the_same_share() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal(ExchangeLinkOutcome.Done, (Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)).Outcome)
                    Dim sid As String = IdOf(Latest(acc, "Photos"), "shareId")
                    Dim a As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(shareId:=sid)
                    Assert.NotNull(a)
                    Dim payload As Byte() = Encoding.ASCII.GetBytes("ping")
                    Await a.Stream.WriteAsync(payload, 0, payload.Length)
                    Await a.Stream.FlushAsync()
                    Assert.Equal(payload, Await ReadExactlyAsync(a.Stream, payload.Length))
                    a.Release()
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_unknown_share_and_a_worker_that_is_down_are_answered_by_closing_the_attach() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim unknown As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:="ICEiIyQlJicoKSorLC0uLw")
                    Assert.NotNull(unknown)
                    Assert.True(Await ExchangeStubAccount.EndedWithoutDataAsync(unknown))
                    unknown.Release()

                    kit.WorkerDialFails = True
                    Dim down As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=IdOf(Latest(acc, "Photos"), "resourceId"))
                    Assert.NotNull(down)
                    Assert.True(Await ExchangeStubAccount.EndedWithoutDataAsync(down))
                    down.Release()
                    ' The peer sees EOF as soon as the attach stream is disposed; the
                    ' producer releases its tunnel count immediately afterwards.
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.ActiveTunnels = 0),
                                "The failed worker attach must release its tunnel slot")
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ ticket 187: leaving

    <Fact>
    Public Async Function Leaving_unpublishes_every_resource_then_revokes_itself_and_forgets_everything() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim id As String = agent.GetStatus().DeviceId
                    Dim told As Boolean = Await agent.LeaveAccountAsync(CancellationToken.None)
                    Assert.True(told)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.Unpublished().Count = 2), "every resource is unpublished before the device goes")
                    Assert.Contains(id, acc.RevokedDevices())
                    Assert.False(agent.GetStatus().Configured)
                    Assert.False(kit.Settings.PublishShares())                      ' a later join publishes nothing until asked (170 rule 1)
                    Assert.False(kit.Rig.Ports.Credentials.Exists())
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Leaving_with_the_server_gone_still_forgets_locally_and_does_not_hang() As Task
        Dim agent As ExchangeDeviceAgent
        Using kit As New Kit()
            kit.Settings.SetPublishShares(True)
            Using acc As New ExchangeStubAccount()
                agent = Await EnrollOnlineAsync(kit, acc)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
            End Using                                                               ' the server is gone
            Using agent
                Dim started As DateTime = DateTime.UtcNow
                Dim told As Boolean = Await agent.LeaveAccountAsync(CancellationToken.None)
                Assert.False(told)                                                  ' the server could not be told
                Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8), "leaving is bounded, not a hang")
                Assert.False(agent.GetStatus().Configured)
                Assert.False(kit.Rig.Ports.Credentials.Exists())
                Assert.False(kit.Settings.PublishShares())
            End Using
        End Using
    End Function
    <Fact>
    Public Async Function At_most_the_cap_of_tunnels_per_resource_is_served_at_once_and_a_slot_is_freed_when_one_ends() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit(maxTunnels:=2)
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim id As String = IdOf(Latest(acc, "Photos"), "resourceId")
                    Dim one As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=id)
                    Dim two As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=id)
                    Assert.NotNull(one)
                    Assert.NotNull(two)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.ActiveTunnels = 2))

                    Dim three As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=id)
                    Assert.NotNull(three)
                    Assert.True(Await ExchangeStubAccount.EndedWithoutDataAsync(three))     ' over the cap: closed, no worker connection made
                    three.Release()
                    Assert.Equal(2, kit.Worker.Accepted)

                    ' another resource has its own count
                    Dim other As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=IdOf(Latest(acc, "Videos"), "resourceId"))
                    Dim payload As Byte() = Encoding.ASCII.GetBytes("x")
                    Await other.Stream.WriteAsync(payload, 0, 1)
                    Await other.Stream.FlushAsync()
                    Assert.Equal(payload, Await ReadExactlyAsync(other.Stream, 1))
                    other.Release()

                    one.Stream.Dispose()
                    one.Release()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.ActiveTunnels <= 1))
                    two.Release()
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_idle_tunnel_ends_after_the_idle_timeout() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit(idle:=TimeSpan.FromMilliseconds(400))
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Dim a As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=IdOf(Latest(acc, "Photos"), "resourceId"))
                    Assert.NotNull(a)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.ActiveTunnels = 1))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.ActiveTunnels = 0, 10))
                    a.Release()
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ secrets, forgetting

    <Fact>
    Public Async Function Nothing_secret_reaches_a_log_a_registry_value_or_a_plain_file() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                kit.Settings.SetThirdPartyPort(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal(ExchangeLinkOutcome.Done, (Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)).Outcome)
                    Dim photosId As String = IdOf(Latest(acc, "Photos"), "resourceId")
                    Dim sid As String = IdOf(Latest(acc, "Photos"), "shareId")
                    Dim a As ExchangeStubAccount.Attached = Await acc.OpenTunnelAsync(resourceId:=photosId)
                    Assert.NotNull(a)
                    a.Release()
                    Dim everything As String = kit.Rig.Everything() & String.Join(vbLf, kit.SettingsPort.Values.Select(Function(kv) kv.Key & "=" & kv.Value))
                    Assert.DoesNotContain(WorkerPassword, everything, StringComparison.Ordinal)
                    Assert.DoesNotContain(sid, everything, StringComparison.Ordinal)
                    Assert.DoesNotContain(photosId, everything, StringComparison.Ordinal)        ' ids are written as the first 8 hex of their hash
                    Assert.DoesNotContain(acc.TokenOf(agent.GetStatus().DeviceId), everything, StringComparison.Ordinal)
                    Assert.DoesNotContain("FMSSFTP", everything, StringComparison.Ordinal)
                    Assert.DoesNotContain("descriptor", everything, StringComparison.Ordinal)
                    Assert.Contains(ExchangeIds.ForLog(photosId), everything, StringComparison.Ordinal)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Disconnecting_forgets_the_ids_the_port_and_both_choices() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New Kit()
                kit.Settings.SetPublishShares(True)
                kit.Settings.SetThirdPartyPort(True)
                Using agent As ExchangeDeviceAgent = Await EnrollOnlineAsync(kit, acc)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.True(kit.Store.Exists())
                    Await agent.DisconnectAsync()
                    Assert.False(kit.Store.Exists())
                    Assert.False(kit.Settings.PublishShares())
                    Assert.False(kit.Settings.ThirdPartyPort())
                    Assert.Equal(ExchangePublishBlock.Off, kit.Publisher.Snapshot.Block)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Sub The_worker_is_untouched_no_exchange_member_exists_on_its_request_or_status()
        Dim request As String() = GetType(WorkerRequest).GetProperties().Select(Function(p) p.Name).ToArray()
        For Each banned As String In {"registerHost", "registerPort", "certFingerprint", "reissue", "enabled"}
            Assert.DoesNotContain(banned, request, StringComparer.OrdinalIgnoreCase)
        Next
        Dim status As String() = GetType(WorkerStatus).GetProperties().Select(Function(p) p.Name).ToArray()
        Assert.DoesNotContain("Exchange", status)
        Assert.DoesNotContain("ExchangeSupported", status)
        Assert.Null(GetType(WorkerResponse).Assembly.GetType("FastMediaSorterCompanion.WorkerExchange"))
        Assert.Null(GetType(WorkerResponse).Assembly.GetType("FastMediaSorterCompanion.WorkerExchangeShare"))
    End Sub

    <Fact>
    Public Sub The_producer_never_logs_an_access_a_share_id_or_a_raw_resource_id_by_construction()
        Dim src As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ExchangePublisher.vb"))
        Dim logCalls As New List(Of String)
        For Each line As String In src.Split({vbCrLf, vbLf}, StringSplitOptions.None)
            If line.Contains("_options.Log.Invoke(", StringComparison.Ordinal) Then logCalls.Add(line)
        Next
        Assert.NotEmpty(logCalls)
        For Each line As String In logCalls
            Assert.DoesNotContain("AccessJson", line, StringComparison.Ordinal)
            Assert.DoesNotContain("ShareId", line, StringComparison.Ordinal)
            Assert.DoesNotContain("Password", line, StringComparison.Ordinal)
            If line.Contains("ResourceId", StringComparison.Ordinal) OrElse line.Contains("tunnelId", StringComparison.Ordinal) Then
                Assert.Contains("ExchangeIds.ForLog(", line, StringComparison.Ordinal)
            End If
        Next
    End Sub

End Class
