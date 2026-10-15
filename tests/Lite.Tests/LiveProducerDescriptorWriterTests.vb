#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports Xunit

' Ticket 199 section 5 and section 8: the descriptor and the hand-off channels. Round-trip rule: this
' product's own reader (023) must accept EVERYTHING this ticket writes - barcode text, import file,
' QR payload are the SAME description (producer rule 3), and each form goes through the real parser,
' byte for byte.

Public Class LiveProducerDescriptorWriterTests

    Private Shared Function Sample() As LiveDescriptorInput
        Return New LiveDescriptorInput With {
            .Url = "http://192.168.1.50:8768/live-audio.aac",
            .Mode = "AUDIO_ONLY",
            .Title = "Kitchen PC",
            .SourceId = "fmsw-0123456789ABCDEF",
            .TargetLatencyMs = 1000,
            .AudioCodec = "aac",
            .SampleRateHz = 44100,
            .BitrateBps = 128000,
            .Channels = 1
        }
    End Function

    <Fact>
    Public Sub The_descriptor_round_trips_through_this_products_reader()
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(Sample())
        Assert.NotEqual("", json)

        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Dim d As LiveBroadcastDescriptor = result.Descriptor
        Assert.Equal(1, d.SchemaVersion)
        Assert.Equal("http://192.168.1.50:8768/live-audio.aac", d.Url)
        Assert.Equal("AUDIO_ONLY", d.Mode)
        Assert.Equal(LiveBroadcastMode.AudioOnly, d.ModeValue)
        Assert.Equal("Kitchen PC", d.Title)
        Assert.Equal("fmsw-0123456789ABCDEF", d.SourceId)
        Assert.True(d.IsLive.HasValue AndAlso d.IsLive.Value)
        Assert.Equal(1000, d.TargetLatencyMs)
        Assert.Single(d.Endpoints)
        Assert.Equal("HTTP", d.Endpoints(0).Transport)
        Assert.Equal("aac", d.Endpoints(0).AudioCodec)
        Assert.Equal(44100, d.Endpoints(0).SampleRate)
        Assert.Equal(128000, d.Endpoints(0).Bitrate)
        Assert.True(d.Endpoints(0).IsLive.HasValue AndAlso d.Endpoints(0).IsLive.Value)
        Assert.Equal(1000, d.Endpoints(0).TargetLatencyMs)
    End Sub

    <Fact>
    Public Sub The_barcode_payload_round_trips_and_is_one_gzip_member()
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(Sample())
        Dim payload As String = LiveProducerDescriptorWriter.BarcodePayload(json)

        Assert.True(payload.StartsWith("FMSBCAST1:", StringComparison.Ordinal), "the 4.1 channel is FMSBCAST1: + Base64 of the GZIP of the JSON")
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(payload)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal("http://192.168.1.50:8768/live-audio.aac", result.Descriptor.Url)

        ' The reader accepts exactly ONE gzip member and refuses anything after it: the payload the
        ' writer builds must decompress with nothing trailing (the reader is the authority here).
        Assert.Equal(json, Encoding.UTF8.GetString(DecodedPayloadBytes(payload)))
    End Sub

    Private Shared Function DecodedPayloadBytes(payload As String) As Byte()
        Dim compressed As Byte() = Convert.FromBase64String(payload.Substring("FMSBCAST1:".Length))
        Using ms As New IO.MemoryStream(compressed)
            Using gzip As New IO.Compression.GZipStream(ms, IO.Compression.CompressionMode.Decompress)
                Using out As New IO.MemoryStream()
                    gzip.CopyTo(out)
                    Return out.ToArray()
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Sub The_import_file_content_round_trips()
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(Sample())
        Dim bytes As Byte() = LiveProducerDescriptorWriter.FileContent(json)

        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadFileBytes(bytes)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal("AUDIO_ONLY", result.Descriptor.Mode)
    End Sub

    <Fact>
    Public Sub The_file_name_is_the_contracts_own_shape()
        Assert.Equal("broadcast_1.fmsbcast", LiveProducerDescriptorWriter.FileName(1))
        Assert.Equal("broadcast_12.fmsbcast", LiveProducerDescriptorWriter.FileName(12))
    End Sub

    <Fact>
    Public Sub The_QR_payload_is_the_barcode_payload_the_same_description_on_every_channel()
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(Sample())
        Assert.Equal(LiveProducerDescriptorWriter.BarcodePayload(json), LiveProducerDescriptorWriter.QrPayload(json))
    End Sub

    <Fact>
    Public Sub No_descriptor_without_a_url_or_a_mode()
        Dim noUrl As LiveDescriptorInput = Sample()
        noUrl.Url = ""
        Assert.Equal("", LiveProducerDescriptorWriter.BuildJson(noUrl))

        Dim noMode As LiveDescriptorInput = Sample()
        noMode.Mode = ""
        Assert.Equal("", LiveProducerDescriptorWriter.BuildJson(noMode))
    End Sub

    <Fact>
    Public Sub An_untitled_broadcast_omits_the_optional_title_member()
        Dim input As LiveDescriptorInput = Sample()
        input.Title = "  "
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(input)
        Assert.DoesNotContain("title", json, StringComparison.OrdinalIgnoreCase)
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastReader.ReadText(json).Status)
    End Sub

    <Fact>
    Public Sub The_video_endpoint_carries_the_item_C_and_H_numbers()
        Dim input As LiveDescriptorInput = Sample()
        input.Url = "http://192.168.1.50:8768/a1b2c3d4/live.ts"
        input.Mode = "VIDEO_AUDIO"
        input.VideoCodec = "h264"
        input.TargetLatencyMs = LiveProducerContract.VideoTargetLatencyMs

        Dim json As String = LiveProducerDescriptorWriter.BuildJson(input)
        Assert.Contains("""transport"":""HTTP""", json)
        Assert.Contains("""videoCodec"":""h264""", json)
        Assert.Contains("""targetLatencyMs"":1000", json)

        ' Ticket 201 consumes the producer's HTTP MPEG-TS descriptor without translation.
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
    End Sub

    <Fact>
    Public Sub A_title_with_a_line_break_cannot_inject_a_header_later()
        Assert.Equal("Kitchen PC", LiveProducerWire.SanitizeHeaderValue("Kitchen" & ControlChars.CrLf & "PC"))
    End Sub

    <Fact>
    Public Sub Endpoints_are_ordered_LAN_then_RELAY_then_TUNNEL()
        Dim input As LiveDescriptorInput = Sample()
        input.AllowLanViewers = True
        input.RelayUrl = "fmsx://relay.example.com:443/bcast-123"
        input.TunnelUrl = "fmsx://tunnel.example.com:443/bcast-123"
        input.CertFingerprint = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"
        input.InnerUrl = input.Url

        Dim json As String = LiveProducerDescriptorWriter.BuildJson(input)
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal(3, result.Descriptor.Endpoints.Count)
        Assert.Equal("HTTP", result.Descriptor.Endpoints(0).Transport)
        Assert.Equal("RELAY", result.Descriptor.Endpoints(1).Transport)
        Assert.Equal("TUNNEL", result.Descriptor.Endpoints(2).Transport)
    End Sub

    <Fact>
    Public Sub AllowLanViewers_false_omits_LAN_endpoint_and_picks_relay_url()
        Dim input As LiveDescriptorInput = Sample()
        input.AllowLanViewers = False
        input.RelayUrl = "https://relay.example.com:443/bcast-123"
        input.TunnelUrl = "fmsx://tunnel.example.com:443/b/bcast-123/http"
        input.InnerUrl = input.Url

        Dim json As String = LiveProducerDescriptorWriter.BuildJson(input)
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal(input.RelayUrl, result.Descriptor.Url)
        Assert.Equal(2, result.Descriptor.Endpoints.Count)
        Assert.Equal("RELAY", result.Descriptor.Endpoints(0).Transport)
        Assert.Equal("TUNNEL", result.Descriptor.Endpoints(1).Transport)
    End Sub

    <Fact>
    Public Sub Protected_descriptor_round_trips_auth_without_a_password()
        Dim input As LiveDescriptorInput = Sample()
        input.AuthUser = "listener"
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(input)
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal("password-v1", result.Descriptor.AuthScheme)
        Assert.Equal("listener", result.Descriptor.AuthUser)
        Assert.DoesNotContain("""password""", json)
        Dim link As String = "fmsbcast://live?payload=" & Uri.EscapeDataString(LiveProducerDescriptorWriter.BarcodePayload(json))
        Assert.Equal("listener", LiveBroadcastLinkReader.ReadLink(link).Descriptor.AuthUser)
    End Sub

    <Fact>
    Public Sub BuildBroadcastRecord_creates_valid_json()
        Dim record As String = LiveProducerDescriptorWriter.BuildBroadcastRecord("bc-01", "dev-99", "Kitchen", "AUDIO_ONLY", "{""test"":1}")
        Assert.Contains("""broadcastId"":""bc-01""", record)
        Assert.Contains("""deviceId"":""dev-99""", record)
        Assert.Contains("""title"":""Kitchen""", record)
        Assert.Contains("""mode"":""AUDIO_ONLY""", record)
        Assert.Contains("""descriptor"":{""test"":1}", record)
    End Sub

End Class
#End If
