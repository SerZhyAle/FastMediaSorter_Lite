# Pointer - `WORKER-LAUNCH`

| | |
| --- | --- |
| **Id** | `WORKER-LAUNCH` |
| **Version** | 1.0, active (since 2026-10-17; split out of `WORKER-IPC` 0.17). Owner: FMS Companion (the Go worker repository) |
| **Home** | `worker-launch/README.md` in the shared contracts catalog |
| **Role here** | launcher - the Share Manager starts the worker (`src/FastMediaSorterCompanion/Core/WorkerProcess.vb`, `Core/ServiceControl.vb`) and the installer scripts register it (`publishing/installer/install-share-service.ps1`); the viewer only reads the worker's state directory for display (`src/Resources/WorkerShares.vb`). The worker is the producer of the flags and of the inspection JSON |

## What this repository must do to stay conformant

- Pass the chosen state directory **explicitly** (`--datadir`) when a host starts a worker, and resolve it in the
  contract's order: explicit directory, then a **readable** machine directory, then the user directory. "Readable"
  means usable by this identity, not merely existing: a machine directory the user cannot read is not adopted.
- Never infer that a worker or its state is absent from a failed pipe.
- `hostkey` is a frozen migration filename. Compare a read-only `--inspect-datadir` before and after any
  migration and stop on a fingerprint change or a corrupt key; never widen the key directory.

## Where it stands

The registry row of this product and its dated exceptions say what is proven and what is not; this pointer
never repeats them. The version table of what was read against which version is in specification 244.
