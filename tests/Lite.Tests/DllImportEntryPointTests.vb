#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Every DllImport in src/ (both viewers and the Share Manager) must resolve to a real
''' export - spec 040. A declaration whose VB name is not an export and that carries no
''' EntryPoint compiles cleanly and throws EntryPointNotFoundException only when it is
''' called; behind a bare Catch that is a feature which silently does nothing. That is
''' exactly how the viewer's wake of the Share Manager stayed broken in a shipped build.
'''
''' The lookup mirrors the runtime's probing with ExactSpelling off: the name as
''' written, then with the W and A suffixes. A library that does not load on this
''' machine (MAPI32 on a box with no mail client) cannot be judged and is skipped.
''' </summary>
Public Class DllImportEntryPointTests

    Private Shared ReadOnly DeclarationPattern As New Regex(
        "<DllImport\(""(?<dll>[^""]+)""(?<args>[^>]*)\)>\s*(?:(?:Public|Private|Friend|Protected|Shared|Overloads)\s+)*(?:Function|Sub)\s+(?<name>\w+)",
        RegexOptions.Compiled Or RegexOptions.IgnoreCase)

    Private Shared ReadOnly EntryPointPattern As New Regex(
        "EntryPoint\s*:=\s*""(?<entry>[^""]+)""", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

    <Fact>
    Public Sub Every_DllImport_in_src_names_a_real_export()
        Dim sourceDir As String = FindSourceDir()
        Assert.False(sourceDir Is Nothing, "src/ not found above " & AppContext.BaseDirectory)

        Dim checkedCount As Integer = 0
        Dim failures As New List(Of String)()

        For Each file As String In Directory.GetFiles(sourceDir, "*.vb", SearchOption.AllDirectories)
            If file.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse
               file.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For

            For Each m As Match In DeclarationPattern.Matches(IO.File.ReadAllText(file))
                Dim dll As String = m.Groups("dll").Value
                Dim entry As Match = EntryPointPattern.Match(m.Groups("args").Value)
                Dim exportName As String = If(entry.Success, entry.Groups("entry").Value, m.Groups("name").Value)

                Dim handle As IntPtr
                If Not NativeLibrary.TryLoad(dll, handle) Then Continue For

                checkedCount += 1
                Dim address As IntPtr
                If Not (NativeLibrary.TryGetExport(handle, exportName, address) OrElse
                        NativeLibrary.TryGetExport(handle, exportName & "W", address) OrElse
                        NativeLibrary.TryGetExport(handle, exportName & "A", address)) Then
                    failures.Add(Path.GetFileName(file) & ": " & m.Groups("name").Value & " -> " & dll & "!" & exportName &
                                 If(entry.Success, "", " (no EntryPoint)"))
                End If
            Next
        Next

        Assert.True(checkedCount > 20, "only " & checkedCount.ToString() & " declarations were checked - is the pattern still matching?")
        Assert.True(failures.Count = 0, "DllImport declarations with no such export:" & Environment.NewLine &
                                        String.Join(Environment.NewLine, failures))
    End Sub

    <Fact>
    Public Sub The_check_catches_a_renamed_import_without_an_EntryPoint()
        ' Self-test on the shape that shipped broken.
        Dim sample As String = "<DllImport(""user32.dll"", CharSet:=CharSet.Auto)>" & vbLf &
                               "    Private Shared Function SendMessageCompanionCopyData(hWnd As IntPtr) As Integer"
        Dim m As Match = DeclarationPattern.Match(sample)
        Assert.True(m.Success)
        Assert.Equal("SendMessageCompanionCopyData", m.Groups("name").Value)
        Assert.False(EntryPointPattern.IsMatch(m.Groups("args").Value))

        Dim handle As IntPtr = NativeLibrary.Load("user32.dll")
        Dim address As IntPtr
        Assert.False(NativeLibrary.TryGetExport(handle, "SendMessageCompanionCopyDataW", address))
        Assert.True(NativeLibrary.TryGetExport(handle, "SendMessageTimeoutW", address))
    End Sub

    Private Shared Function FindSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso IO.File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
#End If
