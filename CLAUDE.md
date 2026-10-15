# CLAUDE.md

> **Cross-project contracts live outside this repository.** The shared contract store is `P:\Contracts`, and **this line is the only place in this repository that names its location** - everywhere else cites a contract by its **id** and a rule or section number (`OCR-OVERLAY rule 8`, `FMSCFG rule 5`, `WORKER-IPC section 6`), never by path and never by drive letter, because whoever clones this repository has no `P:`. Its `README.md` is the index; `_meta/RULES.md` is the law, `_meta/VERSIONING.md` the compatibility rules, `_meta/REGISTRY.md` the live map of who implements what at which version. Nothing here ever keeps a **copy** of a contract: [docs/contracts/](docs/contracts/README.md) holds one short pointer per contract this product touches, and a disagreement between this repository and a contract is an amendment to write in the catalog or a dated exception to record in its registry - never a local edit.

## Project Overview

**Fast Media Sorter & Sharing for Windows** is a Windows Forms application for viewing, sorting and sharing image and video files, shipped as **two exes side by side in one folder** built from **one source tree**: the **.NET 10 x64 mainline** (`FastMediaSorter_LITE.exe`) and a **net48 x86 fallback** (`FastMediaSorter_x86.exe`) for Windows 7/8.1 and 32-bit Windows. Read "Two builds, one source tree" below before touching anything build- or media-related.


## Layout of the agent instructions (2026-10-15)

This file is the **core every session pays for** (every request of every agent carries it) - keep it small. Area detail lives in `.claude/rules/*.md`. A rule file **loads automatically** the first time a session reads or edits a file matching its `paths:`, and is otherwise absent from context; **nothing was deleted, the text was moved verbatim**. Read the row yourself when you have not touched a file of the area yet (planning, writing a brief for a sub-agent, Codex, which does not auto-load `.claude/rules`).

| Rule file | Auto-loads on | Read before |
| --- | --- | --- |
| [naming-and-positioning](.claude/rules/naming-and-positioning.md) | site pages, `publishing/`, `docs/`, `ProductNames.vb` | writing a visible product name, a listing or a README line |
| [canon-overlay](.claude/rules/canon-overlay.md) | `publishing/`, `tools/`, `.github/`, site, `build.ps1` | release channels, site layout, the server/elevation gate |
| [build-release](.claude/rules/build-release.md) | `build.ps1`, `tools/`, `publishing/`, `*.vbproj` | prerequisites, version stamping, release CI, installer, winget, MSIX, dependencies |
| [viewer-mainform](.claude/rules/viewer-mainform.md) | `src/Main_Form*.vb`, `Application_Events.vb`, `FileManager.vb`, zoom, bootstrap | anything in the viewer window: partials, single instance, perspective bars, file I/O |
| [viewer-chrome-dialogs](.claude/rules/viewer-chrome-dialogs.md) | `src/Chrome/`, `DialogLayer*`, `Ask*`, `Main_Form.Chrome.vb` | opening any window, status/toast news, the floating chrome |
| [imaging-icons-archives](.claude/rules/imaging-icons-archives.md) | `src/Imaging/`, `src/Icons/`, `src/Archives/`, editor | decoding/encoding images, glyphs, archive browsing |
| [resources-secure](.claude/rules/resources-secure.md) | `src/Resources/`, `src/Secure/` | SFTP/SMB resources, remote slots, `.fd-sec` containers |
| [settings-window](.claude/rules/settings-window.md) | `src/Table_Form*.vb`, `src/Support/` | the Settings window, send-logs allow-list |
| [ocr-translate](.claude/rules/ocr-translate.md) | `src/Ocr/`, `src/Translate/`, `Main_Form.Ocr*` | OCR pipeline, overlay fit, translators, cache keys |
| [share-companion](.claude/rules/share-companion.md) | `src/FastMediaSorterCompanion/`, `exchange-server/`, `tests/Companion.Tests/` | Share Manager, Go worker, exchange server, editions, scheduled operations |
| [localization](.claude/rules/localization.md) | `src/Localization/`, `*.Localization.vb` | any new or changed UI string, RTL, script fonts |

