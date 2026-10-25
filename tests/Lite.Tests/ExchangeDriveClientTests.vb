#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports Xunit

''' <summary>A fake Drive, a client on it and the delays the client asked for - the rig every Drive-channel test stands on.</summary>
Friend NotInheritable Class DriveRig

    Public ReadOnly Drive As New FakeDriveHandler()
    Public ReadOnly Http As HttpClient
    Public ReadOnly Tokens As New FakeDriveTokens()
    Public ReadOnly Delays As New List(Of TimeSpan)
    Public ReadOnly Client As ExchangeDriveClient

    Public Sub New()
        Http = New HttpClient(Drive, False)
        Client = NewClient(Tokens)
    End Sub

    ''' <summary>Another device's client on the same Drive.</summary>
    Public Function NewClient(tokens As IExchangeDriveTokens) As ExchangeDriveClient
        Return New ExchangeDriveClient(Http, tokens,
            Function(span As TimeSpan, token As CancellationToken) As Task
                SyncLock Delays
                    Delays.Add(span)
                End SyncLock
                Return Task.CompletedTask
            End Function,
            Function() 0.5)
    End Function

    Public Shared Function Sha256Hex(bytes As Byte()) As String
        Using sha As SHA256 = SHA256.Create()
            Return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant()
        End Using
    End Function

    Public Shared Function RandomBytes(count As Integer, seed As Integer) As Byte()
        Dim bytes(count - 1) As Byte
        Dim generator As New Random(seed)
        generator.NextBytes(bytes)
        Return bytes
    End Function
End Class

