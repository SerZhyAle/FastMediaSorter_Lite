#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json

' Spec 234 T-1, DEVICE-EXCHANGE section 27 item DH. The sealed box: ONE JSON object carries every sealed value of the
' contract. A random content key CK seals the payload (AES-256-GCM, the box's context as associated data); CK itself
' is wrapped once per recipient with HPKE Auth mode (ExchangeHpke), the same context as `aad` and `fmsx-e2e-v1` as `info`.
'
' The context is the one the READER expects for the place the box was found in, never a value read from the box, so a box
' moved from one record to another fails its tag. Any failure opens nothing and returns nothing partial; the caller shows
' `sealed-failed`. To the server a box is an opaque member (item DQ): nothing here logs a box, a key or a payload, and no
' result or exception message carries one.

''' <summary>The four contexts of the contract (items DI, DJ, DK, DN).</summary>
Friend NotInheritable Class ExchangeSealedContext

    Private Sub New()
    End Sub

    ''' <summary>The context of a clipboard value.</summary>
    Public Const Clipboard As String = "clipboard"

    Public Shared Function Access(resourceId As String) As String
        Return Build("access:", resourceId)
    End Function

    Public Shared Function Grant(grantId As String) As String
        Return Build("grant:", grantId)
    End Function

    Public Shared Function Broadcast(broadcastId As String) As String
        Return Build("broadcast:", broadcastId)
    End Function

    Private Shared Function Build(prefix As String, id As String) As String
        If String.IsNullOrEmpty(id) Then Throw New ArgumentException("id", NameOf(id))
        Return prefix & id
    End Function

End Class

''' <summary>A device a box is sealed to: its id and its `kem` (the 65-byte public key in base64url).</summary>
Friend NotInheritable Class ExchangeSealedRecipient

    Public ReadOnly DeviceId As String
    Public ReadOnly Kem As String

    Public Sub New(deviceId As String, kem As String)
        Me.DeviceId = deviceId
        Me.Kem = kem
    End Sub

End Class

''' <summary>One `{to, enc, wk}` of a box: the content key wrapped for one recipient.</summary>
Friend NotInheritable Class ExchangeSealedBoxKey

    Public ReadOnly ToDeviceId As String
    Public ReadOnly Enc As String
    Public ReadOnly Wk As String

    Public Sub New(toDeviceId As String, enc As String, wk As String)
        Me.ToDeviceId = toDeviceId
        Me.Enc = enc
        Me.Wk = wk
    End Sub

End Class

