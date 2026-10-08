#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net
Imports System.Text.Json

' Ticket 171 rules 3-5 (DEVICE-EXCHANGE 9 rules 1-3): the pure half of attaching a resource of the user's other devices. No registry, no
' pipe, no clock, no text - the sync calls it, the panel draws its answers, the tests pin it.
'
'   - ATTACH is the user's mark: a producer's record (its name, its device, its `access`) becomes a resource of kind SFTP whose Origin is
'     Exchange, bound to the producer's resource id; the host list of the code is the candidate list, the fingerprint the pin, the password
'     goes through ResourceCommit (a DPAPI blob under a handle) and never into the registry JSON;
'   - FOLLOWING the producer: a later `access` that differs REPLACES the kept one (new password, new address set, new pin) and the user is
'     told once on the row, because a host key is an identity - a quiet swap would hide a reinstall or worse (Q171-3);
'   - STATES, and the rule that absence is not authority: `available`; `device offline` when the producer's device is offline;
'     `no longer published` ONLY when a successful full list shows the id gone (or its device revoked); and `unknown` - no change of state
'     at all - when the directory could not be fetched. NOTHING in this file or its callers deletes, renames or edits a resource because of
'     a state: removal is the user's act (VERSIONING.md 4.5), and the source scan of ExchangeAttachTests holds the word out of the sync.

Friend Enum ExchangeResourceState
    ''' <summary>The producer is online and publishes the resource.</summary>
    Available = 0
    ''' <summary>The producing device is offline: the resource is listed without its access, and comes back by itself.</summary>
    DeviceOffline = 1
    ''' <summary>A successful full list does not hold the id (unpublished, or its device was revoked).</summary>
    NoLongerPublished = 2
    ''' <summary>The directory could not be fetched (no agent, not connected): nothing is known, so nothing changes.</summary>
    Unknown = 3
End Enum

