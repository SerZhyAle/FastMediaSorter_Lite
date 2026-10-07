#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Threading
Imports System.Threading.Tasks

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 1, 6.2, 6.4.
'
' ONE HTTP request per listener (a probe costs a listener slot: the watch has four). This class is the
' owner of that request and nothing else: it knows no LibVLC, so the whole transport - status handling,
' the single GET, the watchdog, closing - is tested against a mock device in Lite.Tests. The player side
' (StreamMediaInput + Media) is LiveVlcMedia, which wraps the stream this class exposes.

''' <summary>What the single GET of a session came to (section 6.4).</summary>
Friend Enum LiveOpenOutcome
    ''' <summary>2xx with an audio body: hand the stream to the player.</summary>
    Connected = 0
    ''' <summary>503: the watch is at its listener cap. Never retried.</summary>
    MaxListeners503 = 1
    ''' <summary>401 / 403. Never retried.</summary>
    AccessDenied = 2
    ''' <summary>404 / 410. Never retried.</summary>
    NotFoundOrGone = 3
    ''' <summary>A 2xx whose body is not audio (a login page, JSON). Never retried.</summary>
    NotAudioStream = 4
    ''' <summary>408, 429, 5xx except 503: worth another try inside the budget.</summary>
    HttpRetryable = 5
    ''' <summary>Any other non-2xx, a 3xx included (redirects are never followed). Never retried.</summary>
    HttpRefused = 6
    ''' <summary>No response: refused, reset, unreachable. Retried inside the budget.</summary>
    NetworkError = 7
    ''' <summary>No headers within the deadline. Retried inside the budget.</summary>
    TimedOut = 8
    ''' <summary>The caller cancelled (stop, another media, closing). Silent.</summary>
    Cancelled = 9
End Enum

Friend NotInheritable Class LiveOpenResult

    Public ReadOnly Property Outcome As LiveOpenOutcome
    Public ReadOnly Property HttpStatus As Integer

    Public Sub New(outcome As LiveOpenOutcome, Optional httpStatus As Integer = 0)
        Me.Outcome = outcome
        Me.HttpStatus = httpStatus
    End Sub

    Public ReadOnly Property IsConnected As Boolean
        Get
            Return Outcome = LiveOpenOutcome.Connected
        End Get
    End Property

    ''' <summary>Retrying cannot change this answer: the session ends with a message.</summary>
    Public ReadOnly Property IsFatal As Boolean
        Get
            Select Case Outcome
                Case LiveOpenOutcome.MaxListeners503, LiveOpenOutcome.AccessDenied, LiveOpenOutcome.NotFoundOrGone,
                     LiveOpenOutcome.NotAudioStream, LiveOpenOutcome.HttpRefused
                    Return True
                Case Else
                    Return False
            End Select
        End Get
    End Property

    ''' <summary>The same answer another try might change (inside the bounded budget).</summary>
    Public ReadOnly Property IsRetryable As Boolean
        Get
            Return Outcome = LiveOpenOutcome.HttpRetryable OrElse Outcome = LiveOpenOutcome.NetworkError OrElse
                   Outcome = LiveOpenOutcome.TimedOut
        End Get
    End Property

    ''' <summary>The reason to record when this result ends the session.</summary>
    Public ReadOnly Property Termination As LiveTerminationKind
        Get
            Select Case Outcome
                Case LiveOpenOutcome.MaxListeners503 : Return LiveTerminationKind.MaxListeners503
                Case LiveOpenOutcome.AccessDenied : Return LiveTerminationKind.AccessDenied
                Case LiveOpenOutcome.NotFoundOrGone : Return LiveTerminationKind.NotFoundOrGone
                Case LiveOpenOutcome.NotAudioStream : Return LiveTerminationKind.NotAudioStream
                Case LiveOpenOutcome.HttpRefused : Return LiveTerminationKind.HttpStatus
                Case LiveOpenOutcome.Cancelled : Return LiveTerminationKind.UserStopped
                Case Else : Return LiveTerminationKind.None
            End Select
        End Get
    End Property

End Class

