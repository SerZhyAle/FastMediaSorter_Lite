#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Net
Imports System.Text
Imports System.Text.Json

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §3, §4.
' Parser and decoder for .fmsbcast files, FMSBCAST1: barcode text, and descriptor JSON.

Friend Module LiveBroadcastReader

    Public Const MaxPayloadBytes As Integer = 65536
    Public Const BarcodePrefix As String = "FMSBCAST1:"

    ''' <summary>
    ''' Parses a broadcast descriptor from text (JSON or FMSBCAST1: payload).
    ''' </summary>
    Public Function ReadText(text As String) As LiveBroadcastReadResult
        If String.IsNullOrWhiteSpace(text) Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
        End If

        Dim trimmed As String = text.Trim()
        If trimmed.Length > MaxPayloadBytes Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.TooLarge)
        End If

        If trimmed.StartsWith(BarcodePrefix, StringComparison.OrdinalIgnoreCase) Then
            Return ReadBarcodeText(trimmed)
        End If

        If trimmed.StartsWith("{", StringComparison.Ordinal) Then
            Dim bytes As Byte() = Encoding.UTF8.GetBytes(trimmed)
            Return ReadJsonBytes(bytes)
        End If

        Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
    End Function

    ''' <summary>
    ''' Reads a descriptor from a .fmsbcast file or stream (max 64 KiB, tolerates UTF-8 BOM).
    ''' Content may be either JSON or FMSBCAST1: text (§4.2).
    ''' </summary>
    Public Function ReadFile(filePath As String) As LiveBroadcastReadResult
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
        End If

        Try
            Dim info As New FileInfo(filePath)
            If info.Length > MaxPayloadBytes Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.TooLarge)
            End If

            Dim bytes As Byte() = File.ReadAllBytes(filePath)
            Return ReadFileBytes(bytes)
        Catch ex As Exception
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End Try
    End Function

    ''' <summary>
    ''' Reads bytes of a .fmsbcast file (which can be UTF-8 JSON or FMSBCAST1: text).
    ''' </summary>
    Public Function ReadFileBytes(bytes As Byte()) As LiveBroadcastReadResult
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
        End If

        If bytes.Length > MaxPayloadBytes Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.TooLarge)
        End If

        ' Strip UTF-8 BOM if present
        Dim offset As Integer = 0
        If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
            offset = 3
        End If

        Dim text As String = Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset).Trim()
        If text.StartsWith(BarcodePrefix, StringComparison.OrdinalIgnoreCase) Then
            Return ReadBarcodeText(text)
        End If

        Return ReadJsonBytes(bytes, offset, bytes.Length - offset)
    End Function

    ''' <summary>
    ''' Decompresses and parses an FMSBCAST1: payload.
    ''' </summary>
    Private Function ReadBarcodeText(text As String) As LiveBroadcastReadResult
        Dim b64Part As String = text.Substring(BarcodePrefix.Length).Trim()
        If String.IsNullOrEmpty(b64Part) Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End If

        Dim compressed As Byte()
        Try
            compressed = Convert.FromBase64String(b64Part)
        Catch
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End Try

        If compressed.Length = 0 Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End If

        ' Decompress with GZIP and enforce MaxPayloadBytes limit on decompressed bytes
        Dim decompressed As Byte()
        Try
            Using msIn As New MemoryStream(compressed)
                Using gz As New GZipStream(msIn, CompressionMode.Decompress)
                    Using msOut As New MemoryStream()
                        Dim buf(4095) As Byte
                        Dim totalRead As Integer = 0
                        While True
                            Dim count As Integer = gz.Read(buf, 0, buf.Length)
                            If count <= 0 Then Exit While
                            totalRead += count
                            If totalRead > MaxPayloadBytes Then
                                Return LiveBroadcastReadResult.Fail(LiveReadStatus.TooLarge)
                            End If
                            msOut.Write(buf, 0, count)
                        End While
                        decompressed = msOut.ToArray()
                    End Using
                End Using
            End Using
        Catch
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End Try

        If decompressed.Length = 0 Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End If

        Return ReadJsonBytes(decompressed, 0, decompressed.Length)
    End Function

    ''' <summary>
    ''' Parses raw JSON bytes with depth limit 32, no trailing commas, no comments.
    ''' </summary>
    Public Function ReadJsonBytes(bytes As Byte(), Optional offset As Integer = 0, Optional count As Integer = -1) As LiveBroadcastReadResult
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
        End If

        If count < 0 Then count = bytes.Length - offset
        If count > MaxPayloadBytes Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.TooLarge)
        End If

        ' Strip UTF-8 BOM if present in slice
        If count >= 3 AndAlso bytes(offset) = &HEF AndAlso bytes(offset + 1) = &HBB AndAlso bytes(offset + 2) = &HBF Then
            offset += 3
            count -= 3
        End If

        Dim options As New JsonDocumentOptions With {
            .MaxDepth = 32,
            .AllowTrailingCommas = False,
            .CommentHandling = JsonCommentHandling.Disallow
        }

        Dim doc As JsonDocument
        Try
            Dim memory As New ReadOnlyMemory(Of Byte)(bytes, offset, count)
            doc = JsonDocument.Parse(memory, options)
        Catch
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End Try

        Using doc
            Dim root As JsonElement = doc.RootElement
            If root.ValueKind <> JsonValueKind.Object Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            End If

            ' schemaVersion
            Dim schemaProp As JsonElement = Nothing
            If Not root.TryGetProperty("schemaVersion", schemaProp) OrElse schemaProp.ValueKind <> JsonValueKind.Number Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            End If

            Dim schemaVersion As Integer
            If Not schemaProp.TryGetInt32(schemaVersion) Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            End If

            If schemaVersion <= 0 Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            ElseIf schemaVersion > 1 Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.UnsupportedVersion)
            End If

            ' url
            Dim urlProp As JsonElement = Nothing
            If Not root.TryGetProperty("url", urlProp) OrElse urlProp.ValueKind <> JsonValueKind.String Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            End If

            Dim urlStr As String = urlProp.GetString()
            If String.IsNullOrWhiteSpace(urlStr) Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            End If

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(urlStr, UriKind.Absolute, uri) OrElse String.IsNullOrEmpty(uri.Scheme) Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            End If

            If IsLoopbackOrUnspecifiedHost(uri.Host) Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.LoopbackHost)
            End If

            ' mode
            Dim modeProp As JsonElement = Nothing
            If Not root.TryGetProperty("mode", modeProp) OrElse modeProp.ValueKind <> JsonValueKind.String Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
            End If

            Dim modeStr As String = modeProp.GetString()
            Dim modeVal As LiveBroadcastMode = ParseMode(modeStr)

            ' Rule: If top-level mode is not AUDIO_ONLY, return UnsupportedMode immediately
            If modeVal <> LiveBroadcastMode.AudioOnly Then
                Return LiveBroadcastReadResult.Fail(LiveReadStatus.UnsupportedMode)
            End If

            Dim descriptor As New LiveBroadcastDescriptor With {
                .SchemaVersion = schemaVersion,
                .Url = urlStr,
                .Mode = modeStr,
                .ModeValue = modeVal
            }

            ' title (optional)
            Dim titleProp As JsonElement = Nothing
            If root.TryGetProperty("title", titleProp) AndAlso titleProp.ValueKind = JsonValueKind.String Then
                Dim t As String = titleProp.GetString()
                If Not String.IsNullOrWhiteSpace(t) Then
                    descriptor.Title = t.Trim()
                End If
            End If

            ' sourceId (optional)
            Dim sourceProp As JsonElement = Nothing
            If root.TryGetProperty("sourceId", sourceProp) AndAlso sourceProp.ValueKind = JsonValueKind.String Then
                Dim s As String = sourceProp.GetString()
                If Not String.IsNullOrEmpty(s) Then
                    descriptor.SourceId = s
                End If
            End If

            ' isLive (optional)
            Dim isLiveProp As JsonElement = Nothing
            If root.TryGetProperty("isLive", isLiveProp) AndAlso (isLiveProp.ValueKind = JsonValueKind.True OrElse isLiveProp.ValueKind = JsonValueKind.False) Then
                descriptor.IsLive = isLiveProp.GetBoolean()
            End If

            ' targetLatencyMs (optional)
            Dim latencyProp As JsonElement = Nothing
            If root.TryGetProperty("targetLatencyMs", latencyProp) AndAlso latencyProp.ValueKind = JsonValueKind.Number Then
                Dim lat As Long
                If latencyProp.TryGetInt64(lat) AndAlso lat >= 0 Then
                    descriptor.TargetLatencyMs = lat
                End If
            End If

            ' endpoints (optional array)
            Dim endpointsProp As JsonElement = Nothing
            If root.TryGetProperty("endpoints", endpointsProp) AndAlso endpointsProp.ValueKind = JsonValueKind.Array Then
                For Each epElem As JsonElement In endpointsProp.EnumerateArray()
                    If epElem.ValueKind = JsonValueKind.Object Then
                        Dim ep As LiveBroadcastEndpoint = ParseEndpoint(epElem)
                        If ep IsNot Nothing Then
                            descriptor.Endpoints.Add(ep)
                        End If
                    End If
                Next
            End If

            ' If endpoints is empty, synthesize compatibility endpoint from top-level fields
            If descriptor.Endpoints.Count = 0 Then
                descriptor.Endpoints.Add(New LiveBroadcastEndpoint With {
                    .Url = descriptor.Url,
                    .Mode = descriptor.Mode,
                    .ModeValue = descriptor.ModeValue,
                    .Transport = "HTTP",
                    .IsLive = descriptor.IsLive,
                    .TargetLatencyMs = descriptor.TargetLatencyMs
                })
            End If

            Return LiveBroadcastReadResult.Ok(descriptor)
        End Using
    End Function

    Private Function ParseEndpoint(elem As JsonElement) As LiveBroadcastEndpoint
        Dim urlProp As JsonElement = Nothing
        If Not elem.TryGetProperty("url", urlProp) OrElse urlProp.ValueKind <> JsonValueKind.String Then
            Return Nothing
        End If

        Dim urlStr As String = urlProp.GetString()
        If String.IsNullOrWhiteSpace(urlStr) Then Return Nothing

        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(urlStr, UriKind.Absolute, uri) Then Return Nothing
        If IsLoopbackOrUnspecifiedHost(uri.Host) Then Return Nothing

        Dim modeProp As JsonElement = Nothing
        Dim modeStr As String = ""
        If elem.TryGetProperty("mode", modeProp) AndAlso modeProp.ValueKind = JsonValueKind.String Then
            modeStr = modeProp.GetString()
        End If

        Dim modeVal As LiveBroadcastMode = ParseMode(modeStr)

        Dim ep As New LiveBroadcastEndpoint With {
            .Url = urlStr,
            .Mode = modeStr,
            .ModeValue = modeVal
        }

        Dim transProp As JsonElement = Nothing
        If elem.TryGetProperty("transport", transProp) AndAlso transProp.ValueKind = JsonValueKind.String Then
            ep.Transport = transProp.GetString()
        End If

        Dim vCodecProp As JsonElement = Nothing
        If elem.TryGetProperty("videoCodec", vCodecProp) AndAlso vCodecProp.ValueKind = JsonValueKind.String Then
            ep.VideoCodec = vCodecProp.GetString()
        End If

        Dim aCodecProp As JsonElement = Nothing
        If elem.TryGetProperty("audioCodec", aCodecProp) AndAlso aCodecProp.ValueKind = JsonValueKind.String Then
            ep.AudioCodec = aCodecProp.GetString()
        End If

        Dim srProp As JsonElement = Nothing
        If elem.TryGetProperty("sampleRate", srProp) AndAlso srProp.ValueKind = JsonValueKind.Number Then
            Dim sr As Long
            If srProp.TryGetInt64(sr) AndAlso sr >= 0 Then ep.SampleRate = sr
        End If

        Dim brProp As JsonElement = Nothing
        If elem.TryGetProperty("bitrate", brProp) AndAlso brProp.ValueKind = JsonValueKind.Number Then
            Dim br As Long
            If brProp.TryGetInt64(br) AndAlso br >= 0 Then ep.Bitrate = br
        End If

        Dim latProp As JsonElement = Nothing
        If elem.TryGetProperty("targetLatencyMs", latProp) AndAlso latProp.ValueKind = JsonValueKind.Number Then
            Dim lat As Long
            If latProp.TryGetInt64(lat) AndAlso lat >= 0 Then ep.TargetLatencyMs = lat
        End If

        Dim liveProp As JsonElement = Nothing
        If elem.TryGetProperty("isLive", liveProp) AndAlso (liveProp.ValueKind = JsonValueKind.True OrElse liveProp.ValueKind = JsonValueKind.False) Then
            ep.IsLive = liveProp.GetBoolean()
        End If

        Return ep
    End Function

    Public Function ParseMode(modeStr As String) As LiveBroadcastMode
        If String.IsNullOrEmpty(modeStr) Then Return LiveBroadcastMode.Unknown
        Select Case modeStr.Trim().ToUpperInvariant()
            Case "AUDIO_ONLY"
                Return LiveBroadcastMode.AudioOnly
            Case "VIDEO_AUDIO"
                Return LiveBroadcastMode.VideoAudio
            Case "VIDEO_ONLY"
                Return LiveBroadcastMode.VideoOnly
            Case Else
                Return LiveBroadcastMode.Unknown
        End Select
    End Function

    ''' <summary>
    ''' Checks whether a host name or IP address is loopback or unspecified (127.0.0.0/8, ::1, localhost, 0.0.0.0, ::).
    ''' </summary>
    Public Function IsLoopbackOrUnspecifiedHost(host As String) As Boolean
        If String.IsNullOrWhiteSpace(host) Then Return True

        Dim trimmed As String = host.Trim().Trim("["c, "]"c)
        If String.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase) Then Return True

        Dim ip As IPAddress = Nothing
        If IPAddress.TryParse(trimmed, ip) Then
            If IPAddress.IsLoopback(ip) Then Return True
            If ip.Equals(IPAddress.Any) OrElse ip.Equals(IPAddress.IPv6Any) OrElse ip.Equals(IPAddress.None) Then Return True
            Return False
        End If

        Return False
    End Function

End Module
#End If
