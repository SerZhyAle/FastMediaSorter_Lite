Option Strict On

Imports System.Linq
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 170 (T-2, T-3, T-4): the producer half of the device agent, inside the Share Manager. It publishes what the share worker
' serves as `sftp-share` resources, answers `open` by splicing a tunnel to the worker, and carries the two opt-in extras (the
' third-party port and the outsider links). This class reads the worker status like the .fmscfg and QR producers,
' and dials its SFTP listener on loopback. Protected resource removal also invalidates worker sessions through schema-3 IPC.
'
' It plugs into the agent through <see cref="IExchangeProducerHooks"/> (the republish hook of 180): after every `welcome` it publishes
' everything again (items G and H of DEVICE-EXCHANGE 14.5 - `access` and `shareId` live in memory on the server), and while the stream
' lives it watches what a descriptor is built from (port, credentials, fingerprint, the address set, the share list) and publishes a
' change once it has been stable for 2 seconds.
'
' Rules held here:
'   - dormant: constructing a publisher reads and writes nothing; with the setting off (the default) it publishes nothing and
'     unpublishes what it published;
'   - nothing secret is logged: an id appears as the first 8 hex of its SHA-256 (ExchangeIds.ForLog), and `access` and `shareId`
'     never reach a log line; they are kept in memory only, plus the ids inside the DPAPI file of ExchangeProducerStore;
'   - one tunnel per `open`, at most 16 per resource at once, a 5 s dial to the worker, an idle timeout of 10 minutes, and no answer
'     (an attach that is closed at once) for an unknown share, a worker that is down or a refusal;
'   - SSH stays end to end: this class never reads a byte of what it carries.

