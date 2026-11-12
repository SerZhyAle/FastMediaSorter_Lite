#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>The two tones a floating panel can take (L0303 R-2): the picture decides light or dark,
''' nothing else of its colour reaches the tone.</summary>
Friend Enum PanelTone
    Dark
    Light
End Enum

''' <summary>
''' Every constant of the panel colour model (L0303 sections 6.2-6.5), in one table the way
''' <see cref="ChromeMetrics"/> keeps the sizes. Together with <see cref="RecipientPalette"/> this is
''' the only place a panel colour number may be written (L0303 section 6.9).
''' </summary>
Friend NotInheritable Class PanelColourMetrics

    Private Sub New()
    End Sub

    ' R-2 tone: OKLab L of the sample at or above which the panel is light, and the band (centred on
    ' it) inside which the previous tone is kept.
    Public Const LightToneFrom As Double = 0.8R
    Public Const ToneHysteresis As Double = 0.04R

    ' R-3 fill: OKLab L of the palette surfaces (32,32,32) and (240,240,240); the share of the
    ' picture's chroma kept and its caps.
    Public Const DarkFillL As Double = 0.2435R
    Public Const LightFillL As Double = 0.955R
    Public Const TintShare As Double = 0.35R
    Public Const DarkChromaCap As Double = 0.035R
    Public Const LightChromaCap As Double = 0.025R
    ''' <summary>Below this OKLCH chroma a sample has no hue worth keeping.</summary>
    Public Const NeutralChroma As Double = 0.0005R

    ' R-4 inks: the palettes' TextPrimary (ThemePalette.Dark (240,240,240), ThemePalette.Light the
    ' system ControlText, black).
    Public Shared ReadOnly InkOnDark As Color = Color.FromArgb(255, 240, 240, 240)
    Public Shared ReadOnly InkOnLight As Color = Color.FromArgb(255, 0, 0, 0)

    ' R-5 state steps in OKLab L.
    Public Const DarkHoverStep As Double = 0.06R
    Public Const DarkPressedStep As Double = 0.11R
    Public Const LightHoverStep As Double = -0.05R
    Public Const LightPressedStep As Double = -0.09R

    ' Floors (WINDOWS-UI section 6 item 4) and the margin a solved colour aims above its floor so
    ' byte rounding cannot drop it under.
    Public Const TextFloor As Double = 4.5R
    Public Const GlyphFloor As Double = 3.0R
    Public Const FloorMargin As Double = 0.05R
    Public Const StateStepFloor As Double = 1.15R
    Public Const HairlineContrast As Double = 1.4R
    Public Const DisabledContrast As Double = 3.0R
    Public Const PinRestContrast As Double = 3.0R
    Public Const PinPinnedContrast As Double = 4.5R
    ''' <summary>The step of a solved lightness search in OKLab L.</summary>
    Public Const SolveStep As Double = 0.005R

    ' R-7 checked accent.
    Public Const DarkAccentMinL As Double = 0.42R
    Public Const DarkAccentMaxL As Double = 0.58R
    Public Const LightAccentMinL As Double = 0.5R
    Public Const LightAccentMaxL As Double = 0.64R
    Public Const AccentChromaCap As Double = 0.13R
    Public Const AccentStep As Double = 0.01R

    ' R-7 destructive family: the palettes' Danger hue at a fixed lightness and chroma per tone.
    Public Shared ReadOnly DangerHueDark As Color = Color.FromArgb(255, 255, 140, 130)
    Public Shared ReadOnly DangerHueLight As Color = Color.FromArgb(255, 178, 34, 34)
    Public Const DarkDestructiveL As Double = 0.38R
    Public Const DarkDestructiveC As Double = 0.1R
    Public Const LightDestructiveL As Double = 0.92R
    Public Const LightDestructiveC As Double = 0.04R

    ' R-7 status inks: the palettes' Warning and Danger roles of the tone (ThemePalette DarkValue /
    ' LightValue), solved against the worst-case backdrop.
    Public Shared ReadOnly WarningOnDark As Color = Color.FromArgb(255, 240, 178, 82)
    Public Shared ReadOnly ErrorOnDark As Color = Color.FromArgb(255, 255, 140, 130)
    Public Shared ReadOnly WarningOnLight As Color = Color.FromArgb(255, 239, 108, 0)
    Public Shared ReadOnly ErrorOnLight As Color = Color.FromArgb(255, 178, 34, 34)
    Public Const StatusInkStep As Single = 0.05F

    ''' <summary>The colour schemes that paint the window black and white (Form_Color_Scheme 1 and 2):
    ''' their tone is fixed.</summary>
    Public Const BlackScheme As Integer = 1
    Public Const WhiteScheme As Integer = 2

    ''' <summary>How many solved models are kept (R-14).</summary>
    Public Const CacheSize As Integer = 64

End Class

''' <summary>
''' The immutable result of <see cref="PanelColourModel.ForSample"/>: every colour a floating panel
''' and its ink paint, and the opacities that keep the ink readable over any content (L0303 section 6).
''' </summary>
Friend NotInheritable Class PanelColours

    Friend Sub New()
    End Sub

    Public Property Tone As PanelTone
    Public Property Fill As Color
    Public Property Ink As Color
    Public Property InkDisabled As Color
    Public Property Hairline As Color
    Public Property Hover As Color
    Public Property Pressed As Color
    Public Property Checked As Color
    Public Property CheckedInk As Color
    Public Property CheckedHover As Color
    Public Property CheckedPressed As Color
    Public Property Destructive As Color
    Public Property DestructiveHover As Color
    Public Property DestructivePressed As Color
    Public Property DestructiveInk As Color
    Public Property WarningInk As Color
    Public Property ErrorInk As Color
    Public Property PinRestInk As Color
    Public Property PinPinnedInk As Color
    Public Property FocusRing As Color
    ''' <summary>The opacity the user set, 0..1.</summary>
    Public Property SetOpacity As Double
    ''' <summary>The smallest opacity at which the ink reads at 4.5:1 over any content on rest, hover
    ''' and pressed (R-9).</summary>
    Public Property TextOpacityNeeded As Double
    ''' <summary>The same for glyphs at 3:1.</summary>
    Public Property GlyphOpacityNeeded As Double
    ''' <summary>The scrim under text on the rest fill at the set opacity (R-10), 0..1.</summary>
    Public Property TextScrimAlpha As Double
    Public Property GlyphScrimAlpha As Double
    ''' <summary>The smallest window opacity for a surface whose text fades with the window (R-11).</summary>
    Public Property WholeWindowFloor As Double
    Public Property IsHighContrast As Boolean

    ''' <summary>The scrim an item with this fill and ink needs at the set opacity (R-10): 0 at or
    ''' above the opacity the pair needs, exact below. No scrim under high contrast.</summary>
    Public Function ScrimAlphaFor(itemFill As Color, itemInk As Color, isText As Boolean) As Double
        If IsHighContrast Then Return 0.0R
        Dim floor As Double = If(isText, PanelColourMetrics.TextFloor, PanelColourMetrics.GlyphFloor)
        Return PanelColourModel.ScrimAlpha(itemFill, itemInk, floor, SetOpacity)
    End Function

End Class

''' <summary>
''' The one place a floating panel's colours are computed (L0303). Pure: colours and numbers in, an
''' immutable <see cref="PanelColours"/> out; every constant in <see cref="PanelColourMetrics"/>.
''' Contrast is WCAG 2 relative luminance throughout (R-1); hue, chroma and lightness steps are OKLab /
''' OKLCH (Ottosson 2020). Composition is modelled the way a layered window composes: sRGB values
''' blended at a byte alpha.
''' </summary>
Friend NotInheritable Class PanelColourModel

    Private Sub New()
    End Sub

    Private Shared ReadOnly LinearLut As Double() = BuildLinearLut()
    Private Shared ReadOnly CacheLock As New Object()
    Private Shared ReadOnly Cache As New Dictionary(Of String, PanelColours)()
    Private Shared ReadOnly CacheOrder As New Queue(Of String)()

    ''' <summary>The grey ramp and the primaries and secondaries: the content a whole-window floor is
    ''' proved against (R-11).</summary>
    Private Shared ReadOnly FloorContent As Color() = BuildFloorContent()

    ' --- the model ----------------------------------------------------------------------------

    ''' <param name="sample">The scheme colour the window is painted (EdgeSampler's sample, black,
    ''' white or the neutral of an empty surface).</param>
    ''' <param name="scheme">Form_Color_Scheme: 1 black and 2 white keep their tone.</param>
    ''' <param name="previousTone">The tone of the previous picture, Nothing on the first one.</param>
    ''' <param name="setOpacityPercent">The panels' opacity setting, 0..100.</param>
    ''' <param name="accent">The user's Windows accent (opaque).</param>
    ''' <param name="highContrast">Windows high contrast is on.</param>
    Public Shared Function ForSample(sample As Color, scheme As Integer, previousTone As PanelTone?,
                                     setOpacityPercent As Integer, accent As Color,
                                     highContrast As Boolean) As PanelColours
        Dim percent As Integer = Math.Max(0, Math.Min(100, setOpacityPercent))
        Dim key As String = String.Concat(Opaque(sample).ToArgb().ToString(), "|", scheme.ToString(), "|",
                                          If(previousTone.HasValue, CInt(previousTone.Value).ToString(), "-"), "|",
                                          percent.ToString(), "|", Opaque(accent).ToArgb().ToString(), "|",
                                          If(highContrast, "1", "0"))
        SyncLock CacheLock
            Dim hit As PanelColours = Nothing
            If Cache.TryGetValue(key, hit) Then Return hit
        End SyncLock

        Dim result As PanelColours = Compute(sample, scheme, previousTone, percent, accent, highContrast)

        SyncLock CacheLock
            If Not Cache.ContainsKey(key) Then
                Cache(key) = result
                CacheOrder.Enqueue(key)
                While CacheOrder.Count > PanelColourMetrics.CacheSize
                    Cache.Remove(CacheOrder.Dequeue())
                End While
            End If
        End SyncLock
        Return result
    End Function

    Private Shared Function Compute(sample As Color, scheme As Integer, previousTone As PanelTone?,
                                    percent As Integer, accent As Color, highContrast As Boolean) As PanelColours
        If highContrast Then Return ForHighContrast(percent)

        Dim tone As PanelTone = ToneFor(sample, scheme, previousTone)
        Dim dark As Boolean = tone = PanelTone.Dark
        Dim ink As Color = InkFor(tone)
        Dim fill As Color = FillFor(sample, tone)
        Dim hover As Color = StateStep(fill, tone, False)
        Dim pressed As Color = StateStep(fill, tone, True)

        Dim checkedInk As Color = Nothing
        Dim checkedFill As Color = SolveStatefulFill(BandedAccentL(accent, tone),
                                                     Math.Min(OkLch(Opaque(accent))(1), PanelColourMetrics.AccentChromaCap),
                                                     OkLch(Opaque(accent))(2), tone, checkedInk)
        Dim dangerHue As Double = OkLch(If(dark, PanelColourMetrics.DangerHueDark, PanelColourMetrics.DangerHueLight))(2)
        Dim destructiveInk As Color = Nothing
        Dim destructiveFill As Color = SolveStatefulFill(
                                                         If(dark, PanelColourMetrics.DarkDestructiveL, PanelColourMetrics.LightDestructiveL),
                                                         If(dark, PanelColourMetrics.DarkDestructiveC, PanelColourMetrics.LightDestructiveC),
                                                         dangerHue, tone, destructiveInk)

        Dim textNeeded As Double = 0.0R
        Dim glyphNeeded As Double = 0.0R
        For Each f As Color In {fill, hover, pressed}
            textNeeded = Math.Max(textNeeded, OpacityNeeded(f, ink, PanelColourMetrics.TextFloor))
            glyphNeeded = Math.Max(glyphNeeded, OpacityNeeded(f, ink, PanelColourMetrics.GlyphFloor))
        Next

        Dim setOpacity As Double = percent / 100.0R
        Dim stateFills As Color() = {fill, hover}
        Dim warning As Color = SolveStatusInk(If(dark, PanelColourMetrics.WarningOnDark, PanelColourMetrics.WarningOnLight), ink, fill, textNeeded)
        Dim errorInk As Color = SolveStatusInk(If(dark, PanelColourMetrics.ErrorOnDark, PanelColourMetrics.ErrorOnLight), ink, fill, textNeeded)

        Dim floorWindow As Double = 0.0R
        For Each f As Color In {fill, hover, pressed}
            floorWindow = Math.Max(floorWindow, WholeWindowFloor(f, ink, PanelColourMetrics.TextFloor))
        Next

        Return New PanelColours() With {
            .Tone = tone,
            .Fill = fill,
            .Ink = ink,
            .InkDisabled = SolveDerivedInk(stateFills, ink, PanelColourMetrics.DisabledContrast + PanelColourMetrics.FloorMargin),
            .Hairline = SolveDerivedInk({fill}, ink, PanelColourMetrics.HairlineContrast),
            .Hover = hover,
            .Pressed = pressed,
            .Checked = checkedFill,
            .CheckedInk = checkedInk,
            .CheckedHover = StateStep(checkedFill, tone, False),
            .CheckedPressed = StateStep(checkedFill, tone, True),
            .Destructive = destructiveFill,
            .DestructiveHover = StateStep(destructiveFill, tone, False),
            .DestructivePressed = StateStep(destructiveFill, tone, True),
            .DestructiveInk = destructiveInk,
            .WarningInk = warning,
            .ErrorInk = errorInk,
            .PinRestInk = SolveDerivedInk(stateFills, ink, PanelColourMetrics.PinRestContrast + PanelColourMetrics.FloorMargin),
            .PinPinnedInk = SolveDerivedInk(stateFills, ink, PanelColourMetrics.PinPinnedContrast + PanelColourMetrics.FloorMargin),
            .FocusRing = checkedFill,
            .SetOpacity = setOpacity,
            .TextOpacityNeeded = textNeeded,
            .GlyphOpacityNeeded = glyphNeeded,
            .TextScrimAlpha = ScrimAlphaFrom(textNeeded, setOpacity),
            .GlyphScrimAlpha = ScrimAlphaFrom(glyphNeeded, setOpacity),
            .WholeWindowFloor = floorWindow,
            .IsHighContrast = False
        }
    End Function

    ''' <summary>R-13: system colours, opaque, no scrim, no floor.</summary>
    Private Shared Function ForHighContrast(percent As Integer) As PanelColours
        Dim fill As Color = SystemColors.Window
        Dim ink As Color = SystemColors.WindowText
        Return New PanelColours() With {
            .Tone = If(RelativeLuminance(fill) < 0.5R, PanelTone.Dark, PanelTone.Light),
            .Fill = fill, .Ink = ink, .InkDisabled = SystemColors.GrayText, .Hairline = ink,
            .Hover = SystemColors.Highlight, .Pressed = SystemColors.Highlight,
            .Checked = SystemColors.Highlight, .CheckedInk = SystemColors.HighlightText,
            .CheckedHover = SystemColors.Highlight, .CheckedPressed = SystemColors.Highlight,
            .Destructive = fill, .DestructiveHover = SystemColors.Highlight, .DestructivePressed = SystemColors.Highlight,
            .DestructiveInk = ink, .WarningInk = ink, .ErrorInk = ink, .PinRestInk = ink, .PinPinnedInk = ink,
            .FocusRing = SystemColors.Highlight, .SetOpacity = percent / 100.0R,
            .TextOpacityNeeded = 0.0R, .GlyphOpacityNeeded = 0.0R, .TextScrimAlpha = 0.0R, .GlyphScrimAlpha = 0.0R,
            .WholeWindowFloor = 0.0R, .IsHighContrast = True
        }
    End Function

    ''' <summary>R-2: light when the sample's OKLab L reaches <see cref="PanelColourMetrics.LightToneFrom"/>;
    ''' inside the hysteresis band the previous tone holds. The black scheme is always dark, the white
    ''' scheme always light.</summary>
    Public Shared Function ToneFor(sample As Color, scheme As Integer, previousTone As PanelTone?) As PanelTone
        If scheme = PanelColourMetrics.BlackScheme Then Return PanelTone.Dark
        If scheme = PanelColourMetrics.WhiteScheme Then Return PanelTone.Light
        Dim l As Double = OkLch(Opaque(sample))(0)
        Dim half As Double = PanelColourMetrics.ToneHysteresis / 2.0R
        If previousTone.HasValue Then
            If previousTone.Value = PanelTone.Light AndAlso l >= PanelColourMetrics.LightToneFrom - half Then Return PanelTone.Light
            If previousTone.Value = PanelTone.Dark AndAlso l < PanelColourMetrics.LightToneFrom + half Then Return PanelTone.Dark
        End If
        Return If(l >= PanelColourMetrics.LightToneFrom, PanelTone.Light, PanelTone.Dark)
    End Function

    Public Shared Function InkFor(tone As PanelTone) As Color
        Return If(tone = PanelTone.Dark, PanelColourMetrics.InkOnDark, PanelColourMetrics.InkOnLight)
    End Function

    ''' <summary>R-3: the sample's hue, a share of its chroma under the tone's cap, the palette
    ''' surface's lightness.</summary>
    Public Shared Function FillFor(sample As Color, tone As PanelTone) As Color
        Dim lch As Double() = OkLch(Opaque(sample))
        Dim dark As Boolean = tone = PanelTone.Dark
        Dim chroma As Double = Math.Min(lch(1) * PanelColourMetrics.TintShare,
                                        If(dark, PanelColourMetrics.DarkChromaCap, PanelColourMetrics.LightChromaCap))
        If lch(1) < PanelColourMetrics.NeutralChroma Then chroma = 0.0R
        Return FromOkLch(If(dark, PanelColourMetrics.DarkFillL, PanelColourMetrics.LightFillL), chroma, lch(2))
    End Function

    ''' <summary>R-5: hover or pressed - the fill's OKLab lightness moved, hue and chroma kept.</summary>
    Public Shared Function StateStep(fill As Color, tone As PanelTone, pressed As Boolean) As Color
        Dim dark As Boolean = tone = PanelTone.Dark
        Dim delta As Double
        If dark Then
            delta = If(pressed, PanelColourMetrics.DarkPressedStep, PanelColourMetrics.DarkHoverStep)
        Else
            delta = If(pressed, PanelColourMetrics.LightPressedStep, PanelColourMetrics.LightHoverStep)
        End If
        Dim lch As Double() = OkLch(fill)
        Return FromOkLch(Clamp01(lch(0) + delta), lch(1), lch(2))
    End Function

    Private Shared Function BandedAccentL(accent As Color, tone As PanelTone) As Double
        Dim l As Double = OkLch(Opaque(accent))(0)
        If tone = PanelTone.Dark Then
            Return Math.Max(PanelColourMetrics.DarkAccentMinL, Math.Min(PanelColourMetrics.DarkAccentMaxL, l))
        End If
        Return Math.Max(PanelColourMetrics.LightAccentMinL, Math.Min(PanelColourMetrics.LightAccentMaxL, l))
    End Function

    ''' <summary>R-7: a fill of the given hue and chroma whose lightness is stepped from the start
    ''' until the better of the two inks reads at the text floor plus the margin on the fill and on its
    ''' hover and pressed steps. The ink is chosen once, on the starting fill.</summary>
    Private Shared Function SolveStatefulFill(startL As Double, chroma As Double, hue As Double,
                                              tone As PanelTone, ByRef ink As Color) As Color
        Dim target As Double = PanelColourMetrics.TextFloor + PanelColourMetrics.FloorMargin
        Dim l As Double = startL
        Dim fill As Color = FromOkLch(l, chroma, hue)
        ink = BetterInk(fill)
        Dim inkIsLight As Boolean = ink.ToArgb() = PanelColourMetrics.InkOnDark.ToArgb()
        Dim direction As Double = If(inkIsLight, -PanelColourMetrics.AccentStep, PanelColourMetrics.AccentStep)
        For guard As Integer = 0 To 120
            If WorstStateContrast(fill, ink, tone) >= target Then Return fill
            If (direction < 0 AndAlso l <= 0.0R) OrElse (direction > 0 AndAlso l >= 1.0R) Then Exit For
            l = Clamp01(l + direction)
            fill = FromOkLch(l, chroma, hue)
        Next
        Return fill
    End Function

    Private Shared Function WorstStateContrast(fill As Color, ink As Color, tone As PanelTone) As Double
        Return Math.Min(ContrastRatio(ink, fill),
                        Math.Min(ContrastRatio(ink, StateStep(fill, tone, False)), ContrastRatio(ink, StateStep(fill, tone, True))))
    End Function

    ''' <summary>The better of the two palette inks on a fill, by contrast ratio (R-1).</summary>
    Public Shared Function BetterInk(fill As Color) As Color
        Return If(ContrastRatio(PanelColourMetrics.InkOnDark, fill) >= ContrastRatio(PanelColourMetrics.InkOnLight, fill),
                  PanelColourMetrics.InkOnDark, PanelColourMetrics.InkOnLight)
    End Function

    ''' <summary>R-6: the colour closest to the fill, on the way from the fill to the ink in OKLab
    ''' lightness (the fill's hue and chroma kept), that reads at <paramref name="target"/> against
    ''' every fill given. Never further than the ink itself.</summary>
    Private Shared Function SolveDerivedInk(fills As Color(), ink As Color, target As Double) As Color
        Dim from As Double() = OkLch(fills(0))
        Dim inkL As Double = OkLch(ink)(0)
        Dim direction As Double = If(inkL >= from(0), PanelColourMetrics.SolveStep, -PanelColourMetrics.SolveStep)
        Dim l As Double = from(0)
        Do
            l += direction
            If (direction > 0 AndAlso l >= inkL) OrElse (direction < 0 AndAlso l <= inkL) Then Return ink
            Dim candidate As Color = FromOkLch(l, from(1), from(2))
            Dim worst As Double = Double.MaxValue
            For Each f As Color In fills
                worst = Math.Min(worst, ContrastRatio(candidate, f))
            Next
            If worst >= target Then Return candidate
        Loop
    End Function

    ''' <summary>R-7 status inks: the palette role moved toward the panel ink until it reads at the
    ''' text floor over any content once the scrim brings the backdrop to the text opacity.</summary>
    Private Shared Function SolveStatusInk(role As Color, ink As Color, fill As Color, textOpacity As Double) As Color
        Dim target As Double = PanelColourMetrics.TextFloor + PanelColourMetrics.FloorMargin
        Dim candidate As Color = Opaque(role)
        Dim share As Single = 0.0F
        While WorstCaseContrast(fill, candidate, textOpacity) < target AndAlso share < 1.0F
            share = Math.Min(1.0F, share + PanelColourMetrics.StatusInkStep)
            candidate = Blend(Opaque(role), ink, share)
        End While
        Return candidate
    End Function

    ' --- translucency ---------------------------------------------------------------------------

    ''' <summary>R-8: the lowest contrast of an opaque ink over the fill composited at
    ''' <paramref name="opacity"/> over any content. The backdrop's luminance lies between the fill
    ''' over black and the fill over white and the contrast is V-shaped in it, so the two endpoints
    ''' decide.</summary>
    Public Shared Function WorstCaseContrast(fill As Color, ink As Color, opacity As Double) As Double
        Return Math.Min(ContrastRatio(ink, Composite(fill, opacity, Color.Black)),
                        ContrastRatio(ink, Composite(fill, opacity, Color.White)))
    End Function

    ''' <summary>R-9: the smallest opacity (a whole byte, as a layered window composes) at which the
    ''' ink reads at <paramref name="floor"/> over any content. 1 when even opaque does not.</summary>
    Public Shared Function OpacityNeeded(fill As Color, ink As Color, floor As Double) As Double
        If WorstCaseContrast(fill, ink, 0.0R) >= floor Then Return 0.0R
        If WorstCaseContrast(fill, ink, 1.0R) < floor Then Return 1.0R
        Dim lo As Integer = 0
        Dim hi As Integer = 255
        While hi - lo > 1
            Dim mid As Integer = (lo + hi) \ 2
            If WorstCaseContrast(fill, ink, mid / 255.0R) >= floor Then hi = mid Else lo = mid
        End While
        Return hi / 255.0R
    End Function

    ''' <summary>R-10: the scrim alpha that brings the effective opacity under an ink to what the pair
    ''' needs - the smallest byte alpha that holds the floor once the scrim is composed over the panel
    ''' at the set opacity; 0 when the set opacity already holds it.</summary>
    Public Shared Function ScrimAlpha(fill As Color, ink As Color, floor As Double, setOpacity As Double) As Double
        Dim p As Double = Clamp01(setOpacity)
        If WorstCaseContrast(fill, ink, p) >= floor Then Return 0.0R
        If ScrimmedWorstCase(fill, ink, p, 1.0R) < floor Then Return 1.0R
        Dim lo As Integer = 0
        Dim hi As Integer = 255
        While hi - lo > 1
            Dim mid As Integer = (lo + hi) \ 2
            If ScrimmedWorstCase(fill, ink, p, mid / 255.0R) >= floor Then hi = mid Else lo = mid
        End While
        Return hi / 255.0R
    End Function

    ''' <summary>The closed form of R-10: s = 1 - (1 - p_needed) / (1 - p) below the needed opacity,
    ''' 0 at or above it.</summary>
    Public Shared Function ScrimAlphaFrom(opacityNeeded As Double, setOpacity As Double) As Double
        Dim p As Double = Clamp01(setOpacity)
        If p >= opacityNeeded Then Return 0.0R
        Return Clamp01(1.0R - (1.0R - opacityNeeded) / (1.0R - p))
    End Function

    ''' <summary>The worst-case contrast of an ink drawn over a scrim of the fill at
    ''' <paramref name="scrim"/>, itself over the panel at <paramref name="setOpacity"/> over black and
    ''' over white content: the two layered windows composed in turn.</summary>
    Public Shared Function ScrimmedWorstCase(fill As Color, ink As Color, setOpacity As Double, scrim As Double) As Double
        Dim overBlack As Color = Composite(fill, scrim, Composite(fill, setOpacity, Color.Black))
        Dim overWhite As Color = Composite(fill, scrim, Composite(fill, setOpacity, Color.White))
        Return Math.Min(ContrastRatio(ink, overBlack), ContrastRatio(ink, overWhite))
    End Function

    ''' <summary>R-11: the smallest window opacity at which a surface whose ink fades with it keeps the
    ''' floor between ink and fill over the grey ramp and the primaries.</summary>
    Public Shared Function WholeWindowFloor(fill As Color, ink As Color, floor As Double) As Double
        If WholeWindowWorst(fill, ink, 1.0R) < floor Then Return 1.0R
        Dim lo As Integer = 0
        Dim hi As Integer = 255
        If WholeWindowWorst(fill, ink, 0.0R) >= floor Then Return 0.0R
        While hi - lo > 1
            Dim mid As Integer = (lo + hi) \ 2
            If WholeWindowWorst(fill, ink, mid / 255.0R) >= floor Then hi = mid Else lo = mid
        End While
        Return hi / 255.0R
    End Function

    ''' <summary>The lowest contrast between ink and fill when both fade with the window at
    ''' <paramref name="opacity"/> over the floor content.</summary>
    Public Shared Function WholeWindowWorst(fill As Color, ink As Color, opacity As Double) As Double
        Dim worst As Double = Double.MaxValue
        For Each content As Color In FloorContent
            worst = Math.Min(worst, ContrastRatio(Composite(ink, opacity, content), Composite(fill, opacity, content)))
        Next
        Return worst
    End Function

    ''' <summary>A colour at a constant opacity over another, the way a layered window composes: the
    ''' opacity truncated to a byte, the sRGB values blended.</summary>
    Public Shared Function Composite(top As Color, opacity As Double, under As Color) As Color
        Dim a As Integer = CInt(Math.Floor(Clamp01(opacity) * 255.0R + 0.000001R))
        Dim t As Double = a / 255.0R
        Return Color.FromArgb(255, Channel(top.R * t + under.R * (1 - t)),
                              Channel(top.G * t + under.G * (1 - t)), Channel(top.B * t + under.B * (1 - t)))
    End Function

    ' --- colour arithmetic ----------------------------------------------------------------------

    ''' <summary>WCAG relative luminance of an sRGB colour (the same curve as ChromeTheme's).</summary>
    Public Shared Function RelativeLuminance(c As Color) As Double
        Return 0.2126R * LinearLut(c.R) + 0.7152R * LinearLut(c.G) + 0.0722R * LinearLut(c.B)
    End Function

    ''' <summary>WCAG contrast ratio, 1 to 21.</summary>
    Public Shared Function ContrastRatio(a As Color, b As Color) As Double
        Dim la As Double = RelativeLuminance(a)
        Dim lb As Double = RelativeLuminance(b)
        Return (Math.Max(la, lb) + 0.05R) / (Math.Min(la, lb) + 0.05R)
    End Function

    ''' <summary>OKLCH of an sRGB colour: { L 0..1, C, h degrees 0..360 }.</summary>
    Public Shared Function ToOkLch(c As Color) As Double()
        Return OkLch(c)
    End Function

    Private Shared Function OkLch(c As Color) As Double()
        Dim lab As Double() = OkLab(c)
        Dim h As Double = Math.Atan2(lab(2), lab(1)) * 180.0R / Math.PI
        If h < 0 Then h += 360.0R
        Return {lab(0), Math.Sqrt(lab(1) * lab(1) + lab(2) * lab(2)), h}
    End Function

    Private Shared Function OkLab(c As Color) As Double()
        Dim r As Double = SrgbToLinear(c.R / 255.0R)
        Dim g As Double = SrgbToLinear(c.G / 255.0R)
        Dim b As Double = SrgbToLinear(c.B / 255.0R)
        Dim l As Double = Math.Cbrt(0.4122214708R * r + 0.5363325363R * g + 0.0514459929R * b)
        Dim m As Double = Math.Cbrt(0.2119034982R * r + 0.6806995451R * g + 0.1073969566R * b)
        Dim s As Double = Math.Cbrt(0.0883024619R * r + 0.2817188376R * g + 0.6299787005R * b)
        Return {0.2104542553R * l + 0.793617785R * m - 0.0040720468R * s,
                1.9779984951R * l - 2.428592205R * m + 0.4505937099R * s,
                0.0259040371R * l + 0.7827717662R * m - 0.808675766R * s}
    End Function

    ''' <summary>An sRGB colour from OKLCH; out of gamut the chroma is reduced, never the lightness or
    ''' the hue (R-3).</summary>
    Public Shared Function FromOkLch(l As Double, c As Double, hueDegrees As Double) As Color
        l = Clamp01(l)
        Dim linear As Double() = OkLchToLinear(l, c, hueDegrees)
        If Not InGamut(linear) Then
            Dim lo As Double = 0.0R
            Dim hi As Double = c
            For i As Integer = 0 To 24
                Dim mid As Double = (lo + hi) / 2.0R
                If InGamut(OkLchToLinear(l, mid, hueDegrees)) Then lo = mid Else hi = mid
            Next
            linear = OkLchToLinear(l, lo, hueDegrees)
        End If
        Return Color.FromArgb(255, Channel(LinearToSrgb(Clamp01(linear(0))) * 255.0R),
                              Channel(LinearToSrgb(Clamp01(linear(1))) * 255.0R),
                              Channel(LinearToSrgb(Clamp01(linear(2))) * 255.0R))
    End Function

    Private Shared Function OkLchToLinear(l As Double, c As Double, hueDegrees As Double) As Double()
        Dim a As Double = c * Math.Cos(hueDegrees * Math.PI / 180.0R)
        Dim b As Double = c * Math.Sin(hueDegrees * Math.PI / 180.0R)
        Dim l_ As Double = l + 0.3963377774R * a + 0.2158037573R * b
        Dim m_ As Double = l - 0.1055613458R * a - 0.0638541728R * b
        Dim s_ As Double = l - 0.0894841775R * a - 1.291485548R * b
        l_ = l_ * l_ * l_
        m_ = m_ * m_ * m_
        s_ = s_ * s_ * s_
        Return {4.0767416621R * l_ - 3.3077115913R * m_ + 0.2309699292R * s_,
                -1.2684380046R * l_ + 2.6097574011R * m_ - 0.3413193965R * s_,
                -0.0041960863R * l_ - 0.7034186147R * m_ + 1.707614701R * s_}
    End Function

    Private Shared Function InGamut(linear As Double()) As Boolean
        Const Tolerance As Double = 0.000001R
        For Each v As Double In linear
            If v < -Tolerance OrElse v > 1.0R + Tolerance Then Return False
        Next
        Return True
    End Function

    ''' <summary>a + (b - a) * t per channel, rounded and clamped; opaque.</summary>
    Public Shared Function Blend(a As Color, b As Color, t As Single) As Color
        Dim share As Double = Clamp01(t)
        Return Color.FromArgb(255, Channel(a.R + (CInt(b.R) - CInt(a.R)) * share),
                              Channel(a.G + (CInt(b.G) - CInt(a.G)) * share), Channel(a.B + (CInt(b.B) - CInt(a.B)) * share))
    End Function

    Private Shared Function SrgbToLinear(v As Double) As Double
        Return If(v <= 0.04045R, v / 12.92R, Math.Pow((v + 0.055R) / 1.055R, 2.4R))
    End Function

    Private Shared Function LinearToSrgb(v As Double) As Double
        Return If(v <= 0.0031308R, 12.92R * v, 1.055R * Math.Pow(v, 1.0R / 2.4R) - 0.055R)
    End Function

    Private Shared Function Clamp01(v As Double) As Double
        If Double.IsNaN(v) OrElse v < 0.0R Then Return 0.0R
        If v > 1.0R Then Return 1.0R
        Return v
    End Function

    Private Shared Function Channel(value As Double) As Integer
        If value <= 0.0R Then Return 0
        If value >= 255.0R Then Return 255
        Return CInt(Math.Round(value, MidpointRounding.AwayFromZero))
    End Function

    Private Shared Function Opaque(c As Color) As Color
        Return Color.FromArgb(255, c.R, c.G, c.B)
    End Function

    Private Shared Function BuildLinearLut() As Double()
        Dim lut(255) As Double
        For i As Integer = 0 To 255
            Dim v As Double = i / 255.0R
            lut(i) = If(v <= 0.03928R, v / 12.92R, Math.Pow((v + 0.055R) / 1.055R, 2.4R))
        Next
        Return lut
    End Function

    Private Shared Function BuildFloorContent() As Color()
        Dim list As New List(Of Color)()
        For v As Integer = 0 To 255
            list.Add(Color.FromArgb(255, v, v, v))
        Next
        For Each rgb As Integer() In {New Integer() {255, 0, 0}, New Integer() {0, 255, 0}, New Integer() {0, 0, 255},
                                      New Integer() {255, 255, 0}, New Integer() {0, 255, 255}, New Integer() {255, 0, 255}}
            list.Add(Color.FromArgb(255, rgb(0), rgb(1), rgb(2)))
        Next
        Return list.ToArray()
    End Function

End Class
#End If
