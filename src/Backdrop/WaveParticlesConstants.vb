#If Not NETFRAMEWORK Then
Option Strict On

' Every number of the "particles and lines" backdrop, once, each group under the section of the
' contract it comes from (WAVE-PARTICLES rule 18, ticket 029 F-1). Nothing here is tuned locally: a
' value that differs from the catalog is an exception row in its registry, never a quiet edit here.
' WaveParticlesConstantsTests asserts them value by value, so a drift fails by name.
'
' Mainline only. The audio visualiser is whole-file "#If Not NETFRAMEWORK" and the x86 fallback gets
' nothing (maintenance policy, CLAUDE.md "Two builds, one source tree").
Friend Module WaveParticlesConstants

    ' --- WAVE-PARTICLES section 3.1 - clock ---------------------------------------------------
    ' 0.13 mapped the Windows host signals, 0.14 settled the arbitration (the system wins);
    ' both were already implemented here, so only the recorded version moves. 0.15 (section 5) lets a
    ' decorative surface behind text fix intensity and speed itself; the audio player backdrop is an
    ' ambient surface with no tuning, so it owes nothing and the version moves again by record only.
    Friend Const ContractVersion As String = "0.15"
    Friend Const ReferenceFrameSeconds As Double = 1.0R / 60.0R   ' REFERENCE_FRAME
    Friend Const TimeStep As Double = 0.002R                       ' TIME_STEP, per reference frame
    Friend Const RampFrames As Integer = 36                        ' RAMP_FRAMES (also the still-frame pass count)

    ' NOT a contract value (the catalog's reference implementation makes the same choice and marks it):
    ' after a stall - a modal loop, a debugger, a resume that was not reset - one tick would otherwise
    ' replay the whole gap as a single step, throwing particles across the field and wiping the trail.
    Friend Const MaxFramesPerTick As Double = 4.0R

    ' --- WAVE-PARTICLES section 3.2 - session rolls, full profile -----------------------------
    Friend Const LineCountMin As Integer = 5
    Friend Const LineCountMax As Integer = 12
    Friend Const ParticleCountMin As Integer = 15
    Friend Const ParticleCountMax As Integer = 55
    Friend Const SampleStep As Double = 20.0R
    Friend Const SampleStepJitterMin As Double = 0.8R
    Friend Const SampleStepJitterMax As Double = 1.2R
    Friend Const StrokeWidthMin As Double = 3.0R
    Friend Const StrokeWidthMax As Double = 6.0R
    Friend Const AmplitudeFractionMin As Double = 0.28R
    Friend Const AmplitudeFractionMax As Double = 0.48R
    Friend Const SpeedMultiplierMin As Double = 0.5R
    Friend Const SpeedMultiplierMax As Double = 1.5R

    ' Per particle (section 3.2, second table).
    Friend Const ParticleRadiusMin As Double = 1.0R
    Friend Const ParticleRadiusMax As Double = 6.0R
    Friend Const ParticleSpeedBase As Double = 0.12R
    Friend Const ParticleSpeedRange As Double = 0.42R
    Friend Const CounterDriftProbability As Double = 0.18R
    Friend Const CounterDriftFactor As Double = -0.35R
    Friend Const ParticleJitter As Double = 0.28R

    ' --- WAVE-PARTICLES section 3.3 - palette DYNAMIC (the only one this surface uses, rule 6) ---
    Friend Const LineHueStepMin As Double = 8.0R
    Friend Const LineHueStepMax As Double = 20.0R
    Friend Const DynamicParticleSpread As Double = 108.0R

    ' --- WAVE-PARTICLES section 3.4 - frame ----------------------------------------------------
    Friend Const WashDarkChannel As Integer = 10        ' rgb(10, 10, 10)
    Friend Const WashLightChannel As Integer = 245      ' rgb(245, 245, 245)
    Friend Const WashAlpha As Double = 38.0R / 255.0R
    Friend Const LineSaturation As Double = 0.8R
    Friend Const LineLightnessDark As Double = 0.65R
    Friend Const LineLightnessLight As Double = 0.35R
    Friend Const ParticleSaturation As Double = 0.9R
    Friend Const ParticleLightnessDark As Double = 0.7R
    Friend Const ParticleLightnessLight As Double = 0.3R
    Friend Const LineOpacityBase As Double = 0.28R
    Friend Const LineOpacityGain As Double = 0.16R
    Friend Const ParticleOpacityBase As Double = 0.38R
    Friend Const ParticleOpacityGain As Double = 0.32R
    Friend Const OpacityScale As Double = 0.7R          ' rule 16: nothing draws brighter than this
    Friend Const CentreDriftRate As Double = 0.45R
    Friend Const CentreDriftFraction As Double = 0.02R
    Friend Const LaneFraction As Double = 0.038R
    Friend Const SpanExtraSteps As Double = 6.0R
    Friend Const SpatialFrequency As Double = 0.0105R
    Friend Const LanePhaseStep As Double = 0.8R
    Friend Const EnvelopeBase As Double = 0.4R
    Friend Const EnvelopeGain As Double = 0.6R
    Friend Const EnvelopeRate As Double = 0.4R
    Friend Const EnvelopeLaneStep As Double = 0.2R

    ' --- WAVE-PARTICLES section 4 - the ramp gain g = 0.35 + 0.65 * p * (2 - p) ----------------
    Friend Const RampGainStart As Double = 0.35R

End Module
#End If
