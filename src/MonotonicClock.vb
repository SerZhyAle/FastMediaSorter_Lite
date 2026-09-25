Option Strict On

Imports System.Diagnostics

''' <summary>
''' The one monotonic clock for throttles and debounces. A throttle measured against
''' DateTime.Now stops working when local time moves back (the DST fall-back, an NTP or
''' manual correction): the last stamp is suddenly in the future, and "has the window
''' elapsed" stays False for up to an hour (audit B-10, the same class as A-08 in 048).
'''
''' Both builds: a Stopwatch rather than Environment.TickCount64, which net48 lacks.
''' </summary>
Friend Module MonotonicClock

    Private ReadOnly clock As Stopwatch = Stopwatch.StartNew()

    ''' <summary>Milliseconds since the process first asked. Only differences mean anything.</summary>
    Friend Function NowMs() As Long
        Return clock.ElapsedMilliseconds
    End Function

    ''' <summary>
    ''' True when at least <paramref name="windowMs"/> have passed since
    ''' <paramref name="lastMs"/>. A NEGATIVE delta - a stamp from the future, which a
    ''' monotonic clock never produces but a stale or foreign stamp can - counts as
    ''' elapsed: a throttle must fail open, never hold forever. Pure.
    ''' </summary>
    Friend Function HasElapsed(nowMs As Long, lastMs As Long, windowMs As Long) As Boolean
        Dim delta As Long = nowMs - lastMs
        Return delta < 0 OrElse delta >= windowMs
    End Function

End Module
