#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' The program constants of src/Support/SitePages.vb against tools/site-addresses.jsonl (ticket 150, T-1):
''' the constants stay constants, and the list - which the gate resolves against the page set - names every
''' one of them. Whether each address answers is tools/Test-SiteAddresses.ps1, not this test.
''' </summary>
Public Class SiteAddressListTests

    Private Const Holder As String = "src/Support/SitePages.vb"

    <Fact>
    Public Sub EveryAddressOfTheListIsOnTheProgramsSiteRoot()
        For Each e As SiteAddressList.Entry In SiteAddressList.Entries()
            Assert.StartsWith(SitePages.SiteRoot, e.Address)
        Next
    End Sub

    <Fact>
    Public Sub TheSiteRootAndThePrivacyPageAreListedForTheirHolder()
        Dim listed As ISet(Of String) = SiteAddressList.ListedFor(Holder)
        Assert.Contains(SitePages.SiteRoot, listed)
        Assert.Contains(SitePages.PrivacyPageUrl, listed)
    End Sub

    <Fact>
    Public Sub TheTrustPageIsListedAndItsLocaleFormIsTheListsOwn()
        Assert.Contains(SitePages.TrustPageUrl("en"), SiteAddressList.ListedFor(Holder))
        Assert.Equal(SitePages.TrustPageUrl(""), SitePages.TrustPageUrl("en"))
        ' The form is stated once, in the list's trust group; the builder must produce exactly it.
        Dim form As String = SiteAddressList.LocaleForm("trust")
        Assert.NotNull(form)
        For Each code As String In Localization.Codes.Where(Function(c) c <> "en")
            Assert.Equal(SitePages.SiteRoot & form.Replace("<code>", code), SitePages.TrustPageUrl(code))
        Next
    End Sub

    <Fact>
    Public Sub TheLandingFollowsTheUiLanguageToItsGeneratedCopy()
        Assert.Equal(SitePages.SiteRoot, SitePages.ForLanguage(SitePages.SiteRoot, "en"))
        Assert.Equal(SitePages.SiteRoot, SitePages.ForLanguage(SitePages.SiteRoot, ""))
        For Each code As String In Localization.Codes.Where(Function(c) c <> "en")
            Assert.Equal(SitePages.SiteRoot & "lang/" & code & "/", SitePages.ForLanguage(SitePages.SiteRoot, code))
            Assert.Equal(SitePages.TrustPageUrl(code), SitePages.ForLanguage(SitePages.SiteRoot & "trust.html", code))
        Next
    End Sub

    <Fact>
    Public Sub AGuidePageGetsTheQueryTheSiteReadsForRussianAndUkrainianOnly()
        Dim guide As String = SitePages.SiteRoot & "publish-folders-android.html"
        Assert.Equal(guide & "?lang=ru", SitePages.ForLanguage(guide, "ru"))
        Assert.Equal(guide & "?lang=uk", SitePages.ForLanguage(guide, "uk"))
        Assert.Equal(guide, SitePages.ForLanguage(guide, "en"))
        Assert.Equal(guide, SitePages.ForLanguage(guide, "de"))
        Assert.Equal(SitePages.PrivacyPageUrl & "?lang=ru", SitePages.ForLanguage(SitePages.PrivacyPageUrl, "ru"))
    End Sub

    <Fact>
    Public Sub AnAddressThatIsNotOursOrAlreadyQualifiedIsLeftAlone()
        Assert.Equal("https://example.com/", SitePages.ForLanguage("https://example.com/", "ru"))
        Dim q As String = SitePages.SiteRoot & "server.html?lang=uk"
        Assert.Equal(q, SitePages.ForLanguage(q, "ru"))
        Assert.Null(SitePages.ForLanguage(Nothing, "ru"))
    End Sub

    <Fact>
    Public Sub TheAndroidAndDocumentTranslationSitesFollowTheUiLanguage()
        Assert.Equal(SitePages.AndroidAppSiteRoot & "index-ru.html", SitePages.AndroidAppSiteUrl("ru"))
        Assert.Equal(SitePages.AndroidAppSiteRoot & "index-uk.html", SitePages.AndroidAppSiteUrl("uk"))
        Assert.Equal(SitePages.AndroidAppSiteRoot, SitePages.AndroidAppSiteUrl("de"))
        Assert.Equal(SitePages.DocHtmlTranslateSiteRoot & "?l=ru", SitePages.DocHtmlTranslateSiteUrl("ru"))
        Assert.Equal(SitePages.DocHtmlTranslateSiteRoot & "de/", SitePages.DocHtmlTranslateSiteUrl("de"))
        Assert.Equal(SitePages.DocHtmlTranslateSiteRoot, SitePages.DocHtmlTranslateSiteUrl("en"))
    End Sub

End Class
#End If
