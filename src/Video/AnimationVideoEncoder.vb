#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Runs FFmpeg for the TEMPORARY video of spec 242 and captures what it writes to STDOUT.
'''
''' The argument line comes from <see cref="AnimationVideoPlan.BuildArguments"/>; what is left here is the
''' process, and the things a process can do wrong:
'''   * write too much - the moment the captured bytes pass the ceiling the process TREE is killed (rule 14);
'''   * never finish - the same ten-minute ceiling as the permanent conversion, killed at the deadline (rule 14);
'''   * be cancelled - the caller landed elsewhere, the setting went off, the viewer is closing (rule 24): the tree
'''     is killed, nothing is finished "for the cache" (a deliberate difference from spec 241 rule 11);
'''   * fail quietly - the non-progress tail of stderr is kept, so the failure names a reason.
'''
''' This module NEVER writes a file (rule 25): the video exists only as the growing buffer behind
''' <see cref="EncodedVideo.Bytes"/>, which the sealing writer reads directly. It does not log either - the caller
''' does, with the original's path - which is also what lets this file be linked into the test project.
''' </summary>
Friend Module AnimationVideoEncoder

    ''' <summary>The first allocation. The measured videos are 3 to 12.5 MB (T-0), so most runs never grow; a bigger one
    ''' doubles, which costs one transient second copy while the old buffer is wiped.</summary>
    Private Const Initial_Capacity As Integer = 16 * 1024 * 1024

    ''' <summary>Room kept above the ceiling so the read that crosses it still has somewhere to land.</summary>
    Private Const Capacity_Slack As Integer = 64 * 1024

    ''' <summary>One read asks for at most this much, so the ceiling is noticed within a read of being crossed.</summary>
    Private Const Max_Read_Chunk As Integer = 256 * 1024

    ''' <summary>A ceiling above this cannot be held in one array; nothing asks for it (the product's is 256 MB).</summary>
    Private Const Max_Supported_Bytes As Long = 1500L * 1024L * 1024L

    ''' <summary>How much non-progress stderr is kept; FFmpeg's real reason is always in the last few lines.</summary>
    Private Const Stderr_Tail_Chars As Integer = 2000

    ''' <summary>How long after the process ended the two pipes may take to report their end. They close with the
    ''' process, so this only matters if something it started still holds them.</summary>
    Private Const Drain_Wait_Ms As Integer = 15000

    ''' <summary>How long to wait for a killed process to be gone before giving up on it.</summary>
    Private Const Kill_Wait_Ms As Integer = 5000

    ''' <summary>
    ''' Starts FFmpeg with stdout and stderr redirected and returns when it has ended and both pipes are drained.
    ''' </summary>
    ''' <param name="ffmpegExe">The runtime <c>GetFfmpegPath()</c> returned (rule 11); nothing found elsewhere.</param>
    ''' <param name="args"><see cref="AnimationVideoPlan.BuildArguments"/>' result.</param>
    ''' <param name="expectedDurationMs">The source's duration, for the percentage; 0 means unknown and the caller
    ''' shows an indeterminate bar rather than a made-up number.</param>
    ''' <param name="maxBytes">The output ceiling (<see cref="AnimationVideoPlan.Video_Max_Bytes"/> in production).</param>
    ''' <param name="belowNormalPriority">Rule 24: the prefetch job runs below normal so the video on screen is not starved.
    ''' Set after the start; a refusal is ignored.</param>
    ''' <param name="progress">Percent 0..99, only ever increasing. It does not report 100: the bar is full when
    ''' the caller has the result.</param>
    ''' <param name="timeoutMs">The wall-clock ceiling; a parameter only so a test can use a second.</param>
    ''' <param name="drainWaitMs">How long the two pipes may take to report their end once the process is gone; a
    ''' parameter only so a test does not wait the full <c>Drain_Wait_Ms</c>.</param>
    Friend Async Function RunAsync(ffmpegExe As String,
                                   args As String,
                                   expectedDurationMs As Integer,
                                   maxBytes As Long,
                                   belowNormalPriority As Boolean,
                                   progress As IProgress(Of Integer),
                                   token As CancellationToken,
                                   Optional timeoutMs As Integer = VideoConvertPlan.Timeout_Ms,
                                   Optional drainWaitMs As Integer = Drain_Wait_Ms) As Task(Of EncodedVideo)
        If maxBytes <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(maxBytes))
        ' The runtime is started from, and given as its working folder, exactly the path named: a bare or drive-relative
        ' name would be searched for along PATH and the current folder (audit 242 L-9). Programmer error, not a result.
        If String.IsNullOrWhiteSpace(ffmpegExe) OrElse Not Path.IsPathFullyQualified(ffmpegExe) Then
            Throw New ArgumentException("The FFmpeg runtime path must be fully qualified.", NameOf(ffmpegExe))
        End If
        If token.IsCancellationRequested Then
            Return New EncodedVideo(AnimationVideoOutcome.Cancelled, Nothing, 0, 0, -1, -1, -1, "Cancelled before FFmpeg was started.")
        End If

        Dim capture As New CaptureState(Math.Min(maxBytes, Max_Supported_Bytes))
        Dim cancelled As Boolean = False
        Dim timedOut As Boolean = False
        Dim exitCode As Integer = -1
        Dim failure As String = ""

        Try
            Dim info As New ProcessStartInfo(ffmpegExe, args) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardInput = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardErrorEncoding = Encoding.UTF8,
                .WorkingDirectory = If(Path.GetDirectoryName(ffmpegExe), String.Empty)
            }
            ' No StandardOutputEncoding on purpose: stdout is the video and is read as bytes from BaseStream.

            Using proc As New Process()
                proc.StartInfo = info
                proc.Start()
                Try
                    ' Nothing is ever sent; a child that reads stdin sees the end at once.
                    proc.StandardInput.Close()
                Catch
                End Try
                If belowNormalPriority Then
                    Try
                        proc.PriorityClass = ProcessPriorityClass.BelowNormal
                    Catch
                    End Try
                End If

                Try
                    ' Dedicated threads: the reads block for the whole encode and must not eat pool threads.
                    Dim stdoutTask As Task = Task.Factory.StartNew(Sub() PumpStdout(proc, capture),
                                                                   CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                    Dim stderrTask As Task = Task.Factory.StartNew(Sub() PumpStderr(proc, capture, expectedDurationMs, progress),
                                                                   CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)

                    ' One clock for both ways out: the caller's cancel and the hard ceiling. Killing is the only
                    ' way to stop an encoder mid-stream.
                    Using deadline As New CancellationTokenSource(timeoutMs)
                        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token)
                            Dim stopped As Boolean = False
                            Try
                                Await proc.WaitForExitAsync(linked.Token).ConfigureAwait(False)
                            Catch ex As OperationCanceledException
                                stopped = True
                                cancelled = token.IsCancellationRequested
                                timedOut = Not cancelled
                                KillQuietly(proc)
                            End Try
                            ' (Await is not allowed inside the Catch itself.)
                            If stopped Then Await WaitAfterKillAsync(proc).ConfigureAwait(False)
                        End Using
                    End Using

                    Try
                        Await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromMilliseconds(drainWaitMs)).ConfigureAwait(False)
                    Catch ex As TimeoutException
                        ' The stdout pump may still be inside its read, with the buffer in its hands: the failure path
                        ' below abandons the capture instead of wiping it under that read.
                        capture.ReadError = "The output streams did not close after the process ended."
                    End Try

                    If Not cancelled AndAlso Not timedOut AndAlso Not capture.CeilingExceeded AndAlso proc.HasExited Then exitCode = proc.ExitCode
                Finally
                    ' An exception on the way out must not leave the encoder running.
                    KillQuietly(proc)
                End Try
            End Using
        Catch ex As Exception
            failure = FailureText.Describe(ex)
        End Try

        Dim outcome As AnimationVideoOutcome = AnimationVideoPlan.DecideRun(
            cancelled, timedOut, capture.CeilingExceeded, failure.Length > 0 OrElse capture.ReadError.Length > 0,
            exitCode, capture.Count, capture.FramesWritten)
        Dim detail As String = Describe(outcome, failure, capture, exitCode, maxBytes, timeoutMs)

        If outcome = AnimationVideoOutcome.Succeeded Then
            Return New EncodedVideo(outcome, capture.Buffer, capture.Count, capture.Count, exitCode, capture.FramesWritten, capture.OutTimeMs, detail)
        End If

        ' Rule 27: a failed, cancelled or abandoned job keeps nothing, not even the half video in memory.
        ' When a pipe never closed, the stdout pump can still be inside its read: the capture is ABANDONED, not just
        ' wiped, so the pump wipes what lands after this point itself, on its way out (audit 242 M-2).
        Dim captured As Long = capture.Count
        capture.Abandon()
        Return New EncodedVideo(outcome, Nothing, 0, captured, exitCode, capture.FramesWritten, capture.OutTimeMs, detail)
    End Function

    ''' <summary>Reads stdout to its end into the growing buffer. Reads straight into the buffer, so there is no
    ''' second full-size copy at the end: the result is a slice of this array.</summary>
    Private Sub PumpStdout(proc As Process, capture As CaptureState)
        Try
            Dim stream As Stream = proc.StandardOutput.BaseStream
            Do
                ' False: the caller gave up on this capture (a pipe that never closed). Nothing more is taken.
                If Not capture.EnsureSpace() Then Exit Do
                Dim target As Byte() = capture.Buffer
                Dim at As Integer = capture.Count
                Dim room As Integer = Math.Min(target.Length - at, Max_Read_Chunk)
                Dim read As Integer = stream.Read(target, at, room)
                If read <= 0 Then Exit Do
                If Not capture.Commit(at, read) Then Exit Do
                If capture.Count > capture.MaxBytes Then
                    capture.CeilingExceeded = True
                    KillQuietly(proc)
                    Exit Do
                End If
            Loop
        Catch ex As Exception
            ' The pipe went away or the buffer could not grow. Nothing more can be captured, and an encoder blocked
            ' on a full stdout would only run to the deadline, so end it now.
            capture.ReadError = FailureText.Describe(ex)
            KillQuietly(proc)
        Finally
            ' An abandoned capture is wiped by the one party that can know no read is left in it: this thread, on exit.
            capture.WipeIfAbandoned()
        End Try
    End Sub

    ''' <summary>Reads stderr line by line: the -progress blocks feed the counts and the percentage, everything else
    ''' is the failure text. Reads to the end whatever happens to one line - a full stderr pipe would stall the encoder.</summary>
    Private Sub PumpStderr(proc As Process, capture As CaptureState, expectedDurationMs As Integer, progress As IProgress(Of Integer))
        Dim lastPercent As Integer = -1
        Try
            Dim reader As StreamReader = proc.StandardError
            Dim line As String = reader.ReadLine()
            While line IsNot Nothing
                Try
                    ' The counts the verdict and the log need; the last block wins.
                    Dim frames As Long = VideoConvertPlan.ParseProgressValue(line, "frame")
                    If frames >= 0 Then capture.FramesWritten = frames
                    Dim outTime As Long = VideoConvertPlan.ParseProgressValue(line, "out_time")
                    If outTime >= 0 Then capture.OutTimeMs = outTime

                    If progress IsNot Nothing Then
                        Dim percent As Integer = AnimationToVideo.ParseProgressPercent(line, expectedDurationMs)
                        If percent > lastPercent Then
                            lastPercent = percent
                            progress.Report(percent)
                        End If
                    End If

                    If Not AnimationVideoPlan.IsProgressLine(line) AndAlso line.Trim().Length > 0 Then capture.AppendTail(line)
                Catch
                    ' A progress handler that throws must not stop the reading.
                End Try
                line = reader.ReadLine()
            End While
        Catch
            ' The pipe was closed under us (the process was killed). The verdict never depends on stderr being whole.
        End Try
    End Sub

    Private Sub KillQuietly(proc As Process)
        Try
            If Not proc.HasExited Then proc.Kill(entireProcessTree:=True)
        Catch
        End Try
    End Sub

    ''' <summary>Waits, bounded, until a killed process is really gone, so no handle remains when the caller
    ''' returns (and a container written next is not racing it).</summary>
    Private Async Function WaitAfterKillAsync(proc As Process) As Task
        Try
            Using limit As New CancellationTokenSource(Kill_Wait_Ms)
                Await proc.WaitForExitAsync(limit.Token).ConfigureAwait(False)
            End Using
        Catch
        End Try
    End Function

    ''' <summary>The English reason for the log (rule 28: no credential, no container name - this module has neither).</summary>
    Private Function Describe(outcome As AnimationVideoOutcome, failure As String, capture As CaptureState,
                              exitCode As Integer, maxBytes As Long, timeoutMs As Integer) As String
        Select Case outcome
            Case AnimationVideoOutcome.Succeeded
                Return ""
            Case AnimationVideoOutcome.Cancelled
                Return "Cancelled."
            Case AnimationVideoOutcome.TimedOut
                Return "FFmpeg exceeded the time limit of " & (timeoutMs \ 1000).ToString(CultureInfo.InvariantCulture) & " s."
            Case AnimationVideoOutcome.OutputTooLarge
                Return "The video exceeded the size limit of " & maxBytes.ToString(CultureInfo.InvariantCulture) & " bytes."
            Case AnimationVideoOutcome.EmptyOutput
                Return "FFmpeg produced no usable video (" & capture.Count.ToString(CultureInfo.InvariantCulture) &
                       " bytes, " & capture.FramesWritten.ToString(CultureInfo.InvariantCulture) & " frames)."
            Case Else
                If failure.Length > 0 Then Return "FFmpeg could not be run: " & failure
                Dim tail As String = capture.TailText()
                If capture.ReadError.Length > 0 Then
                    Return "The video could not be read from FFmpeg: " & capture.ReadError & If(tail.Length > 0, ": " & tail, "")
                End If
                Return "FFmpeg exited with code " & exitCode.ToString(CultureInfo.InvariantCulture) & If(tail.Length > 0, ": " & tail, "")
        End Select
    End Function

    ''' <summary>Everything the two reader threads share with the caller. A field per fact: each has one writer, and the
    ''' caller reads them only after both reader tasks have completed (a task's completion is the barrier).</summary>
    Private NotInheritable Class CaptureState
        Friend Buffer As Byte()
        Friend Count As Integer
        Friend ReadOnly MaxBytes As Long
        Friend CeilingExceeded As Boolean
        Friend ReadError As String = ""
        Friend FramesWritten As Long = -1
        Friend OutTimeMs As Long = -1
        Private ReadOnly tail As New StringBuilder()
        ''' <summary>Guards the hand-over between the stdout pump (the only writer of Buffer and Count) and the caller's
        ''' abandon: the pump commits a read, grows or wipes under it, the caller abandons under it.</summary>
        Private ReadOnly gate As New Object()
        Private abandoned As Boolean

        Friend Sub New(maxBytes As Long)
            Me.MaxBytes = maxBytes
            Buffer = New Byte(CInt(Math.Min(CLng(Initial_Capacity), maxBytes + Capacity_Slack)) - 1) {}
        End Sub

        ''' <summary>Doubles the buffer when it is full, never past the ceiling plus the slack. The old array is wiped:
        ''' it held video bytes. False when the capture was abandoned (nothing to grow for). Pump thread only.</summary>
        Friend Function EnsureSpace() As Boolean
            If Count < Buffer.Length Then Return Not abandoned
            Dim grown As Long = Math.Min(CLng(Buffer.Length) * 2L, MaxBytes + Capacity_Slack)
            If grown <= Buffer.Length Then Throw New InvalidOperationException("The capture buffer cannot grow.")
            Dim bigger As Byte() = New Byte(CInt(grown) - 1) {}
            SyncLock gate
                If abandoned Then Return False
                Buffer.AsSpan(0, Count).CopyTo(bigger)
                CryptographicOperations.ZeroMemory(Buffer.AsSpan(0, Count))
                Buffer = bigger
            End SyncLock
            Return True
        End Function

        ''' <summary>Takes the <paramref name="read"/> bytes the pump just read at <paramref name="at"/>. False when the
        ''' capture was abandoned meanwhile: the bytes are dropped (the pump's exit wipes them). Pump thread only.</summary>
        Friend Function Commit(at As Integer, read As Integer) As Boolean
            SyncLock gate
                If abandoned Then Return False
                Count = at + read
                Return True
            End SyncLock
        End Function

        ''' <summary>The caller is done with this capture and keeps nothing of it. What is in the buffer is wiped now; the
        ''' pump, which may still be inside a read into the buffer, is told to take nothing more and to wipe the whole
        ''' array when it comes out (<see cref="WipeIfAbandoned"/>). Safe whether or not the pump has ended.</summary>
        Friend Sub Abandon()
            SyncLock gate
                abandoned = True
                CryptographicOperations.ZeroMemory(Buffer.AsSpan(0, Math.Min(Count, Buffer.Length)))
            End SyncLock
        End Sub

        ''' <summary>Called by the pump as it ends: wipes the whole array when the capture was abandoned, which covers the
        ''' bytes that landed after <see cref="Abandon"/> ran.</summary>
        Friend Sub WipeIfAbandoned()
            SyncLock gate
                If abandoned Then CryptographicOperations.ZeroMemory(Buffer.AsSpan())
            End SyncLock
        End Sub

        Friend Sub AppendTail(line As String)
            tail.AppendLine(line.Trim())
            If tail.Length > Stderr_Tail_Chars * 2 Then tail.Remove(0, tail.Length - Stderr_Tail_Chars)
        End Sub

        ''' <summary>The last three non-empty error lines (the stream description above them is noise).</summary>
        Friend Function TailText() As String
            Dim lines As String() = tail.ToString().Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim from As Integer = Math.Max(0, lines.Length - 3)
            Dim text As String = String.Join(" | ", lines, from, lines.Length - from)
            Return If(text.Length > 600, text.Substring(text.Length - 600), text)
        End Function

    End Class

End Module

''' <summary>
''' What one encode produced. Only <see cref="AnimationVideoOutcome.Succeeded"/> carries a video: the buffer is the
''' encoder's own array, handed over without a second copy, so the writer can seal it where it lies (spec 242 rule 13).
''' It holds plaintext video, so <see cref="Dispose"/> overwrites it - call it once the container is sealed and read back.
''' </summary>
Friend NotInheritable Class EncodedVideo
    Implements IDisposable

    Private buffer As Byte()
    Private ReadOnly byteCount As Integer

    Friend Sub New(outcome As AnimationVideoOutcome, buffer As Byte(), length As Integer, bytesCaptured As Long,
                   exitCode As Integer, framesWritten As Long, outTimeMs As Long, detail As String)
        Me.Outcome = outcome
        Me.buffer = buffer
        Me.byteCount = If(buffer Is Nothing, 0, length)
        Me.BytesCaptured = bytesCaptured
        Me.ExitCode = exitCode
        Me.FramesWritten = framesWritten
        Me.OutTimeMs = outTimeMs
        Me.Detail = detail
    End Sub

    Public ReadOnly Property Outcome As AnimationVideoOutcome

    ''' <summary>The whole video as a fragmented MP4, or empty unless <see cref="Outcome"/> is Succeeded (or after Dispose).</summary>
    Public ReadOnly Property Bytes As ReadOnlyMemory(Of Byte)
        Get
            Return If(buffer Is Nothing, ReadOnlyMemory(Of Byte).Empty, New ReadOnlyMemory(Of Byte)(buffer, 0, byteCount))
        End Get
    End Property

    ''' <summary><c>Bytes.Length</c>.</summary>
    Public ReadOnly Property Length As Integer
        Get
            Return If(buffer Is Nothing, 0, byteCount)
        End Get
    End Property

    ''' <summary>How much FFmpeg wrote before the run ended, kept for the log even when the bytes were dropped.</summary>
    Public ReadOnly Property BytesCaptured As Long

    ''' <summary>The process' exit code; -1 when it was killed or never started.</summary>
    Public ReadOnly Property ExitCode As Integer

    ''' <summary>The last <c>frame=</c> of the progress stream, -1 when none arrived.</summary>
    Public ReadOnly Property FramesWritten As Long

    ''' <summary>The last <c>out_time=</c> of the progress stream in ms, -1 when none arrived.</summary>
    Public ReadOnly Property OutTimeMs As Long

    ''' <summary>The reason, in English, for the log; empty on success.</summary>
    Public ReadOnly Property Detail As String

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim held As Byte() = buffer
        buffer = Nothing
        If held IsNot Nothing Then CryptographicOperations.ZeroMemory(held.AsSpan(0, Math.Min(byteCount, held.Length)))
    End Sub
End Class
#End If
