Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.Encodings.Web
Imports System.Text.Json

''' <summary>
''' Result of building a StreamsPlayer broadcast resource (.spres + QR + quick link).
''' </summary>
Public NotInheritable Class BroadcastConfigResult
    Public Property ConfigJson As String = ""
    Public Property QrPng As Byte()
    Public Property HasExternal As Boolean
    Public Property HasIpv6 As Boolean
    Public Property LanDisplay As String = ""
    Public Property AccessNote As String = ""
    Public Property QrOverflow As Boolean
    Public Property ClipboardLink As String = ""
End Class

''' <summary>
''' Test-only injection of non-deterministic timestamp/name values.
''' </summary>
Public NotInheritable Class BroadcastConfigStamp
    Public Property ResourceName As String = ""
    Public Property CreatedAtUtc As Date = Date.MinValue
    Public Property ExpiresAtUtc As Date = Date.MinValue
End Class

''' <summary>
''' Builds the .spres resource document, QR code, and SPCH1 clipboard quick link for StreamsPlayer
''' from a live broadcast record and worker status.
''' Contract: CONTRACT_BROADCAST_RESOURCE.md (schemaVersion 1).
''' </summary>
Public Module BroadcastConfigBuilder

    Private Const QrPrefix As String = "SPRES1:"
    Private Const QrComfortLimit As Integer = 900
    Private Const QrHardLimit As Integer = 2331

    Private ReadOnly JsonStringOpts As New JsonSerializerOptions With {.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping}

    Private ReadOnly AudioExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".mka", ".alac", ".aiff", ".ape"
    }

    ''' <summary>
    ''' Builds the .spres config, QR code image, and SPCH1 clipboard line.
    ''' </summary>
    Public Function Build(broadcast As WorkerBroadcast,
                          status As WorkerStatus,
                          includeExternal As Boolean,
                          Optional stamp As BroadcastConfigStamp = Nothing) As BroadcastConfigResult
        If broadcast Is Nothing Then Return Nothing
        If status Is Nothing Then Return Nothing

        Dim port As Integer = If(status.BroadcastPort > 0, status.BroadcastPort, status.ListenPort)
        If port <= 0 Then port = 55272

        Dim reach As WorkerReachability = status.Reachability
        Dim lanFromReach As String = If(reach IsNot Nothing, If(reach.LanAddress, ""), "")
        Dim lanFallback As String = NetworkInfo.LocalIPv4()
        Dim lan As String = If(lanFromReach.Length > 0, lanFromReach, lanFallback)

        Dim paths As New StringBuilder()
        If lan.Length > 0 Then AppendAccessPath(paths, "lan", lan, port)

        Dim hasIpv6 As Boolean = False
        If includeExternal AndAlso reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.Ipv6Address) Then
            AppendAccessPath(paths, "ipv6", reach.Ipv6Address, port)
            hasIpv6 = True
        End If

        Dim hasExt As Boolean = False
        Dim extPort As Integer = port
        If includeExternal AndAlso reach IsNot Nothing AndAlso Not reach.IsCgnat AndAlso Not String.IsNullOrEmpty(reach.ExternalHost) Then
            extPort = If(reach.ExternalPort > 0, reach.ExternalPort, port)
            AppendAccessPath(paths, "portforward", reach.ExternalHost, extPort)
            hasExt = True
        End If

        If paths.Length = 0 Then Return Nothing

        Dim resourceName As String = ResolveResourceName(broadcast)
        Dim createdAtStr As String = broadcast.CreatedAt
        Dim expiresAtStr As String = If(broadcast.ExpiresAt, "")

        If stamp IsNot Nothing Then
            If Not String.IsNullOrEmpty(stamp.ResourceName) Then resourceName = stamp.ResourceName
            If stamp.CreatedAtUtc <> Date.MinValue Then
                createdAtStr = stamp.CreatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)
            End If
            If stamp.ExpiresAtUtc <> Date.MinValue Then
                expiresAtStr = stamp.ExpiresAtUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)
            End If
        End If

        If String.IsNullOrEmpty(createdAtStr) Then
            createdAtStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)
        End If

        Dim isPlaylist As Boolean = String.Equals(broadcast.Kind, "folder", StringComparison.OrdinalIgnoreCase) OrElse broadcast.ItemCount > 1
        Dim mediaKind As String = ClassifyMediaKind(broadcast.HostPath, broadcast.Kind)
        Dim bcastPath As String = If(broadcast.Token.StartsWith("/b/", StringComparison.Ordinal), broadcast.Token, "/b/" & broadcast.Token)
        Dim itemCount As Integer = Math.Max(1, broadcast.ItemCount)
        Dim note As String = ShareText.AccessNote(reach, port, includeExternal)

        Dim sb As New StringBuilder()
        sb.Append("{""schemaVersion"":1")
        sb.Append(",""kind"":""broadcast""")
        sb.Append(",""resourceName"":").Append(J(resourceName))
        sb.Append(",""protocol"":""http""")
        sb.Append(",""mediaKind"":").Append(J(mediaKind))
        sb.Append(",""playlist"":").Append(If(isPlaylist, "true", "false"))
        sb.Append(",""itemCount"":").Append(itemCount.ToString(CultureInfo.InvariantCulture))
        sb.Append(",""accessPaths"":[").Append(paths).Append("]"c)
        sb.Append(",""path"":").Append(J(bcastPath))
        sb.Append(",""durationSeconds"":").Append(broadcast.DurationSeconds.ToString(CultureInfo.InvariantCulture))
        sb.Append(",""expiresAt"":").Append(J(expiresAtStr))
        If note.Length > 0 Then sb.Append(",""accessNote"":").Append(J(note))
        sb.Append(",""createdAt"":").Append(J(createdAtStr))
        sb.Append("}"c)

        Dim json As String = sb.ToString()

        ' Determine best host for SPCH1 clipboard link
        Dim bestHost As String = lan
        Dim bestPort As Integer = port
        If includeExternal AndAlso hasExt AndAlso reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.ExternalHost) Then
            bestHost = reach.ExternalHost
            bestPort = extPort
        ElseIf includeExternal AndAlso hasIpv6 AndAlso reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.Ipv6Address) Then
            bestHost = "[" & reach.Ipv6Address & "]"
            bestPort = port
        End If

        Dim clipLink As String = "SPCH1 http://" & bestHost & ":" & bestPort.ToString(CultureInfo.InvariantCulture) & bcastPath

        Dim result As New BroadcastConfigResult With {
            .ConfigJson = json,
            .HasExternal = hasExt,
            .HasIpv6 = hasIpv6,
            .AccessNote = note,
            .LanDisplay = If(lan.Length > 0, lan & ":" & port.ToString(CultureInfo.InvariantCulture), ""),
            .ClipboardLink = clipLink
        }

        Dim payload As String = QrPayload(json)
        If Encoding.UTF8.GetByteCount(payload) > QrHardLimit Then
            result.QrOverflow = True
        Else
            Try
                result.QrPng = RenderQr(payload)
            Catch
                result.QrPng = Nothing
                result.QrOverflow = True
            End Try
        End If

        Return result
    End Function

    ''' <summary>
    ''' Classifies media kind as "audio" or "video".
    ''' </summary>
    Public Function ClassifyMediaKind(hostPath As String, kind As String) As String
        If String.Equals(kind, "file", StringComparison.OrdinalIgnoreCase) Then
            Dim ext As String = Path.GetExtension(hostPath)
            If Not String.IsNullOrEmpty(ext) AndAlso AudioExtensions.Contains(ext) Then
                Return "audio"
            End If
            Return "video"
        End If

        ' Folder: check if all recognizable media files inside are audio
        Try
            If Directory.Exists(hostPath) Then
                Dim files As String() = Directory.GetFiles(hostPath, "*.*", SearchOption.TopDirectoryOnly)
                Dim hasAudio As Boolean = False
                Dim hasVideo As Boolean = False
                For Each f As String In files
                    Dim ext As String = Path.GetExtension(f)
                    If String.IsNullOrEmpty(ext) Then Continue For
                    If AudioExtensions.Contains(ext) Then
                        hasAudio = True
                    Else
                        ' Assume other non-hidden files could be video or images
                        hasVideo = True
                    End If
                Next
                If hasAudio AndAlso Not hasVideo Then Return "audio"
            End If
        Catch
        End Try

        Return "video"
    End Function

    Private Function ResolveResourceName(broadcast As WorkerBroadcast) As String
        If Not String.IsNullOrWhiteSpace(broadcast.Title) Then
            Return broadcast.Title.Trim()
        End If
        If Not String.IsNullOrWhiteSpace(broadcast.HostPath) Then
            Try
                Dim name As String = Path.GetFileNameWithoutExtension(broadcast.HostPath)
                If Not String.IsNullOrEmpty(name) Then Return name
                Dim dirName As String = Path.GetFileName(broadcast.HostPath.TrimEnd("\"c, "/"c))
                If Not String.IsNullOrEmpty(dirName) Then Return dirName
            Catch
            End Try
        End If
        Return "Broadcast"
    End Function

    Private Sub AppendAccessPath(sb As StringBuilder, kind As String, host As String, port As Integer)
        If sb.Length > 0 Then sb.Append(","c)
        sb.Append("{""kind"":").Append(J(kind)).
           Append(",""host"":").Append(J(host)).
           Append(",""port"":").Append(port.ToString(CultureInfo.InvariantCulture)).
           Append("}"c)
    End Sub

    Private Function J(s As String) As String
        Return JsonSerializer.Serialize(If(s, ""), JsonStringOpts)
    End Function

    Private Function QrPayload(json As String) As String
        Dim raw As Byte() = Encoding.UTF8.GetBytes(json)
        If raw.Length <= QrComfortLimit Then Return json
        Using ms As New MemoryStream()
            Using gz As New GZipStream(ms, CompressionMode.Compress, leaveOpen:=True)
                gz.Write(raw, 0, raw.Length)
            End Using
            Return QrPrefix & Convert.ToBase64String(ms.ToArray())
        End Using
    End Function

    Private Function RenderQr(payload As String) As Byte()
        Using gen As New QRCoder.QRCodeGenerator()
            Using data As QRCoder.QRCodeData = gen.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M)
                Dim png As New QRCoder.PngByteQRCode(data)
                Return png.GetGraphic(10)
            End Using
        End Using
    End Function

End Module
