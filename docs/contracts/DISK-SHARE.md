# Pointer - `DISK-SHARE`

| | |
| --- | --- |
| **Id** | `DISK-SHARE` |
| **Version** | 1.5, draft (since 2026-10-01; 1.5 of 2026-10-17; read on 2026-10-20). Owner: FileDO |
| **Home** | `disk-sharing/README.md` and `DISK-SHARE.md` in the shared contracts catalog |
| **Role here** | **consumer** - the Share Manager is the schema-2 client of the worker's disk requests and the host of the FileDO disk registration (`src/FastMediaSorterCompanion/Core/DiskSharing.vb`, `DiskOperations.vb`, `DiskDriveLetters.vb`, `FileDoDiskManager.vb`, `Forms/Share_Disks_Form.vb`); the worker, in the sibling repository, owns the authoritative record. Modern only |

## What this repository must do to stay conformant

- **The requests.** The six `disk-*` requests travel in the `WORKER-IPC` flat envelope at schema 2 with `owner` set to
  `filedo`; a response is at most 256 KiB and carries at most 1024 records (rule 57); a worker's error text is shown
  cut and with control characters replaced; a disk password is never echoed, logged or put on a command line
  (rules 46, 47).
- **Unknown is never authority (section 16, rule 61; `WORKER-IPC` item D).** A state, mode or holder the host does not know,
  or an absent record, is unknown, never `none`: it never authorizes a mount, dropping a holder or an automatic
  remount. A holder is shown from the worker's live answer.
- **Owners stay apart (rule 39, section 16).** The host's own folder updates never touch or erase a `filedo` root,
  and the host reconciles its snapshot from successful worker responses only.
- **Registration (1.5 amendment).** Registering the FileDO Disk Manager is an explicit action. The installation is
  found through verified registered application identities, never guessed, and a failed pipe call never means
  "not installed": absent, installed but stopped, missing disk payload, incompatible schema or capability, and
  transport or identity failure are told apart. Opening, installing or starting needs an explicit action; a
  mounted disk is handed over only after confirmation, a required RAM save and a clean unmount.
- **For the worker half, so the host never contradicts it.** Autostart is a per-disk opt-in and its key is held by
  the holder only and destroyed when the mark is cleared, the disk is unshared or the container password changes
  (rules 20 to 24); a paired device sees a disk as an ordinary folder root and a closed or locked one as an
  unavailable folder root (rules 25 to 28); the service runs only its own verified `filedo.exe` from the Program
  Files directory (rules 48, 49); the structured findings are at `finding.data.details` (rule 64).
- **Privacy pages (rules 51 to 53).** They disclose that disks can be served to paired devices and that autostart
  keys of encrypted disks are stored on the PC, and the permission lists name both.
- **Evidence.** Conformance is shown by client and worker exchange tests and by a joint run with FileDO on live
  mount, restart and reboot cases; the amendment names plain, encrypted and RAM disks.

## Where it stands

The registry row of this product and its dated exceptions say what is proven and what is open; this pointer never
repeats them. Specification 244 holds the clause-by-clause audit.
