#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' Ticket 180 T-2 (the directory model) in the library, because the viewer reads the same shapes through the local
' channel (171): the agent keeps the account's directory in this model; the viewer receives the snapshot as JSON
' WITHOUT `access` or `shareId` and asks for `access` of one resource at a time.
'
' DEVICE-EXCHANGE 6: a reader ignores unknown members, skips a record it cannot use and refuses one above 16 KiB.
' DEVICE-EXCHANGE 7.4: `directory` is a full snapshot with a `revision`; every `changed` carries the next one; a gap
' in the revisions means the model asks for `list` again. A record is held as the raw JSON the server sent, so a
' newer member this program does not know still travels to whoever reads the model next.
'
' ToString of a record never prints a member (a resource holds `access`).

Friend NotInheritable Class ExchangeDeviceRecord

    Public ReadOnly DeviceId As String
    Public ReadOnly DeviceName As String
    Public ReadOnly Product As String
    Public ReadOnly ProductVersion As String
    Public ReadOnly Platform As String
    Public ReadOnly Roles As IReadOnlyList(Of String)
    Public ReadOnly Presence As String
    Public ReadOnly LastSeen As String
    Public ReadOnly Raw As String

    Public Sub New(raw As String, deviceId As String, deviceName As String, product As String, productVersion As String, platform As String,
                   roles As IReadOnlyList(Of String), presence As String, lastSeen As String)
        Me.Raw = raw
        Me.DeviceId = deviceId
        Me.DeviceName = deviceName
        Me.Product = product
        Me.ProductVersion = productVersion
        Me.Platform = platform
        Me.Roles = roles
        Me.Presence = presence
        Me.LastSeen = lastSeen
    End Sub

    Public ReadOnly Property IsOnline As Boolean
        Get
            Return String.Equals(Presence, ExchangeWire.PresenceOnline, StringComparison.Ordinal)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return "ExchangeDeviceRecord"
    End Function
End Class

Friend NotInheritable Class ExchangeResourceRecord

    Public ReadOnly ResourceId As String
    Public ReadOnly DeviceId As String
    Public ReadOnly Kind As String
    Public ReadOnly Name As String
    Public ReadOnly Presence As String
    ''' <summary>The `access` member as compact JSON, or "" when the record carries none (an offline producer's resource has none).</summary>
    Public ReadOnly AccessJson As String
    Public ReadOnly Raw As String

    ''' <summary>Any protection declaration is locked until this client implements DEVICE-EXCHANGE 17 AH.</summary>
    Public ReadOnly IsProtected As Boolean
    Public ReadOnly ProtectionScheme As String
    Public ReadOnly Property CanAuthorize As Boolean
        Get
            Return Not IsProtected OrElse String.Equals(ProtectionScheme, "pin-v1", StringComparison.Ordinal)
        End Get
    End Property

    Public Sub New(raw As String, resourceId As String, deviceId As String, kind As String, name As String, presence As String, accessJson As String,
                   Optional isProtected As Boolean = False, Optional protectionScheme As String = "")
        Me.Raw = raw
        Me.ResourceId = resourceId
        Me.DeviceId = deviceId
        Me.Kind = kind
        Me.Name = name
        Me.Presence = presence
        Me.IsProtected = isProtected
        Me.ProtectionScheme = protectionScheme
        Me.AccessJson = If(CanAuthorize, accessJson, "")
    End Sub

    Public ReadOnly Property HasAccess As Boolean
        Get
            Return AccessJson.Length > 0
        End Get
    End Property

    Public ReadOnly Property IsSupportedKind As Boolean
        Get
            Return String.Equals(Kind, ExchangeWire.KindSftpShare, StringComparison.Ordinal)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return "ExchangeResourceRecord"
    End Function
End Class

Friend NotInheritable Class ExchangeBroadcastRecord

    Public ReadOnly BroadcastId As String
    Public ReadOnly DeviceId As String
    Public ReadOnly Mode As String
    Public ReadOnly Title As String
    Public ReadOnly Raw As String

    Public Sub New(raw As String, broadcastId As String, deviceId As String, mode As String, title As String)
        Me.Raw = raw
        Me.BroadcastId = broadcastId
        Me.DeviceId = deviceId
        Me.Mode = mode
        Me.Title = title
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeBroadcastRecord"
    End Function
End Class

