Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

' The next-slot function of scheduled operations (ticket 222, sections 4.2 and 5).
'
' ONE function, read by everything that needs a time: the editor's live "Next run at" label, the
' arming at save, the re-arming after a run and the missed-run path alike. FMS_A had two paths that
' disagreed about a stale due time (a save skipped missed slots from a fixed anchor, the re-arm fired
' the stale one once); here there is one rule, so what the user sees is what is armed.
'
' THE GRID. Slots are the points  start + k * interval  (k a whole number), where start is the start
' date at the daily anchor time. Nothing about a run moves them: a run that took 40 minutes does not
' push the next slot (no drift), and a 03:00 daily operation stays at 03:00. An interval that divides
' a day returns to the anchor every day; one that does not (5 h, say) keeps its own fixed phase and
' the editor's preview shows where it lands - the grid is never reset by a run.
'
' Nothing before the start is a slot: an operation saved at 10:00 with a 18:00 anchor first runs at
' 18:00, not at 12:00 of a grid extended backwards.
'
' Wall-clock arithmetic on purpose (ScheduledModel.vb): across a daylight-saving change a slot that
' does not exist that day is simply passed by the first tick after the gap.
Friend Module ScheduleMath

    ''' <summary>The floor of an honoured interval (3): below it is corrected up at save.</summary>
    Friend Const MinIntervalMinutes As Integer = 15
    Friend Const DefaultIntervalMinutes As Integer = 24 * 60
    ''' <summary>A year. The editor takes hours and minutes; nothing needs a longer cadence.</summary>
    Friend Const MaxIntervalMinutes As Integer = 366 * 24 * 60
    Friend Const MinutesPerDay As Integer = 24 * 60

    ''' <summary>"0h0m" is corrected to 1 h, anything below 15 minutes up to 15 (section 4), and
    ''' anything above a year down to it.</summary>
    Friend Function NormalizeInterval(minutes As Integer) As Integer
        If minutes <= 0 Then Return 60
        If minutes < MinIntervalMinutes Then Return MinIntervalMinutes
        If minutes > MaxIntervalMinutes Then Return MaxIntervalMinutes
        Return minutes
    End Function

    ''' <summary>Minutes after midnight, wrapped into one day.</summary>
    Friend Function NormalizeAnchor(minutes As Integer) As Integer
        Dim wrapped As Integer = minutes Mod MinutesPerDay
        Return If(wrapped < 0, wrapped + MinutesPerDay, wrapped)
    End Function

    ''' <summary>The first grid point strictly after <paramref name="now"/>, and never before the start.
    ''' When <paramref name="tz"/> is supplied (ticket 231, 3.7):
    ''' - a slot that falls inside a spring-forward gap advances to the first valid minute after the gap;
    ''' - a slot that falls inside an autumn repeated hour is skipped once it has already run in that hour.</summary>
    Friend Function NextSlot(startDate As DateTime, anchorMinutes As Integer, intervalMinutes As Integer, now As DateTime,
                             Optional tz As TimeZoneInfo = Nothing, Optional lastRunAt As DateTime? = Nothing) As DateTime
        Dim start As DateTime = FirstSlot(startDate, anchorMinutes)
        Dim intervalTicks As Long = CLng(NormalizeInterval(intervalMinutes)) * TimeSpan.TicksPerMinute
        Dim steps As Long = If(now < start, 0L, (now.Ticks - start.Ticks) \ intervalTicks + 1L)
        If steps < 0L Then steps = 0L

        For guard As Integer = 0 To 8
            Dim raw As New DateTime(start.Ticks + steps * intervalTicks, DateTimeKind.Unspecified)
            Dim candidate As DateTime = AdjustForSpringGap(raw, tz)
            If candidate <= now Then
                steps += 1L
                Continue For
            End If
            If tz IsNot Nothing AndAlso tz.IsAmbiguousTime(candidate) AndAlso
               lastRunAt.HasValue AndAlso lastRunAt.Value >= candidate Then
                steps += 1L
                Continue For
            End If
            Return candidate
        Next
        Return New DateTime(start.Ticks + steps * intervalTicks, DateTimeKind.Unspecified)
    End Function

    Private Function AdjustForSpringGap(candidate As DateTime, tz As TimeZoneInfo) As DateTime
        If tz Is Nothing OrElse Not tz.IsInvalidTime(candidate) Then Return candidate
        Dim probe As DateTime = candidate
        For minute As Integer = 1 To 240
            probe = candidate.AddMinutes(minute)
            If Not tz.IsInvalidTime(probe) Then Return probe
        Next
        Return candidate
    End Function

    ''' <summary>The start of the grid: the start date at the anchor time.</summary>
    Friend Function FirstSlot(startDate As DateTime, anchorMinutes As Integer) As DateTime
        Return DateTime.SpecifyKind(startDate.Date, DateTimeKind.Unspecified).AddMinutes(NormalizeAnchor(anchorMinutes))
    End Function

    ''' <summary>The next slot of an operation - the form every caller uses.</summary>
    Friend Function NextSlot(op As ScheduledOperation, now As DateTime, Optional tz As TimeZoneInfo = Nothing) As DateTime
        Return NextSlot(op.StartDate, op.AnchorMinutes, op.IntervalMinutes, now, tz, op.Record?.LastRunAt)
    End Function

    ''' <summary>The last grid point at or before <paramref name="now"/>, or Nothing before the first
    ''' slot. What "a slot came due" is measured against.</summary>
    Friend Function LastSlotAtOrBefore(startDate As DateTime, anchorMinutes As Integer, intervalMinutes As Integer, now As DateTime) As DateTime?
        Dim start As DateTime = FirstSlot(startDate, anchorMinutes)
        If now < start Then Return Nothing
        Dim intervalTicks As Long = CLng(NormalizeInterval(intervalMinutes)) * TimeSpan.TicksPerMinute
        Dim steps As Long = (now.Ticks - start.Ticks) \ intervalTicks
        Return New DateTime(start.Ticks + steps * intervalTicks, DateTimeKind.Unspecified)
    End Function

End Module

''' <summary>What the scheduler does with one operation when it starts (5): arm it, run it late, or
''' let the missed slot go.</summary>
Friend Enum StartupDecision
    ''' <summary>Not enabled, halted, or the scheduler is stood down: nothing is armed.</summary>
    Idle = 0
    ''' <summary>The stored slot is still ahead (or there is none): arm the first grid point after now.</summary>
    Arm = 1
    ''' <summary>The stored slot passed while the program was not running and the policy is "run once at
    ''' start": one run, then the next slot after now.</summary>
    RunLate = 2
    ''' <summary>The stored slot passed and the policy is "skip": one note, then the next slot after now.</summary>
    Missed = 3
