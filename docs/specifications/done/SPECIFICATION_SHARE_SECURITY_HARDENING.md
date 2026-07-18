# Specification - Android Folder Share: SFTP security & DoS hardening

> Outcome (2026-07-15): **shipped** in Release 26.7.15.2200 (commit dcc066e) - Companion `SetNetworkPolicy` wiring (LAN-only export, max-connections cap) plus the re-vendored hardened worker binary (`payload/companion/fms-share-worker.exe`). Worker-side §3.1/3.4/3.5/3.6 landed in the separate `P:\windows\fms_companion` repo and rode in via that re-vendored binary.
> Status: **implemented** (2026-07-15) in source. Worker `go test ./... && go vet ./...` green (incl. new handshake/cap/idle/symlink/policy tests); Companion `dotnet build -c Release` -> 0 errors. **Still out of this repo's reach:** the Android client side of the reconnect contract ([CONTRACT_ANDROID_RECONNECT.md](../../contracts/CONTRACT_ANDROID_RECONNECT.md)).
> Scope split across two repos: the Go worker (`P:\windows\fms_companion`, build there - never from this repo) and the LITE/Companion side (`src/FastMediaSorterCompanion/`).
> Related: [SPECIFICATION_ANDROID_FOLDER_SHARE.md](SPECIFICATION_ANDROID_FOLDER_SHARE.md), [SPECIFICATION_QR_IMPORT_ANDROID.md](SPECIFICATION_QR_IMPORT_ANDROID.md), [SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md), [SPECIFICATION_SHARE_COMPANION_APP.md](SPECIFICATION_SHARE_COMPANION_APP.md).

---

## 0. Why this exists

A security review of the Share service (2026-07-15) found the cryptographic and authentication foundations solid - 139-bit random password from `crypto/rand`, constant-time credential compare, TOFU-pinned ed25519 host key, lexical-plus-prefix root confinement, `golang.org/x/crypto v0.54.0` (past the CVE-2024-45337 / CVE-2025-22869 fixes). Online password brute-force is not a realistic threat.

The real gaps are **DoS resilience** (the SFTP server is trivially floored by a connection flood) and **exposure defaults/visibility** (WAN exposure and failed-auth events are invisible to the owner). This spec closes those, at the level the owner chose - pragmatic, not paranoid.

The worker lives in a separate repo (`P:\windows\fms_companion`) and is vendored into this repo as a prebuilt binary. All worker changes below are implemented and built **there**; this repo only carries the Companion UI wiring and docs.

---

## 1. Threat model (one paragraph)

The untrusted party is a remote SFTP client (anyone who can reach the listen port - the whole internet when a port-forward is up, or anyone on the same LAN/Wi-Fi otherwise). The client is pre-authentication until it presents the exported password. A client that never authenticates must not be able to exhaust host resources; an authenticated client is confined to the shared roots and, on a read-only root, cannot mutate. The bearer of the QR/`.fmscfg` **is** the access token - protecting the token is the user's responsibility, which we make explicit.

---

## 2. Decisions (authoritative)

Each row cites the review finding number and who decided. "Do" items are specified in section 3; "Won't" items in section 4.

| # | Finding | Decision | Owner rationale |
|---|---------|----------|-----------------|
| A | п.7 Credential rotation | **Won't rotate.** Warn about the bearer-token danger at export (QR/file) time. | "If I shared my music folder, I don't want it to suddenly stop working one day." |
| B | п.4 LAN-only is cosmetic | **Do: make LAN-only enforce.** When on, the worker must not open UPnP/NAT-PMP or advertise a WAN path - not just strip the export. | Confirmed in code: `ShareSettings.vb` - "the worker still auto-attempts UPnP regardless (it has no knob)". |
| C | п.2 No connection cap | **Do: add a setting "max simultaneous connections", default 10, user-editable 1..99999 (text entry).** | "Who knows what server he sets this up on - not my business to limit." |
| D | п.1 Slowloris handshake | **Do: fixed 15s handshake deadline.** No UI. | Recommended default accepted. |
| E | п.3 Silent failed logins | **Do: log failed auth / rejected connections to a file. No active throttle.** | Log for visibility; leave throttling to the router/firewall. |
| F | п.5 Firewall opens `public` | **Won't change the firewall.** Keep `domain,private,public`. Document the behavior and warn; do not make a mobile user tick boxes when they move networks. | "Nothing wrong with connecting from my phone to my laptop on the same office Wi-Fi. Let's not be paranoid." |
| G | п.6 Lexical confinement | **Do: resolve symlinks/junctions and re-check the real path is inside the root.** | Recommended; defense in depth. |
| H | п.9 No idle timeout | **Do: idle-session timeout = 3 hours,** paired with a mandatory **Android client reconnect contract** so a "refresh" reopens the session transparently instead of erroring. | "If the user refreshes the open resource on Android, it must reopen, not throw an access error - write the contract, I'll pass it to the client side." |

