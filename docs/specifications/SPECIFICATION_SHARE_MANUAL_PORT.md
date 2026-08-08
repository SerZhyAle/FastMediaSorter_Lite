# Specification - Android Folder Share: user-set (fixed) SFTP port

> Status: **design / not started** (2026-07-16, revision 1). No code yet. Owner ask: *"нужно чтобы у пользователя была возможность менять руками порт, мало кто будет этим пользоваться, но мало ли"*.
> Scope split across two repos: the Go worker (`P:\windows\fms_companion`, build there - **never** from this repo) owns the bind and gains a "fixed port" mode; the Companion side (`src/FastMediaSorterCompanion/`) gains one setting, one additive IPC field and one control. **LITE itself is untouched** (invariant 8 of the Companion migration: `grep Companion src/*.vb` must still surface only the launcher).
> Related: [SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md) (worker IPC protocol, `.fmscfg` schema, the risk row this closes), [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md), [SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](done/SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md) (the firewall rule - program-scoped, so it survives this), [SPECIFICATION_SHARE_SYSTEM_SERVICE.md](done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md), [SPECIFICATION_QR_IMPORT_ANDROID.md](done/SPECIFICATION_QR_IMPORT_ANDROID.md) (frozen wire contract - unaffected).

---

## 0. Why this exists (the real problem)

Today **nobody chooses the SFTP port** - the OS does, once, and the worker then sticks to it:

```go
// P:\windows\fms_companion\internal\service\worker.go:27-31
type settings struct {
	// Port is the SFTP listen port. Assigned by the OS on the very first start,
	// then persisted - the exported config, port mapping and firewall rule all
	// reference it, so it must survive restarts and reboots.
	Port int `json:"port"`
```

That is a sane default and should stay the default. But the sticky port is only *mostly* stable, and the two audiences that care are exactly the ones the Share feature was hardened for:

1. **The manual router forward.** A user behind a router with no working UPnP/NAT-PMP forwards the port by hand (this is what [InternetAccessForm](../../src/FastMediaSorterCompanion/Forms/InternetAccessForm.vb) walks them through, and `ShareText.PortForwardText` literally prints `внешний порт <N> -> <lanIp>:<N>`). The rule they type into the router is pinned to a number they never chose and cannot re-choose. The moment that number changes, the forward points at nothing and the router config has to be redone by hand.

2. **The port genuinely can change - and today the only remedy is "re-export".** This is already a logged, accepted risk ([SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md) §risks):

   > | Worker port fallback: if the persisted port is occupied at start, the worker silently rebinds to a fresh OS-assigned port and re-persists it - previously exported QR/`.fmscfg` configs then point at a dead port | Share tab always shows the CURRENT port from `GetStatus`; hint tells the user to re-export after the port changed; acceptance includes a re-export path |

   The mitigation is honest but weak: the QR the user printed/mailed/scanned is now a lie, and mDNS rediscovery only saves them **on the same LAN** - never the port-forward path, which is the one that needed the fixed number.

3. **A dedicated / always-on box wants a known port** ([SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](done/SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md) §0.2 audience, [SPECIFICATION_SHARE_SYSTEM_SERVICE.md](done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md) mode B). "Which port does this server listen on" should be an answerable question, not an emergent one.

So: an **escape hatch**, not a new default. The owner is explicit that few will use it. The design is therefore judged on *not costing anything when unused*, and the whole feature is one checkbox + one number.

**The single most valuable thing it buys** is not "pick 2222" - it is **"freeze whatever port I already have"**: the user's router forward works *today*; one checkbox makes that survive the next reboot. Section 3.4 makes that the one-click path.

---

## 1. Current reality (what the code does today)

