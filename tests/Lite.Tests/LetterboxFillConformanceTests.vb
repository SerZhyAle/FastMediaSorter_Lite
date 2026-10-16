Option Strict On

Imports System.Collections
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports Xunit

''' <summary>
''' LETTERBOX-BARS 0.1 and LETTERBOX-HALO 0.1, rung 1 of section 5 (ticket 183): every value of the
''' catalog's vectors (vendored under TestData\letterbox-vectors beside a PROVENANCE.txt whose digest
''' is checked first, the .fd-sec way) reproduced by the pure functions of <c>LetterboxFillMath</c> -
''' the one place the rules' numbers live. Exactly for integers and strings, to 1e-4 for reals, as the
''' contract says.
'''
''' Runs on BOTH legs. The bars are shared code that both shipped exes compile (net48 x86 and the
''' .NET 10 mainline), so the same arithmetic is proven under the two runtimes; only the check that
''' the modern route through <c>ZoomMath.PictureBoxZoomBounds</c> gives the same rectangles is
''' modern-only, like ZoomMath itself.
'''
''' The rendered halo gradient is checked separately by HaloGradientTests against the same math
''' (within 2 RGB levels at pixel centres). Full bars/resampling and recorded-motion acceptance
''' remain separate; see docs/contracts/LETTERBOX-FILL.md.
''' </summary>
Public Class LetterboxFillConformanceTests

    ''' <summary>The contract's "1e-4 for reals" plus floating-point slack. The vectors carry four
    ''' decimals, so a value that is exactly 0.0001 away is 0.00010000000003.. in a Double.</summary>
    Private Const RealTolerance As Double = 0.0001 + 0.000000001

    Private Const VectorFile As String = "letterbox-fill-vectors.json"

    Private Shared ReadOnly VectorDirectory As String =
        Path.Combine(AppContext.BaseDirectory, "TestData", "letterbox-vectors")

    Private Shared ReadOnly Vectors As Dictionary(Of String, Object) = LoadVectors()

    ' ------------------------------------------------------------------------- reading the JSON --

    Private Shared Function LoadVectors() As Dictionary(Of String, Object)
        ' The viewer's own portable JSON facade: JavaScriptSerializer on net48, System.Text.Json
        ' on net10, both shaped to Dictionary(Of String, Object) / Object() / numbers.
        Return ObjOf(TranslateHttp.JsonDeserializeObject(File.ReadAllText(Path.Combine(VectorDirectory, VectorFile))))
    End Function

    Private Shared Function ObjOf(node As Object) As Dictionary(Of String, Object)
        Dim map As Dictionary(Of String, Object) = TryCast(node, Dictionary(Of String, Object))
        Assert.NotNull(map)
        Return map
    End Function

    Private Shared Function Items(node As Object) As List(Of Object)
        Assert.False(TypeOf node Is String)
        Dim sequence As IEnumerable = TryCast(node, IEnumerable)
        Assert.NotNull(sequence)
        Return sequence.Cast(Of Object)().ToList()
    End Function

    Private Shared Function DblOf(node As Object) As Double
        Return Convert.ToDouble(node, CultureInfo.InvariantCulture)
    End Function

    Private Shared Function IntOf(node As Object) As Integer
        Return Convert.ToInt32(node, CultureInfo.InvariantCulture)
    End Function

    Private Shared Function Rgb(node As Object) As Color
        Dim c As List(Of Object) = Items(node)
        Assert.Equal(3, c.Count)
        Return Color.FromArgb(255, IntOf(c(0)), IntOf(c(1)), IntOf(c(2)))
    End Function

    Private Shared Function RgbList(node As Object) As List(Of Color)
        Return Items(node).Select(Function(entry) Rgb(entry)).ToList()
    End Function

    Private Shared Function Section(name As String) As Dictionary(Of String, Object)
        Return ObjOf(Vectors(name))
    End Function

    Private Shared Sub AssertNear(expected As Double, actual As Double, what As String)
        Assert.True(Math.Abs(expected - actual) <= RealTolerance,
                    what & ": expected " & expected.ToString("R", CultureInfo.InvariantCulture) &
                    ", got " & actual.ToString("R", CultureInfo.InvariantCulture))
    End Sub

    ' ------------------------------------------------------------------------------ provenance --

    <Fact>
    Public Sub Vendored_vectors_match_their_recorded_digest()
        Dim provenance As String = File.ReadAllText(Path.Combine(VectorDirectory, "PROVENANCE.txt"))
        Dim actual As String
        Using sha As SHA256 = SHA256.Create()
            actual = String.Concat(sha.ComputeHash(File.ReadAllBytes(Path.Combine(VectorDirectory, VectorFile))).Select(Function(b) b.ToString("x2")))
        End Using

        Assert.Contains("  " & actual & "  " & VectorFile, provenance)
    End Sub

    <Fact>
    Public Sub The_vectors_are_the_version_the_module_cites()
        Assert.Equal("LETTERBOX-BARS " & LetterboxFillMath.ContractVersion & ", LETTERBOX-HALO " & LetterboxFillMath.ContractVersion,
                     Convert.ToString(Vectors("contract")))
    End Sub

    ' ----------------------------------------------------------------------------- constants --

    <Fact>
    Public Sub Every_constant_of_both_contracts_is_the_catalogs()
        Dim bars As Dictionary(Of String, Object) = ObjOf(Section("bars")("constants"))
        Assert.Equal(IntOf(bars("aspectDecimals")), LetterboxFillMath.AspectDecimals)
        Assert.Equal(IntOf(bars("edgeInset")), LetterboxFillMath.EdgeInset)
        Assert.Equal(IntOf(bars("edgeOverlap")), LetterboxFillMath.EdgeOverlap)
        Assert.Equal(IntOf(bars("seamBand")), LetterboxFillMath.SeamBand)
        Assert.Equal(IntOf(bars("sampleStep")), LetterboxFillMath.SampleStep)
        Assert.Equal(IntOf(bars("trimPercent")), LetterboxFillMath.TrimPercent)
        Assert.Equal(IntOf(bars("deviationPercent")), LetterboxFillMath.DeviationPercent)
        AssertNear(DblOf(bars("spreadThreshold")), LetterboxFillMath.SpreadThreshold, "bars.constants.spreadThreshold")
        AssertNear(DblOf(bars("smoothFactor")), LetterboxFillMath.SmoothFactor, "bars.constants.smoothFactor")

        Dim halo As Dictionary(Of String, Object) = ObjOf(Section("halo")("constants"))
        AssertNear(DblOf(halo("falloffGamma")), LetterboxFillMath.FalloffGamma, "halo.constants.falloffGamma")
        Assert.Equal(IntOf(halo("blendStops")), LetterboxFillMath.BlendStops)
        AssertNear(DblOf(halo("durationSlowMs")), LetterboxFillMath.DurationSlowMs, "halo.constants.durationSlowMs")
        Assert.Equal(IntOf(halo("frameIntervalMs")), LetterboxFillMath.FrameIntervalMs)
    End Sub

    ' ------------------------------------------------------------------------- LETTERBOX-BARS --

    ''' <summary>Rules 1 and 2: which axis, and where the fitted image sits. The 1920x1081 and 1x1000
    ''' rows are the ones a rounding mistake or a missing clamp fails.</summary>
    <Fact>
    Public Sub Bars_axis_and_fitted_rectangle_rules_1_and_2()
        Dim cases As List(Of Object) = Items(Section("bars")("imageRect"))
        Assert.Equal(8, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Dim image As List(Of Object) = Items(v("image"))
            Dim surface As List(Of Object) = Items(v("surface"))
            Dim label As String = IntOf(image(0)) & "x" & IntOf(image(1)) & " in " & IntOf(surface(0)) & "x" & IntOf(surface(1))

            Dim expectedAxis As LetterboxFillMath.LetterboxAxis = LetterboxFillMath.LetterboxAxis.None
            If v("axis") IsNot Nothing Then
                expectedAxis = If(Convert.ToString(v("axis")) = "pillarbox",
                                  LetterboxFillMath.LetterboxAxis.Pillarbox,
                                  LetterboxFillMath.LetterboxAxis.Letterbox)
            End If
            Assert.True(expectedAxis = LetterboxFillMath.AxisOf(IntOf(image(0)), IntOf(image(1)), IntOf(surface(0)), IntOf(surface(1))),
                        "axis of " & label)

            Dim r As List(Of Object) = Items(v("rect"))
            Assert.Equal(New Rectangle(IntOf(r(0)), IntOf(r(1)), IntOf(r(2)), IntOf(r(3))),
                         LetterboxFillMath.FitRectangle(IntOf(image(0)), IntOf(image(1)), IntOf(surface(0)), IntOf(surface(1))))
        Next
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>The mainline does not call <c>FitRectangle</c>: it asks the zoom model's
    ''' <c>PictureBoxZoomBounds</c> (the same rectangle PictureBox.Zoom paints). That route is what
    ''' reaches the screen, so it is held to the same vectors.</summary>
    <Fact>
    Public Sub The_mainline_route_through_ZoomMath_gives_the_same_rectangles()
        For Each node As Object In Items(Section("bars")("imageRect"))
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Dim image As List(Of Object) = Items(v("image"))
            Dim surface As List(Of Object) = Items(v("surface"))
            Dim r As List(Of Object) = Items(v("rect"))

            Assert.Equal(New Rectangle(IntOf(r(0)), IntOf(r(1)), IntOf(r(2)), IntOf(r(3))),
                         ZoomMath.PictureBoxZoomBounds(IntOf(image(0)), IntOf(image(1)), New Size(IntOf(surface(0)), IntOf(surface(1)))))
        Next
    End Sub
#End If

    ''' <summary>Rule 3: one pixel IN from the edge, and the whole side when it is under 3 px.</summary>
    <Fact>
    Public Sub Bars_edge_index_rule_3()
        Dim cases As List(Of Object) = Items(Section("bars")("edgeIndex"))
        Assert.Equal(4, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Dim displayed As Integer = IntOf(v("displayed"))
            Assert.Equal(IntOf(v("near")), LetterboxFillMath.EdgeIndex(displayed, False))
            Assert.Equal(IntOf(v("far")), LetterboxFillMath.EdgeIndex(displayed, True))
        Next
    End Sub

    ''' <summary>Rule 3: the surface row's source row is clamped into the displayed image, so the
    ''' list has exactly as many entries as the surface has rows.</summary>
    <Fact>
    Public Sub Bars_edge_source_index_clamps_rule_3()
        Assert.Equal(0, LetterboxFillMath.EdgeSourceIndex(0, 240, 600))
        Assert.Equal(0, LetterboxFillMath.EdgeSourceIndex(239, 240, 600))
        Assert.Equal(0, LetterboxFillMath.EdgeSourceIndex(240, 240, 600))
        Assert.Equal(1, LetterboxFillMath.EdgeSourceIndex(241, 240, 600))
        Assert.Equal(599, LetterboxFillMath.EdgeSourceIndex(839, 240, 600))
        Assert.Equal(599, LetterboxFillMath.EdgeSourceIndex(1079, 240, 600))
    End Sub

    ''' <summary>Rule 4: every 100th entry from 0, or the last alone when that yields none.</summary>
    <Fact>
    Public Sub Bars_stepped_indices_rule_4()
        Dim cases As List(Of Object) = Items(Section("bars")("steppedIndices"))
        Assert.Equal(4, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Dim expected As List(Of Integer) = Items(v("indices")).Select(Function(i) IntOf(i)).ToList()
            Assert.Equal(expected, LetterboxFillMath.SteppedIndices(IntOf(v("count"))))
        Next
    End Sub

    ''' <summary>Rules 4 and 5: the spread, uniform-or-perspective, and the trimmed mean. A flat band
    ''' with one black and one white outlier is still uniform.</summary>
    <Fact>
    Public Sub Bars_spread_mode_and_uniform_colour_rules_4_and_5()
        Dim cases As List(Of Object) = Items(Section("bars")("modes"))
        Assert.Equal(4, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Dim name As String = Convert.ToString(v("name"))
            Dim samples As List(Of Color) = RgbList(v("samples"))

            AssertNear(DblOf(v("spread")), LetterboxFillMath.EdgeSpread(samples), name & ": spread")

            Dim expectedMode As LetterboxFillMath.LetterboxBandMode =
                If(Convert.ToString(v("mode")) = "uniform",
                   LetterboxFillMath.LetterboxBandMode.Uniform,
                   LetterboxFillMath.LetterboxBandMode.Perspective)
            Assert.True(expectedMode = LetterboxFillMath.ModeOf(samples), name & ": mode")

            Assert.Equal(Rgb(v("uniformColor")), LetterboxFillMath.TrimmedMeanColor(samples))
        Next
    End Sub

    ''' <summary>Rule 6: <c>round(n * 0.0006) + 1</c>, half to even - 7500 gives 5, 4167 gives 4.</summary>
    <Fact>
    Public Sub Bars_smooth_radius_rule_6()
        Dim cases As List(Of Object) = Items(Section("bars")("smoothRadius"))
        Assert.Equal(7, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Assert.Equal(IntOf(v("radius")), LetterboxFillMath.SmoothRadius(IntOf(v("count"))))
        Next
    End Sub

    ''' <summary>Rule 6: the box filter - window clipped at the ends, FLOOR mean per channel.</summary>
    <Fact>
    Public Sub Bars_box_filter_rule_6()
        Dim v As Dictionary(Of String, Object) = ObjOf(Section("bars")("smoothed"))
        Dim input As List(Of Color) = RgbList(v("input"))
        Dim expected As List(Of Color) = RgbList(v("output"))

        Assert.Equal(expected, LetterboxFillMath.SmoothColors(input, IntOf(v("radius"))))
    End Sub

    ''' <summary>Rule 6: a list shorter than 3, or a radius under 1, is used as it is.</summary>
    <Fact>
    Public Sub Bars_box_filter_leaves_a_short_list_alone_rule_6()
        Dim two As New List(Of Color) From {Color.FromArgb(255, 10, 20, 30), Color.FromArgb(255, 200, 100, 50)}
        Assert.Same(two, LetterboxFillMath.SmoothColors(two, 4))

        Dim three As New List(Of Color) From {Color.FromArgb(255, 1, 2, 3), Color.FromArgb(255, 4, 5, 6), Color.FromArgb(255, 7, 8, 9)}
        Assert.Same(three, LetterboxFillMath.SmoothColors(three, 0))
    End Sub

    ''' <summary>Rules 5 and 7: the two bars (2 px under the photo) and the two seam bands.</summary>
    <Fact>
    Public Sub Bars_bands_and_seams_rules_5_and_7()
        Dim cases As List(Of Object) = Items(Section("bars")("bands"))
        Assert.Equal(4, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Dim expected As Dictionary(Of String, Object) = ObjOf(v("bands"))
            Dim actual As LetterboxFillMath.LetterboxBands =
                LetterboxFillMath.BarBands(IntOf(v("surface")), IntOf(v("imageStart")), IntOf(v("imageLength")))

            Dim nearBar As List(Of Object) = Items(expected("nearBar"))
            Dim farBar As List(Of Object) = Items(expected("farBar"))
            Dim nearSeam As List(Of Object) = Items(expected("nearSeam"))
            Dim farSeam As List(Of Object) = Items(expected("farSeam"))

            Assert.Equal(IntOf(nearBar(0)), actual.NearBarStart)
            Assert.Equal(IntOf(nearBar(1)), actual.NearBarLength)
            Assert.Equal(IntOf(farBar(0)), actual.FarBarStart)
            Assert.Equal(IntOf(farBar(1)), actual.FarBarLength)
            Assert.Equal(IntOf(nearSeam(0)), actual.NearSeamStart)
            Assert.Equal(IntOf(nearSeam(1)), actual.NearSeamLength)
            Assert.Equal(IntOf(farSeam(0)), actual.FarSeamStart)
            Assert.Equal(IntOf(farSeam(1)), actual.FarSeamLength)
        Next
    End Sub

    ' ------------------------------------------------------------------------ LETTERBOX-HALO --

    ''' <summary>Rule 5: slow 350, medium 175, fast 87.5; missing, empty and corrupt read as medium.</summary>
    <Fact>
    Public Sub Halo_durations_rule_5()
        Dim v As Dictionary(Of String, Object) = ObjOf(Section("halo")("durationsMs"))

        AssertNear(DblOf(v("slow")), LetterboxFillMath.HaloDurationMs("slow"), "slow")
        AssertNear(DblOf(v("medium")), LetterboxFillMath.HaloDurationMs("medium"), "medium")
        AssertNear(DblOf(v("fast")), LetterboxFillMath.HaloDurationMs("fast"), "fast")
        AssertNear(DblOf(v("missing")), LetterboxFillMath.HaloDurationMs(""), "missing")
        AssertNear(DblOf(v("missing")), LetterboxFillMath.HaloDurationMs(Nothing), "missing (Nothing)")
        AssertNear(DblOf(v("unknown")), LetterboxFillMath.HaloDurationMs("turbo"), "unknown")

        ' The stored value is a registry fact: case and padding are not a different setting.
        AssertNear(DblOf(v("slow")), LetterboxFillMath.HaloDurationMs("  SLOW "), "slow, padded")
    End Sub

    ''' <summary>Rule 4: quadratic ease-out, the input clamped to [0, 1].</summary>
    <Fact>
    Public Sub Halo_ease_rule_4()
        Dim cases As List(Of Object) = Items(Section("halo")("ease"))
        Assert.Equal(9, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            AssertNear(DblOf(v("eased")), LetterboxFillMath.HaloEase(DblOf(v("progress"))), "ease(" & Convert.ToString(v("progress")) & ")")
        Next
    End Sub

    ''' <summary>Rule 2: the 12 stops, alpha <c>round(255 * (j/11)^1.4)</c> half to even - 9 at j = 1.</summary>
    <Fact>
    Public Sub Halo_stop_table_rule_2()
        Dim stops As List(Of Object) = Items(Section("halo")("stops"))
        Assert.Equal(12, stops.Count)

        Dim alphas As Integer() = LetterboxFillMath.HaloStopAlphas()
        Assert.Equal(stops.Count, alphas.Length)

        For j As Integer = 0 To stops.Count - 1
            Dim pair As List(Of Object) = Items(stops(j))
            ' The vector prints the distance to 6 decimals.
            AssertNear(DblOf(pair(0)), j / (LetterboxFillMath.BlendStops - 1.0), "stop " & j & " distance")
            Assert.Equal(IntOf(pair(1)), alphas(j))
            Assert.Equal(IntOf(pair(1)), LetterboxFillMath.HaloStopAlpha(j / (LetterboxFillMath.BlendStops - 1.0)))
        Next
    End Sub

    ''' <summary>Rule 2 (informational): the table is within 1.1455 alpha levels of the exact curve.</summary>
    <Fact>
    Public Sub Halo_stop_approximation_error_rule_2()
        AssertNear(DblOf(Section("halo")("stopApproximationMaxError")),
                   LetterboxFillMath.HaloStopApproximationMaxError(),
                   "halo.stopApproximationMaxError")
    End Sub

    ''' <summary>Rule 3: the ramp covers <c>floor(L * e)</c> px next to the photo.</summary>
    <Fact>
    Public Sub Halo_reach_rule_3()
        Dim cases As List(Of Object) = Items(Section("halo")("reach"))
        Assert.Equal(5, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Assert.Equal(IntOf(v("reach")), LetterboxFillMath.HaloReach(IntOf(v("barLength")), DblOf(v("eased"))))
        Next
    End Sub

    ''' <summary>Rules 2 and 3 together: the background alpha of every pixel of a bar, from the photo
    ''' out, at a growth. Pixels at or past the front are solid background.</summary>
    <Fact>
    Public Sub Halo_alpha_rows_rules_2_and_3()
        Dim cases As List(Of Object) = Items(Section("halo")("alphaRows"))
        Assert.Equal(5, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            Dim barLength As Integer = IntOf(v("barLength"))
            Dim eased As Double = DblOf(v("eased"))
            Dim expected As List(Of Object) = Items(v("alpha"))
            Dim actual As Double() = LetterboxFillMath.HaloAlphaRow(barLength, eased)

            Assert.Equal(barLength, expected.Count)
            Assert.Equal(barLength, actual.Length)
            For k As Integer = 0 To barLength - 1
                AssertNear(DblOf(expected(k)), actual(k), "alphaRow(L=" & barLength & ", e=" & eased.ToString(CultureInfo.InvariantCulture) & ")[" & k & "]")
            Next
        Next
    End Sub

    ''' <summary>Rule 7: the previous frame is laid over at <c>1 - e</c>; at <c>e = 1</c> it is gone.</summary>
    <Fact>
    Public Sub Halo_underlay_opacity_rule_7()
        Dim cases As List(Of Object) = Items(Section("halo")("underlayOpacity"))
        Assert.Equal(4, cases.Count)

        For Each node As Object In cases
            Dim v As Dictionary(Of String, Object) = ObjOf(node)
            AssertNear(DblOf(v("opacity")), LetterboxFillMath.UnderlayOpacity(DblOf(v("eased"))), "underlay(" & Convert.ToString(v("eased")) & ")")
        Next
    End Sub

    ''' <summary>Rule 6: the progress values an ideal 15 ms timer produces; the last is exactly 1 and a
    ''' faster speed draws FEWER frames (fast = the start frame plus 6).</summary>
    <Fact>
    Public Sub Halo_frame_schedule_rule_6()
        Dim schedules As Dictionary(Of String, Object) = ObjOf(Section("halo")("frameSchedule"))
        Assert.Equal(3, schedules.Count)

        For Each speed As String In New String() {"slow", "medium", "fast"}
            Dim expected As List(Of Object) = Items(schedules(speed))
            Dim actual As List(Of Double) = LetterboxFillMath.HaloFrameSchedule(LetterboxFillMath.HaloDurationMs(speed))

            Assert.Equal(expected.Count, actual.Count)
            For i As Integer = 0 To expected.Count - 1
                AssertNear(DblOf(expected(i)), actual(i), speed & " frame " & i)
            Next

            Assert.Equal(0.0, actual(0))
            Assert.Equal(1.0, actual(actual.Count - 1))
        Next

        Assert.Equal(7, Items(schedules("fast")).Count)
    End Sub

    ' ----------------------------------------- what the vectors do not number but the code owns --

    ''' <summary>Host conditions (section 2): a slideshow under 5000 ms gets no bars, at or above it
    ''' it does; no slideshow, any interval. And the 50 ms throttle.</summary>
    <Fact>
    Public Sub Host_conditions_slideshow_floor_and_redraw_throttle()
        Assert.Equal(5000, LetterboxFillMath.FastSlideshowIntervalMs)
        Assert.Equal(50, LetterboxFillMath.RedrawThrottleMs)

        Assert.True(LetterboxFillMath.SlideshowAllowsBars(False, 0))
        Assert.True(LetterboxFillMath.SlideshowAllowsBars(False, 100))
        Assert.False(LetterboxFillMath.SlideshowAllowsBars(True, 4999))
        Assert.True(LetterboxFillMath.SlideshowAllowsBars(True, 5000))
        Assert.True(LetterboxFillMath.SlideshowAllowsBars(True, 10000))
    End Sub

    ''' <summary>Rule 6: progress is the clock's, clamped; the frame that reaches 1 is drawn at exactly 1
    ''' and ends the growth; a non-positive duration reads as the slow one.</summary>
    <Fact>
    Public Sub Halo_progress_follows_the_clock_and_ends_on_exactly_one_rule_6()
        Dim finished As Boolean

        Assert.Equal(0.0, LetterboxFillMath.HaloProgress(0.0, 175.0, finished))
        Assert.False(finished)

        AssertNear(0.5, LetterboxFillMath.HaloProgress(87.5, 175.0, finished), "half way")
        Assert.False(finished)

        Assert.Equal(1.0, LetterboxFillMath.HaloProgress(175.0, 175.0, finished))
        Assert.True(finished)

        ' A skipped frame is caught up: the late frame is still exactly 1, never 1.4.
        Assert.Equal(1.0, LetterboxFillMath.HaloProgress(250.0, 175.0, finished))
        Assert.True(finished)

        ' No usable duration: the shipped slow one (350 ms), not a division by zero.
        AssertNear(0.5, LetterboxFillMath.HaloProgress(175.0, 0.0, finished), "zero duration reads as slow")
        Assert.False(finished)
    End Sub

    ''' <summary>The production wrapper: the stored speed key goes through the setting's own reader,
    ''' so the three enum values give the vectors' three durations - and an unreadable stored value
    ''' lands on the medium one (the same answer <c>HaloAnimationSpeedTests</c> pins for the enum).</summary>
    <Fact>
    Public Sub The_speed_setting_gives_the_vectors_durations()
        Dim v As Dictionary(Of String, Object) = ObjOf(Section("halo")("durationsMs"))

        AssertNear(DblOf(v("slow")), LetterboxFillMath.HaloDurationMs(HaloSpeedToSetting(HaloAnimationSpeed.Slow)), "enum slow")
        AssertNear(DblOf(v("medium")), LetterboxFillMath.HaloDurationMs(HaloSpeedToSetting(HaloAnimationSpeed.Medium)), "enum medium")
        AssertNear(DblOf(v("fast")), LetterboxFillMath.HaloDurationMs(HaloSpeedToSetting(HaloAnimationSpeed.Fast)), "enum fast")

        For Each stored As String In New String() {Nothing, "", "   ", "350", "mediuim"}
            AssertNear(DblOf(v("medium")),
                       LetterboxFillMath.HaloDurationMs(HaloSpeedToSetting(HaloSpeedFromSetting(stored))),
                       "stored '" & stored & "'")
        Next
    End Sub

    ''' <summary>The numbers live ONCE. The three files that paint the bars and the halo, and the
    ''' sampler that reads the edge, must not grow a private copy of a contract number or a rule's
    ''' arithmetic again - the way these identifiers were what the audit of 2026-10-07 found.</summary>
    <Fact>
    Public Sub The_painting_sources_hold_no_private_copy_of_a_contract_number()
        Dim root As String = FindRepoRoot()
        Assert.True(root IsNot Nothing, "FastMediaSorter.sln not found above " & AppContext.BaseDirectory)

        Dim forbidden As String() = {
            "halo_Falloff_Gamma", "halo_Blend_Stops", "halo_Duration_Slow_Ms", "halo_Frame_Interval_Ms",
            "percent_of_color_smooth_To_Remove", "percent_of_color_deviation", "step_size_while_color_Search",
            "smoothIndex", "edgeOverlap", "color_deviation_threshold",
            "Math.Pow(", "Math.Floor(", ".OrderBy(", "SmoothColorList", "GetTrimmedAverageColor",
            "CheckCornerColorsAndSetBeginPoint", "GetSteppedColors"
        }

        For Each relative As String In New String() {
            "src\Main_Form.PerspectiveBackground.vb",
            "src\Main_Form.PerspectiveHalo.vb",
            "src\HaloPainter.vb",
            "src\Imaging\EdgeSampler.vb"
        }
            Dim path As String = System.IO.Path.Combine(root, relative)
            Assert.True(File.Exists(path), "missing " & relative)

            For Each line As String In File.ReadAllLines(path)
                Dim code As String = line
                Dim apostrophe As Integer = code.IndexOf("'"c)
                If apostrophe >= 0 Then code = code.Substring(0, apostrophe)

                For Each token As String In forbidden
                    ' EdgeSampler.CalculateBackgroundColor (the dynamic colour) legitimately rounds
                    ' and averages with its own constants; only the letterbox names are held.
                    If relative.EndsWith("EdgeSampler.vb", StringComparison.Ordinal) AndAlso
                       (token = "Math.Floor(" OrElse token = "Math.Pow(") Then Continue For

                    Assert.False(code.Contains(token), relative & " holds '" & token & "': " & line.Trim())
                Next
            Next
        Next
    End Sub

    Private Shared Function FindRepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(System.IO.Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
