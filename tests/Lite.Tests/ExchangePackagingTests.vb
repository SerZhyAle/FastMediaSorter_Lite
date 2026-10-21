#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The packaging of the Media Exchange Server (specification 133, ticket 141), read from the
''' files that ship: the packagers, release.yml, both Inno scripts, the MSIX builder and the
''' notices. The shipping gate is the point - nothing from the exchange module may reach a
''' RELEASED package before rung 2 freezes the wire - so the tests hold both halves of it: the
''' gate is shut, and with it shut nothing of the component is compiled or staged. Net10 only:
''' these read repository files, and the runtime they run under changes nothing.
''' </summary>
Public Class ExchangePackagingTests

    ' --- the gate ------------------------------------------------------------------------

    <Fact>
    Public Sub TheShippingGateIsShutInTheReleaseWorkflow()
        ' Owner rule (133 section 8): shut until rung 2 freezes the wire. Flipping it is a deliberate
        ' act - this assertion is changed in the same commit, never worked around.
        Dim yml As String = ReadText(".github", "workflows", "release.yml")
        Assert.Matches("(?m)^\s{0,2}INCLUDE_EXCHANGE:\s*'false'\s*(#.*)?$", yml)
        Assert.DoesNotMatch("(?m)^\s*INCLUDE_EXCHANGE:\s*'true'", yml)
    End Sub

    <Fact>
    Public Sub EveryPackagerHasTheGateSwitchOffByDefault()
        ' build.ps1 is not a packager of the release: it carries the exchange server by default (see below).
        For Each rel As String() In {
                New String() {"tools", "Build-Installer.ps1"},
                New String() {"tools", "Build-ServerInstaller.ps1"},
                New String() {"tools", "Build-OfflineRelease.ps1"}}
            Dim text As String = ReadText(rel)
            Assert.True(Regex.IsMatch(text, "\[switch\]\$IncludeExchange\b"),
                        $"{String.Join("/", rel)} has no [switch]$IncludeExchange (a switch is off unless given)")
        Next
    End Sub

    <Fact>
    Public Sub WithTheGateShutIsccGetsNoExchangeAndTheStageIsProvenEmptyOfIt()
        For Each name As String In {"Build-Installer.ps1", "Build-ServerInstaller.ps1", "Build-OfflineRelease.ps1"}
            Dim text As String = ReadText("tools", name)
            Assert.Contains("/DNoExchange", text)
            Assert.Contains("Stage-ExchangePayload.ps1", text)
            Assert.Contains("-IncludeExchange", text)
        Next
        ' The wildcard copies of bin\Release never carry the tree by luck, gate or no gate.
        Assert.Contains("exchange\*", ReadText("tools", "Build-Installer.ps1"))
        Assert.Contains("exchange\*", ReadText("tools", "Build-OfflineRelease.ps1"))
        Assert.Contains("exchange\*", ReadText(".github", "workflows", "release.yml"))
    End Sub

    <Fact>
    Public Sub TheServerViewerComponentNeverCarriesTheExchangeTree()
        ' The Server .iss installs viewer\* with the viewer component alone. A User stage built with -IncludeExchange holds
        ' exchange\, so without this exclusion ticking only "viewer" would write the exchange tree past its own component.
        Assert.Matches("(?m)^\$excludeDirs\s*=\s*@\([^)]*'exchange'", ReadText("tools", "Stage-ServerViewerPayload.ps1"))
    End Sub

    <Fact>
    Public Sub TheRootBuildIncludesTheExchangeServerUnlessToldNotTo()
        ' A local build carries the exchange server by default; -SkipExchange is the only way out, and a
        ' machine without a 64-bit Go stops rather than packaging setups without it. This is NOT the
        ' shipping gate - release.yml and Build-OfflineRelease.ps1 are held shut by the tests above.
        Dim text As String = ReadText("build.ps1")
        Assert.Contains("$IncludeExchange = -not $SkipExchange", text)
        Assert.DoesNotMatch("\[switch\]\$IncludeExchange\b", text)
        Assert.Contains("$installerArgs.IncludeExchange", text)
        Assert.Contains("$serverArgs.IncludeExchange", text)
        Assert.Contains("build without the exchange server with -SkipExchange", text)
    End Sub

    <Fact>
    Public Sub TheWorkflowGatesGoAndPinsTheActionToACommit()
        Dim yml As String = ReadText(".github", "workflows", "release.yml")
        Dim m As Match = Regex.Match(yml, "uses:\s*actions/setup-go@([^\s#]+)")
        Assert.True(m.Success, "release.yml has no actions/setup-go step")
        Assert.Matches("^[0-9a-f]{40}$", m.Groups(1).Value)   ' a commit SHA, never a tag (ticket 109 section 3)
        Assert.Contains("go-version-file: exchange-server/go.mod", yml)
        Assert.Contains("go test ./...", yml)
        Assert.Contains("exchange-server\build.ps1", yml)
        ' Every Go step is behind the gate.
        Assert.Equal(2, Regex.Matches(yml, "if:\s*env\.INCLUDE_EXCHANGE == 'true'").Count)
    End Sub

    ' --- the stage tree ------------------------------------------------------------------

    <Fact>
    Public Sub TheStagedTreeIsExactlyTheFiveFilesOfThePackageContract()
        Dim text As String = ReadText("tools", "Stage-ExchangePayload.ps1")
        For Each name As String In {"fms-exchange-server.exe", "enable-exchange-server.ps1", "install-exchange-service.ps1", "README.txt"}
            Assert.Contains(name, text)
        Next
        Assert.Contains(".sha256", text)
        Assert.Contains("throw", text)   ' a missing file stops the build; nothing is skipped quietly
    End Sub

    ' --- both installers -----------------------------------------------------------------

    <Fact>
    Public Sub TheCoreWildcardNeverInstallsTheExchangeTree()
        ' Trap 1 (R3): the exclusion is NOT governed by the gate - it must be on the core line itself,
        ' outside any #ifndef NoExchange, so the unchecked component can never be written by core.
        Dim lines As String() = ReadText("publishing", "installer", "FastMediaSorter.iss").Split(vbLf(0))
        Dim coreLine As String = lines.Single(Function(l) l.StartsWith("Source: ""{#SourceDir}\*""", StringComparison.Ordinal))
        Assert.Contains("exchange\*", coreLine)
        Assert.Contains("Components: core", coreLine)
    End Sub

    <Fact>
    Public Sub TheUserComponentHasNoTypesSoEvenFullLeavesItOff()
        Dim iss As String = ReadText("publishing", "installer", "FastMediaSorter.iss")
        Dim comp As String = ComponentLine(iss, "exchange")
        Assert.DoesNotContain("Types:", comp)           ' no Types: unchecked even in `full`, which silent winget installs take
        Assert.Contains("dontinheritcheck", comp)
        Assert.Contains("MinVersion: 10.0.14393", comp)
        AssertInsideNoExchangeGuard(iss, "Name: ""exchange""")
    End Sub

    <Fact>
    Public Sub TheServerComponentHasNoTypesAndTheServiceTaskIsUnchecked()
        Dim iss As String = ReadText("publishing", "installer", "FastMediaSorterServer.iss")
        Assert.DoesNotContain("Types:", ComponentLine(iss, "exchange"))
        Dim task As String = iss.Split(vbLf(0)).Single(Function(l) l.StartsWith("Name: ""exchangeservice""", StringComparison.Ordinal))
        Assert.Contains("Components: exchange", task)
        Assert.Contains("Flags: unchecked", task)
        AssertInsideNoExchangeGuard(iss, "Name: ""exchangeservice""")
        AssertInsideNoExchangeGuard(iss, "Name: ""exchange""")
    End Sub

    <Fact>
    Public Sub TheComponentFilesMessagesAndUninstallCodeCompileOutWithTheGateShut()
        For Each name As String In {"FastMediaSorter.iss", "FastMediaSorterServer.iss"}
            Dim iss As String = ReadText("publishing", "installer", name)
            AssertInsideNoExchangeGuard(iss, "Source: ""{#SourceDir}\exchange\*""")
            AssertInsideNoExchangeGuard(iss, "english.CompExchange=")
            AssertInsideNoExchangeGuard(iss, "russian.CompExchange=")
            AssertInsideNoExchangeGuard(iss, "ukrainian.CompExchange=")
            AssertInsideNoExchangeGuard(iss, "Type: filesandordirs; Name: ""{app}\exchange""")
            ' The component skips no missing source: a setup that claims it carries it.
            Dim src As String = iss.Split(vbLf(0)).Single(Function(l) l.StartsWith("Source: ""{#SourceDir}\exchange\*""", StringComparison.Ordinal))
            Assert.DoesNotContain("skipifsourcedoesntexist", src)
            ' The name comes from the define, never a literal in a message, and the stop mechanism is never AppMutex.
            Assert.Contains("#define ExchangeServerName ""Media Exchange Server""", iss)
            Assert.DoesNotMatch("(?mi)^AppMutex=.*Exchange", iss)
        Next
    End Sub

    <Fact>
    Public Sub UninstallRemovesTheRuleByProgramPathAndNeverByName()
        Dim user As String = ReadText("publishing", "installer", "FastMediaSorter.iss")
        Assert.Contains("RemoveExchangeFirewallRule;", user)
        Assert.Contains("name=all program=", user)
        Dim server As String = ReadText("publishing", "installer", "FastMediaSorterServer.iss")
        Assert.Contains("RemoveExchangeServiceAndRule;", server)
        Assert.Contains("'remove'", server)                 ' the service helper's remove verb; the data directory is kept
        Assert.DoesNotContain("DelTree", server.Substring(server.IndexOf("procedure RemoveExchangeServiceAndRule", StringComparison.Ordinal)))
    End Sub

    <Fact>
    Public Sub TheServiceIsRegisteredOnlyForTheTickedTaskOrRepairedWhenItExists()
        Dim server As String = ReadText("publishing", "installer", "FastMediaSorterServer.iss")
        Dim start As Integer = server.IndexOf("procedure ApplyExchangeService", StringComparison.Ordinal)
        Assert.True(start > 0)
        Dim body As String = server.Substring(start, server.IndexOf("procedure RemoveExchangeServiceAndRule", start, StringComparison.Ordinal) - start)
        Assert.Contains("RunExchangeHelper('repair'", body)
        Assert.Contains("WizardIsTaskSelected('exchangeservice')", body)
        Assert.Contains("RunExchangeHelper('install'", body)
        Assert.Contains("RunExchangeHelper('migrate-to-service'", body)
        ' install/migrate sit AFTER the task test: never on an upgrade where the task was not ticked.
        Assert.True(body.IndexOf("WizardIsTaskSelected", StringComparison.Ordinal) < body.IndexOf("RunExchangeHelper('install'", StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub TheStopScriptStopsOnlyACopyRunningFromTheInstallationAndNeverAServiceInstance()
        Dim text As String = ReadText("publishing", "installer", "stop-companion.ps1")
        Assert.Contains("fms-exchange-server", text)
        Assert.Contains("FastMediaSorterExchangeServer", text)
        Assert.Contains("'exchange\'", text)
        Assert.Contains("$exchangeServicePid", text)
    End Sub

    ' --- the Store package ---------------------------------------------------------------

    <Fact>
    Public Sub TheMsixStageNeverCarriesTheExchangeTree()
        Dim text As String = ReadText("publishing", "msix", "build-msix.ps1")
        ' Excluded by name from the bin\Release copy: the exchange tree, and (specification 249) the FileDO
        ' disk-hosting tree, which the Store package does not carry.
        Assert.Contains("@('exchange\', 'filedo\')", text)
        ' ..and proven on the stage after the copy, gate or no gate.
        Assert.Contains("Join-Path $Stage 'exchange'", text)
        Assert.Contains("Join-Path $Stage 'filedo'", text)
        Assert.Contains("fms-exchange-server*", text)
    End Sub

    ' --- notices -------------------------------------------------------------------------

    <Fact>
    Public Sub TheNoticesCreditTheModulesTheBinaryLinks()
        Dim text As String = ReadText("THIRD-PARTY-NOTICES.txt")
        Dim start As Integer = text.IndexOf("Media Exchange Server (fms-exchange-server.exe)", StringComparison.Ordinal)
        Assert.True(start > 0, "THIRD-PARTY-NOTICES.txt has no Media Exchange Server section")
        Dim finish As Integer = text.IndexOf("6. License texts", start, StringComparison.Ordinal)
        Assert.True(finish > start, "the section must come before the license texts")
        Dim section As String = text.Substring(start, finish - start)
        For Each credited As String In {"Go standard library", "golang.org/x/crypto", "golang.org/x/sys"}
            Assert.Contains(credited, section)
        Next
        ' It comes after the worker's section.
        Assert.True(start > text.IndexOf("Folder Share worker (fms-share-worker.exe)", StringComparison.Ordinal))
        ' Every module the notice names is one go.mod actually requires.
        Dim gomod As String = ReadText("exchange-server", "go.mod")
        Assert.Contains("golang.org/x/crypto", gomod)
        Assert.Contains("golang.org/x/sys", gomod)
    End Sub

    ' --- helpers -------------------------------------------------------------------------

    ''' <summary>The [Components] line of a component, found by its name.</summary>
    Private Shared Function ComponentLine(iss As String, name As String) As String
        Return iss.Split(vbLf(0)).Single(Function(l) l.StartsWith("Name: """ & name & """;", StringComparison.Ordinal)).TrimEnd(vbCr(0))
    End Function

    ''' <summary>
    ''' The line starting with <paramref name="prefix"/> sits between a '#ifndef NoExchange' and its
    ''' '#endif' - the guard that compiles it out when the shipping gate is shut.
    ''' </summary>
    Private Shared Sub AssertInsideNoExchangeGuard(iss As String, prefix As String)
        Dim lines As String() = iss.Split(vbLf(0)).Select(Function(l) l.TrimEnd(vbCr(0))).ToArray()
        Dim index As Integer = Array.FindIndex(lines, Function(l) l.StartsWith(prefix, StringComparison.Ordinal))
        Assert.True(index >= 0, $"no line starts with {prefix}")
        Dim depth As Integer = 0
        For i As Integer = 0 To index
            Dim t As String = lines(i).Trim()
            If t.StartsWith("#ifndef NoExchange", StringComparison.Ordinal) Then depth += 1
            If t.StartsWith("#endif", StringComparison.Ordinal) AndAlso depth > 0 Then depth -= 1
        Next
        Assert.True(depth > 0, $"{prefix} is not inside '#ifndef NoExchange'")
    End Sub

    Private Shared Function ReadText(ParamArray parts As String()) As String
        Return File.ReadAllText(Path.Combine({RepoRoot()}.Concat(parts).ToArray()))
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
