#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports ImageMagick
Imports Xunit

''' <summary>
''' Long animated WEBP, fast open (specification 241), phase B and C: the first frame as its
''' own payload (stage 1, rules 10 and 18). The animations are GENERATED here at run time, as
''' in <see cref="AnimatedWebpFastOpenTests"/>.
''' </summary>
Public Class AnimationFirstFrameTests

    Private Shared Function FirstFrame(source As Byte()) As DecodedPayload
        Using stream As New MemoryStream(source)
            Return New ModernMagickDecoder().DecodeFirstFramePayload(stream, "sample.webp")
        End Using
    End Function

    ''' <summary>Stage 1 is ONE picture: a PNG, the canvas size of the animation, flagged as the
    ''' stand-in it is, with the figures the later stages need.</summary>
    <Fact>
    Public Sub The_first_frame_is_one_png_of_the_animations_canvas()
        Dim payload As DecodedPayload = FirstFrame(AnimatedWebpFastOpenTests.AnimatedWebp())

        Assert.NotNull(payload)
        Assert.Equal(DecodedPayloadKind.Png, payload.Kind)
        Assert.True(payload.IsPreview)
        Assert.False(payload.IsAnimation)
        Assert.False(payload.IsTooLong)
        Assert.Equal(64, payload.Width)
        Assert.Equal(48, payload.Height)
        Assert.Equal(12, payload.Frames)
        Assert.Equal(12L * 80L, payload.DurationMs)

        Dim frames As List(Of IMagickImageInfo) = MagickImageInfo.ReadCollection(payload.Bytes).ToList()
        Assert.Single(frames)
        Assert.Equal(MagickFormat.Png, frames(0).Format)
        Assert.Equal(64UI, frames(0).Width)
        Assert.Equal(48UI, frames(0).Height)
    End Sub

    ''' <summary>The preview shows what the animation's own first frame shows, pixel for pixel
    ''' (a lossless source, so the comparison is exact).</summary>
    <Fact>
    Public Sub The_first_frame_has_the_pixels_of_the_animations_first_frame()
        Dim source As Byte() = AnimatedWebpFastOpenTests.AnimatedWebp()
        Dim payload As DecodedPayload = FirstFrame(source)

        Using animation As New MagickImageCollection(source)
            animation.Coalesce()
            Using actual As New MagickImage(payload.Bytes)
                Assert.Equal(animation(0).Width, actual.Width)
                Assert.Equal(animation(0).Height, actual.Height)
                ' Absolute = the number of pixels that differ at all (fuzz 0).
                Assert.Equal(0.0, animation(0).Compare(actual, ErrorMetric.Absolute))
            End Using
        End Using
    End Sub

    ''' <summary>A single-frame WEBP is no animation: stage 1 answers it exactly as the whole decode.</summary>
    <Fact>
    Public Sub A_still_is_answered_exactly_as_by_the_whole_decode()
        Dim still As Byte()
        Using frames As MagickImageCollection = AnimatedWebpFastOpenTests.NoiseAnimation()
            frames(0).Settings.SetDefine(MagickFormat.WebP, "lossless", True)
            still = frames(0).ToByteArray(MagickFormat.WebP)
        End Using

        Dim first As DecodedPayload = FirstFrame(still)
        Dim whole As DecodedPayload
        Using stream As New MemoryStream(still)
            whole = New ModernMagickDecoder().DecodeToPayload(stream)
        End Using

        Assert.NotNull(first)
        Assert.False(first.IsPreview)
        Assert.False(first.IsAnimation)
        Assert.Equal(whole.Bytes, first.Bytes)
    End Sub

    ''' <summary>The header's transparency flag rides with the preview (it feeds the confirmation of
    ''' "Replace with video" for an animation that is never played).</summary>
    <Fact>
    Public Sub The_header_says_whether_the_animation_has_transparency()
        Assert.True(FirstFrame(AnimatedWebpFastOpenTests.AnimatedWebp(withAlpha:=True)).HasAlpha)
        Assert.False(FirstFrame(AnimatedWebpFastOpenTests.AnimatedWebp()).HasAlpha)
    End Sub

    ''' <summary>Garbage is no picture: Nothing or a refusal, never a preview of nothing.</summary>
    <Fact>
    Public Sub Bytes_that_are_no_image_give_no_preview()
        Dim thrown As Boolean = False
        Dim payload As DecodedPayload = Nothing
        Try
            payload = FirstFrame(New Byte() {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16})
        Catch ex As Exception
            thrown = True
        End Try
        Assert.True(thrown OrElse payload Is Nothing)
    End Sub

End Class
#End If
