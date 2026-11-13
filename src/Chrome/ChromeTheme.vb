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

    ' L0303: the roles a theme built by FromColours carries from the panel colour model. Empty on the
    ' themes FromScheme and ForMenu build, which keep their own blends.
    Public Property AccentHover As Color = Color.Empty
    Public Property AccentPressed As Color = Color.Empty
    Public Property PinRest As Color = Color.Empty
    Public Property PinPinned As Color = Color.Empty
    Public Property WarningForeground As Color = Color.Empty
    Public Property ErrorForeground As Color = Color.Empty
    ''' <summary>R-11: the smallest window opacity at which text that fades with the window (a native edit
    ''' being typed in) keeps its floor over any content. 0 on the themes FromScheme and ForMenu build.</summary>
    Public Property EditFloorOpacity As Double
    ''' <summary>The transport sliders' unplayed track from the model (R-11); empty on the other themes.</summary>
    Public Property SliderTrack As Color = Color.Empty
    ''' <summary>R-11 for the transport's A-B chip: the window opacity its caption needs while it is shown.</summary>
    Public Property ChipFloorOpacity As Double

    ''' <summary>The floating panels' theme (L0303): every colour is the panel colour model's, the opacity
    ''' is the set value and never changes an ink (Q303-3). High contrast stays the system branch (R-13).</summary>
    Public Shared Function FromColours(colours As PanelColours) As ChromeTheme
        If colours Is Nothing Then Return ForHighContrast()
        If colours.IsHighContrast Then
            Dim systemTheme As ChromeTheme = ForHighContrast()
            systemTheme.SliderTrack = colours.SliderTrack
            Return systemTheme
        End If
        Return New ChromeTheme() With {
            .PanelFill = colours.Fill,
            .Foreground = colours.Ink,
            .Hairline = colours.Hairline,
            .ItemHover = colours.Hover,
            .ItemPressed = colours.Pressed,
            .Accent = colours.Checked,
            .AccentForeground = colours.CheckedInk,
            .AccentHover = colours.CheckedHover,
            .AccentPressed = colours.CheckedPressed,
            .DisabledForeground = colours.InkDisabled,
            .FocusRing = colours.FocusRing,
            .DestructiveRest = colours.Destructive,
            .DestructiveHover = colours.DestructiveHover,
            .DestructivePressed = colours.DestructivePressed,
            .DestructiveForeground = colours.DestructiveInk,
            .PinRest = colours.PinRestInk,
            .PinPinned = colours.PinPinnedInk,
            .WarningForeground = colours.WarningInk,
            .ErrorForeground = colours.ErrorInk,
            .PanelOpacity = colours.SetOpacity,
            .EditFloorOpacity = colours.WholeWindowFloor,
            .SliderTrack = colours.SliderTrack,
            .ChipFloorOpacity = colours.ChipFloor,
            .AllowsAnimation = True,
            .IsHighContrast = False
        }
    End Function

    ''' <summary>The ink of a status line that carries a warning or an error: the model's role ink when this
    ''' theme came from it, <see cref="Color.Empty"/> otherwise (the caller then keeps its own rule).</summary>
    Public Function ToneInk(tone As NotifyLevel) As Color
        If tone = NotifyLevel.Warning Then Return WarningForeground
        If tone = NotifyLevel.Error Then Return ErrorForeground
        Return Color.Empty
    End Function

    ''' <param name="back">The scheme's background colour (what the window is painted).</param>
    ''' <param name="fore">The scheme's contrasting colour (what text on it is painted).</param>
    ''' <param name="highContrast">Windows high contrast is on: system colours, nothing translucent.</param>
    Public Shared Function FromScheme(back As Color, fore As Color, highContrast As Boolean) As ChromeTheme
        If highContrast Then Return ForHighContrast()

        Dim fill As Color = Opaque(back)
        Dim ink As Color = Opaque(fore)
        If IsDark(fill) = IsDark(ink) Then
            ink = ContrastingText(fill)
        End If
        ' The user's own accent from Windows (the same source the dark palette uses), the system highlight when
        ' Windows gives none (124 UX-215): one blue for the chrome and the windows beside it.
        Dim accent As Color = Opaque(AccentColors.ForChrome(SystemColors.Highlight))

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

    ''' <summary>The 220 tone inversion: a dark ink becomes white, a light one black - the same binary
    ''' decision IsDark the outline already makes, so pure black and pure white trade places and no
    ''' mid-grey survives un-flipped. Hue is never rotated: a coloured ink is neither black nor
    ''' white and keeps its colour.</summary>
    ''' <summary>Builds an opaque, high-contrast theme for menus (spec 060, APP-STYLE rule 5 amendment:
    ''' menus follow the application theme, not content-sampled photo colors).</summary>
    Public Shared Function ForMenu(palette As ThemePalette) As ChromeTheme
        If SystemInformation.HighContrast Then Return ForHighContrast()
        If palette Is Nothing Then palette = ThemePalette.Dark

        Dim fill As Color = palette.SurfaceRaised
        Dim ink As Color = palette.TextPrimary
        Dim border As Color = palette.Border
        Dim hover As Color = palette.ControlHover
        Dim pressed As Color = palette.ControlPressed
        Dim accent As Color = Opaque(AccentColors.ForChrome(palette.Accent))

        Return New ChromeTheme() With {
            .PanelFill = fill,
            .Foreground = ink,
            .Hairline = border,
            .ItemHover = hover,
            .ItemPressed = pressed,
            .Accent = accent,
            .AccentForeground = palette.AccentInk,
            .DisabledForeground = palette.TextDisabled,
            .FocusRing = accent,
            .DestructiveRest = ChromeMetrics.DestructiveRest,
            .DestructiveHover = ChromeMetrics.DestructiveHover,
            .DestructivePressed = ChromeMetrics.DestructivePressed,
            .DestructiveForeground = Color.White,
            .PanelOpacity = 1.0R,
            .AllowsAnimation = False,
            .IsHighContrast = False
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
        If pressed Then
            If checked AndAlso Not IsHighContrast Then
                Return If(AccentPressed.IsEmpty, Blend(Accent, Foreground, ChromeMetrics.HoverPercent / 100.0F), AccentPressed)
            End If
            Return ItemPressed
        End If
        If checked AndAlso Not IsHighContrast Then
            If Not hover Then Return Accent
            Return If(AccentHover.IsEmpty, Blend(Accent, Foreground, ChromeMetrics.HoverPercent / 200.0F), AccentHover)
        End If
        If hover Then Return ItemHover
        Return Color.Empty
    End Function

    ' --- colour arithmetic ---------------------------------------------------------------

    ''' <summary>The corner pin's ink at rest. Strong enough to read on a light scheme and a dark one (about
    ''' 3:1 or better - 125 UX-313h); under high contrast the system pair is used by the caller.</summary>
    Public Function PinRestInk(pinned As Boolean) As Color
        Dim solved As Color = If(pinned, PinPinned, PinRest)
        If Not solved.IsEmpty Then Return solved
        Return Blend(PanelFill, Foreground, If(pinned, ChromeMetrics.PinPinnedPercent, ChromeMetrics.PinRestPercent) / 100.0F)
    End Function

    ''' <summary>WCAG relative luminance of an sRGB colour.</summary>
    Public Shared Function RelativeLuminance(c As Color) As Double
        Dim r As Double = Linear(c.R)
        Dim g As Double = Linear(c.G)
        Dim b As Double = Linear(c.B)
        Return 0.2126 * r + 0.7152 * g + 0.0722 * b
    End Function

    Private Shared Function Linear(channel As Integer) As Double
        Dim v As Double = channel / 255.0
        Return If(v <= 0.03928, v / 12.92, Math.Pow((v + 0.055) / 1.055, 2.4))
    End Function

    ''' <summary>WCAG contrast ratio of two colours, 1 (none) to 21 (black on white).</summary>
    Public Shared Function ContrastRatio(a As Color, b As Color) As Double
        Dim la As Double = RelativeLuminance(a)
        Dim lb As Double = RelativeLuminance(b)
        Dim hi As Double = Math.Max(la, lb)
        Dim lo As Double = Math.Min(la, lb)
        Return (hi + 0.05) / (lo + 0.05)
    End Function

    ''' <summary>A dark colour is one on which white reads better than black by WCAG contrast
    ''' (L0303 R-1: one luminance for every black-or-white decision of the chrome).</summary>
    Public Shared Function IsDark(c As Color) As Boolean
        Return ContrastRatio(Color.White, c) > ContrastRatio(Color.Black, c)
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
