#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Net.NetworkInformation
Imports Xunit

' Ticket 199 sections 3.3, 4, 5 and 6: the pure rules - the address policy (never loopback,
' 0.0.0.0, link-local or a carrier interface; no address refuses the start), the settings POCO with
' its injected store, the stable sourceId, and the encoder's argument builders.

Public Class LiveProducerAddressPolicyTests

    Private Shared Function Candidate(address As String, nicType As NetworkInterfaceType, looksVirtual As Boolean, hasGateway As Boolean) As LiveAddressCandidate
        Return New LiveAddressCandidate With {.Address = address, .NicType = nicType, .LooksVirtual = looksVirtual, .HasIpv4Gateway = hasGateway}
    End Function

    <Theory>
    <InlineData("127.0.0.1")>
    <InlineData("127.192.0.5")>
    <InlineData("0.0.0.0")>
    <InlineData("169.254.10.7")>
    <InlineData("192.0.2.1")>
    <InlineData("224.0.0.1")>
    <InlineData("255.255.255.255")>
    Public Sub These_addresses_are_never_published(address As String)
        Assert.False(LiveProducerAddressPolicy.IsPublishable(Candidate(address, NetworkInterfaceType.Ethernet, False, True)),
                     address & " must never be published (producer rule 1)")
    End Sub

    <Fact>
    Public Sub A_carrier_interface_is_never_published_whatever_its_address()
        ' The "mobile-carrier interface" rule is the NIC, not the address: a WWAN modem's own address
        ' never publishes, while a PC on a phone's hotspot (a Wi-Fi adapter with a private address)
        ' does.
        Assert.False(LiveProducerAddressPolicy.IsPublishable(Candidate("10.23.4.5", NetworkInterfaceType.WwanPP, False, True)))
        Assert.True(LiveProducerAddressPolicy.IsPublishable(Candidate("192.168.43.15", NetworkInterfaceType.Wireless80211, False, True)))
    End Sub

    <Fact>
    Public Sub No_address_means_no_start_and_no_descriptor()
        Assert.Equal("", LiveProducerAddressPolicy.ChooseAddress(New List(Of LiveAddressCandidate)()))
        Assert.Equal("", LiveProducerAddressPolicy.ChooseAddress(Nothing))
    End Sub

    <Fact>
    Public Sub A_real_LAN_beats_a_virtual_adapter_even_when_the_virtual_came_first()
        Dim candidates As New List(Of LiveAddressCandidate) From {
            Candidate("172.28.0.1", NetworkInterfaceType.Ethernet, True, False),
            Candidate("192.168.1.50", NetworkInterfaceType.Ethernet, False, True)
        }
        Assert.Equal("192.168.1.50", LiveProducerAddressPolicy.ChooseAddress(candidates))
    End Sub

    <Fact>
    Public Sub Nothing_publishable_means_no_address()
        Dim candidates As New List(Of LiveAddressCandidate) From {
            Candidate("127.0.0.1", NetworkInterfaceType.Loopback, False, False),
            Candidate("169.254.1.2", NetworkInterfaceType.Ethernet, False, False)
        }
        Assert.Equal("", LiveProducerAddressPolicy.ChooseAddress(candidates))
    End Sub

    <Fact>
    Public Sub The_URL_is_built_from_the_chosen_address_port_and_path()
        Assert.Equal("http://192.168.1.50:8768/live-audio.aac", LiveProducerAddressPolicy.BuildUrl("192.168.1.50", 8768, "/live-audio.aac"))
        Assert.Equal("http://10.1.2.3:9000/a1b2c3d4/live.ts", LiveProducerAddressPolicy.BuildUrl("10.1.2.3", 9000, "/a1b2c3d4/live.ts"))
    End Sub

End Class

