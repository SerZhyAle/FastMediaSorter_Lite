#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing

''' <summary>
''' The small playback decisions of the .NET 10 viewer that used to be written inline in
''' Main_Form and got them wrong (audit 050). Pure - no LibVLC, no WinForms - so each rule
''' is a test instead of a scene someone has to act out with a real album or MKV.
''' Whole-file modern-only: every caller is a LibVLC path the x86 fallback does not have.
''' </summary>
Friend Module PlaybackRules

    ''' <summary>GET_APPCOMMAND_LPARAM: the high word without its device-source bits.
    ''' APPCOMMAND_MEDIA_PLAY_PAUSE is 14 (WM_APPCOMMAND, Microsoft Win32 documentation).</summary>
    Friend Function IsMediaPlayPauseCommand(lParam As Long) As Boolean
        Return ((lParam >> 16) And &HFFFL) = 14
    End Function

    ''' <summary>VLC's id for the "Disable" entry at the head of every track list.</summary>
    Friend Const Disabled_Track_Id As Integer = -1

    ''' <summary>Nothing to step to.</summary>
    Friend Const No_Track As Integer = Integer.MinValue

    ''' <summary>
    ''' Whether playback repeats (audit 050 D-14). Two settings can ask for it - the old
    ''' "repeat" flag (the video menu and the Settings checkbox) and the end action of the
    ''' kind on screen - and the checkmark, the reopen and the next file must all agree on
    ''' one answer. In slideshow mode, looping is disabled so each video plays once and advances.
    ''' </summary>
    Friend Function EffectiveRepeat(isLoopFlagOn As Boolean, isAudio As Boolean,
                                    audioEndAction As String, videoEndAction As String,
                                    Optional isSlideshow As Boolean = False) As Boolean
        If isSlideshow Then Return False
        If isLoopFlagOn Then Return True
        Return String.Equals(If(isAudio, audioEndAction, videoEndAction), "repeat", StringComparison.Ordinal)
    End Function

    ''' <summary>
    ''' Fallback timeout for a video in slideshow mode: max(duration / rate + 5s, 60s).
    ''' The rate is the one the USER asked for (ticket 119 Ф-1): at 0.5x a clip lasts
    ''' twice its length, and a timeout computed from the raw duration fired mid-video
    ''' and skipped a clip the user had deliberately slowed. rate &lt;= 0 or NaN plays
    ''' as 1.0, and a duration that cannot be known keeps today's 60 s floor.
    ''' </summary>
    Friend Function SlideshowVideoFallbackTimeoutMs(durationMs As Long, Optional rate As Single = Normal_Rate) As Integer
        Dim effective_Rate As Double = If(Single.IsNaN(rate) OrElse rate <= 0.0F, 1.0R, CDbl(rate))
        Dim baseMs As Double = If(durationMs > 0, CDbl(durationMs) / effective_Rate + 5000.0R, 60000.0R)
        ' Clamp BEFORE the Double->Long conversion: a huge duration over a slow rate is
        ' far past what a Long can hold, and the conversion would throw instead of cap.
        If baseMs >= Integer.MaxValue Then Return Integer.MaxValue
        Dim timeoutMs As Long = CLng(Math.Max(baseMs, 60000.0R))
        If timeoutMs > Integer.MaxValue Then timeoutMs = Integer.MaxValue
        Return CInt(timeoutMs)
    End Function

    ''' <summary>What a finished file does next, as the video menu and the Settings window name it: play and
    ''' stop (<c>stay</c>), the next file (<c>advance</c>), this file again (<c>repeat</c>). The stored values
    ''' differ by kind - a video calls the advance <c>nextFile</c>, a track calls it <c>next</c> - so the menu
    ''' asks through <see cref="EndModeOf"/> and <see cref="StoredEndAction"/> and never spells them itself.</summary>
    Friend Const End_Mode_Stay As String = "stay"
    Friend Const End_Mode_Advance As String = "advance"
    Friend Const End_Mode_Repeat As String = "repeat"

    ''' <summary>The mode the menu ticks for what is stored. A repeat is a repeat whatever the loop flag of the
    ''' Settings checkbox says; anything that is neither an advance nor a repeat is "play and stop".</summary>
    Friend Function EndModeOf(endAction As String, isAudio As Boolean, loopFlagOn As Boolean) As String
        If loopFlagOn OrElse String.Equals(endAction, "repeat", StringComparison.Ordinal) Then Return End_Mode_Repeat
        If String.Equals(endAction, If(isAudio, "next", "nextFile"), StringComparison.Ordinal) Then Return End_Mode_Advance
        Return End_Mode_Stay
    End Function

    ''' <summary>The value a chosen mode stores for the kind on screen - the one the Settings choices carry.</summary>
    Friend Function StoredEndAction(mode As String, isAudio As Boolean) As String
        Select Case mode
            Case End_Mode_Advance : Return If(isAudio, "next", "nextFile")
            Case End_Mode_Repeat : Return "repeat"
            Case Else : Return "stay"
        End Select
    End Function

    ''' <summary>
    ''' The track the A/V key steps to, or <see cref="No_Track"/> when there is nothing to
    ''' cycle (audit 050 D-16). Audio cycles over REAL tracks only, and only when there are
    ''' at least two: the "Disable" entry is not a choice a key press should make, and on a
    ''' plain MP4 it was the only other entry - one press of A and the sound was gone, with
    ''' no button left to bring it back. Subtitles keep "Disable": off is a real choice, and
    ''' one subtitle track already offers it.
    ''' </summary>
    Friend Function NextTrackInCycle(ids As Integer(), currentId As Integer, audio As Boolean) As Integer
        If ids Is Nothing Then Return No_Track
        Dim cycle As New List(Of Integer)
        Dim real As Integer = 0
        For Each id As Integer In ids
            If id <> Disabled_Track_Id Then real += 1
            If audio AndAlso id = Disabled_Track_Id Then Continue For
            If Not cycle.Contains(id) Then cycle.Add(id)
        Next
        If audio AndAlso real < 2 Then Return No_Track
        If Not audio AndAlso real < 1 Then Return No_Track
        Dim at As Integer = cycle.IndexOf(currentId)
        Return cycle((at + 1) Mod cycle.Count)
    End Function

        ' ---------------- ticket 119: the playback extras --------------------------
        ' Every decision the new speed / seek / A-B / frame-step / cursor features make,
        ' in the same style as EffectiveRepeat above: pure, so a test replaces the scene.

    ''' <summary>The speeds the transport offers (VLC's ladder). An off-ladder value is
    ''' never a state the user can hold - <see cref="NextRate"/> snaps onto the ladder.</summary>
    Friend ReadOnly Speed_Ladder As Single() = {0.5F, 0.75F, 1.0F, 1.25F, 1.5F, 2.0F}

    ''' <summary>The speed a new file starts at (119 §5.1.2, decision Q119-1: resets).</summary>
    Friend Const Normal_Rate As Single = 1.0F

    ''' <summary>
    ''' One step up or down the speed ladder. An off-ladder (or dead) current rate is
    ''' snapped to the NEAREST step first - ties go to the FASTER step - so a rate libVLC
    ''' reported imprecisely cannot leave the key without an answer. Clamps at the ends:
    ''' a held key must not wrap from 2x back to 0.5x (119 §5.0.4).
    ''' </summary>
    Friend Function NextRate(current As Single, faster As Boolean) As Single
        Dim rate As Single = current
        If Single.IsNaN(rate) OrElse rate <= 0.0F Then rate = Normal_Rate

        Dim best As Integer = 0
        For i As Integer = 1 To Speed_Ladder.Length - 1
            Dim better As Boolean = Math.Abs(Speed_Ladder(i) - rate) < Math.Abs(Speed_Ladder(best) - rate)
            ' "Nearest, ties up": exactly between two steps lands on the faster one.
            If Not better AndAlso Math.Abs(Speed_Ladder(i) - rate) = Math.Abs(Speed_Ladder(best) - rate) Then better = True
            If better Then best = i
        Next

        Dim at As Integer = If(faster, best + 1, best - 1)
        If at < 0 Then at = 0
        If at > Speed_Ladder.Length - 1 Then at = Speed_Ladder.Length - 1
        Return Speed_Ladder(at)
    End Function

    ''' <summary>"1x", "1.25x", "0.5x" - invariant culture, no trailing zeros, so the
    ''' transport button never shows "1,25x" on a German desktop.</summary>
    Friend Function FormatRate(rate As Single) As String
        Dim rounded As Double = Math.Round(CDbl(If(Single.IsNaN(rate) OrElse rate <= 0.0F, Normal_Rate, rate)), 2)
        Return rounded.ToString("0.##", Globalization.CultureInfo.InvariantCulture) & ChrW(&H00D7)
    End Function

    ' ---- Ф-2 seek --------------------------------------------------------------

    Friend Const Seek_Step_Min_Sec As Integer = 1
    Friend Const Seek_Step_Max_Sec As Integer = 600

    ''' <summary>How far before the end a forward jump stops: the file then ends by
    ''' itself through the normal end-of-file path - end actions, the sleep timer and
    ''' the slideshow advance all keep their ordinary behaviour (119 §5.1, Ф-2).</summary>
    Friend Const Seek_End_Guard_Ms As Long = 1000

    ''' <summary>A hand-edited profile (or an import) cannot store 0 or 99999.</summary>
    Friend Function ClampSeekStepSec(value As Integer) As Integer
        If value < Seek_Step_Min_Sec Then Return Seek_Step_Min_Sec
        If value > Seek_Step_Max_Sec Then Return Seek_Step_Max_Sec
        Return value
    End Function

    ''' <summary>
    ''' Where a seek step lands. lengthMs &lt;= 0 or timeMs &lt; 0 - a stream, or a player
    ''' that cannot say where it is - returns -1: the caller no-ops silently. Backward
    ''' clamps at 0. Forward clamps at the end guard and NEVER moves backwards: when the
    ''' playhead is already inside the guard, the jump stays where it is and lets the
    ''' file run out.
    ''' </summary>
    Friend Function SeekTargetMs(timeMs As Long, lengthMs As Long, deltaSec As Integer) As Long
        If lengthMs <= 0 OrElse timeMs < 0 Then Return -1
        Dim delta_Ms As Long = Math.Abs(CLng(deltaSec)) * 1000L
        If deltaSec <= 0 Then
            Return Math.Max(0L, timeMs - delta_Ms)
        End If
        Dim end_Guard As Long = Math.Max(0L, lengthMs - Seek_End_Guard_Ms)
        Return Math.Max(timeMs, Math.Min(timeMs + delta_Ms, end_Guard))
    End Function

    ' ---- Ф-4 A-B repeat --------------------------------------------------------

    Friend Enum AbPhase
        None
        MarkedA
        Armed
    End Enum

    Friend Enum AbCycleResult
        MarkedA
        Armed
        Cleared
        RejectedBTooClose
        RejectedBTooCloseToEnd
        ''' <summary>The media has no known length (a stream, or a player that cannot
        ''' say where it is): nothing to loop, nothing changes.</summary>
        RejectedNoLength
    End Enum

    ''' <summary>Minimum span of an armed A-B loop.</summary>
    Friend Const Ab_Min_Span_Ms As Long = 1000

    ''' <summary>B cannot be set in this window before the end, so an armed span always
    ''' reaches its B before the file ends - reaching B is never "end" (Q119-2).</summary>
    Friend Const Ab_End_Guard_Ms As Long = 500

    ''' <summary>Minimum gap between two loop jumps: Time = A is asynchronous, and the
    ''' next 50 ms tick can still read a time past B - the cooldown makes "exactly once
    ''' per pass" true (119 §5.0.4).</summary>
    Friend Const Ab_Jump_Cooldown_Ms As Long = 400

    ''' <summary>How far past B a still-armed loop tolerates the playhead before giving
    ''' up on jumping: a user seek past the span means "span is behind us, play on",
    ''' not a surprise jump backwards (119 §5.0.4).</summary>
    Friend Const Ab_Late_Window_Ms As Long = 3000

    ''' <summary>
    ''' The whole A-B state machine (the table in 119 §5.0.4 is its contract). Pure:
    ''' the watcher supplies times from the monotonic clock and the player's media time,
    ''' and this class answers whether to jump. Phases: None - nothing marked; MarkedA -
    ''' A is set, waiting for B; Armed - looping A..B.
    ''' </summary>
    Friend NotInheritable Class AbLoopState

        Private phase_Value As AbPhase = AbPhase.None
        Private a_Ms As Long
        Private b_Ms As Long
        ''' <summary>Initialised so the FIRST pass is already past the cooldown.</summary>
        Private last_Jump_Ms As Long = -Ab_Jump_Cooldown_Ms - 1

        Friend ReadOnly Property Phase As AbPhase
            Get
                Return phase_Value
            End Get
        End Property

        Friend ReadOnly Property AMs As Long
            Get
                Return a_Ms
            End Get
        End Property

        Friend ReadOnly Property BMs As Long
            Get
                Return b_Ms
            End Get
        End Property

        ''' <summary>One press of the A-B key, at the media time timeMs of a file of
        ''' lengthMs. Marks A, arms B, or clears - rejections leave the state standing.</summary>
        Friend Function Cycle(timeMs As Long, lengthMs As Long) As AbCycleResult
            If lengthMs <= 0 OrElse timeMs < 0 Then Return AbCycleResult.RejectedNoLength

            Select Case phase_Value
                Case AbPhase.None
                    phase_Value = AbPhase.MarkedA
                    a_Ms = timeMs
                    b_Ms = 0
                    Return AbCycleResult.MarkedA
                Case AbPhase.MarkedA
                    If timeMs < a_Ms + Ab_Min_Span_Ms Then Return AbCycleResult.RejectedBTooClose
                    If timeMs > lengthMs - Ab_End_Guard_Ms Then Return AbCycleResult.RejectedBTooCloseToEnd
                    phase_Value = AbPhase.Armed
                    b_Ms = timeMs
                    Return AbCycleResult.Armed
                Case Else
                    Clear()
                    Return AbCycleResult.Cleared
            End Select
        End Function

        ''' <summary>Whether the watcher should jump back to A right now. Only an armed
        ''' loop jumps, only at or after B, only within the late window (a seek past the
        ''' span plays on), and never twice within the cooldown.</summary>
        Friend Function IsDueToJump(timeMs As Long, nowMs As Long) As Boolean
            If phase_Value <> AbPhase.Armed Then Return False
            If timeMs < b_Ms OrElse timeMs >= b_Ms + Ab_Late_Window_Ms Then Return False
            Return nowMs - last_Jump_Ms >= Ab_Jump_Cooldown_Ms
        End Function

        ''' <summary>Stamps the monotonic time of a jump that was just made.</summary>
        Friend Sub RecordJump(nowMs As Long)
            last_Jump_Ms = nowMs
        End Sub

        Friend Sub Clear()
            phase_Value = AbPhase.None
            a_Ms = 0
            b_Ms = 0
            last_Jump_Ms = -Ab_Jump_Cooldown_Ms - 1
        End Sub

    End Class

    ' ---- Ф-5 frame step --------------------------------------------------------

    Friend Enum FrameStepAction
        ''' <summary>The media cannot pause (some streams): nothing to do.</summary>
        None
        ''' <summary>Playing: this press only pauses. The NEXT press steps.</summary>
        PauseOnly
        ''' <summary>Paused: show one more frame.</summary>
        StepOne
    End Enum

    ''' <summary>
    ''' What one press of the frame-step key means right now. "One press, at most one
    ''' frame" is guaranteed without knowing libVLC's internals: while PLAYING the first
    ''' press only pauses (unless the caller has proven NextFrame pauses-and-steps
    ''' itself - spike S-2, 119 §5.0.5), and while PAUSED each press steps one frame.
    ''' </summary>
    Friend Function PlanFrameStep(isPlaying As Boolean, canPause As Boolean, nextFrameAutoPauses As Boolean) As FrameStepAction
        If Not canPause Then Return FrameStepAction.None
        If isPlaying Then Return If(nextFrameAutoPauses, FrameStepAction.StepOne, FrameStepAction.PauseOnly)
        Return FrameStepAction.StepOne
    End Function

    ' ---- Ф-6 cursor hiding -----------------------------------------------------

    ''' <summary>Layout-induced "moves" under a still hand are below this and are ignored.</summary>
    Friend Const Cursor_Move_Tolerance_Px As Integer = 3

    ''' <summary>The default stillness before the pointer hides itself in a slideshow or
    ''' full-screen viewing; 0 means never.</summary>
    Friend Const Hide_Cursor_Default_Sec As Integer = 3

    ''' <summary>0 = never hide, otherwise 1..30 s.</summary>
    Friend Function ClampHideCursorSec(value As Integer) As Integer
        If value <= 0 Then Return 0
        If value > 30 Then Return 30
        Return value
    End Function

    ''' <summary>
    ''' Whether the stillness timer should hide the pointer now. Requires the user to
    ''' have asked for hiding at all (0 = never), one of the viewing modes that hide it
    ''' to be on, and the pointer to be off the chrome - hiding it over a button the
    ''' user is about to press is the classic bug (119 §5, Ф-6).
    ''' </summary>
    Friend Function ShouldHideCursor(hideAfterSec As Integer, slideshowRunning As Boolean, superFullScreen As Boolean,
                                     stillMs As Long, pointerOnChromeOrMenu As Boolean) As Boolean
        If hideAfterSec <= 0 Then Return False
        If pointerOnChromeOrMenu Then Return False
        If Not slideshowRunning AndAlso Not superFullScreen Then Return False
        Return stillMs >= CLng(hideAfterSec) * 1000L
    End Function

    ''' <summary>A real move of the pointer, not the 1-3 px jitter a relayout can produce.</summary>
    Friend Function PointerMoved(oldX As Integer, oldY As Integer, newX As Integer, newY As Integer) As Boolean
        Return Math.Abs(newX - oldX) > Cursor_Move_Tolerance_Px OrElse Math.Abs(newY - oldY) > Cursor_Move_Tolerance_Px
    End Function

    ''' <summary>The "pointer is over something the user is about to press" test of the cursor-hide rule
    ''' (UX-108 of 123): an open chrome popup, or any of the floating windows' bounds (screen coordinates).</summary>
    Friend Function PointerIsOverChromeOrMenu(at As Point, popupVisible As Boolean, windowBounds As IEnumerable(Of Rectangle)) As Boolean
        If popupVisible Then Return True
        If windowBounds Is Nothing Then Return False
        For Each bounds As Rectangle In windowBounds
            If Not bounds.IsEmpty AndAlso bounds.Contains(at) Then Return True
        Next
        Return False
    End Function

    ' ---- ticket 123 Ф-3: the volume keys ---------------------------------------

    ''' <summary>One press of a volume key moves the level by this much (0..1 scale).</summary>
    Friend Const Volume_Key_Step As Double = 0.05

    ''' <summary>Volume level and mute flag together, as the viewer keeps them.</summary>
    Friend Structure VolumeState
        Friend Level As Double
        Friend Muted As Boolean
    End Structure

    ''' <summary>
    ''' The state after one press of the volume-up or volume-down key. The level steps by
    ''' <see cref="Volume_Key_Step"/> and stays inside 0..1 (rounded to the step so repeated presses never
    ''' drift to 0.1500000001). Raising the volume while muted unmutes - pressing "louder" and hearing
    ''' nothing is the one outcome nobody means - while lowering it never changes the mute flag.
    ''' </summary>
    Friend Function NextVolume(level As Double, muted As Boolean, up As Boolean) As VolumeState
        Dim base As Double = If(Double.IsNaN(level), 0.0R, Math.Max(0.0R, Math.Min(1.0R, level)))
        Dim stepped As Double = base + If(up, Volume_Key_Step, -Volume_Key_Step)
        Dim rounded As Double = Math.Round(Math.Max(0.0R, Math.Min(1.0R, stepped)) / Volume_Key_Step) * Volume_Key_Step
        Return New VolumeState With {.Level = Math.Max(0.0R, Math.Min(1.0R, rounded)), .Muted = If(up, False, muted)}
    End Function