- **Nothing in `src/FastMediaSorterCompanion/` decides a port.** Zero port constants exist in the whole Companion tree. Every occurrence is read-only formatting of `status.ListenPort`: [Share_Status_Form.vb:146](../../src/FastMediaSorterCompanion/Forms/Share_Status_Form.vb), [MainWindow.vb:443-473](../../src/FastMediaSorterCompanion/Forms/MainWindow.vb), [InternetAccessForm.vb:183-277](../../src/FastMediaSorterCompanion/Forms/InternetAccessForm.vb). **There is no port control anywhere.**
- **The bind happens worker-side.** `worker.go:269-302`: `desiredPort := loadSettings().Port` (0 on the first ever start -> OS assigns), `sftpserver.New(Config{Port: desiredPort})`; on failure with a non-zero port it silently retries with `Port: 0` and re-persists the new number. `server.go:148-155` does the `net.Listen` and reads back `listener.Addr().(*net.TCPAddr).Port`.
- **No request carries a port.** The full wire request DTO is `schemaVersion`, `type`, `folders`, `maxConnections`, `lanOnly` ([WorkerIpc.vb:83-98](../../src/FastMediaSorterCompanion/Core/WorkerIpc.vb), mirrored at `internal/ipc/protocol.go:52-61`). The port only ever travels **back**, as `GetStatus` -> `status.listenPort`.
- **The export is already port-agnostic.** [ShareConfigBuilder.vb:101-139](../../src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb) reads `status.ListenPort` once and stamps it into the `lan` / `ipv6` / `portforward` `accessPaths` entries. Whatever the port is, the `.fmscfg` and QR carry it. **No change needed here.**
- **The firewall rule is program-scoped, not port-scoped** - deliberately, and it is documented as such in both the installer helper and the runtime fallback:
  ```
  # installer/enable-share-server.ps1:6-8
  # Program-scoped inbound allow (not port-scoped) so the rule keeps working when the
  # worker's dynamically-assigned SFTP listen port changes.
  ```
  **Changing the port cannot break the firewall exception.** This is the single biggest reason the feature is cheap.
- **`RouterInfo.vb` does not touch ports** despite the name - it detects the router make/model to build a `"how to port forward <model>"` search URL. UPnP/NAT-PMP mapping is entirely worker-side (`worker.go:313-323`, `netaccess.TryMap(port)`), and LITE's only lever over it is the binary `lanOnly` switch.
- **There is no "manual port" concept anywhere.** The phrase in CLAUDE.md ("only that lets LITE advertise a manually-forwarded router port") means something else: `ShareConfigBuilder` emits the `portforward` entry **even when no UPnP mapping succeeded**, which the worker's own `ExportConfig` refuses to do. The number is still the worker's dynamic one. A user-typed port is genuinely new.

---

## 2. Goal - two modes, one checkbox

