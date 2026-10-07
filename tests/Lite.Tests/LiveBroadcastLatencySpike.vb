#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports LibVLCSharp.Shared
Imports Xunit
Imports Xunit.Abstractions

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md phase Phi-L1 and section 11: the measurement the unit tests cannot
' make. The REAL LibVLC plays a REAL AAC stream (ffmpeg-made ADTS, paced at the producer's real-time rate by the mock
' device) through the very classes the viewer uses - LiveHttpPlaybackSession, LiveVlcMedia, LiveBroadcastRunner - and the
' delay between the producer's clock and the player's position is sampled. "Before" is LibVLC given the bare URL, the
' way the viewer's ordinary "Open URL.." does; "after" is the live path.
'
' HAND-RUN, never part of a default run (it opens the Windows audio device, silently, and takes minutes):
'
'     $env:FMS_LIVE_LATENCY = '1'
'     dotnet test tests\Lite.Tests -f net10.0-windows --filter "FullyQualifiedName~LiveBroadcastLatencySpike" -v n
'
' Optional: FMS_LIBVLC_DIR (a folder holding libvlc.dll; default the built viewer's bin\Release\libvlc\win-x64),
' FMS_FFMPEG (default: PATH, then the winget link), FMS_LIVE_SECONDS (length of a scenario, default 30),
' FMS_LIVE_CACHING (list of caching values to compare, default "300"). The report is written to
' %TEMP%\fms-live-latency\ and echoed to the test output.
'
' What it does NOT measure: the Windows audio output device's own latency (tens of ms in shared mode) and the
' producer's capture and encode time - the figures are "source clock to the player's position", on one PC over loopback.
' The phone and watch numbers of section 11 still need the real devices.

Public Class LiveBroadcastLatencySpike

    Private Const GateName As String = "FMS_LIVE_LATENCY"
    Private Const FrameMs As Double = 1024.0R * 1000.0R / 44100.0R

    Private ReadOnly output As ITestOutputHelper
    Private ReadOnly report As New StringBuilder()

    Private Shared libVlcInstance As LibVLC
    Private Shared ReadOnly frameCache As New Dictionary(Of Integer, List(Of Byte()))()
    Private Shared ReadOnly vlcLog As New ConcurrentQueue(Of String)()

    Public Sub New(output As ITestOutputHelper)
        Me.output = output
    End Sub

    Private Sub Say(line As String)
        report.AppendLine(line)
        output.WriteLine(line)
    End Sub

    Private Shared Function Env(name As String, fallback As String) As String
        Dim value As String = Environment.GetEnvironmentVariable(name)
        Return If(String.IsNullOrWhiteSpace(value), fallback, value)
    End Function

