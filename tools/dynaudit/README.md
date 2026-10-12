# DynAudit: the dynamic-audit stand of specification 209

This is the measurement stand of the deep dynamic audit: five properties (responsiveness,
wrapped errors, absence of system blocking, leaks, accessibility of understanding) measured
from OUTSIDE the running program, with numbers and reproductions, not impressions. The
specification that owns every rule cited below lives in the owner-local planning queue
(189, the dynamic audit); this README is only the runbook.

Nothing here changes the product. A defect a scene proves gets its own ticket with the
scene's artifacts attached; a documented decision is never a finding - only its measured
consequence can be.

## Layout

- `Invoke-DynScene.ps1` - the scene driver. One scene per run, one CHECK-VERDICT line,
  exit 0 = pass, 1 = a budget broken or a wrap criterion failed, 2 = could not verify.
  `-SelfTest` is the D-0 validity gate; no scene run before a passing self-test counts.
- `DynAudit.ps1` - the campaign runner. Self-test gate, control run, scene loop, parking
  state, the matrix reprint and the aggregate `DynAudit: PASS/FAILED/COULD NOT VERIFY
  (n scenes, m skipped)`. Joins `tools\Run-AllTests.ps1` only behind `-DynAudit`
  (opt-in hand-run, Q209-5), never in a default run.
- `scenes.jsonl` - the 50-scene catalog x five properties (250 cells; at Draft 214 open,
  36 n/a, 0 measured). Cells carry their probes, their n/a reasons and their budgets.
- `capabilities.jsonl` - the 19-row capability list, each row citing the anchor that
  documents it; the self-test fails when an anchor disappears or a capability has no
  covering scene.
- `budgets.jsonl` - H1..H14, every row a hypothesis (`accepted: false`) until the owner
  accepts it at the D-2 baseline (Q209-4). A breach before that is worded "budget
  hypothesis exceeded [H]", never yet a defect.
- `lib/` - the probes (ping, key, coh, clip, lock, samp, slope, inv, err, uia, pix, nrt,
  start), the fixture window the self-test verifies them against, the cohost and samper
  children, the profile backup/restore, the synthetic corpus, the scrubbing layer.

## The order of a campaign (section 11)

1. **D-0 validity first.** `Invoke-DynScene.ps1 -SelfTest` must exit 0. Then record the
   control run: `DynAudit.ps1 -Control -RunFolder <run>` writes `control.json` (the
   probes' own jitter and the machine's floor, the product never launched). A scene
   without a paired control row is exit 2: a number without a control is worse than no
   number.
2. **D-1 permanent harm** (`-Phase D-1`): S1, S2, S8 - leaks and blocking before latency.
3. **D-2 the responsiveness baseline** (`-Phase D-2`): S4; the owner accepts or amends each
   budget row here.
4. **D-3 wrapped errors** (`-Phase D-3`): S7 and the E cells.
5. **D-4 extremes** (`-Phase D-4`): S5, S6.
6. **D-5 session transitions** (`-Phase D-5`): S3 - real metal only (Q209-8).
7. **D-6 understanding**: the A cells; S4.8 is the anchor scene; the 13-language
   label/state matrix and the scripted Narrator walk are hand passes recorded per
   language into the run folder.
8. **D-7 the report**: the findings registry (P0-P3 with R/V/T/L proof rank; P0 and P1
   require T or L), the tickets it spawns, the matrix reprint with final states.

## The isolated one-time profile (section 5, Q209-2)

Run the campaign from a throw-away local Windows account: its own `HKCU`, its own
`%LOCALAPPDATA%`, nothing of the real profile in play, and no other work open in that
session. Every scene does step 0 (reg export of `SZA\FastMediaSorter`, a copy of
`%LOCALAPPDATA%\SZA\FastMediaSorter`, the `%TEMP%` baseline, SHA-256 of everything,
`recovery-manifest.json`) and refuses to start without it - no backup, no run. The
restore runs in `finally` even after a crash, verifies against the manifest, and its
verdict rides the scene line; a failed restore is a P0 incident, never silent.

The in-place fallback (backup/restore on the working account) works but is weaker
isolation: other processes of the real session pollute B and R. That is why the account
is recommended.

## The confirm switches

- `-Confirm quiet-session` - required by every scene that injects input. SendInput is
  session-global; the switch is the operator's declaration that the isolated session has
  nothing else open. Without it such a scene is exit 2, never a silent pass.
- `-Confirm sleep` - S3.1 puts the machine to sleep and resumes it.
- `-Confirm lock` - S3.2 locks the workstation.
- `-Confirm dev-gate-broadcast` - S7.6 runs behind the live-broadcast dev gate
  (`FMS_LIVE_BROADCAST=1`), in the development environment only (Q209-3).

## Hand-assisted scenes

Scenes whose driving surface is not scriptable from outside (opening Settings, the
Share Manager, an editor save, an SFTP resource, a FileDO disk, the settings storm)
pause at an `[assist]` prompt in the console: do the named thing, press Enter. The
probes keep sampling while the scene waits, so the wait is measured, not lost. Scenes
whose environment cannot be prepared here answer COULD NOT VERIFY with the unmet
requirement named:

- `-SftpFake` - the loopback SFTP stand for S4.10, S6.5, S7.4 (the real devserve belongs
  to the worker repository; this harness only points the product at it).
- `-RuntimeTree` - a disposable staged copy of the runtime tree for the S7.7 rename.
- quota volume (S7.3) - prepare by hand: a small VHD with a tight quota mounted at a
  known letter; see the scene row.
- battery (S8.3), sleep metal and RDP (S3, Q209-8) - hardware states, not scriptable.

## Artifacts and privacy (section 12)

One run folder (`%TEMP%\fms-dynaudit-<stamp>\` unless `-RunFolder`) holds everything:
CSV series, screenshots, UIA walks, log deltas, the backup and manifest, the control run,
`matrix-final.json` and `campaign-summary.csv`. Every line is scrubbed on write: profile
paths become `<USER>`, the machine name `<HOST>`, credential-shaped material `<hidden>`.
All media is generated into the run folder; no real media can appear. The stand dials only
loopback and the sampler records any non-loopback connection the product's processes make
- an unexpected connection is a finding against the no-telemetry claim.

Raw artifacts are never committed; run folders live in `%TEMP%` and are deleted 14 days
after the campaign closes, or earlier at the owner's word. Only the summarized CSV and
the final matrix are kept as evidence, written into the specification's evidence sections.

## Long scenes

The default campaign skips scenes longer than `-MaxSceneMinutes` (15 minutes) and names
them on the verdict line - the 30-minute idle, the 2-hour minimized and the 8-hour soak
are hand runs (D-1 on metal, Q209-6), driven with `-All` or a single `-Scene S8.4`.
