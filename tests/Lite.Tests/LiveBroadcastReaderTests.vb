#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports Xunit

Public Class LiveBroadcastReaderTests

    Private Function CreateFmsBcast1Payload(jsonText As String) As String
        Dim jsonBytes As Byte() = Encoding.UTF8.GetBytes(jsonText)
        Using msOut As New MemoryStream()
            Using gz As New GZipStream(msOut, CompressionLevel.Optimal, True)
                gz.Write(jsonBytes, 0, jsonBytes.Length)
            End Using
            Dim compressedBytes As Byte() = msOut.ToArray()
            Dim b64 As String = Convert.ToBase64String(compressedBytes)
            Return "FMSBCAST1:" & b64
        End Using
    End Function

    <Fact>
    Public Sub Fixture1_Plain_Schema1_Phone_Json_Accepted_With_All_Fields()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.50:8080/live-audio"",""mode"":""AUDIO_ONLY"",""title"":""Pixel 8"",""sourceId"":""phone-uuid-1234"",""isLive"":true,""targetLatencyMs"":1000}"
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.NotNull(res.Descriptor)
        Assert.Equal(1, res.Descriptor.SchemaVersion)
        Assert.Equal("http://192.168.1.50:8080/live-audio", res.Descriptor.Url)
        Assert.Equal("AUDIO_ONLY", res.Descriptor.Mode)
        Assert.Equal(LiveBroadcastMode.AudioOnly, res.Descriptor.ModeValue)
        Assert.Equal("Pixel 8", res.Descriptor.Title)
        Assert.Equal("phone-uuid-1234", res.Descriptor.SourceId)
        Assert.True(res.Descriptor.IsLive)
        Assert.Equal(1000L, res.Descriptor.TargetLatencyMs)
        Assert.Single(res.Descriptor.Endpoints)
    End Sub

    <Fact>
    Public Sub Fixture2_SchemaVersion_2_Returns_UnsupportedVersion()
        Dim json As String = "{""schemaVersion"":2,""url"":""http://192.168.1.50:8080/live-audio"",""mode"":""AUDIO_ONLY""}"
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)

        Assert.Equal(LiveReadStatus.UnsupportedVersion, res.Status)
        Assert.Null(res.Descriptor)
    End Sub

    <Fact>
    Public Sub Fixture3_VideoAudio_Mode_Returns_UnsupportedMode()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.50:8080/video"",""mode"":""VIDEO_AUDIO""}"
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)

        Assert.Equal(LiveReadStatus.UnsupportedMode, res.Status)
        Assert.Null(res.Descriptor)
    End Sub

    <Fact>
    Public Sub Fixture4_Watch_Listen_With_FutureField_Ignored()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.55:9090/listen"",""mode"":""AUDIO_ONLY"",""futureUnknownField"":true}"
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.NotNull(res.Descriptor)
        Assert.Equal("http://192.168.1.55:9090/listen", res.Descriptor.Url)
        Assert.Equal(LiveBroadcastMode.AudioOnly, res.Descriptor.ModeValue)
    End Sub

    <Fact>
    Public Sub Fixture5_Descriptor_With_Endpoints_Selects_Http_Audio_Endpoint()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.60:8000/fallback"",""mode"":""AUDIO_ONLY"",""endpoints"":[" &
            "{""url"":""rtsp://192.168.1.60:8554/live"",""transport"":""RTSP"",""mode"":""VIDEO_AUDIO"",""videoCodec"":""H264""}," &
            "{""url"":""http://192.168.1.60:8000/live.aac"",""transport"":""HTTP"",""mode"":""AUDIO_ONLY"",""audioCodec"":""AAC"",""sampleRate"":48000,""bitrate"":128000,""isLive"":true,""targetLatencyMs"":500}" &
            "]}"
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.NotNull(res.Descriptor)
        Assert.Equal(2, res.Descriptor.Endpoints.Count)

        Dim chosen As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(res.Descriptor)
        Assert.NotNull(chosen)
        Assert.Equal("http://192.168.1.60:8000/live.aac", chosen.Url)
        Assert.Equal(LiveBroadcastMode.AudioOnly, chosen.ModeValue)
        Assert.Equal(500L, chosen.TargetLatencyMs)
    End Sub

    <Fact>
    Public Sub Fixture6_Fmsbcast1_Barcode_Dynamically_Built_And_Parsed()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.70:8080/live-audio"",""mode"":""AUDIO_ONLY"",""title"":""Live Mic""}"
        Dim barcodeText As String = CreateFmsBcast1Payload(json)

        Assert.StartsWith("FMSBCAST1:", barcodeText, StringComparison.Ordinal)
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(barcodeText)

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.NotNull(res.Descriptor)
        Assert.Equal("Live Mic", res.Descriptor.Title)
        Assert.Equal("http://192.168.1.70:8080/live-audio", res.Descriptor.Url)
    End Sub

    <Fact>
    Public Sub Gzip_Bomb_Decompressing_Over_64KiB_Returns_TooLarge()
        ' Construct a small payload that decompresses into > 65536 bytes
        Dim hugeJson As String = "{""schemaVersion"":1,""url"":""http://192.168.1.80:8080/live"",""mode"":""AUDIO_ONLY"",""padding"":""" & New String("A"c, 70000) & """}"
        Dim barcodeText As String = CreateFmsBcast1Payload(hugeJson)

        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(barcodeText)
        Assert.Equal(LiveReadStatus.TooLarge, res.Status)
    End Sub

    <Fact>
    Public Sub Raw_Input_Over_64KiB_Returns_TooLarge()
        Dim largeText As String = "{" & New String(" "c, 66000) & "}"
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(largeText)
        Assert.Equal(LiveReadStatus.TooLarge, res.Status)
    End Sub

    <Fact>
    Public Sub Loopback_Hosts_Refused_With_LoopbackHost_Status()
        Dim loopbacks As String() = {
            "http://127.0.0.1:8080/live",
            "http://localhost:8080/live",
            "http://[::1]:8080/live",
            "http://0.0.0.0:8080/live",
            "http://127.0.0.2:8080/live"
        }

        For Each url As String In loopbacks
            Dim json As String = $"{{""schemaVersion"":1,""url"":""{url}"",""mode"":""AUDIO_ONLY""}}"
            Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
            Assert.Equal(LiveReadStatus.LoopbackHost, res.Status)
        Next
    End Sub

    <Fact>
    Public Sub Relative_Or_Invalid_Url_Returns_InvalidDocument()
        Dim invalidUrls As String() = {
            "/relative/path",
            "not-a-url",
            "ftp://",
            ""
        }

        For Each url As String In invalidUrls
            Dim json As String = $"{{""schemaVersion"":1,""url"":""{url}"",""mode"":""AUDIO_ONLY""}}"
            Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
            Assert.Equal(LiveReadStatus.InvalidDocument, res.Status)
        Next
    End Sub

    <Fact>
    Public Sub Negative_Or_Zero_SchemaVersion_Returns_InvalidDocument()
        Dim jsonZero As String = "{""schemaVersion"":0,""url"":""http://192.168.1.50:8080/live"",""mode"":""AUDIO_ONLY""}"
        Dim resZero As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(jsonZero)
        Assert.Equal(LiveReadStatus.InvalidDocument, resZero.Status)

        Dim jsonNeg As String = "{""schemaVersion"":-1,""url"":""http://192.168.1.50:8080/live"",""mode"":""AUDIO_ONLY""}"
        Dim resNeg As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(jsonNeg)
        Assert.Equal(LiveReadStatus.InvalidDocument, resNeg.Status)
    End Sub

    <Fact>
    Public Sub File_Import_Supports_Both_Json_And_Fmsbcast1_With_BOM()
        Dim tempJsonFile As String = Path.GetTempFileName() & ".fmsbcast"
        Dim tempBarcodeFile As String = Path.GetTempFileName() & ".fmsbcast"

        Try
            Dim jsonContent As String = "{""schemaVersion"":1,""url"":""http://192.168.1.90:8080/live"",""mode"":""AUDIO_ONLY"",""title"":""From JSON File""}"
            ' Write with UTF-8 BOM
            File.WriteAllText(tempJsonFile, jsonContent, Encoding.UTF8)

            Dim res1 As LiveBroadcastReadResult = LiveBroadcastReader.ReadFile(tempJsonFile)
            Assert.Equal(LiveReadStatus.Ok, res1.Status)
            Assert.Equal("From JSON File", res1.Descriptor.Title)

            Dim barcodeContent As String = CreateFmsBcast1Payload("{""schemaVersion"":1,""url"":""http://192.168.1.91:8080/live"",""mode"":""AUDIO_ONLY"",""title"":""From Barcode File""}")
            File.WriteAllText(tempBarcodeFile, barcodeContent, Encoding.UTF8)

            Dim res2 As LiveBroadcastReadResult = LiveBroadcastReader.ReadFile(tempBarcodeFile)
            Assert.Equal(LiveReadStatus.Ok, res2.Status)
            Assert.Equal("From Barcode File", res2.Descriptor.Title)
        Finally
            If File.Exists(tempJsonFile) Then File.Delete(tempJsonFile)
            If File.Exists(tempBarcodeFile) Then File.Delete(tempBarcodeFile)
        End Try
    End Sub

End Class
#End If
