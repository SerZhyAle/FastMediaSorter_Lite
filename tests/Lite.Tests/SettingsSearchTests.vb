#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Globalization
Imports Xunit

Public Class SettingsSearchTests
    <Theory>
    <InlineData("", True)>
    <InlineData(" http://localhost:11434 ", True)>
    <InlineData("https://example.com/translate", True)>
    <InlineData("localhost:11434", False)>
    <InlineData("https://", False)>
    <InlineData("file:///C:/settings", False)>
    Public Sub Endpoint_commit_accepts_automatic_or_complete_http_addresses(value As String, expected As Boolean)
        Assert.Equal(expected, SettingsSearchRules.IsServiceEndpoint(value))
    End Sub
    <Fact>
    Public Sub Search_matches_separate_caption_hint_and_option_terms()
        Assert.True(SettingsSearchRules.Matches("  SUBTITLE off  ", "Subtitle language", "Applied on opening video", {"Always off", "English"}, CultureInfo.GetCultureInfo("en-US")))
        Assert.False(SettingsSearchRules.Matches("subtitle download", "Subtitle language", "Applied on opening video", {"Always off"}, CultureInfo.GetCultureInfo("en-US")))
        Assert.True(SettingsSearchRules.Matches("", "Subtitle language", "", {}, CultureInfo.InvariantCulture))
    End Sub

    <Theory>
    <InlineData("ru-RU", "субтитров", "Язык субтитров")>
    <InlineData("ar", "الترجمة", "لغة الترجمة")>
    <InlineData("zh-CN", "字幕", "字幕语言")>
    Public Sub Search_uses_localized_metadata(cultureName As String, query As String, caption As String)
        Assert.True(SettingsSearchRules.Matches(query, caption, "", {}, CultureInfo.GetCultureInfo(cultureName)))
    End Sub
End Class
#End If
