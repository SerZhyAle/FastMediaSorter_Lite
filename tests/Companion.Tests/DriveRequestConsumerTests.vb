Option Strict On

Imports System.IO
Imports System.Text
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 269 P3.2 - the device agent asks, for the viewer, the PC that lists a folder on Google Drive (DEVICE-EXCHANGE section 24,
''' items CO, CQ, CR), against a scripted queue: a listing comes back whole, a refusal passes its reason through, two minutes of
''' silence is `no-answer`, a fetch collects its files across replies until the `final` one, a file that does not arrive whole is
''' `corrupt`, a cancelled job withdraws its REQUEST, a fetch saves only into a folder under the remote cache, and the job registry
''' is bounded and forgets a finished job once it was read.
''' </summary>
Public Class DriveRequestConsumerTests
    Implements IDisposable

    Private Const DeviceB As String = "BBBBBBBBBBBBBBBBBBBBBA"
    Private Const ResourceId As String = "res_0123456789abcdef"

    Private ReadOnly _root As String

    Public Sub New()
        _root = Path.Combine(Path.GetTempPath(), "fmsdrvcons-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_root)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_root, True)
        Catch ex As IOException
        End Try
    End Sub

    ''' <summary>A queue that answers from a script: each AwaitReplies call hands out the next batch (an empty batch is silence).</summary>
    Private NotInheritable Class ScriptedPort
        Implements IDriveRequestPort

        Public ReadOnly Batches As New Queue(Of DriveReplyView())
        Public ReadOnly Listings As New Dictionary(Of String, Byte())(StringComparer.Ordinal)
        Public ReadOnly Files As New Dictionary(Of String, String())(StringComparer.Ordinal)
        Public ReadOnly Deleted As New List(Of String)
        Public ReadOnly Withdrawn As New List(Of String)
        Public ReadOnly SentJson As New List(Of String)
        Public SendError As String = ""
        Public Gate As Task = Task.CompletedTask

        Public Function SendRequestAsync(requestJson As String, targetDeviceId As String, targetDeviceName As String, ct As CancellationToken) As Task(Of DriveRequestSent) Implements IDriveRequestPort.SendRequestAsync
            SentJson.Add(requestJson)
            Return Task.FromResult(If(SendError.Length > 0, New DriveRequestSent("", SendError), New DriveRequestSent("req-1", "")))
        End Function

        Public Async Function AwaitRepliesAsync(requestPacketId As String, seen As ISet(Of String), ct As CancellationToken) As Task(Of IReadOnlyList(Of DriveReplyView)) Implements IDriveRequestPort.AwaitRepliesAsync
            Await Gate.WaitAsync(ct)
            ct.ThrowIfCancellationRequested()
            If Batches.Count = 0 Then Return Array.Empty(Of DriveReplyView)()
            Return Batches.Dequeue()
        End Function

        Public Function ReadListingAsync(reply As DriveReplyView, ct As CancellationToken) As Task(Of Byte()) Implements IDriveRequestPort.ReadListingAsync
            Dim body As Byte() = Nothing
            Listings.TryGetValue(reply.PacketId, body)
            Return Task.FromResult(body)
        End Function

        Public Function ReceiveAsync(reply As DriveReplyView, directory As String, ct As CancellationToken) As Task(Of IReadOnlyList(Of String)) Implements IDriveRequestPort.ReceiveAsync
            Dim names As String() = Nothing
            If Not Files.TryGetValue(reply.PacketId, names) Then Return Task.FromResult(Of IReadOnlyList(Of String))(Nothing)
            Dim saved As New List(Of String)
            For Each name As String In names
                Dim target As String = Path.Combine(directory, name)
                File.WriteAllText(target, name)
                saved.Add(target)
            Next
            Deleted.Add(reply.PacketId)
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

    Private Shared Function Ok(id As String, Optional final As Boolean = True) As DriveReplyView
        Return New DriveReplyView(id, ExchangeDriveRequests.OutcomeOk, "", final, Nothing)
    End Function

    Private Shared Function ListRequest(Optional path As String = "") As ExchangeDriveRequest
        Return New ExchangeDriveRequest() With {.Action = ExchangeDriveRequestAction.List, .ResourceId = ResourceId, .Path = path}
    End Function

    Private Shared Function FetchRequest(ParamArray paths As String()) As ExchangeDriveRequest
        Return New ExchangeDriveRequest() With {.Action = ExchangeDriveRequestAction.Fetch, .ResourceId = ResourceId, .Paths = paths}
    End Function

    Private Function Run(port As ScriptedPort, request As ExchangeDriveRequest, Optional directory As String = Nothing) As DriveRequestOutcome
        Return DriveRequestConsumer.RunAsync(port, request, DeviceB, "Office PC", If(directory, _root), CancellationToken.None).GetAwaiter().GetResult()
    End Function

    <Fact>
    Public Sub A_list_comes_back_as_the_reply_listing_and_the_reply_is_deleted()
        Dim port As New ScriptedPort()
        Dim body As Byte() = ExchangeDriveRequests.BuildListing(New DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
                                                               {New ExchangeDriveListingEntry("a.jpg", False, 3, DateTimeOffset.UnixEpoch)})
        port.Batches.Enqueue({Ok("r1")})
        port.Listings("r1") = body
        Dim outcome As DriveRequestOutcome = Run(port, ListRequest("Camera"))
        Assert.True(outcome.Succeeded)
        Assert.Equal(body, outcome.ListingBody)
        Assert.Equal({"r1"}, port.Deleted.ToArray())
        Assert.Contains("""path"":""Camera""", port.SentJson(0))
    End Sub

    <Fact>
    Public Sub A_refusal_passes_its_reason_through_and_the_reply_is_deleted()
        Dim port As New ScriptedPort()
        port.Batches.Enqueue({New DriveReplyView("r1", ExchangeDriveRequests.OutcomeRefused, ExchangeDriveRequests.ReasonPinRequired, True, Nothing)})
        Dim outcome As DriveRequestOutcome = Run(port, ListRequest())
        Assert.Equal(ExchangeDriveRequests.ReasonPinRequired, outcome.Error)
        Assert.Equal({"r1"}, port.Deleted.ToArray())
    End Sub

    <Fact>
    Public Sub Silence_is_no_answer()
        Dim port As New ScriptedPort()
        Assert.Equal(ExchangeAgentPipe.ErrorNoAnswer, Run(port, ListRequest()).Error)
    End Sub

    <Fact>
    Public Sub A_listing_that_is_not_readable_is_corrupt()
        Dim port As New ScriptedPort()
        port.Batches.Enqueue({Ok("r1")})
        port.Listings("r1") = Encoding.UTF8.GetBytes("not json")
        Assert.Equal(ExchangeAgentPipe.ErrorCorrupt, Run(port, ListRequest()).Error)
        Assert.Equal({"r1"}, port.Deleted.ToArray())
    End Sub

    <Fact>
    Public Sub A_send_refused_for_space_is_drive_full()
        Dim port As New ScriptedPort() With {.SendError = ExchangeDriveRequests.ReasonDriveFull}
        Assert.Equal(ExchangeDriveRequests.ReasonDriveFull, Run(port, ListRequest()).Error)
    End Sub

    <Fact>
    Public Sub A_fetch_collects_its_files_across_replies_until_the_final_one()
        Dim port As New ScriptedPort()
        port.Batches.Enqueue({Ok("r1", final:=False)})
        port.Batches.Enqueue({Ok("r2", final:=True)})
        port.Files("r1") = {"a.jpg"}
        port.Files("r2") = {"b.jpg"}
        Dim outcome As DriveRequestOutcome = Run(port, FetchRequest("Camera/a.jpg", "Camera/2026/b.jpg"))
        Assert.True(outcome.Succeeded, outcome.Error)
        Assert.Equal(2, outcome.Files.Count)
        Assert.Equal("Camera/a.jpg", outcome.Files(0).Path)
        Assert.Equal(Path.Combine(_root, "a.jpg"), outcome.Files(0).SavedPath)
        Assert.Equal("Camera/2026/b.jpg", outcome.Files(1).Path)
        Assert.Equal(Path.Combine(_root, "b.jpg"), outcome.Files(1).SavedPath)
        Assert.Equal({"r1", "r2"}, port.Deleted.ToArray())
    End Sub

    <Fact>
    Public Sub A_file_saved_under_a_free_name_still_answers_the_path_asked()
        Dim port As New ScriptedPort()
        port.Batches.Enqueue({Ok("r1")})
        port.Files("r1") = {"a (2).jpg"}
        Dim outcome As DriveRequestOutcome = Run(port, FetchRequest("x/a.jpg"))
        Assert.True(outcome.Succeeded, outcome.Error)
        Assert.Equal(Path.Combine(_root, "a (2).jpg"), Assert.Single(outcome.Files).SavedPath)
    End Sub

    <Fact>
    Public Sub A_file_that_did_not_arrive_whole_is_corrupt()
        Dim port As New ScriptedPort()
        port.Batches.Enqueue({Ok("r1")})
        Assert.Equal(ExchangeAgentPipe.ErrorCorrupt, Run(port, FetchRequest("a.jpg")).Error)
        Assert.Equal({"r1"}, port.Deleted.ToArray())
    End Sub

    <Fact>
    Public Sub A_final_reply_that_lacks_a_requested_file_is_unavailable()
        Dim port As New ScriptedPort()
        port.Batches.Enqueue({Ok("r1")})
        port.Files("r1") = {"a.jpg"}
        Assert.Equal(ExchangeDriveRequests.ReasonUnavailable, Run(port, FetchRequest("a.jpg", "b.jpg")).Error)
    End Sub

    <Fact>
    Public Sub Paths_that_share_a_base_name_or_an_unknown_action_are_refused_before_anything_is_sent()
        Dim port As New ScriptedPort()
        Assert.Equal(ExchangeAgentPipe.ErrorBadRequest, Run(port, FetchRequest("one/same.txt", "two/SAME.txt")).Error)
        Assert.Equal(ExchangeAgentPipe.ErrorBadRequest, Run(port, FetchRequest("../a.jpg")).Error)
        Dim changes As New ExchangeDriveRequest() With {.Action = ExchangeDriveRequestAction.Changes, .ResourceId = ResourceId}
        Assert.Equal(ExchangeAgentPipe.ErrorBadRequest, Run(port, changes).Error)
        Assert.Empty(port.SentJson)
    End Sub

    <Fact>
    Public Sub A_cancelled_request_withdraws_its_request_packet()
        Dim port As New ScriptedPort()
        Dim never As New TaskCompletionSource(Of Boolean)()
        port.Gate = never.Task
        Using cts As New CancellationTokenSource()
            Dim running As Task(Of DriveRequestOutcome) = DriveRequestConsumer.RunAsync(port, ListRequest(), DeviceB, "Office PC", _root, cts.Token)
            cts.Cancel()
            Assert.ThrowsAny(Of OperationCanceledException)(Sub() running.GetAwaiter().GetResult())
        End Using
        Assert.Equal({"req-1"}, port.Withdrawn.ToArray())
    End Sub

    <Fact>
    Public Sub A_fetch_saves_only_into_an_existing_folder_under_the_cache()
        Dim cache As String = Path.Combine(_root, "remote-cache")
        Dim session As String = Path.Combine(cache, "123-abcd")
        Directory.CreateDirectory(session)
        Assert.True(DriveRequestConsumer.IsCacheFolder(session, cache))
        Assert.True(DriveRequestConsumer.IsCacheFolder(session & Path.DirectorySeparatorChar, cache))
        Assert.False(DriveRequestConsumer.IsCacheFolder(cache, cache))
        Assert.False(DriveRequestConsumer.IsCacheFolder(Path.Combine(cache, "missing"), cache))
        Assert.False(DriveRequestConsumer.IsCacheFolder(Path.Combine(session, "..", "..", ".."), cache))
        Assert.False(DriveRequestConsumer.IsCacheFolder(_root, cache))
        Assert.False(DriveRequestConsumer.IsCacheFolder(cache & "-other", cache))
        Assert.False(DriveRequestConsumer.IsCacheFolder("remote-cache\123-abcd", cache))
        Assert.False(DriveRequestConsumer.IsCacheFolder("", cache))
    End Sub

    <Fact>
    Public Sub A_cache_folder_reached_through_a_link_is_refused()
        Dim cache As String = Path.Combine(_root, "remote-cache")
        Dim outside As String = Path.Combine(_root, "outside")
        Directory.CreateDirectory(cache)
        Directory.CreateDirectory(outside)
        Dim link As String = Path.Combine(cache, "link")
        Try
            Directory.CreateSymbolicLink(link, outside)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            ' a symbolic link needs developer mode or elevation; without either this machine cannot make the case
            Return
        End Try
        Assert.False(DriveRequestConsumer.IsCacheFolder(link, cache))
    End Sub

    <Fact>
    Public Sub The_registry_holds_at_most_eight_running_jobs_and_forgets_a_finished_one_once_read()
        Using jobs As New DriveRequestJobs()
            Dim hold As New TaskCompletionSource(Of DriveRequestOutcome)()
            Dim ids As New List(Of String)
            For i As Integer = 1 To DriveRequestJobs.MaxRunning
                ids.Add(jobs.Start("Office PC", Function(token As CancellationToken) hold.Task))
            Next
            Assert.DoesNotContain("", ids)
            Assert.Equal("", jobs.Start("Office PC", Function(token As CancellationToken) hold.Task))

            Dim waiting As String = jobs.WaitAsync(ids(0), 0, CancellationToken.None).GetAwaiter().GetResult()
            Dim parsed As ExchangeAgentDriveJob = Nothing
            Assert.True(ExchangeAgentDriveJob.TryParse(waiting, parsed))
            Assert.Equal(ExchangeAgentDriveJobState.Waiting, parsed.State)
            Assert.Equal("Office PC", parsed.DeviceName)

            hold.SetResult(DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorNoAnswer))
            Dim finished As String = jobs.WaitAsync(ids(0), 5000, CancellationToken.None).GetAwaiter().GetResult()
            Assert.True(ExchangeAgentDriveJob.TryParse(finished, parsed))
            Assert.Equal(ExchangeAgentDriveJobState.Failed, parsed.State)
            Assert.Equal(ExchangeAgentPipe.ErrorNoAnswer, parsed.Error)
            Assert.Null(jobs.WaitAsync(ids(0), 0, CancellationToken.None).GetAwaiter().GetResult())
            Assert.True(jobs.Cancel(ids(1)))
            Assert.False(jobs.Cancel(ids(1)))
        End Using
    End Sub

    <Fact>
    Public Sub Finished_jobs_nobody_reads_stay_bounded()
        Using jobs As New DriveRequestJobs()
            For i As Integer = 1 To 100
                jobs.Start("Office PC", Function(token As CancellationToken) Task.FromResult(DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorNoAnswer)))
            Next
            Assert.True(jobs.Count <= DriveRequestJobs.MaxFinished + DriveRequestJobs.MaxRunning, jobs.Count.ToString())
        End Using
    End Sub

    <Fact>
    Public Sub A_cancelled_job_cancels_its_request()
        Using jobs As New DriveRequestJobs()
            Dim id As String = jobs.Start("Office PC", Async Function(token As CancellationToken)
                                                           Await Task.Delay(Timeout.Infinite, token)
                                                           Return DriveRequestOutcome.Failed("")
                                                       End Function)
            Assert.True(jobs.Cancel(id))
            Assert.Null(jobs.WaitAsync(id, 0, CancellationToken.None).GetAwaiter().GetResult())
            Assert.Equal(0, jobs.Count)
        End Using
    End Sub

    <Fact>
    Public Sub A_done_job_carries_its_listing_and_files_over_the_pipe_shape()
        Dim body As Byte() = ExchangeDriveRequests.BuildListing(New DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
                                                               {New ExchangeDriveListingEntry("Sub", True, 0, DateTimeOffset.UnixEpoch)})
        Dim json As String = ExchangeAgentDriveJob.ToJson("j1", ExchangeAgentDriveJobState.Done, "", "Office PC", body,
                                                          {New ExchangeAgentDriveFile("Camera/a.jpg", "C:\cache\a.jpg")})
        Dim job As ExchangeAgentDriveJob = Nothing
        Assert.True(ExchangeAgentDriveJob.TryParse(json, job))
        Assert.Equal("j1", job.JobId)
        Assert.Equal(ExchangeAgentDriveJobState.Done, job.State)
        Assert.Equal("Sub", Assert.Single(job.Listing.Entries).Name)
        Assert.True(job.Listing.Entries(0).IsDirectory)
        Assert.Equal("C:\cache\a.jpg", Assert.Single(job.Files).SavedPath)
        Assert.False(ExchangeAgentDriveJob.TryParse("{""state"":""Done""}", job))
        Assert.False(ExchangeAgentDriveJob.TryParse("{""jobId"":""j"",""state"":""Exploded""}", job))
    End Sub

End Class
