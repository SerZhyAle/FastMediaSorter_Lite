#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports Xunit

''' <summary>
''' The pure half of the floating chrome (002 section 10, cases 1, 2, 8, 9): the scale, the row layout, the
''' theme arithmetic and the metrics. Whole-file "#If Not NETFRAMEWORK" like the code under test - the
''' x86 viewer has no floating chrome.
''' </summary>
Public Class ChromeFoundationTests

    ' --- UiScale (case 1) -----------------------------------------------------------------

    <Fact>
    Public Sub Px_is_monotonic_never_zero_for_a_positive_size_and_deterministic()
        For Each factor As Single In New Single() {0.9F, 1.0F, 1.25F, 1.5F, 1.75F, 2.0F}
            Dim previous As Integer = 0
            For logical As Integer = 1 To 200
                Dim px As Integer = UiScale.Px(logical, factor)
                Assert.True(px >= 1, "Px(" & logical & ", " & factor & ") must not be zero.")
                Assert.True(px >= previous, "Px must never decrease as the logical size grows.")
                Assert.Equal(px, UiScale.Px(logical, factor))
                previous = px
            Next
        Next
    End Sub

    <Fact>
    Public Sub Px_of_a_non_size_is_zero_and_a_bad_factor_falls_back_to_one()
        Assert.Equal(0, UiScale.Px(0, 1.5F))
        Assert.Equal(0, UiScale.Px(-3, 1.5F))
        Assert.Equal(10, UiScale.Px(10, 0.0F))
        Assert.Equal(10, UiScale.Px(10, Single.NaN))
        Assert.Equal(10, UiScale.Px(10, -2.0F))
    End Sub

    <Theory>
    <InlineData(0, 0)>
    <InlineData(-5, 0)>
    <InlineData(1, 90)>
    <InlineData(89, 90)>
    <InlineData(90, 90)>
    <InlineData(120, 120)>
    <InlineData(150, 150)>
    <InlineData(151, 150)>
    <InlineData(Integer.MaxValue, 150)>
    Public Sub The_percent_clamps_exactly_as_the_preference_promises(raw As Integer, expected As Integer)
        Assert.Equal(expected, UiScale.NormalizePercent(raw))
    End Sub

    <Fact>
    Public Sub Automatic_follows_the_process_dpi_and_an_explicit_percent_ignores_it()
        Assert.Equal(1.0F, UiScale.Factor(0, 96))
        Assert.Equal(1.75F, UiScale.Factor(0, 168), 3)
        Assert.Equal(1.25F, UiScale.Factor(0, 120), 3)
        ' An explicit choice is the user's, whatever the monitor says.
        Assert.Equal(1.2F, UiScale.Factor(120, 192), 3)
        Assert.Equal(1.5F, UiScale.Factor(999, 96), 3)
        Assert.Equal(0.9F, UiScale.Factor(40, 96), 3)
    End Sub

    <Fact>
    Public Sub Automatic_survives_a_nonsense_dpi()
        Assert.Equal(1.0F, UiScale.Factor(0, 0))
        Assert.Equal(1.0F, UiScale.Factor(0, -96))
        Assert.Equal(1.0F, UiScale.Factor(0, 48))
        Assert.Equal(4.0F, UiScale.Factor(0, 9600))
    End Sub

    ' --- metrics (case 8) -------------------------------------------------------------------

    <Fact>
    Public Sub No_item_is_smaller_than_the_pointer_minimum_at_any_scale()
        For Each percent As Integer In New Integer() {0, 90, 100, 125, 150}
            Dim factor As Single = UiScale.Factor(percent, 96)
            Assert.True(UiScale.Px(ChromeMetrics.ItemHeight, factor) >= UiScale.Px(ChromeMetrics.MinTarget, factor))
            Assert.True(UiScale.Px(ChromeMetrics.ItemHeight, factor) >= 28, "The item height must clear the ICON-RENDER rule 5 floor of 28 px at " & percent & " %.")
        Next
        Assert.True(ChromeMetrics.ItemHeight >= ChromeMetrics.MinTarget)
        Assert.True(ChromeMetrics.RowHeight >= ChromeMetrics.ItemHeight)
    End Sub

    ' --- ChromeLayout (case 2) -------------------------------------------------------------------

    Private Shared Function Slots(ParamArray widths As Integer()) As List(Of ChromeSlot)
        Dim result As New List(Of ChromeSlot)()
        For i As Integer = 0 To widths.Length - 1
            result.Add(New ChromeSlot("item" & i, widths(i)))
        Next
        Return result
    End Function

    Private Shared Sub AssertNoOverlap(layout As ChromeRowLayout, Optional ignoreEmpty As Boolean = True)
        Dim shown As New List(Of Rectangle)()
        For i As Integer = 0 To layout.Rects.Length - 1
            If Not layout.Overflowed(i) Then shown.Add(layout.Rects(i))
        Next
        If layout.HasOverflow Then shown.Add(layout.OverflowButton)
        For a As Integer = 0 To shown.Count - 1
            For b As Integer = a + 1 To shown.Count - 1
                Assert.False(shown(a).IntersectsWith(shown(b)), "Rectangles " & a & " and " & b & " overlap.")
            Next
        Next
    End Sub

    <Fact>
    Public Sub When_everything_fits_nothing_overflows_and_nothing_is_narrowed()
        Dim items As List(Of ChromeSlot) = Slots(28, 28, 28, 28, 28)
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 400, 28, 4, 28)
        Assert.False(layout.HasOverflow)
        Assert.Empty(layout.OverflowIndexes())
        For i As Integer = 0 To 4
            Assert.Equal(28, layout.Rects(i).Width)
            Assert.Equal(i * 32, layout.Rects(i).X)
            Assert.Equal(28, layout.Rects(i).Height)
        Next
        AssertNoOverlap(layout)
    End Sub

    <Fact>
    Public Sub Narrowing_moves_the_last_items_into_the_menu_in_reading_order()
        Dim items As List(Of ChromeSlot) = Slots(28, 28, 28, 28, 28)
        ' Room for three items and the "more" button: 3*28 + 2*4 + 4 + 28 = 124.
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 124, 28, 4, 28)
        Assert.True(layout.HasOverflow)
        Assert.Equal(New List(Of Integer) From {3, 4}, layout.OverflowIndexes())
        Assert.Equal(Rectangle.Empty, layout.Rects(3))
        Assert.Equal(Rectangle.Empty, layout.Rects(4))
        Assert.True(layout.OverflowButton.Right <= 124)
        AssertNoOverlap(layout)

        ' One pixel narrower costs one more item - never a different one.
        Dim narrower As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 123, 28, 4, 28)
        Assert.Equal(New List(Of Integer) From {2, 3, 4}, narrower.OverflowIndexes())
    End Sub

    <Fact>
    Public Sub The_flexible_item_shrinks_before_any_item_is_dropped()
        Dim items As New List(Of ChromeSlot) From {
            New ChromeSlot("a", 28), New ChromeSlot("path", 320, 90), New ChromeSlot("b", 28), New ChromeSlot("c", 28)}
        ' Natural: 28+320+28+28 + 3*4 = 416. At 300 the path gives up 116 px and nothing overflows.
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 300, 28, 4, 28)
        Assert.False(layout.HasOverflow)
        Assert.Equal(204, layout.Rects(1).Width)
        Assert.Equal(300, layout.Rects(3).Right)
        AssertNoOverlap(layout)
    End Sub

    <Fact>
    Public Sub At_its_minimum_the_flexible_item_holds_and_the_tail_collapses_then_it_takes_back_the_spare()
        Dim items As New List(Of ChromeSlot) From {
            New ChromeSlot("a", 28, pinned:=True), New ChromeSlot("path", 320, 90, pinned:=True),
            New ChromeSlot("b", 28), New ChromeSlot("c", 28)}
        ' 28 + 90 + 28 + 28 + 12 = 186 is the floor; below it the tail starts to go.
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 160, 28, 4, 28)
        Assert.True(layout.HasOverflow)
        Assert.Equal(New List(Of Integer) From {2, 3}, layout.OverflowIndexes())
        ' "a" + path + more = 28 + 4 + path + 4 + 28 = 160 -> the path takes everything that is left.
        Assert.Equal(96, layout.Rects(1).Width)
        Assert.True(layout.Rects(1).Width <= 320)
        AssertNoOverlap(layout)
    End Sub

    <Fact>
    Public Sub A_pinned_item_never_collapses_even_when_it_is_last()
        Dim items As New List(Of ChromeSlot) From {
            New ChromeSlot("core", 28, pinned:=True), New ChromeSlot("x", 28), New ChromeSlot("y", 28), New ChromeSlot("pin2", 28, pinned:=True)}
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 80, 28, 4, 28)
        Assert.False(layout.Overflowed(0))
        Assert.False(layout.Overflowed(3))
        Assert.True(layout.Overflowed(1))
        Assert.True(layout.Overflowed(2))
    End Sub

    <Fact>
    Public Sub Only_a_pinned_core_left_is_kept_rather_than_looping()
        Dim items As New List(Of ChromeSlot) From {New ChromeSlot("core", 100, pinned:=True)}
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 10, 28, 4, 28)
        Assert.False(layout.HasOverflow)
        Assert.False(layout.Overflowed(0))
    End Sub

    <Fact>
    Public Sub A_right_to_left_row_is_the_same_row_mirrored_with_nothing_overlapping()
        Dim items As List(Of ChromeSlot) = Slots(28, 40, 28, 28, 28)
        For Each width As Integer In New Integer() {300, 180, 124, 90}
            Dim ltr As ChromeRowLayout = ChromeLayout.ArrangeRow(items, width, 28, 4, 28, rightToLeft:=False)
            Dim rtl As ChromeRowLayout = ChromeLayout.ArrangeRow(items, width, 28, 4, 28, rightToLeft:=True)
            Assert.Equal(ltr.OverflowIndexes(), rtl.OverflowIndexes())
            For i As Integer = 0 To items.Count - 1
                If ltr.Overflowed(i) Then Continue For
                Assert.Equal(width - ltr.Rects(i).Right, rtl.Rects(i).X)
                Assert.Equal(ltr.Rects(i).Width, rtl.Rects(i).Width)
                Assert.True(rtl.Rects(i).X >= 0 AndAlso rtl.Rects(i).Right <= width)
            Next
            AssertNoOverlap(rtl)
        Next
        ' The first item of the reading order is on the RIGHT in a right-to-left row.
        Dim wide As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 300, 28, 4, 28, rightToLeft:=True)
        Assert.True(wide.Rects(0).X > wide.Rects(1).X)
    End Sub

    <Fact>
    Public Sub An_empty_row_and_a_zero_width_are_harmless()
        Dim empty As ChromeRowLayout = ChromeLayout.ArrangeRow(New List(Of ChromeSlot)(), 100, 28, 4, 28)
        Assert.Empty(empty.Rects)
        Assert.False(empty.HasOverflow)
        Dim none As ChromeRowLayout = ChromeLayout.ArrangeRow(Slots(28, 28), 0, 28, 4, 28)
        Assert.True(none.HasOverflow)
    End Sub

    <Fact>
    Public Sub Natural_width_counts_the_gaps_between_items_only()
        Assert.Equal(0, ChromeLayout.NaturalWidth(New List(Of ChromeSlot)(), 4))
        Assert.Equal(28, ChromeLayout.NaturalWidth(Slots(28), 4))
        Assert.Equal(28 * 5 + 4 * 4, ChromeLayout.NaturalWidth(Slots(28, 28, 28, 28, 28), 4))
    End Sub

    ' --- ChromeTheme (cases 7 and 9) --------------------------------------------------------------

    <Fact>
    Public Sub Blend_is_exact_at_the_ends_clamped_beyond_them_and_always_opaque()
        Dim a As Color = Color.FromArgb(10, 20, 30)
        Dim b As Color = Color.FromArgb(210, 220, 230)
        Assert.Equal(Color.FromArgb(255, 10, 20, 30), ChromeTheme.Blend(a, b, 0.0F))
        Assert.Equal(Color.FromArgb(255, 210, 220, 230), ChromeTheme.Blend(a, b, 1.0F))
        Assert.Equal(ChromeTheme.Blend(a, b, 1.0F), ChromeTheme.Blend(a, b, 5.0F))
        Assert.Equal(ChromeTheme.Blend(a, b, 0.0F), ChromeTheme.Blend(a, b, -1.0F))
        Assert.Equal(ChromeTheme.Blend(a, b, 0.0F), ChromeTheme.Blend(a, b, Single.NaN))
        Assert.Equal(Color.FromArgb(255, 110, 120, 130), ChromeTheme.Blend(a, b, 0.5F))
    End Sub

    <Fact>
    Public Sub The_theme_follows_the_scheme_and_the_states_are_ordered_by_strength()
        Dim dark As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
        Assert.Equal(Color.FromArgb(255, 0, 0, 0).ToArgb(), dark.PanelFill.ToArgb())
        Assert.Equal(0.86R, dark.PanelOpacity, 3)
        Assert.True(dark.AllowsAnimation)
        Assert.False(dark.IsHighContrast)
        ' hover (14 %) is fainter than pressed (26 %) over the same fill.
        Assert.True(dark.ItemHover.R < dark.ItemPressed.R)
        Assert.True(dark.DisabledForeground.R < dark.Foreground.R)

        Dim light As ChromeTheme = ChromeTheme.FromScheme(Color.White, Color.Black, False)
        Assert.True(light.ItemHover.R > light.ItemPressed.R)
    End Sub

    <Fact>
    Public Sub Only_the_destructive_item_wears_the_red_family()
        Dim t As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
        Assert.Equal(Color.FromArgb(96, 32, 32), t.FillFor(True, False, True, False, False))
        Assert.Equal(Color.FromArgb(140, 44, 44), t.FillFor(True, False, True, True, False))
        Assert.Equal(Color.FromArgb(170, 50, 50), t.FillFor(True, False, True, True, True))
        ' An ordinary item rests transparent; red never appears on it.
        Assert.True(t.FillFor(True, False, False, False, False).IsEmpty)
        Assert.NotEqual(Color.FromArgb(140, 44, 44), t.FillFor(True, False, False, True, False))
    End Sub

    <Fact>
    Public Sub A_checked_toggle_gets_the_accent_and_a_disabled_item_no_response_at_all()
        Dim t As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
        Assert.Equal(t.Accent, t.FillFor(True, True, False, False, False))
        Assert.Equal(t.AccentForeground, t.InkFor(True, True, False, False))
        Assert.True(t.FillFor(False, False, False, True, True).IsEmpty)
        Assert.Equal(t.DisabledForeground, t.InkFor(False, True, True, True))
    End Sub

    <Fact>
    Public Sub High_contrast_drops_translucency_animation_and_the_accent_fill()
        Dim t As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, True)
        Assert.True(t.IsHighContrast)
        Assert.Equal(1.0R, t.PanelOpacity)
        Assert.False(t.AllowsAnimation)
        Assert.Equal(SystemColors.Window, t.PanelFill)
        Assert.Equal(SystemColors.WindowText, t.Foreground)
        ' A checked item is not told apart by an accent fill that may not read: the system's pair does it.
        Assert.True(t.FillFor(True, True, False, False, False).IsEmpty)
        Assert.Equal(SystemColors.Highlight, t.FillFor(True, False, False, True, False))
        Assert.Equal(SystemColors.HighlightText, t.InkFor(True, False, False, True))
        Assert.Equal(SystemColors.HighlightText, t.InkFor(True, False, True, True))
    End Sub

    <Fact>
    Public Sub Contrasting_text_is_black_on_light_and_white_on_dark()
        Assert.Equal(Color.White, ChromeTheme.ContrastingText(Color.Black))
        Assert.Equal(Color.Black, ChromeTheme.ContrastingText(Color.White))
        Assert.True(ChromeTheme.IsDark(Color.FromArgb(20, 30, 40)))
        Assert.False(ChromeTheme.IsDark(Color.FromArgb(200, 210, 220)))
    End Sub

End Class
#End If
