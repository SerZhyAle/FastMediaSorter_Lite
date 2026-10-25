Option Strict On
Imports System.IO
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class ExchangeHonestTextTests
    <Fact>
    Public Sub Contract_languages_are_verbatim_in_the_app_and_privacy_page_and_other_languages_are_complete()
        Dim root = ExchangeTestSupport.RepoRoot()
        Dim go = File.ReadAllText(Path.Combine(root, "exchange-server/internal/statement/honest.go"))
        Dim privacy = System.Net.WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(root, "docs/privacy.html")))
        Dim saved = Localization.CurrentCode
        Try
            For Each language In Localization.Codes
                Localization.CurrentCode = language
                Dim paragraphs = ExchangeStatement.Paragraphs(7)
                Assert.Equal(2, paragraphs.Count)
                Assert.Contains("7", paragraphs(1))
                If language = "en" OrElse language = "ru" OrElse language = "uk" Then
                    Dim canonical = Regex.Match(go, "honest" & language.ToUpperInvariant() & " = `([^`]+)`").Groups(1).Value
                    Assert.NotEmpty(canonical)
                    Assert.Equal(canonical, paragraphs(0))
                    Assert.Contains(canonical, privacy)
                    Dim drive As New Dictionary(Of String, String) From {
                        {"en", "Via Google Drive, files and the clipboard are kept in your own Google account, like your other files on Google Drive; no exchange server takes part."},
                        {"ru", "Через Google Drive файлы и буфер хранятся в вашем собственном аккаунте Google, как и другие ваши файлы на Google Drive; сервер обмена в этом не участвует."},
                        {"uk", "Через Google Drive файли й буфер зберігаються у вашому власному акаунті Google, як і інші ваші файли на Google Drive; сервер обміну в цьому не бере участі."}}
                    Assert.Equal(drive(language), DriveChannelText.PrivacyNote())
                    Assert.Contains(drive(language), privacy)
                Else
                    Assert.NotEmpty(paragraphs(0))
                    Localization.CurrentCode = "ru"
                    Assert.NotEqual(ExchangeStatement.Paragraphs()(0), paragraphs(0))
                End If
            Next
            Assert.False(ExchangeAdminParser.Status("{}").Sealed)
            Assert.True(ExchangeAdminParser.Status("{""sealed"":true}").Sealed)
            Assert.Equal(8, ExchangeStatement.Paragraphs(sealed:=False).Count)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub
End Class
