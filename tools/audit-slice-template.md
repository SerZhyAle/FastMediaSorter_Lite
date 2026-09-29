# Specification - audit slice 057.{{NAME}}: {{TITLE}}

> Status: **Ready** - ticket {{NUMBER}}, slice {{PART}} of the concern `{{CONCERN}}`{{MERGED}}, one work item of
> the pre-release full code audit (umbrella ticket 057). Created {{DATE}} by `tools/Audit-FanOut.ps1` from the manifest of baseline
> `{{BASELINE}}` (HEAD `{{HEAD}}` when partitioned). Everything needed to run this slice is in this file;
> nobody has to open 057. States: `Ready` -> `Partial` (a red build or test run, or work left) -> `Done`
> (only with a filled `## Last Audit`).

## 1. Goal

Read every file below in full, against the layers of section 7, and leave behind: every finding with an
id, a severity, a confidence and a runtime; the P1/P2 findings fixed inline with evidence or given their
own `NNN_` ticket; and "found sound" lines the next audit can start from. The audit aims at what no gate
sees - resource ownership, handler symmetry, UI-thread marshalling, `Async Sub` failure paths, file
safety on user data, and the `#If NETFRAMEWORK` branch one exe compiles and nobody runs.

Risk score {{RISK}} (rank {{RANK}} of {{TOTAL}}). {{FILECOUNT}} files, {{LINES}} lines, {{SEAMS}} with a runtime seam,
{{CHANGED}} lines changed since the baseline.

## 2. Files

| File | Lines | Seam | Changed since baseline | Earlier findings |
| --- | ---: | :---: | ---: | --- |
{{FILES}}
{{DATASAMPLE}}
## 3. Pre-scan signals

Re-run the count first (`tools/Audit-Partition.ps1` rewrites the manifest; or read the lines below).
Every hit is either a finding or one line in section 9 saying why it is fine - the list is the starting
point, not the search.

{{SIGNALS}}

## 4. Earlier findings that touch these files (2026-09-25 audit, registry 045)

Do not re-file one of these. Check that its fix holds and cite its id; a fix that broke a neighbouring
path is a new finding.

{{FINDINGS045}}

## 5. Found sound in 045 for this area

{{SOUND}}

## 6. Sibling slices

Same concern, audited separately. Read their files for context (a field's writer may live there), never
audit them here: {{SIBLINGS}}

## 7. Procedure

1. **Pre-scan** - section 3.
2. **Read by layer**, every file in full:
   1. *Structure* - the right partial for the concern; nothing edits a `Designer.vb`; a new file is in the
      net48 `<Compile Include>` list of `src/FastMediaSorter.vbproj` or is whole-file modern-only.
   2. *Dual runtime* - both branches of every seam; the x86 branch still does what the fallback did;
      modern-only code does not leak into shared code outside a seam. The x86 fallback gets bug fixes
      only, never a refinement.
   3. *Threads* - controls touched only on the UI thread; LibVLC and worker callbacks marshalled;
      `Async Sub` failures observed; timers stopped before their target is disposed; cancellation honoured.
   4. *Ownership* - every `IDisposable` has one owner and one disposal path, the failure path included;
      no file handle held past use (a held handle blocks the move/delete the app exists for); handlers
      added to a longer-lived object are removed.
   5. *User data* - move/copy/delete/rename never lose a file on a failure path, a network path or a
      name collision; temp + swap where a file is rewritten.
   6. *Boundaries* - elevation, `WM_COPYDATA`, the worker pipe, DPAPI, logs and the send-logs allow-list,
      `.fd-sec` (no decrypted byte on disk), paths handed to other programs.
   7. *Errors* - a swallowed exception that hides a user-visible failure; a status line that lies.
      `Debug.WriteLine` is stripped in Release: a `Catch` that only writes there logs nothing.
   8. *Installer scripts* (installer slices only) - quoting, idempotence, frozen anchors, no `AppMutex`,
      behaviour under a silent install.
3. **Record** every finding in section 9: id `057.{{NUM}}-<n>` (for example `057.{{NUM}}-1`), file:line, severity, confidence, runtime,
   a one-line failure scenario, the test that would prove it.
4. **Deduplicate** before any ticket: search 045 section 4 by file and symptom, then
   `grep -ril "<symptom>" docs/specifications/`. A match gets a reference, not a duplicate.
5. **Triage.** P1 - its own ticket, or an inline fix under section 8 with its evidence. P2 - inline under
   section 8, otherwise its own ticket. P3 - inline under section 8, otherwise one follow-up ticket per
   concern. P4 - recorded here; no ticket. A new ticket takes the next number in
   `docs/specifications/README.md` and a line in `RELEASE_QUEUE.md`.
6. **Fix and prove** the inline set: `.\build.ps1 -SkipInstaller`, then
   `pwsh -NoProfile -File .\tools\Run-AllTests.ps1 -SkipGo`. Quote both commands and their exit codes in
   `## Last Audit`. A red run ends the slice as `Partial`, never `Done`.
7. **Close** like any ticket: fill `## Last Audit`, set the status line to `Done`, update this ticket's
   row in `docs/specifications/README.md`, remove its line from `RELEASE_QUEUE.md` and move the file to
   `done/` without renumbering it (`tools/Audit-Status.ps1` finds a slice ticket in either folder).

## 8. Inline-fix rule

Fix a finding here only when its proof needs no manual run of the UI:
- (a) the change is in a pure, unit-tested layer (`ZoomMath`, `DecodeCacheKey`, `OcrResultPolicy`,
  `ArchivePaths`, `FdSec*`, `ShareConfigBuilder`, ..) and `tools/Run-AllTests.ps1 -SkipGo` passes after
  it (the viewer suite runs under net48 and net10); or
- (b) the change preserves behaviour by construction: `Using` around an `IDisposable` that had no owner,
  a log line inside a silent `Catch`, a comment, a redundant null check removed.

Every inline fix compiles in both viewers (`.\build.ps1 -SkipInstaller`; `msbuild` alone never proves the
mainline). Everything else is its own ticket, and these always are, whatever they look like:
`publishing/installer/*` (elevated - a ticket with a VM check); frozen anchors; anything that changes the
content of an `OcrOverlayDocument` (needs an `OcrPipelineVersion` bump); anything a contract states (an
amendment first); dead wiring the owner decided to keep (ILMerge, WebView2); any registry, cache-format
or contract change; any new UI string. Never undo an edit with `git checkout`/`restore`/`stash` - edit
it back. No commits unless the owner asks.

## 9. Findings

Disposition is one of: `ticket NNN`, `inline (a)` / `inline (b)` (evidence in `## Last Audit`),
`045 X-NN` (a known finding, re-checked), `P3 follow-up NNN`, `recorded` (P4 only), `sound` (a
pre-scan hit judged fine - say why in the scenario column).

| ID | Sev | Confidence | Runtime | Where | Failure scenario | Test | Disposition |
| --- | --- | --- | --- | --- | --- | --- | --- |

## Last Audit

NOT YET AUDITED. When closing, replace this line with: date; baseline commit; files read (all
{{FILECOUNT}}, or which not and why); counts by severity; inline fixes with the quoted build and test
commands and their exit codes; tickets spawned; "found sound" lines in the 045 section 7 style.