''' <summary>Reads one record of section 6. Returns False for a record that is not an object, is above 16 KiB, or lacks its id.</summary>
Friend NotInheritable Class ExchangeRecordReader

    Private Sub New()
    End Sub

    Private Shared Function Str(root As JsonElement, name As String) As String
        Dim el As JsonElement = Nothing
        If root.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

    Private Shared Function Open(raw As String, ByRef doc As JsonDocument) As Boolean
        doc = Nothing
        If String.IsNullOrEmpty(raw) OrElse Encoding.UTF8.GetByteCount(raw) > ExchangeWire.MaxRecordBytes Then Return False
        Try
            doc = JsonDocument.Parse(raw)
        Catch ex As JsonException
            Return False
        End Try
        If doc.RootElement.ValueKind <> JsonValueKind.Object Then
            doc.Dispose()
            doc = Nothing
            Return False
        End If
        Return True
    End Function

    Public Shared Function TryReadDevice(raw As String, ByRef record As ExchangeDeviceRecord) As Boolean
        record = Nothing
        Dim doc As JsonDocument = Nothing
        If Not Open(raw, doc) Then Return False
        Using doc
            Dim root As JsonElement = doc.RootElement
            Dim id As String = Str(root, "deviceId")
            If id.Length = 0 Then Return False
            Dim roles As New List(Of String)
            Dim rolesEl As JsonElement = Nothing
            If root.TryGetProperty("roles", rolesEl) AndAlso rolesEl.ValueKind = JsonValueKind.Array Then
                For Each r As JsonElement In rolesEl.EnumerateArray()
                    If r.ValueKind = JsonValueKind.String Then roles.Add(r.GetString())
                Next
            End If
            record = New ExchangeDeviceRecord(raw, id, Str(root, "deviceName"), Str(root, "product"), Str(root, "productVersion"),
                                              Str(root, "platform"), roles, Str(root, "presence"), Str(root, "lastSeen"))
            Return True
        End Using
    End Function

    Public Shared Function TryReadResource(raw As String, ByRef record As ExchangeResourceRecord) As Boolean
        record = Nothing
        Dim doc As JsonDocument = Nothing
        If Not Open(raw, doc) Then Return False
        Using doc
            Dim root As JsonElement = doc.RootElement
            Dim id As String = Str(root, "resourceId")
            If id.Length = 0 Then Return False
            Dim access As String = ""
            Dim protection As JsonElement = Nothing
            Dim isProtected As Boolean = root.TryGetProperty("protection", protection)
            Dim accessEl As JsonElement = Nothing
            If root.TryGetProperty("access", accessEl) AndAlso accessEl.ValueKind = JsonValueKind.Object Then access = ExchangeJson.Compact(accessEl.GetRawText())
            Dim scheme As String = If(isProtected AndAlso protection.ValueKind = JsonValueKind.Object, Str(protection, "scheme"), "")
            record = New ExchangeResourceRecord(raw, id, Str(root, "deviceId"), Str(root, "kind"), Str(root, "name"), Str(root, "presence"), access, isProtected, scheme)
            Return True
        End Using
    End Function

    Public Shared Function TryReadBroadcast(raw As String, ByRef record As ExchangeBroadcastRecord) As Boolean
        record = Nothing
        Dim doc As JsonDocument = Nothing
        If Not Open(raw, doc) Then Return False
        Using doc
            Dim root As JsonElement = doc.RootElement
            Dim id As String = Str(root, "broadcastId")
            If id.Length = 0 Then Return False
            record = New ExchangeBroadcastRecord(raw, id, Str(root, "deviceId"), Str(root, "mode"), Str(root, "title"))
            Return True
        End Using
    End Function

    ''' <summary>The record without its secrets (`access`, `shareId`), for a program that must not hold them.</summary>
    Public Shared Function WithoutSecrets(raw As String) As String
        Dim node As JsonNode = JsonNode.Parse(raw)
        Dim obj As JsonObject = TryCast(node, JsonObject)
        If obj Is Nothing Then Return raw
        obj.Remove("access")
        obj.Remove("shareId")
        Return obj.ToJsonString()
    End Function

End Class

