# Specification - Android Folder Share: optional Windows **system service** server (install-time choice)

> Status: **in development / design** (2026-07-15). No code yet. This spec proposes a second way to run the Share server - as a real Windows service registered with the SCM - and an install-time choice between it and today's user-session model, with the trade-offs that make each the right pick.
> Scope split across two repos: the Go worker (`P:\windows\fms_companion`, build there - never from this repo) grows an SCM-aware entry point; the LITE/Companion side (`src/FastMediaSorterCompanion/`, `installer/`) grows the install-time choice, the gate state, and the management-console behavior.
> Related: [SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](done/SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md) (the opt-in gate this extends), [SPECIFICATION_SHARE_SECURITY_HARDENING.md](done/SPECIFICATION_SHARE_SECURITY_HARDENING.md), [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md), [SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md), [CONTRACT_ANDROID_RECONNECT.md](../contracts/CONTRACT_ANDROID_RECONNECT.md).

---

## 0. Why this exists (the real problem)

Today the Share server runs **only while a user is logged in and the Companion tray app is running**. `fms-share-worker.exe` is a child process that the Companion spawns; the Companion is a per-user, logon-autostart (`--tray`) tray app. That model is right for the common case - a personal PC where the owner shares a folder to their own phone for a while - and it is the only model the Microsoft Store allows (MSIX cannot host a Win32 service; see section 6).

But the opt-in spec already pinned down the *other* audience: **"the target is often a dedicated server with a direct public IP, not a home PC"** ([SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](done/SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md) §0.2). On such a box the user-session model has a real gap:

1. **No share across a reboot until someone logs in.** A headless/dedicated server reboots (patch Tuesday, power blip) and the folder is *unreachable* until a human RDPs in and lets the Companion autostart to the tray. For a box whose entire job is to serve folders, "up only while I'm logged in" is the wrong default.
2. **The worker inherits an interactive session's lifetime.** Log off -> the session ends -> the worker dies -> the share drops. Fine for a personal PC (that is arguably a feature - privacy), wrong for a server.
3. **`Session 0` / RDP nuance.** On Windows Server the console session and RDP sessions come and go; anchoring an always-on network service to whichever interactive session happens to exist is fragile.

A Windows **service** is the OS-blessed answer to "run a network server without a logged-in user, start at boot, restart on crash": it runs in Session 0 under a service account, is managed by the SCM, and is exactly what an SFTP server on a dedicated box should be.

So this spec adds a **second mode** and lets the user pick it **at install time** (with a runtime path too), while keeping today's model as the default. It is a further, larger, owner-approved exception to the standing "no Windows service anywhere" rule (section 7) - **scoped to non-Store channels only**, because the Store build genuinely cannot ship a service.

---

## 1. The two modes (this is the "either / or")

