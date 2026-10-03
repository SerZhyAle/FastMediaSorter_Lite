#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Threading
Imports System.Threading.Tasks
Imports LibVLCSharp.Shared

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §1, §6, §7.
' Single HTTP request session feeding response stream directly into LibVLC via StreamMediaInput.

Friend NotInheritable Class LivePlaybackOptions
    Public Property NetworkCachingMs As Integer = 300
    Public Property LiveCachingMs As Integer = 300
    Public Property ConnectTimeoutSeconds As Integer = 2
    Public Property StallTimeoutSeconds As Integer = 3
    Public Property MaxDriftAllowanceMs As Long = 2000L

    Public Function ToMediaOptions(targetLatencyMs As Long?) As String()
        Dim cacheVal As Integer = NetworkCachingMs
        If targetLatencyMs.HasValue AndAlso targetLatencyMs.Value >= 250 AndAlso targetLatencyMs.Value <= 2000 Then
            cacheVal = CInt(targetLatencyMs.Value)
        End If

        Return New String() {
            $":network-caching={cacheVal}",
            $":live-caching={cacheVal}",
            ":clock-jitter=0",
            ":clock-synchro=0"
        }
    End Function
End Class

Friend NotInheritable Class LiveHttpPlaybackSession
    Implements IDisposable

    Private ReadOnly endpoint As LiveBroadcastEndpoint
    Private ReadOnly libVlc As LibVLC
    Private ReadOnly options As LivePlaybackOptions

    Private httpClient As HttpClient
    Private httpResponse As HttpResponseMessage
    Private responseStream As Stream
    Private progressStream As ProgressReadStream
    Private streamMediaInput As StreamMediaInput
    Private media As Media

    Private isDisposed As Integer = 0
    Private watchdogTimer As System.Threading.Timer
    Private ReadOnly connectStopwatch As New Stopwatch()
    Private hasReceivedFirstByte As Boolean = False
    Private isStalled As Boolean = False

    Public Property TerminationReason As LiveTerminationKind = LiveTerminationKind.None
    Public Property CustomErrorMessage As String = Nothing

    Public Event Stalled As Action
    Public Event StreamEnded As Action

    Public Sub New(endpoint As LiveBroadcastEndpoint, libVlc As LibVLC, Optional options As LivePlaybackOptions = Nothing)
        If endpoint Is Nothing Then Throw New ArgumentNullException(NameOf(endpoint))
        If libVlc Is Nothing Then Throw New ArgumentNullException(NameOf(libVlc))

        Me.endpoint = endpoint
        Me.libVlc = libVlc
        Me.options = If(options, New LivePlaybackOptions())
    End Sub

    Public ReadOnly Property MediaInstance As Media
        Get
            Return media
        End Get
    End Property

    Public ReadOnly Property Progress As ProgressReadStream
        Get
            Return progressStream
        End Get
    End Property

    ''' <summary>
    ''' Opens the single HTTP GET connection and returns the LibVLC Media object wrapping the response stream.
    ''' </summary>
    Public Async Function OpenAsync(cancellationToken As CancellationToken) As Task(Of Media)
        If Interlocked.CompareExchange(isDisposed, 0, 0) <> 0 Then Return Nothing

        Dim handler As New HttpClientHandler With {
            .AllowAutoRedirect = False,
            .UseCookies = False
        }

        httpClient = New HttpClient(handler) With {
            .Timeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds)
        }

        Dim request As New HttpRequestMessage(HttpMethod.Get, endpoint.Url)
        request.Headers.Accept.Add(New MediaTypeWithQualityHeaderValue("audio/aac"))
        request.Headers.Accept.Add(New MediaTypeWithQualityHeaderValue("*/*"))

        connectStopwatch.Restart()

        Try
            ' Send request with ResponseHeadersRead to avoid buffering body
            httpResponse = Await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(False)

            Dim statusCode As HttpStatusCode = httpResponse.StatusCode
            If statusCode = HttpStatusCode.ServiceUnavailable Then
                TerminationReason = LiveTerminationKind.MaxListeners503
                Return Nothing
            ElseIf statusCode = HttpStatusCode.Unauthorized OrElse statusCode = HttpStatusCode.Forbidden Then
                TerminationReason = LiveTerminationKind.AccessDenied
                Return Nothing
            ElseIf statusCode = HttpStatusCode.NotFound OrElse statusCode = HttpStatusCode.Gone Then
                TerminationReason = LiveTerminationKind.NotFoundOrGone
                Return Nothing
            ElseIf Not httpResponse.IsSuccessStatusCode Then
                TerminationReason = LiveTerminationKind.UnknownError
                CustomErrorMessage = $"HTTP {CInt(statusCode)} {httpResponse.ReasonPhrase}"
                Return Nothing
            End If

            ' Validate Content-Type (§6.2 item 2)
            Dim contentType As String = If(httpResponse.Content.Headers.ContentType?.MediaType, "")
            If Not IsAudioContentType(contentType) Then
                TerminationReason = LiveTerminationKind.NotAudioStream
                Return Nothing
            End If

            responseStream = Await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(False)
            progressStream = New ProgressReadStream(responseStream)
            AddHandler progressStream.BytesRead, AddressOf OnBytesRead

            streamMediaInput = New StreamMediaInput(progressStream)

            Dim mediaOptions As String() = options.ToMediaOptions(endpoint.TargetLatencyMs)
            media = New Media(libVlc, streamMediaInput, mediaOptions)

            ' Start watchdog timer for stalls (§6.2 item 5)
            watchdogTimer = New System.Threading.Timer(AddressOf WatchdogCheck, Nothing, 1000, 1000)

            Return media
        Catch ex As OperationCanceledException
            TerminationReason = LiveTerminationKind.UserStopped
            Return Nothing
        Catch ex As HttpRequestException
            AppFileLogger.LogException("Live HTTP connection failed", ex)
            TerminationReason = LiveTerminationKind.UnknownError
            Return Nothing
        Catch ex As Exception
            AppFileLogger.LogException("Live HTTP session startup failed", ex)
            TerminationReason = LiveTerminationKind.UnknownError
            Return Nothing
        End Try
    End Function

    Private Sub OnBytesRead(count As Integer)
        If count > 0 Then
            hasReceivedFirstByte = True
        End If
    End Sub

    ''' <summary>
    ''' Watchdog timer that monitors first-byte arrival and subsequent stall timeouts.
    ''' </summary>
    Private Sub WatchdogCheck(state As Object)
        If Interlocked.CompareExchange(isDisposed, 0, 0) <> 0 OrElse isStalled Then Return

        Dim nowUtc As DateTime = DateTime.UtcNow

        ' If first byte did not arrive within stall timeout
        If Not hasReceivedFirstByte Then
            If connectStopwatch.ElapsedMilliseconds >= options.StallTimeoutSeconds * 1000L Then
                TriggerStall()
            End If
            Return
        End If

        ' If bytes stopped arriving for >= StallTimeoutSeconds
        If progressStream IsNot Nothing Then
            Dim idleTime As TimeSpan = nowUtc - progressStream.LastReadTimestampUtc
            If idleTime.TotalSeconds >= options.StallTimeoutSeconds Then
                TriggerStall()
            End If
        End If
    End Sub

    Private Sub TriggerStall()
        If isStalled Then Return
        isStalled = True

        ' Close the response stream to immediately unblock any native blocking read
        Try
            If progressStream IsNot Nothing Then progressStream.Dispose()
        Catch
        End Try

        RaiseEvent Stalled()
    End Sub

    Private Shared Function IsAudioContentType(contentType As String) As Boolean
        If String.IsNullOrWhiteSpace(contentType) Then Return True ' tolerant if producer omitted header
        Dim ct As String = contentType.Trim().ToLowerInvariant()
        Return ct.StartsWith("audio/", StringComparison.Ordinal) OrElse
               ct.Contains("aac") OrElse ct.Contains("mpeg") OrElse ct.Contains("octet-stream")
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(isDisposed, 1) = 0 Then
            Try
                If watchdogTimer IsNot Nothing Then
                    watchdogTimer.Dispose()
                    watchdogTimer = Nothing
                End If
            Catch
            End Try

            Try
                If progressStream IsNot Nothing Then
                    progressStream.Dispose()
                    progressStream = Nothing
                End If
            Catch
            End Try

            Try
                If streamMediaInput IsNot Nothing Then
                    streamMediaInput.Dispose()
                    streamMediaInput = Nothing
                End If
            Catch
            End Try

            Try
                If media IsNot Nothing Then
                    media.Dispose()
                    media = Nothing
                End If
            Catch
            End Try

            Try
                If httpResponse IsNot Nothing Then
                    httpResponse.Dispose()
                    httpResponse = Nothing
                End If
            Catch
            End Try

            Try
                If httpClient IsNot Nothing Then
                    httpClient.Dispose()
                    httpClient = Nothing
                End If
            Catch
            End Try
        End If
    End Sub

End Class
#End If
