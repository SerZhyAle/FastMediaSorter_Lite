#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Security.Cryptography
Imports System.Text
Imports Xunit

''' <summary>
''' Specification 203 rule 1 and contract DEVICE-EXCHANGE 8.1 item CM: the Drive channel requests exactly
''' <c>https://www.googleapis.com/auth/drive.appdata</c> - no broader Drive scope, no openid, no e-mail, no
''' profile - and it signs in the installed-app way (a loopback redirect on 127.0.0.1 with PKCE S256, done\202
''' section 3). The constant and the URL builder live in one shared file the premise probe links, so the suite
''' holds them for the product before the product exists.
''' </summary>
Public Class ExchangeDriveAuthTests

    <Fact>
    Public Sub The_scope_is_exactly_the_one_constant()
        Assert.Equal("https://www.googleapis.com/auth/drive.appdata", ExchangeDriveAuth.Scope)
    End Sub

    <Fact>
    Public Sub The_authorization_url_requests_that_scope_alone()
        Dim url As String = ExchangeDriveAuth.BuildAuthorizationUrl("client-id", "http://127.0.0.1:53211/", "challenge", "state-x")
        Assert.Equal("", ExchangeDriveAuth.ScopeViolation(url))
        ' the pieces of the installed-app flow are all there, each exactly once
        For Each piece As String In {"client_id=client-id", "redirect_uri=http%3A%2F%2F127.0.0.1%3A53211%2F", "response_type=code",
                                     "code_challenge=challenge", "code_challenge_method=S256", "state=state-x",
                                     "access_type=offline", "prompt=consent"}
            Assert.Equal(1, occurrences(url, piece))
        Next
        ' and nothing of the forbidden vocabulary: no wider Drive scope, no identity scope, anywhere in the URL
        ' (a joined or wider scope would already differ from the one constant - ScopeViolation holds that;
        ' these are the plain words, which never need escaping)
        For Each forbidden As String In {"drive.file", "openid", "email", "profile"}
            Assert.DoesNotContain(forbidden, url, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub The_url_validator_refuses_any_other_scope()
        Dim good As String = ExchangeDriveAuth.BuildAuthorizationUrl("c", "http://127.0.0.1:1/", "ch", "st")
        Assert.Equal("", ExchangeDriveAuth.ScopeViolation(good))
        ' a joined second scope, however narrow, is a violation
        Assert.NotEqual("", ExchangeDriveAuth.ScopeViolation(good.Replace("scope=" & Uri.EscapeDataString(ExchangeDriveAuth.Scope), "scope=" & Uri.EscapeDataString(ExchangeDriveAuth.Scope & " https://www.googleapis.com/auth/drive.file"))))
        ' a wider scope is a violation
        Assert.NotEqual("", ExchangeDriveAuth.ScopeViolation(good.Replace(Uri.EscapeDataString(ExchangeDriveAuth.Scope), Uri.EscapeDataString("https://www.googleapis.com/auth/drive"))))
        ' no scope at all is a violation
        Assert.NotEqual("", ExchangeDriveAuth.ScopeViolation(ExchangeDriveAuth.AuthorizationEndpoint & "?response_type=code"))
        Assert.NotEqual("", ExchangeDriveAuth.ScopeViolation(""))
    End Sub

    <Fact>
    Public Sub The_challenge_is_the_base64url_sha256_of_the_verifier()
        ' an independent computation beside the one under test
        Using sha As SHA256 = SHA256.Create()
            Dim expected As String = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes("verifier"))).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
            Assert.Equal(expected, ExchangeDriveAuth.CodeChallenge("verifier"))
        End Using
    End Sub

    <Fact>
    Public Sub The_verifier_is_forty_three_url_safe_characters_and_never_repeats()
        Dim verifierAlphabet As New HashSet(Of Char)("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~".ToCharArray())
        Dim seen As New HashSet(Of String)
        For attempt = 1 To 50
            Dim verifier As String = ExchangeDriveAuth.NewCodeVerifier()
            Assert.Equal(43, verifier.Length)
            For Each c As Char In verifier
                Assert.True(verifierAlphabet.Contains(c), "a verifier character outside the RFC 7636 alphabet: " & c)
            Next
            Assert.DoesNotContain(verifier, seen)
            seen.Add(verifier)
        Next
    End Sub

    <Fact>
    Public Sub The_redirect_uri_is_the_loopback_ip_with_the_port()
        Assert.Equal("http://127.0.0.1:53211/", ExchangeDriveAuth.NewRedirectUri(53211))
        Assert.DoesNotContain("localhost", ExchangeDriveAuth.NewRedirectUri(8080), StringComparison.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub The_token_request_carries_the_code_and_verifier_and_no_scope()
        Dim body As String = ExchangeDriveAuth.TokenRequestBody("the-code", "the-id", "the-secret", "http://127.0.0.1:1/", "the-verifier")
        Dim fields As New Dictionary(Of String, String)
        For Each pair As String In body.Split("&"c)
            Dim eq As Integer = pair.IndexOf("="c)
            fields(Uri.UnescapeDataString(pair.Substring(0, eq))) = Uri.UnescapeDataString(pair.Substring(eq + 1))
        Next
        Assert.Equal(6, fields.Count)
        Assert.Equal("the-code", fields("code"))
        Assert.Equal("the-id", fields("client_id"))
        Assert.Equal("the-secret", fields("client_secret"))
        Assert.Equal("http://127.0.0.1:1/", fields("redirect_uri"))
        Assert.Equal("authorization_code", fields("grant_type"))
        Assert.Equal("the-verifier", fields("code_verifier"))
        Assert.False(fields.ContainsKey("scope"), "the token request names a scope - the code already carries the one scope it was granted with")
    End Sub

    <Fact>
    Public Sub The_state_is_long_enough_and_never_repeats()
        Dim seen As New HashSet(Of String)
        For attempt = 1 To 50
            Dim state As String = ExchangeDriveAuth.NewState()
            Assert.True(state.Length >= 16, "a state shorter than 16 characters")
            Assert.DoesNotContain(state, seen)
            seen.Add(state)
        Next
    End Sub

    Private Shared Function occurrences(haystack As String, needle As String) As Integer
        Dim count As Integer = 0
        Dim at As Integer = 0
        Do
            at = haystack.IndexOf(needle, at, StringComparison.Ordinal)
            If at < 0 Then Exit Do
            count += 1
            at += 1
        Loop
        Return count
    End Function

End Class
#End If
