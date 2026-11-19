# Command line of FastMediaSorter_LITE.exe

<!-- Generated from the command table in code (tools/Build-CliDocs.ps1); never edit by hand. -->

Fast Media Sorter & Sharing, command set 1.1, contract `APP-COMMANDS` 0.1. The same list in machine form is `docs/cli-commands.json` and the output of `FastMediaSorter_LITE.exe commands --json`.

A command is handed to the copy that is already running; that copy does the work and answers, and the program you started exits with the answer's code. The program is a window program: from `cmd` use `start /wait`, from PowerShell `Start-Process -Wait -PassThru`, and read the exit code. Text is printed to the console the program was started from; `--json` prints the response line itself, and `--result <path>` writes the returned data to a file.

## Commands

| Command | What it does | Risk | With nothing running | Bound |
| --- | --- | --- | --- | --- |
| [`commands`](#commands) | List the commands this program accepts. | `safe` | answers by itself, starts no window | 5000 ms |
| [`version`](#version) | Print the version of this program. | `safe` | answers by itself, starts no window | 5000 ms |
| [`status`](#status) | Report whether the program is running and ready for commands. | `safe` | exit 11, not running | 2000 ms |
| [`show`](#show) | Bring the window to the front, starting the program when it is not running. | `safe` | starts the program | 5000 ms |
| [`quit`](#quit) | Close the running program. | `safe` | answers by itself, starts no window | 10000 ms |
| [`open`](#open) | Show a file or a folder in the viewer, or hand a resource locator or a document the viewer reads to the dialog that reads it. The bare launch (the verb word left out) is this verb. | `safe` | starts the program | 60000 ms |
| [`phone-code receive`](#phone-code-receive) | Take the connection code a phone showed (relayed by the Share Manager) into the Resources panel's import dialog; nothing is added until the person confirms. | `writes-user-data` | starts the program | 20000 ms |
| [`stream-catalog build`](#stream-catalog-build) | Build a stream-catalog.zip from an M3U or M3U8 playlist or a folder of playlists. | `writes-user-data` | answers by itself, starts no window | 600000 ms |
| [`stream-catalog check`](#stream-catalog-check) | Run the catalog checks V1 to V12 on an existing stream-catalog.zip and write the report beside it. | `writes-user-data` | answers by itself, starts no window | 120000 ms |
| [`file current`](#file-current) | Say which file the viewer shows now. | `reads-user-data` | exit 11, not running | 2000 ms |
| [`file next`](#file-next) | Show the next file of the listing, as the right arrow does; on the last file it stays there, where the arrow key wraps to the first. | `safe` | exit 11, not running | 5000 ms |
| [`file previous`](#file-previous) | Show the previous file of the listing; on the first file it stays there. | `safe` | exit 11, not running | 5000 ms |
| [`file first`](#file-first) | Show the first file of the listing. | `safe` | exit 11, not running | 5000 ms |
| [`file last`](#file-last) | Show the last file of the listing. | `safe` | exit 11, not running | 5000 ms |
| [`file goto`](#file-goto) | Show the file at a given place of the listing. | `safe` | exit 11, not running | 5000 ms |
| [`slideshow start`](#slideshow-start) | Start the slideshow as the toolbar button does, or at random with --random. | `safe` | exit 11, not running | 5000 ms |
| [`slideshow stop`](#slideshow-stop) | Stop the slideshow. | `safe` | exit 11, not running | 5000 ms |
| [`screenshot window`](#screenshot-window) | Save what the viewer window shows now as a PNG image. | `reads-user-data` | exit 11, not running | 10000 ms |

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | done |
| 1 | the command ran and the operation failed |
| 2 | not proven: no answer inside the bound, or the running copy is older and must be restarted |
| 10 | usage: unknown command, unknown option, bad value, request too large |
| 11 | not ready: nothing running, or busy with an open window |
| 12 | permission: the setting below is off, or the running copy is elevated and the caller is not |
| 13 | resources: too large, or no room |
| 15 | network: the server did not answer |
| 16 | stopped by the person |

## Risk classes and the setting

A `safe` command is always accepted. A command of any other class (`reads-user-data`, `writes-user-data`, `leaves-machine`) is refused with exit 12 while the setting **Allow commands from other programs** (Settings, Files, Keys and associations) is off. It is off until the person turns it on, and no command changes it.

## commands

List the commands this program accepts.

```
commands [--json]
```

- Risk: `safe`; with nothing running: answers by itself, starts no window; answer: `done` within 5000 ms; idempotent: yes; since 1.0.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the manifest (docs/cli-commands.json) instead of the readable list |

| Exit | Meaning |
| --- | --- |
| 0 | printed |
| 10 | unknown option |

```powershell
Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'commands --json' -RedirectStandardOutput C:\temp\fms-commands.json -Wait
```

Expect: exit 0 and the file equals docs/cli-commands.json.

## version

Print the version of this program.

```
version [--json]
```

- Risk: `safe`; with nothing running: answers by itself, starts no window; answer: `done` within 5000 ms; idempotent: yes; since 1.0.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `version` | string | the program version |
| `cliVersion` | string | the version of the command set this manifest describes |

| Exit | Meaning |
| --- | --- |
| 0 | printed |
| 10 | unknown option |

```bat
start /wait FastMediaSorter_LITE.exe version
```

Expect: exit 0 and the version on the console.

## status

Report whether the program is running and ready for commands.

```
status [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 2000 ms; idempotent: yes; since 1.0.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `version` | string | the running program's version |
| `pid` | int | its process id |
| `exe` | string | its file name |
| `ready` | bool | true when no window blocks commands |
| `acceptsRestrictedCommands` | bool | whether the setting admits commands of a class other than safe |

| Exit | Meaning |
| --- | --- |
| 0 | running and ready |
| 11 | not running, or busy |

```powershell
(Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'status' -Wait -PassThru).ExitCode
```

Expect: 0 while the program runs, 11 otherwise.

## show

Bring the window to the front, starting the program when it is not running.

```
show
```

- Risk: `safe`; with nothing running: starts the program; answer: `done` within 5000 ms; idempotent: yes; since 1.0.
- Also reached by: (no arguments), `::fms-show-window::`.

| Exit | Meaning |
| --- | --- |
| 0 | shown |
| 2 | the running copy is older and must be restarted |
| 11 | busy |

```bat
start /wait FastMediaSorter_LITE.exe show
```

Expect: exit 0 and the window in front.

## quit

Close the running program.

```
quit [--stop-jobs]
```

- Risk: `safe`; with nothing running: answers by itself, starts no window; answer: `done` within 10000 ms; idempotent: yes; since 1.0.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--stop-jobs` | bool | no | Consent to stop file operations that are running: they are told to start nothing further, the one in progress is finished, and the program closes. Without it, quit is refused (busy) while file operations run. |

| Exit | Meaning |
| --- | --- |
| 0 | closed, or nothing was running |
| 1 | the window did not close |
| 11 | busy: file operations are running and --stop-jobs was not given, or a window is open |

```powershell
(Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'quit' -Wait -PassThru).ExitCode
```

Expect: 0, and no viewer process is left.

## open

Show a file or a folder in the viewer, or hand a resource locator or a document the viewer reads to the dialog that reads it. The bare launch (the verb word left out) is this verb.

```
open (<target> | --target-file <path>) [--no-back] [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: starts the program; answer: `done` within 60000 ms; idempotent: yes; since 1.0.
- Also reached by: `<path>`, `-NoBack <path>`, `<path> -NoBack`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `<target>` | string, sensitive | no | A file, a folder, an archive, a UNC share path, a resource locator (sftp:// or gd://), a Share Manager document (.fmscfg, .fmsexch while the exchange client is offered) or a live broadcast descriptor (a .fmsbcast file or an intent link). An internal ::name:: token is refused. An address may carry a user name and password (smb://user:password@host/share): the value is never written to the log or an answer. |
| `--target-file` | path | no | Read the target from this file instead of the command line (one line, UTF-8), so an address with a password stays out of the shell history. Give either the target or this option. |
| `--no-back` | bool | no | Keep background tasks off for the session (the shipped -NoBack switch, any letter case, before or after the target). |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

Exactly one of `<target>`, `--target-file` is given.

| Returned | Kind | Meaning |
| --- | --- | --- |
| `kind` | string | what the target turned out to be: image, video, audio, document, archive, folder, resource or other |
| `count` | int | how many files the folder listing holds; no names |

| Exit | Meaning |
| --- | --- |
| 0 | shown, or handed to the dialog that reads it |
| 2 | not proven: the running program may still be working |
| 10 | no target given, both a target and --target-file given, or the file holds no single line |
| 11 | the target or the --target-file file does not exist, or the viewer is busy |
| 15 | the network share did not answer |

```bat
start /wait FastMediaSorter_LITE.exe open D:\Photos\2026
```

Expect: exit 0; the folder is on screen.

```powershell
Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'open \\nas\media\clip.mp4 --result C:\temp\open.json' -Wait
```

Expect: exit 0; the file holds {"kind":"video","count":N}.

```bat
start /wait FastMediaSorter_LITE.exe open --target-file C:\temp\nas-address.txt
```

Expect: exit 0; the address in the file is open, and it is in neither the shell history nor the log.

## phone-code receive

Take the connection code a phone showed (relayed by the Share Manager) into the Resources panel's import dialog; nothing is added until the person confirms.

```
phone-code receive --secret-file <path>
```

- Risk: `writes-user-data`; with nothing running: starts the program; answer: `done` within 20000 ms; idempotent: no; since 1.0.
- Also reached by: `::fms-phone-code::`, `::fms-phone-code-file::`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--secret-file` | path | yes | A UTF-8 file that holds the phone connection code, or the code document the Share Manager opened. The viewer reads it, keeps nothing and never logs it. The shipped ::fms-phone-code:: spelling carries the code itself inside the window message, never on a command line. |

| Exit | Meaning |
| --- | --- |
| 0 | the import dialog is shown |
| 10 | no --secret-file given |
| 11 | the file does not exist, or the viewer is busy |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'phone-code receive --secret-file C:\temp\code.txt' -Wait -PassThru
```

Expect: exit 0; the import dialog shows what would be added.

## stream-catalog build

Build a stream-catalog.zip from an M3U or M3U8 playlist or a folder of playlists.

```
stream-catalog build <input> [--out <zip>] [--overwrite] [--force] [--json] [--result <path>]
```

- Risk: `writes-user-data`; with nothing running: answers by itself, starts no window; answer: `done` within 600000 ms; idempotent: yes; since 1.0.
- Also reached by: `--build-stream-catalog`, `-build-stream-catalog`, `/build-stream-catalog`, `--build-catalog`, `-build-catalog`, `/build-catalog`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `<input>` | path | yes | The playlist file or the folder that holds playlists. |
| `--out` | path | no | The archive to write; stream-catalog.zip in the input folder (or beside the input file) when absent. The shipped option spellings -o, /o, --output and /output are accepted after a shipped verb spelling. |
| `--overwrite` | bool | no | Replace an existing archive, under the churn guard. The shipped verb spellings imply it, because they always replaced. |
| `--force` | bool | no | Bypass the churn guard that refuses a replacement losing more than 20 percent of the previous identities. The shipped spellings -f, /f, --force and /force are accepted. |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `rows` | int | rows written |
| `passed` | bool | whether the built archive passed its own checks |

| Exit | Meaning |
| --- | --- |
| 0 | built and checked |
| 1 | the build failed or the churn guard refused |
| 10 | usage |
| 11 | the input does not exist |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'stream-catalog build D:\lists --out D:\lists\stream-catalog.zip --overwrite' -Wait -PassThru
```

Expect: exit 0; the archive exists.

## stream-catalog check

Run the catalog checks V1 to V12 on an existing stream-catalog.zip and write the report beside it.

```
stream-catalog check <archive> [--json] [--result <path>]
```

- Risk: `writes-user-data`; with nothing running: answers by itself, starts no window; answer: `done` within 120000 ms; idempotent: yes; since 1.0.
- Also reached by: `--check-stream-catalog`, `-check-stream-catalog`, `/check-stream-catalog`, `--check-catalog`, `-check-catalog`, `/check-catalog`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `<archive>` | path | yes | The stream-catalog.zip to check. |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `passed` | bool | whether every check passed |
| `failedChecks` | int | how many checks failed |

| Exit | Meaning |
| --- | --- |
| 0 | every check passed |
| 1 | a check failed |
| 10 | usage |
| 11 | the archive does not exist |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```bat
start /wait FastMediaSorter_LITE.exe stream-catalog check D:\lists\stream-catalog.zip
```

Expect: exit 0 when the archive passes; <stem>-report.txt is beside it.

## file current

Say which file the viewer shows now.

```
file current [--json] [--result <path>]
```

- Risk: `reads-user-data`; with nothing running: exit 11, not running; answer: `done` within 2000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `path` | path | the full path of the shown file |
| `index` | int | its place in the listing, counted from 1 |
| `count` | int | how many files the listing holds |
| `kind` | string | image, video, audio, document, text, archive or other |

| Exit | Meaning |
| --- | --- |
| 0 | answered |
| 11 | not running, busy, or nothing is shown |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'file current --result C:\temp\cur.json' -Wait; Get-Content C:\temp\cur.json -Raw | ConvertFrom-Json
```

Expect: an object with path, index, count and kind.

## file next

Show the next file of the listing, as the right arrow does; on the last file it stays there, where the arrow key wraps to the first.

```
file next [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: no; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `index` | int | the new place, counted from 1 |
| `count` | int | how many files the listing holds |
| `kind` | string | what the new file is: image, video, audio, document, text, archive or other |

| Exit | Meaning |
| --- | --- |
| 0 | moved, or already there |
| 11 | not running, busy, or nothing is shown |

```bat
start /wait FastMediaSorter_LITE.exe file next
```

Expect: exit 0; the next file is on screen.

## file previous

Show the previous file of the listing; on the first file it stays there.

```
file previous [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: no; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `index` | int | the new place, counted from 1 |
| `count` | int | how many files the listing holds |
| `kind` | string | what the new file is: image, video, audio, document, text, archive or other |

| Exit | Meaning |
| --- | --- |
| 0 | moved, or already there |
| 11 | not running, busy, or nothing is shown |

```bat
start /wait FastMediaSorter_LITE.exe file previous
```

Expect: exit 0; the previous file is on screen.

## file first

Show the first file of the listing.

```
file first [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `index` | int | always 1 |
| `count` | int | how many files the listing holds |

| Exit | Meaning |
| --- | --- |
| 0 | moved, or already there |
| 11 | not running, busy, or nothing is shown |

```bat
start /wait FastMediaSorter_LITE.exe file first
```

Expect: exit 0; the first file is on screen.

## file last

Show the last file of the listing.

```
file last [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `index` | int | the last place, the same number as count |
| `count` | int | how many files the listing holds |

| Exit | Meaning |
| --- | --- |
| 0 | moved, or already there |
| 11 | not running, busy, or nothing is shown |

```bat
start /wait FastMediaSorter_LITE.exe file last
```

Expect: exit 0; the last file is on screen.

## file goto

Show the file at a given place of the listing.

```
file goto --number <n> [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--number` | int | yes | The place, counted from 1. |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `index` | int | the place shown |
| `count` | int | how many files the listing holds |

| Exit | Meaning |
| --- | --- |
| 0 | moved, or already there |
| 10 | the number is not given, or is outside the listing |
| 11 | not running, busy, or nothing is shown |

```bat
start /wait FastMediaSorter_LITE.exe file goto --number 40
```

Expect: exit 0; file 40 is on screen.

## slideshow start

Start the slideshow as the toolbar button does, or at random with --random.

```
slideshow start [--random] [--interval-ms <n>] [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--random` | bool | no | Start the random slideshow (the random button). Without it the order is the one the setting for the slideshow order gives. |
| `--interval-ms` | int | no | The pause between files in milliseconds, 1000 to 120000, for this run only; the saved setting does not change. |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `running` | bool | true once the slideshow runs |
| `intervalMs` | int | the pause in use |

| Exit | Meaning |
| --- | --- |
| 0 | running |
| 10 | the pause is not a whole number from 1000 to 120000 |
| 11 | not running, busy, or nothing to show |

```bat
start /wait FastMediaSorter_LITE.exe slideshow start --random --interval-ms 8000
```

Expect: exit 0; the slideshow runs.

## slideshow stop

Stop the slideshow.

```
slideshow stop
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Exit | Meaning |
| --- | --- |
| 0 | stopped, or was not running |
| 11 | the viewer is not running or is busy |

```bat
start /wait FastMediaSorter_LITE.exe slideshow stop
```

Expect: exit 0.

## screenshot window

Save what the viewer window shows now as a PNG image.

```
screenshot window --out <png> [--overwrite] [--json] [--result <path>]
```

- Risk: `reads-user-data`; with nothing running: exit 11, not running; answer: `done` within 10000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--out` | path | yes | The PNG file to write, used exactly as given; a folder gets screenshot_<yyMMdd>_<HHmmss>.png inside it. A relative path is relative to the caller. The folder of the file must exist: it is never created (a missing one is refused as target-missing). |
| `--overwrite` | bool | no | Replace the file when it exists. |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `width` | int | width in pixels |
| `height` | int | height in pixels |

| Exit | Meaning |
| --- | --- |
| 0 | saved |
| 10 | no --out given |
| 11 | not running, busy, the folder of --out does not exist, or the file exists and --overwrite is absent |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |
| 13 | not enough space for the file |

```powershell
Start-Process .\FastMediaSorter_LITE.exe -ArgumentList 'screenshot window --out C:\temp\viewer.png --overwrite' -Wait -PassThru
```

Expect: exit 0; C:\temp\viewer.png is the window as drawn.

## Command line of FastMediaSorterCompanion.exe

<!-- Generated from the command table in code (tools/Build-CliDocs.ps1); never edit by hand. -->

Share Manager - Fast Media Sorter & Sharing, command set 1.3, contract `APP-COMMANDS` 0.1. The same list in machine form is `docs/cli-commands.json` and the output of `FastMediaSorterCompanion.exe commands --json`.

A command is handed to the copy that is already running; that copy does the work and answers, and the program you started exits with the answer's code. The program is a window program: from `cmd` use `start /wait`, from PowerShell `Start-Process -Wait -PassThru`, and read the exit code. Text is printed to the console the program was started from; `--json` prints the response line itself, and `--result <path>` writes the returned data to a file.

### Commands

| Command | What it does | Risk | With nothing running | Bound |
| --- | --- | --- | --- | --- |
| [`commands`](#share-manager-commands) | List the commands this program accepts. | `safe` | answers by itself, starts no window | 5000 ms |
| [`version`](#share-manager-version) | Print the version of this program. | `safe` | answers by itself, starts no window | 5000 ms |
| [`status`](#share-manager-status) | Report whether the Share Manager is running and ready for commands. | `safe` | exit 11, not running | 2000 ms |
| [`show`](#share-manager-show) | Bring the Share Manager window forward, starting the program when it is not running. | `safe` | starts the program | 5000 ms |
| [`quit`](#share-manager-quit) | Close the running Share Manager, as the tray menu Exit does. | `safe` | answers by itself, starts no window | 10000 ms |
| [`start`](#share-manager-start) | Make sure the Share Manager is running and apply its startup option as a plain launch does; with --silent it shows no window and no hint, as the logon autostart does. | `safe` | starts the program | 20000 ms |
| [`share add`](#share-manager-share-add) | Propose a folder to share; the person answers the proposal in the manager before anything is shared. | `leaves-machine` | starts the program | 5000 ms |
| [`resource add`](#share-manager-resource-add) | Open the guided flow that adds a resource, with the camera QR scan when --camera is given. | `safe` | starts the program | 10000 ms |
| [`schedule open`](#share-manager-schedule-open) | Open the Scheduled file operations window; a folder pre-fills the source of a new operation that the person still has to save. | `safe` | starts the program | 10000 ms |
| [`broadcast open`](#share-manager-broadcast-open) | Open the live broadcast window with a path preselected. | `safe` | starts the program | 10000 ms |
| [`clipboard send`](#share-manager-clipboard-send) | Send the clipboard to the exchange server and say how it went. | `leaves-machine` | answers by itself, starts no window | 120000 ms |
| [`clipboard get`](#share-manager-clipboard-get) | Fetch the newest clipboard from the exchange server into the local clipboard and say how it went. | `writes-user-data` | answers by itself, starts no window | 120000 ms |
| [`share list`](#share-manager-share-list) | List the folders the Share Manager shares and the state of each. | `reads-user-data` | exit 11, not running | 5000 ms |
| [`schedule list`](#share-manager-schedule-list) | List the scheduled file operations with their next run and their last result; with no manager running the program reads the operations, answers and exits, starting no scheduler. | `reads-user-data` | answers by itself, starts no window | 5000 ms |
| [`exchange status`](#share-manager-exchange-status) | Say what the exchange client last knew about its server; no network call is made. With no manager running the program reads whether an account is set up, answers (state stopped, no request waiting) and exits. | `safe` | answers by itself, starts no window | 5000 ms |
| [`schedule add`](#share-manager-schedule-add) | Create or update a scheduled backup operation without opening a window. | `writes-user-data` | starts the program | 20000 ms |
| [`schedule remove`](#share-manager-schedule-remove) | Delete a scheduled operation definition without touching any backup files. | `writes-user-data` | starts the program | 15000 ms |
| [`schedule run`](#share-manager-schedule-run) | Queue one scheduled operation or all enabled operations for immediate execution on the single lane; with --wait, wait for completion and exit with its outcome. | `writes-user-data` | starts the program | 600000 ms |
| [`schedule history`](#share-manager-schedule-history) | Print the scheduled operations journal; with no manager running the program reads the journal, answers and exits. | `reads-user-data` | answers by itself, starts no window | 5000 ms |

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | done |
| 1 | the command ran and the operation failed |
| 2 | not proven: no answer inside the bound, or the running copy is older and must be restarted |
| 10 | usage: unknown command, unknown option, bad value, request too large |
| 11 | not ready: nothing running, or busy with an open window |
| 12 | permission: the setting below is off, or the running copy is elevated and the caller is not |
| 13 | resources: too large, or no room |
| 15 | network: the server did not answer |
| 16 | stopped by the person |

### Risk classes and the setting

A `safe` command is always accepted. A command of any other class (`reads-user-data`, `writes-user-data`, `leaves-machine`) is refused with exit 12 while the setting **Allow commands from other programs** (the viewer's Settings window, Files, Keys and associations; the Share Manager reads the same value) is off. It is off until the person turns it on, and no command changes it.

<a id="share-manager-commands"></a>

### commands

List the commands this program accepts.

```
commands [--json]
```

- Risk: `safe`; with nothing running: answers by itself, starts no window; answer: `done` within 5000 ms; idempotent: yes; since 1.0.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the manifest (docs/cli-commands.share-manager.json) instead of the readable list |

| Exit | Meaning |
| --- | --- |
| 0 | printed |
| 10 | unknown option |

```powershell
Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'commands --json' -RedirectStandardOutput C:\temp\fms-sm-commands.json -Wait
```

Expect: exit 0 and the file equals docs/cli-commands.share-manager.json.

<a id="share-manager-version"></a>

### version

Print the version of this program.

```
version [--json]
```

- Risk: `safe`; with nothing running: answers by itself, starts no window; answer: `done` within 5000 ms; idempotent: yes; since 1.0.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `version` | string | the program version |
| `cliVersion` | string | the version of the command set this manifest describes |

| Exit | Meaning |
| --- | --- |
| 0 | printed |
| 10 | unknown option |

```bat
start /wait FastMediaSorterCompanion.exe version
```

Expect: exit 0 and the version on the console.

<a id="share-manager-status"></a>

### status

Report whether the Share Manager is running and ready for commands.

```
status [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: exit 11, not running; answer: `done` within 2000 ms; idempotent: yes; since 1.0.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `version` | string | the running program's version |
| `pid` | int | its process id |
| `exe` | string | its file name |
| `ready` | bool | true when no question or dialog blocks commands |
| `acceptsRestrictedCommands` | bool | whether the setting admits commands of a class other than safe |
| `workerAvailable` | bool | whether the sharing worker payload is installed |
| `sharing` | bool | whether sharing is switched on now |

| Exit | Meaning |
| --- | --- |
| 0 | running and ready |
| 11 | not running, or busy |

```powershell
(Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'status' -Wait -PassThru).ExitCode
```

Expect: 0 while the manager runs and is ready, 11 otherwise.

<a id="share-manager-show"></a>

### show

Bring the Share Manager window forward, starting the program when it is not running.

```
show
```

- Risk: `safe`; with nothing running: starts the program; answer: `done` within 5000 ms; idempotent: yes; since 1.0.
- Also reached by: `--show`, `/show`, `::fms-show-window::`.

| Exit | Meaning |
| --- | --- |
| 0 | the window is forward |
| 2 | the running copy is older and must be restarted |
| 11 | busy: a question or a dialog is open in the running program |

```bat
start /wait FastMediaSorterCompanion.exe show
```

Expect: exit 0 and the window in front.

<a id="share-manager-quit"></a>

### quit

Close the running Share Manager, as the tray menu Exit does.

```
quit
```

- Risk: `safe`; with nothing running: answers by itself, starts no window; answer: `done` within 10000 ms; idempotent: yes; since 1.0.

| Exit | Meaning |
| --- | --- |
| 0 | closed, or was not running |
| 11 | busy: a question or a dialog is open in the running program |

```bat
start /wait FastMediaSorterCompanion.exe quit
```

Expect: exit 0 and no Share Manager process is left.

<a id="share-manager-start"></a>

### start

Make sure the Share Manager is running and apply its startup option as a plain launch does; with --silent it shows no window and no hint, as the logon autostart does.

```
start [--silent]
```

- Risk: `safe`; with nothing running: starts the program; answer: `done` within 20000 ms; idempotent: yes; since 1.0.
- Also reached by: (no arguments), `--tray`, `/tray`, `::fms-plain-start::`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--silent` | bool | no | show nothing: no window, no tray hint; the shipped --tray spelling means this |

| Exit | Meaning |
| --- | --- |
| 0 | running |
| 2 | the running copy is older and must be restarted |
| 11 | busy: a question or a dialog is open in the running program |

```bat
start /wait FastMediaSorterCompanion.exe start --silent
```

Expect: exit 0; the tray icon is up and no window is shown.

<a id="share-manager-share-add"></a>

### share add

Propose a folder to share; the person answers the proposal in the manager before anything is shared.

```
share add <folder>
```

- Risk: `leaves-machine`; with nothing running: starts the program; answer: `accepted` within 5000 ms; idempotent: yes; since 1.0.
- Also reached by: `<folder>`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `<folder>` | path | yes | a folder by drive letter or UNC path, no .. segment and no wildcard |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `proposal` | string | queued, duplicate, already-shared or full: what became of the proposal |

| Exit | Meaning |
| --- | --- |
| 0 | proposed (queued, duplicate or already shared) |
| 10 | not an acceptable folder spelling |
| 11 | thirty-two proposals are already waiting, or a question is open |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'share add D:\Photos --result C:\temp\p.json' -Wait -PassThru
```

Expect: exit 0 and C:\temp\p.json holds the proposal verdict.

<a id="share-manager-resource-add"></a>

### resource add

Open the guided flow that adds a resource, with the camera QR scan when --camera is given.

```
resource add [--camera]
```

- Risk: `safe`; with nothing running: starts the program; answer: `done` within 10000 ms; idempotent: yes; since 1.0.
- Also reached by: `--add-resource`, `/add-resource`, `::fms-add-resource::`, `--add-resource-camera`, `/add-resource-camera`, `::fms-add-resource-camera::`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--camera` | bool | no | start with the camera QR scan; the shipped add-resource-camera spellings mean this |

| Exit | Meaning |
| --- | --- |
| 0 | the flow window is shown |
| 11 | busy: a question or a dialog is open in the running program |

```bat
start /wait FastMediaSorterCompanion.exe resource add --camera
```

Expect: exit 0 and the camera scan on screen.

<a id="share-manager-schedule-open"></a>

### schedule open

Open the Scheduled file operations window; a folder pre-fills the source of a new operation that the person still has to save.

```
schedule open [--folder <path>]
```

- Risk: `safe`; with nothing running: starts the program; answer: `done` within 10000 ms; idempotent: yes; since 1.0.
- Also reached by: `::fms-schedule::`, `::fms-schedule::<folder>`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--folder` | path | no | the pre-filled source folder |

| Exit | Meaning |
| --- | --- |
| 0 | the window is shown |
| 10 | not an acceptable folder spelling |
| 11 | busy: a question or a dialog is open in the running program |

```bat
start /wait FastMediaSorterCompanion.exe schedule open --folder D:\Photos
```

Expect: exit 0 and the window open with D:\Photos as the source.

<a id="share-manager-broadcast-open"></a>

### broadcast open

Open the live broadcast window with a path preselected.

```
broadcast open [--path <path>]
```

- Risk: `safe`; with nothing running: starts the program; answer: `done` within 10000 ms; idempotent: yes; since 1.0.
- Also reached by: `::fms-broadcast::<path>`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--path` | path | no | a drive or UNC path the window preselects |

| Exit | Meaning |
| --- | --- |
| 0 | the window is shown |
| 10 | not an acceptable path spelling |
| 11 | busy: a question or a dialog is open in the running program |

```bat
start /wait FastMediaSorterCompanion.exe broadcast open --path D:\Music
```

Expect: exit 0 and the broadcast window open.

<a id="share-manager-clipboard-send"></a>

### clipboard send

Send the clipboard to the exchange server and say how it went.

```
clipboard send [--json] [--result <path>]
```

- Risk: `leaves-machine`; with nothing running: answers by itself, starts no window; answer: `done` within 120000 ms; idempotent: no; since 1.0.
- Also reached by: `--clipboard-send`, `/clipboard-send`, `::fms-clipboard-send::`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `outcome` | string | a closed token for the result line: sent, empty, no-account, busy, too-large, no-room, no-answer, cancelled or failed |
| `bytes` | int | size sent |

| Exit | Meaning |
| --- | --- |
| 0 | sent |
| 1 | the operation failed or was cancelled |
| 2 | not proven: the running program may still be working |
| 11 | no exchange account, nothing to send, or another clipboard operation is under way |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |
| 13 | the clipboard is larger than the server accepts, or the server has no room |
| 15 | the server did not answer |

```powershell
(Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'clipboard send' -Wait -PassThru).ExitCode
```

Expect: 0 when the clipboard reached the server.

<a id="share-manager-clipboard-get"></a>

### clipboard get

Fetch the newest clipboard from the exchange server into the local clipboard and say how it went.

```
clipboard get [--json] [--result <path>]
```

- Risk: `writes-user-data`; with nothing running: answers by itself, starts no window; answer: `done` within 120000 ms; idempotent: yes; since 1.0.
- Also reached by: `--clipboard-get`, `/clipboard-get`, `::fms-clipboard-get::`.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `outcome` | string | a closed token for the result line: received, empty, no-account, busy, no-answer, cancelled or failed |
| `bytes` | int | size received |

| Exit | Meaning |
| --- | --- |
| 0 | received |
| 1 | the operation failed or was cancelled |
| 2 | not proven: the running program may still be working |
| 11 | no exchange account, nothing on the server, or another clipboard operation is under way |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |
| 15 | the server did not answer |

```powershell
(Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'clipboard get' -Wait -PassThru).ExitCode
```

Expect: 0 when the server clipboard is now the local one.

<a id="share-manager-share-list"></a>

### share list

List the folders the Share Manager shares and the state of each.

```
share list [--json] [--result <path>]
```

- Risk: `reads-user-data`; with nothing running: exit 11, not running; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `shares` | array | one object per shared folder: folder (the path), state (serving or stopped) and writable (true when the folder accepts uploads); no password, PIN, token or address of a peer |

| Exit | Meaning |
| --- | --- |
| 0 | answered |
| 1 | the sharing worker did not answer |
| 11 | not running |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'share list --result C:\temp\shares.json' -Wait; (Get-Content C:\temp\shares.json -Raw | ConvertFrom-Json).shares.Count
```

Expect: the number of shared folders.

<a id="share-manager-schedule-list"></a>

### schedule list

List the scheduled file operations with their next run and their last result; with no manager running the program reads the operations, answers and exits, starting no scheduler.

```
schedule list [--json] [--result <path>]
```

- Risk: `reads-user-data`; with nothing running: answers by itself, starts no window; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `operations` | array | one object per scheduled operation: id (a GUID), name, action (copy, move, delete, archive, fill-free-space, backup or unknown), enabled (bool), nextRun (ISO 8601 with the offset, or null while the operation is off or parked) and lastResult (none, ok, failed, refused, wall, cancelled, skipped or unknown); for backup operations, also retention (generations, keep-days or keep-days+keep-monthly), keep (int, when generations) or keepDays (int, when dated), keepMonthly (bool) and skipLinks (bool); no folder, no file name, no error text |

| Exit | Meaning |
| --- | --- |
| 0 | answered |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'schedule list --result C:\temp\ops.json' -Wait; Get-Content C:\temp\ops.json -Raw | ConvertFrom-Json
```

Expect: an object with an operations array.

<a id="share-manager-exchange-status"></a>

### exchange status

Say what the exchange client last knew about its server; no network call is made. With no manager running the program reads whether an account is set up, answers (state stopped, no request waiting) and exits.

```
exchange status [--json] [--result <path>]
```

- Risk: `safe`; with nothing running: answers by itself, starts no window; answer: `done` within 5000 ms; idempotent: yes; since 1.1.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `accountConfigured` | bool | whether an exchange account is set up on this PC |
| `state` | string | the last known state of the connection: not-configured, connecting, online, reconnecting, sign-in-again, pin-mismatch, update-needed, defect or stopped; no server address, user name or key |
| `pendingRequests` | int | how many requests of other programs to share a folder wait for the person's answer in the manager |

| Exit | Meaning |
| --- | --- |
| 0 | answered |

```powershell
Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'exchange status --result C:\temp\ex.json' -Wait; Get-Content C:\temp\ex.json -Raw | ConvertFrom-Json
```

Expect: an object with accountConfigured, state and pendingRequests.

<a id="share-manager-schedule-add"></a>

### schedule add

Create or update a scheduled backup operation without opening a window.

```
schedule add --action backup --source <folder> --destination <folder> --daily <HH:mm> [--keep <1-999> | --keep-days <1-999> [--keep-monthly]] [--skip-links] [--name <text>] [--json] [--result <path>]
```

- Risk: `writes-user-data`; with nothing running: starts the program; answer: `done` within 20000 ms; idempotent: yes; since 1.2.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--action` | enum | yes | operation action (backup) |
| `--source` | path | yes | absolute drive or UNC source folder |
| `--destination` | path | yes | absolute drive or UNC destination folder outside the source |
| `--daily` | string | yes | daily run time in 24-hour HH:mm form (local wall clock) |
| `--keep` | int | no | how many numbered generations to keep (1 to 999; cannot be combined with --keep-days) |
| `--keep-days` | int | no | keep dated backups for N days (1 to 999); cannot be combined with --keep |
| `--keep-monthly` | bool | no | keep the earliest backup of each calendar month indefinitely (requires --keep-days) |
| `--skip-links` | bool | no | skip junctions and symbolic links instead of refusing the backup |
| `--name` | string | no | display name for the operation |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `id` | string | operation id (GUID) |
| `created` | bool | true when a new operation was created; false when an existing matching operation was updated |
| `nextRun` | string | next scheduled run (ISO 8601 with offset) |

| Exit | Meaning |
| --- | --- |
| 0 | saved |
| 10 | invalid option, refused definition or FileDO without the needed backup support |
| 11 | busy: a question or a dialog is open in the running program |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'schedule add --action backup --source P:\MyProject --destination C:\GD\GIT --daily 13:00 --keep-days 7 --keep-monthly --result C:\temp\add.json' -Wait -PassThru
```

Expect: exit 0 and C:\temp\add.json holds the operation id.

<a id="share-manager-schedule-remove"></a>

### schedule remove

Delete a scheduled operation definition without touching any backup files.

```
schedule remove <id|name> [--json] [--result <path>]
```

- Risk: `writes-user-data`; with nothing running: starts the program; answer: `done` within 15000 ms; idempotent: no; since 1.2.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `<target>` | string | yes | operation id or exact operation name |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `id` | string | id of the removed operation |
| `removed` | bool | true when the definition was deleted |

| Exit | Meaning |
| --- | --- |
| 0 | removed |
| 10 | unknown option or ambiguous operation name |
| 11 | operation not found, or busy with an open window |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```bat
start /wait FastMediaSorterCompanion.exe schedule remove "MyProject"
```

Expect: exit 0 and the operation definition is deleted while backup files remain.

<a id="share-manager-schedule-run"></a>

### schedule run

Queue one scheduled operation or all enabled operations for immediate execution on the single lane; with --wait, wait for completion and exit with its outcome.

```
schedule run (<id|name> | --all) [--wait] [--json] [--result <path>]
```

- Risk: `writes-user-data`; with nothing running: starts the program; answer: `accepted` within 600000 ms; idempotent: no; since 1.2.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `<target>` | string | no | operation id or exact operation name |
| `--all` | bool | no | queue every enabled operation not already in the lane |
| `--wait` | bool | no | wait for the queued run(s) to finish and return the run outcome |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

Exactly one of `<target>`, `--all` is given.

| Returned | Kind | Meaning |
| --- | --- | --- |
| `queued` | int | number of operations queued |
| `completed` | bool | true when --wait waited until every queued run finished |
| `succeeded` | int | runs that ended ok (when --wait) |
| `failed` | int | runs that did not end ok (when --wait) |
| `outcome` | string | queued, or the run outcome token (ok, failed, refused, wall, cancelled, skipped) |

| Exit | Meaning |
| --- | --- |
| 0 | queued, or every waited run finished ok |
| 1 | the waited run failed |
| 2 | not proven: the wait bound passed while the run may still be working |
| 10 | missing target/--all, unknown option or ambiguous operation name |
| 11 | operation not found, paused, nothing to run, already running, busy or target unreachable |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |
| 13 | no space on the target disk |
| 15 | network or remote share failure |
| 16 | cancelled |

```powershell
(Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'schedule run --all --wait' -Wait -PassThru).ExitCode
```

Expect: 0 when every enabled operation finished without errors.

<a id="share-manager-schedule-history"></a>

### schedule history

Print the scheduled operations journal; with no manager running the program reads the journal, answers and exits.

```
schedule history [--operation <id|name>] [--last <n>] [--json] [--result <path>]
```

- Risk: `reads-user-data`; with nothing running: answers by itself, starts no window; answer: `done` within 5000 ms; idempotent: yes; since 1.2.

| Option | Kind | Required | Meaning |
| --- | --- | --- | --- |
| `--operation` | string | no | filter by operation id or exact name |
| `--last` | int | no | maximum number of newest entries to return (1 to 500) |
| `--json` | bool | no | print the response line instead of a sentence |
| `--result` | path | no | write the returned data to this file, whole |

| Returned | Kind | Meaning |
| --- | --- | --- |
| `entries` | array | newest-first journal objects: time (ISO 8601 with offset), operationId, operation, kind (run or note), trigger, outcome, files (int), failed (int), bytes (int64), durationMs (int64), generation (int or null), retiredGeneration (int or null), retiredCount (int or null), keptCount (int or null), keepDays (int or null), monthlyKept (int or null), skippedLinks (int or null) and detail |

| Exit | Meaning |
| --- | --- |
| 0 | answered |
| 1 | reading failed or response exceeded 64 KiB (narrow with --last or use --result) |
| 10 | invalid --last value (must be 1 to 500) |
| 12 | the setting that lets other programs ask this app to work with files, screen, clipboard or the network is off |

```powershell
Start-Process .\FastMediaSorterCompanion.exe -ArgumentList 'schedule history --last 20 --result C:\temp\hist.json' -Wait; Get-Content C:\temp\hist.json -Raw | ConvertFrom-Json
```

Expect: an object with an entries array.
