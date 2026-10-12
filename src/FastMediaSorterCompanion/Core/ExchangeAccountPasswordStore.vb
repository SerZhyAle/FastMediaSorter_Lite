Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' The Share Manager's copy of the exchange ACCOUNT passwords, kept ONLY when the person turned keeping on
' (ExchangeSettings.KeepAccountPasswords, off by default). The server keeps an Argon2id record and nothing else, so
' this file is the only place a password can be read again.
'
' Where it lives: ONE file under %LOCALAPPDATA%\SZA\FastMediaSorter\exchange\, protected by DPAPI (user scope, an entropy of
' its own) - never in the registry, never in a log, never on a command line. Anything that can run under the same
' Windows account can decrypt it; that is the trade-off the person accepts when they turn keeping on (the warning says so).
'
' Nothing is created on construction or on a read: a machine that never kept a password has no file (the dormant rule of 204).
' Keeping off deletes the file (ExchangeAccountPasswordStore.Clear), so nothing stays on disk behind the person's back.

''' <summary>Windows DPAPI for the account passwords: user scope, an entropy of this product's own.</summary>
Friend NotInheritable Class ExchangeAccountPasswordDpapiCipher
    Implements IExchangeSecretCipher

    Public Shared ReadOnly Instance As New ExchangeAccountPasswordDpapiCipher()

    Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("FastMediaSorter.Exchange.AccountPasswords.v1")

    Private Sub New()
    End Sub

    Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
        Return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)
    End Function

    Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
        Return ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser)
    End Function
End Class

''' <summary>The account passwords on disk, one file for every login of this PC's server. A pure read where nothing is written.</summary>
Friend NotInheritable Class ExchangeAccountPasswordStore

    Private Const FileName As String = "account-passwords.dat"
    Private Const Magic As String = "FMSXPWD1"

    Private ReadOnly _directory As String
    Private ReadOnly _cipher As IExchangeSecretCipher

    Public Sub New(directory As String, cipher As IExchangeSecretCipher)
        If String.IsNullOrWhiteSpace(directory) Then Throw New ArgumentException("directory", NameOf(directory))
        _directory = directory
        _cipher = If(cipher, ExchangeAccountPasswordDpapiCipher.Instance)
    End Sub

    Public Shared Function CreateDefault() As ExchangeAccountPasswordStore
        Return New ExchangeAccountPasswordStore(ExchangeCredentialStore.DefaultDirectory(), ExchangeAccountPasswordDpapiCipher.Instance)
    End Function

    Public ReadOnly Property FilePath As String
        Get
            Return Path.Combine(_directory, FileName)
        End Get
    End Property

    ''' <summary>The password kept for this login. False (and an empty password) when none is kept, the file is missing or
    ''' damaged, or it was written under another Windows account. A pure read.</summary>
    Public Function TryLoad(login As String, ByRef password As String) As Boolean
        password = ""
        If String.IsNullOrEmpty(login) Then Return False
        Dim all As Dictionary(Of String, String) = ReadAll()
        Dim found As String = Nothing
        If Not all.TryGetValue(login, found) Then Return False
        password = found
        Return True
    End Function

    ''' <summary>Keeps the password for this login, replacing an earlier one. False when it could not be protected or written.</summary>
    Public Function Save(login As String, password As String) As Boolean
        If String.IsNullOrEmpty(login) OrElse String.IsNullOrEmpty(password) Then Return False
        Dim all As Dictionary(Of String, String) = ReadAll()
        all(login) = password
        Return WriteAll(all)
    End Function

    ''' <summary>Forgets the password of one login (an account that was removed). Nothing is created when nothing was kept.</summary>
    Public Function Remove(login As String) As Boolean
        If String.IsNullOrEmpty(login) OrElse Not File.Exists(FilePath) Then Return True
        Dim all As Dictionary(Of String, String) = ReadAll()
        If Not all.Remove(login) Then Return True
        If all.Count = 0 Then Return DeleteFile()
        Return WriteAll(all)
    End Function

    ''' <summary>Removes the whole file ("keeping is off"). Nothing else is touched.</summary>
    Public Sub Clear()
        DeleteFile()
    End Sub

    Private Function DeleteFile() As Boolean
        Try
            If File.Exists(FilePath) Then File.Delete(FilePath)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    ''' <summary>Every password kept. A missing, foreign or damaged file is an empty set, never an exception.</summary>
    Private Function ReadAll() As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim blob As Byte()
        Try
            If Not File.Exists(FilePath) Then Return result
            blob = File.ReadAllBytes(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return result
        End Try
        Dim plain As Byte() = Nothing
        Try
            plain = _cipher.Unprotect(blob)
            Dim text As String = Encoding.UTF8.GetString(plain)
            Dim lineEnd As Integer = text.IndexOf(ControlChars.Lf)
            If lineEnd < 0 OrElse Not String.Equals(text.Substring(0, lineEnd), Magic, StringComparison.Ordinal) Then Return result
            Using doc As JsonDocument = JsonDocument.Parse(text.Substring(lineEnd + 1))
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return result
                For Each p As JsonProperty In root.EnumerateObject()
                    If p.Value.ValueKind = JsonValueKind.String Then result(p.Name) = p.Value.GetString()
                Next
            End Using
        Catch ex As Exception When TypeOf ex Is CryptographicException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is ArgumentException
            result.Clear()
        Finally
            If plain IsNot Nothing Then Array.Clear(plain, 0, plain.Length)
        End Try
        Return result
    End Function

    ''' <summary>Protects the whole set and replaces the file atomically (a temporary file in the same folder, then a move).</summary>
    Private Function WriteAll(all As Dictionary(Of String, String)) As Boolean
        Dim root As New JsonObject()
        For Each pair As KeyValuePair(Of String, String) In all
            root(pair.Key) = pair.Value
        Next
        Dim plain As Byte() = Encoding.UTF8.GetBytes(Magic & vbLf & root.ToJsonString())
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

End Class
