#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text.Json
Imports Xunit

''' <summary>
''' The form scale resolver (specification L0361 section 5 rules S1-S4, section 6.3) replayed against
''' <c>TestData\form-scale-vectors\form-scale-vectors.json</c>, which <c>tools/Build-FormScaleVectors.ps1</c>
''' writes from the formulas apart from this code. Every font row checks the size to assign AND, through the
''' model of the platform's rescale (measured facts E2-E4), the letter size on screen at the window dpi against
''' the independent statement <c>logical x M x D / 96</c>. Whole-file "#If Not NETFRAMEWORK" like the code under test.
''' </summary>
Public Class FormScaleTests

    Private Shared ReadOnly VectorFile As String =
        Path.Combine(AppContext.BaseDirectory, "TestData", "form-scale-vectors", "form-scale-vectors.json")

    Private Const Tolerance As Double = 0.0005

    Private Shared Function LoadVectors() As JsonDocument
        Assert.True(File.Exists(VectorFile), "missing vectors: " & VectorFile)
        Return JsonDocument.Parse(File.ReadAllText(VectorFile))
    End Function

    <Fact>
    Public Sub Every_font_vector_row_matches_the_resolver_and_the_screen()
        Using doc As JsonDocument = LoadVectors()
            Dim rows As JsonElement = doc.RootElement.GetProperty("font")
            Assert.True(rows.GetArrayLength() >= 1680, "too few font rows: " & rows.GetArrayLength().ToString())
            For Each row As JsonElement In rows.EnumerateArray()
                Dim kind As FormScaleFontKind = If(row.GetProperty("kind").GetString() = "point",
                                                   FormScaleFontKind.Point, FormScaleFontKind.Pixel)
                Dim logical As Double = row.GetProperty("logical").GetDouble()
                Dim m As Double = row.GetProperty("M").GetDouble()
                Dim c As Integer = row.GetProperty("c").GetInt32()
                Dim s As Integer = row.GetProperty("S").GetInt32()
                Dim d As Integer = row.GetProperty("D").GetInt32()
                Dim label As String = row.GetRawText()

                Dim assigned As Double = FormScale.AssignedFontSize(kind, logical, m, c, s)
                Assert.True(Math.Abs(assigned - row.GetProperty("expectAssigned").GetDouble()) < Tolerance, "assigned: " & label)

                Dim onScreen As Double = FormScale.PlatformOnScreenPixels(kind, assigned, c, s, d)
                Assert.True(Math.Abs(onScreen - row.GetProperty("expectOnScreenAtD").GetDouble()) < Tolerance, "on screen: " & label)
            Next
        End Using
    End Sub

    <Fact>
    Public Sub Every_px_vector_row_matches_the_resolver()
        Using doc As JsonDocument = LoadVectors()
            Dim rows As JsonElement = doc.RootElement.GetProperty("px")
            Assert.True(rows.GetArrayLength() >= 168, "too few px rows")
            For Each row As JsonElement In rows.EnumerateArray()
                Dim actual As Integer = FormScale.Px(row.GetProperty("logical").GetDouble(),
                                                     row.GetProperty("D").GetInt32(),
                                                     row.GetProperty("M").GetDouble())
                Assert.Equal(row.GetProperty("expectPx").GetInt32(), actual)
            Next
        End Using
    End Sub

    <Fact>
    Public Sub Seed_rows_of_section_6_3_hold()
        ' S = 168, M = 1.5, logical 12 px = 9 pt; a fresh control reads at S, a parented one at D.
        Assert.Equal(31.5, FormScale.PixelFontSize(12, 1.5, 168), 6)
        Assert.Equal(13.5, FormScale.PointFontSize(12, 1.5, 168, 168), 6)
        Assert.Equal(18.0, FormScale.PlatformOnScreenPixels(FormScaleFontKind.Pixel, 31.5, 168, 168, 96), 6)
        Assert.Equal(40.5, FormScale.PlatformOnScreenPixels(FormScaleFontKind.Point, 13.5, 168, 168, 216), 6)
        Assert.Equal(9.0 * 1.5 * 96 / 168, FormScale.PointFontSize(12, 1.5, 96, 168), 6)
    End Sub

    <Fact>
    Public Sub Point_font_resolved_against_the_window_dpi_for_a_fresh_control_is_the_reported_defect()
        ' The page "Server and resources" defect (section 4): a point size not rebased by c / S, assigned in a
        ' 96 dpi window on a 168 dpi system, comes out 1.75 times too large.
        Dim wrong As Double = FormScale.PlatformOnScreenPixels(FormScaleFontKind.Point, 9.0, 96, 168, 96)
        Dim right As Double = FormScale.PlatformOnScreenPixels(FormScaleFontKind.Point,
                                                               FormScale.RebasePoints(9.0, 96, 168), 96, 168, 96)
        Assert.Equal(12.0, right, 6)
        Assert.Equal(1.75, wrong / right, 6)
    End Sub

    <Fact>
    Public Sub Garbage_inputs_fall_back_to_the_logical_baseline()
        Assert.Equal(1.0, FormScale.LayoutFactor(0))
        Assert.Equal(1.0, FormScale.LayoutFactor(72))
        Assert.Equal(12, FormScale.Px(12, 96, Double.NaN))
        Assert.Equal(12, FormScale.Px(12, -5, 0))
        Assert.Equal(9.0, FormScale.RebasePoints(9.0, 0, 96), 6)
    End Sub

    <Fact>
    Public Sub Single_dpi_machine_gets_unchanged_fonts()
        ' c = D = S: the T-1 helpers must give what they gave before the basis rule.
        For Each dpi As Integer In {96, 120, 144, 168, 192}
            Assert.Equal(9.0, FormScale.RebasePoints(9.0, dpi, dpi), 6)
        Next
    End Sub

End Class
#End If