| Mode | How reached | Behavior |
|---|---|---|
| **Auto** (default, today's behavior, unchanged) | Checkbox off. Registry `Share_ListenPort = 0`. | OS assigns on first start, worker persists and reuses it. If the persisted port is taken at start, worker falls back to a fresh one and re-persists - **exactly as today**, including the "re-export your QR" caveat. |
| **Fixed** (new, opt-in) | Checkbox on + a number. `Share_ListenPort = 1..65535`. | The worker binds **that** port or **does not serve at all**. No silent fallback - a fixed port that quietly became a different port is the precise failure this feature exists to prevent. The error is surfaced honestly and the user can fix the number or drop back to Auto in one click. |

Non-goals, explicitly:

- **Not** a per-root port (`ShareRootParams` stays untouched - one server, one port).
- **Not** a control over the *external* (router-side) port. UPnP asks for a same-number mapping and reports what it got (`reach.ExternalPort`); the manual-forward guide already prints both sides. Mapping external != internal is a router-config concern, not ours.
- **Not** a `schemaVersion` bump. See 3.2.
- **Not** a change to `.fmscfg`, the QR, the frozen Android contract, or the firewall rule.

---

## 3. Design

### 3.1 The setting (Companion side)

[ShareSettings.vb](../../src/FastMediaSorterCompanion/Core/ShareSettings.vb) grows one value, following the `MaxConnections` template verbatim (POCO property -> `ReadInt`/`WriteInt` -> clamp helper):

```vb
Public Property ListenPort As Integer = AutoPort   ' Share_ListenPort; 0 = auto (OS-assigned, sticky)

Public Const AutoPort As Integer = 0
Public Const MinFixedPort As Integer = 1024
Public Const MaxFixedPort As Integer = 65535
Public Const SuggestedFixedPort As Integer = 2222  ' only when nothing better is known - see 3.4

''' <summary>0 stays 0 (auto); anything else is clamped into the fixed range.</summary>
Public Function ClampPort(value As Integer) As Integer
    If value = AutoPort Then Return AutoPort
    Return Math.Max(MinFixedPort, Math.Min(MaxFixedPort, value))
End Function
```

`Load`/`Save` gain one line each, mirroring `Share_MaxConnections`:

```vb
ListenPort = ClampPort(ReadInt("Share_ListenPort", AutoPort))   ' in Load
WriteInt("Share_ListenPort", ClampPort(ListenPort))             ' in Save
```

Registry home is the existing shared hive, HKCU `Software\VB and VBA Program Settings\SZA\FastMediaSorter` (`CompanionGlobals.App_name` / `Second_App_Name`). A missing/garbage value degrades to `0` = auto, i.e. to today's behavior - **an upgrade from a build without this feature is a no-op by construction.**

> **Why the floor is 1024 even though Windows has no privileged ports.** Unlike Unix, Windows lets any process bind port 80 or 22 - so the floor is *not* a permission constraint, it is a footgun guard: low ports collide with system listeners (OpenSSH on 22, IIS/HTTP.SYS on 80/443, WinRM on 5985) and produce a bind failure the user will blame on us. Open decision A revisits whether to allow 22 specifically, since 22 *is* the canonical SFTP port.

> **Why the ceiling matters less than the recommendation.** The real advice, which the UI hint carries (5.2), is **stay below 49152**: that is the default start of the Windows dynamic/ephemeral range, and a port inside it can be grabbed by any random *outgoing* connection from any process before the worker starts. Fixing a port inside the ephemeral range reintroduces the exact flakiness the feature removes. The registered range (1024-49151) is the right home for a fixed port. This is a hint, not a hard limit - `MaxFixedPort` stays 65535 because a user who knows what they are doing may have a reason.

### 3.2 IPC - one additive nullable field, no version bump

The existing `maxConnections` / `lanOnly` pair already establishes the pattern precisely: nullable in VB (`Integer?` / `Boolean?`) + `WhenWritingNull` serialization + pointer in Go, so **unset means "omitted" rather than "0"**, and the worker treats absent as "unchanged".

[WorkerIpc.vb:83-98](../../src/FastMediaSorterCompanion/Core/WorkerIpc.vb):

```vb
Public Class WorkerRequest
    Public Property schemaVersion As Integer = WorkerIpc.SchemaVersion
    Public Property [type] As String = ""
    Public Property folders As List(Of ShareFolder) = Nothing
    Public Property maxConnections As Integer?
    Public Property lanOnly As Boolean?
    Public Property port As Integer?          ' NEW: 0 = auto, 1..65535 = fixed. Omitted = unchanged.
End Class
```

Carried on the **existing** `SetNetworkPolicy` request - not a new type. That choice is deliberate:

- `SetNetworkPolicy` is already the "server-wide knobs" channel, is already pushed before `StartServer` ([ShareController.vb:54](../../src/FastMediaSorterCompanion/Core/ShareController.vb)) so the first reachability pass honors it, and is already pushed on the resume/reconcile path (`ShareController.vb:100`).
- It **already restarts the server when a knob changes**, which a port change requires anyway.
- A *new request type* against an old worker fails loudly (`ok=false, error="unsupported request type .."`, `worker.go:119`) - but a *new field* on an existing type is silently dropped by `encoding/json`. That asymmetry is a hazard, and 3.5 handles it head-on rather than by inventing a new request type.

`schemaVersion` stays **1**. This is an additive, backward-compatible field, exactly like the multi-path export additions (S1006/S1013/S1014) which are documented as "additive, still `schemaVersion` 1". The frozen Android contract is not involved at all - `accessPaths[].port` has always carried whatever number the server had.

Response side gains two additive status fields so LITE can be honest instead of guessing (`protocol.go` `Status`, mirrored in `WorkerStatus`):

```go
PortFixed      bool   `json:"portFixed"`      // the worker is honoring a user-set port
LastStartError string `json:"lastStartError"` // non-empty when the last start attempt failed (e.g. fixed port busy)
```

`lastStartError` exists because the boot-restore path has **no requester to return an error to**: the worker restores shares and starts the server on its own, and today a failure there is invisible to the Companion (`Running=false` with shares present is ambiguous). Without this field the fixed-port failure mode is silent, which defeats the point.

### 3.3 Worker changes (repo `P:\windows\fms_companion` - build there, never here)

**a) Persist the intent, not just the number** (`internal/service/worker.go:27-31`):

```go
type settings struct {
	Port      int  `json:"port"`      // the sticky/current listen port (unchanged semantics)
	PortFixed bool `json:"portFixed"` // NEW: the user demanded this exact port - never silently rebind
	// .. MaxConnections, LanOnly unchanged
}
```

Two fields rather than one sentinel because `port` must keep its existing meaning for auto mode (the sticky OS-assigned number). Absent `portFixed` in an existing `settings.json` unmarshals to `false` = auto = today's behavior. **No migration needed.**

**b) Honor it on start** (`worker.go:269-302`). The fallback becomes conditional:

