Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 234 slice C3, DEVICE-EXCHANGE section 27 items DF and DG on the Windows device agent: one P-256 key per device id, created before the
''' first `enroll` / `register` and reused for as long as the id lives; the public half (`e2eKey {v:1,kem}`) and the capability `e2e-v1` in
''' the frame; a failed enrollment takes back a key it made and leaves one that was there; the private half in no log, setting or token file;
''' the kept keys of the account's devices wired from the agent's stores into the session.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeDeviceKeyTests

    ''' <summary>A key store that remembers what was created and taken back, and can be told to refuse every write.</summary>
    Friend NotInheritable Class TrackingKeyStore
        Implements IExchangeDeviceKeyStore

        Public ReadOnly Inner As New ExchangeMemoryDeviceKeyStore()
        Public ReadOnly Created As New ConcurrentQueue(Of String)
        Public ReadOnly Deleted As New ConcurrentQueue(Of String)
        Public RefuseSave As Boolean

        Public Function TryLoad(deviceId As String) As ExchangeDeviceKey Implements IExchangeDeviceKeyStore.TryLoad
            SyncLock Inner
                Return Inner.TryLoad(deviceId)
            End SyncLock
        End Function

        Public Function Save(deviceId As String, key As ExchangeDeviceKey) As Boolean Implements IExchangeDeviceKeyStore.Save
            If RefuseSave Then Return False
            SyncLock Inner
                Dim isNew As Boolean = Not Inner.Exists(deviceId)
                Dim ok As Boolean = Inner.Save(deviceId, key)
                If ok AndAlso isNew Then Created.Enqueue(deviceId)
                Return ok
            End SyncLock
        End Function

        Public Function Exists(deviceId As String) As Boolean Implements IExchangeDeviceKeyStore.Exists
            SyncLock Inner
                Return Inner.Exists(deviceId)
            End SyncLock
        End Function

        Public Sub Delete(deviceId As String) Implements IExchangeDeviceKeyStore.Delete
            SyncLock Inner
                If Inner.Exists(deviceId) Then Deleted.Enqueue(deviceId)
                Inner.Delete(deviceId)
            End SyncLock
        End Sub

        Public Function KemOf(deviceId As String) As String
            Using key As ExchangeDeviceKey = TryLoad(deviceId)
                Return If(key Is Nothing, "", key.Kem)
            End Using
        End Function
    End Class

    Private Shared Function NewRig(Optional keys As IExchangeDeviceKeyStore = Nothing, Optional trust As IExchangeSealedTrustStore = Nothing) As ExchangeDeviceAgentTests.Rig
        Dim rig As New ExchangeDeviceAgentTests.Rig()
        rig.Ports.DeviceKeys = If(keys, New TrackingKeyStore())
        rig.Ports.SealedTrust = If(trust, New ExchangeMemorySealedTrustStore())
        Return rig
    End Function

    Private Shared Function RegisterRequest(acc As ExchangeStubAccount) As ExchangeRegisterRequest
        Return New ExchangeRegisterRequest With {
            .Server = acc.Address, .Login = "carol", .Password = ExchangeStubAccount.Password, .InvitationCode = ExchangeStubAccount.Invitation,
            .PlatformValid = False, .AcceptedFingerprint = acc.Fingerprint}
    End Function

    ''' <summary>The `e2eKey` object of one frame, parsed; fails the test when the frame carried none.</summary>
    Private Shared Function ParseKey(raw As String) As JsonElement
        Assert.False(String.IsNullOrEmpty(raw), "the frame carried no e2eKey")
        Using doc As JsonDocument = JsonDocument.Parse(raw)
            Return doc.RootElement.Clone()
        End Using
    End Function

    Private Shared Sub AssertKeyMember(raw As String, expectedKem As String)
        Dim e2e As JsonElement = ParseKey(raw)
        Assert.Equal(JsonValueKind.Object, e2e.ValueKind)
        Assert.Equal(1, e2e.GetProperty("v").GetInt32())
        Assert.Equal(expectedKem, e2e.GetProperty("kem").GetString())
        Assert.Equal({"kem", "v"}, e2e.EnumerateObject().Select(Function(p) p.Name).OrderBy(Function(n) n, StringComparer.Ordinal).ToArray())   ' exactly {v, kem}
        Dim point As Byte() = Nothing
        Assert.True(ExchangeDeviceKey.TryParseKem(expectedKem, point))   ' a real P-256 point, not a placeholder
    End Sub

    Private Shared Function Leaks(text As String, scalar As Byte()) As Boolean
        Return text.Contains(Encoding.Latin1.GetString(scalar), StringComparison.Ordinal) OrElse
               text.Contains(Convert.ToHexString(scalar), StringComparison.OrdinalIgnoreCase) OrElse
               text.Contains(Convert.ToBase64String(scalar), StringComparison.Ordinal) OrElse
               text.Contains(ExchangeBase64Url.Encode(scalar), StringComparison.Ordinal)
    End Function

    ' ---------------------------------------------------------------- DF: one key per id, public half and capability in the frame

    <Fact>
    Public Async Function Enrolling_makes_one_key_for_the_id_and_sends_only_its_public_half_with_the_capability() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Null(agent.GetSealedRecipients())   ' no account, no session: nothing to ask
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))

                    Assert.Equal({id}, keys.Created.ToArray())        ' one key, for this id
                    Assert.Empty(keys.Deleted)
                    Dim kem As String = keys.KemOf(id)
                    Assert.NotEqual("", kem)
                    Assert.Equal(id, acc.EnrollDeviceIdsSeen().Single())
                    AssertKeyMember(acc.EnrollE2eKeysSeen().Single(), kem)
                    Assert.Contains(ExchangeSealedNames.CapabilityE2e, acc.EnrollCapabilitiesSeen().Single().Split(","c))

                    ' the session knows its own key: the directory is checked against it, never the other way round
                    Assert.Equal(kem, agent.GetSealedRecipients().TrustedKeyOf(id))
                    Assert.True(agent.GetSealedRecipients().HasOwnKey)

                    ' the private half is nowhere the rig looked: settings, token files, log, the frame's stub copy
                    Dim scalar As Byte()
                    Using key As ExchangeDeviceKey = keys.TryLoad(id)
                        scalar = key.ExportPrivateScalar()
                    End Using
                    Assert.False(Leaks(rig.Everything(), scalar))
                    Assert.False(Leaks(String.Join(vbLf, acc.EnrollE2eKeysSeen()), scalar))
                    Assert.False(Leaks(String.Join(vbLf, acc.EnrollCapabilitiesSeen()), scalar))
                    Assert.False(Leaks(String.Join(vbLf, acc.ReceivedOn(1)), scalar))

                    ' a clean exit and a restart: a hello with the token, the same key, no new one
                    Await agent.StopAsync()
                End Using
                Using second As ExchangeDeviceAgent = rig.NewAgent()
                    second.Start()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() second.GetStatus().State = ExchangeAgentState.Online))
                    Assert.Equal(1, keys.Created.Count)
                    Assert.Equal(1, acc.CountOf("enroll"))
                    Assert.Equal(keys.KemOf(second.GetStatus().DeviceId), second.GetSealedRecipients().TrustedKeyOf(second.GetStatus().DeviceId))

                    ' leaving the account keeps the id, so the key stays with it; signing in again carries the very same key (item DF)
                    Dim id As String = second.GetStatus().DeviceId
                    Dim kem As String = keys.KemOf(id)
                    Await second.DisconnectAsync()
                    Assert.True(keys.Exists(id))
                    Assert.Equal(kem, keys.KemOf(id))
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await second.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Assert.Equal(id, second.GetStatus().DeviceId)
                    Assert.Equal({id, id}, acc.EnrollDeviceIdsSeen().ToArray())
                    Assert.Equal(1, keys.Created.Count)
                    Assert.Empty(keys.Deleted)
                    Dim sent As List(Of String) = acc.EnrollE2eKeysSeen()
                    Assert.Equal(2, sent.Count)
                    AssertKeyMember(sent(1), kem)
                    Assert.Equal(ParseKey(sent(0)).GetProperty("kem").GetString(), ParseKey(sent(1)).GetProperty("kem").GetString())
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Registering_carries_the_same_key_member_and_capability() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim result As ExchangeEnrollResult = Await agent.RegisterAsync(RegisterRequest(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Assert.Equal({id}, keys.Created.ToArray())
                    Assert.Equal(1, acc.CountOf("register"))
                    Assert.Equal(id, acc.EnrollDeviceIdsSeen().Single())
                    AssertKeyMember(acc.EnrollE2eKeysSeen().Single(), keys.KemOf(id))
                    Assert.Contains(ExchangeSealedNames.CapabilityE2e, acc.EnrollCapabilitiesSeen().Single().Split(","c))
                    Assert.DoesNotContain(ExchangeStubAccount.Invitation, rig.Everything())

                    Dim scalar As Byte()
                    Using key As ExchangeDeviceKey = keys.TryLoad(id)
                        scalar = key.ExportPrivateScalar()
                    End Using
                    Assert.False(Leaks(rig.Everything(), scalar))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_key_file_is_protected_and_sits_beside_the_tokens_without_the_private_half_in_the_clear() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Dim store As New ExchangeDeviceKeyStore(rig.Folder, rig.Cipher)
                rig.Ports.DeviceKeys = store
                rig.Ports.SealedTrust = New ExchangeMemorySealedTrustStore()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Assert.True(File.Exists(store.FilePathFor(id)))
                    Dim scalar As Byte()
                    Using key As ExchangeDeviceKey = store.TryLoad(id)
                        Assert.NotNull(key)
                        scalar = key.ExportPrivateScalar()
                        AssertKeyMember(acc.EnrollE2eKeysSeen().Single(), key.Kem)
                    End Using
                    Assert.False(Leaks(Encoding.Latin1.GetString(File.ReadAllBytes(store.FilePathFor(id))), scalar))   ' not in the clear on disk
                    Assert.False(Leaks(rig.Everything(), scalar))   ' Everything() reads every file of the folder, the key file included
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- DF: a failed enrollment

    <Fact>
    Public Async Function A_refused_enrollment_takes_back_the_key_it_made() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    acc.RefuseNextEnroll("bad-credentials")
                    Dim failed As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Refused, failed.Outcome)
                    Dim refusedId As String = acc.EnrollDeviceIdsSeen().Single()
                    Assert.Equal({refusedId}, keys.Created.ToArray())          ' a key was made for the attempt..
                    Assert.Equal({refusedId}, keys.Deleted.ToArray())          ' ..and taken back
                    Assert.False(keys.Exists(refusedId))
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(0, rig.Port.Writes)                           ' no id, no setting

                    ' the next try is a first attempt again: its own id and its own key, and this one stays
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Assert.NotEqual(refusedId, id)
                    Assert.True(keys.Exists(id))
                    Assert.False(keys.Exists(refusedId))
                    Assert.Equal(2, keys.Created.Count)
                    AssertKeyMember(acc.EnrollE2eKeysSeen()(1), keys.KemOf(id))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_enrollment_does_not_take_back_the_key_that_was_there_before() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Dim kem As String = keys.KemOf(id)
                    Await agent.DisconnectAsync()                              ' the id stays, so does its key

                    acc.RefuseNextEnroll("rate-limited")
                    Dim failed As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Refused, failed.Outcome)
                    Assert.Equal(ExchangeRefusal.RateLimited, failed.Refusal)
                    Assert.True(keys.Exists(id))                               ' the device's own key is not the attempt's
                    Assert.Equal(kem, keys.KemOf(id))
                    Assert.Empty(keys.Deleted)

                    ' and the retry sends that key again
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim sent As List(Of String) = acc.EnrollE2eKeysSeen()
                    Assert.Equal(3, sent.Count)
                    AssertKeyMember(sent(2), kem)
                    Assert.Equal(kem, ParseKey(sent(0)).GetProperty("kem").GetString())
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_registration_takes_back_the_key_it_made() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    acc.RefuseNextRegister("capacity")
                    Dim failed As ExchangeEnrollResult = Await agent.RegisterAsync(RegisterRequest(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Refused, failed.Outcome)
                    Assert.Equal(ExchangeRefusal.Capacity, failed.Refusal)
                    Dim id As String = acc.EnrollDeviceIdsSeen().Single()
                    Assert.Equal({id}, keys.Created.ToArray())
                    Assert.Equal({id}, keys.Deleted.ToArray())
                    Assert.False(keys.Exists(id))
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(0, rig.Port.Writes)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_answer_that_never_comes_takes_back_the_key_as_well() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    ' a certificate the person has not accepted is turned away before anything is made: no id, no key
                    Dim noAccept As ExchangeEnrollRequest = rig.Request(acc)
                    noAccept.AcceptedFingerprint = ""
                    Assert.Equal(ExchangeEnrollOutcome.Untrusted, (Await agent.EnrollAsync(noAccept, CancellationToken.None)).Outcome)
                    Assert.Empty(keys.Created)

                    ' a pin that is not the server's: the frame never leaves, the key made for it goes
                    Dim wrong As ExchangeEnrollRequest = rig.Request(acc)
                    wrong.AcceptedFingerprint = ExchangeTrust.FingerprintOf(New Byte() {9})
                    Assert.Equal(ExchangeEnrollOutcome.PinMismatch, (Await agent.EnrollAsync(wrong, CancellationToken.None)).Outcome)
                    Assert.Equal(0, acc.CountOf("enroll"))
                    Assert.Equal(keys.Created.ToArray(), keys.Deleted.ToArray())
                    Assert.Equal(1, keys.Created.Count)
                    Assert.False(keys.Exists(keys.Created.Single()))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_key_that_cannot_be_kept_sends_nothing_and_says_so() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            keys.RefuseSave = True
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim request As ExchangeEnrollRequest = rig.Request(acc)
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(request, CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.StoreFailed, result.Outcome)
                    Assert.Equal(0, rig.Transport.Attempts)                    ' nothing was sent: a key that is not stored is lost at the next start
                    Assert.Equal(0, acc.CountOf("enroll"))
                    Assert.Equal("", request.Password)                         ' the secret is wiped on this path too
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(0, rig.Port.Writes)
                    Assert.Contains(rig.Log, Function(l) l.Contains("device key could not be kept", StringComparison.Ordinal))

                    Dim register As ExchangeRegisterRequest = RegisterRequest(acc)
                    Assert.Equal(ExchangeEnrollOutcome.StoreFailed, (Await agent.RegisterAsync(register, CancellationToken.None)).Outcome)
                    Assert.Equal("", register.Password)
                    Assert.Equal("", register.InvitationCode)
                    Assert.Equal(0, rig.Transport.Attempts)
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- DF: id-collision

    <Fact>
    Public Async Function An_id_collision_is_a_refusal_like_the_others_and_the_next_try_is_a_new_id_with_a_new_key() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New TrackingKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    acc.RefuseNextEnroll("id-collision")
                    Dim request As ExchangeEnrollRequest = rig.Request(acc)
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(request, CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Refused, result.Outcome)
                    Assert.Equal(ExchangeRefusal.IdCollision, result.Refusal)
                    ' it reads as its own sentence, not the generic "the server refused" of an unknown reason
                    Dim sentence As String = ExchangeDeviceText.EnrollFailure(result)
                    Assert.NotEqual("", sentence)
                    Assert.NotEqual(ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.Unknown, "")), sentence)
                    Assert.Equal("", request.Password)
                    Dim collidingId As String = acc.EnrollDeviceIdsSeen().Single()
                    Assert.False(keys.Exists(collidingId))                                    ' the key made for the colliding id goes
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.False(rig.Ports.Credentials.Exists())
                    Assert.Equal(0, rig.Port.Writes)                                          ' the colliding id is not remembered
                    Assert.Equal(ExchangeAgentState.NotConfigured, agent.GetStatus().State)

                    ' the person tries again: a new id, so a new key; the contract's "enrolls anew with a new deviceId"
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Assert.NotEqual(collidingId, id)
                    Assert.True(keys.Exists(id))
                    Assert.NotEqual("", keys.KemOf(id))
                    AssertKeyMember(acc.EnrollE2eKeysSeen()(1), keys.KemOf(id))

                    ' the same for a registration
                    Await agent.DisconnectAsync()
                    acc.RefuseNextRegister("id-collision")
                    Dim register As ExchangeRegisterRequest = RegisterRequest(acc)
                    Dim registered As ExchangeEnrollResult = Await agent.RegisterAsync(register, CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Refused, registered.Outcome)
                    Assert.Equal(ExchangeRefusal.IdCollision, registered.Refusal)
                    Assert.Equal("", register.Password)
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- DG/DP/DR: the layer wired into the session

    Private Shared Function PhoneRecord(id As String, kem As String, Optional capable As Boolean = True) As String
        Dim o As New JsonObject()
        o("deviceId") = id
        o("deviceName") = "Phone"
        o("product") = "fms-android"
        o("presence") = "online"
        If capable Then
            Dim caps As New JsonArray()
            caps.Add(JsonValue.Create(ExchangeSealedNames.CapabilityE2e))
            o("capabilities") = caps
        End If
        If kem.Length > 0 Then
            Dim key As New JsonObject()
            key("v") = 1
            key("kem") = kem
            o("e2eKey") = key
        End If
        Return o.ToJsonString()
    End Function

    Private Shared Function NewKem() As String
        Using k As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Return k.Kem
        End Using
    End Function

    Private Shared Async Function StateOfAsync(agent As ExchangeDeviceAgent, deviceId As String, expected As ExchangeSealState) As Task(Of Boolean)
        Return Await ExchangeStubAccount.WaitUntilAsync(
            Function()
                Dim view As ExchangeSealedRecipients = agent.GetSealedRecipients()
                If view Is Nothing Then Return False
                Dim s As Nullable(Of ExchangeSealState) = view.StateOf(deviceId)
                Return s.HasValue AndAlso s.Value = expected
            End Function)
    End Function

    <Fact>
    Public Async Function The_agent_trusts_a_key_on_first_sight_and_catches_a_changed_one_after_a_restart_and_forgets_it_on_leaving() As Task
        Dim phone As String = ExchangeWireTestKit.IdOf("P"c)
        Dim first As String = NewKem()
        Dim second As String = NewKem()
        Using acc As New ExchangeStubAccount()
            acc.Features = {"directory", "tunnel", "pairing", ExchangeSealedNames.FeatureSealed}
            acc.Devices.Add(PhoneRecord(phone, first))
            Dim trust As New ExchangeMemorySealedTrustStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(trust:=trust)
                Dim ownId As String
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    ownId = agent.GetStatus().DeviceId
                    Assert.True(Await StateOfAsync(agent, phone, ExchangeSealState.Ready))
                    Dim view As ExchangeSealedRecipients = agent.GetSealedRecipients()
                    Assert.True(view.CanSealThroughServer)
                    Assert.Equal({phone}, view.Recipients(False).Select(Function(r) r.DeviceId).ToArray())
                    Assert.Equal(first, view.Recipients(False).Single().Kem)
                    Assert.Equal(first, trust.TryGet(ownId, phone))        ' the agent's own trust store, under this device's entry
                    Await agent.StopAsync()
                End Using

                ' the phone's record now shows another key: the agent has been restarted, only the store remembers
                acc.Devices.Clear()
                acc.Devices.Add(PhoneRecord(phone, second))
                Using again As ExchangeDeviceAgent = rig.NewAgent()
                    again.Start()
                    Assert.True(Await StateOfAsync(again, phone, ExchangeSealState.KeyChanged))
                    Dim view As ExchangeSealedRecipients = again.GetSealedRecipients()
                    Assert.Empty(view.Recipients(False))
                    Assert.Equal("key-changed", view.Devices().Single(Function(d) d.DeviceId = phone).Message.Code)
                    Assert.Equal(first, trust.TryGet(ownId, phone))        ' never replaced

                    ' leaving the account takes the account's kept keys with it (item DG); the own key and id stay (item DF)
                    Await again.DisconnectAsync()
                    Assert.Equal("", trust.TryGet(ownId, phone))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_peer_without_the_capability_is_e2e_missing_in_the_agents_view() As Task
        Dim tv As String = ExchangeWireTestKit.IdOf("T"c)
        Using acc As New ExchangeStubAccount()
            acc.Features = {"directory", ExchangeSealedNames.FeatureSealed}
            acc.Devices.Add(PhoneRecord(tv, "", capable:=False))
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Assert.True(Await StateOfAsync(agent, tv, ExchangeSealState.E2eMissing))
                    Dim view As ExchangeSealedRecipients = agent.GetSealedRecipients()
                    Assert.Empty(view.Recipients(False))
                    Assert.Equal("e2e-missing", view.Devices().Single(Function(d) d.DeviceId = tv).Message.Code)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_server_without_the_sealed_feature_is_server_no_e2e_in_the_agents_view() As Task
        Dim phone As String = ExchangeWireTestKit.IdOf("P"c)
        Using acc As New ExchangeStubAccount()
            acc.Devices.Add(PhoneRecord(phone, NewKem()))
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Assert.True(Await StateOfAsync(agent, phone, ExchangeSealState.Ready))
                    Dim view As ExchangeSealedRecipients = agent.GetSealedRecipients()
                    Assert.False(view.CanSealThroughServer)
                    Assert.Equal("server-no-e2e", view.ServerMessage.Code)
                    Assert.Empty(view.Recipients(False))
                End Using
            End Using
        End Using
    End Function

End Class
