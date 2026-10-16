#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text
Imports System.Text.Json

' Spec 234 T-1, DEVICE-EXCHANGE section 27 item DG. Trust on first use: per account entry, the key a device showed the
' first time this program saw its deviceId. The entry is named by the scope - the id of THIS device in that entry, which is
' unique per entry (item G) - so one store can serve several entries and an entry that is removed takes its keys with it.
'
' What is kept is public (a `kem` is a public key), so the file is plain JSON and is not protected by DPAPI; what it holds
' is a decision, not a secret. A key lives exactly as long as its deviceId (item DF), so a kept key is never replaced:
' Keep answers True only for the very key that is kept. It is dropped by Forget (the person revoked the device, item DG) and
' by ForgetScope (the entry is removed or the account is left), and never because the server removed the record: a server
' that removes a device and lists it again with another key is the very case this store exists to catch.

''' <summary>The per-entry store of first-seen device keys. Behind an interface so tests use the in-memory one.</summary>
Friend Interface IExchangeSealedTrustStore
    ''' <summary>The key kept for the device in the scope, or "" when none is kept.</summary>
    Function TryGet(scope As String, deviceId As String) As String
    ''' <summary>Keeps the key of a device seen for the first time. True when this very key is kept now (it was stored, or was
    ''' already); False when another key is kept for the id (it is NOT replaced) or the key could not be written.</summary>
    Function Keep(scope As String, deviceId As String, kem As String) As Boolean
    ''' <summary>Drops the kept key of one device (the person revoked it).</summary>
    Sub Forget(scope As String, deviceId As String)
    ''' <summary>Drops every key kept for the scope (the entry is removed, the account is left).</summary>
    Sub ForgetScope(scope As String)
End Interface

