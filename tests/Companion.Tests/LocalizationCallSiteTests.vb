Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' APP-BEHAVIOUR rule 7 for the Share Manager (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md
''' Ф-4, §5 item 2). The engine is the viewer's, linked; the call sites and the table are this
''' program's own, so the gate runs here over them. It walks every language through the one global
''' <c>Localization.CurrentCode</c>, so it runs in the same serial collection as every other test that sets it.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class LocalizationCallSiteTests

    <Fact>
    Public Sub TF_never_throws_with_too_few_arguments_in_any_language()
        Dim saved = Localization.CurrentCode
        Try
            For Each code In Localization.Codes
                Localization.CurrentCode = code
                Assert.NotNull(Localization.TF("всего {0} (с запуска {1})"))
                Assert.NotNull(Localization.TF("всего {0} (с запуска {1})", 5))
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub Every_TF_call_site_passes_as_many_arguments_as_its_key_names()
        Dim srcDir = FindCompanionSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate src\FastMediaSorterCompanion from the test binary.")

        Dim wrong As New List(Of String)()
        Dim checkedCalls = 0
        Dim unkeyed = 0
        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).
                Where(Function(f) f.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                                  f.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                                  f.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) < 0)
            For Each c In TfCallSiteScanner.Scan(File.ReadAllText(srcPath), unkeyed)
                checkedCalls += 1
                Dim expected = TfCallSiteScanner.ExpectedArguments(c.Key)
                If c.ArgumentCount <> expected Then
                    wrong.Add(Path.GetFileName(srcPath) & ":" & c.Line & "  expects " & expected &
                              ", passes " & c.ArgumentCount)
                End If
            Next
        Next

        Assert.True(checkedCalls > 20, "Only " & checkedCalls & " TF call sites checked - the scanner is not finding them.")
        Assert.True(wrong.Count = 0, "TF calls whose argument count does not match the key:" & vbLf & String.Join(vbLf, wrong))
    End Sub

    ''' <summary>The four sites that formatted T(..) by hand are gone (Ф-4).</summary>
    <Fact>
    Public Sub No_site_formats_a_translation_by_hand()
        Dim srcDir = FindCompanionSourceDir()
        Dim offenders = Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              File.ReadAllText(f).Contains("String.Format(Localization.T(")).
            Select(Function(f) Path.GetFileName(f)).ToList()
        Assert.True(offenders.Count = 0, "String.Format over a translation bypasses TF's safety: " & String.Join(", ", offenders))
    End Sub

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
