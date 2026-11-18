Option Strict On

Imports System.Collections.Generic
Imports FastMediaSorterCompanion
Imports FastMediaSorterCompanion.AppCommands
Imports Xunit

''' <summary>
''' The Share Manager's command line (ticket L0316, APP-COMMANDS 0.1 rules 2, 7, 9, 10, 13): the table of verbs, the strict parse
''' every verb goes through, the spellings the program shipped before the table (they stay ordinary launches and keep their
''' payloads), the mapping of a verb onto the launch plan and onto the payload an older manager still understands, the gate by
''' risk class and the clipboard verbs' outcome tokens and exit codes. Pure: no window, no mutex, no second process - the
''' two-process round trips are a hand check of the release.
''' </summary>
Public Class ShareManagerCommandsTests

    Private Shared Function Plan(ParamArray args As String()) As LaunchPlan
        Return LaunchArguments.Classify(args)
    End Function

    Private Shared Function ParseOk(ParamArray words As String()) As ParsedCommand
        Dim parsed As ParsedCommand = Nothing
        Dim refusal As ParseRefusal = CommandLineParser.Parse(words, ShareManagerCommandTable.Build(), parsed)
        Assert.True(refusal Is Nothing, "refused: " & If(refusal Is Nothing, "", refusal.Detail))
        Return parsed
    End Function

    ''' <summary>The payload an older manager is sent for a verb, the folder resolved against a fixed working directory.</summary>
    Private Shared Function LegacyOf(ParamArray words As String()) As String
        Dim parsed As ParsedCommand = ParseOk(words)
        Dim badToken As String = Nothing
        Dim folder As String = LaunchArguments.TargetOf(parsed, "C:\Work", badToken)
        Assert.Null(badToken)
        Return LaunchArguments.LegacyForwardOf(parsed, folder)
    End Function

    Private Shared Function Req(command As String, Optional origin As String = "cli", Optional callerElevated As Boolean = False,
                                Optional schema As Integer = 1, Optional args As String() = Nothing) As CommandRequest
        Dim r As New CommandRequest With {.Command = command, .Origin = origin, .RequestId = Guid.NewGuid().ToString("D"),
                                          .CallerElevated = callerElevated, .SchemaVersion = schema}
        If args IsNot Nothing Then r.Args.AddRange(args)
        Return r
    End Function

    Private Shared Function Decide(request As CommandRequest, restrictedAllowed As Boolean, Optional busy As Boolean = False,
                                   Optional receiverElevated As Boolean = False) As DispatchDecision
        Return CommandDispatch.Decide(request, ShareManagerCommandTable.Build(), restrictedAllowed, busy, receiverElevated)
    End Function

    ' --- the table ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_table_holds_the_nineteen_verbs_and_names_this_program()
        Dim m As CommandManifest = ShareManagerCommandTable.Build()
        Assert.Equal("FastMediaSorterCompanion.exe", m.Exe)
        Assert.Equal("1.3", ShareManagerCommandTable.CliVersion)
        Dim names As New List(Of String)()
        For Each c As CommandSpec In m.Commands
            names.Add(c.Name)
        Next
        Assert.Equal(New String() {"commands", "version", "status", "show", "quit", "start", "share add", "resource add",
                                   "schedule open", "broadcast open", "clipboard send", "clipboard get",
                                   "share list", "schedule list", "exchange status",
                                   "schedule add", "schedule remove", "schedule run", "schedule history"}, names.ToArray())
    End Sub

    <Theory>
    <InlineData("show", "start")>
    <InlineData("start", "start")>
    <InlineData("share add", "start")>
    <InlineData("resource add", "start")>
    <InlineData("schedule open", "start")>
    <InlineData("broadcast open", "start")>
    <InlineData("clipboard send", "oneshot")>
    <InlineData("clipboard get", "oneshot")>
    <InlineData("status", "refuse")>
    <InlineData("quit", "oneshot")>
    <InlineData("commands", "oneshot")>
    <InlineData("version", "oneshot")>
    <InlineData("share list", "refuse")>
    <InlineData("schedule list", "oneshot")>
    <InlineData("exchange status", "oneshot")>
    <InlineData("schedule add", "start")>
    <InlineData("schedule remove", "start")>
    <InlineData("schedule run", "start")>
    <InlineData("schedule history", "oneshot")>
    Public Sub Cold_start_of_each_verb_is_the_route_the_old_launch_took(verb As String, coldStart As String)
        Assert.Equal(coldStart, ShareManagerCommandTable.Build().Find(verb).ColdStart)
    End Sub

    <Fact>
    Public Sub Only_the_non_safe_verbs_are_gated_by_the_setting()
        Dim restricted As New List(Of String)()
        For Each c As CommandSpec In ShareManagerCommandTable.Build().Commands
            If c.Risk <> CommandRisk.Safe Then restricted.Add(c.Name & ":" & c.Risk)
        Next
        Assert.Equal(New String() {"share add:leaves-machine", "clipboard send:leaves-machine", "clipboard get:writes-user-data",
                                   "share list:reads-user-data", "schedule list:reads-user-data",
                                   "schedule add:writes-user-data", "schedule remove:writes-user-data",
                                   "schedule run:writes-user-data", "schedule history:reads-user-data"},
                     restricted.ToArray())
    End Sub

    ' --- rule 2: every spelling the program shipped stays an ordinary launch ------------------------------

    <Theory>
    <InlineData("--tray")>
    <InlineData("/tray")>
    <InlineData("--TRAY")>
    Public Sub The_logon_autostart_is_silent_and_forwards_nothing(arg As String)
        Dim p As LaunchPlan = Plan(arg)
        Assert.True(p.SilentTray)
        Assert.False(p.WindowRequested)
        Assert.Equal("", p.ForwardPayload())
    End Sub

    <Fact>
    Public Sub An_empty_command_line_is_a_plain_start()
        Dim p As LaunchPlan = Plan()
        Assert.False(p.SilentTray)
        Assert.False(p.WindowRequested)
        Assert.Null(p.Folder)
        Assert.Equal(Program.PlainStartCommand, p.ForwardPayload())
    End Sub

    <Theory>
    <InlineData("--show")>
    <InlineData("/show")>
    <InlineData("::fms-show-window::")>
    Public Sub Every_show_window_spelling_asks_for_the_window(arg As String)
        Dim p As LaunchPlan = Plan(arg)
        Assert.True(p.WindowRequested)
        Assert.Equal(Program.ShowWindowCommand, p.ForwardPayload())
    End Sub

    <Fact>
    Public Sub The_plain_start_marker_is_read_by_a_receiver_as_a_plain_start()
        Assert.Equal(CompanionCommandKind.PlainStart, CompanionCommand.Parse("::fms-plain-start::").Kind)
        Assert.Equal(CompanionCommandKind.ShowWindow, CompanionCommand.Parse("::fms-show-window::").Kind)
    End Sub

    <Fact>
    Public Sub A_bare_folder_is_proposed_for_sharing()
        Dim p As LaunchPlan = Plan("D:\Data")
        Assert.Equal("D:\Data", p.Folder)
        Assert.False(p.WindowRequested)
        Assert.Equal("D:\Data", p.ForwardPayload())
    End Sub

    <Theory>
    <InlineData("--add-resource")>
    <InlineData("/add-resource")>
    <InlineData("::fms-add-resource::")>
    Public Sub Every_add_resource_spelling_opens_the_guided_add(arg As String)
        Dim p As LaunchPlan = Plan(arg)
        Assert.True(p.AddResourceRequested)
        Assert.False(p.AddResourceCameraRequested)
        Assert.True(p.WindowRequested)
        Assert.Equal(Program.AddResourceCommand, p.ForwardPayload())
    End Sub

    <Theory>
    <InlineData("--add-resource-camera")>
    <InlineData("/add-resource-camera")>
    <InlineData("::fms-add-resource-camera::")>
    Public Sub Every_camera_spelling_opens_the_guided_add_with_the_camera(arg As String)
        Dim p As LaunchPlan = Plan(arg)
        Assert.True(p.AddResourceCameraRequested)
        Assert.True(p.WindowRequested)
        Assert.Equal(Program.AddResourceCameraCommand, p.ForwardPayload())
    End Sub

    <Fact>
    Public Sub The_schedule_marker_with_a_folder_keeps_its_payload()
        Dim p As LaunchPlan = Plan("::fms-schedule::D:\Data")
        Assert.True(p.WindowRequested)
        Assert.Equal("::fms-schedule::D:\Data", p.SchedulePayload)
        Assert.Null(p.Folder)
        Assert.Equal("::fms-schedule::D:\Data", p.ForwardPayload())
    End Sub

    <Fact>
    Public Sub The_schedule_marker_alone_opens_the_page_and_an_invalid_folder_only_shows_the_window()
        Dim bare As LaunchPlan = Plan("::fms-schedule::")
        Assert.Equal("::fms-schedule::", bare.SchedulePayload)
        Dim invalid As LaunchPlan = Plan("::fms-schedule::..\up")
        Assert.True(invalid.WindowRequested)
        Assert.Equal("", invalid.SchedulePayload)
        Assert.Equal(Program.ShowWindowCommand, invalid.ForwardPayload())
    End Sub

    <Fact>
    Public Sub The_broadcast_marker_opens_the_window_and_is_never_taken_for_a_folder_to_share()
        ' L0316: a cold start used to take the marker for a bare folder (it has no leading dash) and never opened the window.
        Dim p As LaunchPlan = Plan("::fms-broadcast::D:\Media\clip.mp4")
        Assert.True(p.WindowRequested)
        Assert.Null(p.Folder)
        Assert.Equal("::fms-broadcast::D:\Media\clip.mp4", p.BroadcastPayload)
        Assert.Equal("::fms-broadcast::D:\Media\clip.mp4", p.ForwardPayload())
        Dim invalid As LaunchPlan = Plan("::fms-broadcast::relative\path")
        Assert.True(invalid.WindowRequested)
        Assert.Null(invalid.Folder)
        Assert.Equal("", invalid.BroadcastPayload)
        Assert.Equal(Program.ShowWindowCommand, invalid.ForwardPayload())
    End Sub

    <Theory>
    <InlineData("--clipboard-send")>
    <InlineData("/clipboard-send")>
    <InlineData("--CLIPBOARD-SEND")>
    <InlineData("::fms-clipboard-send::")>
    Public Sub Every_clipboard_send_spelling_is_the_send_shortcut(arg As String)
        Dim p As LaunchPlan = Plan(arg)
        Assert.True(p.ClipboardSendRequested)
        Assert.False(p.ClipboardGetRequested)
        Assert.False(p.WindowRequested)
        Assert.Equal(Program.ClipboardSendCommand, p.ForwardPayload())
    End Sub

    <Theory>
    <InlineData("--clipboard-get")>
    <InlineData("/clipboard-get")>
    <InlineData("::fms-clipboard-get::")>
    Public Sub Every_clipboard_get_spelling_is_the_get_shortcut(arg As String)
        Dim p As LaunchPlan = Plan(arg)
        Assert.True(p.ClipboardGetRequested)
        Assert.False(p.ClipboardSendRequested)
        Assert.Equal(Program.ClipboardGetCommand, p.ForwardPayload())
    End Sub

    <Fact>
    Public Sub Both_clipboard_switches_at_once_send_because_that_cannot_lose_data()
        Assert.Equal(Program.ClipboardSendCommand, Plan("--clipboard-get", "--clipboard-send").ForwardPayload())
    End Sub

    ' --- a command line is a command only when its first word is reserved (APP-COMMANDS 0.2 item A.1) ------------

    <Fact>
    Public Sub A_reserved_first_word_is_a_command_and_the_disk_is_never_probed()
        Assert.True(ShareManagerCommandTable.IsCommandLine(New String() {"status"}))
        Assert.True(ShareManagerCommandTable.IsCommandLine(New String() {"STATUS"}))
        Assert.True(ShareManagerCommandTable.IsCommandLine(New String() {"share", "add", "D:\x"}))
        ' A folder called "status" does not make the verb a path: the answer takes no file-system input.
        Assert.Equal(1, GetType(ShareManagerCommandTable).GetMethod("IsCommandLine", System.Reflection.BindingFlags.NonPublic Or System.Reflection.BindingFlags.Public Or System.Reflection.BindingFlags.Static).GetParameters().Length)
        ' A first word no verb or alias begins is not a command: a bare folder name keeps being proposed for sharing.
        Assert.False(ShareManagerCommandTable.IsCommandLine(New String() {"holiday"}))
    End Sub

    <Theory>
    <InlineData("--tray")>
    <InlineData("/show")>
    <InlineData("::fms-show-window::")>
    <InlineData("--clipboard-send")>
    <InlineData("D:\Data")>
    <InlineData("\\server\share")>
    Public Sub No_shipped_spelling_is_a_command(arg As String)
        Assert.False(ShareManagerCommandTable.IsCommandLine(New String() {arg}))
    End Sub

    <Fact>
    Public Sub An_empty_command_line_is_not_a_command()
        Assert.False(ShareManagerCommandTable.IsCommandLine(New String() {}))
        Assert.False(ShareManagerCommandTable.IsCommandLine(Nothing))
    End Sub

    ' --- a verb reaches the same plan, and the same payload an older manager still understands ------------

    <Fact>
    Public Sub Each_verb_has_the_payload_of_the_spelling_it_replaces()
        Assert.Equal(Program.ShowWindowCommand, LegacyOf("show"))
        Assert.Equal(Program.PlainStartCommand, LegacyOf("start"))
        Assert.Equal("", LegacyOf("start", "--silent"))
        Assert.Equal("D:\Data", LegacyOf("share", "add", "D:\Data"))
        Assert.Equal(Program.AddResourceCommand, LegacyOf("resource", "add"))
        Assert.Equal(Program.AddResourceCameraCommand, LegacyOf("resource", "add", "--camera"))
        Assert.Equal("::fms-schedule::", LegacyOf("schedule", "open"))
        Assert.Equal("::fms-schedule::D:\Data", LegacyOf("schedule", "open", "--folder", "D:\Data"))
        Assert.Equal(Program.ShowWindowCommand, LegacyOf("broadcast", "open"))
        Assert.Equal("::fms-broadcast::D:\Media", LegacyOf("broadcast", "open", "--path", "D:\Media"))
        Assert.Equal(Program.ClipboardSendCommand, LegacyOf("clipboard", "send"))
        Assert.Equal(Program.ClipboardGetCommand, LegacyOf("clipboard", "get"))
    End Sub

    <Fact>
    Public Sub A_verb_with_no_old_spelling_forwards_nothing_to_an_older_manager()
        Assert.Equal("", LegacyOf("status"))
        Assert.Equal("", LegacyOf("quit"))
        Assert.Equal("", LegacyOf("version"))
        Assert.Equal("", LegacyOf("commands"))
    End Sub

    <Fact>
    Public Sub A_verb_sets_the_same_plan_fields_the_spelling_sets()
        Dim silent As LaunchPlan = LaunchArguments.FromCommand(ParseOk("start", "--silent"), "")
        Assert.True(silent.SilentTray)
        Dim camera As LaunchPlan = LaunchArguments.FromCommand(ParseOk("resource", "add", "--camera"), "")
        Assert.True(camera.AddResourceCameraRequested)
        Assert.True(camera.WindowRequested)
        Dim share As LaunchPlan = LaunchArguments.FromCommand(ParseOk("share", "add", "D:\Data"), "D:\Data")
        Assert.Equal("D:\Data", share.Folder)
        Assert.False(share.WindowRequested)
        Dim send As LaunchPlan = LaunchArguments.FromCommand(ParseOk("clipboard", "send"), "")
        Assert.True(send.ClipboardSendRequested)
        Assert.True(send.ClipboardRequested)
    End Sub

    ' --- a folder or path a command carries ---------------------------------------------------------------

    <Fact>
    Public Sub A_relative_folder_is_read_against_the_working_directory_of_the_sender()
        Assert.Equal("D:\Work\Photos", LaunchArguments.ResolveFolder("Photos", "D:\Work"))
        Assert.Equal("D:\x", LaunchArguments.ResolveFolder("D:\x\", "C:\"))
        Assert.Equal("\\server\share\dir", LaunchArguments.ResolveFolder("\\server\share\dir", ""))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("D:\a*")>
    <InlineData("D:\a|b")>
    <InlineData("\\.\pipe\x")>
    <InlineData("D:\up\..\x")>
    Public Sub An_unacceptable_folder_is_refused(raw As String)
        Assert.Equal("", LaunchArguments.ResolveFolder(raw, "D:\Work"))
    End Sub

    <Fact>
    Public Sub A_relative_folder_without_a_usable_working_directory_is_refused()
        Assert.Equal("", LaunchArguments.ResolveFolder("Photos", ""))
        Assert.Equal("", LaunchArguments.ResolveFolder("Photos", "relative\dir"))
    End Sub

    <Fact>
    Public Sub A_bad_folder_names_the_option_the_person_typed_wrong()
        Dim bad As String = Nothing
        Assert.Equal("", LaunchArguments.TargetOf(ParseOk("share", "add", "D:\a*"), "D:\Work", bad))
        Assert.Equal("folder", bad)
        Assert.Equal("", LaunchArguments.TargetOf(ParseOk("schedule", "open", "--folder", "rel"), "", bad))
        Assert.Equal("--folder", bad)
        Assert.Equal("", LaunchArguments.TargetOf(ParseOk("broadcast", "open", "--path", "D:\up\..\x"), "D:\Work", bad))
        Assert.Equal("--path", bad)
    End Sub

    <Fact>
    Public Sub A_parent_step_in_a_relative_folder_is_the_senders_own_and_is_resolved_before_the_check()
        ' "..\Photos" typed in D:\Work means D:\Photos, as in any shell; an ABSOLUTE path that still carries ".." is refused.
        Dim bad As String = Nothing
        Assert.Equal("D:\Photos", LaunchArguments.TargetOf(ParseOk("share", "add", "..\Photos"), "D:\Work", bad))
        Assert.Null(bad)
    End Sub

    <Fact>
    Public Sub An_optional_folder_that_was_not_given_is_no_fault()
        Dim bad As String = "x"
        Assert.Equal("", LaunchArguments.TargetOf(ParseOk("schedule", "open"), "D:\Work", bad))
        Assert.Null(bad)
        Assert.Equal("", LaunchArguments.TargetOf(ParseOk("broadcast", "open"), "D:\Work", bad))
        Assert.Null(bad)
        Assert.Equal("", LaunchArguments.TargetOf(ParseOk("status"), "D:\Work", bad))
        Assert.Null(bad)
    End Sub

    ' --- the proposal verdict -----------------------------------------------------------------------------

    <Fact>
    Public Sub The_proposal_token_says_which_of_four_things_became_of_the_folder()
        Assert.Equal("queued", ShareManagerCommandTable.ProposalToken(True, False, False))
        Assert.Equal("duplicate", ShareManagerCommandTable.ProposalToken(False, True, False))
        Assert.Equal("already-shared", ShareManagerCommandTable.ProposalToken(False, False, True))
        Assert.Equal("full", ShareManagerCommandTable.ProposalToken(False, False, False))
        Assert.Equal("already-shared", ShareManagerCommandTable.ProposalToken(True, True, True))
    End Sub

    <Fact>
    Public Sub Every_proposal_token_has_a_sentence()
        For Each token As String In {ShareManagerCommandTable.ProposalQueued, ShareManagerCommandTable.ProposalDuplicate,
                                     ShareManagerCommandTable.ProposalAlreadyShared, ShareManagerCommandTable.ProposalFull}
            Assert.False(String.IsNullOrWhiteSpace(ShareManagerCommandText.ForProposal(token)))
        Next
    End Sub

    ' --- rules 10, 12, 13: the decision of the running manager --------------------------------------------

    <Theory>
    <InlineData("status")>
    <InlineData("show")>
    <InlineData("quit")>
    <InlineData("start")>
    <InlineData("resource add")>
    <InlineData("schedule open")>
    <InlineData("broadcast open")>
    <InlineData("exchange status")>
    Public Sub A_safe_verb_runs_with_the_setting_off(verb As String)
        Dim d As DispatchDecision = Decide(Req(verb), restrictedAllowed:=False)
        Assert.Null(d.RefusalReason)
        Assert.NotNull(d.Parsed)
    End Sub

    <Theory>
    <InlineData("share add", "D:\Data")>
    <InlineData("clipboard send", Nothing)>
    <InlineData("clipboard get", Nothing)>
    <InlineData("share list", Nothing)>
    <InlineData("schedule list", Nothing)>
    <InlineData("schedule remove", "op-1")>
    <InlineData("schedule run", "--all")>
    <InlineData("schedule history", Nothing)>
    Public Sub A_gated_verb_from_a_command_line_is_refused_with_the_setting_off(verb As String, arg As String)
        Dim args As String() = If(arg Is Nothing, Nothing, New String() {arg})
        Dim d As DispatchDecision = Decide(Req(verb, args:=args), restrictedAllowed:=False)
        Assert.Equal(CommandReasons.SwitchOff, d.RefusalReason)
        Assert.Equal(CommandExit.Permission, CommandReasons.ExitOf(d.RefusalReason))
        Assert.Null(d.Parsed)
    End Sub

    <Theory>
    <InlineData("share add", "D:\Data")>
    <InlineData("clipboard send", Nothing)>
    <InlineData("clipboard get", Nothing)>
    <InlineData("share list", Nothing)>
    <InlineData("schedule list", Nothing)>
    <InlineData("schedule remove", "op-1")>
    <InlineData("schedule run", "--all")>
    <InlineData("schedule history", Nothing)>
    Public Sub A_gated_verb_runs_with_the_setting_on_and_for_the_sibling_program(verb As String, arg As String)
        Dim args As String() = If(arg Is Nothing, Nothing, New String() {arg})
        Assert.Null(Decide(Req(verb, args:=args), restrictedAllowed:=True).RefusalReason)
        ' The viewer's own message is the person's click, not another program: not gated.
        Assert.Null(Decide(Req(verb, origin:="sibling", args:=args), restrictedAllowed:=False).RefusalReason)
    End Sub

    <Fact>
    Public Sub An_unknown_verb_a_newer_schema_a_missing_folder_and_an_elevation_gap_are_refused_with_their_reason()
        Assert.Equal(CommandReasons.UnknownVerb, Decide(Req("share remove"), True).RefusalReason)
        Assert.Equal(CommandReasons.Schema, Decide(Req("show", schema:=2), True).RefusalReason)
        ' A required positional that is missing is the parser's refusal, before the gate is looked at.
        Dim missing As DispatchDecision = Decide(Req("share add"), restrictedAllowed:=False)
        Assert.NotNull(missing.Refusal)
        Assert.Equal(CommandReasons.BadArgument, missing.RefusalReason)
        Assert.Equal(CommandReasons.Elevation, Decide(Req("show"), True, receiverElevated:=True).RefusalReason)
        Assert.Null(Decide(Req("show", callerElevated:=True), True, receiverElevated:=True).RefusalReason)
    End Sub

    <Fact>
    Public Sub Busy_is_answered_busy_except_for_the_verbs_that_ignore_it()
        Assert.Equal(CommandReasons.Busy, Decide(Req("show"), True, busy:=True).RefusalReason)
        Assert.Equal(CommandReasons.Busy, Decide(Req("quit"), True, busy:=True).RefusalReason)
        Assert.Equal(CommandReasons.Busy, Decide(Req("clipboard send"), True, busy:=True).RefusalReason)
        Assert.True(ShareManagerCommandTable.IgnoresBusy("status"))
        Assert.True(ShareManagerCommandTable.IgnoresBusy("share add"))
        ' The observe verbs (ticket L0319, L0345) only read, so a question that is open does not hide their answer.
        Assert.True(ShareManagerCommandTable.IgnoresBusy("share list"))
        Assert.True(ShareManagerCommandTable.IgnoresBusy("schedule list"))
        Assert.True(ShareManagerCommandTable.IgnoresBusy("schedule history"))
        Assert.True(ShareManagerCommandTable.IgnoresBusy("exchange status"))
        Assert.False(ShareManagerCommandTable.IgnoresBusy("show"))
        Assert.False(ShareManagerCommandTable.IgnoresBusy("quit"))
        Assert.False(ShareManagerCommandTable.IgnoresBusy("clipboard get"))
        Assert.False(ShareManagerCommandTable.IgnoresBusy("schedule add"))
        Assert.False(ShareManagerCommandTable.IgnoresBusy("schedule remove"))
        Assert.False(ShareManagerCommandTable.IgnoresBusy("schedule run"))
    End Sub

    <Fact>
    Public Sub The_log_line_of_a_command_carries_no_folder()
        Dim request As CommandRequest = Req("share add", args:=New String() {"D:\Private\Photos"})
        Dim line As String = CommandDispatch.DescribeForLog(request, CommandResponse.Done(request.RequestId, "x"))
        Assert.DoesNotContain("Private", line, StringComparison.Ordinal)
        Assert.DoesNotContain("D:\", line, StringComparison.Ordinal)
        Assert.Contains("share add", line, StringComparison.Ordinal)
    End Sub

    ' --- an older manager does not know the command tag --------------------------------------------------

    <Fact>
    Public Sub An_older_manager_reads_the_command_tag_as_not_ours_and_answers_zero()
        Assert.Equal(SenderGeneration.Unknown, ActivationSender.Classify(New IntPtr(ShareManagerCommandClient.Tag)))
        ' The program's own wake is still tagged as before, and the pre-tag generation is untouched.
        Assert.Equal(SenderGeneration.Tagged, ActivationSender.Classify(New IntPtr(CopyDataTransport.CompanionTag)))
        Assert.Equal(SenderGeneration.LegacyUntagged, ActivationSender.Classify(IntPtr.Zero))
    End Sub

    ' --- the clipboard verbs' outcome ---------------------------------------------------------------------

    <Theory>
    <InlineData("Sent", "sent", 0)>
    <InlineData("SentViaDrive", "sent", 0)>
    <InlineData("ReceivedValue", "received", 0)>
    <InlineData("ReceivedFile", "received", 0)>
    <InlineData("ReceivedValueViaDrive", "received", 0)>
    <InlineData("ReceivedFileViaDrive", "received", 0)>
    <InlineData("NothingToSend", "empty", 11)>
    <InlineData("ServerEmpty", "empty", 11)>
    <InlineData("EmptyViaDrive", "empty", 11)>
    <InlineData("JoinedLater", "empty", 11)>
    <InlineData("NoServer", "no-account", 11)>
    <InlineData("NotOffered", "no-account", 11)>
    <InlineData("DriveSignedOut", "no-account", 11)>
    <InlineData("OperationUnderWay", "busy", 11)>
    <InlineData("ClipboardBusy", "busy", 11)>
    <InlineData("TooLarge", "too-large", 13)>
    <InlineData("OverServerLimit", "too-large", 13)>
    <InlineData("OverDriveLimit", "too-large", 13)>
    <InlineData("DriveFull", "no-room", 13)>
    <InlineData("DriveDailyLimit", "no-room", 13)>
    <InlineData("DriveOffline", "no-answer", 15)>
    <InlineData("DriveUnavailable", "no-answer", 15)>
    <InlineData("Cancelled", "cancelled", 1)>
    <InlineData("PlaceFailed", "failed", 1)>
    <InlineData("SaveFailed", "failed", 1)>
    <InlineData("Incomplete", "failed", 1)>
    <InlineData("SealedFailed", "failed", 1)>
    Public Sub Each_result_line_has_one_token_and_one_exit_code(codeName As String, token As String, exitCode As Integer)
        ' The enum is Friend, so the case names it by member name and the public method exposes no internal type.
        Dim code As ClipboardLineCode = DirectCast([Enum].Parse(GetType(ClipboardLineCode), codeName), ClipboardLineCode)
        Dim outcome As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.OfLine(New ClipboardLine(code), 5L)
        Assert.Equal(token, outcome.Token)
        Assert.Equal(exitCode, outcome.ExitCode)
        Assert.True(CommandExit.IsKnown(outcome.ExitCode))
        Assert.False(String.IsNullOrWhiteSpace(outcome.Text))
    End Sub

    <Fact>
    Public Sub Every_result_line_code_maps_to_a_token_of_the_closed_set_and_a_known_exit_code()
        ' A new ClipboardLineCode member without a row lands on "failed" (exit 1) rather than on a token nobody documented.
        Dim closed As String() = {"sent", "received", "empty", "no-account", "busy", "too-large", "no-room", "no-answer", "cancelled", "failed"}
        For Each value As Object In [Enum].GetValues(GetType(ClipboardLineCode))
            Dim outcome As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.OfLine(New ClipboardLine(DirectCast(value, ClipboardLineCode)), 1L)
            Assert.True(Array.IndexOf(closed, outcome.Token) >= 0, value.ToString() & " -> " & outcome.Token)
            Assert.True(CommandExit.IsKnown(outcome.ExitCode))
        Next
    End Sub

    <Fact>
    Public Sub An_unreachable_server_is_no_answer_and_another_failure_is_failed()
        Dim unreachable As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.OfLine(
            New ClipboardLine(ClipboardLineCode.Failure, "srv", ConnectionDiagnosisMessages.UnreachableServer.Code), 0L)
        Assert.Equal("no-answer", unreachable.Token)
        Assert.Equal(CommandExit.Network, unreachable.ExitCode)
        Dim unanswered As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.OfLine(
            New ClipboardLine(ClipboardLineCode.Failure, "srv", ConnectionDiagnosisMessages.Unanswered.Code), 0L)
        Assert.Equal("no-answer", unanswered.Token)
        Dim tls As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.OfLine(
            New ClipboardLine(ClipboardLineCode.Failure, "srv", ConnectionDiagnosisMessages.TlsFailed.Code), 0L)
        Assert.Equal("failed", tls.Token)
        Assert.Equal(CommandExit.Failed, tls.ExitCode)
    End Sub

    <Fact>
    Public Sub The_byte_count_rides_only_with_a_success()
        Assert.Equal(5L, ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.Sent), 5L).Bytes)
        Assert.Equal(7L, ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.ReceivedValue), 7L).Bytes)
        Assert.Equal(0L, ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.NothingToSend), 5L).Bytes)
        Assert.Equal(0L, ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.TooLarge), 5L).Bytes)
        Assert.Equal(0L, ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.Sent), -3L).Bytes)
    End Sub

    <Fact>
    Public Sub A_send_to_several_servers_is_done_when_one_stored_it()
        Dim lines As New List(Of ClipboardLine) From {New ClipboardLine(ClipboardLineCode.NoServer), New ClipboardLine(ClipboardLineCode.Sent, "b")}
        Dim outcome As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.Of(New ClipboardRunResult(lines, True), 12L)
        Assert.Equal("sent", outcome.Token)
        Assert.Equal(CommandExit.Done, outcome.ExitCode)
        Assert.Equal(12L, outcome.Bytes)
    End Sub

    <Fact>
    Public Sub A_run_that_stored_nothing_is_judged_by_its_first_line()
        Dim lines As New List(Of ClipboardLine) From {New ClipboardLine(ClipboardLineCode.TooLarge)}
        Dim outcome As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.Of(New ClipboardRunResult(lines, False), 12L)
        Assert.Equal("too-large", outcome.Token)
        Assert.Equal(CommandExit.Resources, outcome.ExitCode)
        Dim none As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.Of(New ClipboardRunResult(New List(Of ClipboardLine)(), False), 0L)
        Assert.Equal("failed", none.Token)
        Assert.Equal(CommandExit.Failed, none.ExitCode)
        Assert.Equal("failed", ShareManagerClipboardOutcome.Of(Nothing, 0L).Token)
    End Sub

    <Fact>
    Public Sub A_success_answers_done_with_the_token_and_the_byte_count_that_survive_a_round_trip()
        Dim outcome As ShareManagerClipboardOutcome = ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.Sent), 5L)
        Dim response As CommandResponse = outcome.ToResponse("id-1", outcome.Text)
        Assert.Equal(CommandStatus.Done, response.Status)
        Assert.Equal(CommandExit.Done, response.ExitCode)
        Dim back As CommandResponse = Nothing
        Assert.True(CommandResponse.TryParse(response.ToLine(), back))
        Assert.Equal("sent", back.Data.GetString("outcome"))
        Assert.Equal(5, back.Data.GetInt("bytes", -1))
    End Sub

    <Fact>
    Public Sub Nothing_to_move_is_busy_with_exit_11_and_every_other_failure_keeps_its_token()
        Dim empty As CommandResponse = ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.ServerEmpty), 0L).ToResponse("id-2", "m")
        Assert.Equal(CommandStatus.Busy, empty.Status)
        Assert.Equal(CommandExit.NotReady, empty.ExitCode)
        Assert.Equal(CommandReasons.Busy, empty.Reason)
        Assert.Equal("empty", empty.Data.GetString("outcome"))
        Dim large As CommandResponse = ShareManagerClipboardOutcome.OfLine(New ClipboardLine(ClipboardLineCode.TooLarge), 0L).ToResponse("id-3", "m")
        Assert.Equal(CommandStatus.Failed, large.Status)
        Assert.Equal(CommandExit.Resources, large.ExitCode)
        Assert.Null(large.Reason)
        Assert.Equal("too-large", large.Data.GetString("outcome"))
    End Sub

    ' --- the words ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_not_running_sentence_names_the_share_manager_and_not_the_viewer()
        Dim text As String = ShareManagerCommandText.ForReason(CommandReasons.NotRunning)
        Assert.Contains(ProductNames.ShareManager, text, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub The_alias_objects_say_what_a_spelling_implies_and_which_give_no_answer_and_none_is_legacy()
        ' APP-COMMANDS 0.2 item E (L0324 Assumed 5: no Share Manager spelling is marked legacy until the owner decides).
        Dim m As CommandManifest = ShareManagerCommandTable.Build()
        Assert.Equal(New String() {"--silent"}, m.Find("start").AliasInfo("--tray").Implies)
        Assert.Equal(New String() {"--camera"}, m.Find("resource add").AliasInfo("--add-resource-camera").Implies)
        Assert.False(m.Find("clipboard get").AliasInfo("::fms-clipboard-get::").Answers)
        Assert.False(m.Find("show").AliasInfo.ContainsKey("--show"))
        For Each spec As CommandSpec In m.Commands
            Assert.False(spec.HasLegacyAlias, spec.Name)
        Next
        Dim json As String = CommandJson.ToLine(m.Find("start").ToJson())
        Assert.Contains("{""spelling"":""--tray"",""implies"":[""--silent""]}", json)
    End Sub

End Class
