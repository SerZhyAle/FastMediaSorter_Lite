#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>What kind of animated file a path names, by its extension (spec 242 rule 5). The route
''' takes animated WEBP and huge GIF; everything else is <see cref="Other"/>.</summary>
Friend Enum AnimationVideoKind
    ''' <summary>Not a format this route handles.</summary>
    Other
    ''' <summary>WEBP: animated when it carries two or more frames (rule 6).</summary>
    Webp
    ''' <summary>GIF: takes the route only when huge (rule 7).</summary>
    Gif
End Enum

''' <summary>
''' The answer of <see cref="AnimationVideoPolicy.Decide"/>: <see cref="Eligible"/>, or the first
''' reason the file does not take the route, so the caller can log why (spec 242 rule 23's
''' <c>Animation video failed: {reason}</c> and the once-per-file "why not" line).
''' </summary>
Friend Enum AnimationVideoVerdict
    ''' <summary>Play it through the temporary encrypted video (rules 5-8).</summary>
    Eligible
    ''' <summary>The "Animated graphics through video" setting is off (rule 2).</summary>
    SettingOff
    ''' <summary>A live stream, not a file (rule 5).</summary>
    Stream
    ''' <summary><c>Current_File_Name</c> is not a readable file path (rule 5).</summary>
    NoReadablePath
    ''' <summary>The file lives inside a <c>.fd-sec</c> container: no decrypted byte on disk (rule 5, Q242-3).</summary>
    InsideSecureContainer
    ''' <summary>Neither a WEBP nor a GIF.</summary>
    UnsupportedFormat
    ''' <summary>The header names no usable canvas (a non-positive side).</summary>
    SizeUnknown
    ''' <summary>One frame is over the decode budget: refused as today, never routed (rule 8).</summary>
    FrameTooLarge
    ''' <summary>A single frame, or a GIF too big to be walked that shows one: not an animation (rules 5-7).</summary>
    NotAnimation
    ''' <summary>An animated WEBP at or under the pixel floor: keeps the instant in-memory path (rule 6, Q242-4).</summary>
    BelowFloor
    ''' <summary>A GIF under both "huge" thresholds: stays on GDI+ untouched (rule 7, Q242-5).</summary>
    GifNotHuge
    ''' <summary>Everything else says yes but the pinned FFmpeg runtime is absent (rule 22). Tested LAST, so
    ''' this verdict means "eligible apart from FFmpeg" - the one the once-per-installation consent
    ''' offer keys on.</summary>
    NoFfmpeg
End Enum

''' <summary>The plain facts about ONE file that <see cref="AnimationVideoPolicy.Decide"/> reads. Filled by
''' the caller from the setting, the file system and the header; no field is looked up here.</summary>
Friend Structure AnimationVideoFacts
    ''' <summary>The setting <c>AnimatedViaVideo</c> (rule 2).</summary>
    Friend SettingOn As Boolean
    ''' <summary>The pinned FFmpeg runtime is present (rule 22).</summary>
    Friend FfmpegPresent As Boolean
    ''' <summary>By extension; see <see cref="AnimationVideoPolicy.KindOfExtension"/>.</summary>
    Friend Kind As AnimationVideoKind
    ''' <summary>Canvas in pixels (WEBP: the header; GIF: the logical-screen descriptor). Long, so a
    ''' crafted 32-bit value arrives unharmed.</summary>
    Friend Width As Long
    Friend Height As Long
    ''' <summary>Frame count: WEBP from the header; GIF from the block walk (0 = not walked, which is what
    ''' a file under <see cref="AnimationVideoPolicy.Gif_Block_Walk_Min_File_Bytes"/> gets).</summary>
    Friend Frames As Long
    ''' <summary>Length of the file on disk, in bytes.</summary>
    Friend FileLength As Long
    ''' <summary>The file is an entry of a <c>.fd-sec</c> container.</summary>
    Friend InsideSecureContainer As Boolean
    ''' <summary>The source is a live stream.</summary>
    Friend IsLiveStream As Boolean
    ''' <summary><c>Current_File_Name</c> is a readable file path (local, UNC, an archive entry or a
    ''' remote-cache copy).</summary>
    Friend IsReadablePath As Boolean
    ''' <summary>Spec 241 classified the animation <c>BudgetVerdict.AnimationTooLong</c> (stage 1's
    ''' <c>StagedInfo.IsTooLong</c>). The policy also derives the same fact from the size, so a caller
    ''' that has no 241 payload (a GIF, a neighbour) leaves it False.</summary>
    Friend AnimationTooLong As Boolean
End Structure

''' <summary>
''' Which files take the animated-graphics-through-video route (spec 242 rules 5-8), as one pure
''' decision over plain facts. No WinForms, no registry, no IO: the caller gathers the facts (the
''' header of a WEBP, the <see cref="GifBlockWalker"/> of a GIF) and acts on the verdict.
'''
''' The order of the checks is the order of the cheapness and of the story: the user's switch, then
''' what kind of source it is, then the format and its thresholds, and FFmpeg's presence LAST - so
''' <see cref="AnimationVideoVerdict.NoFfmpeg"/> says "this file would take the route" and the
''' consent offer of rule 22 is never made for a file that never could.
''' </summary>
Friend Module AnimationVideoPolicy

    ''' <summary>An animated WEBP whose total pixels (width x height x frames) are ABOVE this takes the
    ''' route (rule 6). The same figure as spec 241's first-frame threshold, referenced and not copied.</summary>
    Friend Const Video_Min_Total_Pixels As Long = AnimationStaging.Still_First_Min_Total_Pixels

    ''' <summary>A GIF whose file is AT LEAST this big is huge (rule 7, Q242-5). MB as everywhere in the
    ''' viewer: 1024 x 1024 bytes.</summary>
    Friend Const Gif_Huge_Min_File_Bytes As Long = 16L * 1024L * 1024L

    ''' <summary>A GIF whose total pixels (canvas x frames) are AT LEAST this is huge (rule 7, Q242-5).</summary>
    Friend Const Gif_Huge_Min_Total_Pixels As Long = 50L * 1000L * 1000L

    ''' <summary>The frame count of a GIF comes from a linear walk of its blocks, done only for a file of
    ''' at least this size (rule 7); a smaller one cannot be huge and is never walked.</summary>
    Friend Const Gif_Block_Walk_Min_File_Bytes As Long = 2L * 1024L * 1024L

    ''' <summary>The kind for a file extension, with or without the leading dot, any case. Anything but
    ''' <c>.webp</c> and <c>.gif</c> (and Nothing) is <see cref="AnimationVideoKind.Other"/>.</summary>
    Friend Function KindOfExtension(extension As String) As AnimationVideoKind
        If String.IsNullOrEmpty(extension) Then Return AnimationVideoKind.Other
        Dim bare As String = If(extension(0) = "."c, extension.Substring(1), extension)
        If String.Equals(bare, "webp", StringComparison.OrdinalIgnoreCase) Then Return AnimationVideoKind.Webp
        If String.Equals(bare, "gif", StringComparison.OrdinalIgnoreCase) Then Return AnimationVideoKind.Gif
        Return AnimationVideoKind.Other
    End Function

    ''' <summary>True when a GIF of this length is worth the block walk for its frame count (rule 7).</summary>
    Friend Function GifNeedsBlockWalk(fileLength As Long) As Boolean
        Return fileLength >= Gif_Block_Walk_Min_File_Bytes
    End Function

    ''' <summary>
    ''' The decision for one file: <see cref="AnimationVideoVerdict.Eligible"/>, or the first reason it
    ''' is not. An animation that is over the decode budget (<c>AnimationTooLong</c>, over 400 Mpx in
    ''' total) is eligible without the floors of rules 6 and 7 - FFmpeg does not hold its frames
    ''' (rule 8); a frame over the budget never is. Nothing here forms a product of header values.
    ''' </summary>
    Friend Function Decide(facts As AnimationVideoFacts) As AnimationVideoVerdict
        If Not facts.SettingOn Then Return AnimationVideoVerdict.SettingOff
        If facts.IsLiveStream Then Return AnimationVideoVerdict.Stream
        If Not facts.IsReadablePath Then Return AnimationVideoVerdict.NoReadablePath
        If facts.InsideSecureContainer Then Return AnimationVideoVerdict.InsideSecureContainer
        If facts.Kind <> AnimationVideoKind.Webp AndAlso facts.Kind <> AnimationVideoKind.Gif Then
            Return AnimationVideoVerdict.UnsupportedFormat
        End If
        If facts.Width <= 0 OrElse facts.Height <= 0 Then Return AnimationVideoVerdict.SizeUnknown

        ' The decoder seam's own classification, so the policy and 241 can never disagree on the ceiling.
        Dim budget As BudgetVerdict = DecodeBudget.Classify(facts.Width, facts.Height, facts.Frames)
        If budget = BudgetVerdict.FrameTooLarge Then Return AnimationVideoVerdict.FrameTooLarge

        If Not (facts.AnimationTooLong OrElse budget = BudgetVerdict.AnimationTooLong) Then
            Dim thresholds As AnimationVideoVerdict = If(facts.Kind = AnimationVideoKind.Webp, WebpFloor(facts), GifHugeness(facts))
            If thresholds <> AnimationVideoVerdict.Eligible Then Return thresholds
        End If

        If Not facts.FfmpegPresent Then Return AnimationVideoVerdict.NoFfmpeg
        Return AnimationVideoVerdict.Eligible
    End Function

    ''' <summary>Rule 6: two or more frames and total pixels strictly above <see cref="Video_Min_Total_Pixels"/>.</summary>
    Private Function WebpFloor(facts As AnimationVideoFacts) As AnimationVideoVerdict
        If facts.Frames < 2 Then Return AnimationVideoVerdict.NotAnimation
        ' "Above" the floor is "at least one more", so the one helper serves both rules.
        If Not TotalPixelsAtLeast(facts.Width, facts.Height, facts.Frames, Video_Min_Total_Pixels + 1L) Then
            Return AnimationVideoVerdict.BelowFloor
        End If
        Return AnimationVideoVerdict.Eligible
    End Function

    ''' <summary>Rule 7: an animated GIF whose file is at least 16 MB or whose canvas x frames is at least 50 Mpx.</summary>
    Private Function GifHugeness(facts As AnimationVideoFacts) As AnimationVideoVerdict
        If facts.Frames < 2 Then
            ' Under the walk threshold there is no frame count to expect: a small GIF is simply not huge.
            ' A walked file that counted fewer than two frames is a still.
            Return If(GifNeedsBlockWalk(facts.FileLength), AnimationVideoVerdict.NotAnimation, AnimationVideoVerdict.GifNotHuge)
        End If
        If facts.FileLength >= Gif_Huge_Min_File_Bytes Then Return AnimationVideoVerdict.Eligible
        If TotalPixelsAtLeast(facts.Width, facts.Height, facts.Frames, Gif_Huge_Min_Total_Pixels) Then Return AnimationVideoVerdict.Eligible
        Return AnimationVideoVerdict.GifNotHuge
    End Function

    ''' <summary>
    ''' True when width x height x frames is at least <paramref name="minimum"/>, WITHOUT forming the
    ''' product (a crafted header can claim Int32.MaxValue for all three; Long overflows on the first
    ''' multiplication of two of them, and the viewer builds with overflow checks on). A non-positive
    ''' factor is never "at least" anything. <paramref name="minimum"/> must be 1 or more.
    ''' </summary>
    Friend Function TotalPixelsAtLeast(width As Long, height As Long, frames As Long, minimum As Long) As Boolean
        If width <= 0 OrElse height <= 0 OrElse frames <= 0 Then Return False
        ' A side alone that reaches the minimum settles it: the other two factors are at least 1.
        If width >= minimum OrElse height >= minimum Then Return True
        ' Both sides are below the minimum (a tens-of-millions figure), so this product is below 1E16.
        Dim framePixels As Long = width * height
        Dim framesNeeded As Long = minimum \ framePixels + If(minimum Mod framePixels = 0L, 0L, 1L)
        Return frames >= framesNeeded
    End Function

End Module
#End If
