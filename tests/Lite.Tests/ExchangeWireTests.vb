#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 180 T-1: the codec of the device exchange - the numbers of item T, the table-driven envelopes, the JSON the Go
''' module writes, both frame limits, the chunk bound of item U, and the rules about secrets. The replay of the Go module's
''' generated frames is ExchangeVectorReplayTests; these are the cases a replay cannot reach (a limit, a cut stream, a wipe).
''' </summary>
Public Class ExchangeWireTests

    Private Shared Function Utf8(bytes As Byte()) As String
        Return Encoding.UTF8.GetString(bytes)
    End Function

    ' ---------------------------------------------------------------- the numbers

    <Fact>
    Public Sub The_two_frame_limits_of_item_T_are_named_constants_and_the_pre_auth_one_is_unchanged()
        Assert.Equal(16384, ExchangeWire.MaxFramePreAuth)
        Assert.Equal(262144, ExchangeWire.MaxFrameAuthenticated)
        Assert.Equal(16 * 1024, ExchangeWire.MaxRecordBytes)
        Assert.Equal(1048576, ExchangeWire.MaxChunkLengthField)
        Assert.Equal(2, ExchangeWire.SchemaVersion)
    End Sub

    ' ---------------------------------------------------------------- the envelope table

    ''' <summary>A value for every member kind, so every type of the table can be built and round-tripped.</summary>
    Private Shared Function Sample(typeName As String) As ExchangeEnvelope
        Dim env As New ExchangeEnvelope(typeName)
        For Each s As ExchangeMemberSpec In ExchangeEnvelopeTable.Find(typeName)
            Select Case s.Kind
                Case ExchangeMemberKind.Text : env.SetText(s.Name, "v-" & s.Name)
                Case ExchangeMemberKind.Number : env.SetNumber(s.Name, 7)
                Case ExchangeMemberKind.Flag : env.SetFlag(s.Name, True)
                Case ExchangeMemberKind.TextList : env.SetTextList(s.Name, {"a", "b"})
                Case ExchangeMemberKind.Record : env.SetRecord(s.Name, "{""k"":{""deep"":{""x"":[1,2,{""y"":null}]}}}")
                Case ExchangeMemberKind.RecordList : env.SetRecords(s.Name, {"{""id"":""1"",""f"":{""g"":{""h"":true}}}"})
                Case ExchangeMemberKind.Upserts
                    Dim u As New ExchangeRecordSets()
                    u.Resources.Add("{""resourceId"":""r"",""future"":{""a"":{""b"":{""c"":1}}}}")
                    env.SetUpserts(u)
                Case ExchangeMemberKind.Removals
                    Dim r As New ExchangeIdSets()
                    r.BroadcastIds.Add("b1")
                    env.SetRemovals(r)
            End Select
        Next
        Return env
    End Function

    <Fact>
    Public Sub Every_type_of_the_table_survives_encode_then_decode_and_then_encodes_to_the_same_bytes()
        Assert.Equal(32, ExchangeEnvelopeTable.Types().Count)
        For Each typeName As String In ExchangeEnvelopeTable.Types()
            Dim first As Byte() = ExchangeEnvelopeCodec.Encode(Sample(typeName))
            Dim back As ExchangeEnvelope = ExchangeEnvelopeCodec.Decode(first)
            Assert.True(back.IsKnown, typeName)
            Assert.Equal(typeName, back.Type)
            Dim second As Byte() = ExchangeEnvelopeCodec.Encode(back)
            Assert.True(first.SequenceEqual(second), typeName & ": " & Utf8(first) & " vs " & Utf8(second))
        Next
    End Sub

    <Fact>
    Public Sub Members_are_written_in_the_order_of_the_table_and_schema_version_and_type_come_first()
        Dim text As String = Utf8(ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("enrolled").SetText("deviceToken", TokenOfA()).SetText("account", "alice").
            SetText("publicEndpoint", "h:1").SetNumber("keepaliveSeconds", 30).SetTextList("features", {"directory"}).SetText("serverVersion", "1")))
        Assert.StartsWith("{""schemaVersion"":2,""type"":""enrolled"",""deviceToken"":", text)
        Dim order As String() = {"deviceToken", "account", "publicEndpoint", "keepaliveSeconds", "features", "serverVersion"}
        Dim last As Integer = -1
        For Each name As String In order
            Dim at As Integer = text.IndexOf("""" & name & """", StringComparison.Ordinal)
            Assert.True(at > last, name & " is out of order in " & text)
            last = at
        Next
    End Sub

    Private Shared Function TokenOfA() As String
        Return New String("A"c, 43)
    End Function

    <Fact>
    Public Sub Optional_members_are_left_out_and_the_always_written_ones_are_present_even_when_empty()
        ' hello: productVersion and keepaliveSeconds are omitted when empty / zero
        Dim hello As String = Utf8(ExchangeEnvelopeCodec.Encode(ExchangeMessages.Hello(IdA(), TokenOfA(), "", 0)))
        Assert.DoesNotContain("productVersion", hello)
        Assert.DoesNotContain("keepaliveSeconds", hello)
        ' welcome: features is [] and keepaliveSeconds is 0, both written
        Dim welcome As String = Utf8(ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("welcome")))
        Assert.Contains("""features"":[]", welcome)
        Assert.Contains("""keepaliveSeconds"":0", welcome)
        ' directory: three empty arrays and the revision
        Dim dir As String = Utf8(ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("directory")))
        Assert.Equal("{""schemaVersion"":2,""type"":""directory"",""revision"":0,""devices"":[],""resources"":[],""broadcasts"":[]}", dir)
        ' changed: the nested empties
        Dim changed As String = Utf8(ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("changed").SetNumber("revision", 3)))
        Assert.Contains("""upserts"":{""devices"":[],""resources"":[],""broadcasts"":[]}", changed)
        Assert.Contains("""removals"":{""deviceIds"":[],""resourceIds"":[],""broadcastIds"":[]}", changed)
        ' published: publicPort is present with 0 when set, absent when never set
        Assert.DoesNotContain("publicPort", Utf8(ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("published").SetText("resourceId", "r"))))
        Assert.Contains("""publicPort"":0", Utf8(ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("published").SetText("resourceId", "r").SetNumber("publicPort", 0))))
        ' broadcast-update: maxListeners 0 is written when it was set (0 means no cap)
        Assert.Contains("""maxListeners"":0", Utf8(ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("broadcast-update").SetRecord("broadcast", "{""a"":1}").SetNumber("maxListeners", 0))))
    End Sub

    Private Shared Function IdA() As String
        Return New String("A"c, 22)
    End Function

    <Fact>
    Public Sub Unknown_members_of_a_record_travel_verbatim_at_any_depth()
        Dim record As String = "{ ""resourceId"" : ""r1"", ""future"": { ""a"": { ""b"": { ""c"": [1, 2, { ""d"": ""e"" }] } } } }"
        Dim publish As ExchangeEnvelope = ExchangeMessages.Publish(record)
        Dim wire As String = Utf8(ExchangeEnvelopeCodec.Encode(publish))
        Assert.Contains("""future"":{""a"":{""b"":{""c"":[1,2,{""d"":""e""}]}}}", wire)
        Dim back As ExchangeEnvelope = ExchangeEnvelopeCodec.Decode(Encoding.UTF8.GetBytes(wire))
        Assert.Equal(ExchangeJson.Compact(record), back.Record("resource"))
    End Sub

    <Fact>
    Public Sub An_unknown_type_and_unknown_members_are_ignored_not_errors()
        Dim env As ExchangeEnvelope = ExchangeEnvelopeCodec.Decode(Encoding.UTF8.GetBytes("{""schemaVersion"":2,""type"":""from-the-future"",""x"":1}"))
        Assert.False(env.IsKnown)
        Assert.Equal("from-the-future", env.Type)
        Dim refused As ExchangeEnvelope = ExchangeEnvelopeCodec.Decode(Encoding.UTF8.GetBytes("{""schemaVersion"":2,""type"":""refused"",""reason"":""capacity"",""newMember"":{""z"":1}}"))
        Assert.Equal(ExchangeRefusal.Capacity, refused.Refusal())
    End Sub

    <Fact>
    Public Sub Malformed_bodies_are_named_errors()
        Dim cases As New Dictionary(Of String, ExchangeWireError) From {
            {"not json", ExchangeWireError.Malformed},
            {"[1,2]", ExchangeWireError.Malformed},
            {"""text""", ExchangeWireError.Malformed},
            {"{""type"":""list""}", ExchangeWireError.Schema},
            {"{""schemaVersion"":1,""type"":""list""}", ExchangeWireError.Schema},
            {"{""schemaVersion"":3,""type"":""list""}", ExchangeWireError.Schema},
            {"{""schemaVersion"":""2"",""type"":""list""}", ExchangeWireError.Schema},
            {"{""schemaVersion"":2}", ExchangeWireError.NoType},
            {"{""schemaVersion"":2,""type"":""""}", ExchangeWireError.NoType},
            {"{""schemaVersion"":2,""type"":5}", ExchangeWireError.Malformed},
            {"{""schemaVersion"":2,""type"":""refused""}", ExchangeWireError.Malformed},
            {"{""schemaVersion"":2,""type"":""refused"",""reason"":7}", ExchangeWireError.Malformed},
            {"{""schemaVersion"":2,""type"":""directory"",""revision"":1.5}", ExchangeWireError.Malformed},
            {"{""schemaVersion"":2,""type"":""directory"",""devices"":{}}", ExchangeWireError.Malformed},
            {"{""schemaVersion"":2,""type"":""directory"",""devices"":[1]}", ExchangeWireError.Malformed},
            {"{""schemaVersion"":2,""type"":""publish"",""resource"":null}", ExchangeWireError.Malformed},
            {"{""schemaVersion"":2,""type"":""cast-answer"",""broadcastId"":""b"",""accepted"":""yes""}", ExchangeWireError.Malformed}}
        For Each kv As KeyValuePair(Of String, ExchangeWireError) In cases
            Dim ex As ExchangeWireException = Assert.Throws(Of ExchangeWireException)(Function() ExchangeEnvelopeCodec.Decode(Encoding.UTF8.GetBytes(kv.Key)))
            Assert.True(kv.Value = ex.Error, kv.Key & " -> " & ex.Error.ToString())
        Next
    End Sub

    <Fact>
    Public Sub An_envelope_that_cannot_be_encoded_is_refused_before_a_byte_is_written()
        Assert.Equal(ExchangeWireError.BadMessage, Assert.Throws(Of ExchangeWireException)(Function() ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("nope"))).Error)
        Assert.Equal(ExchangeWireError.BadMessage, Assert.Throws(Of ExchangeWireException)(Function() ExchangeEnvelopeCodec.Encode(New ExchangeEnvelope("refused"))).Error)
        Assert.Equal(ExchangeWireError.BadMessage, Assert.Throws(Of ExchangeWireException)(Function() ExchangeEnvelopeCodec.Encode(ExchangeMessages.Publish("[1]"))).Error)
    End Sub

    ' ---------------------------------------------------------------- the JSON writer

    <Fact>
    Public Sub Strings_are_escaped_as_Gos_encoder_writes_them()
        ' non-ASCII text is built from code points, so the test does not depend on the source file's encoding
        Dim unicode As String = ChrW(&HE9) & ChrW(&H20AC) & ChrW(&HD83D) & ChrW(&HDE00)
        Dim env As ExchangeEnvelope = New ExchangeEnvelope("refused").SetText("reason", "q""b\s/<>&" & vbLf & vbCr & vbTab & ChrW(8) & ChrW(12) & ChrW(1) & ChrW(&H7F) & ChrW(&H2028) & ChrW(&H2029) & unicode)
        Dim wire As String = Utf8(ExchangeEnvelopeCodec.Encode(env))
        Assert.Contains("q\""b\\s/<>&\n\r\t\b\f\u0001" & ChrW(&H7F) & "" & "\u2028\u2029" & "" & unicode, wire)
    End Sub

    <Fact>
    Public Sub A_lone_surrogate_is_written_as_the_replacement_character_not_as_invalid_UTF8()
        Dim env As ExchangeEnvelope = New ExchangeEnvelope("refused").SetText("reason", "a" & ChrW(&HD800) & "b")
        Assert.Contains("a\ufffdb", Utf8(ExchangeEnvelopeCodec.Encode(env)))
    End Sub

    ' ---------------------------------------------------------------- frames and their limits

    Private Shared Function FrameOf(bodyLength As Integer, Optional total As Integer = -1) As Byte()
        Dim n As Integer = If(total < 0, bodyLength, total)
        Dim b(3 + n) As Byte
        b(0) = CByte((bodyLength >> 24) And &HFF)
        b(1) = CByte((bodyLength >> 16) And &HFF)
        b(2) = CByte((bodyLength >> 8) And &HFF)
        b(3) = CByte(bodyLength And &HFF)
        Return b
    End Function

    <Fact>
    Public Async Function A_frame_above_16384_before_authentication_is_refused_without_reading_its_body() As Task
        Dim ms As New MemoryStream(New Byte() {0, 0, &H40, &H1}) ' 16385, no body at all
        Dim reader As New ExchangeFrameReader(ms)
        Dim ex As ExchangeWireException = Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() reader.ReadBodyAsync(CancellationToken.None))
        Assert.Equal(ExchangeWireError.FrameSize, ex.Error)
        Assert.Equal(4L, ms.Position)   ' only the header was consumed: nothing is buffered on the peer's say-so
    End Function

    <Fact>
    Public Async Function A_zero_length_frame_is_refused() As Task
        Dim reader As New ExchangeFrameReader(New MemoryStream(New Byte() {0, 0, 0, 0}))
        Dim ex As ExchangeWireException = Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() reader.ReadBodyAsync(CancellationToken.None))
        Assert.Equal(ExchangeWireError.FrameSize, ex.Error)
    End Function

    <Fact>
    Public Async Function The_authenticated_limit_is_262144_in_both_directions_and_one_more_is_refused() As Task
        ' reading
        Dim reader As New ExchangeFrameReader(New MemoryStream(FrameOf(ExchangeWire.MaxFrameAuthenticated)))
        reader.MarkAuthenticated()
        Dim body As Byte() = Await reader.ReadBodyAsync(CancellationToken.None)
        Assert.Equal(ExchangeWire.MaxFrameAuthenticated, body.Length)

        Dim tooBig As New ExchangeFrameReader(New MemoryStream(FrameOf(ExchangeWire.MaxFrameAuthenticated + 1)))
        tooBig.MarkAuthenticated()
        Dim ex As ExchangeWireException = Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() tooBig.ReadBodyAsync(CancellationToken.None))
        Assert.Equal(ExchangeWireError.FrameSize, ex.Error)

        ' the same size before authentication is refused
        Dim pre As New ExchangeFrameReader(New MemoryStream(FrameOf(20000)))
        Assert.Equal(ExchangeWireError.FrameSize, (Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() pre.ReadBodyAsync(CancellationToken.None))).Error)
    End Function

    ''' <summary>A publish whose record makes the body about <paramref name="bytes"/> long.</summary>
    Private Shared Function BigPublish(bytes As Integer) As ExchangeEnvelope
        Dim filler As New String("x"c, bytes)
        Return ExchangeMessages.Publish("{""resourceId"":""r"",""pad"":""" & filler & """}")
    End Function

    <Fact>
    Public Sub What_this_client_sends_is_bounded_by_the_limit_in_force_for_the_stream()
        Dim env As ExchangeEnvelope = BigPublish(20000)
        Assert.Equal(ExchangeWireError.FrameSize, Assert.Throws(Of ExchangeWireException)(Function() ExchangeFrameWriter.BuildFrame(env, ExchangeWire.MaxFramePreAuth)).Error)
        Dim frame As Byte() = ExchangeFrameWriter.BuildFrame(env, ExchangeWire.MaxFrameAuthenticated)
        Assert.True(frame.Length > 20000)
        Assert.Equal(frame.Length - 4, (CInt(frame(0)) << 24) Or (CInt(frame(1)) << 16) Or (CInt(frame(2)) << 8) Or CInt(frame(3)))
        Assert.Equal(ExchangeWireError.FrameSize, Assert.Throws(Of ExchangeWireException)(Function() ExchangeFrameWriter.BuildFrame(BigPublish(ExchangeWire.MaxFrameAuthenticated), ExchangeWire.MaxFrameAuthenticated)).Error)
    End Sub

    <Fact>
    Public Async Function The_writer_switches_its_limit_when_the_stream_authenticates() As Task
        Dim sink As New ExchangeRecordingStream()
        Dim writer As New ExchangeFrameWriter(sink)
        Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() writer.WriteAsync(BigPublish(20000), CancellationToken.None))
        Assert.Empty(sink.Buffers)   ' nothing was written
        writer.MarkAuthenticated()
        Await writer.WriteAsync(BigPublish(20000), CancellationToken.None)
        Assert.Single(sink.Buffers)
    End Function

    <Fact>
    Public Async Function A_cut_stream_is_told_apart_from_a_clean_close() As Task
        ' EOF before the first byte: the peer closed
        Dim empty As New ExchangeFrameReader(New MemoryStream(Array.Empty(Of Byte)()))
        Await Assert.ThrowsAsync(Of EndOfStreamException)(Function() empty.ReadBodyAsync(CancellationToken.None))
        ' EOF inside the header, and a header whose body never comes (a half-open stream)
        Dim halfHeader As New ExchangeFrameReader(New MemoryStream(New Byte() {0, 0}))
        Assert.Equal(ExchangeWireError.Truncated, (Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() halfHeader.ReadBodyAsync(CancellationToken.None))).Error)
        Dim halfBody As New ExchangeFrameReader(New MemoryStream(New Byte() {0, 0, 0, 10, 1, 2, 3}))
        Assert.Equal(ExchangeWireError.Truncated, (Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() halfBody.ReadBodyAsync(CancellationToken.None))).Error)
    End Function

    <Fact>
    Public Async Function A_half_open_stream_that_goes_silent_is_ended_by_the_callers_deadline() As Task
        Dim pair As Tuple(Of Stream, Stream) = Await ExchangeWireTestKit.LoopbackPairAsync()
        Using pair.Item1
            Using pair.Item2
                ' the peer sends two bytes of a header and then says nothing for ever
                Await pair.Item2.WriteAsync(New Byte() {0, 0}, 0, 2)
                Await pair.Item2.FlushAsync()
                Dim reader As New ExchangeFrameReader(pair.Item1)
                Using cts As New CancellationTokenSource(300)
                    Await Assert.ThrowsAnyAsync(Of OperationCanceledException)(Function() reader.ReadBodyAsync(cts.Token))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_reader_takes_exactly_one_frame_so_the_bytes_after_it_stay_in_the_stream() As Task
        Dim connected As Byte() = ExchangeFrameWriter.BuildFrame(New ExchangeEnvelope("connected"), ExchangeWire.MaxFramePreAuth)
        Dim tail As Byte() = Encoding.ASCII.GetBytes("SSH-2.0-test")
        Dim all(connected.Length + tail.Length - 1) As Byte
        connected.CopyTo(all, 0)
        tail.CopyTo(all, connected.Length)
        Dim ms As New MemoryStream(all)
        Dim env As ExchangeEnvelope = Await New ExchangeFrameReader(ms).ReadAsync(CancellationToken.None)
        Assert.True(env.Is("connected"))
        Dim rest(tail.Length - 1) As Byte
        Assert.Equal(tail.Length, Await ms.ReadAsync(rest, 0, rest.Length))
        Assert.Equal("SSH-2.0-test", Encoding.ASCII.GetString(rest))
    End Function

    ' ---------------------------------------------------------------- the wipe

    <Fact>
    Public Async Function The_buffer_of_a_frame_is_overwritten_once_it_is_sent() As Task
        Dim sink As New ExchangeRecordingStream()
        Dim writer As New ExchangeFrameWriter(sink)
        Dim description As New ExchangeDeviceDescription(IdA(), "PC", ExchangeWire.ProductWindows, "26.10.7.1", "windows 10.0.26200", {ExchangeWire.RoleReceiver}, "")
        Dim enroll As ExchangeEnvelope = ExchangeMessages.Enroll("alice", "Correct-Horse-Battery-9", "", description, 30)
        Await writer.WriteAsync(enroll, CancellationToken.None)
        Assert.Single(sink.Copies)
        Assert.Contains("Correct-Horse-Battery-9", Encoding.UTF8.GetString(sink.Copies(0)))   ' it was sent
        Assert.All(sink.Buffers(0), Sub(b) Assert.Equal(CByte(0), b))                          ' and the buffer holds nothing now
    End Function

    <Fact>
    Public Sub No_ToString_prints_a_member()
        Dim description As New ExchangeDeviceDescription(IdA(), "PC", ExchangeWire.ProductWindows, "1", "windows", Array.Empty(Of String)(), "")
        Dim enroll As ExchangeEnvelope = ExchangeMessages.Enroll("alice", "Correct-Horse-Battery-9", "", description, 30)
        Dim hello As ExchangeEnvelope = ExchangeMessages.Hello(IdA(), TokenOfA(), "1", 30)
        Dim creds As New ExchangeDeviceCredentials(IdA(), TokenOfA())
        Dim record As ExchangeResourceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadResource("{""resourceId"":""r"",""deviceId"":""d"",""kind"":""sftp-share"",""name"":""n"",""access"":{""descriptor"":""FMSSFTP2.secret-descriptor""}}", record))
        Dim texts As String() = {enroll.ToString(), hello.ToString(), creds.ToString(), record.ToString(), New ExchangeOpenResult(ExchangeOpenOutcome.Connected, Nothing, Nothing, Nothing, enroll, ExchangeRefusal.Unknown, "").ToString()}
        For Each t As String In texts
            Assert.DoesNotContain("Correct-Horse", t)
            Assert.DoesNotContain("AAAAAAAA", t)
            Assert.DoesNotContain("secret-descriptor", t)
        Next
        Assert.Equal("wire.enroll", enroll.ToString())
    End Sub

    ' ---------------------------------------------------------------- reasons, ids

    <Fact>
    Public Sub The_ten_reasons_parse_and_an_unknown_one_is_a_generic_refusal()
        Dim names As String() = {"bad-credentials", "device-revoked", "version-unsupported", "tls-required", "id-collision", "capacity", "port-unavailable", "unavailable", "rate-limited"}
        Assert.Equal(10, ExchangeRefusals.All().Length)
        For i As Integer = 0 To names.Length - 1
            Dim r As ExchangeRefusal = ExchangeRefusals.Parse(names(i))
            Assert.NotEqual(ExchangeRefusal.Unknown, r)
            Assert.Equal(names(i), ExchangeRefusals.WireName(r))
            Assert.Equal(ExchangeRefusals.All()(i), r)
        Next
        Assert.Equal(ExchangeRefusal.Unknown, ExchangeRefusals.Parse("something-new"))
        Assert.Equal(ExchangeRefusal.Unknown, ExchangeRefusals.Parse(""))
        Assert.Equal(ExchangeRefusal.Unknown, ExchangeRefusals.Parse("Bad-Credentials"))
    End Sub

    <Fact>
    Public Sub Ids_and_tokens_have_the_contracts_forms()
        For i As Integer = 1 To 50
            Dim id As String = ExchangeIds.NewId()
            Assert.Equal(22, id.Length)
            Assert.True(ExchangeIds.IsValidId(id), id)
        Next
        Assert.False(ExchangeIds.IsValidId(Nothing))
        Assert.False(ExchangeIds.IsValidId(New String("A"c, 21)))
        Assert.False(ExchangeIds.IsValidId(New String("A"c, 23)))
        Assert.False(ExchangeIds.IsValidId(New String("A"c, 21) & "B"))   ' a non-canonical last character
        Assert.False(ExchangeIds.IsValidId(New String("A"c, 21) & "+"))   ' standard alphabet, not url
        Assert.True(ExchangeIds.IsValidToken(New String("A"c, 43)))
        Assert.False(ExchangeIds.IsValidToken(New String("A"c, 42)))
        Assert.False(ExchangeIds.IsValidToken(New String("A"c, 42) & "B"))
    End Sub

    <Fact>
    Public Sub An_id_reaches_a_log_only_as_the_first_8_hex_of_its_sha256()
        Dim id As String = IdA()
        Dim log As String = ExchangeIds.ForLog(id)
        Assert.Equal(8, log.Length)
        Assert.Matches("^[0-9a-f]{8}$", log)
        Assert.Equal(Convert.ToHexString(Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0, 4).ToLowerInvariant(), log)
        Assert.Equal("-", ExchangeIds.ForLog(""))
    End Sub

    ' ---------------------------------------------------------------- relay chunks (item U)

    <Fact>
    Public Async Function A_chunk_is_a_length_a_flag_and_a_payload_and_the_bound_is_on_the_length_field() As Task
        Dim payload As Byte() = {1, 2, 3}
        Dim bytes As Byte() = ExchangeChunks.Build(ExchangeChunks.FlagJoinable, payload)
        Assert.Equal(New Byte() {0, 0, 0, 4, 1, 1, 2, 3}, bytes)
        Dim chunk As ExchangeChunk = Await ExchangeChunks.ReadAsync(New MemoryStream(bytes), CancellationToken.None)
        Assert.True(chunk.IsJoinable)
        Assert.False(chunk.IsHeader)
        Assert.Equal(payload, chunk.Payload)

        ' 1 MiB on the length field = flag + 1048575 payload bytes is the largest a producer writes
        Dim big(ExchangeWire.MaxChunkLengthField - 2) As Byte
        Assert.Equal(1048575, big.Length)
        Dim ok As Byte() = ExchangeChunks.Build(ExchangeChunks.FlagHeader, big)
        Assert.Equal(ExchangeWire.MaxChunkLengthField + 4, ok.Length)
        Assert.Throws(Of ExchangeWireException)(Function() ExchangeChunks.Build(0, New Byte(ExchangeWire.MaxChunkLengthField - 1) {}))
        ' a reader refuses a length above 1 MiB before reading the body, and a reserved flag bit
        Dim tooBig As Byte() = {0, &H10, 0, 1}
        Assert.Equal(ExchangeWireError.FrameSize, (Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() ExchangeChunks.ReadAsync(New MemoryStream(tooBig), CancellationToken.None))).Error)
        Assert.Throws(Of ExchangeWireException)(Function() ExchangeChunks.Build(&H4, payload))
        Dim reserved As Byte() = {0, 0, 0, 2, &H4, 9}
        Assert.Equal(ExchangeWireError.Malformed, (Await Assert.ThrowsAsync(Of ExchangeWireException)(Function() ExchangeChunks.ReadAsync(New MemoryStream(reserved), CancellationToken.None))).Error)
        ' a zero length is the clean end of a broadcast
        Assert.Null(Await ExchangeChunks.ReadAsync(New MemoryStream(ExchangeChunks.EndMarker()), CancellationToken.None))
    End Function

    ' ---------------------------------------------------------------- the server address

    <Theory>
    <InlineData("host.example:22022", "host.example", 22022, True)>
    <InlineData("203.0.113.5:1", "203.0.113.5", 1, True)>
    <InlineData("[2001:db8::1]:443", "2001:db8::1", 443, True)>
    <InlineData("host:0", "", 0, False)>
    <InlineData("host:65536", "", 0, False)>
    <InlineData("host", "", 0, False)>
    <InlineData(":22", "", 0, False)>
    <InlineData("a:b:22", "", 0, False)>
    <InlineData("ho st:22", "", 0, False)>
    <InlineData("https://host:22", "", 0, False)>
    <InlineData("", "", 0, False)>
    Public Sub A_server_address_is_host_colon_port(text As String, host As String, port As Integer, ok As Boolean)
        Dim a As ExchangeServerAddress = Nothing
        Assert.Equal(ok, ExchangeServerAddress.TryParse(text, a))
        If ok Then
            Assert.Equal(host, a.Host)
            Assert.Equal(port, a.Port)
            Dim again As ExchangeServerAddress = Nothing
            Assert.True(ExchangeServerAddress.TryParse(a.ToString(), again))
            Assert.Equal(a.Host, again.Host)
        End If
    End Sub

End Class
#End If
