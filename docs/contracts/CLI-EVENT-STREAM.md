# Pointer - `CLI-EVENT-STREAM`

| | |
| --- | --- |
| **Id** | `CLI-EVENT-STREAM` |
| **Version** | 0.18 (read on 2026-11-17, specification L0346; earlier 0.17 read on 2026-11-13 and 2026-11-15, L0323 catch-up); registry row updates to 0.18 (working tree, not built/tested until release). The scheduled fill's exit-code cross-check is at 0.14 rule 11 (`FillEvents.Judge`) and the 0.17 fill numbers are read into the journal since 2026-11-15, ticket L0329 (direct-called, not project-built; the Companion suite is a release item). Owner: FileDO |
| **Home** | `cli-event-stream/` in the shared contracts catalog |
| **Role here** | **consumer** - the Share Manager's scheduled "fill free space" operation (specification L0299) runs FileDO's `fill .. del` unattended with `--events` and `--stop-file` and reads the run from the event file (`src/FastMediaSorterCompanion/Core/Scheduled/ScheduledFillFreeSpace.vb`, `ScheduledExecutor.ExecuteFill`); held by `tests/Companion.Tests/ScheduledFillFreeSpaceTests.vb`; the scheduled folder backup (specifications L0298, L0346) runs FileDO's `backup .. gen .. stem ..` or `backup .. keep-days .. [keep-monthly] stem .. [skip-links]` the same way (`Core/Scheduled/ScheduledBackup.vb`, `ScheduledExecutor.ExecuteBackupCore`), held by `tests/Companion.Tests/ScheduledBackupTests.vb`. Modern only |

## What this repository must do to stay conformant

- **Only whole lines (rule 4).** The event file is read up to its last line end; a line still being written is
  never parsed.
- **The verdict comes from `result` (rule 10)** - `Passed`, `Failed`, `Done`, `Stopped`, `Not proven` - and is
  cross-checked against the exit code (rule 11 at 0.14: `Passed`/`Done` 0, `Failed` 1, `Not proven` 2 or 10 to 15,
  `Stopped` 16; `fill` is none of the rule 15 exceptions that exit 0 on a stop, so a `Stopped` that exits 0 is
  not proven - `FillEvents.Judge`, ticket L0329). A
  missing `result`, an exit code that disagrees or an unknown verdict is **not proven**, never a pass.
- **A stop is the caller's, written as the stop file (rules 14, 15).** A run stopped through it ends as stopped,
  except that a `Failed` result with exit 1 outranks the stop.
- **Only published numbers are relied on (rule 16).** The journal's figures are `fill`'s own `filesCreated` /
  `bytesWritten` (0.17 item B, which settled proposal `PROPOSAL-2026-11-10-fill-numbers`); the last `progress`
  event's `doneBytes` / `doneItems` are the fallback only when a number is absent (an older FileDO), and the
  journal marks that as `;counts=progress`. An absent key is "not reported", never zero. `filesNotDeleted` above
  zero (item C: a `del` that leaves files of its own run ends `Failed`, exit 1, with no `details.reason`) is named in
  the journal verdict as `;not-deleted=N`; the failure code stays `fill-failed` (a new one would need a 13-language
  string).
- **A newer MAJOR or a non-integer `schemaVersion` is refused** and the run is not proven.
- **A backup ends by the classes of 0.17 item F.** Exit 10 with `Not proven` is a refused definition: the operation
  is parked until it is edited or run by hand; 11, 13, 15, or 2 with `file-in-use` is an obstacle the operation
  waits at; anything else is not proven. `details.reason` of an `error` finding (rule 17) only says which side was
  refused. The generation names are composed from the operation's own stem and the published numbers (item D).
- **Dated backup retention and monthly-first preservation follow 0.18 items I-K.** `keep-days <1-999>`, optional
  `keep-monthly`, `stem <name>` and optional `skip-links` run against `<stem>-<yyMMddHHmm><ext>` in local wall-clock
  time; `retiredCount`, `keptCount`, `keepDays` and `monthlyKept` are read from `result.numbers` (never defaulting
  absent keys to zero), while `details.skippedLink` on `info` findings is journaled as `backup-skip-link`.
- **A sealed backup's password rides `pe:<VARIABLE>` in the child's environment only** (item H), never an argument.

## Where it stands

The registry row of this product says what is proven and what is open; this pointer never repeats it.
Specifications L0299 and L0298 hold the audits.
