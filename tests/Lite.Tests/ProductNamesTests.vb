Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Holds the product's visible names to the one owner of them, docs/guides/PRODUCT_NAMING.md and
''' src/ProductNames.vb (ticket 111). The previous rename left 33 files saying the old name because the
''' name was copied into each of them; these tests make that drift a failing build instead of a review.
'''
''' Runs on BOTH legs (net48 + net10): the sources are linked, the string tables are shared, and only
''' APIs that exist on both runtimes are used.
''' </summary>
Public Class ProductNamesTests

    ' The retired forms are assembled at run time so that this file does not itself carry a banned
    ' form for tools/Test-ProductNaming.ps1 to find.
    Private Const RetiredAdjective As String = "Li" & "te"
    Private Const PreviousFullName As String = "Fast Media Sorter " & "for Windows"

    ' ------------------------------------------------------------ the constants ----

    <Fact>
    Public Sub The_constants_are_the_exact_owner_decided_strings()
        Assert.Equal("Fast Media Sorter & Sharing for Windows", ProductNames.Full)
        Assert.Equal("Fast Media Sorter & Sharing", ProductNames.Compact)
        Assert.Equal("Fast Media Sorter", ProductNames.ShortLegacy)
        Assert.Equal("FMS_W", ProductNames.AliasW)
        Assert.Equal("FMS_A", ProductNames.AliasA)
        Assert.Equal("SZA", ProductNames.Author)
        Assert.Equal("Fast Media Sorter & Sharing for Windows by SZA", ProductNames.WindowTitle)
        Assert.Equal("Share Manager - Fast Media Sorter & Sharing", ProductNames.ShareManager)
        Assert.Equal("Fast Media Sorter & Sharing: Folder Share Server", ProductNames.ServerEdition)
        Assert.Equal("Media Exchange Server", ProductNames.ExchangeServer)
    End Sub

    <Fact>
    Public Sub The_label_forms_are_the_plain_forms_with_the_ampersand_doubled()
        Assert.Equal(ProductNames.Full.Replace("&", "&&"), ProductNames.FullForLabel)
        Assert.Equal(ProductNames.Compact.Replace("&", "&&"), ProductNames.CompactForLabel)
        Assert.Equal(ProductNames.ShareManager.Replace("&", "&&"), ProductNames.ShareManagerForLabel)
    End Sub

    <Fact>
    Public Sub The_names_are_one_Latin_run_with_no_retired_adjective_and_no_and_word()
        Dim all As String() = {ProductNames.Full, ProductNames.Compact, ProductNames.WindowTitle,
                               ProductNames.ShareManager, ProductNames.ServerEdition, ProductNames.ExchangeServer}
        For Each name In all
            Assert.True(name.IndexOf(RetiredAdjective, StringComparison.OrdinalIgnoreCase) < 0, "The name must not carry the retired adjective: " & name)
            Assert.True(name.IndexOf(" and Sharing", StringComparison.Ordinal) < 0, "The ampersand is part of the name: " & name)
            Assert.True(name.All(Function(c) AscW(c) < 128), "The name must be written in Latin: " & name)
        Next
    End Sub

    ' ----------------------------------------------- the places that spell it out ----

    <Fact>
    Public Sub The_main_window_title_is_the_window_title_constant()
        Dim designer = ReadSource("src", "Main_Form.Designer.vb")
        Dim m = Regex.Match(designer, "Me\.Text\s*=\s*""(?<t>(?:[^""]|"""")*)""")
        Assert.True(m.Success, "Main_Form.Designer.vb has no literal Me.Text.")
        Assert.Equal(ProductNames.WindowTitle, m.Groups("t").Value.Replace("""""", """"))
    End Sub

    <Fact>
    Public Sub The_net48_assembly_properties_are_the_full_name()
        Dim info = ReadSource("src", "My Project", "AssemblyInfo.vb")
        Assert.Equal(ProductNames.Full, AssemblyAttribute(info, "AssemblyTitle"))
        Assert.Equal(ProductNames.Full, AssemblyAttribute(info, "AssemblyProduct"))
    End Sub

    <Fact>
    Public Sub The_modern_project_properties_are_the_full_name_escaped_for_xml()
        Dim project = ReadSource("src", "Modern", "FastMediaSorter.Modern.vbproj")
        Assert.Equal(ProductNames.Full, ProjectProperty(project, "Product"))
        Assert.Equal(ProductNames.Full, ProjectProperty(project, "AssemblyTitle"))
        ' The raw text must carry the escaped form - a bare ampersand is not well-formed XML.
        Assert.True(project.IndexOf("<Product>Fast Media Sorter &amp; Sharing for Windows</Product>", StringComparison.Ordinal) >= 0, "The project file must carry the XML-escaped name.")
    End Sub

    ''' <summary>
    ''' The previous full name must not survive in the code of the viewers: every place that shows the
    ''' name cites ProductNames. Comments are history and are not scanned; the Share Manager's own
    ''' folder has its own tests.
    ''' </summary>
    <Fact>
    Public Sub No_viewer_source_line_spells_the_previous_full_name()
        Dim srcDir = System.IO.Path.Combine(RepoRoot(), "src")
        Dim scanned = 0
        Dim hits As New List(Of String)()
        For Each sourcePath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If InExcludedFolder(sourcePath) Then Continue For
            scanned += 1
            Dim lineNo = 0
            For Each line In File.ReadAllLines(sourcePath)
                lineNo += 1
                If line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If line.IndexOf(PreviousFullName, StringComparison.Ordinal) >= 0 Then
                    hits.Add(System.IO.Path.GetFileName(sourcePath) & ":" & lineNo)
                End If
            Next
        Next
        Assert.True(scanned > 40, "Only " & scanned & " source files scanned - the filter is excluding too much.")
        Assert.True(hits.Count = 0, "The previous full name is still spelled in code: " & String.Join(", ", hits.Take(20)))
    End Sub

    ' ------------------------------------------------------- the string tables ----

    ''' <summary>
    ''' Counts strings, not reads (the LocalizationCoverageTests style): every registered key and every
    ''' translation, in all 13 columns, must be free of the banned forms.
    ''' </summary>
    <Fact>
    Public Sub No_localization_string_carries_a_banned_form_of_the_name()
        Dim banned As New List(Of KeyValuePair(Of String, Regex)) From {
            New KeyValuePair(Of String, Regex)("the previous full name", New Regex(Regex.Escape(PreviousFullName), RegexOptions.CultureInvariant)),
            New KeyValuePair(Of String, Regex)("the retired adjective as a word", New Regex("\b" & RetiredAdjective.ToUpperInvariant() & "\b|\b" & RetiredAdjective & "\b", RegexOptions.CultureInvariant)),
            New KeyValuePair(Of String, Regex)("'and' instead of the ampersand", New Regex(" and Sharing\b|Sorter and Sharing", RegexOptions.CultureInvariant)),
            New KeyValuePair(Of String, Regex)("the retired Share Manager title", New Regex("Fast Media Sorter ?: Share Manager", RegexOptions.CultureInvariant))
        }

        Dim checkedStrings = 0
        Dim offenders As New List(Of String)()
        For Each entry In Localization.All
            Dim candidates As New List(Of String)()
            candidates.Add(entry.Key)
            If entry.Value IsNot Nothing Then candidates.AddRange(entry.Value)
            For Each candidateText In candidates
                If candidateText Is Nothing Then Continue For
                checkedStrings += 1
                For Each rule In banned
                    If rule.Value.IsMatch(candidateText) Then
                        offenders.Add(rule.Key & ": " & Shorten(candidateText))
                    End If
                Next
            Next
        Next

        Assert.True(checkedStrings > 1000, "Only " & checkedStrings & " strings inspected - the tables did not all load.")
        Assert.True(offenders.Count = 0,
                    "Localization strings carry a banned form of the product name (" & offenders.Count & "):" & vbLf &
                    String.Join(vbLf, offenders.Take(30)))
    End Sub

    ''' <summary>
    ''' The Latin name is not translated: wherever a column mentions the Share Manager by its full
    ''' visible name it is the one in ProductNames, never a localized spelling of it.
    ''' </summary>
    <Fact>
    Public Sub The_localization_tables_never_spell_the_share_manager_title_by_hand()
        Dim offenders As New List(Of String)()
        For Each entry In Localization.All
            Dim candidates As New List(Of String)()
            candidates.Add(entry.Key)
            If entry.Value IsNot Nothing Then candidates.AddRange(entry.Value)
            For Each candidateText In candidates
                If candidateText IsNot Nothing AndAlso candidateText.IndexOf(ProductNames.ShareManager, StringComparison.Ordinal) >= 0 Then
                    offenders.Add(Shorten(candidateText))
                End If
            Next
        Next
        Assert.True(offenders.Count = 0,
                    "A table spells the Share Manager's visible name instead of taking it from ProductNames " &
                    "through a placeholder: " & String.Join(" | ", offenders.Take(10)))
    End Sub

    ' ---------------------------------------------------------------- helpers ----

    Private Shared Function AssemblyAttribute(source As String, name As String) As String
        Dim m = Regex.Match(source, "<Assembly:\s*" & name & "\(""(?<v>(?:[^""]|"""")*)""\)>")
        Assert.True(m.Success, "AssemblyInfo.vb has no " & name & " literal.")
        Return m.Groups("v").Value.Replace("""""", """")
    End Function

    ''' <summary>The XML-decoded text of a simple property element of the project file.</summary>
    Private Shared Function ProjectProperty(project As String, name As String) As String
        Dim m = Regex.Match(project, "<" & name & ">(?<v>[^<]*)</" & name & ">")
        Assert.True(m.Success, "The project file has no <" & name & "> element.")
        Return WebUtility.HtmlDecode(m.Groups("v").Value)
    End Function

    Private Shared Function InExcludedFolder(fullPath As String) As Boolean
        Dim sep = System.IO.Path.DirectorySeparatorChar
        Return fullPath.IndexOf(sep & "obj" & sep, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               fullPath.IndexOf(sep & "bin" & sep, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               fullPath.IndexOf(sep & "FastMediaSorterCompanion" & sep, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Private Shared Function Shorten(text As String) As String
        Dim flat = text.Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(flat.Length <= 90, flat, flat.Substring(0, 90) & "..")
    End Function

    Private Shared Function ReadSource(ParamArray parts As String()) As String
        Dim full = RepoRoot()
        For Each part In parts
            full = System.IO.Path.Combine(full, part)
        Next
        Assert.True(File.Exists(full), "Source file not found: " & full)
        Return File.ReadAllText(full)
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(System.IO.Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Could not locate the repository root (FastMediaSorter.sln) from the test binary.")
    End Function

End Class
