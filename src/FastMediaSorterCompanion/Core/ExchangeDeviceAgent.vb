Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Sockets
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 180 T-2 (sections 3-7): the device agent. ONE per Windows user, inside the Share Manager. It owns the device
' id, the token (DPAPI), the certificate pin, the control stream and the directory model; it enrolls this PC into an
' account, issues pairing codes, lists and revokes the account's devices, and serves the viewer through the local channel.
'
' Dormant (rule 1): with no account configured nothing here runs - ExchangeDeviceHost.StartIfConfigured constructs no agent,
' so there is no connection, no pipe, no registry value and no file. Gated (rule 2): every entry reads ExchangeFeatures.
' One control stream per device (rule 3): the viewer uses data streams and the local channel only.
'
' Secrets: the password exists in the enrollment request and in the `enroll` frame, and both are overwritten once sent;
' the token goes straight from the `enrolled` envelope into ExchangeCredentialStore; nothing secret reaches a log - ids are
' written as the first 8 hex of their SHA-256 (ExchangeIds.ForLog).

''' <summary>Everything the agent needs from outside, so a test can hand in fakes and count the calls.</summary>
Friend NotInheritable Class ExchangeAgentPorts

    Public Property Accounts As ExchangeAccountStore = ExchangeAccountStore.CreateDefault()
    Public Property Credentials As ExchangeCredentialStore = ExchangeCredentialStore.CreateDefault()
    Public Property Transport As IExchangeTransport = ExchangeTlsTransport.Instance
    Public Property RoleFacts As IExchangeRoleFacts = New ExchangeProductionRoleFacts()
    Public Property ProductVersion As String = ExchangeProductVersion.Current()
    Public Property MachineName As Func(Of String) = Function() Environment.MachineName
    Public Property Log As Action(Of String) = Sub(line) AppFileLogger.WriteLine(line)
    ''' <summary>Adjusts the control session's options (a test shortens the keepalive unit and the backoff).</summary>
    Public Property ConfigureSession As Action(Of ExchangeControlOptions) = Sub(o) Return
    ''' <summary>Starts the local channel for a handler; Nothing disables it (a test without a pipe).</summary>
    Public Property StartLocalChannel As Func(Of IExchangeAgentHandler, IDisposable) = Function(h) ExchangeDeviceLocalChannel.StartDefault(h)
    ''' <summary>Ticket 170: the producer (publishes the shares, answers `open`). Nothing by default - a test that does not ask for it
    ''' never publishes; the production start (<see cref="ExchangeDeviceHost"/>) hands in the real one.</summary>
    Public Property Producer As IExchangeProducerHooks = Nothing
    ''' <summary>Where the private device key lives (spec 234, item DF). Nothing: DPAPI files beside the token store. A test hands in
    ''' the in-memory one.</summary>
    Public Property DeviceKeys As IExchangeDeviceKeyStore = Nothing
    ''' <summary>The kept first-seen keys of the account's devices (item DG). Nothing: a file beside the token store.</summary>
    Public Property SealedTrust As IExchangeSealedTrustStore = Nothing
    ''' <summary>What the account's other devices list on Google Drive (203 section 5.11): merged into the directory the viewer reads, and into
    ''' nothing else. The default asks the process's Drive host without creating it; a test hands in its own.</summary>
    Public Property DriveDirectory As Func(Of ExchangeDirectorySnapshot) = Function() DriveChannelHosts.ListedDirectory()

End Class

''' <summary>The roles this program can declare today (item CE: `fms-windows` - the producer, the consumer, the broadcaster, the receiver and
''' `clipboard`). A role is declared only where its code is built and on: a role is never declared ahead of its code, because a role not
''' declared is never asked of the device (6.1) and one declared and not served is a broken promise to the account.
''' The roles travel in `enroll` and nowhere else (the wire has no frame that changes them later), so they say what this PC CAN do, not
''' what is switched on this minute: the share worker is installed (170 - whether it publishes is the user's setting, off by default) and
''' the viewer is installed beside it (171).</summary>
Friend NotInheritable Class ExchangeProductionRoleFacts
    Implements IExchangeRoleFacts

    Public Function SharesPublished() As Boolean Implements IExchangeRoleFacts.SharesPublished
        Try
            Return WorkerProcess.IsAvailable()
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    Public Function ViewerAttaches() As Boolean Implements IExchangeRoleFacts.ViewerAttaches
        Try
            Return IO.File.Exists(IO.Path.Combine(AppContext.BaseDirectory, "FastMediaSorter_LITE.exe"))
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    Public Function CanReceive() As Boolean Implements IExchangeRoleFacts.CanReceive
        Return ExchangeFeatures.IsEnabled() AndAlso ViewerAttaches()
    End Function

    Public Function LiveBroadcastEnabled() As Boolean Implements IExchangeRoleFacts.LiveBroadcastEnabled
        Return LiveBroadcastFeatures.IsEnabled()
    End Function

    ''' <summary>The Share Manager sends and gets the clipboard (spec 230, item BV): it takes part wherever the exchange client exists.
    ''' It is a Share Manager function only - the viewer never declares anything, and this class is the Share Manager's.</summary>
    Public Function TakesPartInClipboard() As Boolean Implements IExchangeRoleFacts.TakesPartInClipboard
        Return ExchangeFeatures.IsEnabled()
    End Function
End Class