''' <summary>An immutable view of the account's directory at one revision.</summary>
Friend NotInheritable Class ExchangeDirectorySnapshot

    Public ReadOnly Revision As Long
    Public ReadOnly Devices As IReadOnlyList(Of ExchangeDeviceRecord)
    Public ReadOnly Resources As IReadOnlyList(Of ExchangeResourceRecord)
    Public ReadOnly Broadcasts As IReadOnlyList(Of ExchangeBroadcastRecord)

    Public Shared ReadOnly Empty As New ExchangeDirectorySnapshot(0, New List(Of ExchangeDeviceRecord), New List(Of ExchangeResourceRecord), New List(Of ExchangeBroadcastRecord))

    Public Sub New(revision As Long, devices As IReadOnlyList(Of ExchangeDeviceRecord), resources As IReadOnlyList(Of ExchangeResourceRecord),
                   broadcasts As IReadOnlyList(Of ExchangeBroadcastRecord))
        Me.Revision = revision
        Me.Devices = devices
        Me.Resources = resources
        Me.Broadcasts = broadcasts
    End Sub

    ''' <summary>The snapshot without this device's own records (a consumer hides what it serves itself, 9 rule 1).</summary>
    Public Function WithoutDevice(ownDeviceId As String) As ExchangeDirectorySnapshot
        If String.IsNullOrEmpty(ownDeviceId) Then Return Me
        Return New ExchangeDirectorySnapshot(Revision,
            Devices.Where(Function(d) Not String.Equals(d.DeviceId, ownDeviceId, StringComparison.Ordinal)).ToList(),
            Resources.Where(Function(r) Not String.Equals(r.DeviceId, ownDeviceId, StringComparison.Ordinal)).ToList(),
            Broadcasts.Where(Function(b) Not String.Equals(b.DeviceId, ownDeviceId, StringComparison.Ordinal)).ToList())
    End Function

    ''' <summary>The JSON the local channel carries: no `access`, no `shareId`.</summary>
    Public Function ToLocalJson() As String
        Dim root As New JsonObject()
        root("revision") = Revision
        Dim d As New JsonArray()
        For Each r As ExchangeDeviceRecord In Devices
            d.Add(JsonNode.Parse(ExchangeRecordReader.WithoutSecrets(r.Raw)))
        Next
        Dim rs As New JsonArray()
        For Each r As ExchangeResourceRecord In Resources
            rs.Add(JsonNode.Parse(ExchangeRecordReader.WithoutSecrets(r.Raw)))
        Next
        Dim b As New JsonArray()
        For Each r As ExchangeBroadcastRecord In Broadcasts
            b.Add(JsonNode.Parse(ExchangeRecordReader.WithoutSecrets(r.Raw)))
        Next
        root("devices") = d
        root("resources") = rs
        root("broadcasts") = b
        Return root.ToJsonString()
    End Function

    ''' <summary>Reads what <see cref="ToLocalJson"/> wrote.</summary>
    Public Shared Function FromLocalJson(json As String) As ExchangeDirectorySnapshot
        Dim devices As New List(Of ExchangeDeviceRecord)
        Dim resources As New List(Of ExchangeResourceRecord)
        Dim broadcasts As New List(Of ExchangeBroadcastRecord)
        Dim revision As Long = 0
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Dim root As JsonElement = doc.RootElement
            Dim el As JsonElement = Nothing
            If root.TryGetProperty("revision", el) AndAlso el.ValueKind = JsonValueKind.Number Then el.TryGetInt64(revision)
            If root.TryGetProperty("devices", el) AndAlso el.ValueKind = JsonValueKind.Array Then
                For Each item As JsonElement In el.EnumerateArray()
                    Dim rec As ExchangeDeviceRecord = Nothing
                    If ExchangeRecordReader.TryReadDevice(item.GetRawText(), rec) Then devices.Add(rec)
                Next
            End If
            If root.TryGetProperty("resources", el) AndAlso el.ValueKind = JsonValueKind.Array Then
                For Each item As JsonElement In el.EnumerateArray()
                    Dim rec As ExchangeResourceRecord = Nothing
                    If ExchangeRecordReader.TryReadResource(item.GetRawText(), rec) Then resources.Add(rec)
                Next
            End If
            If root.TryGetProperty("broadcasts", el) AndAlso el.ValueKind = JsonValueKind.Array Then
                For Each item As JsonElement In el.EnumerateArray()
                    Dim rec As ExchangeBroadcastRecord = Nothing
                    If ExchangeRecordReader.TryReadBroadcast(item.GetRawText(), rec) Then broadcasts.Add(rec)
                Next
            End If
        End Using
        Return New ExchangeDirectorySnapshot(revision, devices, resources, broadcasts)
    End Function

End Class

