# Technical Specification: Android Folder Share (Companion mode)

> Status: draft for implementation
> Date: 2026-07-10
> Branch context: main
> Derived from: FastMediaSorter Android project spec S0421 (Level A) and its reference implementation `P:\windows\fms_companion` (Go, tests green, e2e-proven against the shipped Android importer on 2026-07-10). The standalone companion product is discontinued; this spec ports its full functionality into Fast Media Sorter for Windows.

---

## 1. Goal

Add a "Share folders with your Android" feature to Fast Media Sorter for Windows: the user picks folder(s) on this PC, the app runs a secure SFTP server for them in the background (surviving app close and, optionally, reboot), and exports a QR code / `.fmscfg` file that the FastMediaSorter Android app imports in one action (Add resource -> SFTP/FTP -> Import from companion). The Android side is ALREADY SHIPPED and device-verified - this spec is only the Windows half.

**Architecture in one sentence:** LITE does NOT implement SFTP in VB. It drives a bundled headless sidecar, `fms-share-worker.exe` (Go, prebuilt, vendored in `payload/companion/`), over a named-pipe JSON control channel; the worker owns the SFTP server, keys, credentials, folder confinement, mDNS announce, UPnP/NAT-PMP port mapping, reachability and QR/config rendering.

**Why a sidecar (verified 2026-07-10):** there is no production-grade free SFTP *server* library for .NET Framework 4.8 - SSH.NET is client-only, FxSsh is net8.0-only and hobby-grade, Rebex File Server costs ~$499/dev. The Go worker is already written, unit-tested (18 tests), and proven end-to-end against the real Android importer (434-file browse over live SFTP, pinned host key). Reusing it costs one 9 MB exe in the payload.

**What this is not:**
- Not a VB reimplementation of SFTP, SSH crypto, UPnP or QR encoding - all of that stays in the worker.
- Not a Windows service - blocked by Microsoft Store policy (10.2.4, packagedServices restricted capability) and unnecessary: a logon-session background process covers the home scenario.
- Not the P2P "connect by ID" mode (Level B of the original spec) - out of scope, future phase.
- Not a change to the Android app or to the `.fmscfg` contract - both are frozen and shipped.

**Success definition:** on a PC with LITE installed, the user opens Settings -> Share tab, adds a folder, presses Start; a phone with FastMediaSorter on the same LAN imports the exported QR/`.fmscfg` and browses/plays the folder contents; sharing survives LITE being closed (worker keeps running) and, with the autostart toggle on, survives reboot. All three distribution channels (portable ZIP, Inno/winget, Store MSIX) ship the feature without breaking their existing validation constraints.

---

## 2. Verified current state (facts checked 2026-07-10)

### 2.1 LITE codebase integration points

