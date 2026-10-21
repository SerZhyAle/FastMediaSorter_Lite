#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' The FFmpeg runner of spec 242 (rules 14, 24, 25, 27), driven against a FAKE child: Windows PowerShell, which is on
''' every Windows this suite runs on, standing in for ffmpeg.exe. The runner only ever sees "an executable that writes
''' bytes to stdout and text to stderr", so the stand-in proves the parts that are expensive to get wrong with the real
''' encoder - binary-clean capture, the ceiling, cancel, the deadline, the progress cap and the kill of the whole tree.
''' The same run against the real pinned FFmpeg is <see cref="AnimationVideoFfmpegTests"/>.
''' </summary>
Public Class AnimationVideoEncoderTests

    ' --- the stand-in ---------------------------------------------------------------

    ''' <summary>One repeating unit of 263 bytes: 0..255, then CR LF, a Ctrl-Z, CR LF, 0xFF, 0x00 - everything a text
    ''' decoder or a console would mangle. A block is 100 units, so the expected stream is a pure function of its length.</summary>
    Private Const Unit_Length As Integer = 263
    Private Const Block_Length As Integer = Unit_Length * 100

    Private Shared Function PowerShellExe() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
    End Function

    ''' <summary>The line that makes the script a command: encoded, so no quoting of the script can go wrong.</summary>
    Private Shared Function ScriptArguments(ParamArray lines As String()) As String
        Dim script As String = String.Join(vbLf, {
            "$ErrorActionPreference='Stop'",
            "$ProgressPreference='SilentlyContinue'",
            "$u=[byte[]]([byte[]](0..255)+[byte[]](13,10,26,13,10,255,0))",
            "$blk=New-Object byte[] " & Block_Length,
            "for($k=0;$k -lt 100;$k++){[Array]::Copy($u,0,$blk,$k*263,263)}",
            "$o=[Console]::OpenStandardOutput()"
        }) & vbLf & String.Join(vbLf, lines)
        Return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " & Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
    End Function

    Private Shared Function Literal(value As String) As String
        Return "'" & value.Replace("'", "''") & "'"
    End Function

    ''' <summary>The bytes a stand-in writes for <paramref name="blocks"/> blocks.</summary>
    Private Shared Function ExpectedPayload(blocks As Integer) As Byte()
        Dim unit(Unit_Length - 1) As Byte
        For i As Integer = 0 To 255
            unit(i) = CByte(i)
        Next
        Dim tail As Byte() = {13, 10, 26, 13, 10, 255, 0}
        For i As Integer = 0 To tail.Length - 1
            unit(256 + i) = tail(i)
        Next
        Dim data(Block_Length * blocks - 1) As Byte
        For i As Integer = 0 To data.Length - 1
            data(i) = unit(i Mod Unit_Length)
        Next
        Return data
    End Function

    ''' <summary>Records what the runner reports, synchronously, so a test can act on the very report (Progress(Of T)
    ''' would post it).</summary>
    Friend NotInheritable Class RecordingProgress
        Implements IProgress(Of Integer)

        Private ReadOnly gate As New Object()
        Private ReadOnly values As New List(Of Integer)()
        Private ReadOnly onReport As Action(Of Integer)

        Friend Sub New(Optional onReport As Action(Of Integer) = Nothing)
            Me.onReport = onReport
        End Sub

        Public Sub Report(value As Integer) Implements IProgress(Of Integer).Report
            SyncLock gate
                values.Add(value)
            End SyncLock
            If onReport IsNot Nothing Then onReport(value)
        End Sub

        Friend Function Snapshot() As Integer()
            SyncLock gate
                Return values.ToArray()
            End SyncLock
        End Function
    End Class

    Private Shared Function NewPidFile() As String
        Return Path.Combine(Path.GetTempPath(), "fms-avtest-" & Guid.NewGuid().ToString("N") & ".txt")
    End Function

    ''' <summary>The script lines that publish the stand-in's own process id and that of a child it starts. The child
    ''' inherits the stdout pipe, so a kill that spared it would also hold the capture open.</summary>
    Private Shared Function PublishPids(pidFile As String) As String()
        Return {
            "$psi=New-Object Diagnostics.ProcessStartInfo('ping.exe','-n 120 127.0.0.1')",
            "$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true",
            "$c=[Diagnostics.Process]::Start($psi)",
            "[IO.File]::WriteAllText(" & Literal(pidFile) & ",($PID.ToString()+','+$c.Id.ToString()))"
        }
    End Function

    Private Shared Function IsRunning(pid As Integer) As Boolean
        Try
            Using candidate As Process = Process.GetProcessById(pid)
                Return Not candidate.HasExited
            End Using
        Catch ex As ArgumentException
            Return False
        End Try
    End Function

    ''' <summary>Both processes named in the pid file are gone (the child may take a moment to follow its parent).</summary>
    Private Shared Sub AssertNothingLeftRunning(pidFile As String)
        Assert.True(File.Exists(pidFile), "the stand-in never published its process ids")
        For Each text As String In File.ReadAllText(pidFile).Split(","c)
            Dim pid As Integer = Integer.Parse(text.Trim())
            Assert.True(SpinWait.SpinUntil(Function() Not IsRunning(pid), 10000), "process " & pid & " is still running")
        Next
    End Sub

    ' Span work stays out of the async tests: a Span is a restricted type and may not live in an Async method.

    Private Shared Function SameBytes(expected As Byte(), actual As ReadOnlyMemory(Of Byte)) As Boolean
        Return expected.AsSpan().SequenceEqual(actual.Span)
    End Function

    Private Shared Function ByteAt(memory As ReadOnlyMemory(Of Byte), index As Integer) As Byte
        Return memory.Slice(index, 1).ToArray()(0)
    End Function

    Private Shared Function IsAllZero(memory As ReadOnlyMemory(Of Byte)) As Boolean
        Return memory.Span.IndexOfAnyExcept(CByte(0)) < 0
    End Function

    Private Shared Sub DeleteQuietly(path As String)
        Try
            File.Delete(path)
        Catch
        End Try
    End Sub

    Private Shared Async Function RunAsync(arguments As String, expectedDurationMs As Integer, maxBytes As Long,
                                           belowNormal As Boolean, progress As IProgress(Of Integer),
                                           token As CancellationToken,
                                           Optional timeoutMs As Integer = VideoConvertPlan.Timeout_Ms,
                                           Optional drainWaitMs As Integer = 15000) As Task(Of EncodedVideo)
        Return Await AnimationVideoEncoder.RunAsync(PowerShellExe(), arguments, expectedDurationMs, maxBytes, belowNormal,
                                                    progress, token, timeoutMs, drainWaitMs).WaitAsync(TimeSpan.FromSeconds(90))
    End Function

    ''' <summary>Ends the processes named in a pid file (a stand-in left a child on purpose).</summary>
    Private Shared Sub KillRecorded(pidFile As String)
        If Not File.Exists(pidFile) Then Return
        For Each text As String In File.ReadAllText(pidFile).Split(","c)
            Dim pid As Integer
            If Not Integer.TryParse(text.Trim(), pid) Then Continue For
            Try
                Using candidate As Process = Process.GetProcessById(pid)
                    candidate.Kill(True)
                End Using
            Catch
            End Try
        Next
    End Sub

    ' --- bytes, intact and in order -------------------------------------------------------

    <Fact>
    Public Async Function A_few_megabytes_of_binary_stdout_arrive_intact_and_in_order() As Task
        Const blocks As Integer = 120
        Dim args As String = ScriptArguments(
            "[Console]::Error.WriteLine('frame=1')",
            "for($n=0;$n -lt " & blocks & ";$n++){$o.Write($blk,0,$blk.Length)}",
            "$o.Flush()",
            "[Console]::Error.WriteLine('frame=300')",
            "[Console]::Error.WriteLine('out_time=00:00:09.000000')",
            "[Console]::Error.WriteLine('progress=end')")

        Using video As EncodedVideo = Await RunAsync(args, 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
            Assert.Equal("", video.Detail)
            Assert.Equal(0, video.ExitCode)
            Assert.Equal(Block_Length * blocks, video.Length)
            Assert.Equal(video.Length, video.Bytes.Length)
            Assert.Equal(video.Length, CInt(video.BytesCaptured))
            ' CR LF, Ctrl-Z, NUL and 0xFF are all in the stream: a text decode or a console would have changed them.
            Assert.True(SameBytes(ExpectedPayload(blocks), video.Bytes), "the captured stream differs from what was written")
            Assert.Equal(300L, video.FramesWritten)
            Assert.Equal(9000L, video.OutTimeMs)
        End Using
    End Function

    <Fact>
    Public Async Function A_video_larger_than_the_first_allocation_survives_the_growth() As Task
        ' 800 blocks = about 21 MB, over the 16 MB first buffer: one doubling in the middle of the stream.
        Const blocks As Integer = 800
        Dim args As String = ScriptArguments(
            "for($n=0;$n -lt " & blocks & ";$n++){$o.Write($blk,0,$blk.Length)}",
            "$o.Flush()",
            "[Console]::Error.WriteLine('frame=10')")

        Using video As EncodedVideo = Await RunAsync(args, 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
            Assert.Equal(Block_Length * blocks, video.Length)
            Assert.True(SameBytes(ExpectedPayload(blocks), video.Bytes), "the stream changed across the buffer growth")
        End Using
    End Function

    <Fact>
    Public Async Function Dispose_overwrites_the_captured_video_and_empties_the_result() As Task
        Dim args As String = ScriptArguments(
            "for($n=0;$n -lt 3;$n++){$o.Write($blk,0,$blk.Length)}",
            "$o.Flush()",
            "[Console]::Error.WriteLine('frame=3')")

        Dim video As EncodedVideo = Await RunAsync(args, 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None)
        Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
        Dim held As ReadOnlyMemory(Of Byte) = video.Bytes
        Assert.True(held.Length > 0 AndAlso ByteAt(held, 1) <> 0)
        video.Dispose()
        Assert.Equal(0, video.Length)
        Assert.True(video.Bytes.IsEmpty)
        ' The array the writer was handed no longer holds the video.
        Assert.True(IsAllZero(held), "plaintext video bytes survived Dispose")
    End Function

    ' --- the size ceiling ---------------------------------------------------------------

    <Fact>
    Public Async Function The_ceiling_kills_the_process_tree_and_reports_too_large_with_nothing_left_running() As Task
        Dim pidFile As String = NewPidFile()
        Try
            Const maxBytes As Long = 1024L * 1024L
            Dim lines As New List(Of String)(PublishPids(pidFile))
            lines.Add("while($true){$o.Write($blk,0,$blk.Length)}")

            Using video As EncodedVideo = Await RunAsync(ScriptArguments(lines.ToArray()), 0, maxBytes, False, Nothing, CancellationToken.None)
                Assert.Equal(AnimationVideoOutcome.OutputTooLarge, video.Outcome)
                Assert.True(video.BytesCaptured > maxBytes, "the ceiling was reported before it was crossed")
                ' It stops within a read of the ceiling, not megabytes past it.
                Assert.True(video.BytesCaptured <= maxBytes + 256L * 1024L, "captured " & video.BytesCaptured)
                Assert.Equal(0, video.Length)
                Assert.True(video.Bytes.IsEmpty)
                Assert.Contains("size limit", video.Detail, StringComparison.Ordinal)
            End Using
            AssertNothingLeftRunning(pidFile)
        Finally
            DeleteQuietly(pidFile)
        End Try
    End Function

    ' --- cancel ---------------------------------------------------------------------------

    <Fact>
    Public Async Function Cancel_kills_the_whole_tree_and_reports_cancelled() As Task
        Dim pidFile As String = NewPidFile()
        Try
            Using cts As New CancellationTokenSource()
                Dim lines As New List(Of String)(PublishPids(pidFile))
                lines.Add("$o.Write($blk,0,$blk.Length);$o.Flush()")
                lines.Add("[Console]::Error.WriteLine('frame=1')")
                lines.Add("[Console]::Error.WriteLine('out_time=00:00:00.500000')")
                lines.Add("Start-Sleep -Seconds 120")

                ' The progress report is the sign that the ids are published and the process is mid-run.
                Dim progress As New RecordingProgress(Sub(percent As Integer) cts.Cancel())
                Dim watch As Stopwatch = Stopwatch.StartNew()
                Using video As EncodedVideo = Await RunAsync(ScriptArguments(lines.ToArray()), 1000, AnimationVideoPlan.Video_Max_Bytes, False, progress, cts.Token)
                    Assert.Equal(AnimationVideoOutcome.Cancelled, video.Outcome)
                    Assert.True(video.Bytes.IsEmpty)
                    Assert.Equal(-1, video.ExitCode)
                End Using
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), "cancel waited for the stand-in's sleep")
            End Using
            AssertNothingLeftRunning(pidFile)
        Finally
            DeleteQuietly(pidFile)
        End Try
    End Function

    <Fact>
    Public Async Function A_token_cancelled_before_the_start_never_starts_the_process() As Task
        Using cts As New CancellationTokenSource()
            cts.Cancel()
            ' A path that cannot be started: if the runner tried, the outcome would be EncoderFailed.
            Using video As EncodedVideo = Await AnimationVideoEncoder.RunAsync("C:\no\such\ffmpeg.exe", "-version", 0, 1024L, False, Nothing, cts.Token)
                Assert.Equal(AnimationVideoOutcome.Cancelled, video.Outcome)
            End Using
        End Using
    End Function

    ' --- the deadline -----------------------------------------------------------------------

    <Fact>
    Public Async Function The_deadline_kills_a_run_that_never_ends() As Task
        Dim pidFile As String = NewPidFile()
        Try
            Dim lines As New List(Of String)(PublishPids(pidFile))
            lines.Add("Start-Sleep -Seconds 120")
            Dim watch As Stopwatch = Stopwatch.StartNew()
            Using video As EncodedVideo = Await RunAsync(ScriptArguments(lines.ToArray()), 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None, timeoutMs:=1000)
                Assert.Equal(AnimationVideoOutcome.TimedOut, video.Outcome)
                Assert.Contains("time limit", video.Detail, StringComparison.Ordinal)
                Assert.True(video.Bytes.IsEmpty)
            End Using
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), "the deadline did not end the run")
            ' The deadline can strike while the stand-in is still starting; whatever it managed to publish is gone.
            If File.Exists(pidFile) Then AssertNothingLeftRunning(pidFile)
        Finally
            DeleteQuietly(pidFile)
        End Try
    End Function

    ' --- progress -------------------------------------------------------------------------------

    <Fact>
    Public Async Function Progress_only_rises_is_capped_below_100_and_skips_lines_without_a_time() As Task
        Dim args As String = ScriptArguments(
            "[Console]::Error.WriteLine('out_time=N/A')",
            "[Console]::Error.WriteLine('out_time=00:00:00.250000')",
            "[Console]::Error.WriteLine('out_time=00:00:00.250000')",
            "[Console]::Error.WriteLine('out_time=00:00:00.100000')",
            "[Console]::Error.WriteLine('out_time=00:00:00.500000')",
            "[Console]::Error.WriteLine('speed=2x')",
            "[Console]::Error.WriteLine('out_time=00:00:30.000000')",
            "[Console]::Error.WriteLine('progress=end')",
            "for($n=0;$n -lt 3;$n++){$o.Write($blk,0,$blk.Length)}",
            "$o.Flush()",
            "[Console]::Error.WriteLine('frame=3')")

        Dim progress As New RecordingProgress()
        Using video As EncodedVideo = Await RunAsync(args, 1000, AnimationVideoPlan.Video_Max_Bytes, False, progress, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
        End Using
        ' Rising only (25, 50 - the repeat and the dip are dropped), then the cap: 3000 % of the duration is 99, never 100.
        Assert.Equal(New Integer() {25, 50, 99}, progress.Snapshot())
    End Function

    <Fact>
    Public Async Function An_unknown_duration_reports_no_percentage_at_all() As Task
        Dim args As String = ScriptArguments(
            "[Console]::Error.WriteLine('out_time=00:00:00.500000')",
            "for($n=0;$n -lt 3;$n++){$o.Write($blk,0,$blk.Length)}",
            "$o.Flush()",
            "[Console]::Error.WriteLine('frame=3')")

        Dim progress As New RecordingProgress()
        Using video As EncodedVideo = Await RunAsync(args, 0, AnimationVideoPlan.Video_Max_Bytes, False, progress, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
            Assert.Equal(500L, video.OutTimeMs)
        End Using
        Assert.Empty(progress.Snapshot())
    End Function

    <Fact>
    Public Async Function A_progress_handler_that_throws_does_not_stop_the_run() As Task
        Dim args As String = ScriptArguments(
            "[Console]::Error.WriteLine('out_time=00:00:00.250000')",
            "[Console]::Error.WriteLine('out_time=00:00:00.500000')",
            "for($n=0;$n -lt 3;$n++){$o.Write($blk,0,$blk.Length)}",
            "$o.Flush()",
            "[Console]::Error.WriteLine('frame=3')")

        Dim progress As New RecordingProgress(Sub(percent As Integer) Throw New InvalidOperationException("handler failed"))
        Using video As EncodedVideo = Await RunAsync(args, 1000, AnimationVideoPlan.Video_Max_Bytes, False, progress, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
            Assert.Equal(Block_Length * 3, video.Length)
        End Using
    End Function

    ' --- failures ---------------------------------------------------------------------------------

    <Fact>
    Public Async Function A_non_zero_exit_is_an_encoder_failure_with_the_error_lines_and_not_the_progress_block() As Task
        Dim args As String = ScriptArguments(
            "[Console]::Error.WriteLine('first line of noise')",
            "[Console]::Error.WriteLine('second line of noise')",
            "[Console]::Error.WriteLine('[webp @ 0000] image data not found')",
            "[Console]::Error.WriteLine('Error while decoding stream #0:0: Invalid data found when processing input')",
            "$o.Write($blk,0,$blk.Length);$o.Flush()",
            "[Console]::Error.WriteLine('frame=11')",
            "[Console]::Error.WriteLine('out_time=00:00:00.500000')",
            "[Console]::Error.WriteLine('speed=1x')",
            "[Console]::Error.WriteLine('progress=end')",
            "exit 183")

        Using video As EncodedVideo = Await RunAsync(args, 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.EncoderFailed, video.Outcome)
            Assert.Equal(183, video.ExitCode)
            Assert.Equal(11L, video.FramesWritten)
            Assert.Contains("exited with code 183", video.Detail, StringComparison.Ordinal)
            Assert.Contains("image data not found", video.Detail, StringComparison.Ordinal)
            Assert.Contains("Invalid data found", video.Detail, StringComparison.Ordinal)
            Assert.DoesNotContain("progress=", video.Detail, StringComparison.Ordinal)
            Assert.DoesNotContain("speed=", video.Detail, StringComparison.Ordinal)
            Assert.DoesNotContain("first line of noise", video.Detail, StringComparison.Ordinal)
            ' A failed run keeps no video, not even the part that was written.
            Assert.True(video.Bytes.IsEmpty)
            Assert.Equal(Block_Length, CInt(video.BytesCaptured))
        End Using
    End Function

    <Fact>
    Public Async Function A_program_that_cannot_be_started_is_an_encoder_failure_not_an_exception() As Task
        Using video As EncodedVideo = Await AnimationVideoEncoder.RunAsync("C:\no\such\dir\ffmpeg.exe", "-version", 0, 1024L, False, Nothing, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.EncoderFailed, video.Outcome)
            Assert.Equal(-1, video.ExitCode)
            Assert.StartsWith("FFmpeg could not be run", video.Detail, StringComparison.Ordinal)
        End Using
    End Function

    ''' <summary>Audit 242 L-9: the runtime is started from exactly the path named, so a bare, root-relative or
    ''' drive-relative name (searched for along PATH and the current folder) is a programmer error.</summary>
    <Theory>
    <InlineData("ffmpeg.exe")>
    <InlineData("ffmpeg")>
    <InlineData("..\ffmpeg.exe")>
    <InlineData("\ffmpeg.exe")>
    <InlineData("C:ffmpeg.exe")>
    <InlineData("")>
    <InlineData(" ")>
    Public Async Function A_runtime_path_that_is_not_fully_qualified_is_refused(exe As String) As Task
        Await Assert.ThrowsAsync(Of ArgumentException)(
            Function() AnimationVideoEncoder.RunAsync(exe, "-version", 0, 1024L, False, Nothing, CancellationToken.None))
    End Function

    ''' <summary>Audit 242 M-2: a process that has ended while something it started still holds the output pipe. The
    ''' runner gives up on the pipe after the drain wait, returns a failure without a video, and does not hang. (The
    ''' capture is abandoned, not wiped under the still-pending read; the pump wipes it when the pipe finally closes.)</summary>
    <Fact>
    Public Async Function A_pipe_still_held_after_the_process_ended_is_a_bounded_failure_without_a_video() As Task
        Dim pidFile As String = NewPidFile()
        Try
            Dim lines As New List(Of String)(PublishPids(pidFile))
            lines.Add("$o.Write($blk,0,$blk.Length);$o.Flush()")
            lines.Add("[Console]::Error.WriteLine('frame=1')")
            ' The script ends here: the stand-in is gone, its child (ping) still holds the inherited stdout pipe.

            Dim watch As Stopwatch = Stopwatch.StartNew()
            Using video As EncodedVideo = Await RunAsync(ScriptArguments(lines.ToArray()), 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None, drainWaitMs:=1500)
                Assert.Equal(AnimationVideoOutcome.EncoderFailed, video.Outcome)
                Assert.Contains("did not close", video.Detail, StringComparison.Ordinal)
                Assert.True(video.Bytes.IsEmpty)
                Assert.Equal(0, video.Length)
            End Using
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), "the runner waited for the held pipe")
        Finally
            KillRecorded(pidFile)
            DeleteQuietly(pidFile)
        End Try
    End Function

    <Fact>
    Public Async Function A_clean_exit_with_only_a_header_is_empty_output() As Task
        Dim args As String = ScriptArguments(
            "$o.Write($blk,0,10);$o.Flush()",
            "[Console]::Error.WriteLine('frame=0')")

        Using video As EncodedVideo = Await RunAsync(args, 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.EmptyOutput, video.Outcome)
            Assert.Equal(0, video.ExitCode)
            Assert.True(video.Bytes.IsEmpty)
        End Using
    End Function

    <Fact>
    Public Async Function A_clean_exit_with_no_output_at_all_is_empty_output() As Task
        Using video As EncodedVideo = Await RunAsync(ScriptArguments("exit 0"), 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None)
            Assert.Equal(AnimationVideoOutcome.EmptyOutput, video.Outcome)
        End Using
    End Function

    <Fact>
    Public Async Function A_ceiling_below_one_is_refused() As Task
        Await Assert.ThrowsAsync(Of ArgumentOutOfRangeException)(
            Function() AnimationVideoEncoder.RunAsync(PowerShellExe(), "-NoProfile -Command exit", 0, 0L, False, Nothing, CancellationToken.None))
    End Function

    ' --- priority -----------------------------------------------------------------------------------

    <Fact>
    Public Async Function Below_normal_priority_is_applied_after_the_start_when_asked_for() As Task
        Dim priorityFile As String = NewPidFile()
        Try
            ' Give the runner time to set the class before the stand-in reads its own.
            Dim lines As String() = {
                "Start-Sleep -Milliseconds 700",
                "[IO.File]::WriteAllText(" & Literal(priorityFile) & ",[string][Diagnostics.Process]::GetCurrentProcess().PriorityClass)",
                "$o.Write($blk,0,$blk.Length);$o.Flush()",
                "[Console]::Error.WriteLine('frame=1')"
            }
            Using video As EncodedVideo = Await RunAsync(ScriptArguments(lines), 0, AnimationVideoPlan.Video_Max_Bytes, True, Nothing, CancellationToken.None)
                Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
            End Using
            Assert.Equal("BelowNormal", File.ReadAllText(priorityFile).Trim())

            ' Asked not to, the child keeps whatever class it inherits from this process.
            Using video As EncodedVideo = Await RunAsync(ScriptArguments(lines), 0, AnimationVideoPlan.Video_Max_Bytes, False, Nothing, CancellationToken.None)
                Assert.Equal(AnimationVideoOutcome.Succeeded, video.Outcome)
            End Using
            Using self As Process = Process.GetCurrentProcess()
                Assert.Equal(self.PriorityClass.ToString(), File.ReadAllText(priorityFile).Trim())
            End Using
        Finally
            DeleteQuietly(priorityFile)
        End Try
    End Function

End Class
#End If
