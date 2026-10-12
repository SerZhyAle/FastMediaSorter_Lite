#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports Xunit

' Ticket 199 section 3.5 and section 8: the hub. A new listener gets the header then starts at a
' joinable cut; a slow listener skips and never stalls the others; the ring keeps two seconds in
' memory. The clock is injected, so the slow-listener case drives time by hand.

Public Class LiveBroadcastHubTests

    ''' <summary>An audio hub over synthetic ADTS frames with a hand-driven clock.</summary>
    Private Shared Function AudioHub(ByRef clock As Long) As LiveBroadcastHub
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        clock = 0
        Return hub
    End Function

    <Fact>
    Public Sub A_new_listener_starts_at_the_next_frame_boundary()
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)

        Dim first As Byte() = LiveAdtsBuilder.AdtsStream(44100, 1, 3, 20)
        hub.Append(first)

        Dim cursor As LiveHubCursor = hub.Attach()
        ' Nothing has arrived since the attach: the listener waits for the next frame, it is never
        ' handed the two seconds of history (contract 5.2: start at the live edge).
        Assert.Null(cursor.Pull())

        Dim second As Byte() = LiveAdtsBuilder.AdtsStream(44100, 1, 2, 20)
        now += 30
        hub.Append(second)
        Dim received As Byte() = cursor.Pull()
        Assert.NotNull(received)
        ' The whole second segment arrived, starting at its first frame boundary - no header bytes
        ' for ADTS (every frame carries its own), and nothing of the first segment.
        Assert.Equal(second.Length, received.Length)
        Assert.Equal(2, LiveAdtsFrames.CountFrames(received))
    End Sub

    <Fact>
    Public Sub A_new_video_listener_gets_the_PAT_and_PMT_header_then_starts_at_the_keyframe()
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveVideoJoinPolicy(), Function() now)

        ' The stream before the listener joins: PAT, PMT, one IDR keyframe, one ordinary frame.
        Dim before As Byte() = LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.PmtPacket(),
                                                      LiveTsBuilder.VideoPesPacket(True), LiveTsBuilder.VideoPesPacket(False))
        hub.Append(before)

        Dim cursor As LiveHubCursor = hub.Attach()
        Assert.Null(cursor.Pull())

        ' The next keyframe arrives after the attach: the listener starts there, preceded by PAT/PMT.
        Dim joinSegment As Byte() = LiveTsBuilder.Segment(LiveTsBuilder.VideoPesPacket(False), LiveTsBuilder.VideoPesPacket(True))
        now += 50
        hub.Append(joinSegment)

        Dim received As Byte() = cursor.Pull()
        Assert.NotNull(received)
        Assert.True(received.Length > joinSegment.Length, "the header bytes (376) precede the keyframe bytes")

        Dim header As Byte() = New Byte(375) {}
        Array.Copy(received, 0, header, 0, 376)
        Dim analysis As TsAnalysis = TsChunker.Analyze(header)
        Assert.True(analysis.PatFound)
        Assert.True(analysis.PmtFound)

        ' The whole answer re-analyzed: PAT and PMT in front (the header block), then the stream
        ' starting EXACTLY at the keyframe packet - the joinable cut of item C.
        Dim whole As TsAnalysis = TsChunker.Analyze(received)
        Assert.True(whole.PatFound)
        Assert.True(whole.PmtFound)
        Assert.Single(whole.JoinPointOffsets)
        Assert.Equal(376, whole.JoinPointOffsets(0))
        Assert.Equal(&H47, received(376))
    End Sub

    <Fact>
    Public Sub A_slow_listener_skips_forward_and_never_stalls_the_others()
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)

        Dim fast As LiveHubCursor = hub.Attach()
        Dim slow As LiveHubCursor = hub.Attach()

        For i As Integer = 1 To 10
            now += 40
            hub.Append(LiveAdtsBuilder.AdtsStream(44100, 1, 2, 20))
        Next

        ' The fast listener drinks everything as it comes.
        Dim fastBytes As Integer = 0
        Dim data As Byte() = fast.Pull()
        While data IsNot Nothing
            fastBytes += data.Length
            data = fast.Pull()
        End While
        Assert.True(fastBytes > 0)

        ' The slow listener starts too, drinks ONE segment, and then sleeps past the retention
        ' window: its next pull finds its segment aged out and skips to a fresh frame boundary
        ' instead of replaying the whole two seconds, and says so.
        Assert.NotNull(slow.Pull())
        now += LiveProducerContract.HubRetentionMs + 100
        hub.Append(LiveAdtsBuilder.AdtsStream(44100, 1, 2, 20))
        Dim skipped As Byte() = slow.Pull()
        Assert.NotNull(skipped)
        Assert.True(slow.SkippedForward)

        ' The hub still serves both: the fast listener's next pull works, unchanged.
        Assert.NotNull(fast.Pull())
    End Sub

    <Fact>
    Public Sub The_ring_keeps_only_two_seconds_in_memory()
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now, retentionMs:=2000)

        For i As Integer = 1 To 30
            now += 100
            hub.Append(LiveAdtsBuilder.AdtsStream(44100, 1, 1, 100))
        Next
        ' 30 segments of 100 ms: only the last ~20 (two seconds) may remain.
        Assert.True(hub.RetainedSegments <= 22, "retention is two seconds, not the whole broadcast (safety rule 3)")
    End Sub

    <Fact>
    Public Sub Clear_ends_every_listener_and_empties_the_ring()
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        hub.Append(LiveAdtsBuilder.AdtsStream(44100, 1, 2, 20))
        Dim cursor As LiveHubCursor = hub.Attach()
        Assert.Equal(1, hub.ListenerCount)

        hub.Clear()
        Assert.Equal(0, hub.ListenerCount)
        Assert.True(cursor.Detached)
        Assert.Null(cursor.Pull())
        Assert.Equal(0, hub.RetainedSegments)
    End Sub

    <Fact>
    Public Sub Detach_counts_listeners_correctly()
        Dim now As Long = 0
        Dim hub As New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() now)
        Dim a As LiveHubCursor = hub.Attach()
        Dim b As LiveHubCursor = hub.Attach()
        Assert.Equal(2, hub.ListenerCount)
        a.Detach()
        Assert.Equal(1, hub.ListenerCount)
        b.Detach()
        Assert.Equal(0, hub.ListenerCount)
    End Sub

End Class
#End If
