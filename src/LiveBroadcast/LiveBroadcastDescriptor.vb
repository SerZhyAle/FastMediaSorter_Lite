#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Text.Json

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §3.1/§3.2. Pure data, modern build only:
' no WinForms, no LibVLC, no network - so every contract fixture runs in Lite.Tests.

''' <summary>The recognised value of a descriptor's required <c>mode</c> string.</summary>
Friend Enum LiveBroadcastMode
    ''' <summary>A string this build does not know. Never guessed from a path or a codec.</summary>
    Unknown = 0
    AudioOnly = 1
    VideoAudio = 2
    VideoOnly = 3
End Enum

''' <summary>The outcome of reading a hand-off text or file (§3.2). The UI never sees an exception.</summary>
Friend Enum LiveReadStatus
    Ok = 0
    ''' <summary>The text looks like none of the three hand-off forms.</summary>
    NotBroadcast = 1
    ''' <summary>JSON / Base64 / GZIP / required fields are damaged.</summary>
    InvalidDocument = 2
    ''' <summary><c>schemaVersion</c> above 1 (also a sealed-only descriptor this build cannot play): refused cleanly with the
    ''' `format-unsupported` text of DEVICE-EXCHANGE (LIVE-BROADCAST 1.1), never "damaged file", never a demand to update.</summary>
    UnsupportedVersion = 3
    ''' <summary>A correct descriptor whose mode (or endpoints) this build cannot play.</summary>
    UnsupportedMode = 4
    ''' <summary>The input or the DECOMPRESSED json is over 64 KiB.</summary>
    TooLarge = 5
    ''' <summary>The host is loopback / unspecified: an address only the device itself can reach.</summary>
    LoopbackHost = 6
End Enum

''' <summary>One candidate address of a broadcast (upstream §3.2 <c>endpoints</c> member).</summary>
Friend NotInheritable Class LiveBroadcastEndpoint
    Public Property BroadcastId As String = ""
    Public Property RequiresAgentSeal As Boolean
    Public Property Url As String = ""
    Public Property Transport As String = ""
    Public Property CertFingerprint As String = ""
    Public Property Inner As String = ""
    Public Property IsSealed As Boolean
    ''' <summary>The <c>seal.v</c> of a sealed endpoint (0 when absent or unreadable). A receiver plays only a version it knows (1);
    ''' any other value leaves the endpoint skippable (LIVE-BROADCAST item Q).</summary>
    Public Property SealVersion As Integer
    ''' <summary>The group key of THIS link, base64url, from a RELAY endpoint's <c>seal.key</c> (an outward descriptor only).
    ''' A secret: never logged, never shown, never put in a ToString.</summary>
    Public Property SealKey As String = ""
    ''' <summary>The tunnel pin, base64url, from a TUNNEL endpoint's <c>seal.pin</c> (an outward descriptor only).</summary>
    Public Property SealPin As String = ""
    Public Property AuthUser As String = ""
    Public Property Password As String
    Public Property Mode As String = ""
    Public Property ModeValue As LiveBroadcastMode = LiveBroadcastMode.Unknown
    Public Property VideoCodec As String
    Public Property AudioCodec As String
    Public Property SampleRate As Long?
    Public Property Bitrate As Long?
    Public Property IsLive As Boolean?
    Public Property TargetLatencyMs As Long?
End Class

