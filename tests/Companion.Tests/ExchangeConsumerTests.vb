Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 187 T-3, rules 3-8: the viewer's own account entries and its own session, against ExchangeStubAccount (a TLS stand-in on 127.0.0.1).
''' The library is the same one the Share Manager compiles; these tests need no Share Manager, no agent and no pipe - which is the point.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeConsumerTests
    Implements IDisposable

    Private ReadOnly _tokenRoot As String
    Private ReadOnly _port As New ExchangeDeviceAgentTests.MemoryPort()
    Private ReadOnly _cipher As New ExchangeDeviceAgentTests.XorCipher()

    Public Sub New()
        _tokenRoot = Path.Combine(Path.GetTempPath(), "fms-entries-" & Guid.NewGuid().ToString("N"))
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(_tokenRoot) Then Directory.Delete(_tokenRoot, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Function MakeStore() As ExchangeEntryStore
        Return New ExchangeEntryStore(_port, _tokenRoot, _cipher)
    End Function

    Private Function MakePorts(Optional store As ExchangeEntryStore = Nothing) As ExchangeConsumerPorts
        Return New ExchangeConsumerPorts With {.Entries = If(store, MakeStore()), .MachineName = Function() "OFFICE-PC", .ProductVersion = "26.10.11.1"}
    End Function

    Private Shared Function Request(acc As ExchangeStubAccount, Optional login As String = ExchangeStubAccount.Login,
                                    Optional password As String = ExchangeStubAccount.Password, Optional trusted As String = Nothing) As ExchangeConsumerEnrollRequest
        Return New ExchangeConsumerEnrollRequest With {.Server = acc.Address, .Login = login, .Password = password,
                                                       .TrustedFingerprint = If(trusted, acc.Fingerprint), .ServerName = "Home NAS"}
    End Function

    Private Function AllStoredText() As String
        Dim sb As New System.Text.StringBuilder()
        SyncLock _port.Values
            For Each kv In _port.Values
                sb.Append(kv.Key).Append("="c).Append(kv.Value).Append(vbLf)
            Next
        End SyncLock
        If Directory.Exists(_tokenRoot) Then
            For Each f As String In Directory.GetFiles(_tokenRoot, "*", SearchOption.AllDirectories)
                sb.Append(Convert.ToBase64String(File.ReadAllBytes(f)))
            Next
        End If
        Return sb.ToString()
    End Function

    ' ---- enrolling --------------------------------------------------------------------------------------------

    <Fact>
    Public Async Function Enrolling_stores_the_entry_and_the_token_and_the_password_nowhere() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim req As ExchangeConsumerEnrollRequest = Request(acc)
            Dim result As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(req, MakePorts(store), CancellationToken.None)

            Assert.Equal(ExchangeConsumerEnrollOutcome.Enrolled, result.Outcome)
            Assert.Equal("", req.Password)                                       ' wiped
            Dim entry As ExchangeEntry = result.Entry
            Assert.Equal(1, store.Count())
            Assert.Equal(acc.Address, entry.Server)
            Assert.Equal("alice", entry.Login)
            Assert.Equal(acc.Fingerprint, entry.Pin)
            Assert.Equal(ExchangeCertificateKind.Pinned, entry.Certificate)
            Assert.Equal("Home NAS", entry.Name)
            Assert.Equal("OFFICE-PC (viewer)", entry.DeviceName)                 ' Q187-5
            Assert.False(entry.AutoAttach)                                       ' Q187-4: off by default

            Dim token As String = ""
            Assert.True(store.TokenStoreFor(entry.Id).TryLoad(entry.DeviceId, token))
            Assert.Equal(acc.TokenOf(entry.DeviceId), token)

            ' rule 4: the password is in no value and in no file
            Assert.DoesNotContain(ExchangeStubAccount.Password, AllStoredText())
            Assert.Equal({ExchangeStubAccount.Password}, acc.EnrollPasswordsSeen().ToArray())   ' it crossed the wire once, as the field of `enroll`

            ' rule 5: the one role of this device is the consumer; the device name is the machine and "(viewer)"
            Assert.Equal({"resource-consumer"}, acc.EnrollRolesSeen().Single().Split(","c))
            Assert.Equal({"pin-v1"}, acc.EnrollCapabilitiesSeen().Single().Split(","c))
            Assert.Equal("OFFICE-PC (viewer)", acc.EnrollNamesSeen().Single())
        End Using
    End Function

    <Fact>
    Public Async Function A_leaf_that_is_not_the_strings_fp_is_refused_with_both_fingerprints_nothing_pinned_and_no_password_sent() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim wrong As String = "SHA256:" & New String("A"c, 43)
            Dim result As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc, trusted:=wrong), MakePorts(store), CancellationToken.None)

            Assert.Equal(ExchangeConsumerEnrollOutcome.PinMismatch, result.Outcome)
            Assert.Equal(acc.Fingerprint, result.Fingerprint)                    ' what the server showed: the UI shows both, never pins
            Assert.Empty(acc.EnrollPasswordsSeen())                              ' the password never reached a stream
            Assert.Equal(0, store.Count())
            Assert.False(Directory.Exists(_tokenRoot))
        End Using
    End Function

    <Fact>
    Public Async Function Without_an_accepted_fingerprint_or_platform_trust_nothing_is_sent() As Task
        Using acc As New ExchangeStubAccount()
            Dim req As ExchangeConsumerEnrollRequest = Request(acc, trusted:="")
            Dim result As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(req, MakePorts(), CancellationToken.None)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Untrusted, result.Outcome)
            Assert.Empty(acc.Received())
        End Using
    End Function

    <Fact>
    Public Async Function A_wrong_password_is_a_named_refusal_and_stores_nothing() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim result As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc, password:="not-the-password"), MakePorts(store), CancellationToken.None)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Refused, result.Outcome)
            Assert.Equal(ExchangeRefusal.BadCredentials, result.Refusal)
            Assert.Equal(0, store.Count())
            Assert.DoesNotContain("not-the-password", AllStoredText())
        End Using
    End Function

    <Fact>
    Public Async Function Two_logins_on_one_server_are_two_entries_with_their_own_device_ids_and_tokens() As Task
        Using acc As New ExchangeStubAccount()
            acc.ExtraLogins.Add("bob")
            Dim store As ExchangeEntryStore = MakeStore()
            Dim a As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), MakePorts(store), CancellationToken.None)
            Dim b As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc, login:="bob"), MakePorts(store), CancellationToken.None)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Enrolled, a.Outcome)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Enrolled, b.Outcome)
            Assert.NotEqual(a.Entry.Id, b.Entry.Id)
            Assert.NotEqual(a.Entry.DeviceId, b.Entry.DeviceId)                  ' item G never fires: no two entries share a device id
            Assert.Equal(2, store.Count())
            Dim ta As String = "", tb As String = ""
            Assert.True(store.TokenStoreFor(a.Entry.Id).TryLoad(a.Entry.DeviceId, ta))
            Assert.True(store.TokenStoreFor(b.Entry.Id).TryLoad(b.Entry.DeviceId, tb))
            Assert.NotEqual(ta, tb)
            ' a token is bound to its own device id: the other entry's id opens nothing
            Dim crossed As String = ""
            Assert.False(store.TokenStoreFor(a.Entry.Id).TryLoad(b.Entry.DeviceId, crossed))
        End Using
    End Function

    <Fact>
    Public Async Function Adding_the_same_server_and_login_again_re_enrolls_the_same_entry() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim first As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), MakePorts(store), CancellationToken.None)
            Dim before As String = ""
            Assert.True(store.TokenStoreFor(first.Entry.Id).TryLoad(first.Entry.DeviceId, before))
            ' the same identity, spelled differently: the login in capitals and the host folded
            Dim again As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc, login:="ALICE"), MakePorts(store), CancellationToken.None)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Enrolled, again.Outcome)
            Assert.Equal(first.Entry.Id, again.Entry.Id)
            Assert.Equal(first.Entry.DeviceId, again.Entry.DeviceId)
            Assert.Equal(1, store.Count())
            Dim after As String = ""
            Assert.True(store.TokenStoreFor(again.Entry.Id).TryLoad(again.Entry.DeviceId, after))
            Assert.NotEqual(before, after)                                       ' a rotated token (7.2)
        End Using
    End Function

    <Fact>
    Public Async Function An_id_collision_makes_one_fresh_device_id_and_a_second_is_shown() As Task
        Using acc As New ExchangeStubAccount()
            acc.RefuseNextEnroll("id-collision")
            Dim store As ExchangeEntryStore = MakeStore()
            Dim result As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), MakePorts(store), CancellationToken.None)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Enrolled, result.Outcome)
            Dim ids As List(Of String) = acc.EnrollDeviceIdsSeen()
            Assert.Equal(2, ids.Count)
            Assert.NotEqual(ids(0), ids(1))
            Assert.Equal(ids(1), result.Entry.DeviceId)
        End Using
        Using acc As New ExchangeStubAccount()
            acc.RefuseNextEnroll("id-collision")
            acc.RefuseNextEnroll("id-collision")
            Dim store As ExchangeEntryStore = MakeStore()
            Dim result As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc, login:="carol"), MakePorts(store), CancellationToken.None)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Refused, result.Outcome)
            Assert.Equal(ExchangeRefusal.IdCollision, result.Refusal)
            Assert.Equal(2, acc.EnrollDeviceIdsSeen().Count)                     ' once more, never a loop
        End Using
    End Function

    ' ---- removing ---------------------------------------------------------------------------------------------

    <Fact>
    Public Async Function Removing_an_entry_removes_its_token_and_pin_and_no_other_entrys() As Task
        Using acc As New ExchangeStubAccount()
            acc.ExtraLogins.Add("bob")
            Dim store As ExchangeEntryStore = MakeStore()
            Dim a As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), MakePorts(store), CancellationToken.None)
            Dim b As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc, login:="bob"), MakePorts(store), CancellationToken.None)
            Assert.True(store.Remove(a.Entry.Id))
            Assert.Equal(1, store.Count())
            Assert.Null(store.Find(a.Entry.Id))
            Dim t As String = ""
            Assert.False(store.TokenStoreFor(a.Entry.Id).TryLoad(a.Entry.DeviceId, t))
            Assert.True(store.TokenStoreFor(b.Entry.Id).TryLoad(b.Entry.DeviceId, t))
            Assert.DoesNotContain(acc.Fingerprint & "|alice", AllStoredText())
            Assert.True(store.Remove(b.Entry.Id))
            Assert.Equal("", _port.GetValue(ExchangeEntryStore.ValueName))       ' the last one leaves no value behind
        End Using
    End Function

    ' ---- the session ------------------------------------------------------------------------------------------

    <Fact>
    Public Async Function A_lease_opens_the_control_stream_lists_without_this_device_and_says_bye_when_it_ends() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim ports As ExchangeConsumerPorts = MakePorts(store)
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), ports, CancellationToken.None)
            acc.Devices.Add("{""deviceId"":""" & joined.Entry.DeviceId & """,""deviceName"":""OFFICE-PC (viewer)"",""product"":""fms-windows"",""presence"":""online""}")
            acc.Devices.Add("{""deviceId"":""HOSTPC"",""deviceName"":""Host (Share Manager)"",""product"":""fms-windows"",""presence"":""online""}")
            acc.Resources.Add("{""resourceId"":""R1"",""deviceId"":""HOSTPC"",""kind"":""sftp-share"",""name"":""Photos"",""access"":{""descriptor"":""FMSSFTP2.secret""}}")
            Using hub As New ExchangeEntryHub(ports)
                Dim session As ExchangeEntrySession = hub.SessionFor(joined.Entry)

                ' no lease: nothing is connected and nothing is answered (rule 8)
                Dim idle As ExchangeAgentDirectoryResult = Await session.GetDirectoryAsync(0, 0, CancellationToken.None)
                Assert.False(idle.Reply.IsOk)
                Assert.Equal(0, acc.CountOf("hello"))

                Using session.Lease()
                    Dim dir As ExchangeAgentDirectoryResult = Await session.GetDirectoryAsync(0, 5000, CancellationToken.None)
                    Assert.True(dir.Reply.IsOk)
                    Assert.Equal({"HOSTPC"}, dir.Snapshot.Devices.Select(Function(d) d.DeviceId).ToArray())   ' its own record is hidden (9.1)
                    Assert.Equal("Photos", dir.Snapshot.Resources.Single().Name)
                    Dim state As ExchangeAgentStateResult = Await session.GetStateAsync(CancellationToken.None)
                    Assert.Equal(ExchangeAgentState.Online, state.Info.State)
                    Assert.Equal(joined.Entry.DeviceId, state.Info.DeviceId)
                    Assert.Equal(1, acc.CountOf("hello"))                           ' a hello of its own device, with the entry's token
                End Using

                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("bye") >= 1), "the last lease ends with `bye`")
                Dim after As ExchangeAgentStateResult = Await session.GetStateAsync(CancellationToken.None)
                Assert.NotEqual(ExchangeAgentState.Online, after.Info.State)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Access_is_read_from_the_listed_record_with_a_lease_of_its_own() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), ports, CancellationToken.None)
            acc.Devices.Add("{""deviceId"":""HOSTPC"",""deviceName"":""Host"",""product"":""fms-windows"",""presence"":""online""}")
            acc.Resources.Add("{""resourceId"":""R1"",""deviceId"":""HOSTPC"",""kind"":""sftp-share"",""name"":""Photos"",""access"":{""descriptor"":""FMSSFTP2.secret""}}")
            acc.Resources.Add("{""resourceId"":""R2"",""deviceId"":""HOSTPC"",""kind"":""sftp-share"",""name"":""Offline""}")
            Using hub As New ExchangeEntryHub(ports)
                Dim session As ExchangeEntrySession = hub.SessionFor(joined.Entry)
                Dim ok As ExchangeAgentAccessResult = Await session.GetAccessAsync("R1", CancellationToken.None)
                Assert.True(ok.Reply.IsOk)
                Assert.Contains("FMSSFTP2.secret", ok.AccessJson)
                Dim none As ExchangeAgentAccessResult = Await session.GetAccessAsync("R2", CancellationToken.None)
                Assert.False(none.Reply.IsOk)
                Assert.Equal(ExchangeAgentPipe.ErrorNoAccess, none.Reply.Error)
                Dim unknown As ExchangeAgentAccessResult = Await session.GetAccessAsync("R9", CancellationToken.None)
                Assert.Equal(ExchangeAgentPipe.ErrorUnknownResource, unknown.Reply.Error)
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("bye") >= 3))   ' a connection per question, each closed
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_entry_whose_token_is_gone_says_sign_in_again_and_connects_to_nothing() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim ports As ExchangeConsumerPorts = MakePorts(store)
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), ports, CancellationToken.None)
            store.TokenStoreFor(joined.Entry.Id).Delete()
            acc.RevokeFromServer(joined.Entry.DeviceId)
            Dim before As Integer = acc.Received().Count
            Using hub As New ExchangeEntryHub(ports)
                Dim session As ExchangeEntrySession = hub.SessionFor(joined.Entry)
                Using session.Lease()
                    Dim state As ExchangeAgentStateResult = Await session.GetStateAsync(CancellationToken.None)
                    Assert.Equal(ExchangeAgentState.SignInAgain, state.Info.State)
                    Assert.Equal("no-token", state.Info.LastReason)
                    Assert.True(state.Info.Configured)
                End Using
            End Using
            Assert.Equal(before, acc.Received().Count)                            ' nothing was sent anywhere
            Assert.NotNull(store.Find(joined.Entry.Id))                           ' the entry stays: the user signs in again
        End Using
    End Function

    <Fact>
    Public Async Function A_device_revoked_by_the_server_discards_the_token_and_keeps_the_entry() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim ports As ExchangeConsumerPorts = MakePorts(store)
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), ports, CancellationToken.None)
            acc.RevokeFromServer(joined.Entry.DeviceId)
            acc.PreEnroll(joined.Entry.DeviceId)          ' (the stub keeps its token; revoke it by refusing the hello)
            acc.RefuseNextHello("device-revoked")
            Using hub As New ExchangeEntryHub(ports)
                Dim session As ExchangeEntrySession = hub.SessionFor(joined.Entry)
                Using session.Lease()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function()
                                                                              Dim s As ExchangeAgentStateResult = session.GetStateAsync(CancellationToken.None).Result
                                                                              Return s.Info.State = ExchangeAgentState.SignInAgain
                                                                          End Function))
                End Using
            End Using
            Dim token As String = ""
            Assert.False(store.TokenStoreFor(joined.Entry.Id).TryLoad(joined.Entry.DeviceId, token))
            Assert.NotNull(store.Find(joined.Entry.Id))
        End Using
    End Function

    <Fact>
    Public Sub Dormant_a_machine_with_no_entry_reads_creates_and_connects_to_nothing()
        Dim store As ExchangeEntryStore = MakeStore()
        Assert.Empty(store.Load())
        Assert.Equal(0, store.Count())
        Assert.Null(store.FindByIdentity("203.0.113.5:22022", "alice"))
        Assert.Equal(0, _port.Writes)
        Assert.False(Directory.Exists(_tokenRoot))
        Using hub As New ExchangeEntryHub(MakePorts(store))
            Assert.Null(hub.SessionFor(Nothing))
        End Using
    End Sub

    <Fact>
    Public Sub A_damaged_value_is_no_entries_and_a_hand_edited_element_is_skipped()
        _port.SetValue(ExchangeEntryStore.ValueName, "not json")
        Assert.Empty(MakeStore().Load())
        _port.SetValue(ExchangeEntryStore.ValueName, "[{""id"":""x""},{""id"":""0123456789abcdef0123456789abcdef"",""x"":""203.0.113.5:22022"",""login"":""alice"",""deviceId"":""bad""}]")
        Assert.Empty(MakeStore().Load())
    End Sub

End Class