Public Class LiveProducerSettingsTests

    Private Shared Function NewStore() As Dictionary(Of String, String)
        Return New Dictionary(Of String, String)(StringComparer.Ordinal)
    End Function

    Private Shared Function Reader(store As Dictionary(Of String, String)) As Func(Of String, String)
        Return Function(key)
                   Dim value As String = Nothing
                   If store.TryGetValue(key, value) Then Return value
                   Return Nothing
               End Function
    End Function

    <Fact>
    Public Sub The_defaults_are_the_shapes_own_numbers()
        Dim s As New LiveProducerSettings()
        Assert.Equal(8768, s.AudioPort)
        Assert.Equal(44100, s.SampleRate)
        Assert.Equal(128, s.AudioBitrateKbps)
        Assert.Equal(1, s.MicrophoneChannels)
        Assert.Equal(2, s.SystemAudioChannels)
        Assert.False(s.AllowLanVideoViewers)
        Assert.True(s.OnAirChip)
        Assert.Equal(1.0, s.MicrophoneGain, 5)
        Assert.Equal(0, s.ScreenMonitorIndex)
        Assert.True(s.ScreenCursor)
    End Sub

    <Fact>
    Public Sub Load_and_Save_round_trip_through_the_injected_store()
        Dim store As Dictionary(Of String, String) = NewStore()
        Dim saved As New LiveProducerSettings With {
            .AudioPort = 9123, .SampleRate = 48000, .AudioBitrateKbps = 96,
            .MicrophoneChannels = 1, .SystemAudioChannels = 2,
            .Title = "Attic", .AllowLanVideoViewers = True, .OnAirChip = False, .MicrophoneGain = 1.5,
            .ScreenMonitorIndex = 1, .ScreenCursor = False, .MaxListeners = 7
        }
        saved.Save(Sub(key, value) store(key) = value)

        Dim loaded As LiveProducerSettings = LiveProducerSettings.Load(Reader(store))
        Assert.Equal(9123, loaded.AudioPort)
        Assert.Equal(48000, loaded.SampleRate)
        Assert.Equal(96, loaded.AudioBitrateKbps)
        Assert.Equal("Attic", loaded.Title)
        Assert.True(loaded.AllowLanVideoViewers)
        Assert.False(loaded.OnAirChip)
        Assert.Equal(1.5, loaded.MicrophoneGain, 5)
        Assert.Equal(1, loaded.ScreenMonitorIndex)
        Assert.Equal(7, loaded.MaxListeners)
        Assert.False(loaded.ScreenCursor)
    End Sub

    <Fact>
    Public Sub Missing_values_fall_back_to_the_defaults_and_bad_values_are_clamped()
        Dim loaded As LiveProducerSettings = LiveProducerSettings.Load(Function(key) CStr(Nothing))
        Assert.Equal(8768, loaded.AudioPort)
        Assert.Equal(1.0, loaded.MicrophoneGain, 5)

        Assert.Equal(1024, LiveProducerSettings.ClampPort(1))
        Assert.Equal(65535, LiveProducerSettings.ClampPort(99999))
        Assert.Equal(1, LiveProducerSettings.SanitizeChannels(0))
        Assert.Equal(2, LiveProducerSettings.SanitizeChannels(6))
    End Sub

    <Fact>
    Public Sub The_value_names_live_next_to_the_stores_own()
        Assert.Equal("Live_AudioPort", LiveProducerSettings.KeyAudioPort)
        Assert.Equal("LiveBroadcast_SourceId", LiveProducerContract.SourceIdRegistryValue)
    End Sub

End Class

