#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports Xunit

Public Class HaloGradientTests
    <Fact>
    Public Sub The_rendered_gradient_matches_pixel_centres_in_both_axes_and_directions()
        For Each length As Integer In {1, 2, 3, 7, 19, 64, 257, 1024}
            For Each horizontal As Boolean In {False, True}
                For Each reverse As Boolean In {False, True}
                    For Each eased As Double In {0.0, 0.13, 0.5, 1.0}
                        For Each background As Color In {Color.Black, Color.White, Color.FromArgb(37, 109, 203)}
                            Dim bars As Color = Color.FromArgb(217, 53, 119)
                            Dim area As New Rectangle(3, 5, If(horizontal, length, 4), If(horizontal, 4, length))
                            Using frame As New Bitmap(area.Right + 3, area.Bottom + 3)
                                Using g As Graphics = Graphics.FromImage(frame)
                                    g.Clear(bars)
                                    g.PixelOffsetMode = PixelOffsetMode.Half
                                    HaloPainter.PaintFade(g, background, area, horizontal, reverse, eased)
                                End Using
                                Dim expected = LetterboxFillMath.HaloAlphaRow(length, eased)
                                For k As Integer = 0 To length - 1
                                    Dim axis As Integer = If(reverse, length - 1 - k, k)
                                    Dim pixel = frame.GetPixel(area.Left + If(horizontal, axis, 1), area.Top + If(horizontal, 1, axis))
                                    Dim observed As Integer() = {pixel.R, pixel.G, pixel.B}
                                    Dim source As Integer() = {bars.R, bars.G, bars.B}
                                    Dim target As Integer() = {background.R, background.G, background.B}
                                    For channel As Integer = 0 To 2
                                        Dim predicted As Double = source(channel) + (target(channel) - source(channel)) * expected(k) / 255.0
                                        Assert.True(Math.Abs(observed(channel) - predicted) <= 2.0,
                                            $"L={length}, horizontal={horizontal}, reverse={reverse}, eased={eased}, k={k}, channel={channel}: {observed(channel)} vs {predicted}")
                                    Next
                                Next
                                Assert.Equal(bars.ToArgb(), frame.GetPixel(0, 0).ToArgb())
                            End Using
                        Next
                    Next
                Next
            Next
        Next
    End Sub
End Class
#End If
