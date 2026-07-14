# Specification - Opt-in "SFTP server features" (install-time and deferred)

> Status: **implemented** (2026-07-12). Open decisions resolved to the recommended defaults (A keep the button visible, B grey the box on a per-user install, C autostart offered/on, D the `enable-share-server.ps1` helper). Build gate: `msbuild Release -> 0 errors`.
> Related: [SPECIFICATION_ANDROID_FOLDER_SHARE.md](SPECIFICATION_ANDROID_FOLDER_SHARE.md), [SPECIFICATION_QR_IMPORT_ANDROID.md](SPECIFICATION_QR_IMPORT_ANDROID.md).
>
> Shipped: `src/Companion/ServerFeatures.vb` (gate + `EnableViaElevation`), `src/Share_Enable_Form.vb` (deferred opt-in dialog), the enablement panel + gate in `src/Table_Form.Share.vb`, gated entry points in `src/Main_Form.ShareWizard.vb` / `.KeyboardInput.vb` / `.ShareTray.vb`, the read-only mirror in `src/ShareSettings.vb`, the enable copy in `src/Companion/ShareText.vb`, the installer checkbox + firewall/marker steps in `installer/FastMediaSorter.iss`, and the elevated helper `installer/enable-share-server.ps1`.

---

## 0. Why this exists (the real problem)

The Android Folder Share feature ships in every channel, but its server side has never been reachable out of the box, and a device test on 2026-07-12 pinned down why:

1. **Nothing opens the Windows Firewall port in the LITE-driven flow.** The worker *has* an `AddFirewallRule()` (`internal/app/onboarding.go`), but it is only called from `App.FirstRunSetup()` - the discontinued standalone-companion GUI onboarding. The shipped `fms-share-worker.exe` is built from `cmd/worker` -> `service.Worker.Run()`, which **never calls it**. So the SFTP listen port is blocked by Windows Firewall on any machine that did not manually allow it. The exported config advertised a public address that then timed out from outside - exactly the "Downloads" symptom (`193.178.50.43:64048` unreachable).

2. **The target is often a dedicated server with a direct public IP, not a home PC behind a router.** On such a box there is no LAN, no router, no UPnP, and mDNS (multicast, LAN-scoped) cannot reach a phone on a different network - all irrelevant. The single thing that makes it reachable is **an inbound firewall allow for the worker exe**. Once the port is open, the worker's own `localLANIP()` already returns the machine's directly-bound public IP as the primary `accessPaths[0]`, and the phone connects from anywhere.

3. **Sharing is a security-relevant, admin-level action that should be a deliberate opt-in, not a silent always-on capability.** Exposing folders over SFTP + punching a firewall hole must be something the user explicitly chose, with a clear "what and why", and must degrade to a pure viewer/sorter when they did not.

This spec adds that opt-in: at install time (a new wizard checkbox) and later (from inside the app), each performing the one privileged step that actually matters - the firewall exception - and gating the Share UI behind it.

This **deliberately overrides CLAUDE.md Invariant #4** ("no installer-written firewall steps, no runtime elevation") for this one scoped case, with owner approval. See section 8.

---

## 1. Current reality (what the code does today)

- **Worker binary is always bundled** at `<app dir>\companion\fms-share-worker.exe` in every channel (portable ZIP, Inno, Store MSIX). `WorkerProcess.IsAvailable()` = "that file exists" and is effectively always true in a real install.
- **Share entry points are not gated.** The toolbar "Поделиться / Share" button is built unconditionally (`Main_Form.ModernLayout.vb:98` -> `BuildShareToolbarControls`), the folder-box right-click item and `Shift+S` are always wired, and the Settings "Поделиться / Share" tab (`Tab_Page_6`, `Table_Form.Share.vb`) is always created. `IsAvailable()` is only used to enable/disable controls *within* the Share UI and to gate the tray probe - never to hide the feature.
- **The installer already elevates in the common case.** `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog commandline`, installing to `{autopf}` (Program Files). The field log confirms `C:\Program Files\FastMediaSorter_LITE\...`, i.e. the user's installs are machine-wide/elevated - so an install-time firewall step is feasible for them.
- **No firewall rule is ever added by the LITE flow** (see section 0.1). The port is closed unless the user allowed it by hand.
- **Consent/UX state lives in `ShareSettings`** (HKCU `...\SZA\FastMediaSorter`): `Share_AutostartEnabled`, `Share_WorkerEverStarted`, `Share_ExternalAccessIntent`, `Share_LanOnlyExport`, `Share_ExcludePassword`. There is no "server features enabled" concept yet.