''' <summary>An imported broadcast. Holds no <c>JsonDocument</c>; unknown members are dropped.</summary>
Friend NotInheritable Class LiveBroadcastDescriptor
    Public Property SchemaVersion As Integer = 1
    Public Property Url As String = ""
    ''' <summary>The mode string exactly as received.</summary>
    Public Property Mode As String = ""
    Public Property ModeValue As LiveBroadcastMode = LiveBroadcastMode.Unknown
    Public Property Title As String
    Public Property SourceId As String
    Public Property BroadcastId As String
    Public Property RequiresAgentSeal As Boolean
    ''' <summary>The record's <c>startedAt</c> (DEVICE-EXCHANGE 6.3), RFC 3339 text as received, "" when the
    ''' record has none. Two records of one <c>sourceId</c> order by the later value (LIVE-BROADCAST item G as
    ''' item BP resolves it); the record's <c>updatedAt</c> is informational and is not kept.</summary>
    Public Property RecordStartedAt As String = ""
    Public Property AuthScheme As String = ""
    Public Property AuthUser As String = ""
    ' Credentials are session-only; never serialized to the recent-channel store.
    Public Property Password As String
    Public Property IsLive As Boolean?
    Public Property TargetLatencyMs As Long?
    Public ReadOnly Property Endpoints As New List(Of LiveBroadcastEndpoint)()
End Class

''' <summary>Status-bearing result: <see cref="Descriptor"/> is set only when <see cref="Status"/> is Ok.</summary>
Friend NotInheritable Class LiveBroadcastReadResult
    Public ReadOnly Property Status As LiveReadStatus
    Public ReadOnly Property Descriptor As LiveBroadcastDescriptor

    Private Sub New(status As LiveReadStatus, descriptor As LiveBroadcastDescriptor)
        Me.Status = status
        Me.Descriptor = descriptor
    End Sub

    Public Shared Function Ok(descriptor As LiveBroadcastDescriptor) As LiveBroadcastReadResult
        Return New LiveBroadcastReadResult(LiveReadStatus.Ok, descriptor)
    End Function

    Public Shared Function Fail(status As LiveReadStatus) As LiveBroadcastReadResult
        Return New LiveBroadcastReadResult(status, Nothing)
    End Function
End Class

''' <summary>One directory record as a list shows it: a descriptor this program can play, or the refusal it shows instead
''' (LIVE-BROADCAST 1.1: a record this program cannot use is listed with the reason, never dropped without a word).</summary>
Friend NotInheritable Class LiveDirectoryRow
    ''' <summary>The playable descriptor; Nothing when the record is refused.</summary>
    Public ReadOnly Property Descriptor As LiveBroadcastDescriptor
    Public ReadOnly Property Status As LiveReadStatus

    Public Sub New(status As LiveReadStatus, descriptor As LiveBroadcastDescriptor)
        Me.Status = status
        Me.Descriptor = descriptor
    End Sub

    Public ReadOnly Property IsPlayable As Boolean
        Get
            Return Status = LiveReadStatus.Ok AndAlso Descriptor IsNot Nothing
        End Get
    End Property

    ''' <summary>The sentence of the refusal in the language shown (see <see cref="LiveBroadcastMessages"/>); empty for a playable record.</summary>
    Public ReadOnly Property RefusalText As String
        Get
            Return If(IsPlayable, "", LiveBroadcastMessages.ForReadStatus(Status))
        End Get
    End Property
End Class

' DEVICE-EXCHANGE 6.3/7.7: directory, guest and cast records use the same descriptor reader.
Friend Module LiveBroadcastRecords

    ''' <summary>A directory record, read the way a list shows it. Never throws and never returns Nothing.</summary>
    Public Function RowOf(raw As String) As LiveDirectoryRow
        Dim result As LiveBroadcastReadResult = ReadRecord(raw)
        Return New LiveDirectoryRow(result.Status, result.Descriptor)
    End Function

    ''' <summary>The record's <c>startedAt</c> as an instant, Nothing when the descriptor has none or its text is not a date
    ''' this program can read: an unreadable or absent start never orders anything.</summary>
    Public Function StartedAtOf(descriptor As LiveBroadcastDescriptor) As DateTimeOffset?
        If descriptor Is Nothing OrElse String.IsNullOrWhiteSpace(descriptor.RecordStartedAt) Then Return Nothing
        Dim parsed As DateTimeOffset
        If DateTimeOffset.TryParse(descriptor.RecordStartedAt, Globalization.CultureInfo.InvariantCulture,
                                  Globalization.DateTimeStyles.AssumeUniversal, parsed) Then Return parsed.ToUniversalTime()
        Return Nothing
    End Function

    ''' <summary>
    ''' LIVE-BROADCAST item G as DEVICE-EXCHANGE item BP resolves it: of several playable rows that name one non-empty
    ''' <c>sourceId</c>, a list shows ONE - the one with the later <c>startedAt</c>; <c>updatedAt</c> decides nothing. Rows
    ''' whose starts are equal or unreadable keep the snapshot's own order, the later record standing (as the recent-channel
    ''' store keeps the incoming one), so the same snapshot always shows the same row and two records never alternate.
    ''' A refused row, or one without a <c>sourceId</c>, is never collapsed. Returns the indexes of the rows to leave out.
    ''' </summary>
    Public Function SupersededIndexes(rows As IReadOnlyList(Of LiveDirectoryRow)) As HashSet(Of Integer)
        Dim superseded As New HashSet(Of Integer)()
        Dim standing As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For i As Integer = 0 To rows.Count - 1
            Dim row As LiveDirectoryRow = rows(i)
            If row Is Nothing OrElse Not row.IsPlayable OrElse String.IsNullOrEmpty(row.Descriptor.SourceId) Then Continue For
            Dim held As Integer
            If Not standing.TryGetValue(row.Descriptor.SourceId, held) Then
                standing(row.Descriptor.SourceId) = i
                Continue For
            End If
            Dim heldStart As DateTimeOffset? = StartedAtOf(rows(held).Descriptor)
            Dim thisStart As DateTimeOffset? = StartedAtOf(row.Descriptor)
            If heldStart.HasValue AndAlso thisStart.HasValue AndAlso thisStart.Value < heldStart.Value Then
                superseded.Add(i)
            Else
                superseded.Add(held)
                standing(row.Descriptor.SourceId) = i
            End If
        Next
        Return superseded
    End Function

    Public Function ReadRecord(raw As String) As LiveBroadcastReadResult
        If String.IsNullOrWhiteSpace(raw) Then Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        Try
            Using doc As JsonDocument = JsonDocument.Parse(raw)
                Dim descriptor As JsonElement = Nothing
                If Not doc.RootElement.TryGetProperty("descriptor", descriptor) Then Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
                Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(descriptor.GetRawText())
                If result.Status <> LiveReadStatus.Ok Then Return result
                Dim id As JsonElement = Nothing
                If doc.RootElement.TryGetProperty("broadcastId", id) AndAlso id.ValueKind = JsonValueKind.String Then result.Descriptor.BroadcastId = id.GetString()
                result.Descriptor.RequiresAgentSeal = True
                Dim startedAt As JsonElement = Nothing
                If doc.RootElement.TryGetProperty("startedAt", startedAt) AndAlso startedAt.ValueKind = JsonValueKind.String Then
                    Dim text As String = startedAt.GetString()
                    If Not String.IsNullOrEmpty(text) Then result.Descriptor.RecordStartedAt = text
                End If
                Return result
            End Using
        Catch ex As JsonException
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        Catch ex As InvalidOperationException
            ' a record whose root is not an object
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End Try
    End Function

    ' Only called after the person selects an account broadcast (or accepts a cast).
    Public Async Function PrepareAsync(descriptor As LiveBroadcastDescriptor, ct As Threading.CancellationToken) As Threading.Tasks.Task(Of String)
        If Not descriptor.RequiresAgentSeal Then Return ""
        Dim client As New ExchangeAgentClient()
        Dim reply As ExchangeAgentReply = Await client.SendAsync(ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestGetBroadcastSeal,
            Sub(o) o("broadcastId") = descriptor.BroadcastId), 4000, ct)
        If Not reply.IsOk Then Return If(reply.Error.Length > 0, reply.Error, "e2e-missing")
        Try
            Using doc As JsonDocument = JsonDocument.Parse(reply.DataJson)
                Dim credentials As JsonElement
                If doc.RootElement.TryGetProperty("credentials", credentials) AndAlso credentials.ValueKind = JsonValueKind.Object Then
                    descriptor.AuthUser = credentials.GetProperty("user").GetString()
                    descriptor.Password = credentials.GetProperty("password").GetString()
                End If
            End Using
            Return ""
        Catch ex As JsonException
            Return "sealed-failed"
        Catch ex As InvalidOperationException
            Return "sealed-failed"
        Catch ex As Collections.Generic.KeyNotFoundException
            Return "sealed-failed"
        End Try
    End Function
End Module
#End If
