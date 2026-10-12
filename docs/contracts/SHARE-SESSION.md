# Pointer - `SHARE-SESSION`

| | |
| --- | --- |
| **Id** | `SHARE-SESSION` |
| **Version** | 1.2, active (read 2026-10-02: the 1.1 clarification states guarantee 3's own trigger - the identity survives until the host's state store is explicitly removed by hand; no obligation, class or threshold changed; the catalog is still at 1.1 on 2026-10-07 and the custody statements below were re-read against `install-share-service.ps1`, `FastMediaSorter.iss` and `WorkerProcess.vb` that day, nothing re-run). Owner: FMS Companion (the Go worker holds the server guarantees) |
| **Home** | `remote-folder-access/README.md` + `share-session-reconnect.md` in the shared contracts catalog |
| **Role here** | **client** of the session for the 64-bit viewer's SFTP resources (obligations O1-O4, "The client half" below); no server half; custodian of guarantee 3's state across installs, uninstalls and host-mode changes (ticket 036) |

The server guarantees belong to the Go worker this repository vendors as a binary. The client obligations bind the viewer's SFTP provider since tickets 007 and 100 made servers and shares openable resources; the launcher and the Share Manager UI still open no SFTP session.

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

## The client half: the viewer's SFTP resources (O1-O4)

Since the 64-bit viewer opens servers and Share Manager shares as resources (`src/Resources/SftpProvider.vb`,
`ResourceConnect.vb`, `EndpointResolver.vb`), it is a client of this contract, and the four obligations are held
by code here:

- **O2, four classes kept apart.** `ResourceFailureKind` separates transport/timeout, an auth refusal, a host-key
  mismatch and an SFTP status, and `ResourceMessages` gives each its own sentence. A wrong credential, a key that is
  not the pin and a declined trust prompt end the whole round at once and are never retried on another address; a
  status (permission denied, no such file) is shown and never triggers a reconnect. Pinned by
  `ResourceConnectTests` and, against a real server, `SftpLiveTests` (hand-run, skipped in the default run, passed on
  2026-10-07 against real SSH servers: `Live_AWrongPassword_IsAuth`,
  `Live_APinThatIsNotTheServersKey_IsKeyMismatch_WithBothFingerprintsToShow`,
  `Live_AMissingFolder_IsNoSuchFile_NotATransportError`).
- **O1 and O3, transparent and bounded.** `SftpProvider.ConnectLocked` re-dials a dropped link: the first attempt
  and three retries after 1, 4 and 10 s, stopping at once on a non-retryable class and on the person's cancel.
  Every connection re-verifies the stored pin before authenticating, and a candidate address is never trusted
  alone - `EndpointResolver` races them (two lanes, 5 s per candidate, 45 s per round). Live case:
  `Live_ADroppedServer_IsATransportFailure_NotLocalIO_AndTheNextCallReconnects`.
- **Connection limit.** The previous client is disposed before each new dial, and one connection serves a
  resource session.
- **O4.** A 30 s SSH keepalive is only an optimisation; nothing relies on the server keeping a session open.

Observations, not claims of conformance: `ReconnectPolicy.RunAsync` (an injectable clock and a 195 s ceiling) is
exercised by tests only - production reconnects through the inline loop of `ConnectLocked`, which has the same
steps and no global ceiling. mDNS rediscovery by fingerprint is not implemented, so a share whose LAN address
went stale is reached only through its other stored paths. Acceptance scenarios 1 (idle close), 3 (state
directory removed by hand) and 4 (network pulled mid-idle) have not been run against the real worker; scenario 2
and a refused password have, against the worker's own `devserve`, in the hand-run
`tests/Integration/SftpConsumerLive.ps1` (25 cases, passed 2026-10-07).

Not filed, for the owner: the catalog names Android as the only client of `SHARE-SESSION`; a registry row for the
viewer as a second client is to be added through a proposal.

## Resource PIN amendment read at 1.2

Protected resources use the exact non-echo SSH keyboard-interactive challenge `Resource`,
`Password`, `PIN`. The original resource PIN authorizes one virtual root for one SSH session.
Ordinary password authentication exposes only unprotected roots. Traversal and protected
physical descendants remain denied. Configuration changes and explicit proof invalidation
close prior sessions. The live Windows consumer fixture exercises the actual Go SFTP boundary.
