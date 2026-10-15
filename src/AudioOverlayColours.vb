#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Drawing

' DECLARED OUTSIDE THE THEME - a content overlay (APP-STYLE rule 5 and its 0.10 amendment, condition 3;
' WINDOWS-UI section 6 item 7 and section 8 item 1).
'
' The audio surface of the viewer (Main_Form.AudioView.vb) paints over a cover picture or over the WAVE-PARTICLES
' backdrop, and both are CONTENT: a cover can be anything from pure white to pure black, and the backdrop is
' lines and particles in a hue the session rolled. A themed text colour there would be unreadable half the time
' - the defect rule 5 exists to prevent - so the title, the artist, the album and the format line are NOT theme
' roles. They stand on a plate, and the plate and its ink are one pair that carries its own contrast:
'
'   * the plate is a scrim, black at PlateAlpha / 255, painted over whatever is under it;
'   * the ink is light (white; the format line a step softer), so the worst content under the plate is the
'     brightest one - pure white - and the plate is what makes that readable.
'
' They are the same in the light and the dark application theme, on purpose: they follow the content, not the
' theme. The pair is held by AudioOverlayContrastTests - every text ink over the plate over pure white and pure
' black (and the greys and corners of the colour cube between) is at least TextContrastFloor, every stroke ink
' StrokeContrastFloor (WINDOWS-UI section 6 item 4) - so a changed alpha or ink fails by name instead of being
' found on a pale cover. Main_Form.AudioView.vb carries no colour literal of its own; the same test reads it.
'
' What this table does NOT own: the visualiser's wash, lines and particles. Their colours and opacities are the
' WAVE-PARTICLES contract's (src/Backdrop/WaveParticlesConstants.vb) and are not tuned here.
Friend NotInheritable Class AudioOverlayColours

    Private Sub New()
    End Sub

    ' --- the plate ---------------------------------------------------------------------------------------------

    ''' <summary>Opacity of the scrim, 0..255. 175 is 68.6 percent: pure white content under it is 80/80/80.</summary>
    Friend Const PlateAlpha As Integer = 175

    ''' <summary>The scrim itself: black, translucent. Drawn under every line of text of the surface.</summary>
    Friend Shared ReadOnly PlateBacking As Color = Color.FromArgb(PlateAlpha, 0, 0, 0)

    ' --- text ink (carries WCAG 4.5 : 1 over the plate over any content) ----------------------------------------

    Friend Shared ReadOnly TitleInk As Color = Color.White
    ''' <summary>Artist and album.</summary>
    Friend Shared ReadOnly DetailInk As Color = Color.White
    ''' <summary>The "MP3 - 5.2 MB" line: a step quieter than the title, still a text.</summary>
    Friend Shared ReadOnly FormatInk As Color = Color.Gainsboro

    ' --- stroke ink (carries WCAG 3 : 1) -------------------------------------------------------------------------

    ''' <summary>The pen and the fill the visualiser is made with. Every pass sets the session's own colour on them
    ''' before it draws, so this is the value an unset stroke would have - declared so no literal is left behind.</summary>
    Friend Shared ReadOnly StrokeInk As Color = Color.White

    ' --- the bars the pair is held to ----------------------------------------------------------------------------

    ''' <summary>WINDOWS-UI section 6 item 4: normal text against its actual fill.</summary>
    Friend Const TextContrastFloor As Double = 4.5R
    ''' <summary>WINDOWS-UI section 6 item 4: meaningful glyphs and strokes.</summary>
    Friend Const StrokeContrastFloor As Double = 3.0R

    ''' <summary>What the eye gets of a translucent scrim over an opaque content colour: the scrim composited over it.</summary>
    Friend Shared Function Composite(scrim As Color, content As Color) As Color
        Dim covered As Double = scrim.A / 255.0R
        Return Color.FromArgb(255,
            CInt(Math.Round(content.R * (1.0R - covered) + scrim.R * covered)),
            CInt(Math.Round(content.G * (1.0R - covered) + scrim.G * covered)),
            CInt(Math.Round(content.B * (1.0R - covered) + scrim.B * covered)))
    End Function

    ''' <summary>The plate over an opaque content colour.</summary>
    Friend Shared Function PlateOver(content As Color) As Color
        Return Composite(PlateBacking, content)
    End Function

    ''' <summary>WCAG contrast of an ink over the plate over opaque content.</summary>
    Friend Shared Function InkContrast(ink As Color, content As Color) As Double
        Return Utils.ContrastRatio(ink, PlateOver(content))
    End Function

    Friend Shared Function TextInks() As Color()
        Return {TitleInk, DetailInk, FormatInk}
    End Function

    Friend Shared Function StrokeInks() As Color()
        Return {StrokeInk}
    End Function

End Class
#End If
