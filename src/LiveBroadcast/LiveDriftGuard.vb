#If Not NETFRAMEWORK Then
Option Strict On

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md, contract-sync work item 2.
' LIVE-BROADCAST consumer rule 2: "never let the delay grow" - also while bytes keep arriving.
'
' Pure policy, no clocks of its own: every call is handed "now". The model is the difference between the
' audio that has ARRIVED (AdtsFrameCounter) and the audio that has been PLAYED (the player's time):
'
'     lag = arrived - played
'
' It includes the player's own cache, so a healthy session sits at a steady, small value; a producer
' clock that runs fast against this PC's output clock makes it climb. When it has CLIMBED, stays above the
' threshold for a while, the session drops its queue and rejoins the live edge exactly as a reconnect does.
'
' Found by driving the real window (specification 023, implementation record): a rejoin only helps when
' the lag GREW. A stream the player simply starts late - a very quiet source encoded at a tiny bitrate, which LibVLC
' needs many seconds to probe - shows a large but constant lag, and rejoining it would reproduce the same slow
' start every time, for ever. So the guard demands growth as well as height, and starts its warm-up when the player
' first reports a position, not when the connection opened.

Friend Enum LiveDriftAction
    None = 0
    ''' <summary>Drop the queue and reconnect to the live edge.</summary>
    Rejoin = 1
End Enum

Friend NotInheritable Class LiveDriftGuard

    ''' <summary>How far above the effective target the lag may sit (open question 1: a fixed margin).</summary>
    Public Const MarginMs As Integer = 500

    ''' <summary>
    ''' The lowest and highest rejoin threshold. The floor sits above what a healthy session shows: measured on LibVLC
    ''' 3.0.21 the player's position lags the data it has received by about 0.75 s with readings up to 1.0 s (its time is
    ''' reported in coarse steps), so a threshold under about 1.3 s would rejoin a healthy stream. The ceiling is the
    ''' contract's 2 s steady-state limit, less what the producer and the output device add.
    ''' </summary>
    Public Const MinThresholdMs As Integer = 1300
    Public Const MaxThresholdMs As Integer = 1500

    ''' <summary>Nothing is judged for this long after the player first reports a position (the first buffer fill, the first output).</summary>
    Public Const WarmupMs As Long = 5000L

    ''' <summary>The lag must stay above the threshold this long, so one slow packet never costs a gap.</summary>
    Public Const SustainMs As Long = 2000L

    ''' <summary>
    ''' The lag must also have grown by this much since its lowest value in the session: a drifting clock does that, a
    ''' source that merely started late does not (and a rejoin would not cure it).
    ''' </summary>
    Public Const MinGrowthMs As Double = 300.0R

    ''' <summary>After a rejoin the guard waits this long before it may act again, so it cannot flap.</summary>
    Public Const CooldownMs As Long = 15000L

    Private ReadOnly thresholdValue As Integer
    Private firstValidMs As Long = -1L
    Private lowestLagMs As Double = Double.MaxValue
    Private aboveSinceMs As Long = -1L
    Private cooldownUntilMs As Long = Long.MinValue

    Public Sub New(effectiveTargetMs As Integer)
        thresholdValue = ThresholdFor(effectiveTargetMs)
    End Sub

    ''' <summary>The rejoin threshold for a target: target plus a margin, kept inside 1300..1500 ms.</summary>
    Public Shared Function ThresholdFor(effectiveTargetMs As Integer) As Integer
        Return Math.Max(MinThresholdMs, Math.Min(MaxThresholdMs, effectiveTargetMs + MarginMs))
    End Function

    Public ReadOnly Property ThresholdMs As Integer
        Get
            Return thresholdValue
        End Get
    End Property

    ''' <summary>The last lag that was a valid measurement, in ms (0 before the first).</summary>
    Public ReadOnly Property LastLagMs As Double

    ''' <summary>How many times the guard asked for a rejoin.</summary>
    Public ReadOnly Property RejoinCount As Integer

    ''' <summary>A new connection started playing: the warm-up begins again and the lag is unknown.</summary>
    Public Sub NoteSessionStarted(nowMs As Long)
        firstValidMs = -1L
        lowestLagMs = Double.MaxValue
        aboveSinceMs = -1L
        _LastLagMs = 0.0R
    End Sub

    ''' <summary>
    ''' Judges one sample. <paramref name="arrivedMs"/> is the audio received on THIS connection,
    ''' <paramref name="playedMs"/> the player's time on the same media. A sample that cannot be trusted
    ''' (not playing, no time yet, the player ahead of the data) is ignored - the guard never rejoins
    ''' on a measurement it cannot vouch for.
    ''' </summary>
    Public Function Evaluate(nowMs As Long, arrivedMs As Double, playedMs As Long, isPlaying As Boolean) As LiveDriftAction
        If Not isPlaying OrElse playedMs <= 0 OrElse arrivedMs <= 0.0R Then
            aboveSinceMs = -1L
            Return LiveDriftAction.None
        End If

        Dim lag As Double = arrivedMs - playedMs
        If lag < -250.0R Then
            ' The player claims to be ahead of everything that arrived: the clock is not comparable.
            aboveSinceMs = -1L
            Return LiveDriftAction.None
        End If
        _LastLagMs = lag

        ' The warm-up starts at the first usable sample - the player has a position - not at the connection.
        If firstValidMs < 0L Then firstValidMs = nowMs
        If lag < lowestLagMs Then lowestLagMs = lag

        If nowMs - firstValidMs < WarmupMs OrElse nowMs < cooldownUntilMs Then
            aboveSinceMs = -1L
            Return LiveDriftAction.None
        End If

        ' High AND grown: a constant high lag is not drift (a rejoin would only repeat it).
        If lag <= thresholdValue OrElse lag - lowestLagMs < MinGrowthMs Then
            aboveSinceMs = -1L
            Return LiveDriftAction.None
        End If

        If aboveSinceMs < 0L Then
            aboveSinceMs = nowMs
            Return LiveDriftAction.None
        End If

        If nowMs - aboveSinceMs < SustainMs Then Return LiveDriftAction.None

        _RejoinCount += 1
        aboveSinceMs = -1L
        cooldownUntilMs = nowMs + CooldownMs
        Return LiveDriftAction.Rejoin
    End Function

End Class
#End If
