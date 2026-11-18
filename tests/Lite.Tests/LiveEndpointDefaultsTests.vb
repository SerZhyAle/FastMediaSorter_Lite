#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports Xunit

' LIVE-BROADCAST 1.7 items V and W (21_contract_amendment_2026-11-12.md, "Cases a reader is held to"), ticket L0329:
' what a missing optional endpoint member means. Every test is an input and the reading the contract gives it.
' The RTSP rows of the contract are not played by this receiver (an RTSP endpoint is skipped), so they are not here.

Public Class LiveEndpointDefaultsTests

    Private Shared Function Read(json As String) As LiveBroadcastDescriptor
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json.Replace("'"c, """"c))
        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Return res.Descriptor
    End Function

    Private Shared Function EffectiveMs(descriptor As LiveBroadcastDescriptor, endpoint As LiveBroadcastEndpoint) As Integer
        Return LivePlaybackOptions.EffectiveTargetMs(LiveEndpointSelector.TargetLatencyOf(descriptor, endpoint))
    End Function

    ' ------------------------------------------------------------------ item V: targetLatencyMs by transport

    <Fact>
    Public Sub V_An_Http_Audio_Endpoint_With_No_Latency_And_No_Top_Level_Value_Reads_1000()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.aac','mode':'AUDIO_ONLY','endpoints':[" &
            "{'url':'http://192.168.1.50:8768/live.aac','transport':'HTTP','mode':'AUDIO_ONLY'}]}")
        Dim ep As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(d)

        Assert.Equal(1000, EffectiveMs(d, ep))
    End Sub

    <Fact>
    Public Sub V_A_Direct_Http_Endpoint_Takes_The_Top_Level_Value_When_It_States_None()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.aac','mode':'AUDIO_ONLY','targetLatencyMs':500,'endpoints':[" &
            "{'url':'http://192.168.1.50:8768/live.aac','transport':'HTTP','mode':'AUDIO_ONLY'}]}")
        Dim ep As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(d)

        Assert.Equal(500, EffectiveMs(d, ep))
    End Sub

    <Fact>
    Public Sub V_A_Relay_Endpoint_With_No_Latency_Reads_2000_Never_The_Top_Level_Value()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.aac','mode':'AUDIO_ONLY','targetLatencyMs':200,'endpoints':[" &
            "{'url':'https://exchange.example.com/b/AAECAwQFBgcICQoLDA0ODw/live.aac','transport':'RELAY','mode':'AUDIO_ONLY'}]}")
        Dim ep As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(d)

        Assert.NotNull(ep)
        Assert.Equal("RELAY", ep.Transport)
        Assert.Equal(2000, EffectiveMs(d, ep))
    End Sub

    <Fact>
    Public Sub V_A_Tunnel_Endpoint_With_No_Latency_Reads_2000_Never_The_Top_Level_Value()
        Dim d As New LiveBroadcastDescriptor With {.TargetLatencyMs = 200}
        Dim ep As New LiveBroadcastEndpoint With {.Transport = "TUNNEL"}

        Assert.Equal(2000, EffectiveMs(d, ep))
    End Sub

    <Theory>
    <InlineData("RELAY")>
    <InlineData("TUNNEL")>
    <InlineData("HTTP")>
    Public Sub V_The_Endpoints_Own_Value_Always_Wins(transport As String)
        Dim d As New LiveBroadcastDescriptor With {.TargetLatencyMs = 1500}
        Dim ep As New LiveBroadcastEndpoint With {.Transport = transport, .TargetLatencyMs = 400}

        Assert.Equal(400, EffectiveMs(d, ep))
    End Sub

    <Fact>
    Public Sub V_A_Default_Is_Still_Bounded_By_The_Clamp_Of_Item_L()
        Assert.InRange(EffectiveMs(Nothing, New LiveBroadcastEndpoint With {.Transport = "RELAY"}),
                       LivePlaybackOptions.MinTargetLatencyMs, LivePlaybackOptions.MaxTargetLatencyMs)
    End Sub

    ' ------------------------------------------------------------------ item W: mode, transport, an endpoint that cannot be read

    <Fact>
    Public Sub W_A_Descriptor_With_No_Endpoints_List_Is_One_Http_Endpoint_Of_Its_Root_Members()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.ts','mode':'VIDEO_AUDIO','targetLatencyMs':700}")
        Dim candidates As List(Of LiveBroadcastEndpoint) = LiveEndpointSelector.Candidates(d)

        Assert.Single(candidates)
        Assert.Equal("HTTP", candidates(0).Transport)
        Assert.Equal(LiveBroadcastMode.VideoAudio, candidates(0).ModeValue)
        Assert.Equal(700, EffectiveMs(d, candidates(0)))
    End Sub

    <Fact>
    Public Sub W_An_Endpoint_With_No_Mode_Takes_The_Descriptors_Mode()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.ts','mode':'VIDEO_AUDIO','endpoints':[" &
            "{'url':'http://192.168.1.50:8768/live.ts','transport':'HTTP'}]}")
        Dim candidates As List(Of LiveBroadcastEndpoint) = LiveEndpointSelector.Candidates(d)

        Assert.Single(candidates)
        Assert.Equal(LiveBroadcastMode.VideoAudio, candidates(0).ModeValue)
        Assert.Equal("VIDEO_AUDIO", candidates(0).Mode)
    End Sub

    <Fact>
    Public Sub W_An_Endpoint_Whose_Stated_Mode_Is_Not_Understood_Is_Skipped_And_The_Descriptor_Is_Kept()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.aac','mode':'AUDIO_ONLY','endpoints':[" &
            "{'url':'http://192.168.1.50:8768/odd','transport':'HTTP','mode':'HOLOGRAM'}," &
            "{'url':'http://192.168.1.50:8768/live.aac','transport':'HTTP','mode':'AUDIO_ONLY'}]}")
        Dim candidates As List(Of LiveBroadcastEndpoint) = LiveEndpointSelector.Candidates(d)

        Assert.Single(candidates)
        Assert.Equal("http://192.168.1.50:8768/live.aac", candidates(0).Url)
    End Sub

    <Fact>
    Public Sub W_An_Endpoint_With_No_Transport_And_A_Scheme_That_Is_Neither_Http_Nor_Rtsp_Is_Skipped_The_Others_Are_Tried()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.aac','mode':'AUDIO_ONLY','endpoints':[" &
            "{'url':'fmsx://exchange.example.com/b/AAECAwQFBgcICQoLDA0ODw/http','mode':'AUDIO_ONLY'}," &
            "{'url':'http://192.168.1.50:8768/live.aac','transport':'HTTP','mode':'AUDIO_ONLY'}]}")
        Dim candidates As List(Of LiveBroadcastEndpoint) = LiveEndpointSelector.Candidates(d)

        Assert.Single(candidates)
        Assert.Equal("http://192.168.1.50:8768/live.aac", candidates(0).Url)
    End Sub

    <Fact>
    Public Sub W_An_Endpoint_With_No_Url_Is_Skipped_And_The_Descriptor_Is_Kept()
        Dim d As LiveBroadcastDescriptor = Read(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live.aac','mode':'AUDIO_ONLY','endpoints':[" &
            "{'transport':'HTTP','mode':'AUDIO_ONLY'}," &
            "{'url':'http://192.168.1.50:8768/live.aac','transport':'HTTP','mode':'AUDIO_ONLY'}]}")
        Dim candidates As List(Of LiveBroadcastEndpoint) = LiveEndpointSelector.Candidates(d)

        Assert.Single(d.Endpoints)
        Assert.Single(candidates)
    End Sub

End Class
#End If
