Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Xunit
Imports fmsl.AppCommands

''' <summary>
''' Ticket L0315, contract APP-COMMANDS 0.1: the pure profile library the viewer's command line stands on -
''' the strict parser (rules 1 and 3), the frozen spellings that stay ordinary launches (rule 2), the first
''' instance's decision (rules 7, 10, 12, 13), the request and response lines (rule 7), the files (rules 5,
''' 9, 14), the log redaction (rule 16) and the generated documents (rules 18 and 20). The window and the
''' second process are hand checks of the release (their WM_COPYDATA leg needs two real processes).
''' </summary>
Public Class AppCommandsTests

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function Parse(ParamArray args As String()) As ParseRefusal
        Dim parsed As ParsedCommand = Nothing
        Return CommandLineParser.Parse(args, ViewerCommandTable.Build(), parsed)
    End Function

    Private Shared Function ParseOk(ParamArray args As String()) As ParsedCommand
        Dim parsed As ParsedCommand = Nothing
        Dim refusal As ParseRefusal = CommandLineParser.Parse(args, ViewerCommandTable.Build(), parsed)
        Assert.True(refusal Is Nothing, "refused: " & If(refusal Is Nothing, "", refusal.Detail))
        Return parsed
    End Function

    Private Shared Function WithTestVerb(risk As String) As CommandManifest
        Dim m As CommandManifest = ViewerCommandTable.Build()
        m.Commands.Add(New CommandSpec With {.Name = "test-read", .Summary = "test", .Synopsis = "test-read", .Risk = risk,
                                             .ColdStart = "refuse", .Since = "1.0"})
        Return m
    End Function

    Private Shared Function Req(command As String, Optional origin As String = "cli", Optional callerElevated As Boolean = False,
                                Optional schema As Integer = 1, Optional args As String() = Nothing) As CommandRequest
        Dim r As New CommandRequest With {.Command = command, .Origin = origin, .RequestId = Guid.NewGuid().ToString("D"),
                                          .CallerElevated = callerElevated, .SchemaVersion = schema}
        If args IsNot Nothing Then r.Args.AddRange(args)
        Return r
    End Function

    ''' <summary>The shortest argument list the parser accepts for a verb: its required options and values.</summary>
    Private Shared Function ValidArgsOf(spec As CommandSpec) As String()
        Dim result As New List(Of String)()
        ' Item R.4: the first member of each oneOf group stands in for the group.
        Dim chosen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each g As String() In spec.OneOf
            chosen.Add(g(0))
        Next
        For Each a As CommandArg In spec.Args
            If Not a.Required AndAlso Not chosen.Contains(a.Name) Then Continue For
            Dim sample As String = If(String.Equals(a.Kind, "int", StringComparison.Ordinal), "1", "x")
            If a.Values IsNot Nothing AndAlso a.Values.Length > 0 Then sample = a.Values(0)
            If a.Positional Then
                result.Add(sample)
            Else
                result.Add("--" & a.Name)
                If Not String.Equals(a.Kind, "bool", StringComparison.Ordinal) Then result.Add(sample)
            End If
        Next
        Return result.ToArray()
    End Function

    ' --- rules 18 and 20: one table, two generated documents ---------------------------------------

    Private Shared Function Normalized(path As String) As String
        Return File.ReadAllText(path, Encoding.UTF8).Replace(vbCrLf, vbLf)
    End Function

    <Fact>
    Public Sub Docs_cli_commands_json_equals_the_table_in_code()
        Dim onDisk As String = Normalized(Path.Combine(RepoRoot(), "docs", "cli-commands.json"))
        Assert.True(String.Equals(ViewerCommandTable.Build().ToJsonText(), onDisk, StringComparison.Ordinal),
                    "docs/cli-commands.json is stale: run pwsh -NoProfile -File tools/Build-CliDocs.ps1")
    End Sub

    <Fact>
    Public Sub Docs_CLI_md_equals_the_table_in_code()
        ' L0316: the file holds the viewer's document, then the Share Manager's section - both from their tables.
        Dim onDisk As String = Normalized(Path.Combine(RepoRoot(), "docs", "CLI.md"))
        Assert.True(String.Equals(CliDocument.Markdown(), onDisk, StringComparison.Ordinal),
                    "docs/CLI.md is stale: run pwsh -NoProfile -File tools/Build-CliDocs.ps1")
    End Sub

    <Fact>
    Public Sub Docs_share_manager_json_equals_the_table_in_code()
        Dim onDisk As String = Normalized(Path.Combine(RepoRoot(), "docs", "cli-commands.share-manager.json"))
        Assert.True(String.Equals(ShareManagerCommandTable.Build().ToJsonText(), onDisk, StringComparison.Ordinal),
                    "docs/cli-commands.share-manager.json is stale: run pwsh -NoProfile -File tools/Build-CliDocs.ps1")
    End Sub

    <Fact>
    Public Sub The_viewer_document_inside_CLI_md_is_unchanged_by_the_second_section()
        ' The viewer's own document stays the first part of the file, byte for byte.
        Dim onDisk As String = Normalized(Path.Combine(RepoRoot(), "docs", "CLI.md"))
        Assert.StartsWith(ViewerCommandTable.ToMarkdown(), onDisk, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub The_comparison_fails_on_a_changed_table()
        ' The negative control of the two facts above: a one-word change must be seen.
        Dim m As CommandManifest = ViewerCommandTable.Build()
        m.Commands(0).Summary &= " changed"
        Dim onDisk As String = Normalized(Path.Combine(RepoRoot(), "docs", "cli-commands.json"))
        Assert.False(String.Equals(m.ToJsonText(), onDisk, StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub The_manifest_is_valid_json_with_every_verb_and_rule_18_fields()
        Dim value As Object = Nothing
        Assert.True(CommandJson.TryParse(ViewerCommandTable.Build().ToJsonText(), value))
        Dim root As JsonObject = DirectCast(value, JsonObject)
        Assert.Equal(1, root.GetInt("schemaVersion", 0))
        Assert.Equal("FastMediaSorter_LITE.exe", root.GetString("exe"))
        Dim commandsValue As Object = Nothing
        Assert.True(root.TryGet("commands", commandsValue))
        Dim names As New List(Of String)()
        For Each c As Object In DirectCast(commandsValue, List(Of Object))
            Dim o As JsonObject = DirectCast(c, JsonObject)
            names.Add(o.GetString("name"))
            For Each field As String In {"name", "aliases", "summary", "synopsis", "args", "returns", "risk", "coldStart",
                                         "wait", "timeoutMs", "interactive", "idempotent", "exitCodes", "examples", "since"}
                Dim ignored As Object = Nothing
                Assert.True(o.TryGet(field, ignored), o.GetString("name") & " lacks " & field)
            Next
            Assert.Contains(o.GetString("coldStart"), {"start", "oneshot", "refuse"})
            Assert.InRange(o.GetInt("timeoutMs", 0), 1, CommandBounds.MaxCompletionMs)
            Dim expectedRisk As String = If(o.GetString("name").StartsWith("phone-code", StringComparison.Ordinal) OrElse
                                            o.GetString("name").StartsWith("stream-catalog", StringComparison.Ordinal),
                                            CommandRisk.WritesUserData, CommandRisk.Safe)
            ' L0318: reading what the window holds or shows is a rule-13 class above safe.
            If o.GetString("name") = "file current" OrElse o.GetString("name") = "screenshot window" Then expectedRisk = CommandRisk.ReadsUserData
            Assert.Equal(expectedRisk, o.GetString("risk"))
        Next
        Assert.Equal({"commands", "version", "status", "show", "quit", "open", "phone-code receive",
                      "stream-catalog build", "stream-catalog check",
                      "file current", "file next", "file previous", "file first", "file last", "file goto",
                      "slideshow start", "slideshow stop", "screenshot window"}, names.ToArray())
    End Sub

    <Fact>
    Public Sub Show_carries_the_frozen_aliases()
        Dim show As CommandSpec = ViewerCommandTable.Build().Find("show")
        Assert.Equal({"", "::fms-show-window::"}, show.Aliases.ToArray())
    End Sub

    ' --- rule 2: every old spelling stays an ordinary launch -----------------------------------------

    <Fact>
    Public Sub An_empty_command_line_is_not_a_command()
        Assert.False(ViewerCommandTable.IsCommandLine(New String() {}))
    End Sub

    <Fact>
    Public Sub The_show_window_token_is_not_a_command()
        Assert.False(ViewerCommandTable.IsCommandLine({"::fms-show-window::"}))
    End Sub

    <Fact>
    Public Sub The_NoBack_flag_with_a_path_is_not_a_command()
        Assert.False(ViewerCommandTable.IsCommandLine({"-NoBack", "C:\photos\a.jpg"}))
    End Sub

    <Fact>
    Public Sub A_drive_path_is_not_a_command()
        Assert.False(ViewerCommandTable.IsCommandLine({"C:\photos\a.jpg"}))
    End Sub

    <Fact>
    Public Sub A_network_path_is_not_a_command()
        Assert.False(ViewerCommandTable.IsCommandLine({"\\server\share\a.png"}))
    End Sub

    <Fact>
    Public Sub A_relative_file_name_with_an_extension_is_not_a_command()
        Assert.False(ViewerCommandTable.IsCommandLine({"a.jpg"}))
        Assert.False(ViewerCommandTable.IsCommandLine({".jpg"}))
    End Sub

    <Fact>
    Public Sub A_path_split_over_several_arguments_is_not_a_command()
        Assert.False(ViewerCommandTable.IsCommandLine({"C:\My", "Photos\a.jpg"}))
    End Sub

    <Fact>
    Public Sub A_reserved_first_word_wins_over_a_file_of_the_same_name_and_the_disk_is_never_probed()
        ' APP-COMMANDS 0.2 item A.1: a file called "status" in the working folder does not turn the verb into a path,
        ' because the answer takes no file-system input at all (the signature has no probe to ask).
        Assert.Equal(1, GetType(ViewerCommandTable).GetMethod("IsCommandLine", System.Reflection.BindingFlags.NonPublic Or System.Reflection.BindingFlags.Public Or System.Reflection.BindingFlags.Static).GetParameters().Length)
        Assert.True(ViewerCommandTable.IsCommandLine({"status"}))
        Assert.True(ViewerCommandTable.IsCommandLine({"STATUS"}))
        Assert.True(ViewerCommandTable.IsCommandLine({"file", "current"}))
        ' A first word no verb or alias begins is not a command: it stays the path it always was.
        Assert.False(ViewerCommandTable.IsCommandLine({"holiday"}))
        Assert.False(ViewerCommandTable.IsCommandLine({"no-such-word"}))
    End Sub

    <Fact>
    Public Sub The_reserved_first_words_are_the_verb_starts_and_nothing_else()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        For Each word As String In {"commands", "version", "status", "show", "quit", "open", "phone-code", "stream-catalog", "file", "slideshow", "screenshot"}
            Assert.True(CommandLineParser.IsReservedFirstWord(word, manifest), word)
        Next
        For Each word As String In {"photo", "current", "window", "a.jpg", "-NoBack", ""}
            Assert.False(CommandLineParser.IsReservedFirstWord(word, manifest), word)
        Next
        Assert.False(CommandLineParser.IsReservedFirstWord("status", Nothing))
    End Sub

    ' --- rules 1 and 3: the strict parser ------------------------------------------------------------

    <Fact>
    Public Sub An_unknown_verb_is_exit_10()
        Dim r As ParseRefusal = Parse("frobnicate")
        Assert.Equal(CommandReasons.UnknownVerb, r.Reason)
        Assert.Equal("frobnicate", r.Token)
        Assert.Equal(CommandExit.Usage, CommandReasons.ExitOf(r.Reason))
    End Sub

    <Fact>
    Public Sub An_unknown_option_is_exit_10()
        Dim r As ParseRefusal = Parse("status", "--frob")
        Assert.Equal(CommandReasons.BadArgument, r.Reason)
        Assert.Equal(CommandLineParser.DetailUnknownOption, r.Detail)
        Assert.Equal(CommandExit.Usage, CommandReasons.ExitOf(r.Reason))
    End Sub

    <Fact>
    Public Sub A_single_dash_switch_is_exit_10()
        Assert.Equal(CommandLineParser.DetailUnknownOption, Parse("status", "-json").Detail)
        Assert.Equal(CommandLineParser.DetailUnknownOption, Parse("status", "/json").Detail)
    End Sub

    <Fact>
    Public Sub A_bad_bool_value_is_exit_10()
        Assert.Equal(CommandLineParser.DetailBadValue, Parse("status", "--json=maybe").Detail)
    End Sub

    <Fact>
    Public Sub A_missing_option_value_is_exit_10()
        Assert.Equal(CommandLineParser.DetailMissingValue, Parse("status", "--result").Detail)
        Assert.Equal(CommandLineParser.DetailMissingValue, Parse("status", "--result", "--json").Detail)
    End Sub

    <Fact>
    Public Sub An_extra_value_is_exit_10()
        Dim r As ParseRefusal = Parse("quit", "now")
        Assert.Equal(CommandLineParser.DetailExtraValue, r.Detail)
        Assert.Equal(CommandExit.Usage, CommandReasons.ExitOf(r.Reason))
    End Sub

    <Fact>
    Public Sub An_argument_over_32_KiB_is_too_large()
        Dim r As ParseRefusal = Parse("status", "--result", New String("a"c, CommandBounds.MaxArgumentBytes + 1))
        Assert.Equal(CommandReasons.TooLarge, r.Reason)
        Assert.Equal(CommandExit.Usage, CommandReasons.ExitOf(r.Reason))
    End Sub

    <Fact>
    Public Sub A_request_over_64_KiB_is_too_large()
        Dim chunk As New String("a"c, 30000)
        Dim r As ParseRefusal = Parse("status", chunk, chunk, chunk)
        Assert.Equal(CommandReasons.TooLarge, r.Reason)
        Assert.Equal(CommandLineParser.DetailRequestTooLarge, r.Detail)
    End Sub

    <Fact>
    Public Sub Verbs_and_options_ignore_case_values_do_not()
        Dim p As ParsedCommand = ParseOk("STATUS", "--JSON", "--Result=C:\Out\R.json")
        Assert.Equal("status", p.Spec.Name)
        Assert.True(p.Flag("json"))
        Assert.Equal("C:\Out\R.json", p.Value("result"))
    End Sub

    <Fact>
    Public Sub An_option_value_may_follow_as_the_next_argument()
        Assert.Equal("out.json", ParseOk("status", "--result", "out.json").Value("result"))
    End Sub

    <Fact>
    Public Sub Every_base_verb_parses_bare()
        For Each verb As String In {"commands", "version", "status", "show", "quit"}
            Assert.Equal(verb, ParseOk(verb).Spec.Name)
        Next
    End Sub

    ' --- rules 7, 10, 12, 13: the first instance decides before anything runs ------------------------

    <Fact>
    Public Sub A_refused_request_carries_nothing_to_run()
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("status", args:={"--frob"}), ViewerCommandTable.Build(), True, False, False)
        Assert.Equal(CommandReasons.BadArgument, d.RefusalReason)
        Assert.Null(d.Parsed)
        Dim u As DispatchDecision = CommandDispatch.Decide(Req("frobnicate"), ViewerCommandTable.Build(), True, False, False)
        Assert.Equal(CommandReasons.UnknownVerb, u.RefusalReason)
        Assert.Null(u.Parsed)
    End Sub

    <Fact>
    Public Sub A_restricted_verb_is_switch_off_while_the_setting_is_off()
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("test-read"), WithTestVerb(CommandRisk.ReadsUserData), False, False, False)
        Assert.Equal(CommandReasons.SwitchOff, d.RefusalReason)
        Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(d.RefusalReason))
        Assert.Equal(CommandStatus.Refused, CommandReasons.StatusOf(d.RefusalReason))
    End Sub

    <Fact>
    Public Sub A_restricted_verb_runs_once_the_setting_is_on()
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("test-read"), WithTestVerb(CommandRisk.WritesUserData), True, False, False)
        Assert.Null(d.RefusalReason)
        Assert.NotNull(d.Parsed)
    End Sub

    <Fact>
    Public Sub A_claimed_sibling_origin_never_lifts_the_setting()
        ' Item T.1: a window message cannot prove its sender, so the origin on the line is read and dropped. The line is
        ' parsed the way the receiver parses it: whatever origin it claims, the request is a command line request.
        For Each risk As String In {CommandRisk.ReadsUserData, CommandRisk.WritesUserData, CommandRisk.LeavesMachine}
            Dim line As String = Req("test-read", origin:="sibling").ToLine()
            Assert.Contains("sibling", line)
            Dim parsed As CommandRequest = Nothing
            Assert.True(CommandRequest.TryParse(line, parsed))
            Assert.Equal(CommandOrigin.Cli, parsed.Origin)
            Dim d As DispatchDecision = CommandDispatch.Decide(parsed, WithTestVerb(risk), False, False, False)
            Assert.Equal(CommandReasons.SwitchOff, d.RefusalReason)
            Assert.Equal(CommandReasons.SwitchOff, CommandDispatch.Decide(Req("test-read", origin:="sibling"), WithTestVerb(risk), False, False, False).RefusalReason)
        Next
    End Sub

    <Fact>
    Public Sub A_claimed_sibling_origin_does_not_lift_the_setting_for_the_real_restricted_verbs()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        Dim checkedRestricted As Integer = 0
        For Each spec As CommandSpec In manifest.Commands
            If String.Equals(spec.Risk, CommandRisk.Safe, StringComparison.Ordinal) Then Continue For
            If spec.HasLegacyAlias Then Continue For
            checkedRestricted += 1
            Dim request As CommandRequest = Req(spec.Name, origin:="sibling", args:=ValidArgsOf(spec))
            Assert.Equal(CommandReasons.SwitchOff, CommandDispatch.Decide(request, manifest, False, False, False).RefusalReason)
        Next
        Assert.True(checkedRestricted >= 2, "file current and screenshot window are restricted verbs with no legacy alias")
    End Sub

    <Fact>
    Public Sub Legacy_lifts_only_the_setting_and_only_for_a_verb_with_a_legacy_alias()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        Dim receive As CommandSpec = manifest.Find("phone-code receive")
        Assert.True(receive.HasLegacyAlias)
        Dim legacyRequest As CommandRequest = Req("phone-code receive", args:={"--secret-file", "C:\t\c.txt"})
        legacyRequest.Legacy = True
        Assert.Null(CommandDispatch.Decide(legacyRequest, manifest, False, False, False).RefusalReason)
        ' It lifts rule 13 only: the parser, elevation and busy all still apply.
        Dim noArgs As CommandRequest = Req("phone-code receive")
        noArgs.Legacy = True
        Assert.Equal(CommandReasons.BadArgument, CommandDispatch.Decide(noArgs, manifest, False, False, False).RefusalReason)
        Assert.Equal(CommandReasons.Elevation, CommandDispatch.Decide(legacyRequest, manifest, False, False, True).RefusalReason)
        Assert.Equal(CommandReasons.Busy, CommandDispatch.Decide(legacyRequest, manifest, False, True, False).RefusalReason)
        ' A verb with no legacy alias takes no lift from the member.
        Dim shot As CommandSpec = manifest.Find("screenshot window")
        Assert.False(shot.HasLegacyAlias)
        Dim claim As CommandRequest = Req("screenshot window", args:=ValidArgsOf(shot))
        claim.Legacy = True
        Assert.Equal(CommandReasons.SwitchOff, CommandDispatch.Decide(claim, manifest, False, False, False).RefusalReason)
        ' The member rides the line only when true, and a line without it reads false.
        Dim plain As CommandRequest = Nothing
        Assert.True(CommandRequest.TryParse(Req("status").ToLine(), plain))
        Assert.False(plain.Legacy)
        Assert.DoesNotContain("legacy", Req("status").ToLine())
        Dim carried As CommandRequest = Nothing
        Assert.True(CommandRequest.TryParse(legacyRequest.ToLine(), carried))
        Assert.True(carried.Legacy)
    End Sub

    <Fact>
    Public Sub A_safe_verb_is_never_gated()
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("status"), ViewerCommandTable.Build(), False, False, False)
        Assert.Null(d.RefusalReason)
    End Sub

    <Fact>
    Public Sub A_busy_window_answers_busy_11()
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("show"), ViewerCommandTable.Build(), False, True, False)
        Assert.Equal(CommandReasons.Busy, d.RefusalReason)
        Assert.Equal(CommandExit.NotReady, CommandReasons.ExitOf(d.RefusalReason))
        Assert.Equal(CommandStatus.Busy, CommandReasons.StatusOf(d.RefusalReason))
    End Sub

    <Fact>
    Public Sub A_newer_schema_is_refused_with_exit_2()
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("status", schema:=2), ViewerCommandTable.Build(), True, False, False)
        Assert.Equal(CommandReasons.Schema, d.RefusalReason)
        Assert.Equal(CommandExit.NotProven, CommandReasons.ExitOf(d.RefusalReason))
    End Sub

    <Fact>
    Public Sub An_elevated_receiver_refuses_a_caller_that_is_not()
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("quit"), ViewerCommandTable.Build(), True, False, True)
        Assert.Equal(CommandReasons.Elevation, d.RefusalReason)
        Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(d.RefusalReason))
        Assert.Null(CommandDispatch.Decide(Req("quit", callerElevated:=True), ViewerCommandTable.Build(), True, False, True).RefusalReason)
    End Sub

    <Fact>
    Public Sub Every_reason_token_has_its_rule_12_status_and_exit()
        Dim expected As New Dictionary(Of String, Tuple(Of String, Integer)) From {
            {"unknown-verb", Tuple.Create("refused", 10)}, {"bad-argument", Tuple.Create("refused", 10)},
            {"too-large", Tuple.Create("refused", 10)}, {"schema", Tuple.Create("refused", 2)},
            {"switch-off", Tuple.Create("refused", 12)}, {"elevation", Tuple.Create("refused", 12)},
            {"busy", Tuple.Create("busy", 11)}, {"not-running", Tuple.Create("refused", 11)},
            {"target-missing", Tuple.Create("failed", 11)}, {"timeout", Tuple.Create("failed", 2)},
            {"cancelled", Tuple.Create("failed", 16)},
            {"error", Tuple.Create("failed", 1)}, {"no-space", Tuple.Create("failed", 13)},
            {"response-too-large", Tuple.Create("failed", 1)}}
        Assert.Equal(expected.Count, CommandReasons.All.Length)
        For Each reason As String In CommandReasons.All
            Assert.Equal(expected(reason).Item1, CommandReasons.StatusOf(reason))
            Assert.Equal(expected(reason).Item2, CommandReasons.ExitOf(reason))
        Next
    End Sub

    ' --- APP-COMMANDS 0.2 items I.3, L, M, N.1 ------------------------------------------------------

    <Fact>
    Public Sub An_answer_belongs_to_a_request_only_by_its_own_non_empty_request_id()
        Dim id As String = Guid.NewGuid().ToString("D")
        Assert.True(CommandResponse.Done(id, "ok").IsAnswerTo(id))
        Assert.True(CommandResponse.Done(id.ToUpperInvariant(), "ok").IsAnswerTo(id))
        Assert.False(CommandResponse.Done(String.Empty, "x").IsAnswerTo(id))
        Assert.False(CommandResponse.Done(Nothing, "x").IsAnswerTo(id))
        Assert.False(CommandResponse.Done(Guid.NewGuid().ToString("D"), "x").IsAnswerTo(id))
        ' Both callers read an answer that is not theirs as unreadable (exit 2) and never take its code.
        Dim client As String = SourceText("src", "AppCommands", "ViewerCommandClient.vb")
        Assert.Contains("response.IsAnswerTo(requestId)", client)
        Assert.Contains("CommandReasons.Schema, AppCommandsText.ForReason(CommandReasons.Schema)", client)
    End Sub

    <Fact>
    Public Sub A_response_line_over_64_KiB_fails_with_response_too_large_and_keeps_the_request_id()
        Dim id As String = Guid.NewGuid().ToString("D")
        Dim small As CommandResponse = CommandResponse.Done(id, "ok")
        Assert.Equal(small.ToLine(), small.ToBoundedLine("too large"))
        Dim data As New JsonObject()
        data.Add("blob", New String("x"c, CommandBounds.MaxResponseBytes))
        Dim big As CommandResponse = CommandResponse.Done(id, "ok", data)
        Dim line As String = big.ToBoundedLine("too large")
        Assert.True(Encoding.UTF8.GetByteCount(line) <= CommandBounds.MaxResponseBytes)
        Dim back As CommandResponse = Nothing
        Assert.True(CommandResponse.TryParse(line, back))
        Assert.Equal(id, back.RequestId)
        Assert.Equal(CommandStatus.Failed, back.Status)
        Assert.Equal(CommandReasons.ResponseTooLarge, back.Reason)
        Assert.Equal(CommandExit.Failed, back.ExitCode)
        Assert.Null(back.Data)
        ' The one place a reply is finished writes through the bound, in both programs.
        Assert.Contains("ToBoundedLine(", SourceText("src", "Main_Form.AppCommands.vb"))
        Assert.Contains("ToBoundedLine(", SourceText("src", "FastMediaSorterCompanion", "TrayContext.AppCommands.vb"))
    End Sub

    <Fact>
    Public Sub A_line_that_cannot_be_read_is_answered_only_into_the_own_reply_file()
        Dim reply As String = CommandFiles.ReplyPathFor(ViewerCommandTable.ReplyFolder, Guid.NewGuid().ToString("D"))
        Dim noId As New JsonObject()
        noId.Add("schemaVersion", 1)
        noId.Add("command", "status")
        noId.Add("replyPath", reply)
        Dim replyOut As String = Nothing
        Dim reason As String = Nothing
        Assert.True(CommandDispatch.TryAnswerUnreadable(CommandJson.ToLine(noId), ViewerCommandTable.ReplyFolder, replyOut, reason))
        Assert.Equal(reply, replyOut)
        Assert.Equal(CommandReasons.BadArgument, reason)
        ' Over the bound of rule 3: too-large.
        Dim fat As New JsonObject()
        fat.Add("replyPath", reply)
        fat.Add("padding", New String("x"c, CommandBounds.MaxRequestBytes))
        Assert.True(CommandDispatch.TryAnswerUnreadable(CommandJson.ToLine(fat), ViewerCommandTable.ReplyFolder, replyOut, reason))
        Assert.Equal(CommandReasons.TooLarge, reason)
        ' No answer: not JSON, no reply path, a path outside the reply folder, a name that is not a GUID, another program's folder.
        Assert.False(CommandDispatch.TryAnswerUnreadable("not json", ViewerCommandTable.ReplyFolder, replyOut, reason))
        Assert.False(CommandDispatch.TryAnswerUnreadable("{""command"":""status""}", ViewerCommandTable.ReplyFolder, replyOut, reason))
        Dim outside As New JsonObject()
        outside.Add("replyPath", "C:\Windows\Temp\" & Guid.NewGuid().ToString("D") & ".reply.json")
        Assert.False(CommandDispatch.TryAnswerUnreadable(CommandJson.ToLine(outside), ViewerCommandTable.ReplyFolder, replyOut, reason))
        Dim named As New JsonObject()
        named.Add("replyPath", CommandFiles.ReplyDirectory(ViewerCommandTable.ReplyFolder) & "\a-name.reply.json")
        Assert.False(CommandDispatch.TryAnswerUnreadable(CommandJson.ToLine(named), ViewerCommandTable.ReplyFolder, replyOut, reason))
        Assert.False(CommandDispatch.TryAnswerUnreadable(CommandJson.ToLine(noId), ShareManagerCommandTable.ReplyFolder, replyOut, reason))
        ' Both receivers answer such a line with an empty request id.
        Assert.Contains("TryAnswerUnreadable(", SourceText("src", "Main_Form.AppCommands.vb"))
        Assert.Contains("TryAnswerUnreadable(", SourceText("src", "FastMediaSorterCompanion", "TrayContext.vb"))
    End Sub

    <Fact>
    Public Sub Quit_takes_stop_jobs_as_a_bool_option_and_the_busy_refusal_names_it()
        Dim quit As CommandSpec = ViewerCommandTable.Build().Find("quit")
        Dim stopJobs As CommandArg = quit.FindOption("stop-jobs")
        Assert.NotNull(stopJobs)
        Assert.Equal("bool", stopJobs.Kind)
        Dim parsed As ParsedCommand = Nothing
        Assert.Null(CommandLineParser.Parse({"quit", "--stop-jobs"}, ViewerCommandTable.Build(), parsed))
        Assert.True(parsed.Flag("stop-jobs"))
        Dim handler As String = SourceText("src", "Main_Form.AppCommands.vb")
        Assert.Contains("parsed.Flag(""stop-jobs"")", handler)
        Assert.Contains("AppCommandsText.QuitWhileJobsRun()", handler)
        Assert.Contains("CancelBatch()", handler)
        Assert.Contains("--stop-jobs", SourceText("src", "AppCommands", "AppCommandsText.vb"))
    End Sub

    ' --- rule 7: the lines ---------------------------------------------------------------------------

    <Fact>
    Public Sub A_request_line_round_trips_and_has_no_line_break()
        Dim r As CommandRequest = Req("status", args:={"--result", "C:\out\""x"".json"})
        r.WorkingDir = "C:\work"
        r.ReplyPath = "C:\temp\x.reply.json"
        r.CallerPid = 4242
        Dim line As String = r.ToLine()
        Assert.DoesNotContain(vbLf, line)
        Assert.DoesNotContain(vbCr, line)
        Dim back As CommandRequest = Nothing
        Assert.True(CommandRequest.TryParse(line, back))
        Assert.Equal("status", back.Command)
        Assert.Equal({"--result", "C:\out\""x"".json"}, back.Args.ToArray())
        Assert.Equal(r.RequestId, back.RequestId)
        Assert.Equal(4242, back.CallerPid)
        Assert.Equal("C:\temp\x.reply.json", back.ReplyPath)
    End Sub

    <Fact>
    Public Sub A_malformed_request_is_not_a_request()
        Dim r As CommandRequest = Nothing
        Assert.False(CommandRequest.TryParse("{""schemaVersion"":1", r))
        Assert.False(CommandRequest.TryParse("{""schemaVersion"":1,""command"":""status""}", r))
        Assert.False(CommandRequest.TryParse("[1,2]", r))
        Assert.False(CommandRequest.TryParse(New String("["c, 100) & New String("]"c, 100), r))
    End Sub

    <Fact>
    Public Sub A_response_line_round_trips_and_omits_reason_on_done()
        Dim data As New JsonObject()
        data.Add("ready", True)
        Dim done As CommandResponse = CommandResponse.Done("id-1", "Ready.", data)
        done.Reason = CommandReasons.Busy
        Dim line As String = done.ToLine()
        Assert.DoesNotContain("""reason""", line)
        Dim back As CommandResponse = Nothing
        Assert.True(CommandResponse.TryParse(line, back))
        Assert.Equal(CommandStatus.Done, back.Status)
        Assert.True(back.Data.GetBool("ready", False))
    End Sub

    <Fact>
    Public Sub An_exit_code_outside_the_vocabulary_reads_as_failure()
        Dim back As CommandResponse = Nothing
        Assert.True(CommandResponse.TryParse("{""schemaVersion"":1,""requestId"":""x"",""status"":""failed"",""exitCode"":99}", back))
        Assert.Equal(CommandExit.Failed, back.ExitCode)
    End Sub

    ' --- files: the reply folder, rule 9, rule 14, rule 5 --------------------------------------------

    <Fact>
    Public Sub A_reply_path_is_accepted_only_in_the_reply_folder_and_named_by_the_id()
        Dim id As String = Guid.NewGuid().ToString("D")
        Dim good As New CommandRequest With {.RequestId = id, .ReplyPath = CommandFiles.ReplyPathFor(ViewerCommandTable.ReplyFolder, id)}
        Assert.True(CommandFiles.IsValidReplyPath(ViewerCommandTable.ReplyFolder, good))
        Dim elsewhere As New CommandRequest With {.RequestId = id, .ReplyPath = "C:\Windows\" & id & ".reply.json"}
        Assert.False(CommandFiles.IsValidReplyPath(ViewerCommandTable.ReplyFolder, elsewhere))
        Dim climbing As New CommandRequest With {.RequestId = id,
            .ReplyPath = Path.Combine(CommandFiles.ReplyDirectory(ViewerCommandTable.ReplyFolder), "..", id & ".reply.json")}
        Assert.False(CommandFiles.IsValidReplyPath(ViewerCommandTable.ReplyFolder, climbing))
        Dim notGuid As New CommandRequest With {.RequestId = "..\evil", .ReplyPath = CommandFiles.ReplyPathFor(ViewerCommandTable.ReplyFolder, "..\evil")}
        Assert.False(CommandFiles.IsValidReplyPath(ViewerCommandTable.ReplyFolder, notGuid))
    End Sub

    Private Shared Function ScratchFolder() As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-appcommands-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Return dir
    End Function

    <Fact>
    Public Sub A_result_file_is_written_whole_and_replaced_whole()
        Dim dir As String = ScratchFolder()
        Try
            Dim target As String = Path.Combine(dir, "r.json")
            Dim data As New JsonObject()
            data.Add("pid", 7)
            CommandFiles.WriteResult(target, data)
            CommandFiles.WriteResult(target, Nothing)
            Assert.Equal("{}" & vbLf, File.ReadAllText(target))
            Assert.Single(Directory.GetFiles(dir))
        Finally
            Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub An_out_file_is_kept_without_overwrite()
        Dim dir As String = ScratchFolder()
        Try
            Dim target As String = Path.Combine(dir, "o.bin")
            File.WriteAllText(target, "keep")
            Assert.False(CommandFiles.TryWriteOut(target, Encoding.ASCII.GetBytes("new"), False))
            Assert.Equal("keep", File.ReadAllText(target))
            Assert.True(CommandFiles.TryWriteOut(target, Encoding.ASCII.GetBytes("new"), True))
            Assert.Equal("new", File.ReadAllText(target))
        Finally
            Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub An_out_file_never_creates_a_missing_folder()
        ' CAPTURE-OUTPUT 0.8 section 5: a caller-named path is used exactly as given and its folder is never created.
        Dim dir As String = ScratchFolder()
        Try
            Dim missing As String = Path.Combine(dir, "not-there")
            Dim target As String = Path.Combine(missing, "o.png")
            Assert.Throws(Of DirectoryNotFoundException)(Sub() CommandFiles.TryWriteOut(target, Encoding.ASCII.GetBytes("new"), True))
            Assert.False(Directory.Exists(missing), "the out folder was created")
            Assert.Empty(Directory.GetFileSystemEntries(dir))
            ' The reply and --result files keep their own rule: those folders are the product's and are created.
            Dim result As String = Path.Combine(dir, "made", "r.json")
            CommandFiles.WriteResult(result, Nothing)
            Assert.True(File.Exists(result))
        Finally
            Directory.Delete(dir, True)
        End Try
        ' The receiver refuses before it captures anything, with the named reason, and has a catch for the late race.
        Dim drive As String = SourceText("src", "Main_Form.AppCommandsDrive.vb")
        Assert.Contains("Not Directory.Exists(outFolder)", drive)
        Assert.Contains("Catch ex As DirectoryNotFoundException", drive)
    End Sub

    <Fact>
    Public Sub A_secret_file_is_read_without_its_line_end_and_bounded()
        Dim dir As String = ScratchFolder()
        Try
            Dim target As String = Path.Combine(dir, "s.txt")
            File.WriteAllText(target, "p@ss wörd" & vbCrLf, New UTF8Encoding(False))
            Dim secret As String = Nothing
            Assert.True(CommandFiles.TryReadSecret(target, secret))
            Assert.Equal("p@ss wörd", secret)
            File.WriteAllText(target, New String("x"c, CommandBounds.MaxSecretBytes + 1))
            Assert.False(CommandFiles.TryReadSecret(target, secret))
            Assert.False(CommandFiles.TryReadSecret(Path.Combine(dir, "missing.txt"), secret))
        Finally
            Directory.Delete(dir, True)
        End Try
    End Sub

    ' --- rule 16: the log names the verb, never a value ----------------------------------------------

    <Fact>
    Public Sub The_log_line_keeps_option_names_and_drops_every_value()
        Dim r As CommandRequest = Req("status", args:={"--result", "C:\Users\secret-name\r.json", "--secret-file=C:\keys\k.txt", "--json"})
        r.CallerPid = 99
        Dim line As String = CommandDispatch.DescribeForLog(r, CommandResponse.Done(r.RequestId, "ok"))
        Assert.Contains("App command: status", line)
        Assert.Contains("origin=cli", line)
        Assert.Contains("caller=99", line)
        Assert.Contains("outcome=done", line)
        Assert.Contains("--result", line)
        Assert.Contains("--secret-file", line)
        Assert.DoesNotContain("secret-name", line)
        Assert.DoesNotContain("keys", line)
        Assert.Contains("values=2 redacted", line)
    End Sub

    ' --- ticket L0317: open, phone-code receive, stream-catalog build / check ----------------------------

    <Fact>
    Public Sub Open_takes_one_target_and_the_switch_on_either_side()
        Dim plain As ParsedCommand = ParseOk("open", "D:\Photos\2026")
        Assert.Equal("open", plain.Spec.Name)
        Assert.Equal("D:\Photos\2026", plain.Positionals.Single())
        Assert.False(plain.Flag("no-back"))
        Assert.True(ParseOk("open", "D:\Photos\2026", "--no-back").Flag("no-back"))
        Dim before As ParsedCommand = ParseOk("open", "--no-back", "D:\Photos\2026")
        Assert.True(before.Flag("no-back"))
        Assert.Equal("D:\Photos\2026", before.Positionals.Single())
        Assert.Equal("open", ParseOk("OPEN", "x.jpg").Spec.Name)
        Assert.Equal("\\nas\media\clip.mp4", ParseOk("open", "\\nas\media\clip.mp4").Positionals.Single())
        Assert.Equal("smb://nas/media", ParseOk("open", "smb://nas/media").Positionals.Single())
    End Sub

    <Fact>
    Public Sub Open_without_a_target_or_with_two_is_exit_10()
        Dim missing As ParseRefusal = Parse("open")
        Assert.Equal(CommandLineParser.DetailMissingArgument, missing.Detail)
        Assert.Equal("target", missing.Token)
        Assert.Equal(CommandExit.Usage, CommandReasons.ExitOf(missing.Reason))
        Assert.Equal(CommandLineParser.DetailExtraValue, Parse("open", "a.jpg", "b.jpg").Detail)
        Assert.Equal(CommandLineParser.DetailUnknownOption, Parse("open", "a.jpg", "--frob").Detail)
    End Sub

    <Fact>
    Public Sub Open_takes_the_target_or_target_file_and_never_both()
        ' APP-COMMANDS 0.2 items H.2 and R.4.
        Dim fromFile As ParsedCommand = ParseOk("open", "--target-file", "C:\temp\address.txt")
        Assert.Empty(fromFile.Positionals)
        Assert.Equal("C:\temp\address.txt", fromFile.Value("target-file"))
        Dim both As ParseRefusal = Parse("open", "smb://nas/media", "--target-file", "C:\temp\address.txt")
        Assert.Equal(CommandLineParser.DetailExtraValue, both.Detail)
        Assert.Equal(CommandExit.Usage, CommandReasons.ExitOf(both.Reason))
    End Sub

    <Fact>
    Public Sub The_manifest_marks_the_open_target_sensitive_and_names_the_oneOf_group()
        Dim open As CommandSpec = ViewerCommandTable.Build().Find("open")
        Dim json As String = CommandJson.ToLine(open.ToJson())
        Assert.Contains("""sensitive"":true", json)
        Assert.Contains("""oneOf"":[[""target"",""target-file""]]", json)
        Assert.Contains("--target-file", open.Synopsis)
        Assert.Contains("Exactly one of `<target>`, `--target-file` is given.", ViewerCommandTable.ToMarkdown())
    End Sub

    <Fact>
    Public Sub A_target_read_from_a_file_takes_the_place_of_the_option_and_never_reaches_the_log()
        Dim parsed As ParsedCommand = ParseOk("open", "--target-file", "C:\temp\address.txt", "--no-back")
        CommandLineParser.InlineFileValue(parsed, "target-file", "smb://user:secret@nas/media")
        Assert.Equal("smb://user:secret@nas/media", parsed.Positionals.Single())
        Assert.Equal(New String() {"--no-back", "--", "smb://user:secret@nas/media"}, parsed.RawArgs.ToArray())
        ' The first instance parses the rewritten request to the same command.
        Dim again As ParsedCommand = Nothing
        Dim lineArgs As New List(Of String) From {"open"}
        lineArgs.AddRange(parsed.RawArgs)
        Assert.Null(CommandLineParser.Parse(lineArgs.ToArray(), ViewerCommandTable.Build(), again))
        Assert.Equal("smb://user:secret@nas/media", again.Positionals.Single())
        Assert.True(again.Flag("no-back"))
        Dim line As String = CommandDispatch.DescribeForLog(Req("open", args:=parsed.RawArgs.ToArray()),
                                                           CommandResponse.Done(Guid.NewGuid().ToString("D"), "done"))
        Assert.DoesNotContain("secret", line)
        Assert.DoesNotContain("nas", line)
    End Sub

    <Fact>
    Public Sub The_inline_form_of_target_file_is_removed_too()
        Dim parsed As ParsedCommand = ParseOk("open", "--target-file=C:\temp\address.txt")
        CommandLineParser.InlineFileValue(parsed, "target-file", "D:\Photos")
        Assert.Equal(New String() {"--", "D:\Photos"}, parsed.RawArgs.ToArray())
        Assert.False(parsed.Options.ContainsKey("target-file"))
    End Sub

    <Fact>
    Public Sub The_first_instance_answers_a_request_that_still_carries_target_file_as_a_bad_argument()
        ' The sender reads the file; a request that arrives with the option and no target has none (source check).
        Dim receiver As String = SourceText("src", "Main_Form.AppCommands.vb")
        Assert.Contains("If(parsed.Positionals.Count > 0, parsed.Positionals(0), String.Empty)", receiver)
        Dim client As String = SourceText("src", "AppCommands", "ViewerCommandClient.vb")
        Assert.Contains("CommandLineParser.InlineFileValue(parsed, ""target-file""", client)
    End Sub

    <Fact>
    Public Sub The_old_spellings_of_open_are_not_commands_and_read_the_same()
        Assert.False(ViewerCommandTable.IsCommandLine({"C:\a\b.jpg"}))
        Assert.False(ViewerCommandTable.IsCommandLine({"-NoBack", "C:\a\b.jpg"}))
        Assert.False(ViewerCommandTable.IsCommandLine({"C:\a\b.jpg", "-NoBack"}))
        Assert.False(ViewerCommandTable.IsCommandLine({"-noback"}))
        Assert.Equal({"<path>", "-NoBack <path>", "<path> -NoBack"}, ViewerCommandTable.Build().Find("open").Aliases.ToArray())
        Dim noBack As Boolean
        Assert.Equal("C:\a b\c.jpg", OpenTargets.SplitLegacyArgument("-NoBack C:\a b\c.jpg", noBack))
        Assert.True(noBack)
        Assert.Equal("C:\a b\c.jpg", OpenTargets.SplitLegacyArgument("C:\a b\c.jpg -noback", noBack))
        Assert.True(noBack)
        Assert.Equal("C:\a b\c.jpg", OpenTargets.SplitLegacyArgument("C:\a b\c.jpg", noBack))
        Assert.False(noBack)
        ' What the verb hands the one entry is exactly what a bare launch of the same spelling handed it.
        Assert.Equal("C:\a b\c.jpg", OpenTargets.ToLegacyArgument("C:\a b\c.jpg", False))
        Assert.Equal("C:\a b\c.jpg -NoBack", OpenTargets.ToLegacyArgument("C:\a b\c.jpg", True))
        Assert.Equal("C:\a b\c.jpg", OpenTargets.SplitLegacyArgument(OpenTargets.ToLegacyArgument("C:\a b\c.jpg", True), noBack))
        Assert.True(noBack)
    End Sub

    <Fact>
    Public Sub An_open_target_is_told_apart_before_the_disk_is_touched()
        Assert.Equal(OpenTargetClass.Internal, OpenTargets.Classify("::fms-phone-code::abc"))
        Assert.Equal(OpenTargetClass.Internal, OpenTargets.Classify("::fms-show-window::"))
        Assert.Equal(OpenTargetClass.Address, OpenTargets.Classify("sftp://u@host:22/p"))
        Assert.Equal(OpenTargetClass.Address, OpenTargets.Classify("gd://account/folder"))
        Assert.Equal(OpenTargetClass.Address, OpenTargets.Classify("https://host/a.mp4"))
        Assert.Equal(OpenTargetClass.Document, OpenTargets.Classify("D:\x\a.fmscfg"))
        Assert.Equal(OpenTargetClass.Document, OpenTargets.Classify("a.FMSEXCH"))
        Assert.Equal(OpenTargetClass.Document, OpenTargets.Classify("D:\x\live.fmsbcast"))
        Assert.Equal(OpenTargetClass.Path, OpenTargets.Classify("D:\x\a.jpg"))
        Assert.Equal(OpenTargetClass.Path, OpenTargets.Classify("C:\folder"))
        Assert.Equal(OpenTargetClass.Path, OpenTargets.Classify("C://folder"))
        Assert.Equal(OpenTargetClass.Path, OpenTargets.Classify("\\nas\share\x.png"))
        Assert.Equal(OpenTargetClass.Empty, OpenTargets.Classify(""))
        Assert.Equal(OpenTargetClass.Empty, OpenTargets.Classify("   "))
    End Sub

    <Fact>
    Public Sub A_relative_open_target_is_the_callers_not_the_viewers()
        Assert.Equal("C:\work\pics\a.jpg", OpenTargets.ResolveAgainst("C:\work", "pics\a.jpg"))
        Assert.Equal("D:\x\a.jpg", OpenTargets.ResolveAgainst("C:\work", "D:\x\a.jpg"))
        Assert.Equal("sftp://u@h/p", OpenTargets.ResolveAgainst("C:\work", "sftp://u@h/p"))
        Assert.Null(OpenTargets.ResolveAgainst("C:\work", ""))
        Assert.Null(OpenTargets.ResolveAgainst("C:\work", "   "))
    End Sub

    <Fact>
    Public Sub Open_is_safe_starts_the_viewer_and_is_never_gated()
        Dim spec As CommandSpec = ViewerCommandTable.Build().Find("open")
        Assert.Equal(CommandRisk.Safe, spec.Risk)
        Assert.Equal("start", spec.ColdStart)
        Assert.True(spec.Idempotent)
        Assert.True(CommandDispatch.IsAdmittedByRisk(spec, False, False))
        Dim d As DispatchDecision = CommandDispatch.Decide(Req("open", args:={"C:\x"}), ViewerCommandTable.Build(), False, False, False)
        Assert.Null(d.RefusalReason)
        Assert.Equal("C:\x", d.Parsed.Positionals.Single())
        Dim busy As DispatchDecision = CommandDispatch.Decide(Req("open", args:={"C:\x"}), ViewerCommandTable.Build(), False, True, False)
        Assert.Equal(CommandReasons.Busy, busy.RefusalReason)
    End Sub

    <Fact>
    Public Sub Phone_code_receive_reads_a_file_and_is_gated_only_from_a_command_line()
        Dim parsed As ParsedCommand = ParseOk("phone-code", "receive", "--secret-file", "C:\t\code.txt")
        Assert.Equal("phone-code receive", parsed.Spec.Name)
        Assert.Equal("C:\t\code.txt", parsed.Value("secret-file"))
        Assert.Equal(CommandLineParser.DetailMissingArgument, Parse("phone-code", "receive").Detail)
        Assert.Equal("--secret-file", Parse("phone-code", "receive").Token)
        Assert.Equal(CommandReasons.UnknownVerb, Parse("phone-code").Reason)
        Dim spec As CommandSpec = parsed.Spec
        Assert.Equal(CommandRisk.WritesUserData, spec.Risk)
        Assert.Equal("start", spec.ColdStart)
        Assert.False(spec.Idempotent)
        Assert.Equal({"::fms-phone-code::", "::fms-phone-code-file::"}, spec.Aliases.ToArray())
        Dim cli As DispatchDecision = CommandDispatch.Decide(Req("phone-code receive", args:={"--secret-file", "C:\t\c.txt"}),
                                                             ViewerCommandTable.Build(), False, False, False)
        Assert.Equal(CommandReasons.SwitchOff, cli.RefusalReason)
        Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(cli.RefusalReason))
        ' The Share Manager's relay uses the shipped window-message spellings, never a claimed origin: a line that says
        ' "sibling" is held like any other, and the setting on admits a command line.
        Assert.Equal(CommandReasons.SwitchOff, CommandDispatch.Decide(Req("phone-code receive", "sibling", args:={"--secret-file", "C:\t\c.txt"}),
                                                                      ViewerCommandTable.Build(), False, False, False).RefusalReason)
        Assert.Null(CommandDispatch.Decide(Req("phone-code receive", args:={"--secret-file", "C:\t\c.txt"}),
                                           ViewerCommandTable.Build(), True, False, False).RefusalReason)
        ' The path of a code file is a value: the log keeps the option name and drops it.
        Dim line As String = CommandDispatch.DescribeForLog(Req("phone-code receive", args:={"--secret-file", "C:\private\code.txt"}),
                                                            CommandResponse.Done("x", "ok"))
        Assert.Contains("--secret-file", line)
        Assert.DoesNotContain("private", line)
    End Sub

    <Fact>
    Public Sub Stream_catalog_build_and_check_parse_with_their_options()
        Dim build As ParsedCommand = ParseOk("stream-catalog", "build", "D:\lists", "--out", "D:\z.zip", "--overwrite", "--force")
        Assert.Equal("stream-catalog build", build.Spec.Name)
        Assert.Equal("D:\lists", build.Positionals.Single())
        Assert.Equal("D:\z.zip", build.Value("out"))
        Assert.True(build.Flag("overwrite"))
        Assert.True(build.Flag("force"))
        Dim bare As ParsedCommand = ParseOk("stream-catalog", "build", "a.m3u")
        Assert.False(bare.Flag("overwrite"))
        Assert.False(bare.Flag("force"))
        Assert.Null(bare.Value("out"))
        Dim check As ParsedCommand = ParseOk("STREAM-CATALOG", "Check", "D:\z.zip")
        Assert.Equal("stream-catalog check", check.Spec.Name)
        Assert.Equal("D:\z.zip", check.Positionals.Single())
        Assert.Equal(CommandLineParser.DetailMissingArgument, Parse("stream-catalog", "build").Detail)
        Assert.Equal(CommandLineParser.DetailMissingArgument, Parse("stream-catalog", "check").Detail)
        Assert.Equal(CommandReasons.UnknownVerb, Parse("stream-catalog").Reason)
        Assert.Equal(CommandReasons.UnknownVerb, Parse("stream-catalog", "frob", "x").Reason)
        Assert.Equal(CommandLineParser.DetailExtraValue, Parse("stream-catalog", "build", "a.m3u", "b.m3u").Detail)
        Assert.Equal(CommandLineParser.DetailUnknownOption, Parse("stream-catalog", "check", "z.zip", "--out", "x").Detail)
    End Sub

    <Fact>
    Public Sub Stream_catalog_verbs_are_oneshot_writers_held_by_the_setting()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        For Each name As String In {"stream-catalog build", "stream-catalog check"}
            Dim spec As CommandSpec = manifest.Find(name)
            Assert.Equal("oneshot", spec.ColdStart)
            Assert.Equal(CommandRisk.WritesUserData, spec.Risk)
            Assert.False(CommandDispatch.IsAdmittedByRisk(spec, False, False))
            Assert.True(CommandDispatch.IsAdmittedByRisk(spec, False, True))
            Assert.Equal(6, spec.Aliases.Count)
        Next
        Assert.Equal(ViewerCommandTable.StreamCatalogBuildSpellings, manifest.Find("stream-catalog build").Aliases.ToArray())
        Assert.Equal(ViewerCommandTable.StreamCatalogCheckSpellings, manifest.Find("stream-catalog check").Aliases.ToArray())
    End Sub

    <Fact>
    Public Sub A_verb_that_returns_data_offers_json_and_result()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        For Each name As String In {"open", "stream-catalog build", "stream-catalog check",
                                    "file current", "file next", "file previous", "file first", "file last", "file goto",
                                    "slideshow start", "screenshot window"}
            Assert.NotNull(manifest.Find(name).FindOption("json"))
            Assert.NotNull(manifest.Find(name).FindOption("result"))
            Assert.True(manifest.Find(name).Returns.Count > 0, name)
        Next
        Assert.Equal(0, manifest.Find("phone-code receive").Returns.Count)
        Assert.Equal(0, manifest.Find("slideshow stop").Returns.Count)
    End Sub

    <Fact>
    Public Sub Every_exit_code_a_verb_lists_is_in_the_table_of_rule_11_once()
        For Each spec As CommandSpec In ViewerCommandTable.Build().Commands
            Dim seen As New HashSet(Of Integer)()
            For Each e As CommandExitCode In spec.ExitCodes
                Assert.True(CommandExit.IsKnown(e.Code), spec.Name & " lists " & e.Code)
                Assert.True(seen.Add(e.Code), spec.Name & " lists " & e.Code & " twice")
            Next
            Assert.True(seen.Contains(CommandExit.Done), spec.Name & " has no success code")
        Next
    End Sub

    ' --- L0318: the nine drive-and-observe verbs ---------------------------------------------------------

    Private Shared ReadOnly DriveVerbs As String() = {"file current", "file next", "file previous", "file first", "file last", "file goto",
                                                      "slideshow start", "slideshow stop", "screenshot window"}

    Private Shared Function SourceText(ParamArray relative As String()) As String
        Dim parts As New List(Of String)({RepoRoot()})
        parts.AddRange(relative)
        Return Normalized(Path.Combine(parts.ToArray()))
    End Function

    <Fact>
    Public Sub The_nine_drive_verbs_are_new_in_1_1_and_refuse_a_cold_start()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        Assert.Equal("1.1", ViewerCommandTable.CliVersion)
        For Each name As String In DriveVerbs
            Dim spec As CommandSpec = manifest.Find(name)
            Assert.NotNull(spec)
            Assert.Equal("1.1", spec.Since)
            Assert.Equal("refuse", spec.ColdStart)
            Assert.Equal("done", spec.Wait)
            Assert.False(spec.Interactive)
        Next
        ' The bounds of the draft: 2 s for the answer that reads, 5 s for a move, 10 s for a picture.
        Assert.Equal(2000, manifest.Find("file current").TimeoutMs)
        Assert.Equal(5000, manifest.Find("file next").TimeoutMs)
        Assert.Equal(5000, manifest.Find("slideshow start").TimeoutMs)
        Assert.Equal(10000, manifest.Find("screenshot window").TimeoutMs)
    End Sub

    <Fact>
    Public Sub Only_file_current_and_screenshot_window_are_held_by_the_setting()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        For Each name As String In DriveVerbs
            Dim restricted As Boolean = name = "file current" OrElse name = "screenshot window"
            Assert.Equal(If(restricted, CommandRisk.ReadsUserData, CommandRisk.Safe), manifest.Find(name).Risk)
            Dim args As String() = If(name = "file goto", {"--number", "3"}, If(name = "screenshot window", {"--out", "C:\t\w.png"}, New String() {}))
            Dim verb As String = name
            Dim off As DispatchDecision = CommandDispatch.Decide(Req(verb, args:=args), manifest, False, False, False)
            If restricted Then
                Assert.Equal(CommandReasons.SwitchOff, off.RefusalReason)
                Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(off.RefusalReason))
                Assert.Null(CommandDispatch.Decide(Req(verb, args:=args), manifest, True, False, False).RefusalReason)
            Else
                Assert.Null(off.RefusalReason)
            End If
        Next
    End Sub

    <Fact>
    Public Sub The_drive_verbs_parse_with_their_options_and_refuse_a_bad_one_as_exit_10()
        Assert.Equal("file current", ParseOk("file", "current").Spec.Name)
        Assert.Equal("40", ParseOk("file", "goto", "--number", "40").Value("number"))
        Assert.Equal(CommandLineParser.DetailMissingArgument, Parse("file", "goto").Detail)
        Assert.Equal("--number", Parse("file", "goto").Token)
        Assert.Equal(CommandLineParser.DetailBadValue, Parse("file", "goto", "--number", "forty").Detail)
        Assert.Equal(CommandLineParser.DetailUnknownOption, Parse("file", "next", "--number", "2").Detail)
        Assert.Equal(CommandReasons.UnknownVerb, Parse("file").Reason)
        Assert.Equal(CommandReasons.UnknownVerb, Parse("file", "frob").Reason)
        Assert.Equal(CommandReasons.UnknownVerb, Parse("slideshow").Reason)
        Dim start As ParsedCommand = ParseOk("slideshow", "start", "--random", "--interval-ms", "8000")
        Assert.True(start.Flag("random"))
        Assert.Equal("8000", start.Value("interval-ms"))
        Assert.False(ParseOk("slideshow", "start").Flag("random"))
        Assert.True(Not ParseOk("slideshow", "start").Has("interval-ms"))
        Assert.Equal(CommandLineParser.DetailBadValue, Parse("slideshow", "start", "--interval-ms", "soon").Detail)
        Assert.Equal("slideshow stop", ParseOk("slideshow", "stop").Spec.Name)
        Dim shot As ParsedCommand = ParseOk("screenshot", "window", "--out", "C:\t\w.png", "--overwrite")
        Assert.Equal("C:\t\w.png", shot.Value("out"))
        Assert.True(shot.Flag("overwrite"))
        Assert.Equal(CommandLineParser.DetailMissingArgument, Parse("screenshot", "window").Detail)
        Assert.Equal("--out", Parse("screenshot", "window").Token)
        Assert.Equal(CommandReasons.UnknownVerb, Parse("screenshot").Reason)
    End Sub

    <Fact>
    Public Sub Every_drive_verb_lists_the_exit_codes_the_draft_names()
        Dim manifest As CommandManifest = ViewerCommandTable.Build()
        Dim goto_ As List(Of Integer) = manifest.Find("file goto").ExitCodes.Select(Function(e) e.Code).ToList()
        Assert.Contains(10, goto_)
        Assert.Contains(11, goto_)
        Dim shot As List(Of Integer) = manifest.Find("screenshot window").ExitCodes.Select(Function(e) e.Code).ToList()
        For Each code As Integer In {0, 10, 11, 12, 13}
            Assert.Contains(code, shot)
        Next
        Assert.Contains(10, manifest.Find("slideshow start").ExitCodes.Select(Function(e) e.Code).ToList())
        Assert.Contains(12, manifest.Find("file current").ExitCodes.Select(Function(e) e.Code).ToList())
    End Sub

    ' --- the listing arithmetic ---------------------------------------------------------------------------

    <Fact>
    Public Sub File_next_then_file_previous_returns_to_the_same_place()
        For current As Integer = 0 To 3
            Dim forward As NavigationPlan = ViewerNavigation.Plan(NavigationMove.StepForward, current, 5, 0, True)
            Assert.Equal(NavigationOutcome.Move, forward.Outcome)
            Assert.Equal(current + 1, forward.Target)
            Dim back As NavigationPlan = ViewerNavigation.Plan(NavigationMove.StepBack, forward.Target, 5, 0, True)
            Assert.Equal(NavigationOutcome.Move, back.Outcome)
            Assert.Equal(current, back.Target)
        Next
    End Sub

    <Fact>
    Public Sub File_last_then_file_next_stays_on_the_last_file()
        Dim toLast As NavigationPlan = ViewerNavigation.Plan(NavigationMove.ToLast, 1, 5, 0, True)
        Assert.Equal(NavigationOutcome.Move, toLast.Outcome)
        Assert.Equal(4, toLast.Target)
        Dim next_ As NavigationPlan = ViewerNavigation.Plan(NavigationMove.StepForward, 4, 5, 0, True)
        Assert.Equal(NavigationOutcome.Stay, next_.Outcome)
        Assert.Equal(4, next_.Target)
        ' ... and the first file has no previous either.
        Dim prev As NavigationPlan = ViewerNavigation.Plan(NavigationMove.StepBack, 0, 5, 0, True)
        Assert.Equal(NavigationOutcome.Stay, prev.Outcome)
        Assert.Equal(0, prev.Target)
        ' Already on the last file, "last" has nothing to move.
        Assert.Equal(NavigationOutcome.Stay, ViewerNavigation.Plan(NavigationMove.ToLast, 4, 5, 0, True).Outcome)
        Assert.Equal(NavigationOutcome.Stay, ViewerNavigation.Plan(NavigationMove.ToFirst, 0, 5, 0, True).Outcome)
    End Sub

    <Fact>
    Public Sub File_goto_zero_and_count_plus_one_are_outside_the_listing()
        For Each number As Integer In {0, 6, -1, Integer.MinValue, Integer.MaxValue}
            Assert.Equal(NavigationOutcome.OutOfRange, ViewerNavigation.Plan(NavigationMove.ToNumber, 2, 5, number, True).Outcome)
        Next
        Dim one As NavigationPlan = ViewerNavigation.Plan(NavigationMove.ToNumber, 2, 5, 1, True)
        Assert.Equal(NavigationOutcome.Move, one.Outcome)
        Assert.Equal(0, one.Target)
        Dim five As NavigationPlan = ViewerNavigation.Plan(NavigationMove.ToNumber, 2, 5, 5, True)
        Assert.Equal(NavigationOutcome.Move, five.Outcome)
        Assert.Equal(4, five.Target)
        Dim same As NavigationPlan = ViewerNavigation.Plan(NavigationMove.ToNumber, 2, 5, 3, True)
        Assert.Equal(NavigationOutcome.Stay, same.Outcome)
        Assert.Equal(2, same.Target)
    End Sub

    <Fact>
    Public Sub An_empty_listing_or_a_place_outside_it_shows_nothing()
        For Each move As NavigationMove In [Enum].GetValues(GetType(NavigationMove))
            Assert.Equal(NavigationOutcome.NothingShown, ViewerNavigation.Plan(move, 0, 0, 1, True).Outcome)
            Assert.Equal(NavigationOutcome.NothingShown, ViewerNavigation.Plan(move, -1, 5, 1, True).Outcome)
            Assert.Equal(NavigationOutcome.NothingShown, ViewerNavigation.Plan(move, 5, 5, 1, True).Outcome)
        Next
    End Sub

    <Fact>
    Public Sub A_listing_that_is_not_read_yet_hands_the_step_to_the_windows_own_key_path()
        ' After an external open the window holds only the one file; its first flip lists the folder.
        Dim forward As NavigationPlan = ViewerNavigation.Plan(NavigationMove.StepForward, 0, 1, 0, False)
        Assert.Equal(NavigationOutcome.Move, forward.Outcome)
        Assert.Equal(-1, forward.Target)
        Dim back As NavigationPlan = ViewerNavigation.Plan(NavigationMove.StepBack, 0, 1, 0, False)
        Assert.Equal(NavigationOutcome.Move, back.Outcome)
        Assert.Equal(-1, back.Target)
        ' Absolute places are judged against the list in memory, as the Home, End and jump keys judge them.
        Assert.Equal(NavigationOutcome.Stay, ViewerNavigation.Plan(NavigationMove.ToLast, 0, 1, 0, False).Outcome)
        Assert.Equal(NavigationOutcome.OutOfRange, ViewerNavigation.Plan(NavigationMove.ToNumber, 0, 1, 2, False).Outcome)
    End Sub

    <Fact>
    Public Sub Each_move_verb_names_one_move_and_file_current_names_none()
        Dim move As NavigationMove
        Assert.True(ViewerNavigation.TryMoveOfVerb("file next", move))
        Assert.Equal(NavigationMove.StepForward, move)
        Assert.True(ViewerNavigation.TryMoveOfVerb("file previous", move))
        Assert.Equal(NavigationMove.StepBack, move)
        Assert.True(ViewerNavigation.TryMoveOfVerb("file first", move))
        Assert.Equal(NavigationMove.ToFirst, move)
        Assert.True(ViewerNavigation.TryMoveOfVerb("file last", move))
        Assert.Equal(NavigationMove.ToLast, move)
        Assert.True(ViewerNavigation.TryMoveOfVerb("file goto", move))
        Assert.Equal(NavigationMove.ToNumber, move)
        Assert.False(ViewerNavigation.TryMoveOfVerb("file current", move))
        Assert.False(ViewerNavigation.TryMoveOfVerb("slideshow start", move))
    End Sub

    ' --- the slideshow pause -----------------------------------------------------------------------------

    <Fact>
    Public Sub The_slideshow_pause_is_bounded_like_the_saved_setting()
        Assert.Equal(1000, ViewerNavigation.MinSlideshowIntervalMs)
        Assert.Equal(120000, ViewerNavigation.MaxSlideshowIntervalMs)
        Assert.True(ViewerNavigation.IsSlideshowIntervalInRange(1000))
        Assert.True(ViewerNavigation.IsSlideshowIntervalInRange(8000))
        Assert.True(ViewerNavigation.IsSlideshowIntervalInRange(120000))
        For Each bad As Integer In {999, 0, -1000, 120001, Integer.MaxValue, Integer.MinValue}
            Assert.False(ViewerNavigation.IsSlideshowIntervalInRange(bad), bad.ToString())
        Next
        ' The settings reader keeps the saved value between the same two numbers: the command may ask for no more.
        Dim lifecycle As String = SourceText("src", "Main_Form.Lifecycle.vb")
        Assert.Contains("Slideshow_Base_Interval_Ms, 1000, 120000)", lifecycle)
    End Sub

    ' --- the screenshot target ---------------------------------------------------------------------------

    <Fact>
    Public Sub An_automatic_screenshot_name_is_the_local_second_with_a_number_while_taken()
        Dim when_ As New DateTime(2026, 11, 14, 9, 5, 7)
        Assert.Equal("screenshot_261114_090507.png", ViewerNavigation.ScreenshotName(when_, Nothing))
        Dim taken As New HashSet(Of String)({"screenshot_261114_090507.png"}, StringComparer.OrdinalIgnoreCase)
        Assert.Equal("screenshot_261114_090507 (2).png", ViewerNavigation.ScreenshotName(when_, Function(n As String) taken.Contains(n)))
        taken.Add("screenshot_261114_090507 (2).png")
        Assert.Equal("screenshot_261114_090507 (3).png", ViewerNavigation.ScreenshotName(when_, Function(n As String) taken.Contains(n)))
    End Sub

    <Fact>
    Public Sub A_path_the_program_gives_is_used_exactly_as_given()
        Dim when_ As New DateTime(2026, 11, 14, 9, 5, 7)
        Dim never As Func(Of String, Boolean) = Function(p As String) False
        Dim always As Func(Of String, Boolean) = Function(p As String) True
        Assert.Equal("C:\work\shot.png", ViewerNavigation.ResolveScreenshotTarget("shot.png", "C:\work", when_, never, never))
        Assert.Equal("C:\out\a b (1).png", ViewerNavigation.ResolveScreenshotTarget("C:\out\a b (1).png", "C:\work", when_, never, never))
        Assert.Equal("C:\out\x.PNG", ViewerNavigation.ResolveScreenshotTarget("C:\out\x.PNG", "C:\work", when_, never, always))
        ' An existing file is not renumbered: the caller refuses it, or replaces it under --overwrite.
        Assert.Equal("C:\work\shot.png", ViewerNavigation.ResolveScreenshotTarget("shot.png", "C:\work", when_, never, always))
        Assert.Equal("C:\work\sub\shot.png", ViewerNavigation.ResolveScreenshotTarget("sub\shot.png", "C:\work", when_, never, never))
        Assert.Null(ViewerNavigation.ResolveScreenshotTarget("", "C:\work", when_, never, never))
        Assert.Null(ViewerNavigation.ResolveScreenshotTarget("https://example.test/a.png", "C:\work", when_, never, never))
    End Sub

    <Fact>
    Public Sub A_folder_as_target_gets_the_automatic_name_inside_it()
        Dim when_ As New DateTime(2026, 11, 14, 9, 5, 7)
        Dim never As Func(Of String, Boolean) = Function(p As String) False
        Dim isFolder As Func(Of String, Boolean) = Function(p As String) String.Equals(p, "C:\shots", StringComparison.OrdinalIgnoreCase)
        Assert.Equal("C:\shots\screenshot_261114_090507.png", ViewerNavigation.ResolveScreenshotTarget("C:\shots", "C:\work", when_, isFolder, never))
        Assert.Equal("C:\shots\screenshot_261114_090507.png", ViewerNavigation.ResolveScreenshotTarget("C:\shots\", "C:\work", when_, never, never))
        Assert.Equal("C:\work\new\screenshot_261114_090507.png", ViewerNavigation.ResolveScreenshotTarget("new\", "C:\work", when_, never, never))
        Dim taken As Func(Of String, Boolean) = Function(p As String) p = "C:\shots\screenshot_261114_090507.png"
        Assert.Equal("C:\shots\screenshot_261114_090507 (2).png", ViewerNavigation.ResolveScreenshotTarget("C:\shots\", "C:\work", when_, never, taken))
        Assert.Equal("D:\screenshot_261114_090507.png", ViewerNavigation.ResolveScreenshotTarget("D:\", "C:\work", when_, never, never))
    End Sub

    <Fact>
    Public Sub A_full_disk_is_told_by_the_error_code_not_the_message()
        Assert.True(ViewerNavigation.IsDiskFullHResult(&H80070070))
        Assert.True(ViewerNavigation.IsDiskFullHResult(&H80070027))
        Assert.False(ViewerNavigation.IsDiskFullHResult(&H80070020))
        Assert.False(ViewerNavigation.IsDiskFullHResult(&H80070005))
        Assert.False(ViewerNavigation.IsDiskFullHResult(0))
    End Sub

    ' --- the PNG time chunk --------------------------------------------------------------------------------

    Private Shared Function PngChunk(type As String, data As Byte()) As Byte()
        Dim chunk As New List(Of Byte)()
        chunk.AddRange(New Byte() {CByte((data.Length >> 24) And &HFF), CByte((data.Length >> 16) And &HFF),
                                   CByte((data.Length >> 8) And &HFF), CByte(data.Length And &HFF)})
        Dim body As New List(Of Byte)(Encoding.ASCII.GetBytes(type))
        body.AddRange(data)
        chunk.AddRange(body)
        Dim crc As UInteger = PngTimeChunk.Crc32(body.ToArray(), 0, body.Count)
        chunk.AddRange(New Byte() {CByte((crc >> 24) And &HFFUI), CByte((crc >> 16) And &HFFUI), CByte((crc >> 8) And &HFFUI), CByte(crc And &HFFUI)})
        Return chunk.ToArray()
    End Function

    ''' <summary>A 1 x 1 grey PNG built from chunks: signature, IHDR, IDAT, IEND.</summary>
    Private Shared Function TinyPng() As Byte()
        Dim png As New List(Of Byte)(New Byte() {&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA})
        png.AddRange(PngChunk("IHDR", New Byte() {0, 0, 0, 1, 0, 0, 0, 1, 8, 0, 0, 0, 0}))
        png.AddRange(PngChunk("IDAT", New Byte() {&H78, &H9C, &H63, &H60, 0, 0, 0, 2, 0, 1}))
        png.AddRange(PngChunk("IEND", New Byte() {}))
        Return png.ToArray()
    End Function

    <Fact>
    Public Sub The_crc_of_a_png_chunk_matches_the_check_value_of_the_algorithm()
        ' The standard CRC-32 check value of "123456789".
        Dim bytes As Byte() = Encoding.ASCII.GetBytes("123456789")
        Assert.Equal(&HCBF43926UI, PngTimeChunk.Crc32(bytes, 0, bytes.Length))
        ' The IEND chunk of every PNG ends in AE 42 60 82.
        Dim iend As Byte() = PngChunk("IEND", New Byte() {})
        Assert.Equal({0, 0, 0, 0, &H49, &H45, &H4E, &H44, &HAE, &H42, &H60, &H82}, iend.Select(Function(b) CInt(b)).ToArray())
    End Sub

    <Fact>
    Public Sub The_time_chunk_goes_right_after_the_header_with_the_local_second_and_a_valid_crc()
        Dim original As Byte() = TinyPng()
        Dim at As New DateTime(2026, 11, 14, 9, 5, 7)
        Dim withTime As Byte() = PngTimeChunk.Insert(original, at)
        Assert.Equal(original.Length + 19, withTime.Length)
        ' signature (8) and IHDR (25) unchanged, then the 19 bytes of the chunk, then the rest as it was.
        For i As Integer = 0 To 32
            Assert.Equal(CInt(original(i)), CInt(withTime(i)))
        Next
        For i As Integer = 33 To original.Length - 1
            Assert.Equal(CInt(original(i)), CInt(withTime(i + 19)))
        Next
        Assert.Equal({0, 0, 0, 7, &H74, &H49, &H4D, &H45, &H7, &HEA, 11, 14, 9, 5, 7},
                     withTime.Skip(33).Take(15).Select(Function(b) CInt(b)).ToArray())
        Dim crc As UInteger = PngTimeChunk.Crc32(withTime, 37, 11)
        Assert.Equal({CInt((crc >> 24) And &HFFUI), CInt((crc >> 16) And &HFFUI), CInt((crc >> 8) And &HFFUI), CInt(crc And &HFFUI)},
                     withTime.Skip(48).Take(4).Select(Function(b) CInt(b)).ToArray())
        Dim read As DateTime
        Assert.True(PngTimeChunk.TryRead(withTime, read))
        Assert.Equal(at, read)
    End Sub

    <Fact>
    Public Sub A_png_that_has_a_time_chunk_or_a_file_that_is_no_png_is_left_alone()
        Dim once As Byte() = PngTimeChunk.Insert(TinyPng(), New DateTime(2026, 11, 14, 9, 5, 7))
        Dim twice As Byte() = PngTimeChunk.Insert(once, New DateTime(2030, 1, 1, 0, 0, 0))
        Assert.Equal(once.Length, twice.Length)
        Dim read As DateTime
        Assert.True(PngTimeChunk.TryRead(twice, read))
        Assert.Equal(New DateTime(2026, 11, 14, 9, 5, 7), read)
        Dim junk As Byte() = Encoding.ASCII.GetBytes("this is not a png file at all, not even close")
        Assert.True(Object.ReferenceEquals(junk, PngTimeChunk.Insert(junk, DateTime.Now)))
        Assert.False(PngTimeChunk.TryRead(junk, read))
        Assert.False(PngTimeChunk.TryRead(TinyPng(), read))
        Assert.False(PngTimeChunk.TryRead(Nothing, read))
    End Sub

    <Fact>
    Public Sub A_png_with_the_time_chunk_still_decodes()
        Using bmp As New System.Drawing.Bitmap(3, 2)
            Using stream As New MemoryStream()
                bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png)
                Dim withTime As Byte() = PngTimeChunk.Insert(stream.ToArray(), New DateTime(2026, 11, 14, 9, 5, 7))
                Dim read As DateTime
                Assert.True(PngTimeChunk.TryRead(withTime, read))
                Using decoded As System.Drawing.Image = System.Drawing.Image.FromStream(New MemoryStream(withTime))
                    Assert.Equal(3, decoded.Width)
                    Assert.Equal(2, decoded.Height)
                End Using
            End Using
        End Using
    End Sub

    ' --- the window side: one implementation, no second one ---------------------------------------------

    <Fact>
    Public Sub Every_drive_verb_has_a_case_in_the_dispatch_and_calls_the_one_existing_implementation()
        Dim dispatch As String = SourceText("src", "Main_Form.AppCommands.vb")
        For Each name As String In DriveVerbs
            Assert.Contains("""" & name & """", dispatch)
        Next
        Dim drive As String = SourceText("src", "Main_Form.AppCommandsDrive.vb")
        For Each call_ As String In {"ReadShowMediaFile(Mode_Next)", "ReadShowMediaFile(Mode_Prev)", "JumpTo(plan.Target", "SetSlideShow(False, pause)",
                                     "SetRandomSlideShow(False, pause)", "SlideShowStop()", "ManualNavigationStopsShow()"}
            Assert.Contains(call_, drive)
        Next
        ' A command never writes the list position or the saved pause itself.
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(drive, "current_File_Index\s*[+\-]?=(?!=)"), "the index is moved by the pipeline only")
        Assert.DoesNotContain("Slideshow_Base_Interval_Ms", drive)
        Assert.DoesNotContain("SaveSetting", drive)
    End Sub

    <Fact>
    Public Sub The_slideshow_entry_points_take_a_pause_for_one_run_and_never_save_it()
        Dim show As String = SourceText("src", "Main_Form.Slideshow.vb")
        Assert.Contains("Optional interval_Override_Ms As Integer = 0", show)
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(show, "Slideshow_Base_Interval_Ms\s*=(?!=)"), "the saved pause is written by the settings window only")
    End Sub

    <Fact>
    Public Sub The_window_picture_is_drawn_by_PrintWindow_with_full_content_and_stamped_with_the_local_second()
        Dim drive As String = SourceText("src", "Main_Form.AppCommandsDrive.vb")
        Assert.Contains("CapturePwRenderFullContent As UInteger = 2UI", drive)
        Assert.Contains("CapturePrintWindow(Handle, hdc, CapturePwRenderFullContent)", drive)
        Assert.Contains("PngTimeChunk.Insert(png, captured)", drive)
        Assert.Contains("CommandFiles.TryWriteOut(target, png, overwrite)", drive)
        Assert.Contains("Dim captured As DateTime = DateTime.Now", drive)
    End Sub

End Class
