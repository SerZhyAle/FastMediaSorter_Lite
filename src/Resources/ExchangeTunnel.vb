#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 171 rule 6 (phase C3) and rule 7 (C4): the tunnel as ONE MORE CANDIDATE of the endpoint race, last, with the longest head-start
' for the others, and created only when something dials it.
'
'   - The candidate is a loopback forwarder of the client library (127.0.0.1:<port>, every accepted connection one `connect` data stream
'     made with the stored token - or, for an outsider link, `connect {shareId}` with no `hello`). SSH stays the gate: the host-key pin and
'     the credential are checked through the tunnel exactly as through a direct address (REMOTE-FOLDER-ACCESS governs a session on every
'     transport).
'   - Its DIAL is what creates the forwarder and asks the server for a stream (<see cref="ExchangeTunnelDialer"/>), so nothing is opened on
'     the exchange while a direct address answers: the resolver does not dial a staggered candidate until its head-start has passed or every
'     earlier candidate has failed, and a round that was won first never dials it.
'   - The forwarder is bound to loopback only (the library's listener) and to ONE resource (its opener names one id), is closed with the
'     session (the provider owns its lease) and after an idle timeout when nothing uses it.
'   - Trust is item D of DEVICE-EXCHANGE 14.3 and nothing else: the platform trust store first (nothing pinned); a leaf it rejects only
'     after the user has been shown its fingerprint and accepted it; a changed certificate where a pin exists is a named, fatal failure.
'
' Nothing in this file prints, logs or keeps a token, a share id or a certificate beyond the lease's life.

Friend NotInheritable Class ExchangeTunnelRules
    Private Sub New()
    End Sub

    ''' <summary>The head-start every direct address gets (milliseconds of the round) before the tunnel is dialled - or until all of them have
    ''' failed. Long enough for a LAN or a forwarded port to answer, short enough that a tunnel is not slow to appear when they cannot.</summary>
    Friend Const StaggerMs As Integer = 2500

    Friend Const TunnelKind As String = "tunnel"

    ''' <summary>The detail of the KeyMismatch failure that means "the exchange server's certificate is not the pinned one" (not the SSH key).</summary>
    Friend Const CertificateDetail As String = "exchange-certificate"

    ''' <summary>A forwarder nothing is using is closed after this long (milliseconds).</summary>
    Friend Const IdleCloseMs As Integer = 5 * 60 * 1000

    ''' <summary>The candidate placed last: a placeholder address (the dialer replaces it with the lease's loopback port) and the longest stagger.</summary>
    Friend Shared Function Candidate() As FmsEndpoint
        Return New FmsEndpoint With {.Kind = TunnelKind, .Host = "tunnel.invalid", .Port = 1, .StartAfterMs = StaggerMs}
    End Function

    Friend Shared Function IsTunnel(endpoint As FmsEndpoint) As Boolean
        Return endpoint IsNot Nothing AndAlso String.Equals(endpoint.Kind, TunnelKind, StringComparison.Ordinal)
    End Function

End Class

Friend Enum ExchangeTunnelFailure
    None = 0
    ''' <summary>No account, no token, the features are not offered, or the server did not answer: nothing to tunnel through.</summary>
    Unavailable = 1
    ''' <summary>The user was shown a certificate the platform does not trust and said no (or it could not be pinned).</summary>
    Declined = 2
    ''' <summary>A pin exists and the certificate presented is another one: a named stop, never re-pinned here.</summary>
    CertificateChanged = 3
    Cancelled = 4
End Enum

Friend NotInheritable Class ExchangeTunnelOpen
    Friend ReadOnly Property Lease As ExchangeTunnelLease
    Friend ReadOnly Property Failure As ExchangeTunnelFailure

    Friend Sub New(lease As ExchangeTunnelLease, failure As ExchangeTunnelFailure)
        Me.Lease = lease
        Me.Failure = failure
    End Sub

    Friend Shared Function Failed(failure As ExchangeTunnelFailure) As ExchangeTunnelOpen
        Return New ExchangeTunnelOpen(Nothing, failure)
    End Function
End Class

