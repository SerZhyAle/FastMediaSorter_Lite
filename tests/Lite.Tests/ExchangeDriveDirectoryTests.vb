#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports Xunit

''' <summary>
''' Specification 203 section 3.2 and acceptance criteria 2, 4 and 5 - the directory half of the Drive channel on an in-memory
''' Drive: a share published on one PC is discovered on another; a record is kept alive by a rewrite and judged on DRIVE's
''' clock, whatever the writer's clock says; sign-out deletes the device's own records and nobody else's.
''' </summary>
Public Class ExchangeDriveDirectoryTests

    Private Const IdA As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdB As String = "BBBBBBBBBBBBBBBBBBBBBA"

    Private NotInheritable Class Clock
        Public Ms As Long
        Public Function ReadMs() As Long
            Return Ms
        End Function
    End Class

    Private Shared Function DeviceJson(id As String, name As String) As String
        Return "{""deviceId"":""" & id & """,""deviceName"":""" & name & """,""product"":""FastMediaSorter"",""productVersion"":""46"",""platform"":""windows"",""roles"":[""consumer"",""producer""],""capabilities"":[]}"
    End Function

    Private Shared Function ResourceJson(resourceId As String, deviceId As String, name As String) As String
        Return "{""resourceId"":""" & resourceId & """,""deviceId"":""" & deviceId & """,""kind"":""" & ExchangeWire.KindSftpShare & """,""name"":""" & name & """,""access"":{""host"":""192.0.2.10"",""port"":22}}"
    End Function

    Private Shared Function BroadcastJson(broadcastId As String, deviceId As String) As String
        Return "{""broadcastId"":""" & broadcastId & """,""deviceId"":""" & deviceId & """,""mode"":""video"",""title"":""Evening""}"
    End Function

    Private Shared Function DirectoryOf(rig As DriveRig, deviceId As String, Optional clock As Clock = Nothing, Optional utcNow As Func(Of DateTime) = Nothing) As ExchangeDriveDirectory
        Dim client As ExchangeDriveClient = rig.NewClient(New FakeDriveTokens())
        Dim monotonic As Func(Of Long) = Nothing
        If clock IsNot Nothing Then monotonic = AddressOf clock.ReadMs
        Return New ExchangeDriveDirectory(client, deviceId, utcNow, monotonic)
    End Function

    <Fact>
    Public Async Function A_device_and_a_share_published_on_one_PC_are_discovered_on_another() As Task
        Dim rig As New DriveRig()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim b As ExchangeDriveDirectory = DirectoryOf(rig, IdB)
        Dim shareId As String = ExchangeIds.NewId()

        Await a.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        Await a.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, ResourceJson(shareId, IdA, "Photos"), 0, CancellationToken.None)

        Dim seenByB As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Dim device As ExchangeDeviceRecord = Assert.Single(seenByB.Snapshot.Devices)
        Assert.Equal(IdA, device.DeviceId)
        Assert.Equal("Office PC", device.DeviceName)
        Assert.True(device.IsOnline)
        Dim resource As ExchangeResourceRecord = Assert.Single(seenByB.Snapshot.Resources)
        Assert.Equal(shareId, resource.ResourceId)
        Assert.Equal("Photos", resource.Name)
        Assert.Equal(ExchangeWire.PresenceOnline, resource.Presence)
        Assert.True(resource.HasAccess)
        Assert.Equal(0, seenByB.SkippedFiles)

        ' the PC that published it does not list its own share (a consumer hides what it serves, 9 rule 1)
        Dim seenByA As ExchangeDriveDirectoryResult = Await a.ReadAsync(CancellationToken.None)
        Assert.Empty(seenByA.Snapshot.Devices)
        Assert.Empty(seenByA.Snapshot.Resources)
    End Function

    <Fact>
    Public Async Function A_device_writes_only_its_own_records() As Task
        Dim rig As New DriveRig()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim shareId As String = ExchangeIds.NewId()
        ' somebody else's device record, somebody else's resource, a device record filed under another id, a request kind, no JSON
        Await Assert.ThrowsAsync(Of ArgumentException)(Function() a.PublishAsync(ExchangeDriveRecordKind.Device, IdB, DeviceJson(IdB, "Other"), 0, CancellationToken.None))
        Await Assert.ThrowsAsync(Of ArgumentException)(Function() a.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, ResourceJson(shareId, IdB, "Theirs"), 0, CancellationToken.None))
        Await Assert.ThrowsAsync(Of ArgumentException)(Function() a.PublishAsync(ExchangeDriveRecordKind.Device, shareId, DeviceJson(IdA, "Mine"), 0, CancellationToken.None))
        Await Assert.ThrowsAsync(Of ArgumentException)(Function() a.PublishAsync(ExchangeDriveRecordKind.Request, shareId, "{""deviceId"":""" & IdA & """}", 0, CancellationToken.None))
        Await Assert.ThrowsAsync(Of ArgumentException)(Function() a.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, "not json", 0, CancellationToken.None))
        Assert.Empty(rig.Drive.Snapshot())
        Assert.Equal(0, a.StandingCount)
    End Function

    <Fact>
    Public Async Function A_standing_record_is_rewritten_every_third_of_its_ttl_and_not_duplicated() As Task
        Dim rig As New DriveRig()
        Dim clock As New Clock()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA, clock)
        Await a.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        Dim name As String = ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, IdA)
        Dim writtenAt As DateTimeOffset = rig.Drive.Find(name).ModifiedTime

        rig.Drive.Advance(TimeSpan.FromSeconds(99))
        clock.Ms = 99_999
        Assert.Equal(0, Await a.RefreshStandingAsync(CancellationToken.None))
        Assert.Equal(writtenAt, rig.Drive.Find(name).ModifiedTime)

        rig.Drive.Advance(TimeSpan.FromSeconds(1))
        clock.Ms = 100_000
        Assert.Equal(1, Await a.RefreshStandingAsync(CancellationToken.None))
        Assert.Equal(writtenAt.AddSeconds(100), rig.Drive.Find(name).ModifiedTime)
        Assert.Equal(1, rig.Drive.CountNamed(name))
    End Function

    <Fact>
    Public Async Function A_record_ages_on_Drives_clock_whatever_the_writers_or_the_readers_clock_says() As Task
        Dim rig As New DriveRig()
        Dim clock As New Clock()
        ' acceptance criterion 5: the writer's PC is five hours slow, the reader's is in the year 2001
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA, clock, Function() DateTime.UtcNow.AddHours(-5))
        Dim b As ExchangeDriveDirectory = DirectoryOf(rig, IdB, Nothing, Function() New DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        Dim shareId As String = ExchangeIds.NewId()
        Await a.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Slow PC"), 0, CancellationToken.None)
        Await a.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, ResourceJson(shareId, IdA, "Photos"), 0, CancellationToken.None)

        ' `writtenAt` is the slow clock's (informational): hours before the real instant
        Using document As JsonDocument = JsonDocument.Parse(rig.Drive.Find(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, IdA)).Content)
            Dim written As DateTime = document.RootElement.GetProperty("writtenAt").GetDateTime().ToUniversalTime()
            Assert.True(DateTime.UtcNow - written > TimeSpan.FromHours(4))
        End Using

        Dim fresh As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Assert.True(Assert.Single(fresh.Snapshot.Devices).IsOnline)
        Assert.Equal(ExchangeWire.PresenceOnline, Assert.Single(fresh.Snapshot.Resources).Presence)

        rig.Drive.Advance(TimeSpan.FromSeconds(250))
        Dim stillLive As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Assert.True(Assert.Single(stillLive.Snapshot.Devices).IsOnline)

        ' a full ttl (300 s) with no rewrite: offline, on Drive's clock alone
        rig.Drive.Advance(TimeSpan.FromSeconds(100))
        Dim expired As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Dim gone As ExchangeDeviceRecord = Assert.Single(expired.Snapshot.Devices)
        Assert.False(gone.IsOnline)
        Assert.Equal(ExchangeWire.PresenceOffline, Assert.Single(expired.Snapshot.Resources).Presence)
        Assert.False(String.IsNullOrEmpty(gone.LastSeen))

        ' the writer comes back, rewrites: the same file is live again
        clock.Ms = 400_000
        Assert.Equal(2, Await a.RefreshStandingAsync(CancellationToken.None))
        Dim back As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Assert.True(Assert.Single(back.Snapshot.Devices).IsOnline)
        Assert.Equal(ExchangeWire.PresenceOnline, Assert.Single(back.Snapshot.Resources).Presence)
    End Function

    <Fact>
    Public Async Function A_broadcast_announcement_is_listed_while_live_and_dropped_when_it_ends() As Task
        Dim rig As New DriveRig()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim b As ExchangeDriveDirectory = DirectoryOf(rig, IdB)
        Dim castId As String = ExchangeIds.NewId()
        Await a.PublishAsync(ExchangeDriveRecordKind.Broadcast, castId, BroadcastJson(castId, IdA), 0, CancellationToken.None)

        Dim live As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Assert.Equal("Evening", Assert.Single(live.Snapshot.Broadcasts).Title)

        rig.Drive.Advance(TimeSpan.FromSeconds(ExchangeDriveRecords.BroadcastTtlSeconds + 1))
        Dim ended As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Assert.Empty(ended.Snapshot.Broadcasts)
    End Function

    <Fact>
    Public Async Function Withdrawing_deletes_the_file_and_stops_the_rewrite() As Task
        Dim rig As New DriveRig()
        Dim clock As New Clock()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA, clock)
        Dim shareId As String = ExchangeIds.NewId()
        Await a.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, ResourceJson(shareId, IdA, "Photos"), 0, CancellationToken.None)
        Await a.WithdrawAsync(ExchangeDriveRecordKind.Resource, shareId, CancellationToken.None)
        Assert.Empty(rig.Drive.Snapshot())
        clock.Ms = 10_000_000
        Assert.Equal(0, Await a.RefreshStandingAsync(CancellationToken.None))
        Assert.Empty(rig.Drive.Snapshot())
    End Function

    <Fact>
    Public Async Function Sign_out_deletes_the_devices_own_records_and_nothing_else() As Task
        Dim rig As New DriveRig()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim b As ExchangeDriveDirectory = DirectoryOf(rig, IdB)
        Dim shareId As String = ExchangeIds.NewId()
        Dim castId As String = ExchangeIds.NewId()
        Await a.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        Await a.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, ResourceJson(shareId, IdA, "Photos"), 0, CancellationToken.None)
        Await a.PublishAsync(ExchangeDriveRecordKind.Broadcast, castId, BroadcastJson(castId, IdA), 0, CancellationToken.None)
        Await b.PublishAsync(ExchangeDriveRecordKind.Device, IdB, DeviceJson(IdB, "Laptop"), 0, CancellationToken.None)

        ' what must survive: a probe file, the packet queue with a payload NAMED like a record, the other device's record
        rig.Drive.AddFile("probe.json", New Byte() {1})
        Dim queue As FakeDriveFile = rig.Drive.AddFolder(ExchangeDrivePackets.QueueFolderName)
        Dim packet As FakeDriveFile = rig.Drive.AddFolder("p1", queue.Id)
        rig.Drive.AddFile(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, IdA), New Byte() {2}, packet.Id)

        Assert.Equal(3, Await a.DeleteAllOwnAsync(CancellationToken.None))
        Assert.Equal(0, a.StandingCount)
        Dim names As List(Of String) = rig.Drive.Snapshot().Select(Function(f) f.Name).ToList()
        Assert.Contains(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, IdB), names)
        Assert.Contains("probe.json", names)
        Assert.Contains("p1", names)
        ' the payload that only looked like a record is still in its packet folder
        Assert.Single(rig.Drive.Children(packet.Id))
        Assert.Equal(1, names.Where(Function(n) n.StartsWith("fmsx-", StringComparison.Ordinal) AndAlso n <> ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, IdA)).Count())
        Assert.Equal(0, names.Where(Function(n) n.StartsWith("fmsx-resource", StringComparison.Ordinal) OrElse n.StartsWith("fmsx-broadcast", StringComparison.Ordinal)).Count())

        ' the other PC no longer sees it
        Dim seen As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Assert.Empty(seen.Snapshot.Devices)
        Assert.Empty(seen.Snapshot.Resources)
    End Function

    <Fact>
    Public Async Function Records_left_by_a_crash_are_swept_by_the_next_start_and_live_ones_stay() As Task
        Dim rig As New DriveRig()
        Dim before As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim oldShare As String = ExchangeIds.NewId()
        Await before.PublishAsync(ExchangeDriveRecordKind.Resource, oldShare, ResourceJson(oldShare, IdA, "Old"), 0, CancellationToken.None)
        rig.Drive.Advance(TimeSpan.FromSeconds(400))

        ' the program starts again: a new instance publishes a fresh record, then sweeps
        Dim after As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim newShare As String = ExchangeIds.NewId()
        Await after.PublishAsync(ExchangeDriveRecordKind.Resource, newShare, ResourceJson(newShare, IdA, "New"), 0, CancellationToken.None)
        Assert.Equal(1, Await after.DeleteOwnExpiredAsync(CancellationToken.None))
        Dim names As List(Of String) = rig.Drive.Snapshot().Select(Function(f) f.Name).ToList()
        Assert.DoesNotContain(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Resource, oldShare), names)
        Assert.Contains(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Resource, newShare), names)
    End Function

    <Fact>
    Public Async Function A_record_this_program_cannot_read_is_skipped_and_foreign_files_are_left_alone() As Task
        Dim rig As New DriveRig()
        Dim b As ExchangeDriveDirectory = DirectoryOf(rig, IdB)
        Dim futureId As String = ExchangeIds.NewId()
        Dim brokenId As String = ExchangeIds.NewId()
        Dim requestId As String = ExchangeIds.NewId()
        ' schemaVersion 3 (a newer program), a file that is no JSON, a request record (not built before rung 3), an unrelated file
        rig.Drive.AddFile(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Resource, futureId), Encoding.UTF8.GetBytes("{""schemaVersion"":3,""type"":""resource"",""writtenAt"":""2026-10-23T12:00:00Z"",""ttlSeconds"":300,""record"":{}}"))
        rig.Drive.AddFile(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Device, brokenId), Encoding.UTF8.GetBytes("not json"))
        rig.Drive.AddFile(ExchangeDriveRecords.FileName(ExchangeDriveRecordKind.Request, requestId), Encoding.UTF8.GetBytes("{}"))
        rig.Drive.AddFile("notes.txt", New Byte() {1, 2, 3})

        Dim result As ExchangeDriveDirectoryResult = Await b.ReadAsync(CancellationToken.None)
        Assert.Equal(2, result.SkippedFiles)
        Assert.Empty(result.Snapshot.Devices)
        Assert.Empty(result.Snapshot.Resources)
        Assert.Equal(4, rig.Drive.Snapshot().Count)
    End Function

    <Fact>
    Public Async Function An_unchanged_record_is_not_downloaded_again() As Task
        Dim rig As New DriveRig()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim b As ExchangeDriveDirectory = DirectoryOf(rig, IdB)
        Await a.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        Await b.ReadAsync(CancellationToken.None)
        rig.Drive.ClearEvents()
        Await b.ReadAsync(CancellationToken.None)
        Assert.DoesNotContain(rig.Drive.Events, Function(e) e.StartsWith("download:", StringComparison.Ordinal))
    End Function

    <Fact>
    Public Async Function A_listing_and_the_queue_poll_can_share_one_call() As Task
        Dim rig As New DriveRig()
        Dim a As ExchangeDriveDirectory = DirectoryOf(rig, IdA)
        Dim b As ExchangeDriveDirectory = DirectoryOf(rig, IdB)
        Await a.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        Dim client As ExchangeDriveClient = rig.NewClient(New FakeDriveTokens())
        Dim listing As ExchangeDriveListing = Await client.ListAllAsync(CancellationToken.None)
        Dim before As Integer = rig.Drive.RequestCount
        Dim result As ExchangeDriveDirectoryResult = Await b.ReadFromAsync(listing, CancellationToken.None)
        Assert.Single(result.Snapshot.Devices)
        Assert.Same(listing, result.Listing)
        ' one download for the one record, no second listing
        Assert.Equal(before + 1, rig.Drive.RequestCount)
    End Function

End Class
#End If
