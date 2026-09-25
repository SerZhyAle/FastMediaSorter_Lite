Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 027 section 5.4 for the Share Manager: every glyph the icon map assigns to a
''' Share Manager control is named by a string registered in the Share Manager's OWN
''' table (the viewer's tables are not compiled in here), with every translation.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class IconNameTests

    <Fact>
    Public Sub Every_share_manager_name_is_a_registered_string_with_all_translations()
        Dim known As New Dictionary(Of String, String())(StringComparer.Ordinal)
        For Each kv In Localization.All
            known(kv.Key) = kv.Value
        Next
        Dim entries = IconMap.All.Where(Function(m) m.Surface = IconMap.IconSurface.ShareManager).ToList()
        Assert.NotEmpty(entries)
        For Each meaning As IconMap.IconMeaning In entries
            Assert.True(known.ContainsKey(meaning.LocalizationKey), meaning.ControlName & ": name key not registered: " & meaning.LocalizationKey)
            Assert.DoesNotContain(known(meaning.LocalizationKey), Function(t) String.IsNullOrWhiteSpace(t))
        Next
    End Sub

    <Fact>
    Public Sub Every_share_manager_glyph_is_embedded()
        For Each meaning As IconMap.IconMeaning In IconMap.All.Where(Function(m) m.Surface = IconMap.IconSurface.ShareManager AndAlso m.IsVocabulary)
            Using s = CompanionAssets.OpenBundledAsset("glyphs/" & meaning.GlyphId & ".svg")
                Assert.True(s IsNot Nothing, meaning.GlyphId & ".svg is not embedded in the Share Manager")
            End Using
        Next
    End Sub

End Class
