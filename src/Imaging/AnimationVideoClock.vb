#If Not NETFRAMEWORK Then
Option Strict On

' Spec 242: where an animation video IS, counted by us. Nothing here touches LibVLC, WinForms or a clock.

''' <summary>
''' The position of the animation video on screen.
'''
''' LibVLC's own clock (<c>MediaPlayer.Time</c>, <c>Position</c>) does not run for a media that is fed through
''' <c>StreamMediaInput</c> - which is how the video leaves its encrypted container, with no path to open - so a
''' transport bar reading it would show a thumb that never moves and a clock stuck at 0:00. The length and the
''' seek itself are still the player's (both work for such a media); only the "now" is counted here.
'''
''' Every call is handed the time, whether the player is playing and its rate, so the class reads no clock and no
''' control and a test can drive it. Elapsed time between two calls is credited to the state recorded at the
''' FIRST of them (the one that held during the interval), then the new state is recorded - so a pause is
''' noticed at the next call, and the timer that calls <see cref="Sample"/> every few hundred milliseconds bounds
''' the error. The position wraps at the length: the video loops, the bar goes round with it.
''' </summary>
Friend NotInheritable Class AnimationVideoClock

    Private activeValue As Boolean
    Private currentMs As Double
    Private lastMs As Long
    Private lastPlaying As Boolean
    Private lastRate As Double = 1.0R

    ''' <summary>True from <see cref="Start"/> until <see cref="Clear"/>: the reads of the player's own position
    ''' are replaced by this clock for exactly that long.</summary>
    Friend ReadOnly Property IsActive As Boolean
        Get
            Return activeValue
        End Get
    End Property

    ''' <summary>The media started (or restarted) playing at <paramref name="atMs"/>.</summary>
    Friend Sub Start(nowMs As Long, atMs As Long, playing As Boolean, rate As Single)
        activeValue = True
        currentMs = Math.Max(0L, atMs)
        lastMs = nowMs
        lastPlaying = playing
        lastRate = NormalRate(rate)
    End Sub

    ''' <summary>The media is gone: the player's own clock is read again.</summary>
    Friend Sub Clear()
        activeValue = False
        currentMs = 0.0R
        lastMs = 0L
        lastPlaying = False
        lastRate = 1.0R
    End Sub

    ''' <summary>Counts the time that passed since the last call and records the state from now on.</summary>
    Friend Sub Sample(nowMs As Long, playing As Boolean, rate As Single, lengthMs As Long)
        If Not activeValue Then Return
        Dim elapsed As Long = nowMs - lastMs
        ' A clock that went back is re-based, never subtracted.
        If elapsed > 0L AndAlso lastPlaying Then currentMs += elapsed * lastRate
        lastMs = nowMs
        lastPlaying = playing
        lastRate = NormalRate(rate)
        currentMs = Wrap(currentMs, lengthMs)
    End Sub

    ''' <summary>The position in milliseconds now.</summary>
    Friend Function PositionMs(nowMs As Long, playing As Boolean, rate As Single, lengthMs As Long) As Long
        Sample(nowMs, playing, rate, lengthMs)
        Return CLng(Math.Floor(currentMs))
    End Function

    ''' <summary>The position as a fraction of the length (0..1, below 1), 0 while the length is not known.</summary>
    Friend Function Fraction(nowMs As Long, playing As Boolean, rate As Single, lengthMs As Long) As Single
        Sample(nowMs, playing, rate, lengthMs)
        If lengthMs <= 0L Then Return 0.0F
        Return CSng(Math.Min(Math.Max(currentMs / lengthMs, 0.0R), 0.999999R))
    End Function

    ''' <summary>The player was told to jump to <paramref name="atMs"/>: the clock follows from there.</summary>
    Friend Sub SeekTo(atMs As Long, nowMs As Long, playing As Boolean, rate As Single, lengthMs As Long)
        If Not activeValue Then Return
        Sample(nowMs, playing, rate, lengthMs)
        Dim target As Double = Math.Max(0L, atMs)
        If lengthMs > 0L Then target = Math.Min(target, lengthMs - 1L)
        currentMs = Math.Max(0.0R, target)
    End Sub

    Private Shared Function NormalRate(rate As Single) As Double
        If Single.IsNaN(rate) OrElse Single.IsInfinity(rate) OrElse rate <= 0.0F Then Return 1.0R
        Return rate
    End Function

    Private Shared Function Wrap(valueMs As Double, lengthMs As Long) As Double
        If lengthMs <= 0L OrElse valueMs < lengthMs Then Return valueMs
        Return valueMs - Math.Floor(valueMs / lengthMs) * lengthMs
    End Function

End Class
#End If
