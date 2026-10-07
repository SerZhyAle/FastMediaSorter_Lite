Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports Xunit

''' <summary>
''' Tests for 018_SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md (U-13 and U-15).
''' </summary>
Public Class EdgeSamplerTests

    <Fact>
    Public Sub U13_ImageOwnsData_contract_holds_across_platforms()
        Dim decoder = ImageDecoderProvider.Current
        Assert.NotNull(decoder)
#If NETFRAMEWORK Then
        Assert.False(decoder.ImageOwnsData)
#Else
        Assert.True(decoder.ImageOwnsData)
#End If
    End Sub

    <Fact>
    Public Sub U15_EdgeSampler_GetPixel_reads_correct_color()
        Using bmp As New Bitmap(10, 10, PixelFormat.Format32bppArgb)
            bmp.SetPixel(2, 3, Color.FromArgb(255, 100, 150, 200))
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Assert.Equal(10, sampler.Width)
                Assert.Equal(10, sampler.Height)

                Dim c = sampler.GetPixel(2, 3)
                Assert.Equal(255, c.A)
                Assert.Equal(100, c.R)
                Assert.Equal(150, c.G)
                Assert.Equal(200, c.B)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub U15_EdgeSampler_CalculateBackgroundColor_auto_scheme()
        Using bmp As New Bitmap(20, 20, PixelFormat.Format32bppArgb)
            Using g As Graphics = Graphics.FromImage(bmp)
                g.Clear(Color.FromArgb(255, 50, 60, 70))
            End Using
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Dim back = sampler.CalculateBackgroundColor(0, 1, 1, 2, 2, 30L, 2.0, 1, Color.White)
                Assert.Equal(50, back.R)
                Assert.Equal(60, back.G)
                Assert.Equal(70, back.B)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub U15_EdgeSampler_CalculateBackgroundColor_side_scheme()
        Using bmp As New Bitmap(10, 10, PixelFormat.Format32bppArgb)
            For y As Integer = 0 To 9
                bmp.SetPixel(1, y, Color.FromArgb(255, 10, 20, 30))
            Next
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Dim back = sampler.CalculateBackgroundColor(3, 1, 1, 2, 2, 30L, 2.0, 1, Color.White)
                Assert.Equal(10, back.R)
                Assert.Equal(20, back.G)
                Assert.Equal(30, back.B)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' The GetPixel code this replaced averaged with <c>CInt(sum / count)</c>, which rounds
    ''' half to even; an integer division truncates and tints every photo one level darker
    ''' on a half. Two samples that sum to an odd number tell the two apart: 11 and 12 average
    ''' 11.5, which rounds to 12 and truncates to 11.
    ''' </summary>
    <Fact>
    Public Sub U15_EdgeSampler_side_top_bottom_averages_round_like_the_original()
        ' by side: column x = 1, rows 0..1
        Using bmp As New Bitmap(2, 2, PixelFormat.Format32bppArgb)
            bmp.SetPixel(1, 0, Color.FromArgb(255, 11, 1, 5))
            bmp.SetPixel(1, 1, Color.FromArgb(255, 12, 2, 6))
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Dim side = sampler.CalculateBackgroundColor(3, 1, 1, 2, 2, 30L, 2.0, 1, Color.White)
                Assert.Equal(12, side.R) ' (11 + 12) / 2 = 11.5 -> 12, not 11
                Assert.Equal(2, side.G)  ' 1.5 -> 2
                Assert.Equal(6, side.B)  ' 5.5 -> 6
            End Using
        End Using

        ' by top: row y = 1, columns 0..1
        Using bmp As New Bitmap(2, 3, PixelFormat.Format32bppArgb)
            bmp.SetPixel(0, 1, Color.FromArgb(255, 11, 1, 5))
            bmp.SetPixel(1, 1, Color.FromArgb(255, 12, 2, 6))
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Dim top = sampler.CalculateBackgroundColor(4, 1, 1, 2, 2, 30L, 2.0, 1, Color.White)
                Assert.Equal(12, top.R)
                Assert.Equal(2, top.G)
                Assert.Equal(6, top.B)
            End Using
        End Using

        ' by bottom: row y = Height - 1, columns 0..1
        Using bmp As New Bitmap(2, 3, PixelFormat.Format32bppArgb)
            bmp.SetPixel(0, 2, Color.FromArgb(255, 21, 1, 5))
            bmp.SetPixel(1, 2, Color.FromArgb(255, 22, 2, 6))
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Dim bottom = sampler.CalculateBackgroundColor(5, 1, 1, 2, 2, 30L, 2.0, 1, Color.White)
                Assert.Equal(22, bottom.R) ' 21.5 -> 22
                Assert.Equal(2, bottom.G)
                Assert.Equal(6, bottom.B)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub U15_EdgeSampler_edge_sampling_methods()
        Using bmp As New Bitmap(10, 10, PixelFormat.Format32bppArgb)
            Using g As Graphics = Graphics.FromImage(bmp)
                g.Clear(Color.FromArgb(255, 80, 90, 100))
            End Using
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Dim vColor = sampler.SampleVerticalEdgeColor(True, 5, 2)
                Assert.Equal(80, vColor.R)
                Assert.Equal(90, vColor.G)
                Assert.Equal(100, vColor.B)

                Dim hColor = sampler.SampleHorizontalEdgeColor(True, 5, 2)
                Assert.Equal(80, hColor.R)
                Assert.Equal(90, hColor.G)
                Assert.Equal(100, hColor.B)

                Dim vList = sampler.GetDisplayedVerticalEdgeColors(10, New Rectangle(0, 0, 10, 10), True)
                Assert.Equal(10, vList.Count)
                Assert.Equal(80, vList(0).R)

                Dim hList = sampler.GetDisplayedHorizontalEdgeColors(10, New Rectangle(0, 0, 10, 10), True)
                Assert.Equal(10, hList.Count)
                Assert.Equal(80, hList(0).R)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' The sampler reads a few dozen pixels per frame, so it must not copy the raster: a copy of a
    ''' 24 MP photo is ~19-38 ms and ~96 MB per page flip against microseconds for the reads
    ''' (see the class remarks). A pixel written after the sampler was made is therefore what the
    ''' next read returns - a snapshot would hand back the old value.
    ''' </summary>
    <Fact>
    Public Sub U15_EdgeSampler_reads_the_live_bitmap_and_never_snapshots_it()
        Using bmp As New Bitmap(4, 4, PixelFormat.Format32bppArgb)
            bmp.SetPixel(2, 2, Color.FromArgb(255, 1, 2, 3))
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Assert.Equal(1, sampler.GetPixel(2, 2).R)
                bmp.SetPixel(2, 2, Color.FromArgb(255, 9, 8, 7))
                Dim after = sampler.GetPixel(2, 2)
                Assert.Equal(9, after.R)
                Assert.Equal(8, after.G)
                Assert.Equal(7, after.B)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' A transparent pixel comes back opaque (the background colour must never be see-through),
    ''' and out-of-range reads clamp to the nearest edge pixel instead of throwing.
    ''' </summary>
    <Fact>
    Public Sub U15_EdgeSampler_returns_opaque_colors_and_clamps_out_of_range_reads()
        Using bmp As New Bitmap(3, 3, PixelFormat.Format32bppArgb)
            bmp.SetPixel(0, 0, Color.FromArgb(10, 100, 110, 120))
            bmp.SetPixel(2, 2, Color.FromArgb(255, 7, 8, 9))
            Using sampler As EdgeSampler = EdgeSampler.TryCreate(bmp)
                Assert.NotNull(sampler)
                Assert.Equal(255, sampler.GetPixel(0, 0).A)
                Assert.Equal(sampler.GetPixel(0, 0), sampler.GetPixel(-5, -5))
                Assert.Equal(sampler.GetPixel(2, 2), sampler.GetPixel(50, 50))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub U15_EdgeSampler_handles_null_or_empty_safely()
        Assert.Null(EdgeSampler.TryCreate(Nothing))
        Using emptyBmp As New Bitmap(1, 1)
            ' Disposed bitmap
            emptyBmp.Dispose()
            Assert.Null(EdgeSampler.TryCreate(emptyBmp))
        End Using
    End Sub

End Class
