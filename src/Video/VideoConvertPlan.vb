#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.IO

''' <summary>
''' How an animation becomes a video, and what the result means
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §9, §10).
'''
''' Pure - no process, no disk, no UI - because this module holds the two things that are
''' expensive to get wrong and impossible to see: the exact encoder arguments (an odd-sized
''' GIF fails outright without the scale filter, a 10-bit profile plays nowhere), and the
''' decision table that says when the ORIGINAL may be deleted. The delete is the
''' irreversible half of the feature, so "only the full-success path deletes" is a rule a
''' test states, not a comment somebody has to keep true by hand.
''' </summary>
Friend Module VideoConvertPlan

    ''' <summary>Animation frame delays are per-frame; a constant frame rate is what keeps a
    ''' player's seek bar honest. The clamp is against nonsense on both ends - a 2 ms frame
    ''' delay would ask for 500 fps.</summary>
    Friend Const Min_Fps As Integer = 1
    Friend Const Max_Fps As Integer = 50

    ''' <summary>Below this the encoder produced a header and nothing else. Together with
    ''' "exit code 0" and "the file is there" it is the whole definition of success.</summary>
    Friend Const Min_Output_Bytes As Long = 1024

    ''' <summary>A hard ceiling on one conversion. An animation that cannot be encoded in
    ''' ten minutes is not the case this button exists for (§9.4).</summary>
    Friend Const Timeout_Ms As Integer = 600000

    Friend Const Output_Extension As String = ".mp4"

    ''' <summary>
    ''' The source's average rate, rounded, clamped. Both arguments come from what the
    ''' viewer is ALREADY playing - GDI+ frame count and the summed frame delays - so this
    ''' never needs to open the file a second time.
    ''' </summary>
    Friend Function Fps(frameCount As Integer, totalDurationMs As Integer) As Integer
        If frameCount <= 0 OrElse totalDurationMs <= 0 Then Return 10
        Dim rate As Double = frameCount * 1000.0R / totalDurationMs
        Dim rounded As Integer = CInt(Math.Round(rate, MidpointRounding.AwayFromZero))
        Return Math.Max(Min_Fps, Math.Min(Max_Fps, rounded))
    End Function

    ''' <summary>
    ''' Where the video goes: the source name with the extension replaced, in the same
    ''' folder - and never over a file that is already there.
    '''
    ''' NameCollisionPolicy is deliberately NOT consulted. That setting describes copying
    ''' and moving BETWEEN folders; reusing it here would let its "replace" value turn
    ''' "Replace with video" into "overwrite somebody else's file", which is not what the
    ''' button says.
    ''' </summary>
    ''' <param name="exists">Asked for each candidate. A parameter rather than File.Exists
    ''' so the naming rule can be proven without a disk.</param>
    Friend Function TargetPathFor(sourcePath As String, exists As Func(Of String, Boolean)) As String
        Dim folder As String = Path.GetDirectoryName(sourcePath)
        Dim baseName As String = Path.GetFileNameWithoutExtension(sourcePath)

        Dim candidate As String = Combine(folder, baseName & Output_Extension)
        Dim attempt As Integer = 2
        While exists(candidate)
            candidate = Combine(folder, baseName & " (" & attempt.ToString(CultureInfo.InvariantCulture) & ")" & Output_Extension)
            attempt += 1
        End While
        Return candidate
    End Function

    ''' <summary>
    ''' Where the encoder actually writes: <c>cat.fms-tmp.mp4</c>, NOT <c>cat.mp4.fms-tmp</c>.
    ''' The marker goes BEFORE the extension for exactly the reason ImageFileWriter.TempPathFor
    ''' documents - the encoder picks its container from the extension it is handed, and a
    ''' name ending in ".fms-tmp" is a container FFmpeg does not know.
    ''' </summary>
    Friend Function TempPathFor(targetPath As String) As String
        Dim folder As String = Path.GetDirectoryName(targetPath)
        Dim name As String = Path.GetFileNameWithoutExtension(targetPath) & ImageFileWriter.TempMarker & Path.GetExtension(targetPath)
        Return Combine(folder, name)
    End Function

    ''' <summary>
    ''' The command line (§9.1). Every flag earns its place:
    '''   -nostdin              the process runs with redirected pipes and must never wait
    '''                         on a console that does not exist;
    '''   -progress pipe:1      newline-terminated key=value progress on stdout. The stderr
    '''                         status line is \r-terminated, so it arrives as one enormous
    '''                         "line" at the very end - useless for a progress dialog;
    '''   -nostats              having asked for progress, do not also print that line;
    '''   -an                   the sources have no audio track;
    '''   scale=trunc(iw/2)*2.. H.264 yuv420p needs even dimensions - an odd-sized GIF fails
    '''                         outright without this;
    '''   format=yuv420p        the profile every player understands. Alpha is flattened,
    '''                         which is why the confirmation says so (§9.3);
    '''   -fps_mode cfr -r      a constant frame rate keeps players and seek bars honest;
    '''   -crf 20 -preset medium  visually transparent for animation-sized material, and
    '''                         finishes in seconds;
    '''   +faststart            the moov atom up front, so the file starts playing before it
    '''                         is fully read - which matters over a share.
    ''' </summary>
    Friend Function BuildArguments(sourcePath As String, tempPath As String, fps As Integer) As String
        Return "-hide_banner -nostdin -y -progress pipe:1 -nostats" &
               " -i " & Quote(sourcePath) &
               " -an" &
               " -vf ""scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p""" &
               " -fps_mode cfr -r " & fps.ToString(CultureInfo.InvariantCulture) &
               " -c:v libx264 -profile:v high -crf 20 -preset medium" &
               " -movflags +faststart " &
               Quote(tempPath)
    End Function

    ''' <summary>
    ''' The encoder's verdict. Success requires ALL of: not cancelled, exit code 0, the file
    ''' is there, and it is not just a header.
    ''' </summary>
    Friend Function DecideEncode(cancelled As Boolean, exitCode As Integer,
                                 tempExists As Boolean, tempBytes As Long) As VideoReplaceOutcome
        If cancelled Then Return VideoReplaceOutcome.Cancelled
        If exitCode <> 0 Then Return VideoReplaceOutcome.EncoderFailed
        If Not tempExists Then Return VideoReplaceOutcome.OutputMissing
        If tempBytes < Min_Output_Bytes Then Return VideoReplaceOutcome.OutputTooSmall
        Return VideoReplaceOutcome.EncodedOk
    End Function

    ''' <summary>
    ''' The original is deleted ONLY once the video exists at its FINAL name (invariant 2).
    ''' Everything before that leaves the source exactly where it was.
    ''' </summary>
    Friend Function ShouldDeleteOriginal(encode As VideoReplaceOutcome, swapped As Boolean) As Boolean
        Return encode = VideoReplaceOutcome.EncodedOk AndAlso swapped
    End Function

    ''' <summary>
    ''' What the whole operation reports.
    '''
    ''' A failed DELETE does not roll the video back: the user gets the video plus a message
    ''' naming the reason (§10 step 6). Silently deleting the new file to restore symmetry
    ''' would be the worse answer - it throws away work that succeeded.
    ''' </summary>
    Friend Function DecideReplace(encode As VideoReplaceOutcome, swapped As Boolean, originalDeleted As Boolean) As VideoReplaceOutcome
        If encode <> VideoReplaceOutcome.EncodedOk Then Return encode
        If Not swapped Then Return VideoReplaceOutcome.SwapFailed
        If Not originalDeleted Then Return VideoReplaceOutcome.OriginalNotDeleted
        Return VideoReplaceOutcome.Success
    End Function

    ''' <summary>The temporary file survives exactly one outcome pair - the two in which it
    ''' has already become the target. Everything else takes it away with it.</summary>
    Friend Function ShouldRemoveTemp(outcome As VideoReplaceOutcome) As Boolean
        Return outcome <> VideoReplaceOutcome.Success AndAlso outcome <> VideoReplaceOutcome.OriginalNotDeleted
    End Function

    Private Function Combine(folder As String, name As String) As String
        Return If(String.IsNullOrEmpty(folder), name, Path.Combine(folder, name))
    End Function

    Private Function Quote(value As String) As String
        Return """" & value & """"
    End Function

End Module

''' <summary>
''' Every state the conversion can end in. One value per line, because the UI has to say
''' which - and because <see cref="VideoConvertPlan.ShouldDeleteOriginal"/> is allowed to
''' answer True for exactly one of them.
''' </summary>
Friend Enum VideoReplaceOutcome
    ''' <summary>The encoder produced a usable temporary file. Not a final state.</summary>
    EncodedOk
    ''' <summary>Video at its final name, original permanently gone.</summary>
    Success
    ''' <summary>Video written, but the original could not be deleted - and is still there.</summary>
    OriginalNotDeleted
    ''' <summary>The user pressed Cancel. Original untouched, temporary removed.</summary>
    Cancelled
    ''' <summary>Non-zero exit code, or the process was killed at the deadline.</summary>
    EncoderFailed
    ''' <summary>Exit code 0 and no file - FFmpeg reported success at nothing.</summary>
    OutputMissing
    ''' <summary>A header and no frames.</summary>
    OutputTooSmall
    ''' <summary>The encode was fine; renaming it onto the target name was not.</summary>
    SwapFailed
End Enum
#End If