''' <summary>Where a tunnel comes from. Bound to ONE resource; creating it is the dial of the tunnel candidate.</summary>
Friend Interface IExchangeTunnelSource
    Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeTunnelOpen)
End Interface

''' <summary>The loopback forwarder of one tunnel and its idle watchdog. Disposed with the SFTP session that dialled it.</summary>
Friend NotInheritable Class ExchangeTunnelLease
    Implements IDisposable

    Private ReadOnly _opener As IExchangeDataOpener
    Private ReadOnly _idleCloseMs As Integer
    Private ReadOnly _cts As New CancellationTokenSource()
    Private _forwarder As ExchangeForwarder
    Private _disposed As Boolean

    Friend Sub New(opener As IExchangeDataOpener, idleCloseMs As Integer)
        If opener Is Nothing Then Throw New ArgumentNullException(NameOf(opener))
        _opener = opener
        _idleCloseMs = idleCloseMs
    End Sub

    ''' <summary>Binds 127.0.0.1 on a port the system chooses and starts watching for idleness. Never reachable from another machine: the
    ''' listener is the library's loopback listener.</summary>
    Friend Sub Start()
        _forwarder = New ExchangeForwarder(_opener)
        _forwarder.Start()
        Dim watch As Task = Task.Run(Function() WatchAsync(_cts.Token))
    End Sub

    Friend ReadOnly Property Port As Integer
        Get
            Return If(_forwarder Is Nothing, 0, _forwarder.Port)
        End Get
    End Property

    Friend ReadOnly Property ConnectsStarted As Integer
        Get
            Return If(_forwarder Is Nothing, 0, _forwarder.ConnectsStarted)
        End Get
    End Property

    Friend ReadOnly Property ActiveBridges As Integer
        Get
            Return If(_forwarder Is Nothing, 0, _forwarder.ActiveBridges)
        End Get
    End Property

    Friend ReadOnly Property IsDisposed As Boolean
        Get
            Return _disposed
        End Get
    End Property

    ''' <summary>Closes the forwarder when no bridge is open and no connection was accepted for the idle timeout.</summary>
    Private Async Function WatchAsync(ct As CancellationToken) As Task
        If _idleCloseMs <= 0 Then Return
        Dim tick As Integer = Math.Max(20, Math.Min(30000, _idleCloseMs \ 4))
        Dim lastSeen As Integer = -1
        Dim lastChange As Long = Environment.TickCount64
        Try
            While Not ct.IsCancellationRequested
                Await Task.Delay(tick, ct).ConfigureAwait(False)
                Dim started As Integer = ConnectsStarted
                If started <> lastSeen OrElse ActiveBridges > 0 Then
                    lastSeen = started
                    lastChange = Environment.TickCount64
                ElseIf Environment.TickCount64 - lastChange >= _idleCloseMs Then
                    Dispose()
                    Return
                End If
            End While
        Catch ex As OperationCanceledException
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        Try : _cts.Cancel() : Catch : End Try
        Try : _forwarder?.Dispose() : Catch : End Try
        Try : _cts.Dispose() : Catch : End Try
    End Sub

End Class

''' <summary>The handshake-only look at the exchange server's certificate (no byte of envelope), behind an interface so a test hands in its own.</summary>
Friend Interface IExchangeCertificateProbe
    Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
End Interface

Friend NotInheritable Class ExchangeTlsCertificateProbe
    Implements IExchangeCertificateProbe

    Friend Shared ReadOnly Instance As New ExchangeTlsCertificateProbe()

    Private Sub New()
    End Sub

    Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeCertificateProbe.ProbeAsync
        Return ExchangeTlsTransport.ProbeAsync(host, port, ct)
    End Function
End Class

