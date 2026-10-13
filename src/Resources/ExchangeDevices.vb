#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 171 rules 2, 3, 4, 5 and 8 (phase C2): the viewer's use of the device agent's local channel.
'
'   - The directory comes from the agent (`GetDirectory`, 180), long-polled while the "My devices" view is open and not otherwise; the viewer
'     never opens a control stream and never sends `list`.
'   - The agent is the Share Manager's: a viewer without the Share Manager shows nothing at all (Q171-1 - a half-working row is worse than none),
'     and a Share Manager that is installed but not running is woken by the USER (a button that says so), never silently.
'   - An attached resource is bound to its producer's resource id; each time the directory shows it with a different `access` the kept one is
'     REPLACED and the row says so once; a resource of any other origin is never read, never compared, never touched.
'   - Absence is not authority: a directory that could not be fetched changes NO state, and nothing in the sync deletes, renames or edits a
'     resource because of a state - removal is the user's act.
'
' Dormant: nothing here runs until the "My devices" view (or an open of an attached resource) asks; with the features not offered nothing is built.

''' <summary>What the viewer can say about the exchange right now.</summary>
Friend Enum ExchangeDevicesAvailability
    ''' <summary>The client features are not offered, or the Share Manager (the agent's home) is not installed: no row is drawn.</summary>
    Hidden = 0
    ''' <summary>The Share Manager is installed and nobody answers on the agent's channel.</summary>
    NotRunning = 1
    ''' <summary>The agent runs and no account is configured.</summary>
    NotSignedIn = 2
    ''' <summary>An account is configured and the agent has no directory yet (connecting, reconnecting, stopped, signed out).</summary>
    Offline = 3
    ''' <summary>The agent answered with a directory.</summary>
    Ready = 4
End Enum

Friend NotInheritable Class ExchangeDevicesState

    Friend ReadOnly Property Availability As ExchangeDevicesAvailability
    ''' <summary>The agent's state as it reported it (Nothing when it did not answer).</summary>
    Friend ReadOnly Property Agent As ExchangeAgentStateInfo
    ''' <summary>The directory of a SUCCESSFUL full fetch, or Nothing. Only then may a state like "no longer published" be drawn.</summary>
    Friend ReadOnly Property Snapshot As ExchangeDirectorySnapshot

    Friend Shared ReadOnly Hidden As New ExchangeDevicesState(ExchangeDevicesAvailability.Hidden, Nothing, Nothing)

    Friend Sub New(availability As ExchangeDevicesAvailability, agent As ExchangeAgentStateInfo, snapshot As ExchangeDirectorySnapshot)
        Me.Availability = availability
        Me.Agent = agent
        Me.Snapshot = snapshot
    End Sub

    Friend ReadOnly Property FullList As Boolean
        Get
            Return Snapshot IsNot Nothing
        End Get
    End Property

    Friend ReadOnly Property OwnDeviceId As String
        Get
            Return If(Agent Is Nothing, "", Agent.DeviceId)
        End Get
    End Property

End Class

''' <summary>The three requests of the local channel the viewer makes, behind an interface so the view and the sync are tested without a pipe.</summary>
Friend Interface IExchangeAgentApi
    Function GetStateAsync(ct As CancellationToken) As Task(Of ExchangeAgentStateResult)
    Function GetDirectoryAsync(sinceRevision As Long, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDirectoryResult)
    Function GetAccessAsync(resourceId As String, ct As CancellationToken) As Task(Of ExchangeAgentAccessResult)
End Interface

Friend NotInheritable Class ExchangeAgentApi
    Implements IExchangeAgentApi

    Private ReadOnly _client As ExchangeAgentClient

    Friend Sub New(Optional client As ExchangeAgentClient = Nothing)
        _client = If(client, New ExchangeAgentClient())
    End Sub

    Public Function GetStateAsync(ct As CancellationToken) As Task(Of ExchangeAgentStateResult) Implements IExchangeAgentApi.GetStateAsync
        Return _client.GetStateAsync(ct)
    End Function

    Public Function GetDirectoryAsync(sinceRevision As Long, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDirectoryResult) Implements IExchangeAgentApi.GetDirectoryAsync
        Return _client.GetDirectoryAsync(sinceRevision, waitMs, ct)
    End Function

    Public Function GetAccessAsync(resourceId As String, ct As CancellationToken) As Task(Of ExchangeAgentAccessResult) Implements IExchangeAgentApi.GetAccessAsync
        Return _client.GetAccessAsync(resourceId, ct)
    End Function
End Class

''' <summary>Where the Share Manager (the agent's home) is, and the one user-triggered way to start it (Q171-1, 180 section 6).</summary>
Friend NotInheritable Class ExchangeAgentLocator

    Private Sub New()
    End Sub

    Friend Const CompanionExeFileName As String = "FastMediaSorterCompanion.exe"
    Private Const ServerEditionKey As String = "Software\Microsoft\Windows\CurrentVersion\Uninstall\{A9F3C61B-4D2E-4F58-9C7A-1E6B0D3F82A4}_is1"

    ''' <summary>The Share Manager's exe: beside the viewer first (the User edition), then the Server edition's recorded install location.
    ''' "" when this machine has none - and then the exchange rows do not exist.</summary>
    Friend Shared Function CompanionExePath() As String
        Try
            Dim beside As String = Path.Combine(AppContext.BaseDirectory, CompanionExeFileName)
            If File.Exists(beside) Then Return beside
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
        End Try
        For Each hive As Microsoft.Win32.RegistryKey In {Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine}
            For Each subKey As String In {ServerEditionKey, ServerEditionKey.Replace("Software\", "Software\WOW6432Node\")}
                Try
                    Using k As Microsoft.Win32.RegistryKey = hive.OpenSubKey(subKey, False)
                        If k Is Nothing Then Continue For
                        Dim loc As String = TryCast(k.GetValue("InstallLocation"), String)
                        If String.IsNullOrWhiteSpace(loc) Then Continue For
                        Dim candidate As String = Path.Combine(loc.Trim().TrimEnd(Path.DirectorySeparatorChar), CompanionExeFileName)
                        If File.Exists(candidate) Then Return candidate
                    End Using
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.SecurityException
                End Try
            Next
        Next
        Return ""
    End Function

    ''' <summary>Starts the Share Manager silent in the tray (`--tray`), the same launch a sign-in autostart makes. The user asked for it
    ''' (a button); it is never done on its own. False when it could not be started.</summary>
    Friend Shared Function TryStart() As Boolean
        Dim exe As String = CompanionExePath()
        If exe.Length = 0 Then Return False
        Try
            Dim started As Process = Process.Start(New ProcessStartInfo(exe, "--tray") With {.UseShellExecute = True})
            If started IsNot Nothing Then started.Dispose()
            Return True
        Catch ex As Exception When TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException
            AppFileLogger.WriteLine("Exchange: the Share Manager could not be started - " & ex.GetType().Name)
            Return False
        End Try
    End Function

End Class

''' <summary>The "My devices" feed: asks the agent what it is and what the account holds, as one immutable <see cref="ExchangeDevicesState"/>.
''' Runs only while someone asks (<see cref="RefreshAsync"/> once, or <see cref="RunAsync"/> while a view is open).</summary>
Friend NotInheritable Class ExchangeDevicesFeed

    Private ReadOnly _api As IExchangeAgentApi
    Private ReadOnly _enabled As Func(Of Boolean)
    Private ReadOnly _installed As Func(Of Boolean)
    Private ReadOnly _gate As New Object()
    Private _state As ExchangeDevicesState = ExchangeDevicesState.Hidden
    Private _revision As Long

    ''' <summary>The state moved (any thread).</summary>
    Friend Event Changed As EventHandler

    ''' <summary>How long one directory request may wait for a newer revision (the channel's long poll; the agent caps it at 30 s).</summary>
    Friend Property PollWaitMs As Integer = 25000
    ''' <summary>How long the loop waits before it asks again when the agent could not answer.</summary>
    Friend Property RetryMs As Integer = 3000
    Private Const MinLoopMs As Integer = 250

    Friend Sub New(api As IExchangeAgentApi, enabled As Func(Of Boolean), installed As Func(Of Boolean))
        _api = api
        _enabled = enabled
        _installed = installed
    End Sub

    ''' <summary>The production feed: the real channel, the real gate, the real Share Manager lookup.</summary>
    Friend Shared Function CreateDefault() As ExchangeDevicesFeed
        Return New ExchangeDevicesFeed(New ExchangeAgentApi(), AddressOf ExchangeFeatures.IsEnabled, Function() ExchangeAgentLocator.CompanionExePath().Length > 0)
    End Function

    Friend ReadOnly Property Current As ExchangeDevicesState
        Get
            SyncLock _gate
                Return _state
            End SyncLock
        End Get
    End Property

    ''' <summary>False without the gate or without the Share Manager: nothing is drawn and the channel is never touched.</summary>
    Friend Function IsOffered() As Boolean
        Return _enabled.Invoke() AndAlso _installed.Invoke()
    End Function

    Private Sub SetState(state As ExchangeDevicesState)
        Dim changed As Boolean
        SyncLock _gate
            changed = Not Object.ReferenceEquals(_state, state) AndAlso (_state.Availability <> state.Availability OrElse
                      Not Object.ReferenceEquals(_state.Snapshot, state.Snapshot) OrElse Not SameAgent(_state.Agent, state.Agent))
            _state = state
        End SyncLock
        If changed Then RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    Private Shared Function SameAgent(a As ExchangeAgentStateInfo, b As ExchangeAgentStateInfo) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return a Is b
        Return a.State = b.State AndAlso a.Configured = b.Configured AndAlso String.Equals(a.LastReason, b.LastReason, StringComparison.Ordinal)
    End Function

    ''' <summary>One look: the agent's state and, when it is online, its directory (no waiting). A directory request that fails leaves NO
    ''' snapshot, so no state is drawn from it (absence is not authority).</summary>
    Friend Async Function RefreshAsync(ct As CancellationToken) As Task(Of ExchangeDevicesState)
        Return Await LookAsync(0, ct).ConfigureAwait(False)
    End Function

    Private Async Function LookAsync(waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeDevicesState)
        If Not IsOffered() Then
            SetState(ExchangeDevicesState.Hidden)
            Return ExchangeDevicesState.Hidden
        End If
        Dim stateResult As ExchangeAgentStateResult = Await _api.GetStateAsync(ct).ConfigureAwait(False)
        Dim state As ExchangeDevicesState
        If Not stateResult.Reply.IsOk OrElse stateResult.Info Is Nothing Then
            state = New ExchangeDevicesState(ExchangeDevicesAvailability.NotRunning, Nothing, Nothing)
        ElseIf Not stateResult.Info.Configured Then
            state = New ExchangeDevicesState(ExchangeDevicesAvailability.NotSignedIn, stateResult.Info, Nothing)
        ElseIf stateResult.Info.State <> ExchangeAgentState.Online Then
            state = New ExchangeDevicesState(ExchangeDevicesAvailability.Offline, stateResult.Info, Nothing)
        Else
            Dim dir As ExchangeAgentDirectoryResult = Await _api.GetDirectoryAsync(_revision, waitMs, ct).ConfigureAwait(False)
            If dir.Reply.IsOk AndAlso dir.Snapshot IsNot Nothing Then
                _revision = dir.Snapshot.Revision
                state = New ExchangeDevicesState(ExchangeDevicesAvailability.Ready, stateResult.Info, dir.Snapshot)
            Else
                state = New ExchangeDevicesState(ExchangeDevicesAvailability.Offline, stateResult.Info, Nothing)
            End If
        End If
        SetState(state)
        Return state
    End Function

    ''' <summary>Looks and long-polls until cancelled: what runs while the view is open. Never throws.</summary>
    Friend Async Function RunAsync(ct As CancellationToken) As Task
        Try
            While Not ct.IsCancellationRequested
                Dim started As Long = Environment.TickCount64
                Dim state As ExchangeDevicesState = Await LookAsync(PollWaitMs, ct).ConfigureAwait(False)
                ' a long poll that returned at once (nothing newer is fine, an error is not) must not spin: a short pause when it did not wait
                If state.Availability <> ExchangeDevicesAvailability.Ready Then
                    Await Task.Delay(RetryMs, ct).ConfigureAwait(False)
                ElseIf Environment.TickCount64 - started < MinLoopMs Then
                    Await Task.Delay(MinLoopMs, ct).ConfigureAwait(False)      ' a poll that answered at once never becomes a hot loop
                End If
            End While
        Catch ex As OperationCanceledException
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
            AppFileLogger.WriteLine("Exchange: the devices view stopped - " & ex.GetType().Name)
        End Try
    End Function

End Class

Friend Enum ExchangeAttachOutcome
    Attached = 0
    ''' <summary>Already attached (the same producer's resource id is bound to a resource here).</summary>
    AlreadyAttached = 1
    ''' <summary>The producer is offline: its record carries no `access`, so there is nothing to attach yet.</summary>
    NoAccess = 2
    ''' <summary>The record's access is not one this viewer can open (a key login, a root it cannot use, a code it refuses).</summary>
    Unusable = 3
    ''' <summary>The agent could not be reached for the access.</summary>
    AgentUnavailable = 4
    ''' <summary>The credential could not be protected or the registry could not be saved; nothing was stored.</summary>
    NotStored = 5
    ''' <summary>The SFTP verification failed; the caller receives the actual connection failure.</summary>
    ConnectionFailed = 6
    ''' <summary>The online protected resource has no access for this device's older enrollment.</summary>
    SignInAgain = 7
End Enum

''' <summary>Attaching, and following the producer. The registry and the credential go through ResourceCommit (the secret first, the reference
''' last); the agent is asked for `access` one resource at a time, the only place a secret crosses to this program.</summary>
Friend NotInheritable Class ExchangeSync

    Private ReadOnly _api As IExchangeAgentApi
    Private ReadOnly _load As Func(Of List(Of ResourceDescriptor))
    Private ReadOnly _commit As Func(Of ResourceDescriptor, String, ResourceCommitResult)
    Private ReadOnly _readPassword As Func(Of ResourceDescriptor, String)
    Private ReadOnly _newId As Func(Of String)
    Private ReadOnly _entryId As String
    Private ReadOnly _dialer As IEndpointDialer
    Private ReadOnly _tunnelSource As Func(Of ResourceDescriptor, IExchangeTunnelSource)

    ''' <param name="entryId">Ticket 187: the entry this sync serves. An attached resource is bound to the entry AND the producer's resource
    ''' id; only resources carrying this entry's id are looked up, followed or created. Nothing = the Share Manager's older account.</param>
    Friend Sub New(api As IExchangeAgentApi, load As Func(Of List(Of ResourceDescriptor)), commit As Func(Of ResourceDescriptor, String, ResourceCommitResult),
                   readPassword As Func(Of ResourceDescriptor, String), newId As Func(Of String), Optional entryId As String = Nothing,
                   Optional dialer As IEndpointDialer = Nothing, Optional tunnelSource As Func(Of ResourceDescriptor, IExchangeTunnelSource) = Nothing)
        _api = api
        _load = load
        _commit = commit
        _readPassword = readPassword
        _newId = newId
        _entryId = If(String.IsNullOrEmpty(entryId), Nothing, entryId)
        _dialer = dialer
        _tunnelSource = tunnelSource
        If _tunnelSource Is Nothing Then _tunnelSource = Function(d As ResourceDescriptor) ExchangeTunnelSources.For(d, Nothing, Nothing, Nothing)
    End Sub

    ''' <summary>The sync of one of the viewer's own entries, over its own session (no Share Manager).</summary>
    Friend Shared Function CreateFor(entry As ExchangeEntry, api As IExchangeAgentApi) As ExchangeSync
        Dim secrets As IResourceSecretStore = New RegistryResourceSecretStore()
        Return New ExchangeSync(api, AddressOf ResourceRegistry.Load,
            Function(d, pw) ResourceCommit.Commit(d, pw, AddressOf ResourceRegistry.HiveRead, AddressOf ResourceRegistry.HiveWrite, secrets),
            Function(d)
                Dim read As CredentialReadResult = ResourceCommit.ReadCredential(d, secrets)
                Return If(read.Outcome = CredentialReadOutcome.Ok, read.Secret, "")
            End Function,
            AddressOf ResourceImport.NewResourceId, entry.Id)
    End Function

    ''' <summary>The production sync over the real registry and the real credential store.</summary>
    Friend Shared Function CreateDefault() As ExchangeSync
        Dim secrets As IResourceSecretStore = New RegistryResourceSecretStore()
        Return New ExchangeSync(New ExchangeAgentApi(), AddressOf ResourceRegistry.Load,
            Function(d, pw) ResourceCommit.Commit(d, pw, AddressOf ResourceRegistry.HiveRead, AddressOf ResourceRegistry.HiveWrite, secrets),
            Function(d)
                Dim read As CredentialReadResult = ResourceCommit.ReadCredential(d, secrets)
                Return If(read.Outcome = CredentialReadOutcome.Ok, read.Secret, "")
            End Function,
            AddressOf ResourceImport.NewResourceId)
    End Function

    ''' <summary>The user ticked a resource: ask the agent for its access, build the resource, store it (the credential through the DPAPI blob).</summary>
    Friend Async Function AttachAsync(record As ExchangeResourceRecord, deviceName As String, ct As CancellationToken, Optional resourcePin As String = Nothing,
                                     Optional reportFailure As Action(Of ResourceFailure) = Nothing) As Task(Of ExchangeAttachOutcome)
        If record Is Nothing OrElse Not record.IsSupportedKind OrElse Not record.CanAuthorize Then Return ExchangeAttachOutcome.Unusable
        If ExchangeAttach.NeedsPinEnrollment(record) Then Return ExchangeAttachOutcome.SignInAgain
        If record.IsProtected AndAlso String.IsNullOrEmpty(resourcePin) Then
            reportFailure?.Invoke(New ResourceFailure(ResourceFailureKind.Auth))
            Return ExchangeAttachOutcome.ConnectionFailed
        End If
        If ResourceOrigin.FindByExchangeId(_load.Invoke(), record.ResourceId, _entryId) IsNot Nothing Then Return ExchangeAttachOutcome.AlreadyAttached
        Dim access As ExchangeAgentAccessResult = Await _api.GetAccessAsync(record.ResourceId, ct).ConfigureAwait(False)
        If Not access.Reply.IsOk Then
            Select Case access.Reply.Error
                Case ExchangeAgentPipe.ErrorNoAccess
                    Return If(record.IsProtected AndAlso String.Equals(record.Presence, ExchangeWire.PresenceOnline, StringComparison.Ordinal),
                              ExchangeAttachOutcome.SignInAgain, ExchangeAttachOutcome.NoAccess)
                Case ExchangeAgentPipe.ErrorUnknownResource : Return ExchangeAttachOutcome.NoAccess
                Case Else : Return If(access.Reply.Kind = ExchangeAgentReplyKind.Refused, ExchangeAttachOutcome.Unusable, ExchangeAttachOutcome.AgentUnavailable)
            End Select
        End If
        Dim draft As ExchangeAttachDraft = ExchangeAttach.BuildDraft(record.ResourceId, record.DeviceId, deviceName, record.Name, access.AccessJson, _newId, _entryId)
        If draft Is Nothing Then Return ExchangeAttachOutcome.Unusable
        draft.Descriptor.PinRequired = record.IsProtected
        If record.IsProtected Then
            Dim verified As ConnectOutcome = Await ResourceConnect.ConnectAsync(draft.Descriptor, draft.Password, Nothing, Nothing, Function() False, ct,
                dialerOverride:=_dialer, tunnelSource:=_tunnelSource.Invoke(draft.Descriptor), resourcePin:=resourcePin).ConfigureAwait(False)
            If Not verified.Succeeded Then
                ResourcePinMemory.Forget(draft.Descriptor)
                reportFailure?.Invoke(verified.Failure)
                Return ExchangeAttachOutcome.ConnectionFailed
            End If
            DirectCast(verified.Provider, IDisposable).Dispose()
        End If
        Dim result As ResourceCommitResult = _commit.Invoke(draft.Descriptor, draft.Password)
        If Not result.Succeeded Then ResourcePinMemory.Forget(draft.Descriptor)
        Return If(result.Succeeded, ExchangeAttachOutcome.Attached, ExchangeAttachOutcome.NotStored)
    End Function

    ''' <summary>Ticket 187 rule 7: attaches the listed resources that can be attached now - supported kind, the producer online with an `access`.
    ''' The user's act ("Attach everything listed") or, per entry and off by default, the setting that attaches NEW ones (the caller passes only
    ''' the ids that were not seen before). Returns how many were attached; one that is already attached, offline or unusable is skipped.</summary>
    Friend Async Function AttachListedAsync(state As ExchangeDevicesState, only As ISet(Of String), ct As CancellationToken) As Task(Of Integer)
        Dim attached As Integer = 0
        If state Is Nothing OrElse state.Snapshot Is Nothing Then Return 0
        For Each record As ExchangeResourceRecord In state.Snapshot.Resources
            If only IsNot Nothing AndAlso Not only.Contains(record.ResourceId) Then Continue For
            If Not record.IsSupportedKind OrElse Not record.HasAccess Then Continue For
            Dim outcome As ExchangeAttachOutcome = Await AttachAsync(record, DeviceNameOf(state.Snapshot, record.ResourceId), ct).ConfigureAwait(False)
            If outcome = ExchangeAttachOutcome.Attached Then attached += 1
        Next
        Return attached
    End Function

    ''' <summary>Follows the producer for one attached resource: asks for its current `access`, and replaces the kept one when it differs
    ''' (new password, new address set, new pin), setting the notice. Returns the resource as stored now (the same object when nothing
    ''' changed - or when the agent could not be asked, in which case the kept access simply stays: a resource keeps opening by its direct
    ''' addresses without the agent).</summary>
    Friend Async Function FollowOneAsync(resource As ResourceDescriptor, deviceName As String, ct As CancellationToken) As Task(Of ExchangeFollowOutcome)
        If Not ResourceOrigin.IsExchange(resource) Then Return New ExchangeFollowOutcome(resource, False)
        Dim listed As ExchangeResourceRecord = Nothing
        Try
            Dim directory As ExchangeAgentDirectoryResult = Await _api.GetDirectoryAsync(-1, 0, ct).ConfigureAwait(False)
            If directory.Reply.IsOk AndAlso directory.Snapshot IsNot Nothing Then
                listed = directory.Snapshot.Resources.FirstOrDefault(Function(r) r.ResourceId = resource.ExchangeResourceId)
            End If
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            ' A missing directory cannot authorize clearing a stored lock.
        End Try
        If listed IsNot Nothing AndAlso Not listed.CanAuthorize Then
            Dim denied As ResourceDescriptor = System.Text.Json.JsonSerializer.Deserialize(Of ResourceDescriptor)(System.Text.Json.JsonSerializer.Serialize(resource))
            denied.PinRequired = True
            denied.ProtectionUnsupported = True
            ResourcePinMemory.Forget(resource)
            _commit.Invoke(denied, Nothing)
            Return New ExchangeFollowOutcome(denied, True)
        End If
        Dim access As ExchangeAgentAccessResult
        Try
            access = Await _api.GetAccessAsync(resource.ExchangeResourceId, ct).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            Return New ExchangeFollowOutcome(resource, False)
        End Try
        If Not access.Reply.IsOk Then Return New ExchangeFollowOutcome(resource, False)     ' no access now (offline) or no agent: nothing to follow
        ' a producer whose access names no root keeps the root the resource already has: the name that would rebuild "/" + name is that root's
        Dim draft As ExchangeAttachDraft = ExchangeAttach.BuildDraft(resource.ExchangeResourceId, resource.ExchangeDeviceId, deviceName,
                                                                     If(resource.RootPath, "").Trim("/"c), access.AccessJson, _newId, _entryId)
        If draft Is Nothing Then Return New ExchangeFollowOutcome(resource, False)
        draft.Descriptor.PinRequired = resource.PinRequired
        draft.Descriptor.ProtectionUnsupported = resource.ProtectionUnsupported
        If listed IsNot Nothing Then
            draft.Descriptor.PinRequired = listed.IsProtected
            draft.Descriptor.ProtectionUnsupported = Not listed.CanAuthorize
        End If
        Dim followed As ExchangeFollowResult = ExchangeAttach.Follow(resource, _readPassword.Invoke(resource), draft, deviceName)
        If Object.ReferenceEquals(followed.Descriptor, resource) Then Return New ExchangeFollowOutcome(resource, False)
        Dim stored As ResourceCommitResult = _commit.Invoke(followed.Descriptor, followed.NewPassword)
        If Not stored.Succeeded Then Return New ExchangeFollowOutcome(resource, False)
        Return New ExchangeFollowOutcome(followed.Descriptor, followed.Changed)
    End Function

    ''' <summary>Follows every attached resource the directory shows as available. A resource of any other origin is never read. Returns the
    ''' ids of the resources that were replaced.</summary>
    Friend Async Function FollowAllAsync(state As ExchangeDevicesState, ct As CancellationToken) As Task(Of List(Of String))
        Dim changed As New List(Of String)
        If state Is Nothing OrElse state.Snapshot Is Nothing Then Return changed      ' no full list: nothing is known, so nothing changes
        For Each resource As ResourceDescriptor In _load.Invoke()
            If Not ResourceOrigin.IsExchange(resource) Then Continue For
            ' only what was attached through THIS entry: another entry's resource of the same id is another account's
            If Not String.Equals(If(resource.ExchangeEntryId, ""), If(_entryId, ""), StringComparison.Ordinal) Then Continue For
            Dim currentRecord As ExchangeResourceRecord = state.Snapshot.Resources.FirstOrDefault(Function(r) r.ResourceId = resource.ExchangeResourceId)
            If currentRecord IsNot Nothing AndAlso Not currentRecord.CanAuthorize Then
                Await FollowOneAsync(resource, DeviceNameOf(state.Snapshot, resource.ExchangeResourceId), ct).ConfigureAwait(False)
                changed.Add(resource.Id)
                Continue For
            End If
            Dim resourceState As ExchangeResourceState = ExchangeAttach.StateOf(resource.ExchangeResourceId, state.Snapshot, True)
            If resourceState = ExchangeResourceState.NoLongerPublished AndAlso resource.PinRequired Then
                ResourcePinMemory.Forget(resource)
                changed.Add(resource.Id)
            End If
            If resourceState <> ExchangeResourceState.Available Then Continue For
            Dim deviceName As String = DeviceNameOf(state.Snapshot, resource.ExchangeResourceId)
            Dim outcome As ExchangeFollowOutcome = Await FollowOneAsync(resource, deviceName, ct).ConfigureAwait(False)
            If Not Object.ReferenceEquals(outcome.Descriptor, resource) Then changed.Add(resource.Id)
        Next
        Return changed
    End Function

    Private Shared Function DeviceNameOf(snapshot As ExchangeDirectorySnapshot, resourceId As String) As String
        Dim record As ExchangeResourceRecord = snapshot.Resources.FirstOrDefault(Function(r) String.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
        If record Is Nothing Then Return ""
        Dim device As ExchangeDeviceRecord = snapshot.Devices.FirstOrDefault(Function(d) String.Equals(d.DeviceId, record.DeviceId, StringComparison.Ordinal))
        Return If(device Is Nothing, "", If(device.DeviceName.Length > 0, device.DeviceName, device.Product))
    End Function

End Class

Friend NotInheritable Class ExchangeFollowOutcome
    Friend ReadOnly Property Descriptor As ResourceDescriptor
    ''' <summary>The producer's access changed (address, key, user, root or password): the row says so once.</summary>
    Friend ReadOnly Property Changed As Boolean

    Friend Sub New(descriptor As ResourceDescriptor, changed As Boolean)
        Me.Descriptor = descriptor
        Me.Changed = changed
    End Sub
End Class
#End If
