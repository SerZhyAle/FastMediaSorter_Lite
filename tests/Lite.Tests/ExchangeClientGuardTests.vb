#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 180 T-5 and rules 1, 2 and 5: the gate of the exchange CLIENT features and the source scans that keep the client library honest.
'''
'''   - <c>ExchangeFeatures.Offered</c> is False until the owner opens the gate, and it can only become True together with an evidence file
'''     that names the rung it was opened at (204 section 3 item 3) - the shape of the guard of 159 section 3.4.
'''   - the developer switch FMS_EXCHANGE_CLIENT=1 shows the client features while the gate is shut, and nothing else does.
'''   - the net48 project lists none of the library (the x86 fallback gets none of this), and every library file is whole-file modern-only.
'''   - no log call of the client takes a token, a password, a pairing code, an `access` or a raw id (the 148 scan, extended): an id reaches
'''     a log only through ExchangeIds.ForLog.
'''   - the number 1 of the superseded wire is not in the client at all.
'''   - ticket 223 T-1: no client surface of EITHER program reads <c>ExchangeProcess</c> - the server-administration module
'''     of the Share Manager. The installed component is a shipping-gate marker, never a functional dependency of a client
'''     that joins a REMOTE server.
''' </summary>
Public Class ExchangeClientGuardTests

    Private Shared Function Root() As String
        Return ExchangeWireTestKit.RepoRoot()
    End Function

    Private Shared Function LibraryFiles() As String()
        Return Directory.GetFiles(Path.Combine(Root(), "src", "Exchange"), "*.vb")
    End Function

    ''' <summary>The library and the agent's own files (the server-administration files of the Share Manager are another ticket's).</summary>
    Private Shared Function ClientFiles() As List(Of String)
        Dim list As New List(Of String)(LibraryFiles())
        Dim core As String = Path.Combine(Root(), "src", "FastMediaSorterCompanion", "Core")
        If Directory.Exists(core) Then
            For Each f As String In Directory.GetFiles(core, "Exchange*.vb")
                Dim name As String = Path.GetFileName(f)
                If name.StartsWith("ExchangeDevice", StringComparison.Ordinal) OrElse name = "ExchangeControlSession.vb" Then list.Add(f)
            Next
        End If
        ' the client's windows: the sign-in dialog and the account group's partial (never the server-administration files of ticket 168)
        Dim forms As String = Path.Combine(Root(), "src", "FastMediaSorterCompanion", "Forms")
        For Each name As String In {"Exchange_Account_Form.vb", "Share_Settings_Form.ExchangeAccount.vb"}
            Dim f As String = Path.Combine(forms, name)
            If File.Exists(f) Then list.Add(f)
        Next
        Return list
    End Function

    <Fact>
    Public Sub The_agent_is_reached_only_through_the_gated_entries()
        ' every use of the host (the one place an agent is created) is in the tray's start / exit and in the account group; the host's own
        ' production methods read ExchangeFeatures first, so a shut gate constructs nothing
        ' ticket 168 section 3.7: ExchangeServerWizard.vb is the "use this PC as my server" wizard, which enrols THIS PC's own agent. Its production
        ' ports ask the host (GetOrCreate reads the gate, and the wizard asks ExchangeFeatures.IsEnabled itself before it probes or enrols anything)
        Dim allowed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "ExchangeDeviceAgent.vb", "TrayContext.vb", "Share_Settings_Form.ExchangeAccount.vb", "Share_Settings_Form.ExchangeAdmin.vb", "Exchange_Account_Form.vb", "ExchangeServerWizard.vb",
            "ExchangeLocalJoin.vb"}   ' 2026-10-12: the local join enrols THIS PC's agent in the server that runs on it; every entry asks ExchangeFeatures first, and GetOrCreate reads the gate
        ' ticket 170: reading ExchangeDeviceHost.Current (the export and the producer rows) never constructs an agent, so it is not a use of the host
        Dim src As String = Path.Combine(Root(), "src")
        Dim users As New List(Of String)
        For Each f As String In Directory.GetFiles(src, "*.vb", SearchOption.AllDirectories)
            If f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse f.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For
            Dim text As String = File.ReadAllText(f)
            If text.Replace("ExchangeDeviceHost.Current", "").Contains("ExchangeDeviceHost.") OrElse text.Contains("New ExchangeDeviceAgent(") Then users.Add(Path.GetFileName(f))
        Next
        Assert.NotEmpty(users)
        For Each u As String In users
            Assert.True(allowed.Contains(u), u & " reaches the device agent outside the gated entries")
        Next

        Dim host As String = File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "Core", "ExchangeDeviceAgent.vb"))
        Dim at As Integer = host.IndexOf("Public Shared Sub StartIfConfigured()", StringComparison.Ordinal)
        Assert.True(at > 0)
        Assert.Contains("ExchangeFeatures.IsEnabled()", host.Substring(at, 600))
        Dim getOrCreate As Integer = host.IndexOf("Public Shared Function GetOrCreate()", StringComparison.Ordinal)
        Assert.True(getOrCreate > 0)
        Assert.Contains("ExchangeFeatures.IsEnabled()", host.Substring(getOrCreate, 300))
        ' and the account group is built only behind the gate
        Dim group As String = File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "Forms", "Share_Settings_Form.ExchangeAccount.vb"))
        Dim create As Integer = group.IndexOf("Private Function CreateAccountSection()", StringComparison.Ordinal)
        Assert.True(create > 0)
        Assert.Contains("If Not ExchangeFeatures.IsEnabled() Then Return Nothing", group.Substring(create, 300))
    End Sub

    ' ---------------------------------------------------------------- the gate

    <Fact>
    Public Sub The_gate_is_shut_unless_an_evidence_file_names_the_rung_it_was_opened_at()
        Dim source As String = File.ReadAllText(Path.Combine(Root(), "src", "Exchange", "ExchangeFeatures.vb"))
        Dim m As Match = Regex.Match(source, "Public Const Offered As Boolean = (True|False)")
        Assert.True(m.Success, "ExchangeFeatures.Offered must stay a compile-time constant (Public Const Offered As Boolean = ..)")
        Dim offered As Boolean = m.Groups(1).Value = "True"
        Assert.Equal(offered, ExchangeFeatures.Offered)
        If Not offered Then Return

        Dim evidence As String = Path.Combine(Root(), "docs", "specifications", "exchange-gate-evidence.md")
        Assert.True(File.Exists(evidence),
                    "ExchangeFeatures.Offered is True but docs\specifications\exchange-gate-evidence.md does not exist. Opening the gate needs the owner's word and " &
                    "an evidence file naming the rung it was opened at (204 section 3 item 3).")
        Dim text As String = File.ReadAllText(evidence)
        Assert.Matches("(?i)rung\s*[2-5]", text)
        Assert.Matches("20\d\d-\d\d-\d\d", text)
    End Sub

    <Fact>
    Public Sub The_build_offers_the_clients_on_the_owners_word_of_2026_10_14()
        ' The test above allows True WITH evidence. This one states the present decision (ticket 223 T-3): the owner opened the exchange CLIENTS
        ' on 2026-10-14, ahead of the joint rung 2 run, so every install can join a remote exchange server with no server component. Taking it
        ' back is the constant and this line together - exchange-gate-evidence.md says how.
        Assert.True(ExchangeFeatures.Offered, "ticket 223 T-3 opened the exchange clients; closing them again is the owner's word, recorded in the evidence file")
        Assert.True(ExchangeFeatures.BuildOffers())
    End Sub

    Private Shared Function Reader(process As String, user As String, machine As String) As Func(Of String, EnvironmentVariableTarget, String)
        Return Function(name, target)
                   Assert.Equal("FMS_EXCHANGE_CLIENT", name)
                   Select Case target
                       Case EnvironmentVariableTarget.Process : Return process
                       Case EnvironmentVariableTarget.User : Return user
                       Case Else : Return machine
                   End Select
               End Function
    End Function

    <Fact>
    Public Sub The_developer_switch_is_the_value_1_in_the_process_user_or_machine_environment_and_nothing_else()
        Assert.False(ExchangeFeatures.DeveloperSwitchOn(Reader(Nothing, Nothing, Nothing)))
        Assert.False(ExchangeFeatures.DeveloperSwitchOn(Reader("", "", "")))
        Assert.False(ExchangeFeatures.DeveloperSwitchOn(Reader("0", "no", "true")))
        Assert.False(ExchangeFeatures.DeveloperSwitchOn(Reader("11", "01", "1 1")))
        Assert.True(ExchangeFeatures.DeveloperSwitchOn(Reader("1", Nothing, Nothing)))
        Assert.True(ExchangeFeatures.DeveloperSwitchOn(Reader(Nothing, "1", Nothing)))
        Assert.True(ExchangeFeatures.DeveloperSwitchOn(Reader(Nothing, Nothing, "1")))   ' the machine variable the first check sets on the test host
        Assert.True(ExchangeFeatures.DeveloperSwitchOn(Reader(Nothing, Nothing, " 1 ")))
        Assert.False(ExchangeFeatures.DeveloperSwitchOn(Nothing))
    End Sub

    <Fact>
    Public Sub With_the_gate_shut_the_features_exist_only_through_the_switch()
        ' the state of a build whose constant is False, set for this flow only
        Using ExchangeFeatures.ForFlow(offered:=False)
            Assert.False(ExchangeFeatures.BuildOffers())
            Assert.False(ExchangeFeatures.IsEnabled(Reader(Nothing, Nothing, Nothing)))
            Assert.True(ExchangeFeatures.IsEnabled(Reader(Nothing, Nothing, "1")))
        End Using
        ' and the build as it is: on with no switch at all
        Assert.True(ExchangeFeatures.IsEnabled(Reader(Nothing, Nothing, Nothing)))
    End Sub

    <Fact>
    Public Sub A_reader_that_throws_is_the_switch_off_not_an_error()
        Dim throwing As Func(Of String, EnvironmentVariableTarget, String) =
            Function(n, t)
                Throw New ArgumentException("no machine scope here")
            End Function
        Assert.False(ExchangeFeatures.DeveloperSwitchOn(throwing))
    End Sub

    ' ---------------------------------------------------------------- what is linked where

    <Fact>
    Public Sub The_x86_project_lists_none_of_the_client_library()
        Dim text As String = File.ReadAllText(Path.Combine(Root(), "src", "FastMediaSorter.vbproj"))
        Assert.DoesNotContain("Exchange\", text, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("\Exchange", text, StringComparison.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub Every_library_file_is_whole_file_modern_only()
        Dim files As String() = LibraryFiles()
        Assert.NotEmpty(files)
        For Each f As String In files
            Dim lines As String() = File.ReadAllLines(f).Where(Function(l) l.Trim().Length > 0).ToArray()
            Assert.True(lines.First().Trim() = "#If Not NETFRAMEWORK Then", Path.GetFileName(f) & " must open with #If Not NETFRAMEWORK Then")
            Assert.True(lines.Last().Trim() = "#End If", Path.GetFileName(f) & " must close with #End If")
        Next
    End Sub

    <Fact>
    Public Sub The_share_manager_links_the_whole_folder_and_the_viewer_gets_it_by_its_glob()
        Dim companion As String = File.ReadAllText(Path.Combine(Root(), "src", "FastMediaSorterCompanion", "FastMediaSorterCompanion.vbproj"))
        Assert.Contains("..\Exchange\*.vb", companion)
        Dim modern As String = File.ReadAllText(Path.Combine(Root(), "src", "Modern", "FastMediaSorter.Modern.vbproj"))
        Assert.Contains("Include=""..\**\*.vb""", modern)
        Assert.DoesNotContain("..\Exchange", modern.Replace("Include=""..\**\*.vb""", ""))   ' by the glob, never by a list
    End Sub

    <Fact>
    Public Sub No_library_file_holds_a_user_facing_string()
        ' the words belong to the program that shows them; the localization coverage tests scan these files for Cyrillic, and an
        ' English sentence here would reach a user untranslated
        For Each f As String In LibraryFiles()
            Dim text As String = File.ReadAllText(f)
            Assert.False(Regex.IsMatch(text, "\p{IsCyrillic}"), Path.GetFileName(f) & " holds a Cyrillic character")
            Assert.DoesNotContain("Localization.", text)
            Assert.DoesNotContain("MessageBox", text)
        Next
    End Sub

    ' ---------------------------------------------------------------- secrets and the old wire

    ''' <summary>The text of each logging statement (continuation lines joined), with its string literals and its ForLog arguments removed.</summary>
    Private Shared Iterator Function LoggingStatements(file As String) As IEnumerable(Of Tuple(Of Integer, String))
        Dim lines As String() = System.IO.File.ReadAllLines(file)
        Dim markers As String() = {"AppFileLogger.WriteLine(", "Log.Invoke(", "_options.Log(", "_ports.Log("}
        For i As Integer = 0 To lines.Length - 1
            Dim line As String = lines(i)
            Dim at As Integer = -1
            For Each marker As String In markers
                Dim p As Integer = line.IndexOf(marker, StringComparison.Ordinal)
                If p >= 0 Then at = If(at < 0, p, Math.Min(at, p))
            Next
            If at < 0 Then Continue For
            Dim sb As New StringBuilder(line.Substring(at))
            Dim j As Integer = i
            While (sb.ToString().Count(Function(c) c = "("c) > sb.ToString().Count(Function(c) c = ")"c) OrElse sb.ToString().TrimEnd().EndsWith("_", StringComparison.Ordinal)) AndAlso j + 1 < lines.Length
                j += 1
                sb.Append(" "c).Append(lines(j).Trim())
            End While
            Dim stmt As String = sb.ToString()
            stmt = Regex.Replace(stmt, """([^""]|"""")*""", """""")                        ' string literals
            Dim prev As String
            Do
                prev = stmt
                stmt = Regex.Replace(stmt, "ExchangeIds\.ForLog\([^()]*\)", "ID")         ' the one allowed way to name an id
            Loop While prev <> stmt
            Yield Tuple.Create(i + 1, stmt)
        Next
    End Function

    <Fact>
    Public Sub No_log_call_of_the_client_takes_a_secret_or_a_raw_id()
        Dim forbidden As New Regex("(?i)(token|password|pairingcode|accessjson|\.access\b|\bcode\b|deviceid|resourceid|shareid|broadcastid|tunnelid|fingerprint|\bpin\b)")
        Dim problems As New List(Of String)
        For Each f As String In ClientFiles()
            For Each t As Tuple(Of Integer, String) In LoggingStatements(f)
                If forbidden.IsMatch(t.Item2) Then problems.Add(Path.GetFileName(f) & ":" & t.Item1.ToString() & "  " & t.Item2)
            Next
        Next
        Assert.True(problems.Count = 0, "a log call that may carry a secret or a raw id (use ExchangeIds.ForLog for an id, and log no token, password, code or access):" & vbLf & String.Join(vbLf, problems))
    End Sub

    <Fact>
    Public Sub The_scan_itself_sees_a_log_call_that_takes_a_token()
        ' proof that the scan above is not blind: a synthetic file with the mistake is caught, one with the right form is not
        Dim path1 As String = Path.Combine(Path.GetTempPath(), "fms-logscan-" & Guid.NewGuid().ToString("N") & ".vb")
        Try
            System.IO.File.WriteAllText(path1, "AppFileLogger.WriteLine(""x "" & token)" & vbLf & "AppFileLogger.WriteLine(""y "" & ExchangeIds.ForLog(deviceId))" & vbLf &
                                        "_ports.Log.Invoke(""z "" &" & vbLf & "    creds.DeviceId)")
            Dim forbidden As New Regex("(?i)(token|deviceid)")
            Dim hits As List(Of Tuple(Of Integer, String)) = LoggingStatements(path1).Where(Function(t) forbidden.IsMatch(t.Item2)).ToList()
            Assert.Equal({1, 3}, hits.Select(Function(t) t.Item1).ToArray())
        Finally
            If System.IO.File.Exists(path1) Then System.IO.File.Delete(path1)
        End Try
    End Sub

    <Fact>
    Public Sub The_number_one_of_the_superseded_wire_is_not_in_the_client()
        For Each f As String In ClientFiles()
            Dim text As String = File.ReadAllText(f)
            Assert.False(Regex.IsMatch(text, "(?i)schemaVersion\W{0,6}[=:]\W{0,3}1\b"), Path.GetFileName(f) & " writes or reads schemaVersion 1")
            Assert.False(Regex.IsMatch(text, "(?i)LegacySchema|schemaVersion1"), Path.GetFileName(f) & " names a legacy schema")
        Next
    End Sub

    ' ---------------------------------------------------------------- ticket 223: the dependency split

    ''' <summary>The client surfaces of BOTH programs: the library and the agent (<see cref="ClientFiles"/>), the account
    ''' and connect windows, and the viewer's resource surfaces that read the gate.</summary>
    Private Shared Function ClientSurfacesOfBothPrograms() As List(Of String)
        Dim list As New List(Of String)(ClientFiles())
        Dim src As String = Path.Combine(Root(), "src")
        For Each name As String In {"Exchange_Connect_Form.vb", "Exchange_Devices_Panel.vb", "Resource_List_Panel.vb", "Main_Form.ResourceOpen.vb"}
            Dim f As String = Path.Combine(src, name)
            If File.Exists(f) Then list.Add(f)
        Next
        Dim resources As String = Path.Combine(src, "Resources")
        For Each name As String In {"ExchangeDevices.vb", "ExchangeTunnel.vb", "FmsTunnelPaths.vb"}
            Dim f As String = Path.Combine(resources, name)
            If File.Exists(f) Then list.Add(f)
        Next
        Return list
    End Function

    ''' <summary>Ticket 223 T-1: joining a REMOTE server is the client path, and it never reads the server-administration
    ''' module of the Share Manager. A client surface that reaches ExchangeProcess couples itself to a local server binary
    ''' it does not need - the day the gate opens for every install (223 T-3), the client must already be component-free.</summary>
    <Fact>
    Public Sub No_client_surface_reads_the_server_administration_module()
        Dim problems As New List(Of String)
        For Each f As String In ClientSurfacesOfBothPrograms()
            If File.ReadAllText(f).Contains("ExchangeProcess") Then problems.Add(Path.GetFileName(f))
        Next
        Assert.True(problems.Count = 0,
                    "a client surface references ExchangeProcess (the server-administration module of the Share Manager; the client of a remote server never reads it):" & vbLf & String.Join(vbLf, problems))
    End Sub

    <Fact>
    Public Sub The_split_scan_itself_sees_a_reference()
        ' proof the scan above is not blind: a line with the reference is caught, one without it is not
        Dim scanPath As String = Path.Combine(Path.GetTempPath(), "fms-split-scan-" & Guid.NewGuid().ToString("N") & ".vb")
        Try
            System.IO.File.WriteAllText(scanPath, "If ExchangeProcess.IsAvailable() Then Return Nothing" & vbLf &
                                        "If ExchangeFeatures.IsEnabled() Then Return Nothing")
            Dim lines As String() = System.IO.File.ReadAllLines(scanPath)
            Assert.True(lines(0).Contains("ExchangeProcess"), "the scan must see the reference")
            Assert.False(lines(1).Contains("ExchangeProcess"), "the scan must not see a clean line")
        Finally
            If System.IO.File.Exists(scanPath) Then System.IO.File.Delete(scanPath)
        End Try
    End Sub

End Class
#End If
