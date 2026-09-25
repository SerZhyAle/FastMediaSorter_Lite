Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' Wiring a source file can only prove by reading it (the same style the viewer's tests
''' use for its crash hooks). Three things this suite holds in place:
'''
'''   * the crash hooks of ticket 054 H-11 - a tray app with no ThreadException/
'''     UnhandledException/UnobservedTaskException handler showed the raw WinForms
'''     "Unhandled exception" dialog and never wrote companion.log;
'''   * the in-app return of ticket 054 H-05 - ManageAction.MigrateToUser existed in the
'''     helper for a long time with no caller, while the button that promised it opened a
'''     web page;
'''   * the LAN-only split of ticket 054 H-06 - the policy push must be built from the
'''     server-level flag, never the wizard's per-export one, and the push must live where
'''     the switch is, not where the export is.
''' </summary>
Public Class HostingWiringTests

    <Fact>
    Public Sub Program_RegistersAllThreeCrashHooks()
        Dim src As String = ReadSource("Program.vb")
        Assert.Contains("Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)", src)
        Assert.Contains("AddHandler Application.ThreadException", src)
        Assert.Contains("AddHandler AppDomain.CurrentDomain.UnhandledException", src)
        Assert.Contains("AddHandler Threading.Tasks.TaskScheduler.UnobservedTaskException", src)
        ' ...and each of them lands in the log, not in a dialog of its own.
        For Each hook As String In {"OnThreadException", "OnUnhandledException", "OnUnobservedTaskException"}
            Assert.Contains(hook, src)
        Next
    End Sub

    <Fact>
    Public Sub MigrateToUser_HasACallerOutsideServiceControl()
        Dim hits As List(Of String) = Sources().
            Where(Function(p) Not p.EndsWith("\ServiceControl.vb", StringComparison.OrdinalIgnoreCase)).
            Where(Function(p) File.ReadAllText(p).IndexOf("ManageAction.MigrateToUser", StringComparison.Ordinal) >= 0).
            Select(Function(p) Path.GetFileName(p)).
            ToList()
        Assert.True(hits.Count > 0,
                    "ManageAction.MigrateToUser has no caller outside ServiceControl - the promised in-app " &
                    "return would again be a web page (ticket 054 H-05).")
    End Sub

    <Fact>
    Public Sub ThePolicyPush_IsBuiltFromTheServerLevelLanOnlyFlag()
        ' The controller owns the push; the export flag must not reach it in any form.
        ' Comments may explain the split by name; code may not use the export flag.
        Dim controller As String = CodeOnly(ReadSource(Path.Combine("Core", "ShareController.vb")))
        Assert.Contains("NetworkPolicyRequest(s.MaxConnections, s.LanOnly, s.ListenPort)", controller)
        Assert.DoesNotContain("LanOnlyExport", controller)
    End Sub

    <Fact>
    Public Sub ThePushLivesWithTheSwitch_NotWithTheExport()
        ' The Settings - Network switch pushes the moment it moves (ticket 054 H-06)...
        Dim settings As String = ReadSource(Path.Combine("Forms", "Share_Settings_Form.vb"))
        Assert.Contains("PushNetworkPolicyAsync", settings)
        ' ...and the wizard's checkbox shapes only its export.
        Dim wizard As String = ReadSource(Path.Combine("Forms", "PackageWizardForm.vb"))
        Assert.DoesNotContain("PushNetworkPolicyAsync", wizard)
    End Sub

    ' ------------------------------------------------------------------ helpers ----

    ''' <summary>The source without its whole-line comments (''' and ').</summary>
    Private Shared Function CodeOnly(src As String) As String
        Return String.Join(vbLf, src.Split({vbCrLf, vbLf}, StringSplitOptions.None).
            Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal)))
    End Function

    Private Shared Function Sources() As IEnumerable(Of String)
        Dim dir = FindCompanionSourceDir()
        Assert.True(dir IsNot Nothing, "Could not locate src\FastMediaSorterCompanion from the test binary.")
        Return Directory.GetFiles(dir, "*.vb", SearchOption.AllDirectories).
            Where(Function(f) f.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              f.IndexOf("\Localization\", StringComparison.OrdinalIgnoreCase) < 0)
    End Function

    Private Shared Function ReadSource(relative As String) As String
        Dim dir = FindCompanionSourceDir()
        Assert.True(dir IsNot Nothing, "Could not locate src\FastMediaSorterCompanion from the test binary.")
        Dim full = Path.Combine(dir, relative)
        Assert.True(File.Exists(full), "Missing source file: " & relative)
        Return File.ReadAllText(full)
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
