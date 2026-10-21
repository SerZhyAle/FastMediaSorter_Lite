#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports SixLabors.ImageSharp
Imports SixLabors.ImageSharp.Formats.Webp
Imports SixLabors.ImageSharp.PixelFormats
Imports Xunit

''' <summary>
''' Long animated WEBP, fast open (specification 241), phase B and C: the first frame as its
''' own payload (stage 1, rules 10 and 18). The animations are GENERATED here at run time, as
''' in <see cref="AnimatedWebpFastOpenTests"/>.
''' </summary>
Public Class AnimationFirstFrameTests

    Private Shared Function FirstFrame(source As Byte()) As DecodedPayload
        Using stream As New MemoryStream(source)
            Return New ModernImageSharpDecoder().DecodeFirstFramePayload(stream, "sample.webp")
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

        Using png As New MemoryStream(payload.Bytes)
            Dim info As ImageInfo = SixLabors.ImageSharp.Image.Identify(png)
            Assert.Equal(64, info.Width)
            Assert.Equal(48, info.Height)
            Assert.True(info.FrameMetadataCollection Is Nothing OrElse info.FrameMetadataCollection.Count <= 1, "a still PNG, not an animation")
        End Using
    End Sub

    ''' <summary>The preview shows what the animation's own first frame shows, pixel for pixel
    ''' (a lossless source, so the comparison is exact).</summary>
    <Fact>
    Public Sub The_first_frame_has_the_pixels_of_the_animations_first_frame()
        Dim source As Byte() = AnimatedWebpFastOpenTests.AnimatedWebp()
        Dim payload As DecodedPayload = FirstFrame(source)

        Using expected As Image(Of Rgba32) = SixLabors.ImageSharp.Image.Load(Of Rgba32)(source)
            Using actual As Image(Of Rgba32) = SixLabors.ImageSharp.Image.Load(Of Rgba32)(payload.Bytes)
                Assert.Equal(expected.Width, actual.Width)
                Assert.Equal(expected.Height, actual.Height)
                Dim differing As Integer = 0
                For y As Integer = 0 To expected.Height - 1
                    For x As Integer = 0 To expected.Width - 1
                        If expected.Frames.RootFrame(x, y) <> actual.Frames.RootFrame(x, y) Then differing += 1
                    Next
                Next
                Assert.Equal(0, differing)
            End Using
        End Using
    End Sub

    ''' <summary>A single-frame WEBP is no animation: stage 1 answers it exactly as the whole decode.</summary>
    <Fact>
    Public Sub A_still_is_answered_exactly_as_by_the_whole_decode()
        Dim still As Byte()
        Using image As Image(Of Rgba32) = AnimatedWebpFastOpenTests.NoiseAnimation()
            Do While image.Frames.Count > 1
                image.Frames.RemoveFrame(image.Frames.Count - 1)
            Loop
            Using output As New MemoryStream()
                image.Save(output, New WebpEncoder With {.FileFormat = WebpFileFormatType.Lossless})
                still = output.ToArray()
            End Using
        End Using

        Dim first As DecodedPayload = FirstFrame(still)
        Dim whole As DecodedPayload
        Using stream As New MemoryStream(still)
            whole = New ModernImageSharpDecoder().DecodeToPayload(stream)
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
