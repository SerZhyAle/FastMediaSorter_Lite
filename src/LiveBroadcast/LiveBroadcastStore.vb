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
    ''' Reads all stored broadcast descriptors from disk, newest first.
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
    ''' Atomically writes the entries to disk.
    ''' </summary>
    Public Sub SaveAll(entries As IEnumerable(Of LiveBroadcastDescriptor))
        If entries Is Nothing Then Return

        Try
            Dim dir As String = Path.GetDirectoryName(storeFilePath)
            If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then
                Directory.CreateDirectory(dir)
            End If

            Dim tempFile As String = storeFilePath & ".tmp." & Guid.NewGuid().ToString("N")
            Using fs As New FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None)
                Using writer As New Utf8JsonWriter(fs, New JsonWriterOptions With {.Indented = True})
                    writer.WriteStartArray()
                    For Each d As LiveBroadcastDescriptor In entries
                        WriteDescriptor(writer, d)
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

        If Not String.IsNullOrEmpty(d.Title) Then
            writer.WriteString("title", d.Title)
        End If

        If Not String.IsNullOrEmpty(d.SourceId) Then
            writer.WriteString("sourceId", d.SourceId)
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

End Class
#End If