## Working rules measured from the agent transcripts (2026-10-15)

Source and numbers: [tools/agent-cost/README.md](tools/agent-cost/README.md) (315 sessions, 22.8k requests, 2026-09-19 to 2026-10-15). Every line is a failure that recurred; the number is how often. Hooks refuse some of these at the tool call - the refusal itself costs a turn, so follow the rule and skip the refusal.

**Shell**
- The **Bash tool has no `dotnet`, `go`, `gofmt` or `python` on PATH** (exit 127, 39 times, `python` 27). Run builds, tests, Go and Python through the **PowerShell tool**; use Bash for git and small text filters.
- **Never type a PowerShell cmdlet or a `.ps1` as a Bash command** (hook refusals, 78 times). From Bash: `pwsh -NoProfile -File x.ps1`. `find` without `-maxdepth` is refused too - use Glob or Grep.
- **No `sleep` / `Start-Sleep` poll loops** (refused 33 times). Start a long command with `run_in_background` and you are notified when it ends; to wait on a condition use Monitor.
- **No long inline scripts with nested quotes in one command** (`unexpected EOF`, 51 times). Write the script into the scratchpad with Write, then run the file. The same goes for a one-off Perl/Python edit script.
- Test projects are `.vbproj`, never `.csproj` (`tests\Lite.Tests\Lite.Tests.vbproj`, `tests\Companion.Tests\Companion.Tests.vbproj`; 13 `MSB1009` otherwise).
- `Remove-Item` with a path built from a pattern is stopped by a harness guard (20 times): delete exact paths from a list, never a computed glob.

**Reading**
- A file over 500 lines is windowed by a hook. **Grep the symbol first, then Read an `offset`/`limit` window**; the hot files (`Table_Form.ModernLayout.vb` 2.6k lines, `Main_Form.MediaLoading.vb`, `Main_Form.FileOperations.vb`, `Main_Form.Chrome.vb`, `Main_Form.Lifecycle.vb`) were re-read 40-70 times each, whole, with nothing changed between the reads.
- **Do not Read a ledger whole**: `docs/specifications/README.md` is 90 KB and was read ~25 times. `pwsh -NoProfile -File tools\Get-Spec.ps1 -Next | -Id 231 | -Find text | -Open` answers the actual questions in a few lines.
- A file already in context (CLAUDE.md, a rule file you were given) is **never** Read again.
- Keep raw output out of the conversation: write a log to the scratchpad and Grep it. A 40 KB result is carried by every later turn of the session.

**Delegating**
- A sub-agent **already has this file**. Never write "read CLAUDE.md first" in a brief - it makes the agent Read a 150 KB file it cannot even load in one go (9 refusals). Name the rule file of the area instead.
- The built-in general-purpose agent inherits the session model (51 of 143 sub-agent runs were Opus). Use the project agents in `.claude/agents/` (`implementer`, `auditor`: Sonnet, turn-capped) or pass `model` explicitly.
- **One slice per agent, about 80 turns.** The costliest runs were 150-350 turns that grew to 400-870k tokens of context; a turn's price is the whole context, so a long run pays quadratically. When a slice is not finished, start a **fresh** agent with a written handoff instead of continuing the full one.
- Sessions: auto-compaction fires at 300k tokens (`CLAUDE_CODE_AUTO_COMPACT_WINDOW` in `.claude/settings.json`). Run `/compact` at a ticket boundary, `/clear` when switching tickets.

**Checks** - see "Inner loop" under *Testing & Validation*.

## Naming, canon and localization - the short form

Full text of each is in its rule file (table above); these lines are the part that is wrong too expensively to learn from a hook.

