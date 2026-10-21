#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' The shared size-token resolver (UI-SIZING 1.1 sections 2 and 5; BUTTONS 2.0 section 3; BOOLEAN-OPTION 2.0
''' section 2; COLLAPSIBLE-GROUP 1.1 section 3; ticket 243 slice T-3): the token table, the composition with the
''' interface factor, the independent text factor on glyphs, and the pins that hold the first adopters
''' (ButtonGlyphs, SettingsCheckBox, SettingsCollapsibleGroup) and the viewer chrome constants to the table.
''' Whole-file "#If Not NETFRAMEWORK" like the code under test.
''' </summary>
Public Class UiSizingTests

    Private Shared ReadOnly Profiles As UiInputProfile() = {UiInputProfile.Pointer, UiInputProfile.Touch}
    Private Shared ReadOnly Densities As UiDensity() = {UiDensity.Ordinary, UiDensity.Compact}
    Private Shared ReadOnly Garbage As Single() = {
        Single.NaN, 0.0F, -1.0F, Single.PositiveInfinity, Single.NegativeInfinity}

    ' --- the token table ------------------------------------------------------------------------

    <Fact>
    Public Sub Ordinary_pointer_row_is_the_buttons_boolean_and_group_baseline()
        Dim t As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)
        Assert.Equal(32, t.Target)
        Assert.Equal(12, t.PaddingX)
        Assert.Equal(16, t.GlyphTier)
        Assert.Equal(8, t.GlyphGap)
        Assert.Equal(8, t.ButtonGap)
        Assert.Equal(16, t.GroupGap)
        Assert.Equal(20, t.CheckBox)
        Assert.Equal(16, t.MarkerTier)
    End Sub

    <Fact>
    Public Sub Ordinary_touch_row_is_the_larger_baseline()
        Dim t As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Touch, UiDensity.Ordinary)
        Assert.Equal(48, t.Target)
        Assert.Equal(16, t.PaddingX)
        Assert.Equal(20, t.GlyphTier)
        Assert.Equal(8, t.GlyphGap)
        Assert.Equal(8, t.ButtonGap)
        Assert.Equal(16, t.GroupGap)
        Assert.Equal(24, t.CheckBox)
        Assert.Equal(16, t.MarkerTier)
    End Sub

    <Fact>
    Public Sub Compact_row_is_the_same_for_both_profiles_and_has_the_declared_floor()
        Assert.Equal(28, UiSizing.CompactTargetFloor)
        For Each profile As UiInputProfile In Profiles
            Dim t As UiSizeTokens = UiSizing.Tokens(profile, UiDensity.Compact)
            Assert.Equal(UiSizing.CompactTargetFloor, t.Target)
            Assert.Equal(8, t.PaddingX)
            Assert.Equal(16, t.GlyphTier)
            Assert.Equal(8, t.GlyphGap)
            Assert.Equal(8, t.ButtonGap)
            Assert.Equal(16, t.GroupGap)
            Assert.Equal(16, t.CheckBox)
            Assert.Equal(16, t.MarkerTier)
        Next
    End Sub

    <Fact>
    Public Sub Ordinary_baselines_are_boolean_option_minimums_and_compact_is_smaller_than_ordinary()
        Dim pointer As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)
        Dim touch As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Touch, UiDensity.Ordinary)
        Dim compact As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Touch, UiDensity.Compact)
        Assert.True(pointer.CheckBox >= 20, "BOOLEAN-OPTION 2.0 section 2: the box is at least 20 for a pointer.")
        Assert.True(touch.CheckBox >= 24, "BOOLEAN-OPTION 2.0 section 2: the box is at least 24 for touch.")
        Assert.True(compact.Target < pointer.Target AndAlso pointer.Target < touch.Target)
        Assert.True(compact.PaddingX < pointer.PaddingX AndAlso pointer.PaddingX < touch.PaddingX)
        Assert.True(compact.CheckBox < pointer.CheckBox AndAlso pointer.CheckBox < touch.CheckBox)
    End Sub

    <Fact>
    Public Sub Tokens_never_return_a_target_below_the_compact_floor_even_for_a_value_outside_the_enums()
        For Each profile As UiInputProfile In Profiles.Concat({CType(7, UiInputProfile), CType(-3, UiInputProfile)})
            For Each density As UiDensity In Densities.Concat({CType(9, UiDensity), CType(-1, UiDensity)})
                Dim t As UiSizeTokens = UiSizing.Tokens(profile, density)
                Assert.NotNull(t)
                Assert.True(t.Target >= UiSizing.CompactTargetFloor,
                            "Tokens(" & profile.ToString() & ", " & density.ToString() & ").Target = " & t.Target)
                Assert.True(t.GlyphTier > 0 AndAlso t.CheckBox > 0 AndAlso t.MarkerTier > 0)
            Next
        Next
        ' A value outside the enums is the ordinary pointer row, never a smaller one.
        Assert.Equal(32, UiSizing.Tokens(CType(7, UiInputProfile), CType(9, UiDensity)).Target)
    End Sub

    ' --- DevicePx -------------------------------------------------------------------------------

    <Fact>
    Public Sub DevicePx_is_monotonic_in_size_and_factor_and_never_zero_for_a_positive_token()
        Dim factors As New List(Of Single)()
        For f As Integer = 50 To 400 Step 5
            factors.Add(f / 100.0F)
        Next
        For Each factor As Single In factors
            Dim previous As Integer = 0
            For logical As Integer = 1 To 200
                Dim px As Integer = UiSizing.DevicePx(logical, factor)
                Assert.True(px >= 1, "DevicePx(" & logical & ", " & factor & ") must not be zero.")
                Assert.True(px >= previous, "DevicePx must not decrease as the logical size grows.")
                Assert.Equal(px, UiSizing.DevicePx(logical, factor))
                previous = px
            Next
        Next
        For Each logical As Integer In New Integer() {1, 8, 16, 20, 24, 28, 32, 48}
            Dim previous As Integer = 0
            For Each factor As Single In factors
                Dim px As Integer = UiSizing.DevicePx(logical, factor)
                Assert.True(px >= previous, "DevicePx must not decrease as the factor grows.")
                previous = px
            Next
        Next
    End Sub

    <Fact>
    Public Sub DevicePx_agrees_with_UiScale_Px_for_every_finite_positive_factor()
        ' The resolver mirrors UiScale.Px (the Share Manager cannot link UiScale); the two must not drift.
        For Each factor As Single In New Single() {0.5F, 0.9F, 1.0F, 1.25F, 1.5F, 1.75F, 2.0F, 3.0F, 4.0F}
            For logical As Integer = 1 To 100
                Assert.Equal(UiScale.Px(logical, factor), UiSizing.DevicePx(logical, factor))
            Next
        Next
    End Sub

    <Fact>
    Public Sub DevicePx_treats_a_nonsense_factor_as_one_and_a_nonsense_size_as_nothing()
        For Each factor As Single In Garbage
            For Each logical As Integer In New Integer() {1, 16, 32, 48}
                Assert.Equal(logical, UiSizing.DevicePx(logical, factor))
                Assert.Equal(UiSizing.DevicePx(logical, 1.0F), UiSizing.DevicePx(logical, factor))
            Next
            Assert.Equal(0, UiSizing.DevicePx(0, factor))
            Assert.Equal(0, UiSizing.DevicePx(-5, factor))
        Next
        Assert.Equal(1, UiSizing.DevicePx(1, 0.0001F))
        Assert.Equal(UiSizing.MaxDevicePx, UiSizing.DevicePx(Integer.MaxValue, 1.0F))
        Assert.Equal(UiSizing.MaxDevicePx, UiSizing.DevicePx(32, 1.0E+30F))
    End Sub

    ' --- TargetPx -------------------------------------------------------------------------------

    <Fact>
    Public Sub TargetPx_is_never_zero_and_never_decreases_as_the_interface_factor_grows()
        For Each profile As UiInputProfile In Profiles
            For Each density As UiDensity In Densities
                Dim previous As Integer = 0
                For Each factor As Single In New Single() {0.9F, 1.0F, 1.5F, 4.0F}
                    Dim px As Integer = UiSizing.TargetPx(profile, density, factor)
                    Assert.True(px >= 1, "TargetPx(" & profile.ToString() & ", " & density.ToString() & ", " & factor & ") is zero.")
                    Assert.True(px >= previous, "TargetPx must not decrease as the factor grows.")
                    previous = px
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub TargetPx_at_factor_one_is_the_logical_token_and_follows_the_profile_order()
        Assert.Equal(32, UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.0F))
        Assert.Equal(48, UiSizing.TargetPx(UiInputProfile.Touch, UiDensity.Ordinary, 1.0F))
        Assert.Equal(28, UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Compact, 1.0F))
        Assert.Equal(28, UiSizing.TargetPx(UiInputProfile.Touch, UiDensity.Compact, 1.0F))
        For Each factor As Single In New Single() {0.9F, 1.0F, 1.5F, 4.0F}
            Assert.True(UiSizing.TargetPx(UiInputProfile.Touch, UiDensity.Ordinary, factor) >
                        UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, factor))
            Assert.True(UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, factor) >
                        UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Compact, factor))
        Next
    End Sub

    <Fact>
    Public Sub TargetPx_is_never_below_the_compact_floor_at_the_same_factor()
        For Each profile As UiInputProfile In Profiles
            For Each density As UiDensity In Densities
                For Each factor As Single In New Single() {0.9F, 1.0F, 1.25F, 1.5F, 2.0F, 4.0F}
                    Assert.True(UiSizing.TargetPx(profile, density, factor) >= UiSizing.DevicePx(UiSizing.CompactTargetFloor, factor),
                                "TargetPx(" & profile.ToString() & ", " & density.ToString() & ", " & factor & ") fell below the floor.")
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub TargetPx_does_not_shrink_without_bound_for_a_garbage_or_tiny_factor()
        Dim lowest As Integer = UiSizing.DevicePx(UiSizing.CompactTargetFloor, UiSizing.MinTargetScale)
        Assert.True(lowest >= 20, "the guard floor of a target is " & lowest)
        For Each profile As UiInputProfile In Profiles
            For Each density As UiDensity In Densities
                For Each factor As Single In Garbage.Concat({0.0001F, 0.01F, 0.5F, 0.74F})
                    Assert.True(UiSizing.TargetPx(profile, density, factor) >= lowest,
                                "TargetPx(" & profile.ToString() & ", " & density.ToString() & ", " & factor & ") = " &
                                UiSizing.TargetPx(profile, density, factor))
                Next
            Next
        Next
        ' A garbage factor is the logical size, exactly like DevicePx.
        Assert.Equal(32, UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, Single.NaN))
        Assert.Equal(48, UiSizing.TargetPx(UiInputProfile.Touch, UiDensity.Ordinary, Single.PositiveInfinity))
    End Sub

    <Fact>
    Public Sub A_target_takes_no_text_factor()
        ' UI-SIZING section 2 rule 3: text enlargement grows text and layout in the caller; targets do not take it.
        Dim names As String() = GetType(UiSizing).GetMethod("TargetPx").GetParameters().Select(Function(p) p.Name).ToArray()
        Assert.Equal(New String() {"profile", "density", "interfaceFactor"}, names)
        Dim before As Integer = UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.25F)
        UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.25F, 2.0F)
        Assert.Equal(before, UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.25F))
    End Sub

    ' --- GlyphPx and the text factor ------------------------------------------------------------

    <Fact>
    Public Sub The_recorded_glyph_and_text_bounds_are_the_constants_the_brief_names()
        Assert.Equal(1.5F, UiSizing.GlyphGrowthLimit)
        Assert.Equal(2.25F, UiSizing.MaxTextFactor)
        Assert.Equal(1.0F, UiSizing.GlyphGrowthFactor(1.0F))
        Assert.Equal(1.25F, UiSizing.GlyphGrowthFactor(1.5F))
        Assert.Equal(UiSizing.GlyphGrowthLimit, UiSizing.GlyphGrowthFactor(2.0F))
        Assert.Equal(UiSizing.GlyphGrowthLimit, UiSizing.GlyphGrowthFactor(UiSizing.MaxTextFactor))
        Assert.Equal(UiSizing.GlyphGrowthLimit, UiSizing.GlyphGrowthFactor(50.0F))
    End Sub

    <Fact>
    Public Sub GlyphPx_with_no_text_enlargement_is_the_tier_at_the_interface_factor()
        For Each profile As UiInputProfile In Profiles
            For Each density As UiDensity In Densities
                Dim tier As Integer = UiSizing.Tokens(profile, density).GlyphTier
                For Each factor As Single In New Single() {0.9F, 1.0F, 1.25F, 1.5F, 2.0F, 4.0F}
                    Assert.Equal(UiSizing.DevicePx(tier, factor), UiSizing.GlyphPx(profile, density, factor, 1.0F))
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub GlyphPx_grows_with_the_text_factor_but_never_past_one_and_a_half_times_its_tier()
        For Each profile As UiInputProfile In Profiles
            For Each density As UiDensity In Densities
                Dim tier As Integer = UiSizing.Tokens(profile, density).GlyphTier
                For Each factor As Single In New Single() {0.9F, 1.0F, 1.25F, 1.5F, 2.0F, 4.0F}
                    ' Same operation order as the resolver (tier x (interface x growth)), so the midpoint cases agree.
                    Dim ceiling As Integer = CInt(Math.Round(tier * (CDbl(factor) * 1.5R), MidpointRounding.AwayFromZero))
                    Dim baseline As Integer = UiSizing.GlyphPx(profile, density, factor, 1.0F)
                    Dim previous As Integer = baseline
                    For Each text As Single In New Single() {1.0F, 1.25F, 1.5F, 1.75F, 2.0F, 2.25F, 3.0F, 100.0F}
                        Dim px As Integer = UiSizing.GlyphPx(profile, density, factor, text)
                        Assert.True(px >= previous, "GlyphPx must not shrink as the text factor grows.")
                        Assert.True(px <= ceiling, "GlyphPx " & px & " exceeds 1.5 x tier " & ceiling & " at text " & text)
                        previous = px
                    Next
                    Assert.Equal(ceiling, previous)
                Next
            Next
        Next
        ' At the ordinary pointer tier and factor 1.0 the growth is visible: 16 -> 24 at a text factor of 2.0.
        Assert.Equal(16, UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.0F, 1.0F))
        Assert.True(UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.0F, 2.0F) > 16)
        Assert.Equal(24, UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.0F, 2.0F))
        Assert.Equal(24, UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.0F, UiSizing.MaxTextFactor))
    End Sub

    <Fact>
    Public Sub GlyphPx_treats_a_smaller_or_nonsense_text_factor_as_no_enlargement()
        For Each profile As UiInputProfile In Profiles
            For Each density As UiDensity In Densities
                For Each factor As Single In New Single() {0.9F, 1.0F, 1.5F}
                    Dim plain As Integer = UiSizing.GlyphPx(profile, density, factor, 1.0F)
                    For Each text As Single In Garbage.Concat({0.5F, 0.99F})
                        Assert.Equal(plain, UiSizing.GlyphPx(profile, density, factor, text))
                    Next
                Next
            Next
        Next
        Assert.Equal(1.0F, UiSizing.NormalizeTextFactor(0.5F))
        Assert.Equal(1.0F, UiSizing.NormalizeTextFactor(Single.NaN))
        Assert.Equal(1.0F, UiSizing.NormalizeTextFactor(Single.NegativeInfinity))
        Assert.Equal(1.0F, UiSizing.NormalizeTextFactor(Single.PositiveInfinity))
        Assert.Equal(UiSizing.MaxTextFactor, UiSizing.NormalizeTextFactor(9.0F))
    End Sub

    <Fact>
    Public Sub GlyphPx_applies_the_interface_factor_and_a_garbage_interface_factor_is_one()
        For Each text As Single In New Single() {1.0F, 1.5F, 2.0F}
            For Each factor As Single In Garbage
                Assert.Equal(UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.0F, text),
                             UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, factor, text))
            Next
            Assert.True(UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 2.0F, text) >=
                        UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, 1.0F, text))
        Next
    End Sub

    ' --- the system text size -------------------------------------------------------------------

    <Fact>
    Public Sub SystemTextScale_is_always_within_one_and_the_text_maximum_and_never_throws()
        Dim scale As Single = UiSizing.SystemTextScale()
        Assert.InRange(scale, 1.0F, UiSizing.MaxTextFactor)
    End Sub

    <Fact>
    Public Sub A_text_scale_percent_becomes_a_bounded_factor()
        Assert.Equal(1.0F, UiSizing.TextScaleFromPercent(100))
        Assert.Equal(1.25F, UiSizing.TextScaleFromPercent(125))
        Assert.Equal(1.5F, UiSizing.TextScaleFromPercent(150))
        Assert.Equal(2.25F, UiSizing.TextScaleFromPercent(225))
        Assert.Equal(2.25F, UiSizing.TextScaleFromPercent(300))
        Assert.Equal(2.25F, UiSizing.TextScaleFromPercent(Integer.MaxValue))
        Assert.Equal(1.0F, UiSizing.TextScaleFromPercent(99))
        Assert.Equal(1.0F, UiSizing.TextScaleFromPercent(0))
        Assert.Equal(1.0F, UiSizing.TextScaleFromPercent(-100))
        Assert.Equal(1.0F, UiSizing.TextScaleFromPercent(Integer.MinValue))
    End Sub

    ' --- the adopters and the chrome are held to the table --------------------------------------

    <Fact>
    Public Sub Button_glyphs_take_the_ordinary_pointer_tier_and_gap_from_the_table()
        Dim t As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)
        Assert.Equal(t.GlyphTier, ButtonGlyphs.GlyphLogicalSize)
        Assert.Equal(t.GlyphGap, ButtonGlyphs.GapLogicalSize)
    End Sub

    <Fact>
    Public Sub The_settings_check_box_takes_box_target_and_gap_from_the_table()
        Dim t As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)
        Assert.Equal(t.CheckBox, SettingsCheckBox.BoxSideLogical)
        Assert.Equal(t.Target, SettingsCheckBox.ControlSideLogical)
        Assert.Equal(t.GlyphGap, SettingsCheckBox.CaptionGapLogical)
    End Sub

    <Fact>
    Public Sub The_collapsible_group_takes_marker_tier_and_gap_from_the_table()
        Dim t As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)
        Assert.Equal(t.MarkerTier, SettingsCollapsibleGroup.MarkerTierLogical)
        Assert.Equal(t.GlyphGap, SettingsCollapsibleGroup.MarkerGapLogical)
    End Sub

    <Fact>
    Public Sub The_viewer_chrome_constants_are_pinned_to_the_table_until_the_chrome_slice_adopts_it()
        ' T-9 owns the chrome; until then these three equalities are the only coupling, and they fail loudly
        ' when either side moves alone. UiScale.TouchPreset (44 / 28) is a known deviation from the 48 touch row.
        Dim t As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)
        Assert.Equal(t.Target, ChromeMetrics.ItemHeight)
        Assert.Equal(t.GlyphTier, ChromeMetrics.GlyphSize)
        Assert.Equal(UiSizing.CompactTargetFloor, ChromeMetrics.MinTarget)
    End Sub

End Class
#End If
