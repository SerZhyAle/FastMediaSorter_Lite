#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

' Ticket 199 B0 and section 8: the pure checks of the two payload formats - ADTS (the 2.1 shape) and
' MPEG-TS (the item C shape). "Frames decode with AdtsFrameCounter" is the CONSUMER's counter and has
' its own tests; this file proves the producer-side walkers: frame boundaries for the audio join
' policy, and PAT/PMT/IDR finding, 188-byte alignment and the no-IDR-in-2-s flag for the video half.

Public Class LiveProducerAdtsTests

    <Fact>
    Public Sub A_built_frame_parses_and_declares_its_own_length()
        Dim frame As Byte() = LiveAdtsBuilder.AdtsFrame(44100, 1, 40)
        Assert.Equal(47, frame.Length)

        Dim h As LiveAdtsFrames.AdtsHeader = LiveAdtsFrames.ReadHeader(frame, 0)
        Assert.True(h.IsValid)
        Assert.Equal(44100, h.SampleRate)
        Assert.Equal(1, h.Channels)
        Assert.Equal(47, h.FrameLengthBytes)
        Assert.False(h.HasCrc)
    End Sub

    <Fact>
    Public Sub Every_frame_boundary_is_found_the_audio_join_cut()
        Dim bytes As Byte() = LiveAdtsBuilder.AdtsStream(44100, 1, 5, 13)
        Assert.Equal(5, LiveAdtsFrames.CountFrames(bytes))

        ' The first frame sits at 0; the next frame boundary of the audio policy is the NEXT frame.
        Dim first As Integer = LiveAdtsFrames.FirstFrameAt(bytes, 0)
        Assert.Equal(0, first)
        Dim h As LiveAdtsFrames.AdtsHeader = LiveAdtsFrames.ReadHeader(bytes, first)
        Dim second As Integer = LiveAdtsFrames.FirstFrameAt(bytes, first + h.FrameLengthBytes)
        Assert.Equal(h.FrameLengthBytes, second)
    End Sub

    <Fact>
    Public Sub The_44100_frame_rate_is_the_contracts_own_figure()
        ' AAC-LC carries 1024 samples per frame: 43.07 frames per second at 44 100 Hz, the watch's
        ' measured figure of contract section 5.5.
        Assert.Equal(44100 / 1024.0, LiveAdtsFrames.FramesPerSecond(44100), 5)
    End Sub

    <Fact>
    Public Sub Garbage_is_not_a_frame()
        Dim junk As Byte() = New Byte(99) {}
        Assert.Equal(0, LiveAdtsFrames.CountFrames(junk))
        Assert.Equal(-1, LiveAdtsFrames.FirstFrameAt(junk, 0))
    End Sub

    <Fact>
    Public Sub A_truncated_frame_does_not_count_as_decoded()
        Dim whole As Byte() = LiveAdtsBuilder.AdtsFrame(44100, 2, 30)
        Dim truncated As Byte() = New Byte(20 - 1) {}
        Array.Copy(whole, truncated, 20)
        Assert.Equal(0, LiveAdtsFrames.CountFrames(truncated))
    End Sub

    <Fact>
    Public Sub The_consumer_counter_accepts_the_producers_frames()
        ' The round-trip on the payload level: the viewer's own AdtsFrameCounter must accept the
        ' bytes the producer's audio join policy walks.
        Dim bytes As Byte() = LiveAdtsBuilder.AdtsStream(44100, 1, 4, 11)
        Dim counter As New AdtsFrameCounter()
        counter.Feed(bytes, 0, bytes.Length)
        Assert.Equal(4, counter.FrameCount)
        Assert.Equal(4 * 1024.0 * 1000.0 / 44100.0, counter.DurationMs, 2)
    End Sub

End Class

Public Class LiveProducerTsChunkerTests

    <Fact>
    Public Sub Pat_and_Pmt_are_found()
        Dim segment As Byte() = LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.PmtPacket())
        Dim analysis As TsAnalysis = TsChunker.Analyze(segment)
        Assert.True(analysis.Aligned)
        Assert.True(analysis.PatFound)
        Assert.True(analysis.PmtFound)
        Assert.True(analysis.VideoPidFound)
        Assert.True(analysis.AudioPidFound)
    End Sub

    <Fact>
    Public Sub A_PES_with_an_IDR_is_a_join_point()
        Dim segment As Byte() = LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.PmtPacket(),
                                                      LiveTsBuilder.VideoPesPacket(False), LiveTsBuilder.VideoPesPacket(True))
        Dim analysis As TsAnalysis = TsChunker.Analyze(segment)
        Assert.Single(analysis.JoinPointOffsets)
        Assert.Equal(3 * 188, analysis.JoinPointOffsets(0))
    End Sub

    <Fact>
    Public Sub A_stream_without_an_IDR_has_no_join_point_and_is_flagged()
        Dim segment As Byte() = LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.PmtPacket(),
                                                      LiveTsBuilder.VideoPesPacket(False), LiveTsBuilder.VideoPesPacket(False))
        Dim analysis As TsAnalysis = TsChunker.Analyze(segment)
        Assert.Empty(analysis.JoinPointOffsets)
        Assert.False(TsChunker.HasJoinPointInWindow(New List(Of TsChunkEvent) From
                {New TsChunkEvent With {.EventKind = TsChunkEvent.Kind.PatPacket}}, TsChunkEvent.Kind.JoinPoint),
            "a stream with no IDR in the window is flagged, not joined mid-frame")
    End Sub

    <Fact>
    Public Sub Misaligned_bytes_fail_the_188_byte_alignment_check()
        Dim segment As Byte() = LiveTsBuilder.Segment(LiveTsBuilder.PatPacket())
        Dim cut(segment.Length - 10 - 1) As Byte
        Array.Copy(segment, cut, cut.Length)
        Assert.False(TsChunker.IsAligned(cut))

        Dim noSync As Byte() = New Byte(188 * 2 - 1) {}
        Assert.False(TsChunker.IsAligned(noSync))
    End Sub

    <Fact>
    Public Sub The_header_block_is_the_latest_PAT_then_PMT_preceding_a_join()
        Dim state As New TsChunkerState()
        state.Feed(LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.PmtPacket(), LiveTsBuilder.VideoPesPacket(True)))

        Assert.True(state.PatSeen)
        Assert.True(state.PmtSeen)
        Assert.Equal(LiveTsBuilder.VideoPid, state.VideoPid)
        Assert.Equal(LiveTsBuilder.AudioPid, state.AudioPid)

        ' Item C: "a new connection starts at the next keyframe, PRECEDED BY PAT and PMT" - the
        ' header block is exactly the two packets, PAT first, 376 bytes.
        Dim header As Byte() = state.HeaderBlock()
        Assert.NotNull(header)
        Assert.Equal(376, header.Length)
        Dim pat As Byte() = LiveTsBuilder.PatPacket()
        Dim pmt As Byte() = LiveTsBuilder.PmtPacket()
        For i As Integer = 0 To 187
            Assert.Equal(pat(i), header(i))
        Next
        For i As Integer = 0 To 187
            Assert.Equal(pmt(i), header(188 + i))
        Next
    End Sub

    <Fact>
    Public Sub No_header_block_before_either_table_was_seen()
        Dim state As New TsChunkerState()
        state.Feed(LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.VideoPesPacket(True)))
        Assert.Null(state.HeaderBlock())
    End Sub

End Class
#End If
