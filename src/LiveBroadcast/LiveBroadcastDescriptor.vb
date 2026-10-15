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
    ''' <summary><c>schemaVersion</c> above 1: the user must update the app, never "damaged file".</summary>
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
    Public Property Url As String = ""
    Public Property Transport As String = ""
    Public Property CertFingerprint As String = ""
    Public Property Inner As String = ""
    Public Property IsSealed As Boolean
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

' DEVICE-EXCHANGE 6.3/7.7: directory, guest and cast records use the same descriptor reader.
Friend Module LiveBroadcastRecords
    Public Function ReadRecord(raw As String) As LiveBroadcastReadResult
        Try
            Using doc As JsonDocument = JsonDocument.Parse(raw)
                Dim descriptor As JsonElement = Nothing
                If Not doc.RootElement.TryGetProperty("descriptor", descriptor) Then Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
                Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(descriptor.GetRawText())
                If result.Status <> LiveReadStatus.Ok Then Return result
                Dim id As JsonElement = Nothing
                If doc.RootElement.TryGetProperty("broadcastId", id) AndAlso id.ValueKind = JsonValueKind.String Then result.Descriptor.BroadcastId = id.GetString()
                Dim credentials As JsonElement = Nothing
                If doc.RootElement.TryGetProperty("credentials", credentials) AndAlso credentials.ValueKind = JsonValueKind.Object Then
                    Dim user As JsonElement = Nothing
                    Dim password As JsonElement = Nothing
                    If credentials.TryGetProperty("user", user) AndAlso user.ValueKind = JsonValueKind.String AndAlso
                       credentials.TryGetProperty("password", password) AndAlso password.ValueKind = JsonValueKind.String Then
                        result.Descriptor.AuthUser = user.GetString()
                        result.Descriptor.Password = password.GetString()
                    End If
                End If
                Return result
            End Using
        Catch ex As JsonException
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End Try
    End Function
End Module
#End If
