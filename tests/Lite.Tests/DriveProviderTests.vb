#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 269 P3.3: a folder of another device reached through Google Drive (DEVICE-EXCHANGE items BB, CO, CQ). The provider follows the agent's
''' jobs against a FAKE channel, so nothing here touches the pipe or Drive: a refusal keeps its reason, a cancel withdraws the request, a listing
''' is one level of names, a fetch leaves no staging folder behind. The rest holds the locator, the records it may be stored as, and the group of
''' the device section (only what no other road reaches, and a live record ends a Drive record).
''' </summary>
Public Class DriveProviderTests

    Private Const Rid As String = "EBESExQVFhcYGRobHB0eHw"
    Private Const Did As String = "AAECAwQFBgcICQoLDA0ODw"
    Private Const Own As String = "ICEiIyQlJicoKSorLC0uLw"

    Private NotInheritable Class FakeChannel
        Implements IDriveJobChannel

        Public ReadOnly Lists As New List(Of String)
        Public ReadOnly Fetches As New List(Of String)
        Public ReadOnly Polls As New List(Of String)
        Public ReadOnly Withdrawn As New List(Of String)
        Public ReadOnly StartReplies As New Queue(Of ExchangeAgentDriveJobResult)
        Public ReadOnly PollReplies As New Queue(Of ExchangeAgentDriveJobResult)
        ''' <summary>What the agent does with a fetch: gets the folder it was told to save into.</summary>
        Public OnFetch As Action(Of String)
        Public OnPoll As Action

        Public Function ListAsync(resourceId As String, path As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult) Implements IDriveJobChannel.ListAsync
            Lists.Add(path)
            Return Task.FromResult(StartReplies.Dequeue())
        End Function

        Public Function FetchAsync(resourceId As String, paths As IEnumerable(Of String), directory As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult) Implements IDriveJobChannel.FetchAsync
            Fetches.AddRange(paths)
            OnFetch?.Invoke(directory)
            Return Task.FromResult(StartReplies.Dequeue())
        End Function

        Public Function JobAsync(jobId As String, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult) Implements IDriveJobChannel.JobAsync
            Polls.Add(jobId)
            OnPoll?.Invoke()
            Return Task.FromResult(PollReplies.Dequeue())
        End Function

        Public Function CancelAsync(jobId As String, ct As CancellationToken) As Task(Of ExchangeAgentReply) Implements IDriveJobChannel.CancelAsync
            Withdrawn.Add(jobId)
            Return Task.FromResult(Ok())
        End Function
    End Class

    Private Shared Function Ok() As ExchangeAgentReply
        Return New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", "")
    End Function

    Private Shared Function Waiting(jobId As String, Optional device As String = "", Optional lastRecord As String = "") As ExchangeAgentDriveJobResult
        Return New ExchangeAgentDriveJobResult(Ok(), New ExchangeAgentDriveJob(jobId, ExchangeAgentDriveJobState.Waiting, "", device, Nothing, Nothing, lastRecord))
    End Function

    Private Shared Function Listed(jobId As String, ParamArray entries As ExchangeDriveListingEntry()) As ExchangeAgentDriveJobResult
        Return New ExchangeAgentDriveJobResult(Ok(), New ExchangeAgentDriveJob(jobId, ExchangeAgentDriveJobState.Done, "", "", New ExchangeDriveListingReply("", entries), Nothing))
    End Function

    Private Shared Function FailedJob(jobId As String, reason As String) As ExchangeAgentDriveJobResult
        Return New ExchangeAgentDriveJobResult(Ok(), New ExchangeAgentDriveJob(jobId, ExchangeAgentDriveJobState.Failed, reason, "", Nothing, Nothing))
    End Function

    Private Shared Function Refused(reason As String) As ExchangeAgentDriveJobResult
        Return New ExchangeAgentDriveJobResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Refused, reason, ""), Nothing)
    End Function

    Private Shared Function NoAgent() As ExchangeAgentDriveJobResult
        Return New ExchangeAgentDriveJobResult(New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", ""), Nothing)
    End Function

    Private Shared Function FileEntry(name As String, size As Long) As ExchangeDriveListingEntry
        Return New ExchangeDriveListingEntry(name, False, size, New DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero))
    End Function

    Private Shared Function ScratchRoot() As String
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-gd-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root)
        Return root
    End Function

    Private Shared Function ReasonThrownBy(action As Action) As String
        Dim thrown As ResourceFailureException = Assert.Throws(Of ResourceFailureException)(action)
        Return DriveFailures.ReasonOf(thrown.Failure)
    End Function

    ' ---- the locator ----

    <Fact>
    Public Sub A_locator_round_trips_the_resource_and_the_folder()
        Dim resource As New ResourceDescriptor With {.Kind = ResourceKind.Drive, .ExchangeResourceId = Rid, .RootPath = "/"}
        Dim locator As String = ResourceLocator.Build(resource, "/Фото 2026/лето")
        Dim id As String = "", folder As String = ""
        Assert.StartsWith("gd://" & Rid & "/", locator)
        Assert.True(ResourceLocator.TryParseDrive(locator, id, folder))
        Assert.Equal(Rid, id)
        Assert.Equal("/Фото 2026/лето", folder)
    End Sub

    <Theory>
    <InlineData("gd://")>
    <InlineData("gd:///sub")>
    <InlineData("gd://abc/sub?x=1")>
    <InlineData("gd://abc/sub#x")>
    <InlineData("gd://abc/%2E%2E/up")>
    <InlineData("sftp://host/sub")>
    <InlineData("C:\Photos")>
    Public Sub A_locator_out_of_form_is_refused(text As String)
        Dim id As String = "", folder As String = ""
        Assert.False(ResourceLocator.TryParseDrive(text, id, folder))
    End Sub

    <Fact>
    Public Sub A_drive_locator_is_a_locator_and_never_an_sftp_address()
        Dim user As String = "", host As String = "", port As Integer = 0, folder As String = "/"
        Assert.True(ResourceLocator.IsLocator("gd://abc/sub"))
        Assert.True(ResourceLocator.IsLocator("sftp://host/sub"))
        Assert.False(ResourceLocator.IsLocator("C:\Photos"))
        ' a slot and the file manager parse with TryParse: neither takes a road that cannot be written to
        Assert.False(ResourceLocator.TryParse("gd://abc/sub", user, host, port, folder))
    End Sub

    <Fact>
    Public Sub Parent_and_child_of_a_drive_locator_stay_inside_the_authority()
        Assert.Equal("gd://abc/sub/a.jpg", ResourceLocator.AppendName("gd://abc/sub", "a.jpg"))
        Assert.Equal("gd://abc/sub", ResourceLocator.ParentOf("gd://abc/sub/a.jpg"))
        Assert.Equal("gd://abc/", ResourceLocator.ParentOf("gd://abc/a.jpg"))
        Assert.Equal("gd://abc/", ResourceLocator.ParentOf("gd://abc/"))
    End Sub

    ' ---- what may be stored ----

    <Fact>
    Public Sub A_record_through_drive_is_storable_with_its_resource_id_and_nothing_else()
        Dim record As ResourceDescriptor = ExchangeAttach.DriveDescriptor(Rid, "  Photos ", Did, "Desk", Function() "id-1")
        Assert.True(ResourceRegistry.IsStorable(record))
        Assert.Equal(ResourceKind.Drive, record.Kind)
        Assert.Equal("Photos", record.DisplayName)
        Assert.True(record.ReadOnlyHint)
        Assert.True(ResourceOrigin.IsExchange(record))
        Assert.Equal("", If(record.Host, ""))
        Assert.Equal("", If(record.SecretHandle, ""))
        Assert.False(ResourceRegistry.IsStorable(New ResourceDescriptor With {.Id = "x", .Kind = ResourceKind.Drive}))
    End Sub

    ' ---- capabilities ----

    <Fact>
    Public Sub The_provider_is_read_only_and_never_fetches_ahead()
        Dim caps As ProviderCapabilities = New DriveProvider(New FakeChannel(), Rid).Capabilities
        Assert.False(caps.CanWrite OrElse caps.CanDelete OrElse caps.CanRename OrElse caps.CanCreateFolder)
        Assert.True(caps.NeedsMaterialisation)
        Assert.True(caps.FetchOnDemandOnly)
        Assert.Equal(0, RemoteCacheStore.PrefetchDepth(caps))
    End Sub

    ' ---- a listing ----

    <Fact>
    Public Sub A_listing_follows_the_job_and_keeps_one_level_of_names()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Waiting("j1", "Desk", "2026-11-08T10:00:00Z"))
        channel.PollReplies.Enqueue(Listed("j1", FileEntry("a.jpg", 10), FileEntry("sub/b.jpg", 11), FileEntry("../c.jpg", 12), New ExchangeDriveListingEntry("d", True, 0, DateTimeOffset.UnixEpoch)))
        Dim provider As New DriveProvider(channel, Rid)

        Dim entries As IReadOnlyList(Of ResourceEntry) = provider.ListEntries("/sub", CancellationToken.None)

        Assert.Equal(New String() {"sub"}, channel.Lists.ToArray())
        Assert.Equal(New String() {"a.jpg", "d"}, entries.Select(Function(e) e.Name).ToArray())
        Assert.Equal("/sub/a.jpg", entries(0).FullPath)
        Assert.Equal(10L, entries(0).Length)
        Assert.True(entries(1).IsDirectory)
        Assert.Equal("Desk", provider.DeviceName)
        Assert.Equal(New DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero), provider.LastRecordAt.Value)
        Dim known As ResourceEntry = Nothing
        Assert.True(provider.TryStat("/sub/a.jpg", known))
        Assert.False(provider.TryStat("/sub/../c.jpg", known))
        Assert.Empty(channel.Withdrawn)
    End Sub

    <Fact>
    Public Sub A_refused_job_keeps_its_reason_and_withdraws_nothing()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Waiting("j1"))
        channel.PollReplies.Enqueue(FailedJob("j1", ExchangeDriveRequests.ReasonPinRequired))
        Dim provider As New DriveProvider(channel, Rid)

        Assert.Equal(ExchangeDriveRequests.ReasonPinRequired, ReasonThrownBy(Sub() provider.ListEntries("/", CancellationToken.None)))
        Assert.Empty(channel.Withdrawn)
    End Sub

    <Fact>
    Public Sub A_refusal_at_the_start_sends_no_poll()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Refused(ExchangeAgentPipe.ErrorDriveOffline))
        Dim provider As New DriveProvider(channel, Rid)

        Assert.Equal(ExchangeAgentPipe.ErrorDriveOffline, ReasonThrownBy(Sub() provider.ListEntries("/", CancellationToken.None)))
        Assert.Empty(channel.Polls)
    End Sub

    <Fact>
    Public Sub No_agent_on_the_pipe_is_its_own_reason()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(NoAgent())
        Dim provider As New DriveProvider(channel, Rid)

        Assert.Equal(DriveFailures.ReasonAgentAbsent, ReasonThrownBy(Sub() provider.ListEntries("/", CancellationToken.None)))
    End Sub

    <Fact>
    Public Sub A_busy_agent_is_asked_again()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Refused(ExchangeAgentPipe.ErrorBusy))
        channel.StartReplies.Enqueue(Waiting("j1"))
        channel.PollReplies.Enqueue(Listed("j1"))
        Dim provider As New DriveProvider(channel, Rid)

        Assert.Empty(provider.ListEntries("/", CancellationToken.None))
        Assert.Equal(2, channel.Lists.Count)
    End Sub

    <Fact>
    Public Sub A_pipe_that_stays_silent_gives_the_job_up()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Waiting("j1"))
        For i As Integer = 1 To DriveProvider.MaxPipeMisses
            channel.PollReplies.Enqueue(NoAgent())
        Next
        Dim provider As New DriveProvider(channel, Rid)

        Assert.Equal(DriveFailures.ReasonAgentAbsent, ReasonThrownBy(Sub() provider.ListEntries("/", CancellationToken.None)))
        Assert.Equal(DriveProvider.MaxPipeMisses, channel.Polls.Count)
        Assert.Equal(New String() {"j1"}, channel.Withdrawn.ToArray())
    End Sub

    <Fact>
    Public Sub A_cancel_withdraws_the_request()
        Using cancel As New CancellationTokenSource()
            Dim channel As New FakeChannel()
            channel.StartReplies.Enqueue(Waiting("j1"))
            channel.PollReplies.Enqueue(Waiting("j1"))
            channel.OnPoll = Sub() cancel.Cancel()
            Dim provider As New DriveProvider(channel, Rid)

            Assert.Throws(Of OperationCanceledException)(Sub() provider.ListEntries("/", cancel.Token))
            Assert.Equal(New String() {"j1"}, channel.Withdrawn.ToArray())
        End Using
    End Sub

    <Fact>
    Public Sub A_job_the_agent_no_longer_has_is_not_withdrawn()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Waiting("j1"))
        channel.PollReplies.Enqueue(Refused(ExchangeAgentPipe.ErrorUnknownJob))
        Dim provider As New DriveProvider(channel, Rid)

        Assert.Equal(ExchangeAgentPipe.ErrorUnknownJob, ReasonThrownBy(Sub() provider.ListEntries("/", CancellationToken.None)))
        Assert.Empty(channel.Withdrawn)
    End Sub

    ' ---- the last road of item BB ----

    <Fact>
    Public Sub A_started_listing_is_adopted_and_not_sent_twice()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Waiting("j1"))
        channel.PollReplies.Enqueue(Listed("j1", FileEntry("a.jpg", 1)))
        Dim provider As New DriveProvider(channel, Rid)

        Assert.True(provider.TryStartListing("/", CancellationToken.None))
        Assert.Single(provider.ListEntries("/", CancellationToken.None))
        Assert.Single(channel.Lists)
    End Sub

    <Fact>
    Public Sub A_started_listing_of_another_folder_is_not_adopted()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Waiting("j1"))
        channel.StartReplies.Enqueue(Waiting("j2"))
        channel.PollReplies.Enqueue(Listed("j2"))
        Dim provider As New DriveProvider(channel, Rid)

        Assert.True(provider.TryStartListing("/", CancellationToken.None))
        provider.ListEntries("/sub", CancellationToken.None)

        Assert.Equal(New String() {"", "sub"}, channel.Lists.ToArray())
        Assert.Equal(New String() {"j2"}, channel.Polls.ToArray())
    End Sub

    <Fact>
    Public Sub A_listing_refused_at_once_leaves_the_callers_failure_standing()
        Dim channel As New FakeChannel()
        channel.StartReplies.Enqueue(Refused(ExchangeAgentPipe.ErrorUnknownResource))
        Assert.False(New DriveProvider(channel, Rid).TryStartListing("/", CancellationToken.None))
    End Sub

    ' ---- a fetch ----

    <Fact>
    Public Sub A_fetch_lands_at_the_target_and_leaves_no_staging_folder()
        Dim root As String = ScratchRoot()
        Dim target As String = Path.Combine(root, "target.bin")
        Try
            Dim channel As New FakeChannel()
            Dim saved As String = ""
            channel.OnFetch = Sub(folder)
                                  saved = Path.Combine(folder, "a.jpg")
                                  File.WriteAllBytes(saved, New Byte() {1, 2, 3})
                                  channel.PollReplies.Enqueue(New ExchangeAgentDriveJobResult(Ok(), New ExchangeAgentDriveJob("j1", ExchangeAgentDriveJobState.Done, "", "", Nothing, {New ExchangeAgentDriveFile("sub/a.jpg", saved)})))
                              End Sub
            channel.StartReplies.Enqueue(Waiting("j1"))
            Dim provider As New DriveProvider(channel, Rid, stagingRoot:=root)

            provider.Download("/sub/a.jpg", target, Nothing, CancellationToken.None)

            Assert.Equal(New String() {"sub/a.jpg"}, channel.Fetches.ToArray())
            Assert.Equal(3L, New FileInfo(target).Length)
            Assert.Empty(Directory.GetDirectories(root))
            Assert.Null(provider.LastFailure)
        Finally
            Directory.Delete(root, True)
        End Try
    End Sub

    <Fact>
    Public Sub A_failed_fetch_says_why_and_leaves_no_staging_folder()
        Dim root As String = ScratchRoot()
        Try
            Dim channel As New FakeChannel()
            channel.StartReplies.Enqueue(Waiting("j1"))
            channel.PollReplies.Enqueue(FailedJob("j1", ExchangeDriveRequests.ReasonTooLarge))
            Dim provider As New DriveProvider(channel, Rid, stagingRoot:=root)

            Assert.Equal(ExchangeDriveRequests.ReasonTooLarge, ReasonThrownBy(Sub() provider.Download("/a.jpg", Path.Combine(root, "t.bin"), Nothing, CancellationToken.None)))
            Assert.Equal(ExchangeDriveRequests.ReasonTooLarge, DriveFailures.ReasonOf(provider.LastFailure))
            Assert.Empty(Directory.GetDirectories(root))
        Finally
            Directory.Delete(root, True)
        End Try
    End Sub

    <Fact>
    Public Sub A_saved_path_outside_the_staging_folder_is_never_moved()
        Dim root As String = ScratchRoot()
        Dim elsewhere As String = Path.Combine(root, "elsewhere.jpg")
        Try
            File.WriteAllBytes(elsewhere, New Byte() {9})
            Dim channel As New FakeChannel()
            channel.StartReplies.Enqueue(Waiting("j1"))
            channel.PollReplies.Enqueue(New ExchangeAgentDriveJobResult(Ok(), New ExchangeAgentDriveJob("j1", ExchangeAgentDriveJobState.Done, "", "", Nothing, {New ExchangeAgentDriveFile("a.jpg", elsewhere)})))
            Dim provider As New DriveProvider(channel, Rid, stagingRoot:=root)

            Assert.Equal(ExchangeDriveRequests.ReasonUnavailable, ReasonThrownBy(Sub() provider.Download("/a.jpg", Path.Combine(root, "t.bin"), Nothing, CancellationToken.None)))
            Assert.True(File.Exists(elsewhere))
        Finally
            Directory.Delete(root, True)
        End Try
    End Sub

    <Fact>
    Public Sub No_writing_verb_exists_on_this_road()
        Dim provider As New DriveProvider(New FakeChannel(), Rid)
        Assert.Throws(Of ResourceFailureException)(Sub() provider.Upload("a", "b", Nothing, CancellationToken.None))
        Assert.Throws(Of ResourceFailureException)(Sub() provider.DeleteFile("a"))
        Assert.Throws(Of ResourceFailureException)(Sub() provider.Rename("a", "b"))
        Assert.Throws(Of ResourceFailureException)(Sub() provider.CreateDirectory("a"))
    End Sub

    ' ---- how a refusal reads ----

    <Theory>
    <InlineData("pin-required", True)>
    <InlineData("too-large", True)>
    <InlineData("not-permitted", True)>
    <InlineData("unavailable", False)>
    <InlineData("drive-offline", False)>
    <InlineData("no-answer", False)>
    Public Sub A_refusal_that_waiting_cannot_change_is_not_retryable(reason As String, final As Boolean)
        Dim failure As ResourceFailure = DriveFailures.FromReason(reason)
        Assert.Equal(reason, DriveFailures.ReasonOf(failure))
        Assert.Equal(Not final, failure.IsRetryable)
    End Sub

    <Fact>
    Public Sub The_token_of_another_failure_is_not_read_as_this_channels()
        Assert.Equal("", DriveFailures.ReasonOf(New ResourceFailure(ResourceFailureKind.Transport, Nothing, "pin-required")))
        Assert.Equal("", DriveFailures.ReasonOf(Nothing))
    End Sub

    <Theory>
    <InlineData("pin-required")>
    <InlineData("too-large")>
    <InlineData("not-permitted")>
    <InlineData("unavailable")>
    <InlineData("drive-offline")>
    <InlineData("no-answer")>
    <InlineData("agent-absent")>
    <InlineData("anything-else")>
    Public Sub Every_reason_is_a_sentence_and_no_token_shows(reason As String)
        Dim text As String = ResourceMessages.ForFailure(DriveFailures.FromReason(reason))
        Assert.False(String.IsNullOrWhiteSpace(text))
        Assert.DoesNotContain(reason, text)
    End Sub

    <Fact>
    Public Sub Every_string_of_the_road_is_translated_in_every_column()
        Dim keys As String() = {
            "через Google Drive", "через GD",
            "заблокировано: PIN-код через GD не передаётся",
            "Папка защищена PIN-кодом, через GD она не открывается."}
        For Each key As String In keys
            Dim row As String() = Localization.All.Where(Function(entry) String.Equals(entry.Key, key, StringComparison.Ordinal)).Select(Function(entry) entry.Value).Single()
            For Each cell As String In row
                Assert.False(String.IsNullOrWhiteSpace(cell), key)
            Next
            ' the Ukrainian "через" reads like the Russian one, so a column or two may match the key; a row that is mostly the key was never translated
            Assert.True(row.Count(Function(cell) Not String.Equals(cell, key, StringComparison.Ordinal)) >= row.Length - 2, key)
        Next
    End Sub

    ' ---- the group of the device section ----

    Private Shared Function Device(id As String, name As String, Optional presence As String = "offline") As ExchangeDeviceRecord
        Return New ExchangeDeviceRecord("", id, name, "FMS", "1", "windows", Array.Empty(Of String)(), presence, "2026-11-08T10:00:00Z")
    End Function

    Private Shared Function Record(id As String, deviceId As String, Optional access As String = "", Optional isProtected As Boolean = False, Optional scheme As String = "") As ExchangeResourceRecord
        Return New ExchangeResourceRecord("", id, deviceId, ExchangeWire.KindSftpShare, "Folder " & id, "online", access, isProtected, scheme)
    End Function

    Private Shared Function Snapshot(ParamArray resources As ExchangeResourceRecord()) As ExchangeDirectorySnapshot
        Return New ExchangeDirectorySnapshot(1, {Device(Did, "Desk"), Device(Own, "This PC")}, resources, Array.Empty(Of ExchangeBroadcastRecord)())
    End Function

    <Fact>
    Public Sub The_group_lists_only_what_no_other_road_reaches()
        Dim elsewhere As New HashSet(Of String)(StringComparer.Ordinal) From {"r-listed"}
        Dim live As New ResourceDescriptor With {.Id = "1", .Kind = ResourceKind.Sftp, .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "r-live"}

        Dim groups As List(Of ExchangeDeviceGroup) = ExchangeDevicesModel.BuildDriveGroups(
            Snapshot(Record("r-gd", Did), Record("r-listed", Did), Record("r-live", Did), Record("r-access", Did, access:="{""a"":1}")), elsewhere, {live}, Own)

        Dim g As ExchangeDeviceGroup = Assert.Single(groups)
        Assert.Equal("Desk", g.Name)
        Assert.Equal("r-gd", Assert.Single(g.Rows).ResourceId)
    End Sub

    <Fact>
    Public Sub A_tick_in_the_group_reads_only_records_through_drive()
        Dim mine As New ResourceDescriptor With {.Id = "1", .Kind = ResourceKind.Drive, .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "r-gd"}
        Dim row As ExchangeDeviceRow = Assert.Single(Assert.Single(ExchangeDevicesModel.BuildDriveGroups(Snapshot(Record("r-gd", Did)), Nothing, {mine}, Own)).Rows)
        Assert.True(row.Attached)
    End Sub

    <Fact>
    Public Sub A_device_with_nothing_for_this_road_draws_no_group()
        Assert.Empty(ExchangeDevicesModel.BuildDriveGroups(Snapshot(), Nothing, Nothing, Own))
        Assert.Empty(ExchangeDevicesModel.BuildDriveGroups(Nothing, Nothing, Nothing, Own))
    End Sub

    <Fact>
    Public Sub A_protected_folder_stays_a_row_and_says_so()
        Dim row As ExchangeDeviceRow = Assert.Single(Assert.Single(ExchangeDevicesModel.BuildDriveGroups(
            Snapshot(Record("r-pin", Did, isProtected:=True, scheme:="pin-v1")), Nothing, Nothing, Own)).Rows)
        Assert.True(row.PinProtected)
        Assert.False(String.IsNullOrWhiteSpace(ExchangeResourceText.RowStateViaDrive(row)))
    End Sub

    <Fact>
    Public Sub Only_a_live_record_of_the_same_folder_makes_a_drive_record_a_twin()
        Dim driveOne As New ResourceDescriptor With {.Id = "1", .Kind = ResourceKind.Drive, .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "r1"}
        Dim driveTwo As New ResourceDescriptor With {.Id = "2", .Kind = ResourceKind.Drive, .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "r2"}
        Dim live As New ResourceDescriptor With {.Id = "3", .Kind = ResourceKind.Sftp, .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "r1"}
        Dim manual As New ResourceDescriptor With {.Id = "4", .Kind = ResourceKind.Sftp, .ExchangeResourceId = "r2"}

        Dim twins As List(Of ResourceDescriptor) = ExchangeDevicesModel.DriveTwins({driveOne, driveTwo, live, manual})

        Assert.Same(driveOne, Assert.Single(twins))
    End Sub

    ' ---- the waiting line's fact on the pipe ----

    <Fact>
    Public Sub The_last_record_time_survives_the_pipe()
        Dim json As String = ExchangeAgentDriveJob.ToJson("j1", ExchangeAgentDriveJobState.Waiting, "", "Desk", Nothing, Nothing, "2026-11-08T10:00:00Z")
        Dim job As ExchangeAgentDriveJob = Nothing
        Assert.True(ExchangeAgentDriveJob.TryParse(json, job))
        Assert.Equal("2026-11-08T10:00:00Z", job.LastRecordAt)
        Assert.Equal("Desk", job.DeviceName)
    End Sub

End Class
#End If
