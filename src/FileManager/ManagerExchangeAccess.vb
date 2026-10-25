#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading

''' <summary>Typed directory refusals, kept separate from producer/trust/PIN provider failures.</summary>
Friend NotInheritable Class ManagerExchangeException
    Inherits IO.IOException
    Friend Sub New(state As ManagerExchangeAvailability)
        MyBase.New("Exchange place refused: " & state.ToString())
        Me.State = state
    End Sub
    Friend ReadOnly Property State As ManagerExchangeAvailability
End Class

Friend Module ManagerExchangeAccess
    ''' <summary>Menus never dial. Hiding the device group does not disable configured resources.</summary>
    Friend Function CachedState(place As ManagerPlace, adapter As ManagerExchangePlaces) As ManagerExchangeAvailability
        If place.ExchangeEntryId.Length = 0 Then Return ManagerExchangeAvailability.Available
        If adapter IsNot Nothing Then Return adapter.StateOf(place)
        Return If(ExchangeEntryStore.CreateDefault().Find(place.ExchangeEntryId) Is Nothing,
                  ManagerExchangeAvailability.SignInRequired, ManagerExchangeAvailability.Available)
    End Function

    Friend Sub RequireAvailable(state As ManagerExchangeAvailability)
        If state <> ManagerExchangeAvailability.Available Then Throw New ManagerExchangeException(state)
    End Sub

    ''' <summary>One fresh read at a mutation boundary. No attach, permission expansion or mutation retry.</summary>
    Friend Sub Validate(place As ManagerPlace, descriptor As ResourceDescriptor, ct As CancellationToken,
                        Optional loadEntries As Func(Of IEnumerable(Of ExchangeEntry)) = Nothing,
                        Optional connect As Func(Of ExchangeEntry, ManagerExchangeConnection) = Nothing)
        If place.ExchangeEntryId.Length = 0 Then Return
        If ManagerPlace.ForResource(descriptor).ExchangeIdentity <> place.ExchangeIdentity Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Removed)
        Dim entry As ExchangeEntry = If(loadEntries, Function() ExchangeEntryStore.CreateDefault().Load())().FirstOrDefault(Function(e) e.Id = place.ExchangeEntryId)
        If entry Is Nothing Then Throw New ManagerExchangeException(ManagerExchangeAvailability.SignInRequired)
        Using connection As ManagerExchangeConnection = If(connect, AddressOf ManagerExchangePlaces.OpenEntry)(entry)
            Dim directory As ExchangeAgentDirectoryResult = connection.Api.GetDirectoryAsync(-1, 7000, ct).GetAwaiter().GetResult()
            Dim state As ExchangeAgentStateResult = connection.Api.GetStateAsync(ct).GetAwaiter().GetResult()
            If state.Info?.LastReason = "device-revoked" Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Revoked)
            If state.Info?.State = ExchangeAgentState.SignInAgain Then Throw New ManagerExchangeException(ManagerExchangeAvailability.SignInRequired)
            If Not directory.Reply.IsOk OrElse directory.Snapshot Is Nothing Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Offline)
            ValidateRecord(place, descriptor, directory.Snapshot)
        End Using
    End Sub

    Friend Sub ValidateRecord(place As ManagerPlace, descriptor As ResourceDescriptor, snapshot As ExchangeDirectorySnapshot)
        Dim record As ExchangeResourceRecord = snapshot.Resources.FirstOrDefault(Function(r) r.ResourceId = place.ExchangeResourceId AndAlso r.DeviceId = place.ExchangeDeviceId)
        If record Is Nothing Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Removed)
        If Not record.IsSupportedKind OrElse Not record.CanAuthorize Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Unsupported)
        If record.IsProtected Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Locked)
        Dim device As ExchangeDeviceRecord = snapshot.Devices.FirstOrDefault(Function(d) d.DeviceId = place.ExchangeDeviceId)
        If device Is Nothing OrElse Not device.IsOnline OrElse record.Presence <> ExchangeWire.PresenceOnline Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Offline)
        If record.HostKeyFingerprint.Length > 0 AndAlso record.HostKeyFingerprint <> descriptor.PinnedFingerprint Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Removed)
        ' Clear metadata, when supplied, is used only to detect a changed root, never to replace access.
        Dim access As ExchangeAccessInfo = Nothing
        If record.HasAccess AndAlso ExchangeResourceAccess.TryRead(record.AccessJson, record.Name, access) AndAlso
            Not ManagerPathRules.AreEqual(place.Kind, place.RootPath, access.Root) Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Removed)
    End Sub
End Module
#End If
