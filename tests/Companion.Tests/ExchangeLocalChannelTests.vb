Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Pipes
Imports System.Linq
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 180 T-3 (section 6): the local channel from the viewer to the agent, both ends, against a stub handler and against the real agent. The
''' pipe names used here are private to the test (the frozen one of the current user is never touched). Held: one JSON line in, one out; the DACL
''' names this user only and denies the network; a second creator of the same name fails; a bad or oversize request is answered or dropped, never
''' served; and the secret `access` crosses only through GetAccess while the directory snapshot carries none.
''' </summary>
Public Class ExchangeLocalChannelTests

    Private Shared Function PipeName() As String
        Return "fms-device-agent-test-" & Guid.NewGuid().ToString("N").Substring(0, 12)
    End Function

    ''' <summary>A handler with scripted answers that records the requests it got.</summary>
    Private NotInheritable Class StubHandler
        Implements IExchangeAgentHandler

        Public ReadOnly Seen As New List(Of String)
        Public DirectoryRevision As Long = 5
        Public Delay As Integer

        Public Async Function HandleAsync(requestType As String, request As JsonElement, ct As CancellationToken) As Task(Of ExchangeLocalResult) Implements IExchangeAgentHandler.HandleAsync
            SyncLock Seen
                Seen.Add(requestType)
            End SyncLock
            Select Case requestType
                Case ExchangeAgentPipe.RequestGetState
                    Return ExchangeLocalResult.Success(New ExchangeAgentStateInfo(True, ExchangeAgentState.Online, {"directory", "pairing"}, "DEV", "My PC", "").ToJson())
                Case ExchangeAgentPipe.RequestGetDirectory
                    Dim since As Long = 0
                    Dim el As JsonElement = Nothing
                    If request.TryGetProperty("sinceRevision", el) Then since = el.GetInt64()
                    If Delay > 0 Then Await Task.Delay(Delay, ct)
                    Dim snapshot As New ExchangeDirectorySnapshot(DirectoryRevision, New List(Of ExchangeDeviceRecord), New List(Of ExchangeResourceRecord), New List(Of ExchangeBroadcastRecord))
                    Return ExchangeLocalResult.Success(snapshot.ToLocalJson())
                Case ExchangeAgentPipe.RequestGetAccess
                    Return ExchangeLocalResult.Success("{""descriptor"":""FMSSFTP2.stub""}")
                Case Else
                    Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnsupported)
            End Select
        End Function
    End Class

    ' ---------------------------------------------------------------- the name and the DACL

    <Fact>
    Public Sub The_pipe_name_is_the_prefix_and_a_hash_of_the_user_sid_never_the_sid_itself()
        Dim name As String = ExchangeAgentPipe.PipeName()
        Assert.StartsWith("fms-device-agent-", name)
        Assert.Matches("^fms-device-agent-[0-9a-f]{16}$", name)
        Assert.DoesNotContain(ExchangeAgentPipe.CurrentUserSid(), name)
        Assert.Equal(name, ExchangeAgentPipe.NameForSid(ExchangeAgentPipe.CurrentUserSid()))
        Assert.NotEqual(name, ExchangeAgentPipe.NameForSid("S-1-5-21-1-2-3-1001"))
    End Sub

    <Fact>
    Public Sub The_dacl_names_the_current_user_and_denies_the_network_and_nobody_else()
        Dim security As PipeSecurity = ExchangeDeviceLocalChannel.BuildSecurity()
        Dim rules As List(Of PipeAccessRule) = security.GetAccessRules(True, False, GetType(SecurityIdentifier)).Cast(Of PipeAccessRule)().ToList()
        Dim me1 As String = ExchangeAgentPipe.CurrentUserSid()
        Dim allows As List(Of PipeAccessRule) = rules.Where(Function(r) r.AccessControlType = AccessControlType.Allow).ToList()
        Assert.Single(allows)
        Assert.Equal(me1, allows(0).IdentityReference.Value)
        Dim denies As List(Of PipeAccessRule) = rules.Where(Function(r) r.AccessControlType = AccessControlType.Deny).ToList()
        Assert.Single(denies)
        Assert.Equal(New SecurityIdentifier(WellKnownSidType.NetworkSid, Nothing).Value, denies(0).IdentityReference.Value)
    End Sub

    ' ---------------------------------------------------------------- both ends

    <Fact>
    Public Async Function One_request_one_reply_for_state_directory_and_access() As Task
        Dim name As String = PipeName()
        Dim handler As New StubHandler()
        Using channel As New ExchangeDeviceLocalChannel(handler, name)
            channel.Start()
            Dim client As New ExchangeAgentClient(name)

            Dim state As ExchangeAgentStateResult = Await client.GetStateAsync(CancellationToken.None)
            Assert.True(state.Reply.IsOk)
            Assert.True(state.Info.Configured)
            Assert.True(state.Info.IsOnline)
            Assert.Equal({"directory", "pairing"}, state.Info.Features.ToArray())
            Assert.Equal("My PC", state.Info.DeviceName)

            Dim dir As ExchangeAgentDirectoryResult = Await client.GetDirectoryAsync(0, 0, CancellationToken.None)
            Assert.True(dir.Reply.IsOk)
            Assert.Equal(5L, dir.Snapshot.Revision)

            Dim access As ExchangeAgentAccessResult = Await client.GetAccessAsync("R1", CancellationToken.None)
            Assert.True(access.Reply.IsOk)
            Assert.Contains("FMSSFTP2.stub", access.AccessJson)
            Assert.DoesNotContain("FMSSFTP2.stub", access.Reply.ToString())   ' never printed

            Assert.Equal({"GetState", "GetDirectory", "GetAccess"}, handler.Seen.ToArray())
        End Using
    End Function

    <Fact>
    Public Async Function A_bad_request_is_answered_bad_request_and_an_unknown_one_unsupported() As Task
        Dim name As String = PipeName()
        Using channel As New ExchangeDeviceLocalChannel(New StubHandler(), name)
            channel.Start()
            Dim client As New ExchangeAgentClient(name)
            Dim notJson As ExchangeAgentReply = Await client.SendAsync("this is not json", 3000, CancellationToken.None)
            Assert.Equal(ExchangeAgentReplyKind.Refused, notJson.Kind)
            Assert.Equal("bad-request", notJson.Error)
            Dim noType As ExchangeAgentReply = Await client.SendAsync("{""x"":1}", 3000, CancellationToken.None)
            Assert.Equal("bad-request", noType.Error)
            Dim array As ExchangeAgentReply = Await client.SendAsync("[1,2]", 3000, CancellationToken.None)
            Assert.Equal("bad-request", array.Error)
            Dim unknown As ExchangeAgentReply = Await client.SendAsync(ExchangeAgentPipe.BuildRequest("Frobnicate"), 3000, CancellationToken.None)
            Assert.Equal(ExchangeAgentReplyKind.Refused, unknown.Kind)
            Assert.Equal("unsupported", unknown.Error)
        End Using
    End Function

    <Fact>
    Public Async Function A_request_line_above_4_KiB_is_dropped_not_served() As Task
        Dim name As String = PipeName()
        Dim handler As New StubHandler()
        Using channel As New ExchangeDeviceLocalChannel(handler, name)
            channel.Start()
            Dim client As New ExchangeAgentClient(name)
            Dim huge As String = ExchangeAgentPipe.BuildRequest("GetState", Sub(o) o("pad") = New String("x"c, 5000))
            Dim reply As ExchangeAgentReply = Await client.SendAsync(huge, 3000, CancellationToken.None)
            Assert.NotEqual(ExchangeAgentReplyKind.Ok, reply.Kind)
            Assert.Empty(handler.Seen)
            ' and the server is still there for the next client
            Assert.True((Await client.GetStateAsync(CancellationToken.None)).Reply.IsOk)
        End Using
    End Function

    <Fact>
    Public Async Function A_long_poll_waits_for_the_handler_and_the_caller_can_cancel_it() As Task
        Dim name As String = PipeName()
        Dim handler As New StubHandler() With {.Delay = 1500}
        Using channel As New ExchangeDeviceLocalChannel(handler, name)
            channel.Start()
            Dim client As New ExchangeAgentClient(name)
            Dim waited As ExchangeAgentDirectoryResult = Await client.GetDirectoryAsync(5, 2000, CancellationToken.None)
            Assert.True(waited.Reply.IsOk)
            Using cts As New CancellationTokenSource(200)
                Dim cancelled As ExchangeAgentDirectoryResult = Await client.GetDirectoryAsync(5, 2000, cts.Token)
                Assert.Equal(ExchangeAgentReplyKind.Cancelled, cancelled.Reply.Kind)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_exchange_is_bounded_by_the_deadline_when_the_agent_never_answers() As Task
        Dim name As String = PipeName()
        Dim handler As New StubHandler() With {.Delay = 20000}
        Using channel As New ExchangeDeviceLocalChannel(handler, name)
            channel.Start()
            Dim client As New ExchangeAgentClient(name)
            Dim watch As Stopwatch = Stopwatch.StartNew()
            Dim reply As ExchangeAgentReply = Await client.SendAsync(ExchangeAgentPipe.BuildRequest("GetDirectory"), 700, CancellationToken.None)
            Assert.Equal(ExchangeAgentReplyKind.TimedOut, reply.Kind)
            Assert.True(watch.ElapsedMilliseconds < 5000, watch.ElapsedMilliseconds.ToString())
        End Using
    End Function

    <Fact>
    Public Async Function With_nobody_listening_the_client_says_not_running_quickly() As Task
        Dim client As New ExchangeAgentClient(PipeName(), 300)
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim reply As ExchangeAgentReply = Await client.SendAsync(ExchangeAgentPipe.BuildRequest("GetState"), 3000, CancellationToken.None)
        Assert.Equal(ExchangeAgentReplyKind.NotRunning, reply.Kind)
        Assert.True(watch.ElapsedMilliseconds < 3000, watch.ElapsedMilliseconds.ToString())
        Dim state As ExchangeAgentStateResult = Await client.GetStateAsync(CancellationToken.None)
        Assert.False(state.Reply.IsOk)
        Assert.Null(state.Info)
    End Function

    <Fact>
    Public Sub A_second_creator_of_the_same_name_fails_instead_of_serving_beside_the_first()
        Dim name As String = PipeName()
        Using first As New ExchangeDeviceLocalChannel(New StubHandler(), name)
            first.Start()
            Using second As New ExchangeDeviceLocalChannel(New StubHandler(), name)
                Assert.Throws(Of IOException)(Sub() second.Start())
            End Using
        End Using
    End Sub

    <Fact>
    Public Async Function The_server_is_ours_so_the_client_talks_to_it() As Task
        ' the client refuses a pipe whose server process runs as another user; here the server is this process, which passes
        Dim name As String = PipeName()
        Using channel As New ExchangeDeviceLocalChannel(New StubHandler(), name)
            channel.Start()
            Dim reply As ExchangeAgentReply = Await New ExchangeAgentClient(name).SendAsync(ExchangeAgentPipe.BuildRequest("GetState"), 3000, CancellationToken.None)
            Assert.NotEqual(ExchangeAgentReplyKind.Untrusted, reply.Kind)
            Assert.True(reply.IsOk)
        End Using
    End Function

    <Fact>
    Public Async Function Many_clients_in_a_row_each_get_their_own_connection() As Task
        Dim name As String = PipeName()
        Dim handler As New StubHandler()
        Using channel As New ExchangeDeviceLocalChannel(handler, name)
            channel.Start()
            Dim client As New ExchangeAgentClient(name)
            For i As Integer = 1 To 12
                Assert.True((Await client.GetStateAsync(CancellationToken.None)).Reply.IsOk, "request " & i.ToString())
            Next
            Dim parallel As New List(Of Task(Of ExchangeAgentStateResult))
            For i As Integer = 1 To 6
                parallel.Add(client.GetStateAsync(CancellationToken.None))
            Next
            For Each r As ExchangeAgentStateResult In Await Task.WhenAll(parallel)
                Assert.True(r.Reply.IsOk)
            Next
            Assert.Equal(18, handler.Seen.Count)
        End Using
    End Function

    ' ---------------------------------------------------------------- the real agent behind the channel

    <Fact>
    Public Async Function The_agent_answers_state_directory_and_access_and_the_directory_carries_no_secret() As Task
        Using acc As New ExchangeStubAccount()
            acc.Features = {"directory", "pairing", ExchangeSealedNames.FeatureSealed}
            Using phoneKey As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Dim phoneId As String = ExchangeWireTestKit.IdOf("P"c)
                Dim res1Id As String = ExchangeWireTestKit.IdOf("1"c)
                Dim res2Id As String = ExchangeWireTestKit.IdOf("2"c)
                acc.Devices.Add("{""deviceId"":""" & phoneId & """,""deviceName"":""Phone"",""product"":""fms-android"",""presence"":""online"",""capabilities"":[""e2e-v1""],""e2eKey"":{""v"":1,""kem"":""" & phoneKey.Kem & """}}")
                Using rig As New ExchangeDeviceAgentTests.Rig()
                    rig.Ports.DeviceKeys = New ExchangeMemoryDeviceKeyStore()
                    rig.Ports.SealedTrust = New ExchangeMemorySealedTrustStore()
                    Using agent As ExchangeDeviceAgent = rig.NewAgent()
                        Dim name As String = PipeName()
                        Dim client As New ExchangeAgentClient(name)

                        ' before an account exists: configured False, and the directory is refused
                        Using channel As New ExchangeDeviceLocalChannel(agent, name)
                            channel.Start()
                            Dim before As ExchangeAgentStateResult = Await client.GetStateAsync(CancellationToken.None)
                            Assert.True(before.Reply.IsOk)
                            Assert.False(before.Info.Configured)
                            Assert.Equal(ExchangeAgentState.NotConfigured, before.Info.State)
                            Assert.Equal("not-configured", (Await client.GetDirectoryAsync(0, 0, CancellationToken.None)).Reply.Error)

                            Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                            Dim agentId As String = agent.GetStatus().DeviceId
                            Dim agentKem As String = ""
                            Using doc As JsonDocument = JsonDocument.Parse(acc.EnrollE2eKeysSeen().Last())
                                agentKem = doc.RootElement.GetProperty("kem").GetString()
                            End Using
                            Const fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
                            Dim accessClear As String = Nothing
                            Assert.True(ExchangeResourceAccess.TryBuild(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "top-secret", fp, "", "", False), "/Photos", accessClear))
                            Dim box As String = ""
                            Dim recipients As New List(Of ExchangeSealedRecipient) From {New ExchangeSealedRecipient(agentId, agentKem)}
                            Assert.True(ExchangeSealedAccess.SealAccess(accessClear, res1Id, phoneId, phoneKey, recipients, box))
                            Dim rec1Json As String = Nothing
                            Assert.True(ExchangeResourceRecordBuilder.TryBuild(res1Id, phoneId, "Photos", "", "", Nothing, DateTime.UtcNow, rec1Json, False, box, fp, "/Photos"))
                            acc.Resources.Add(rec1Json)
                            acc.Resources.Add("{""resourceId"":""" & res2Id & """,""deviceId"":""" & phoneId & """,""kind"":""sftp-share"",""name"":""Offline one""}")
                            Await acc.PushDirectoryAsync()
                            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetFullDirectory().Resources.Count = 2))

                            Dim state As ExchangeAgentStateResult = Await client.GetStateAsync(CancellationToken.None)
                            Assert.True(state.Info.Configured)
                            Assert.True(state.Info.IsOnline)
                            Assert.Contains("directory", state.Info.Features)
                            Assert.Equal("TESTPC", state.Info.DeviceName)
                            Assert.Equal(agent.GetStatus().DeviceId, state.Info.DeviceId)

                            Dim dir As ExchangeAgentDirectoryResult = Await client.GetDirectoryAsync(0, 0, CancellationToken.None)
                            Assert.True(dir.Reply.IsOk)
                            Assert.Equal(8L, dir.Snapshot.Revision)
                            Assert.Equal(2, dir.Snapshot.Resources.Count)
                            Assert.DoesNotContain("top-secret", dir.Reply.DataJson)
                            Assert.DoesNotContain("SHARE", dir.Reply.DataJson)
                            Assert.DoesNotContain("""access""", dir.Reply.DataJson)

                            Dim access As ExchangeAgentAccessResult = Await client.GetAccessAsync(res1Id, CancellationToken.None)
                            Assert.True(access.Reply.IsOk)
                            Assert.Contains("top-secret", access.AccessJson)
                            Assert.Contains("/Photos", access.AccessJson)
                            Assert.Equal("no-access", (Await client.GetAccessAsync(res2Id, CancellationToken.None)).Reply.Error)
                            Assert.Equal("unknown-resource", (Await client.GetAccessAsync("nope", CancellationToken.None)).Reply.Error)
                            Assert.Equal("bad-request", (Await client.SendAsync(ExchangeAgentPipe.BuildRequest("GetAccess"), 3000, CancellationToken.None)).Error)
                            Assert.Equal("bad-request", (Await client.SendAsync(ExchangeAgentPipe.BuildRequest("CastAnswer"), 3000, CancellationToken.None)).Error)
                            Assert.Equal("unsupported", (Await client.SendAsync(ExchangeAgentPipe.BuildRequest("CastPolicy"), 3000, CancellationToken.None)).Error)
                        End Using
                    End Using
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_directory_long_poll_returns_when_a_newer_revision_arrives() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim name As String = PipeName()
                    Using channel As New ExchangeDeviceLocalChannel(agent, name)
                        channel.Start()
                        Dim client As New ExchangeAgentClient(name)
                        Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetFullDirectory().Revision = 7))

                        ' nothing newer than 7: the call waits; a change arrives while it waits
                        Dim waiting As Task(Of ExchangeAgentDirectoryResult) = client.GetDirectoryAsync(7, 10000, CancellationToken.None)
                        Await Task.Delay(300)
                        Assert.False(waiting.IsCompleted)
                        Dim up As New ExchangeRecordSets()
                        up.Devices.Add("{""deviceId"":""PH"",""deviceName"":""Phone"",""presence"":""online""}")
                        Await acc.PushAsync(New ExchangeEnvelope("changed").SetNumber("revision", 8).SetUpserts(up).SetRemovals(New ExchangeIdSets()))
                        Dim result As ExchangeAgentDirectoryResult = Await waiting
                        Assert.True(result.Reply.IsOk)
                        Assert.Equal(8L, result.Snapshot.Revision)
                        Assert.Single(result.Snapshot.Devices)   ' this device's own record is not in the list (and PH is)
                    End Using
                End Using
            End Using
        End Using
    End Function

End Class
