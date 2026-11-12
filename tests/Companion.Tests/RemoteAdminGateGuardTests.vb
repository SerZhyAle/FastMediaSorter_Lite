Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-4, specification sections 5.10 and 7.6 ("Gate guard"): the remote administration client's gate
''' (<see cref="ExchangeRemoteAdminFeatures.Offered"/>) held against its evidence file in BOTH directions.
'''
''' The file <c>docs\specifications\remote-admin-gate-evidence.md</c> is line-anchored, because a guard that matched negative
''' sentences ("not passed") was a false green once. Only a line of the form
''' <c>EVIDENCE E1 PASS yyyy-mm-dd initials repo-relative-path</c> counts:
'''   * Offered = True needs exactly one such line for E1 and one for E2, a date that is not in the future and an existing
'''     evidence path;
'''   * Offered = False forbids any such line - evidence that says "passed" while the gate is shut is a stale file or a
'''     forgotten flip.
''' The same rule, with the same pattern, is read by <c>tools\Test-FeatureGates.ps1</c> for the release preflight, so a gate cannot
''' be open for the tag and shut for the suite. Nothing opens the gate from the ticket that built the client.
''' </summary>
Public Class RemoteAdminGateGuardTests

    Friend Const EvidenceFileName As String = "remote-admin-gate-evidence.md"

    ' One pattern for the whole rule; tools\Test-FeatureGates.ps1 carries the same text.
    Friend Shared ReadOnly PassLine As New Regex(
        "^EVIDENCE (?<id>E\d+) PASS (?<date>\d{4}-\d{2}-\d{2}) (?<who>\S+) (?<path>\S[^\r\n]*?)[ \t\r]*$",
        RegexOptions.Multiline Or RegexOptions.CultureInvariant)

    Private Shared Function GateSourcePath() As String
        Return Path.Combine(ExchangeTestSupport.CompanionDir(), "Core", "RemoteAdmin", "ExchangeRemoteAdminFeatures.vb")
    End Function

    Private Shared Function EvidencePath() As String
        Return Path.Combine(ExchangeTestSupport.RepoRoot(), "docs", "specifications", EvidenceFileName)
    End Function

    ''' <summary>The rule. Returns the defects found in <paramref name="text"/> (the evidence file, Nothing when it does not exist)
    ''' for a gate whose constant is <paramref name="offered"/>; empty means the gate and its evidence agree. A path is resolved
    ''' under <paramref name="root"/>; <paramref name="utcNow"/> is the clock (one day of allowance for the time zone).</summary>
    Friend Shared Function Defects(offered As Boolean, text As String, root As String, utcNow As DateTime) As List(Of String)
        Dim found As New List(Of String)()
        Dim lines As New List(Of Match)()
        If text IsNot Nothing Then
            For Each m As Match In PassLine.Matches(text)
                lines.Add(m)
            Next
        End If

        If Not offered Then
            If lines.Count > 0 Then
                found.Add("Offered is False but " & EvidenceFileName & " holds " & lines.Count.ToString(CultureInfo.InvariantCulture) &
                          " PASS line(s) (" & String.Join(", ", lines.Select(Function(hit) hit.Groups("id").Value)) &
                          "): evidence that says passed while the gate is shut is a stale file or a forgotten flip")
            End If
            Return found
        End If

        If text Is Nothing Then
            found.Add("Offered is True but docs\specifications\" & EvidenceFileName & " does not exist")
            Return found
        End If
        For Each id As String In New String() {"E1", "E2"}
            Dim count As Integer = 0
            For Each m As Match In lines
                If m.Groups("id").Value = id Then count += 1
            Next
            If count <> 1 Then
                found.Add("Offered is True and the evidence file needs exactly one PASS line for " & id & " (found " &
                          count.ToString(CultureInfo.InvariantCulture) & ")")
            End If
        Next
        For Each m As Match In lines
            Dim id As String = m.Groups("id").Value
            If id <> "E1" AndAlso id <> "E2" Then found.Add(id & ": only E1 and E2 are evidence ids of this gate")
            Dim stamp As DateTime
            If Not DateTime.TryParseExact(m.Groups("date").Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, stamp) Then
                found.Add(id & ": " & m.Groups("date").Value & " is not a calendar date")
            ElseIf stamp.Date > utcNow.Date.AddDays(1) Then
                found.Add(id & ": " & m.Groups("date").Value & " is in the future")
            End If
            Dim relative As String = m.Groups("path").Value
            If Path.IsPathRooted(relative) OrElse Regex.IsMatch(relative, "(^|[\\/])\.\.([\\/]|$)") Then
                found.Add(id & ": the evidence path must be repo-relative (not rooted, no ..): " & relative)
            ElseIf Not File.Exists(Path.Combine(root, relative)) AndAlso Not Directory.Exists(Path.Combine(root, relative)) Then
                found.Add(id & ": the evidence path does not exist: " & relative)
            End If
        Next
        Return found
    End Function

    ' ---------------------------------------------------------------- the gate against the real file

    <Fact>
    Public Sub The_constant_stays_a_compile_time_constant_and_the_source_says_what_the_class_says()
        Dim source As String = File.ReadAllText(GateSourcePath())
        Dim m As Match = Regex.Match(source, "(?m)^\s*Public Const Offered As Boolean = (True|False)\s*$")
        Assert.True(m.Success, "ExchangeRemoteAdminFeatures.Offered must stay a compile-time constant (Public Const Offered As Boolean = ..)")
        Assert.Equal(m.Groups(1).Value = "True", ExchangeRemoteAdminFeatures.Offered)
        Assert.Equal("FMS_EXCHANGE_REMOTE_ADMIN", ExchangeRemoteAdminFeatures.DeveloperSwitchName)
    End Sub

    <Fact>
    Public Sub The_gate_and_its_evidence_file_agree_in_both_directions()
        Dim evidenceFile As String = EvidencePath()
        Dim text As String = If(File.Exists(evidenceFile), File.ReadAllText(evidenceFile), Nothing)
        Dim problems As List(Of String) = Defects(ExchangeRemoteAdminFeatures.Offered, text, ExchangeTestSupport.RepoRoot(), DateTime.UtcNow)
        Assert.True(problems.Count = 0, "ExchangeRemoteAdminFeatures.Offered = " & ExchangeRemoteAdminFeatures.Offered.ToString() &
                    " disagrees with docs\specifications\" & EvidenceFileName & ": " & String.Join("; ", problems))
    End Sub

    <Fact>
    Public Sub The_evidence_file_exists_and_documents_the_line_format_of_both_ids()
        Dim evidenceFile As String = EvidencePath()
        Assert.True(File.Exists(evidenceFile), "docs\specifications\" & EvidenceFileName & " must exist even while the gate is shut: it is where the format is written")
        Dim text As String = File.ReadAllText(evidenceFile)
        Assert.Contains("EVIDENCE E1 PASS 2026-MM-DD", text)
        Assert.Contains("EVIDENCE E2 PASS 2026-MM-DD", text)
        ' the documented placeholders must not be PASS lines themselves, or a shut gate would be red for describing the format
        Assert.False(PassLine.IsMatch("EVIDENCE E1 PASS 2026-MM-DD <owner-initials> <repo-relative path of the run log or capture>"))
        Assert.True(PassLine.IsMatch("EVIDENCE E1 PASS 2026-10-01 SZ logs/run.log"))
    End Sub

    <Fact>
    Public Sub The_release_preflight_table_names_this_gate_its_source_and_its_evidence_file()
        Dim script As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.RepoRoot(), "tools", "Test-FeatureGates.ps1"))
        Assert.Contains("ExchangeRemoteAdminFeatures.Offered", script)
        Assert.Contains(EvidenceFileName, script)
        Assert.Contains("src\FastMediaSorterCompanion\Core\RemoteAdmin\ExchangeRemoteAdminFeatures.vb", script)
        Assert.True(File.Exists(GateSourcePath()), "the path the gate table names must be the real source")
        ' the pattern is the guard's and the script's alike: the same text, so the two cannot drift apart unseen
        Assert.Contains(PassLine.ToString(), script)
    End Sub

    ' ---------------------------------------------------------------- the rule, in both directions, on synthetic input

    Private Shared Function Scratch() As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-remote-admin-gate-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        File.WriteAllText(Path.Combine(dir, "run.log"), "binary-used sha256=00 bytes=1 version-line=x")
        Return dir
    End Function

    Private Shared Sub Cleanup(dir As String)
        Try
            Directory.Delete(dir, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Shared ReadOnly FixedNow As New DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)

    Private Const GoodE1 As String = "EVIDENCE E1 PASS 2026-10-01 SZ run.log"
    Private Const GoodE2 As String = "EVIDENCE E2 PASS 2026-10-02 SZ run.log"

    <Fact>
    Public Sub A_shut_gate_with_no_file_or_free_text_only_is_clean()
        Dim root As String = Scratch()
        Try
            Assert.Empty(Defects(False, Nothing, root, FixedNow))
            Assert.Empty(Defects(False, "", root, FixedNow))
            Assert.Empty(Defects(False, "E1 did not pass yet." & vbLf & "evidence e1 pass is pending" & vbLf &
                                 "EVIDENCE E1 PASS 2026-MM-DD <owner-initials> <path>" & vbLf, root, FixedNow))
            ' a sentence that merely contains the words, an indented line and a line with no path are not PASS lines
            Assert.Empty(Defects(False, "  " & GoodE1 & vbLf & "See: " & GoodE2 & vbLf & "EVIDENCE E1 PASS 2026-10-01 SZ" & vbLf, root, FixedNow))
        Finally
            Cleanup(root)
        End Try
    End Sub

    <Fact>
    Public Sub A_shut_gate_forbids_any_pass_line_even_a_complete_one()
        Dim root As String = Scratch()
        Try
            Assert.Single(Defects(False, GoodE1 & vbLf, root, FixedNow))
            Assert.Single(Defects(False, "intro" & vbLf & GoodE1 & vbLf & GoodE2 & vbLf, root, FixedNow))
            ' CRLF files count too
            Assert.Single(Defects(False, GoodE2 & vbCrLf & "free text" & vbCrLf, root, FixedNow))
            ' a PASS line for an unknown id is still a PASS line
            Assert.Single(Defects(False, "EVIDENCE E7 PASS 2026-10-01 SZ run.log" & vbLf, root, FixedNow))
        Finally
            Cleanup(root)
        End Try
    End Sub

    <Fact>
    Public Sub An_open_gate_with_no_file_or_no_pass_lines_is_a_defect()
        Dim root As String = Scratch()
        Try
            Assert.NotEmpty(Defects(True, Nothing, root, FixedNow))
            Assert.Equal(2, Defects(True, "", root, FixedNow).Count)
            Assert.Equal(2, Defects(True, "E1 and E2 passed, honest." & vbLf, root, FixedNow).Count)
        Finally
            Cleanup(root)
        End Try
    End Sub

    <Fact>
    Public Sub An_open_gate_with_both_lines_dated_in_the_past_and_an_existing_path_is_clean()
        Dim root As String = Scratch()
        Try
            Assert.Empty(Defects(True, "intro" & vbLf & GoodE1 & vbLf & GoodE2 & vbLf, root, FixedNow))
            ' CRLF, a path with a subfolder, a folder as the evidence, and today's date
            Directory.CreateDirectory(Path.Combine(root, "captures", "ac278"))
            Dim text As String = "EVIDENCE E1 PASS 2026-10-30 SZ run.log" & vbCrLf & "EVIDENCE E2 PASS 2026-10-30 SZ captures/ac278" & vbCrLf
            Assert.Empty(Defects(True, text, root, FixedNow))
            ' tomorrow in UTC is today somewhere east of it: allowed
            Assert.Empty(Defects(True, "EVIDENCE E1 PASS 2026-10-31 SZ run.log" & vbLf & GoodE2 & vbLf, root, FixedNow))
        Finally
            Cleanup(root)
        End Try
    End Sub

    <Fact>
    Public Sub An_open_gate_needs_exactly_one_line_per_id()
        Dim root As String = Scratch()
        Try
            Assert.Single(Defects(True, GoodE1 & vbLf, root, FixedNow))
            Assert.Single(Defects(True, GoodE2 & vbLf, root, FixedNow))
            Assert.Single(Defects(True, GoodE1 & vbLf & GoodE1 & vbLf & GoodE2 & vbLf, root, FixedNow))
            Assert.Single(Defects(True, GoodE1 & vbLf & GoodE2 & vbLf & "EVIDENCE E3 PASS 2026-10-02 SZ run.log" & vbLf, root, FixedNow))
        Finally
            Cleanup(root)
        End Try
    End Sub

    <Fact>
    Public Sub An_open_gate_refuses_a_future_date_an_impossible_date_a_missing_path_and_a_path_that_leaves_the_repository()
        Dim root As String = Scratch()
        Try
            Dim future As String = "EVIDENCE E1 PASS 2026-11-15 SZ run.log" & vbLf & GoodE2 & vbLf
            Assert.Contains("future", Assert.Single(Defects(True, future, root, FixedNow)))
            Dim impossible As String = "EVIDENCE E1 PASS 2026-13-45 SZ run.log" & vbLf & GoodE2 & vbLf
            Assert.Contains("calendar", Assert.Single(Defects(True, impossible, root, FixedNow)))
            Dim missing As String = "EVIDENCE E1 PASS 2026-10-01 SZ no-such.log" & vbLf & GoodE2 & vbLf
            Assert.Contains("does not exist", Assert.Single(Defects(True, missing, root, FixedNow)))
            Dim up As String = "EVIDENCE E1 PASS 2026-10-01 SZ ../run.log" & vbLf & GoodE2 & vbLf
            Assert.Contains("repo-relative", Assert.Single(Defects(True, up, root, FixedNow)))
            Dim rooted As String = "EVIDENCE E1 PASS 2026-10-01 SZ " & Path.Combine(root, "run.log") & vbLf & GoodE2 & vbLf
            Assert.Contains("repo-relative", Assert.Single(Defects(True, rooted, root, FixedNow)))
        Finally
            Cleanup(root)
        End Try
    End Sub

    ' ---------------------------------------------------------------- the switch and its test seam

    Private Shared Function Reader(value As String) As Func(Of String, String)
        Return Function(name)
                   Assert.Equal("FMS_EXCHANGE_REMOTE_ADMIN", name)
                   Return value
               End Function
    End Function

    <Fact>
    Public Sub The_developer_switch_is_the_value_1_and_nothing_else()
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader(Nothing)))
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader("")))
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader("0")))
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader("true")))
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader("11")))
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader("1 1")))
        Assert.True(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader("1")))
        Assert.True(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(Reader(" 1 ")))
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(DirectCast(Nothing, Func(Of String, String))))
    End Sub

    <Fact>
    Public Sub A_reader_that_throws_is_the_switch_off_not_an_error()
        Dim throwing As Func(Of String, String) =
            Function(n)
                Throw New ArgumentException("no machine scope here")
            End Function
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(throwing))
        Dim denied As Func(Of String, String) =
            Function(n)
                Throw New System.Security.SecurityException("denied")
            End Function
        Assert.False(ExchangeRemoteAdminFeatures.DeveloperSwitchOn(denied))
    End Sub

    <Fact>
    Public Sub With_the_gate_shut_the_surfaces_exist_only_through_the_own_switch()
        Using ExchangeRemoteAdminFeatures.ForFlow(switchOn:=False, offered:=False)
            Assert.False(ExchangeRemoteAdminFeatures.BuildOffers())
            Assert.False(ExchangeRemoteAdminFeatures.IsEnabled())
            Assert.False(ExchangeRemoteAdminFeatures.IsEnabled(Reader(Nothing)))
            Assert.True(ExchangeRemoteAdminFeatures.IsEnabled(Reader("1")))
            ' the other gates' switches do not light this one up (it is its own gate, spec 5.10)
            Dim otherGates As Func(Of String, String) =
                Function(name)
                    Return If(name = "FMS_EXCHANGE_CLIENT" OrElse name = "FMS_LIVE_BROADCAST", "1", Nothing)
                End Function
            Assert.False(ExchangeRemoteAdminFeatures.IsEnabled(otherGates))
        End Using
    End Sub

    <Fact>
    Public Sub The_flow_scope_decides_for_its_own_flow_and_nests()
        Using ExchangeRemoteAdminFeatures.ForFlow(switchOn:=True, offered:=False)
            Assert.True(ExchangeRemoteAdminFeatures.IsEnabled())
            Assert.False(ExchangeRemoteAdminFeatures.BuildOffers())
            Using ExchangeRemoteAdminFeatures.ForFlow(switchOn:=False, offered:=False)
                Assert.False(ExchangeRemoteAdminFeatures.IsEnabled())
            End Using
            Assert.True(ExchangeRemoteAdminFeatures.IsEnabled(), "the inner scope must restore the outer one")
        End Using
        Using ExchangeRemoteAdminFeatures.ForFlow(switchOn:=False, offered:=True)
            Assert.True(ExchangeRemoteAdminFeatures.BuildOffers())
            Assert.True(ExchangeRemoteAdminFeatures.IsEnabled())
            Assert.True(ExchangeRemoteAdminFeatures.IsEnabled(Reader(Nothing)))
        End Using
    End Sub

    <Fact>
    Public Sub The_flow_scope_of_one_thread_is_not_seen_by_another()
        Dim seenElsewhere As Boolean? = Nothing
        Using ExchangeRemoteAdminFeatures.ForFlow(switchOn:=False, offered:=Not ExchangeRemoteAdminFeatures.Offered)
            Dim other As New System.Threading.Thread(Sub() seenElsewhere = ExchangeRemoteAdminFeatures.BuildOffers())
            ' a thread started from this flow inherits it (that is how the UI tests run); a thread that is not started from it does not
            System.Threading.ExecutionContext.SuppressFlow()
            Try
                other.Start()
            Finally
                System.Threading.ExecutionContext.RestoreFlow()
            End Try
            Assert.True(other.Join(5000))
        End Using
        Assert.True(seenElsewhere.HasValue AndAlso seenElsewhere.Value = ExchangeRemoteAdminFeatures.Offered,
                    "a thread outside the flow must see the build's own value")
    End Sub

End Class