Public Class LiveSourceIdentityTests

    Private Shared Function Reader(store As Dictionary(Of String, String)) As Func(Of String, String)
        Return Function(key)
                   Dim value As String = Nothing
                   If store.TryGetValue(key, value) Then Return value
                   Return Nothing
               End Function
    End Function

    Private Shared Function Writer(store As Dictionary(Of String, String)) As Action(Of String, String)
        Return Sub(key, value) store(key) = value
    End Function

    Private Shared Function MustNotReach() As String
        Throw New InvalidOperationException("this seam must not be called in this test")
    End Function

    <Fact>
    Public Sub The_enrolled_device_id_wins_and_nothing_is_written()
        Dim store As Dictionary(Of String, String) = New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim id As String = LiveSourceIdentity.Resolve("device-7",
                                                      Reader(store),
                                                      Writer(store),
                                                      AddressOf MustNotReach)
        Assert.Equal("device-7", id)
        Assert.Empty(store)
    End Sub

    <Fact>
    Public Sub Without_enrollment_the_stored_id_is_kept()
        Dim store As New Dictionary(Of String, String)(StringComparer.Ordinal) From {{LiveProducerContract.SourceIdRegistryValue, "fmsw-ABCDEF0123456789"}}
        Dim id As String = LiveSourceIdentity.Resolve("",
                                                      Reader(store),
                                                      Writer(store),
                                                      AddressOf MustNotReach)
        Assert.Equal("fmsw-ABCDEF0123456789", id)
    End Sub

    <Fact>
    Public Sub A_first_broadcast_draws_and_keeps_one_random_id()
        Dim store As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim id As String = LiveSourceIdentity.Resolve(Nothing,
                                                      Reader(store),
                                                      Writer(store),
                                                      Function() "fmsw-0000000000000001")
        Assert.Equal("fmsw-0000000000000001", id)
        Assert.Equal("fmsw-0000000000000001", store(LiveProducerContract.SourceIdRegistryValue))

        ' The next broadcast reads the SAME id: stable per device.
        Dim again As String = LiveSourceIdentity.Resolve(Nothing,
                                                         Reader(store),
                                                         Writer(store),
                                                         AddressOf MustNotReach)
        Assert.Equal(id, again)
    End Sub

    <Fact>
    Public Sub The_random_id_has_the_familys_opaque_shape()
        Dim id As String = LiveSourceIdentity.NewRandomId()
        Assert.StartsWith("fmsw-", id)
        Assert.Equal(21, id.Length)
    End Sub

    <Fact>
    Public Sub The_item_G_suffix_exists_for_the_day_a_device_runs_two_broadcasts()
        Assert.Equal("fmsw-ABC", LiveSourceIdentity.SourceIdFor("fmsw-ABC", ""))
        Assert.Equal("fmsw-ABC:screen", LiveSourceIdentity.SourceIdFor("fmsw-ABC", "screen"))
    End Sub

End Class

