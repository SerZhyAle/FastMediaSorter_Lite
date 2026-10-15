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
    Public Sub Panel_size_presets_multiply_the_scale_and_medium_is_the_historical_size()
        Assert.Equal(1.0F, UiScale.SizeMultiplier("medium", 1000.0F))
        Assert.Equal(1.0F, UiScale.SizeMultiplier("", 1000.0F))
        Assert.Equal(1.0F, UiScale.SizeMultiplier(Nothing, 1000.0F))
        Assert.Equal(1.0F, UiScale.SizeMultiplier("nonsense", 1000.0F))
        Assert.Equal(UiScale.SmallPreset, UiScale.SizeMultiplier("small", 3000.0F))
        Assert.Equal(UiScale.LargePreset, UiScale.SizeMultiplier("large", 300.0F))
        Assert.True(UiScale.SmallPreset < 1.0F AndAlso 1.0F < UiScale.LargePreset)
    End Sub

    <Fact>
    Public Sub Adaptive_size_is_bounded_stepped_and_never_shrinks_as_the_window_grows()
        Assert.Equal(1.0F, UiScale.AdaptiveMultiplier(UiScale.AdaptiveReferenceWidth))
        Assert.Equal(1.0F, UiScale.AdaptiveMultiplier(0.0F))
        Assert.Equal(1.0F, UiScale.AdaptiveMultiplier(Single.NaN))
        Dim previous As Single = 0.0F
        For width As Integer = 100 To 6000 Step 7
            Dim value As Single = UiScale.AdaptiveMultiplier(width)
            Assert.InRange(value, UiScale.AdaptiveMin, UiScale.AdaptiveMax)
            Assert.True(value >= previous, "The adaptive size must not shrink as the window widens.")
            previous = value
        Next
        Assert.Equal(UiScale.AdaptiveMin, UiScale.AdaptiveMultiplier(100.0F))
        Assert.Equal(UiScale.AdaptiveMax, UiScale.AdaptiveMultiplier(6000.0F))
        ' Step-quantised: a one-pixel drag of the window edge does not change it.
        Assert.Equal(UiScale.AdaptiveMultiplier(1600.0F), UiScale.AdaptiveMultiplier(1601.0F))
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

    ' --- kept places: a conditional item that is away still holds its slot (125 UX-313b) ---------

    <Fact>
    Public Sub A_kept_place_holds_the_items_after_it_where_they_are_while_the_row_has_room()
        Dim items As New List(Of ChromeSlot) From {
            New ChromeSlot("a", 28), New ChromeSlot("away", 40, soft:=True), New ChromeSlot("b", 28)}
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 400, 28, 4, 28)
        Assert.False(layout.HasOverflow)
        ' a at 0, the kept place at 32 (40 wide), b right after it at 76.
        Assert.Equal(32, layout.Rects(1).X)
        Assert.Equal(76, layout.Rects(2).X)
        ' Without the place b would stand at 32: the place is what keeps b still when the item comes back.
        Dim without As ChromeRowLayout = ChromeLayout.ArrangeRow(Slots(28, 28), 400, 28, 4, 28)
        Assert.Equal(32, without.Rects(1).X)
    End Sub

    <Fact>
    Public Sub Kept_places_go_first_the_last_one_first_before_the_path_shrinks_or_anything_overflows()
        Dim items As New List(Of ChromeSlot) From {
            New ChromeSlot("a", 28), New ChromeSlot("path", 320, 90), New ChromeSlot("away1", 40, soft:=True),
            New ChromeSlot("b", 28), New ChromeSlot("away2", 40, soft:=True), New ChromeSlot("c", 28)}
        ' Natural 28+320+40+28+40+28 + 5*4 = 504. At 460 the LAST place goes (460 - 40 - 4 = fits) and the first stays.
        Dim layout As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 460, 28, 4, 28)
        Assert.False(layout.HasOverflow)
        Assert.Equal(320, layout.Rects(1).Width)
        Assert.Equal(40, layout.Rects(2).Width)
        Assert.Equal(Rectangle.Empty, layout.Rects(4))
        Assert.False(layout.Overflowed(4))
        ' At 416 both are gone and the path is still at its full width - nothing was narrowed or sent to the menu.
        Dim tighter As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 416, 28, 4, 28)
        Assert.False(tighter.HasOverflow)
        Assert.Equal(320, tighter.Rects(1).Width)
        Assert.Equal(Rectangle.Empty, tighter.Rects(2))
        Assert.Equal(Rectangle.Empty, tighter.Rects(4))
        Assert.Equal(416, tighter.Rects(5).Right)
        ' Only below that does the path give width up, exactly as it did before places existed.
        Dim narrow As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 300, 28, 4, 28)
        Assert.False(narrow.HasOverflow)
        Assert.Equal(204, narrow.Rects(1).Width)
        AssertNoOverlap(narrow)
    End Sub

    <Fact>
    Public Sub A_kept_place_is_never_what_goes_into_the_more_menu_and_the_real_tail_still_does()
        Dim items As New List(Of ChromeSlot) From {
            New ChromeSlot("core", 28, pinned:=True), New ChromeSlot("away", 60, soft:=True),
            New ChromeSlot("x", 28), New ChromeSlot("y", 28)}
        ' Room for core + x + y only: 28+4+28+4+28 = 92. The place is dropped, nothing overflows.
        Dim fits As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 92, 28, 4, 20)
        Assert.False(fits.HasOverflow)
        Assert.False(fits.Overflowed(1))
        ' Narrower than that the REAL last item collapses (core + x + the 20 px button = 84), in reading order,
        ' and the place stays out of the list.
        Dim narrower As ChromeRowLayout = ChromeLayout.ArrangeRow(items, 90, 28, 4, 20)
        Assert.True(narrower.HasOverflow)
        Assert.Equal(New List(Of Integer) From {3}, narrower.OverflowIndexes())
        AssertNoOverlap(narrower)
    End Sub

    <Fact>
    Public Sub A_kept_place_is_not_a_droppable_item_for_the_pinned_core_floor()
        Dim onlyPlaces As New List(Of ChromeSlot) From {
            New ChromeSlot("core", 28, pinned:=True), New ChromeSlot("away", 60, soft:=True)}
        ' No real item can collapse, so no "more" button is reserved for it.
        Assert.Equal(28, ChromeLayout.PinnedCoreWidth(onlyPlaces, 4, 28))
        Dim withTail As New List(Of ChromeSlot) From {
            New ChromeSlot("core", 28, pinned:=True), New ChromeSlot("x", 28)}
        Assert.Equal(28 + 4 + 28, ChromeLayout.PinnedCoreWidth(withTail, 4, 28))
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
