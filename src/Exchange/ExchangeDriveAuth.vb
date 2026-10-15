#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Security.Cryptography
Imports System.Text

' Specification 203 step 1 (with the premise experiment of done\202) - the one place the Drive channel's OAuth
' identity lives. The channel's whole authority is the scope: `drive.appdata` and NOTHING else (contract
' DEVICE-EXCHANGE 8.1 item CM; 203 rule 1: no broader scope is ever requested - no `drive`, no `drive.file`,
' no `openid`, no e-mail, no profile). Every authorization URL of every caller is built here and every one is
' re-checked against the constant before it leaves (BuildAuthorizationUrl throws, the probe's scope test holds
' the constant in the unit suite) - a caller cannot widen the scope by mistake.
'
' The flow is the installed-app one: a loopback redirect on 127.0.0.1 (never `localhost`; done\202 section 3,
' with the Desktop-app client of item B of 8.2) with a random port and PKCE (S256), the system browser at
' Google's consent screen. A Desktop-app client's secret is
' not confidential - Google itself says so - and is still never logged, never stored and never committed; the
' probe of tests\Integration\DriveAppDataProbe reads it from the run's environment only.
'
' Pure logic only: no HTTP, no storage, no logging. The token exchange, the loopback listener and every Drive
' REST call stay in the caller (the probe today, the product only after the premise experiment of 8.2 passes).

''' <summary>The Drive channel's OAuth core: the scope constant, PKCE (S256), the authorization URL and the token
''' request body. Shared by the premise probe and, after the experiment of contract 8.2 passes, by the product.</summary>
Friend NotInheritable Class ExchangeDriveAuth

    Private Sub New()
    End Sub

    ''' <summary>The one scope the channel may ever request (contract DEVICE-EXCHANGE 8.1 item CM). Google's Drive
    ''' API treats the application data folder as the only thing this scope can read and write.</summary>
    Public Const Scope As String = "https://www.googleapis.com/auth/drive.appdata"

    ''' <summary>Google's OAuth authorization endpoint (installed-app flow).</summary>
    Public Const AuthorizationEndpoint As String = "https://accounts.google.com/o/oauth2/v2/auth"

    ''' <summary>Google's OAuth token endpoint.</summary>
    Public Const TokenEndpoint As String = "https://oauth2.googleapis.com/token"

    ''' <summary>The loopback host of the installed-app redirect (item B: 127.0.0.1, never `localhost`).</summary>
    Public Const LoopbackHost As String = "127.0.0.1"

    ''' <summary>The redirect address of a loopback listener that took <paramref name="port"/>. Google's installed-app
    ''' rules accept any port on the loopback IP; the trailing slash matches HttpListener's prefix form.</summary>
    Public Shared Function NewRedirectUri(port As Integer) As String
        Return "http://" & LoopbackHost & ":" & port.ToString(Globalization.CultureInfo.InvariantCulture) & "/"
    End Function

    ''' <summary>A fresh PKCE code verifier: 32 random bytes, base64url - 43 characters of the RFC 7636 verifier
    ''' alphabet ([A-Za-z0-9-_]), 256 bits of entropy.</summary>
    Public Shared Function NewCodeVerifier() As String
        Return Base64Url(RandomNumberGenerator.GetBytes(32))
    End Function

    ''' <summary>The S256 challenge for a verifier: base64url of the SHA-256 of its ASCII bytes, no padding (RFC 7636
    ''' section 4.2).</summary>
    Public Shared Function CodeChallenge(codeVerifier As String) As String
        Using sha As SHA256 = SHA256.Create()
            Dim hash As Byte() = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier))
            Return Base64Url(hash)
        End Using
    End Function

    ''' <summary>A fresh cross-site state value: 16 random bytes, base64url. The listener refuses a redirect whose
    ''' state does not match what was sent.</summary>
    Public Shared Function NewState() As String
        Return Base64Url(RandomNumberGenerator.GetBytes(16))
    End Function

    ''' <summary>The authorization URL the caller opens in the system browser. Carries exactly the parameters of the
    ''' installed-app flow: the one scope, PKCE S256, offline access so Google issues a refresh token, and the consent
    ''' prompt so a re-run gets one again. Throws if the URL it built does not satisfy <see cref="ScopeViolation"/> -
    ''' which is the constant's own guard, not an expected path.</summary>
    Public Shared Function BuildAuthorizationUrl(clientId As String, redirectUri As String, codeChallenge As String, state As String) As String
        Dim url As String = AuthorizationEndpoint &
            "?client_id=" & Escape(clientId) &
            "&redirect_uri=" & Escape(redirectUri) &
            "&response_type=code" &
            "&scope=" & Escape(Scope) &
            "&code_challenge=" & Escape(codeChallenge) &
            "&code_challenge_method=S256" &
            "&state=" & Escape(state) &
            "&access_type=offline" &
            "&prompt=consent"
        Dim violation As String = ScopeViolation(url)
        If violation.Length > 0 Then Throw New InvalidOperationException("the authorization URL failed its own scope guard: " & violation)
        Return url
    End Function

    ''' <summary>"" when the URL's scope parameter is exactly <see cref="Scope"/> (once, nothing joined to it),
    ''' otherwise the reason it is not. The guard of 203 rule 1: a scope list, an alias or a wider Drive scope is
    ''' a violation, and so is any second scope parameter however narrow.</summary>
    Public Shared Function ScopeViolation(authorizationUrl As String) As String
        If String.IsNullOrEmpty(authorizationUrl) Then Return "the URL is empty"
        Dim queryAt As Integer = authorizationUrl.IndexOf("?"c)
        If queryAt < 0 Then Return "the URL carries no query"
        Dim sawScope As Boolean = False
        For Each pair As String In authorizationUrl.Substring(queryAt + 1).Split("&"c)
            Dim eq As Integer = pair.IndexOf("="c)
            Dim name As String = If(eq < 0, pair, pair.Substring(0, eq))
            If Not String.Equals(Uri.UnescapeDataString(name), "scope", StringComparison.Ordinal) Then Continue For
            If sawScope Then Return "the URL carries a second scope parameter"
            sawScope = True
            Dim value As String = If(eq < 0, "", Uri.UnescapeDataString(pair.Substring(eq + 1)))
            If Not String.Equals(value, Scope, StringComparison.Ordinal) Then
                Return "the scope is """ & value & """, not the one constant"
            End If
        Next
        If Not sawScope Then Return "the URL carries no scope parameter"
        Return ""
    End Function

    ''' <summary>The body of the authorization-code token request: form-encoded, carrying the code, the client, the
    ''' redirect and the PKCE verifier - and no scope, because the code already carries the one scope it was granted
    ''' with.</summary>
    Public Shared Function TokenRequestBody(authorizationCode As String, clientId As String, clientSecret As String, redirectUri As String, codeVerifier As String) As String
        Return "code=" & Escape(authorizationCode) &
            "&client_id=" & Escape(clientId) &
            "&client_secret=" & Escape(clientSecret) &
            "&redirect_uri=" & Escape(redirectUri) &
            "&grant_type=authorization_code" &
            "&code_verifier=" & Escape(codeVerifier)
    End Function

    Private Shared Function Escape(value As String) As String
        Return Uri.EscapeDataString(value)
    End Function

    ''' <summary>Base64url (RFC 4648 section 5) without padding - the alphabet PKCE and OAuth state use.</summary>
    Private Shared Function Base64Url(bytes As Byte()) As String
        Return Convert.ToBase64String(bytes).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
    End Function

End Class
#End If
