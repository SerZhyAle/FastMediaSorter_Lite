#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing

''' <summary>
''' One playback session of the "particles and lines" backdrop: the rolls of WAVE-PARTICLES section
''' 3.2 and the state and arithmetic of section 4 - and nothing else. No Graphics, no control, no
''' clock: the host hands in elapsed reference frames and draws what this class hands back, which is
''' what lets rules 2, 8, 9, 12 and 16 be proven in tests instead of by eye (ticket 029 F-1).
'''
''' Order of one frame, as the contract writes it: <see cref="Advance"/> (clock, ramp, particles, wash
''' alpha), then the host washes the buffer, strokes <see cref="LinePoints"/> lane by lane in
''' <see cref="LineColour"/>, and fills <see cref="Particles"/> in <see cref="ParticleColour"/>.
''' </summary>
Friend NotInheritable Class WaveParticlesSession

    ''' <summary>One particle. Doubles throughout, like the reference implementation.</summary>
    Friend Structure Particle
        Public X As Double
        Public Y As Double
        Public Vx As Double
        Public Vy As Double
        Public Radius As Double
        Public Hue As Double
    End Structure

    Private ReadOnly random As Random

    ' --- rolled once per session (rule 3) ------------------------------------------------------
    Friend ReadOnly DirectionX As Double
    Friend ReadOnly DirectionY As Double
    Friend ReadOnly NormalX As Double
    Friend ReadOnly NormalY As Double
    Friend ReadOnly LineCount As Integer
    Friend ReadOnly StepPx As Double
    Friend ReadOnly StrokeWidth As Double
    Friend ReadOnly AmplitudeFraction As Double
    Friend ReadOnly SpeedMultiplier As Double
    Friend ReadOnly LineBaseHue As Double
    Friend ReadOnly LineHueStep As Double
    Friend ReadOnly ParticleHueBase As Double
    Friend ReadOnly Particles As Particle()

    ' --- running state (section 4) -------------------------------------------------------------
    ''' <summary>The phase t.</summary>
    Friend Property Phase As Double
    ''' <summary>f: reference frames drawn since the session started, capped at RAMP_FRAMES.</summary>
    Friend Property FramesDrawn As Double
    Friend Property Width As Integer
    Friend Property Height As Integer
    ''' <summary>Light surface (rule 7): moves the wash and the lightness of lines and particles,
    ''' never a hue.</summary>
    Friend Property Light As Boolean

    Friend Sub New(random As Random, width As Integer, height As Integer, light As Boolean)
        Me.random = If(random, New Random())
        Me.Width = Math.Max(0, width)
        Me.Height = Math.Max(0, height)
        Me.Light = light

        Dim theta As Double = Uniform(0.0R, 360.0R) * Math.PI / 180.0R
        DirectionX = Math.Cos(theta) : DirectionY = Math.Sin(theta)
        NormalX = -Math.Sin(theta) : NormalY = Math.Cos(theta)
        LineCount = IntegerBetween(WaveParticlesConstants.LineCountMin, WaveParticlesConstants.LineCountMax)
        StepPx = WaveParticlesConstants.SampleStep * Uniform(WaveParticlesConstants.SampleStepJitterMin, WaveParticlesConstants.SampleStepJitterMax)
        StrokeWidth = Uniform(WaveParticlesConstants.StrokeWidthMin, WaveParticlesConstants.StrokeWidthMax)
        AmplitudeFraction = Uniform(WaveParticlesConstants.AmplitudeFractionMin, WaveParticlesConstants.AmplitudeFractionMax)
        Dim particleCount As Integer = IntegerBetween(WaveParticlesConstants.ParticleCountMin, WaveParticlesConstants.ParticleCountMax)
        SpeedMultiplier = Uniform(WaveParticlesConstants.SpeedMultiplierMin, WaveParticlesConstants.SpeedMultiplierMax)
        ' Palette DYNAMIC (section 3.3).
        LineBaseHue = Uniform(0.0R, 360.0R)
        LineHueStep = Uniform(WaveParticlesConstants.LineHueStepMin, WaveParticlesConstants.LineHueStepMax)
        ParticleHueBase = Uniform(0.0R, 360.0R)

        Particles = New Particle(particleCount - 1) {}
        For i As Integer = 0 To particleCount - 1
            Particles(i) = RollParticle()
        Next
        SeedPositions()
    End Sub

    Private Function RollParticle() As Particle
        Dim along As Double = (WaveParticlesConstants.ParticleSpeedBase + Uniform(0.0R, WaveParticlesConstants.ParticleSpeedRange)) * SpeedMultiplier
        If Uniform(0.0R, 1.0R) < WaveParticlesConstants.CounterDriftProbability Then along *= WaveParticlesConstants.CounterDriftFactor
        Dim jitterX As Double = (Uniform(0.0R, 1.0R) - 0.5R) * WaveParticlesConstants.ParticleJitter * SpeedMultiplier
        Dim jitterY As Double = (Uniform(0.0R, 1.0R) - 0.5R) * WaveParticlesConstants.ParticleJitter * SpeedMultiplier
        Return New Particle With {
            .Radius = Uniform(WaveParticlesConstants.ParticleRadiusMin, WaveParticlesConstants.ParticleRadiusMax),
            .Vx = DirectionX * along + jitterX,
            .Vy = DirectionY * along + jitterY,
            .Hue = HueOf(ParticleHueBase + (Uniform(0.0R, 1.0R) - 0.5R) * WaveParticlesConstants.DynamicParticleSpread)}
    End Function

    Private Sub SeedPositions()
        For i As Integer = 0 To Particles.Length - 1
            Particles(i).X = Uniform(0.0R, Width)
            Particles(i).Y = Uniform(0.0R, Height)
        Next
    End Sub

    Private Function Uniform(low As Double, high As Double) As Double
        Return low + random.NextDouble() * (high - low)
    End Function

    Private Function IntegerBetween(low As Integer, high As Integer) As Integer
        Return low + CInt(Math.Floor(random.NextDouble() * (high - low + 1)))
    End Function

    Friend Shared Function HueOf(value As Double) As Double
        Return ((value Mod 360.0R) + 360.0R) Mod 360.0R
    End Function

    ' --- section 4 -----------------------------------------------------------------------------

    ''' <summary>The ramp gain g for the frames drawn so far (rule 8): 0.35 rising to 1.</summary>
    Friend ReadOnly Property Gain As Double
        Get
            Dim p As Double = FramesDrawn / WaveParticlesConstants.RampFrames
            Return WaveParticlesConstants.RampGainStart + (1.0R - WaveParticlesConstants.RampGainStart) * p * (2.0R - p)
        End Get
    End Property

    ''' <summary>Opacity of every line this frame, rule 16 included.</summary>
    Friend ReadOnly Property LineOpacity As Double
        Get
            Return (WaveParticlesConstants.LineOpacityBase + WaveParticlesConstants.LineOpacityGain * Gain) * WaveParticlesConstants.OpacityScale
        End Get
    End Property

    Friend ReadOnly Property ParticleOpacity As Double
        Get
            Return (WaveParticlesConstants.ParticleOpacityBase + WaveParticlesConstants.ParticleOpacityGain * Gain) * WaveParticlesConstants.OpacityScale
        End Get
    End Property

    ''' <summary>The colour the buffer is washed with, and the one a fresh or still frame starts from.</summary>
    Friend ReadOnly Property WashColour As Color
        Get
            Dim c As Integer = If(Light, WaveParticlesConstants.WashLightChannel, WaveParticlesConstants.WashDarkChannel)
            Return Color.FromArgb(c, c, c)
        End Get
    End Property

    ''' <summary>
    ''' One frame covering <paramref name="referenceFrames"/> reference frames (k): advances the clock,
    ''' the ramp and every particle (rule 2), and returns the wash alpha 1 - (1 - WASH_ALPHA)^k that
    ''' leaves what k washes at WASH_ALPHA would leave (section 4 as amended in 0.10). A step is clamped
    ''' to <see cref="WaveParticlesConstants.MaxFramesPerTick"/>, so a stall is not replayed.
    ''' </summary>
    Friend Function Advance(referenceFrames As Double) As Double
        Dim k As Double = ClampFrames(referenceFrames)
        Phase += WaveParticlesConstants.TimeStep * k
        FramesDrawn = Math.Min(FramesDrawn + k, CDbl(WaveParticlesConstants.RampFrames))
        Dim w As Double = Width
        Dim h As Double = Height
        For i As Integer = 0 To Particles.Length - 1
            Dim p As Particle = Particles(i)
            p.X += p.Vx * k
            p.Y += p.Vy * k
            ' Section 4 as amended in 0.12: an overshoot is mirrored back across the edge crossed and
            ' the velocity turned inward, so after every step the particle is inside the buffer whatever
            ' k was - a flip while still outside could oscillate beyond the edge for minutes.
            If p.X < 0 Then p.X = Math.Min(-p.X, w) : p.Vx = Math.Abs(p.Vx)
            If p.X > w Then p.X = Math.Max(2.0R * w - p.X, 0.0R) : p.Vx = -Math.Abs(p.Vx)
            If p.Y < 0 Then p.Y = Math.Min(-p.Y, h) : p.Vy = Math.Abs(p.Vy)
            If p.Y > h Then p.Y = Math.Max(2.0R * h - p.Y, 0.0R) : p.Vy = -Math.Abs(p.Vy)
            Particles(i) = p
        Next
        Return 1.0R - Math.Pow(1.0R - WaveParticlesConstants.WashAlpha, k)
    End Function

    Friend Shared Function ClampFrames(referenceFrames As Double) As Double
        If Double.IsNaN(referenceFrames) OrElse referenceFrames < 0.0R Then Return 0.0R
        Return Math.Min(referenceFrames, WaveParticlesConstants.MaxFramesPerTick)
    End Function

    ''' <summary>Reference frames in an elapsed span, the unit the whole contract counts in.</summary>
    Friend Shared Function FramesIn(elapsedSeconds As Double) As Double
        Return elapsedSeconds / WaveParticlesConstants.ReferenceFrameSeconds
    End Function

    ''' <summary>The points of lane <paramref name="lane"/> along the session's direction, in buffer
    ''' pixels: sampled every <see cref="StepPx"/> across the path span, centred on the drifting centre.</summary>
    Friend Function LinePoints(lane As Integer) As PointF()
        Dim w As Double = Width
        Dim h As Double = Height
        Dim small As Double = Math.Min(w, h)
        Dim drift As Double = Math.Sin(WaveParticlesConstants.CentreDriftRate * Phase) * WaveParticlesConstants.CentreDriftFraction * small
        Dim cx As Double = w / 2.0R + DirectionX * drift
        Dim cy As Double = h / 2.0R + DirectionY * drift
        Dim laneSpacing As Double = WaveParticlesConstants.LaneFraction * small
        Dim span As Double = Math.Sqrt(w * w + h * h) + WaveParticlesConstants.SpanExtraSteps * StepPx
        Dim band As Double = (lane - (LineCount - 1) / 2.0R) * laneSpacing
        Dim envelope As Double = WaveParticlesConstants.EnvelopeBase + WaveParticlesConstants.EnvelopeGain *
            Math.Abs(Math.Sin(WaveParticlesConstants.EnvelopeRate * Phase + WaveParticlesConstants.EnvelopeLaneStep * lane))
        Dim amplitude As Double = h * AmplitudeFraction * Gain * envelope

        Dim points As New List(Of PointF)()
        Dim s As Double = -span / 2.0R
        While s <= span / 2.0R
            Dim offset As Double = band + Math.Sin(WaveParticlesConstants.SpatialFrequency * s + Phase + WaveParticlesConstants.LanePhaseStep * lane) * amplitude
            points.Add(New PointF(CSng(cx + DirectionX * s + NormalX * offset), CSng(cy + DirectionY * s + NormalY * offset)))
            s += StepPx
        End While
        Return points.ToArray()
    End Function

    Friend Function LineColour(lane As Integer) As Color
        Return HslColour(HueOf(LineBaseHue + lane * LineHueStep), WaveParticlesConstants.LineSaturation,
                         If(Light, WaveParticlesConstants.LineLightnessLight, WaveParticlesConstants.LineLightnessDark), LineOpacity)
    End Function

    Friend Function ParticleColour(index As Integer) As Color
        Return HslColour(Particles(index).Hue, WaveParticlesConstants.ParticleSaturation,
                         If(Light, WaveParticlesConstants.ParticleLightnessLight, WaveParticlesConstants.ParticleLightnessDark), ParticleOpacity)
    End Function

    ' --- rules 9 and 12 --------------------------------------------------------------------------

    ''' <summary>
    ''' Rule 12: a size change keeps every roll and the ramp counter; only the particle positions are
    ''' re-seeded inside the new bounds. (The host carries the old buffer, stretched, into the new one.)
    ''' </summary>
    Friend Sub Resize(width As Integer, height As Integer)
        Me.Width = Math.Max(0, width)
        Me.Height = Math.Max(0, height)
        SeedPositions()
    End Sub

    ''' <summary>
    ''' Rule 9: the frame the animation would have reached after RAMP_FRAMES frames. The session goes
    ''' back to the start, its particles are re-seeded, and exactly RAMP_FRAMES passes of k = 1 run;
    ''' <paramref name="drawPass"/> is called after each with that pass's wash alpha, and the host draws
    ''' into a buffer it has already filled with <see cref="WashColour"/> at full opacity.
    ''' </summary>
    Friend Sub SettledFrame(drawPass As Action(Of Double))
        Phase = 0.0R
        FramesDrawn = 0.0R
        SeedPositions()
        For i As Integer = 1 To WaveParticlesConstants.RampFrames
            Dim washAlpha As Double = Advance(1.0R)
            If drawPass IsNot Nothing Then drawPass(washAlpha)
        Next
    End Sub

    ''' <summary>HSL (degrees, 0..1, 0..1) with an opacity in 0..1 - the one conversion every surface of
    ''' the contract uses.</summary>
    Friend Shared Function HslColour(hue As Double, saturation As Double, lightness As Double, alpha As Double) As Color
        Dim c As Double = (1.0R - Math.Abs(2.0R * lightness - 1.0R)) * saturation
        Dim x As Double = c * (1.0R - Math.Abs((hue / 60.0R) Mod 2.0R - 1.0R))
        Dim m As Double = lightness - c / 2.0R
        Dim r As Double = m, green As Double = m, blue As Double = m
        If hue < 60 Then
            r = c + m : green = x + m
        ElseIf hue < 120 Then
            r = x + m : green = c + m
        ElseIf hue < 180 Then
            green = c + m : blue = x + m
        ElseIf hue < 240 Then
            green = x + m : blue = c + m
        ElseIf hue < 300 Then
            r = x + m : blue = c + m
        Else
            r = c + m : blue = x + m
        End If
        Return Color.FromArgb(Channel(alpha), Channel(r), Channel(green), Channel(blue))
    End Function

    Private Shared Function Channel(value As Double) As Integer
        Return Math.Max(0, Math.Min(255, CInt(Math.Round(value * 255.0R))))
    End Function
End Class
#End If
