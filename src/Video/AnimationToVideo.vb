#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Runs FFmpeg (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §9.4).
'''
''' Everything decision-shaped lives in <see cref="VideoConvertPlan"/>; what is left here
''' is the process itself, and the three things a process can do wrong:
'''   * never finish - a hard ten-minute ceiling, after which it is killed;
'''   * be cancelled - the Cancel button kills it, and the caller removes the temp file;
'''   * fail quietly - the tail of stderr is kept so the failure message can name a reason
'''     instead of a number.
'''
''' Nothing here touches the original file. That is deliberate and is what makes
''' invariant 2 checkable: the only path that can delete a source is in Main_Form.ToVideo.
''' </summary>
Friend Module AnimationToVideo

    ''' <summary>How much of stderr is kept for the failure message. FFmpeg's real reason is
    ''' always in the last few lines; the rest is a configuration banner.</summary>
    Private Const Stderr_Tail_Chars As Integer = 2000

    Friend NotInheritable Class EncoderRun
        Public Property ExitCode As Integer = -1
        Public Property Cancelled As Boolean
        Public Property TimedOut As Boolean
        ''' <summary>The tail of stderr, or the exception text when the process could not be
        ''' started at all.</summary>
        Public Property Detail As String = ""
    End Class

    ''' <summary>
    ''' Encodes into the temporary file and reports how it went.
    '''
    ''' <paramref name="totalDurationMs"/> is what the viewer is already playing (the summed
    ''' GIF frame delays), so the percentage costs no extra pass over the file. Zero means
    ''' "unknown" and the caller shows an indeterminate bar rather than inventing a number.
    ''' </summary>
    Friend Async Function RunAsync(ffmpegExe As String,
                                   sourcePath As String,
                                   tempPath As String,
                                   fps As Integer,
                                   totalDurationMs As Integer,
                                   progress As IProgress(Of Integer),
                                   token As CancellationToken) As Task(Of EncoderRun)
        Dim run As New EncoderRun()
        Dim stderrTail As New StringBuilder()

        Try
            Dim info As New ProcessStartInfo(ffmpegExe, VideoConvertPlan.BuildArguments(sourcePath, tempPath, fps)) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8,
                .WorkingDirectory = If(Path.GetDirectoryName(tempPath), String.Empty)
            }

            Using proc As New Process()
                proc.StartInfo = info
                proc.EnableRaisingEvents = True

                AddHandler proc.OutputDataReceived,
                    Sub(sender As Object, e As DataReceivedEventArgs)
                        If e.Data Is Nothing OrElse progress Is Nothing Then Return
                        Dim percent As Integer = ParseProgressPercent(e.Data, totalDurationMs)
                        If percent >= 0 Then progress.Report(percent)
                    End Sub

                AddHandler proc.ErrorDataReceived,
                    Sub(sender As Object, e As DataReceivedEventArgs)
                        If e.Data Is Nothing Then Return
                        SyncLock stderrTail
                            stderrTail.AppendLine(e.Data)
                            If stderrTail.Length > Stderr_Tail_Chars * 2 Then
                                stderrTail.Remove(0, stderrTail.Length - Stderr_Tail_Chars)
                            End If
                        End SyncLock
                    End Sub

                proc.Start()
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                ' One clock for both bounds: the caller's Cancel and the hard ceiling.
                ' Killing is the only way out of either - nothing can politely ask an
                ' encoder to stop mid-file.
                Using deadline As New CancellationTokenSource(VideoConvertPlan.Timeout_Ms)
                    Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token)
                        Try
                            Await proc.WaitForExitAsync(linked.Token).ConfigureAwait(False)
                        Catch ex As OperationCanceledException
                            run.Cancelled = token.IsCancellationRequested
                            run.TimedOut = deadline.IsCancellationRequested AndAlso Not run.Cancelled
                            KillQuietly(proc)
                            ' Wait again, uncancellably, so the temp file's handle is gone
                            ' before the caller tries to delete it.
                            Try
                                proc.WaitForExit(5000)
                            Catch
                            End Try
                        End Try
                    End Using
                End Using

                If Not run.Cancelled AndAlso Not run.TimedOut Then run.ExitCode = proc.ExitCode
            End Using
        Catch ex As Exception
            run.Detail = ex.Message
            AppFileLogger.LogException("FFmpeg could not be run for: " & sourcePath, ex)
            Return run
        End Try

        SyncLock stderrTail
            If run.Detail.Length = 0 Then run.Detail = LastLine(stderrTail.ToString())
        End SyncLock
        If run.TimedOut AndAlso run.Detail.Length = 0 Then run.Detail = "FFmpeg exceeded the time limit."
        Return run
    End Function

    ''' <summary>
    ''' One "-progress" line -> a percentage, or -1 when the line says nothing about time.
    '''
    ''' <c>out_time=HH:MM:SS.ffffff</c> is used rather than <c>out_time_ms</c>, whose name has
    ''' lied since 2017 - it carries MICROseconds, and reading it as milliseconds gives a
    ''' progress bar that finishes a thousand times too early.
    '''
    ''' Capped at 99: the bar reaches 100 when the process exits, not when FFmpeg's own
    ''' rounding says so.
    ''' </summary>
    Friend Function ParseProgressPercent(line As String, totalDurationMs As Integer) As Integer
        If totalDurationMs <= 0 OrElse String.IsNullOrEmpty(line) Then Return -1
        If Not line.StartsWith("out_time=", StringComparison.Ordinal) Then Return -1

        Dim value As String = line.Substring("out_time=".Length).Trim()
        Dim elapsed As TimeSpan
        If Not TimeSpan.TryParse(value, CultureInfo.InvariantCulture, elapsed) Then Return -1
        If elapsed.Ticks < 0 Then Return 0

        Dim percent As Integer = CInt(Math.Floor(elapsed.TotalMilliseconds * 100.0R / totalDurationMs))
        Return Math.Max(0, Math.Min(99, percent))
    End Function

    Private Sub KillQuietly(proc As Process)
        Try
            If Not proc.HasExited Then proc.Kill(entireProcessTree:=True)
        Catch
        End Try
    End Sub

    ''' <summary>FFmpeg's actual complaint is the last non-empty line; everything above it is
    ''' the stream description it always prints.</summary>
    Private Function LastLine(text As String) As String
        If String.IsNullOrWhiteSpace(text) Then Return ""
        Dim lines As String() = text.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        For index As Integer = lines.Length - 1 To 0 Step -1
            Dim candidate As String = lines(index).Trim()
            If candidate.Length > 0 Then Return candidate
        Next
        Return ""
    End Function

End Module
#End If
