#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The per-control font recipe and the audit of rule S9 (specification L0361, ticket L0365): a recipe re-resolves
''' to the size rule S4 assigns, the expected on-screen size is <c>l x M x max(1, D / 96)</c>, and the audit tells a
''' correct font from the two defects that started L0361 (a point font read through the system dpi in a 96 dpi
''' window; a pixel font resolved for the wrong basis). No window is shown. Whole-file "#If Not NETFRAMEWORK".
''' </summary>
Public Class FormScaleRecipesTests

    Private Const SystemDpi As Integer = 168

    <Fact>
    Public Sub A_point_recipe_resolves_by_rule_S4()
        Dim recipe As FormScaleFontRecipe = FormScaleFontRecipe.FromDesignPoints("Segoe UI", 9.0, FontStyle.Regular)
        Assert.Equal(12.0, recipe.DesignPixels, 6)
        Assert.Equal(9.0, recipe.AssignedSize(SystemDpi, SystemDpi), 6)
        Assert.Equal(9.0 * 96 / SystemDpi, recipe.AssignedSize(96, SystemDpi), 6)
        Assert.Equal(12.0, recipe.ExpectedOnScreenPixels(96), 6)
        Assert.Equal(21.0, recipe.ExpectedOnScreenPixels(SystemDpi), 6)
        Assert.Equal(27.0, recipe.ExpectedOnScreenPixels(216), 6)
    End Sub

    <Fact>
    Public Sub A_pixel_recipe_resolves_by_rule_S4()
        Dim recipe As New FormScaleFontRecipe(FormScaleFontKind.Pixel, "Segoe UI", FontStyle.Regular, 12.0 * 1.5)
        Assert.Equal(18.0, recipe.AssignedSize(96, SystemDpi), 6)
        Assert.Equal(31.5, recipe.AssignedSize(SystemDpi, SystemDpi), 6)
        Assert.Equal(40.5, recipe.ExpectedOnScreenPixels(216), 6)
    End Sub

    <Fact>
    Public Sub The_audit_passes_a_correct_font_and_names_a_wrong_one()
        Dim point As FormScaleFontRecipe = FormScaleFontRecipe.FromDesignPoints("Segoe UI", 9.0, FontStyle.Regular)
        ' Right: a 96 dpi window shows 9 x 96 / 168 pt through the system dpi as 12 px.
        Using right As New Font("Segoe UI", CSng(9.0 * 96 / SystemDpi), FontStyle.Regular)
            Assert.Null(FormScaleRecipeTable.Compare("row", point, right, SystemDpi, 96))
        End Using
        ' The reported defect: 9 pt in a 96 dpi window on a 168 dpi system is 21 px, 1.75 times too large.
        Using wrong As New Font("Segoe UI", 9.0F, FontStyle.Regular)
            Dim mismatch As FormScaleFontMismatch = FormScaleRecipeTable.Compare("row", point, wrong, SystemDpi, 96)
            Assert.NotNull(mismatch)
            Assert.Equal(12.0, mismatch.ExpectedPixels, 6)
            Assert.Equal(21.0, mismatch.ActualPixels, 3)
            Assert.Null(FormScaleRecipeTable.Compare("row", point, wrong, SystemDpi, SystemDpi))
        End Using
        ' A pixel font resolved for 96 dpi but shown at 168 after the platform's rescale is too small.
        Dim pixel As New FormScaleFontRecipe(FormScaleFontKind.Pixel, "Segoe UI", FontStyle.Regular, 12.0)
        Using small As New Font("Segoe UI", 12.0F * 96 / SystemDpi, FontStyle.Regular, GraphicsUnit.Pixel)
            Assert.NotNull(FormScaleRecipeTable.Compare("value", pixel, small, SystemDpi, 96))
        End Using
        Using whole As New Font("Segoe UI", 21.0F, FontStyle.Regular, GraphicsUnit.Pixel)
            Assert.Null(FormScaleRecipeTable.Compare("value", pixel, whole, SystemDpi, SystemDpi))
        End Using
    End Sub

    <Fact>
    Public Sub The_audit_names_a_style_change()
        Dim recipe As New FormScaleFontRecipe(FormScaleFontKind.Pixel, "Segoe UI", FontStyle.Regular, 12.0)
        Using bold As New Font("Segoe UI", 12.0F, FontStyle.Bold, GraphicsUnit.Pixel)
            Dim mismatch As FormScaleFontMismatch = FormScaleRecipeTable.Compare("title", recipe, bold, 96, 96)
            Assert.NotNull(mismatch)
            Assert.Contains("style", mismatch.Reason)
        End Using
    End Sub

    ' ---- Rule S10, the heal: one correction for every scale, monitor and order of events ----------------------------

    ''' <summary>The font the window's resolver issues for a recipe at a basis dpi: a pixel font in whole pixels, a point font exact.</summary>
    Private Shared Function Issued(recipe As FormScaleFontRecipe, basisDpi As Integer, systemDpi As Integer) As Font
        Dim size As Double = recipe.AssignedSize(basisDpi, systemDpi)
        If recipe.Kind = FormScaleFontKind.Point Then Return New Font(recipe.Family, CSng(size), recipe.Style)
        Return New Font(recipe.Family, CSng(Math.Round(size)), recipe.Style, GraphicsUnit.Pixel)
    End Function

    <Fact>
    Public Sub The_heal_leaves_every_font_that_rule_S4_resolved_for_the_dpi_the_control_reads_it_at()
        ' Every system dpi, window dpi and design multiplier of the vectors, for a control at the system dpi (a fresh one
        ' or a page that was never shown: it is rescaled by D / S when it is first shown) and for one already at D.
        For Each s As Integer In New Integer() {96, 120, 144, 168, 192}
            For Each d As Integer In New Integer() {96, 120, 144, 168, 192, 216, 288}
                For Each m As Double In New Double() {1.0, 1.25, 1.5, 2.0, 2.5}
                    For Each kind As FormScaleFontKind In New FormScaleFontKind() {FormScaleFontKind.Pixel, FormScaleFontKind.Point}
                        For Each basis As Integer In New Integer() {s, d}
                            Dim recipe As New FormScaleFontRecipe(kind, "Segoe UI", FontStyle.Regular, 12.0 * m)
                            Using font As Font = Issued(recipe, basis, s)
                                Assert.True(FormScaleRecipeTable.HealBasis(recipe, font, basis, d, s) = 0,
                                            $"{kind} S={s} D={d} M={m} c={basis}")
                            End Using
                        Next
                    Next
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_heal_repairs_a_font_resolved_for_the_window_on_a_control_that_still_starts_at_the_system_dpi()
        ' The defect of the Settings window's General page: a page built before it was first shown keeps the system dpi
        ' (168) on its controls while FindForm() already says "this window"; the font was resolved for D, and the platform
        ' then rescaled it again by D / 168 when the page was shown - 0.57 times too small at 96, 1.29 times too large at 216.
        Dim recipe As New FormScaleFontRecipe(FormScaleFontKind.Pixel, "Segoe UI", FontStyle.Regular, 12.0 * 1.5)
        For Each d As Integer In New Integer() {96, 120, 144, 192, 216, 288}
            Using wrong As Font = Issued(recipe, d, SystemDpi)
                Assert.Equal(SystemDpi, FormScaleRecipeTable.HealBasis(recipe, wrong, SystemDpi, d, SystemDpi))
            End Using
            Using right As Font = Issued(recipe, SystemDpi, SystemDpi)
                Assert.Equal(0, FormScaleRecipeTable.HealBasis(recipe, right, SystemDpi, d, SystemDpi))
            End Using
        Next
    End Sub

    <Fact>
    Public Sub The_heal_resolves_a_correction_for_the_control_own_dpi_never_for_the_window()
        Dim recipe As New FormScaleFontRecipe(FormScaleFontKind.Pixel, "Segoe UI", FontStyle.Regular, 12.0 * 1.5)
        ' Settled at 96 (the control is at the window's dpi) but carrying the 168 size: corrected for 96.
        Using big As Font = Issued(recipe, SystemDpi, SystemDpi)
            Assert.Equal(96, FormScaleRecipeTable.HealBasis(recipe, big, 96, 96, SystemDpi))
        End Using
        ' Pending at 168 in a window at 96: corrected for 168, so the rescale at first show makes it right.
        Using small As Font = Issued(recipe, 96, SystemDpi)
            Assert.Equal(SystemDpi, FormScaleRecipeTable.HealBasis(recipe, small, SystemDpi, 96, SystemDpi))
        End Using
    End Sub

    <Fact>
    Public Sub The_heal_leaves_a_font_the_product_did_not_issue_alone()
        Dim recipe As New FormScaleFontRecipe(FormScaleFontKind.Pixel, "Segoe UI", FontStyle.Regular, 18.0)
        Using otherFamily As New Font("Consolas", 7.0F, FontStyle.Regular, GraphicsUnit.Pixel),
              otherStyle As New Font("Segoe UI", 7.0F, FontStyle.Bold, GraphicsUnit.Pixel),
              otherUnit As New Font("Segoe UI", 2.0F, FontStyle.Regular, GraphicsUnit.Millimeter)
            Assert.Equal(0, FormScaleRecipeTable.HealBasis(recipe, otherFamily, 96, 96, SystemDpi))
            Assert.Equal(0, FormScaleRecipeTable.HealBasis(recipe, otherStyle, 96, 96, SystemDpi))
            Assert.Equal(0, FormScaleRecipeTable.HealBasis(recipe, otherUnit, 96, 96, SystemDpi))
            Assert.False(FormScaleRecipeTable.IsOwnFont(recipe, Nothing))
            Assert.False(FormScaleRecipeTable.IsOwnFont(Nothing, otherFamily))
        End Using
    End Sub

    <Fact>
    Public Sub Track_records_only_an_issued_font()
        Dim table As New FormScaleRecipeTable()
        Dim recipe As New FormScaleFontRecipe(FormScaleFontKind.Pixel, "Segoe UI", FontStyle.Regular, 12.0)
        Using issued As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel),
              other As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel),
              given As New Label With {.Font = issued},
              foreign As New Label With {.Font = other}
            table.Issue(issued, recipe)
            table.Track(given)
            table.Track(foreign)
            Dim found As FormScaleFontRecipe = Nothing
            Assert.True(table.TryGetRecipe(given, found))
            Assert.Same(recipe, found)
            Assert.False(table.TryGetRecipe(foreign, found))
            Assert.Single(table.Entries())
        End Using
    End Sub

End Class
#End If
