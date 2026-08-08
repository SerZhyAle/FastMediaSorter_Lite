# Specification — Fast Media Sorter **Server** edition (always-on Android Folder Share)

> Status: **implemented, 2026-08-08** (designed 2026-08-06). Shipping in the release that carries this file. This replaced the earlier "install-time system-service choice" proposal with a public **Server edition**: a separately downloadable, installable distribution for machines whose job is to keep selected folders available to Android even when nobody is signed in.
>
> What landed: the SCM-aware worker host and its `--service` / `--manage-sid` / `--machine-datadir` / `--inspect-datadir` entry points (worker repository); [ServerFeatures.vb](../../../src/FastMediaSorterCompanion/Core/ServerFeatures.vb) with `ServerHostMode`, [ServiceControl.vb](../../../src/FastMediaSorterCompanion/Core/ServiceControl.vb), the Hosting console [Share_Hosting_Form.vb](../../../src/FastMediaSorterCompanion/Forms/Share_Hosting_Form.vb) and its strings; the elevated helper [install-share-service.ps1](../../../publishing/installer/install-share-service.ps1); the Server installer [FastMediaSorterServer.iss](../../../publishing/installer/FastMediaSorterServer.iss) plus User-edition conflict detection; [Build-ServerInstaller.ps1](../../../tools/Build-ServerInstaller.ps1) and the asset in `build.ps1`, `Build-OfflineRelease.ps1` and `release.yml`; the `SerZhyAle.FastMediaSorter.Server` manifest set; [server.html](../../../server.html) and the operational guide [SERVER_EDITION_BUILD_AND_TEST.md](../../guides/SERVER_EDITION_BUILD_AND_TEST.md).
>
> Acceptance criterion 10 in §8 is a **live-machine** matrix, and it is only partly discharged: the service host itself was exercised on the author's machine. Everything that needs an isolated VM - reboot with no logon, both migration directions, update over a real prior install, uninstall - is tracked as an open checklist in [SERVER_EDITION_BUILD_AND_TEST.md](../../guides/SERVER_EDITION_BUILD_AND_TEST.md#the-verification-matrix), not silently marked done here.
>
> Scope: the Go worker repository (`P:\windows\fms_companion`) supplies the SCM-aware worker host; this repository supplies the Server installer/profile, the Share Manager console behavior, migration, documentation, and winget/GitHub release plumbing. Build the Go worker only in its own repository; never from this repository.
>
> Related: [SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md), [SPECIFICATION_SHARE_SECURITY_HARDENING.md](SPECIFICATION_SHARE_SECURITY_HARDENING.md), [SPECIFICATION_SHARE_COMPANION_APP.md](SPECIFICATION_SHARE_COMPANION_APP.md), [SPECIFICATION_ANDROID_FOLDER_SHARE.md](SPECIFICATION_ANDROID_FOLDER_SHARE.md), [CONTRACT_ANDROID_RECONNECT.md](../../contracts/CONTRACT_ANDROID_RECONNECT.md).

---

## 0. Outcome and terminology

Fast Media Sorter has two mutually exclusive ways to host the same SFTP worker:

| | **User edition** (existing/default) | **Server edition** (new/optional) |
|---|---|---|
| Distribution | Current GitHub installer, current winget package, Store MSIX | Dedicated GitHub Server installer, dedicated website page, dedicated winget package |
| Host | Share Manager starts `fms-share-worker.exe` in the interactive session | Windows SCM starts `fms-share-worker.exe` as an automatic service |
| Availability | While a signed-in user and the tray process remain available | From boot, before logon, through logoff, and after SCM crash recovery |
| Service account | Signed-in user | `NT AUTHORITY\LocalService` by default |
| Data location | Existing per-user location until migrated | Machine-owned data location with an explicit ACL |
| Best fit | Occasional personal-PC sharing | Dedicated server, VPS, NAS-like Windows box, always-on home server |
| Microsoft Store MSIX | Supported, user-session only | Intentionally not offered |

