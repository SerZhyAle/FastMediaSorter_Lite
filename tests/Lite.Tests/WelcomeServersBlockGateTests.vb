#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Spec 215 section 7 (the launcher half): the finish screen's installed-servers block only knocks. What it must
''' never do is start a server, run a companion flow, or send a folder - and what it sends must be the very string the
''' Share Manager parses. The window itself is hand-accepted (spec 215 T-5); these are the lines that keep its rules
''' true without one, read from the sources the same way <see cref="WelcomeWhereGateTests"/> reads the settings catalog.
''' </summary>
Public Class WelcomeServersBlockGateTests

    Private Shared Function SourceDir() As String
        Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing AndAlso Not File.Exists(Path.Combine(dir.FullName, "src", "Main_Form.Welcome.vb"))
            dir = dir.Parent
        End While
        Assert.NotNull(dir)
        Return Path.Combine(dir.FullName, "src")
    End Function

    Private Shared Function Code(relative As String) As String
        Return LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), relative)))
    End Function

    <Fact>
    Public Sub The_block_wakes_the_manager_through_the_same_entry_point_the_settings_button_uses_and_sends_no_folder()
        Dim host As String = Code("Main_Form.Welcome.vb")
        Dim body As Match = Regex.Match(host, "Friend Sub WelcomeOpenServerManager\b.*?End Sub", RegexOptions.Singleline)
        Assert.True(body.Success, "WelcomeOpenServerManager not found in Main_Form.Welcome.vb.")
        Assert.Contains("ActivateShareEntryPoint(managerOnly:=True", body.Value)

        ' The settings button's entry point is the very same one (OpenShareManagerWindow), so there is one door.
        Dim launcher As String = Code("Main_Form.ShareLauncher.vb")
        Dim settingsDoor As Match = Regex.Match(launcher, "Friend Sub OpenShareManagerWindow\b.*?End Sub", RegexOptions.Singleline)
        Assert.True(settingsDoor.Success)
        Assert.Contains("ActivateShareEntryPoint(managerOnly:=True", settingsDoor.Value)

        ' A wizard row never reads the viewed folder: the add-resource intent is a payload of its own.
        Assert.DoesNotContain("ResolveCurrentFolder", body.Value)
        Assert.Contains("addResource AndAlso sentFolder.Length = 0", launcher)
    End Sub

    <Fact>
    Public Sub Neither_wizard_file_starts_a_process_opens_a_pipe_or_asks_for_elevation()
        ' Rule 1 and rule 3 of spec 215: the probe is a name lookup, the act is the wake - nothing else.
        For Each relative As String In {"Welcome\Welcome_Form.vb", "Main_Form.Welcome.vb"}
            Dim text As String = Code(relative)
            For Each forbidden As String In {"Process.Start", "ProcessStartInfo", "EnableViaElevation", "ServerFeatures",
                                             "NamedPipeClientStream", "WorkerIpc", "new Mutex", "TcpListener", "netsh"}
                Assert.False(text.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0,
                             relative & " names '" & forbidden & "': the servers block may only report and knock (spec 215).")
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_intent_the_viewer_sends_is_the_string_the_Share_Manager_parses()
        Dim viewer As String = Code("Main_Form.ShareLauncher.vb")
        Dim companion As String = Code("FastMediaSorterCompanion\Program.vb")

        Dim sentCommand As Match = Regex.Match(viewer, "CompanionAddResourceCommand As String = ""([^""]+)""")
        Dim parsedCommand As Match = Regex.Match(companion, "AddResourceCommand As String = ""([^""]+)""")
        Assert.True(sentCommand.Success AndAlso parsedCommand.Success)
        Assert.Equal(parsedCommand.Groups(1).Value, sentCommand.Groups(1).Value)

        Dim sentFlag As Match = Regex.Match(viewer, "CompanionAddResourceFlag As String = ""([^""]+)""")
        Dim parsedFlag As Match = Regex.Match(companion, "AddResourceFlag As String = ""([^""]+)""")
        Assert.True(sentFlag.Success AndAlso parsedFlag.Success)
        Assert.Equal(parsedFlag.Groups(1).Value, sentFlag.Groups(1).Value)
    End Sub

    <Fact>
    Public Sub The_intent_is_gated_on_the_receivers_version_in_both_directions_of_the_wake()
        ' The viewer sends it only to a receiver known to understand it, and the Share Manager's own forwarder
        ' applies the same gate: the gate is one function, never a second copy.
        Dim viewer As String = Code("Main_Form.ShareLauncher.vb")
        Dim companion As String = Code("FastMediaSorterCompanion\Program.vb")
        Assert.Contains("ShareActivationPolicy.IsAddResourceCapable(receiver)", viewer)
        Assert.Contains("ShareActivationPolicy.IsAddResourceCapable(receiverVersion)", companion)
    End Sub

End Class

#End If
