#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json

' Spec 234 T-3, DEVICE-EXCHANGE section 27 items DK and DL, the BROADCASTER side. One broadcast that goes through the exchange
' server is sealed here: the group keys of its links (ExchangeBroadcastKeys), the record's box (item DK) and the epochs of the
' relay upload (item DL) live in one ExchangeSealedBroadcastSession per broadcast. The transport is not here: the agent sends
' `broadcast-update` and LiveSealedRelayUploader writes the chunks.
'
' Rules this file keeps:
'   - A group key is dropped by replacing it, never by editing the ring: an own device that is no longer a recipient makes the own
'     key (`relayKey`) NEW, and the next upload is a replacement with a new epoch key. The record is updated FIRST (item N): a
'     listener that still holds the old key cannot open the new ring, and a device that was kept learns the new key from the box.
'   - No clear fallback: with no sealed server there is nothing to send (`CanUpload` is False) - the caller opens no upload.
'   - Nothing here logs. A key, a box, a ring, a payload and the credentials never reach a message, an exception or a ToString.

''' <summary>The group keys of one broadcast: the key of the account's own devices (`relayKey`) and one per outward link. Each is 32
''' random bytes made here (item DL). The text form is base64url, the way the contract writes it.</summary>
Friend NotInheritable Class ExchangeBroadcastKeys
    Implements IDisposable

    ''' <summary>The ring holds at most 64 entries (item DL); the own key takes one.</summary>
    Public Const MaxLinks As Integer = ExchangeRelayRecords.MaxRingEntries - 1

    Private ReadOnly _gate As New Object()
    Private _relayKey As Byte()
    Private ReadOnly _links As New List(Of Byte())()

    Public Sub New()
        _relayKey = ExchangeRelayRecords.NewKey()
    End Sub

    ''' <summary>The own devices' key as it travels in the box. A secret: the caller seals it and drops the text.</summary>
    Public Function RelayKeyText() As String
        SyncLock _gate
            Return ExchangeBase64Url.Encode(_relayKey)
        End SyncLock
    End Function

    Public ReadOnly Property LinkCount As Integer
        Get
            SyncLock _gate
                Return _links.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>A new key for one outward link, as the text the outward descriptor carries in `seal.key`; "" at the ring bound.</summary>
    Public Function AddLink() As String
        SyncLock _gate
            If _links.Count >= MaxLinks Then Return ""
            Dim key As Byte() = ExchangeRelayRecords.NewKey()
            _links.Add(key)
            Return ExchangeBase64Url.Encode(key)
        End SyncLock
    End Function

    ''' <summary>Drops the key of a withdrawn link. True when it was one of ours.</summary>
    Public Function WithdrawLink(keyText As String) As Boolean
        Dim raw As Byte() = Nothing
        If Not ExchangeBase64Url.TryDecode(keyText, raw) OrElse raw.Length <> ExchangeRelayRecords.KeyLength Then Return False
        Try
            SyncLock _gate
                For i As Integer = 0 To _links.Count - 1
                    If CryptographicOperations.FixedTimeEquals(_links(i), raw) Then
                        Array.Clear(_links(i), 0, _links(i).Length)
                        _links.RemoveAt(i)
                        Return True
                    End If
                Next
            End SyncLock
            Return False
        Finally
            Array.Clear(raw, 0, raw.Length)
        End Try
    End Function

    ''' <summary>Replaces the own devices' key: an own device was removed, so what it knew opens nothing from now on.</summary>
    Public Sub RotateRelayKey()
        SyncLock _gate
            Array.Clear(_relayKey, 0, _relayKey.Length)
            _relayKey = ExchangeRelayRecords.NewKey()
        End SyncLock
    End Sub

    ''' <summary>Copies of every group key, the own key first. The caller wipes them once the ring is built.</summary>
    Public Function Snapshot() As IReadOnlyList(Of Byte())
        SyncLock _gate
            Dim all As New List(Of Byte())(_links.Count + 1)
            all.Add(CType(_relayKey.Clone(), Byte()))
            For Each link As Byte() In _links
                all.Add(CType(link.Clone(), Byte()))
            Next
            Return all
        End SyncLock
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _gate
            Array.Clear(_relayKey, 0, _relayKey.Length)
            For Each link As Byte() In _links
                Array.Clear(link, 0, link.Length)
            Next
            _links.Clear()
        End SyncLock
    End Sub

    ''' <summary>The type name; never a key.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeBroadcastKeys"
    End Function

