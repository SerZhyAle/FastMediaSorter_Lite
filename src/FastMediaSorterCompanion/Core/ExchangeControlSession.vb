Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 180 T-2 (section 5): the control stream of the device agent and its state machine.
'
' ONE control stream per device, owned by the agent; its life is the device's presence. The loop:
'   hello (the stored token) -> welcome -> [republish hook] -> list -> subscribe -> read loop;
'   a keepalive at the accepted interval, a missing echo for three intervals is a dead stream;
'   reconnect with bounded backoff (1, 2, 4, 8, 16, 30 s with jitter), reset by a welcome;
'   a REFUSAL is not retried (ExchangeRefusalPolicy): bad-credentials and device-revoked stop and say "sign in again", a
'   pin mismatch stops with a named state and NEVER re-pins, version-unsupported stops with "update", rate-limited backs
'   off for a minute, capacity retries slowly. Nothing is a retry storm and nothing is silent: every state change is an event.
'
' What this class never does: log or print a token, an `access`, a pairing code or a password; open a second control
' stream; speak a plain stream. It holds the clock and the delay as injected functions so the whole machine is tested
' with short intervals against a stub server.

''' <summary>Hooks the agent gives the session. Both are called from the session's own tasks.</summary>
Friend Interface IExchangeSessionHooks
    ''' <summary>After every `welcome` and before `list`: republish this device's resources and broadcasts (items G and H of
    ''' section 14.5 - `access` and `shareId` live in memory on the server, so everything is published again).</summary>
    Function OnWelcomeAsync(session As ExchangeControlSession, ct As CancellationToken) As Task

    ''' <summary>An envelope the session does not handle itself (`open`, `cast-offer`, `cast-result`, `published`, ..).</summary>
    Sub OnMessage(session As ExchangeControlSession, message As ExchangeEnvelope)
End Interface