```go
s := loadSettings()
desiredPort := s.Port
server := sftpserver.New(sftpserver.Config{Roots: w.roots, Port: desiredPort, ..})
if _, err := server.Start(ctx); err != nil {
	if s.PortFixed {
		// The user pinned this port (router forward / exported QR depend on it).
		// Rebinding elsewhere would silently invalidate both - stay down and say why.
		w.setLastStartError(fmt.Sprintf("port %d unavailable: %v", desiredPort, err))
		return
	}
	if desiredPort != 0 {
		// Auto mode: the persisted port may be taken - fall back rather than staying down.
		server = sftpserver.New(sftpserver.Config{Roots: w.roots, ..})
		..
	}
}
```

- **Do not set `SO_REUSEADDR` to "help" a fixed port bind.** On Windows `SO_REUSEADDR` does not mean what it means on Unix - it lets an unrelated process **steal** a live bind. It would turn a port conflict into a security problem. If a short retry is wanted for a `TIME_WAIT` remnant, retry the plain `net.Listen` a couple of times with a small delay (open decision B).
- `netaccess.StartAnnounce(port, ..)` and `netaccess.TryMap(port)` already take the port as a parameter and need no change - mDNS re-announces and UPnP re-maps on the restart that a port change triggers.

**c) Apply it on `SetNetworkPolicy`** (`worker.go:225-252`), alongside `maxConnections`/`lanOnly`, using the same read-modify-write + restart-on-change shape already there:

```go
s := loadSettings()
if req.Port != nil {
	s.Port, s.PortFixed = *req.Port, *req.Port != 0
}
// .. MaxConnections, LanOnly
saveSettings(s)
// restart the server if anything that affects the bind changed (existing path)
```

Note `port: 0` from LITE means "back to auto": it clears `PortFixed` and lets the next start take an OS-assigned port. It does **not** need to null out `s.Port` - a stale number with `PortFixed=false` is just today's sticky port, which is the correct auto behavior.

**d) Fix the settings clobber - a live bug that would eat this feature** (`worker.go:301`):

```go
saveSettings(settings{Port: port})   // BUG: fresh literal - silently wipes MaxConnections and LanOnly
```

Every port fallback today **resets the user's max-connections and LAN-only choices to defaults**. `setNetworkPolicy` (`worker.go:241-244`) already does it correctly. Must become read-modify-write:

```go
s := loadSettings()
s.Port = port
saveSettings(s)
```

This is worth fixing regardless of this spec; it is called out here because a `PortFixed` field added on top of the buggy line would be wiped by the very fallback it is supposed to suppress.

**e) Re-vendor.** `go build -ldflags="-H=windowsgui -s -w"` at `cmd/worker`, drop the binary + refreshed `.sha256` into `payload/companion/`. That directory is **gitignored** - the re-vendored binary is a separate, deliberate step, and a fresh clone still has no Share feature until it is placed there.

### 3.4 UI - one checkbox and one number, in the existing options row

Home is [MainWindow.vb](../../src/FastMediaSorterCompanion/Forms/MainWindow.vb), directly under the max-connections row (`MainWindow.vb:193-201`), whose `NumericUpDown` + `ValueChanged`-persists / `Leave`-commits + `_loading`-guard idiom this copies exactly:

```vb
chkFixedPort = New CheckBox With {.Text = If(Rus, "Фиксированный порт:", "Fixed port:"), .AutoSize = True}
numPort = New NumericUpDown With {.Minimum = ShareSettings.MinFixedPort, .Maximum = ShareSettings.MaxFixedPort,
    .Width = 84, .Margin = New Padding(0, 2, 0, 0)}
```

Behavior:

1. **Unchecked (default)**: `numPort.Enabled = False`, saved value `0`. The port label elsewhere keeps showing the live `status.ListenPort` exactly as today.
2. **Checking the box pre-fills the number with the port already in use** - `status.ListenPort` when the server is running, else the last saved non-zero `Share_ListenPort`, else `SuggestedFixedPort`. **This is the headline interaction**: the user whose router forward works today ticks one box and their setup is now durable. They never have to know or type a number.
3. **Changing the number** persists + pushes `SetNetworkPolicy` on `Leave` (not on every `ValueChanged` tick - a spinner passing through 2221 must not restart the server). `ValueChanged` persists only.
4. **Unchecking** pushes `port = 0` -> auto. One click back to the safe default from any broken state.
5. **A port change restarts the server** (worker-side), so the LAN/IPv6/external labels and any exported config are stale by definition -> reuse the existing "re-export" hint, now shown on a port change (5.3).

