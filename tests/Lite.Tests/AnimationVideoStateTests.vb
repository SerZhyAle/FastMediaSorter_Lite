#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports Xunit

''' <summary>
''' Animated graphics through video (specification 242), step T-6a: the pure state behind the viewer's video
''' surface - the phase machine (none, opening, shown), the session serial that makes every late answer
''' recognisable, the routing of libvlc events (<c>EventSerial</c> / <c>ArmEvents</c> / <c>EventsDone</c>), the
''' constants and the yes/no question of <c>AnimationVideoSurfaceRules</c>. No WinForms, no LibVLC, no
''' registry. The phases travel as names (a Public test method cannot take the Friend enum), which also makes a
''' failing row say what it got.
''' </summary>
Public Class AnimationVideoStateTests

    Private Shared ReadOnly Wait As TimeSpan = TimeSpan.FromSeconds(30)

    Private Const Gen As Integer = 7
    Private Const AnimPath As String = "C:\pics\anim.webp"

    ''' <summary>A state holding a session that is opening (or already shown), for generation <see cref="Gen"/>.</summary>
    Private Shared Function Held(state As AnimationVideoState, phaseName As String) As Integer
        Dim serial As Integer = state.Begin(Gen, AnimPath)
        If phaseName = "Shown" Then Assert.True(state.TryReveal(serial, Gen))
        Return serial
    End Function

    Private Shared Function PhaseOf(state As AnimationVideoState) As String
        Return state.Phase.ToString()
    End Function

    Private Shared Sub AssertIdle(state As AnimationVideoState)
        Assert.Equal("None", PhaseOf(state))
        Assert.Equal(0, state.Serial)
        Assert.Equal(-1, state.Generation)
        Assert.Equal("", state.FilePath)
        Assert.False(state.IsActive)
        Assert.False(state.IsShown)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' The idle state
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_new_state_holds_nothing()
        Dim state As New AnimationVideoState()
        AssertIdle(state)
        Assert.Equal(0, state.EventSerial)
    End Sub

    <Theory>
    <InlineData(0, -1)>     ' "no session" and "no generation" must not match each other
    <InlineData(0, 0)>
    <InlineData(0, Gen)>
    <InlineData(1, -1)>
    <InlineData(1, Gen)>
    <InlineData(-1, -1)>
    Public Sub A_new_state_accepts_no_answer(serial As Integer, generation As Integer)
        Dim state As New AnimationVideoState()
        Assert.False(state.Accepts(serial, generation))
    End Sub

    <Fact>
    Public Sub Every_call_on_an_idle_state_refuses_and_changes_nothing()
        Dim state As New AnimationVideoState()

        Assert.False(state.TryReveal(0, -1))
        Assert.False(state.TryReveal(1, Gen))
        Assert.False(state.Finish(0))
        Assert.False(state.Finish(1))
        Assert.False(state.ArmEvents(0))
        Assert.False(state.ArmEvents(1))
        Assert.Equal(0, state.FinishCurrent())
        state.EventsDone(0)
        state.EventsDone(1)

        AssertIdle(state)
        Assert.Equal(0, state.EventSerial)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Begin and the serial
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub Begin_opens_a_session_for_the_showing_and_the_file()
        Dim state As New AnimationVideoState()

        Dim serial As Integer = state.Begin(Gen, AnimPath)

        Assert.True(serial > 0)
        Assert.Equal("Opening", PhaseOf(state))
        Assert.Equal(serial, state.Serial)
        Assert.Equal(Gen, state.Generation)
        Assert.Equal(AnimPath, state.FilePath)
        Assert.True(state.IsActive)
        Assert.False(state.IsShown)
        Assert.True(state.Accepts(serial, Gen))
        ' Begin only starts the session; the events are routed by ArmEvents, right before the media plays.
        Assert.Equal(0, state.EventSerial)
    End Sub

    <Fact>
    Public Sub Begin_without_a_path_keeps_an_empty_path()
        Dim state As New AnimationVideoState()

        state.Begin(Gen, Nothing)

        Assert.Equal("", state.FilePath)
        Assert.Equal("Opening", PhaseOf(state))
    End Sub

    <Fact>
    Public Sub Begin_accepts_generation_zero()
        Dim state As New AnimationVideoState()

        Dim serial As Integer = state.Begin(0, AnimPath)

        Assert.Equal(0, state.Generation)
        Assert.True(state.Accepts(serial, 0))
        Assert.False(state.Accepts(serial, -1))
    End Sub

    <Fact>
    Public Sub Serials_are_never_zero_and_never_repeat_across_finished_and_replaced_sessions()
        Dim state As New AnimationVideoState()
        Dim seen As New HashSet(Of Integer)()
        Dim previous As Integer = 0

        For i As Integer = 1 To 1000
            Dim serial As Integer = state.Begin(i, AnimPath)
            Assert.True(serial > 0, "serial must never be 0 or negative")
            Assert.True(serial > previous, "serial must keep growing")
            Assert.True(seen.Add(serial), "serial " & serial & " repeated")
            previous = serial
            ' Every third session is finished, the others are replaced by the next Begin.
            If i Mod 3 = 0 Then state.FinishCurrent()
        Next
    End Sub

    <Fact>
    Public Sub Begin_replaces_a_held_session_and_the_old_serial_is_no_longer_accepted()
        Dim state As New AnimationVideoState()
        Dim first As Integer = Held(state, "Shown")

        Dim second As Integer = state.Begin(Gen + 1, "C:\pics\other.webp")

        Assert.NotEqual(first, second)
        Assert.Equal("Opening", PhaseOf(state))      ' a fresh session is opening, not shown
        Assert.Equal(second, state.Serial)
        Assert.Equal(Gen + 1, state.Generation)
        Assert.Equal("C:\pics\other.webp", state.FilePath)
        Assert.False(state.Accepts(first, Gen))
        Assert.False(state.Accepts(first, Gen + 1))
        Assert.True(state.Accepts(second, Gen + 1))
        Assert.False(state.TryReveal(first, Gen))
        Assert.False(state.Finish(first))
        Assert.True(state.IsActive)                    ' the stale calls changed nothing
        Assert.Equal(second, state.Serial)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Accepts
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData("Opening", True, True, True)>
    <InlineData("Opening", False, True, False)>     ' another session's serial
    <InlineData("Opening", True, False, False)>     ' the showing moved on
    <InlineData("Opening", False, False, False)>
    <InlineData("Shown", True, True, True)>
    <InlineData("Shown", False, True, False)>
    <InlineData("Shown", True, False, False)>
    <InlineData("Shown", False, False, False)>
    Public Sub Accepts_needs_the_held_serial_and_the_showing_on_screen(phaseName As String, serialOk As Boolean, generationOk As Boolean, expected As Boolean)
        Dim state As New AnimationVideoState()
        Dim serial As Integer = Held(state, phaseName)

        Dim askedSerial As Integer = If(serialOk, serial, serial + 1)
        Dim askedGeneration As Integer = If(generationOk, Gen, Gen + 1)

        Assert.Equal(expected, state.Accepts(askedSerial, askedGeneration))
    End Sub

    <Fact>
    Public Sub Accepts_refuses_serial_zero_while_a_session_is_held()
        Dim state As New AnimationVideoState()
        Held(state, "Opening")

        Assert.False(state.Accepts(0, Gen))
        Assert.False(state.Accepts(-1, Gen))
    End Sub

    <Theory>
    <InlineData("Finish")>
    <InlineData("FinishCurrent")>
    <InlineData("Begin")>
    Public Sub An_answer_of_an_ended_or_replaced_session_is_not_accepted(how As String)
        Dim state As New AnimationVideoState()
        Dim old As Integer = Held(state, "Shown")

        Select Case how
            Case "Finish"
                Assert.True(state.Finish(old))
            Case "FinishCurrent"
                Assert.Equal(old, state.FinishCurrent())
            Case Else
                state.Begin(Gen + 1, AnimPath)
        End Select

        Assert.False(state.Accepts(old, Gen))
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' TryReveal (opening to shown, rule 21)
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub TryReveal_moves_opening_to_shown_once()
        Dim state As New AnimationVideoState()
        Dim serial As Integer = state.Begin(Gen, AnimPath)

        Assert.True(state.TryReveal(serial, Gen))
        Assert.Equal("Shown", PhaseOf(state))
        Assert.True(state.IsShown)
        Assert.True(state.IsActive)

        Assert.False(state.TryReveal(serial, Gen))        ' a second reveal is refused
        Assert.Equal("Shown", PhaseOf(state))

        Assert.Equal(serial, state.Serial)                 ' revealing keeps the identity of the session
        Assert.Equal(Gen, state.Generation)
        Assert.Equal(AnimPath, state.FilePath)
        Assert.True(state.Accepts(serial, Gen))
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(-1)>
    <InlineData(1000)>
    Public Sub TryReveal_refuses_a_serial_that_is_not_the_held_one(offsetFromHeld As Integer)
        Dim state As New AnimationVideoState()
        Dim serial As Integer = state.Begin(Gen, AnimPath)
        Dim asked As Integer = If(offsetFromHeld = 0, 0, serial + offsetFromHeld)

        Assert.False(state.TryReveal(asked, Gen))

        Assert.Equal("Opening", PhaseOf(state))
        Assert.False(state.IsShown)
        Assert.True(state.TryReveal(serial, Gen))          ' the real one still works afterwards
    End Sub

    <Theory>
    <InlineData(Gen + 1)>
    <InlineData(Gen - 1)>
    <InlineData(0)>
    <InlineData(-1)>
    Public Sub TryReveal_refuses_when_the_showing_has_moved_on(askedGeneration As Integer)
        Dim state As New AnimationVideoState()
        Dim serial As Integer = state.Begin(Gen, AnimPath)

        Assert.False(state.TryReveal(serial, askedGeneration))

        Assert.Equal("Opening", PhaseOf(state))
        Assert.True(state.TryReveal(serial, Gen))
    End Sub

    <Fact>
    Public Sub TryReveal_refuses_after_the_session_ended()
        Dim state As New AnimationVideoState()
        Dim serial As Integer = state.Begin(Gen, AnimPath)
        state.FinishCurrent()

        Assert.False(state.TryReveal(serial, Gen))

        AssertIdle(state)
    End Sub

    <Fact>
    Public Sub TryReveal_of_a_replaced_session_refuses_and_leaves_the_new_one_opening()
        Dim state As New AnimationVideoState()
        Dim old As Integer = state.Begin(Gen, AnimPath)
        Dim fresh As Integer = state.Begin(Gen, AnimPath)   ' same showing, new session

        Assert.False(state.TryReveal(old, Gen))

        Assert.Equal("Opening", PhaseOf(state))
        Assert.Equal(fresh, state.Serial)
        Assert.True(state.TryReveal(fresh, Gen))
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Finish and FinishCurrent
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData("Opening")>
    <InlineData("Shown")>
    Public Sub Finish_ends_the_held_session_and_clears_it(phaseName As String)
        Dim state As New AnimationVideoState()
        Dim serial As Integer = Held(state, phaseName)

        Assert.True(state.Finish(serial))

        AssertIdle(state)
        Assert.False(state.Finish(serial))                 ' a second finish of the same serial is refused
    End Sub

    <Theory>
    <InlineData("Opening", 0)>
    <InlineData("Opening", 1)>
    <InlineData("Opening", -1)>
    <InlineData("Shown", 0)>
    <InlineData("Shown", 1)>
    <InlineData("Shown", -1)>
    Public Sub Finish_of_another_serial_refuses_and_changes_nothing(phaseName As String, offsetFromHeld As Integer)
        Dim state As New AnimationVideoState()
        Dim serial As Integer = Held(state, phaseName)
        Dim asked As Integer = If(offsetFromHeld = 0, 0, serial + offsetFromHeld)

        Assert.False(state.Finish(asked))

        Assert.Equal(phaseName, PhaseOf(state))
        Assert.Equal(serial, state.Serial)
        Assert.Equal(Gen, state.Generation)
        Assert.Equal(AnimPath, state.FilePath)
        Assert.True(state.IsActive)
    End Sub

    <Fact>
    Public Sub A_late_finish_of_an_old_session_does_not_end_the_new_one()
        Dim state As New AnimationVideoState()
        Dim old As Integer = Held(state, "Shown")
        Assert.Equal(old, state.FinishCurrent())
        Dim fresh As Integer = state.Begin(Gen + 1, AnimPath)

        Assert.False(state.Finish(old))

        Assert.Equal("Opening", PhaseOf(state))
        Assert.Equal(fresh, state.Serial)
        Assert.True(state.Accepts(fresh, Gen + 1))
    End Sub

    <Theory>
    <InlineData("Opening")>
    <InlineData("Shown")>
    Public Sub FinishCurrent_returns_the_ended_serial_and_clears_the_session(phaseName As String)
        Dim state As New AnimationVideoState()
        Dim serial As Integer = Held(state, phaseName)

        Assert.Equal(serial, state.FinishCurrent())

        AssertIdle(state)
        Assert.Equal(0, state.FinishCurrent())             ' nothing left to end
        AssertIdle(state)
    End Sub

    <Fact>
    Public Sub A_new_session_can_follow_a_finished_one_with_a_newer_serial()
        Dim state As New AnimationVideoState()
        Dim first As Integer = Held(state, "Shown")
        state.FinishCurrent()

        Dim second As Integer = state.Begin(Gen + 1, "C:\pics\next.webp")

        Assert.True(second > first)
        Assert.Equal("Opening", PhaseOf(state))
        Assert.Equal("C:\pics\next.webp", state.FilePath)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' IsActive / IsShown per phase
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData("None", False, False)>
    <InlineData("Opening", True, False)>
    <InlineData("Shown", True, True)>
    Public Sub IsActive_and_IsShown_follow_the_phase(phaseName As String, expectedActive As Boolean, expectedShown As Boolean)
        Dim state As New AnimationVideoState()
        If phaseName <> "None" Then Held(state, phaseName)

        Assert.Equal(phaseName, PhaseOf(state))
        Assert.Equal(expectedActive, state.IsActive)
        Assert.Equal(expectedShown, state.IsShown)
    End Sub

    <Fact>
    Public Sub A_full_session_walks_every_phase_in_order()
        Dim state As New AnimationVideoState()

        Assert.Equal("None", PhaseOf(state))

        Dim serial As Integer = state.Begin(Gen, AnimPath)
        Assert.Equal("Opening", PhaseOf(state))
        Assert.True(state.IsActive)
        Assert.False(state.IsShown)

        Assert.True(state.ArmEvents(serial))
        Assert.Equal(serial, state.EventSerial)
        Assert.Equal("Opening", PhaseOf(state))          ' arming routes events, it is not a phase change

        Assert.True(state.TryReveal(serial, Gen))
        Assert.Equal("Shown", PhaseOf(state))
        Assert.True(state.IsShown)
        Assert.Equal(serial, state.EventSerial)

        Assert.Equal(serial, state.FinishCurrent())
        Assert.Equal("None", PhaseOf(state))
        Assert.False(state.IsActive)
        Assert.False(state.IsShown)
        Assert.False(state.Accepts(serial, Gen))
        Assert.Equal(serial, state.EventSerial)            ' the player may still raise events until its stop returned

        state.EventsDone(serial)
        Assert.Equal(0, state.EventSerial)
        AssertIdle(state)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' The routing of libvlc events: ArmEvents / EventSerial / EventsDone
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData("Opening")>
    <InlineData("Shown")>
    Public Sub ArmEvents_routes_the_events_of_the_held_session(phaseName As String)
        Dim state As New AnimationVideoState()
        Dim serial As Integer = Held(state, phaseName)

        Assert.True(state.ArmEvents(serial))
        Assert.Equal(serial, state.EventSerial)

        Assert.True(state.ArmEvents(serial))               ' arming twice is harmless
        Assert.Equal(serial, state.EventSerial)
        Assert.Equal(phaseName, PhaseOf(state))
    End Sub

    <Fact>
    Public Sub ArmEvents_refuses_zero_unknown_and_stale_serials_and_changes_nothing()
        Dim state As New AnimationVideoState()

        ' Nothing held yet.
        Assert.False(state.ArmEvents(0))
        Assert.False(state.ArmEvents(1))
        Assert.Equal(0, state.EventSerial)

        ' A held session: other serials are refused.
        Dim current As Integer = state.Begin(Gen, AnimPath)
        Assert.False(state.ArmEvents(0))
        Assert.False(state.ArmEvents(-1))
        Assert.False(state.ArmEvents(current + 1))
        Assert.Equal(0, state.EventSerial)

        ' A session that is gone: its serial is refused.
        state.FinishCurrent()
        Assert.False(state.ArmEvents(current))
        Assert.Equal(0, state.EventSerial)

        ' A session that was replaced: only the new one may arm.
        Dim older As Integer = state.Begin(Gen, AnimPath)
        Dim newer As Integer = state.Begin(Gen, AnimPath)
        Assert.False(state.ArmEvents(older))
        Assert.Equal(0, state.EventSerial)
        Assert.True(state.ArmEvents(newer))
        Assert.Equal(newer, state.EventSerial)
    End Sub

    <Fact>
    Public Sub Arming_a_new_session_replaces_the_routing_of_the_previous_one()
        Dim state As New AnimationVideoState()
        Dim first As Integer = state.Begin(Gen, AnimPath)
        Assert.True(state.ArmEvents(first))
        state.FinishCurrent()
        Dim second As Integer = state.Begin(Gen + 1, AnimPath)

        Assert.True(state.ArmEvents(second))

        Assert.Equal(second, state.EventSerial)
    End Sub

    <Fact>
    Public Sub EventSerial_stays_non_zero_until_EventsDone_of_that_serial()
        Dim state As New AnimationVideoState()
        Dim serial As Integer = state.Begin(Gen, AnimPath)
        Assert.True(state.ArmEvents(serial))

        ' Neither a phase change nor the end of the session takes the routing away: the native stop of the
        ' media has not returned yet, so the player can still raise events for it.
        Assert.True(state.TryReveal(serial, Gen))
        Assert.Equal(serial, state.EventSerial)
        Assert.Equal(serial, state.FinishCurrent())
        Assert.Equal(serial, state.EventSerial)

        ' Other serials and the "no session" serial do not release it.
        state.EventsDone(0)
        state.EventsDone(serial + 1)
        state.EventsDone(-serial)
        Assert.Equal(serial, state.EventSerial)

        state.EventsDone(serial)
        Assert.Equal(0, state.EventSerial)
    End Sub

    <Fact>
    Public Sub Finish_does_not_release_the_event_routing_either()
        Dim state As New AnimationVideoState()
        Dim serial As Integer = state.Begin(Gen, AnimPath)
        Assert.True(state.ArmEvents(serial))

        Assert.True(state.Finish(serial))

        Assert.Equal(serial, state.EventSerial)
        state.EventsDone(serial)
        Assert.Equal(0, state.EventSerial)
    End Sub

    <Fact>
    Public Sub Begin_alone_does_not_release_the_routing_of_the_previous_session()
        Dim state As New AnimationVideoState()
        Dim old As Integer = state.Begin(Gen, AnimPath)
        Assert.True(state.ArmEvents(old))
        state.FinishCurrent()

        state.Begin(Gen + 1, AnimPath)

        Assert.Equal(old, state.EventSerial)               ' still the old one until its stop is acknowledged
        state.EventsDone(old)
        Assert.Equal(0, state.EventSerial)
    End Sub

    <Fact>
    Public Sub EventsDone_of_an_old_serial_does_not_clear_a_newer_sessions_routing()
        Dim state As New AnimationVideoState()
        Dim old As Integer = state.Begin(Gen, AnimPath)
        Assert.True(state.ArmEvents(old))
        state.FinishCurrent()                              ' the stop of the old media is still running ..
        Dim fresh As Integer = state.Begin(Gen + 1, AnimPath)
        Assert.True(state.ArmEvents(fresh))                ' .. while the next animation already plays

        state.EventsDone(old)                              ' the old stop returns late

        Assert.Equal(fresh, state.EventSerial)
        state.EventsDone(fresh)
        Assert.Equal(0, state.EventSerial)
    End Sub

    <Fact>
    Public Sub EventsDone_twice_is_harmless_and_never_touches_the_phase()
        Dim state As New AnimationVideoState()
        Dim serial As Integer = state.Begin(Gen, AnimPath)
        Assert.True(state.ArmEvents(serial))

        state.EventsDone(serial)
        state.EventsDone(serial)

        Assert.Equal(0, state.EventSerial)
        Assert.Equal("Opening", PhaseOf(state))          ' the session itself is still held
        Assert.Equal(serial, state.Serial)
        Assert.True(state.Accepts(serial, Gen))
    End Sub

    <Fact>
    Public Sub An_event_of_a_stale_serial_or_generation_is_not_accepted()
        Dim state As New AnimationVideoState()

        ' Animation A (showing 1) plays; the user moves on to animation B (showing 2) before A's stop returned.
        Dim a As Integer = state.Begin(1, "C:\pics\a.webp")
        Assert.True(state.ArmEvents(a))
        Assert.True(state.TryReveal(a, 1))
        Assert.Equal(a, state.FinishCurrent())
        Dim b As Integer = state.Begin(2, "C:\pics\b.webp")

        ' A late event of A still carries A's serial: the UI side asks Accepts and drops it.
        Assert.Equal(a, state.EventSerial)
        Assert.False(state.Accepts(state.EventSerial, 1))
        Assert.False(state.Accepts(state.EventSerial, 2))

        Assert.True(state.ArmEvents(b))
        Assert.Equal(b, state.EventSerial)
        Assert.True(state.Accepts(state.EventSerial, 2))
        Assert.False(state.Accepts(state.EventSerial, 1))   ' B's serial but a showing that is gone
        Assert.False(state.Accepts(a, 1))
        Assert.False(state.Accepts(a, 2))
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Thread-safety smoke: EventSerial is read on the libvlc thread, EventsDone is called when the native
    ' stop returns; every other member belongs to the UI thread.
    ' ---------------------------------------------------------------------------------------------

    Private NotInheritable Class Probe
        Public MaxIssued As Integer
        Public Done As Integer
    End Class

    <Fact>
    Public Sub EventSerial_is_safe_to_read_while_the_ui_thread_cycles_and_a_stop_thread_acknowledges()
        Const Cycles As Integer = 20000
        Const Readers As Integer = 4

        Dim state As New AnimationVideoState()
        Dim probe As New Probe()
        Dim errors As New ConcurrentQueue(Of String)()
        Dim stops As New BlockingCollection(Of Integer)()
        Dim threads As New List(Of Thread)()

        ' The "libvlc" threads: read the routing only; whatever they see is 0 or a serial that was issued.
        For r As Integer = 1 To Readers
            Dim reader As New Thread(New ThreadStart(
                Sub()
                    Try
                        Do While Volatile.Read(probe.Done) = 0
                            Dim seen As Integer = state.EventSerial
                            Dim issued As Integer = Volatile.Read(probe.MaxIssued)
                            If seen < 0 OrElse seen > issued Then
                                errors.Enqueue("EventSerial " & seen & " with only " & issued & " issued")
                            End If
                        Loop
                    Catch ex As Exception
                        errors.Enqueue(ex.ToString())
                    End Try
                End Sub))
            reader.IsBackground = True
            reader.Start()
            threads.Add(reader)
        Next

        ' The thread on which the native stop returns: acknowledges the serials the UI thread ended.
        Dim stopper As New Thread(New ThreadStart(
            Sub()
                Try
                    For Each ended As Integer In stops.GetConsumingEnumerable()
                        state.EventsDone(ended)
                    Next
                Catch ex As Exception
                    errors.Enqueue(ex.ToString())
                End Try
            End Sub))
        stopper.IsBackground = True
        stopper.Start()

        ' This thread plays the UI thread: whole sessions, back to back.
        For i As Integer = 1 To Cycles
            Dim serial As Integer = state.Begin(i, AnimPath)
            Volatile.Write(probe.MaxIssued, serial)
            If Not state.ArmEvents(serial) Then errors.Enqueue("arm refused at " & i)
            If Not state.Accepts(serial, i) Then errors.Enqueue("opening session not accepted at " & i)
            If Not state.TryReveal(serial, i) Then errors.Enqueue("reveal refused at " & i)
            If Not state.IsShown Then errors.Enqueue("not shown at " & i)
            If state.FinishCurrent() <> serial Then errors.Enqueue("wrong serial ended at " & i)
            stops.Add(serial)
        Next
        stops.CompleteAdding()

        Assert.True(stopper.Join(Wait), "the stop thread did not finish")
        Volatile.Write(probe.Done, 1)
        For Each t As Thread In threads
            Assert.True(t.Join(Wait), "a reader thread did not finish")
        Next

        Assert.True(errors.IsEmpty, String.Join(Environment.NewLine, errors))
        ' Every serial that was armed was acknowledged, so the routing ends released and the session idle.
        Assert.Equal(0, state.EventSerial)
        AssertIdle(state)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' AnimationVideoSurfaceRules: constants, options, reason codes
    ' ---------------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_open_timeout_is_eight_seconds()
        Assert.Equal(8000, AnimationVideoSurfaceRules.Open_Timeout_Ms)
    End Sub

    <Fact>
    Public Sub Reason_codes_are_the_pinned_distinct_non_empty_codes()
        Dim reasons As String() = {
            AnimationVideoSurfaceRules.Reason_Vlc_Unavailable,
            AnimationVideoSurfaceRules.Reason_Open,
            AnimationVideoSurfaceRules.Reason_Player_Error,
            AnimationVideoSurfaceRules.Reason_Timeout,
            AnimationVideoSurfaceRules.Reason_No_Preview,
            AnimationVideoSurfaceRules.Reason_Busy}

        Assert.Equal(New String() {"vlc-unavailable", "vlc-open", "vlc", "timeout", "no-preview", "vlc-busy"}, reasons)
        Assert.Equal(reasons.Length, New HashSet(Of String)(reasons, StringComparer.Ordinal).Count)
        For Each reason As String In reasons
            Assert.False(String.IsNullOrWhiteSpace(reason))
            ' A reason is a code for the log line, never a message: it carries no path, no address, no space.
            For Each c As Char In reason
                Assert.True((c >= "a"c AndAlso c <= "z"c) OrElse c = "-"c, "reason """ & reason & """ has the character '" & c & "'")
            Next
        Next
    End Sub

    <Fact>
    Public Sub Player_options_are_exactly_the_native_loop_with_no_audio_and_no_subtitles()
        Assert.Equal(New String() {":input-repeat=65535", ":no-audio", ":no-spu"}, AnimationVideoSurfaceRules.PlayerOptions)
    End Sub

    <Fact>
    Public Sub Player_options_carry_the_native_loop_the_viewers_own_repeat_uses()
        Dim options As String() = AnimationVideoSurfaceRules.PlayerOptions

        Assert.Contains(":input-repeat=65535", options)
        Assert.Equal(1, options.Count(Function(o) o.StartsWith(":input-repeat=", StringComparison.Ordinal)))
    End Sub

    <Fact>
    Public Sub Player_options_are_per_media_options_never_instance_arguments()
        For Each o As String In AnimationVideoSurfaceRules.PlayerOptions
            Assert.False(String.IsNullOrWhiteSpace(o))
            Assert.StartsWith(":", o, StringComparison.Ordinal)
            Assert.False(o.StartsWith("--", StringComparison.Ordinal), "option """ & o & """ is an instance argument")
        Next
    End Sub

    <Fact>
    Public Sub Player_options_write_no_file_and_open_no_network()
        Dim forbidden As String() = {"sout", "record", "dump", "demux", "logfile", "file-logging", "://", "http", "https", "rtsp", "rtp", "udp", "tcp", "ftp", "mms", "smb", "sftp"}

        For Each o As String In AnimationVideoSurfaceRules.PlayerOptions
            For Each fragment As String In forbidden
                Assert.False(o.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0,
                             "option """ & o & """ contains """ & fragment & """")
            Next
        Next
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' IsStillWanted (spec 241's three conditions: generation, the preview's identity, a living form)
    ' ---------------------------------------------------------------------------------------------

    <Theory>
    <InlineData(5, 5, True, True, True)>                ' nothing changed while the call waited
    <InlineData(0, 0, True, True, True)>
    <InlineData(Integer.MaxValue, Integer.MaxValue, True, True, True)>
    <InlineData(5, 6, True, True, False)>               ' the user moved on to another showing
    <InlineData(6, 5, True, True, False)>
    <InlineData(5, 5, False, True, False)>              ' the preview picture is no longer on screen
    <InlineData(5, 5, True, False, False)>              ' the form is closing
    <InlineData(5, 5, False, False, False)>
    <InlineData(5, 6, False, True, False)>
    <InlineData(5, 6, True, False, False)>
    <InlineData(5, 6, False, False, False)>
    Public Sub IsStillWanted_follows_generation_preview_and_form(callGeneration As Integer, currentGeneration As Integer, previewStillOnScreen As Boolean, formAlive As Boolean, expected As Boolean)
        Assert.Equal(expected, AnimationVideoSurfaceRules.IsStillWanted(callGeneration, currentGeneration, previewStillOnScreen, formAlive))
    End Sub

    <Fact>
    Public Sub IsStillWanted_is_true_only_when_all_three_conditions_hold()
        For Each sameGeneration As Boolean In New Boolean() {True, False}
            For Each previewOn As Boolean In New Boolean() {True, False}
                For Each formAlive As Boolean In New Boolean() {True, False}
                    Dim current As Integer = If(sameGeneration, 11, 12)
                    Dim expected As Boolean = sameGeneration AndAlso previewOn AndAlso formAlive

                    Assert.Equal(expected, AnimationVideoSurfaceRules.IsStillWanted(11, current, previewOn, formAlive))
                Next
            Next
        Next
    End Sub

End Class
#End If
