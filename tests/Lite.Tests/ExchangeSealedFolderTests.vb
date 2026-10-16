#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' Spec 234 slice D3, DEVICE-EXCHANGE section 27 items DI (sealed `access` of a folder and the host key), DJ (the guest's granted record),
' DG (which device key is trusted), DP (no clear fallback through the server) and DR (the four states). The pure half is
' ExchangeSealedAccess.vb: the producer seals the `access` object (SealAccess), the consumer's agent opens it (Resolve); the viewer's half is
' ExchangeSync (attach and follow), which maps the agent's typed refusals to an outcome and checks the opened descriptor's host key against the
' record's own `hostKeyFingerprint`. Real keys and real boxes; a fake directory per device; no session, no socket, no DPAPI.

Public Class ExchangeSealedFolderTests

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const OtherFp As String = "SHA256:9f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const Password As String = "k7PmQ2wXr9TzS4vGnHb3JdLe"
    Private Const Rid As String = "EBESExQVFhcYGRobHB0eHw"
    Private Const Rid2 As String = "FBUWFxgZGhscHR4fICEiIw"
    Private Const Gid As String = "q3Vb7YtK0xP2mN9sLfR4wA"
    Private Const Gid2 As String = "r4Wc8ZuL1yQ3nO0tMgS5xQ"

    ' ---------------------------------------------------------------- helpers

    ''' <summary>One device of the account: its id, its key, the directory it sees and the view it derives from it.</summary>
    Private NotInheritable Class Peer
        Implements IDisposable

        Public ReadOnly Id As String
        Public ReadOnly Key As ExchangeDeviceKey
        Public ReadOnly Trust As IExchangeSealedTrustStore = New ExchangeMemorySealedTrustStore()
        ''' <summary>The `kem` the OTHER devices see for this one ("" = the record carries no `e2eKey`).</summary>
        Public Advertised As String
        ''' <summary>Whether the other devices see the capability `e2e-v1` on this one.</summary>
        Public Capable As Boolean = True
        Public Features As IReadOnlyList(Of String) = New String() {"directory", "sealed"}
        Public Snapshot As ExchangeDirectorySnapshot = ExchangeDirectorySnapshot.Empty
        Public ReadOnly View As ExchangeSealedRecipients

        Public Sub New(letter As Char, Optional withKey As Boolean = True)
            Id = ExchangeWireTestKit.IdOf(letter)
            Key = If(withKey, ExchangeDeviceKey.Generate(), Nothing)
            Advertised = If(Key Is Nothing, "", Key.Kem)
            View = New ExchangeSealedRecipients(Id, Advertised, Trust, Function() Snapshot, Function() Features)
        End Sub

        Public Function Rec() As String
            Dim o As New JsonObject()
            o("deviceId") = Id
            o("deviceName") = "Device " & Id.Substring(0, 1)
            o("product") = "fms-windows"
            o("presence") = "online"
            If Capable Then
                Dim caps As New JsonArray()
                caps.Add(JsonValue.Create(ExchangeSealedNames.CapabilityE2e))
                o("capabilities") = caps
            End If
            If Advertised.Length > 0 Then
                Dim k As New JsonObject()
                k("v") = 1
                k("kem") = Advertised
                o("e2eKey") = k
            End If
            Return o.ToJsonString()
        End Function

        Public Sub Show(revision As Long, ParamArray devices As String())
            Snapshot = ExchangeDirectorySnapshot.FromLocalJson("{""revision"":" & revision.ToString(Globalization.CultureInfo.InvariantCulture) &
                                                               ",""devices"":[" & String.Join(",", devices) & "]}")
            View.Refresh()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Key IsNot Nothing Then Key.Dispose()
        End Sub
    End Class

    ''' <summary>One account: a producer P, a consumer C and a third device T, each seeing the others as they advertise themselves.</summary>
    Private NotInheritable Class World
        Implements IDisposable

        Public ReadOnly P As Peer
        Public ReadOnly C As New Peer("C"c)
        Public ReadOnly T As New Peer("T"c)
        Private _revision As Long

        ''' <param name="producerHasNoKey">The producer is a device that never had a key (an older build): its record shows no `e2eKey`
        ''' and no capability from the first sight, which is not the same as a key that went away.</param>
        Public Sub New(Optional producerHasNoKey As Boolean = False)
            P = New Peer("P"c, Not producerHasNoKey)
            If producerHasNoKey Then P.Capable = False
            Show()
        End Sub

        ''' <summary>Every device reads the directory again (the revision moves).</summary>
        Public Sub Show()
            _revision += 1
            ' the loop variable must not be named p: VB is case-insensitive, so it would shadow the field P even inside the collection expression
            For Each peer As Peer In {P, C, T}
                peer.Show(_revision, P.Rec(), C.Rec(), T.Rec())
            Next
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            P.Dispose()
            C.Dispose()
            T.Dispose()
        End Sub
    End Class

    Private Shared Function AccessJson(Optional fp As String = Fp) As String
        Dim json As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", Password, fp, "", "", False), "/Photos", json))
        Return json
    End Function

    Private Shared Function DescriptorOf(accessJson As String) As String
        Using doc As JsonDocument = JsonDocument.Parse(accessJson)
            Return doc.RootElement.GetProperty("descriptor").GetString()
        End Using
    End Function

    Private Shared Function RecipientsOf(ParamArray peers As Peer()) As List(Of ExchangeSealedRecipient)
        Return peers.Select(Function(p) New ExchangeSealedRecipient(p.Id, p.Key.Kem)).ToList()
    End Function

    ''' <summary>The producer's box for the `access` of one folder, addressed to <paramref name="recipients"/>.</summary>
    Private Shared Function SealFor(sender As Peer, resourceId As String, access As String, ParamArray recipients As Peer()) As String
        Dim box As String = ""
        Assert.True(ExchangeSealedAccess.SealAccess(access, resourceId, sender.Id, sender.Key, RecipientsOf(recipients), box))
        Assert.NotEmpty(box)
        Return box
    End Function

    ''' <summary>The record as the producer publishes it (the publisher's own builder) and a reader parses it. `access` is "" for a sealed one.</summary>
    Private Shared Function RecordOf(resourceId As String, deviceId As String, clearAccess As String, box As String, fingerprint As String) As ExchangeResourceRecord
        Dim json As String = Nothing
        Assert.True(ExchangeResourceRecordBuilder.TryBuild(resourceId, deviceId, "Photos", clearAccess, "", Nothing, New DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc),
                                                           json, False, box, fingerprint, If(fingerprint.Length > 0, "/Photos", "")))
        Dim rec As ExchangeResourceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadResource(json, rec))
        Return rec
    End Function

    Private Shared Sub AssertRefused(result As ExchangeSealedAccessResult, expected As ConnectionDiagnosisMessage)
        Assert.NotNull(result)
        Assert.Same(expected, result.State)
        Assert.False(result.Opened)
        Assert.Equal("", result.AccessJson)
    End Sub

    Private Shared Sub AssertOpened(result As ExchangeSealedAccessResult, expectedAccess As String)
        Assert.NotNull(result)
        Assert.Null(result.State)
        Assert.True(result.Opened)
        Assert.True(ExchangeResourceAccess.SameAccess(expectedAccess, result.AccessJson, "Photos"))
    End Sub

    Private Shared Function Mutated(box As String, member As String) As String
        Dim node As JsonObject = CType(JsonNode.Parse(box), JsonObject)
        Dim text As String = node(member).GetValue(Of String)()
        node(member) = If(text(0) = "A"c, "B", "A") & text.Substring(1)
        Return node.ToJsonString()
    End Function

    ' ---------------------------------------------------------------- DI: the producer seals, a device of the account opens

    <Fact>
    Public Sub A_sealed_folder_round_trips_between_two_devices_and_the_record_has_no_clear_access()
        Using w As New World()
            Dim access As String = AccessJson()
            Dim box As String = SealFor(w.P, Rid, access, w.P, w.C)
            Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", box, Fp)

            Assert.False(record.HasAccess)                  ' item DI: `sealedAccess` in place of `access`
            Assert.True(record.HasSealedAccess)
            Assert.Equal(Fp, record.HostKeyFingerprint)    ' the identity pair stays in clear (item BA)
            Using doc As JsonDocument = JsonDocument.Parse(record.Raw)
                Dim member As JsonElement = Nothing
                Assert.False(doc.RootElement.TryGetProperty("access", member))
                Assert.True(doc.RootElement.TryGetProperty("sealedAccess", member))
            End Using
            ' nothing of the descriptor, the password or the fingerprint is readable in the record that goes through the server
            For Each secret As String In {Password, DescriptorOf(access), "FMSSFTP", "192.168.1.23"}
                Assert.DoesNotContain(secret, record.Raw, StringComparison.Ordinal)
            Next

            AssertOpened(ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, w.C.Key), access)       ' the other device
            AssertOpened(ExchangeSealedAccess.Resolve(record, w.P.View, w.P.Trust, w.P.Key), access)       ' the producer is a recipient of its own entry
            Assert.Equal(Fp, ExchangeSealedAccess.FingerprintOf(access, "Photos"))
        End Using
    End Sub

    <Fact>
    Public Sub A_device_that_is_not_a_recipient_of_the_box_cannot_open_it()
        Using w As New World()
            Dim box As String = SealFor(w.P, Rid, AccessJson(), w.P, w.C)         ' T is in the account, but not in the box
            Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", box, Fp)
            AssertRefused(ExchangeSealedAccess.Resolve(record, w.T.View, w.T.Trust, w.T.Key), ConnectionDiagnosisMessages.SealedFailed)
        End Using
    End Sub

    <Fact>
    Public Sub A_box_read_under_another_resource_id_is_not_opened()
        Using w As New World()
            Dim box As String = SealFor(w.P, Rid, AccessJson(), w.P, w.C)
            ' the same box laid into the record of another folder: the context is built from the record's own id, never read from the box
            Dim moved As ExchangeResourceRecord = RecordOf(Rid2, w.P.Id, "", box, Fp)
            AssertRefused(ExchangeSealedAccess.Resolve(moved, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)
        End Using
    End Sub

    <Fact>
    Public Sub A_box_from_another_device_than_the_record_owner_is_not_opened()
        Using w As New World()
            Dim box As String = SealFor(w.T, Rid, AccessJson(), w.C)              ' T sealed it, the record says P published it
            Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", box, Fp)
            AssertRefused(ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)
        End Using
    End Sub

    <Fact>
    Public Sub A_box_whose_from_key_is_not_the_key_this_program_trusts_for_the_owner_is_not_opened()
        Using w As New World()
            Using impostor As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                ' the server made a box that names P as sender but carries its own key as `fromKey`
                Dim box As String = ""
                Assert.True(ExchangeSealedAccess.SealAccess(AccessJson(), Rid, w.P.Id, impostor, RecipientsOf(w.C), box))
                Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", box, Fp)
                AssertRefused(ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_tampered_ciphertext_or_nonce_and_a_box_that_does_not_parse_are_sealed_failed()
        Using w As New World()
            Dim box As String = SealFor(w.P, Rid, AccessJson(), w.P, w.C)
            AssertOpened(ExchangeSealedAccess.Resolve(RecordOf(Rid, w.P.Id, "", box, Fp), w.C.View, w.C.Trust, w.C.Key), AccessJson())
            For Each member As String In {"ct", "n"}
                Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", Mutated(box, member), Fp)
                AssertRefused(ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)
            Next
            ' a box that does not even parse
            Dim junk As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", "{""v"":1}", Fp)
            AssertRefused(ExchangeSealedAccess.Resolve(junk, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)
        End Using
    End Sub

    <Fact>
    Public Sub A_record_that_names_another_fingerprint_than_the_opened_descriptor_is_sealed_failed()
        Using w As New World()
            Dim access As String = AccessJson()
            Dim box As String = SealFor(w.P, Rid, access, w.P, w.C)
            ' item DI: the host key is checked against the OPENED descriptor; a record that says another one is not that resource
            Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", box, OtherFp)
            AssertRefused(ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)
            ' a record with no fingerprint of its own cannot differ
            AssertOpened(ExchangeSealedAccess.Resolve(RecordOf(Rid, w.P.Id, "", box, ""), w.C.View, w.C.Trust, w.C.Key), access)
        End Using
    End Sub

    ' ---------------------------------------------------------------- DG, DP, DR: the four states and no clear fallback

    <Fact>
    Public Sub A_clear_access_from_a_device_with_no_e2e_key_is_never_used_and_says_e2e_missing()
        Using w As New World(producerHasNoKey:=True)         ' the producer's record shows no `e2eKey` and no capability, from the first sight
            Assert.Equal(ExchangeSealState.E2eMissing, w.C.View.StateOf(w.P.Id).Value)
            Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, AccessJson(), "", Fp)
            Assert.True(record.HasAccess)
            AssertRefused(ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.E2eMissing)
        End Using
    End Sub

    <Fact>
    Public Sub A_clear_access_from_a_device_that_has_a_key_is_sealed_failed_and_from_a_key_changed_device_is_key_changed()
        Using w As New World()
            Dim clear As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, AccessJson(), "", Fp)
            ' P has a key and still sends clear text: it did not seal, which no conforming producer does
            AssertRefused(ExchangeSealedAccess.Resolve(clear, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)

            ' the key P shows now is another one than the first C saw (item DG): nothing from it is used, clear or boxed
            Using other As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                w.P.Advertised = other.Kem
                w.Show()
                Assert.Equal(ExchangeSealState.KeyChanged, w.C.View.StateOf(w.P.Id).Value)
                AssertRefused(ExchangeSealedAccess.Resolve(clear, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.KeyChanged)
                Dim boxed As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", SealFor(w.P, Rid, AccessJson(), w.C), Fp)
                AssertRefused(ExchangeSealedAccess.Resolve(boxed, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.KeyChanged)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_device_with_no_key_of_its_own_opens_nothing_and_says_e2e_missing()
        Using w As New World()
            Dim box As String = SealFor(w.P, Rid, AccessJson(), w.P, w.C)
            Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", box, Fp)
            AssertRefused(ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, Nothing), ConnectionDiagnosisMessages.E2eMissing)    ' no key loaded
            Using keyless As New Peer("K"c, withKey:=False)
                keyless.Show(1, keyless.Rec(), w.P.Rec())
                Assert.False(keyless.View.HasOwnKey)
                Using stray As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                    AssertRefused(ExchangeSealedAccess.Resolve(record, keyless.View, keyless.Trust, stray), ConnectionDiagnosisMessages.E2eMissing)
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_server_without_the_sealed_feature_is_server_no_e2e_for_a_box_and_for_a_clear_access()
        Using w As New World()
            Dim boxed As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", SealFor(w.P, Rid, AccessJson(), w.P, w.C), Fp)
            Dim clear As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, AccessJson(), "", Fp)
            w.C.Features = New String() {"directory", "tunnel"}
            w.C.View.Refresh()
            Assert.Equal("server-no-e2e", w.C.View.ServerMessage.Code)
            AssertRefused(ExchangeSealedAccess.Resolve(boxed, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.ServerNoE2e)
            AssertRefused(ExchangeSealedAccess.Resolve(clear, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.ServerNoE2e)
            ' the producer's side of the same fact: no recipient is offered, so nothing is sealed and nothing is published
            w.P.Features = New String() {"directory", "tunnel"}
            w.P.View.Refresh()
            Assert.Empty(w.P.View.Recipients())
            Dim sealedBox As String = "kept"
            Assert.False(ExchangeSealedAccess.SealAccess(AccessJson(), Rid, w.P.Id, w.P.Key, w.P.View.Recipients(), sealedBox))
            Assert.Equal("", sealedBox)
        End Using
    End Sub

    <Fact>
    Public Sub A_record_with_nothing_to_open_answers_no_state_and_a_missing_record_is_sealed_failed()
        Using w As New World()
            Dim offline As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", "", "")
            Dim none As ExchangeSealedAccessResult = ExchangeSealedAccess.Resolve(offline, w.C.View, w.C.Trust, w.C.Key)
            Assert.Null(none.State)                      ' the caller keeps its `no-access`
            Assert.False(none.Opened)
            AssertRefused(ExchangeSealedAccess.Resolve(Nothing, w.C.View, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.SealedFailed)
            AssertRefused(ExchangeSealedAccess.Resolve(RecordOf(Rid, w.P.Id, "", SealFor(w.P, Rid, AccessJson(), w.C), Fp), Nothing, w.C.Trust, w.C.Key), ConnectionDiagnosisMessages.E2eMissing)
        End Using
    End Sub

    <Fact>
    Public Sub SealAccess_refuses_what_it_cannot_seal_without_throwing_and_leaves_no_box()
        Using w As New World()
            Dim box As String = "kept"
            Assert.False(ExchangeSealedAccess.SealAccess("", Rid, w.P.Id, w.P.Key, RecipientsOf(w.C), box))
            Assert.Equal("", box)
            Assert.False(ExchangeSealedAccess.SealAccess(AccessJson(), Rid, w.P.Id, Nothing, RecipientsOf(w.C), box))
            Assert.False(ExchangeSealedAccess.SealAccess(AccessJson(), Rid, w.P.Id, w.P.Key, New List(Of ExchangeSealedRecipient), box))
            Assert.False(ExchangeSealedAccess.SealAccess(AccessJson(), Rid, w.P.Id, w.P.Key, Nothing, box))
            Assert.False(ExchangeSealedAccess.SealAccess(AccessJson(), "not an id", w.P.Id, w.P.Key, RecipientsOf(w.C), box))
            Assert.Equal("", box)
        End Using
    End Sub

    ' ---------------------------------------------------------------- DJ: the guest's granted record

    Private Shared Function GrantBox(owner As Peer, grantId As String, guest As Peer, access As String) As String
        Dim payload As Byte() = Encoding.UTF8.GetBytes("{""access"":" & access & "}")
        Dim sealedResult As ExchangeSealResult = ExchangeSealedBoxes.Seal(payload, ExchangeSealedContext.Grant(grantId), owner.Id, owner.Key,
                                                                          RecipientsOf(guest))
        Try
            Assert.True(sealedResult.Succeeded)
            Return ExchangeJson.Compact(sealedResult.Box.ToJson())
        Finally
            sealedResult.Wipe()
        End Try
    End Function

    Private Shared Function GrantedRecord(grantId As String, ownerDeviceId As String, box As String, clearAccess As String, fingerprint As String) As ExchangeResourceRecord
        Dim o As New JsonObject()
        o("resourceId") = Rid
        o("deviceId") = ownerDeviceId
        o("kind") = "sftp-share"
        o("name") = "Photos"
        o("grantId") = grantId
        If box.Length > 0 Then o("sealed") = JsonNode.Parse(box)
        If clearAccess.Length > 0 Then o("access") = JsonNode.Parse(clearAccess)
        If fingerprint.Length > 0 Then o("hostKeyFingerprint") = fingerprint
        Dim rec As ExchangeResourceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadResource(o.ToJsonString(), rec))
        Return rec
    End Function

    ''' <summary>A guest: one device of its own, which knows none of the owner's devices.</summary>
    Private Shared Function NewGuest() As Peer
        Dim g As New Peer("G"c)
        g.Show(1, g.Rec())
        Return g
    End Function

    <Fact>
    Public Sub A_guest_opens_a_granted_box_pins_the_first_from_key_and_refuses_a_different_one_later()
        Using owner As New Peer("O"c)
            Using owner2 As New Peer("Q"c)
                Using guest As Peer = NewGuest()
                    Dim access As String = AccessJson()
                    Dim slot As String = ExchangeSealedTrustSlots.ForGrant(Gid)

                    Dim first As ExchangeResourceRecord = GrantedRecord(Gid, owner.Id, GrantBox(owner, Gid, guest, access), "", Fp)
                    Assert.True(first.HasSealedAccess)
                    Assert.False(first.HasAccess)
                    AssertOpened(ExchangeSealedAccess.Resolve(first, guest.View, guest.Trust, guest.Key), access)
                    Assert.Equal(owner.Key.Kem, guest.Trust.TryGet(guest.Id, slot))        ' trust on first use, in the `grant:` key space
                    Assert.Equal("", guest.Trust.TryGet(guest.Id, owner.Id))                 ' never under a device id

                    ' a new box of the same grant from the same device (the guest's devices changed, item DJ) opens again
                    Dim again As ExchangeResourceRecord = GrantedRecord(Gid, owner.Id, GrantBox(owner, Gid, guest, access), "", Fp)
                    AssertOpened(ExchangeSealedAccess.Resolve(again, guest.View, guest.Trust, guest.Key), access)

                    ' a later box of the same grant with another `fromKey` is not opened: key-changed, and the pin stays
                    Dim moved As ExchangeResourceRecord = GrantedRecord(Gid, owner2.Id, GrantBox(owner2, Gid, guest, access), "", Fp)
                    AssertRefused(ExchangeSealedAccess.Resolve(moved, guest.View, guest.Trust, guest.Key), ConnectionDiagnosisMessages.KeyChanged)
                    Assert.Equal(owner.Key.Kem, guest.Trust.TryGet(guest.Id, slot))

                    ' another grant has a pin of its own
                    Dim other As ExchangeResourceRecord = GrantedRecord(Gid2, owner2.Id, GrantBox(owner2, Gid2, guest, access), "", Fp)
                    AssertOpened(ExchangeSealedAccess.Resolve(other, guest.View, guest.Trust, guest.Key), access)
                    Assert.Equal(owner2.Key.Kem, guest.Trust.TryGet(guest.Id, ExchangeSealedTrustSlots.ForGrant(Gid2)))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_granted_box_that_does_not_open_pins_nothing()
        Using owner As New Peer("O"c)
            Using guest As Peer = NewGuest()
                Dim access As String = AccessJson()
                Dim slot As String = ExchangeSealedTrustSlots.ForGrant(Gid)
                Dim box As String = GrantBox(owner, Gid, guest, access)

                ' tampered: nothing is kept
                AssertRefused(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, Mutated(box, "ct"), "", Fp), guest.View, guest.Trust, guest.Key), ConnectionDiagnosisMessages.SealedFailed)
                Assert.Equal("", guest.Trust.TryGet(guest.Id, slot))
                ' the box of another grant under this grant's id (wrong context): nothing is kept
                Dim wrongContext As String = GrantBox(owner, Gid2, guest, access)
                AssertRefused(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, wrongContext, "", Fp), guest.View, guest.Trust, guest.Key), ConnectionDiagnosisMessages.SealedFailed)
                Assert.Equal("", guest.Trust.TryGet(guest.Id, slot))
                ' another fingerprint than the opened descriptor's: refused, and the first box's key is not kept either
                AssertRefused(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, box, "", OtherFp), guest.View, guest.Trust, guest.Key), ConnectionDiagnosisMessages.SealedFailed)
                Assert.Equal("", guest.Trust.TryGet(guest.Id, slot))
                ' no store to keep a pin in: not opened at all
                AssertRefused(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, box, "", Fp), guest.View, Nothing, guest.Key), ConnectionDiagnosisMessages.SealedFailed)
                ' a device that is not an addressee of the box
                Using stranger As New Peer("S"c)
                    stranger.Show(1, stranger.Rec())
                    AssertRefused(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, box, "", Fp), stranger.View, stranger.Trust, stranger.Key), ConnectionDiagnosisMessages.SealedFailed)
                    Assert.Equal("", stranger.Trust.TryGet(stranger.Id, slot))
                End Using
                ' and the box still opens for the guest it was made for
                AssertOpened(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, box, "", Fp), guest.View, guest.Trust, guest.Key), access)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_granted_record_with_a_clear_access_and_no_box_is_e2e_missing()
        Using owner As New Peer("O"c)
            Using guest As Peer = NewGuest()
                Dim record As ExchangeResourceRecord = GrantedRecord(Gid, owner.Id, "", AccessJson(), Fp)
                AssertRefused(ExchangeSealedAccess.Resolve(record, guest.View, guest.Trust, guest.Key), ConnectionDiagnosisMessages.E2eMissing)
            End Using
        End Using
    End Sub

    ' ---------------------------------------------------------------- DJ: the giver (a device of the owning account)

    ''' <summary>A member's `grants` record as the server lists it (item DJ): the grant, the guest login, and the guest account's `devices`
    ''' (public values, each `{deviceId, deviceName, e2eKey}`). The payload is never in it.</summary>
    Private Shared Function GrantsRecord(ParamArray guestDevices As String()) As String
        Return "{""grantId"":""" & Gid & """,""kind"":""resource"",""guest"":""friend"",""resourceId"":""" & Rid & """,""devices"":[" & String.Join(",", guestDevices) & "]}"
    End Function

    Private Shared Function WireOf(frame As ExchangeEnvelope) As JsonObject
        Return CType(JsonNode.Parse(Encoding.UTF8.GetString(ExchangeEnvelopeCodec.Encode(frame))), JsonObject)
    End Function

    <Fact>
    Public Sub A_grant_carries_a_sealed_box_for_the_guests_devices_and_never_a_clear_access()
        Using owner As New Peer("O"c)
            Using guest As Peer = NewGuest()
                Using guest2 As New Peer("H"c)
                    Dim access As String = AccessJson()
                    Dim frame As ExchangeEnvelope = Nothing
                    Assert.True(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, GrantsRecord(guest.Rec(), guest2.Rec()), owner.Id, owner.Key, frame))

                    Dim wire As JsonObject = WireOf(frame)
                    Assert.Equal("grant", wire("type").GetValue(Of String)())
                    Assert.Equal(Gid, wire("grantId").GetValue(Of String)())
                    Assert.Equal("friend", wire("guest").GetValue(Of String)())
                    Assert.Equal(Rid, wire("resourceId").GetValue(Of String)())
                    Assert.NotNull(wire("sealed"))
                    Assert.False(wire.ContainsKey("access"))                                        ' item DJ: `sealed` IN PLACE OF `access`
                    Assert.False(wire.ContainsKey("credentials"))
                    Dim text As String = wire.ToJsonString()
                    Assert.DoesNotContain(Password, text, StringComparison.Ordinal)
                    Assert.DoesNotContain("FMSSFTP", text, StringComparison.Ordinal)
                    Assert.DoesNotContain(DescriptorOf(access), text, StringComparison.Ordinal)

                    ' the box is addressed to both guest devices, under the context `grant:<grantId>`, and the sender is the giver
                    Dim box As ExchangeSealedBox = Nothing
                    Assert.True(ExchangeSealedBox.TryParse(wire("sealed").ToJsonString(), box))
                    Assert.Equal(owner.Id, box.FromDeviceId)
                    Assert.Equal({guest.Id, guest2.Id}.OrderBy(Function(s) s, StringComparer.Ordinal).ToArray(),
                                 box.Keys.Select(Function(k) k.ToDeviceId).OrderBy(Function(s) s, StringComparer.Ordinal).ToArray())

                    ' the guest opens it through the consumer's own code path and pins the giver's key for THIS grant
                    Dim record As ExchangeResourceRecord = GrantedRecord(Gid, owner.Id, wire("sealed").ToJsonString(), "", Fp)
                    AssertOpened(ExchangeSealedAccess.Resolve(record, guest.View, guest.Trust, guest.Key), access)
                    Assert.Equal(owner.Key.Kem, guest.Trust.TryGet(guest.Id, ExchangeSealedTrustSlots.ForGrant(Gid)))
                    ' the same box under another grant id is not this grant's box
                    AssertRefused(ExchangeSealedAccess.Resolve(GrantedRecord(Gid2, owner.Id, wire("sealed").ToJsonString(), "", Fp), guest.View, guest.Trust, guest.Key), ConnectionDiagnosisMessages.SealedFailed)
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_grant_to_a_guest_whose_devices_hold_no_key_is_not_sent_at_all_and_never_in_clear()
        Using owner As New Peer("O"c)
            Using noKey As New Peer("N"c, withKey:=False)
                Dim access As String = AccessJson()
                Dim frame As ExchangeEnvelope = Nothing
                ' a guest device without `e2eKey` is no recipient; with no other device there is nobody to seal to, and no clear `access` in its place
                Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, GrantsRecord(noKey.Rec()), owner.Id, owner.Key, frame))
                Assert.Null(frame)
                Assert.Empty(ExchangeSealedAccess.GuestRecipientsOf(GrantsRecord(noKey.Rec())))
                ' no `devices` at all, a record that is no object, a descriptor that is no object, a giver without a key, a grant id that is no id
                Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, GrantsRecord(), owner.Id, owner.Key, frame))
                Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, "[]", owner.Id, owner.Key, frame))
                Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, "not json", owner.Id, owner.Key, frame))
                Using guest As Peer = NewGuest()
                    Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, "[1]", GrantsRecord(guest.Rec()), owner.Id, owner.Key, frame))
                    Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, "", GrantsRecord(guest.Rec()), owner.Id, owner.Key, frame))
                    Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, GrantsRecord(guest.Rec()), owner.Id, Nothing, frame))
                    Assert.False(ExchangeSealedAccess.TryGrantFrame("short", "friend", Rid, access, GrantsRecord(guest.Rec()), owner.Id, owner.Key, frame))
                    Assert.Null(frame)
                    ' a keyless device beside a keyed one: the box goes to the keyed one only
                    Dim only As IReadOnlyList(Of ExchangeSealedRecipient) = ExchangeSealedAccess.GuestRecipientsOf(GrantsRecord(noKey.Rec(), guest.Rec()))
                    Assert.Equal({guest.Id}, only.Select(Function(r) r.DeviceId).ToArray())
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_device_id_listed_twice_with_two_keys_seals_to_nobody()
        Using owner As New Peer("O"c)
            Using guest As Peer = NewGuest()
                Using impostor As New Peer("H"c)
                    impostor.Advertised = impostor.Key.Kem
                    ' the same deviceId carrying a second key: a record the giver does not seal to
                    Dim twin As String = guest.Rec().Replace(guest.Key.Kem, impostor.Key.Kem, StringComparison.Ordinal)
                    Assert.Empty(ExchangeSealedAccess.GuestRecipientsOf(GrantsRecord(guest.Rec(), twin)))
                    Dim frame As ExchangeEnvelope = Nothing
                    Assert.False(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, AccessJson(), GrantsRecord(guest.Rec(), twin), owner.Id, owner.Key, frame))
                    ' the same record twice is one recipient
                    Assert.Single(ExchangeSealedAccess.GuestRecipientsOf(GrantsRecord(guest.Rec(), guest.Rec())))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Replacing_a_grants_box_after_the_guests_devices_change_keeps_the_pin_and_opens_on_the_new_device()
        Using owner As New Peer("O"c)
            Using guest As Peer = NewGuest()
                Using guest2 As New Peer("H"c)
                    Dim access As String = AccessJson()
                    Dim first As ExchangeEnvelope = Nothing
                    Assert.True(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, GrantsRecord(guest.Rec()), owner.Id, owner.Key, first))
                    AssertOpened(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, WireOf(first)("sealed").ToJsonString(), "", Fp), guest.View, guest.Trust, guest.Key), access)

                    ' a device joins the guest account: the SAME grantId with a new box (the replacing `grant` of item DJ)
                    Dim second As ExchangeEnvelope = Nothing
                    Assert.True(ExchangeSealedAccess.TryGrantFrame(Gid, "friend", Rid, access, GrantsRecord(guest.Rec(), guest2.Rec()), owner.Id, owner.Key, second))
                    Assert.Equal(WireOf(first)("grantId").GetValue(Of String)(), WireOf(second)("grantId").GetValue(Of String)())
                    Dim replaced As String = WireOf(second)("sealed").ToJsonString()
                    AssertOpened(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, replaced, "", Fp), guest.View, guest.Trust, guest.Key), access)       ' the pin held
                    guest2.Show(1, guest2.Rec())
                    AssertOpened(ExchangeSealedAccess.Resolve(GrantedRecord(Gid, owner.Id, replaced, "", Fp), guest2.View, guest2.Trust, guest2.Key), access)    ' its own first use
                    Assert.Equal(owner.Key.Kem, guest2.Trust.TryGet(guest2.Id, ExchangeSealedTrustSlots.ForGrant(Gid)))
                End Using
            End Using
        End Using
    End Sub

    ' ---------------------------------------------------------------- the pipe and the viewer

    <Fact>
    Public Sub The_four_states_travel_the_pipe_as_their_own_codes_and_nothing_else_is_a_sealed_state()
        Assert.Same(ConnectionDiagnosisMessages.E2eMissing, ExchangeAgentPipe.SealedMessageOf(ExchangeAgentPipe.ErrorE2eMissing))
        Assert.Same(ConnectionDiagnosisMessages.KeyChanged, ExchangeAgentPipe.SealedMessageOf(ExchangeAgentPipe.ErrorKeyChanged))
        Assert.Same(ConnectionDiagnosisMessages.ServerNoE2e, ExchangeAgentPipe.SealedMessageOf(ExchangeAgentPipe.ErrorServerNoE2e))
        Assert.Same(ConnectionDiagnosisMessages.SealedFailed, ExchangeAgentPipe.SealedMessageOf(ExchangeAgentPipe.ErrorSealedFailed))
        For Each message As ConnectionDiagnosisMessage In {ConnectionDiagnosisMessages.E2eMissing, ConnectionDiagnosisMessages.KeyChanged,
                                                           ConnectionDiagnosisMessages.ServerNoE2e, ConnectionDiagnosisMessages.SealedFailed}
            Assert.Same(message, ExchangeAgentPipe.SealedMessageOf(message.Code))          ' the token IS the code
        Next
        Assert.Null(ExchangeAgentPipe.SealedMessageOf(ExchangeAgentPipe.ErrorNoAccess))
        Assert.Null(ExchangeAgentPipe.SealedMessageOf(""))
        Assert.Null(ExchangeAgentPipe.SealedMessageOf("update-the-program"))
    End Sub

    Private Shared Function AccessResultOf(fp As String, Optional host As String = "192.168.1.23") As ExchangeAgentAccessResult
        Dim json As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(New FmsSftpCode({host}, 2022, "fms", Password, fp, "", "", False), "/Camera", json))
        Return New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", json), json)
    End Function

    Private Shared Function RefusalOf(token As String) As ExchangeAgentAccessResult
        Return New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Refused, token, ""), "")
    End Function

    Private Shared Function ListedRecord(fingerprint As String) As ExchangeResourceRecord
        Return New ExchangeResourceRecord("{}", Rid, Gid, "sftp-share", "Camera", "online", "", hostKeyFingerprint:=fingerprint)
    End Function

    <Theory>
    <InlineData("e2e-missing", "E2eMissing")>
    <InlineData("key-changed", "KeyChanged")>
    <InlineData("server-no-e2e", "ServerNoE2e")>
    <InlineData("sealed-failed", "SealedFailed")>
    <InlineData("no-access", "NoAccess")>
    <InlineData("unknown-resource", "NoAccess")>
    <InlineData("a-token-of-a-newer-agent", "Unusable")>
    Public Async Function The_viewer_maps_each_refusal_of_the_agent_to_its_own_outcome_and_stores_nothing(token As String, expected As String) As Task
        Dim api As New ExchangeSyncTests.FakeApi()
        api.Access(Rid) = RefusalOf(token)
        Dim reg As New ExchangeSyncTests.Registry()
        Dim outcome As ExchangeAttachOutcome = Await reg.Sync(api).AttachAsync(ListedRecord(Fp), "Home PC", CancellationToken.None)
        Assert.Equal(expected, outcome.ToString())
        Assert.Equal(0, reg.Commits)
        Assert.Empty(reg.Items)
    End Function

    <Fact>
    Public Sub Each_sealed_outcome_has_its_own_sentence_from_the_common_message_table_and_none_is_the_generic_failure()
        Dim sentences As New Dictionary(Of ExchangeAttachOutcome, String) From {
            {ExchangeAttachOutcome.E2eMissing, ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.E2eMissing)},
            {ExchangeAttachOutcome.KeyChanged, ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.KeyChanged)},
            {ExchangeAttachOutcome.ServerNoE2e, ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.ServerNoE2e)},
            {ExchangeAttachOutcome.SealedFailed, ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.SealedFailed)}}
        Dim generic As String = ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.NotStored)
        Assert.Equal(ExchangeResourceText.SealedText(ConnectionDiagnosisMessages.E2eMissing), sentences(ExchangeAttachOutcome.E2eMissing))
        Assert.Equal(ExchangeResourceText.SealedText(ConnectionDiagnosisMessages.KeyChanged), sentences(ExchangeAttachOutcome.KeyChanged))
        Assert.Equal(ExchangeResourceText.SealedText(ConnectionDiagnosisMessages.ServerNoE2e), sentences(ExchangeAttachOutcome.ServerNoE2e))
        Assert.Equal(ExchangeResourceText.SealedText(ConnectionDiagnosisMessages.SealedFailed), sentences(ExchangeAttachOutcome.SealedFailed))
        Assert.Equal(4, sentences.Values.Distinct(StringComparer.Ordinal).Count())
        For Each pair As KeyValuePair(Of ExchangeAttachOutcome, String) In sentences
            Assert.False(String.IsNullOrWhiteSpace(pair.Value))
            Assert.NotEqual(generic, pair.Value)
            Assert.DoesNotContain("-e2e", pair.Value, StringComparison.Ordinal)           ' the sentence, never the raw code
            Assert.DoesNotContain("sealed-failed", pair.Value, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Async Function The_viewer_attaches_nothing_when_the_opened_descriptor_has_another_fingerprint_than_the_record() As Task
        Dim api As New ExchangeSyncTests.FakeApi()
        Dim reg As New ExchangeSyncTests.Registry()
        api.Access(Rid) = AccessResultOf(OtherFp)             ' the agent (or something posing as it) handed on another host key
        Assert.Equal(ExchangeAttachOutcome.SealedFailed, Await reg.Sync(api).AttachAsync(ListedRecord(Fp), "Home PC", CancellationToken.None))
        Assert.Equal(0, reg.Commits)
        Assert.Empty(reg.Items)

        api.Access(Rid) = AccessResultOf(Fp)                  ' the record and the opened descriptor agree
        Assert.Equal(ExchangeAttachOutcome.Attached, Await reg.Sync(api).AttachAsync(ListedRecord(Fp), "Home PC", CancellationToken.None))
        Assert.Equal(1, reg.Commits)
        Assert.Equal(Fp, reg.Items.Single().PinnedFingerprint)
    End Function

    <Fact>
    Public Async Function Following_does_not_merge_an_access_whose_fingerprint_differs_from_the_record() As Task
        Dim api As New ExchangeSyncTests.FakeApi()
        Dim reg As New ExchangeSyncTests.Registry()
        api.Access(Rid) = AccessResultOf(Fp)
        Assert.Equal(ExchangeAttachOutcome.Attached, Await reg.Sync(api).AttachAsync(ListedRecord(Fp), "Home PC", CancellationToken.None))
        Dim attached As ResourceDescriptor = reg.Items.Single()

        Dim devices As New List(Of ExchangeDeviceRecord)
        Dim resources As New List(Of ExchangeResourceRecord) From {ListedRecord(Fp)}
        api.Snapshot = New ExchangeDirectorySnapshot(5, devices, resources, New List(Of ExchangeBroadcastRecord))

        api.Access(Rid) = AccessResultOf(OtherFp, host:="192.168.1.77")
        Dim refused As ExchangeFollowOutcome = Await reg.Sync(api).FollowOneAsync(attached, "Home PC", CancellationToken.None)
        Assert.False(refused.Changed)
        Assert.Same(attached, refused.Descriptor)             ' not merged: the kept access, the kept pin
        Assert.Equal(1, reg.Commits)
        Assert.Equal("192.168.1.23", reg.Items.Single().Host)

        api.Access(Rid) = AccessResultOf(Fp, host:="192.168.1.77")      ' the same resource at another address is followed as before
        Dim followed As ExchangeFollowOutcome = Await reg.Sync(api).FollowOneAsync(attached, "Home PC", CancellationToken.None)
        Assert.True(followed.Changed)
        Assert.Equal("192.168.1.77", reg.Items.Single().Host)
    End Function

    ' ---------------------------------------------------------------- spec 198 rule 4: no secret in a description or an exception

    <Fact>
    Public Sub No_secret_reaches_a_description_a_state_or_the_text_of_a_failure()
        Using w As New World()
            Dim access As String = AccessJson()
            Dim box As String = SealFor(w.P, Rid, access, w.P, w.C)
            Dim record As ExchangeResourceRecord = RecordOf(Rid, w.P.Id, "", box, Fp)
            Dim opened As ExchangeSealedAccessResult = ExchangeSealedAccess.Resolve(record, w.C.View, w.C.Trust, w.C.Key)
            Assert.True(opened.Opened)
            Dim parsed As ExchangeSealedBox = Nothing
            Assert.True(ExchangeSealedBox.TryParse(box, parsed))

            Dim texts As New List(Of String) From {opened.ToString(), parsed.ToString(), w.C.View.ToString(), box}
            texts.Add(ExchangeSealedAccess.Resolve(RecordOf(Rid, w.P.Id, "", Mutated(box, "ct"), Fp), w.C.View, w.C.Trust, w.C.Key).ToString())
            texts.Add(ExchangeSealedAccess.Resolve(RecordOf(Rid, w.P.Id, "", box, OtherFp), w.C.View, w.C.Trust, w.C.Key).ToString())
            texts.Add(ExchangeSealedAccess.Resolve(Nothing, w.C.View, w.C.Trust, w.C.Key).ToString())
            For Each text As String In texts
                For Each secret As String In {Password, DescriptorOf(access), "FMSSFTP", Fp, OtherFp}
                    Assert.DoesNotContain(secret, text, StringComparison.Ordinal)
                Next
            Next
            Assert.Equal("ExchangeSealedAccessResult opened", opened.ToString())
            Assert.Equal("ExchangeSealedBox", parsed.ToString())
            Assert.Equal("ExchangeSealedAccessResult sealed-failed", texts(4))
            Assert.Equal("ExchangeSealedAccessResult sealed-failed", texts(5))
        End Using
    End Sub

End Class
#End If
