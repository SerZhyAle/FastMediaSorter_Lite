Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Holds the Share Manager's visible names to the one owner of them, docs/guides/PRODUCT_NAMING.md and
''' src/ProductNames.vb (ticket 111; the viewer's half is ProductNamesTests in the viewer's test project). The previous
''' rename left 33 files saying the old name because the name was copied into each of them.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ProductNamesTests

    ' The retired forms are assembled at run time so that this file does not itself carry a banned
    ' form for tools/Test-ProductNaming.ps1 to find.
    Private Const RetiredAdjective As String = "Li" & "te"
    Private Const PreviousFullName As String = "Fast Media Sorter " & "for Windows"
    Private Const PreviousShareManagerTitle As String = "Fast Media Sorter" & ": Share Manager"

    <Fact>
    Public Sub The_constants_are_the_exact_owner_decided_strings()
        Assert.Equal("Fast Media Sorter & Sharing for Windows", ProductNames.Full)
        Assert.Equal("Fast Media Sorter & Sharing", ProductNames.Compact)
        Assert.Equal("Share Manager - Fast Media Sorter & Sharing", ProductNames.ShareManager)
        Assert.Equal("Share Manager - Fast Media Sorter && Sharing", ProductNames.ShareManagerForLabel)
        Assert.Equal("Fast Media Sorter && Sharing", ProductNames.CompactForLabel)
    End Sub

    <Fact>
    Public Sub The_share_manager_name_fits_the_tray_tooltip_limit()
        ' NotifyIcon.Text holds 63 characters (PRODUCT_NAMING.md section 3, rule 3).
        Assert.True(ProductNames.ShareManager.Length <= 63,
                    "NotifyIcon.Text is limited to 63 characters; the name is " & ProductNames.ShareManager.Length)
    End Sub

    <Fact>
    Public Sub The_project_product_and_title_are_the_share_manager_name_escaped_for_xml()
        Dim project As String = File.ReadAllText(Path.Combine(CompanionSourceDir(), "FastMediaSorterCompanion.vbproj"))
        Assert.Equal(ProductNames.ShareManager, ProjectProperty(project, "Product"))
        Assert.Equal(ProductNames.ShareManager, ProjectProperty(project, "AssemblyTitle"))
        ' The raw text must carry the escaped form - a bare ampersand is not well-formed XML.
        Assert.True(project.IndexOf("<Product>Share Manager - Fast Media Sorter &amp; Sharing</Product>", StringComparison.Ordinal) >= 0,
                    "The project file must carry the XML-escaped name.")
    End Sub

    ''' <summary>
    ''' The window caption, the tray tooltip and the message-box captions all cite the constant; the
    ''' retired title must not be spelled in any code line of the program. The FindWindowW handshake
    ''' caption is a different, frozen string and stays as it is.
    ''' </summary>
    <Fact>
    Public Sub No_source_line_spells_a_retired_name()
        Dim scanned As Integer = 0
        Dim hits As New List(Of String)()
        For Each sourcePath In CompanionSources()
            scanned += 1
            Dim lineNo As Integer = 0
            For Each line In File.ReadAllLines(sourcePath)
                lineNo += 1
                If line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If line.IndexOf(PreviousShareManagerTitle, StringComparison.Ordinal) >= 0 OrElse
                   line.IndexOf(PreviousFullName, StringComparison.Ordinal) >= 0 Then
                    hits.Add(Path.GetFileName(sourcePath) & ":" & lineNo)
                End If
            Next
        Next
        Assert.True(scanned > 30, "Only " & scanned & " source files scanned - the filter is excluding too much.")
        Assert.True(hits.Count = 0, "A retired name is still spelled in code: " & String.Join(", ", hits.Take(20)))
    End Sub

    ''' <summary>The frozen handshake and wake strings are not part of the visible name.</summary>
    <Fact>
    Public Sub The_window_handshake_caption_is_not_the_visible_name()
        Assert.Equal("FastMediaSorterCompanionMessageWindow_{2f6a1c94}", Program.MessageWindowTitle)
        Assert.NotEqual(ProductNames.ShareManager, Program.MessageWindowTitle)
    End Sub

    ''' <summary>
    ''' Counts strings, not reads: every registered key and every translation, in all 13 columns, is
    ''' free of the banned forms, and none spells the Share Manager's visible name by hand (it comes
    ''' through a placeholder from ProductNames, in Latin in every language).
    ''' </summary>
    <Fact>
    Public Sub No_localization_string_carries_a_banned_form_of_the_name()
        Dim banned As New List(Of KeyValuePair(Of String, Regex)) From {
            New KeyValuePair(Of String, Regex)("the previous full name", New Regex(Regex.Escape(PreviousFullName), RegexOptions.CultureInvariant)),
            New KeyValuePair(Of String, Regex)("the retired adjective as a word", New Regex("\b" & RetiredAdjective.ToUpperInvariant() & "\b|\b" & RetiredAdjective & "\b", RegexOptions.CultureInvariant)),
            New KeyValuePair(Of String, Regex)("the retired Share Manager title", New Regex(Regex.Escape(PreviousShareManagerTitle) & "|Fast Media Sorter ?: Share Manager", RegexOptions.CultureInvariant)),
            New KeyValuePair(Of String, Regex)("the visible Share Manager name spelled by hand", New Regex(Regex.Escape(ProductNames.ShareManager), RegexOptions.CultureInvariant)),
            New KeyValuePair(Of String, Regex)("'and' instead of the ampersand", New Regex(" and Sharing\b|Sorter and Sharing", RegexOptions.CultureInvariant))
        }

        Dim checkedStrings As Integer = 0
        Dim offenders As New List(Of String)()
        For Each entry In Localization.All
            Dim candidates As New List(Of String)()
            candidates.Add(entry.Key)
            If entry.Value IsNot Nothing Then candidates.AddRange(entry.Value)
            For Each candidateText In candidates
                If candidateText Is Nothing Then Continue For
                checkedStrings += 1
                For Each rule In banned
                    If rule.Value.IsMatch(candidateText) Then offenders.Add(rule.Key & ": " & Shorten(candidateText))
                Next
            Next
        Next

        Assert.True(checkedStrings > 1000, "Only " & checkedStrings & " strings inspected - the tables did not all load.")
        Assert.True(offenders.Count = 0,
                    "Localization strings carry a banned form of the product name (" & offenders.Count & "):" & vbLf &
                    String.Join(vbLf, offenders.Take(30)))
    End Sub

    ''' <summary>
    ''' A source scan of the table files themselves, so a literal added to a table that Localization.All
    ''' does not enumerate (or one that failed to register) is still caught.
    ''' </summary>
    <Fact>
    Public Sub No_localization_table_file_carries_a_banned_form()
        Dim folder As String = Path.Combine(CompanionSourceDir(), "Localization")
        Dim files As String() = Directory.GetFiles(folder, "*.vb")
        Assert.True(files.Length >= 8, "Only " & files.Length & " table files found in " & folder)

        Dim bannedPatterns As String() = {
            Regex.Escape(PreviousFullName),
            Regex.Escape(PreviousShareManagerTitle),
            "\b" & RetiredAdjective.ToUpperInvariant() & "\b",
            "\b" & RetiredAdjective & "\b"}
        Dim offenders As New List(Of String)()
        For Each f In files
            Dim lineNo As Integer = 0
            For Each line In File.ReadAllLines(f)
                lineNo += 1
                For Each pattern In bannedPatterns
                    If Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant) Then
                        offenders.Add(Path.GetFileName(f) & ":" & lineNo)
                    End If
                Next
            Next
        Next
        Assert.True(offenders.Count = 0, "A table file carries a banned form: " & String.Join(", ", offenders.Take(20)))
    End Sub

    ' ---------------------------------------------------------------- helpers ----

    ''' <summary>The XML-decoded text of a simple property element of the project file.</summary>
    Private Shared Function ProjectProperty(project As String, name As String) As String
        Dim m As Match = Regex.Match(project, "<" & name & ">(?<v>[^<]*)</" & name & ">")
        Assert.True(m.Success, "The project file has no <" & name & "> element.")
        Return WebUtility.HtmlDecode(m.Groups("v").Value)
    End Function

    Private Shared Function CompanionSources() As IEnumerable(Of String)
        Return Directory.GetFiles(CompanionSourceDir(), "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf(Path.DirectorySeparatorChar & "Localization" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
    End Function

    Private Shared Function Shorten(text As String) As String
        Dim flat As String = text.Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(flat.Length <= 90, flat, flat.Substring(0, 90) & "..")
    End Function

    Private Shared Function CompanionSourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Program.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Could not locate src\FastMediaSorterCompanion from the test binary.")
    End Function

End Class
