#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports Xunit

Public Class LiveBroadcastLinkReaderTests

    Private Function CreateFmsBcast1Payload(jsonText As String) As String
        Dim jsonBytes As Byte() = Encoding.UTF8.GetBytes(jsonText)
        Using msOut As New MemoryStream()
            Using gz As New GZipStream(msOut, CompressionLevel.Optimal, True)
                gz.Write(jsonBytes, 0, jsonBytes.Length)
            End Using
            Dim compressedBytes As Byte() = msOut.ToArray()
            Return "FMSBCAST1:" & Convert.ToBase64String(compressedBytes)
        End Using
    End Function

    <Fact>
    Public Sub Android_Intent_Url_Parsed_Successfully()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.100:8080/live-audio"",""mode"":""AUDIO_ONLY"",""title"":""Phone Intent""}"
        Dim bcastText As String = CreateFmsBcast1Payload(json)
        Dim urlEncoded As String = Uri.EscapeDataString(bcastText)

        Dim intentUrl As String = $"intent://fmsbcast/import?payload={urlEncoded}#Intent;scheme=fmsbcast;package=org.sza.fms;end"
        Dim res As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(intentUrl)

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.NotNull(res.Descriptor)
        Assert.Equal("Phone Intent", res.Descriptor.Title)
        Assert.Equal("http://192.168.1.100:8080/live-audio", res.Descriptor.Url)
    End Sub

    <Fact>
    Public Sub Custom_Scheme_Fmsbcast_Url_Parsed_Successfully()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.101:8080/live-audio"",""mode"":""AUDIO_ONLY""}"
        Dim bcastText As String = CreateFmsBcast1Payload(json)
        Dim urlEncoded As String = Uri.EscapeDataString(bcastText)

        Dim link As String = $"fmsbcast://import?payload={urlEncoded}"
        Dim res As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(link)

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.NotNull(res.Descriptor)
        Assert.Equal("http://192.168.1.101:8080/live-audio", res.Descriptor.Url)
    End Sub

    <Fact>
    Public Sub Duplicate_Payload_Parameter_Returns_InvalidDocument()
        Dim link As String = "intent://fmsbcast/import?payload=FMSBCAST1%3Aabc&payload=FMSBCAST1%3Adef#Intent;scheme=fmsbcast;end"
        Dim res As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(link)

        Assert.Equal(LiveReadStatus.InvalidDocument, res.Status)
    End Sub

    <Fact>
    Public Sub Bare_Http_Url_Auto_Detects_Known_Paths_On_Lan_Ipv4()
        Dim paths As String() = {"/live-audio", "/live-audio.aac", "/listen"}
        For Each p As String In paths
            Dim url As String = $"http://192.168.1.105:8080{p}"
            Dim res As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(url)

            Assert.Equal(LiveReadStatus.Ok, res.Status)
            Assert.NotNull(res.Descriptor)
            Assert.Equal(url, res.Descriptor.Url)
            Assert.Equal(LiveBroadcastMode.AudioOnly, res.Descriptor.ModeValue)
            Assert.Equal("192.168.1.105", res.Descriptor.Title)
            Assert.True(res.Descriptor.IsLive)
        Next
    End Sub

    <Fact>
    Public Sub Bare_Http_Url_Ignores_Non_Live_Paths()
        Dim nonLiveUrls As String() = {
            "http://192.168.1.105:8080/movie.mp4",
            "http://192.168.1.105:8080/stream",
            "http://192.168.1.105:8080/"
        }

        For Each url As String In nonLiveUrls
            Dim res As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(url)
            Assert.Equal(LiveReadStatus.NotBroadcast, res.Status)
        Next
    End Sub

    <Fact>
    Public Sub Bare_Http_Url_Ignores_Loopback_And_Hostnames()
        Dim urls As String() = {
            "http://127.0.0.1:8080/listen",
            "http://localhost:8080/listen",
            "http://example.com/listen"
        }

        For Each url As String In urls
            Dim res As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(url)
            Assert.Equal(LiveReadStatus.NotBroadcast, res.Status)
        Next
    End Sub

End Class
#End If