**Rule of thumb:** install the Server edition only when the machine must keep folders reachable whenever Windows is running. It is not a separate protocol or a second Android app: SFTP, `.fmscfg`, QR export, firewall rule, and the Android reconnect contract remain identical.

The Server edition is a packaging and host-mode distinction, **not a fork of the worker**. Both editions must use the same IPC schema and persistent-state format.

### 0.1 Initial setup is still deliberate

"No logon required" starts **after provisioning**. An administrator must initially choose roots, approve the firewall and service-account access, and establish the host key/credentials. The service must never guess which folders to publish.

A future fully unattended deployment may accept an administrator-created configuration file or explicit command-line root parameters. That is a separate deployment feature; it is not implied by the interactive Server installer.

---

## 1. Distribution and upgrade model

### 1.1 GitHub and website

Each release provides, in addition to the existing User installer:

```text
FastMediaSorter-<version>-windows-x64-setup.exe          User edition (existing)
FastMediaSorter-<version>-windows-x64-server-setup.exe   Server edition (new)
```

The website adds a dedicated **Always-on Folder Share Server** page that contains:

- the Server installer download, SHA-256, release notes, and minimum OS/admin requirements;
- a plain explanation that it starts at boot and stays up without a user logon;
- first-time setup, firewall, root-permission, LAN/WAN, backup, update, and removal instructions;
- a User-to-Server and Server-to-User migration guide;
- links to the Android Folder Share guide and the existing security/reconnect documentation.

The Server installer may contain the Viewer as a convenience, but the required payload is the Share Manager plus the worker. It must not rely on the Viewer process being present or running.

### 1.2 winget

Publish a distinct package, for discoverability and an unambiguous unattended intent:

```text
SerZhyAle.FastMediaSorter           existing User edition
SerZhyAle.FastMediaSorter.Server    new Server edition
```

The Server package has its own ARP display name, installer identity, default directory, release asset, documentation URL, and update correlation. Its manifest must use the Server installer and describe the service, administrator requirement, and no-logon behavior.

Do **not** make the existing `SerZhyAle.FastMediaSorter` winget package silently install a service. Its current silent behavior remains user-session/viewer-safe. A user who explicitly chooses the Server package has made the necessary product choice. winget still needs a silent Server installer; it must require elevation rather than silently bypassing it.

### 1.3 Microsoft Store

The Store/MSIX build remains User edition only. This is a product/channel scope decision: the Server edition needs a machine service, machine data ACLs, root ACL grants, and an administrator-led deployment flow unsuitable for the Store product.

