#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §3.1, §3.2, §13.
' Ticket 201: supported HTTP media, relay and broadcast-tunnel endpoints in producer order.

Friend Module LiveEndpointSelector

    ''' <summary>
    ''' Selects the best supported endpoint from the descriptor for playback.
    ''' Audio and MPEG-TS video use the same streaming HTTP input. Sealed endpoints await ticket 234.
    ''' </summary>
    Public Function SelectEndpoint(descriptor As LiveBroadcastDescriptor) As LiveBroadcastEndpoint
        Dim available As List(Of LiveBroadcastEndpoint) = Candidates(descriptor)
        Return If(available.Count = 0, Nothing, available(0))
    End Function

    ' Preserve the producer's order. The runner starts this list again after every lost connection.
    Public Function Candidates(descriptor As LiveBroadcastDescriptor) As List(Of LiveBroadcastEndpoint)
        Dim result As New List(Of LiveBroadcastEndpoint)()
        If descriptor Is Nothing Then Return result

        ' Check endpoints list first
        If descriptor.Endpoints IsNot Nothing Then
            For Each ep As LiveBroadcastEndpoint In descriptor.Endpoints
                If IsSupportedEndpoint(ep) Then
                    ep.AuthUser = descriptor.AuthUser
                    ep.Password = descriptor.Password
                    result.Add(ep)
                End If
            Next
        End If

        ' Old descriptors without an endpoint list retain their top-level address.
        If descriptor.Endpoints.Count = 0 AndAlso descriptor.ModeValue <> LiveBroadcastMode.Unknown AndAlso IsHttpUrl(descriptor.Url) Then
            result.Add(New LiveBroadcastEndpoint With {
                .Url = descriptor.Url,
                .Mode = descriptor.Mode,
                .ModeValue = descriptor.ModeValue,
                .Transport = "HTTP",
                .AuthUser = descriptor.AuthUser,
                .Password = descriptor.Password,
                .IsLive = descriptor.IsLive,
                .TargetLatencyMs = descriptor.TargetLatencyMs
            })
        End If

        Return result
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

    Private Function IsSupportedEndpoint(ep As LiveBroadcastEndpoint) As Boolean
        If ep Is Nothing OrElse String.IsNullOrWhiteSpace(ep.Url) Then Return False

        If ep.IsSealed OrElse (ep.ModeValue = LiveBroadcastMode.Unknown AndAlso LiveBroadcastReader.ParseMode(ep.Mode) = LiveBroadcastMode.Unknown) Then
            Return False
        End If

        ' Transport must be HTTP (or empty if URL scheme is http/https)
        If Not String.IsNullOrEmpty(ep.Transport) AndAlso
           Not String.Equals(ep.Transport, "HTTP", StringComparison.OrdinalIgnoreCase) AndAlso
           Not String.Equals(ep.Transport, "RELAY", StringComparison.OrdinalIgnoreCase) AndAlso
           Not String.Equals(ep.Transport, "TUNNEL", StringComparison.OrdinalIgnoreCase) Then
            Return False
        End If

        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(ep.Url, UriKind.Absolute, uri) OrElse LiveBroadcastReader.IsLoopbackOrUnspecifiedHost(uri.Host) Then Return False
        If String.Equals(ep.Transport, "TUNNEL", StringComparison.OrdinalIgnoreCase) Then
            Dim inner As Uri = Nothing
            Dim segments As String() = uri.AbsolutePath.Trim("/"c).Split("/"c)
            Return uri.Scheme = "fmsx" AndAlso segments.Length = 3 AndAlso segments(0) = "b" AndAlso segments(1).Length > 0 AndAlso segments(2) = "http" AndAlso
                Uri.TryCreate(ep.Inner, UriKind.Absolute, inner) AndAlso inner.Scheme = "http" AndAlso inner.UserInfo.Length = 0
        End If
        If String.Equals(ep.Transport, "RELAY", StringComparison.OrdinalIgnoreCase) AndAlso uri.Scheme <> "https" Then Return False
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
