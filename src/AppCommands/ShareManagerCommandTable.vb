Option Strict On
Option Explicit On

Imports System.Collections.Generic

' The Share Manager's command table (APP-COMMANDS rules 17, 18 and 20): the ONE place its verbs are
' declared. docs/cli-commands.share-manager.json, the Share Manager section of docs/CLI.md
' (tools/Build-CliDocs.ps1) and the output of "commands --json" are all rendered from Build();
' AppCommandsDocsTests fails when a document disagrees with it. Pure - linked into Lite.Tests and
' compiled by the generator on its own, never reading a window or the registry.
'
' Ticket L0316. Every spelling the Share Manager shipped before this table stays an alias of one
' verb and keeps its old transport and behaviour (rule 2); LegacyForm in the program names which verb
' each alias reaches, and this table lists them so the documents say so.
Namespace AppCommands

    Friend Module ShareManagerCommandTable

        Friend Const ExeName As String = "FastMediaSorterCompanion.exe"
        Friend Const CliVersion As String = "1.3"
        ''' <summary>The folder under %TEMP% that holds this program's reply files (CommandFiles.ReplyDirectory):
        ''' its own, so a reply path of the viewer is never a valid reply path here.</summary>
        Friend Const ReplyFolder As String = "FastMediaSorter-share-manager-commands"
        ''' <summary>The English label of the setting of rule 13, as docs/CLI.md names it - the one setting both programs read.</summary>
        Friend Const SettingLabelEnglish As String = "Let other programs ask this app to work with my files, screen, clipboard or the network"
        Friend Const SettingPlaceEnglish As String = "the viewer's Settings window, Files, Keys and associations; the Share Manager reads the same value"

        ' The closed set of clipboard outcome tokens (the "outcome" the two clipboard verbs return).
        Friend Const OutcomeSent As String = "sent"
        Friend Const OutcomeReceived As String = "received"
        Friend Const OutcomeEmpty As String = "empty"
        Friend Const OutcomeNoAccount As String = "no-account"
        Friend Const OutcomeBusy As String = "busy"
        Friend Const OutcomeTooLarge As String = "too-large"
        Friend Const OutcomeNoRoom As String = "no-room"
        Friend Const OutcomeNoAnswer As String = "no-answer"
        Friend Const OutcomeCancelled As String = "cancelled"
        Friend Const OutcomeFailed As String = "failed"

        ' The closed set of what became of a folder proposal (the "proposal" share add returns).
        Friend Const ProposalQueued As String = "queued"
        Friend Const ProposalDuplicate As String = "duplicate"
        Friend Const ProposalAlreadyShared As String = "already-shared"
        Friend Const ProposalFull As String = "full"

        ' The closed sets of the three observe verbs (ticket L0319, rule 12 spirit: a token never carries a name, a path or a
        ' credential). The data builders of the program (ShareManagerObserve) produce exactly these; the documents list them.
        ' share list: the state of a shared folder.
        Friend Const ShareServing As String = "serving"
        Friend Const ShareStopped As String = "stopped"
        ' schedule list: the action of an operation and how its last run ended.
        Friend Const ActionCopy As String = "copy"
        Friend Const ActionMove As String = "move"
        Friend Const ActionDelete As String = "delete"
        Friend Const ActionArchive As String = "archive"
        Friend Const ActionFillFreeSpace As String = "fill-free-space"
        Friend Const ActionBackup As String = "backup"
        ''' <summary>An action a newer build wrote that this one does not know: said as it is, never guessed.</summary>
        Friend Const ActionUnknown As String = "unknown"
        Friend Const RetentionGenerations As String = "generations"
        Friend Const RetentionKeepDays As String = "keep-days"
        Friend Const RetentionKeepDaysMonthly As String = "keep-days+keep-monthly"
        Friend Const ResultNone As String = "none"
        Friend Const ResultUnknown As String = "unknown"
        Friend Const ResultOk As String = "ok"
        Friend Const ResultFailed As String = "failed"
        Friend Const ResultRefused As String = "refused"
        Friend Const ResultWall As String = "wall"
        Friend Const ResultCancelled As String = "cancelled"
        Friend Const ResultSkipped As String = "skipped"
        ' exchange status: the last known state of the connection of this program's exchange account.
        Friend Const ExchangeNotConfigured As String = "not-configured"
        Friend Const ExchangeConnecting As String = "connecting"
        Friend Const ExchangeOnline As String = "online"
        Friend Const ExchangeReconnecting As String = "reconnecting"
        Friend Const ExchangeSignInAgain As String = "sign-in-again"
        Friend Const ExchangePinMismatch As String = "pin-mismatch"
        Friend Const ExchangeUpdateNeeded As String = "update-needed"
        Friend Const ExchangeDefect As String = "defect"
        Friend Const ExchangeStopped As String = "stopped"

        Private Function Ex(code As Integer, meaning As String) As CommandExitCode
            Return New CommandExitCode With {.Code = code, .Meaning = meaning}
        End Function

        Private Function Sample(shell As String, line As String, expect As String) As CommandExample
            Return New CommandExample With {.Shell = shell, .Line = line, .Expect = expect}
        End Function

        Private Function JsonArg() As CommandArg
            Return New CommandArg With {.Name = "json", .Kind = "bool", .Required = False, .DefaultValue = False,
                                        .Description = "print the response line instead of a sentence"}
        End Function

        Private Function ResultArg(what As String) As CommandArg
            Return New CommandArg With {.Name = "result", .Kind = "path", .Required = False,
                                        .Description = what}
        End Function

        Private Function Ret(name As String, kind As String, description As String) As CommandReturn
            Return New CommandReturn With {.Name = name, .Kind = kind, .Description = description}
        End Function

        Friend Function Build() As CommandManifest
            Dim m As New CommandManifest With {.Product = ProductNames.ShareManager, .Exe = ExeName, .CliVersion = CliVersion}

            Dim commands As New CommandSpec With {
                .Name = "commands", .Summary = "List the commands this program accepts.",
                .Synopsis = "commands [--json]", .ColdStart = "oneshot", .TimeoutMs = 5000, .Forward = "never",
                .Idempotent = True, .Since = "1.0"}
            commands.Args.Add(New CommandArg With {.Name = "json", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                   .Description = "print the manifest (docs/cli-commands.share-manager.json) instead of the readable list"})
            commands.ExitCodes.Add(Ex(0, "printed"))
            commands.ExitCodes.Add(Ex(10, "unknown option"))
            commands.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'commands --json' -RedirectStandardOutput C:\temp\fms-sm-commands.json -Wait",
                "exit 0 and the file equals docs/cli-commands.share-manager.json"))
            m.Commands.Add(commands)

            Dim version As New CommandSpec With {
                .Name = "version", .Summary = "Print the version of this program.",
                .Synopsis = "version [--json]", .ColdStart = "oneshot", .TimeoutMs = 5000, .Forward = "never",
                .Idempotent = True, .Since = "1.0"}
            version.Args.Add(JsonArg())
            version.Returns.Add(Ret("version", "string", "the program version"))
            version.Returns.Add(Ret("cliVersion", "string", "the version of the command set this manifest describes"))
            version.ExitCodes.Add(Ex(0, "printed"))
            version.ExitCodes.Add(Ex(10, "unknown option"))
            version.Examples.Add(Sample("cmd", "start /wait " & ExeName & " version", "exit 0 and the version on the console"))
            m.Commands.Add(version)

            Dim status As New CommandSpec With {
                .Name = "status", .Summary = "Report whether the Share Manager is running and ready for commands.",
                .Synopsis = "status [--json] [--result <path>]", .ColdStart = "refuse", .TimeoutMs = 2000,
                .Idempotent = True, .Since = "1.0"}
            status.Args.Add(JsonArg())
            status.Args.Add(ResultArg("write the returned data to this file, whole"))
            status.Returns.Add(Ret("version", "string", "the running program's version"))
            status.Returns.Add(Ret("pid", "int", "its process id"))
            status.Returns.Add(Ret("exe", "string", "its file name"))
            status.Returns.Add(Ret("ready", "bool", "true when no question or dialog blocks commands"))
            status.Returns.Add(Ret("acceptsRestrictedCommands", "bool", "whether the setting admits commands of a class other than safe"))
            status.Returns.Add(Ret("workerAvailable", "bool", "whether the sharing worker payload is installed"))
            status.Returns.Add(Ret("sharing", "bool", "whether sharing is switched on now"))
            status.ExitCodes.Add(Ex(0, "running and ready"))
            status.ExitCodes.Add(Ex(11, "not running, or busy"))
            status.Examples.Add(Sample("powershell",
                "(Start-Process .\" & ExeName & " -ArgumentList 'status' -Wait -PassThru).ExitCode",
                "0 while the manager runs and is ready, 11 otherwise"))
            m.Commands.Add(status)

            Dim show As New CommandSpec With {
                .Name = "show", .Summary = "Bring the Share Manager window forward, starting the program when it is not running.",
                .Synopsis = "show", .ColdStart = "start", .TimeoutMs = 5000, .Idempotent = True, .Since = "1.0"}
            show.Aliases.Add("--show")
            show.Aliases.Add("/show")
            show.AddAlias("::fms-show-window::", New CommandAliasInfo With {.Answers = False})
            show.ExitCodes.Add(Ex(0, "the window is forward"))
            show.ExitCodes.Add(Ex(2, "the running copy is older and must be restarted"))
            show.ExitCodes.Add(Ex(11, "busy: a question or a dialog is open in the running program"))
            show.Examples.Add(Sample("cmd", "start /wait " & ExeName & " show", "exit 0 and the window in front"))
            m.Commands.Add(show)

            Dim quit As New CommandSpec With {
                .Name = "quit", .Summary = "Close the running Share Manager, as the tray menu Exit does.",
                .Synopsis = "quit", .ColdStart = "oneshot", .TimeoutMs = 10000, .Idempotent = True, .Since = "1.0"}
            quit.ExitCodes.Add(Ex(0, "closed, or was not running"))
            quit.ExitCodes.Add(Ex(11, "busy: a question or a dialog is open in the running program"))
            quit.Examples.Add(Sample("cmd", "start /wait " & ExeName & " quit", "exit 0 and no Share Manager process is left"))
            m.Commands.Add(quit)

            Dim start As New CommandSpec With {
                .Name = "start", .Summary = "Make sure the Share Manager is running and apply its startup option as a plain launch does; with --silent it shows no window and no hint, as the logon autostart does.",
                .Synopsis = "start [--silent]", .ColdStart = "start", .TimeoutMs = 20000, .Idempotent = True, .Since = "1.0"}
            start.Aliases.Add("")
            start.AddAlias("--tray", New CommandAliasInfo With {.Implies = {"--silent"}})
            start.AddAlias("/tray", New CommandAliasInfo With {.Implies = {"--silent"}})
            start.AddAlias("::fms-plain-start::", New CommandAliasInfo With {.Answers = False})
            start.Args.Add(New CommandArg With {.Name = "silent", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                .Description = "show nothing: no window, no tray hint; the shipped --tray spelling means this"})
            start.ExitCodes.Add(Ex(0, "running"))
            start.ExitCodes.Add(Ex(2, "the running copy is older and must be restarted"))
            start.ExitCodes.Add(Ex(11, "busy: a question or a dialog is open in the running program"))
            start.Examples.Add(Sample("cmd", "start /wait " & ExeName & " start --silent", "exit 0; the tray icon is up and no window is shown"))
            m.Commands.Add(start)

            Dim shareAdd As New CommandSpec With {
                .Name = "share add", .Summary = "Propose a folder to share; the person answers the proposal in the manager before anything is shared.",
                .Synopsis = "share add <folder>", .ColdStart = "start", .Wait = "accepted", .Risk = CommandRisk.LeavesMachine,
                .TimeoutMs = 5000, .Idempotent = True, .Since = "1.0"}
            shareAdd.Aliases.Add("<folder>")
            shareAdd.Args.Add(New CommandArg With {.Name = "folder", .Kind = "path", .Required = True, .Positional = True,
                                                   .Description = "a folder by drive letter or UNC path, no .. segment and no wildcard"})
            shareAdd.Returns.Add(Ret("proposal", "string", "queued, duplicate, already-shared or full: what became of the proposal"))
            shareAdd.ExitCodes.Add(Ex(0, "proposed (queued, duplicate or already shared)"))
            shareAdd.ExitCodes.Add(Ex(10, "not an acceptable folder spelling"))
            shareAdd.ExitCodes.Add(Ex(11, "thirty-two proposals are already waiting, or a question is open"))
            shareAdd.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            shareAdd.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'share add D:\Photos --result C:\temp\p.json' -Wait -PassThru",
                "exit 0 and C:\temp\p.json holds the proposal verdict"))
            m.Commands.Add(shareAdd)

            Dim resourceAdd As New CommandSpec With {
                .Name = "resource add", .Summary = "Open the guided flow that adds a resource, with the camera QR scan when --camera is given.",
                .Synopsis = "resource add [--camera]", .ColdStart = "start", .TimeoutMs = 10000, .Idempotent = True, .Since = "1.0"}
            resourceAdd.Aliases.Add("--add-resource")
            resourceAdd.Aliases.Add("/add-resource")
            resourceAdd.AddAlias("::fms-add-resource::", New CommandAliasInfo With {.Answers = False})
            resourceAdd.AddAlias("--add-resource-camera", New CommandAliasInfo With {.Implies = {"--camera"}})
            resourceAdd.AddAlias("/add-resource-camera", New CommandAliasInfo With {.Implies = {"--camera"}})
            resourceAdd.AddAlias("::fms-add-resource-camera::", New CommandAliasInfo With {.Implies = {"--camera"}, .Answers = False})
            resourceAdd.Args.Add(New CommandArg With {.Name = "camera", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                      .Description = "start with the camera QR scan; the shipped add-resource-camera spellings mean this"})
            resourceAdd.ExitCodes.Add(Ex(0, "the flow window is shown"))
            resourceAdd.ExitCodes.Add(Ex(11, "busy: a question or a dialog is open in the running program"))
            resourceAdd.Examples.Add(Sample("cmd", "start /wait " & ExeName & " resource add --camera", "exit 0 and the camera scan on screen"))
            m.Commands.Add(resourceAdd)

            Dim scheduleOpen As New CommandSpec With {
                .Name = "schedule open", .Summary = "Open the Scheduled file operations window; a folder pre-fills the source of a new operation that the person still has to save.",
                .Synopsis = "schedule open [--folder <path>]", .ColdStart = "start", .TimeoutMs = 10000, .Idempotent = True, .Since = "1.0"}
            scheduleOpen.AddAlias("::fms-schedule::", New CommandAliasInfo With {.Answers = False})
            scheduleOpen.AddAlias("::fms-schedule::<folder>", New CommandAliasInfo With {.Answers = False})
            scheduleOpen.Args.Add(New CommandArg With {.Name = "folder", .Kind = "path", .Required = False,
                                                       .Description = "the pre-filled source folder"})
            scheduleOpen.ExitCodes.Add(Ex(0, "the window is shown"))
            scheduleOpen.ExitCodes.Add(Ex(10, "not an acceptable folder spelling"))
            scheduleOpen.ExitCodes.Add(Ex(11, "busy: a question or a dialog is open in the running program"))
            scheduleOpen.Examples.Add(Sample("cmd", "start /wait " & ExeName & " schedule open --folder D:\Photos", "exit 0 and the window open with D:\Photos as the source"))
            m.Commands.Add(scheduleOpen)

            Dim broadcastOpen As New CommandSpec With {
                .Name = "broadcast open", .Summary = "Open the live broadcast window with a path preselected.",
                .Synopsis = "broadcast open [--path <path>]", .ColdStart = "start", .TimeoutMs = 10000, .Idempotent = True, .Since = "1.0"}
            broadcastOpen.AddAlias("::fms-broadcast::<path>", New CommandAliasInfo With {.Answers = False})
            broadcastOpen.Args.Add(New CommandArg With {.Name = "path", .Kind = "path", .Required = False,
                                                        .Description = "a drive or UNC path the window preselects"})
            broadcastOpen.ExitCodes.Add(Ex(0, "the window is shown"))
            broadcastOpen.ExitCodes.Add(Ex(10, "not an acceptable path spelling"))
            broadcastOpen.ExitCodes.Add(Ex(11, "busy: a question or a dialog is open in the running program"))
            broadcastOpen.Examples.Add(Sample("cmd", "start /wait " & ExeName & " broadcast open --path D:\Music", "exit 0 and the broadcast window open"))
            m.Commands.Add(broadcastOpen)

            Dim clipSend As New CommandSpec With {
                .Name = "clipboard send", .Summary = "Send the clipboard to the exchange server and say how it went.",
                .Synopsis = "clipboard send [--json] [--result <path>]", .ColdStart = "oneshot", .Risk = CommandRisk.LeavesMachine,
                .TimeoutMs = 120000, .Idempotent = False, .Since = "1.0"}
            clipSend.Aliases.Add("--clipboard-send")
            clipSend.Aliases.Add("/clipboard-send")
            clipSend.AddAlias("::fms-clipboard-send::", New CommandAliasInfo With {.Answers = False})
            clipSend.Args.Add(JsonArg())
            clipSend.Args.Add(ResultArg("write the returned data to this file, whole"))
            clipSend.Returns.Add(Ret("outcome", "string", "a closed token for the result line: sent, empty, no-account, busy, too-large, no-room, no-answer, cancelled or failed"))
            clipSend.Returns.Add(Ret("bytes", "int", "size sent"))
            clipSend.ExitCodes.Add(Ex(0, "sent"))
            clipSend.ExitCodes.Add(Ex(1, "the operation failed or was cancelled"))
            clipSend.ExitCodes.Add(Ex(2, "not proven: the running program may still be working"))
            clipSend.ExitCodes.Add(Ex(11, "no exchange account, nothing to send, or another clipboard operation is under way"))
            clipSend.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            clipSend.ExitCodes.Add(Ex(13, "the clipboard is larger than the server accepts, or the server has no room"))
            clipSend.ExitCodes.Add(Ex(15, "the server did not answer"))
            clipSend.Examples.Add(Sample("powershell",
                "(Start-Process .\" & ExeName & " -ArgumentList 'clipboard send' -Wait -PassThru).ExitCode",
                "0 when the clipboard reached the server"))
            m.Commands.Add(clipSend)

            Dim clipGet As New CommandSpec With {
                .Name = "clipboard get", .Summary = "Fetch the newest clipboard from the exchange server into the local clipboard and say how it went.",
                .Synopsis = "clipboard get [--json] [--result <path>]", .ColdStart = "oneshot", .Risk = CommandRisk.WritesUserData,
                .TimeoutMs = 120000, .Idempotent = True, .Since = "1.0"}
            clipGet.Aliases.Add("--clipboard-get")
            clipGet.Aliases.Add("/clipboard-get")
            clipGet.AddAlias("::fms-clipboard-get::", New CommandAliasInfo With {.Answers = False})
            clipGet.Args.Add(JsonArg())
            clipGet.Args.Add(ResultArg("write the returned data to this file, whole"))
            clipGet.Returns.Add(Ret("outcome", "string", "a closed token for the result line: received, empty, no-account, busy, no-answer, cancelled or failed"))
            clipGet.Returns.Add(Ret("bytes", "int", "size received"))
            clipGet.ExitCodes.Add(Ex(0, "received"))
            clipGet.ExitCodes.Add(Ex(1, "the operation failed or was cancelled"))
            clipGet.ExitCodes.Add(Ex(2, "not proven: the running program may still be working"))
            clipGet.ExitCodes.Add(Ex(11, "no exchange account, nothing on the server, or another clipboard operation is under way"))
            clipGet.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            clipGet.ExitCodes.Add(Ex(15, "the server did not answer"))
            clipGet.Examples.Add(Sample("powershell",
                "(Start-Process .\" & ExeName & " -ArgumentList 'clipboard get' -Wait -PassThru).ExitCode",
                "0 when the server clipboard is now the local one"))
            m.Commands.Add(clipGet)

            ' --- the observe verbs (ticket L0319, since 1.1): read-only, never a window, never a network call ---
            Dim shareList As New CommandSpec With {
                .Name = "share list", .Summary = "List the folders the Share Manager shares and the state of each.",
                .Synopsis = "share list [--json] [--result <path>]", .ColdStart = "refuse", .Risk = CommandRisk.ReadsUserData,
                .TimeoutMs = 5000, .Idempotent = True, .Since = "1.1"}
            shareList.Args.Add(JsonArg())
            shareList.Args.Add(ResultArg("write the returned data to this file, whole"))
            shareList.Returns.Add(Ret("shares", "array", "one object per shared folder: folder (the path), state (" & ShareServing & " or " &
                                                         ShareStopped & ") and writable (true when the folder accepts uploads); no password, PIN, token or address of a peer"))
            shareList.ExitCodes.Add(Ex(0, "answered"))
            shareList.ExitCodes.Add(Ex(1, "the sharing worker did not answer"))
            shareList.ExitCodes.Add(Ex(11, "not running"))
            shareList.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            shareList.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'share list --result C:\temp\shares.json' -Wait; (Get-Content C:\temp\shares.json -Raw | ConvertFrom-Json).shares.Count",
                "the number of shared folders"))
            m.Commands.Add(shareList)

            Dim scheduleList As New CommandSpec With {
                .Name = "schedule list", .Summary = "List the scheduled file operations with their next run and their last result; with no manager running the program reads the operations, answers and exits, starting no scheduler.",
                .Synopsis = "schedule list [--json] [--result <path>]", .ColdStart = "oneshot", .Risk = CommandRisk.ReadsUserData,
                .TimeoutMs = 5000, .Idempotent = True, .Since = "1.1"}
            scheduleList.Args.Add(JsonArg())
            scheduleList.Args.Add(ResultArg("write the returned data to this file, whole"))
            scheduleList.Returns.Add(Ret("operations", "array", "one object per scheduled operation: id (a GUID), name, action (" & ActionCopy & ", " & ActionMove & ", " &
                                                                ActionDelete & ", " & ActionArchive & ", " & ActionFillFreeSpace & ", " & ActionBackup & " or " & ActionUnknown &
                                                                "), enabled (bool), nextRun (ISO 8601 with the offset, or null while the operation is off or parked) and lastResult (" &
                                                                ResultNone & ", " & ResultOk & ", " & ResultFailed & ", " & ResultRefused & ", " & ResultWall & ", " & ResultCancelled &
                                                                ", " & ResultSkipped & " or " & ResultUnknown & "); for backup operations, also retention (" & RetentionGenerations & ", " &
                                                                RetentionKeepDays & " or " & RetentionKeepDaysMonthly & "), keep (int, when generations) or keepDays (int, when dated), " &
                                                                "keepMonthly (bool) and skipLinks (bool); no folder, no file name, no error text"))
            scheduleList.ExitCodes.Add(Ex(0, "answered"))
            scheduleList.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            scheduleList.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'schedule list --result C:\temp\ops.json' -Wait; Get-Content C:\temp\ops.json -Raw | ConvertFrom-Json",
                "an object with an operations array"))
            m.Commands.Add(scheduleList)

            Dim exchangeStatus As New CommandSpec With {
                .Name = "exchange status", .Summary = "Say what the exchange client last knew about its server; no network call is made. With no manager running the program reads whether an account is set up, answers (state " & ExchangeStopped & ", no request waiting) and exits.",
                .Synopsis = "exchange status [--json] [--result <path>]", .ColdStart = "oneshot", .TimeoutMs = 5000,
                .Idempotent = True, .Since = "1.1"}
            exchangeStatus.Args.Add(JsonArg())
            exchangeStatus.Args.Add(ResultArg("write the returned data to this file, whole"))
            exchangeStatus.Returns.Add(Ret("accountConfigured", "bool", "whether an exchange account is set up on this PC"))
            exchangeStatus.Returns.Add(Ret("state", "string", "the last known state of the connection: " & ExchangeNotConfigured & ", " & ExchangeConnecting & ", " &
                                                              ExchangeOnline & ", " & ExchangeReconnecting & ", " & ExchangeSignInAgain & ", " & ExchangePinMismatch & ", " &
                                                              ExchangeUpdateNeeded & ", " & ExchangeDefect & " or " & ExchangeStopped & "; no server address, user name or key"))
            exchangeStatus.Returns.Add(Ret("pendingRequests", "int", "how many requests of other programs to share a folder wait for the person's answer in the manager"))
            exchangeStatus.ExitCodes.Add(Ex(0, "answered"))
            exchangeStatus.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'exchange status --result C:\temp\ex.json' -Wait; Get-Content C:\temp\ex.json -Raw | ConvertFrom-Json",
                "an object with accountConfigured, state and pendingRequests"))
            m.Commands.Add(exchangeStatus)

            ' --- scheduled operations management and journal (tickets L0345, L0346, since 1.2) ---
            Dim scheduleAdd As New CommandSpec With {
                .Name = "schedule add", .Summary = "Create or update a scheduled backup operation without opening a window.",
                .Synopsis = "schedule add --action backup --source <folder> --destination <folder> --daily <HH:mm> [--keep <1-999> | --keep-days <1-999> [--keep-monthly]] [--skip-links] [--name <text>] [--json] [--result <path>]",
                .ColdStart = "start", .Risk = CommandRisk.WritesUserData,
                .TimeoutMs = 20000, .Idempotent = True, .Since = "1.2"}
            scheduleAdd.Args.Add(New CommandArg With {.Name = "action", .Kind = "enum", .Required = True, .Values = {"backup"},
                                                      .Description = "operation action (backup)"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "source", .Kind = "path", .Required = True,
                                                      .Description = "absolute drive or UNC source folder"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "destination", .Kind = "path", .Required = True,
                                                      .Description = "absolute drive or UNC destination folder outside the source"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "daily", .Kind = "string", .Required = True,
                                                      .Description = "daily run time in 24-hour HH:mm form (local wall clock)"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "keep", .Kind = "int", .Required = False, .DefaultValue = 7,
                                                      .Description = "how many numbered generations to keep (1 to 999; cannot be combined with --keep-days)"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "keep-days", .Kind = "int", .Required = False,
                                                      .Description = "keep dated backups for N days (1 to 999); cannot be combined with --keep"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "keep-monthly", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                      .Description = "keep the earliest backup of each calendar month indefinitely (requires --keep-days)"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "skip-links", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                      .Description = "skip junctions and symbolic links instead of refusing the backup"})
            scheduleAdd.Args.Add(New CommandArg With {.Name = "name", .Kind = "string", .Required = False,
                                                      .Description = "display name for the operation"})
            scheduleAdd.Args.Add(JsonArg())
            scheduleAdd.Args.Add(ResultArg("write the returned data to this file, whole"))
            scheduleAdd.Returns.Add(Ret("id", "string", "operation id (GUID)"))
            scheduleAdd.Returns.Add(Ret("created", "bool", "true when a new operation was created; false when an existing matching operation was updated"))
            scheduleAdd.Returns.Add(Ret("nextRun", "string", "next scheduled run (ISO 8601 with offset)"))
            scheduleAdd.ExitCodes.Add(Ex(0, "saved"))
            scheduleAdd.ExitCodes.Add(Ex(10, "invalid option, refused definition or FileDO without the needed backup support"))
            scheduleAdd.ExitCodes.Add(Ex(11, "busy: a question or a dialog is open in the running program"))
            scheduleAdd.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            scheduleAdd.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'schedule add --action backup --source P:\MyProject --destination C:\GD\GIT --daily 13:00 --keep-days 7 --keep-monthly --result C:\temp\add.json' -Wait -PassThru",
                "exit 0 and C:\temp\add.json holds the operation id"))
            m.Commands.Add(scheduleAdd)

            Dim scheduleRemove As New CommandSpec With {
                .Name = "schedule remove", .Summary = "Delete a scheduled operation definition without touching any backup files.",
                .Synopsis = "schedule remove <id|name> [--json] [--result <path>]",
                .ColdStart = "start", .Risk = CommandRisk.WritesUserData,
                .TimeoutMs = 15000, .Idempotent = False, .Since = "1.2"}
            scheduleRemove.Args.Add(New CommandArg With {.Name = "target", .Kind = "string", .Required = True, .Positional = True,
                                                         .Description = "operation id or exact operation name"})
            scheduleRemove.Args.Add(JsonArg())
            scheduleRemove.Args.Add(ResultArg("write the returned data to this file, whole"))
            scheduleRemove.Returns.Add(Ret("id", "string", "id of the removed operation"))
            scheduleRemove.Returns.Add(Ret("removed", "bool", "true when the definition was deleted"))
            scheduleRemove.ExitCodes.Add(Ex(0, "removed"))
            scheduleRemove.ExitCodes.Add(Ex(10, "unknown option or ambiguous operation name"))
            scheduleRemove.ExitCodes.Add(Ex(11, "operation not found, or busy with an open window"))
            scheduleRemove.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            scheduleRemove.Examples.Add(Sample("cmd",
                "start /wait " & ExeName & " schedule remove ""MyProject""",
                "exit 0 and the operation definition is deleted while backup files remain"))
            m.Commands.Add(scheduleRemove)

            Dim scheduleRun As New CommandSpec With {
                .Name = "schedule run", .Summary = "Queue one scheduled operation or all enabled operations for immediate execution on the single lane; with --wait, wait for completion and exit with its outcome.",
                .Synopsis = "schedule run (<id|name> | --all) [--wait] [--json] [--result <path>]",
                .ColdStart = "start", .Wait = "accepted", .Risk = CommandRisk.WritesUserData,
                .TimeoutMs = 600000, .Idempotent = False, .Since = "1.2"}
            scheduleRun.Args.Add(New CommandArg With {.Name = "target", .Kind = "string", .Required = False, .Positional = True,
                                                      .Description = "operation id or exact operation name"})
            scheduleRun.Args.Add(New CommandArg With {.Name = "all", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                      .Description = "queue every enabled operation not already in the lane"})
            scheduleRun.Args.Add(New CommandArg With {.Name = "wait", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                      .Description = "wait for the queued run(s) to finish and return the run outcome"})
            scheduleRun.Args.Add(JsonArg())
            scheduleRun.Args.Add(ResultArg("write the returned data to this file, whole"))
            scheduleRun.OneOf.Add({"target", "all"})
            scheduleRun.Returns.Add(Ret("queued", "int", "number of operations queued"))
            scheduleRun.Returns.Add(Ret("completed", "bool", "true when --wait waited until every queued run finished"))
            scheduleRun.Returns.Add(Ret("succeeded", "int", "runs that ended ok (when --wait)"))
            scheduleRun.Returns.Add(Ret("failed", "int", "runs that did not end ok (when --wait)"))
            scheduleRun.Returns.Add(Ret("outcome", "string", "queued, or the run outcome token (ok, failed, refused, wall, cancelled, skipped)"))
            scheduleRun.ExitCodes.Add(Ex(0, "queued, or every waited run finished ok"))
            scheduleRun.ExitCodes.Add(Ex(1, "the waited run failed"))
            scheduleRun.ExitCodes.Add(Ex(2, "not proven: the wait bound passed while the run may still be working"))
            scheduleRun.ExitCodes.Add(Ex(10, "missing target/--all, unknown option or ambiguous operation name"))
            scheduleRun.ExitCodes.Add(Ex(11, "operation not found, paused, nothing to run, already running, busy or target unreachable"))
            scheduleRun.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            scheduleRun.ExitCodes.Add(Ex(13, "no space on the target disk"))
            scheduleRun.ExitCodes.Add(Ex(15, "network or remote share failure"))
            scheduleRun.ExitCodes.Add(Ex(16, "cancelled"))
            scheduleRun.Examples.Add(Sample("powershell",
                "(Start-Process .\" & ExeName & " -ArgumentList 'schedule run --all --wait' -Wait -PassThru).ExitCode",
                "0 when every enabled operation finished without errors"))
            m.Commands.Add(scheduleRun)

            Dim scheduleHistory As New CommandSpec With {
                .Name = "schedule history", .Summary = "Print the scheduled operations journal; with no manager running the program reads the journal, answers and exits.",
                .Synopsis = "schedule history [--operation <id|name>] [--last <n>] [--json] [--result <path>]",
                .ColdStart = "oneshot", .Risk = CommandRisk.ReadsUserData,
                .TimeoutMs = 5000, .Idempotent = True, .Since = "1.2"}
            scheduleHistory.Args.Add(New CommandArg With {.Name = "operation", .Kind = "string", .Required = False,
                                                          .Description = "filter by operation id or exact name"})
            scheduleHistory.Args.Add(New CommandArg With {.Name = "last", .Kind = "int", .Required = False, .DefaultValue = 50,
                                                          .Description = "maximum number of newest entries to return (1 to 500)"})
            scheduleHistory.Args.Add(JsonArg())
            scheduleHistory.Args.Add(ResultArg("write the returned data to this file, whole"))
            scheduleHistory.Returns.Add(Ret("entries", "array", "newest-first journal objects: time (ISO 8601 with offset), operationId, operation, kind (run or note), trigger, outcome, files (int), failed (int), bytes (int64), durationMs (int64), generation (int or null), retiredGeneration (int or null), retiredCount (int or null), keptCount (int or null), keepDays (int or null), monthlyKept (int or null), skippedLinks (int or null) and detail"))
            scheduleHistory.ExitCodes.Add(Ex(0, "answered"))
            scheduleHistory.ExitCodes.Add(Ex(1, "reading failed or response exceeded 64 KiB (narrow with --last or use --result)"))
            scheduleHistory.ExitCodes.Add(Ex(10, "invalid --last value (must be 1 to 500)"))
            scheduleHistory.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            scheduleHistory.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'schedule history --last 20 --result C:\temp\hist.json' -Wait; Get-Content C:\temp\hist.json -Raw | ConvertFrom-Json",
                "an object with an entries array"))
            m.Commands.Add(scheduleHistory)

            Return m
        End Function

        ''' <summary>The Share Manager section of docs/CLI.md, rendered from the manifest and nothing else.</summary>
        Friend Function ToMarkdownSection() As String
            Return CommandDocs.ToMarkdownSection(Build(), SettingLabelEnglish, SettingPlaceEnglish, "share-manager-")
        End Function

        ''' <summary>
        ''' Whether a command line is a command of this profile (APP-COMMANDS 0.2 item A.1). A first argument is a
        ''' command when it is a reserved word - the first word of a verb or of an alias spelling - and a reserved
        ''' word wins over a folder of the same name; the disk is never probed. Any other first word (the frozen
        ''' spellings: an empty line, a dash or slash switch, a marker, a drive or UNC path, a bare folder name)
        ''' keeps being read as the folder it always was.
        ''' </summary>
        Friend Function IsCommandLine(args As IList(Of String)) As Boolean
            If args Is Nothing OrElse args.Count = 0 Then Return False
            Return CommandLineParser.IsReservedFirstWord(args(0), Build())
        End Function

        ''' <summary>The verbs the manager answers even while a modal window is open (rule 10): <c>status</c> reports the
        ''' state without touching any window, <c>share add</c> only stages a PROPOSAL in the queue (a modal question
        ''' waiting for its answer is exactly when a second folder arrives; the queue holds it, as it always did), and the
        ''' three observe verbs of ticket L0319 only READ state (<c>exchange status</c> reports the requests that wait for
        ''' the person, which is exactly the moment a question is open, so a busy refusal there would hide its own subject).</summary>
        Friend Function IgnoresBusy(verb As String) As Boolean
            Return String.Equals(verb, "status", StringComparison.Ordinal) OrElse
                   String.Equals(verb, "share add", StringComparison.Ordinal) OrElse
                   String.Equals(verb, "share list", StringComparison.Ordinal) OrElse
                   String.Equals(verb, "schedule list", StringComparison.Ordinal) OrElse
                   String.Equals(verb, "schedule history", StringComparison.Ordinal) OrElse
                   String.Equals(verb, "exchange status", StringComparison.Ordinal)
        End Function

        ''' <summary>The proposal verdict of the folder queue as the token share add returns.</summary>
        Friend Function ProposalToken(queued As Boolean, duplicate As Boolean, alreadyShared As Boolean) As String
            If alreadyShared Then Return ProposalAlreadyShared
            If duplicate Then Return ProposalDuplicate
            If queued Then Return ProposalQueued
            Return ProposalFull
        End Function

    End Module

End Namespace
