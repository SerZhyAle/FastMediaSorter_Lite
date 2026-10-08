#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 177 section 3 items 3 and 5, the part of the guards that needs nothing of the exchange client's own types:
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
        Dim switchedOn As New List(Of String)
        Dim found As Integer = 0
        For Each f As String In Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.vb", SearchOption.AllDirectories)
            If f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse
               f.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For
            For Each m As Match In Regex.Matches(File.ReadAllText(f), "(?im)^\s*(Public|Friend)\s+Const\s+Offered\s+As\s+Boolean\s*=\s*(\w+)")
                found += 1
                If Not String.Equals(m.Groups(2).Value, "False", StringComparison.OrdinalIgnoreCase) then switchedOn.Add(Path.GetFileName(f) & ": " & m.Value.Trim())
            Next
        Next
        Assert.True(found >= 1, "the scan found no Offered constant at all - the pattern no longer matches the sources")
        Assert.True(switchedOn.Count = 0, "an offer switch is on without an evidence-bearing test change: " & String.Join("; ", switchedOn))
    End Sub

End Class
#End If