''' <summary>The box as the wire has it (item DH): `v`, `from`, `fromKey`, `n`, `ct`, `keys`. Every binary member stays the
''' base64url string it travels as; Open decodes and checks them.</summary>
Friend NotInheritable Class ExchangeSealedBox

    Public Const CurrentVersion As Integer = 1
    Public Const MaxRecipients As Integer = 40
    ' A reader's own bound on the keys it will look through; the 40 is a SENDER rule (item DH).
    Private Const MaxKeysRead As Integer = 256

    Public ReadOnly Version As Integer
    Public ReadOnly FromDeviceId As String
    Public ReadOnly FromKey As String
    Public ReadOnly Nonce As String
    Public ReadOnly Ciphertext As String
    Public ReadOnly Keys As IReadOnlyList(Of ExchangeSealedBoxKey)

    Public Sub New(version As Integer, fromDeviceId As String, fromKey As String, nonce As String, ciphertext As String, keys As IReadOnlyList(Of ExchangeSealedBoxKey))
        Me.Version = version
        Me.FromDeviceId = fromDeviceId
        Me.FromKey = fromKey
        Me.Nonce = nonce
        Me.Ciphertext = ciphertext
        Me.Keys = keys
    End Sub

    ''' <summary>The JSON object, members in the order of the contract, the way the wire writes JSON.</summary>
    Public Function ToJson() As String
        Dim w As New ExchangeJsonWriter()
        w.BeginObject()
        w.Name("v")
        w.Number(Version)
        w.Name("from")
        w.Text(FromDeviceId)
        w.Name("fromKey")
        w.Text(FromKey)
        w.Name("n")
        w.Text(Nonce)
        w.Name("ct")
        w.Text(Ciphertext)
        w.Name("keys")
        w.BeginArray()
        For Each k As ExchangeSealedBoxKey In Keys
            w.BeginObject()
            w.Name("to")
            w.Text(k.ToDeviceId)
            w.Name("enc")
            w.Text(k.Enc)
            w.Name("wk")
            w.Text(k.Wk)
            w.EndObject()
        Next
        w.EndArray()
        w.EndObject()
        Return Encoding.UTF8.GetString(w.ToArray())
    End Function

    ''' <summary>False when the text is not JSON or lacks a member of the box (unknown extra members are ignored). An unknown
    ''' `v` still parses: Open answers it as UnknownVersion.</summary>
    Public Shared Function TryParse(json As String, ByRef box As ExchangeSealedBox) As Boolean
        box = Nothing
        If String.IsNullOrWhiteSpace(json) Then Return False
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Return TryRead(doc.RootElement, box)
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

    Public Shared Function TryRead(element As JsonElement, ByRef box As ExchangeSealedBox) As Boolean
        box = Nothing
        If element.ValueKind <> JsonValueKind.Object Then Return False
        Dim v As JsonElement
        Dim ver As Integer
        If Not element.TryGetProperty("v", v) OrElse v.ValueKind <> JsonValueKind.Number OrElse Not v.TryGetInt32(ver) Then Return False
        Dim fromId As String = Nothing
        Dim fromKey As String = Nothing
        Dim nonce As String = Nothing
        Dim ct As String = Nothing
        If Not TryString(element, "from", fromId) OrElse Not TryString(element, "fromKey", fromKey) Then Return False
        If Not TryString(element, "n", nonce) OrElse Not TryString(element, "ct", ct) Then Return False
        Dim keysElement As JsonElement
        If Not element.TryGetProperty("keys", keysElement) OrElse keysElement.ValueKind <> JsonValueKind.Array Then Return False
        If keysElement.GetArrayLength() > MaxKeysRead Then Return False
        Dim keys As New List(Of ExchangeSealedBoxKey)
        For Each item As JsonElement In keysElement.EnumerateArray()
            If item.ValueKind <> JsonValueKind.Object Then Return False
            Dim toId As String = Nothing
            Dim enc As String = Nothing
            Dim wk As String = Nothing
            If Not TryString(item, "to", toId) OrElse Not TryString(item, "enc", enc) OrElse Not TryString(item, "wk", wk) Then Return False
            keys.Add(New ExchangeSealedBoxKey(toId, enc, wk))
        Next
        box = New ExchangeSealedBox(ver, fromId, fromKey, nonce, ct, keys)
        Return True
    End Function

    Private Shared Function TryString(element As JsonElement, name As String, ByRef value As String) As Boolean
        value = Nothing
        Dim p As JsonElement
        If Not element.TryGetProperty(name, p) OrElse p.ValueKind <> JsonValueKind.String Then Return False
        value = p.GetString()
        Return value IsNot Nothing
    End Function

    ''' <summary>The type name only: a box is never written to a log (item DQ).</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedBox"
    End Function

End Class

Friend Enum ExchangeSealOutcome
    Sealed = 0
    NoRecipients = 1
    ''' <summary>More than 40 distinct recipients. Which 40 (the ones seen most recently) is the caller's choice.</summary>
    TooManyRecipients = 2
    ''' <summary>A recipient with no deviceId, a `kem` that is no point of P-256, or one id listed with two keys.</summary>
    InvalidRecipient = 3
End Enum

Friend NotInheritable Class ExchangeSealResult

    Public ReadOnly Outcome As ExchangeSealOutcome
    Public ReadOnly Box As ExchangeSealedBox
    ''' <summary>CK, only when the caller asked for it (the clipboard needs it for its value key). The caller wipes it.</summary>
    Public ContentKey As Byte()

    Friend Sub New(outcome As ExchangeSealOutcome, box As ExchangeSealedBox, contentKey As Byte())
        Me.Outcome = outcome
        Me.Box = box
        Me.ContentKey = contentKey
    End Sub

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Outcome = ExchangeSealOutcome.Sealed
        End Get
    End Property

    Public Sub Wipe()
        If ContentKey IsNot Nothing Then Array.Clear(ContentKey, 0, ContentKey.Length)
        ContentKey = Nothing
    End Sub

    ''' <summary>The outcome name; never the box or a key.</summary>
    Public Overrides Function ToString() As String
        Return Outcome.ToString()
    End Function

