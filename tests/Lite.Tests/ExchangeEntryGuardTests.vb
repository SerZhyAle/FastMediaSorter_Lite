#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 187 T-6 (rules 5, 10, 11, 12 and the dual-mode rule of 177): the gate and the honesty of the viewer's OWN exchange entries.
'''
'''   - no surface exists without <c>ExchangeFeatures.IsEnabled</c>: the section, the dialog, the dropped file and the pasted line;
'''   - the viewer is a complete client: the section and the dialog never name the Share Manager, its agent, its pipe or its locator;
'''   - the entries' library is reached only from the entries' own files, so nothing else of the viewer can open a connection for them;
'''   - no log call of an entry's file takes a server, a login, a fingerprint, a token or a device id (rule 11);
'''   - a machine with no entry reads, creates and connects to nothing.
''' </summary>
Public Class ExchangeEntryGuardTests

    Private Shared Function Root() As String
        Return ExchangeWireTestKit.RepoRoot()
    End Function

    Private Shared Function Source(relative As String) As String
        Return File.ReadAllText(Path.Combine(Root(), "src", relative.Replace("/"c, Path.DirectorySeparatorChar)))
    End Function

    ''' <summary>The code of a file with comments and string literals removed, so a scan reads what runs.</summary>
    Private Shared Function CodeOnly(text As String) As String
        Dim noStrings As String = Regex.Replace(text, """(?:[^""]|"""")*""", """""")
        Return String.Join(vbLf, noStrings.Split({vbLf}, StringSplitOptions.None).Select(Function(l)
                                                                                           Dim i As Integer = l.IndexOf("'"c)
                                                                                           Return If(i >= 0, l.Substring(0, i), l)
                                                                                       End Function))
    End Function

    ' ---------------------------------------------------------------- the gate

    <Fact>
    Public Sub Every_surface_of_the_entries_is_behind_the_gate()
        ' the section exists only while the features are offered, and has no other precondition
        Dim panel As String = Source("Exchange_Devices_Panel.vb")
        Dim offered As Integer = panel.IndexOf("Friend Function IsOffered() As Boolean", StringComparison.Ordinal)
        Assert.True(offered > 0)
        Assert.Contains("Return ExchangeFeatures.IsEnabled()", panel.Substring(offered, 200))
        Dim rebuild As Integer = panel.IndexOf("Friend Sub RebuildSources()", StringComparison.Ordinal)
        Assert.True(rebuild > 0)
        Assert.Contains("If(IsOffered(), _entries.Load()", panel.Substring(rebuild, 400))

        ' the dialog is opened through one door that reads the gate first
        Dim run As Integer = panel.IndexOf("Friend Shared Function Run(", StringComparison.Ordinal)
        Assert.True(run > 0)
        Assert.Contains("If Not ExchangeFeatures.IsEnabled() Then Return Nothing", panel.Substring(run, 300))

        ' the host builds the section only behind the gate
        Dim list As String = Source("Resource_List_Panel.vb")
        Dim built As Integer = list.IndexOf("devices = New Exchange_Devices_Panel(", StringComparison.Ordinal)
        Assert.True(built > 0)
        Assert.Contains("ExchangeFeatures.IsEnabled()", list.Substring(Math.Max(0, built - 400), 400))

        ' a dropped .fmsexch file and a pasted FMSEXCH line open the dialog only while the features are offered
        Dim open As String = Source("Main_Form.ResourceOpen.vb")
        Dim file As Integer = open.IndexOf(ChrW(34) & ".fmsexch" & ChrW(34), StringComparison.Ordinal)
        Assert.True(file > 0)
        Assert.Contains("ExchangeFeatures.IsEnabled()", open.Substring(Math.Max(0, file - 200), 200))
        Dim pasted As Integer = open.IndexOf("ExchangeConnectInput.LooksLikeConnectionString(text)", StringComparison.Ordinal)
        Assert.True(pasted > 0)
        Assert.Contains("ExchangeFeatures.IsEnabled()", open.Substring(Math.Max(0, pasted - 120), 120))
    End Sub

    ' ---------------------------------------------------------------- the viewer is complete

    <Fact>
    Public Sub The_viewers_entry_surfaces_never_reach_for_the_share_manager_or_its_agent()
        Dim files As String() = {"Exchange_Devices_Panel.vb", "Exchange_Connect_Form.vb", "Resources/ExchangeEntryApi.vb", "Resources/ExchangeViewerEntries.vb",
                                 "Resources/ExchangeEntryText.vb", "Exchange/ExchangeEntrySession.vb", "Exchange/ExchangeConsumer.vb", "Exchange/ExchangeConnectFlow.vb",
                                 "Exchange/ExchangeEntries.vb"}
        Dim banned As New Regex("\bExchangeAgentApi\b|ExchangeAgentLocator|\bExchangeAgentClient\b|ExchangeAgentPipe\.PipeName|FastMediaSorterCompanion\.exe|ExchangeDevicesFeed\.CreateDefault")
        For Each f As String In files
            Dim code As String = CodeOnly(Source(f))
            Dim m As Match = banned.Match(code)
            Assert.False(m.Success, f & " reaches the Share Manager's agent (" & m.Value & ") - the viewer is a complete client (187 rule 5)")
        Next
        ' the one remaining use of the agent's road is the older resources that no entry claims, and it is named in one place
        Dim users As New List(Of String)
        For Each f As String In Directory.GetFiles(Path.Combine(Root(), "src"), "*.vb", SearchOption.AllDirectories)
            If f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse f.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For
            If CodeOnly(File.ReadAllText(f)).Contains("ExchangeSync.CreateDefault") Then users.Add(Path.GetFileName(f))
        Next
        Assert.Equal({"ExchangeViewerEntries.vb"}, users.Distinct().OrderBy(Function(n) n).ToArray())
    End Sub

    <Fact>
    Public Sub Only_the_entries_own_files_open_a_session_for_an_entry()
        Dim allowed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "Exchange_Devices_Panel.vb", "Exchange_Connect_Form.vb", "ExchangeViewerEntries.vb", "ExchangeEntrySession.vb", "ExchangeConsumer.vb",
            "ExchangeConnectFlow.vb", "ExchangeEntries.vb", "ExchangeTunnel.vb", "HostingProbesLive.vb", "ExchangeEntryApi.vb", "ExchangeDevices.vb", "ExchangeEntryText.vb"}
        For Each f As String In Directory.GetFiles(Path.Combine(Root(), "src"), "*.vb", SearchOption.AllDirectories)
            If f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse f.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For
            If f.Contains(Path.DirectorySeparatorChar & "FastMediaSorterCompanion" & Path.DirectorySeparatorChar) Then Continue For
            Dim code As String = CodeOnly(File.ReadAllText(f))
            If Regex.IsMatch(code, "ExchangeEntryHub\b|ExchangeEntryStore\b|ExchangeConsumer\.|ExchangeViewerHub\b|ExchangeFollowSync\b") Then
                Dim name As String = Path.GetFileName(f)
                ' the main window opens the dialog and picks the sync of an attached resource; the resource list builds the section
                If name = "Main_Form.ResourceOpen.vb" OrElse name = "Resource_List_Panel.vb" OrElse name = "ResourceRegistry.vb" Then Continue For
                Assert.True(allowed.Contains(name), name & " reaches the entries' library outside the entries' own files")
            End If
        Next
    End Sub

    ' ---------------------------------------------------------------- rule 11

    <Fact>
    Public Sub No_log_call_of_an_entry_file_takes_a_server_a_login_a_fingerprint_a_token_or_an_id()
        Dim forbidden As New Regex("(?i)(\.server\b|\.login\b|login\b|fingerprint|\bpin\b|token|password|deviceid|\.endpoint|\.host\b|inputbox|loginbox)")
        Dim files As String() = {"Exchange_Devices_Panel.vb", "Exchange_Connect_Form.vb", "Resources/ExchangeEntryApi.vb", "Resources/ExchangeViewerEntries.vb",
                                 "Exchange/ExchangeEntrySession.vb", "Exchange/ExchangeConsumer.vb", "Exchange/ExchangeConnectFlow.vb", "Exchange/ExchangeEntries.vb"}
        Dim problems As New List(Of String)
        For Each f As String In files
            Dim lines As String() = Source(f).Split({vbLf}, StringSplitOptions.None)
            For i As Integer = 0 To lines.Length - 1
                Dim line As String = lines(i)
                If line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If Not (line.Contains("AppFileLogger.") OrElse line.Contains("Log.Invoke(") OrElse line.Contains(".Log(")) Then Continue For
                ' a statement may continue on the next lines
                Dim statement As String = line
                Dim j As Integer = i
                While statement.TrimEnd().EndsWith("_", StringComparison.Ordinal) OrElse statement.TrimEnd().EndsWith("&", StringComparison.Ordinal)
                    j += 1
                    If j >= lines.Length Then Exit While
                    statement &= lines(j)
                End While
                ' the text between the quotes is a fixed sentence; only what is concatenated in can carry a value
                Dim inCode As String = Regex.Replace(Regex.Replace(statement, """(?:[^""]|"""")*""", """"""), "ExchangeIds\.ForLog\([^)]*\)", "")
                If forbidden.IsMatch(inCode) Then problems.Add(f & ":" & (i + 1).ToString() & "  " & line.Trim())
            Next
        Next
        Assert.True(problems.Count = 0, "a log call that may carry a server, a login, a fingerprint, a token or an id:" & vbLf & String.Join(vbLf, problems))
    End Sub

    <Fact>
    Public Sub The_support_bundle_reports_numbers_of_entries_and_never_what_they_hold()
        Dim facts As New HostingReport.ClientFacts With {.Entries = 3, .WithToken = 2, .AutoAttach = 1}
        Dim text As String = HostingReport.DescribeClients(facts)
        Assert.Equal("3 (tokens present: 2; attaching new folders by themselves: 1)", text)
        Assert.Equal("none", HostingReport.DescribeClients(New HostingReport.ClientFacts()))
        ' the probe reads counts only: its body names no server, login, fingerprint or id
        Dim live As String = CodeOnly(Source("Support/HostingProbesLive.vb"))
        Dim at As Integer = live.IndexOf("Friend Function ReadClients()", StringComparison.Ordinal)
        Assert.True(at > 0)
        Dim body As String = live.Substring(at, live.IndexOf("End Function", at, StringComparison.Ordinal) - at)
        Assert.DoesNotMatch("(?i)\.(server|login|pin|deviceid|name)\b", body)
    End Sub

    ' ---------------------------------------------------------------- dual mode

    <Fact>
    Public Sub A_machine_with_no_entry_reads_creates_and_connects_to_nothing()
        Dim settings As New ExchangeStoresTests.MemorySettings()
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-noentry-" & Guid.NewGuid().ToString("N"))
        Dim store As New ExchangeEntryStore(settings, root, Nothing)
        Assert.Empty(store.Load())
        Assert.Equal(0, store.Count())
        Assert.Null(store.Find(ExchangeEntryStore.NewEntryId()))
        Assert.Null(store.FindByIdentity("203.0.113.5:22022", "alice"))
        Assert.False(store.Remove(ExchangeEntryStore.NewEntryId()))
        Assert.Equal(0, settings.Writes)
        Assert.False(Directory.Exists(root))
        Using hub As New ExchangeEntryHub(New ExchangeConsumerPorts With {.Entries = store})
            Assert.Null(hub.SessionFor(Nothing))
        End Using
        Assert.Equal(0, settings.Writes)
    End Sub

    <Fact>
    Public Sub The_value_the_entries_own_is_listed_so_a_dormant_run_can_assert_it_wrote_none_and_it_is_not_in_the_log_package()
        Assert.Equal({ExchangeEntryStore.ValueName}, ExchangeEntryStore.AllNames)
        Dim logPackage As String = Source("Support/LogPackage.vb")
        Assert.DoesNotContain(ExchangeEntryStore.ValueName, logPackage)
        Assert.DoesNotContain("Exchange_", logPackage.Substring(Math.Max(0, logPackage.IndexOf("SafeSettingNames", StringComparison.Ordinal))).Split({"End"}, StringSplitOptions.None)(0))
    End Sub

End Class
#End If
