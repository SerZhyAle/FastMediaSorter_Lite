#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' INSTALL-TRUST, read from the files that ship (ticket 037 section 5). The page is a render
''' target of tools/trust-copy.json, so the rules are held on that source; whether the
''' committed HTML still matches it is Build-SitePages -Check, a release-class check.
''' Net10 only: these read repository files, and the runtime they run under changes nothing.
''' </summary>
Public Class TrustPageContractTests

    ' --- rule 1 and the parity of the thirteen locales ----------------------------------

    <Fact>
    Public Sub EveryLocaleHasTheEnglishStructure()
        Using doc As JsonDocument = TrustCopy()
            Dim en As JsonElement = doc.RootElement.GetProperty("en")
            Dim shape As Integer() = SectionShape(en)
            For Each loc As JsonProperty In Locales(doc)
                Assert.True(shape.SequenceEqual(SectionShape(loc.Value)),
                            $"{loc.Name}: sections/items {String.Join(",", SectionShape(loc.Value))} differ from English {String.Join(",", shape)}")
                For Each key As String In {"title", "desc", "intro", "note", "origins", "backLabel", "dlLabel", "privacyLabel"}
                    Assert.False(String.IsNullOrWhiteSpace(loc.Value.GetProperty(key).GetString()), $"{loc.Name}.{key} is empty")
                Next
                For Each section As JsonElement In loc.Value.GetProperty("sections").EnumerateArray()
                    Assert.False(String.IsNullOrWhiteSpace(section.GetProperty("h").GetString()), $"{loc.Name}: a section heading is empty")
                    Assert.False(String.IsNullOrWhiteSpace(section.GetProperty("p").GetString()), $"{loc.Name}: a section lead is empty")
                    For Each item As JsonElement In section.GetProperty("items").EnumerateArray()
                        Assert.False(String.IsNullOrWhiteSpace(item.GetString()), $"{loc.Name}: an item is empty")
                    Next
                Next
            Next
        End Using
    End Sub

    <Fact>
    Public Sub TheLocalesAreTheSitesLanguages()
        ' The renderer takes its language list from site-copy.json; a locale missing here is
        ' a page that would be rendered from nothing.
        Using trust As JsonDocument = TrustCopy(), site As JsonDocument = ReadJson("tools", "site-copy.json")
            Dim trustCodes As String() = Locales(trust).Select(Function(p) p.Name).OrderBy(Function(c) c).ToArray()
            Dim siteCodes As String() = site.RootElement.EnumerateObject().
                Where(Function(p) Not p.Name.StartsWith("_", StringComparison.Ordinal)).
                Select(Function(p) p.Name).OrderBy(Function(c) c).ToArray()
            Assert.Equal(siteCodes, trustCodes)
            Assert.Equal(13, trustCodes.Length)
        End Using
    End Sub

    <Fact>
    Public Sub EnglishKeepsRuleOnesOrder()
        ' What the warning is, why it appears, exactly what to click - and what the program
        ' never does LAST. The section between the third and the last answers rule 5.
        Using doc As JsonDocument = TrustCopy()
            Dim headings As String() = doc.RootElement.GetProperty("en").GetProperty("sections").
                EnumerateArray().Select(Function(s) s.GetProperty("h").GetString()).ToArray()
            Assert.Equal("What the warning is", headings(0))
            Assert.Equal("Why it appears", headings(1))
            Assert.Equal("Exactly what to click", headings(2))
            Assert.Contains("administrator rights", headings(3))
            Assert.Equal("What the program never does", headings(headings.Length - 1))
        End Using
    End Sub

    ' --- rule 4 -------------------------------------------------------------------------

    <Fact>
    Public Sub EnglishNeverTellsTheUserToWeakenProtection()
        ' The only permitted mention of switching a protection off is the negated one
        ' ("Never switch SmartScreen off and never disable your antivirus"). Other locales
        ' are held by the structure test and a human read: a thirteen-language phrase list
        ' would cost more in false positives than it catches.
        Dim forbidden As Regex() = {
            New Regex("\b(turn|switch)\s+(off\s+)?(windows\s+)?(smartscreen|defender|your antivirus|the antivirus|real-time protection)(\s+off)?\b", RegexOptions.IgnoreCase),
            New Regex("\bdisabl(e|ing)\s+(windows\s+)?(smartscreen|defender|your antivirus|the antivirus|real-time protection)\b", RegexOptions.IgnoreCase),
            New Regex("\brun\s+(it\s+|the\s+\w+\s+)?as\s+(an\s+)?administrator\b", RegexOptions.IgnoreCase)}
        Using doc As JsonDocument = TrustCopy()
            For Each text As String In AllStrings(doc.RootElement.GetProperty("en"))
                For Each rx As Regex In forbidden
                    For Each m As Match In rx.Matches(text)
                        Dim before As String = text.Substring(0, m.Index).TrimEnd()
                        Assert.True(before.EndsWith("never", StringComparison.OrdinalIgnoreCase),
                                    $"English trust copy instructs weakening a protection: ""{m.Value}"" in: {text}")
                    Next
                Next
            Next
        End Using
    End Sub

    <Fact>
    Public Sub NoSurfaceTellsTheUserToUnblockAFile()
        ' INSTALL-TRUST 1.2 item K: the per-file "Unblock" checkbox of a download's Properties is not a permitted
        ' instruction - rule 4's list is closed (More info -> Run anyway, or an exception for one install folder),
        ' because "Unblock" removes the mark of the web that SmartScreen keys on. The thirteen locales of the trust
        ' copy and every surface that tells a user what to click are searched for the checkbox's names and for the
        ' mark itself. (Russian "разблокируется", an encrypted disk unlocking, is not an instruction and does not
        ' match: only the infinitives that name the action do.)
        Dim unblock As New Regex(
            "unblock|zone\.identifier|mark[\s-]of[\s-]the[\s-]web|разблокировать|розблокувати|desbloquear|débloquer|" &
            "blockierung aufheben|entsperren|sbloccare|解除阻止|解除锁定|取消阻止|إلغاء الحظر|إلغاء حظر|अनब्लॉक|আনব্লক|ان بلاک",
            RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
        Dim root As String = RepoRoot()
        Dim files As New List(Of String)()
        files.AddRange(Directory.EnumerateFiles(root, "*.html", SearchOption.TopDirectoryOnly))
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "lang"), "*.html", SearchOption.AllDirectories))
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.html", SearchOption.TopDirectoryOnly))
        files.AddRange(Directory.EnumerateFiles(root, "README*.md", SearchOption.TopDirectoryOnly))
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "publishing", "winget"), "*.yaml", SearchOption.AllDirectories))
        For Each parts As String() In {
                New String() {"tools", "trust-copy.json"}, New String() {"tools", "site-copy.json"},
                New String() {"publishing", "installer", "FastMediaSorter.iss"},
                New String() {"publishing", "installer", "FastMediaSorterServer.iss"},
                New String() {"docs", "guides", "STORE_PUBLISHING.md"},
                New String() {".github", "workflows", "release.yml"}}
            files.Add(Path.Combine({root}.Concat(parts).ToArray()))
        Next
        Assert.True(files.Count > 40, $"the surface list found only {files.Count} files - a path moved")
        For Each surface As String In files
            Assert.True(File.Exists(surface), $"{surface} is listed as a trust surface and is missing")
            Dim m As Match = unblock.Match(File.ReadAllText(surface))
            Assert.False(m.Success, $"{Path.GetRelativePath(root, surface)} mentions ""{m.Value}"" - INSTALL-TRUST rule 4 permits no ""Unblock"" instruction")
        Next
    End Sub

    ' --- rule 6 -------------------------------------------------------------------------

    <Fact>
    Public Sub TheThreeRuleSixTextsNameTheSameNetworkContacts()
        ' The page's never-does list with its note, the privacy policy and the Store privacy
        ' block must name the same third-party and local-network contacts - a contact one of
        ' them leaves out is the "implied away" call rule 6 forbids.
        Dim contacts As String() = {"check-host.net", "UPnP", "NAT-PMP", "mDNS", "FFmpeg", "Ollama",
                                    "doc-html-translate", "Send the logs to the author"}
        Dim page As String
        Using doc As JsonDocument = TrustCopy()
            Dim en As JsonElement = doc.RootElement.GetProperty("en")
            Dim sections As JsonElement() = en.GetProperty("sections").EnumerateArray().ToArray()
            page = String.Join(" ", AllStrings(sections(sections.Length - 1))) & " " & en.GetProperty("note").GetString()
        End Using
        Dim privacy As String = ReadText("docs", "privacy.html")
        Dim store As String = StorePrivacyBlock()
        For Each contact As String In contacts
            Assert.True(page.Contains(contact, StringComparison.Ordinal), $"trust.html (never-does + note) does not name {contact}")
            Assert.True(privacy.Contains(contact, StringComparison.Ordinal), $"docs/privacy.html does not name {contact}")
            Assert.True(store.Contains(contact, StringComparison.Ordinal), $"the Store privacy block does not name {contact}")
        Next
    End Sub

    ' --- SITE-EXPERIENCE rule 14 (ticket 153) -------------------------------------------

    <Fact>
    Public Sub EveryDeclaredSiteOriginIsNamedInEveryTrustLocaleAndInEachPrivacyBlock()
        ' tools/Test-SiteOrigins.ps1 holds the pages against tools/site-origins.json; this holds the
        ' two disclosures: each locale of the trust copy and each language block of the privacy page
        ' names every declared host, so a host added to the declaration cannot go undisclosed.
        Dim hosts As New List(Of String)()
        Using decl As JsonDocument = ReadJson("tools", "site-origins.json")
            For Each o As JsonElement In decl.RootElement.GetProperty("origins").EnumerateArray()
                hosts.Add(o.GetProperty("host").GetString())
            Next
        End Using
        Assert.NotEmpty(hosts)
        Using doc As JsonDocument = TrustCopy()
            For Each loc As JsonProperty In Locales(doc)
                Dim text As String = loc.Value.GetProperty("origins").GetString()
                For Each host As String In hosts
                    Assert.True(text.Contains(host, StringComparison.Ordinal), $"trust-copy.json {loc.Name}.origins does not name {host}")
                Next
            Next
        End Using
        Dim privacy As String = ReadText("docs", "privacy.html")
        For Each host As String In hosts
            Assert.True(Regex.Matches(privacy, Regex.Escape(host)).Count >= 3,
                        $"docs/privacy.html names {host} fewer than three times (one per language block: en, ru, ua)")
        Next
    End Sub
    ' --- rule 7 -------------------------------------------------------------------------

    <Fact>
    Public Sub EveryDownloadSurfaceLinksThePage()
        Dim surfaces As New List(Of String()) From {
            {New String() {"index.html"}},
            {New String() {"server.html"}},
            {New String() {"README.md"}},
            {New String() {"README_RU.md"}},
            {New String() {"README_UK.md"}},
            {New String() {".github", "workflows", "release.yml"}},
            {New String() {"publishing", "installer", "FastMediaSorter.iss"}}}
        Using doc As JsonDocument = TrustCopy()
            For Each loc As JsonProperty In Locales(doc)
                If loc.Name <> "en" Then surfaces.Add(New String() {"lang", loc.Name, "index.html"})
            Next
        End Using
        For Each parts As String() In surfaces
            Assert.True(ReadText(parts).Contains("trust.html", StringComparison.Ordinal),
                        $"{String.Join("/", parts)} does not link the install-trust page")
        Next
        ' winget has no inline prose: the page rides as a Documentations entry, per package.
        For Each manifest As String() In {
                New String() {"publishing", "winget", "SerZhyAle.FastMediaSorter.locale.en-US.yaml"},
                New String() {"publishing", "winget", "server", "SerZhyAle.FastMediaSorter.Server.locale.en-US.yaml"}}
            Assert.Contains("DocumentUrl: " & SitePages.TrustPageUrl("en"), ReadText(manifest))
        Next
    End Sub

    <Fact>
    Public Sub EveryUiLanguageLinksAPageThatExists()
        ' The viewer's About link and the Share Manager's elevation dialogs build the URL from
        ' the active UI language; each must land on a page this repository publishes.
        Dim root As String = RepoRoot()
        For Each code As String In Localization.Codes
            Dim url As String = SitePages.TrustPageUrl(code)
            Assert.StartsWith(SitePages.SiteRoot, url)
            Dim relative As String = url.Substring(SitePages.SiteRoot.Length).Replace("/"c, Path.DirectorySeparatorChar)
            Assert.True(File.Exists(Path.Combine(root, relative)), $"{code}: {url} has no page at {relative}")
        Next
        Assert.Equal(SitePages.SiteRoot & "trust.html", SitePages.TrustPageUrl(""))
        Assert.True(File.Exists(Path.Combine(root, SitePages.PrivacyPageUrl.Substring(SitePages.SiteRoot.Length).Replace("/"c, Path.DirectorySeparatorChar))),
                    $"{SitePages.PrivacyPageUrl} has no page in the repository")
    End Sub

    ' --- helpers ------------------------------------------------------------------------

    Private Shared Function TrustCopy() As JsonDocument
        Return ReadJson("tools", "trust-copy.json")
    End Function

    Private Shared Function ReadJson(ParamArray parts As String()) As JsonDocument
        Return JsonDocument.Parse(ReadText(parts))
    End Function

    Private Shared Function ReadText(ParamArray parts As String()) As String
        Return File.ReadAllText(Path.Combine({RepoRoot()}.Concat(parts).ToArray()))
    End Function

    Private Shared Function Locales(doc As JsonDocument) As IEnumerable(Of JsonProperty)
        Return doc.RootElement.EnumerateObject().Where(Function(p) Not p.Name.StartsWith("_", StringComparison.Ordinal)).ToArray()
    End Function

    Private Shared Function SectionShape(locale As JsonElement) As Integer()
        Return locale.GetProperty("sections").EnumerateArray().
            Select(Function(s) s.GetProperty("items").GetArrayLength()).ToArray()
    End Function

    Private Shared Iterator Function AllStrings(e As JsonElement) As IEnumerable(Of String)
        Select Case e.ValueKind
            Case JsonValueKind.String
                Yield e.GetString()
            Case JsonValueKind.Array
                For Each child As JsonElement In e.EnumerateArray()
                    For Each s As String In AllStrings(child)
                        Yield s
                    Next
                Next
            Case JsonValueKind.Object
                For Each child As JsonProperty In e.EnumerateObject()
                    For Each s As String In AllStrings(child.Value)
                        Yield s
                    Next
                Next
        End Select
    End Function

    ''' <summary>The Store privacy block of STORE_PUBLISHING.md, from its heading to the next rule.</summary>
    Private Shared Function StorePrivacyBlock() As String
        Dim text As String = ReadText("docs", "guides", "STORE_PUBLISHING.md")
        Dim start As Integer = text.IndexOf("### Privacy policy", StringComparison.Ordinal)
        Assert.True(start >= 0, "STORE_PUBLISHING.md has no privacy block")
        Dim finish As Integer = text.IndexOf(vbLf & "---", start, StringComparison.Ordinal)
        Return If(finish > start, text.Substring(start, finish - start), text.Substring(start))
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