''' <summary>What a producer's record becomes once attached. Holds the password until it is protected: never printed, never logged.</summary>
Friend NotInheritable Class ExchangeAttachDraft

    Friend ReadOnly Property Descriptor As ResourceDescriptor
    Friend ReadOnly Property Password As String

    Friend Sub New(descriptor As ResourceDescriptor, password As String)
        Me.Descriptor = descriptor
        Me.Password = password
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeAttachDraft"
    End Function

End Class

''' <summary>What following the producer decided about one attached resource.</summary>
Friend NotInheritable Class ExchangeFollowResult

    ''' <summary>The resource as it should be stored (the same object when nothing changed).</summary>
    Friend ReadOnly Property Descriptor As ResourceDescriptor
    ''' <summary>The new password to commit with it, or Nothing when the stored one stays.</summary>
    Friend ReadOnly Property NewPassword As String
    ''' <summary>The producer's access differs from the kept one: address set, host key, user, root or password.</summary>
    Friend ReadOnly Property Changed As Boolean

    Friend Sub New(descriptor As ResourceDescriptor, newPassword As String, changed As Boolean)
        Me.Descriptor = descriptor
        Me.NewPassword = newPassword
        Me.Changed = changed
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeFollowResult"
    End Function

End Class

Friend NotInheritable Class ExchangeAttach
    Private Sub New()
    End Sub

    ''' <summary>The kind an address is stored under, in the producer's own vocabulary (<c>lan</c>, <c>ipv6</c>, <c>portforward</c>): an IPv6
    ''' literal is <c>ipv6</c>, a private or link-local IPv4 address <c>lan</c>, anything else (a public address or a name) <c>portforward</c>.
    ''' The kind is a label for the list; the resolver races them all.</summary>
    Friend Shared Function KindOfHost(host As String) As String
        Dim ip As IPAddress = Nothing
        If IPAddress.TryParse(If(host, "").Trim("["c, "]"c), ip) Then
            If ip.AddressFamily = Sockets.AddressFamily.InterNetworkV6 Then Return "ipv6"
            Dim b As Byte() = ip.GetAddressBytes()
            If b(0) = 10 OrElse (b(0) = 172 AndAlso b(1) >= 16 AndAlso b(1) <= 31) OrElse (b(0) = 192 AndAlso b(1) = 168) OrElse
               (b(0) = 169 AndAlso b(1) = 254) OrElse (b(0) = 100 AndAlso b(1) >= 64 AndAlso b(1) <= 127) Then Return "lan"
        End If
        Return "portforward"
    End Function

    ''' <summary>The resource a producer's record becomes. Nothing for a record that cannot be attached: no `access` (an offline producer's),
    ''' one that is not whole, a key login (this viewer opens a share with a password), or a root that is not a path the viewer can use.</summary>
    Friend Shared Function BuildDraft(resourceId As String, deviceId As String, deviceName As String, name As String, accessJson As String,
                               newId As Func(Of String)) As ExchangeAttachDraft
        Dim info As ExchangeAccessInfo = Nothing
        If Not ExchangeResourceAccess.TryRead(accessJson, name, info) Then Return Nothing
        If String.IsNullOrEmpty(info.Code.Password) Then Return Nothing
        Dim root As String = FmsConfigReader.CanonicalVirtualPath(info.Root)
        If String.IsNullOrEmpty(root) Then Return Nothing

        Dim hosts As IReadOnlyList(Of String) = info.Code.Hosts
        Dim descriptor As New ResourceDescriptor With {
            .Id = newId(), .DisplayName = If(String.IsNullOrWhiteSpace(name), hosts(0), name.Trim()), .Kind = ResourceKind.Sftp,
            .Host = hosts(0), .Port = info.Code.Port, .UserName = info.Code.Username, .RootPath = root,
            .ReadOnlyHint = False, .PinnedFingerprint = info.Code.Fingerprint,
            .Endpoints = hosts.Select(Function(h) New ResourceEndpoint With {.Kind = KindOfHost(h), .Host = h, .Port = info.Code.Port}).ToList(),
            .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = resourceId, .ExchangeDeviceId = deviceId, .ExchangeDeviceName = deviceName}
        Return New ExchangeAttachDraft(descriptor, info.Code.Password)
    End Function

    ''' <summary>Whether the kept resource already says what the draft says: the same addresses in the same order, port, user, root, pin and password.</summary>
    Friend Shared Function SameAccess(existing As ResourceDescriptor, existingPassword As String, draft As ExchangeAttachDraft) As Boolean
        If existing Is Nothing OrElse draft Is Nothing Then Return False
        Dim d As ResourceDescriptor = draft.Descriptor
        If Not String.Equals(existing.Host, d.Host, StringComparison.OrdinalIgnoreCase) OrElse existing.Port <> d.Port OrElse
           Not String.Equals(existing.UserName, d.UserName, StringComparison.Ordinal) OrElse
           Not String.Equals(existing.RootPath, d.RootPath, StringComparison.Ordinal) OrElse
           Not String.Equals(existing.PinnedFingerprint, d.PinnedFingerprint, StringComparison.Ordinal) Then Return False
        Dim a As List(Of String) = HostsOf(existing)
        Dim b As List(Of String) = HostsOf(d)
        If a.Count <> b.Count Then Return False
        For i As Integer = 0 To a.Count - 1
            If Not String.Equals(a(i), b(i), StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        Return String.Equals(If(existingPassword, ""), draft.Password, StringComparison.Ordinal)
    End Function

    Private Shared Function HostsOf(r As ResourceDescriptor) As List(Of String)
        Dim list As New List(Of String)
        If r.Endpoints IsNot Nothing Then
            For Each e As ResourceEndpoint In r.Endpoints
                list.Add(e.Host & ":" & e.Port.ToString(CultureInfo.InvariantCulture))
            Next
        End If
        If list.Count = 0 Then list.Add(r.Host & ":" & r.Port.ToString(CultureInfo.InvariantCulture))
        Return list
    End Function

    ''' <summary>Follows the producer (item 9.2, Q171-3): the kept resource takes the draft's addresses, user, root, pin and password, and
    ''' carries a notice when anything of them changed. The resource's id, its name, its last-used time and everything the user may have set
    ''' stay; the same object comes back unchanged when the access is the same. <paramref name="existingPassword"/> is what is stored now.</summary>
    Friend Shared Function Follow(existing As ResourceDescriptor, existingPassword As String, draft As ExchangeAttachDraft, deviceName As String) As ExchangeFollowResult
        If Not ResourceOrigin.IsExchange(existing) OrElse draft Is Nothing Then Return New ExchangeFollowResult(existing, Nothing, False)
        Dim d As ResourceDescriptor = draft.Descriptor
        Dim changed As Boolean = Not SameAccess(existing, existingPassword, draft)
        Dim nameChanged As Boolean = Not String.Equals(If(deviceName, ""), If(existing.ExchangeDeviceName, ""), StringComparison.Ordinal)
        If Not changed AndAlso Not nameChanged Then Return New ExchangeFollowResult(existing, Nothing, False)

        Dim next_ As ResourceDescriptor = System.Text.Json.JsonSerializer.Deserialize(Of ResourceDescriptor)(System.Text.Json.JsonSerializer.Serialize(existing))
        next_.ExchangeDeviceName = If(String.IsNullOrEmpty(deviceName), existing.ExchangeDeviceName, deviceName)
        If Not changed Then Return New ExchangeFollowResult(next_, Nothing, False)
        next_.Host = d.Host
        next_.Port = d.Port
        next_.UserName = d.UserName
        next_.RootPath = d.RootPath
        next_.PinnedFingerprint = d.PinnedFingerprint
        next_.Endpoints = d.Endpoints
        next_.ExchangeNotice = True
        Return New ExchangeFollowResult(next_, If(String.Equals(If(existingPassword, ""), draft.Password, StringComparison.Ordinal), Nothing, draft.Password), True)
    End Function

    ''' <summary>The state of an attached resource against the last directory (item 5). Absence is not authority: the state is
    ''' <see cref="ExchangeResourceState.Unknown"/> - and so no change at all - unless the directory is a successful FULL list.</summary>
    Friend Shared Function StateOf(resourceId As String, snapshot As ExchangeDirectorySnapshot, fullList As Boolean) As ExchangeResourceState
        If snapshot Is Nothing OrElse Not fullList Then Return ExchangeResourceState.Unknown
        Dim record As ExchangeResourceRecord = snapshot.Resources.FirstOrDefault(Function(r) String.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
        If record Is Nothing Then Return ExchangeResourceState.NoLongerPublished
        ' a record whose device is not in the list is a revoked device's (the server removes a device's records with it)
        If record.DeviceId.Length > 0 AndAlso Not snapshot.Devices.Any(Function(d) String.Equals(d.DeviceId, record.DeviceId, StringComparison.Ordinal)) Then
            Return ExchangeResourceState.NoLongerPublished
        End If
        Return If(String.Equals(record.Presence, ExchangeWire.PresenceOnline, StringComparison.Ordinal), ExchangeResourceState.Available, ExchangeResourceState.DeviceOffline)
    End Function

End Class

' --- the "My devices" list, as data -----------------------------------------------------------------------------------------

Friend NotInheritable Class ExchangeDeviceRow
    Friend ReadOnly Property ResourceId As String
    Friend ReadOnly Property Name As String
    Friend ReadOnly Property Kind As String
    ''' <summary>The viewer can use this kind (v1: `sftp-share`); another is shown as "not supported by this version", never as an error.</summary>
    Friend ReadOnly Property Supported As Boolean
    Friend ReadOnly Property DeviceOnline As Boolean
    ''' <summary>Already attached (ticked).</summary>
    Friend ReadOnly Property Attached As Boolean
    Friend ReadOnly Property State As ExchangeResourceState

    Friend Sub New(resourceId As String, name As String, kind As String, supported As Boolean, deviceOnline As Boolean, attached As Boolean, state As ExchangeResourceState)
        Me.ResourceId = resourceId
        Me.Name = name
        Me.Kind = kind
        Me.Supported = supported
        Me.DeviceOnline = deviceOnline
        Me.Attached = attached
        Me.State = state
    End Sub
End Class

Friend NotInheritable Class ExchangeDeviceGroup
    Friend ReadOnly Property DeviceId As String
    Friend ReadOnly Property Name As String
    Friend ReadOnly Property Online As Boolean
    Friend ReadOnly Property Rows As IReadOnlyList(Of ExchangeDeviceRow)

    Friend Sub New(deviceId As String, name As String, online As Boolean, rows As IReadOnlyList(Of ExchangeDeviceRow))
        Me.DeviceId = deviceId
        Me.Name = name
        Me.Online = online
        Me.Rows = rows
    End Sub
End Class

Friend NotInheritable Class ExchangeDevicesModel
    Private Sub New()
    End Sub

    ''' <summary>The directory grouped by device, with presence: online devices first, then by name; a device's resources by name. The
    ''' snapshot is the agent's - it already leaves out this device's own records (9 rule 1), but a record of this device is dropped here
    ''' too, so the rule does not depend on the channel. An unknown kind is kept (supported = False).</summary>
    Friend Shared Function Build(snapshot As ExchangeDirectorySnapshot, attached As IEnumerable(Of ResourceDescriptor), ownDeviceId As String) As List(Of ExchangeDeviceGroup)
        Dim groups As New List(Of ExchangeDeviceGroup)
        If snapshot Is Nothing Then Return groups
        Dim bound As New Dictionary(Of String, ResourceDescriptor)(StringComparer.Ordinal)
        For Each r As ResourceDescriptor In If(attached, Enumerable.Empty(Of ResourceDescriptor)())
            If ResourceOrigin.IsExchange(r) AndAlso Not String.IsNullOrEmpty(r.ExchangeResourceId) Then bound(r.ExchangeResourceId) = r
        Next
        Dim ordered As IEnumerable(Of ExchangeDeviceRecord) = snapshot.Devices.
            Where(Function(d) String.IsNullOrEmpty(ownDeviceId) OrElse Not String.Equals(d.DeviceId, ownDeviceId, StringComparison.Ordinal)).
            OrderBy(Function(d) If(d.IsOnline, 0, 1)).ThenBy(Function(d) d.DeviceName, StringComparer.CurrentCultureIgnoreCase)
        For Each device As ExchangeDeviceRecord In ordered
            Dim rows As New List(Of ExchangeDeviceRow)
            For Each r As ExchangeResourceRecord In snapshot.Resources.Where(Function(x) String.Equals(x.DeviceId, device.DeviceId, StringComparison.Ordinal)).
                    OrderBy(Function(x) x.Name, StringComparer.CurrentCultureIgnoreCase)
                Dim isAttached As Boolean = bound.ContainsKey(r.ResourceId)
                rows.Add(New ExchangeDeviceRow(r.ResourceId, r.Name, r.Kind, r.IsSupportedKind, device.IsOnline, isAttached,
                                               ExchangeAttach.StateOf(r.ResourceId, snapshot, fullList:=True)))
            Next
            groups.Add(New ExchangeDeviceGroup(device.DeviceId, If(device.DeviceName.Length > 0, device.DeviceName, device.Product), device.IsOnline, rows))
        Next
        Return groups
    End Function

    ''' <summary>The attached resources whose producer is not among the listed devices' resources any more, with their state: what the
    ''' list marks on the rows of the main resource list. Only resources of the exchange are ever looked at.</summary>
    Friend Shared Function StatesOf(resources As IEnumerable(Of ResourceDescriptor), snapshot As ExchangeDirectorySnapshot, fullList As Boolean) As Dictionary(Of String, ExchangeResourceState)
        Dim states As New Dictionary(Of String, ExchangeResourceState)(StringComparer.OrdinalIgnoreCase)
        For Each r As ResourceDescriptor In If(resources, Enumerable.Empty(Of ResourceDescriptor)())
            If Not ResourceOrigin.IsExchange(r) Then Continue For
            states(r.Id) = ExchangeAttach.StateOf(r.ExchangeResourceId, snapshot, fullList)
        Next
        Return states
    End Function

End Class
#End If
