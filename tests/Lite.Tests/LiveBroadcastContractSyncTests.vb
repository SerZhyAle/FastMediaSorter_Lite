#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports Xunit

' Contract synchronization, round 2026-10-07 (specification 181): what LIVE-BROADCAST 0.15 to 0.18 asks of a
' CONSUMER, read against the consumer in src/LiveBroadcast. Nothing here changes behaviour: every test pins a
' reading the consumer already has, including specification 201's RELAY and TUNNEL playback.
'
'   0.15 item G  - sourceId names a SOURCE; "<deviceSourceId>:<source>" is one opaque string, and a stored
'                  broadcast is replaced only by the same string.
'   0.16 item H  - channel count and sample rate are producer settings; an endpoint may carry "channels".
'   0.18 item J  - TUNNEL.inner is never read for a destination; the documentation host 192.0.2.1 is never
'                  dialled: the HTTP connection is carried by the exchange tunnel.
'   0.17 item I  - HTTP video descriptors are playable; first-picture timing still needs live acceptance.

Public Class LiveBroadcastContractSyncTests

    Private Const IdA As String = "AAECAwQFBgcICQoLDA0ODw"

    ''' <summary>JSON written with apostrophes so a test reads like the wire.</summary>
    Private Shared Function Json(text As String) As String
        Return text.Replace("'"c, """"c)
    End Function

    Private Shared Function Descriptor(sourceId As String, url As String) As LiveBroadcastDescriptor
        Return New LiveBroadcastDescriptor With {
            .SchemaVersion = 1,
            .Url = url,
            .Mode = "AUDIO_ONLY",
            .ModeValue = LiveBroadcastMode.AudioOnly,
            .Title = sourceId,
            .SourceId = sourceId
        }
    End Function

    ' ------------------------------------------------------------------ 0.15 item G: sourceId names a source

    <Theory>
    <InlineData(IdA & ":mic")>
    <InlineData(IdA & ":system-audio")>
    <InlineData(IdA & ":screen")>
    <InlineData(IdA & ":camera")>
    <InlineData("phone-uuid-1234")>
    Public Sub A_SourceId_Is_Read_As_One_Opaque_String_Whatever_Its_Shape(sourceId As String)
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(
            Json("{'schemaVersion':1,'url':'http://192.168.1.50:8768/live-audio.aac','mode':'AUDIO_ONLY','sourceId':'" & sourceId & "'}"))

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.Equal(sourceId, res.Descriptor.SourceId)
    End Sub

    <Fact>
    Public Sub Two_Sources_Of_One_Device_Are_Two_Channels_And_Each_Is_Replaced_Only_By_Its_Own_Id()
        Dim file As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(file)
            store.Upsert(Descriptor(IdA & ":mic", "http://192.168.1.50:8768/live-audio.aac"))
            store.Upsert(Descriptor(IdA & ":system-audio", "http://192.168.1.50:8769/live-audio.aac"))
            store.Upsert(Descriptor(IdA, "http://192.168.1.50:8770/live-audio.aac"))   ' a device with one source writes its id alone

            Assert.Equal(3, store.LoadAll().Count)

            ' The microphone moves to another port: ONLY the microphone's entry is replaced, and it moves to the top.
            store.Upsert(Descriptor(IdA & ":mic", "http://192.168.1.50:9000/live-audio.aac"))
            Dim after As List(Of LiveBroadcastDescriptor) = store.LoadAll()

            Assert.Equal(3, after.Count)
            Assert.Equal(IdA & ":mic", after(0).SourceId)
            Assert.Equal("http://192.168.1.50:9000/live-audio.aac", after(0).Url)
            Assert.Contains(after, Function(d) d.SourceId = IdA & ":system-audio" AndAlso d.Url.Contains(":8769/"))
            Assert.Contains(after, Function(d) d.SourceId = IdA AndAlso d.Url.Contains(":8770/"))
        Finally
            If System.IO.File.Exists(file) Then System.IO.File.Delete(file)
        End Try
    End Sub

    ' ------------------------------------------------- 1.2 item G as DEVICE-EXCHANGE item BP resolves it: the later startedAt wins

    <Fact>
    Public Sub A_Directory_Records_startedAt_Is_Read_Into_The_Descriptor_And_UpdatedAt_Decides_Nothing()
        Dim record As String = Json("{'broadcastId':'bc-77','deviceId':'dev-1','title':'Kitchen','mode':'AUDIO_ONLY'," &
                                    "'startedAt':'2026-10-22T09:30:00.000Z','updatedAt':'2026-10-22T11:00:00.000Z'," &
                                    "'descriptor':{'schemaVersion':1,'url':'http://192.168.1.50:8768/live-audio.aac','mode':'AUDIO_ONLY'," &
                                    "'sourceId':'" & IdA & ":mic'}}")
        Dim row As LiveDirectoryRow = LiveBroadcastRecords.RowOf(record)

        Assert.True(row.IsPlayable)
        Assert.Equal("bc-77", row.Descriptor.BroadcastId)
        Assert.Equal("2026-10-22T09:30:00.000Z", row.Descriptor.RecordStartedAt)

        ' a record without a startedAt (an older producer) reads as before, with no start kept
        Dim withoutStart As String = Json("{'broadcastId':'bc-78','mode':'AUDIO_ONLY'," &
                                          "'descriptor':{'schemaVersion':1,'url':'http://192.168.1.50:8769/live-audio.aac','mode':'AUDIO_ONLY'}}")
        Dim plainRow As LiveDirectoryRow = LiveBroadcastRecords.RowOf(withoutStart)
        Assert.True(plainRow.IsPlayable)
        Assert.Equal("", plainRow.Descriptor.RecordStartedAt)
    End Sub

    Private Shared Function DirectoryRow(broadcastId As String, sourceId As String, startedAt As String, updatedAt As String) As LiveDirectoryRow
        Dim started As String = If(startedAt Is Nothing, "", "'startedAt':'" & startedAt & "',")
        Return LiveBroadcastRecords.RowOf(Json("{'broadcastId':'" & broadcastId & "','deviceId':'dev-1','title':'T','mode':'AUDIO_ONLY'," & started &
                                               "'updatedAt':'" & updatedAt & "'," &
                                               "'descriptor':{'schemaVersion':1,'url':'http://192.168.1.50:8768/live-audio.aac','mode':'AUDIO_ONLY'," &
                                               "'sourceId':'" & sourceId & "'}}"))
    End Function

    <Fact>
    Public Sub Two_Directory_Records_Of_One_SourceId_Are_One_Row_With_The_Later_startedAt_Whatever_Their_updatedAt()
        ' The older broadcast was touched last (a writer clock stepped forward): updatedAt must not decide.
        Dim older As LiveDirectoryRow = DirectoryRow("bc-1", IdA & ":mic", "2026-10-22T09:00:00.000Z", "2026-10-22T12:00:00.000Z")
        Dim newer As LiveDirectoryRow = DirectoryRow("bc-2", IdA & ":mic", "2026-10-22T10:00:00.000Z", "2026-10-22T10:00:00.000Z")

        Dim forward As HashSet(Of Integer) = LiveBroadcastRecords.SupersededIndexes(New List(Of LiveDirectoryRow) From {older, newer})
        Assert.Equal({0}, forward.ToArray())

        ' the snapshot order does not alternate the answer
        Dim backward As HashSet(Of Integer) = LiveBroadcastRecords.SupersededIndexes(New List(Of LiveDirectoryRow) From {newer, older})
        Assert.Equal({1}, backward.ToArray())
    End Sub

    <Fact>
    Public Sub Records_Of_Different_SourceIds_Or_Without_One_Or_Refused_Are_Never_Collapsed()
        Dim mic As LiveDirectoryRow = DirectoryRow("bc-1", IdA & ":mic", "2026-10-22T09:00:00.000Z", "2026-10-22T09:00:00.000Z")
        Dim screen As LiveDirectoryRow = DirectoryRow("bc-2", IdA & ":screen", "2026-10-22T10:00:00.000Z", "2026-10-22T10:00:00.000Z")
        Dim noId1 As LiveDirectoryRow = LiveBroadcastRecords.RowOf(Json("{'broadcastId':'bc-3','mode':'AUDIO_ONLY'," &
            "'descriptor':{'schemaVersion':1,'url':'http://192.168.1.50:8769/live-audio.aac','mode':'AUDIO_ONLY'}}"))
        Dim noId2 As LiveDirectoryRow = LiveBroadcastRecords.RowOf(Json("{'broadcastId':'bc-4','mode':'AUDIO_ONLY'," &
            "'descriptor':{'schemaVersion':1,'url':'http://192.168.1.50:8770/live-audio.aac','mode':'AUDIO_ONLY'}}"))
        Dim refused1 As LiveDirectoryRow = LiveBroadcastRecords.RowOf(Json("{'broadcastId':'bc-5','descriptor':{'schemaVersion':9}}"))
        Dim refused2 As LiveDirectoryRow = LiveBroadcastRecords.RowOf(Json("{'broadcastId':'bc-6','descriptor':{'schemaVersion':9}}"))

        Assert.False(refused1.IsPlayable)
        Assert.Empty(LiveBroadcastRecords.SupersededIndexes(New List(Of LiveDirectoryRow) From {mic, screen, noId1, noId2, refused1, refused2}))
    End Sub

    <Fact>
    Public Sub Equal_Or_Unreadable_Starts_Keep_The_Snapshots_Own_Order_And_The_Later_Record_Stands()
        Dim first As LiveDirectoryRow = DirectoryRow("bc-1", IdA, "2026-10-22T09:00:00.000Z", "2026-10-22T09:00:00.000Z")
        Dim same As LiveDirectoryRow = DirectoryRow("bc-2", IdA, "2026-10-22T09:00:00.000Z", "2026-10-22T09:00:00.000Z")
        Dim none As LiveDirectoryRow = DirectoryRow("bc-3", IdA, Nothing, "2026-10-22T09:00:00.000Z")
        Dim garbage As LiveDirectoryRow = DirectoryRow("bc-4", IdA, "yesterday", "2026-10-22T09:00:00.000Z")

        Assert.Equal({0}, LiveBroadcastRecords.SupersededIndexes(New List(Of LiveDirectoryRow) From {first, same}).ToArray())
        Assert.Equal({0}, LiveBroadcastRecords.SupersededIndexes(New List(Of LiveDirectoryRow) From {first, none}).ToArray())
        Assert.Equal({0}, LiveBroadcastRecords.SupersededIndexes(New List(Of LiveDirectoryRow) From {first, garbage}).ToArray())
        Assert.Equal({0, 1}, LiveBroadcastRecords.SupersededIndexes(New List(Of LiveDirectoryRow) From {first, same, none}).OrderBy(Function(i) i).ToArray())
    End Sub

    ' ------------------------------------------------------------------ 0.16 item H: channels and sample rate are settings

    <Fact>
    Public Sub An_Endpoint_Channels_Member_Is_Accepted_And_Does_Not_Change_What_Plays()
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(Json(
            "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live-audio.aac','mode':'AUDIO_ONLY','endpoints':[" &
            "{'url':'http://192.168.1.50:8768/live-audio.aac','transport':'HTTP','mode':'AUDIO_ONLY','audioCodec':'aac'," &
            "'sampleRate':48000,'channels':2,'bitrate':192000,'isLive':true,'targetLatencyMs':1000}]}"))

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Dim chosen As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(res.Descriptor)
        Assert.NotNull(chosen)
        Assert.Equal(48000L, chosen.SampleRate)
        Assert.Equal(1000L, chosen.TargetLatencyMs)
    End Sub

    <Fact>
    Public Sub Stereo_48k_Adts_Is_Counted_By_Its_Own_Header()
        ' AAC-LC (profile 1), sampling-frequency index 3 (48000 Hz), channel configuration 2 (stereo).
        Const frameLength As Integer = 400
        Dim frame(frameLength - 1) As Byte
        frame(0) = &HFF
        frame(1) = &HF1
        frame(2) = CByte((1 << 6) Or (3 << 2) Or 0)                                  ' profile, rate index, channel bit 2 = 0
        frame(3) = CByte((2 << 6) Or ((frameLength >> 11) And &H3))                  ' channel bits 1..0 = 10
        frame(4) = CByte((frameLength >> 3) And &HFF)
        frame(5) = CByte(((frameLength And &H7) << 5) Or &H1F)
        frame(6) = &HFC

        Dim counter As New AdtsFrameCounter()
        For i As Integer = 1 To 47
            counter.Feed(frame, 0, frame.Length)
        Next

        Assert.Equal(47L, counter.FrameCount)
        Assert.Equal(0L, counter.SkippedBytes)
        ' 47 x 1024 / 48000 s = 1002.67 ms: the duration comes from the header, never from a fixed 44.1 kHz.
        Assert.InRange(counter.DurationMs, 1002.0, 1003.5)
    End Sub

    ' ------------------------------------------------------------------ 0.18 item J: TUNNEL.inner is never a destination

    Private Const ExchangeDescriptor As String =
        "{'schemaVersion':1,'url':'http://192.168.1.50:8768/live-audio.aac','mode':'AUDIO_ONLY','sourceId':'" & IdA & ":mic','endpoints':[" &
        "{'url':'http://192.168.1.50:8768/live-audio.aac','transport':'HTTP','mode':'AUDIO_ONLY','isLive':true,'targetLatencyMs':1000}," &
        "{'url':'https://relay.example.net:44022/v2/b/q3Vb7YtK0xP2mN9sLfR4wA/stream','transport':'RELAY','mode':'AUDIO_ONLY','targetLatencyMs':2000," &
        "'certFingerprint':'SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk'}," &
        "{'url':'fmsx://relay.example.net:44022/b/q3Vb7YtK0xP2mN9sLfR4wA/http','transport':'TUNNEL','mode':'AUDIO_ONLY','targetLatencyMs':2000," &
        "'inner':'http://192.0.2.1:8768/live-audio.aac','certFingerprint':'SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk'}]}"

    <Fact>
    Public Sub Exchange_Endpoints_Keep_Producer_Order_And_Inner_Is_Never_The_Exchange_Destination()
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(Json(ExchangeDescriptor))

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.Equal(3, res.Descriptor.Endpoints.Count)
        Assert.Equal({"HTTP", "RELAY", "TUNNEL"}, res.Descriptor.Endpoints.ConvertAll(Function(e) e.Transport).ToArray())

        Assert.Equal(3, LiveEndpointSelector.Candidates(res.Descriptor).Count)
        ' The producer's order is LAN first, followed by relay and tunnel.
        Dim chosen As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(res.Descriptor)
        Assert.Equal("http://192.168.1.50:8768/live-audio.aac", chosen.Url)

        ' `inner` (and the documentation host it may carry) is not an address of the model at all.
        For Each ep As LiveBroadcastEndpoint In res.Descriptor.Endpoints
            Assert.DoesNotContain("192.0.2.1", ep.Url, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub A_Tunnel_With_A_Documentation_Inner_Host_Is_Playable_Through_The_Exchange()
        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(Json(
            "{'schemaVersion':1,'url':'fmsx://relay.example.net:44022/b/q3Vb7YtK0xP2mN9sLfR4wA/http','mode':'AUDIO_ONLY','endpoints':[" &
            "{'url':'fmsx://relay.example.net:44022/b/q3Vb7YtK0xP2mN9sLfR4wA/http','transport':'TUNNEL','mode':'AUDIO_ONLY'," &
            "'inner':'http://192.0.2.1:8768/live-audio.aac'}]}"))

        Assert.Equal(LiveReadStatus.Ok, res.Status)
        Assert.Equal("TUNNEL", LiveEndpointSelector.SelectEndpoint(res.Descriptor).Transport)
    End Sub

End Class
#End If
