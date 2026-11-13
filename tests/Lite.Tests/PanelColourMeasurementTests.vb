#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading
Imports Xunit

''' <summary>
''' L0303 M-2 and M-4 (a): the tables of the 2026-11-10 baseline (sections A-F) computed by the real panel colour
''' model, the toast, the audio plate and the OCR plate re-measured beside them, and the ink layer's refresh timed
''' on the real <c>ChromeSurface.PaintInk</c> path. The tables assert their floors; the timing is reported, never
''' asserted (section 7.4). With <c>FMS_MEASURE_OUT</c> set the report is also written to that file (a relative
''' path is taken from the repository root).
''' </summary>
Public Class PanelColourMeasurementTests

    Private Const DefaultAccentArgb As Integer = &HFF0078D4
    Private Const Slack As Double = 0.01R

    Private Shared ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture
    Private Shared ReadOnly DefaultAccent As Color = Color.FromArgb(DefaultAccentArgb)

    Private Shared ReadOnly AccentHex As String() = {
        "FFB900", "FF8C00", "F7630C", "CA5010", "DA3B01", "EF6950", "D13438", "FF4343",
        "E74856", "E81123", "EA005E", "C30052", "E3008C", "BF0077", "C239B3", "9A0089",
        "0078D7", "0063B1", "8E8CD8", "6B69D6", "8764B8", "744DA9", "B146C2", "881798",
        "0099BC", "2D7D9A", "00B7C3", "038387", "00B294", "018574", "00CC6A", "10893E",
        "7A7574", "5D5A58", "68768A", "515C6B", "567C73", "486860", "498205", "107C10",
        "767676", "4C4A48", "69797E", "4A5459", "647C64", "525E54", "847545", "7E735F", "0078D4"}

    ''' <summary>The baseline's 45-colour content corpus: 9 greys, the 24 ColorChecker patches, 12 vivid colours.</summary>
    Private Shared ReadOnly CorpusNames As String() = {
        "grey 0", "grey 32", "grey 64", "grey 96", "grey 128", "grey 160", "grey 192", "grey 224", "grey 255",
        "dark skin", "light skin", "blue sky", "foliage", "blue flower", "bluish green", "orange", "purplish blue",
        "moderate red", "purple", "yellow green", "orange yellow", "blue", "green", "red", "yellow", "magenta", "cyan",
        "white 9.5", "neutral 8", "neutral 6.5", "neutral 5", "neutral 3.5", "black 2",
        "red", "green", "blue", "yellow", "cyan", "magenta", "orange", "green 200", "teal 190", "magenta 190",
        "spring", "rose"}

    Private Shared ReadOnly CorpusRgb As Integer()() = {
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

    Private Shared ReadOnly SetOpacities As Integer() = {100, 86, 75, 60, 50, 49, 40, 30, 20, 10, 0}
    Private Shared ReadOnly OverlayOpacities As Integer() = {100, 88, 80, 70, 60, 50, 40}
    Private Shared ReadOnly Scales As Integer() = {100, 150, 200, 250, 300}

    ' --- the facts ------------------------------------------------------------------------------

    ''' <summary>M-2: every table of the after-report holds the floor it answers (criteria 2-4 and 6).</summary>
    <Fact>
    Public Sub After_report_tables_hold_their_floors()
        Dim failures As New List(Of String)()
        Dim report As String = BuildReport(1, failures, IncludeTiming:=Not String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FMS_MEASURE_OUT")))
        WriteReportIfAsked(report)
        Assert.True(failures.Count = 0, String.Join(Environment.NewLine, failures.Take(20)))
    End Sub

    ''' <summary>M-4 (a): the ink layer is painted on the real surface at every scale; the numbers are reported, the
    ''' only assertion is that a measurement was taken.</summary>
    <Fact>
    Public Sub Ink_layer_refresh_is_timed_on_the_real_paint_path()
        Dim rows As List(Of TimingRow) = TimeInkLayer(10)
        Assert.Equal(Scales.Length * 2, rows.Count)
        For Each row As TimingRow In rows
            Assert.True(row.MeanMs > 0.0R, $"{row.Scale} %: no time measured")
        Next
    End Sub

    ' --- the report ------------------------------------------------------------------------------

    ''' <summary>Builds the after-report. <paramref name="stride"/> thins section A's 256-level cube (1 = every
    ''' opaque fill); every floor that does not hold is added to <paramref name="failures"/>.</summary>
    Friend Shared Function BuildReport(stride As Integer, failures As List(Of String), includeTiming As Boolean) As String
        Dim sb As New StringBuilder()
        sb.AppendLine("# L0303 after measurements")
        sb.AppendLine()
        sb.AppendLine(String.Format(Inv, "Taken {0:yyyy-MM-dd HH:mm}, .NET {1}, {2}. Every number is computed by `PanelColourModel` as shipped " &
                                    "(`PanelColourMeasurementTests`); the section letters answer [baseline-report.md](baseline-report.md) row by row.",
                                    DateTime.Now, Environment.Version, Environment.OSVersion.VersionString))
        sb.AppendLine()
        SectionA(sb, stride, failures)
        SectionB(sb, failures)
        SectionC(sb, failures)
        SectionD(sb, failures)
        SectionE(sb, failures)
        SectionF(sb, failures)
        SectionOther(sb, failures)
        If includeTiming Then SectionH(sb)
        Return sb.ToString()
    End Function

    Private Shared Sub SectionA(sb As StringBuilder, stride As Integer, failures As List(Of String))
        ' Two rules: the palette pair (the panels' ink on a model fill) and the ink of text on a raw picture colour.
        Dim palTotal As Long = 0, palBelow45 As Long = 0, palBelow3 As Long = 0, palWeaker As Long = 0
        Dim conBelow45 As Long = 0, conBelow3 As Long = 0
        Dim palMin As Double = Double.MaxValue, conMin As Double = Double.MaxValue
        Dim palWorst As Color = Color.Empty, conWorst As Color = Color.Empty
        For r As Integer = 0 To 255 Step stride
            For g As Integer = 0 To 255 Step stride
                For b As Integer = 0 To 255 Step stride
                    Dim fill As Color = Color.FromArgb(255, r, g, b)
                    Dim ink As Color = PanelColourModel.BetterInk(fill)
                    Dim c As Double = PanelColourModel.ContrastRatio(ink, fill)
                    Dim other As Color = If(ink.ToArgb() = PanelColourMetrics.InkOnDark.ToArgb(), PanelColourMetrics.InkOnLight, PanelColourMetrics.InkOnDark)
                    palTotal += 1
                    If c < 4.5R Then palBelow45 += 1
                    If c < 3.0R Then palBelow3 += 1
                    If PanelColourModel.ContrastRatio(other, fill) > c + 0.000001R Then palWeaker += 1
                    If c < palMin Then palMin = c : palWorst = fill
                    Dim cc As Double = PanelColourModel.ContrastRatio(PanelColourModel.ContentInk(fill), fill)
                    If cc < 4.5R Then conBelow45 += 1
                    If cc < 3.0R Then conBelow3 += 1
                    If cc < conMin Then conMin = cc : conWorst = fill
                Next
            Next
        Next
        sb.AppendLine(String.Format(Inv, "## A. Ink rule over {0:N0} opaque fills", palTotal))
        sb.AppendLine()
        sb.AppendLine("| Rule (where) | Min contrast | Worst fill | Fills < 4.5:1 | Fills < 3:1 | Picks the weaker ink |")
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |")
        sb.AppendLine(String.Format(Inv, "| `PanelColourModel.BetterInk` - WCAG better of the palette inks (240,240,240) / (0,0,0) (the panels' ink, on model fills only; F1 and F2 retired) | {0:0.00}:1 | {1} | {2:0.00} % | {3:0.00} % | {4:0.00} % |",
                                    palMin, Rgb(palWorst), 100.0R * palBelow45 / palTotal, 100.0R * palBelow3 / palTotal, 100.0R * palWeaker / palTotal))
        sb.AppendLine(String.Format(Inv, "| `PanelColourModel.ContentInk` - the palette ink, pure black or white where it falls under 4.5:1 (the thumbnail panel on the raw scheme colour) | {0:0.00}:1 | {1} | {2:0.00} % | {3:0.00} % | 0.00 % |",
                                    conMin, Rgb(conWorst), 100.0R * conBelow45 / palTotal, 100.0R * conBelow3 / palTotal))
        If palWeaker > 0 Then failures.Add($"A: BetterInk picks the weaker palette ink for {palWeaker} fills")
        If conMin < 4.5R Then failures.Add($"A: ContentInk {conMin:0.00}:1 at {Rgb(conWorst)}")

        ' The panels themselves: the ink of the model's fill for every sample of the 17-level grid.
        Dim panelMin As Double = Double.MaxValue
        Dim panelWorst As Color = Color.Empty
        Dim count As Integer = 0
        For r As Integer = 0 To 16
            For g As Integer = 0 To 16
                For b As Integer = 0 To 16
                    Dim sample As Color = Color.FromArgb(255, Math.Min(255, r * 16), Math.Min(255, g * 16), Math.Min(255, b * 16))
                    Dim m As PanelColours = PanelColourModel.ForSample(sample, 0, Nothing, 100, DefaultAccent, False)
                    Dim c As Double = PanelColourModel.ContrastRatio(m.Ink, m.Fill)
                    count += 1
                    If c < panelMin Then panelMin = c : panelWorst = sample
                Next
            Next
        Next
        sb.AppendLine(String.Format(Inv, "| The panel ink on the model's fill, any sample of the 17-level grid ({0:N0} samples; F1 on the raw sample before) | {1:0.00}:1 | sample {2} | 0.00 % | 0.00 % | - |",
                                    count, panelMin, Rgb(panelWorst)))
        If panelMin < 4.5R Then failures.Add($"A: panel ink {panelMin:0.00}:1 for sample {Rgb(panelWorst)}")
        sb.AppendLine()
    End Sub

    Private Shared Sub SectionB(sb As StringBuilder, failures As List(Of String))
        sb.AppendLine("## B. Checked item: the model's accent fill and its ink")
        sb.AppendLine()
        sb.AppendLine("| Tone | Swatches | Min ink contrast (rest / hover / pressed) | Worst swatch | Below 4.5:1 | Largest hue shift |")
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |")
        For Each tone As KeyValuePair(Of String, Integer) In {New KeyValuePair(Of String, Integer)("dark (black scheme)", PanelColourMetrics.BlackScheme),
                                                             New KeyValuePair(Of String, Integer)("light (white scheme)", PanelColourMetrics.WhiteScheme)}
            Dim minC As Double = Double.MaxValue
            Dim worst As String = ""
            Dim below As Integer = 0
            Dim shift As Double = 0.0R
            For Each hex As String In AccentHex
                Dim accent As Color = FromHex(hex)
                Dim m As PanelColours = PanelColourModel.ForSample(If(tone.Value = PanelColourMetrics.BlackScheme, Color.Black, Color.White),
                                                                   tone.Value, Nothing, 100, accent, False)
                Dim c As Double = Math.Min(PanelColourModel.ContrastRatio(m.CheckedInk, m.Checked),
                                           Math.Min(PanelColourModel.ContrastRatio(m.CheckedInk, m.CheckedHover),
                                                    PanelColourModel.ContrastRatio(m.CheckedInk, m.CheckedPressed)))
                If c < 4.5R - Slack Then below += 1
                If c < minC Then minC = c : worst = "#" & hex
                Dim a As Double() = PanelColourModel.ToOkLch(accent)
                Dim k As Double() = PanelColourModel.ToOkLch(m.Checked)
                If a(1) >= 0.03R Then shift = Math.Max(shift, HueGap(a(2), k(2)))
            Next
            sb.AppendLine(String.Format(Inv, "| {0} | {1} | {2:0.00}:1 | {3} | {4} | {5:0.0} degrees |", tone.Key, AccentHex.Length, minC, worst, below, shift))
            If below > 0 Then failures.Add($"B: {below} swatches under 4.5:1 on the {tone.Key} tone")
        Next
        sb.AppendLine()
    End Sub

    Private Shared Sub SectionC(sb As StringBuilder, failures As List(Of String))
        sb.AppendLine("## C. Panel fill = the model's tint of the sampled pixel: OKLCH chroma")
        sb.AppendLine()
        Dim rows As New StringBuilder()
        Dim largest As Double = 0.0R
        Dim largestAt As String = ""
        For i As Integer = 9 To CorpusRgb.Length - 1
            Dim sample As Color = CorpusColor(i)
            Dim m As PanelColours = PanelColourModel.ForSample(sample, 0, Nothing, 100, DefaultAccent, False)
            Dim s As Double() = PanelColourModel.ToOkLch(sample)
            Dim f As Double() = PanelColourModel.ToOkLch(m.Fill)
            Dim cap As Double = If(m.Tone = PanelTone.Dark, PanelColourMetrics.DarkChromaCap, PanelColourMetrics.LightChromaCap)
            Dim gap As Double = If(s(1) >= 0.03R, HueGap(s(2), f(2)), 0.0R)
            Dim ink As Double = PanelColourModel.ContrastRatio(m.Ink, m.Fill)
            If f(1) > largest Then largest = f(1) : largestAt = CorpusNames(i)
            rows.AppendLine(String.Format(Inv, "| {0} | {1} | {2} {3} | {4:0.000} | {5:0} | {6:0.0} | {7:0.00}:1 |",
                                          CorpusNames(i), Rgb(sample), If(m.Tone = PanelTone.Dark, "dark", "light"), Rgb(m.Fill), f(1), f(2), gap, ink))
            If f(1) > cap + 0.003R Then failures.Add($"C: {CorpusNames(i)} fill chroma {f(1):0.000} over the cap {cap:0.000}")
            If gap > 8.0R Then failures.Add($"C: {CorpusNames(i)} hue moved {gap:0.0} degrees")
            If ink < 4.5R Then failures.Add($"C: {CorpusNames(i)} ink {ink:0.00}:1")
        Next
        sb.AppendLine(String.Format(Inv, "Largest chroma a panel is painted in: C = {0:0.000} ({1}); the caps are {2:0.000} dark / {3:0.000} light (baseline: 0.322).",
                                    largest, largestAt, PanelColourMetrics.DarkChromaCap, PanelColourMetrics.LightChromaCap))
        sb.AppendLine()
        sb.AppendLine("| Sampled colour | sRGB | Tone, fill | OKLCH C | h | Hue shift | Ink contrast |")
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |")
        sb.Append(rows)
        sb.AppendLine()
    End Sub

    Private Shared Sub SectionD(sb As StringBuilder, failures As List(Of String))
        sb.AppendLine("## D. Panels with the ink layer: ink over its scrim over (item fill at the set opacity over the picture)")
        sb.AppendLine()
        sb.AppendLine("Content: the 45-colour corpus. Item fills: rest, hover and pressed with the panel ink, the scrim solved for text (R-10). No flip below 50.")
        sb.AppendLine()
        For Each scheme As SchemeCase In SchemeCases()
            sb.AppendLine("### " & scheme.Name)
            sb.AppendLine()
            sb.AppendLine("| Set opacity | Min | P05 | Median | < 4.5:1 | < 3:1 | Worst (fill / content) |")
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |")
            For Each percent As Integer In SetOpacities
                Dim values As New List(Of Double)()
                Dim minC As Double = Double.MaxValue
                Dim worst As String = ""
                For Each sample As NamedColour In scheme.Samples
                    Dim m As PanelColours = PanelColourModel.ForSample(sample.Colour, scheme.Scheme, Nothing, percent, DefaultAccent, False)
                    For Each itemFill As Color In {m.Fill, m.Hover, m.Pressed}
                        Dim scrim As Double = m.ScrimAlphaFor(itemFill, m.Ink, True)
                        For i As Integer = 0 To CorpusRgb.Length - 1
                            Dim backdrop As Color = PanelColourModel.Composite(itemFill, scrim, PanelColourModel.Composite(itemFill, m.SetOpacity, CorpusColor(i)))
                            Dim c As Double = PanelColourModel.ContrastRatio(m.Ink, backdrop)
                            values.Add(c)
                            If c < minC Then minC = c : worst = sample.Name & " / " & CorpusNames(i)
                        Next
                    Next
                Next
                sb.AppendLine(StatsRow(percent.ToString(Inv), values, worst))
                If minC < 4.5R - Slack Then failures.Add($"D: {scheme.Name} at {percent}: {minC:0.00}:1 ({worst})")
            Next
            sb.AppendLine()
        Next
    End Sub

    Private Shared Sub SectionE(sb As StringBuilder, failures As List(Of String))
        sb.AppendLine("## E. Transport bar (real controls): the window, ink included, at max(whole-window floor, set opacity)")
        sb.AppendLine()
        For Each scheme As SchemeCase In SchemeCases()
            sb.AppendLine("### " & scheme.Name)
            sb.AppendLine()
            sb.AppendLine("| Set opacity | Window opacity (lowest) | Min | P05 | Median | < 4.5:1 | < 3:1 | Worst (fill / content) |")
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |")
            For Each percent As Integer In SetOpacities
                Dim values As New List(Of Double)()
                Dim minC As Double = Double.MaxValue
                Dim worst As String = ""
                Dim lowestWindow As Double = 1.0R
                For Each sample As NamedColour In scheme.Samples
                    Dim m As PanelColours = PanelColourModel.ForSample(sample.Colour, scheme.Scheme, Nothing, percent, DefaultAccent, False)
                    Dim w As Double = Math.Min(1.0R, Math.Max(m.WholeWindowFloor, m.SetOpacity))
                    lowestWindow = Math.Min(lowestWindow, w)
                    For i As Integer = 0 To CorpusRgb.Length - 1
                        Dim content As Color = CorpusColor(i)
                        Dim c As Double = PanelColourModel.ContrastRatio(PanelColourModel.Composite(m.Ink, w, content), PanelColourModel.Composite(m.Fill, w, content))
                        values.Add(c)
                        If c < minC Then minC = c : worst = sample.Name & " / " & CorpusNames(i)
                    Next
                Next
                Dim row As String = StatsRow(percent.ToString(Inv), values, worst)
                sb.AppendLine(row.Insert(row.IndexOf("|"c, 2), String.Format(Inv, "| {0:0} % ", lowestWindow * 100.0R)))
                If minC < 4.5R - Slack Then failures.Add($"E: {scheme.Name} at {percent}: {minC:0.00}:1 ({worst})")
            Next
            sb.AppendLine()
        Next
    End Sub

    Private Shared Sub SectionF(sb As StringBuilder, failures As List(Of String))
        sb.AppendLine("## F. Recipients table (ink layer over the rows; the table's tone is the panels'; range 40..100, default 88)")
        sb.AppendLine()
        For Each tone As KeyValuePair(Of String, Integer) In {New KeyValuePair(Of String, Integer)("dark tone, black scheme", PanelColourMetrics.BlackScheme),
                                                             New KeyValuePair(Of String, Integer)("light tone, white scheme", PanelColourMetrics.WhiteScheme)}
            sb.AppendLine("### " & tone.Key)
            sb.AppendLine()
            sb.AppendLine("| Overlay opacity | Min | P05 | Median | < 4.5:1 | < 3:1 | Worst (row / content) |")
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |")
            Dim sample As Color = If(tone.Value = PanelColourMetrics.BlackScheme, Color.Black, Color.White)
            For Each percent As Integer In OverlayOpacities
                Dim m As PanelColours = PanelColourModel.ForSample(sample, tone.Value, Nothing, percent, DefaultAccent, False)
                Dim dark As Boolean = (m.Tone = PanelTone.Dark)
                Dim rows As New List(Of Tuple(Of String, Color, Color))()
                For slot As Integer = 0 To 10
                    rows.Add(Tuple.Create($"slot {slot}", RecipientPalette.ColorFor(slot, dark, m.Fill), m.Ink))
                    rows.Add(Tuple.Create($"slot {slot} hover", RecipientPalette.HoverColorFor(slot, dark, m.Fill), m.Ink))
                Next
                rows.Add(Tuple.Create("copy", m.Hover, m.Ink))
                rows.Add(Tuple.Create("delete", m.Destructive, m.DestructiveInk))
                Dim values As New List(Of Double)()
                Dim minC As Double = Double.MaxValue
                Dim worst As String = ""
                For Each row As Tuple(Of String, Color, Color) In rows
                    Dim scrim As Double = PanelColourModel.ScrimAlpha(row.Item2, row.Item3, PanelColourMetrics.TextFloor, m.SetOpacity)
                    For i As Integer = 0 To CorpusRgb.Length - 1
                        Dim backdrop As Color = PanelColourModel.Composite(row.Item2, scrim, PanelColourModel.Composite(row.Item2, m.SetOpacity, CorpusColor(i)))
                        Dim c As Double = PanelColourModel.ContrastRatio(row.Item3, backdrop)
                        values.Add(c)
                        If c < minC Then minC = c : worst = row.Item1 & " / " & CorpusNames(i)
                    Next
                Next
                sb.AppendLine(StatsRow(percent.ToString(Inv), values, worst))
                If minC < 4.5R - Slack Then failures.Add($"F: {tone.Key} at {percent}: {minC:0.00}:1 ({worst})")
            Next
            sb.AppendLine()
        Next
    End Sub

    ''' <summary>Surfaces over content that are not panels (section 3.1): re-measured so a regression shows.</summary>
    Private Shared Sub SectionOther(sb As StringBuilder, failures As List(Of String))
        sb.AppendLine("## Other surfaces over content (not changed by L0303)")
        sb.AppendLine()
        sb.AppendLine("| Surface | How | Min | Median | Worst content |")
        sb.AppendLine("| --- | --- | --- | --- | --- |")

        Dim toast As New List(Of Double)()
        Dim toastMin As Double = Double.MaxValue
        Dim toastWorst As String = ""
        For i As Integer = 0 To CorpusRgb.Length - 1
            Dim c As Double = PanelColourModel.ContrastRatio(PanelColourModel.Composite(NotifyToast.CardInk, 0.94R, CorpusColor(i)),
                                                             PanelColourModel.Composite(NotifyToast.CardFill, 0.94R, CorpusColor(i)))
            toast.Add(c)
            If c < toastMin Then toastMin = c : toastWorst = CorpusNames(i)
        Next
        Dim toastFloor As Double = PanelColourModel.WholeWindowWorst(NotifyToast.CardFill, NotifyToast.CardInk, 0.94R)
        sb.AppendLine(String.Format(Inv, "| Notify toast | card {0} / ink {1}, the whole window at 94 % | {2:0.00}:1 (grey ramp and primaries: {3:0.00}:1) | {4:0.00}:1 | {5} |",
                                    Rgb(NotifyToast.CardFill), Rgb(NotifyToast.CardInk), toastMin, toastFloor, Median(toast), toastWorst))
        If Math.Min(toastMin, toastFloor) < 4.5R Then failures.Add($"Other: toast {Math.Min(toastMin, toastFloor):0.00}:1")

        Dim audio As New List(Of Double)()
        Dim audioMin As Double = Double.MaxValue
        Dim audioWorst As String = ""
        For Each ink As Color In AudioOverlayColours.TextInks().Distinct()
            For i As Integer = 0 To CorpusRgb.Length - 1
                Dim c As Double = AudioOverlayColours.InkContrast(ink, CorpusColor(i))
                audio.Add(c)
                If c < audioMin Then audioMin = c : audioWorst = ink.Name & " over " & CorpusNames(i)
            Next
        Next
        sb.AppendLine(String.Format(Inv, "| Audio plate | text inks over the plate (alpha {0}) over the content | {1:0.00}:1 | {2:0.00}:1 | {3} |",
                                    AudioOverlayColours.PlateAlpha, audioMin, Median(audio), audioWorst))
        If audioMin < AudioOverlayColours.TextContrastFloor Then failures.Add($"Other: audio plate {audioMin:0.00}:1 ({audioWorst})")

        ' The OCR plate takes its paper and ink from the picture (OCR-OVERLAY rule 13): a synthetic text block on each
        ' corpus colour, written in the better of black and white, sampled as the overlay does. Reported only.
        Dim ocr As New List(Of Double)()
        Dim ocrMin As Double = Double.MaxValue
        Dim ocrWorst As String = ""
        For i As Integer = 0 To CorpusRgb.Length - 1
            Dim paper As Color = CorpusColor(i)
            Dim textInk As Color = PanelColourModel.BetterInk(paper)
            Dim plate As OcrPlateColors.PlatePalette = OcrPlateColors.Compute(TextScene(paper.ToArgb(), textInk.ToArgb()), OcrFrame, OcrFrame, OcrBlock, 14)
            If plate.Background = 0 AndAlso plate.Ink = 0 Then Continue For
            Dim c As Double = PanelColourModel.ContrastRatio(Color.FromArgb(plate.Ink Or &HFF000000), Color.FromArgb(plate.Background Or &HFF000000))
            ocr.Add(c)
            If c < ocrMin Then ocrMin = c : ocrWorst = CorpusNames(i)
        Next
        If ocr.Count > 0 Then
            sb.AppendLine(String.Format(Inv, "| OCR plate | sampled plate ink on plate paper, opaque, {0} of {1} synthetic blocks sampled | {2:0.00}:1 | {3:0.00}:1 | {4} |",
                                        ocr.Count, CorpusRgb.Length, ocrMin, Median(ocr), ocrWorst))
        Else
            sb.AppendLine("| OCR plate | no synthetic block produced a plate | - | - | - |")
        End If
        sb.AppendLine()
    End Sub

    Private Shared Sub SectionH(sb As StringBuilder)
        sb.AppendLine("## H. Ink layer cost per refresh (the real `ChromeSurface.PaintInk`: 24 captioned buttons, black scheme)")
        sb.AppendLine()
        sb.AppendLine(String.Format(Inv, "Measured on this machine, {0} processors; painting into the layer's bitmap only (the hand-over to the layered window is not timed). Reported, never asserted.", Environment.ProcessorCount))
        sb.AppendLine()
        sb.AppendLine("| Panel (scale) | Size px | Set opacity | Scrim | Mean ms | P95 ms |")
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |")
        For Each row As TimingRow In TimeInkLayer(60)
            sb.AppendLine(String.Format(Inv, "| {0} % | {1} x {2} | {3} | {4} | {5:0.00} | {6:0.00} |",
                                        row.Scale, row.Width, row.Height, row.SetOpacity, If(row.Scrimmed, "yes", "no"), row.MeanMs, row.P95Ms))
        Next
        sb.AppendLine()
    End Sub

    ' --- M-4 (a) ---------------------------------------------------------------------------------

    Friend NotInheritable Class TimingRow
        Public Scale As Integer
        Public Width As Integer
        Public Height As Integer
        Public SetOpacity As Integer
        Public Scrimmed As Boolean
        Public MeanMs As Double
        Public P95Ms As Double
    End Class

    Friend Shared Function TimeInkLayer(iterations As Integer) As List(Of TimingRow)
        Dim rows As New List(Of TimingRow)()
        Dim failure As Exception = Nothing
        Dim worker As New Thread(Sub()
                                     Try
                                         For Each scale As Integer In Scales
                                             For Each percent As Integer In {100, 30}
                                                 rows.Add(TimeOne(scale, percent, iterations))
                                             Next
                                         Next
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        worker.SetApartmentState(ApartmentState.STA)
        worker.Start()
        Assert.True(worker.Join(120000), "The ink layer timing did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
        Return rows
    End Function

    Private Shared Function TimeOne(scale As Integer, percent As Integer, iterations As Integer) As TimingRow
        Dim f As Single = scale / 100.0F
        Dim width As Integer = CInt(Math.Round(1000 * f))
        Dim height As Integer = CInt(Math.Round(44 * f))
        Dim colours As PanelColours = PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing, percent, DefaultAccent, False)
        Using textFont As New Font("Segoe UI", 14.0F * f, FontStyle.Regular, GraphicsUnit.Pixel),
              symbolFont As New Font("Segoe UI Symbol", 16.0F * f, FontStyle.Regular, GraphicsUnit.Pixel),
              surface As New ChromeSurface With {.Size = New Size(width, height)}
            surface.Configure(ChromeTheme.FromColours(colours), f, False, textFont, symbolFont)
            Dim items As New List(Of ChromeItem)()
            For i As Integer = 0 To 23
                items.Add(New ChromeItem("item" & i.ToString(Inv), ChromeItemKind.Button) With {.Caption = "Item " & i.ToString(Inv)})
            Next
            surface.SetItems(items)
            surface.InkExternal = True
            surface.ScrimOpacity = colours.SetOpacity
            Dim samples As New List(Of Double)()
            Using ink As New Bitmap(width, height, PixelFormat.Format32bppPArgb)
                For i As Integer = -4 To iterations - 1
                    Dim sw As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
                    Using g As Graphics = Graphics.FromImage(ink)
                        g.Clear(Color.Transparent)
                        surface.PaintInk(g)
                    End Using
                    sw.Stop()
                    If i >= 0 Then samples.Add(sw.Elapsed.TotalMilliseconds)
                Next
            End Using
            samples.Sort()
            Return New TimingRow With {
                .Scale = scale, .Width = width, .Height = height, .SetOpacity = percent,
                .Scrimmed = colours.TextScrimAlpha > 0.0R,
                .MeanMs = samples.Average(),
                .P95Ms = samples(Math.Max(0, CInt(Math.Ceiling(0.95R * samples.Count)) - 1))}
        End Using
    End Function

    ' --- helpers --------------------------------------------------------------------------------

    Private NotInheritable Class NamedColour
        Public Name As String
        Public Colour As Color
    End Class

    Private NotInheritable Class SchemeCase
        Public Name As String
        Public Scheme As Integer
        Public Samples As List(Of NamedColour)
    End Class

    Private Shared Function SchemeCases() As List(Of SchemeCase)
        Dim dynamicSamples As New List(Of NamedColour)()
        For i As Integer = 0 To CorpusRgb.Length - 1
            dynamicSamples.Add(New NamedColour With {.Name = CorpusNames(i), .Colour = CorpusColor(i)})
        Next
        Return New List(Of SchemeCase) From {
            New SchemeCase With {.Name = "black scheme (the shipped default)", .Scheme = PanelColourMetrics.BlackScheme,
                                 .Samples = New List(Of NamedColour) From {New NamedColour With {.Name = "black", .Colour = Color.Black}}},
            New SchemeCase With {.Name = "white scheme", .Scheme = PanelColourMetrics.WhiteScheme,
                                 .Samples = New List(Of NamedColour) From {New NamedColour With {.Name = "white", .Colour = Color.White}}},
            New SchemeCase With {.Name = "dynamic schemes (sample = each of the 45 colours)", .Scheme = 0, .Samples = dynamicSamples}}
    End Function

    Private Shared Function StatsRow(label As String, values As List(Of Double), worst As String) As String
        Dim sorted As List(Of Double) = values.OrderBy(Function(v) v).ToList()
        Dim n As Integer = sorted.Count
        Dim p05 As Double = sorted(Math.Max(0, CInt(Math.Ceiling(0.05R * n)) - 1))
        Dim below45 As Integer = sorted.Where(Function(v) v < 4.5R).Count()
        Dim below3 As Integer = sorted.Where(Function(v) v < 3.0R).Count()
        Return String.Format(Inv, "| {0} | {1:0.00} | {2:0.00} | {3:0.00} | {4:0.0} % | {5:0.0} % | {6} |",
                             label, sorted(0), p05, Median(sorted), 100.0R * below45 / n, 100.0R * below3 / n, worst)
    End Function

    Private Shared Function Median(values As IEnumerable(Of Double)) As Double
        Dim sorted As List(Of Double) = values.OrderBy(Function(v) v).ToList()
        Return sorted(Math.Max(0, CInt(Math.Ceiling(0.5R * sorted.Count)) - 1))
    End Function

    Private Shared Function CorpusColor(i As Integer) As Color
        Return Color.FromArgb(255, CorpusRgb(i)(0), CorpusRgb(i)(1), CorpusRgb(i)(2))
    End Function

    Private Shared Function FromHex(h As String) As Color
        Return Color.FromArgb(255, Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16))
    End Function

    Private Shared Function Rgb(c As Color) As String
        Return String.Format(Inv, "({0},{1},{2})", c.R, c.G, c.B)
    End Function

    Private Shared Function HueGap(a As Double, b As Double) As Double
        Dim d As Double = Math.Abs(a - b) Mod 360.0R
        Return If(d > 180.0R, 360.0R - d, d)
    End Function

    Private Const OcrFrame As Integer = 120
    Private Shared ReadOnly OcrBlock As New Rectangle(20, 20, 80, 60)

    ''' <summary>A frame of <paramref name="paper"/> with 14 rows of every-other-column ink at the top of the block:
    ''' the shape of a line of text (the scene of OcrPlateColorsTests).</summary>
    Private Shared Function TextScene(paper As Integer, ink As Integer) As Integer()
        Dim pixels(OcrFrame * OcrFrame - 1) As Integer
        For i As Integer = 0 To pixels.Length - 1
            pixels(i) = paper
        Next
        For y As Integer = 20 To 33
            For x As Integer = 20 To 99
                If (x Mod 2) = 0 Then pixels(y * OcrFrame + x) = ink
            Next
        Next
        Return pixels
    End Function

    Private Shared Sub WriteReportIfAsked(report As String)
        Dim target As String = Environment.GetEnvironmentVariable("FMS_MEASURE_OUT")
        If String.IsNullOrEmpty(target) Then Return
        If Not Path.IsPathRooted(target) Then target = Path.Combine(RepositoryRoot(), target)
        File.WriteAllText(target, report, New UTF8Encoding(False))
    End Sub

    Private Shared Function RepositoryRoot() As String
        For Each start As String In {Environment.CurrentDirectory, AppContext.BaseDirectory}
            Dim dir As DirectoryInfo = New DirectoryInfo(start)
            While dir IsNot Nothing
                If Directory.Exists(Path.Combine(dir.FullName, "tests", "Lite.Tests")) Then Return dir.FullName
                dir = dir.Parent
            End While
        Next
        Return Environment.CurrentDirectory
    End Function

End Class
#End If
