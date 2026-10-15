Option Strict On

Imports System.Drawing

Partial Public Class Main_Form

    ' One-shot timer that retries a perspective redraw the throttle dropped, so the
    ' image we finally land on after fast scrolling still gets its bars. Without it a
    ' draw skipped inside the throttle window was never retried.
    Private WithEvents perspective_Trailing_Timer As New System.Windows.Forms.Timer() With {.Interval = how_long_wait_before_draw_perspective}

    ''' <summary>A draw was asked for by a NEW photo (rather than a resize/zoom), so the
    ''' dynamic-perspective halo should grow rather than just appear.
    '''
    ''' It is a field and not only the Draw_Perspective argument because the throttle can
    ''' drop that call and hand it to the trailing timer, which knows nothing about what
    ''' asked for it - without this the photo you scrolled to would land with its halo
    ''' already fully grown.</summary>
    Private perspective_Animate_Pending As Boolean = False

    Private Sub ArmTrailingPerspectiveRedraw()
        ' Restart on every skipped request so only the last one survives, firing once
        ' the throttle window has elapsed (i.e. shortly after scrolling stops).
        perspective_Trailing_Timer.Stop()
        perspective_Trailing_Timer.Start()
    End Sub

    ''' <summary>Drops the perspective frame of the box that is NOT
    ''' <paramref name="activeIndex"/>. Only the visible box may carry bars (audit B-11).</summary>
    Private Sub ClearHiddenBoxBackground(activeIndex As Integer)
        Dim hidden As PictureBox = Nothing
        If activeIndex = 1 Then
            hidden = Picture_Box_2
        ElseIf activeIndex = 2 Then
            hidden = Picture_Box_1
        End If
        If hidden Is Nothing OrElse hidden.BackgroundImage Is Nothing Then Return
        Try
            Dim old_Bg As Image = hidden.BackgroundImage
            hidden.BackgroundImage = Nothing
            old_Bg.Dispose()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1137: hidden box background dispose skipped: [" & ex.GetType().Name & "] " & ex.Message)
        End Try
    End Sub

    ''' <summary>The settings window switched perspective on or off: draw or clear now
    ''' rather than on the next photo (audit B-04).</summary>
    Friend Sub RefreshPerspective()
        If is_form_shown Then Draw_Perspective()
    End Sub

    Private Sub Perspective_Trailing_Timer_Tick(sender As Object, e As EventArgs) Handles perspective_Trailing_Timer.Tick
        perspective_Trailing_Timer.Stop()
        Draw_Perspective()
    End Sub

    ''' <summary>Takes the perspective bars off both picture boxes.
    '''
    ''' Draw_Perspective only ever drops a stale BackgroundImage from INSIDE its
    ''' "there is a bitmap to analyse" branch, so a surface blanked WITHOUT a new image
    ''' (an unsupported format, an emptied folder) kept the previous file's
    ''' bars painted - and since the bitmap is box-sized and its middle is cleared to
    ''' the old BackColor, what the user got was the previous photo's bars framing a
    ''' black rectangle, with no image behind any of it.</summary>
    Private Sub ClearPerspectiveBackground()
        ' A redraw still queued for the media we just dropped has nothing left to draw.
        perspective_Trailing_Timer.Stop()
        perspective_Animate_Pending = False
#If Not NETFRAMEWORK Then
        ' Same for a halo still growing around it - and it must let go of the surface
        ' before we dispose the bitmap it is painting into.
        StopPerspectiveHalo()
#End If

        Try
            If Picture_Box_1.BackgroundImage IsNot Nothing Then
                Dim old_Bg_1 As Image = Picture_Box_1.BackgroundImage
                Picture_Box_1.BackgroundImage = Nothing
                old_Bg_1.Dispose()
            End If

            If Picture_Box_2.BackgroundImage IsNot Nothing Then
                Dim old_Bg_2 As Image = Picture_Box_2.BackgroundImage
                Picture_Box_2.BackgroundImage = Nothing
                old_Bg_2.Dispose()
            End If
        Catch ex As Exception
            ' Cosmetics: never let this abort the caller's blanking of the surface.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1132: perspective clear skipped: [" & ex.GetType().Name & "] " & ex.Message)
        End Try
    End Sub

    ' The arithmetic of the bars - which axis, the fitted rectangle, the sampled edge, the spread and
    ' the uniform-or-perspective decision, the trimmed mean, the smoothing, the bands and the seam -
    ' lives once in LetterboxFillMath (LETTERBOX-BARS 0.1 rules 1-7, held to the catalog's vectors by
    ' LetterboxFillConformanceTests). This file keeps only the GDI+ glue that paints what it returns.

    Private Function GetEdgeSampleDepth(scale As Double, sourceSize As Integer) As Integer
        Dim safeScale As Double = Math.Max(scale, 0.0001)
        Dim scaledSampleDepth As Integer = CInt(Math.Ceiling(1.0 / safeScale))
        Dim maxDepth As Integer = Math.Min(sourceSize, 8)
        Return Math.Min(Math.Max(scaledSampleDepth, 2), maxDepth)
    End Function

    Private Function GetZoomedImageRectangle(sourceWidth As Integer, sourceHeight As Integer, targetWidth As Integer, targetHeight As Integer) As Rectangle
#If Not NETFRAMEWORK Then
        ' The PictureBox still uses SizeMode.Zoom at Fit. The viewport's FitBounds
        ' deliberately does not enlarge small photos, but PictureBox does; sampling
        ' its bars from FitBounds put their edges inside those photos.
        Return ZoomMath.PictureBoxZoomBounds(sourceWidth, sourceHeight, New Size(targetWidth, targetHeight))
#Else
        ' LETTERBOX-BARS rule 2. The modern branch above reaches the same arithmetic through
        ' ZoomMath.PictureBoxZoomBounds (the conformance test holds both to the vectors).
        Return LetterboxFillMath.FitRectangle(sourceWidth, sourceHeight, targetWidth, targetHeight)
#End If
    End Function

    Private Function CreateDisplayedImageBitmap(sourceImage As Image, displaySize As Size, backgroundColor As Color) As Bitmap
        Dim displayedBitmap As New Bitmap(displaySize.Width, displaySize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb)

        Try
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
        Catch
            ' The transient GDI+ failure Draw_Perspective's outer Catch swallows can land
            ' here, before the caller's Using owns the bitmap - free it, then let the
            ' exception go on exactly as before (audit 057.S04-3).
            displayedBitmap.Dispose()
            Throw
        End Try

        Return displayedBitmap
    End Function

    ' The sampler is made once per frame by the caller (018 U-15) and shared by both edges. It
    ' wraps the displayed bitmap without copying it (see EdgeSampler: a copy costs more than
    ' the reads); Nothing - an unusable bitmap - means no colors, which the painters below
    ' already treat as "nothing to draw".
    Private Function GetDisplayedVerticalEdgeColors(sampler As EdgeSampler, targetHeight As Integer, imageRect As Rectangle, isLeftEdge As Boolean) As List(Of Color)
        If sampler Is Nothing Then Return New List(Of Color)()
        Return sampler.GetDisplayedVerticalEdgeColors(targetHeight, imageRect, isLeftEdge)
    End Function

    Private Function GetDisplayedHorizontalEdgeColors(sampler As EdgeSampler, targetWidth As Integer, imageRect As Rectangle, isTopEdge As Boolean) As List(Of Color)
        If sampler Is Nothing Then Return New List(Of Color)()
        Return sampler.GetDisplayedHorizontalEdgeColors(targetWidth, imageRect, isTopEdge)
    End Function

    Private Function PaintVerticalPerspectiveBackground(g As Graphics, edgeColors As List(Of Color), targetRect As Rectangle) As Boolean
        If targetRect.Width <= 0 OrElse
            targetRect.Height <= 0 OrElse
            edgeColors.Count = 0 Then

            Return False
        End If

        ' LETTERBOX-BARS rule 4: sampled colours, spread, uniform or perspective.
        Dim sampledColors As List(Of Color) = LetterboxFillMath.SteppedColors(edgeColors)

        If LetterboxFillMath.ModeOf(sampledColors) = LetterboxFillMath.LetterboxBandMode.Uniform Then
            ' Rule 5: one colour, the trimmed mean of the same samples.
            Using brush As New SolidBrush(LetterboxFillMath.TrimmedMeanColor(sampledColors))
                g.FillRectangle(brush, targetRect)
            End Using
        Else
            ' Rule 6: the smoothed edge, row by row.
            Dim smoothedColors As List(Of Color) = LetterboxFillMath.SmoothColors(edgeColors, LetterboxFillMath.SmoothRadius(edgeColors.Count))

            For y As Integer = targetRect.Top To targetRect.Bottom - 1
                Dim colorIndex As Integer = Math.Max(0, Math.Min(y, smoothedColors.Count - 1))
                Using brush As New SolidBrush(smoothedColors(colorIndex))
                    g.FillRectangle(brush, targetRect.Left, y, targetRect.Width, 1)
                End Using
            Next
        End If

        Return True
    End Function

    Private Function PaintHorizontalPerspectiveBackground(g As Graphics, edgeColors As List(Of Color), targetRect As Rectangle) As Boolean
        If targetRect.Width <= 0 OrElse
            targetRect.Height <= 0 OrElse
            edgeColors.Count = 0 Then

            Return False
        End If

        ' LETTERBOX-BARS rules 4-6, the same three steps as the vertical painter (transposed).
        Dim sampledColors As List(Of Color) = LetterboxFillMath.SteppedColors(edgeColors)

        If LetterboxFillMath.ModeOf(sampledColors) = LetterboxFillMath.LetterboxBandMode.Uniform Then
            Using brush As New SolidBrush(LetterboxFillMath.TrimmedMeanColor(sampledColors))
                g.FillRectangle(brush, targetRect)
            End Using
        Else
            Dim smoothedColors As List(Of Color) = LetterboxFillMath.SmoothColors(edgeColors, LetterboxFillMath.SmoothRadius(edgeColors.Count))

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

    ''' <param name="animate">True when a NEW photo asked for this draw. Only matters to
    ''' dynamic perspective (modern build), where it decides whether the halo grows out
    ''' of the photo or is simply already there - a resize or zoom redraw must not
    ''' re-run the animation, or dragging the window frame would pulse.</param>
    Private Sub Draw_Perspective(Optional animate As Boolean = False)

        '  Dim sw As New Stopwatch()
        '   sw.Start()

        If animate Then perspective_Animate_Pending = True

        Dim perspective_Applies As Boolean =
            Is_Pespective AndAlso
            (is_PictureBox1_Visible OrElse
            is_PictureBox2_Visible) AndAlso
            LetterboxFillMath.SlideshowAllowsBars(Is_slide_show_mode, SlideShowTimer.Interval)

#If Not NETFRAMEWORK Then
        ' A zoomed picture has no bars: the viewport covers the whole panel, so the
        ' "bars" would be 0-1 px wide, built from a panel-sized bitmap that can reach
        ' hundreds of MB at high zoom (audit B-12).
        If Not MediaIsAtFit() Then perspective_Applies = False
#End If

        Dim throttle_Elapsed As Boolean =
            MonotonicClock.HasElapsed(MonotonicClock.NowMs(), last_Perspective_Draw_Ms, how_long_wait_before_draw_perspective)

        ' Nothing is going to be drawn, so nothing may STAY drawn either: the only other
        ' place bars are removed is inside the drawing branch below, so switching
        ' perspective off, a fast slideshow or a zoom left the previous photo's bars
        ' framing every later one (audit B-04). ClearPerspectiveBackground also drops
        ' the animation intent - otherwise it would sit here and make some later resize
        ' animate a photo that has been on screen for minutes.
        If Not perspective_Applies Then
            ClearPerspectiveBackground()
            Return
        End If

        ' A redraw is wanted but came too soon after the last one: defer it instead
        ' of dropping it. (Fast scrolling otherwise lost the draw for the image we
        ' settled on, so it showed no perspective bars until the next resize/reload.)
        If perspective_Applies AndAlso Not throttle_Elapsed Then
            ArmTrailingPerspectiveRedraw()
        End If

        If perspective_Applies AndAlso throttle_Elapsed Then

            perspective_Trailing_Timer.Stop()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1129: corners redrawn")
            last_Perspective_Draw_Ms = MonotonicClock.NowMs()

            Dim animate_This_Draw As Boolean = perspective_Animate_Pending
            perspective_Animate_Pending = False

            Try

            Dim active_Bitmap As Bitmap = Nothing
            Dim active_PictureBox_Index As Int16 = 0
            Dim is_perspective_drown As Boolean = False

            ' TryCast, not CType: a WMF/EMF decodes to a Metafile, and the cast threw into
            ' the outer Catch with the previous photo's bars still up (audit B-04).
            If is_PictureBox1_Visible Then
                active_Bitmap = TryCast(Picture_Box_1.Image, Bitmap)
                active_PictureBox_Index = 1
            ElseIf is_PictureBox2_Visible Then
                active_Bitmap = TryCast(Picture_Box_2.Image, Bitmap)
                active_PictureBox_Index = 2
            End If

            Dim w = Picture_Box_1.Width - 1
            Dim h = Picture_Box_1.Height - 1

            Dim has_Drawable_Bitmap As Boolean =
                active_Bitmap IsNot Nothing AndAlso
                active_Bitmap.Width > 1 AndAlso
                active_Bitmap.Height > 1 AndAlso
                w > 1 AndAlso
                h > 1

            ' Nothing to analyse means nothing to show: take the old bars down.
            If Not has_Drawable_Bitmap Then ClearPerspectiveBackground()

            If has_Drawable_Bitmap Then

                Dim bH = active_Bitmap.Height - 1
                Dim bW = active_Bitmap.Width - 1

                'Try

                ' LETTERBOX-BARS rule 1: bars exist only when the aspect ratios differ at two
                ' decimals (half to even); a pillarbox when the image is the narrower one.
                Dim bars_Axis As LetterboxFillMath.LetterboxAxis = LetterboxFillMath.AxisOf(bW + 1, bH + 1, w + 1, h + 1)

                If bars_Axis <> LetterboxFillMath.LetterboxAxis.None Then


                    Dim targetWidth As Integer = w + 1
                    Dim targetHeight As Integer = h + 1
                    Dim imageRect As Rectangle = GetZoomedImageRectangle(bW + 1, bH + 1, targetWidth, targetHeight)

                    If Not imageRect.IsEmpty Then
                        Dim Perspective_Bitmap As New Bitmap(targetWidth, targetHeight)
                        ' The bitmap is box-sized (megabytes at a maximised window) and the
                        ' pixel work below is exactly what the outer Catch exists to swallow -
                        ' GDI+ throws here transiently while a background decode runs. Without
                        ' this Finally the bitmap leaked once per such failure, on a path that
                        ' runs on every photo.
                        Dim bitmap_Ownership_Passed As Boolean = False
                        Try
                            Using g_bg As Graphics = Graphics.FromImage(Perspective_Bitmap)
                                g_bg.Clear(Color.FromArgb(255, Me.BackColor.R, Me.BackColor.G, Me.BackColor.B))
                            End Using

                            Using displayedBitmap As Bitmap = CreateDisplayedImageBitmap(active_Bitmap, imageRect.Size, Me.BackColor),
                                  edgeSampler As EdgeSampler = EdgeSampler.TryCreate(displayedBitmap)
                                Using g As Graphics = Graphics.FromImage(Perspective_Bitmap)
                                    g.CompositingMode = Drawing2D.CompositingMode.SourceCopy

                                    If bars_Axis = LetterboxFillMath.LetterboxAxis.Pillarbox Then
                                        ' LETTERBOX-BARS rules 5 and 7: the two bars (each reaches 2 px under the
                                        ' photo) and the seam bands outside it, in x.
                                        Dim bands As LetterboxFillMath.LetterboxBands =
                                            LetterboxFillMath.BarBands(targetWidth, imageRect.Left, imageRect.Width)

                                        Dim leftColors As List(Of Color) = GetDisplayedVerticalEdgeColors(edgeSampler, targetHeight, imageRect, True)
                                        Dim rightColors As List(Of Color) = GetDisplayedVerticalEdgeColors(edgeSampler, targetHeight, imageRect, False)
                                        Dim leftRect As New Rectangle(bands.NearBarStart, 0, bands.NearBarLength, targetHeight)
                                        Dim rightRect As New Rectangle(bands.FarBarStart, 0, bands.FarBarLength, targetHeight)

                                        Dim didDrawLeft As Boolean = PaintVerticalPerspectiveBackground(g, leftColors, leftRect)
                                        Dim didDrawRight As Boolean = PaintVerticalPerspectiveBackground(g, rightColors, rightRect)

                                        If bands.NearSeamLength > 0 Then
                                            PaintVerticalSeamBand(g, leftColors, targetHeight, bands.NearSeamStart, bands.NearSeamLength)
                                        End If

                                        If bands.FarSeamLength > 0 Then
                                            PaintVerticalSeamBand(g, rightColors, targetHeight, bands.FarSeamStart, bands.FarSeamLength)
                                        End If

                                        is_perspective_drown = didDrawLeft OrElse didDrawRight
                                    Else
                                        ' The same, transposed: the bands in y.
                                        Dim bands As LetterboxFillMath.LetterboxBands =
                                            LetterboxFillMath.BarBands(targetHeight, imageRect.Top, imageRect.Height)

                                        Dim topColors As List(Of Color) = GetDisplayedHorizontalEdgeColors(edgeSampler, targetWidth, imageRect, True)
                                        Dim bottomColors As List(Of Color) = GetDisplayedHorizontalEdgeColors(edgeSampler, targetWidth, imageRect, False)
                                        Dim topRect As New Rectangle(0, bands.NearBarStart, targetWidth, bands.NearBarLength)
                                        Dim bottomRect As New Rectangle(0, bands.FarBarStart, targetWidth, bands.FarBarLength)

                                        Dim didDrawTop As Boolean = PaintHorizontalPerspectiveBackground(g, topColors, topRect)
                                        Dim didDrawBottom As Boolean = PaintHorizontalPerspectiveBackground(g, bottomColors, bottomRect)

                                        If bands.NearSeamLength > 0 Then
                                            PaintHorizontalSeamBand(g, topColors, targetWidth, bands.NearSeamStart, bands.NearSeamLength)
                                        End If

                                        If bands.FarSeamLength > 0 Then
                                            PaintHorizontalSeamBand(g, bottomColors, targetWidth, bands.FarSeamStart, bands.FarSeamLength)
                                        End If

                                        is_perspective_drown = didDrawTop OrElse didDrawBottom
                                    End If
                                End Using
                            End Using

                            If is_perspective_drown Then
                                ' Dynamic perspective (modern build): the halo layer fades these
                                ' bars into the background colour and, on a new photo, grows that
                                ' fade out from the photo edge. It takes ownership of the bitmap
                                ' when it takes over; when it declines, the bars go up flat as
                                ' they always have.
                                Dim halo_Took_Over As Boolean = False
#If Not NETFRAMEWORK Then
                                halo_Took_Over = TryStartPerspectiveHalo(Perspective_Bitmap,
                                                                        imageRect,
                                                                        active_PictureBox_Index,
                                                                        bars_Axis = LetterboxFillMath.LetterboxAxis.Pillarbox,
                                                                        animate_This_Draw)
#End If
                                If Not halo_Took_Over Then
                                    If active_PictureBox_Index = 1 Then
                                        Picture_Box_1.BackgroundImage?.Dispose()
                                        Picture_Box_1.BackgroundImage = Perspective_Bitmap
                                        bitmap_Ownership_Passed = True
                                    ElseIf active_PictureBox_Index = 2 Then
                                        Picture_Box_2.BackgroundImage?.Dispose()
                                        Picture_Box_2.BackgroundImage = Perspective_Bitmap
                                        bitmap_Ownership_Passed = True
                                    End If
                                Else
                                    bitmap_Ownership_Passed = True
                                End If
                            Else
                                Perspective_Bitmap.Dispose()
                                bitmap_Ownership_Passed = True
                            End If
                        Finally
                            ' Nobody took it: either GDI+ threw on the way here, or the
                            ' active box index matched neither surface.
                            If Not bitmap_Ownership_Passed Then Perspective_Bitmap.Dispose()
                        End Try
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
                    ReportOperationError("E105", ex)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w3070: E105 " & ex.Message)
                End Try

                ' The two boxes alternate. Whatever the hidden one still carries is the
                ' frame of the photo BEFORE the previous one: the halo dissolved out of it
                ' and a throttled flip showed it (audit B-11). The halo has already taken
                ' its underlay by now, so the hidden box can let go.
                ClearHiddenBoxBackground(active_PictureBox_Index)
            End If

            Catch ex As Exception
                ' Perspective bars are non-essential cosmetics; never let a GDI+
                ' failure here abort the (already-shown) image. It'll be retried on
                ' the next navigation/resize, or by the trailing-redraw timer.
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1131: perspective draw skipped: [" & ex.GetType().Name & "] " & ex.Message)
                ' One line, no stack: Release strips Debug, and a failure that repeats on
                ' every photo (no bars at all) must leave a trace in current.log (057.S04-4).
                AppFileLogger.WriteLine("Perspective draw skipped: [" & ex.GetType().Name & "] " & ex.Message)
            End Try
        End If

        '  sw.Stop()
        ' Debug.WriteLine("Draw_Perspective elapsed: " & sw.ElapsedMilliseconds & " ms")

    End Sub

End Class
