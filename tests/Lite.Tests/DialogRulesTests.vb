#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports Xunit

''' <summary>
''' Ticket 124, the pure half: what a notification level turns into (UX-206/UX-207), where windows and toasts
''' go, how a hand-authored layout reflows when a caption grows (UX-203), what the empty surface says (UX-209).
''' No window is built here.
''' </summary>
Public Class DialogRulesTests

    ' --- NotifyRules: classification of the legacy prefix -------------------------------------

    <Theory>
    <InlineData("! Файл не найден", "Файл не найден")>
    <InlineData("!Ждите.. файл копируется", "Ждите.. файл копируется")>
    <InlineData("!   много пробелов", "много пробелов")>
    <InlineData("!", "")>
    Public Sub A_leading_bang_is_a_warning_and_is_removed(raw As String, expected As String)
        Dim message As NotifyMessage = NotifyRules.Classify(raw)
        Assert.Equal(NotifyLevel.Warning, message.Level)
        Assert.Equal(expected, message.Text)
    End Sub

    <Theory>
    <InlineData("Папка пустая")>
    <InlineData("Готово: a.jpg")>
    <InlineData(" ! не в начале")>
    <InlineData("a! b")>
    <InlineData("")>
    Public Sub Any_other_text_is_information_and_untouched(raw As String)
        Dim message As NotifyMessage = NotifyRules.Classify(raw)
        Assert.Equal(NotifyLevel.Info, message.Level)
        Assert.Equal(raw, message.Text)
    End Sub

    <Fact>
    Public Sub Nothing_classifies_as_empty_information()
        Dim message As NotifyMessage = NotifyRules.Classify(Nothing)
        Assert.Equal(NotifyLevel.Info, message.Level)
        Assert.Equal(String.Empty, message.Text)
    End Sub

    ''' <summary>The thumbnail panel used to look at the first character; it reads the level now.</summary>
    <Fact>
    Public Sub IsWarningOrWorse_reads_the_level_not_a_character()
        Assert.True(NotifyRules.IsWarningOrWorse("! x"))
        Assert.False(NotifyRules.IsWarningOrWorse("x"))
    End Sub

    ' --- NotifyRules: severity -> presentation ------------------------------------------------

    ''' <summary>Q124-3: a toast is for a warning or an error, and only when the status line cannot be seen.</summary>
    <Theory>
    <InlineData(0, False, False)>
    <InlineData(0, True, False)>
    <InlineData(1, False, True)>
    <InlineData(1, True, False)>
    <InlineData(2, False, True)>
    <InlineData(2, True, False)>
    Public Sub A_toast_needs_a_problem_and_no_status_line(level As Integer, statusVisible As Boolean, expected As Boolean)
        Assert.Equal(expected, NotifyRules.ShouldToast(CType(level, NotifyLevel), statusVisible))
    End Sub

    <Theory>
    <InlineData(0, True, False)>
    <InlineData(1, True, False)>
    <InlineData(2, True, True)>
    <InlineData(2, False, False)>
    Public Sub Only_an_error_that_became_a_toast_plays_the_sound(level As Integer, toasted As Boolean, expected As Boolean)
        Assert.Equal(expected, NotifyRules.ShouldPlaySound(CType(level, NotifyLevel), toasted))
    End Sub

    <Fact>
    Public Sub A_toast_lives_two_to_three_seconds_and_information_never_one()
        Assert.Equal(0, NotifyRules.ToastMilliseconds(NotifyLevel.Info))
        Dim warning As Integer = NotifyRules.ToastMilliseconds(NotifyLevel.Warning)
        Dim failure As Integer = NotifyRules.ToastMilliseconds(NotifyLevel.Error)
        Assert.InRange(warning, 2000, 3000)
        Assert.InRange(failure, 2000, 3000)
        Assert.True(failure >= warning, "an error stays at least as long as a warning")
    End Sub

    ''' <summary>Q124-2: the ink is contrast-checked against the surface it sits on, light or dark, in the
    ''' theme's own warning and danger roles.</summary>
    <Theory>
    <InlineData(1, 0, 0, 0)>
    <InlineData(1, 255, 255, 255)>
    <InlineData(1, 128, 128, 128)>
    <InlineData(2, 0, 0, 0)>
    <InlineData(2, 255, 255, 255)>
    <InlineData(2, 200, 40, 40)>
    <InlineData(2, 34, 34, 34)>
    <InlineData(1, 239, 108, 0)>
    Public Sub A_warning_or_an_error_reads_on_any_surface(level As Integer, r As Integer, g As Integer, b As Integer)
        Dim fill As Color = Color.FromArgb(r, g, b)
        Dim foreground As Color = ChromeTheme.ContrastingText(fill)
        Dim ink As Color = NotifyRules.InkOnSurface(CType(level, NotifyLevel), fill, foreground)
        Assert.True(ChromeTheme.ContrastRatio(ink, fill) >= 4.5,
                    "ink " & ink.ToString() & " on " & fill.ToString() & " is only " & ChromeTheme.ContrastRatio(ink, fill).ToString("0.00"))
    End Sub

    <Fact>
    Public Sub Information_is_the_plain_foreground()
        Assert.Equal(Color.White, NotifyRules.InkOnSurface(NotifyLevel.Info, Color.Black, Color.White))
    End Sub

    <Fact>
    Public Sub Warning_and_error_are_told_apart_on_a_light_surface()
        Dim fill As Color = Color.White
        Dim warning As Color = NotifyRules.InkOnSurface(NotifyLevel.Warning, fill, Color.Black)
        Dim failure As Color = NotifyRules.InkOnSurface(NotifyLevel.Error, fill, Color.Black)
        Assert.NotEqual(warning.ToArgb(), failure.ToArgb())
    End Sub

    ' --- placement ------------------------------------------------------------------------------

    <Fact>
    Public Sub A_dialog_is_centred_on_the_viewer()
        Dim at As Point = DialogPlacement.CenterOn(New Size(400, 200), New Rectangle(100, 100, 1000, 600), New Rectangle(0, 0, 1920, 1080))
        Assert.Equal(New Point(400, 300), at)
    End Sub

    <Fact>
    Public Sub A_dialog_is_kept_on_the_viewers_monitor()
        Dim work As New Rectangle(1920, 0, 1920, 1040)
        ' The viewer straddles two monitors, its centre far to the left of this one.
        Dim at As Point = DialogPlacement.CenterOn(New Size(600, 400), New Rectangle(1000, 100, 1000, 600), work)
        Assert.True(work.Contains(New Rectangle(at, New Size(600, 400))))
    End Sub

    <Fact>
    Public Sub A_dialog_larger_than_the_monitor_is_pinned_to_its_start_corner()
        Dim work As New Rectangle(0, 0, 800, 600)
        Dim at As Point = DialogPlacement.CenterOn(New Size(2000, 2000), New Rectangle(0, 0, 800, 600), work)
        Assert.Equal(work.Location, at)
        Assert.Equal(New Size(800, 600), DialogPlacement.Capped(New Size(2000, 2000), work))
    End Sub

    <Fact>
    Public Sub A_toast_sits_at_the_bottom_centre_of_the_media_inside_it()
        Dim media As New Rectangle(0, 0, 1920, 1080)
        Dim bounds As Rectangle = DialogPlacement.ToastBounds(media, New Size(600, 80), 64)
        Assert.Equal(660, bounds.Left)
        Assert.Equal(1080 - 64 - 80, bounds.Top)
        Assert.True(media.Contains(bounds))
    End Sub

    <Fact>
    Public Sub A_toast_larger_than_the_media_is_cut_to_it()
        Dim media As New Rectangle(10, 20, 300, 100)
        Dim bounds As Rectangle = DialogPlacement.ToastBounds(media, New Size(900, 400), 64)
        Assert.True(media.Contains(bounds))
    End Sub

    ' --- reflow ---------------------------------------------------------------------------------

    <Fact>
    Public Sub A_label_that_wraps_moves_everything_below_it_down_by_the_growth()
        Dim bounds As New List(Of Rectangle) From {
            New Rectangle(14, 14, 432, 16),
            New Rectangle(14, 40, 432, 24),
            New Rectangle(266, 100, 86, 28)}
        Dim growth As Integer
        Dim moved As Rectangle() = DialogReflow.GrowVertical(bounds, New List(Of Integer) From {32, 0, 0}, growth)
        Assert.Equal(New Rectangle(14, 14, 432, 32), moved(0))
        Assert.Equal(New Rectangle(14, 56, 432, 24), moved(1))
        Assert.Equal(New Rectangle(266, 116, 86, 28), moved(2))
        Assert.Equal(16, growth)
    End Sub

    <Fact>
    Public Sub Nothing_that_fits_moves()
        Dim bounds As New List(Of Rectangle) From {New Rectangle(0, 0, 100, 20), New Rectangle(0, 30, 100, 20)}
        Dim growth As Integer
        Dim moved As Rectangle() = DialogReflow.GrowVertical(bounds, New List(Of Integer) From {0, 0}, growth)
        Assert.Equal(bounds, moved.ToList())
        Assert.Equal(0, growth)
    End Sub

    ''' <summary>The label of a form row sits 4 px lower than its field and ends above the next row; growing it
    ''' must not push the field BESIDE it, only the rows below.</summary>
    <Fact>
    Public Sub A_field_beside_a_growing_label_stays_and_the_next_row_goes_down()
        Dim bounds As New List(Of Rectangle) From {
            New Rectangle(14, 18, 140, 18),
            New Rectangle(160, 14, 286, 24),
            New Rectangle(14, 48, 140, 18),
            New Rectangle(160, 44, 286, 24)}
        Dim growth As Integer
        Dim moved As Rectangle() = DialogReflow.GrowVertical(bounds, New List(Of Integer) From {36, 0, 0, 0}, growth)
        Assert.Equal(14, moved(1).Top)
        Assert.Equal(66, moved(2).Top)
        Assert.Equal(62, moved(3).Top)
        Assert.Equal(18, growth)
    End Sub

    ''' <summary>Two labels growing side by side push what is under them once, not twice.</summary>
    <Fact>
    Public Sub Growth_side_by_side_is_not_added_up()
        Dim bounds As New List(Of Rectangle) From {
            New Rectangle(10, 10, 100, 20),
            New Rectangle(150, 10, 100, 20),
            New Rectangle(10, 60, 240, 28)}
        Dim growth As Integer
        Dim moved As Rectangle() = DialogReflow.GrowVertical(bounds, New List(Of Integer) From {50, 40, 0}, growth)
        Assert.Equal(90, moved(2).Top)
        Assert.Equal(30, growth)
    End Sub

    ''' <summary>Growth chains: a label that grows and then another under it that grows too add up, because the
    ''' second one starts lower.</summary>
    <Fact>
    Public Sub Growth_down_a_column_adds_up()
        Dim bounds As New List(Of Rectangle) From {
            New Rectangle(10, 10, 300, 20),
            New Rectangle(10, 40, 300, 20),
            New Rectangle(10, 80, 100, 28)}
        Dim growth As Integer
        Dim moved As Rectangle() = DialogReflow.GrowVertical(bounds, New List(Of Integer) From {40, 40, 0}, growth)
        Assert.Equal(60, moved(1).Top)
        Assert.Equal(120, moved(2).Top)
        Assert.Equal(40, growth)
    End Sub

    <Fact>
    Public Sub A_button_that_outgrew_its_width_widens_towards_the_start_and_pushes_its_neighbour()
        Dim bounds As New List(Of Rectangle) From {New Rectangle(266, 150, 86, 28), New Rectangle(360, 150, 86, 28)}
        Dim moved As Rectangle() = DialogReflow.GrowTowardsStart(bounds, New List(Of Integer) From {0, 130}, 8)
        Assert.Equal(446, moved(1).Right)
        Assert.Equal(130, moved(1).Width)
        Assert.True(moved(0).Right + 8 <= moved(1).Left, "the neighbour made room")
        Assert.Equal(86, moved(0).Width)
    End Sub

    <Fact>
    Public Sub A_left_aligned_button_widens_towards_the_end()
        Dim bounds As New List(Of Rectangle) From {New Rectangle(12, 228, 110, 28), New Rectangle(130, 228, 110, 28)}
        Dim moved As Rectangle() = DialogReflow.GrowTowardsEnd(bounds, New List(Of Integer) From {150, 0}, 8)
        Assert.Equal(12, moved(0).Left)
        Assert.Equal(150, moved(0).Width)
        Assert.True(moved(0).Right + 8 <= moved(1).Left, "the neighbour made room")
        Assert.Equal(110, moved(1).Width)
    End Sub

    <Fact>
    Public Sub A_widened_button_leaves_the_other_row_alone()
        Dim bounds As New List(Of Rectangle) From {New Rectangle(266, 150, 86, 28), New Rectangle(200, 20, 86, 28)}
        Dim moved As Rectangle() = DialogReflow.GrowTowardsStart(bounds, New List(Of Integer) From {140, 0}, 8)
        Assert.Equal(bounds(1), moved(1))
    End Sub

    ' --- the empty state -----------------------------------------------------------------------

    <Fact>
    Public Sub The_empty_surface_says_what_happened_and_names_the_key_that_goes_on()
        Dim lines As String() = EmptyStateRules.Lines()
        Assert.True(lines.Length >= 2)
        Assert.All(lines, Sub(l) Assert.False(String.IsNullOrWhiteSpace(l)))
        Assert.Contains("F4", lines(1))
    End Sub

    ' --- the Settings window's header tools ----------------------------------------------------

    <Theory>
    <InlineData(4, 1.0)>
    <InlineData(4, 1.75)>
    <InlineData(2, 2.5)>
    Public Sub The_header_tools_sit_in_one_row_on_the_end_edge_in_reading_order(count As Integer, scale As Double)
        Dim side As Integer = CInt(Math.Round(44 * scale))
        Dim gap As Integer = CInt(Math.Round(6 * scale))
        Dim edge As Integer = CInt(Math.Round(16 * scale))
        Dim width As Integer = CInt(Math.Round(900 * scale))
        Dim ltr As Integer() = SettingsHeaderRules.ToolLefts(width, count, side, gap, edge, rtl:=False)
        Assert.Equal(count, ltr.Length)
        For i As Integer = 1 To count - 1
            Assert.Equal(side + gap, ltr(i) - ltr(i - 1))
        Next
        Assert.Equal(width - edge, ltr(count - 1) + side)
        Dim rtl As Integer() = SettingsHeaderRules.ToolLefts(width, count, side, gap, edge, rtl:=True)
        Assert.Equal(edge, rtl.Min())
        For i As Integer = 1 To count - 1
            Assert.Equal(side + gap, rtl(i - 1) - rtl(i))
        Next
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub The_title_never_runs_under_the_tools_and_is_never_narrower_than_a_pixel(rtl As Boolean)
        For Each width As Integer In New Integer() {0, 120, 400, 900, 2400}
            Dim lefts As Integer() = SettingsHeaderRules.ToolLefts(width, 4, 77, 10, 28, rtl)
            Dim span As Integer() = SettingsHeaderRules.TitleSpan(width, 4, 77, 10, 28, 35, 21, rtl)
            Assert.True(span(1) >= 1)
            If width >= 900 Then
                Dim titleRight As Integer = span(0) + span(1)
                If rtl Then
                    Assert.True(span(0) >= lefts.Max() + 77, "the title starts after the tools")
                Else
                    Assert.True(titleRight <= lefts.Min(), "the title ends before the tools")
                End If
            End If
        Next
    End Sub

End Class
#End If
