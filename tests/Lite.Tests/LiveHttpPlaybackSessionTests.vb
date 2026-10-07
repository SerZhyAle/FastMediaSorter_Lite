#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 6.2, 6.4, 10.2: the single HTTP request of a
' live listener, against a mock device that speaks the wire shapes of LIVE-BROADCAST section 2. These prove
' the state machine and the number of connections; they say nothing about audio latency on a real output
' device (that is the hand-run measurement of section 11).

Public Class LiveHttpPlaybackSessionTests

    Private Shared Function Quick() As LivePlaybackOptions
        Return New LivePlaybackOptions With {.ConnectTimeoutMs = 500, .StallTimeoutMs = 400, .WatchdogPeriodMs = 50}
    End Function

    Private Shared Function EndpointOf(url As String) As LiveBroadcastEndpoint
        Return New LiveBroadcastEndpoint With {.Url = url, .Mode = "AUDIO_ONLY", .ModeValue = LiveBroadcastMode.AudioOnly, .Transport = "HTTP"}
    End Function

    <Fact>
    Public Async Function Phone_Chunked_Body_Is_One_Request_And_Frames_Are_Counted() As Task
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 20, 0, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)
                Assert.Equal(LiveOpenOutcome.Connected, result.Outcome)
                Assert.Equal(200, result.HttpStatus)

                Dim player As New LiveMockPlayer(session.Stream)
                Assert.Equal(LiveStreamEnd.Eof, Await session.Ended.WaitAsync(TimeSpan.FromSeconds(5)))
                Await player.Completion.WaitAsync(TimeSpan.FromSeconds(5))

                Assert.Equal(20L, session.Frames.FrameCount)
                ' 20 frames of 1024 samples at 44.1 kHz.
                Assert.InRange(session.Frames.DurationMs, 464.0, 465.0)
                Assert.Equal(1, session.RequestCount)
                Assert.Equal(1, device.AcceptedCount)
                Assert.Equal(1, device.MaxConcurrent)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Watch_Http10_Raw_Body_Until_Close_Plays_And_Ends_With_Eof() As Task
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.WatchRaw(c, 30, 0, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url("/listen")), Quick())
                Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)
                Assert.Equal(LiveOpenOutcome.Connected, result.Outcome)

                Dim player As New LiveMockPlayer(session.Stream)
                Assert.Equal(LiveStreamEnd.Eof, Await session.Ended.WaitAsync(TimeSpan.FromSeconds(5)))
                Await player.Completion.WaitAsync(TimeSpan.FromSeconds(5))

                Assert.Equal(30L, session.Frames.FrameCount)
                Assert.Equal(1, device.AcceptedCount)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Request_Carries_No_Icy_Metadata_Request_And_Names_The_Path() As Task
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 1, 0, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url("/live-audio.aac")), Quick())
                Await session.OpenAsync(CancellationToken.None)
                Dim head As String = device.Requests.First()
                Assert.StartsWith("GET /live-audio.aac HTTP/1.1", head, StringComparison.Ordinal)
                ' "No ICY metadata request is ever sent" (section 6.4) - and no Range, so no resume semantics either.
                Assert.DoesNotContain("icy-metadata", head, StringComparison.OrdinalIgnoreCase)
                Assert.DoesNotContain("Range:", head, StringComparison.OrdinalIgnoreCase)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Watch_503_Is_The_Maximum_Listeners_Outcome_With_Exactly_One_Request() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               LiveMockDevice.WatchBusy(c)
                                               Return Task.CompletedTask
                                           End Function)
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url("/listen")), Quick())
                Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)

                Assert.Equal(LiveOpenOutcome.MaxListeners503, result.Outcome)
                Assert.Equal(503, result.HttpStatus)
                Assert.True(result.IsFatal)
                Assert.False(result.IsRetryable)
                Assert.Equal(LiveTerminationKind.MaxListeners503, result.Termination)
                ' No probe, no health check: the one connection IS the 503.
                Assert.Equal(1, session.RequestCount)
                Assert.Equal(1, device.AcceptedCount)
                Assert.Single(device.Requests)
            End Using
        End Using
    End Function

    <Theory>
    <InlineData(200, "Connected")>
    <InlineData(204, "Connected")>
    <InlineData(206, "Connected")>
    <InlineData(503, "MaxListeners503")>
    <InlineData(401, "AccessDenied")>
    <InlineData(403, "AccessDenied")>
    <InlineData(404, "NotFoundOrGone")>
    <InlineData(410, "NotFoundOrGone")>
    <InlineData(408, "HttpRetryable")>
    <InlineData(429, "HttpRetryable")>
    <InlineData(500, "HttpRetryable")>
    <InlineData(502, "HttpRetryable")>
    <InlineData(504, "HttpRetryable")>
    <InlineData(301, "HttpRefused")>
    <InlineData(302, "HttpRefused")>
    <InlineData(307, "HttpRefused")>
    <InlineData(400, "HttpRefused")>
    <InlineData(418, "HttpRefused")>
    Public Sub Status_Table_Of_Section_6_4(status As Integer, expected As String)
        Assert.Equal([Enum].Parse(GetType(LiveOpenOutcome), expected), LiveHttpPlaybackSession.ClassifyStatus(status))
    End Sub

    <Fact>
    Public Async Function Status_Errors_Reach_The_Caller_With_Their_Status_And_One_Request_Each() As Task
        For Each code As Integer In New Integer() {401, 404, 418, 502}
            Dim answer As Integer = code
            Using device As New LiveMockDevice(Function(c, t)
                                                   LiveMockDevice.Status(c, answer, "x")
                                                   Return Task.CompletedTask
                                               End Function)
                Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                    Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)
                    Assert.Equal(answer, result.HttpStatus)
                    Assert.Equal(LiveHttpPlaybackSession.ClassifyStatus(answer), result.Outcome)
                    Assert.Equal(1, device.AcceptedCount)
                End Using
            End Using
        Next
    End Function

    <Fact>
    Public Async Function A_Redirect_Is_Never_Followed_So_No_Second_Address_Is_Opened() As Task
        Using elsewhere As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 5, 0, t))
            Using device As New LiveMockDevice(Function(c, t)
                                                   LiveMockDevice.Status(c, 302, "Found", "Location: " & elsewhere.Url() & vbCrLf)
                                                   Return Task.CompletedTask
                                               End Function)
                Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                    Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)

                    Assert.Equal(LiveOpenOutcome.HttpRefused, result.Outcome)
                    Assert.Equal(302, result.HttpStatus)
                    Assert.True(result.IsFatal)
                    Assert.Equal(1, device.AcceptedCount)
                    Assert.Equal(0, elsewhere.AcceptedCount)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_Page_That_Is_Not_Audio_Is_Refused_By_Name() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               c.WriteAscii("HTTP/1.1 200 OK" & vbCrLf & "Content-Type: text/html; charset=utf-8" & vbCrLf &
                                                            "Content-Length: 12" & vbCrLf & vbCrLf & "<html></html>")
                                               Return Task.CompletedTask
                                           End Function)
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)
                Assert.Equal(LiveOpenOutcome.NotAudioStream, result.Outcome)
                Assert.Equal(LiveTerminationKind.NotAudioStream, result.Termination)
                Assert.Equal(1, device.AcceptedCount)
            End Using
        End Using
    End Function

    <Theory>
    <InlineData("audio/aac", True)>
    <InlineData("Audio/AAC", True)>
    <InlineData("audio/aac; charset=binary", True)>
    <InlineData("AUDIO/MPEG ; q=1", True)>
    <InlineData("audio/aacp", True)>
    <InlineData("application/ogg", True)>
    <InlineData("application/octet-stream", True)>
    <InlineData("", True)>
    <InlineData("text/html", False)>
    <InlineData("application/json", False)>
    <InlineData("video/mp4", False)>
    <InlineData("image/png", False)>
    Public Sub Content_Type_Is_Compared_Without_Case_And_Ignoring_Parameters(mediaType As String, expected As Boolean)
        Assert.Equal(expected, LiveHttpPlaybackSession.IsAudioContentType(mediaType))
    End Sub

    <Theory>
    <InlineData("audio/aac", True)>
    <InlineData("Audio/AAC; charset=binary", True)>
    <InlineData("audio/aacp", True)>
    <InlineData("audio/x-aac", True)>
    <InlineData("audio/mpeg", False)>
    <InlineData("application/ogg", False)>
    <InlineData("application/octet-stream", False)>
    <InlineData("", False)>
    Public Sub Only_A_Declared_Aac_Body_Gets_A_Forced_Demuxer(mediaType As String, expected As Boolean)
        Assert.Equal(expected, LiveHttpPlaybackSession.IsAdtsAacContentType(mediaType))
    End Sub

    <Fact>
    Public Async Function The_Declared_Content_Type_Is_Kept_For_The_Player_Options() As Task
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 3, 0, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Assert.False(session.IsAdtsAac)
                Await session.OpenAsync(CancellationToken.None)
                Assert.Equal("audio/aac", session.ContentType)
                Assert.True(session.IsAdtsAac)
            End Using
        End Using

        Using device As New LiveMockDevice(Function(c, t)
                                               c.WriteAscii("HTTP/1.0 200 OK" & vbCrLf & "Content-Type: audio/mpeg" & vbCrLf & vbCrLf)
                                               c.Write(New Byte() {1, 2, 3})
                                               Return Task.CompletedTask
                                           End Function)
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url("/radio")), Quick())
                Await session.OpenAsync(CancellationToken.None)
                Assert.Equal("audio/mpeg", session.ContentType)
                Assert.False(session.IsAdtsAac)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_Device_That_Never_Answers_Times_Out_Within_The_Deadline() As Task
        Using device As New LiveMockDevice(Function(c, t) Task.Delay(Timeout.Infinite, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Dim watch As Stopwatch = Stopwatch.StartNew()
                Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)
                watch.Stop()

                Assert.Equal(LiveOpenOutcome.TimedOut, result.Outcome)
                Assert.True(result.IsRetryable)
                Assert.InRange(watch.ElapsedMilliseconds, 400L, 3000L)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Cancelling_During_Open_Is_Silent_And_Not_A_Timeout() As Task
        Using device As New LiveMockDevice(Function(c, t) Task.Delay(Timeout.Infinite, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()),
                                                         New LivePlaybackOptions With {.ConnectTimeoutMs = 10000})
                Using cts As New CancellationTokenSource()
                    Dim opening As Task(Of LiveOpenResult) = session.OpenAsync(cts.Token)
                    Await Task.Delay(150)
                    cts.Cancel()
                    Dim result As LiveOpenResult = Await opening.WaitAsync(TimeSpan.FromSeconds(5))
                    Assert.Equal(LiveOpenOutcome.Cancelled, result.Outcome)
                    Assert.Equal(LiveTerminationKind.UserStopped, result.Termination)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_Refused_Connection_Is_A_Retryable_Network_Error() As Task
        ' A port that nobody listens on: bind, note the number, release it.
        Dim probe As New System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
        probe.Start()
        Dim port As Integer = CType(probe.LocalEndpoint, System.Net.IPEndPoint).Port
        probe.Stop()

        ' Windows takes about two seconds to refuse a loopback connection, so the deadline here is generous; what is
        ' asserted is that either way it is retryable and not an end of the broadcast.
        Using session As New LiveHttpPlaybackSession(EndpointOf("http://127.0.0.1:" & port.ToString(Globalization.CultureInfo.InvariantCulture) & "/live-audio"),
                                                     New LivePlaybackOptions With {.ConnectTimeoutMs = 6000})
            Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)
            Assert.True(result.Outcome = LiveOpenOutcome.NetworkError OrElse result.Outcome = LiveOpenOutcome.TimedOut, result.Outcome.ToString())
            Assert.True(result.IsRetryable)
            Assert.False(result.IsFatal)
        End Using
    End Function

    <Fact>
    Public Async Function A_Device_That_Hangs_Up_At_Once_Is_A_Fast_Retryable_Network_Error() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               c.Reset()
                                               Return Task.CompletedTask
                                           End Function)
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Dim result As LiveOpenResult = Await session.OpenAsync(CancellationToken.None)
                Assert.Equal(LiveOpenOutcome.NetworkError, result.Outcome)
                Assert.True(result.IsRetryable)
                ' SocketsHttpHandler would have retried this GET up to three more times on new connections; each
                ' would cost a listener slot on a watch. The session refuses every connect but the first.
                Await Task.Delay(300)
                Assert.Equal(1, device.AcceptedCount)
                Assert.Equal(1, session.ConnectionCount)
                Assert.Equal(1, session.RequestCount)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_Silent_Stream_Is_Closed_By_The_Watchdog_And_Frees_The_Blocked_Reader() As Task
        ' Headers and three frames, then nothing: the player's read blocks. The watchdog must close the response so
        ' the read returns (section 6.2 item 5) - and it must do so without opening anything.
        Using device As New LiveMockDevice(Async Function(c, t)
                                               Await LiveMockDevice.PhoneChunked(c, 3, 0, t, endCleanly:=False)
                                               Await Task.Delay(Timeout.Infinite, t)
                                           End Function)
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Await session.OpenAsync(CancellationToken.None)
                Dim player As New LiveMockPlayer(session.Stream)

                Dim ended As LiveStreamEnd = Await session.Ended.WaitAsync(TimeSpan.FromSeconds(5))
                Assert.Equal(LiveStreamEnd.Stalled, ended)
                Await player.Completion.WaitAsync(TimeSpan.FromSeconds(5))
                Assert.Equal(1, session.RequestCount)
                Assert.Equal(1, device.AcceptedCount)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_Consumer_That_Is_Not_Reading_Is_Not_A_Stalled_Stream() As Task
        ' The watchdog measures a READ that waits, not a player that has not started yet.
        Using device As New LiveMockDevice(Async Function(c, t)
                                               Await LiveMockDevice.PhoneChunked(c, 2, 0, t, endCleanly:=False)
                                               Await Task.Delay(Timeout.Infinite, t)
                                           End Function)
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Await session.OpenAsync(CancellationToken.None)
                Await Task.Delay(1200)
                Assert.False(session.Ended.IsCompleted)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Dispose_Releases_A_Blocked_Read_And_Reports_Closed() As Task
        Using device As New LiveMockDevice(Async Function(c, t)
                                               Await LiveMockDevice.PhoneChunked(c, 2, 0, t, endCleanly:=False)
                                               Await Task.Delay(Timeout.Infinite, t)
                                           End Function)
            Dim session As New LiveHttpPlaybackSession(EndpointOf(device.Url()),
                                                       New LivePlaybackOptions With {.StallTimeoutMs = 60000, .WatchdogPeriodMs = 1000})
            Await session.OpenAsync(CancellationToken.None)
            Dim player As New LiveMockPlayer(session.Stream)
            Await Task.Delay(200)
            Assert.False(player.Completion.IsCompleted)

            session.Dispose()

            Await player.Completion.WaitAsync(TimeSpan.FromSeconds(5))
            Assert.Equal(LiveStreamEnd.Closed, Await session.Ended.WaitAsync(TimeSpan.FromSeconds(5)))
            Assert.True(session.IsDisposed)
        End Using
    End Function

    <Fact>
    Public Async Function A_Reset_After_The_Start_Is_Reported_As_A_Reset_Not_A_Clean_End() As Task
        Using device As New LiveMockDevice(Function(c, t)
                                               c.WriteAscii("HTTP/1.0 200 OK" & vbCrLf & "Content-Type: audio/aac" & vbCrLf & vbCrLf)
                                               c.Write(LiveMockDevice.AdtsFrame())
                                               c.Stream.Flush()
                                               Thread.Sleep(100)
                                               c.Reset()
                                               Return Task.CompletedTask
                                           End Function)
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url("/listen")), Quick())
                Await session.OpenAsync(CancellationToken.None)
                Dim player As New LiveMockPlayer(session.Stream)
                Dim ended As LiveStreamEnd = Await session.Ended.WaitAsync(TimeSpan.FromSeconds(5))
                Assert.True(ended = LiveStreamEnd.Reset OrElse ended = LiveStreamEnd.Eof, "an abortive close is a reset (or, on a fast path, EOF) - never a stall or a close")
                Await player.Completion.WaitAsync(TimeSpan.FromSeconds(5))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_Session_Opens_One_Connection_Only() As Task
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 1, 0, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Await session.OpenAsync(CancellationToken.None)
                Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() session.OpenAsync(CancellationToken.None))
                Assert.Equal(1, session.RequestCount)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_First_Byte_And_The_Headers_Are_Timed_For_The_Latency_Report() As Task
        Using device As New LiveMockDevice(Function(c, t) LiveMockDevice.PhoneChunked(c, 5, 0, t))
            Using session As New LiveHttpPlaybackSession(EndpointOf(device.Url()), Quick())
                Await session.OpenAsync(CancellationToken.None)
                Assert.True(session.HeadersElapsedMs >= 0L)
                Dim player As New LiveMockPlayer(session.Stream)
                Await session.FirstByte.WaitAsync(TimeSpan.FromSeconds(5))
                Assert.True(session.FirstByteElapsedMs >= session.HeadersElapsedMs)
                Await player.Completion.WaitAsync(TimeSpan.FromSeconds(5))
            End Using
        End Using
    End Function

End Class
#End If
