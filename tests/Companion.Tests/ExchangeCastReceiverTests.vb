Option Strict On

Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

<Collection("Exchange device host")>
Public Class ExchangeCastReceiverTests
    Private Const Broadcast As String = "{""broadcastId"":""abcdefghijklmnopqrstuA"",""deviceId"":""phone"",""mode"":""AUDIO_ONLY"",""descriptor"":{""schemaVersion"":1,""url"":""http://192.168.1.2/listen"",""mode"":""AUDIO_ONLY""}}"

    Private Shared Async Function Request(agent As ExchangeDeviceAgent, kind As String, Optional json As String = "{}") As Task(Of ExchangeLocalResult)
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Return Await agent.HandleAsync(kind, doc.RootElement, CancellationToken.None)
        End Using
    End Function

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Async Function A_Cast_Is_Queued_For_The_Viewer_And_Answered_Exactly_Once(accepted As Boolean) As Task
        Using account As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent()
            Await agent.EnrollAsync(rig.Request(account), CancellationToken.None)
            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online AndAlso account.LiveConnections() > 0))
            Await Request(agent, ExchangeAgentPipe.RequestGetCastOffer)
            Await account.PushAsync(New ExchangeEnvelope("cast-offer").SetRecord("broadcast", Broadcast).SetText("fromDeviceId", "phone"))
            Dim offer As ExchangeLocalResult = Nothing
            For attempt As Integer = 0 To 100
                offer = Await Request(agent, ExchangeAgentPipe.RequestGetCastOffer)
                If offer.DataJson <> "null" Then Exit For
                Await Task.Delay(10)
            Next
            Assert.Equal(Broadcast, offer.DataJson)
            Dim json As String = "{""broadcastId"":""abcdefghijklmnopqrstuA"",""accepted"":" & If(accepted, "true", "false") & ",""reason"":""declined""}"
            Assert.True((Await Request(agent, ExchangeAgentPipe.RequestCastAnswer, json)).Ok)
            Assert.False((Await Request(agent, ExchangeAgentPipe.RequestCastAnswer, json)).Ok)
            Assert.Equal("null", (Await Request(agent, ExchangeAgentPipe.RequestGetCastOffer)).DataJson)
            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() account.Received().Exists(Function(line) line.EndsWith(":cast-answer", StringComparison.Ordinal))))
            Assert.DoesNotContain("abcdefghijklmnopqrstuA", rig.Everything())
            If accepted Then
                Await account.PushAsync(New ExchangeEnvelope("cast-stop").SetText("broadcastId", "abcdefghijklmnopqrstuA").SetText("receiverDeviceId", agent.GetStatus().DeviceId))
                Dim stopReply As ExchangeLocalResult = Nothing
                For attempt As Integer = 0 To 100
                    stopReply = Await Request(agent, ExchangeAgentPipe.RequestGetCastOffer)
                    If stopReply.DataJson.Contains("stopBroadcastId", StringComparison.Ordinal) Then Exit For
                    Await Task.Delay(10)
                Next
                Assert.Contains("stopBroadcastId", stopReply.DataJson)
                Assert.Equal("null", (Await Request(agent, ExchangeAgentPipe.RequestGetCastOffer)).DataJson)
            End If
        End Using
    End Function

    <Fact>
    Public Async Function Directory_Lists_Broadcasts_Without_Credentials_And_Selection_Fetches_The_Private_Record() As Task
        Using account As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent()
            account.Broadcasts.Add(Broadcast.Substring(0, Broadcast.Length - 1) & ",""credentials"":{""user"":""guest"",""password"":""private-secret""}}")
            Await agent.EnrollAsync(rig.Request(account), CancellationToken.None)
            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetDirectory().Broadcasts.Count = 1))
            Dim listing As ExchangeLocalResult = Await Request(agent, ExchangeAgentPipe.RequestGetLiveDirectory)
            Assert.True(listing.Ok)
            Assert.DoesNotContain("private-secret", listing.DataJson)
            Dim selection As ExchangeLocalResult = Await Request(agent, ExchangeAgentPipe.RequestGetBroadcast, "{""broadcastId"":""abcdefghijklmnopqrstuA""}")
            Assert.True(selection.Ok)
            Assert.Contains("private-secret", selection.DataJson)
            Assert.DoesNotContain("private-secret", rig.Everything())
            Assert.False((Await Request(agent, ExchangeAgentPipe.RequestGetBroadcast, "{""broadcastId"":""missing""}")).Ok)
        End Using
    End Function

    <Fact>
    Public Async Function With_No_Viewer_A_Cast_Is_Refused_Instead_Of_Queued_Indefinitely() As Task
        Using account As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent()
            Await agent.EnrollAsync(rig.Request(account), CancellationToken.None)
            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() account.LiveConnections() > 0))
            Await account.PushAsync(New ExchangeEnvelope("cast-offer").SetRecord("broadcast", Broadcast).SetText("fromDeviceId", "phone"))
            Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() account.Received().Exists(Function(line) line.EndsWith(":cast-answer", StringComparison.Ordinal))))
            Assert.Equal("null", (Await Request(agent, ExchangeAgentPipe.RequestGetCastOffer)).DataJson)
        End Using
    End Function
End Class
