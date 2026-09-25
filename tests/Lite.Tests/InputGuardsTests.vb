Option Strict On

Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The input rules of ticket 047, on both shipped runtimes:
''' C-10 - the historical key map must not answer Ctrl/Alt combinations (Alt+F4 opened the
''' file chooser and kept the window open, Ctrl+1 moved a file, Alt+D deleted one);
''' C-20 - a held destructive key acts once, and a double-click on an overlay row once;
''' C-21 - a high-resolution wheel flips one file per WHEEL_DELTA, not per message.
''' </summary>
Public Class InputGuardsTests

    ' ------------------------------------------------------------------ C-10 ----

    <Theory>
    <InlineData(Keys.F4)>
    <InlineData(Keys.D1)>
    <InlineData(Keys.Delete)>
    <InlineData(Keys.Space)>
    <InlineData(Keys.D Or Keys.Shift)>
    <InlineData(Keys.D1 Or Keys.Shift)>
    <InlineData(Keys.PageDown Or Keys.Shift)>
    Public Sub Bare_keys_and_Shift_belong_to_the_map(keyData As Keys)
        Assert.True(KeyRouting.MainMapAccepts(keyData))
    End Sub

    <Theory>
    <InlineData(Keys.F4 Or Keys.Alt)>
    <InlineData(Keys.Space Or Keys.Alt)>
    <InlineData(Keys.D Or Keys.Alt)>
    <InlineData(Keys.D1 Or Keys.Alt)>
    <InlineData(Keys.D1 Or Keys.Control)>
    <InlineData(Keys.D Or Keys.Control)>
    <InlineData(Keys.X Or Keys.Control)>
    <InlineData(Keys.Q Or Keys.Control)>
    <InlineData(Keys.Delete Or Keys.Control Or Keys.Shift)>
    <InlineData(Keys.D1 Or Keys.Control Or Keys.Alt)>
    Public Sub Ctrl_and_Alt_combinations_are_left_to_Windows(keyData As Keys)
        Assert.False(KeyRouting.MainMapAccepts(keyData))
    End Sub

    ' ------------------------------------------------------------------ C-20 ----

    <Theory>
    <InlineData(Keys.D1)>
    <InlineData(Keys.D0)>
    <InlineData(Keys.NumPad5)>
    <InlineData(Keys.Delete)>
    <InlineData(Keys.D)>
    Public Sub Slot_digits_and_delete_are_destructive(keyCode As Keys)
        Assert.True(KeyRouting.IsDestructiveKey(keyCode))
    End Sub

    ''' <summary>Flipping keeps its auto-repeat: holding an arrow to run through a folder is
    ''' the fast path the viewer-core audit measured.</summary>
    <Theory>
    <InlineData(Keys.Right)>
    <InlineData(Keys.Left)>
    <InlineData(Keys.Space)>
    <InlineData(Keys.PageDown)>
    <InlineData(Keys.U)>
    <InlineData(Keys.F6)>
    Public Sub Navigation_keys_are_not_destructive(keyCode As Keys)
        Assert.False(KeyRouting.IsDestructiveKey(keyCode))
    End Sub

    <Fact>
    Public Sub The_repeat_bit_is_bit_30_of_lParam()
        Assert.False(KeyRouting.IsAutoRepeat(&H1L))                ' first press, repeat count 1
        Assert.True(KeyRouting.IsAutoRepeat(&H40000001L))          ' key was already down
        Assert.False(KeyRouting.IsAutoRepeat(&H200001L))           ' extended-key bit only
    End Sub

    <Fact>
    Public Sub A_double_click_on_one_row_acts_once()
        Dim guard As New RepeatClickGuard()
        Assert.False(guard.IsRepeat(1, 1000))
        Assert.True(guard.IsRepeat(1, 1150))
    End Sub

    <Fact>
    Public Sub Two_deliberate_clicks_both_act()
        Dim guard As New RepeatClickGuard()
        Assert.False(guard.IsRepeat(1, 1000))
        Assert.False(guard.IsRepeat(1, 1000 + RepeatClickGuard.Window_Ms))
    End Sub

    <Fact>
    Public Sub Two_rows_in_quick_succession_are_two_gestures()
        Dim guard As New RepeatClickGuard()
        Assert.False(guard.IsRepeat(1, 1000))
        Assert.False(guard.IsRepeat(2, 1050))
    End Sub

    ' ------------------------------------------------------------------ C-21 ----

    ''' <summary>A classic wheel: every notch is one message of 120, and every one flips.</summary>
    <Fact>
    Public Sub A_classic_notch_flips_at_once()
        Dim wheel As New WheelAccumulator()
        Assert.Equal(-1, wheel.Add(-120, 0))
        Assert.Equal(-1, wheel.Add(-120, 50))
        Assert.Equal(1, wheel.Add(120, 100))
    End Sub

    ''' <summary>A touchpad swipe: forty messages of -30 are ten notches' worth, so ten
    ''' flips - not forty.</summary>
    <Fact>
    Public Sub A_touchpad_swipe_flips_once_per_notch()
        Dim wheel As New WheelAccumulator()
        Dim flips As Integer = 0
        For i As Integer = 0 To 39
            flips += Math.Abs(wheel.Add(-30, i * 10))
        Next
        Assert.Equal(10, flips)
    End Sub

    <Fact>
    Public Sub Turning_back_drops_what_was_collected_the_other_way()
        Dim wheel As New WheelAccumulator()
        Assert.Equal(0, wheel.Add(-100, 0))
        Assert.Equal(0, wheel.Add(100, 10))      ' not the -100 plus this; a fresh start
        Assert.Equal(1, wheel.Add(20, 20))
    End Sub

    <Fact>
    Public Sub A_pause_starts_a_new_gesture()
        Dim wheel As New WheelAccumulator()
        Assert.Equal(0, wheel.Add(-100, 0))
        Assert.Equal(0, wheel.Add(-100, WheelAccumulator.Idle_Reset_Ms + 1))
    End Sub

    ''' <summary>One large message is still one flip, as it always was, and it does not bank
    ''' a run of flips for the messages that follow.</summary>
    <Fact>
    Public Sub A_large_message_is_one_flip_and_banks_nothing()
        Dim wheel As New WheelAccumulator()
        Assert.Equal(-1, wheel.Add(-600, 0))
        Assert.Equal(0, wheel.Add(-1, 10))
    End Sub

    <Fact>
    Public Sub A_zero_delta_does_nothing()
        Dim wheel As New WheelAccumulator()
        Assert.Equal(0, wheel.Add(0, 0))
    End Sub

End Class
