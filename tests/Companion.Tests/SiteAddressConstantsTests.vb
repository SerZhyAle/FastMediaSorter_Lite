Option Strict On

Imports System.Collections.Generic
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The Share Manager's site constants against tools/site-addresses.jsonl (ticket 150, T-1): each constant
''' stays a constant, and the list - which tools/Test-SiteAddresses.ps1 resolves against the page set -
''' names it for the file that holds it. The viewer's half is Lite.Tests' SiteAddressListTests.
''' </summary>
Public Class SiteAddressConstantsTests

    <Fact>
    Public Sub ShareLinksSiteConstantsAreListedForTheirHolder()
        Dim listed As ISet(Of String) = SiteAddressList.ListedFor("src/FastMediaSorterCompanion/Core/ShareLinks.vb")
        Assert.Contains(ShareLinks.SiteUrl, listed)
        Assert.Contains(ShareLinks.SiteGuideUrl, listed)
        Assert.Contains(ShareLinks.ServerEditionUrl, listed)
    End Sub

    <Fact>
    Public Sub TheHostingTextServerPageIsListedForItsHolder()
        Assert.Contains(HostingText.ServerEditionUrl, SiteAddressList.ListedFor("src/FastMediaSorterCompanion/Core/HostingText.vb"))
        Assert.Equal(ShareLinks.ServerEditionUrl, HostingText.ServerEditionUrl)
    End Sub

    <Fact>
    Public Sub TheConstantsShareTheLinkedSiteRoot()
        Assert.Equal(SitePages.SiteRoot, ShareLinks.SiteUrl)
        Assert.StartsWith(SitePages.SiteRoot, ShareLinks.SiteGuideUrl)
        Assert.StartsWith(SitePages.SiteRoot, ShareLinks.ServerEditionUrl)
    End Sub

End Class
