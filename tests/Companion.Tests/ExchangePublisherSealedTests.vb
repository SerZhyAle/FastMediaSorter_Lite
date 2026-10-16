Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 234 slice D3, DEVICE-EXCHANGE section 27 items DI, DP and H: the producer inside the agent publishes a folder SEALED or not at all.
''' The record carries `sealedAccess` and no `access`; the box opens on another device of the account through the same ExchangeSealedAccess.Resolve
''' the consumer's agent uses; a server that does not list `sealed` gets no folder (state `server-no-e2e`); a device that is not a ready recipient
''' of its own entry publishes nothing (state `e2e-missing`); and when the recipient set changes while the producer is online (a device joins or
''' leaves) the folders are published again, once, so a device added later can open them. Same rig as ExchangePublisherTests (the stub account, an
''' in-memory key store, a fake share worker); every socket is on 127.0.0.1.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangePublisherSealedTests

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const WorkerPassword As String = "Zk7PmQ2wXr9TzS4vGnHb3JdL"

    ''' <summary>Another device of the account: an id, a key of its own, a trust store, and the directory record the server lists for it.</summary>
    Private NotInheritable Class Phone
        Implements IDisposable

        Public ReadOnly Id As String
        Public ReadOnly Key As ExchangeDeviceKey = ExchangeDeviceKey.Generate()

        Public Sub New(letter As Char)
            Id = ExchangeWireTestKit.IdOf(letter)
        End Sub

        Public Function Record(Optional capable As Boolean = True, Optional withKey As Boolean = True) As String
            Dim o As New JsonObject()
            o("deviceId") = Id
            o("deviceName") = "Phone"
            o("product") = "fms-android"
            o("presence") = "online"
            If capable Then
                Dim caps As New JsonArray()
                caps.Add(JsonValue.Create(ExchangeSealedNames.CapabilityE2e))
                o("capabilities") = caps
            End If
            If withKey Then
                Dim k As New JsonObject()
                k("v") = 1
                k("kem") = Key.Kem
                o("e2eKey") = k
            End If
            Return o.ToJsonString()
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Key.Dispose()
        End Sub
    End Class

    ''' <summary>What this phone gets out of a published record: the production Resolve, with a view of the account built the way the phone's
    ''' agent would build it (the account's directory, the phone's own key, the producer's key trusted on first sight).</summary>
    Private Shared Function ResolveOn(agent As ExchangeDeviceAgent, phone As Phone, recordJson As String) As ExchangeSealedAccessResult
        Dim rec As ExchangeResourceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadResource(recordJson, rec))
        Dim features As IReadOnlyList(Of String) = New String() {"directory", "sealed"}
        Dim trust As New ExchangeMemorySealedTrustStore()
        Dim view As New ExchangeSealedRecipients(phone.Id, phone.Key.Kem, trust, Function() agent.GetFullDirectory(), Function() features)
        view.Refresh()
        Return ExchangeSealedAccess.Resolve(rec, view, trust, phone.Key)
    End Function

    Private Shared Function MemberCount(recordJson As String, member As String) As Integer
        Using doc As JsonDocument = JsonDocument.Parse(recordJson)
            Dim el As JsonElement = Nothing
            Return If(doc.RootElement.TryGetProperty(member, el), 1, 0)
        End Using
    End Function

    ''' <summary>The device ids a box is addressed to.</summary>
    Private Shared Function AddresseesOf(recordJson As String) As String()
        Using doc As JsonDocument = JsonDocument.Parse(recordJson)
            Dim box As ExchangeSealedBox = Nothing
            Assert.True(ExchangeSealedBox.TryParse(doc.RootElement.GetProperty("sealedAccess").GetRawText(), box))
            Return box.Keys.Select(Function(k) k.ToDeviceId).OrderBy(Function(s) s, StringComparer.Ordinal).ToArray()
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ item DI

    <Fact>
    Public Async Function A_published_folder_has_sealedAccess_and_no_access_and_a_second_device_opens_the_box() As Task
        Using phone As New Phone("P"c)
            Using acc As New ExchangeStubAccount()
                acc.Devices.Add(phone.Record())
                Using kit As New ExchangePublisherTests.Kit()
                    kit.Settings.SetPublishShares(True)
                    Using agent As ExchangeDeviceAgent = Await ExchangePublisherTests.EnrollOnlineAsync(kit, acc)
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                        Dim producer As String = agent.GetStatus().DeviceId
                        Dim photos As String = ExchangePublisherTests.Latest(acc, "Photos")

                        Assert.Equal(0, MemberCount(photos, "access"))                       ' item DI: no clear `access` ever
                        Assert.Equal(1, MemberCount(photos, "sealedAccess"))
                        Assert.Equal(Fp, JsonDocument.Parse(photos).RootElement.GetProperty("hostKeyFingerprint").GetString())    ' the identity pair stays in clear
                        Assert.DoesNotContain(WorkerPassword, photos, StringComparison.Ordinal)
                        Assert.DoesNotContain("FMSSFTP", photos, StringComparison.Ordinal)
                        Assert.Equal({producer, phone.Id}.OrderBy(Function(s) s, StringComparer.Ordinal).ToArray(), AddresseesOf(photos))

                        ' the other device opens it through the consumer's own code path
                        Dim opened As ExchangeSealedAccessResult = ResolveOn(agent, phone, photos)
                        Assert.Null(opened.State)
                        Assert.True(opened.Opened)
                        Dim info As ExchangeAccessInfo = Nothing
                        Assert.True(ExchangeResourceAccess.TryRead(opened.AccessJson, "Photos", info))
                        Assert.Equal(WorkerPassword, info.Code.Password)
                        Assert.Equal(Fp, info.Code.Fingerprint)
                        Assert.Equal("/Photos", info.Root)

                        ' and the producer opens its own record (it is a recipient of its own entry)
                        Assert.Equal(Fp, ExchangeSealedAccess.FingerprintOf(ExchangePublisherTests.OpenOwnBox(kit, producer, photos), "Photos"))
                        Assert.All(kit.Publisher.Snapshot.Rows, Sub(r) Assert.Equal(ExchangePublishState.Published, r.State))
                    End Using
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ item H

    <Fact>
    Public Async Function A_device_that_joins_or_leaves_while_the_producer_is_online_makes_it_publish_again_once() As Task
        Using phone As New Phone("P"c)
            Using acc As New ExchangeStubAccount()
                Using kit As New ExchangePublisherTests.Kit()
                    kit.Settings.SetPublishShares(True)
                    Using agent As ExchangeDeviceAgent = Await ExchangePublisherTests.EnrollOnlineAsync(kit, acc)
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                        Await Task.Delay(300)
                        Dim producer As String = agent.GetStatus().DeviceId
                        Dim first As String = ExchangePublisherTests.Latest(acc, "Photos")
                        Assert.Equal({producer}, AddresseesOf(first))                              ' alone in the account: only itself
                        Assert.Equal(ExchangeSealState.Ready, agent.GetSealedRecipients().StateOf(producer).Value)
                        Dim afterFirst As Integer = acc.PublishLog().Count

                        ' the phone joins: a changed directory arrives, the folders are sealed again, for both devices
                        acc.Devices.Add(phone.Record())
                        Await acc.PushDirectoryAsync()
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishLog().Count >= afterFirst + 2))
                        Await Task.Delay(400)
                        Assert.Equal(afterFirst + 2, acc.PublishLog().Count)                       ' once for each folder, not a storm
                        Dim joined As String = ExchangePublisherTests.Latest(acc, "Photos")
                        Assert.Equal({producer, phone.Id}.OrderBy(Function(s) s, StringComparer.Ordinal).ToArray(), AddresseesOf(joined))
                        Assert.True(ResolveOn(agent, phone, joined).Opened)
                        Assert.True(ResolveOn(agent, phone, ExchangePublisherTests.Latest(acc, "Videos")).Opened)
                        Assert.Equal(0, MemberCount(joined, "access"))
                        Dim afterJoin As Integer = acc.PublishLog().Count

                        ' the same state pushed again changes nothing: no publish at all
                        Await acc.PushDirectoryAsync()
                        Await Task.Delay(400)
                        Assert.Equal(afterJoin, acc.PublishLog().Count)

                        ' the phone leaves: the next record is not addressed to it any more
                        acc.Devices.Clear()
                        Await acc.PushDirectoryAsync()
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishLog().Count >= afterJoin + 2))
                        Await Task.Delay(300)
                        Dim left As String = ExchangePublisherTests.Latest(acc, "Photos")
                        Assert.Equal({producer}, AddresseesOf(left))
                        Dim gone As ExchangeSealedAccessResult = ResolveOn(agent, phone, left)
                        Assert.False(gone.Opened)
                        Assert.Same(ConnectionDiagnosisMessages.SealedFailed, gone.State)
                        Assert.Equal("", gone.AccessJson)
                        Assert.All(kit.Publisher.Snapshot.Rows, Sub(r) Assert.Equal(ExchangePublishState.Published, r.State))
                    End Using
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_device_without_the_capability_or_without_a_key_is_never_a_recipient() As Task
        Using noKey As New Phone("N"c)
            Using noCapability As New Phone("M"c)
                Using acc As New ExchangeStubAccount()
                    acc.Devices.Add(noKey.Record(withKey:=False))
                    acc.Devices.Add(noCapability.Record(capable:=False))
                    Using kit As New ExchangePublisherTests.Kit()
                        kit.Settings.SetPublishShares(True)
                        Using agent As ExchangeDeviceAgent = Await ExchangePublisherTests.EnrollOnlineAsync(kit, acc)
                            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                            Dim view As ExchangeSealedRecipients = agent.GetSealedRecipients()
                            Assert.Equal(ExchangeSealState.E2eMissing, view.StateOf(noKey.Id).Value)
                            Assert.Equal(ExchangeSealState.E2eMissing, view.StateOf(noCapability.Id).Value)
                            Assert.Equal({agent.GetStatus().DeviceId}, AddresseesOf(ExchangePublisherTests.Latest(acc, "Photos")))
                        End Using
                    End Using
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ item DP

    <Fact>
    Public Async Function A_server_without_the_sealed_feature_gets_no_folder_and_the_state_is_server_no_e2e() As Task
        Using acc As New ExchangeStubAccount()
            Using kit As New ExchangePublisherTests.Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = Await ExchangePublisherTests.EnrollOnlineAsync(kit, acc, sealedServer:=False)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Block = ExchangePublishBlock.ServerNoE2e))
                    Await Task.Delay(400)                                                          ' several polls
                    Assert.Equal(0, acc.CountOf("publish"))                                        ' not even a record without `access`
                    Assert.Empty(acc.PublishLog())
                    Assert.Equal("server-no-e2e", agent.GetSealedRecipients().ServerMessage.Code)
                    Assert.Empty(kit.Publisher.Snapshot.Rows)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_producer_that_is_not_a_ready_recipient_of_its_own_entry_publishes_nothing() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = {"directory", "tunnel", "pairing", ExchangeSealedNames.FeatureSealed}
            ' ListEnrolledDevices stays off: the directory does not list this device, so there is no recipient to seal to
            Using kit As New ExchangePublisherTests.Kit()
                kit.Settings.SetPublishShares(True)
                Using agent As ExchangeDeviceAgent = kit.Rig.NewAgent()
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(kit.Rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Block = ExchangePublishBlock.NoOwnKey))
                    Await Task.Delay(300)
                    Assert.Equal(0, acc.CountOf("publish"))
                    Assert.Empty(acc.PublishLog())
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_server_without_the_sealed_feature_keeps_presence_and_the_directory_and_a_link_cannot_be_made() As Task
        Using phone As New Phone("P"c)
            Using acc As New ExchangeStubAccount()
                acc.Devices.Add(phone.Record())
                Using kit As New ExchangePublisherTests.Kit()
                    kit.Settings.SetPublishShares(True)
                    Using agent As ExchangeDeviceAgent = Await ExchangePublisherTests.EnrollOnlineAsync(kit, acc, sealedServer:=False)
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Block = ExchangePublishBlock.ServerNoE2e))
                        ' presence and the directory work as before: the entry is online and lists the account's other device
                        Assert.Equal(ExchangeAgentState.Online, agent.GetStatus().State)
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetFullDirectory().Devices.Any(Function(d) d.DeviceId = phone.Id)))
                        ' a link is a change to a published record, and nothing is published: it is refused as "not published", not sent in clear
                        Assert.Equal(ExchangeLinkOutcome.NotPublished, (Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)).Outcome)
                        Await Task.Delay(300)
                        Assert.Equal(0, acc.CountOf("publish"))
                        Assert.Empty(acc.PublishLog())
                        Assert.Empty(acc.PublishedNow())
                        Assert.Equal("server-no-e2e", agent.GetSealedRecipients().ServerMessage.Code)
                    End Using
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ item DJ: a link

    <Fact>
    Public Async Function A_link_keeps_the_record_sealed_with_the_share_id_inside_the_box_and_no_record_ever_has_a_clear_access() As Task
        Using phone As New Phone("P"c)
            Using acc As New ExchangeStubAccount()
                acc.Devices.Add(phone.Record())
                Using kit As New ExchangePublisherTests.Kit()
                    kit.Settings.SetPublishShares(True)
                    Using agent As ExchangeDeviceAgent = Await ExchangePublisherTests.EnrollOnlineAsync(kit, acc)
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                        Dim producer As String = agent.GetStatus().DeviceId
                        Assert.Equal(ExchangeLinkOutcome.Done, (Await kit.Publisher.CreateLinkAsync("Photos", CancellationToken.None)).Outcome)

                        Dim linked As String = ExchangePublisherTests.Latest(acc, "Photos")
                        Dim shareId As String = JsonDocument.Parse(linked).RootElement.GetProperty("shareId").GetString()
                        Assert.False(String.IsNullOrEmpty(shareId))                              ' the id the server routes an outsider by stays a member of the record
                        Assert.Equal(0, MemberCount(linked, "access"))
                        Assert.Equal(1, MemberCount(linked, "sealedAccess"))
                        Assert.Equal({producer, phone.Id}.OrderBy(Function(s) s, StringComparer.Ordinal).ToArray(), AddresseesOf(linked))
                        ' the descriptor inside the box is the one the person hands out: it carries the same share id
                        Dim info As ExchangeAccessInfo = Nothing
                        Assert.True(ExchangeResourceAccess.TryRead(ExchangePublisherTests.OpenOwnBox(kit, producer, linked), "Photos", info))
                        Assert.Equal(shareId, info.Code.ShareId)
                        Assert.Equal(Fp, info.Code.Fingerprint)

                        ' the link is taken away: still sealed, and now without the id
                        Assert.Equal(ExchangeLinkOutcome.Done, (Await kit.Publisher.RemoveLinkAsync("Photos", CancellationToken.None)).Outcome)
                        Dim removed As String = ExchangePublisherTests.Latest(acc, "Photos")
                        Assert.Equal(0, MemberCount(removed, "shareId"))
                        Assert.Equal(1, MemberCount(removed, "sealedAccess"))

                        ' not one record of the whole run carried a clear access or the worker's credential
                        Assert.NotEmpty(acc.PublishLog())
                        Assert.All(acc.PublishLog(), Sub(r)
                                                         Assert.Equal(0, MemberCount(r, "access"))
                                                         Assert.DoesNotContain(WorkerPassword, r, StringComparison.Ordinal)
                                                         Assert.DoesNotContain("FMSSFTP", r, StringComparison.Ordinal)
                                                     End Sub)
                    End Using
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ the reason follows the plan

    <Fact>
    Public Async Function The_reason_shown_follows_the_plan_when_the_directory_settles_later_and_a_PIN_lock_still_publishes_nothing() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = {"directory", "tunnel", "pairing", ExchangeSealedNames.FeatureSealed}
            ' the directory does not list this device yet: the first plan says "no key of its own"
            Using kit As New ExchangePublisherTests.Kit()
                kit.Settings.SetPublishShares(True)
                kit.PinLocked = True
                Using agent As ExchangeDeviceAgent = kit.Rig.NewAgent()
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(kit.Rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Block = ExchangePublishBlock.NoOwnKey))

                    ' the server lists the device now; no share moved, only the reason: the PIN lock is named, nothing is sealed or published
                    acc.ListEnrolledDevices = True
                    Await acc.PushDirectoryAsync()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() kit.Publisher.Snapshot.Block = ExchangePublishBlock.PinLocked))
                    Await Task.Delay(300)
                    Assert.Equal(0, acc.CountOf("publish"))
                    Assert.Empty(acc.PublishLog())

                    ' the lock is lifted: the folders go out, sealed
                    kit.PinLocked = False
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                    Assert.Equal(ExchangePublishBlock.None, kit.Publisher.Snapshot.Block)
                    Assert.All(acc.PublishLog(), Sub(r)
                                                     Assert.Equal(0, MemberCount(r, "access"))
                                                     Assert.Equal(1, MemberCount(r, "sealedAccess"))
                                                 End Sub)
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ spec 198 rule 4

    <Fact>
    Public Async Function No_secret_and_no_box_reaches_a_log_line_of_a_sealed_publish() As Task
        Using phone As New Phone("P"c)
            Using acc As New ExchangeStubAccount()
                acc.Devices.Add(phone.Record())
                Using kit As New ExchangePublisherTests.Kit()
                    kit.Settings.SetPublishShares(True)
                    Using agent As ExchangeDeviceAgent = Await ExchangePublisherTests.EnrollOnlineAsync(kit, acc)
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.PublishedNow().Count = 2))
                        Dim photos As String = ExchangePublisherTests.Latest(acc, "Photos")
                        Dim box As String = JsonDocument.Parse(photos).RootElement.GetProperty("sealedAccess").GetRawText()
                        Dim everything As String = kit.Rig.Everything()
                        Assert.DoesNotContain(WorkerPassword, everything, StringComparison.Ordinal)
                        Assert.DoesNotContain("FMSSFTP", everything, StringComparison.Ordinal)
                        Assert.DoesNotContain(box, everything, StringComparison.Ordinal)
                        Assert.DoesNotContain("sealedAccess", everything, StringComparison.Ordinal)
                        ' (the phone's `kem` is a PUBLIC key the directory cache keeps on purpose, so it is not asserted here)
                    End Using
                End Using
            End Using
        End Using
    End Function

End Class