''' <summary>
''' Specification 203 sections 3.2 and 3.3 - the Drive v3 calls of the channel against an in-memory Drive: listing and its
''' paging, the server's Date header, create / update / download / delete, the resumable upload with a chunk that fails, the
''' back-off of item CQ, the refusals that are NOT retried (item CS) and the one refresh on a 401.
''' </summary>
Public Class ExchangeDriveClientTests

    <Fact>
    Public Async Function Listing_pages_and_carries_the_servers_clock() As Task
        Dim rig As New DriveRig()
        rig.Drive.PageSize = 2
        For index As Integer = 1 To 5
            rig.Drive.AddFile("f" & index, New Byte() {CByte(index)})
        Next
        Dim listing As ExchangeDriveListing = Await rig.Client.ListAllAsync(CancellationToken.None)
        Assert.Equal(5, listing.Files.Count)
        Assert.Equal(3, rig.Drive.RequestCount)
        Assert.True(listing.HasServerDate)
        Assert.Equal(rig.Drive.ServerNow, listing.ServerDate)
        Assert.Equal(New String() {"f1", "f2", "f3", "f4", "f5"}, listing.Files.Select(Function(f) f.Name).ToArray())
        Assert.Equal(1L, listing.Files(0).Size)
        Assert.True(listing.Files(0).HasParent(FakeDriveHandler.RootId))
    End Function

    <Fact>
    Public Async Function A_small_file_is_created_with_its_content_type_content_and_hidden_marker() As Task
        Dim rig As New DriveRig()
        Dim props As New Dictionary(Of String, String)(StringComparer.Ordinal) From {{"fmsxOwner", "dev"}}
        Dim body As Byte() = DriveRig.RandomBytes(5000, 1)
        Dim created As ExchangeDriveFile = Await rig.Client.CreateSmallFileAsync(ExchangeDriveClient.AppDataFolder, "a.bin", "image/png", body, props, CancellationToken.None)
        Assert.Equal("a.bin", created.Name)
        Assert.Equal(5000L, created.Size)
        Assert.Equal("dev", created.AppProperty("fmsxOwner"))
        Dim stored As FakeDriveFile = rig.Drive.Find("a.bin")
        Assert.Equal(body, stored.Content)
        Assert.Equal("image/png", stored.MimeType)
        Assert.Contains(FakeDriveHandler.RootId, stored.Parents)
    End Function

    <Fact>
    Public Async Function An_update_moves_the_modified_time_on_the_servers_clock() As Task
        Dim rig As New DriveRig()
        Dim created As ExchangeDriveFile = Await rig.Client.CreateSmallFileAsync(ExchangeDriveClient.AppDataFolder, "r.json", "application/json", Encoding.UTF8.GetBytes("{}"), Nothing, CancellationToken.None)
        rig.Drive.Advance(TimeSpan.FromSeconds(100))
        Dim updated As ExchangeDriveFile = Await rig.Client.UpdateSmallFileAsync(created.Id, "application/json", Encoding.UTF8.GetBytes("{""x"":1}"), CancellationToken.None)
        Assert.NotEqual(created.ModifiedTime, updated.ModifiedTime)
        Dim before As DateTimeOffset
        Dim after As DateTimeOffset
        Assert.True(ExchangeDriveRecords.TryParseDriveTime(created.ModifiedTime, before))
        Assert.True(ExchangeDriveRecords.TryParseDriveTime(updated.ModifiedTime, after))
        Assert.Equal(TimeSpan.FromSeconds(100), after - before)
        Assert.Equal("{""x"":1}", Encoding.UTF8.GetString(rig.Drive.Find("r.json").Content))
    End Function

    <Fact>
    Public Async Function A_resumable_upload_survives_a_failed_chunk_and_arrives_whole() As Task
        Dim rig As New DriveRig()
        Dim body As Byte() = DriveRig.RandomBytes(20 * 1024 * 1024, 7)
        rig.Drive.FailWhen(Function(r) r.Method = "PUT" AndAlso r.Path.StartsWith("/upload/resumable/", StringComparison.Ordinal), 1, 503)
        Dim last As Long = 0
        Dim monotonic As Boolean = True
        Using source As New MemoryStream(body, False)
            Dim created As ExchangeDriveFile = Await rig.Client.UploadResumableAsync(ExchangeDriveClient.AppDataFolder, "big.bin", "video/mp4", source, CancellationToken.None,
                Sub(done As Long)
                    If done < last Then monotonic = False
                    last = done
                End Sub)
            Assert.Equal(body.LongLength, created.Size)
        End Using
        Assert.Equal(DriveRig.Sha256Hex(body), DriveRig.Sha256Hex(rig.Drive.Find("big.bin").Content))
        Assert.Equal(body.LongLength, last)
        Assert.True(monotonic)
        Assert.Single(rig.Delays)
    End Function

    <Fact>
    Public Async Function Download_returns_the_content_and_a_bounded_read_refuses_more() As Task
        Dim rig As New DriveRig()
        Dim stored As FakeDriveFile = rig.Drive.AddFile("d.bin", DriveRig.RandomBytes(100, 3))
        Dim all As Byte() = Await rig.Client.DownloadBytesAsync(stored.Id, 1000, CancellationToken.None)
        Assert.Equal(stored.Content, all)
        Dim failure As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() rig.Client.DownloadBytesAsync(stored.Id, 50, CancellationToken.None))
        Assert.Equal(ExchangeDriveFailure.Other, failure.Failure)
    End Function

    <Fact>
    Public Async Function Delete_takes_a_folder_with_its_files_and_a_missing_file_is_a_success() As Task
        Dim rig As New DriveRig()
        Dim folder As FakeDriveFile = rig.Drive.AddFolder("p")
        rig.Drive.AddFile("a", New Byte() {1}, folder.Id)
        rig.Drive.AddFile("b", New Byte() {2}, folder.Id)
        Await rig.Client.DeleteAsync(folder.Id, CancellationToken.None)
        Assert.Empty(rig.Drive.Snapshot())
        Await rig.Client.DeleteAsync(folder.Id, CancellationToken.None)
    End Function

    <Fact>
    Public Async Function The_quota_is_read_and_a_plan_without_a_limit_reads_as_zero() As Task
        Dim rig As New DriveRig()
        rig.Drive.QuotaLimit = 1000
        rig.Drive.QuotaUsage = 250
        Dim quota As ExchangeDriveQuota = Await rig.Client.GetStorageQuotaAsync(CancellationToken.None)
        Assert.Equal(1000L, quota.LimitBytes)
        Assert.Equal(250L, quota.UsageBytes)
        rig.Drive.QuotaLimit = 0
        quota = Await rig.Client.GetStorageQuotaAsync(CancellationToken.None)
        Assert.Equal(0L, quota.LimitBytes)
    End Function

    <Fact>
    Public Async Function A_rate_limit_is_retried_with_the_exponential_jittered_back_off() As Task
        Dim rig As New DriveRig()
        rig.Drive.FailWhen(Function(r) r.Path = "/drive/v3/files", 2, 429)
        Dim listing As ExchangeDriveListing = Await rig.Client.ListAllAsync(CancellationToken.None)
        Assert.Empty(listing.Files)
        Assert.Equal(2, rig.Delays.Count)
        Assert.Equal(TimeSpan.FromSeconds(0.75), rig.Delays(0))
        Assert.Equal(TimeSpan.FromSeconds(1.5), rig.Delays(1))
    End Function

    <Fact>
    Public Async Function A_403_with_a_rate_limit_reason_is_retried_and_a_server_error_that_never_ends_gives_up() As Task
        Dim rig As New DriveRig()
        rig.Drive.FailWhen(Function(r) r.Path = "/drive/v3/files", 1, 403, "userRateLimitExceeded")
        Await rig.Client.ListAllAsync(CancellationToken.None)
        Assert.Single(rig.Delays)

        rig.Delays.Clear()
        rig.Drive.FailWhen(Function(r) r.Path = "/drive/v3/files", 100, 503)
        Dim failure As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() rig.Client.ListAllAsync(CancellationToken.None))
        Assert.Equal(ExchangeDriveFailure.Unavailable, failure.Failure)
        Assert.Equal(5, rig.Delays.Count)
    End Function

    <Fact>
    Public Async Function The_daily_limit_and_a_full_drive_are_never_retried() As Task
        Dim rig As New DriveRig()
        rig.Drive.FailWhen(Function(r) r.Path = "/drive/v3/files", 100, 403, "dailyLimitExceeded")
        Dim daily As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() rig.Client.ListAllAsync(CancellationToken.None))
        Assert.Equal(ExchangeDriveFailure.DailyLimit, daily.Failure)
        Assert.Equal(1, rig.Drive.RequestCount)
        Assert.Empty(rig.Delays)

        rig.Drive.ClearFailures()
        rig.Drive.FailWhen(Function(r) r.Path = "/drive/v3/files", 100, 403, "storageQuotaExceeded")
        Dim full As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() rig.Client.ListAllAsync(CancellationToken.None))
        Assert.Equal(ExchangeDriveFailure.DriveFull, full.Failure)
        Assert.Empty(rig.Delays)
    End Function

    <Fact>
    Public Async Function A_401_refreshes_the_token_once_and_succeeds() As Task
        Dim rig As New DriveRig()
        rig.Drive.ValidateBearer = True
        rig.Drive.AcceptedBearers.Add("new")
        rig.Tokens.Token = "old"
        rig.Tokens.NextToken = "new"
        Dim listing As ExchangeDriveListing = Await rig.Client.ListAllAsync(CancellationToken.None)
        Assert.Empty(listing.Files)
        Assert.Equal(1, rig.Tokens.InvalidateCount)
    End Function

    <Fact>
    Public Async Function A_401_that_a_refresh_does_not_cure_means_signed_out_and_is_not_looped() As Task
        Dim rig As New DriveRig()
        rig.Drive.FailWhen(Function(r) True, 100, 401, "authError")
        Dim failure As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() rig.Client.ListAllAsync(CancellationToken.None))
        Assert.Equal(ExchangeDriveFailure.SignedOut, failure.Failure)
        Assert.Equal(2, rig.Drive.RequestCount)
    End Function

    <Fact>
    Public Async Function No_token_means_signed_out_without_any_request() As Task
        Dim rig As New DriveRig()
        rig.Tokens.Token = ""
        Dim failure As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() rig.Client.ListAllAsync(CancellationToken.None))
        Assert.Equal(ExchangeDriveFailure.SignedOut, failure.Failure)
        Assert.Equal(0, rig.Drive.RequestCount)
    End Function

    <Fact>
    Public Sub A_failure_carries_the_kind_and_status_but_never_a_body()
        Dim failure As New ExchangeDriveException(ExchangeDriveFailure.DriveFull, 403)
        Assert.Contains("DriveFull", failure.Message)
        Assert.Contains("403", failure.Message)
        Assert.Equal(ExchangeDriveFailure.DriveFull, ExchangeDriveFailures.Classify(403, "storageQuotaExceeded"))
        Assert.Equal("rateLimitExceeded", ExchangeDriveFailures.ReasonOf("{""error"":{""errors"":[{""reason"":""rateLimitExceeded""}]}}"))
    End Sub

End Class
#End If
