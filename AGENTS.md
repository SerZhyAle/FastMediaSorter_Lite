# Repository Guidelines

## Canon pointer

The universal agent rules live in the **`sza` canon** - the `sza` Claude Code plugin from
`github.com/SerZhyAle/sza-unified-rules` (start at `rules/INVARIANTS.md`; if your agent cannot load
the plugin, read `rules/INVARIANTS.md` from that URL first). This repo is **Overlay A - Windows
desktop**, consumption model **reference**: this file and `CLAUDE.md` keep only repo deltas and defer
to the canon. `CLAUDE.md` is the authoritative agent contract, carries the full canon pointer, and
where both files exist the stricter wins. Adoption record: `contrib/fastmediasorter_lite.md` in the
canon repo; stamp: `.sza-canon.json`.

## Where the detail lives

`CLAUDE.md` is the small core every session loads (always-on rules, a layout table). Area detail is in `.claude/rules/*.md`, one file per area; Claude Code loads a file when it first touches a matching path, **Codex does not auto-load them**, so read the row of the layout table in `CLAUDE.md` for the area you are about to change before editing. Inner loop: `tools/Invoke-Tests.ps1` (bounded digest, kept log), `tools/Get-Spec.ps1` (ledger answers). Cost baseline and the measurement scripts: `tools/agent-cost/README.md`.

## Product naming

The product is **Fast Media Sorter & Sharing for Windows**. Every name, its spelling rules and the list of
frozen technical anchors are in `docs/guides/PRODUCT_NAMING.md` - cite it, never copy it. In code use the
constants of `src/ProductNames.vb`; the gate is `tools/Test-ProductNaming.ps1` (allow-list
`tools/product-naming-allow.jsonl`). No "Lite" in visible text; the exe names, registry hive, mutexes, installer
and winget identifiers, `tests/Lite.Tests` and the repository slug keep their old spelling on purpose.

## Preserve Local Work; Git Is for GitHub Only

- The current files on disk are the source of truth, including all uncommitted work. Never assume that HEAD, a branch, a tag, or a remote contains the correct or complete version.
- Do not use Git during ordinary development, debugging, builds, tests, or recovery. Use filesystem tools to inspect files and make targeted edits.
- Git is allowed only when the user explicitly requests a GitHub operation, such as publishing changes or preparing a commit or PR. A request to fix code, repair a script, or make a build pass is not permission to use Git.
- Never use Git to roll back, restore, overwrite, clean, hide, or replace local work. This includes reset, restore, checkout, clean, stash, revert, switch, rebase, and replacing files with content from git show, archives, another checkout, or a remote. Do not invoke these indirectly through scripts, aliases, IDE actions, or other tools.
- Do not create worktrees or isolated checkouts, change branches, or pull/merge remote content as part of ordinary development. Preserve the user's working directory and ongoing work.
- If a build or test fails, diagnose and fix the actual cause in the current files. Never discard features, remove existing changes, or substitute an older version merely to obtain a passing build. If the cause cannot be fixed, report the concrete blocker and preserve the work.
- These restrictions apply to every agent and tool. Any exception requires a direct user instruction for the specific operation and affected files; generic instructions to finish, fix, build, or publish do not authorize destructive recovery.

## Project Structure & Module Organization

`src/` contains the Windows Forms applications and shared VB.NET code. `FastMediaSorter.vbproj` builds the legacy x86 .NET Framework 4.8 viewer; `Modern/FastMediaSorter.Modern.vbproj` builds the modern .NET 10 viewer; `FastMediaSorterCompanion/` is the .NET 10 Share Manager. Shared viewer sources use `#If NETFRAMEWORK` seams, so preserve both branches when changing shared behavior.

Tests live in `tests/Lite.Tests` (multi-targeted viewer logic), `tests/Companion.Tests` (Share Manager), and `tests/Integration` (optional live worker checks). Keep shipped art in `assets/`, release-channel material in `publishing/`, automation in `tools/`, and internal documentation in `docs/`. Root HTML and `assets/` back the GitHub Pages site; update localized pages in `lang/<locale>/` when altering published site copy.

## Build, Test, and Development Commands

Between releases, run only measured checks of at most 5 seconds and a direct check of the change
(canon `TESTING_AND_QA` section 2). The measured list is in `.claude/rules/build-release.md`.
For engineering placement changes, run from the repository root:

```powershell
pwsh -NoProfile -File .\tools\Invoke-Tests.ps1 -Fast -Area engineering
```

Every check ends in one verdict line and exits 0 (pass), 1 (defect) or 2 (could not verify - never a pass); `-SkipGo` is reported as skipped. Which runner owns which check is `tools/check-placement.jsonl`.

Other areas have no measured fast selector and report COULD NOT VERIFY. At a release, `tools/Release.ps1`
runs the full suite and package build; the owner may explicitly request a particular slower run. Record
each deferred rung as a `RELEASE` audit item naming its command, and report "not built, not tested until
the release". `build.ps1` packages and deploys unless skipped; read its parameters before an explicit build.

## Coding Style & Naming Conventions

Write VB.NET with `Option Explicit On`, `Option Strict On`, `Option Infer On`, and `Option Compare Binary`, matching project settings. Follow nearby formatting (four-space indentation) and existing PascalCase types/members. Keep WinForms partial features in descriptive files such as `Main_Form.FileOperations.vb`; place reusable logic in domain folders (`Ocr/`, `Imaging/`, `Secure/`). Do not replace the `NETFRAMEWORK` define or silently collapse platform-specific branches.

## Testing Guidelines

Use xUnit and name test files `*Tests.vb`; use focused, behavior-based test method names. Add unit coverage for pure logic and contract changes, especially serializer, localization, and platform-seam behavior. `Lite.Tests` compiles linked production files for both `net48` and `net10.0-windows`; record both targets as release evidence for shared changes. There is no stated coverage threshold.

## Commits and Pull Requests

Recent commits use concise imperative prefixes such as `docs:`, `chore:`, `winget:`, or a component label (`OCR:`, `Share:`). Keep each commit scoped. PRs should describe user-visible and compatibility effects, link the issue/specification when available, list test commands run, and include screenshots for UI or website changes. Never commit generated `bin/`, `dist/`, `stage/`, or `obj/` outputs; the secrets rule (never commit secrets or signing material) has its home in the canon, REPOSITORY_LAYOUT rule 1, and applies in full.