- **Naming** ([PRODUCT_NAMING.md](docs/guides/PRODUCT_NAMING.md), constants in `src/ProductNames.vb`, gate `tools/Test-ProductNaming.ps1`): no `Lite` in visible text; `&` is part of the name and is escaped per medium; the name is not translated. **Frozen anchors, only the visible name moves**: exe `FastMediaSorter_LITE.exe`, mutex `FastMediaSorterSingleInstanceMutex`, registry `SZA\FastMediaSorter`, ProgIDs `FastMediaSorter.*`, installer `AppId` / `UninstallDisplayName` / `DefaultDirName` / `OutputBaseFilename`, winget `PackageIdentifier` / `PackageName` / `Moniker` (no `AppsAndFeaturesEntries`), Store `Identity` and manifest `DisplayName`.
- **Positioning** ([POSITIONING.md](docs/guides/POSITIONING.md), `tools/site-facts.json`): cite it, never restate it; `tools/Test-SitePositioning.ps1` holds the language count and the Windows floor - change them in `site-facts.json` in the same edit.
- **Canon**: the universal rules are the `sza` plugin (`github.com/SerZhyAle/sza-unified-rules`, start at `rules/INVARIANTS.md`; stamp `.sza-canon.json`). This repo is Overlay A and **references** the canon, it never mirrors it. Local deltas that bite: one source tree / two toolchains (below), site pages use the **full screen width** (no `max-width` caps, `tools/Test-SiteQuality.ps1` rule 8), server/elevation features ship **OFF** behind `ServerFeatures.IsEnabled()`, no hooks are registered in this repo (the plugin carries them).
- **Localization** (13 languages, `.claude/rules/localization.md`): the key **is** the Russian source string, `Localization.T("Удалить")`; anything carrying a value uses `TF("... {0} ...", v)`, never concatenation; **never a smart double quote** (U+201C/U+201D) in a literal - use `«..»` or `「..」`; RTL is `RightToLeft = Yes` only, never `RightToLeftLayout`; a new string needs all 13 columns and `LocalizationParityTests` / `LocalizationCoverageTests` green; the x86 fallback ships RU+EN only.

## Two builds, one source tree - read before editing a seam

The viewer is **one app** shipped as **two exes in one folder**. Design: `SPECIFICATION_DOTNET10_MODERN_BUILD.md` (local specification); journal: [PROGRESS_DOTNET10_MODERN_BUILD.md](docs/guides/PROGRESS_DOTNET10_MODERN_BUILD.md).

| | **Mainline** | **Fallback** |
|---|---|---|
| Exe | `FastMediaSorter_LITE.exe` (frozen anchor) | `FastMediaSorter_x86.exe` (new name) |
| Project | [src/Modern/FastMediaSorter.Modern.vbproj](src/Modern/FastMediaSorter.Modern.vbproj) (SDK-style) | [src/FastMediaSorter.vbproj](src/FastMediaSorter.vbproj) (old-style) |
| Target | `net10.0-windows`, x64, self-contained single-file | net48, genuinely `PlatformTarget=x86` (32-bit process) |
| Built by | `dotnet publish` | `msbuild` |
| Runs on | Windows 10 1607+ / 11 / Server 2016+ | Windows 7/8.1 and 32-bit Windows |
| Video | **LibVLC only** | IE WebBrowser (H.264) + LibVLC fallback |
| Images | ImageSharp 3 | WIC + ImageSharp 2 fallback |
| Managed deps | NuGet `PackageReference`; publish lays them down | `packages.config`; every managed dep embedded as a `FmsPayloadmanaged/*.dll` resource, resolved by `RuntimeBootstrap` `AssemblyResolve` (ILMerge is imported but never runs) |

