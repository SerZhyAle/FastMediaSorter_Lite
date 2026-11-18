Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' BOOLEAN-OPTION 2.2: every native check box the two programs construct is classified in
''' <c>TestData/boolean-option-sites.tsv</c> (ticket L0325). A new <c>New CheckBox</c> site that is not listed fails here,
''' so a check box cannot enter the product without someone deciding whether it is an option row, a settings row or one of
''' the recorded exceptions. A source scan: no control is created.
''' </summary>
Public Class BooleanOptionInventoryTests

    Private Shared ReadOnly SitePattern As New Regex("New\s+(System\.Windows\.Forms\.)?CheckBox\b", RegexOptions.Compiled)
    Private Shared ReadOnly AssignPattern As New Regex("(?:Me\.)?(\w+)(?:\(\w+\))?\s*(?:As\s+\w+\s*)?=\s*New\s+(?:System\.Windows\.Forms\.)?CheckBox", RegexOptions.Compiled)
    Private Shared ReadOnly DimPattern As New Regex("(\w+)\s+As\s+New\s+(?:System\.Windows\.Forms\.)?CheckBox", RegexOptions.Compiled)
    Private Shared ReadOnly Classes As String() = {"option-row", "settings-row", "welcome-variant", "toolbar-toggle", "net48-only", "migration"}

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "tests", "Lite.Tests", "TestData", "boolean-option-sites.tsv")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("The repository root was not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>file|variable -> how many sites carry that key.</summary>
    Private Shared Function Count(keys As IEnumerable(Of String)) As Dictionary(Of String, Integer)
        Dim result As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For Each key As String In keys
            Dim n As Integer
            result.TryGetValue(key, n)
            result(key) = n + 1
        Next
        Return result
    End Function

    Private Shared Function ScanSites(root As String) As List(Of String)
        Dim keys As New List(Of String)()
        Dim src As String = Path.Combine(root, "src")
        For Each file As String In Directory.EnumerateFiles(src, "*.vb", SearchOption.AllDirectories)
            Dim rel As String = file.Substring(root.Length + 1).Replace("\"c, "/"c)
            If rel.Contains("/obj/") OrElse rel.Contains("/bin/") Then Continue For
            For Each line As String In IO.File.ReadLines(file)
                If Not SitePattern.IsMatch(line) Then Continue For
                Dim m As Match = AssignPattern.Match(line)
                If Not m.Success Then m = DimPattern.Match(line)
                keys.Add(rel & "|" & If(m.Success, m.Groups(1).Value, ""))
            Next
        Next
        Return keys
    End Function

    Private Shared Function ListedSites(root As String) As List(Of String())
        Dim path As String = IO.Path.Combine(root, "tests", "Lite.Tests", "TestData", "boolean-option-sites.tsv")
        Return IO.File.ReadAllLines(path).
            Where(Function(l) l.Length > 0 AndAlso Not l.StartsWith("#", StringComparison.Ordinal)).
            Select(Function(l) l.Split(ControlChars.Tab)).ToList()
    End Function

    <Fact>
    Public Sub Every_native_check_box_site_is_classified()
        Dim root As String = RepoRoot()
        Dim found As Dictionary(Of String, Integer) = Count(ScanSites(root))
        Dim listed As Dictionary(Of String, Integer) = Count(ListedSites(root).Select(Function(p) p(0) & "|" & p(1)))
        Dim missing As New List(Of String)()
        For Each pair As KeyValuePair(Of String, Integer) In found
            Dim n As Integer
            listed.TryGetValue(pair.Key, n)
            If pair.Value > n Then missing.Add(pair.Key & " (" & pair.Value & " sites, " & n & " classified)")
        Next
        Assert.True(missing.Count = 0, "Unclassified CheckBox sites (BOOLEAN-OPTION; add each to TestData/boolean-option-sites.tsv " &
                    "with its class, and present a preference as an option row): " & String.Join("; ", missing))
    End Sub

    <Fact>
    Public Sub Every_listed_site_still_exists_and_names_a_known_class()
        Dim root As String = RepoRoot()
        Dim found As Dictionary(Of String, Integer) = Count(ScanSites(root))
        Dim rows As List(Of String()) = ListedSites(root)
        For Each p As String() In rows
            Assert.True(p.Length = 3, "A line of boolean-option-sites.tsv is not file, variable, class: " & String.Join("|", p))
            Assert.Contains(p(2), Classes)
        Next
        Dim listed As Dictionary(Of String, Integer) = Count(rows.Select(Function(p) p(0) & "|" & p(1)))
        For Each pair As KeyValuePair(Of String, Integer) In listed
            Dim n As Integer
            found.TryGetValue(pair.Key, n)
            Assert.True(n >= pair.Value, "boolean-option-sites.tsv lists " & pair.Value & " site(s) of " & pair.Key & ", the source has " & n & " - remove the stale line.")
        Next
    End Sub
End Class
