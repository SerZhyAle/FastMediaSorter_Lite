#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Channels
Imports System.Threading.Tasks

''' <summary>One decoded picture and its original presentation timestamp. The session
''' owns the bitmap; its preview control borrows it until the next presentation.</summary>
Friend NotInheritable Class DecodedVideoFrame
    Implements IDisposable

    Friend ReadOnly Image As Bitmap
    Friend ReadOnly TimeMs As Long

    Friend Sub New(image As Bitmap, timeMs As Long)
        Me.Image = image
        Me.TimeMs = timeMs
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Image.Dispose()
    End Sub
End Class

''' <summary>Bounded streaming decode: PNG frames on stdout, original PTS on stderr.
''' The pipe and two-frame queue apply backpressure while the viewer holds a frame.
''' No decoded pictures or temporary video are written to disk.</summary>
Friend NotInheritable Class VideoFrameDecoder
    Implements IDisposable

    Private ReadOnly worker As Process
    Private ReadOnly stopping As New CancellationTokenSource()
    Private ReadOnly frames As Channel(Of DecodedVideoFrame) = Channel.CreateBounded(Of DecodedVideoFrame)(2)
    Private ReadOnly timestamps As Channel(Of Long) = Channel.CreateUnbounded(Of Long)()
    Private ReadOnly completed As Task
    Private disposed As Integer
    Private errorTail As String = ""
    Private Shared ReadOnly TimestampPattern As New Regex("\bn:\s*\d+\s+pts:.*?\bpts_time:(?<time>[-+\d.eE]+)", RegexOptions.CultureInvariant)
    Private Const MaxPngBytes As Integer = 128 * 1024 * 1024

    Friend Sub New(executable As String, sourcePath As String, startMs As Long)
        If Not Path.IsPathFullyQualified(executable) OrElse Not Path.IsPathFullyQualified(sourcePath) Then
            Throw New ArgumentException("Frame decoding requires absolute local paths.")
        End If
        Dim info As New ProcessStartInfo(executable) With {
            .UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True}
        For Each argument As String In {"-hide_banner", "-loglevel", "info", "-nostdin",
                                       "-ss", (Math.Max(0, startMs) / 1000.0R).ToString("0.###", CultureInfo.InvariantCulture),
                                       "-copyts", "-protocol_whitelist", "file,pipe", "-threads", "2", "-i", sourcePath,
                                       "-map", "0:v:0", "-an", "-sn", "-dn",
                                       "-filter_threads", "1", "-vf", "showinfo",
                                       "-fps_mode", "passthrough", "-c:v", "png", "-threads", "1", "-f", "image2pipe", "pipe:1"}
            info.ArgumentList.Add(argument)
        Next
        worker = New Process With {.StartInfo = info}
        Try
            If Not worker.Start() Then Throw New IOException("FFmpeg did not start.")
            completed = Task.Run(AddressOf ReadFramesAsync)
        Catch
            worker.Dispose()
            stopping.Dispose()
            Throw
        End Try
    End Sub

    Friend Async Function ReadNextAsync(token As CancellationToken) As Task(Of DecodedVideoFrame)
        While Await frames.Reader.WaitToReadAsync(token).ConfigureAwait(False)
            Dim frame As DecodedVideoFrame = Nothing
            If frames.Reader.TryRead(frame) Then Return frame
        End While
        Return Nothing
    End Function

    Private Async Function ReadMetadataAsync() As Task
        Try
            While True
                Dim line As String = Await worker.StandardError.ReadLineAsync(stopping.Token).ConfigureAwait(False)
                If line Is Nothing Then Exit While
                Dim match As Match = TimestampPattern.Match(line)
                If match.Success Then
                    Dim seconds As Double
                    If Double.TryParse(match.Groups("time").Value, NumberStyles.Float, CultureInfo.InvariantCulture, seconds) AndAlso Double.IsFinite(seconds) Then
                        Await timestamps.Writer.WriteAsync(CLng(Math.Round(seconds * 1000)), stopping.Token).ConfigureAwait(False)
                    End If
                ElseIf line.Length > 0 Then
                    errorTail = line.Substring(Math.Max(0, line.Length - 600))
                End If
            End While
            timestamps.Writer.TryComplete()
        Catch ex As Exception
            timestamps.Writer.TryComplete(ex)
        End Try
    End Function

    Private Async Function ReadFramesAsync() As Task
        Dim metadata As Task = ReadMetadataAsync()
        Try
            While True
                Dim bytes As Byte() = Await ReadPngAsync(worker.StandardOutput.BaseStream, stopping.Token).ConfigureAwait(False)
                If bytes Is Nothing Then Exit While
                Dim timeMs As Long = Await timestamps.Reader.ReadAsync(stopping.Token).ConfigureAwait(False)
                Dim picture As Bitmap
                Using input As New MemoryStream(bytes, writable:=False), decoded As Image = Image.FromStream(input)
                    picture = New Bitmap(decoded)
                End Using
                Dim frame As New DecodedVideoFrame(picture, timeMs)
                Try
                    Await frames.Writer.WriteAsync(frame, stopping.Token).ConfigureAwait(False)
                Catch
                    frame.Dispose()
                    Throw
                End Try
            End While
            Await worker.WaitForExitAsync(stopping.Token).ConfigureAwait(False)
            Await metadata.ConfigureAwait(False)
            If worker.ExitCode <> 0 Then Throw New IOException("FFmpeg frame decoding failed: " & errorTail)
            frames.Writer.TryComplete()
        Catch ex As Exception
            frames.Writer.TryComplete(ex)
        Finally
            If Volatile.Read(disposed) <> 0 Then DrainFrames()
        End Try
    End Function

    ''' <summary>PNG chunk boundaries frame image2pipe without guessing at compressed
    ''' payload bytes. Both individual chunks and the complete image are bounded.</summary>
    Friend Shared Async Function ReadPngAsync(input As Stream, token As CancellationToken) As Task(Of Byte())
        Dim signature(7) As Byte
        If Not Await ReadExactlyAsync(input, signature, allowEnd:=True, token:=token).ConfigureAwait(False) Then Return Nothing
        Dim expected As Byte() = {137, 80, 78, 71, 13, 10, 26, 10}
        If Not signature.SequenceEqual(expected) Then Throw New InvalidDataException("Invalid frame PNG signature.")
        Using output As New MemoryStream()
            output.Write(signature)
            Do
                Dim header(7) As Byte
                Await ReadExactlyAsync(input, header, allowEnd:=False, token:=token).ConfigureAwait(False)
                Dim length As Long = CLng(header(0)) * 16777216L + CLng(header(1)) * 65536L + CLng(header(2)) * 256L + header(3)
                If length > MaxPngBytes OrElse output.Length + length + 12 > MaxPngBytes Then Throw New InvalidDataException("Frame PNG exceeds the memory limit.")
                output.Write(header)
                Dim chunk(CInt(length) + 3) As Byte
                Await ReadExactlyAsync(input, chunk, allowEnd:=False, token:=token).ConfigureAwait(False)
                output.Write(chunk)
                If header(4) = 73 AndAlso header(5) = 69 AndAlso header(6) = 78 AndAlso header(7) = 68 Then Return output.ToArray()
            Loop
        End Using
    End Function

    Private Shared Async Function ReadExactlyAsync(input As Stream, bytes As Byte(), allowEnd As Boolean, token As CancellationToken) As Task(Of Boolean)
        Dim offset As Integer = 0
        While offset < bytes.Length
            Dim count As Integer = Await input.ReadAsync(bytes.AsMemory(offset), token).ConfigureAwait(False)
            If count = 0 Then
                If allowEnd AndAlso offset = 0 Then Return False
                Throw New EndOfStreamException("A decoded PNG frame was truncated.")
            End If
            offset += count
        End While
        Return True
    End Function

    Private Sub DrainFrames()
        Dim frame As DecodedVideoFrame = Nothing
        While frames.Reader.TryRead(frame)
            frame.Dispose()
        End While
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(disposed, 1) <> 0 Then Return
        stopping.Cancel()
        Try
            If Not worker.HasExited Then worker.Kill()
            worker.WaitForExit(1000)
        Catch ex As InvalidOperationException
        Catch ex As ComponentModel.Win32Exception
        End Try
        DrainFrames()
        ' Readers finish cancellation before their streams and token source are released.
        completed.ContinueWith(Sub(finished)
                                   worker.Dispose()
                                   stopping.Dispose()
                               End Sub, TaskScheduler.Default)
    End Sub
End Class
#End If