Not selected from the review: п.9 writable-mode UI warning, п.8 check-host.net opt-out. See section 4.

---

## 3. Design

### 3.1 Handshake deadline (D) - `internal/sftpserver/server.go`

`handleConn` receives a raw `net.Conn` and calls `ssh.NewServerConn` with no deadline; `x/crypto/ssh` has no built-in login-grace, so a client that stalls the handshake pins a goroutine forever.

```go
const handshakeGrace = 15 * time.Second

func (s *Server) handleConn(conn net.Conn, sshConfig *ssh.ServerConfig) {
    _ = conn.SetDeadline(time.Now().Add(handshakeGrace)) // whole handshake, incl. auth
    serverConn, chans, reqs, err := ssh.NewServerConn(conn, sshConfig)
    if err != nil {
        s.secLog.handshakeFailed(conn.RemoteAddr(), err) // see 3.5
        return
    }
    _ = conn.SetDeadline(time.Time{}) // clear before serving; per-session idle takes over (3.4)
    ...
}
```

Fixed value, no configuration surface.

### 3.2 Max simultaneous connections (C) - worker + IPC + Companion UI

**Semantics:** the limit bounds **all** concurrent connections counted from `Accept` (pre-auth included), because slowloris is a pre-auth attack. A stuck handshake frees its slot within the 15s grace (3.1), so a flood cannot hold slots for long. When the limit is reached, a new connection is **closed immediately** (fast-fail) - never queued (a queue is itself a memory DoS).

**Worker - `internal/sftpserver/server.go`:**
```go
// Config gains:
MaxConnections int // 0 or <1 -> default 10; clamped to [1, 99999]

// acceptLoop, after Accept:
select {
case s.sem <- struct{}{}:
default:
    s.secLog.connectionRejected(conn.RemoteAddr(), "max connections reached")
    _ = conn.Close()
    continue
}
// per-conn goroutine defers: <-s.sem
```
`sem` is a buffered channel sized to the clamped limit, built in `Start`.

**Persistence - `internal/service/worker.go` `settings`:**
```go
type settings struct {
    Port           int  `json:"port"`
    MaxConnections int  `json:"maxConnections"` // 0 -> default 10
    LanOnly        bool `json:"lanOnly"`        // see 3.3
}
```

**Transport - new additive IPC request** (`IPCSchemaVersion` stays 1; an older worker replies `OK:false, "unsupported request type"`, which the client treats as a no-op per the existing convention):
```
TypeSetNetworkPolicy = "SetNetworkPolicy"
Request gains: MaxConnections *int `json:"maxConnections,omitempty"`
               LanOnly        *bool `json:"lanOnly,omitempty"`
```
Pointer fields so "unset" is distinguishable from "0/false". The worker persists to `settings.json` and, if the server is running **and a value actually changed**, applies by restarting the server (`stopServer()`+`startServer()`, the same path `SetSharedFolders` uses). The restart rebuilds the connection semaphore at the new size and re-runs reachability (releasing the WAN mapping when `lanOnly` was turned on, opening it when turned off). A change while stopped, or a no-op push, just persists. **Implementation note:** the restart momentarily drops active sessions - this is deliberate and acceptable because it is a rare admin action and the Android reconnect contract (3.4.1) makes the client re-establish transparently. (An earlier draft proposed a live semaphore resize that never dropped sessions; the restart path was chosen for simplicity and correctness, since `lanOnly` needs a reachability re-run anyway.) The Companion pushes only on the field's commit (`NumericUpDown.Leave`), not on every spinner tick, so holding the control does not thrash the server.

**Companion UI (`Forms/*` share settings):** a numeric text field "Maximum simultaneous connections" defaulting to 10, accepting 1..99999, sent via `SetNetworkPolicy` on change and again before `StartServer`. Note in the UI help that values below 2 can briefly refuse a reconnect while an old idle session is still being reaped (3.4).

### 3.3 LAN-only enforcement (B) - worker `computeReachability`

