Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Shared plumbing of the exchange server tests (tickets 141-144). The exchange modules keep a few
''' process-global test seams (the exe directory, the data directory, the process runner, the elevated
''' launcher, the SCM and netsh reads), so every test class that sets one is in
''' <see cref="UiLanguageCollection"/> - one collection, never in parallel - and restores them all through
''' <see cref="Reset"/>, which is also what a test calls first.
''' </summary>
Friend Module ExchangeTestSupport

    ''' <summary>Puts every seam back to "real". Called at the start and end of every test that sets one.</summary>
    Friend Sub Reset()
        ExchangeProcess.ExeDirectoryOverride = Nothing
        ExchangeProcess.DataDirOverride = Nothing
        ExchangeFeatures.ComponentProbeOverride = Nothing
        ExchangeProcess.RunOverride = Nothing
        ExchangeProcess.ForgetDataDir()
        ExchangeAdmin.PipeNameOverride = Nothing
        ExchangeElevation.Launcher = AddressOf DefaultLauncherForTests
        ExchangeFirewall.ShowRuleOverride = Nothing
        ExchangeService.QueryStateOverride = Nothing
        ExchangeService.ImagePathOverride = Nothing
        ExchangeService.ControlOverride = Nothing
        ExchangeService.MachineDataDirOverride = Nothing
        ExchangeService.UserDataDirOverride = Nothing
        ExchangeService.StateWaitMs = 15000
    End Sub

    ''' <summary>A launcher that must never run: a test that reaches the real runas fails loudly instead of
    ''' raising a UAC prompt on the developer's machine.</summary>
    Private Function DefaultLauncherForTests(file As String, args As String) As Integer
        Throw New InvalidOperationException("A test reached the real elevated launcher: " & file & " " & args)
    End Function

    ''' <summary>A scratch folder that holds a stand-in for the exchange component: an empty exe and, on
    ''' request, the two helper scripts. Nothing in it is ever executed by a fake-driven test.</summary>
    Friend Function MakeFakeComponent(withExe As Boolean, withFirewallHelper As Boolean, withServiceHelper As Boolean) As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-exchange-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        If withExe Then File.WriteAllBytes(Path.Combine(dir, ExchangeProcess.ExeName), New Byte() {})
        If withFirewallHelper Then File.WriteAllText(Path.Combine(dir, ExchangeFirewall.HelperScriptName), "# stand-in")
        If withServiceHelper Then File.WriteAllText(Path.Combine(dir, ExchangeService.HelperScriptName), "# stand-in")
        Return dir
    End Function

    Friend Sub DeleteQuietly(dir As String)
        Try
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        Catch
        End Try
    End Sub

    ''' <summary>Walks up from the test binary to the repository root (the folder that holds src and tests).</summary>
    Friend Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If Directory.Exists(Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")) AndAlso
               Directory.Exists(Path.Combine(dir.FullName, "tests")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Could not locate the repository root from the test binary.")
    End Function

    Friend Function CompanionDir() As String
        Return Path.Combine(RepoRoot(), "src", "FastMediaSorterCompanion")
    End Function

    ''' <summary>Every Companion .vb source outside obj/bin.</summary>
    Friend Function CompanionSources() As List(Of String)
        Return Directory.GetFiles(CompanionDir(), "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0).ToList()
    End Function

    Friend Function ReadSource(relative As String) As String
        Dim full As String = Path.Combine(CompanionDir(), relative)
        Assert.True(File.Exists(full), "Missing source file: " & relative)
        Return File.ReadAllText(full)
    End Function

    ''' <summary>The source without its whole-line comments (''' and ').</summary>
    Friend Function CodeOnly(src As String) As String
        Return String.Join(vbLf, src.Split({vbCrLf, vbLf}, StringSplitOptions.None).
            Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal)))
    End Function

    ''' <summary>The text of one method body, by its declaration line up to the next "End Sub/Function".</summary>
    Friend Function MethodBody(src As String, declaration As String) As String
        Dim start As Integer = src.IndexOf(declaration, StringComparison.Ordinal)
        Assert.True(start >= 0, declaration & " not found")
        Dim endSub As Integer = src.IndexOf(vbLf & "    End Sub", start, StringComparison.Ordinal)
        Dim endFn As Integer = src.IndexOf(vbLf & "    End Function", start, StringComparison.Ordinal)
        Dim finish As Integer = If(endSub < 0, endFn, If(endFn < 0, endSub, Math.Min(endSub, endFn)))
        Assert.True(finish > start, "no end for " & declaration)
        Return src.Substring(start, finish - start)
    End Function

    ''' <summary>Runs a test body on an STA thread (WinForms), failing on a timeout and re-throwing what it threw.</summary>
    Friend Sub OnUi(test As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         test()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI check timed out.")
        If failure IsNot Nothing Then Throw failure
    End Sub

End Module
