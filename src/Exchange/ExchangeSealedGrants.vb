#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' DEVICE-EXCHANGE item DJ: registration contains only {}, never the target's access or credentials.
Friend NotInheritable Class ExchangeSealedGrants
    Private Sub New()
    End Sub

    Public Shared Function ResourcePayload(accessJson As String) As String
        Return New JsonObject From {{"access", JsonNode.Parse(accessJson)}}.ToJsonString()
    End Function

    Public Shared Function BroadcastPayload(accessJson As String) As String
        Dim payload = DirectCast(JsonNode.Parse(accessJson), JsonObject)
        payload.Remove("from")
        Return payload.ToJsonString()
    End Function

    Public Shared Function HasRelayKey(payload As String) As Boolean
        Using doc = JsonDocument.Parse(payload)
            Dim value As JsonElement
            Return doc.RootElement.TryGetProperty("relayKey", value)
        End Using
    End Function

    Public Shared Function WithRelayKey(payload As String, groupKey As String) As String
        Dim json = DirectCast(JsonNode.Parse(payload), JsonObject)
        json("relayKey") = groupKey
        Return json.ToJsonString()
    End Function

    Public Shared Function RegisteringBox(grantId As String, deviceId As String, key As ExchangeDeviceKey) As String
        Dim result = ExchangeSealedBoxes.Seal(Encoding.UTF8.GetBytes("{}"), ExchangeSealedContext.Grant(grantId), deviceId, key,
            New ExchangeSealedRecipient() {New ExchangeSealedRecipient(deviceId, key.Kem)})
        Try
            If Not result.Succeeded Then Return ""
            Dim json = DirectCast(JsonNode.Parse(result.Box.ToJson()), JsonObject)
            json("keys") = New JsonArray()
            Return json.ToJsonString()
        Finally
            result.Wipe()
        End Try
    End Function

    Public Shared Function SealPayload(payload As String, grantId As String, deviceId As String, key As ExchangeDeviceKey,
                                      recipients As IReadOnlyList(Of ExchangeSealedRecipient)) As String
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(payload)
        Try
            Dim result = ExchangeSealedBoxes.Seal(bytes, ExchangeSealedContext.Grant(grantId), deviceId, key, recipients)
            Try
                Return If(result.Succeeded, result.Box?.ToJson(), "")
            Finally
                result.Wipe()
            End Try
        Finally
            Array.Clear(bytes, 0, bytes.Length)
        End Try
    End Function
End Class
#End If
