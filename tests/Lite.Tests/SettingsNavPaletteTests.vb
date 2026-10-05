#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The gates of SPECIFICATION 128: the settings navigation draws a vocabulary glyph on
''' every page (§3.1 - the "text-only nav row" defect class cannot return), every page's
''' glyph takes its identity ink from the one table (§3.2), every ink holds 3 : 1
''' against its own theme's sidebar fill, and the twelve dark inks are pairwise
''' distinguishable. Like SettingsColorGateTests this reads the sources where the
''' alternative is building the form; the palette table itself is linked and tested
''' directly, including its behaviour under the mutations of §5 scenario 4. Net10 only:
''' the x86 fallback keeps its five-page UI under the maintenance policy of CLAUDE.md.
''' </summary>
Public Class SettingsNavPaletteTests

    ' 128 §3.2: two pages may not share a recognizable hue. The reviewed table's closest
    ' dark pair (Video #EF5350 / Files #FF7043) sits near 36 RGB units apart; the gate
    ' holds a little under it, so the reviewed values pass and a duplicate or a
    ' near-duplicate fails.
    Private Const MinimumRgbDistance As Double = 32.0

    ' --- §3.1 every page has a glyph -------------------------------------------------

    <Fact>
    Public Sub Every_navigation_page_has_a_vocabulary_glyph_and_a_vendored_asset()
        Assert.Empty(CompletenessViolations(NavControlNamesFromSource()))
    End Sub

    <Fact>
    Public Sub The_completeness_gate_fails_when_a_row_loses_its_map_entry()
        ' §5 scenario 4: removing a nav row's IconMap entry (the settingsNavGeneral/
        ' settingsNavStorage defect the ticket fixes) must fail the gate, not pass it.
        Dim violations = CompletenessViolations({"settingsNavGeneral", "settingsNavImaginary"})
        Assert.Single(violations)
        Assert.Contains("settingsNavImaginary", violations(0))
    End Sub

    <Fact>
    Public Sub The_identity_table_covers_exactly_the_navigation_rows()
        Dim names As New HashSet(Of String)(NavControlNamesFromSource(), StringComparer.Ordinal)
        Dim paletteNames As New HashSet(Of String)(SettingsNavIconPalette.All.Select(Function(e) e.ControlName), StringComparer.Ordinal)
        Assert.True(names.SetEquals(paletteNames),
                    "the identity table and the navigation disagree; only in nav: " &
                    String.Join(", ", names.Except(paletteNames)) & "; only in table: " &
                    String.Join(", ", paletteNames.Except(names)))
    End Sub

    <Fact>
    Public Sub The_lookup_returns_the_theme_ink_for_every_row()
        For Each entry As SettingsNavIconPalette.NavInk In SettingsNavIconPalette.All
            Dim ink As Color = Nothing
            Assert.True(SettingsNavIconPalette.TryGetInk(entry.ControlName, True, ink))
            Assert.Equal(entry.DarkInk, ink)
            Assert.True(SettingsNavIconPalette.TryGetInk(entry.ControlName, False, ink))
            Assert.Equal(entry.LightInk, ink)
        Next
        Dim unknown As Color = Nothing
        Assert.False(SettingsNavIconPalette.TryGetInk("settingsNavImaginary", True, unknown))
        Assert.False(SettingsNavIconPalette.TryGetInk("", True, unknown))
    End Sub

    ' --- §3.2 contrast ---------------------------------------------------------------

    <Fact>
    Public Sub The_two_sidebar_fills_come_from_the_palette_loader()
        ' The gate measures against the fills the window really paints: exactly the two
        ' SettingsSidebar literals of LoadSystemSettingsPalette, dark and light.
        Dim fills = SidebarFillsFromSource()
        Assert.True(RelativeLuminance(fills.Dark) < RelativeLuminance(fills.Light),
                    "the darker SettingsSidebar literal is not the dark theme's")
    End Sub

    <Fact>
    Public Sub Every_ink_holds_three_to_one_against_its_own_sidebar()
        Dim fills = SidebarFillsFromSource()
        Dim failures As New List(Of String)()
        For Each entry As SettingsNavIconPalette.NavInk In SettingsNavIconPalette.All
            Dim dark As Double = ContrastRatio(entry.DarkInk, fills.Dark)
            If dark < 3.0 Then failures.Add(entry.ControlName & " dark " & entry.DarkInk.ToString() & " at " & dark.ToString("0.00", Globalization.CultureInfo.InvariantCulture))
            Dim light As Double = ContrastRatio(entry.LightInk, fills.Light)
            If light < 3.0 Then failures.Add(entry.ControlName & " light " & entry.LightInk.ToString() & " at " & light.ToString("0.00", Globalization.CultureInfo.InvariantCulture))
        Next
        Assert.True(failures.Count = 0,
                    "inks under 3:1 against their sidebar: " & vbLf & String.Join(vbLf, failures))
    End Sub

    ' --- §3.2 distinctness -----------------------------------------------------------

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Sub Identity_inks_meet_contrast_on_selected_hover_and_pressed_surfaces(dark As Boolean)
        Dim sidebar As Color = If(dark, Color.FromArgb(25, 26, 29), Color.FromArgb(238, 240, 244))
        Dim accent As Color = Color.FromArgb(0, 120, 215)
        Dim fills As New List(Of Color) From {sidebar}
        For Each amount As Double In {0.18, 0.22, 0.28, 0.3, 0.4}
            fills.Add(Color.FromArgb(CInt(sidebar.R + (CInt(accent.R) - CInt(sidebar.R)) * amount),
                                    CInt(sidebar.G + (CInt(accent.G) - CInt(sidebar.G)) * amount),
                                    CInt(sidebar.B + (CInt(accent.B) - CInt(sidebar.B)) * amount)))
        Next
        For Each entry In SettingsNavIconPalette.All
            Dim ink As Color = SettingsNavIconPalette.ResolveInk(entry.ControlName, dark, fills.ToArray(),
                                                                  If(dark, Color.White, Color.Black), False)
            For Each fill As Color In fills
                Assert.True(Utils.ContrastRatio(ink, fill) >= 3.0, entry.ControlName & " " & fill.ToString())
            Next
        Next
    End Sub

    <Fact>
    Public Sub High_contrast_uses_the_system_row_foreground_instead_of_identity()
        Assert.Equal(Color.Yellow, SettingsNavIconPalette.ResolveInk("settingsNavOcr", True,
                     {Color.Black}, Color.Yellow, True))
    End Sub

    <Fact>
    Public Sub The_twelve_dark_inks_are_pairwise_distinguishable()
        Assert.Empty(DistinctnessViolations(SettingsNavIconPalette.All))
    End Sub

    <Fact>
    Public Sub The_distinctness_gate_fails_on_a_duplicated_ink()
        ' §5 scenario 4: two palette entries made identical must fail the gate.
        Dim mutated As List(Of SettingsNavIconPalette.NavInk) = SettingsNavIconPalette.All.ToList()
        mutated(1) = New SettingsNavIconPalette.NavInk(mutated(1).ControlName, mutated(0).DarkInk, mutated(1).LightInk)
        Dim violations = DistinctnessViolations(mutated)
        Assert.Single(violations)
        Assert.Contains(mutated(0).ControlName, violations(0))
        Assert.Contains(mutated(1).ControlName, violations(0))
    End Sub

    ' ------------------------------------------------------------------ gate bodies ----

    Private Shared Function CompletenessViolations(controlNames As String()) As List(Of String)
        Dim violations As New List(Of String)()
        For Each name As String In controlNames
            Dim meaning As IconMeaning = IconMap.GetMeaning(name)
            If meaning Is Nothing Then
                violations.Add(name & " has no IconMap row")
            ElseIf String.IsNullOrEmpty(meaning.GlyphId) Then
                violations.Add(name & " maps to no glyph")
            ElseIf Not File.Exists(Path.Combine(GlyphDir(), meaning.GlyphId & ".svg")) Then
                violations.Add(name & " -> " & meaning.GlyphId & ".svg is not vendored")
            End If
        Next
        Return violations
    End Function

    Private Shared Function DistinctnessViolations(entries As IEnumerable(Of SettingsNavIconPalette.NavInk)) As List(Of String)
        Dim violations As New List(Of String)()
        Dim listed As List(Of SettingsNavIconPalette.NavInk) = entries.ToList()
        For i As Integer = 0 To listed.Count - 1
            For j As Integer = i + 1 To listed.Count - 1
                Dim distance As Double = RgbDistance(listed(i).DarkInk, listed(j).DarkInk)
                If distance < MinimumRgbDistance Then
                    violations.Add(listed(i).ControlName & " and " & listed(j).ControlName &
                                   " share a recognizable hue (" & distance.ToString("0.0", Globalization.CultureInfo.InvariantCulture) & " RGB units apart)")
                End If
            Next
        Next
        Return violations
    End Function

    Private Shared Function RgbDistance(a As Color, b As Color) As Double
        Dim dr As Double = CDbl(a.R) - b.R
        Dim dg As Double = CDbl(a.G) - b.G
        Dim db As Double = CDbl(a.B) - b.B
        Return Math.Sqrt(dr * dr + dg * dg + db * db)
    End Function

    Private NotInheritable Class SidebarFills
        Public Dark As Color
        Public Light As Color
    End Class

    Private Shared Function SidebarFillsFromSource() As SidebarFills
        Dim pattern As New Regex("SettingsSidebar = Color\.FromArgb\((\d+), (\d+), (\d+)\)", RegexOptions.Compiled)
        Dim matches As List(Of Match) = pattern.Matches(ReadSource("Table_Form.ModernLayout.vb")).Cast(Of Match)().ToList()
        Assert.True(matches.Count = 2,
                    "expected the two SettingsSidebar literals of LoadSystemSettingsPalette, found " & matches.Count)
        Dim first As Color = Color.FromArgb(CInt(matches(0).Groups(1).Value), CInt(matches(0).Groups(2).Value), CInt(matches(0).Groups(3).Value))
        Dim second As Color = Color.FromArgb(CInt(matches(1).Groups(1).Value), CInt(matches(1).Groups(2).Value), CInt(matches(1).Groups(3).Value))
        Dim fills As New SidebarFills()
        If RelativeLuminance(first) < RelativeLuminance(second) Then
            fills.Dark = first
            fills.Light = second
        Else
            fills.Dark = second
            fills.Light = first
        End If
        Return fills
    End Function

    ' ------------------------------------------------------------------- source read ----

    Private Shared Function NavControlNamesFromSource() As String()
        Dim arrayPattern As New Regex("ModernNavControlNames As String\(\) = \{(.*?)\}", RegexOptions.Singleline Or RegexOptions.Compiled)
        Dim match As Match = arrayPattern.Match(ReadSource("Table_Form.ModernLayout.vb"))
        Assert.True(match.Success, "ModernNavControlNames not found in Table_Form.ModernLayout.vb")
        Dim namePattern As New Regex("""([^""]+)""", RegexOptions.Compiled)
        Dim names As List(Of String) = namePattern.Matches(match.Groups(1).Value).
            Cast(Of Match)().Select(Function(m) m.Groups(1).Value).ToList()
        Assert.True(names.Count >= 11, "only " & names.Count & " navigation rows parsed")
        Return names.ToArray()
    End Function

    Private Shared Function ReadSource(fileName As String) As String
        Return File.ReadAllText(Path.Combine(SourceDir(), fileName))
    End Function

    Private Shared Function GlyphDir() As String
        Return Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")
    End Function

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function
End Class
#End If
