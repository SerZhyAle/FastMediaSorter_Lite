Option Strict On

Imports System.Drawing
Imports System.IO
Imports Xunit

''' <summary>
''' 129_SPECIFICATION_RECIPIENT_IDENTITY_COLORS.md: the one palette behind the settings grid's
''' folder cells and the recipients overlay's rows. The blend factors are made objective here -
''' the theme's text ink must clear 4.5:1 on every fill, in both themes - and the source scan
''' keeps both surfaces on this one module with the grey row literals gone.
'''
''' Shared file, no "#If": the palette is shared, so every gate runs on net48 and net10.
''' </summary>
Public Class RecipientPaletteTests

    Private Const SlotCount As Integer = 11

    ' The text inks the fills must carry, the brightest actually used per theme: the settings
    ' window's dark text ink and the theme's dark primary text; light windows draw black.
    Private Shared ReadOnly DarkInks() As Color = {
        Color.FromArgb(244, 245, 247),
        Color.FromArgb(240, 240, 240)}
    Private Shared ReadOnly LightInks() As Color = {Color.Black}

    <Fact>
    Public Sub Every_slot_in_both_themes_gets_an_opaque_fill_away_from_the_surface()
        For Each dark As Boolean In {True, False}
            Dim surface As Color = RecipientPalette.ColorFor(-1, dark)
            For slot As Integer = 0 To SlotCount - 1
                Dim fill As Color = RecipientPalette.ColorFor(slot, dark)
                Assert.Equal(255, fill.A)
                Assert.NotEqual(surface, fill)
            Next
        Next
    End Sub

    <Fact>
    Public Sub A_slot_outside_0_to_10_falls_back_to_the_neutral_row_fill()
        For Each dark As Boolean In {True, False}
            Dim surface As Color = RecipientPalette.ColorFor(-1, dark)
            Assert.Equal(surface, RecipientPalette.ColorFor(-1, dark))
            Assert.Equal(surface, RecipientPalette.ColorFor(SlotCount, dark))
            Assert.Equal(surface, RecipientPalette.ColorFor(999, dark))
            Assert.Equal(surface, RecipientPalette.HoverColorFor(SlotCount, dark))
        Next
    End Sub

    <Fact>
    Public Sub Base_hues_are_pairwise_distinct_well_beyond_the_visibility_floor()
        ' The bases are not exposed (the module offers fills only), so each is reconstructed from
        ' its resting fill - fill = round(base*(1-t) + surface*t) inverts exactly, to the blend's
        ' own rounding. The floor stays far under the tuned table's closest pair (light coral and
        ' amber, about 29 RGB) while still being a distance a user tells apart.
        Const floor As Double = 22.0R
        For Each dark As Boolean In {True, False}
            Dim surface As Color = RecipientPalette.ColorFor(-1, dark)
            Dim t As Double = If(dark, 0.6R, 0.7R)
            Dim bases(SlotCount - 1) As Color
            For slot As Integer = 0 To SlotCount - 1
                bases(slot) = ReconstructBase(RecipientPalette.ColorFor(slot, dark), surface, t)
            Next
            For i As Integer = 0 To SlotCount - 2
                For j As Integer = i + 1 To SlotCount - 1
                    Assert.True(RgbDistance(bases(i), bases(j)) >= floor,
                                $"slots {i} and {j} (dark={dark}) are only {RgbDistance(bases(i), bases(j)):F1} apart")
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub Text_ink_clears_45_to_1_on_every_rest_and_hover_fill_in_both_themes()
        For Each dark As Boolean In {True, False}
            Dim inks() As Color = If(dark, DarkInks, LightInks)
            For slot As Integer = 0 To SlotCount - 1
                For Each fill As Color In {RecipientPalette.ColorFor(slot, dark), RecipientPalette.HoverColorFor(slot, dark)}
                    For Each ink As Color In inks
                        Assert.True(Utils.ContrastRatio(ink, fill) >= 4.5R,
                                    $"ink {ink} on slot {slot} fill {fill} (dark={dark}) is {Utils.ContrastRatio(ink, fill):F2}:1")
                    Next
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub Hover_deepens_the_same_blend_instead_of_swapping_identity()
        For Each dark As Boolean In {True, False}
            Dim surface As Color = RecipientPalette.ColorFor(-1, dark)
            For slot As Integer = 0 To SlotCount - 1
                Dim rest As Color = RecipientPalette.ColorFor(slot, dark)
                Dim hover As Color = RecipientPalette.HoverColorFor(slot, dark)
                Assert.True(RgbDistance(hover, surface) > RgbDistance(rest, surface),
                            $"slot {slot} (dark={dark}) hover does not deepen the hue")
            Next
        Next
    End Sub

    <Fact>
    Public Sub Grid_and_overlay_resolve_the_one_palette_and_the_row_literals_are_gone()
        Dim root As String = FindRepoRoot()
        Dim grid As String = File.ReadAllText(Path.Combine(root, "src", "Table_Form.vb"))
        Dim overlay As String = File.ReadAllText(Path.Combine(root, "src", "Main_Form.RecipientsOverlay.vb"))

        ' One module for both surfaces (129 §3.3, §3.4).
        Assert.Contains("RecipientPalette.ColorFor(", grid)
        Assert.Contains("RecipientPalette.ColorFor(", overlay)
        Assert.Contains("RecipientPalette.HoverColorFor(", overlay)

        ' The grey row fills and their hover/press steps left the overlay's row path with the
        ' palette in; the destructive red and the copy zone's accent are states, not recipients.
        For Each literal As String In {"(52, 52, 52)", "(72, 72, 72)", "(96, 96, 96)"}
            Assert.DoesNotContain(literal, overlay)
        Next

        ' Health-by-fill is gone from the grid: the fixed yellow and pink died with it (129 §3.2).
        Assert.DoesNotContain("(255, 248, 220)", grid)
        Assert.DoesNotContain("(255, 226, 226)", grid)
    End Sub

    <Fact>
    Public Sub Grid_reserves_real_padding_preserves_selected_identity_and_honours_high_contrast()
        Dim grid As String = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Table_Form.vb"))
        Assert.Contains("cell.Style.Padding = If(Data_Grid_View.RightToLeft", grid)
        Assert.Contains("New Padding(Sp(SlotMarkZone), 0, 0, 0)", grid)
        Assert.Contains("New Padding(0, 0, Sp(SlotMarkZone), 0)", grid)
        Assert.Contains("cell.Style.SelectionBackColor = If(SystemInformation.HighContrast, SettingsAccent, cell.Style.BackColor)", grid)
        Assert.Contains("If(SystemInformation.HighContrast, SettingsSurface, RecipientPalette.ColorFor(", grid)
        Assert.Contains("e.Paint(e.ClipBounds, DataGridViewPaintParts.All)", grid)
        Assert.DoesNotContain("e.PaintContent(content)", grid)
    End Sub

    <Fact>
    Public Sub Overlay_identity_uses_the_application_theme_even_when_media_chrome_changes()
        Dim overlay As String = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Main_Form.RecipientsOverlay.vb"))
        Assert.Contains("RecipientPalette.ColorFor(slot, ThemeService.IsDark)", overlay)
        Assert.Contains("RecipientPalette.HoverColorFor(slot, ThemeService.IsDark)", overlay)
        Assert.Contains("Return ThemePalette.ForDark(ThemeService.IsDark).TextPrimary", overlay)
        Assert.Contains("row.ForeColor = If(is_Copy_Zone, theme.Foreground, RecipientRowInk())", overlay)
        Assert.DoesNotContain("ChromeTheme.IsDark(chrome_Theme.PanelFill)", overlay)
        Assert.Contains("MouseOverBackColor = If(unusable OrElse theme.IsHighContrast, row.BackColor", overlay)
    End Sub

#If Not NETFRAMEWORK Then
    <Fact>
    Public Sub Actual_application_text_ink_passes_contrast_on_recipient_fills()
        For Each dark As Boolean In {True, False}
            Dim ink As Color = ThemePalette.ForDark(dark).TextPrimary
            For slot As Integer = 0 To SlotCount - 1
                For Each fill As Color In {RecipientPalette.ColorFor(slot, dark), RecipientPalette.HoverColorFor(slot, dark)}
                    Assert.True(Utils.ContrastRatio(ink, fill) >= 4.5R)
                Next
            Next
        Next
    End Sub
#End If

    Private Shared Function ReconstructBase(fill As Color, surface As Color, t As Double) As Color
        Dim w As Double = 1.0R - t
        Return Color.FromArgb(255,
                              CInt(Math.Round((fill.R - surface.R * t) / w, MidpointRounding.AwayFromZero)),
                              CInt(Math.Round((fill.G - surface.G * t) / w, MidpointRounding.AwayFromZero)),
                              CInt(Math.Round((fill.B - surface.B * t) / w, MidpointRounding.AwayFromZero)))
    End Function

    Private Shared Function RgbDistance(a As Color, b As Color) As Double
        ' CInt: Byte - Byte overflows-checked in VB when a channel of b exceeds a's.
        Dim dr As Double = CInt(a.R) - CInt(b.R)
        Dim dg As Double = CInt(a.G) - CInt(b.G)
        Dim db As Double = CInt(a.B) - CInt(b.B)
        Return Math.Sqrt(dr * dr + dg * dg + db * db)
    End Function

    Private Shared Function FindRepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
