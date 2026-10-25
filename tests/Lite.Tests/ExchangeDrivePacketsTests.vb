#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports Xunit

''' <summary>
''' Specification 203 section 3.3 and DEVICE-EXCHANGE items CN to CS: the manifest of a Drive packet (the shipped phone's
''' version 1 and the members item CN adds), what makes a packet for this device, expired or an unfinished upload, the quota
''' arithmetic, and the polling back-off of item CQ.
''' </summary>
Public Class ExchangeDrivePacketsTests

    Private Const OwnId As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const PeerId As String = "BBBBBBBBBBBBBBBBBBBBBA"

    ' What the shipped phone writes (S3040): version 1, names only, no files[], status CLAIMED once it took the packet.
    Private Const PhoneManifest As String =
        "{""packetId"":""4f1c0a52-7d0e-4c3e-9d0b-1a2b3c4d5e6f"",""createdAtEpochMs"":1790000000000,""senderDeviceName"":""Pixel"",""targetDeviceName"":null," &
        """payloadKind"":""MEDIA_FILES"",""fileNames"":[""a.jpg"",""b.mp4""],""totalSizeBytes"":12345,""ttlDays"":3,""status"":""CLAIMED"",""someFutureMember"":true}"

    <Fact>
    Public Sub The_shipped_phones_manifest_is_read_whole()
        Dim manifest As ExchangeDrivePacketManifest = Nothing
        Dim refusal As ExchangeDriveManifestRefusal
        Assert.True(ExchangeDrivePackets.TryDecode(Encoding.UTF8.GetBytes(PhoneManifest), manifest, refusal))
        Assert.Equal("4f1c0a52-7d0e-4c3e-9d0b-1a2b3c4d5e6f", manifest.PacketId)
        Assert.Equal(1, manifest.SchemaVersion)
        Assert.Equal("Pixel", manifest.SenderDeviceName)
        Assert.Equal("", manifest.TargetDeviceName)
        Assert.Equal(ExchangeDrivePayloadKind.MediaFiles, manifest.PayloadKind)
        Assert.Equal({"a.jpg", "b.mp4"}, manifest.FileNames)
        Assert.Empty(manifest.Files)
        Assert.Equal(12345L, manifest.TotalSizeBytes)
        Assert.Equal(3, manifest.TtlDays)
        ' CLAIMED written by the shipped phone is read as PENDING for every other device (item CR): the codec keeps it, the
        ' queue treats Claimed like Pending - this test holds that the status is not turned into a refusal
        Assert.Equal(ExchangeDrivePacketStatus.Claimed, manifest.Status)
    End Sub

    <Fact>
    Public Sub A_manifest_this_program_writes_round_trips_with_version_2()
        Dim manifest As New ExchangeDrivePacketManifest() With {
            .PacketId = ExchangeDrivePackets.NewPacketId(),
            .CreatedAtEpochMs = 1790000000123L,
            .SenderDeviceName = "Office PC", .SenderDeviceId = OwnId,
            .TargetDeviceName = "Pixel", .TargetDeviceId = PeerId,
            .PayloadKind = ExchangeDrivePayloadKind.Files,
            .FileNames = {"holiday.jpg"},
            .Files = {New ExchangeDrivePacketItem("holiday.jpg", 2048, "ab12", "image/jpeg")},
            .TotalSizeBytes = 2048, .TtlSeconds = 600
        }
        Dim body As Byte() = ExchangeDrivePackets.Encode(manifest)
        Assert.NotNull(body)
        Dim text As String = Encoding.UTF8.GetString(body)
        ' version-1 members the shipped reader filters on stay; the new ones ride beside them
        For Each member As String In {"""packetId""", """schemaVersion"":2", """payloadKind"":""FILES""", """targetDeviceName"":""Pixel""", """ttlDays"":7", """ttlSeconds"":600", """status"":""PENDING"""}
            Assert.Contains(member, text, StringComparison.Ordinal)
        Next

        Dim back As ExchangeDrivePacketManifest = Nothing
        Dim refusal As ExchangeDriveManifestRefusal
        Assert.True(ExchangeDrivePackets.TryDecode(body, back, refusal))
        Assert.Equal(2, back.SchemaVersion)
        Assert.Equal(PeerId, back.TargetDeviceId)
        Assert.Equal(OwnId, back.SenderDeviceId)
        Assert.Equal(600, back.TtlSeconds)
        Assert.Single(back.Files)
        Assert.Equal("ab12", back.Files(0).Sha256)
        Assert.Equal("image/jpeg", back.Files(0).MimeType)
    End Sub

    <Fact>
    Public Sub An_untargeted_packet_writes_a_null_target_name()
        Dim manifest As New ExchangeDrivePacketManifest() With {
            .PacketId = ExchangeDrivePackets.NewPacketId(), .PayloadKind = ExchangeDrivePayloadKind.Files,
            .FileNames = {"a.txt"}, .Files = {New ExchangeDrivePacketItem("a.txt", 1, "", "")}, .TotalSizeBytes = 1
        }
        Dim text As String = Encoding.UTF8.GetString(ExchangeDrivePackets.Encode(manifest))
        Assert.Contains("""targetDeviceName"":null", text, StringComparison.Ordinal)
        Assert.DoesNotContain("targetDeviceId", text, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub A_manifest_that_breaks_the_forms_of_item_CN_is_not_written_or_read()
        Dim good As New ExchangeDrivePacketManifest() With {.PacketId = "ok-id_1", .PayloadKind = ExchangeDrivePayloadKind.Files, .FileNames = {"a.txt"}}
        Assert.NotNull(ExchangeDrivePackets.Encode(good))

        For Each badName As String In {"manifest.json", "..", ".", "a/b.txt", "a\b.txt", "bad" & ChrW(7) & ".txt", ""}
            Dim bad As New ExchangeDrivePacketManifest() With {.PacketId = "ok-id_1", .PayloadKind = ExchangeDrivePayloadKind.Files, .FileNames = {badName}}
            Assert.Null(ExchangeDrivePackets.Encode(bad))
        Next
        Assert.Null(ExchangeDrivePackets.Encode(New ExchangeDrivePacketManifest() With {.PacketId = "has space", .PayloadKind = ExchangeDrivePayloadKind.Files}))
        Assert.Null(ExchangeDrivePackets.Encode(New ExchangeDrivePacketManifest() With {.PacketId = "ok", .PayloadKind = ExchangeDrivePayloadKind.Unknown}))

        Dim manifest As ExchangeDrivePacketManifest = Nothing
        Dim refusal As ExchangeDriveManifestRefusal
        Assert.False(ExchangeDrivePackets.TryDecode(Encoding.UTF8.GetBytes(PhoneManifest.Replace("""a.jpg""", """../a.jpg""")), manifest, refusal))
        Assert.Equal(ExchangeDriveManifestRefusal.BadName, refusal)
        Assert.False(ExchangeDrivePackets.TryDecode(Encoding.UTF8.GetBytes(PhoneManifest.Replace("4f1c0a52-7d0e-4c3e-9d0b-1a2b3c4d5e6f", "a/b")), manifest, refusal))
        Assert.Equal(ExchangeDriveManifestRefusal.BadName, refusal)
        Assert.False(ExchangeDrivePackets.TryDecode(Encoding.UTF8.GetBytes("not json"), manifest, refusal))
        Assert.Equal(ExchangeDriveManifestRefusal.Malformed, refusal)
        Assert.False(ExchangeDrivePackets.TryDecode(Nothing, manifest, refusal))
        Assert.Null(manifest)
    End Sub

    <Fact>
    Public Sub A_kind_this_program_does_not_know_decodes_as_unknown_and_is_not_received()
        Dim manifest As ExchangeDrivePacketManifest = Nothing
        Dim refusal As ExchangeDriveManifestRefusal
        Assert.True(ExchangeDrivePackets.TryDecode(Encoding.UTF8.GetBytes(PhoneManifest.Replace("MEDIA_FILES", "HOLOGRAM")), manifest, refusal))
        Assert.Equal(ExchangeDrivePayloadKind.Unknown, manifest.PayloadKind)
        Assert.Equal("HOLOGRAM", manifest.PayloadKindText)
        Assert.False(ExchangeDrivePackets.IsReceivableKind(manifest.PayloadKind))
        ' a settings export is the phone's own; it stays in the queue for the phone
        Assert.False(ExchangeDrivePackets.IsReceivableKind(ExchangeDrivePayloadKind.Settings))
        For Each kind As ExchangeDrivePayloadKind In {ExchangeDrivePayloadKind.Files, ExchangeDrivePayloadKind.MediaFiles, ExchangeDrivePayloadKind.Clipboard,
                                                      ExchangeDrivePayloadKind.Request, ExchangeDrivePayloadKind.Reply}
            Assert.True(ExchangeDrivePackets.IsReceivableKind(kind))
        Next
    End Sub

    ' ------------------------------------------------------------------------------------------ item CR

    <Fact>
    Public Sub A_packets_lifetime_follows_item_CR()
        Assert.Equal(7L * 86400, ExchangeDrivePackets.LifetimeSeconds(Kind(ExchangeDrivePayloadKind.Files)))
        Assert.Equal(900L, ExchangeDrivePackets.LifetimeSeconds(Kind(ExchangeDrivePayloadKind.Request)))
        Assert.Equal(86400L, ExchangeDrivePackets.LifetimeSeconds(Kind(ExchangeDrivePayloadKind.Reply)))
        ' ttlDays when given, ttlSeconds replaces it, the clipboard never expires
        Assert.Equal(3L * 86400, ExchangeDrivePackets.LifetimeSeconds(New ExchangeDrivePacketManifest() With {.PayloadKind = ExchangeDrivePayloadKind.Files, .TtlDays = 3}))
        Assert.Equal(600L, ExchangeDrivePackets.LifetimeSeconds(New ExchangeDrivePacketManifest() With {.PayloadKind = ExchangeDrivePayloadKind.Files, .TtlDays = 3, .TtlSeconds = 600}))
        Assert.Equal(-1L, ExchangeDrivePackets.LifetimeSeconds(New ExchangeDrivePacketManifest() With {.PayloadKind = ExchangeDrivePayloadKind.Clipboard, .TtlSeconds = 5}))
    End Sub

    <Fact>
    Public Sub Expiry_is_the_folders_created_time_against_drives_date_never_the_writers_clock()
        Dim driveNow As New DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero)
        Dim request As New ExchangeDrivePacketManifest() With {.PayloadKind = ExchangeDrivePayloadKind.Request, .CreatedAtEpochMs = 1L}
        ' a createdAtEpochMs of 1970 changes nothing: only the folder's createdTime counts
        Assert.Equal(ExchangeDriveRecordState.Live, ExchangeDrivePackets.Judge("2026-10-23T12:45:01.000Z", driveNow, request))
        Assert.Equal(ExchangeDriveRecordState.Expired, ExchangeDrivePackets.Judge("2026-10-23T12:45:00.000Z", driveNow, request))
        ' the clipboard has no expiry
        Dim clip As New ExchangeDrivePacketManifest() With {.PayloadKind = ExchangeDrivePayloadKind.Clipboard}
        Assert.Equal(ExchangeDriveRecordState.Live, ExchangeDrivePackets.Judge("2020-01-01T00:00:00.000Z", driveNow, clip))
        ' a time that cannot be read deletes nothing
        Assert.Equal(ExchangeDriveRecordState.Live, ExchangeDrivePackets.Judge("", driveNow, request))
    End Sub

    <Fact>
    Public Sub A_folder_without_a_manifest_is_an_orphan_after_seven_days()
        Dim driveNow As New DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero)
        Assert.False(ExchangeDrivePackets.IsOrphanExpired("2026-10-16T13:00:01.000Z", driveNow))
        Assert.True(ExchangeDrivePackets.IsOrphanExpired("2026-10-16T13:00:00.000Z", driveNow))
        Assert.False(ExchangeDrivePackets.IsOrphanExpired("", driveNow))
    End Sub

    <Fact>
    Public Sub The_target_id_decides_then_the_target_name_then_anyone()
        Dim any As New ExchangeDrivePacketManifest()
        Assert.Equal(ExchangeDriveAddress.Anyone, ExchangeDrivePackets.AddresseeOf(any, OwnId, "Office PC"))

        Dim byName As New ExchangeDrivePacketManifest() With {.TargetDeviceName = " office pc "}
        Assert.Equal(ExchangeDriveAddress.ThisDevice, ExchangeDrivePackets.AddresseeOf(byName, OwnId, "Office PC"))
        Assert.Equal(ExchangeDriveAddress.OtherDevice, ExchangeDrivePackets.AddresseeOf(byName, OwnId, "Laptop"))

        ' when the id is present the name is only shown: a matching name does not make it ours
        Dim byId As New ExchangeDrivePacketManifest() With {.TargetDeviceId = PeerId, .TargetDeviceName = "Office PC"}
        Assert.Equal(ExchangeDriveAddress.OtherDevice, ExchangeDrivePackets.AddresseeOf(byId, OwnId, "Office PC"))
        Assert.Equal(ExchangeDriveAddress.ThisDevice, ExchangeDrivePackets.AddresseeOf(byId, PeerId, "Laptop"))
    End Sub

    <Fact>
    Public Sub A_sender_is_not_its_own_recipient()
        Assert.True(ExchangeDrivePackets.IsOwnPacket(New ExchangeDrivePacketManifest() With {.SenderDeviceId = OwnId, .SenderDeviceName = "x"}, OwnId, "Office PC"))
        Assert.False(ExchangeDrivePackets.IsOwnPacket(New ExchangeDrivePacketManifest() With {.SenderDeviceId = PeerId, .SenderDeviceName = "Office PC"}, OwnId, "Office PC"))
        ' the shipped phone writes names only
        Assert.True(ExchangeDrivePackets.IsOwnPacket(New ExchangeDrivePacketManifest() With {.SenderDeviceName = "office pc"}, OwnId, "Office PC"))
        Assert.False(ExchangeDrivePackets.IsOwnPacket(New ExchangeDrivePacketManifest() With {.SenderDeviceName = "Pixel"}, OwnId, "Office PC"))
        Assert.False(ExchangeDrivePackets.IsOwnPacket(New ExchangeDrivePacketManifest(), OwnId, "Office PC"))
    End Sub

    ' ------------------------------------------------------------------------------------------ item CS

    <Fact>
    Public Sub A_packet_must_fit_with_one_percent_of_the_limit_to_spare()
        Dim free As Long
        ' a 15 GB account holding 10 GB: 1% (150 MB) is the reserve, so 4.85 GB can still be sent
        Dim limit As Long = 15L * 1000 * 1000 * 1000
        Dim used As Long = 10L * 1000 * 1000 * 1000
        Assert.True(ExchangeDrivePackets.FitsQuota(limit, used, 4_850_000_000L, free))
        Assert.Equal(4_850_000_000L, free)
        Assert.False(ExchangeDrivePackets.FitsQuota(limit, used, 4_850_000_001L, free))
        ' a full account has nothing free, never a negative number
        Assert.False(ExchangeDrivePackets.FitsQuota(limit, limit, 1, free))
        Assert.Equal(0L, free)
        ' no limit reported (an unlimited plan) always fits
        Assert.True(ExchangeDrivePackets.FitsQuota(0, 123, Long.MaxValue, free))
    End Sub

    ' ------------------------------------------------------------------------------------------ item CQ

    <Fact>
    Public Sub Polling_runs_every_ten_seconds_when_something_waits_and_every_sixty_otherwise()
        Assert.Equal(10, ExchangeDrivePolling.IntervalSeconds(True))
        Assert.Equal(60, ExchangeDrivePolling.IntervalSeconds(False))
        Assert.Equal(120, ExchangeDrivePolling.ReplyWaitSeconds)
    End Sub

    <Fact>
    Public Sub The_backoff_doubles_from_one_second_to_five_minutes_and_a_success_resets_it()
        Dim backoff As New ExchangeDrivePolling.Backoff()
        Dim top As New List(Of Integer)
        For attempt As Integer = 1 To 12
            top.Add(CInt(Math.Round(backoff.NextDelay(0.999999).TotalSeconds)))
        Next
        Assert.Equal({1, 2, 4, 8, 16, 32, 64, 128, 256, 300, 300, 300}, top)

        ' jitter only ever shortens: the lowest draw is half the base, the highest is the base
        backoff.Reset()
        Assert.Equal(0, backoff.Failures)
        Assert.Equal(0.5, backoff.NextDelay(0.0).TotalSeconds, 6)
        Assert.Equal(1.0, backoff.NextDelay(0.0).TotalSeconds, 6)
        Assert.Equal(2.0, backoff.NextDelay(0.0).TotalSeconds, 6)

        ' a hundred failures never overflow the cap
        For attempt As Integer = 1 To 100
            Assert.True(backoff.NextDelay(0.999999).TotalSeconds <= 300.0)
        Next
    End Sub

    ' ------------------------------------------------------------------------------------------ names on disk

    <Fact>
    Public Sub A_received_file_keeps_only_a_base_name_the_file_system_accepts()
        Assert.Equal("a.jpg", ExchangeDrivePackets.SafeFileName("a.jpg"))
        Assert.Equal("a.jpg", ExchangeDrivePackets.SafeFileName("..\..\dir/a.jpg"))
        Assert.Equal("a_b_c.txt", ExchangeDrivePackets.SafeFileName("a:b*c.txt"))
        Assert.Equal("file", ExchangeDrivePackets.SafeFileName(""))
        Assert.Equal("file", ExchangeDrivePackets.SafeFileName("..."))
        Assert.Equal("_CON.txt", ExchangeDrivePackets.SafeFileName("CON.txt"))
        Assert.Equal("report", ExchangeDrivePackets.SafeFileName("report. "))
    End Sub

    <Fact>
    Public Sub A_name_that_is_taken_gets_a_numbered_suffix_before_its_extension()
        Assert.Equal("a (2).jpg", ExchangeDrivePackets.WithSuffix("a.jpg", 2))
        Assert.Equal("notes (3)", ExchangeDrivePackets.WithSuffix("notes", 3))
        Assert.Equal("a.b (2).txt", ExchangeDrivePackets.WithSuffix("a.b.txt", 2))
    End Sub

    Private Shared Function Kind(payload As ExchangeDrivePayloadKind) As ExchangeDrivePacketManifest
        Return New ExchangeDrivePacketManifest() With {.PayloadKind = payload}
    End Function

End Class
#End If
