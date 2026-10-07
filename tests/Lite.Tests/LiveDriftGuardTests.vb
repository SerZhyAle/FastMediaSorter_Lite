#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

' LIVE-BROADCAST consumer rule 2 in steady state (contract-sync work item 2): a producer clock that runs a
' little fast must not let the delay grow for as long as the broadcast plays - and a stream that merely starts
' late must not send the guard into a rejoin loop (found by driving the real window).

Public Class LiveDriftGuardTests

    <Theory>
    <InlineData(250, 1300)>
    <InlineData(500, 1300)>
    <InlineData(1000, 1500)>
    <InlineData(2000, 1500)>
    Public Sub Threshold_Is_Target_Plus_Margin_Kept_Inside_1300_To_1500_Ms(target As Integer, expected As Integer)
        Assert.Equal(expected, LiveDriftGuard.ThresholdFor(target))
        Assert.Equal(expected, New LiveDriftGuard(target).ThresholdMs)
    End Sub

    <Fact>
    Public Sub A_Steady_Healthy_Session_Is_Left_Alone_For_An_Hour()
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        For second As Long = 1 To 3600
            Dim t As Long = second * 1000L
            Assert.Equal(LiveDriftAction.None, guard.Evaluate(t, t + 500.0R, t, True))
        Next
        Assert.Equal(0, guard.RejoinCount)
    End Sub

    <Fact>
    Public Sub A_Producer_Clock_Half_A_Percent_Fast_Keeps_The_Delay_Bounded_Over_A_Simulated_Hour()
        ' Producer clock +0.5 %: every wall second the producer delivers 1.005 s of audio, the player consumes
        ' 1.000 s. The lag starts at 500 ms (the player's own cache) and grows 5 ms a second. Whenever the guard
        ' says Rejoin the session is replaced: a new connection, the lag back at its starting value.
        Dim guard As New LiveDriftGuard(1000)
        Dim sessionStart As Long = 0L
        guard.NoteSessionStarted(0L)
        Dim maxLag As Double = 0.0R
        Dim rejoins As Integer = 0

        For second As Long = 1 To 3600
            Dim t As Long = second * 1000L
            Dim played As Long = t - sessionStart
            Dim arrived As Double = (t - sessionStart) * 1.005R + 500.0R
            Dim lag As Double = arrived - played
            maxLag = Math.Max(maxLag, lag)

            If guard.Evaluate(t, arrived, played, True) = LiveDriftAction.Rejoin Then
                rejoins += 1
                sessionStart = t
                guard.NoteSessionStarted(t)
            End If
        Next

        Assert.True(rejoins >= 10, "an hour at +0.5 % needs the guard many times; saw " & rejoins)
        Assert.True(rejoins <= 30, "the guard must not flap; saw " & rejoins)
        ' The threshold plus the time it must be sustained at 5 ms per second: well inside the 2 s ceiling.
        Assert.True(maxLag < 2000.0R, "the lag reached " & maxLag & " ms")
        Assert.Equal(rejoins, guard.RejoinCount)
    End Sub

    <Fact>
    Public Sub Without_The_Guard_The_Same_Clock_Would_Have_Passed_Two_Seconds()
        ' The negative control of the test above: the drift is real, so bounded delay is the guard's doing.
        Dim lagAfterAnHour As Double = 3600 * 1000 * 0.005R + 500.0R
        Assert.True(lagAfterAnHour > 2000.0R)
    End Sub

    <Fact>
    Public Sub A_Large_Constant_Lag_Is_Not_Drift_And_Is_Never_Rejoined()
        ' The real window, a very quiet source at a tiny bitrate: LibVLC took ~11 s to probe it, so the player trails the
        ' data by 11 s from the first position it reports - and stays there. A rejoin would repeat the same slow start.
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        For second As Long = 1 To 300
            Dim t As Long = second * 1000L
            Assert.Equal(LiveDriftAction.None, guard.Evaluate(t, t + 11400.0R, t, True))
        Next
        Assert.Equal(0, guard.RejoinCount)
    End Sub

    <Fact>
    Public Sub A_Lag_That_Grows_From_A_Late_Start_Is_Still_Caught()
        ' Late start AND drift: the lag starts at 1.2 s (under the threshold), grows 20 ms a second.
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        Dim rejoined As Long = -1L
        For second As Long = 1 To 60
            Dim t As Long = second * 1000L
            If guard.Evaluate(t, t + 1200.0R + 20.0R * second, t, True) = LiveDriftAction.Rejoin Then
                rejoined = t
                Exit For
            End If
        Next
        Assert.True(rejoined > 0L, "growth past the threshold must be acted on")
        ' 1200 + 20 s > 1500 at second 16; it must hold for 2 s
        Assert.InRange(rejoined, 17000L, 22000L)
    End Sub

    <Fact>
    Public Sub One_Slow_Sample_Does_Not_Cost_A_Gap()
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        For t As Long = 1000L To 9000L Step 1000L
            guard.Evaluate(t, t + 700.0R, t, True)
        Next
        ' A single 2 s lag sample after warm-up, then back to normal: nothing.
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(10000L, 12000.0R, 10000L, True))
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(11000L, 11700.0R, 11000L, True))
        Assert.Equal(0, guard.RejoinCount)
    End Sub

    <Fact>
    Public Sub Nothing_Is_Judged_During_The_Warm_Up()
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        guard.Evaluate(1000L, 1700.0R, 1000L, True)
        For t As Long = 2000L To 5000L Step 1000L
            Assert.Equal(LiveDriftAction.None, guard.Evaluate(t, t + 5000.0R, t, True))
        Next
    End Sub

    <Fact>
    Public Sub The_Warm_Up_Starts_When_The_Player_First_Reports_A_Position_Not_When_The_Connection_Opened()
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        ' Twelve seconds of "no position yet": nothing to judge, and no warm-up spent.
        For t As Long = 1000L To 12000L Step 1000L
            Assert.Equal(LiveDriftAction.None, guard.Evaluate(t, t * 1.0R, 0L, True))
        Next
        guard.Evaluate(13000L, 25700.0R, 13000L, True)           ' first position: warm-up starts here, lag 12.7 s
        For t As Long = 14000L To 17000L Step 1000L
            Assert.Equal(LiveDriftAction.None, guard.Evaluate(t, t + 12700.0R, t, True))
        Next
    End Sub

    <Fact>
    Public Sub It_Acts_Only_After_The_Lag_Has_Grown_And_Stays_High_And_Then_Waits_Out_A_Cooldown()
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        For t As Long = 1000L To 8000L Step 1000L
            guard.Evaluate(t, t + 700.0R, t, True)                   ' healthy: lag 0.7 s
        Next

        Assert.Equal(LiveDriftAction.None, guard.Evaluate(9000L, 9000.0R + 1700.0R, 9000L, True))     ' high and grown: starts counting
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(10000L, 10000.0R + 1700.0R, 10000L, True))  ' 1 s of 2
        Assert.Equal(LiveDriftAction.Rejoin, guard.Evaluate(11000L, 11000.0R + 1700.0R, 11000L, True)) ' 2 s sustained

        ' Straight after a rejoin the guard keeps quiet for the cooldown, whatever it sees.
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(12000L, 12000.0R + 3000.0R, 12000L, True))
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(20000L, 20000.0R + 3000.0R, 20000L, True))
    End Sub

    <Fact>
    Public Sub A_Measurement_It_Cannot_Vouch_For_Is_Never_Acted_On()
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        ' Not playing, no player time yet, no audio received yet, a player "ahead" of everything received.
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(20000L, 20000.0R, 0L, True))
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(21000L, 0.0R, 21000L, True))
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(22000L, 30000.0R, 1000L, False))
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(23000L, 1000.0R, 9000L, True))
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(25000L, 60000.0R, 1000L, False))
        Assert.Equal(0, guard.RejoinCount)
    End Sub

    <Fact>
    Public Sub A_New_Session_Starts_The_Warm_Up_Again()
        Dim guard As New LiveDriftGuard(1000)
        guard.NoteSessionStarted(0L)
        guard.Evaluate(10000L, 12000.0R, 10000L, True)
        guard.NoteSessionStarted(10500L)
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(12500L, 15000.0R, 2000L, True))
        Assert.Equal(LiveDriftAction.None, guard.Evaluate(14500L, 17000.0R, 4000L, True))
    End Sub

End Class
#End If