#Region "Setup: libvlc and a real AAC stream"

    Private Shared Function FindLibVlcDir() As String
        Dim configured As String = Environment.GetEnvironmentVariable("FMS_LIBVLC_DIR")
        If Not String.IsNullOrWhiteSpace(configured) AndAlso File.Exists(Path.Combine(configured, "libvlc.dll")) Then Return configured

        Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            For Each candidate As String In New String() {
                Path.Combine(dir.FullName, "bin", "Release", "libvlc", "win-x64"),
                Path.Combine(dir.FullName, "src", "Modern", "bin", "Release", "net10.0-windows", "libvlc", "win-x64")}
                If File.Exists(Path.Combine(candidate, "libvlc.dll")) Then Return candidate
            Next
            dir = dir.Parent
        End While
        Return Nothing
    End Function

    Private Shared Function FindFfmpeg() As String
        Dim configured As String = Environment.GetEnvironmentVariable("FMS_FFMPEG")
        If Not String.IsNullOrWhiteSpace(configured) AndAlso File.Exists(configured) Then Return configured
        For Each folder As String In Env("PATH", "").Split(";"c)
            If folder.Length = 0 Then Continue For
            Dim candidate As String = Path.Combine(folder.Trim(), "ffmpeg.exe")
            If File.Exists(candidate) Then Return candidate
        Next
        Dim winget As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe")
        Return If(File.Exists(winget), winget, Nothing)
    End Function

    ''' <summary>Real ADTS AAC-LC mono 44.1 kHz at 128 kbit/s, split into frames. Cached for the run.</summary>
    Private Shared Function RealFrames(seconds As Integer, Optional kilobitsPerSecond As Integer = 128) As List(Of Byte())
        Dim cached As List(Of Byte()) = Nothing
        If frameCache.TryGetValue(kilobitsPerSecond, cached) Then Return cached

        Dim ffmpeg As String = FindFfmpeg()
        If ffmpeg Is Nothing Then Return Nothing
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-live-latency")
        Directory.CreateDirectory(folder)
        Dim silent As Boolean = Environment.GetEnvironmentVariable("FMS_LIVE_SILENT") = "1"
        Dim file As String = Path.Combine(folder, If(silent, "silence-", "tone-") & seconds.ToString(CultureInfo.InvariantCulture) & "s-" & kilobitsPerSecond.ToString(CultureInfo.InvariantCulture) & "k.aac")

        If Not IO.File.Exists(file) Then
            Dim info As New ProcessStartInfo(ffmpeg) With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardError = True}
            ' A real encoder's real ADTS frames, either a very quiet 440 Hz tone or true silence (for runs that play out loud).
            Dim source As String = If(silent, "anullsrc=r=44100:cl=mono", "sine=frequency=440:sample_rate=44100")
            For Each arg As String In New String() {"-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", source, "-t", seconds.ToString(CultureInfo.InvariantCulture),
                                                     "-af", "volume=0.05", "-ac", "1", "-c:a", "aac", "-b:a", kilobitsPerSecond.ToString(CultureInfo.InvariantCulture) & "k", "-f", "adts", file}
                info.ArgumentList.Add(arg)
            Next
            Using process As Process = Process.Start(info)
                process.WaitForExit(60000)
                If process.ExitCode <> 0 Then Return Nothing
            End Using
        End If

        Dim bytes As Byte() = IO.File.ReadAllBytes(file)
        Dim frames As New List(Of Byte())()
        Dim index As Integer = 0
        While index + 7 <= bytes.Length
            If bytes(index) = &HFF AndAlso (bytes(index + 1) And &HF6) = &HF0 Then
                Dim length As Integer = ((bytes(index + 3) And &H3) << 11) Or (CInt(bytes(index + 4)) << 3) Or (bytes(index + 5) >> 5)
                If length < 7 OrElse index + length > bytes.Length Then Exit While
                Dim frame(length - 1) As Byte
                Buffer.BlockCopy(bytes, index, frame, 0, length)
                frames.Add(frame)
                index += length
            Else
                index += 1
            End If
        End While
        frameCache(kilobitsPerSecond) = frames
        Return frames
    End Function

#End Region

#Region "The paced device"

    Private Enum Shape
        Phone
        Watch
    End Enum

    ''' <summary>A device that plays the real stream at the producer's clock (clock 1.0 = real time; 1.01 = 1 % fast).</summary>
    Private NotInheritable Class PacedDevice
        Public ReadOnly StreamStart As New ConcurrentDictionary(Of Integer, Long)()
        Public DropWallTicks As Long
        Private ReadOnly frames As List(Of Byte())
        Private ReadOnly kind As Shape
        Private ReadOnly clock As Double
        Private ReadOnly dropAfterMs As Double

        Public Sub New(frames As List(Of Byte()), kind As Shape, clock As Double, dropAfterMs As Double)
            Me.frames = frames
            Me.kind = kind
            Me.clock = clock
            Me.dropAfterMs = dropAfterMs
        End Sub

        Public Async Function ServeAsync(c As LiveMockConnection, token As CancellationToken) As Task
            ' The phone answers an HTTP/1.1 request chunked and an HTTP/1.0 request (VLC's second try, with Icy-MetaData)
            ' raw, like any HTTP server; the watch always answers 1.0 raw.
            Dim chunked As Boolean = kind = Shape.Phone AndAlso c.RequestHead.Contains("HTTP/1.1")
            If chunked Then
                c.WriteAscii("HTTP/1.1 200 OK" & vbCrLf & "Content-Type: audio/aac" & vbCrLf & "Transfer-Encoding: chunked" & vbCrLf & "Accept-Ranges: none" & vbCrLf &
                             "icy-name: Spike" & vbCrLf & vbCrLf)
            Else
                c.WriteAscii("HTTP/1.0 200 OK" & vbCrLf & "Content-Type: audio/aac" & vbCrLf & "Cache-Control: no-cache" & vbCrLf & "Connection: close" & vbCrLf & vbCrLf)
            End If

            Dim started As Long = Stopwatch.GetTimestamp()
            StreamStart(c.Sequence) = started
            Dim perWrite As Integer = If(kind = Shape.Watch, 2, 1)       ' the watch sends two frames about every 47 ms (contract 5.5)
            Dim group As Long = 0
            Dim frameIndex As Long = 0
            Dim frameMsReal As Double = FrameMs / clock

            While Not token.IsCancellationRequested
                Dim dueMs As Double = group * perWrite * frameMsReal
                Dim nowMs As Double = (Stopwatch.GetTimestamp() - started) * 1000.0R / Stopwatch.Frequency
                If dueMs > nowMs Then Await Task.Delay(Math.Max(1, CInt(dueMs - nowMs)), token).ConfigureAwait(False)

                For k As Integer = 1 To perWrite
                    Dim frame As Byte() = frames(CInt(frameIndex Mod frames.Count))
                    If chunked Then
                        c.WriteAscii(frame.Length.ToString("x", CultureInfo.InvariantCulture) & vbCrLf)
                        c.Write(frame)
                        c.WriteAscii(vbCrLf)
                    Else
                        c.Write(frame)
                    End If
                    frameIndex += 1
                Next
                group += 1

                If c.Sequence = 1 AndAlso dropAfterMs > 0 AndAlso dueMs >= dropAfterMs Then
                    DropWallTicks = Stopwatch.GetTimestamp()
                    c.Reset()
                    Return
                End If
            End While
        End Function
    End Class