Friend NotInheritable Class ExchangeProductVersion
    Private Sub New()
    End Sub

    ''' <summary>The build's stamp (YY.M.D.HHmm), as the exe's version carries it.</summary>
    Public Shared Function Current() As String
        Try
            Dim v As Version = Reflection.Assembly.GetExecutingAssembly().GetName().Version
            If v Is Nothing Then Return ""
            Return v.ToString()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            Return ""
        End Try
    End Function
End Class

Friend Enum ExchangeEnrollOutcome
    Enrolled = 0
    ''' <summary>The server answered `refused`; <see cref="ExchangeEnrollResult.Refusal"/> says why.</summary>
    Refused = 1
    ''' <summary>A pin exists and the certificate differs.</summary>
    PinMismatch = 2
    ''' <summary>The platform does not accept the certificate and the user has not accepted its fingerprint.</summary>
    Untrusted = 3
    TlsFailed = 4
    Network = 5
    TimedOut = 6
    Cancelled = 7
    ''' <summary>The server answered something an enrollment does not allow.</summary>
    Protocol = 8
    ''' <summary>The address, the login or the credential typed is not usable (nothing was sent).</summary>
    BadInput = 9
    ''' <summary>Enrolled, but the token could not be stored: nothing is configured here, and a new enrollment rotates the token.</summary>
    StoreFailed = 10
End Enum

Friend NotInheritable Class ExchangeEnrollResult

    Public ReadOnly Outcome As ExchangeEnrollOutcome
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>The certificate fingerprint seen on the connection ("" when the handshake did not get that far).</summary>
    Public ReadOnly Fingerprint As String

    Public Sub New(outcome As ExchangeEnrollOutcome, refusal As ExchangeRefusal, fingerprint As String)
        Me.Outcome = outcome
        Me.Refusal = refusal
        Me.Fingerprint = If(fingerprint, "")
    End Sub

End Class

''' <summary>What the user typed. The password and the pairing code are cleared by the agent as soon as the frame is built.</summary>
Friend NotInheritable Class ExchangeEnrollRequest

    Public Property Server As String = ""
    Public Property Login As String = ""
    Public Property Password As String = ""
    Public Property PairingCode As String = ""
    ''' <summary>What the first-contact probe found: the platform validated the certificate.</summary>
    Public Property PlatformValid As Boolean
    ''' <summary>The fingerprint the user accepted (only meaningful when the platform did not validate the certificate).</summary>
    Public Property AcceptedFingerprint As String = ""
    Public Property DeviceName As String = ""

    ''' <summary>Overwrites the secrets.</summary>
    Public Sub Wipe()
        Password = ""
        PairingCode = ""
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeEnrollRequest"
    End Function

End Class

''' <summary>What the user gave to REGISTER (226 rule 1): the invitation code of the string (or the operator's shared invitation
''' password) and the login and password the person chose. The secrets are cleared by the agent as soon as the frame is built;</summary>
Friend NotInheritable Class ExchangeRegisterRequest

    Public Property Server As String = ""
    Public Property Login As String = ""
    Public Property Password As String = ""
    ''' <summary>The one-time code the invitation string carried; "" when the person typed the shared password instead.</summary>
    Public Property InvitationCode As String = ""
    ''' <summary>The operator's shared invitation password; "" when a one-time code is used.</summary>
    Public Property SharedPassword As String = ""
    Public Property PlatformValid As Boolean
    Public Property AcceptedFingerprint As String = ""
    Public Property DeviceName As String = ""

    Public Sub Wipe()
        Password = ""
        InvitationCode = ""
        SharedPassword = ""
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeRegisterRequest"
    End Function

End Class

''' <summary>What the settings window and the viewer's state request need to know, in one immutable value.</summary>
Friend NotInheritable Class ExchangeDeviceStatus

    Public ReadOnly Configured As Boolean
    Public ReadOnly State As ExchangeAgentState
    Public ReadOnly LastReason As String
    Public ReadOnly Features As IReadOnlyList(Of String)
    Public ReadOnly DeviceId As String
    Public ReadOnly DeviceName As String
    Public ReadOnly Login As String
    Public ReadOnly Server As String
    Public ReadOnly Certificate As ExchangeCertificateKind
    Public ReadOnly Fingerprint As String
    ''' <summary>The `publicEndpoint` of the last `welcome` ("" while there is no stream): the address the server says its clients should
    ''' use - ask 2 of the entries proposal, shown beside the entry's own address when they differ.</summary>
    Public ReadOnly PublicEndpoint As String

    Public Sub New(configured As Boolean, state As ExchangeAgentState, lastReason As String, features As IReadOnlyList(Of String),
                   deviceId As String, deviceName As String, login As String, server As String, certificate As ExchangeCertificateKind, fingerprint As String,
                   Optional publicEndpoint As String = "")
        Me.Configured = configured
        Me.State = state
        Me.LastReason = lastReason
        Me.Features = features
        Me.DeviceId = deviceId
        Me.DeviceName = deviceName
        Me.Login = login
        Me.Server = server
        Me.Certificate = certificate
        Me.Fingerprint = fingerprint
        Me.PublicEndpoint = If(publicEndpoint, "")
    End Sub

    Public Function HasFeature(name As String) As Boolean
        For Each f As String In Features
            If String.Equals(f, name, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

End Class

''' <summary>What the local channel asks of the agent. One dispatch, so the channel stays a transport.</summary>
Friend Interface IExchangeAgentHandler
    Function HandleAsync(requestType As String, request As JsonElement, ct As CancellationToken) As Task(Of ExchangeLocalResult)
End Interface

Friend NotInheritable Class ExchangeLocalResult
    Public ReadOnly Ok As Boolean
    Public ReadOnly [Error] As String
    Public ReadOnly DataJson As String

    Public Sub New(ok As Boolean, [error] As String, dataJson As String)
        Me.Ok = ok
        Me.Error = If([error], "")
        Me.DataJson = If(dataJson, "")
    End Sub

    Public Shared Function Failure(code As String) As ExchangeLocalResult
        Return New ExchangeLocalResult(False, code, "")
    End Function

    Public Shared Function Success(dataJson As String) As ExchangeLocalResult
        Return New ExchangeLocalResult(True, "", dataJson)
    End Function
End Class

Partial Friend NotInheritable Class ExchangeDeviceAgent
    Implements IExchangeSessionHooks
    Implements IExchangeAgentHandler
    Implements IDisposable

    Private ReadOnly _ports As ExchangeAgentPorts
    Private ReadOnly _gate As New Object()
    Private _session As ExchangeControlSession
    Private _castOffer As String
    Private _castId As String
    Private _castSequence As Long
    Private _castPlayingId As String
    Private _castStopId As String
    Private _viewerSeenMs As Long = Long.MinValue
    Friend Property CastAnswerTimeoutMs As Integer = 55000
    Private _cts As CancellationTokenSource
    Private _runTask As Task
    Private _channel As IDisposable
    Private _idleState As ExchangeAgentState = ExchangeAgentState.Stopped
    Private _idleReason As String = ""
    Private _changeWaiters As New List(Of TaskCompletionSource(Of Boolean))
    Private _disposed As Boolean
    Private _sealedTrust As IExchangeSealedTrustStore

    ' Live broadcast state (spec 199 section 3.3)
    Private _liveBroadcastRecordJson As String
    Private _liveBroadcastId As String
    Private _liveBroadcastState As LiveProducerState
    ' Ticket 263 T-6: the id of the live broadcast whose `broadcast-start` is sent and not yet answered (Nothing when none is
    ' awaiting). Set right before a start goes out (the publish path and the one after a reconnect), cleared by `broadcast-started`
    ' for that id, by a `refused` (which is then THAT start's refusal), by the broadcast's end and by a new welcome. Guarded by _gate.
    Private _startAwaitingId As String
    Private _liveUploadCts As CancellationTokenSource
    ' Sealed relay (spec 234 T-3, items DK and DL): the keys and epochs of the running broadcast, and the keys of one about to start.
    Private _liveSealed As ExchangeSealedBroadcastSession
    Private ReadOnly _pendingBroadcastKeys As New Dictionary(Of String, ExchangeBroadcastKeys)(StringComparer.Ordinal)
    Private _liveSealedPublisher As ExchangeSealedBroadcastPublisher
    ' Inner TLS of the sealed TUNNEL (item DM, ticket 234 E1b): ONE identity per broadcast, made when its outward descriptor is built
    ' (the pin goes into it), adopted when the broadcast starts, wiped when it stops. The private key lives in memory only.
    Private _liveTunnel As ExchangeSealedTunnel
    Private _liveTunnelBroadcastId As String
    Private ReadOnly _pendingTunnels As New Dictionary(Of String, ExchangeSealedTunnel)(StringComparer.Ordinal)
    Private ReadOnly _castPendingLock As New Object()
    Private ReadOnly _pendingCasts As New Dictionary(Of String, TaskCompletionSource(Of Tuple(Of Boolean, String)))(StringComparer.Ordinal)

    ''' <summary>The state or its reason changed (any thread).</summary>
    Public Event StatusChanged As EventHandler
    ''' <summary>The mirrored directory moved a revision (any thread).</summary>
    Public Event DirectoryChanged As EventHandler
    ''' <summary>An envelope the agent does not handle itself arrived on the control stream: `open` (170), `cast-offer` (174), ..</summary>
    Public Event ServerMessage As Action(Of ExchangeEnvelope)
    ''' <summary>Ticket 263 T-6: the server refused the `broadcast-start` of the live broadcast with this id (any thread). The reason is the
    ''' typed `refused.reason` and nothing else: never the record, the descriptor or a key. A `refused` that arrives while no start is
    ''' awaiting its answer is not attributed to the broadcast and raises nothing.</summary>
    Public Event BroadcastStartRefused As Action(Of String, ExchangeRefusal)

    Public Sub New(ports As ExchangeAgentPorts)
        If ports Is Nothing Then Throw New ArgumentNullException(NameOf(ports))
        _ports = ports
        Interlocked.Increment(_constructed)
    End Sub

    ''' <summary>Counts constructions, so a test can assert that a dormant start built none.</summary>
    Public Shared ReadOnly Property Constructed As Integer
        Get
            Return Volatile.Read(_constructed)
        End Get
    End Property

    Private Shared _constructed As Integer

    ' --- start and stop ---

    ''' <summary>Starts what the stored account says: no account - nothing; an account without a token - "sign in again"; both - the
    ''' control stream and the local channel. Called once from the Share Manager's start.</summary>
    Public Sub Start()
        If _disposed Then Return
        Dim account As ExchangeAccount = _ports.Accounts.Load()
        If account Is Nothing Then
            SetIdle(ExchangeAgentState.NotConfigured, "")
            Return
        End If
        Dim token As String = ""
        If Not _ports.Credentials.TryLoad(account.DeviceId, token) Then
            _ports.Log.Invoke("Exchange agent: an account is configured but there is no usable token - sign in again")
            SetIdle(ExchangeAgentState.SignInAgain, "no-token")
            Return
        End If
        Dim address As ExchangeServerAddress = Nothing
        If Not account.TryGetAddress(address) Then
            _ports.Log.Invoke("Exchange agent: the stored server address does not parse")
            SetIdle(ExchangeAgentState.SignInAgain, "bad-address")
            Return
        End If
        StartSession(account, address, New ExchangeDeviceCredentials(account.DeviceId, token), Nothing)
    End Sub

    ' --- the device key and the kept keys of the sealed scheme (spec 234, items DF and DG) ---

    Private Function KeyStore() As IExchangeDeviceKeyStore
        Return If(_ports.DeviceKeys, New ExchangeDeviceKeyStore(_ports.Credentials.FolderPath, Nothing))
    End Function

    ''' <summary>One object for the agent's life: the file store caches what it read, and two of them over one file would disagree.</summary>
    Private Function TrustStore() As IExchangeSealedTrustStore
        SyncLock _gate
            If _sealedTrust Is Nothing Then _sealedTrust = If(_ports.SealedTrust, New ExchangeSealedTrustStore(_ports.Credentials.FolderPath))
            Return _sealedTrust
        End SyncLock
    End Function

    ''' <summary>The key of the id before `enroll` or `register` (item DF): the one kept for it, else a new one kept first. Returns its
    ''' public `kem`; "" when no key could be kept - then nothing is sent, because a key that is not stored would be lost at the next
    ''' start and the device would have to enroll anew. <paramref name="created"/> tells the caller to take a new key back when the
    ''' enrollment does not succeed.</summary>
    Private Function PrepareDeviceKey(deviceId As String, ByRef created As Boolean) As String
        Dim store As IExchangeDeviceKeyStore = KeyStore()
        created = Not store.Exists(deviceId)
        Using key As ExchangeDeviceKey = ExchangeDeviceKeys.LoadOrCreate(store, deviceId)
            If key Is Nothing Then
                created = False
                _ports.Log.Invoke("Exchange agent: the device key could not be kept on this computer - nothing was sent")
                Return ""
            End If
            Return key.Kem
        End Using
    End Function

    ''' <summary>A failed enrollment leaves nothing behind: a key made for it is dropped. A key that was there before is the device's.</summary>
    Private Sub DropFreshKeyOnFailure(deviceId As String, created As Boolean, result As ExchangeEnrollResult)
        If created AndAlso result.Outcome <> ExchangeEnrollOutcome.Enrolled Then KeyStore().Delete(deviceId)
    End Sub

    ''' <summary>The `kem` of this device's own key, "" when it has none (a device enrolled before it had one). A pure read.</summary>
    Private Function OwnKemOf(deviceId As String) As String
        Using key As ExchangeDeviceKey = KeyStore().TryLoad(deviceId)
            Return If(key Is Nothing, "", key.Kem)
        End Using
    End Function

    ''' <summary>Who this account may seal to and the state of each device (spec 234, items DG, DP, DR). Nothing while no session runs.</summary>
    Public Function GetSealedRecipients() As ExchangeSealedRecipients
        SyncLock _gate
            Return If(_session Is Nothing, Nothing, _session.Sealed)
        End SyncLock
    End Function

    Private Sub StartSession(account As ExchangeAccount, address As ExchangeServerAddress, creds As ExchangeDeviceCredentials, adopted As ExchangeOpenResult)
        StopSessionOnly()
        Dim options As New ExchangeControlOptions With {
            .Server = address, .Pin = account.Pin, .Credentials = creds, .ProductVersion = _ports.ProductVersion,
            .Transport = _ports.Accounts.TransportFor(account, _ports.Transport), .Log = _ports.Log,
            .OwnKem = OwnKemOf(creds.DeviceId), .SealedTrust = TrustStore(), .OwnKey = Function() KeyStore().TryLoad(creds.DeviceId)}
        _ports.ConfigureSession.Invoke(options)
        Dim session As New ExchangeControlSession(options, Me)
        AddHandler session.StateChanged, AddressOf OnSessionStateChanged
        AddHandler session.DirectoryChanged, AddressOf OnSessionDirectoryChanged
        AddHandler session.Sealed.Changed, AddressOf OnSealedRecipientsChanged
        AddHandler session.DeviceRevoked, AddressOf OnSessionDeviceRevoked
        Dim cts As New CancellationTokenSource()
        SyncLock _gate
            _session = session
            _cts = cts
        End SyncLock
        If _channel Is Nothing Then
            Try
                _channel = _ports.StartLocalChannel.Invoke(Me)
            Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is InvalidOperationException
                _ports.Log.Invoke("Exchange agent: the local channel could not be created - " & ex.GetType().Name)
            End Try
        End If
        Dim certificateNote As String = If(account.Certificate = ExchangeCertificateKind.Pinned, "pinned", "validated by the system")
        _ports.Log.Invoke("Exchange agent: starting, device " & ExchangeIds.ForLog(creds.DeviceId) & ", server certificate " & certificateNote)
        _runTask = Task.Run(Function() session.RunAsync(cts.Token, adopted))
    End Sub

    Private Sub StopSessionOnly()
        Dim session As ExchangeControlSession
        Dim cts As CancellationTokenSource
        Dim run As Task
        SyncLock _gate
            session = _session
            cts = _cts
            run = _runTask
            _session = Nothing
            _castId = Nothing
            _castOffer = Nothing
            _castPlayingId = Nothing
            _castStopId = Nothing
            _cts = Nothing
            _runTask = Nothing
        End SyncLock
        If cts IsNot Nothing Then
            Try : cts.Cancel() : Catch : End Try
        End If
        If run IsNot Nothing Then
            Try
                run.Wait(3000)
            Catch
            End Try
        End If
        If session IsNot Nothing Then
            RemoveHandler session.StateChanged, AddressOf OnSessionStateChanged
            RemoveHandler session.DirectoryChanged, AddressOf OnSessionDirectoryChanged
            RemoveHandler session.Sealed.Changed, AddressOf OnSealedRecipientsChanged
            RemoveHandler session.DeviceRevoked, AddressOf OnSessionDeviceRevoked
        End If
        If cts IsNot Nothing Then
            Try : cts.Dispose() : Catch : End Try
        End If
        WakeWaiters()
    End Sub

    ''' <summary>A clean exit of the Share Manager: `bye` (presence goes offline at once), the stream and the channel closed.</summary>
    Public Async Function StopAsync() As Task
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session IsNot Nothing Then
            Try
                Await session.SayByeAsync().ConfigureAwait(False)
            Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IO.IOException
            End Try
        End If
        Await Task.Run(Sub() StopSessionOnly()).ConfigureAwait(False)
        DisposeChannel()
    End Function

    Private Sub DisposeChannel()
        Dim c As IDisposable = _channel
        _channel = Nothing
        If c IsNot Nothing Then
            Try : c.Dispose() : Catch : End Try
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        StopSessionOnly()
        DisposeChannel()
        Dim liveId As String
        SyncLock _gate
            liveId = _liveBroadcastId
        End SyncLock
        DisposeSealedBroadcast(liveId)
    End Sub

    ' --- state ---

    Private Sub SetIdle(state As ExchangeAgentState, reason As String)
        SyncLock _gate
            _idleState = state
            _idleReason = reason
        End SyncLock
        RaiseEvent StatusChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OnSessionStateChanged(sender As Object, e As EventArgs)
        Interlocked.Increment(_guestGeneration)
        Dim s As ExchangeControlSession = TryCast(sender, ExchangeControlSession)
        If s IsNot Nothing Then _ports.Log.Invoke("Exchange agent: " & s.State.ToString() & If(s.LastReason.Length > 0, " (" & s.LastReason & ")", ""))
        RaiseEvent StatusChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OnSessionDirectoryChanged(sender As Object, e As EventArgs)
        WakeWaiters()
        RaiseEvent DirectoryChanged(Me, EventArgs.Empty)
        Dim refresh As Task = RefreshGuestGrantsAsync()
    End Sub

    Private Sub OnSessionDeviceRevoked(sender As Object, e As EventArgs)
        ' The server no longer knows this device: its token is useless. Discarded here; the account entry stays so the window
        ' can say "sign in again" with the server and login already known.
        _ports.Credentials.Delete()
        _ports.Log.Invoke("Exchange agent: this device was revoked - the token was discarded")
    End Sub

    ''' <summary>The current status.</summary>
    Public Function GetStatus() As ExchangeDeviceStatus
        Dim account As ExchangeAccount = _ports.Accounts.Load()
        Dim session As ExchangeControlSession
        Dim idleState As ExchangeAgentState
        Dim idleReason As String
        SyncLock _gate
            session = _session
            idleState = _idleState
            idleReason = _idleReason
        End SyncLock
        If account Is Nothing Then
            Return New ExchangeDeviceStatus(False, ExchangeAgentState.NotConfigured, "", Array.Empty(Of String)(), "", "", "", "", ExchangeCertificateKind.None, "")
        End If
        Dim state As ExchangeAgentState = idleState
        Dim reason As String = idleReason
        Dim features As IReadOnlyList(Of String) = Array.Empty(Of String)()
        Dim announced As String = ""
        If session IsNot Nothing Then
            state = session.State
            reason = session.LastReason
            features = session.Features
            announced = session.PublicEndpoint
        End If
        Return New ExchangeDeviceStatus(True, state, reason, features, account.DeviceId, account.DeviceName, account.Login, account.Server, account.Certificate, account.Pin, announced)
    End Function

    ''' <summary>The directory without this device's own records.</summary>
    Public Function GetDirectory() As ExchangeDirectorySnapshot
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return ExchangeDirectorySnapshot.Empty
        Dim account As ExchangeAccount = _ports.Accounts.Load()
        Return session.Model.Snapshot().WithoutDevice(If(account Is Nothing, "", account.DeviceId))
    End Function

    ''' <summary>The whole directory, this device included (the window marks it as this device).</summary>
    Public Function GetFullDirectory() As ExchangeDirectorySnapshot
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return ExchangeDirectorySnapshot.Empty
        Return session.Model.Snapshot()
    End Function

    ' --- enrollment ---

    ''' <summary>First contact: what certificate does this server show? No byte of envelope is sent. The fingerprint is shown to the user
    ''' beside "compare this with the one the operator gave you"; only an explicit accept pins it (item D).</summary>
    Public Async Function ProbeCertificateAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(server, address) Then Throw New ArgumentException("server")
        If TypeOf _ports.Transport Is ExchangeTlsTransport Then
            Return Await ExchangeTlsTransport.ProbeAsync(address.Host, address.Port, ct).ConfigureAwait(False)
        End If
        ' an injected transport (a test): connect with no pin and report what it showed
        Using conn As ExchangeTlsConnection = Await _ports.Transport.ConnectAsync(address.Host, address.Port, "", ct).ConfigureAwait(False)
            Return New ExchangeCertificateInfo(conn.Fingerprint, conn.Verdict = ExchangeTrustVerdict.PlatformValid)
        End Using
    End Function

    ''' <summary>226 rule 1: registers this PC by invitation - `register` with the one-time code (or the shared invitation password), the
    ''' chosen login and password, then `enrolled`. The completion is a sign-in's own: the token is stored under DPAPI, the account entry is
    ''' written, the control stream stays open and the local channel starts. The request's secrets are wiped whatever happens.</summary>
    Public Async Function RegisterAsync(request As ExchangeRegisterRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult)
        Dim keyDeviceId As String = ""
        Dim keyCreated As Boolean = False
        Dim keyKept As Boolean = False
        Try
            Dim address As ExchangeServerAddress = Nothing
            Dim login As String = request.Login.Trim()
            Dim hasCode As Boolean = request.InvitationCode.Trim().Length > 0
            Dim hasShared As Boolean = request.SharedPassword.Length > 0
            If Not ExchangeServerAddress.TryParse(request.Server, address) OrElse login.Length = 0 OrElse
               request.Password.Length = 0 OrElse hasCode = hasShared Then
                Return New ExchangeEnrollResult(ExchangeEnrollOutcome.BadInput, ExchangeRefusal.Unknown, "")
            End If
            Dim pin As String = If(request.PlatformValid, "", request.AcceptedFingerprint)
            If Not request.PlatformValid AndAlso Not ExchangeTrust.IsFingerprint(pin) Then
                Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, "")
            End If

            Dim deviceId As String = _ports.Accounts.ProposeDeviceId()
            keyDeviceId = deviceId
            Dim kem As String = PrepareDeviceKey(deviceId, keyCreated)
            If kem.Length = 0 Then Return New ExchangeEnrollResult(ExchangeEnrollOutcome.StoreFailed, ExchangeRefusal.Unknown, "")
            Dim name As String = ExchangeDeviceDescription.Clip(If(request.DeviceName.Trim().Length > 0, request.DeviceName, _ports.MachineName.Invoke()), 64)
            If name.Length = 0 Then name = "Windows PC"
            Dim description As New ExchangeDeviceDescription(deviceId, name, ExchangeWire.ProductWindows,
                ExchangeDeviceDescription.Clip(_ports.ProductVersion, 64), ExchangeDeviceDescription.PlatformText(),
                ExchangeRoles.Compute(_ports.RoleFacts), If(_ports.RoleFacts.CanReceive(), ExchangeAgentPipe.ReceiverJson, ""), kem)
            Dim code As String = request.InvitationCode.Trim()
            Dim sharedPw As String = request.SharedPassword
            Dim password As String = request.Password
            request.Wipe()
            Dim register As ExchangeEnvelope = ExchangeMessages.Register(code, sharedPw, login, password, description, ExchangeWire.ProposedKeepaliveSeconds)
            Dim opened As ExchangeOpenResult
            Try
                ' the answer of `register` is `enrolled`, the same frame a sign-in answers with (224 rule 6)
                opened = Await ExchangeSessions.EnrollAsync(_ports.Transport, address, pin, register, ct).ConfigureAwait(False)
            Finally
                register.SetText("password", "").SetText("invitation", "").SetText("invitationPassword", "")
            End Try

            Select Case opened.Outcome
                Case ExchangeOpenOutcome.Connected
                    Dim shim As New ExchangeEnrollRequest With {.PlatformValid = request.PlatformValid}
                    ' keyKept is set inside, at the moment the account is saved: whatever fails after that, the key stays with the account
                    Dim completed As ExchangeEnrollResult = CompleteEnrollment(opened, address, login, shim, description, pin, keyKept)
                    Return completed
                Case ExchangeOpenOutcome.Refused
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, opened.Refusal, opened.Fingerprint)
                Case ExchangeOpenOutcome.PinMismatch
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.PinMismatch, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.Untrusted
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.TlsFailed
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.TlsFailed, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.Network
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Network, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.TimedOut
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.TimedOut, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.Cancelled
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Cancelled, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case Else
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Protocol, ExchangeRefusal.Unknown, opened.Fingerprint)
            End Select
        Finally
            request.Wipe()
            ' a failed enrollment leaves nothing behind (a key that was there before is the device's own and stays)
            If keyCreated AndAlso Not keyKept Then KeyStore().Delete(keyDeviceId)
        End Try
    End Function
    ''' <summary>Enrolls this PC: `enroll` with the password OR a pairing code, then `enrolled`. On success the token is stored under DPAPI, the
    ''' account entry is written, the control stream stays open (the very stream that enrolled becomes the control stream) and the local
    ''' channel starts. The request's secrets are wiped whatever happens.</summary>
    Public Async Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult)
        Dim keyDeviceId As String = ""
        Dim keyCreated As Boolean = False
        Dim keyKept As Boolean = False
        Try
            Dim address As ExchangeServerAddress = Nothing
            Dim login As String = request.Login.Trim()
            Dim hasPassword As Boolean = request.Password.Length > 0
            Dim hasCode As Boolean = request.PairingCode.Trim().Length > 0
            If Not ExchangeServerAddress.TryParse(request.Server, address) OrElse login.Length = 0 OrElse hasPassword = hasCode Then
                Return New ExchangeEnrollResult(ExchangeEnrollOutcome.BadInput, ExchangeRefusal.Unknown, "")
            End If
            Dim pin As String = If(request.PlatformValid, "", request.AcceptedFingerprint)
            If Not request.PlatformValid AndAlso Not ExchangeTrust.IsFingerprint(pin) Then
                Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, "")
            End If

            ' a failed enrollment leaves nothing behind: the id is stored with the account entry on success
            Dim deviceId As String = _ports.Accounts.ProposeDeviceId()
            keyDeviceId = deviceId
            Dim kem As String = PrepareDeviceKey(deviceId, keyCreated)
            If kem.Length = 0 Then Return New ExchangeEnrollResult(ExchangeEnrollOutcome.StoreFailed, ExchangeRefusal.Unknown, "")
            Dim name As String = ExchangeDeviceDescription.Clip(If(request.DeviceName.Trim().Length > 0, request.DeviceName, _ports.MachineName.Invoke()), 64)
            If name.Length = 0 Then name = "Windows PC"
            Dim description As New ExchangeDeviceDescription(deviceId, name, ExchangeWire.ProductWindows,
                ExchangeDeviceDescription.Clip(_ports.ProductVersion, 64), ExchangeDeviceDescription.PlatformText(),
                ExchangeRoles.Compute(_ports.RoleFacts), If(_ports.RoleFacts.CanReceive(), ExchangeAgentPipe.ReceiverJson, ""), kem)
            Dim enroll As ExchangeEnvelope = ExchangeMessages.Enroll(login, request.Password, request.PairingCode.Trim().ToUpperInvariant(), description,
                                                                    ExchangeWire.ProposedKeepaliveSeconds)
            Dim opened As ExchangeOpenResult
            Try
                opened = Await ExchangeSessions.EnrollAsync(_ports.Transport, address, pin, enroll, ct).ConfigureAwait(False)
            Finally
                ' the frame's buffer was overwritten in the writer; the envelope's copy of the secrets goes too
                enroll.SetText("password", "").SetText("pairingCode", "")
                request.Wipe()
            End Try

            Select Case opened.Outcome
                Case ExchangeOpenOutcome.Connected
                    ' keyKept is set inside, at the moment the account is saved: whatever fails after that, the key stays with the account
                    Dim completed As ExchangeEnrollResult = CompleteEnrollment(opened, address, login, request, description, pin, keyKept)
                    Return completed
                Case ExchangeOpenOutcome.Refused
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, opened.Refusal, opened.Fingerprint)
                Case ExchangeOpenOutcome.PinMismatch
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.PinMismatch, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.Untrusted
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.TlsFailed
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.TlsFailed, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.Network
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Network, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.TimedOut
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.TimedOut, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case ExchangeOpenOutcome.Cancelled
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Cancelled, ExchangeRefusal.Unknown, opened.Fingerprint)
                Case Else
                    Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Protocol, ExchangeRefusal.Unknown, opened.Fingerprint)
            End Select
        Finally
            request.Wipe()
            ' a failed enrollment leaves nothing behind (a key that was there before is the device's own and stays)
            If keyCreated AndAlso Not keyKept Then KeyStore().Delete(keyDeviceId)
        End Try
    End Function

    Private Function CompleteEnrollment(opened As ExchangeOpenResult, address As ExchangeServerAddress, login As String,
                                        request As ExchangeEnrollRequest, description As ExchangeDeviceDescription, pin As String,
                                        ByRef keyKept As Boolean) As ExchangeEnrollResult
        Dim token As String = opened.Welcome.Text("deviceToken")
        If Not ExchangeIds.IsValidToken(token) Then
            opened.Dispose()
            Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Protocol, ExchangeRefusal.Unknown, opened.Fingerprint)
        End If
        If Not _ports.Credentials.Save(description.DeviceId, token) Then
            opened.Dispose()
            _ports.Log.Invoke("Exchange agent: enrolled, but the token could not be stored under DPAPI")
            Return New ExchangeEnrollResult(ExchangeEnrollOutcome.StoreFailed, ExchangeRefusal.Unknown, opened.Fingerprint)
        End If
        Dim kind As ExchangeCertificateKind = If(request.PlatformValid, ExchangeCertificateKind.CaSigned, ExchangeCertificateKind.Pinned)
        Dim account As New ExchangeAccount(address.ToString(), login, pin, kind, description.DeviceId, description.DeviceName, If(request.PlatformValid, "", opened.KeyPin))
        _ports.Accounts.Save(account)
        ' The device is configured from here on, so its key is the account's: a failure of the session start below must not take the
        ' key back (the device could never open sealed content again). Only an enrollment that never got this far drops a fresh key.
        keyKept = True
        _ports.Log.Invoke("Exchange agent: enrolled as device " & ExchangeIds.ForLog(description.DeviceId))
        StartSession(account, address, New ExchangeDeviceCredentials(description.DeviceId, token), opened)
        Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Enrolled, ExchangeRefusal.Unknown, opened.Fingerprint)
    End Function

    ' --- pairing, devices, revoke, leave ---

    ''' <summary>A one-time code for the user's other device (5.3). Not answered when the stream is down.</summary>
    Public Function RequestPairingCodeAsync(ct As CancellationToken) As Task(Of ExchangePairingResult)
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return Task.FromResult(New ExchangePairingResult(False, "", "", ExchangeRefusal.Unknown, False))
        Return session.RequestPairingCodeAsync(ct)
    End Function

    ''' <summary>226 rule 4: change the account's password over this device's own control stream. On success the fresh key of the answer
    ''' is stored and the stream restarts with it; the account's OTHER devices are signed out by the server (224 rule 9) - the window's
    ''' sentence says so. A wrong old password is the common refusal, and nothing is retried on its own (218).</summary>
    Public Async Function ChangePasswordAsync(oldPassword As String, newPassword As String, ct As CancellationToken) As Task(Of ExchangePasswordChangeResult)
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return New ExchangePasswordChangeResult(ExchangePasswordChangeOutcome.NoStream, ExchangeRefusal.Unknown, "")
        Dim result As ExchangePasswordChangeResult = Await session.ChangePasswordAsync(oldPassword, newPassword, ct).ConfigureAwait(False)
        If result.Outcome = ExchangePasswordChangeOutcome.Changed Then
            Dim account As ExchangeAccount = _ports.Accounts.Load()
            If account Is Nothing OrElse Not _ports.Credentials.Save(account.DeviceId, result.FreshToken) Then
                _ports.Log.Invoke("Exchange agent: this account's key changed, but the new one could not be stored on this computer")
                Return New ExchangePasswordChangeResult(ExchangePasswordChangeOutcome.NoStream, ExchangeRefusal.Unknown, "")
            End If
            _ports.Log.Invoke("Exchange agent: this account's key was changed - the other devices sign in again")
            StopSessionOnly()
            Start()
        End If
        Return result
    End Function
    ''' <summary>Revokes another device of the account. Revoking THIS device is leaving the account (<see cref="LeaveAccountAsync"/>).</summary>
    Public Async Function RevokeAsync(deviceId As String, ct As CancellationToken) As Task(Of Boolean)
        Dim account As ExchangeAccount = _ports.Accounts.Load()
        If account IsNot Nothing AndAlso String.Equals(account.DeviceId, deviceId, StringComparison.Ordinal) Then
            Return Await LeaveAccountAsync(ct).ConfigureAwait(False)
        End If
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return False
        Return Await session.RevokeAsync(deviceId, ct).ConfigureAwait(False)
    End Function

    ''' <summary>Leaves the account: this device is revoked on the server, then the token is discarded, the stream closed and the entry
    ''' forgotten. False when the server could not be told (the local forgetting still happens, and the window says so).</summary>
    Public Async Function LeaveAccountAsync(ct As CancellationToken) As Task(Of Boolean)
        Dim account As ExchangeAccount = _ports.Accounts.Load()
        Dim told As Boolean = False
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If account IsNot Nothing AndAlso session IsNot Nothing Then
            ' ticket 187 rule 9, in this order and each step best effort: every resource is unpublished, then the device revokes ITSELF on the
            ' live stream (that removes its records and closes its streams, so a `bye` after it has no stream to ride), then it forgets
            ' locally. Nothing here waits for the server beyond a short, bounded step: a server that is down costs a second or two, not a hang.
            Dim leave As IExchangeProducerLeave = TryCast(_ports.Producer, IExchangeProducerLeave)
            Try
                Using bounded As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    bounded.CancelAfter(3000)
                    If leave IsNot Nothing Then Await leave.UnpublishAllAsync(session, bounded.Token).ConfigureAwait(False)
                    told = Await session.RevokeAsync(account.DeviceId, bounded.Token).ConfigureAwait(False)
                End Using
            Catch ex As OperationCanceledException
                ' the time ran out or the caller cancelled: the leaving goes on locally
            End Try
            ' give the frame a moment to leave before the stream is closed
            Await Task.Delay(150, CancellationToken.None).ConfigureAwait(False)
        End If
        Await DisconnectAsync().ConfigureAwait(False)
        Return told
    End Function

    ''' <summary>"Disconnect": forget the token and the account entry, close the stream and the channel; keep nothing. The device record stays on the
    ''' server (offline) until another device of the account revokes it.</summary>
    Public Async Function DisconnectAsync() As Task
        Await StopAsync().ConfigureAwait(False)
        ' the kept keys of the account's devices go with the account (item DG); this device's own key lives as long as its id, which
        ' stays, so a later sign-in under the same id carries the same key (item DF)
        Dim leaving As ExchangeAccount = _ports.Accounts.Load()
        If leaving IsNot Nothing Then TrustStore().ForgetScope(leaving.DeviceId)
        _ports.Credentials.Delete()
        _ports.Accounts.Forget()
        ' "keep nothing" has one exception: that the person chose to leave, so the local join does not undo it at the next start
        _ports.Accounts.SetLocalJoinDeclined(True)
        ' the ids, the third-party port and both choices go with the account: a later enrollment publishes nothing until asked (170 rule 1)
        _ports.Producer?.OnAccountForgotten()
        SetIdle(ExchangeAgentState.NotConfigured, "")
        _ports.Log.Invoke("Exchange agent: disconnected, nothing kept")
    End Function

    ''' <summary>The user replaced the pin after seeing the new fingerprint: the one explicit act that lets a changed certificate in (item D).
    ''' The stream restarts with the new pin.</summary>
    Public Sub ReplacePinAndRestart(fingerprint As String, platformValid As Boolean)
        _ports.Accounts.ReplacePin(If(platformValid, "", fingerprint), If(platformValid, ExchangeCertificateKind.CaSigned, ExchangeCertificateKind.Pinned))
        StopSessionOnly()
        Start()
    End Sub

    ''' <summary>Ask 2 of the entries proposal: the operator moved the server and the welcome announced the new address. The edit is in
    ''' place - the device id, the token, the pin decision and the published shares are kept (the producer store is keyed by the device id);
    ''' only the stored address changes and the stream restarts with it. Nothing happens when the value is not an address or no account is
    ''' configured. A different leaf at the new address is item D's explicit replacement, as everywhere.</summary>
    Public Sub ReplaceServerAndRestart(server As String)
        Dim account As ExchangeAccount = _ports.Accounts.Load()
        If account Is Nothing Then Return
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(server, address) Then Return
        _ports.Accounts.Save(New ExchangeAccount(New ExchangeServerAddress(address.Host.ToLowerInvariant(), address.Port).ToString(),
                                                 account.Login, account.Pin, account.Certificate, account.DeviceId, account.DeviceName, account.KeyPin))
        _ports.Log.Invoke("Exchange agent: the stored server address was updated after a welcome named another one")
        StopSessionOnly()
        Start()
    End Sub

    ' --- hooks of the control session ---

    Public Async Function OnWelcomeAsync(session As ExchangeControlSession, ct As CancellationToken) As Task Implements IExchangeSessionHooks.OnWelcomeAsync
        Dim activeRecord As String = Nothing
        Dim activeState As LiveProducerState = Nothing
        Dim activeId As String = Nothing
        SyncLock _gate
            _castId = Nothing
            _castOffer = Nothing
            activeRecord = _liveBroadcastRecordJson
            activeState = _liveBroadcastState
            activeId = _liveBroadcastId
            ' A start sent on the stream that just ended is answered never; the one below is the awaited one.
            _startAwaitingId = Nothing
        End SyncLock
        ' Spec 200 rule 6: After a reconnect the agent sends broadcast-start again with the same id
        If Not String.IsNullOrEmpty(activeRecord) Then
            Try
                ' Item DV: the verifiers go out again with it - a restarted server holds none, and a start without the member reads as
                ' "no pair to check". The list is the set as it is NOW (a pair revoked meanwhile stays gone); the hashes are cached.
                Dim verifiers As IReadOnlyList(Of String) = Await VerifiersOfAsync(activeState).ConfigureAwait(False)
                Dim startSent As Boolean = Await SendBroadcastStartAsync(session, activeId, activeRecord, verifiers, ct).ConfigureAwait(False)
                ' Item DL: the record (with its current box) is out, so a replacement waiting for it may follow.
                If startSent Then CurrentSealed()?.MarkPublished()
            Catch
            End Try
        End If
        ' The republish of this device's resources (170) plugs in here; the broadcasts (173) join it. A device with no producer
        ' (the default of the ports) publishes nothing.
        Dim producer As IExchangeProducerHooks = _ports.Producer
        If producer IsNot Nothing Then Await producer.OnWelcomeAsync(session, ct).ConfigureAwait(False)
    End Function

    Public Sub OnMessage(session As ExchangeControlSession, message As ExchangeEnvelope) Implements IExchangeSessionHooks.OnMessage
        ' Ticket 263 T-6 (DEVICE-EXCHANGE 7.4, item DT): the answer to the live broadcast's `broadcast-start`. `broadcast-started` closes
        ' the wait; a `refused` that reaches this hook (no request/answer waiter took it) while a start is awaiting is that start's
        ' refusal. With no start awaiting a `refused` belongs to something else and falls through exactly as before.
        If message IsNot Nothing AndAlso message.Is(ExchangeEnvelopeTypes.BroadcastStarted) Then
            Dim startedId As String = message.Text("broadcastId")
            SyncLock _gate
                If String.Equals(_startAwaitingId, startedId, StringComparison.Ordinal) Then _startAwaitingId = Nothing
            End SyncLock
            Return
        End If
        If message IsNot Nothing AndAlso message.Is(ExchangeEnvelopeTypes.Refused) Then
            Dim refusedId As String
            SyncLock _gate
                refusedId = _startAwaitingId
                _startAwaitingId = Nothing
            End SyncLock
            If Not String.IsNullOrEmpty(refusedId) Then
                Try
                    RaiseEvent BroadcastStartRefused(refusedId, message.Refusal())
                Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
                    _ports.Log.Invoke("Exchange agent: a listener of the broadcast refusal failed (" & ex.GetType().Name & ")")
                End Try
                Return
            End If
        End If
        If message IsNot Nothing AndAlso message.Is(ExchangeEnvelopeTypes.CastStop) Then
            SyncLock _gate
                If message.Text("broadcastId") = _castPlayingId AndAlso _castPlayingId IsNot Nothing Then
                    _castStopId = _castPlayingId
                    _castPlayingId = Nothing
                End If
            End SyncLock
            Return
        End If
        If message IsNot Nothing AndAlso message.Is(ExchangeEnvelopeTypes.CastResult) Then
            Dim bId As String = message.Text("broadcastId")
            Dim rId As String = message.Text("receiverDeviceId")
            Dim accepted As Boolean = message.Flag("accepted")
            Dim reason As String = message.Text("reason")
            Dim tcs As TaskCompletionSource(Of Tuple(Of Boolean, String)) = Nothing
            SyncLock _castPendingLock
                If _pendingCasts.TryGetValue(bId & ":" & rId, tcs) Then
                    tcs.TrySetResult(Tuple.Create(accepted, reason))
                End If
            End SyncLock
            Return
        End If
        If message IsNot Nothing AndAlso message.Is(ExchangeEnvelopeTypes.CastOffer) Then
            Dim pending As Task = ReceiveCastAsync(session, message)
            Return
        End If
        ' `open` is either for the live broadcast tunnel or the resource producer's tunnel
        If message IsNot Nothing AndAlso message.Is(ExchangeEnvelopeTypes.Open) Then
            Dim broadcastId As String = message.Text("broadcastId")
            If Not String.IsNullOrEmpty(broadcastId) Then
                Dim tunnelId As String = message.Text("tunnelId")
                Dim ct As CancellationToken = If(_cts IsNot Nothing, _cts.Token, CancellationToken.None)
                Dim t As Task = Task.Run(Function() HandleBroadcastOpenAsync(session, tunnelId, broadcastId, ct))
                Return
            End If
            Dim producer As IExchangeProducerHooks = _ports.Producer
            If producer IsNot Nothing Then
                producer.OnServerMessage(session, message)
                Return
            End If
        End If
        RaiseEvent ServerMessage(message)
    End Sub

    Private Async Function ReceiveCastAsync(session As ExchangeControlSession, message As ExchangeEnvelope) As Task
        Dim raw As String = message.Record("broadcast")
        Dim record As ExchangeBroadcastRecord = Nothing
        If Not ExchangeRecordReader.TryReadBroadcast(raw, record) Then Return
        Dim queued As Boolean
        Dim sequence As Long
        SyncLock _gate
            queued = _castId Is Nothing AndAlso ReferenceEquals(_session, session) AndAlso
                _viewerSeenMs <> Long.MinValue AndAlso Environment.TickCount64 - _viewerSeenMs < 8000
            If queued Then
                _castSequence += 1
                _castId = record.BroadcastId
                _castOffer = raw
            End If
            sequence = _castSequence
        End SyncLock
        If Not queued Then
            Await session.SendAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.CastAnswer).SetText("broadcastId", record.BroadcastId).
                SetFlag("accepted", False).SetText("reason", "unavailable"), CancellationToken.None).ConfigureAwait(False)
            Return
        End If
        ' An absent viewer must not leave the caster waiting indefinitely. No automatic launch or focus theft.
        Await Task.Delay(CastAnswerTimeoutMs).ConfigureAwait(False)
        Dim expired As Boolean
        SyncLock _gate
            expired = _castId = record.BroadcastId AndAlso _castSequence = sequence
            If expired Then
                _castId = Nothing
                _castOffer = Nothing
            End If
        End SyncLock
        If expired Then
            Await session.SendAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.CastAnswer).SetText("broadcastId", record.BroadcastId).
                SetFlag("accepted", False).SetText("reason", "timeout"), CancellationToken.None).ConfigureAwait(False)
        End If
    End Function

    ' --- live broadcast publication, relay upload, tunnel bridging, and cast out (spec 199 section 3.3) ---

    Friend Sub OnBroadcastStarted(state As LiveProducerState, audioHub As LiveBroadcastHub, videoHub As LiveBroadcastHub)
        If state Is Nothing Then Return
        Dim session As ExchangeControlSession
        SyncLock _gate
            _liveBroadcastId = state.BroadcastId
            _liveBroadcastState = state
            _liveUploadCts?.Cancel()
            _liveUploadCts = New CancellationTokenSource()
            session = _session
        End SyncLock

        Dim status As ExchangeDeviceStatus = Me.GetStatus()
        Dim deviceId As String = If(status IsNot Nothing, status.DeviceId, "")
        ' Items DK and DL: a broadcast with RELAY / TUNNEL endpoints is sealed. Its record carries the descriptor WITHOUT keys
        ' (`seal: {v}`) and the box; the descriptor with the link keys (state.DescriptorJson) never goes to the server.
        Dim publisher As ExchangeSealedBroadcastPublisher = If(state.Sealed, BeginSealedBroadcast(state, deviceId), Nothing)
        Dim recordJson As String
        If publisher IsNot Nothing Then
            recordJson = publisher.PrepareRecord()
        ElseIf state.Sealed Then
            ' sealed, but no key of this device: nothing goes through the server (the LAN endpoint, if any, is not sealed)
            recordJson = LiveProducerDescriptorWriter.BuildBroadcastRecord(state.BroadcastId, deviceId, state.SourceName, state.Mode, state.RecordDescriptorJson,
                                                                           state.StartedAtUtc, Date.UtcNow)
        Else
            ' Ticket 239 CR-15: the hand-out descriptor of a broadcast with no password is shortened to the LAN; the account's own
            ' record keeps every endpoint (state.RecordDescriptorJson is then set), exactly as before.
            recordJson = LiveProducerDescriptorWriter.BuildBroadcastRecord(state.BroadcastId, deviceId, state.SourceName, state.Mode,
                If(String.IsNullOrEmpty(state.RecordDescriptorJson), state.DescriptorJson, state.RecordDescriptorJson),
                state.StartedAtUtc, Date.UtcNow)
        End If
        SyncLock _gate
            _liveBroadcastRecordJson = recordJson
        End SyncLock

        If session IsNot Nothing Then
            Dim sealedSession As ExchangeSealedBroadcastSession = publisher?.Session
            Dim unused As Task = Task.Run(
                Async Function()
                    Try
                        ' Item DV: `verifiers` on every start, `[]` when the link has no password. The PBKDF2 runs on this pool thread.
                        Dim verifiers As IReadOnlyList(Of String) = Await VerifiersOfAsync(state).ConfigureAwait(False)
                        Dim sent As Boolean = Await SendBroadcastStartAsync(session, state.BroadcastId, recordJson, verifiers, CancellationToken.None).ConfigureAwait(False)
                        If sent Then sealedSession?.MarkPublished()
                    Catch
                    End Try
                End Function)
        End If

        Dim uploadCts As CancellationTokenSource
        SyncLock _gate
            uploadCts = _liveUploadCts
        End SyncLock
        If uploadCts IsNot Nothing Then
            Dim unusedUpload As Task = Task.Run(Function() RunUploadLoopAsync(state, audioHub, videoHub, uploadCts.Token))
        End If
    End Sub

    ''' <summary>Ticket 263 T-6: remember changed endpoints even while offline; repeat them on the next welcome.
    ''' Sealed records keep their session and box, and are serialized with recipient updates by the publisher.</summary>
    Friend Async Function OnBroadcastChangedAsync(state As LiveProducerState) As Task(Of Boolean)
        Dim publisher As ExchangeSealedBroadcastPublisher
        SyncLock _gate
            If state Is Nothing OrElse Not ReferenceEquals(state, _liveBroadcastState) OrElse _liveBroadcastId <> state.BroadcastId Then Return False
            publisher = _liveSealedPublisher
        End SyncLock
        If publisher IsNot Nothing Then Return Await publisher.PublishAsync(force:=True).ConfigureAwait(False)
        Dim status As ExchangeDeviceStatus = GetStatus()
        Dim record As String = LiveProducerDescriptorWriter.BuildBroadcastRecord(state.BroadcastId, status.DeviceId, state.SourceName, state.Mode,
            If(String.IsNullOrEmpty(state.RecordDescriptorJson), state.DescriptorJson, state.RecordDescriptorJson), state.StartedAtUtc, Date.UtcNow)
        SyncLock _gate
            If Not ReferenceEquals(state, _liveBroadcastState) Then Return False
            _liveBroadcastRecordJson = record
        End SyncLock
        Return Await SendBroadcastUpdateAsync(state, record).ConfigureAwait(False)
    End Function

    Friend Sub OnBroadcastStopped(broadcastId As String)
        Dim session As ExchangeControlSession
        Dim uploadCts As CancellationTokenSource
        SyncLock _gate
            If _liveBroadcastId = broadcastId Then
                _liveBroadcastId = Nothing
                _liveBroadcastRecordJson = Nothing
                _liveBroadcastState = Nothing
                If String.Equals(_startAwaitingId, broadcastId, StringComparison.Ordinal) Then _startAwaitingId = Nothing
                uploadCts = _liveUploadCts
                _liveUploadCts = Nothing
            End If
            session = _session
        End SyncLock
        Try
            uploadCts?.Cancel()
        Catch
        End Try
        ' the keys of the ended broadcast (and any made for one that never started) are wiped
        DisposeSealedBroadcast(broadcastId)
        If session IsNot Nothing AndAlso Not String.IsNullOrEmpty(broadcastId) Then
            Dim unused As Task = Task.Run(
                Async Function()
                    Try
                        Await session.SendAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.BroadcastEnd).SetText("broadcastId", broadcastId), CancellationToken.None).ConfigureAwait(False)
                    Catch
                    End Try
                End Function)
        End If
    End Sub

    Private Async Function RunUploadLoopAsync(state As LiveProducerState, audioHub As LiveBroadcastHub, videoHub As LiveBroadcastHub, ct As CancellationToken) As Task
        ' Item DL, no clear fallback: the relay carries sealed records or nothing. A broadcast without a sealed session (no sealing
        ' layer wired, or no key of this device) uploads nothing. Ticket 263 T-3: a PASSWORD no longer keeps a sealed broadcast off the
        ' relay - the relay checks the verifiers sent with the start and every update (item BF) and, sealed, carries ciphertext in any
        ' case. A protected broadcast with no sealing never lists a RELAY (the controller withholds it), so state.RelayUrl is empty.
        If String.IsNullOrEmpty(state.RelayUrl) OrElse Not state.Sealed Then Return
        Dim isVideo As Boolean = String.Equals(state.Mode, "VIDEO_AUDIO", StringComparison.OrdinalIgnoreCase) OrElse
                                 String.Equals(state.Mode, "VIDEO_ONLY", StringComparison.OrdinalIgnoreCase)
        Dim hub As LiveBroadcastHub = If(isVideo, videoHub, audioHub)
        Dim sealedSession As ExchangeSealedBroadcastSession = CurrentSealed()
        If hub Is Nothing OrElse sealedSession Is Nothing OrElse Not String.Equals(sealedSession.BroadcastId, state.BroadcastId, StringComparison.Ordinal) Then Return

        Await LiveSealedRelayUploader.RunAsync(sealedSession, hub, isVideo,
                                               Function(token) OpenSealedUploadAsync(state.BroadcastId, token), 1000, ct).ConfigureAwait(False)
    End Function

    ''' <summary>One relay upload of item DL: `hello`, `upload` with the content type of the sealed relay, `upload-accepted`.
    ''' Nothing while there is no connected session (the uploader tries again).</summary>
    Private Async Function OpenSealedUploadAsync(broadcastId As String, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing OrElse session.ConnectionOptions Is Nothing Then Return Nothing
        Dim opts As ExchangeControlOptions = session.ConnectionOptions
        Return Await ExchangeDataStreams.UploadAsync(opts.Transport, opts.Server, opts.Pin, opts.Credentials, opts.ProductVersion,
                                                     broadcastId, ExchangeRelayRecords.ContentType, ct).ConfigureAwait(False)
    End Function

    ''' <summary>Ticket 263 T-6 (read-only, for the tests): the id of the broadcast whose `broadcast-start` is sent and not yet answered,
    ''' or Nothing.</summary>
    Friend ReadOnly Property BroadcastStartAwaitingId As String
        Get
            SyncLock _gate
                Return _startAwaitingId
            End SyncLock
        End Get
    End Property

    ' --- sealed broadcast (spec 234 T-3, items DK and DL) ---

    ''' <summary>The sealed session of the running broadcast; Nothing when there is none.</summary>
    Friend Function CurrentSealed() As ExchangeSealedBroadcastSession
        SyncLock _gate
            Return _liveSealed
        End SyncLock
    End Function

    ''' <summary>True when the current session may seal through its server: connected, `sealed` listed, a key of this device. The
    ''' producer asks before it lists a RELAY or TUNNEL endpoint.</summary>
    Friend Function CanSealBroadcast() As Boolean
        Dim view As ExchangeSealedRecipients = GetSealedRecipients()
        Return view IsNot Nothing AndAlso view.CanSealThroughServer
    End Function

    ''' <summary>A new group key for one outward link of the broadcast, as the text the outward descriptor carries (base64url); ""
    ''' at the ring bound. Before the broadcast starts the key waits for its session; on a running one the ring changes, so a
    ''' replacement upload follows. A secret: never logged.</summary>
    Friend Function NewBroadcastLinkKey(broadcastId As String) As String
        If String.IsNullOrEmpty(broadcastId) Then Return ""
        Dim running As ExchangeSealedBroadcastSession = Nothing
        Dim publisher As ExchangeSealedBroadcastPublisher = Nothing
        Dim keys As ExchangeBroadcastKeys = Nothing
        SyncLock _gate
            If _liveSealed IsNot Nothing AndAlso String.Equals(_liveSealed.BroadcastId, broadcastId, StringComparison.Ordinal) Then
                running = _liveSealed
                publisher = _liveSealedPublisher
            ElseIf Not _pendingBroadcastKeys.TryGetValue(broadcastId, keys) Then
                ' one broadcast at a time: keys made for another id that never started are wiped
                For Each stale As ExchangeBroadcastKeys In _pendingBroadcastKeys.Values
                    stale.Dispose()
                Next
                _pendingBroadcastKeys.Clear()
                keys = New ExchangeBroadcastKeys()
                _pendingBroadcastKeys(broadcastId) = keys
            End If
        End SyncLock
        If running IsNot Nothing Then
            Dim text As String = running.AddLink()
            ' the record is sent first, then the replacement upload follows (item N)
            If text.Length > 0 AndAlso publisher IsNot Nothing Then
                Dim unused As Task = publisher.NotifyRecipientsChangedAsync()
            End If
            Return text
        End If
        Return keys.AddLink()
    End Function

    ''' <summary>The `SHA256:` pin of the inner TLS leaf of this broadcast's TUNNEL (item DM), as the outward descriptor carries it in
    ''' `seal.pin`. The tunnel identity is made on the first ask for a broadcast id (before the broadcast starts, as the descriptor is
    ''' built first) and the same pin comes back on every later ask; "" when this device cannot seal through the server or the identity
    ''' could not be made. An identity made for a broadcast that never starts is wiped when the next broadcast asks.</summary>
    Friend Function NewBroadcastTunnelPin(broadcastId As String) As String
        If String.IsNullOrEmpty(broadcastId) OrElse Not CanSealBroadcast() Then Return ""
        Dim stale As New List(Of ExchangeSealedTunnel)()
        Dim identity As ExchangeSealedTunnel = Nothing
        Try
            SyncLock _gate
                If _liveTunnel IsNot Nothing AndAlso String.Equals(_liveTunnelBroadcastId, broadcastId, StringComparison.Ordinal) Then
                    identity = _liveTunnel
                ElseIf Not _pendingTunnels.TryGetValue(broadcastId, identity) Then
                    ' one broadcast at a time: identities made for another id that never started are wiped
                    stale.AddRange(_pendingTunnels.Values)
                    _pendingTunnels.Clear()
                    identity = New ExchangeSealedTunnel()
                    _pendingTunnels(broadcastId) = identity
                End If
                Return identity.Pin
            End SyncLock
        Catch ex As System.Security.Cryptography.CryptographicException
            Return ""
        Finally
            For Each old As ExchangeSealedTunnel In stale
                old.Dispose()
            Next
        End Try
    End Function

    ''' <summary>Makes the sealed session and its publisher for a broadcast that starts now; Nothing when this device has no key of its
    ''' own to seal with.</summary>
    Private Function BeginSealedBroadcast(state As LiveProducerState, deviceId As String) As ExchangeSealedBroadcastPublisher
        If Not CanSealBroadcast() Then Return Nothing
        Dim isVideo As Boolean = String.Equals(state.Mode, "VIDEO_AUDIO", StringComparison.OrdinalIgnoreCase) OrElse
                                 String.Equals(state.Mode, "VIDEO_ONLY", StringComparison.OrdinalIgnoreCase)
        Dim keys As ExchangeBroadcastKeys = Nothing
        Dim previous As ExchangeSealedBroadcastSession = Nothing
        Dim previousPublisher As ExchangeSealedBroadcastPublisher = Nothing
        Dim identity As ExchangeSealedTunnel = Nothing
        Dim previousTunnel As ExchangeSealedTunnel = Nothing
        Dim wantsTunnel As Boolean = Not String.IsNullOrEmpty(state.TunnelUrl)
        SyncLock _gate
            If Not _pendingBroadcastKeys.TryGetValue(state.BroadcastId, keys) Then keys = New ExchangeBroadcastKeys()
            _pendingBroadcastKeys.Remove(state.BroadcastId)
            previous = _liveSealed
            previousPublisher = _liveSealedPublisher
            ' Item DM: the tunnel identity made for the outward descriptor is adopted (its pin is already handed out); a start without a
            ' prior ask makes one now. An identity of an earlier broadcast is wiped.
            If wantsTunnel Then
                If _liveTunnel IsNot Nothing AndAlso String.Equals(_liveTunnelBroadcastId, state.BroadcastId, StringComparison.Ordinal) Then
                    identity = _liveTunnel
                ElseIf _pendingTunnels.TryGetValue(state.BroadcastId, identity) Then
                    _pendingTunnels.Remove(state.BroadcastId)
                End If
            End If
            If _liveTunnel IsNot Nothing AndAlso Not ReferenceEquals(_liveTunnel, identity) Then previousTunnel = _liveTunnel
            _liveTunnel = Nothing
            _liveTunnelBroadcastId = Nothing
        End SyncLock
        previousPublisher?.Dispose()
        previous?.Dispose()
        previousTunnel?.Dispose()
        If wantsTunnel AndAlso identity Is Nothing Then
            Try
                identity = New ExchangeSealedTunnel()
            Catch ex As System.Security.Cryptography.CryptographicException
                identity = Nothing
            End Try
        End If

        ' Item BF: the box hands the account's OWN devices the OWN pair (user `own`), not the pair the person typed for the link; both
        ' open the producer's Digest and are checked by the relay, and either can be revoked alone. Nothing to carry without a password.
        Dim ownPassword As String = If(state.Credentials Is Nothing, Nothing, state.Credentials.PasswordFor(LiveCredentialPairs.OwnUser))
        Dim ownUser As String = If(String.IsNullOrEmpty(ownPassword), "", LiveCredentialPairs.OwnUser)
        ownPassword = If(ownPassword, "")
        Dim sealedSession As New ExchangeSealedBroadcastSession(
            state.BroadcastId, If(isVideo, "video/mp2t", "audio/aac"), keys,
            Function() GetSealedRecipients(), Function() KeyStore().TryLoad(deviceId),
            hasRelay:=Not String.IsNullOrEmpty(state.RelayUrl), tunnelPin:=If(identity IsNot Nothing, identity.Pin, ""),
            credentialUser:=ownUser, credentialPassword:=ownPassword)
        Dim publisher As New ExchangeSealedBroadcastPublisher(
            sealedSession,
            Function(box) LiveProducerDescriptorWriter.BuildBroadcastRecord(state.BroadcastId, deviceId, state.SourceName, state.Mode, state.RecordDescriptorJson,
                                                                              state.StartedAtUtc, Date.UtcNow, box),
            Sub(record)
                SyncLock _gate
                    If String.Equals(_liveBroadcastId, state.BroadcastId, StringComparison.Ordinal) Then _liveBroadcastRecordJson = record
                End SyncLock
            End Sub,
            Function(record) SendBroadcastUpdateAsync(state, record),
            ExchangeSealedBroadcastPublisher.DefaultDebounceMs)
        SyncLock _gate
            _liveSealed = sealedSession
            _liveSealedPublisher = publisher
            _liveTunnel = identity
            _liveTunnelBroadcastId = If(identity IsNot Nothing, state.BroadcastId, Nothing)
        End SyncLock
        Return publisher
    End Function

    Private Async Function SendBroadcastUpdateAsync(state As LiveProducerState, record As String) As Task(Of Boolean)
        Dim session As ExchangeControlSession
        SyncLock _gate
            If Not ReferenceEquals(state, _liveBroadcastState) OrElse _liveBroadcastId <> state.BroadcastId Then Return False
            session = _session
        End SyncLock
        If session Is Nothing Then Return False
        Try
            ' Item DV: `verifiers` on every update too, the full current list (a non-empty list REPLACES the relay's held one, `[]`
            ' tells it to check nobody, an absent member would leave a revoked pair in force).
            Dim verifiers As IReadOnlyList(Of String) = Await VerifiersOfAsync(state).ConfigureAwait(False)
            SyncLock _gate
                If Not ReferenceEquals(state, _liveBroadcastState) OrElse Not ReferenceEquals(session, _session) Then Return False
            End SyncLock
            Return Await session.SendAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.BroadcastUpdate).SetRecord("broadcast", record).
                                           SetNumber("maxListeners", state.MaxListeners).SetRecords("verifiers", verifiers), CancellationToken.None).ConfigureAwait(False)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>The `verifiers` of a broadcast (items BF and DV): one wire object per credential pair it holds now, `[]` when it has none
    ''' (the link has no password). The first call pays the PBKDF2 of the pairs on a pool thread - never on the caller's - and the set
    ''' caches the hashes, so a later start, update or reconnect costs nothing.</summary>
    Private Shared Async Function VerifiersOfAsync(state As LiveProducerState) As Task(Of IReadOnlyList(Of String))
        Dim pairs As LiveCredentialPairs = If(state Is Nothing, Nothing, state.Credentials)
        If pairs Is Nothing OrElse pairs.Count = 0 Then Return Array.Empty(Of String)()
        Return Await Task.Run(Function() pairs.VerifierRecords()).ConfigureAwait(False)
    End Function

    ''' <summary>Sends a `broadcast-start` and remembers that its answer is awaited (ticket 263 T-6). When the frame is not sent no answer
    ''' will come, so the mark is taken back at once and a later unrelated `refused` is never attributed to the broadcast.</summary>
    Private Async Function SendBroadcastStartAsync(session As ExchangeControlSession, broadcastId As String, record As String,
                                                   verifiers As IEnumerable(Of String), ct As CancellationToken) As Task(Of Boolean)
        Dim maxListeners As Integer
        SyncLock _gate
            If _liveBroadcastId <> broadcastId OrElse _liveBroadcastState Is Nothing OrElse Not ReferenceEquals(session, _session) Then Return False
            record = _liveBroadcastRecordJson
            maxListeners = _liveBroadcastState.MaxListeners
            _startAwaitingId = broadcastId
        End SyncLock
        Dim sent As Boolean = False
        Try
            sent = Await session.SendAsync(BroadcastStartEnvelope(record, verifiers, maxListeners), ct).ConfigureAwait(False)
        Finally
            If Not sent Then
                SyncLock _gate
                    If String.Equals(_startAwaitingId, broadcastId, StringComparison.Ordinal) Then _startAwaitingId = Nothing
                End SyncLock
            End If
        End Try
        Return sent
    End Function

    Private Shared Function BroadcastStartEnvelope(record As String, verifiers As IEnumerable(Of String), maxListeners As Integer) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.BroadcastStart).SetRecord("broadcast", record).
            SetNumber("maxListeners", maxListeners).SetRecords("verifiers", verifiers)
    End Function

    ''' <summary>Item BF, "each revocable alone": drops ONE credential pair of the running broadcast. The pair leaves the producer's
    ''' Digest at once (the set is shared with the server) and the relay's list with a `broadcast-update` that carries the new full
    ''' list; a relay that follows item BF drops the verifiers it held and checks the next listen request against the new list.
    ''' False when no broadcast is running or the user holds no pair - and also for the LAST pair, which a protected broadcast keeps
    ''' (<see cref="LiveCredentialPairs.Revoke"/>). True means the pair is dropped here; the update is sent best-effort and the next
    ''' reconnect repeats the list (<see cref="OnWelcomeAsync"/>). There is no window control for it yet: the password is locked on
    ''' air, and per-grant and per-link pairs wait for the worker's per-grant login (ticket 263 T-4).</summary>
    Friend Async Function RevokeBroadcastPairAsync(user As String) As Task(Of Boolean)
        Dim state As LiveProducerState
        Dim record As String
        SyncLock _gate
            state = _liveBroadcastState
            record = _liveBroadcastRecordJson
        End SyncLock
        If state Is Nothing OrElse state.Credentials Is Nothing OrElse String.IsNullOrEmpty(record) Then Return False
        If Not state.Credentials.Revoke(user) Then Return False
        If String.Equals(user, LiveCredentialPairs.OwnUser, StringComparison.Ordinal) Then
            Dim publisher As ExchangeSealedBroadcastPublisher
            SyncLock _gate
                publisher = If(ReferenceEquals(state, _liveBroadcastState), _liveSealedPublisher, Nothing)
            End SyncLock
            If publisher IsNot Nothing Then
                publisher.Session.SetCredentials("", "")
                Await publisher.PublishAsync(force:=True).ConfigureAwait(False)
                Return True
            End If
        End If
        Await SendBroadcastUpdateAsync(state, record).ConfigureAwait(False)
        Return True
    End Function

    ''' <summary>The sealed state of an ended broadcast (or of keys made for one that never started) is wiped.</summary>
    Private Sub DisposeSealedBroadcast(broadcastId As String)
        Dim sealedSession As ExchangeSealedBroadcastSession = Nothing
        Dim publisher As ExchangeSealedBroadcastPublisher = Nothing
        Dim stale As New List(Of ExchangeBroadcastKeys)()
        Dim tunnels As New List(Of ExchangeSealedTunnel)()
        SyncLock _gate
            If _liveSealed IsNot Nothing AndAlso String.Equals(_liveSealed.BroadcastId, broadcastId, StringComparison.Ordinal) Then
                sealedSession = _liveSealed
                publisher = _liveSealedPublisher
                _liveSealed = Nothing
                _liveSealedPublisher = Nothing
            End If
            ' item DM: the tunnel identity (and its private key) of the ended broadcast, or one made for a broadcast that never started
            If _liveTunnel IsNot Nothing AndAlso String.Equals(_liveTunnelBroadcastId, broadcastId, StringComparison.Ordinal) Then
                tunnels.Add(_liveTunnel)
                _liveTunnel = Nothing
                _liveTunnelBroadcastId = Nothing
            End If
            Dim pendingTunnel As ExchangeSealedTunnel = Nothing
            If Not String.IsNullOrEmpty(broadcastId) AndAlso _pendingTunnels.TryGetValue(broadcastId, pendingTunnel) Then
                tunnels.Add(pendingTunnel)
                _pendingTunnels.Remove(broadcastId)
            End If
            Dim waiting As ExchangeBroadcastKeys = Nothing
            If Not String.IsNullOrEmpty(broadcastId) AndAlso _pendingBroadcastKeys.TryGetValue(broadcastId, waiting) Then
                stale.Add(waiting)
                _pendingBroadcastKeys.Remove(broadcastId)
            End If
        End SyncLock
        publisher?.Dispose()
        sealedSession?.Dispose()
        For Each keys As ExchangeBroadcastKeys In stale
            keys.Dispose()
        Next
        For Each tunnel As ExchangeSealedTunnel In tunnels
            tunnel.Dispose()
        Next
    End Sub

    ''' <summary>The recipients of the sealed directory changed: the running broadcast's record gets a new box (item DK).</summary>
    Private Sub OnSealedRecipientsChanged(sender As Object, e As EventArgs)
        Dim publisher As ExchangeSealedBroadcastPublisher
        SyncLock _gate
            publisher = _liveSealedPublisher
        End SyncLock
        If publisher Is Nothing Then Return
        Dim unused As Task = publisher.NotifyRecipientsChangedAsync()
    End Sub

    Private Async Function HandleBroadcastOpenAsync(session As ExchangeControlSession, tunnelId As String, broadcastId As String, ct As CancellationToken) As Task
        If String.IsNullOrEmpty(tunnelId) OrElse session Is Nothing Then Return
        Dim state As LiveProducerState
        SyncLock _gate
            state = _liveBroadcastState
        End SyncLock
        If state Is Nothing OrElse state.BroadcastId <> broadcastId OrElse Not state.Running Then Return
        ' Item DM / item DP: a sealed broadcast has no clear fallback through the server. Its tunnel bridges only behind the inner TLS
        ' of this broadcast's own identity; with no identity (no key of this device, the server lost `sealed`) the bridge stays shut,
        ' so no clear media byte or Digest exchange crosses the server. LAN viewers and the sealed relay are not affected.
        Dim tunnel As ExchangeSealedTunnel = Nothing
        If state.Sealed Then
            SyncLock _gate
                If _liveTunnel IsNot Nothing AndAlso String.Equals(_liveTunnelBroadcastId, broadcastId, StringComparison.Ordinal) Then tunnel = _liveTunnel
            End SyncLock
            If tunnel Is Nothing OrElse Not CanSealBroadcast() Then Return
        End If

        Dim localClient As New TcpClient()
        Dim attached As ExchangeOpenResult = Nothing
        Try
            If tunnel IsNot Nothing Then
                ' attach, answer with the TLS server, and only then touch the local broadcast server
                Dim sealedOpts As ExchangeControlOptions = session.ConnectionOptions
                attached = Await ExchangeDataStreams.AttachAsync(sealedOpts.Transport, sealedOpts.Server, sealedOpts.Pin, sealedOpts.Credentials, sealedOpts.ProductVersion, tunnelId, ct).ConfigureAwait(False)
                If attached Is Nothing OrElse Not attached.IsConnected Then Return
                Dim inner As Stream = Await tunnel.ServerStreamAsync(attached.Stream, ct).ConfigureAwait(False)
                Try
                    Await localClient.ConnectAsync(state.ListenerAddress, state.Port, ct).ConfigureAwait(False)
                    Await ExchangeBridge.RunAsync(inner, localClient.GetStream(), New ExchangeBridgeOptions With {.IdleTimeout = TimeSpan.FromSeconds(30)}, ct).ConfigureAwait(False)
                Finally
                    Try : inner.Dispose() : Catch : End Try
                End Try
                Return
            End If
            Await localClient.ConnectAsync(state.ListenerAddress, state.Port, ct).ConfigureAwait(False)
            Dim opts As ExchangeControlOptions = session.ConnectionOptions
            attached = Await ExchangeDataStreams.AttachAsync(opts.Transport, opts.Server, opts.Pin, opts.Credentials, opts.ProductVersion, tunnelId, ct).ConfigureAwait(False)
            If attached Is Nothing OrElse Not attached.IsConnected Then Return

            Await ExchangeBridge.RunAsync(attached.Stream, localClient.GetStream(), New ExchangeBridgeOptions With {.IdleTimeout = TimeSpan.FromSeconds(30)}, ct).ConfigureAwait(False)
        Catch
        Finally
            Try : localClient.Dispose() : Catch : End Try
            Try : attached?.Dispose() : Catch : End Try
        End Try
    End Function

    ''' <summary>Initiates a cast offer to a receiver device (spec 199 section 3.3 item 5).</summary>
    Public Async Function CastAsync(broadcastId As String, receiverDeviceId As String, ct As CancellationToken) As Task(Of Tuple(Of Boolean, String))
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return Tuple.Create(False, "unavailable")

        Dim key As String = broadcastId & ":" & receiverDeviceId
        Dim tcs As New TaskCompletionSource(Of Tuple(Of Boolean, String))()
        SyncLock _castPendingLock
            _pendingCasts(key) = tcs
        End SyncLock

        Try
            ' Item DK / 7.8: a device the current box does not address gets the record updated BEFORE the cast
            Dim publisher As ExchangeSealedBroadcastPublisher
            SyncLock _gate
                publisher = If(String.Equals(_liveBroadcastId, broadcastId, StringComparison.Ordinal), _liveSealedPublisher, Nothing)
            End SyncLock
            If publisher IsNot Nothing Then Await publisher.BeforeCastAsync(receiverDeviceId).ConfigureAwait(False)
            Dim sent As Boolean = Await session.SendAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.Cast).
                SetText("broadcastId", broadcastId).SetText("receiverDeviceId", receiverDeviceId), ct).ConfigureAwait(False)
            If Not sent Then Return Tuple.Create(False, "unavailable")

            Using reg As CancellationTokenRegistration = ct.Register(Sub() tcs.TrySetResult(Tuple.Create(False, "timeout")))
                Return Await tcs.Task.ConfigureAwait(False)
            End Using
        Finally
            SyncLock _castPendingLock
                _pendingCasts.Remove(key)
            End SyncLock
        End Try
    End Function

    ''' <summary>Stops an active cast session on the receiver (spec 199 section 3.3 item 5).</summary>
    Public Async Function CastStopAsync(broadcastId As String, receiverDeviceId As String, ct As CancellationToken) As Task(Of Boolean)
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return False
        Return Await session.SendAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.CastStop).
            SetText("broadcastId", broadcastId).SetText("receiverDeviceId", receiverDeviceId), ct).ConfigureAwait(False)
    End Function

    ''' <summary>Finds qualifying candidate receiver devices on the account (spec 200 rule 8).</summary>
    Public Function GetCastCandidates(mode As String, descriptorJson As String) As List(Of ExchangeDeviceRecord)
        Dim result As New List(Of ExchangeDeviceRecord)()
        Dim snapshot As ExchangeDirectorySnapshot = GetFullDirectory()
        If snapshot Is Nothing OrElse snapshot.Devices Is Nothing Then Return result

        Dim transports As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If Not String.IsNullOrEmpty(descriptorJson) Then
            Try
                Using doc As JsonDocument = JsonDocument.Parse(descriptorJson)
                    Dim endpointsEl As JsonElement = Nothing
                    If doc.RootElement.TryGetProperty("endpoints", endpointsEl) AndAlso endpointsEl.ValueKind = JsonValueKind.Array Then
                        For Each ep In endpointsEl.EnumerateArray()
                            Dim tEl As JsonElement = Nothing
                            If ep.TryGetProperty("transport", tEl) AndAlso tEl.ValueKind = JsonValueKind.String Then
                                transports.Add(tEl.GetString())
                            End If
                        Next
                    End If
                End Using
            Catch
            End Try
        End If
        If transports.Count = 0 Then transports.Add("HTTP")

        For Each d As ExchangeDeviceRecord In snapshot.Devices
            If Not d.IsOnline Then Continue For
            If Not d.Roles.Contains(ExchangeWire.RoleReceiver, StringComparer.OrdinalIgnoreCase) Then Continue For

            Dim qualified As Boolean = False
            Try
                Using doc As JsonDocument = JsonDocument.Parse(d.Raw)
                    Dim recEl As JsonElement = Nothing
                    If doc.RootElement.TryGetProperty("receiver", recEl) AndAlso recEl.ValueKind = JsonValueKind.Object Then
                        Dim modesEl As JsonElement = Nothing
                        Dim modeMatch As Boolean = False
                        If recEl.TryGetProperty("modes", modesEl) AndAlso modesEl.ValueKind = JsonValueKind.Array Then
                            For Each m In modesEl.EnumerateArray()
                                If String.Equals(m.GetString(), mode, StringComparison.OrdinalIgnoreCase) Then
                                    modeMatch = True
                                    Exit For
                                End If
                            Next
                        End If

                        Dim transMatch As Boolean = False
                        Dim transEl As JsonElement = Nothing
                        If recEl.TryGetProperty("transports", transEl) AndAlso transEl.ValueKind = JsonValueKind.Array Then
                            For Each tr In transEl.EnumerateArray()
                                If transports.Contains(tr.GetString()) Then
                                    transMatch = True
                                    Exit For
                                End If
                            Next
                        End If

                        If modeMatch AndAlso transMatch Then qualified = True
                    End If
                End Using
            Catch
            End Try

            If qualified Then result.Add(d)
        Next

        Return result
    End Function

    ''' <summary>The producer (170) when this agent has one; the window reads its snapshot and drives the links through it.</summary>
    Public ReadOnly Property Publisher As ExchangePublisher
        Get
            Return TryCast(_ports.Producer, ExchangePublisher)
        End Get
    End Property

    ' --- the local channel ---

    Private Sub WakeWaiters()
        Dim list As List(Of TaskCompletionSource(Of Boolean))
        SyncLock _gate
            list = _changeWaiters
            _changeWaiters = New List(Of TaskCompletionSource(Of Boolean))
        End SyncLock
        For Each w As TaskCompletionSource(Of Boolean) In list
            w.TrySetResult(True)
        Next
    End Sub

    ''' <summary>A waiter the next directory change (or a stop) completes. Registered before the caller looks at the revision.</summary>
    Private Function RegisterWaiter() As TaskCompletionSource(Of Boolean)
        Dim tcs As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        SyncLock _gate
            _changeWaiters.Add(tcs)
        End SyncLock
        Return tcs
    End Function

    ''' <summary>The revision of the mirrored directory, or -1 when there is no snapshot (no stream, or not listed yet). It includes the
    ''' Drive listing's revision (<see cref="DriveDirectoryMerge"/>), so a change listed on Drive answers a viewer that holds the old number.</summary>
    Private Function CurrentRevision() As Long
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing OrElse Not session.Model.HasSnapshot Then Return -1
        Return session.Model.Revision + DriveListing().Revision
    End Function

    Private Function DriveListing() As ExchangeDirectorySnapshot
        Dim source As Func(Of ExchangeDirectorySnapshot) = _ports.DriveDirectory
        Dim listing As ExchangeDirectorySnapshot = If(source Is Nothing, Nothing, source.Invoke())
        Return If(listing, ExchangeDirectorySnapshot.Empty)
    End Function

    ''' <summary>What the local channel hands the viewer: the server's directory with the Drive listing merged in (203 section 5.11). The
    ''' own device is dropped unless <paramref name="includeOwn"/>. Other callers keep reading the server's directory alone: a cast
    ''' candidate or a "My devices" row that exists only on Drive could not be used through the server.</summary>
    Private Function ListedDirectory(includeOwn As Boolean) As ExchangeDirectorySnapshot
        Dim merged As ExchangeDirectorySnapshot = DriveDirectoryMerge.Union(GetFullDirectory(), DriveListing())
        If includeOwn Then Return merged
        Dim account As ExchangeAccount = _ports.Accounts.Load()
        Return merged.WithoutDevice(If(account Is Nothing, "", account.DeviceId))
    End Function

    Private Shared Async Function WaitAsync(tcs As TaskCompletionSource(Of Boolean), waitMs As Integer, ct As CancellationToken) As Task
        Using deadline As New CancellationTokenSource(waitMs)
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token)
                Using linked.Token.Register(Sub() tcs.TrySetCanceled())
                    Try
                        Await tcs.Task.ConfigureAwait(False)
                    Catch ex As OperationCanceledException
                    End Try
                End Using
            End Using
        End Using
    End Function

    Public Async Function HandleAsync(requestType As String, request As JsonElement, ct As CancellationToken) As Task(Of ExchangeLocalResult) Implements IExchangeAgentHandler.HandleAsync
        Select Case requestType
            Case ExchangeAgentPipe.RequestGetState
                Dim st As ExchangeDeviceStatus = GetStatus()
                Return ExchangeLocalResult.Success(New ExchangeAgentStateInfo(st.Configured, st.State, st.Features, st.DeviceId, st.DeviceName, st.LastReason).ToJson())

            Case ExchangeAgentPipe.RequestGetDirectory, ExchangeAgentPipe.RequestGetLiveDirectory
                Dim status As ExchangeDeviceStatus = GetStatus()
                If Not status.Configured Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNotConfigured)
                Dim since As Long = ReadLong(request, "sinceRevision", 0)
                Dim waitMs As Integer = CInt(Math.Min(Math.Max(ReadLong(request, "waitMs", 0), 0), 30000))
                ' Register the waiter BEFORE looking at the revision: a change that lands between the look and the wait would
                ' otherwise be missed and the caller would sit out its whole deadline.
                Dim waiter As TaskCompletionSource(Of Boolean) = If(waitMs > 0, RegisterWaiter(), Nothing)
                Dim revision As Long = CurrentRevision()
                If waiter IsNot Nothing AndAlso revision <= since Then
                    ' nothing newer than the caller holds (or no snapshot yet): wait for the next change, up to its deadline
                    Await WaitAsync(waiter, waitMs, ct).ConfigureAwait(False)
                    revision = CurrentRevision()
                End If
                If revision < 0 Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNotConnected)
                Return ExchangeLocalResult.Success(ListedDirectory(requestType = ExchangeAgentPipe.RequestGetLiveDirectory).ToLocalJson())

            Case ExchangeAgentPipe.RequestGetAccess
                Dim id As String = ReadString(request, "resourceId")
                If id.Length = 0 Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
                ' a folder listed on Drive alone has no `access` (item X): it is found here and answered "no access", not "unknown"
                Dim snapshot As ExchangeDirectorySnapshot = ListedDirectory(True)
                Dim found As ExchangeResourceRecord = Nothing
                For Each r As ExchangeResourceRecord In snapshot.Resources
                    If String.Equals(r.ResourceId, id, StringComparison.Ordinal) Then found = r
                Next
                If found Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnknownResource)
                ' spec 234 items DI, DJ, DP: the access comes out of the record's box, opened here with this device's key; a clear
                ' `access` from the server is never handed on, and the viewer is told the typed state instead
                Dim sealedView As ExchangeSealedRecipients = GetSealedRecipients()
                If sealedView Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNotConnected)
                Dim resolved As ExchangeSealedAccessResult
                Using ownKey As ExchangeDeviceKey = KeyStore().TryLoad(sealedView.OwnDeviceId)
                    resolved = ExchangeSealedAccess.Resolve(found, sealedView, TrustStore(), ownKey)
                End Using
                If resolved.State IsNot Nothing Then Return ExchangeLocalResult.Failure(resolved.State.Code)
                If Not resolved.Opened Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNoAccess)
                Return ExchangeLocalResult.Success(resolved.AccessJson)

            Case ExchangeAgentPipe.RequestGetBroadcastSeal
                Dim id As String = ReadString(request, "broadcastId")
                If Not ExchangeIds.IsValidId(id) Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
                Dim view As ExchangeSealedRecipients = GetSealedRecipients()
                If view Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNotConnected)
                For Each broadcast As ExchangeBroadcastRecord In GetFullDirectory().Broadcasts
                    If broadcast.BroadcastId <> id Then Continue For
                    Dim resolved As ExchangeSealedAccessResult
                    Using ownKey As ExchangeDeviceKey = KeyStore().TryLoad(view.OwnDeviceId)
                        resolved = ExchangeSealedBroadcastAccess.Resolve(broadcast, view, TrustStore(), ownKey)
                    End Using
                    If resolved.State IsNot Nothing Then Return ExchangeLocalResult.Failure(resolved.State.Code)
                    If Not resolved.Opened Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNoAccess)
                    Return ExchangeLocalResult.Success(resolved.AccessJson)
                Next
                Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnknownResource)

            Case ExchangeAgentPipe.RequestGetBroadcast
                Dim id As String = ReadString(request, "broadcastId")
                For Each broadcast As ExchangeBroadcastRecord In GetFullDirectory().Broadcasts
                    If broadcast.BroadcastId = id Then Return ExchangeLocalResult.Success(broadcast.Raw)
                Next
                Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnknownResource)

            Case ExchangeAgentPipe.RequestGetCastOffer
                SyncLock _gate
                    _viewerSeenMs = Environment.TickCount64
                    If _castStopId IsNot Nothing Then
                        Dim stopRecord As New JsonObject()
                        stopRecord("stopBroadcastId") = _castStopId
                        _castStopId = Nothing
                        Return ExchangeLocalResult.Success(stopRecord.ToJsonString())
                    End If
                    Return ExchangeLocalResult.Success(If(_castOffer, "null"))
                End SyncLock

            Case ExchangeAgentPipe.RequestCastAnswer
                Dim id As String = ReadString(request, "broadcastId")
                Dim session As ExchangeControlSession
                SyncLock _gate
                    If _castId Is Nothing OrElse id <> _castId Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
                    session = _session
                    _castId = Nothing
                    _castOffer = Nothing
                End SyncLock
                If session Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNotConnected)
                Dim acceptedEl As JsonElement = Nothing
                Dim accepted As Boolean = request.TryGetProperty("accepted", acceptedEl) AndAlso acceptedEl.ValueKind = JsonValueKind.True
                If accepted Then
                    SyncLock _gate
                        _castPlayingId = id
                    End SyncLock
                End If
                Dim reason As String = If(accepted, "", ReadString(request, "reason"))
                If Not accepted AndAlso reason <> "unsupported" AndAlso reason <> "unavailable" Then reason = "declined"
                Dim sent As Boolean = Await session.SendAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.CastAnswer).
                    SetText("broadcastId", id).SetFlag("accepted", accepted).SetText("reason", reason), ct).ConfigureAwait(False)
                Return If(sent, ExchangeLocalResult.Success("null"), ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNotConnected))

            Case ExchangeAgentPipe.RequestCastPolicy
                ' The persisted preference belongs to the viewer, not to the agent's local protocol.
                Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnsupported)

            Case Else
                Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
        End Select
    End Function

    Private Shared Function ReadLong(request As JsonElement, name As String, def As Long) As Long
        Dim el As JsonElement = Nothing
        Dim n As Long
        If request.ValueKind = JsonValueKind.Object AndAlso request.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.Number AndAlso el.TryGetInt64(n) Then Return n
        Return def
    End Function

    Private Shared Function ReadString(request As JsonElement, name As String) As String
        Dim el As JsonElement = Nothing
        If request.ValueKind = JsonValueKind.Object AndAlso request.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

