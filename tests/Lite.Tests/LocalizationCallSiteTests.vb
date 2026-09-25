Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' APP-BEHAVIOUR rule 7 - localized rendering never ends the process
''' (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md Ф-4, §5 item 2).
'''
''' Two halves. The runtime half: TF degrades instead of throwing - a missing argument
''' renders blank, an unparseable template renders itself - in every shipped language.
''' The source half: every TF call passes exactly as many arguments as its key names,
''' so the degradation stays a safety net rather than something a user sees.
''' </summary>
Public Class LocalizationCallSiteTests

    <Fact>
    Public Sub TF_never_throws_with_too_few_or_too_many_arguments_in_any_language()
        Dim saved = Localization.CurrentCode
        Try
            For Each code In Localization.Codes
                Localization.CurrentCode = code
                ' A real two-placeholder key (registered), so every translation is exercised.
                Dim none = Localization.TF("Не удалось обработать {0}: {1}")
                Assert.NotNull(none)
                Dim extra = Localization.TF("Не удалось обработать {0}: {1}", "a", "b", "c")
                Assert.Contains("a", extra)
                Dim nothingArgs = Localization.TF("Не удалось обработать {0}: {1}", CType(Nothing, Object()))
                Assert.NotNull(nothingArgs)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub A_missing_argument_renders_blank()
        Assert.Equal("a- b-", Localization.SafeFormat("a-{0} b-{1}", New Object() {}))
        Assert.Equal("x=1 y=", Localization.SafeFormat("x={0} y={1}", New Object() {1}))
    End Sub

    <Fact>
    Public Sub An_unparseable_template_renders_itself()
        Assert.Equal("broken {0", Localization.SafeFormat("broken {0", New Object() {1}))
        Assert.Equal("bad {x}", Localization.SafeFormat("bad {x}", New Object() {1}))
        Assert.Equal("stray }", Localization.SafeFormat("stray }", New Object() {}))
        Assert.Equal("huge {123456}", Localization.SafeFormat("huge {123456}", New Object() {}))
    End Sub

    <Fact>
    Public Sub Escaped_braces_are_not_placeholders()
        Assert.Equal(-1, Localization.HighestPlaceholder("{{0}}"))
        Assert.Equal(2, Localization.HighestPlaceholder("{0} {2:N0} {1,5}"))
        Assert.Equal("{0} 7", Localization.SafeFormat("{{0}} {0}", New Object() {7}))
    End Sub

    <Fact>
    Public Sub Every_TF_call_site_passes_as_many_arguments_as_its_key_names()
        Dim srcDir = FindSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate the src\ directory from the test binary.")

        Dim wrong As New List(Of String)()
        Dim checkedCalls = 0
        Dim unkeyed = 0
        For Each srcPath In ViewerSources(srcDir)
            For Each c In TfCallSiteScanner.Scan(File.ReadAllText(srcPath), unkeyed)
                checkedCalls += 1
                Dim expected = TfCallSiteScanner.ExpectedArguments(c.Key)
                If c.ArgumentCount <> expected Then
                    wrong.Add(Path.GetFileName(srcPath) & ":" & c.Line & "  expects " & expected &
                              ", passes " & c.ArgumentCount & "  " & Shorten(c.Key))
                End If
            Next
        Next

        ' The gate can only fail if it inspects something.
        Assert.True(checkedCalls > 80, "Only " & checkedCalls & " TF call sites checked - the scanner is not finding them.")
        Assert.True(wrong.Count = 0, "TF calls whose argument count does not match the key:" & vbLf & String.Join(vbLf, wrong))
    End Sub

    <Fact>
    Public Sub The_scanner_counts_nested_and_multi_line_arguments()
        Dim unkeyed = 0
        Dim src = "x = Localization.TF(""a {0} {1}"" & vbCrLf & ""b"", Foo(1, 2), New Integer() {3, 4}) ' c, d" & vbLf &
                  "y = Localization.TF(" & vbLf & "    ""one {0}"", ' note, here" & vbLf & "    Bar("","", 5))" & vbLf &
                  "z = Localization.TF(someVariable, 1)"
        Dim calls = TfCallSiteScanner.Scan(src, unkeyed)
        Assert.Equal(2, calls.Count)
        Assert.Equal(2, calls(0).ArgumentCount)
        Assert.Equal("a {0} {1}" & vbCrLf & "b", calls(0).Key)
        Assert.Equal(1, calls(1).ArgumentCount)
        Assert.Equal(1, unkeyed)
    End Sub

    Private Shared Function ViewerSources(srcDir As String) As IEnumerable(Of String)
        Return Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) < 0)
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