Today `internal/service/worker.go` `computeReachability` **always** calls `netaccess.TryMap(port)`. The `ShareSettings.LanOnlyExport` VB flag only strips the WAN entry from the exported config; the UPnP hole is punched regardless.

**Change:** gate the mapping on the persisted `LanOnly` setting.
```go
func (w *Worker) computeReachability(port int, fingerprint string) {
    instance := netaccess.InstanceName()
    stopMdns, mdnsErr := netaccess.StartAnnounce(port, instance, fingerprint, instance)
    var mapping *netaccess.Mapping
    if !w.lanOnly() {                       // LAN-only: never open UPnP/NAT-PMP
        mapping, _ = netaccess.TryMap(port)
    }
    reach := netaccess.Compute(port, mapping, mdnsErr == nil)
    ...
}
```
When `LanOnly` is on: no `TryMap`, `reach.ExternalHost` stays empty, `BuildConfig` naturally omits the port-forward path, and no WAN mapping exists to leak. mDNS + the LAN access path are unaffected. Toggling `LanOnly` **on** at runtime via `SetNetworkPolicy` releases any live mapping (`w.mapping.Release()`); toggling **off** recomputes reachability.

The VB `LanOnlyExport` toggle now drives real enforcement: Companion sends `lanOnly` through `SetNetworkPolicy`. The export-stripping becomes belt-and-suspenders (harmless).

**Firewall (F):** unchanged - the program-scoped inbound rule stays `domain,private,public`. LAN-only does **not** remove it (the LAN path still needs an inbound allow). See section 5 for the documentation/warning this decision requires.

### 3.4 Idle-session timeout (H) - worker, 3 hours

**Server side - `internal/sftpserver/server.go` `handleSession`.** Wrap the `ssh.Channel` handed to `sftp.NewRequestServer` in an activity-tracking `io.ReadWriteCloser` that resets a timer on every `Read`/`Write`; on expiry it closes the channel (and the underlying connection), which surfaces to the client as a normal connection close.

```go
const idleTimeout = 3 * time.Hour

type idleChannel struct {
    ssh.Channel
    reset func()
}
func (c *idleChannel) Read(p []byte) (int, error)  { c.reset(); return c.Channel.Read(p) }
func (c *idleChannel) Write(p []byte) (int, error) { c.reset(); return c.Channel.Write(p) }
```
- **Idle = no SFTP data for 3h.** An in-flight transfer continuously reads/writes, so a long download or a slow ranged video read never times out mid-transfer.
- **Low-level SSH keepalives do not reset the timer** (only real SFTP I/O does), so the timeout reliably reclaims abandoned-but-TCP-alive sessions - which is the whole point for the connection cap (3.2) and memory.
- Credentials and host key are **not** touched on timeout (no rotation, decision A): an immediate reconnect with the stored credentials succeeds. This is what makes the reconnect contract (3.4.1) seamless.

#### 3.4.1 Android client reconnect contract (liftable - hand this to the client side)

> This subsection is written to be lifted verbatim into the Android/client contract (companion of [SPECIFICATION_QR_IMPORT_ANDROID.md](SPECIFICATION_QR_IMPORT_ANDROID.md)). It is a behavioral contract, implementation-agnostic.

**Server guarantees**

1. **Idle close is graceful and non-punitive.** After 3 hours with no SFTP request on a session, the server closes that SSH session/connection. It is a plain transport close - not an auth revocation, not a ban. The client is expected to reconnect.
2. **Stable identity across reconnect.** The host-key fingerprint (`hostKeyFingerprintSha256`, the TOFU pin) and the credentials (`username`/`password`) are **unchanged** by an idle close and across worker restarts/reboots. A stored pin and stored credentials remain valid indefinitely (until the user deletes the share).
3. **No server-side session state.** Every connection is independent (one auth -> one SFTP subsystem). There is no cursor/handle the client must restore beyond reopening the paths it cares about.

**Client obligations**

