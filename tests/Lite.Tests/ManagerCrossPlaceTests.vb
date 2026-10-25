#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports Xunit

Public Class ManagerCrossPlaceTests
    <Theory>
    <InlineData(ManagerExchangeAvailability.Locked)>
    <InlineData(ManagerExchangeAvailability.Removed)>
    <InlineData(ManagerExchangeAvailability.SignInRequired)>
    Public Sub EveryCommandOnRefusedRootKeepsSourceAndDisplayCache(state As Integer)
        Dim cache As String = Path.Combine(Path.GetTempPath(), "fms-261-cache-" & Guid.NewGuid().ToString("N"))
        File.WriteAllText(cache, "display cache sentinel")
        Try
            For Each kind As ManagerCommandKind In [Enum].GetValues(Of ManagerCommandKind)()
                Dim source As New ManagerFakeProvider()
                Dim target As New ManagerFakeProvider()
                source.PutFile("/root/file.txt")
                source.Entries("/root/file.txt").LocalPath = cache
                Using lease As New ManagerEndpointLease(New ManagerPlace("source", "source", ResourceKind.Sftp, "/root"), ManagerOpenResult.Ok(source, source),
                    Function(provider)
                        ManagerExchangeAccess.RequireAvailable(CType(state, ManagerExchangeAvailability))
                        Return provider.Capabilities
                    End Function), destination = ManagerTestLease(target, "destination"),
                    request = MakeManagerRequest(kind, lease.Fork(), {"/root/file.txt"}, destination.Fork(), leaf:="new-name")
                    Assert.Throws(Of ManagerExchangeException)(Sub() BuildManagerTestPlan(request))
                    Assert.Equal(0, source.DeleteCalls)
                    Assert.Empty(target.Bytes)
                    Assert.True(source.Bytes.ContainsKey("/root/file.txt"))
                    Assert.Equal("display cache sentinel", File.ReadAllText(cache))
                End Using
            Next
        Finally
            File.Delete(cache)
        End Try
    End Sub

    <Theory>
    <InlineData(False, False)>
    <InlineData(False, True)>
    <InlineData(True, False)>
    <InlineData(True, True)>
    Public Sub AllDirectionsUseVerifiedExecutor(sourceLocal As Boolean, destinationLocal As Boolean)
        Dim temp As String = Path.Combine(Path.GetTempPath(), "fms-261-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temp)
        Dim source As New ManagerFakeProvider()
        Dim destination As New ManagerFakeProvider()
        source.PutFile("/root/TrueName.txt", "exact bytes")
        Dim localSource As String = Path.Combine(temp, "source")
        Dim localDestination As String = Path.Combine(temp, "destination")
        Directory.CreateDirectory(localSource)
        Directory.CreateDirectory(localDestination)
        File.WriteAllText(Path.Combine(localSource, "TrueName.txt"), "exact bytes")
        Try
            Using sourceLease As ManagerEndpointLease = If(sourceLocal, LocalLease(localSource), ManagerTestLease(source)),
                  destinationLease As ManagerEndpointLease = If(destinationLocal, LocalLease(localDestination), ManagerTestLease(destination, "destination"))
                Dim sourcePath As String = If(sourceLocal, Path.Combine(localSource, "TrueName.txt"), "/root/TrueName.txt")
                Using request = MakeManagerRequest(ManagerCommandKind.Move, sourceLease.Fork(), {sourcePath}, destinationLease.Fork(),
                                                  If(destinationLocal, localDestination, "/root"))
                    Dim plan As BatchPlan = BuildManagerTestPlan(request)
                    ExecuteManagerTestPlan(plan)
                    Assert.True(plan.Operations.Single().ManagerOperation.Completed)
                    Assert.Equal(TransferResult.Completed, plan.Operations.Single().ManagerOperation.Outcome.Result)
                    If destinationLocal Then
                        Assert.Equal("exact bytes", File.ReadAllText(Path.Combine(localDestination, "TrueName.txt")))
                    Else
                        Assert.Equal("exact bytes", Text.Encoding.UTF8.GetString(destination.Bytes("/root/TrueName.txt")))
                    End If
                    Assert.Equal(StatOutcome.Missing, sourceLease.Directory.StatEntry(sourcePath).Outcome)
                End Using
            End Using
        Finally
            Directory.Delete(temp, True)
        End Try
    End Sub

    Private Shared Function LocalLease(root As String) As ManagerEndpointLease
        Return New ManagerEndpointLease(ManagerPlace.ForDrive(root, root), ManagerOpenResult.Ok(New LocalProvider(Function() False)))
    End Function

    <Fact>
    Public Sub SameResourceFoldersPreserveCaseDistinctNames()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/A.txt", "upper")
        provider.PutFile("/root/a.txt", "lower")
        provider.PutDirectory("/root/destination")
        Using source = ManagerTestLease(provider), destination = source.Fork(),
              request = MakeManagerRequest(ManagerCommandKind.Copy, source.Fork(), {"/root/A.txt", "/root/a.txt"}, destination.Fork(), "/root/destination")
            ExecuteManagerTestPlan(BuildManagerTestPlan(request))
            Assert.Equal("upper", Text.Encoding.UTF8.GetString(provider.Bytes("/root/destination/A.txt")))
            Assert.Equal("lower", Text.Encoding.UTF8.GetString(provider.Bytes("/root/destination/a.txt")))
            Assert.Equal(0, provider.DeleteCalls)
        End Using
    End Sub

    <Theory>
    <InlineData(ManagerCommandKind.Copy, True)>
    <InlineData(ManagerCommandKind.Move, False)>
    <InlineData(ManagerCommandKind.Rename, False)>
    <InlineData(ManagerCommandKind.CreateDirectory, False)>
    <InlineData(ManagerCommandKind.Delete, False)>
    <InlineData(ManagerCommandKind.DeletePermanently, False)>
    Public Sub ReadonlySourcePermissionTable(kind As Integer, allowed As Boolean)
        Dim provider As New ManagerFakeProvider With {.Caps = New ProviderCapabilities(canWrite:=False, canDelete:=False, canRename:=False, canCreateFolder:=False)}
        provider.PutFile("/root/file.txt")
        Using lease = ManagerTestLease(provider)
            Assert.Equal(allowed, ManagerCommandPlanner.Allows(CType(kind, ManagerCommandKind), lease.Capabilities, {ManagerTestRow(lease, "/root/file.txt")}, ManagerLocation.AtRoot(lease.Place)))
        End Using
    End Sub

    <Fact>
    Public Sub StaleQueuedPermissionRefusesWithoutWriteOrDelete()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/file.txt")
        Dim destination As New ManagerFakeProvider()
        Dim available As Boolean = True
        Using source As New ManagerEndpointLease(New ManagerPlace("source", "source", ResourceKind.Sftp, "/root"), ManagerOpenResult.Ok(provider, provider),
            Function(p)
                ManagerExchangeAccess.RequireAvailable(If(available, ManagerExchangeAvailability.Available, ManagerExchangeAvailability.Removed))
                Return p.Capabilities
            End Function), target = ManagerTestLease(destination, "target"),
            request = MakeManagerRequest(ManagerCommandKind.Move, source.Fork(), {"/root/file.txt"}, target.Fork())
            Dim plan = BuildManagerTestPlan(request)
            available = False
            Assert.Throws(Of ManagerExchangeException)(Sub() ExecuteManagerTestPlan(plan))
            Assert.Equal(0, provider.DeleteCalls)
            Assert.Empty(destination.Bytes)
            Assert.True(provider.Bytes.ContainsKey("/root/file.txt"))
        End Using
    End Sub
End Class
#End If
