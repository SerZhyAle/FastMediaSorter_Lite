#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

' Ticket 180 T-1 and section 3 of the ticket. Where the secret of an enrolled device lives.
'
'   - The DEVICE TOKEN is stored under DPAPI (current-user scope) in a file under
'     %LOCALAPPDATA%\SZA\FastMediaSorter\exchange\ and NEVER in the registry. The password is never stored at all:
'     it exists in the enrollment dialog's field and in the `enroll` frame, and that frame's buffer is overwritten
'     once sent (ExchangeFrameWriter).
'   - The server's host:port, the login, the pin and the device id are NOT secrets and live in the registry
'     (ExchangeAccountStore), beside the product's other settings, under values of their own.
'
' Nothing in this file writes or creates anything on construction or on a read: a machine with no account has no
' exchange folder, no token file and no registry value (the dormant rule of 204 section 2).

''' <summary>Protects and unprotects bytes for the current user. Injectable so the failure paths are testable.</summary>
Friend Interface IExchangeSecretCipher
    Function Protect(plain As Byte()) As Byte()
    Function Unprotect(blob As Byte()) As Byte()
End Interface

''' <summary>Windows DPAPI, user scope, with an entropy of this product's own.</summary>
Friend NotInheritable Class ExchangeDpapiCipher
    Implements IExchangeSecretCipher

    Public Shared ReadOnly Instance As New ExchangeDpapiCipher()

    Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("FastMediaSorter.Exchange.DeviceToken.v1")

    Private Sub New()
    End Sub

    Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
        Return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)
    End Function

    Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
        Return ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser)
    End Function
End Class

''' <summary>The device token on disk, bound to its device id so a token copied beside another id is no token.</summary>
Friend NotInheritable Class ExchangeCredentialStore

    Private Const FileName As String = "device-token.dat"
    Private Const PayloadMagic As String = "FMSXTOK1"

    Private ReadOnly _directory As String
    Private ReadOnly _cipher As IExchangeSecretCipher

    Public Sub New(directory As String, cipher As IExchangeSecretCipher)
        If String.IsNullOrWhiteSpace(directory) Then Throw New ArgumentException("directory", NameOf(directory))
        _directory = directory
        _cipher = If(cipher, ExchangeDpapiCipher.Instance)
    End Sub

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\exchange. Nothing is created by asking for the path.</summary>
    Public Shared Function DefaultDirectory() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SZA", "FastMediaSorter", "exchange")
    End Function

    Public Shared Function CreateDefault() As ExchangeCredentialStore
        Return New ExchangeCredentialStore(DefaultDirectory(), ExchangeDpapiCipher.Instance)
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

    ''' <summary>Protects and stores the token for the device id, replacing any earlier one atomically (a temporary file in
    ''' the same folder, then a move). False when it could not be protected or written - never a half-written token.</summary>
    Public Function Save(deviceId As String, token As String) As Boolean
        If Not ExchangeIds.IsValidId(deviceId) OrElse Not ExchangeIds.IsValidToken(token) Then Return False
        Dim plain As Byte() = Encoding.UTF8.GetBytes(PayloadMagic & vbLf & deviceId & vbLf & token)
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

    ''' <summary>The token stored for exactly this device id, or False (no file, another user's or another machine's blob, a
    ''' token for another id, a damaged file). A pure read.</summary>
    Public Function TryLoad(deviceId As String, ByRef token As String) As Boolean
        token = ""
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
            If Not String.Equals(lines(1), deviceId, StringComparison.Ordinal) Then Return False
            If Not ExchangeIds.IsValidToken(lines(2)) Then Return False
            token = lines(2)
            Return True
        Catch ex As CryptographicException
            Return False
        Finally
            If plain IsNot Nothing Then Array.Clear(plain, 0, plain.Length)
        End Try
    End Function

    ''' <summary>Removes the token file ("forget"). Nothing else is touched.</summary>
    Public Sub Delete()
        Try
            If File.Exists(FilePath) Then File.Delete(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

End Class
#End If