4. **Reconnect transparently on any transport-level failure.** On EOF / "connection closed by remote" / SSH disconnect / TCP reset / i/o timeout during an operation - and specifically when the user taps **Refresh** on an open resource - the client MUST, without surfacing an error first: re-dial the access path (racing `accessPaths` as today), re-verify the host key against the stored pin, re-authenticate with the stored credentials, reopen the SFTP subsystem, and retry the pending/refresh operation. Only if the reconnect itself fails does the client surface an error.
5. **Classify failures - do not blindly retry-loop.** The reconnect in (4) applies to **transport-level** failures only. The client MUST distinguish:

   | Failure at reconnect | Meaning | Client action |
   |---|---|---|
   | Transport (EOF, reset, closed, timeout) | Idle close or network blip | **Reconnect transparently** (obligation 4). |
   | **Auth rejected** (SSH auth failure) | Credentials no longer valid (share deleted/re-created; should not happen from idle alone) | Do **not** retry-loop. Surface "re-pair needed" and stop. |
   | **Host-key mismatch** (fingerprint != stored pin) | Possible MITM - the key never changes on a benign reconnect | Do **not** connect or auto-accept. Surface a security warning, keep the old pin. |
   | SFTP status (permission denied, no such file) | Normal app-level result (e.g. read-only root, deleted file) | Surface normally; do not reconnect. |

6. **Bounded retry.** Transparent reconnect SHOULD retry a small bounded number of times (e.g. race the access paths once, then 1-2 backed-off retries) before surfacing an error - never an unbounded loop.
7. **Optional keepalive is not a substitute.** The client MAY keep a session warm with its own SSH keepalives, but MUST NOT rely on the server holding an idle session open - the 3h close can happen regardless. Obligation 4 is the contract; keepalive is only an optimization.

**Net effect:** the user leaves a shared folder open on the phone overnight, the server reaps the idle session at 3h, and the next tap/Refresh reconnects in well under a second with the stored pin and password - indistinguishable from "it just kept working".

### 3.5 Failed-auth security log (E) - worker

Add a small append-only security log (separate from any debug/stderr output) recording, one line per event: UTC timestamp, remote IP:port (best-effort), event. Events: `auth-failed` (from the `PasswordCallback` reject path - log the offered username, never the offered password), `handshake-failed` (3.1), `connection-rejected` (limit hit, 3.2). **No throttle, no delay** - visibility only (decision E).

- Location: alongside the worker's data dir (`%LOCALAPPDATA%\FastMediaSorterCompanion\`), file e.g. `security.log`.
- **Bounded**: cap size (e.g. 1 MB) with single-file truncation/rotation so the log itself cannot become a disk-fill DoS.
- The `PasswordCallback` still returns constant-time compare results unchanged (3.1 of the review) - logging happens after the decision, does not branch on which of user/pass failed.

### 3.6 Real-path root confinement (G) - `internal/sftpserver/roots.go`

`resolve` currently checks the cleaned host path is lexically inside the root. A pre-existing symlink/junction inside a shared folder (the client cannot create one - `Filecmd` has no `Symlink` case) would let `os.Open`/`os.OpenFile` follow it outside the root.

**Change:** after the existing lexical clean+prefix check, resolve the real path and re-check containment. Use `filepath.EvalSymlinks` on the deepest existing ancestor of the target (the target itself may not exist yet on a write/create), then re-apply the `HasPrefix(rootReal + sep)` test against the **evaluated** root. Reject with `sftp.ErrSSHFxPermissionDenied` on escape or on an EvalSymlinks error for an existing path. Keep the lexical check as the fast first line; the real-path check is defense in depth.

Note in code: on Windows, directory junctions are creatable without privilege, so the lexical check alone is insufficient - this is the reason the real-path re-check exists.

### 3.7 Export bearer-token warning (A) - Companion UI

At the moment the user produces a QR or saves a `.fmscfg`, show a clear one-time-per-surface warning (not a nag): the QR/file embeds the password and grants full access to the shared folders to **anyone who sees it** - treat it like a key, don't post it publicly, and it does not expire. This replaces credential rotation. Reuse the existing `ExcludePasswordFromExport` safeguard copy where it fits; the warning is informational and does not block export.

---

## 4. Non-goals (explicitly rejected)

- **Credential rotation** (A) - rejected by owner; a shared resource must not spontaneously stop working. Mitigated by the export warning (3.7).
- **Auth throttle / tarpit / lockout** (E) - not added; 139-bit password makes online guessing irrelevant, and the owner prefers to leave rate-limiting to the router/firewall. Logging only (3.5).
- **Changing the firewall profile scope** (F) - kept `domain,private,public`. A mobile user must not have to reconfigure when switching networks. Documented + warned instead (section 5). Same-LAN reachability (home, office Wi-Fi) is an accepted, intended capability.
- **Writable-mode UI warning** (п.9) - not selected.
- **check-host.net opt-out** (п.8) - not selected; the external connect-back stays (it is the only honest reachability confirmation). Worth a line in the privacy note that a public host:port is submitted to check-host.net during reachability checks.

