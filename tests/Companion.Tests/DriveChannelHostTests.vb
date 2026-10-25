Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Specification 203 section 5.10 (T-UI-1) - the state machine behind the Google Drive sign-in page, against the in-memory Drive:
''' not configured, signed out, signing in, cancelled, signed in, signed out; a refusal is a plain reason (never an exception
''' text, a status, the client or a token); nothing asks Google or writes a file because the application started or a page was
''' read; the poller runs only while signed in and ends with the sign-out and the disposal.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class DriveChannelHostTests

    Private Const ClientId As String = "host-test-client.apps.example"
    Private Const ClientSecret As String = "host-test-secret-value"
    Private Const IdServer As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdFresh As String = "BBBBBBBBBBBBBBBBBBBBBA"

    ' ------------------------------------------------------------------------------------------ the rig

    ''' <summary>A wait the test releases by hand; remembers every span the poller asked for (one entry per finished round).</summary>
    Private NotInheritable Class WaitGate
        Private ReadOnly _spans As New List(Of TimeSpan)
        Private ReadOnly _sem As New SemaphoreSlim(0)

        Public Async Function Wait(span As TimeSpan, token As CancellationToken) As Task
            SyncLock _spans
                _spans.Add(span)
            End SyncLock
            Await _sem.WaitAsync(token).ConfigureAwait(False)
        End Function

        Public Sub Open()
            _sem.Release()
        End Sub

        Public Function Asked() As List(Of TimeSpan)
            SyncLock _spans
                Return _spans.ToList()
            End SyncLock
        End Function
    End Class

    ''' <summary>What the person's browser does: opens the URL and, a moment later, lands on the loopback redirect.</summary>
    Private NotInheritable Class BrowserSim
        Public Reply As Func(Of System.Collections.Specialized.NameValueCollection, String) = Function(query) "code=auth-code-1&state=" & Uri.EscapeDataString(query("state"))
        ''' <summary>The browser opens and the person never answers.</summary>
        Public Silent As Boolean
        ''' <summary>There is no browser to open.</summary>
        Public Missing As Boolean
        Public Opened As Integer
        Public Done As Task = Task.CompletedTask

        Public Function Open(authorizationUrl As String) As Boolean
            If Missing Then Return False
            Interlocked.Increment(Opened)
            If Silent Then Return True
            Dim parsed = System.Web.HttpUtility.ParseQueryString(New Uri(authorizationUrl).Query)
            Dim redirect As String = parsed("redirect_uri")
            Dim answer As String = Reply(parsed)
            Done = Task.Run(
                Async Function() As Task
                    Using http As New HttpClient()
                        Using landing As HttpResponseMessage = Await http.GetAsync(redirect & "?" & answer)
                            Assert.True(landing.IsSuccessStatusCode)
                        End Using
                    End Using
                End Function)
            Return True
        End Function
    End Class

    Private NotInheritable Class OfflineHandler
        Inherits HttpMessageHandler

        Protected Overrides Function SendAsync(request As HttpRequestMessage, cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)
            Throw New HttpRequestException("no network")
        End Function
    End Class

    ''' <summary>A PC on the fake Drive: its own folder for the token and the id, a host over them and the counters the tests read.</summary>
    Private NotInheritable Class Rig
        Implements IDisposable

        Public ReadOnly Drive As New FakeDriveHandler()
        Public ReadOnly Http As HttpClient
        Public ReadOnly Folder As String
        Public ReadOnly ExchangeFolder As String
        Public ReadOnly Store As ExchangeDriveTokenStore
        Public ReadOnly Config As ExchangeDriveClientConfig
        Public ReadOnly Ids As DriveDeviceId
        Public ReadOnly Gate As New WaitGate()
        Public ReadOnly Browser As New BrowserSim()
        Public ReadOnly Seen As New List(Of DriveChannelState)
        Public Created As Integer
        Private ReadOnly _hosts As New List(Of DriveChannelHost)

        Public Sub New(Optional configured As Boolean = True, Optional handler As HttpMessageHandler = Nothing, Optional serverId As String = "")
            Folder = Directory.CreateTempSubdirectory("fmsdrvhost").FullName
            ExchangeFolder = Path.Combine(Folder, "exchange")
            Http = New HttpClient(If(handler, CType(Drive, HttpMessageHandler)), False)
            Store = New ExchangeDriveTokenStore(ExchangeFolder, Nothing)
            Config = If(configured, New ExchangeDriveClientConfig(ClientId, ClientSecret), New ExchangeDriveClientConfig("", ""))
            Ids = New DriveDeviceId(ExchangeFolder, Function() serverId, Function() IdFresh)
        End Sub

        ''' <summary>The service on the same stack the real host builds, except the waits (counted by <see cref="Gate"/>) and the handled store (memory).</summary>
        Public Function BuildService(id As String, name As String) As ExchangeDriveService
            Interlocked.Increment(Created)
            Dim session As New ExchangeDriveSession(Http, Store, Config)
            Dim client As New ExchangeDriveClient(Http, session, Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
            Return New ExchangeDriveService(session, client, New ExchangeDriveDirectory(client, id),
                                            New ExchangeDriveQueue(client, id, name, New ExchangeDriveMemoryHandledStore()),
                                            AddressOf Gate.Wait, Function() 0.5)
        End Function

        Public Function NewHost(Optional create As Func(Of String, String, ExchangeDriveService) = Nothing) As DriveChannelHost
            Dim factory As Func(Of String, String, ExchangeDriveService) = If(create, New Func(Of String, String, ExchangeDriveService)(AddressOf BuildService))
            Dim host As New DriveChannelHost(Http, Store, Config, Ids, "Office PC", AddressOf Browser.Open, Function() "<html>ok</html>", factory)
            AddHandler host.Changed,
                Sub(sender As Object, e As EventArgs)
                    SyncLock Seen
                        Seen.Add(host.State)
                    End SyncLock
                End Sub
            _hosts.Add(host)
            Return host
        End Function

        ''' <summary>A PC that signed in at an earlier start: a refresh token the fake Drive honours, kept under DPAPI.</summary>
        Public Sub StoreSignIn()
            Const seed As String = "seed-refresh-token-for-the-host-tests"
            Drive.AcceptRefreshToken(seed)
            Assert.True(Store.Save(ClientId, seed))
        End Sub

        Public Function SeenStates() As List(Of DriveChannelState)
            SyncLock Seen
                Return Seen.ToList()
            End SyncLock
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            For Each host As DriveChannelHost In _hosts
                host.Dispose()
            Next
            Http.Dispose()
            Try
                Directory.Delete(Folder, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Sub
    End Class

    Private Shared Async Function Until(condition As Func(Of Boolean)) As Task
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
        Do While Not condition()
            If DateTime.UtcNow > deadline Then Throw New TimeoutException("the condition never held")
            Await Task.Delay(10)
        Loop
    End Function

    Private Shared Function DeviceJson(id As String, name As String) As String
        Return "{""deviceId"":""" & id & """,""deviceName"":""" & name & """,""product"":""FastMediaSorter"",""productVersion"":""46"",""platform"":""windows"",""roles"":[""consumer"",""producer""],""capabilities"":[]}"
    End Function

    ''' <summary>A sentence for a person: not empty, and never an exception, a URL, a status, the client or a token.</summary>
    Private Shared Sub AssertPlain(text As String)
        Assert.False(String.IsNullOrWhiteSpace(text))
        For Each forbidden As String In New String() {ClientId, ClientSecret, "Exception", "HTTP", "://", "Bearer", "System.", "seed-refresh-token"}
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase)
        Next
    End Sub

    ''' <summary>Signs in through the host and waits for the first poll round to finish.</summary>
    Private Shared Async Function SignedInHost(rig As Rig) As Task(Of DriveChannelHost)
        Dim host As DriveChannelHost = rig.NewHost()
        Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
        Assert.True(outcome.HasValue)
        Assert.Equal(ExchangeDriveSignInOutcome.Signed, outcome.Value)
        Await rig.Browser.Done
        Await Until(Function() rig.Gate.Asked().Count >= 1)
        Return host
    End Function

    ' ------------------------------------------------------------------------------------------ no client

    <Fact>
    Public Sub A_build_with_no_client_in_the_environment_or_the_metadata_is_not_configured()
        Dim idName As String = ExchangeDriveClientConfig.ClientIdVariable
        Dim secretName As String = ExchangeDriveClientConfig.ClientSecretVariable
        Dim oldId As String = Environment.GetEnvironmentVariable(idName)
        Dim oldSecret As String = Environment.GetEnvironmentVariable(secretName)
        Try
            Environment.SetEnvironmentVariable(idName, Nothing)
            Environment.SetEnvironmentVariable(secretName, Nothing)
            ' this assembly carries no FmsDriveClientId / FmsDriveClientSecret metadata yet (specification 203 section 5.9)
            Assert.False(ExchangeDriveClientConfig.Resolve().IsConfigured)

            Environment.SetEnvironmentVariable(idName, "x")
            Assert.False(ExchangeDriveClientConfig.Resolve().IsConfigured) ' an id alone is not a client
            Environment.SetEnvironmentVariable(secretName, "y")
            Assert.True(ExchangeDriveClientConfig.Resolve().IsConfigured)
        Finally
            Environment.SetEnvironmentVariable(idName, oldId)
            Environment.SetEnvironmentVariable(secretName, oldSecret)
        End Try
    End Sub

    <Fact>
    Public Async Function Without_a_client_the_state_is_not_configured_and_Sign_in_does_nothing() As Task
        Using rig As New Rig(configured:=False)
            Dim host As DriveChannelHost = rig.NewHost()
            Assert.False(host.IsConfigured)
            Assert.Equal(DriveChannelState.NotConfigured, host.State)
            Assert.Equal(ExchangeDriveFailure.None, host.LastFailure)

            Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
            Assert.Equal(ExchangeDriveSignInOutcome.NotConfigured, outcome.Value)
            host.StartIfSignedIn()

            Assert.Equal(DriveChannelState.NotConfigured, host.State)
            Assert.Equal(0, rig.Browser.Opened)
            Assert.Equal(0, rig.Created)
            Assert.Equal(0, rig.Drive.RequestCount)
            Assert.False(Directory.Exists(rig.ExchangeFolder))
            AssertPlain(DriveChannelText.StateLine(host.State))
            AssertPlain(DriveChannelText.StateDetail(host.State, host.LastFailure))
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ nothing happens by itself

    <Fact>
    Public Sub Building_reading_and_starting_a_host_with_no_stored_sign_in_asks_nobody_and_writes_nothing()
        Using rig As New Rig()
            Dim host As DriveChannelHost = rig.NewHost()
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Assert.Equal(ExchangeDriveFailure.None, host.LastFailure)
            Assert.False(host.LastOutcome.HasValue)
            Assert.Null(host.LastSignOut)
            Assert.Equal("", host.DeviceId)
            Assert.Null(host.Service)

            host.StartIfSignedIn()
            host.StartIfSignedIn()
            host.SetActive(True)

            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Assert.Null(host.Service)
            Assert.Equal(0, rig.Created)
            Assert.Equal(0, rig.Drive.RequestCount)
            Assert.Empty(rig.Gate.Asked())
            Assert.Equal(0, rig.Browser.Opened)
            Assert.False(Directory.Exists(rig.ExchangeFolder))
            Assert.Equal("", rig.Ids.Peek())
        End Using
    End Sub

    <Fact>
    Public Sub The_application_start_does_not_even_build_a_host_when_no_token_file_exists()
        Dim before As Func(Of Boolean) = DriveChannelHosts.TokenPresent
        DriveChannelHosts.Release()
        Try
            DriveChannelHosts.TokenPresent = Function() False
            DriveChannelHosts.StartIfSignedIn()
            Assert.False(DriveChannelHosts.IsCreated())
        Finally
            DriveChannelHosts.TokenPresent = before
            DriveChannelHosts.Release()
        End Try
    End Sub

    <Fact>
    Public Sub The_process_host_is_released_at_exit_and_a_released_host_is_not_created_by_releasing()
        Using rig As New Rig()
            DriveChannelHosts.Release()
            Dim host As DriveChannelHost = rig.NewHost()
            DriveChannelHosts.Use(host)
            Try
                Assert.True(DriveChannelHosts.IsCreated())
                Assert.Same(host, DriveChannelHosts.Current())
                DriveChannelHosts.Release()
                Assert.False(DriveChannelHosts.IsCreated())
                DriveChannelHosts.Release()
                Assert.False(DriveChannelHosts.IsCreated())
                Assert.Equal(0, rig.Drive.RequestCount)
            Finally
                DriveChannelHosts.Release()
            End Try
        End Using
    End Sub

    ' ------------------------------------------------------------------------------------------ sign in

    <Theory>
    <InlineData("")>
    <InlineData(IdServer)>
    Public Async Function Signing_in_stores_the_sign_in_builds_the_service_and_starts_the_poller(serverId As String) As Task
        Using rig As New Rig(serverId:=serverId)
            Dim host As DriveChannelHost = rig.NewHost()
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Assert.Null(host.Service)

            Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
            Assert.Equal(ExchangeDriveSignInOutcome.Signed, outcome.Value)
            Await rig.Browser.Done

            ' one device: the server account's id when there is one, else the random one kept beside the token
            Dim expectedId As String = If(serverId.Length > 0, serverId, IdFresh)
            Assert.Equal(expectedId, host.DeviceId)
            Assert.Equal(expectedId, rig.Ids.Peek())
            Assert.True(File.Exists(Path.Combine(rig.ExchangeFolder, DriveDeviceId.FileName)))
            Assert.True(rig.Store.Exists())
            Assert.Equal(DriveChannelState.SignedIn, host.State)
            Assert.Equal(ExchangeDriveSignInOutcome.Signed, host.LastOutcome.Value)
            Assert.False(host.LastOutcomeWasCancelled)
            Assert.Equal("", DriveChannelText.SignInOutcome(host.LastOutcome.Value, False))
            Assert.NotNull(host.Service)
            Assert.Equal(1, rig.Created)

            ' the poller ran its first round and now waits for the next one
            Await Until(Function() rig.Gate.Asked().Count = 1)
            Assert.Equal(TimeSpan.FromSeconds(60), rig.Gate.Asked()(0))
            Assert.Equal(ExchangeDriveServiceState.Running, host.Service.State)
            Assert.Equal(DriveChannelState.SignedIn, host.State)
            Assert.Equal(ExchangeDriveFailure.None, host.LastFailure)

            ' the page was told: the wait for the browser and the result were both announced
            Dim seen As List(Of DriveChannelState) = rig.SeenStates()
            Assert.Contains(DriveChannelState.SigningIn, seen)
            Assert.Contains(DriveChannelState.SignedIn, seen)
            AssertPlain(DriveChannelText.DeviceLine(host.DeviceName, host.DeviceId))
        End Using
    End Function

    <Fact>
    Public Async Function A_sign_in_the_person_cancels_ends_quietly_and_keeps_nothing() As Task
        Using rig As New Rig()
            rig.Browser.Silent = True
            Dim host As DriveChannelHost = rig.NewHost()
            Dim attempt As Task(Of ExchangeDriveSignInOutcome?) = host.SignInAsync()
            Assert.Equal(DriveChannelState.SigningIn, host.State)
            AssertPlain(DriveChannelText.StateLine(host.State))
            AssertPlain(DriveChannelText.StateDetail(host.State, host.LastFailure))

            Await Until(Function() rig.Browser.Opened = 1)
            Dim started As DateTime = DateTime.UtcNow
            host.CancelSignIn()
            Dim outcome As ExchangeDriveSignInOutcome? = Await attempt
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10))

            Assert.Equal(ExchangeDriveSignInOutcome.TimedOut, outcome.Value)
            Assert.True(host.LastOutcomeWasCancelled)
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Assert.False(rig.Store.Exists())
            Assert.Null(host.Service)
            Assert.Equal(0, rig.Created)
            Assert.Equal(0, rig.Drive.RequestCount)
            AssertPlain(DriveChannelText.SignInOutcome(outcome.Value, host.LastOutcomeWasCancelled))
            Assert.NotEqual(DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.TimedOut, True), DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.TimedOut, False))

            ' a cancel with nothing to cancel is nothing; the page can try again at once
            host.CancelSignIn()
            Assert.Equal(DriveChannelState.SignedOut, host.State)
        End Using
    End Function

    <Fact>
    Public Async Function A_refusal_is_a_plain_reason_and_leaves_the_PC_signed_out() As Task
        Dim cases As New List(Of Tuple(Of String, ExchangeDriveSignInOutcome, Action(Of Rig)))
        cases.Add(Tuple.Create("a wider grant", ExchangeDriveSignInOutcome.ScopeMismatch,
                               New Action(Of Rig)(Sub(r) r.Drive.GrantedScope = "https://www.googleapis.com/auth/drive")))
        cases.Add(Tuple.Create("consent turned down", ExchangeDriveSignInOutcome.ConsentDenied,
                               New Action(Of Rig)(Sub(r) r.Browser.Reply = Function(q) "error=access_denied&state=" & Uri.EscapeDataString(q("state")))))
        cases.Add(Tuple.Create("no refresh token", ExchangeDriveSignInOutcome.TokenRefused,
                               New Action(Of Rig)(Sub(r) r.Drive.IssueRefreshToken = False)))
        cases.Add(Tuple.Create("no browser", ExchangeDriveSignInOutcome.NoBrowser,
                               New Action(Of Rig)(Sub(r) r.Browser.Missing = True)))

        For Each item As Tuple(Of String, ExchangeDriveSignInOutcome, Action(Of Rig)) In cases
            Using rig As New Rig()
                item.Item3.Invoke(rig)
                Dim host As DriveChannelHost = rig.NewHost()
                Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
                Await rig.Browser.Done

                Assert.True(outcome.HasValue, item.Item1)
                Assert.Equal(item.Item2, outcome.Value)
                Assert.Equal(item.Item2, host.LastOutcome.Value)
                Assert.Equal(DriveChannelState.SignedOut, host.State)
                Assert.False(rig.Store.Exists(), item.Item1)
                Assert.Null(host.Service)
                Assert.Equal(0, rig.Created)
                AssertPlain(DriveChannelText.SignInOutcome(outcome.Value, host.LastOutcomeWasCancelled))
            End Using
        Next
    End Function

    <Fact>
    Public Async Function A_sign_in_with_no_network_says_so_in_words() As Task
        Using rig As New Rig(handler:=New OfflineHandler())
            Dim host As DriveChannelHost = rig.NewHost()
            Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
            Await rig.Browser.Done

            Assert.Equal(ExchangeDriveSignInOutcome.Offline, outcome.Value)
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Assert.False(rig.Store.Exists())
            Assert.Null(host.Service)
            AssertPlain(DriveChannelText.SignInOutcome(outcome.Value, False))
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ sign out

    <Fact>
    Public Async Function Signing_out_deletes_the_own_records_revokes_removes_the_file_and_stops_the_poller() As Task
        Using rig As New Rig()
            Dim host As DriveChannelHost = Await SignedInHost(rig)
            Await host.Service.Records.PublishAsync(ExchangeDriveRecordKind.Device, host.DeviceId, DeviceJson(host.DeviceId, "Office PC"), 0, CancellationToken.None)
            Assert.Equal(1, rig.Drive.Snapshot().Where(Function(f) Not f.IsFolder).Count())
            Dim refreshToken As String = rig.Drive.LastRefreshToken
            Assert.NotEqual("", refreshToken)

            Dim result As ExchangeDriveSignOutResult = Await host.SignOutAsync()
            Assert.Contains(DriveChannelState.SigningOut, rig.SeenStates()) ' announced synchronously, before any request

            Assert.True(result.RecordsDeleted)
            Assert.Equal(1, result.DeletedCount)
            Assert.True(result.RevocationConfirmed)
            Assert.Same(result, host.LastSignOut)
            Assert.Contains(refreshToken, rig.Drive.Revoked)
            Assert.False(rig.Store.Exists())
            Assert.Empty(rig.Drive.Snapshot().Where(Function(f) Not f.IsFolder))
            Assert.Null(host.Service)
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Assert.Equal(ExchangeDriveFailure.None, host.LastFailure)
            AssertPlain(DriveChannelText.SignOutOutcome(result))

            ' the poller is over: asking for the short interval reaches nobody and no request follows
            Dim requests As Integer = rig.Drive.RequestCount
            host.SetActive(True)
            Await Task.Delay(150)
            Assert.Equal(requests, rig.Drive.RequestCount)
            Assert.Equal(1, rig.Gate.Asked().Count)

            ' leaving twice is harmless and asks nobody
            Dim again As ExchangeDriveSignOutResult = Await host.SignOutAsync()
            Assert.True(again.RecordsDeleted)
            Assert.True(again.RevocationConfirmed)
            Assert.Equal(0, again.DeletedCount)
            Assert.Equal(requests, rig.Drive.RequestCount)

            ' and the PC can sign in again: a new service, a new poller
            Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
            Assert.Equal(ExchangeDriveSignInOutcome.Signed, outcome.Value)
            Await rig.Browser.Done
            Assert.Equal(DriveChannelState.SignedIn, host.State)
            Assert.Equal(2, rig.Created)
            Assert.Null(host.LastSignOut)
            Await Until(Function() rig.Gate.Asked().Count = 2)
        End Using
    End Function

    <Fact>
    Public Async Function Signing_out_with_Drive_unreachable_still_leaves_and_the_result_says_what_was_not_confirmed() As Task
        Using rig As New Rig()
            Dim host As DriveChannelHost = Await SignedInHost(rig)
            Await host.Service.Records.PublishAsync(ExchangeDriveRecordKind.Device, host.DeviceId, DeviceJson(host.DeviceId, "Office PC"), 0, CancellationToken.None)
            rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 1000, 503)

            Dim result As ExchangeDriveSignOutResult = Await host.SignOutAsync()

            Assert.False(result.RecordsDeleted)
            Assert.True(result.RevocationConfirmed)
            Assert.False(rig.Store.Exists())
            Assert.Null(host.Service)
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            AssertPlain(DriveChannelText.SignOutOutcome(result))
            AssertPlain(DriveChannelText.SignOutOutcome(New ExchangeDriveSignOutResult(True, 0, False)))
            AssertPlain(DriveChannelText.SignOutOutcome(New ExchangeDriveSignOutResult(False, 0, False)))
        End Using
    End Function

    <Fact>
    Public Async Function A_surprise_while_signing_out_never_escapes_and_the_file_still_goes() As Task
        Using rig As New Rig()
            rig.StoreSignIn()
            Dim host As DriveChannelHost = rig.NewHost(Function(id As String, name As String) As ExchangeDriveService
                                                           Throw New InvalidOperationException("detail-that-must-not-reach-the-person")
                                                       End Function)

            Dim result As ExchangeDriveSignOutResult = Await host.SignOutAsync()

            Assert.NotNull(result)
            Assert.False(result.RecordsDeleted)
            Assert.False(result.RevocationConfirmed)
            Assert.False(rig.Store.Exists())
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Dim text As String = DriveChannelText.SignOutOutcome(result)
            AssertPlain(text)
            Assert.DoesNotContain("detail-that-must-not-reach-the-person", text)
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ the poller

    <Fact>
    Public Async Function With_a_stored_sign_in_the_start_runs_one_poller_and_Dispose_ends_it() As Task
        Using rig As New Rig()
            rig.StoreSignIn()
            Dim host As DriveChannelHost = rig.NewHost()
            Assert.Equal(DriveChannelState.SignedIn, host.State) ' stored, not yet started: reading it asks nobody
            Assert.Equal(0, rig.Drive.RequestCount)
            Assert.Equal(0, rig.Created)

            host.StartIfSignedIn()
            host.StartIfSignedIn() ' a second start changes nothing
            Assert.Equal(1, rig.Created)
            Assert.Equal(IdFresh, host.DeviceId)
            Await Until(Function() rig.Gate.Asked().Count = 1)
            Assert.Equal(ExchangeDriveServiceState.Running, host.Service.State)
            Assert.Equal(DriveChannelState.SignedIn, host.State)

            host.SetActive(True) ' a screen waits: the next round is the short interval
            Await Until(Function() rig.Gate.Asked().Count = 2)
            Assert.Equal(TimeSpan.FromSeconds(10), rig.Gate.Asked()(1))

            Dim service As ExchangeDriveService = host.Service
            host.Dispose()
            Assert.Null(host.Service)
            Dim requests As Integer = rig.Drive.RequestCount
            rig.Gate.Open()
            service.PollSoon()
            Await Task.Delay(150)
            Assert.Equal(requests, rig.Drive.RequestCount)
            Assert.Equal(2, rig.Gate.Asked().Count)
            Assert.True(rig.Store.Exists(), "closing the program keeps the sign-in")

            ' a disposed host starts nothing and signs nobody in
            host.StartIfSignedIn()
            Assert.Equal(1, rig.Created)
            Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
            Assert.False(outcome.HasValue)
            host.Dispose()
        End Using
    End Function

    <Fact>
    Public Async Function A_token_Google_no_longer_honours_ends_the_poller_and_the_page_offers_the_sign_in_again() As Task
        Using rig As New Rig()
            rig.StoreSignIn()
            rig.Drive.RefreshRevoked = True ' revoked at myaccount.google.com while the program was closed
            Dim host As DriveChannelHost = rig.NewHost()
            host.StartIfSignedIn()

            Await Until(Function() host.Service IsNot Nothing AndAlso host.Service.State = ExchangeDriveServiceState.SignedOut)
            Assert.Equal(DriveChannelState.SignedOut, host.State)
            Assert.Equal(ExchangeDriveFailure.SignedOut, host.LastFailure)
            Assert.False(rig.Store.Exists())
            Assert.Empty(rig.Gate.Asked()) ' the loop ended before its first wait
            AssertPlain(DriveChannelText.StateDetail(host.State, host.LastFailure))
            Assert.NotEqual(DriveChannelText.StateDetail(DriveChannelState.SignedOut, ExchangeDriveFailure.None), DriveChannelText.StateDetail(host.State, host.LastFailure))
        End Using
    End Function

    <Fact>
    Public Async Function A_round_that_fails_shows_as_unavailable_with_a_reason_and_recovers() As Task
        Using rig As New Rig()
            rig.StoreSignIn()
            rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 1000, 503)
            Dim host As DriveChannelHost = rig.NewHost()
            host.StartIfSignedIn()

            Await Until(Function() rig.Gate.Asked().Count = 1)
            Assert.Equal(DriveChannelState.Unavailable, host.State)
            Assert.Equal(ExchangeDriveFailure.Unavailable, host.LastFailure)
            AssertPlain(DriveChannelText.StateDetail(host.State, host.LastFailure))

            rig.Drive.ClearFailures()
            rig.Gate.Open()
            Await Until(Function() rig.Gate.Asked().Count = 2)
            Assert.Equal(DriveChannelState.SignedIn, host.State)
            Assert.Equal(ExchangeDriveFailure.None, host.LastFailure)
        End Using
    End Function

    <Fact>
    Public Async Function The_daily_limit_and_a_full_Drive_are_named_not_hidden_behind_a_status() As Task
        Using rig As New Rig()
            rig.StoreSignIn()
            rig.Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 1000, 403, "dailyLimitExceeded")
            Dim host As DriveChannelHost = rig.NewHost()
            host.StartIfSignedIn()
            Await Until(Function() rig.Gate.Asked().Count = 1)

            Assert.Equal(DriveChannelState.Unavailable, host.State)
            Assert.Equal(ExchangeDriveFailure.DailyLimit, host.LastFailure)
            AssertPlain(DriveChannelText.StateDetail(host.State, host.LastFailure))
            AssertPlain(DriveChannelText.FailureDetail(ExchangeDriveFailure.DriveFull))
            AssertPlain(DriveChannelText.FailureDetail(ExchangeDriveFailure.Offline))
            AssertPlain(DriveChannelText.FailureDetail(ExchangeDriveFailure.Other))
        End Using
    End Function

    <Fact>
    Public Async Function A_consent_that_completes_as_the_host_is_disposed_starts_no_poller() As Task
        Using rig As New Rig()
            Dim host As DriveChannelHost = Nothing
            host = rig.NewHost(Function(id As String, name As String) As ExchangeDriveService
                                   ' the program is closing exactly when the sign-in finishes
                                   host.Dispose()
                                   Return rig.BuildService(id, name)
                               End Function)

            Dim outcome As ExchangeDriveSignInOutcome? = Await host.SignInAsync()
            Await rig.Browser.Done
            Await Task.Delay(150)

            Assert.Equal(ExchangeDriveSignInOutcome.Signed, outcome.Value)
            Assert.Equal(1, rig.Created)
            Assert.Null(host.Service)
            Assert.Empty(rig.Gate.Asked())
            Assert.Equal(1, rig.Drive.RequestCount) ' the code exchange only; a poller would have refreshed the token and listed
            Assert.True(rig.Store.Exists(), "the sign-in itself stays for the next start")
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ the words

    <Fact>
    Public Sub Every_sentence_of_the_page_is_plain_for_every_state_failure_and_outcome()
        For Each state As DriveChannelState In [Enum].GetValues(Of DriveChannelState)()
            AssertPlain(DriveChannelText.StateLine(state))
            For Each failure As ExchangeDriveFailure In [Enum].GetValues(Of ExchangeDriveFailure)()
                Dim detail As String = DriveChannelText.StateDetail(state, failure)
                If detail.Length > 0 Then AssertPlain(detail)
            Next
        Next
        For Each outcome As ExchangeDriveSignInOutcome In [Enum].GetValues(Of ExchangeDriveSignInOutcome)()
            For Each cancelled As Boolean In New Boolean() {False, True}
                Dim text As String = DriveChannelText.SignInOutcome(outcome, cancelled)
                If outcome <> ExchangeDriveSignInOutcome.Signed Then AssertPlain(text)
            Next
        Next
        AssertPlain(DriveChannelText.ScopeNote())
        AssertPlain(DriveChannelText.SignOutQuestion())
        Assert.Equal("", DriveChannelText.DeviceLine("Office PC", ""))
        AssertPlain(DriveChannelText.DeviceLine("Office PC", IdFresh))
        Assert.DoesNotContain(IdFresh, DriveChannelText.DeviceLine("Office PC", IdFresh)) ' only a short form of the id is shown
    End Sub

End Class
