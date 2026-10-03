#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §4.3.
' Link reader for Android intent:// links, fmsbcast:// URLs and bare HTTP stream auto-detection.

Friend Module LiveBroadcastLinkReader

    Private ReadOnly KnownLiveAudioPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "/live-audio",
        "/live-audio.aac",
        "/listen"
    }

    ''' <summary>
    ''' Attempts to parse a link (intent://, fmsbcast://, or auto-detected bare HTTP live audio stream).
    ''' </summary>
    Public Function ReadLink(linkText As String) As LiveBroadcastReadResult
        If String.IsNullOrWhiteSpace(linkText) Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
        End If

        Dim trimmed As String = linkText.Trim()

        ' 1. Intent URL or fmsbcast:// URL
        If trimmed.StartsWith("intent://", StringComparison.OrdinalIgnoreCase) OrElse
           trimmed.StartsWith("intent:", StringComparison.OrdinalIgnoreCase) OrElse
           trimmed.StartsWith("fmsbcast://", StringComparison.OrdinalIgnoreCase) Then
            Return ReadIntentOrCustomScheme(trimmed)
        End If

        ' 2. Bare HTTP stream auto-detection
        If trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
           trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then

            Dim autoResult As LiveBroadcastReadResult = TryAutoDetectBareHttpUrl(trimmed)
            If autoResult IsNot Nothing Then Return autoResult
        End If

        Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
    End Function

    ''' <summary>
    ''' Extracts and decodes the FMSBCAST1 payload from an intent:// or fmsbcast:// link.
    ''' Payload is URL-decoded exactly once.
    ''' </summary>
    Private Function ReadIntentOrCustomScheme(linkText As String) As LiveBroadcastReadResult
        Dim payloadParam As String = Nothing
        Dim count As Integer = 0

        ' Search for payload= in query or parameters
        Dim searchIndex As Integer = 0
        While True
            Dim idx As Integer = linkText.IndexOf("payload=", searchIndex, StringComparison.OrdinalIgnoreCase)
            If idx < 0 Then Exit While

            ' Ensure it is preceded by ?, &, ;, or start of string / host
            If idx = 0 OrElse linkText(idx - 1) = "?"c OrElse linkText(idx - 1) = "&"c OrElse linkText(idx - 1) = ";"c Then
                Dim valStart As Integer = idx + "payload=".Length
                Dim valEnd As Integer = linkText.IndexOfAny(New Char() {"&"c, ";"c, "#"c}, valStart)
                Dim rawVal As String = If(valEnd >= 0, linkText.Substring(valStart, valEnd - valStart), linkText.Substring(valStart))
                payloadParam = rawVal
                count += 1
            End If
            searchIndex = idx + 8
        End While

        If count = 0 Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)
        End If

        If count > 1 Then
            ' Duplicate payload parameter is an invalid document (§4.3)
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End If

        Dim decodedPayload As String
        Try
            decodedPayload = Uri.UnescapeDataString(payloadParam)
        Catch
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End Try

        If String.IsNullOrWhiteSpace(decodedPayload) Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End If

        Dim trimmedPayload As String = decodedPayload.Trim()
        If Not trimmedPayload.StartsWith(LiveBroadcastReader.BarcodePrefix, StringComparison.OrdinalIgnoreCase) AndAlso
           Not trimmedPayload.StartsWith("{", StringComparison.Ordinal) Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End If

        Return LiveBroadcastReader.ReadText(trimmedPayload)
    End Function

    ''' <summary>
    ''' Auto-detects literal LAN IPv4 URLs with path /live-audio, /live-audio.aac, or /listen (§4.3, repo item 5).
    ''' Any non-loopback, non-unspecified IPv4 literal (Wi-Fi, hotspot, Ethernet, VPN, carrier-grade) matches.
    ''' </summary>
    Public Function TryAutoDetectBareHttpUrl(url As String) As LiveBroadcastReadResult
        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return Nothing
        If Not (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) OrElse uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) Then
            Return Nothing
        End If

        ' Host must be a literal IPv4 address, and not loopback/unspecified
        Dim ip As IPAddress = Nothing
        If Not IPAddress.TryParse(uri.Host, ip) Then Return Nothing
        If ip.AddressFamily <> Sockets.AddressFamily.InterNetwork Then Return Nothing
        If LiveBroadcastReader.IsLoopbackOrUnspecifiedHost(uri.Host) Then Return Nothing

        Dim path As String = uri.AbsolutePath
        If Not KnownLiveAudioPaths.Contains(path) Then Return Nothing

        Dim descriptor As New LiveBroadcastDescriptor With {
            .SchemaVersion = 1,
            .Url = url,
            .Mode = "AUDIO_ONLY",
            .ModeValue = LiveBroadcastMode.AudioOnly,
            .Title = uri.Host,
            .IsLive = True
        }

        descriptor.Endpoints.Add(New LiveBroadcastEndpoint With {
            .Url = url,
            .Mode = "AUDIO_ONLY",
            .ModeValue = LiveBroadcastMode.AudioOnly,
            .Transport = "HTTP",
            .IsLive = True
        })

        Return LiveBroadcastReadResult.Ok(descriptor)
    End Function

    ''' <summary>
    ''' Explicitly creates a live descriptor for any remote HTTP URL when the user checks "This is a live broadcast" (§4.3).
    ''' </summary>
    Public Function CreateExplicitLiveDescriptor(url As String, Optional title As String = Nothing) As LiveBroadcastDescriptor
        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return Nothing

        Dim descriptor As New LiveBroadcastDescriptor With {
            .SchemaVersion = 1,
            .Url = url,
            .Mode = "AUDIO_ONLY",
            .ModeValue = LiveBroadcastMode.AudioOnly,
            .Title = If(String.IsNullOrWhiteSpace(title), uri.Host, title.Trim()),
            .IsLive = True
        }

        descriptor.Endpoints.Add(New LiveBroadcastEndpoint With {
            .Url = url,
            .Mode = "AUDIO_ONLY",
            .ModeValue = LiveBroadcastMode.AudioOnly,
            .Transport = "HTTP",
            .IsLive = True
        })

        Return descriptor
    End Function

End Module
#End If
