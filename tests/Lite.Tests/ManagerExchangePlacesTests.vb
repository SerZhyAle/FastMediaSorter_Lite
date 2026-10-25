#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Friend NotInheritable Class ManagerDirectorySpy
    Implements IExchangeAgentApi
    Friend Property Snapshot As ExchangeDirectorySnapshot = ExchangeDirectorySnapshot.Empty
    Friend Property State As ExchangeAgentState = ExchangeAgentState.Online
    Friend Property StateCalls As Integer
    Friend Property DirectoryCalls As Integer
    Friend Property AccessCalls As Integer
    Friend Property ThrowOnState As Boolean
    Public Function GetStateAsync(ct As CancellationToken) As Task(Of ExchangeAgentStateResult) Implements IExchangeAgentApi.GetStateAsync
        StateCalls += 1
        If ThrowOnState Then Throw New IO.IOException("Injected directory failure")
        Return Task.FromResult(New ExchangeAgentStateResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", ""),
            New ExchangeAgentStateInfo(True, State, Array.Empty(Of String)(), "self", "self", "")))
    End Function
    Public Function GetDirectoryAsync(revision As Long, wait As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDirectoryResult) Implements IExchangeAgentApi.GetDirectoryAsync
        DirectoryCalls += 1
        Return Task.FromResult(New ExchangeAgentDirectoryResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", ""), Snapshot))
    End Function
    Public Function GetAccessAsync(id As String, ct As CancellationToken) As Task(Of ExchangeAgentAccessResult) Implements IExchangeAgentApi.GetAccessAsync
        AccessCalls += 1
        Throw New InvalidOperationException("Discovery must never request access.")
    End Function
End Class

Friend Module ManagerExchangeFixture
    Friend Function ExchangeTestAccount(Optional id As String = "entry") As ExchangeEntry
        Return New ExchangeEntry(id, "example.invalid:1234", "viewer", "", ExchangeCertificateKind.None, "self", "self", "Same label", False)
    End Function
    Friend Function ExchangeTestDevice(Optional id As String = "device", Optional presence As String = "online") As ExchangeDeviceRecord
        Return New ExchangeDeviceRecord("{}", id, "Same label", "producer", "1", "android", Array.Empty(Of String)(), presence, "")
    End Function
    Friend Function ExchangeTestRoot(Optional id As String = "resource", Optional deviceId As String = "device", Optional locked As Boolean = False,
                         Optional scheme As String = "pin-v1") As ExchangeResourceRecord
        Return New ExchangeResourceRecord("{}", id, deviceId, ExchangeWire.KindSftpShare, "Same label", "online", "", locked, scheme)
    End Function
    Friend Function ExchangeTestDescriptor() As ResourceDescriptor
        Return New ResourceDescriptor With {.Id = "saved", .DisplayName = "Same label", .Kind = ResourceKind.Sftp, .RootPath = "/root",
            .Origin = ResourceOrigin.Exchange, .ExchangeEntryId = "entry", .ExchangeDeviceId = "device", .ExchangeResourceId = "resource"}
    End Function
    Friend Function ExchangeTestDirectoryOf(ParamArray roots As ExchangeResourceRecord()) As ExchangeDirectorySnapshot
        Return New ExchangeDirectorySnapshot(1, {ExchangeTestDevice(), ExchangeTestDevice("device2")}, roots, Array.Empty(Of ExchangeBroadcastRecord)())
    End Function
    Friend Function ExchangeTestReady(snapshot As ExchangeDirectorySnapshot) As ExchangeDevicesState
        Return New ExchangeDevicesState(ExchangeDevicesAvailability.Ready, Nothing, snapshot)
    End Function
End Module

Public Class ManagerExchangePlacesTests
    <Fact>
    Public Async Function OneFailedAccountDoesNotHideAnotherAccount() As Task
        Dim failed As New ManagerDirectorySpy With {.ThrowOnState = True}
        Dim ready As New ManagerDirectorySpy With {.Snapshot = ExchangeTestDirectoryOf(ExchangeTestRoot())}
        Using adapter As New ManagerExchangePlaces(Function() {ExchangeTestAccount("failed"), ExchangeTestAccount()},
            Function() {ExchangeTestDescriptor()}, Function(e) New ManagerExchangeConnection(If(e.Id = "failed", failed, ready), Nothing))
            Dim snapshot = Await adapter.RefreshAsync(CancellationToken.None)
            Assert.Equal(ManagerExchangeAvailability.Offline, snapshot.Nodes.Single(Function(n) n.EntryId = "failed").State)
            Assert.Equal(ManagerExchangeAvailability.Available, snapshot.Nodes.Single(Function(n) n.Place IsNot Nothing).State)
            Assert.Equal(0, failed.AccessCalls + ready.AccessCalls)
        End Using
    End Function

    <Fact>
    Public Sub SameAddressAcrossAccountsRequiresExplicitPlaceSelection()
        Dim first As ResourceDescriptor = ExchangeTestDescriptor()
        first.Host = "example.invalid"
        first.Port = 22
        first.UserName = "viewer"
        Dim second As ResourceDescriptor = ExchangeTestDescriptor()
        second.Id = "other"
        second.ExchangeEntryId = "other-account"
        second.Host = first.Host
        second.Port = first.Port
        second.UserName = first.UserName
        Assert.Equal(ManagerAddressKind.AmbiguousResource, ManagerAddress.Resolve("sftp://viewer@example.invalid/root", {first, second}).Kind)
        Assert.NotEqual(ManagerPlace.ForResource(first).Id, ManagerPlace.ForResource(second).Id)
        second.Id = first.Id
        Assert.NotEqual(ManagerPlace.ForResource(first).Id, ManagerPlace.ForResource(second).Id)
    End Sub

    <Fact>
    Public Sub PresenceUpdatesKeepDeviceAndAccountOrderStable()
        Dim before = ManagerExchangePlaces.Project(ExchangeTestAccount(), ExchangeTestReady(ExchangeTestDirectoryOf(ExchangeTestRoot(), ExchangeTestRoot("second", "device2"))),
            Array.Empty(Of ResourceDescriptor)(), Array.Empty(Of ManagerExchangeNode)())
        Dim snapshot As New ExchangeDirectorySnapshot(2, {ExchangeTestDevice("device2", "offline"), ExchangeTestDevice()},
            {ExchangeTestRoot("second", "device2"), ExchangeTestRoot()}, Array.Empty(Of ExchangeBroadcastRecord)())
        Dim after = ManagerExchangePlaces.Project(ExchangeTestAccount(), ExchangeTestReady(snapshot), Array.Empty(Of ResourceDescriptor)(), before)
        Assert.Equal(before.Select(Function(n) n.Id), after.Select(Function(n) n.Id))
    End Sub

    <Fact>
    Public Sub RevokedSignInAndEmptyAreDistinctSnapshots()
        Dim revoked As New ExchangeDevicesState(ExchangeDevicesAvailability.Offline,
            New ExchangeAgentStateInfo(True, ExchangeAgentState.SignInAgain, Array.Empty(Of String)(), "self", "self", "device-revoked"), Nothing)
        Dim signedOut As New ExchangeDevicesState(ExchangeDevicesAvailability.NotSignedIn, Nothing, Nothing)
        Dim prior As ManagerExchangeNode() = Array.Empty(Of ManagerExchangeNode)()
        Assert.Equal(ManagerExchangeAvailability.Revoked, ManagerExchangePlaces.Project(ExchangeTestAccount(), revoked, Array.Empty(Of ResourceDescriptor)(), prior)(0).State)
        Assert.Equal(ManagerExchangeAvailability.SignInRequired, ManagerExchangePlaces.Project(ExchangeTestAccount(), signedOut, Array.Empty(Of ResourceDescriptor)(), prior)(0).State)
        Assert.Equal(ManagerExchangeAvailability.Available, ManagerExchangePlaces.Project(ExchangeTestAccount(), ExchangeTestReady(ExchangeDirectorySnapshot.Empty), Array.Empty(Of ResourceDescriptor)(), prior)(0).State)
    End Sub

    <Fact>
    Public Async Function NoAccountMakesNoDirectorySessionTunnelOrAccessCalls() As Task
        Dim connections As Integer
        Using adapter As New ManagerExchangePlaces(Function() Array.Empty(Of ExchangeEntry)(), Function() {ExchangeTestDescriptor()},
            Function(e)
                connections += 1
                Throw New InvalidOperationException("No account may connect.")
            End Function)
            Assert.Empty((Await adapter.RefreshAsync(CancellationToken.None)).Nodes)
            Assert.Equal(0, connections)
        End Using
    End Function

    <Fact>
    Public Async Function DiscoveryOnlyListsAndPublishesOneGeneration() As Task
        Dim spy As New ManagerDirectorySpy With {.Snapshot = ExchangeTestDirectoryOf(ExchangeTestRoot(), ExchangeTestRoot("other", "device2"))}
        Using adapter As New ManagerExchangePlaces(Function() {ExchangeTestAccount()}, Function() Array.Empty(Of ResourceDescriptor)(),
            Function(e) New ManagerExchangeConnection(spy, Nothing))
            Dim first As ManagerExchangeSnapshot = Await adapter.RefreshAsync(CancellationToken.None)
            Assert.Equal(2, first.Nodes.Where(Function(n) n.Kind = ManagerExchangeNodeKind.Root).Count())
            Assert.All(first.Nodes, Sub(n) Assert.Null(n.Place))
            Assert.Equal(first.Nodes.Count, first.Nodes.Select(Function(n) n.Id).Distinct().Count())
            Assert.Equal(0, spy.AccessCalls)
            Dim second As ManagerExchangeSnapshot = Await adapter.RefreshAsync(CancellationToken.None)
            Assert.True(second.Generation > first.Generation)
            Assert.Same(second, adapter.Current)
        End Using
    End Function

    <Fact>
    Public Sub DuplicateLabelsAcrossAccountsAndDevicesNeverAlias()
        Dim snapshot As ExchangeDirectorySnapshot = ExchangeTestDirectoryOf(ExchangeTestRoot(), ExchangeTestRoot("other", "device2"))
        Dim first = ManagerExchangePlaces.Project(ExchangeTestAccount(), ExchangeTestReady(snapshot), {ExchangeTestDescriptor()}, Array.Empty(Of ManagerExchangeNode)())
        Dim second = ManagerExchangePlaces.Project(ExchangeTestAccount("second"), ExchangeTestReady(snapshot), {ExchangeTestDescriptor()}, Array.Empty(Of ManagerExchangeNode)())
        Dim all = first.Concat(second).ToList()
        Assert.Equal(all.Count, all.Select(Function(n) n.Id).Distinct().Count())
        Assert.Single(all.Where(Function(n) n.Place IsNot Nothing))
    End Sub

    <Fact>
    Public Sub RemovedRootIsRetainedAndCannotAliasNewSameNameObject()
        Dim saved As ResourceDescriptor = ExchangeTestDescriptor()
        Dim first = ManagerExchangePlaces.Project(ExchangeTestAccount(), ExchangeTestReady(ExchangeTestDirectoryOf(ExchangeTestRoot())), {saved}, Array.Empty(Of ManagerExchangeNode)())
        Dim changed = ManagerExchangePlaces.Project(ExchangeTestAccount(), ExchangeTestReady(ExchangeTestDirectoryOf(ExchangeTestRoot("new-id"))), {saved}, first)
        Assert.Equal(ManagerExchangeAvailability.Removed, changed.Single(Function(n) n.ResourceId = "resource").State)
        Assert.Null(changed.Single(Function(n) n.ResourceId = "new-id").Place)
        Dim offline = ManagerExchangePlaces.Project(ExchangeTestAccount(), New ExchangeDevicesState(ExchangeDevicesAvailability.Offline, Nothing, Nothing), {saved}, first)
        Assert.Equal(ManagerExchangeAvailability.Offline, offline.Single(Function(n) n.Place IsNot Nothing).State)
    End Sub

    <Fact>
    Public Sub MissingEntryNeverConnectsToAnotherAccount()
        Dim descriptor As ResourceDescriptor = ManagerExchangeFixture.ExchangeTestDescriptor()
        Dim connections As Integer
        Dim errorValue = Assert.Throws(Of ManagerExchangeException)(Sub() ManagerExchangeAccess.Validate(ManagerPlace.ForResource(descriptor), descriptor,
            CancellationToken.None, Function() {ExchangeTestAccount("another")}, Function(e)
                connections += 1
                Return Nothing
            End Function))
        Assert.Equal(ManagerExchangeAvailability.SignInRequired, errorValue.State)
        Assert.Equal(0, connections)
    End Sub

    <Theory>
    <InlineData(False, "pin-v1", ManagerExchangeAvailability.Removed)>
    <InlineData(True, "pin-v1", ManagerExchangeAvailability.Locked)>
    <InlineData(True, "future", ManagerExchangeAvailability.Unsupported)>
    Public Sub RefusalsKeepTheirNamedState(locked As Boolean, scheme As String, expected As Integer)
        Dim descriptor As ResourceDescriptor = ManagerExchangeFixture.ExchangeTestDescriptor()
        Dim snapshot As ExchangeDirectorySnapshot = If(locked, ExchangeTestDirectoryOf(ExchangeTestRoot(locked:=True, scheme:=scheme)), ExchangeTestDirectoryOf())
        Dim refusal = Assert.Throws(Of ManagerExchangeException)(Sub() ManagerExchangeAccess.ValidateRecord(ManagerPlace.ForResource(descriptor), descriptor, snapshot))
        Assert.Equal(CType(expected, ManagerExchangeAvailability), refusal.State)
    End Sub
End Class
#End If
