#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Text.Json

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §5.
' Atomic storage for recent broadcast descriptors in %LOCALAPPDATA%\SZA\FastMediaSorter\live-broadcasts.json.

Friend NotInheritable Class LiveBroadcastStore

    Public Const MaxEntries As Integer = 20
    Private ReadOnly storeFilePath As String

    Public Sub New(Optional customFilePath As String = Nothing)
        If Not String.IsNullOrEmpty(customFilePath) Then
            storeFilePath = customFilePath
        Else
            storeFilePath = Path.Combine(AppPaths.LocalAppDataRoot(), "live-broadcasts.json")
        End If
    End Sub

    ''' <summary>
    ''' Reads all stored broadcast descriptors from disk, newest first. An entry this program cannot use (a later schema written by
    ''' a newer program, a mode it cannot play, a damaged one) is not returned - and is not lost either: <see cref="SaveAll"/> keeps
    ''' it in the file byte for byte (LIVE-BROADCAST 1.1: a record this program cannot read is refused, never destroyed).
    ''' </summary>
    Public Function LoadAll() As List(Of LiveBroadcastDescriptor)
        Dim list As New List(Of LiveBroadcastDescriptor)()
        If Not File.Exists(storeFilePath) Then Return list

        Try
            Dim jsonText As String = File.ReadAllText(storeFilePath, Encoding.UTF8)
            If String.IsNullOrWhiteSpace(jsonText) Then Return list

            Dim doc As JsonDocument = JsonDocument.Parse(jsonText)
            Using doc
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return list
                For Each elem As JsonElement In doc.RootElement.EnumerateArray()
                    If elem.ValueKind = JsonValueKind.Object Then
                        Dim bytes As Byte() = Encoding.UTF8.GetBytes(elem.GetRawText())
                        Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadJsonBytes(bytes)
                        If res.Status = LiveReadStatus.Ok AndAlso res.Descriptor IsNot Nothing Then
                            list.Add(res.Descriptor)
                        End If
                    End If
                Next
            End Using
        Catch ex As Exception
            AppFileLogger.LogException("Failed to load live broadcasts store", ex)
        End Try

        Return list
    End Function

    ''' <summary>
    ''' Inserts or updates a descriptor according to sourceId and URL rules, and saves to disk.
    ''' </summary>
    Public Sub Upsert(descriptor As LiveBroadcastDescriptor)
        If descriptor Is Nothing OrElse String.IsNullOrWhiteSpace(descriptor.Url) Then Return
        ' Ticket 234 owns encrypted-channel persistence. Never rewrite a sealed channel as a clear one.
        If descriptor.Endpoints.Exists(Function(ep) ep.IsSealed) Then Return

        Dim list As List(Of LiveBroadcastDescriptor) = LoadAll()

        ' Upsert rule 1: If sourceId is present, replace entry with same sourceId (ordinal match)
        ' Upsert rule 2: Without sourceId, replace entry with same normalized URL
        Dim existingIndex As Integer = -1

        If Not String.IsNullOrEmpty(descriptor.SourceId) Then
            existingIndex = list.FindIndex(Function(d) String.Equals(d.SourceId, descriptor.SourceId, StringComparison.Ordinal))
        Else
            existingIndex = list.FindIndex(Function(d) String.Equals(NormalizeUrl(d.Url), NormalizeUrl(descriptor.Url), StringComparison.OrdinalIgnoreCase))
        End If

        If existingIndex >= 0 Then
            ' LIVE-BROADCAST item G as DEVICE-EXCHANGE item BP resolves it: of two records of one sourceId,
            ' the one with the LATER startedAt is the channel's address. An incoming record with an EARLIER
            ' start never displaces a newer stored one (an out-of-order poll, a clock behind); equal or absent
            ' starts keep the store's own order - the incoming record wins, as before. updatedAt decides
            ' nothing.
            Dim existingStarted As DateTimeOffset? = RecordStartedAtOf(list(existingIndex))
            Dim incomingStarted As DateTimeOffset? = RecordStartedAtOf(descriptor)
            If existingStarted.HasValue AndAlso incomingStarted.HasValue AndAlso incomingStarted.Value < existingStarted.Value Then Return
            list.RemoveAt(existingIndex)
        End If

        ' Rule 3: Place at the top
        list.Insert(0, descriptor)

        ' Rule 3: Cap at MaxEntries
        While list.Count > MaxEntries
            list.RemoveAt(list.Count - 1)
        End While

        SaveAll(list)
    End Sub

    ''' <summary>
    ''' The stored entries this program cannot use, as the exact JSON text they have in the file, in file order. They survive every
    ''' rewrite: a newer program of the same product may have written them and will read them again. Only the entry-by-entry
    ''' damage is tolerated here; a file that is not a JSON array at all is treated as empty (as <see cref="LoadAll"/> does).
    ''' </summary>
    Private Function ReadUnusableEntries() As List(Of String)
        Dim kept As New List(Of String)()
        If Not File.Exists(storeFilePath) Then Return kept
        Try
            Dim jsonText As String = File.ReadAllText(storeFilePath, Encoding.UTF8)
            If String.IsNullOrWhiteSpace(jsonText) Then Return kept
            Using doc As JsonDocument = JsonDocument.Parse(jsonText)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return kept
                For Each elem As JsonElement In doc.RootElement.EnumerateArray()
                    If elem.ValueKind <> JsonValueKind.Object Then Continue For
                    Dim raw As String = elem.GetRawText()
                    Dim res As LiveBroadcastReadResult = LiveBroadcastReader.ReadJsonBytes(Encoding.UTF8.GetBytes(raw))
                    If res.Status = LiveReadStatus.Ok AndAlso res.Descriptor IsNot Nothing Then Continue For
                    kept.Add(raw)
                Next
            End Using
        Catch ex As Exception
            AppFileLogger.LogException("Failed to read the unusable entries of the live broadcasts store", ex)
        End Try
        Return kept
    End Function

    ''' <summary>True when a kept unusable entry names the same channel as a descriptor now being saved (same rules as <see cref="Upsert"/>:
    ''' sourceId when the entry has one, otherwise the normalised address). The readable descriptor then replaces it.</summary>
    Private Function IsReplacedBy(rawEntry As String, snapshot As List(Of LiveBroadcastDescriptor)) As Boolean
        Dim sourceId As String = Nothing
        Dim url As String = Nothing
        Try
            Using doc As JsonDocument = JsonDocument.Parse(rawEntry)
                Dim p As JsonElement = Nothing
                If doc.RootElement.TryGetProperty("sourceId", p) AndAlso p.ValueKind = JsonValueKind.String Then sourceId = p.GetString()
                If doc.RootElement.TryGetProperty("url", p) AndAlso p.ValueKind = JsonValueKind.String Then url = p.GetString()
            End Using
        Catch ex As JsonException
            Return False
        End Try
        If Not String.IsNullOrEmpty(sourceId) Then
            Return snapshot.Exists(Function(d) String.Equals(d.SourceId, sourceId, StringComparison.Ordinal))
        End If
        If String.IsNullOrWhiteSpace(url) Then Return False
        Return snapshot.Exists(Function(d) String.IsNullOrEmpty(d.SourceId) AndAlso
                                          String.Equals(NormalizeUrl(d.Url), NormalizeUrl(url), StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' Atomically writes the entries to disk. The entries of the file this program cannot use are written back after them, unchanged
    ''' (at most <see cref="MaxEntries"/> of them), unless one of the saved descriptors is the same channel and replaces it.
    ''' </summary>
    Public Sub SaveAll(entries As IEnumerable(Of LiveBroadcastDescriptor))
        If entries Is Nothing Then Return
        Dim snapshot As New List(Of LiveBroadcastDescriptor)(entries)
        If snapshot.Exists(Function(d) d.Endpoints.Exists(Function(ep) ep.IsSealed)) Then Return

        Dim unusable As List(Of String) = ReadUnusableEntries()
        unusable.RemoveAll(Function(entry) IsReplacedBy(entry, snapshot))
        While unusable.Count > MaxEntries
            unusable.RemoveAt(unusable.Count - 1)
        End While

        Try
            Dim dir As String = Path.GetDirectoryName(storeFilePath)
            If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then
                Directory.CreateDirectory(dir)
            End If

            Dim tempFile As String = storeFilePath & ".tmp." & Guid.NewGuid().ToString("N")
            Using fs As New FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None)
                Using writer As New Utf8JsonWriter(fs, New JsonWriterOptions With {.Indented = True})
                    writer.WriteStartArray()
                    For Each d As LiveBroadcastDescriptor In snapshot
                        WriteDescriptor(writer, d)
                    Next
                    For Each raw As String In unusable
                        writer.WriteRawValue(raw)
                    Next
                    writer.WriteEndArray()
                End Using
            End Using

            If File.Exists(storeFilePath) Then
                File.Replace(tempFile, storeFilePath, Nothing)
            Else
                File.Move(tempFile, storeFilePath)
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Failed to save live broadcasts store", ex)
        End Try
    End Sub

    Private Sub WriteDescriptor(writer As Utf8JsonWriter, d As LiveBroadcastDescriptor)
        writer.WriteStartObject()
        writer.WriteNumber("schemaVersion", d.SchemaVersion)
        writer.WriteString("url", d.Url)
        writer.WriteString("mode", d.Mode)
        If d.AuthScheme.Length > 0 Then
            writer.WriteStartObject("auth")
            writer.WriteString("scheme", d.AuthScheme)
            writer.WriteString("user", d.AuthUser)
            writer.WriteEndObject()
        End If

        If Not String.IsNullOrEmpty(d.Title) Then
            writer.WriteString("title", d.Title)
        End If

        If Not String.IsNullOrEmpty(d.SourceId) Then
            writer.WriteString("sourceId", d.SourceId)
        End If

        ' The record's startedAt, kept so the item G / BP order survives a restart (a reader tolerates
        ' its absence - it is this store's member, not a descriptor member a producer writes).
        If Not String.IsNullOrEmpty(d.RecordStartedAt) Then
            writer.WriteString("recordStartedAt", d.RecordStartedAt)
        End If

        If d.IsLive.HasValue Then
            writer.WriteBoolean("isLive", d.IsLive.Value)
        End If

        If d.TargetLatencyMs.HasValue Then
            writer.WriteNumber("targetLatencyMs", d.TargetLatencyMs.Value)
        End If

        If d.Endpoints IsNot Nothing AndAlso d.Endpoints.Count > 0 Then
            writer.WriteStartArray("endpoints")
            For Each ep As LiveBroadcastEndpoint In d.Endpoints
                writer.WriteStartObject()
                writer.WriteString("url", ep.Url)
                writer.WriteString("mode", ep.Mode)
                If Not String.IsNullOrEmpty(ep.Transport) Then writer.WriteString("transport", ep.Transport)
                If Not String.IsNullOrEmpty(ep.CertFingerprint) Then writer.WriteString("certFingerprint", ep.CertFingerprint)
                If Not String.IsNullOrEmpty(ep.Inner) Then writer.WriteString("inner", ep.Inner)
                If Not String.IsNullOrEmpty(ep.VideoCodec) Then writer.WriteString("videoCodec", ep.VideoCodec)
                If Not String.IsNullOrEmpty(ep.AudioCodec) Then writer.WriteString("audioCodec", ep.AudioCodec)
                If ep.SampleRate.HasValue Then writer.WriteNumber("sampleRate", ep.SampleRate.Value)
                If ep.Bitrate.HasValue Then writer.WriteNumber("bitrate", ep.Bitrate.Value)
                If ep.TargetLatencyMs.HasValue Then writer.WriteNumber("targetLatencyMs", ep.TargetLatencyMs.Value)
                If ep.IsLive.HasValue Then writer.WriteBoolean("isLive", ep.IsLive.Value)
                writer.WriteEndObject()
            Next
            writer.WriteEndArray()
        End If

        writer.WriteEndObject()
    End Sub

    Private Function NormalizeUrl(url As String) As String
        If String.IsNullOrWhiteSpace(url) Then Return ""
        Dim uri As Uri = Nothing
        If Uri.TryCreate(url, UriKind.Absolute, uri) Then
            Return uri.AbsoluteUri
        End If
        Return url.Trim()
    End Function

    ''' <summary>The record's <c>startedAt</c> as an instant, Nothing when the entry has none or its text is not
    ''' a date this program can read (LIVE-BROADCAST item G as DEVICE-EXCHANGE item BP resolves it: an unreadable
    ''' or absent start never orders anything - the store's own order decides, as it always did).</summary>
    Private Shared Function RecordStartedAtOf(descriptor As LiveBroadcastDescriptor) As DateTimeOffset?
        Return LiveBroadcastRecords.StartedAtOf(descriptor)
    End Function

End Class
#End If
