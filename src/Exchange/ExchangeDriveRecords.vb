#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' Specification 203 section 3.2 - the directory half of the Drive channel: one JSON file per record in the
' appDataFolder, named and wrapped exactly as DEVICE-EXCHANGE 8.1 says (cited, not restated here), and the one
' judgement the contract adds to them, item C: a record is live or expired by Drive's clock - the file's
' `modifiedTime` against the `Date` header of the response that listed it - and NEVER by `writtenAt`, which a
' device with a wrong clock writes wrong.
'
' Pure logic only: no HTTP, no storage. The Drive calls are ExchangeDriveClient's, the publishing and reading of
' a whole directory ExchangeDriveDirectory's.
'
' The numbers below marked "working default" are NOT the contract's: item C leaves the default ttlSeconds to the
' freeze at rung 3 (README 8.1, "Owed at rung 3"). They are the values this program writes and the values it
' applies when a record omits the member, kept in this one place so the freeze changes one file.

''' <summary>The kinds of record file (the `type` member and the second part of the file name).</summary>
Friend Enum ExchangeDriveRecordKind
    Device
    Resource
    Broadcast
    Request
End Enum

''' <summary>What Drive's clock says about a record.</summary>
Friend Enum ExchangeDriveRecordState
    ''' <summary>Within one full ttlSeconds of the file's modifiedTime.</summary>
    Live
    ''' <summary>A full ttlSeconds or more has passed: offline (device, resource) or ended (broadcast).</summary>
    Expired
End Enum

