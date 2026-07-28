Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Guards the Share Manager's 13-language string table
''' (SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A', §7.1).
'''
''' Same contract as the viewer's LocalizationParityTests, over Companion's own tables:
''' a Russian-keyed dictionary fails softly by design - a missing entry shows Russian
''' rather than crashing - which is right at runtime and wrong at build time. These
''' tests turn each soft failure into a hard one.
'''
''' Companion is not multi-targeted: it is .NET 10 only, so it always ships all 13.
''' </summary>
Public Class LocalizationParityTests

    ' Translations stored per key: everything after Russian, which is the key itself.
    Private Const TranslationSlots As Integer = 12

    Private Shared ReadOnly Codes As String() = {
        "ru", "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh"}

    <Fact>
    Public Sub The_Share_Manager_ships_all_thirteen_languages()
        ' Unlike the viewer there is no net48 leg to cut the list down.
        Assert.Equal(Codes, Localization.Codes)
        Assert.Equal(Codes.Length, Localization.Names.Length)
    End Sub

    <Fact>
    Public Sub Every_registered_string_has_a_translation_in_every_language()
        Dim missing As New List(Of String)()

        For Each entry In Localization.All
            Assert.Equal(TranslationSlots, entry.Value.Length)
            For i = 0 To entry.Value.Length - 1
                If String.IsNullOrEmpty(entry.Value(i)) Then
                    missing.Add(Codes(i + 1) & "  <-  " & Shorten(entry.Key))
                End If
            Next
        Next

        Assert.True(missing.Count = 0,
                    "Untranslated slots (" & missing.Count & "):" & vbLf & String.Join(vbLf, missing.Take(40)))
    End Sub

    ''' <summary>
    ''' The tables really loaded - a walk over an empty dictionary passes as quietly as
    ''' a clean one, so assert the table has real size.
    ''' </summary>
    <Fact>
    Public Sub The_table_is_not_trivially_empty()
        Assert.True(Localization.All.Count() > 180,
                    "Only " & Localization.All.Count() & " strings registered - the tables did not all load.")
    End Sub

    <Fact>
    Public Sub Placeholder_arity_matches_across_languages()
        Dim broken As New List(Of String)()

        For Each entry In Localization.All
            Dim expected = Placeholders(entry.Key)
            For i = 0 To entry.Value.Length - 1
                Dim got = Placeholders(entry.Value(i))
                If Not expected.SetEquals(got) Then
                    ' A dropped or renumbered {0} throws FormatException on the user's
                    ' machine, in a language nobody on this side reads.
                    broken.Add(Codes(i + 1) & "  " & Shorten(entry.Key) &
                               "  expected {" & String.Join(",", expected) & "} got {" & String.Join(",", got) & "}")
                End If
            Next
        Next

        Assert.True(broken.Count = 0, "Placeholder mismatch:" & vbLf & String.Join(vbLf, broken))
    End Sub

    <Fact>
    Public Sub No_Russian_text_survives_into_a_non_Cyrillic_language()
        Dim cyrillic As New Regex("[Ѐ-ӿ]")
        Dim leaked As New List(Of String)()

        For Each entry In Localization.All
            For i = 0 To entry.Value.Length - 1
                If Codes(i + 1) = "uk" Then Continue For
                If cyrillic.IsMatch(entry.Value(i)) Then
                    leaked.Add(Codes(i + 1) & "  " & Shorten(entry.Value(i)))
                End If
            Next
        Next

        Assert.True(leaked.Count = 0, "Cyrillic left in a non-Cyrillic language:" & vbLf & String.Join(vbLf, leaked))
    End Sub

    ''' <summary>
    ''' Every string Companion passes to Localization.T / TF must be registered. Without
    ''' this a new call site quietly shows Russian in all thirteen languages.
    ''' </summary>
    <Fact>
    Public Sub Every_T_call_site_in_the_Companion_sources_is_registered()
        Dim srcDir = FindCompanionSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate src\FastMediaSorterCompanion from the test binary.")

        Dim callSite As New Regex(
            "Localization\.(?:TF|TC|T)\(\s*(?:""(?<ctx>(?:[^""]|"""")*)""\s*,\s*)?" &
            "(?<key>""(?:[^""]|"""")*""(?:\s*&\s*(?:vbCrLf|vbLf|vbCr|Chr\(1[03]\)|""(?:[^""]|"""")*"")\s*)*)",
            RegexOptions.Singleline)
        Dim token As New Regex("""(?<lit>(?:[^""]|"""")*)""|(?<nl>vbCrLf|vbLf|vbCr|Chr\(10\)|Chr\(13\))")

        Dim known As New HashSet(Of String)(Localization.All.Select(Function(kv) kv.Key), StringComparer.Ordinal)
        Dim unregistered As New List(Of String)()

        For Each srcPath In CompanionSources(srcDir)
            Dim text = File.ReadAllText(srcPath)
            For Each m As Match In callSite.Matches(text)
                Dim key = RebuildKey(m.Groups("key").Value, token)
                If key.Length = 0 Then Continue For
                If Not known.Contains(key) Then
                    unregistered.Add(Path.GetFileName(srcPath) & "  " & Shorten(key))
                End If
            Next
        Next

        Assert.True(unregistered.Count = 0,
                    "Call sites with no entry in the table (they would show Russian to every language):" &
                    vbLf & String.Join(vbLf, unregistered.Distinct().Take(40)))
    End Sub

    ''' <summary>Prove the scanner looks where it should - a regex that matches nothing
    ''' would make the guard above pass for the wrong reason.</summary>
    <Fact>
    Public Sub The_call_site_scanner_actually_finds_call_sites()
        Dim srcDir = FindCompanionSourceDir()
        Dim callSite As New Regex("Localization\.(?:TF|TC|T)\(")
        Dim found = CompanionSources(srcDir).Sum(Function(f) callSite.Matches(File.ReadAllText(f)).Count)

        Assert.True(found > 200, "Only " & found & " call sites seen - the scanner is not looking where it should.")
    End Sub

    ''' <summary>
    ''' The old RU/EN flag is gone from Companion for good. The viewer still keeps a
    ''' derived shim under a shrinking budget; here the count is zero, and staying at
    ''' zero is what stops a new two-way branch from being added by habit.
    ''' </summary>
    <Fact>
    Public Sub No_source_reads_the_old_Russian_flag()
        Dim srcDir = FindCompanionSourceDir()
        Dim readers As New List(Of String)()

        For Each srcPath In CompanionSources(srcDir)
            Dim n = Regex.Matches(File.ReadAllText(srcPath), "\bIs_Russian_Language\b").Count
            If n > 0 Then readers.Add(Path.GetFileName(srcPath) & " x" & n)
        Next

        Assert.True(readers.Count = 0,
                    "Is_Russian_Language is back in the Share Manager - use Localization.T instead:" &
                    vbLf & String.Join(vbLf, readers))
    End Sub

    ''' <summary>
    ''' Companion and the viewer must persist the language under the SAME registry
    ''' coordinates, or a switch in one silently fails to reach the other (invariant 9).
    ''' </summary>
    <Fact>
    Public Sub The_language_is_stored_where_the_viewer_reads_it()
        Assert.Equal("SZA", CompanionGlobals.App_name)
        Assert.Equal("FastMediaSorter", CompanionGlobals.Second_App_Name)
        Assert.Equal("UiLanguage", Localization.UiLanguageValue)
        Assert.Equal("Is_Russian_Language", Localization.LegacyRussianValue)
    End Sub

    ''' <summary>
    ''' The offline port-forward guide exists in three languages only, so every other
    ''' UI language must resolve to English rather than to a page that is not there.
    ''' </summary>
    <Fact>
    Public Sub The_guide_falls_back_to_English_outside_its_three_languages()
        Dim saved = Localization.CurrentCode
        Try
            For Each code In Codes
                Localization.CurrentCode = code
                Dim guide = Localization.GuideCode()
                If code = "ru" OrElse code = "uk" Then
                    Assert.Equal(code, guide)
                Else
                    Assert.Equal("en", guide)
                End If
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' ------------------------------------------------------------------ helpers ----

    Private Shared Function CompanionSources(srcDir As String) As IEnumerable(Of String)
        Return Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) < 0)
    End Function

    Private Shared Function RebuildKey(expr As String, token As Regex) As String
        Dim sb As New Text.StringBuilder()
        For Each t As Match In token.Matches(expr)
            If t.Groups("lit").Success Then
                sb.Append(t.Groups("lit").Value.Replace("""""", """"))
            Else
                Select Case t.Groups("nl").Value
                    Case "vbCrLf" : sb.Append(vbCrLf)
                    Case "vbLf", "Chr(10)" : sb.Append(vbLf)
                    Case "vbCr", "Chr(13)" : sb.Append(vbCr)
                End Select
            End If
        Next
        Return sb.ToString()
    End Function

    Private Shared Function Placeholders(s As String) As HashSet(Of String)
        Dim set1 As New HashSet(Of String)(StringComparer.Ordinal)
        If s Is Nothing Then Return set1
        For Each m As Match In Regex.Matches(s, "\{(\d+)[^}]*\}")
            set1.Add(m.Groups(1).Value)
        Next
        Return set1
    End Function

    Private Shared Function Shorten(s As String) As String
        If s Is Nothing Then Return ""
        Dim one = s.Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(one.Length <= 70, one, one.Substring(0, 70) & "..")
    End Function

    ''' <summary>Walk up from the test binary until src\FastMediaSorterCompanion appears.</summary>
    Private Shared Function FindCompanionSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Program.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
