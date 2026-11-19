Option Strict On
Option Explicit On

Imports System.Collections.Generic

' The viewer's command table (APP-COMMANDS rules 17 and 18): the ONE place the viewer's verbs are
' declared. docs/cli-commands.json, docs/CLI.md (tools/Build-CliDocs.ps1) and the output of
' "commands --json" are all rendered from Build(); AppCommandsDocsTests fails when a document
' disagrees with it. Pure - linked into Lite.Tests and compiled by the generator on its own.
Namespace AppCommands

    Friend Module ViewerCommandTable

        Friend Const ExeName As String = "FastMediaSorter_LITE.exe"
        Friend Const CliVersion As String = "1.1"
        ''' <summary>The folder under %TEMP% that holds reply files (CommandFiles.ReplyDirectory).</summary>
        Friend Const ReplyFolder As String = "FastMediaSorter-commands"
        ''' <summary>The English label of the setting of rule 13, as docs/CLI.md names it.</summary>
        Friend Const SettingLabelEnglish As String = "Allow commands from other programs"
        Friend Const SettingPlaceEnglish As String = "Settings, Files, Keys and associations"

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

        Private Function ResultArg() As CommandArg
            Return New CommandArg With {.Name = "result", .Kind = "path", .Required = False,
                                        .Description = "write the returned data to this file, whole"}
        End Function

        Friend Function Build() As CommandManifest
            Dim m As New CommandManifest With {.Product = ProductNames.Compact, .Exe = ExeName, .CliVersion = CliVersion}

            Dim commands As New CommandSpec With {
                .Name = "commands", .Summary = "List the commands this program accepts.",
                .Synopsis = "commands [--json]", .ColdStart = "oneshot", .TimeoutMs = 5000, .Forward = "never",
                .Idempotent = True, .Since = "1.0"}
            commands.Args.Add(New CommandArg With {.Name = "json", .Kind = "bool", .Required = False, .DefaultValue = False,
                                                   .Description = "print the manifest (docs/cli-commands.json) instead of the readable list"})
            commands.ExitCodes.Add(Ex(0, "printed"))
            commands.ExitCodes.Add(Ex(10, "unknown option"))
            commands.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'commands --json' -RedirectStandardOutput C:\temp\fms-commands.json -Wait",
                "exit 0 and the file equals docs/cli-commands.json"))
            m.Commands.Add(commands)

            Dim version As New CommandSpec With {
                .Name = "version", .Summary = "Print the version of this program.",
                .Synopsis = "version [--json]", .ColdStart = "oneshot", .TimeoutMs = 5000, .Forward = "never",
                .Idempotent = True, .Since = "1.0"}
            version.Args.Add(JsonArg())
            version.Returns.Add(New CommandReturn With {.Name = "version", .Kind = "string", .Description = "the program version"})
            version.Returns.Add(New CommandReturn With {.Name = "cliVersion", .Kind = "string", .Description = "the version of the command set this manifest describes"})
            version.ExitCodes.Add(Ex(0, "printed"))
            version.ExitCodes.Add(Ex(10, "unknown option"))
            version.Examples.Add(Sample("cmd", "start /wait " & ExeName & " version", "exit 0 and the version on the console"))
            m.Commands.Add(version)

            Dim status As New CommandSpec With {
                .Name = "status", .Summary = "Report whether the program is running and ready for commands.",
                .Synopsis = "status [--json] [--result <path>]", .ColdStart = "refuse", .TimeoutMs = 2000,
                .Idempotent = True, .Since = "1.0"}
            status.Args.Add(JsonArg())
            status.Args.Add(ResultArg())
            status.Returns.Add(New CommandReturn With {.Name = "version", .Kind = "string", .Description = "the running program's version"})
            status.Returns.Add(New CommandReturn With {.Name = "pid", .Kind = "int", .Description = "its process id"})
            status.Returns.Add(New CommandReturn With {.Name = "exe", .Kind = "string", .Description = "its file name"})
            status.Returns.Add(New CommandReturn With {.Name = "ready", .Kind = "bool", .Description = "true when no window blocks commands"})
            status.Returns.Add(New CommandReturn With {.Name = "acceptsRestrictedCommands", .Kind = "bool",
                                                       .Description = "whether the setting admits commands of a class other than safe"})
            status.ExitCodes.Add(Ex(0, "running and ready"))
            status.ExitCodes.Add(Ex(11, "not running, or busy"))
            status.Examples.Add(Sample("powershell",
                "(Start-Process .\" & ExeName & " -ArgumentList 'status' -Wait -PassThru).ExitCode",
                "0 while the program runs, 11 otherwise"))
            m.Commands.Add(status)

            Dim show As New CommandSpec With {
                .Name = "show", .Summary = "Bring the window to the front, starting the program when it is not running.",
                .Synopsis = "show", .ColdStart = "start", .TimeoutMs = 5000, .Idempotent = True, .Since = "1.0"}
            show.AddAlias("", Nothing)
            show.AddAlias("::fms-show-window::", New CommandAliasInfo With {.Legacy = True, .Answers = False})
            show.ExitCodes.Add(Ex(0, "shown"))
            show.ExitCodes.Add(Ex(2, "the running copy is older and must be restarted"))
            show.ExitCodes.Add(Ex(11, "busy"))
            show.Examples.Add(Sample("cmd", "start /wait " & ExeName & " show", "exit 0 and the window in front"))
            m.Commands.Add(show)

            Dim quit As New CommandSpec With {
                .Name = "quit", .Summary = "Close the running program.",
                .Synopsis = "quit [--stop-jobs]", .ColdStart = "oneshot", .TimeoutMs = 10000, .Idempotent = True, .Since = "1.0"}
            quit.Args.Add(New CommandArg With {.Name = "stop-jobs", .Kind = "bool", .Required = False, .DefaultValue = False,
                .Description = "Consent to stop file operations that are running: they are told to start nothing further, the one in progress is finished, and the program closes. Without it, quit is refused (busy) while file operations run."})
            quit.ExitCodes.Add(Ex(0, "closed, or nothing was running"))
            quit.ExitCodes.Add(Ex(1, "the window did not close"))
            quit.ExitCodes.Add(Ex(11, "busy: file operations are running and --stop-jobs was not given, or a window is open"))
            quit.Examples.Add(Sample("powershell",
                "(Start-Process .\" & ExeName & " -ArgumentList 'quit' -Wait -PassThru).ExitCode",
                "0, and no viewer process is left"))
            m.Commands.Add(quit)

            AddOpen(m)
            AddPhoneCode(m)
            AddStreamCatalog(m)
            AddNavigation(m)
            AddSlideshow(m)
            AddScreenshot(m)

            Return m
        End Function

        ''' <summary>The bare launch as a verb (L0317): the spellings that already ship stay what they are - a path, a
        ''' folder, -NoBack before or after - and are listed as aliases of this verb; they never reach the strict parser.</summary>
        Private Sub AddOpen(m As CommandManifest)
            Dim open As New CommandSpec With {
                .Name = "open",
                .Summary = "Show a file or a folder in the viewer, or hand a resource locator or a document the viewer reads to the dialog that reads it. The bare launch (the verb word left out) is this verb.",
                .Synopsis = "open (<target> | --target-file <path>) [--no-back] [--json] [--result <path>]", .ColdStart = "start", .TimeoutMs = 60000, .Idempotent = True, .Since = "1.0",
                .DefaultFor = ExeName}
            open.AddAlias("<path>", Nothing)
            open.AddAlias("-NoBack <path>", New CommandAliasInfo With {.Implies = {"--no-back"}})
            open.AddAlias("<path> -NoBack", New CommandAliasInfo With {.Implies = {"--no-back"}})
            open.Args.Add(New CommandArg With {.Name = "target", .Kind = "string", .Required = False, .Positional = True, .Sensitive = True,
                .Description = "A file, a folder, an archive, a UNC share path, a resource locator (sftp:// or gd://), a Share Manager document (.fmscfg, .fmsexch while the exchange client is offered) or a live broadcast descriptor (a .fmsbcast file or an intent link). An internal ::name:: token is refused. An address may carry a user name and password (smb://user:password@host/share): the value is never written to the log or an answer."})
            open.Args.Add(New CommandArg With {.Name = "target-file", .Kind = "path", .Required = False,
                .Description = "Read the target from this file instead of the command line (one line, UTF-8), so an address with a password stays out of the shell history. Give either the target or this option."})
            open.OneOf.Add({"target", "target-file"})
            open.Args.Add(New CommandArg With {.Name = "no-back", .Kind = "bool", .Required = False, .DefaultValue = False,
                .Description = "Keep background tasks off for the session (the shipped -NoBack switch, any letter case, before or after the target)."})
            open.Args.Add(JsonArg())
            open.Args.Add(ResultArg())
            open.Returns.Add(New CommandReturn With {.Name = "kind", .Kind = "string",
                .Description = "what the target turned out to be: image, video, audio, document, archive, folder, resource or other"})
            open.Returns.Add(New CommandReturn With {.Name = "count", .Kind = "int",
                .Description = "how many files the folder listing holds; no names"})
            open.ExitCodes.Add(Ex(0, "shown, or handed to the dialog that reads it"))
            open.ExitCodes.Add(Ex(2, "not proven: the running program may still be working"))
            open.ExitCodes.Add(Ex(10, "no target given, both a target and --target-file given, or the file holds no single line"))
            open.ExitCodes.Add(Ex(11, "the target or the --target-file file does not exist, or the viewer is busy"))
            open.ExitCodes.Add(Ex(15, "the network share did not answer"))
            open.Examples.Add(Sample("cmd", "start /wait " & ExeName & " open D:\Photos\2026", "exit 0; the folder is on screen"))
            open.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'open \\nas\media\clip.mp4 --result C:\temp\open.json' -Wait",
                "exit 0; the file holds {""kind"":""video"",""count"":N}"))
            open.Examples.Add(Sample("cmd", "start /wait " & ExeName & " open --target-file C:\temp\nas-address.txt",
                "exit 0; the address in the file is open, and it is in neither the shell history nor the log"))
            m.Commands.Add(open)
        End Sub

        ''' <summary>The verb behind the two window-message spellings. The Share Manager's relay uses those shipped
        ''' spellings (legacy aliases, item F), never a request with a claimed origin; a command line is held by the setting
        ''' of rule 13 (the old spellings stay as they were, never gated by it).</summary>
        Private Sub AddPhoneCode(m As CommandManifest)
            Dim receive As New CommandSpec With {
                .Name = "phone-code receive",
                .Summary = "Take the connection code a phone showed (relayed by the Share Manager) into the Resources panel's import dialog; nothing is added until the person confirms.",
                .Synopsis = "phone-code receive --secret-file <path>", .Risk = CommandRisk.WritesUserData,
                .ColdStart = "start", .TimeoutMs = 20000, .Idempotent = False, .Since = "1.0"}
            receive.AddAlias("::fms-phone-code::", New CommandAliasInfo With {.Legacy = True, .Answers = False})
            receive.AddAlias("::fms-phone-code-file::", New CommandAliasInfo With {.Legacy = True, .Answers = False})
            receive.Args.Add(New CommandArg With {.Name = "secret-file", .Kind = "path", .Required = True,
                .Description = "A UTF-8 file that holds the phone connection code, or the code document the Share Manager opened. The viewer reads it, keeps nothing and never logs it. The shipped ::fms-phone-code:: spelling carries the code itself inside the window message, never on a command line."})
            receive.ExitCodes.Add(Ex(0, "the import dialog is shown"))
            receive.ExitCodes.Add(Ex(10, "no --secret-file given"))
            receive.ExitCodes.Add(Ex(11, "the file does not exist, or the viewer is busy"))
            receive.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            receive.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'phone-code receive --secret-file C:\temp\code.txt' -Wait -PassThru",
                "exit 0; the import dialog shows what would be added"))
            m.Commands.Add(receive)
        End Sub

        ''' <summary>The catalogue producer and validator (STREAM-BANK) on the shared profile. The six shipped switch
        ''' spellings are aliases and keep their behaviour; only their exit codes moved to the table of rule 11.</summary>
        Private Sub AddStreamCatalog(m As CommandManifest)
            Dim build As New CommandSpec With {
                .Name = "stream-catalog build",
                .Summary = "Build a stream-catalog.zip from an M3U or M3U8 playlist or a folder of playlists.",
                .Synopsis = "stream-catalog build <input> [--out <zip>] [--overwrite] [--force] [--json] [--result <path>]", .Risk = CommandRisk.WritesUserData,
                .ColdStart = "oneshot", .TimeoutMs = 600000, .Idempotent = True, .Since = "1.0"}
            For Each spelling As String In StreamCatalogBuildSpellings
                build.AddAlias(spelling, New CommandAliasInfo With {.Implies = {"--overwrite"}, .Legacy = True})
            Next
            build.Args.Add(New CommandArg With {.Name = "input", .Kind = "path", .Required = True, .Positional = True,
                .Description = "The playlist file or the folder that holds playlists."})
            build.Args.Add(New CommandArg With {.Name = "out", .Kind = "path", .Required = False, .DefaultValue = "stream-catalog.zip beside the input",
                .Description = "The archive to write; stream-catalog.zip in the input folder (or beside the input file) when absent. The shipped option spellings -o, /o, --output and /output are accepted after a shipped verb spelling."})
            build.Args.Add(New CommandArg With {.Name = "overwrite", .Kind = "bool", .Required = False, .DefaultValue = False,
                .Description = "Replace an existing archive, under the churn guard. The shipped verb spellings imply it, because they always replaced."})
            build.Args.Add(New CommandArg With {.Name = "force", .Kind = "bool", .Required = False, .DefaultValue = False,
                .Description = "Bypass the churn guard that refuses a replacement losing more than 20 percent of the previous identities. The shipped spellings -f, /f, --force and /force are accepted."})
            build.Args.Add(JsonArg())
            build.Args.Add(ResultArg())
            build.Returns.Add(New CommandReturn With {.Name = "rows", .Kind = "int", .Description = "rows written"})
            build.Returns.Add(New CommandReturn With {.Name = "passed", .Kind = "bool", .Description = "whether the built archive passed its own checks"})
            build.ExitCodes.Add(Ex(0, "built and checked"))
            build.ExitCodes.Add(Ex(1, "the build failed or the churn guard refused"))
            build.ExitCodes.Add(Ex(10, "usage"))
            build.ExitCodes.Add(Ex(11, "the input does not exist"))
            build.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            build.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'stream-catalog build D:\lists --out D:\lists\stream-catalog.zip --overwrite' -Wait -PassThru",
                "exit 0; the archive exists"))
            m.Commands.Add(build)

            Dim check As New CommandSpec With {
                .Name = "stream-catalog check",
                .Summary = "Run the catalog checks V1 to V12 on an existing stream-catalog.zip and write the report beside it.",
                .Synopsis = "stream-catalog check <archive> [--json] [--result <path>]", .Risk = CommandRisk.WritesUserData,
                .ColdStart = "oneshot", .TimeoutMs = 120000, .Idempotent = True, .Since = "1.0"}
            For Each spelling As String In StreamCatalogCheckSpellings
                check.AddAlias(spelling, New CommandAliasInfo With {.Legacy = True})
            Next
            check.Args.Add(New CommandArg With {.Name = "archive", .Kind = "path", .Required = True, .Positional = True,
                .Description = "The stream-catalog.zip to check."})
            check.Args.Add(JsonArg())
            check.Args.Add(ResultArg())
            check.Returns.Add(New CommandReturn With {.Name = "passed", .Kind = "bool", .Description = "whether every check passed"})
            check.Returns.Add(New CommandReturn With {.Name = "failedChecks", .Kind = "int", .Description = "how many checks failed"})
            check.ExitCodes.Add(Ex(0, "every check passed"))
            check.ExitCodes.Add(Ex(1, "a check failed"))
            check.ExitCodes.Add(Ex(10, "usage"))
            check.ExitCodes.Add(Ex(11, "the archive does not exist"))
            check.ExitCodes.Add(Ex(12, "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"))
            check.Examples.Add(Sample("cmd", "start /wait " & ExeName & " stream-catalog check D:\lists\stream-catalog.zip",
                "exit 0 when the archive passes; <stem>-report.txt is beside it"))
            m.Commands.Add(check)
        End Sub

        Private Const BusyOrNothingShown As String = "not running, busy, or nothing is shown"
        Private Const SwitchedOff As String = "the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off"

        ''' <summary>The six moves of the listing (L0318): each is the arrow key, the Home or End key or the jump-to-number
        ''' box of the window, called through the one implementation the key uses. Mainline only: the older x86 copy answers
        ''' that it is older (exit 2).</summary>
        Private Sub AddNavigation(m As CommandManifest)
            Dim current As New CommandSpec With {
                .Name = "file current",
                .Summary = "Say which file the viewer shows now.",
                .Synopsis = "file current [--json] [--result <path>]", .Risk = CommandRisk.ReadsUserData,
                .ColdStart = "refuse", .TimeoutMs = 2000, .Idempotent = True, .Since = "1.1"}
            current.Args.Add(JsonArg())
            current.Args.Add(ResultArg())
            current.Returns.Add(New CommandReturn With {.Name = "path", .Kind = "path", .Description = "the full path of the shown file"})
            current.Returns.Add(New CommandReturn With {.Name = "index", .Kind = "int", .Description = "its place in the listing, counted from 1"})
            current.Returns.Add(New CommandReturn With {.Name = "count", .Kind = "int", .Description = "how many files the listing holds"})
            current.Returns.Add(New CommandReturn With {.Name = "kind", .Kind = "string", .Description = "image, video, audio, document, text, archive or other"})
            current.ExitCodes.Add(Ex(0, "answered"))
            current.ExitCodes.Add(Ex(11, BusyOrNothingShown))
            current.ExitCodes.Add(Ex(12, SwitchedOff))
            current.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'file current --result C:\temp\cur.json' -Wait; Get-Content C:\temp\cur.json -Raw | ConvertFrom-Json",
                "an object with path, index, count and kind"))
            m.Commands.Add(current)

            m.Commands.Add(MoveSpec("file next", "Show the next file of the listing, as the right arrow does; on the last file it stays there, where the arrow key wraps to the first.",
                "the new place, counted from 1", "the next file is on screen", False, False, True))
            m.Commands.Add(MoveSpec("file previous", "Show the previous file of the listing; on the first file it stays there.",
                "the new place, counted from 1", "the previous file is on screen", False, False, True))
            m.Commands.Add(MoveSpec("file first", "Show the first file of the listing.",
                "always 1", "the first file is on screen", True, False, False))
            m.Commands.Add(MoveSpec("file last", "Show the last file of the listing.",
                "the last place, the same number as count", "the last file is on screen", True, False, False))

            Dim number As CommandSpec = MoveSpec("file goto", "Show the file at a given place of the listing.",
                "the place shown", "file 40 is on screen", True, True, False)
            number.Synopsis = "file goto --number <n> [--json] [--result <path>]"
            number.Args.Insert(0, New CommandArg With {.Name = "number", .Kind = "int", .Required = True,
                .Description = "The place, counted from 1."})
            m.Commands.Add(number)
        End Sub

        ''' <summary>The shape the five moves share: they answer with the new place and how many files there are.</summary>
        Private Function MoveSpec(name As String, summary As String, indexMeaning As String, onScreen As String,
                                  idempotent As Boolean, hasNumber As Boolean, withKind As Boolean) As CommandSpec
            Dim spec As New CommandSpec With {
                .Name = name, .Summary = summary,
                .Synopsis = name & " [--json] [--result <path>]", .ColdStart = "refuse", .TimeoutMs = 5000,
                .Idempotent = idempotent, .Since = "1.1"}
            spec.Args.Add(JsonArg())
            spec.Args.Add(ResultArg())
            spec.Returns.Add(New CommandReturn With {.Name = "index", .Kind = "int", .Description = indexMeaning})
            spec.Returns.Add(New CommandReturn With {.Name = "count", .Kind = "int", .Description = "how many files the listing holds"})
            If withKind Then
                spec.Returns.Add(New CommandReturn With {.Name = "kind", .Kind = "string", .Description = "what the new file is: image, video, audio, document, text, archive or other"})
            End If
            spec.ExitCodes.Add(Ex(0, "moved, or already there"))
            If hasNumber Then spec.ExitCodes.Add(Ex(10, "the number is not given, or is outside the listing"))
            spec.ExitCodes.Add(Ex(11, BusyOrNothingShown))
            Dim line As String = "start /wait " & ExeName & " " & name & If(hasNumber, " --number 40", String.Empty)
            spec.Examples.Add(Sample("cmd", line, "exit 0; " & onScreen))
            Return spec
        End Function

        ''' <summary>The slideshow as two verbs (L0318): start is the toolbar button's one implementation, with a pause
        ''' for this run only; stop is the single stop point every key and click already uses.</summary>
        Private Sub AddSlideshow(m As CommandManifest)
            Dim start As New CommandSpec With {
                .Name = "slideshow start",
                .Summary = "Start the slideshow as the toolbar button does, or at random with --random.",
                .Synopsis = "slideshow start [--random] [--interval-ms <n>] [--json] [--result <path>]",
                .ColdStart = "refuse", .TimeoutMs = 5000, .Idempotent = True, .Since = "1.1"}
            start.Args.Add(New CommandArg With {.Name = "random", .Kind = "bool", .Required = False, .DefaultValue = False,
                .Description = "Start the random slideshow (the random button). Without it the order is the one the setting for the slideshow order gives."})
            start.Args.Add(New CommandArg With {.Name = "interval-ms", .Kind = "int", .Required = False,
                .Description = "The pause between files in milliseconds, 1000 to 120000, for this run only; the saved setting does not change."})
            start.Args.Add(JsonArg())
            start.Args.Add(ResultArg())
            start.Returns.Add(New CommandReturn With {.Name = "running", .Kind = "bool", .Description = "true once the slideshow runs"})
            start.Returns.Add(New CommandReturn With {.Name = "intervalMs", .Kind = "int", .Description = "the pause in use"})
            start.ExitCodes.Add(Ex(0, "running"))
            start.ExitCodes.Add(Ex(10, "the pause is not a whole number from 1000 to 120000"))
            start.ExitCodes.Add(Ex(11, "not running, busy, or nothing to show"))
            start.Examples.Add(Sample("cmd", "start /wait " & ExeName & " slideshow start --random --interval-ms 8000", "exit 0; the slideshow runs"))
            m.Commands.Add(start)

            Dim [stop] As New CommandSpec With {
                .Name = "slideshow stop",
                .Summary = "Stop the slideshow.",
                .Synopsis = "slideshow stop", .ColdStart = "refuse", .TimeoutMs = 5000, .Idempotent = True, .Since = "1.1"}
            [stop].ExitCodes.Add(Ex(0, "stopped, or was not running"))
            [stop].ExitCodes.Add(Ex(11, "the viewer is not running or is busy"))
            [stop].Examples.Add(Sample("cmd", "start /wait " & ExeName & " slideshow stop", "exit 0"))
            m.Commands.Add([stop])
        End Sub

        ''' <summary>The window as one PNG (L0318, CAPTURE-OUTPUT): what the viewer draws now, the window and nothing else of the screen.</summary>
        Private Sub AddScreenshot(m As CommandManifest)
            Dim shot As New CommandSpec With {
                .Name = "screenshot window",
                .Summary = "Save what the viewer window shows now as a PNG image.",
                .Synopsis = "screenshot window --out <png> [--overwrite] [--json] [--result <path>]", .Risk = CommandRisk.ReadsUserData,
                .ColdStart = "refuse", .TimeoutMs = 10000, .Idempotent = True, .Since = "1.1"}
            shot.Args.Add(New CommandArg With {.Name = "out", .Kind = "path", .Required = True,
                .Description = "The PNG file to write, used exactly as given; a folder gets screenshot_<yyMMdd>_<HHmmss>.png inside it. A relative path is relative to the caller. The folder of the file must exist: it is never created (a missing one is refused as target-missing)."})
            shot.Args.Add(New CommandArg With {.Name = "overwrite", .Kind = "bool", .Required = False, .DefaultValue = False,
                .Description = "Replace the file when it exists."})
            shot.Args.Add(JsonArg())
            shot.Args.Add(ResultArg())
            shot.Returns.Add(New CommandReturn With {.Name = "width", .Kind = "int", .Description = "width in pixels"})
            shot.Returns.Add(New CommandReturn With {.Name = "height", .Kind = "int", .Description = "height in pixels"})
            shot.ExitCodes.Add(Ex(0, "saved"))
            shot.ExitCodes.Add(Ex(10, "no --out given"))
            shot.ExitCodes.Add(Ex(11, "not running, busy, the folder of --out does not exist, or the file exists and --overwrite is absent"))
            shot.ExitCodes.Add(Ex(12, SwitchedOff))
            shot.ExitCodes.Add(Ex(13, "not enough space for the file"))
            shot.Examples.Add(Sample("powershell",
                "Start-Process .\" & ExeName & " -ArgumentList 'screenshot window --out C:\temp\viewer.png --overwrite' -Wait -PassThru",
                "exit 0; C:\temp\viewer.png is the window as drawn"))
            m.Commands.Add(shot)
        End Sub

        ''' <summary>The six shipped spellings of the build switch (compared without regard to case).</summary>
        Friend ReadOnly StreamCatalogBuildSpellings As String() = {
            "--build-stream-catalog", "-build-stream-catalog", "/build-stream-catalog",
            "--build-catalog", "-build-catalog", "/build-catalog"}

        ''' <summary>The six shipped spellings of the check switch.</summary>
        Friend ReadOnly StreamCatalogCheckSpellings As String() = {
            "--check-stream-catalog", "-check-stream-catalog", "/check-stream-catalog",
            "--check-catalog", "-check-catalog", "/check-catalog"}

        Friend Function ToMarkdown() As String
            Return CommandDocs.ToMarkdown(Build(), SettingLabelEnglish, SettingPlaceEnglish)
        End Function

        ''' <summary>The frozen spellings of rule 2 that keep their old transport and behaviour: an empty command
        ''' line and the show-window token. Anything that is not a verb word stays the old path argument.</summary>
        Friend Const LegacyShowToken As String = "::fms-show-window::"

        ''' <summary>
        ''' Whether a command line is a command of this profile (APP-COMMANDS 0.2 item A.1). A first argument is a
        ''' command when it is a reserved word - the first word of a verb or of an alias spelling - and a reserved
        ''' word wins over a file of the same name; the disk is never probed. Any other first word (the frozen
        ''' spellings: empty, the show token, a path, -NoBack, a ".ext" name, a bare file name) keeps being read as
        ''' the path it always was, by the verb that opens a file.
        ''' </summary>
        Friend Function IsCommandLine(args As IList(Of String)) As Boolean
            If args Is Nothing OrElse args.Count = 0 Then Return False
            Return CommandLineParser.IsReservedFirstWord(args(0), Build())
        End Function

    End Module

End Namespace