''' <summary>What the agent gives its producer. Both are called from the session's own tasks.</summary>
Friend Interface IExchangeProducerHooks

    ''' <summary>After every `welcome`, before `list`: publish everything again. Returns once the first publish pass is done (it must
    ''' not wait for the connection to end); background work is bound to <paramref name="ct"/>, which ends with the connection.</summary>
    Function OnWelcomeAsync(session As ExchangeControlSession, ct As CancellationToken) As Task

    ''' <summary>An envelope the session does not handle itself: `open` is this producer's.</summary>
    Sub OnServerMessage(session As ExchangeControlSession, message As ExchangeEnvelope)

    ''' <summary>"Disconnect: keep nothing": the ids, the third-party port and both choices are forgotten.</summary>
    Sub OnAccountForgotten()

End Interface

''' <summary>Ticket 187 rule 9: what leaving the server asks of the producer, before the device revokes itself: every resource it holds is
''' unpublished (best effort, bounded by the caller's token). A separate interface so a producer without it is not a defect.</summary>
Friend Interface IExchangeProducerLeave
    Function UnpublishAllAsync(session As ExchangeControlSession, ct As CancellationToken) As Task
End Interface

Friend Enum ExchangePublishState
    ''' <summary>The server has the resource (it answered `published`).</summary>
    Published = 0
    ''' <summary>Wanted, not on the server yet (not connected, no answer, will be tried again).</summary>
    Pending = 1
    ''' <summary>The server refused it; <see cref="ExchangePublishRow.Refusal"/> says why.</summary>
    Refused = 2
End Enum

Friend NotInheritable Class ExchangePublishRow

    Public ReadOnly Name As String
    Public ReadOnly State As ExchangePublishState
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>An outsider link exists for this share (rule 8).</summary>
    Public ReadOnly HasLink As Boolean
    Public ReadOnly PinProtected As Boolean

    Public Sub New(name As String, state As ExchangePublishState, refusal As ExchangeRefusal, hasLink As Boolean, Optional pinProtected As Boolean = False)
        Me.Name = name
        Me.State = state
        Me.Refusal = refusal
        Me.HasLink = hasLink
        Me.PinProtected = pinProtected
    End Sub

End Class

''' <summary>What the window shows, in one immutable value.</summary>
Friend NotInheritable Class ExchangePublishSnapshot

    Public ReadOnly Publish As Boolean
    Public ReadOnly ThirdPartyPortAsked As Boolean
    Public ReadOnly Block As ExchangePublishBlock
    Public ReadOnly Connected As Boolean
    Public ReadOnly Rows As IReadOnlyList(Of ExchangePublishRow)
    ''' <summary>"host:port" the third-party port is reachable at, or "" while none is granted.</summary>
    Public ReadOnly ThirdPartyEndpoint As String

    Public Shared ReadOnly Empty As New ExchangePublishSnapshot(False, False, ExchangePublishBlock.Off, False, New List(Of ExchangePublishRow), "")

    Public Sub New(publish As Boolean, thirdPartyAsked As Boolean, block As ExchangePublishBlock, connected As Boolean,
                   rows As IReadOnlyList(Of ExchangePublishRow), thirdPartyEndpoint As String)
        Me.Publish = publish
        Me.ThirdPartyPortAsked = thirdPartyAsked
        Me.Block = block
        Me.Connected = connected
        Me.Rows = rows
        Me.ThirdPartyEndpoint = If(thirdPartyEndpoint, "")
    End Sub

    Public Function RowFor(name As String) As ExchangePublishRow
        For Each r As ExchangePublishRow In Rows
            If String.Equals(r.Name, name, StringComparison.Ordinal) Then Return r
        Next
        Return Nothing
    End Function

End Class

Friend Enum ExchangeLinkOutcome
    ''' <summary>The link was created, rotated or removed and the server has the new record.</summary>
    Done = 0
    ''' <summary>No such share is published right now (publication off, server down, a PIN-locked root, or an unknown name).</summary>
    NotPublished = 1
    ''' <summary>No control stream to the exchange server.</summary>
    NotConnected = 2
    ''' <summary>The server refused or did not answer.</summary>
    Failed = 3
    ''' <summary>The server's public endpoint is not one a code can carry (empty or loopback).</summary>
    NoEndpoint = 4
End Enum

Friend NotInheritable Class ExchangeLinkResult
    Public ReadOnly Outcome As ExchangeLinkOutcome
    Public ReadOnly Refusal As ExchangeRefusal
    Public Sub New(outcome As ExchangeLinkOutcome, refusal As ExchangeRefusal)
        Me.Outcome = outcome
        Me.Refusal = refusal
    End Sub
End Class

''' <summary>Everything the publisher needs from outside, so a test hands in fakes and counts the calls.</summary>
Friend NotInheritable Class ExchangePublisherOptions

    Public Property Settings As ExchangePublishSettings = ExchangePublishSettings.CreateDefault()
    Public Property Store As ExchangeProducerStore = ExchangeProducerStore.CreateDefault()
    Public Property InvalidatePinSessions As Func(Of Task) = Async Function()
                                                               Dim response As WorkerResponse = Await Task.Run(Function() WorkerIpc.Send(New WorkerRequest With {.type = "InvalidatePinSessions"})).ConfigureAwait(False)
                                                               If Not response.Ok Then Throw New InvalidOperationException("Worker refused resource PIN session invalidation")
                                                           End Function
    Public Property GetStatus As Func(Of Task(Of WorkerStatus)) = Function() ShareController.GetStatusAsync()
    ''' <summary>True under the server-wide "LAN only" setting (nothing reachable from outside travels in a descriptor).</summary>
    Public Property LanOnly As Func(Of Boolean) = Function()
                                                      Dim s As New ShareSettings()
                                                      s.Load()
                                                      Return s.LanOnly
                                                  End Function
    Public Property LanFallback As Func(Of String) = Function() NetworkInfo.LocalIPv4()
    Public Property HasPin As Func(Of String, Boolean) = Function(hostPath) Not String.IsNullOrEmpty(ShareRootParamsStore.GetFor(hostPath)?.AccessPin)
    Public Property Log As Action(Of String) = Sub(line) AppFileLogger.WriteLine(line)
    Public Property NowUtc As Func(Of DateTime) = Function() DateTime.UtcNow
    Public Property Delay As Func(Of TimeSpan, CancellationToken, Task) = Function(t, ct) Task.Delay(t, ct)

    ''' <summary>A change is published once it has been stable this long (rule 5).</summary>
    Public Property DebounceMs As Integer = 2000
    ''' <summary>How often the worker's status is read while the stream lives.</summary>
    Public Property PollMs As Integer = 5000
    ''' <summary>How long a status read may take.</summary>
    Public Property StatusTimeoutMs As Integer = 4000
    ''' <summary>How long a `publish` may wait for its answer.</summary>
    Public Property PublishTimeoutMs As Integer = 10000

    ' --- tunnels (rule 6) ---
    Public Property WorkerDialTimeoutMs As Integer = 5000
    Public Property MaxTunnelsPerResource As Integer = 16
    Public Property TunnelIdleTimeout As TimeSpan = ExchangeBridgeOptions.DefaultIdleTimeout
    ''' <summary>Opens the worker's SFTP listener on the loopback address. A test hands in its own listener.</summary>
    Public Property DialWorker As Func(Of Integer, CancellationToken, Task(Of System.IO.Stream)) = AddressOf ExchangePublisher.DialWorkerLoopbackAsync

End Class

Friend NotInheritable Class ExchangePublisher
    Implements IExchangeProducerHooks, IExchangeProducerLeave
    Implements IDisposable

    Private NotInheritable Class Published
        Public ReadOnly ResourceId As String
        Public ReadOnly ShareId As String
        Public ReadOnly Signature As String
        Public ReadOnly AccessJson As String
        Public ReadOnly State As ExchangePublishState
        Public ReadOnly PinProtected As Boolean
        Public ReadOnly Refusal As ExchangeRefusal
        Public Sub New(resourceId As String, shareId As String, signature As String, accessJson As String, state As ExchangePublishState, refusal As ExchangeRefusal, pinProtected As Boolean)
            Me.ResourceId = resourceId
            Me.ShareId = shareId
            Me.Signature = signature
            Me.AccessJson = accessJson
            Me.State = state
            Me.Refusal = refusal
            Me.PinProtected = pinProtected
        End Sub
    End Class

    Private ReadOnly _options As ExchangePublisherOptions
    Private ReadOnly _gate As New Object()
    Private ReadOnly _applyGate As New SemaphoreSlim(1, 1)
    Private ReadOnly _published As New Dictionary(Of String, Published)(StringComparer.Ordinal)
    Private ReadOnly _tunnels As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    Private _session As ExchangeControlSession
    Private _connectionToken As CancellationToken
    Private _nudge As TaskCompletionSource(Of Boolean)
    ' A nudge that found no waiting watch (before its first loop, or between two): the next loop starts at once instead of after a poll.
    Private _nudged As Boolean
    ' The recipients object this publisher listens to (spec 234 item H: a changed recipient set publishes again).
    Private _hookedSealed As ExchangeSealedRecipients
    Private _snapshot As ExchangePublishSnapshot = ExchangePublishSnapshot.Empty
    Private _listenPort As Integer
    ''' <summary>The last plan outcome that was written to the log, so a blocked producer says why once instead of every pass.</summary>
    Private _lastBlockLogged As ExchangePublishBlock? = Nothing
    Private _grantedPort As Integer
    Private _disposed As Boolean

    ''' <summary>The snapshot changed (any thread).</summary>
    Public Event Changed As EventHandler

    Public Sub New(options As ExchangePublisherOptions)
        If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
        _options = options
        Interlocked.Increment(_constructed)
    End Sub

    Private Shared _constructed As Integer

    ''' <summary>Counts constructions, so a test can assert that a dormant start built none.</summary>
    Public Shared ReadOnly Property Constructed As Integer
        Get
            Return Volatile.Read(_constructed)
        End Get
    End Property

    Public Shared Function CreateDefault() As ExchangePublisher
        Return New ExchangePublisher(New ExchangePublisherOptions())
    End Function

    Public ReadOnly Property Snapshot As ExchangePublishSnapshot
        Get
            SyncLock _gate
                Return _snapshot
            End SyncLock
        End Get
    End Property

    ''' <summary>Tunnels open right now, over every resource (a test asserts the cap and the end).</summary>
    Public ReadOnly Property ActiveTunnels As Integer
        Get
            SyncLock _gate
                Dim n As Integer = 0
                For Each v As Integer In _tunnels.Values
                    n += v
                Next
                Return n
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Settings As ExchangePublishSettings
        Get
            Return _options.Settings
        End Get
    End Property

    ' --- the hooks ---------------------------------------------------------------------------------------------------------

    Public Async Function OnWelcomeAsync(session As ExchangeControlSession, ct As CancellationToken) As Task Implements IExchangeProducerHooks.OnWelcomeAsync
        SyncLock _gate
            _session = session
            _connectionToken = ct
            _published.Clear()          ' the server forgot `access` and `shareId` with the old connection (14.5 items G, H, K)
            _grantedPort = 0
            ' item H of 27 DI: the recipient set moves when a device joins, gains the capability or leaves - then the boxes are made again.
            ' The same session object serves every connection of this agent, so it is hooked once.
            If Not ReferenceEquals(_hookedSealed, session.Sealed) Then
                If _hookedSealed IsNot Nothing Then RemoveHandler _hookedSealed.Changed, AddressOf OnSealedChanged
                _hookedSealed = session.Sealed
                AddHandler _hookedSealed.Changed, AddressOf OnSealedChanged
            End If
        End SyncLock
        Try
            Await ApplyAsync(force:=True, ct).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            Return
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            _options.Log.Invoke("Exchange producer: the first publish failed - " & ex.GetType().Name)
        End Try
        ' the watch runs for as long as the connection does; it never completes the hook
        Dim unused As Task = Task.Run(Function() WatchAsync(ct))
    End Function

    Public Sub OnServerMessage(session As ExchangeControlSession, message As ExchangeEnvelope) Implements IExchangeProducerHooks.OnServerMessage
        If message Is Nothing OrElse Not message.Is(ExchangeEnvelopeTypes.Open) Then Return
        Dim tunnelId As String = message.Text("tunnelId")
        Dim resourceId As String = message.Text("resourceId")
        Dim shareId As String = message.Text("shareId")
        Dim ct As CancellationToken
        SyncLock _gate
            ct = _connectionToken
        End SyncLock
        Dim t As Task = Task.Run(Function() HandleOpenAsync(session, tunnelId, resourceId, shareId, ct))
    End Sub

    ''' <summary>Leaving: every resource on the server is unpublished, in turn, each best effort. The local share, its credential, its host
    ''' key, its `.fmscfg` and its QR are not touched - the share only stops being listed in the account.</summary>
    Public Async Function UnpublishAllAsync(session As ExchangeControlSession, ct As CancellationToken) As Task Implements IExchangeProducerLeave.UnpublishAllAsync
        If session Is Nothing Then Return
        Dim all As List(Of Published)
        SyncLock _gate
            all = _published.Values.ToList()
        End SyncLock
        If all.Any(Function(p) p.PinProtected) Then Await _options.InvalidatePinSessions.Invoke().ConfigureAwait(False)
        For Each p As Published In all
            Try
                Await session.SendAsync(ExchangeMessages.Unpublish(p.ResourceId), ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Exit For
            End Try
        Next
        _options.Log.Invoke("Exchange producer: unpublished " & all.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " resources before leaving")
    End Function

    Public Sub OnAccountForgotten() Implements IExchangeProducerHooks.OnAccountForgotten
        SyncLock _gate
            _published.Clear()
            _session = Nothing
            _grantedPort = 0
            _snapshot = ExchangePublishSnapshot.Empty
        End SyncLock
        _options.Store.Delete()
        _options.Settings.Reset()
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    ''' <summary>The window changed a choice, or a share changed: look again soon (debounced).</summary>
    Public Sub Notify()
        Dim n As TaskCompletionSource(Of Boolean)
        SyncLock _gate
            n = _nudge
            If n Is Nothing OrElse n.Task.IsCompleted Then _nudged = True
        End SyncLock
        If n IsNot Nothing Then n.TrySetResult(True)
    End Sub

    Private Sub OnSealedChanged(sender As Object, e As EventArgs)
        Notify()
    End Sub

    ' --- the watch ---------------------------------------------------------------------------------------------------------

    Private Async Function WatchAsync(ct As CancellationToken) As Task
        Try
            While Not ct.IsCancellationRequested AndAlso Not _disposed
                Dim nudge As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                SyncLock _gate
                    _nudge = nudge
                    If _nudged Then
                        _nudged = False
                        nudge.TrySetResult(True)
                    End If
                End SyncLock
                Dim poll As Task = _options.Delay.Invoke(TimeSpan.FromMilliseconds(_options.PollMs), ct)
                Await Task.WhenAny(poll, nudge.Task).ConfigureAwait(False)
                ct.ThrowIfCancellationRequested()
                Dim needed As Boolean = Await ChangeNeededAsync(ct).ConfigureAwait(False)
                If Not needed Then Continue While
                ' what a descriptor is built from moved: wait until it has been quiet for the debounce, then publish
                Await _options.Delay.Invoke(TimeSpan.FromMilliseconds(_options.DebounceMs), ct).ConfigureAwait(False)
                Await ApplyAsync(force:=False, ct).ConfigureAwait(False)
            End While
        Catch ex As OperationCanceledException
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            _options.Log.Invoke("Exchange producer: the watch stopped - " & ex.GetType().Name)
        End Try
    End Function

    Private Async Function ChangeNeededAsync(ct As CancellationToken) As Task(Of Boolean)
        Dim plan As ExchangePublishPlan = Await ComputePlanAsync(ct).ConfigureAwait(False)
        If plan Is Nothing Then Return False
        Return Differs(plan)
    End Function

    ''' <summary>True when the server's copy is not what the plan wants: a share to add, a share to drop, a changed record, or one that
    ''' never made it.</summary>
    Private Function Differs(plan As ExchangePublishPlan) As Boolean
        SyncLock _gate
            If _session Is Nothing Then Return False
            ' The reason a plan publishes nothing can move without a share moving (the directory settles after the welcome, so the first
            ' pass saw "no key yet" and a later one sees the PIN lock): the window shows the plan's block, so it is looked at again.
            If plan.Block <> _snapshot.Block Then Return True
            For Each name As String In _published.Keys
                If plan.Find(name) Is Nothing Then Return True
            Next
            Dim recipients As Long = _session.Sealed.Revision
            For Each s As ExchangePlannedShare In plan.Shares
                Dim p As Published = Nothing
                If Not _published.TryGetValue(s.Name, p) Then Return True
                If p.State <> ExchangePublishState.Published AndAlso p.State <> ExchangePublishState.Refused Then Return True
                If Not String.Equals(p.Signature, SignatureOf(s, recipients), StringComparison.Ordinal) Then Return True
            Next
            Return False
        End SyncLock
    End Function

    ''' <summary>What a published record was made from. The recipient set rides in it (spec 234 item H): a sealed box is addressed to the
    ''' devices of that moment, so when one joins, gains the capability, changes its key or leaves, the record is made again.</summary>
    Private Shared Function SignatureOf(s As ExchangePlannedShare, recipientRevision As Long) As String
        Dim text As String = s.ResourceId & vbLf & s.ShareId & vbLf & s.AccessJson & vbLf & If(s.PublicPortRequest.HasValue, s.PublicPortRequest.Value.ToString(Globalization.CultureInfo.InvariantCulture), "-") & vbLf & s.PinProtected.ToString() &
                             vbLf & recipientRevision.ToString(Globalization.CultureInfo.InvariantCulture)
        Return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
    End Function

    ''' <summary>Item DI: the folder's `access` as a box for the recipients of this entry, with the identity pair that stays in clear
    ''' (item BA: the host key fingerprint and the root). False when it cannot be sealed - this device has no key, no recipient takes it -
    ''' and then nothing at all is published for the folder: there is no clear fallback (item DP). Nothing is logged but the fact.</summary>
    Private Shared Function TrySeal(session As ExchangeControlSession, share As ExchangePlannedShare, deviceId As String,
                                    ByRef boxJson As String, ByRef fingerprint As String, ByRef root As String) As Boolean
        boxJson = ""
        fingerprint = ""
        root = ""
        Dim info As ExchangeAccessInfo = Nothing
        If Not ExchangeResourceAccess.TryRead(share.AccessJson, share.Name, info) Then Return False
        Dim load As Func(Of ExchangeDeviceKey) = session.ConnectionOptions.OwnKey
        Dim key As ExchangeDeviceKey = If(load Is Nothing, Nothing, load.Invoke())
        If key Is Nothing Then Return False
        Try
            If Not ExchangeSealedAccess.SealAccess(share.AccessJson, share.ResourceId, deviceId, key, session.Sealed.Recipients(), boxJson) Then Return False
        Finally
            key.Dispose()
        End Try
        fingerprint = info.Code.Fingerprint
        root = info.Root
        Return True
    End Function

    ' --- planning and applying ---------------------------------------------------------------------------------------------

    Private Async Function ComputePlanAsync(ct As CancellationToken) As Task(Of ExchangePublishPlan)
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return Nothing
        Dim deviceId As String = session.ConnectionOptions.Credentials.DeviceId
        Dim publish As Boolean = _options.Settings.PublishShares()
        Dim status As WorkerStatus = Nothing
        If publish Then
            Try
                Using cts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    cts.CancelAfter(_options.StatusTimeoutMs)
                    Dim read As Task(Of WorkerStatus) = _options.GetStatus.Invoke()
                    Dim winner As Task = Await Task.WhenAny(read, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(False)
                    If winner Is read Then status = Await read.ConfigureAwait(False)
                End Using
            Catch ex As OperationCanceledException When Not ct.IsCancellationRequested
                status = Nothing
            Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
                _options.Log.Invoke("Exchange producer: the worker status could not be read - " & ex.GetType().Name)
                status = Nothing
            End Try
        End If
        ct.ThrowIfCancellationRequested()
        Dim state As ExchangeProducerState = If(publish, _options.Store.Load(deviceId), ExchangeProducerState.Empty)
        ' spec 234 items DP, DF, DG: a folder is published sealed or not at all. The server must list `sealed`; this device must hold a key
        ' and be a ready recipient of its own entry (the directory lists it with the capability and the key it holds).
        Dim sealedView As ExchangeSealedRecipients = session.Sealed
        Dim inputs As New ExchangePublishInputs With {
            .Publish = publish, .ThirdPartyPort = _options.Settings.ThirdPartyPort(), .Status = status, .DeviceId = deviceId,
            .PublicEndpoint = session.PublicEndpoint, .IncludeExternal = Not _options.LanOnly.Invoke(),
            .LanFallback = _options.LanFallback.Invoke(), .HasPin = _options.HasPin, .State = state,
            .ServerSealed = Not sealedView.ServerKnown OrElse sealedView.ServerSealed,
            .OwnKeyReady = sealedView.HasOwnKey AndAlso sealedView.Recipients().Count > 0}
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(inputs)
        If publish AndAlso (Not _lastBlockLogged.HasValue OrElse _lastBlockLogged.Value <> plan.Block) Then
            _lastBlockLogged = plan.Block
            ' facts only (booleans and counts) - never the credential, the address or the fingerprint themselves
            Dim detail As String = ""
            If plan.Block <> ExchangePublishBlock.None AndAlso plan.Block <> ExchangePublishBlock.Off Then
                If status Is Nothing Then
                    detail = " (no worker status)"
                Else
                    detail = " (running=" & status.Running.ToString() & ", port=" & (status.ListenPort > 0).ToString() &
                             ", user=" & (Not String.IsNullOrEmpty(status.Username)).ToString() &
                             ", password=" & (Not String.IsNullOrEmpty(status.Password)).ToString() &
                             ", fingerprint=" & ExchangeTrust.IsFingerprint(status.Fingerprint).ToString() &
                             ", roots=" & If(status.Roots Is Nothing, 0, status.Roots.Count).ToString(Globalization.CultureInfo.InvariantCulture) & ")"
                End If
            End If
            _options.Log.Invoke("Exchange producer: plan " & plan.Block.ToString() & " for " & plan.Shares.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " folders" & detail)
        End If
        If status IsNot Nothing AndAlso status.ListenPort > 0 Then
            SyncLock _gate
                _listenPort = status.ListenPort
            End SyncLock
        End If
        Return plan
    End Function

    ''' <summary>Reads the world, then makes the server's copy match: unpublish what is no longer wanted, publish what is new or changed
    ''' (everything when <paramref name="force"/>), save the ids that were created. Serialized: two passes never interleave.</summary>
    Private Async Function ApplyAsync(force As Boolean, ct As CancellationToken) As Task
        Await _applyGate.WaitAsync(ct).ConfigureAwait(False)
        Try
            Await ApplyCoreAsync(force, ct).ConfigureAwait(False)
        Finally
            _applyGate.Release()
        End Try
    End Function

    ' Caller owns _applyGate across plan load, store mutation and publication. Link commands
    ' share that transaction boundary so a background pass cannot save their previous state.
    Private Async Function ApplyCoreAsync(force As Boolean, ct As CancellationToken) As Task
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return
        Dim plan As ExchangePublishPlan = Await ComputePlanAsync(ct).ConfigureAwait(False)
        If plan Is Nothing Then Return
        Dim deviceId As String = session.ConnectionOptions.Credentials.DeviceId

        ' the ids created for shares seen for the first time are kept BEFORE they are published
        If plan.Block = ExchangePublishBlock.None Then
            Dim state As ExchangeProducerState = plan.State
            If Not _options.Store.Save(deviceId, state) Then _options.Log.Invoke("Exchange producer: the share ids could not be stored under DPAPI")
        End If

        ' what is on the server and no longer wanted goes
        Dim gone As New List(Of KeyValuePair(Of String, Published))
        SyncLock _gate
            For Each pair As KeyValuePair(Of String, Published) In _published
                If plan.Find(pair.Key) Is Nothing Then gone.Add(pair)
            Next
        End SyncLock
        If gone.Any(Function(p) p.Value.PinProtected) Then Await _options.InvalidatePinSessions.Invoke().ConfigureAwait(False)
        For Each pair As KeyValuePair(Of String, Published) In gone
            Await session.SendAsync(ExchangeMessages.Unpublish(pair.Value.ResourceId), ct).ConfigureAwait(False)
            SyncLock _gate
                _published.Remove(pair.Key)
            End SyncLock
            _options.Log.Invoke("Exchange producer: unpublished resource " & ExchangeIds.ForLog(pair.Value.ResourceId))
        Next

        Dim state2 As ExchangeProducerState = plan.State
        ' read BEFORE the boxes are made: a set that moves meanwhile leaves an older number in the signature, so the next pass seals again
        Dim recipientRevision As Long = session.Sealed.Revision
        For Each share As ExchangePlannedShare In plan.Shares
            Dim signature As String = SignatureOf(share, recipientRevision)
            Dim current As Published = Nothing
            SyncLock _gate
                _published.TryGetValue(share.Name, current)
            End SyncLock
            If Not force AndAlso current IsNot Nothing AndAlso String.Equals(current.Signature, signature, StringComparison.Ordinal) AndAlso
               current.State = ExchangePublishState.Published Then Continue For
            state2 = Await PublishOneAsync(session, share, signature, state2, deviceId, ct).ConfigureAwait(False)
        Next
        Rebuild(plan)
    End Function

    ''' <summary>One `publish` and its answer; keeps the granted third-party port. Returns the state (the carrier may have moved).</summary>
    Private Async Function PublishOneAsync(session As ExchangeControlSession, share As ExchangePlannedShare, signature As String,
                                           state As ExchangeProducerState, deviceId As String, ct As CancellationToken) As Task(Of ExchangeProducerState)
        Dim request As Integer? = share.PublicPortRequest
        For attempt As Integer = 1 To 2
            Dim json As String = Nothing
            ' item DI: the record carries `sealedAccess` and NO `access`; the identity pair stays in clear (item BA)
            Dim boxJson As String = ""
            Dim fingerprint As String = ""
            Dim root As String = ""
            If Not TrySeal(session, share, deviceId, boxJson, fingerprint, root) Then
                SetPublished(share, signature, ExchangePublishState.Refused, ExchangeRefusal.Unknown)
                _options.Log.Invoke("Exchange producer: resource " & ExchangeIds.ForLog(share.ResourceId) & " cannot be sealed for this account, nothing is published (e2e-missing)")
                Return state
            End If
            If Not ExchangeResourceRecordBuilder.TryBuild(share.ResourceId, deviceId, share.Name, "", share.ShareId, request, _options.NowUtc.Invoke(), json, share.PinProtected,
                                                          boxJson, fingerprint, root) Then
                SetPublished(share, signature, ExchangePublishState.Refused, ExchangeRefusal.Capacity)
                _options.Log.Invoke("Exchange producer: the record of resource " & ExchangeIds.ForLog(share.ResourceId) & " cannot be built")
                Return state
            End If
            Dim reply As ExchangeEnvelope = Await session.RequestAsync(ExchangeMessages.Publish(json), {ExchangeEnvelopeTypes.Published}, _options.PublishTimeoutMs, ct).ConfigureAwait(False)
            json = Nothing
            If reply Is Nothing Then
                SetPublished(share, signature, ExchangePublishState.Pending, ExchangeRefusal.Unknown)
                _options.Log.Invoke("Exchange producer: no answer to the publish of resource " & ExchangeIds.ForLog(share.ResourceId))
                Return state
            End If
            If reply.Is(ExchangeEnvelopeTypes.Published) Then
                SetPublished(share, signature, ExchangePublishState.Published, ExchangeRefusal.Unknown)
                Dim granted As Long? = reply.NumberOrNothing("publicPort")
                If request.HasValue AndAlso granted.HasValue AndAlso granted.Value >= 1 AndAlso granted.Value <= 65535 Then
                    SyncLock _gate
                        _grantedPort = CInt(granted.Value)
                    End SyncLock
                    If state.CarrierPort <> CInt(granted.Value) OrElse Not String.Equals(state.CarrierName, share.Name, StringComparison.Ordinal) Then
                        state = state.WithCarrier(share.Name, CInt(granted.Value))
                        _options.Store.Save(deviceId, state)
                    End If
                End If
                _options.Log.Invoke("Exchange producer: published resource " & ExchangeIds.ForLog(share.ResourceId))
                Return state
            End If
            Dim why As ExchangeRefusal = reply.Refusal()
            ' a port that cannot be had is asked for as "any" once, then given up; every other refusal is shown, never retried in a loop
            If why = ExchangeRefusal.PortUnavailable AndAlso request.HasValue AndAlso request.Value <> 0 AndAlso attempt = 1 Then
                request = 0
                Continue For
            End If
            SetPublished(share, signature, ExchangePublishState.Refused, why)
            _options.Log.Invoke("Exchange producer: the server refused resource " & ExchangeIds.ForLog(share.ResourceId) & " (" & ExchangeRefusals.WireName(why) & ")")
            Return state
        Next
        Return state
    End Function

    Private Sub SetPublished(share As ExchangePlannedShare, signature As String, state As ExchangePublishState, refusal As ExchangeRefusal)
        SyncLock _gate
            _published(share.Name) = New Published(share.ResourceId, share.ShareId, signature, share.AccessJson, state, refusal, share.PinProtected)
        End SyncLock
    End Sub

    Private Sub Rebuild(plan As ExchangePublishPlan)
        Dim rows As New List(Of ExchangePublishRow)
        Dim endpoint As String = ""
        Dim connected As Boolean
        SyncLock _gate
            connected = _session IsNot Nothing
            For Each s As ExchangePlannedShare In plan.Shares
                Dim p As Published = Nothing
                If _published.TryGetValue(s.Name, p) Then
                    rows.Add(New ExchangePublishRow(s.Name, p.State, p.Refusal, s.ShareId.Length > 0, s.PinProtected))
                Else
                    rows.Add(New ExchangePublishRow(s.Name, ExchangePublishState.Pending, ExchangeRefusal.Unknown, s.ShareId.Length > 0, s.PinProtected))
                End If
            Next
            If _grantedPort > 0 AndAlso _session IsNot Nothing Then
                Dim ep As ExchangeEndpoint = Nothing
                If ExchangeEndpointRules.TryParse(_session.PublicEndpoint, ep) Then endpoint = New ExchangeEndpoint(ep.Host, _grantedPort).ToString()
            End If
            _snapshot = New ExchangePublishSnapshot(_options.Settings.PublishShares(), _options.Settings.ThirdPartyPort(), plan.Block, connected, rows, endpoint)
        End SyncLock
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Publishes now, outside the watch's rhythm (the window just changed a choice). Never throws.</summary>
    Public Async Function ApplyNowAsync(ct As CancellationToken) As Task
        Try
            Await ApplyAsync(force:=False, ct).ConfigureAwait(False)
        Catch ex As OperationCanceledException
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            _options.Log.Invoke("Exchange producer: applying failed - " & ex.GetType().Name)
        End Try
    End Function

    ' --- outsider links (rule 8) -------------------------------------------------------------------------------------------

    ''' <summary>Creates the outsider link of a share: a share id of 16 random bytes, published with the record. Nothing is created by
    ''' publishing alone. Spec 228 rule 4/5: supports link with a password.</summary>
    Public Function CreateLinkAsync(name As String, ct As CancellationToken, Optional password As String = "") As Task(Of ExchangeLinkResult)
        Return ChangeLinkAsync(name, rotate:=False, remove:=False, ct, password)
    End Function

    ''' <summary>"New link": rotates the id - the only revocation the capability model has. Every earlier QR and file stops working.</summary>
    Public Function RotateLinkAsync(name As String, ct As CancellationToken, Optional newPassword As String = "") As Task(Of ExchangeLinkResult)
        Return ChangeLinkAsync(name, rotate:=True, remove:=False, ct, newPassword)
    End Function

    ''' <summary>"Remove link": the share id is deleted and the record is published without it.</summary>
    Public Function RemoveLinkAsync(name As String, ct As CancellationToken) As Task(Of ExchangeLinkResult)
        Return ChangeLinkAsync(name, rotate:=False, remove:=True, ct)
    End Function

    Private Async Function ChangeLinkAsync(name As String, rotate As Boolean, remove As Boolean, ct As CancellationToken, Optional password As String = "") As Task(Of ExchangeLinkResult)
        Await _applyGate.WaitAsync(ct).ConfigureAwait(False)
        Try
            Return Await ChangeLinkCoreAsync(name, rotate, remove, ct, password).ConfigureAwait(False)
        Finally
            _applyGate.Release()
        End Try
    End Function

    Private Async Function ChangeLinkCoreAsync(name As String, rotate As Boolean, remove As Boolean, ct As CancellationToken, password As String) As Task(Of ExchangeLinkResult)
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return New ExchangeLinkResult(ExchangeLinkOutcome.NotConnected, ExchangeRefusal.Unknown)
        Dim deviceId As String = session.ConnectionOptions.Credentials.DeviceId
        If Not remove Then
            Dim ep As ExchangeEndpoint = Nothing
            If Not ExchangeEndpointRules.TryParse(session.PublicEndpoint, ep) OrElse ExchangeEndpointRules.IsLoopbackOrUnspecified(ep.Host) Then
                Return New ExchangeLinkResult(ExchangeLinkOutcome.NoEndpoint, ExchangeRefusal.Unknown)
            End If
        End If
        Dim plan As ExchangePublishPlan = Await ComputePlanAsync(ct).ConfigureAwait(False)
        If plan Is Nothing OrElse plan.Block <> ExchangePublishBlock.None OrElse plan.Find(name) Is Nothing Then
            Return New ExchangeLinkResult(ExchangeLinkOutcome.NotPublished, ExchangeRefusal.Unknown)
        End If
        If Not remove AndAlso plan.Find(name).PinProtected Then Return New ExchangeLinkResult(ExchangeLinkOutcome.Failed, ExchangeRefusal.ProtectionUnsupported)
        Dim entry As ExchangeProducerEntry = plan.State.Get(name)
        If entry Is Nothing Then Return New ExchangeLinkResult(ExchangeLinkOutcome.NotPublished, ExchangeRefusal.Unknown)
        If remove Then
            If entry.ShareId.Length = 0 Then Return New ExchangeLinkResult(ExchangeLinkOutcome.Done, ExchangeRefusal.Unknown)
            entry = entry.WithLink("", "")
        ElseIf entry.ShareId.Length = 0 OrElse rotate OrElse Not String.IsNullOrEmpty(password) Then
            Dim newId As String = If(entry.ShareId.Length > 0 AndAlso Not rotate, entry.ShareId, ExchangeIds.NewId())
            Dim pw As String = If(Not String.IsNullOrEmpty(password), password, entry.LinkPassword)
            entry = entry.WithLink(newId, pw)
        Else
            Return New ExchangeLinkResult(ExchangeLinkOutcome.Done, ExchangeRefusal.Unknown)   ' already there
        End If
        If Not _options.Store.Save(deviceId, plan.State.WithEntry(name, entry)) Then
            Return New ExchangeLinkResult(ExchangeLinkOutcome.Failed, ExchangeRefusal.Unknown)
        End If
        _options.Log.Invoke("Exchange producer: link " & If(remove, "removed", If(rotate, "rotated", "created")) & " for resource " & ExchangeIds.ForLog(entry.ResourceId))
        Await ApplyCoreAsync(force:=False, ct).ConfigureAwait(False)
        Dim row As ExchangePublishRow = Snapshot.RowFor(name)
        If row IsNot Nothing AndAlso row.State = ExchangePublishState.Refused Then Return New ExchangeLinkResult(ExchangeLinkOutcome.Failed, row.Refusal)
        If row Is Nothing OrElse row.State <> ExchangePublishState.Published Then Return New ExchangeLinkResult(ExchangeLinkOutcome.Failed, ExchangeRefusal.Unknown)
        Return New ExchangeLinkResult(ExchangeLinkOutcome.Done, ExchangeRefusal.Unknown)
    End Function

    ''' <summary>The FMSSFTP2 text of a share that has a link (a secret: shown through the window's guarded door, never logged). False
    ''' when there is none. The text is what leaves the account, so it carries no password (ANYWHERE-ACCESS item K): the person it is
    ''' handed to is asked for the link's password instead.</summary>
    Public Function TryGetLinkCode(name As String, ByRef codeText As String) As Boolean
        codeText = ""
        Dim p As Published = Nothing
        SyncLock _gate
            If Not _published.TryGetValue(name, p) OrElse p.State <> ExchangePublishState.Published OrElse p.ShareId.Length = 0 OrElse p.PinProtected Then Return False
        End SyncLock
        Dim info As ExchangeAccessInfo = Nothing
        If Not ExchangeResourceAccess.TryRead(p.AccessJson, name, info) OrElse info.Code.ShareId.Length = 0 Then Return False
        ' The install's shared login never leaves the account (DEVICE-EXCHANGE item L): the outsider code is written in the
        ' key-login form - no `pw`. (The confined per-link login is owed by the worker proposal; until it exists the code
        ' opens nothing without the producer's boundary learning the link's password.)
        Dim outsider As New FmsSftpCode(info.Code.Hosts, info.Code.Port, info.Code.Username, Nothing, info.Code.Fingerprint,
                                        info.Code.Exchange, info.Code.ShareId, info.Code.Drive)
        Return outsider.TryBuild(codeText)
    End Function

    ''' <summary>What an export (.fmscfg + its QR) takes from this share's link: the server's endpoint, the share id and the granted
    ''' third-party port (0 when none). Nothing when the share has no live link.</summary>
    Public Function GetExport(name As String) As ExchangeExport
        Dim p As Published = Nothing
        Dim endpoint As String = ""
        Dim granted As Integer
        SyncLock _gate
            If _session Is Nothing OrElse Not _published.TryGetValue(name, p) OrElse p.State <> ExchangePublishState.Published OrElse p.ShareId.Length = 0 OrElse p.PinProtected Then Return Nothing
            endpoint = _session.PublicEndpoint
            granted = _grantedPort
        End SyncLock
        Dim ep As ExchangeEndpoint = Nothing
        If Not ExchangeEndpointRules.TryParse(endpoint, ep) OrElse ExchangeEndpointRules.IsLoopbackOrUnspecified(ep.Host) Then Return Nothing
        Dim carrier As Boolean = _options.Settings.ThirdPartyPort() AndAlso granted > 0
        Return New ExchangeExport With {.ClientHost = ep.Host, .ClientPort = ep.Port, .ShareId = p.ShareId, .RelayPort = If(carrier, granted, 0)}
    End Function

    ''' <summary>What the package wizard's export takes from the exchange: the live link of the FIRST share of the exported set (roots order)
    ''' that has one - one .fmscfg carries one tunnel path, and every link reaches the same worker. Nothing when there is none, so with no
    ''' agent, no account, publication off or no link the export is exactly what it was.</summary>
    Public Function ExportFor(status As WorkerStatus) As ExchangeExport
        If status Is Nothing OrElse status.Roots Is Nothing Then Return Nothing
        For Each root As ShareFolder In status.Roots
            If root Is Nothing OrElse String.IsNullOrEmpty(root.name) Then Continue For
            Dim export As ExchangeExport = GetExport(root.name)
            If export IsNot Nothing Then Return export
        Next
        Return Nothing
    End Function

    ''' <summary>The .fmscfg (and its QR) of ONE share's outsider link: the same builder as every export, with the tunnel path (and the
    ''' third-party port, when granted) added and only this share in the file. Nothing when the share has no live link.</summary>
    Public Function BuildLinkExport(name As String, status As WorkerStatus) As ShareConfigResult
        If status Is Nothing OrElse status.Roots Is Nothing Then Return Nothing
        Dim export As ExchangeExport = GetExport(name)
        If export Is Nothing Then Return Nothing
        Dim only As ShareFolder = status.Roots.FirstOrDefault(Function(r) r IsNot Nothing AndAlso String.Equals(r.name, name, StringComparison.Ordinal))
        If only Is Nothing Then Return Nothing
        Dim narrowed As New WorkerStatus With {
            .Mode = status.Mode, .Running = status.Running, .ListenPort = status.ListenPort, .Fingerprint = status.Fingerprint,
            .Username = status.Username, .Password = status.Password, .Roots = New List(Of ShareFolder) From {only},
            .Reachability = status.Reachability, .DesiredPort = status.DesiredPort}
        ' The link's .fmscfg leaves the account: no shared password in it (DEVICE-EXCHANGE item L); the reader asks
        ' for the link's password instead. The direct hosts stay - a worker that is not given the link's password
        ' answers them with an authentication failure, which is the point.
        Return ShareConfigBuilder.Build(narrowed, includeExternal:=True, includePassword:=False, exchange:=export)
    End Function

    ' --- tunnels (rule 6) --------------------------------------------------------------------------------------------------

    Friend Shared Async Function DialWorkerLoopbackAsync(port As Integer, ct As CancellationToken) As Task(Of System.IO.Stream)
        Dim client As New TcpClient(AddressFamily.InterNetwork)
        Try
            client.NoDelay = True
            Await client.ConnectAsync(System.Net.IPAddress.Loopback, port, ct).ConfigureAwait(False)
            Return client.GetStream()
        Catch
            client.Dispose()
            Throw
        End Try
    End Function

    Private Function FindByIds(resourceId As String, shareId As String) As String
        SyncLock _gate
            For Each pair As KeyValuePair(Of String, Published) In _published
                If pair.Value.State <> ExchangePublishState.Published Then Continue For
                If resourceId.Length > 0 AndAlso String.Equals(pair.Value.ResourceId, resourceId, StringComparison.Ordinal) Then Return pair.Key
                If shareId.Length > 0 AndAlso pair.Value.ShareId.Length > 0 AndAlso String.Equals(pair.Value.ShareId, shareId, StringComparison.Ordinal) Then Return pair.Key
            Next
        End SyncLock
        Return ""
    End Function

    ''' <summary>One `open`: find the share, dial the worker (5 s), attach, pump. A failure at any step closes the attach stream with no
    ''' answer - the consumer gets the end of the stream, never a reason that would tell it what exists.</summary>
    Friend Async Function HandleOpenAsync(session As ExchangeControlSession, tunnelId As String, resourceId As String, shareId As String, ct As CancellationToken) As Task
        If String.IsNullOrEmpty(tunnelId) OrElse session Is Nothing Then Return
        Dim name As String = FindByIds(resourceId, shareId)
        Dim port As Integer
        Dim counted As Boolean = False
        Dim worker As System.IO.Stream = Nothing
        Dim attached As ExchangeOpenResult = Nothing
        Try
            SyncLock _gate
                port = _listenPort
                If name.Length > 0 Then
                    Dim n As Integer = 0
                    _tunnels.TryGetValue(name, n)
                    If n < _options.MaxTunnelsPerResource Then
                        _tunnels(name) = n + 1
                        counted = True
                    End If
                End If
            End SyncLock
            ' dial the worker first: an unknown share, a full resource or a worker that is down is answered by closing the attach at once
            If counted AndAlso port > 0 Then
                Using dial As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    dial.CancelAfter(_options.WorkerDialTimeoutMs)
                    Try
                        worker = Await _options.DialWorker.Invoke(port, dial.Token).ConfigureAwait(False)
                    Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is SocketException OrElse TypeOf ex Is System.IO.IOException
                        worker = Nothing
                    End Try
                End Using
            End If
            Dim opts As ExchangeControlOptions = session.ConnectionOptions
            attached = Await ExchangeDataStreams.AttachAsync(opts.Transport, opts.Server, opts.Pin, opts.Credentials, opts.ProductVersion, tunnelId, ct).ConfigureAwait(False)
            If attached Is Nothing OrElse Not attached.IsConnected Then Return
            If worker Is Nothing Then
                _options.Log.Invoke("Exchange producer: tunnel " & ExchangeIds.ForLog(tunnelId) & " answered by closing (no share or no worker)")
                Return     ' disposed below: the consumer sees the stream end
            End If
            Dim result As ExchangeBridgeResult = Await ExchangeBridge.RunAsync(attached.Stream, worker, New ExchangeBridgeOptions With {.IdleTimeout = _options.TunnelIdleTimeout}, ct).ConfigureAwait(False)
            _options.Log.Invoke("Exchange producer: tunnel " & ExchangeIds.ForLog(tunnelId) & " ended (" & result.Ended.ToString() & ")")
        Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is System.IO.IOException OrElse TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is ExchangeWireException
            ' the connection ended under it
        Finally
            Try : worker?.Dispose() : Catch : End Try
            Try : attached?.Dispose() : Catch : End Try
            If counted Then
                SyncLock _gate
                    Dim n As Integer = 0
                    If _tunnels.TryGetValue(name, n) Then
                        If n <= 1 Then _tunnels.Remove(name) Else _tunnels(name) = n - 1
                    End If
                End SyncLock
            End If
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _disposed = True
        Dim n As TaskCompletionSource(Of Boolean)
        SyncLock _gate
            n = _nudge
        End SyncLock
        If n IsNot Nothing Then n.TrySetResult(True)
    End Sub

End Class

''' <summary>
''' Spec 228 rule 5: Enforces password verification and 1 check/sec rate limiting for link access.
''' </summary>
Friend NotInheritable Class LinkPasswordValidator
    Private Shared ReadOnly _attempts As New Dictionary(Of String, Long)(StringComparer.Ordinal)
    Private Shared ReadOnly _gate As New Object()

    Friend Shared Function VerifyPassword(shareId As String, candidate As String, expected As String, Optional clockNowMs As Long = -1) As Boolean
        If String.IsNullOrEmpty(expected) Then Return True
        Dim now As Long = If(clockNowMs >= 0, clockNowMs, MonotonicClock.NowMs())
        SyncLock _gate
            Dim lastMs As Long = 0
            If _attempts.TryGetValue(shareId, lastMs) Then
                If (now - lastMs) < 1000 Then
                    ' Rate limit: at most one check per second
                    Return False
                End If
            End If

            If String.IsNullOrEmpty(candidate) Then
                _attempts(shareId) = now
                Return False
            End If

            Dim candBytes As Byte() = Encoding.UTF8.GetBytes(candidate)
            Dim expBytes As Byte() = Encoding.UTF8.GetBytes(expected)
            Dim matched As Boolean = CryptographicOperations.FixedTimeEquals(candBytes, expBytes)
            If Not matched Then
                _attempts(shareId) = now
                Return False
            End If
            Return True
        End SyncLock
    End Function

    Friend Shared Sub ResetForTesting()
        SyncLock _gate
            _attempts.Clear()
        End SyncLock
    End Sub
End Class

