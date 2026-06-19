Option Strict On

Imports System.Drawing

Partial Public Class Main_Form

    ' One-shot timer that retries a perspective redraw the throttle dropped, so the
    ' image we finally land on after fast scrolling still gets its bars. Without it a
    ' draw skipped inside the throttle window was never retried.
    Private WithEvents perspective_Trailing_Timer As New System.Windows.Forms.Timer() With {.Interval = how_long_wait_before_draw_perspective}

    Private Sub ArmTrailingPerspectiveRedraw()
        ' Restart on every skipped request so only the last one survives, firing once
        ' the throttle window has elapsed (i.e. shortly after scrolling stops).
        perspective_Trailing_Timer.Stop()
        perspective_Trailing_Timer.Start()
    End Sub

    Private Sub Perspective_Trailing_Timer_Tick(sender As Object, e As EventArgs) Handles perspective_Trailing_Timer.Tick
        perspective_Trailing_Timer.Stop()
        Draw_Perspective()
    End Sub

    Private Function CheckCornerColorsAndSetBeginPoint(list_of_corner_colors As List(Of System.Drawing.Color)) As Long

        Dim corner_colors_Count As Integer = list_of_corner_colors.Count
        If corner_colors_Count > percent_of_color_smooth_To_Remove Then
            Dim color_Trim_Count As Integer = CInt(Math.Floor(corner_colors_Count * percent_of_color_smooth_To_Remove / 100))

            ' Prepare sorted lists for each channel
            Dim sorted_R = list_of_corner_colors.Select(Function(c) CInt(c.R)).OrderBy(Function(v) v).ToList()
            Dim sorted_G = list_of_corner_colors.Select(Function(c) CInt(c.G)).OrderBy(Function(v) v).ToList()
            Dim sorted_B = list_of_corner_colors.Select(Function(c) CInt(c.B)).OrderBy(Function(v) v).ToList()

            ' Remove bottom and top 10%
            sorted_R = sorted_R.Skip(color_Trim_Count).Take(sorted_R.Count - 2 * color_Trim_Count).ToList()
            sorted_G = sorted_G.Skip(color_Trim_Count).Take(sorted_G.Count - 2 * color_Trim_Count).ToList()
            sorted_B = sorted_B.Skip(color_Trim_Count).Take(sorted_B.Count - 2 * color_Trim_Count).ToList()

            ' Calculate min, max, and diffSum on trimmed lists
            Dim maxR As Integer = sorted_R.Max()
            Dim minR As Integer = sorted_R.Min()
            Dim maxG As Integer = sorted_G.Max()
            Dim minG As Integer = sorted_G.Min()
            Dim maxB As Integer = sorted_B.Max()
            Dim minB As Integer = sorted_B.Min()

            Return (maxR - minR) + (maxG - minG) + (maxB - minB)

        Else
            ' Fallback to original logic if not enough data
            Dim maxR As Integer = list_of_corner_colors.Max(Function(c) CInt(c.R))
            Dim maxG As Integer = list_of_corner_colors.Max(Function(c) CInt(c.G))
            Dim maxB As Integer = list_of_corner_colors.Max(Function(c) CInt(c.B))

            Dim minR As Integer = list_of_corner_colors.Min(Function(c) CInt(c.R))
            Dim minG As Integer = list_of_corner_colors.Min(Function(c) CInt(c.G))
            Dim minB As Integer = list_of_corner_colors.Min(Function(c) CInt(c.B))

            Return (maxR - minR) + (maxG - minG) + (maxB - minB)
        End If

    End Function

    Private Function SmoothColorList(colors As List(Of Color), radius As Integer) As List(Of Color)
        If radius < 1 OrElse colors.Count < 3 Then Return colors
        Dim result As New List(Of Color)(colors.Count)
        For i As Integer = 0 To colors.Count - 1
            Dim r_sum As Integer = 0, g_sum As Integer = 0, b_sum As Integer = 0
            Dim j_start As Integer = Math.Max(0, i - radius)
            Dim j_end As Integer = Math.Min(colors.Count - 1, i + radius)
            For j As Integer = j_start To j_end
                r_sum += colors(j).R
                g_sum += colors(j).G
                b_sum += colors(j).B
            Next
            Dim count As Integer = j_end - j_start + 1
            result.Add(Color.FromArgb(255, r_sum \ count, g_sum \ count, b_sum \ count))
        Next
        Return result
    End Function

    Private Function GetEdgeSampleDepth(scale As Double, sourceSize As Integer) As Integer
        Dim safeScale As Double = Math.Max(scale, 0.0001)
        Dim scaledSampleDepth As Integer = CInt(Math.Ceiling(1.0 / safeScale))
        Dim maxDepth As Integer = Math.Min(sourceSize, 8)
        Return Math.Min(Math.Max(scaledSampleDepth, 2), maxDepth)
    End Function

    Private Function SampleVerticalEdgeColor(sourceBitmap As Bitmap, isLeftEdge As Boolean, sourceY As Integer, sampleDepth As Integer) As Color
        Dim clampedY As Integer = Math.Max(0, Math.Min(sourceY, sourceBitmap.Height - 1))
        Dim effectiveDepth As Integer = Math.Max(1, Math.Min(sampleDepth, sourceBitmap.Width))
        Dim startX As Integer = If(isLeftEdge, 0, sourceBitmap.Width - effectiveDepth)
        Dim sumR As Integer = 0
        Dim sumG As Integer = 0
        Dim sumB As Integer = 0

        For x As Integer = startX To startX + effectiveDepth - 1
            Dim px = sourceBitmap.GetPixel(x, clampedY)
            sumR += CInt(px.R)
            sumG += CInt(px.G)
            sumB += CInt(px.B)
        Next

        Return Color.FromArgb(255, sumR \ effectiveDepth, sumG \ effectiveDepth, sumB \ effectiveDepth)
    End Function

    Private Function SampleHorizontalEdgeColor(sourceBitmap As Bitmap, isTopEdge As Boolean, sourceX As Integer, sampleDepth As Integer) As Color
        Dim clampedX As Integer = Math.Max(0, Math.Min(sourceX, sourceBitmap.Width - 1))
        Dim effectiveDepth As Integer = Math.Max(1, Math.Min(sampleDepth, sourceBitmap.Height))
        Dim startY As Integer = If(isTopEdge, 0, sourceBitmap.Height - effectiveDepth)
        Dim sumR As Integer = 0
        Dim sumG As Integer = 0
        Dim sumB As Integer = 0

        For y As Integer = startY To startY + effectiveDepth - 1
            Dim px = sourceBitmap.GetPixel(clampedX, y)
            sumR += CInt(px.R)
            sumG += CInt(px.G)
            sumB += CInt(px.B)
        Next

        Return Color.FromArgb(255, sumR \ effectiveDepth, sumG \ effectiveDepth, sumB \ effectiveDepth)
    End Function

    Private Function GetTrimmedChannelAverage(channelValues As IEnumerable(Of Integer)) As Integer
        Dim sortedValues = channelValues.OrderBy(Function(v) v).ToList()
        If sortedValues.Count = 0 Then Return 0

        Dim trimCount As Integer = CInt(Math.Floor(sortedValues.Count * percent_of_color_smooth_To_Remove / 100.0))
        Dim maxAllowedTrim As Integer = Math.Max(0, (sortedValues.Count - 1) \ 2)
        trimCount = Math.Min(trimCount, maxAllowedTrim)

        Dim trimmedValues = sortedValues.Skip(trimCount).Take(sortedValues.Count - 2 * trimCount).ToList()
        If trimmedValues.Count = 0 Then trimmedValues = sortedValues

        Return CInt(Math.Round(trimmedValues.Average()))
    End Function

    Private Function GetTrimmedAverageColor(colors As List(Of Color)) As Color
        If colors.Count = 0 Then Return Color.Black

        Dim avgR As Integer = GetTrimmedChannelAverage(colors.Select(Function(c) CInt(c.R)))
        Dim avgG As Integer = GetTrimmedChannelAverage(colors.Select(Function(c) CInt(c.G)))
        Dim avgB As Integer = GetTrimmedChannelAverage(colors.Select(Function(c) CInt(c.B)))

        Return Color.FromArgb(255, avgR, avgG, avgB)
    End Function

    Private Function GetZoomedImageRectangle(sourceWidth As Integer, sourceHeight As Integer, targetWidth As Integer, targetHeight As Integer) As Rectangle
        If sourceWidth <= 0 OrElse
            sourceHeight <= 0 OrElse
            targetWidth <= 0 OrElse
            targetHeight <= 0 Then

            Return Rectangle.Empty
        End If

        Dim scale As Double = Math.Min(targetWidth / CDbl(sourceWidth), targetHeight / CDbl(sourceHeight))
        Dim scaledWidth As Integer = Math.Max(1, Math.Min(targetWidth, CInt(Math.Round(sourceWidth * scale))))
        Dim scaledHeight As Integer = Math.Max(1, Math.Min(targetHeight, CInt(Math.Round(sourceHeight * scale))))
        Dim left As Integer = (targetWidth - scaledWidth) \ 2
        Dim top As Integer = (targetHeight - scaledHeight) \ 2

        Return New Rectangle(left, top, scaledWidth, scaledHeight)
    End Function

    Private Function CreateDisplayedImageBitmap(sourceImage As Image, displaySize As Size, backgroundColor As Color) As Bitmap
        Dim displayedBitmap As New Bitmap(displaySize.Width, displaySize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb)

        Using g As Graphics = Graphics.FromImage(displayedBitmap)
            g.Clear(Color.FromArgb(255, backgroundColor.R, backgroundColor.G, backgroundColor.B))
            ' Wrap mode TileFlipXY prevents GDI+ from blending the outermost row/column
            ' with the off-image (cleared background) area during downscaling. Without it,
            ' the sampled edge column is pulled toward the dark BackColor, so the perspective
            ' bars start darker than the real image edge and the seam becomes visible.
            Dim destRect As New Rectangle(0, 0, displaySize.Width, displaySize.Height)
            Using attr As New System.Drawing.Imaging.ImageAttributes()
                attr.SetWrapMode(Drawing2D.WrapMode.TileFlipXY)
                g.DrawImage(sourceImage, destRect, 0, 0, sourceImage.Width, sourceImage.Height, GraphicsUnit.Pixel, attr)
            End Using
        End Using

        Return displayedBitmap
    End Function

    Private Function GetSteppedColors(colors As List(Of Color), stepSize As Integer) As List(Of Color)
        Dim samples As New List(Of Color)
        Dim safeStep As Integer = Math.Max(1, stepSize)

        For i As Integer = 0 To colors.Count - 1 Step safeStep
            samples.Add(colors(i))
        Next

        If samples.Count = 0 AndAlso colors.Count > 0 Then
            samples.Add(colors(colors.Count - 1))
        End If

        Return samples
    End Function

    Private Function GetDisplayedVerticalEdgeColors(displayedBitmap As Bitmap, targetHeight As Integer, imageRect As Rectangle, isLeftEdge As Boolean) As List(Of Color)
        Dim edgeColors As New List(Of Color)(targetHeight)
        ' Sample one pixel in from the very edge. The outermost downscaled column folds
        ' in the image's darkest border/vignette pixels and comes out a few levels darker
        ' than what the PictureBox (SizeMode = Zoom) actually paints for the photo's edge,
        ' which left a faint dark vertical seam between the photo and the bar. The second
        ' column equals the displayed photo edge, so the bar now joins it seamlessly.
        Dim edgeInset As Integer = If(displayedBitmap.Width >= 3, 1, 0)
        Dim sourceX As Integer = If(isLeftEdge, edgeInset, displayedBitmap.Width - 1 - edgeInset)

        For targetY As Integer = 0 To targetHeight - 1
            Dim sourceY As Integer = Math.Max(0, Math.Min(targetY - imageRect.Top, displayedBitmap.Height - 1))
            Dim px As Color = displayedBitmap.GetPixel(sourceX, sourceY)
            edgeColors.Add(Color.FromArgb(255, px.R, px.G, px.B))
        Next

        Return edgeColors
    End Function

    Private Function GetDisplayedHorizontalEdgeColors(displayedBitmap As Bitmap, targetWidth As Integer, imageRect As Rectangle, isTopEdge As Boolean) As List(Of Color)
        Dim edgeColors As New List(Of Color)(targetWidth)
        ' One pixel in from the very edge - see GetDisplayedVerticalEdgeColors for why
        ' the outermost downscaled row darkens the seam.
        Dim edgeInset As Integer = If(displayedBitmap.Height >= 3, 1, 0)
        Dim sourceY As Integer = If(isTopEdge, edgeInset, displayedBitmap.Height - 1 - edgeInset)

        For targetX As Integer = 0 To targetWidth - 1
            Dim sourceX As Integer = Math.Max(0, Math.Min(targetX - imageRect.Left, displayedBitmap.Width - 1))
            Dim px As Color = displayedBitmap.GetPixel(sourceX, sourceY)
            edgeColors.Add(Color.FromArgb(255, px.R, px.G, px.B))
        Next

        Return edgeColors
    End Function

    Private Function PaintVerticalPerspectiveBackground(g As Graphics, edgeColors As List(Of Color), targetRect As Rectangle, colorDeviationThreshold As Double) As Boolean
        If targetRect.Width <= 0 OrElse
            targetRect.Height <= 0 OrElse
            edgeColors.Count = 0 Then

            Return False
        End If

        Dim sampledColors As List(Of Color) = GetSteppedColors(edgeColors, step_size_while_color_Search)

        If CheckCornerColorsAndSetBeginPoint(sampledColors) < colorDeviationThreshold Then
            Using brush As New SolidBrush(GetTrimmedAverageColor(sampledColors))
                g.FillRectangle(brush, targetRect)
            End Using
        Else
            Dim smoothedColors As List(Of Color) = SmoothColorList(edgeColors, CInt(edgeColors.Count * smoothIndex) + 1)

            For y As Integer = targetRect.Top To targetRect.Bottom - 1
                Dim colorIndex As Integer = Math.Max(0, Math.Min(y, smoothedColors.Count - 1))
                Using brush As New SolidBrush(smoothedColors(colorIndex))
                    g.FillRectangle(brush, targetRect.Left, y, targetRect.Width, 1)
                End Using
            Next
        End If

        Return True
    End Function

    Private Function PaintHorizontalPerspectiveBackground(g As Graphics, edgeColors As List(Of Color), targetRect As Rectangle, colorDeviationThreshold As Double) As Boolean
        If targetRect.Width <= 0 OrElse
            targetRect.Height <= 0 OrElse
            edgeColors.Count = 0 Then

            Return False
        End If

        Dim sampledColors As List(Of Color) = GetSteppedColors(edgeColors, step_size_while_color_Search)

        If CheckCornerColorsAndSetBeginPoint(sampledColors) < colorDeviationThreshold Then
            Using brush As New SolidBrush(GetTrimmedAverageColor(sampledColors))
                g.FillRectangle(brush, targetRect)
            End Using
        Else
            Dim smoothedColors As List(Of Color) = SmoothColorList(edgeColors, CInt(edgeColors.Count * smoothIndex) + 1)

            For x As Integer = targetRect.Left To targetRect.Right - 1
                Dim colorIndex As Integer = Math.Max(0, Math.Min(x, smoothedColors.Count - 1))
                Using brush As New SolidBrush(smoothedColors(colorIndex))
                    g.FillRectangle(brush, x, targetRect.Top, 1, targetRect.Height)
                End Using
            Next
        End If

        Return True
    End Function

    Private Sub PaintVerticalSeamBand(g As Graphics, edgeColors As List(Of Color), targetHeight As Integer, startX As Integer, bandWidth As Integer)
        Dim safeBandWidth As Integer = Math.Max(0, bandWidth)
        If safeBandWidth <= 0 OrElse edgeColors.Count = 0 Then
            Return
        End If

        For y As Integer = 0 To targetHeight - 1
            Dim colorIndex As Integer = Math.Max(0, Math.Min(y, edgeColors.Count - 1))
            Using brush As New SolidBrush(edgeColors(colorIndex))
                g.FillRectangle(brush, startX, y, safeBandWidth, 1)
            End Using
        Next
    End Sub

    Private Sub PaintHorizontalSeamBand(g As Graphics, edgeColors As List(Of Color), targetWidth As Integer, startY As Integer, bandHeight As Integer)
        Dim safeBandHeight As Integer = Math.Max(0, bandHeight)
        If safeBandHeight <= 0 OrElse edgeColors.Count = 0 Then
            Return
        End If

        For x As Integer = 0 To targetWidth - 1
            Dim colorIndex As Integer = Math.Max(0, Math.Min(x, edgeColors.Count - 1))
            Using brush As New SolidBrush(edgeColors(colorIndex))
                g.FillRectangle(brush, x, startY, 1, safeBandHeight)
            End Using
        Next
    End Sub

    Private Sub Draw_Perspective()

        '  Dim sw As New Stopwatch()
        '   sw.Start()

        Dim perspective_Applies As Boolean =
            Is_Pespective AndAlso
            (is_PictureBox1_Visible OrElse
            is_PictureBox2_Visible) AndAlso
            (Not Is_slide_show_mode Or
            SlideShowTimer.Interval > slideshow_limit_to_change_color)

        Dim throttle_Elapsed As Boolean =
            last_Perspective_Draw_Time < DateTime.Now.Subtract(TimeSpan.FromMilliseconds(how_long_wait_before_draw_perspective))

        ' A redraw is wanted but came too soon after the last one: defer it instead
        ' of dropping it. (Fast scrolling otherwise lost the draw for the image we
        ' settled on, so it showed no perspective bars until the next resize/reload.)
        If perspective_Applies AndAlso Not throttle_Elapsed Then
            ArmTrailingPerspectiveRedraw()
        End If

        If perspective_Applies AndAlso throttle_Elapsed Then

            perspective_Trailing_Timer.Stop()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1129: corners redrawn")
            last_Perspective_Draw_Time = DateTime.Now()

            Try

            Dim active_Bitmap As Bitmap = Nothing
            Dim active_PictureBox_Index As Int16 = 0
            Dim is_perspective_drown As Boolean = False

            If is_PictureBox1_Visible Then
                active_Bitmap = CType(Picture_Box_1.Image, Bitmap)
                active_PictureBox_Index = 1
            ElseIf is_PictureBox2_Visible Then
                active_Bitmap = CType(Picture_Box_2.Image, Bitmap)
                active_PictureBox_Index = 2
            End If

            Dim w = Picture_Box_1.Width - 1
            Dim h = Picture_Box_1.Height - 1

            If active_Bitmap IsNot Nothing AndAlso
                    active_Bitmap.Width > 1 AndAlso
                    active_Bitmap.Height > 1 AndAlso
            w > 1 AndAlso
                h > 1 Then

                Dim bH = active_Bitmap.Height - 1
                Dim bW = active_Bitmap.Width - 1

                'Try

                Dim bitmap_proportion = (bW + 1.0) / (bH + 1.0)
                Dim pictureBox_proportion = (w + 1.0) / (h + 1.0)

                If Not Math.Round(bitmap_proportion, 2) = Math.Round(pictureBox_proportion, 2) Then


                    Dim targetWidth As Integer = w + 1
                    Dim targetHeight As Integer = h + 1
                    Dim imageRect As Rectangle = GetZoomedImageRectangle(bW + 1, bH + 1, targetWidth, targetHeight)

                    If Not imageRect.IsEmpty Then
                        Dim Perspective_Bitmap As New Bitmap(targetWidth, targetHeight)
                        Using g_bg As Graphics = Graphics.FromImage(Perspective_Bitmap)
                            g_bg.Clear(Color.FromArgb(255, Me.BackColor.R, Me.BackColor.G, Me.BackColor.B))
                        End Using

                        Dim color_deviation_threshold As Double = (percent_of_color_deviation / 100) * 255 * 3
                        Dim edgeOverlap As Integer = 2

                        Using displayedBitmap As Bitmap = CreateDisplayedImageBitmap(active_Bitmap, imageRect.Size, Me.BackColor)
                            Using g As Graphics = Graphics.FromImage(Perspective_Bitmap)
                                g.CompositingMode = Drawing2D.CompositingMode.SourceCopy

                                If bitmap_proportion < pictureBox_proportion Then
                                    Dim leftColors As List(Of Color) = GetDisplayedVerticalEdgeColors(displayedBitmap, targetHeight, imageRect, True)
                                    Dim rightColors As List(Of Color) = GetDisplayedVerticalEdgeColors(displayedBitmap, targetHeight, imageRect, False)
                                    Dim leftRect As New Rectangle(0, 0, Math.Min(targetWidth, imageRect.Left + edgeOverlap), targetHeight)
                                    Dim rightStart As Integer = Math.Max(0, imageRect.Right - edgeOverlap)
                                    Dim rightRect As New Rectangle(rightStart, 0, targetWidth - rightStart, targetHeight)

                                    Dim didDrawLeft As Boolean = PaintVerticalPerspectiveBackground(g, leftColors, leftRect, color_deviation_threshold)
                                    Dim didDrawRight As Boolean = PaintVerticalPerspectiveBackground(g, rightColors, rightRect, color_deviation_threshold)

                                    Dim leftSeamWidth As Integer = Math.Min(edgeOverlap, imageRect.Left)
                                    If leftSeamWidth > 0 Then
                                        PaintVerticalSeamBand(g, leftColors, targetHeight, imageRect.Left - leftSeamWidth, leftSeamWidth)
                                    End If

                                    Dim rightSeamWidth As Integer = Math.Min(edgeOverlap, targetWidth - imageRect.Right)
                                    If rightSeamWidth > 0 Then
                                        PaintVerticalSeamBand(g, rightColors, targetHeight, imageRect.Right, rightSeamWidth)
                                    End If

                                    is_perspective_drown = didDrawLeft OrElse didDrawRight
                                Else
                                    Dim topColors As List(Of Color) = GetDisplayedHorizontalEdgeColors(displayedBitmap, targetWidth, imageRect, True)
                                    Dim bottomColors As List(Of Color) = GetDisplayedHorizontalEdgeColors(displayedBitmap, targetWidth, imageRect, False)
                                    Dim topRect As New Rectangle(0, 0, targetWidth, Math.Min(targetHeight, imageRect.Top + edgeOverlap))
                                    Dim bottomStart As Integer = Math.Max(0, imageRect.Bottom - edgeOverlap)
                                    Dim bottomRect As New Rectangle(0, bottomStart, targetWidth, targetHeight - bottomStart)

                                    Dim didDrawTop As Boolean = PaintHorizontalPerspectiveBackground(g, topColors, topRect, color_deviation_threshold)
                                    Dim didDrawBottom As Boolean = PaintHorizontalPerspectiveBackground(g, bottomColors, bottomRect, color_deviation_threshold)

                                    Dim topSeamHeight As Integer = Math.Min(edgeOverlap, imageRect.Top)
                                    If topSeamHeight > 0 Then
                                        PaintHorizontalSeamBand(g, topColors, targetWidth, imageRect.Top - topSeamHeight, topSeamHeight)
                                    End If

                                    Dim bottomSeamHeight As Integer = Math.Min(edgeOverlap, targetHeight - imageRect.Bottom)
                                    If bottomSeamHeight > 0 Then
                                        PaintHorizontalSeamBand(g, bottomColors, targetWidth, imageRect.Bottom, bottomSeamHeight)
                                    End If

                                    is_perspective_drown = didDrawTop OrElse didDrawBottom
                                End If
                            End Using
                        End Using

                        If is_perspective_drown Then
                            If active_PictureBox_Index = 1 Then
                                Picture_Box_1.BackgroundImage?.Dispose()
                                Picture_Box_1.BackgroundImage = Perspective_Bitmap
                            ElseIf active_PictureBox_Index = 2 Then
                                Picture_Box_2.BackgroundImage?.Dispose()
                                Picture_Box_2.BackgroundImage = Perspective_Bitmap
                            End If
                        Else
                            Perspective_Bitmap.Dispose()
                        End If
                    End If
                End If
                ' Catch ex As Exception
                '   MsgBox("E104 " & ex.Message)
                '     Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w3050: E104 " & ex.Message)
                ' End Try

                Try
                    If Not is_perspective_drown Then
                        If active_PictureBox_Index = 1 AndAlso
                                Picture_Box_1.BackgroundImage IsNot Nothing Then

                            Dim oldBg1 As Image = Picture_Box_1.BackgroundImage
                            Picture_Box_1.BackgroundImage = Nothing
                            oldBg1.Dispose()

                        ElseIf active_PictureBox_Index = 2 AndAlso
                                Picture_Box_2.BackgroundImage IsNot Nothing Then

                            Dim oldBg2 As Image = Picture_Box_2.BackgroundImage
                            Picture_Box_2.BackgroundImage = Nothing
                            oldBg2.Dispose()
                        End If
                    End If
                Catch ex As Exception
                    MsgBox("E105 " & ex.Message)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w3070: E105 " & ex.Message)
                End Try
            End If

            Catch ex As Exception
                ' Perspective bars are non-essential cosmetics; never let a GDI+
                ' failure here abort the (already-shown) image. It'll be retried on
                ' the next navigation/resize, or by the trailing-redraw timer.
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1131: perspective draw skipped: [" & ex.GetType().Name & "] " & ex.Message)
            End Try
        End If

        '  sw.Stop()
        ' Debug.WriteLine("Draw_Perspective elapsed: " & sw.ElapsedMilliseconds & " ms")

    End Sub

End Class
