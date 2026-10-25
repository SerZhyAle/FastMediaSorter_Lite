#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Net
Imports System.Threading

' Ticket 199 sections 3, 5, 6: the producer session - the one object the "Live broadcast" window and
' the tray talk to. Everything external is injected (the gate, the address candidates, the settings
' store, the source and encoder factories, the clock), so the suite drives whole broadcasts with fakes
' and no test needs a device, a network or a process.
'
' The rules this class owns:
'   - safety rule 1: a broadcast starts only by the user's explicit act - Start() is called from one
'     button, never from a cast, the network or logon;
'   - one live broadcast per device at a time (the sourceId rule): Start() refuses while running;
'   - producer rule 1: no publishable LAN address (and no exchange in this ticket) means the start is
'     refused, NEITHER source opens and no descriptor exists;
'   - the port is the user's setting with no fallback: a taken port refuses the start;
'   - safety rule 3: no recording - the hub's two seconds in memory are the only copy;
'   - safety rule 6: the gate - nothing is read, opened or written while it is shut.

''' <summary>What is being captured (section 4; the camera is phase B3).</summary>
Friend Enum LiveProducerSourceKind
    Microphone = 0
    SystemAudio = 1
    Screen = 2
    Camera = 3
End Enum

''' <summary>Why a start was refused - one sentence per reason in the window, never a raw error.</summary>
Friend Enum LiveStartRefusal
    None = 0
    ''' <summary>The gate is shut (safety rule 6): nothing was opened, read or written.</summary>
    GateShut = 1
    ''' <summary>One live broadcast per device at a time (section 2).</summary>
    AlreadyRunning = 2
    ''' <summary>Producer rule 1: no reachable LAN address.</summary>
    NoLanAddress = 3
    ''' <summary>The user's port is taken; there is no fallback port (2.1).</summary>
    PortTaken = 4
    ''' <summary>The encoder engine could not start (the pinned FFmpeg runtime is missing).</summary>
    EncoderUnavailable = 5
    ''' <summary>Windows did not grant the capture, or the device is gone (safety rule 4).</summary>
    SourceUnavailable = 6
End Enum

