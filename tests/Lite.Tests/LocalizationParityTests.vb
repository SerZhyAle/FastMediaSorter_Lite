Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Guards the 13-language string table (SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §7.1).
'''
''' A Russian-keyed dictionary fails softly by design - a missing entry shows the
''' Russian source instead of crashing. That is the right runtime behaviour and the
''' wrong build behaviour, so these tests turn every soft failure into a hard one.
'''
''' Runs on BOTH legs (net48 + net10): the string tables are shared, only Codes/Names
''' are cut by the #If NETFRAMEWORK seam.
''' </summary>
Public Class LocalizationParityTests

    ' Number of translations stored per key: everything after Russian in the full set.
    Private Const TranslationSlots As Integer = 12

    <Fact>
    Public Sub Every_registered_string_has_a_translation_in_every_language()
        Dim missing As New List(Of String)()

        For Each entry In Localization.All
            Dim key = StripContext(entry.Key)
            Assert.Equal(TranslationSlots, entry.Value.Length)
            For i = 0 To entry.Value.Length - 1
                If String.IsNullOrEmpty(entry.Value(i)) Then
                    missing.Add(FullCodes(i + 1) & "  <-  " & Shorten(key))
                End If
            Next
        Next

        Assert.True(missing.Count = 0,
                    "Untranslated slots (" & missing.Count & "):" & vbLf & String.Join(vbLf, missing.Take(40)))
    End Sub

    ''' <summary>
    ''' The tables really do carry every language - not just "no empty slot because the
    ''' table is empty". A walk that inspects nothing passes exactly as quietly as a
    ''' clean one, so assert the table has real size.
    ''' </summary>
    <Fact>
    Public Sub The_table_is_not_trivially_empty()
        Assert.True(Localization.All.Count() > 250,
                    "Only " & Localization.All.Count() & " strings registered - the tables did not all load.")
    End Sub

    <Fact>
    Public Sub Placeholder_arity_matches_across_languages()
        Dim broken As New List(Of String)()

        For Each entry In Localization.All
            Dim key = StripContext(entry.Key)
            Dim expected = Placeholders(key)
            For i = 0 To entry.Value.Length - 1
                Dim got = Placeholders(entry.Value(i))
                If Not expected.SetEquals(got) Then
                    ' A dropped or renumbered {0} is the single most expensive machine-
                    ' translation error: it throws FormatException on the user's machine,
                    ' in a language nobody on this side reads.
                    broken.Add(FullCodes(i + 1) & "  " & Shorten(key) &
                               "  expected {" & String.Join(",", expected) & "} got {" & String.Join(",", got) & "}")
                End If
            Next
        Next

        Assert.True(broken.Count = 0, "Placeholder mismatch:" & vbLf & String.Join(vbLf, broken))
    End Sub

    ''' <summary>
    ''' No Cyrillic survives into a non-Cyrillic language: a Latin, Arabic, Indic or CJK
    ''' translation containing Russian letters is a slot someone forgot to fill in.
    ''' Ukrainian is Cyrillic and is skipped; so is the key itself.
    ''' </summary>
    <Fact>
    Public Sub No_Russian_text_survives_into_a_non_Cyrillic_language()
        Dim cyrillic As New Regex("[Ѐ-ӿ]")
        Dim leaked As New List(Of String)()

        For Each entry In Localization.All
            For i = 0 To entry.Value.Length - 1
                Dim code = FullCodes(i + 1)
                If code = "uk" Then Continue For
                If cyrillic.IsMatch(entry.Value(i)) Then
                    leaked.Add(code & "  " & Shorten(entry.Value(i)))
                End If
            Next
        Next

        Assert.True(leaked.Count = 0, "Cyrillic left in a non-Cyrillic language:" & vbLf & String.Join(vbLf, leaked))
    End Sub

    ''' <summary>
    ''' Every string the code passes to Localization.T / TC must be registered. Without
    ''' this a new call site shows Russian to all 13 languages and nothing complains -
    ''' the exact failure the soft fallback is designed to hide.
    ''' </summary>
    <Fact>
    Public Sub Every_T_call_site_in_the_sources_is_registered()
        Dim srcDir = FindSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate the src\ directory from the test binary.")

        ' A key can be written as a concatenation of literals and newline constants -
        ' Localization.T("a" & vbCrLf & "b"). Matching only the first literal would
        ' report every such call site as unregistered, so the whole chain is rebuilt
        ' into the string the dictionary is actually keyed by.
        Dim callSite As New Regex(
            "Localization\.(?:TF|TC|T)\(\s*(?:""(?<ctx>(?:[^""]|"""")*)""\s*,\s*)?" &
            "(?<key>""(?:[^""]|"""")*""(?:\s*&\s*(?:vbCrLf|vbLf|vbCr|Chr\(1[03]\)|""(?:[^""]|"""")*"")\s*)*)",
            RegexOptions.Singleline)
        Dim token As New Regex("""(?<lit>(?:[^""]|"""")*)""|(?<nl>vbCrLf|vbLf|vbCr|Chr\(10\)|Chr\(13\))")

        Dim known As New HashSet(Of String)(Localization.All.Select(Function(kv) StripContext(kv.Key)), StringComparer.Ordinal)
        Dim unregistered As New List(Of String)()

        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If srcPath.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            ' The Share Manager is a separate program with its own tables - its keys are
            ' guarded by Companion.Tests.LocalizationParityTests, not by this one.
            If srcPath.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            ' The layer itself declares the keys; it does not consume them.
            If srcPath.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For

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

    ''' <summary>
    ''' The guard above can only fail if it actually inspects something; prove the
    ''' regex finds the real call sites rather than silently matching nothing.
    ''' </summary>
    <Fact>
    Public Sub The_call_site_scanner_actually_finds_call_sites()
        Dim srcDir = FindSourceDir()
        Dim callSite As New Regex("Localization\.(?:TF|TC|T)\(")
        Dim found = Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) < 0).
            Sum(Function(f) callSite.Matches(File.ReadAllText(f)).Count)

        Assert.True(found > 300, "Only " & found & " call sites seen - the scanner is not looking where it should.")
    End Sub

    ''' <summary>
    ''' Is_Russian_Language is kept as a compatibility shim (Common_Module) but new code
    ''' must not read it. This test is what lets the shim stay: the compiler cannot warn,
    ''' so the test does.
    ''' </summary>
    <Fact>
    Public Sub Legacy_Is_Russian_Language_reads_are_tracked()
        Dim srcDir = FindSourceDir()
        Dim readers As New List(Of String)()

        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If srcPath.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If srcPath.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            ' Common_Module declares it; Localization owns the value it derives from.
            If Path.GetFileName(srcPath).Equals("Common_Module.vb", StringComparison.OrdinalIgnoreCase) Then Continue For
            If srcPath.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For

            Dim n = Regex.Matches(File.ReadAllText(srcPath), "Is_Russian_Language").Count
            If n > 0 Then readers.Add(Path.GetFileName(srcPath) & " x" & n)
        Next

        ' A budget, not a ban. What is left is deliberate, not forgotten:
        '   * interpolated $"" strings, which cannot be a dictionary key as written;
        '   * a handful of locals still feeding menu builders;
        '   * calls into OptionalRuntimeManager / OcrLanguageCatalog, which carry their
        '     own RU/EN text and are migrated with those modules, not here.
        ' All of them behave correctly today (a German user gets the English branch).
        ' Lower this number as they migrate - never raise it.
        Dim total = readers.Sum(Function(s) Integer.Parse(s.Substring(s.LastIndexOf("x"c) + 1), CultureInfo.InvariantCulture))
        Assert.True(total <= 40,
                    "Is_Russian_Language reads grew to " & total & " - new code must call Localization.T instead:" &
                    vbLf & String.Join(vbLf, readers))
    End Sub

    ' ------------------------------------------------------------------ helpers ----

    ''' <summary>Full 13-language code list, independent of the build's own seam.</summary>
    Private Shared ReadOnly FullCodes As String() = {
        "ru", "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh"}

    ''' <summary>
    ''' Turn the source text of a key expression - one literal, or several joined by
    ''' newline constants - into the exact string the dictionary is keyed by.
    ''' </summary>
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

    Private Shared Function StripContext(key As String) As String
        Dim sep = key.IndexOf(ChrW(1))
        Return If(sep < 0, key, key.Substring(sep + 1))
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

    ''' <summary>Walk up from the test binary until src\ appears - no hardcoded path.</summary>
    Private Shared Function FindSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