**Optional local pre-check** (open decision C): before pushing, LITE can try `New TcpListener(IPAddress.Any, port).Start()` for instant "порт занят" feedback instead of a round-trip. Two traps if implemented: (i) **skip the probe when `port = status.ListenPort` and the server is running** - otherwise we report our own worker as the conflict; (ii) it is advisory only - the worker is authoritative, and the probe cannot see a Hyper-V *excluded range* reservation (5.4), where the bind fails with **nothing listening**.

No other form changes. `Share_Root_Params_Form` (per-root, wrong altitude), `Share_Enable_Form` (the gate), `PackageWizardForm` (snapshots `ListenPort` at `:528` and keeps working) are untouched. `Share_Status_Form:146` already prints the live port.

### 3.5 Honesty: the two ways a fixed port can fail to be the port

Both are surfaced, neither is silent. **`ShareController` verifies after every start**: if `_settings.ListenPort <> 0 AndAlso st.Running AndAlso st.ListenPort <> _settings.ListenPort` -> banner.

| Failure | Cause | What the user sees |
|---|---|---|
| **Server down, `lastStartError` set** | The fixed port is busy / excluded. The worker refused to rebind elsewhere (3.3b). | The busy message (5.3) + the two ways out: pick another port, or untick and go auto. |
| **Server up, but on the wrong port** | An **old worker binary** silently dropped the unknown `port` field (3.2), or a user hand-edited `settings.json`. | The mismatch message (5.3) -> "update the app". This is the *only* reason `portFixed` exists in the status: `st.PortFixed = False` while we asked for a fixed port is a positive identification of a stale worker, versus inferring it from a number. |

The stale-worker case is real: the vendored binary is gitignored and re-vendored by hand, so a dev machine (or a user mid-upgrade) can easily have a Companion that knows about `port` and a worker that does not. Detecting it via `PortFixed` rather than guessing is the difference between "update the app" and a mystery.

### 3.6 What does **not** change (and must not be "improved" while here)

- **`.fmscfg` / QR** - [ShareConfigBuilder.vb:101](../../src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb) reads `status.ListenPort` and stamps `lan` / `ipv6` / `portforward`. Any port flows through for free. The canonical byte-identical test vector (`"port":2022`) is a **fixture**, not a default - do not touch it.
- **The frozen Android contract** - `accessPaths[].port` already carries an arbitrary number; the phone races all entries. Nothing to negotiate.
- **The firewall rule** - program-scoped (§1). No re-elevation, no UAC prompt on a port change. **A port change must never trigger elevation** - if a design pressure ever appears to make the rule port-scoped, that is the wrong direction.
- **mDNS** - `_sftp-fms._tcp` announces on the live port with `fp=` TXT; a fixed port simply makes the announce stable too.
- **`ServerFeatures` gate** - a port control lives inside the already-gated Share UI. Nothing new is exposed pre-consent.
- **`ShareRootParams`, `schemaVersion`, `MaxConnections`, `LanOnly`** - untouched.

---

## 4. Files to touch

| Area | File | Change |
|---|---|---|
| Setting | [src/FastMediaSorterCompanion/Core/ShareSettings.vb](../../src/FastMediaSorterCompanion/Core/ShareSettings.vb) | `ListenPort` + `AutoPort`/`MinFixedPort`/`MaxFixedPort`/`SuggestedFixedPort` + `ClampPort()`; one line each in `Load`/`Save` (`Share_ListenPort`) |
| IPC | [src/FastMediaSorterCompanion/Core/WorkerIpc.vb](../../src/FastMediaSorterCompanion/Core/WorkerIpc.vb) | `port As Integer?` on `WorkerRequest`; `PortFixed As Boolean` + `LastStartError As String` on `WorkerStatus` |
| Push + verify | [src/FastMediaSorterCompanion/Core/ShareController.vb](../../src/FastMediaSorterCompanion/Core/ShareController.vb) | `.port = ` in `PushNetworkPolicyAsync`; post-start port verification (3.5) |
| UI | [src/FastMediaSorterCompanion/Forms/MainWindow.vb](../../src/FastMediaSorterCompanion/Forms/MainWindow.vb) | `chkFixedPort` + `numPort` + handlers + pre-fill (3.4) + the two banners |
| Copy | [src/FastMediaSorterCompanion/Core/ShareText.vb](../../src/FastMediaSorterCompanion/Core/ShareText.vb) | `FixedPortHint`, `PortBusyText`, `PortMismatchText`, `PortReexportHint` (RU/EN, §5) |
| Guide | [src/FastMediaSorterCompanion/Forms/InternetAccessForm.vb](../../src/FastMediaSorterCompanion/Forms/InternetAccessForm.vb) | one sentence: fixing the port is what makes the router rule durable (5.5). No structural change - it already interpolates the live port |
| Worker | `P:\windows\fms_companion\internal\service\worker.go` | `PortFixed` in `settings`; conditional fallback (3.3b); `Port` branch in `setNetworkPolicy` (3.3c); **fix the `saveSettings` clobber at :301** (3.3d) |
| Worker | `P:\windows\fms_companion\internal\ipc\protocol.go` | `Port *int` on `Request`; `PortFixed bool` + `LastStartError string` on `Status` |
| Worker | `internal/sftpserver/server.go` | **none** - `Config.Port` is already honored |
| Payload | `payload/companion/fms-share-worker.exe` + `.sha256` | re-vendor after the worker build (gitignored - a deliberate manual step) |
| Docs | [SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md) | update the port-fallback risk row (§0.2) - it is mitigated, not just documented; update Appendix A with the additive fields |
| Docs | `CLAUDE.md` | one clause in the Android Folder Share section if this ships |