| | **A. User-session server** (today, default) | **B. System service server** (new, opt-in) |
|---|---|---|
| Who runs the worker | Companion tray app spawns it as a child | The SCM runs it as a Windows service |
| When it is up | While the user is logged in **and** Companion is running | From **boot**, before/without any login; restarts on crash |
| Survives reboot | Only after a login + Companion autostart | **Yes**, unattended |
| Survives logoff | No (share drops) | **Yes** |
| Runs as | The logged-in user (the user's own file rights) | A **service account** (see §3.3) - not the user |
| Admin needed | Once, only for the firewall exception (opt-in) | Once, for the firewall exception **and** `sc create`/`delete` |
| Data dir (host key, creds, stats) | `%LOCALAPPDATA%\FastMediaSorterCompanion\` (per user) | A **machine** location (see §3.4) - no user profile in Session 0 |
| Companion's role | Owns the worker (spawn/stop) | **Management console** only - connects to the running service, never spawns/kills it |
| Ships in Store MSIX | **Yes** | **No** - MSIX cannot host a service (§6) |
| Best for | A personal PC sharing to its owner's phone occasionally | A dedicated / headless / always-on box, a rented VPS, a "home server" |

**One-sentence rule of thumb, shown to the user at the choice point:** *pick the service if this machine's job is to keep the folder reachable even when nobody is logged in; keep the user-session server if you just want to share from your own PC while you use it.*

Both modes reuse the **same** `fms-share-worker.exe`, the **same** `.fmscfg`/QR export path, the **same** firewall rule, the **same** Android reconnect contract, and are both gated behind `ServerFeatures.IsEnabled()`. The service is not a new server - it is the existing worker hosted by the SCM instead of by the Companion.

---

## 2. Current reality (what the code does today)

- **Worker entry point**: `cmd/worker` -> `service.Worker.Run()` (the Go package is even *named* `service`, but it currently runs as a plain foreground process driven over the `\\.\pipe\fms-companion` control channel; it does **not** talk to the SCM).
- **Lifecycle owner**: the Companion (`WorkerProcess`/`WorkerIpc`) spawns and stops the worker; `TrayContext` keeps the Companion resident so the worker survives a window-close. `installer/stop-companion.ps1` kills both before a file-replacing install/uninstall (RM cannot close a tray app or a windowless worker).
- **Opt-in gate** (`ServerFeatures.IsEnabled()`): true when the machine marker file, the HKCU `Share_ServerFeaturesEnabled` flag, **or** packaged (Store). The one privileged step it performs is the **program-scoped inbound firewall allow** for the worker exe.
- **Autostart**: `AutostartManager` writes an HKCU Run value targeting `FastMediaSorterCompanion.exe --tray`.
- **Data dir**: `%LOCALAPPDATA%\FastMediaSorterCompanion\` holds the TOFU-pinned ed25519 host key, credentials, `stats.json`. **Never regenerate it** - paired phones pin the host key.
- **Installer**: `installer/FastMediaSorter.iss` already has the elevated firewall add/delete + marker write (`RegisterServerFeatures`/uninstall), a selectable `share` component, and the post-install Companion launch.

Everything above is mode A. Mode B adds a service host without disturbing any of it.

---

## 3. Design

### 3.1 SCM-aware worker (Go, `P:\windows\fms_companion` - build there)

Add a service dispatch path to the existing worker, using `golang.org/x/sys/windows/svc`. The **same** `service.Worker` runs either way:

- `fms-share-worker.exe` (no args) -> today's foreground/pipe-driven mode (unchanged). Detect "am I launched by the SCM?" with `svc.IsWindowsService()`; if false, run exactly as now.
- Under the SCM (`svc.IsWindowsService()` true, or an explicit `--service` guard) -> run inside `svc.Run(name, handler)`. The handler:
  - reports `StartPending -> Running`, starts `service.Worker.Run()` on a goroutine,
  - keeps the **same** named-pipe control channel open (so the Companion console and `stop-companion.ps1` still talk to it),
  - on `svc.Stop`/`svc.Shutdown` -> graceful worker shutdown (release SFTP listener + UPnP mapping, same path as `StopServer`), report `Stopped`.
- New thin subcommands used by the installer/console (not by phones): `--install-service`, `--uninstall-service`, `--service` (SCM entry). These wrap `sc`-equivalent registration, or the installer can call `sc.exe` directly (§3.2) - **decide in Open decision E** whether registration lives in Go or in the installer/`.ps1`.

**No SFTP/protocol change.** IPC schema stays v1. The reconnect contract is unaffected - a service restart looks to the phone exactly like the worker restart it already handles.

### 3.2 Service registration (who runs `sc create`)

Program-scoped, auto-start, restart-on-failure:

```
sc create  FastMediaSorterCompanionSFTP ^
    binPath= "\"<app>\companion\fms-share-worker.exe\" --service" ^
    start= auto ^
    obj= "NT AUTHORITY\LocalService"  (or the account chosen per §3.3) ^
    DisplayName= "Fast Media Sorter - Folder Share (SFTP)"
sc description FastMediaSorterCompanionSFTP "Serves selected folders read-only to Android over SFTP."
sc failure  FastMediaSorterCompanionSFTP reset= 86400 actions= restart/5000/restart/5000/restart/60000
```

- **Elevated** - the installer is already elevated for `{autopf}`; the runtime path spawns one UAC helper (§4.3), mirroring the firewall helper.
- **Service name** `FastMediaSorterCompanionSFTP` is a **new frozen anchor** (like the mutex/AppId) - once shipped, never rename it or `sc upgrade` correlation breaks. Reuse the existing firewall rule name unchanged.
- **Uninstall / disable** -> `sc stop` (wait) then `sc delete`, alongside the existing firewall-rule delete and marker cleanup. `stop-companion.ps1` gains a service-aware branch (stop the service instead of killing a child worker).

### 3.3 Service account (the security fork - Open decision A)

The service does **not** run as the interactive user, so folder access is the account's, not the user's. Options, least- to most-privileged:

1. **`NT AUTHORITY\LocalService`** (recommended default). Least privilege; presents anonymous creds on the network. **Caveat that must be surfaced:** it cannot read a user's profile folders (`C:\Users\<me>\Pictures`) without an explicit ACL grant. On a dedicated server the shared media usually lives on a data drive/UNC the service can be granted read on; for a personal-PC user picking service mode, the enable flow must **grant the service account read on each shared root** (`icacls <root> /grant "LOCAL SERVICE":(OI)(CI)RX`) and warn.
2. **A dedicated low-privilege local account** (`fms-share$`, `LogonAsService` right, random password). More isolation, more moving parts (account creation, password, ACLs). Overkill for most; note as an option.
3. **The installing user / a named account** the admin types. Matches "share my own files" without extra ACLs, but runs the network server as a full user - worse blast radius. Only if the user insists.

**Confinement is unchanged** either way - the worker still restricts every session to the shared roots (lexical + real-path re-check, §3.6 of the hardening spec). The account only decides *what the worker itself can open on disk*; it does not widen what a phone can reach beyond the shared roots.

### 3.4 Data directory relocation (host-key migration - Open decision B)

Session 0 has no `%LOCALAPPDATA%` for the user. The service's host key + credentials + `stats.json` must live in a **machine** location the service account can read/write, e.g. `%ProgramData%\FastMediaSorterCompanion\` (ACL'd to the service account) or the service profile (`C:\Windows\ServiceProfiles\LocalService\AppData\Local\...`). Recommend `%ProgramData%` for legibility and easy ACL grant.

**The host key is TOFU-pinned by paired phones - it must not silently change when a user switches modes.** So:

- **Fresh install -> service mode**: the service generates its host key in the machine dir on first start (no phones paired yet - fine).
- **User-session (paired) -> service mode later**: the enable flow **migrates** the existing `%LOCALAPPDATA%\...\hostkey` + credentials into the machine dir before starting the service, so paired phones keep working (same fingerprint, same password). Never regenerate. If migration can't happen (source unreadable), warn that phones will need re-pairing rather than silently minting a new key.
- **Service -> user-session**: symmetric, or leave the machine key in place and have the user-mode worker read it (Open decision B: single shared machine dir for both modes vs. per-mode dirs + migration). A **single machine data dir used by both modes** is the simpler, migration-free design and is the recommendation - the only cost is the machine dir must be created/ACL'd even in user mode.

### 3.5 Companion becomes a management console in service mode

In mode B the Companion **must not** spawn or kill the worker (the SCM owns its lifecycle). It instead:

- Detects service mode (service installed -> §5 gate state) and connects to the already-running worker over the **same** pipe for status/stats/config/QR - all read/drive operations work unchanged (the pipe is mode-agnostic).
- Replaces "Start/Stop server" with "Start/Stop **service**" (routes to `sc start`/`sc stop`, which may need elevation) or hides Start/Stop entirely and shows service status + a "Manage in Services.." affordance. **Open decision C.**
- Still builds `.fmscfg`/QR on the LITE/Companion side (unchanged - the export path never depended on who hosts the worker).
- Tray icon: still useful as a status/console launcher, but **closing it must not stop the share** (the service is independent) - so the "close hides to tray to keep the worker alive" rationale weakens to "console convenience". Autostart of the *console* becomes optional in service mode (the service is the real autostart).
- `WorkerProcess.IsAvailable()`/spawn code is bypassed in service mode; guard it so the console never double-hosts a worker.

### 3.6 Firewall

**Unchanged** - the existing program-scoped inbound allow for `fms-share-worker.exe` covers the service too (the rule keys on the exe, not on who launched it). Service mode does **not** add a second rule. If service mode is chosen at install, the firewall step and the service registration happen in the same elevated pass.

---

## 4. Install-time & runtime choice (the UX)

### 4.1 Installer wizard (Inno, non-Store)

On the existing custom `InstallOptionsPage`, when the `share` component / "server features" is chosen, present a **radio choice** (default = mode A):

- ( ) **Run only while I'm signed in** (recommended for a personal PC) - *the Share Manager runs the server; it stops when you sign out.*
- ( ) **Run as a background Windows service** (recommended for a server / always-on PC) - *the folder stays reachable after a reboot, even with nobody signed in. Needs administrator rights and installs a Windows service.*

Rules:
- Default **A**. B is only actionable when Setup is elevated (installs to `{autopf}` already are); grey B with "requires administrator install" otherwise (mirrors opt-in Open decision B).
- Choosing **B** on install performs, in the post-install elevated step: firewall add (existing) + create the machine data dir + ACL it + `sc create ... start=auto` + optional first `sc start` + write the marker (mode recorded, §5). Choosing **A** is exactly today's behavior.
- **Silent / winget**: the radio is skipped; silent installs are **always mode A, viewer-only opt-in off** - no service, no `sc`, no surprise (winget validation unaffected, §6). Never register a service in a silent install.
- **Uninstall**: if a service is registered, `sc stop`+`sc delete` before removing files (via the service-aware `stop-companion.ps1`), then the existing firewall/marker cleanup.

### 4.2 Component/Tasks alternative

Could be an Inno `[Tasks]` entry ("Install as a background service") instead of a radio; a radio reads clearer as a mutually-exclusive either/or and is preferred. **Open decision D.**

### 4.3 Deferred runtime switch (in-app)

From the Settings > Share enablement surface, offer "Run as a Windows service.." for a user who installed mode A and later wants always-on. It spawns one UAC helper (`install-share-service.ps1`, sibling to `enable-share-server.ps1`) that: stops a running user-mode worker, migrates the data dir (§3.4), `sc create`+`start`, records the mode. Symmetric "Stop running as a service" reverts (`sc delete`, optionally hand the worker back to the Companion). Exactly one UAC prompt each way, never silent - same pattern as `EnableViaElevation()`.

---

## 5. Gate & mode state

Extend `ServerFeatures` with an explicit **mode**, not just enabled/disabled:

```vb
Public Enum ServerHostMode
    None          ' not enabled
    UserSession   ' mode A - Companion hosts the worker (today)
    SystemService ' mode B - the SCM hosts the worker
End Enum
Public Function HostMode() As ServerHostMode
```

- `IsEnabled()` stays true for **either** mode (marker OR HKCU flag OR packaged OR **service registered**). Add "service registered" (query the SCM for `FastMediaSorterCompanionSFTP`) as a fourth truthy source, so a service install lights up the Share UI even without the marker/flag.
- `HostMode()` drives the console behavior in §3.5 (spawn vs. connect-only).
- Packaged (Store) can only ever be `UserSession` (or `None`) - `SystemService` is unreachable there by construction (§6).

Persist the chosen mode in the marker file's contents (e.g. `mode=service`) and/or an HKLM/HKCU value; the live SCM query is authoritative.

---

## 6. Channel impact (why the Store can't have this)

- **Microsoft Store / MSIX**: **service mode is impossible and must be hidden.** MSIX packages cannot register or host a classic Win32 Windows service - the package runs in a per-user container, `runFullTrust` notwithstanding, and Store policy forbids services outright (this is the very reason the whole feature is "no Windows service anywhere" today). Packaged builds stay **mode A only**; the installer radio does not exist there; `HostMode()` can never be `SystemService`. This asymmetry is acceptable and expected - the Store audience is the personal-PC audience, which wants mode A anyway.
- **winget**: manifest unchanged (direct Inno `setup.exe`, `InstallerType: inno`, no deps, no `Scope`). The radio is skipped under `WizardSilent` -> silent installs are mode A -> validation installs a viewer with no service. Do **not** add `AppsAndFeaturesEntries` or a dependency. `winget upgrade` correlation (ARP name) is untouched.
- **Inno installer (GitHub) & portable ZIP**: the only channels that can offer mode B. Portable has no installer, so mode B there is runtime-only via the UAC helper (§4.3); default remains mode A.

---

## 7. Invariant impact (explicit, larger override)

CLAUDE.md and the Android-share specs currently state **"No Windows service anywhere (blocked by Store policy)"** and Invariant #4 ("no runtime elevation / no installer firewall steps"). The opt-in spec already scoped the firewall exception. This spec scopes a **second, larger** owner-approved exception:

- A Windows **service may be registered** - but **only** by the elevated Inno installer when the user explicitly chose mode B, or by a single UAC-prompted runtime helper. **Never in a silent/winget install, never in the Store build, never silently.**
- The "no service anywhere" line must be re-scoped to **"no service in the Store/MSIX build; an optional, user-chosen service in the Inno/portable channels."** Update CLAUDE.md's "Android Folder Share" section and [SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md) Invariant #4 when this ships.
- Still no *silent* elevation; still no service in the Store; the default install is still mode A (nothing changes for the vast majority).

---

## 8. Files to touch (planned)

**Worker repo `P:\windows\fms_companion` (build there):**
- `cmd/worker` / `internal/service` - `svc.IsWindowsService()` branch, `svc.Run` handler wrapping `Worker.Run()`, graceful stop on `svc.Stop/Shutdown`, keep the pipe. `--service`/`--install-service`/`--uninstall-service` subcommands (or leave registration to the installer - Open decision E).
- Data-dir resolution - machine location when running as a service (§3.4).
- Tests: service-start/stop lifecycle (mock SCM handler), data-dir selection, host-key migration no-regenerate.

**This repo `src/FastMediaSorterCompanion/`:**
- `Core/ServerFeatures.vb` - `ServerHostMode`/`HostMode()`, SCM query as a fourth truthy source, `InstallAsService()`/`UninstallService()` via a UAC helper.
- `Core/WorkerProcess.vb` / `WorkerIpc.vb` - in service mode, connect-only (never spawn/kill).
- `Forms/*` + `TrayContext.vb` - console-mode UI (§3.5): service status, Start/Stop-service vs. hidden, tray no longer required to keep the share alive.
- `installer/FastMediaSorter.iss` - the mode radio (§4.1), elevated `sc create`/data-dir/ACL on mode B, silent-skip, `sc stop`+`delete` on uninstall.
- `installer/stop-companion.ps1` - service-aware stop branch.
- `installer/install-share-service.ps1` (new) - elevated `sc create`/migrate/ACL for the runtime switch; symmetric `-Uninstall`.
- Docs/site + `docs/guides/STORE_PUBLISHING.md` - clarify service is GitHub/portable-only, not Store.

**Docs:** CLAUDE.md + `SPECIFICATION_ANDROID_FOLDER_SHARE.md` invariant re-scope (§7).

---

## 9. Open decisions

- **A. Service account** - `LocalService` + per-root ACL grant (recommended), a dedicated low-priv account, or a named user. Default: `LocalService` with an ACL grant on each shared root and a warning.
- **B. Data dir** - single machine dir shared by both modes (recommended, migration-free) vs. per-mode dirs with host-key migration on switch. Either way: never regenerate the pinned host key.
- **C. Console Start/Stop** - route Start/Stop to `sc start`/`sc stop` (needs elevation) vs. hide server controls in service mode and show status + "Manage in Services.." only. Default: show status + one elevated Start/Stop-service.
- **D. Installer surface** - radio (mutually-exclusive, recommended) vs. a `[Tasks]` checkbox. Default: radio.
- **E. Registration owner** - `sc.exe` driven from the Inno script + `.ps1` (simpler, auditable) vs. `--install-service` inside the Go worker (self-contained). Default: installer/`.ps1`.
- **F. Restart-on-failure policy** - `sc failure` restart cadence values. Default as in §3.2, tune later.

---

## 10. Acceptance criteria (when built)

1. **Fresh install, mode B, admin granted** (dedicated-server case): `sc query FastMediaSorterCompanionSFTP` shows `RUNNING`, `start=auto`; firewall rule present; after a **reboot with no login**, a phone on cellular connects and browses the shared folder (the core win over mode A).
2. **Fresh install, mode A** (default): no service (`sc query` -> not found); behaves exactly as today; Store/winget/portable unaffected.
3. **Runtime switch A -> B**: one UAC prompt registers the service, migrates the host key (paired phone keeps working - **same fingerprint, no re-pair**), and the Companion becomes a console that no longer spawns a worker. B -> A reverts cleanly.
4. **Uninstall in mode B**: service stopped + deleted, firewall rule removed, machine data dir handled per policy, no orphaned listening port (external TCP probe closed).
5. **Store MSIX build**: the service option is absent; `HostMode()` never returns `SystemService`; packaged behavior is mode A only.
6. **winget silent install**: mode A, no service, no elevation surprise; upgrade correlation intact.
7. **Build gate**: worker `go test ./...` green (service lifecycle + data-dir + no-regenerate tests); `msbuild FastMediaSorter.sln /p:Configuration=Release` -> 0 errors, 0 new warnings; no `v*` tag as part of this work.

---

## 11. Invariants preserved

- The worker remains the **sole** owner of SFTP, credentials, host key, mapping, stats, and network policy in **both** modes; only *who launches it* changes. LITE still knows nothing about it (migration invariant 8).
- The TOFU-pinned host key and stored credentials are **never regenerated** across a mode switch, reboot, or service restart - reinforced by §3.4's migration rule.
- The `ServerFeatures` opt-in gate still gates the whole Share surface; service mode is a *host* of an already-opted-in server, not a way around the gate.
- One firewall rule, program-scoped, added only with explicit consent; removed on uninstall/disable. No new firewall surface.
- No service in the Store build; no silent elevation; default install unchanged (mode A).
