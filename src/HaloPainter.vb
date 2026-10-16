#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Drawing.Drawing2D

' LETTERBOX-HALO rules 2-3. Shared by the viewer and the rendered-pixel check.
Public NotInheritable Class HaloPainter
    Public Shared Sub PaintFade(g As Graphics,
                              background As Color,
                              barRect As Rectangle,
                              horizontal As Boolean,
                              photoEdgeAtFarEnd As Boolean,
                              eased As Double)

        If barRect.Width <= 0 OrElse barRect.Height <= 0 Then Return

        Dim maxDistance As Integer = If(horizontal, barRect.Width, barRect.Height)
        Dim reach As Integer = LetterboxFillMath.HaloReach(maxDistance, eased)

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
        Dim stopCount As Integer = LetterboxFillMath.BlendStops
        Dim positions(stopCount - 1) As Single
        Dim colors(stopCount - 1) As Color

        For i As Integer = 0 To stopCount - 1
            ' Position runs along the gradient; distance runs out from the photo.
            Dim position As Double = i / (stopCount - 1.0)
            Dim distance As Double = If(photoEdgeAtFarEnd, 1.0 - position, position)
            ' LETTERBOX-HALO rule 2: round(255 * d^1.4), half to even, clamped.
            Dim alpha As Integer = LetterboxFillMath.HaloStopAlpha(distance)

            positions(i) = CSng(position)
            colors(i) = Color.FromArgb(alpha, background)
        Next

        Dim blend As New ColorBlend(stopCount)
        blend.Positions = positions
        blend.Colors = colors
        Return blend
    End Function

End Class
#End If