**No vbproj entry needed** - the Companion is SDK-style, and this adds no new file. **No `<Compile Include>`, no LITE-side change, no installer change.**

---

## 5. User-visible copy (RU / EN - house style: plain hyphen, ё, `..`)

Companion is RU/EN only (`CompanionGlobals.Is_Russian_Language`, read from the same hive LITE writes). Long/shared prose goes in `ShareText.vb` as `Function Name(rus As Boolean) As String`; short labels stay inline as `If(Rus, .., ..)`.

### 5.1 The control

- Checkbox RU: `Фиксированный порт:` / EN: `Fixed port:`

### 5.2 Hint next to it (`ShareText.FixedPortHint`)

- RU: `Обычно порт выбирает система и запоминает его. Но если он окажется занят при запуске, порт сменится - и выданные раньше QR-коды, и правило на роутере будут указывать в пустоту. Зафиксируйте порт, если пробрасывали его на роутере вручную. Лучше выбрать число меньше 49152: выше начинается диапазон, из которого Windows раздаёт порты исходящим соединениям, и его может занять любая программа.`
- EN: `The port is normally picked by the system and remembered. But if it is taken at startup it changes - and both the QR codes you already handed out and your router rule then point at nothing. Fix the port if you forwarded it on the router by hand. Prefer a number below 49152: above that is the range Windows hands out to outgoing connections, and any program can take it.`

### 5.3 Failure and staleness messages

- **Busy** (`PortBusyText(rus, port)`) RU: `Порт <N> занят другой программой - общий доступ не запущен. Выберите другой порт или снимите галочку «Фиксированный порт», чтобы порт выбирала система.`
  EN: `Port <N> is taken by another program - sharing did not start. Pick a different port, or untick "Fixed port" to let the system choose.`
- **Mismatch / stale worker** (`PortMismatchText(rus, want, got)`) RU: `Сервер работает на порту <G>, а не на выбранном <W>. Обновите приложение - установленный рабочий модуль ещё не умеет выбирать порт.`
  EN: `The server is running on port <G>, not the chosen <W>. Update the app - the installed worker does not support choosing the port yet.`
- **Re-export after a port change** (`PortReexportHint(rus)`) RU: `Порт изменился - выданные раньше QR-коды и файлы настроек больше не подходят. Создайте их заново.`
  EN: `The port changed - QR codes and config files you handed out earlier no longer match. Export them again.`

### 5.4 Excluded-range hint (only when a bind fails with nothing listening)

- RU: `Порт не удаётся занять, хотя его никто не слушает. Возможно, он попал в диапазон, зарезервированный Hyper-V, WSL или Docker. Проверьте командой: netsh int ipv4 show excludedportrange tcp`
- EN: `The port cannot be bound even though nothing is listening on it. It may fall inside a range reserved by Hyper-V, WSL or Docker. Check with: netsh int ipv4 show excludedportrange tcp`

> This is a genuine and famously confusing Windows behavior: Hyper-V (and anything on top of it - WSL2, Docker Desktop, Windows Sandbox) reserves large TCP blocks at boot, and a bind inside one fails with `WSAEACCES` while `netstat` shows the port free. Without this hint the user has no way to understand the failure. Show it as a second line under 5.3's busy message when the port is inside an excluded range (or, if that lookup is not implemented, whenever the bind fails with an access error - open decision C).

### 5.5 One sentence for the port-forward guide

