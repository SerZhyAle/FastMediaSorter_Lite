Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 269 P4 - a scheduled operation through the person's own Google Drive (ticket 231 rules 3 and 4, DEVICE-EXCHANGE items CO, CN,
''' CR, CS), on the in-memory Drive and through the real executor: a destination sends whole packets and a file of the person's goes only
''' after its packet landed; a source hands a packet back to Drive only after every file of it reached the destination; a device asked
''' for its changes moves the operation's mark only on a clean run; Drive not answering skips a run and a lost sign-in or no room stops
''' it at the wall; and the three definitions that cannot work are refused before any request.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledDriveSideTests
    Implements IDisposable

    Private Const ThisPc As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const OtherPc As String = "BBBBBBBBBBBBBBBBBBBBBA"
    Private Const ResourceId As String = "res_0123456789abcdef"
    Private Const AsOfText As String = "2026-10-13T11:00:00.000Z"

    Private Shared ReadOnly Noon As New DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified)
    Private Shared ReadOnly Clear As CancellationToken = CancellationToken.None

    Private ReadOnly _drive As New FakeDriveHandler()
    Private ReadOnly _http As HttpClient
    Private ReadOnly _client As ExchangeDriveClient
    Private ReadOnly _handled As New ExchangeDriveMemoryHandledStore()
    Private ReadOnly _queue As ExchangeDriveQueue
    Private ReadOnly _tree As New ScratchTree()
    Private ReadOnly _journal As New List(Of ItemJournalEntry)()
    Private ReadOnly _scratchBefore As HashSet(Of String)
    Private _port As IDriveRequestPort
    Private _afterSend As Action

    Public Sub New()
        _http = New HttpClient(_drive, False)
        _client = New ExchangeDriveClient(_http, New FakeDriveTokens(), Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
        _queue = New ExchangeDriveQueue(_client, ThisPc, "This PC", _handled)
        _scratchBefore = New HashSet(Of String)(ScratchFolders(), StringComparer.OrdinalIgnoreCase)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _http.Dispose()
        _tree.Dispose()
    End Sub

    Private Shared Function ScratchFolders() As String()
        Return Directory.GetDirectories(Path.GetTempPath(), "fms-scheduled-*")
    End Function

    ''' <summary>The temporary folders a run made and left behind.</summary>
    Private Function LeftInTemp() As String()
        Return ScratchFolders().Where(Function(f As String) Not _scratchBefore.Contains(f)).ToArray()
    End Function

    Private Function Rig(Optional before As Action(Of String) = Nothing) As ScheduledServices
        Dim made As New ScheduledServices With {
            .Clock = Function() Noon,
            .IsDriveSignedIn = Function() True,
            .DeleteToRecycleBin = Function() False,
            .VolumeFacts = Function(place As String) New DeleteVolumeFacts With {.Kind = DeleteVolumeKind.FixedDisk},
            .ItemJournal = Sub(entry As ItemJournalEntry) _journal.Add(entry),
            .BeforeItem = before}
        Dim usual As Func(Of ScheduledLocation, ScheduledSideOpen) = made.OpenSide
        made.OpenSide = Function(location As ScheduledLocation) As ScheduledSideOpen
                            If location.Kind <> ScheduledLocationKind.Drive Then Return usual.Invoke(location)
                            Return ScheduledSideOpen.Opened(New DriveScheduledSide(location, _queue, _client, _port, Sub() _afterSend?.Invoke()))
                        End Function
        Return made
    End Function

    Private Function RunOf(op As ScheduledOperation, Optional services As ScheduledServices = Nothing, Optional token As CancellationToken = Nothing) As ScheduledRunResult
        Return New ScheduledExecutor(If(services, Rig())).Run(op, token)
    End Function

    ''' <summary>Files of this PC to the other device.</summary>
    Private Function ToDrive(action As ScheduledAction, Optional folder As String = "src") As ScheduledOperation
        Dim op As New ScheduledOperation With {.Id = "op1", .Name = "test", .Action = action}
        op.Source.Path = _tree.Dir(folder)
        op.Destination.Kind = ScheduledLocationKind.Drive
        op.Destination.ResourceId = OtherPc
        op.Destination.ResourceName = "Office PC"
        op.Destination.DriveDeviceName = "Office PC"
        Return op
    End Function

    ''' <summary>What other devices send to this PC, into a folder of this PC.</summary>
    Private Function FromDrive(action As ScheduledAction, Optional folder As String = "dst") As ScheduledOperation
        Dim op As New ScheduledOperation With {.Id = "op1", .Name = "test", .Action = action}
        op.Source.Kind = ScheduledLocationKind.Drive
        op.Destination.Path = _tree.Dir(folder)
        Return op
    End Function

    ''' <summary>The changes of a resource of the other device, into a folder of this PC.</summary>
    Private Function FromDevice(Optional resource As String = ResourceId) As ScheduledOperation
        Dim op As ScheduledOperation = FromDrive(ScheduledAction.Copy)
        op.Source.ResourceId = OtherPc
        op.Source.DriveResourceId = resource
        op.Source.DriveDeviceName = "Office PC"
        op.Source.ResourceName = "Office PC: Photos"
        Return op
    End Function

    Private Shared Function BodyOf(name As String) As Byte()
        Return Encoding.UTF8.GetBytes("body of " & name)
    End Function

    ''' <summary>One packet from the other device, addressed to this PC.</summary>
    Private Sub SendFromOther(ParamArray names As String())
        Dim other As New ExchangeDriveQueue(_client, OtherPc, "Office PC", New ExchangeDriveMemoryHandledStore())
        Dim sources As New List(Of ExchangeDriveSendSource)()
        For Each name As String In names
            Dim body As Byte() = BodyOf(name)
            sources.Add(New ExchangeDriveSendSource(name, ExchangeDriveSendSource.MimeTypeOf(name), Function() As Stream
                                                                                                        Return New MemoryStream(body)
                                                                                                    End Function))
        Next
        Assert.Equal(ExchangeDriveSendOutcome.Sent, other.SendFilesAsync(sources, ThisPc, "This PC", Clear).GetAwaiter().GetResult().Outcome)
    End Sub

    ''' <summary>The packets on Drive a device can still take, each as its sorted names, asked with a memory of its own.</summary>
    Private Function PacketsFor(id As String) As List(Of String())
        Dim queue As New ExchangeDriveQueue(_client, id, "reader", New ExchangeDriveMemoryHandledStore())
        Dim poll As ExchangeDriveQueuePoll = queue.PollAsync(_client.ListAllAsync(Clear).GetAwaiter().GetResult(), Clear).GetAwaiter().GetResult()
        Return queue.IncomingFiles(poll).Select(Function(p As ExchangeDrivePacket) ExchangeDrivePackets.ExpectedItems(p.Manifest).Select(Function(i As ExchangeDrivePacketItem) i.Name).OrderBy(Function(n As String) n, StringComparer.Ordinal).ToArray()).ToList()
    End Function

    Private Function Read(relative As String) As String
        Return _tree.Read(relative)
    End Function

    ' --- this PC to Drive ------------------------------------------------------------------------------

    <Fact>
    Public Sub A_copy_sends_one_packet_to_the_chosen_device_and_keeps_the_originals()
        _tree.Write("src\a.txt", "A")
        _tree.Write("src\b.txt", "BB")
        Dim polled As Integer = 0
        _afterSend = Sub() polled += 1

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Copy))

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.Equal(2, result.Files)
        Assert.Equal(3L, result.Bytes)
        Assert.Equal({"a.txt", "b.txt"}, Assert.Single(PacketsFor(OtherPc)))
        Assert.Empty(PacketsFor("CCCCCCCCCCCCCCCCCCCCCA"))
        Assert.Equal("A", Read("src\a.txt"))
        Assert.Equal("BB", Read("src\b.txt"))
        Assert.Equal(1, polled)
        Assert.Empty(LeftInTemp())
    End Sub

    <Fact>
    Public Sub A_destination_with_no_device_named_is_for_any_own_device()
        _tree.Write("src\a.txt", "A")
        Dim op As ScheduledOperation = ToDrive(ScheduledAction.Copy)
        op.Destination.ResourceId = ""
        op.Destination.DriveDeviceName = ""

        Assert.Equal(ScheduledRunOutcome.Ok, RunOf(op).Outcome)

        Assert.Single(PacketsFor(OtherPc))
        Assert.Single(PacketsFor("CCCCCCCCCCCCCCCCCCCCCA"))
    End Sub

    <Fact>
    Public Sub A_move_removes_the_originals_after_the_packet_landed_and_writes_the_journal()
        _tree.Write("src\a.txt", "A")
        Dim seenOnDrive As Boolean = False
        Dim originalStillThere As Boolean = False
        _afterSend = Sub()
                         seenOnDrive = PacketsFor(OtherPc).Count = 1
                         originalStillThere = _tree.Exists("src\a.txt")
                     End Sub

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move))

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.True(seenOnDrive)
        Assert.True(originalStillThere)
        Assert.False(_tree.Exists("src\a.txt"))
        Assert.Equal("move", Assert.Single(_journal).Verb)
    End Sub

    <Fact>
    Public Sub A_file_changed_under_its_packet_stays_and_the_run_says_so()
        Dim place As String = _tree.Write("src\a.txt", "A")
        _afterSend = Sub() File.WriteAllText(place, "changed while the packet was on its way")

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move))

        Assert.Equal(ScheduledRunOutcome.Failed, result.Outcome)
        Assert.Equal(1, result.Failed)
        Assert.Equal(0, result.Files)
        Assert.Contains("kept-both", result.FirstError)
        Assert.True(_tree.Exists("src\a.txt"))
    End Sub

    <Fact>
    Public Sub Folders_flatten_and_a_repeated_name_starts_the_next_packet()
        _tree.Write("src\one\x.txt", "1")
        _tree.Write("src\two\x.txt", "2")
        _tree.Write("src\z.txt", "3")

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Copy))

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.Equal(3, result.Files)
        Dim packets As List(Of String()) = PacketsFor(OtherPc)
        Assert.Equal(2, packets.Count)
        Assert.Equal(3, packets.Sum(Function(names As String()) names.Length))
        Assert.All(packets, Sub(names As String()) Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count()))
    End Sub

    ''' <summary>A destination with limits a test can reach; the Drive side answers the real ones (1 000 files, 5 GiB).</summary>
    Private NotInheritable Class SmallPacketSide
        Implements IScheduledSide, IScheduledPacketTarget

        Friend ReadOnly Sent As New List(Of String())()
        Friend Files As Integer = 2
        Friend Bytes As Long = 10

        Public ReadOnly Property IsLocalPath As Boolean Implements IScheduledSide.IsLocalPath
            Get
                Return False
            End Get
        End Property
        Public ReadOnly Property Root As String Implements IScheduledSide.Root
            Get
                Return "packets"
            End Get
        End Property
        Public Function Describe() As String Implements IScheduledSide.Describe
            Return "packets"
        End Function
        Public Function CheckReadable() As SideProblem Implements IScheduledSide.CheckReadable
            Return SideProblem.Ok
        End Function
        Public Function CheckWritable() As SideProblem Implements IScheduledSide.CheckWritable
            Return SideProblem.Ok
        End Function
        Public Function List(recursive As Boolean, ct As CancellationToken) As List(Of SideEntry) Implements IScheduledSide.List
            Return New List(Of SideEntry)()
        End Function
        Public Function Combine(relative As String) As String Implements IScheduledSide.Combine
            Return relative
        End Function
        Public Function Exists(relative As String) As Boolean Implements IScheduledSide.Exists
            Return False
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
        End Sub
        Public Function AcceptsName(name As String) As Boolean Implements IScheduledPacketTarget.AcceptsName
            Return Not name.StartsWith("bad", StringComparison.Ordinal)
        End Function
        Public ReadOnly Property MaxPacketFiles As Integer Implements IScheduledPacketTarget.MaxPacketFiles
            Get
                Return Files
            End Get
        End Property
        Public ReadOnly Property MaxPacketBytes As Long Implements IScheduledPacketTarget.MaxPacketBytes
            Get
                Return Bytes
            End Get
        End Property
        Public Sub SendPacket(items As IReadOnlyList(Of ScheduledPacketFile), ct As CancellationToken) Implements IScheduledPacketTarget.SendPacket
            Sent.Add(items.Select(Function(i As ScheduledPacketFile) i.Name).ToArray())
        End Sub
    End Class

    Private Function RigOver(side As SmallPacketSide) As ScheduledServices
        Dim made As ScheduledServices = Rig()
        Dim usual As Func(Of ScheduledLocation, ScheduledSideOpen) = made.OpenSide
        made.OpenSide = Function(location As ScheduledLocation) If(location.Kind = ScheduledLocationKind.Drive, ScheduledSideOpen.Opened(side), usual.Invoke(location))
        Return made
    End Function

    <Fact>
    Public Sub A_packet_holds_at_most_what_the_target_allows()
        _tree.Write("src\a.txt", "1")
        _tree.Write("src\b.txt", "2")
        _tree.Write("src\c.txt", "3")
        Dim side As New SmallPacketSide()

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Copy), RigOver(side))

        Assert.Equal(3, result.Files)
        Assert.Equal({"a.txt", "b.txt"}, side.Sent(0))
        Assert.Equal({"c.txt"}, side.Sent(1))
    End Sub

    <Fact>
    Public Sub Bytes_start_the_next_packet_and_one_file_over_the_limit_or_with_a_bad_name_fails_alone()
        _tree.Write("src\a.txt", "123456")
        _tree.Write("src\b.txt", "123456")
        _tree.Write("src\bad.txt", "1")
        _tree.Write("src\huge.txt", "12345678901")
        Dim side As New SmallPacketSide()

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move), RigOver(side))

        Assert.Equal(ScheduledRunOutcome.Failed, result.Outcome)
        Assert.Equal(2, result.Files)
        Assert.Equal(2, result.Failed)
        Assert.Equal({"a.txt"}, side.Sent(0))
        Assert.Equal({"b.txt"}, side.Sent(1))
        Assert.False(_tree.Exists("src\a.txt"))
        Assert.False(_tree.Exists("src\b.txt"))
        Assert.True(_tree.Exists("src\bad.txt"))
        Assert.True(_tree.Exists("src\huge.txt"))
    End Sub

    <Fact>
    Public Sub A_locked_file_fails_alone_and_the_rest_of_its_packet_goes()
        _tree.Write("src\a.txt", "A")
        Dim locked As String = _tree.Write("src\b.txt", "B")
        Using holder As New FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)
            Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move))

            Assert.Equal(ScheduledRunOutcome.Failed, result.Outcome)
            Assert.Equal(1, result.Files)
            Assert.Equal(1, result.Failed)
        End Using
        Assert.Equal({"a.txt"}, Assert.Single(PacketsFor(OtherPc)))
        Assert.False(_tree.Exists("src\a.txt"))
        Assert.True(_tree.Exists("src\b.txt"))
    End Sub

    <Fact>
    Public Sub A_full_Drive_stops_at_the_wall_before_a_byte_and_keeps_every_original()
        _tree.Write("src\a.txt", "A")
        _drive.QuotaLimit = 1000
        _drive.QuotaUsage = 1000

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move))

        Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
        Assert.StartsWith("destination-drive-full|", result.Detail)
        Assert.Equal(0, _drive.CountNamed("a.txt"))
        Assert.True(_tree.Exists("src\a.txt"))
    End Sub

    <Fact>
    Public Sub A_packet_that_does_not_fit_stops_at_the_wall_and_keeps_every_original()
        _tree.Write("src\a.bin", New String("x"c, 5000))
        _drive.QuotaLimit = 1000
        _drive.QuotaUsage = 100

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move))

        Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
        Assert.StartsWith("destination-drive-full|", result.Detail)
        Assert.Equal(ScheduledHalt.Wall, result.Halt)
        Assert.Equal(0, _drive.CountNamed("a.bin"))
        Assert.True(_tree.Exists("src\a.bin"))
    End Sub

    <Fact>
    Public Sub A_lost_sign_in_stops_at_the_wall()
        _tree.Write("src\a.txt", "A")
        _drive.FailWhen(Function(r) True, 100000, 401)

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move))

        Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
        Assert.StartsWith("destination-signed-out|", result.Detail)
        Assert.True(_tree.Exists("src\a.txt"))
    End Sub

    <Fact>
    Public Sub Drive_not_answering_skips_the_run_without_stopping_the_operation()
        _tree.Write("src\a.txt", "A")
        _drive.FailWhen(Function(r) True, 100000, 503)

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Move))

        Assert.Equal(ScheduledRunOutcome.Skipped, result.Outcome)
        Assert.StartsWith("destination-drive-unavailable|", result.Detail)
        Assert.Equal(ScheduledHalt.None, result.Halt)
        Assert.True(_tree.Exists("src\a.txt"))
    End Sub

    <Fact>
    Public Sub An_archive_goes_as_one_packet()
        _tree.Write("src\a.txt", "A")
        _tree.Write("src\b.txt", "B")

        Dim result As ScheduledRunResult = RunOf(ToDrive(ScheduledAction.Archive))

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.Equal(2, result.Files)
        Dim names As String() = Assert.Single(PacketsFor(OtherPc))
        Assert.EndsWith(".zip", Assert.Single(names), StringComparison.OrdinalIgnoreCase)
        Assert.True(_tree.Exists("src\a.txt"))
        Assert.Empty(LeftInTemp())
    End Sub

    ' --- what other devices send, to a folder of this PC ---------------------------------------------

    <Fact>
    Public Sub A_pick_up_delivers_every_file_and_the_packet_leaves_Drive()
        SendFromOther("a.txt", "b.txt")

        Dim result As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.Equal(2, result.Files)
        Assert.Equal("body of a.txt", Read("dst\a.txt"))
        Assert.Equal("body of b.txt", Read("dst\b.txt"))
        Assert.Empty(PacketsFor(ThisPc))
        Assert.All(_journal, Sub(entry As ItemJournalEntry) Assert.Equal("drive-pickup", entry.Verb))
        Assert.All(_journal, Sub(entry As ItemJournalEntry) Assert.DoesNotContain("fms-scheduled", entry.Source))
        Assert.Equal("", result.DriveMark)
        Assert.Empty(LeftInTemp())
    End Sub

    <Fact>
    Public Sub A_second_run_finds_nothing_and_is_a_success()
        SendFromOther("a.txt")
        Assert.Equal(1, RunOf(FromDrive(ScheduledAction.Copy)).Files)

        Dim again As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))

        Assert.Equal(ScheduledRunOutcome.Ok, again.Outcome)
        Assert.Equal(0, again.Files)
    End Sub

    <Fact>
    Public Sub A_packet_for_another_device_is_not_touched()
        Dim other As New ExchangeDriveQueue(_client, OtherPc, "Office PC", New ExchangeDriveMemoryHandledStore())
        Dim body As Byte() = BodyOf("a.txt")
        Dim source As New ExchangeDriveSendSource("a.txt", "text/plain", Function() As Stream
                                                                             Return New MemoryStream(body)
                                                                         End Function)
        other.SendFilesAsync({source}, "CCCCCCCCCCCCCCCCCCCCCA", "Third PC", Clear).GetAwaiter().GetResult()

        Dim result As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))

        Assert.Equal(0, result.Files)
        Assert.False(_tree.Exists("dst\a.txt"))
        Assert.Single(PacketsFor("CCCCCCCCCCCCCCCCCCCCCA"))
    End Sub

    <Fact>
    Public Sub A_name_taken_at_the_destination_keeps_its_packet_and_nothing_is_overwritten()
        SendFromOther("a.txt", "b.txt")
        _tree.Write("dst\b.txt", "mine")

        Dim result As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.Equal(1, result.Files)
        Assert.Equal(1, result.Skipped)
        Assert.Equal("body of a.txt", Read("dst\a.txt"))
        Assert.Equal("mine", Read("dst\b.txt"))
        Assert.Equal({"a.txt", "b.txt"}, Assert.Single(PacketsFor(ThisPc)))
    End Sub

    <Fact>
    Public Sub Keep_both_lets_the_packet_go()
        SendFromOther("a.txt")
        _tree.Write("dst\a.txt", "mine")
        Dim op As ScheduledOperation = FromDrive(ScheduledAction.Copy)
        op.Conflict = ScheduledConflict.KeepBoth

        Dim result As ScheduledRunResult = RunOf(op)

        Assert.Equal(1, result.Files)
        Assert.Equal("mine", Read("dst\a.txt"))
        Assert.Equal("body of a.txt", Read("dst\a (2).txt"))
        Assert.Empty(PacketsFor(ThisPc))
    End Sub

    <Fact>
    Public Sub A_file_the_conditions_filter_out_keeps_its_packet()
        SendFromOther("a.txt", "b.jpg")
        Dim op As ScheduledOperation = FromDrive(ScheduledAction.Copy)
        op.Conditions.NameMask = "*.txt"

        Dim result As ScheduledRunResult = RunOf(op)

        Assert.Equal(1, result.Files)
        Assert.False(_tree.Exists("dst\b.jpg"))
        Assert.Single(PacketsFor(ThisPc))
    End Sub

    <Fact>
    Public Sub A_packet_that_did_not_arrive_whole_stays_on_Drive_and_the_run_counts_it()
        SendFromOther("good.txt")
        SendFromOther("spoiled.txt")
        _drive.Find("spoiled.txt").Content = Encoding.UTF8.GetBytes("BODY OF SPOILED.TXT")

        Dim result As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))

        Assert.Equal(ScheduledRunOutcome.Failed, result.Outcome)
        Assert.Equal(1, result.Files)
        Assert.Equal(1, result.Failed)
        Assert.Contains("mismatch", result.FirstError)
        Assert.Contains("spoiled.txt", result.FirstError)
        Assert.Equal("body of good.txt", Read("dst\good.txt"))
        Assert.False(_tree.Exists("dst\spoiled.txt"))
        Assert.Equal({"spoiled.txt"}, Assert.Single(PacketsFor(ThisPc)))
        Assert.Empty(LeftInTemp())
    End Sub

    <Fact>
    Public Sub A_cancelled_run_confirms_what_left_and_keeps_the_rest_for_the_next_run()
        SendFromOther("a.txt")
        SendFromOther("b.txt")
        Using cancel As New CancellationTokenSource()
            Dim services As ScheduledServices = Rig(Sub(name As String)
                                                        If name = "b.txt" Then cancel.Cancel()
                                                    End Sub)

            Dim result As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy), services, cancel.Token)

            Assert.Equal(ScheduledRunOutcome.Cancelled, result.Outcome)
            Assert.True(_tree.Exists("dst\a.txt"))
            Assert.False(_tree.Exists("dst\b.txt"))
            Assert.Equal({"b.txt"}, Assert.Single(PacketsFor(ThisPc)))
        End Using
        Assert.Empty(LeftInTemp())

        Dim next_ As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))

        Assert.Equal(ScheduledRunOutcome.Ok, next_.Outcome)
        Assert.Equal("body of b.txt", Read("dst\b.txt"))
        Assert.Empty(PacketsFor(ThisPc))
    End Sub

    <Fact>
    Public Sub A_destination_that_cannot_be_written_leaves_every_packet_on_Drive()
        SendFromOther("a.txt")
        Dim blocker As String = _tree.Write("blocker", "a file where the folder should be")
        Dim op As ScheduledOperation = FromDrive(ScheduledAction.Copy)
        op.Destination.Path = Path.Combine(blocker, "below")

        Dim result As ScheduledRunResult = RunOf(op)

        Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
        Assert.StartsWith("destination-", result.Detail)
        Assert.Single(PacketsFor(ThisPc))
        Assert.Empty(LeftInTemp())

        ' Nothing was marked handled either: the folder coming back delivers the packet.
        Dim retried As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))
        Assert.Equal(1, retried.Files)
    End Sub

    <Fact>
    Public Sub Drive_not_answering_on_a_pick_up_skips_and_a_lost_sign_in_is_the_wall()
        _drive.FailWhen(Function(r) True, 100000, 503)
        Dim skipped As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy))
        Assert.Equal(ScheduledRunOutcome.Skipped, skipped.Outcome)
        Assert.StartsWith("source-drive-unavailable|", skipped.Detail)
        Assert.Equal(ScheduledHalt.None, skipped.Halt)

        Using signedOut As New FakeDriveHandler()
            Using http As New HttpClient(signedOut, False)
                signedOut.FailWhen(Function(r) True, 100000, 401)
                Dim client As New ExchangeDriveClient(http, New FakeDriveTokens(), Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
                Dim queue As New ExchangeDriveQueue(client, ThisPc, "This PC", New ExchangeDriveMemoryHandledStore())
                Dim services As ScheduledServices = Rig()
                Dim usual As Func(Of ScheduledLocation, ScheduledSideOpen) = services.OpenSide
                services.OpenSide = Function(location As ScheduledLocation) If(location.Kind = ScheduledLocationKind.Drive,
                    ScheduledSideOpen.Opened(New DriveScheduledSide(location, queue, client, Nothing, Nothing)), usual.Invoke(location))

                Dim walled As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Copy), services)

                Assert.Equal(ScheduledRunOutcome.Wall, walled.Outcome)
                Assert.StartsWith("source-signed-out|", walled.Detail)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub An_archive_of_a_pick_up_lets_the_packet_go_after_the_read_back()
        SendFromOther("a.txt", "b.txt")

        Dim result As ScheduledRunResult = RunOf(FromDrive(ScheduledAction.Archive))

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.Equal(2, result.Files)
        Assert.True(File.Exists(result.ArchivePath))
        Assert.Empty(PacketsFor(ThisPc))
        Assert.Empty(LeftInTemp())
    End Sub

    ' --- the changes of a resource of another device ------------------------------------------------

    ''' <summary>A device that answers a `changes` request with one final reply.</summary>
    Private NotInheritable Class ChangesPort
        Implements IDriveRequestPort

        Friend ReadOnly Requests As New List(Of String)()
        Friend ReadOnly Targets As New List(Of String)()
        Friend ReadOnly Withdrawn As New List(Of String)()
        Friend ReadOnly Deleted As New List(Of String)()
        Friend Names As String() = {"a.txt"}
        Friend Reason As String = ""
        Friend Silent As Boolean
        Friend Spoiled As Boolean
        Friend SendError As String = ""
        Friend Failure As ExchangeDriveException

        Public Function SendRequestAsync(requestJson As String, targetDeviceId As String, targetDeviceName As String, ct As CancellationToken) As Task(Of DriveRequestSent) Implements IDriveRequestPort.SendRequestAsync
            If Failure IsNot Nothing Then Throw Failure
            Requests.Add(requestJson)
            Targets.Add(targetDeviceId & "|" & targetDeviceName)
            Return Task.FromResult(If(SendError.Length > 0, New DriveRequestSent("", SendError), New DriveRequestSent("req-1", "")))
        End Function

        Public Function AwaitRepliesAsync(requestPacketId As String, seen As ISet(Of String), ct As CancellationToken) As Task(Of IReadOnlyList(Of DriveReplyView)) Implements IDriveRequestPort.AwaitRepliesAsync
            If Silent Then Return Task.FromResult(Of IReadOnlyList(Of DriveReplyView))(Array.Empty(Of DriveReplyView)())
            Dim outcome As String = If(Reason.Length > 0, ExchangeDriveRequests.OutcomeRefused, ExchangeDriveRequests.OutcomeOk)
            Dim replies As DriveReplyView() = {New DriveReplyView("reply-1", outcome, Reason, True, Nothing, AsOfText)}
            Return Task.FromResult(Of IReadOnlyList(Of DriveReplyView))(replies)
        End Function

        Public Function ReadListingAsync(reply As DriveReplyView, ct As CancellationToken) As Task(Of Byte()) Implements IDriveRequestPort.ReadListingAsync
            Return Task.FromResult(Of Byte())(Nothing)
        End Function

        Public Function ReceiveAsync(reply As DriveReplyView, folder As String, ct As CancellationToken) As Task(Of IReadOnlyList(Of String)) Implements IDriveRequestPort.ReceiveAsync
            If Spoiled Then Return Task.FromResult(Of IReadOnlyList(Of String))(Nothing)
            Dim saved As New List(Of String)()
            For Each name As String In Names
                Dim place As String = Path.Combine(folder, name)
                File.WriteAllBytes(place, BodyOf(name))
                saved.Add(place)
            Next
            Return Task.FromResult(Of IReadOnlyList(Of String))(saved)
        End Function

        Public Function DeleteAsync(reply As DriveReplyView, ct As CancellationToken) As Task Implements IDriveRequestPort.DeleteAsync
            Deleted.Add(reply.PacketId)
            Return Task.CompletedTask
        End Function

        Public Function WithdrawAsync(requestPacketId As String, ct As CancellationToken) As Task Implements IDriveRequestPort.WithdrawAsync
            Withdrawn.Add(requestPacketId)
            Return Task.CompletedTask
        End Function
    End Class

    Private Function Mark(Optional resource As String = ResourceId) As String
        Return OtherPc & "/" & resource & "/|" & AsOfText
    End Function

    <Fact>
    Public Sub A_first_run_asks_for_everything_and_a_clean_run_moves_the_mark()
        Dim port As New ChangesPort() With {.Names = {"a.txt", "b.txt"}}
        _port = port

        Dim result As ScheduledRunResult = RunOf(FromDevice())

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.Equal(2, result.Files)
        Assert.Equal("body of a.txt", Read("dst\a.txt"))
        Assert.DoesNotContain("modifiedAfter", Assert.Single(port.Requests))
        Assert.Contains(ResourceId, port.Requests(0))
        Assert.Equal(OtherPc & "|Office PC", Assert.Single(port.Targets))
        Assert.Equal(Mark(), result.DriveMark)
        Assert.Empty(LeftInTemp())
    End Sub

    <Fact>
    Public Sub The_next_run_asks_what_changed_after_the_mark()
        Dim port As New ChangesPort()
        _port = port
        Dim op As ScheduledOperation = FromDevice()
        op.Record.DriveMark = Mark()

        Assert.Equal(ScheduledRunOutcome.Ok, RunOf(op).Outcome)

        Assert.Contains("""modifiedAfter"":""2026-10-13T11:00:00", Assert.Single(port.Requests))
    End Sub

    <Fact>
    Public Sub A_re_pointed_source_starts_over()
        Dim port As New ChangesPort()
        _port = port
        Dim op As ScheduledOperation = FromDevice("res_fedcba9876543210")
        op.Record.DriveMark = Mark()

        Dim result As ScheduledRunResult = RunOf(op)

        Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
        Assert.DoesNotContain("modifiedAfter", Assert.Single(port.Requests))
        Assert.Equal(Mark("res_fedcba9876543210"), result.DriveMark)
    End Sub

    ''' <summary>Every file meets a refusal of the destination.</summary>
    Private NotInheritable Class RefusingTransfer
        Implements IScheduledTransfer

        Public Function CopyFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide, targetRelative As String, overwrite As Boolean, ct As CancellationToken) As Long Implements IScheduledTransfer.CopyFile
            Throw New ScheduledTransferException("denied")
        End Function
        Public Function MoveFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide, targetRelative As String, overwrite As Boolean, ct As CancellationToken) As MoveOutcome Implements IScheduledTransfer.MoveFile
            Throw New ScheduledTransferException("denied")
        End Function
        Public Function DeleteFile(side As IScheduledSide, entry As SideEntry, ct As CancellationToken) As String Implements IScheduledTransfer.DeleteFile
            Throw New ScheduledTransferException("denied")
        End Function
    End Class

    <Fact>
    Public Sub A_failed_file_keeps_the_mark_where_it_was()
        _port = New ChangesPort()
        Dim services As ScheduledServices = Rig()
        services.TransferFor = Function(a As IScheduledSide, b As IScheduledSide) New RefusingTransfer()

        Dim result As ScheduledRunResult = RunOf(FromDevice(), services)

        Assert.Equal(ScheduledRunOutcome.Failed, result.Outcome)
        Assert.Equal(1, result.Failed)
        Assert.Equal("", result.DriveMark)
    End Sub

    <Fact>
    Public Sub A_cancelled_run_keeps_the_mark_where_it_was()
        _port = New ChangesPort()
        Using cancel As New CancellationTokenSource()
            Dim services As ScheduledServices = Rig(Sub(name As String) cancel.Cancel())

            Dim result As ScheduledRunResult = RunOf(FromDevice(), services, cancel.Token)

            Assert.Equal(ScheduledRunOutcome.Cancelled, result.Outcome)
            Assert.Equal("", result.DriveMark)
        End Using
    End Sub

    <Fact>
    Public Sub A_destination_that_is_gone_keeps_the_mark_where_it_was()
        _port = New ChangesPort()
        Dim blocker As String = _tree.Write("blocker", "x")
        Dim op As ScheduledOperation = FromDevice()
        op.Destination.Path = Path.Combine(blocker, "below")

        Dim result As ScheduledRunResult = RunOf(op)

        Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
        Assert.Equal("", result.DriveMark)
    End Sub

    <Fact>
    Public Sub A_device_that_does_not_answer_skips_the_run_and_a_refusal_is_the_wall()
        Dim silent As New ChangesPort() With {.Silent = True}
        _port = silent
        Dim noAnswer As ScheduledRunResult = RunOf(FromDevice())
        Assert.Equal(ScheduledRunOutcome.Skipped, noAnswer.Outcome)
        Assert.StartsWith("source-offline|", noAnswer.Detail)
        Assert.Equal("", noAnswer.DriveMark)

        _port = New ChangesPort() With {.Reason = ExchangeDriveRequests.ReasonPinRequired}
        Dim pin As ScheduledRunResult = RunOf(FromDevice())
        Assert.Equal(ScheduledRunOutcome.Wall, pin.Outcome)
        Assert.StartsWith("source-pin-locked|", pin.Detail)

        _port = New ChangesPort() With {.Reason = ExchangeDriveRequests.ReasonDriveFull}
        Dim full As ScheduledRunResult = RunOf(FromDevice())
        Assert.Equal(ScheduledRunOutcome.Wall, full.Outcome)
        Assert.StartsWith("source-drive-full|", full.Detail)

        _port = New ChangesPort() With {.Reason = ExchangeDriveRequests.ReasonNotPermitted}
        Dim refused As ScheduledRunResult = RunOf(FromDevice())
        Assert.Equal(ScheduledRunOutcome.Wall, refused.Outcome)
        Assert.StartsWith("source-unreachable|", refused.Detail)
    End Sub

    <Fact>
    Public Sub A_reply_that_did_not_arrive_whole_skips_the_run_and_delivers_nothing()
        Dim port As New ChangesPort() With {.Spoiled = True}
        _port = port

        Dim result As ScheduledRunResult = RunOf(FromDevice())

        Assert.Equal(ScheduledRunOutcome.Skipped, result.Outcome)
        Assert.False(_tree.Exists("dst\a.txt"))
        Assert.Equal({"reply-1"}, port.Deleted.ToArray())
        Assert.Empty(LeftInTemp())
    End Sub

    <Fact>
    Public Sub The_engine_keeps_the_mark_and_hands_it_to_the_next_run()
        Dim port As New ChangesPort()
        _port = port
        Using hive As New ScratchHive()
            Dim folder As String = _tree.Dir("state")
            Dim store As New ScheduledStore(folder)
            Dim op As ScheduledOperation = FromDevice()
            op.Id = "aaa"
            op.AnchorMinutes = 3 * 60
            op.IntervalMinutes = 24 * 60
            op.StartDate = Noon.Date
            op.CreatedAt = Noon.AddDays(-10)
            store.SaveDefinition(op)
            store.SaveRunState(op.Id, op.Record, op.Halt, op.HaltDetail)
            Using engine As New ScheduledEngine(store, New ScheduledJournal(folder), Rig(), New RecordingTimers()) With {.CatchUpDelay = TimeSpan.Zero}
                engine.Start()
                engine.Paused = True

                Assert.Equal(TriggerOutcome.Started, engine.RunNow("aaa"))
                engine.WhenIdleAsync().GetAwaiter().GetResult()
                Assert.Equal(Mark(), engine.TryGet("aaa").Record.DriveMark)

                Assert.Equal(TriggerOutcome.Started, engine.RunNow("aaa"))
                engine.WhenIdleAsync().GetAwaiter().GetResult()
            End Using
            Assert.Equal(2, port.Requests.Count)
            Assert.DoesNotContain("modifiedAfter", port.Requests(0))
            Assert.Contains("modifiedAfter", port.Requests(1))
            Assert.Equal(Mark(), New ScheduledStore(folder).LoadAll().Single().Record.DriveMark)
        End Using
    End Sub

    ' --- the definition ---------------------------------------------------------------------------

    <Fact>
    Public Sub Google_Drive_on_both_sides_a_delete_and_an_age_bound_are_refused_before_any_request()
        Dim both As ScheduledOperation = FromDrive(ScheduledAction.Copy)
        both.Destination.Kind = ScheduledLocationKind.Drive
        Dim deleting As ScheduledOperation = FromDrive(ScheduledAction.Delete)
        Dim aged As ScheduledOperation = FromDrive(ScheduledAction.Copy)
        aged.Conditions.MinAgeHours = 24
        Dim young As ScheduledOperation = FromDrive(ScheduledAction.Copy)
        young.Conditions.MaxAgeHours = 24

        Assert.StartsWith("issue-DriveBothSides|", RunOf(both).Detail)
        Assert.StartsWith("issue-DriveDeleteUnsupported|", RunOf(deleting).Detail)
        Assert.StartsWith("issue-DriveAgeUnsupported|", RunOf(aged).Detail)
        Assert.StartsWith("issue-DriveAgeUnsupported|", RunOf(young).Detail)
        Assert.Equal(ScheduledRunOutcome.Refused, RunOf(both).Outcome)
        Assert.Equal(0, _drive.RequestCount)
    End Sub

    <Fact>
    Public Sub A_preview_of_a_Drive_source_makes_no_request_and_takes_nothing()
        SendFromOther("a.txt")
        Dim before As Integer = _drive.RequestCount

        Dim view As ScheduledPreview = New ScheduledExecutor(Rig()).Preview(FromDrive(ScheduledAction.Copy), Clear)

        Assert.True(view.DriveSource)
        Assert.Equal(before, _drive.RequestCount)
        Assert.Empty(LeftInTemp())
    End Sub

    <Fact>
    Public Sub The_picker_offers_what_the_directory_lists_and_no_protected_resource()
        Dim office As New ExchangeDeviceRecord("{}", OtherPc, "Office PC", "fms", "1", "windows", New List(Of String)(), "online", "")
        Dim tablet As New ExchangeDeviceRecord("{}", "CCCCCCCCCCCCCCCCCCCCCA", "Tablet", "fms", "1", "android", New List(Of String)(), "offline", "")
        Dim open As New ExchangeResourceRecord("{}", ResourceId, OtherPc, "folder", "Photos", "online", "")
        Dim locked As New ExchangeResourceRecord("{}", "res_fedcba9876543210", OtherPc, "folder", "Private", "online", "", isProtected:=True, protectionScheme:="pin-v1")
        Dim orphan As New ExchangeResourceRecord("{}", "res_aaaaaaaaaaaaaaaa", "DDDDDDDDDDDDDDDDDDDDDA", "folder", "Lost", "online", "")
        Dim listed As New ExchangeDirectorySnapshot(1, {office, tablet}, {open, locked, orphan}, New List(Of ExchangeBroadcastRecord)())

        Dim sources As List(Of ScheduledDriveChoice) = ScheduledDriveChoice.ForSource(listed)
        Assert.Equal(2, sources.Count)
        Assert.Equal("", sources(0).DeviceId)
        Assert.Equal("", sources(0).Label)
        Assert.Equal(OtherPc, sources(1).DeviceId)
        Assert.Equal(ResourceId, sources(1).ResourceId)
        Assert.Equal("Office PC: Photos", sources(1).Label)

        Dim targets As List(Of ScheduledDriveChoice) = ScheduledDriveChoice.ForDestination(listed)
        Assert.Equal({"", "Office PC", "Tablet"}, targets.Select(Function(c As ScheduledDriveChoice) c.Label).ToArray())
        Assert.Equal("", targets(1).ResourceId)
    End Sub

    <Fact>
    Public Sub A_Drive_location_and_the_mark_survive_the_store()
        Dim op As ScheduledOperation = FromDevice()
        op.Record.DriveMark = Mark()

        Dim read As ScheduledOperation = ScheduledJson.Deserialize(ScheduledJson.Serialize(op))
        Dim record As ScheduledRunRecord = ScheduledJson.RecordFromDto(ScheduledJson.RecordToDto(op.Record))

        Assert.Equal(ScheduledLocationKind.Drive, read.Source.Kind)
        Assert.Equal(OtherPc, read.Source.ResourceId)
        Assert.Equal(ResourceId, read.Source.DriveResourceId)
        Assert.Equal("Office PC", read.Source.DriveDeviceName)
        Assert.Equal(Mark(), record.DriveMark)
        Assert.Contains("Google Drive", read.Source.Describe())
        Assert.StartsWith("Google Drive", ToDrive(ScheduledAction.Copy).Destination.Describe())
    End Sub

End Class