Friend NotInheritable Class LiveHttpPlaybackSession
    Implements IDisposable

    Private ReadOnly endpoint As LiveBroadcastEndpoint
    Private ReadOnly options As LivePlaybackOptions
    Private ReadOnly endedSource As New TaskCompletionSource(Of LiveStreamEnd)(TaskCreationOptions.RunContinuationsAsynchronously)
    Private ReadOnly frameCounter As New AdtsFrameCounter()
    Private ReadOnly firstByteSource As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
    Private ReadOnly sinceRequest As New Stopwatch()
    Private headersMs As Long = -1L
    Private firstByteMs As Long = -1L

    Private httpClient As HttpClient
    Private httpResponse As HttpResponseMessage
    Private progressStream As ProgressReadStream
    Private watchdog As System.Threading.Timer

    Private disposedFlag As Integer
    Private openedFlag As Integer
    Private requestsSent As Integer
    Private connectionsMade As Integer
    Private contentTypeValue As String = ""

    Public Sub New(endpoint As LiveBroadcastEndpoint, Optional options As LivePlaybackOptions = Nothing)
        If endpoint Is Nothing Then Throw New ArgumentNullException(NameOf(endpoint))
        Me.endpoint = endpoint
        Me.options = If(options, New LivePlaybackOptions())
    End Sub

    ''' <summary>The byte stream of the body (valid once <see cref="OpenAsync"/> returned Connected).</summary>
    Public ReadOnly Property Stream As Stream
        Get
            Return progressStream
        End Get
    End Property

    ''' <summary>Counts the audio that has arrived, for the drift guard.</summary>
    Public ReadOnly Property Frames As AdtsFrameCounter
        Get
            Return frameCounter
        End Get
    End Property

    ''' <summary>Completes once, when the body stops delivering (EOF, reset, stall) or this side closes it.</summary>
    Public ReadOnly Property Ended As Task(Of LiveStreamEnd)
        Get
            Return endedSource.Task
        End Get
    End Property

    ''' <summary>Completes when the first bytes of the body have been read (the stream is alive, audio can start).</summary>
    Public ReadOnly Property FirstByte As Task
        Get
            Return firstByteSource.Task
        End Get
    End Property

    ''' <summary>Milliseconds from sending the request to the response headers (-1 until they came).</summary>
    Public ReadOnly Property HeadersElapsedMs As Long
        Get
            Return Interlocked.Read(headersMs)
        End Get
    End Property

    ''' <summary>Milliseconds from sending the request to the first body bytes (-1 until they came).</summary>
    Public ReadOnly Property FirstByteElapsedMs As Long
        Get
            Return Interlocked.Read(firstByteMs)
        End Get
    End Property

    ''' <summary>How many HTTP requests this session has put on the wire. Never more than one.</summary>
    Public ReadOnly Property RequestCount As Integer
        Get
            Return Volatile.Read(requestsSent)
        End Get
    End Property

    ''' <summary>How many TCP connections this session asked for. Never more than one: a second is refused.</summary>
    Public ReadOnly Property ConnectionCount As Integer
        Get
            Return Math.Min(1, Volatile.Read(connectionsMade))
        End Get
    End Property

    Public ReadOnly Property TotalBytesRead As Long
        Get
            Dim current As ProgressReadStream = progressStream
            Return If(current Is Nothing, 0L, current.TotalBytesRead)
        End Get
    End Property

    Public ReadOnly Property IsDisposed As Boolean
        Get
            Return Volatile.Read(disposedFlag) <> 0
        End Get
    End Property

    ''' <summary>
    ''' Opens the one GET. Redirects are never followed (a 3xx would quietly open a second address), the
    ''' system proxy is not used (the device is on the LAN), and no ICY metadata is asked for. May be called
    ''' once: a session is exactly one connection, a retry is a NEW session.
    ''' </summary>
    Public Async Function OpenAsync(cancellationToken As CancellationToken) As Task(Of LiveOpenResult)
        If Interlocked.Exchange(openedFlag, 1) <> 0 Then Throw New InvalidOperationException("A live session opens one connection.")
        If IsDisposed OrElse cancellationToken.IsCancellationRequested Then Return New LiveOpenResult(LiveOpenOutcome.Cancelled)

        ' The connection is made HERE, once. SocketsHttpHandler quietly retries a GET up to three times when a
        ' connection dies before the response, and each retry is another TCP connection - on the watch, another
        ' listener slot. Refusing every connect after the first turns "one request per listener" from a habit
        ' into something the code cannot get wrong.
        Dim handler As New SocketsHttpHandler With {
            .AllowAutoRedirect = False,
            .UseProxy = False,
            .UseCookies = False,
            .AutomaticDecompression = DecompressionMethods.None,
            .ConnectTimeout = TimeSpan.FromMilliseconds(options.ConnectTimeoutMs),
            .ConnectCallback = AddressOf ConnectOnce
        }
        httpClient = New HttpClient(handler, disposeHandler:=True) With {.Timeout = System.Threading.Timeout.InfiniteTimeSpan}

        Using request As New HttpRequestMessage(HttpMethod.Get, endpoint.Url)
            request.Headers.Accept.Add(New MediaTypeWithQualityHeaderValue("audio/aac"))
            request.Headers.Accept.Add(New MediaTypeWithQualityHeaderValue("audio/*", 0.8))
            request.Headers.UserAgent.Add(New ProductInfoHeaderValue("FastMediaSorter", "1"))

            Using deadline As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                deadline.CancelAfter(options.ConnectTimeoutMs)
                Try
                    Interlocked.Increment(requestsSent)
                    sinceRequest.Restart()
                    httpResponse = Await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(False)
                    Interlocked.Exchange(headersMs, sinceRequest.ElapsedMilliseconds)
                Catch ex As OperationCanceledException
                    Return New LiveOpenResult(If(cancellationToken.IsCancellationRequested OrElse IsDisposed,
                                                 LiveOpenOutcome.Cancelled, LiveOpenOutcome.TimedOut))
                Catch ex As ObjectDisposedException
                    Return New LiveOpenResult(LiveOpenOutcome.Cancelled)
                Catch ex As HttpRequestException
                    Return New LiveOpenResult(If(IsDisposed, LiveOpenOutcome.Cancelled, LiveOpenOutcome.NetworkError))
                Catch ex As Exception
                    AppFileLogger.LogException("Live HTTP connection failed", ex)
                    Return New LiveOpenResult(If(IsDisposed, LiveOpenOutcome.Cancelled, LiveOpenOutcome.NetworkError))
                End Try
            End Using
        End Using

        Dim status As Integer = CInt(httpResponse.StatusCode)
        Dim outcome As LiveOpenOutcome = ClassifyStatus(status)
        If outcome = LiveOpenOutcome.Connected Then
            contentTypeValue = If(httpResponse.Content.Headers.ContentType?.MediaType, "")
            If Not IsAudioContentType(contentTypeValue) Then outcome = LiveOpenOutcome.NotAudioStream
        End If

        If outcome <> LiveOpenOutcome.Connected Then
            ' Closing the response closes the socket: a refused listener must not keep a slot.
            Close(LiveStreamEnd.Closed)
            Return New LiveOpenResult(outcome, status)
        End If

        Try
            Dim body As Stream = Await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(False)
            progressStream = New ProgressReadStream(body)
        Catch ex As OperationCanceledException
            Close(LiveStreamEnd.Closed)
            Return New LiveOpenResult(LiveOpenOutcome.Cancelled)
        Catch ex As Exception
            Close(LiveStreamEnd.Closed)
            Return New LiveOpenResult(If(IsDisposed, LiveOpenOutcome.Cancelled, LiveOpenOutcome.NetworkError))
        End Try

        progressStream.Observer = AddressOf frameCounter.Feed
        AddHandler progressStream.StreamEnded, Sub(kind As LiveStreamEnd) endedSource.TrySetResult(kind)
        AddHandler progressStream.BytesRead,
            Sub(count As Integer)
                If Not firstByteSource.Task.IsCompleted Then
                    Interlocked.CompareExchange(firstByteMs, sinceRequest.ElapsedMilliseconds, -1L)
                    firstByteSource.TrySetResult(True)
                End If
            End Sub

        ' Disposed while the headers were arriving: nobody will ever read this stream.
        If IsDisposed Then
            Close(LiveStreamEnd.Closed)
            Return New LiveOpenResult(LiveOpenOutcome.Cancelled)
        End If

        watchdog = New System.Threading.Timer(AddressOf WatchdogTick, Nothing, options.WatchdogPeriodMs, options.WatchdogPeriodMs)
        Return New LiveOpenResult(LiveOpenOutcome.Connected, status)
    End Function

    Private Function ConnectOnce(context As SocketsHttpConnectionContext, token As CancellationToken) As ValueTask(Of Stream)
        Return New ValueTask(Of Stream)(ConnectOnceAsync(context, token))
    End Function

    Private Async Function ConnectOnceAsync(context As SocketsHttpConnectionContext, token As CancellationToken) As Task(Of Stream)
        If Interlocked.Increment(connectionsMade) > 1 Then
            Throw New IOException("A live session opens one connection; the handler's own retry is refused.")
        End If
        Dim socket As New Sockets.Socket(Sockets.SocketType.Stream, Sockets.ProtocolType.Tcp) With {.NoDelay = True}
        Try
            Await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(False)
            Return New Sockets.NetworkStream(socket, ownsSocket:=True)
        Catch
            socket.Dispose()
            Throw
        End Try
    End Function

    ''' <summary>The media type the device declared (empty when it declared none, or before the headers came).</summary>
    Public ReadOnly Property ContentType As String
        Get
            Return contentTypeValue
        End Get
    End Property

    ''' <summary>The body was declared ADTS AAC: the player is told its demuxer (see LivePlaybackOptions.ToMediaOptions).</summary>
    Public ReadOnly Property IsAdtsAac As Boolean
        Get
            Return IsAdtsAacContentType(contentTypeValue)
        End Get
    End Property

    ''' <summary>audio/aac, audio/aacp and audio/x-aac, case-insensitive. Anything else is left to LibVLC's own probe.</summary>
    Public Shared Function IsAdtsAacContentType(mediaType As String) As Boolean
        If String.IsNullOrWhiteSpace(mediaType) Then Return False
        Dim type As String = mediaType.Trim()
        Dim semicolon As Integer = type.IndexOf(";"c)
        If semicolon >= 0 Then type = type.Substring(0, semicolon).Trim()
        Return String.Equals(type, "audio/aac", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(type, "audio/aacp", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(type, "audio/x-aac", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>The answer of a status line to the question "is this a connection" (section 6.4).</summary>
    Public Shared Function ClassifyStatus(statusCode As Integer) As LiveOpenOutcome
        If statusCode >= 200 AndAlso statusCode <= 299 Then Return LiveOpenOutcome.Connected
        Select Case statusCode
            Case 503
                Return LiveOpenOutcome.MaxListeners503
            Case 401, 403
                Return LiveOpenOutcome.AccessDenied
            Case 404, 410
                Return LiveOpenOutcome.NotFoundOrGone
            Case 408, 429
                Return LiveOpenOutcome.HttpRetryable
            Case Else
                If statusCode >= 500 AndAlso statusCode <= 599 Then Return LiveOpenOutcome.HttpRetryable
                Return LiveOpenOutcome.HttpRefused
        End Select
    End Function

    ''' <summary>
    ''' Whether a Content-Type can carry what a broadcast sends. The media type is compared without case
    ''' and its parameters are ignored. A missing type is tolerated (a third-party producer may omit it);
    ''' text, JSON, HTML, images and video are not audio.
    ''' </summary>
    Public Shared Function IsAudioContentType(mediaType As String) As Boolean
        If String.IsNullOrWhiteSpace(mediaType) Then Return True
        Dim type As String = mediaType.Trim()
        Dim semicolon As Integer = type.IndexOf(";"c)
        If semicolon >= 0 Then type = type.Substring(0, semicolon).Trim()

        If type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) Then Return True
        Return String.Equals(type, "application/ogg", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(type, "application/octet-stream", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(type, "binary/octet-stream", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' The watchdog (section 6.2 item 5): a read that has waited longer than the stall timeout means the
    ''' device has gone quiet; closing the response is what releases the player's blocked native read.
    ''' It opens no network request of its own.
    ''' </summary>
    Private Sub WatchdogTick(state As Object)
        If IsDisposed Then Return
        Dim reader As ProgressReadStream = progressStream
        If reader Is Nothing Then Return
        Dim pendingSince As Long = reader.ReadPendingSinceTickMs
        If pendingSince < 0L Then Return
        If Environment.TickCount64 - Math.Max(pendingSince, reader.LastReadTickMs) < options.StallTimeoutMs Then Return
        Close(LiveStreamEnd.Stalled)
    End Sub

    ''' <summary>
    ''' Closes the connection NOW, from any thread, and records why. Idempotent; the first reason wins.
    ''' This is the call that frees the player's native read, so it comes before stopping the player.
    ''' </summary>
    Public Sub Close(reason As LiveStreamEnd)
        endedSource.TrySetResult(reason)
        Try
            Dim timer As System.Threading.Timer = Interlocked.Exchange(watchdog, Nothing)
            If timer IsNot Nothing Then timer.Dispose()
        Catch
        End Try
        Try
            Dim reader As ProgressReadStream = progressStream
            If reader IsNot Nothing Then reader.Dispose()
        Catch
        End Try
        Try
            Dim response As HttpResponseMessage = httpResponse
            If response IsNot Nothing Then response.Dispose()
        Catch
        End Try
        Try
            Dim client As HttpClient = httpClient
            If client IsNot Nothing Then client.Dispose()
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(disposedFlag, 1) = 0 Then Close(LiveStreamEnd.Closed)
    End Sub

End Class
#End If
