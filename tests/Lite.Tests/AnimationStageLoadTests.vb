#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports SixLabors.ImageSharp.Formats.Webp
Imports SixLabors.ImageSharp.PixelFormats
Imports Xunit

''' <summary>
''' Long animated WEBP, fast open (specification 241), phase B: what each loading stage hands back
''' for a long and for a small animation, through the real loader (<c>FileManager.LoadImageStaged</c>
''' -> <c>DecodeCache.LoadImage</c> -> the decoder). The decode cache is switched off, so what is
''' loaded is the decode itself. Animations are generated here at run time.
''' </summary>
<Collection("DecodeCacheBudget")>
Public Class AnimationStageLoadTests

    Private Const LongSide As Integer = 400
    Private Const LongFrames As Integer = 70       ' 400 x 400 x 70 = 11.2 Mpx, over the 10 Mpx threshold

    ''' <summary>Solid-colour frames, so the encode and the later GIF transcode cost milliseconds
    ''' while the animation is still over the stage-1 threshold.</summary>
    Private Shared Function LongAnimationWebp(Optional withAlpha As Boolean = False) As Byte()
        Dim image As SixLabors.ImageSharp.Image(Of Rgba32) = Nothing
        Try
            For f As Integer = 0 To LongFrames - 1
                Dim pixels(LongSide * LongSide * 4 - 1) As Byte
                Dim red As Byte = CByte((f * 3) And 255)
                Dim green As Byte = CByte(255 - ((f * 3) And 255))
                For at As Integer = 0 To pixels.Length - 4 Step 4
                    pixels(at) = red
                    pixels(at + 1) = green
                    pixels(at + 2) = 90
                    pixels(at + 3) = If(withAlpha AndAlso at < 4 * LongSide, CByte(0), CByte(255))
                Next
                Using one As SixLabors.ImageSharp.Image(Of Rgba32) = SixLabors.ImageSharp.Image.LoadPixelData(Of Rgba32)(pixels, LongSide, LongSide)
                    If image Is Nothing Then
                        image = one.Clone()
                    Else
                        image.Frames.AddFrame(one.Frames.RootFrame)
                    End If
                End Using
            Next
            For Each frame As SixLabors.ImageSharp.ImageFrame(Of Rgba32) In image.Frames
                frame.Metadata.GetFormatMetadata(WebpFormat.Instance).FrameDelay = 80UI
            Next
            Using output As New MemoryStream()
                image.Save(output, New WebpEncoder With {.FileFormat = WebpFileFormatType.Lossless})
                Return output.ToArray()
            End Using
        Finally
            image?.Dispose()
        End Try
    End Function

    Private Shared Function WriteTemp(bytes As Byte(), ext As String) As String
        Dim tmpPath As String = Path.Combine(Path.GetTempPath(), "fms_stagetest_" & Guid.NewGuid().ToString("N") & ext)
        File.WriteAllBytes(tmpPath, bytes)
        Return tmpPath
    End Function

    ''' <summary>Runs <paramref name="check"/> on the load of <paramref name="bytes"/> at one stage, with the
    ''' cache off and the picture and its stream disposed afterwards.</summary>
    Private Shared Sub WithLoaded(bytes As Byte(), stage As ImageLoadStage, check As Action(Of Tuple(Of System.Drawing.Image, MemoryStream)))
        Dim saved As Integer = DecodeCacheStore.BudgetMb
        DecodeCacheStore.BudgetMb = 0
        Dim path As String = WriteTemp(bytes, ".webp")
        Try
            Dim failure As PathFailureKind = PathFailureKind.None
            Dim loaded As Tuple(Of System.Drawing.Image, MemoryStream) = FileManager.LoadImageStaged(path, failure, stage)
            Assert.NotNull(loaded)
            Try
                check(loaded)
            Finally
                AnimationStaging.Unmark(loaded.Item1)
                loaded.Item1.Dispose()
                loaded.Item2?.Dispose()
            End Try
        Finally
            DecodeCacheStore.BudgetMb = saved
            File.Delete(path)
        End Try
    End Sub

    Private Shared Function FrameCountOf(picture As System.Drawing.Image) As Integer
        Return picture.GetFrameCount(FrameDimension.Time)
    End Function

    ''' <summary>A long animation arrives at the Staged stage as its first frame: a plain picture of
    ''' the canvas, no stream, marked with the figures of the whole animation.</summary>
    <Fact>
    Public Sub Staged_gives_a_long_animation_as_a_marked_first_frame()
        WithLoaded(LongAnimationWebp(), ImageLoadStage.Staged,
            Sub(loaded)
                Assert.Null(loaded.Item2)
                Assert.Equal(New System.Drawing.Size(LongSide, LongSide), loaded.Item1.Size)
                Assert.Equal(1, FrameCountOf(loaded.Item1))
                Assert.NotEqual(ImageFormat.Gif, loaded.Item1.RawFormat)

                Dim info As StagedInfo = AnimationStaging.InfoOf(loaded.Item1)
                Assert.NotNull(info)
                Assert.Equal(LongFrames, info.Frames)
                Assert.Equal(LongSide, info.Width)
                Assert.Equal(LongSide, info.Height)
                Assert.Equal(LongFrames * 80L, info.DurationMs)
                Assert.False(info.HasAlpha)
                Assert.False(info.IsTooLong)
            End Sub)
    End Sub

    <Fact>
    Public Sub Staged_reports_transparency_of_a_long_animation_from_its_header()
        WithLoaded(LongAnimationWebp(withAlpha:=True), ImageLoadStage.Staged,
            Sub(loaded)
                Dim info As StagedInfo = AnimationStaging.InfoOf(loaded.Item1)
                Assert.NotNull(info)
                Assert.True(info.HasAlpha)
            End Sub)
    End Sub

    ''' <summary>The Whole stage is today's behaviour: the complete GIF, its stream kept for GDI+,
    ''' and no mark - this is what the upgrade worker asks for.</summary>
    <Fact>
    Public Sub Whole_gives_the_complete_animation_unmarked()
        WithLoaded(LongAnimationWebp(), ImageLoadStage.Whole,
            Sub(loaded)
                Assert.NotNull(loaded.Item2)
                Assert.Equal(ImageFormat.Gif, loaded.Item1.RawFormat)
                Assert.Equal(LongFrames, FrameCountOf(loaded.Item1))
                Assert.Null(AnimationStaging.InfoOf(loaded.Item1))
            End Sub)
    End Sub

    ''' <summary>A small animation keeps today's single step even at the Staged stage (rule 9).</summary>
    <Fact>
    Public Sub Staged_keeps_a_small_animation_whole()
        WithLoaded(AnimatedWebpFastOpenTests.AnimatedWebp(), ImageLoadStage.Staged,
            Sub(loaded)
                Assert.NotNull(loaded.Item2)
                Assert.Equal(ImageFormat.Gif, loaded.Item1.RawFormat)
                Assert.True(FrameCountOf(loaded.Item1) > 1)
                Assert.Null(AnimationStaging.InfoOf(loaded.Item1))
            End Sub)
    End Sub

    ''' <summary>A thumbnail wants the first frame of any animation, however small (rule 14).</summary>
    <Fact>
    Public Sub FirstFrame_gives_even_a_small_animation_as_one_picture()
        WithLoaded(AnimatedWebpFastOpenTests.AnimatedWebp(), ImageLoadStage.FirstFrame,
            Sub(loaded)
                Assert.Null(loaded.Item2)
                Assert.Equal(New System.Drawing.Size(64, 48), loaded.Item1.Size)
                Assert.Equal(1, FrameCountOf(loaded.Item1))
            End Sub)
    End Sub

    ''' <summary>A still is a still at every stage: the same picture, no mark.</summary>
    <Theory>
    <InlineData(0)>   ' Whole
    <InlineData(1)>   ' Staged
    <InlineData(2)>   ' FirstFrame
    Public Sub A_still_webp_is_loaded_the_same_at_every_stage(stageNumber As Integer)
        Dim stage As ImageLoadStage = CType(stageNumber, ImageLoadStage)
        Dim bytes As Byte()
        Using one As SixLabors.ImageSharp.Image(Of Rgba32) = SixLabors.ImageSharp.Image.LoadPixelData(Of Rgba32)(New Byte(30 * 20 * 4 - 1) {}, 30, 20)
            Using output As New MemoryStream()
                one.Save(output, New WebpEncoder With {.FileFormat = WebpFileFormatType.Lossless})
                bytes = output.ToArray()
            End Using
        End Using

        WithLoaded(bytes, stage,
            Sub(loaded)
                Assert.Equal(New System.Drawing.Size(30, 20), loaded.Item1.Size)
                Assert.Null(AnimationStaging.InfoOf(loaded.Item1))
            End Sub)
    End Sub

End Class
#End If
