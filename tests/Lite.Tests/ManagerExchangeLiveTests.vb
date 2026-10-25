#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit
Imports Xunit.Abstractions

Public NotInheritable Class ManagerExchangeLiveFactAttribute
    Inherits FactAttribute
    Public Sub New()
        If String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FMS261_RESOURCE")) OrElse
           String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FMS261_ROOT")) Then Skip = "needs an explicitly supplied expendable exchange root: run tests\Integration\ManagerExchangeLive.ps1"
    End Sub
End Class

''' <summary>Opt-in only. Mutations stay in one new operation-owned child of the supplied expendable root.</summary>
Public Class ManagerExchangeLiveTests
    Private ReadOnly output As ITestOutputHelper
    Public Sub New(output As ITestOutputHelper)
        Me.output = output
    End Sub

    <ManagerExchangeLiveFact>
    Public Sub SuppliedDeviceRootRecordsObservedVerbsAndByteHashes()
        Dim descriptor As ResourceDescriptor = ResourceRegistry.Load().Single(Function(d) d.Id = Environment.GetEnvironmentVariable("FMS261_RESOURCE"))
        Assert.True(ResourceOrigin.IsExchange(descriptor))
        Assert.False(String.IsNullOrEmpty(descriptor.ExchangeEntryId))
        Assert.False(String.IsNullOrEmpty(descriptor.PinnedFingerprint))
        Dim place As ManagerPlace = ManagerPlace.ForResource(descriptor)
        Dim testRoot As String = ManagerPathRules.NormalizeRemote(Environment.GetEnvironmentVariable("FMS261_ROOT"))
        Assert.NotNull(testRoot)
        Assert.True(ManagerPathRules.Contains(place.Kind, place.RootPath, testRoot))
        Assert.False(ManagerPathRules.AreEqual(place.Kind, place.RootPath, testRoot))
        ManagerExchangeAccess.Validate(place, descriptor, CancellationToken.None)
        Dim read As CredentialReadResult = ResourceCommit.ReadCredential(descriptor, New RegistryResourceSecretStore())
        Assert.Equal(CredentialReadOutcome.Ok, read.Outcome)
        Dim route As String = Environment.GetEnvironmentVariable("FMS261_ROUTE")
        Dim raw As New SftpEndpointDialer(descriptor, read.Secret,
            New HostKeyGate(descriptor.PinnedFingerprint, Function(fp, ct) False, Function(fp) False, Function() Environment.TickCount64), Function() False)
        Dim dialer As New RouteOnlyDialer(raw, route)
        Dim tunnel As IExchangeTunnelSource = If(route = "Tunnel", ExchangeTunnelSources.For(descriptor, New RegistryResourceSecretStore(), Function(fp, ct) False, Function(fp) False), Nothing)
        Dim connected As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, read.Secret, Function(fp, ct) False, Function(fp) False,
            Function() False, CancellationToken.None, dialerOverride:=dialer, tunnelSource:=tunnel).GetAwaiter().GetResult()
        Assert.True(connected.Succeeded, If(connected.Failure Is Nothing, "No provider", connected.Failure.ToString()))
        Dim owned As String = ManagerPathRules.Combine(place.Kind, testRoot, "fms-261-" & Guid.NewGuid().ToString("N"))
        Dim local As String = Path.Combine(Path.GetTempPath(), "fms-261-live-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(local)
        Dim verbs As New Collections.Generic.List(Of String)()
        Dim digest As String = ""
        Dim ownedCreated As Boolean
        Using remote As New ManagerEndpointLease(place, ManagerOpenResult.Ok(DirectCast(connected.Provider, IManagerDirectoryProvider), DirectCast(connected.Provider, IDisposable)))
            Try
                ManagerTreePlan.ValidatePath(remote, testRoot, allowRoot:=False)
                Assert.True(ManagerTreePlan.RequireEntry(remote, testRoot).IsDirectory)
                Assert.Equal(StatOutcome.Missing, remote.Directory.StatEntry(owned).Outcome)
                If Environment.GetEnvironmentVariable("FMS261_ACCESS") = "ReadOnly" Then
                    Dim refused As Exception = Xunit.Record.Exception(Sub() remote.Provider.CreateDirectory(owned))
                    If refused Is Nothing Then ownedCreated = True
                    Assert.NotNull(refused)
                    Dim failure As ResourceFailure = ResourceFailure.FromException(refused)
                    Assert.True(failure.Kind = ResourceFailureKind.SftpStatus OrElse failure.Kind = ResourceFailureKind.Unsupported, failure.ToString())
                    verbs.Add("mkdir-refused:" & failure.Kind.ToString())
                Else
                    Using create As New ManagerCommandRequest(ManagerCommandKind.CreateDirectory, New ManagerLocation(place, testRoot), 1,
                        Array.Empty(Of ManagerEntry)(), remote.Fork(), leafName:=ManagerPathRules.LeafName(place.Kind, owned))
                        ExecuteManagerTestPlan(BuildManagerTestPlan(create))
                    End Using
                    ownedCreated = True
                    verbs.Add("mkdir")
                    Dim bytes As Byte() = RandomNumberGenerator.GetBytes(131071)
                    digest = Convert.ToHexString(SHA256.HashData(bytes))
                    Dim input As String = Path.Combine(local, "exact-name.bin")
                    File.WriteAllBytes(input, bytes)
                    Using disk As New ManagerEndpointLease(ManagerPlace.ForDrive(local, local), ManagerOpenResult.Ok(New LocalProvider(Function() False)))
                        Using upload = MakeManagerRequest(ManagerCommandKind.Move, disk.Fork(), {input}, remote.Fork(), owned)
                            ExecuteManagerTestPlan(BuildManagerTestPlan(upload))
                        End Using
                        verbs.Add("upload-verified-move")
                        Dim original As String = ManagerPathRules.Combine(place.Kind, owned, "exact-name.bin")
                        Using rename As New ManagerCommandRequest(ManagerCommandKind.Rename, New ManagerLocation(place, owned), 1,
                            {ManagerTestRow(remote, original)}, remote.Fork(), leafName:="renamed.bin")
                            ExecuteManagerTestPlan(BuildManagerTestPlan(rename))
                        End Using
                        verbs.Add("rename")
                        Dim renamed As String = ManagerPathRules.Combine(place.Kind, owned, "renamed.bin")
                        Using download = MakeManagerRequest(ManagerCommandKind.Move, remote.Fork(), {renamed}, disk.Fork(), local)
                            ExecuteManagerTestPlan(BuildManagerTestPlan(download))
                        End Using
                        verbs.Add("download-verified-checked-delete")
                        Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(local, "renamed.bin")))))
                    End Using
                    Assert.Equal(DirectoryRemoval.Removed, remote.Directory.RemoveEmptyDirectory(place.RootPath, owned))
                    verbs.Add("rmdir")
                End If
                output.WriteLine("FMS261-EVIDENCE " & System.Text.Json.JsonSerializer.Serialize(New With {
                    .producer = Environment.GetEnvironmentVariable("FMS261_PRODUCER"), .route = route,
                    .expectedAccess = Environment.GetEnvironmentVariable("FMS261_ACCESS"), .verbs = verbs, .sha256 = digest,
                    .uiObserved = False, .conformancePromoted = False}))
            Finally
                ' Only exact names created by this run; never enumerate and recursively delete a user's tree.
                If ownedCreated Then
                    For Each leaf As String In {"exact-name.bin", "renamed.bin"}
                        Dim path As String = ManagerPathRules.Combine(place.Kind, owned, leaf)
                        If remote.Directory.StatEntry(path).Outcome = StatOutcome.Found Then remote.Provider.DeleteFile(path)
                    Next
                    If remote.Directory.StatEntry(owned).Outcome = StatOutcome.Found Then remote.Directory.RemoveEmptyDirectory(place.RootPath, owned)
                End If
                Directory.Delete(local, True)
            End Try
        End Using
    End Sub

    Private NotInheritable Class RouteOnlyDialer
        Implements IEndpointDialer
        Private ReadOnly inner As IEndpointDialer
        Private ReadOnly route As String
        Friend Sub New(inner As IEndpointDialer, route As String)
            Me.inner = inner
            Me.route = route
        End Sub
        Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
            If (route = "Tunnel") <> String.Equals(endpoint.Kind, ExchangeTunnelRules.TunnelKind, StringComparison.Ordinal) Then
                Return Task.FromResult(DialResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)))
            End If
            Return inner.DialAsync(endpoint, ct)
        End Function
    End Class

    Private NotInheritable Class LoopbackDataOpener
        Implements IExchangeDataOpener
        Private ReadOnly port As Integer
        Friend Sub New(port As Integer)
            Me.port = port
        End Sub
        Public Async Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeOpenResult) Implements IExchangeDataOpener.OpenAsync
            Dim client As New System.Net.Sockets.TcpClient()
            Try
                Await client.ConnectAsync(System.Net.IPAddress.Loopback, port, ct)
                Return New ExchangeOpenResult(ExchangeOpenOutcome.Connected, client.GetStream(), Nothing, Nothing, Nothing, ExchangeRefusal.Unknown, "")
            Catch
                client.Dispose()
                Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Network)
            End Try
        End Function
    End Class

    Private NotInheritable Class LoopbackTunnelSource
        Implements IExchangeTunnelSource
        Private ReadOnly port As Integer
        Friend ReadOnly Leases As New Collections.Generic.List(Of ExchangeTunnelLease)()
        Friend Sub New(port As Integer)
            Me.port = port
        End Sub
        Public Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeTunnelOpen) Implements IExchangeTunnelSource.OpenAsync
            Dim lease As New ExchangeTunnelLease(New LoopbackDataOpener(port), 0)
            lease.Start()
            Leases.Add(lease)
            Return Task.FromResult(New ExchangeTunnelOpen(lease, ExchangeTunnelFailure.None))
        End Function
    End Class

    <WritableLiveFact>
    Public Sub Live_LoopbackWindowsExchange_DirectAndTunnel_WritableVerbs_ArchiveOnDevice_And_RemoteToRemoteRelay()
        Using serverA As New WritableServerFixture(),
              serverB As New WritableServerFixture()
            Dim work As String = Path.Combine(Path.GetTempPath(), "fms-261-loopback-" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(work)
            Try
                For Each route As String In {"Direct", "Tunnel"}
                    Dim descriptorA As New ResourceDescriptor With {
                        .Id = "ex-win-a-" & route.ToLowerInvariant(),
                        .DisplayName = "Windows Exchange A (" & route & ")",
                        .Kind = ResourceKind.Sftp,
                        .Origin = ResourceOrigin.Exchange,
                        .ExchangeEntryId = "entry-win-loopback",
                        .ExchangeDeviceId = "dev-win-a",
                        .ExchangeResourceId = "res-win-a",
                        .Host = If(route = "Direct", "127.0.0.1", "127.0.0.1"),
                        .Port = If(route = "Direct", serverA.Port, 1),
                        .UserName = serverA.User,
                        .RootPath = "/MOV",
                        .PinnedFingerprint = serverA.Fingerprint
                    }
                    Dim tunnelSourceA As LoopbackTunnelSource = If(route = "Tunnel", New LoopbackTunnelSource(serverA.Port), Nothing)
                    Dim rawA As New SftpEndpointDialer(descriptorA, serverA.Password,
                        New HostKeyGate(descriptorA.PinnedFingerprint, Function(fp, ct) False, Function(fp) False, Function() Environment.TickCount64), Function() False)
                    Dim dialerA As New RouteOnlyDialer(rawA, route)
                    Dim connectedA As ConnectOutcome = ResourceConnect.ConnectAsync(descriptorA, serverA.Password,
                        Function(fp, ct) False, Function(fp) False, Function() False, CancellationToken.None,
                        dialerOverride:=dialerA, tunnelSource:=tunnelSourceA).GetAwaiter().GetResult()
                    Assert.True(connectedA.Succeeded, "Failed to connect " & route & ": " & If(connectedA.Failure?.ToString(), "null"))
                    If route = "Tunnel" Then
                        Assert.NotEmpty(tunnelSourceA.Leases)
                        Assert.True(tunnelSourceA.Leases(0).ConnectsStarted >= 1)
                    End If

                    Dim placeA As ManagerPlace = ManagerPlace.ForResource(descriptorA)
                    Dim localDir As String = Path.Combine(work, "local-" & route)
                    Directory.CreateDirectory(localDir)
                    Dim owned As String = "/MOV/ex-261-" & route.ToLowerInvariant() & "-" & Guid.NewGuid().ToString("N").Substring(0, 8)

                    Using remoteA As New ManagerEndpointLease(placeA, ManagerOpenResult.Ok(DirectCast(connectedA.Provider, IManagerDirectoryProvider), DirectCast(connectedA.Provider, IDisposable))),
                          disk As New ManagerEndpointLease(ManagerPlace.ForDrive(localDir, localDir), ManagerOpenResult.Ok(New LocalProvider(Function() False)))
                        ' 1. CreateDirectory, Upload/Move, Rename, Case-distinct files, Download/CheckedDelete, Archive-on-Device
                        Using mkdirReq As New ManagerCommandRequest(ManagerCommandKind.CreateDirectory, New ManagerLocation(placeA, "/MOV"), 1,
                            Array.Empty(Of ManagerEntry)(), remoteA.Fork(), leafName:=ManagerPathRules.LeafName(placeA.Kind, owned))
                            ExecuteManagerTestPlan(BuildManagerTestPlan(mkdirReq))
                        End Using

                        Dim payload As Byte() = RandomNumberGenerator.GetBytes(65537)
                        Dim expectedHash As String = Convert.ToHexString(SHA256.HashData(payload))
                        Dim localFile As String = Path.Combine(localDir, "payload.bin")
                        File.WriteAllBytes(localFile, payload)

                        Using uploadReq = MakeManagerRequest(ManagerCommandKind.Move, disk.Fork(), {localFile}, remoteA.Fork(), owned)
                            ExecuteManagerTestPlan(BuildManagerTestPlan(uploadReq))
                        End Using
                        Assert.False(File.Exists(localFile))

                        Dim remoteOriginal As String = owned & "/payload.bin"
                        Using renameReq As New ManagerCommandRequest(ManagerCommandKind.Rename, New ManagerLocation(placeA, owned), 1,
                            {ManagerTestRow(remoteA, remoteOriginal)}, remoteA.Fork(), leafName:="renamed.bin")
                            ExecuteManagerTestPlan(BuildManagerTestPlan(renameReq))
                        End Using

                        ' Archive on device (create verified ZIP on exchange device root + extract on device root)
                        Dim zipTree As String = Path.Combine(localDir, "zip_src")
                        Directory.CreateDirectory(Path.Combine(zipTree, "nested_empty"))
                        File.WriteAllText(Path.Combine(zipTree, "device_note.txt"), "exchange archive payload " & route)
                        Dim zipCreateReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, disk.Fork(), {zipTree}, remoteA.Fork(), owned, leaf:="device_bundle.zip")
                        Dim zipJob As ManagerArchiveJob = ManagerArchiveJob.Prepare(zipCreateReq, New ManagerArchiveOptions With {.DeleteOriginals = True}, Path.Combine(work, "scratch-" & route))
                        Using pub As ManagerCommandRequest = zipJob.Request
                            ExecuteManagerTestPlan(zipJob.AddCleanup(BuildManagerTestPlan(pub),
                                Function(l, e) New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = PermanentReason.UserAsked}))
                        End Using
                        Assert.False(Directory.Exists(zipTree))
                        Assert.Equal(StatOutcome.Found, remoteA.Directory.StatEntry(owned & "/device_bundle.zip").Outcome)

                        remoteA.Provider.CreateDirectory(owned & "/unpacked")
                        Dim zipExtractReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, remoteA.Fork(), {owned & "/device_bundle.zip"}, remoteA.Fork(), owned & "/unpacked")
                        Dim extractJob As ManagerArchiveJob = ManagerArchiveJob.Prepare(zipExtractReq, New ManagerArchiveOptions With {.Extract = True}, Path.Combine(work, "scratch-ext-" & route))
                        Using pub As ManagerCommandRequest = extractJob.Request
                            ExecuteManagerTestPlan(extractJob.AddCleanup(BuildManagerTestPlan(pub), Nothing))
                        End Using
                        Dim verifyUnpacked As String = Path.Combine(localDir, "verify_note.txt")
                        remoteA.Provider.Download(owned & "/unpacked/zip_src/device_note.txt", verifyUnpacked, Nothing, CancellationToken.None)
                        Assert.Equal("exchange archive payload " & route, File.ReadAllText(verifyUnpacked))

                        ' Download verified with checked source delete
                        Dim remoteRenamed As String = owned & "/renamed.bin"
                        Using downloadReq = MakeManagerRequest(ManagerCommandKind.Move, remoteA.Fork(), {remoteRenamed}, disk.Fork(), localDir)
                            ExecuteManagerTestPlan(BuildManagerTestPlan(downloadReq))
                        End Using
                        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(localDir, "renamed.bin")))))

                        ' Remote-to-Remote relay (serverA -> this PC scratch -> serverB) over Tunnel
                        If route = "Tunnel" Then
                            Dim descriptorB As New ResourceDescriptor With {
                                .Id = "ex-win-b-tunnel",
                                .DisplayName = "Windows Exchange B (Tunnel)",
                                .Kind = ResourceKind.Sftp,
                                .Origin = ResourceOrigin.Exchange,
                                .ExchangeEntryId = "entry-win-loopback",
                                .ExchangeDeviceId = "dev-win-b",
                                .ExchangeResourceId = "res-win-b",
                                .Host = "127.0.0.1",
                                .Port = 1,
                                .UserName = serverB.User,
                                .RootPath = "/MOV",
                                .PinnedFingerprint = serverB.Fingerprint
                            }
                            Dim tunnelSourceB As New LoopbackTunnelSource(serverB.Port)
                            Dim rawB As New SftpEndpointDialer(descriptorB, serverB.Password,
                                New HostKeyGate(descriptorB.PinnedFingerprint, Function(fp, ct) False, Function(fp) False, Function() Environment.TickCount64), Function() False)
                            Dim connectedB As ConnectOutcome = ResourceConnect.ConnectAsync(descriptorB, serverB.Password,
                                Function(fp, ct) False, Function(fp) False, Function() False, CancellationToken.None,
                                dialerOverride:=New RouteOnlyDialer(rawB, "Tunnel"), tunnelSource:=tunnelSourceB).GetAwaiter().GetResult()
                            Assert.True(connectedB.Succeeded)
                            Using remoteB As New ManagerEndpointLease(ManagerPlace.ForResource(descriptorB),
                                ManagerOpenResult.Ok(DirectCast(connectedB.Provider, IManagerDirectoryProvider), DirectCast(connectedB.Provider, IDisposable)))
                                Dim relayDest As String = "/MOV/relayed-" & Guid.NewGuid().ToString("N").Substring(0, 8)
                                remoteB.Provider.CreateDirectory(relayDest)
                                Using relayMove = MakeManagerRequest(ManagerCommandKind.Move, remoteA.Fork(), {owned & "/device_bundle.zip"}, remoteB.Fork(), relayDest)
                                    Dim relayPlan As BatchPlan = BuildManagerTestPlan(relayMove)
                                    Dim preflight As New ManagerTransferPreflight(relayPlan.Operations, remoteA.Capabilities, remoteB.Capabilities, sameResource:=False)
                                    Assert.True(preflight.RelaysThroughPc)
                                    Assert.False(String.IsNullOrWhiteSpace(ManagerTransferPreflight.RelayText()))
                                    ExecuteManagerTestPlan(relayPlan)
                                End Using
                                Assert.Equal(StatOutcome.Missing, remoteA.Directory.StatEntry(owned & "/device_bundle.zip").Outcome)
                                Assert.Equal(StatOutcome.Found, remoteB.Directory.StatEntry(relayDest & "/device_bundle.zip").Outcome)
                            End Using
                        End If

                        output.WriteLine("FMS261-LOOPBACK-EVIDENCE " & System.Text.Json.JsonSerializer.Serialize(New With {
                            .producer = "Windows-Loopback", .route = route, .access = "Writable",
                            .verbs = New String() {"mkdir", "upload-verified-move", "rename", "archive-create-on-device", "archive-extract-on-device", "download-verified-checked-delete", "remote-to-remote-relay"},
                            .sha256 = expectedHash}))
                    End Using
                Next
            Finally
                Directory.Delete(work, True)
            End Try
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_LoopbackWindowsExchange_DirectAndTunnel_ReadOnlyRoot_ListsDownloadsAndRefusesWrites()
        Using roServer As New LiveServerFixture()
            Dim work As String = Path.Combine(Path.GetTempPath(), "fms-261-ro-loopback-" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(work)
            Try
                For Each route As String In {"Direct", "Tunnel"}
                    Dim descriptor As New ResourceDescriptor With {
                        .Id = "ex-win-ro-" & route.ToLowerInvariant(),
                        .DisplayName = "Windows ReadOnly (" & route & ")",
                        .Kind = ResourceKind.Sftp,
                        .Origin = ResourceOrigin.Exchange,
                        .ExchangeEntryId = "entry-win-ro",
                        .ExchangeDeviceId = "dev-win-ro",
                        .ExchangeResourceId = "res-win-ro",
                        .Host = "127.0.0.1",
                        .Port = If(route = "Direct", roServer.Port, 1),
                        .UserName = roServer.User,
                        .RootPath = "/" & LiveServerFixture.RootName,
                        .PinnedFingerprint = roServer.Fingerprint
                    }
                    Dim tunnelSource As LoopbackTunnelSource = If(route = "Tunnel", New LoopbackTunnelSource(roServer.Port), Nothing)
                    Dim raw As New SftpEndpointDialer(descriptor, roServer.Plan.Password,
                        New HostKeyGate(descriptor.PinnedFingerprint, Function(fp, ct) False, Function(fp) False, Function() Environment.TickCount64), Function() False)
                    Dim connected As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, roServer.Plan.Password,
                        Function(fp, ct) False, Function(fp) False, Function() False, CancellationToken.None,
                        dialerOverride:=New RouteOnlyDialer(raw, route), tunnelSource:=tunnelSource).GetAwaiter().GetResult()
                    Assert.True(connected.Succeeded)

                    Dim place As ManagerPlace = ManagerPlace.ForResource(descriptor)
                    Using remote As New ManagerEndpointLease(place, ManagerOpenResult.Ok(DirectCast(connected.Provider, IManagerDirectoryProvider), DirectCast(connected.Provider, IDisposable)))
                        ' Listing succeeds and includes folders + files
                        Dim listing As DirectoryListing = remote.Directory.ListChildren(place.RootPath, CancellationToken.None)
                        Assert.Equal(DirectoryOutcome.Listed, listing.Outcome)
                        Assert.Contains(listing.Entries, Function(e) e.IsDirectory AndAlso e.Name = "sub")
                        Assert.Contains(listing.Entries, Function(e) Not e.IsDirectory AndAlso e.Name = "only.txt")

                        ' External open downloads to ManagerExternalCache and retains lease with non-writeback notice
                        Dim leasePath As String = ManagerExternalCache.PrepareLeasePath(descriptor.Id, place.RootPath & "/only.txt", "only.txt")
                        ResourceTransfer.DownloadVerified(remote.Provider, place.RootPath & "/only.txt", leasePath, True, Nothing, CancellationToken.None)
                        ManagerExternalCache.RetainLease(leasePath)
                        Assert.Equal("read only share content", File.ReadAllText(leasePath))
                        Assert.False(String.IsNullOrWhiteSpace(ManagerExternalCache.RemoteNoticeText()))

                        ' Write verbs are refused by the read-only producer server with typed ResourceFailure
                        Dim refusedMkdir As Exception = Xunit.Record.Exception(Sub() remote.Provider.CreateDirectory(place.RootPath & "/forbid-" & route))
                        Assert.NotNull(refusedMkdir)
                        Dim mkdirFailure As ResourceFailure = ResourceFailure.FromException(refusedMkdir)
                        Assert.True(mkdirFailure.Kind = ResourceFailureKind.SftpStatus OrElse mkdirFailure.Kind = ResourceFailureKind.Unsupported, mkdirFailure.ToString())

                        Dim refusedDelete As Exception = Xunit.Record.Exception(Sub() remote.Provider.DeleteFile(place.RootPath & "/only.txt"))
                        Assert.NotNull(refusedDelete)
                        Dim deleteFailure As ResourceFailure = ResourceFailure.FromException(refusedDelete)
                        Assert.True(deleteFailure.Kind = ResourceFailureKind.SftpStatus OrElse deleteFailure.Kind = ResourceFailureKind.Unsupported, deleteFailure.ToString())

                        output.WriteLine("FMS261-LOOPBACK-EVIDENCE " & System.Text.Json.JsonSerializer.Serialize(New With {
                            .producer = "Windows-Devserve", .route = route, .access = "ReadOnly",
                            .verbs = New String() {"list", "download-verified-external-open", "mkdir-refused"}}))
                    End Using
                Next
            Finally
                Directory.Delete(work, True)
            End Try
        End Using
    End Sub
End Class
#End If
