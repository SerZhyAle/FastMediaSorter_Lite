#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' DEVICE-EXCHANGE items DG, DJ, DK and DP. Secrets leave the agent only over its current-user pipe.
Friend NotInheritable Class ExchangeSealedBroadcastAccess
    Private Sub New()
    End Sub

    Public Shared Function Resolve(record As ExchangeBroadcastRecord, directory As IExchangeSealedDirectory,
                                   trust As IExchangeSealedTrustStore, ownKey As ExchangeDeviceKey) As ExchangeSealedAccessResult
        If directory Is Nothing Then Return Fail("not-connected")
        If directory.ServerMessage IsNot Nothing Then Return Fail(directory.ServerMessage.Code)
        If ownKey Is Nothing OrElse Not directory.HasOwnKey Then Return Fail("e2e-missing")
        If record Is Nothing Then Return Fail("unknown-resource")
        Try
            Using doc As JsonDocument = JsonDocument.Parse(record.Raw)
                Dim root As JsonElement = doc.RootElement
                Dim grantId As String = TextOf(root, "grantId")
                Dim granted As Boolean = grantId.Length > 0
                If Not granted Then
                    Dim state As ExchangeSealState? = directory.StateOf(record.DeviceId)
                    If Not state.HasValue Then Return Fail("sealed-failed")
                    If state.Value = ExchangeSealState.KeyChanged Then Return Fail("key-changed")
                    If state.Value <> ExchangeSealState.Ready Then Return Fail("e2e-missing")
                End If
                Dim element As JsonElement
                If Not root.TryGetProperty("sealed", element) Then Return Fail(If(granted, "e2e-missing", "sealed-failed"))
                Dim box As ExchangeSealedBox = Nothing
                If Not ExchangeSealedBox.TryParse(element.GetRawText(), box) Then Return Fail("sealed-failed")
                Dim slot As String = ExchangeSealedTrustSlots.ForGrant(grantId)
                Dim kept As String = ""
                If granted Then
                    If trust Is Nothing OrElse Not ExchangeIds.IsValidId(grantId) Then Return Fail("sealed-failed")
                    kept = trust.TryGet(directory.OwnDeviceId, slot)
                    If kept.Length > 0 AndAlso kept <> box.FromKey Then Return Fail("key-changed")
                ElseIf box.FromDeviceId <> record.DeviceId OrElse box.FromKey <> directory.TrustedKeyOf(record.DeviceId) Then
                    Return Fail("sealed-failed")
                End If
                Dim context As String = If(granted, ExchangeSealedContext.Grant(grantId), ExchangeSealedContext.Broadcast(record.BroadcastId))
                Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, directory.OwnDeviceId, ownKey, context,
                    Function(id) If(granted, If(kept.Length > 0, kept, box.FromKey), directory.TrustedKeyOf(id)))
                Try
                    If opened.Outcome = ExchangeSealedOpenOutcome.NoEntry Then Return Fail("not-addressed")
                    If Not opened.Succeeded Then Return Fail("sealed-failed")
                    Dim payload As JsonObject = TryCast(JsonNode.Parse(Encoding.UTF8.GetString(opened.Payload)), JsonObject)
                    If payload Is Nothing Then Return Fail("sealed-failed")
                    Dim relayKey As String = NodeText(payload, "relayKey")
                    Dim pin As String = NodeText(payload, "tunnelPin")
                    If relayKey.Length > 0 Then
                        Dim raw As Byte() = Nothing
                        If Not ExchangeBase64Url.TryDecode(relayKey, raw) Then Return Fail("sealed-failed")
                        Dim valid As Boolean = raw.Length = ExchangeRelayRecords.KeyLength
                        Array.Clear(raw, 0, raw.Length)
                        If Not valid Then Return Fail("sealed-failed")
                    End If
                    If pin.Length > 0 AndAlso Not ExchangeSealedTunnel.IsPin(pin) Then Return Fail("sealed-failed")
                    Dim descriptor As JsonElement
                    Dim endpoints As JsonElement
                    If root.TryGetProperty("descriptor", descriptor) AndAlso descriptor.ValueKind = JsonValueKind.Object AndAlso
                       descriptor.TryGetProperty("endpoints", endpoints) AndAlso endpoints.ValueKind = JsonValueKind.Array Then
                        For Each ep As JsonElement In endpoints.EnumerateArray()
                            If ep.ValueKind <> JsonValueKind.Object Then Continue For
                            If TextOf(ep, "transport") = "RELAY" AndAlso relayKey.Length = 0 Then Return Fail("sealed-failed")
                            If TextOf(ep, "transport") = "TUNNEL" AndAlso pin.Length = 0 Then Return Fail("sealed-failed")
                        Next
                    End If
                    Dim result As New JsonObject()
                    If relayKey.Length > 0 Then result("relayKey") = relayKey
                    If pin.Length > 0 Then result("tunnelPin") = pin
                    Dim credentials As JsonObject = TryCast(payload("credentials"), JsonObject)
                    If credentials IsNot Nothing Then
                        If NodeText(credentials, "user").Length = 0 OrElse NodeText(credentials, "password").Length = 0 Then Return Fail("sealed-failed")
                        result("credentials") = credentials.DeepClone()
                    End If
                    If granted AndAlso kept.Length = 0 AndAlso Not trust.Keep(directory.OwnDeviceId, slot, box.FromKey) Then Return Fail("key-changed")
                    result("from") = New JsonObject From {{"deviceId", JsonValue.Create(box.FromDeviceId)}, {"checkCode", JsonValue.Create(ExchangeDeviceKey.CheckCodeOfKem(box.FromKey))}}
                    Return New ExchangeSealedAccessResult(result.ToJsonString(), Nothing)
                Finally
                    opened.Wipe()
                End Try
            End Using
        Catch ex As JsonException
            Return Fail("sealed-failed")
        Catch ex As InvalidOperationException
            Return Fail("sealed-failed")
        End Try
    End Function

    Private Shared Function TextOf(root As JsonElement, name As String) As String
        Dim el As JsonElement
        If root.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return el.GetString()
        Return ""
    End Function

    Private Shared Function NodeText(root As JsonObject, name As String) As String
        Dim value As String = Nothing
        Dim node As JsonValue = TryCast(root(name), JsonValue)
        Return If(node IsNot Nothing AndAlso node.TryGetValue(Of String)(value), value, "")
    End Function

    Private Shared Function Fail(code As String) As ExchangeSealedAccessResult
        Return New ExchangeSealedAccessResult("", New ConnectionDiagnosisMessage(code, ConnectionDiagnosisKind.State, Nothing, False))
    End Function
End Class
#End If
