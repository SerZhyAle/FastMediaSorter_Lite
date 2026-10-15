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
                                    Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
                                    If agent IsNot Nothing Then
                                        agent.OnBroadcastStarted(state, audioHub, videoHub)
                                    End If
                                End Sub,
            .BroadcastStopped = Sub(broadcastId)
                                    Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
                                    If agent IsNot Nothing Then
                                        agent.OnBroadcastStopped(broadcastId)
                                    End If
                                End Sub
        }
    End Function

    ''' <summary>The one controller instance of the Share Manager - one live broadcast per device at a
    ''' time (section 2) means one object, owned by the tray context and shown by the window.</summary>
    Private _controller As LiveBroadcastController

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