| Hook point | Anchor | Relevance |
|---|---|---|
| Feature-slice pattern: new `Main_Form.<Feature>.vb` / `Table_Form.<Feature>.vb` partial + `<Compile Include>` in vbproj | [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) Compile items ~161-290; precedent [DONE/SPECIFICATION_OCR_TRANSLATION_OVERLAY.md](done/SPECIFICATION_OCR_TRANSLATION_OVERLAY.md) | New files MUST be registered by hand - no glob include |
| Settings tab: CONTENT built at runtime, but the TabPage container itself is a Designer artifact (`Tab_Page_5` created in [src/Table_Form.Designer.vb](../../src/Table_Form.Designer.vb) lines 39/143/202-210/631; `BuildOcrTabIfNeeded` early-returns if the page is missing) and the tab TITLE is localized in [src/Table_Form.vb](../../src/Table_Form.vb) `LngCh` (~lines 210/252) | [src/Table_Form.Ocr.vb](../../src/Table_Form.Ocr.vb) lines 83-99 (`PrepareOcrTabForDisplay`/`SelectOcrTab`), 137-267 (`BuildOcrTabIfNeeded`), 323+ (`LocalizeOcrTab`) | The Share tab copies the runtime-content pattern; the new TabPage is created fully IN CODE (see Phase 2.1 - avoids hand-editing the auto-generated Designer.vb) |
| Per-feature settings POCO over registry | [src/OcrTranslateSettings.vb](../../src/OcrTranslateSettings.vb) lines 186-208 (`ReadString/WriteBool` wrappers over `GetSetting/SaveSetting`, HKCU `...\SZA\FastMediaSorter`) | `ShareSettings.vb` mirrors this |
| Bilingual UI via `Is_Russian_Language` + per-form `LngCh()` | [src/Main_Form.Localization.vb](../../src/Main_Form.Localization.vb) lines 16-104; [src/Common_Module.vb](../../src/Common_Module.vb) line 5 | All new strings need RU + EN literals |
| Hidden child-process spawn precedent | [src/Translate/OllamaManager.vb](../../src/Translate/OllamaManager.vb) lines 47-74 (`StartServer` with `CreateNoWindow`, detect via `Process.GetProcessesByName`) | Worker spawn copies this |
| Single-exe model: managed deps embedded as `FmsPayloadmanaged/*` resources + `AssemblyResolve` | [src/RuntimeBootstrap.vb](../../src/RuntimeBootstrap.vb) lines 21-85; vbproj lines ~345-373 | The worker is a NATIVE exe - it is NOT embedded; it ships as a payload file next to the app exe |
| No existing server sockets, no NotifyIcon, no autostart mechanism | grep over src/ = 0 hits (verified) | Everything network-server-side is new - hence the sidecar |
| Option Strict On + WarningsAsErrors | [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) (41999, 42016-42022, 42032, 42036) | VB snippets below are written fully typed |

### 2.2 Distribution channels (all four must keep working)

| Channel | Install scope | Autostart mechanism | Firewall | Anchor |
|---|---|---|---|---|
| Portable ZIP (GitHub) | none (unzip & run) | HKCU `...\CurrentVersion\Run` value, written by the app at runtime (opt-in toggle) | OS consent prompt on first listen | [.github/workflows/release.yml](../../.github/workflows/release.yml) staging step |
| Inno installer (GitHub + winget) | **PrivilegesRequired=lowest with overrides allowed** (default flow: per-user, non-elevated; dialog/cmdline may elevate; winget VALIDATION runs elevated/machine) | same HKCU Run value, written by the app at RUNTIME only - an installer-written Run value under an elevated/other-user install lands in the wrong HKCU hive, and autostart must stay an opt-in toggle (note: the .iss already does gated HKCU writes for file associations, lines 217-259, and passes winget - registry writes per se are not the problem) | same OS prompt | [installer/FastMediaSorter.iss](../../installer/FastMediaSorter.iss) lines 27, 39-40 |
| winget | consumes the Inno exe silently; manifest deliberately has NO Scope/Dependencies (prior validation failures 0x8A150044) | as above | as above | [winget/SerZhyAle.FastMediaSorter.installer.yaml](../../winget/SerZhyAle.FastMediaSorter.installer.yaml); [SPECIFICATION_WINGET_PUBLISHING.md](SPECIFICATION_WINGET_PUBLISHING.md) lines 24-62 |
| Store MSIX | full-trust desktop bridge, `rescap:runFullTrust` only; built by [msix/build-msix.ps1](../../msix/build-msix.ps1), uploaded unsigned, Microsoft-signed | `uap5` `windows.startupTask` manifest extension ONLY (an HKCU Run write from inside the package is VIRTUALIZED and silently never autostarts) | `desktop2` `windows.firewallRules` manifest extension (install-time rules, no UAC, survives updates) | [msix/AppxManifest.xml](../../msix/AppxManifest.xml) lines 27-30, 64-89 |