#End Region

#Region "The player side, mirroring Main_Form.LiveBroadcast"

    Private NotInheritable Class Sample
        Public WallMs As Double          ' since the run began
        Public PlayedMs As Long
        Public ArrivedMs As Double
        Public DelayMs As Double         ' producer clock to player position
        Public Connection As Integer
    End Class

    Private NotInheritable Class SpikeHost
        Implements ILiveRunnerHost

        Public ReadOnly Libvlc As LibVLC
        Public ReadOnly Player As MediaPlayer
        Public ReadOnly Options As LivePlaybackOptions
        Public Connections As Integer
        Public ReadOnly OpenedTicks As New ConcurrentDictionary(Of Integer, Long)()
        Public ReadOnly PlayingTicks As New ConcurrentDictionary(Of Integer, Long)()
        Private media As LiveVlcMedia
        Public Current As LiveHttpPlaybackSession

        Public Sub New(libvlc As LibVLC, player As MediaPlayer, options As LivePlaybackOptions)
            Me.Libvlc = libvlc
            Me.Player = player
            Me.Options = options
        End Sub

        Public Function StartPlaybackAsync(session As LiveHttpPlaybackSession) As Task Implements ILiveRunnerHost.StartPlaybackAsync
            Connections += 1
            OpenedTicks(Connections) = Stopwatch.GetTimestamp()
            Current = session
            Dim previous As LiveVlcMedia = media
            media = LiveVlcMedia.Create(Libvlc, session, Options, 1000)
            Player.Play(media.Media)
            If previous IsNot Nothing Then previous.Dispose()
            Return Task.CompletedTask
        End Function

        Public Async Function StopPlaybackAsync() As Task Implements ILiveRunnerHost.StopPlaybackAsync
            Dim stopping As Task = Task.Run(Sub()
                                                Try
                                                    Player.Stop()
                                                    Player.Media = Nothing
                                                Catch
                                                End Try
                                            End Sub)
            Dim done As Task = Await Task.WhenAny(stopping, Task.Delay(5000))
            If done Is stopping AndAlso media IsNot Nothing Then
                media.Dispose()
                media = Nothing
            End If
            Current = Nothing
        End Function

        Public Sub OnState(state As LiveSessionState, retryCount As Integer, elapsedMs As Long) Implements ILiveRunnerHost.OnState
        End Sub
    End Class

