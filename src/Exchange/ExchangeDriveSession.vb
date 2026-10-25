#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Reflection
Imports System.Text
Imports System.Text.Json
Imports System.Threading

' Specification 203 acceptance criteria 1 and 4 - the sign-in of the Drive channel and its end.
'
'   sign in   the installed-app flow of ExchangeDriveAuth: a loopback listener on 127.0.0.1 (random port), the system
'             browser at Google's consent screen with the one scope, PKCE S256, a state check, the code exchanged at once
'             for tokens. The refresh token goes under DPAPI (ExchangeDriveTokenStore); the access token stays in memory.
'   in use    GetAccessTokenAsync hands out a valid access token, refreshing it a minute before it expires.
'   sign out  the refresh token is revoked at Google and the stored file is deleted. Deleting the device's own records from
'             the appDataFolder needs a live token and so comes BEFORE this (ExchangeDriveDirectory.DeleteAllOwnAsync).
'
' The OAuth client (id and secret) is NOT in the repository. A Desktop-app client's secret is not confidential - Google
' says so - but owner policy (203 rule 1) is that neither is ever committed: ExchangeDriveClientConfig reads them from the
' environment (a developer's shell, the probe's variables) and from assembly metadata a release build injects. With
' neither, the channel reports "not configured" and everything about it stays absent.
'
' This file has no UI text and opens no window itself: the browser is opened by a delegate the caller passes, and the page
' the browser lands on is the caller's string (the localized one), so the session is testable without either.

