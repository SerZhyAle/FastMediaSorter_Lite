#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The small playback decisions of the .NET 10 viewer that used to be written inline in
''' Main_Form and got them wrong (audit 050). Pure - no LibVLC, no WinForms - so each rule
''' is a test instead of a scene someone has to act out with a real album or MKV.
''' Whole-file modern-only: every caller is a LibVLC path the x86 fallback does not have.
''' </summary>
Friend Module PlaybackRules

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
    ''' Fallback timeout for a video in slideshow mode: max(duration + 5s, 60s).
    ''' </summary>
    Friend Function SlideshowVideoFallbackTimeoutMs(durationMs As Long) As Integer
        Dim baseMs As Long = If(durationMs > 0, durationMs + 5000L, 60000L)
        Dim timeoutMs As Long = Math.Max(baseMs, 60000L)
        If timeoutMs > Integer.MaxValue Then timeoutMs = Integer.MaxValue
        Return CInt(timeoutMs)
    End Function

    ''' <summary>The end action to keep once the user turns repeat OFF from the menu: a
    ''' "repeat" end action would otherwise switch it straight back on for the next file.
    ''' "stay" is the value of both lists that means "do nothing at the end".</summary>
    Friend Function EndActionAfterRepeatOff(endAction As String) As String
        Return If(String.Equals(endAction, "repeat", StringComparison.Ordinal), "stay", endAction)
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
