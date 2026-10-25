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

    Private Shared Function VideoSample() As LiveDescriptorInput
        Dim input As LiveDescriptorInput = Sample()
        input.Url = "http://192.168.1.50:8768/a1b2c3d4/live.ts"
        input.Mode = "VIDEO_AUDIO"
        input.VideoCodec = "h264"
        input.TargetLatencyMs = LiveProducerContract.VideoTargetLatencyMs
        input.AllowLanViewers = True
        input.RelayUrl = "https://relay.example.com:443/v2/b/bcast-123/stream"
        input.TunnelUrl = "fmsx://tunnel.example.com:443/b/bcast-123/http"
        input.InnerUrl = input.Url
        Return input
    End Function

    ''' <summary>The "keyframeIntervalMs" member of every endpoint object of <paramref name="json"/>, in order (-1 when absent).</summary>
    Private Shared Function KeyframeMembers(json As String) As List(Of Long)
        Dim found As New List(Of Long)()
        Using doc As Text.Json.JsonDocument = Text.Json.JsonDocument.Parse(json)
            For Each endpoint As Text.Json.JsonElement In doc.RootElement.GetProperty("endpoints").EnumerateArray()
                Dim member As Text.Json.JsonElement = Nothing
                found.Add(If(endpoint.TryGetProperty("keyframeIntervalMs", member), member.GetInt64(), -1L))
            Next
        End Using
        Return found
    End Function

    <Fact>
    Public Sub Every_video_endpoint_carries_the_keyframe_interval_the_encoder_is_configured_for()
        ' LIVE-BROADCAST item L: LAN HTTP, RELAY and TUNNEL of a video mode all write the encoder's own interval; RELAY never above 2000.
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(VideoSample())
        Dim members As List(Of Long) = KeyframeMembers(json)
        Assert.Equal(3, members.Count)
        Assert.All(members, Sub(value) Assert.Equal(CLng(LiveProducerContract.KeyframeIntervalMs), value))
        Assert.True(members(1) <= 2000L, "a RELAY endpoint never carries a value above 2000")

        ' the same member rides in a VIDEO_ONLY descriptor, and this product's own reader still accepts the whole document
        Dim videoOnly As LiveDescriptorInput = VideoSample()
        videoOnly.Mode = "VIDEO_ONLY"
        videoOnly.AudioCodec = Nothing
        Dim onlyJson As String = LiveProducerDescriptorWriter.BuildJson(videoOnly)
        Assert.All(KeyframeMembers(onlyJson), Sub(value) Assert.Equal(1000L, value))
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastReader.ReadText(json).Status)
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastReader.ReadText(onlyJson).Status)
    End Sub

    <Fact>
    Public Sub The_keyframe_interval_follows_each_endpoint_that_is_written()
        Dim lanOff As LiveDescriptorInput = VideoSample()
        lanOff.AllowLanViewers = False
        Assert.Equal(2, KeyframeMembers(LiveProducerDescriptorWriter.BuildJson(lanOff)).Count)

        Dim directoryRecord As String = LiveProducerDescriptorWriter.BuildJson(VideoSample().ForRecord())
        Assert.All(KeyframeMembers(directoryRecord), Sub(value) Assert.Equal(1000L, value))
    End Sub

    <Fact>
    Public Sub An_audio_only_endpoint_does_not_carry_a_keyframe_interval()
        Dim input As LiveDescriptorInput = Sample()
        input.RelayUrl = "https://relay.example.com:443/v2/b/bcast-123/stream"
        input.TunnelUrl = "fmsx://tunnel.example.com:443/b/bcast-123/http"
        input.InnerUrl = input.Url
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(input)
        Assert.DoesNotContain("keyframeIntervalMs", json, StringComparison.Ordinal)
        Dim members As List(Of Long) = KeyframeMembers(json)
        Assert.Equal(3, members.Count)
        Assert.All(members, Sub(value) Assert.Equal(-1L, value))
    End Sub

    <Fact>
    Public Sub The_copied_link_targets_the_importer_and_reads_back_through_the_link_reader()
        ' LIVE-BROADCAST 4.3: `fmsbcast://import?payload=` + the URL-encoded FMSBCAST1 text, the same description as the other channels.
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(VideoSample())
        Dim barcode As String = LiveProducerDescriptorWriter.BarcodePayload(json)
        Dim link As String = LiveProducerDescriptorWriter.LinkText(barcode)

        Assert.StartsWith("fmsbcast://import?payload=", link, StringComparison.Ordinal)
        Assert.Equal("fmsbcast://import?payload=" & Uri.EscapeDataString(barcode), link)
        ' the Base64 text carries '+', '/' and '=': none of them travels raw inside the query
        Assert.DoesNotContain("+", link.Substring("fmsbcast://import?payload=".Length), StringComparison.Ordinal)
        Assert.DoesNotContain("FMSBCAST1:", link, StringComparison.Ordinal)

        Dim read As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(link)
        Assert.Equal(LiveReadStatus.Ok, read.Status)
        Assert.Equal(LiveBroadcastReader.ReadText(barcode).Descriptor.Url, read.Descriptor.Url)
        Assert.Equal(3, read.Descriptor.Endpoints.Count)
    End Sub

    <Fact>
    Public Sub The_link_carries_no_password_and_an_empty_barcode_gives_no_link()
        Dim input As LiveDescriptorInput = Sample()
        input.AuthUser = "listener"
        Dim link As String = LiveProducerDescriptorWriter.LinkText(LiveProducerDescriptorWriter.BarcodePayload(LiveProducerDescriptorWriter.BuildJson(input)))
        Assert.Equal("listener", LiveBroadcastLinkReader.ReadLink(link).Descriptor.AuthUser)
        Dim carried As String = Encoding.UTF8.GetString(DecodedPayloadBytes(Uri.UnescapeDataString(link.Substring(LiveProducerContract.LinkPrefix.Length))))
        Assert.Contains("""scheme"":""password-v1""", carried)
        Assert.DoesNotContain("""password""", carried, StringComparison.Ordinal)
        Assert.Equal("", LiveProducerDescriptorWriter.LinkText(""))
        Assert.Equal("", LiveProducerDescriptorWriter.LinkText(Nothing))
    End Sub

    <Fact>
    Public Sub The_reader_still_reads_the_earlier_live_link_form_that_is_already_out_in_the_world()
        ' A link copied by an earlier build is `fmsbcast://live?payload=`. The importer's host is the writer's now, but a reader
        ' reads every version that wrote data: the same barcode text opens through both hosts and yields the same description.
        Dim barcode As String = LiveProducerDescriptorWriter.BarcodePayload(LiveProducerDescriptorWriter.BuildJson(VideoSample()))
        Dim older As String = "fmsbcast://live?payload=" & Uri.EscapeDataString(barcode)
        Dim current As String = LiveProducerDescriptorWriter.LinkText(barcode)

        Dim fromOlder As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(older)
        Dim fromCurrent As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(current)
        Assert.Equal(LiveReadStatus.Ok, fromOlder.Status)
        Assert.Equal(LiveReadStatus.Ok, fromCurrent.Status)
        Assert.NotEqual(older, current)
        Assert.Equal(fromCurrent.Descriptor.Url, fromOlder.Descriptor.Url)
        Assert.Equal(fromCurrent.Descriptor.Endpoints.Count, fromOlder.Descriptor.Endpoints.Count)
    End Sub

    <Fact>
    Public Sub BuildBroadcastRecord_creates_valid_json()
        Dim started As New Date(2026, 10, 22, 9, 30, 0, DateTimeKind.Utc)
        Dim updated As New Date(2026, 10, 22, 9, 31, 15, DateTimeKind.Utc)
        Dim record As String = LiveProducerDescriptorWriter.BuildBroadcastRecord("bc-01", "dev-99", "Kitchen", "AUDIO_ONLY", "{""test"":1}", started, updated)
        Assert.Contains("""broadcastId"":""bc-01""", record)
        Assert.Contains("""deviceId"":""dev-99""", record)
        Assert.Contains("""title"":""Kitchen""", record)
        Assert.Contains("""mode"":""AUDIO_ONLY""", record)
        ' DEVICE-EXCHANGE 6.3: the record carries the stable start and the writer's clock
        ' (startedAt orders two records of one sourceId per LIVE-BROADCAST item G as item
        ' BP resolves it; updatedAt stays informational).
        Assert.Contains("""startedAt"":""2026-10-22T09:30:00.000Z""", record)
        Assert.Contains("""updatedAt"":""2026-10-22T09:31:15.000Z""", record)
        Assert.Contains("""descriptor"":{""test"":1}", record)
    End Sub

End Class
#End If
