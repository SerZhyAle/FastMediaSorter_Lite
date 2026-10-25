#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports Xunit

''' <summary>
''' Specification 203 section 3.2 and DEVICE-EXCHANGE 8.1 item C: the record files of the Drive channel - their names, their
''' envelope, and above all the judgement of expiry, which is made on Drive's clock (the file's modifiedTime against the Date
''' header of the response) and never on the writer's own `writtenAt`.
''' </summary>
Public Class ExchangeDriveRecordsTests

    Private Shared ReadOnly DeviceId As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Shared ReadOnly OtherId As String = "BBBBBBBBBBBBBBBBBBBBBA"

    <Fact>
    Public Sub A_record_file_name_is_the_one_the_contract_gives()
        Assert.Equal("fmsx-device-" & DeviceId & ".json", ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, DeviceId))
        Assert.Equal("fmsx-resource-" & DeviceId & ".json", ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Resource, DeviceId))
        Assert.Equal("fmsx-broadcast-" & DeviceId & ".json", ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Broadcast, DeviceId))
        Assert.Equal("fmsx-request-" & DeviceId & ".json", ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Request, DeviceId))
        ' an id outside the 22-character form names no file
        Assert.Equal("", ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, "short"))
        Assert.Equal("", ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, ""))
    End Sub

    <Fact>
    Public Sub A_file_name_parses_back_and_a_stranger_is_not_a_record()
        Dim kind As ExchangeDriveRecordKind
        Dim id As String = ""
        Assert.True(ExchangeDriveRecords.TryParseFileName("fmsx-broadcast-" & OtherId & ".json", kind, id))
        Assert.Equal(ExchangeDriveRecordKind.Broadcast, kind)
        Assert.Equal(OtherId, id)

        ' the probe's own files, the queue, a half name and another product's names are left alone
        For Each stranger As String In {"fmsx-probe-android-ab5b1f66.json", "fmsx-device-short.json", "fmsx-device-" & DeviceId, "device-" & DeviceId & ".json",
                                        "fmsx-gadget-" & DeviceId & ".json", "manifest.json", "", "fmsx-.json"}
            Assert.False(ExchangeDriveRecords.TryParseFileName(stranger, kind, id), stranger)
        Next
    End Sub

    <Fact>
    Public Sub The_envelope_round_trips_and_keeps_every_member_of_the_record()
        Dim record As String = "{""deviceId"":""" & DeviceId & """,""deviceName"":""Office PC"",""futureMember"":{""x"":1}}"
        Dim body As Byte() = ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Device, record, 240, New DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc))
        Assert.NotNull(body)
        Dim text As String = Encoding.UTF8.GetString(body)
        Assert.Contains("""schemaVersion"":1", text, StringComparison.Ordinal)
        Assert.Contains("""type"":""device""", text, StringComparison.Ordinal)
        Assert.Contains("""writtenAt"":""2026-10-23T12:00:00Z""", text, StringComparison.Ordinal)
        Assert.Contains("""ttlSeconds"":240", text, StringComparison.Ordinal)

        Dim file As ExchangeDriveRecordFile = Nothing
        Dim refusal As ExchangeDriveRecordRefusal
        Assert.True(ExchangeDriveRecords.TryDecode(body, ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(240, file.TtlSeconds)
        Assert.Equal(1, file.SchemaVersion)
        Assert.Contains("futureMember", file.RecordJson, StringComparison.Ordinal)

        Dim read As ExchangeDeviceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadDevice(file.RecordJson, read))
        Assert.Equal(DeviceId, read.DeviceId)
    End Sub

    <Fact>
    Public Sub A_protected_resource_is_wrapped_with_schema_2_and_nothing_else_is()
        Dim plain As String = "{""resourceId"":""" & DeviceId & """,""deviceId"":""" & OtherId & """,""kind"":""sftp"",""name"":""Photos""}"
        Dim guarded As String = "{""resourceId"":""" & DeviceId & """,""deviceId"":""" & OtherId & """,""kind"":""sftp"",""name"":""Photos"",""protection"":{""scheme"":""pin""}}"
        Dim now As DateTime = DateTime.UtcNow
        Assert.Contains("""schemaVersion"":1", Encoding.UTF8.GetString(ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Resource, plain, 0, now)), StringComparison.Ordinal)
        Dim wrapped As Byte() = ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Resource, guarded, 0, now)
        Assert.Contains("""schemaVersion"":2", Encoding.UTF8.GetString(wrapped), StringComparison.Ordinal)

        Dim file As ExchangeDriveRecordFile = Nothing
        Dim refusal As ExchangeDriveRecordRefusal
        Assert.True(ExchangeDriveRecords.TryDecode(wrapped, ExchangeDriveRecordKind.Resource, file, refusal))
        Assert.Equal(2, file.SchemaVersion)
        ' a ttl of 0 is "the default of the kind", written out so a reader never has to guess
        Assert.Equal(ExchangeDriveRecords.ResourceTtlSeconds, file.TtlSeconds)
    End Sub

    <Fact>
    Public Sub A_reader_refuses_a_version_it_does_not_know_a_wrong_type_and_a_broken_file_cleanly()
        Dim file As ExchangeDriveRecordFile = Nothing
        Dim refusal As ExchangeDriveRecordRefusal

        ' schemaVersion 3, and 2 on a kind that has no version 2
        Assert.False(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":3,""type"":""device"",""record"":{}}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecordRefusal.UnsupportedVersion, refusal)
        Assert.False(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":2,""type"":""device"",""record"":{}}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecordRefusal.UnsupportedVersion, refusal)
        Assert.False(ExchangeDriveRecords.TryDecode(Bytes("{""type"":""device"",""record"":{}}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecordRefusal.UnsupportedVersion, refusal)

        ' a body that says another type than its file name promised
        Assert.False(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":1,""type"":""broadcast"",""record"":{}}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecordRefusal.WrongType, refusal)

        Assert.False(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":1,""type"":""device""}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecordRefusal.NoRecord, refusal)
        Assert.False(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":1,""type"":""device"",""record"":[]}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecordRefusal.NoRecord, refusal)

        For Each broken As String In {"not json", "[]", "", "{"}
            Assert.False(ExchangeDriveRecords.TryDecode(Bytes(broken), ExchangeDriveRecordKind.Device, file, refusal), broken)
            Assert.Equal(ExchangeDriveRecordRefusal.Malformed, refusal)
        Next
        Assert.False(ExchangeDriveRecords.TryDecode(Nothing, ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Null(file)
    End Sub

    <Fact>
    Public Sub An_absent_or_absurd_ttl_reads_as_the_default_of_the_kind()
        Dim file As ExchangeDriveRecordFile = Nothing
        Dim refusal As ExchangeDriveRecordRefusal
        Assert.True(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":1,""type"":""broadcast"",""record"":{}}"), ExchangeDriveRecordKind.Broadcast, file, refusal))
        Assert.Equal(ExchangeDriveRecords.BroadcastTtlSeconds, file.TtlSeconds)
        ' a year is more than any record may claim: one wrong file cannot keep a dead device "online"
        Assert.True(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":1,""type"":""device"",""ttlSeconds"":31536000,""record"":{}}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecords.DeviceTtlSeconds, file.TtlSeconds)
        Assert.True(ExchangeDriveRecords.TryDecode(Bytes("{""schemaVersion"":1,""type"":""device"",""ttlSeconds"":-5,""record"":{}}"), ExchangeDriveRecordKind.Device, file, refusal))
        Assert.Equal(ExchangeDriveRecords.DeviceTtlSeconds, file.TtlSeconds)
    End Sub

    <Fact>
    Public Sub A_record_that_is_no_object_or_too_big_is_not_written()
        Dim now As DateTime = DateTime.UtcNow
        Assert.Null(ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Device, "", 100, now))
        Assert.Null(ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Device, "[1]", 100, now))
        Assert.Null(ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Device, "{broken", 100, now))
        Assert.Null(ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Device, "{""pad"":""" & New String("x"c, ExchangeWire.MaxRecordBytes) & """}", 100, now))
    End Sub

    ' ------------------------------------------------------------------------------------------ item C

    <Fact>
    Public Sub A_record_expires_only_after_a_full_ttl_on_drives_clock()
        Dim driveNow As New DateTimeOffset(2026, 10, 23, 13, 49, 25, TimeSpan.Zero)
        ' modified 299 s before Drive's Date header, ttl 300: still standing
        Assert.Equal(ExchangeDriveRecordState.Live, ExchangeDriveRecords.Judge("2026-10-23T13:44:26.000Z", driveNow, 300))
        ' exactly a full ttl: offline
        Assert.Equal(ExchangeDriveRecordState.Expired, ExchangeDriveRecords.Judge("2026-10-23T13:44:25.000Z", driveNow, 300))
        Assert.Equal(ExchangeDriveRecordState.Expired, ExchangeDriveRecords.Judge("2026-10-23T12:00:00.000Z", driveNow, 300))
    End Sub

    <Fact>
    Public Sub A_wrong_local_clock_cannot_change_the_verdict()
        ' Acceptance criterion 5. Device A's local clock is an hour wrong, so the `writtenAt` it writes is an hour old - or an
        ' hour ahead. Device B judges by modifiedTime against the Date header and never reads writtenAt, so A stays online
        ' while it rewrites, in either direction.
        Dim driveNow As New DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero)
        Dim record As String = "{""deviceId"":""" & DeviceId & """}"
        For Each skew As Integer In {-60, 60}
            Dim writtenAt As DateTime = driveNow.UtcDateTime.AddMinutes(skew)
            Dim file As ExchangeDriveRecordFile = Nothing
            Dim refusal As ExchangeDriveRecordRefusal
            Assert.True(ExchangeDriveRecords.TryDecode(ExchangeDriveRecords.Encode(ExchangeDriveRecordKind.Device, record, 300, writtenAt), ExchangeDriveRecordKind.Device, file, refusal))
            ' just rewritten (Drive stamped modifiedTime 20 s before its Date header)
            Assert.Equal(ExchangeDriveRecordState.Live, ExchangeDriveRecords.Judge("2026-10-23T12:59:40.000Z", driveNow, file.TtlSeconds))
            ' stopped rewriting a full ttl ago: offline, whatever writtenAt claims
            Assert.Equal(ExchangeDriveRecordState.Expired, ExchangeDriveRecords.Judge("2026-10-23T12:55:00.000Z", driveNow, file.TtlSeconds))
        Next
    End Sub

    <Fact>
    Public Sub A_time_that_cannot_be_read_proves_nothing_live_and_a_future_time_is_not_old()
        Dim driveNow As New DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero)
        Assert.Equal(ExchangeDriveRecordState.Expired, ExchangeDriveRecords.Judge("", driveNow, 300))
        Assert.Equal(ExchangeDriveRecordState.Expired, ExchangeDriveRecords.Judge("yesterday", driveNow, 300))
        ' Drive's front-ends differ by a second or two: a modifiedTime just ahead of the header is a fresh write
        Assert.Equal(ExchangeDriveRecordState.Live, ExchangeDriveRecords.Judge("2026-10-23T13:00:02.000Z", driveNow, 300))
    End Sub

    <Fact>
    Public Sub A_standing_record_is_rewritten_at_least_every_third_of_its_ttl()
        Assert.Equal(100, ExchangeDriveRecords.RewriteIntervalSeconds(300))
        Assert.Equal(30, ExchangeDriveRecords.RewriteIntervalSeconds(90))
        Assert.Equal(1, ExchangeDriveRecords.RewriteIntervalSeconds(2))
        Assert.Equal(1, ExchangeDriveRecords.RewriteIntervalSeconds(0))
        ' the rewrite interval of every default leaves room for two missed rewrites before the reader's full ttl
        For Each kind As ExchangeDriveRecordKind In [Enum].GetValues(Of ExchangeDriveRecordKind)()
            Dim ttl As Integer = ExchangeDriveRecords.DefaultTtlSeconds(kind)
            Assert.True(ExchangeDriveRecords.RewriteIntervalSeconds(ttl) * 3 <= ttl)
        Next
    End Sub

    Private Shared Function Bytes(text As String) As Byte()
        Return Encoding.UTF8.GetBytes(text)
    End Function

End Class
#End If
