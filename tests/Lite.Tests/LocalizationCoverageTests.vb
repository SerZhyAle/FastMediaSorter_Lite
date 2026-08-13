Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Finds Russian UI text that never reaches the localization layer.
'''
''' This exists because the first metric was wrong. LocalizationParityTests counts
''' Is_Russian_Language READS and holds them under a budget - but one
''' "Dim rus As Boolean = Is_Russian_Language" at the top of a Select Case covers dozens
''' of strings. The budget read 38 of 40, comfortably green, while 187 strings were still
''' untranslated - including eight drop-downs in the settings window that were not even
''' bilingual and showed Russian to every user of the shipped build.
'''
''' So this test counts STRINGS. Every Cyrillic string literal in the sources must either
''' sit inside a Localization.T/TF/TC call, or itself be a registered key (key arrays and
''' helper arguments such as JumpBy/Choice reach the layer one hop later). What is left
''' is an explicit, justified allow-list.
''' </summary>
Public Class LocalizationCoverageTests

    <Fact>
    Public Sub No_Russian_UI_text_bypasses_the_localization_layer()
        Dim srcDir = FindSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate the src\ directory from the test binary.")

        Dim known As New HashSet(Of String)(Localization.All.Select(Function(kv) StripContext(kv.Key)), StringComparer.Ordinal)
        Dim cyrillic As New Regex("[А-яЁё]")
        Dim literal As New Regex("""(?:[^""]|"""")*""")
        Dim stray As New List(Of String)()

        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If Skip(srcPath) Then Continue For

            Dim text = BlankComments(File.ReadAllText(srcPath))
            Dim covered = CoveredSpans(text)

            For Each m As Match In literal.Matches(text)
                If Not cyrillic.IsMatch(m.Value) Then Continue For
                Dim value = m.Value.Substring(1, m.Value.Length - 2).Replace("""""", """")
                If known.Contains(value) Then Continue For
                If covered.Any(Function(s) m.Index >= s.Key AndAlso m.Index < s.Value) Then Continue For
                stray.Add(Path.GetFileName(srcPath) & "  " & Shorten(value))
            Next
        Next

        Assert.True(stray.Count = 0,
                    "Russian text that never reaches Localization.T (" & stray.Count & "):" & vbLf &
                    String.Join(vbLf, stray.Take(40)))
    End Sub

    ''' <summary>A scan that inspects nothing passes as quietly as a clean one.</summary>
    <Fact>
    Public Sub The_coverage_scanner_actually_inspects_the_sources()
        Dim srcDir = FindSourceDir()
        Dim seen = Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).Count(Function(f) Not Skip(f))
        Assert.True(seen > 40, "Only " & seen & " source files scanned - the filter is excluding too much.")
    End Sub

    ' ------------------------------------------------------------------ helpers ----

    ''' <summary>
    ''' Files whose Cyrillic literals are correct as they are:
    '''   * the layer itself - it DECLARES the keys and holds the endonyms;
    '''   * OcrLanguageCatalog - 33 language names, each its own endonym (spec §2.8);
    '''   * OllamaTranslator - Russian words used to DETECT an untranslated reply from
    '''     the model. They are matched against text, never shown, so translating them
    '''     would break the check they exist for.
    '''   * OcrTextFilter - the Cyrillic vowel set. A character CLASS, in the same sense as
    '''     the Latin one beside it: it is matched against recognized text to tell a word
    '''     from a run of consonants, and never displayed. "Translating" an alphabet is not
    '''     a meaningful operation.
    ''' The Share Manager has its own tables and its own copy of this test.
    ''' </summary>
    Private Shared ReadOnly Exempt As String() = {
        "OcrLanguageCatalog.vb", "OllamaTranslator.vb", "OcrTextFilter.vb"}

    Private Shared Function Skip(srcPath As String) As Boolean
        If srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If srcPath.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If srcPath.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If srcPath.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Return Exempt.Any(Function(n) Path.GetFileName(srcPath).Equals(n, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' Replaces whole-line comments with spaces, keeping every offset intact so the
    ''' span arithmetic below still lines up. Russian prose in a comment is fine.
    ''' </summary>
    Friend Shared Function BlankComments(text As String) As String
        Return Regex.Replace(text, "(?m)^([ \t]*)'.*$",
                             Function(m) m.Groups(1).Value & New String(" "c, m.Value.Length - m.Groups(1).Value.Length))
    End Function

    ''' <summary>
    ''' Argument lists of Localization.T/TF/TC and of the table's own Add/AddC, found by
    ''' balancing parentheses so a call spanning several lines counts as one span. A
    ''' naive same-line check reported every multi-line call as a violation.
    ''' </summary>
    Friend Shared Function CoveredSpans(text As String) As List(Of KeyValuePair(Of Integer, Integer))
        Dim spans As New List(Of KeyValuePair(Of Integer, Integer))()
        For Each m As Match In Regex.Matches(text, "(?:Localization\.(?:TF|TC|T)|(?<![\w.])AddC|(?<![\w.])Add)\s*\(")
            Dim i = m.Index + m.Length
            Dim depth = 1
            Dim inString = False
            While i < text.Length AndAlso depth > 0
                Dim c = text(i)
                If inString Then
                    If c = """"c Then
                        If i + 1 < text.Length AndAlso text(i + 1) = """"c Then i += 1 Else inString = False
                    End If
                Else
                    If c = """"c Then
                        inString = True
                    ElseIf c = "("c Then
                        depth += 1
                    ElseIf c = ")"c Then
                        depth -= 1
                    End If
                End If
                i += 1
            End While
            spans.Add(New KeyValuePair(Of Integer, Integer)(m.Index, i))
        Next
        Return spans
    End Function

    Private Shared Function StripContext(key As String) As String
        Dim sep = key.IndexOf(ChrW(1))
        Return If(sep < 0, key, key.Substring(sep + 1))
    End Function

    Private Shared Function Shorten(s As String) As String
        Dim one = s.Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(one.Length <= 70, one, one.Substring(0, 70) & "..")
    End Function

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
