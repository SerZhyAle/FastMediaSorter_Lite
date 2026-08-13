#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' "Dynamic perspective" - the .NET 10 viewer only. The x86 fallback keeps the flat
' bars (its Settings checkbox is hidden there), so this whole file compiles to nothing.
'
' WHAT IT CHANGES. Static perspective paints the pillar/letterbox bars at full edge
' colour all the way out to the screen edge. Dynamic perspective takes those same bars
' and fades them towards the form's background colour the further they get from the
' photo - a halo rather than a bar. That fade is a PERMANENT property of the picture,
' not just an intro: it is what the frame looks like at rest.
'
' The growth is a SEPARATE sub-option (Is_Animated_Perspective). With it off the halo is
' simply already there when the photo arrives; with it on it grows out of the photo edge.
' Either way the resting frame is the same pixels - that is the whole point of the split.
'
' HOW THE GROWTH WORKS, and why it is one code path with the resting state. At progress
' p the fade ramp spans only the first `p * barWidth` pixels out from the photo, and
' everything past that front is still plain background. So the halo starts hugging the
' photo, expands outward, and at p = 1 the ramp covers the whole bar - which IS the
' resting halo. Nothing special happens at the end of the animation, it just stops
' moving. Because the ramp always ends AT the background colour, the halo's growing
' front never shows a hard step against the background at any p. Drawing the halo
' instantly is just this same path entered at p = 1.
'
' WHY WE RECOMPOSE FROM THE BARS EVERY FRAME. Growing the ramp makes a given pixel get
' *less* background as time passes, so frames cannot be accumulated on top of each
' other - each one is bars + a fresh ramp. The bars bitmap Draw_Perspective handed us
' is kept as the clean source until the halo comes to rest, then released.
'
' The ramp is one LinearGradientBrush per bar (a per-column SolidBrush loop, which is
' what the static painter does once per image, would be ~30x that work per second).
Partial Public Class Main_Form

    ''' <summary>Time the halo takes to grow from the photo edge out to the screen edge
    ''' at the SLOW setting - the timing this effect shipped with. Medium (the default)
    ''' halves it and Fast quarters it; see HaloDurationMs.</summary>
    Private Const halo_Duration_Slow_Ms As Double = 350

    Private Const halo_Frame_Interval_Ms As Integer = 15

    ''' <summary>Shape of the fade across the halo: alpha(background) = distance ^ gamma,
    ''' distance being 0 at the photo edge and 1 at the screen edge. Above 1 the edge
    ''' colour holds near the photo and drops off further out (a glow); 1.0 would be a
    ''' flat linear wash across the whole bar.</summary>
    Private Const halo_Falloff_Gamma As Double = 1.4

    ''' <summary>Stops used to approximate the gamma curve in the gradient brush.</summary>
    Private Const halo_Blend_Stops As Integer = 12

    ''' <summary>The full-strength bars from Draw_Perspective - the clean source every
    ''' frame is recomposed from. We own it; Nothing once the halo is at rest.</summary>
    Private halo_Bars As Bitmap

    ''' <summary>The bitmap sitting on the picture box as BackgroundImage while the halo
    ''' animates. The picture box owns it (the existing BackgroundImage?.Dispose() paths
    ''' free it), we only draw into it - hence the identity check in the tick.</summary>
    Private halo_Frame As Bitmap

    ''' <summary>Where the photo itself sits inside the box, i.e. where the halo starts.</summary>
    Private halo_Image_Rect As Rectangle

    ''' <summary>1 or 2 - which picture box the running halo belongs to.</summary>
    Private halo_Box_Index As Integer

    ''' <summary>Duration of the RUNNING growth, snapshotted when it started. Read from
    ''' the setting once per animation rather than once per frame, so switching the speed
    ''' mid-flight cannot make a halo jump: the change lands on the next photo.</summary>
    Private halo_Running_Duration_Ms As Double = halo_Duration_Slow_Ms / 2.0

    ''' <summary>True = pillarbox (left/right bars), False = letterbox (top/bottom).
    ''' Mirrors the branch Draw_Perspective actually painted, so we never fade an axis
    ''' it left blank (which at the corners would double-fade towards the background).</summary>
    Private halo_Vertical_Bars As Boolean

    Private ReadOnly halo_Clock As New Stopwatch()

    Private WithEvents halo_Timer As New System.Windows.Forms.Timer() With {.Interval = halo_Frame_Interval_Ms}

    ''' <summary>Picture box the running halo is painting into, or Nothing.</summary>
    Private Function HaloTargetBox() As PictureBox
        If halo_Box_Index = 1 Then Return Picture_Box_1
        If halo_Box_Index = 2 Then Return Picture_Box_2
        Return Nothing
    End Function

    ''' <summary>How long the growth should take at the user's chosen speed: the shipped
    ''' 350 ms as Slow, half of it as Medium (the default) and a quarter as Fast. Only the
    ''' duration changes - the 15 ms frame cap stays, so Fast simply draws fewer frames
    ''' (about six plus the starting one) instead of drawing them harder.</summary>
    Private Shared Function HaloDurationMs() As Double
        Select Case Halo_Animation_Speed
            Case HaloAnimationSpeed.Slow : Return halo_Duration_Slow_Ms
            Case HaloAnimationSpeed.Fast : Return halo_Duration_Slow_Ms / 4.0
            Case Else : Return halo_Duration_Slow_Ms / 2.0
        End Select
    End Function

    ''' <summary>Ease-out: the halo springs out and settles, instead of crawling at a
    ''' constant rate and stopping dead at the screen edge.</summary>
    Private Shared Function HaloEase(progress As Double) As Double
        Dim p As Double = Math.Max(0.0, Math.Min(1.0, progress))
        Return 1.0 - (1.0 - p) * (1.0 - p)
    End Function

    ''' <summary>Takes over the finished perspective bars and drives the surface from
    ''' here on. Returns False when dynamic perspective is off (or setup failed), in
    ''' which case the caller still owns <paramref name="bars"/> and paints it flat.</summary>
    ''' <param name="animate">A NEW photo asked for this draw. Whether that actually grows
    ''' the halo is Is_Animated_Perspective's call, not the caller's.</param>
    Private Function TryStartPerspectiveHalo(bars As Bitmap,
                                             imageRect As Rectangle,
                                             boxIndex As Integer,
                                             verticalBars As Boolean,
                                             animate As Boolean) As Boolean

        If Not Is_Dynamic_Perspective Then Return False
        If bars Is Nothing Then Return False
        If boxIndex <> 1 AndAlso boxIndex <> 2 Then Return False

        ' A new photo only grows its halo if the user asked for the animation; otherwise
        ' the same halo is simply drawn at rest. (A resize/zoom redraw never grows.)
        Dim grow As Boolean = animate AndAlso Is_Animated_Perspective

        ' Only ever touches state we own, so `bars` is still safe to hand back.
        StopPerspectiveHalo()

        Dim frame As Bitmap = Nothing
        Try
            frame = New Bitmap(bars.Width, bars.Height)

            halo_Bars = bars
            halo_Frame = frame
            halo_Image_Rect = imageRect
            halo_Box_Index = boxIndex
            halo_Vertical_Bars = verticalBars

            RenderHaloFrame(If(grow, 0.0, 1.0))
        Catch ex As Exception
            ' Give the bitmap back untouched rather than leaving the caller with a
            ' disposed one - it will paint the flat bars, which is the old behaviour.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1133: halo start skipped: [" & ex.GetType().Name & "] " & ex.Message)
            halo_Bars = Nothing
            halo_Frame = Nothing
            frame?.Dispose()
            Return False
        End Try

        Dim target As PictureBox = HaloTargetBox()
        target.BackgroundImage?.Dispose()
        target.BackgroundImage = frame

        If grow Then
            halo_Running_Duration_Ms = HaloDurationMs()
            halo_Clock.Restart()
            halo_Timer.Start()
        Else
            ' Already at rest (animation off, or a resize/zoom redraw): there is no next
            ' frame, so nothing left to recompose from.
            ReleaseHaloSource()
            halo_Frame = Nothing
        End If

        Return True
    End Function

    ''' <summary>Stops a running halo and drops the source bars. The frame bitmap is
    ''' left alone - the picture box owns it and the normal BackgroundImage disposal
    ''' paths free it.</summary>
    Private Sub StopPerspectiveHalo()
        halo_Timer.Stop()
        halo_Clock.Reset()
        ReleaseHaloSource()
        halo_Frame = Nothing
    End Sub

    Private Sub ReleaseHaloSource()
        If halo_Bars Is Nothing Then Return
        Dim bars As Bitmap = halo_Bars
        halo_Bars = Nothing
        Try
            bars.Dispose()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1134: halo source dispose skipped: [" & ex.GetType().Name & "] " & ex.Message)
        End Try
    End Sub

    Private Sub Halo_Timer_Tick(sender As Object, e As EventArgs) Handles halo_Timer.Tick
        Dim target As PictureBox = HaloTargetBox()

        ' The frame we animate is only on screen for as long as nobody replaced it.
        ' A new photo, a blanked surface or a plain redraw disposes it out from under
        ' us, so bail on identity rather than draw into a dead bitmap.
        If halo_Frame Is Nothing OrElse
            halo_Bars Is Nothing OrElse
            target Is Nothing OrElse
            Not ReferenceEquals(target.BackgroundImage, halo_Frame) Then

            StopPerspectiveHalo()
            Return
        End If

        ' Against the clock, not against a tick count: a frame the system timer skipped is
        ' caught up by the elapsed time, and the last frame is always exactly 1.0.
        Dim duration As Double = If(halo_Running_Duration_Ms > 0.0, halo_Running_Duration_Ms, halo_Duration_Slow_Ms)
        Dim progress As Double = halo_Clock.Elapsed.TotalMilliseconds / duration
        Dim finished As Boolean = progress >= 1.0
        If finished Then progress = 1.0

        Try
            RenderHaloFrame(progress)
        Catch ex As Exception
            ' Cosmetics - never let a GDI+ transient take the (already shown) photo
            ' with it. The next navigation or resize repaints the background.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1135: halo frame skipped: [" & ex.GetType().Name & "] " & ex.Message)
            finished = True
        End Try

        target.Invalidate()

        If finished Then
            halo_Timer.Stop()
            halo_Clock.Reset()
            ReleaseHaloSource()
            halo_Frame = Nothing
        End If
    End Sub

    ''' <summary>Repaints the frame for one point in the growth: the clean bars, then a
    ''' background ramp over each bar reaching `eased * barWidth` out from the photo.</summary>
    Private Sub RenderHaloFrame(progress As Double)
        Dim eased As Double = HaloEase(progress)
        Dim background As Color = Color.FromArgb(255, Me.BackColor.R, Me.BackColor.G, Me.BackColor.B)
        Dim width As Integer = halo_Frame.Width
        Dim height As Integer = halo_Frame.Height

        Using g As Graphics = Graphics.FromImage(halo_Frame)
            g.PixelOffsetMode = PixelOffsetMode.Half
            g.SmoothingMode = SmoothingMode.None

            ' Back to the untouched bars - a grown ramp gives a pixel LESS background
            ' than the previous frame did, so frames cannot stack.
            g.CompositingMode = CompositingMode.SourceCopy
            g.DrawImageUnscaled(halo_Bars, 0, 0)
            g.CompositingMode = CompositingMode.SourceOver

            If halo_Vertical_Bars Then
                PaintHaloFade(g, background, New Rectangle(0, 0, halo_Image_Rect.Left, height), True, True, eased)
                PaintHaloFade(g, background, New Rectangle(halo_Image_Rect.Right, 0, width - halo_Image_Rect.Right, height), True, False, eased)
            Else
                PaintHaloFade(g, background, New Rectangle(0, 0, width, halo_Image_Rect.Top), False, True, eased)
                PaintHaloFade(g, background, New Rectangle(0, halo_Image_Rect.Bottom, width, height - halo_Image_Rect.Bottom), False, False, eased)
            End If
        End Using
    End Sub

    ''' <summary>Lays the background over one bar: a solid block past the halo's front,
    ''' then a ramp from the front (full background) back to the photo edge (untouched
    ''' bar colour).</summary>
    ''' <param name="horizontal">True for a left/right bar (the fade runs along x).</param>
    ''' <param name="photoEdgeAtFarEnd">True when the photo touches the bar's right/bottom
    ''' side (a left or top bar), False when it touches the left/top side.</param>
    Private Sub PaintHaloFade(g As Graphics,
                              background As Color,
                              barRect As Rectangle,
                              horizontal As Boolean,
                              photoEdgeAtFarEnd As Boolean,
                              eased As Double)

        If barRect.Width <= 0 OrElse barRect.Height <= 0 Then Return

        Dim maxDistance As Integer = If(horizontal, barRect.Width, barRect.Height)
        Dim reach As Integer = CInt(Math.Floor(maxDistance * Math.Max(0.0, Math.Min(1.0, eased))))

        ' Past the front the halo simply has not arrived yet - that untouched
        ' background is what makes it read as growing out of the photo.
        If reach < maxDistance Then
            Dim gapLength As Integer = maxDistance - reach
            Dim gap As Rectangle
            If horizontal Then
                gap = If(photoEdgeAtFarEnd,
                         New Rectangle(barRect.Left, barRect.Top, gapLength, barRect.Height),
                         New Rectangle(barRect.Right - gapLength, barRect.Top, gapLength, barRect.Height))
            Else
                gap = If(photoEdgeAtFarEnd,
                         New Rectangle(barRect.Left, barRect.Top, barRect.Width, gapLength),
                         New Rectangle(barRect.Left, barRect.Bottom - gapLength, barRect.Width, gapLength))
            End If

            Using solid As New SolidBrush(background)
                g.FillRectangle(solid, gap)
            End Using
        End If

        If reach <= 0 Then Return

        Dim ramp As Rectangle
        If horizontal Then
            ramp = If(photoEdgeAtFarEnd,
                      New Rectangle(barRect.Right - reach, barRect.Top, reach, barRect.Height),
                      New Rectangle(barRect.Left, barRect.Top, reach, barRect.Height))
        Else
            ramp = If(photoEdgeAtFarEnd,
                      New Rectangle(barRect.Left, barRect.Bottom - reach, barRect.Width, reach),
                      New Rectangle(barRect.Left, barRect.Top, barRect.Width, reach))
        End If

        Using brush As New LinearGradientBrush(ramp,
                                               background,
                                               background,
                                               If(horizontal, LinearGradientMode.Horizontal, LinearGradientMode.Vertical))
            ' Default WrapMode Tile makes GDI+ sample the wrapped-around far end for the
            ' first row/column of the filled rectangle - a 1px stripe of solid background
            ' right against the photo, i.e. exactly the seam the perspective bars exist
            ' to remove. Mirroring the tile hides it. Set before InterpolationColors.
            brush.WrapMode = WrapMode.TileFlipXY
            brush.InterpolationColors = BuildHaloBlend(background, photoEdgeAtFarEnd)
            g.FillRectangle(brush, ramp)
        End Using
    End Sub

    ''' <summary>The gamma falloff as gradient stops: transparent at the photo edge (the
    ''' bar colour shows through untouched) to opaque background at the halo's front.</summary>
    Private Shared Function BuildHaloBlend(background As Color, photoEdgeAtFarEnd As Boolean) As ColorBlend
        Dim positions(halo_Blend_Stops - 1) As Single
        Dim colors(halo_Blend_Stops - 1) As Color

        For i As Integer = 0 To halo_Blend_Stops - 1
            ' Position runs along the gradient; distance runs out from the photo.
            Dim position As Double = i / (halo_Blend_Stops - 1.0)
            Dim distance As Double = If(photoEdgeAtFarEnd, 1.0 - position, position)
            Dim alpha As Integer = CInt(Math.Round(255.0 * Math.Pow(distance, halo_Falloff_Gamma)))

            positions(i) = CSng(position)
            colors(i) = Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), background)
        Next

        Dim blend As New ColorBlend(halo_Blend_Stops)
        blend.Positions = positions
        blend.Colors = colors
        Return blend
    End Function

End Class
#End If