Do not state that MSIX services are categorically impossible. Windows supports packaged services on sufficiently recent versions of Windows, but the current Store package targets Windows 10 1809 and this product deliberately keeps its Store distribution free of this administrative server role. See [Microsoft's MSIX service guidance](https://learn.microsoft.com/en-us/windows/msix/packaging-tool/convert-an-installer-with-services).

### 1.4 Editions never coexist

User and Server editions must not coexist as independent live installations. They would otherwise compete for the frozen IPC pipe, service name, port, and persistent host key.

- The Server installer detects a User edition and offers a **migration**, not side-by-side installation.
- The User installer detects a Server edition and offers a **return to User mode**, not a second worker.
- Silent installers must fail clearly if the other edition is present, unless an explicit, documented migration argument is supplied.
- The Share Manager may link to the Server download/instructions, but it must not silently download or execute an installer.

---

## 2. User-to-Server and Server-to-User exchange

The Share Manager exposes a clear **Hosting** section:

- **Install Server edition…** — opens the signed GitHub release/website page and explains that the Server installer migrates the existing share identity. It does not download/install in the background.
- **Server edition detected** — connects as a management console only; it never launches a second worker.
- **Return to User edition…** — opens the documented User installer/migration path.
- **Remove Server role** — an elevated, explicit action that stops and deletes the service and reverts the machine-side sharing capability without deleting the host key unless the user explicitly requests identity removal.

The Server installer owns the authoritative migration transaction:

1. stop the User worker cleanly over IPC and close the Share Manager;
2. copy and validate persistent state into the machine data directory;
3. preserve `hostkey`, credentials, roots, port, settings, usage/security logs, and any mode-independent state;
4. create the service account/data-directory/root ACLs and firewall rule;
5. register and start the SCM service, verify status and IPC reachability;
6. only then remove/retire the User edition registration.

If any state copy or key validation fails, migration stops before service registration and tells the user that re-pairing would otherwise be required. It must never silently generate a replacement host key. The reverse path has the same no-regenerate rule.

---

## 3. Worker service host

### 3.1 Required entry point

The Lite-shipped sidecar currently builds from `cmd/worker`, whose no-argument path runs the foreground worker. The Server edition adds explicit service dispatch:

```text
fms-share-worker.exe                         foreground/User edition behavior
fms-share-worker.exe --service ...           SCM service behavior
```

`--service` enters `golang.org/x/sys/windows/svc` (or a correctly adapted existing service wrapper), reports `StartPending` then `Running`, runs `service.Worker.Run(ctx)`, and cancels `ctx` on `Stop`/`Shutdown`. Cancellation already reaches the worker's normal shutdown path, which releases the SFTP listener, UPnP mapping, mDNS registration, IPC listener, and persistent statistics.

The service host must not merely ignore an error from `Worker.Run`. If the worker's main loop ends unexpectedly, the process/service must report failure and terminate so configured SCM recovery can restart it. A service left marked `RUNNING` with a dead worker is unacceptable.

The earlier dormant service implementation in `P:\windows\fms_companion\internal\service\install.go` is useful reference material only. It is not reused unchanged: it has a legacy service name/account/data-dir assumption and does not make the Lite `cmd/worker` sidecar SCM-aware.

### 3.2 Registration

The frozen SCM service name is:

```text
FastMediaSorterCompanionSFTP
```

Registration belongs to the Server installer and the explicitly elevated migration helper, not to an ordinary phone-facing worker command. The service configuration is:

- executable: the installed `companion\fms-share-worker.exe` with `--service`, explicit machine data directory, and management policy parameters;
- start: **delayed automatic** by default, so network initialization has time to complete;
- account: `NT AUTHORITY\LocalService` unless an administrator deliberately chooses a supported alternative;
- recovery: restart on process failure, with a bounded retry cadence and reset period;
- lifecycle: graceful `sc stop`/SCM stop before installer replacement or uninstall, with a bounded wait before any forceful fallback.

Automatic start alone is not enough for network-dependent discovery. The worker must retry mDNS/UPnP/reachability work after network availability changes or on a bounded backoff. SCM recovery only helps a failed process; it does not restart a healthy service that happened to start before networking was ready.

### 3.3 Service account and roots

`LocalService` is the default because it has low local privilege and presents anonymous credentials to remote servers. This has consequences that the UI must surface before enabling a root:

- User-profile folders and custom NTFS folders may need a read/execute grant for `LOCAL SERVICE`.
- A UNC root usually will not work under `LocalService`, because it presents anonymous network credentials. Such roots need an explicitly supported alternative account/deployment design; do not promise they work by default.
- The installer/helper grants only `(OI)(CI)(RX)` to the selected root for `LOCAL SERVICE`, records exactly what it added, and removes only that recorded ACE on unshare/remove when safe. It must never recursively rewrite unrelated ACLs or remove pre-existing administrator grants.
- The worker's root confinement remains mandatory. The service account decides what the worker can open; it never lets Android browse outside the roots explicitly configured in the worker.

Named-user or dedicated local service accounts are deferred options. They require a separate threat model, credential handling, and documented network-share behavior.

### 3.4 Machine state and identity preservation

The Server edition uses a machine directory such as:

```text
%ProgramData%\FastMediaSorterCompanion\
```

It contains `hostkey`, credentials, shares, port/settings, stats, and security logs. Its ACL permits only LocalService, SYSTEM, Administrators, and the explicitly enrolled Share Manager user SID(s), with the minimum rights each needs. It must not grant generic Users access: the directory contains the SFTP password and private host key.

Fresh Server installs generate their identity there. A mode/edition migration copies the existing identity only after validating it. A packaged Store install remains per-user/per-package and never uses this Server directory.

### 3.5 IPC control-plane security

The current pipe `\\.\pipe\fms-companion` remains schema v1 and continues to serve status, folder configuration, start/stop-serving, statistics, config export, and QR rendering.

Its DACL must not be derived from `os/user.Current()` when the server runs in Session 0: that identity is LocalService/SYSTEM, not the interactive Share Manager user. The Server installer persists the authorized management SID(s), and the worker builds the pipe DACL from those SIDs plus SYSTEM and Administrators. It must use narrowly scoped pipe rights, not an `Authenticated Users` or `Everyone` grant.

This is security-critical: IPC status/config export carries the SFTP credential. Windows enforces named-pipe DACLs when the client connects; see [Microsoft's named-pipe security guidance](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights).

### 3.6 Share Manager behavior in Server edition

In Server mode, Share Manager is a management console:

- it connects to the existing service worker; it never spawns/kills a worker process;
- normal **Start sharing** / **Stop sharing** remain `StartServer` / `StopServer` IPC operations, so routine sharing control does not require UAC;
- separate administrative controls expose service install, disable, repair, start/stop, and return-to-User migration;
- closing the manager/tray has no impact on the service;
- autostart of the console is optional and unrelated to server availability.

`ServerFeatures.HostMode()` is authoritative for this behavior. It queries the SCM service state and validates the Server edition registration before returning `SystemService`.

---

## 4. Installer profiles and state

### 4.1 Server installer

The Server installer is elevated and interactive by default. Its principal page says:

> Install the always-on Folder Share Server. It starts with Windows and can serve selected folders even when nobody is signed in. Administrator approval is required for the Windows service, firewall rule, data directory, and folder read permissions.

It selects the Share payload, creates the machine directory, configures the service, and opens Share Manager for first-time root setup. A user may decline to add roots; the service then runs but exposes nothing.

The installer must use a distinct Inno AppId, ARP display name, install directory, and output filename from the User edition so winget update correlation remains correct. The service name and on-disk persistent-state schema stay common/frozen across both editions.

### 4.2 User installer

The existing User installer remains the default, including its current silent/winget behavior. It must detect a Server edition before update/install and guide an interactive user through the supported migration. It must never overwrite the Server worker binary while its service is running.

### 4.3 Runtime elevation

All machine-affecting work is behind visible administrator approval:

- Server installation/migration: elevated Server installer;
- service repair/enable/disable and ACL changes: one short-lived elevated helper;
- service removal/uninstall: elevated uninstaller/helper.

No phone request, ordinary IPC command, or silent User edition install can create the service or broaden ACLs/firewall access.

---

## 5. Gate and mode state

```vb
Public Enum ServerHostMode
    None
    UserSession
    SystemService
End Enum
```

- `IsEnabled()` stays true only when the Share feature is legitimately available: existing consent state for User mode, packaged Store User mode, or a validated Server installation/service state.
- `HostMode()` distinguishes hosting behavior. The live SCM query is authoritative for `SystemService`.
- Server mode is machine state; no HKCU-only flag may claim a Server service exists.
- The user-visible state distinguishes **service installed**, **service running**, **SFTP serving**, and **no roots configured**. These are not interchangeable.

---

## 6. Documentation requirements

Publish and keep linked from both installers/Share Manager:

1. GitHub Server release notes and checksum.
2. Website Server landing page and Android pairing guide.
3. winget install/update/remove examples for `SerZhyAle.FastMediaSorter.Server`.
4. User-to-Server and Server-to-User migration instructions, including the promise that a successful migration keeps the same host-key fingerprint.
5. Folder-permission guidance, the LocalService UNC limitation, firewall scope, router/port-forward details, and how to validate externally after reboot with no login.
6. Backup/restore instructions for the machine state directory without exposing the private host key or credentials.
7. Clear Store wording: Store installs provide User mode only; Server is obtained from GitHub, the website, or winget.

Update `CLAUDE.md` and the Android-folder-share invariant text when implementation ships: an optional, explicit Windows service is permitted only for the Server edition. It is never created by Store delivery or by a default/silent User installation.

---

## 7. Planned files

**Worker repository `P:\windows\fms_companion`:**

- `cmd/worker/main.go` — explicit `--service` dispatch plus machine data-dir and management-SID arguments.
- `internal/service/` — SCM handler, graceful shutdown/error propagation, service state, network readiness/retry, and unit seams.
- `internal/ipc/` — server pipe security descriptor based on installed management SIDs; tests for Session-0-compatible authorization.
- `internal/sftpserver/` — deterministic data-dir selection and migration-safe validation.
- tests — service start/stop/error recovery, no-key-regeneration, data ACL/path selection, and IPC authorization.

**This repository:**

- `src/FastMediaSorterCompanion/Core/ServerFeatures.vb` — edition/service detection, `ServerHostMode`, and elevated management entry points.
- `src/FastMediaSorterCompanion/Core/WorkerProcess.vb` / `WorkerIpc.vb` — connect-only behavior in Server mode.
- `src/FastMediaSorterCompanion/Forms/*` and `TrayContext.vb` — management-console state and migration/download guidance.
- `publishing/installer/FastMediaSorter.iss` — User-edition conflict detection.
- `publishing/installer/FastMediaSorterServer.iss` (new, or shared Inno include/profile) — Server installer and migration transaction.
- `publishing/installer/install-share-service.ps1` (new) — elevated, auditable install/repair/remove/migration helper.
- `publishing/installer/stop-companion.ps1` — stop/wait for the SCM service before replacement/uninstall.
- `publishing/winget/` — new Server-package manifest set.
- `tools/Build-Installer.ps1` / release scripts — Server installer asset and checksum generation.
- docs/site — Server page, channel matrix, migration, security, and Store scope.

---

## 8. Acceptance criteria

1. **Fresh Server installation:** after UAC approval and root configuration, `sc query FastMediaSorterCompanionSFTP` reports `RUNNING`; the program-scoped firewall rule exists; a reboot with no login leaves the phone able to browse the configured root.
2. **User installation unchanged:** no SCM service is created by the existing GitHub installer, existing winget package, or Store MSIX; user-session behavior remains unchanged.
3. **GitHub/website/winget:** Server installer asset, Server landing page, and `SerZhyAle.FastMediaSorter.Server` all resolve to the same Server profile; normal updates preserve the machine host key.
4. **Migration User → Server:** one explicit elevation/migration path preserves host-key fingerprint, credentials, roots, port, and stats; paired phones reconnect without re-pairing; no second worker is spawned.
5. **Migration Server → User:** service stops/deletes cleanly; User worker resumes from preserved identity; paired phones still connect.
6. **Permissions:** a configured NTFS root is readable by LocalService only through the recorded grant; a non-authorized local user cannot connect to the control pipe or obtain credentials; unsupported UNC roots fail with a precise explanation.
7. **Network readiness:** after boot before networking is ready, the worker retries reachability/discovery; a later available network yields valid status/advertisement without an interactive restart.
8. **Uninstall:** stops and deletes the service before binary removal; removes only app-owned firewall/ACL state; leaves no listening port or orphaned SCM registration; identity deletion is an explicit separate choice.
9. **Store MSIX:** remains User mode only; no Server installer link/action is offered inside the packaged build.
10. **Build gate:** worker `go test ./...`; Companion/LITE Release build; Server installer compile; isolated VM tests for fresh install, migration both directions, reboot-without-login, update, and uninstall. No `v*` tag is created as part of development work.

---

## 9. Invariants

- One SFTP implementation, IPC protocol, QR/config format, Android reconnect contract, and persistent identity across both editions.
- The worker remains the sole owner of SFTP, credentials, host key, mappings, network policy, and statistics.
- Host key and credentials are never regenerated on a successful migration, service restart, update, reboot, or mode change.
- Server installation is explicit, elevated, and auditable; it is never a side effect of a User installation, a phone request, or a silent default.
- Only the Server edition may register `FastMediaSorterCompanionSFTP`; Store delivery never does.
- User and Server editions do not run concurrently.
