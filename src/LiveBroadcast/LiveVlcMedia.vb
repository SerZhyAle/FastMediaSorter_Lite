#If Not NETFRAMEWORK Then
Option Strict On

Imports LibVLCSharp.Shared

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md section 6.2 items 3-4.
' The LibVLC half of a live session: the response stream of LiveHttpPlaybackSession, wrapped as the
' player's media input, with the per-media cache options of this session only.

''' <summary>
''' A <see cref="Media"/> fed from one live HTTP body. It owns the <see cref="StreamMediaInput"/> and the
''' <see cref="Media"/>; the HTTP session (and with it the stream) is owned by whoever opened it. Dispose
''' this AFTER the session is closed and the player stopped - closing the stream is what releases a
''' native read blocked inside LibVLC.
''' </summary>
Friend NotInheritable Class LiveVlcMedia
    Implements IDisposable

    Private input As StreamMediaInput
    Private mediaValue As Media

    Private Sub New(input As StreamMediaInput, media As Media)
        Me.input = input
        Me.mediaValue = media
    End Sub

    Public ReadOnly Property Media As Media
        Get
            Return mediaValue
        End Get
    End Property

    ''' <summary>
    ''' Wraps the session's stream. Seek, resume and repeat options are never added; the cache options
    ''' come from <paramref name="options"/> and the endpoint's effective latency target.
    ''' </summary>
    Public Shared Function Create(libVlc As LibVLC, session As LiveHttpPlaybackSession, options As LivePlaybackOptions,
                                  targetLatencyMs As Long?) As LiveVlcMedia
        If libVlc Is Nothing Then Throw New ArgumentNullException(NameOf(libVlc))
        If session Is Nothing OrElse session.Stream Is Nothing Then Throw New ArgumentException("The session has no open stream.", NameOf(session))

        Dim streamInput As New StreamMediaInput(session.Stream)
        Try
            Dim created As New Media(libVlc, streamInput, options.ToMediaOptions(targetLatencyMs, session.IsAdtsAac))
            Return New LiveVlcMedia(streamInput, created)
        Catch
            streamInput.Dispose()
            Throw
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim m As Media = Threading.Interlocked.Exchange(mediaValue, Nothing)
        Dim i As StreamMediaInput = Threading.Interlocked.Exchange(input, Nothing)
        Try
            If m IsNot Nothing Then m.Dispose()
        Catch
        End Try
        Try
            If i IsNot Nothing Then i.Dispose()
        Catch
        End Try
    End Sub

End Class
#End If
