#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports Xunit

''' <summary>
''' Specification 203 acceptance criteria 1 and 4 - the refresh token under DPAPI (never plain on disk, bound to the OAuth
''' client, replaced atomically, nothing created by asking), the loopback sign-in with its scope and state checks, the
''' access token refreshed a minute before it expires, and the sign-out that deletes the device's own records, revokes the
''' token and removes the file.
''' </summary>
Public Class ExchangeDriveSessionTests
    Implements IDisposable

    Private Const ClientId As String = "test-client.apps.example"
    Private Const ClientSecret As String = "test-secret"
    Private Const IdA As String = "AAAAAAAAAAAAAAAAAAAAAA"

    Private ReadOnly _temp As New List(Of String)

    ''' <summary>A cipher that is not DPAPI: reverses the bytes behind a marker, and refuses what it did not produce.</summary>
    Private NotInheritable Class ReversingCipher
        Implements IExchangeSecretCipher

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            Dim blob(plain.Length + 3) As Byte
            blob(0) = &H46 : blob(1) = &H4B : blob(2) = &H45 : blob(3) = &H21
            For index As Integer = 0 To plain.Length - 1
                blob(4 + index) = plain(plain.Length - 1 - index)
            Next
            Return blob
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            If blob.Length < 4 OrElse blob(0) <> &H46 OrElse blob(3) <> &H21 Then Throw New CryptographicException("not ours")
            Dim plain(blob.Length - 5) As Byte
            For index As Integer = 0 To plain.Length - 1
                plain(index) = blob(blob.Length - 1 - index)
            Next
            Return plain
        End Function
    End Class

    Private NotInheritable Class OfflineHandler
        Inherits HttpMessageHandler

        Protected Overrides Function SendAsync(request As HttpRequestMessage, cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)
            Throw New HttpRequestException("no network")
        End Function
    End Class

    Private NotInheritable Class ClockBox
        Public Value As DateTimeOffset = New DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)
        Public Function Now() As DateTimeOffset
            Return Value
        End Function
    End Class

    Private Function NewFolder() As String
        Dim path As String = Directory.CreateTempSubdirectory("fmsgd").FullName
        _temp.Add(path)
        Return path
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each path As String In _temp
            Try
                Directory.Delete(path, True)
            Catch ex As IOException
            End Try
        Next
    End Sub

    Private Function NewStore(Optional cipher As IExchangeSecretCipher = Nothing) As ExchangeDriveTokenStore
        Return New ExchangeDriveTokenStore(Path.Combine(NewFolder(), "exchange"), cipher)
    End Function

    Private Shared Function NewSession(rig As DriveRig, store As ExchangeDriveTokenStore, Optional clock As ClockBox = Nothing) As ExchangeDriveSession
        Dim now As Func(Of DateTimeOffset) = Nothing
        If clock IsNot Nothing Then now = AddressOf clock.Now
        Return New ExchangeDriveSession(rig.Http, store, New ExchangeDriveClientConfig(ClientId, ClientSecret), now)
    End Function

    ''' <summary>What the person's browser does: opens the URL it was given and, a moment later, lands on the redirect.</summary>
    Private NotInheritable Class Browser
        Public Reply As Func(Of System.Collections.Specialized.NameValueCollection, String) = Function(query) "code=auth-code-1&state=" & Uri.EscapeDataString(query("state"))
        Public LastAuthorizationUrl As String = ""
        Public LandingPage As String = ""
        Public StrayStatus As Integer
        Public SendStray As Boolean
        Public Done As Task = Task.CompletedTask

        Public Function Open(authorizationUrl As String) As Boolean
            LastAuthorizationUrl = authorizationUrl
            Dim query = System.Web.HttpUtility.ParseQueryString(New Uri(authorizationUrl).Query)
            Dim redirect As String = query("redirect_uri")
            Dim answer As String = Reply(query)
            Done = Task.Run(
                Async Function() As Task
                    Using http As New HttpClient()
                        If SendStray Then
                            Using stray As HttpResponseMessage = Await http.GetAsync(redirect & "favicon.ico")
                                StrayStatus = CInt(stray.StatusCode)
                            End Using
                        End If
                        Using landing As HttpResponseMessage = Await http.GetAsync(redirect & "?" & answer)
                            LandingPage = Await landing.Content.ReadAsStringAsync()
                        End Using
                    End Using
                End Function)
            Return True
        End Function
    End Class

    Private Shared Function NoShell(url As String) As Boolean
        Throw New InvalidOperationException("no shell")
    End Function

    Private Shared Function QueryOf(url As String) As System.Collections.Specialized.NameValueCollection
        Return System.Web.HttpUtility.ParseQueryString(New Uri(url).Query)
    End Function

    ' ------------------------------------------------------------------------------------------ the token store

    <Fact>
    Public Sub Asking_for_the_token_creates_nothing_on_a_PC_that_never_signed_in()
        Dim root As String = Path.Combine(NewFolder(), "exchange")
        Dim store As New ExchangeDriveTokenStore(root, New ReversingCipher())
        Assert.False(store.Exists())
        Dim token As String = "x"
        Assert.False(store.TryLoad(ClientId, token))
        Assert.Equal("", token)
        store.Delete()
        Assert.False(Directory.Exists(root))
    End Sub

    <Fact>
    Public Sub A_token_is_saved_protected_and_read_back_only_for_the_client_it_was_issued_to()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Const secretToken As String = "1//0gRefreshTokenValue-abc_DEF"
        Assert.True(store.Save(ClientId, secretToken))
        Assert.True(store.Exists())

        Dim raw As String = Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath))
        Assert.DoesNotContain(secretToken, raw)

        Dim loaded As String = ""
        Assert.True(store.TryLoad(ClientId, loaded))
        Assert.Equal(secretToken, loaded)
        Assert.False(store.TryLoad("another-client", loaded))
        Assert.Equal("", loaded)
    End Sub

    <Fact>
    Public Sub A_second_save_replaces_the_first_atomically_and_leaves_no_temporary_file()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Assert.True(store.Save(ClientId, "first-token"))
        Assert.True(store.Save(ClientId, "second-token"))
        Dim loaded As String = ""
        Assert.True(store.TryLoad(ClientId, loaded))
        Assert.Equal("second-token", loaded)
        Assert.Equal(New String() {"drive-token.dat"}, Directory.GetFiles(Path.GetDirectoryName(store.FilePath)).Select(Function(f) Path.GetFileName(f)).ToArray())
    End Sub

    <Fact>
    Public Sub A_token_that_could_not_be_one_is_refused_and_no_file_appears()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Assert.False(store.Save(ClientId, ""))
        Assert.False(store.Save(ClientId, "has space"))
        Assert.False(store.Save(ClientId, "line" & vbLf & "break"))
        Assert.False(store.Save(ClientId, New String("a"c, 2049)))
        Assert.False(store.Save("", "token"))
        Assert.False(store.Exists())
    End Sub

    <Fact>
    Public Sub A_damaged_file_or_another_users_blob_reads_as_no_token_and_delete_touches_only_the_file()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Assert.True(store.Save(ClientId, "token"))
        Dim neighbour As String = Path.Combine(Path.GetDirectoryName(store.FilePath), "device.dat")
        File.WriteAllText(neighbour, "keep")

        File.WriteAllBytes(store.FilePath, New Byte() {1, 2, 3, 4, 5, 6})
        Dim loaded As String = ""
        Assert.False(store.TryLoad(ClientId, loaded))

        store.Delete()
        Assert.False(store.Exists())
        Assert.True(File.Exists(neighbour))
    End Sub

    <Fact>
    Public Sub With_DPAPI_the_file_holds_no_readable_token_and_a_blob_of_another_entropy_is_not_taken()
        Dim store As ExchangeDriveTokenStore = NewStore()
        Const secretToken As String = "1//0gRealDpapiRefreshToken-xyz"
        Assert.True(store.Save(ClientId, secretToken))
        Dim bytes As Byte() = File.ReadAllBytes(store.FilePath)
        Assert.DoesNotContain(secretToken, Encoding.UTF8.GetString(bytes))
        Assert.DoesNotContain(secretToken, Encoding.Unicode.GetString(bytes))
        Dim loaded As String = ""
        Assert.True(store.TryLoad(ClientId, loaded))
        Assert.Equal(secretToken, loaded)

        ' protected for the same user but with the exchange device token's entropy: not a Drive token
        Dim foreign As Byte() = ProtectedData.Protect(Encoding.UTF8.GetBytes("FMSXGDT1" & vbLf & ExchangeDriveTokenStore.ClientTag(ClientId) & vbLf & "other"), Encoding.UTF8.GetBytes("some.other.entropy"), DataProtectionScope.CurrentUser)
        File.WriteAllBytes(store.FilePath, foreign)
        Assert.False(store.TryLoad(ClientId, loaded))
    End Sub

    ' ------------------------------------------------------------------------------------------ sign in

    <Fact>
    Public Async Function Without_a_client_the_channel_is_absent_and_asks_nobody() As Task
        Dim rig As New DriveRig()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As New ExchangeDriveSession(rig.Http, store, New ExchangeDriveClientConfig("", ""))
        Assert.False(session.IsConfigured)
        Assert.False(session.IsSignedIn())
        Dim browser As New Browser()
        Assert.Equal(ExchangeDriveSignInOutcome.NotConfigured, Await session.SignInAsync(AddressOf browser.Open, "", 5, CancellationToken.None))
        Assert.Equal("", browser.LastAuthorizationUrl)
        Assert.Equal("", Await session.GetAccessTokenAsync(CancellationToken.None))
        Assert.Equal(0, rig.Drive.RequestCount)
        Assert.False(store.Exists())
    End Function

    <Fact>
    Public Async Function Signing_in_keeps_the_refresh_token_under_DPAPI_and_asks_for_the_one_scope() As Task
        Dim rig As New DriveRig()
        Dim store As ExchangeDriveTokenStore = NewStore()
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Dim browser As New Browser()
        Assert.False(session.IsSignedIn())

        Dim outcome As ExchangeDriveSignInOutcome = Await session.SignInAsync(AddressOf browser.Open, "<html>signed in</html>", 30, CancellationToken.None)
        Assert.Equal(ExchangeDriveSignInOutcome.Signed, outcome)
        Await browser.Done
        Assert.Equal("<html>signed in</html>", browser.LandingPage)

        ' what left for Google: one scope, PKCE S256, the loopback address, no secret in the URL
        Dim query = QueryOf(browser.LastAuthorizationUrl)
        Assert.Equal("https://www.googleapis.com/auth/drive.appdata", query("scope"))
        Assert.Equal("S256", query("code_challenge_method"))
        Assert.Equal(ClientId, query("client_id"))
        Assert.StartsWith("http://127.0.0.1:", query("redirect_uri"), StringComparison.Ordinal)
        Assert.DoesNotContain(ClientSecret, browser.LastAuthorizationUrl)
        Assert.Contains("code_verifier=", rig.Drive.LastExchangeBody)
        Assert.Contains("auth-code-1", rig.Drive.LastExchangeBody)

        ' criterion 1: the refresh token is on disk, under DPAPI, and readable only through the store
        Assert.True(session.IsSignedIn())
        Dim bytes As Byte() = File.ReadAllBytes(store.FilePath)
        Assert.DoesNotContain(rig.Drive.LastRefreshToken, Encoding.UTF8.GetString(bytes))
        Dim loaded As String = ""
        Assert.True(store.TryLoad(ClientId, loaded))
        Assert.Equal(rig.Drive.LastRefreshToken, loaded)
    End Function

    <Fact>
    Public Async Function A_request_that_is_not_the_redirect_is_answered_404_and_the_wait_goes_on() As Task
        Dim rig As New DriveRig()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Dim browser As New Browser() With {.SendStray = True}
        Assert.Equal(ExchangeDriveSignInOutcome.Signed, Await session.SignInAsync(AddressOf browser.Open, "ok", 30, CancellationToken.None))
        Await browser.Done
        Assert.Equal(404, browser.StrayStatus)
    End Function

    <Fact>
    Public Async Function An_answer_with_the_wrong_state_exchanges_nothing_and_stores_nothing() As Task
        Dim rig As New DriveRig()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Dim browser As New Browser() With {.Reply = Function(query) "code=evil&state=not-the-state"}
        Assert.Equal(ExchangeDriveSignInOutcome.TokenRefused, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
        Await browser.Done
        Assert.Equal("", rig.Drive.LastExchangeBody)
        Assert.False(store.Exists())
    End Function

    <Fact>
    Public Async Function A_consent_that_was_turned_down_ends_the_sign_in_without_a_token() As Task
        Dim rig As New DriveRig()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Dim browser As New Browser() With {.Reply = Function(query) "error=access_denied&state=" & Uri.EscapeDataString(query("state"))}
        Assert.Equal(ExchangeDriveSignInOutcome.ConsentDenied, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
        Await browser.Done
        Assert.False(store.Exists())
        Assert.Equal(0, rig.Drive.RequestCount)
    End Function

    <Fact>
    Public Async Function A_grant_wider_or_other_than_the_one_scope_is_refused_revoked_and_not_kept() As Task
        For Each granted As String In New String() {
                "https://www.googleapis.com/auth/drive.appdata https://www.googleapis.com/auth/drive",
                "https://www.googleapis.com/auth/drive.file",
                "https://www.googleapis.com/auth/drive",
                ""}
            Dim rig As New DriveRig()
            rig.Drive.GrantedScope = granted
            Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
            Dim session As ExchangeDriveSession = NewSession(rig, store)
            Dim browser As New Browser()
            Assert.Equal(ExchangeDriveSignInOutcome.ScopeMismatch, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
            Await browser.Done
            Assert.False(store.Exists())
            Assert.Contains(rig.Drive.LastRefreshToken, rig.Drive.Revoked)
        Next
    End Function

    <Fact>
    Public Async Function An_answer_without_a_refresh_token_is_refused() As Task
        Dim rig As New DriveRig()
        rig.Drive.IssueRefreshToken = False
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Dim browser As New Browser()
        Assert.Equal(ExchangeDriveSignInOutcome.TokenRefused, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
        Await browser.Done
        Assert.False(store.Exists())
    End Function

    <Fact>
    Public Async Function No_browser_and_no_answer_end_the_sign_in_without_hanging() As Task
        Dim rig As New DriveRig()
        Dim session As ExchangeDriveSession = NewSession(rig, NewStore(New ReversingCipher()))
        Assert.Equal(ExchangeDriveSignInOutcome.NoBrowser, Await session.SignInAsync(Function(u) False, "x", 30, CancellationToken.None))
        Assert.Equal(ExchangeDriveSignInOutcome.NoBrowser, Await session.SignInAsync(AddressOf NoShell, "x", 30, CancellationToken.None))

        Using source As New CancellationTokenSource(TimeSpan.FromMilliseconds(300))
            Dim started As DateTime = DateTime.UtcNow
            Assert.Equal(ExchangeDriveSignInOutcome.TimedOut, Await session.SignInAsync(Function(u) True, "x", 30, source.Token))
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5))
        End Using
    End Function

    <Fact>
    Public Async Function A_sign_in_with_no_network_reports_offline_and_stores_nothing() As Task
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Using http As New HttpClient(New OfflineHandler())
            Dim session As New ExchangeDriveSession(http, store, New ExchangeDriveClientConfig(ClientId, ClientSecret))
            Dim browser As New Browser()
            Assert.Equal(ExchangeDriveSignInOutcome.Offline, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
            Await browser.Done
            Assert.False(store.Exists())
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ the access token

    <Fact>
    Public Async Function The_access_token_is_reused_until_a_minute_before_it_expires_then_refreshed() As Task
        Dim rig As New DriveRig()
        Dim clock As New ClockBox()
        Dim session As ExchangeDriveSession = NewSession(rig, NewStore(New ReversingCipher()), clock)
        Dim browser As New Browser()
        Assert.Equal(ExchangeDriveSignInOutcome.Signed, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
        Await browser.Done

        Dim first As String = Await session.GetAccessTokenAsync(CancellationToken.None)
        Assert.NotEqual("", first)
        Dim requests As Integer = rig.Drive.RequestCount
        clock.Value = clock.Value.AddSeconds(3539)
        Assert.Equal(first, Await session.GetAccessTokenAsync(CancellationToken.None))
        Assert.Equal(requests, rig.Drive.RequestCount)

        clock.Value = clock.Value.AddSeconds(2) ' 3541 s: inside the last minute
        Dim second As String = Await session.GetAccessTokenAsync(CancellationToken.None)
        Assert.NotEqual(first, second)
        Assert.Equal(requests + 1, rig.Drive.RequestCount)

        session.Invalidate()
        Assert.NotEqual(second, Await session.GetAccessTokenAsync(CancellationToken.None))
    End Function

    <Fact>
    Public Async Function A_refresh_token_that_Google_no_longer_honours_is_deleted_and_the_person_is_signed_out() As Task
        Dim rig As New DriveRig()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Dim browser As New Browser()
        Assert.Equal(ExchangeDriveSignInOutcome.Signed, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
        Await browser.Done

        rig.Drive.RefreshRevoked = True ' revoked at myaccount.google.com
        session.Invalidate()
        Assert.Equal("", Await session.GetAccessTokenAsync(CancellationToken.None))
        Assert.False(store.Exists())
        Assert.False(session.IsSignedIn())
    End Function

    <Fact>
    Public Async Function With_no_network_the_token_stays_and_the_failure_says_offline() As Task
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Assert.True(store.Save(ClientId, "kept-token"))
        Using http As New HttpClient(New OfflineHandler())
            Dim session As New ExchangeDriveSession(http, store, New ExchangeDriveClientConfig(ClientId, ClientSecret))
            Dim failure As ExchangeDriveException = Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() session.GetAccessTokenAsync(CancellationToken.None))
            Assert.Equal(ExchangeDriveFailure.Offline, failure.Failure)
            Assert.True(store.Exists())
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ sign out, criterion 4

    <Fact>
    Public Async Function Signing_out_deletes_the_own_records_then_revokes_the_token_and_clears_the_file() As Task
        Dim rig As New DriveRig()
        rig.Drive.ValidateBearer = True ' every call needs a live access token
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Dim browser As New Browser()
        Assert.Equal(ExchangeDriveSignInOutcome.Signed, Await session.SignInAsync(AddressOf browser.Open, "x", 30, CancellationToken.None))
        Await browser.Done

        Dim client As New ExchangeDriveClient(rig.Http, session)
        Dim directory As New ExchangeDriveDirectory(client, IdA)
        Dim shareId As String = ExchangeIds.NewId()
        Await directory.PublishAsync(ExchangeDriveRecordKind.Device, IdA, "{""deviceId"":""" & IdA & """,""deviceName"":""Office PC"",""product"":""FastMediaSorter"",""productVersion"":""46"",""platform"":""windows"",""roles"":[""consumer""],""capabilities"":[]}", 0, CancellationToken.None)
        Await directory.PublishAsync(ExchangeDriveRecordKind.Resource, shareId, "{""resourceId"":""" & shareId & """,""deviceId"":""" & IdA & """,""kind"":""" & ExchangeWire.KindSftpShare & """,""name"":""Photos"",""access"":{""host"":""192.0.2.10"",""port"":22}}", 0, CancellationToken.None)
        rig.Drive.AddFile("someone-elses.json", New Byte() {1}) ' a file of another app/device in the same folder stays
        Assert.Equal(3, rig.Drive.Snapshot().Count)

        ' the order of the sign-out: the records need a live token, so they go first
        Assert.Equal(2, Await directory.DeleteAllOwnAsync(CancellationToken.None))
        Dim refreshToken As String = rig.Drive.LastRefreshToken
        Assert.True(Await session.SignOutAsync(CancellationToken.None))

        Assert.Equal(1, rig.Drive.Snapshot().Count)
        Assert.NotNull(rig.Drive.Find("someone-elses.json"))
        Assert.Contains(refreshToken, rig.Drive.Revoked)
        Assert.False(store.Exists())
        Assert.False(session.IsSignedIn())

        ' nothing works afterwards, and nothing is asked of Google
        Dim requests As Integer = rig.Drive.RequestCount
        Assert.Equal("", Await session.GetAccessTokenAsync(CancellationToken.None))
        Assert.Equal(requests, rig.Drive.RequestCount)
        Await Assert.ThrowsAsync(Of ExchangeDriveException)(Function() client.ListAllAsync(CancellationToken.None))
    End Function

    <Fact>
    Public Async Function Signing_out_with_no_network_still_removes_the_file_and_says_the_revocation_is_unconfirmed() As Task
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Assert.True(store.Save(ClientId, "kept-token"))
        Using http As New HttpClient(New OfflineHandler())
            Dim session As New ExchangeDriveSession(http, store, New ExchangeDriveClientConfig(ClientId, ClientSecret))
            Assert.False(Await session.SignOutAsync(CancellationToken.None))
            Assert.False(store.Exists())
        End Using
    End Function

    <Fact>
    Public Async Function Signing_out_when_never_signed_in_asks_nobody_and_creates_nothing() As Task
        Dim rig As New DriveRig()
        Dim store As ExchangeDriveTokenStore = NewStore(New ReversingCipher())
        Dim session As ExchangeDriveSession = NewSession(rig, store)
        Assert.False(Await session.SignOutAsync(CancellationToken.None))
        Assert.Equal(0, rig.Drive.RequestCount)
        Assert.False(Directory.Exists(Path.GetDirectoryName(store.FilePath)))
    End Function

    <Fact>
    Public Sub The_scope_guard_accepts_exactly_the_one_scope()
        Assert.True(ExchangeDriveSession.GrantIsExactlyTheScope("https://www.googleapis.com/auth/drive.appdata"))
        Assert.True(ExchangeDriveSession.GrantIsExactlyTheScope("  https://www.googleapis.com/auth/drive.appdata  "))
        Assert.False(ExchangeDriveSession.GrantIsExactlyTheScope(""))
        Assert.False(ExchangeDriveSession.GrantIsExactlyTheScope(Nothing))
        Assert.False(ExchangeDriveSession.GrantIsExactlyTheScope("https://www.googleapis.com/auth/drive.appdata https://www.googleapis.com/auth/drive.file"))
        Assert.False(ExchangeDriveSession.GrantIsExactlyTheScope("https://www.googleapis.com/auth/drive.appdata/extra"))
    End Sub

    <Fact>
    Public Sub A_token_answer_is_read_without_ever_throwing_on_content()
        Dim ok As ExchangeDriveTokenResponse = ExchangeDriveSession.ParseTokenResponse("{""access_token"":""a"",""refresh_token"":""r"",""expires_in"":3599,""scope"":""s""}")
        Assert.Equal("a", ok.AccessToken)
        Assert.Equal("r", ok.RefreshToken)
        Assert.Equal(3599, ok.ExpiresInSeconds)
        Assert.Equal("invalid_grant", ExchangeDriveSession.ParseTokenResponse("{""error"":""invalid_grant""}").Error)
        For Each junk As String In New String() {"", "not json", "[1,2]", "null", "{""access_token"":5}"}
            Assert.Equal("", ExchangeDriveSession.ParseTokenResponse(junk).AccessToken)
        Next
        Assert.Equal("ExchangeDriveTokenResponse", ok.ToString()) ' a log line never carries a token
    End Sub

End Class
#End If