Friend NotInheritable Class ExchangeControlOptions

    Public Property Server As ExchangeServerAddress
    ''' <summary>The pin the user accepted ("" when the platform validates the certificate).</summary>
    Public Property Pin As String = ""
    Public Property Credentials As ExchangeDeviceCredentials
    Public Property ProductVersion As String = ""
    Public Property Transport As IExchangeTransport = ExchangeTlsTransport.Instance
    ''' <summary>One keepalive "second" in milliseconds: 1000 in production; tests shorten it.</summary>
    Public Property KeepaliveUnitMs As Integer = 1000
    ''' <summary>The backoff before the n-th retry (0 is the first). The default is the jittered table of section 5.</summary>
    Public Property Backoff As Func(Of Integer, TimeSpan) = Function(attempt) ExchangeBackoff.Delay(attempt, Random.Shared.NextDouble())
    ''' <summary>The monotonic clock in milliseconds.</summary>
    Public Property Clock As Func(Of Long) = Function() Environment.TickCount64
    ''' <summary>Waits; the production one is Task.Delay.</summary>
    Public Property Delay As Func(Of TimeSpan, CancellationToken, Task) = Function(t, ct) Task.Delay(t, ct)
    ''' <summary>A line for the log. The session passes only names, states and ids already reduced to their log form - never a
    ''' token, an `access`, a pairing code or a password.</summary>
    Public Property Log As Action(Of String) = Sub(line) Return

End Class

Friend NotInheritable Class ExchangePairingResult

    Public ReadOnly Succeeded As Boolean
    Public ReadOnly Code As String
    ''' <summary>`expiresAt` as the server sent it (UTC ISO 8601).</summary>
    Public ReadOnly ExpiresAt As String
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>False when there was no answer at all (not connected, or the stream died).</summary>
    Public ReadOnly Answered As Boolean

    Public Sub New(succeeded As Boolean, code As String, expiresAt As String, refusal As ExchangeRefusal, answered As Boolean)
        Me.Succeeded = succeeded
        Me.Code = If(code, "")
        Me.ExpiresAt = If(expiresAt, "")
        Me.Refusal = refusal
        Me.Answered = answered
    End Sub

    ''' <summary>A pairing code is a secret until used: never printed.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangePairingResult"
    End Function

End Class

Friend NotInheritable Class ExchangeControlSession

    Private ReadOnly _options As ExchangeControlOptions
    Private ReadOnly _hooks As IExchangeSessionHooks
    Private ReadOnly _gate As New Object()
    Private ReadOnly _model As New ExchangeDirectoryModel()
    Private ReadOnly _requestGate As New SemaphoreSlim(1, 1)

    Private _state As ExchangeAgentState = ExchangeAgentState.Stopped
    Private _lastReason As String = ""
    Private _features As IReadOnlyList(Of String) = Array.Empty(Of String)()
    Private _writer As ExchangeFrameWriter
    Private _pending As TaskCompletionSource(Of ExchangeEnvelope)
    Private _pendingTypes As String()
    Private _lastEcho As Long
    Private _publicEndpoint As String = ""

    ''' <summary>The state or its reason changed (raised on the session's own thread).</summary>
    Public Event StateChanged As EventHandler
    ''' <summary>The mirrored directory moved a revision.</summary>
    Public Event DirectoryChanged As EventHandler
    ''' <summary>The server said `device-revoked`: the token must be thrown away.</summary>
    Public Event DeviceRevoked As EventHandler

    Public Sub New(options As ExchangeControlOptions, hooks As IExchangeSessionHooks)
        If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
        _options = options
        _hooks = hooks
    End Sub

    Public ReadOnly Property State As ExchangeAgentState
        Get
            SyncLock _gate
                Return _state
            End SyncLock
        End Get
    End Property

    ''' <summary>The wire reason or a named cause of the last stop or wait ("" when none): `bad-credentials`, `pin-mismatch`, ..</summary>
    Public ReadOnly Property LastReason As String
        Get
            SyncLock _gate
                Return _lastReason
            End SyncLock
        End Get
    End Property

    ''' <summary>What the last `welcome` said the server offers; the agent hides what is absent.</summary>
    Public ReadOnly Property Features As IReadOnlyList(Of String)
        Get
            SyncLock _gate
                Return _features
            End SyncLock
        End Get
    End Property

    ''' <summary>The `host:port` the operator configured as the address every client reaches the server by.</summary>
    Public ReadOnly Property PublicEndpoint As String
        Get
            SyncLock _gate
                Return _publicEndpoint
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Model As ExchangeDirectoryModel
        Get
            Return _model
        End Get
    End Property

    ''' <summary>The server, the pin, the credentials and the transport this session was made with. Ticket 170: the producer opens its
    ''' tunnel (data) streams to the same server with the same token; a data stream never conflicts with the control stream. A secret
    ''' holder (the credentials): never printed.</summary>
    Public ReadOnly Property ConnectionOptions As ExchangeControlOptions
        Get
            Return _options
        End Get
    End Property

    Private Sub SetState(newState As ExchangeAgentState, reason As String)
        Dim changed As Boolean
        SyncLock _gate
            changed = (_state <> newState) OrElse Not String.Equals(_lastReason, reason, StringComparison.Ordinal)
            _state = newState
            _lastReason = reason
        End SyncLock
        If changed Then RaiseEvent StateChanged(Me, EventArgs.Empty)
    End Sub

    ' --- the loop ---

    ''' <summary>Runs until cancelled or until a refusal that is not retried. <paramref name="adopted"/> is an already
    ''' authenticated stream (the one `enroll` just opened), used for the first connection instead of a `hello`.</summary>
    Public Async Function RunAsync(ct As CancellationToken, Optional adopted As ExchangeOpenResult = Nothing) As Task
        Dim attempt As Integer = 0
        Dim pendingAdopted As ExchangeOpenResult = adopted
        Try
            While Not ct.IsCancellationRequested
                SetState(ExchangeAgentState.Connecting, "")
                Dim open As ExchangeOpenResult
                If pendingAdopted IsNot Nothing Then
                    open = pendingAdopted
                    pendingAdopted = Nothing
                Else
                    open = Await ExchangeSessions.HelloAsync(_options.Transport, _options.Server, _options.Pin, _options.Credentials,
                                                             _options.ProductVersion, ExchangeWire.ProposedKeepaliveSeconds, ct).ConfigureAwait(False)
                End If

                Dim waitFor As TimeSpan = TimeSpan.Zero
                Select Case open.Outcome
                    Case ExchangeOpenOutcome.Connected
                        attempt = 0
                        Await RunConnectedAsync(open, ct).ConfigureAwait(False)
                        If ct.IsCancellationRequested Then Exit While
                        waitFor = _options.Backoff.Invoke(attempt)
                        attempt += 1
                        SetState(ExchangeAgentState.Reconnecting, "lost")
                    Case ExchangeOpenOutcome.Refused
                        Dim decision As ExchangeRefusalDecision = ExchangeRefusalPolicy.ForHello(open.Refusal)
                        Dim reason As String = ExchangeRefusals.WireName(open.Refusal)
                        If reason.Length = 0 Then reason = "refused"
                        If open.Refusal = ExchangeRefusal.DeviceRevoked Then RaiseEvent DeviceRevoked(Me, EventArgs.Empty)
                        If decision.Stops Then
                            SetState(decision.State, reason)
                            Return
                        End If
                        SetState(ExchangeAgentState.Reconnecting, reason)
                        waitFor = TimeSpan.FromSeconds(decision.RetryAfterSeconds)
                    Case ExchangeOpenOutcome.PinMismatch
                        SetState(ExchangeAgentState.PinMismatch, "pin-mismatch")
                        Return
                    Case ExchangeOpenOutcome.Untrusted
                        SetState(ExchangeAgentState.PinMismatch, "untrusted")
                        Return
                    Case ExchangeOpenOutcome.Cancelled
                        Exit While
                    Case Else
                        ' no connection, a failed handshake, a stream that closed or spoke nonsense: back off and try again
                        SetState(ExchangeAgentState.Reconnecting, open.Outcome.ToString())
                        waitFor = _options.Backoff.Invoke(attempt)
                        attempt += 1
                End Select

                Try
                    Await _options.Delay.Invoke(waitFor, ct).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    Exit While
                End Try
            End While
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            ' A fault this class did not expect is a defect of this program, never a silent end: logged by type, the state says so, nothing retries.
            _options.Log.Invoke("control session: unexpected " & ex.GetType().Name)
            SetState(ExchangeAgentState.Defect, "unexpected")
            Return
        Finally
            ClearConnection()
        End Try
        SetState(ExchangeAgentState.Stopped, "")
    End Function

    Private Sub ClearConnection()
        SyncLock _gate
            _writer = Nothing
            _features = Array.Empty(Of String)()
        End SyncLock
        Dim p As TaskCompletionSource(Of ExchangeEnvelope) = Interlocked.Exchange(_pending, Nothing)
        If p IsNot Nothing Then p.TrySetCanceled()
        _model.Clear()
    End Sub

    Private Async Function RunConnectedAsync(open As ExchangeOpenResult, ct As CancellationToken) As Task
        Using open
            Dim welcome As ExchangeEnvelope = open.Welcome
            Dim seconds As Integer = CInt(Math.Max(1L, welcome.Number("keepaliveSeconds")))
            Dim intervalMs As Long = CLng(seconds) * _options.KeepaliveUnitMs
            SyncLock _gate
                _writer = open.Writer
                _features = New List(Of String)(welcome.TextList("features"))
                _publicEndpoint = welcome.Text("publicEndpoint")
            End SyncLock
            _lastEcho = _options.Clock.Invoke()
            SetState(ExchangeAgentState.Online, "")

            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                Dim reading As Task = ReadLoopAsync(open, linked.Token)
                Dim watching As Task = KeepaliveLoopAsync(open.Writer, intervalMs, linked.Token)
                Try
                    ' items G and H: publish everything again after every welcome, then list, then subscribe
                    If _hooks IsNot Nothing Then Await _hooks.OnWelcomeAsync(Me, linked.Token).ConfigureAwait(False)
                    Await open.Writer.WriteAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.List), linked.Token).ConfigureAwait(False)
                    Await open.Writer.WriteAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.Subscribe), linked.Token).ConfigureAwait(False)
                    Await Task.WhenAny(reading, watching).ConfigureAwait(False)
                Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException
                    ' the stream is gone
                Catch ex As Exception
                    ' a fault in a hook must end the connection (and be reconnected by the loop), never the agent
                    _options.Log.Invoke("control session: hook failed - " & ex.GetType().Name)
                Finally
                    linked.Cancel()
                    ClearConnection()
                End Try
                Try
                    Await Task.WhenAll(reading, watching).ConfigureAwait(False)
                Catch
                End Try
            End Using
        End Using
    End Function

    Private Async Function ReadLoopAsync(open As ExchangeOpenResult, ct As CancellationToken) As Task
        Try
            While Not ct.IsCancellationRequested
                Dim env As ExchangeEnvelope = Await open.Reader.ReadAsync(ct).ConfigureAwait(False)
                If Not env.IsKnown Then Continue While   ' an unknown type is ignored (7.1)
                Handle(env)
            End While
        Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException
            ' ends the connection
        End Try
    End Function

    Private Sub Handle(env As ExchangeEnvelope)
        Select Case env.Type
            Case ExchangeEnvelopeTypes.Keepalive
                _lastEcho = _options.Clock.Invoke()
                Return
            Case ExchangeEnvelopeTypes.Directory
                _model.ApplyDirectory(env)
                RaiseEvent DirectoryChanged(Me, EventArgs.Empty)
                Return
            Case ExchangeEnvelopeTypes.Changed
                Select Case _model.ApplyChanged(env)
                    Case ExchangeDirectoryApply.Applied
                        RaiseEvent DirectoryChanged(Me, EventArgs.Empty)
                    Case ExchangeDirectoryApply.Gap
                        ' a gap in the revisions: take the whole directory again
                        Dim w As ExchangeFrameWriter = CurrentWriter()
                        If w IsNot Nothing Then
                            Dim t As Task = w.WriteAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.List), CancellationToken.None)
                        End If
                End Select
                Return
        End Select

        ' an answer somebody waits for
        Dim pending As TaskCompletionSource(Of ExchangeEnvelope) = _pending
        Dim types As String() = _pendingTypes
        If pending IsNot Nothing AndAlso types IsNot Nothing Then
            If env.Is(ExchangeEnvelopeTypes.Refused) OrElse Array.IndexOf(types, env.Type) >= 0 Then
                If pending.TrySetResult(env) Then Return
            End If
        End If
        If _hooks IsNot Nothing Then _hooks.OnMessage(Me, env)
    End Sub

    Private Function CurrentWriter() As ExchangeFrameWriter
        SyncLock _gate
            Return _writer
        End SyncLock
    End Function

    Private Async Function KeepaliveLoopAsync(writer As ExchangeFrameWriter, intervalMs As Long, ct As CancellationToken) As Task
        Dim tickMs As Integer = CInt(Math.Max(1L, intervalMs \ 2))
        Dim sinceSend As Long = 0
        Try
            While Not ct.IsCancellationRequested
                Await _options.Delay.Invoke(TimeSpan.FromMilliseconds(tickMs), ct).ConfigureAwait(False)
                ' three intervals without an echo is a dead stream (7.3)
                If _options.Clock.Invoke() - _lastEcho > intervalMs * ExchangeWire.KeepaliveWindowIntervals Then Return
                sinceSend += tickMs
                If sinceSend >= intervalMs Then
                    sinceSend = 0
                    Await writer.WriteAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.Keepalive), ct).ConfigureAwait(False)
                End If
            End While
        Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException
            ' ends the connection
        End Try
    End Function

    ' --- requests of the agent ---

    ''' <summary>Sends one envelope that has no answer (`revoke`, `unpublish`, `bye`, ..). False when there is no stream.</summary>
    Public Async Function SendAsync(env As ExchangeEnvelope, ct As CancellationToken) As Task(Of Boolean)
        Dim w As ExchangeFrameWriter = CurrentWriter()
        If w Is Nothing Then Return False
        Try
            Await w.WriteAsync(env, ct).ConfigureAwait(False)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException
            Return False
        End Try
    End Function

    ''' <summary>Sends an envelope and waits for the next one of the given types, or `refused`. Nothing when there is no stream,
    ''' no answer in time, or the stream died.</summary>
    Public Async Function RequestAsync(env As ExchangeEnvelope, expectedTypes As String(), timeoutMs As Integer, ct As CancellationToken) As Task(Of ExchangeEnvelope)
        Await _requestGate.WaitAsync(ct).ConfigureAwait(False)
        Try
            Dim w As ExchangeFrameWriter = CurrentWriter()
            If w Is Nothing Then Return Nothing
            Dim tcs As New TaskCompletionSource(Of ExchangeEnvelope)(TaskCreationOptions.RunContinuationsAsynchronously)
            _pendingTypes = expectedTypes
            _pending = tcs
            Try
                Await w.WriteAsync(env, ct).ConfigureAwait(False)
                Using deadline As New CancellationTokenSource(timeoutMs)
                    Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token)
                        Using linked.Token.Register(Sub() tcs.TrySetCanceled())
                            Return Await tcs.Task.ConfigureAwait(False)
                        End Using
                    End Using
                End Using
            Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException
                Return Nothing
            Finally
                _pending = Nothing
                _pendingTypes = Nothing
            End Try
        Finally
            _requestGate.Release()
        End Try
    End Function

    ''' <summary>`pairing-request` -> `pairing-code` (or a refusal): the code another device enrolls with, valid until `expiresAt`.</summary>
    Public Async Function RequestPairingCodeAsync(ct As CancellationToken) As Task(Of ExchangePairingResult)
        Dim reply As ExchangeEnvelope = Await RequestAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.PairingRequest),
                                                           {ExchangeEnvelopeTypes.PairingCode}, 10000, ct).ConfigureAwait(False)
        If reply Is Nothing Then Return New ExchangePairingResult(False, "", "", ExchangeRefusal.Unknown, False)
        If reply.Is(ExchangeEnvelopeTypes.Refused) Then Return New ExchangePairingResult(False, "", "", reply.Refusal(), True)
        Return New ExchangePairingResult(True, reply.Text("code"), reply.Text("expiresAt"), ExchangeRefusal.Unknown, True)
    End Function

    ''' <summary>`revoke`: the token of another device (or this one) dies; the change arrives as `changed`.</summary>
    Public Function RevokeAsync(deviceId As String, ct As CancellationToken) As Task(Of Boolean)
        Return SendAsync(ExchangeMessages.Revoke(deviceId), ct)
    End Function

    ''' <summary>`bye`: going offline on purpose, no answer. Best effort, bounded.</summary>
    Public Async Function SayByeAsync() As Task
        Using cts As New CancellationTokenSource(1500)
            Try
                Await SendAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.Bye), cts.Token).ConfigureAwait(False)
            Catch ex As OperationCanceledException
            End Try
        End Using
    End Function

End Class
