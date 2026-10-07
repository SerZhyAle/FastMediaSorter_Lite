#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §3.1, §3.2, §13.
' Endpoint selector: chooses the first supported AUDIO_ONLY HTTP endpoint.

Friend Module LiveEndpointSelector

    ''' <summary>
    ''' Selects the best supported endpoint from the descriptor for playback.
    ''' Today: first AUDIO_ONLY endpoint with HTTP transport and http:// / https:// URL,
    ''' falling back to the top-level URL if compatible.
    ''' </summary>
    Public Function SelectEndpoint(descriptor As LiveBroadcastDescriptor) As LiveBroadcastEndpoint
        If descriptor Is Nothing Then Return Nothing

        ' Check endpoints list first
        If descriptor.Endpoints IsNot Nothing Then
            For Each ep As LiveBroadcastEndpoint In descriptor.Endpoints
                If IsSupportedHttpAudioEndpoint(ep) Then
                    Return ep
                End If
            Next
        End If

        ' Fallback to top-level URL if mode is AudioOnly and URL is http/https
        If descriptor.ModeValue = LiveBroadcastMode.AudioOnly AndAlso IsHttpUrl(descriptor.Url) Then
            Return New LiveBroadcastEndpoint With {
                .Url = descriptor.Url,
                .Mode = descriptor.Mode,
                .ModeValue = descriptor.ModeValue,
                .Transport = "HTTP",
                .IsLive = descriptor.IsLive,
                .TargetLatencyMs = descriptor.TargetLatencyMs
            }
        End If

        Return Nothing
    End Function

    ''' <summary>
    ''' The latency target the descriptor asks for: the chosen endpoint's value, then the top-level one (section 6.3),
    ''' else Nothing (the effective default of 1000 ms is applied by <see cref="LivePlaybackOptions"/>).
    ''' </summary>
    Public Function TargetLatencyOf(descriptor As LiveBroadcastDescriptor, endpoint As LiveBroadcastEndpoint) As Long?
        If endpoint IsNot Nothing AndAlso endpoint.TargetLatencyMs.HasValue Then Return endpoint.TargetLatencyMs
        If descriptor IsNot Nothing Then Return descriptor.TargetLatencyMs
        Return Nothing
    End Function

    Private Function IsSupportedHttpAudioEndpoint(ep As LiveBroadcastEndpoint) As Boolean
        If ep Is Nothing OrElse String.IsNullOrWhiteSpace(ep.Url) Then Return False

        ' Mode must be AUDIO_ONLY
        If ep.ModeValue <> LiveBroadcastMode.AudioOnly AndAlso
           Not String.Equals(ep.Mode, "AUDIO_ONLY", StringComparison.OrdinalIgnoreCase) Then
            Return False
        End If

        ' Transport must be HTTP (or empty if URL scheme is http/https)
        If Not String.IsNullOrEmpty(ep.Transport) AndAlso
           Not String.Equals(ep.Transport, "HTTP", StringComparison.OrdinalIgnoreCase) Then
            Return False
        End If

        Return IsHttpUrl(ep.Url)
    End Function

    Private Function IsHttpUrl(url As String) As Boolean
        If String.IsNullOrWhiteSpace(url) Then Return False
        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return False
        Return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) OrElse
               uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
    End Function

End Module
#End If
