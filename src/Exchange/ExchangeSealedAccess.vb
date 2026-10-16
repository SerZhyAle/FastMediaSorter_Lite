#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports System.Text.Json

' Spec 234 T-2, DEVICE-EXCHANGE section 27 items DI (sealed `access` of a folder), DJ (the guest's granted record), DP, DR.
' The pure half of sealed folders: the producer seals the `access` of a folder, the consumer's agent opens it. No pipe, no
' session, no clock - the publisher and the agent call it, the tests pin it.
'
'   - PRODUCER: SealAccess puts the 6.2 `access` object (exactly) into a box with the context `access:<resourceId>`, addressed to the
'     entry's recipients (item DG). The record then carries `sealedAccess` and no `access`.
'   - PRODUCER as the giver of a grant (item DJ): SealGrant puts `{access}` into a box with the context `grant:<grantId>` addressed
'     to the guest account's devices that hold a key (GuestRecipientsOf reads them from the member's `grants` record, `devices`);
'     TryGrantFrame builds the `grant` message with `sealed` and neither `access` nor `credentials`, or none when it cannot be sealed.
'   - CONSUMER of a device of the same account: Resolve opens `sealedAccess` with this device's key. The context is built here from
'     the record's own resourceId, never read from the box; `from` must be the record's owning device and `fromKey` the key this
'     program trusts for that device (item DG). The opened descriptor's fingerprint must be the record's own `hostKeyFingerprint`
'     (item DI): a record that differs is not merged, not attached and shows `sealed-failed`.
'   - A clear `access` that came through the server is never used (item DP): a device with no `e2eKey` shows `e2e-missing`, a device
'     that has one and still sends clear text is `sealed-failed`, and a server without `sealed` is `server-no-e2e`.
'   - CONSUMER as a guest (item DJ): the granted record carries `sealed` with the context `grant:<grantId>` and a payload
'     `{access}`. A guest knows none of the owner's devices, so the first box of a grant pins its `fromKey` for that grantId (trust on
'     first use, in the `grant:` key space of the trust store); a later box with another `fromKey` is not opened (`key-changed`).
'
' Every failure is a typed state (ConnectionDiagnosisMessages) and carries no value: not the box, not a key, not the access.

''' <summary>What opening the access of one record gave: the `access` JSON, or the state to show. Never both.</summary>
Friend NotInheritable Class ExchangeSealedAccessResult

    ''' <summary>The opened `access` as compact JSON ("" unless <see cref="State"/> is Nothing and there was something to open). A secret.</summary>
    Public ReadOnly AccessJson As String
    ''' <summary>The state of item DR to show, or Nothing when the access opened (or the record has none).</summary>
    Public ReadOnly State As ConnectionDiagnosisMessage

    Public Sub New(accessJson As String, state As ConnectionDiagnosisMessage)
        Me.AccessJson = If(accessJson, "")
        Me.State = state
    End Sub

    Public ReadOnly Property Opened As Boolean
        Get
            Return State Is Nothing AndAlso AccessJson.Length > 0
        End Get
    End Property

    ''' <summary>Never the access.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedAccessResult " & If(State Is Nothing, If(Opened, "opened", "none"), State.Code)
    End Function

End Class

Friend NotInheritable Class ExchangeSealedAccess

    Private Sub New()
    End Sub

    ' --- the producer ---

    ''' <summary>Seals the `access` object of a folder (the 6.2 JSON, exactly) for the recipients. False when nothing could be sealed
    ''' (no key, no recipient, a recipient the box refuses): the caller then publishes nothing for this folder.</summary>
    Public Shared Function SealAccess(accessJson As String, resourceId As String, ownDeviceId As String, ownKey As ExchangeDeviceKey,
                                      recipients As IReadOnlyList(Of ExchangeSealedRecipient), ByRef boxJson As String) As Boolean
        boxJson = ""
        If String.IsNullOrEmpty(accessJson) OrElse ownKey Is Nothing OrElse recipients Is Nothing OrElse recipients.Count = 0 Then Return False
        If Not ExchangeIds.IsValidId(resourceId) Then Return False
        Dim payload As Byte() = Encoding.UTF8.GetBytes(accessJson)
        Try
            Dim sealedResult As ExchangeSealResult = ExchangeSealedBoxes.Seal(payload, ExchangeSealedContext.Access(resourceId), ownDeviceId, ownKey, recipients)
            Try
                If Not sealedResult.Succeeded Then Return False
                boxJson = ExchangeJson.Compact(sealedResult.Box.ToJson())
                Return True
            Finally
                sealedResult.Wipe()
            End Try
        Finally
            Array.Clear(payload, 0, payload.Length)
        End Try
    End Function

    ' --- the producer as the giver of a grant (item DJ) ---

    ''' <summary>The recipients of a grant: the guest account's devices that hold a key, out of the `devices` of the member's `grants` record
    ''' (`{deviceId, deviceName, e2eKey}` - public values the server already holds). A device without a valid `e2eKey` is left out - it
    ''' gets nothing, and there is no clear value in its place (item DP). Empty when the record names none, or names one id with two keys.</summary>
    Public Shared Function GuestRecipientsOf(grantRecordJson As String) As IReadOnlyList(Of ExchangeSealedRecipient)
        Dim list As New List(Of ExchangeSealedRecipient)
        If String.IsNullOrEmpty(grantRecordJson) Then Return list
        Try
            Using doc As JsonDocument = JsonDocument.Parse(grantRecordJson)
                Dim devices As JsonElement = Nothing
                If doc.RootElement.ValueKind <> JsonValueKind.Object OrElse Not doc.RootElement.TryGetProperty("devices", devices) OrElse
                   devices.ValueKind <> JsonValueKind.Array Then Return list
                Dim kems As New Dictionary(Of String, String)(StringComparer.Ordinal)
                For Each el As JsonElement In devices.EnumerateArray()
                    If el.ValueKind <> JsonValueKind.Object Then Continue For
                    Dim rec As ExchangeDeviceRecord = Nothing
                    If Not ExchangeRecordReader.TryReadDevice(el.GetRawText(), rec) OrElse rec.E2eKem.Length = 0 Then Continue For
                    Dim kept As String = Nothing
                    If kems.TryGetValue(rec.DeviceId, kept) Then
                        If Not String.Equals(kept, rec.E2eKem, StringComparison.Ordinal) Then
                            list.Clear()
                            Return list
                        End If
                        Continue For
                    End If
                    kems.Add(rec.DeviceId, rec.E2eKem)
                    list.Add(New ExchangeSealedRecipient(rec.DeviceId, rec.E2eKem))
                Next
            End Using
        Catch ex As JsonException
            list.Clear()
        End Try
        Return list
    End Function

    ''' <summary>Seals the `access` of a folder for a guest account (item DJ): the payload is `{"access": {..}}` under the context
    ''' `grant:&lt;grantId&gt;`, addressed to the guest's devices. False when nothing could be sealed (no key, no valid grant id, no guest
    ''' device holds a key, a descriptor that is no object): the caller sends no `grant` at all - there is no clear `access` in its place.</summary>
    Public Shared Function SealGrant(accessJson As String, grantId As String, ownDeviceId As String, ownKey As ExchangeDeviceKey,
                                     guestDevices As IReadOnlyList(Of ExchangeSealedRecipient), ByRef boxJson As String) As Boolean
        boxJson = ""
        If String.IsNullOrEmpty(accessJson) OrElse ownKey Is Nothing OrElse guestDevices Is Nothing OrElse guestDevices.Count = 0 Then Return False
        If Not ExchangeIds.IsValidId(grantId) OrElse Not ExchangeIds.IsValidId(ownDeviceId) Then Return False
        Dim access As String = ""
        Try
            Using doc As JsonDocument = JsonDocument.Parse(accessJson)
                If doc.RootElement.ValueKind <> JsonValueKind.Object Then Return False
                access = ExchangeJson.Compact(doc.RootElement.GetRawText())
            End Using
        Catch ex As JsonException
            Return False
        End Try
        Dim payload As Byte() = Encoding.UTF8.GetBytes("{""access"":" & access & "}")
        Try
            Dim sealedResult As ExchangeSealResult = ExchangeSealedBoxes.Seal(payload, ExchangeSealedContext.Grant(grantId), ownDeviceId, ownKey, guestDevices)
            Try
                If Not sealedResult.Succeeded Then Return False
                boxJson = ExchangeJson.Compact(sealedResult.Box.ToJson())
                Return True
            Finally
                sealedResult.Wipe()
            End Try
        Finally
            Array.Clear(payload, 0, payload.Length)
        End Try
    End Function

    ''' <summary>The `grant` message that gives a folder to a guest account, sealed to the guest's devices named in
    ''' <paramref name="grantRecordJson"/>: it carries `sealed` and neither `access` nor `credentials`. False (and no message) when it
    ''' cannot be sealed - then nothing is sent, because there is no clear fallback through the server (item DP). A second call with
    ''' the same <paramref name="grantId"/> and a new recipient set is the "replace this grant's box" of item DJ.</summary>
    Public Shared Function TryGrantFrame(grantId As String, guest As String, resourceId As String, accessJson As String, grantRecordJson As String,
                                         ownDeviceId As String, ownKey As ExchangeDeviceKey, ByRef frame As ExchangeEnvelope) As Boolean
        frame = Nothing
        Dim boxJson As String = ""
        If Not SealGrant(accessJson, grantId, ownDeviceId, ownKey, GuestRecipientsOf(grantRecordJson), boxJson) Then Return False
        frame = ExchangeMessages.GrantTo(grantId, guest, resourceId, "", "", "", boxJson)
        Return True
    End Function

    ''' <summary>The `fp` of an `access` JSON (item BA: the record's hostKeyFingerprint), "" when the access does not read.</summary>
    Public Shared Function FingerprintOf(accessJson As String, fallbackName As String) As String
        Dim info As ExchangeAccessInfo = Nothing
        If Not ExchangeResourceAccess.TryRead(accessJson, fallbackName, info) Then Return ""
        Return info.Code.Fingerprint
    End Function

    ' --- the consumer ---

    ''' <summary>The access of a listed record for the agent that holds <paramref name="ownKey"/>. A record with nothing to open (an
    ''' offline producer's) answers a result with neither an access nor a state; the caller keeps its `no-access`.</summary>
    ''' <param name="sealed">The recipients of the entry (states of the devices, trusted keys, features of the server).</param>
    ''' <param name="trust">The kept keys of the entry; used only for a granted record (a guest's first-box pin).</param>
    ''' <param name="ownKey">This device's key, Nothing when it has none.</param>
    Public Shared Function Resolve(record As ExchangeResourceRecord, sealed As IExchangeSealedDirectory, trust As IExchangeSealedTrustStore, ownKey As ExchangeDeviceKey) As ExchangeSealedAccessResult
        If record Is Nothing Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        ' a protection this client cannot honour blanks the access, and a box beside it is not opened either
        If Not record.CanAuthorize Then Return None()
        Dim granted As Boolean = record.GrantId.Length > 0
        Dim hasBox As Boolean = If(granted, record.SealedGrantJson.Length > 0, record.SealedAccessJson.Length > 0)
        If Not hasBox AndAlso Not record.HasAccess Then Return None()
        If sealed Is Nothing Then Return Failed(ConnectionDiagnosisMessages.E2eMissing)
        If sealed.ServerMessage IsNot Nothing Then Return Failed(sealed.ServerMessage)
        If Not hasBox Then Return Failed(ClearAccessState(record, sealed, granted))
        If ownKey Is Nothing OrElse Not sealed.HasOwnKey Then Return Failed(ConnectionDiagnosisMessages.E2eMissing)
        Return If(granted, OpenGranted(record, sealed, trust, ownKey), OpenOwn(record, sealed, ownKey))
    End Function

    ''' <summary>A clear `access` is never used through the server (item DP). Which state it shows depends on who sent it.</summary>
    Private Shared Function ClearAccessState(record As ExchangeResourceRecord, sealed As IExchangeSealedDirectory, granted As Boolean) As ConnectionDiagnosisMessage
        If granted Then Return ConnectionDiagnosisMessages.E2eMissing
        Dim state As Nullable(Of ExchangeSealState) = sealed.StateOf(record.DeviceId)
        If state.HasValue AndAlso state.Value = ExchangeSealState.KeyChanged Then Return ConnectionDiagnosisMessages.KeyChanged
        ' a device with a key that still sends clear text did not seal: nothing a conforming producer does
        If state.HasValue AndAlso state.Value = ExchangeSealState.Ready Then Return ConnectionDiagnosisMessages.SealedFailed
        Return ConnectionDiagnosisMessages.E2eMissing
    End Function

    Private Shared Function OpenOwn(record As ExchangeResourceRecord, sealed As IExchangeSealedDirectory, ownKey As ExchangeDeviceKey) As ExchangeSealedAccessResult
        Dim state As Nullable(Of ExchangeSealState) = sealed.StateOf(record.DeviceId)
        If Not state.HasValue Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        If state.Value = ExchangeSealState.KeyChanged Then Return Failed(ConnectionDiagnosisMessages.KeyChanged)
        If state.Value <> ExchangeSealState.Ready Then Return Failed(ConnectionDiagnosisMessages.E2eMissing)

        Dim box As ExchangeSealedBox = Nothing
        If Not ExchangeSealedBox.TryParse(record.SealedAccessJson, box) Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        ' the sender must be the device that owns the record, and the key it shows the one this program trusts for it
        Dim trusted As String = sealed.TrustedKeyOf(record.DeviceId)
        If trusted.Length = 0 OrElse Not String.Equals(box.FromDeviceId, record.DeviceId, StringComparison.Ordinal) OrElse
           Not String.Equals(box.FromKey, trusted, StringComparison.Ordinal) Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)

        Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, sealed.OwnDeviceId, ownKey, ExchangeSealedContext.Access(record.ResourceId), AddressOf sealed.TrustedKeyOf)
        Return AccessOfPayload(opened, record, False)
    End Function

    Private Shared Function OpenGranted(record As ExchangeResourceRecord, sealed As IExchangeSealedDirectory, trust As IExchangeSealedTrustStore, ownKey As ExchangeDeviceKey) As ExchangeSealedAccessResult
        Dim slot As String = ExchangeSealedTrustSlots.ForGrant(record.GrantId)
        Dim scope As String = sealed.OwnDeviceId
        If trust Is Nothing OrElse Not ExchangeSealedTrustSlots.IsValidSlot(slot) OrElse Not ExchangeIds.IsValidId(scope) Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)

        Dim box As ExchangeSealedBox = Nothing
        If Not ExchangeSealedBox.TryParse(record.SealedGrantJson, box) Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        Dim kept As String = trust.TryGet(scope, slot)
        If kept.Length > 0 AndAlso Not String.Equals(kept, box.FromKey, StringComparison.Ordinal) Then Return Failed(ConnectionDiagnosisMessages.KeyChanged)
        Dim pinned As String = If(kept.Length > 0, kept, box.FromKey)

        Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, sealed.OwnDeviceId, ownKey, ExchangeSealedContext.Grant(record.GrantId), Function(fromDevice) pinned)
        Dim result As ExchangeSealedAccessResult = AccessOfPayload(opened, record, True)
        ' trust on first use: the key is kept only once the box opened, and a key that cannot be kept is no trust at all
        If result.Opened AndAlso kept.Length = 0 AndAlso Not trust.Keep(scope, slot, box.FromKey) Then
            Return Failed(If(trust.TryGet(scope, slot).Length > 0, ConnectionDiagnosisMessages.KeyChanged, ConnectionDiagnosisMessages.SealedFailed))
        End If
        Return result
    End Function

    ''' <summary>The access out of an opened box: the payload IS the `access` object (item DI) or holds it as `access` (item DJ).</summary>
    Private Shared Function AccessOfPayload(opened As ExchangeSealedOpenResult, record As ExchangeResourceRecord, wrapped As Boolean) As ExchangeSealedAccessResult
        If opened Is Nothing OrElse Not opened.Succeeded OrElse opened.Payload Is Nothing Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        Dim text As String
        Try
            text = Encoding.UTF8.GetString(opened.Payload)
        Finally
            opened.Wipe()
        End Try
        Dim accessJson As String = text
        Try
            If wrapped Then
                accessJson = ""
                Using doc As JsonDocument = JsonDocument.Parse(text)
                    Dim el As JsonElement = Nothing
                    If doc.RootElement.ValueKind = JsonValueKind.Object AndAlso doc.RootElement.TryGetProperty("access", el) AndAlso el.ValueKind = JsonValueKind.Object Then
                        accessJson = el.GetRawText()
                    End If
                End Using
            End If
            accessJson = ExchangeJson.Compact(accessJson)
        Catch ex As JsonException
            Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        End Try
        Dim info As ExchangeAccessInfo = Nothing
        If accessJson.Length = 0 OrElse Not ExchangeResourceAccess.TryRead(accessJson, record.Name, info) Then Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        ' item DI: the SSH host key is checked against the OPENED descriptor; a record that says another fingerprint is not that resource
        If record.HostKeyFingerprint.Length > 0 AndAlso Not String.Equals(record.HostKeyFingerprint, info.Code.Fingerprint, StringComparison.Ordinal) Then
            Return Failed(ConnectionDiagnosisMessages.SealedFailed)
        End If
        Return New ExchangeSealedAccessResult(accessJson, Nothing)
    End Function

    Private Shared Function Failed(state As ConnectionDiagnosisMessage) As ExchangeSealedAccessResult
        Return New ExchangeSealedAccessResult("", state)
    End Function

    Private Shared Function None() As ExchangeSealedAccessResult
        Return New ExchangeSealedAccessResult("", Nothing)
    End Function

End Class
#End If
