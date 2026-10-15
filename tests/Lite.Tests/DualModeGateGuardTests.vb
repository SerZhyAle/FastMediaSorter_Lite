#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 204 section 3 items 3 and 5, the part of the guards that needs nothing of the exchange client's own types:
''' it reads the files that decide what ships and what compiles. The fallback viewer (net48, x86) is OUTSIDE the
''' exchange program - no exchange surface, configured or not - and the offer switches stay shut until a rung opens
''' them on evidence. (That release.yml's INCLUDE_EXCHANGE is 'false' is held by <c>ExchangePackagingTests</c>.)
'''
''' What is NOT here, and why: the unit guard "no account means no agent object, no pipe, no HttpClient or Socket made
''' by an exchange type" and the guard on <c>ExchangeFeatures.Offered</c> need the types of ticket 180 (the agent, the
''' counting socket factory, the Features class); each lands as a case in the step that creates its type. The scan of
''' <c>Offered</c> below already covers that class the moment its file exists.
''' </summary>
Public Class DualModeGateGuardTests

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    <Fact>
    Public Sub The_x86_fallback_project_lists_no_exchange_file()
        Dim text As String = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FastMediaSorter.vbproj"))
        Dim hits As List(Of String) = Regex.Matches(text, "<(Compile|None|Content|EmbeddedResource)\s+Include=""[^""]*""", RegexOptions.IgnoreCase).
            Cast(Of Match)().Select(Function(m) m.Value).
            Where(Function(v) v.IndexOf("Exchange", StringComparison.OrdinalIgnoreCase) >= 0).ToList()
        Assert.True(hits.Count = 0,
                    "src\FastMediaSorter.vbproj (net48 x86, outside the exchange program) lists an exchange item: " & String.Join("; ", hits))
        ' A whole-folder include would pick src\Exchange up without naming it.
        Assert.DoesNotMatch("Include=""(\.\.\\)?\*\*|Include=""Exchange\\\*", text)
    End Sub

    <Fact>
    Public Sub Every_exchange_client_source_is_whole_file_modern_only()
        ' Belt and braces behind the project list: even a hand-added Compile entry cannot put exchange code into the
        ' net48 exe, because the file compiles to nothing there.
        Dim dir As String = Path.Combine(RepoRoot(), "src", "Exchange")
        If Not Directory.Exists(dir) Then Return
        For Each f As String In Directory.GetFiles(dir, "*.vb", SearchOption.AllDirectories)
            Dim lines As List(Of String) = File.ReadAllLines(f).Where(Function(l) l.Trim().Length > 0).ToList()
            Assert.True(lines.Count >= 2, f & " is empty")
            Assert.Equal("#If Not NETFRAMEWORK Then", lines(0).Trim())
            Assert.Equal("#End If", lines(lines.Count - 1).Trim())
        Next
    End Sub

    <Fact>
    Public Sub No_offer_switch_in_the_sources_is_on()
        ' Every compiled-in "Offered" constant (the broadcast page's today; the exchange's Features class once it
        ' exists) is False. Opening one is a gate opening: it needs the rung's evidence and changes THIS test in the
        ' same commit, with the registry note that cites the run of the fixture (177 T-5).
        '
        ' OPEN GATE (196 phase 1, 2026-10-12): BroadcastPage.Offered is True in the SAME change that
        ' re-vendored a worker serving the five broadcast verbs (payload\companion\fms-share-worker.exe,
        ' sha256 08a09e867f1455dfc08cc1c54660740e025ba3c22fb25ed710c97551a1e493a9 - the verbs and
        ' broadcastSupported verified in the image). The evidence rungs: internal/httpgw + internal/service
        ' suites green in fms_companion (range/partial content, method refusal, unknown/expired/revoked
        ' token, no traversal, snapshot immutability, limits, the five IPC verbs), and the same-change
        ' host guard BroadcastWorkerCapabilityTests holds the page and the image together.
        '
        ' OPEN GATE (223 T-3, 2026-10-14): ExchangeFeatures.Offered is True on the owner's word - the exchange CLIENTS on every
        ' install, ahead of the joint rung 2 run. docs\specifications\exchange-gate-evidence.md records the decision and what was
        ' reached; ExchangeClientGuardTests holds the constant and that file together. The server component stays gated (141).
        Dim openGates As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"BroadcastPage.vb", "196 phase 1: worker half built and re-vendored 2026-10-12; BroadcastWorkerCapabilityTests holds page and image together"},
            {"ExchangeFeatures.vb", "223 T-3: owner's word 2026-10-14; exchange-gate-evidence.md, held by ExchangeClientGuardTests"},
            {"LiveBroadcastFeatures.vb", "199: owner's word 2026-10-15; live-broadcast-gate-evidence.md, held by LiveBroadcastGateGuardTests"}
        }
        Dim switchedOn As New List(Of String)
        Dim found As Integer = 0
        For Each f As String In Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.vb", SearchOption.AllDirectories)
            If f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse
               f.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For
            For Each m As Match In Regex.Matches(File.ReadAllText(f), "(?im)^\s*(Public|Friend)\s+Const\s+Offered\s+As\s+Boolean\s*=\s*(\w+)")
                found += 1
                Dim evidence As String = Nothing
                If String.Equals(m.Groups(2).Value, "True", StringComparison.OrdinalIgnoreCase) AndAlso
                   openGates.TryGetValue(Path.GetFileName(f), evidence) Then Continue For
                If Not String.Equals(m.Groups(2).Value, "False", StringComparison.OrdinalIgnoreCase) Then
                    switchedOn.Add(Path.GetFileName(f) & ": " & m.Value.Trim())
                End If
            Next
        Next
        Assert.True(found >= 1, "the scan found no Offered constant at all - the pattern no longer matches the sources")
        Assert.True(switchedOn.Count = 0, "an offer switch is on without an evidence-bearing test change: " & String.Join("; ", switchedOn))
        ' A stale entry (its constant went back to False) must not linger in the table either.
        For Each gate As KeyValuePair(Of String, String) In openGates
            Dim stillOn As Boolean = False
            For Each f As String In Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.vb", SearchOption.AllDirectories)
                If f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse
                   f.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For
                If Not String.Equals(Path.GetFileName(f), gate.Key, StringComparison.OrdinalIgnoreCase) Then Continue For
                stillOn = stillOn OrElse File.ReadAllText(f).IndexOf("Const Offered As Boolean = True", StringComparison.OrdinalIgnoreCase) >= 0
            Next
            Assert.True(stillOn, "the open-gates table lists " & gate.Key & " but its Offered constant is False again - remove the stale entry")
        Next
    End Sub

End Class
#End If
