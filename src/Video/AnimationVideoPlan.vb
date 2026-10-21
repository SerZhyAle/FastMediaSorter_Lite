#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization

''' <summary>
''' How an animation becomes the TEMPORARY video of spec 242, and what a finished run means.
'''
''' Pure - no process, no disk, no UI - for the same reason <see cref="VideoConvertPlan"/> is: the exact
''' argument line is the part that is expensive to get wrong and impossible to see (spec 242 rule 12), and
''' rule 25 ("no plaintext video byte reaches the disk") is a property of THIS string, so a test states it
''' instead of a comment keeping it true.
'''
''' The permanent "Replace with video" keeps <see cref="VideoConvertPlan"/> untouched (medium, crf 20, a file
''' with +faststart). This module is its sibling for the other route: the same encoder, a different
''' destination (stdout), a faster preset, a size cap, and a flattened background for transparency.
''' </summary>
Friend Module AnimationVideoPlan

    ''' <summary>Spec 242 rule 14: the captured video is at most this large; the process tree is killed beyond it.</summary>
    Friend Const Video_Max_Bytes As Long = 256L * 1024L * 1024L

    ''' <summary>Spec 242 rule 12 / Q242-8, confirmed by the T-0 measurements: speed matters here, size does not.</summary>
    Friend Const Video_Crf As Integer = 22
    Friend Const Video_Preset As String = "veryfast"

    ''' <summary>Spec 242 rule 12: a bigger animation is downscaled (never upscaled, aspect kept) to fit this box.</summary>
    Friend Const Max_Width As Integer = 3840
    Friend Const Max_Height As Integer = 2160

    ''' <summary>The literal that names FFmpeg's stdout. Spec 242 rule 25: the ONLY output argument.</summary>
    Friend Const Output_Pipe As String = "pipe:1"

    ''' <summary>Downscale only, aspect kept, both sides even (H.264 yuv420p refuses an odd size). The fit-in box is
    ''' the source's own size capped at <see cref="Max_Width"/> x <see cref="Max_Height"/>, so a small source is never
    ''' enlarged.</summary>
    Private ReadOnly Scale_Chain As String =
        "scale=w='min(" & Max_Width.ToString(CultureInfo.InvariantCulture) & ",iw)':h='min(" & Max_Height.ToString(CultureInfo.InvariantCulture) &
        ",ih)':force_original_aspect_ratio=decrease:force_divisible_by=2"

    ''' <summary>
    ''' The command line (spec 242 rule 12). Every flag earns its place:
    '''   -nostdin / -xerror     as in <see cref="VideoConvertPlan.BuildArguments"/> (a decode error ends the run with
    '''                          a non-zero exit instead of a quietly shorter video);
    '''   -progress pipe:2       the key=value blocks go to STDERR, because stdout carries the video;
    '''   -nostats               having asked for progress, no second status line;
    '''   -loglevel error        stderr is then progress blocks plus real errors, nothing else;
    '''   -protocol_whitelist file   the INPUT may only be a local or UNC file: whatever a container inside the
    '''                          source names (a playlist, a concat or subfile reference) is refused, not followed;
    '''   -an                    the sources have no audio;
    '''   the filter             plain: the capped even-size scale and yuv420p; with alpha (rule 9): the animation
    '''                          is composited over a solid colour the size of its canvas, because H.264 has no alpha;
    '''   -fps_mode cfr -r       a constant frame rate, from <see cref="VideoConvertPlan.Fps"/>;
    '''   libx264 high           the profile every player understands, <c>veryfast</c> at crf 22;
    '''   frag_keyframe+empty_moov+default_base_moof -f mp4 pipe:1
    '''                          a fragmented MP4 is a complete, valid file that needs no seekable output; there is no
    '''                          output PATH anywhere in this string (rule 25).
    ''' </summary>
    ''' <param name="sourcePath">The animation, opened in place (rule 11). Must be a FULLY QUALIFIED path (a drive path
    ''' <c>C:\..</c>, a UNC path <c>\\server\share\..</c> or a <c>\\?\</c> path): FFmpeg takes a bare string that is
    ''' not one as a URL (<c>concat:a|b</c>, <c>subfile,..</c>, <c>http://..</c>) or resolves it against its own
    ''' folder, and a name that starts with a dash is easy to mistake for an option. Refused with an
    ''' <see cref="ArgumentException"/>, never rewritten.</param>
    ''' <param name="fps">The output rate; clamped to the same range as <see cref="VideoConvertPlan.Fps"/>.</param>
    ''' <param name="hasAlpha">True when the source declares transparency (rule 9).</param>
    ''' <param name="canvasWidth">The source canvas; read only when <paramref name="hasAlpha"/>, and then it must be positive.</param>
    ''' <param name="canvasHeight">See <paramref name="canvasWidth"/>.</param>
    ''' <param name="backgroundRgb">The viewer's BackColor as 0xRRGGBB (the high byte is ignored); read only with alpha.</param>
    Friend Function BuildArguments(sourcePath As String, fps As Integer, hasAlpha As Boolean,
                                   canvasWidth As Integer, canvasHeight As Integer,
                                   backgroundRgb As Integer) As String
        If String.IsNullOrEmpty(sourcePath) Then Throw New ArgumentException("The source path is empty.", NameOf(sourcePath))
        ' Rooted is not enough: "\x" (the current drive's root) and "C:x" (the drive's current folder) are rooted
        ' and still depend on a folder FFmpeg is started in. Fully qualified names exactly one file (audit 242 L-9).
        If Not System.IO.Path.IsPathFullyQualified(sourcePath) Then
            Throw New ArgumentException("The source path must be fully qualified.", NameOf(sourcePath))
        End If

        Dim rate As String = Math.Max(VideoConvertPlan.Min_Fps, Math.Min(VideoConvertPlan.Max_Fps, fps)).ToString(CultureInfo.InvariantCulture)

        Dim filter As String
        If hasAlpha Then
            If canvasWidth <= 0 OrElse canvasHeight <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(canvasWidth), "A transparent source needs its canvas size to be composited.")
            End If
            filter = " -filter_complex " & Utils.QuoteArgument(
                "color=c=" & FormatBackground(backgroundRgb) &
                ":s=" & canvasWidth.ToString(CultureInfo.InvariantCulture) & "x" & canvasHeight.ToString(CultureInfo.InvariantCulture) &
                ":r=" & rate &
                "[bg];[bg][0:v]overlay=shortest=1:format=auto," & Scale_Chain & ",format=yuv420p")
        Else
            filter = " -vf " & Utils.QuoteArgument(Scale_Chain & ",format=yuv420p")
        End If

        Return "-hide_banner -nostdin -y -xerror -progress pipe:2 -nostats -loglevel error" &
               " -protocol_whitelist file -i " & Utils.QuoteArgument(sourcePath) &
               " -an" &
               filter &
               " -fps_mode cfr -r " & rate &
               " -c:v libx264 -profile:v high -preset " & Video_Preset & " -crf " & Video_Crf.ToString(CultureInfo.InvariantCulture) &
               " -movflags frag_keyframe+empty_moov+default_base_moof -f mp4 " & Output_Pipe
    End Function

    ''' <summary>The colour FFmpeg's <c>color</c> source takes: <c>0x202020</c>. Each channel is clamped to 0..255.</summary>
    Friend Function FormatBackground(r As Integer, g As Integer, b As Integer) As String
        Dim packed As Integer = (Channel(r) << 16) Or (Channel(g) << 8) Or Channel(b)
        Return "0x" & packed.ToString("X6", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Same, from a packed 0xRRGGBB (<c>Color.ToArgb() And &amp;HFFFFFF</c>); the alpha byte is dropped.</summary>
    Friend Function FormatBackground(rgb As Integer) As String
        Return "0x" & (rgb And &HFFFFFF).ToString("X6", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>
    ''' What a finished run means. Success needs ALL of: not cancelled, not killed at the ceiling or the deadline,
    ''' the output stream read to its end, exit code 0, and a stream that is more than a header with at least one
    ''' frame behind it.
    '''
    ''' Deliberately NOT <see cref="VideoConvertPlan.DecideEncode"/>'s duration tolerance. That check guards the
    ''' deletion of an ORIGINAL, which this route never does (spec 242 rule 26), and FFmpeg reads a GIF's duration
    ''' differently from the header facts the caller has (057.S09-1), so applying it here would throw away
    ''' complete videos. A truncated source is caught where it matters: <c>-xerror</c> makes a decode error exit
    ''' non-zero.
    ''' </summary>
    ''' <param name="captureFailed">Reading the output stream failed before its end (the bytes are not whole).</param>
    ''' <param name="framesWritten">The last <c>frame=</c> of the progress stream, or -1 when none arrived (then the
    ''' byte count alone decides).</param>
    Friend Function DecideRun(cancelled As Boolean, timedOut As Boolean, ceilingExceeded As Boolean,
                              captureFailed As Boolean, exitCode As Integer,
                              bytesProduced As Long, framesWritten As Long) As AnimationVideoOutcome
        If cancelled Then Return AnimationVideoOutcome.Cancelled
        If ceilingExceeded Then Return AnimationVideoOutcome.OutputTooLarge
        If timedOut Then Return AnimationVideoOutcome.TimedOut
        If captureFailed OrElse exitCode <> 0 Then Return AnimationVideoOutcome.EncoderFailed
        If bytesProduced < VideoConvertPlan.Min_Output_Bytes OrElse framesWritten = 0 Then Return AnimationVideoOutcome.EmptyOutput
        Return AnimationVideoOutcome.Succeeded
    End Function

    ''' <summary>
    ''' True for a line of the <c>-progress</c> block (<c>frame=</c>, <c>out_time=</c>, <c>speed=</c>, ..). With the
    ''' progress on stderr (rule 12) the last lines of stderr are ALWAYS these, so the failure reason has to be taken
    ''' from the lines that are not.
    ''' </summary>
    Friend Function IsProgressLine(line As String) As Boolean
        If String.IsNullOrEmpty(line) Then Return False
        Dim equals As Integer = line.IndexOf("="c)
        If equals <= 0 Then Return False
        Dim key As String = line.Substring(0, equals)
        If key.StartsWith("stream_", StringComparison.Ordinal) AndAlso key.EndsWith("_q", StringComparison.Ordinal) Then Return True
        For Each known As String In Progress_Keys
            If String.Equals(key, known, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    Private ReadOnly Progress_Keys As String() = {
        "frame", "fps", "bitrate", "total_size", "out_time_us", "out_time_ms", "out_time",
        "dup_frames", "drop_frames", "speed", "progress"
    }

    Private Function Channel(value As Integer) As Integer
        Return Math.Max(0, Math.Min(255, value))
    End Function

End Module

''' <summary>
''' How one encode of the temporary video ended (spec 242 rules 14, 23). One value per line because the caller logs
''' which, and because only <see cref="Succeeded"/> carries a video; every other value means "release what was made
''' and fall back to spec 241's route".
''' </summary>
Friend Enum AnimationVideoOutcome
    ''' <summary>A complete video was captured.</summary>
    Succeeded
    ''' <summary>The caller cancelled (the user landed on another file, the setting went off, the viewer is closing).</summary>
    Cancelled
    ''' <summary>The wall-clock ceiling (10 minutes, <see cref="VideoConvertPlan.Timeout_Ms"/>) killed it.</summary>
    TimedOut
    ''' <summary>The output grew past <see cref="AnimationVideoPlan.Video_Max_Bytes"/> (rule 14) and the process tree was killed.</summary>
    OutputTooLarge
    ''' <summary>FFmpeg could not be started, exited non-zero, or its output could not be read to the end.</summary>
    EncoderFailed
    ''' <summary>Exit 0 and nothing usable: a header and no frames.</summary>
    EmptyOutput
End Enum
#End If
