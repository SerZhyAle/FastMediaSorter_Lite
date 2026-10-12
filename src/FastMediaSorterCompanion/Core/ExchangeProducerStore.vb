Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' Ticket 170 rule 2. The ids of the published shares: a resource id per share, kept so that an attachment on another device
' survives a restart, a stopped share and a restart of the exchange server; an optional outsider share id per share (rule 8);
' and the third-party port the server granted (rule 7), remembered so it is asked for again after a reconnect.
'
' Where they live: ONE file under %LOCALAPPDATA%\SZA\FastMediaSorter\exchange\, protected by DPAPI (user scope, an entropy of its own)
' - never in the worker's shares.json (WORKER-IPC rule 8) and never in the registry. A share id is a bearer capability, which is why
' the whole file is a secret, not just the ids in it. The key of an entry is the share's NAME (the worker's own key): a rename is a new
' resource, and the rename confirmation says its old links stop working (Q170-1).
'
' Nothing is created on construction or on a read: a machine that never published has no file (the dormant rule of 204).

''' <summary>One published share's ids.</summary>
Friend NotInheritable Class ExchangeProducerEntry

    Public ReadOnly ResourceId As String
    ''' <summary>The outsider capability, or "" when no link exists.</summary>
    Public ReadOnly ShareId As String

    Public Sub New(resourceId As String, shareId As String)
        Me.ResourceId = resourceId
        Me.ShareId = If(shareId, "")
    End Sub

    Public Function WithShareId(newShareId As String) As ExchangeProducerEntry
        Return New ExchangeProducerEntry(ResourceId, newShareId)
    End Function

    Public Overrides Function ToString() As String
        Return "ExchangeProducerEntry"
    End Function

End Class

''' <summary>The whole of what is kept. Immutable: every change returns a new value, and the store writes whole values.</summary>
Friend NotInheritable Class ExchangeProducerState

    Private ReadOnly _entries As IReadOnlyDictionary(Of String, ExchangeProducerEntry)

    ''' <summary>The share the third-party port is bound to ("" when none) and the port it was granted (0 when none).</summary>
    Public ReadOnly CarrierName As String
    Public ReadOnly CarrierPort As Integer

    Public Shared ReadOnly Empty As New ExchangeProducerState(New Dictionary(Of String, ExchangeProducerEntry)(StringComparer.Ordinal), "", 0)

    Public Sub New(entries As IReadOnlyDictionary(Of String, ExchangeProducerEntry), carrierName As String, carrierPort As Integer)
        _entries = entries
        Me.CarrierName = If(carrierName, "")
        Me.CarrierPort = carrierPort
    End Sub

    Public ReadOnly Property Entries As IReadOnlyDictionary(Of String, ExchangeProducerEntry)
        Get
            Return _entries
        End Get
    End Property

    Public Function [Get](name As String) As ExchangeProducerEntry
        Dim e As ExchangeProducerEntry = Nothing
        If name IsNot Nothing AndAlso _entries.TryGetValue(name, e) Then Return e
        Return Nothing
    End Function

    Private Function Copy() As Dictionary(Of String, ExchangeProducerEntry)
        Return New Dictionary(Of String, ExchangeProducerEntry)(_entries, StringComparer.Ordinal)
    End Function

    ''' <summary>The entry of a share, created with a fresh resource id the first time it is asked for (kept from then on).</summary>
    Public Function WithEntry(name As String, entry As ExchangeProducerEntry) As ExchangeProducerState
        Dim d As Dictionary(Of String, ExchangeProducerEntry) = Copy()
        d(name) = entry
        Return New ExchangeProducerState(d, CarrierName, CarrierPort)
    End Function

    Public Function WithoutEntry(name As String) As ExchangeProducerState
        Dim d As Dictionary(Of String, ExchangeProducerEntry) = Copy()
        d.Remove(name)
        Dim carrier As String = If(String.Equals(CarrierName, name, StringComparison.Ordinal), "", CarrierName)
        Return New ExchangeProducerState(d, carrier, If(carrier.Length = 0, 0, CarrierPort))
    End Function

    Public Function WithCarrier(name As String, port As Integer) As ExchangeProducerState
        Return New ExchangeProducerState(Copy(), name, port)
    End Function

    Public Overrides Function ToString() As String
        Return "ExchangeProducerState"
    End Function

End Class

Friend NotInheritable Class ExchangeProducerDpapiCipher
    Implements IExchangeSecretCipher

    Public Shared ReadOnly Instance As New ExchangeProducerDpapiCipher()

    Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("FastMediaSorter.Exchange.ProducerIds.v1")

    Private Sub New()
    End Sub

    Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
        Return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)
    End Function

    Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
        Return ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser)
    End Function
End Class

Friend NotInheritable Class ExchangeProducerStore

    Private Const FileName As String = "producer-ids.dat"
    Private Const Magic As String = "FMSXPRD1"

    Private ReadOnly _directory As String
    Private ReadOnly _cipher As IExchangeSecretCipher

    Public Sub New(directory As String, cipher As IExchangeSecretCipher)
        If String.IsNullOrWhiteSpace(directory) Then Throw New ArgumentException("directory", NameOf(directory))
        _directory = directory
        _cipher = If(cipher, ExchangeProducerDpapiCipher.Instance)
    End Sub

    Public Shared Function CreateDefault() As ExchangeProducerStore
        Return New ExchangeProducerStore(ExchangeCredentialStore.DefaultDirectory(), ExchangeProducerDpapiCipher.Instance)
    End Function

    Public ReadOnly Property FilePath As String
        Get
            Return Path.Combine(_directory, FileName)
        End Get
    End Property

    ''' <summary>True when the file exists. A pure read: it creates nothing.</summary>
    Public Function Exists() As Boolean
        Try
            Return File.Exists(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    ''' <summary>What is kept for this device id, or an empty state (no file, a blob of another user or machine, another device's file,
    ''' a damaged one). A pure read.</summary>
    Public Function Load(deviceId As String) As ExchangeProducerState
        Dim blob As Byte()
        Try
            If Not File.Exists(FilePath) Then Return ExchangeProducerState.Empty
            blob = File.ReadAllBytes(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return ExchangeProducerState.Empty
        End Try
        Dim plain As Byte() = Nothing
        Try
            plain = _cipher.Unprotect(blob)
            Dim text As String = Encoding.UTF8.GetString(plain)
            Dim lineEnd As Integer = text.IndexOf(ControlChars.Lf)
            If lineEnd < 0 OrElse Not String.Equals(text.Substring(0, lineEnd), Magic, StringComparison.Ordinal) Then Return ExchangeProducerState.Empty
            Using doc As JsonDocument = JsonDocument.Parse(text.Substring(lineEnd + 1))
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return ExchangeProducerState.Empty
                Dim el As JsonElement = Nothing
                If Not root.TryGetProperty("deviceId", el) OrElse el.ValueKind <> JsonValueKind.String OrElse
                   Not String.Equals(el.GetString(), deviceId, StringComparison.Ordinal) Then Return ExchangeProducerState.Empty
                Dim entries As New Dictionary(Of String, ExchangeProducerEntry)(StringComparer.Ordinal)
                If root.TryGetProperty("shares", el) AndAlso el.ValueKind = JsonValueKind.Object Then
                    For Each p As JsonProperty In el.EnumerateObject()
                        If p.Value.ValueKind <> JsonValueKind.Object Then Continue For
                        Dim rid As JsonElement = Nothing
                        Dim sid As JsonElement = Nothing
                        If Not p.Value.TryGetProperty("resourceId", rid) OrElse rid.ValueKind <> JsonValueKind.String OrElse Not ExchangeIds.IsValidId(rid.GetString()) Then Continue For
                        Dim shareId As String = ""
                        If p.Value.TryGetProperty("shareId", sid) AndAlso sid.ValueKind = JsonValueKind.String AndAlso ExchangeIds.IsValidId(sid.GetString()) Then shareId = sid.GetString()
                        entries(p.Name) = New ExchangeProducerEntry(rid.GetString(), shareId)
                    Next
                End If
                Dim carrierName As String = ""
                Dim carrierPort As Integer = 0
                If root.TryGetProperty("carrier", el) AndAlso el.ValueKind = JsonValueKind.Object Then
                    Dim n As JsonElement = Nothing
                    Dim pt As JsonElement = Nothing
                    If el.TryGetProperty("name", n) AndAlso n.ValueKind = JsonValueKind.String AndAlso entries.ContainsKey(n.GetString()) Then
                        carrierName = n.GetString()
                        If el.TryGetProperty("port", pt) AndAlso pt.ValueKind = JsonValueKind.Number Then
                            Dim port As Integer
                            If pt.TryGetInt32(port) AndAlso port >= 1 AndAlso port <= 65535 Then carrierPort = port
                        End If
                    End If
                End If
                Return New ExchangeProducerState(entries, carrierName, carrierPort)
            End Using
        Catch ex As Exception When TypeOf ex Is CryptographicException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is ArgumentException
            Return ExchangeProducerState.Empty
        Finally
            If plain IsNot Nothing Then Array.Clear(plain, 0, plain.Length)
        End Try
    End Function

    ''' <summary>Protects and stores the state for the device id, replacing the earlier file atomically. False when it could not be
    ''' protected or written (the caller then does not trust that a share id survives a restart).</summary>
    Public Function Save(deviceId As String, state As ExchangeProducerState) As Boolean
        If Not ExchangeIds.IsValidId(deviceId) OrElse state Is Nothing Then Return False
        Dim root As New JsonObject()
        root("deviceId") = deviceId
        Dim shares As New JsonObject()
        For Each pair As KeyValuePair(Of String, ExchangeProducerEntry) In state.Entries
            Dim o As New JsonObject()
            o("resourceId") = pair.Value.ResourceId
            If pair.Value.ShareId.Length > 0 Then o("shareId") = pair.Value.ShareId
            shares(pair.Key) = o
        Next
        root("shares") = shares
        If state.CarrierName.Length > 0 Then
            Dim c As New JsonObject()
            c("name") = state.CarrierName
            c("port") = state.CarrierPort
            root("carrier") = c
        End If
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

    ''' <summary>Removes the file ("keep nothing" on Disconnect). Nothing else is touched.</summary>
    Public Sub Delete()
        Try
            If File.Exists(FilePath) Then File.Delete(FilePath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

End Class