End Class

''' <summary>Why a box did not open (item DH: no entry, an unknown `v`, an untrusted `fromKey`, a failed tag).</summary>
Friend Enum ExchangeSealedOpenOutcome
    Opened = 0
    ''' <summary>No `keys` entry names this device.</summary>
    NoEntry = 1
    UnknownVersion = 2
    ''' <summary>`fromKey` is not the key this reader trusts for `from` (or it trusts none, or either is no key).</summary>
    UntrustedFromKey = 3
    ''' <summary>A failed tag at the HPKE wrap or at `ct` - and a member that is malformed, which no honest sender writes.</summary>
    TagFailed = 4
End Enum

Friend NotInheritable Class ExchangeSealedOpenResult

    Public ReadOnly Outcome As ExchangeSealedOpenOutcome
    ''' <summary>The payload, only when Outcome is Opened. The caller wipes it when it holds a secret.</summary>
    Public Payload As Byte()
    ''' <summary>CK, only when asked for and opened.</summary>
    Public ContentKey As Byte()

    Friend Sub New(outcome As ExchangeSealedOpenOutcome, payload As Byte(), contentKey As Byte())
        Me.Outcome = outcome
        Me.Payload = payload
        Me.ContentKey = contentKey
    End Sub

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Outcome = ExchangeSealedOpenOutcome.Opened
        End Get
    End Property

    Public Sub Wipe()
        If Payload IsNot Nothing Then Array.Clear(Payload, 0, Payload.Length)
        If ContentKey IsNot Nothing Then Array.Clear(ContentKey, 0, ContentKey.Length)
        Payload = Nothing
        ContentKey = Nothing
    End Sub

    ''' <summary>The outcome name; never the payload.</summary>
    Public Overrides Function ToString() As String
        Return Outcome.ToString()
    End Function

End Class

''' <summary>Seals a payload to devices and opens a box addressed to this device.</summary>
Friend NotInheritable Class ExchangeSealedBoxes

    Private Const ContentKeyLength As Integer = 32
    Private Const WrappedKeyLength As Integer = ContentKeyLength + ExchangeAead.TagLength

    ''' <summary>The HPKE `info` of every box: the ASCII bytes `fmsx-e2e-v1`.</summary>
    Public Shared ReadOnly Info As Byte() = Encoding.ASCII.GetBytes("fmsx-e2e-v1")

    Private Sub New()
    End Sub

    ''' <summary>Seals <paramref name="payloadJson"/> (the UTF-8 bytes of one JSON object) to the recipients. The sender's
    ''' device id and key are its own; at most 40 distinct recipients (item DH - the caller picks the 40 seen most recently).
    ''' The sender itself may be among the recipients; it is then just another entry.</summary>
    Public Shared Function Seal(payloadJson As Byte(), context As String, senderDeviceId As String, sender As ExchangeDeviceKey, recipients As IReadOnlyList(Of ExchangeSealedRecipient), Optional keepContentKey As Boolean = False) As ExchangeSealResult
        Return SealCore(payloadJson, context, senderDeviceId, sender, recipients, keepContentKey, Nothing, Nothing)
    End Function

    ''' <summary>The same with a given content key and nonce. TEST HOOK, to prove the AEAD layer against a vector; a real box
    ''' draws both at random (the only caller that may fix them is a test).</summary>
    Friend Shared Function SealWithFixedRandom(payloadJson As Byte(), context As String, senderDeviceId As String, sender As ExchangeDeviceKey, recipients As IReadOnlyList(Of ExchangeSealedRecipient), contentKey As Byte(), nonce As Byte()) As ExchangeSealResult
        Return SealCore(payloadJson, context, senderDeviceId, sender, recipients, True, contentKey, nonce)
    End Function

    Private Shared Function SealCore(payloadJson As Byte(), context As String, senderDeviceId As String, sender As ExchangeDeviceKey, recipients As IReadOnlyList(Of ExchangeSealedRecipient), keepContentKey As Boolean, fixedKey As Byte(), fixedNonce As Byte()) As ExchangeSealResult
        If payloadJson Is Nothing Then Throw New ArgumentNullException(NameOf(payloadJson))
        If String.IsNullOrEmpty(context) Then Throw New ArgumentException("context", NameOf(context))
        If String.IsNullOrEmpty(senderDeviceId) Then Throw New ArgumentException("senderDeviceId", NameOf(senderDeviceId))
        If sender Is Nothing Then Throw New ArgumentNullException(NameOf(sender))
        If recipients Is Nothing OrElse recipients.Count = 0 Then Return Failed(ExchangeSealOutcome.NoRecipients)

        ' The recipients: one entry per device id; one id with two keys is a caller's bug, not something to seal to.
        Dim distinct As New List(Of ExchangeSealedRecipient)
        Dim points As New List(Of Byte())
        Dim seen As New Dictionary(Of String, String)(StringComparer.Ordinal)
        For Each r As ExchangeSealedRecipient In recipients
            If r Is Nothing OrElse String.IsNullOrEmpty(r.DeviceId) OrElse String.IsNullOrEmpty(r.Kem) Then Return Failed(ExchangeSealOutcome.InvalidRecipient)
            Dim kemSeen As String = Nothing
            If seen.TryGetValue(r.DeviceId, kemSeen) Then
                If Not String.Equals(kemSeen, r.Kem, StringComparison.Ordinal) Then Return Failed(ExchangeSealOutcome.InvalidRecipient)
                Continue For
            End If
            seen.Add(r.DeviceId, r.Kem)
            distinct.Add(r)
        Next
        If distinct.Count > ExchangeSealedBox.MaxRecipients Then Return Failed(ExchangeSealOutcome.TooManyRecipients)
        For Each r As ExchangeSealedRecipient In distinct
            Dim point As Byte() = Nothing
            If Not ExchangeDeviceKey.TryParseKem(r.Kem, point) Then Return Failed(ExchangeSealOutcome.InvalidRecipient)
            points.Add(point)
        Next

        Dim ck As Byte() = If(fixedKey, RandomNumberGenerator.GetBytes(ContentKeyLength))
        Dim nonce As Byte() = If(fixedNonce, RandomNumberGenerator.GetBytes(ExchangeAead.NonceLength))
        Dim contextBytes As Byte() = Encoding.UTF8.GetBytes(context)
        Try
            Dim ct As Byte() = ExchangeAead.Seal(ck, nonce, contextBytes, payloadJson)
            Dim keys As New List(Of ExchangeSealedBoxKey)(distinct.Count)
            For i As Integer = 0 To distinct.Count - 1
                Dim wrapped As ExchangeHpkeSealed = ExchangeHpke.SealAuth(points(i), sender, Info, contextBytes, ck)
                If wrapped Is Nothing Then Return Failed(ExchangeSealOutcome.InvalidRecipient)
                keys.Add(New ExchangeSealedBoxKey(distinct(i).DeviceId, ExchangeBase64Url.Encode(wrapped.Enc), ExchangeBase64Url.Encode(wrapped.Ciphertext)))
            Next
            Dim box As New ExchangeSealedBox(ExchangeSealedBox.CurrentVersion, senderDeviceId, sender.Kem, ExchangeBase64Url.Encode(nonce), ExchangeBase64Url.Encode(ct), keys)
            ' The caller owns the copy it asked for; the working key is wiped below when it was drawn here.
            Return New ExchangeSealResult(ExchangeSealOutcome.Sealed, box, If(keepContentKey, CType(ck.Clone(), Byte()), Nothing))
        Finally
            If fixedKey Is Nothing Then Array.Clear(ck, 0, ck.Length)
        End Try
    End Function

    ''' <summary>Opens a box addressed to this device. <paramref name="expectedContext"/> is the context of the place the box
    ''' was found in; <paramref name="trustedKeyForFrom"/> answers, for the box's `from`, the `kem` this reader trusts for that
    ''' device (item DG; for a guest the one it kept for the grant, item DJ), or Nothing when it trusts none.</summary>
    Public Shared Function Open(box As ExchangeSealedBox, ownDeviceId As String, ownKey As ExchangeDeviceKey, expectedContext As String, trustedKeyForFrom As Func(Of String, String), Optional keepContentKey As Boolean = False) As ExchangeSealedOpenResult
        If ownKey Is Nothing Then Throw New ArgumentNullException(NameOf(ownKey))
        If String.IsNullOrEmpty(expectedContext) Then Throw New ArgumentException("expectedContext", NameOf(expectedContext))
        If box Is Nothing OrElse box.Keys Is Nothing Then Return Refused(ExchangeSealedOpenOutcome.TagFailed)
        If box.Version <> ExchangeSealedBox.CurrentVersion Then Return Refused(ExchangeSealedOpenOutcome.UnknownVersion)

        Dim entry As ExchangeSealedBoxKey = Nothing
        For Each k As ExchangeSealedBoxKey In box.Keys
            If String.Equals(k.ToDeviceId, ownDeviceId, StringComparison.Ordinal) Then
                entry = k
                Exit For
            End If
        Next
        If entry Is Nothing Then Return Refused(ExchangeSealedOpenOutcome.NoEntry)

        ' The key the box claims must be the key this reader trusts for the sender, byte for byte.
        Dim claimed As Byte() = Nothing
        Dim trusted As Byte() = Nothing
        Dim trustedText As String = If(trustedKeyForFrom Is Nothing, Nothing, trustedKeyForFrom(box.FromDeviceId))
        If Not ExchangeDeviceKey.TryParseKem(box.FromKey, claimed) OrElse Not ExchangeDeviceKey.TryParseKem(trustedText, trusted) OrElse
           Not CryptographicOperations.FixedTimeEquals(claimed, trusted) Then
            Return Refused(ExchangeSealedOpenOutcome.UntrustedFromKey)
        End If

        Dim nonce As Byte() = Nothing
        Dim enc As Byte() = Nothing
        Dim wk As Byte() = Nothing
        Dim ct As Byte() = Nothing
        If Not ExchangeBase64Url.TryDecode(box.Nonce, nonce) OrElse nonce.Length <> ExchangeAead.NonceLength OrElse
           Not ExchangeBase64Url.TryDecode(entry.Enc, enc) OrElse enc.Length <> ExchangeHpke.EncLength OrElse
           Not ExchangeBase64Url.TryDecode(entry.Wk, wk) OrElse wk.Length <> WrappedKeyLength OrElse
           Not ExchangeBase64Url.TryDecode(box.Ciphertext, ct) OrElse ct.Length < ExchangeAead.TagLength Then
            Return Refused(ExchangeSealedOpenOutcome.TagFailed)
        End If

        Dim contextBytes As Byte() = Encoding.UTF8.GetBytes(expectedContext)
        Dim ck As Byte() = Nothing
        If Not ExchangeHpke.TryOpenAuth(enc, claimed, ownKey, Info, contextBytes, wk, ck) OrElse ck.Length <> ContentKeyLength Then
            Return Refused(ExchangeSealedOpenOutcome.TagFailed)
        End If
        Dim payload As Byte() = Nothing
        If Not ExchangeAead.TryOpen(ck, nonce, contextBytes, ct, payload) Then
            Array.Clear(ck, 0, ck.Length)
            Return Refused(ExchangeSealedOpenOutcome.TagFailed)
        End If
        If Not keepContentKey Then
            Array.Clear(ck, 0, ck.Length)
            ck = Nothing
        End If
        Return New ExchangeSealedOpenResult(ExchangeSealedOpenOutcome.Opened, payload, ck)
    End Function

    Private Shared Function Failed(outcome As ExchangeSealOutcome) As ExchangeSealResult
        Return New ExchangeSealResult(outcome, Nothing, Nothing)
    End Function

    Private Shared Function Refused(outcome As ExchangeSealedOpenOutcome) As ExchangeSealedOpenResult
        Return New ExchangeSealedOpenResult(outcome, Nothing, Nothing)
    End Function

End Class
#End If
