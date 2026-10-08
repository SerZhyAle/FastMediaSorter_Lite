Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' <see cref="ExchangeProcess"/> with the process launch replaced by a function (tickets 141/142 T-1, re-aligned to the
''' accounts model by 168): the parsing of what the binary reports (counts, never a login or a hash), the exit-code
''' mapping of the configuration verbs (the public address, the third-party ports, the certificate) and - the part a
''' security reviewer reads first - that nothing the binary prints reaches companion.log.
''' The real binary is driven by <see cref="ExchangeBinaryIntegrationTests"/>; the admin channel by
''' <see cref="ExchangeAdminTests"/>.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeProcessTests
    Implements IDisposable

    Private ReadOnly _dir As String

    Public Sub New()
        ExchangeTestSupport.Reset()
        _dir = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        ExchangeProcess.ExeDirectoryOverride = _dir
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ExchangeTestSupport.Reset()
        ExchangeTestSupport.DeleteQuietly(_dir)
    End Sub

    Private Shared Function Result(exitCode As Integer, Optional stdout As String = "", Optional stderr As String = "") As ExchangeProcess.RunResult
        Return New ExchangeProcess.RunResult With {.Started = True, .ExitCode = exitCode, .StdOut = stdout, .StdErr = stderr}
    End Function

    ' --- location ---------------------------------------------------------------------------

    <Fact>
    Public Sub The_component_is_available_exactly_when_the_exe_exists()
        Assert.True(ExchangeProcess.IsAvailable())
        Assert.EndsWith(ExchangeProcess.ExeName, ExchangeProcess.ExePath())
        File.Delete(Path.Combine(_dir, ExchangeProcess.ExeName))
        Assert.False(ExchangeProcess.IsAvailable())
    End Sub

    <Fact>
    Public Sub By_default_the_exe_is_resolved_under_exchange_beside_the_running_program()
        ExchangeProcess.ExeDirectoryOverride = Nothing
        Dim p As String = ExchangeProcess.ExePath()
        Assert.EndsWith(Path.Combine("exchange", "fms-exchange-server.exe"), p)
    End Sub

    ' --- what the binary reports ------------------------------------------------------------

    <Fact>
    Public Sub Inspect_json_is_parsed_into_the_report()
        Dim json As String = "{""dataDir"":""C:\\d"",""configPresent"":true,""accounts"":2,""devices"":5," &
                             """certPresent"":true,""certImported"":true,""fingerprint"":""SHA256:abc"",""listen"":"":22022"",""portRange"":""40000-40100""," &
                             """publicEndpoint"":""203.0.113.7:22022""}"
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.ParseInspect(json, 0)
        Assert.True(r.Answered)
        Assert.True(r.Configured)
        Assert.Equal(2, r.Accounts)
        Assert.Equal(5, r.Devices)
        Assert.True(r.CertImported)
        Assert.Equal("203.0.113.7:22022", r.PublicEndpoint)
        Assert.Equal("40000-40100", r.PortRange)
        Assert.Equal(22022, r.Port)
        Assert.Equal("SHA256:abc", r.Fingerprint)
        Assert.Equal(Path.Combine("C:\d", "exchange.json"), r.ConfigPath)
        Assert.Equal("", r.ErrorText)
    End Sub

    <Fact>
    Public Sub A_failing_inspect_still_answers_and_no_account_is_not_configured()
        Dim json As String = "{""dataDir"":""D"",""accounts"":0,""error"":""bad""}"
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.ParseInspect(json, 4)
        Assert.True(r.Answered)
        Assert.False(r.Configured)
        Assert.Equal("bad", r.ErrorText)
        Assert.Equal(4, r.ExitCode)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData("not json")>
    <InlineData("[1,2]")>
    <InlineData("{""dataDir"":")>
    Public Sub Text_that_is_not_a_json_object_is_not_an_answer(text As String)
        Assert.False(ExchangeProcess.ParseInspect(text, 0).Answered)
    End Sub

    <Fact>
    Public Sub Unknown_members_are_ignored_and_missing_ones_are_zero_values()
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.ParseInspect("{""future"":123}", 0)
        Assert.True(r.Answered)
        Assert.Equal(0, r.Accounts)
        Assert.False(r.Configured)
        Assert.Equal("", r.PublicEndpoint)
        Assert.False(r.CertImported)
        Assert.Equal(0, r.Port)
    End Sub

    <Theory>
    <InlineData("{""accounts"":-3}")>
    <InlineData("{""accounts"":""2""}")>
    <InlineData("{""accounts"":2.5}")>
    <InlineData("{""accounts"":99999999999}")>
    Public Sub A_count_that_is_not_a_plain_non_negative_integer_is_zero_never_an_exception(json As String)
        Assert.Equal(0, ExchangeProcess.ParseInspect(json, 0).Accounts)
    End Sub

    <Theory>
    <InlineData(":22022", 22022)>
    <InlineData("127.0.0.1:2222", 2222)>
    <InlineData("[::1]:8080", 8080)>
    <InlineData("", 0)>
    <InlineData("22022", 0)>
    <InlineData(":abc", 0)>
    <InlineData(":99999", 0)>
    Public Sub The_port_of_a_listen_endpoint(listen As String, expected As Integer)
        Assert.Equal(expected, ExchangeProcess.PortOfListen(listen))
    End Sub

    <Theory>
    <InlineData(":22022", 2222, ":2222")>
    <InlineData("127.0.0.1:22022", 2222, "127.0.0.1:2222")>
    <InlineData("[::1]:22022", 2222, "[::1]:2222")>
    <InlineData("", 2222, ":2222")>
    <InlineData("nonsense", 2222, ":2222")>
    Public Sub A_new_port_keeps_the_host_part(listen As String, port As Integer, expected As String)
        Assert.Equal(expected, ExchangeProcess.WithPort(listen, port))
    End Sub

    ' --- the listen port goes THROUGH the binary ----------------------------------------------

    <Theory>
    <InlineData(0, "Saved")>
    <InlineData(2, "BadAddress")>
    <InlineData(4, "ConfigProblem")>
    <InlineData(1, "Failed")>
    Public Sub Set_listen_maps_the_exit_code_and_hands_the_address_as_one_argument(code As Integer, expected As String)
        Dim seen As String() = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seen = args
                                          Return Result(code)
                                      End Function
        Assert.Equal(expected, ExchangeProcess.SetListen(":2222").ToString())
        Assert.Equal({"set-listen", ":2222"}, seen)
    End Sub

    ' --- the public address, the third-party ports and the certificate go THROUGH the binary (168) ---

    <Theory>
    <InlineData(0, "Saved")>
    <InlineData(2, "Refused")>
    <InlineData(4, "ConfigProblem")>
    <InlineData(1, "Failed")>
    Public Sub The_public_address_goes_through_set_endpoint_as_one_argument(code As Integer, expected As String)
        Dim seen As String() = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seen = args
                                          Return Result(code)
                                      End Function
        Assert.Equal(expected, ExchangeProcess.SetEndpoint("  203.0.113.7:22022 ").ToString())
        Assert.Equal({"set-endpoint", "203.0.113.7:22022"}, seen)
    End Sub

    <Fact>
    Public Sub An_empty_public_address_clears_it_with_the_dash_the_binary_knows()
        Dim seen As String() = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seen = args
                                          Return Result(0)
                                      End Function
        Assert.Equal(ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.SetEndpoint(""))
        Assert.Equal({"set-endpoint", "-"}, seen)
    End Sub

    <Fact>
    Public Sub The_third_party_range_goes_through_set_port_range()
        Dim seen As String() = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seen = args
                                          Return Result(0)
                                      End Function
        Assert.Equal(ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.SetPortRange("41000-41100"))
        Assert.Equal({"set-port-range", "41000-41100"}, seen)
        ExchangeProcess.RunOverride = Function(args, stdin) Result(2)
        Assert.Equal(ExchangeProcess.ConfigOutcome.Refused, ExchangeProcess.SetPortRange("22000-23000"))
    End Sub

    <Fact>
    Public Sub Replacing_the_certificate_runs_tls_replace_and_reads_the_new_fingerprint()
        Dim seen As String() = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seen = args
                                          Return Result(0, "fingerprint SHA256:AbCdEf" & vbLf)
                                      End Function
        Dim r As ExchangeProcess.CertificateResult = ExchangeProcess.ReplaceCertificate()
        Assert.Equal({"tls", "replace"}, seen)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Done, r.Outcome)
        Assert.Equal("SHA256:AbCdEf", r.Fingerprint)
    End Sub

    <Fact>
    Public Sub Importing_a_certificate_hands_the_two_paths_as_arguments_and_never_reads_the_key()
        Dim seen As String() = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seen = args
                                          Return Result(0, "fingerprint SHA256:Zz9" & vbLf)
                                      End Function
        Dim r As ExchangeProcess.CertificateResult = ExchangeProcess.ImportCertificate("C:\keys\site.crt", "C:\keys\site.key")
        Assert.Equal({"tls", "import", "--cert", "C:\keys\site.crt", "--key", "C:\keys\site.key"}, seen)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Done, r.Outcome)
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ExchangeProcess.vb"))
        Assert.DoesNotContain("File.ReadAll", code)
    End Sub

    <Theory>
    <InlineData("tls-refused: mismatch", "Mismatch")>
    <InlineData("tls-refused: expired", "Expired")>
    <InlineData("tls-refused: not-yet-valid", "NotYetValid")>
    <InlineData("tls-refused: unreadable", "Unreadable")>
    Public Sub A_refused_pair_is_named_by_the_one_stable_line_and_never_a_success(line As String, expected As String)
        Dim r As ExchangeProcess.CertificateResult = ExchangeProcess.CertificateResultOf(Result(2, "", line & vbLf))
        Assert.Equal(expected, r.Outcome.ToString())
        Assert.Equal("", r.Fingerprint)
    End Sub

    <Fact>
    Public Sub A_certificate_run_that_prints_no_fingerprint_or_does_not_run_is_a_failure()
        Assert.Equal(ExchangeProcess.CertificateOutcome.Failed, ExchangeProcess.CertificateResultOf(Result(0, "ok" & vbLf)).Outcome)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Failed, ExchangeProcess.CertificateResultOf(Result(1, "", "boom")).Outcome)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Failed, ExchangeProcess.CertificateResultOf(Result(2, "", "something else")).Outcome)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Failed, ExchangeProcess.CertificateResultOf(New ExchangeProcess.RunResult()).Outcome)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Failed, ExchangeProcess.CertificateResultOf(New ExchangeProcess.RunResult With {.Started = True, .TimedOut = True}).Outcome)
    End Sub

    ''' <summary>162: a UI never builds the config's JSON itself, it calls the binary's verbs. The admin channel's own request
    ''' (a different JSON, for the pipe) is the one named exception, and it writes no file.</summary>
    <Fact>
    Public Sub This_program_never_edits_the_config_file_itself()
        Dim scanned As Integer = 0
        For Each sourcePath As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = Path.GetFileName(sourcePath)
            If Not name.StartsWith("Exchange", StringComparison.Ordinal) AndAlso
               Not name.StartsWith("Share_Settings_Form.Exchange", StringComparison.Ordinal) Then Continue For
            scanned += 1
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(sourcePath))
            Dim forbidden As New List(Of String) From {"File.WriteAllText", "File.Create", "File.AppendAllText"}
            ' the admin channel builds ITS request in memory (never a file); nothing else builds JSON for the server
            If Not name.Equals("ExchangeAdmin.vb", StringComparison.Ordinal) Then forbidden.AddRange({"JsonNode", "Utf8JsonWriter", "JsonSerializer.Serialize"})
            For Each word As String In forbidden
                Assert.False(code.Contains(word), name & " writes or builds JSON itself (" & word & ") - every value goes through the binary's verbs")
            Next
        Next
        Assert.True(scanned >= 8, "Only " & scanned & " exchange sources scanned.")
    End Sub

    <Fact>
    Public Sub A_timeout_or_a_launch_failure_is_a_failure_never_a_success()
        ExchangeProcess.RunOverride = Function(args, stdin) New ExchangeProcess.RunResult With {.Started = True, .TimedOut = True}
        Assert.Equal(ExchangeProcess.ConfigOutcome.Failed, ExchangeProcess.SetEndpoint("203.0.113.7:22022"))
        Assert.Equal(ExchangeProcess.ConfigOutcome.Failed, ExchangeProcess.SetPortRange("41000-41100"))
        ExchangeProcess.RunOverride = Function(args, stdin) New ExchangeProcess.RunResult()
        Assert.Equal(ExchangeProcess.ConfigOutcome.Failed, ExchangeProcess.SetEndpoint("203.0.113.7:22022"))
        Assert.False(ExchangeProcess.Inspect().Answered)
    End Sub

    <Fact>
    Public Sub Without_the_exe_nothing_runs()
        File.Delete(Path.Combine(_dir, ExchangeProcess.ExeName))
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          Throw New InvalidOperationException("must not run")
                                      End Function
        Assert.Equal(ExchangeProcess.ConfigOutcome.Unavailable, ExchangeProcess.SetEndpoint("203.0.113.7:22022"))
        Assert.Equal(ExchangeProcess.ConfigOutcome.Unavailable, ExchangeProcess.SetPortRange("41000-41100"))
        Assert.Equal(ExchangeProcess.CertificateOutcome.Unavailable, ExchangeProcess.ReplaceCertificate().Outcome)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Unavailable, ExchangeProcess.ImportCertificate("a", "b").Outcome)
        Assert.Equal(ExchangeProcess.ListenOutcome.Unavailable, ExchangeProcess.SetListen(":2222"))
        Assert.Equal(ExchangeProcess.StartOutcome.Unavailable, ExchangeProcess.Start())
        Assert.False(ExchangeProcess.Inspect().Answered)
    End Sub

    ''' <summary>The data directory the admin pipe's name comes from is the binary's own answer, asked once and remembered briefly.</summary>
    <Fact>
    Public Sub The_data_directory_is_the_one_inspect_reports_and_is_remembered()
        Dim asked As Integer = 0
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          asked += 1
                                          Return Result(0, "{""dataDir"":""C:\\ProgramData\\FastMediaSorterExchange"",""accounts"":1}")
                                      End Function
        Assert.Equal("C:\ProgramData\FastMediaSorterExchange", ExchangeProcess.DataDir())
        Assert.Equal("C:\ProgramData\FastMediaSorterExchange", ExchangeProcess.DataDir())
        Assert.Equal(1, asked)
        ExchangeProcess.DataDirOverride = "C:\scratch"
        Assert.Equal("C:\scratch", ExchangeProcess.DataDir())
    End Sub

    ''' <summary>The security property of ticket 142, kept by 168: whatever the binary prints, it never reaches companion.log (the
    ''' one file a person can send to the author).</summary>
    <Fact>
    Public Sub Nothing_the_binary_prints_reaches_companion_log()
        Const printed As String = "ZzPrinted9Secret3Value"
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          If args(0) = "tls" Then Return Result(2, printed, "tls-refused: mismatch " & printed)
                                          Return Result(0, printed & vbLf, "stderr " & printed)
                                      End Function
        ExchangeProcess.SetEndpoint("203.0.113.7:22022")
        ExchangeProcess.SetPortRange("41000-41100")
        ExchangeProcess.ImportCertificate("a.crt", "a.key")
        ExchangeProcess.ReplaceCertificate()
        ExchangeProcess.RunOverride = Function(args, stdin) Result(0, "{""dataDir"":""D""} " & printed)
        ExchangeProcess.Inspect()

        Dim logPath As String = AppFileLogger.LogFilePath
        If String.IsNullOrEmpty(logPath) OrElse Not File.Exists(logPath) Then Return
        Dim text As String
        Using fs As New FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Using reader As New StreamReader(fs)
                text = reader.ReadToEnd()
            End Using
        End Using
        Assert.DoesNotContain(printed, text)
        ' ... while the verb and the exit code are there, which is what a bug report needs.
        Assert.Contains("Exchange server: set-endpoint - exit 0", text)
        Assert.Contains("Exchange server: tls import - exit 2", text)
    End Sub

    ' --- start / stop --------------------------------------------------------------------------

    <Fact>
    Public Sub Stopping_when_nothing_runs_says_so_and_touches_nothing()
        ' The fake exe is an empty file, so no process of that path exists.
        Assert.Equal(ExchangeProcess.StopOutcome.NotRunning, ExchangeProcess.StopServer())
        Assert.Equal(0, ExchangeProcess.RunningPid())
    End Sub

    <Fact>
    Public Sub Exit_with_no_server_started_by_this_session_stops_nothing_whatever_the_setting()
        Assert.Equal(ExchangeProcess.StopOutcome.NotRunning, ExchangeProcess.StopStartedOnExit(keepRunning:=False))
        Assert.Equal(ExchangeProcess.StopOutcome.NotRunning, ExchangeProcess.StopStartedOnExit(keepRunning:=True))
    End Sub

    <Fact>
    Public Sub The_settings_default_to_nothing_running_in_the_background()
        Dim s As New ExchangeSettings()
        Assert.False(s.KeepRunningOnExit)
        Assert.False(s.EverStarted)
        Assert.Equal(ExchangeSettings.UnsetPort, s.ListenPort)
    End Sub

    <Theory>
    <InlineData(-5, 0)>
    <InlineData(0, 0)>
    <InlineData(80, 1024)>
    <InlineData(22022, 22022)>
    <InlineData(70000, 65535)>
    Public Sub A_hand_edited_port_value_is_clamped(value As Integer, expected As Integer)
        Assert.Equal(expected, ExchangeSettings.ClampPort(value))
    End Sub

End Class