**Rules - do not "fix" these away:**
- **Clarify ambiguity first (owner decision):** When a request does not clearly identify the target screen, runtime, or expected outcome, ask a concise clarifying question before changing code; do not infer the target from similarly named legacy/shared UI.
- **Maintenance policy (owner decision):** .NET 10 x64 is the active development target. Do not add features, UI refinements, or intentional behavior changes to the net48 x86 fallback; touch it only to fix an actual bug that is found. When a shared-source change is mainline-only, preserve net48 behavior with a `#If NETFRAMEWORK` seam.
- **Shared Windows UI (owner decision):** adopt `WINDOWS-UI` 0.1, `APP-SETTINGS` 0.2 section 8 and `APP-STYLE` 0.12 section 10 from the shared catalog. The modern viewer/settings are the reference for navigation, independent groups/context, side-by-side editors, conventional left checkboxes, readable native numeric controls, complete live theme coverage, DPI and floating viewer chrome; secondary windows follow the same control conventions. `ICON-RENDER` 0.16 section 11 permits navigation identity inks. See [APP-UX pointer](docs/contracts/APP-UX.md); normative rules stay in the catalog. Other products need their own implementation and conformance audit; the net48 fallback stays under the maintenance policy above.
- **One app, not two**: one folder, one mutex `FastMediaSorterSingleInstanceMutex`, one registry hive `SZA\FastMediaSorter`. Launching either exe while the other runs forwards the file to the running window - so [Application_Events.vb](src/Application_Events.vb) searches **both** process names (`Viewer_Process_Names`): `GetProcessesByName` matches on name only, so searching just our own name finds nothing when the sibling is the one running and cancels the launch **silently**, with no window and no file opened.
- **Both projects compile the SAME sources**, but a NEW file has to be added to the net48 list BY HAND: the modern vbproj links `..\**\*.vb` (picked up automatically), while the old-style [src/FastMediaSorter.vbproj](src/FastMediaSorter.vbproj) carries an explicit `<Compile Include="..">` list. A `.vb` dropped under `src/` is seen only by the mainline - the x86 build then fails on the first thing that references it (`BC30451: '..' is not declared`), which is the *good* case; a file nothing references yet is silently not in that exe. Add the `<Compile Include>` entry (with `<DependentUpon>Main_Form.vb</DependentUpon>` for a `Main_Form.*.vb` partial) in the same commit. Divergences live behind `#If NETFRAMEWORK` / `#If Not NETFRAMEWORK` seams, never in per-project file lists, and a change to shared code must make sense on both runtimes.
- **`NETFRAMEWORK` is defined BY HAND**: old-style projects do **not** define it, so [src/FastMediaSorter.vbproj](src/FastMediaSorter.vbproj) sets `<DefineConstants>NETFRAMEWORK=True</DefineConstants>` in **both** configurations. Delete or typo it and every seam silently compiles the wrong branch - no error, just a modern-only code path inside the net48 exe.
- **Never delete a seam to "simplify"** - each exists because the two runtimes genuinely differ. There is no seam list here (a hand-kept one drifted four-fifths behind the tree); the tree answers in one line: `grep -rlE '#If (Not )?NETFRAMEWORK' src/ --include=*.vb`. Whole-file seams (the file is one runtime's only) are worth knowing by shape: [src/Imaging/](src/Imaging) decoders and encoders, `Main_Form.Zoom.vb` + `ZoomMath.vb`, `Main_Form.PerspectiveHalo.vb`, both `*.ShareLauncher.vb`.
- **Shared adjacent libraries, resolved per process bitness** out of the same tree: `libvlc\win-x64` vs `libvlc\win-x86`, `x64\tesseract50.dll` vs `x86\` (`CurrentArchFolder()`/`CurrentVlcArchFolder()` in [OptionalRuntimeManager.vb](src/OptionalRuntimeManager.vb)). This needed no code change - don't "unify" it.
- **The x86 build keeps OCR and translation; only Share is cut** (owner decision). Share cannot work there: the Companion it wakes is `net10.0-windows` **x64**, so it does not start on Windows 7/8.1 or 32-bit Windows - exactly the machines the x86 exe exists for. `Main_Form.ShareLauncher.vb` and `Table_Form.ShareLauncher.vb` are whole-file `#If Not NETFRAMEWORK`, and the installer's `share` component carries `MinVersion: 10.0.14393`. **There is no `FEATURE_FULL` constant and no stripped build configuration** (epic O-7): there is exactly ONE net48 build and it IS the x86 fallback, so `NETFRAMEWORK` already names it - a second constant would only add a way for the two to disagree. The epic's section 1.1 still describes a leaner legacy without OCR/translate; that predates the two-exe directive and is superseded.

**Modern-only compatibility pins** (each deliberate; removing one is a visible regression):
- `ApplyApplicationDefaults` in [Application_Events.vb](src/Application_Events.vb) selects `HighDpiMode.PerMonitorV2` + `Microsoft Sans Serif` 8.25 for the modern viewer. The **shipped net48 exe never embedded `app.manifest`**, so DPI-unaware IS the reference behavior; .NET would otherwise default to `SystemAware` + Segoe UI 9pt and shift the pixel-tuned Designer layout.
- The modern vbproj sets `System.Globalization.UseNls=true` so `abc`/`xyz` file sorting matches net48 (.NET 5+ uses ICU, net48 uses NLS).
- Tesseract's loader needs `InteropDotNet.LibraryLoader.Instance.CustomSearchPath` under single-file publish (`Assembly.Location` is `""` there).
- A portable JSON facade in `TranslateHttp` ([src/Translate/OllamaTranslator.vb](src/Translate/OllamaTranslator.vb)) keeps `JavaScriptSerializer` on net48 and uses `System.Text.Json` on modern, shaped to the same untyped graph so the **OCR disk cache stays compatible** across both exes.

## Build & Run

### Repository layout (where things live)
Release-channel artifacts live under one umbrella, **`publishing/`** (`installer/`, `winget/`, `msix/`, `store/`); build/release **automation** that is not channel-specific stays in `tools/`. A script inside `publishing/<channel>/` derives the repo root by going **two** levels up, and a `.iss`/manifest path to repo assets is `..\..\assets\..` - re-check both if a channel folder moves. Portable convention: [docs/guides/REPOSITORY_LAYOUT.md](docs/guides/REPOSITORY_LAYOUT.md); documentation/SEO/versioning/site strategy: [docs/guides/DOCUMENTATION_CONCEPT.md](docs/guides/DOCUMENTATION_CONCEPT.md); docs index: [docs/README.md](docs/README.md).

**Project identity** (both viewer projects): `RootNamespace = fmsl`, startup object `fmsl.My.MyApplication`; `AssemblyName` is `FastMediaSorter_LITE` (modern) vs `FastMediaSorter_x86` (net48) - see "Two builds, one source tree" above.

### Where the rest of the build detail went
Prerequisites (Go, .NET, VS), version stamping, the release pipeline, the standalone installer, winget and MSIX rules: [.claude/rules/build-release.md](.claude/rules/build-release.md) (auto-loads on `build.ps1`, `tools/`, `publishing/`, `*.vbproj`). Two things to hold without opening it: **`msbuild` alone never produces the mainline exe** (only `dotnet publish` does), and **"сборка" never creates a tag**.

### "Сборка" vs "Релиз" - two distinct flows (see [BUILD_AND_RELEASE.md](docs/guides/BUILD_AND_RELEASE.md))

- **Сборка / build** = LOCAL only: `.\build.ps1` (all three programs; deploys **both viewer exes side by side** to the user's work folders, then packages the offline `dist\..-setup.exe` via `tools\Build-Installer.ps1 -SkipBuild`, skippable with `-SkipInstaller`). Test by hand, then commit/push. **Never creates or pushes a `v*` tag** - packaging an installer locally is still a build. Pushing to a branch is free: GitHub Actions does NOT run on branch pushes.
- **Релиз / release** = GitHub build + winget + Microsoft Store: `.\tools\Release.ps1 -Push` (runs the free local preflight first and pushes the `vYY.M.D.HHmm` tag only on a bare `Release-Preflight: PASS`; a step skipped with `-SkipTests`/`-SkipLocalCheck` makes it COULD NOT VERIFY, so those switches serve a quicker dry run and never ship a tag). The release `workflow` ([.github/workflows/release.yml](.github/workflows/release.yml)) is triggered **ONLY by pushing a `v*` tag** - that tag push is the single billable operation.
- <!-- canon-ok: INVARIANTS 4 says never trigger a release unasked; this line is the repo-specific mapping of that to the two Russian trigger words the owner actually types, which the canon cannot know. --> **When the user asks for a "сборка"/"build"/"собери", run the LOCAL flow only and do NOT create or push a tag.** A tag (= paid GitHub run) requires an explicit "релиз"/"release" instruction. `tools/Release.ps1` defaults to a dry-run (no push) for safety; it pushes only with `-Push`.

### Build Commands

Full local build - **all three programs**, deployed into the distribution shape, then packaged into `dist\FastMediaSorter-<version>-windows-x64-setup.exe` (needs Inno Setup 6):
```powershell
.\build.ps1
```
Flags: `-SkipModern` (x86 viewer + Companion only), `-SkipCompanion`, `-SkipInstaller` (stop after the deploy - packaging is the slow step), `-InstallerIncludeBest` / `-InstallerSkipOcr` (default is the lighter `fast` OCR models), `-NoClean`. The packaging step reuses `bin\Release` (`-SkipBuild`) and reads the version as **text** from the just-written `src\My Project\VersionInfo.vb`, since the exe's numeric `FileVersion` would turn `26.7.30.0108` into `26.7.30.108` and misname the package.

Partial builds (syntax checks only - neither shape is shippable on its own):
```powershell
# x86 viewer + Companion; does NOT produce the mainline exe
msbuild FastMediaSorter.sln /p:Configuration=Release /p:Platform="Any CPU"
# the mainline x64 viewer on its own
dotnet publish src\Modern\FastMediaSorter.Modern.vbproj -c Release -r win-x64
```

**`bin\Release` IS the distribution shape** - both viewer exes (`FastMediaSorter_LITE.exe` mainline, `FastMediaSorter_x86.exe` fallback) plus the shared `libvlc\`, `x64\`, `x86\`, `flags\` and `companion\` trees.

## Code Style & Constraints

**VB.NET Strict Mode** - every module enforces `Option Explicit On`, `Option Strict On`, `Option Compare Binary` (case-sensitive) and `Option Infer On`.

**File Organization**:
- `.Designer.vb` files are auto-generated (don't edit manually).
- `Main_Form` is one logical class spread across many `Partial Class Main_Form` files (`Main_Form.*.vb`). Fields/methods are shared across all of them - put a change in the file matching its concern, and watch for name collisions across partials.
- Build treats a fixed set of VB warnings as errors (`WarningsAsErrors`, kept identical in both viewer projects; the authoritative list is in [the modern project](src/Modern/FastMediaSorter.Modern.vbproj) and [the fallback project](src/FastMediaSorter.vbproj)), so these will fail the build, not just warn.

**Keyboard Shortcuts** - most features are driven by hotkeys, not menus; the folder shortcuts are the hardcoded `Hardkeys_to_move_mediafile` array in `Common_Module`, and the full mapping is in the `Main_Form` keyboard handlers.
- **Video track keys are the modern build only** (`Main_Form.VideoTracks.vb`): **A** cycles the audio track, **V** the subtitles, and a "Tracks" button appears in the toolbar **only when the file offers a choice** (`SetToolbarItemHidden`). Modern-only because on net48 a video goes to the IE WebBrowser first, so the control would silently do nothing for an MP4. VLC's own audio-track key is `B`, already "previous file" here - hence `A`. The choice is remembered **by ISO language code**, not by id (per-file) or name (VLC returns composites like `Russian - [Russian]` that only match within one release); `player.Media.Tracks` keeps a clean `Language` field even after the `Media` object is disposed. `AudioTrackDescription`/`SpuDescription` always include a `Disable` entry with **id -1** - count real tracks, not `Length`.
- **Zoom keys are the modern build only** (`Main_Form.Zoom.vb` + `ZoomMath.vb`, whole-file `#If Not NETFRAMEWORK`): the **grey** NumPad block - `+`/`-` zoom at the cursor, `/` fits, `*` is 100 %. Only the grey block, because every `Ctrl` combo is already taken and NumPad `0..9` are the sort hotkeys. The first-run help gets its mainline-only lines from `ModernHelpLines()` in `Main_Form.Localization.vb`, which returns `""` on net48 so the x86 viewer never advertises keys it does not have.
- **Move vs copy is chosen at the moment of the action, mainline only** (`SPECIFICATION_COPY_ACTIONS_REWORK.md`): `0..9` move, **`Shift` + a TOP-ROW digit copies** into the same slot (not numpad - with NumLock on, Windows drops NumLock while Shift is held, so `Shift+NumPad1` arrives as `Keys.End`). Every single-file entry point (keys, the recipients overlay's copy zone, both media menus, `Shift`+double-click in the settings grid) passes an explicit `RecipientActionKind` to the ONE implementation `Main_Form.ExecuteRecipientAction`. The **thumbnail panel is the exception**: it acts on a whole selection, so it keeps its own multi-file loop (`PoMove_for_Panel` in [Image_Panel_Form.vb](src/Image_Panel_Form.vb)) and takes move-or-copy as a parameter. **`PoMove(slot)` is a thin wrapper and, on modern, no file operation consults `Is_Copying_not_Moving`**: on net48 two places still honour the global copy mode (`PoMove` and the panel's `#If NETFRAMEWORK` branch), and on modern `PoMove` always means Move. Do not reintroduce a `Is_Copying_not_Moving` read anywhere else - in the mainline UI nothing can switch it. Whether a copy advances to the next file is `AdvanceAfterCopy` (default on).

**UI Threading**: image/video loading runs on background threads - marshal UI updates with `Invoke()`/`BeginInvoke()`; LibVLC playback is asynchronous.

**File Locking**: `LoadImageWithStream()` keeps its MemoryStream open so Windows releases the file handle, and it is disposed explicitly when the image is no longer needed - critical for fast move/delete.

## Testing & Validation

### Inner loop - use this, not raw `dotnet test ... | Select-Object -Last N`

```powershell
pwsh -NoProfile -File .\tools\Invoke-Tests.ps1 -Build modern                      # compile check of the mainline (net10 x64), errors deduplicated
pwsh -NoProfile -File .\tools\Invoke-Tests.ps1 -Suite lite -Filter OcrOverlayFit  # one class / test-name fragment, net10 only
pwsh -NoProfile -File .\tools\Invoke-Tests.ps1 -Suite companion -Filter Scheduled
pwsh -NoProfile -File .\tools\Invoke-Tests.ps1 -Suite lite -Tfm both              # shared code or a #If NETFRAMEWORK seam: both shipped runtimes
```

It prints a **bounded digest** (each compile error once, each failing test with its first message line, the totals) and keeps the full log in `%TEMP%\fms-tests\` - **grep that log, do not re-run to see more**. Exit 0 pass, 1 failed, 2 could not verify.
- Order: compile check, then the filtered suite for what you touched, then `Run-AllTests.ps1` **once** before you say done. Re-running the whole battery after each edit was most of the ~400 minutes of build/test wall-clock measured, and 40% of the runs were red (100 of them compile errors that a compile check finds in a third of the time).
- `Lite.Tests` is multi-targeted (net48 + net10); iterate on net10 and finish with `-Tfm both` when the change reaches shared code.
- Kill a stray viewer before a build that writes `bin\Release` (a running exe locks it); never rely on a green run while the exe is still running.

**There IS an automated suite - run it before claiming anything works:**

```powershell
pwsh -NoProfile -File .\tools\Run-AllTests.ps1            # unit suites
pwsh -NoProfile -File .\tools\Run-AllTests.ps1 -Integration -SkipGo
```

[tools/Run-AllTests.ps1](tools/Run-AllTests.ps1) drives [tests/Lite.Tests/](tests/Lite.Tests) (the viewer, **multi-targeted so the same linked sources are tested under BOTH shipped runtimes**, net48 and net10), [tests/Companion.Tests/](tests/Companion.Tests) (the Share Manager, net10), the Go worker suite in the sibling `fms_companion` repository (`-WorkerRepo` or `FMS_WORKER_REPO` to point elsewhere) and `tools/Test-CheckPlacement.ps1`, and ends in **one verdict line** (contract `CHECK-VERDICT`, via [tools/lib/Verdict.ps1](tools/lib/Verdict.ps1)): `Run-AllTests: PASS (4 passed)`. **Exit 0 = pass, 1 = a suite ran and failed, 2 = could not verify** (a missing project, `dotnet`, `go.exe`, worker repository or live worker - "could not check" is never folded into a pass, nor into a defect). `-SkipGo` reports that suite as **SKIPPED**, never as passed. The live suites are opt-in, each behind its own switch: `-Integration` runs [tests/Integration/WorkerRoundTrip.ps1](tests/Integration/WorkerRoundTrip.ps1) (needs a running worker - without one it is COULD NOT VERIFY); `-OcrScenes` runs [tests/Integration/OcrOverlayScenes.ps1](tests/Integration/OcrOverlayScenes.ps1) (drives the built `bin\Release` viewer on screen and borrows the OCR settings - owner decision: never part of a default or `-Integration` run); `-DynAudit` runs [tools/dynaudit/DynAudit.ps1](tools/dynaudit/DynAudit.ps1) (specification 209; isolated profile, backed up and restored in `finally`; hand-run, never a default run). Which runner owns which check is declared once in [tools/check-placement.jsonl](tools/check-placement.jsonl) (contract `CHECK-PLACEMENT`) and held in both directions by `Test-CheckPlacement.ps1`: a check wired into a runner without its record, or recorded without being run, fails the next test run. A new check that reports a verdict needs its record in the same commit.

The suite does not cover the UI, so these still matter:
1. **Build**: `.\build.ps1` - confirms both viewers compile (a shared-code change can break the *other* exe; `msbuild` alone never proves the mainline builds).
2. **Run**: open the built `.exe` and exercise load/navigate/slideshow/video. **Exercise a media change in both exes** when it touches a seam (video, image decode, drag-drop, file ops). The Share Manager (launched from the viewer's folder right-click or the Settings "Files and system" button) needs `FastMediaSorterCompanion.exe` built and `payload/companion/fms-share-worker.exe` present (committed via a `.gitignore` negation) to do more than show "unavailable".
3. **Code analysis**: Visual Studio's analyzer in the Debug config.

## Git & PR Workflow

- **Never use git to undo, restore, hide or replace local work** - no `reset`, `restore`, `checkout`, `clean`, `stash`, `switch`, `revert`, `rebase`, and no overwriting a file with `git show` output. The files on disk are the source of truth: several agents work in this tree at once and most of their work is uncommitted (one `git reset --hard` once wiped two days of it). Take an edit back by editing it back; keep a copy in the scratchpad first. The full rule is "Preserve Local Work; Git Is for GitHub Only" in [AGENTS.md](AGENTS.md). `.claude/settings.json` denies those commands for Claude Code, and the owner's codex rules forbid them; a deny list cannot see `git -C <path> ..`, scripts or IDE buttons, so the rule still has to be kept by hand. Snapshots of the whole tree are taken every 15 minutes into `refs/backup/autosnap/*` - look there first if work vanishes.
- VB.NET agent ([.github/agents/vb-net-profy.agent.md](.github/agents/vb-net-profy.agent.md)) available for focused VB.NET tasks
- Prefer small, targeted commits over broad rewrites
- Preserve existing UI behavior unless explicitly requested

## Common Development Tasks

- **A new file operation** goes in [FileManager.vb](src/FileManager.vb) and is called from the `Main_Form` handler; test with real files in a temp folder.
- **Tuning the perspective background**: the constants (`DeviationPercent`, `SampleStep`) are in `src/LetterboxFillMath.vb` and change only after the catalog amendment (the contract is the owner's own: version bump, log row, regenerated vectors); then re-take the vectors into `tests/Lite.Tests/TestData/letterbox-vectors/`.
- **A new media format**: add the extension to `Image_File_Extensions` or `video_File_Extensions` in Main_Form.vb and test playback in **both** exes. A new *image* format belongs behind the `IImageDecoder` seam ([src/Imaging/](src/Imaging)), not in a new `FileManager` branch. Dispose `Image`/`Bitmap` and `MemoryStream`.

## Environment Notes

Developed primarily on Windows 11; the support matrix is the table under "Two builds, one source tree". Settings/state live in the registry hive `SZA\FastMediaSorter`, shared by both exes.
