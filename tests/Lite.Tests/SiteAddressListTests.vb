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

End Class
#End If
