# Pointer - `APP-COMMANDS`

| | |
| --- | --- |
| **Id** | `APP-COMMANDS` |
| **Version** | implements 0.2 in part, read at 0.2 on 2026-11-15 (L0323 catch-up; built against 0.1 under tickets L0315 to L0319, the research behind it is ticket L0314 and the two proposals of 2026-11-14). Ticket L0324 (2026-11-15, compiled by `tools/Build-CliDocs.ps1` for the shared library only, nothing run) built items F and T.1 (the wire `origin` is no longer read, `legacy` carries the shipped spellings), E (the alias object) and C (`forward: never`) and A.2 (`defaultFor`) and N.3 (`cliVersion`) **in the viewer's table and answers** (the Share Manager's table and answers got C and N.3 too, not E), and the CAPTURE-OUTPUT 0.8 line of section 5 (a missing `--out` folder is refused). The second pass of L0324 (2026-11-15, same compile rule: only the shared library was compiled, by `tools/Build-CliDocs.ps1`; the viewer partials, the Share Manager's wiring, both clients and both test projects were not built) built A.1 (the reserved first words win over a path, no disk probe), I.3 (the caller compares `requestId` and exits 2 on an empty or foreign one; both receivers answer a line they cannot read `refused`, `requestId` `""`, into the validated reply file), L (the 64 KiB response bound, `response-too-large`), M (the reasons `error`, `no-space`, `response-too-large`) and N.1 (`quit --stop-jobs`, exit 11 `busy` naming the file operations). The third pass of L0324 (2026-11-15, same compile rule) built H on `open` (the target marked `sensitive`, `--target-file <path>` read once by the sender and carried as the target, never logged) and the R.4 member `oneOf` (`[["target","target-file"]]`, held by the parser). The same pass gave the Share Manager's table its alias objects (item E: `implies` and `answers: false`), with no spelling marked `legacy`. Still open, the exceptions of the registry rows of 2026-11-15 (both `partial`) narrowed to: `legacy: true` on the Share Manager's shipped spellings (item F.1; held back so a `legacy` request lifts the setting for none of its verbs, an owner decision recorded in L0324); an oversized transport payload cannot be answered (the transport refuses it before reading, so no reply path is known). The rows move after the release conformance run, never before it |
| **Home** | `app-commands/README.md` in the shared contracts catalog |
| **Role here** | implementer - the mainline viewer `FastMediaSorter_LITE.exe` answers the base five verbs (`src/AppCommands/`, `src/Main_Form.AppCommands.vb`, the call in `src/Application_Events.vb`) and, since ticket L0317, `open`, `phone-code receive`, `stream-catalog build` and `stream-catalog check` on its frozen spellings, and, since ticket L0318, the nine drive-and-observe verbs (`file ..`, `slideshow ..`, `screenshot window`; `src/AppCommands/ViewerNavigation.vb`, `src/Main_Form.AppCommandsDrive.vb`); the Share Manager `FastMediaSorterCompanion.exe` answers its own verbs (ticket L0316: `src/AppCommands/ShareManagerCommandTable.vb`, `src/FastMediaSorterCompanion/TrayContext.AppCommands.vb`, `LaunchArguments.vb`, `ShareManagerCommandClient.vb`). The net48 x86 viewer implements nothing of it |

## What this repository must do to stay conformant

- **One table in code, three renders (rules 18 and 20).** `src/AppCommands/ViewerCommandTable.vb` is the only
  declaration of the viewer's verbs. `docs/cli-commands.json`, `docs/CLI.md` and the output of
  `commands --json` are rendered from it (`tools/Build-CliDocs.ps1`, `-Check` to compare); `AppCommandsTests`
  fails when either document disagrees with it. Never edit the two documents by hand.
- **The strict parser is shared by both ends (rules 1, 3, 7).** `CommandLineParser.Parse` refuses an unknown
  verb, option or value and anything over 64 KiB per request or 32 KiB per argument with exit 10 and runs
  nothing; the first instance re-parses the forwarded arguments with the same parser.
- **The frozen spellings stay ordinary launches (rule 2).** The empty command line, `::fms-show-window::`,
  every path and `-NoBack` keep their old transport (`FMSV`) and behaviour, and the manifest now lists them as alias
  objects (`spelling`, `implies`, `legacy`, `answers`; `show` lists `""` and `::fms-show-window::`, `open` carries
  `defaultFor` `FastMediaSorter_LITE.exe`). **Reserved words (0.2 item A.1, ticket L0324):** a first word that is a verb
  start of the table is a command, whatever file or folder of that name exists in the working folder - the clients
  call `IsCommandLine(args)` with the one argument and no longer probe the disk (`CommandLineParser.IsReservedFirstWord`;
  a file that shares a verb's name is opened as `open <path>`, `.\status` or its full path).
- **Transport (rules 6 to 9; proposal items 1 and 2).** The second process decides its role before the mutex
  and the first window, sends one request line over `WM_COPYDATA` tagged `FMSC` with a 1 s delivery bound, and
  reads the answer from the reply file it names under `%TEMP%\FastMediaSorter-commands\<requestId>.reply.json`;
  the first instance writes only there (any other reply path is ignored). Completion is the verb's `timeoutMs`.
  An answer of 0 from every window means an older copy or the x86 build: exit 2 with a sentence, and for `show`
  the old `::fms-show-window::` forward as well.
- **Gate (rule 13; proposal item 6).** The one setting is `AllowAppCommands` (Settings, Files, Keys
  and associations), off by default, not exported in a settings file, reset by the profile reset, and changed
  by no command. It gates every request for every class but `safe`. **The wire `origin` is not read** (0.2 items F and
  T.1: a `WM_COPYDATA` sender cannot be proven, so `CommandRequest.TryParse` drops the member and the log line
  says `origin=cli`). The one lift is the request member `legacy: true` (item F), honoured only for a verb whose
  manifest lists a legacy alias (`CommandSpec.HasLegacyAlias`: `show`, `phone-code receive`, the `stream-catalog`
  switch spellings) and lifting rule 13 and nothing else - the parser, elevation and busy still apply. The
  Share Manager's receiver takes the same path. The base five verbs and `open` are `safe`; `phone-code receive` and the two `stream-catalog` verbs are
  `writes-user-data` (ticket L0317).
- **Busy (rule 10)** while a modal window or the welcome wizard is up, and for `quit` while file operations
  are pending unless `quit --stop-jobs` consents (item N.1: the batch is cancelled, then the window closes; the refusal
  is exit 11 `busy` with a sentence that names the running file operations and the option, never a path; the window's own
  close path drains the one operation in flight and may still ask "Close anyway / Wait longer" past 15 s - open, ticket L0324);
  **elevation (rule 12)** when the running copy is elevated and the caller is not.
- **Answers (0.2 items I.3, L, M; ticket L0324).** The caller accepts a reply only when its `requestId` is the one it
  sent, else exit 2 `schema`. A line a receiver cannot read is answered `refused` with `requestId` `""` into the
  reply file it names, when that file is a GUID-named one under the product's own reply folder
  (`CommandFiles.IsValidReplyFile`). A response over 64 KiB (`CommandBounds.MaxResponseBytes`) is replaced by
  `response-too-large` (exit 1, `failed`). The closed reasons added: `error` (failed, 1), `no-space` (failed, 13).
- **Log (rule 16).** One `current.log` line per accepted request: verb, origin, caller pid, outcome, option
  names and a count of values - never a value.

## The viewer's frozen verbs (ticket L0317; rules 2, 3, 4, 9, 13 and case C13, proposal items 3, 4, 6)

- **`open <target> [--no-back]`** is the bare launch as a verb. Its shipped spellings (`<path>`, `-NoBack <path>`,
  `<path> -NoBack`) are listed as its aliases and stay ordinary launches, never gated. The verb answers
  `kind` and `count` (never a file name), exit 11 `target-missing` for a path that is not there, exit 10 without a
  target, exit 2 `timeout` for a locked file that has not unlocked yet (it still opens when it unlocks). A
  `::name::` token is refused as a target. `OpenTargets.vb` is the pure half (Lite.Tests); `Main_Form.AppCommands.vb`
  claims the open the entry started and reports what landed.
- **`phone-code receive --secret-file <path>`** is the verb behind the Share Manager relay. The file holds the
  code; the viewer reads it, keeps nothing, logs only the option name and shows the import dialog (the reply is
  written before the dialog opens, because the dialog is modal). It is refused with exit 12 `switch-off` while the
  setting is off; only a request that carries `legacy: true` (the relay, the old `::fms-phone-code::` spellings, both
  listed as legacy aliases of the verb) is admitted without the setting.
- **`stream-catalog build` / `check`** share one implementation with the six shipped switch spellings
  (`StreamCatalogCli.BuildCore` / `CheckCore`) and run in the calling process (`oneshot`). Exit codes moved to rule 11:
  usage 2 to 10, missing input 11 (the modern exe only; the net48 branch still only refuses with 1). The switches
  stay ungated, the verbs are held by the setting.
- **Cold start** (`ViewerCommandClient`): `open` and `phone-code receive` start the viewer with the target (or the
  code file) as the launch text, so the first window consumes it exactly as a bare launch would.

## The viewer's drive-and-observe verbs (ticket L0318; rules 1, 4, 8, 9, 13, 14, CAPTURE-OUTPUT, proposal item 5)

- **Nine verbs, `coldStart: refuse`, manifest version 1.1.** `file current`, `file next`, `file previous`,
  `file first`, `file last`, `file goto --number <n>`, `slideshow start [--random] [--interval-ms <n>]`,
  `slideshow stop` and `screenshot window --out <png> [--overwrite]`. `file current` and `screenshot window` are
  `reads-user-data` (gated, exit 12 while the setting is off); the other seven are `safe`.
- **One implementation each.** A move calls `ReadShowMediaFile(Mode_Next / Mode_Prev)` or `JumpTo` - the code behind
  the arrow, Home, End and jump keys; a slideshow calls `SetSlideShow` / `SetRandomSlideShow` / `SlideShowStop` (the
  toolbar button's code). `ViewerNavigation.vb` is the pure half (the plan of a move, the pause bound, the screenshot
  name and target, the PNG `tIME` chunk) and is tested without a window; `Main_Form.AppCommandsDrive.vb` is the glue.
  The window's list position and the saved slideshow interval are written by the pipeline and the settings window
  only - a source scan in `AppCommandsTests` holds that.
- **Ends stay put.** A step past the first or last file answers the unchanged place with exit 0; `file goto` outside
  `1..count` is exit 10. A move while a folder scan or an archive open is still running is answered once the listing
  has landed; one that cannot be started is exit 11 `busy`.
- **Screenshot.** `PrintWindow` with the full-content flag draws the whole window rectangle into a 24-bit bitmap; the
  PNG carries a `tIME` chunk of the local second (CAPTURE-OUTPUT 0.5). The path is used exactly as given; a folder gets
  `screenshot_<yyMMdd>_<HHmmss>.png` (a ` (2)` while taken); an existing file without `--overwrite` is exit 11, a full
  disk exit 13 `Resources`. The folder of `--out` is never created (CAPTURE-OUTPUT 0.8 section 5, ticket L0324): a
  missing one is refused before the window is captured as exit 11 `target-missing`
  (`CommandFiles.WriteWhole(.., createFolder:=False)` throws `DirectoryNotFoundException` for the late race). The
  reply and `--result` folders are the product's own and are still created. A full disk answers `no-space` (item M, ticket L0324).

## The Share Manager (ticket L0316)

- **Its own table, the same renders (rules 18 and 20).** `ShareManagerCommandTable.vb` declares `commands`,
  `version`, `status`, `show`, `quit`, `start [--silent]`, `share add`, `resource add [--camera]`,
  `schedule open`, `broadcast open`, `clipboard send` and `clipboard get`. `docs/cli-commands.share-manager.json`,
  its section of `docs/CLI.md` (after the viewer's document, which stays byte for byte) and its
  `commands --json` come from it; `CliDocument.vb` composes the file for the generator and the tests only.
- **Same library, own wiring.** The Share Manager links the shared files of `src/AppCommands/` and
  decides its role in `Program.vb` before the mutex and any window. Cold start per verb is in the table:
  `start` verbs open the tray and the window, `clipboard` runs the one-shot, `quit`/`commands`/`version`
  never start anything, `status` is refused with exit 11.
- **Transport.** FMSC over `WM_COPYDATA`, the reply under `%TEMP%\FastMediaSorter-share-manager-commands\`.
  The viewer's messages and every shipped spelling keep the legacy tag and are admitted unchanged (the legacy tag
  is the transport's, not a claim about who sent it; the profile request carries no origin of its own that is trusted);
  an older manager answers a profile request 0 and gets exit 2 plus the old alias. `commands` and `version` are
  `forward: never` and `version` answers `cliVersion`. The table does not yet list its aliases as objects, so no verb
  of the manager has a legacy alias in the manifest and a request tagged `legacy: true` lifts nothing there; marking them
  would widen the lift for `share add` and `clipboard send/get`, an owner decision (open, ticket L0324).
- **Gate.** `AllowAppCommands` is the one setting for both programs. From a command line it gates the
  non-safe verbs `share add`, `clipboard send` (leaves the machine) and `clipboard get` (writes user data), exit 12.
  `share add` stays a proposal the user answers in the window and says `accepted`.
- **Busy** while a modal window is open, except `status` and `share add` (the proposal queue holds it).
- **Clipboard.** The pair answers with an outcome token and a byte count through `--result`; exit codes 0, 1, 11, 13, 15, 12, 2.
- **Observe verbs (ticket L0319, table 1.1).** `share list` (`reads-user-data`, manager only: cold start `refuse`),
  `schedule list` (`reads-user-data`, `oneshot`: with no manager the process reads the operation files, answers and
  exits - no tray, no scheduler, nothing runs) and `exchange status` (`safe`, `oneshot`: no network call; with no
  manager it reads whether an account is set up and answers state `stopped`, nothing pending). Each answer is a documented
  object (`shares`; `operations`; `accountConfigured`, `state`, `pendingRequests`) with closed tokens, built in
  `ShareManagerObserve.vb` from only the members it publishes - no password, PIN, token, key, server, user name, peer
  address, operation folder or error text - and a test fixes the member names. All three answer while a window is open.
  `pendingRequests` is the folder proposals (queued plus the one on screen) that wait for the person: the exchange client
  keeps no other request that waits for a person. No new reason token; rule 13 gates the two `reads-user-data` verbs.
- **Schedule management and journal verbs (ticket L0345, manifest 1.2).** `schedule add` (`writes-user-data`, `start`),
  `schedule remove` (`writes-user-data`, `start`), `schedule run` (`writes-user-data`, `start`, `wait: accepted`, `oneOf`
  `[["target","all"]]`) and `schedule history` (`reads-user-data`, `oneshot`, ignores busy), implemented in
  `ShareManagerScheduleCli.vb` and wired in `ShareManagerCommandClient.vb` and `TrayContext.AppCommands.vb`. When no
  manager is running, `add`, `remove` and `run` start the manager silently (`--tray`), deliver the request and exit with
  its answer (`schedule add` upserts by action + source + destination and enforces `ScheduledValidation` and
  `FileDoBackupProbe.HasGenerations`).

Not verified yet (release items of L0315, L0316, L0317, L0318, L0319, L0324 and L0345): the conformance run
`pwsh -NoProfile -File tools\Invoke-Tests.ps1 -Suite lite -Filter AppCommandsConformance` and the Share Manager's
`-Suite companion -Filter "ShareManagerCommands|ShareManagerScheduleCli"` over the L0324/L0345 edits (the Companion,
viewer partials and test projects were not compiled by `Invoke-Tests.ps1 -Build` or `Run-AllTests.ps1` between releases;
only the pure `AppCommands` profile library compiled via `Build-CliDocs.ps1`),
the net48 compile of the shared sources, the missing `--out` folder on a real window, `quit --stop-jobs` against a running
batch, a line that cannot be read answered into its reply file by both receivers, the two-process round trips C1 to C8 against a real build,
the three observe verbs against a real worker with two shared folders, a real scheduled operation and an exchange account (the test host of ticket 182),
every spelling of `open`, `phone-code receive` and `stream-catalog` on screen in both exes, each of the nine
drive-and-observe verbs on a real window (local folder, network share, archive), the Share Manager relay
against a running and a stopped viewer,
both programs, the older-copy answer against the x86 exe and a manager without the profile; the two desktop
shortcuts and the viewer's folder menu against a running and a stopped manager.