---

## 5. Documentation & warning debt created by decision F

Because the firewall stays open on all profiles, the following must state the behavior plainly (no code enforcement, per owner):

- **[docs/guides/STORE_PUBLISHING.md](../../guides/STORE_PUBLISHING.md) privacy/description copy** and the site: the Share server, once enabled, is reachable by any device on the **same network** (including public Wi-Fi), protected by the exported password; it is not exposed to the internet unless a port-forward path exists (and never when LAN-only is on).
- **Companion Share UI**: a short standing note near the enable/opt-in surface - "While sharing, devices on your current network can reach this PC on the share port (password-protected). Turn the share off when you don't need it." Pairs with the existing `ServerFeatures` opt-in gate.
- **LAN-only toggle help**: now truthfully "no internet exposure" (enforced, 3.3), not just "omitted from the QR".

---

## 6. Files touched

**Worker repo `P:\windows\fms_companion` (build there):**
- `internal/sftpserver/server.go` - handshake deadline (3.1), connection semaphore (3.2), idle-channel wrapper (3.4), hook the security log.
- `internal/sftpserver/roots.go` - real-path confinement (3.6).
- `internal/service/worker.go` - `settings{MaxConnections,LanOnly}`, `SetNetworkPolicy` handling, LAN-only gate in `computeReachability`, live semaphore/mapping apply.
- `internal/ipc/protocol.go` - `TypeSetNetworkPolicy`, `Request.MaxConnections/LanOnly` (pointer, additive; schema stays 1).
- new `internal/sftpserver/seclog.go` (or `internal/service/`) - bounded append-only security log (3.5).
- tests: server_test (handshake timeout, connection cap, idle close, symlink escape rejected), worker/ipc test for `SetNetworkPolicy`.

**This repo `src/FastMediaSorterCompanion/`:**
- `Core/WorkerIpc.vb` / protocol POCO - add `SetNetworkPolicy` request + fields.
- `Core/ShareSettings.vb` - add `MaxConnections` (default 10); wire `LanOnlyExport` -> worker `lanOnly` enforcement.
- `Forms/*` share settings - the max-connections field (1..99999) and the export bearer-token warning (3.7); the standing network-reachability note (section 5).
- Docs/site + `docs/guides/STORE_PUBLISHING.md` - section 5 copy.

**Android/client repo (separate):** implement the reconnect contract (3.4.1).

---

## 7. IPC contract summary (stays schema v1, additive)

```
Request  += Type "SetNetworkPolicy"
         += MaxConnections *int  (omitempty)   // clamp [1,99999], nil = unchanged
         += LanOnly        *bool (omitempty)   // nil = unchanged
```
Old worker + new client: `OK:false "unsupported request type"` -> client treats as no-op (policy simply not applied until worker updated). New worker + old client: never sends it -> worker uses persisted/default policy (10, lanOnly follows persisted `Share_LanOnlyExport`). No wire break either direction.

---

## 8. Verification gate

Worker (`P:\windows\fms_companion`): `go test ./...` green, including new tests -
1. a connection that sends nothing is closed within ~15s (handshake deadline);
2. with `MaxConnections=2`, a 3rd concurrent pre-auth connection is closed immediately and logged;
3. an idle authenticated session is closed at the (test-shortened) idle timeout and an immediate reconnect with the same credentials succeeds;
4. a symlink/junction inside a root pointing outside is rejected with permission-denied;
5. `LanOnly=true` -> `computeReachability` performs no port mapping (mock `TryMap` asserts not called).

LITE/Companion: `msbuild FastMediaSorter.sln /p:Configuration=Release` -> 0 errors; manual - toggle LAN-only and confirm no UPnP mapping appears; set max-connections and confirm it round-trips; produce a QR and confirm the bearer-token warning shows.

Android: reconnect contract conformance (transport-close -> transparent reconnect; host-key mismatch -> warning; auth-reject -> re-pair prompt).

---

## 9. Invariants preserved

- Worker remains the sole owner of SFTP, credentials, host key, mapping, and now the security log and network policy. LITE still knows nothing about it (invariant 8 of the migration spec) - all new wiring is Companion-side.
- Host key and credentials are never regenerated (TOFU pins and stored passwords stay valid) - reinforced by decisions A and H.
- `ServerFeatures` opt-in gate (SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL) is unchanged and still gates the whole Share surface; this spec hardens what runs **after** opt-in.
- No new elevation surface. The one owner-approved firewall exception is untouched (decision F).
