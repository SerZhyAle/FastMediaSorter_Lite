Option Strict On

Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets

' Ticket 199: the Share Manager's wiring of the producer's ports (LiveProducerPorts). Everything the
' controller needs from the machine is read here, behind the gate: the dormant-state rule (section 8)
' holds that NOTHING is read, opened or written while the feature is off, and the controller enforces
' it by checking the gate before it calls any port - so these functions may touch the hive, the NIC
' table and the device, but only ever after the user turned the feature on and pressed Start.

''' <summary>The real ports of the Share Manager's live broadcast.</summary>
Public Module LiveBroadcastHost

    ''' <summary>True when the build or the developer switch offers the producer (safety rule 6): every
    ''' surface - the window, the tray item, the settings rows - consults this and nothing else.</summary>
    Friend Function Offered() As Boolean
        Return LiveBroadcastFeatures.IsEnabled()
    End Function

    Friend Function BuildPorts() As LiveProducerPorts
        Return New LiveProducerPorts With {
            .GateEnabled = AddressOf LiveBroadcastFeatures.IsEnabled,
            .PickAddress = Function() LiveProducerAddressPolicy.ChooseAddress(EnumerateLanCandidates()),
            .LoadSettings = Function() LiveProducerSettings.Load(AddressOf ReadHiveValue),
            .EnrolledDeviceId = Function()
                                     Dim account As ExchangeAccount = ExchangeAccountStore.CreateDefault().Load()
                                     Return If(account Is Nothing, "", account.DeviceId)
                                 End Function,
            .ReadStored = Function(key) GetSetting(App_name, Second_App_Name, key, ""),
            .WriteStored = Sub(key, value) SaveSetting(App_name, Second_App_Name, key, value),
            .CreateEncoder = Function() New LiveFfmpegEncoder(),
            .CreateAudioSource = Function(kind) New WasapiAudioSource(kind = LiveProducerSourceKind.SystemAudio),
            .CreateVideoSource = Function(kind)
                                     ' The screen and camera sources read their own rows only here,
                                     ' behind the gate (the dormant rule): the monitor/camera pick and
                                     ' the cursor/mode settings are the rows the window just saved.
                                     Dim sourceSettings As LiveProducerSettings = LiveProducerSettings.Load(AddressOf ReadHiveValue)
                                     If kind = LiveProducerSourceKind.Camera Then
                                         Return New CameraCaptureSource(sourceSettings.CameraDeviceIndex, sourceSettings.CameraModeIndex)
                                     End If
                                     ' Ticket 263 T-2: a remembered window is the target INSTEAD of the monitor; it is
                                     ' looked up again at Start and an absent one refuses the start (never the monitor).
                                     If sourceSettings.HasScreenWindow Then
                                         Return New ScreenCaptureSource(sourceSettings.ScreenWindowTitle, sourceSettings.ScreenWindowProcess, sourceSettings.ScreenCursor)
                                     End If
                                     Return New ScreenCaptureSource(sourceSettings.ScreenMonitorIndex, sourceSettings.ScreenCursor)
                                 End Function,
            .ClockMs = AddressOf MonotonicClock.NowMs,
            .NewToken = AddressOf LiveProducerTokens.NewToken,
            .NewId = AddressOf LiveProducerTokens.NewId,
            .ExchangeEndpointInfo = Function()
                                        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
                                        If agent Is Nothing Then Return Nothing
                                        Dim status As ExchangeDeviceStatus = agent.GetStatus()
                                        If status Is Nothing OrElse String.IsNullOrEmpty(status.PublicEndpoint) Then Return Nothing
                                        Return Tuple.Create(status.PublicEndpoint, status.Fingerprint)
                                    End Function,
            .BroadcastStarted = Sub(state, audioHub, videoHub)
                                    WatchNetwork()
                                    Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
                                    If agent IsNot Nothing Then
                                        ListenTo(agent)
                                        agent.OnBroadcastStarted(state, audioHub, videoHub)
                                    End If
                                End Sub,
            .BroadcastChanged = Sub(state)
                                    Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
                                    If agent IsNot Nothing Then
                                        Dim pending As System.Threading.Tasks.Task = agent.OnBroadcastChangedAsync(state)
                                    End If
                                End Sub,
            .BroadcastStopped = Sub(broadcastId)
                                    If System.Threading.Interlocked.Exchange(_watchingNetwork, 0) <> 0 Then
                                        RemoveHandler NetworkChange.NetworkAddressChanged, AddressOf OnNetworkAddressChanged
                                    End If
                                    Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
                                    If agent IsNot Nothing Then
                                        agent.OnBroadcastStopped(broadcastId)
                                    End If
                                End Sub,
            .Sealing = New AgentSealing()
        }
    End Function

    ''' <summary>Ticket 263 T-6: the exchange server refused the `broadcast-start` of the live broadcast with this id (any thread). The
    ''' window shows one sentence for it. Only the id and the typed reason cross: never the record, the descriptor or a key.</summary>
    Friend Event ExchangeStartRefused As Action(Of String, ExchangeRefusal)

    Private ReadOnly SubscribeGate As New Object()
    Private _listeningTo As ExchangeDeviceAgent

    ''' <summary>Passes the agent's start refusals on to <see cref="ExchangeStartRefused"/>. Once per agent: the process has one agent, and
    ''' a second broadcast must not add a second forwarder.</summary>
    Friend Sub ListenTo(agent As ExchangeDeviceAgent)
        If agent Is Nothing Then Return
        SyncLock SubscribeGate
            If ReferenceEquals(_listeningTo, agent) Then Return
            _listeningTo = agent
        End SyncLock
        AddHandler agent.BroadcastStartRefused, AddressOf ForwardStartRefused
    End Sub

    Private Sub ForwardStartRefused(broadcastId As String, refusal As ExchangeRefusal)
        RaiseEvent ExchangeStartRefused(broadcastId, refusal)
    End Sub

    ''' <summary>The producer's sealing port, answered by the current exchange agent (spec 234, DEVICE-EXCHANGE items DK and DL):
    ''' the keys and the sealed directory live in the agent, the controller only asks.</summary>
    Private NotInheritable Class AgentSealing
        Implements ILiveBroadcastSealing

        Public ReadOnly Property CanSealThroughServer As Boolean Implements ILiveBroadcastSealing.CanSealThroughServer
            Get
                Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
                Return agent IsNot Nothing AndAlso agent.CanSealBroadcast()
            End Get
        End Property

        Public Function NewLinkKey(broadcastId As String) As String Implements ILiveBroadcastSealing.NewLinkKey
            Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
            Return If(agent Is Nothing, "", agent.NewBroadcastLinkKey(broadcastId))
        End Function

        Public Function NewTunnelPin(broadcastId As String) As String Implements ILiveBroadcastSealing.NewTunnelPin
            Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
            Return If(agent Is Nothing, "", agent.NewBroadcastTunnelPin(broadcastId))
        End Function
    End Class

    ''' <summary>The one controller instance of the Share Manager - one live broadcast per device at a
    ''' time (section 2) means one object, owned by the tray context and shown by the window.</summary>
    Private _controller As LiveBroadcastController
    Private _watchingNetwork As Integer

    Private Sub WatchNetwork()
        If System.Threading.Interlocked.Exchange(_watchingNetwork, 1) <> 0 Then Return
        AddHandler NetworkChange.NetworkAddressChanged, AddressOf OnNetworkAddressChanged
    End Sub

    Private Sub OnNetworkAddressChanged(sender As Object, e As EventArgs)
        Dim pending As System.Threading.Tasks.Task = System.Threading.Tasks.Task.Run(
            Sub()
                Dim current As LiveBroadcastController = _controller
                If current IsNot Nothing AndAlso current.State.Running Then current.UpdateNetwork()
            End Sub)
    End Sub

    Friend Function Controller() As LiveBroadcastController
        If _controller Is Nothing Then _controller = New LiveBroadcastController(BuildPorts())
        Return _controller
    End Function

    Private Function ReadHiveValue(key As String) As String
        Return GetSetting(App_name, Second_App_Name, key, "")
    End Function

    ''' <summary>The machine's LAN candidates in the shape the address policy scores: every Up,
    ''' non-loopback, non-tunnel NIC's IPv4 unicast addresses with NetworkInfo's signals (a real IPv4
    ''' gateway, a physical NIC, the virtual-adapter heuristic).</summary>
    Friend Function EnumerateLanCandidates() As System.Collections.Generic.IEnumerable(Of LiveAddressCandidate)
        Dim list As New System.Collections.Generic.List(Of LiveAddressCandidate)()
        Try
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus <> OperationalStatus.Up Then Continue For
                Select Case nic.NetworkInterfaceType
                    Case NetworkInterfaceType.Loopback, NetworkInterfaceType.Tunnel
                        Continue For
                End Select

                Dim props As IPInterfaceProperties = nic.GetIPProperties()
                If props Is Nothing Then Continue For
                Dim hasGateway As Boolean = NicHasIpv4Gateway(props)

                For Each ua As UnicastIPAddressInformation In props.UnicastAddresses
                    Dim addr As IPAddress = If(ua Is Nothing, Nothing, ua.Address)
                    If addr Is Nothing OrElse addr.AddressFamily <> AddressFamily.InterNetwork Then Continue For
                    list.Add(New LiveAddressCandidate With {
                        .Address = addr.ToString(),
                        .NicType = nic.NetworkInterfaceType,
                        .LooksVirtual = LooksVirtualNic(nic),
                        .HasIpv4Gateway = hasGateway
                    })
                Next
            Next
        Catch
        End Try
        Return list
    End Function

    Private Function NicHasIpv4Gateway(props As IPInterfaceProperties) As Boolean
        For Each gw As GatewayIPAddressInformation In props.GatewayAddresses
            If gw Is Nothing OrElse gw.Address Is Nothing Then Continue For
            If gw.Address.AddressFamily <> AddressFamily.InterNetwork Then Continue For
            Dim s As String = gw.Address.ToString()
            If String.IsNullOrEmpty(s) OrElse s = "0.0.0.0" Then Continue For
            Return True
        Next
        Return False
    End Function

    ''' <summary>The same heuristic NetworkInfo scores with (name/description match), kept in one
    ''' place there and mirrored here because the producer needs the same judgement per candidate.</summary>
    Private Function LooksVirtualNic(nic As NetworkInterface) As Boolean
        Dim hay As String = ((If(nic.Name, "")) & " " & (If(nic.Description, ""))).ToLowerInvariant()
        For Each h As String In VirtualNicHints
            If hay.IndexOf(h, StringComparison.Ordinal) >= 0 Then Return True
        Next
        Return False
    End Function

    Private ReadOnly VirtualNicHints As String() = {
        "virtual", "vmware", "virtualbox", "vbox", "hyper-v", "vethernet",
        "tap-", "tap adapter", "vpn", "tailscale", "wireguard", "zerotier",
        "pseudo", "wan miniport", "bluetooth", "docker", "hamachi", "loopback"
    }

End Module
