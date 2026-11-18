Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Xunit
Imports fmsl.AppCommands
Imports fmsl.Streams

''' <summary>
''' Ticket L0320, contract APP-COMMANDS 0.1 section 4: the cases C1 to C13, one fact (or, for C10, one fact per
''' cold-start mode) each, for the viewer. Every fact is the decision half of its case - what the library, the
''' command table and the client's own text promise, held without a window or a second process. The process half
''' (two real processes, a real window count) is <c>tests/Integration/AppCommandScenes.ps1</c>; C5 and C9 are
''' manual rungs by the contract itself and their pure halves are here.
''' </summary>
Public Class AppCommandsConformanceTests

    Private Shared Function Manifest() As CommandManifest
        Return ViewerCommandTable.Build()
    End Function

    Private Shared Function Client() As String
        Return TextOf("src", "AppCommands", "ViewerCommandClient.vb")
    End Function

    Private Shared ReadOnly BuildSpellings As String() = {"--build-stream-catalog", "-build-stream-catalog", "/build-stream-catalog",
                                                          "--build-catalog", "-build-catalog", "/build-catalog"}
    Private Shared ReadOnly CheckSpellings As String() = {"--check-stream-catalog", "-check-stream-catalog", "/check-stream-catalog",
                                                          "--check-catalog", "-check-catalog", "/check-catalog"}

    ' --- C1 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C1_commands_json_is_the_manifest_of_the_table_and_starts_no_window()
        Dim m As CommandManifest = Manifest()
        Dim spec As CommandSpec = m.Find("commands")
        Assert.Equal("oneshot", spec.ColdStart)
        Assert.Equal(CommandRisk.Safe, spec.Risk)
        ' What is printed is the table's own text, and the document in the repository is the same text.
        Assert.True(String.Equals(m.ToJsonText(), TextOf("docs", "cli-commands.json"), StringComparison.Ordinal),
                    "docs/cli-commands.json differs from what commands --json prints")
        ' The shape the catalog's schema asks for (the schema file itself is the catalog's; the members are held here).
        Dim value As Object = Nothing
        Assert.True(CommandJson.TryParse(m.ToJsonText(), value))
        Dim root As JsonObject = DirectCast(value, JsonObject)
        Assert.Equal(1, root.GetInt("schemaVersion", 0))
        For Each member As String In {"schemaVersion", "product", "exe", "cliVersion", "commands"}
            Dim ignored As Object = Nothing
            Assert.True(root.TryGet(member, ignored), "the manifest lacks " & member)
        Next
        ' No window: the verb is answered before any delivery, any mutex and any form.
        Dim text As String = Client()
        Dim block As String = Slice(text, "Case ""commands""", "Case ""version""")
        Assert.True(block.Length > 0, "the commands case is not in the client")
        Assert.Contains("ToJsonText()", block)
        Assert.Contains("Return True", block)
        Assert.DoesNotContain("Main_Form", block)
        Assert.True(text.IndexOf("Case ""commands""", StringComparison.Ordinal) < text.IndexOf("Deliver(request)", StringComparison.Ordinal))
    End Sub

    ' --- C2 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C2_version_is_done_and_status_without_a_viewer_is_exit_11_not_running()
        Dim m As CommandManifest = Manifest()
        Assert.Equal("oneshot", m.Find("version").ColdStart)
        Assert.Equal("refuse", m.Find("status").ColdStart)
        Dim done As CommandResponse = CommandResponse.Done("r", "26.11.14.0001")
        Assert.Equal(CommandExit.Done, done.ExitCode)
        Assert.Equal(CommandStatus.Done, done.Status)
        Dim none As CommandResponse = CommandResponse.ForReason("r", CommandReasons.NotRunning, "not running")
        Assert.Equal(11, none.ExitCode)
        Assert.Equal(CommandReasons.NotRunning, none.Reason)
        ' The client: version answers itself with the product version; a refuse verb with nobody running answers not-running.
        Dim text As String = Client()
        Dim version As String = Slice(text, "Case ""version""", "Case ""stream-catalog build""")
        Assert.Contains("ProductVersion", version)
        Assert.Contains("CommandResponse.Done(", version)
        Dim noTarget As String = Slice(text, "Case Delivery.NoTarget", "Case Delivery.OlderCopy")
        Assert.Contains("CommandReasons.NotRunning", noTarget)
    End Sub

    ' --- C3 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C3_the_first_instance_takes_every_verb_and_the_caller_leaves_after_the_answer()
        Dim m As CommandManifest = Manifest()
        For Each spec As CommandSpec In m.Commands
            If AnsweredByTheCaller.Contains(spec.Name) Then Continue For
            Dim decision As DispatchDecision = CommandDispatch.Decide(Req(spec.Name, args:=ValidArgs(spec)), m, True, False, False)
            Assert.True(decision.RefusalReason Is Nothing, spec.Name & " was refused: " & decision.RefusalReason)
            Assert.Equal(spec.Name, decision.Parsed.Spec.Name)
        Next
        ' The caller waits for the reply file named by its own request id, prints the answer and ends.
        Dim text As String = Client()
        Assert.Contains("CommandFiles.ReplyPathFor(ViewerCommandTable.ReplyFolder, requestId)", text)
        Dim tail As String = Slice(text, "Dim response As CommandResponse = WaitForReply(", "End Function")
        Assert.Contains("Emit(", tail)
        Assert.Contains("Return True", tail)
        Assert.DoesNotContain("TryColdStart", tail)
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
        ' The client prints the refusal and ends before it asks any instance.
        Dim text As String = Client()
        Dim refusal As String = Slice(text, "If refusal IsNot Nothing Then", "End If")
        Assert.Contains("Return True", refusal)
        Assert.True(text.IndexOf("If refusal IsNot Nothing Then", StringComparison.Ordinal) < text.IndexOf("Deliver(request)", StringComparison.Ordinal))
    End Sub

    ' --- C5 (manual rung: a first instance that pumps no messages; the bound and the mapping are facts) ---

    <Fact>
    Public Sub C5_a_receiver_that_never_answers_is_given_up_inside_the_delivery_bound_as_exit_2()
        Assert.Equal(1000, CommandBounds.DeliveryMs)
        Assert.Equal(CommandExit.NotProven, CommandReasons.ExitOf(CommandReasons.Timeout))
        Assert.Equal(CommandExit.NotReady, CommandReasons.ExitOf(CommandReasons.Busy))
        Dim text As String = Client()
        Dim deliver As String = Slice(text, "Private Function Deliver(", "Private Function SendOnce(")
        Assert.Contains("CommandBounds.DeliveryMs - CInt(watch.ElapsedMilliseconds)", deliver)
        Assert.Contains("If remaining <= 0 Then Exit Do", deliver)
        Assert.Contains("SendOnce(handle, payload, remaining)", deliver)
        Dim sendOnce As String = Slice(text, "Private Function SendOnce(", "Private Sub ForwardLegacyShow(")
        Assert.Contains("SMTO_ABORTIFHUNG", sendOnce)
        Assert.Contains("timeoutMs", sendOnce)
        Dim noAnswer As String = Slice(text, "Case Delivery.NoAnswer", "End Select")
        Assert.Contains("CommandReasons.Timeout", noAnswer)
    End Sub

    ' --- C6 ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub C6_a_modal_window_answers_busy_11_to_every_verb_that_reaches_the_first_instance()
        Dim m As CommandManifest = Manifest()
        For Each spec As CommandSpec In m.Commands
            If AnsweredByTheCaller.Contains(spec.Name) Then Continue For
            Dim decision As DispatchDecision = CommandDispatch.Decide(Req(spec.Name, args:=ValidArgs(spec)), m, True, True, False)
            Assert.Equal(CommandReasons.Busy, decision.RefusalReason)
            Assert.Equal(CommandExit.NotReady, CommandReasons.ExitOf(decision.RefusalReason))
            Assert.Equal(CommandStatus.Busy, CommandReasons.StatusOf(decision.RefusalReason))
            Assert.Null(decision.Parsed)
        Next
        ' The receiver's rule: a window of its own, the welcome wizard or a disabled form is busy.
        Dim receiver As String = TextOf("src", "Main_Form.AppCommands.vb")
        Dim busy As String = Slice(receiver, "Private Function AppCommandBusy()", "End Function")
        Assert.True(busy.Length > 0, "AppCommandBusy is not in the receiver")
        Assert.Contains("f.Modal", busy)
        Assert.Contains("welcome_Open", busy)
        Assert.Contains("Not Enabled", busy)
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
        Dim m As CommandManifest = Manifest()
        Dim restricted As Integer = 0
        For Each spec As CommandSpec In m.Commands
            If AnsweredByTheCaller.Contains(spec.Name) Then Continue For
            Dim off As DispatchDecision = CommandDispatch.Decide(Req(spec.Name, args:=ValidArgs(spec)), m, False, False, False)
            Dim on_ As DispatchDecision = CommandDispatch.Decide(Req(spec.Name, args:=ValidArgs(spec)), m, True, False, False)
            Assert.Null(on_.RefusalReason)
            If String.Equals(spec.Risk, CommandRisk.Safe, StringComparison.Ordinal) Then
                Assert.Null(off.RefusalReason)
            Else
                restricted += 1
                Assert.Equal(CommandReasons.SwitchOff, off.RefusalReason)
                Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(off.RefusalReason))
                Assert.Equal(CommandStatus.Refused, CommandReasons.StatusOf(off.RefusalReason))
            End If
        Next
        Assert.True(restricted > 0, "the viewer declares no verb above safe")
        ' The sentence names the setting, in the program's language and in the generated document.
        Assert.Contains("SettingLabel()", Slice(TextOf("src", "AppCommands", "AppCommandsText.vb"), "Case CommandReasons.SwitchOff", "Case CommandReasons.Busy"))
        Assert.Contains(ViewerCommandTable.SettingLabelEnglish, TextOf("docs", "CLI.md"))
    End Sub

    ' --- C9 (manual rung: another user or session, a real elevated copy; the refusal and the folder are facts) ---

    <Fact>
    Public Sub C9_an_elevated_first_instance_refuses_a_caller_that_is_not_and_replies_only_into_the_users_folder()
        Dim m As CommandManifest = Manifest()
        Dim ordinary As DispatchDecision = CommandDispatch.Decide(Req("status"), m, True, False, True)
        Assert.Equal(CommandReasons.Elevation, ordinary.RefusalReason)
        Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(ordinary.RefusalReason))
        Assert.Null(CommandDispatch.Decide(Req("status", callerElevated:=True), m, True, False, True).RefusalReason)
        Assert.Null(CommandDispatch.Decide(Req("status", callerElevated:=True), m, True, False, False).RefusalReason)
        ' A reply goes nowhere but the user's own temporary folder, named by the request id.
        Dim folder As String = CommandFiles.ReplyDirectory(ViewerCommandTable.ReplyFolder)
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
        Dim elsewhere As CommandRequest = Req("status")
        elsewhere.ReplyPath = Path.Combine(Path.GetPathRoot(Path.GetTempPath()), "other-place", elsewhere.RequestId & ".reply.json")
        Assert.False(CommandFiles.IsValidReplyPath(ViewerCommandTable.ReplyFolder, elsewhere))
        Dim own As CommandRequest = Req("status")
        own.ReplyPath = CommandFiles.ReplyPathFor(ViewerCommandTable.ReplyFolder, own.RequestId)
        Assert.True(CommandFiles.IsValidReplyPath(ViewerCommandTable.ReplyFolder, own))
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
    Public Sub C10_start_verbs_become_the_first_instance()
        Assert.Equal({"show", "open", "phone-code receive"}, VerbsOf("start"))
        Dim noTarget As String = Slice(Client(), "Case Delivery.NoTarget", "Case Delivery.OlderCopy")
        Dim start As String = Slice(noTarget, "Case ""start""", "Case ""oneshot""")
        Assert.Contains("TryColdStart(", start)
        ' This process goes on as the viewer: it returns False, so the entry continues to the mutex and the window.
        Assert.Contains("Return False", start)
        Dim entry As String = TextOf("src", "Application_Events.vb")
        Assert.True(entry.IndexOf("ViewerCommandClient.TryRun(", StringComparison.Ordinal) > 0)
    End Sub

    <Fact>
    Public Sub C10_oneshot_verbs_are_decided_before_the_mutex_and_leave_nothing_behind()
        Assert.Equal({"commands", "version", "quit", "stream-catalog build", "stream-catalog check"}, VerbsOf("oneshot"))
        Dim entry As String = TextOf("src", "Application_Events.vb")
        Dim callAt As Integer = entry.IndexOf("ViewerCommandClient.TryRun(", StringComparison.Ordinal)
        Dim mutexAt As Integer = entry.IndexOf("New Mutex(", StringComparison.Ordinal)
        Assert.True(callAt > 0 AndAlso mutexAt > callAt, "the commands are decided after the mutex is taken")
        Dim noTarget As String = Slice(Client(), "Case Delivery.NoTarget", "Case Delivery.OlderCopy")
        Dim oneshot As String = Slice(noTarget, "Case ""oneshot""", "Case Else")
        Assert.Contains("Return True", oneshot)
        Assert.DoesNotContain("TryColdStart", oneshot)
        ' A stream-catalog verb runs in the calling process and never touches a window.
        Dim catalog As String = Slice(Client(), "Private Function RunStreamCatalog(", "Private Function SafeFullPath(")
        Assert.DoesNotContain("Main_Form", catalog.Replace("Main_Form.COPYDATASTRUCT", String.Empty))
    End Sub

    <Fact>
    Public Sub C10_refuse_verbs_with_nothing_running_are_exit_11()
        Dim refuse As String() = VerbsOf("refuse")
        Assert.Equal({"status", "file current", "file next", "file previous", "file first", "file last", "file goto",
                      "slideshow start", "slideshow stop", "screenshot window"}, refuse)
        Dim noTarget As String = Slice(Client(), "Case Delivery.NoTarget", "Case Delivery.OlderCopy")
        Dim otherwise As String = Slice(noTarget, "Case Else", "End Select")
        Assert.Contains("CommandReasons.NotRunning", otherwise)
        Assert.Equal(CommandExit.NotReady, CommandReasons.ExitOf(CommandReasons.NotRunning))
        ' Every declared mode is one of the three the contract names.
        Assert.Equal(Manifest().Commands.Count, VerbsOf("start").Length + VerbsOf("oneshot").Length + refuse.Length)
    End Sub

    ' --- C11 -----------------------------------------------------------------------------------------

    <Fact>
    Public Sub C11_result_appears_whole_and_out_is_written_only_where_named_and_kept_without_overwrite()
        Dim dir As String = ScratchFolder()
        Try
            Dim target As String = Path.Combine(dir, "r.json")
            Dim data As New JsonObject()
            data.Add("pid", 7)
            CommandFiles.WriteResult(target, data)
            Dim value As Object = Nothing
            Assert.True(CommandJson.TryParse(File.ReadAllText(target), value), "the result file is not whole JSON")
            Assert.Equal(7, DirectCast(value, JsonObject).GetInt("pid", 0))
            Assert.Single(Directory.GetFiles(dir))
            ' --out: an existing file survives without --overwrite, and nothing but the named file appears.
            Dim named As String = Path.Combine(dir, "o.bin")
            File.WriteAllText(named, "keep")
            Assert.False(CommandFiles.TryWriteOut(named, Encoding.ASCII.GetBytes("new"), False))
            Assert.Equal("keep", File.ReadAllText(named))
            Dim fresh As String = Path.Combine(dir, "fresh.bin")
            Assert.True(CommandFiles.TryWriteOut(fresh, Encoding.ASCII.GetBytes("new"), False))
            Assert.Equal("new", File.ReadAllText(fresh))
            Assert.Equal(3, Directory.GetFiles(dir).Length)
            Assert.True(CommandFiles.TryWriteOut(named, Encoding.ASCII.GetBytes("over"), True))
            Assert.Equal("over", File.ReadAllText(named))
        Finally
            Directory.Delete(dir, True)
        End Try
        ' A failed command writes no result file: the client hands --result over only for exit 0.
        Assert.Contains("If(response.ExitCode = CommandExit.Done, request.ResultPath, Nothing)", Client())
    End Sub

    ' --- C12 -----------------------------------------------------------------------------------------

    <Fact>
    Public Sub C12_a_secret_given_through_a_file_is_in_no_log_line_and_no_data()
        Dim dir As String = ScratchFolder()
        Try
            Dim marker As String = "S3CRET-" & Guid.NewGuid().ToString("N")
            Dim codeFile As String = Path.Combine(dir, "code.txt")
            File.WriteAllText(codeFile, marker & vbLf)
            Dim secret As String = Nothing
            Assert.True(CommandFiles.TryReadSecret(codeFile, secret))
            Assert.Equal(marker, secret)
            ' The request carries the path, never the secret; its log line carries an option name and a count.
            Dim request As CommandRequest = Req("phone-code receive", args:={"--secret-file", codeFile})
            Dim reply As CommandResponse = CommandResponse.Done(request.RequestId, "shown")
            Dim line As String = CommandDispatch.DescribeForLog(request, reply)
            Assert.DoesNotContain(marker, line)
            Assert.DoesNotContain(codeFile, line)
            Assert.Contains("--secret-file", line)
            Assert.DoesNotContain(marker, request.ToLine())
            Assert.DoesNotContain(marker, reply.ToLine())
        Finally
            Directory.Delete(dir, True)
        End Try
        ' The receiver hands the code to the queued dialog action only: no log call, no reply member takes it.
        Dim receiver As String = TextOf("src", "Main_Form.AppCommands.vb")
        For Each text As String In receiver.Split(ControlChars.Lf)
            If text.IndexOf("secret", StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            If text.TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
            If text.IndexOf("secret-file", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Assert.DoesNotContain("AppFileLogger", text)
            Assert.DoesNotContain("data.Add", text)
            Assert.DoesNotContain("CommandResponse.", text)
        Next
    End Sub

    ' --- C13 -----------------------------------------------------------------------------------------

    <Fact>
    Public Sub C13_every_shipped_spelling_behaves_as_its_verb()
        Dim m As CommandManifest = Manifest()
        Dim build As CommandSpec = m.Find("stream-catalog build")
        Dim check As CommandSpec = m.Find("stream-catalog check")
        ' The six switch spellings of each stream-catalog verb are the very words the entry still treats as the switch,
        ' and each is an alias of its own verb only.
        For Each spelling As String In BuildSpellings
            Assert.True(StreamCatalogCli.IsCatalogCommandLine({spelling}), spelling & " is not a catalog switch any more")
            Assert.Contains(spelling, build.Aliases)
            Assert.False(check.Aliases.Contains(spelling), spelling & " is also an alias of check")
        Next
        For Each spelling As String In CheckSpellings
            Assert.True(StreamCatalogCli.IsCatalogCommandLine({spelling}), spelling & " is not a catalog switch any more")
            Assert.Contains(spelling, check.Aliases)
            Assert.False(build.Aliases.Contains(spelling), spelling & " is also an alias of build")
        Next
        ' The launch spellings stay ordinary launches (never a command), so they keep their transport and their behaviour.
        For Each spelling As String In {"::fms-show-window::", "-NoBack", "::fms-phone-code::abc", "::fms-phone-code-file::C:\x\code.txt"}
            Assert.False(ViewerCommandTable.IsCommandLine({spelling}), spelling & " became a command")
        Next
        Assert.False(ViewerCommandTable.IsCommandLine(New String() {}))
        ' ... and each reads the same as its verb: the verb hands the one entry exactly the old text.
        Dim noBack As Boolean
        Assert.Equal("C:\a b\c.jpg", OpenTargets.SplitLegacyArgument(OpenTargets.ToLegacyArgument("C:\a b\c.jpg", True), noBack))
        Assert.True(noBack)
        Assert.Equal(OpenTargetClass.Internal, OpenTargets.Classify("::fms-phone-code::abc"))
        Assert.Equal(OpenTargetClass.Internal, OpenTargets.Classify("::fms-phone-code-file::C:\x\code.txt"))
        Assert.Equal({"", "::fms-show-window::"}, m.Find("show").Aliases.ToArray())
        Assert.Equal({"<path>", "-NoBack <path>", "<path> -NoBack"}, m.Find("open").Aliases.ToArray())
        Assert.Equal({"::fms-phone-code::", "::fms-phone-code-file::"}, m.Find("phone-code receive").Aliases.ToArray())
    End Sub

End Class
