Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The viewer's per-entry session holds a device key (DEVICE-EXCHANGE 27 item DF), so what a producer seals to it opens; and a device that
''' enrolled before it had a key - an entry or the Share Manager's account - presents its first one by itself, with nobody asked anything.
''' Against ExchangeStubAccount (a TLS stand-in on 127.0.0.1).
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeEntryKeyTests
    Implements IDisposable

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

    Private ReadOnly _tokenRoot As String = Path.Combine(Path.GetTempPath(), "fms-entry-keys-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly _port As New ExchangeDeviceAgentTests.MemoryPort()
    Private ReadOnly _cipher As New ExchangeDeviceAgentTests.XorCipher()
    Private ReadOnly _log As New ConcurrentQueue(Of String)

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(_tokenRoot) Then Directory.Delete(_tokenRoot, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Function MakeStore() As ExchangeEntryStore
        Return New ExchangeEntryStore(_port, _tokenRoot, _cipher)
    End Function

    Private Function MakePorts(store As ExchangeEntryStore) As ExchangeConsumerPorts
        Return New ExchangeConsumerPorts With {.Entries = store, .MachineName = Function() "OFFICE-PC", .ProductVersion = "26.10.11.1", .Log = Sub(line) _log.Enqueue(line)}
    End Function

    Private Shared Function Request(acc As ExchangeStubAccount, Optional password As String = ExchangeStubAccount.Password) As ExchangeConsumerEnrollRequest
        Return New ExchangeConsumerEnrollRequest With {.Server = acc.Address, .Login = ExchangeStubAccount.Login, .Password = password,
                                                       .TrustedFingerprint = acc.Fingerprint, .ServerName = "Home NAS"}
    End Function

    ''' <summary>An entry as a viewer before spec 234 left it: an entry and a token, no key anywhere, and a server that lists it without one.</summary>
    Private Function LegacyEntry(acc As ExchangeStubAccount, store As ExchangeEntryStore) As ExchangeEntry
        Dim deviceId As String = ExchangeWireTestKit.IdOf("D"c)
        Dim token As String = acc.PreEnrollKeyless(deviceId, "OLD-NAME (viewer)")
        Dim entry As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), acc.Address, ExchangeStubAccount.Login, acc.Fingerprint, ExchangeCertificateKind.Pinned,
                                       deviceId, "OFFICE-PC (viewer)", "Home NAS", False)
        Assert.True(store.TokenStoreFor(entry.Id).Save(deviceId, token))
        Assert.True(store.Upsert(entry))
        Return entry
    End Function

    Private Shared Function PhoneRecord(phoneId As String, phoneKey As ExchangeDeviceKey) As String
        Return "{""deviceId"":""" & phoneId & """,""deviceName"":""Phone"",""product"":""fms-android"",""presence"":""online"",""capabilities"":[""e2e-v1""],""e2eKey"":{""v"":1,""kem"":""" & phoneKey.Kem & """}}"
    End Function

    Private Shared Function ClearAccess() As String
        Dim access As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "top-secret", Fp, "", "", False), "/Photos", access))
        Return access
    End Function

    ''' <summary>A folder of the phone as a server lists it: online, with a box for this device, or with none.</summary>
    Private Shared Function PhoneFolder(resourceId As String, phoneId As String, phoneKey As ExchangeDeviceKey, toDeviceId As String, toKem As String) As String
        Dim box As String = ""
        If toDeviceId.Length > 0 Then
            Dim recipients As New List(Of ExchangeSealedRecipient) From {New ExchangeSealedRecipient(toDeviceId, toKem)}
            Assert.True(ExchangeSealedAccess.SealAccess(ClearAccess(), resourceId, phoneId, phoneKey, recipients, box))
        End If
        Dim json As String = Nothing
        Assert.True(ExchangeResourceRecordBuilder.TryBuild(resourceId, phoneId, "Photos", "", "", Nothing, DateTime.UtcNow, json, False, box, Fp, "/Photos"))
        Dim node As System.Text.Json.Nodes.JsonNode = System.Text.Json.Nodes.JsonNode.Parse(json)
        node("presence") = "online"
        Return node.ToJsonString()
    End Function

    Private Shared Function KemOf(e2eKeyJson As String) As String
        Using doc As JsonDocument = JsonDocument.Parse(e2eKeyJson)
            Return doc.RootElement.GetProperty("kem").GetString()
        End Using
    End Function

    Private Function StoredToken(store As ExchangeEntryStore, entry As ExchangeEntry) As String
        Dim token As String = ""
        Return If(store.TokenStoreFor(entry.Id).TryLoad(entry.DeviceId, token), token, "")
    End Function

    Private Shared Function StateOf(session As ExchangeEntrySession) As ExchangeAgentState
        Return session.GetStateAsync(CancellationToken.None).GetAwaiter().GetResult().Info.State
    End Function

    Private Function Logged() As String
        Return String.Join(vbLf, _log)
    End Function

    Private Shared Function SealedFeatures() As String()
        Return {"directory", "tunnel", "pairing", ExchangeSealedNames.FeatureSealed}
    End Function

    ' ---- a new entry ------------------------------------------------------------------------------------------

    <Fact>
    Public Async Function A_new_entry_keeps_a_key_first_and_presents_it_in_its_enroll() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), MakePorts(store), CancellationToken.None)

            Assert.Equal(ExchangeConsumerEnrollOutcome.Enrolled, joined.Outcome)
            Using kept As ExchangeDeviceKey = store.DeviceKeysFor(joined.Entry.Id).TryLoad(joined.Entry.DeviceId)
                Assert.NotNull(kept)
                Assert.Equal(kept.Kem, KemOf(acc.EnrollE2eKeysSeen().Single()))
            End Using
            Assert.Contains(ExchangeSealedNames.CapabilityE2e, acc.EnrollCapabilitiesSeen().Single().Split(","c))
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_first_enrollment_leaves_no_key_and_no_folder() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc, "wrong"), MakePorts(store), CancellationToken.None)

            Assert.NotEqual(ExchangeConsumerEnrollOutcome.Enrolled, joined.Outcome)
            Assert.Equal(0, store.Count())
            Assert.False(Directory.Exists(_tokenRoot))
        End Using
    End Function

    <Fact>
    Public Async Function Removing_an_entry_takes_its_key_and_its_kept_keys_along() As Task
        Using acc As New ExchangeStubAccount()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), MakePorts(store), CancellationToken.None)
            Assert.True(store.DeviceKeysFor(joined.Entry.Id).Exists(joined.Entry.DeviceId))

            Assert.True(store.Remove(joined.Entry.Id))

            Assert.False(store.DeviceKeysFor(joined.Entry.Id).Exists(joined.Entry.DeviceId))
            Assert.False(Directory.Exists(Path.Combine(_tokenRoot, joined.Entry.Id)))
        End Using
    End Function

    ' ---- what is sealed to it opens -------------------------------------------------------------------------

    <Fact>
    Public Async Function A_box_sealed_to_the_entry_opens_through_its_session() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            Dim store As ExchangeEntryStore = MakeStore()
            Dim ports As ExchangeConsumerPorts = MakePorts(store)
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), ports, CancellationToken.None)
            Using phoneKey As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Dim phoneId As String = ExchangeWireTestKit.IdOf("P"c)
                Dim folderId As String = ExchangeWireTestKit.IdOf("1"c)
                acc.Devices.Add(PhoneRecord(phoneId, phoneKey))
                acc.Resources.Add(PhoneFolder(folderId, phoneId, phoneKey, joined.Entry.DeviceId, KemOf(acc.EnrollE2eKeysSeen().Single())))
                Using hub As New ExchangeEntryHub(ports)
                    Dim opened As ExchangeAgentAccessResult = Await hub.SessionFor(joined.Entry).GetAccessAsync(folderId, CancellationToken.None)

                    Assert.True(opened.Reply.IsOk, opened.Reply.Error)
                    Assert.Contains("top-secret", opened.AccessJson)
                End Using
            End Using
            Assert.DoesNotContain("top-secret", Logged())
        End Using
    End Function

    <Fact>
    Public Async Function A_producer_that_does_not_seal_ends_in_the_typed_state_when_the_wait_is_over() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            Dim ports As ExchangeConsumerPorts = MakePorts(MakeStore())
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), ports, CancellationToken.None)
            Dim hostId As String = ExchangeWireTestKit.IdOf("H"c)
            Dim folderId As String = ExchangeWireTestKit.IdOf("1"c)
            acc.Devices.Add("{""deviceId"":""" & hostId & """,""deviceName"":""Host"",""product"":""fms-windows"",""presence"":""online""}")
            acc.Resources.Add("{""resourceId"":""" & folderId & """,""deviceId"":""" & hostId & """,""kind"":""sftp-share"",""name"":""Photos"",""presence"":""online"",""access"":{""descriptor"":""FMSSFTP2.secret""}}")
            Using hub As New ExchangeEntryHub(ports)
                Dim session As ExchangeEntrySession = hub.SessionFor(joined.Entry)
                session.AccessWaitMs = 400
                Dim watch As Stopwatch = Stopwatch.StartNew()
                Dim result As ExchangeAgentAccessResult = Await session.GetAccessAsync(folderId, CancellationToken.None)

                Assert.False(result.Reply.IsOk)
                Assert.Equal(ExchangeAgentPipe.ErrorE2eMissing, result.Reply.Error)
                Assert.Equal("", result.AccessJson)
                Assert.True(watch.ElapsedMilliseconds >= 300, "the wait gives a producer its moment")
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_damaged_box_is_answered_at_once() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            Dim ports As ExchangeConsumerPorts = MakePorts(MakeStore())
            Dim joined As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(Request(acc), ports, CancellationToken.None)
            Using phoneKey As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Dim phoneId As String = ExchangeWireTestKit.IdOf("P"c)
                Dim folderId As String = ExchangeWireTestKit.IdOf("1"c)
                acc.Devices.Add(PhoneRecord(phoneId, phoneKey))
                acc.Resources.Add("{""resourceId"":""" & folderId & """,""deviceId"":""" & phoneId & """,""kind"":""sftp-share"",""name"":""Photos"",""presence"":""online"",""sealedAccess"":{""v"":1}}")
                Using hub As New ExchangeEntryHub(ports)
                    Dim watch As Stopwatch = Stopwatch.StartNew()
                    Dim result As ExchangeAgentAccessResult = Await hub.SessionFor(joined.Entry).GetAccessAsync(folderId, CancellationToken.None)

                    Assert.Equal(ExchangeAgentPipe.ErrorSealedFailed, result.Reply.Error)
                    Assert.True(watch.ElapsedMilliseconds < ExchangeEntrySession.AccessOpenWaitMs / 2, "nothing mends a damaged box")
                End Using
            End Using
        End Using
    End Function

    ' ---- an entry from before it had a key ------------------------------------------------------------------

    <Fact>
    Public Async Function An_entry_without_a_key_presents_one_with_a_code_and_keeps_its_id_and_its_stream() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            Dim store As ExchangeEntryStore = MakeStore()
            Dim entry As ExchangeEntry = LegacyEntry(acc, store)
            Dim oldToken As String = acc.TokenOf(entry.DeviceId)
            Using hub As New ExchangeEntryHub(MakePorts(store))
                Dim session As ExchangeEntrySession = hub.SessionFor(entry)
                Using lease As IDisposable = session.Lease()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.EnrollDeviceIdsSeen().Count = 1))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.TokenOf(entry.DeviceId) <> oldToken AndAlso StoredToken(store, entry) = acc.TokenOf(entry.DeviceId)))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() StateOf(session) = ExchangeAgentState.Online AndAlso session.PublicEndpoint.Length > 0))

                    ' the same device, as the server knows it, with what the viewer can do and no password
                    Assert.Equal(entry.DeviceId, acc.EnrollDeviceIdsSeen().Single())
                    Assert.Equal("OLD-NAME (viewer)", acc.EnrollNamesSeen().Single())
                    Assert.Equal({"pin-v1", "e2e-v1"}, acc.EnrollCapabilitiesSeen().Single().Split(","c))
                    Assert.Equal("", acc.EnrollPasswordsSeen().Single())
                    Using kept As ExchangeDeviceKey = store.DeviceKeysFor(entry.Id).TryLoad(entry.DeviceId)
                        Assert.Equal(kept.Kem, KemOf(acc.EnrollE2eKeysSeen().Single()))
                    End Using

                    ' the stream `enroll` opened is the control stream: one `hello` in all, and no `bye` said to the server on the way
                    Assert.Equal(1, acc.CountOf("hello"))
                    Assert.Equal(0, acc.CountOf("bye"))

                    ' and the entry's next look gets the directory, without its own record
                    Dim look As ExchangeAgentDirectoryResult = Await session.GetDirectoryAsync(0, 5000, CancellationToken.None)
                    Assert.True(look.Reply.IsOk)
                    Assert.DoesNotContain(look.Snapshot.Devices, Function(d) d.DeviceId = entry.DeviceId)

                    ' nothing is left to ask of the person
                    Assert.False(session.SignInCuresMissingAccess)
                End Using
            End Using
            Dim log As String = Logged()
            Assert.DoesNotContain(ExchangeStubAccount.PairingCode, log)
            Assert.DoesNotContain(oldToken, log)
            Assert.DoesNotContain(acc.TokenOf(entry.DeviceId), log)
            Assert.DoesNotContain(acc.Address, log)
            Assert.DoesNotContain(ExchangeStubAccount.Login, log)
            Assert.DoesNotContain(acc.Fingerprint, log)
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_code_leaves_the_session_alone_and_is_not_asked_again_at_once() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            acc.PairingRefusal = "rate-limited"
            Dim store As ExchangeEntryStore = MakeStore()
            Dim entry As ExchangeEntry = LegacyEntry(acc, store)
            Dim token As String = acc.TokenOf(entry.DeviceId)
            Using hub As New ExchangeEntryHub(MakePorts(store))
                Dim session As ExchangeEntrySession = hub.SessionFor(entry)
                Using lease As IDisposable = session.Lease()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("pairing-request") = 1))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() session.SignInCuresMissingAccess))

                    Await acc.PushDirectoryAsync()
                    Await acc.PushDirectoryAsync()
                    Await Task.Delay(200)

                    Assert.Equal(1, acc.CountOf("pairing-request"))
                    Assert.Empty(acc.EnrollDeviceIdsSeen())
                    Assert.Equal(1, acc.CountOf("hello"))
                    Assert.Equal(ExchangeAgentState.Online, StateOf(session))
                    Assert.Equal(token, StoredToken(store, entry))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_enroll_goes_back_to_the_kept_token_and_the_key_stays() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            acc.RefuseNextEnroll("rate-limited")
            Dim store As ExchangeEntryStore = MakeStore()
            Dim entry As ExchangeEntry = LegacyEntry(acc, store)
            Dim token As String = acc.TokenOf(entry.DeviceId)
            Using hub As New ExchangeEntryHub(MakePorts(store))
                Dim session As ExchangeEntrySession = hub.SessionFor(entry)
                Using lease As IDisposable = session.Lease()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.EnrollDeviceIdsSeen().Count = 1))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("hello") = 2))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() StateOf(session) = ExchangeAgentState.Online AndAlso session.PublicEndpoint.Length > 0))

                    Assert.Equal(token, StoredToken(store, entry))
                    Assert.True(store.DeviceKeysFor(entry.Id).Exists(entry.DeviceId))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_access_asked_during_the_upgrade_waits_and_opens_once_the_producer_sealed_to_the_new_key() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            Dim store As ExchangeEntryStore = MakeStore()
            Dim entry As ExchangeEntry = LegacyEntry(acc, store)
            Using phoneKey As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Dim phoneId As String = ExchangeWireTestKit.IdOf("P"c)
                Dim folderId As String = ExchangeWireTestKit.IdOf("1"c)
                acc.Devices.Add(PhoneRecord(phoneId, phoneKey))
                acc.Resources.Add(PhoneFolder(folderId, phoneId, phoneKey, "", ""))
                Using hub As New ExchangeEntryHub(MakePorts(store))
                    Dim asking As Task(Of ExchangeAgentAccessResult) = hub.SessionFor(entry).GetAccessAsync(folderId, CancellationToken.None)

                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.EnrollE2eKeysSeen().Count = 1))
                    Assert.False(asking.IsCompleted)

                    ' what a producer does when it sees the key: the folder again, sealed to this device
                    acc.Resources(0) = PhoneFolder(folderId, phoneId, phoneKey, entry.DeviceId, KemOf(acc.EnrollE2eKeysSeen().Single()))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("hello") = 1 AndAlso acc.TokenOf(entry.DeviceId) = StoredToken(store, entry)))
                    Await Task.Delay(100)
                    Await acc.PushDirectoryAsync()

                    Dim opened As ExchangeAgentAccessResult = Await asking
                    Assert.True(opened.Reply.IsOk, opened.Reply.Error)
                    Assert.Contains("top-secret", opened.AccessJson)
                End Using
            End Using
        End Using
    End Function

    ' ---- the Share Manager's account ---------------------------------------------------------------------------

    <Fact>
    Public Async Function The_agent_of_an_account_without_a_key_presents_one_the_same_way() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Dim keys As New ExchangeMemoryDeviceKeyStore()
                rig.Ports.DeviceKeys = keys
                rig.Ports.SealedTrust = New ExchangeMemorySealedTrustStore()
                Dim id As String = ExchangeWireTestKit.IdOf("D"c)
                Dim oldToken As String = acc.PreEnrollKeyless(id, "OLD-NAME")
                rig.Ports.Accounts.Save(New ExchangeAccount(acc.Address, ExchangeStubAccount.Login, acc.Fingerprint, ExchangeCertificateKind.Pinned, id, "PC"))
                Assert.True(rig.Ports.Credentials.Save(id, oldToken))
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    agent.Start()

                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.EnrollDeviceIdsSeen().Count = 1))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function()
                                                                             Dim stored As String = ""
                                                                             Return rig.Ports.Credentials.TryLoad(id, stored) AndAlso stored = acc.TokenOf(id) AndAlso stored <> oldToken
                                                                         End Function))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online AndAlso agent.GetSealedRecipients() IsNot Nothing AndAlso agent.GetSealedRecipients().HasOwnKey))

                    Assert.Equal(id, acc.EnrollDeviceIdsSeen().Single())
                    Assert.Equal("OLD-NAME", acc.EnrollNamesSeen().Single())
                    Assert.Equal("", acc.EnrollPasswordsSeen().Single())
                    Using kept As ExchangeDeviceKey = keys.TryLoad(id)
                        Assert.Equal(kept.Kem, KemOf(acc.EnrollE2eKeysSeen().Single()))
                    End Using
                    Assert.Equal(1, acc.CountOf("hello"))
                    Assert.Equal(0, acc.CountOf("bye"))
                End Using
                Dim everything As String = rig.Everything()
                Assert.DoesNotContain(ExchangeStubAccount.PairingCode, everything)
                Assert.DoesNotContain(acc.TokenOf(id), everything)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_agent_goes_back_to_its_kept_token_when_the_key_is_refused() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            acc.RefuseNextEnroll("rate-limited")
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Dim keys As New ExchangeMemoryDeviceKeyStore()
                rig.Ports.DeviceKeys = keys
                rig.Ports.SealedTrust = New ExchangeMemorySealedTrustStore()
                Dim id As String = ExchangeWireTestKit.IdOf("D"c)
                Dim token As String = acc.PreEnrollKeyless(id, "PC")
                rig.Ports.Accounts.Save(New ExchangeAccount(acc.Address, ExchangeStubAccount.Login, acc.Fingerprint, ExchangeCertificateKind.Pinned, id, "PC"))
                Assert.True(rig.Ports.Credentials.Save(id, token))
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    agent.Start()

                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("hello") = 2))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
                    Dim stored As String = ""
                    Assert.True(rig.Ports.Credentials.TryLoad(id, stored))
                    Assert.Equal(token, stored)
                    Assert.True(keys.Exists(id))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_agent_waits_with_its_key_while_a_broadcast_runs_on_the_stream() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            Using rig As New ExchangeDeviceAgentTests.Rig()
                rig.Ports.DeviceKeys = New ExchangeMemoryDeviceKeyStore()
                rig.Ports.SealedTrust = New ExchangeMemorySealedTrustStore()
                Dim id As String = ExchangeWireTestKit.IdOf("D"c)
                Dim token As String = acc.PreEnrollKeyless(id, "PC")
                rig.Ports.Accounts.Save(New ExchangeAccount(acc.Address, ExchangeStubAccount.Login, acc.Fingerprint, ExchangeCertificateKind.Pinned, id, "PC"))
                Assert.True(rig.Ports.Credentials.Save(id, token))
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim live As Reflection.FieldInfo = GetType(ExchangeDeviceAgent).GetField("_liveBroadcastId", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
                    live.SetValue(agent, ExchangeWireTestKit.IdOf("B"c))
                    agent.Start()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
                    Await acc.PushDirectoryAsync()
                    Await Task.Delay(400)

                    ' every chance so far passed by: the stream is what the broadcast rides on
                    Assert.Empty(acc.EnrollDeviceIdsSeen())
                    Assert.Equal(1, acc.CountOf("hello"))
                    Assert.Equal(0, acc.CountOf("bye"))
                    Assert.Equal(ExchangeAgentState.Online, agent.GetStatus().State)

                    ' the broadcast is over: the next change of the directory asks again, and the waiting cost no try of the pace
                    live.SetValue(agent, Nothing)
                    Await acc.PushDirectoryAsync()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.EnrollDeviceIdsSeen().Count = 1))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Letting_go_while_the_key_is_on_its_way_ends_the_new_stream_too() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            Dim answer As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            acc.EnrollHold = answer.Task
            Dim store As ExchangeEntryStore = MakeStore()
            Dim entry As ExchangeEntry = LegacyEntry(acc, store)
            Using hub As New ExchangeEntryHub(MakePorts(store))
                Dim session As ExchangeEntrySession = hub.SessionFor(entry)
                Dim lease As IDisposable = session.Lease()
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.EnrollDeviceIdsSeen().Count = 1))
                ' between the two streams nothing asks the person to sign in
                Assert.False(session.SignInCuresMissingAccess)
                lease.Dispose()
                answer.SetResult(True)

                ' the new token is kept, for the next time somebody looks, and the stream that came with it says `bye`
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("bye") = 1))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.LiveConnections() = 0))
                Assert.Equal(acc.TokenOf(entry.DeviceId), StoredToken(store, entry))
                Assert.Equal(ExchangeAgentState.Stopped, StateOf(session))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_lease_taken_while_the_key_is_on_its_way_gets_the_new_stream_and_no_second_connection() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = SealedFeatures()
            acc.ListEnrolledDevices = True
            Dim answer As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            acc.EnrollHold = answer.Task
            Dim store As ExchangeEntryStore = MakeStore()
            Dim entry As ExchangeEntry = LegacyEntry(acc, store)
            Using hub As New ExchangeEntryHub(MakePorts(store))
                Dim session As ExchangeEntrySession = hub.SessionFor(entry)
                Dim first As IDisposable = session.Lease()
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.EnrollDeviceIdsSeen().Count = 1))
                first.Dispose()
                Using second As IDisposable = session.Lease()
                    answer.SetResult(True)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() StateOf(session) = ExchangeAgentState.Online AndAlso session.PublicEndpoint.Length > 0))
                    Assert.Equal(1, acc.CountOf("hello"))
                    Assert.Equal(1, acc.EnrollDeviceIdsSeen().Count)
                    Assert.Equal(0, acc.CountOf("bye"))
                End Using
            End Using
        End Using
    End Function

    ' ---- the pace ----------------------------------------------------------------------------------------------

    <Fact>
    Public Sub One_try_at_a_time_and_a_growing_pause_after_each()
        Dim gate As New ExchangeKeyUpgradeGate()
        Const start As Long = 1000
        Assert.True(gate.IsPending(start))
        Assert.True(gate.TryBegin(start))
        Assert.False(gate.TryBegin(start))
        Assert.True(gate.IsPending(start))

        gate.Finish(start)
        Assert.False(gate.IsPending(start))
        Assert.False(gate.TryBegin(start + ExchangeKeyUpgradeGate.FirstPauseMs - 1))

        Dim second As Long = start + ExchangeKeyUpgradeGate.FirstPauseMs
        Assert.True(gate.TryBegin(second))
        gate.Finish(second)
        Assert.False(gate.TryBegin(second + ExchangeKeyUpgradeGate.FirstPauseMs))
        Assert.True(gate.TryBegin(second + 2 * ExchangeKeyUpgradeGate.FirstPauseMs))
    End Sub

    <Fact>
    Public Sub The_pause_stops_growing_at_an_hour()
        Dim gate As New ExchangeKeyUpgradeGate()
        Dim now As Long = 0
        For i As Integer = 1 To 12
            Assert.True(gate.TryBegin(now))
            gate.Finish(now)
            now += ExchangeKeyUpgradeGate.LongestPauseMs
        Next
        Assert.True(gate.TryBegin(now))
        gate.Finish(now)
        Assert.False(gate.TryBegin(now + ExchangeKeyUpgradeGate.LongestPauseMs - 1))
        Assert.True(gate.TryBegin(now + ExchangeKeyUpgradeGate.LongestPauseMs))
    End Sub

End Class
