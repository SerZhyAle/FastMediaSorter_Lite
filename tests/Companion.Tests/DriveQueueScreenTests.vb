Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 269 P3.4 - the queue screen of the Google Drive page (DEVICE-EXCHANGE item CS: the packets waiting in the queue, with
''' their size, each deletable; item CQ: the screen open keeps the poller at 10 s), on the in-memory Drive.
''' </summary>
Public Class DriveQueueScreenTests
    Implements IDisposable

    Private Const IdHere As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdThere As String = "BBBBBBBBBBBBBBBBBBBBBA"
    Private Const IdThird As String = "CCCCCCCCCCCCCCCCCCCCCA"
    Private Const ClientId As String = "queue-test-client.apps.example"
    Private Const ClientSecret As String = "queue-test-secret-value"

    Private ReadOnly _drive As New FakeDriveHandler()
    Private ReadOnly _http As HttpClient
    Private ReadOnly _client As ExchangeDriveClient
    Private ReadOnly _handled As New ExchangeDriveMemoryHandledStore()
    Private ReadOnly _here As ExchangeDriveQueue
    Private ReadOnly _there As ExchangeDriveQueue
    Private ReadOnly _third As ExchangeDriveQueue

    Public Sub New()
        _http = New HttpClient(_drive, False)
        _client = New ExchangeDriveClient(_http, New FakeDriveTokens(), Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
        _here = New ExchangeDriveQueue(_client, IdHere, "Office PC", _handled)
        _there = New ExchangeDriveQueue(_client, IdThere, "Phone", New ExchangeDriveMemoryHandledStore())
        _third = New ExchangeDriveQueue(_client, IdThird, "Laptop", New ExchangeDriveMemoryHandledStore())
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _http.Dispose()
    End Sub

    Private Shared Function File(name As String, size As Integer) As ExchangeDriveSendSource()
        Return New ExchangeDriveSendSource() {ExchangeDriveSendSource.FromBytes(name, "application/octet-stream", New Byte(size - 1) {})}
    End Function

    Private Async Function RowsHereAsync() As Task(Of IReadOnlyList(Of DriveQueueRow))
        Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(CancellationToken.None)
        Dim poll As ExchangeDriveQueuePoll = Await _here.PollAsync(listing, CancellationToken.None)
        Return DriveQueueScreen.Build(poll.Packets, poll.InProgress, poll.Skipped, AddressOf _here.WasTaken).Rows
    End Function

    ' --- the model ---

    <Fact>
    Public Async Function Each_packet_of_the_queue_has_a_row_naming_what_it_is_from_this_PC() As Task
        Assert.True((Await _here.SendFilesAsync(File("mine.bin", 10), IdThere, "Phone", CancellationToken.None)).Succeeded)
        Assert.True((Await _there.SendFilesAsync(File("offered.bin", 20), "", "", CancellationToken.None)).Succeeded)
        Assert.True((Await _there.SendFilesAsync(File("forme.bin", 30), IdHere, "Office PC", CancellationToken.None)).Succeeded)
        Assert.True((Await _there.SendFilesAsync(File("forthird.bin", 40), IdThird, "Laptop", CancellationToken.None)).Succeeded)

        Dim rows As IReadOnlyList(Of DriveQueueRow) = Await RowsHereAsync()

        Assert.Equal(4, rows.Count)
        Assert.Single(rows.Where(Function(r) r.Role = DriveQueueRole.Sent AndAlso r.Packet.DriveBytes = 10))
        Assert.Equal(2, rows.Where(Function(r) r.Role = DriveQueueRole.Waiting).Count())
        Assert.Single(rows.Where(Function(r) r.Role = DriveQueueRole.OtherDevices AndAlso r.Packet.DriveBytes = 40))
    End Function

    <Fact>
    Public Async Function A_packet_this_PC_took_reads_received_and_stays_listed() As Task
        Dim sent As ExchangeDriveSendResult = Await _there.SendFilesAsync(File("offered.bin", 20), "", "", CancellationToken.None)
        Assert.True(sent.Succeeded)
        _handled.Add(sent.PacketId)

        Dim rows As IReadOnlyList(Of DriveQueueRow) = Await RowsHereAsync()

        Assert.Equal(DriveQueueRole.Received, Assert.Single(rows).Role)
    End Function

    <Fact>
    Public Async Function A_request_and_its_reply_are_rows_too_since_the_person_may_delete_any_packet() As Task
        Dim request As ExchangeDriveSendResult = Await _there.SendRequestAsync("{""action"":""list""}", IdHere, "Office PC", CancellationToken.None)
        Assert.True(request.Succeeded)

        Dim row As DriveQueueRow = Assert.Single(Await RowsHereAsync())

        Assert.Equal(DriveQueueRole.Waiting, row.Role)
        Assert.Equal(ExchangeDrivePayloadKind.Request, row.Packet.Manifest.PayloadKind)
        Assert.Equal(0L, row.Packet.DriveBytes)
    End Function

    ' --- the words ---

    <Fact>
    Public Sub The_lines_above_the_list_say_why_there_is_no_list()
        Dim empty As New DriveQueueView(Array.Empty(Of DriveQueueRow)(), 0, 0)
        Assert.Equal(Localization.T("Очередь видна после входа в Google Drive."), DriveChannelText.QueueState(DriveChannelState.SignedOut, ExchangeDriveFailure.None, Nothing))
        Assert.Equal(Localization.T("Очередь видна после входа в Google Drive."), DriveChannelText.QueueState(DriveChannelState.NotConfigured, ExchangeDriveFailure.None, Nothing))
        Assert.Equal(Localization.T("Чтение очереди.."), DriveChannelText.QueueState(DriveChannelState.SignedIn, ExchangeDriveFailure.None, Nothing))
        Assert.Equal(Localization.T("Очередь пуста."), DriveChannelText.QueueState(DriveChannelState.SignedIn, ExchangeDriveFailure.None, empty))
        Assert.Equal(DriveChannelText.FailureDetail(ExchangeDriveFailure.Offline), DriveChannelText.QueueState(DriveChannelState.Unavailable, ExchangeDriveFailure.Offline, Nothing))
    End Sub

    <Fact>
    Public Sub Packets_with_no_row_are_still_counted()
        Assert.Equal("", DriveChannelText.QueueExtras(Nothing))
        Assert.Equal("", DriveChannelText.QueueExtras(New DriveQueueView(Array.Empty(Of DriveQueueRow)(), 0, 0)))
        Dim both As String = DriveChannelText.QueueExtras(New DriveQueueView(Array.Empty(Of DriveQueueRow)(), 2, 3))
        Assert.Contains("2", both)
        Assert.Contains("3", both)
    End Sub

    <Fact>
    Public Sub A_row_names_the_device_from_the_manifest_or_its_short_id()
        Dim targeted As New ExchangeDrivePacketManifest() With {.SenderDeviceName = "Phone", .TargetDeviceName = "Office PC", .TargetDeviceId = IdHere}
        Dim untargeted As New ExchangeDrivePacketManifest() With {.SenderDeviceName = "Phone"}
        Dim noName As New ExchangeDrivePacketManifest() With {.SenderDeviceId = IdThere}
        Assert.Equal(Localization.TF("для «{0}»", "Office PC"), DriveChannelText.QueuePeer(DriveQueueRole.Sent, targeted))
        Assert.Equal(Localization.T("для всех ваших устройств"), DriveChannelText.QueuePeer(DriveQueueRole.Sent, untargeted))
        Assert.Equal(Localization.TF("от «{0}» для «{1}»", "Phone", "Office PC"), DriveChannelText.QueuePeer(DriveQueueRole.OtherDevices, targeted))
        Assert.Equal(Localization.TF("от «{0}»", "Phone"), DriveChannelText.QueuePeer(DriveQueueRole.Waiting, targeted))
        Assert.Equal(Localization.TF("от «{0}»", IdThere.Substring(0, 8) & ".."), DriveChannelText.QueuePeer(DriveQueueRole.Received, noName))
    End Sub

    <Fact>
    Public Sub Every_role_and_kind_has_a_translated_word()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In New String() {"en", "ar", "zh"}
                Localization.CurrentCode = code
                For Each role As DriveQueueRole In [Enum].GetValues(GetType(DriveQueueRole))
                    Assert.DoesNotMatch("[А-Яа-яЁё]", DriveChannelText.QueueRoleWord(role))
                Next
                For Each kind As ExchangeDrivePayloadKind In [Enum].GetValues(GetType(ExchangeDrivePayloadKind))
                    Assert.DoesNotMatch("[А-Яа-яЁё]", DriveChannelText.QueueKindWord(kind))
                Next
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub A_failed_delete_never_promises_a_retry_it_will_not_make()
        Assert.Equal("", DriveChannelText.QueueDeleteFailed(ExchangeDriveFailure.None))
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "ru"
            For Each failure As ExchangeDriveFailure In {ExchangeDriveFailure.Offline, ExchangeDriveFailure.Unavailable, ExchangeDriveFailure.Other}
                Assert.Contains("Повторите попытку.", DriveChannelText.QueueDeleteFailed(failure))
                Assert.DoesNotContain("автоматически", DriveChannelText.QueueDeleteFailed(failure))
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- the host: the view comes from the poller's round, deleting goes to Drive ---

    Private NotInheritable Class Rig
        Implements IDisposable

        Public ReadOnly Folder As String
        Public ReadOnly Host As DriveChannelHost
        Public ReadOnly Service As ExchangeDriveService
        Public Asked As New List(Of TimeSpan)
        Private ReadOnly _store As ExchangeDriveTokenStore
        Private ReadOnly _config As New ExchangeDriveClientConfig(ClientId, ClientSecret)
        Private ReadOnly _http As HttpClient

        Public Sub New(drive As FakeDriveHandler, http As HttpClient)
            _http = http
            Folder = Directory.CreateTempSubdirectory("fmsdrvqueue").FullName
            Dim exchange As String = Path.Combine(Folder, "exchange")
            _store = New ExchangeDriveTokenStore(exchange, Nothing)
            Const seed As String = "seed-refresh-token-for-the-queue-tests"
            drive.AcceptRefreshToken(seed)
            Assert.True(_store.Save(ClientId, seed))
            Dim ids As New DriveDeviceId(exchange, Function() "", Function() IdHere)
            Host = New DriveChannelHost(http, _store, _config, ids, "Office PC", Function(url As String) True, Function() "", AddressOf BuildService)
        End Sub

        Private Function BuildService(id As String, name As String) As ExchangeDriveService
            Dim session As New ExchangeDriveSession(_http, _store, _config)
            Dim client As New ExchangeDriveClient(_http, session, Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
            Return New ExchangeDriveService(session, client, New ExchangeDriveDirectory(client, id),
                                            New ExchangeDriveQueue(client, id, name, New ExchangeDriveMemoryHandledStore()),
                                            AddressOf WaitAsync, Function() 0.5)
        End Function

        Private Function WaitAsync(span As TimeSpan, token As CancellationToken) As Task
            SyncLock Asked
                Asked.Add(span)
            End SyncLock
            Return Task.Delay(Timeout.Infinite, token)
        End Function

        Public Function WaitsSoFar() As List(Of TimeSpan)
            SyncLock Asked
                Return Asked.ToList()
            End SyncLock
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Host.Dispose()
            Try
                Directory.Delete(Folder, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Sub
    End Class

    Private Shared Async Function Until(condition As Func(Of Boolean)) As Task
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(4)
        Do While Not condition()
            If DateTime.UtcNow > deadline Then Throw New TimeoutException("the condition never held")
            Await Task.Delay(5)
        Loop
    End Function

    <Fact>
    Public Async Function Before_a_start_there_is_no_view_and_a_delete_says_so() As Task
        Assert.True((Await _there.SendFilesAsync(File("offered.bin", 20), IdHere, "Office PC", CancellationToken.None)).Succeeded)
        Dim row As DriveQueueRow = Assert.Single(Await RowsHereAsync())
        Using rig As New Rig(_drive, _http)
            Assert.Null(rig.Host.QueueView)
            Assert.Equal(ExchangeDriveFailure.SignedOut, Await rig.Host.DeleteQueuedPacketAsync(row.Packet, CancellationToken.None))
        End Using
        Assert.NotNull(_drive.Find("offered.bin"))
    End Function

    <Fact>
    Public Async Function A_round_fills_the_view_and_a_delete_empties_Drive_and_the_view_at_once() As Task
        Assert.True((Await _there.SendFilesAsync(File("offered.bin", 20), IdHere, "Office PC", CancellationToken.None)).Succeeded)
        Using rig As New Rig(_drive, _http)
            rig.Host.StartIfSignedIn()
            Await Until(Function() rig.Host.QueueView IsNot Nothing)

            Dim row As DriveQueueRow = Assert.Single(rig.Host.QueueView.Rows)
            Assert.Equal(DriveQueueRole.Waiting, row.Role)
            Assert.Equal(20L, row.Packet.DriveBytes)

            Assert.Equal(ExchangeDriveFailure.None, Await rig.Host.DeleteQueuedPacketAsync(row.Packet, CancellationToken.None))

            Assert.Empty(rig.Host.QueueView.Rows)
            Assert.Null(_drive.Find("offered.bin"))
        End Using
    End Function

    <Fact>
    Public Async Function A_delete_Drive_refuses_keeps_the_row_and_names_the_failure() As Task
        Assert.True((Await _there.SendFilesAsync(File("offered.bin", 20), IdHere, "Office PC", CancellationToken.None)).Succeeded)
        Using rig As New Rig(_drive, _http)
            rig.Host.StartIfSignedIn()
            Await Until(Function() rig.Host.QueueView IsNot Nothing)
            Dim row As DriveQueueRow = Assert.Single(rig.Host.QueueView.Rows)
            _drive.FailWhen(Function(r) r.Method = "DELETE", 100000, 503)

            Dim failure As ExchangeDriveFailure = Await rig.Host.DeleteQueuedPacketAsync(row.Packet, CancellationToken.None)

            Assert.NotEqual(ExchangeDriveFailure.None, failure)
            Assert.Single(rig.Host.QueueView.Rows)
            Assert.NotNull(_drive.Find("offered.bin"))
        End Using
    End Function

    <Fact>
    Public Async Function Deleting_an_already_gone_packet_is_no_failure() As Task
        Assert.True((Await _there.SendFilesAsync(File("offered.bin", 20), IdHere, "Office PC", CancellationToken.None)).Succeeded)
        Using rig As New Rig(_drive, _http)
            rig.Host.StartIfSignedIn()
            Await Until(Function() rig.Host.QueueView IsNot Nothing)
            Dim row As DriveQueueRow = Assert.Single(rig.Host.QueueView.Rows)
            Await _there.DeletePacketAsync(row.Packet, CancellationToken.None)

            Assert.Equal(ExchangeDriveFailure.None, Await rig.Host.DeleteQueuedPacketAsync(row.Packet, CancellationToken.None))
        End Using
    End Function

    <Fact>
    Public Async Function The_screen_open_raises_the_poller_to_ten_seconds_and_closing_it_lowers_it_again() As Task
        Using rig As New Rig(_drive, _http)
            rig.Host.StartIfSignedIn()
            Await Until(Function() rig.WaitsSoFar().Count = 1)
            Assert.Equal(TimeSpan.FromSeconds(60), rig.WaitsSoFar()(0))

            rig.Host.SetActive(True)
            Await Until(Function() rig.WaitsSoFar().Count >= 2)
            Assert.Equal(TimeSpan.FromSeconds(10), rig.WaitsSoFar()(1))

            rig.Host.SetActive(False)
            Await Until(Function() rig.WaitsSoFar().Count >= 3)
            Assert.Equal(TimeSpan.FromSeconds(60), rig.WaitsSoFar()(2))
        End Using
    End Function

End Class
