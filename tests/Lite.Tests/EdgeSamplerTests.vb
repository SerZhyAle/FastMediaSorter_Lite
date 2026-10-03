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
