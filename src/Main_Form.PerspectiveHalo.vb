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

    ' THE NUMBERS ARE NOT HERE. The growth time (350 ms at Slow, halved for Medium, quartered for
    ' Fast), the 15 ms frame, the fade's shape - alpha(background) = distance ^ 1.4, distance 0 at
    ' the photo edge and 1 at the screen edge, so the edge colour holds near the photo and drops
    ' off further out - and its 12 gradient stops, the ease-out, the reach and the underlay's
    ' opacity all live once in LetterboxFillMath (LETTERBOX-HALO 0.1 rules 2-7), held to the
    ' catalog's vectors by LetterboxFillConformanceTests. This file keeps the GDI+ painting and the
    ' timer.

    ''' <summary>The full-strength bars from Draw_Perspective - the clean source every
    ''' frame is recomposed from. We own it; Nothing once the halo is at rest.</summary>
    Private halo_Bars As Bitmap

    ''' <summary>The bitmap sitting on the picture box as BackgroundImage while the halo
    ''' animates. The picture box owns it (the existing BackgroundImage?.Dispose() paths
    ''' free it), we only draw into it - hence the identity check in the tick.</summary>
    Private halo_Frame As Bitmap

    ''' <summary>
    ''' The frame that was on screen when this growth started - the previous photo's one,
    ''' taken over from the picture box instead of being disposed. It is laid over each
    ''' frame of the growth at alpha 1-eased, so the picture starts from what was already
    ''' there and dissolves into the new frame instead of the bar snapping to flat
    ''' background and growing back. We own it; Nothing when there was none.
    '''
    ''' IT FADES WITH TIME, NEVER WITH DISTANCE. At the end of the growth its alpha is 0,
    ''' so the resting frame is composed of exactly what it always was - the resting halo
    ''' is the reference the whole file is built around, and an underlay that survived into
    ''' it would change the picture the moment the underlay was released.
    ''' </summary>
    Private halo_Underlay As Bitmap

    ''' <summary>Where the photo itself sits inside the box, i.e. where the halo starts.</summary>
    Private halo_Image_Rect As Rectangle

    ''' <summary>1 or 2 - which picture box the running halo belongs to.</summary>
    Private halo_Box_Index As Integer

    ''' <summary>Duration of the RUNNING growth, snapshotted when it started. Read from
    ''' the setting once per animation rather than once per frame, so switching the speed
    ''' mid-flight cannot make a halo jump: the change lands on the next photo.</summary>
    Private halo_Running_Duration_Ms As Double = LetterboxFillMath.HaloDurationMs("medium")

    ''' <summary>True = pillarbox (left/right bars), False = letterbox (top/bottom).
    ''' Mirrors the branch Draw_Perspective actually painted, so we never fade an axis
    ''' it left blank (which at the corners would double-fade towards the background).</summary>
    Private halo_Vertical_Bars As Boolean

    Private ReadOnly halo_Clock As New Stopwatch()

    Private WithEvents halo_Timer As New System.Windows.Forms.Timer() With {.Interval = LetterboxFillMath.FrameIntervalMs}

    ''' <summary>Picture box the running halo is painting into, or Nothing.</summary>
    Private Function HaloTargetBox() As PictureBox
        If halo_Box_Index = 1 Then Return Picture_Box_1
        If halo_Box_Index = 2 Then Return Picture_Box_2
        Return Nothing
    End Function

    ''' <summary>How long the growth should take at the user's chosen speed (LETTERBOX-HALO rule 5,
    ''' LetterboxFillMath.HaloDurationMs): the shipped 350 ms as Slow, half of it as Medium (the
    ''' default) and a quarter as Fast. Only the duration changes - the 15 ms frame cap stays, so
    ''' Fast simply draws fewer frames (about six plus the starting one) instead of drawing them
    ''' harder.</summary>
    Private Shared Function HaloDurationMs() As Double
        Return LetterboxFillMath.HaloDurationMs(HaloSpeedToSetting(Halo_Animation_Speed))
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
        ' "Reduce motion" (§4.3 of SPECIFICATION_SETTINGS_EXPANSION) lands here rather
        ' than on the setting itself: the resting halo IS the frame, so switching the
        ' growth off must not change what the picture ends up looking like.
        Dim grow As Boolean = animate AndAlso Is_Animated_Perspective AndAlso Not ReduceMotionActive()

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

            ' Before the first frame is composed - it is what that frame starts from.
            If grow Then TakeOverPreviousFrameAsUnderlay(frame.Size)

            RenderHaloFrame(If(grow, 0.0, 1.0))
        Catch ex As Exception
            ' Give the bitmap back untouched rather than leaving the caller with a
            ' disposed one - it will paint the flat bars, which is the old behaviour.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1133: halo start skipped: [" & ex.GetType().Name & "] " & ex.Message)
            AppFileLogger.WriteLine("Perspective halo start skipped: [" & ex.GetType().Name & "] " & ex.Message)
            halo_Bars = Nothing
            halo_Frame = Nothing
            ReleaseHaloUnderlay()
            frame?.Dispose()
            Return False
        End Try

        Dim target As PictureBox = HaloTargetBox()
        ' Nothing when the frame that was here became the underlay - it is ours now.
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
        ReleaseHaloUnderlay()
        halo_Frame = Nothing
    End Sub

    ''' <summary>
    ''' Takes the frame currently on the picture box away from it, to be dissolved out of
    ''' during the growth. Ownership MOVES: the box's reference is cleared, so the caller's
    ''' usual BackgroundImage?.Dispose() finds nothing and this bitmap lives until the
    ''' growth ends.
    '''
    ''' A frame of a different size is left where it is. It belongs to a window of another
    ''' size, and stretching it would dissolve out of a picture that was never on screen.
    ''' </summary>
    Private Sub TakeOverPreviousFrameAsUnderlay(frameSize As Size)
        ReleaseHaloUnderlay()

        Dim target As PictureBox = HaloTargetBox()
        If target Is Nothing Then Return

        ' The frame that was ON SCREEN. With the background preload the new photo lands
        ' in the other box, so what was visible is the box that is now hidden; reading
        ' the target dissolved out of the photo before the previous one (audit B-11).
        ' Draw_Perspective clears the hidden box after every draw, so at most one of the
        ' two carries a frame, and when the same box is reused it is the target.
        Dim other As PictureBox = If(ReferenceEquals(target, Picture_Box_1), Picture_Box_2, Picture_Box_1)
        For Each source As PictureBox In New PictureBox() {other, target}
            Dim previous As Bitmap = TryCast(source.BackgroundImage, Bitmap)
            If previous Is Nothing OrElse previous.Size <> frameSize Then Continue For

            source.BackgroundImage = Nothing
            halo_Underlay = previous
            Return
        Next
    End Sub

    ''' <summary>The previous frame is ours from the moment it is taken over, and it is a
    ''' box-sized bitmap - it must not outlive the growth that dissolves it.</summary>
    Private Sub ReleaseHaloUnderlay()
        If halo_Underlay Is Nothing Then Return
        Dim underlay As Bitmap = halo_Underlay
        halo_Underlay = Nothing
        Try
            underlay.Dispose()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1136: halo underlay dispose skipped: [" & ex.GetType().Name & "] " & ex.Message)
        End Try
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
        ' caught up by the elapsed time, and the last frame is always exactly 1.0
        ' (LETTERBOX-HALO rule 6).
        Dim finished As Boolean
        Dim progress As Double = LetterboxFillMath.HaloProgress(halo_Clock.Elapsed.TotalMilliseconds, halo_Running_Duration_Ms, finished)

        Try
            RenderHaloFrame(progress)
        Catch ex As Exception
            ' Cosmetics - never let a GDI+ transient take the (already shown) photo
            ' with it. The next navigation or resize repaints the background.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1135: halo frame skipped: [" & ex.GetType().Name & "] " & ex.Message)
            AppFileLogger.WriteLine("Perspective halo frame skipped: [" & ex.GetType().Name & "] " & ex.Message)
            finished = True
        End Try

        target.Invalidate()

        If finished Then
            halo_Timer.Stop()
            halo_Clock.Reset()
            ReleaseHaloSource()
            ' The last frame drew it at alpha 0, so nothing on screen depends on it now.
            ReleaseHaloUnderlay()
            halo_Frame = Nothing
        End If
    End Sub

    ''' <summary>Repaints the frame for one point in the growth: the clean bars, then a
    ''' background ramp over each bar reaching `eased * barWidth` out from the photo.</summary>
    Private Sub RenderHaloFrame(progress As Double)
        Dim eased As Double = LetterboxFillMath.HaloEase(progress)
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
                HaloPainter.PaintFade(g, background, New Rectangle(0, 0, halo_Image_Rect.Left, height), True, True, eased)
                HaloPainter.PaintFade(g, background, New Rectangle(halo_Image_Rect.Right, 0, width - halo_Image_Rect.Right, height), True, False, eased)
            Else
                HaloPainter.PaintFade(g, background, New Rectangle(0, 0, width, halo_Image_Rect.Top), False, True, eased)
                HaloPainter.PaintFade(g, background, New Rectangle(0, halo_Image_Rect.Bottom, width, height - halo_Image_Rect.Bottom), False, False, eased)
            End If

            ' Last, over the finished frame: what was on screen before this photo, fading
            ' out as the growth runs. Everything above this line is untouched - the frame
            ' underneath is exactly what it always was, and at eased = 1 this draws nothing
            ' at all, so the halo still comes to rest on the same pixels.
            PaintHaloUnderlay(g, eased)
        End Using
    End Sub

    ''' <summary>
    ''' The previous frame, laid whole over this one at alpha 1-eased.
    '''
    ''' ONE DrawImage, over the whole surface. Fading it by DISTANCE instead - showing it
    ''' where the growth has not reached - was tried and is wrong twice over: the bar ends
    ''' the growth showing the old frame rather than the background, and painting a picture
    ''' through a gradient means slicing it into constant-alpha bands, which is visible as
    ''' stripes and as blocks at the corners. Time is the right axis: the old frame is what
    ''' the eye was already on, and it simply stops being there.
    ''' </summary>
    Private Sub PaintHaloUnderlay(g As Graphics, eased As Double)
        If halo_Underlay Is Nothing Then Return

        Dim fade As Single = CSng(LetterboxFillMath.UnderlayOpacity(eased))
        If fade <= 0.0F Then Return

        Dim area As New Rectangle(0, 0, halo_Frame.Width, halo_Frame.Height)
        Using attributes As New Imaging.ImageAttributes()
            attributes.SetColorMatrix(New Imaging.ColorMatrix() With {.Matrix33 = fade})
            g.DrawImage(halo_Underlay, area, 0, 0, area.Width, area.Height, GraphicsUnit.Pixel, attributes)
        End Using
    End Sub

End Class
#End If
