#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization

''' <summary>
''' The decisions of the modern viewer's video transport bar (ticket 121, section 7), pure - no WinForms
''' control, no LibVLC - so each is a test instead of a scene to act out with a real video. The bar's glue
''' (<c>Main_Form.VideoControls.vb</c>) only reads the live state, asks here and applies the answer.
''' Whole-file modern-only, like the bar itself (the net48 fallback has IE's own controls).
''' </summary>
Friend Module TransportRules

    ' --- the row (UX-406, UX-410) ----------------------------------------------------------------

    ''' <summary>Everything the row's arithmetic needs, in device pixels.</summary>
    Friend Structure RowInput
        Friend Width As Integer
        Friend Height As Integer
        Friend Pad As Integer
        Friend ButtonWidth As Integer
        Friend SpeedWidth As Integer
        Friend TimeWidth As Integer
        ''' <summary>The A-B badge by the speed button; 0 when no span is marked.</summary>
        Friend BadgeWidth As Integer
        Friend VolumeWidth As Integer
        Friend SeekMinimum As Integer
    End Structure

    ''' <summary>Where each part of the row goes; <see cref="Rectangle.Empty"/> = left out because it does not fit.</summary>
    Friend Structure RowLayout
        Friend Play As Rectangle
        Friend Seek As Rectangle
        Friend Time As Rectangle
        Friend Badge As Rectangle
        Friend Speed As Rectangle
        Friend Mute As Rectangle
        Friend Volume As Rectangle
        Friend More As Rectangle
    End Structure

    ''' <summary>
    ''' The row, left to right: play | seek (stretches) | clock | A-B badge | speed | mute | volume | more.
    ''' The bar is pinned left-to-right (APP-BEHAVIOUR rule 8), so the order never mirrors. Play, seek and the
    ''' "more" button are always there - the menu behind the latter carries every other operation. When the seek
    ''' track would be narrower than its minimum, parts are dropped in the order of least loss: the volume slider
    ''' (the keys, the wheel and the menu still reach it), the clock, the badge, the speed button, the mute button.
    ''' The two sliders and the clock take the bar's FULL height as their hit target (UX-409: 18 px of a 38 px bar
    ''' was the click zone); the painted track stays centred inside it.
    ''' </summary>
    Friend Function ArrangeRow(input As RowInput) As RowLayout
        Dim width As Integer = Math.Max(0, input.Width)
        Dim height As Integer = Math.Max(0, input.Height)
        Dim pad As Integer = Math.Max(0, input.Pad)

        Dim showVolume As Boolean = input.VolumeWidth > 0
        Dim showTime As Boolean = input.TimeWidth > 0
        Dim showBadge As Boolean = input.BadgeWidth > 0
        Dim showSpeed As Boolean = input.SpeedWidth > 0
        Dim showMute As Boolean = True

        Dim result As New RowLayout()
        Do
            Dim right As Integer = width
            result.More = New Rectangle(right - input.ButtonWidth, 0, input.ButtonWidth, height)
            right = result.More.Left
            result.Volume = Rectangle.Empty
            If showVolume Then
                result.Volume = New Rectangle(right - pad - input.VolumeWidth, 0, input.VolumeWidth, height)
                right = result.Volume.Left
            End If
            result.Mute = Rectangle.Empty
            If showMute Then
                result.Mute = New Rectangle(right - input.ButtonWidth, 0, input.ButtonWidth, height)
                right = result.Mute.Left
            End If
            result.Speed = Rectangle.Empty
            If showSpeed Then
                result.Speed = New Rectangle(right - input.SpeedWidth, 0, input.SpeedWidth, height)
                right = result.Speed.Left
            End If
            result.Badge = Rectangle.Empty
            If showBadge AndAlso showSpeed Then
                result.Badge = New Rectangle(right - input.BadgeWidth, 0, input.BadgeWidth, height)
                right = result.Badge.Left
            End If
            result.Time = Rectangle.Empty
            If showTime Then
                result.Time = New Rectangle(right - pad - input.TimeWidth, 0, input.TimeWidth, height)
                right = result.Time.Left
            End If

            result.Play = New Rectangle(0, 0, input.ButtonWidth, height)
            Dim seekLeft As Integer = result.Play.Right + pad
            Dim seekWidth As Integer = right - pad - seekLeft
            If seekWidth >= input.SeekMinimum OrElse Not (showVolume OrElse showTime OrElse showBadge OrElse showSpeed OrElse showMute) Then
                result.Seek = New Rectangle(seekLeft, 0, Math.Max(input.SeekMinimum, seekWidth), height)
                Exit Do
            End If
            ' One part goes per round, the cheapest loss first.
            If showVolume Then
                showVolume = False
            ElseIf showTime Then
                showTime = False
            ElseIf showBadge Then
                showBadge = False
            ElseIf showSpeed Then
                showSpeed = False
            Else
                showMute = False
            End If
        Loop
        Return result
    End Function

    ' --- the clock (UX-410) ----------------------------------------------------------------------

    ''' <summary>mm:ss, or h:mm:ss once it is worth it. A stream of unknown length reports -1 - dashes, not a bogus
    ''' clock. (The one formatter: the bar, the A-B status lines and the hover time all use it.)</summary>
    Friend Function FormatMediaTime(ms As Long) As String
        If ms < 0 Then Return "--:--"
        Dim t As TimeSpan = TimeSpan.FromMilliseconds(ms)
        If t.TotalHours >= 1 Then
            Return CInt(Math.Floor(t.TotalHours)).ToString(CultureInfo.InvariantCulture) & ":" &
                   t.Minutes.ToString("00", CultureInfo.InvariantCulture) & ":" & t.Seconds.ToString("00", CultureInfo.InvariantCulture)
        End If
        Return t.Minutes.ToString("00", CultureInfo.InvariantCulture) & ":" & t.Seconds.ToString("00", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>The clock's words: "elapsed / total", or "-remaining / total" once the user asked for the
    ''' remaining time. An unknown length (a stream) has no remaining time and shows its elapsed time.</summary>
    Friend Function FormatClock(positionMs As Long, lengthMs As Long, showRemaining As Boolean) As String
        If showRemaining AndAlso lengthMs > 0 AndAlso positionMs >= 0 Then
            Return "-" & FormatMediaTime(Math.Max(0L, lengthMs - positionMs)) & " / " & FormatMediaTime(lengthMs)
        End If
        Return FormatMediaTime(positionMs) & " / " & FormatMediaTime(lengthMs)
    End Function

    ''' <summary>The widest string the clock can show for a file of this length, every digit an "8" and the minus
    ''' included: what the label's width is measured with, so "1:23:45 / 2:34:56" never clips (UX-410).</summary>
    Friend Function ClockSample(lengthMs As Long) As String
        Dim total As String = FormatMediaTime(Math.Max(0L, lengthMs))
        Dim chars As Char() = total.ToCharArray()
        For i As Integer = 0 To chars.Length - 1
            If Char.IsDigit(chars(i)) Then chars(i) = "8"c
        Next
        Dim wide As New String(chars)
        Return "-" & wide & " / " & wide
    End Function

    ' --- the seek track (UX-409) ----------------------------------------------------------------

    ''' <summary>How long after a release the 250 ms tick may not move the thumb back: a seek takes a moment to
    ''' reach the player's own position, and a tick in that moment returned the thumb to where it was.</summary>
    Friend Const Seek_Hold_Ms As Long = 700

    ''' <summary>Where along a track of this width a point at x lies, 0..1.</summary>
    Friend Function FractionAt(x As Integer, trackWidth As Integer) As Double
        If trackWidth <= 0 Then Return 0.0R
        Return Math.Max(0.0R, Math.Min(1.0R, x / CDbl(trackWidth)))
    End Function

    ''' <summary>The time under the pointer for the seek track's tooltip; "" while the length is not known.</summary>
    Friend Function SeekHoverText(x As Integer, trackWidth As Integer, lengthMs As Long) As String
        If lengthMs <= 0 Then Return ""
        Return FormatMediaTime(CLng(Math.Round(FractionAt(x, trackWidth) * lengthMs)))
    End Function

    ''' <summary>Whether the periodic refresh may write the player's position into the thumb: not while the user holds
    ''' it, and not in the moment after a release (<see cref="Seek_Hold_Ms"/>).</summary>
    Friend Function SeekTickMayOverwrite(isDragging As Boolean, nowMs As Long, holdUntilMs As Long) As Boolean
        Return Not isDragging AndAlso nowMs >= holdUntilMs
    End Function

    ''' <summary>A release on the track seeks only a media that can: a live stream and a file of unknown length
    ''' cannot, and a position written to them is a surprise.</summary>
    Friend Function CanSeekTo(isSeekable As Boolean, lengthMs As Long) As Boolean
        Return isSeekable AndAlso lengthMs > 0
    End Function

    ' --- the volume (UX-408) --------------------------------------------------------------------

    ''' <summary>The glyph of the mute button: the speaker is crossed out while muted AND while the level is zero -
    ''' a slider dragged to the left end is silence, and an unchanged speaker beside it said otherwise.</summary>
    Friend Function VolumeGlyphId(muted As Boolean, level As Double) As String
        Return If(muted OrElse Double.IsNaN(level) OrElse level < 0.005R, "media.mute", "media.volume")
    End Function

    ''' <summary>The level as the percent the status line says.</summary>
    Friend Function VolumePercent(level As Double) As Integer
        If Double.IsNaN(level) Then Return 0
        Return CInt(Math.Round(Math.Max(0.0R, Math.Min(1.0R, level)) * 100.0R))
    End Function

    ' --- A-B repeat (UX-412) --------------------------------------------------------------------

    ''' <summary>The marks on the seek track as fractions of the length; -1 = not marked.</summary>
    Friend Structure AbMarks
        Friend A As Double
        Friend B As Double
    End Structure

    ''' <summary>Where the A and B marks sit on the track. Only a known length can place them.</summary>
    Friend Function AbMarksFor(phase As PlaybackRules.AbPhase, aMs As Long, bMs As Long, lengthMs As Long) As AbMarks
        Dim marks As New AbMarks With {.A = -1.0R, .B = -1.0R}
        If lengthMs <= 0 OrElse phase = PlaybackRules.AbPhase.None Then Return marks
        marks.A = Math.Max(0.0R, Math.Min(1.0R, aMs / CDbl(lengthMs)))
        If phase = PlaybackRules.AbPhase.Armed Then marks.B = Math.Max(0.0R, Math.Min(1.0R, bMs / CDbl(lengthMs)))
        Return marks
    End Function

    ''' <summary>The badge by the speed button: "A" while only the start is marked, "A-B" while the loop runs.
    ''' Language-neutral on purpose, like the speed caption.</summary>
    Friend Function AbBadgeText(phase As PlaybackRules.AbPhase) As String
        Select Case phase
            Case PlaybackRules.AbPhase.MarkedA : Return "A"
            Case PlaybackRules.AbPhase.Armed : Return "A-B"
            Case Else : Return ""
        End Select
    End Function

    ' --- repeat (UX-413) ------------------------------------------------------------------------

    ''' <summary>
    ''' Whether switching repeat needs the video reopened. ":input-repeat" is baked in when Play() is called and
    ''' libvlc has no call that changes it on a running media, so turning repeat OFF under a media that was started
    ''' with it must reopen (the file would otherwise loop for ever under a status that says "off"). Turning it ON
    ''' needs nothing: the end of the pass is caught by the viewer itself (<see cref="RepeatAtEnd"/>), so the video
    ''' goes on playing instead of being reopened at the same second.
    ''' </summary>
    Friend Function RepeatToggleNeedsReopen(startedWithRepeat As Boolean, repeatNow As Boolean) As Boolean
        Return startedWithRepeat AndAlso Not repeatNow
    End Function

    ''' <summary>The end of a pass reopens the file when repeat is on and the media was NOT started with the native
    ''' loop (a media started with it never reports an end). A slideshow and a live stream never repeat.</summary>
    Friend Function RepeatAtEnd(repeatNow As Boolean, startedWithRepeat As Boolean, isLive As Boolean) As Boolean
        Return repeatNow AndAlso Not startedWithRepeat AndAlso Not isLive
    End Function

    ' --- the bar's lifetime (UX-402, UX-404) ----------------------------------------------------

    ''' <summary>What can hold the bar on screen when its hide countdown runs out.</summary>
    Friend Structure HoldState
        Friend SliderHeld As Boolean
        Friend PointerOnBar As Boolean
        ''' <summary>A chrome popup menu is open (the video menu, the speed ladder, a panel's menu).</summary>
        Friend PopupOpen As Boolean
        ''' <summary>The F10 keyboard ring is on.</summary>
        Friend KeyboardRingOn As Boolean
        Friend PausedAndKept As Boolean
    End Structure

    ''' <summary>True = the countdown restarts instead of the bar going away. A menu opened FROM the bar must never
    ''' lose its anchor under it (UX-402), and the keyboard ring is a place the user is standing in.</summary>
    Friend Function ShouldKeepBarUp(state As HoldState) As Boolean
        Return state.SliderHeld OrElse state.PointerOnBar OrElse state.PopupOpen OrElse state.KeyboardRingOn OrElse state.PausedAndKept
    End Function

    ''' <summary>Whether the sleep timer must be polled. It used to ride the bar's 250 ms tick, which stops with the
    ''' bar - so in the full screen without panels, where the bar is never shown, music played past the deadline
    ''' (UX-404). Now a timer of its own runs exactly while this is True.</summary>
    Friend Function SleepCheckNeeded(audioNowPlaying As Boolean, vlcPlaying As Boolean, alreadyStopped As Boolean) As Boolean
        Return audioNowPlaying AndAlso vlcPlaying AndAlso Not alreadyStopped
    End Function

    ' --- opening and buffering (UX-405) ---------------------------------------------------------

    ''' <summary>
    ''' The next buffering percentage worth saying, or -1 to stay quiet. libvlc reports a cache level many times a
    ''' second; the status line says it in steps of 5 % (never twice the same step) and says 100 once to go back to the
    ''' ordinary sentence. <paramref name="lastStep"/> is the step shown last (-1 = none yet).
    ''' </summary>
    Friend Function NextBufferingStep(cache As Single, lastStep As Integer) As Integer
        If Single.IsNaN(cache) Then Return -1
        If cache >= 99.5F Then Return If(lastStep >= 0 AndAlso lastStep < 100, 100, -1)
        Dim stepValue As Integer = CInt(Math.Floor(Math.Max(0.0F, cache) / 5.0F)) * 5
        Return If(stepValue = lastStep, -1, stepValue)
    End Function

    ' --- the strip under the bar (UX-415) -------------------------------------------------------

    ''' <summary>
    ''' The top of the status panel. While a video or a track is open it always stands above the bar's slot - the
    ''' bar appearing and going away used to carry the panel down and up by the bar's height.
    ''' </summary>
    Friend Function StatusTop(mediaBottom As Integer, panelHeight As Integer, inset As Integer, margin As Integer,
                              barHeight As Integer, reserveBarSlot As Boolean) As Integer
        If reserveBarSlot Then Return mediaBottom - barHeight - panelHeight - margin * 2
        Return mediaBottom - panelHeight - inset
    End Function

    ' --- the keyboard ring (UX-403) -------------------------------------------------------------

    ''' <summary>The next stop of the bar's own ring, or -1 when the step leaves it (the caller moves on to the
    ''' neighbouring panel). The ring does not wrap inside the bar.</summary>
    Friend Function RingStep(current As Integer, count As Integer, forward As Boolean) As Integer
        If count <= 0 Then Return -1
        Dim target As Integer = current + If(forward, 1, -1)
        If target < 0 OrElse target >= count Then Return -1
        Return target
    End Function

    ''' <summary>The action an arrow key does on the focused slider of the ring, by the id the playback keys use
    ''' (so the ring, the keys and the wheel are one implementation): a short seek step for the seek bar, a medium
    ''' one for the page keys, the volume key for the volume slider.</summary>
    Friend Function RingSliderAction(isSeek As Boolean, up As Boolean, page As Boolean) As String
        If isSeek Then
            If page Then Return If(up, "seekFwdMedium", "seekBackMedium")
            Return If(up, "seekFwdShort", "seekBackShort")
        End If
        Return If(up, "volumeUp", "volumeDown")
    End Function
End Module
#End If
