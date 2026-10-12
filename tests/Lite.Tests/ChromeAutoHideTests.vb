#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports Xunit

''' <summary>
''' The pure auto-hide cycle of the floating chrome (117). The leave-to-hide decision, the fade
''' step and the pin algebra the panels share with the recipients overlay - all as functions of
''' an injected clock, with no window and no timer.
''' </summary>
Public Class ChromeAutoHideTests

    ' --- the leave-to-hide countdown (117 Ф-1) -------------------------------------

    Private Structure TickState
        Public Wanted As Boolean
        Public LastActivity As Long
    End Structure

    ''' <summary>One tick through the policy. Argument order: nowMs, pinned, over, hold, hotZone, active.</summary>
    Private Shared Function Step_(ByRef state As TickState, nowMs As Long, pinned As Boolean,
                                  over As Boolean, hold As Boolean, hotZone As Boolean,
                                  active As Boolean) As Boolean
        Return ChromeAutoHide.NextWanted(nowMs, state.Wanted, state.LastActivity,
                                         pinned, over, hold, hotZone, active)
    End Function

    <Fact>
    Public Sub Countdown_starts_at_the_leave_and_hides_after_exactly_five_seconds()
        Dim state As New TickState With {.Wanted = True, .LastActivity = 0}
        ' Held up by the pointer: the activity keeps refreshing, so the countdown is zero on leave.
        Assert.True(Step_(state, 1000, False, True, False, False, True))
        Assert.Equal(1000L, state.LastActivity)
        Assert.True(Step_(state, 2500, False, True, False, False, True))
        Assert.Equal(2500L, state.LastActivity)
        ' Left at 2500: one millisecond before the boundary it is still up.
        Assert.True(Step_(state, 2500 + ChromeMetrics.PanelHideDelayMilliseconds - 1, False, False, False, False, True))
        ' At the boundary it lets go.
        Assert.False(Step_(state, 2500 + ChromeMetrics.PanelHideDelayMilliseconds, False, False, False, False, True))
    End Sub

    <Fact>
    Public Sub Re_entry_cancels_the_countdown_and_restarts_it_on_the_next_leave()
        Dim state As New TickState With {.Wanted = True, .LastActivity = 0}
        Assert.True(Step_(state, 1000, False, True, False, False, True))
        ' Left at 1000, gone for 3 s, back over the panel while it is still fading.
        Assert.True(Step_(state, 4000, False, True, False, False, True))
        Assert.Equal(4000L, state.LastActivity)
        ' Left again at 4000: a fresh 5 s, not the remainder of the first one.
        Assert.True(Step_(state, 4000 + ChromeMetrics.PanelHideDelayMilliseconds - 1, False, False, False, False, True))
        Assert.False(Step_(state, 4000 + ChromeMetrics.PanelHideDelayMilliseconds, False, False, False, False, True))
    End Sub

    <Fact>
    Public Sub The_countdown_is_the_fixed_five_second_constant_not_the_video_setting()
        ' 117 Ф-1: the panels stopped borrowing VideoControlsHideDelaySec (default 3 s). The
        ' boundary must sit at the chrome's own constant - visibly not at the video default.
        Assert.Equal(5000, ChromeMetrics.PanelHideDelayMilliseconds)
        Dim state As New TickState With {.Wanted = True, .LastActivity = 0}
        Assert.True(Step_(state, 3000, False, False, False, False, True), "3 s (the video default) must not hide a panel")
        Assert.False(Step_(state, 5000, False, False, False, False, True))
    End Sub

    <Fact>
    Public Sub Losing_window_activation_hides_an_unpinned_panel_immediately()
        Dim state As New TickState With {.Wanted = True, .LastActivity = 0}
        Assert.True(Step_(state, 1000, False, True, False, False, True))
        ' One millisecond after the leave the window deactivates: no countdown grace.
        Assert.False(Step_(state, 1001, False, False, False, False, False))
    End Sub

    <Fact>
    Public Sub Holds_keep_the_panel_up_only_while_the_window_is_active()
        Dim state As New TickState With {.Wanted = True, .LastActivity = 0}
        ' A popup / path editing / keyboard mode: up, and the countdown does not run.
        Assert.True(Step_(state, 20000, False, False, True, False, True))
        Assert.Equal(20000L, state.LastActivity)
        ' The same hold with the window deactivated: it drops (only over/pinned survive that).
        Assert.False(Step_(state, 20001, False, False, True, False, False))
    End Sub

    <Fact>
    Public Sub Hot_zone_and_pin_reveal_and_keep_the_panel_up()
        Dim state As New TickState With {.Wanted = False, .LastActivity = 0}
        Assert.True(Step_(state, 5000, False, False, False, True, True))
        Assert.Equal(5000L, state.LastActivity)
        ' Pinned stays up whatever else is true - even an inactive window.
        Assert.True(Step_(state, 99000, True, False, False, False, False))
        Assert.Equal(99000L, state.LastActivity)
    End Sub

    <Fact>
    Public Sub A_hold_reveals_a_hidden_panel_and_a_quiet_tick_keeps_it_hidden()
        Dim state As New TickState With {.Wanted = False, .LastActivity = 0}
        ' A hold (open popup, keyboard mode) summons a hidden panel at once - that is how F10 works.
        Assert.True(Step_(state, 1000, False, False, True, False, True))
        Assert.Equal(1000L, state.LastActivity)
        ' With nothing holding it and the pointer away, it stays hidden for good.
        Dim quiet As New TickState With {.Wanted = False, .LastActivity = 0}
        Assert.False(Step_(quiet, 1000, False, False, False, False, True))
        Assert.False(Step_(quiet, 900000, False, False, False, False, True))
    End Sub

    <Fact>
    Public Sub Surface_without_holds_is_not_summoned_by_unrelated_popups()
        ' The recipients overlay carries no holds: an open popup on the toolbar must not reveal it.
        Dim state As New TickState With {.Wanted = False, .LastActivity = 0}
        state.Wanted = Step_(state, 1000, pinned:=False, over:=False, hold:=False, hotZone:=False, active:=True)
        Assert.False(state.Wanted)
        ' Reveal by pointer hotZone still works.
        state.Wanted = Step_(state, 2000, pinned:=False, over:=False, hold:=False, hotZone:=True, active:=True)
        Assert.True(state.Wanted)
        ' Leaves the hotZone: stays wanted for the 5-second countdown.
        state.Wanted = Step_(state, 2000 + ChromeMetrics.PanelHideDelayMilliseconds - 1, pinned:=False, over:=False, hold:=False, hotZone:=False, active:=True)
        Assert.True(state.Wanted)
        ' Past 5 seconds: hides.
        state.Wanted = Step_(state, 2000 + ChromeMetrics.PanelHideDelayMilliseconds, pinned:=False, over:=False, hold:=False, hotZone:=False, active:=True)
        Assert.False(state.Wanted)
    End Sub

    ' --- the fade step ----------------------------------------------------------------------

    <Fact>
    Public Sub Fade_moves_half_way_in_half_the_fade_time_and_never_overshoots()
        Assert.Equal(0.5R, ChromeAutoHide.NextAlpha(1.0R, 0.0R, True, ChromeMetrics.FadeMilliseconds \ 2), 10)
        Assert.Equal(1.0R, ChromeAutoHide.NextAlpha(1.0R, 0.0R, True, ChromeMetrics.FadeMilliseconds * 10), 10)
        Assert.Equal(0.5R, ChromeAutoHide.NextAlpha(0.0R, 1.0R, True, ChromeMetrics.FadeMilliseconds \ 2), 10)
        Assert.Equal(0.0R, ChromeAutoHide.NextAlpha(0.0R, 1.0R, True, ChromeMetrics.FadeMilliseconds * 10), 10)
    End Sub

    <Fact>
    Public Sub Without_animation_the_alpha_jumps_and_a_transition_tick_moves_nothing()
        Assert.Equal(1.0R, ChromeAutoHide.NextAlpha(1.0R, 0.25R, False, 0), 10)
        Assert.Equal(0.0R, ChromeAutoHide.NextAlpha(0.0R, 0.75R, False, 150), 10)
        Assert.Equal(0.5R, ChromeAutoHide.NextAlpha(1.0R, 0.5R, True, 0), 10)
    End Sub

    ' --- the pin algebra the overlay follows (117 Ф-2, 2026-10-04 revision) -------------------

    <Fact>
    Public Sub Panel_pin_truth_table_global_switch_or_the_surface_own_pin()
        For Each globalOn In New Boolean() {False, True}
            For Each row1 In New Boolean() {False, True}
                For Each row2 In New Boolean() {False, True}
                    For Each status In New Boolean() {False, True}
                        For Each overlay In New Boolean() {False, True}
                            Dim state As New ChromeAutoHide.ChromePinState With {
                                .GlobalPinned = globalOn, .Row1Pinned = row1,
                                .Row2Pinned = row2, .StatusPinned = status, .OverlayPinned = overlay}
                            Assert.Equal(globalOn OrElse row1, ChromeAutoHide.PanelPinned(state, "chrome_Row1"))
                            Assert.Equal(globalOn OrElse row2, ChromeAutoHide.PanelPinned(state, "chrome_Row2"))
                            Assert.Equal(globalOn OrElse status, ChromeAutoHide.PanelPinned(state, "chrome_Status"))
                            ' The destination table is a fourth floating surface with a pin of its own.
                            Assert.Equal(globalOn OrElse overlay, ChromeAutoHide.PanelPinned(state, "recipients_Overlay"))
                        Next
                    Next
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub Unpinning_one_panel_while_the_global_switch_is_on_splits_it_into_three_pins_minus_one()
        Dim state As New ChromeAutoHide.ChromePinState With {.GlobalPinned = True}
        ' Acceptance 117.4: the click must do something visible - the all-switch becomes the three
        ' individual pins minus the clicked panel.
        state = ChromeAutoHide.TogglePin(state, "chrome_Row1")
        Assert.False(state.GlobalPinned)
        Assert.False(state.Row1Pinned)
        Assert.True(state.Row2Pinned)
        Assert.True(state.StatusPinned)
        ' The table keeps its own pin untouched: the split no longer speaks for it.
        Assert.False(state.OverlayPinned)
        Assert.True(ChromeAutoHide.PanelPinned(state, "chrome_Row2"))
        Assert.False(ChromeAutoHide.PanelPinned(state, "chrome_Row1"))
        Assert.False(ChromeAutoHide.PanelPinned(state, "recipients_Overlay"))
        ' Pinning the missing panel by hand brings it back.
        state = ChromeAutoHide.TogglePin(state, "chrome_Row1")
        Assert.True(ChromeAutoHide.PanelPinned(state, "chrome_Row1"))
    End Sub

    <Fact>
    Public Sub Pin_clicks_off_the_global_switch_turn_a_single_own_pin()
        Dim state As New ChromeAutoHide.ChromePinState
        state = ChromeAutoHide.TogglePin(state, "chrome_Status")
        Assert.False(state.GlobalPinned)
        Assert.True(state.StatusPinned)
        Assert.True(ChromeAutoHide.PanelPinned(state, "chrome_Status"))
        Assert.False(ChromeAutoHide.PanelPinned(state, "chrome_Row1"))
        ' And the same click again unpins it.
        state = ChromeAutoHide.TogglePin(state, "chrome_Status")
        Assert.False(state.StatusPinned)
        ' The table's corner pin works the same way, by its window name.
        state = ChromeAutoHide.TogglePin(state, "recipients_Overlay")
        Assert.True(state.OverlayPinned)
        Assert.True(ChromeAutoHide.PanelPinned(state, "recipients_Overlay"))
    End Sub

    ' --- the popup menu's dismissal on an activation change (ChromeMenu.PreFilterMessage) ----

    ''' <summary>The window set at the moment the popup opens: argument order is openForeground,
    ''' currentForeground, then the viewer's and its owned forms' handles. Returns whether the
    ''' popup survives this poll of the foreground window.</summary>
    Private Shared Function Survives(openForeground As IntPtr, currentForeground As IntPtr,
                                     viewer As IntPtr, ParamArray owned() As IntPtr) As Boolean
        Dim all As New List(Of IntPtr)() From {viewer}
        all.AddRange(owned)
        Return Not ChromeAutoHide.ShouldPopupCloseOnActivationChange(openForeground, currentForeground, all)
    End Function

    <Fact>
    Public Sub A_press_outside_an_open_popup_is_consumed_and_nothing_else_is()
        Assert.True(ChromeAutoHide.ShouldConsumePressThatClosesPopup(True, True))
        Assert.False(ChromeAutoHide.ShouldConsumePressThatClosesPopup(True, False))
        Assert.False(ChromeAutoHide.ShouldConsumePressThatClosesPopup(False, True))
        Assert.False(ChromeAutoHide.ShouldConsumePressThatClosesPopup(False, False))
    End Sub

    <Fact>
    Public Sub A_popup_from_a_background_viewer_survives_until_the_foreground_moves()
        Dim viewer As New IntPtr(100)
        Dim browser As New IntPtr(200)
        ' The menu opened from a no-activate panel while the browser was foreground: the static
        ' "the viewer is not focused" must not close it - that state IS how it opens.
        Assert.True(Survives(browser, browser, viewer))
        ' The foreground moving to the viewer (or an owned form) keeps it up.
        Assert.True(Survives(browser, viewer, viewer))
        Assert.True(Survives(browser, browser, viewer, New IntPtr(300)))
        Assert.True(Survives(viewer, New IntPtr(300), viewer, New IntPtr(300)))
        ' The foreground moving to yet another foreign window closes it.
        Assert.False(Survives(browser, New IntPtr(400), viewer))
        Assert.False(Survives(viewer, browser, viewer))
    End Sub

    <Fact>
    Public Sub A_popup_survives_a_foreground_that_vanishes_to_nothing_only_when_it_had_none()
        Dim viewer As New IntPtr(100)
        ' Nothing was foreground when it opened and nothing is now: no change, stay.
        Assert.True(Survives(IntPtr.Zero, IntPtr.Zero, viewer))
        ' The foreground window disappeared (its app closed): treat it as a move away.
        Assert.False(Survives(viewer, IntPtr.Zero, viewer))
    End Sub

End Class
#End If
