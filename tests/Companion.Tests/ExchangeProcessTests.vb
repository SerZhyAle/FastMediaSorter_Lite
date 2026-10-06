Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' <see cref="ExchangeProcess"/> with the process launch replaced by a function (tickets 141/142 T-1): the
''' parsing of what the binary reports, the stdin/stdout contract of the password commands, the exit-code
''' mapping, and - the part a security reviewer reads first - that no plaintext reaches companion.log.
''' The real binary is driven by <see cref="ExchangeBinaryIntegrationTests"/>.
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
        Dim json As String = "{""dataDir"":""C:\\d"",""configPresent"":true,""passwordSet"":true,""passwordHashValid"":true," &
                             """certPresent"":true,""fingerprint"":""SHA256:abc"",""listen"":"":22022"",""portRange"":""40000-40100""}"
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.ParseInspect(json, 0)
        Assert.True(r.Answered)
        Assert.True(r.Configured)
        Assert.Equal(22022, r.Port)
        Assert.Equal("SHA256:abc", r.Fingerprint)
        Assert.Equal(Path.Combine("C:\d", "exchange.json"), r.ConfigPath)
        Assert.Equal("", r.ErrorText)
    End Sub

    <Fact>
    Public Sub A_failing_inspect_still_answers_and_a_broken_hash_is_not_configured()
        Dim json As String = "{""dataDir"":""D"",""passwordSet"":true,""passwordHashValid"":false,""error"":""bad""}"
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
        Assert.False(r.PasswordSet)
        Assert.Equal(0, r.Port)
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

    <Fact>
    Public Sub This_program_never_edits_the_config_file_itself()
        Dim scanned As Integer = 0
        For Each sourcePath As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = Path.GetFileName(sourcePath)
            If Not name.StartsWith("Exchange", StringComparison.Ordinal) AndAlso
               Not name.StartsWith("Share_Settings_Form.Exchange", StringComparison.Ordinal) Then Continue For
            scanned += 1
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(sourcePath))
            For Each forbidden As String In {"File.WriteAllText", "File.Create", "File.AppendAllText", "JsonNode", "Utf8JsonWriter", "JsonSerializer.Serialize"}
                Assert.False(code.Contains(forbidden), name & " writes or builds JSON itself (" & forbidden & ") - the port goes through set-listen")
            Next
        Next
        Assert.True(scanned >= 8, "Only " & scanned & " exchange sources scanned.")
    End Sub

    ' --- passwords ----------------------------------------------------------------------------

    <Fact>
    Public Sub The_password_travels_on_stdin_never_on_the_command_line()
        Dim seenArgs As String() = Nothing
        Dim seenStdin As String = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seenArgs = args
                                          seenStdin = stdin
                                          Return Result(0)
                                      End Function
        Dim r As ExchangeProcess.PasswordResult = ExchangeProcess.SetPassword("Abcdefghi1-secret")
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, r.Outcome)
        Assert.Equal({"set-password"}, seenArgs)
        Assert.Equal("Abcdefghi1-secret", seenStdin)
        Assert.DoesNotContain("secret", String.Join(" ", seenArgs))
        Assert.Equal("", r.Plaintext)
    End Sub

    <Theory>
    <InlineData(0, "Saved")>
    <InlineData(3, "PolicyRefused")>
    <InlineData(4, "ConfigProblem")>
    <InlineData(1, "Failed")>
    <InlineData(2, "Failed")>
    Public Sub Password_exit_codes_map_to_outcomes(code As Integer, expected As String)
        ExchangeProcess.RunOverride = Function(args, stdin) Result(code)
        Assert.Equal(expected, ExchangeProcess.SetPassword("Abcdefghi1").Outcome.ToString())
    End Sub

    <Fact>
    Public Sub A_timeout_or_a_launch_failure_is_a_failure_never_a_success()
        ExchangeProcess.RunOverride = Function(args, stdin) New ExchangeProcess.RunResult With {.Started = True, .TimedOut = True}
        Assert.Equal(ExchangeProcess.PasswordOutcome.Failed, ExchangeProcess.SetPassword("Abcdefghi1").Outcome)
        ExchangeProcess.RunOverride = Function(args, stdin) New ExchangeProcess.RunResult()
        Assert.Equal(ExchangeProcess.PasswordOutcome.Failed, ExchangeProcess.GeneratePassword().Outcome)
        Assert.False(ExchangeProcess.Inspect().Answered)
    End Sub

    <Fact>
    Public Sub Without_the_exe_nothing_runs()
        File.Delete(Path.Combine(_dir, ExchangeProcess.ExeName))
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          Throw New InvalidOperationException("must not run")
                                      End Function
        Assert.Equal(ExchangeProcess.PasswordOutcome.Unavailable, ExchangeProcess.SetPassword("Abcdefghi1").Outcome)
        Assert.Equal(ExchangeProcess.PasswordOutcome.Unavailable, ExchangeProcess.GeneratePassword().Outcome)
        Assert.Equal(ExchangeProcess.ListenOutcome.Unavailable, ExchangeProcess.SetListen(":2222"))
        Assert.Equal(ExchangeProcess.StartOutcome.Unavailable, ExchangeProcess.Start())
        Assert.False(ExchangeProcess.Inspect().Answered)
    End Sub

    <Fact>
    Public Sub A_generated_password_is_read_once_from_stdout_and_must_be_well_formed()
        ExchangeProcess.RunOverride = Function(args, stdin) Result(0, "Hq7Tm2Zk9Xv4Bn8Rc3Wd" & vbLf)
        Dim r As ExchangeProcess.PasswordResult = ExchangeProcess.GeneratePassword()
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, r.Outcome)
        Assert.Equal("Hq7Tm2Zk9Xv4Bn8Rc3Wd", r.Plaintext)

        Dim seen As String() = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seen = args
                                          Return Result(0, "Hq7Tm2Zk9Xv4Bn8Rc3Wd")
                                      End Function
        ExchangeProcess.GeneratePassword()
        Assert.Equal({"set-password", "--generate"}, seen)

        ' What is not one policy-conforming line is never shown.
        For Each bad As String In {"", "short1A", "Hq7Tm2Zk9Xv4" & vbLf & "second line", "alllowercase1234567"}
            ExchangeProcess.RunOverride = Function(args, stdin) Result(0, bad)
            Assert.Equal(ExchangeProcess.PasswordOutcome.Failed, ExchangeProcess.GeneratePassword().Outcome)
            Assert.Equal("", ExchangeProcess.GeneratePassword().Plaintext)
        Next
    End Sub

    ''' <summary>The security property of ticket 142: whatever the binary prints and whatever is typed, the
    ''' plaintext never reaches companion.log (the one file a person can send to the author).</summary>
    <Fact>
    Public Sub The_plaintext_never_reaches_companion_log()
        Const generated As String = "ZzGen9erated7Secret3Value"
        Const typed As String = "TypedBy4Hand8SecretValue"
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          If args.Contains("--generate") Then Return Result(0, generated & vbLf, "stderr " & generated)
                                          Return Result(3, "", "refused " & typed)
                                      End Function
        ExchangeProcess.GeneratePassword()
        ExchangeProcess.SetPassword(typed)
        ExchangeProcess.RunOverride = Function(args, stdin) Result(0, "{""dataDir"":""D""} " & generated)
        ExchangeProcess.Inspect()

        Dim logPath As String = AppFileLogger.LogFilePath
        If String.IsNullOrEmpty(logPath) OrElse Not File.Exists(logPath) Then Return
        Dim text As String
        Using fs As New FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Using reader As New StreamReader(fs)
                text = reader.ReadToEnd()
            End Using
        End Using
        Assert.DoesNotContain(generated, text)
        Assert.DoesNotContain(typed, text)
        ' ... while the verb and the exit code are there, which is what a bug report needs.
        Assert.Contains("Exchange server: set-password --generate - exit 0", text)
        Assert.Contains("Exchange server: set-password - exit 3", text)
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
