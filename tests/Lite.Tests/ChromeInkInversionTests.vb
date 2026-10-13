#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The 220 rule: below half of a panel's set opacity every label, glyph and outline the panel
''' draws inverts tone - black becomes white, white becomes black - while the fills, the accent
''' family and the destructive reds keep their hue, and high contrast never flips. Pure colour
''' checks and call-site guards; no window is shown.
''' </summary>
Public Class ChromeInkInversionTests

    <Theory>
    <InlineData(49, True)>
    <InlineData(40, True)>
    <InlineData(0, True)>
    <InlineData(50, False)>
    <InlineData(51, False)>
    <InlineData(86, False)>
    <InlineData(100, False)>
    Public Sub The_flip_fires_strictly_below_half_of_the_set_value(setOpacityPercent As Integer, expected As Boolean)
        Assert.Equal(expected, ChromeMetrics.InvertsInk(setOpacityPercent))
        Assert.Equal(50, ChromeMetrics.InkInvertBelowPercent)
    End Sub

    <Theory>
    <InlineData(40, 40, 40, 255, 255, 255)>
    <InlineData(235, 235, 235, 20, 20, 20)>
    Public Sub The_flipped_theme_trades_the_ink_pair_and_recomputes_the_blends(br As Integer, bg As Integer,
                                                                              bb As Integer, fr As Integer,
                                                                              fg As Integer, fb_ As Integer)
        Dim base_Theme As ChromeTheme = ChromeTheme.FromScheme(Color.FromArgb(br, bg, bb), Color.FromArgb(fr, fg, fb_), False)
        Dim flipped As ChromeTheme = base_Theme.WithInvertedInk()

        Assert.False(base_Theme.InkInverted)
        Assert.True(flipped.InkInverted)

        ' The ink pair trades places: a dark ink becomes white, a light one black.
        Dim flippedInk As Color = ChromeTheme.InvertedTone(base_Theme.Foreground)
        Assert.Equal(flippedInk.ToArgb(), flipped.Foreground.ToArgb())
        Assert.Equal(ChromeTheme.InvertedTone(base_Theme.DestructiveForeground).ToArgb(),
                     flipped.DestructiveForeground.ToArgb())
        Assert.Equal(Color.Black.ToArgb(), flipped.DestructiveForeground.ToArgb())   ' white -> black, the owner's word

        ' The derived inks recompute from the flipped pair, not from the old ink.
        Assert.Equal(ChromeTheme.Blend(base_Theme.PanelFill, flippedInk, ChromeMetrics.HairlinePercent / 100.0F).ToArgb(),
                     flipped.Hairline.ToArgb())
        Assert.Equal(ChromeTheme.Blend(base_Theme.PanelFill, flippedInk, ChromeMetrics.DisabledPercent / 100.0F).ToArgb(),
                     flipped.DisabledForeground.ToArgb())

        ' The fills and the colour identities are copied unchanged.
        Assert.Equal(base_Theme.PanelFill.ToArgb(), flipped.PanelFill.ToArgb())
        Assert.Equal(base_Theme.ItemHover.ToArgb(), flipped.ItemHover.ToArgb())
        Assert.Equal(base_Theme.ItemPressed.ToArgb(), flipped.ItemPressed.ToArgb())
        Assert.Equal(base_Theme.Accent.ToArgb(), flipped.Accent.ToArgb())
        Assert.Equal(base_Theme.AccentForeground.ToArgb(), flipped.AccentForeground.ToArgb())
        Assert.Equal(base_Theme.FocusRing.ToArgb(), flipped.FocusRing.ToArgb())
        Assert.Equal(base_Theme.DestructiveRest.ToArgb(), flipped.DestructiveRest.ToArgb())
        Assert.Equal(base_Theme.DestructiveHover.ToArgb(), flipped.DestructiveHover.ToArgb())
        Assert.Equal(base_Theme.DestructivePressed.ToArgb(), flipped.DestructivePressed.ToArgb())
        Assert.Equal(base_Theme.PanelOpacity, flipped.PanelOpacity)
        Assert.Equal(base_Theme.AllowsAnimation, flipped.AllowsAnimation)
        Assert.Equal(base_Theme.PanelCornerRadius, flipped.PanelCornerRadius)

        ' The ink the surface asks for follows the flip; the checked accent keeps its hue.
        Assert.Equal(flipped.Foreground.ToArgb(), flipped.InkFor(True, False, False, False).ToArgb())
        Assert.Equal(flipped.DisabledForeground.ToArgb(), flipped.InkFor(False, False, False, False).ToArgb())
        Assert.Equal(base_Theme.AccentForeground.ToArgb(), flipped.InkFor(True, True, False, False).ToArgb())
        Assert.Equal(flipped.DestructiveForeground.ToArgb(), flipped.InkFor(True, False, True, False).ToArgb())
    End Sub

    <Fact>
    Public Sub No_mid_gray_survives_the_flip_un_inverted()
        Dim gray As Color = Color.FromArgb(128, 128, 128)
        Assert.NotEqual(gray.ToArgb(), ChromeTheme.InvertedTone(gray).ToArgb())
        Assert.Equal(Color.White.ToArgb(), ChromeTheme.InvertedTone(gray).ToArgb())
        Assert.Equal(Color.White.ToArgb(), ChromeTheme.InvertedTone(Color.Black).ToArgb())
        Assert.Equal(Color.Black.ToArgb(), ChromeTheme.InvertedTone(Color.White).ToArgb())
        ' The mapping is tone-based, never a channel rotation: a dark blue does not come back yellow.
        Assert.Equal(Color.White.ToArgb(), ChromeTheme.InvertedTone(Color.FromArgb(30, 100, 200)).ToArgb())
    End Sub

    <Fact>
    Public Sub The_outline_flips_with_the_ink_because_it_is_always_the_opposite_tone()
        For Each ink As Color In {Color.Gainsboro, Color.FromArgb(30, 30, 30), Color.White, Color.Black}
            Dim flipped As Color = ChromeTheme.InvertedTone(ink)
            Assert.Equal(ChromeTheme.InvertedTone(ChromeInkOutline.OutlineFor(ink)).ToArgb(),
                         ChromeInkOutline.OutlineFor(flipped).ToArgb())
        Next
        ' Black glyph / white outline becomes white glyph / black outline.
        Assert.Equal(Color.White.ToArgb(), ChromeInkOutline.OutlineFor(Color.FromArgb(30, 30, 30)).ToArgb())
        Assert.Equal(Color.Black.ToArgb(), ChromeInkOutline.OutlineFor(Color.White).ToArgb())
    End Sub

    <Fact>
    Public Sub High_contrast_never_flips()
        Dim theme As ChromeTheme = ChromeTheme.ForHighContrast()
        Assert.Same(theme, theme.WithInvertedInk())
        Assert.False(theme.WithInvertedInk().InkInverted)
        Assert.Equal(1.0R, theme.PanelOpacity)
    End Sub

    <Fact>
    Public Sub Both_flip_call_sites_key_on_the_set_value_and_their_own_knobs()
        Dim chrome As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.Chrome.vb"))
        Assert.Matches("ChromeMetrics\.InvertsInk\(modern_Preferences\.ChromeOpacityPercent\)", chrome)
        Assert.Matches("chrome_Theme\s*=\s*chrome_Base_Theme\.WithInvertedInk\(\)", chrome)
        ' The theme rebuild keeps the base un-flipped for the surfaces with their own knob.
        Assert.Matches("chrome_Base_Theme\s*=\s*ChromeTheme\.FromScheme\(", chrome)

        Dim overlay As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.RecipientsOverlay.vb"))
        Assert.Matches("InvertsInk\(modern_Preferences\.RecipientsOverlayOpacity\)", overlay)
        Assert.Matches("InvertedTone\(ThemePalette\.ForDark\(ThemeService\.IsDark\)\.TextPrimary\)", overlay)
        ' The overlay never reads the panels' flipped view - its own knob decides its flip.
        Assert.DoesNotMatch("chrome_Theme\.", overlay)

        ' The settings row's hint names the flip (Q220-4), in the 13-language discipline's source.
        Dim hint As String = File.ReadAllText(Path.Combine(SourceDir(), "Localization", "Localization.Chrome.vb"))
        Assert.Matches("Ниже 50 % надписи и значки инвертируются", hint)
    End Sub

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function

End Class
#End If
