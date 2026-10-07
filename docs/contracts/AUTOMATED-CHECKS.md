# Pointer - `CHECK-VERDICT`, `CHECK-BASELINE`, `CHECK-PLACEMENT` and `BUILD-EVIDENCE`

| | |
| --- | --- |
| **Ids** | `CHECK-VERDICT`, `CHECK-BASELINE`, `CHECK-PLACEMENT`, `BUILD-EVIDENCE` |
| **Version** | 0.12 / 0.11 / 0.11 / 0.10, draft (read 2026-10-07; 0.12 adds the verdict word `NOT APPLICABLE`, `CHECK-BASELINE` 0.11 puts metric bounds out of scope, the other two are unchanged since the 2026-10-02 reading). Owner: FastMediaSorter Android |
| **Home** | `automated-checks/README.md` in the shared contracts catalog |
| **Role here** | `CHECK-VERDICT`, `CHECK-PLACEMENT`, `BUILD-EVIDENCE`: producer and consumer - every check script under `tools/` and `tests/Integration/`, the runner `tools/Run-AllTests.ps1`, the release preflight in `tools/Release.ps1`, and `build.ps1`. `CHECK-BASELINE`: no role - no accepted debt exists |

## What this repository does to stay conformant

- **One vocabulary, one helper.** A check dot-sources `tools/lib/Verdict.ps1`: exit `0` pass, `1` a defect,
  `2` could not verify, and one last line `<ScriptName>: PASS | FAIL | COULD NOT VERIFY (..)` on the failure
  path as well. A step the caller switched off (`-SkipGo`, `-SkipTests`) is named as skipped and never counted
  as passed; in the release preflight a skipped step makes the verdict `COULD NOT VERIFY`, so the tag push
  refuses it. No check emits `3`; the reserved `4` is read as `2` by the runners.
- **`NOT APPLICABLE` (0.12) is not produced anywhere here** - no check has a "work switched off by the
  caller" path of its own; the aggregators count a switched-off step as skipped, which 0.12 item A keeps. The
  readers (`Invoke-ChildCheck`, `Get-AggregateCode`) do not yet know the word, so a future producer must teach
  them first or its line would be counted as a pass. Recorded as an exception.
- **Placement is declared, and held both ways.** `tools/check-placement.jsonl` carries the one
  `per-change runner: none` declaration (0.11 rule 8) and a record per check (classes `agent-closure`,
  `release`, `build`, `hand-run`); `tools/Test-CheckPlacement.ps1` - run by every `Run-AllTests` - fails a check
  a runner runs without a record, a record whose runner does not run it, and a script that mentions the
  verdict vocabulary with no record. There is no per-change runner (no hook, no CI test job); the closest is
  `Run-AllTests`, which `CLAUDE.md` makes the step before "done". The detector is textual, so a check that
  does not use the helper escapes it - `tools/Test-WindowsStoreConformance.ps1` has no record.
- **Evidence about the artifact, not the source.** A check prints `<ScriptName>: subject = ..` first.
  `tools/Test-ArtifactVersion.ps1` is meant to read the version resource of the built exes - in `build.ps1`
  before anything is deployed, and in the release preflight on the staged package and both `setup.exe` files
  against the tag. **The script on disk since 2026-10-03 is not that check** (it compares the file-version
  string, prints no verdict line and does not split the preflight's comma-joined list): see the registry
  exception. Both generated artifacts have a check mode against their source - the site
  (`Build-SitePages.ps1 -Check`) and the glyph import (`Sync-IconGlyphs.ps1 -Check`) - run before every tag.
- **Not held, recorded in the registry:** `BUILD-EVIDENCE` rules 4-6 need execution telemetry and none
  exists, so no run-time figure is stated in prose; rule 8 (the rebuilt artifact) - the tag workflow checks out
  the tag and proves `HEAD` is its commit, then rebuilds with no test step and no version read-back, and
  `Release.ps1 -AllowDirty -Push` still tags `HEAD` after judging a dirty tree; the verdict line of
  `Run-AllTests` counts skipped steps but not skipped tests (rule 2.13).
- **`CHECK-BASELINE`.** No baseline file, no frozen finding class. The product-naming allow-list is a reasoned
  exemption list that the check itself fails when it goes stale; it is not accepted debt and falls under
  neither of the contract's two shapes.
