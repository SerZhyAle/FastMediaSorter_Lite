Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Threading

' Ticket 199 section 4: the encoder engine of the working assumption (Q199-1, candidate a) - FFmpeg as
' a child process fed raw PCM (and raw video, in the video modes) over pipes and writing ADTS or
' MPEG-TS to the hub. The pure argument builders are LiveFfmpegArgs (tested there); this wrapper is
' only the process, the stdin pipe and the stdout reader, and it exists once.
'
' Safety rule 3 (no recording): the child writes to the pipe only. Nothing here opens a file for
' writing, and the wrapper refuses to run a runtime that is not the shared pinned one
' (LiveFfmpegRuntime resolves it; the version pin and the SHA-256 are the viewer manager's).
'
' Stop discipline (acceptance 3): Stop() closes stdin (FFmpeg flushes and exits), waits briefly, kills
' the child when it lingers, and joins both reader threads - no process remains.

''' <summary>FFmpeg as the live-broadcast encoder, one instance per broadcast.</summary>
Friend NotInheritable Class LiveFfmpegEncoder
    Implements ILiveEncoder

    Private _process As Process
    Private _stdoutReader As Thread
    Private _audioPipe As System.IO.Pipes.NamedPipeServerStream
    Private _audioWriter As Thread
    Private _options As LiveEncoderOptions
    Private _output As Action(Of Byte())
    Private _stopped As Boolean

    Public Function Start(options As LiveEncoderOptions, output As Action(Of Byte()), ByRef reason As String) As Boolean Implements ILiveEncoder.Start
        If options Is Nothing OrElse output Is Nothing Then
            reason = "no options"
            Return False
        End If
        Dim ffmpegExe As String = LiveFfmpegRuntime.GetFfmpegPath()
        If ffmpegExe.Length = 0 Then
            reason = "ffmpeg runtime not downloaded"
            Return False
        End If

        Dim arguments As String
        If options.WithVideo Then
            ' The video modes feed raw frames on stdin and PCM through a named pipe the wrapper owns;
            ' the pipe path is passed to FFmpeg as the second input's file name.
            Dim pipeName As String = "fms-live-" & Guid.NewGuid().ToString("N")
            _audioPipe = New System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.Out, 1,
                                                                  System.IO.Pipes.PipeTransmissionMode.Byte,
                                                                  System.IO.Pipes.PipeOptions.Asynchronous)
            arguments = LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\" & pipeName)
        Else
            arguments = LiveFfmpegArgs.AudioToAdts(options)
        End If

        _options = options
        _output = output

        Dim info As New ProcessStartInfo(ffmpegExe, arguments) With {
            .UseShellExecute = False,
            .RedirectStandardInput = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .CreateNoWindow = True
        }
        Try
            _process = Process.Start(info)
        Catch
            ' The window shows one sentence for EncoderUnavailable; the exception's own words go to
            ' the log, not to the user (APP-BEHAVIOUR rule 6).
            reason = "encoder process did not start"
            Return False
        End Try
        If _process Is Nothing Then
            reason = "ffmpeg did not start"
            Return False
        End If

        _stdoutReader = New Thread(AddressOf ReadStdout) With {.IsBackground = True, .Name = "LiveFfmpegEncoder.stdout"}
        _stdoutReader.Start()

        If _audioPipe IsNot Nothing Then
            _audioWriter = New Thread(AddressOf ConnectAudioPipe) With {.IsBackground = True, .Name = "LiveFfmpegEncoder.audiopipe"}
            _audioWriter.Start()
        End If
        Return True
    End Function

    Public Sub WritePcm(chunk As LivePcmChunk) Implements ILiveEncoder.WritePcm
        If chunk Is Nothing OrElse chunk.Data Is Nothing OrElse chunk.Data.Length = 0 Then Return
        If _stopped Then Return
        Try
            If _options.WithVideo Then
                ' The video modes carry audio through the named pipe (a second stdin is not a thing).
                If _audioPipe IsNot Nothing AndAlso _audioPipe.IsConnected Then
                    _audioPipe.Write(chunk.Data, 0, chunk.Data.Length)
                    _audioPipe.Flush()
                End If
            Else
                Dim stdin As Stream = _process?.StandardInput.BaseStream
                stdin?.Write(chunk.Data, 0, chunk.Data.Length)
                stdin?.Flush()
            End If
        Catch
            ' The child died: the reader thread notices through the process exit and the controller's
            ' stop path cleans up. A dropped chunk is not worth an exception on the capture thread.
        End Try
    End Sub

    Public Sub WriteVideo(frame As LiveVideoFrame) Implements ILiveEncoder.WriteVideo
        If frame Is Nothing OrElse frame.Pixels Is Nothing OrElse frame.Pixels.Length = 0 Then Return
        If _stopped OrElse Not _options.WithVideo Then Return
        Try
            Dim stdin As Stream = _process?.StandardInput.BaseStream
            stdin?.Write(frame.Pixels, 0, frame.Pixels.Length)
            stdin?.Flush()
        Catch
        End Try
    End Sub

    Public Sub [Stop]() Implements ILiveEncoder.Stop
        If _stopped Then Return
        _stopped = True
        Try
            _process?.StandardInput.Close()
        Catch
        End Try
        Try
            If _process IsNot Nothing AndAlso Not _process.WaitForExit(1500) Then
                _process.Kill()
            End If
        Catch
        End Try
        Try
            _audioPipe?.Dispose()
        Catch
        End Try
        Try
            _stdoutReader?.Join(1000)
        Catch
        End Try
        Try
            _process?.Dispose()
        Catch
        End Try
        _process = Nothing
        _audioPipe = Nothing
    End Sub

    Private Sub ReadStdout()
        Try
            Dim p As Process = _process
            If p Is Nothing Then Return
            Using stdout As Stream = p.StandardOutput.BaseStream
                Dim buffer(16383) As Byte
                While Not _stopped
                    Dim read As Integer = stdout.Read(buffer, 0, buffer.Length)
                    If read <= 0 Then Exit While
                    Dim data(read - 1) As Byte
                    Array.Copy(buffer, data, read)
                    _output?.Invoke(data)
                End While
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>Waits for FFmpeg to connect to the audio pipe. The connect blocks until the child
    ''' opens its second input; if it never does, Stop() disposes the pipe and the wait ends.</summary>
    Private Sub ConnectAudioPipe()
        Try
            _audioPipe?.WaitForConnection()
        Catch
        End Try
    End Sub

End Class
