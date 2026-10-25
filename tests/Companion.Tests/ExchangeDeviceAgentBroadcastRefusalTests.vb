Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 263 T-6, second half (DEVICE-EXCHANGE 7.4, item DT), the BROADCASTER side as the device agent wires it: the answer to
''' `broadcast-start` is `broadcast-started` or `refused`. A `refused` that arrives while a start awaits its answer is THAT start's
''' refusal and is raised as a typed event (the id and the reason, nothing else); a `refused` with no start awaiting is not the
''' broadcast's and goes the way it always went. The stub account answers a start only when the test queues an answer.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeDeviceAgentBroadcastRefusalTests

    Private Const BroadcastId As String = "bcast-0123456789abcdeR"

    Private Shared Async Function OnlineAgentAsync(rig As ExchangeDeviceAgentTests.Rig, acc As ExchangeStubAccount) As Task(Of ExchangeDeviceAgent)
        Dim agent As ExchangeDeviceAgent = rig.NewAgent()
        Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
        Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
        Return agent
    End Function

    ''' <summary>A plain LAN broadcast (no password, no relay, no tunnel, not sealed): the start goes out and nothing is uploaded.</summary>
    Private Shared Function StateFor() As LiveProducerState
        Dim input As New LiveDescriptorInput With {
            .Url = "http://127.0.0.1:1/live.aac", .Mode = "AUDIO_ONLY", .Title = "t", .SourceId = "src", .AudioCodec = "aac",
            .SampleRateHz = 44100, .Channels = 1, .AllowLanViewers = True, .InnerUrl = "http://127.0.0.1:1/live.aac"
        }
        Return New LiveProducerState With {
            .Running = True, .Mode = "AUDIO_ONLY", .Url = input.Url, .Port = 1, .ListenerAddress = "127.0.0.1", .SourceName = "src",
            .BroadcastId = BroadcastId, .Credentials = LiveCredentialPairs.ForLink("", iterations:=2),
            .DescriptorJson = LiveProducerDescriptorWriter.BuildJson(input)
        }
    End Function

    Private Shared Function NewHub() As LiveBroadcastHub
        Return New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() 0L)
    End Function

    ''' <summary>What the agent raised: the start refusals, and (as the proof that a message was handled) the loose server messages.</summary>
    Private NotInheritable Class Seen
        Public ReadOnly Refusals As New ConcurrentQueue(Of Tuple(Of String, ExchangeRefusal))()
        Public ReadOnly Others As New ConcurrentQueue(Of String)()

        Public Sub OnRefused(id As String, reason As ExchangeRefusal)
            Refusals.Enqueue(Tuple.Create(id, reason))
        End Sub

        Public Sub OnOther(message As ExchangeEnvelope)
            If message.Type = "refused" Then Others.Enqueue(message.Type)     ' other loose messages (directory pushes ..) are none of this test's business
        End Sub
    End Class

    Private Shared Function Watch(agent As ExchangeDeviceAgent) As Seen
        Dim seen As New Seen()
        AddHandler agent.BroadcastStartRefused, AddressOf seen.OnRefused
        AddHandler agent.ServerMessage, AddressOf seen.OnOther
        Return seen
    End Function

    Private Shared Function LooseRefusal(reason As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Refused).SetText("reason", reason)
    End Function

    <Theory>
    <InlineData("capacity")>
    <InlineData("not-permitted")>
    <InlineData("rate-limited")>
    <InlineData("some-reason-this-build-does-not-know")>
    Public Async Function A_refused_start_raises_the_typed_reason_with_the_broadcast_id_and_nothing_else(wire As String) As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                    Dim seen As Seen = Watch(agent)
                    acc.RefuseNextBroadcastStart(wire)
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(StateFor(), hub, hub)
                    Try
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() Not seen.Refusals.IsEmpty, 20))
                        Dim got As Tuple(Of String, ExchangeRefusal) = Nothing
                        Assert.True(seen.Refusals.TryPeek(got))
                        Assert.Equal(BroadcastId, got.Item1)
                        Assert.Equal(ExchangeRefusals.Parse(wire), got.Item2)
                        Assert.Single(seen.Refusals)
                        Assert.Null(agent.BroadcastStartAwaitingId)          ' answered: the wait is over
                        Assert.Empty(seen.Others)                           ' the refusal was the start's, not a loose server message
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_started_answer_closes_the_wait_and_a_later_refused_is_not_the_broadcasts() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                    Dim seen As Seen = Watch(agent)
                    acc.AcceptNextBroadcastStart()
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(StateFor(), hub, hub)
                    Try
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-start") >= 1, 20))
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.BroadcastStartAwaitingId Is Nothing, 20))
                        Assert.Empty(seen.Refusals)

                        Await acc.PushAsync(LooseRefusal("capacity"))
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() Not seen.Others.IsEmpty, 20))   ' it was handled, as a loose message
                        Assert.Equal("refused", seen.Others.First())
                        Assert.Empty(seen.Refusals)
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_with_no_start_awaiting_is_not_attributed_to_the_broadcast() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                    Dim seen As Seen = Watch(agent)
                    Assert.Null(agent.BroadcastStartAwaitingId)
                    Await acc.PushAsync(LooseRefusal("capacity"))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() Not seen.Others.IsEmpty, 20))
                    Assert.Equal("refused", seen.Others.First())
                    Assert.Empty(seen.Refusals)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_end_of_the_broadcast_clears_the_wait_so_a_late_refused_is_not_its_refusal() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                    Dim seen As Seen = Watch(agent)
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(StateFor(), hub, hub)       ' the stub does not answer this start
                    Try
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-start") >= 1, 20))
                        Assert.Equal(BroadcastId, agent.BroadcastStartAwaitingId)
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                    Assert.Null(agent.BroadcastStartAwaitingId)

                    Await acc.PushAsync(LooseRefusal("capacity"))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() Not seen.Others.IsEmpty, 20))
                    Assert.Empty(seen.Refusals)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function After_a_reconnect_the_repeated_start_awaits_its_answer_and_a_refusal_of_it_is_attributed() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                    Dim seen As Seen = Watch(agent)
                    acc.AcceptNextBroadcastStart()
                    Dim hub As LiveBroadcastHub = NewHub()
                    agent.OnBroadcastStarted(StateFor(), hub, hub)
                    Try
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("broadcast-start") >= 1, 20))
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.BroadcastStartAwaitingId Is Nothing, 20))

                        acc.RefuseNextBroadcastStart("capacity")           ' the answer to the start the welcome sends again
                        acc.DropAll()
                        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() Not seen.Refusals.IsEmpty, 30))
                        Dim got As Tuple(Of String, ExchangeRefusal) = Nothing
                        Assert.True(seen.Refusals.TryPeek(got))
                        Assert.Equal(BroadcastId, got.Item1)
                        Assert.Equal(ExchangeRefusal.Capacity, got.Item2)
                        Assert.True(acc.CountOf("broadcast-start") >= 2)
                    Finally
                        agent.OnBroadcastStopped(BroadcastId)
                    End Try
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_host_passes_a_start_refusal_on_once_even_when_it_listens_to_the_same_agent_twice() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                    Dim forwarded As New ConcurrentQueue(Of Tuple(Of String, ExchangeRefusal))()
                    Dim handler As Action(Of String, ExchangeRefusal) = Sub(id As String, reason As ExchangeRefusal) forwarded.Enqueue(Tuple.Create(id, reason))
                    AddHandler LiveBroadcastHost.ExchangeStartRefused, handler
                    Try
                        LiveBroadcastHost.ListenTo(agent)
                        LiveBroadcastHost.ListenTo(agent)                   ' the second broadcast of a session does this again
                        acc.RefuseNextBroadcastStart("not-permitted")
                        Dim hub As LiveBroadcastHub = NewHub()
                        agent.OnBroadcastStarted(StateFor(), hub, hub)
                        Try
                            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() Not forwarded.IsEmpty, 20))
                            Await Task.Delay(400)                            ' a second forwarder would have shown by now
                            Assert.Single(forwarded)
                            Dim got As Tuple(Of String, ExchangeRefusal) = Nothing
                            Assert.True(forwarded.TryPeek(got))
                            Assert.Equal(BroadcastId, got.Item1)
                            Assert.Equal(ExchangeRefusal.NotPermitted, got.Item2)
                        Finally
                            agent.OnBroadcastStopped(BroadcastId)
                        End Try
                    Finally
                        RemoveHandler LiveBroadcastHost.ExchangeStartRefused, handler
                    End Try
                End Using
            End Using
        End Using
    End Function
End Class

''' <summary>
''' Ticket 263 T-6: the one sentence the Live window shows when the exchange server did not list the broadcast. The cause is told in
''' words (capacity, not permitted and rate limited have their own; anything else one generic wording), the raw reason code is never
''' printed, and the second sentence ("still served on your network") is there only when a LAN endpoint is on air.
''' </summary>
<Collection("Exchange device host")>
Public Class LiveBroadcastStartRefusalTextTests

    Private Shared ReadOnly Languages As String() = {"ru", "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh"}

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_three_named_causes_and_the_generic_wording_are_four_different_words_in_every_language()
        For Each code As String In Languages
            InLanguage(code, Sub()
                                 Dim words As String() = {
                                     Live_Broadcast_Form.StartRefusalReason(ExchangeRefusal.Capacity),
                                     Live_Broadcast_Form.StartRefusalReason(ExchangeRefusal.NotPermitted),
                                     Live_Broadcast_Form.StartRefusalReason(ExchangeRefusal.RateLimited),
                                     Live_Broadcast_Form.StartRefusalReason(ExchangeRefusal.Unknown)}
                                 Assert.Equal(4, words.Distinct().Count())
                                 Assert.All(words, Sub(w) Assert.False(String.IsNullOrWhiteSpace(w), code))
                             End Sub)
        Next
    End Sub

    <Fact>
    Public Sub Every_other_refusal_gets_the_generic_wording_and_no_raw_code_is_ever_printed()
        InLanguage("en", Sub()
                             Dim generic As String = Live_Broadcast_Form.StartRefusalReason(ExchangeRefusal.Unknown)
                             Assert.Equal("the server declined the request", generic)
                             For Each refusal As ExchangeRefusal In ExchangeRefusals.All()
                                 Dim reason As String = Live_Broadcast_Form.StartRefusalReason(refusal)
                                 If refusal <> ExchangeRefusal.Capacity AndAlso refusal <> ExchangeRefusal.NotPermitted AndAlso refusal <> ExchangeRefusal.RateLimited Then
                                     Assert.Equal(generic, reason)
                                 End If
                                 Dim code As String = ExchangeRefusals.WireName(refusal)
                                 Assert.DoesNotContain(code, Live_Broadcast_Form.StartRefusalLine(refusal, True), StringComparison.OrdinalIgnoreCase)
                                 Assert.DoesNotContain(code, Live_Broadcast_Form.StartRefusalLine(refusal, False), StringComparison.OrdinalIgnoreCase)
                             Next
                         End Sub)
    End Sub

    <Fact>
    Public Sub The_second_sentence_is_there_only_when_a_lan_endpoint_is_on_air()
        InLanguage("en", Sub()
                             Dim withLan As String = Live_Broadcast_Form.StartRefusalLine(ExchangeRefusal.Capacity, True)
                             Dim withoutLan As String = Live_Broadcast_Form.StartRefusalLine(ExchangeRefusal.Capacity, False)
                             Assert.Equal("The exchange server did not list this broadcast (the server's limit is reached). It is still served on your network.", withLan)
                             Assert.Equal("The exchange server did not list this broadcast (the server's limit is reached).", withoutLan)
                         End Sub)
        For Each code As String In Languages
            InLanguage(code, Sub()
                                 Dim withLan As String = Live_Broadcast_Form.StartRefusalLine(ExchangeRefusal.RateLimited, True)
                                 Dim withoutLan As String = Live_Broadcast_Form.StartRefusalLine(ExchangeRefusal.RateLimited, False)
                                 Assert.NotEqual(withLan, withoutLan)
                                 Assert.True(withLan.Length > withoutLan.Length, code)
                             End Sub)
        Next
    End Sub
End Class
