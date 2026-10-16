#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

' Spec 234 T-1, DEVICE-EXCHANGE section 27 item DF. Where the private key of a device lives: one key per deviceId, under
' DPAPI (current-user scope) in a file beside the device token (ExchangeCredentialStore), never in the registry, a
' backup, a settings export, a .fmscfg file, a hand-over or a log.
'
' A key lives exactly as long as its deviceId (item DF): Save never replaces a key by another one under the same id, a
' damaged file is not overwritten silently, and the only way a key goes away is Delete (revoke, or "forget the account").
' A device that lost its key enrolls anew with a new deviceId. Nothing here logs, and nothing offers the scalar to a
' caller: TryLoad returns a key OBJECT whose export is Friend and meant for this store and for tests.
'
' Like the token store, a pure read creates nothing: a machine with no account has no exchange folder and no key file.

''' <summary>The per-deviceId store of device keys. Behind an interface so tests use the in-memory one.</summary>
Friend Interface IExchangeDeviceKeyStore
    ''' <summary>The key of this device id as a NEW object (the caller disposes it), or Nothing - none, a damaged file, another
    ''' user's or another machine's blob, a file kept for another id.</summary>
    Function TryLoad(deviceId As String) As ExchangeDeviceKey
    ''' <summary>Keeps the key of the id. True when it is stored now or the very same key already was; False when another key
    ''' is kept for the id (a key never changes under its id), or it could not be protected or written.</summary>
    Function Save(deviceId As String, key As ExchangeDeviceKey) As Boolean
    ''' <summary>True when something is kept for the id, readable or not.</summary>
    Function Exists(deviceId As String) As Boolean
    Sub Delete(deviceId As String)
End Interface

''' <summary>A store that keeps scalars in memory only: tests, and a device that must not persist.</summary>
Friend NotInheritable Class ExchangeMemoryDeviceKeyStore
    Implements IExchangeDeviceKeyStore

    Private ReadOnly _scalars As New Dictionary(Of String, Byte())(StringComparer.Ordinal)

    Public Function TryLoad(deviceId As String) As ExchangeDeviceKey Implements IExchangeDeviceKeyStore.TryLoad
        If String.IsNullOrEmpty(deviceId) Then Return Nothing
        Dim scalar As Byte() = Nothing
        If Not _scalars.TryGetValue(deviceId, scalar) Then Return Nothing
        Return ExchangeDeviceKey.FromPrivateScalar(scalar)
    End Function

    Public Function Save(deviceId As String, key As ExchangeDeviceKey) As Boolean Implements IExchangeDeviceKeyStore.Save
        If String.IsNullOrEmpty(deviceId) OrElse key Is Nothing Then Return False
        If _scalars.ContainsKey(deviceId) Then
            Using existing As ExchangeDeviceKey = TryLoad(deviceId)
                Return existing IsNot Nothing AndAlso String.Equals(existing.Kem, key.Kem, StringComparison.Ordinal)
            End Using
        End If
        Dim scalar As Byte() = key.ExportPrivateScalar()
        If scalar Is Nothing Then Return False
        _scalars(deviceId) = scalar
        Return True
    End Function

    Public Function Exists(deviceId As String) As Boolean Implements IExchangeDeviceKeyStore.Exists
        Return Not String.IsNullOrEmpty(deviceId) AndAlso _scalars.ContainsKey(deviceId)
    End Function

    Public Sub Delete(deviceId As String) Implements IExchangeDeviceKeyStore.Delete
        If String.IsNullOrEmpty(deviceId) Then Return
        Dim scalar As Byte() = Nothing
        If _scalars.TryGetValue(deviceId, scalar) Then
            Array.Clear(scalar, 0, scalar.Length)
            _scalars.Remove(deviceId)
        End If
    End Sub

End Class

''' <summary>DPAPI, user scope, with an entropy of its own - a token blob is no key blob.</summary>
Friend NotInheritable Class ExchangeDeviceKeyCipher
    Implements IExchangeSecretCipher

    Public Shared ReadOnly Instance As New ExchangeDeviceKeyCipher()

    Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("FastMediaSorter.Exchange.DeviceKey.v1")

    Private Sub New()
    End Sub

    Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
        Return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)
    End Function

    Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
        Return ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser)
    End Function
End Class

