#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq

' Spec 234 T-1, DEVICE-EXCHANGE section 27 items DG, DP and DR. Who an account entry may seal to, and what a person is told
' about the devices that cannot be sealed to.
'
' A recipient is a device of the entry that this program shows in its own list (the directory it holds), that declares the
' capability `e2e-v1`, and whose key is the one this program saw first (trust on first use, ExchangeSealedTrust.vb). Three
' states follow, per device: Ready, E2eMissing (no capability or no key: item DP) and KeyChanged (the record shows another
' key than the kept one: item DG, nothing is sealed to it until the person removes it from the account). The entry as a whole
' has a fourth: ServerSealed, whether `welcome.features` lists `sealed`. A server that does not leaves the entry with presence
' and the directory only (item DP): no access published, no relay, no tunnel, no clipboard value sent through it.
'
' Nothing here touches the wire or a file of its own beyond the trust store, and nothing here is secret: keys are public.
' The users of the entry - the publisher (item DI), the broadcaster (item DK), the clipboard and the receivers - read it
' through IExchangeSealedDirectory, so that a test needs a fake of an interface and not a session.

''' <summary>The names of the sealed scheme that are not a row of a table: the capability and the server feature.</summary>
Friend NotInheritable Class ExchangeSealedNames

    Private Sub New()
    End Sub

    ''' <summary>The capability to seal and open (item DF), in `capabilities` of a device record.</summary>
    Public Const CapabilityE2e As String = "e2e-v1"
    ''' <summary>The `welcome.features` entry of a server that keeps and delivers `e2eKey`, `sealedAccess`, sealed boxes (item DP).</summary>
    Public Const FeatureSealed As String = "sealed"

End Class

''' <summary>Whether this program may seal to one device of the entry (item DG, item DP).</summary>
Friend Enum ExchangeSealState
    ''' <summary>A recipient: the capability, a key, and the key is the kept one.</summary>
    Ready = 0
    ''' <summary>No capability or no key in the record: nothing is sealed to it and nothing from it is used (`e2e-missing`).</summary>
    E2eMissing = 1
    ''' <summary>The record shows another key than the kept one: nothing is sealed to it (`key-changed`).</summary>
    KeyChanged = 2
End Enum

''' <summary>One device of the entry as the sealed layer sees it. Immutable; carries a public key and no secret.</summary>
Friend NotInheritable Class ExchangeSealedDevice

    Public ReadOnly DeviceId As String
    Public ReadOnly DeviceName As String
    ''' <summary>The `kem` the record lists ("" when it lists none or an invalid one); for this device itself, its own key.</summary>
    Public ReadOnly Kem As String
    Public ReadOnly LastSeen As String
    Public ReadOnly IsOnline As Boolean
    ''' <summary>This program's own device (its key is the trust anchor, not the directory's).</summary>
    Public ReadOnly IsSelf As Boolean
    Public ReadOnly State As ExchangeSealState

    Public Sub New(deviceId As String, deviceName As String, kem As String, lastSeen As String, isOnline As Boolean, isSelf As Boolean, state As ExchangeSealState)
        Me.DeviceId = deviceId
        Me.DeviceName = deviceName
        Me.Kem = If(kem, "")
        Me.LastSeen = If(lastSeen, "")
        Me.IsOnline = isOnline
        Me.IsSelf = isSelf
        Me.State = state
    End Sub

    ''' <summary>The check code of the listed key (item DG), "" when there is none. Showing it is optional, comparing it never required.</summary>
    Public ReadOnly Property CheckCode As String
        Get
            Return ExchangeDeviceKey.CheckCodeOfKem(Kem)
        End Get
    End Property

    ''' <summary>The row of item DR to show beside the device (`e2e-missing`, `key-changed`); Nothing for a device that is ready.
    ''' Its text comes from ConnectionDiagnosis (Message.Code), whole (item DA).</summary>
    Public ReadOnly Property Message As ConnectionDiagnosisMessage
        Get
            Select Case State
                Case ExchangeSealState.E2eMissing
                    Return ConnectionDiagnosisMessages.E2eMissing
                Case ExchangeSealState.KeyChanged
                    Return ConnectionDiagnosisMessages.KeyChanged
                Case Else
                    Return Nothing
            End Select
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return "ExchangeSealedDevice " & State.ToString()
    End Function

End Class

''' <summary>What the users of an entry ask the sealed layer. All values are a consistent reading of the last refresh.</summary>
Friend Interface IExchangeSealedDirectory
    ''' <summary>This program's own deviceId in the entry.</summary>
    ReadOnly Property OwnDeviceId As String
    ''' <summary>True when this program holds a usable key of its own: without it nothing is sealed or opened.</summary>
    ReadOnly Property HasOwnKey As Boolean
    ''' <summary>True once a `welcome` of this connection was read; False while not connected.</summary>
    ReadOnly Property ServerKnown As Boolean
    ''' <summary>True when the connected server lists `sealed` in its features. False while not connected.</summary>
    ReadOnly Property ServerSealed As Boolean
    ''' <summary>True when content may be sent through the server now: a connected server that is sealed, and a key of our own.</summary>
    ReadOnly Property CanSealThroughServer As Boolean
    ''' <summary>`server-no-e2e` when the connected server lists no `sealed`; Nothing otherwise (also while not connected).</summary>
    ReadOnly Property ServerMessage As ConnectionDiagnosisMessage
    ''' <summary>Moves by one whenever the set of recipients changes (a device joined, gained the capability, lost it, changed its
    ''' key or was removed, or the server's `sealed` flipped): item DI publishes again, item DK re-keys the broadcast.</summary>
    ReadOnly Property Revision As Long
    ''' <summary>Every device of the entry that the directory lists, with its state: the list the person sees.</summary>
    Function Devices() As IReadOnlyList(Of ExchangeSealedDevice)
    ''' <summary>The devices to seal to: Ready only, at most 40, the most recently seen first (online ones before the rest, then
    ''' by `lastSeen` newest first, then by id). Empty while the server is not sealed. This program's own device IS a recipient when it
    ''' is Ready (item DG: a recipient is any device this program lists with the capability and a trusted key, and its own key is the
    ''' anchor) - the sender can then read back what it sent, e.g. the value on its own clipboard slot. A caller that never reads its
    ''' own box passes <c>includeSelf:=False</c> to leave the slot to another device.</summary>
    Function Recipients(Optional includeSelf As Boolean = True) As IReadOnlyList(Of ExchangeSealedRecipient)
    ''' <summary>The state of one listed device; Nothing for an id the directory does not list.</summary>
    Function StateOf(deviceId As String) As Nullable(Of ExchangeSealState)
    ''' <summary>The key to hold a sender to when opening a box (the `trustedKeyForFrom` of ExchangeSealedBoxes.Open): this program's
    ''' own key for itself, the kept key of a Ready device, "" for anyone else - a missing, changed or unknown sender is not opened.</summary>
    Function TrustedKeyOf(deviceId As String) As String
    ''' <summary>Raised (on the thread that refreshed) when anything the person sees changed; read <see cref="Revision"/> to know
    ''' whether the recipient set did.</summary>
    Event Changed As EventHandler
End Interface

''' <summary>The recipients of one account entry: the directory, the kept keys and the server's features, read together.</summary>
Friend NotInheritable Class ExchangeSealedRecipients
    Implements IExchangeSealedDirectory

    Private ReadOnly _ownDeviceId As String
    Private ReadOnly _ownKem As String
    Private ReadOnly _trust As IExchangeSealedTrustStore
    Private ReadOnly _snapshot As Func(Of ExchangeDirectorySnapshot)
    Private ReadOnly _features As Func(Of IReadOnlyList(Of String))

    Private ReadOnly _gate As New Object()
    ' First sightings the trust store could not write: kept for this process (and written again at the next refresh) so that a
    ' full disk does not turn into a key that is trusted anew every start.
    Private ReadOnly _pending As New Dictionary(Of String, String)(StringComparer.Ordinal)
    ' Devices the person revoked: not listed and not trusted again while the directory still lists them.
    Private ReadOnly _revoked As New HashSet(Of String)(StringComparer.Ordinal)
    Private _devices As IReadOnlyList(Of ExchangeSealedDevice) = Array.Empty(Of ExchangeSealedDevice)()
    Private _serverKnown As Boolean
    Private _serverSealed As Boolean
    Private _revision As Long
    Private _setKey As String = ""
    Private _shownKey As String = ""

    Public Event Changed As EventHandler Implements IExchangeSealedDirectory.Changed

    ''' <param name="ownDeviceId">This program's deviceId in the entry; it also names the trust scope.</param>
    ''' <param name="ownKem">The `kem` of this program's own key, "" when it has none.</param>
    ''' <param name="trust">The kept keys; Nothing keeps them in memory for this object only.</param>
    ''' <param name="snapshot">The directory the entry holds now.</param>
    ''' <param name="serverFeatures">The `features` of the last `welcome`, or Nothing while not connected.</param>
    Public Sub New(ownDeviceId As String, ownKem As String, trust As IExchangeSealedTrustStore,
                   snapshot As Func(Of ExchangeDirectorySnapshot), serverFeatures As Func(Of IReadOnlyList(Of String)))
        If snapshot Is Nothing Then Throw New ArgumentNullException(NameOf(snapshot))
        If serverFeatures Is Nothing Then Throw New ArgumentNullException(NameOf(serverFeatures))
        _ownDeviceId = If(ownDeviceId, "")
        Dim point As Byte() = Nothing
        _ownKem = If(ExchangeDeviceKey.TryParseKem(ownKem, point), ownKem, "")
        _trust = If(trust, New ExchangeMemorySealedTrustStore())
        _snapshot = snapshot
        _features = serverFeatures
    End Sub

    Public ReadOnly Property OwnDeviceId As String Implements IExchangeSealedDirectory.OwnDeviceId
        Get
            Return _ownDeviceId
        End Get
    End Property

    Public ReadOnly Property HasOwnKey As Boolean Implements IExchangeSealedDirectory.HasOwnKey
        Get
            Return _ownKem.Length > 0
        End Get
    End Property

    Public ReadOnly Property ServerKnown As Boolean Implements IExchangeSealedDirectory.ServerKnown
        Get
            SyncLock _gate
                Return _serverKnown
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property ServerSealed As Boolean Implements IExchangeSealedDirectory.ServerSealed
        Get
            SyncLock _gate
                Return _serverSealed
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property CanSealThroughServer As Boolean Implements IExchangeSealedDirectory.CanSealThroughServer
        Get
            SyncLock _gate
                Return _serverSealed AndAlso HasOwnKey
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property ServerMessage As ConnectionDiagnosisMessage Implements IExchangeSealedDirectory.ServerMessage
        Get
            SyncLock _gate
                Return If(_serverKnown AndAlso Not _serverSealed, ConnectionDiagnosisMessages.ServerNoE2e, Nothing)
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Revision As Long Implements IExchangeSealedDirectory.Revision
        Get
            SyncLock _gate
                Return _revision
            End SyncLock
        End Get
    End Property

    Public Function Devices() As IReadOnlyList(Of ExchangeSealedDevice) Implements IExchangeSealedDirectory.Devices
        SyncLock _gate
            Return _devices
        End SyncLock
    End Function

    Public Function Recipients(Optional includeSelf As Boolean = True) As IReadOnlyList(Of ExchangeSealedRecipient) Implements IExchangeSealedDirectory.Recipients
        Dim list As IReadOnlyList(Of ExchangeSealedDevice)
        Dim sealedServer As Boolean
        SyncLock _gate
            list = _devices
            sealedServer = _serverSealed
        End SyncLock
        Dim result As New List(Of ExchangeSealedRecipient)
        If Not sealedServer Then Return result
        Return list.Where(Function(d) d.State = ExchangeSealState.Ready AndAlso (includeSelf OrElse Not d.IsSelf)).
            OrderByDescending(Function(d) d.IsOnline).
            ThenByDescending(Function(d) d.LastSeen, StringComparer.Ordinal).
            ThenBy(Function(d) d.DeviceId, StringComparer.Ordinal).
            Take(ExchangeSealedBox.MaxRecipients).
            Select(Function(d) New ExchangeSealedRecipient(d.DeviceId, d.Kem)).ToList()
    End Function

    Public Function StateOf(deviceId As String) As Nullable(Of ExchangeSealState) Implements IExchangeSealedDirectory.StateOf
        If String.IsNullOrEmpty(deviceId) Then Return Nothing
        SyncLock _gate
            For Each d As ExchangeSealedDevice In _devices
                If String.Equals(d.DeviceId, deviceId, StringComparison.Ordinal) Then Return d.State
            Next
        End SyncLock
        Return Nothing
    End Function

    Public Function TrustedKeyOf(deviceId As String) As String Implements IExchangeSealedDirectory.TrustedKeyOf
        If String.IsNullOrEmpty(deviceId) Then Return ""
        If String.Equals(deviceId, _ownDeviceId, StringComparison.Ordinal) Then Return _ownKem
        SyncLock _gate
            For Each d As ExchangeSealedDevice In _devices
                If String.Equals(d.DeviceId, deviceId, StringComparison.Ordinal) Then
                    Return If(d.State = ExchangeSealState.Ready, d.Kem, "")
                End If
            Next
        End SyncLock
        Return ""
    End Function

    ' --- the owner of the entry (the control session) ---

    ''' <summary>Reads the directory, the kept keys and the features again. The first sight of a device that declares the capability
    ''' with a valid key keeps that key (trust on first use). Called after every `directory`, every applied `changed`, every
    ''' `welcome` and when the connection is cleared.</summary>
    Public Sub Refresh()
        Apply(_snapshot.Invoke())
    End Sub

    Private Sub Apply(snapshot As ExchangeDirectorySnapshot)
        Dim features As IReadOnlyList(Of String) = _features.Invoke()
        Dim raise As Boolean
        SyncLock _gate
            Dim devices As List(Of ExchangeSealedDevice) = Evaluate(If(snapshot, ExchangeDirectorySnapshot.Empty))
            Dim known As Boolean = features IsNot Nothing
            Dim sealedServer As Boolean = known AndAlso features.Contains(ExchangeSealedNames.FeatureSealed)

            Dim setKey As String = If(sealedServer, "s", "n") & ";" & String.Join(",", devices.Where(Function(d) d.State = ExchangeSealState.Ready).
                Select(Function(d) d.DeviceId & ":" & d.Kem).OrderBy(Function(s) s, StringComparer.Ordinal))
            Dim shownKey As String = setKey & ";" & If(known, "k", "u") & ";" & String.Join(",", devices.Select(Function(d) d.DeviceId & ":" & CInt(d.State).ToString(Globalization.CultureInfo.InvariantCulture) & ":" & d.Kem))

            _devices = devices
            _serverKnown = known
            _serverSealed = sealedServer
            If Not String.Equals(setKey, _setKey, StringComparison.Ordinal) Then
                _setKey = setKey
                _revision += 1
            End If
            If Not String.Equals(shownKey, _shownKey, StringComparison.Ordinal) Then
                _shownKey = shownKey
                raise = True
            End If
        End SyncLock
        If raise Then RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    ''' <summary>The person revoked the device (`revoke`, 5.2): its kept key is dropped (item DG) and it is no longer listed, nor
    ''' trusted anew while the directory still lists it. Call it when the server has answered the revoke.</summary>
    Public Sub Forget(deviceId As String)
        If String.IsNullOrEmpty(deviceId) OrElse String.Equals(deviceId, _ownDeviceId, StringComparison.Ordinal) Then Return
        SyncLock _gate
            _trust.Forget(_ownDeviceId, deviceId)
            _pending.Remove(deviceId)
            _revoked.Add(deviceId)
        End SyncLock
        Refresh()
    End Sub

    ''' <summary>The entry is removed or the account is left: every kept key of the entry goes.</summary>
    Public Sub ForgetAll()
        SyncLock _gate
            _trust.ForgetScope(_ownDeviceId)
            _pending.Clear()
            _revoked.Clear()
        End SyncLock
        ' the view is emptied, not re-read: the directory may still list the devices for a moment, and reading it would keep a first
        ' key again for an entry that has just been left. The next Refresh after a (re)connection is a first sight, as it should be.
        Apply(ExchangeDirectorySnapshot.Empty)
    End Sub

    ' --- evaluation; the caller holds the gate ---

    Private Function Evaluate(snapshot As ExchangeDirectorySnapshot) As List(Of ExchangeSealedDevice)
        Dim present As New HashSet(Of String)(StringComparer.Ordinal)
        For Each rec As ExchangeDeviceRecord In snapshot.Devices
            present.Add(rec.DeviceId)
        Next
        ' a revoked device that the directory no longer lists is gone for good; a later device under that id is a new first sight.
        ' Only a directory that was actually read says so: the empty one of a connection that ended (revision 0) proves nothing, and the
        ' server may not have taken the revoke yet.
        If snapshot.Revision > 0 Then _revoked.RemoveWhere(Function(id) Not present.Contains(id))

        Dim list As New List(Of ExchangeSealedDevice)
        For Each rec As ExchangeDeviceRecord In snapshot.Devices
            If _revoked.Contains(rec.DeviceId) Then Continue For
            Dim isSelf As Boolean = String.Equals(rec.DeviceId, _ownDeviceId, StringComparison.Ordinal)
            If isSelf Then
                list.Add(EvaluateSelf(rec))
            Else
                list.Add(EvaluateOther(rec))
            End If
        Next
        Return list.OrderByDescending(Function(d) d.IsSelf).
            ThenByDescending(Function(d) d.IsOnline).
            ThenBy(Function(d) d.DeviceName, StringComparer.OrdinalIgnoreCase).
            ThenBy(Function(d) d.DeviceId, StringComparer.Ordinal).ToList()
    End Function

    ''' <summary>Our own key is the anchor: the directory is checked against it, never the other way round.</summary>
    Private Function EvaluateSelf(rec As ExchangeDeviceRecord) As ExchangeSealedDevice
        Dim state As ExchangeSealState
        If _ownKem.Length = 0 Then
            state = ExchangeSealState.E2eMissing
        ElseIf rec.E2eKem.Length = 0 Then
            ' the server holds no key of ours (a device enrolled before it had one): it cannot be sealed to
            state = ExchangeSealState.E2eMissing
        ElseIf Not String.Equals(rec.E2eKem, _ownKem, StringComparison.Ordinal) Then
            state = ExchangeSealState.KeyChanged
        ElseIf Not rec.HasCapability(ExchangeSealedNames.CapabilityE2e) Then
            state = ExchangeSealState.E2eMissing
        Else
            state = ExchangeSealState.Ready
        End If
        Return New ExchangeSealedDevice(rec.DeviceId, rec.DeviceName, If(_ownKem.Length > 0, _ownKem, rec.E2eKem), rec.LastSeen, rec.IsOnline, True, state)
    End Function

    Private Function EvaluateOther(rec As ExchangeDeviceRecord) As ExchangeSealedDevice
        Dim declares As Boolean = rec.HasCapability(ExchangeSealedNames.CapabilityE2e)
        Dim kept As String = _trust.TryGet(_ownDeviceId, rec.DeviceId)
        Dim pendingKem As String = Nothing
        If kept.Length = 0 AndAlso _pending.TryGetValue(rec.DeviceId, pendingKem) Then
            kept = pendingKem
            If _trust.Keep(_ownDeviceId, rec.DeviceId, kept) Then _pending.Remove(rec.DeviceId)
        End If

        Dim state As ExchangeSealState
        If kept.Length > 0 Then
            ' a key lives as long as its id (item DF): another key, or none where one was, is never legitimate
            If Not String.Equals(rec.E2eKem, kept, StringComparison.Ordinal) Then
                state = ExchangeSealState.KeyChanged
            ElseIf declares Then
                state = ExchangeSealState.Ready
            Else
                state = ExchangeSealState.E2eMissing
            End If
        ElseIf declares AndAlso rec.E2eKem.Length > 0 Then
            ' first sight: keep this key
            If Not _trust.Keep(_ownDeviceId, rec.DeviceId, rec.E2eKem) Then _pending(rec.DeviceId) = rec.E2eKem
            state = ExchangeSealState.Ready
        Else
            state = ExchangeSealState.E2eMissing
        End If
        Return New ExchangeSealedDevice(rec.DeviceId, rec.DeviceName, rec.E2eKem, rec.LastSeen, rec.IsOnline, False, state)
    End Function

    Public Overrides Function ToString() As String
        Return "ExchangeSealedRecipients"
    End Function

End Class
#End If
