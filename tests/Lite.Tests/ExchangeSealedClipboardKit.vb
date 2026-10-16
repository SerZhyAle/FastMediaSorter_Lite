#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

' Spec 234 T-4: what the sealed clipboard tests share - a device of the account with a real key pair, the directory of an entry
' (who a value is sealed for, which sender key a box is held to), and the other end of a sealed value: a payload written by hand,
' a value sealed to chosen devices, a box opened with a device's key. Compiled into Lite.Tests (the library client) and linked into
' Companion.Tests (the Share Manager's send and get), so both speak about the same wire shape.

''' <summary>A device of the account with a real key pair.</summary>
Friend NotInheritable Class SealedTestDevice
    Implements IDisposable

    Public ReadOnly Id As String
    Public ReadOnly Key As ExchangeDeviceKey

    Public Sub New(id As String)
        Me.Id = id
        Key = ExchangeDeviceKey.Generate()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Key.Dispose()
    End Sub

End Class

''' <summary>The sealed directory of one entry: the devices a test lists, switches for what the connected server says and for what this
''' device holds, and a kept key per device a test can change (a sender whose key is not the one this device kept).</summary>
Friend NotInheritable Class SealedTestDirectory
    Implements IExchangeSealedDirectory

    Private ReadOnly _gate As New Object()
    Private ReadOnly _devices As New List(Of SealedTestDevice)()
    Private ReadOnly _kept As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private _revision As Long

    Public ReadOnly Own As SealedTestDevice
    ''' <summary>False models a directory that has not heard from its server yet (the exchange must find out by itself).</summary>
    Public Property ServerHeard As Boolean = True
    Public Property ServerSupportsSealed As Boolean = True
    ''' <summary>This program has no usable key of its own.</summary>
    Public Property OwnKeyMissing As Boolean
    ''' <summary>No device of the entry is Ready: the recipient list is empty.</summary>
    Public Property NoRecipientReady As Boolean
    ''' <summary>Recipients() calls seen, to show that a directory was consulted.</summary>
    Public RecipientsCalls As Integer

    Public Sub New(own As SealedTestDevice)
        Me.Own = own
        _devices.Add(own)
    End Sub

    Public Sub Add(device As SealedTestDevice)
        SyncLock _gate
            _devices.Add(device)
            _revision += 1
        End SyncLock
    End Sub

    ''' <summary>The key this device keeps for <paramref name="deviceId"/> from now on (a test models a changed sender key with another kem).</summary>
    Public Sub Keep(deviceId As String, kem As String)
        SyncLock _gate
            _kept(deviceId) = kem
        End SyncLock
    End Sub

    Public ReadOnly Property OwnDeviceId As String Implements IExchangeSealedDirectory.OwnDeviceId
        Get
            Return Own.Id
        End Get
    End Property

    Public ReadOnly Property HasOwnKey As Boolean Implements IExchangeSealedDirectory.HasOwnKey
        Get
            Return Not OwnKeyMissing
        End Get
    End Property

    Public ReadOnly Property ServerKnown As Boolean Implements IExchangeSealedDirectory.ServerKnown
        Get
            Return ServerHeard
        End Get
    End Property

    Public ReadOnly Property ServerSealed As Boolean Implements IExchangeSealedDirectory.ServerSealed
        Get
            Return ServerHeard AndAlso ServerSupportsSealed
        End Get
    End Property

    Public ReadOnly Property CanSealThroughServer As Boolean Implements IExchangeSealedDirectory.CanSealThroughServer
        Get
            Return ServerHeard AndAlso ServerSupportsSealed AndAlso Not OwnKeyMissing
        End Get
    End Property

    Public ReadOnly Property ServerMessage As ConnectionDiagnosisMessage Implements IExchangeSealedDirectory.ServerMessage
        Get
            Return If(ServerHeard AndAlso Not ServerSupportsSealed, ConnectionDiagnosisMessages.ServerNoE2e, Nothing)
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
            Return _devices.Select(Function(d) New ExchangeSealedDevice(d.Id, d.Id, d.Key.Kem, "", True, d Is Own, ExchangeSealState.Ready)).ToList()
        End SyncLock
    End Function

    Public Function Recipients(Optional includeSelf As Boolean = True) As IReadOnlyList(Of ExchangeSealedRecipient) Implements IExchangeSealedDirectory.Recipients
        SyncLock _gate
            RecipientsCalls += 1
            If NoRecipientReady OrElse Not ServerSupportsSealed Then Return New List(Of ExchangeSealedRecipient)()
            Return _devices.Where(Function(d) includeSelf OrElse d IsNot Own).
                Select(Function(d) New ExchangeSealedRecipient(d.Id, d.Key.Kem)).ToList()
        End SyncLock
    End Function

    Public Function StateOf(deviceId As String) As Nullable(Of ExchangeSealState) Implements IExchangeSealedDirectory.StateOf
        SyncLock _gate
            If _devices.Any(Function(d) String.Equals(d.Id, deviceId, StringComparison.Ordinal)) Then Return ExchangeSealState.Ready
            Return Nothing
        End SyncLock
    End Function

    Public Function TrustedKeyOf(deviceId As String) As String Implements IExchangeSealedDirectory.TrustedKeyOf
        SyncLock _gate
            Dim kept As String = Nothing
            If _kept.TryGetValue(deviceId, kept) Then Return kept
            For Each d As SealedTestDevice In _devices
                If String.Equals(d.Id, deviceId, StringComparison.Ordinal) Then Return d.Key.Kem
            Next
            Return ""
        End SyncLock
    End Function

    Public Event Changed As EventHandler Implements IExchangeSealedDirectory.Changed

    ''' <summary>Raises <see cref="Changed"/> (a test that wants a subscriber to run).</summary>
    Public Sub Raise()
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

End Class

''' <summary>A value as the sealed clipboard keeps it: the box, the sealed bytes, their hash. Built here the way a sending device
''' builds it, from members written by hand, so a test can also build the values no conforming sender would.</summary>
Friend NotInheritable Class SealedTestValue

    Public ReadOnly BoxJson As String
    Public ReadOnly SealedBytes As Byte()
    Public ReadOnly SealedSha As String
    Public ReadOnly ValueKey As Byte()

    Private Sub New(boxJson As String, sealedBytes As Byte(), valueKey As Byte())
        Me.BoxJson = boxJson
        Me.SealedBytes = sealedBytes
        Me.ValueKey = valueKey
        SealedSha = Convert.ToHexString(SHA256.HashData(sealedBytes)).ToLowerInvariant()
    End Sub

    Public Shared Function Sha(bytes As Byte()) As String
        Return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    End Function

    ''' <summary>A value as it arrived on a wire: the box and the sealed bytes, with no key of its own.</summary>
    Public Shared Function Wrap(boxJson As String, sealedBytes As Byte()) As SealedTestValue
        Return New SealedTestValue(boxJson, sealedBytes, Nothing)
    End Function

    ''' <summary>The payload of the box as JSON bytes, members in the order of the contract. Not checked: a test may hand in a name
    ''' or a hash that no conforming sender writes.</summary>
    Public Shared Function PayloadJson(kind As String, mimeType As String, name As String, size As Long, sha256 As String) As Byte()
        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms)
                w.WriteStartObject()
                w.WriteString("kind", kind)
                If Not String.IsNullOrEmpty(mimeType) Then w.WriteString("mimeType", mimeType)
                If Not String.IsNullOrEmpty(name) Then w.WriteString("name", name)
                w.WriteNumber("size", size)
                w.WriteString("sha256", sha256)
                w.WriteEndObject()
            End Using
            Return ms.ToArray()
        End Using
    End Function

    ''' <summary>Seals <paramref name="sealedClear"/> (the bytes that become segments) under a box that states <paramref name="payloadJson"/>,
    ''' sent by <paramref name="sender"/> to <paramref name="recipients"/>.</summary>
    Public Shared Function Make(sealedClear As Byte(), payloadJson As Byte(), sender As SealedTestDevice, recipients As IEnumerable(Of SealedTestDevice)) As SealedTestValue
        Dim list As IReadOnlyList(Of ExchangeSealedRecipient) = recipients.Select(Function(d) New ExchangeSealedRecipient(d.Id, d.Key.Kem)).ToList()
        Dim result As ExchangeSealResult = ExchangeSealedBoxes.Seal(payloadJson, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, list, True)
        Try
            If Not result.Succeeded Then Throw New InvalidOperationException("the test box did not seal: " & result.Outcome.ToString())
            Dim key As Byte() = ExchangeClipboardSeal.ValueKey(result.ContentKey)
            Return New SealedTestValue(result.Box.ToJson(), ExchangeClipboardSeal.SealBytes(sealedClear, key), key)
        Finally
            result.Wipe()
        End Try
    End Function

    ''' <summary>A conforming value: payload members from the clear bytes themselves.</summary>
    Public Shared Function MakeClear(clear As Byte(), kind As String, mimeType As String, name As String,
                                     sender As SealedTestDevice, recipients As IEnumerable(Of SealedTestDevice)) As SealedTestValue
        Return Make(clear, PayloadJson(kind, mimeType, name, clear.LongLength, Sha(clear)), sender, recipients)
    End Function

    ''' <summary>What a receiver does with a box: opens it with its key, holding the sender to <paramref name="trustedKemOfSender"/>.
    ''' Nothing on any failure.</summary>
    Public Shared Function OpenBox(boxJson As String, receiver As SealedTestDevice, trustedKemOfSender As String,
                                   ByRef payload As ExchangeClipboardSealedPayload) As Byte()
        payload = Nothing
        Dim box As ExchangeSealedBox = Nothing
        If Not ExchangeSealedBox.TryParse(boxJson, box) Then Return Nothing
        Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, receiver.Id, receiver.Key, ExchangeSealedContext.Clipboard,
                                                                          Function(deviceId As String) trustedKemOfSender, True)
        Try
            If Not opened.Succeeded Then Return Nothing
            If Not ExchangeClipboardSealedPayload.TryParse(opened.Payload, payload) Then Return Nothing
            Return ExchangeClipboardSeal.ValueKey(opened.ContentKey)
        Finally
            opened.Wipe()
        End Try
    End Function

    ''' <summary>The clear bytes of a sealed value for a receiver; Nothing when the box, a tag, the size or the hash does not hold.</summary>
    Public Shared Function OpenValue(boxJson As String, sealedBytes As Byte(), receiver As SealedTestDevice, trustedKemOfSender As String,
                                     ByRef payload As ExchangeClipboardSealedPayload) As Byte()
        Dim key As Byte() = OpenBox(boxJson, receiver, trustedKemOfSender, payload)
        If key Is Nothing Then Return Nothing
        Try
            Return ExchangeClipboardSeal.OpenBytes(sealedBytes, key, payload.Size, payload.Sha256)
        Finally
            Array.Clear(key, 0, key.Length)
        End Try
    End Function

End Class
#End If
