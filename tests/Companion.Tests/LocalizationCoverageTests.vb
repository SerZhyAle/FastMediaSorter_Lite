Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The Share Manager's half of the coverage guard - see the viewer's
''' LocalizationCoverageTests for why counting Is_Russian_Language reads was the wrong
''' metric and counting strings is the right one.
'''
''' Companion has no exemptions: every Cyrillic literal in its sources must reach the
''' layer, and its own parity test already proves the flag itself is gone.
''' </summary>
Public Class LocalizationCoverageTests

    <Fact>
    Public Sub No_Russian_UI_text_bypasses_the_localization_layer()
        Dim srcDir = FindCompanionSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate src\FastMediaSorterCompanion from the test binary.")

        Dim known As New HashSet(Of String)(Localization.All.Select(Function(kv) kv.Key), StringComparer.Ordinal)
        Dim cyrillic As New Regex("[А-яЁё]")
        Dim literal As New Regex("""(?:[^""]|"""")*""")
        Dim stray As New List(Of String)()

        For Each srcPath In Sources(srcDir)
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

    <Fact>
    Public Sub The_coverage_scanner_actually_inspects_the_sources()
        Dim seen = Sources(FindCompanionSourceDir()).Count()
        Assert.True(seen > 20, "Only " & seen & " source files scanned - the filter is excluding too much.")
    End Sub

    ' ------------------------------------------------------------------ helpers ----

    Private Shared Function Sources(srcDir As String) As IEnumerable(Of String)
        Return Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) < 0)
    End Function

    Private Shared Function BlankComments(text As String) As String
        Return Regex.Replace(text, "(?m)^([ \t]*)'.*$",
                             Function(m) m.Groups(1).Value & New String(" "c, m.Value.Length - m.Groups(1).Value.Length))
    End Function

    Private Shared Function CoveredSpans(text As String) As List(Of KeyValuePair(Of Integer, Integer))
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

    Private Shared Function Shorten(s As String) As String
        Dim one = s.Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(one.Length <= 70, one, one.Substring(0, 70) & "..")
    End Function

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