End Class

''' <summary>The one agent of this process, created only when the gate is open and an account is configured (or the user asks to sign in).</summary>
Friend NotInheritable Class ExchangeDeviceHost

    Private Sub New()
    End Sub

    Private Shared ReadOnly Gate As New Object()
    Private Shared _agent As ExchangeDeviceAgent

    ''' <summary>The ports of a real agent: the defaults plus the producer of ticket 170 (constructing it reads and writes nothing).</summary>
    Friend Shared Function ProductionPorts() As ExchangeAgentPorts
        Return New ExchangeAgentPorts With {.Producer = ExchangePublisher.CreateDefault()}
    End Function

    ''' <summary>The agent, or Nothing when none was started (the dormant state).</summary>
    Public Shared ReadOnly Property Current As ExchangeDeviceAgent
        Get
            SyncLock Gate
                Return _agent
            End SyncLock
        End Get
    End Property

    ''' <summary>The Share Manager's start. With the gate shut, or no account configured, NO agent is constructed: no connection, no pipe,
    ''' no registry value, no file. The decision and the construction are injectable so the dormant test passes counting fakes.</summary>
    Public Shared Function StartIfConfigured(enabled As Boolean, ports As ExchangeAgentPorts, create As Func(Of ExchangeAgentPorts, ExchangeDeviceAgent)) As ExchangeDeviceAgent
        If Not enabled Then Return Nothing
        If ports Is Nothing OrElse Not ports.Accounts.IsConfigured() Then Return Nothing
        Dim agent As ExchangeDeviceAgent = create(ports)
        SyncLock Gate
            _agent = agent
        End SyncLock
        agent.Start()
        Return agent
    End Function

    ''' <summary>The production start, called from the tray context.</summary>
    Public Shared Sub StartIfConfigured()
        Try
            ' with the gate shut nothing is even constructed
            If Not ExchangeFeatures.IsEnabled() Then Return
            StartIfConfigured(True, ProductionPorts(), Function(p) New ExchangeDeviceAgent(p))
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is InvalidOperationException
            AppFileLogger.WriteLine("Exchange agent: could not start - " & ex.GetType().Name)
        End Try
    End Sub

    ''' <summary>For the "Exchange account" group: the agent, created the first time someone signs in. Only when the gate is open.</summary>
    Public Shared Function GetOrCreate() As ExchangeDeviceAgent
        If Not ExchangeFeatures.IsEnabled() Then Return Nothing
        SyncLock Gate
            If _agent Is Nothing Then _agent = New ExchangeDeviceAgent(ProductionPorts())
            Return _agent
        End SyncLock
    End Function

    ''' <summary>At exit: `bye`, then everything closed.</summary>
    Public Shared Sub StopAtExit()
        Dim a As ExchangeDeviceAgent
        SyncLock Gate
            a = _agent
            _agent = Nothing
        End SyncLock
        If a Is Nothing Then Return
        Try
            Task.Run(Function() a.StopAsync()).Wait(4000)
        Catch
        End Try
        a.Dispose()
    End Sub

End Class
