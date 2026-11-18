Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports FastMediaSorterCompanion
Imports FastMediaSorterCompanion.AppCommands
Imports Xunit

''' <summary>
''' Ticket L0320, contract APP-COMMANDS 0.1 section 4: the cases C1 to C13, one fact (or, for C10, one fact per cold-start
''' mode) each, for the Share Manager. Every fact is the decision half of its case - what the shared library, the program's
''' command table and the client's own text promise, held without a window or a second process. The process half (two real
''' processes, a real window and tray count) is <c>tests/Integration/AppCommandScenes.ps1</c>; C5 and C9 are manual rungs by the
''' contract itself and their pure halves are here.
''' </summary>
Public Class ShareManagerCommandsConformanceTests

    Private Shared Function Manifest() As CommandManifest
        Return ShareManagerCommandTable.Build()
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function TextOf(ParamArray relative As String()) As String
        Dim parts As New List(Of String)({RepoRoot()})
        parts.AddRange(relative)
        Return File.ReadAllText(Path.Combine(parts.ToArray()), Encoding.UTF8).Replace(vbCrLf, vbLf)
    End Function

    Private Shared Function Client() As String
        Return TextOf("src", "FastMediaSorterCompanion", "ShareManagerCommandClient.vb")
    End Function

    ''' <summary>The part of a source text from the first occurrence of the start marker up to the first occurrence of the end
    ''' marker after it (to the end when that is not found); empty when the start marker is not there.</summary>
    Private Shared Function Slice(text As String, startMarker As String, endMarker As String) As String
        Dim at As Integer = text.IndexOf(startMarker, StringComparison.Ordinal)
        If at < 0 Then Return String.Empty
        Dim stop_ As Integer = text.IndexOf(endMarker, at + startMarker.Length, StringComparison.Ordinal)
        If stop_ < 0 Then stop_ = text.Length
        Return text.Substring(at, stop_ - at)
    End Function

    Private Shared Function ScratchFolder() As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-appcommands-conformance", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Return dir
    End Function

    Private Shared Function Req(command As String, Optional origin As String = "cli", Optional callerElevated As Boolean = False,
                                Optional args As IEnumerable(Of String) = Nothing) As CommandRequest
        Dim r As New CommandRequest With {.Command = command, .Origin = origin, .RequestId = Guid.NewGuid().ToString("D"),
                                          .CallerElevated = callerElevated}
        If args IsNot Nothing Then r.Args.AddRange(args)
        Return r
    End Function

    ''' <summary>The shortest argument list the parser accepts for a verb.</summary>
    Private Shared Function ValidArgs(spec As CommandSpec) As List(Of String)
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
        Return result
    End Function

    Private Shared Function Decide(spec As CommandSpec, restrictedAllowed As Boolean, busy As Boolean) As DispatchDecision
        Return CommandDispatch.Decide(Req(spec.Name, args:=ValidArgs(spec)), Manifest(), restrictedAllowed, busy, False)
    End Function

    Private Shared Function CallerAnswers(name As String) As Boolean
        Return String.Equals(name, "commands", StringComparison.Ordinal) OrElse String.Equals(name, "version", StringComparison.Ordinal)
    End Function

    ' --- C1 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C1_commands_json_is_the_manifest_of_the_table_and_starts_no_window()
        Dim m As CommandManifest = Manifest()
        Dim spec As CommandSpec = m.Find("commands")
        Assert.Equal("oneshot", spec.ColdStart)
        Assert.Equal(CommandRisk.Safe, spec.Risk)
        Assert.True(String.Equals(m.ToJsonText(), TextOf("docs", "cli-commands.share-manager.json"), StringComparison.Ordinal),
                    "docs/cli-commands.share-manager.json differs from what commands --json prints")
        Dim value As Object = Nothing
        Assert.True(CommandJson.TryParse(m.ToJsonText(), value))
        Dim root As JsonObject = DirectCast(value, JsonObject)
        Assert.Equal(1, root.GetInt("schemaVersion", 0))
        Assert.Equal("FastMediaSorterCompanion.exe", root.GetString("exe"))
        For Each member As String In {"schemaVersion", "product", "exe", "cliVersion", "commands"}
            Dim ignored As Object = Nothing
            Assert.True(root.TryGet(member, ignored), "the manifest lacks " & member)
        Next
        ' No window, no tray, no mutex: the verb is answered before any delivery, and the whole decision is taken before the mutex.
        Dim text As String = Client()
        Dim block As String = Slice(text, "Case ""commands""", "Case ""version""")
        Assert.True(block.Length > 0, "the commands case is not in the client")
        Assert.Contains("ToJsonText()", block)
        Assert.Contains("Return True", block)
        Assert.DoesNotContain("TrayContext", block)
        Assert.DoesNotContain("Mutex", block)
        Assert.True(text.IndexOf("Case ""commands""", StringComparison.Ordinal) < text.IndexOf("Deliver(request)", StringComparison.Ordinal))
        Dim program As String = TextOf("src", "FastMediaSorterCompanion", "Program.vb")
        Dim callAt As Integer = program.IndexOf("ShareManagerCommandClient.TryRun(", StringComparison.Ordinal)
        Dim mutexAt As Integer = program.IndexOf("mtx = New Mutex(True, MutexName, createdNew)", StringComparison.Ordinal)
        Assert.True(callAt > 0 AndAlso mutexAt > callAt, "the command line is decided after the manager's mutex is taken")
    End Sub

    ' --- C2 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C2_version_is_done_and_status_without_a_manager_is_exit_11_not_running()
        Dim m As CommandManifest = Manifest()
        Assert.Equal("oneshot", m.Find("version").ColdStart)
        Assert.Equal("refuse", m.Find("status").ColdStart)
        Dim done As CommandResponse = CommandResponse.Done("r", "26.11.14.0001")
        Assert.Equal(CommandExit.Done, done.ExitCode)
        Dim none As CommandResponse = CommandResponse.ForReason("r", CommandReasons.NotRunning, "not running")
        Assert.Equal(11, none.ExitCode)
        Assert.Equal(CommandReasons.NotRunning, none.Reason)
        Dim text As String = Client()
        Assert.Contains("ProductVersion", Slice(text, "Case ""version""", "End Select"))
        Dim cold As String = Slice(text, "Private Function ColdStart(", "Private Function RunClipboardAlone(")
        Dim refuse As String = Slice(cold, "Case Else" & vbLf & "                exitCode", "End Select")
        Assert.Contains("CommandReasons.NotRunning", refuse)
        ' The sentence names this program, not the viewer.
        Assert.Contains(ProductNames.ShareManager, ShareManagerCommandText.ForReason(CommandReasons.NotRunning))
    End Sub

    ' --- C3 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C3_the_manager_takes_every_verb_and_the_caller_leaves_after_the_answer()
        For Each spec As CommandSpec In Manifest().Commands
            If CallerAnswers(spec.Name) Then Continue For
            Dim decision As DispatchDecision = Decide(spec, True, False)
            Assert.True(decision.RefusalReason Is Nothing, spec.Name & " was refused: " & decision.RefusalReason)
            Assert.Equal(spec.Name, decision.Parsed.Spec.Name)
        Next
        ' A verb the person answers in a window says so: share add is answered once the proposal is shown.
        Assert.Equal("accepted", Manifest().Find("share add").Wait)
        Dim text As String = Client()
        Assert.Contains("CommandFiles.ReplyPathFor(ShareManagerCommandTable.ReplyFolder, requestId)", text)
        Dim tail As String = Slice(text, "Dim response As CommandResponse = WaitForReply(", "End Function")
        Assert.Contains("Emit(", tail)
        Assert.Contains("Return True", tail)
        Dim id As String = Guid.NewGuid().ToString("D")
        Assert.Equal(id, CommandResponse.Done(id, "m").RequestId)
    End Sub

    ' --- C4 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C4_a_refused_request_runs_nothing_and_a_following_status_is_still_taken()
        Dim m As CommandManifest = Manifest()
        Dim chunk As New String("a"c, 30000)
        Dim refused As DispatchDecision() = {
            CommandDispatch.Decide(Req("frobnicate"), m, True, False, False),
            CommandDispatch.Decide(Req("status", args:={"--frob"}), m, True, False, False),
            CommandDispatch.Decide(Req("status", args:={chunk, chunk, chunk}), m, True, False, False)}
        For Each d As DispatchDecision In refused
            Assert.Null(d.Parsed)
            Assert.Equal(CommandExit.Usage, CommandReasons.ExitOf(d.RefusalReason))
        Next
        Assert.Equal(CommandReasons.UnknownVerb, refused(0).RefusalReason)
        Assert.Equal(CommandReasons.BadArgument, refused(1).RefusalReason)
        Assert.Equal(CommandReasons.TooLarge, refused(2).RefusalReason)
        Dim after As DispatchDecision = CommandDispatch.Decide(Req("status"), m, True, False, False)
        Assert.Null(after.RefusalReason)
        Assert.Equal("status", after.Parsed.Spec.Name)
        Dim text As String = Client()
        Dim refusal As String = Slice(text, "If refusal IsNot Nothing Then", "End If")
        Assert.Contains("Return True", refusal)
        Assert.True(text.IndexOf("If refusal IsNot Nothing Then", StringComparison.Ordinal) < text.IndexOf("Deliver(request)", StringComparison.Ordinal))
    End Sub

    ' --- C5 (manual rung: a manager that pumps no messages; the bound and the mapping are facts) -------

    <Fact>
    Public Sub C5_a_manager_that_never_answers_is_given_up_inside_the_delivery_bound_as_exit_2()
        Assert.Equal(1000, CommandBounds.DeliveryMs)
        Assert.Equal(CommandExit.NotProven, CommandReasons.ExitOf(CommandReasons.Timeout))
        Assert.Equal(CommandExit.NotReady, CommandReasons.ExitOf(CommandReasons.Busy))
        Dim text As String = Client()
        Dim deliver As String = Slice(text, "Private Function Deliver(", "Private Function SendOnce(")
        Assert.Contains("CommandBounds.DeliveryMs - CInt(watch.ElapsedMilliseconds)", deliver)
        Assert.Contains("If remaining <= 0 Then Exit Do", deliver)
        Assert.Contains("SendOnce(hwnd, payload, remaining)", deliver)
        Dim sendOnce As String = Slice(text, "Private Function SendOnce(", "Private Function WaitForReply(")
        Assert.Contains("SMTO_ABORTIFHUNG", sendOnce)
        Assert.Contains("timeoutMs", sendOnce)
        Dim noAnswer As String = Slice(text, "Case Delivery.NoAnswer", "End Select")
        Assert.Contains("CommandReasons.Timeout", noAnswer)
    End Sub

    ' --- C6 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C6_a_modal_window_answers_busy_11_except_to_the_verbs_that_only_read_or_queue()
        Dim ignoring As Integer = 0
        For Each spec As CommandSpec In Manifest().Commands
            If CallerAnswers(spec.Name) Then Continue For
            If ShareManagerCommandTable.IgnoresBusy(spec.Name) Then
                ignoring += 1
                Continue For
            End If
            Dim decision As DispatchDecision = Decide(spec, True, True)
            Assert.Equal(CommandReasons.Busy, decision.RefusalReason)
            Assert.Equal(CommandExit.NotReady, CommandReasons.ExitOf(decision.RefusalReason))
            Assert.Equal(CommandStatus.Busy, CommandReasons.StatusOf(decision.RefusalReason))
            Assert.Null(decision.Parsed)
        Next
        Assert.True(ignoring > 0, "no verb ignores busy any more: the table of that exception is empty")
        Dim tray As String = TextOf("src", "FastMediaSorterCompanion", "TrayContext.AppCommands.vb")
        Dim busy As String = Slice(tray, "Private Function AppCommandBusy()", "End Function")
        Assert.True(busy.Length > 0, "AppCommandBusy is not in the tray context")
        Assert.Contains("f.Modal", busy)
        Assert.Contains("AppCommandBusy() AndAlso Not ShareManagerCommandTable.IgnoresBusy(request.Command)", tray)
    End Sub

    ' --- C7 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C7_a_verb_past_its_timeout_is_exit_2_timeout_and_never_done()
        Dim late As CommandResponse = CommandResponse.ForReason("r", CommandReasons.Timeout, "no answer")
        Assert.Equal(CommandExit.NotProven, late.ExitCode)
        Assert.Equal(CommandStatus.Failed, late.Status)
        Assert.Equal(CommandReasons.Timeout, late.Reason)
        For Each spec As CommandSpec In Manifest().Commands
            Assert.InRange(spec.CompletionMs(), 1, CommandBounds.MaxCompletionMs)
            Assert.Equal(Math.Min(spec.TimeoutMs, CommandBounds.MaxCompletionMs), spec.CompletionMs())
        Next
        Dim text As String = Client()
        Dim wait As String = Slice(text, "Dim response As CommandResponse = WaitForReply(", "exitCode = Emit(")
        Assert.Contains("parsed.Spec.CompletionMs()", wait)
        Assert.Contains("If response Is Nothing Then", wait)
        Assert.Contains("CommandReasons.Timeout", wait)
        Dim waitFunction As String = Slice(text, "Private Function WaitForReply(", "Private Function Emit(")
        Assert.Contains("While watch.ElapsedMilliseconds < completionMs", waitFunction)
        Assert.Contains("Return Nothing", waitFunction)
    End Sub

    ' --- C8 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C8_with_the_setting_off_every_class_but_safe_is_exit_12_switch_off_and_the_safe_verbs_work()
        Dim restricted As Integer = 0
        For Each spec As CommandSpec In Manifest().Commands
            If CallerAnswers(spec.Name) Then Continue For
            Dim off As DispatchDecision = Decide(spec, False, False)
            Assert.Null(Decide(spec, True, False).RefusalReason)
            If String.Equals(spec.Risk, CommandRisk.Safe, StringComparison.Ordinal) Then
                Assert.Null(off.RefusalReason)
            Else
                restricted += 1
                Assert.Equal(CommandReasons.SwitchOff, off.RefusalReason)
                Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(off.RefusalReason))
                Assert.Equal(CommandStatus.Refused, CommandReasons.StatusOf(off.RefusalReason))
                ' A claimed origin is not a credential (APP-COMMANDS item T.1): a line that says "sibling" is read like any
                ' other and held by the setting, and so is a line that claims legacy for a verb with no legacy alias (item F.2).
                Dim claimed As CommandRequest = Nothing
                Assert.True(CommandRequest.TryParse(Req(spec.Name, "sibling", args:=ValidArgs(spec)).ToLine(), claimed))
                Assert.Equal(CommandReasons.SwitchOff, CommandDispatch.Decide(claimed, Manifest(), False, False, False).RefusalReason)
                Assert.False(spec.HasLegacyAlias)
                claimed.Legacy = True
                Assert.Equal(CommandReasons.SwitchOff, CommandDispatch.Decide(claimed, Manifest(), False, False, False).RefusalReason)
            End If
        Next
        Assert.Equal(9, restricted)
        ' Once in the client, before any cold start can get round it; the manager decides again for what reaches it.
        Dim text As String = Client()
        Dim gate As Integer = text.IndexOf("CommandDispatch.IsAdmittedByRisk(parsed.Spec, False, AppCommandsSetting.IsAllowed())", StringComparison.Ordinal)
        Assert.True(gate > 0 AndAlso gate < text.IndexOf("Deliver(request)", StringComparison.Ordinal), "the client does not apply rule 13 before it delivers")
        ' The sentence names the setting, in the program's language and in the generated document.
        Assert.Contains(AppCommandsText.SettingLabel(), ShareManagerCommandText.ForReason(CommandReasons.SwitchOff))
        Assert.Contains(ShareManagerCommandTable.SettingLabelEnglish, TextOf("docs", "CLI.md"))
    End Sub

    ' --- C9 (manual rung: another user or session, a real elevated copy; the refusal and the folder are facts) ---

    <Fact>
    Public Sub C9_an_elevated_manager_refuses_a_caller_that_is_not_and_replies_only_into_the_users_folder()
        Dim m As CommandManifest = Manifest()
        Dim ordinary As DispatchDecision = CommandDispatch.Decide(Req("status"), m, True, False, True)
        Assert.Equal(CommandReasons.Elevation, ordinary.RefusalReason)
        Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(ordinary.RefusalReason))
        Assert.Null(CommandDispatch.Decide(Req("status", callerElevated:=True), m, True, False, True).RefusalReason)
        Assert.Null(CommandDispatch.Decide(Req("status", callerElevated:=True), m, True, False, False).RefusalReason)
        Dim folder As String = CommandFiles.ReplyDirectory(ShareManagerCommandTable.ReplyFolder)
        Assert.True(Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase),
                    "the reply folder is not under the user's temporary folder")
        Dim elsewhere As CommandRequest = Req("status")
        elsewhere.ReplyPath = Path.Combine(Path.GetPathRoot(Path.GetTempPath()), "other-place", elsewhere.RequestId & ".reply.json")
        Assert.False(CommandFiles.IsValidReplyPath(ShareManagerCommandTable.ReplyFolder, elsewhere))
        Dim own As CommandRequest = Req("status")
        own.ReplyPath = CommandFiles.ReplyPathFor(ShareManagerCommandTable.ReplyFolder, own.RequestId)
        Assert.True(CommandFiles.IsValidReplyPath(ShareManagerCommandTable.ReplyFolder, own))
    End Sub

    ' --- C10: one fact per declared coldStart mode ---------------------------------------------------

    Private Shared Function VerbsOf(mode As String) As String()
        Dim names As New List(Of String)()
        For Each spec As CommandSpec In Manifest().Commands
            If String.Equals(spec.ColdStart, mode, StringComparison.Ordinal) Then names.Add(spec.Name)
        Next
        Return names.ToArray()
    End Function

    <Fact>
    Public Sub C10_start_verbs_become_the_manager_with_the_plan_the_old_launch_made()
        Assert.Equal({"show", "start", "share add", "resource add", "schedule open", "broadcast open",
                      "schedule add", "schedule remove", "schedule run"}, VerbsOf("start"))
        Dim cold As String = Slice(Client(), "Private Function ColdStart(", "Private Function RunClipboardAlone(")
        Dim start As String = Slice(cold, "Case ""start""", "Case ""oneshot""")
        Assert.Contains("plan = LaunchArguments.FromCommand(parsed, target)", start)
        ' This process goes on as the manager: the client returns False, and Program.Main takes the plan to the mutex and the tray.
        Assert.Contains("Return False", start)
        ' Schedule management verbs start the manager silently (--tray), deliver the request, wait for the answer and exit.
        Assert.Contains("IsBackgroundScheduleVerb(parsed.Spec.Name)", start)
        Assert.Contains("StartSilentManagerAndDeliver(", start)
        Dim program As String = TextOf("src", "FastMediaSorterCompanion", "Program.vb")
        Dim callAt As Integer = program.IndexOf("ShareManagerCommandClient.TryRun(", StringComparison.Ordinal)
        Dim takenAt As Integer = program.IndexOf("If(startPlan, LaunchArguments.Classify(args))", StringComparison.Ordinal)
        Assert.True(callAt > 0 AndAlso takenAt > callAt, "Program.Main does not continue with the plan the start verb handed back")
    End Sub

    <Fact>
    Public Sub C10_oneshot_verbs_answer_in_their_own_process_and_start_no_tray_and_hold_no_manager_mutex()
        Assert.Equal({"commands", "version", "quit", "clipboard send", "clipboard get", "schedule list", "schedule history", "exchange status"}, VerbsOf("oneshot"))
        Dim cold As String = Slice(Client(), "Private Function ColdStart(", "Private Function RunClipboardAlone(")
        Dim oneshot As String = Slice(cold, "Case ""oneshot""", "Case Else" & vbLf & "                exitCode")
        Assert.True(oneshot.Length > 0, "the oneshot branch is not in the client")
        Assert.Contains("Return True", oneshot)
        Assert.DoesNotContain("TrayContext", oneshot)
        Assert.DoesNotContain("Application.Run", oneshot)
        Assert.DoesNotContain("New Mutex", oneshot)
        ' The list and history verbs answer from the files: nothing starts, nothing runs.
        Assert.Contains("ShareManagerObserve.ScheduleListResponse(request.RequestId)", oneshot)
        Assert.Contains("ShareManagerScheduleCli.ScheduleHistoryResponse(request.RequestId, parsed)", oneshot)
        Assert.Contains("ShareManagerObserve.ExchangeStatusResponse(request.RequestId, 0)", oneshot)
        ' The decision is taken before the manager's own mutex exists.
        Dim program As String = TextOf("src", "FastMediaSorterCompanion", "Program.vb")
        Dim callAt As Integer = program.IndexOf("ShareManagerCommandClient.TryRun(", StringComparison.Ordinal)
        Assert.True(callAt > 0 AndAlso callAt < program.IndexOf("mtx = New Mutex(True, MutexName, createdNew)", StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub C10_refuse_verbs_with_nothing_running_are_exit_11()
        Dim refuse As String() = VerbsOf("refuse")
        Assert.Equal({"status", "share list"}, refuse)
        Dim cold As String = Slice(Client(), "Private Function ColdStart(", "Private Function RunClipboardAlone(")
        Dim otherwise As String = Slice(cold, "Case Else" & vbLf & "                exitCode", "End Select")
        Assert.Contains("CommandReasons.NotRunning", otherwise)
        Assert.Equal(CommandExit.NotReady, CommandReasons.ExitOf(CommandReasons.NotRunning))
        Assert.Equal(Manifest().Commands.Count, VerbsOf("start").Length + VerbsOf("oneshot").Length + refuse.Length)
    End Sub

    ' --- C11 -----------------------------------------------------------------------------------------

    <Fact>
    Public Sub C11_result_appears_whole_and_no_verb_writes_a_file_it_was_not_told_to()
        Dim dir As String = ScratchFolder()
        Try
            Dim target As String = Path.Combine(dir, "r.json")
            Dim data As New JsonObject()
            data.Add("outcome", "sent")
            data.Add("bytes", 5)
            CommandFiles.WriteResult(target, data)
            Dim value As Object = Nothing
            Assert.True(CommandJson.TryParse(File.ReadAllText(target), value), "the result file is not whole JSON")
            Assert.Equal("sent", DirectCast(value, JsonObject).GetString("outcome"))
            Assert.Single(Directory.GetFiles(dir))
            ' A second run replaces it whole, leaving no half-written sibling behind.
            CommandFiles.WriteResult(target, Nothing)
            Assert.Equal("{}" & vbLf, File.ReadAllText(target))
            Assert.Single(Directory.GetFiles(dir))
        Finally
            Directory.Delete(dir, True)
        End Try
        ' The Share Manager declares no --out: the half of C11 about a named output file has no verb to apply to here, and the
        ' shared writer that the viewer's screenshot uses is held by the viewer's suite.
        For Each spec As CommandSpec In Manifest().Commands
            Assert.True(spec.FindOption("out") Is Nothing, spec.Name & " declares --out")
        Next
        ' The observe, clipboard and schedule verbs offer --result; the client hands it over with the answer's data.
        For Each name As String In {"status", "clipboard send", "clipboard get", "share list", "schedule list", "exchange status",
                                    "schedule add", "schedule remove", "schedule run", "schedule history"}
            Assert.True(Manifest().Find(name).FindOption("result") IsNot Nothing, name & " has no --result")
        Next
    End Sub

    ' --- C12 -----------------------------------------------------------------------------------------

    <Fact>
    Public Sub C12_what_the_clipboard_carries_is_in_no_log_line_and_no_data()
        For Each spec As CommandSpec In Manifest().Commands
            Assert.True(spec.FindOption("secret-file") Is Nothing, spec.Name & " takes a secret by file; this program has no such verb")
        Next
        ' The clipboard is the only secret this program moves; the answer says what happened and how many bytes, never what.
        Dim outcome As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.ReceivedValue), 11L)
        Dim data As JsonObject = outcome.ToData()
        Assert.Equal(2, data.Count)
        Assert.Equal("received", data.GetString("outcome"))
        Dim bytes As Object = Nothing
        Assert.True(data.TryGet("bytes", bytes))
        Assert.Equal(11, Convert.ToInt32(bytes))
        Dim request As CommandRequest = Req("clipboard get", args:={"--result", "D:\Private\answer.json"})
        Dim response As CommandResponse = outcome.ToResponse(request.RequestId, outcome.Text)
        Dim line As String = CommandDispatch.DescribeForLog(request, response)
        Assert.DoesNotContain("Private", line)
        Assert.DoesNotContain("answer.json", line)
        Assert.Contains("clipboard get", line)
        Assert.Contains("--result", line)
        Assert.DoesNotContain("D:\Private", response.ToLine())
    End Sub

    ' --- C13 -----------------------------------------------------------------------------------------

    ''' <summary>One shipped spelling: it is an alias of exactly the named verb, it is no command (it keeps its old transport), and
    ''' the old launch it names asks for exactly what the verb forwards to an older manager.</summary>
    Private Shared Sub SpellingBehavesAsItsVerb(alias_ As String, launchArgument As String, ParamArray verbWords As String())
        Dim m As CommandManifest = Manifest()
        Dim verb As String = String.Join(" ", verbWords.Where(Function(w) Not w.StartsWith("-", StringComparison.Ordinal) AndAlso
                                                                      w.IndexOf(":"c) < 0 AndAlso w.IndexOf("\"c) < 0).ToArray())
        Dim owners As New List(Of String)()
        For Each spec As CommandSpec In m.Commands
            If spec.Aliases.Contains(alias_) Then owners.Add(spec.Name)
        Next
        Assert.Equal(1, owners.Count)
        Assert.Equal(verb, owners(0))
        Dim args As String() = If(launchArgument.Length = 0, New String() {}, New String() {launchArgument})
        If args.Length > 0 Then
            Assert.False(ShareManagerCommandTable.IsCommandLine(args), launchArgument & " became a command")
        End If
        Dim parsed As ParsedCommand = Nothing
        Assert.True(CommandLineParser.Parse(verbWords, m, parsed) Is Nothing, "the verb words do not parse: " & String.Join(" ", verbWords))
        Dim badToken As String = Nothing
        Dim folder As String = LaunchArguments.TargetOf(parsed, "C:\Work", badToken)
        Assert.Null(badToken)
        Assert.Equal(LaunchArguments.LegacyForwardOf(parsed, folder), LaunchArguments.Classify(args).ForwardPayload())
    End Sub

    <Fact>
    Public Sub C13_every_shipped_spelling_behaves_as_its_verb()
        SpellingBehavesAsItsVerb("", "", "start")
        SpellingBehavesAsItsVerb("--tray", "--tray", "start", "--silent")
        SpellingBehavesAsItsVerb("/tray", "/tray", "start", "--silent")
        SpellingBehavesAsItsVerb("--show", "--show", "show")
        SpellingBehavesAsItsVerb("/show", "/show", "show")
        SpellingBehavesAsItsVerb("::fms-show-window::", "::fms-show-window::", "show")
        SpellingBehavesAsItsVerb("<folder>", "D:\Data", "share", "add", "D:\Data")
        SpellingBehavesAsItsVerb("--add-resource", "--add-resource", "resource", "add")
        SpellingBehavesAsItsVerb("/add-resource", "/add-resource", "resource", "add")
        SpellingBehavesAsItsVerb("::fms-add-resource::", "::fms-add-resource::", "resource", "add")
        SpellingBehavesAsItsVerb("--add-resource-camera", "--add-resource-camera", "resource", "add", "--camera")
        SpellingBehavesAsItsVerb("/add-resource-camera", "/add-resource-camera", "resource", "add", "--camera")
        SpellingBehavesAsItsVerb("::fms-add-resource-camera::", "::fms-add-resource-camera::", "resource", "add", "--camera")
        SpellingBehavesAsItsVerb("::fms-schedule::", "::fms-schedule::", "schedule", "open")
        SpellingBehavesAsItsVerb("::fms-schedule::<folder>", "::fms-schedule::D:\Data", "schedule", "open", "--folder", "D:\Data")
        SpellingBehavesAsItsVerb("::fms-broadcast::<path>", "::fms-broadcast::D:\Media", "broadcast", "open", "--path", "D:\Media")
        SpellingBehavesAsItsVerb("--clipboard-send", "--clipboard-send", "clipboard", "send")
        SpellingBehavesAsItsVerb("/clipboard-send", "/clipboard-send", "clipboard", "send")
        SpellingBehavesAsItsVerb("::fms-clipboard-send::", "::fms-clipboard-send::", "clipboard", "send")
        SpellingBehavesAsItsVerb("--clipboard-get", "--clipboard-get", "clipboard", "get")
        SpellingBehavesAsItsVerb("/clipboard-get", "/clipboard-get", "clipboard", "get")
        SpellingBehavesAsItsVerb("::fms-clipboard-get::", "::fms-clipboard-get::", "clipboard", "get")
        ' The plain-start marker a second launch forwards is the payload of the verb start.
        Assert.Equal("::fms-plain-start::", Program.PlainStartCommand)
        Assert.True(Manifest().Find("start").Aliases.Contains(Program.PlainStartCommand))
    End Sub

End Class
