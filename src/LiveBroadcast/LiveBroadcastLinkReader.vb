#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §4.3.
' Link reader for Android intent:// links, fmsbcast:// URLs and bare HTTP stream auto-detection.

Friend Module LiveBroadcastLinkReader

    Private ReadOnly KnownLiveAudioPaths As New HashSet(Of String)(StringComparer.Ordinal) From {
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
        ' Only the URI proper is read. An Android intent link carries its own parameters after '#Intent;' (the package
        ' hint, the browser fallback page): they are never opened and never executed (section 4.3), so they are never
        ' even looked at for a payload.
        Dim hash As Integer = linkText.IndexOf("#"c)
        Dim uriPart As String = If(hash >= 0, linkText.Substring(0, hash), linkText)
        Dim intentPart As String = If(hash >= 0, linkText.Substring(hash), "")

        ' A link that is not ours is not our business (an intent for another scheme, say).
        Dim isOurs As Boolean = uriPart.StartsWith("fmsbcast:", StringComparison.OrdinalIgnoreCase) OrElse
                                intentPart.IndexOf("scheme=fmsbcast", StringComparison.OrdinalIgnoreCase) >= 0
        If Not isOurs Then Return LiveBroadcastReadResult.Fail(LiveReadStatus.NotBroadcast)

        Dim query As Integer = uriPart.IndexOf("?"c)
        If query < 0 Then Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)

        Dim payloadRaw As String = Nothing
        Dim count As Integer = 0
        For Each pair As String In uriPart.Substring(query + 1).Split("&"c)
            Dim equals As Integer = pair.IndexOf("="c)
            Dim name As String = If(equals >= 0, pair.Substring(0, equals), pair)
            If Not String.Equals(name, "payload", StringComparison.Ordinal) Then Continue For
            count += 1
            payloadRaw = If(equals >= 0, pair.Substring(equals + 1), "")
        Next

        ' Missing, or given twice: both refused (section 4.3).
        If count <> 1 Then Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)

        Dim payload As String = StrictPercentDecode(payloadRaw)
        If payload Is Nothing Then Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)

        ' Decoded exactly once, and it must be an FMSBCAST1 payload: not JSON, not a second link.
        payload = payload.Trim()
        If Not payload.StartsWith(LiveBroadcastReader.BarcodePrefix, StringComparison.OrdinalIgnoreCase) Then
            Return LiveBroadcastReadResult.Fail(LiveReadStatus.InvalidDocument)
        End If

        Return LiveBroadcastReader.ReadText(payload)
    End Function

    ''' <summary>
    ''' Percent-decodes ONCE and refuses what a lenient decoder would let through: a '%' that is not followed by two
    ''' hex digits. A '+' stays a plus (the payload is Base64; this is not form encoding). Returns Nothing when malformed.
    ''' </summary>
    Public Function StrictPercentDecode(raw As String) As String
        If raw Is Nothing Then Return Nothing
        For i As Integer = 0 To raw.Length - 1
            If raw(i) <> "%"c Then Continue For
            If i + 2 >= raw.Length OrElse Not Uri.IsHexDigit(raw(i + 1)) OrElse Not Uri.IsHexDigit(raw(i + 2)) Then Return Nothing
            i += 2
        Next
        Try
            Return Uri.UnescapeDataString(raw)
        Catch
            Return Nothing
        End Try
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