Public Class LiveFfmpegArgsTests

    <Fact>
    Public Sub The_audio_pipeline_is_raw_PCM_in_ADTS_out_at_the_shapes_numbers()
        Dim args As String = LiveFfmpegArgs.AudioToAdts(New LiveEncoderOptions With {
            .SampleRate = 44100, .Channels = 1, .AudioBitrateKbps = 128,
            .InputSampleRate = 48000, .InputChannels = 2
        })
        Assert.Contains("-f s16le -ar 48000 -ac 2 -i pipe:0", args)
        Assert.Contains("-ar 44100 -ac 1", args)
        Assert.Contains("-c:a aac -b:a 128k", args)
        Assert.Contains("-f adts -flush_packets 1 pipe:1", args)
    End Sub

    <Fact>
    Public Sub A_fake_source_declaring_nothing_feeds_the_shapes_own_numbers()
        Dim args As String = LiveFfmpegArgs.AudioToAdts(New LiveEncoderOptions())
        Assert.Contains("-f s16le -ar 44100 -ac 1 -i pipe:0", args)
    End Sub

    <Fact>
    Public Sub The_video_pipeline_carries_the_item_C_numbers()
        Dim options As New LiveEncoderOptions With {
            .WithVideo = True, .WithAudio = True, .VideoFps = 25, .VideoWidth = 1280, .VideoHeight = 720,
            .VideoBitrateKbps = 2000, .SampleRate = 44100, .Channels = 1
        }
        Dim args As String = LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\fms-live-test")
        Assert.Contains("-c:v libx264 -preset ultrafast -tune zerolatency -bf 0", args)
        ' The 1 s keyframe interval of section 3.2: 25 fps x 1 s = one keyframe a second.
        Assert.Contains("-g 25", args)
        Assert.Contains("-f mpegts -muxdelay 0.1 -pat_period 0.1 -flush_packets 1 pipe:1", args)
        Assert.Contains("\\.\pipe\fms-live-test", args)
    End Sub

    <Fact>
    Public Sub VIDEO_ONLY_opens_no_audio_input()
        Dim options As New LiveEncoderOptions With {.WithVideo = True, .WithAudio = False, .VideoFps = 30}
        Dim args As String = LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\fms-live-test")
        Assert.DoesNotContain("\\.\pipe\fms-live-test", args)
        Assert.DoesNotContain("-c:a aac", args)
        Assert.Equal(30, LiveFfmpegArgs.GopSize(options))
    End Sub

    <Fact>
    Public Sub A_monitor_sized_input_declares_its_own_size_and_scales_to_the_shapes_numbers()
        ' Section 4: the capture is the monitor's own size (1920x1080 more often than not), the
        ' shape's numbers stay 1280x720, and the ENGINE scales between them.
        Dim options As New LiveEncoderOptions With {
            .WithVideo = True, .WithAudio = False, .VideoFps = 25,
            .VideoWidth = 1280, .VideoHeight = 720,
            .InputVideoWidth = 1920, .InputVideoHeight = 1080
        }
        Dim args As String = LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\fms-live-test")
        Assert.Contains("-f rawvideo -pix_fmt bgra -s 1920x1080", args)
        Assert.Contains("-vf scale=1280:720", args)
        Assert.Contains("-c:v libx264 -preset ultrafast -tune zerolatency -bf 0", args)
    End Sub

    <Fact>
    Public Sub An_input_already_at_the_shapes_size_carries_no_scale_filter()
        Dim options As New LiveEncoderOptions With {.WithVideo = True, .WithAudio = False, .VideoFps = 25}
        Dim args As String = LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\fms-live-test")
        Assert.Contains("-f rawvideo -pix_fmt bgra -s 1280x720", args)
        Assert.DoesNotContain("-vf", args)
    End Sub

End Class

Public Class LiveVideoCopyTests

    <Fact>
    Public Sub A_same_size_frame_is_a_row_copy()
        Dim src As Byte() = {1, 2, 3, 4, 5, 6, 7, 8}
        Dim dst As Byte() = {0, 0, 0, 0, 0, 0, 0, 0}
        LiveVideoCopy.FoldFrame(src, 2, 1, dst, 2, 1)
        Assert.Equal(src, dst)
    End Sub

    <Fact>
    Public Sub A_larger_frame_is_copied_clamped()
        Dim src As Byte() = {1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3}
        Dim dst As Byte() = {9, 9, 9, 9, 9, 9, 9, 9}
        LiveVideoCopy.FoldFrame(src, 3, 1, dst, 2, 1)
        Assert.Equal(New Byte() {1, 1, 1, 1, 2, 2, 2, 2}, dst)
    End Sub

    <Fact>
    Public Sub A_shrunk_frame_blacks_the_stale_right_strip_and_rows()
        ' A monitor that shrank mid-broadcast: the copied region holds the new pixels, everything the
        ' copy no longer covers is black - the old picture never bleeds through.
        Dim src As Byte() = {7, 7, 7, 7}
        Dim dst As Byte() = {9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9}
        LiveVideoCopy.FoldFrame(src, 1, 1, dst, 2, 2)
        Assert.Equal(New Byte() {7, 7, 7, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}, dst)
    End Sub

    <Fact>
    Public Sub Bad_dimensions_are_ignored_not_thrown()
        LiveVideoCopy.FoldFrame(Nothing, 2, 2, New Byte(7) {}, 2, 2)
        LiveVideoCopy.FoldFrame(New Byte(7) {}, 0, 2, New Byte(7) {}, 2, 2)
        LiveVideoCopy.FoldFrame(New Byte(7) {}, 2, 2, Nothing, 2, 2)
        LiveVideoCopy.FoldFrame(New Byte(7) {}, 2, 2, New Byte(7) {}, 0, 0)
    End Sub

End Class
#End If
