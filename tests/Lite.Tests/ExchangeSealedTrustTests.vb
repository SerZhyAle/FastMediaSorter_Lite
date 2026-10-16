#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json.Nodes
Imports Xunit

' Spec 234 slice C3, DEVICE-EXCHANGE section 27 items DF, DG, DP and DR: the client's side of "who may I seal to". The device key is
' covered by ExchangeSealedBoxTests (vectors, key store); this file holds the trust on first use (ExchangeSealedTrust.vb), the
' per-device states and the recipient list (ExchangeSealedRecipients.vb), with an in-memory trust store and a fake directory
' snapshot. Nothing here needs a session, a socket or DPAPI.

Public Class ExchangeSealedTrustTests

    ' ---------------------------------------------------------------- helpers

    Private Shared Function NewKem() As String
        Using k As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Return k.Kem
        End Using
    End Function

    ''' <summary>One device record as the server lists it. kem "" = no e2eKey member; capable False = no capability.</summary>
    Private Shared Function Rec(id As String, name As String, kem As String, Optional capable As Boolean = True,
                                Optional online As Boolean = True, Optional lastSeen As String = "") As String
        Dim o As New JsonObject()
        o("deviceId") = id
        o("deviceName") = name
        o("product") = "fms-android"
        o("presence") = If(online, "online", "offline")
        If lastSeen.Length > 0 Then o("lastSeen") = lastSeen
        If capable Then
            Dim caps As New JsonArray()
            caps.Add(JsonValue.Create("pin-v1"))
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

    Private Shared Function Snap(revision As Long, ParamArray devices As String()) As ExchangeDirectorySnapshot
        Return ExchangeDirectorySnapshot.FromLocalJson("{""revision"":" & revision.ToString(Globalization.CultureInfo.InvariantCulture) &
                                                       ",""devices"":[" & String.Join(",", devices) & "]}")
    End Function

    ''' <summary>One entry: this program's own id and key, a store, a directory that the test moves, the features of a connection.</summary>
    Private NotInheritable Class Rig
        Public ReadOnly OwnId As String
        Public ReadOnly OwnKem As String
        Public ReadOnly Trust As IExchangeSealedTrustStore
        Public Snapshot As ExchangeDirectorySnapshot = ExchangeDirectorySnapshot.Empty
        ''' <summary>Nothing = not connected (no welcome yet).</summary>
        Public Features As IReadOnlyList(Of String) = New String() {"directory", "sealed"}
        Public ReadOnly Recipients As ExchangeSealedRecipients
        Public Changes As Integer

        Public Sub New(Optional trust As IExchangeSealedTrustStore = Nothing, Optional withOwnKey As Boolean = True, Optional ownId As String = "")
            Me.OwnId = If(ownId.Length > 0, ownId, ExchangeWireTestKit.IdOf("O"c))
            Me.OwnKem = If(withOwnKey, NewKem(), "")
            Me.Trust = If(trust, New ExchangeMemorySealedTrustStore())
            Recipients = New ExchangeSealedRecipients(Me.OwnId, Me.OwnKem, Me.Trust, Function() Snapshot, Function() Features)
            AddHandler Recipients.Changed, Sub(s, e) Changes += 1
        End Sub

        Public Function SelfRec() As String
            Return Rec(OwnId, "This PC", OwnKem)
        End Function

        Public Sub Show(revision As Long, ParamArray devices As String())
            Snapshot = Snap(revision, devices)
            Recipients.Refresh()
        End Sub

        Public Function StateOf(id As String) As ExchangeSealState
            Dim s As Nullable(Of ExchangeSealState) = Recipients.StateOf(id)
            Assert.True(s.HasValue, "the device is not listed")
            Return s.Value
        End Function

        Public Function Ids(Optional includeSelf As Boolean = False) As String()
            Return Recipients.Recipients(includeSelf).Select(Function(x) x.DeviceId).ToArray()
        End Function
    End Class

    Private Shared ReadOnly Phone As String = ExchangeWireTestKit.IdOf("P"c)
    Private Shared ReadOnly Tv As String = ExchangeWireTestKit.IdOf("T"c)

    ' ---------------------------------------------------------------- DG: trust on first use, key-changed

    <Fact>
    Public Sub A_device_with_the_capability_and_a_valid_key_is_a_recipient_and_its_key_is_kept_on_first_sight()
        Dim r As New Rig()
        Dim phoneKem As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", phoneKem))

        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal(phoneKem, r.Trust.TryGet(r.OwnId, Phone))        ' the first key seen is kept under this entry
        Assert.Equal(phoneKem, r.Recipients.TrustedKeyOf(Phone))
        Assert.Equal({Phone}, r.Ids())
        Dim only As ExchangeSealedRecipient = r.Recipients.Recipients(False).Single()
        Assert.Equal(phoneKem, only.Kem)
        Assert.Null(r.Recipients.Devices().Single(Function(d) d.DeviceId = Phone).Message)   ' a ready device carries no row
    End Sub

    <Fact>
    Public Sub A_changed_key_is_key_changed_nothing_is_sealed_to_it_and_the_kept_key_is_never_replaced()
        Dim r As New Rig()
        Dim first As String = NewKem()
        Dim second As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", first))
        Dim revisionBefore As Long = r.Recipients.Revision
        Dim changesBefore As Integer = r.Changes

        r.Show(2, r.SelfRec(), Rec(Phone, "Phone", second))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(Phone))
        Assert.Equal("key-changed", r.Recipients.Devices().Single(Function(d) d.DeviceId = Phone).Message.Code)
        Assert.Equal("", r.Recipients.TrustedKeyOf(Phone))            ' a box from it is not opened either
        Assert.Empty(r.Ids())
        Assert.Equal(first, r.Trust.TryGet(r.OwnId, Phone))           ' the second key was NOT kept
        Assert.Equal(revisionBefore + 1, r.Recipients.Revision)       ' the recipient set shrank: publishers seal again
        Assert.True(r.Changes > changesBefore)

        ' still not trusted on the next look, and the record of the first key is the legitimate one again
        r.Show(3, r.SelfRec(), Rec(Phone, "Phone", second))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(Phone))
        r.Show(4, r.SelfRec(), Rec(Phone, "Phone", first))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal({Phone}, r.Ids())
    End Sub

    <Fact>
    Public Sub A_device_whose_record_loses_its_key_is_not_a_recipient_and_the_kept_key_stays()
        ' The code reads a record with no key where a key was kept as "another key" (key-changed), not as the plain e2e-missing of a
        ' device that never had one: the person's remedy is the same (remove and add again) and the kept key is never replaced.
        Dim r As New Rig()
        Dim kem As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", kem))
        r.Show(2, r.SelfRec(), Rec(Phone, "Phone", ""))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(Phone))
        Assert.Empty(r.Ids())
        Assert.Equal(kem, r.Trust.TryGet(r.OwnId, Phone))
    End Sub

    <Fact>
    Public Sub Revoking_drops_the_kept_key_hides_the_device_and_a_later_device_under_that_id_is_a_first_sight()
        Dim r As New Rig()
        Dim old As String = NewKem()
        Dim fresh As String = NewKem()
        r.Show(5, r.SelfRec(), Rec(Phone, "Phone", old))
        Assert.Equal({Phone}, r.Ids())

        r.Recipients.Forget(Phone)                                    ' the person removed it (revoke, 5.2)
        Assert.Equal("", r.Trust.TryGet(r.OwnId, Phone))
        Assert.Null(r.Recipients.StateOf(Phone))                      ' not listed any more
        Assert.Empty(r.Ids())
        Assert.Equal("", r.Recipients.TrustedKeyOf(Phone))

        ' the directory still lists it until the removal arrives in `changed`: it is not trusted anew meanwhile
        r.Show(5, r.SelfRec(), Rec(Phone, "Phone", old))
        Assert.Null(r.Recipients.StateOf(Phone))
        Assert.Equal("", r.Trust.TryGet(r.OwnId, Phone))
        ' a connection that ended proves nothing about the revoke
        r.Snapshot = ExchangeDirectorySnapshot.Empty
        r.Features = Nothing
        r.Recipients.Refresh()
        r.Features = New String() {"sealed"}
        r.Show(6, r.SelfRec(), Rec(Phone, "Phone", old))
        Assert.Null(r.Recipients.StateOf(Phone))

        ' the removal arrived: the id is free; a device that enrolls under it again is seen for the first time
        r.Show(7, r.SelfRec())
        r.Show(8, r.SelfRec(), Rec(Phone, "Phone", fresh))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal(fresh, r.Trust.TryGet(r.OwnId, Phone))
        Assert.Equal({Phone}, r.Ids())
    End Sub

    <Fact>
    Public Sub A_revoked_device_stops_being_a_recipient_the_moment_its_removal_arrives()
        Dim r As New Rig()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", NewKem()), Rec(Tv, "TV", NewKem()))
        Assert.Equal(2, r.Ids().Length)
        Dim revision As Long = r.Recipients.Revision
        r.Show(2, r.SelfRec(), Rec(Tv, "TV", r.Recipients.TrustedKeyOf(Tv)))   ' `changed`: the phone is removed
        Assert.Equal({Tv}, r.Ids())
        Assert.Null(r.Recipients.StateOf(Phone))
        Assert.Equal(revision + 1, r.Recipients.Revision)
    End Sub

    <Fact>
    Public Sub A_server_that_removes_a_device_and_lists_it_again_with_another_key_is_caught()
        Dim r As New Rig()
        Dim first As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", first))
        r.Show(2, r.SelfRec())                                        ' the record is gone from the directory (not a revoke by this person)
        Assert.Equal(first, r.Trust.TryGet(r.OwnId, Phone))           ' the kept key outlives the record
        r.Show(3, r.SelfRec(), Rec(Phone, "Phone", NewKem()))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(Phone))
        Assert.Empty(r.Ids())
    End Sub

    <Fact>
    Public Sub The_kept_keys_survive_a_reconnect_and_the_list_is_empty_while_the_connection_is_down()
        Dim r As New Rig()
        Dim kem As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", kem))
        Dim revision As Long = r.Recipients.Revision

        r.Snapshot = ExchangeDirectorySnapshot.Empty
        r.Features = Nothing                                          ' ClearConnection
        r.Recipients.Refresh()
        Assert.Empty(r.Recipients.Devices())
        Assert.Empty(r.Ids())
        Assert.False(r.Recipients.ServerKnown)
        Assert.False(r.Recipients.CanSealThroughServer)
        Assert.Null(r.Recipients.ServerMessage)                       ' not connected is no `server-no-e2e`
        Assert.True(r.Recipients.Revision > revision)
        Assert.Equal(kem, r.Trust.TryGet(r.OwnId, Phone))

        r.Features = New String() {"directory", "sealed"}
        r.Show(9, r.SelfRec(), Rec(Phone, "Phone", kem))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
    End Sub

    <Fact>
    Public Sub Two_entries_of_one_store_keep_their_own_keys()
        Dim store As New ExchangeMemorySealedTrustStore()
        Dim a As New Rig(store, ownId:=ExchangeWireTestKit.IdOf("A"c))
        Dim b As New Rig(store, ownId:=ExchangeWireTestKit.IdOf("B"c))
        Dim k1 As String = NewKem()
        Dim k2 As String = NewKem()
        a.Show(1, a.SelfRec(), Rec(Phone, "Phone", k1))
        b.Show(1, b.SelfRec(), Rec(Phone, "Phone", k2))               ' the same id under another entry is a first sight there
        Assert.Equal(ExchangeSealState.Ready, a.StateOf(Phone))
        Assert.Equal(ExchangeSealState.Ready, b.StateOf(Phone))
        Assert.Equal(k1, store.TryGet(a.OwnId, Phone))
        Assert.Equal(k2, store.TryGet(b.OwnId, Phone))
        a.Recipients.ForgetAll()                                      ' leaving entry A takes only A's keys
        Assert.Equal("", store.TryGet(a.OwnId, Phone))
        Assert.Equal(k2, store.TryGet(b.OwnId, Phone))
    End Sub

    <Fact>
    Public Sub Leaving_the_entry_drops_every_kept_key_and_the_next_sight_is_a_first_one()
        Dim r As New Rig()
        Dim first As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", first))
        r.Recipients.Forget(Phone)
        r.Recipients.ForgetAll()
        Assert.Equal("", r.Trust.TryGet(r.OwnId, Phone))
        Dim second As String = NewKem()
        r.Show(2, r.SelfRec(), Rec(Phone, "Phone", second))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal(second, r.Trust.TryGet(r.OwnId, Phone))
    End Sub

    ''' <summary>A store that cannot write: a first sight is kept for the process, and written once the store works again.</summary>
    Private NotInheritable Class FlakyTrustStore
        Implements IExchangeSealedTrustStore

        Public ReadOnly Inner As New ExchangeMemorySealedTrustStore()
        Public FailWrites As Boolean

        Public Function TryGet(scope As String, deviceId As String) As String Implements IExchangeSealedTrustStore.TryGet
            Return Inner.TryGet(scope, deviceId)
        End Function

        Public Function Keep(scope As String, deviceId As String, kem As String) As Boolean Implements IExchangeSealedTrustStore.Keep
            If FailWrites AndAlso Inner.TryGet(scope, deviceId).Length = 0 Then Return False
            Return Inner.Keep(scope, deviceId, kem)
        End Function

        Public Sub Forget(scope As String, deviceId As String) Implements IExchangeSealedTrustStore.Forget
            Inner.Forget(scope, deviceId)
        End Sub

        Public Sub ForgetScope(scope As String) Implements IExchangeSealedTrustStore.ForgetScope
            Inner.ForgetScope(scope)
        End Sub
    End Class

    <Fact>
    Public Sub A_first_sight_the_store_cannot_write_is_held_in_memory_and_written_later()
        Dim store As New FlakyTrustStore()
        store.FailWrites = True
        Dim r As New Rig(store)
        Dim first As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", first))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal("", store.Inner.TryGet(r.OwnId, Phone))          ' nothing on disk yet

        ' a full disk must not turn into a key that is trusted anew: the held key already catches a change
        r.Show(2, r.SelfRec(), Rec(Phone, "Phone", NewKem()))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(Phone))

        store.FailWrites = False
        r.Show(3, r.SelfRec(), Rec(Phone, "Phone", first))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal(first, store.Inner.TryGet(r.OwnId, Phone))       ' written at the next refresh
        r.Show(4, r.SelfRec(), Rec(Phone, "Phone", NewKem()))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(Phone))
    End Sub

    ' ---------------------------------------------------------------- DP/DR: e2e-missing

    <Fact>
    Public Sub A_device_without_the_capability_or_without_a_usable_key_is_e2e_missing_and_nothing_is_kept()
        Dim r As New Rig()
        Dim noCap As String = ExchangeWireTestKit.IdOf("C"c)
        Dim noKey As String = ExchangeWireTestKit.IdOf("K"c)
        Dim badKey As String = ExchangeWireTestKit.IdOf("B"c)
        Dim otherV As String = ExchangeWireTestKit.IdOf("V"c)
        Dim old As String = ExchangeWireTestKit.IdOf("L"c)
        Dim badKeyRecord As String = Rec(badKey, "Bad key", "AAAA")   ' 3 bytes, no P-256 point
        Dim otherVersion As String = Rec(otherV, "Other version", NewKem()).Replace("""v"":1", """v"":2")
        Dim legacy As String = "{""deviceId"":""" & old & """,""deviceName"":""Legacy"",""product"":""fms-android"",""presence"":""online""}"
        r.Show(1, r.SelfRec(), Rec(noCap, "No capability", NewKem(), capable:=False), Rec(noKey, "No key", "", capable:=True),
               badKeyRecord, otherVersion, legacy)

        For Each id As String In {noCap, noKey, badKey, otherV, old}
            Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(id))
            Assert.Equal("e2e-missing", r.Recipients.Devices().Single(Function(d) d.DeviceId = id).Message.Code)
            Assert.Equal("", r.Recipients.TrustedKeyOf(id))
            Assert.Equal("", r.Trust.TryGet(r.OwnId, id))             ' nothing kept for a device that cannot be sealed to
        Next
        Assert.Empty(r.Ids())
        Assert.Equal(5, r.Recipients.Devices().Where(Function(d) Not d.IsSelf).Count())   ' all of them stay in the person's list
    End Sub

    <Fact>
    Public Sub A_device_that_loses_the_capability_it_had_is_e2e_missing_and_ready_again_when_it_returns()
        Dim r As New Rig()
        Dim kem As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", kem))
        r.Show(2, r.SelfRec(), Rec(Phone, "Phone", kem, capable:=False))
        Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(Phone))
        Assert.Empty(r.Ids())
        Assert.Equal(kem, r.Trust.TryGet(r.OwnId, Phone))
        r.Show(3, r.SelfRec(), Rec(Phone, "Phone", kem))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
    End Sub

    <Fact>
    Public Sub A_device_without_the_capability_but_with_a_key_becomes_ready_on_the_first_sight_with_the_capability()
        Dim r As New Rig()
        Dim kem As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", kem, capable:=False))
        Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(Phone))
        Assert.Equal("", r.Trust.TryGet(r.OwnId, Phone))
        r.Show(2, r.SelfRec(), Rec(Phone, "Phone", kem))
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal(kem, r.Trust.TryGet(r.OwnId, Phone))
    End Sub

    ' ---------------------------------------------------------------- DP: the server

    <Fact>
    Public Sub A_server_without_the_sealed_feature_seals_nothing_and_says_server_no_e2e()
        Dim r As New Rig()
        Dim kem As String = NewKem()
        r.Features = New String() {"directory", "tunnel", "pairing"}
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", kem))
        Assert.True(r.Recipients.ServerKnown)
        Assert.False(r.Recipients.ServerSealed)
        Assert.False(r.Recipients.CanSealThroughServer)
        Assert.Equal("server-no-e2e", r.Recipients.ServerMessage.Code)
        Assert.Empty(r.Ids())
        Assert.Empty(r.Recipients.Recipients())
        ' the presence and the directory stay: the devices are still listed, with their states
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(Phone))
        Assert.Equal(2, r.Recipients.Devices().Count)

        r.Features = New String() {"directory", "sealed"}
        r.Recipients.Refresh()
        Assert.True(r.Recipients.ServerSealed)
        Assert.True(r.Recipients.CanSealThroughServer)
        Assert.Null(r.Recipients.ServerMessage)
        Assert.Equal({Phone}, r.Ids())
    End Sub

    <Fact>
    Public Sub Content_goes_through_the_server_only_with_a_connected_sealed_server_and_a_key_of_our_own()
        Dim noKey As New Rig(withOwnKey:=False)
        noKey.Show(1, noKey.SelfRec(), Rec(Phone, "Phone", NewKem()))
        Assert.False(noKey.Recipients.HasOwnKey)
        Assert.True(noKey.Recipients.ServerSealed)
        Assert.False(noKey.Recipients.CanSealThroughServer)
        Assert.Equal("", noKey.Recipients.TrustedKeyOf(noKey.OwnId))

        Dim down As New Rig()
        down.Features = Nothing
        down.Show(1, down.SelfRec(), Rec(Phone, "Phone", NewKem()))
        Assert.False(down.Recipients.ServerKnown)
        Assert.False(down.Recipients.CanSealThroughServer)
        Assert.Empty(down.Ids())                                      ' no welcome: no feature list to trust
    End Sub

    ' ---------------------------------------------------------------- our own device

    <Fact>
    Public Sub Our_own_device_is_a_recipient_on_request_and_its_own_key_is_the_anchor()
        Dim r As New Rig()
        r.Show(1, Rec(Phone, "Phone", NewKem()), r.SelfRec())
        Dim self As ExchangeSealedDevice = r.Recipients.Devices().First()
        Assert.True(self.IsSelf)                                      ' listed first
        Assert.Equal(r.OwnId, self.DeviceId)
        Assert.Equal(ExchangeSealState.Ready, self.State)
        Assert.Equal(r.OwnKem, self.Kem)
        Assert.Equal(r.OwnKem, r.Recipients.TrustedKeyOf(r.OwnId))
        Assert.Contains(r.OwnId, r.Ids(includeSelf:=True))
        Assert.DoesNotContain(r.OwnId, r.Ids(includeSelf:=False))
        Assert.Equal("", r.Trust.TryGet(r.OwnId, r.OwnId))            ' the directory never writes the anchor

        r.Recipients.Forget(r.OwnId)                                  ' leaving is another path; Forget never hides this device
        Assert.Equal(ExchangeSealState.Ready, r.StateOf(r.OwnId))
    End Sub

    <Fact>
    Public Sub A_directory_that_shows_another_key_for_our_own_device_is_key_changed_and_the_anchor_does_not_move()
        Dim r As New Rig()
        r.Show(1, Rec(r.OwnId, "This PC", NewKem()))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(r.OwnId))
        Assert.Equal(r.OwnKem, r.Recipients.TrustedKeyOf(r.OwnId))    ' our own key is still the one we hold
        Assert.Empty(r.Ids(includeSelf:=True))
    End Sub

    <Fact>
    Public Sub Our_own_record_without_a_key_or_without_the_capability_is_e2e_missing()
        Dim r As New Rig()
        r.Show(1, Rec(r.OwnId, "This PC", ""))                        ' enrolled before it had a key
        Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(r.OwnId))
        r.Show(2, Rec(r.OwnId, "This PC", r.OwnKem, capable:=False))
        Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(r.OwnId))
        Assert.Empty(r.Ids(includeSelf:=True))

        Dim noKey As New Rig(withOwnKey:=False)
        noKey.Show(1, Rec(noKey.OwnId, "This PC", NewKem()))
        Assert.Equal(ExchangeSealState.E2eMissing, noKey.StateOf(noKey.OwnId))
        Assert.Empty(noKey.Ids(includeSelf:=True))
    End Sub

    ' ---------------------------------------------------------------- the 40 and the order

    <Fact>
    Public Sub At_most_forty_recipients_the_online_ones_first_then_the_most_recently_seen()
        Assert.Equal(40, ExchangeSealedBox.MaxRecipients)
        Dim r As New Rig()
        Dim ids As New List(Of String)
        Dim records As New List(Of String)
        records.Add(r.SelfRec())
        For i As Integer = 0 To 44
            Dim id As String = ExchangeIds.NewId()
            ids.Add(id)
            ' devices 0..2 are online with the OLDEST lastSeen; the higher the index the more recently an offline device was seen
            records.Add(Rec(id, "D" & i.ToString(Globalization.CultureInfo.InvariantCulture), NewKem(), True, i < 3,
                            "2026-10-01T10:00:" & i.ToString("D2", Globalization.CultureInfo.InvariantCulture) & "Z"))
        Next
        r.Show(1, records.ToArray())

        Dim expected As New List(Of String) From {ids(2), ids(1), ids(0)}
        For i As Integer = 44 To 8 Step -1
            expected.Add(ids(i))
        Next
        Dim actual As String() = r.Ids()
        Assert.Equal(40, actual.Length)
        Assert.Equal(expected.ToArray(), actual)
        Assert.Equal(46, r.Recipients.Devices().Count)                ' everything stays in the person's list: 45 + this device
        ' with this device counted in, one more of the least recently seen falls off
        Assert.Equal(40, r.Recipients.Recipients(True).Count)
        Assert.Contains(r.OwnId, r.Ids(includeSelf:=True))
    End Sub

    ' ---------------------------------------------------------------- Revision and Changed

    <Fact>
    Public Sub Revision_moves_when_the_recipient_set_changes_and_Changed_when_a_state_or_a_key_does()
        Dim r As New Rig()
        Dim kem As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", kem, lastSeen:="2026-10-01T10:00:00Z"))
        Dim rev As Long = r.Recipients.Revision
        Dim changes As Integer = r.Changes
        Assert.True(changes > 0)

        ' the same directory again changes nothing
        r.Recipients.Refresh()
        Assert.Equal(rev, r.Recipients.Revision)
        Assert.Equal(changes, r.Changes)

        ' a name or a presence is not part of the recipient set
        r.Show(2, r.SelfRec(), Rec(Phone, "Renamed phone", kem, online:=False, lastSeen:="2026-10-01T11:00:00Z"))
        Assert.Equal(rev, r.Recipients.Revision)

        ' a device that cannot be sealed to appears: the person's list changed, the recipient set did not
        r.Show(3, r.SelfRec(), Rec(Phone, "Renamed phone", kem), Rec(Tv, "TV", "", capable:=False))
        Assert.Equal(rev, r.Recipients.Revision)
        Assert.Equal(changes + 1, r.Changes)

        ' it gains the capability and a key: the set grows
        Dim tvKem As String = NewKem()
        r.Show(4, r.SelfRec(), Rec(Phone, "Renamed phone", kem), Rec(Tv, "TV", tvKem))
        Assert.Equal(rev + 1, r.Recipients.Revision)
        Assert.Equal(changes + 2, r.Changes)

        ' a key changes under a ready device: the set shrinks
        r.Show(5, r.SelfRec(), Rec(Phone, "Renamed phone", NewKem()), Rec(Tv, "TV", tvKem))
        Assert.Equal(rev + 2, r.Recipients.Revision)
        Assert.Equal(changes + 3, r.Changes)

        ' e2e-missing -> key-changed on a device that is out of the set already: a state moved, the set did not
        r.Show(6, r.SelfRec(), Rec(Phone, "Renamed phone", kem, capable:=False), Rec(Tv, "TV", tvKem))
        Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(Phone))
        Assert.Equal(rev + 2, r.Recipients.Revision)
        Assert.Equal(changes + 4, r.Changes)
        r.Show(7, r.SelfRec(), Rec(Phone, "Renamed phone", NewKem(), capable:=False), Rec(Tv, "TV", tvKem))
        Assert.Equal(ExchangeSealState.KeyChanged, r.StateOf(Phone))
        Assert.Equal(rev + 2, r.Recipients.Revision)
        Assert.Equal(changes + 5, r.Changes)

        ' a removal
        r.Show(8, r.SelfRec())
        Assert.Equal(rev + 3, r.Recipients.Revision)

        ' the server's `sealed` flipping moves the set as well
        r.Features = New String() {"directory"}
        r.Recipients.Refresh()
        Assert.Equal(rev + 4, r.Recipients.Revision)
        r.Features = New String() {"directory", "sealed"}
        r.Recipients.Refresh()
        Assert.Equal(rev + 5, r.Recipients.Revision)
    End Sub

    ' ---------------------------------------------------------------- the stores

    Private Shared Sub TheStoreKeepsTheFirstKey(store As IExchangeSealedTrustStore, scope As String)
        Dim dev As String = ExchangeWireTestKit.IdOf("D"c)
        Dim k1 As String = NewKem()
        Dim k2 As String = NewKem()
        Assert.Equal("", store.TryGet(scope, dev))
        Assert.True(store.Keep(scope, dev, k1))
        Assert.Equal(k1, store.TryGet(scope, dev))
        Assert.True(store.Keep(scope, dev, k1))                       ' the very key that is kept
        Assert.False(store.Keep(scope, dev, k2))                      ' another one is not kept
        Assert.Equal(k1, store.TryGet(scope, dev))                    ' and nothing replaced the first

        ' a grant is a slot of its own; it never collides with a device id
        Dim grantSlot As String = ExchangeSealedTrustSlots.ForGrant(ExchangeWireTestKit.IdOf("G"c))
        Assert.True(ExchangeSealedTrustSlots.IsValidSlot(grantSlot))
        Assert.True(store.Keep(scope, grantSlot, k2))
        Assert.Equal(k2, store.TryGet(scope, grantSlot))
        Assert.Equal(k1, store.TryGet(scope, dev))

        store.Forget(scope, dev)                                      ' the person revoked it
        Assert.Equal("", store.TryGet(scope, dev))
        Assert.Equal(k2, store.TryGet(scope, grantSlot))
        Assert.True(store.Keep(scope, dev, k2))                       ' re-trusted on its next first sight
        store.Forget(scope, ExchangeWireTestKit.IdOf("Z"c))           ' an unknown id is nothing

        store.ForgetScope(scope)
        Assert.Equal("", store.TryGet(scope, dev))
        Assert.Equal("", store.TryGet(scope, grantSlot))
    End Sub

    <Fact>
    Public Sub The_memory_store_keeps_the_first_key_and_answers_nothing_for_empty_input()
        Dim store As New ExchangeMemorySealedTrustStore()
        TheStoreKeepsTheFirstKey(store, ExchangeWireTestKit.IdOf("S"c))
        Assert.False(store.Keep("", "d", "k"))
        Assert.False(store.Keep("s", "", "k"))
        Assert.False(store.Keep("s", "d", ""))
        Assert.Equal("", store.TryGet("", "d"))
        Assert.False(ExchangeSealedTrustSlots.IsValidSlot(""))
        Assert.False(ExchangeSealedTrustSlots.IsValidSlot("grant:"))
        Assert.False(ExchangeSealedTrustSlots.IsValidSlot("grant:not an id"))
        Assert.False(ExchangeSealedTrustSlots.IsValidSlot("not an id"))
    End Sub

    Private Shared Function TempFolder() As String
        Return Path.Combine(Path.GetTempPath(), "fms-trust-" & Guid.NewGuid().ToString("N"))
    End Function

    Private Shared Sub DeleteFolder(folder As String)
        Try
            If Directory.Exists(folder) Then Directory.Delete(folder, True)
        Catch ex As IOException
        End Try
    End Sub

    <Fact>
    Public Sub The_file_store_keeps_the_first_key_across_instances_and_one_file_names_one_entry()
        Dim folder As String = TempFolder()
        Try
            Dim scope As String = ExchangeWireTestKit.IdOf("S"c)
            Dim dev As String = ExchangeWireTestKit.IdOf("D"c)
            Dim k1 As String = NewKem()
            Dim fresh As New ExchangeSealedTrustStore(folder)
            Assert.Equal("", fresh.TryGet(scope, dev))
            Assert.False(Directory.Exists(folder))                    ' a pure read creates nothing: a machine with no account has no file
            TheStoreKeepsTheFirstKey(New ExchangeSealedTrustStore(folder), scope)

            Assert.True(New ExchangeSealedTrustStore(folder).Keep(scope, dev, k1))
            Dim again As New ExchangeSealedTrustStore(folder)         ' a new process: the file is read back
            Assert.Equal(k1, again.TryGet(scope, dev))
            Dim text As String = File.ReadAllText(again.FilePath)
            Assert.Contains(k1, text)
            Assert.Contains(scope, text)
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"))         ' the write left no temporary file

            ' a file written for another entry is not read, and the first key kept for the new entry replaces it
            Dim other As String = ExchangeWireTestKit.IdOf("T"c)
            Dim reader As New ExchangeSealedTrustStore(folder)
            Assert.Equal("", reader.TryGet(other, dev))
            Dim k2 As String = NewKem()
            Assert.True(reader.Keep(other, dev, k2))
            Assert.Equal(k2, New ExchangeSealedTrustStore(folder).TryGet(other, dev))
            Assert.Equal("", New ExchangeSealedTrustStore(folder).TryGet(scope, dev))

            ' leaving the entry deletes the file
            reader.ForgetScope(other)
            Assert.False(File.Exists(reader.FilePath))
        Finally
            DeleteFolder(folder)
        End Try
    End Sub

    <Fact>
    Public Sub The_file_store_refuses_what_is_no_key_and_survives_a_damaged_or_unwritable_file()
        Dim folder As String = TempFolder()
        Try
            Dim scope As String = ExchangeWireTestKit.IdOf("S"c)
            Dim dev As String = ExchangeWireTestKit.IdOf("D"c)
            Dim store As New ExchangeSealedTrustStore(folder)
            Assert.False(store.Keep(scope, dev, "AAAA"))              ' no P-256 point
            Assert.False(store.Keep(scope, dev, ""))
            Assert.False(store.Keep(scope, "not an id", NewKem()))
            Assert.False(store.Keep("not an id", dev, NewKem()))
            Assert.False(File.Exists(store.FilePath))

            ' a damaged file is the same as none, and the first key kept writes a whole one
            Directory.CreateDirectory(folder)
            File.WriteAllText(store.FilePath, "{ this is not json")
            Dim damaged As New ExchangeSealedTrustStore(folder)
            Assert.Equal("", damaged.TryGet(scope, dev))
            Dim kem As String = NewKem()
            Assert.True(damaged.Keep(scope, dev, kem))
            Assert.Equal(kem, New ExchangeSealedTrustStore(folder).TryGet(scope, dev))

            ' an entry that is no key is dropped when read; the others stay
            Dim other As String = ExchangeWireTestKit.IdOf("E"c)
            File.WriteAllText(store.FilePath, "{""v"":1,""scope"":""" & scope & """,""keys"":{""" & dev & """:""" & kem & """,""" & other & """:""AAAA"",""x y"":""" & kem & """}}")
            Dim tolerant As New ExchangeSealedTrustStore(folder)
            Assert.Equal(kem, tolerant.TryGet(scope, dev))
            Assert.Equal("", tolerant.TryGet(scope, other))
            Assert.Equal("", tolerant.TryGet(scope, "x y"))

            ' another format version is not read
            File.WriteAllText(store.FilePath, "{""v"":2,""scope"":""" & scope & """,""keys"":{""" & dev & """:""" & kem & """}}")
            Assert.Equal("", New ExchangeSealedTrustStore(folder).TryGet(scope, dev))
        Finally
            DeleteFolder(folder)
        End Try

        ' a folder that cannot be created: the key is not kept, and the store says so
        Dim blocker As String = Path.Combine(Path.GetTempPath(), "fms-trust-blocker-" & Guid.NewGuid().ToString("N"))
        Try
            File.WriteAllText(blocker, "a file where the folder should be")
            Dim blocked As New ExchangeSealedTrustStore(Path.Combine(blocker, "sub"))
            Dim scope As String = ExchangeWireTestKit.IdOf("S"c)
            Dim dev As String = ExchangeWireTestKit.IdOf("D"c)
            Assert.False(blocked.Keep(scope, dev, NewKem()))
            Assert.Equal("", blocked.TryGet(scope, dev))              ' rolled back: it is not trusted in memory either
        Finally
            Try
                File.Delete(blocker)
            Catch ex As IOException
            End Try
        End Try
    End Sub

    <Fact>
    Public Sub The_recipients_work_over_the_file_store_and_a_restart_keeps_the_trust()
        Dim folder As String = TempFolder()
        Try
            Dim own As String = ExchangeWireTestKit.IdOf("O"c)
            Dim first As String = NewKem()
            Dim r1 As New Rig(New ExchangeSealedTrustStore(folder), ownId:=own)
            r1.Show(1, r1.SelfRec(), Rec(Phone, "Phone", first))
            Assert.Equal(ExchangeSealState.Ready, r1.StateOf(Phone))

            ' the Share Manager is restarted: a new store over the same folder, a new object for the entry
            Dim r2 As New Rig(New ExchangeSealedTrustStore(folder), ownId:=own)
            r2.Show(1, r2.SelfRec(), Rec(Phone, "Phone", NewKem()))
            Assert.Equal(ExchangeSealState.KeyChanged, r2.StateOf(Phone))
            r2.Show(2, r2.SelfRec(), Rec(Phone, "Phone", first))
            Assert.Equal(ExchangeSealState.Ready, r2.StateOf(Phone))
        Finally
            DeleteFolder(folder)
        End Try
    End Sub

    ' ---------------------------------------------------------------- the directory snapshot

    <Fact>
    Public Sub An_old_directory_snapshot_without_e2eKey_loads_and_its_devices_are_e2e_missing()
        Dim json As String = "{""revision"":3,""devices"":[" &
            "{""deviceId"":""" & Phone & """,""deviceName"":""Phone"",""product"":""fms-android"",""presence"":""online""}," &
            "{""deviceId"":""" & Tv & """,""deviceName"":""TV"",""capabilities"":[""pin-v1""]}],""resources"":[],""broadcasts"":[]}"
        Dim snap As ExchangeDirectorySnapshot = ExchangeDirectorySnapshot.FromLocalJson(json)
        Assert.Equal(3L, snap.Revision)
        Assert.Equal(2, snap.Devices.Count)
        Assert.All(snap.Devices, Sub(d) Assert.Equal("", d.E2eKem))
        Assert.False(snap.Devices(1).HasCapability(ExchangeSealedNames.CapabilityE2e))

        Dim r As New Rig()
        r.Snapshot = snap
        r.Recipients.Refresh()
        Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(Phone))
        Assert.Equal(ExchangeSealState.E2eMissing, r.StateOf(Tv))
        Assert.Empty(r.Ids())
    End Sub

    <Fact>
    Public Sub A_member_that_is_no_key_reads_as_no_key_and_never_throws()
        Dim kem As String = NewKem()
        For Each key As String In {"""e2eKey"":null", """e2eKey"":""text""", """e2eKey"":{}", """e2eKey"":{""v"":1}", """e2eKey"":{""v"":""1"",""kem"":""" & kem & """}",
                                   """e2eKey"":{""v"":1,""kem"":42}", """e2eKey"":{""v"":1,""kem"":""" & kem & "A""}", """e2eKey"":{""v"":1,""kem"":""" & kem & "=""}",
                                   """e2eKey"":{""v"":1,""kem"":""" & kem.Substring(0, 40) & """}"}
            Dim rec As ExchangeDeviceRecord = Nothing
            Assert.True(ExchangeRecordReader.TryReadDevice("{""deviceId"":""" & Phone & """," & key & "}", rec), key)
            Assert.Equal("", rec.E2eKem)
        Next
        Dim good As ExchangeDeviceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadDevice("{""deviceId"":""" & Phone & """,""e2eKey"":{""v"":1,""kem"":""" & kem & """,""future"":true}}", good))
        Assert.Equal(kem, good.E2eKem)                                ' a member this program does not know travels along
    End Sub

    <Fact>
    Public Sub The_local_channel_json_keeps_the_public_key_and_the_capability_for_the_viewer()
        Dim kem As String = NewKem()
        Dim shown As ExchangeDirectorySnapshot = Snap(4, Rec(Phone, "Phone", kem), Rec(Tv, "TV", ""))
        Dim json As String = shown.ToLocalJson()
        Assert.Contains(kem, json)                                    ' public by design (item DF)
        Dim back As ExchangeDirectorySnapshot = ExchangeDirectorySnapshot.FromLocalJson(json)
        Dim p As ExchangeDeviceRecord = back.Devices.Single(Function(d) d.DeviceId = Phone)
        Assert.Equal(kem, p.E2eKem)
        Assert.True(p.HasCapability(ExchangeSealedNames.CapabilityE2e))
        Assert.Equal("", back.Devices.Single(Function(d) d.DeviceId = Tv).E2eKem)
    End Sub

    ' ---------------------------------------------------------------- check code, rows of item DR

    <Fact>
    Public Sub A_listed_device_shows_the_check_code_of_the_key_the_record_lists()
        Dim r As New Rig()
        Dim kem As String = NewKem()
        r.Show(1, r.SelfRec(), Rec(Phone, "Phone", kem), Rec(Tv, "TV", "", capable:=False))
        Dim phoneDevice As ExchangeSealedDevice = r.Recipients.Devices().Single(Function(d) d.DeviceId = Phone)
        Assert.Equal(ExchangeDeviceKey.CheckCodeOfKem(kem), phoneDevice.CheckCode)
        Assert.Matches("^[0-9]{4} [0-9]{4} [0-9]{4}$", phoneDevice.CheckCode)
        Assert.Equal("", r.Recipients.Devices().Single(Function(d) d.DeviceId = Tv).CheckCode)
        Assert.Equal(ExchangeDeviceKey.CheckCodeOfKem(r.OwnKem), r.Recipients.Devices().Single(Function(d) d.IsSelf).CheckCode)
    End Sub

    <Fact>
    Public Sub The_four_rows_of_item_DR_are_the_contract_rows_and_the_device_message_is_one_of_them()
        Assert.Same(ConnectionDiagnosisMessages.E2eMissing, New ExchangeSealedDevice(Phone, "P", "", "", True, False, ExchangeSealState.E2eMissing).Message)
        Assert.Same(ConnectionDiagnosisMessages.KeyChanged, New ExchangeSealedDevice(Phone, "P", "", "", True, False, ExchangeSealState.KeyChanged).Message)
        Assert.Null(New ExchangeSealedDevice(Phone, "P", "", "", True, False, ExchangeSealState.Ready).Message)

        ' kinds and steps of the table
        Assert.Equal(ConnectionDiagnosisKind.State, ConnectionDiagnosisMessages.E2eMissing.Kind)
        Assert.Equal(ConnectionDiagnosisKind.State, ConnectionDiagnosisMessages.KeyChanged.Kind)
        Assert.Equal(ConnectionDiagnosisKind.State, ConnectionDiagnosisMessages.ServerNoE2e.Kind)
        Assert.Equal(ConnectionDiagnosisKind.Local, ConnectionDiagnosisMessages.SealedFailed.Kind)
        Assert.False(ConnectionDiagnosisMessages.E2eMissing.StepValue.HasValue)
        Assert.False(ConnectionDiagnosisMessages.KeyChanged.StepValue.HasValue)
        Assert.Equal(ConnectionDiagnosisStep.Operator, ConnectionDiagnosisMessages.ServerNoE2e.StepValue.Value)
        Assert.False(ConnectionDiagnosisMessages.SealedFailed.StepValue.HasValue)

        Dim rows As String()() = {
            New String() {"e2e-missing", "This device cannot receive protected content yet, so nothing is sent to it through the server.",
                          "Это устройство пока не умеет принимать защищённое содержимое, поэтому через сервер ему ничего не отправляется.",
                          "Цей пристрій поки не вміє приймати захищений вміст, тому через сервер йому нічого не надсилається."},
            New String() {"key-changed", "The protection key of this device has changed, so nothing is sent to it. Remove the device from the account and add it again.",
                          "Ключ защиты этого устройства изменился, поэтому ему ничего не отправляется. Удалите устройство из аккаунта и добавьте его снова.",
                          "Ключ захисту цього пристрою змінився, тому йому нічого не надсилається. Видаліть пристрій з акаунта й додайте його знову."},
            New String() {"server-no-e2e", "This server cannot carry protected content, so folders, broadcasts and the clipboard do not go through it.",
                          "Этот сервер не умеет передавать защищённое содержимое, поэтому папки, трансляции и буфер через него не идут.",
                          "Цей сервер не вміє передавати захищений вміст, тому папки, трансляції й буфер через нього не йдуть."},
            New String() {"sealed-failed", "This could not be opened on this device. Try again from the other device.",
                          "Это не удалось открыть на этом устройстве. Повторите с другого устройства.",
                          "Це не вдалося відкрити на цьому пристрої. Повторіть з іншого пристрою."}}
        For Each row As String() In rows
            Assert.Equal(row(1), ConnectionDiagnosisLocalization.GetMessageText(row(0), "en"))
            Assert.Equal(row(2), ConnectionDiagnosisLocalization.GetMessageText(row(0), "ru"))
            Assert.Equal(row(3), ConnectionDiagnosisLocalization.GetMessageText(row(0), "uk"))
        Next
    End Sub

    ' ---------------------------------------------------------------- the private key stays private

    <Fact>
    Public Sub The_private_key_never_appears_in_a_ToString_or_in_the_message_of_an_exception()
        Dim secrets As New List(Of String)
        Dim text As New List(Of String)
        Using key As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Dim scalar As Byte() = key.ExportPrivateScalar()
            secrets.Add(Convert.ToHexString(scalar))
            secrets.Add(Convert.ToHexString(scalar).ToLowerInvariant())
            secrets.Add(Convert.ToBase64String(scalar))
            secrets.Add(ExchangeBase64Url.Encode(scalar))
            Assert.True(secrets.All(Function(s) s.Length >= 43))

            Dim keys As New ExchangeMemoryDeviceKeyStore()
            Dim id As String = ExchangeWireTestKit.IdOf("D"c)
            Assert.True(keys.Save(id, key))
            Dim folder As String = TempFolder()
            Try
                Dim cipher As New ReversibleTestCipher()
                Dim fileStore As New ExchangeDeviceKeyStore(folder, cipher)
                Assert.True(fileStore.Save(id, key))
                cipher.FailUnprotect = True
                Assert.Null(fileStore.TryLoad(id))                    ' a key that cannot be opened is nothing, not an exception
                text.Add(fileStore.ToString())
                For Each fileName As String In Directory.GetFiles(folder)
                    Dim bytes As Byte() = File.ReadAllBytes(fileName)
                    Assert.DoesNotContain(Convert.ToHexString(scalar), Convert.ToHexString(bytes))   ' protected on disk
                Next
            Finally
                DeleteFolder(folder)
            End Try

            Dim rig As New Rig()
            rig.Show(1, rig.SelfRec(), Rec(Phone, "Phone", key.Kem))
            text.Add(key.ToString())
            text.Add(keys.ToString())
            text.Add(rig.Trust.ToString())
            text.Add(New ExchangeSealedTrustStore(TempFolder()).ToString())
            text.Add(rig.Recipients.ToString())
            text.Add(rig.Snapshot.Devices(0).ToString())
            text.Add(rig.Snapshot.ToString())
            For Each d As ExchangeSealedDevice In rig.Recipients.Devices()
                text.Add(d.ToString())
            Next

            Dim attempts As New List(Of Action) From {
                Sub() ExchangeDeviceKeys.LoadOrCreate(Nothing, id),
                Sub() ExchangeDeviceKeys.LoadOrCreate(keys, "not an id").Dispose(),
                Sub() ExchangeDeviceKey.FromPrivateScalar(New Byte() {1, 2, 3}),
                Sub() ExchangeSealedBoxes.Open(Nothing, id, key, "access:x", Function(f) ""),
                Sub() GC.KeepAlive(New ExchangeDeviceKeyStore("", Nothing))}
            For Each attempt As Action In attempts
                Try
                    attempt.Invoke()
                Catch ex As Exception
                    text.Add(ex.ToString())
                End Try
            Next
        End Using

        For Each line As String In text
            For Each secret As String In secrets
                Assert.DoesNotContain(secret, line)
            Next
        Next
    End Sub

End Class
#End If
