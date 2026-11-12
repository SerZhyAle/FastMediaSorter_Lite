Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 269 P3.1 - the Share Manager answers REQUEST packets for a folder it lists on Drive (DEVICE-EXCHANGE section 24,
''' items CO, CR, CS), on the in-memory Drive: `list` answers `listing.json`, `fetch` answers the files whole and splits an
''' answer above `packetBytes` with `final` on the last reply, `changes` answers only what changed after `modifiedAfter`, a path
''' that leaves the root is `not-permitted`, a folder not listed is `unavailable`, a locked root is `pin-required` and sends no
''' file, a file above `packetBytes` is `too-large`, and the REQUEST is deleted once its last reply landed.
''' </summary>
Public Class DriveRequestProducerTests
    Implements IDisposable

    Private Const IdA As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdB As String = "BBBBBBBBBBBBBBBBBBBBBA"
    Private Const ResourceId As String = "res_0123456789abcdef"

    Private ReadOnly _drive As New FakeDriveHandler()
    Private ReadOnly _http As HttpClient
    Private ReadOnly _folder As String
    Private ReadOnly _consumerClient As ExchangeDriveClient
    Private ReadOnly _producerClient As ExchangeDriveClient
    Private ReadOnly _consumer As ExchangeDriveQueue
    Private ReadOnly _producerQueue As ExchangeDriveQueue
    Private ReadOnly _now As New DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)
    Private ReadOnly _log As New List(Of String)

    Public Sub New()
        _http = New HttpClient(_drive, False)
        _folder = Directory.CreateTempSubdirectory("fmsdrvreq").FullName
        _consumerClient = NewClient()
        _producerClient = NewClient()
        _consumer = New ExchangeDriveQueue(_consumerClient, IdA, "Laptop", New ExchangeDriveMemoryHandledStore())
        _producerQueue = New ExchangeDriveQueue(_producerClient, IdB, "Office PC", New ExchangeDriveMemoryHandledStore())
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_folder, True)
        Catch ex As IOException
        End Try
        _http.Dispose()
    End Sub

    Private Function NewClient() As ExchangeDriveClient
        Return New ExchangeDriveClient(_http, New FakeDriveTokens(), Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
    End Function

    Private Function Producer(Optional locked As Boolean = False, Optional packetBytes As Long = 0, Optional served As Boolean = True) As DriveRequestProducer
        Dim root As New DriveServedRoot(ResourceId, _folder, locked)
        Return New DriveRequestProducer(_producerQueue, Function(id As String) If(served AndAlso id = ResourceId, root, Nothing), Function() _now,
                                        If(packetBytes > 0, packetBytes, ExchangeDrivePackets.DefaultPacketBytes), AddressOf _log.Add)
    End Function

    Private Function WriteFile(relative As String, content As Byte(), Optional modified As DateTime? = Nothing) As String
        Dim full As String = Path.Combine(_folder, relative.Replace("/"c, Path.DirectorySeparatorChar))
        Directory.CreateDirectory(Path.GetDirectoryName(full))
        File.WriteAllBytes(full, content)
        If modified.HasValue Then File.SetLastWriteTimeUtc(full, modified.Value)
        Return full
    End Function

    Private Async Function PollAsync(client As ExchangeDriveClient, queue As ExchangeDriveQueue) As Task(Of ExchangeDriveQueuePoll)
        Dim listing As ExchangeDriveListing = Await client.ListAllAsync(CancellationToken.None)
        Return Await queue.PollAsync(listing, CancellationToken.None)
    End Function

    ''' <summary>The consumer asks, the producer answers one poll, the consumer reads the replies (oldest first).</summary>
    Private Async Function AskAsync(requestJson As String, worker As DriveRequestProducer) As Task(Of (RequestId As String, Answered As Integer, Replies As IReadOnlyList(Of ExchangeDrivePacket), RequestLeft As Boolean))
        Dim sent As ExchangeDriveSendResult = Await _consumer.SendRequestAsync(requestJson, IdB, "Office PC", CancellationToken.None)
        Assert.True(sent.Succeeded)
        Dim answered As Integer = Await worker.AnswerAsync(Await PollAsync(_producerClient, _producerQueue), CancellationToken.None)
        Dim seen As ExchangeDriveQueuePoll = Await PollAsync(_consumerClient, _consumer)
        Dim left As Boolean = seen.Packets.Any(Function(p) p.PacketId = sent.PacketId)
        Return (sent.PacketId, answered, _consumer.RepliesTo(seen, sent.PacketId), left)
    End Function

    Private Shared Function Json(action As String, Optional path As String = Nothing, Optional paths As String() = Nothing, Optional resource As String = ResourceId) As String
        Return ExchangeDriveRequests.Build(New ExchangeDriveRequest() With {
            .Action = If(action = "list", ExchangeDriveRequestAction.List, If(action = "fetch", ExchangeDriveRequestAction.Fetch, ExchangeDriveRequestAction.Changes)),
            .ResourceId = resource, .Path = If(path, ""), .Paths = If(paths, Array.Empty(Of String)())})
    End Function

    <Fact>
    Public Async Function A_list_is_answered_with_a_listing_and_the_request_is_deleted() As Task
        WriteFile("Camera/a.jpg", New Byte() {1, 2, 3})
        Directory.CreateDirectory(Path.Combine(_folder, "Camera", "2026"))
        Using worker As DriveRequestProducer = Producer()
            Dim result = Await AskAsync(Json("list", "Camera"), worker)
            Assert.Equal(1, result.Answered)
            Assert.False(result.RequestLeft)
            Dim reply As ExchangeDrivePacket = Assert.Single(result.Replies)
            Assert.Equal("ok", reply.Manifest.Outcome)
            Assert.True(reply.Manifest.Final)
            Assert.Equal("2026-10-30T12:00:00Z", reply.Manifest.AsOf)
            Dim body As Byte() = Await _consumer.ReadPayloadAsync(reply, ExchangeDriveRequests.ListingFileName, ExchangeDriveRequests.MaxListingBytes, CancellationToken.None)
            Dim listing As ExchangeDriveListingReply = Nothing
            Assert.True(ExchangeDriveRequests.TryParseListing(body, listing))
            Assert.Equal(New String() {"2026", "a.jpg"}, listing.Entries.Select(Function(e) e.Name).ToArray())
            Assert.True(listing.Entries(0).IsDirectory)
            Assert.Equal(3L, listing.Entries(1).Size)
        End Using
    End Function

    <Fact>
    Public Async Function A_fetch_above_packet_bytes_is_split_into_replies_that_arrive_whole() As Task
        Dim first As Byte() = Encoding.UTF8.GetBytes(New String("x"c, 600))
        Dim second As Byte() = Encoding.UTF8.GetBytes(New String("y"c, 600))
        WriteFile("one/same.txt", first)
        WriteFile("two/same.txt", second)
        Using worker As DriveRequestProducer = Producer(packetBytes:=1000)
            Dim result = Await AskAsync(Json("fetch", paths:={"one/same.txt", "two/same.txt"}), worker)
            Assert.Equal(1, result.Answered)
            Assert.False(result.RequestLeft)
            Assert.Equal(2, result.Replies.Count)
            Assert.Single(result.Replies.Where(Function(r) r.Manifest.Final))
            Dim got As New List(Of Byte())
            For Each reply As ExchangeDrivePacket In result.Replies
                Assert.Equal("ok", reply.Manifest.Outcome)
                ' read whole and checked against the manifest's size and SHA-256 (Nothing otherwise)
                got.Add(Await _consumer.ReadPayloadAsync(reply, "same.txt", 4096, CancellationToken.None))
            Next
            Assert.Empty(_log)
            Assert.All(got, Sub(bytes) Assert.NotNull(bytes))
            Assert.Contains(got, Function(bytes) bytes.SequenceEqual(first))
            Assert.Contains(got, Function(bytes) bytes.SequenceEqual(second))
        End Using
    End Function

    <Fact>
    Public Async Function Changes_answers_only_files_modified_after_the_previous_as_of() As Task
        WriteFile("Camera/old.jpg", New Byte() {1}, New DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc))
        WriteFile("Camera/Sub/new.jpg", New Byte() {2, 2}, New DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc))
        Dim json As String = ExchangeDriveRequests.Build(New ExchangeDriveRequest() With {
            .Action = ExchangeDriveRequestAction.Changes, .ResourceId = ResourceId, .Path = "Camera",
            .ModifiedAfter = New DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero), .HasModifiedAfter = True})
        Using worker As DriveRequestProducer = Producer()
            Dim result = Await AskAsync(json, worker)
            Dim reply As ExchangeDrivePacket = Assert.Single(result.Replies)
            Assert.True(reply.Manifest.Final)
            Assert.Equal("2026-10-30T12:00:00Z", reply.Manifest.AsOf)
            Assert.Equal(New String() {"new.jpg"}, reply.Manifest.FileNames.ToArray())
        End Using
    End Function

    <Fact>
    Public Async Function A_path_that_leaves_the_root_is_not_permitted_and_sends_nothing() As Task
        WriteFile("inside.txt", New Byte() {1})
        Using worker As DriveRequestProducer = Producer()
            Dim result = Await AskAsync("{""action"":""fetch"",""resourceId"":""" & ResourceId & """,""paths"":[""../outside.txt""]}", worker)
            Dim reply As ExchangeDrivePacket = Assert.Single(result.Replies)
            Assert.Equal("refused", reply.Manifest.Outcome)
            Assert.Equal("not-permitted", reply.Manifest.Reason)
            Assert.True(reply.Manifest.Final)
            Assert.Empty(reply.Manifest.FileNames)
            Assert.False(result.RequestLeft)
        End Using
    End Function

    <Fact>
    Public Async Function A_folder_that_is_not_listed_is_unavailable_and_a_missing_path_too() As Task
        Using worker As DriveRequestProducer = Producer(served:=False)
            Dim result = Await AskAsync(Json("list", ""), worker)
            Assert.Equal("unavailable", Assert.Single(result.Replies).Manifest.Reason)
        End Using
        Using worker As DriveRequestProducer = Producer()
            Dim result = Await AskAsync(Json("list", "NoSuchFolder"), worker)
            Assert.Equal("unavailable", Assert.Single(result.Replies).Manifest.Reason)
        End Using
    End Function

    <Fact>
    Public Async Function A_locked_root_is_pin_required_and_never_sends_a_file() As Task
        WriteFile("secret.jpg", New Byte() {9})
        Using worker As DriveRequestProducer = Producer(locked:=True)
            Dim result = Await AskAsync(Json("fetch", paths:={"secret.jpg"}), worker)
            Dim reply As ExchangeDrivePacket = Assert.Single(result.Replies)
            Assert.Equal("pin-required", reply.Manifest.Reason)
            Assert.Empty(reply.Manifest.FileNames)
        End Using
    End Function

    <Fact>
    Public Async Function A_file_above_packet_bytes_is_too_large() As Task
        WriteFile("big.bin", New Byte(2047) {})
        Using worker As DriveRequestProducer = Producer(packetBytes:=1024)
            Dim result = Await AskAsync(Json("fetch", paths:={"big.bin"}), worker)
            Assert.Equal("too-large", Assert.Single(result.Replies).Manifest.Reason)
        End Using
    End Function

    <Fact>
    Public Async Function A_request_is_answered_once_and_an_unknown_action_is_left_alone() As Task
        WriteFile("a.txt", New Byte() {1})
        Using worker As DriveRequestProducer = Producer()
            Dim result = Await AskAsync("{""action"":""thumbnails"",""resourceId"":""" & ResourceId & """}", worker)
            Assert.Equal(0, result.Answered)
            Assert.Empty(result.Replies)
            Assert.True(result.RequestLeft)
            ' the same poll again: still nothing, and no second reply to anything
            Assert.Equal(0, Await worker.AnswerAsync(Await PollAsync(_producerClient, _producerQueue), CancellationToken.None))
        End Using
    End Function

    ''' <summary>Ticket 269 P3.2: the agent's proxy (DriveRequestConsumer over the real queue) asks and the producer answers while the
    ''' proxy waits between its polls - a listing comes back, then a fetched file lands whole in the cache folder, and neither the
    ''' REQUESTs nor the REPLYs stay on Drive.</summary>
    <Fact>
    Public Async Function The_agent_proxy_lists_and_fetches_through_the_producer() As Task
        WriteFile("Camera/a.jpg", New Byte() {7, 8, 9})
        Using worker As DriveRequestProducer = Producer()
            ' the requester's wait between polls is where the other PC answers
            Dim answering As New ExchangeDriveQueue(_consumerClient, IdA, "Laptop", New ExchangeDriveMemoryHandledStore(),
                delay:=Async Function(span As TimeSpan, token As CancellationToken)
                           Await worker.AnswerAsync(Await PollAsync(_producerClient, _producerQueue), token)
                       End Function)
            Dim port As New DriveQueueRequestPort(answering)

            Dim listed As DriveRequestOutcome = Await DriveRequestConsumer.RunAsync(port, New ExchangeDriveRequest() With {
                .Action = ExchangeDriveRequestAction.List, .ResourceId = ResourceId, .Path = "Camera"}, IdB, "Office PC", "", CancellationToken.None)
            Assert.True(listed.Succeeded, listed.Error)
            Dim listing As ExchangeDriveListingReply = Nothing
            Assert.True(ExchangeDriveRequests.TryParseListing(listed.ListingBody, listing))
            Assert.Equal("a.jpg", Assert.Single(listing.Entries).Name)

            Dim cache As String = Directory.CreateTempSubdirectory("fmsdrvcache").FullName
            Try
                Dim fetched As DriveRequestOutcome = Await DriveRequestConsumer.RunAsync(port, New ExchangeDriveRequest() With {
                    .Action = ExchangeDriveRequestAction.Fetch, .ResourceId = ResourceId, .Paths = {"Camera/a.jpg"}}, IdB, "Office PC", cache, CancellationToken.None)
                Assert.True(fetched.Succeeded, fetched.Error)
                Dim file As ExchangeAgentDriveFile = Assert.Single(fetched.Files)
                Assert.Equal("Camera/a.jpg", file.Path)
                Assert.Equal(New Byte() {7, 8, 9}, IO.File.ReadAllBytes(file.SavedPath))
            Finally
                Directory.Delete(cache, True)
            End Try

            Dim left As ExchangeDriveQueuePoll = Await PollAsync(_consumerClient, _consumer)
            Assert.Empty(left.Packets)
        End Using
    End Function

End Class
