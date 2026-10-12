#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 171 rules 2-5 and 8: the viewer's use of the agent's local channel, against a FAKE channel and an in-memory registry. The feed says what the
''' agent is without drawing anything when the features are not offered; attaching asks for `access` of ONE resource, commits it through the same call the
''' registry uses (the password beside it, never in it); following the producer replaces a changed access once and asks for nothing when the agent is down;
''' a failed or partial directory changes no state and deletes nothing.
''' </summary>
Public Class ExchangeSyncTests

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const Rid As String = "EBESExQVFhcYGRobHB0eHw"
    Private Const Did As String = "AAECAwQFBgcICQoLDA0ODw"
    Private Const Own As String = "ICEiIyQlJicoKSorLC0uLw"
    Private Const Password As String = "k7PmQ2wXr9TzS4vGnHb3JdLe"

    Friend NotInheritable Class FakeApi
        Implements IExchangeAgentApi

        Public StateReply As ExchangeAgentReply = New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", "")
        Public Info As ExchangeAgentStateInfo = New ExchangeAgentStateInfo(True, ExchangeAgentState.Online, {"directory", "tunnel"}, Own, "This PC", "")
        Public DirectoryReply As ExchangeAgentReply = New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", "")
        Public Snapshot As ExchangeDirectorySnapshot
        Public Access As New Dictionary(Of String, ExchangeAgentAccessResult)(StringComparer.Ordinal)
        Public StateCalls As Integer
        Public DirectoryCalls As Integer
        Public SinceSeen As New List(Of Long)
        Public AccessAsked As New List(Of String)

        Public Function GetStateAsync(ct As CancellationToken) As Task(Of ExchangeAgentStateResult) Implements IExchangeAgentApi.GetStateAsync
            Interlocked.Increment(StateCalls)
            Return Task.FromResult(New ExchangeAgentStateResult(StateReply, If(StateReply.IsOk, Info, Nothing)))
        End Function

        Public Function GetDirectoryAsync(sinceRevision As Long, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDirectoryResult) Implements IExchangeAgentApi.GetDirectoryAsync
            Interlocked.Increment(DirectoryCalls)
            SyncLock SinceSeen
                SinceSeen.Add(sinceRevision)
            End SyncLock
            Return Task.FromResult(New ExchangeAgentDirectoryResult(DirectoryReply, If(DirectoryReply.IsOk, Snapshot, Nothing)))
        End Function

        Public Function GetAccessAsync(resourceId As String, ct As CancellationToken) As Task(Of ExchangeAgentAccessResult) Implements IExchangeAgentApi.GetAccessAsync
            SyncLock AccessAsked
                AccessAsked.Add(resourceId)
            End SyncLock
            Dim r As ExchangeAgentAccessResult = Nothing
            If Access.TryGetValue(resourceId, r) Then Return Task.FromResult(r)
            Return Task.FromResult(New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Refused, ExchangeAgentPipe.ErrorUnknownResource, ""), ""))
        End Function
    End Class

    Private Shared Function AccessResult(Optional host As String = "192.168.1.23", Optional password As String = Password, Optional fp As String = Fp) As ExchangeAgentAccessResult
        Dim json As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(New FmsSftpCode({host}, 2022, "fms", password, fp, "", "", False), "/Camera", json))
        Return New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", json), json)
    End Function

    Private Shared Function NoAccess() As ExchangeAgentAccessResult
        Return New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Refused, ExchangeAgentPipe.ErrorNoAccess, ""), "")
    End Function

    Private Shared Function Dir(Optional online As Boolean = True, Optional resourceKind As String = "sftp-share", Optional revision As Long = 4) As ExchangeDirectorySnapshot
        Dim devices As New List(Of ExchangeDeviceRecord) From {New ExchangeDeviceRecord("{}", Did, "Home PC", "fms-windows", "", "", New List(Of String), If(online, "online", "offline"), "")}
        Dim resources As New List(Of ExchangeResourceRecord) From {New ExchangeResourceRecord("{}", Rid, Did, resourceKind, "Camera", If(online, "online", "offline"), "")}
        Return New ExchangeDirectorySnapshot(revision, devices, resources, New List(Of ExchangeBroadcastRecord))
    End Function

    ''' <summary>An in-memory registry with the sync's two registry calls, counting commits and keeping the passwords beside, never in, the descriptors.</summary>
    Friend NotInheritable Class Registry
        Public ReadOnly Items As New List(Of ResourceDescriptor)
        Public ReadOnly Passwords As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public Commits As Integer
        Public FailCommit As Boolean
        Private _seq As Integer

        Public Function Load() As List(Of ResourceDescriptor)
            Return Items.Select(Function(d) System.Text.Json.JsonSerializer.Deserialize(Of ResourceDescriptor)(System.Text.Json.JsonSerializer.Serialize(d))).ToList()
        End Function

        Public Function Commit(d As ResourceDescriptor, pw As String) As ResourceCommitResult
            If FailCommit Then Return New ResourceCommitResult(ResourceCommitOutcome.RegistryFailed)
            Commits += 1
            Dim at As Integer = Items.FindIndex(Function(x) String.Equals(x.Id, d.Id, StringComparison.OrdinalIgnoreCase))
            If at >= 0 Then Items(at) = d Else Items.Add(d)
            If pw IsNot Nothing Then Passwords(d.Id) = pw
            Return New ResourceCommitResult(ResourceCommitOutcome.Committed, "cred:" & d.Id)
        End Function

        Public Function ReadPassword(d As ResourceDescriptor) As String
            Dim v As String = Nothing
            Return If(Passwords.TryGetValue(d.Id, v), v, "")
        End Function

        Public Function NewId() As String
            _seq += 1
            Return "res:t" & _seq.ToString()
        End Function

        Public Function Sync(api As IExchangeAgentApi) As ExchangeSync
            Return New ExchangeSync(api, AddressOf Load, AddressOf Commit, AddressOf ReadPassword, AddressOf NewId)
        End Function
    End Class

    Private Shared Function Record(Optional kind As String = "sftp-share") As ExchangeResourceRecord
        Return New ExchangeResourceRecord("{}", Rid, Did, kind, "Camera", "online", "")
    End Function

    ' --- attach ---

    <Theory>
    <InlineData("online", True, "pin-v1", "SignInAgain")>
    <InlineData("offline", True, "pin-v1", "NoAccess")>
    <InlineData("online", False, "", "NoAccess")>
    <InlineData("online", True, "future", "Unusable")>
    Public Async Function Missing_access_distinguishes_an_older_pin_enrollment_from_an_offline_producer(presence As String, isProtected As Boolean,
                                                                                                     scheme As String, expected As String) As Task
        Dim api As New FakeApi()
        api.Access(Rid) = NoAccess()
        Dim reg As New Registry()
        Dim record As New ExchangeResourceRecord("{}", Rid, Did, "sftp-share", "Downloads", presence, "", isProtected, scheme)
        Dim outcome As ExchangeAttachOutcome = Await reg.Sync(api).AttachAsync(record, "W25", CancellationToken.None, "1234")
        Assert.Equal(expected, outcome.ToString())
        Assert.Equal(0, reg.Commits)
        If expected = "SignInAgain" OrElse expected = "Unusable" Then Assert.Empty(api.AccessAsked)
    End Function

    Private NotInheritable Class RefusingDialer
        Implements IEndpointDialer

        Friend Failure As ResourceFailure
        Friend Calls As Integer

        Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
            Calls += 1
            Return Task.FromResult(DialResult.Failed(Failure))
        End Function
    End Class

    Private NotInheritable Class UnavailableTunnel
        Implements IExchangeTunnelSource

        Friend Calls As Integer

        Public Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeTunnelOpen) Implements IExchangeTunnelSource.OpenAsync
            Calls += 1
            Return Task.FromResult(ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Unavailable))
        End Function
    End Class

    <Fact>
    Public Async Function Protected_attachment_tries_the_account_tunnel_when_direct_access_fails() As Task
        Dim api As New FakeApi()
        api.Access(Rid) = AccessResult()
        Dim reg As New Registry()
        Dim dialer As New RefusingDialer With {.Failure = New ResourceFailure(ResourceFailureKind.Transport)}
        Dim tunnel As New UnavailableTunnel()
        Dim sourceResource As ResourceDescriptor = Nothing
        Dim sync As New ExchangeSync(api, AddressOf reg.Load, AddressOf reg.Commit, AddressOf reg.ReadPassword, AddressOf reg.NewId,
            "entry:test", dialer, Function(d)
                                      sourceResource = d
                                      Return tunnel
                                  End Function)
        Dim record As New ExchangeResourceRecord("{}", Rid, Did, "sftp-share", "Camera", "online", api.Access(Rid).AccessJson, True, "pin-v1")
        Dim failure As ResourceFailure = Nothing
        Dim outcome As ExchangeAttachOutcome = Await sync.AttachAsync(record, "Home PC", CancellationToken.None, "1234", Sub(f) failure = f)
        Assert.Equal(ExchangeAttachOutcome.ConnectionFailed, outcome)
        Assert.Equal(1, tunnel.Calls)
        Assert.True(dialer.Calls > 0)
        Assert.Equal("entry:test", sourceResource.ExchangeEntryId)
        Assert.Equal(Rid, sourceResource.ExchangeResourceId)
        Assert.True(sourceResource.PinRequired)
        Assert.Equal(ResourceFailureKind.Transport, failure.Kind)
        Assert.Equal(0, reg.Commits)
        Assert.Null(ResourcePinMemory.GetPin(sourceResource))
    End Function

    <Theory>
    <InlineData(ResourceFailureKind.Auth)>
    <InlineData(ResourceFailureKind.Timeout)>
    <InlineData(ResourceFailureKind.KeyMismatch)>
    Public Async Function Protected_attachment_reports_the_connection_failure_without_claiming_offline(kindValue As Integer) As Task
        Dim kind As ResourceFailureKind = CType(kindValue, ResourceFailureKind)
        Dim api As New FakeApi()
        api.Access(Rid) = AccessResult()
        Dim reg As New Registry()
        Dim dialer As New RefusingDialer With {.Failure = New ResourceFailure(kind)}
        Dim sync As New ExchangeSync(api, AddressOf reg.Load, AddressOf reg.Commit, AddressOf reg.ReadPassword, AddressOf reg.NewId,
            dialer:=dialer, tunnelSource:=Function(d) Nothing)
        Dim record As New ExchangeResourceRecord("{}", Rid, Did, "sftp-share", "Camera", "online", api.Access(Rid).AccessJson, True, "pin-v1")
        Dim failure As ResourceFailure = Nothing
        Dim outcome As ExchangeAttachOutcome = Await sync.AttachAsync(record, "Home PC", CancellationToken.None, "1234", Sub(f) failure = f)
        Assert.Equal(ExchangeAttachOutcome.ConnectionFailed, outcome)
        Assert.Equal(kind, failure.Kind)
        Assert.Equal(0, reg.Commits)
    End Function

    <Fact>
    Public Async Function Ticking_a_resource_asks_the_agent_for_its_access_and_stores_it_with_the_password_beside() As Task
        Dim api As New FakeApi()
        api.Access(Rid) = AccessResult()
        Dim reg As New Registry()
        Dim outcome As ExchangeAttachOutcome = Await reg.Sync(api).AttachAsync(Record(), "Home PC", CancellationToken.None)
        Assert.Equal(ExchangeAttachOutcome.Attached, outcome)
        Assert.Equal({Rid}, api.AccessAsked.ToArray())                         ' ONE resource's access, the only place a secret crosses
        Dim stored As ResourceDescriptor = reg.Items.Single()
        Assert.Equal(ResourceOrigin.Exchange, stored.Origin)
        Assert.Equal(Rid, stored.ExchangeResourceId)
        Assert.Equal(Password, reg.Passwords(stored.Id))
        Assert.DoesNotContain(Password, ResourceRegistry.Serialize(reg.Items), StringComparison.Ordinal)
    End Function

    <Fact>
    Public Async Function Ticking_twice_attaches_once() As Task
        Dim api As New FakeApi()
        api.Access(Rid) = AccessResult()
        Dim reg As New Registry()
        Assert.Equal(ExchangeAttachOutcome.Attached, Await reg.Sync(api).AttachAsync(Record(), "Home PC", CancellationToken.None))
        Assert.Equal(ExchangeAttachOutcome.AlreadyAttached, Await reg.Sync(api).AttachAsync(Record(), "Home PC", CancellationToken.None))
        Assert.Single(reg.Items)
        Assert.Single(api.AccessAsked)
    End Function

    <Fact>
    Public Async Function An_offline_producers_resource_cannot_be_attached_and_an_unknown_kind_is_not_even_asked_for() As Task
        Dim api As New FakeApi()
        api.Access(Rid) = NoAccess()
        Dim reg As New Registry()
        Assert.Equal(ExchangeAttachOutcome.NoAccess, Await reg.Sync(api).AttachAsync(Record(), "Home PC", CancellationToken.None))
        Assert.Equal(ExchangeAttachOutcome.Unusable, Await reg.Sync(api).AttachAsync(Record("webdav-share"), "Home PC", CancellationToken.None))
        Assert.Equal(1, api.AccessAsked.Count)
        Assert.Empty(reg.Items)
    End Function

    <Fact>
    Public Async Function A_missing_agent_or_a_failing_registry_stores_nothing() As Task
        Dim api As New FakeApi()
        api.Access(Rid) = New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", ""), "")
        Dim reg As New Registry()
        Assert.Equal(ExchangeAttachOutcome.AgentUnavailable, Await reg.Sync(api).AttachAsync(Record(), "Home PC", CancellationToken.None))

        api.Access(Rid) = AccessResult()
        reg.FailCommit = True
        Assert.Equal(ExchangeAttachOutcome.NotStored, Await reg.Sync(api).AttachAsync(Record(), "Home PC", CancellationToken.None))
        Assert.Empty(reg.Items)
    End Function

    ' --- follow ---

    Private Shared Async Function AttachedAsync(reg As Registry, api As FakeApi) As Task(Of ResourceDescriptor)
        api.Access(Rid) = AccessResult()
        Assert.Equal(ExchangeAttachOutcome.Attached, Await reg.Sync(api).AttachAsync(Record(), "Home PC", CancellationToken.None))
        Return reg.Items.Single()
    End Function

    <Fact>
    Public Async Function A_changed_access_replaces_the_kept_one_with_a_notice_and_a_new_password() As Task
        Dim api As New FakeApi()
        Dim reg As New Registry()
        Dim attached As ResourceDescriptor = Await AttachedAsync(reg, api)
        api.Access(Rid) = AccessResult(host:="192.168.1.77", password:="Nn9PmQ2wXr9TzS4vGnHb3Jd1")
        Dim outcome As ExchangeFollowOutcome = Await reg.Sync(api).FollowOneAsync(attached, "Home PC", CancellationToken.None)
        Assert.True(outcome.Changed)
        Assert.Equal("192.168.1.77", reg.Items.Single().Host)
        Assert.True(reg.Items.Single().ExchangeNotice.GetValueOrDefault())
        Assert.Equal("Nn9PmQ2wXr9TzS4vGnHb3Jd1", reg.Passwords(attached.Id))
        Assert.Equal(attached.Id, reg.Items.Single().Id)
        Assert.Equal(2, reg.Commits)
    End Function

    <Fact>
    Public Async Function An_unchanged_access_commits_nothing() As Task
        Dim api As New FakeApi()
        Dim reg As New Registry()
        Dim attached As ResourceDescriptor = Await AttachedAsync(reg, api)
        Dim outcome As ExchangeFollowOutcome = Await reg.Sync(api).FollowOneAsync(attached, "Home PC", CancellationToken.None)
        Assert.False(outcome.Changed)
        Assert.Equal(1, reg.Commits)
    End Function

    <Fact>
    Public Async Function Without_the_agent_or_without_access_the_kept_access_stays_and_nothing_is_committed() As Task
        Dim api As New FakeApi()
        Dim reg As New Registry()
        Dim attached As ResourceDescriptor = Await AttachedAsync(reg, api)
        api.Access(Rid) = New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", ""), "")
        Assert.Same(attached, (Await reg.Sync(api).FollowOneAsync(attached, "Home PC", CancellationToken.None)).Descriptor)      ' rule 8: it keeps opening by its direct addresses
        api.Access(Rid) = NoAccess()
        Assert.Same(attached, (Await reg.Sync(api).FollowOneAsync(attached, "Home PC", CancellationToken.None)).Descriptor)
        Assert.Equal(1, reg.Commits)
        Assert.Equal("192.168.1.23", reg.Items.Single().Host)
    End Function

    <Fact>
    Public Async Function Following_asks_only_about_available_resources_of_the_exchange_and_never_deletes() As Task
        Dim api As New FakeApi()
        Dim reg As New Registry()
        Dim attached As ResourceDescriptor = Await AttachedAsync(reg, api)
        Dim manual As New ResourceDescriptor With {.Id = "res:manual", .Kind = ResourceKind.Sftp, .Host = "10.0.0.5", .Port = 22, .UserName = "me", .RootPath = "/", .DisplayName = "NAS"}
        reg.Items.Add(manual)
        api.AccessAsked.Clear()
        Dim count As Integer = reg.Items.Count

        ' the producer is offline: nothing is asked
        Assert.Empty(Await reg.Sync(api).FollowAllAsync(New ExchangeDevicesState(ExchangeDevicesAvailability.Ready, api.Info, Dir(online:=False)), CancellationToken.None))
        ' no full list at all: nothing is known, so nothing changes
        Assert.Empty(Await reg.Sync(api).FollowAllAsync(New ExchangeDevicesState(ExchangeDevicesAvailability.Offline, api.Info, Nothing), CancellationToken.None))
        Assert.Empty(Await reg.Sync(api).FollowAllAsync(Nothing, CancellationToken.None))
        ' the resource is gone from a FULL list: it is not asked for, and it is not removed
        Dim gone As New ExchangeDirectorySnapshot(9, New List(Of ExchangeDeviceRecord), New List(Of ExchangeResourceRecord), New List(Of ExchangeBroadcastRecord))
        Assert.Empty(Await reg.Sync(api).FollowAllAsync(New ExchangeDevicesState(ExchangeDevicesAvailability.Ready, api.Info, gone), CancellationToken.None))
        Assert.Empty(api.AccessAsked)
        Assert.Equal(count, reg.Items.Count)
        Assert.Contains(reg.Items, Function(r) r.Id = attached.Id)

        ' the producer is online with a changed access: only that resource is asked and replaced; the manual one is never read
        api.Access(Rid) = AccessResult(host:="192.168.1.88")
        Dim changed As List(Of String) = Await reg.Sync(api).FollowAllAsync(New ExchangeDevicesState(ExchangeDevicesAvailability.Ready, api.Info, Dir(online:=True)), CancellationToken.None)
        Assert.Equal({attached.Id}, changed.ToArray())
        Assert.Equal({Rid}, api.AccessAsked.ToArray())
        Assert.Equal("10.0.0.5", reg.Items.Single(Function(r) r.Id = "res:manual").Host)
    End Function

    ' --- the feed ---

    Private Shared Function Feed(api As FakeApi, Optional enabled As Boolean = True, Optional installed As Boolean = True) As ExchangeDevicesFeed
        Return New ExchangeDevicesFeed(api, Function() enabled, Function() installed) With {.PollWaitMs = 10, .RetryMs = 10}
    End Function

    <Fact>
    Public Async Function Without_the_gate_or_without_the_Share_Manager_nothing_is_drawn_and_the_channel_is_never_touched() As Task
        Dim api As New FakeApi()
        Assert.Equal(ExchangeDevicesAvailability.Hidden, (Await Feed(api, enabled:=False).RefreshAsync(CancellationToken.None)).Availability)
        Assert.Equal(ExchangeDevicesAvailability.Hidden, (Await Feed(api, installed:=False).RefreshAsync(CancellationToken.None)).Availability)
        Assert.Equal(0, api.StateCalls)
        Assert.Equal(0, api.DirectoryCalls)
        Assert.False(Feed(api, enabled:=False).IsOffered())
    End Function

    <Fact>
    Public Async Function The_feed_says_what_the_agent_is() As Task
        Dim api As New FakeApi()
        api.StateReply = New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", "")
        Assert.Equal(ExchangeDevicesAvailability.NotRunning, (Await Feed(api).RefreshAsync(CancellationToken.None)).Availability)

        api.StateReply = New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", "")
        api.Info = New ExchangeAgentStateInfo(False, ExchangeAgentState.NotConfigured, Array.Empty(Of String)(), "", "", "")
        Assert.Equal(ExchangeDevicesAvailability.NotSignedIn, (Await Feed(api).RefreshAsync(CancellationToken.None)).Availability)

        api.Info = New ExchangeAgentStateInfo(True, ExchangeAgentState.Reconnecting, Array.Empty(Of String)(), Own, "This PC", "lost")
        Dim offline As ExchangeDevicesState = Await Feed(api).RefreshAsync(CancellationToken.None)
        Assert.Equal(ExchangeDevicesAvailability.Offline, offline.Availability)
        Assert.Null(offline.Snapshot)
        Assert.False(offline.FullList)

        api.Info = New ExchangeAgentStateInfo(True, ExchangeAgentState.Online, {"directory"}, Own, "This PC", "")
        api.Snapshot = Dir()
        Dim ready As ExchangeDevicesState = Await Feed(api).RefreshAsync(CancellationToken.None)
        Assert.Equal(ExchangeDevicesAvailability.Ready, ready.Availability)
        Assert.True(ready.FullList)
        Assert.Equal(Own, ready.OwnDeviceId)
    End Function

    <Fact>
    Public Async Function A_directory_that_could_not_be_fetched_is_no_snapshot_so_no_state_is_drawn_from_it() As Task
        Dim api As New FakeApi()
        api.Snapshot = Dir()
        Dim f As ExchangeDevicesFeed = Feed(api)
        Assert.True((Await f.RefreshAsync(CancellationToken.None)).FullList)
        api.DirectoryReply = New ExchangeAgentReply(ExchangeAgentReplyKind.TimedOut, "", "")
        Dim failed As ExchangeDevicesState = Await f.RefreshAsync(CancellationToken.None)
        Assert.Equal(ExchangeDevicesAvailability.Offline, failed.Availability)
        Assert.Null(failed.Snapshot)                     ' the old snapshot is not carried over as if it were fresh
        Dim item As ResourceDescriptor = ExchangeAttach.BuildDraft(Rid, Did, "d", "Camera", AccessResult().AccessJson, Function() "res:x").Descriptor
        Assert.Equal(ExchangeResourceState.Unknown, ExchangeAttach.StateOf(item.ExchangeResourceId, failed.Snapshot, failed.FullList))
    End Function

    <Fact>
    Public Async Function The_loop_long_polls_with_the_last_revision_raises_Changed_and_stops_when_cancelled() As Task
        Dim api As New FakeApi()
        api.Snapshot = Dir(revision:=7)
        Dim f As ExchangeDevicesFeed = Feed(api)
        Dim changes As Integer
        AddHandler f.Changed, Sub() Interlocked.Increment(changes)
        Using cts As New CancellationTokenSource()
            Dim run As Task = f.RunAsync(cts.Token)
            Assert.True(Await Waiter(Function() api.DirectoryCalls >= 3))
            cts.Cancel()
            Await run.WaitAsync(TimeSpan.FromSeconds(10))
        End Using
        SyncLock api.SinceSeen
            Assert.Equal(0L, api.SinceSeen(0))
            Assert.Equal(7L, api.SinceSeen(2))           ' the revision of the last snapshot rides on the next request
        End SyncLock
        Assert.Equal(1, changes)                          ' the same snapshot again is not a change
        Dim calls As Integer = api.DirectoryCalls
        Await Task.Delay(100)
        Assert.Equal(calls, api.DirectoryCalls)           ' stopped: no polling goes on while nobody looks
    End Function

    Private Shared Async Function Waiter(condition As Func(Of Boolean)) As Task(Of Boolean)
        Dim until As DateTime = DateTime.UtcNow.AddSeconds(10)
        While DateTime.UtcNow < until
            If condition() Then Return True
            Await Task.Delay(10)
        End While
        Return condition()
    End Function

    <Fact>
    Public Sub The_source_reads_no_secret_and_the_local_json_carries_no_access()
        ' the snapshot the viewer gets over the channel carries neither `access` nor `shareId`; `access` is asked for one resource at a time
        Dim snap As ExchangeDirectorySnapshot = Dir()
        Dim json As String = snap.ToLocalJson()
        Assert.DoesNotContain("access", json, StringComparison.Ordinal)
        Assert.DoesNotContain("shareId", json, StringComparison.Ordinal)
    End Sub

End Class
#End If
