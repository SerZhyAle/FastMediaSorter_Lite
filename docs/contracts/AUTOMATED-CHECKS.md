# Pointer - `CHECK-VERDICT`, `CHECK-BASELINE`, `CHECK-PLACEMENT` and `BUILD-EVIDENCE`

| | |
| --- | --- |
| **Ids** | `CHECK-VERDICT`, `CHECK-BASELINE`, `CHECK-PLACEMENT`, `BUILD-EVIDENCE` |
| **Version** | 0.10 / 0.9 / 0.10 / 0.9, draft (read 2026-09-24). Owner: FastMediaSorter Android. **Implemented here 2026-09-25** (ticket 042): `CHECK-VERDICT`, `CHECK-PLACEMENT`, `BUILD-EVIDENCE` rules 1, 2 and 7; `CHECK-BASELINE` dormant |
| **Home** | `automated-checks/README.md` in the shared contracts catalog |
| **Role here** | consumer - every check script under `tools/` and `tests/Integration/`, the test runner `tools/Run-AllTests.ps1`, the release preflight in `tools/Release.ps1`, and `build.ps1` |

## What this repository does to stay conformant

- **One vocabulary, one helper.** Every check dot-sources `tools/lib/Verdict.ps1`: exit `0` pass, `1` a
  defect, `2` could not verify, and one last line `<ScriptName>: PASS | FAIL | COULD NOT VERIFY (..)` on the
  failure path as well. A step the caller switched off (`-SkipGo`, `-SkipTests`) is named as skipped and
  never counted as passed; in the release preflight a skipped step makes the verdict `COULD NOT VERIFY`, so
  the tag push refuses it. No check emits `3`; the reserved `4` is read as `2` by the runners.
- **Placement is declared, and held both ways.** `tools/check-placement.jsonl` names the runner of every
  check (classes `agent-closure`, `release`, `build`, `hand-run`), and `tools/Test-CheckPlacement.ps1` - run
  by every `Run-AllTests` - fails a check a runner runs without a record, a record whose runner does not run
  it, and a verdict-reporting script with no record. There is no per-change runner (no hook, no CI test
  job): the closest is `Run-AllTests`, which CLAUDE.md makes the step before "done".
- **Evidence about the artifact, not the source.** Each check prints `<ScriptName>: subject = ..` first.
  `tools/Test-ArtifactVersion.ps1` reads the version resource of the built exes - in `build.ps1` before
  anything is deployed, and in the release preflight on the staged package and both `setup.exe` files
  against the tag. Both generated artifacts have a check mode against their source: the site
  (`Build-SitePages.ps1 -Check`) and the glyph import (`Sync-IconGlyphs.ps1 -Check`), both run before every
  tag.
- **Not held, recorded in the registry:** `CHECK-BASELINE` is dormant (no accepted-findings file exists);
  `BUILD-EVIDENCE` rules 4-6 are not implemented - no execution telemetry exists to judge a stated run time
  against; the release binaries are rebuilt by the tag workflow, which runs no test step - the
  preflight judges the local tree the tag is cut from.