End Module

''' <summary>
''' The sleep timer's deadline (audit 050 D-03). It is armed when the user starts listening
''' and SURVIVES the automatic advance to the next track - restarting it on every track, as
''' the old start time did, meant an album of tracks shorter than the timer never stopped,
''' which is exactly the case the feature exists for. An explicit start re-arms it; a change
''' of the setting re-arms it with the new length. Times are milliseconds of a monotonic
''' clock supplied by the caller, never the wall clock (045 §3 theme 5).
''' </summary>
Friend NotInheritable Class SleepDeadline

    Private deadline_Ms As Long = -1
    Private armed_Minutes As Integer = 0

    Friend ReadOnly Property IsArmed As Boolean
        Get
            Return deadline_Ms >= 0
        End Get
    End Property

    ''' <summary>A track started. <paramref name="automatic"/> is True for an end-of-track
    ''' advance or a slideshow step, which keep a running deadline.</summary>
    Friend Sub TrackStarted(nowMs As Long, minutes As Integer, automatic As Boolean)
        If minutes <= 0 Then
            Disarm()
            Return
        End If
        If automatic AndAlso IsArmed AndAlso armed_Minutes = minutes Then Return
        deadline_Ms = nowMs + minutes * 60000L
        armed_Minutes = minutes
    End Sub

    ''' <summary>True once the deadline has passed. <paramref name="minutes"/> is the
    ''' current setting: a timer switched off meanwhile never fires, and a changed length
    ''' re-arms from now rather than firing on the old one.</summary>
    Friend Function IsDue(nowMs As Long, minutes As Integer) As Boolean
        If minutes <= 0 Then
            Disarm()
            Return False
        End If
        If Not IsArmed Then Return False
        If minutes <> armed_Minutes Then
            deadline_Ms = nowMs + minutes * 60000L
            armed_Minutes = minutes
            Return False
        End If
        Return nowMs >= deadline_Ms
    End Function

    Friend Sub Disarm()
        deadline_Ms = -1
        armed_Minutes = 0
    End Sub

End Class
#End If