Store-policy hard facts (verified against learn.microsoft.com, 2026-07-10):
- Windows services in consumer Store MSIX: effectively NOT approvable (`packagedServices` "in most cases won't be approved"; policy 10.2.4). Design has no service anywhere.
- Runtime elevation in Store apps needs `allowElevation` - approval-gated, realistically unobtainable. The Store build must NEVER elevate; the `windows.firewallRules` manifest extension replaces netsh.
- `uap5:StartupTask` for full-trust packaged apps is supported and may point at a DIFFERENT executable in the package (the worker), letting the worker autostart without the main window.
- A long-running sidecar keeps the package "in use" and can delay MSIX updates - see risk table.
- Store listing texts (description, runFullTrust justification, privacy policy - currently "no network requests except optional outbound translation") MUST be updated in the same submission; see [STORE_PUBLISHING.md](../guides/STORE_PUBLISHING.md) lines 110-211. IARC: "user-to-user sharing" is a re-rating trigger; answer honestly (local-network file access by the same user).

### 2.3 The vendored worker (prebuilt, already in this repo)

- `payload/companion/fms-share-worker.exe` - headless Go worker, 9.0 MB, SHA256 in the `.sha256` sidecar. Built from `P:\windows\fms_companion` @ `cmd/worker` with `go build -ldflags="-H=windowsgui -s -w"`. Rebuild instructions: Appendix C.
- Worker owns and persists (in `%LOCALAPPDATA%\FastMediaSorterCompanion\`, or `--datadir <dir>` override): ed25519 host key (`hostkey`, TOFU-pinned by Android - NEVER regenerate), generated credential (`credential.json`), shared folders (`shares.json`), stable listen port (`settings.json`). Port caveat: if the persisted port is occupied at start, the worker falls back to a fresh OS-assigned port and re-persists it - previously exported configs must be re-exported (the tab always shows the current port).
- Worker behavior on start: restores persisted shares and (if any) starts the SFTP server on the persisted port, announces `_sftp-fms._tcp` on mDNS, attempts UPnP-IGD/NAT-PMP port mapping with lease renewal, computes reachability (LAN address, external address, CGNAT detection) asynchronously.
- Control channel: named pipe `\\.\pipe\fms-companion`, one JSON request/response per connection, schema-versioned. Full protocol: Appendix A. Working PowerShell reference client (same .NET APIs VB will use - verified live 2026-07-10, full cycle incl. QR PNG export): Appendix A.3.
- The worker exposes an `ExportConfig` request (returns the `.fmscfg` JSON + a rendered QR PNG), but LITE does NOT use it: the config and QR are built on the LITE side by `ShareConfigBuilder` (QRCoder) from the worker's live Status, because `ExportConfig` cannot advertise a manual port forward or the schema v2 per-root params. Contract summary: Appendix B.

### 2.4 The Android side (context only - nothing to do)

Shipped and device-verified 2026-07-10: "Import from companion" in Add resource -> SFTP/FTP reads `.fmscfg` (plain JSON or `FMSCFG1:`+base64(gzip)), creates one read-only SFTP resource per shared root with the TOFU-pinned host key. Contract frozen; canonical vector byte-identical in both repos (Appendix B).

---

## 3. Invariants

1. The same `FastMediaSorter_LITE.exe` ships in all channels with no code fork - channel differences are handled at RUNTIME by package-identity detection (packaged vs unpackaged) and in the PACKAGING assets (manifest/installer), never by `#If` builds.
2. The worker exe is a sibling payload: `<app dir>\companion\fms-share-worker.exe` in every channel layout. The VB code resolves it relative to `Application.ExecutablePath` only.
3. Never regenerate or touch the worker's data dir contents from VB - the host key is pinned by phones; deleting it breaks every paired device.
4. No elevation anywhere at runtime. No Windows service anywhere. No installer-written autostart or firewall steps in the Inno script - autostart is a runtime opt-in (an install-time HKCU write can land in the wrong hive when the install is elevated/machine-scoped, as winget validation is), and firewall needs rights the default per-user flow does not have.
5. IPC schema version is 1; the VB client sends it on every request and surfaces a mismatch as "update the app" - never silently ignores it.
6. All user-visible strings exist in RU and EN (`LngCh` pattern); RU prose follows house style (plain hyphen, ё where correct, `..` not `...`).
7. Every new `.vb` file gets a `<Compile Include>` entry in [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj); build gate is `msbuild Release -> 0 errors, 0 new warnings` before each commit.
8. No `v*` tag, no `tools/Release.ps1 -Push` as part of this work - release is a separate owner-confirmed operation.

---

## 4. Work plan

### Phase 1 - Worker client plumbing (no UI). Risk: low

| # | Item | Files | Notes |
|---|---|---|---|
| 1.1 | `Companion/WorkerIpc.vb` - named-pipe JSON client | new | `NamedPipeClientStream(".", "fms-companion", InOut)`, UTF-8, one request/response per connection, 5s connect timeout. Serialize with `System.Web.Script.Serialization.JavaScriptSerializer` - `System.Web.Extensions` is ALREADY referenced ([src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) line 117), just `Imports`. Deserialization is case-INSENSITIVE (verified against reference source + live test), so response DTOs may use normal PascalCase VB property names; for requests, either name DTO properties camelCase (legal in VB) or rely on Go's equally case-insensitive unmarshal. Request DTO: `schemaVersion=1`, `type`, optional `folders`. Response DTO mirrors Appendix A.2. |
| 1.2 | `Companion/WorkerProcess.vb` - locate/spawn/stop | new | Resolve `companion\fms-share-worker.exe` next to `Application.ExecutablePath`; `EnsureRunning()`: try pipe connect, on failure spawn hidden (copy [src/Translate/OllamaManager.vb](../../src/Translate/OllamaManager.vb) lines 57-74 pattern, `UseShellExecute=False`, `CreateNoWindow=True`) and poll the pipe up to ~5s; `StopWorker()`: send `StopServer`, then kill the tracked/named process. |
| 1.3 | `ShareSettings.vb` - registry POCO | new | Keys under the house registry path via `GetSetting/SaveSetting` wrappers (copy [src/OcrTranslateSettings.vb](../../src/OcrTranslateSettings.vb) lines 186-208): `Share_AutostartEnabled` (Boolean, default False), `Share_WorkerEverStarted` (Boolean, telemetry-free UX hint). Shared-folder list itself is NOT duplicated here - the worker persists it. |
| 1.4 | vbproj registration + build | [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) | Three `<Compile Include>` entries. Gate: Release build green. |

### Phase 2 - Share tab UI. Risk: medium (largest slice)

| # | Item | Files | Notes |
|---|---|---|---|
| 2.1 | `Table_Form.Share.vb` - new settings tab | new | Content-building mirrors [src/Table_Form.Ocr.vb](../../src/Table_Form.Ocr.vb) (`BuildShareTabIfNeeded()`, `PrepareShareTabForDisplay()`, `LocalizeShareTab()`), BUT unlike the OCR tab (whose `Tab_Page_5` lives in the auto-generated Designer.vb - which house rules say not to hand-edit, [CLAUDE.md](../../CLAUDE.md) ~line 181) the Share TabPage is created fully in code: `BuildShareTabIfNeeded()` news up `Tab_Page_6 As TabPage`, adds it to `Tab_Control.Controls`, then builds the controls. Add the tab title to BOTH `LngCh` branches in [src/Table_Form.vb](../../src/Table_Form.vb) (~lines 210/252) or set it inside `LocalizeShareTab`. Controls: shared-folders ListView + Add (FolderBrowserDialog) / Remove buttons; Start/Stop sharing button; status labels (running, port, LAN address, internet path yes/no, host-key fingerprint); QR PictureBox; "Save .fmscfg.." button (SaveFileDialog, filter `*.fmscfg`); autostart CheckBox; a one-line hint area (firewall prompt expectation / manual-forward hint / CGNAT explanation from the worker). |
| 2.2 | Wire tab to worker | same + Phase 1 classes | Tab open -> `EnsureRunning()` + `GetStatus`; Add/Remove -> `SetSharedFolders` (full list each time); Start -> `StartServer`, then poll `GetStatus` ~1/s until reachability appears (worker computes it async, typically <6s), then `ExportConfig` -> decode `qrPngBase64` into the PictureBox (`Convert.FromBase64String` + `MemoryStream` + `Image.FromStream`); Save button writes `configJson` text to the chosen path. Surface `lastError` from status. |
| 2.3 | Localization | `Table_Form.Share.vb` (`LocalizeShareTab`) | RU + EN literals for every control + hints. RU copy provided inline in the file; follow house prose rules. |
| 2.4 | Autostart toggle (channel-aware) | `Companion/AutostartManager.vb` (new) | Detect package identity via P/Invoke `kernel32!GetCurrentPackageFullName` (returns `APPMODEL_ERROR_NO_PACKAGE` = 15700 when unpackaged - verified correct API). Unpackaged: write/delete quoted worker path under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `FastMediaSorterShare`. Packaged: the manifest StartupTask (Phase 3) is authoritative and the checkbox is READ-ONLY/explanatory ("managed by Windows: Settings > Apps > Startup") - this is the DEFAULT plan, because calling WinRT `Windows.ApplicationModel.StartupTask` from .NET Framework 4.8 requires the `Microsoft.Windows.SDK.Contracts` NuGet (Microsoft's documented route) and even then a user-disabled task cannot be re-enabled programmatically (`RequestEnableAsync` will not override Task Manager). WinRT interop is an optional later enhancement. |
| 2.5 | vbproj registration + build + smoke | vbproj | Smoke: tab opens, folder add/remove round-trips (visible in worker `GetStatus`), Start serves (see acceptance), QR renders. |

### Phase 3 - Packaging: ship the worker in every channel. Risk: medium

| # | Item | Files | Notes |
|---|---|---|---|
| 3.1 | Local build deploy | [build.ps1](../../build.ps1) | NOTE: build.ps1 currently deploys "the exe alone" (comment at lines 60-62; copies only `FastMediaSorter_LITE.exe` to `C:\GD\i\` and `C:\GD\tc\SZA\_APP\`) - this feature deliberately extends that contract. Extend the destination loop to also mirror `payload/companion\` next to each deployed exe; alternatively scope the worker deploy to `bin\Release` only and accept that the `C:\GD` targets lack the Share feature (owner call at implementation time). |
| 3.2 | CI staging | [.github/workflows/release.yml](../../.github/workflows/release.yml) staging step | Stage `payload/companion/fms-share-worker.exe` into `stage/...-windows-x64/companion/` so both ZIP and Inno pick it up. Verify SHA256 against the sidecar during staging (fail the build on mismatch). |
| 3.3 | Inno payload | [installer/FastMediaSorter.iss](../../installer/FastMediaSorter.iss) `[Files]` | Add the `companion\` subdir. NO registry/firewall/service entries. Uninstall: also kill a running `fms-share-worker` process before file removal (`[UninstallRun]` taskkill or InnoSetup code section), and remove the HKCU Run value if present (mirror the file-association cleanup pattern, lines 342-362). |
| 3.4 | MSIX payload + manifest | [msix/build-msix.ps1](../../msix/build-msix.ps1), [msix/AppxManifest.xml](../../msix/AppxManifest.xml) | Stage `companion\` into the package payload. Manifest: add `uap5` + `desktop2` namespaces; `uap5:Extension Category="windows.startupTask"` with `Executable="companion\fms-share-worker.exe"` (relative path is the documented form for startupTask), `EntryPoint="Windows.FullTrustApplication"`, `TaskId="FmsShareWorker"`, `Enabled="true"`; `desktop2:Extension Category="windows.firewallRules"` with `Executable="fms-share-worker.exe"` - NAME ONLY, no path (documented form; a subfolder path is unproven), rules TCP in, profile private (discuss public at submission). CAVEAT: a manifest startup task arms only after the app has been launched at least once (documented StartupTask behavior) - the Share-tab first-use flow satisfies this naturally. Verify with `-SelfSign` sideload. |
| 3.5 | Store texts | [STORE_PUBLISHING.md](../guides/STORE_PUBLISHING.md) checklist | Update description, runFullTrust justification, privacy policy (local-network SFTP server, credentials generated and stored locally, nothing leaves the user's devices), IARC re-check. This is a submission-time task - record it in the doc now. |

### Phase 4 - Docs & closure. Risk: very low

| # | Item | Files |
|---|---|---|
| 4.1 | CHANGELOG `[Unreleased]` RU entry (Added) | [CHANGELOG.md](../../CHANGELOG.md) |
| 4.2 | READMEs trilingual feature bullet + docs/index.html at release time | README.md / README_RU.md / README_UK.md |
| 4.3 | CLAUDE.md: document the sidecar (payload/companion, pipe protocol pointer, "never regenerate hostkey") | [CLAUDE.md](../../CLAUDE.md) |
| 4.4 | Move this spec to DONE/ with `> Outcome:` note | this file |

---

## 5. Ordering

Strictly 1 -> 2 -> 3 -> 4. Within Phase 3, 3.1-3.2 before 3.3-3.4 (staging feeds both). Phase 2 is testable end-to-end with a real phone before any packaging work - do that smoke first.

---

## 6. Risk watch list

| Risk | Mitigation |
|---|---|
| Firewall: on a standard-user account the Windows consent prompt creates BLOCK rules regardless of the click - LAN peers silently cannot connect | Share-tab hint states admin approval is needed once (unpackaged); MSIX channel avoids it entirely via manifest firewall rules. If LAN connect fails, status hint suggests checking Defender Firewall inbound rules |
| MSIX: running sidecar delays package updates | Acceptable for MVP; note in CLAUDE.md. Future: worker exits when no shares configured |
| `JavaScriptSerializer` numeric-type quirks (returns Integer/Long/Decimal depending on magnitude) | Type DTO numeric members explicitly (`Integer` for ports/versions); integration smoke covers every request type. Case sensitivity is a NON-issue: deserialize matches property names case-insensitively (BindingFlags.IgnoreCase in the reference source; empirically verified on 4.8.9337.0) |
| Worker port fallback: if the persisted port is occupied at start, the worker silently rebinds to a fresh OS-assigned port and re-persists it - previously exported QR/`.fmscfg` configs then point at a dead port | Share tab always shows the CURRENT port from `GetStatus`; hint tells the user to re-export after the port changed; acceptance includes a re-export path |
| MSIX pipe namespace: one learn.microsoft.com bullet says packaged apps "must use `\\.\pipe\LOCAL\`" - over-broad (targets AppContainer; full-trust mediumIL processes are exempt per the same page), but it is a primary-source statement | Verify pipe connect early in the `-SelfSign` sideload smoke (already in acceptance); fallback if it ever fails: coordinated rename to `LOCAL\fms-companion` on worker + client |
| Worker exe flagged by AV heuristics (unsigned Go binary opening sockets) in ZIP/winget channels | Known posture of this repo (unsigned exe, prior Wacapew.A!ml false-positive history); Store channel is Microsoft-signed. Do not pack/obfuscate the worker |
| Stale worker from a previous app version after update | `WorkerProcess.EnsureRunning` compares pipe schema version; on mismatch kills the named process and spawns the sibling exe |
| Two LITE instances / two workers | Worker's pipe is a singleton by name - second worker fails fast on pipe bind and exits; client just connects to the existing one |
| `hostkey` deleted by user cleanup tools | Status shows fingerprint; docs note pairing must be redone (Android shows host-key-mismatch error by design) |

---

## 7. Out of scope / future work

- Level B "connect by ID" P2P mode (rendezvous server) - separate future spec.
- QR-scan pairing from the LITE side, multi-user/service mode. (Write-enabled shares shipped with schema v2: destination roots (`isDestination`) are served writable - see Appendix B.)
- Signing the GitHub/winget binaries; Linux companion.
- Localizing the worker's own strings (hints arrive EN from the worker; the VB layer may translate the two known hint kinds by pattern later).

---

## 8. Acceptance checklist

- [ ] All new `.vb` files registered in vbproj; `msbuild Release` -> 0 errors, 0 new warnings.
- [ ] Share tab: add folder -> Start -> status shows running + port + LAN address; QR renders; `.fmscfg` saves.
- [ ] Phone smoke (the definitive test): FastMediaSorter Android -> Add resource -> SFTP/FTP -> Import from companion -> pick the saved `.fmscfg` -> resource opens, files browse, a video plays with seek.
- [ ] Close LITE window -> phone still browses (worker survives the app).
- [ ] Autostart ON + reboot (unpackaged install) -> share reachable after logon without opening LITE.
- [ ] Autostart toggle OFF removes the Run value (unpackaged).
- [ ] Inno per-user silent install (`/VERYSILENT`) still exits 0 and adds/removes the `companion\` payload cleanly; uninstall kills the worker.
- [ ] MSIX `-SelfSign` sideload: app runs, worker spawns and the pipe connects (early check - see pipe-namespace risk row), StartupTask visible in Task Manager > Startup AFTER launching the app once (manifest tasks arm on first launch), firewall rules present (`Get-NetFirewallRule` filtered by the package), phone import works; reboot-autostart tested after that first launch.
- [ ] CHANGELOG `[Unreleased]` RU entry added; CLAUDE.md updated.
- [ ] This file moved to DONE/ with `> Outcome:` note.

---

## Appendix A - Worker IPC protocol (v1)

### A.1 Transport

Named pipe `\\.\pipe\fms-companion` (ACL: SYSTEM, Administrators, current user). One JSON request -> one JSON response per connection; the worker closes after responding. UTF-8, no length prefix - read until the peer closes or the JSON object completes.

### A.2 Messages

Request envelope:

```json
{"schemaVersion":1,"type":"GetStatus"}
{"schemaVersion":1,"type":"SetSharedFolders","folders":[{"name":"Photos","hostPath":"C:\\Users\\me\\Pictures","readOnly":true}]}
{"schemaVersion":1,"type":"StartServer"}
{"schemaVersion":1,"type":"StopServer"}
{"schemaVersion":1,"type":"ExportConfig"}
```

Response envelope (fields absent when not applicable):

```json
{
  "schemaVersion": 1,
  "ok": true,
  "status": {
    "running": true, "listenPort": 63442,
    "fingerprint": "SHA256:..", "username": "fms", "password": "..",
    "roots": [{"name":"Photos","hostPath":"C:\\..","readOnly":true}],
    "lastError": "",
    "reachability": {
      "lanAddress":"192.168.1.100","lanMdnsActive":true,
      "portMapMethod":"upnp","externalHost":"..","externalPort":63442,
      "isCgnat":false,"manualForwardHint":""
    }
  },
  "export": {
    "configJson": "{..the exact .fmscfg content..}",
    "qrPngBase64": "iVBOR..",
    "hasInternetPath": true,
    "manualForwardHint": "",
    "fileExtension": ".fmscfg"
  }
}
```

Rules: `SetSharedFolders` replaces the whole list and persists it (restarts a live server). `StartServer` with zero folders sets `lastError`. `ExportConfig` fails (`ok=false`) until the server runs and reachability produced at least one access path - poll `GetStatus` first. Schema-version mismatch returns `ok=false` with an explanatory `error`. Note `error` carries `omitempty` - it is ABSENT on success, never an empty string (`lastError` inside `status` is always present).

### A.3 Reference client (PowerShell, verified live 2026-07-10)

The exact .NET calls VB will make; full cycle GetStatus -> SetSharedFolders -> StartServer -> ExportConfig (QR PNG decoded, 2526 bytes) -> StopServer ran green against the vendored worker:

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'fms-companion', [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect(5000)
$bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"type":"GetStatus"}')
$pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()
# ..read to end, ConvertFrom-Json (see P:\ANDROID\FastMediaSorter_mob_v2\temp\S0421\pipe_client_demo.ps1)
```

## Appendix B - `.fmscfg` contract (FROZEN - do not touch)

Authoritative doc: `P:\windows\fms_companion\docs\CONFIG_FORMAT.md`. The Android parser ships against the canonical vector below (byte-identical fixture in the Android repo, `app_v2/src/test/resources/companion/canonical_vector.json`; Go serializer test `TestCanonicalVector`). The exported config is built on the LITE side by `ShareConfigBuilder` (the worker's own `ExportConfig` is unused - it cannot advertise a manual port forward); any schema change is still a cross-repo breaking change and needs a `schemaVersion` bump on BOTH ends:

```json
{"schemaVersion":1,"resourceName":"Home PC","protocol":"sftp","accessPaths":[{"kind":"lan","host":"192.168.1.23","port":2022},{"kind":"portforward","host":"203.0.113.7","port":2022}],"username":"fms","password":"k7PmQ2wXr9TzS4vGnHb3JdLe","hostKeyFingerprintSha256":"SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk","roots":[{"virtualPath":"/Photos","label":"Photos"},{"virtualPath":"/Music","label":"Music"}],"createdAt":"2026-07-10T12:00:00Z"}
```

The QR payload variant is plain JSON when small, else `FMSCFG1:` + base64(gzip(json)); when even the compressed payload exceeds QR capacity, LITE shows "share the .fmscfg file instead" and never truncates.

**Schema v2 (S1002, 2026-07):** each `roots[]` entry may additionally carry the target resource's configuration - `profile` (e.g. `audio_library`), `mediaTypes`, scan conditions (`scanSubdirectories`, `showSubfoldersAsItems`, `showHiddenFiles`, `allFiles`), `isDestination` (+ optional `destinationColor`; the folder is then shared writable), `comment`, `accessPin`, `slideshowInterval`. Per-root params are edited via "Настроить.." on the Share tab (`Share_Root_Params_Form`, stored per host path by `ShareRootParamsStore` in the registry) and emitted by `ShareConfigBuilder` only when they differ from the Android import defaults. `schemaVersion` is adaptive: `2` only when at least one v2 field is present, else `1` (old Android apps keep importing unconfigured shares; a real v2 file makes them ask for an app update). Frozen v2 canonical vector: `TestCanonicalVectorV2` in the Go repo + Android fixture `canonical_vector_v2.json`; the Android-side contract is `P:\ANDROID\FastMediaSorter_mob_v2\PLAN\S1002_companion-config-v2-resource-params\COMPANION_EXPORT_SPEC.md`.

## Appendix C - Rebuilding the worker

Source repo: `P:\windows\fms_companion` (Go 1.26+, reference implementation of the discontinued standalone companion; also holds the Go test suite and the Android e2e harness `cmd/devserve`).

```powershell
cd P:\windows\fms_companion
go vet ./... ; go test ./...          # 18 tests must stay green
go build -ldflags="-H=windowsgui -s -w" -o build\bin\fms-share-worker.exe .\cmd\worker
Copy-Item build\bin\fms-share-worker.exe P:\WINDOWS\FastMediaSorter_Lite\payload\companion\
Get-FileHash P:\WINDOWS\FastMediaSorter_Lite\payload\companion\fms-share-worker.exe -Algorithm SHA256   # update the .sha256 sidecar
```

Never change the pipe name, the IPC schema (without bumping `IPCSchemaVersion` on both sides), or anything under `internal/config` (frozen contract; v2 additions are optional-field-only and covered by `TestCanonicalVectorV2`).
