# Pointer - `CLI-EVENT-STREAM`

| | |
| --- | --- |
| **Id** | `CLI-EVENT-STREAM` |
| **Version** | 0.13 (read on 2026-11-10). Owner: FileDO |
| **Home** | `cli-event-stream/` in the shared contracts catalog |
| **Role here** | **consumer** - the Share Manager's scheduled "fill free space" operation (specification L0299) runs FileDO's `fill .. del` unattended with `--events` and `--stop-file` and reads the run from the event file (`src/FastMediaSorterCompanion/Core/Scheduled/ScheduledFillFreeSpace.vb`, `ScheduledExecutor.ExecuteFill`); held by `tests/Companion.Tests/ScheduledFillFreeSpaceTests.vb`. Modern only |

## What this repository must do to stay conformant

- **Only whole lines (rule 4).** The event file is read up to its last line end; a line still being written is
  never parsed.
- **The verdict comes from `result` (rule 10)** - `Passed`, `Failed`, `Done`, `Stopped`, `Not proven` - and is
  cross-checked against the exit code (rule 11: `Passed`/`Done` 0, `Failed` 1, `Not proven` 2, `Stopped` 0). A
  missing `result`, an exit code that disagrees or an unknown verdict is **not proven**, never a pass.
- **A stop is the caller's, written as the stop file (rules 14, 15).** A run stopped through it ends as stopped,
  except that a `Failed` result with exit 1 outranks the stop.
- **Only published numbers are relied on (rule 16).** The journal's figures come from the last `progress`
  event's `doneBytes` / `doneItems`; `fill`'s own `filesCreated` / `bytesWritten` are not read until they are
  published (proposal `PROPOSAL-2026-11-10-fill-numbers`).
- **A newer MAJOR or a non-integer `schemaVersion` is refused** and the run is not proven.

## Where it stands

The registry row of this product says what is proven and what is open; this pointer never repeats it.
Specification L0299 holds the audit.
