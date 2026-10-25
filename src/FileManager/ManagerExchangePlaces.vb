#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

Friend Enum ManagerExchangeNodeKind
    Account
    Device
    Root
End Enum

Friend Enum ManagerExchangeAvailability
    Available
    Offline
    Removed
    SignInRequired
    Locked
    Unsupported
    Revoked
End Enum

''' <summary>Display facts only. No access descriptors, tokens or session handles reach a control.</summary>
Friend NotInheritable Class ManagerExchangeNode
    Friend Sub New(id As String, parentId As String, title As String, kind As ManagerExchangeNodeKind,
                   state As ManagerExchangeAvailability, entryId As String, deviceId As String,
                   resourceId As String, place As ManagerPlace)
        Me.Id = id
        Me.ParentId = parentId
        Me.Title = title
        Me.Kind = kind
        Me.State = state
        Me.EntryId = entryId
        Me.DeviceId = deviceId
        Me.ResourceId = resourceId
        Me.Place = place
    End Sub
    Friend ReadOnly Property Id As String
    Friend ReadOnly Property ParentId As String
    Friend ReadOnly Property Title As String
    Friend ReadOnly Property Kind As ManagerExchangeNodeKind
    Friend ReadOnly Property State As ManagerExchangeAvailability
    Friend ReadOnly Property EntryId As String
    Friend ReadOnly Property DeviceId As String
    Friend ReadOnly Property ResourceId As String
    Friend ReadOnly Property Place As ManagerPlace

    Friend ReadOnly Property StateText As String
        Get
            Select Case State
                Case ManagerExchangeAvailability.Offline : Return Localization.T("недоступно: устройство не в сети")
                Case ManagerExchangeAvailability.Removed : Return Localization.T("больше не опубликовано")
                Case ManagerExchangeAvailability.SignInRequired : Return ExchangeEntryText.ResourceNeedsSignIn()
                Case ManagerExchangeAvailability.Locked : Return Localization.T("защищено PIN-кодом")
                Case ManagerExchangeAvailability.Unsupported : Return Localization.T("заблокировано: защита не поддерживается")
                Case ManagerExchangeAvailability.Revoked : Return ExchangeEntryText.TableText(ConnectionDiagnosisMessages.DeviceRevoked)
                Case Else : Return ""
            End Select
        End Get
    End Property

    Public Overrides Function ToString() As String
        Dim indent As String = If(Kind = ManagerExchangeNodeKind.Account, "", If(Kind = ManagerExchangeNodeKind.Device, "  ", "    "))
        Return indent & Title & If(StateText.Length = 0, "", " - " & StateText)
    End Function
End Class

Friend NotInheritable Class ManagerExchangeSnapshot
    Friend Sub New(generation As Integer, nodes As IEnumerable(Of ManagerExchangeNode))
        Me.Generation = generation
        Me.Nodes = Array.AsReadOnly(nodes.ToArray())
    End Sub
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property Nodes As IReadOnlyList(Of ManagerExchangeNode)
End Class

''' <summary>A per-entry feed and counted session lease, owned by the adapter rather than controls.</summary>
Friend NotInheritable Class ManagerExchangeConnection
    Implements IDisposable
    Friend Sub New(api As IExchangeAgentApi, lease As IDisposable)
        Me.Api = api
        Me.lease = lease
        Feed = New ExchangeDevicesFeed(api, Function() True, Function() True)
    End Sub
    Friend ReadOnly Property Api As IExchangeAgentApi
    Friend ReadOnly Property Feed As ExchangeDevicesFeed
    Private lease As IDisposable
    Public Sub Dispose() Implements IDisposable.Dispose
        Interlocked.Exchange(lease, Nothing)?.Dispose()
    End Sub
End Class

