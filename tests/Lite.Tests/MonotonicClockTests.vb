Option Strict On

Imports Xunit

''' <summary>Audit 049 B-10: the perspective throttle on a monotonic clock, failing open.</summary>
Public Class MonotonicClockTests

    <Fact>
    Public Sub A_draw_inside_the_window_is_throttled()
        Assert.False(MonotonicClock.HasElapsed(1030, 1000, 50))
    End Sub

    <Fact>
    Public Sub A_draw_after_the_window_is_allowed()
        Assert.True(MonotonicClock.HasElapsed(1050, 1000, 50))
        Assert.True(MonotonicClock.HasElapsed(9000, 1000, 50))
    End Sub

    <Fact>
    Public Sub A_last_stamp_from_the_future_does_not_hold_the_throttle()
        ' What DateTime.Now produced after a DST fall-back: "last" an hour ahead of "now".
        ' The old comparison stayed False for the whole hour; this must fail open.
        Assert.True(MonotonicClock.HasElapsed(1000, 1000 + 3600000, 50))
    End Sub

    <Fact>
    Public Sub The_initial_stamp_lets_the_first_draw_through()
        ' Main_Form starts the field at minus the window, so the very first draw is never
        ' throttled even when the clock was started a moment ago.
        Assert.True(MonotonicClock.HasElapsed(0, -50, 50))
    End Sub

    <Fact>
    Public Sub The_clock_never_goes_backwards()
        Dim first As Long = MonotonicClock.NowMs()
        Dim second As Long = MonotonicClock.NowMs()
        Assert.True(second >= first)
    End Sub

End Class