''' <summary>Why a record file was not used. A reader skips such a file and goes on; none of these is an error of the account.</summary>
Friend Enum ExchangeDriveRecordRefusal
    None
    ''' <summary>Not UTF-8 JSON, not an object, over the size bound, or a member of the wrong type.</summary>
    Malformed
    ''' <summary>`schemaVersion` this reader does not know (item AG: refused cleanly, never half-read).</summary>
    UnsupportedVersion
    ''' <summary>`type` is not the kind the file name says, or not a kind at all.</summary>
    WrongType
    ''' <summary>The `record` member is missing or is not an object.</summary>
    NoRecord
End Enum

''' <summary>One decoded record file.</summary>
Friend NotInheritable Class ExchangeDriveRecordFile

    Public ReadOnly Kind As ExchangeDriveRecordKind
    Public ReadOnly SchemaVersion As Integer
    ''' <summary>Informational only (item C): never consulted for expiry.</summary>
    Public ReadOnly WrittenAt As String
    ''' <summary>The `ttlSeconds` member, or the working default of the kind when the member was absent or unusable.</summary>
    Public ReadOnly TtlSeconds As Integer
    ''' <summary>The `record` object as raw JSON, so a newer member this program does not know travels on.</summary>
    Public ReadOnly RecordJson As String

    Public Sub New(kind As ExchangeDriveRecordKind, schemaVersion As Integer, writtenAt As String, ttlSeconds As Integer, recordJson As String)
        Me.Kind = kind
        Me.SchemaVersion = schemaVersion
        Me.WrittenAt = If(writtenAt, "")
        Me.TtlSeconds = ttlSeconds
        Me.RecordJson = recordJson
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeDriveRecordFile"
    End Function
End Class

''' <summary>File names, the envelope, the working TTLs and the judgement of item C.</summary>
Friend NotInheritable Class ExchangeDriveRecords

    Private Sub New()
    End Sub

    Public Const FilePrefix As String = "fmsx-"
    Public Const FileSuffix As String = ".json"

    ''' <summary>Working default (not frozen, see the file header): a device stands for this long after its last rewrite.</summary>
    Public Const DeviceTtlSeconds As Integer = 300
    ''' <summary>Working default: a published resource stands for this long after its last rewrite.</summary>
    Public Const ResourceTtlSeconds As Integer = 300
    ''' <summary>Working default: a live broadcast announcement stands for this long after its last rewrite.</summary>
    Public Const BroadcastTtlSeconds As Integer = 90
    ''' <summary>Working default: a request record (8.3) stands for this long.</summary>
    Public Const RequestTtlSeconds As Integer = 900

    ''' <summary>The largest ttlSeconds a reader believes. A record that claims more is read with the default of its kind,
    ''' so one wrong file cannot keep a dead device "online" for a year.</summary>
    Public Const MaxTtlSeconds As Integer = 24 * 60 * 60

    Private Const EnvelopeSchema As Integer = 1
    Private Const ProtectedResourceSchema As Integer = 2
    Private Const MaxFileBytes As Integer = ExchangeWire.MaxRecordBytes + 2048

    Public Shared Function KindName(kind As ExchangeDriveRecordKind) As String
        Select Case kind
            Case ExchangeDriveRecordKind.Device : Return "device"
            Case ExchangeDriveRecordKind.Resource : Return "resource"
            Case ExchangeDriveRecordKind.Broadcast : Return "broadcast"
            Case Else : Return "request"
        End Select
    End Function

    Private Shared Function TryKind(name As String, ByRef kind As ExchangeDriveRecordKind) As Boolean
        Select Case name
            Case "device" : kind = ExchangeDriveRecordKind.Device : Return True
            Case "resource" : kind = ExchangeDriveRecordKind.Resource : Return True
            Case "broadcast" : kind = ExchangeDriveRecordKind.Broadcast : Return True
            Case "request" : kind = ExchangeDriveRecordKind.Request : Return True
            Case Else : Return False
        End Select
    End Function

    ''' <summary>The working TTL a writer uses, and a reader applies when the member is absent.</summary>
    Public Shared Function DefaultTtlSeconds(kind As ExchangeDriveRecordKind) As Integer
        Select Case kind
            Case ExchangeDriveRecordKind.Device : Return DeviceTtlSeconds
            Case ExchangeDriveRecordKind.Resource : Return ResourceTtlSeconds
            Case ExchangeDriveRecordKind.Broadcast : Return BroadcastTtlSeconds
            Case Else : Return RequestTtlSeconds
        End Select
    End Function

    ''' <summary>How often a writer rewrites a record that stands: at least every ttlSeconds / 3 (item C).</summary>
    Public Shared Function RewriteIntervalSeconds(ttlSeconds As Integer) As Integer
        Return Math.Max(1, ttlSeconds \ 3)
    End Function

    ' ------------------------------------------------------------------------------------------ names

    ''' <summary>"fmsx-device-&lt;id&gt;.json" and its siblings; "" when the id is not in the 22-character id form.</summary>
    Public Shared Function FileName(kind As ExchangeDriveRecordKind, id As String) As String
        If Not ExchangeIds.IsValidId(id) Then Return ""
        Return FilePrefix & KindName(kind) & "-" & id & FileSuffix
    End Function

    ''' <summary>True for a name of exactly the form the contract gives. Anything else in the folder - the packet queue, a
    ''' probe file, a name another product wrote - is not a record file and is left alone.</summary>
    Public Shared Function TryParseFileName(name As String, ByRef kind As ExchangeDriveRecordKind, ByRef id As String) As Boolean
        id = ""
        If String.IsNullOrEmpty(name) OrElse Not name.StartsWith(FilePrefix, StringComparison.Ordinal) OrElse
           Not name.EndsWith(FileSuffix, StringComparison.Ordinal) Then Return False
        Dim middle As String = name.Substring(FilePrefix.Length, name.Length - FilePrefix.Length - FileSuffix.Length)
        Dim dash As Integer = middle.IndexOf("-"c)
        If dash <= 0 Then Return False
        Dim parsedKind As ExchangeDriveRecordKind
        If Not TryKind(middle.Substring(0, dash), parsedKind) Then Return False
        Dim parsedId As String = middle.Substring(dash + 1)
        If Not ExchangeIds.IsValidId(parsedId) Then Return False
        kind = parsedKind
        id = parsedId
        Return True
    End Function

    ' ------------------------------------------------------------------------------------------ the envelope

    ''' <summary>The file body of a record: the envelope of 8.1 around <paramref name="recordJson"/>. A resource record
    ''' that carries `protection` is wrapped with schemaVersion 2 (item AG). Nothing when the record is no JSON object or
    ''' is over the bound a reader accepts.</summary>
    Public Shared Function Encode(kind As ExchangeDriveRecordKind, recordJson As String, ttlSeconds As Integer, writtenAtUtc As DateTime) As Byte()
        If String.IsNullOrEmpty(recordJson) OrElse Encoding.UTF8.GetByteCount(recordJson) > ExchangeWire.MaxRecordBytes Then Return Nothing
        Dim record As JsonObject
        Try
            record = TryCast(JsonNode.Parse(recordJson), JsonObject)
        Catch ex As JsonException
            Return Nothing
        End Try
        If record Is Nothing Then Return Nothing

        Dim schema As Integer = EnvelopeSchema
        If kind = ExchangeDriveRecordKind.Resource AndAlso record.ContainsKey("protection") Then schema = ProtectedResourceSchema
        Dim ttl As Integer = If(ttlSeconds > 0 AndAlso ttlSeconds <= MaxTtlSeconds, ttlSeconds, DefaultTtlSeconds(kind))

        Dim envelope As New JsonObject()
        envelope("schemaVersion") = schema
        envelope("type") = KindName(kind)
        envelope("writtenAt") = writtenAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        envelope("ttlSeconds") = ttl
        envelope("record") = record
        Return Encoding.UTF8.GetBytes(envelope.ToJsonString())
    End Function

    ''' <summary>Reads a record file. <paramref name="expectedKind"/> is the kind the file NAME promised; a body that says
    ''' another type is refused, so a renamed file cannot pose as a device. Never throws on content.</summary>
    Public Shared Function TryDecode(body As Byte(), expectedKind As ExchangeDriveRecordKind, ByRef file As ExchangeDriveRecordFile, ByRef refusal As ExchangeDriveRecordRefusal) As Boolean
        file = Nothing
        refusal = ExchangeDriveRecordRefusal.None
        If body Is Nothing OrElse body.Length = 0 OrElse body.Length > MaxFileBytes Then
            refusal = ExchangeDriveRecordRefusal.Malformed
            Return False
        End If
        Try
            Using document As JsonDocument = JsonDocument.Parse(body)
                Dim root As JsonElement = document.RootElement
                If root.ValueKind <> JsonValueKind.Object Then
                    refusal = ExchangeDriveRecordRefusal.Malformed
                    Return False
                End If

                Dim typeText As String = ReadText(root, "type")
                Dim kind As ExchangeDriveRecordKind
                If Not TryKind(typeText, kind) OrElse kind <> expectedKind Then
                    refusal = ExchangeDriveRecordRefusal.WrongType
                    Return False
                End If

                Dim schema As Integer = 0
                Dim schemaEl As JsonElement = Nothing
                If Not root.TryGetProperty("schemaVersion", schemaEl) OrElse schemaEl.ValueKind <> JsonValueKind.Number OrElse Not schemaEl.TryGetInt32(schema) Then
                    refusal = ExchangeDriveRecordRefusal.UnsupportedVersion
                    Return False
                End If
                ' 1 for every kind; 2 only for a resource record (it carries `protection`). Any other number is a
                ' version this reader does not know and refuses cleanly (item AG).
                Dim known As Boolean = schema = EnvelopeSchema OrElse (schema = ProtectedResourceSchema AndAlso kind = ExchangeDriveRecordKind.Resource)
                If Not known Then
                    refusal = ExchangeDriveRecordRefusal.UnsupportedVersion
                    Return False
                End If

                Dim recordEl As JsonElement = Nothing
                If Not root.TryGetProperty("record", recordEl) OrElse recordEl.ValueKind <> JsonValueKind.Object Then
                    refusal = ExchangeDriveRecordRefusal.NoRecord
                    Return False
                End If
                Dim recordJson As String = recordEl.GetRawText()
                If Encoding.UTF8.GetByteCount(recordJson) > ExchangeWire.MaxRecordBytes Then
                    refusal = ExchangeDriveRecordRefusal.Malformed
                    Return False
                End If

                Dim ttl As Integer = 0
                Dim ttlEl As JsonElement = Nothing
                If Not root.TryGetProperty("ttlSeconds", ttlEl) OrElse ttlEl.ValueKind <> JsonValueKind.Number OrElse Not ttlEl.TryGetInt32(ttl) _
                   OrElse ttl <= 0 OrElse ttl > MaxTtlSeconds Then
                    ttl = DefaultTtlSeconds(kind)
                End If

                file = New ExchangeDriveRecordFile(kind, schema, ReadText(root, "writtenAt"), ttl, recordJson)
                Return True
            End Using
        Catch ex As JsonException
            refusal = ExchangeDriveRecordRefusal.Malformed
            Return False
        End Try
    End Function

    Private Shared Function ReadText(element As JsonElement, name As String) As String
        Dim el As JsonElement = Nothing
        If element.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

    ' ------------------------------------------------------------------------------------------ item C

    ''' <summary>Parses Drive's RFC 3339 `modifiedTime` / `createdTime` ("2026-10-23T13:47:32.267Z").</summary>
    Public Shared Function TryParseDriveTime(text As String, ByRef instant As DateTimeOffset) As Boolean
        Return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal Or DateTimeStyles.AdjustToUniversal, instant)
    End Function

    ''' <summary>Item C: a record is Expired only after a FULL <paramref name="ttlSeconds"/> has passed since its Drive
    ''' <paramref name="modifiedTime"/>, measured against the `Date` header of the response that listed it
    ''' (<paramref name="driveNow"/>) - never against this machine's clock and never against `writtenAt`. A time that
    ''' cannot be parsed proves nothing live, so it reads as Expired; a modifiedTime ahead of the header (Drive
    ''' front-ends differ by a second or two) is not yet old, so it reads as Live.</summary>
    Public Shared Function Judge(modifiedTime As String, driveNow As DateTimeOffset, ttlSeconds As Integer) As ExchangeDriveRecordState
        Dim modified As DateTimeOffset
        If Not TryParseDriveTime(modifiedTime, modified) Then Return ExchangeDriveRecordState.Expired
        Dim ttl As Integer = If(ttlSeconds > 0, ttlSeconds, 1)
        Dim age As TimeSpan = driveNow - modified
        Return If(age >= TimeSpan.FromSeconds(ttl), ExchangeDriveRecordState.Expired, ExchangeDriveRecordState.Live)
    End Function

End Class
#End If
