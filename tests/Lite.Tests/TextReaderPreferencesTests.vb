#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports Xunit

' Specification 277 section 12.1 (plan step P3.2): defaults, bounds and the profile reset scope
' of the text reader and editor preferences.
Public Class TextReaderPreferencesTests

    <Fact>
    Public Sub Every_stored_text_name_is_a_real_preference_and_reset_by_the_profile_reset()
        For Each name In ModernViewerPreferences.TextPreferenceNames
            Assert.NotNull(GetType(ModernViewerPreferences).GetProperty(name))
            Assert.True(ProfileResetScope.IsResetName(name), name)
        Next
        Assert.Equal(28, ModernViewerPreferences.TextPreferenceNames.Length)
    End Sub

    <Fact>
    Public Sub Defaults_match_the_specification_and_every_opt_in_is_off()
        Dim p As New ModernViewerPreferences()
        Assert.False(p.FullTextReaderEnabled)
        Assert.False(p.TextEditingEnabled)
        Assert.False(p.TextTranslationEnabled)
        Assert.Equal("Segoe UI", p.TextFontFamilyProportional)
        Assert.Equal("Consolas", p.TextFontFamilyMonospace)
        Assert.Equal(12.0F, p.TextFontSize)
        Assert.Equal(4, p.TextReadWindowMb)
        Assert.Equal(16, p.TextEditingMaxMb)
        Assert.Equal(30, p.TextRecoveryIntervalSeconds)
        Assert.Equal("source", p.TextMarkdownPresentation)
        Assert.Equal("selection_or_buffer", p.TextTranslationScope)
    End Sub

    <Fact>
    Public Sub Normalize_clamps_every_bound_and_rejects_unknown_choices()
        Dim p As New ModernViewerPreferences() With {
            .TextFontSize = 200.0F, .TextLineSpacing = Single.NaN, .TextTabWidth = 0, .TextReadWindowMb = 64,
            .TextEditingMaxMb = 0, .TextRecoveryIntervalSeconds = 1, .TextBackupVersions = 99,
            .TextTheme = "neon", .TextMarkdownPresentation = "html", .TextTranslationScope = "all",
            .TextEncodingFallbackCodePage = 437, .TextFontFamilyMonospace = "   "}
        p.NormalizeTextPreferences()
        Assert.Equal(48.0F, p.TextFontSize)
        Assert.Equal(1.2F, p.TextLineSpacing)
        Assert.Equal(1, p.TextTabWidth)
        Assert.Equal(16, p.TextReadWindowMb)
        Assert.Equal(1, p.TextEditingMaxMb)
        Assert.Equal(10, p.TextRecoveryIntervalSeconds)
        Assert.Equal(10, p.TextBackupVersions)
        Assert.Equal("system", p.TextTheme)
        Assert.Equal("source", p.TextMarkdownPresentation)
        Assert.Equal("selection_or_buffer", p.TextTranslationScope)
        Assert.Equal(0, p.TextEncodingFallbackCodePage)
        Assert.Equal("Consolas", p.TextFontFamilyMonospace)

        p.TextEncodingFallbackCodePage = 1251
        p.NormalizeTextPreferences()
        Assert.Equal(1251, p.TextEncodingFallbackCodePage)
    End Sub
End Class
#End If