''' <summary>The slot names of the store. A device is kept under its deviceId; the sender of a granted record (item DJ) is kept under
''' <c>grant:&lt;grantId&gt;</c> - a separate key space in the same scope, so a grant never collides with a device id (a deviceId
''' cannot contain a colon) and the first box of a grant pins the key of everything that follows it.</summary>
Friend NotInheritable Class ExchangeSealedTrustSlots

    Private Const GrantPrefix As String = "grant:"

    Private Sub New()
    End Sub

    Public Shared Function ForGrant(grantId As String) As String
        Return GrantPrefix & grantId
    End Function

    ''' <summary>True for a device id or a grant slot built from a valid grant id.</summary>
    Friend Shared Function IsValidSlot(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        If name.StartsWith(GrantPrefix, StringComparison.Ordinal) Then Return ExchangeIds.IsValidId(name.Substring(GrantPrefix.Length))
        Return ExchangeIds.IsValidId(name)
    End Function

End Class

''' <summary>A store that keeps nothing beyond the process: tests, and a session given no store.</summary>
Friend NotInheritable Class ExchangeMemorySealedTrustStore
    Implements IExchangeSealedTrustStore

    Private ReadOnly _gate As New Object()
    Private ReadOnly _scopes As New Dictionary(Of String, Dictionary(Of String, String))(StringComparer.Ordinal)

    Public Function TryGet(scope As String, deviceId As String) As String Implements IExchangeSealedTrustStore.TryGet
        If String.IsNullOrEmpty(scope) OrElse String.IsNullOrEmpty(deviceId) Then Return ""
        SyncLock _gate
            Dim keys As Dictionary(Of String, String) = Nothing
            Dim kem As String = Nothing
            If _scopes.TryGetValue(scope, keys) AndAlso keys.TryGetValue(deviceId, kem) Then Return kem
            Return ""
        End SyncLock
    End Function

    Public Function Keep(scope As String, deviceId As String, kem As String) As Boolean Implements IExchangeSealedTrustStore.Keep
        If String.IsNullOrEmpty(scope) OrElse String.IsNullOrEmpty(deviceId) OrElse String.IsNullOrEmpty(kem) Then Return False
        SyncLock _gate
            Dim keys As Dictionary(Of String, String) = Nothing
            If Not _scopes.TryGetValue(scope, keys) Then
                keys = New Dictionary(Of String, String)(StringComparer.Ordinal)
                _scopes(scope) = keys
            End If
            Dim kept As String = Nothing
            If keys.TryGetValue(deviceId, kept) Then Return String.Equals(kept, kem, StringComparison.Ordinal)
            keys(deviceId) = kem
            Return True
        End SyncLock
    End Function

    Public Sub Forget(scope As String, deviceId As String) Implements IExchangeSealedTrustStore.Forget
        If String.IsNullOrEmpty(scope) OrElse String.IsNullOrEmpty(deviceId) Then Return
        SyncLock _gate
            Dim keys As Dictionary(Of String, String) = Nothing
            If _scopes.TryGetValue(scope, keys) Then keys.Remove(deviceId)
        End SyncLock
    End Sub

    Public Sub ForgetScope(scope As String) Implements IExchangeSealedTrustStore.ForgetScope
        If String.IsNullOrEmpty(scope) Then Return
        SyncLock _gate
            _scopes.Remove(scope)
        End SyncLock
    End Sub

End Class

''' <summary>The kept keys of one folder as one JSON file, `sealed-trust.json`, beside the device token of the entry. The file
''' names the scope it was written for; a file written for another scope is not read (an entry whose device id changed starts
''' empty) and is replaced by the first key kept. A pure read creates nothing: a machine with no account has no file.</summary>
Friend NotInheritable Class ExchangeSealedTrustStore
    Implements IExchangeSealedTrustStore

    Friend Const FileName As String = "sealed-trust.json"
    Private Const FormatVersion As Integer = 1
    Private Const MaxFileBytes As Integer = 1048576
    ''' <summary>A bound on the file, not on the product: an entry holds a few dozen devices.</summary>
    Private Const MaxKeys As Integer = 2048

    Private ReadOnly _directory As String
    Private ReadOnly _gate As New Object()
    ' The parsed file: loaded once, then changed only by this object (one process owns an entry's folder).
    Private _loaded As Boolean
    Private _scope As String = ""
    Private ReadOnly _keys As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Public Sub New(directory As String)
        If String.IsNullOrWhiteSpace(directory) Then Throw New ArgumentException("directory", NameOf(directory))
        _directory = directory
    End Sub

    Public ReadOnly Property FilePath As String
        Get
            Return Path.Combine(_directory, FileName)
        End Get
    End Property

    Public Function TryGet(scope As String, deviceId As String) As String Implements IExchangeSealedTrustStore.TryGet
        If Not ExchangeIds.IsValidId(scope) OrElse String.IsNullOrEmpty(deviceId) Then Return ""
        SyncLock _gate
            EnsureLoaded()
            Dim kem As String = Nothing
            If String.Equals(_scope, scope, StringComparison.Ordinal) AndAlso _keys.TryGetValue(deviceId, kem) Then Return kem
            Return ""
        End SyncLock
    End Function

    Public Function Keep(scope As String, deviceId As String, kem As String) As Boolean Implements IExchangeSealedTrustStore.Keep
        If Not ExchangeIds.IsValidId(scope) OrElse Not ExchangeSealedTrustSlots.IsValidSlot(deviceId) Then Return False
        Dim point As Byte() = Nothing
        If Not ExchangeDeviceKey.TryParseKem(kem, point) Then Return False
        SyncLock _gate
            EnsureLoaded()
            If Not String.Equals(_scope, scope, StringComparison.Ordinal) Then
                _scope = scope
                _keys.Clear()
            End If
            Dim kept As String = Nothing
            If _keys.TryGetValue(deviceId, kept) Then Return String.Equals(kept, kem, StringComparison.Ordinal)
            If _keys.Count >= MaxKeys Then Return False
            _keys(deviceId) = kem
            If Write() Then Return True
            _keys.Remove(deviceId)
            Return False
        End SyncLock
    End Function

    Public Sub Forget(scope As String, deviceId As String) Implements IExchangeSealedTrustStore.Forget
        If Not ExchangeIds.IsValidId(scope) OrElse String.IsNullOrEmpty(deviceId) Then Return
        SyncLock _gate
            EnsureLoaded()
            If Not String.Equals(_scope, scope, StringComparison.Ordinal) OrElse Not _keys.ContainsKey(deviceId) Then Return
            Dim kept As String = _keys(deviceId)
            _keys.Remove(deviceId)
            If Not Write() Then _keys(deviceId) = kept
        End SyncLock
    End Sub

    Public Sub ForgetScope(scope As String) Implements IExchangeSealedTrustStore.ForgetScope
        If Not ExchangeIds.IsValidId(scope) Then Return
        SyncLock _gate
            EnsureLoaded()
            If Not String.Equals(_scope, scope, StringComparison.Ordinal) Then Return
            _keys.Clear()
            _scope = ""
            Try
                If File.Exists(FilePath) Then File.Delete(FilePath)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End SyncLock
    End Sub

    ' --- the file ---

    Private Sub EnsureLoaded()
        If _loaded Then Return
        _loaded = True
        _scope = ""
        _keys.Clear()
        Dim bytes As Byte()
        Try
            If Not File.Exists(FilePath) Then Return
            Dim info As New FileInfo(FilePath)
            If info.Length > MaxFileBytes Then Return
            bytes = File.ReadAllBytes(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return
        End Try
        Try
            Using doc As JsonDocument = JsonDocument.Parse(bytes)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return
                Dim el As JsonElement = Nothing
                Dim version As Integer = 0
                If Not root.TryGetProperty("v", el) OrElse el.ValueKind <> JsonValueKind.Number OrElse Not el.TryGetInt32(version) OrElse version <> FormatVersion Then Return
                If Not root.TryGetProperty("scope", el) OrElse el.ValueKind <> JsonValueKind.String Then Return
                Dim scope As String = el.GetString()
                If Not ExchangeIds.IsValidId(scope) Then Return
                If Not root.TryGetProperty("keys", el) OrElse el.ValueKind <> JsonValueKind.Object Then Return
                Dim read As New Dictionary(Of String, String)(StringComparer.Ordinal)
                For Each p As JsonProperty In el.EnumerateObject()
                    If read.Count >= MaxKeys Then Exit For
                    If p.Value.ValueKind <> JsonValueKind.String Then Continue For
                    Dim kem As String = p.Value.GetString()
                    Dim point As Byte() = Nothing
                    ' an unreadable entry is dropped; the device is then seen for the first time again
                    If ExchangeSealedTrustSlots.IsValidSlot(p.Name) AndAlso ExchangeDeviceKey.TryParseKem(kem, point) Then read(p.Name) = kem
                Next
                _scope = scope
                For Each pair As KeyValuePair(Of String, String) In read
                    _keys(pair.Key) = pair.Value
                Next
            End Using
        Catch ex As JsonException
            ' a damaged file is the same as none; the first key kept writes a whole one
        End Try
    End Sub

    Private Function Write() As Boolean
        Dim ms As New MemoryStream()
        Using w As New Utf8JsonWriter(ms)
            w.WriteStartObject()
            w.WriteNumber("v", FormatVersion)
            w.WriteString("scope", _scope)
            w.WriteStartObject("keys")
            For Each pair As KeyValuePair(Of String, String) In _keys
                w.WriteString(pair.Key, pair.Value)
            Next
            w.WriteEndObject()
            w.WriteEndObject()
        End Using
        Try
            Directory.CreateDirectory(_directory)
            Dim temp As String = Path.Combine(_directory, FileName & "." & Guid.NewGuid().ToString("N") & ".tmp")
            Try
                File.WriteAllBytes(temp, ms.ToArray())
                File.Move(temp, FilePath, True)
            Finally
                Try
                    If File.Exists(temp) Then File.Delete(temp)
                Catch
                End Try
            End Try
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    Public Overrides Function ToString() As String
        Return "ExchangeSealedTrustStore"
    End Function

End Class
#End If
