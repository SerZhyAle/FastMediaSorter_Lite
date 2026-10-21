#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' The arithmetic of the settings page rail (SETTINGS-NAVIGATION 2.0 section 2 rules 5 and 7; ticket 243, slice
''' T-7a). Pure: no window, no font, no registry. The window that uses these rules is held by
''' <c>SettingsRailTests</c>.
''' </summary>
Public Class SettingsRailRulesTests

    ' --- sizes (rule 5) ------------------------------------------------------------------------

    <Fact>
    Public Sub Sizes_are_the_ordinary_pointer_tokens_and_never_retyped()
        Dim tokens As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)
        Assert.Equal(32, tokens.Target)
        Assert.Equal(16, tokens.GlyphTier)
        Assert.Equal(8, tokens.GlyphGap)
        Assert.Equal(tokens.Target, SettingsRailRules.RowTargetPx(1.0F))
        Assert.Equal(tokens.GlyphTier, SettingsRailRules.GlyphPx(1.0F))
        Assert.Equal(tokens.GlyphGap, SettingsRailRules.GlyphGapPx(1.0F))
    End Sub

    <Theory>
    <InlineData(1.0F, 32, 16, 8)>
    <InlineData(1.25F, 40, 20, 10)>
    <InlineData(1.5F, 48, 24, 12)>
    <InlineData(2.0F, 64, 32, 16)>
    Public Sub Sizes_scale_with_the_interface_factor(factor As Single, row As Integer, glyph As Integer, gap As Integer)
        Assert.Equal(row, SettingsRailRules.RowTargetPx(factor))
        Assert.Equal(glyph, SettingsRailRules.GlyphPx(factor))
        Assert.Equal(gap, SettingsRailRules.GlyphGapPx(factor))
    End Sub

    <Theory>
    <InlineData(Single.NaN)>
    <InlineData(Single.PositiveInfinity)>
    <InlineData(0.0F)>
    <InlineData(-1.0F)>
    Public Sub A_garbage_factor_gives_the_logical_sizes(factor As Single)
        Assert.Equal(32, SettingsRailRules.RowTargetPx(factor))
        Assert.Equal(16, SettingsRailRules.GlyphPx(factor))
        Assert.Equal(8, SettingsRailRules.GlyphGapPx(factor))
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(0.9F)>
    Public Sub A_row_is_never_shorter_than_the_target_whatever_the_caption(factor As Single)
        Dim target As Integer = SettingsRailRules.RowTargetPx(factor)
        For Each captionHeight As Integer In {Integer.MinValue, -5, 0, 1, 10, 14, 17, 100, Integer.MaxValue}
            Dim height As Integer = SettingsRailRules.RowHeightPx(factor, captionHeight)
            Assert.True(height >= target, String.Format("caption {0}: row {1} < target {2}", captionHeight, height, target))
            Assert.True(height <= UiSizing.MaxDevicePx)
        Next
    End Sub

    <Fact>
    Public Sub A_one_line_caption_keeps_the_target_and_a_wrapped_one_grows_the_row()
        ' 15 px of text + 6 px slack = 21: inside the 32 px target, so nothing grows.
        Assert.Equal(32, SettingsRailRules.RowHeightPx(1.0F, 15))
        Assert.Equal(32, SettingsRailRules.RowHeightPx(1.0F, 26))
        ' Two lines of 15 px + 6 px slack = 36.
        Assert.Equal(36, SettingsRailRules.RowHeightPx(1.0F, 30))
        ' Three lines: 45 + 6.
        Assert.Equal(51, SettingsRailRules.RowHeightPx(1.0F, 45))
    End Sub

    ' --- rail width (rule 5) -------------------------------------------------------------------

    <Fact>
    Public Sub The_rail_is_the_longest_caption_plus_glyph_gap_and_padding()
        ' 120 + 16 + 8 + 40 = 184, well under 30 % of 1000.
        Assert.Equal(184, SettingsRailRules.RailWidth(120.0R, 16, 8, 40, 1000.0R))
    End Sub

    <Fact>
    Public Sub A_fractional_caption_width_rounds_up_so_the_text_never_loses_a_pixel()
        Assert.Equal(185, SettingsRailRules.RailWidth(120.2R, 16, 8, 40, 1000.0R))
    End Sub

    <Fact>
    Public Sub The_rail_never_takes_more_than_thirty_percent_of_the_client()
        ' Wanted 700 + 16 + 8 + 40 = 764 is above the share: 30 % of 1000 is 300.
        Assert.Equal(300, SettingsRailRules.RailWidth(700.0R, 16, 8, 40, 1000.0R))
        Assert.Equal(0.3R, SettingsRailRules.MaxShareOfClient)
    End Sub

    <Fact>
    Public Sub The_share_limit_rounds_down_never_past_the_share()
        ' 30 % of 999 is 299.7: the limit is 299.
        Assert.Equal(299, SettingsRailRules.RailWidth(700.0R, 16, 8, 40, 999.0R))
    End Sub

    <Fact>
    Public Sub The_floor_is_padding_glyph_gap_and_six_glyph_widths_of_caption()
        Dim floor As Integer = 40 + 16 + 8 + SettingsRailRules.FloorCaptionGlyphs * 16
        Assert.Equal(6, SettingsRailRules.FloorCaptionGlyphs)
        ' A failed measurement (0) or a one-letter caption is raised to the floor.
        Assert.Equal(floor, SettingsRailRules.RailWidth(0.0R, 16, 8, 40, 2000.0R))
        Assert.Equal(floor, SettingsRailRules.RailWidth(7.0R, 16, 8, 40, 2000.0R))
    End Sub

    <Fact>
    Public Sub The_floor_beats_the_share_limit_on_a_tiny_client()
        Dim floor As Integer = 40 + 16 + 8 + 6 * 16
        ' 30 % of 200 is 60, far below the floor: the floor wins, a rail narrower than its own glyph is not a rail.
        Assert.Equal(floor, SettingsRailRules.RailWidth(300.0R, 16, 8, 40, 200.0R))
    End Sub

    <Theory>
    <InlineData(0.0R)>
    <InlineData(-100.0R)>
    <InlineData(Double.NaN)>
    <InlineData(Double.PositiveInfinity)>
    <InlineData(Double.NegativeInfinity)>
    Public Sub An_unknown_client_width_sets_no_share_limit(client As Double)
        Assert.Equal(784, SettingsRailRules.RailWidth(720.0R, 16, 8, 40, client))
    End Sub

    <Theory>
    <InlineData(Double.NaN)>
    <InlineData(Double.PositiveInfinity)>
    <InlineData(Double.NegativeInfinity)>
    <InlineData(-50.0R)>
    Public Sub A_garbage_caption_width_counts_as_nothing(caption As Double)
        Assert.Equal(SettingsRailRules.RailWidth(0.0R, 16, 8, 40, 1000.0R), SettingsRailRules.RailWidth(caption, 16, 8, 40, 1000.0R))
    End Sub

    <Fact>
    Public Sub The_width_is_never_negative_and_never_above_the_device_ceiling()
        Assert.True(SettingsRailRules.RailWidth(-1.0R, -5, -5, -5, -1.0R) >= 0)
        Assert.True(SettingsRailRules.RailWidth(Double.NaN, Integer.MinValue, Integer.MinValue, Integer.MinValue, Double.NaN) >= 0)
        Assert.Equal(UiSizing.MaxDevicePx, SettingsRailRules.RailWidth(1.0E+30R, 16, 8, 40, 0.0R))
        Assert.Equal(UiSizing.MaxDevicePx, SettingsRailRules.RailWidth(1.0E+30R, Integer.MaxValue, Integer.MaxValue, Integer.MaxValue, 0.0R))
    End Sub

    <Fact>
    Public Sub A_wider_caption_never_gives_a_narrower_rail()
        Dim previous As Integer = 0
        For caption As Integer = 0 To 600 Step 7
            Dim width As Integer = SettingsRailRules.RailWidth(caption, 16, 8, 40, 1000.0R)
            Assert.True(width >= previous, String.Format("caption {0}: {1} < {2}", caption, width, previous))
            previous = width
        Next
    End Sub

    ' --- keyboard (rule 7) ---------------------------------------------------------------------

    <Theory>
    <InlineData(0, 11, CInt(RailKey.Down), 1)>
    <InlineData(5, 11, CInt(RailKey.Down), 6)>
    <InlineData(9, 11, CInt(RailKey.Down), 10)>
    <InlineData(10, 11, CInt(RailKey.Down), 10)>
    <InlineData(10, 11, CInt(RailKey.Up), 9)>
    <InlineData(1, 11, CInt(RailKey.Up), 0)>
    <InlineData(0, 11, CInt(RailKey.Up), 0)>
    <InlineData(5, 11, CInt(RailKey.Home), 0)>
    <InlineData(0, 11, CInt(RailKey.Home), 0)>
    <InlineData(5, 11, CInt(RailKey.End), 10)>
    <InlineData(10, 11, CInt(RailKey.End), 10)>
    Public Sub Arrow_home_and_end_move_the_focus_and_never_wrap(current As Integer, count As Integer, keyCode As Integer, expected As Integer)
        Assert.Equal(expected, SettingsRailRules.NextIndex(current, count, CType(keyCode, RailKey)))
    End Sub

    <Theory>
    <InlineData(CInt(RailKey.Up), 0)>
    <InlineData(CInt(RailKey.Down), 0)>
    <InlineData(CInt(RailKey.Home), 0)>
    <InlineData(CInt(RailKey.End), 10)>
    <InlineData(CInt(RailKey.Enter), -1)>
    <InlineData(CInt(RailKey.Space), -1)>
    <InlineData(CInt(RailKey.None), -1)>
    Public Sub No_focus_yet_enters_the_list_at_an_end(keyCode As Integer, expected As Integer)
        Assert.Equal(expected, SettingsRailRules.NextIndex(-1, 11, CType(keyCode, RailKey)))
    End Sub

    <Theory>
    <InlineData(-1)>
    <InlineData(0)>
    <InlineData(3)>
    Public Sub An_empty_list_has_no_row_to_focus(current As Integer)
        For Each key As RailKey In [Enum].GetValues(GetType(RailKey))
            Assert.Equal(-1, SettingsRailRules.NextIndex(current, 0, key))
            Assert.Equal(-1, SettingsRailRules.NextIndex(current, -4, key))
        Next
    End Sub

    <Fact>
    Public Sub A_focus_past_the_end_counts_as_the_last_row()
        Assert.Equal(10, SettingsRailRules.NextIndex(40, 11, RailKey.Down))
        Assert.Equal(9, SettingsRailRules.NextIndex(40, 11, RailKey.Up))
        Assert.Equal(10, SettingsRailRules.NextIndex(40, 11, RailKey.None))
        Assert.Equal(0, SettingsRailRules.NextIndex(Integer.MaxValue, 1, RailKey.Up))
    End Sub

    <Fact>
    Public Sub A_single_row_list_stays_on_its_row()
        For Each key As RailKey In [Enum].GetValues(GetType(RailKey))
            If key = RailKey.None OrElse key = RailKey.Enter OrElse key = RailKey.Space Then Continue For
            Assert.Equal(0, SettingsRailRules.NextIndex(0, 1, key))
        Next
    End Sub

    <Fact>
    Public Sub Keys_that_do_not_move_the_focus_leave_it_where_it_is()
        For Each key As RailKey In {RailKey.None, RailKey.Enter, RailKey.Space}
            Assert.Equal(4, SettingsRailRules.NextIndex(4, 11, key))
        Next
    End Sub

    <Fact>
    Public Sub Walking_down_and_then_up_visits_every_row_once()
        Dim at As Integer = 0
        Dim seen As New List(Of Integer) From {at}
        For step1 As Integer = 1 To 30
            at = SettingsRailRules.NextIndex(at, 11, RailKey.Down)
            If at <> seen(seen.Count - 1) Then seen.Add(at)
        Next
        Assert.True(Enumerable.Range(0, 11).SequenceEqual(seen), String.Join(",", seen))
        For step2 As Integer = 1 To 30
            at = SettingsRailRules.NextIndex(at, 11, RailKey.Up)
        Next
        Assert.Equal(0, at)
    End Sub

    <Fact>
    Public Sub Only_enter_and_space_choose_a_page()
        Assert.True(SettingsRailRules.IsSelectionKey(RailKey.Enter))
        Assert.True(SettingsRailRules.IsSelectionKey(RailKey.Space))
        For Each key As RailKey In {RailKey.None, RailKey.Up, RailKey.Down, RailKey.Home, RailKey.End}
            Assert.False(SettingsRailRules.IsSelectionKey(key), key.ToString())
        Next
    End Sub

    <Fact>
    Public Sub No_movement_key_is_also_a_selection_key()
        For Each key As RailKey In [Enum].GetValues(GetType(RailKey))
            Dim moves As Boolean = SettingsRailRules.NextIndex(2, 11, key) <> 2
            If moves Then Assert.False(SettingsRailRules.IsSelectionKey(key), key.ToString())
        Next
    End Sub

End Class
#End If
