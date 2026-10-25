#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports Xunit

Public Class ExchangeSealedBroadcastAccessTests
    Private Shared ReadOnly Bid As String = ExchangeWireTestKit.IdOf("B"c)
    Private Shared ReadOnly GrantId As String = ExchangeWireTestKit.IdOf("G"c)

    Private Shared Function Record(sender As SealedTestDevice, receiver As SealedTestDevice, Optional granted As Boolean = False,
                                   Optional context As String = Nothing) As ExchangeBroadcastRecord
        Dim payload As New JsonObject From {{"relayKey", JsonValue.Create(ExchangeBase64Url.Encode(ExchangeRelayRecords.NewKey()))}}
        Dim box = ExchangeSealedBoxes.Seal(Encoding.UTF8.GetBytes(payload.ToJsonString()), If(context, If(granted, ExchangeSealedContext.Grant(GrantId), ExchangeSealedContext.Broadcast(Bid))),
            sender.Id, sender.Key, New ExchangeSealedRecipient() {New ExchangeSealedRecipient(receiver.Id, receiver.Key.Kem)})
        Try
            Dim raw As New JsonObject From {{"broadcastId", JsonValue.Create(Bid)}, {"deviceId", JsonValue.Create(sender.Id)}, {"sealed", JsonNode.Parse(box.Box.ToJson())}}
            If granted Then raw("grantId") = GrantId
            Return New ExchangeBroadcastRecord(raw.ToJsonString(), Bid, sender.Id, "AUDIO_ONLY", "Private cast")
        Finally
            box.Wipe()
        End Try
    End Function

    <Fact>
    Public Sub Own_record_opens_only_with_the_listed_producers_key_and_expected_context()
        Using sender As New SealedTestDevice(ExchangeWireTestKit.IdOf("P"c)), receiver As New SealedTestDevice(ExchangeWireTestKit.IdOf("C"c)), impostor As New SealedTestDevice(sender.Id)
            Dim view As New SealedTestDirectory(receiver)
            view.Add(sender)
            Dim trust As New ExchangeMemorySealedTrustStore()
            Dim opened = ExchangeSealedBroadcastAccess.Resolve(Record(sender, receiver), view, trust, receiver.Key)
            Assert.True(opened.Opened)
            Using doc = JsonDocument.Parse(opened.AccessJson)
                Assert.Equal(sender.Id, doc.RootElement.GetProperty("from").GetProperty("deviceId").GetString())
                Assert.Equal(sender.Key.CheckCode, doc.RootElement.GetProperty("from").GetProperty("checkCode").GetString())
            End Using
            Assert.Equal("sealed-failed", ExchangeSealedBroadcastAccess.Resolve(Record(impostor, receiver), view, trust, receiver.Key).State.Code)
            Assert.Equal("sealed-failed", ExchangeSealedBroadcastAccess.Resolve(Record(sender, receiver, context:="clipboard"), view, trust, receiver.Key).State.Code)
            view.ServerSupportsSealed = False
            Assert.Equal("server-no-e2e", ExchangeSealedBroadcastAccess.Resolve(Record(sender, receiver), view, trust, receiver.Key).State.Code)
        End Using
    End Sub

    <Fact>
    Public Sub Guest_pins_only_an_opened_grant_and_rejects_a_replacement_sender()
        Using sender As New SealedTestDevice(ExchangeWireTestKit.IdOf("P"c)), receiver As New SealedTestDevice(ExchangeWireTestKit.IdOf("C"c)), stranger As New SealedTestDevice(ExchangeWireTestKit.IdOf("S"c))
            Dim view As New SealedTestDirectory(receiver)
            Dim trust As New ExchangeMemorySealedTrustStore()
            Assert.Equal("not-addressed", ExchangeSealedBroadcastAccess.Resolve(Record(sender, stranger, True), view, trust, receiver.Key).State.Code)
            Assert.Equal("", trust.TryGet(receiver.Id, ExchangeSealedTrustSlots.ForGrant(GrantId)))
            Assert.True(ExchangeSealedBroadcastAccess.Resolve(Record(sender, receiver, True), view, trust, receiver.Key).Opened)
            Assert.Equal(sender.Key.Kem, trust.TryGet(receiver.Id, ExchangeSealedTrustSlots.ForGrant(GrantId)))
            Assert.Equal("key-changed", ExchangeSealedBroadcastAccess.Resolve(Record(stranger, receiver, True), view, trust, receiver.Key).State.Code)
        End Using
    End Sub
End Class
#End If
