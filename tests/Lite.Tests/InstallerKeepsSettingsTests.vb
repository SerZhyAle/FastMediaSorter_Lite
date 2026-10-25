#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Installing a new version over the old one keeps every setting (owner requirement, 2026-10-25).
''' The settings live outside the install folder - the registry hive SZA\FastMediaSorter and the per-user
''' state folders - so the installers keep them by never reaching there. Read from the two Inno scripts
''' and the winget manifests as they ship: [InstallDelete] runs on EVERY upgrade, so it may remove
''' shortcuts only; no registry deletion may touch anything but file associations and the logon
''' autostart; the uninstall removes disposable caches and nothing the user configured (a reinstall
''' finds the settings, ticket 055 I-10); winget never uninstalls the previous version first.
''' Net10 only: these read repository files, and the runtime they run under changes nothing.
''' </summary>
Public Class InstallerKeepsSettingsTests

    ''' <summary>
    ''' The only things [InstallDelete] may name: Start-menu and desktop shortcuts. A leftover of an old
    ''' product name is cleaned there; a user's data never is.
    ''' </summary>
    Private Shared ReadOnly ShortcutRoots As String() = {"{autoprograms}\", "{commonprograms}\", "{autodesktop}\", "{group}\"}

    ''' <summary>
    ''' The only registry locations a [Code] deletion may reach: the per-user file-association classes
    ''' and the Share Manager's logon autostart (with the shell's approval record for it).
    ''' </summary>
    Private Shared ReadOnly RegistryDeletionTargets As String() = {
        "Software\Classes", "\CurrentVersion\Run", "\StartupApproved\Run"}

    ''' <summary>
    ''' The folders under %LOCALAPPDATA%\SZA\FastMediaSorter an uninstall may remove: downloaded runtimes,
    ''' models, caches and logs, all rebuilt on demand. Any other folder in that tree (the exchange
    ''' credentials, the broadcast descriptors, ..) is the user's. A new cache is added here on purpose.
    ''' </summary>
    Private Shared ReadOnly DisposableStateFolders As String() = {
        "optional-runtime", "ffmpeg", "tessdata", "tessdata-best", "ocr-cache", "decode-cache",
        "archive-cache", "browser-translate-cache", "downloads", "remote-cache", "logs"}

    Private Const StateRoot As String = "{localappdata}\SZA\FastMediaSorter"

    <Theory>
    <InlineData("FastMediaSorter.iss")>
    <InlineData("FastMediaSorterServer.iss")>
    Public Sub AnUpgradeDeletesShortcutsAndNothingElse(script As String)
        Dim entries As List(Of String) = SectionLines(ReadScript(script), "InstallDelete")
        Assert.NotEmpty(entries)
        For Each entry As String In entries
            Dim target As String = TargetOf(entry)
            Assert.True(ShortcutRoots.Any(Function(root) target.StartsWith(root, StringComparison.OrdinalIgnoreCase)),
                        script & " [InstallDelete] runs on every upgrade and may remove shortcuts only, but it names: " & entry)
        Next
    End Sub

    <Theory>
    <InlineData("FastMediaSorter.iss")>
    <InlineData("FastMediaSorterServer.iss")>
    Public Sub NoRegistryDeletionReachesTheSettingsHive(script As String)
        Dim iss As String = ReadScript(script)
        Dim deletions As Integer = 0
        For Each line As String In SectionLines(iss, "Code")
            If Regex.IsMatch(line, "\bRegDelete\w*\s*\(") Then
                deletions += 1
                Assert.True(RegistryDeletionTargets.Any(Function(target) line.Contains(target)),
                            script & " [Code] deletes a registry location other than associations and autostart: " & line)
            End If
            ' Reached through the command line instead of the Inno API it is the same deletion.
            Assert.False(Regex.IsMatch(line, "(?i)\breg(\.exe)?\b.*\bdelete\b"),
                         script & " [Code] runs reg delete: " & line)
        Next
        ' Both scripts remove the autostart value at least; zero means the scan above read nothing.
        Assert.True(deletions > 0, script & ": no RegDelete call found in [Code] - the section reader is blind")
        ' A [Registry] entry (none today) must never write into, or remove on uninstall, the settings hive.
        For Each line As String In SectionLines(iss, "Registry")
            Assert.DoesNotContain("SZA", line)
            Assert.DoesNotContain("VB and VBA Program Settings", line)
        Next
    End Sub

    <Theory>
    <InlineData("FastMediaSorter.iss")>
    <InlineData("FastMediaSorterServer.iss")>
    Public Sub TheUninstallRemovesDisposableCachesAndNeverTheUsersState(script As String)
        Dim entries As List(Of String) = SectionLines(ReadScript(script), "UninstallDelete")
        Assert.NotEmpty(entries)
        For Each entry As String In entries
            Dim target As String = TargetOf(entry)
            Dim kind As String = TypeOfEntry(entry)

            ' Data the machine or its paired phones depend on: never listed, not even by a parent folder.
            For Each forbidden As String In {"{commonappdata}", "{userappdata}", "{userdocs}", "FastMediaSorterCompanion", "FastMediaSorterExchange"}
                Assert.DoesNotContain(forbidden, target)
            Next

            If target.StartsWith(StateRoot & "\", StringComparison.OrdinalIgnoreCase) Then
                Dim folder As String = target.Substring(StateRoot.Length + 1)
                Assert.True(DisposableStateFolders.Contains(folder, StringComparer.OrdinalIgnoreCase),
                            script & " [UninstallDelete] removes a folder of the state root that is not a disposable cache: " & entry)
            ElseIf target.Equals(StateRoot, StringComparison.OrdinalIgnoreCase) OrElse target.Equals("{localappdata}\SZA", StringComparison.OrdinalIgnoreCase) Then
                ' The root goes only when nothing else is left in it.
                Assert.Equal("dirifempty", kind)
            End If
        Next
    End Sub

    <Theory>
    <InlineData("SerZhyAle.FastMediaSorter.installer.yaml")>
    <InlineData("SerZhyAle.FastMediaSorter.Server.installer.yaml")>
    Public Sub WingetUpgradesOverThePreviousInstallInsteadOfUninstallingIt(manifest As String)
        Dim manifestPath As String = If(manifest.Contains(".Server."),
                                        Combine("publishing", "winget", "server", manifest),
                                        Combine("publishing", "winget", manifest))
        Dim yaml As String = File.ReadAllText(manifestPath)
        ' uninstallPrevious would run the uninstaller first: the downloaded runtimes and models and the
        ' file associations go, and a failed install would leave the machine with nothing.
        Assert.DoesNotMatch("(?mi)^[^#\r\n]*UpgradeBehavior:\s*uninstallPrevious", yaml)
    End Sub

    ' --- reading the scripts -------------------------------------------------------------

    ''' <summary>The active lines of one section: no comment, no preprocessor directive, no blank line.</summary>
    Private Shared Function SectionLines(iss As String, section As String) As List(Of String)
        Dim result As New List(Of String)
        Dim inside As Boolean = False
        For Each raw As String In iss.Split(ControlChars.Lf)
            Dim line As String = raw.Trim()
            Dim header As Match = Regex.Match(line, "^\[(\w+)\]$")
            If header.Success Then
                inside = String.Equals(header.Groups(1).Value, section, StringComparison.OrdinalIgnoreCase)
            ElseIf inside AndAlso line.Length > 0 AndAlso Not line.StartsWith(";", StringComparison.Ordinal) _
                   AndAlso Not line.StartsWith("#", StringComparison.Ordinal) _
                   AndAlso Not line.StartsWith("//", StringComparison.Ordinal) Then
                result.Add(line)
            End If
        Next
        Return result
    End Function

    Private Shared Function TargetOf(entry As String) As String
        Dim m As Match = Regex.Match(entry, "Name:\s*""([^""]*)""")
        Assert.True(m.Success, "no Name: in the entry " & entry)
        Return m.Groups(1).Value
    End Function

    Private Shared Function TypeOfEntry(entry As String) As String
        Dim m As Match = Regex.Match(entry, "Type:\s*(\w+)")
        Assert.True(m.Success, "no Type: in the entry " & entry)
        Return m.Groups(1).Value
    End Function

    Private Shared Function ReadScript(name As String) As String
        Return File.ReadAllText(Combine("publishing", "installer", name))
    End Function

    Private Shared Function Combine(ParamArray parts As String()) As String
        Return Path.Combine({RepoRoot()}.Concat(parts).ToArray())
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
