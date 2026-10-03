#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The colours of the floating chrome, derived from the active colour scheme (002 section 2.3) so a
''' panel keeps following the picture the way the window already does. Pure: colours in, colours out -
''' the ratios live in <see cref="ChromeMetrics"/>, the high-contrast switch is a parameter, so all of
''' it can be tested without a window.
'''
''' Every colour here is OPAQUE. The translucency of a panel is the host window's Opacity (a child
''' control cannot alpha-compose over the media, only an owned top-level window can), so a hover
''' tint is precomputed as "foreground blended over the fill" rather than drawn with an alpha.
''' </summary>
Friend NotInheritable Class ChromeTheme

    Private Sub New()
    End Sub

    Public Property PanelCornerRadius As Integer = ChromeMetrics.PanelCorner
    Public Property PanelFill As Color
    Public Property Foreground As Color
    Public Property Hairline As Color
    Public Property ItemHover As Color
    Public Property ItemPressed As Color
    Public Property Accent As Color
    Public Property AccentForeground As Color
    Public Property DisabledForeground As Color
    Public Property FocusRing As Color
    Public Property DestructiveRest As Color
    Public Property DestructiveHover As Color
    Public Property DestructivePressed As Color
    Public Property DestructiveForeground As Color

    ''' <summary>Host window opacity, 0..1. 1.0 under high contrast.</summary>
    Public Property PanelOpacity As Double

    ''' <summary>False under high contrast: no fade, no ease, no translucency, no accent fill.</summary>
    Public Property AllowsAnimation As Boolean

    ''' <summary>True when the panel draws system colours (high contrast).</summary>
    Public Property IsHighContrast As Boolean

    ''' <param name="back">The scheme's background colour (what the window is painted).</param>
    ''' <param name="fore">The scheme's contrasting colour (what text on it is painted).</param>
    ''' <param name="highContrast">Windows high contrast is on: system colours, nothing translucent.</param>
    Public Shared Function FromScheme(back As Color, fore As Color, highContrast As Boolean) As ChromeTheme
        If highContrast Then Return ForHighContrast()

        Dim fill As Color = Opaque(back)
        Dim ink As Color = Opaque(fore)
        Dim accent As Color = Opaque(SystemColors.Highlight)

        Return New ChromeTheme() With {
            .PanelFill = fill,
            .Foreground = ink,
            .Hairline = Blend(fill, ink, ChromeMetrics.HairlinePercent / 100.0F),
            .ItemHover = Blend(fill, ink, ChromeMetrics.HoverPercent / 100.0F),
            .ItemPressed = Blend(fill, ink, ChromeMetrics.PressedPercent / 100.0F),
            .Accent = accent,
            .AccentForeground = ContrastingText(accent),
            .DisabledForeground = Blend(fill, ink, ChromeMetrics.DisabledPercent / 100.0F),
            .FocusRing = accent,
            .DestructiveRest = ChromeMetrics.DestructiveRest,
            .DestructiveHover = ChromeMetrics.DestructiveHover,
            .DestructivePressed = ChromeMetrics.DestructivePressed,
            .DestructiveForeground = Color.White,
            .PanelOpacity = ChromeMetrics.PanelOpacityPercent / 100.0R,
            .AllowsAnimation = True,
            .IsHighContrast = False
        }
    End Function

    ''' <summary>The high-contrast branch (002 section 5): system colours, an opaque panel, no
    ''' animation, and a selection that is told apart by the system's own highlight pair.</summary>
    Public Shared Function ForHighContrast() As ChromeTheme
        Dim fill As Color = SystemColors.Window
        Dim ink As Color = SystemColors.WindowText
        Return New ChromeTheme() With {
            .PanelFill = fill,
            .Foreground = ink,
            .Hairline = ink,
            .ItemHover = SystemColors.Highlight,
            .ItemPressed = SystemColors.Highlight,
            .Accent = SystemColors.Highlight,
            .AccentForeground = SystemColors.HighlightText,
            .DisabledForeground = SystemColors.GrayText,
            .FocusRing = SystemColors.Highlight,
            .DestructiveRest = fill,
            .DestructiveHover = SystemColors.Highlight,
            .DestructivePressed = SystemColors.Highlight,
            .DestructiveForeground = ink,
            .PanelOpacity = 1.0R,
            .AllowsAnimation = False,
            .IsHighContrast = True
        }
    End Function

    ''' <summary>The colour an item's picture or caption is drawn in for a state.</summary>
    Public Function InkFor(enabled As Boolean, checked As Boolean, destructive As Boolean, hover As Boolean) As Color
        If Not enabled Then Return DisabledForeground
        If hover AndAlso IsHighContrast Then Return SystemColors.HighlightText
        If destructive Then Return DestructiveForeground
        If checked AndAlso Not IsHighContrast Then Return AccentForeground
        Return Foreground
    End Function

    ''' <summary>The fill behind an item for a state; <see cref="Color.Empty"/> = nothing is drawn,
    ''' the panel shows through. Pressed wins over hover wins over rest.</summary>
    Public Function FillFor(enabled As Boolean, checked As Boolean, destructive As Boolean,
                            hover As Boolean, pressed As Boolean) As Color
        If destructive Then
            If Not enabled Then Return Color.Empty
            If pressed Then Return DestructivePressed
            If hover Then Return DestructiveHover
            Return DestructiveRest
        End If
        If Not enabled Then Return Color.Empty
        If pressed Then Return If(checked AndAlso Not IsHighContrast, Blend(Accent, Foreground, ChromeMetrics.HoverPercent / 100.0F), ItemPressed)
        If checked AndAlso Not IsHighContrast Then
            Return If(hover, Blend(Accent, Foreground, ChromeMetrics.HoverPercent / 200.0F), Accent)
        End If
        If hover Then Return ItemHover
        Return Color.Empty
    End Function

    ' --- colour arithmetic ---------------------------------------------------------------

    ''' <summary>Rec. 601 luma below 128 is a dark colour (the rule the window already uses).</summary>
    Public Shared Function IsDark(c As Color) As Boolean
        Return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128.0
    End Function

    ''' <summary>Black or white, whichever reads on <paramref name="background"/>.</summary>
    Public Shared Function ContrastingText(background As Color) As Color
        Return If(IsDark(background), Color.White, Color.Black)
    End Function

    ''' <summary>a + (b - a) * t per channel, rounded and clamped; the result is opaque.</summary>
    Public Shared Function Blend(a As Color, b As Color, t As Single) As Color
        If Single.IsNaN(t) Then t = 0.0F
        If t < 0.0F Then t = 0.0F
        If t > 1.0F Then t = 1.0F
        Return Color.FromArgb(255,
                              Channel(CInt(a.R) + (CInt(b.R) - CInt(a.R)) * t),
                              Channel(CInt(a.G) + (CInt(b.G) - CInt(a.G)) * t),
                              Channel(CInt(a.B) + (CInt(b.B) - CInt(a.B)) * t))
    End Function

    Private Shared Function Channel(value As Single) As Integer
        If value <= 0.0F Then Return 0
        If value >= 255.0F Then Return 255
        Return CInt(Math.Round(value, MidpointRounding.AwayFromZero))
    End Function

    Private Shared Function Opaque(c As Color) As Color
        Return Color.FromArgb(255, c.R, c.G, c.B)
    End Function

End Class
#End If