''' <summary>Consumer-only directory adapter. Discovery never asks for access or attaches anything.</summary>
Friend NotInheritable Class ManagerExchangePlaces
    Implements IDisposable
    Private ReadOnly entries As Func(Of IEnumerable(Of ExchangeEntry))
    Private ReadOnly resources As Func(Of IEnumerable(Of ResourceDescriptor))
    Private ReadOnly connect As Func(Of ExchangeEntry, ManagerExchangeConnection)
    Private ReadOnly sources As New Dictionary(Of String, ManagerExchangeConnection)(StringComparer.Ordinal)
    Private ReadOnly bindings As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private ReadOnly turn As New SemaphoreSlim(1, 1)
    Private ReadOnly lifetime As New CancellationTokenSource()
    Private currentValue As New ManagerExchangeSnapshot(0, Array.Empty(Of ManagerExchangeNode)())
    Private generation As Integer

    Friend Sub New(entries As Func(Of IEnumerable(Of ExchangeEntry)), resources As Func(Of IEnumerable(Of ResourceDescriptor)),
                   connect As Func(Of ExchangeEntry, ManagerExchangeConnection))
        Me.entries = entries
        Me.resources = resources
        Me.connect = connect
    End Sub

    Friend Shared Function OpenEntry(entry As ExchangeEntry) As ManagerExchangeConnection
        Dim session As ExchangeEntrySession = ExchangeViewerHub.Instance().SessionFor(entry)
        Return New ManagerExchangeConnection(New ExchangeEntryApi(session), session.Lease())
    End Function

    Friend Shared Function CreateDefault() As ManagerExchangePlaces
        Return New ManagerExchangePlaces(Function() ExchangeEntryStore.CreateDefault().Load(), AddressOf ResourceRegistry.Load, AddressOf OpenEntry)
    End Function

    Friend ReadOnly Property Current As ManagerExchangeSnapshot
        Get
            Return Volatile.Read(currentValue)
        End Get
    End Property

    Friend Async Function RefreshAsync(ct As CancellationToken) As Task(Of ManagerExchangeSnapshot)
        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token)
            Await turn.WaitAsync(linked.Token).ConfigureAwait(False)
            Try
                Dim configured As List(Of ExchangeEntry) = entries().OrderBy(Function(e) e.Id, StringComparer.Ordinal).ToList()
                For Each id In sources.Keys.Where(Function(k) Not configured.Any(Function(e) e.Id = k)).ToArray()
                    sources(id).Dispose()
                    sources.Remove(id)
                    bindings.Remove(id)
                Next
                Dim saved As List(Of ResourceDescriptor) = resources().ToList()
                Dim nodes As New List(Of ManagerExchangeNode)()
                For Each entry In configured
                    linked.Token.ThrowIfCancellationRequested()
                    Dim binding As String = Identity(entry.Server, entry.Login, entry.DeviceId, entry.Pin, entry.KeyPin)
                    If sources.ContainsKey(entry.Id) AndAlso bindings(entry.Id) <> binding Then
                        sources(entry.Id).Dispose()
                        sources.Remove(entry.Id)
                        bindings.Remove(entry.Id)
                    End If
                    If Not sources.ContainsKey(entry.Id) Then
                        sources.Add(entry.Id, connect(entry))
                        bindings.Add(entry.Id, binding)
                    End If
                    Dim state As ExchangeDevicesState
                    Try
                        state = Await sources(entry.Id).Feed.RefreshAsync(linked.Token).ConfigureAwait(False)
                    Catch ex As OperationCanceledException
                        Throw
                    Catch ex As Exception
                        AppFileLogger.WriteLine("Manager exchange: directory unavailable - " & ex.GetType().Name)
                        state = New ExchangeDevicesState(ExchangeDevicesAvailability.Offline, Nothing, Nothing)
                    End Try
                    nodes.AddRange(Project(entry, state, saved, Current.Nodes))
                Next
                linked.Token.ThrowIfCancellationRequested()
                Dim snapshot As New ManagerExchangeSnapshot(Interlocked.Increment(generation), nodes)
                Volatile.Write(currentValue, snapshot)
                Return snapshot
            Finally
                turn.Release()
            End Try
        End Using
    End Function

    ''' <summary>Length-prefix components so even delimiters in names/roots cannot alias identities.</summary>
    Friend Shared Function Identity(ParamArray parts As String()) As String
        Return ManagerPlace.BindingIdentity(parts)
    End Function

    Friend Shared Function Project(entry As ExchangeEntry, state As ExchangeDevicesState, saved As IEnumerable(Of ResourceDescriptor),
                                   previous As IEnumerable(Of ManagerExchangeNode)) As IReadOnlyList(Of ManagerExchangeNode)
        Dim result As New List(Of ManagerExchangeNode)()
        Dim accountId As String = Identity(entry.Id)
        Dim availability As ManagerExchangeAvailability = If(state.Availability = ExchangeDevicesAvailability.Ready,
            ManagerExchangeAvailability.Available, If(state.Agent?.State = ExchangeAgentState.SignInAgain OrElse
            state.Availability = ExchangeDevicesAvailability.NotSignedIn, ManagerExchangeAvailability.SignInRequired, ManagerExchangeAvailability.Offline))
        If state.Agent?.LastReason = "device-revoked" Then availability = ManagerExchangeAvailability.Revoked
        Dim accountTitle As String = Localization.TF("{0}: {1}", entry.DisplayName, entry.Login)
        result.Add(New ManagerExchangeNode(accountId, "", accountTitle, ManagerExchangeNodeKind.Account, availability, entry.Id, "", "", Nothing))
        Dim snapshot As ExchangeDirectorySnapshot = state.Snapshot
        Dim attached As List(Of ResourceDescriptor) = saved.Where(Function(d) ResourceOrigin.IsExchange(d) AndAlso d.ExchangeEntryId = entry.Id).ToList()
        Dim deviceIds As IEnumerable(Of String) = If(snapshot Is Nothing, Enumerable.Empty(Of String)(), snapshot.Devices.Select(Function(d) d.DeviceId))
        deviceIds = deviceIds.Concat(attached.Select(Function(d) d.ExchangeDeviceId))
        If snapshot Is Nothing Then deviceIds = deviceIds.Concat(previous.Where(Function(n) n.EntryId = entry.Id).Select(Function(n) n.DeviceId))
        For Each deviceId In deviceIds.Where(Function(id) Not String.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).OrderBy(Function(id) id, StringComparer.Ordinal)
            Dim device As ExchangeDeviceRecord = snapshot?.Devices.FirstOrDefault(Function(d) d.DeviceId = deviceId)
            Dim prior As ManagerExchangeNode = previous.FirstOrDefault(Function(n) n.EntryId = entry.Id AndAlso n.DeviceId = deviceId AndAlso n.Kind = ManagerExchangeNodeKind.Device)
            Dim deviceTitle As String = If(device Is Nothing, If(prior?.Title, deviceId), If(device.DeviceName.Length > 0, device.DeviceName, device.Product))
            Dim deviceState As ManagerExchangeAvailability = If(availability <> ManagerExchangeAvailability.Available, availability,
                If(device IsNot Nothing AndAlso device.IsOnline, ManagerExchangeAvailability.Available, ManagerExchangeAvailability.Offline))
            Dim deviceKey As String = Identity(entry.Id, deviceId)
            result.Add(New ManagerExchangeNode(deviceKey, accountId, deviceTitle, ManagerExchangeNodeKind.Device, deviceState, entry.Id, deviceId, "", Nothing))
            Dim records As IEnumerable(Of ExchangeResourceRecord) = If(snapshot Is Nothing, Enumerable.Empty(Of ExchangeResourceRecord)(), snapshot.Resources.Where(Function(r) r.DeviceId = deviceId))
            Dim roots As New List(Of ManagerExchangeNode)()
            For Each record As ExchangeResourceRecord In records.OrderBy(Function(r) r.ResourceId, StringComparer.Ordinal)
                Dim matches As List(Of ResourceDescriptor) = attached.Where(Function(d) d.ExchangeDeviceId = deviceId AndAlso d.ExchangeResourceId = record.ResourceId).ToList()
                Dim rootState As ManagerExchangeAvailability = If(deviceState <> ManagerExchangeAvailability.Available, deviceState,
                    If(Not record.IsSupportedKind OrElse Not record.CanAuthorize, ManagerExchangeAvailability.Unsupported,
                    If(record.IsProtected, ManagerExchangeAvailability.Locked,
                    If(record.Presence = ExchangeWire.PresenceOnline, ManagerExchangeAvailability.Available, ManagerExchangeAvailability.Offline))))
                If matches.Count = 0 Then
                    roots.Add(New ManagerExchangeNode(Identity(entry.Id, deviceId, record.ResourceId, ""), deviceKey, record.Name,
                        ManagerExchangeNodeKind.Root, rootState, entry.Id, deviceId, record.ResourceId, Nothing))
                Else
                    For Each descriptor In matches.OrderBy(Function(d) d.RootPath, StringComparer.Ordinal)
                        roots.Add(New ManagerExchangeNode(Identity(entry.Id, deviceId, record.ResourceId, descriptor.RootPath), deviceKey, record.Name,
                            ManagerExchangeNodeKind.Root, rootState, entry.Id, deviceId, record.ResourceId, ManagerPlace.ForResource(descriptor)))
                    Next
                End If
            Next
            For Each descriptor In attached.Where(Function(d) d.ExchangeDeviceId = deviceId AndAlso Not records.Any(Function(r) r.ResourceId = d.ExchangeResourceId))
                roots.Add(New ManagerExchangeNode(Identity(entry.Id, deviceId, descriptor.ExchangeResourceId, descriptor.RootPath), deviceKey, descriptor.DisplayName,
                    ManagerExchangeNodeKind.Root, If(snapshot Is Nothing, deviceState, ManagerExchangeAvailability.Removed), entry.Id, deviceId,
                    descriptor.ExchangeResourceId, ManagerPlace.ForResource(descriptor)))
            Next
            result.AddRange(roots)
        Next
        Return result
    End Function

    Friend Function StateOf(place As ManagerPlace) As ManagerExchangeAvailability
        If place.ExchangeEntryId.Length = 0 Then Return ManagerExchangeAvailability.Available
        Dim node As ManagerExchangeNode = Current.Nodes.FirstOrDefault(Function(n) n.Place IsNot Nothing AndAlso n.Place.ExchangeIdentity = place.ExchangeIdentity)
        Return If(node Is Nothing, ManagerExchangeAvailability.SignInRequired, node.State)
    End Function

    Friend Async Function AttachAsync(node As ManagerExchangeNode, pin As String, ct As CancellationToken) As Task(Of ExchangeAttachOutcome)
        Dim entry As ExchangeEntry = entries().FirstOrDefault(Function(e) e.Id = node.EntryId)
        If entry Is Nothing Then Return ExchangeAttachOutcome.SignInAgain
        Using connection As ManagerExchangeConnection = connect(entry)
            Dim directory As ExchangeAgentDirectoryResult = Await connection.Api.GetDirectoryAsync(-1, 7000, ct).ConfigureAwait(False)
            Dim record As ExchangeResourceRecord = directory.Snapshot?.Resources.FirstOrDefault(Function(r) r.DeviceId = node.DeviceId AndAlso r.ResourceId = node.ResourceId)
            If record Is Nothing Then Return ExchangeAttachOutcome.NoAccess
            Return Await ExchangeSync.CreateFor(entry, connection.Api).AttachAsync(record, node.Title, ct, pin).ConfigureAwait(False)
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        lifetime.Cancel()
        ' A refresh may still own a feed. Release its sessions after it leaves the serialized turn.
        Dim drain As Task = ReleaseAsync()
    End Sub
    Private Async Function ReleaseAsync() As Task
        Await turn.WaitAsync().ConfigureAwait(False)
        Try
            For Each source In sources.Values
                source.Dispose()
            Next
            sources.Clear()
            bindings.Clear()
        Finally
            turn.Release()
        End Try
    End Function
End Class
#End If