End Class

''' <summary>The sealed state of one broadcast on this device: its box (item DK) and its epochs (item DL).</summary>
Friend NotInheritable Class ExchangeSealedBroadcastSession
    Implements IDisposable

    Private ReadOnly _gate As New Object()
    Private ReadOnly _broadcastId As String
    Private ReadOnly _mediaType As String
    Private ReadOnly _keys As ExchangeBroadcastKeys
    Private ReadOnly _directory As Func(Of IExchangeSealedDirectory)
    Private ReadOnly _loadKey As Func(Of ExchangeDeviceKey)
    Private ReadOnly _hasRelay As Boolean
    Private ReadOnly _tunnelPin As String
    Private _credentialUser As String
    Private _credentialPassword As String

    Private _epoch As UInteger
    Private _boxJson As String
    Private _boxRecipients As HashSet(Of String)
    Private _boxDirty As Boolean
    Private _pendingReplace As Boolean
    Private _replaceSeq As Long
    Private _disposed As Boolean

    ''' <param name="mediaType">The true media type the key ring names (`video/mp2t`, `audio/aac`).</param>
    ''' <param name="directory">The sealed directory of the CURRENT control session (a function: the session is made anew after a
    ''' restart); Nothing from it means not connected.</param>
    ''' <param name="loadKey">This device's own key; the caller owns the returned object, this class disposes it.</param>
    ''' <param name="hasRelay">A RELAY endpoint is listed, so the box carries `relayKey`.</param>
    ''' <param name="tunnelPin">The `SHA256:` pin of the tunnel's leaf (item DM, a later slice); "" while the tunnel has none.</param>
    ''' <param name="credentialUser">With <paramref name="credentialPassword"/>: the credentials of a protected broadcast (item BF).</param>
    Public Sub New(broadcastId As String, mediaType As String, keys As ExchangeBroadcastKeys,
                   directory As Func(Of IExchangeSealedDirectory), loadKey As Func(Of ExchangeDeviceKey),
                   hasRelay As Boolean, tunnelPin As String, credentialUser As String, credentialPassword As String)
        If String.IsNullOrEmpty(broadcastId) Then Throw New ArgumentException("broadcastId", NameOf(broadcastId))
        If keys Is Nothing Then Throw New ArgumentNullException(NameOf(keys))
        _broadcastId = broadcastId
        _mediaType = If(mediaType, "")
        _keys = keys
        _directory = directory
        _loadKey = loadKey
        _hasRelay = hasRelay
        _tunnelPin = If(tunnelPin, "")
        _credentialUser = If(credentialUser, "")
        _credentialPassword = If(credentialPassword, "")
    End Sub

    Public ReadOnly Property BroadcastId As String
        Get
            Return _broadcastId
        End Get
    End Property

    Public ReadOnly Property Keys As ExchangeBroadcastKeys
        Get
            Return _keys
        End Get
    End Property

    ''' <summary>The epoch of the last sender made (0 before the first upload).</summary>
    Public ReadOnly Property Epoch As UInteger
        Get
            SyncLock _gate
                Return _epoch
            End SyncLock
        End Get
    End Property

    ''' <summary>The record's current box (JSON), Nothing while none could be made (no recipient is known yet, or nothing to carry).</summary>
    Public ReadOnly Property BoxJson As String
        Get
            SyncLock _gate
                Return _boxJson
            End SyncLock
        End Get
    End Property

    ''' <summary>True once something asked for a replacement upload that the record has not been updated for yet.</summary>
    Public ReadOnly Property ReplacementPending As Boolean
        Get
            SyncLock _gate
                Return _pendingReplace
            End SyncLock
        End Get
    End Property

    ''' <summary>Moves by one each time a replacement upload is released (the record was updated first). The uploader watches it.</summary>
    Public ReadOnly Property ReplaceSeq As Long
        Get
            SyncLock _gate
                Return _replaceSeq
            End SyncLock
        End Get
    End Property

    ''' <summary>True when content may go through the server now: a connected, sealed server, a key of our own and somebody who can
    ''' open it - a recipient of the box or an outward link (a device with no `e2eKey` is no recipient, item DP). No clear fallback.</summary>
    Public ReadOnly Property CanUpload As Boolean
        Get
            Dim dir As IExchangeSealedDirectory = CurrentDirectory()
            If dir Is Nothing OrElse Not dir.CanSealThroughServer Then Return False
            SyncLock _gate
                If _disposed Then Return False
                Return _boxJson IsNot Nothing OrElse _keys.LinkCount > 0
            End SyncLock
        End Get
    End Property

    ''' <summary>True when the current box addresses the device.</summary>
    Public Function Addresses(deviceId As String) As Boolean
        If String.IsNullOrEmpty(deviceId) Then Return False
        SyncLock _gate
            If _boxRecipients Is Nothing OrElse _boxJson Is Nothing Then Return False
            Dim prefix As String = deviceId & "|"
            For Each pair As String In _boxRecipients
                If pair.StartsWith(prefix, StringComparison.Ordinal) Then Return True
            Next
            Return False
        End SyncLock
    End Function

    ''' <summary>Reads the recipients again and rebuilds the box when they changed. A recipient that is gone (removed, or its key
    ''' changed, or it no longer qualifies) makes the own group key new and asks for a replacement upload with a new epoch. Returns
    ''' True when the record has to be published (a new box, or a replacement waiting): the caller sends `broadcast-update`, then
    ''' calls <see cref="MarkPublished"/>. An empty list is "the directory has not been read yet" (just after a welcome), not
    ''' "everyone was removed", so it changes nothing.</summary>
    ''' <summary>Credential revocation must rebuild the box even when its recipients are unchanged. Invalidate the old box
    ''' immediately, including offline, so it cannot be republished with a password the serving boundary already refuses.</summary>
    Public Sub SetCredentials(user As String, password As String)
        SyncLock _gate
            If _disposed Then Return
            user = If(user, "")
            password = If(password, "")
            If _credentialUser = user AndAlso _credentialPassword = password Then Return
            _credentialUser = user
            _credentialPassword = password
            _boxJson = Nothing
            _boxDirty = True
        End SyncLock
    End Sub

    Public Function Refresh() As Boolean
        Dim dir As IExchangeSealedDirectory = CurrentDirectory()
        If dir Is Nothing OrElse Not dir.CanSealThroughServer Then Return NeedsPublish()
        Dim recipients As IReadOnlyList(Of ExchangeSealedRecipient) = dir.Recipients()
        If recipients Is Nothing OrElse recipients.Count = 0 Then Return NeedsPublish()
        ' A box addresses at most 40 recipients (item DH): the directory lists them most recently seen first, so the first 40 stay.
        If recipients.Count > ExchangeSealedBox.MaxRecipients Then recipients = recipients.Take(ExchangeSealedBox.MaxRecipients).ToList()
        Dim pairs As New HashSet(Of String)(recipients.Select(Function(r) r.DeviceId & "|" & r.Kem), StringComparer.Ordinal)
        SyncLock _gate
            If _disposed Then Return False
            If _boxRecipients IsNot Nothing AndAlso _boxRecipients.SetEquals(pairs) AndAlso _boxJson IsNot Nothing Then Return NeedsPublishLocked()
            Dim dropped As Boolean = _boxRecipients IsNot Nothing AndAlso _boxRecipients.Except(pairs).Any()
            If dropped Then
                _keys.RotateRelayKey()
                _pendingReplace = True
            End If
            Dim box As String = BuildBox(dir, recipients)
            If box IsNot Nothing Then
                _boxJson = box
                _boxDirty = True
            ElseIf dropped AndAlso _boxJson IsNot Nothing Then
                ' The box on the server still carries the key that was just replaced: the record must not keep it.
                _boxJson = Nothing
                _boxDirty = True
            End If
            ' The set is remembered even when no box could be made (nothing to carry): the next change is measured against it.
            _boxRecipients = pairs
            Return NeedsPublishLocked()
        End SyncLock
    End Function

    Private Function NeedsPublish() As Boolean
        SyncLock _gate
            Return NeedsPublishLocked()
        End SyncLock
    End Function

    Private Function NeedsPublishLocked() As Boolean
        Return _boxDirty OrElse _pendingReplace
    End Function

    ''' <summary>The caller sent the record that holds <see cref="BoxJson"/>: the box is no longer new, and a waiting replacement is
    ''' released - the uploader sees <see cref="ReplaceSeq"/> move and opens the new epoch. Order matters: update first, upload after.</summary>
    Public Sub MarkPublished()
        SyncLock _gate
            _boxDirty = False
            If _pendingReplace Then
                _pendingReplace = False
                _replaceSeq += 1
            End If
        End SyncLock
    End Sub

    ''' <summary>A link was added or withdrawn, or the ring has to change for another reason: the next publish releases a replacement.</summary>
    Public Sub RequestReplacement()
        SyncLock _gate
            If Not _disposed Then _pendingReplace = True
        End SyncLock
    End Sub

    ''' <summary>A new key for one more outward link; the ring changes, so a replacement upload follows. "" at the ring bound.</summary>
    Public Function AddLink() As String
        Dim text As String = _keys.AddLink()
        If text.Length > 0 Then RequestReplacement()
        Return text
    End Function

    ''' <summary>The link is withdrawn: its key is dropped and the next epoch cannot be opened with it.</summary>
    Public Function WithdrawLink(keyText As String) As Boolean
        If Not _keys.WithdrawLink(keyText) Then Return False
        RequestReplacement()
        Return True
    End Function

    ''' <summary>A sender for a NEW epoch (item DL: every upload, first or replacement, opens one): a new EK and the ring of every
    ''' group key held now. The caller disposes it.</summary>
    Public Function NewSender() As ExchangeRelaySender
        Dim groupKeys As IReadOnlyList(Of Byte()) = _keys.Snapshot()
        Dim ek As Byte() = ExchangeRelayRecords.NewKey()
        Try
            Dim epoch As UInteger
            SyncLock _gate
                _epoch += 1UI
                epoch = _epoch
            End SyncLock
            Return New ExchangeRelaySender(_broadcastId, _mediaType, epoch, ek, groupKeys)
        Finally
            Array.Clear(ek, 0, ek.Length)
            For Each k As Byte() In groupKeys
                Array.Clear(k, 0, k.Length)
            Next
        End Try
    End Function

    Private Function CurrentDirectory() As IExchangeSealedDirectory
        Return If(_directory Is Nothing, Nothing, _directory.Invoke())
    End Function

    ''' <summary>The payload of the box (item DK): `relayKey` when a RELAY endpoint is listed, `tunnelPin` when the tunnel has one,
    ''' `credentials` when the broadcast is protected. Nothing when there is nothing to carry.</summary>
    Private Function BuildPayload() As Byte()
        If Not _hasRelay AndAlso _tunnelPin.Length = 0 AndAlso _credentialPassword.Length = 0 Then Return Nothing
        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms)
                w.WriteStartObject()
                If _hasRelay Then w.WriteString("relayKey", _keys.RelayKeyText())
                If _tunnelPin.Length > 0 Then w.WriteString("tunnelPin", _tunnelPin)
                If _credentialPassword.Length > 0 Then
                    w.WriteStartObject("credentials")
                    w.WriteString("user", _credentialUser)
                    w.WriteString("password", _credentialPassword)
                    w.WriteEndObject()
                End If
                w.WriteEndObject()
            End Using
            Return ms.ToArray()
        End Using
    End Function

    Private Function BuildBox(dir As IExchangeSealedDirectory, recipients As IReadOnlyList(Of ExchangeSealedRecipient)) As String
        Dim payload As Byte() = BuildPayload()
        If payload Is Nothing Then Return Nothing
        Try
            Using key As ExchangeDeviceKey = If(_loadKey Is Nothing, Nothing, _loadKey.Invoke())
                If key Is Nothing Then Return Nothing
                Dim result As ExchangeSealResult = ExchangeSealedBoxes.Seal(payload, ExchangeSealedContext.Broadcast(_broadcastId), dir.OwnDeviceId, key, recipients)
                If Not result.Succeeded Then Return Nothing
                Return result.Box.ToJson()
            End Using
        Finally
            Array.Clear(payload, 0, payload.Length)
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _gate
            _disposed = True
            _boxJson = Nothing
        End SyncLock
        _keys.Dispose()
    End Sub

    ''' <summary>The type name; never a key, a box or a credential.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedBroadcastSession"
    End Function

End Class
#End If
