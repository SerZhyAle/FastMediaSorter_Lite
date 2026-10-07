#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Ticket 177 section 3 item 1, the half a unit run can hold: the COMMITTED capture of the modern viewer started
''' on a clean profile before any exchange client code existed. The capture itself is taken by
''' <c>tests\Integration\ExchangeBaseline.ps1</c> (hand-run: it starts the real exe); what runs here is the
''' proof that the committed capture is untouched (PROVENANCE first) and that it really is a capture of
''' NOTHING - no listener, no connection, no child process, no new file, no exchange value, no exchange word in
''' the log. A later change that wants a different baseline has to regenerate it with the tool, which is what
''' makes the change visible in review.
''' </summary>
Public Class DualModeViewerBaselineTests

    Private Shared Function ReadViewer() As JsonElement
        DualModeFixtures.VerifyProvenance()
        Dim doc As JsonDocument = JsonDocument.Parse(File.ReadAllText(DualModeFixtures.FixturePath("viewer-baseline.json")))
        Return doc.RootElement.Clone()
    End Function

    <Fact>
    Public Sub The_committed_provenance_matches_every_fixture()
        DualModeFixtures.VerifyProvenance()
    End Sub

    <Fact>
    Public Sub The_viewer_capture_is_a_real_clean_profile_run()
        Dim root As JsonElement = ReadViewer()
        Assert.Equal("viewer", root.GetProperty("program").GetString())
        Assert.True(root.GetProperty("captured").GetBoolean())
        Assert.StartsWith("clean", root.GetProperty("profile").GetString())
        Assert.True(root.GetProperty("secondsObserved").GetInt32() >= 8, "the run was too short to see a late connection")
        Assert.True(root.GetProperty("closedGracefully").GetBoolean(), "the viewer was killed, so its on-exit writes were not observed")
        Dim registry As JsonElement = root.GetProperty("registry")
        Assert.Empty(registry.GetProperty("valueNamesBefore").EnumerateArray().ToList())
        ' A clean first run writes plenty of settings; a capture that wrote none was not a real run.
        Assert.True(registry.GetProperty("added").GetArrayLength() > 20)
        Assert.True(root.GetProperty("log").GetProperty("present").GetBoolean())
    End Sub

    <Fact>
    Public Sub The_viewer_baseline_holds_no_listener_no_connection_no_child_and_no_new_file()
        Dim root As JsonElement = ReadViewer()
        Dim network As JsonElement = root.GetProperty("network")
        Assert.Equal(0, network.GetProperty("maxListeningOrUdpEndpoints").GetInt32())
        Assert.Equal(0, network.GetProperty("maxConnections").GetInt32())
        Assert.Equal(0, network.GetProperty("endpointKinds").GetArrayLength())
        Assert.Equal(0, root.GetProperty("childProcesses").GetArrayLength())
        Assert.Equal(0, root.GetProperty("files").GetProperty("created").GetArrayLength())
        Assert.Empty(root.GetProperty("registry").GetProperty("runKeyAdded").EnumerateArray().ToList())
    End Sub

    <Fact>
    Public Sub The_viewer_baseline_has_no_exchange_value_and_no_exchange_word_in_its_log()
        Dim root As JsonElement = ReadViewer()
        Dim registry As JsonElement = root.GetProperty("registry")
        For Each section As String In New String() {"valueNamesWhileRunning", "valueNamesAfterClose", "added"}
            For Each name As JsonElement In registry.GetProperty(section).EnumerateArray()
                Assert.False(DualModeFixtures.ExchangeValuePattern.IsMatch(name.GetString()),
                             "the baseline already holds an exchange value name in " & section & ": " & name.GetString())
            Next
        Next
        Assert.Equal(0, registry.GetProperty("forbiddenValueNames").GetArrayLength())
        Assert.False(root.GetProperty("log").GetProperty("mentionsExchange").GetBoolean())
    End Sub

    <Fact>
    Public Sub No_value_name_of_the_baseline_belongs_to_the_exchange_server_group_either()
        ' The server-side preferences of the Share Manager's own group (Exchange_KeepRunningOnExit and friends) are
        ' written only when somebody changes them in that group - a VIEWER start never writes one.
        Dim names As List(Of String) = ReadViewer().GetProperty("registry").GetProperty("added").EnumerateArray().
            Select(Function(e) e.GetString()).ToList()
        Assert.DoesNotContain(names, Function(n) n.StartsWith("Exchange", StringComparison.OrdinalIgnoreCase))
    End Sub

    <FactWhenFixture("sharemanager-baseline.json")>
    Public Sub The_Share_Manager_baseline_holds_nothing_either()
        DualModeFixtures.VerifyProvenance()
        Using doc As JsonDocument = JsonDocument.Parse(File.ReadAllText(DualModeFixtures.FixturePath("sharemanager-baseline.json")))
            Dim root As JsonElement = doc.RootElement
            Assert.True(root.GetProperty("captured").GetBoolean())
            Dim network As JsonElement = root.GetProperty("network")
            Assert.Equal(0, network.GetProperty("maxListeningOrUdpEndpoints").GetInt32())
            Assert.Equal(0, network.GetProperty("maxConnections").GetInt32())
            Assert.Equal(0, root.GetProperty("childProcesses").GetArrayLength())
            Assert.Equal(0, root.GetProperty("files").GetProperty("created").GetArrayLength())
            Assert.Equal(0, root.GetProperty("registry").GetProperty("forbiddenValueNames").GetArrayLength())
            Assert.False(root.GetProperty("log").GetProperty("mentionsExchange").GetBoolean())
        End Using
    End Sub

End Class
#End If