Friend Class LiveStartRequest
    Public Property Kind As LiveProducerSourceKind = LiveProducerSourceKind.Microphone
    ''' <summary>The mode string of the descriptor: AUDIO_ONLY, VIDEO_AUDIO, VIDEO_ONLY.</summary>
    Public Property Mode As String = "AUDIO_ONLY"
    Public Property Title As String = ""
    Public Property Password As String = ""
    Public Property BroadcastId As String = ""
End Class

Friend Class LiveStartResult
    Public Property Ok As Boolean
    Public Property Refusal As LiveStartRefusal = LiveStartRefusal.None
    ''' <summary>The raw reason of the failing seam (the encoder's or the source's own words); the window
    ''' shows one sentence per refusal kind, never this raw.</summary>
    Public Property SeamReason As String

    Public Shared Function Failed(refusal As LiveStartRefusal, Optional seamReason As String = "") As LiveStartResult
        Return New LiveStartResult With {.Ok = False, .Refusal = refusal, .SeamReason = seamReason}
    End Function
End Class

''' <summary>The state the window shows while on air (safety rule 2).</summary>
Friend Class LiveProducerState
    Public Property Running As Boolean
    Public Property Kind As LiveProducerSourceKind
    Public Property Mode As String = ""
    Public Property Url As String = ""
    Public Property Address As String = ""
    Public Property Port As Integer
    ''' <summary>The descriptor the person HANDS OUT (link, barcode, file): with no password it carries no RELAY and no TUNNEL
    ''' (LIVE-BROADCAST item M, ticket 239 CR-15), and it is "" when nothing else is reachable - see <see cref="HandOffWithheld"/>.
    ''' The descriptor of the account's own directory record is <see cref="RecordDescriptorJson"/>.</summary>
    Public Property DescriptorJson As String = ""
    ''' <summary>The barcode text of <see cref="DescriptorJson"/> ("" when there is nothing to hand out).</summary>
    Public Property BarcodeText As String = ""
    ''' <summary>True when RELAY / TUNNEL endpoints exist for this broadcast but were left out of the hand-out because it has no
    ''' password. With <see cref="DescriptorJson"/> empty as well, the hand-off actions say so and ask for a password.</summary>
    Public Property HandOffWithheld As Boolean
    Public Property SourceName As String = ""
    Public Property Listeners As Integer
    Public Property MaxListeners As Integer
    Public Property VideoToken As String = ""
    Public Property BroadcastId As String = ""
    Public Property Password As String = ""
    Public Property RelayUrl As String = ""
    Public Property TunnelUrl As String = ""
    Public Property ListenerAddress As String = ""
    ''' <summary>True when a LAN endpoint of this broadcast is really on air (a LAN address was picked and, for video, the LAN listener
    ''' is on): the listener is then bound to that address. False when the broadcast is reachable through the exchange alone.</summary>
    Public Property LanEndpointOn As Boolean
    ''' <summary>True when the RELAY / TUNNEL endpoints of this broadcast are sealed (DEVICE-EXCHANGE items DK, DL): the agent then
    ''' uploads sealed records, never clear bytes. <see cref="DescriptorJson"/> is then the OUTWARD descriptor (keys in its endpoints).</summary>
    Public Property Sealed As Boolean
    ''' <summary>The descriptor of the directory record: the same endpoints, `seal: {v}` and no key. Empty when the descriptor needs no split
    ''' (neither sealed nor shortened for the hand-out, <see cref="HandOffWithheld"/>).</summary>
    Public Property RecordDescriptorJson As String = ""
    ''' <summary>The user name of a protected broadcast's credentials ("" when it is open); the password is <see cref="Password"/>.</summary>
    Public Property AuthUser As String = ""
    ''' <summary>Ticket 263 T-3 (CR-14, DEVICE-EXCHANGE items BF and DV): the credential pairs of this broadcast - the link pair (user
    ''' `listener`, <see cref="Password"/>) and the own pair for the account's own devices (user `own`, carried in the sealed box). The
    ''' SAME object feeds the producer's Digest and the `verifiers` the agent sends with every `broadcast-start` and `broadcast-update`
    ''' (<see cref="LiveCredentialPairs.VerifierRecords"/>, `[]` for a set with no pair). Never Nothing; empty when the link has no password.</summary>
    Public Property Credentials As LiveCredentialPairs = LiveCredentialPairs.None()

    ''' <summary>The broadcast's start, UTC (DEVICE-EXCHANGE 6.3 <c>startedAt</c>): stable for the life of the
    ''' broadcast. A restarted broadcast gets a new state and a new <see cref="BroadcastId"/> with it, and the
    ''' later start is what orders two records of one sourceId (LIVE-BROADCAST item G as DEVICE-EXCHANGE
    ''' item BP resolves it; <c>updatedAt</c> stays informational).</summary>
    Public Property StartedAtUtc As Date = Date.UtcNow
End Class

''' <summary>Every external fact the controller needs, injected.</summary>
Friend Class LiveProducerPorts
    ''' <summary>The gate: LiveBroadcastFeatures.IsEnabled() in the Share Manager, faked in tests.</summary>
    Public Property GateEnabled As Func(Of Boolean)
    ''' <summary>The one address this broadcast publishes: the host wires the scored pick
    ''' (NetworkInfo's candidates through LiveProducerAddressPolicy.ChooseAddress); a test wires its own.
    ''' The controller still refuses an address that could not be published (loopback, unspecified,
    '' link-local), so no wiring can accidentally violate producer rule 1.</summary>
    Public Property PickAddress As Func(Of String)
    ''' <summary>The settings store reader (the hive), null when nothing should be read.</summary>
    Public Property LoadSettings As Func(Of LiveProducerSettings)
    ''' <summary>The enrolled device id of the exchange agent, or ""/Nothing when not enrolled.</summary>
    Public Property EnrolledDeviceId As Func(Of String)
    Public Property ReadStored As Func(Of String, String)
    Public Property WriteStored As Action(Of String, String)
    Public Property CreateEncoder As Func(Of ILiveEncoder)
    Public Property CreateAudioSource As Func(Of LiveProducerSourceKind, ILiveAudioSource)
    ''' <summary>The video source of the kind asked for: the screen source or the camera source
    ''' (B3) - one seam for both, so the host picks by kind and the suite drives either with fakes.</summary>
    Public Property CreateVideoSource As Func(Of LiveProducerSourceKind, ILiveVideoSource)
    Public Property ClockMs As Func(Of Long)
    ''' <summary>The opaque token of the video path (item H); a fresh one per broadcast.</summary>
    Public Property NewToken As Func(Of String)
    ''' <summary>Generates a fresh 16-byte broadcast identifier.</summary>
    Public Property NewId As Func(Of String)
    ''' <summary>Returns (publicEndpoint, certFingerprint) from the connected exchange agent, or Nothing.</summary>
    Public Property ExchangeEndpointInfo As Func(Of Tuple(Of String, String))
    ''' <summary>Notifies when broadcast starts with its state and hubs.</summary>
    Public Property BroadcastStarted As Action(Of LiveProducerState, LiveBroadcastHub, LiveBroadcastHub)
    ''' <summary>Notifies when broadcast stops with its broadcastId.</summary>
    Public Property BroadcastStopped As Action(Of String)
    ''' <summary>Ticket 263 T-6: the endpoints of the running broadcast changed; publish the current record without restarting capture.</summary>
    Public Property BroadcastChanged As Action(Of LiveProducerState)
    ''' <summary>The sealed layer of the exchange agent (spec 234, items DK and DL), or Nothing when none is wired: the descriptor is
    ''' then built without `seal`, as before. When set, a RELAY or TUNNEL endpoint is listed only while the layer
    ''' <see cref="ILiveBroadcastSealing.CanSealThroughServer"/>, and each carries `seal`.</summary>
    Public Property Sealing As ILiveBroadcastSealing
End Class

Friend Class LiveBroadcastController

    Private ReadOnly _ports As LiveProducerPorts
    Private _state As New LiveProducerState()
    Private ReadOnly _stateLock As New Object()

    Private _audioSource As ILiveAudioSource
    Private _videoSource As ILiveVideoSource
    Private _endingSource As ILiveSourceEnding
    Private _endingHandler As Action(Of String)
    Private _encoder As ILiveEncoder
    Private _audioHub As LiveBroadcastHub
    Private _videoHub As LiveBroadcastHub
    Private _server As LiveBroadcastServer
    Private _encodingVideo As Boolean
    Private _settings As LiveProducerSettings
    Private _serverConfig As LiveServerConfig
    Private _descriptorInput As LiveDescriptorInput

    ''' <summary>The window's live level meter: the peak of each chunk, after the producer's gain.</summary>
    Public Event AudioLevel As Action(Of Double)

    ''' <summary>Listener count changed (the window's "how many listeners" line, safety rule 2).</summary>
    Public Event ListenersChanged As EventHandler

    ''' <summary>Ticket 263 T-2: the video source ended on its own (the captured window was closed) and the
    ''' controller has ALREADY stopped the broadcast through its ordinary stop. The argument is the source's
    ''' reason code; the window shows one sentence. Raised on a thread-pool thread, never on the UI thread.</summary>
    Public Event SourceEnded As Action(Of String)
    Public Event EndpointsChanged As EventHandler

    Public Sub New(ports As LiveProducerPorts)
        _ports = ports
    End Sub

    Public ReadOnly Property State As LiveProducerState
        Get
            Return _state
        End Get
    End Property

    ''' <summary>Starts one broadcast. Returns a refusal - never throws - for every rule of sections
    ''' 3, 5 and 6; on a refusal NOTHING stays open (no listener, no process, no descriptor).</summary>
    Public Function Start(request As LiveStartRequest) As LiveStartResult
        If request Is Nothing Then Return LiveStartResult.Failed(LiveStartRefusal.SourceUnavailable)
        If _ports Is Nothing OrElse _ports.GateEnabled Is Nothing Then Return LiveStartResult.Failed(LiveStartRefusal.GateShut)

        ' Safety rule 6, the first check of all: while the gate is shut nothing is read or opened.
        If Not _ports.GateEnabled.Invoke() Then Return LiveStartResult.Failed(LiveStartRefusal.GateShut)

        SyncLock _stateLock
            If _state.Running Then Return LiveStartResult.Failed(LiveStartRefusal.AlreadyRunning)
            Try
                Dim address As String = If(_ports.PickAddress?.Invoke(), "")
                Dim hasLanAddress As Boolean = LiveProducerAddressPolicy.IsPublishablePicked(address)
                Dim exchangeInfo As Tuple(Of String, String) = _ports.ExchangeEndpointInfo?.Invoke()
                ' DEVICE-EXCHANGE item DP: with a sealing layer wired, nothing is offered through a server that cannot seal (or
                ' while not connected) - no RELAY, no TUNNEL, no clear fallback. What is left is the LAN.
                If _ports.Sealing IsNot Nothing AndAlso exchangeInfo IsNot Nothing AndAlso Not _ports.Sealing.CanSealThroughServer Then exchangeInfo = Nothing
                If Not hasLanAddress AndAlso (exchangeInfo Is Nothing OrElse String.IsNullOrEmpty(exchangeInfo.Item1)) Then
                    ' Producer rule 1: neither source opens and no descriptor exists.
                    Return LiveStartResult.Failed(LiveStartRefusal.NoLanAddress)
                End If
                ' LIVE-BROADCAST item J: preserve the inner scheme/path on the documentation host
                ' when only exchange transports are reachable; never publish loopback as a LAN URL.
                If Not hasLanAddress Then address = LiveProducerAddressPolicy.DocumentationHost

                _settings = _ports.LoadSettings?.Invoke()
                If _settings Is Nothing Then _settings = New LiveProducerSettings()

                Dim kind As LiveProducerSourceKind = request.Kind
                Dim isVideo As Boolean = String.Equals(request.Mode, "VIDEO_AUDIO", StringComparison.OrdinalIgnoreCase) OrElse
                                          String.Equals(request.Mode, "VIDEO_ONLY", StringComparison.OrdinalIgnoreCase)

                Dim broadcastId As String = request.BroadcastId
                If String.IsNullOrEmpty(broadcastId) Then
                    broadcastId = If(_ports.NewId?.Invoke(), "")
                    If String.IsNullOrEmpty(broadcastId) Then broadcastId = LiveProducerTokens.NewId()
                End If

                Dim publicEndpoint As String = If(exchangeInfo IsNot Nothing, exchangeInfo.Item1, "")
                Dim certFingerprint As String = If(exchangeInfo IsNot Nothing, exchangeInfo.Item2, "")
                Dim relayUrl As String = ""
                Dim tunnelUrl As String = ""
                If Not String.IsNullOrEmpty(publicEndpoint) Then
                    ' Ticket 263 T-3 (CR-14, item BF): the relay now checks Basic credentials against the `verifiers` the agent uploads
                    ' with `broadcast-start` / `-update`, so a password no longer keeps the broadcast off the relay by itself. The safety
                    ' rule that remains: a PROTECTED broadcast is offered the relay only when it is SEALED (a sealing layer is wired,
                    ' and the exchangeInfo test above already dropped the server when it cannot seal). Then the relay carries
                    ' ciphertext, and the verifiers are a second factor even if one server build ignored them (a server that does not
                    ' know `verifiers` drops the member silently). With no sealing layer the relay would carry the CLEAR stream and
                    ' nothing but the verifiers would stand between it and a listener - so RELAY stays withheld exactly as before and
                    ' the Digest runs over the TUNNEL alone. A broadcast with no password has no verifiers to depend on.
                    If String.IsNullOrEmpty(request.Password) OrElse _ports.Sealing IsNot Nothing Then
                        relayUrl = "https://" & publicEndpoint & "/v2/b/" & broadcastId & "/stream"
                    End If
                    tunnelUrl = "fmsx://" & publicEndpoint & "/b/" & broadcastId & "/http"
                End If

                ' Items DK and DL: the RELAY and TUNNEL endpoints are sealed. The key of the first outward link is made now and goes
                ' only into the descriptor the person hands out; the agent keeps it for the key ring of the upload.
                Dim isSealed As Boolean = _ports.Sealing IsNot Nothing AndAlso (relayUrl.Length > 0 OrElse tunnelUrl.Length > 0)
                Dim relayLinkKey As String = ""
                If isSealed AndAlso relayUrl.Length > 0 Then
                    relayLinkKey = If(_ports.Sealing.NewLinkKey(broadcastId), "")
                    If relayLinkKey.Length = 0 Then relayUrl = ""
                End If
                ' Item DM: the TUNNEL runs TLS inside. Its endpoint is listed only with the pin of the leaf the agent will answer with.
                Dim tunnelPin As String = ""
                If isSealed AndAlso tunnelUrl.Length > 0 Then
                    tunnelPin = If(_ports.Sealing.NewTunnelPin(broadcastId), "")
                    If tunnelPin.Length = 0 Then tunnelUrl = ""
                End If

                ' Section 3.4: a LAN listener for video is off by default and video starts exchange-only
                ' (173). With the tick off and no exchange half built there is no reachable endpoint of any
                ' transport, and producer rule 1 as amended says that means no start - no source opens.
                If isVideo AndAlso Not _settings.AllowLanVideoViewers AndAlso String.IsNullOrEmpty(relayUrl) AndAlso String.IsNullOrEmpty(tunnelUrl) Then
                    Return LiveStartResult.Failed(LiveStartRefusal.NoLanAddress)
                End If

                ' The hubs and the server: the audio listener is on (as on the phone), the LAN video
                ' listener only behind the user's tick (3.4, Q199-3).
                _audioHub = New LiveBroadcastHub(New LiveAudioJoinPolicy(), _ports.ClockMs)
                _videoHub = New LiveBroadcastHub(New LiveVideoJoinPolicy(), _ports.ClockMs)

                Dim token As String = ""
                If isVideo Then
                    token = _ports.NewToken?.Invoke()
                    If String.IsNullOrEmpty(token) Then token = LiveProducerTokens.NewToken()
                End If

                ' Ticket 263 T-3: the link pair and the own pair (none at all without a link password, item DV). The verifiers are NOT made
                ' here - 600000 PBKDF2 rounds per pair would hold this call (and the window) for half a second; the agent asks for them
                ' once, on a pool thread, and the set caches them.
                Dim credentials As LiveCredentialPairs = LiveCredentialPairs.ForLink(request.Password)

                Dim serverConfig As New LiveServerConfig With {
                    .IcyName = If(request.Title.Length = 0, Environment.MachineName, request.Title),
                    .IcyGenre = "live",
                    .AudioBitrateKbps = _settings.AudioBitrateKbps,
                    .AudioHub = _audioHub,
                    .VideoAllowed = isVideo,
                    .VideoHub = _videoHub,
                    .VideoToken = token,
                    .Credentials = credentials
                }

                ' A private loopback listener remains reachable by the tunnel, never by LAN clients.
                Dim bindAddress As IPAddress = If(Not hasLanAddress OrElse (isVideo AndAlso Not _settings.AllowLanVideoViewers),
                                                 IPAddress.Loopback, IPAddress.Parse(address))
                _serverConfig = serverConfig
                _server = CreateServer(bindAddress)

                Dim started As LiveServerStartResult = _server.Start()
                If started <> LiveServerStartResult.Ok Then
                    _server.Dispose()
                    _server = Nothing
                    Return LiveStartResult.Failed(LiveStartRefusal.PortTaken)
                End If

                ' The encoder engine, then the source; each failure undoes everything above it.
                _encodingVideo = isVideo
                _encoder = _ports.CreateEncoder?.Invoke()
                Dim encoderReason As String = ""
                If _encoder Is Nothing Then
                    StopOpened()
                    Return LiveStartResult.Failed(LiveStartRefusal.EncoderUnavailable)
                End If

                ' The source first, the encoder second: the source declares the device's real mix format
                ' (WASAPI answers 48 000 Hz stereo more often than not), and the encoder is told those
                ' input numbers while the output keeps the shape's own. Chunks pushed between the two
                ' starts are dropped by the sink - a fraction of a second at the very start of a live
                ' stream, which nobody can hear behind the keyframe join.
                Dim sourceReason As String = ""
                If isVideo Then
                    _videoSource = _ports.CreateVideoSource?.Invoke(kind)
                    If _videoSource Is Nothing OrElse Not _videoSource.Start(AddressOf VideoSink, sourceReason) Then
                        StopOpened()
                        Return LiveStartResult.Failed(LiveStartRefusal.SourceUnavailable, sourceReason)
                    End If
                    ListenForSourceEnd(_videoSource)
                End If
                If Not String.Equals(request.Mode, "VIDEO_ONLY", StringComparison.OrdinalIgnoreCase) Then
                    _audioSource = _ports.CreateAudioSource?.Invoke(kind)
                    If _audioSource IsNot Nothing Then _audioSource.Gain = _settings.MicrophoneGain
                    If _audioSource Is Nothing OrElse Not _audioSource.Start(AddressOf AudioSink, sourceReason) Then
                        StopOpened()
                        Return LiveStartResult.Failed(LiveStartRefusal.SourceUnavailable, sourceReason)
                    End If
                End If

                Dim options As New LiveEncoderOptions With {
                    .SampleRate = _settings.SampleRate,
                    .Channels = If(kind = LiveProducerSourceKind.SystemAudio, _settings.SystemAudioChannels, _settings.MicrophoneChannels),
                    .AudioBitrateKbps = _settings.AudioBitrateKbps,
                    .WithVideo = isVideo,
                    .WithAudio = Not String.Equals(request.Mode, "VIDEO_ONLY", StringComparison.OrdinalIgnoreCase)
                }
                If _audioSource IsNot Nothing Then
                    options.InputSampleRate = _audioSource.SampleRate
                    options.InputChannels = _audioSource.Channels
                End If
                If isVideo Then
                    options.VideoFps = Math.Max(1, Math.Min(30, _videoSource.FramesPerSecond))
                    options.VideoWidth = 1280
                    options.VideoHeight = 720
                    ' The monitor's own size is the engine's INPUT; the shape's 1280x720 is its output and
                    ' the engine scales between them (section 4's "at most 1280x720 by default").
                    If _videoSource IsNot Nothing Then
                        options.InputVideoWidth = _videoSource.Width
                        options.InputVideoHeight = _videoSource.Height
                    End If
                End If

                If Not _encoder.Start(options, AddressOf EncoderOutput, encoderReason) Then
                    StopOpened()
                    Return LiveStartResult.Failed(LiveStartRefusal.EncoderUnavailable, encoderReason)
                End If

                ' The descriptor and the three hand-off channels (section 5), built once the broadcast is
                ' really on air - on a refused start none of this exists.
                Dim sourceId As String = LiveSourceIdentity.Resolve(_ports.EnrolledDeviceId?.Invoke(),
                                                                    _ports.ReadStored, _ports.WriteStored,
                                                                    AddressOf LiveSourceIdentity.NewRandomId)
                ' Item J names both cases: no usable LAN address, and the video LAN listener off. In either the stream has no LAN
                ' endpoint, so `inner` (and the stream URL beside it) keeps the scheme, path, port and token on the documentation
                ' host and a private LAN address never rides inside it. The listener binding above is unchanged (loopback, for the
                ' tunnel); the separate LAN endpoint is written only when the LAN listener is on.
                Dim lanEndpointOn As Boolean = hasLanAddress AndAlso ((Not isVideo) OrElse _settings.AllowLanVideoViewers)
                Dim url As String = LiveProducerAddressPolicy.BuildUrl(If(lanEndpointOn, address, LiveProducerAddressPolicy.DocumentationHost), _settings.AudioPort,
                                                                        If(isVideo, "/" & token & LiveProducerContract.VideoPath, LiveProducerContract.AudioPath))
                Dim input As New LiveDescriptorInput With {
                    .Url = url,
                    .Mode = request.Mode,
                    .Title = request.Title,
                    .SourceId = sourceId,
                    .TargetLatencyMs = If(isVideo, LiveProducerContract.VideoTargetLatencyMs, 1000),
                    .AudioCodec = If(options.WithAudio, "aac", Nothing),
                    .VideoCodec = If(isVideo, "h264", Nothing),
                    .SampleRateHz = _settings.SampleRate,
                    .BitrateBps = _settings.AudioBitrateKbps * 1000L,
                    .Channels = options.Channels,
                    .AllowLanViewers = lanEndpointOn,
                    .RelayUrl = relayUrl,
                    .TunnelUrl = tunnelUrl,
                    .CertFingerprint = certFingerprint,
                    .InnerUrl = url,
                    .AuthUser = If(String.IsNullOrEmpty(request.Password), "", LiveCredentialPairs.LinkUser),
                    .Sealed = isSealed,
                    .Outward = True,
                    .RelayKey = relayLinkKey,
                    .TunnelPin = tunnelPin
                }
                ' The descriptor of the full outward shape carries the link key; the one the directory record carries has `seal: {v}` alone.
                Dim fullJson As String = LiveProducerDescriptorWriter.BuildJson(input)
                Dim withheld As Boolean = LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(input)
                ' LIVE-BROADCAST item M (ticket 239 CR-15): what the person hands out is always protected when it carries a RELAY or a
                ' TUNNEL. With no password the three hand-off channels get the LAN alone ("" when that is unreachable too); the
                ' account's own directory record keeps its endpoints and is built from the same input as before.
                Dim json As String = LiveProducerDescriptorWriter.BuildHandOutJson(input)
                Dim recordJson As String = If(isSealed OrElse withheld, LiveProducerDescriptorWriter.BuildJson(input.ForRecord()), "")
                If isSealed AndAlso (fullJson.Length = 0 OrElse recordJson.Length = 0) Then
                    ' No endpoint of any transport could be handed out (a sealed TUNNEL has no pin before its TLS exists, item DM).
                    StopOpened()
                    Return LiveStartResult.Failed(LiveStartRefusal.NoLanAddress)
                End If

                _state = New LiveProducerState With {
                    .Running = True,
                    .Kind = kind,
                    .Mode = request.Mode,
                    .Url = If(input.AllowLanViewers AndAlso Not String.IsNullOrEmpty(url), url, If(Not String.IsNullOrEmpty(relayUrl), relayUrl, tunnelUrl)),
                    .Address = address,
                    .Port = _settings.AudioPort,
                    .DescriptorJson = json,
                    .BarcodeText = LiveProducerDescriptorWriter.BarcodePayload(json),
                    .SourceName = If(isVideo, _videoSource?.DisplayName, _audioSource?.DisplayName),
                    .Listeners = 0,
                    .MaxListeners = Math.Max(0, Math.Min(10000, _settings.MaxListeners)),
                    .VideoToken = token,
                    .BroadcastId = broadcastId,
                    .LanEndpointOn = lanEndpointOn,
                    .Password = request.Password,
                    .RelayUrl = relayUrl,
                    .TunnelUrl = tunnelUrl,
                    .Sealed = isSealed,
                    .RecordDescriptorJson = recordJson,
                    .HandOffWithheld = withheld,
                    .AuthUser = input.AuthUser,
                    .Credentials = credentials
                }
                _state.ListenerAddress = bindAddress.ToString()
                _descriptorInput = input
                _ports.BroadcastStarted?.Invoke(_state, _audioHub, _videoHub)
                Return New LiveStartResult With {.Ok = True}
            Catch ex As Exception
                StopOpened()
                _state = New LiveProducerState()
                Return LiveStartResult.Failed(LiveStartRefusal.SourceUnavailable, ex.GetType().Name)
            End Try
        End SyncLock
    End Function

    Private Function CreateServer(address As IPAddress) As LiveBroadcastServer
        Dim server As New LiveBroadcastServer(address, _settings.AudioPort, _serverConfig)
        AddHandler server.ListenersChanged,
            Sub(sender, e)
                SyncLock _stateLock
                    If Not ReferenceEquals(_server, server) Then Return
                    _state.Listeners = server.ListenerCount
                End SyncLock
                RaiseEvent ListenersChanged(Me, EventArgs.Empty)
            End Sub
        Return server
    End Function

    ''' <summary>Changes the exchange server's admission cap on air. It applies to its relay and tunnel connections;
    ''' the UI labels that scope explicitly, because LAN viewers do not pass through the exchange server.</summary>
    Public Sub UpdateMaxListeners(value As Integer)
        SyncLock _stateLock
            If Not _state.Running Then Return
            value = Math.Max(0, Math.Min(10000, value))
            If _state.MaxListeners = value Then Return
            _state.MaxListeners = value
            _settings.MaxListeners = value
            _ports.BroadcastChanged?.Invoke(_state)
        End SyncLock
    End Sub

    ''' <summary>Rebinds only the HTTP listener when the selected LAN address or the video LAN choice changes. The hubs,
    ''' encoder, source, broadcast id, keys and start time survive. A failed explicit change keeps the previous setting.</summary>
    Public Function UpdateNetwork(Optional allowLanVideoViewers As Boolean? = Nothing) As LiveStartResult
        SyncLock _stateLock
            If Not _state.Running OrElse _descriptorInput Is Nothing Then Return LiveStartResult.Failed(LiveStartRefusal.SourceUnavailable)
            Dim allowLan As Boolean = If(allowLanVideoViewers, _settings.AllowLanVideoViewers)
            Dim address As String = If(_ports.PickAddress?.Invoke(), "")
            Dim hasLan As Boolean = LiveProducerAddressPolicy.IsPublishablePicked(address)
            Dim lanOn As Boolean = hasLan AndAlso (Not _encodingVideo OrElse allowLan)
            If Not lanOn AndAlso String.IsNullOrEmpty(_state.RelayUrl) AndAlso String.IsNullOrEmpty(_state.TunnelUrl) Then
                If allowLanVideoViewers.HasValue Then Return LiveStartResult.Failed(LiveStartRefusal.NoLanAddress)
                StopOpened()
                _state = New LiveProducerState()
                RaiseEvent SourceEnded("network-unavailable")
                Return LiveStartResult.Failed(LiveStartRefusal.NoLanAddress)
            End If
            Dim bindAddress As IPAddress = If(lanOn, IPAddress.Parse(address), IPAddress.Loopback)
            If Not String.Equals(bindAddress.ToString(), _state.ListenerAddress, StringComparison.Ordinal) Then
                Dim replacement As LiveBroadcastServer = CreateServer(bindAddress)
                Dim result As LiveServerStartResult = replacement.Start()
                If result <> LiveServerStartResult.Ok Then
                    replacement.Dispose()
                    ' Do not keep publishing an address which disappeared. An explicit checkbox failure is reversible.
                    If Not allowLanVideoViewers.HasValue Then
                        StopOpened()
                        _state = New LiveProducerState()
                        RaiseEvent SourceEnded("network-unavailable")
                    End If
                    Return LiveStartResult.Failed(LiveStartRefusal.PortTaken)
                End If
                Dim previous As LiveBroadcastServer = _server
                _server = replacement
                previous?.Dispose()
                _state.ListenerAddress = bindAddress.ToString()
                _state.Listeners = replacement.ListenerCount
            End If
            _settings.AllowLanVideoViewers = allowLan
            Dim publishedAddress As String = If(hasLan, address, LiveProducerAddressPolicy.DocumentationHost)
            If _state.Address = publishedAddress AndAlso _state.LanEndpointOn = lanOn Then Return New LiveStartResult With {.Ok = True}
            _state.Address = publishedAddress
            _state.LanEndpointOn = lanOn
            _descriptorInput.AllowLanViewers = lanOn
            _descriptorInput.Url = LiveProducerAddressPolicy.BuildUrl(If(lanOn, address, LiveProducerAddressPolicy.DocumentationHost), _state.Port,
                If(_encodingVideo, "/" & _state.VideoToken & LiveProducerContract.VideoPath, LiveProducerContract.AudioPath))
            _descriptorInput.InnerUrl = _descriptorInput.Url
            _state.DescriptorJson = LiveProducerDescriptorWriter.BuildHandOutJson(_descriptorInput)
            _state.BarcodeText = LiveProducerDescriptorWriter.BarcodePayload(_state.DescriptorJson)
            _state.RecordDescriptorJson = LiveProducerDescriptorWriter.BuildJson(_descriptorInput.ForRecord())
            _state.Url = If(lanOn, _descriptorInput.Url, If(_state.RelayUrl.Length > 0, _state.RelayUrl, _state.TunnelUrl))
            _ports.BroadcastChanged?.Invoke(_state)
            RaiseEvent EndpointsChanged(Me, EventArgs.Empty)
            Return New LiveStartResult With {.Ok = True}
        End SyncLock
    End Function

    ''' <summary>Stops the broadcast (the window's button, a sign-out, the process ending): the source,
    ''' the encoder, the listener - every listener ends cleanly or by reset, and no process or port
    ''' remains (acceptance 3).</summary>
    Public Sub [Stop]()
        SyncLock _stateLock
            StopOpened()
            _state = New LiveProducerState()
        End SyncLock
    End Sub

    Private Sub StopOpened()
        Dim stoppedId As String = _state.BroadcastId
        If _state.Running AndAlso Not String.IsNullOrEmpty(stoppedId) Then
            Try
                _ports.BroadcastStopped?.Invoke(stoppedId)
            Catch
                ' Publication failure must never keep capture devices or sockets alive.
            End Try
        End If
        ' Close encoder inputs first, releasing capture callbacks blocked in a pipe write.
        Try
            _encoder?.[Stop]()
        Catch
        End Try
        Try
            _audioSource?.[Stop]()
        Catch
        End Try
        If _endingSource IsNot Nothing Then
            Try
                RemoveHandler _endingSource.Ended, _endingHandler
            Catch
            End Try
            _endingSource = Nothing
            _endingHandler = Nothing
        End If
        Try
            _videoSource?.[Stop]()
        Catch
        End Try
        _audioSource = Nothing
        _videoSource = Nothing
        _encoder = Nothing

        If _server IsNot Nothing Then
            Try
                _server.Stop()
            Catch
            End Try
            Try
                _server.Dispose()
            Catch
            End Try
            _server = Nothing
        End If
        _audioHub?.Clear()
        _videoHub?.Clear()
        _audioHub = Nothing
        _videoHub = Nothing
    End Sub

    ''' <summary>A source that can end by itself (the window route of the screen source) reports it here.
    ''' The event comes on the capture runtime's thread and the stop joins that source's pacing thread, so
    ''' the stop is queued to the pool; a source that is no longer THE running one (an old broadcast's
    ''' late event) is ignored.</summary>
    Private Sub ListenForSourceEnd(source As ILiveVideoSource)
        Dim ending As ILiveSourceEnding = TryCast(source, ILiveSourceEnding)
        If ending Is Nothing Then Return
        Dim handler As Action(Of String) = Sub(reason) StopBecauseSourceEnded(source, reason)
        AddHandler ending.Ended, handler
        _endingSource = ending
        _endingHandler = handler
    End Sub

    Private Sub StopBecauseSourceEnded(source As ILiveVideoSource, reason As String)
        ThreadPool.QueueUserWorkItem(
            Sub(state)
                Dim stopped As Boolean = False
                SyncLock _stateLock
                    If _state.Running AndAlso ReferenceEquals(source, _videoSource) Then
                        StopOpened()
                        _state = New LiveProducerState()
                        stopped = True
                    End If
                End SyncLock
                If stopped Then RaiseEvent SourceEnded(reason)
            End Sub)
    End Sub

    Private Sub AudioSink(chunk As LivePcmChunk)
        Dim encoder As ILiveEncoder = _encoder
        If encoder Is Nothing OrElse chunk Is Nothing Then Return
        RaiseEvent AudioLevel(chunk.Peak)
        Try
            encoder.WritePcm(chunk)
        Catch
        End Try
    End Sub

    Private Sub VideoSink(frame As LiveVideoFrame)
        Dim encoder As ILiveEncoder = _encoder
        If encoder Is Nothing OrElse frame Is Nothing Then Return
        Try
            encoder.WriteVideo(frame)
        Catch
        End Try
    End Sub

    Private Sub EncoderOutput(bytes As Byte())
        Dim hub As LiveBroadcastHub = If(_encodingVideo, _videoHub, _audioHub)
        If hub Is Nothing OrElse bytes Is Nothing Then Return
        Try
            hub.Append(bytes)
        Catch
        End Try
    End Sub

    Private Function IsVideoMode() As Boolean
        Return String.Equals(_state.Mode, "VIDEO_AUDIO", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(_state.Mode, "VIDEO_ONLY", StringComparison.OrdinalIgnoreCase)
    End Function

End Class

''' <summary>The opaque path segment of the LAN video URL (item H): a fresh random token per
''' broadcast, so the screen's fixed path is not guessable once the user turns the LAN listener on.</summary>
Friend NotInheritable Class LiveProducerTokens

    Private Sub New()
    End Sub

    Public Shared Function NewToken() As String
        Dim bytes(5) As Byte
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes)
        Dim hex As New Text.StringBuilder(bytes.Length * 2)
        For Each b As Byte In bytes
            hex.Append(b.ToString("x2", Globalization.CultureInfo.InvariantCulture))
        Next
        Return hex.ToString()
    End Function

    ''' <summary>Generates a standard 16-byte base64url broadcast identifier.</summary>
    Public Shared Function NewId() As String
        Dim bytes(15) As Byte
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes)
        Return Convert.ToBase64String(bytes).Replace("+"c, "-"c).Replace("/"c, "_"c).TrimEnd("="c)
    End Function

End Class
#End If
