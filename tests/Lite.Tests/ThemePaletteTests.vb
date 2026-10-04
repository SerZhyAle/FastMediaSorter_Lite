#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

''' <summary>
''' The application theme's pure half (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md,
''' APP-STYLE rules 2-4). The window-walking half cannot be unit-tested; what it relies on is here:
''' the stored spelling, the resolution (high contrast included), a complete table, light mode being
''' the pre-060 look, and the map that decides what the applier may repaint.
''' </summary>
Public Class ThemePaletteTests

    <Theory>
    <InlineData("system", ThemeMode.System)>
    <InlineData("light", ThemeMode.Light)>
    <InlineData("dark", ThemeMode.Dark)>
    <InlineData("DARK", ThemeMode.Dark)>
    <InlineData(" light ", ThemeMode.Light)>
    <InlineData("", ThemeMode.System)>
    <InlineData(Nothing, ThemeMode.System)>
    <InlineData("sepia", ThemeMode.System)>
    Public Sub Stored_values_parse_and_anything_unknown_is_system(stored As String, expected As ThemeMode)
        Assert.Equal(expected, ThemeModes.Parse(stored))
    End Sub

    <Fact>
    Public Sub Every_mode_round_trips_through_its_stored_spelling()
        For Each mode As ThemeMode In {ThemeMode.System, ThemeMode.Light, ThemeMode.Dark}
            Assert.Equal(mode, ThemeModes.Parse(ThemeModes.ToSetting(mode)))
        Next
    End Sub

    <Fact>
    Public Sub The_stored_value_is_the_one_both_programs_read()
        Assert.Equal("UiTheme", ThemeModes.SettingValue)
    End Sub

    <Theory>
    <InlineData(ThemeMode.System, True, False, False)>
    <InlineData(ThemeMode.System, False, False, True)>
    <InlineData(ThemeMode.Light, False, False, False)>
    <InlineData(ThemeMode.Dark, True, False, True)>
    <InlineData(ThemeMode.Dark, False, True, False)>
    <InlineData(ThemeMode.System, False, True, False)>
    Public Sub Resolution_follows_the_mode_and_high_contrast_always_wins(mode As ThemeMode, appsUseLight As Boolean,
                                                                         highContrast As Boolean, expectedDark As Boolean)
        Assert.Equal(expectedDark, ThemeModes.ResolveDark(mode, appsUseLight, highContrast))
    End Sub

    <Fact>
    Public Sub Every_role_has_a_value_in_both_palettes()
        For Each role As ThemeRole In ThemePalette.AllRoles()
            Assert.False(ThemePalette.Light.ColorOf(role).IsEmpty, "light " & role.ToString())
            Assert.False(ThemePalette.Dark.ColorOf(role).IsEmpty, "dark " & role.ToString())
        Next
        Assert.True(ThemePalette.Light.ColorOf(ThemeRole.None).IsEmpty)
    End Sub

    ''' <summary>Spec 060 rule 5: light mode is the pre-060 look, so the roles the applier writes
    ''' in place of a system colour are that same system colour.</summary>
    <Fact>
    Public Sub Light_mode_writes_back_the_system_colours_it_replaced()
        Dim p As ThemePalette = ThemePalette.Light
        Assert.Equal(SystemColors.Control, p.SurfaceWindow)
        Assert.Equal(SystemColors.Window, p.SurfaceRaised)
        Assert.Equal(SystemColors.ControlText, p.TextPrimary)
        Assert.Equal(SystemColors.GrayText, p.TextMuted)
        Assert.Equal(SystemColors.Highlight, p.Accent)
        Assert.Equal(SystemColors.HighlightText, p.AccentInk)
        Assert.Equal(SystemColors.HotTrack, p.Link)
    End Sub

    <Fact>
    Public Sub Dark_text_is_readable_on_every_dark_surface_it_is_drawn_on()
        Dim p As ThemePalette = ThemePalette.Dark
        For Each surface As Color In {p.SurfaceWindow, p.SurfaceRaised, p.SurfaceSunken, p.ControlFace}
            Assert.True(Contrast(p.TextPrimary, surface) >= 7.0, "primary on " & surface.ToString())
            Assert.True(Contrast(p.TextMuted, surface) >= 4.5, "muted on " & surface.ToString())
            Assert.True(Contrast(p.Link, surface) >= 4.5, "link on " & surface.ToString())
            For Each status As Color In {p.Success, p.Warning, p.Danger}
                Assert.True(Contrast(status, surface) >= 4.5, status.ToString() & " on " & surface.ToString())
            Next
        Next
        Assert.True(Contrast(p.AccentInk, p.Accent) >= 4.5, "accent ink")
    End Sub

    ''' <summary>APP-STYLE section 4: `text.disabled` is the text of an unavailable control, and a control
    ''' the user cannot use still has to be legible enough to say what it is (the 3 : 1 floor of
    ''' ICON-RENDER section 3 rule 3, the only measured floor the catalog gives an inactive mark).</summary>
    <Fact>
    Public Sub Dark_disabled_text_holds_three_to_one_on_the_control_face()
        Dim p As ThemePalette = ThemePalette.Dark
        For Each surface As Color In {p.SurfaceWindow, p.SurfaceRaised, p.ControlFace}
            Assert.True(Contrast(p.TextDisabled, surface) >= 3.0, "disabled on " & surface.ToString())
        Next
    End Sub

    <Fact>
    Public Sub System_colours_map_to_roles_and_the_codes_own_colours_do_not()
        Assert.Equal(ThemeRole.SurfaceWindow, ThemePalette.RoleForSystemColor(SystemColors.Control, True))
        Assert.Equal(ThemeRole.SurfaceRaised, ThemePalette.RoleForSystemColor(SystemColors.Window, True))
        Assert.Equal(ThemeRole.SurfaceSelected, ThemePalette.RoleForSystemColor(SystemColors.Info, True))
        Assert.Equal(ThemeRole.Accent, ThemePalette.RoleForSystemColor(SystemColors.Highlight, True))
        Assert.Equal(ThemeRole.TextPrimary, ThemePalette.RoleForSystemColor(SystemColors.ControlText, False))
        Assert.Equal(ThemeRole.TextPrimary, ThemePalette.RoleForSystemColor(SystemColors.WindowText, False))
        Assert.Equal(ThemeRole.TextMuted, ThemePalette.RoleForSystemColor(SystemColors.GrayText, False))
        Assert.Equal(ThemeRole.AccentInk, ThemePalette.RoleForSystemColor(SystemColors.HighlightText, False))
        ' A literal, even one equal in value to a system colour, is the code's own choice.
        Assert.Equal(ThemeRole.None, ThemePalette.RoleForSystemColor(Color.White, True))
        Assert.Equal(ThemeRole.None, ThemePalette.RoleForSystemColor(Color.FromArgb(32, 32, 32), True))
        Assert.Equal(ThemeRole.None, ThemePalette.RoleForSystemColor(Color.Transparent, True))
    End Sub

    ''' <summary>A status line painted ForestGreen before the theme existed follows it, in both
    ''' directions; an arbitrary literal does not.</summary>
    <Fact>
    Public Sub Status_colours_of_either_palette_are_recognised_as_their_role()
        Assert.Equal(ThemeRole.Success, ThemePalette.RoleForColor(Color.ForestGreen, False))
        Assert.Equal(ThemeRole.Danger, ThemePalette.RoleForColor(Color.Firebrick, False))
        Assert.Equal(ThemeRole.Warning, ThemePalette.RoleForColor(Color.FromArgb(239, 108, 0), False))
        Assert.Equal(ThemeRole.Success, ThemePalette.RoleForColor(ThemePalette.Dark.Success, False))
        Assert.Equal(ThemeRole.None, ThemePalette.RoleForColor(Color.FromArgb(30, 120, 220), False))
        Assert.Equal(ThemeRole.None, ThemePalette.RoleForColor(Color.ForestGreen, True))
        Assert.Equal(ThemePalette.Dark.Danger, ThemePalette.Dark.Restate(Color.Firebrick, False))
        Assert.Equal(Color.Firebrick.ToArgb(), ThemePalette.Light.Restate(ThemePalette.Dark.Danger, False).ToArgb())
        Assert.Equal(Color.White, ThemePalette.Dark.Restate(Color.White, False))
    End Sub

    ''' <summary>The literal greys the forms used for hints and notes mean "muted" and must not
    ''' stay dark grey on charcoal (reported on the Share Manager, 2026-09-26).</summary>
    <Fact>
    Public Sub Literal_hint_greys_are_muted_text()
        For Each grey As Color In {Color.DimGray, Color.Gray, Color.DarkGray}
            Assert.Equal(ThemeRole.TextMuted, ThemePalette.RoleForColor(grey, False))
            Assert.Equal(ThemePalette.Dark.TextMuted, ThemePalette.Dark.Restate(grey, False))
        Next
        Assert.Equal(ThemeRole.None, ThemePalette.RoleForColor(Color.DimGray, True))
    End Sub

    ' WCAG 2 contrast ratio.
    Private Shared Function Contrast(a As Color, b As Color) As Double
        Dim la As Double = Luminance(a)
        Dim lb As Double = Luminance(b)
        Return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05)
    End Function

    Private Shared Function Luminance(c As Color) As Double
        Return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B)
    End Function

    Private Shared Function Channel(v As Byte) As Double
        Dim s As Double = v / 255.0
        Return If(s <= 0.03928, s / 12.92, Math.Pow((s + 0.055) / 1.055, 2.4))
    End Function

End Class
#End If