''' <summary>The OAuth client the product signs in with.</summary>
Friend NotInheritable Class ExchangeDriveClientConfig

    ''' <summary>The shell variables the premise probe already reads (done\202 section 8.1 step 9).</summary>
    Public Const ClientIdVariable As String = "FMS_DRIVE_CLIENT_ID"
    Public Const ClientSecretVariable As String = "FMS_DRIVE_CLIENT_SECRET"
    ''' <summary>The assembly metadata keys a release build may inject (`AssemblyMetadata`), the second source.</summary>
    Public Const ClientIdMetadataKey As String = "FmsDriveClientId"
    Public Const ClientSecretMetadataKey As String = "FmsDriveClientSecret"

    Public ReadOnly ClientId As String
    Public ReadOnly ClientSecret As String

    Public Sub New(clientId As String, clientSecret As String)
        Me.ClientId = If(clientId, "").Trim()
        Me.ClientSecret = If(clientSecret, "").Trim()
    End Sub

    Public ReadOnly Property IsConfigured As Boolean
        Get
            Return ClientId.Length > 0 AndAlso ClientSecret.Length > 0
        End Get
    End Property

    ''' <summary>The environment first (so a developer's shell wins), then the build's metadata. Never throws.</summary>
    Public Shared Function Resolve() As ExchangeDriveClientConfig
        Dim id As String = Environment.GetEnvironmentVariable(ClientIdVariable)
        Dim secret As String = Environment.GetEnvironmentVariable(ClientSecretVariable)
        If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(secret) Then
            Try
                For Each attribute As AssemblyMetadataAttribute In GetType(ExchangeDriveClientConfig).Assembly.GetCustomAttributes(Of AssemblyMetadataAttribute)()
                    If String.Equals(attribute.Key, ClientIdMetadataKey, StringComparison.Ordinal) AndAlso String.IsNullOrWhiteSpace(id) Then id = attribute.Value
                    If String.Equals(attribute.Key, ClientSecretMetadataKey, StringComparison.Ordinal) AndAlso String.IsNullOrWhiteSpace(secret) Then secret = attribute.Value
                Next
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is InvalidOperationException
            End Try
        End If
        Return New ExchangeDriveClientConfig(id, secret)
    End Function

    Public Overrides Function ToString() As String
        Return "ExchangeDriveClientConfig"
    End Function
End Class

''' <summary>How a sign-in ended.</summary>
Friend Enum ExchangeDriveSignInOutcome
    Signed
    ''' <summary>No client id/secret (see <see cref="ExchangeDriveClientConfig"/>): the channel is absent.</summary>
    NotConfigured
    ''' <summary>The person turned the consent down.</summary>
    ConsentDenied
    ''' <summary>No redirect came back in time, or the caller cancelled.</summary>
    TimedOut
    NoBrowser
    NoPort
    ''' <summary>The token came back with a scope other than `drive.appdata` alone (item CM): refused, nothing stored.</summary>
    ScopeMismatch
    ''' <summary>The token exchange was refused or its answer had no refresh token.</summary>
    TokenRefused
    ''' <summary>The token file could not be written.</summary>
    StorageFailed
    Offline
End Enum

''' <summary>The tokens of one exchange, as Google answers.</summary>
Friend NotInheritable Class ExchangeDriveTokenResponse

    Public AccessToken As String = ""
    Public RefreshToken As String = ""
    Public ExpiresInSeconds As Integer
    Public Scope As String = ""
    Public [Error] As String = ""

    Public Overrides Function ToString() As String
        Return "ExchangeDriveTokenResponse"
    End Function
End Class

''' <summary>The signed-in state of this PC for the Drive channel, and the token source of its calls.</summary>
Friend NotInheritable Class ExchangeDriveSession
    Implements IExchangeDriveTokens

    ''' <summary>How long before expiry an access token is replaced.</summary>
    Private Const RefreshMarginSeconds As Integer = 60
    ''' <summary>The wait for the person at the consent screen.</summary>
    Public Const DefaultConsentWaitSeconds As Integer = 300

    Private ReadOnly _http As HttpClient
    Private ReadOnly _store As ExchangeDriveTokenStore
    Private ReadOnly _config As ExchangeDriveClientConfig
    Private ReadOnly _now As Func(Of DateTimeOffset)
    Private ReadOnly _gate As New SemaphoreSlim(1, 1)

    Private _accessToken As String = ""
    Private _accessExpires As DateTimeOffset

    Public Sub New(http As HttpClient, store As ExchangeDriveTokenStore, config As ExchangeDriveClientConfig, Optional now As Func(Of DateTimeOffset) = Nothing)
        If http Is Nothing Then Throw New ArgumentNullException(NameOf(http))
        If store Is Nothing Then Throw New ArgumentNullException(NameOf(store))
        If config Is Nothing Then Throw New ArgumentNullException(NameOf(config))
        _http = http
        _store = store
        _config = config
        _now = If(now, Function() DateTimeOffset.UtcNow)
    End Sub

    ''' <summary>True when a refresh token for this client is stored: the person is signed in on this PC. A pure read
    ''' (it makes no network call and creates nothing).</summary>
    Public Function IsSignedIn() As Boolean
        If Not _config.IsConfigured Then Return False
        Dim token As String = ""
        Return _store.TryLoad(_config.ClientId, token)
    End Function

    Public ReadOnly Property IsConfigured As Boolean
        Get
            Return _config.IsConfigured
        End Get
    End Property

    ' ------------------------------------------------------------------------------------------ sign in

    ''' <summary>Runs the consent flow. <paramref name="openBrowser"/> opens the URL it is given (False: it could not).
    ''' <paramref name="landingHtml"/> is the page the browser shows after the redirect (the caller's, localized).</summary>
    Public Async Function SignInAsync(openBrowser As Func(Of String, Boolean), landingHtml As String, waitSeconds As Integer, cancellation As CancellationToken) As Task(Of ExchangeDriveSignInOutcome)
        If Not _config.IsConfigured Then Return ExchangeDriveSignInOutcome.NotConfigured

        Dim listener As HttpListener = Nothing
        Dim redirectUri As String = ""
        If Not TryStartLoopback(listener, redirectUri) Then Return ExchangeDriveSignInOutcome.NoPort

        Try
            Dim codeVerifier As String = ExchangeDriveAuth.NewCodeVerifier()
            Dim state As String = ExchangeDriveAuth.NewState()
            Dim authorizationUrl As String = ExchangeDriveAuth.BuildAuthorizationUrl(_config.ClientId, redirectUri, ExchangeDriveAuth.CodeChallenge(codeVerifier), state)
            ' 203 rule 1, twice: the URL about to leave this process is re-read against the one constant.
            If ExchangeDriveAuth.ScopeViolation(authorizationUrl).Length > 0 Then Throw New InvalidOperationException("scope guard")

            Dim opened As Boolean = False
            Try
                opened = openBrowser(authorizationUrl)
            Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is IOException
                opened = False
            End Try
            If Not opened Then Return ExchangeDriveSignInOutcome.NoBrowser

            ' One deadline for the whole consent. A request that carries none of state, code or error (a favicon fetch, a
            ' scanner) is not the redirect: it is answered 404 and the wait goes on.
            Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(Math.Max(5, waitSeconds))
            Dim received As HttpListenerContext = Nothing
            Do
                Dim remaining As TimeSpan = deadline - DateTime.UtcNow
                If remaining <= TimeSpan.Zero Then Return ExchangeDriveSignInOutcome.TimedOut
                Dim contextTask As Task(Of HttpListenerContext) = listener.GetContextAsync()
                Dim timer As Task = Task.Delay(remaining, cancellation)
                Dim finished As Task = Await Task.WhenAny(contextTask, timer).ConfigureAwait(False)
                If finished IsNot contextTask Then Return ExchangeDriveSignInOutcome.TimedOut
                Dim candidate As HttpListenerContext = contextTask.Result
                Dim query = candidate.Request.QueryString
                If query("state") IsNot Nothing OrElse query("code") IsNot Nothing OrElse query("error") IsNot Nothing Then
                    received = candidate
                    Exit Do
                End If
                candidate.Response.StatusCode = 404
                candidate.Response.Close()
            Loop

            Dim query2 = received.Request.QueryString
            Dim consentError As String = If(query2("error"), "")
            Dim stateMatches As Boolean = String.Equals(query2("state"), state, StringComparison.Ordinal)
            Dim code As String = If(query2("code"), "")
            AnswerBrowser(received, landingHtml)

            If consentError.Length > 0 Then Return ExchangeDriveSignInOutcome.ConsentDenied
            ' A state that does not match means something else answered on the port: nothing is exchanged.
            If Not stateMatches OrElse code.Length = 0 Then Return ExchangeDriveSignInOutcome.TokenRefused

            Dim response As ExchangeDriveTokenResponse = Nothing
            Dim reached As Boolean = False
            Dim body As String = ExchangeDriveAuth.TokenRequestBody(code, _config.ClientId, _config.ClientSecret, redirectUri, codeVerifier)
            Try
                response = Await PostTokenAsync(ExchangeDriveAuth.TokenEndpoint, body, cancellation).ConfigureAwait(False)
                reached = True
            Catch ex As HttpRequestException
                reached = False
            End Try
            If Not reached Then Return ExchangeDriveSignInOutcome.Offline
            If response.AccessToken.Length = 0 OrElse response.RefreshToken.Length = 0 Then Return ExchangeDriveSignInOutcome.TokenRefused

            ' Item CM: the grant is exactly the one scope. Anything else - more, fewer, different - is refused before the
            ' token is kept, and revoked so it does not linger at Google.
            If Not GrantIsExactlyTheScope(response.Scope) Then
                Await RevokeQuietlyAsync(response.RefreshToken, cancellation).ConfigureAwait(False)
                Return ExchangeDriveSignInOutcome.ScopeMismatch
            End If

            If Not _store.Save(_config.ClientId, response.RefreshToken) Then
                Await RevokeQuietlyAsync(response.RefreshToken, cancellation).ConfigureAwait(False)
                Return ExchangeDriveSignInOutcome.StorageFailed
            End If
            Remember(response)
            Return ExchangeDriveSignInOutcome.Signed
        Finally
            Try
                listener.Close()
            Catch ex As ObjectDisposedException
            End Try
        End Try
    End Function

    ''' <summary>True when the space-separated list granted is exactly <see cref="ExchangeDriveAuth.Scope"/>.</summary>
    Public Shared Function GrantIsExactlyTheScope(granted As String) As Boolean
        If String.IsNullOrWhiteSpace(granted) Then Return False
        Dim parts As String() = granted.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Return parts.Length = 1 AndAlso String.Equals(parts(0), ExchangeDriveAuth.Scope, StringComparison.Ordinal)
    End Function

    Private Shared Function TryStartLoopback(ByRef listener As HttpListener, ByRef redirectUri As String) As Boolean
        Dim picker As New Random()
        For attempt As Integer = 1 To 20
            Dim port As Integer = picker.Next(49152, 65536)
            Dim candidate As New HttpListener()
            candidate.Prefixes.Add(ExchangeDriveAuth.NewRedirectUri(port))
            Try
                candidate.Start()
                listener = candidate
                redirectUri = ExchangeDriveAuth.NewRedirectUri(port)
                Return True
            Catch ex As HttpListenerException
                candidate.Close()
            End Try
        Next
        Return False
    End Function

    Private Shared Sub AnswerBrowser(context As HttpListenerContext, landingHtml As String)
        Try
            Dim page As Byte() = Encoding.UTF8.GetBytes(If(landingHtml, ""))
            context.Response.ContentType = "text/html; charset=utf-8"
            context.Response.ContentLength64 = page.Length
            context.Response.OutputStream.Write(page, 0, page.Length)
            context.Response.OutputStream.Close()
        Catch ex As Exception When TypeOf ex Is HttpListenerException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------ the token source

    ''' <summary>A valid access token for the signed-in PC, refreshing it when it is within a minute of expiry. "" when
    ''' signed out - or when Google no longer honours the refresh token (revoked, expired): the stored file is then deleted
    ''' and the person signs in again (CT); nothing else is deleted.</summary>
    Public Async Function GetAccessTokenAsync(cancellation As CancellationToken) As Task(Of String) Implements IExchangeDriveTokens.GetAccessTokenAsync
        If Not _config.IsConfigured Then Return ""
        Await _gate.WaitAsync(cancellation).ConfigureAwait(False)
        Try
            If _accessToken.Length > 0 AndAlso _now() < _accessExpires.AddSeconds(-RefreshMarginSeconds) Then Return _accessToken

            Dim refresh As String = ""
            If Not _store.TryLoad(_config.ClientId, refresh) Then
                _accessToken = ""
                Return ""
            End If

            Dim response As ExchangeDriveTokenResponse = Nothing
            Dim reached As Boolean = False
            Try
                response = Await PostTokenAsync(ExchangeDriveAuth.TokenEndpoint, ExchangeDriveAuth.RefreshRequestBody(refresh, _config.ClientId, _config.ClientSecret), cancellation).ConfigureAwait(False)
                reached = True
            Catch ex As HttpRequestException
                reached = False
            End Try
            If Not reached Then Throw New ExchangeDriveException(ExchangeDriveFailure.Offline, 0)

            If response.AccessToken.Length = 0 Then
                If String.Equals(response.Error, "invalid_grant", StringComparison.Ordinal) Then
                    ' Revoked at myaccount.google.com, expired, or the password changed: this token will never work again.
                    _store.Delete()
                    _accessToken = ""
                    Return ""
                End If
                Throw New ExchangeDriveException(ExchangeDriveFailure.Unavailable, 0)
            End If
            Remember(response)
            Return _accessToken
        Finally
            _gate.Release()
        End Try
    End Function

    Public Sub Invalidate() Implements IExchangeDriveTokens.Invalidate
        _accessToken = ""
    End Sub

    Private Sub Remember(response As ExchangeDriveTokenResponse)
        _accessToken = response.AccessToken
        _accessExpires = _now().AddSeconds(If(response.ExpiresInSeconds > 0, response.ExpiresInSeconds, 3600))
    End Sub

    ' ------------------------------------------------------------------------------------------ sign out

    ''' <summary>Revokes the refresh token at Google and deletes the stored file; the in-memory access token goes too. The
    ''' file is deleted even when Google cannot be reached (the person asked to leave); the result says whether Google
    ''' confirmed the revocation, so the caller can tell the person to finish it at myaccount.google.com when not.</summary>
    Public Async Function SignOutAsync(cancellation As CancellationToken) As Task(Of Boolean)
        Dim revoked As Boolean = False
        Dim refresh As String = ""
        If _config.IsConfigured AndAlso _store.TryLoad(_config.ClientId, refresh) Then
            revoked = Await RevokeAsync(refresh, cancellation).ConfigureAwait(False)
        End If
        _store.Delete()
        _accessToken = ""
        Return revoked
    End Function

    Private Async Function RevokeAsync(token As String, cancellation As CancellationToken) As Task(Of Boolean)
        Try
            Using request As New HttpRequestMessage(HttpMethod.Post, ExchangeDriveAuth.RevokeEndpoint)
                request.Content = New StringContent(ExchangeDriveAuth.RevokeRequestBody(token), Encoding.UTF8, "application/x-www-form-urlencoded")
                Using response As HttpResponseMessage = Await _http.SendAsync(request, cancellation).ConfigureAwait(False)
                    ' 200 revoked; 400 invalid_token means it was already revoked or expired - the wanted end state.
                    Return response.IsSuccessStatusCode OrElse CInt(response.StatusCode) = 400
                End Using
            End Using
        Catch ex As HttpRequestException
            Return False
        Catch ex As TaskCanceledException When Not cancellation.IsCancellationRequested
            Return False
        End Try
    End Function

    Private Async Function RevokeQuietlyAsync(token As String, cancellation As CancellationToken) As Task
        Await RevokeAsync(token, cancellation).ConfigureAwait(False)
    End Function

    ' ------------------------------------------------------------------------------------------ the token endpoint

    Private Async Function PostTokenAsync(endpoint As String, body As String, cancellation As CancellationToken) As Task(Of ExchangeDriveTokenResponse)
        Using request As New HttpRequestMessage(HttpMethod.Post, endpoint)
            request.Content = New StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
            Using response As HttpResponseMessage = Await _http.SendAsync(request, cancellation).ConfigureAwait(False)
                Dim json As String = Await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(False)
                Return ParseTokenResponse(json)
            End Using
        End Using
    End Function

    ''' <summary>Reads Google's token answer, success or error. Never throws on content; a body that is no JSON object reads
    ''' as an empty response (no access token), which every caller treats as a refusal.</summary>
    Public Shared Function ParseTokenResponse(json As String) As ExchangeDriveTokenResponse
        Dim result As New ExchangeDriveTokenResponse()
        If String.IsNullOrEmpty(json) Then Return result
        Try
            Using document As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = document.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return result
                result.AccessToken = Text(root, "access_token")
                result.RefreshToken = Text(root, "refresh_token")
                result.Scope = Text(root, "scope")
                result.Error = Text(root, "error")
                Dim expires As JsonElement = Nothing
                Dim seconds As Integer = 0
                If root.TryGetProperty("expires_in", expires) AndAlso expires.ValueKind = JsonValueKind.Number AndAlso expires.TryGetInt32(seconds) Then result.ExpiresInSeconds = seconds
            End Using
        Catch ex As JsonException
        End Try
        Return result
    End Function

    Private Shared Function Text(element As JsonElement, name As String) As String
        Dim el As JsonElement = Nothing
        If element.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

End Class
#End If
