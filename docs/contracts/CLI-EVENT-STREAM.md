# Pointer - `CLI-EVENT-STREAM`

| | |
| --- | --- |
| **Id** | `CLI-EVENT-STREAM` |
| **Version** | 0.17 (read on 2026-11-13 and again on 2026-11-15, L0323 catch-up); registry row `partial 0.13`, which moves to 0.17 with the fix. Open, dated 2026-11-15: the scheduled fill's exit-code cross-check is not at 0.14 rule 11 (`FillEvents.Judge`) and the 0.17 fill numbers are not yet read into the journal; ticket L0329. Owner: FileDO |
| **Home** | `cli-event-stream/` in the shared contracts catalog |
| **Role here** | **consumer** - the Share Manager's scheduled "fill free space" operation (specification L0299) runs FileDO's `fill .. del` unattended with `--events` and `--stop-file` and reads the run from the event file (`src/FastMediaSorterCompanion/Core/Scheduled/ScheduledFillFreeSpace.vb`, `ScheduledExecutor.ExecuteFill`); held by `tests/Companion.Tests/ScheduledFillFreeSpaceTests.vb`; the scheduled folder backup (specification L0298) runs FileDO's `backup .. gen .. stem ..` the same way (`Core/Scheduled/ScheduledBackup.vb`, `ScheduledExecutor.ExecuteBackup`), held by `tests/Companion.Tests/ScheduledBackupTests.vb`. Modern only |

## What this repository must do to stay conformant

- **Only whole lines (rule 4).** The event file is read up to its last line end; a line still being written is
  never parsed.
- **The verdict comes from `result` (rule 10)** - `Passed`, `Failed`, `Done`, `Stopped`, `Not proven` - and is
  cross-checked against the exit code (rule 11: `Passed`/`Done` 0, `Failed` 1, `Not proven` 2, `Stopped` 0; the 0.14
  table adds 10 to 15 and 16, which `FillEvents.Judge` does not yet have, ticket L0329). A
  missing `result`, an exit code that disagrees or an unknown verdict is **not proven**, never a pass.
- **A stop is the caller's, written as the stop file (rules 14, 15).** A run stopped through it ends as stopped,
  except that a `Failed` result with exit 1 outranks the stop.
- **Only published numbers are relied on (rule 16).** The journal's figures come from the last `progress`
  event's `doneBytes` / `doneItems`; `fill`'s own `filesCreated` / `bytesWritten` are not read until they are
  published (proposal `PROPOSAL-2026-11-10-fill-numbers`); 0.17 publishes them and the journal does not read them
  yet (ticket L0329).
- **A newer MAJOR or a non-integer `schemaVersion` is refused** and the run is not proven.
- **A backup ends by the classes of 0.17 item F.** Exit 10 with `Not proven` is a refused definition: the operation
  is parked until it is edited or run by hand; 11, 13, 15, or 2 with `file-in-use` is an obstacle the operation
  waits at; anything else is not proven. `details.reason` of an `error` finding (rule 17) only says which side was
  refused. The generation names are composed from the operation's own stem and the published numbers (item D).
- **A sealed backup's password rides `pe:<VARIABLE>` in the child's environment only** (item H), never an argument.

## Where it stands

The registry row of this product says what is proven and what is open; this pointer never repeats it.
Specifications L0299 and L0298 hold the audits.