- RU: `Чтобы правило на роутере не пришлось переделывать, зафиксируйте порт в настройках общего доступа - иначе он может смениться при следующем запуске.`
- EN: `So you do not have to redo the router rule, fix the port in the sharing settings - otherwise it may change on the next start.`

---

## 6. Security & honesty

- **A fixed port changes nothing about exposure.** The firewall exception is program-scoped and already allows the worker on any port it binds (§1); this feature moves a number, it does not open anything new. No new UAC prompt, no new elevation, no widening of the `ServerFeatures` gate.
- **Fail-closed, not fail-quiet.** A fixed port that cannot bind leaves the server **down** with a stated reason. The alternative - rebinding elsewhere - would keep a LAN share alive while silently killing the forwarded path and invalidating every exported QR, i.e. it would look like it worked. That is the failure mode this feature exists to remove; reproducing it as a "convenience fallback" would be self-defeating.
- **The user can always get back.** Untick -> auto -> today's behavior, in one click, with no elevation.
- **Never `SO_REUSEADDR` on Windows** (3.3b) - it would let an unrelated process steal our bind. Non-negotiable.
- **No new secret, no new network surface.** The port is already published in plaintext in the `.fmscfg`/QR by design.

---

## 7. Acceptance criteria

1. **Default is a no-op.** Upgrade a machine that has been sharing: `Share_ListenPort` absent -> auto -> the same port as before, same behavior, no UI nag. A user who never opens the setting cannot tell this shipped.
2. **Freeze the current port (the headline path).** Server running on the OS-assigned port N -> tick "Фиксированный порт" -> the box pre-fills with **N** -> reboot -> the server comes back on **N**. The router rule and the previously exported QR still work, with no re-export.
3. **Set a specific port.** Tick + type 2222 -> server restarts on 2222 -> `GetStatus.listenPort = 2222`, `portFixed = true` -> a freshly exported `.fmscfg` has `"port":2222` in the `lan` entry (and in `portforward` when no UPnP mapping remapped it) -> the phone connects.
4. **Busy fixed port fails loudly.** Occupy 2222 with `nc`/another listener, restart the worker -> server does **not** come up on a random port; `lastStartError` is set; the UI shows 5.3-busy with both ways out. Untick -> auto -> serves immediately.
5. **Back to auto.** Untick -> `port = 0` pushed -> `portFixed = false` -> next start takes an OS-assigned port -> today's fallback behavior is intact.
6. **Stale worker is named, not guessed.** Point the Companion at a pre-change `fms-share-worker.exe`, ask for a fixed port -> the server runs on the old port, `portFixed = false` -> the UI shows the 5.3-mismatch "update the app" message. No crash, no silent wrong number.
7. **Settings survive a port change.** Set max-connections to 25 + LAN-only on, then change the port -> both are still 25 / on in `settings.json` (this is the `worker.go:301` clobber fix, 3.3d - it is currently broken and must be regression-tested).
8. **Firewall untouched.** Change the port -> **no UAC prompt**; `netsh advfirewall firewall show rule name="FastMediaSorter Companion SFTP"` is unchanged and the new port is reachable from outside.
9. **Contract intact.** The canonical `.fmscfg` vector still matches byte-for-byte; `schemaVersion` still 1; an old Android build imports a new export unchanged.
10. **Build gate.** `.\build.ps1` -> 0 errors, 0 new warnings, both viewers + Companion. No `v*` tag as part of this work.
11. **Language sweep** RU/EN on the checkbox, the hint, both failure messages and the guide sentence.

---

## 8. Invariant impact

**None.** Checked against [SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md) §3:

| # | Invariant | Impact |
|---|---|---|
| 1 | One exe, channel differences at runtime | Untouched - Companion-only, no `#If`. |
| 2 | Worker is a sibling payload | Untouched. |
| 3 | Never touch the worker data dir from VB | **Respected**: the port goes through `SetNetworkPolicy`; VB never opens `settings.json`. The host key is never involved. |
| 4 | No silent elevation, no service | **Respected** - and this is precisely why the firewall rule is program-scoped. A port change is unprivileged. |
| 5 | IPC schema version 1, mismatch surfaced | **Respected** - additive field, no bump; and 3.5 surfaces a stale worker explicitly rather than ignoring it. |
| 6 | RU + EN, house style | §5. |
| 7 | New `.vb` -> vbproj entry | N/A - SDK-style project, no new files. |
| 8 | No `v*` tag in this work | Respected. |
| 8* | (Companion migration) LITE knows nothing about the worker | **Respected** - zero LITE-side changes; `grep Companion src/*.vb` still surfaces only the launcher. |