''' <summary>The tunnel of an attached resource of the user's own account: `connect {resourceId}` with the token the agent stored. The
''' viewer never sees a password and never needs the agent for this - the token is DPAPI-protected for the same Windows user.</summary>
Friend NotInheritable Class ExchangeAccountTunnelSource
    Implements IExchangeTunnelSource

    Private ReadOnly _resourceId As String
    Private ReadOnly _accounts As ExchangeAccountStore
    Private ReadOnly _credentials As ExchangeCredentialStore
    Private ReadOnly _transport As IExchangeTransport
    Private ReadOnly _probe As IExchangeCertificateProbe
    Private ReadOnly _productVersion As String
    Private ReadOnly _idleCloseMs As Integer

    Friend Sub New(resourceId As String, accounts As ExchangeAccountStore, credentials As ExchangeCredentialStore, transport As IExchangeTransport,
                   probe As IExchangeCertificateProbe, productVersion As String, Optional idleCloseMs As Integer = ExchangeTunnelRules.IdleCloseMs)
        _resourceId = resourceId
        _accounts = accounts
        _credentials = credentials
        _transport = transport
        _probe = probe
        _productVersion = productVersion
        _idleCloseMs = idleCloseMs
    End Sub

    Public Async Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeTunnelOpen) Implements IExchangeTunnelSource.OpenAsync
        Dim account As ExchangeAccount = _accounts.Load()
        If account Is Nothing Then Return ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Unavailable)
        Dim address As ExchangeServerAddress = Nothing
        Dim token As String = ""
        If Not account.TryGetAddress(address) OrElse Not _credentials.TryLoad(account.DeviceId, token) Then Return ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Unavailable)

        Dim trust As ExchangeTunnelFailure = Await ExchangeTunnelTrust.CheckKnownAsync(_probe, address, account.Pin, ct).ConfigureAwait(False)
        If trust <> ExchangeTunnelFailure.None Then Return ExchangeTunnelOpen.Failed(trust)

        Dim opener As New ExchangeConnectOpener(_transport, address, account.Pin, New ExchangeDeviceCredentials(account.DeviceId, token), _productVersion,
                                                ExchangeConnectOpener.Target.Resource, _resourceId, "")
        Dim lease As New ExchangeTunnelLease(opener, _idleCloseMs)
        lease.Start()
        Return New ExchangeTunnelOpen(lease, ExchangeTunnelFailure.None)
    End Function

End Class