End Enum

Friend Module MissedRunRules

    ''' <summary>
    ''' The one rule for a slot that came due while the program was not running. A missed slot is one
    ''' the operation was ARMED for and the program was not there to take; an operation that was never
    ''' armed (new, re-enabled) has none. A scheduler that was paused is not a missed run: pausing is the
    ''' user's own act and nothing catches up when it is released (5) - the caller passes
    ''' <paramref name="standDown"/> for that and gets <see cref="StartupDecision.Idle"/>.
    ''' </summary>
    Friend Function DecideAtStart(op As ScheduledOperation, now As DateTime, policy As MissedRunPolicy, standDown As Boolean) As StartupDecision
        If standDown OrElse op Is Nothing OrElse Not op.IsArmable Then Return StartupDecision.Idle
        ' A walled operation catches up with nothing: it waits for its cause to go, then runs at its next slot.
        If op.Halt = ScheduledHalt.Wall Then Return StartupDecision.Arm
        Dim due As DateTime? = op.Record.NextRunAt
        Dim retry As Boolean = If(op.HasExplicitRetryMissedRuns, op.RetryMissedRunsAtStart, policy = MissedRunPolicy.RunOnceAtStart)
        If due.HasValue AndAlso due.Value <= now Then Return If(retry, StartupDecision.RunLate, StartupDecision.Missed)
        ' Ticket 231 rule 5, "or was skipped": the last run could not start (the other device was off, no network) and no
        ' success has followed, so it is tried once more at start. One try per start: a skip again ends it until the next one.
        If retry AndAlso op.Record.SkippedSinceSuccess AndAlso op.Record.LastOutcome = ScheduledRunOutcome.Skipped Then Return StartupDecision.RunLate
        Return StartupDecision.Arm
    End Function

    Friend Function DecideAtStart(op As ScheduledOperation, now As DateTime, standDown As Boolean) As StartupDecision
        Return DecideAtStart(op, now, If(op IsNot Nothing AndAlso op.RetryMissedRunsAtStart, MissedRunPolicy.RunOnceAtStart, MissedRunPolicy.Skip), standDown)
    End Function

End Module
