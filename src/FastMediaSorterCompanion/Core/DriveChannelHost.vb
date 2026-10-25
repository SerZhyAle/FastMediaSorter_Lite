Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Threading

''' <summary>Where the Google Drive channel stands, for the sign-in page's one state line.</summary>
Friend Enum DriveChannelState
    ''' <summary>This build has no OAuth client (no id/secret): the channel is absent and the page says so calmly.</summary>
    NotConfigured
    ''' <summary>No stored sign-in, or Google no longer honours the stored one.</summary>
    SignedOut
    ''' <summary>The browser consent is open and the host waits for it.</summary>
    SigningIn
    ''' <summary>A sign-in is stored; the last round (if any) reached Drive.</summary>
    SignedIn
    ''' <summary>Signed in, but the last round failed in a way that may pass (network, a limit): see <see cref="DriveChannelHost.LastFailure"/>.</summary>
    Unavailable
    ''' <summary>The sign-out is running: records deleted, token revoked, file removed.</summary>
    SigningOut
End Enum

''' <summary>
''' The Share Manager's one owner of the Google Drive channel (specification 203 section 5.9, ticket T-UI-1): it holds the single
''' <see cref="ExchangeDriveService"/> of the process, runs the sign-in and the sign-out, and tells the page what it needs to
''' say - a state, the last failure kind, the last outcome. It produces no user text (<see cref="DriveChannelText"/> does).
'''
''' Rules held here:
'''   * Nothing touches the network or the disk because a page was opened or the application started: the service is built
'''     only when a sign-in is stored (<see cref="StartIfSignedIn"/>) or when the person signs in; with no stored token the
'''     poller never starts. The token store, the id file and the HTTP client are only constructed - none of them creates a
'''     file or a request by itself.
'''   * The device id is resolved (and, on a PC with none, created) only on an explicit sign-in or with a stored sign-in
'''     (<see cref="DriveDeviceId"/>): one id, the remote-server account's when there is one.
'''   * Without an OAuth client the state is <see cref="DriveChannelState.NotConfigured"/> and nothing else breaks; the client
'''     id and secret are never read into a message, a log line or a field this class exposes.
'''   * Events arrive on whatever thread raised them (a poller thread, the sign-in's continuation): the page marshals.
'''   * No exception text leaves this class: failures are kinds (<see cref="ExchangeDriveFailure"/>, <see cref="ExchangeDriveSignInOutcome"/>).
''' </summary>
Friend NotInheritable Class DriveChannelHost
    Implements IDisposable

    Private ReadOnly _http As HttpClient
    Private ReadOnly _store As ExchangeDriveTokenStore
    Private ReadOnly _config As ExchangeDriveClientConfig
    Private ReadOnly _ids As DriveDeviceId
    Private ReadOnly _deviceName As String
    Private ReadOnly _openBrowser As Func(Of String, Boolean)
    Private ReadOnly _landing As Func(Of String)
    Private ReadOnly _createService As Func(Of String, String, ExchangeDriveService)
    Private ReadOnly _createPublisher As Func(Of ExchangeDriveService, String, IDriveChannelPublisher)
    Private ReadOnly _lock As New Object()

    Private _service As ExchangeDriveService
    Private _publisher As IDriveChannelPublisher
    Private _deviceId As String = ""
    Private _signingIn As Boolean
    Private _signingOut As Boolean
    Private _disposed As Boolean
    Private _consent As CancellationTokenSource
    Private _cancelRequested As Boolean
    Private _outcome As ExchangeDriveSignInOutcome?
    Private _outcomeCancelled As Boolean
    Private _signOutResult As ExchangeDriveSignOutResult

    ''' <summary>Raised when the state, the failure, the outcome or the device id changed. May be raised on any thread.</summary>
    Public Event Changed As EventHandler

    ''' <param name="http">The shared client of the channel.</param>
    ''' <param name="store">The DPAPI token store.</param>
    ''' <param name="config">The OAuth client (environment first, then the build's metadata).</param>
    ''' <param name="ids">The device id of this PC.</param>
    ''' <param name="deviceName">The name other devices see (the machine name).</param>
    ''' <param name="openBrowser">Opens the consent URL in the system browser; False when it could not.</param>
    ''' <param name="landing">The page the browser shows after the redirect.</param>
    ''' <param name="createService">Builds the service for an id and a name. Nothing: <see cref="ExchangeDriveService.Create"/> on the real stack.</param>
    ''' <param name="createPublisher">Builds what lists this PC's device and folders on Drive while the service runs (203 section 5.11).
    ''' Nothing: nothing is listed (a test of the sign-in alone).</param>
    Public Sub New(http As HttpClient, store As ExchangeDriveTokenStore, config As ExchangeDriveClientConfig, ids As DriveDeviceId, deviceName As String,
                   openBrowser As Func(Of String, Boolean), landing As Func(Of String),
                   Optional createService As Func(Of String, String, ExchangeDriveService) = Nothing,
                   Optional createPublisher As Func(Of ExchangeDriveService, String, IDriveChannelPublisher) = Nothing)
        If http Is Nothing Then Throw New ArgumentNullException(NameOf(http))
        If store Is Nothing Then Throw New ArgumentNullException(NameOf(store))
        If config Is Nothing Then Throw New ArgumentNullException(NameOf(config))
        If ids Is Nothing Then Throw New ArgumentNullException(NameOf(ids))
        _http = http
        _store = store
        _config = config
        _ids = ids
        _deviceName = If(deviceName, "")
        _openBrowser = If(openBrowser, Function(url As String) False)
        _landing = If(landing, Function() "")
        _createService = If(createService, Function(id As String, name As String) ExchangeDriveService.Create(_http, _store, _config, id, name))
        _createPublisher = createPublisher
    End Sub

    ''' <summary>The channel on the real stack: the shared HTTP client, the per-user token folder, the build's OAuth client, the
    ''' machine name and the system browser.</summary>
    Public Shared Function CreateDefault() As DriveChannelHost
        Return New DriveChannelHost(ExchangeDriveClient.CreateHttpClient(), ExchangeDriveTokenStore.CreateDefault(), ExchangeDriveClientConfig.Resolve(),
                                    DriveDeviceId.CreateDefault(), Environment.MachineName,
                                    Function(url As String) NetworkInfo.OpenInBrowser(url), Function() DriveChannelText.LandingPage(),
                                    Nothing, Function(service As ExchangeDriveService, id As String) DriveChannelPublisher.CreateFor(service, id, Environment.MachineName))
    End Function

    ' ------------------------------------------------------------------------------------------ reading

    ''' <summary>False in a build with no OAuth client; the page then disables "Sign in" and says why.</summary>
    Public ReadOnly Property IsConfigured As Boolean
        Get
            Return _config.IsConfigured
        End Get
    End Property

    Public ReadOnly Property State As DriveChannelState
        Get
            Dim service As ExchangeDriveService
            SyncLock _lock
                If _signingOut Then Return DriveChannelState.SigningOut
                If _signingIn Then Return DriveChannelState.SigningIn
                service = _service
            End SyncLock
            If Not _config.IsConfigured Then Return DriveChannelState.NotConfigured
            If service Is Nothing Then Return If(HasStoredSignIn(), DriveChannelState.SignedIn, DriveChannelState.SignedOut)
            Select Case service.State
                Case ExchangeDriveServiceState.SignedOut : Return DriveChannelState.SignedOut
                Case ExchangeDriveServiceState.Unavailable : Return DriveChannelState.Unavailable
                Case Else : Return DriveChannelState.SignedIn
            End Select
        End Get
    End Property

    ''' <summary>The kind of the last failed round (None after a good one). <see cref="ExchangeDriveFailure.SignedOut"/> in the
    ''' <see cref="DriveChannelState.SignedOut"/> state means the sign-in was lost (revoked or expired), not "never signed in".</summary>
    Public ReadOnly Property LastFailure As ExchangeDriveFailure
        Get
            Dim service As ExchangeDriveService
            SyncLock _lock
                service = _service
            End SyncLock
            Return If(service Is Nothing, ExchangeDriveFailure.None, service.LastFailure)
        End Get
    End Property

    ''' <summary>How the last sign-in attempt of this session ended, or Nothing before any.</summary>
    Public ReadOnly Property LastOutcome As ExchangeDriveSignInOutcome?
        Get
            SyncLock _lock
                Return _outcome
            End SyncLock
        End Get
    End Property

    ''' <summary>True when the last attempt ended because the person cancelled it (the wait itself reports a time-out).</summary>
    Public ReadOnly Property LastOutcomeWasCancelled As Boolean
        Get
            SyncLock _lock
                Return _outcomeCancelled
            End SyncLock
        End Get
    End Property

    ''' <summary>What the last sign-out removed and confirmed, or Nothing.</summary>
    Public ReadOnly Property LastSignOut As ExchangeDriveSignOutResult
        Get
            SyncLock _lock
                Return _signOutResult
            End SyncLock
        End Get
    End Property

    ''' <summary>The device id in use, or "" until a sign-in resolved one (this property reads and creates nothing).</summary>
    Public ReadOnly Property DeviceId As String
        Get
            SyncLock _lock
                Return _deviceId
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property DeviceName As String
        Get
            Return _deviceName
        End Get
    End Property

    ''' <summary>The service, or Nothing before a sign-in is stored. The queue and the consumers of 228-231 read it from here.</summary>
    Public ReadOnly Property Service As ExchangeDriveService
        Get
            SyncLock _lock
                Return _service
            End SyncLock
        End Get
    End Property

    ''' <summary>What the account's other devices list on Drive as of the last good round (this PC's own records are not in it), or an
    ''' empty snapshot: not signed in, or no round has answered yet. A pure read.</summary>
    Public ReadOnly Property ListedDirectory As ExchangeDirectorySnapshot
        Get
            Dim service As ExchangeDriveService
            SyncLock _lock
                service = _service
            End SyncLock
            Dim latest As ExchangeDriveUpdate = If(service Is Nothing, Nothing, service.Latest)
            If latest Is Nothing OrElse latest.Snapshot Is Nothing Then Return ExchangeDirectorySnapshot.Empty
            Return latest.Snapshot.Snapshot
        End Get
    End Property

    Private Function HasStoredSignIn() As Boolean
        If Not _config.IsConfigured Then Return False
        Dim token As String = ""
        Return _store.TryLoad(_config.ClientId, token)
    End Function

    ' ------------------------------------------------------------------------------------------ starting

    ''' <summary>The application's start: with a stored sign-in the service is built and the poller started; without one it does
    ''' nothing at all (no request, no file). A second call changes nothing.</summary>
    Public Sub StartIfSignedIn()
        SyncLock _lock
            If _disposed OrElse _service IsNot Nothing OrElse _signingIn OrElse _signingOut Then Return
        End SyncLock
        If Not HasStoredSignIn() Then Return
        Try
            Dim id As String = _ids.Resolve()
            If id.Length = 0 Then Return
            BuildService(id, True)
        Catch ex As Exception When Not IsFatal(ex)
            ' the application's start must never fail because of the Drive channel: the page then shows the stored sign-in as it is
            Return
        End Try
        RaiseChanged()
    End Sub

    ''' <summary>A screen that waits for something raises the poller to its 10 s rhythm; a no-op while there is no service.</summary>
    Public Sub SetActive(active As Boolean)
        Dim service As ExchangeDriveService
        SyncLock _lock
            service = _service
        End SyncLock
        If service IsNot Nothing Then service.SetActive(active)
    End Sub

    Private Sub BuildService(id As String, start As Boolean)
        Dim fresh As ExchangeDriveService = _createService(id, _deviceName)
        ' only a service that polls lists this PC; the sign-out path builds one without starting it and must write nothing
        Dim freshPublisher As IDriveChannelPublisher = If(start AndAlso _createPublisher IsNot Nothing, _createPublisher(fresh, id), Nothing)
        Dim old As ExchangeDriveService = Nothing
        Dim oldPublisher As IDriveChannelPublisher = Nothing
        Dim kept As Boolean
        SyncLock _lock
            ' a consent that completed at the very moment the host was disposed must not start a poller nobody will stop
            kept = Not _disposed
            If kept Then
                old = _service
                oldPublisher = _publisher
                _service = fresh
                _publisher = freshPublisher
                _deviceId = id
            End If
        End SyncLock
        If Not kept Then
            If freshPublisher IsNot Nothing Then freshPublisher.Dispose()
            fresh.Dispose()
            Return
        End If
        If oldPublisher IsNot Nothing Then oldPublisher.Dispose()
        If old IsNot Nothing Then Detach(old)
        AddHandler fresh.StateChanged, AddressOf OnServiceChanged
        AddHandler fresh.Updated, AddressOf OnServiceUpdated
        If start Then
            fresh.Start()
            If freshPublisher IsNot Nothing Then freshPublisher.Start()
        End If
    End Sub

    Private Sub Detach(service As ExchangeDriveService)
        RemoveHandler service.StateChanged, AddressOf OnServiceChanged
        RemoveHandler service.Updated, AddressOf OnServiceUpdated
        service.Dispose()
    End Sub

    ''' <summary>Ends what lists this PC on Drive and waits for a write in flight. Sign-out calls it before it deletes the records.</summary>
    Private Async Function StopPublisherAsync() As Task
        Dim publisher As IDriveChannelPublisher
        SyncLock _lock
            publisher = _publisher
        End SyncLock
        If publisher IsNot Nothing Then Await publisher.StopAsync().ConfigureAwait(False)
    End Function

    Private Sub OnServiceChanged(sender As Object, e As EventArgs)
        RaiseChanged()
    End Sub

    Private Sub OnServiceUpdated(sender As Object, e As ExchangeDriveUpdate)
        RaiseChanged()
    End Sub

    Private Sub RaiseChanged()
        Try
            RaiseEvent Changed(Me, EventArgs.Empty)
        Catch ex As Exception When TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is InvalidOperationException
            ' a window that is closing: nothing to tell
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------ sign in

    ''' <summary>The consent flow: the device id is resolved first (so a disk that cannot keep it is reported before the person is
    ''' sent to a browser), then the system browser is opened and the loopback redirect awaited. On success the service is built
    ''' and started. Nothing when a sign-in or a sign-out is already running.</summary>
    Public Async Function SignInAsync() As Task(Of ExchangeDriveSignInOutcome?)
        If Not _config.IsConfigured Then Return ExchangeDriveSignInOutcome.NotConfigured
        Dim source As New CancellationTokenSource()
        SyncLock _lock
            If _disposed OrElse _signingIn OrElse _signingOut Then
                source.Dispose()
                Return Nothing
            End If
            _signingIn = True
            _cancelRequested = False
            _consent = source
            _outcome = Nothing
            _outcomeCancelled = False
            _signOutResult = Nothing
        End SyncLock
        RaiseChanged()

        Dim outcome As ExchangeDriveSignInOutcome = ExchangeDriveSignInOutcome.TokenRefused
        Dim cancelled As Boolean = False
        Try
            Dim id As String = _ids.Resolve()
            If id.Length = 0 Then
                outcome = ExchangeDriveSignInOutcome.StorageFailed
            Else
                Dim session As New ExchangeDriveSession(_http, _store, _config)
                outcome = Await session.SignInAsync(_openBrowser, SafeLanding(), ExchangeDriveSession.DefaultConsentWaitSeconds, source.Token).ConfigureAwait(False)
                If outcome = ExchangeDriveSignInOutcome.Signed Then BuildService(id, True)
            End If
        Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is TaskCanceledException
            outcome = ExchangeDriveSignInOutcome.Offline
        Catch ex As Exception When Not IsFatal(ex)
            ' anything else (a DPAPI or JSON surprise, a closed listener): a plain "could not complete" - an async Sub on the page
            ' would turn an escaping exception into a crash of the Share Manager, and the person would see no reason either way
            outcome = ExchangeDriveSignInOutcome.TokenRefused
        Finally
            SyncLock _lock
                cancelled = _cancelRequested
                _signingIn = False
                _consent = Nothing
                _outcome = outcome
                _outcomeCancelled = cancelled AndAlso outcome = ExchangeDriveSignInOutcome.TimedOut
            End SyncLock
            source.Dispose()
            RaiseChanged()
        End Try
        Return outcome
    End Function

    ''' <summary>The exceptions no handler may swallow: the process is in trouble, not the Drive channel.</summary>
    Private Shared Function IsFatal(ex As Exception) As Boolean
        Return TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is StackOverflowException OrElse TypeOf ex Is AccessViolationException
    End Function

    Private Function SafeLanding() As String
        Try
            Return If(_landing(), "")
        Catch ex As InvalidOperationException
            Return ""
        End Try
    End Function

    ''' <summary>Stops the wait for the browser; the sign-in then ends as a cancelled one. A no-op when none is running.</summary>
    Public Sub CancelSignIn()
        Dim source As CancellationTokenSource
        SyncLock _lock
            If Not _signingIn Then Return
            _cancelRequested = True
            source = _consent
        End SyncLock
        Try
            If source IsNot Nothing Then source.Cancel()
        Catch ex As ObjectDisposedException
            ' the wait ended just now
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------ sign out

    ''' <summary>Specification 203 criterion 4: stops the poller, deletes this PC's own records while the token still works, revokes
    ''' the token at Google and removes the stored file - the file is removed even when Drive or Google cannot be reached; the
    ''' result says what was confirmed. Nothing when a sign-in or a sign-out is already running.</summary>
    Public Async Function SignOutAsync() As Task(Of ExchangeDriveSignOutResult)
        Dim service As ExchangeDriveService
        SyncLock _lock
            If _disposed OrElse _signingIn OrElse _signingOut Then Return Nothing
            _signingOut = True
            _outcome = Nothing
            _outcomeCancelled = False
            service = _service
        End SyncLock
        RaiseChanged()

        Dim result As ExchangeDriveSignOutResult = Nothing
        Try
            ' nothing may write a record behind the deletion that follows
            Await StopPublisherAsync().ConfigureAwait(False)
            If service Is Nothing AndAlso HasStoredSignIn() Then
                Dim id As String = _ids.Resolve()
                If id.Length > 0 Then
                    BuildService(id, False)
                    SyncLock _lock
                        service = _service
                    End SyncLock
                End If
            End If
            If service Is Nothing Then
                ' nothing to delete or revoke on a PC that is not signed in; a stale file is still removed
                _store.Delete()
                result = New ExchangeDriveSignOutResult(True, 0, True)
            Else
                Try
                    result = Await service.SignOutAsync(CancellationToken.None).ConfigureAwait(False)
                Catch ex As Exception When Not IsFatal(ex)
                    ' Drive or Google could not be reached, or something unexpected broke: the person asked to leave, so the
                    ' file goes anyway and the result says nothing was confirmed
                    Try
                        _store.Delete()
                    Catch inner As Exception When Not IsFatal(inner)
                    End Try
                    result = New ExchangeDriveSignOutResult(False, 0, False)
                End Try
            End If
        Catch outer As Exception When Not IsFatal(outer)
            ' the service could not even be built (a disk or DPAPI surprise): same promise - the file goes, nothing is confirmed,
            ' and no exception text reaches the page (an async Sub would otherwise end the Share Manager)
            Try
                _store.Delete()
            Catch cleanup As Exception When Not IsFatal(cleanup)
            End Try
            result = New ExchangeDriveSignOutResult(False, 0, False)
        Finally
            Dim old As ExchangeDriveService
            Dim oldPublisher As IDriveChannelPublisher
            SyncLock _lock
                old = _service
                oldPublisher = _publisher
                _service = Nothing
                _publisher = Nothing
                _signingOut = False
                _signOutResult = result
            End SyncLock
            If oldPublisher IsNot Nothing Then oldPublisher.Dispose()
            If old IsNot Nothing Then Detach(old)
            RaiseChanged()
        End Try
        Return result
    End Function

    ' ------------------------------------------------------------------------------------------ end

    ''' <summary>Cancels a consent in progress and stops the poller. The sign-in stays: the next start picks it up.</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        Dim service As ExchangeDriveService
        Dim publisher As IDriveChannelPublisher
        Dim source As CancellationTokenSource
        SyncLock _lock
            If _disposed Then Return
            _disposed = True
            service = _service
            publisher = _publisher
            _service = Nothing
            _publisher = Nothing
            source = _consent
        End SyncLock
        Try
            If source IsNot Nothing Then source.Cancel()
        Catch ex As ObjectDisposedException
        End Try
        If publisher IsNot Nothing Then publisher.Dispose()
        If service IsNot Nothing Then Detach(service)
    End Sub

End Class

''' <summary>
''' The process's one <see cref="DriveChannelHost"/>, created on first use and released at exit - the same shape as
''' <see cref="ExchangeDeviceHost"/>. Creating it only constructs objects: no request, no file.
''' </summary>
Friend NotInheritable Class DriveChannelHosts

    Private Shared ReadOnly Gate As New Object()
    Private Shared _current As DriveChannelHost

    Private Sub New()
    End Sub

    Public Shared Function Current() As DriveChannelHost
        SyncLock Gate
            If _current Is Nothing Then _current = DriveChannelHost.CreateDefault()
            Return _current
        End SyncLock
    End Function

    ''' <summary>The application's start: with a stored sign-in the poller starts, otherwise nothing happens.</summary>
    Public Shared Sub StartIfSignedIn()
        ' not signed in (no token file) and no host yet: not even the host is built - the start of a PC that never used the
        ' channel creates nothing and asks for nothing
        SyncLock Gate
            If _current Is Nothing AndAlso Not TokenPresent() Then Return
        End SyncLock
        Current().StartIfSignedIn()
    End Sub

    ''' <summary>Whether a token file of the channel exists for this user - a plain existence check, nothing is read or created.
    ''' Replaceable so a test does not look at the real profile.</summary>
    Friend Shared TokenPresent As Func(Of Boolean) = Function() ExchangeDriveTokenStore.CreateDefault().Exists()

    ''' <summary>The application's exit: the poller is cancelled, the sign-in stays. A host never created is not created here.</summary>
    Public Shared Sub Release()
        Dim host As DriveChannelHost
        SyncLock Gate
            host = _current
            _current = Nothing
        End SyncLock
        If host IsNot Nothing Then host.Dispose()
    End Sub

    ''' <summary>The Drive listing for the directory the viewer reads (203 section 5.11). Asking creates nothing: no host yet, or no
    ''' sign-in, is an empty snapshot.</summary>
    Public Shared Function ListedDirectory() As ExchangeDirectorySnapshot
        Dim host As DriveChannelHost
        SyncLock Gate
            host = _current
        End SyncLock
        Return If(host Is Nothing, ExchangeDirectorySnapshot.Empty, host.ListedDirectory)
    End Function

    ''' <summary>Whether the process's host exists. Asking creates nothing (a diagnostic and test seam).</summary>
    Friend Shared Function IsCreated() As Boolean
        SyncLock Gate
            Return _current IsNot Nothing
        End SyncLock
    End Function

    ''' <summary>Test seam: replaces the process's host (the previous one is disposed). Nothing restores the default except
    ''' <see cref="Release"/>.</summary>
    Friend Shared Sub Use(host As DriveChannelHost)
        Dim old As DriveChannelHost
        SyncLock Gate
            old = _current
            _current = host
        End SyncLock
        If old IsNot Nothing AndAlso Not Object.ReferenceEquals(old, host) Then old.Dispose()
    End Sub

End Class
