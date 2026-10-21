#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The About page of the viewer (ticket 243 T-6, SETTINGS-NAVIGATION 2.0 section 3 rules 3 and 4): the twelve required
''' items each have a row in the builder, building the page contacts nothing (only a click may start a process or open an
''' address), and the link table and the opener behind the links answer for every language. The viewer window itself is
''' not built here - it needs the whole viewer - so the inventory reads the builder's source, as DialogGlueTests does.
''' The Share Manager's half is Companion.Tests/AboutPageTests.vb.
''' </summary>
<Collection("AppFileLogger")>
Public Class AboutPageTests

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function Source(fileName As String) As String
        Return File.ReadAllText(Path.Combine(SourceDir(), fileName))
    End Function

    ''' <summary>The source without its whole-line comments, so a sentence that says "no Process.Start here" is not a finding.</summary>
    Private Shared Function CodeOnly(text As String) As String
        Return String.Join(vbLf, text.Split({vbCrLf, vbLf}, StringSplitOptions.None).
            Where(Function(line) Not line.TrimStart().StartsWith("'", StringComparison.Ordinal)))
    End Function

    ''' <summary>One method, by its declaration up to its own End Sub / End Function.</summary>
    Private Shared Function MethodBody(text As String, declaration As String) As String
        Dim start As Integer = text.IndexOf(declaration, StringComparison.Ordinal)
        Assert.True(start >= 0, declaration & " not found")
        Dim endMark As String = If(declaration.Contains(" Function "), "End Function", "End Sub")
        Dim finish As Integer = text.IndexOf(endMark, start, StringComparison.Ordinal)
        Assert.True(finish > start, "no " & endMark & " after " & declaration)
        Return CodeOnly(text.Substring(start, finish - start))
    End Function

    Private Const NetworkTokens As String = "Process\.Start|HttpClient|WebClient|WebRequest|HttpWebRequest|TcpClient|\bSocket\b|\bDns\.|\bPing\b"

    ' --- the inventory -----------------------------------------------------------------------------------

    <Fact>
    Public Sub TheTwelveRequiredItemsAreDistinctAndEachNamesAViewerRowAndAManagerControl()
        Assert.Equal(12, AboutItems.Required.Length)
        Assert.Equal(12, AboutItems.Required.Distinct().Count())
        For Each item As String In AboutItems.Required
            Assert.False(String.IsNullOrEmpty(AboutItems.ViewerRow(item)), "no viewer row for " & item)
            Assert.StartsWith("manager", AboutItems.ManagerControl(item))
        Next
        ' The Manager names one control per item; the viewer shares a row only where one block carries two items.
        Assert.Equal(12, AboutItems.Required.Select(Function(item) AboutItems.ManagerControl(item)).Distinct().Count())
        Dim sharedRows As String() = AboutItems.Required.GroupBy(Function(item) AboutItems.ViewerRow(item)).Where(Function(g) g.Count() > 1).Select(Function(g) g.Key).ToArray()
        Assert.Equal({"about_intro"}, sharedRows)
        Assert.Equal("", AboutItems.ViewerRow("no-such-item"))
        Assert.Equal("", AboutItems.ManagerControl("no-such-item"))
    End Sub

    <Fact>
    Public Sub TheViewerAboutBuilderCarriesARowForEveryRequiredItem()
        Dim layout As String = Source("Table_Form.ModernLayout.vb")
        Dim builder As String = MethodBody(layout, "Private Sub BuildModernAboutPage()")
        For Each item As String In AboutItems.Required
            Dim row As String = AboutItems.ViewerRow(item)
            Assert.Contains("""" & row & """", builder)
            ' The row is not left to show its own key: the title and the description switches both know it.
            Assert.True(Regex.Matches(layout, "Case\s[^\r\n]*""" & Regex.Escape(row) & """").Count >= 2, "no title and description for the row " & row)
        Next
    End Sub

    <Fact>
    Public Sub TheViewerRowsOfTheLinkTableAreAllOnThePage()
        Dim builder As String = MethodBody(Source("Table_Form.ModernLayout.vb"), "Private Sub BuildModernAboutPage()")
        For Each row As String In {"project_help", "project_site", "project_github", "project_releases", "project_privacy", "project_license",
                                   "project_notices", "project_trust", "project_email", "doc_html_site", "related_android", "related_portal"}
            Assert.Contains("""" & row & """", builder)
        Next
    End Sub

    ' --- rule 3.4: opening About contacts nothing ----------------------------------------------------------

    <Fact>
    Public Sub NoAboutOrResourcesBuilderStartsAProcessOrTouchesTheNetwork()
        Dim layout As String = Source("Table_Form.ModernLayout.vb")
        For Each declaration As String In {"Private Sub BuildModernAboutPage()", "Private Sub BuildModernResourcesPage()",
                                            "Private Sub BuildModernSftpPage()", "Private Sub AddInformationBlock(",
                                            "Private Sub AddProjectLinkRow(", "Private Shared Function ModernLinkAddress("}
            Dim body As String = MethodBody(layout, declaration)
            Assert.False(Regex.IsMatch(body, NetworkTokens), declaration & " reaches for a process or the network")
        Next
    End Sub

    <Fact>
    Public Sub OnlyTheClickHandlerOpensAnAddressAndItGoesThroughAboutResources()
        Dim handler As String = MethodBody(Source("Table_Form.ModernLayout.vb"), "Private Sub ModernProjectLinkClicked(")
        Assert.Contains("AboutResources.Open(", handler)
        Assert.DoesNotContain("Process.Start", handler)
        ' ProductLinks is pure string building; AboutResources is the one class that starts anything.
        Dim links As String = CodeOnly(Source("Support\SitePages.vb"))
        Assert.False(Regex.IsMatch(links, NetworkTokens), "SitePages.vb (ProductLinks) must not start a process or touch the network")
        Dim opener As String = CodeOnly(Source("Support\AboutResources.vb"))
        Assert.False(Regex.IsMatch(opener, "HttpClient|WebClient|WebRequest|HttpWebRequest|TcpClient|\bSocket\b|\bDns\.|\bPing\b"), "AboutResources opens things, it never probes them")
    End Sub

    ' --- ProductLinks -------------------------------------------------------------------------------------

    Private Shared Function AllLinkKeys() As IEnumerable(Of String)
        Return ProductLinks.AboutKeys.Concat(ProductLinks.RelatedKeys).Concat(ProductLinks.ResourceKeys).Distinct()
    End Function

    <Fact>
    Public Sub EveryLinkKeyResolvesToAnAddressInEveryLanguage()
        Dim kinds As String() = {"https://", "mailto:", ProductLinks.LocalScheme, ProductLinks.HelpScheme}
        For Each code As String In Localization.Codes
            For Each key As String In AllLinkKeys()
                Dim address As String = ProductLinks.Url(key, code)
                Assert.False(String.IsNullOrEmpty(address), key & " / " & code)
                Assert.Contains(kinds, Function(kind) address.StartsWith(kind, StringComparison.Ordinal))
            Next
        Next
        Assert.Equal("", ProductLinks.Url("no-such-key", "en"))
    End Sub

    <Fact>
    Public Sub TheRequiredAboutLinksAreAllInTheAboutTable()
        For Each key As String In {ProductLinks.Help, ProductLinks.Site, ProductLinks.Repository, ProductLinks.Releases, ProductLinks.Privacy,
                                   ProductLinks.License, ProductLinks.Notices, ProductLinks.Contact}
            Assert.Contains(key, ProductLinks.AboutKeys)
        Next
        Assert.Equal(3, ProductLinks.RelatedKeys.Length)
    End Sub

    <Fact>
    Public Sub TheLicenceAndTheNoticesAreBundledFilesAndTheHelpIsTheShippedHelp()
        Assert.Equal(ProductLinks.LocalScheme & "LICENSE", ProductLinks.Url(ProductLinks.License, "en"))
        Assert.Equal(ProductLinks.LocalScheme & "THIRD-PARTY-NOTICES.txt", ProductLinks.Url(ProductLinks.Notices, "en"))
        Assert.Equal(ProductLinks.HelpScheme & "index", ProductLinks.Url(ProductLinks.Help, "en"))
        Assert.Equal(ProductLinks.HelpScheme & "share-settings.help", ProductLinks.Url(ProductLinks.Help, "en", "share-settings.help"))
        Assert.Equal(ProductLinks.HelpScheme & "index", ProductLinks.Url(ProductLinks.Help, "en", "  "))
    End Sub

    <Fact>
    Public Sub TheContactIsAMailtoWithTheProductAsTheSubject()
        Dim address As String = ProductLinks.Url(ProductLinks.Contact, "de")
        Assert.StartsWith("mailto:" & MailSender.AuthorAddress & "?subject=", address)
        Assert.DoesNotContain(" ", address)
    End Sub

    <Fact>
    Public Sub OnlineForAnswersAnHttpsPageForABundledFileAndForTheHelp()
        Assert.Equal(ProductLinks.LicenseOnlineUrl, ProductLinks.OnlineFor(ProductLinks.Url(ProductLinks.License, "en"), "en"))
        Assert.Equal(ProductLinks.NoticesOnlineUrl, ProductLinks.OnlineFor(ProductLinks.Url(ProductLinks.Notices, "ru"), "ru"))
        For Each code As String In Localization.Codes
            Dim help As String = ProductLinks.OnlineFor(ProductLinks.Url(ProductLinks.Help, code), code)
            Assert.StartsWith("https://", help)
            Assert.Equal(ProductLinks.HelpOnline(code), help)
        Next
        Assert.StartsWith("https://", ProductLinks.LicenseOnlineUrl)
        Assert.StartsWith("https://", ProductLinks.NoticesOnlineUrl)
    End Sub

    <Fact>
    Public Sub OnlineForLeavesAnyOtherAddressAloneAndAnUnknownBundledFileHasNoPage()
        Assert.Equal("https://example.org/a", ProductLinks.OnlineFor("https://example.org/a", "ru"))
        Assert.Equal("mailto:x@example.org", ProductLinks.OnlineFor("mailto:x@example.org", "en"))
        Assert.Equal("", ProductLinks.OnlineFor("", "en"))
        Assert.Equal("", ProductLinks.OnlineFor(ProductLinks.LocalScheme & "unknown.txt", "en"))
    End Sub

    ' --- AboutResources -----------------------------------------------------------------------------------

    ''' <summary>Runs <paramref name="body"/> with the process starts replaced by recorders and the search folder pointed at
    ''' <paramref name="folder"/>, and puts everything back.</summary>
    Private Shared Sub WithSeams(folder As String, body As Action(Of List(Of String), List(Of String)))
        Dim launched As New List(Of String)()
        Dim files As New List(Of String)()
        Dim oldLaunch As Action(Of String) = AboutResources.Launch
        Dim oldFile As Action(Of String) = AboutResources.LaunchFile
        Dim oldFrom As String = AboutResources.SearchFrom
        Try
            AboutResources.Launch = Sub(address) launched.Add(address)
            AboutResources.LaunchFile = Sub(path) files.Add(path)
            AboutResources.SearchFrom = folder
            body(launched, files)
        Finally
            AboutResources.Launch = oldLaunch
            AboutResources.LaunchFile = oldFile
            AboutResources.SearchFrom = oldFrom
        End Try
    End Sub

    Private Shared Function NewFolder() As String
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-about-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Return folder
    End Function

    <Fact>
    Public Sub ABundledFileBesideTheProgramOpensFromDiskAndStartsNoBrowser()
        Dim folder As String = NewFolder()
        Try
            File.WriteAllText(Path.Combine(folder, "LICENSE"), "MIT License")
            File.WriteAllText(Path.Combine(folder, "THIRD-PARTY-NOTICES.txt"), "notices")
            WithSeams(folder,
                Sub(launched, files)
                    Dim opened As String = AboutResources.Open(ProductLinks.Url(ProductLinks.License, "en"), "en")
                    Assert.Equal(Path.GetFullPath(Path.Combine(folder, "LICENSE")), opened)
                    AboutResources.Open(ProductLinks.Url(ProductLinks.Notices, "en"), "en")
                    Assert.Equal(2, files.Count)
                    Assert.EndsWith("THIRD-PARTY-NOTICES.txt", files(1))
                    Assert.Empty(launched)
                End Sub)
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub AMissingBundledFileFallsBackToItsWebPage()
        Dim folder As String = NewFolder()
        Try
            WithSeams(folder,
                Sub(launched, files)
                    Assert.Equal(ProductLinks.LicenseOnlineUrl, AboutResources.Open(ProductLinks.Url(ProductLinks.License, "en"), "en"))
                    Assert.Equal(ProductLinks.NoticesOnlineUrl, AboutResources.Open(ProductLinks.Url(ProductLinks.Notices, "ru"), "ru"))
                    Assert.Equal({ProductLinks.LicenseOnlineUrl, ProductLinks.NoticesOnlineUrl}, launched.ToArray())
                    Assert.Empty(files)
                End Sub)
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub ABundledFileThatCannotBeStartedFallsBackToItsWebPage()
        Dim folder As String = NewFolder()
        Try
            File.WriteAllText(Path.Combine(folder, "LICENSE"), "MIT License")
            WithSeams(folder,
                Sub(launched, files)
                    AboutResources.LaunchFile = Sub(path) Throw New InvalidOperationException("no editor")
                    Assert.Equal(ProductLinks.LicenseOnlineUrl, AboutResources.Open(ProductLinks.Url(ProductLinks.License, "en"), "en"))
                    Assert.Equal({ProductLinks.LicenseOnlineUrl}, launched.ToArray())
                End Sub)
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub AnyOtherAddressIsHandedToWindowsAsItIs()
        WithSeams(Path.GetTempPath(),
            Sub(launched, files)
                Assert.Equal("https://example.org/x", AboutResources.Open("https://example.org/x", "de"))
                Dim mail As String = ProductLinks.Url(ProductLinks.Contact, "en")
                Assert.Equal(mail, AboutResources.Open(mail, "en"))
                Assert.Equal({"https://example.org/x", mail}, launched.ToArray())
                Assert.Empty(files)
            End Sub)
    End Sub

    <Fact>
    Public Sub AHelpSurfaceThatIsNotShippedOpensTheTopicsPage()
        WithSeams(Path.GetTempPath(),
            Sub(launched, files)
                Dim opened As String = AboutResources.Open(ProductLinks.HelpScheme & "no-such-surface-zz", "ru")
                Assert.Equal(ProductLinks.HelpOnline("ru"), opened)
                Assert.Equal({ProductLinks.HelpOnline("ru")}, launched.ToArray())
            End Sub)
    End Sub

    <Fact>
    Public Sub NothingIsStartedForAnEmptyAddressOrAnUnknownBundledName()
        Dim folder As String = NewFolder()
        Try
            WithSeams(folder,
                Sub(launched, files)
                    Assert.Equal("", AboutResources.Open("", "en"))
                    Assert.Equal("", AboutResources.Open(ProductLinks.LocalScheme & "NO-SUCH-FILE.txt", "en"))
                    Assert.Empty(launched)
                    Assert.Empty(files)
                End Sub)
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub LocateAnswersEmptyForAMissingNameAndTheFullPathForAPresentOne()
        Dim folder As String = NewFolder()
        Try
            File.WriteAllText(Path.Combine(folder, "LICENSE"), "x")
            Assert.Equal("", AboutResources.Locate("", folder))
            Assert.Equal("", AboutResources.Locate("NO-SUCH-FILE.txt", folder))
            Assert.Equal(Path.GetFullPath(Path.Combine(folder, "LICENSE")), AboutResources.Locate("LICENSE", folder))
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub TheManualAddressIsTheWebPageTheReaderCanOpenByHand()
        Assert.Equal(ProductLinks.LicenseOnlineUrl, AboutResources.ManualAddress(ProductLinks.Url(ProductLinks.License, "en"), "en"))
        Assert.Equal(ProductLinks.HelpOnline("uk"), AboutResources.ManualAddress(ProductLinks.Url(ProductLinks.Help, "uk"), "uk"))
        Assert.Equal(ProductLinks.RepositoryUrl, AboutResources.ManualAddress(ProductLinks.Url(ProductLinks.Repository, "en"), "en"))
    End Sub

End Class
#End If