---

## 2. Goal - three states

| State | How reached | App behavior |
|---|---|---|
| **Enabled at install** | New installer checkbox ticked + admin granted | Firewall exception added, consent recorded machine-side, worker present. On first run the full Share UI is visible; the user can start sharing immediately. |
| **Viewer only** | Checkbox left off, or admin declined, or silent/winget install | App is a pure image/video viewer and sorter. Share **functionality** (folder list, start/stop, QR, tray) is hidden. A single discoverable **opt-in** affordance remains (Settings and the Share button's dialog) so the user can enable it later. |
| **Enabled later (deferred)** | User opts in from inside the app | A confirmation dialog explains what/why, one UAC prompt adds the firewall exception, consent is recorded (this user's HKCU), and the full Share UI appears live without a restart. |

Uninstall reverses the privileged change (removes the firewall rule) alongside the existing cleanup.

---

## 3. Design

### 3.1 The enablement gate (single source of truth)

Introduce a new module `Companion\ServerFeatures.vb` exposing:

```vb
Public Module ServerFeatures
    Public Function IsEnabled() As Boolean   ' the gate the whole UI reads
    Public Function CanEnable() As Boolean   ' worker payload present (nothing to enable otherwise)
    Public Function MarkerPath() As String   ' <app dir>\companion\server-features.enabled
    ' Runtime enable/disable (deferred opt-in path) - see 3.4
    Public Function EnableViaElevation(rus As Boolean) As ServerEnableResult
    Public Sub RecordEnabledForThisUser()    ' writes the HKCU flag
End Module
```

`IsEnabled()` returns **true when EITHER**:

- **Machine marker present**: the file `<app dir>\companion\server-features.enabled` exists (written by the elevated installer), **OR**
- **Per-user flag set**: HKCU `...\SZA\FastMediaSorter\Share_ServerFeaturesEnabled = 1` (written by the deferred runtime opt-in for the actual logged-in user).

Two sources because of a hive hazard already documented for autostart: an **elevated machine install runs under the elevating admin's profile, so an install-time HKCU write can land in the wrong hive**. The installer therefore records consent as a **machine-side marker file** (hive-independent, readable by the non-elevated app, removed with `{app}` on uninstall). The runtime path, which already runs as the real user, writes the **HKCU flag**. Either is sufficient; both mean "consented + firewall configured".

`IsEnabled()` is cheap (a `File.Exists` + one registry read) and is called during layout, so cache it once per process with a public `Refresh()` the deferred-opt-in path calls after enabling.

> Note (multi-user): a machine marker enables the Share UI for every user of that machine - consistent with the firewall rule itself being machine-global. Acceptable, and the intended behavior on a single-user dedicated server. Not a regression (the feature was previously always-on for everyone).

### 3.2 What "install server features" actually does

Minimal, and each step is idempotent:

1. **Worker present** - already true (always bundled). No download, no extract. (Rejected alternative: ship the worker only on opt-in - would force a runtime download for the deferred path, break offline installs, and complicate winget. The dormant ~8.6 MB binary is harmless; it never runs unless LITE spawns it, and LITE only spawns it when the gate is on.)
2. **Firewall exception** - a **program-scoped** inbound allow for the worker exe (survives the worker's dynamic listen port changing):
   ```
   netsh advfirewall firewall add rule
     name="FastMediaSorter Companion SFTP"
     dir=in action=allow
     program="<app dir>\companion\fms-share-worker.exe"
     protocol=TCP profile=domain,private,public enable=yes
   ```
   Program-scoped (not port-scoped) matches the worker's own persisted-port model. `profile=domain,private,public` so a dedicated server (whose network is usually classified Public) is covered. Removed on uninstall / disable.
3. **Record consent** - marker file (install path) or HKCU flag (runtime path), per 3.1.
4. **Autostart (offered, not forced)** - reuse `AutostartManager`. On a server the user typically wants the worker to auto-run at logon so shares survive reboot. Default the autostart sub-option **on** in the enable flow, but keep it a visible, reversible choice (it is still the existing HKCU Run opt-in, written at runtime only).

No Windows service (Store-policy-forbidden, and unnecessary). No silent elevation anywhere - every privileged step is behind a visible UAC prompt or the already-elevated installer.

### 3.3 UI gating by state

The gate (`ServerFeatures.IsEnabled()`) controls visibility:

| Surface | Enabled | Viewer-only (disabled) |
|---|---|---|
| Toolbar "Поделиться / Share" button | Visible -> opens the share wizard | Visible -> opens the **enablement dialog** (3.4). *(See Open decision A - alternative is to hide it entirely.)* |
| `Shift+S` hotkey | Share wizard | No-op, or opens the enablement dialog (match the button) |
| Folder-box right-click "Share this folder.." | Share wizard | Enablement dialog |
| Settings -> "Поделиться / Share" tab (`Tab_Page_6`) | Full sharing UI (as today) | Tab still present, body replaced by the **enablement panel** (explainer + "Установить функции сервера.." button). This is the primary discoverable opt-in home ("заглянет в настройки на закладку Поделиться"). |
| Tray icon | Appears while serving (as today) | Never appears (worker never runs) |

Rationale for keeping the button/tab discoverable rather than fully hidden: the user explicitly named both "the Share tab in settings" and "the Share button dialog" as opt-in entry points. The *functionality* (serving, folder list, QR) is what is gated; the entry stays visible so the feature is discoverable. Open decision A lets this flip to fully-hidden if preferred.

### 3.4 Deferred runtime opt-in (the in-app path)

A new small modal `Share_Enable_Form` (or a reused confirmation), shown from the disabled-state button/tab:

1. **Explain** (what/why, RU/EN/UK - copy in 5.3): a background SFTP server lets an Android phone browse this PC's folders read-only; enabling adds a Windows Firewall exception and needs administrator rights once; folders you share become reachable over the network.
2. **Confirm** -> run the elevated firewall step. The app is non-elevated, so spawn a **bundled elevated helper** rather than self-elevating the whole app:
   - Ship `installer/enable-share-server.ps1` (sibling to `stop-companion.ps1`), installed to `{app}`. It runs the `netsh` add-rule from 3.2 for a worker path passed as `-ExePath`, and exits non-zero on failure.
   - Launch it elevated: `ShellExecute("runas", "powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File ""{app}\enable-share-server.ps1"" -ExePath ""<worker>""")`. Exactly one UAC prompt.
   - (A direct elevated `netsh` via `runas` is an acceptable simpler variant; the script is preferred - auditable, and can carry the matching remove/`-Disable` path.)
3. **On helper success** (exit 0): `ServerFeatures.RecordEnabledForThisUser()` (HKCU flag) + `Refresh()`, reveal the full Share UI live (re-point the toolbar button, swap the Settings tab body to the real UI), then optionally offer to start sharing the current folder right away.
4. **On UAC-declined / helper failure**: stay disabled, show a short message, **do not** flip the flag. The user can retry.

### 3.5 Install-time opt-in (the wizard path)

In `installer/FastMediaSorter.iss`:

- **New checkbox** on the existing custom `InstallOptionsPage` (below the image-association block), default **unchecked**. Copy in 5.1/5.2.
- **Elevation**: the box is only actionable when Setup is (or becomes) elevated. Because installs to `{autopf}` already elevate, this is the normal path. If Setup is running per-user/non-elevated (user chose a per-user dir via the privileges dialog), the firewall step cannot run: either (i) grey the checkbox with a hint "requires administrator install", or (ii) let it trigger the same runtime helper on first launch. Recommend (i) for predictability; note (ii) as Open decision B.
- **Perform on install** (only if checked AND elevated), in `[Run]` or `CurStepChanged(ssPostInstall)`:
  - `netsh ... add rule ...` (3.2), program-scoped to `{app}\companion\fms-share-worker.exe`.
  - Write the **marker file** `{app}\companion\server-features.enabled` (hive-safe consent record).
- **Silent / winget / Store**: the checkbox defaults off and is **skipped in silent installs** (`WizardSilent`), so `winget`/CI validation installs viewer-only with no elevation/firewall surprises - winget and Store flows are unaffected (see section 9). Never add an `AppsAndFeaturesEntries`/dependency for this.
- **Uninstall** (`CurUninstallStepChanged`, alongside the existing association cleanup): `netsh ... delete rule name="FastMediaSorter Companion SFTP"`. The marker file goes with `{app}`.

### 3.6 Go worker

No behavioral change required for this spec. Notes:

- `internal/app/onboarding.go` `AddFirewallRule()`/`RemoveFirewallRule()` remain the standalone-GUI onboarding's code and are **not** on the LITE path. The authoritative firewall step is owned by the installer + the runtime helper (both on the Windows/LITE side) so it happens where elevation naturally exists. Leave the Go functions in place for the standalone build; do not wire them into `cmd/worker`.
- The mDNS UDP-5353 firewall rule added to `AddFirewallRule()` earlier is only meaningful for LAN discovery and only in that dead-for-LITE path; it is harmless but irrelevant to the server scenario. No action.
- The already-shipped reachability/`localLANIP()` hardening stands. On a direct-IP server it yields the public IP as `accessPaths[0]`, which becomes reachable the moment the firewall rule from this spec is in place - closing the loop on the field failure.

---

## 4. Files to touch

| Area | File | Change |
|---|---|---|
| Gate | `src/Companion/ServerFeatures.vb` (new) | `IsEnabled()`/`CanEnable()`/marker + HKCU flag + `EnableViaElevation()` + `Refresh()` |
| Settings POCO | `src/ShareSettings.vb` | add `ServerFeaturesEnabled` (HKCU `Share_ServerFeaturesEnabled`) load/save |
| Toolbar/entry points | `src/Main_Form.ShareWizard.vb` | button click + right-click + `OpenShareWizard` branch on the gate; live re-point after enable |
| Layout | `src/Main_Form.ModernLayout.vb` | keep building the button; visibility/behavior per gate |
| Hotkey | `src/Main_Form.KeyboardInput.vb` | `Shift+S` branch on the gate |
| Tray | `src/Main_Form.ShareTray.vb` | unchanged (already keyed on worker Running); no icon when never started |
| Settings tab | `src/Table_Form.Share.vb` | render enablement panel vs. full UI by the gate; "enable" button -> `EnableViaElevation` |
| Enable dialog | `src/Share_Enable_Form.vb` (new, optional) | explainer + confirm; or fold into the Share tab panel |
| Installer | `installer/FastMediaSorter.iss` | new checkbox + copy; firewall add on install (elevated+checked); marker file; firewall delete on uninstall; silent-skip |
| Installer helper | `installer/enable-share-server.ps1` (new) | elevated `netsh` add/remove for a passed `-ExePath` |
| vbproj | `src/FastMediaSorter.vbproj` | `<Compile Include>` for each new `.vb` |
| Docs | `CLAUDE.md`, `SPECIFICATION_ANDROID_FOLDER_SHARE.md` Invariant #4 | record the scoped override (section 8) |

---

## 5. User-visible copy (RU / EN / UK - house style: plain hyphens, ё, `..`)

### 5.1 Installer checkbox label

- RU: `Установить функции сервера общего доступа к папкам (SFTP-сервер для Android)`
- EN: `Install folder-sharing server features (SFTP server for Android)`
- UK: `Встановити функції сервера спільного доступу до папок (SFTP-сервер для Android)`

### 5.2 Installer explainer (under the checkbox)

- RU: `Добавляет небольшую фоновую службу, которая позволяет телефону Android открывать и просматривать папки этого ПК по сети (только чтение, по протоколу SFTP). При включении в брандмауэр Windows добавляется разрешение для этой службы, поэтому во время установки потребуются права администратора. Оставьте выключенным, чтобы установить только просмотрщик и сортировщик изображений и видео - включить общий доступ можно позже в разделе «Настройки > Поделиться».`
- EN: `Adds a small background service that lets an Android phone open and browse this PC's folders over the network (read-only, over SFTP). Turning it on adds a Windows Firewall exception for the service, so setup needs administrator rights. Leave it off to install just the image and video viewer/sorter - you can turn sharing on later in Settings > Share.`
- UK: `Додає невелику фонову службу, яка дозволяє телефону Android відкривати й переглядати папки цього ПК по мережі (лише читання, за протоколом SFTP). Під час увімкнення до брандмауера Windows додається дозвіл для служби, тож встановлення потребує прав адміністратора. Залиште вимкненим, щоб встановити лише переглядач і сортувальник зображень та відео - увімкнути спільний доступ можна пізніше в розділі «Налаштування > Поділитися».`

### 5.3 In-app enablement panel/dialog

- Title RU: `Включить общий доступ к папкам` / EN: `Enable folder sharing` / UK: `Увімкнути спільний доступ до папок`
- Body RU: `Чтобы делиться папками с телефоном Android, нужен небольшой фоновый SFTP-сервер и разрешение в брандмауэре Windows. Это требует прав администратора один раз. После включения папки, которые вы выберете, станут доступны для чтения по сети. Продолжить?`
- Body EN: `To share folders with an Android phone, a small background SFTP server and a Windows Firewall exception are needed. This asks for administrator rights once. After it is on, the folders you pick become readable over the network. Continue?`
- Body UK: `Щоб ділитися папками з телефоном Android, потрібні невеликий фоновий SFTP-сервер і дозвіл у брандмауері Windows. Це один раз потребує прав адміністратора. Після ввімкнення вибрані папки стануть доступними для читання по мережі. Продовжити?`
- Buttons: RU `Установить функции сервера..` / `Отмена`; EN `Install server features..` / `Cancel`; UK `Встановити функції сервера..` / `Скасувати`.
- Success RU: `Готово. Общий доступ включён.` / failure RU (UAC declined): `Общий доступ не включён - не получены права администратора.`

---

## 6. Security & honesty

- Enabling makes chosen folders reachable over the network; the explainer states this before the UAC prompt. No folder is shared until the user adds one - enabling only stands up the server capability + firewall hole.
- Keep the existing QR/`.fmscfg` password-secret warning and the "exclude password" safeguard.
- The firewall rule is program-scoped and inbound-allow only; removed on uninstall/disable. No port is left open after the feature is removed.
- Still no silent elevation: install-time uses the wizard's own elevation, runtime uses one explicit UAC prompt.

---

## 7. Acceptance criteria

1. **Fresh install, checkbox ON, admin granted** (dedicated-server case): firewall rule present (`netsh advfirewall firewall show rule name="FastMediaSorter Companion SFTP"` lists it); marker file present; first launch shows the full Share UI; adding a folder + Start makes the exported config's primary address reachable from an external network (phone on cellular connects and browses). This is the concrete fix for the 2026-07-12 "Downloads" failure.
2. **Fresh install, checkbox OFF**: no firewall rule; no marker; app runs as viewer/sorter; no tray, no functional sharing; the Share tab shows only the enablement panel and the toolbar button opens the enablement dialog.
3. **Deferred opt-in**: from state (2), enabling via the dialog shows exactly one UAC prompt, adds the rule, flips the gate, and reveals the full Share UI live (no restart). Declining leaves everything disabled and unflagged.
4. **Silent/winget install**: no elevation/firewall action, viewer-only; winget validation unaffected (section 9).
5. **Uninstall**: firewall rule removed; marker gone; no orphaned open port.
6. **Build gate**: `msbuild Release` -> 0 errors, 0 new warnings; every new `.vb` registered in the vbproj. No `v*` tag as part of this work.

---

## 8. Invariant impact (explicit override)

CLAUDE.md / SPECIFICATION_ANDROID_FOLDER_SHARE.md **Invariant #4** currently reads "No elevation anywhere at runtime .. No installer-written .. firewall steps". This spec **scopes a deliberate, owner-approved exception**:

- Firewall changes ARE now made - but only (a) by the elevated installer when the new checkbox is chosen, or (b) by a single explicit UAC-prompted helper at runtime. Never silently.
- Still no Windows service anywhere. Still no *silent* elevation. Still no install-time HKCU consent write (marker file instead, to dodge the hive hazard the invariant was protecting).

Update both documents to record the exception and its scope when this ships.

---

## 9. Channel impact

- **winget**: manifest still points at the Inno `setup.exe`, `InstallerType: inno`, no dependencies, no `Scope`. The new checkbox defaults off and is skipped under `WizardSilent`, so validation installs viewer-only - unchanged behavior, no new failure surface. Do not add `AppsAndFeaturesEntries`.
- **Store MSIX**: packaged builds declare firewall access via the manifest `desktop2:windows.firewallRules` extension (already noted in the MSIX plan), and run full-trust; there is no interactive installer checkbox there. Treat packaged builds as "server features available" (the manifest grants the firewall rule and StartupTask), so `ServerFeatures.IsEnabled()` should also return true when `AutostartManager.IsPackaged()` - i.e. gate = marker OR HKCU flag OR packaged. Confirm the manifest firewall extension is present when this ships.
- **Portable ZIP**: no installer, so no install-time path - only the deferred runtime opt-in applies (marker never written; HKCU flag path used).

---

## 10. Open decisions

- **A. Disabled-state Share button**: keep it visible as an opt-in entry (recommended, matches "диалог кнопки поделиться"), or hide it entirely and rely solely on the Settings > Share tab for opt-in. Default in this spec: keep visible.
- **B. Per-user (non-elevated) install with the box ticked**: grey the box with a "needs admin install" hint (recommended), or accept the tick and defer the firewall step to a first-launch UAC prompt. Default: grey/hint.
- **C. Autostart default when enabling**: on (recommended for the server scenario) or off. Default: on, but reversible in the same flow.
- **D. Elevated helper form**: `enable-share-server.ps1` via `runas` (recommended, auditable) vs. direct `netsh` via `runas`. Default: script.

---

## 11. Test plan

1. VM/server with a direct public IP, no router: install with the box ON -> confirm rule via `netsh`, start a share, connect from a phone on cellular. (Reproduces + fixes the field case.)
2. Same box, box OFF -> viewer only; then deferred opt-in -> one UAC prompt -> reachable. Toggle off/uninstall -> rule gone, port closed (verify with an external TCP probe).
3. Home PC behind NAT with the box ON -> LAN path works immediately; internet path works if the user also forwards/UPnP (unchanged from today, now with the local firewall no longer blocking).
4. `winget install` (silent) -> viewer only, no prompts; `winget upgrade` correlation intact (ARP name unchanged).
5. Language sweep RU/EN/UK on the checkbox, explainer, dialog, and Settings panel.
