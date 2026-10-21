#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text
Imports SixLabors.ImageSharp
Imports SixLabors.ImageSharp.Formats.Gif
Imports SixLabors.ImageSharp.PixelFormats
Imports Xunit

''' <summary>
''' Animated graphics through video (specification 242), step T-1: the pure halves - which files take
''' the route (rules 5-8), the GIF block walk behind rule 7, and the neighbour prepared ahead (rule 24).
''' No WinForms, no FFmpeg, no registry. The verdicts travel as names (a Public test method cannot
''' take the Friend enum), which also makes a failing row say what it got.
''' </summary>
Public Class AnimationVideoPolicyTests

    Private Const MiB As Long = 1024L * 1024L

    ' ---------------------------------------------------------------------------------------------
    ' Policy helpers
    ' ---------------------------------------------------------------------------------------------

    ''' <summary>A file that passes every gate except the size/format question the test asks.</summary>
    Private Shared Function Facts(kind As AnimationVideoKind, width As Long, height As Long, frames As Long, Optional fileLength As Long = 1000L) As AnimationVideoFacts
        Return New AnimationVideoFacts With {
            .SettingOn = True,
            .FfmpegPresent = True,
            .IsReadablePath = True,
            .Kind = kind,
            .Width = width,
            .Height = height,
            .Frames = frames,
            .FileLength = fileLength}
    End Function

    Private Shared Function Verdict(f As AnimationVideoFacts) As String
        Return AnimationVideoPolicy.Decide(f).ToString()
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' The constants (rules 6, 7)
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_constants_are_the_ones_the_specification_names()
        Assert.Equal(10000000L, AnimationVideoPolicy.Video_Min_Total_Pixels)
        Assert.Equal(AnimationStaging.Still_First_Min_Total_Pixels, AnimationVideoPolicy.Video_Min_Total_Pixels)
        Assert.Equal(16L * MiB, AnimationVideoPolicy.Gif_Huge_Min_File_Bytes)
        Assert.Equal(50000000L, AnimationVideoPolicy.Gif_Huge_Min_Total_Pixels)
        Assert.Equal(2L * MiB, AnimationVideoPolicy.Gif_Block_Walk_Min_File_Bytes)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Animated WEBP (rule 6, Q242-4): total pixels strictly above 10 Mpx
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData(1L, 1L, 10000000L, "BelowFloor")>       ' exactly 10 Mpx: not above
    <InlineData(1L, 1L, 10000001L, "Eligible")>         ' boundary + 1
    <InlineData(1000L, 1000L, 10L, "BelowFloor")>
    <InlineData(1000L, 1000L, 11L, "Eligible")>
    <InlineData(5000000L, 1L, 2L, "BelowFloor")>        ' 10 Mpx in two frames
    <InlineData(5000001L, 1L, 2L, "Eligible")>
    <InlineData(10000000L, 1L, 2L, "Eligible")>         ' a side at the floor, the product above it
    <InlineData(10000001L, 1L, 2L, "Eligible")>         ' a side beyond the floor on its own
    <InlineData(64L, 48L, 12L, "BelowFloor")>           ' a sticker keeps the instant path
    <InlineData(1000L, 1000L, 2L, "BelowFloor")>
    <InlineData(960L, 540L, 711L, "Eligible")>          ' the measured sample A: 368 Mpx
    <InlineData(1000L, 1000L, 1L, "NotAnimation")>      ' one frame is a still, however big
    <InlineData(20000L, 20000L, 1L, "NotAnimation")>
    <InlineData(1000L, 1000L, 0L, "NotAnimation")>
    Public Sub An_animated_webp_needs_two_frames_and_more_than_ten_megapixels(width As Long, height As Long, frames As Long, expected As String)
        Assert.Equal(expected, Verdict(Facts(AnimationVideoKind.Webp, width, height, frames)))
    End Sub

    ''' <summary>The WEBP floor is spec 241's staging threshold: wherever one says "staged", the other says
    ''' "eligible" (within the budget, two or more frames).</summary>
    <Fact>
    Public Sub The_webp_floor_agrees_with_the_staging_threshold_of_241()
        Dim sides As Long() = {1L, 2L, 7L, 100L, 999L, 1000L, 1001L, 3162L, 3163L, 5000L, 20000L}
        Dim counts As Long() = {1L, 2L, 3L, 9L, 10L, 11L, 100L, 1000L, 9999L, 10000L, 10001L, 5000000L, 10000000L, 10000001L}
        For Each w As Long In sides
            For Each h As Long In sides
                For Each n As Long In counts
                    Dim budget As BudgetVerdict = DecodeBudget.Classify(w, h, n)
                    If budget <> BudgetVerdict.Within Then Continue For
                    Dim eligible As Boolean = Verdict(Facts(AnimationVideoKind.Webp, w, h, n)) = "Eligible"
                    Assert.True(AnimationStaging.ShouldStage(w, h, n) = eligible, $"{w} x {h} x {n}")
                Next
            Next
        Next
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Huge GIF (rule 7, Q242-5): file >= 16 MB or total pixels >= 50 Mpx, frame count walked from 2 MB
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData(1L, 1L, 50000000L, 3145728L, "Eligible")>        ' exactly 50 Mpx: "at least"
    <InlineData(1L, 1L, 49999999L, 3145728L, "GifNotHuge")>      ' boundary - 1
    <InlineData(1L, 1L, 50000001L, 3145728L, "Eligible")>
    <InlineData(5000L, 5000L, 2L, 3145728L, "Eligible")>         ' 50 Mpx in two frames
    <InlineData(5000L, 4999L, 2L, 3145728L, "GifNotHuge")>
    <InlineData(100L, 100L, 5L, 16777215L, "GifNotHuge")>        ' 16 MB - 1 byte
    <InlineData(100L, 100L, 5L, 16777216L, "Eligible")>          ' exactly 16 MB: "at least"
    <InlineData(100L, 100L, 5L, 16777217L, "Eligible")>
    <InlineData(100L, 100L, 2L, 2097152L, "GifNotHuge")>         ' walked, animated, small
    <InlineData(100L, 100L, 1L, 2097152L, "NotAnimation")>       ' walked: a single frame is a still
    <InlineData(100L, 100L, 0L, 2097152L, "NotAnimation")>       ' walked and nothing counted
    <InlineData(100L, 100L, 1L, 16777216L, "NotAnimation")>      ' a huge file of one frame is a still
    <InlineData(100L, 100L, 0L, 2097151L, "GifNotHuge")>         ' under 2 MB: never walked, never huge
    <InlineData(100L, 100L, 0L, 1000L, "GifNotHuge")>
    <InlineData(100L, 100L, 7L, 2097151L, "GifNotHuge")>         ' a small GIF the caller walked anyway
    <InlineData(7000L, 7000L, 2L, 2097151L, "Eligible")>         ' ... and counted huge by pixels
    Public Sub A_gif_is_huge_by_its_file_or_by_its_pixels(width As Long, height As Long, frames As Long, fileLength As Long, expected As String)
        Assert.Equal(expected, Verdict(Facts(AnimationVideoKind.Gif, width, height, frames, fileLength)))
    End Sub

    <Fact>
    Public Sub The_block_walk_is_for_files_of_at_least_two_megabytes()
        Assert.False(AnimationVideoPolicy.GifNeedsBlockWalk(0L))
        Assert.False(AnimationVideoPolicy.GifNeedsBlockWalk(2L * MiB - 1L))
        Assert.True(AnimationVideoPolicy.GifNeedsBlockWalk(2L * MiB))
        Assert.True(AnimationVideoPolicy.GifNeedsBlockWalk(Long.MaxValue))
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Over budget (rule 8): AnimationTooLong plays, FrameTooLarge never
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub An_animation_over_the_budget_is_eligible_without_the_floors()
        ' By 241's classification (the flag) ...
        Dim flagged As AnimationVideoFacts = Facts(AnimationVideoKind.Webp, 64L, 48L, 12L)
        flagged.AnimationTooLong = True
        Assert.Equal("Eligible", Verdict(flagged))

        Dim flaggedGif As AnimationVideoFacts = Facts(AnimationVideoKind.Gif, 64L, 48L, 12L, 1000L)
        flaggedGif.AnimationTooLong = True
        Assert.Equal("Eligible", Verdict(flaggedGif))

        ' ... and by the size alone: 400 Mpx x 2 frames, exactly at and one over the total ceiling.
        Assert.Equal("Eligible", Verdict(Facts(AnimationVideoKind.Webp, 20000L, 20000L, 2L)))
        Assert.Equal("Eligible", Verdict(Facts(AnimationVideoKind.Gif, 20000L, 20000L, 2L, 3L * MiB)))
        Assert.Equal("Eligible", Verdict(Facts(AnimationVideoKind.Webp, 960L, 540L, 100000L)))
    End Sub

    <Fact>
    Public Sub An_animation_over_the_budget_still_needs_the_runtime()
        Dim f As AnimationVideoFacts = Facts(AnimationVideoKind.Webp, 20000L, 20000L, 2L)
        f.FfmpegPresent = False
        Assert.Equal("NoFfmpeg", Verdict(f))
    End Sub

    <Fact>
    Public Sub A_frame_over_the_budget_is_never_eligible()
        Assert.Equal("FrameTooLarge", Verdict(Facts(AnimationVideoKind.Webp, 20001L, 20000L, 2L)))
        Assert.Equal("FrameTooLarge", Verdict(Facts(AnimationVideoKind.Webp, 400000001L, 1L, 2L)))
        Assert.Equal("FrameTooLarge", Verdict(Facts(AnimationVideoKind.Gif, 65535L, 65535L, 3L, 100L * MiB)))

        ' Even when the caller says 241 found the animation too long: the frame decides.
        Dim f As AnimationVideoFacts = Facts(AnimationVideoKind.Webp, 20001L, 20000L, 2L)
        f.AnimationTooLong = True
        Assert.Equal("FrameTooLarge", Verdict(f))
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Hostile headers: no product of header values is ever formed
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub A_crafted_header_neither_overflows_nor_routes(isGif As Boolean)
        Dim kind As AnimationVideoKind = If(isGif, AnimationVideoKind.Gif, AnimationVideoKind.Webp)
        Dim big As Long = Integer.MaxValue
        Assert.Equal("FrameTooLarge", Verdict(Facts(kind, big, big, big, Long.MaxValue)))
        Assert.Equal("FrameTooLarge", Verdict(Facts(kind, Long.MaxValue, Long.MaxValue, Long.MaxValue, Long.MaxValue)))
        Assert.Equal("FrameTooLarge", Verdict(Facts(kind, Long.MaxValue, 2L, 10L, 3L * MiB)))
        Assert.Equal("SizeUnknown", Verdict(Facts(kind, 0L, 1000L, 50L)))
        Assert.Equal("SizeUnknown", Verdict(Facts(kind, 1000L, -5L, 50L)))
        Assert.Equal("SizeUnknown", Verdict(Facts(kind, Long.MinValue, Long.MinValue, Long.MinValue)))
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub A_crafted_frame_count_is_a_long_animation_not_an_overflow(isGif As Boolean)
        Dim kind As AnimationVideoKind = If(isGif, AnimationVideoKind.Gif, AnimationVideoKind.Webp)
        Assert.Equal("Eligible", Verdict(Facts(kind, 1L, 1L, Long.MaxValue, 3L * MiB)))
        Assert.Equal("Eligible", Verdict(Facts(kind, 400000000L, 1L, Integer.MaxValue, 3L * MiB)))
        Assert.Equal("Eligible", Verdict(Facts(kind, 20000L, 20000L, Long.MaxValue, 3L * MiB)))
    End Sub

    <Theory>
    <InlineData(2147483647L, 2147483647L, 2147483647L, 50000000L, True)>
    <InlineData(Long.MaxValue, Long.MaxValue, Long.MaxValue, Long.MaxValue, True)>
    <InlineData(3037000500L, 3037000500L, 1L, 50000000L, True)>         ' the square of this is Long.MaxValue + a bit
    <InlineData(49999999L, 49999999L, 1L, 50000000L, True)>             ' both sides just below the minimum
    <InlineData(Long.MaxValue, 1L, 1L, 50000000L, True)>
    <InlineData(1L, 1L, Long.MaxValue, Long.MaxValue, True)>
    <InlineData(1L, 1L, 9223372036854775806L, Long.MaxValue, False)>
    <InlineData(7071L, 7071L, 1L, 50000000L, False)>                    ' 49 999 041
    <InlineData(7071L, 7071L, 2L, 50000000L, True)>
    <InlineData(5000L, 5000L, 2L, 50000000L, True)>                     ' exactly the minimum
    <InlineData(5000L, 4999L, 2L, 50000000L, False)>
    <InlineData(0L, 100L, 100L, 1L, False)>
    <InlineData(100L, -1L, 100L, 1L, False)>
    <InlineData(100L, 100L, 0L, 1L, False)>
    Public Sub TotalPixelsAtLeast_never_forms_the_product(width As Long, height As Long, frames As Long, minimum As Long, expected As Boolean)
        Assert.Equal(expected, AnimationVideoPolicy.TotalPixelsAtLeast(width, height, frames, minimum))
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Every reason is reachable, and the order they are given in
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub Every_verdict_is_reachable()
        Dim seen As New HashSet(Of AnimationVideoVerdict)()
        Dim good As AnimationVideoFacts = Facts(AnimationVideoKind.Webp, 1000L, 1000L, 11L)
        seen.Add(AnimationVideoPolicy.Decide(good))

        Dim f As AnimationVideoFacts = good
        f.SettingOn = False
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.IsLiveStream = True
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.IsReadablePath = False
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.InsideSecureContainer = True
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.Kind = AnimationVideoKind.Other
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.Width = 0L
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.Width = 400000001L
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.Frames = 1L
        seen.Add(AnimationVideoPolicy.Decide(f))

        f = good
        f.Frames = 2L
        seen.Add(AnimationVideoPolicy.Decide(f))                  ' BelowFloor

        f = Facts(AnimationVideoKind.Gif, 100L, 100L, 5L, 3L * MiB)
        seen.Add(AnimationVideoPolicy.Decide(f))                  ' GifNotHuge

        f = good
        f.FfmpegPresent = False
        seen.Add(AnimationVideoPolicy.Decide(f))

        For Each reason As AnimationVideoVerdict In [Enum].GetValues(GetType(AnimationVideoVerdict))
            Assert.True(seen.Contains(reason), $"no test reaches {reason}")
        Next
    End Sub

    ''' <summary>When several things are wrong the first in the story is reported, and FFmpeg's absence only
    ''' when the file would otherwise take the route (so the consent offer of rule 22 is never made for a file
    ''' that could not play anyway).</summary>
    <Fact>
    Public Sub The_first_reason_wins_and_the_runtime_is_asked_last()
        Dim bad As AnimationVideoFacts = Facts(AnimationVideoKind.Other, 0L, 0L, 0L)
        bad.SettingOn = False
        bad.FfmpegPresent = False
        bad.IsLiveStream = True
        bad.IsReadablePath = False
        bad.InsideSecureContainer = True
        Assert.Equal("SettingOff", Verdict(bad))

        bad.SettingOn = True
        Assert.Equal("Stream", Verdict(bad))
        bad.IsLiveStream = False
        Assert.Equal("NoReadablePath", Verdict(bad))
        bad.IsReadablePath = True
        Assert.Equal("InsideSecureContainer", Verdict(bad))
        bad.InsideSecureContainer = False
        Assert.Equal("UnsupportedFormat", Verdict(bad))
        bad.Kind = AnimationVideoKind.Webp
        Assert.Equal("SizeUnknown", Verdict(bad))

        ' A small sticker without FFmpeg is BelowFloor, not NoFfmpeg: nothing to offer a download for.
        Dim sticker As AnimationVideoFacts = Facts(AnimationVideoKind.Webp, 64L, 48L, 12L)
        sticker.FfmpegPresent = False
        Assert.Equal("BelowFloor", Verdict(sticker))

        Dim huge As AnimationVideoFacts = Facts(AnimationVideoKind.Webp, 1000L, 1000L, 11L)
        huge.FfmpegPresent = False
        Assert.Equal("NoFfmpeg", Verdict(huge))
    End Sub

    <Theory>
    <InlineData(".webp", "Webp")>
    <InlineData(".WEBP", "Webp")>
    <InlineData("webp", "Webp")>
    <InlineData(".gif", "Gif")>
    <InlineData(".GiF", "Gif")>
    <InlineData("gif", "Gif")>
    <InlineData(".png", "Other")>
    <InlineData(".apng", "Other")>
    <InlineData(".giff", "Other")>
    <InlineData(".", "Other")>
    <InlineData("", "Other")>
    <InlineData(Nothing, "Other")>
    Public Sub The_kind_follows_the_extension(extension As String, expected As String)
        Assert.Equal(expected, AnimationVideoPolicy.KindOfExtension(extension).ToString())
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' The neighbour prepared ahead (rule 24)
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData(5, 10, False, 6)>
    <InlineData(5, 10, True, 4)>
    <InlineData(9, 10, False, 0)>        ' wraps forward
    <InlineData(0, 10, True, 9)>         ' wraps backward
    <InlineData(0, 10, False, 1)>
    <InlineData(9, 10, True, 8)>
    <InlineData(0, 2, False, 1)>
    <InlineData(1, 2, False, 0)>
    <InlineData(0, 2, True, 1)>
    <InlineData(1, 2, True, 0)>
    <InlineData(0, 1, False, -1)>        ' a list of one has no neighbour
    <InlineData(0, 1, True, -1)>
    <InlineData(0, 0, False, -1)>        ' nor an empty list
    <InlineData(0, 0, True, -1)>
    <InlineData(3, -4, False, -1)>
    <InlineData(-7, 10, False, 1)>       ' an out-of-range current is clamped as PrefetchIndex clamps it
    <InlineData(99, 10, False, 0)>
    <InlineData(99, 10, True, 8)>
    Public Sub The_neighbour_follows_the_direction_of_travel(current As Integer, total As Integer, backwards As Boolean, expected As Integer)
        Assert.Equal(expected, AnimationVideoPrefetchPlan.NeighbourToPrepare(current, total, backwards))
    End Sub

    ''' <summary>The plan never invents a direction rule of its own: for every list longer than one it is the
    ''' decode prefetch's index.</summary>
    <Fact>
    Public Sub The_neighbour_is_the_one_the_decode_prefetch_uses()
        For total As Integer = 2 To 7
            For current As Integer = -1 To total
                For Each backwards As Boolean In {False, True}
                    Assert.Equal(NavigationRules.PrefetchIndex(current, total, backwards),
                                 AnimationVideoPrefetchPlan.NeighbourToPrepare(current, total, backwards))
                Next
            Next
        Next
        Assert.Equal(-1, AnimationVideoPrefetchPlan.No_Neighbour)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' The GIF block walk (rule 7)
    ' ---------------------------------------------------------------------------------------------

    Private Shared Function Cat(ParamArray parts As Byte()()) As Byte()
        Using joined As New MemoryStream()
            For Each part As Byte() In parts
                joined.Write(part, 0, part.Length)
            Next
            Return joined.ToArray()
        End Using
    End Function

    Private Shared Function Bytes(ParamArray values As Integer()) As Byte()
        Dim result(values.Length - 1) As Byte
        For i As Integer = 0 To values.Length - 1
            result(i) = CByte(values(i))
        Next
        Return result
    End Function

    ''' <summary>Signature, logical-screen descriptor and (optionally) a 4-entry global colour table whose
    ''' bytes are 3B, so a table read as blocks would be noticed.</summary>
    Private Shared Function Header(width As Integer, height As Integer, Optional globalTable As Boolean = False, Optional signature As String = "GIF89a") As Byte()
        Dim head As Byte() = Cat(Encoding.ASCII.GetBytes(signature),
                                 Bytes(width And &HFF, (width >> 8) And &HFF, height And &HFF, (height >> 8) And &HFF,
                                       If(globalTable, &H81, &H0), 0, 0))
        If Not globalTable Then Return head
        Dim table(11) As Byte
        For i As Integer = 0 To 11
            table(i) = &H3B
        Next
        Return Cat(head, table)
    End Function

    ''' <summary>Data sub-blocks of <paramref name="total"/> bytes in all, 255 at most each, then the terminator.
    ''' The bytes look like block introducers on purpose.</summary>
    Private Shared Function SubBlocks(total As Integer) As Byte()
        Using result As New MemoryStream()
            Dim left As Integer = total
            Dim filler As Integer = 0
            Do While left > 0
                Dim size As Integer = Math.Min(255, left)
                result.WriteByte(CByte(size))
                For i As Integer = 1 To size
                    result.WriteByte(CByte(If(filler Mod 3 = 0, &H3B, If(filler Mod 3 = 1, &H2C, &H21))))
                    filler += 1
                Next
                left -= size
            Loop
            result.WriteByte(0)
            Return result.ToArray()
        End Using
    End Function

    ''' <summary>An image descriptor with its data: optionally a local colour table of 8 entries.</summary>
    Private Shared Function Frame(Optional localTable As Boolean = False, Optional dataBytes As Integer = 10) As Byte()
        Dim descriptor As Byte() = Bytes(&H2C, 0, 0, 0, 0, 1, 0, 1, 0, If(localTable, &H82, &H0))
        Dim table As Byte() = New Byte() {}
        If localTable Then
            ReDim table(23)
            For i As Integer = 0 To 23
                table(i) = &H2C
            Next
        End If
        Return Cat(descriptor, table, Bytes(2), SubBlocks(dataBytes))
    End Function

    Private Shared Function GraphicControl() As Byte()
        Return Bytes(&H21, &HF9, 4, 0, 10, 0, 0, 0)
    End Function

    Private Shared Function Comment(text As String) As Byte()
        Dim body As Byte() = Encoding.ASCII.GetBytes(text)
        ' An empty comment is the terminator alone: the length byte 0 IS the end of the sub-blocks.
        If body.Length = 0 Then Return Bytes(&H21, &HFE, 0)
        Return Cat(Bytes(&H21, &HFE, body.Length), body, Bytes(0))
    End Function

    ''' <summary>The NETSCAPE 2.0 looping extension.</summary>
    Private Shared Function Netscape() As Byte()
        Return Cat(Bytes(&H21, &HFF, 11), Encoding.ASCII.GetBytes("NETSCAPE2.0"), Bytes(3, 1, 0, 0, 0))
    End Function

    Private Shared Function PlainText() As Byte()
        Return Cat(Bytes(&H21, &H1, 12, 0, 0, 0, 0, 8, 0, 8, 0, 1, 1, 0, 0), Bytes(2), Encoding.ASCII.GetBytes("hi"), Bytes(0))
    End Function

    Private Shared Function Trailer() As Byte()
        Return Bytes(&H3B)
    End Function

    Private Shared Function Cut(source As Byte(), length As Integer) As Byte()
        Dim result(length - 1) As Byte
        Array.Copy(source, result, length)
        Return result
    End Function

    ''' <summary>A stream with the knobs a hostile or flaky source has: no seeking, and a read that fails
    ''' after so many bytes.</summary>
    Private NotInheritable Class ProbeStream
        Inherits Stream

        Private ReadOnly inner As Stream
        Private ReadOnly seekable As Boolean
        Private ReadOnly failAfter As Long
        Private delivered As Long

        Friend Sub New(inner As Stream, seekable As Boolean, Optional failAfter As Long = Long.MaxValue)
            Me.inner = inner
            Me.seekable = seekable
            Me.failAfter = failAfter
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return seekable
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property Length As Long
            Get
                If Not seekable Then Throw New NotSupportedException()
                Return inner.Length
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                If Not seekable Then Throw New NotSupportedException()
                Return inner.Position
            End Get
            Set(value As Long)
                If Not seekable Then Throw New NotSupportedException()
                inner.Position = value
            End Set
        End Property

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            If delivered >= failAfter Then Throw New IOException("the share went away")
            Dim wanted As Integer = CInt(Math.Min(CLng(count), failAfter - delivered))
            Dim got As Integer = inner.Read(buffer, offset, wanted)
            delivered += got
            Return got
        End Function

        Public Overrides Sub Flush()
        End Sub

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            If Not seekable Then Throw New NotSupportedException()
            Return inner.Seek(offset, origin)
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            Throw New NotSupportedException()
        End Sub
    End Class

    ''' <summary>Walks the bytes as a seekable file AND as a stream that cannot seek, and insists the two
    ''' agree - the skip logic has a separate branch for each.</summary>
    Private Shared Function WalkBoth(data As Byte()) As GifWalkResult
        Dim seekable As GifWalkResult
        Using stream As New MemoryStream(data)
            seekable = GifBlockWalker.Walk(stream)
        End Using
        Dim oneWay As GifWalkResult
        Using stream As New ProbeStream(New MemoryStream(data), seekable:=False)
            oneWay = GifBlockWalker.Walk(stream)
        End Using
        Assert.Equal(seekable.IsGif, oneWay.IsGif)
        Assert.Equal(seekable.Width, oneWay.Width)
        Assert.Equal(seekable.Height, oneWay.Height)
        Assert.Equal(seekable.Frames, oneWay.Frames)
        Assert.Equal(seekable.IsComplete, oneWay.IsComplete)
        Return seekable
    End Function

    <Fact>
    Public Sub A_plain_gif_gives_its_canvas_and_its_frames()
        Dim data As Byte() = Cat(Header(300, 200), Frame(), Frame(), Frame(), Trailer())
        Dim result As GifWalkResult = WalkBoth(data)
        Assert.True(result.IsGif)
        Assert.Equal(300, result.Width)
        Assert.Equal(200, result.Height)
        Assert.Equal(3, result.Frames)
        Assert.True(result.IsComplete)
    End Sub

    <Fact>
    Public Sub The_canvas_is_two_bytes_little_endian()
        Dim result As GifWalkResult = WalkBoth(Cat(Header(65535, 258), Trailer()))
        Assert.Equal(65535, result.Width)
        Assert.Equal(258, result.Height)
    End Sub

    <Fact>
    Public Sub Comment_application_plain_text_and_graphic_control_extensions_are_stepped_over()
        Dim data As Byte() = Cat(
            Header(64, 48, globalTable:=True),
            Netscape(),
            Comment("made by a test; 3B 2C 21"),
            GraphicControl(), Frame(localTable:=True, dataBytes:=300),
            PlainText(),
            GraphicControl(), Frame(),
            Comment(""),
            GraphicControl(), Frame(localTable:=True, dataBytes:=1),
            Trailer())
        Dim result As GifWalkResult = WalkBoth(data)
        Assert.True(result.IsGif)
        Assert.Equal(64, result.Width)
        Assert.Equal(48, result.Height)
        Assert.Equal(3, result.Frames)
        Assert.True(result.IsComplete)
    End Sub

    <Fact>
    Public Sub A_gif_without_frames_is_counted_as_none()
        Dim result As GifWalkResult = WalkBoth(Cat(Header(10, 10), Trailer()))
        Assert.True(result.IsGif)
        Assert.Equal(10, result.Width)
        Assert.Equal(0, result.Frames)
        Assert.True(result.IsComplete)

        Dim onlyExtensions As GifWalkResult = WalkBoth(Cat(Header(10, 10), Netscape(), Comment("x"), GraphicControl(), Trailer()))
        Assert.Equal(0, onlyExtensions.Frames)
        Assert.True(onlyExtensions.IsComplete)
    End Sub

    <Fact>
    Public Sub A_gif_without_a_trailer_keeps_its_frames_and_says_it_is_not_complete()
        Dim result As GifWalkResult = WalkBoth(Cat(Header(8, 8), Frame(), Frame()))
        Assert.Equal(2, result.Frames)
        Assert.False(result.IsComplete)
    End Sub

    <Fact>
    Public Sub The_older_signature_is_a_gif_too()
        Dim result As GifWalkResult = WalkBoth(Cat(Header(8, 8, signature:="GIF87a"), Frame(), Trailer()))
        Assert.True(result.IsGif)
        Assert.Equal(1, result.Frames)
    End Sub

    <Fact>
    Public Sub Something_that_is_not_a_gif_is_said_so()
        Assert.False(WalkBoth(Cat(Header(8, 8, signature:="GIF88a"), Frame(), Trailer())).IsGif)
        Assert.False(WalkBoth(Cat(Header(8, 8, signature:="gif89a"), Frame(), Trailer())).IsGif)
        Assert.False(WalkBoth(Bytes(&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA, 0, 0, 0, &HD)).IsGif)
        Assert.False(WalkBoth(New Byte() {}).IsGif)
        Assert.False(WalkBoth(Bytes(&H47, &H49, &H46)).IsGif)
        Assert.False(GifBlockWalker.Walk(Nothing).IsGif)
        Dim none As GifWalkResult = WalkBoth(Bytes(1, 2, 3))
        Assert.Equal(0, none.Frames)
        Assert.False(none.IsComplete)
    End Sub

    ''' <summary>The file is cut at EVERY length: never an exception, the frame count never goes backwards as
    ''' more of the file is there, it never exceeds the real count, and only the whole file is complete.</summary>
    <Fact>
    Public Sub A_truncated_gif_returns_what_it_counted_at_every_cut()
        Dim data As Byte() = Cat(
            Header(64, 48, globalTable:=True), Netscape(), Comment("c"),
            GraphicControl(), Frame(localTable:=True, dataBytes:=300),
            PlainText(),
            GraphicControl(), Frame(),
            GraphicControl(), Frame(localTable:=True, dataBytes:=600),
            Trailer())
        Dim previous As Integer = 0
        For length As Integer = 0 To data.Length
            Dim result As GifWalkResult = WalkBoth(Cut(data, length))
            Assert.True(result.Frames >= previous, $"frames went backwards at {length}")
            Assert.True(result.Frames <= 3, $"too many frames at {length}")
            Assert.Equal(length = data.Length, result.IsComplete)
            previous = result.Frames
        Next
        Assert.Equal(3, previous)
    End Sub

    <Fact>
    Public Sub A_cut_in_the_header_or_the_colour_table_keeps_the_signature_and_what_was_read()
        Dim head As Byte() = Header(300, 200, globalTable:=True)
        Dim noTable As GifWalkResult = WalkBoth(Cut(head, 8))
        Assert.True(noTable.IsGif)
        Assert.Equal(0, noTable.Width)

        Dim inTable As GifWalkResult = WalkBoth(Cut(head, head.Length - 3))
        Assert.True(inTable.IsGif)
        Assert.Equal(300, inTable.Width)
        Assert.Equal(200, inTable.Height)
        Assert.Equal(0, inTable.Frames)
        Assert.False(inTable.IsComplete)
    End Sub

    <Fact>
    Public Sub A_sub_block_that_claims_more_than_the_file_has_ends_the_walk()
        ' The second frame's first data sub-block says 255 bytes and 6 are left.
        Dim lying As Byte() = Cat(Bytes(&H2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2), Bytes(255), Bytes(1, 2, 3, 4, 5, 6))
        Dim result As GifWalkResult = WalkBoth(Cat(Header(9, 9), Frame(), lying))
        Assert.Equal(1, result.Frames)
        Assert.False(result.IsComplete)

        ' An extension that does the same.
        Dim extension As GifWalkResult = WalkBoth(Cat(Header(9, 9), Frame(), Bytes(&H21, &HFE, 200, 1, 2, 3)))
        Assert.Equal(1, extension.Frames)
        Assert.False(extension.IsComplete)
    End Sub

    <Fact>
    Public Sub A_sub_block_that_claims_less_than_it_holds_desyncs_into_a_stop_not_a_loop()
        ' The data says "1 byte, then the terminator" and carries more; the rest is read as a block and
        ' is not one. The frame itself had its terminator, so it counts.
        Dim lying As Byte() = Cat(Bytes(&H2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2), Bytes(1, &HAA, 0), Bytes(&HBB, &HCC, &HDD), Trailer())
        Dim result As GifWalkResult = WalkBoth(Cat(Header(9, 9), Frame(), lying, Frame()))
        Assert.Equal(2, result.Frames)
        Assert.False(result.IsComplete)
    End Sub

    <Fact>
    Public Sub An_unknown_block_ends_the_walk()
        Dim result As GifWalkResult = WalkBoth(Cat(Header(9, 9), Frame(), Bytes(&H77), Frame(), Trailer()))
        Assert.Equal(1, result.Frames)
        Assert.False(result.IsComplete)
    End Sub

    ''' <summary>A frame's data longer than the walker's buffer, in both skip branches.</summary>
    <Fact>
    Public Sub Frames_larger_than_the_read_buffer_are_skipped_whole()
        Dim data As Byte() = Cat(Header(100, 100), Frame(dataBytes:=200000), Frame(localTable:=True, dataBytes:=70000), Frame(dataBytes:=65536), Trailer())
        Dim result As GifWalkResult = WalkBoth(data)
        Assert.Equal(3, result.Frames)
        Assert.True(result.IsComplete)
    End Sub

    ''' <summary>A million empty extensions: every step consumes a byte, so the walk is as long as the file and no longer.</summary>
    <Fact>
    Public Sub A_file_of_nothing_but_empty_extensions_ends_with_the_file()
        Dim unit As Byte() = Bytes(&H21, &HF9, 0)
        Dim body(3 * 1000000 - 1) As Byte
        For i As Integer = 0 To 999999
            Array.Copy(unit, 0, body, i * 3, 3)
        Next
        Dim result As GifWalkResult = WalkBoth(Cat(Header(5, 5), body))
        Assert.True(result.IsGif)
        Assert.Equal(0, result.Frames)
        Assert.False(result.IsComplete)
    End Sub

    ''' <summary>Random bytes behind a good header, and a good file with bytes flipped: never an exception.</summary>
    <Fact>
    Public Sub Random_garbage_never_throws_and_never_overcounts()
        Dim noise As New Random(242)
        Dim head As Byte() = Header(50, 50, globalTable:=True)
        For round As Integer = 1 To 150
            Dim body(noise.Next(0, 3000)) As Byte
            noise.NextBytes(body)
            Dim result As GifWalkResult = WalkBoth(Cat(head, body))
            Assert.True(result.IsGif)
            ' A frame is at least 12 bytes (introducer, descriptor, code size, terminator).
            Assert.True(result.Frames * 12 <= body.Length, $"{result.Frames} frames in {body.Length} bytes")
        Next

        Dim good As Byte() = Cat(Header(50, 50, globalTable:=True), Netscape(), Frame(localTable:=True, dataBytes:=500), Frame(), Trailer())
        For round As Integer = 1 To 150
            Dim damaged As Byte() = DirectCast(good.Clone(), Byte())
            damaged(noise.Next(0, damaged.Length)) = CByte(noise.Next(0, 256))
            Dim result As GifWalkResult = WalkBoth(damaged)
            Assert.True(result.Frames * 12 <= damaged.Length, $"{result.Frames} frames in {damaged.Length} bytes")
        Next
    End Sub

    ''' <summary>A stream that fails half way is the end of the data: the frames before it are counted.</summary>
    <Fact>
    Public Sub A_stream_that_fails_is_the_end_of_the_data()
        Dim data As Byte() = Cat(Header(64, 48), Frame(dataBytes:=300), Frame(dataBytes:=300), Frame(dataBytes:=300), Trailer())
        Dim failAt As Long = Header(64, 48).Length + Frame(dataBytes:=300).Length * 2 + 20
        For Each seekable As Boolean In {False, True}
            Using stream As New ProbeStream(New MemoryStream(data), seekable, failAt)
                Dim result As GifWalkResult = GifBlockWalker.Walk(stream)
                Assert.True(result.IsGif)
                Assert.Equal(2, result.Frames)
                Assert.False(result.IsComplete)
            End Using
        Next

        ' A stream that fails before the first byte is not a GIF.
        Using stream As New ProbeStream(New MemoryStream(data), seekable:=False, failAfter:=0L)
            Assert.False(GifBlockWalker.Walk(stream).IsGif)
        End Using

        ' A stream already closed.
        Dim closed As New MemoryStream(data)
        closed.Dispose()
        Assert.False(GifBlockWalker.Walk(closed).IsGif)
    End Sub

    ''' <summary>A GIF made by a real encoder (ImageSharp), not by this file: the walk counts what the
    ''' encoder wrote.</summary>
    <Fact>
    Public Sub A_gif_written_by_a_real_encoder_is_counted()
        Using animation As Image(Of Rgba32) = AnimatedWebpFastOpenTests.NoiseAnimation()
            Using output As New MemoryStream()
                animation.Save(output, New GifEncoder())
                Dim result As GifWalkResult = WalkBoth(output.ToArray())
                Assert.True(result.IsGif)
                Assert.Equal(64, result.Width)
                Assert.Equal(48, result.Height)
                Assert.Equal(12, result.Frames)
                Assert.True(result.IsComplete)
            End Using
        End Using
    End Sub

    ''' <summary>The walk's answer feeds the policy: a walked GIF of 12 frames of 64 x 48 is not huge.</summary>
    <Fact>
    Public Sub The_walk_feeds_the_policy()
        Dim data As Byte() = Cat(Header(7000, 7000), Frame(), Frame(), Trailer())
        Dim walked As GifWalkResult = WalkBoth(data)
        Dim f As AnimationVideoFacts = Facts(AnimationVideoKind.Gif, walked.Width, walked.Height, walked.Frames, 3L * MiB)
        Assert.Equal("Eligible", Verdict(f))
        f.Frames = 1L
        Assert.Equal("NotAnimation", Verdict(f))
    End Sub

End Class
#End If