Friend Enum ExchangeDirectoryApply
    ''' <summary>The change was applied; the revision moved.</summary>
    Applied = 0
    ''' <summary>A revision at or below the one held: ignored.</summary>
    Stale = 1
    ''' <summary>A revision that skips one, or a change before any snapshot: the caller sends `list` again.</summary>
    Gap = 2
End Enum

''' <summary>The model the agent keeps: replaced by `directory`, advanced by `changed`. Thread-safe.</summary>
Friend NotInheritable Class ExchangeDirectoryModel

    Private ReadOnly _gate As New Object()
    Private _revision As Long
    Private _hasSnapshot As Boolean
    Private ReadOnly _devices As New Dictionary(Of String, ExchangeDeviceRecord)(StringComparer.Ordinal)
    Private ReadOnly _resources As New Dictionary(Of String, ExchangeResourceRecord)(StringComparer.Ordinal)
    Private ReadOnly _broadcasts As New Dictionary(Of String, ExchangeBroadcastRecord)(StringComparer.Ordinal)
    Private _skipped As Integer

    ''' <summary>Records refused or unusable so far (above 16 KiB, no id): counted for a log line, never shown.</summary>
    Public ReadOnly Property SkippedRecords As Integer
        Get
            SyncLock _gate
                Return _skipped
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property HasSnapshot As Boolean
        Get
            SyncLock _gate
                Return _hasSnapshot
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Revision As Long
        Get
            SyncLock _gate
                Return _revision
            End SyncLock
        End Get
    End Property

    ''' <summary>A full `directory` replaces everything.</summary>
    Public Sub ApplyDirectory(directory As ExchangeEnvelope)
        SyncLock _gate
            _devices.Clear()
            _resources.Clear()
            _broadcasts.Clear()
            AddRecords(directory.Records("devices"), directory.Records("resources"), directory.Records("broadcasts"))
            _revision = directory.Number("revision")
            _hasSnapshot = True
        End SyncLock
    End Sub

    Public Function ApplyChanged(changed As ExchangeEnvelope) As ExchangeDirectoryApply
        SyncLock _gate
            If Not _hasSnapshot Then Return ExchangeDirectoryApply.Gap
            Dim rev As Long = changed.Number("revision")
            If rev <= _revision Then Return ExchangeDirectoryApply.Stale
            If rev <> _revision + 1 Then Return ExchangeDirectoryApply.Gap
            Dim u As ExchangeRecordSets = changed.Upserts()
            AddRecords(u.Devices, u.Resources, u.Broadcasts)
            Dim r As ExchangeIdSets = changed.Removals()
            For Each id As String In r.DeviceIds
                _devices.Remove(id)
            Next
            For Each id As String In r.ResourceIds
                _resources.Remove(id)
            Next
            For Each id As String In r.BroadcastIds
                _broadcasts.Remove(id)
            Next
            _revision = rev
            Return ExchangeDirectoryApply.Applied
        End SyncLock
    End Function

    Private Sub AddRecords(devices As IEnumerable(Of String), resources As IEnumerable(Of String), broadcasts As IEnumerable(Of String))
        For Each raw As String In devices
            Dim rec As ExchangeDeviceRecord = Nothing
            If ExchangeRecordReader.TryReadDevice(raw, rec) Then _devices(rec.DeviceId) = rec Else _skipped += 1
        Next
        For Each raw As String In resources
            Dim rec As ExchangeResourceRecord = Nothing
            If ExchangeRecordReader.TryReadResource(raw, rec) Then _resources(rec.ResourceId) = rec Else _skipped += 1
        Next
        For Each raw As String In broadcasts
            Dim rec As ExchangeBroadcastRecord = Nothing
            If ExchangeRecordReader.TryReadBroadcast(raw, rec) Then _broadcasts(rec.BroadcastId) = rec Else _skipped += 1
        Next
    End Sub

    ''' <summary>Forgets everything (the stream ended, the account was left).</summary>
    Public Sub Clear()
        SyncLock _gate
            _devices.Clear()
            _resources.Clear()
            _broadcasts.Clear()
            _revision = 0
            _hasSnapshot = False
        End SyncLock
    End Sub

    Public Function Snapshot() As ExchangeDirectorySnapshot
        SyncLock _gate
            Return New ExchangeDirectorySnapshot(_revision, _devices.Values.ToList(), _resources.Values.ToList(), _broadcasts.Values.ToList())
        End SyncLock
    End Function

    ''' <summary>The `access` JSON of one resource, or "" (unknown id, a resource without access).</summary>
    Public Function AccessOf(resourceId As String) As String
        SyncLock _gate
            Dim rec As ExchangeResourceRecord = Nothing
            If _resources.TryGetValue(resourceId, rec) Then Return rec.AccessJson
            Return ""
        End SyncLock
    End Function

End Class
#End If
