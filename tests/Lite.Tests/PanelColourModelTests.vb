#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

''' <summary>
''' L0303 section 7.1: the panel colour model holds its floors over the whole sampled sRGB grid, the
''' two fixed schemes and every set opacity, and the opacity setting never changes an ink. Pure
''' arithmetic; no window, no timing.
''' </summary>
Public Class PanelColourModelTests

    Private Const DefaultAccentArgb As Integer = &HFF0078D4

    ' Windows 10/11 personalization accent swatches (48) and the Windows 11 default.
    Private Shared ReadOnly AccentHex As String() = {
        "FFB900", "FF8C00", "F7630C", "CA5010", "DA3B01", "EF6950", "D13438", "FF4343",
        "E74856", "E81123", "EA005E", "C30052", "E3008C", "BF0077", "C239B3", "9A0089",
        "0078D7", "0063B1", "8E8CD8", "6B69D6", "8764B8", "744DA9", "B146C2", "881798",
        "0099BC", "2D7D9A", "00B7C3", "038387", "00B294", "018574", "00CC6A", "10893E",
        "7A7574", "5D5A58", "68768A", "515C6B", "567C73", "486860", "498205", "107C10",
        "767676", "4C4A48", "69797E", "4A5459", "647C64", "525E54", "847545", "7E735F", "0078D4"}

    ' 9 greys, the 24 ColorChecker patches and 12 vivid colours (the baseline's 45-colour corpus).
    Private Shared ReadOnly Corpus As Integer()() = {
        New Integer() {0, 0, 0}, New Integer() {32, 32, 32}, New Integer() {64, 64, 64}, New Integer() {96, 96, 96},
        New Integer() {128, 128, 128}, New Integer() {160, 160, 160}, New Integer() {192, 192, 192},
        New Integer() {224, 224, 224}, New Integer() {255, 255, 255},
        New Integer() {115, 82, 68}, New Integer() {194, 150, 130}, New Integer() {98, 122, 157}, New Integer() {87, 108, 67},
        New Integer() {133, 128, 177}, New Integer() {103, 189, 170}, New Integer() {214, 126, 44}, New Integer() {80, 91, 166},
        New Integer() {193, 90, 99}, New Integer() {94, 60, 108}, New Integer() {157, 188, 64}, New Integer() {224, 163, 46},
        New Integer() {56, 61, 150}, New Integer() {70, 148, 73}, New Integer() {175, 54, 60}, New Integer() {231, 199, 31},
        New Integer() {187, 86, 149}, New Integer() {8, 133, 161}, New Integer() {243, 243, 242}, New Integer() {200, 200, 200},
        New Integer() {160, 160, 160}, New Integer() {122, 122, 121}, New Integer() {85, 85, 85}, New Integer() {52, 52, 52},
        New Integer() {255, 0, 0}, New Integer() {0, 255, 0}, New Integer() {0, 0, 255}, New Integer() {255, 255, 0},
        New Integer() {0, 255, 255}, New Integer() {255, 0, 255}, New Integer() {255, 128, 0}, New Integer() {0, 200, 0},
        New Integer() {0, 190, 190}, New Integer() {190, 0, 190}, New Integer() {0, 205, 90}, New Integer() {255, 0, 128}}

    Private Shared Function Rgb(r As Integer, g As Integer, b As Integer) As Color
        Return Color.FromArgb(255, r, g, b)
    End Function

    Private Shared Function Hex(h As String) As Color
        Return Rgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16))
    End Function

    Private Shared ReadOnly DefaultAccent As Color = Color.FromArgb(DefaultAccentArgb)

    ''' <summary>The 17-level sRGB grid (4,913 colours).</summary>
    Private Shared Iterator Function Grid() As IEnumerable(Of Color)
        For r As Integer = 0 To 16
            For g As Integer = 0 To 16
                For b As Integer = 0 To 16
                    Yield Rgb(Math.Min(255, r * 16), Math.Min(255, g * 16), Math.Min(255, b * 16))
                Next
            Next
        Next
    End Function

    ''' <summary>Every model the opaque checks cover: the grid on a dynamic scheme plus the black and
    ''' white schemes.</summary>
    Private Shared Iterator Function OpaqueModels() As IEnumerable(Of PanelColours)
        For Each sample As Color In Grid()
            Yield PanelColourModel.ForSample(sample, 0, Nothing, 100, DefaultAccent, False)
        Next
        Yield PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing, 100, DefaultAccent, False)
        Yield PanelColourModel.ForSample(Color.White, PanelColourMetrics.WhiteScheme, Nothing, 100, DefaultAccent, False)
    End Function

    Private Shared Function Cr(a As Color, b As Color) As Double
        Return PanelColourModel.ContrastRatio(a, b)
    End Function

    Private Shared Function HueGap(a As Double, b As Double) As Double
        Dim d As Double = Math.Abs(a - b) Mod 360.0R
        Return If(d > 180.0R, 360.0R - d, d)
    End Function

    <Fact>
    Public Sub Worst_case_includes_content_between_black_and_white()
        Dim ink As Color = Rgb(118, 118, 118)
        Assert.True(Cr(ink, Color.Black) >= 4.5R)
        Assert.True(Cr(ink, Color.White) >= 4.5R)
        Assert.Equal(1.0R, Cr(ink, ink))
        Assert.Equal(1.0R, PanelColourModel.WorstCaseContrast(Color.Black, ink, 0.0R))
        Assert.Equal(1.0R, PanelColourModel.WorstCaseContrast(Color.Black, ink, 0.5R))
        Assert.Equal(1.0R, PanelColourModel.ScrimmedWorstCase(Color.Black, ink, 0.0R, 0.0R))
        Assert.Equal(1.0R, PanelColourModel.ScrimmedWorstCase(Color.Black, ink, 0.2R, 0.2R))
        Assert.True(PanelColourModel.OpacityNeeded(Color.Black, ink, 4.5R) > 0.0R)
    End Sub

    <Fact>
    Public Sub Ink_is_the_wcag_better_of_the_tone_inks_for_every_sample()
        For Each m As PanelColours In OpaqueModels()
            Assert.Equal(PanelColourModel.BetterInk(m.Fill).ToArgb(), m.Ink.ToArgb())
            Assert.True(Cr(m.Ink, m.Fill) >= 9.0R, $"ink {m.Ink} on {m.Fill}: {Cr(m.Ink, m.Fill):0.00}")
        Next
        ' Today's named failures of the luma-140 rule (L0303 section 5.1): each gets an ink that reads.
        For Each sample As Color In {Rgb(0, 238, 2), Rgb(0, 205, 90), Rgb(148, 132, 119), Rgb(0, 200, 0), Rgb(0, 190, 190), Rgb(255, 0, 255)}
            Dim m As PanelColours = PanelColourModel.ForSample(sample, 0, Nothing, 100, DefaultAccent, False)
            Assert.True(Cr(m.Ink, m.Fill) >= 9.0R, $"{sample}: {Cr(m.Ink, m.Fill):0.00}")
        Next
    End Sub

    <Fact>
    Public Sub Fill_is_a_tint_of_the_picture()
        Dim worstHue As Double = 0.0R
        For Each sample As Color In Grid()
            Dim m As PanelColours = PanelColourModel.ForSample(sample, 0, Nothing, 100, DefaultAccent, False)
            Dim fillLch As Double() = PanelColourModel.ToOkLch(m.Fill)
            Dim sampleLch As Double() = PanelColourModel.ToOkLch(sample)
            Dim dark As Boolean = m.Tone = PanelTone.Dark
            Dim cap As Double = If(dark, PanelColourMetrics.DarkChromaCap, PanelColourMetrics.LightChromaCap)
            ' One byte of rounding moves the chroma by at most a few thousandths.
            Assert.True(fillLch(1) <= cap + 0.003R, $"{sample} -> {m.Fill}: chroma {fillLch(1):0.0000}")
            Assert.InRange(fillLch(0), If(dark, PanelColourMetrics.DarkFillL, PanelColourMetrics.LightFillL) - 0.01R,
                           If(dark, PanelColourMetrics.DarkFillL, PanelColourMetrics.LightFillL) + 0.01R)
            If sampleLch(1) >= 0.03R Then
                Dim gap As Double = HueGap(sampleLch(2), fillLch(2))
                worstHue = Math.Max(worstHue, gap)
                Assert.True(gap <= 8.0R, $"{sample} -> {m.Fill}: hue moved {gap:0.0} degrees")
            End If
        Next
        Assert.True(worstHue > 0.0R)
    End Sub

    <Fact>
    Public Sub Black_and_white_schemes_keep_their_tone()
        For Each sample As Color In {Color.White, Color.Black, Rgb(128, 128, 128), Rgb(255, 255, 0)}
            Assert.Equal(PanelTone.Dark, PanelColourModel.ForSample(sample, PanelColourMetrics.BlackScheme, PanelTone.Light, 86, DefaultAccent, False).Tone)
            Assert.Equal(PanelTone.Light, PanelColourModel.ForSample(sample, PanelColourMetrics.WhiteScheme, PanelTone.Dark, 86, DefaultAccent, False).Tone)
        Next
        Dim black As PanelColours = PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing, 86, DefaultAccent, False)
        Dim white As PanelColours = PanelColourModel.ForSample(Color.White, PanelColourMetrics.WhiteScheme, Nothing, 86, DefaultAccent, False)
        Assert.Equal(Rgb(32, 32, 32).ToArgb(), black.Fill.ToArgb())
        Assert.Equal(Rgb(240, 240, 240).ToArgb(), white.Fill.ToArgb())
        Assert.Equal(PanelColourMetrics.InkOnDark.ToArgb(), black.Ink.ToArgb())
        Assert.Equal(PanelColourMetrics.InkOnLight.ToArgb(), white.Ink.ToArgb())
        ' An empty surface on a dynamic scheme takes the tone of the neutral it is painted.
        Assert.Equal(PanelTone.Dark, PanelColourModel.ForSample(Rgb(32, 32, 32), 0, Nothing, 86, DefaultAccent, False).Tone)
    End Sub

    <Fact>
    Public Sub Tone_does_not_toggle_inside_the_hysteresis_band()
        Dim half As Double = PanelColourMetrics.ToneHysteresis / 2.0R
        Dim inside As New List(Of Color)()
        Dim below As Color = Color.Empty
        Dim above As Color = Color.Empty
        For v As Integer = 150 To 255
            Dim c As Color = Rgb(v, v, v)
            Dim l As Double = PanelColourModel.ToOkLch(c)(0)
            If l > PanelColourMetrics.LightToneFrom - half + 0.002R AndAlso l < PanelColourMetrics.LightToneFrom + half - 0.002R Then inside.Add(c)
            If l < PanelColourMetrics.LightToneFrom - half - 0.005R Then below = c
            If above.IsEmpty AndAlso l > PanelColourMetrics.LightToneFrom + half + 0.005R Then above = c
        Next
        Assert.True(inside.Count >= 3)
        Dim tone As PanelTone? = PanelTone.Dark
        For Each c As Color In inside
            tone = PanelColourModel.ToneFor(c, 0, tone)
            Assert.Equal(PanelTone.Dark, tone.Value)
        Next
        tone = PanelTone.Light
        For Each c As Color In inside
            tone = PanelColourModel.ToneFor(c, 0, tone)
            Assert.Equal(PanelTone.Light, tone.Value)
        Next
        Assert.Equal(PanelTone.Light, PanelColourModel.ToneFor(above, 0, PanelTone.Dark))
        Assert.Equal(PanelTone.Dark, PanelColourModel.ToneFor(below, 0, PanelTone.Light))
        ' Without a previous tone the threshold alone decides.
        Assert.Equal(PanelTone.Light, PanelColourModel.ToneFor(Rgb(250, 250, 250), 0, Nothing))
        Assert.Equal(PanelTone.Dark, PanelColourModel.ToneFor(Rgb(150, 150, 150), 0, Nothing))
    End Sub

    <Fact>
    Public Sub Every_state_reads_on_an_opaque_panel()
        Dim text As Double = PanelColourMetrics.TextFloor
        For Each m As PanelColours In OpaqueModels()
            Dim where As String = $"fill {m.Fill}"
            For Each f As Color In {m.Fill, m.Hover, m.Pressed}
                Assert.True(Cr(m.Ink, f) >= text, $"{where}: ink on {f} {Cr(m.Ink, f):0.00}")
                Assert.True(Cr(m.WarningInk, f) >= text, $"{where}: warning on {f} {Cr(m.WarningInk, f):0.00}")
                Assert.True(Cr(m.ErrorInk, f) >= text, $"{where}: error on {f} {Cr(m.ErrorInk, f):0.00}")
            Next
            For Each f As Color In {m.Checked, m.CheckedHover, m.CheckedPressed}
                Assert.True(Cr(m.CheckedInk, f) >= text, $"{where}: checked ink on {f} {Cr(m.CheckedInk, f):0.00}")
            Next
            For Each f As Color In {m.Destructive, m.DestructiveHover, m.DestructivePressed}
                Assert.True(Cr(m.DestructiveInk, f) >= text, $"{where}: destructive ink on {f} {Cr(m.DestructiveInk, f):0.00}")
            Next
            For Each f As Color In {m.Fill, m.Hover}
                Assert.True(Cr(m.InkDisabled, f) >= PanelColourMetrics.DisabledContrast, $"{where}: disabled on {f}")
                Assert.True(Cr(m.PinRestInk, f) >= PanelColourMetrics.PinRestContrast, $"{where}: pin at rest on {f}")
                Assert.True(Cr(m.PinPinnedInk, f) >= PanelColourMetrics.PinPinnedContrast, $"{where}: pinned on {f}")
            Next
            Assert.True(Cr(m.Hairline, m.Fill) >= PanelColourMetrics.HairlineContrast, $"{where}: hairline")
            Assert.True(Cr(m.Fill, m.Hover) >= PanelColourMetrics.StateStepFloor, $"{where}: rest to hover {Cr(m.Fill, m.Hover):0.000}")
            ' The disabled ink is quieter than the ink: it sits between the fill and the ink.
            Assert.True(Cr(m.InkDisabled, m.Fill) < Cr(m.Ink, m.Fill))
        Next
    End Sub

    <Fact>
    Public Sub Ink_reads_over_any_content_at_every_set_opacity()
        Dim samples As New List(Of PanelColours)()
        For Each rgbRow As Integer() In Corpus
            For percent As Integer = 0 To 100
                samples.Add(PanelColourModel.ForSample(Rgb(rgbRow(0), rgbRow(1), rgbRow(2)), 0, Nothing, percent, DefaultAccent, False))
            Next
        Next
        For percent As Integer = 0 To 100
            samples.Add(PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing, percent, DefaultAccent, False))
            samples.Add(PanelColourModel.ForSample(Color.White, PanelColourMetrics.WhiteScheme, Nothing, percent, DefaultAccent, False))
        Next
        For Each m As PanelColours In samples
            Dim pairs As New List(Of Tuple(Of Color, Color))()
            For Each f As Color In {m.Fill, m.Hover, m.Pressed}
                pairs.Add(Tuple.Create(f, m.Ink))
            Next
            pairs.Add(Tuple.Create(m.Fill, m.WarningInk))
            pairs.Add(Tuple.Create(m.Fill, m.ErrorInk))
            For Each f As Color In {m.Checked, m.CheckedHover, m.CheckedPressed}
                pairs.Add(Tuple.Create(f, m.CheckedInk))
            Next
            For Each f As Color In {m.Destructive, m.DestructiveHover, m.DestructivePressed}
                pairs.Add(Tuple.Create(f, m.DestructiveInk))
            Next
            For Each pair As Tuple(Of Color, Color) In pairs
                Dim sText As Double = m.ScrimAlphaFor(pair.Item1, pair.Item2, True)
                Dim sGlyph As Double = m.ScrimAlphaFor(pair.Item1, pair.Item2, False)
                Dim cText As Double = PanelColourModel.ScrimmedWorstCase(pair.Item1, pair.Item2, m.SetOpacity, sText)
                Dim cGlyph As Double = PanelColourModel.ScrimmedWorstCase(pair.Item1, pair.Item2, m.SetOpacity, sGlyph)
                Assert.True(cText >= PanelColourMetrics.TextFloor, $"{pair.Item2} on {pair.Item1} at {m.SetOpacity:0.00}: {cText:0.00}")
                Assert.True(cGlyph >= PanelColourMetrics.GlyphFloor, $"{pair.Item2} on {pair.Item1} at {m.SetOpacity:0.00}: {cGlyph:0.00}")
                Assert.True(sGlyph <= sText)
            Next
        Next
    End Sub

    <Fact>
    Public Sub Scrim_is_zero_at_or_above_the_needed_opacity_and_exact_below()
        For Each rgbRow As Integer() In Corpus
            For percent As Integer = 0 To 100
                Dim m As PanelColours = PanelColourModel.ForSample(Rgb(rgbRow(0), rgbRow(1), rgbRow(2)), 0, Nothing, percent, DefaultAccent, False)
                Dim needed As Double = PanelColourModel.OpacityNeeded(m.Fill, m.Ink, PanelColourMetrics.TextFloor)
                Dim s As Double = m.ScrimAlphaFor(m.Fill, m.Ink, True)
                If m.SetOpacity >= needed Then
                    Assert.Equal(0.0R, s)
                Else
                    Dim closed As Double = PanelColourModel.ScrimAlphaFrom(needed, m.SetOpacity)
                    ' Each layered composition rounds its colour channels. Start at the rounded-up
                    ' closed form and use only the extra bytes actually needed to hold the floor.
                    Dim firstByte As Integer = CInt(Math.Ceiling(closed * 255.0R))
                    Dim selectedByte As Integer = CInt(Math.Round(s * 255.0R))
                    Assert.True(selectedByte >= firstByte)
                    Assert.True(PanelColourModel.ScrimmedWorstCase(m.Fill, m.Ink, m.SetOpacity, s) >= PanelColourMetrics.TextFloor)
                    If selectedByte > firstByte Then
                        Assert.True(PanelColourModel.ScrimmedWorstCase(m.Fill, m.Ink, m.SetOpacity, (selectedByte - 1) / 255.0R) < PanelColourMetrics.TextFloor)
                    End If
                End If
            Next
        Next
        ' The tone-wide value covers the worst of rest, hover and pressed, not only rest.
        Dim dark As PanelColours = PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing, 30, DefaultAccent, False)
        Assert.True(dark.TextScrimAlpha > 0.0R)
        Assert.Equal((New Color() {dark.Fill, dark.Hover, dark.Pressed}).Max(Function(f) PanelColourModel.OpacityNeeded(f, dark.Ink, PanelColourMetrics.TextFloor)), dark.TextOpacityNeeded)
        Assert.Equal(PanelColourModel.ScrimAlphaFrom(dark.TextOpacityNeeded, dark.SetOpacity), dark.TextScrimAlpha)
        Assert.Equal(0.0R, PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing, 100, DefaultAccent, False).TextScrimAlpha)
    End Sub

    <Fact>
    Public Sub The_opacity_setting_never_changes_an_ink()
        For Each sample As Color In Grid()
            Dim a As PanelColours = PanelColourModel.ForSample(sample, 0, Nothing, 49, DefaultAccent, False)
            Dim b As PanelColours = PanelColourModel.ForSample(sample, 0, Nothing, 50, DefaultAccent, False)
            Dim c As PanelColours = PanelColourModel.ForSample(sample, 0, Nothing, 0, DefaultAccent, False)
            For Each other As PanelColours In {b, c}
                Assert.Equal(a.Tone, other.Tone)
                Assert.Equal(a.Fill.ToArgb(), other.Fill.ToArgb())
                Assert.Equal(a.Ink.ToArgb(), other.Ink.ToArgb())
                Assert.Equal(a.InkDisabled.ToArgb(), other.InkDisabled.ToArgb())
                Assert.Equal(a.CheckedInk.ToArgb(), other.CheckedInk.ToArgb())
                Assert.Equal(a.DestructiveInk.ToArgb(), other.DestructiveInk.ToArgb())
                Assert.Equal(a.WarningInk.ToArgb(), other.WarningInk.ToArgb())
                Assert.Equal(a.ErrorInk.ToArgb(), other.ErrorInk.ToArgb())
                Assert.Equal(a.PinRestInk.ToArgb(), other.PinRestInk.ToArgb())
            Next
        Next
    End Sub

    <Fact>
    Public Sub Accent_keeps_its_hue_and_reads()
        Dim schemes As Integer() = {PanelColourMetrics.BlackScheme, PanelColourMetrics.WhiteScheme}
        Dim worstSwatchHue As Double = 0.0R
        For Each scheme As Integer In schemes
            For Each h As String In AccentHex
                Dim accent As Color = Hex(h)
                Dim m As PanelColours = PanelColourModel.ForSample(Color.Gray, scheme, Nothing, 86, accent, False)
                For Each f As Color In {m.Checked, m.CheckedHover, m.CheckedPressed}
                    Assert.True(Cr(m.CheckedInk, f) >= PanelColourMetrics.TextFloor, $"#{h} scheme {scheme}: {Cr(m.CheckedInk, f):0.00} on {f}")
                Next
                Dim a As Double() = PanelColourModel.ToOkLch(accent)
                Dim t As Double() = PanelColourModel.ToOkLch(m.Checked)
                Assert.True(t(1) <= PanelColourMetrics.AccentChromaCap + 0.003R, $"#{h}: chroma {t(1):0.000}")
                If a(1) >= 0.03R Then
                    Dim gap As Double = HueGap(a(2), t(2))
                    worstSwatchHue = Math.Max(worstSwatchHue, gap)
                    Assert.True(gap <= 3.0R, $"#{h} scheme {scheme}: hue moved {gap:0.0} degrees")
                End If
                Assert.Equal(m.Checked.ToArgb(), m.FocusRing.ToArgb())
            Next
            ' Any hue at three lightnesses: the floor always holds; the hue holds while the tamed accent
            ' keeps a visible chroma.
            For hue As Integer = 0 To 359
                For Each l As Double In {0.3R, 0.6R, 0.9R}
                    Dim accent As Color = PanelColourModel.FromOkLch(l, 0.15R, hue)
                    Dim m As PanelColours = PanelColourModel.ForSample(Color.Gray, scheme, Nothing, 86, accent, False)
                    For Each f As Color In {m.Checked, m.CheckedHover, m.CheckedPressed}
                        Assert.True(Cr(m.CheckedInk, f) >= PanelColourMetrics.TextFloor, $"hue {hue} L {l}: {Cr(m.CheckedInk, f):0.00}")
                    Next
                    Dim a As Double() = PanelColourModel.ToOkLch(accent)
                    Dim t As Double() = PanelColourModel.ToOkLch(m.Checked)
                    If a(1) >= 0.03R AndAlso t(1) >= 0.03R Then
                        Assert.True(HueGap(a(2), t(2)) <= 5.0R, $"hue {hue} L {l}: moved {HueGap(a(2), t(2)):0.0}")
                    End If
                Next
            Next
        Next
        Assert.True(worstSwatchHue > 0.0R)
    End Sub

    <Fact>
    Public Sub Whole_window_floor_keeps_text_readable()
        Dim models As New List(Of PanelColours) From {
            PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing, 86, DefaultAccent, False),
            PanelColourModel.ForSample(Color.White, PanelColourMetrics.WhiteScheme, Nothing, 86, DefaultAccent, False)}
        For Each rgbRow As Integer() In Corpus
            models.Add(PanelColourModel.ForSample(Rgb(rgbRow(0), rgbRow(1), rgbRow(2)), 0, Nothing, 86, DefaultAccent, False))
        Next
        For Each m As PanelColours In models
            Assert.InRange(m.WholeWindowFloor, 0.3R, 0.75R)
            For step_ As Integer = CInt(Math.Ceiling(m.WholeWindowFloor * 255.0R)) To 255 Step 5
                Dim w As Double = step_ / 255.0R
                For Each f As Color In {m.Fill, m.Hover, m.Pressed}
                    Assert.True(PanelColourModel.WholeWindowWorst(f, m.Ink, w) >= PanelColourMetrics.TextFloor, $"{f} at {w:0.000}")
                    For Each rgbRow As Integer() In Corpus
                        Dim content As Color = Rgb(rgbRow(0), rgbRow(1), rgbRow(2))
                        Dim c As Double = Cr(PanelColourModel.Composite(m.Ink, w, content), PanelColourModel.Composite(f, w, content))
                        Assert.True(c >= PanelColourMetrics.TextFloor, $"{f} at {w:0.000} over {content}: {c:0.00}")
                    Next
                Next
            Next
        Next
    End Sub

    ''' <summary>L0303 S-4 (R-11 on the transport bar): the sliders' track holds the glyph floor against the bar's
    ''' fill when both fade with the window at its floor, and the A-B chip's caption holds the text floor at the
    ''' chip's own floor, for the corpus, both fixed schemes and every Windows accent swatch.</summary>
    <Fact>
    Public Sub Transport_track_and_chip_read_at_their_window_floors()
        Dim samples As New List(Of KeyValuePair(Of Color, Integer)) From {
            New KeyValuePair(Of Color, Integer)(Color.Black, PanelColourMetrics.BlackScheme),
            New KeyValuePair(Of Color, Integer)(Color.White, PanelColourMetrics.WhiteScheme)}
        For Each rgbRow As Integer() In Corpus
            samples.Add(New KeyValuePair(Of Color, Integer)(Rgb(rgbRow(0), rgbRow(1), rgbRow(2)), 0))
        Next
        For Each sample As KeyValuePair(Of Color, Integer) In samples
            For Each swatch As String In AccentHex
                Dim m As PanelColours = PanelColourModel.ForSample(sample.Key, sample.Value, Nothing, 0, Hex(swatch), False)
                Assert.True(Cr(m.SliderTrack, m.Fill) >= PanelColourMetrics.GlyphFloor, $"track {m.SliderTrack} on {m.Fill}")
                Assert.True(PanelColourModel.WholeWindowWorst(m.Fill, m.SliderTrack, m.WholeWindowFloor) >= PanelColourMetrics.GlyphFloor,
                            $"track {m.SliderTrack} on {m.Fill} at {m.WholeWindowFloor:0.000}")
                ' The track is a mark between the fill and the thumb (the ink), never the ink itself on a dark panel.
                Assert.True(Cr(m.SliderTrack, m.Fill) <= Cr(m.Ink, m.Fill) + 0.000001R)
                Assert.InRange(m.ChipFloor, 0.0R, 1.0R)
                Assert.True(PanelColourModel.WholeWindowWorst(m.Checked, m.CheckedInk, m.ChipFloor) >= PanelColourMetrics.TextFloor,
                            $"chip {m.CheckedInk} on {m.Checked} at {m.ChipFloor:0.000}")
            Next
        Next
        Dim hc As PanelColours = PanelColourModel.ForSample(Rgb(0, 205, 90), 0, Nothing, 30, DefaultAccent, True)
        Assert.Equal(hc.Ink.ToArgb(), hc.SliderTrack.ToArgb())
        Assert.Equal(0.0R, hc.ChipFloor)
    End Sub

    <Fact>
    Public Sub High_contrast_is_untouched()
        Dim m As PanelColours = PanelColourModel.ForSample(Rgb(0, 205, 90), 0, Nothing, 30, DefaultAccent, True)
        Assert.True(m.IsHighContrast)
        Assert.Equal(SystemColors.Window.ToArgb(), m.Fill.ToArgb())
        Assert.Equal(SystemColors.WindowText.ToArgb(), m.Ink.ToArgb())
        Assert.Equal(SystemColors.Highlight.ToArgb(), m.Checked.ToArgb())
        Assert.Equal(0.0R, m.TextScrimAlpha)
        Assert.Equal(0.0R, m.ScrimAlphaFor(m.Fill, m.Ink, True))
        Assert.Equal(0.0R, m.WholeWindowFloor)
    End Sub

    <Fact>
    Public Sub The_inks_are_the_palette_text_roles()
        Assert.Equal(ThemePalette.Dark.TextPrimary.ToArgb(), PanelColourMetrics.InkOnDark.ToArgb())
        Assert.Equal(Rgb(32, 32, 32).ToArgb(), ThemePalette.Dark.SurfaceWindow.ToArgb())
        Assert.Equal(PanelColourModel.ToOkLch(ThemePalette.Dark.SurfaceWindow)(0), PanelColourMetrics.DarkFillL, 3)
        Assert.Equal(PanelColourModel.ToOkLch(Rgb(240, 240, 240))(0), PanelColourMetrics.LightFillL, 3)
        Assert.Equal(ThemePalette.Dark.Warning.ToArgb(), PanelColourMetrics.WarningOnDark.ToArgb())
        Assert.Equal(ThemePalette.Dark.Danger.ToArgb(), PanelColourMetrics.ErrorOnDark.ToArgb())
    End Sub

    ''' <summary>R-14 caches the result and hands one instance to every caller, so no caller may change it.</summary>
    <Fact>
    Public Sub The_cached_result_is_immutable()
        For Each p As Reflection.PropertyInfo In GetType(PanelColours).GetProperties(
                Reflection.BindingFlags.Public Or Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
            Assert.True(p.SetMethod Is Nothing, p.Name & " has a setter")
        Next
        Dim accent As Color = Color.FromArgb(DefaultAccentArgb)
        Dim first As PanelColours = PanelColourModel.ForSample(Rgb(0, 205, 90), 0, Nothing, 86, accent, False)
        Assert.Same(first, PanelColourModel.ForSample(Rgb(0, 205, 90), 0, Nothing, 86, accent, False))
    End Sub

    <Fact>
    Public Sub Recipient_rows_read_at_every_overlay_setting()
        ' L0303 R-12: the recipients table's rows on the model - identity fills blended toward the table's own fill
        ' in the panels' tone, the copy zone on the state fills, the delete row on the destructive family, the strip
        ' glyphs on the pin inks - read over any picture at every opacity the setting offers, through the scrim
        ' the ink layer lays (R-10); an unusable row's solved ink keeps the disabled floor on its own fills (R-6).
        Dim samples As New List(Of Tuple(Of Color, Integer)) From {
            Tuple.Create(Color.Black, PanelColourMetrics.BlackScheme),
            Tuple.Create(Color.White, PanelColourMetrics.WhiteScheme),
            Tuple.Create(Rgb(32, 32, 32), 0),
            Tuple.Create(Rgb(0, 205, 90), 0),
            Tuple.Create(Rgb(148, 132, 119), 0),
            Tuple.Create(Rgb(240, 236, 220), 0)}
        Dim percents As New List(Of Integer)
        For percent As Integer = 40 To 100 Step 6
            percents.Add(percent)
        Next
        percents.Add(88)
        percents.Add(100)
        Const slack As Double = 0.01R
        For Each s As Tuple(Of Color, Integer) In samples
            For Each percent As Integer In percents
                Dim m As PanelColours = PanelColourModel.ForSample(s.Item1, s.Item2, Nothing, percent, DefaultAccent, False)
                Dim dark As Boolean = (m.Tone = PanelTone.Dark)
                Dim p As Double = m.SetOpacity
                Dim pairs As New List(Of Tuple(Of String, Color, Color, Double))
                For slot As Integer = 0 To 10
                    Dim rest As Color = RecipientPalette.ColorFor(slot, dark, m.Fill)
                    Dim hover As Color = RecipientPalette.HoverColorFor(slot, dark, m.Fill)
                    pairs.Add(Tuple.Create($"slot {slot} rest", rest, m.Ink, PanelColourMetrics.TextFloor))
                    pairs.Add(Tuple.Create($"slot {slot} hover", hover, m.Ink, PanelColourMetrics.TextFloor))
                    Dim disabled As Color = PanelColourModel.SolveDerivedInk({rest, hover}, m.Ink,
                                                                             PanelColourMetrics.DisabledContrast + PanelColourMetrics.FloorMargin)
                    pairs.Add(Tuple.Create($"slot {slot} disabled rest", rest, disabled, PanelColourMetrics.DisabledContrast))
                    pairs.Add(Tuple.Create($"slot {slot} disabled hover", hover, disabled, PanelColourMetrics.DisabledContrast))
                    Dim verb As Color = PanelColourModel.SolveDerivedInk({rest}, m.Ink, PanelColourMetrics.TextFloor + PanelColourMetrics.FloorMargin)
                    pairs.Add(Tuple.Create($"slot {slot} verb", rest, verb, PanelColourMetrics.TextFloor))
                Next
                pairs.Add(Tuple.Create("copy rest", m.Hover, m.Ink, PanelColourMetrics.TextFloor))
                pairs.Add(Tuple.Create("copy hover", m.Pressed, m.Ink, PanelColourMetrics.TextFloor))
                pairs.Add(Tuple.Create("copy unusable", m.Fill, m.InkDisabled, PanelColourMetrics.DisabledContrast))
                pairs.Add(Tuple.Create("delete rest", m.Destructive, m.DestructiveInk, PanelColourMetrics.TextFloor))
                pairs.Add(Tuple.Create("delete hover", m.DestructiveHover, m.DestructiveInk, PanelColourMetrics.TextFloor))
                pairs.Add(Tuple.Create("delete pressed", m.DestructivePressed, m.DestructiveInk, PanelColourMetrics.TextFloor))
                pairs.Add(Tuple.Create("pin free", m.Fill, m.PinRestInk, PanelColourMetrics.GlyphFloor))
                pairs.Add(Tuple.Create("pin pinned", m.Fill, m.PinPinnedInk, PanelColourMetrics.GlyphFloor))
                pairs.Add(Tuple.Create("strip hover", m.Hover, m.Ink, PanelColourMetrics.GlyphFloor))
                For Each pair As Tuple(Of String, Color, Color, Double) In pairs
                    Dim fill As Color = pair.Item2
                    Dim ink As Color = pair.Item3
                    Dim floor As Double = pair.Item4
                    Dim where As String = $"{pair.Item1} on {s.Item1} scheme {s.Item2} at {percent} %"
                    Assert.True(Cr(ink, fill) >= floor - slack, $"{where}: opaque {Cr(ink, fill):F2}:1")
                    Dim scrim As Double = PanelColourModel.ScrimAlpha(fill, ink, floor, p)
                    Dim seen As Double = PanelColourModel.ScrimmedWorstCase(fill, ink, p, scrim)
                    Assert.True(seen >= floor - slack, $"{where}: over a picture {seen:F2}:1 with scrim {scrim:F3}")
                Next
            Next
        Next
    End Sub

End Class
#End If