''' <summary>The tunnel of a resource attached through one of the viewer's own ENTRIES (ticket 187): `connect {resourceId}` with the token of
''' THAT entry, over the entry's own address and pin. The viewer needs no Share Manager and no agent for it. An entry that was removed (or whose
''' token is gone) is "unavailable" - never another account's token, and never a fallback to the Share Manager's account.</summary>
Friend NotInheritable Class ExchangeEntryTunnelSource
    Implements IExchangeTunnelSource

    Private ReadOnly _resourceId As String
    Private ReadOnly _entryId As String
    Private ReadOnly _entries As ExchangeEntryStore
    Private ReadOnly _transport As IExchangeTransport
    Private ReadOnly _probe As IExchangeCertificateProbe
    Private ReadOnly _productVersion As String
    Private ReadOnly _idleCloseMs As Integer

    Friend Sub New(resourceId As String, entryId As String, entries As ExchangeEntryStore, transport As IExchangeTransport,
                   probe As IExchangeCertificateProbe, productVersion As String, Optional idleCloseMs As Integer = ExchangeTunnelRules.IdleCloseMs)
        _resourceId = resourceId
        _entryId = entryId
        _entries = entries
        _transport = transport
        _probe = probe
        _productVersion = productVersion
        _idleCloseMs = idleCloseMs
    End Sub

    Public Async Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeTunnelOpen) Implements IExchangeTunnelSource.OpenAsync
        Dim entry As ExchangeEntry = _entries.Find(_entryId)
        If entry Is Nothing Then Return ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Unavailable)
        Dim address As ExchangeServerAddress = Nothing
        Dim token As String = ""
        If Not entry.TryGetAddress(address) OrElse Not _entries.TokenStoreFor(entry.Id).TryLoad(entry.DeviceId, token) Then Return ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Unavailable)

        Dim trust As ExchangeTunnelFailure = Await ExchangeTunnelTrust.CheckKnownAsync(_probe, address, entry.Pin, ct).ConfigureAwait(False)
        If trust <> ExchangeTunnelFailure.None Then Return ExchangeTunnelOpen.Failed(trust)

        Dim opener As New ExchangeConnectOpener(_transport, address, entry.Pin, New ExchangeDeviceCredentials(entry.DeviceId, token), _productVersion,
                                                ExchangeConnectOpener.Target.Resource, _resourceId, "")
        Dim lease As New ExchangeTunnelLease(opener, _idleCloseMs)
        lease.Start()
        Return New ExchangeTunnelOpen(lease, ExchangeTunnelFailure.None)
    End Function

End Class
''' <summary>The tunnel of an outsider link: `connect {shareId}` on a TLS stream to the `x` of the code or the tunnel path of the file, no
''' `hello`, no account. The certificate is judged as item D says: the platform first; a leaf it rejects only after the user accepted its
''' fingerprint (asked here, stored on the resource); a changed one where a pin exists is a named stop.</summary>
Friend NotInheritable Class ExchangeLinkTunnelSource
    Implements IExchangeTunnelSource

    Private ReadOnly _host As String
    Private ReadOnly _port As Integer
    Private ReadOnly _shareId As Func(Of String)
    Private ReadOnly _pin As String
    Private ReadOnly _consent As Func(Of String, CancellationToken, Boolean)
    Private ReadOnly _persistPin As Func(Of String, Boolean)
    Private ReadOnly _transport As IExchangeTransport
    Private ReadOnly _probe As IExchangeCertificateProbe
    Private ReadOnly _productVersion As String
    Private ReadOnly _idleCloseMs As Integer

    ''' <param name="shareId">Reads the share id when it is needed (a DPAPI blob): it is a bearer capability and is never held longer than the call.</param>
    ''' <param name="consent">Asked once, for a certificate the platform does not trust, with its fingerprint; True only on an explicit accept.</param>
    ''' <param name="persistPin">Stores an accepted fingerprint on the resource; False means it was not stored and the tunnel does not open.</param>
    Friend Sub New(host As String, port As Integer, shareId As Func(Of String), pin As String, consent As Func(Of String, CancellationToken, Boolean),
                   persistPin As Func(Of String, Boolean), transport As IExchangeTransport, probe As IExchangeCertificateProbe, productVersion As String,
                   Optional idleCloseMs As Integer = ExchangeTunnelRules.IdleCloseMs)
        _host = host
        _port = port
        _shareId = shareId
        _pin = If(pin, "")
        _consent = consent
        _persistPin = persistPin
        _transport = transport
        _probe = probe
        _productVersion = productVersion
        _idleCloseMs = idleCloseMs
    End Sub

    Public Async Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeTunnelOpen) Implements IExchangeTunnelSource.OpenAsync
        Dim address As New ExchangeServerAddress(_host, _port)
        Dim sid As String = _shareId.Invoke()
        If Not ExchangeIds.IsValidId(sid) Then Return ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Unavailable)

        Dim pin As String = _pin
        If pin.Length > 0 Then
            Dim known As ExchangeTunnelFailure = Await ExchangeTunnelTrust.CheckKnownAsync(_probe, address, pin, ct).ConfigureAwait(False)
            If known <> ExchangeTunnelFailure.None Then Return ExchangeTunnelOpen.Failed(known)
        Else
            Dim info As ExchangeCertificateInfo
            Try
                info = Await _probe.ProbeAsync(address.Host, address.Port, ct).ConfigureAwait(False)
            Catch ex As ExchangeConnectException
                Return ExchangeTunnelOpen.Failed(If(ex.Failure = ExchangeConnectFailure.Cancelled, ExchangeTunnelFailure.Cancelled, ExchangeTunnelFailure.Unavailable))
            End Try
            If Not info.PlatformValid Then
                ' a leaf the platform rejects: only after the user has been shown its fingerprint and accepted it (item D)
                If _consent Is Nothing OrElse Not _consent.Invoke(info.Fingerprint, ct) Then Return ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Declined)
                If _persistPin Is Nothing OrElse Not _persistPin.Invoke(info.Fingerprint) Then Return ExchangeTunnelOpen.Failed(ExchangeTunnelFailure.Declined)
                pin = info.Fingerprint
            End If
        End If

        Dim opener As New ExchangeConnectOpener(_transport, address, pin, Nothing, _productVersion, ExchangeConnectOpener.Target.Share, sid, "")
        Dim lease As New ExchangeTunnelLease(opener, _idleCloseMs)
        lease.Start()
        Return New ExchangeTunnelOpen(lease, ExchangeTunnelFailure.None)
    End Function

End Class

''' <summary>The one trust rule of both sources (DEVICE-EXCHANGE item D), pure up to the probe.</summary>
Friend NotInheritable Class ExchangeTunnelTrust
    Private Sub New()
    End Sub

    ''' <summary>For a server whose certificate the user already decided about: <paramref name="pin"/> is the fingerprint they accepted ("" when
    ''' the platform validated the chain and nothing was pinned). A pin that differs from what the server shows now is
    ''' <see cref="ExchangeTunnelFailure.CertificateChanged"/>; no pin and a chain the platform no longer accepts is the same stop.</summary>
    Friend Shared Async Function CheckKnownAsync(probe As IExchangeCertificateProbe, address As ExchangeServerAddress, pin As String, ct As CancellationToken) As Task(Of ExchangeTunnelFailure)
        Dim info As ExchangeCertificateInfo
        Try
            info = Await probe.ProbeAsync(address.Host, address.Port, ct).ConfigureAwait(False)
        Catch ex As ExchangeConnectException
            Return If(ex.Failure = ExchangeConnectFailure.Cancelled, ExchangeTunnelFailure.Cancelled, ExchangeTunnelFailure.Unavailable)
        End Try
        If Not String.IsNullOrEmpty(pin) Then
            Return If(String.Equals(pin, info.Fingerprint, StringComparison.Ordinal), ExchangeTunnelFailure.None, ExchangeTunnelFailure.CertificateChanged)
        End If
        Return If(info.PlatformValid, ExchangeTunnelFailure.None, ExchangeTunnelFailure.CertificateChanged)
    End Function

End Class

''' <summary>The dialer of the race with the tunnel in it: every ordinary candidate goes to the inner dialer untouched; the tunnel candidate
''' creates its lease NOW (this is the moment the exchange is first touched), points the inner dialer at the lease's loopback port, and hands
''' the lease to the connected provider so it lives and dies with the session.</summary>
Friend NotInheritable Class ExchangeTunnelDialer
    Implements IEndpointDialer

    Private ReadOnly _inner As IEndpointDialer
    Private ReadOnly _source As IExchangeTunnelSource

    Friend Sub New(inner As IEndpointDialer, source As IExchangeTunnelSource)
        _inner = inner
        _source = source
    End Sub

    Public Async Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
        If Not ExchangeTunnelRules.IsTunnel(endpoint) Then Return Await _inner.DialAsync(endpoint, ct).ConfigureAwait(False)
        Dim opened As ExchangeTunnelOpen
        Try
            opened = Await _source.OpenAsync(ct).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            Return DialResult.Failed(New ResourceFailure(ResourceFailureKind.Cancelled))
        End Try
        If opened Is Nothing OrElse opened.Lease Is Nothing Then Return DialResult.Failed(FailureOf(If(opened Is Nothing, ExchangeTunnelFailure.Unavailable, opened.Failure)))
        Dim lease As ExchangeTunnelLease = opened.Lease
        Dim result As DialResult = Nothing
        Try
            result = Await _inner.DialAsync(New FmsEndpoint With {.Kind = ExchangeTunnelRules.TunnelKind, .Host = "127.0.0.1", .Port = lease.Port}, ct).ConfigureAwait(False)
        Catch
            lease.Dispose()
            Throw
        End Try
        If result Is Nothing OrElse result.Session Is Nothing Then
            lease.Dispose()
            Return If(result, DialResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)))
        End If
        Dim provider As SftpProvider = TryCast(result.Session, SftpProvider)
        If provider Is Nothing Then
            ' a dialer that is not the SFTP one (a test's): the lease then lives with whatever the session is
            Return DialResult.Verified(New LeaseOwningSession(result.Session, lease))
        End If
        provider.OwnedResource = lease
        Return result
    End Function

    Private Shared Function FailureOf(failure As ExchangeTunnelFailure) As ResourceFailure
        Select Case failure
            Case ExchangeTunnelFailure.CertificateChanged
                Return New ResourceFailure(ResourceFailureKind.KeyMismatch, Nothing, ExchangeTunnelRules.CertificateDetail)
            Case ExchangeTunnelFailure.Cancelled, ExchangeTunnelFailure.Declined
                Return New ResourceFailure(ResourceFailureKind.Cancelled)
            Case Else
                Return New ResourceFailure(ResourceFailureKind.Transport, Nothing, "exchange")
        End Select
    End Function

    ''' <summary>A session that is not an SFTP provider, with the lease that must be closed with it.</summary>
    Private NotInheritable Class LeaseOwningSession
        Implements IDisposable

        Private ReadOnly _session As IDisposable
        Private ReadOnly _lease As ExchangeTunnelLease

        Friend Sub New(session As IDisposable, lease As ExchangeTunnelLease)
            _session = session
            _lease = lease
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try : _session.Dispose() : Catch : End Try
            Try : _lease.Dispose() : Catch : End Try
        End Sub
    End Class

End Class

''' <summary>Where the tunnel source of a resource comes from, and the one gate: nothing exists for a resource that has nothing to do with the
''' exchange, or while the client features are not offered.</summary>
Friend NotInheritable Class ExchangeTunnelSources
    Private Sub New()
    End Sub

    ''' <summary>The source for this resource, or Nothing (no origin of the exchange, the features not offered, no tunnel to be had).</summary>
    Friend Shared Function [For](resource As ResourceDescriptor, secrets As IResourceSecretStore,
                          consent As Func(Of String, CancellationToken, Boolean), persistPin As Func(Of String, Boolean)) As IExchangeTunnelSource
        If resource Is Nothing OrElse Not ExchangeFeatures.IsEnabled() Then Return Nothing
        If ResourceOrigin.IsExchange(resource) AndAlso Not String.IsNullOrEmpty(resource.ExchangeResourceId) Then
            ' attached through one of the viewer's own entries (ticket 187): that entry's token, never the Share Manager's account
            If Not String.IsNullOrEmpty(resource.ExchangeEntryId) Then
                Return New ExchangeEntryTunnelSource(resource.ExchangeResourceId, resource.ExchangeEntryId, ExchangeEntryStore.CreateDefault(),
                                                     ExchangeTlsTransport.Instance, ExchangeTlsCertificateProbe.Instance, ProductVersion())
            End If
            Return New ExchangeAccountTunnelSource(resource.ExchangeResourceId, ExchangeAccountStore.CreateDefault(), ExchangeCredentialStore.CreateDefault(),
                                                   ExchangeTlsTransport.Instance, ExchangeTlsCertificateProbe.Instance, ProductVersion())
        End If
        If ResourceOrigin.IsLink(resource) AndAlso Not String.IsNullOrEmpty(resource.TunnelHost) AndAlso resource.TunnelPort.HasValue AndAlso Not String.IsNullOrEmpty(resource.TunnelSecretHandle) Then
            Dim handle As String = resource.TunnelSecretHandle
            Return New ExchangeLinkTunnelSource(resource.TunnelHost, resource.TunnelPort.Value,
                                                Function() ReadShareId(secrets, handle), resource.TunnelPin, consent, persistPin,
                                                ExchangeTlsTransport.Instance, ExchangeTlsCertificateProbe.Instance, ProductVersion())
        End If
        Return Nothing
    End Function

    ''' <summary>The link's share id from its DPAPI blob, or "" (the blob is missing or is another user's). Held only for the call.</summary>
    Friend Shared Function ReadShareId(secrets As IResourceSecretStore, handle As String) As String
        If secrets Is Nothing OrElse String.IsNullOrEmpty(handle) Then Return ""
        Dim blob As String = secrets.GetBlob(handle)
        If String.IsNullOrEmpty(blob) Then Return ""
        Dim opened As SecretProtectionResult = DpapiSecretChecks.UnprotectChecked(blob)
        Return If(opened.Succeeded, opened.Value, "")
    End Function

    Private Shared Function ProductVersion() As String
        Try
            Dim v As Version = Reflection.Assembly.GetExecutingAssembly().GetName().Version
            Return If(v Is Nothing, "", v.ToString())
        Catch ex As InvalidOperationException
            Return ""
        End Try
    End Function

End Class
#End If
