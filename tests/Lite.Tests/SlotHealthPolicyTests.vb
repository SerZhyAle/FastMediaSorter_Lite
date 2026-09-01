#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports Xunit

''' <summary>
''' Ф2 of 011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md §4, acceptance point 6 -
''' the good TTL, the bad TTL, the repeat-press window and the boundary at exactly the TTL.
'''
''' What is really under test is the promise in invariant 5: a dead slot costs at most ONE
''' probe per Bad_Ttl_Seconds however many times its key is pressed. That promise cannot be
''' checked by looking at the code, because the interesting cases are all about a clock - and
''' it cannot be checked by hand either, because the scene is "put a NAS to sleep and press 3
''' twenty times". Handing the policy the time is what turns it into arithmetic.
'''
''' Modern-only, like the feature: on the net48 leg this file and SlotHealth.vb both compile
''' to nothing, which is the honest shape (invariant 10).
''' </summary>
Public Class SlotHealthPolicyTests

    Private Shared ReadOnly Base As New DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc)

    Private Shared Function VerdictAt(state As SlotState, secondsAgo As Double) As SlotVerdict
        Return SlotVerdict.ForState(state, Base.AddSeconds(-secondsAgo))
    End Function

    ' ------------------------------------------------------------ nothing known ----

    ''' <summary>The first press of the session pays - and it is the press that pays for
    ''' itself twenty times over (§0.2).</summary>
    <Fact>
    Public Sub No_verdict_means_probe()
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, Nothing, isRepeatPress:=False))
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, Nothing, isRepeatPress:=True))
    End Sub

    ''' <summary>A slot with no path is never probed: there is nothing to ask about, and
    ''' asking would cost the same two seconds as a real destination.</summary>
    <Fact>
    Public Sub A_slot_with_no_path_is_never_probed()
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.NotConfigured, 9999), False))
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.NotConfigured, 9999), True))
    End Sub

    ' -------------------------------------------------------------- the two TTLs ----

    <Fact>
    Public Sub A_good_verdict_is_trusted_for_two_minutes()
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Ready, 1), False))
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Ready, 119), False))
        ' A destination whose folder does not exist YET is a good verdict too - the answer
        ' "I will make it" is as usable as "it is there" (D6).
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.WillBeCreated, 119), False))
    End Sub

    ''' <summary>
    ''' Invariant 5, as arithmetic. Twenty presses into a dead slot, and only the first pays -
    ''' the scene of §0.2, where each press used to queue an operation, remove a file from the
    ''' list, and wait out a full SMB timeout on the worker.
    ''' </summary>
    <Fact>
    Public Sub A_dead_slot_costs_one_probe_per_bad_ttl()
        Dim dead As SlotVerdict = SlotVerdict.ForState(SlotState.Unreachable, Base)

        ' Twenty presses inside the retry window. Not one re-probes, gesture or no gesture.
        For press As Integer = 1 To 20
            Dim [when] As DateTime = Base.AddSeconds(press * 0.05R)
            Assert.False(SlotHealthPolicy.ShouldProbe([when], dead, isRepeatPress:=False))
            Assert.False(SlotHealthPolicy.ShouldProbe([when], dead, isRepeatPress:=True))
        Next

        ' Once the bad TTL is out, the next press does pay - that is how a woken NAS is
        ' noticed without the user having to do anything.
        Assert.True(SlotHealthPolicy.ShouldProbe(Base.AddSeconds(SlotHealthPolicy.Bad_Ttl_Seconds), dead, False))
    End Sub

    ''' <summary>
    ''' The reconciliation of D4 with invariant 5, pinned - because as written in §2 and §7
    ''' the two contradict each other, and the contradiction is only visible once the clock is
    ''' real: "a dead slot costs at most one probe per Bad_Ttl_Seconds HOWEVER MANY TIMES its
    ''' key is pressed" versus "a double press inside two seconds forces a re-probe". At
    ''' sorting speed every press is a double press, so taken literally the second rule
    ''' repeals the first.
    '''
    ''' What is implemented: the gesture re-probes only once the standing answer is older than
    ''' the gesture's own window. So invariant 5 holds for pressing, and the WORST case a
    ''' user hammering a dead key can reach is one probe per retry window - background work
    ''' with a two-second cap, against the twenty SMB timeouts and twenty rolled-back files
    ''' this whole feature exists to remove.
    ''' </summary>
    <Fact>
    Public Sub Hammering_a_dead_key_cannot_beat_one_probe_per_retry_window()
        ' Ten seconds of presses, ten a second, each one re-verdicted the moment a probe
        ' would have answered - the most expensive shape a user can produce.
        Dim last_Probe_At As DateTime = Base
        Dim probes As Integer = 1
        For press As Integer = 1 To 100
            Dim [when] As DateTime = Base.AddSeconds(press * 0.1R)
            Dim standing As SlotVerdict = SlotVerdict.ForState(SlotState.Unreachable, last_Probe_At)
            If SlotHealthPolicy.ShouldProbe([when], standing, isRepeatPress:=True) Then
                probes += 1
                last_Probe_At = [when]
            End If
        Next

        Dim window_Count As Integer = CInt(Math.Ceiling(10.0R / SlotHealthPolicy.Retry_Window_Seconds))
        Assert.True(probes <= window_Count + 1,
                    "a hammered dead slot probed " & probes.ToString() & " times in ten seconds")
    End Sub

    <Fact>
    Public Sub A_bad_verdict_expires_sooner_than_a_good_one()
        Assert.True(SlotHealthPolicy.Bad_Ttl_Seconds < SlotHealthPolicy.Good_Ttl_Seconds)
        ' The same age: still trusted when good, already stale when bad.
        Dim age As Double = SlotHealthPolicy.Bad_Ttl_Seconds + 1
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Ready, age), False))
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Missing, age), False))
    End Sub

    ' ------------------------------------------------------------- the boundary ----

    ''' <summary>At EXACTLY the TTL the answer has expired. An inclusive bound is what keeps
    ''' "trusted for N seconds" literally true rather than N-plus-a-tick.</summary>
    <Fact>
    Public Sub The_boundary_is_exactly_the_ttl()
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Ready, SlotHealthPolicy.Good_Ttl_Seconds - 0.001R), False))
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Ready, SlotHealthPolicy.Good_Ttl_Seconds), False))

        Assert.False(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Denied, SlotHealthPolicy.Bad_Ttl_Seconds - 0.001R), False))
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Denied, SlotHealthPolicy.Bad_Ttl_Seconds), False))
    End Sub

    ''' <summary>A verdict stamped in the future - the clock moved back over a DST change or
    ''' an NTP correction - is not trusted. An age-based cache that believes a timestamp from
    ''' the future can hold a bad answer for two months.</summary>
    <Fact>
    Public Sub A_verdict_from_the_future_is_not_trusted()
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, VerdictAt(SlotState.Ready, -60), False))
    End Sub

    ' ---------------------------------------------------- the repeat-press window ----

    <Fact>
    Public Sub Two_presses_inside_the_window_are_a_retry()
        Assert.True(SlotHealthPolicy.IsRepeatPress(Base, Base.AddSeconds(-0.4R)))
        Assert.True(SlotHealthPolicy.IsRepeatPress(Base, Base.AddSeconds(-SlotHealthPolicy.Retry_Window_Seconds)))
        Assert.False(SlotHealthPolicy.IsRepeatPress(Base, Base.AddSeconds(-SlotHealthPolicy.Retry_Window_Seconds - 0.001R)))
        ' Never pressed before is not a repeat.
        Assert.False(SlotHealthPolicy.IsRepeatPress(Base, DateTime.MinValue))
    End Sub

    ''' <summary>
    ''' D4, and the reason it has a floor. "Pressed twice inside two seconds" is the retry
    ''' gesture - but at sorting speed EVERY press looks like that, so a retry re-probes only
    ''' once the standing answer is older than the window itself. A verdict younger than the
    ''' window IS the answer to the retry, and paying two seconds to hear it again would turn
    ''' the gesture into a tax on fast sorting (this is what reconciles D4 with acceptance 1).
    ''' </summary>
    <Fact>
    Public Sub A_retry_re_probes_only_once_the_answer_is_older_than_the_window()
        Dim fresh As SlotVerdict = VerdictAt(SlotState.Unreachable, 0.5R)
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, fresh, isRepeatPress:=True))

        Dim settled As SlotVerdict = VerdictAt(SlotState.Unreachable, SlotHealthPolicy.Retry_Window_Seconds + 0.5R)
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, settled, isRepeatPress:=True))
        ' Without the gesture the same verdict is still inside its bad TTL and is trusted -
        ' which is what makes the double press a deliberate act rather than an accident.
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, settled, isRepeatPress:=False))
    End Sub

    ''' <summary>The retry works on a HEALTHY slot too, and it must not become a way to
    ''' re-probe a good destination on every second press - the good TTL still applies to
    ''' everything the gesture does not name.</summary>
    <Fact>
    Public Sub A_retry_also_re_asks_about_a_slot_believed_healthy()
        Dim good As SlotVerdict = VerdictAt(SlotState.Ready, 30)
        Assert.True(SlotHealthPolicy.ShouldProbe(Base, good, isRepeatPress:=True))
        Assert.False(SlotHealthPolicy.ShouldProbe(Base, good, isRepeatPress:=False))
    End Sub

    ' ------------------------------------------------------------------ usability ----

    ''' <summary>The two states an action may proceed on, and no others. Invariant 6 lives
    ''' here: everything else is a REFUSAL, never a redirection to some other folder.</summary>
    <Fact>
    Public Sub Only_ready_and_will_be_created_let_an_action_through()
        Assert.True(SlotHealthPolicy.IsUsable(SlotState.Ready))
        Assert.True(SlotHealthPolicy.IsUsable(SlotState.WillBeCreated))

        Assert.False(SlotHealthPolicy.IsUsable(SlotState.NotConfigured))
        Assert.False(SlotHealthPolicy.IsUsable(SlotState.Missing))
        Assert.False(SlotHealthPolicy.IsUsable(SlotState.Denied))
        Assert.False(SlotHealthPolicy.IsUsable(SlotState.Unreachable))
        Assert.False(SlotHealthPolicy.IsUsable(SlotState.Invalid))
    End Sub

    ''' <summary>Every state is covered by the line above - a state added later must fail
    ''' this rather than quietly default to "usable".</summary>
    <Fact>
    Public Sub Every_state_is_accounted_for()
        Dim states As Array = [Enum].GetValues(GetType(SlotState))
        Assert.Equal(7, states.Length)
        Dim usable As Integer = 0
        For Each state As SlotState In states
            If SlotHealthPolicy.IsUsable(state) Then usable += 1
        Next
        Assert.Equal(2, usable)
    End Sub

    ''' <summary>The probe's own bound (D2). It is a constant rather than a number typed at
    ''' the call site because §3.3's argument depends on it: two seconds is short enough to
    ''' be worth paying on a first press and long enough that a healthy share always
    ''' answers inside it.</summary>
    <Fact>
    Public Sub The_probe_is_bounded()
        Assert.Equal(2000, SlotHealthPolicy.Probe_Timeout_Ms)
        Assert.True(SlotHealthPolicy.Probe_Timeout_Ms <= SlotHealthPolicy.Retry_Window_Seconds * 1000)
    End Sub

End Class
#End If
