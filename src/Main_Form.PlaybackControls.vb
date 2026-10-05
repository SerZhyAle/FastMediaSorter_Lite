#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Windows.Forms

' The playback extras of ticket 119 - speed, seek steps, A-B repeat, frame step and
' cursor hiding - modern build only, on the LibVLC path the x86 fallback does not have.
'
' WHAT LIVES HERE: the scene glue. Every DECISION is a pure rule in PlaybackRules.vb
' and every KEY reaches this code through one of the two dispatch paths that share
' RunPlaybackAction (119 §5.0.2) - the factory keys resolved by
' CustomHotkeys.PlaybackActionOfFactoryKey in KeybUse, and the overrides answered by
' RunHotkeyAction. A rebound key and its factory key cannot drift apart.
'
' STATE OWNED HERE (119 §5.0.1): playback_Rate - what the USER asked for, never read
' back from libVLC (its Rate may not be the rate it actually plays); the A-B loop
' state and its 50 ms UI-thread watcher (no libvlc callback, nothing to deadlock
' against ticket 115); rate_Applied_Serial mirroring tracks_Applied_Serial, so the
' rate is applied once per media and never fights a resume; and the cursor-hiding
' machine of Ф-6 with its one boolean guarding the ShowCursor ref count.
Partial Public Class Main_Form

    ' ------------------------------------------------------------------ state ----

    ''' <summary>The speed the user asked for. libVLC's own Rate is only logged.</summary>
    Private playback_Rate As Single = PlaybackRules.Normal_Rate

    Private ReadOnly ab_Loop As New AbLoopState()

    ''' <summary>The A-B watcher. UI-thread timer on purpose: the loop jump is a plain
    ''' "seek back" the same way the slider is, not a native callback.</summary>
    Private WithEvents ab_Loop_Timer As New Timer() With {.Interval = 50}

    ''' <summary>Which Play() the rate was applied for - the same once-per-media guard
    ''' the preferred tracks use (audit 050 D-10).</summary>
    Private rate_Applied_Serial As Integer = -1

    Private WithEvents cursor_Hide_Timer As New Timer() With {.Interval = 250}

    ''' <summary>The stillness clock: last REAL move of the pointer, monotonic ms.</summary>
    Private cursor_Still_Since_Ms As Long = MonotonicClock.NowMs()
    Private cursor_Last_X As Integer = -1
    Private cursor_Last_Y As Integer = -1
    Private cursor_Is_Hidden As Boolean = False

    ''' <summary>Which mechanism the pointer is hidden with: paired Cursor.Hide/Show over
    ''' a playing VLC media, blank cursors on the surfaces everywhere else (spike S-3's
    ''' default, 119 §5.0.5). Tracked so a slideshow stepping onto a video while hidden
    ''' swaps mechanisms instead of doubling the ref count.</summary>
    Private cursor_Hid_Over_Video As Boolean = False

    ''' <summary>Surfaces blanked while hidden, with the cursor to restore each to.</summary>
    Private ReadOnly cursor_Blanked As New List(Of KeyValuePair(Of Control, Cursor))

    ' ------------------------------------------------------------ new media ----

    ''' <summary>
    ''' One reset for everything per-file: a DIFFERENT file starts at 1x with no A-B
    ''' marks (119 §5.1.2, Q119-1). Called from PlayVideoWithVlcAsync next to
    ''' ClearPendingVideoPosition and from both VLC stop paths. The same-file repeat
    ''' reopen is NOT this - RestartCurrentVideoPreservingPosition keeps the rate and
    ''' the marks and re-applies both (119 §5.1.3).
    ''' </summary>
    Friend Sub ResetPlaybackExtrasForNewMedia()
        playback_Rate = PlaybackRules.Normal_Rate
        ab_Loop.Clear()
        ab_Loop_Timer.Stop()
        rate_Applied_Serial = -1
        RefreshPlaybackSpeedButton()
        ClearAbIndicators()
    End Sub

    ''' <summary>Whether the extras can act on the media playing right now: a live
    ''' broadcast is nobody's playback experiment, and a pending resource unlock owns
    ''' the window (streams opened from "Open URL" pass - they ARE media).</summary>
    Private Function CanUsePlaybackExtras() As Boolean
        Return is_Vlc_Playing AndAlso vlc_Media_Player IsNot Nothing AndAlso
               Not IsLiveBroadcastActive() AndAlso resource_Pending Is Nothing
    End Function

    ''' <summary>Whether this action can act RIGHT NOW. The factory dispatch path uses
    ''' it to leave an inapplicable key alone; the override path only wants the "no
    ''' video" status, so it never calls this (119 §5.0.2).</summary>
    Private Function PlaybackActionApplicable(actionId As String) As Boolean
        ' The basics only need a LibVLC media on screen (a live broadcast included - the bar's own
        ' buttons work there too), not the stricter "extras" gate.
        If CustomHotkeys.IsVideoBasicsAction(actionId) Then Return CanUseVideoBasics()
        If Not CanUsePlaybackExtras() Then Return False
        Select Case actionId
            Case "seekBackShort", "seekFwdShort", "seekBackMedium", "seekFwdMedium", "seekBackLong", "seekFwdLong"
                Try
                    Return vlc_Media_Player.IsSeekable AndAlso vlc_Media_Player.Length > 0
                Catch
                    Return False
                End Try
            Case "frameStep"
                Return Not IsAudioNowPlaying()
            Case Else
                ' Speed: every VLC media plays at some rate. A-B answers for itself
                ' (a stream gets its own status line from the Cycle result).
                Return True
        End Select
    End Function

    ''' <summary>abLoop and speedReset ignore a held key (119 §5.0.2): nothing good
    ''' comes from re-arming a loop at 20 presses a second, and both are decisions,
    ''' not adjustments. The seek and speed-step keys keep auto-repeat on purpose.</summary>
    Private Function PlaybackActionIgnoresRepeat(actionId As String) As Boolean
        ' Pause and mute are toggles: a held key would flicker them. Volume keeps its auto-repeat.
        Return actionId = "abLoop" OrElse actionId = "speedReset" OrElse actionId = "playPause" OrElse actionId = "mute"
    End Function

    ''' <summary>Whether pause / mute / volume can act now (UX-102 of 123): a media that LibVLC owns is
    ''' the current file - a video or an audio track, a live stream included - and nothing else owns the
    ''' window. On an image this is False, so the keys do not exist there and cost the sorter nothing.</summary>
    Private Function CanUseVideoBasics() As Boolean
        Return is_Vlc_Playing AndAlso vlc_Media_Player IsNot Nothing AndAlso resource_Pending Is Nothing
    End Function

    ''' <summary>The four video basics, through the same calls the bar and the menu make.</summary>
    Private Sub RunVideoBasicsAction(actionId As String)
        Select Case actionId
            Case "playPause"
                PlayPauseFromUser()
            Case "mute"
                ToggleVideoMute()
                lbl_Status.Text = Localization.T(If(is_Video_Muted, "Звук выключен", "Звук включён"))
            Case "volumeUp", "volumeDown"
                NudgeVideoVolume(actionId = "volumeUp")
                lbl_Status.Text = Localization.TF("Громкость: {0} %", CInt(Math.Round(video_Volume_Level * 100)).ToString(Globalization.CultureInfo.InvariantCulture))
        End Select
        RefreshVideoControlsState()
        ShowVideoControls()
    End Sub

    ''' <summary>One press of a volume key (the arithmetic is <see cref="PlaybackRules.NextVolume"/>).</summary>
    Private Sub NudgeVideoVolume(up As Boolean)
        Dim [next] As PlaybackRules.VolumeState = PlaybackRules.NextVolume(video_Volume_Level, is_Video_Muted, up)
        SetVideoAudioState([next].Level, [next].Muted)
    End Sub

    ' ------------------------------------------------------------- dispatch ----

    ''' <summary>
    ''' The factory-key half of the dispatch (119 §5.0.2): resolves the combination
    ''' through CustomHotkeys.PlaybackActionOfFactoryKey and answers for it only when
    ''' an applicable action exists. Runs AFTER TryHandleCustomHotkey - a moved action
    ''' is already handled there, and its abandoned default is swallowed by
    ''' IsRetiredDefault before this is ever asked.
    ''' </summary>
    Private Function TryHandlePlaybackKey(e As KeyEventArgs) As Boolean
        Dim actionId As String = CustomHotkeys.PlaybackActionOfFactoryKey(e.KeyData)
        If actionId.Length = 0 Then Return False

        If PlaybackActionIgnoresRepeat(actionId) AndAlso IsAutoRepeatOf(e.KeyCode) Then Return True
        ' A video basic over an image is inert, including the slideshow. Do not let K/M
        ' fall through to the legacy map's unconditional SlideShowStop.
        If Not PlaybackActionApplicable(actionId) Then Return CustomHotkeys.IsVideoBasicsAction(actionId)

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2540: playback key " & e.KeyData.ToString & " -> " & actionId)
        RunPlaybackAction(actionId)
        Return True
    End Function

    ''' <summary>
    ''' The single implementation both dispatch paths call (119 §5.0.2). The override
    ''' path arrives here even when nothing is playing - it answers with a status line
    ''' and changes no state - while the factory path has already filtered those keys
    ''' out and left them to the historical map.
    ''' </summary>
    Private Sub RunPlaybackAction(actionId As String)
        If CustomHotkeys.IsVideoBasicsAction(actionId) Then
            If CanUseVideoBasics() Then RunVideoBasicsAction(actionId) Else lbl_Status.Text = Localization.T("Нет воспроизводимого видео")
            Return
        End If
        If Not CanUsePlaybackExtras() Then
            lbl_Status.Text = Localization.T("Нет воспроизводимого видео")
            Return
        End If

        Select Case actionId
            Case "speedSlower"
                ApplyPlaybackRate(PlaybackRules.NextRate(playback_Rate, faster:=False), announce:=True)
            Case "speedFaster"
                ApplyPlaybackRate(PlaybackRules.NextRate(playback_Rate, faster:=True), announce:=True)
            Case "speedReset"
                ApplyPlaybackRate(PlaybackRules.Normal_Rate, announce:=True)
            Case "seekBackShort"
                SeekByStep(False, SeekStepSec(Function(p) p.SeekShortSec), actionId)
            Case "seekFwdShort"
                SeekByStep(True, SeekStepSec(Function(p) p.SeekShortSec), actionId)
            Case "seekBackMedium"
                SeekByStep(False, SeekStepSec(Function(p) p.SeekMediumSec), actionId)
            Case "seekFwdMedium"
                SeekByStep(True, SeekStepSec(Function(p) p.SeekMediumSec), actionId)
            Case "seekBackLong"
                SeekByStep(False, SeekStepSec(Function(p) p.SeekLongSec), actionId)
            Case "seekFwdLong"
                SeekByStep(True, SeekStepSec(Function(p) p.SeekLongSec), actionId)
            Case "frameStep"
                RunFrameStep()
            Case "abLoop"
                RunAbLoop()
        End Select
    End Sub

    ''' <summary>The step of one of the three seek sizes, from the live profile.</summary>
    Private Function SeekStepSec(pick As Func(Of ModernViewerPreferences, Integer)) As Integer
        Return If(modern_Preferences Is Nothing, 3, pick(modern_Preferences))
    End Function

    ' ------------------------------------------------------------------ Ф-1 ----

    ''' <summary>
    ''' Sets the speed (119 §5.1.1). SetRate answers -1 when the media refuses - some
    ''' live inputs do - and then the state goes back to 1x rather than showing a
    ''' speed the player is not playing at.
    ''' </summary>
    Private Sub ApplyPlaybackRate(rate As Single, announce As Boolean)
        playback_Rate = rate
        RefreshPlaybackSpeedButton()
        If Not CanUsePlaybackExtras() Then Return

        Try
            Dim refused As Boolean = vlc_Media_Player.SetRate(rate) < 0
            If refused Then
                playback_Rate = PlaybackRules.Normal_Rate
                Try
                    vlc_Media_Player.SetRate(PlaybackRules.Normal_Rate)
                Catch
                End Try
                lbl_Status.Text = Localization.T("Скорость недоступна для этого файла")
                RefreshVideoControlsState()
                RefreshPlaybackSpeedButton()
                Return
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2520: SetRate failed: " & ex.Message)
            Return
        End Try

        RecomputeSlideshowFallback()
        RefreshVideoControlsState()
        RefreshPlaybackSpeedButton()
        If announce Then lbl_Status.Text = Localization.TF("Скорость: {0}", PlaybackRules.FormatRate(playback_Rate))
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2521: rate " & PlaybackRules.FormatRate(playback_Rate) &
                        " (player reports " & If(IsVlcActuallyPlaying(), vlc_Media_Player.Rate.ToString(Globalization.CultureInfo.InvariantCulture), "idle") & ")")
    End Sub

    ''' <summary>
    ''' Re-arms the slideshow's video fallback from the current length AND rate (119
    ''' §5.1.4). A rate change is the user saying "I am watching this", so the new
    ''' budget starts now rather than continuing the old countdown.
    ''' </summary>
    Private Sub RecomputeSlideshowFallback()
        If Not slideshow_Waits_For_Video OrElse vlc_Media_Player Is Nothing Then Return
        Try
            Dim dur As Long = If(vlc_Media_Player.Length > 0, vlc_Media_Player.Length, 0L)
            slideshow_Video_Fallback_Timer.Stop()
            slideshow_Video_Fallback_Timer.Interval = PlaybackRules.SlideshowVideoFallbackTimeoutMs(dur, playback_Rate)
            slideshow_Video_Fallback_Timer.Start()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2522: fallback re-arm failed: " & ex.Message)
        End Try
    End Sub

    ' ------------------------------------------------------------------ Ф-2 ----

    ''' <summary>
    ''' One seek of one size (119 Ф-2). The same Time primitive the slider's Scrubbed
    ''' uses - no new native entry point. A jump that would pass the end lands inside
    ''' the end guard and the file ends by itself through the NORMAL end path.
    ''' </summary>
    Private Sub SeekByStep(forward As Boolean, stepSec As Integer, actionId As String)
        ' The drag owns the position while it is held, and a pending restore is still
        ' on its way from the last Playing event - both make "where am I" a lie, so
        ' the step ignores them silently (119 Ф-2 edge cases).
        If video_Seek.IsDragging OrElse pending_Video_Seek >= 0.0F Then Return
        If modern_Preferences IsNot Nothing Then stepSec = PlaybackRules.ClampSeekStepSec(stepSec)

        Dim time_Ms As Long, length_Ms As Long
        Try
            time_Ms = vlc_Media_Player.Time
            length_Ms = vlc_Media_Player.Length
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2530: seek read failed: " & ex.Message)
            Return
        End Try

        Dim target As Long = PlaybackRules.SeekTargetMs(time_Ms, length_Ms, If(forward, stepSec, -stepSec))
        If target < 0 Then Return

        Try
            vlc_Media_Player.Time = target
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2531: seek failed: " & ex.Message)
            Return
        End Try

        ' What actually happened, never what was asked: near the end a long forward
        ' step reports "+0 с" instead of a lie.
        Dim applied_Sec As Long = CLng((target - time_Ms) / 1000.0R)
        lbl_Status.Text = Localization.TF("Перемотка: {0} с",
                                           If(applied_Sec >= 0, "+", "") & applied_Sec.ToString(Globalization.CultureInfo.InvariantCulture))
        RefreshVideoControlsState()
        ShowVideoControls()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2532: " & actionId & " -> " & target.ToString(Globalization.CultureInfo.InvariantCulture) & " ms")
    End Sub

    ' ------------------------------------------------------------------ Ф-5 ----

    ''' <summary>One press of the frame-step key (119 Ф-5). Playing means the press
    ''' only PAUSES - the plan guarantees one press is at most one frame without
    ''' knowing libVLC's internals (spike S-2's default).</summary>
    Private Sub RunFrameStep()
        Dim plan As PlaybackRules.FrameStepAction
        Try
            plan = PlaybackRules.PlanFrameStep(IsVlcActuallyPlaying(), vlc_Media_Player.CanPause, nextFrameAutoPauses:=False)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2533: frame step plan failed: " & ex.Message)
            Return
        End Try

        Try
            Select Case plan
                Case PlaybackRules.FrameStepAction.PauseOnly
                    SlideShowStop()
                    vlc_Media_Player.SetPause(True)
                    lbl_Status.Text = Localization.T("На паузе. Следующий кадр - ещё раз эту клавишу")
                Case PlaybackRules.FrameStepAction.StepOne
                    SlideShowStop()
                    vlc_Media_Player.NextFrame()
                Case Else
                    Return
            End Select
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2534: frame step failed: " & ex.Message)
            Return
        End Try

        ' The 250 ms clock would lag the frame the user just asked for.
        RefreshVideoControlsState()
        ShowVideoControls()
    End Sub

    ' ------------------------------------------------------------------ Ф-4 ----

    ''' <summary>One press of the A-B key: mark A, arm the span, or clear (119 Ф-4).
    ''' The Cycle result decides the status line; rejections keep the state standing.</summary>
    Private Sub RunAbLoop()
        Dim time_Ms As Long, length_Ms As Long
        Try
            time_Ms = vlc_Media_Player.Time
            length_Ms = vlc_Media_Player.Length
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2535: A-B read failed: " & ex.Message)
            Return
        End Try

        Dim result As AbCycleResult = ab_Loop.Cycle(time_Ms, length_Ms)
        Select Case result
            Case AbCycleResult.MarkedA
                lbl_Status.Text = Localization.TF("Метка A: {0}", FormatMediaTime(time_Ms))
            Case AbCycleResult.Armed
                ' Completing the span is "stay on this clip": the slideshow stops here
                ' (119 Ф-4). Marking only A does not.
                SlideShowStop()
                lbl_Status.Text = Localization.TF("Повтор A-B: {0} - {1}", FormatMediaTime(ab_Loop.AMs), FormatMediaTime(ab_Loop.BMs))
            Case AbCycleResult.Cleared
                lbl_Status.Text = Localization.T("Повтор A-B выключен")
            Case AbCycleResult.RejectedBTooClose
                lbl_Status.Text = Localization.T("Метка B слишком близко к A (нужна минимум 1 с)")
            Case AbCycleResult.RejectedBTooCloseToEnd
                lbl_Status.Text = Localization.T("Метка B слишком близко к концу")
            Case Else
                lbl_Status.Text = Localization.T("Повтор A-B требует файл с известной длиной")
        End Select

        ab_Loop_Timer.Enabled = (ab_Loop.Phase = AbPhase.Armed)
        ' The marks and the badge on the bar say it for as long as it stands (121 UX-412); the status line only for a few seconds.
        RefreshVideoControlsState()
        ShowVideoControls()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2536: A-B " & result.ToString())
    End Sub

    ''' <summary>The A-B watcher (119 Ф-4). Enabled only while the loop is armed;
    ''' a plain UI-thread seek back to A, guarded against a drag and against a
    ''' cooldown double-jump (AbLoopState.IsDueToJump).</summary>
    Private Sub Ab_Loop_Timer_Tick(sender As Object, e As EventArgs) Handles ab_Loop_Timer.Tick
        If Not is_Vlc_Playing OrElse vlc_Media_Player Is Nothing OrElse video_Seek.IsDragging Then Return
        Try
            Dim t As Long = vlc_Media_Player.Time
            If ab_Loop.IsDueToJump(t, MonotonicClock.NowMs()) Then
                vlc_Media_Player.Time = ab_Loop.AMs
                ab_Loop.RecordJump(MonotonicClock.NowMs())
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2537: A-B jump")
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2538: A-B tick failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' The defensive end-of-file path of an armed loop (119 Ф-4, decision Q119-2): a
    ''' stalled UI thread or a lost tick let the file run out inside the span. The
    ''' video reopens at A - it is NOT marked finished (position memory keeps its
    ''' mid-file point) and the slideshow does NOT advance.
    ''' </summary>
    Private Sub ReopenAtAbStart()
        Dim length_Ms As Long = 0L
        Try
            length_Ms = If(vlc_Media_Player IsNot Nothing, vlc_Media_Player.Length, 0L)
        Catch
        End Try
        Dim at As Single = If(length_Ms > 0, ab_Loop.AMs / CSng(length_Ms), -1.0F)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2539: A-B end guard reopens at " & at.ToString(Globalization.CultureInfo.InvariantCulture))
        ab_Loop_Timer.Stop()
        RestartCurrentVideoPreservingPosition(If(at > 0.0F AndAlso at < 1.0F, at, -1.0F))
        ab_Loop_Timer.Enabled = (ab_Loop.Phase = AbPhase.Armed)
    End Sub

    ' ------------------------------------------------------------------ Ф-6 ----

    ''' <summary>Whether the pointer may be hidden right now: the slideshow is running,
    ''' or a full-screen mode is on over a playing video (119 Ф-6, owner decision in
    ''' §5 - plain full screen over video behaves like the other two).</summary>
    Private Function CursorHideConditionsHold() As Boolean
        If Is_slide_show_mode Then Return True
        If is_Super_Full_Screen_Mode Then Return True
        Return is_Full_Screen_Mode AndAlso IsVlcActuallyPlaying()
    End Function

    ''' <summary>The pointer is over something the user is about to press: the floating
    ''' transport, an open menu, the recipients overlay, the revealed slideshow chrome
    ''' (119 Ф-6). Hiding over those is the classic bug this feature exists to avoid.</summary>
    Private Function PointerIsOnChromeOrMenu() As Boolean
        If Is_slide_show_mode AndAlso slideshow_Chrome_Revealed Then Return True
        ' The three checks of UX-108 of 123. An open chrome popup is the one that counts (the old
        ' video_Menu.Visible test never fired: the menus open through ChromeShowToolStripMenu and the
        ' ContextMenuStrip itself is never shown). The floating panels are windows of their own, and
        ' so are the transport and the recipients overlay - the pointer over any of them stays.
        Dim over As New List(Of Rectangle)()
        If video_Chrome_Host IsNot Nothing AndAlso Not video_Chrome_Host.IsDisposed AndAlso video_Chrome_Host.Visible Then over.Add(video_Chrome_Host.Bounds)
        If recipients_Overlay IsNot Nothing AndAlso Not recipients_Overlay.IsDisposed AndAlso recipients_Overlay.Visible Then over.Add(recipients_Overlay.Bounds)
        For Each p As ChromePanel In chrome_Panels
            If p.Host IsNot Nothing AndAlso Not p.Host.IsDisposed AndAlso p.Host.Visible Then over.Add(p.Host.Bounds)
        Next
        Return PlaybackRules.PointerIsOverChromeOrMenu(Cursor.Position, ChromeMenu.PopupVisible, over)
    End Function

    ''' <summary>A real move of the pointer seen by the chrome's own tick, wherever it happened: the media
    ''' surfaces report their moves through <see cref="OnPlaybackPointerActivity"/>, but a move over a
    ''' floating panel or the transport never reaches them, so the stillness clock kept running there and
    ''' the pointer vanished over the toolbar (UX-108 of 123). Screen coordinates, so no jitter filter.</summary>
    Private Sub NotePointerMovedOnScreen()
        cursor_Still_Since_Ms = MonotonicClock.NowMs()
        If cursor_Is_Hidden Then ShowCursorNow()
    End Sub

    ''' <summary>Every mouse move the media surfaces see lands here (119 Ф-6). Layout
    ''' jitter under a still hand is ignored; a real move shows the pointer and
    ''' restarts the stillness clock.</summary>
    Private Sub OnPlaybackPointerActivity(x As Integer, y As Integer)
        If cursor_Last_X >= 0 AndAlso Not PlaybackRules.PointerMoved(cursor_Last_X, cursor_Last_Y, x, y) Then Return
        cursor_Last_X = x
        cursor_Last_Y = y
        cursor_Still_Since_Ms = MonotonicClock.NowMs()
        If cursor_Is_Hidden Then ShowCursorNow()
    End Sub

    Private Sub Cursor_Hide_Timer_Tick(sender As Object, e As EventArgs) Handles cursor_Hide_Timer.Tick
        Dim hide_After_Sec As Integer = If(modern_Preferences Is Nothing, PlaybackRules.Hide_Cursor_Default_Sec,
                                           PlaybackRules.ClampHideCursorSec(modern_Preferences.HideCursorAfterSec))
        If Not CursorHideConditionsHold() OrElse hide_After_Sec = 0 Then
            ShowCursorNow()
            cursor_Hide_Timer.Stop()
            Return
        End If
        ' The pure rule's second mode flag stands for "full-screen viewing" in the
        ' widest sense - super-full-screen, or plain full screen over a playing video.
        If PlaybackRules.ShouldHideCursor(hide_After_Sec, Is_slide_show_mode,
                                          is_Super_Full_Screen_Mode OrElse (is_Full_Screen_Mode AndAlso IsVlcActuallyPlaying()),
                                          MonotonicClock.NowMs() - cursor_Still_Since_Ms, PointerIsOnChromeOrMenu()) Then
            HideCursorNow()
        End If
    End Sub

    ''' <summary>Starts or stops the stillness timer from every state change that can
    ''' turn the conditions on or off. The timer is NEVER always-running.</summary>
    Friend Sub UpdateCursorHideTimer()
        If cursor_Hide_Timer Is Nothing Then Return
        If CursorHideConditionsHold() AndAlso
           (modern_Preferences Is Nothing OrElse PlaybackRules.ClampHideCursorSec(modern_Preferences.HideCursorAfterSec) > 0) Then
            cursor_Hide_Timer.Start()
        Else
            cursor_Hide_Timer.Stop()
            ShowCursorNow()
        End If
    End Sub

    ''' <summary>The ONLY pair of places that touches the cursor (119 Ф-6). The boolean
    ''' guards the ref count: Cursor.Hide is counted, and a second Hide without a Show
    ''' would leave the pointer buried until the process ends.</summary>
    Private Sub HideCursorNow()
        Dim over_Video As Boolean = IsVlcActuallyPlaying()
        If cursor_Is_Hidden Then
            If cursor_Hid_Over_Video = over_Video Then Return
            ShowCursorNow()
        End If
        cursor_Is_Hidden = True
        cursor_Hid_Over_Video = over_Video
        Try
            If over_Video Then
                Cursor.Hide()
            Else
                BlankSurfaceCursors()
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2541: cursor hide failed: " & ex.Message)
            cursor_Is_Hidden = False
        End Try
    End Sub

    Private Sub ShowCursorNow()
        If Not cursor_Is_Hidden Then Return
        cursor_Is_Hidden = False
        Try
            If cursor_Hid_Over_Video Then
                Cursor.Show()
            Else
                RestoreSurfaceCursors()
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2542: cursor show failed: " & ex.Message)
        End Try
        cursor_Blanked.Clear()
    End Sub

    ''' <summary>A 1x1 transparent cursor - the "blank" mechanism for the surfaces the
    ''' viewer owns (spike S-3's default). Built once, kept for the session.</summary>
    Private Shared blank_Cursor As Cursor

    Private Shared Function BlankCursor() As Cursor
        If blank_Cursor Is Nothing Then
            Using bmp As New Bitmap(1, 1)
                Using g As Graphics = Graphics.FromImage(bmp)
                    g.Clear(Color.Transparent)
                End Using
                blank_Cursor = New Cursor(bmp.GetHicon())
            End Using
        End If
        Return blank_Cursor
    End Function

    Private Sub BlankSurfaceCursors()
        cursor_Blanked.Clear()
        For Each surface As Control In CursorHideSurfaces()
            If surface Is Nothing Then Continue For
            cursor_Blanked.Add(New KeyValuePair(Of Control, Cursor)(surface, surface.Cursor))
            surface.Cursor = BlankCursor()
        Next
    End Sub

    Private Sub RestoreSurfaceCursors()
        For Each pair As KeyValuePair(Of Control, Cursor) In cursor_Blanked
            If pair.Key IsNot Nothing AndAlso Not pair.Key.IsDisposed Then pair.Key.Cursor = pair.Value
        Next
    End Sub

    Private Function CursorHideSurfaces() As IEnumerable(Of Control)
        Dim surfaces As New List(Of Control) From {Me, panel_Media, Picture_Box_1, Picture_Box_2}
        If IsVlcActuallyPlaying() Then surfaces.Add(vlc_Video_View)
        Return surfaces
    End Function

    ''' <summary>The guarantees of 119 Ф-6: the pointer never survives a stop, a
    ''' deactivation, a teardown, or the conditions ending. Each is one call.</summary>
    Friend Sub RestorePlaybackCursorForInterruption()
        ShowCursorNow()
        cursor_Still_Since_Ms = MonotonicClock.NowMs()
    End Sub

    ''' <summary>Mouse activity on the media surfaces feeds the stillness clock. The
    ''' VLC surface reports through Vlc_Video_View_MouseMove (Main_Form.VideoControls.vb)
    ''' because it is created dynamically; these four are the designer's fields.</summary>
    Private Sub PlaybackCursor_SurfaceMove(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseMove,
                                                                                              Picture_Box_2.MouseMove,
                                                                                              panel_Media.MouseMove,
                                                                                              MyBase.MouseMove
        OnPlaybackPointerActivity(e.X, e.Y)
    End Sub

End Class
#End If
