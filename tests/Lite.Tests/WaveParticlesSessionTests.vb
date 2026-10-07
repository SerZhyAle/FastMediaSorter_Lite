#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The "particles and lines" backdrop against the WAVE-PARTICLES contract (ticket 029): rung 1 of
''' section 7 made mechanical (every section 3 value asserted by name), and the rules a viewer would
''' otherwise have to notice by eye - the clock is real time (2), a fresh session ramps in (8), a
''' resize keeps the session (12), the still frame is the settled one (9), nothing draws brighter than
''' section 3 (16) and the constants live in one cited home (18).
''' Modern-only like the feature: both linked sources are whole-file "#If Not NETFRAMEWORK", so this
''' file compiles to nothing on the net48 leg.
''' </summary>
Public Class WaveParticlesSessionTests

    Private Const Tolerance As Double = 0.000000001

    Private Shared Function NewSession(Optional seed As Integer = 7, Optional width As Integer = 800, Optional height As Integer = 600,
                                       Optional light As Boolean = False) As WaveParticlesSession
        Return New WaveParticlesSession(New Random(seed), width, height, light)
    End Function

    ' --- rung 1: section 3, value by value -------------------------------------------------------

    <Fact>
    Public Sub Section_3_1_clock_values()
        Assert.Equal("0.15", WaveParticlesConstants.ContractVersion)
        Assert.Equal(1.0R / 60.0R, WaveParticlesConstants.ReferenceFrameSeconds, 12)
        Assert.Equal(0.002R, WaveParticlesConstants.TimeStep)
        Assert.Equal(36, WaveParticlesConstants.RampFrames)
    End Sub

    <Fact>
    Public Sub Section_3_2_session_roll_values()
        Assert.Equal(5, WaveParticlesConstants.LineCountMin)
        Assert.Equal(12, WaveParticlesConstants.LineCountMax)
        Assert.Equal(15, WaveParticlesConstants.ParticleCountMin)
        Assert.Equal(55, WaveParticlesConstants.ParticleCountMax)
        Assert.Equal(20.0R, WaveParticlesConstants.SampleStep)
        Assert.Equal(0.8R, WaveParticlesConstants.SampleStepJitterMin)
        Assert.Equal(1.2R, WaveParticlesConstants.SampleStepJitterMax)
        Assert.Equal(3.0R, WaveParticlesConstants.StrokeWidthMin)
        Assert.Equal(6.0R, WaveParticlesConstants.StrokeWidthMax)
        Assert.Equal(0.28R, WaveParticlesConstants.AmplitudeFractionMin)
        Assert.Equal(0.48R, WaveParticlesConstants.AmplitudeFractionMax)
        Assert.Equal(0.5R, WaveParticlesConstants.SpeedMultiplierMin)
        Assert.Equal(1.5R, WaveParticlesConstants.SpeedMultiplierMax)
        Assert.Equal(1.0R, WaveParticlesConstants.ParticleRadiusMin)
        Assert.Equal(6.0R, WaveParticlesConstants.ParticleRadiusMax)
        Assert.Equal(0.12R, WaveParticlesConstants.ParticleSpeedBase)
        Assert.Equal(0.42R, WaveParticlesConstants.ParticleSpeedRange)
        Assert.Equal(0.18R, WaveParticlesConstants.CounterDriftProbability)
        Assert.Equal(-0.35R, WaveParticlesConstants.CounterDriftFactor)
        Assert.Equal(0.28R, WaveParticlesConstants.ParticleJitter)
    End Sub

    <Fact>
    Public Sub Section_3_3_dynamic_palette_values()
        Assert.Equal(8.0R, WaveParticlesConstants.LineHueStepMin)
        Assert.Equal(20.0R, WaveParticlesConstants.LineHueStepMax)
        Assert.Equal(108.0R, WaveParticlesConstants.DynamicParticleSpread)
    End Sub

    <Fact>
    Public Sub Section_3_4_frame_values()
        Assert.Equal(10, WaveParticlesConstants.WashDarkChannel)
        Assert.Equal(245, WaveParticlesConstants.WashLightChannel)
        Assert.Equal(38.0R / 255.0R, WaveParticlesConstants.WashAlpha, 12)
        Assert.Equal(0.8R, WaveParticlesConstants.LineSaturation)
        Assert.Equal(0.65R, WaveParticlesConstants.LineLightnessDark)
        Assert.Equal(0.35R, WaveParticlesConstants.LineLightnessLight)
        Assert.Equal(0.9R, WaveParticlesConstants.ParticleSaturation)
        Assert.Equal(0.7R, WaveParticlesConstants.ParticleLightnessDark)
        Assert.Equal(0.3R, WaveParticlesConstants.ParticleLightnessLight)
        Assert.Equal(0.28R, WaveParticlesConstants.LineOpacityBase)
        Assert.Equal(0.16R, WaveParticlesConstants.LineOpacityGain)
        Assert.Equal(0.38R, WaveParticlesConstants.ParticleOpacityBase)
        Assert.Equal(0.32R, WaveParticlesConstants.ParticleOpacityGain)
        Assert.Equal(0.7R, WaveParticlesConstants.OpacityScale)
        Assert.Equal(0.45R, WaveParticlesConstants.CentreDriftRate)
        Assert.Equal(0.02R, WaveParticlesConstants.CentreDriftFraction)
        Assert.Equal(0.038R, WaveParticlesConstants.LaneFraction)
        Assert.Equal(6.0R, WaveParticlesConstants.SpanExtraSteps)
        Assert.Equal(0.0105R, WaveParticlesConstants.SpatialFrequency)
        Assert.Equal(0.8R, WaveParticlesConstants.LanePhaseStep)
        Assert.Equal(0.4R, WaveParticlesConstants.EnvelopeBase)
        Assert.Equal(0.6R, WaveParticlesConstants.EnvelopeGain)
        Assert.Equal(0.4R, WaveParticlesConstants.EnvelopeRate)
        Assert.Equal(0.2R, WaveParticlesConstants.EnvelopeLaneStep)
        Assert.Equal(0.35R, WaveParticlesConstants.RampGainStart)
    End Sub

    ' --- section 3.2: what a session rolls ---------------------------------------------------------

    <Fact>
    Public Sub Rolls_stay_inside_their_ranges_over_many_seeds()
        Dim minLines As Integer = Integer.MaxValue, maxLines As Integer = 0
        Dim minParticles As Integer = Integer.MaxValue, maxParticles As Integer = 0
        For seed As Integer = 1 To 600
            Dim s As WaveParticlesSession = NewSession(seed)
            Assert.InRange(s.LineCount, WaveParticlesConstants.LineCountMin, WaveParticlesConstants.LineCountMax)
            Assert.InRange(s.Particles.Length, WaveParticlesConstants.ParticleCountMin, WaveParticlesConstants.ParticleCountMax)
            Assert.InRange(s.StepPx, WaveParticlesConstants.SampleStep * WaveParticlesConstants.SampleStepJitterMin, WaveParticlesConstants.SampleStep * WaveParticlesConstants.SampleStepJitterMax)
            Assert.InRange(s.StrokeWidth, WaveParticlesConstants.StrokeWidthMin, WaveParticlesConstants.StrokeWidthMax)
            Assert.InRange(s.AmplitudeFraction, WaveParticlesConstants.AmplitudeFractionMin, WaveParticlesConstants.AmplitudeFractionMax)
            Assert.InRange(s.SpeedMultiplier, WaveParticlesConstants.SpeedMultiplierMin, WaveParticlesConstants.SpeedMultiplierMax)
            Assert.InRange(s.LineHueStep, WaveParticlesConstants.LineHueStepMin, WaveParticlesConstants.LineHueStepMax)
            Assert.InRange(s.LineBaseHue, 0.0R, 360.0R)
            ' d is a unit vector and n is its normal.
            Assert.Equal(1.0R, s.DirectionX * s.DirectionX + s.DirectionY * s.DirectionY, 9)
            Assert.Equal(0.0R, s.DirectionX * s.NormalX + s.DirectionY * s.NormalY, 9)
            For Each p In s.Particles
                Assert.InRange(p.Radius, WaveParticlesConstants.ParticleRadiusMin, WaveParticlesConstants.ParticleRadiusMax)
                Assert.InRange(p.Hue, 0.0R, 360.0R)
                Assert.InRange(p.X, 0.0R, 800.0R)
                Assert.InRange(p.Y, 0.0R, 600.0R)
            Next
            minLines = Math.Min(minLines, s.LineCount) : maxLines = Math.Max(maxLines, s.LineCount)
            minParticles = Math.Min(minParticles, s.Particles.Length) : maxParticles = Math.Max(maxParticles, s.Particles.Length)
        Next
        ' Integer ranges are inclusive at both ends: both ends occur.
        Assert.Equal(WaveParticlesConstants.LineCountMin, minLines)
        Assert.Equal(WaveParticlesConstants.LineCountMax, maxLines)
        Assert.Equal(WaveParticlesConstants.ParticleCountMin, minParticles)
        Assert.Equal(WaveParticlesConstants.ParticleCountMax, maxParticles)
    End Sub

    <Fact>
    Public Sub Two_sessions_differ_in_direction_and_colour()
        Dim a As WaveParticlesSession = NewSession(1)
        Dim b As WaveParticlesSession = NewSession(2)
        Assert.NotEqual(a.DirectionX, b.DirectionX)
        Assert.NotEqual(a.LineBaseHue, b.LineBaseHue)
    End Sub

    ' --- rule 2: the clock is real time --------------------------------------------------------------

    <Theory>
    <InlineData(30)>
    <InlineData(60)>
    <InlineData(120)>
    <InlineData(144)>
    Public Sub One_second_is_the_same_second_at_any_refresh_rate(framesPerSecond As Integer)
        Dim s As WaveParticlesSession = NewSession()
        Dim retention As Double = 1.0R
        Dim step1 As Double = WaveParticlesSession.FramesIn(1.0R / framesPerSecond)
        For i As Integer = 1 To framesPerSecond
            retention *= 1.0R - s.Advance(step1)
        Next
        ' 0.12 per second at speed scale 1 (section 3.1).
        Assert.Equal(0.12R, s.Phase, 9)
        ' The ramp is over after 36 reference frames - 0.6 s - at every rate (section 4 as amended in 0.10).
        Assert.Equal(CDbl(WaveParticlesConstants.RampFrames), s.FramesDrawn, 9)
        ' The trail keeps exactly what 60 washes at WASH_ALPHA keep.
        Assert.Equal(Math.Pow(1.0R - WaveParticlesConstants.WashAlpha, 60.0R), retention, 9)
    End Sub

    <Fact>
    Public Sub Wash_alpha_is_exactly_WASH_ALPHA_at_one_reference_frame()
        Assert.Equal(38.0R / 255.0R, NewSession().Advance(1.0R), 12)
    End Sub

    <Fact>
    Public Sub A_step_never_exceeds_the_cap_and_a_bad_step_is_no_step()
        Dim s As WaveParticlesSession = NewSession()
        s.Advance(600.0R)    ' ten seconds on the clock: a stall, a debugger, a resume that was not reset
        Assert.Equal(WaveParticlesConstants.MaxFramesPerTick * WaveParticlesConstants.TimeStep, s.Phase, 12)
        Dim before As Double = s.Phase
        Assert.Equal(0.0R, s.Advance(-5.0R), 12)
        Assert.Equal(0.0R, s.Advance(Double.NaN), 12)
        Assert.Equal(before, s.Phase, 12)
    End Sub

    ' --- rules 8 and 16 -----------------------------------------------------------------------------

    <Fact>
    Public Sub The_ramp_starts_at_035_and_settles_at_1()
        Dim s As WaveParticlesSession = NewSession()
        Assert.Equal(0.35R, s.Gain, 12)
        Dim last As Double = s.Gain
        For i As Integer = 1 To WaveParticlesConstants.RampFrames
            s.Advance(1.0R)
            Assert.True(s.Gain >= last, "the gain never falls during the ramp")
            last = s.Gain
        Next
        Assert.Equal(1.0R, s.Gain, 12)
        s.Advance(1.0R)
        Assert.Equal(1.0R, s.Gain, 12)
    End Sub

    <Fact>
    Public Sub Settled_opacities_are_the_derived_values_and_nothing_ever_exceeds_them()
        Dim s As WaveParticlesSession = NewSession()
        Dim lineCeiling As Double = (0.28R + 0.16R) * 0.7R       ' 0.308
        Dim particleCeiling As Double = (0.38R + 0.32R) * 0.7R   ' 0.490
        For i As Integer = 0 To 60
            Assert.True(s.LineOpacity <= lineCeiling + Tolerance, "line opacity exceeds section 3.4")
            Assert.True(s.ParticleOpacity <= particleCeiling + Tolerance, "particle opacity exceeds section 3.4")
            s.Advance(1.0R)
        Next
        Assert.Equal(0.308R, s.LineOpacity, 3)
        Assert.Equal(0.49R, s.ParticleOpacity, 3)
    End Sub

    ' --- rule 7: a light surface mirrors lightness, never hue -------------------------------------------

    <Fact>
    Public Sub A_light_surface_changes_the_wash_and_the_lightness_but_not_a_hue()
        Dim s As WaveParticlesSession = NewSession()
        For i As Integer = 1 To WaveParticlesConstants.RampFrames : s.Advance(1.0R) : Next
        Assert.Equal(Color.FromArgb(10, 10, 10), s.WashColour)
        Dim darkLine As Color = s.LineColour(0)
        Dim darkParticle As Color = s.ParticleColour(0)

        s.Light = True
        Assert.Equal(Color.FromArgb(245, 245, 245), s.WashColour)
        Dim lightLine As Color = s.LineColour(0)
        Assert.InRange(Math.Abs(darkLine.GetHue() - lightLine.GetHue()), 0.0R, 2.0R)   ' 8-bit channels: a degree or two of rounding
        Assert.InRange(Math.Abs(darkParticle.GetHue() - s.ParticleColour(0).GetHue()), 0.0R, 2.0R)
        Assert.True(lightLine.GetBrightness() < darkLine.GetBrightness(), "light surface = darker ink")
        Assert.Equal(darkLine.A, lightLine.A)
    End Sub

    <Fact>
    Public Sub Hsl_conversion_hits_the_primaries()
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), WaveParticlesSession.HslColour(0.0R, 1.0R, 0.5R, 1.0R))
        Assert.Equal(Color.FromArgb(255, 0, 255, 0), WaveParticlesSession.HslColour(120.0R, 1.0R, 0.5R, 1.0R))
        Assert.Equal(Color.FromArgb(255, 0, 0, 255), WaveParticlesSession.HslColour(240.0R, 1.0R, 0.5R, 1.0R))
        Assert.Equal(Color.FromArgb(128, 255, 255, 255), WaveParticlesSession.HslColour(0.0R, 0.0R, 1.0R, 0.5R))
    End Sub

    ' --- rule 12: a resize keeps the session --------------------------------------------------------------

    <Fact>
    Public Sub Resize_keeps_every_roll_and_the_ramp_and_puts_the_particles_inside()
        Dim s As WaveParticlesSession = NewSession(11, 800, 600)
        For i As Integer = 1 To 20 : s.Advance(1.0R) : Next
        Dim framesDrawn As Double = s.FramesDrawn
        Dim phase As Double = s.Phase
        Dim lines As Integer = s.LineCount, stroke As Double = s.StrokeWidth, dx As Double = s.DirectionX
        Dim hue As Double = s.LineBaseHue, count As Integer = s.Particles.Length

        s.Resize(320, 180)

        Assert.Equal(framesDrawn, s.FramesDrawn)
        Assert.Equal(phase, s.Phase)
        Assert.Equal(lines, s.LineCount)
        Assert.Equal(stroke, s.StrokeWidth)
        Assert.Equal(dx, s.DirectionX)
        Assert.Equal(hue, s.LineBaseHue)
        Assert.Equal(count, s.Particles.Length)
        Assert.Equal(320, s.Width)
        Assert.Equal(180, s.Height)
        For Each p In s.Particles
            Assert.InRange(p.X, 0.0R, 320.0R)
            Assert.InRange(p.Y, 0.0R, 180.0R)
        Next
    End Sub

    ' --- rule 9: the still frame is the settled one ------------------------------------------------------------

    <Fact>
    Public Sub The_still_frame_is_exactly_36_passes_of_one_reference_frame_and_ends_settled()
        Dim s As WaveParticlesSession = NewSession()
        s.Advance(3.0R)   ' a session already under way: the still frame starts over from the beginning
        Dim passes As Integer = 0
        Dim alphas As New List(Of Double)()
        s.SettledFrame(Sub(washAlpha As Double)
                           passes += 1
                           alphas.Add(washAlpha)
                       End Sub)
        Assert.Equal(36, passes)
        Assert.All(alphas, Sub(a) Assert.Equal(38.0R / 255.0R, a, 12))
        Assert.Equal(1.0R, s.Gain, 12)
        Assert.Equal(CDbl(WaveParticlesConstants.RampFrames), s.FramesDrawn, 12)
        Assert.Equal(36 * WaveParticlesConstants.TimeStep, s.Phase, 12)
    End Sub

    ' --- section 4 as amended in 0.12: a particle is never left outside ---------------------------------------

    <Fact>
    Public Sub Particles_stay_inside_after_frame_hitches()
        Dim s As WaveParticlesSession = NewSession(5, 200, 100)
        Dim rng As New Random(99)
        For i As Integer = 1 To 600
            ' Mostly single frames, now and then the longest step a stall is allowed to be.
            s.Advance(If(rng.NextDouble() < 0.1R, WaveParticlesConstants.MaxFramesPerTick, 0.5R + rng.NextDouble()))
            For Each p In s.Particles
                Assert.InRange(p.X, 0.0R, 200.0R)
                Assert.InRange(p.Y, 0.0R, 100.0R)
            Next
        Next
    End Sub

    <Fact>
    Public Sub A_lane_is_sampled_across_the_whole_span()
        Dim s As WaveParticlesSession = NewSession(3, 800, 600)
        Dim span As Double = Math.Sqrt(800.0R * 800.0R + 600.0R * 600.0R) + 6.0R * s.StepPx
        Dim expected As Integer = CInt(Math.Floor(span / s.StepPx)) + 1
        For lane As Integer = 0 To s.LineCount - 1
            Assert.InRange(s.LinePoints(lane).Length, expected - 1, expected + 1)
        Next
    End Sub

    ' --- rule 18: one cited home ----------------------------------------------------------------------------

    <Fact>
    Public Sub The_visualiser_file_holds_no_section_3_literal_and_cites_the_contract()
        Dim sourceDir As String = FindSourceDir()
        Assert.False(sourceDir Is Nothing, "src/ not found above " & AppContext.BaseDirectory)
        Dim text As String = IO.File.ReadAllText(Path.Combine(sourceDir, "Main_Form.AudioView.vb"))

        ' Distinctive numbers of sections 3.1-3.4: the file may draw them, never define them.
        Dim forbidden() As String = {"0.0105", "0.038", "0.003", "108.0", "0.38R", "0.32R", "0.16R", "0.28R", "0.48",
                                     "0.45F", "0.35F", "0.65F", "36.0F", "Math.Min(36", "0.18R", "0.42R"}
        For Each literal As String In forbidden
            Assert.False(text.Contains(literal), "Main_Form.AudioView.vb defines a contract number again: " & literal)
        Next
        Assert.Contains("WAVE-PARTICLES", text)
        Assert.DoesNotContain("parameter-for-parameter", text)

        Dim constants As String = IO.File.ReadAllText(Path.Combine(sourceDir, "Backdrop", "WaveParticlesConstants.vb"))
        For Each section As String In {"section 3.1", "section 3.2", "section 3.3", "section 3.4"}
            Assert.True(Regex.IsMatch(constants, "WAVE-PARTICLES " & Regex.Escape(section)), "the constants file does not cite " & section)
        Next
    End Sub

    Private Shared Function FindSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso IO.File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Return Nothing
    End Function
End Class
#End If
