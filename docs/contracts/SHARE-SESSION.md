# Pointer - `SHARE-SESSION`

| | |
| --- | --- |
| **Id** | `SHARE-SESSION` |
| **Version** | 1.1, active (read 2026-10-02: the 1.1 clarification states guarantee 3's own trigger - the identity survives until the host's state store is explicitly removed by hand; no obligation, class or threshold changed). Owner: FMS Companion (the Go worker holds the server guarantees) |
| **Home** | `remote-folder-access/README.md` + `share-session-reconnect.md` in the shared contracts catalog |
| **Role here** | no implementation of either half of the session; custodian of guarantee 3's state across installs, uninstalls and host-mode changes (ticket 036) |

Both halves of the session belong elsewhere: the server guarantees to the Go worker this repository
vendors as a binary, the client obligations to the Android app. What ships from here is the launcher
and the Share Manager UI, neither of which opens an SFTP session.

## What this repository holds to stay conformant

**Guarantee 3 - the identity survives every restart, migration, update and host-mode change, until the
host's state store is explicitly removed by hand (1.1) - is kept partly by code in this repository:**

1. **`src/FastMediaSorterCompanion/Core/WorkerProcess.vb`**: never touches or regenerates the worker's
   data directory (`%LOCALAPPDATA%\FastMediaSorterCompanion\`).
2. **`publishing/installer/install-share-service.ps1`**:
   - `Copy-State` validates the host-key fingerprint using the worker binary itself (`--inspect-datadir`)
     before and after copying.
   - If the target already holds a different key, migration stops immediately (exit code 7) before
     registering the service, avoiding unpairing paired phones.
   - `-OnlyWhenTargetIsFresh` ensures an existing identity in the machine store is preserved on upgrades
     rather than overwritten by stale user state.
   - `Resolve-UserDataDir` stops (exit code 10) if multiple user profiles hold candidate identities rather
     than guessing.
   - Removing the server role leaves the state directory intact so paired phones keep working.
3. **`publishing/installer/FastMediaSorter.iss`**: uninstaller removes only the viewer settings folder; the
   worker's state directory survives so a reinstall preserves pairing.
4. **Text consistency**: no user-facing string or documentation states the idle window as a number or
   promises that re-pairing is needed after an update, restart, reboot or edition switch.

Held and verified by `tests/Integration/ShareIdentityMigration.ps1`.

