#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports Xunit

Public Class IconMapTests
    <Fact>
    Public Sub EveryMappedGlyphIsInTheVendoredVocabulary()
        Dim vocabulary As String = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "icon-vocabulary", "vocabulary.jsonl"))
        For Each meaning As IconMeaning In IconMap.All
            ' A meaning waiting for a catalog proposal has no glyph yet - its gate is
            ' in IconographyContractTests.
            If Not meaning.IsVocabulary Then Continue For
            Assert.Contains("""id"": """ & meaning.GlyphId & """", vocabulary)
        Next
    End Sub

    <Fact>
    Public Sub EveryViewerGlyphMeaningIsCoveredByTheToolbarRecolourPass()
        ' Audit B-05: the recolour pass walked a fixed list and forgot btn_Edit, whose
        ' pencil stayed black on the black scheme. The pass now asks
        ' IsToolbarCatalogGlyph of every button, so every viewer meaning with a glyph id
        ' must answer True unless it is a declared text-caption control.
        For Each meaning As IconMeaning In IconMap.All
            If meaning.Surface <> IconMap.IconSurface.Viewer OrElse String.IsNullOrEmpty(meaning.GlyphId) Then Continue For
            Dim expected As Boolean = Not IconMap.TextCaptionToolbarControls.Contains(meaning.ControlName)
            Assert.True(expected = IconMap.IsToolbarCatalogGlyph(meaning.ControlName), meaning.ControlName)
        Next
        Assert.True(IconMap.IsToolbarCatalogGlyph("btn_Edit"))
        Assert.False(IconMap.IsToolbarCatalogGlyph("btn_Translate"))
        ' ICON-SET 0.28 brought action.convert-to-video: the button draws it like any other.
        Assert.True(IconMap.IsToolbarCatalogGlyph("btn_ToVideo"))
        Assert.False(IconMap.IsToolbarCatalogGlyph("btn_Random_Slideshow"))  ' its drawing (variant "random") is still owed
        Assert.False(IconMap.IsToolbarCatalogGlyph("btnShare"))          ' Share Manager surface
    End Sub

    <Fact>
    Public Sub EveryVendoredGlyphParsesAndRenders()
        Dim glyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")
        For Each filePath As String In Directory.GetFiles(glyphDir, "*.svg")
            Dim svg As String = File.ReadAllText(filePath)
            Using bitmap = GlyphRenderer.RenderSvg(svg, 24, Drawing.Color.MediumPurple)
                Assert.Equal(24, bitmap.Width)
                Assert.Equal(24, bitmap.Height)
            End Using
        Next
    End Sub

    <Fact>
    Public Sub AnSvgWithoutPathsOrMalformed_StillThrowsToTheCaller()
        ' Audit 057.S04-2: the failure path now frees the bitmap it would have returned;
        ' the caller must still see the same exception, not a blank glyph.
        Assert.Throws(Of FormatException)(
            Sub() GlyphRenderer.RenderSvg("<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 24 24""><g/></svg>", 24, Drawing.Color.Black))
        Assert.Throws(Of System.Xml.XmlException)(
            Sub() GlyphRenderer.RenderSvg("<svg", 24, Drawing.Color.Black))
    End Sub

    <Fact>
    Public Sub VendoredGlyphHashesMatchTheirProvenance()
        Dim glyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")
        For Each line As String In File.ReadAllLines(Path.Combine(glyphDir, "PROVENANCE.txt"))
            Dim parts As String() = line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length <> 2 OrElse Not parts(0).EndsWith(".svg", StringComparison.Ordinal) Then Continue For
            Using sha As SHA256 = SHA256.Create()
                Dim actual As String = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(glyphDir, parts(0))))).Replace("-", "").ToLowerInvariant()
                Assert.Equal(parts(1), actual)
            End Using
        Next
    End Sub
End Class
#End If