#End Region

    Private NotInheritable Class ScenarioResult
        Public Name As String
        Public FirstSoundMs As Double = -1
        Public HeadersMs As Long = -1
        Public FirstByteMs As Long = -1
        Public Samples As New List(Of Sample)()
        Public RecoveryMs As Double = -1
        Public Rejoins As Integer
        Public Connections As Integer
        Public Note As String = ""
    End Class

    Private Shared Function Percentile(values As List(Of Double), p As Double) As Double
        If values.Count = 0 Then Return Double.NaN
        Dim sorted As List(Of Double) = values.OrderBy(Function(v) v).ToList()
        Dim index As Integer = Math.Min(sorted.Count - 1, CInt(Math.Floor(p * (sorted.Count - 1) + 0.5)))
        Return sorted(index)
    End Function

    Private Shared Function Ms(ticks As Long) As Double
        Return ticks * 1000.0R / Stopwatch.Frequency
    End Function

    Private Shared Function F(value As Double) As String
        Return If(Double.IsNaN(value), "n/a", value.ToString("0", CultureInfo.InvariantCulture))
    End Function

    ''' <summary>Runs one scenario through the live path: runner + session + LiveVlcMedia on the real LibVLC.</summary>
    Private Async Function RunLivePathAsync(name As String, kind As Shape, clock As Double, seconds As Integer, dropAfterMs As Double,
                                            options As LivePlaybackOptions, useGuard As Boolean) As Task(Of ScenarioResult)
        Dim result As New ScenarioResult With {.Name = name}
        Dim device As New PacedDevice(RealFrames(seconds + 30, If(kind = Shape.Watch, 64, 128)), kind, clock, dropAfterMs)
        Using mock As New LiveMockDevice(AddressOf device.ServeAsync)
            Using player As New MediaPlayer(libVlcInstance)
                player.Volume = 0
                Dim host As New SpikeHost(libVlcInstance, player, options)
                Dim endpoint As New LiveBroadcastEndpoint With {.Url = mock.Url(If(kind = Shape.Watch, "/listen", "/live-audio.aac")),
                                                                 .Mode = "AUDIO_ONLY", .ModeValue = LiveBroadcastMode.AudioOnly, .Transport = "HTTP"}
                Dim runner As New LiveBroadcastRunner(endpoint, New LiveReconnectPolicy(), host, options, 1000)
                Dim playingEvent As Long = 0
                AddHandler player.Playing, Sub(s, e) Interlocked.CompareExchange(playingEvent, Stopwatch.GetTimestamp(), 0L)
                AddHandler player.EndReached, Sub(s, e) runner.NotifyPlayerEnded()
                AddHandler player.EncounteredError, Sub(s, e) runner.NotifyPlayerEnded()

                Dim runStart As Long = Stopwatch.GetTimestamp()
                Using cts As New CancellationTokenSource()
                    Dim run As Task(Of LiveRunEnd) = runner.RunAsync(cts.Token)
                    Dim lastGuardCheck As Long = 0
                    Dim firstSoundSeen As Boolean = False
                    Dim secondConnectionSound As Boolean = False

                    While Ms(Stopwatch.GetTimestamp() - runStart) < seconds * 1000.0R
                        Await Task.Delay(20)
                        Dim now As Long = Stopwatch.GetTimestamp()
                        Dim wall As Double = Ms(now - runStart)
                        Dim session As LiveHttpPlaybackSession = host.Current
                        If session Is Nothing Then Continue While

                        Dim played As Long = 0
                        Dim isPlaying As Boolean = False
                        Try
                            played = player.Time
                            isPlaying = player.IsPlaying
                        Catch
                        End Try

                        Dim connection As Integer = host.Connections
                        If played > 0 AndAlso isPlaying Then
                            If Not firstSoundSeen Then
                                firstSoundSeen = True
                                result.FirstSoundMs = wall
                                result.HeadersMs = session.HeadersElapsedMs
                                result.FirstByteMs = session.FirstByteElapsedMs
                            End If
                            If connection >= 2 AndAlso Not secondConnectionSound AndAlso device.DropWallTicks > 0 Then
                                secondConnectionSound = True
                                result.RecoveryMs = Ms(now - device.DropWallTicks)
                            End If
                        End If

                        ' One sample every 250 ms once there is a position to compare.
                        If played > 0 AndAlso device.StreamStart.ContainsKey(connection) AndAlso (result.Samples.Count = 0 OrElse wall - result.Samples(result.Samples.Count - 1).WallMs >= 250.0R) Then
                            Dim sinceStreamStart As Double = Ms(now - device.StreamStart(connection))
                            result.Samples.Add(New Sample With {
                                .WallMs = wall, .PlayedMs = played, .ArrivedMs = session.Frames.DurationMs,
                                .DelayMs = sinceStreamStart - played / clock, .Connection = connection})
                        End If

                        If useGuard AndAlso wall - lastGuardCheck >= 1000.0R Then
                            lastGuardCheck = CLng(wall)
                            Dim before As Integer = runner.Drift.RejoinCount
                            runner.SampleDrift(played, isPlaying)
                            If runner.Drift.RejoinCount > before Then Say("    guard: lag " & F(runner.Drift.LastLagMs) & " ms above " & runner.Drift.ThresholdMs & " ms at t=" & F(wall) & " ms - rejoining")
                        End If
                    End While

                    cts.Cancel()
                    Await run.WaitAsync(TimeSpan.FromSeconds(10))
                End Using
                result.Rejoins = runner.Drift.RejoinCount
                result.Connections = host.Connections
            End Using
        End Using
        Return result
    End Function

    ''' <summary>"Before": LibVLC given the bare URL, as the viewer's ordinary Open URL does.</summary>
    Private Async Function RunDirectPathAsync(name As String, kind As Shape, clock As Double, seconds As Integer) As Task(Of ScenarioResult)
        Dim result As New ScenarioResult With {.Name = name}
        Dim device As New PacedDevice(RealFrames(seconds + 30, If(kind = Shape.Watch, 64, 128)), kind, clock, 0)
        Using mock As New LiveMockDevice(AddressOf device.ServeAsync)
            Using player As New MediaPlayer(libVlcInstance)
                player.Volume = 0
                Dim url As String = mock.Url(If(kind = Shape.Watch, "/listen", "/live-audio.aac"))
                Dim runStart As Long = Stopwatch.GetTimestamp()
                Using media As New Media(libVlcInstance, url, FromType.FromLocation)
                    player.Play(media)
                    Dim firstSoundSeen As Boolean = False
                    While Ms(Stopwatch.GetTimestamp() - runStart) < seconds * 1000.0R
                        Await Task.Delay(20)
                        Dim now As Long = Stopwatch.GetTimestamp()
                        Dim wall As Double = Ms(now - runStart)
                        Dim played As Long = 0
                        Dim isPlaying As Boolean = False
                        Try
                            played = player.Time
                            isPlaying = player.IsPlaying
                        Catch
                        End Try
                        If played > 0 AndAlso isPlaying AndAlso Not firstSoundSeen Then
                            firstSoundSeen = True
                            result.FirstSoundMs = wall
                        End If
                        If played > 0 AndAlso Not device.StreamStart.IsEmpty AndAlso (result.Samples.Count = 0 OrElse wall - result.Samples(result.Samples.Count - 1).WallMs >= 250.0R) Then
                            result.Samples.Add(New Sample With {.WallMs = wall, .PlayedMs = played, .DelayMs = Ms(now - device.StreamStart(device.StreamStart.Keys.Max())) - played / clock, .Connection = 1})
                        End If
                    End While
                    Await Task.Run(Sub()
                                       player.Stop()
                                       player.Media = Nothing
                                   End Sub)
                End Using
            End Using
        End Using
        result.Connections = 1
        Return result
    End Function

    Private Sub Describe(result As ScenarioResult, settleFromMs As Double)
        Dim steady As List(Of Sample) = result.Samples.Where(Function(s) s.WallMs >= settleFromMs AndAlso s.Connection = result.Samples.Last().Connection).ToList()
        Dim delays As List(Of Double) = steady.Select(Function(s) s.DelayMs).ToList()
        Dim lags As List(Of Double) = steady.Where(Function(s) s.ArrivedMs > 0).Select(Function(s) s.ArrivedMs - s.PlayedMs).ToList()
        Say("  " & result.Name)
        Say("    open -> first sound (player time > 0): " & F(result.FirstSoundMs) & " ms" &
            If(result.HeadersMs >= 0, "   [headers " & result.HeadersMs & " ms, first byte " & result.FirstByteMs & " ms]", ""))
        If delays.Count > 0 Then
            Say("    source delay after " & F(settleFromMs / 1000.0R) & " s (producer clock -> player position, excluding the output device): median " &
                F(Percentile(delays, 0.5)) & " ms, p95 " & F(Percentile(delays, 0.95)) & " ms, max " & F(delays.Max()) & " ms (" & delays.Count & " samples)")
        End If
        If lags.Count > 0 Then
            Say("    arrived-minus-played (what the drift guard sees): median " & F(Percentile(lags, 0.5)) & " ms, p95 " & F(Percentile(lags, 0.95)) & " ms, max " & F(lags.Max()) & " ms")
        End If
        If steady.Count > 8 Then
            ' Least squares over every steady sample: the first and last points alone are as noisy as the Time reading.
            Dim meanX As Double = steady.Average(Function(s) s.WallMs / 1000.0R)
            Dim meanY As Double = steady.Average(Function(s) s.DelayMs)
            Dim sxy As Double = steady.Sum(Function(s) (s.WallMs / 1000.0R - meanX) * (s.DelayMs - meanY))
            Dim sxx As Double = steady.Sum(Function(s) (s.WallMs / 1000.0R - meanX) * (s.WallMs / 1000.0R - meanX))
            If sxx > 0 Then Say("    delay trend (least squares over the steady part): " & (sxy / sxx).ToString("0.0", CultureInfo.InvariantCulture) & " ms per second")
        End If
        If result.RecoveryMs >= 0 Then Say("    forced reconnect: sound returned " & F(result.RecoveryMs) & " ms after the device dropped the connection")
        If result.Connections > 1 Then Say("    connections: " & result.Connections & ", drift rejoins: " & result.Rejoins)
        If result.Note.Length > 0 Then Say("    " & result.Note)
    End Sub

    ''' <summary>Finds libvlc and ffmpeg, starts LibVLC once, and prints the header of the report.</summary>
    Private Sub Prepare(seconds As Integer)
        Dim vlcDir As String = FindLibVlcDir()
        Assert.True(vlcDir IsNot Nothing, "COULD NOT VERIFY: no libvlc.dll (set FMS_LIBVLC_DIR or build the viewer so bin\Release\libvlc\win-x64 exists)")
        Assert.True(RealFrames(seconds + 30, 128) IsNot Nothing AndAlso RealFrames(seconds + 30, 64) IsNot Nothing, "COULD NOT VERIFY: no ffmpeg to make a real AAC stream (set FMS_FFMPEG or install it)")

        If libVlcInstance Is Nothing Then
            Core.Initialize(vlcDir)
            If Environment.GetEnvironmentVariable("FMS_LIVE_VERBOSE") = "1" Then
                ' Diagnostics only: what LibVLC itself says while the first sound is being waited for.
                libVlcInstance = New LibVLC("--no-video-title-show", "--no-osd", "--no-mouse-events", "--verbose=2")
                AddHandler libVlcInstance.Log, Sub(s, e) vlcLog.Enqueue(Ms(Stopwatch.GetTimestamp()).ToString("0", CultureInfo.InvariantCulture) & " [" & e.Module & "] " & e.Message)
            Else
                libVlcInstance = New LibVLC("--no-video-title-show", "--no-osd", "--quiet", "--no-mouse-events")
            End If
        End If

        Say("Live broadcast latency, spec 023 phase 1 - " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
        Say("libvlc: " & vlcDir)
        Say("scenario length " & seconds & " s; the producer clock is the mock device's; one PC, loopback, output device silent (volume 0)")
        Say("")
    End Sub

    Private Sub SaveReport(prefix As String)
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-live-latency")
        Directory.CreateDirectory(folder)
        Dim stamp As String = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        Dim file As String = Path.Combine(folder, prefix & stamp & ".txt")
        IO.File.WriteAllText(file, report.ToString())
        output.WriteLine("report: " & file)
        If Not vlcLog.IsEmpty Then
            Dim logFile As String = Path.Combine(folder, "vlc-log-" & stamp & ".txt")
            IO.File.WriteAllLines(logFile, vlcLog.ToArray())
            output.WriteLine("libvlc log: " & logFile)
        End If
    End Sub

    ''' <summary>
    ''' A long run on the live path with the producer clock off by a few tenths of a percent and the drift guard on:
    ''' FMS_LIVE_SOAK_SECONDS (default 600), FMS_LIVE_SOAK_CLOCK_PERCENT (default 100.5). Shows the delay staying bounded
    ''' for as long as it plays - what the 30-minute line of the section 11 report asks for, on this PC.
    ''' </summary>
    <Fact>
    Public Async Function Phase_L1_Soak_With_A_Drifting_Producer_Clock() As Task
        If Environment.GetEnvironmentVariable(GateName) <> "1" OrElse Environment.GetEnvironmentVariable("FMS_LIVE_SOAK") <> "1" Then
            output.WriteLine("NOT RUN: hand-run soak - set FMS_LIVE_LATENCY=1 and FMS_LIVE_SOAK=1.")
            Return
        End If
        Dim seconds As Integer = Integer.Parse(Env("FMS_LIVE_SOAK_SECONDS", "600"), CultureInfo.InvariantCulture)
        Dim clock As Double = Double.Parse(Env("FMS_LIVE_SOAK_CLOCK_PERCENT", "100.5"), CultureInfo.InvariantCulture) / 100.0R
        Prepare(seconds)
        Say("SOAK - " & seconds & " s, producer clock " & (clock * 100.0R).ToString("0.0", CultureInfo.InvariantCulture) & " %, drift guard ON")
        Dim result As ScenarioResult = Await RunLivePathAsync("phone shape, soak", Shape.Phone, clock, seconds, 0, New LivePlaybackOptions(), True)
        Describe(result, 10000)
        ' Per-minute maxima: the delay should sawtooth under the guard's threshold instead of climbing.
        For minute As Integer = 0 To seconds \ 60 - 1
            Dim inMinute As List(Of Double) = result.Samples.Where(Function(s) s.WallMs >= minute * 60000.0R AndAlso s.WallMs < (minute + 1) * 60000.0R).Select(Function(s) s.DelayMs).ToList()
            If inMinute.Count > 0 Then Say("    minute " & (minute + 1) & ": delay median " & F(Percentile(inMinute, 0.5)) & " ms, max " & F(inMinute.Max()) & " ms")
        Next
        SaveReport("soak-")
    End Function

    ''' <summary>
    ''' Serves the mock phone on this PC's LAN address for the REAL viewer to connect to (FMS_LIVE_SERVE=1,
    ''' FMS_LIVE_SERVE_SECONDS, default 90; FMS_LIVE_SILENT=1 for silence). Writes %TEMP%\fms-live-latency\serve-info.txt
    ''' with the address, the descriptor, the FMSBCAST1 code and a .fmsbcast file - the three hand-off forms - so the viewer's
    ''' whole path (import, window, player, status line) can be driven from outside. A loopback address would be refused by
    ''' the importer, which is the point of using the LAN one.
    ''' </summary>
    <Fact>
    Public Async Function Phase_L1_Serve_The_Mock_Phone_For_The_Real_Viewer() As Task
        If Environment.GetEnvironmentVariable(GateName) <> "1" OrElse Environment.GetEnvironmentVariable("FMS_LIVE_SERVE") <> "1" Then
            output.WriteLine("NOT RUN: hand-run - set FMS_LIVE_LATENCY=1 and FMS_LIVE_SERVE=1.")
            Return
        End If
        Dim seconds As Integer = Integer.Parse(Env("FMS_LIVE_SERVE_SECONDS", "90"), CultureInfo.InvariantCulture)
        Assert.True(RealFrames(seconds + 30) IsNot Nothing, "COULD NOT VERIFY: no ffmpeg to make a real AAC stream")

        Dim lan As String = Nothing
        For Each nic As System.Net.NetworkInformation.NetworkInterface In System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            If nic.OperationalStatus <> System.Net.NetworkInformation.OperationalStatus.Up Then Continue For
            If nic.NetworkInterfaceType = System.Net.NetworkInformation.NetworkInterfaceType.Loopback Then Continue For
            ' A bridged Hyper-V switch can be the machine's only real LAN address; the NAT "Default Switch" and Bluetooth are not.
            Dim text As String = nic.Description & " " & nic.Name
            If text.IndexOf("default switch", StringComparison.OrdinalIgnoreCase) >= 0 OrElse text.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            For Each address As System.Net.NetworkInformation.UnicastIPAddressInformation In nic.GetIPProperties().UnicastAddresses
                If address.Address.AddressFamily = System.Net.Sockets.AddressFamily.InterNetwork AndAlso Not address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal) Then
                    lan = address.Address.ToString()
                    Exit For
                End If
            Next
            If lan IsNot Nothing Then Exit For
        Next
        Assert.True(lan IsNot Nothing, "COULD NOT VERIFY: no LAN IPv4 address on this PC")

        ' FMS_LIVE_SERVE_CLOCK_PERCENT: a producer clock that runs fast (e.g. 103), to make the viewer's drift guard act.
        Dim serveClock As Double = Double.Parse(Env("FMS_LIVE_SERVE_CLOCK_PERCENT", "100"), CultureInfo.InvariantCulture) / 100.0R
        Dim device As New PacedDevice(RealFrames(seconds + 30), Shape.Phone, serveClock, 0)
        Using mock As New LiveMockDevice(AddressOf device.ServeAsync, Nothing, True)
            Dim url As String = "http://" & lan & ":" & mock.Port.ToString(CultureInfo.InvariantCulture) & "/live-audio.aac"
            Dim json As String = "{""schemaVersion"":1,""url"":""" & url & """,""mode"":""AUDIO_ONLY"",""title"":""Spike phone"",""sourceId"":""spike-phone"",""isLive"":true,""targetLatencyMs"":1000}"
            Dim code As String
            Using packed As New MemoryStream()
                Using gz As New IO.Compression.GZipStream(packed, IO.Compression.CompressionLevel.Optimal, True)
                    Dim bytes As Byte() = Encoding.UTF8.GetBytes(json)
                    gz.Write(bytes, 0, bytes.Length)
                End Using
                code = "FMSBCAST1:" & Convert.ToBase64String(packed.ToArray())
            End Using
            Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-live-latency")
            Directory.CreateDirectory(folder)
            Dim fileForm As String = Path.Combine(folder, "spike-phone.fmsbcast")
            IO.File.WriteAllText(fileForm, json)
            IO.File.WriteAllLines(Path.Combine(folder, "serve-info.txt"), New String() {url, json, code, fileForm})
            output.WriteLine("serving " & url & " for " & seconds & " s")

            Await Task.Delay(seconds * 1000)
            output.WriteLine("connections accepted by the mock phone: " & mock.AcceptedCount)
            IO.File.AppendAllText(Path.Combine(folder, "serve-info.txt"), Environment.NewLine & "accepted=" & mock.AcceptedCount & Environment.NewLine)
        End Using
    End Function

    ''' <summary>One phone-shape live path with LibVLC's own log on: FMS_LIVE_VERBOSE=1 FMS_LIVE_VERBOSE_RUN=1.</summary>
    <Fact>
    Public Async Function Phase_L1_Verbose_Log_Of_One_Start() As Task
        If Environment.GetEnvironmentVariable(GateName) <> "1" OrElse Environment.GetEnvironmentVariable("FMS_LIVE_VERBOSE_RUN") <> "1" Then
            output.WriteLine("NOT RUN: hand-run diagnostic - set FMS_LIVE_LATENCY=1, FMS_LIVE_VERBOSE=1 and FMS_LIVE_VERBOSE_RUN=1.")
            Return
        End If
        Prepare(14)
        Dim options As New LivePlaybackOptions()
        Dim extra As String = Environment.GetEnvironmentVariable("FMS_LIVE_RAW")
        If Not String.IsNullOrWhiteSpace(extra) Then options.OverrideOptions = extra.Split(";"c).Where(Function(o) o.Trim().Length > 0).Select(Function(o) o.Trim()).ToArray()
        vlcLog.Enqueue("--- scenario start " & Ms(Stopwatch.GetTimestamp()).ToString("0", CultureInfo.InvariantCulture) & " ms; options: " & String.Join(" ", options.ToMediaOptions(1000)))
        Dim kind As Shape = If(Environment.GetEnvironmentVariable("FMS_LIVE_SHAPE") = "watch", Shape.Watch, Shape.Phone)
        Dim result As ScenarioResult
        If Environment.GetEnvironmentVariable("FMS_LIVE_DIRECT") = "1" Then
            result = Await RunDirectPathAsync("direct URL, verbose", kind, 1.0R, 14)
        Else
            result = Await RunLivePathAsync("live path, verbose", kind, 1.0R, 14, 0, options, False)
        End If
        Describe(result, 8000)
        SaveReport("verbose-")
    End Function

    ''' <summary>
    ''' Compares LibVLC option variants on the live path: FMS_LIVE_VARIANTS="label=opt;opt|label2=opt;opt", each opt a
    ''' per-media option such as ":demux=adts". The variant REPLACES the viewer's option list, so name every option.
    ''' </summary>
    <Fact>
    Public Async Function Phase_L1_Option_Variants_On_Real_LibVLC() As Task
        Dim variants As String = Environment.GetEnvironmentVariable("FMS_LIVE_VARIANTS")
        If Environment.GetEnvironmentVariable(GateName) <> "1" OrElse String.IsNullOrWhiteSpace(variants) Then
            output.WriteLine("NOT RUN: hand-run measurement - set FMS_LIVE_LATENCY=1 and FMS_LIVE_VARIANTS (see the header of this file).")
            Return
        End If

        Dim seconds As Integer = Integer.Parse(Env("FMS_LIVE_SECONDS", "20"), CultureInfo.InvariantCulture)
        Prepare(seconds)

        For Each candidate As String In variants.Split("|"c)
            Dim split As Integer = candidate.IndexOf("="c)
            Dim label As String = If(split > 0, candidate.Substring(0, split), candidate)
            Dim list As String() = If(split > 0, candidate.Substring(split + 1), "").Split(";"c).Where(Function(o) o.Trim().Length > 0).Select(Function(o) o.Trim()).ToArray()
            Say("VARIANT " & label & ": " & If(list.Length = 0, "(no per-media options)", String.Join(" ", list)))
            Dim phone As ScenarioResult = Await RunLivePathAsync("phone shape", Shape.Phone, 1.0R, seconds, 0, New LivePlaybackOptions With {.OverrideOptions = list}, False)
            Describe(phone, 8000)
            Dim watch As ScenarioResult = Await RunLivePathAsync("watch shape", Shape.Watch, 1.0R, seconds, 0, New LivePlaybackOptions With {.OverrideOptions = list}, False)
            Describe(watch, 8000)
            Say("")
        Next
        SaveReport("variants-")
    End Function

    <Fact>
    Public Async Function Phase_L1_Latency_Of_Phone_And_Watch_Shapes_On_Real_LibVLC() As Task
        If Environment.GetEnvironmentVariable(GateName) <> "1" Then
            output.WriteLine("NOT RUN: hand-run measurement - set FMS_LIVE_LATENCY=1 (see the header of this file).")
            Return
        End If

        Dim seconds As Integer = Integer.Parse(Env("FMS_LIVE_SECONDS", "30"), CultureInfo.InvariantCulture)
        Dim cachings As Integer() = Env("FMS_LIVE_CACHING", "300").Split(","c).Select(Function(v) Integer.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray()
        Prepare(seconds)

        Say("BEFORE - LibVLC given the bare URL (what Open URL.. does today)")
        Dim beforePhone As ScenarioResult = Await RunDirectPathAsync("phone shape, direct URL", Shape.Phone, 1.0R, seconds)
        Describe(beforePhone, 10000)
        Dim beforeWatch As ScenarioResult = Await RunDirectPathAsync("watch shape, direct URL", Shape.Watch, 1.0R, seconds)
        Describe(beforeWatch, 10000)
        Say("")

        For Each caching As Integer In cachings
            Say("AFTER - the live path (one GET -> StreamMediaInput), caching " & caching & " ms")
            Dim afterPhone As ScenarioResult = Await RunLivePathAsync("phone shape", Shape.Phone, 1.0R, seconds, 0, New LivePlaybackOptions With {.CachingMs = caching}, False)
            Describe(afterPhone, 10000)
            Dim afterWatch As ScenarioResult = Await RunLivePathAsync("watch shape", Shape.Watch, 1.0R, seconds, 0, New LivePlaybackOptions With {.CachingMs = caching}, False)
            Describe(afterWatch, 10000)
            Say("")
        Next

        Say("FORCED RECONNECT - the device drops the connection after 8 s; phone shape")
        Dim forced As ScenarioResult = Await RunLivePathAsync("phone shape, drop at 8 s", Shape.Phone, 1.0R, Math.Max(seconds, 25), 8000, New LivePlaybackOptions With {.CachingMs = cachings(0)}, False)
        Describe(forced, 8000 + 6000)
        Say("")

        Say("STEADY-STATE DRIFT - producer clock +3 % (a deliberately large offset so the effect shows in a minute)")
        Dim noGuard As ScenarioResult = Await RunLivePathAsync("phone shape, +3 %, guard OFF", Shape.Phone, 1.03R, 60, 0, New LivePlaybackOptions With {.CachingMs = cachings(0)}, False)
        Describe(noGuard, 10000)
        Dim withGuard As ScenarioResult = Await RunLivePathAsync("phone shape, +3 %, guard ON", Shape.Phone, 1.03R, 60, 0, New LivePlaybackOptions With {.CachingMs = cachings(0)}, True)
        Describe(withGuard, 10000)
        Say("")

        SaveReport("report-")
    End Function

End Class
#End If