The `.fmscfg` contract ([SPECIFICATION_QR_IMPORT_ANDROID.md](done/SPECIFICATION_QR_IMPORT_ANDROID.md)) is frozen and **not** touched: `accessPaths[].port` has always been an arbitrary integer.

---

## 9. Channel impact

**None in any channel.** No installer change, no manifest change, no new dependency, no size change beyond the re-vendored worker.

- **Inno / portable**: the setting is inert until used.
- **winget**: nothing to validate - manifest untouched, no `AppsAndFeaturesEntries`, no dependency.
- **Store / MSIX**: no manifest change. The firewall rule comes from `desktop2:windows.firewallRules` and is program-scoped there too, so a fixed port needs nothing extra. Worth stating explicitly in the submission notes only if a listing text ever mentions ports (it does not).
- **System-service mode** ([SPECIFICATION_SHARE_SYSTEM_SERVICE.md](done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md), shipped 2026-08-08 as the Server edition): **complementary and worth more there** - a headless box wants a known port. The setting lives in the worker's `settings.json`, which the Server edition relocates to `%ProgramData%\FastMediaSorterCompanion`; the Companion-as-management-console pushes `SetNetworkPolicy` over the same pipe. No conflict - but this spec must read the data dir the way `sftpserver.DataDir()` now resolves it (machine dir wins once it exists), not the per-user path it was written against.

---

## 10. Open decisions

- **A. Floor at 1024, or allow the whole 1..65535?** 22 is the canonical SFTP port and a user may want it (their phone does not care - it reads the port from the config - but a *human* reading the router rule might). Windows does not restrict low ports, so it is only a footgun guard. **Default in this spec: floor 1024**, on the grounds that a collision with OpenSSH/IIS/WinRM produces a failure the user blames on us. Flip to `1` with a warning under 1024 if the owner prefers.
- **B. Retry a busy fixed port before failing?** A `TIME_WAIT` remnant from our own previous run can clear within seconds, so 2-3 plain `net.Listen` retries with a short delay would paper over a self-inflicted restart race. **Default: no retry in v1** (fail fast, clear message); add if the field shows restart flakiness. **Never** via `SO_REUSEADDR` (§6).
- **C. Local pre-check + excluded-range lookup in the UI (3.4/5.4)?** A `TcpListener` probe gives instant feedback but is advisory, races the worker, needs the "skip when it is our own port" guard, and cannot see Hyper-V reservations. The excluded-range lookup is a second, separate call. **Default: ship without the probe** (the worker's `lastStartError` is authoritative and sufficient); show the 5.4 hint whenever a fixed-port bind fails with an access error, without parsing ranges. Add both later if the message proves confusing.
- **D. Does an external `ExternalPort` mismatch deserve its own hint?** When UPnP maps the fixed internal port to a *different* external port, the export is correct (`portforward` uses `reach.ExternalPort`) but the user's mental model ("I fixed the port") is not. **Default: no extra hint** - the existing external-address label already shows the real external port. Revisit if it confuses.

---

## 11. Test plan

1. **Regression first (the point of the feature).** Auto mode, occupy the persisted port before start -> confirm today's fallback + re-persist still happen and the UI shows the new port. Nothing about auto mode may change.
2. **Freeze-current-port**, end to end on real hardware: home PC behind a router with a hand-made forward -> note port N -> tick the box (verify it pre-fills N, not a suggestion) -> reboot the PC -> phone on cellular connects with the **old** QR, no re-export, no router edit. This is the whole feature; it either works here or it does not ship.
3. **Fixed + busy**: occupy the chosen port -> restart -> server stays down, message shown, both escape routes work (change number / untick).
4. **Fixed + excluded range**: pick a port inside `netsh int ipv4 show excludedportrange tcp` on a Hyper-V/WSL box -> bind fails with nothing listening -> confirm the 5.4 hint appears and is accurate.
5. **Stale worker**: old `fms-share-worker.exe` + new Companion -> mismatch message, no silent wrong port.
6. **Settings-clobber regression** (3.3d): max-connections 25 + LAN-only on -> change port -> both survive in `settings.json`. Then the same across an auto-mode fallback.
7. **Export/contract**: fixed port -> export -> byte-compare the `accessPaths` shape against the canonical vector's structure; import on an **old** Android build.
8. **Firewall**: change the port several times -> no UAC prompt ever; external TCP probe reachable on each new port; rule unchanged.
9. **Language sweep** RU/EN over every string in §5.
10. **Uninstall**: unchanged (the worker data dir policy is not touched by this spec).