''' <summary>The device keys on disk, one file per deviceId, bound to the id so a file copied beside another id is no key.</summary>
Friend NotInheritable Class ExchangeDeviceKeyStore
    Implements IExchangeDeviceKeyStore

    Private Const FilePrefix As String = "device-key-"
    Private Const FileSuffix As String = ".dat"
    Private Const PayloadMagic As String = "FMSXKEY1"

    Private ReadOnly _directory As String
    Private ReadOnly _cipher As IExchangeSecretCipher

    Public Sub New(directory As String, cipher As IExchangeSecretCipher)
        If String.IsNullOrWhiteSpace(directory) Then Throw New ArgumentException("directory", NameOf(directory))
        _directory = directory
        _cipher = If(cipher, ExchangeDeviceKeyCipher.Instance)
    End Sub

    ''' <summary>The folder of the device token (%LOCALAPPDATA%\SZA\FastMediaSorter\exchange); the key files sit beside it.</summary>
    Public Shared Function CreateDefault() As ExchangeDeviceKeyStore
        Return New ExchangeDeviceKeyStore(ExchangeCredentialStore.DefaultDirectory(), ExchangeDeviceKeyCipher.Instance)
    End Function

    ''' <summary>The file of an id, or "" for an id that is not a wire id (a path is never built from anything else).</summary>
    Public Function FilePathFor(deviceId As String) As String
        If Not ExchangeIds.IsValidId(deviceId) Then Return ""
        Return Path.Combine(_directory, FilePrefix & deviceId & FileSuffix)
    End Function

    Public Function Exists(deviceId As String) As Boolean Implements IExchangeDeviceKeyStore.Exists
        Dim path As String = FilePathFor(deviceId)
        If path.Length = 0 Then Return False
        Try
            Return File.Exists(path)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    Public Function Save(deviceId As String, key As ExchangeDeviceKey) As Boolean Implements IExchangeDeviceKeyStore.Save
        Dim target As String = FilePathFor(deviceId)
        If target.Length = 0 OrElse key Is Nothing Then Return False
        If Exists(deviceId) Then
            Using existing As ExchangeDeviceKey = TryLoad(deviceId)
                Return existing IsNot Nothing AndAlso String.Equals(existing.Kem, key.Kem, StringComparison.Ordinal)
            End Using
        End If
        Dim scalar As Byte() = key.ExportPrivateScalar()
        If scalar Is Nothing Then Return False
        Dim plain As Byte() = Encoding.UTF8.GetBytes(PayloadMagic & vbLf & deviceId & vbLf & ExchangeBase64Url.Encode(scalar))
        Try
            Dim blob As Byte() = _cipher.Protect(plain)
            If blob Is Nothing OrElse blob.Length = 0 Then Return False
            Directory.CreateDirectory(_directory)
            Dim temp As String = Path.Combine(_directory, FilePrefix & Guid.NewGuid().ToString("N") & ".tmp")
            Try
                File.WriteAllBytes(temp, blob)
                File.Move(temp, target, False)
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
            Array.Clear(scalar, 0, scalar.Length)
        End Try
    End Function

    Public Function TryLoad(deviceId As String) As ExchangeDeviceKey Implements IExchangeDeviceKeyStore.TryLoad
        Dim path As String = FilePathFor(deviceId)
        If path.Length = 0 Then Return Nothing
        Dim blob As Byte()
        Try
            If Not File.Exists(path) Then Return Nothing
            blob = File.ReadAllBytes(path)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return Nothing
        End Try
        Dim plain As Byte() = Nothing
        Dim scalar As Byte() = Nothing
        Try
            plain = _cipher.Unprotect(blob)
            Dim lines As String() = Encoding.UTF8.GetString(plain).Split(ControlChars.Lf)
            If lines.Length <> 3 OrElse Not String.Equals(lines(0), PayloadMagic, StringComparison.Ordinal) Then Return Nothing
            If Not String.Equals(lines(1), deviceId, StringComparison.Ordinal) Then Return Nothing
            If Not ExchangeBase64Url.TryDecode(lines(2), scalar) Then Return Nothing
            Return ExchangeDeviceKey.FromPrivateScalar(scalar)
        Catch ex As CryptographicException
            Return Nothing
        Finally
            If plain IsNot Nothing Then Array.Clear(plain, 0, plain.Length)
            If scalar IsNot Nothing Then Array.Clear(scalar, 0, scalar.Length)
        End Try
    End Function

    Public Sub Delete(deviceId As String) Implements IExchangeDeviceKeyStore.Delete
        Dim path As String = FilePathFor(deviceId)
        If path.Length = 0 Then Return
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

End Class

''' <summary>The one decision around a store: the key of an id, made once.</summary>
Friend NotInheritable Class ExchangeDeviceKeys

    Private Sub New()
    End Sub

    ''' <summary>The stored key of the id, or a new one kept first. Nothing when none could be kept (a key that is not stored
    ''' is never used: it would be lost at the next start and the device would have to enroll anew) and when the id already
    ''' has a file this store cannot read - that key is not replaced silently (item DF). The caller disposes the result.</summary>
    Public Shared Function LoadOrCreate(store As IExchangeDeviceKeyStore, deviceId As String) As ExchangeDeviceKey
        If store Is Nothing Then Throw New ArgumentNullException(NameOf(store))
        Dim existing As ExchangeDeviceKey = store.TryLoad(deviceId)
        If existing IsNot Nothing Then Return existing
        If store.Exists(deviceId) Then Return Nothing
        Dim fresh As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
        If store.Save(deviceId, fresh) Then Return fresh
        fresh.Dispose()
        Return Nothing
    End Function

End Class
#End If
