# Pointer - `DISK-SHARE`

| | |
| --- | --- |
| **Id** | `DISK-SHARE` |
| **Version** | 1.4, draft, read 2026-10-07 (sections 16-19; section 19 corrected where `vdInfo` / `vdMount` sit in the finding event). Owner: FileDO |
| **Home** | `disk-sharing/README.md` + `DISK-SHARE.md` in the shared contracts catalog |
| **Role here** | consumer - Share Manager disk UI and schema-2 worker client (`DiskSharing`, `DiskOperations`, `DiskDriveLetters`, `Share_Disks_Form`, the disk menu of the main list) |

The Share Manager implements file-backed registration, lifecycle controls and disk status through the
existing worker protocol (`WORKER-IPC` 0.14 section 9 and item D). Root ownership remains separate from ordinary
folder updates: only FMS-owned roots ever reach `SetSharedFolders`, and a root with an owner this host does not know
is left alone. A local Windows drive letter is a user-session alias of the worker mount, not another holder or a
phone-facing field.

## What this repository must do to stay conformant

- **Unknown is never authority (`WORKER-IPC` item D, `DISK-SHARE` section 16).** A state outside the six, a holder
  outside `none` / `service` / `session`, or a missing record is *unknown*: Open is enabled only for `closed`,
  `failed` and `locked`; `DiskOperations.OpenAsync` and the encrypted-autostart proof refuse an unknown state without
  asking the worker; the holder and state columns say "Unknown" and never "No" or "Error"; a drive letter is attached
  only to a disk reported `open`; a missing record or list is an error, never an empty answer. Pinned by
  `DiskUnknownAuthorityTests` (2026-10-07).
- A disk operation is a schema-2 request, bounded at 256 KiB and 1024 records, and an encrypted credential is never
  echoed back, logged or kept after the call (`DiskSharing.SendAsync`).
- The worker owns the authoritative record; the host reconciles from successful responses only and never treats a
  failed read as "nothing is held" (`MainWindow.RefreshDiskSnapshotAsync` keeps the last snapshot and the aliases on a
  transport failure).
- Section 19 (1.4) is the **worker's** to follow (`finding.data.details.vdInfo` / `.vdMount`, rule 64): the worker's
  source reads `details` since 2026-10-05, but the binary vendored in `payload/companion/` is older (2026-10-04) and
  has not been re-vendored here, so a share through it can still be refused until it is. Not rebuilt from this
  repository, by rule.

Evidence: Companion.Tests disk protocol, ownership and unknown-authority tests, the generated request vectors
(`WORKER-IPC` pointer), a Windows alias round-trip; worker suites and its capability probe against an actual
source-built FileDO (ticket 114, 2026-10-04). Native mounting, encrypted/RAM/sealed SFTP parity, reboot and installed
update proof remain open. A compatible published FileDO component and explicitly approved administrator-capable
hosting are release prerequisites; the ordinary LocalService host is not silently elevated. Ticket 114 tracks these
remaining obligations.
