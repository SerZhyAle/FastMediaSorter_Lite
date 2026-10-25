#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

' Specification 203 acceptance criterion 1: "stores refresh token encrypted under DPAPI". The refresh token is the one
' long-lived secret of the Drive channel (it can mint access tokens for the person's appDataFolder until revoked), so it is
' kept the way the exchange device token is (ExchangeCredentialStore): DPAPI, current-user scope, a file under
' %LOCALAPPDATA%\SZA\FastMediaSorter\exchange\, replaced atomically, never in the registry, never in a log, never in a
' tracked file. Access tokens are never stored at all - they live in memory for under an hour.
'
' The file is bound to the OAuth client it was issued to: a token copied beside another client id is no token (Google
' refuses it anyway; this refuses first, with no network call). Nothing here writes or creates anything on construction or
' on a read: a PC that never signed in has no file (the dormant rule of 204 section 2).

''' <summary>DPAPI (current user) with an entropy of the Drive channel's own, so a blob of the exchange device token cannot
''' be fed to this store or the other way round.</summary>
Friend NotInheritable Class ExchangeDriveDpapiCipher
    Implements IExchangeSecretCipher

    Public Shared ReadOnly Instance As New ExchangeDriveDpapiCipher()

    Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("FastMediaSorter.Exchange.DriveRefreshToken.v1")

    Private Sub New()
    End Sub

    Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
        Return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)
    End Function

    Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
        Return ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser)
    End Function
End Class

''' <summary>The refresh token on disk, protected, bound to a client id.</summary>
Friend NotInheritable Class ExchangeDriveTokenStore

    Private Const FileName As String = "drive-token.dat"
    Private Const PayloadMagic As String = "FMSXGDT1"
    Private Const MaxTokenLength As Integer = 2048

    Private ReadOnly _directory As String
    Private ReadOnly _cipher As IExchangeSecretCipher

    Public Sub New(directory As String, cipher As IExchangeSecretCipher)
        If String.IsNullOrWhiteSpace(directory) Then Throw New ArgumentException("directory", NameOf(directory))
        _directory = directory
        _cipher = If(cipher, ExchangeDriveDpapiCipher.Instance)
    End Sub

    ''' <summary>The same folder as the exchange device token; nothing is created by asking for the path.</summary>
    Public Shared Function CreateDefault() As ExchangeDriveTokenStore
        Return New ExchangeDriveTokenStore(ExchangeCredentialStore.DefaultDirectory(), ExchangeDriveDpapiCipher.Instance)
    End Function

    Public ReadOnly Property FilePath As String
        Get
            Return Path.Combine(_directory, FileName)
        End Get
    End Property

    ''' <summary>True when a token file exists. A pure read: it creates nothing.</summary>
    Public Function Exists() As Boolean
        Try
            Return File.Exists(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    ''' <summary>A form a refresh token can take: non-empty, bounded, one line of printable ASCII.</summary>
    Public Shared Function IsPlausibleToken(token As String) As Boolean
        If String.IsNullOrEmpty(token) OrElse token.Length > MaxTokenLength Then Return False
        For Each c As Char In token
            If c < "!"c OrElse c > "~"c Then Return False
        Next
        Return True
    End Function

    ''' <summary>The short, non-reversible tag of a client id that the file is bound to (first 16 hex of its SHA-256).</summary>
    Public Shared Function ClientTag(clientId As String) As String
        Using sha As SHA256 = SHA256.Create()
            Dim hash As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(If(clientId, "")))
            Dim builder As New StringBuilder(16)
            For index As Integer = 0 To 7
                builder.Append(hash(index).ToString("x2", Globalization.CultureInfo.InvariantCulture))
            Next
            Return builder.ToString()
        End Using
    End Function

    ''' <summary>Protects and stores the refresh token for the client id, replacing any earlier one atomically (a temporary
    ''' file in the same folder, then a move). False when it could not be protected or written - never a half-written token.</summary>
    Public Function Save(clientId As String, refreshToken As String) As Boolean
        If String.IsNullOrEmpty(clientId) OrElse Not IsPlausibleToken(refreshToken) Then Return False
        Dim plain As Byte() = Encoding.UTF8.GetBytes(PayloadMagic & vbLf & ClientTag(clientId) & vbLf & refreshToken)
        Try
            Dim blob As Byte() = _cipher.Protect(plain)
            If blob Is Nothing OrElse blob.Length = 0 Then Return False
            Directory.CreateDirectory(_directory)
            Dim temp As String = Path.Combine(_directory, FileName & "." & Guid.NewGuid().ToString("N") & ".tmp")
            Try
                File.WriteAllBytes(temp, blob)
                File.Move(temp, FilePath, True)
            Finally
                Try
                    If File.Exists(temp) Then File.Delete(temp)
                Catch
                End Try
            End Try
            Return True
        Catch ex As Exception When TypeOf ex Is CryptographicException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        Finally
            Array.Clear(plain, 0, plain.Length)
        End Try
    End Function

    ''' <summary>The refresh token stored for exactly this client id, or False (no file, another user's or another machine's
    ''' blob, a token for another client, a damaged file). A pure read.</summary>
    Public Function TryLoad(clientId As String, ByRef refreshToken As String) As Boolean
        refreshToken = ""
        Dim blob As Byte()
        Try
            If Not File.Exists(FilePath) Then Return False
            blob = File.ReadAllBytes(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
        Dim plain As Byte() = Nothing
        Try
            plain = _cipher.Unprotect(blob)
            Dim lines As String() = Encoding.UTF8.GetString(plain).Split(ControlChars.Lf)
            If lines.Length <> 3 OrElse Not String.Equals(lines(0), PayloadMagic, StringComparison.Ordinal) Then Return False
            If Not String.Equals(lines(1), ClientTag(clientId), StringComparison.Ordinal) Then Return False
            If Not IsPlausibleToken(lines(2)) Then Return False
            refreshToken = lines(2)
            Return True
        Catch ex As CryptographicException
            Return False
        Finally
            If plain IsNot Nothing Then Array.Clear(plain, 0, plain.Length)
        End Try
    End Function

    ''' <summary>Removes the token file (sign-out). Nothing else is touched.</summary>
    Public Sub Delete()
        Try
            If File.Exists(FilePath) Then File.Delete(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

End Class
#End If
