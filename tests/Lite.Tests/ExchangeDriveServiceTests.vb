#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading
Imports Xunit

''' <summary>
''' Specification 203 - the Drive channel as one object: dormant until signed in, one listing per round shared by the
''' directory and the queue, 60 s idle / 10 s active, the back-off ladder of item CQ, the end of the loop when Google stops
''' honouring the token (CT), and the sign-out of criterion 4 in its order (own records first, then the revocation).
''' </summary>
Public Class ExchangeDriveServiceTests
    Implements IDisposable

    Private Const ClientId As String = "test-client.apps.example"
    Private Const ClientSecret As String = "test-secret"
    Private Const IdA As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdB As String = "BBBBBBBBBBBBBBBBBBBBBA"

    Private ReadOnly _temp As New List(Of String)
    Private ReadOnly _services As New List(Of ExchangeDriveService)

    ''' <summary>A wait the test releases by hand; remembers every span the loop asked for.</summary>
    Private NotInheritable Class Gate
        Public ReadOnly Spans As New List(Of TimeSpan)
        Private ReadOnly _sem As New SemaphoreSlim(0)

        Public Async Function Wait(span As TimeSpan, token As CancellationToken) As Task
            SyncLock Spans
                Spans.Add(span)
            End SyncLock
            Await _sem.WaitAsync(token).ConfigureAwait(False)
        End Function

        Public Sub Open()
            _sem.Release()
        End Sub

        Public Function Asked() As List(Of TimeSpan)
            SyncLock Spans
                Return Spans.ToList()
            End SyncLock
        End Function
    End Class

    Private NotInheritable Class Device
        Public Id As String = ""
        Public Seed As String = ""
        Public Store As ExchangeDriveTokenStore
        Public Session As ExchangeDriveSession
        Public Service As ExchangeDriveService
        Public Gate As New Gate()
        Public Updates As Integer
        Public States As New List(Of ExchangeDriveServiceState)
    End Class

    Private Function NewFolder() As String
        Dim path As String = Directory.CreateTempSubdirectory("fmsgd").FullName
        _temp.Add(path)
        Return path
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each service As ExchangeDriveService In _services
            service.Dispose()
        Next
        For Each path As String In _temp
            Try
                Directory.Delete(path, True)
            Catch ex As IOException
            End Try
        Next
    End Sub

    ''' <summary>A PC on the rig: DPAPI token store, session, client, directory, queue and the service over them.</summary>
    Private Function NewDevice(rig As DriveRig, id As String, name As String, signedIn As Boolean) As Device
        Dim device As New Device()
        device.Id = id
        device.Store = New ExchangeDriveTokenStore(Path.Combine(NewFolder(), "exchange"), Nothing)
        device.Seed = "seed-refresh-token-" & id
        If signedIn Then
            rig.Drive.ValidateBearer = True
            rig.Drive.AcceptRefreshToken(device.Seed) ' every PC that signed in holds a refresh token of its own
            Assert.True(device.Store.Save(ClientId, device.Seed))
        End If
        device.Session = New ExchangeDriveSession(rig.Http, device.Store, New ExchangeDriveClientConfig(ClientId, ClientSecret))
        Dim client As ExchangeDriveClient = rig.NewClient(device.Session)
        device.Service = New ExchangeDriveService(device.Session, client, New ExchangeDriveDirectory(client, id),
                                                  New ExchangeDriveQueue(client, id, name, New ExchangeDriveMemoryHandledStore()),
                                                  AddressOf device.Gate.Wait, Function() 0.5)
        AddHandler device.Service.Updated, Sub(sender As Object, e As ExchangeDriveUpdate) Interlocked.Increment(device.Updates)
        AddHandler device.Service.StateChanged, Sub(sender As Object, e As EventArgs)
                                                    SyncLock device.States
                                                        device.States.Add(device.Service.State)
                                                    End SyncLock
                                                End Sub
        _services.Add(device.Service)
        Return device
    End Function

    Private Shared Async Function Until(condition As Func(Of Boolean)) As Task
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(10)
        Do While Not condition()
            If DateTime.UtcNow > deadline Then Throw New TimeoutException("the condition never held")
            Await Task.Delay(10)
        Loop
    End Function

    Private Shared Function DeviceJson(id As String, name As String) As String
        Return "{""deviceId"":""" & id & """,""deviceName"":""" & name & """,""product"":""FastMediaSorter"",""productVersion"":""46"",""platform"":""windows"",""roles"":[""consumer"",""producer""],""capabilities"":[]}"
    End Function

    Private Shared Function ResourceJson(resourceId As String, deviceId As String, name As String) As String
        Return "{""resourceId"":""" & resourceId & """,""deviceId"":""" & deviceId & """,""kind"":""" & ExchangeWire.KindSftpShare & """,""name"":""" & name & """,""access"":{""host"":""192.0.2.10"",""port"":22}}"
    End Function

    ' ------------------------------------------------------------------------------------------ dormant

    <Fact>
    Public Async Function A_PC_that_never_signed_in_touches_nothing() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", False)
        a.Service.Start()
        Assert.Equal(ExchangeDriveServiceState.SignedOut, a.Service.State)
        Assert.Equal(ExchangeDriveFailure.SignedOut, a.Service.LastFailure)
        Await Task.Delay(100)
        Assert.Equal(0, rig.Drive.RequestCount)
        Assert.Empty(a.Gate.Asked())
        Assert.False(Directory.Exists(Path.GetDirectoryName(a.Store.FilePath)))
        Await a.Service.StopAsync()
    End Function

    ' ------------------------------------------------------------------------------------------ the rounds

    <Fact>
    Public Async Function One_round_reads_the_directory_and_the_queue_from_a_single_listing() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        Dim b As Device = NewDevice(rig, IdB, "Laptop", True)
        Dim shareId As String = ExchangeIds.NewId()
        Await a.Service.Records.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        Await a.Service.Records.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, ResourceJson(shareId, IdA, "Photos"), 0, CancellationToken.None)
        Await a.Service.Packets.SendFilesAsync({ExchangeDriveSendSource.FromBytes("x.txt", "text/plain", New Byte() {1, 2, 3})}, IdB, "Laptop", CancellationToken.None)

        Dim lists As Integer = 0
        rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files" AndAlso r.Query.ContainsKey("spaces") AndAlso Interlocked.Increment(lists) < 0, Integer.MaxValue, 500)

        Dim update As ExchangeDriveUpdate = Await b.Service.PollOnceAsync(CancellationToken.None)
        Assert.Equal(1, lists)
        Assert.Contains(update.Snapshot.Snapshot.Devices, Function(d) d.DeviceId = IdA)
        Assert.Single(update.Poll.Packets)
        Assert.Single(b.Service.Packets.IncomingFiles(update.Poll))
    End Function

    <Fact>
    Public Async Function The_loop_polls_every_60_seconds_when_idle_and_every_10_when_a_screen_waits() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        a.Service.Start()
        a.Service.Start() ' a second Start must not start a second loop

        Await Until(Function() a.Gate.Asked().Count = 1)
        Assert.Equal(ExchangeDriveServiceState.Running, a.Service.State)
        Assert.Equal(TimeSpan.FromSeconds(60), a.Gate.Asked()(0))
        Assert.NotNull(a.Service.Latest)
        Await Task.Delay(100)
        Assert.Equal(1, Volatile.Read(a.Updates))

        a.Service.SetActive(True) ' the wait is cut short, the next one is the short interval
        Await Until(Function() a.Gate.Asked().Count = 2)
        Assert.Equal(TimeSpan.FromSeconds(10), a.Gate.Asked()(1))
        Assert.Equal(2, Volatile.Read(a.Updates))

        a.Service.SetActive(False)
        Await Until(Function() a.Gate.Asked().Count = 3)
        Assert.Equal(TimeSpan.FromSeconds(60), a.Gate.Asked()(2))

        Await a.Service.StopAsync()
        Assert.Equal(ExchangeDriveServiceState.Stopped, a.Service.State)
        Dim requests As Integer = rig.Drive.RequestCount
        Await Task.Delay(100)
        Assert.Equal(requests, rig.Drive.RequestCount)
    End Function

    <Fact>
    Public Async Function A_rate_limit_that_outlasts_the_retries_backs_off_1_s_doubling_and_recovers() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 1000, 503)
        a.Service.Start()

        Await Until(Function() a.Gate.Asked().Count = 1)
        Assert.Equal(ExchangeDriveServiceState.Unavailable, a.Service.State)
        Assert.Equal(ExchangeDriveFailure.Unavailable, a.Service.LastFailure)
        Assert.Equal(TimeSpan.FromSeconds(0.75), a.Gate.Asked()(0)) ' 1 s base, jitter 0.5 -> 0.75 s

        a.Gate.Open()
        Await Until(Function() a.Gate.Asked().Count = 2)
        Assert.Equal(TimeSpan.FromSeconds(1.5), a.Gate.Asked()(1))

        rig.Drive.ClearFailures()
        a.Gate.Open()
        Await Until(Function() a.Gate.Asked().Count = 3)
        Assert.Equal(ExchangeDriveServiceState.Running, a.Service.State)
        Assert.Equal(ExchangeDriveFailure.None, a.Service.LastFailure)
        Assert.Equal(TimeSpan.FromSeconds(60), a.Gate.Asked()(2)) ' a good round resumes the interval

        ' ..and the ladder starts over from the bottom
        rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 1000, 503)
        a.Gate.Open()
        Await Until(Function() a.Gate.Asked().Count = 4)
        Assert.Equal(TimeSpan.FromSeconds(0.75), a.Gate.Asked()(3))
        Await a.Service.StopAsync()
    End Function

    <Fact>
    Public Async Function A_full_Drive_or_the_daily_limit_is_not_climbed_up_the_ladder() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 1000, 403, "dailyLimitExceeded")
        a.Service.Start()
        Await Until(Function() a.Gate.Asked().Count = 1)
        Assert.Equal(ExchangeDriveServiceState.Unavailable, a.Service.State)
        Assert.Equal(ExchangeDriveFailure.DailyLimit, a.Service.LastFailure)
        Assert.Equal(TimeSpan.FromSeconds(60), a.Gate.Asked()(0))
        Await a.Service.StopAsync()
    End Function

    <Fact>
    Public Async Function A_token_Google_no_longer_honours_ends_the_loop_and_signs_the_PC_out() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        rig.Drive.RefreshRevoked = True ' revoked at myaccount.google.com while the program was closed
        a.Service.Start()

        Await Until(Function() a.Service.State = ExchangeDriveServiceState.SignedOut)
        Assert.Equal(ExchangeDriveFailure.SignedOut, a.Service.LastFailure)
        Assert.Empty(a.Gate.Asked())
        Assert.False(a.Store.Exists())
        Assert.False(a.Session.IsSignedIn())
        Assert.Equal(0, Volatile.Read(a.Updates))
    End Function

    ' ------------------------------------------------------------------------------------------ criterion 4

    <Fact>
    Public Async Function Signing_out_stops_the_loop_deletes_the_own_records_then_revokes_and_clears_the_token() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        Dim b As Device = NewDevice(rig, IdB, "Laptop", True)
        Dim shareId As String = ExchangeIds.NewId()
        Await a.Service.Records.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        Await a.Service.Records.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, ResourceJson(shareId, IdA, "Photos"), 0, CancellationToken.None)
        Await b.Service.Records.PublishAsync(ExchangeDriveRecordKind.Device, IdB, DeviceJson(IdB, "Laptop"), 0, CancellationToken.None)
        a.Service.Start()
        Await Until(Function() a.Gate.Asked().Count = 1)

        ' criterion 2 holds before the sign-out: the other PC sees A
        Dim before As ExchangeDriveUpdate = Await b.Service.PollOnceAsync(CancellationToken.None)
        Assert.Contains(before.Snapshot.Snapshot.Devices, Function(d) d.DeviceId = IdA)

        Dim result As ExchangeDriveSignOutResult = Await a.Service.SignOutAsync(CancellationToken.None)
        Assert.True(result.RecordsDeleted)
        Assert.Equal(2, result.DeletedCount)
        Assert.True(result.RevocationConfirmed)
        Assert.Equal(ExchangeDriveServiceState.SignedOut, a.Service.State)
        Assert.Null(a.Service.Latest)
        Assert.False(a.Store.Exists())
        Assert.False(a.Session.IsSignedIn())
        Assert.Contains(a.Seed, rig.Drive.Revoked)
        Assert.DoesNotContain(b.Seed, rig.Drive.Revoked)
        Assert.True(b.Session.IsSignedIn()) ' the other PC's sign-in is its own

        ' only B's record is left, and the next PC to look sees no A
        Assert.Equal(1, rig.Drive.Snapshot().Where(Function(f) Not f.IsFolder).Count())
        Dim after As ExchangeDriveUpdate = Await b.Service.PollOnceAsync(CancellationToken.None)
        Assert.DoesNotContain(after.Snapshot.Snapshot.Devices, Function(d) d.DeviceId = IdA)
    End Function

    <Fact>
    Public Async Function Signing_out_with_Drive_unreachable_still_leaves_and_says_the_records_will_lapse() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        Await a.Service.Records.PublishAsync(ExchangeDriveRecordKind.Device, IdA, DeviceJson(IdA, "Office PC"), 0, CancellationToken.None)
        rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 1000, 503)

        Dim result As ExchangeDriveSignOutResult = Await a.Service.SignOutAsync(CancellationToken.None)
        Assert.False(result.RecordsDeleted)
        Assert.True(result.RevocationConfirmed)
        Assert.False(a.Store.Exists())
        Assert.Equal(ExchangeDriveServiceState.SignedOut, a.Service.State)

        ' the record is still there, and unreadable within minutes on Drive's clock (item C)
        rig.Drive.ClearFailures()
        Assert.Equal(1, rig.Drive.Snapshot().Where(Function(f) Not f.IsFolder).Count())
    End Function

    <Fact>
    Public Async Function Signing_out_a_PC_that_never_signed_in_deletes_nothing_and_asks_nobody() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", False)
        Dim result As ExchangeDriveSignOutResult = Await a.Service.SignOutAsync(CancellationToken.None)
        Assert.True(result.RecordsDeleted)
        Assert.Equal(0, result.DeletedCount)
        Assert.False(result.RevocationConfirmed)
        Assert.Equal(0, rig.Drive.RequestCount)
    End Function

    <Fact>
    Public Async Function A_state_change_is_announced_once_per_change() As Task
        Dim rig As New DriveRig()
        Dim a As Device = NewDevice(rig, IdA, "Office PC", True)
        a.Service.Start()
        Await Until(Function() a.Gate.Asked().Count = 1)
        a.Service.PollSoon()
        Await Until(Function() a.Gate.Asked().Count = 2)
        Await a.Service.StopAsync()
        SyncLock a.States
            Assert.Equal(New ExchangeDriveServiceState() {ExchangeDriveServiceState.Running, ExchangeDriveServiceState.Stopped}, a.States.ToArray())
        End SyncLock
    End Function

End Class
#End If
