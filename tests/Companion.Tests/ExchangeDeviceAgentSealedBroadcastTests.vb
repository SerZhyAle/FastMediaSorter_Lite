Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 234 T-3 (DEVICE-EXCHANGE section 27 items DK, DL, DM, DP), the BROADCASTER side as the device agent wires it: what a sealed
''' broadcast puts into its records, when the relay upload starts at all, and that the tunnel of a sealed broadcast does not bridge clear
''' bytes through the server. The record layer itself (box, epochs, order of update and replacement) is held by
''' ExchangeSealedBroadcastTests in the viewer suite; this file drives the AGENT against the stub account.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeDeviceAgentSealedBroadcastTests

    Private Const BroadcastId As String = "bcast-0123456789abcdeA"
    Private Const Secret As String = "s3cret-pass-0042"

    Private Shared Async Function OnlineAgentAsync(rig As ExchangeDeviceAgentTests.Rig, acc As ExchangeStubAccount, sealedServer As Boolean) As Task(Of ExchangeDeviceAgent)
        If sealedServer Then
            acc.Features = {"directory", "tunnel", "pairing", ExchangeSealedNames.FeatureSealed}
            acc.ListEnrolledDevices = True      ' the directory lists this device with its e2eKey: it is its own recipient
        End If
        Dim agent As ExchangeDeviceAgent = rig.NewAgent()
        Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
        Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
        Return agent
    End Function

    ''' <summary>The state the producer hands the agent: a LAN, a RELAY and a TUNNEL endpoint, the descriptor with the link key (what the
    ''' person hands out) and the one with `seal: {v}` alone (what the directory gets).</summary>
    Private Shared Function StateFor(isSealed As Boolean, withRelay As Boolean, password As String, linkKey As String, listenerPort As Integer) As LiveProducerState
        Dim input As New LiveDescriptorInput With {
            .Url = "http://127.0.0.1:" & listenerPort.ToString(System.Globalization.CultureInfo.InvariantCulture) & "/live.aac",
            .Mode = "AUDIO_ONLY",
            .Title = "t",
            .SourceId = "src",
            .AudioCodec = "aac",
            .SampleRateHz = 44100,
            .Channels = 1,
            .AllowLanViewers = True,
            .RelayUrl = If(withRelay, "https://relay.example:443/v2/b/" & BroadcastId & "/stream", ""),
            .TunnelUrl = "fmsx://relay.example:443/b/" & BroadcastId & "/http",
            .CertFingerprint = "sha256:" & New String("a"c, 64),
            .InnerUrl = "http://127.0.0.1:" & listenerPort.ToString(System.Globalization.CultureInfo.InvariantCulture) & "/live.aac",
            .AuthUser = If(password.Length > 0, "listener", ""),
            .Sealed = isSealed,
            .Outward = True,
            .RelayKey = linkKey
        }
        Return New LiveProducerState With {
            .Running = True,
            .Mode = "AUDIO_ONLY",
            .Url = input.Url,
            .Port = listenerPort,
            .ListenerAddress = "127.0.0.1",
            .SourceName = "src",
            .BroadcastId = BroadcastId,
            .Password = password,
            .AuthUser = input.AuthUser,
            .Credentials = LiveCredentialPairs.ForLink(password, iterations:=2),      ' ticket 263 T-3: none without a password
            .RelayUrl = input.RelayUrl,
            .TunnelUrl = input.TunnelUrl,
            .Sealed = isSealed,
            .DescriptorJson = LiveProducerDescriptorWriter.BuildJson(input),
            .RecordDescriptorJson = If(isSealed, LiveProducerDescriptorWriter.BuildJson(input.ForRecord()), "")
        }
    End Function

    Private Shared Function NewHub() As LiveBroadcastHub
        Return New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() 0L)
    End Function

    Private Shared Function RecordsOf(acc As ExchangeStubAccount) As List(Of String)
        Return acc.BroadcastLog().Select(Function(line) line.Substring(line.IndexOf("|"c) + 1)).ToList()
    End Function

    Private Shared Function HasBox(record As String) As Boolean
        Using doc As JsonDocument = JsonDocument.Parse(record)
            Dim member As JsonElement = Nothing
            Return doc.RootElement.TryGetProperty("sealed", member)
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ the record

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function Endpoint_and_cap_changes_update_the_record_with_the_same_id_and_current_verifiers(sealedServer As Boolean) As Task
        Using acc As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig()
            Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, sealedServer)
                Dim link As String = If(sealedServer, agent.NewBroadcastLinkKey(BroadcastId), "")
                Dim state As LiveProducerState = StateFor(sealedServer, sealedServer, Secret, link, 1)
                state.MaxListeners = 4
                Dim hub As LiveBroadcastHub = NewHub()
                agent.OnBroadcastStarted(state, hub, hub)
                Try
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-start") > 0))
                    Dim sealedSession As ExchangeSealedBroadcastSession = agent.CurrentSealed()
                    Dim original As String = If(state.RecordDescriptorJson.Length > 0, state.RecordDescriptorJson, state.DescriptorJson)
                    state.RecordDescriptorJson = original.Replace("127.0.0.1", "192.168.12.34")
                    state.MaxListeners = 2
                    Dim before As Integer = acc.CountOf("broadcast-update")
                    Assert.True(Await agent.OnBroadcastChangedAsync(state))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-update") > before))
                    Dim record As String = RecordsOf(acc).Last()
                    Assert.Contains("192.168.12.34", record)
                    Assert.DoesNotContain(Secret, record)
                    Using doc As JsonDocument = JsonDocument.Parse(record)
                        Assert.Equal(BroadcastId, doc.RootElement.GetProperty("broadcastId").GetString())
                        Assert.Equal(state.StartedAtUtc, doc.RootElement.GetProperty("startedAt").GetDateTime(), TimeSpan.FromMilliseconds(1))
                    End Using
                    Assert.Equal(1, acc.CountOf("broadcast-start"))
                    Assert.Same(sealedSession, agent.CurrentSealed())
                    Assert.Equal(4L, acc.BroadcastCaps().First())
                    Assert.Equal(2L, acc.BroadcastCaps().Last())
                    Using verifiers As JsonDocument = JsonDocument.Parse(acc.BroadcastVerifiersLog().Last().Split("|"c)(1))
                        Assert.Equal(2, verifiers.RootElement.GetArrayLength())
                    End Using
                Finally
                    agent.OnBroadcastStopped(BroadcastId)
                End Try
                Assert.False(Await agent.OnBroadcastChangedAsync(state))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_sealed_broadcast_goes_out_with_a_box_for_its_recipients_and_never_a_key_or_credentials_in_clear() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, True)
                    Assert.True(agent.CanSealBroadcast())
                    Dim linkKey As String = agent.NewBroadcastLinkKey(BroadcastId)     ' made before the start, the way the controller does it
                    Assert.Equal(43, linkKey.Length)
                    Dim state As LiveProducerState = StateFor(True, True, "", linkKey, 1)
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(state, hub, hub)
                    Try
                        Dim sealedSession As ExchangeSealedBroadcastSession = agent.CurrentSealed()
                        Assert.NotNull(sealedSession)
                        Assert.Equal(BroadcastId, sealedSession.BroadcastId)

                        ' the directory may need a moment to list this device: the record that carries a box is the start or its update
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() RecordsOf(acc).Any(Function(r) HasBox(r))))
                        Assert.StartsWith("broadcast-start|", acc.BroadcastLog()(0), StringComparison.Ordinal)

                        Dim relayKey As String = sealedSession.Keys.RelayKeyText()
                        For Each record As String In RecordsOf(acc)
                            Assert.DoesNotContain(linkKey, record)         ' the link key rides only in the descriptor the person hands out
                            Assert.DoesNotContain(relayKey, record)        ' the own devices' key is inside the box
                            Assert.DoesNotContain("credentials", record)
                            Using doc As JsonDocument = JsonDocument.Parse(record)
                                For Each ep As JsonElement In doc.RootElement.GetProperty("descriptor").GetProperty("endpoints").EnumerateArray()
                                    Dim transport As String = ep.GetProperty("transport").GetString()
                                    Dim seal As JsonElement = Nothing
                                    If transport = "RELAY" OrElse transport = "TUNNEL" Then
                                        Assert.True(ep.TryGetProperty("seal", seal), transport & " carries seal")
                                        Assert.Equal(1, seal.GetProperty("v").GetInt32())
                                        Dim member As JsonElement = Nothing
                                        Assert.False(seal.TryGetProperty("key", member))
                                    Else
                                        Assert.False(ep.TryGetProperty("seal", seal))      ' a LAN endpoint does not pass through the server
                                    End If
                                Next
                            End Using
                        Next

                        ' the box addresses this device (its own entry in the directory)
                        Dim ownId As String = agent.GetStatus().DeviceId
                        Dim boxed As String = RecordsOf(acc).First(Function(r) HasBox(r))
                        Using doc As JsonDocument = JsonDocument.Parse(boxed)
                            Dim keys As JsonElement = doc.RootElement.GetProperty("sealed").GetProperty("keys")
                            Assert.True(keys.EnumerateArray().Any(Function(k) String.Equals(k.GetProperty("to").GetString(), ownId, StringComparison.Ordinal)))
                        End Using

                        ' the descriptor the person hands out is the only place with the link key, and it never went to the server
                        Assert.Contains(linkKey, state.DescriptorJson)
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                    Assert.Null(agent.CurrentSealed())                     ' the keys of an ended broadcast are wiped
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_credentials_of_a_protected_sealed_broadcast_travel_in_the_box_only() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, True)
                    ' a protected broadcast lists no RELAY (the Digest runs on the producer's own server): LAN and TUNNEL
                    Dim state As LiveProducerState = StateFor(True, False, Secret, "", 1)
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(state, hub, hub)
                    Try
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() RecordsOf(acc).Any(Function(r) HasBox(r))))
                        For Each record As String In RecordsOf(acc)
                            Assert.DoesNotContain(Secret, record)
                            Assert.DoesNotContain("credentials", record)
                        Next
                        Await Task.Delay(300)
                        Assert.Equal(0, acc.CountOf("upload"))             ' no RELAY: nothing is uploaded, least of all in clear
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ the upload

    <Fact>
    Public Async Function A_sealed_server_gets_the_relay_upload_and_a_server_without_sealed_gets_none() As Task
        ' a server that lists `sealed`: the upload is opened (the stub does not answer it, which is all this test needs to see)
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, True)
                    Dim linkKey As String = agent.NewBroadcastLinkKey(BroadcastId)
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(StateFor(True, True, "", linkKey, 1), hub, hub)
                    Try
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("upload") >= 1))
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                End Using
            End Using
        End Using

        ' an older server: no `sealed`, so no key is made, no box, no upload (state server-no-e2e), whatever the state says
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, False)
                    Assert.False(agent.CanSealBroadcast())
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(StateFor(True, True, "", "", 1), hub, hub)
                    Try
                        Assert.Null(agent.CurrentSealed())
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-start") >= 1))
                        Await Task.Delay(900)                              ' several upload retries would have shown
                        Assert.Equal(0, acc.CountOf("upload"))
                        For Each record As String In RecordsOf(acc)
                            Assert.False(HasBox(record))
                        Next
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Pipe_opens_a_broadcast_and_guest_access_registers_then_seals_a_separate_group() As Task
        Using acc As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig(), guestKey = ExchangeDeviceKey.Generate()
            Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, True)
                Dim link = agent.NewBroadcastLinkKey(BroadcastId)
                Dim hub = NewHub()
                agent.OnBroadcastStarted(StateFor(True, True, "", link, 1), hub, hub)
                Try
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() RecordsOf(acc).Count > 0))
                    acc.Broadcasts.Add(RecordsOf(acc).Last())
                    Await acc.PushDirectoryAsync()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetFullDirectory().Broadcasts.Count = 1))
                    Using req = JsonDocument.Parse("{""broadcastId"":""" & BroadcastId & """}")
                        Dim opened = Await agent.HandleAsync(ExchangeAgentPipe.RequestGetBroadcastSeal, req.RootElement, CancellationToken.None)
                        Assert.True(opened.Ok, opened.Error)
                        Using payload = JsonDocument.Parse(opened.DataJson)
                            Assert.Equal(agent.GetStatus().DeviceId, payload.RootElement.GetProperty("from").GetProperty("deviceId").GetString())
                        End Using
                    End Using
                    Assert.Equal("", Await agent.GrantGuestAsync("visitor", BroadcastId, True, False, CancellationToken.None))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.GrantFrames.Count = 1))
                    Dim registration = acc.GrantFrames.ToArray()(0)
                    Dim box As ExchangeSealedBox = Nothing
                    Assert.True(ExchangeSealedBox.TryParse(registration.Record("sealed"), box))
                    Assert.Empty(box.Keys)
                    Assert.Equal("", registration.Record("access"))
                    Assert.Equal("", registration.Record("credentials"))
                    Dim guestId = "q3Vb7YtK0xP2mN9sLfR4wA"
                    Dim devices = "[{""deviceId"":""" & guestId & """,""deviceName"":""Visitor"",""e2eKey"":{""v"":1,""kem"":""" & guestKey.Kem & """}}]"
                    acc.Grants.Add("{""grantId"":""" & registration.Text("grantId") & """,""guest"":""visitor"",""broadcastId"":""" & BroadcastId & """,""devices"":" & devices & "}")
                    Await acc.PushDirectoryAsync()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.GrantFrames.Count >= 2))
                    Dim replacement = acc.GrantFrames.ToArray()(1)
                    Assert.True(ExchangeSealedBox.TryParse(replacement.Record("sealed"), box))
                    Dim openedGrant = ExchangeSealedBoxes.Open(box, guestId, guestKey, ExchangeSealedContext.Grant(registration.Text("grantId")), Function(id) box.FromKey)
                    Try
                        Assert.True(openedGrant.Succeeded)
                        Using payload = JsonDocument.Parse(openedGrant.Payload)
                            Dim group = payload.RootElement.GetProperty("relayKey").GetString()
                            Assert.NotEqual(link, group)
                            Assert.NotEqual(agent.CurrentSealed().Keys.RelayKeyText(), group)
                            Assert.DoesNotContain(group, String.Join(vbLf, rig.Log))
                            Assert.Equal(2, agent.CurrentSealed().Keys.LinkCount)
                        End Using
                    Finally
                        openedGrant.Wipe()
                    End Try
                    acc.Grants.Clear()
                    Await acc.PushDirectoryAsync()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.CurrentSealed().Keys.LinkCount = 1))
                    Assert.DoesNotContain(link, String.Join(vbLf, rig.Log))
                    Assert.DoesNotContain(box.Ciphertext, String.Join(vbLf, rig.Log))
                Finally
                    agent.OnBroadcastStopped(BroadcastId)
                End Try
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------ the tunnel (item DM)

    <Fact>
    Public Async Function The_tunnel_of_a_sealed_broadcast_stays_shut_until_it_runs_TLS_inside_and_an_unsealed_one_still_bridges() As Task
        Dim listener As New TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        Try
            Dim port As Integer = CType(listener.LocalEndpoint, IPEndPoint).Port
            Using acc As New ExchangeStubAccount()
                Using rig As New ExchangeDeviceAgentTests.Rig()
                    Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, True)
                        Dim hub As LiveBroadcastHub = NewHub()
                        Dim linkKey As String = agent.NewBroadcastLinkKey(BroadcastId)

                        ' sealed: a tunnel `open` reaches the agent, it attaches to run inner TLS, but touches the local server only once inner TLS completes
                        agent.OnBroadcastStarted(StateFor(True, True, "", linkKey, port), hub, hub)
                        Try
                            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-start") >= 1))
                            Await acc.PushAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.Open).SetText("broadcastId", BroadcastId).SetText("tunnelId", "tunnel-0001"))
                            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("attach") >= 1))
                            Dim accepted As Task(Of TcpClient) = listener.AcceptTcpClientAsync()
                            Dim first As Task = Await Task.WhenAny(accepted, Task.Delay(800))
                            Assert.False(ReferenceEquals(first, accepted), "no clear connection to the producer's server for a sealed broadcast without inner TLS")
                        Finally
                            agent.OnBroadcastStopped(BroadcastId)
                        End Try
                    End Using
                End Using
            End Using
        Finally
            listener.Stop()
        End Try

        ' the control: a broadcast that is not sealed (no relay, no key) still bridges its tunnel, as before
        Dim control As New TcpListener(IPAddress.Loopback, 0)
        control.Start()
        Try
            Dim port As Integer = CType(control.LocalEndpoint, IPEndPoint).Port
            Using acc As New ExchangeStubAccount()
                Using rig As New ExchangeDeviceAgentTests.Rig()
                    Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc, False)
                        Dim hub As LiveBroadcastHub = NewHub()
                        Dim state As LiveProducerState = StateFor(False, False, "", "", port)
                        agent.OnBroadcastStarted(state, hub, hub)
                        Try
                            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-start") >= 1))
                            Await acc.PushAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.Open).SetText("broadcastId", BroadcastId).SetText("tunnelId", "tunnel-0002"))
                            Dim accepted As Task(Of TcpClient) = control.AcceptTcpClientAsync()
                            Dim first As Task = Await Task.WhenAny(accepted, Task.Delay(5000))
                            Assert.True(ReferenceEquals(first, accepted), "the unsealed tunnel connects to the producer's server")
                            accepted.Result.Dispose()
                        Finally
                            agent.OnBroadcastStopped(BroadcastId)
                        End Try
                    End Using
                End Using
            End Using
        Finally
            control.Stop()
        End Try
    End Function

End Class
