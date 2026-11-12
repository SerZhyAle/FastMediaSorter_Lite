#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports Xunit

''' <summary>
''' Ticket 269 P3 - the `request` member of a REQUEST and the `listing.json` of its REPLY (DEVICE-EXCHANGE section 24, item
''' CO): the catalog's request vectors read back, a path that is not relative or names a stream is refused, `fetch` is bounded
''' at 100 paths, an `asOf` is floored, and an answer is split by `packetBytes` with unique base names per reply.
''' </summary>
Public Class ExchangeDriveRequestsTests

    <Fact>
    Public Sub The_catalog_request_vectors_read_back()
        Dim list As ExchangeDriveRequest = Nothing
        Assert.True(ExchangeDriveRequests.TryParse("{ ""action"": ""list"", ""resourceId"": ""res_0123456789abcdef"", ""path"": ""Camera/2026"" }", list))
        Assert.Equal(ExchangeDriveRequestAction.List, list.Action)
        Assert.Equal("res_0123456789abcdef", list.ResourceId)
        Assert.Equal("Camera/2026", list.Path)

        Dim fetch As ExchangeDriveRequest = Nothing
        Assert.True(ExchangeDriveRequests.TryParse("{ ""action"": ""fetch"", ""resourceId"": ""res_0123456789abcdef"", ""paths"": [""Camera/a.jpg"", ""Camera/b.jpg""] }", fetch))
        Assert.Equal(ExchangeDriveRequestAction.Fetch, fetch.Action)
        Assert.Equal(New String() {"Camera/a.jpg", "Camera/b.jpg"}, fetch.Paths.ToArray())

        Dim changes As ExchangeDriveRequest = Nothing
        Assert.True(ExchangeDriveRequests.TryParse("{ ""action"": ""changes"", ""resourceId"": ""res_0123456789abcdef"", ""path"": ""Camera"", ""modifiedAfter"": ""2026-10-14T10:00:00Z"" }", changes))
        Assert.Equal(ExchangeDriveRequestAction.Changes, changes.Action)
        Assert.True(changes.HasModifiedAfter)
        Assert.Equal(New DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero), changes.ModifiedAfter)
    End Sub

    <Fact>
    Public Sub An_unknown_action_is_read_as_unknown_and_a_request_without_a_resource_is_not_read()
        Dim request As ExchangeDriveRequest = Nothing
        Assert.True(ExchangeDriveRequests.TryParse("{""action"":""thumbnails"",""resourceId"":""r1""}", request))
        Assert.Equal(ExchangeDriveRequestAction.Unknown, request.Action)
        Assert.False(ExchangeDriveRequests.TryParse("{""action"":""list"",""path"":""""}", request))
        Assert.False(ExchangeDriveRequests.TryParse("[1,2]", request))
        Assert.False(ExchangeDriveRequests.TryParse("not json", request))
        Assert.False(ExchangeDriveRequests.TryParse("{""action"":""changes"",""resourceId"":""r1"",""modifiedAfter"":""yesterday""}", request))
    End Sub

    <Fact>
    Public Sub Built_requests_read_back_and_a_bad_one_is_not_built()
        Dim built As String = ExchangeDriveRequests.Build(New ExchangeDriveRequest() With {
            .Action = ExchangeDriveRequestAction.Changes, .ResourceId = "r1", .Path = "Camera",
            .ModifiedAfter = New DateTimeOffset(2026, 10, 14, 10, 0, 0, 900, TimeSpan.Zero), .HasModifiedAfter = True})
        Dim back As ExchangeDriveRequest = Nothing
        Assert.True(ExchangeDriveRequests.TryParse(built, back))
        Assert.Equal("Camera", back.Path)
        Assert.Equal(New DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero), back.ModifiedAfter)

        Assert.Equal("", ExchangeDriveRequests.Build(New ExchangeDriveRequest() With {.Action = ExchangeDriveRequestAction.List, .ResourceId = "r1", .Path = "../up"}))
        Dim tooMany As String() = Enumerable.Range(1, 101).Select(Function(i) "f" & i.ToString()).ToArray()
        Assert.Equal("", ExchangeDriveRequests.Build(New ExchangeDriveRequest() With {.Action = ExchangeDriveRequestAction.Fetch, .ResourceId = "r1", .Paths = tooMany}))
    End Sub

    <Theory>
    <InlineData("", True)>
    <InlineData("Camera", True)>
    <InlineData("Camera/2026/a.jpg", True)>
    <InlineData("..", False)>
    <InlineData("Camera/../..", False)>
    <InlineData("./a.jpg", False)>
    <InlineData("/abs", False)>
    <InlineData("trailing/", False)>
    <InlineData("a//b", False)>
    <InlineData("a\b", False)>
    <InlineData("C:", False)>
    <InlineData("a.jpg:stream", False)>
    <InlineData("dir./a", False)>
    <InlineData("dir /a", False)>
    Public Sub Only_a_relative_path_of_plain_segments_is_accepted(path As String, expected As Boolean)
        Assert.Equal(expected, ExchangeDriveRequests.IsRelativePath(path))
    End Sub

    <Fact>
    Public Sub A_listing_round_trips_and_an_entry_outside_the_folder_is_dropped()
        Dim body As Byte() = ExchangeDriveRequests.BuildListing(New DateTimeOffset(2026, 10, 15, 8, 0, 0, 750, TimeSpan.Zero), {
            New ExchangeDriveListingEntry("a.jpg", False, 3, New DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            New ExchangeDriveListingEntry("Sub", True, 99, New DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero))})
        Dim listing As ExchangeDriveListingReply = Nothing
        Assert.True(ExchangeDriveRequests.TryParseListing(body, listing))
        Assert.Equal("2026-10-15T08:00:00Z", listing.AsOf)
        Assert.Equal(2, listing.Entries.Count)
        Assert.Equal(3L, listing.Entries(0).Size)
        Assert.True(listing.Entries(1).IsDirectory)
        Assert.Equal(0L, listing.Entries(1).Size)

        Dim hostile As Byte() = Encoding.UTF8.GetBytes("{""asOf"":""2026-10-15T08:00:00Z"",""entries"":[{""name"":""../x"",""dir"":false},{""name"":""ok.txt"",""size"":1}]}")
        Assert.True(ExchangeDriveRequests.TryParseListing(hostile, listing))
        Assert.Equal("ok.txt", Assert.Single(listing.Entries).Name)
    End Sub

    <Fact>
    Public Sub An_answer_is_split_by_packet_bytes_with_unique_base_names_per_reply()
        Dim files As New List(Of ExchangeDriveAnswerFile) From {
            New ExchangeDriveAnswerFile("a/x.jpg", 40),
            New ExchangeDriveAnswerFile("b/x.jpg", 40),
            New ExchangeDriveAnswerFile("c.jpg", 50),
            New ExchangeDriveAnswerFile("d.jpg", 10)}
        Dim replies As List(Of List(Of ExchangeDriveAnswerFile)) = ExchangeDriveRequests.SplitReplies(files, 100)
        For Each reply As List(Of ExchangeDriveAnswerFile) In replies
            Assert.True(reply.Sum(Function(f) f.Size) <= 100)
            Assert.Equal(reply.Count, reply.Select(Function(f) f.Name.ToLowerInvariant()).Distinct().Count())
        Next
        Assert.Equal(4, replies.Sum(Function(r) r.Count))
        Assert.Equal(2, replies.Count)

        Assert.Null(ExchangeDriveRequests.SplitReplies({New ExchangeDriveAnswerFile("huge.bin", 101)}, 100))
        Assert.Empty(Assert.Single(ExchangeDriveRequests.SplitReplies(New List(Of ExchangeDriveAnswerFile), 100)))
    End Sub

End Class
#End If
