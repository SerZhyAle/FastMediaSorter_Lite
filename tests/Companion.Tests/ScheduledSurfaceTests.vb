Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Where scheduled file operations live (ticket 222, section 0 and 8 "Gates"): in the Share Manager, and
''' nowhere else. The viewer carries a launcher and its two sentences; the engine's types are not in its sources.
''' </summary>
Public Class ScheduledSurfaceTests

    Private Shared Function RepositoryRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If Directory.Exists(Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")) AndAlso
               File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Nothing
    End Function

    ' --- the launcher command -------------------------------------------------------------------------

    <Fact>
    Public Sub The_schedule_command_carries_a_folder_or_nothing_and_never_becomes_a_share_proposal()
        Dim withFolder As CompanionCommand = CompanionCommand.Parse(CompanionCommand.ScheduleCommandPrefix & "D:\Photos")
        Assert.Equal(CompanionCommandKind.Schedule, withFolder.Kind)
        Assert.Equal("D:\Photos", withFolder.TargetPath)

        Dim bare As CompanionCommand = CompanionCommand.Parse(CompanionCommand.ScheduleCommandPrefix)
        Assert.Equal(CompanionCommandKind.Schedule, bare.Kind)
        Assert.Equal("", bare.TargetPath)

        ' A path that is not an acceptable folder is refused whole - not read as something else.
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(CompanionCommand.ScheduleCommandPrefix & "..\x").Kind)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(CompanionCommand.ScheduleCommandPrefix & "\\?\C:\x").Kind)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(CompanionCommand.ScheduleCommandPrefix & "relative").Kind)
        ' The prefix is exact and case-sensitive like its siblings.
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse("::FMS-SCHEDULE::D:\x").Kind)
    End Sub

    <Fact>
    Public Sub An_older_receiver_gets_the_plain_wake_never_the_unknown_payload()
        Dim payload As String = CompanionCommand.ScheduleCommandPrefix & "D:\Photos"
        Assert.Equal(Program.ShowWindowCommand, Program.CompatiblePayload(payload, Nothing))
        Assert.Equal(Program.ShowWindowCommand, Program.CompatiblePayload(payload, ShareActivationPolicy.MinAddResourceVersion))
        Assert.Equal(payload, Program.CompatiblePayload(payload, ShareActivationPolicy.MinScheduleVersion))
        Assert.Equal(payload, Program.CompatiblePayload(payload, New Version(26, 11, 1, 0)))
        Assert.False(ShareActivationPolicy.IsScheduleCapable(Nothing))
        Assert.True(ShareActivationPolicy.IsScheduleCapable(ShareActivationPolicy.MinScheduleVersion))
    End Sub

    ' --- the viewer is a launcher and nothing else ----------------------------------------------------

    ''' <summary>The engine's own types. The viewer's sources may not name them.</summary>
    Private Shared ReadOnly EngineTypes As String() = {
        "ScheduledOperation", "ScheduledEngine", "ScheduledStore", "ScheduledJournal", "ScheduledExecutor",
        "ScheduledSettings", "ScheduledHost", "ScheduledServices", "ScheduleMath", "ScheduledConditions",
        "ScheduledText", "Scheduled_Operation_Form", "Scheduled_History_Form"}

    <Fact>
    Public Sub The_viewer_names_no_engine_type()
        Dim root As String = RepositoryRoot()
        Assert.NotNull(root)
        Dim offenders As New List(Of String)()
        Dim pattern As New Regex("\b(?:" & String.Join("|", EngineTypes) & ")\b", RegexOptions.Compiled)
        For Each sourceFile As String In Directory.EnumerateFiles(Path.Combine(root, "src"), "*.vb", SearchOption.AllDirectories)
            If sourceFile.IndexOf(Path.DirectorySeparatorChar & "FastMediaSorterCompanion" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If sourceFile.IndexOf(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If sourceFile.IndexOf(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim text As String = File.ReadAllText(sourceFile)
            For Each match As Match In pattern.Matches(text)
                offenders.Add(Path.GetFileName(sourceFile) & ": " & match.Value)
            Next
        Next
        Assert.True(offenders.Count = 0, "The viewer names an engine type of scheduled operations: " & String.Join(", ", offenders.Distinct()))
    End Sub

    <Fact>
    Public Sub The_viewers_only_surface_is_the_launcher_item_and_its_wake()
        Dim root As String = RepositoryRoot()
        Dim menu As String = File.ReadAllText(Path.Combine(root, "src", "Main_Form.FolderMenu.vb"))
        Dim launcher As String = File.ReadAllText(Path.Combine(root, "src", "Main_Form.ShareLauncher.vb"))
        Assert.Contains("ActivateScheduleEntryPoint()", menu)
        Assert.Contains("CompanionScheduleCommandPrefix", launcher)
        ' The same constant the receiver parses (frozen once it ships).
        Dim sent As Match = Regex.Match(launcher, "CompanionScheduleCommandPrefix As String = ""([^""]+)""")
        Assert.True(sent.Success)
        Assert.Equal(CompanionCommand.ScheduleCommandPrefix, sent.Groups(1).Value)
        ' It travels only to a receiver that is known to understand it.
        Assert.Contains("ShareActivationPolicy.IsScheduleCapable(receiver)", launcher)
        ' No settings page, no list, no history in the viewer: the settings sources carry no such words.
        Dim settingsFiles As String() = Directory.GetFiles(Path.Combine(root, "src"), "Table_Form*.vb")
        For Each sourceFile As String In settingsFiles
            Assert.DoesNotContain("по расписанию", File.ReadAllText(sourceFile))
        Next
    End Sub

    ' --- the page is a page of the Share Manager's own settings ----------------------------------------

    <Fact>
    Public Sub The_feature_is_not_behind_the_share_opt_in()
        Dim root As String = RepositoryRoot()
        For Each name As String In {"Share_Settings_Form.Scheduled.vb", "Scheduled_Operation_Form.vb", "Scheduled_History_Form.vb"}
            Dim text As String = File.ReadAllText(Path.Combine(root, "src", "FastMediaSorterCompanion", "Forms", name))
            Assert.DoesNotContain("ServerFeatures", text)
        Next
        For Each sourceFile As String In Directory.GetFiles(Path.Combine(root, "src", "FastMediaSorterCompanion", "Core", "Scheduled"), "*.vb")
            Dim text As String = File.ReadAllText(sourceFile)
            Assert.DoesNotContain("ServerFeatures", text)
        Next
    End Sub

    <Fact>
    Public Sub Scheduled_operations_ship_only_with_the_share_component_of_the_user_installer()
        Dim root As String = RepositoryRoot()
        Dim text As String = File.ReadAllText(Path.Combine(root, "publishing", "installer", "FastMediaSorter.iss"))
        Dim written As New List(Of String)()
        For Each line As String In text.Split(ControlChars.Lf)
            Dim trimmed As String = line.TrimStart()
            If Not trimmed.StartsWith("Source:", StringComparison.Ordinal) Then Continue For
            If Not trimmed.Contains("FastMediaSorterCompanion.exe") Then Continue For
            If trimmed.Contains("Excludes:") Then
                ' The generic wildcard of the core component EXCLUDES the manager - it can never write it.
                Assert.Contains("Components: core", trimmed)
            Else
                written.Add(trimmed)
            End If
        Next
        ' The one line that writes the Share Manager - and with it every scheduled operation - is the share component's.
        Assert.Single(written)
        Assert.Contains("Components: share", written(0))
    End Sub
End Class
