#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text
Imports System.Threading
Imports Xunit

''' <summary>
''' Specification 203 section 3.3 and acceptance criterion 3 - the packet queue on an in-memory Drive: files copied from one PC
''' to another arrive whole with the SHA-256 the manifest said (and never partial), the manifest is written last, the right
''' device deletes the right packet at the right time, expiry and orphans are judged on Drive's clock, the quota is read before
''' a byte goes up, the clipboard value is the latest manifest, and a request is answered or given up after 120 seconds.
''' </summary>
Public Class ExchangeDriveQueueTests
    Implements IDisposable

    Private Const IdA As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdB As String = "BBBBBBBBBBBBBBBBBBBBBA"
    Private Const IdC As String = "CCCCCCCCCCCCCCCCCCCCCA"

    Private ReadOnly _temp As New List(Of String)

    Private NotInheritable Class Clock
        Public Ms As Long
        Public Function ReadMs() As Long
            Return Ms
        End Function
        Public Function Wait(span As TimeSpan, token As CancellationToken) As Task
            Ms += CLng(span.TotalMilliseconds)
            Return Task.CompletedTask
        End Function
    End Class

    Private NotInheritable Class Device
        Public Id As String = ""
        Public Name As String = ""
        Public Client As ExchangeDriveClient
        Public Queue As ExchangeDriveQueue
        Public Handled As ExchangeDriveMemoryHandledStore
    End Class

    Private Shared Function NewDevice(rig As DriveRig, id As String, name As String, Optional clock As Clock = Nothing, Optional packetBytes As Long = 0) As Device
        Dim device As New Device()
        device.Id = id
        device.Name = name
        device.Client = rig.NewClient(New FakeDriveTokens())
        device.Handled = New ExchangeDriveMemoryHandledStore()
        Dim delay As Func(Of TimeSpan, CancellationToken, Task) = Nothing
        Dim monotonic As Func(Of Long) = Nothing
        If clock IsNot Nothing Then
            delay = AddressOf clock.Wait
            monotonic = AddressOf clock.ReadMs
        End If
        device.Queue = New ExchangeDriveQueue(device.Client, id, name, device.Handled, Nothing, If(packetBytes > 0, packetBytes, ExchangeDrivePackets.DefaultPacketBytes), delay, monotonic)
        Return device
    End Function

    Private Shared Async Function PollOf(device As Device) As Task(Of ExchangeDriveQueuePoll)
        Dim listing As ExchangeDriveListing = Await device.Client.ListAllAsync(CancellationToken.None)
        Return Await device.Queue.PollAsync(listing, CancellationToken.None)
    End Function

    Private Function NewFolder() As String
        Dim path As String = Directory.CreateTempSubdirectory("fmsgd").FullName
        _temp.Add(path)
        Return path
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each path As String In _temp
            Try
                Directory.Delete(path, True)
            Catch ex As IOException
            End Try
        Next
    End Sub

    Private Shared Function QueueFolder(rig As DriveRig) As FakeDriveFile
        Dim existing As FakeDriveFile = rig.Drive.Find(ExchangeDrivePackets.QueueFolderName)
        Return If(existing, rig.Drive.AddFolder(ExchangeDrivePackets.QueueFolderName))
    End Function

    ''' <summary>A committed packet written the way the shipped phone writes it (payload files, then the manifest).</summary>
    Private Shared Function SeedPacket(rig As DriveRig, packetId As String, manifestJson As String, ParamArray payloads As KeyValuePair(Of String, Byte())()) As FakeDriveFile
        Dim queue As FakeDriveFile = QueueFolder(rig)
        Dim folder As FakeDriveFile = rig.Drive.AddFolder(packetId, queue.Id)
        For Each payload As KeyValuePair(Of String, Byte()) In payloads
            rig.Drive.AddFile(payload.Key, payload.Value, folder.Id)
        Next
        rig.Drive.AddFile(ExchangeDrivePackets.ManifestFileName, Encoding.UTF8.GetBytes(manifestJson), folder.Id)
        Return folder
    End Function

    Private Shared Function Payload(name As String, content As Byte()) As KeyValuePair(Of String, Byte())
        Return New KeyValuePair(Of String, Byte())(name, content)
    End Function

    ' ------------------------------------------------------------------------------------------ files, criterion 3

    <Fact>
    Public Async Function Files_copied_from_one_PC_to_another_arrive_whole_with_a_matching_sha256() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim small As Byte() = DriveRig.RandomBytes(10_000, 11)
        Dim big As Byte() = DriveRig.RandomBytes(6 * 1024 * 1024, 12) ' above 5 MiB: the resumable path
        Dim sources As ExchangeDriveSendSource() = {
            ExchangeDriveSendSource.FromBytes("note.txt", "text/plain", small),
            ExchangeDriveSendSource.FromBytes("clip.mp4", "video/mp4", big)}
        Dim lastDone As Long = 0
        Dim total As Long = 0

        Dim sent As ExchangeDriveSendResult = Await a.Queue.SendFilesAsync(sources, IdB, "Laptop", CancellationToken.None,
            Sub(done As Long, all As Long)
                lastDone = done
                total = all
            End Sub)
        Assert.True(sent.Succeeded)
        Assert.Equal(small.LongLength + big.LongLength, total)
        Assert.Equal(total, lastDone)

        ' the commit marker is written LAST
        Dim creates As List(Of String) = rig.Drive.Events.Where(Function(e) e.StartsWith("create:", StringComparison.Ordinal)).ToList()
        Assert.Equal(New String() {"create:note.txt", "create:clip.mp4", "create:manifest.json"}, creates.ToArray())

        Dim poll As ExchangeDriveQueuePoll = Await PollOf(b)
        Dim packet As ExchangeDrivePacket = Assert.Single(poll.Packets)
        Assert.Equal(ExchangeDriveAddress.ThisDevice, packet.Address)
        Assert.Equal(sent.PacketId, packet.PacketId)
        Assert.Same(packet, Assert.Single(b.Queue.IncomingFiles(poll)))

        Dim destination As String = NewFolder()
        Dim result As ExchangeDriveReceiveResult = Await b.Queue.ReceiveFilesAsync(packet, destination, CancellationToken.None)
        Assert.True(result.Succeeded)
        Assert.Equal(2, result.SavedPaths.Count)
        Assert.Equal(DriveRig.Sha256Hex(small), DriveRig.Sha256Hex(File.ReadAllBytes(result.SavedPaths(0))))
        Assert.Equal(DriveRig.Sha256Hex(big), DriveRig.Sha256Hex(File.ReadAllBytes(result.SavedPaths(1))))
        Assert.Equal("note.txt", Path.GetFileName(result.SavedPaths(0)))
        Assert.Equal(2, Directory.GetFiles(destination).Length)

        ' targeted and taken in full: the packet folder is gone from Drive, the person's space is back
        Assert.True(result.PacketDeleted)
        Assert.Empty(rig.Drive.Children(QueueFolder(rig).Id))
        Assert.Empty((Await PollOf(b)).Packets)
    End Function

    <Fact>
    Public Async Function A_name_that_is_already_taken_gets_a_suffix_and_the_old_file_is_untouched() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim destination As String = NewFolder()
        File.WriteAllText(Path.Combine(destination, "note.txt"), "mine")
        File.WriteAllText(Path.Combine(destination, "note (2).txt"), "also mine")

        Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("note.txt", "text/plain", Encoding.UTF8.GetBytes("theirs"))}, IdB, "Laptop", CancellationToken.None)
        Dim packet As ExchangeDrivePacket = Assert.Single((Await PollOf(b)).Packets)
        Dim result As ExchangeDriveReceiveResult = Await b.Queue.ReceiveFilesAsync(packet, destination, CancellationToken.None)
        Assert.True(result.Succeeded)
        Assert.Equal("note (3).txt", Path.GetFileName(result.SavedPaths(0)))
        Assert.Equal("mine", File.ReadAllText(Path.Combine(destination, "note.txt")))
        Assert.Equal("also mine", File.ReadAllText(Path.Combine(destination, "note (2).txt")))
        Assert.Equal("theirs", File.ReadAllText(result.SavedPaths(0)))
    End Function

    <Fact>
    Public Async Function A_packet_with_a_name_outside_the_forms_is_refused_before_anything_is_written() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim bytes As Byte() = New Byte() {1}
        For Each bad As String In New String() {"a/b.txt", "..\x.txt", "manifest.json", "..", ""}
            Dim result As ExchangeDriveSendResult = Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes(bad, "text/plain", bytes)}, IdB, "Laptop", CancellationToken.None)
            Assert.Equal(ExchangeDriveSendOutcome.BadPacket, result.Outcome)
        Next
        Dim twice As ExchangeDriveSendResult = Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("x.txt", "text/plain", bytes), ExchangeDriveSendSource.FromBytes("X.TXT", "text/plain", bytes)}, IdB, "Laptop", CancellationToken.None)
        Assert.Equal(ExchangeDriveSendOutcome.BadPacket, twice.Outcome)
        Assert.Empty(rig.Drive.Snapshot())
    End Function

    <Fact>
    Public Async Function A_file_that_is_not_what_the_manifest_said_is_never_placed() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim original As Byte() = DriveRig.RandomBytes(5000, 21)
        Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("pic.jpg", "image/jpeg", original)}, IdB, "Laptop", CancellationToken.None)
        Dim stored As FakeDriveFile = rig.Drive.Find("pic.jpg")
        Dim destination As String = NewFolder()

        ' same size, other bytes: the hash catches it
        Dim tampered As Byte() = DirectCast(original.Clone(), Byte())
        tampered(100) = CByte(tampered(100) Xor 255)
        stored.Content = tampered
        Dim packet As ExchangeDrivePacket = Assert.Single((Await PollOf(b)).Packets)
        Dim result As ExchangeDriveReceiveResult = Await b.Queue.ReceiveFilesAsync(packet, destination, CancellationToken.None)
        Assert.Equal(ExchangeDriveReceiveOutcome.Corrupt, result.Outcome)
        Assert.Equal("pic.jpg", result.FailedName)
        Assert.False(result.PacketDeleted)
        Assert.Empty(Directory.GetFileSystemEntries(destination))

        ' cut short: the size catches it
        stored.Content = original.Take(4000).ToArray()
        packet = Assert.Single((Await PollOf(b)).Packets)
        result = Await b.Queue.ReceiveFilesAsync(packet, destination, CancellationToken.None)
        Assert.Equal(ExchangeDriveReceiveOutcome.Corrupt, result.Outcome)
        Assert.Empty(Directory.GetFileSystemEntries(destination))

        ' the packet stays for another attempt, and is not marked taken
        Dim again As ExchangeDriveQueuePoll = Await PollOf(b)
        Assert.Single(b.Queue.IncomingFiles(again))
        stored.Content = original
        packet = Assert.Single(again.Packets)
        Assert.True((Await b.Queue.ReceiveFilesAsync(packet, destination, CancellationToken.None)).Succeeded)
    End Function

    <Fact>
    Public Async Function A_file_the_manifest_names_but_the_folder_lacks_is_reported_missing() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("pic.jpg", "image/jpeg", New Byte() {1, 2, 3})}, IdB, "Laptop", CancellationToken.None)
        Dim stored As FakeDriveFile = rig.Drive.Find("pic.jpg")
        Dim folder As FakeDriveFile = rig.Drive.Snapshot().First(Function(f) f.IsFolder AndAlso f.Name <> ExchangeDrivePackets.QueueFolderName)
        Await rig.Client.DeleteAsync(stored.Id, CancellationToken.None)
        Dim packet As ExchangeDrivePacket = Assert.Single((Await PollOf(b)).Packets)
        Dim result As ExchangeDriveReceiveResult = Await b.Queue.ReceiveFilesAsync(packet, NewFolder(), CancellationToken.None)
        Assert.Equal(ExchangeDriveReceiveOutcome.MissingFile, result.Outcome)
        Assert.NotNull(rig.Drive.Find(folder.Name))
    End Function

    ' ------------------------------------------------------------------------------------------ who takes what

    <Fact>
    Public Async Function An_untargeted_packet_stays_and_each_PC_takes_it_once() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim c As Device = NewDevice(rig, IdC, "Tablet")
        Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("for-all.txt", "text/plain", Encoding.UTF8.GetBytes("hi"))}, "", "", CancellationToken.None)

        Assert.Empty(a.Queue.IncomingFiles(Await PollOf(a)))   ' a sender is not its own recipient
        Dim pollB As ExchangeDriveQueuePoll = Await PollOf(b)
        Dim received As ExchangeDriveReceiveResult = Await b.Queue.ReceiveFilesAsync(Assert.Single(b.Queue.IncomingFiles(pollB)), NewFolder(), CancellationToken.None)
        Assert.True(received.Succeeded)
        Assert.False(received.PacketDeleted)

        Dim afterB As ExchangeDriveQueuePoll = Await PollOf(b)
        Assert.Single(afterB.Packets)
        Assert.Empty(b.Queue.IncomingFiles(afterB))
        Assert.Single(c.Queue.IncomingFiles(Await PollOf(c)))
    End Function

    <Fact>
    Public Async Function A_packet_for_another_device_is_not_ours_to_take() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim c As Device = NewDevice(rig, IdC, "Tablet")
        Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("x.txt", "text/plain", New Byte() {1})}, IdB, "Laptop", CancellationToken.None)
        rig.Drive.ClearEvents()
        Dim poll As ExchangeDriveQueuePoll = Await PollOf(c)
        Dim packet As ExchangeDrivePacket = Assert.Single(poll.Packets)
        Assert.Equal(ExchangeDriveAddress.OtherDevice, packet.Address)
        Assert.Empty(c.Queue.IncomingFiles(poll))
        Dim result As ExchangeDriveReceiveResult = Await c.Queue.ReceiveFilesAsync(packet, NewFolder(), CancellationToken.None)
        Assert.Equal(ExchangeDriveReceiveOutcome.NotForThisDevice, result.Outcome)
        Assert.DoesNotContain(rig.Drive.Events, Function(e) e.Contains("x.txt"))
        Assert.Equal(1, rig.Drive.Snapshot().Where(Function(f) f.Name = "x.txt").Count())
    End Function

    <Fact>
    Public Async Function The_shipped_phones_packet_is_received_by_name_and_deleted_as_targeted() As Task
        Dim rig As New DriveRig()
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim photo As Byte() = DriveRig.RandomBytes(3000, 31)
        Dim video As Byte() = DriveRig.RandomBytes(4000, 32)
        ' version 1: names only, no files[], no sender id, the target by display name, status PENDING
        Dim manifest As String = "{""packetId"":""4f1c0a52-7d0e-4c3e-9d0b-1a2b3c4d5e6f"",""createdAtEpochMs"":1790000000000,""senderDeviceName"":""Pixel"",""targetDeviceName"":""laptop""," &
            """payloadKind"":""MEDIA_FILES"",""fileNames"":[""a.jpg"",""b.mp4""],""totalSizeBytes"":7000,""ttlDays"":3,""status"":""PENDING""}"
        SeedPacket(rig, "4f1c0a52-7d0e-4c3e-9d0b-1a2b3c4d5e6f", manifest, Payload("a.jpg", photo), Payload("b.mp4", video))

        Dim poll As ExchangeDriveQueuePoll = Await PollOf(b)
        Dim packet As ExchangeDrivePacket = Assert.Single(b.Queue.IncomingFiles(poll))
        Assert.Equal(ExchangeDriveAddress.ThisDevice, packet.Address)
        Dim result As ExchangeDriveReceiveResult = Await b.Queue.ReceiveFilesAsync(packet, NewFolder(), CancellationToken.None)
        Assert.True(result.Succeeded)
        Assert.Equal(photo, File.ReadAllBytes(result.SavedPaths(0)))
        Assert.Equal(video, File.ReadAllBytes(result.SavedPaths(1)))
        Assert.True(result.PacketDeleted)
    End Function

    <Fact>
    Public Async Function A_settings_export_or_an_unknown_kind_is_listed_but_never_offered() As Task
        Dim rig As New DriveRig()
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        SeedPacket(rig, "settings-1", "{""packetId"":""settings-1"",""createdAtEpochMs"":1,""senderDeviceName"":""Pixel"",""targetDeviceName"":null,""payloadKind"":""SETTINGS"",""fileNames"":[""s.json""],""totalSizeBytes"":1,""ttlDays"":3,""status"":""PENDING""}", Payload("s.json", New Byte() {1}))
        SeedPacket(rig, "future-1", "{""packetId"":""future-1"",""createdAtEpochMs"":1,""senderDeviceName"":""Pixel"",""targetDeviceName"":null,""payloadKind"":""HOLOGRAM"",""fileNames"":[],""totalSizeBytes"":0,""ttlDays"":3,""status"":""PENDING""}")
        Dim poll As ExchangeDriveQueuePoll = Await PollOf(b)
        Assert.Equal(2, poll.Packets.Count)
        Assert.Empty(b.Queue.IncomingFiles(poll))
        Dim result As ExchangeDriveReceiveResult = Await b.Queue.ReceiveFilesAsync(poll.Packets(0), NewFolder(), CancellationToken.None)
        Assert.Equal(ExchangeDriveReceiveOutcome.NotReceivable, result.Outcome)
    End Function

    <Fact>
    Public Async Function The_person_can_delete_any_packet_of_the_queue() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("x.txt", "text/plain", New Byte() {1, 2, 3})}, IdB, "Laptop", CancellationToken.None)
        Dim packet As ExchangeDrivePacket = Assert.Single((Await PollOf(a)).Packets)
        Assert.Equal(3L, packet.DriveBytes)
        Await a.Queue.DeletePacketAsync(packet, CancellationToken.None)
        Assert.Empty((Await PollOf(a)).Packets)
        Assert.Empty(rig.Drive.Children(QueueFolder(rig).Id))
    End Function

    ' ------------------------------------------------------------------------------------------ expiry and orphans

    <Fact>
    Public Async Function An_unfinished_upload_is_not_a_packet_and_an_old_one_is_swept_as_an_orphan() As Task
        Dim rig As New DriveRig()
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim queue As FakeDriveFile = QueueFolder(rig)
        Dim folder As FakeDriveFile = rig.Drive.AddFolder("half-sent", queue.Id)
        rig.Drive.AddFile("part.bin", New Byte() {1}, folder.Id)

        Dim fresh As ExchangeDriveQueuePoll = Await PollOf(b)
        Assert.Empty(fresh.Packets)
        Assert.Equal(1, fresh.InProgress)
        Assert.Equal(0, fresh.Cleaned)

        rig.Drive.Advance(TimeSpan.FromDays(6))
        Assert.Equal(0, (Await PollOf(b)).Cleaned)

        rig.Drive.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1))
        Dim swept As ExchangeDriveQueuePoll = Await PollOf(b)
        Assert.Equal(1, swept.Cleaned)
        Assert.Empty(rig.Drive.Children(queue.Id))
    End Function

    <Fact>
    Public Async Function An_expired_packet_is_deleted_by_any_device_on_Drives_clock() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim c As Device = NewDevice(rig, IdC, "Tablet")
        Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("x.txt", "text/plain", New Byte() {1})}, IdB, "Laptop", CancellationToken.None, Nothing, 1)

        rig.Drive.Advance(TimeSpan.FromHours(23))
        Dim live As ExchangeDriveQueuePoll = Await PollOf(c)
        Assert.Single(live.Packets)
        Assert.Equal(0, live.Cleaned)

        rig.Drive.Advance(TimeSpan.FromHours(2))
        Dim gone As ExchangeDriveQueuePoll = Await PollOf(c)
        Assert.Empty(gone.Packets)
        Assert.Equal(1, gone.Cleaned)
        Assert.Empty(rig.Drive.Children(QueueFolder(rig).Id))
    End Function

    ' ------------------------------------------------------------------------------------------ failing and refusing

    <Fact>
    Public Async Function A_sender_whose_upload_failed_deletes_its_own_packet_folder_at_once() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        rig.Drive.FailWhen(Function(r) r.Method = "POST" AndAlso r.Path = "/upload/drive/v3/files" AndAlso r.BodyText().Contains("""name"":""manifest.json"""), 1, 400)
        Dim failure As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(
            Function() a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("x.txt", "text/plain", New Byte() {1, 2})}, IdB, "Laptop", CancellationToken.None))
        Assert.Equal(ExchangeDriveFailure.Other, failure.Failure)
        Assert.Contains("create:x.txt", rig.Drive.Events)
        Assert.DoesNotContain("create:manifest.json", rig.Drive.Events)
        Assert.Empty(rig.Drive.Children(QueueFolder(rig).Id))
        Assert.Equal(0, rig.Drive.Snapshot().Where(Function(f) f.Name = "x.txt").Count())
    End Function

    <Fact>
    Public Async Function A_packet_that_does_not_fit_is_refused_before_a_byte_is_uploaded() As Task
        Dim rig As New DriveRig()
        rig.Drive.QuotaLimit = 100_000
        rig.Drive.QuotaUsage = 0
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim result As ExchangeDriveSendResult = Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("big.bin", "application/octet-stream", New Byte(199_999) {})}, IdB, "Laptop", CancellationToken.None)
        Assert.Equal(ExchangeDriveSendOutcome.DriveFull, result.Outcome)
        Assert.Equal(99_000L, result.FreeBytes) ' the limit less the 1% reserve
        Assert.Empty(rig.Drive.Events)
        Assert.Empty(rig.Drive.Snapshot())

        Dim fits As ExchangeDriveSendResult = Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("ok.bin", "application/octet-stream", New Byte(98_999) {})}, IdB, "Laptop", CancellationToken.None)
        Assert.True(fits.Succeeded)
    End Function

    <Fact>
    Public Async Function A_packet_above_the_senders_packet_size_is_refused() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", Nothing, 1000)
        Dim result As ExchangeDriveSendResult = Await a.Queue.SendFilesAsync({ExchangeDriveSendSource.FromBytes("big.bin", "application/octet-stream", New Byte(1999) {})}, IdB, "Laptop", CancellationToken.None)
        Assert.Equal(ExchangeDriveSendOutcome.TooLarge, result.Outcome)
        Assert.Empty(rig.Drive.Snapshot())
    End Function

    ' ------------------------------------------------------------------------------------------ the clipboard

    <Fact>
    Public Async Function The_clipboard_value_is_the_latest_manifest_and_the_receiver_never_deletes_it() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim first As ExchangeDriveSendResult = Await a.Queue.SendClipboardTextAsync("line one" & vbCrLf & "line two", CancellationToken.None)
        Assert.True(first.Succeeded)

        Dim poll As ExchangeDriveQueuePoll = Await PollOf(b)
        Assert.NotNull(poll.ClipboardValue)
        Dim value As ExchangeDriveReceiveResult = Await b.Queue.ReceiveClipboardAsync(poll.ClipboardValue, NewFolder(), CancellationToken.None)
        Assert.True(value.Succeeded)
        Assert.Equal("line one" & vbLf & "line two", value.Text)
        Assert.False(value.PacketDeleted)
        Assert.Single((Await PollOf(b)).Packets)
        Assert.True((Await b.Queue.ReceiveClipboardAsync(poll.ClipboardValue, NewFolder(), CancellationToken.None)).Succeeded) ' read again on the next "get"

        ' a newer value replaces it, and its sender deletes the older packet
        rig.Drive.Advance(TimeSpan.FromMinutes(1))
        Dim second As ExchangeDriveSendResult = Await b.Queue.SendClipboardTextAsync("newer", CancellationToken.None)
        Assert.True(second.Succeeded)
        Dim afterwards As ExchangeDriveQueuePoll = Await PollOf(a)
        Assert.Single(afterwards.Packets)
        Assert.Equal(second.PacketId, afterwards.ClipboardValue.PacketId)
        Assert.Equal("newer", (Await a.Queue.ReceiveClipboardAsync(afterwards.ClipboardValue, NewFolder(), CancellationToken.None)).Text)
    End Function

    <Fact>
    Public Async Function Any_device_deletes_a_clipboard_packet_ten_minutes_older_than_the_value() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC")
        Dim b As Device = NewDevice(rig, IdB, "Laptop")
        Dim c As Device = NewDevice(rig, IdC, "Tablet")
        Dim d As Device = NewDevice(rig, "DDDDDDDDDDDDDDDDDDDDDA", "Phone")
        ' every delete fails at first, so the senders' own clean-up leaves the old values behind (a crash, a lost connection)
        rig.Drive.FailWhen(Function(r) r.Method = "DELETE", 1000, 403, "forbidden")
        Await a.Queue.SendClipboardTextAsync("old", CancellationToken.None)
        rig.Drive.Advance(TimeSpan.FromMinutes(5))
        Await b.Queue.SendClipboardTextAsync("middle", CancellationToken.None)
        rig.Drive.Advance(TimeSpan.FromMinutes(6))
        Await c.Queue.SendClipboardTextAsync("newest", CancellationToken.None)
        rig.Drive.ClearFailures()

        ' d sees three packets: "old" is 11 minutes behind the value, "middle" only 6 - inside the margin that protects a read in progress
        Dim poll As ExchangeDriveQueuePoll = Await PollOf(d)
        Assert.Equal(1, poll.Cleaned)
        Assert.Equal(2, poll.Packets.Count)
        Assert.Equal("newest", (Await d.Queue.ReceiveClipboardAsync(poll.ClipboardValue, NewFolder(), CancellationToken.None)).Text)

        ' the value itself is never deleted, however old it grows
        rig.Drive.Advance(TimeSpan.FromDays(3))
        Dim later As ExchangeDriveQueuePoll = Await PollOf(d)
        Assert.Equal(0, later.Cleaned)
        Assert.Equal("newest", (Await d.Queue.ReceiveClipboardAsync(later.ClipboardValue, NewFolder(), CancellationToken.None)).Text)
    End Function

    ' ------------------------------------------------------------------------------------------ request and reply

    <Fact>
    Public Async Function A_request_is_answered_with_a_reply_that_carries_its_file_and_both_are_cleaned_up() As Task
        Dim rig As New DriveRig()
        Dim clock As New Clock()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", clock)
        Dim b As Device = NewDevice(rig, IdB, "Laptop", clock)

        Dim request As ExchangeDriveSendResult = Await a.Queue.SendRequestAsync("{""op"":""list"",""path"":""/""}", IdB, "Laptop", CancellationToken.None)
        Assert.True(request.Succeeded)

        Dim pollB As ExchangeDriveQueuePoll = Await PollOf(b)
        Dim asked As ExchangeDrivePacket = Assert.Single(b.Queue.RequestsForThisDevice(pollB))
        Assert.Contains("""list""", asked.Manifest.RequestJson)
        Assert.Equal(900, ExchangeDrivePackets.LifetimeSeconds(asked.Manifest))

        Dim listing As Byte() = Encoding.UTF8.GetBytes("{""entries"":[{""name"":""a.jpg""}]}")
        Dim reply As ExchangeDriveSendResult = Await b.Queue.SendReplyAsync(asked, "ok", "", "2026-10-23T12:00:00Z", True,
            {ExchangeDriveSendSource.FromBytes("listing.json", "application/json", listing)}, CancellationToken.None)
        Assert.True(reply.Succeeded)

        Dim replies As IReadOnlyList(Of ExchangeDrivePacket) = Await a.Queue.AwaitRepliesAsync(request.PacketId, Nothing, CancellationToken.None)
        Dim answer As ExchangeDrivePacket = Assert.Single(replies)
        Assert.True(answer.Manifest.Final)
        Assert.Equal("ok", answer.Manifest.Outcome)
        Assert.Equal(request.PacketId, answer.Manifest.InReplyTo)
        Assert.Equal(listing, Await a.Queue.ReadPayloadAsync(answer, "listing.json", 1024 * 1024, CancellationToken.None))
        Assert.Null(Await a.Queue.ReadPayloadAsync(answer, "other.json", 1024, CancellationToken.None))
        Assert.Equal(0L, clock.Ms) ' the reply was already there: no wait

        ' a reply is deleted by the requester, a request by the producer that answered it (item CR)
        Await a.Queue.DeletePacketAsync(answer, CancellationToken.None)
        Await b.Queue.DeletePacketAsync(asked, CancellationToken.None)
        Assert.Empty(rig.Drive.Children(QueueFolder(rig).Id))
    End Function

    <Fact>
    Public Async Function Waiting_for_a_reply_gives_up_after_120_seconds_and_deletes_the_own_request() As Task
        Dim rig As New DriveRig()
        Dim clock As New Clock()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", clock)
        Dim request As ExchangeDriveSendResult = Await a.Queue.SendRequestAsync("{""op"":""list""}", IdB, "Laptop", CancellationToken.None)
        Assert.Single((Await PollOf(a)).Packets)

        Dim replies As IReadOnlyList(Of ExchangeDrivePacket) = Await a.Queue.AwaitRepliesAsync(request.PacketId, Nothing, CancellationToken.None)
        Assert.Empty(replies)
        Assert.True(clock.Ms >= 120_000)
        Assert.True(clock.Ms <= 130_000)
        Assert.Empty(rig.Drive.Children(QueueFolder(rig).Id))
    End Function

    <Fact>
    Public Async Function A_reply_already_seen_is_not_returned_twice() As Task
        Dim rig As New DriveRig()
        Dim clock As New Clock()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", clock)
        Dim b As Device = NewDevice(rig, IdB, "Laptop", clock)
        Dim request As ExchangeDriveSendResult = Await a.Queue.SendRequestAsync("{""op"":""fetch""}", IdB, "Laptop", CancellationToken.None)
        Dim asked As ExchangeDrivePacket = Assert.Single(b.Queue.RequestsForThisDevice(Await PollOf(b)))
        Dim part As ExchangeDriveSendResult = Await b.Queue.SendReplyAsync(asked, "ok", "", "", False, {ExchangeDriveSendSource.FromBytes("p1.bin", "application/octet-stream", New Byte() {1})}, CancellationToken.None)

        Dim seen As New HashSet(Of String)(StringComparer.Ordinal) From {part.PacketId}
        Dim replies As IReadOnlyList(Of ExchangeDrivePacket) = Await a.Queue.AwaitRepliesAsync(request.PacketId, seen, CancellationToken.None, 30)
        Assert.Empty(replies)
    End Function

    ' ------------------------------------------------------------------------------------------ the handled set

    <Fact>
    Public Sub The_handled_set_is_kept_in_a_file_that_appears_with_the_first_packet_and_stays_bounded()
        Dim path As String = System.IO.Path.Combine(NewFolder(), "sub", "drive-handled.txt")
        Dim store As New ExchangeDriveFileHandledStore(path)
        Assert.False(store.Contains("packet-1"))
        Assert.False(File.Exists(path)) ' nothing is created by asking
        store.Add("packet-1")
        store.Add("packet-1")
        store.Add("bad name")
        Assert.True(File.Exists(path))
        Assert.Equal(New String() {"packet-1"}, File.ReadAllLines(path))
        Assert.True(New ExchangeDriveFileHandledStore(path).Contains("packet-1"))

        For index As Integer = 1 To 2100
            store.Add("p" & index.ToString(Globalization.CultureInfo.InvariantCulture))
        Next
        Dim reloaded As New ExchangeDriveFileHandledStore(path)
        Assert.True(reloaded.Contains("p2100"))
        Assert.False(reloaded.Contains("packet-1"))
        Assert.True(File.ReadAllLines(path).Length <= 2000)
    End Sub

End Class
#End If
