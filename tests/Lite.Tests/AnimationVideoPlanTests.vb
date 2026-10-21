#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The temporary video's argument line and the meaning of a finished run (spec 242 rules 9, 12, 14, 25).
'''
''' The line is the one T-0 measured on the pinned FFmpeg 9.0.1; the golden strings below are that measurement. The
''' structural tests read it back with Windows' own <c>CommandLineToArgvW</c>, because the dangerous failures are
''' the ones a string comparison cannot see: a path whose quoting swallows the next argument, and an output that
''' is a file (rule 25 - no plaintext video byte reaches the disk).
''' </summary>
Public Class AnimationVideoPlanTests

    Private Const Plain_Scale As String = "scale=w='min(3840,iw)':h='min(2160,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2"

    <DllImport("shell32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CommandLineToArgvW(commandLine As String, ByRef argumentCount As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function LocalFree(memory As IntPtr) As IntPtr
    End Function

    ''' <summary>What ffmpeg.exe would receive: the arguments after the program name, split by the rules Windows applies.</summary>
    Private Shared Function ArgvOf(arguments As String) As List(Of String)
        Dim count As Integer
        Dim argv As IntPtr = CommandLineToArgvW("ffmpeg.exe " & arguments, count)
        Assert.NotEqual(IntPtr.Zero, argv)
        Try
            Dim result As New List(Of String)()
            For i As Integer = 1 To count - 1
                result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)))
            Next
            Return result
        Finally
            LocalFree(argv)
        End Try
    End Function

    ' --- constants ---------------------------------------------------------------

    <Fact>
    Public Sub The_bounds_are_the_ones_the_specification_names()
        Assert.Equal(256L * 1024L * 1024L, AnimationVideoPlan.Video_Max_Bytes)
        Assert.Equal(22, AnimationVideoPlan.Video_Crf)
        Assert.Equal("veryfast", AnimationVideoPlan.Video_Preset)
        Assert.Equal(3840, AnimationVideoPlan.Max_Width)
        Assert.Equal(2160, AnimationVideoPlan.Max_Height)
        Assert.Equal("pipe:1", AnimationVideoPlan.Output_Pipe)
    End Sub

    ' --- the argument line ---------------------------------------------------------

    <Fact>
    Public Sub The_plain_line_is_exactly_the_measured_shape()
        Assert.Equal(
            "-hide_banner -nostdin -y -xerror -progress pipe:2 -nostats -loglevel error" &
            " -protocol_whitelist file -i ""C:\pics\a.webp"" -an" &
            " -vf """ & Plain_Scale & ",format=yuv420p""" &
            " -fps_mode cfr -r 20 -c:v libx264 -profile:v high -preset veryfast -crf 22" &
            " -movflags frag_keyframe+empty_moov+default_base_moof -f mp4 pipe:1",
            AnimationVideoPlan.BuildArguments("C:\pics\a.webp", 20, False, 0, 0, 0))
    End Sub

    <Fact>
    Public Sub The_alpha_line_composites_over_the_background_at_the_canvas_size()
        Assert.Equal(
            "-hide_banner -nostdin -y -xerror -progress pipe:2 -nostats -loglevel error" &
            " -protocol_whitelist file -i ""C:\pics\b.webp"" -an" &
            " -filter_complex ""color=c=0x202020:s=960x540:r=20[bg];[bg][0:v]overlay=shortest=1:format=auto," & Plain_Scale & ",format=yuv420p""" &
            " -fps_mode cfr -r 20 -c:v libx264 -profile:v high -preset veryfast -crf 22" &
            " -movflags frag_keyframe+empty_moov+default_base_moof -f mp4 pipe:1",
            AnimationVideoPlan.BuildArguments("C:\pics\b.webp", 20, True, 960, 540, &H202020))
    End Sub

    <Theory>
    <InlineData(201, 101)>
    <InlineData(1, 1)>
    <InlineData(3841, 2161)>
    <InlineData(5000, 1000)>
    <InlineData(198, 360)>
    Public Sub Any_canvas_size_goes_into_the_colour_source_unchanged_and_the_scale_chain_follows(width As Integer, height As Integer)
        Dim line As String = AnimationVideoPlan.BuildArguments("C:\x\a.webp", 15, True, width, height, &H112233)
        Dim graph As String = ArgvOf(line)(ArgvOf(line).IndexOf("-filter_complex") + 1)
        Assert.StartsWith("color=c=0x112233:s=" & width & "x" & height & ":r=15[bg];[bg][0:v]overlay=shortest=1:format=auto,", graph, StringComparison.Ordinal)
        Assert.EndsWith(Plain_Scale & ",format=yuv420p", graph, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub Without_alpha_the_canvas_is_ignored_and_there_is_no_colour_source()
        Dim line As String = AnimationVideoPlan.BuildArguments("C:\x\a.webp", 24, False, 640, 480, &HFFFFFF)
        Assert.DoesNotContain("color=", line, StringComparison.Ordinal)
        Assert.DoesNotContain("overlay", line, StringComparison.Ordinal)
        Assert.DoesNotContain("-filter_complex", line, StringComparison.Ordinal)
        Assert.Contains(" -vf ", line, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub A_transparent_source_without_a_canvas_is_refused_not_guessed()
        Assert.Throws(Of ArgumentOutOfRangeException)(Function() AnimationVideoPlan.BuildArguments("C:\x\a.webp", 20, True, 0, 540, 0))
        Assert.Throws(Of ArgumentOutOfRangeException)(Function() AnimationVideoPlan.BuildArguments("C:\x\a.webp", 20, True, 960, -1, 0))
    End Sub

    <Fact>
    Public Sub An_empty_source_is_refused()
        Assert.Throws(Of ArgumentException)(Function() AnimationVideoPlan.BuildArguments("", 20, False, 0, 0, 0))
        Assert.Throws(Of ArgumentException)(Function() AnimationVideoPlan.BuildArguments(Nothing, 20, False, 0, 0, 0))
    End Sub

    ''' <summary>Audit 242 L-9: FFmpeg takes a bare string as a URL (<c>concat:a|b</c>, <c>subfile,..</c>, <c>http://..</c>)
    ''' or resolves it against its own folder, so anything that is not a fully qualified path is refused - never
    ''' rewritten. A drive-qualified or UNC path is not touched.</summary>
    <Theory>
    <InlineData("concat:a.gif|b.gif")>
    <InlineData("concat:C:\x\a.gif|C:\x\b.gif")>
    <InlineData("subfile,,start,0,end,0,,:C:\x\a.gif")>
    <InlineData("http://host/a.gif")>
    <InlineData("file:C:\x\a.gif")>
    <InlineData("pipe:0")>
    <InlineData("a.gif")>
    <InlineData("-a.gif")>
    <InlineData("-i")>
    <InlineData("..\a.gif")>
    <InlineData(".\a.gif")>
    <InlineData("\a.gif")>
    <InlineData("\x\a.gif")>
    <InlineData("C:a.gif")>
    <InlineData("/a.gif")>
    <InlineData(" ")>
    Public Sub A_source_that_is_not_fully_qualified_is_refused(source As String)
        For Each alpha As Boolean In {False, True}
            Assert.Throws(Of ArgumentException)(Function() AnimationVideoPlan.BuildArguments(source, 20, alpha, 100, 100, 0))
        Next
    End Sub

    <Fact>
    Public Sub A_drive_or_unc_source_is_accepted_and_the_input_is_whitelisted_to_files_before_it()
        For Each source As String In {"C:\a.gif", "\\server\share\a.gif", "d:\dir\-a.gif"}
            Dim argv As List(Of String) = ArgvOf(AnimationVideoPlan.BuildArguments(source, 20, False, 0, 0, 0))
            Dim at As Integer = argv.IndexOf("-protocol_whitelist")
            Assert.True(at >= 0, "the input is not restricted to files")
            Assert.Equal("file", argv(at + 1))
            Assert.Equal("-i", argv(at + 2))
            Assert.Equal(source, argv(at + 3))
            Assert.Equal(1, argv.Where(Function(token) token = "-protocol_whitelist").Count())
        Next
    End Sub

    <Fact>
    Public Sub The_rate_is_clamped_the_way_the_permanent_conversion_clamps_it_in_both_places_it_appears()
        Dim low As List(Of String) = ArgvOf(AnimationVideoPlan.BuildArguments("C:\x\a.webp", 0, True, 10, 10, 0))
        Assert.Equal("1", low(low.IndexOf("-r") + 1))
        Assert.Contains(":r=1[bg]", low(low.IndexOf("-filter_complex") + 1), StringComparison.Ordinal)

        Dim high As List(Of String) = ArgvOf(AnimationVideoPlan.BuildArguments("C:\x\a.webp", 500, True, 10, 10, 0))
        Assert.Equal("50", high(high.IndexOf("-r") + 1))
        Assert.Contains(":r=50[bg]", high(high.IndexOf("-filter_complex") + 1), StringComparison.Ordinal)
    End Sub

    ' --- the background colour -----------------------------------------------------

    <Fact>
    Public Sub The_background_is_six_upper_case_hex_digits_after_0x()
        Assert.Equal("0x202020", AnimationVideoPlan.FormatBackground(32, 32, 32))
        Assert.Equal("0xFF0080", AnimationVideoPlan.FormatBackground(255, 0, 128))
        Assert.Equal("0x000000", AnimationVideoPlan.FormatBackground(0, 0, 0))
        Assert.Equal("0x0A0B0C", AnimationVideoPlan.FormatBackground(10, 11, 12))
    End Sub

    <Fact>
    Public Sub A_channel_outside_the_byte_range_is_clamped_not_wrapped()
        Assert.Equal("0xFF0000", AnimationVideoPlan.FormatBackground(999, -5, 0))
    End Sub

    <Fact>
    Public Sub A_packed_colour_drops_the_alpha_byte()
        ' Color.ToArgb() of an opaque colour carries 0xFF in the top byte.
        Assert.Equal("0x112233", AnimationVideoPlan.FormatBackground(&HFF112233))
        Assert.Equal("0x112233", AnimationVideoPlan.FormatBackground(&H112233))
    End Sub

    ' --- the source path ------------------------------------------------------------

    <Theory>
    <InlineData("C:\pics\plain.webp")>
    <InlineData("C:\Фото\котик 1.webp")>
    <InlineData("C:\My Pictures\a b  c.webp")>
    <InlineData("C:\a""b.webp")>
    <InlineData("C:\dir\sub\")>
    <InlineData("C:\dir with space\")>
    <InlineData("E:\")>
    <InlineData("\\server\share\a b.webp")>
    <InlineData("\\server\share\dir\")>
    <InlineData("C:\a&b%PATH%^c(d).webp")>
    <InlineData("C:\日本語\アニメ.webp")>
    <InlineData("C:\a\\""b\.webp")>
    Public Sub Every_path_reaches_ffmpeg_as_exactly_one_argument_and_the_rest_of_the_line_survives(path As String)
        Dim reference As List(Of String) = ArgvOf(AnimationVideoPlan.BuildArguments("C:\x.webp", 20, False, 0, 0, 0))
        For Each alpha As Boolean In {False, True}
            Dim argv As List(Of String) = ArgvOf(AnimationVideoPlan.BuildArguments(path, 20, alpha, 321, 123, &H202020))
            Assert.Equal(path, argv(argv.IndexOf("-i") + 1))
            ' A quoting bug swallows a neighbour or leaves a stray quote: the count and the tail show it.
            Assert.Equal(reference.Count, argv.Count)
            Assert.Equal("pipe:1", argv(argv.Count - 1))
            Assert.Equal("mp4", argv(argv.Count - 2))
            Assert.Equal("-an", argv(argv.IndexOf("-i") + 2))
        Next
    End Sub

    ' --- rule 25: no output path ------------------------------------------------------

    ''' <summary>Options that take a value, and those that do not. Everything else on the line is a positional argument,
    ''' which for ffmpeg is an OUTPUT. An option this table does not know fails the test on purpose: a new argument has
    ''' to be looked at by whoever adds it, with rule 25 in mind.</summary>
    Private Shared ReadOnly Valued_Options As String() = {
        "-progress", "-loglevel", "-protocol_whitelist", "-i", "-vf", "-filter_complex", "-fps_mode", "-r", "-c:v", "-profile:v", "-preset", "-crf",
        "-movflags", "-f"
    }
    Private Shared ReadOnly Flag_Options As String() = {"-hide_banner", "-nostdin", "-y", "-xerror", "-nostats", "-an"}

    Private Shared Function PositionalArguments(argv As List(Of String)) As List(Of String)
        Dim result As New List(Of String)()
        Dim i As Integer = 0
        While i < argv.Count
            Dim token As String = argv(i)
            If Valued_Options.Contains(token) Then
                i += 2
            ElseIf Flag_Options.Contains(token) Then
                i += 1
            ElseIf token.StartsWith("-", StringComparison.Ordinal) Then
                Throw New Xunit.Sdk.XunitException("The argument line carries an option this rule-25 scan does not know: " & token)
            Else
                result.Add(token)
                i += 1
            End If
        End While
        Return result
    End Function

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub The_only_output_is_the_literal_pipe_and_no_other_token_looks_like_a_path(alpha As Boolean)
        ' The source is deliberately named like an output to prove the scan reads positions, not suffixes.
        Dim line As String = AnimationVideoPlan.BuildArguments("C:\clips\out.mp4", 20, alpha, 200, 100, &H202020)
        Dim argv As List(Of String) = ArgvOf(line)

        Assert.Equal(New String() {"pipe:1"}, PositionalArguments(argv).ToArray())
        Assert.Equal("pipe:1", argv(argv.Count - 1))
        Assert.Equal(1, argv.Where(Function(token) token = "pipe:1").Count())
        Assert.Equal("pipe:2", argv(argv.IndexOf("-progress") + 1))

        ' -y may only be followed by another option, never by a file name.
        Dim yAt As Integer = argv.IndexOf("-y")
        Assert.True(yAt >= 0 AndAlso argv(yAt + 1).StartsWith("-", StringComparison.Ordinal), "-y must not be followed by a file")

        ' Apart from the source, no token holds a path separator, a drive, a file extension or the temp marker.
        Dim sourceAt As Integer = argv.IndexOf("-i") + 1
        For index As Integer = 0 To argv.Count - 1
            If index = sourceAt Then Continue For
            Dim token As String = argv(index)
            Assert.DoesNotContain("\", token, StringComparison.Ordinal)
            Assert.DoesNotContain("/", token, StringComparison.Ordinal)
            Assert.False(Regex.IsMatch(token, "\.(mp4|mkv|webm|mov|m4v|tmp|ts)$", RegexOptions.IgnoreCase), "a file name on the line: " & token)
            Assert.DoesNotContain("fms-tmp", token, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub The_plan_source_names_no_output_file_at_all()
        Dim code As String = CodeOnly(ReadSource("AnimationVideoPlan.vb"))
        Assert.DoesNotContain(".mp4", code, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("TempPathFor", code, StringComparison.Ordinal)
        Assert.DoesNotContain("TargetPathFor", code, StringComparison.Ordinal)
        Assert.DoesNotContain("Output_Extension", code, StringComparison.Ordinal)
    End Sub

    ''' <summary>Rule 25 also holds for the process half: the encoder holds the video in memory and has no way to put it
    ''' on a disk - no file API is even named in its code.</summary>
    <Fact>
    Public Sub The_encoder_source_names_no_file_api()
        Dim code As String = CodeOnly(ReadSource("AnimationVideoEncoder.vb"))
        For Each forbidden As String In {"FileStream", "File.", "Directory.", "StreamWriter", "BinaryWriter", "WriteAllBytes", "GetTempFileName", "FileMode", "MemoryMappedFile", "AppFileLogger"}
            Assert.False(code.Contains(forbidden, StringComparison.Ordinal), "AnimationVideoEncoder.vb names " & forbidden)
        Next
    End Sub

    ' --- the verdict -----------------------------------------------------------------

    Private Shared Sub Verdict(expected As AnimationVideoOutcome, label As String,
                               Optional cancelled As Boolean = False, Optional timedOut As Boolean = False,
                               Optional ceiling As Boolean = False, Optional captureFailed As Boolean = False,
                               Optional exitCode As Integer = 0, Optional bytes As Long = 5000000L,
                               Optional frames As Long = 300L)
        Assert.True(expected = AnimationVideoPlan.DecideRun(cancelled, timedOut, ceiling, captureFailed, exitCode, bytes, frames),
                    label & ": expected " & expected.ToString() & " but got " &
                    AnimationVideoPlan.DecideRun(cancelled, timedOut, ceiling, captureFailed, exitCode, bytes, frames).ToString())
    End Sub

    <Fact>
    Public Sub A_clean_exit_with_a_real_video_succeeds()
        Verdict(AnimationVideoOutcome.Succeeded, "clean")
        Verdict(AnimationVideoOutcome.Succeeded, "one frame", frames:=1L)
        Verdict(AnimationVideoOutcome.Succeeded, "exactly the floor", bytes:=VideoConvertPlan.Min_Output_Bytes)
    End Sub

    <Fact>
    Public Sub Frames_that_never_arrived_do_not_veto_a_video_whose_bytes_are_there()
        ' -1 = the progress stream was not seen (an old build, a stderr that stayed quiet): the bytes decide.
        Verdict(AnimationVideoOutcome.Succeeded, "frames unknown", frames:=-1L)
    End Sub

    <Fact>
    Public Sub Cancel_wins_over_every_other_reason()
        Verdict(AnimationVideoOutcome.Cancelled, "cancelled", cancelled:=True)
        Verdict(AnimationVideoOutcome.Cancelled, "cancelled and killed at the deadline", cancelled:=True, timedOut:=True, exitCode:=1)
        Verdict(AnimationVideoOutcome.Cancelled, "cancelled over the ceiling", cancelled:=True, ceiling:=True, exitCode:=1)
        Verdict(AnimationVideoOutcome.Cancelled, "cancelled, nothing written", cancelled:=True, exitCode:=-1, bytes:=0L, frames:=-1L)
    End Sub

    <Fact>
    Public Sub A_run_killed_at_the_ceiling_is_too_large_whatever_its_exit_code_says()
        Verdict(AnimationVideoOutcome.OutputTooLarge, "ceiling", ceiling:=True, exitCode:=1)
        Verdict(AnimationVideoOutcome.OutputTooLarge, "ceiling with a clean-looking exit", ceiling:=True, exitCode:=0)
        Verdict(AnimationVideoOutcome.OutputTooLarge, "ceiling beats the deadline", ceiling:=True, timedOut:=True, exitCode:=1)
    End Sub

    <Fact>
    Public Sub A_run_killed_at_the_deadline_is_timed_out()
        Verdict(AnimationVideoOutcome.TimedOut, "deadline", timedOut:=True, exitCode:=1)
        Verdict(AnimationVideoOutcome.TimedOut, "deadline, no output", timedOut:=True, exitCode:=-1, bytes:=0L, frames:=-1L)
    End Sub

    <Fact>
    Public Sub A_non_zero_exit_is_a_failure_however_much_was_written()
        Verdict(AnimationVideoOutcome.EncoderFailed, "exit 1", exitCode:=1)
        ' -xerror: a truncated animated WEBP exits 183 with part of a video behind it.
        Verdict(AnimationVideoOutcome.EncoderFailed, "exit 183 with a partial video", exitCode:=183, bytes:=900000L, frames:=11L)
        Verdict(AnimationVideoOutcome.EncoderFailed, "could not be started", exitCode:=-1, bytes:=0L, frames:=-1L)
    End Sub

    <Fact>
    Public Sub An_unreadable_output_stream_is_a_failure_even_after_a_clean_exit()
        Verdict(AnimationVideoOutcome.EncoderFailed, "capture failed", captureFailed:=True)
    End Sub

    <Fact>
    Public Sub A_header_without_frames_is_empty_not_a_success()
        Verdict(AnimationVideoOutcome.EmptyOutput, "no bytes", bytes:=0L, frames:=-1L)
        Verdict(AnimationVideoOutcome.EmptyOutput, "a header only", bytes:=VideoConvertPlan.Min_Output_Bytes - 1L)
        Verdict(AnimationVideoOutcome.EmptyOutput, "bytes but zero frames reported", frames:=0L)
    End Sub

    ' --- the stderr split --------------------------------------------------------------

    <Theory>
    <InlineData("frame=312", True)>
    <InlineData("fps=0.00", True)>
    <InlineData("stream_0_0_q=-1.0", True)>
    <InlineData("bitrate=N/A", True)>
    <InlineData("total_size=N/A", True)>
    <InlineData("out_time_us=1500000", True)>
    <InlineData("out_time_ms=1500000", True)>
    <InlineData("out_time=00:00:01.500000", True)>
    <InlineData("dup_frames=0", True)>
    <InlineData("drop_frames=0", True)>
    <InlineData("speed=24.8x", True)>
    <InlineData("progress=continue", True)>
    <InlineData("progress=end", True)>
    <InlineData("[webp @ 000001] image data not found", False)>
    <InlineData("Error while decoding stream #0:0: Invalid data found when processing input", False)>
    <InlineData("C:\pics\a=b.webp: No such file or directory", False)>
    <InlineData("frames=3 is not a progress key", False)>
    <InlineData("=", False)>
    <InlineData("", False)>
    Public Sub Only_the_progress_block_lines_are_taken_out_of_the_failure_text(line As String, expected As Boolean)
        Assert.Equal(expected, AnimationVideoPlan.IsProgressLine(line))
    End Sub

    ' --- helpers -----------------------------------------------------------------------

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Video")
            If File.Exists(Path.Combine(candidate, "AnimationVideoPlan.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src\Video not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function ReadSource(fileName As String) As String
        Return File.ReadAllText(Path.Combine(SourceDir(), fileName)).Replace(vbCr, "")
    End Function

    ''' <summary>The code without comment lines (XML doc comments included), so a sentence about a file is not a use of one.</summary>
    Private Shared Function CodeOnly(source As String) As String
        Dim kept As New List(Of String)()
        For Each line As String In source.Split(vbLf)
            If Not line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then kept.Add(line)
        Next
        Return String.Join(vbLf, kept)
    End Function

End Class
#End If
