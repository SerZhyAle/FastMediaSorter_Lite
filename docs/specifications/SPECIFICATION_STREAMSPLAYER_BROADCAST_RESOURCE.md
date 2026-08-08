# Specification - Broadcast a local film or album to **StreamsPlayer** (cross-product collaboration)

> Status: **proposal, 2026-08-08**. Not implemented. Owner decisions of 2026-08-08 are folded in (§0.3).
>
> Scope spans **three repositories**, and each owns a disjoint slice:
> - `P:\windows\fms_companion` (Go worker) - the HTTP broadcast gateway, its tokens, and the additive IPC verbs. **Build it only in its own repository**; never from this one, and never hand-edit the vendored `payload/companion/fms-share-worker.exe`.
> - this repository - the Share Manager surface that creates, lists and revokes a broadcast, the `.spres` document + QR + clipboard export, and the two LITE entry points.
> - `P:\windows\Streams_Player` (C#/WPF) - reading `.spres`, the file association, racing the access paths, and playing a playlist channel.
>
> Related: [SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md) (worker IPC + `.fmscfg`), [SPECIFICATION_QR_IMPORT_ANDROID.md](done/SPECIFICATION_QR_IMPORT_ANDROID.md) (the frozen import contract this one is modelled on), [SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](done/SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md) (the opt-in gate a second listening port inherits), [SPECIFICATION_SHARE_SECURITY_HARDENING.md](done/SPECIFICATION_SHARE_SECURITY_HARDENING.md).

---

## 0. Outcome

### 0.1 What the user gets

A film, an album, or a whole folder sitting on the PC becomes a **channel inside StreamsPlayer** - the same list that holds internet radio and live TV - and plays there like any other channel, on this machine, on another machine in the flat, or over the internet.

The user's path, end to end:

1. In Fast Media Sorter, right-click the video being watched (or the folder box) -> **"Broadcast this file.."** / **"Broadcast this folder.."**.
2. The Share Manager opens on the new **Broadcasts** page, with the resource already prepared: a title, a lifetime, and one address block.
3. It hands back three interchangeable things - a **`.spres` file**, a **one-line text in the clipboard**, and a **QR code**.
4. On the receiving side, StreamsPlayer imports any of them and the film appears as a channel. Pressing play streams it from the PC.

### 0.2 Terminology

| Term | Meaning |
|---|---|
| **Broadcast** | One published resource: a single media file, or an ordered list of files from one folder, reachable over HTTP for as long as its token lives. |
| **Gateway** | The HTTP listener inside `fms-share-worker.exe` that serves broadcasts. A second listening socket in the **same** process as the SFTP server - never a second program. |
| **Token** | The unguessable secret in the broadcast URL. Grants read access to **that broadcast only**, and to nothing else on the PC. |
| **`.spres`** | The resource document handed to StreamsPlayer. A sibling of `.fmscfg`, not a version of it. |
| **Playlist channel** | A broadcast whose address answers with an M3U body; StreamsPlayer plays its items in order inside one channel row. |

### 0.3 Owner decisions taken on 2026-08-08

1. **Transport is an HTTP gateway in the Go worker** - not `sftp://` taught to the player (§1).
2. **Both units ship**: a file becomes a channel, a folder becomes a playlist channel (§2).
3. **Delivery is `.spres` + clipboard text + QR.** Fast Media Sorter never locates or launches `StreamsPlayer.exe`; the two products stay linked by documents only (§5, §12).

---

## 1. Why the gateway, and not `sftp://` in the player

The player's LibVLC payload does ship `libsftp_plugin.dll`, so `sftp://` would technically open. It is still the wrong seam, for three reasons that are properties of the player's own code:

- **`StreamMediaKindClassifier.IsLaunchable` is one predicate on four inputs.** It gates `--url` (`StreamLaunchRequest.Parse`), the M3U import (`M3uPlaylistParser.Analyze`), the clipboard share read (`ChannelShareText.Read`) and the add-channel dialog. Widening it to `sftp` does not add one private path for our films; it lets credential-bearing SFTP URLs into every import surface, including a playlist a user downloads from a stranger.
- **It would break a shipped setting.** FFmpeg (the Flyleaf backend, `VideoBackendFactory`) has no SFTP protocol. An `sftp` channel would have to force LibVLC, so the Settings choice "video backend" would silently not apply to part of the catalog - the sort of divergence the player's own conventions exist to prevent.
- **Seeking a two-hour film over SFTP is a read of a remote file, not a byte range.** HTTP `Range` is what a media player is built to seek with, and the gateway gets it for free.

The cost is honest and is stated where the user can see it: **gateway traffic is plain HTTP** (no certificate exists for a home PC), so on the internet path both the content and the token travel unencrypted. That is why the internet path stays off by default and carries a warning (§7.3), and why a broadcast token is scoped to one resource instead of being the SFTP password that opens every shared folder.

---

## 2. The resource model

| Unit | What is published | What StreamsPlayer shows |
|---|---|---|
| **One file** | Exactly that file's bytes. | One channel row. `MediaKind` = `Video` or `Audio`, decided by extension on the FMS side. |
| **One folder** | An **ordered snapshot** of the media files directly inside it (optionally recursive), taken at creation time. | **One** channel row that plays the items in order. Not N rows. |

**The folder snapshot is frozen at creation.** Files added to the folder afterwards are not served, and a file deleted afterwards is a gap the player reports and skips. Rationale: a live re-scan turns "I shared this season" into "I share whatever lands in this folder from now on", which is a different, larger promise than the user made - and it would make a token's reach depend on a future event. The Share Manager offers an explicit **Refresh** on a broadcast, which re-snapshots under the same token.

Neither unit ever exposes a directory listing, a parent path, or a sibling file. The gateway resolves a request against a table built at creation time; there is no path arithmetic on request data, so there is no traversal surface to harden.

---

## 3. The gateway (Go worker repository)

New package `internal/httpgw`, started and stopped by the same worker lifecycle that owns `internal/sftpserver`.

### 3.1 Surface

| Request | Answer |
|---|---|
| `GET /b/<token>` on a **file** broadcast | The bytes. `Accept-Ranges: bytes`, `206` + `Content-Range` for a range request, `Content-Type` from the extension, `Content-Length` always. |
| `GET /b/<token>` on a **folder** broadcast | `audio/x-mpegurl` M3U: one `#EXTINF:-1,<display name>` + absolute `http://<same host:port>/b/<token>/<n>` per item, in snapshot order. |
| `GET /b/<token>/<n>` | The n-th item's bytes, same rules as a file broadcast. |
| `HEAD` on any of the above | Identical headers, no body. Required - it is what the player's access-path race uses (§6.3). |
| Anything else (`POST`, `PUT`, `DELETE`, `PROPFIND`, ..) | `405`, no body. |
| Unknown, revoked or expired token | `404`, no body, no distinction between the three. A `403` would confirm that a token once existed. |

Fixed properties:

- **`Range` support is mandatory**, including `If-Range` ignored and multi-range refused with `200` (a single-range player is the whole audience). Without it a film cannot be seeked and the feature is not delivered.
- **No listing, no redirect, no upload, no HTML.** The gateway serves bytes and one M3U body; it is not a web server.
- **Byte-for-byte.** No transcoding, no remux, no thumbnailing. A format the player cannot decode is an honest playback error, not a job for the PC's CPU.
- **Limits**: the existing `MaxConnections` policy applies to the gateway as its own budget; per-connection idle and header timeouts mirror `sftpserver`'s grace deadlines; the handler never buffers a whole file.
- **Counted, locally**: connections and bytes served feed the same `stats.json` recorder the SFTP path uses, and like it, **never leave the PC**.
- **Security log**: unknown-token hits and method refusals go to the existing `SecLogger`, so a scan on an exposed port is visible to the owner.

### 3.2 Tokens and lifetime

- 32 bytes from `crypto/rand`, base64url without padding. Nothing derived from the path, the file name, or the clock.
- Stored in the worker's data dir (`broadcasts.json`, same ACL and same "never regenerate, never hand-edit" rule as the host key's directory), so a restart of the worker - or a migration between User and Server host mode - keeps every broadcast working.
- **Lifetime** is chosen at creation: 1 hour / 24 hours (default) / 7 days / until revoked. Expiry is enforced by the gateway on every request, not by a sweeper, so a clock change cannot resurrect a dead token.
- **Revocation is immediate and per-broadcast.** Revoking one never touches another, and stopping the share stops them all.
- The token never appears in a log line in full; the security log and the UI show its first 6 characters.

### 3.3 Port and firewall

- Its own TCP port, `0` = OS-assigned on first enable, then persisted so a `.spres` handed out yesterday still resolves today.
- Its own UPnP/NAT-PMP mapping via `netaccess.TryMap`, requested only when the internet path is enabled and never when `LanOnlyExport` is on.
- **No new UAC prompt.** The existing opt-in firewall rule is program-scoped with no port clause (`enable-share-server.ps1`: `program="$ExePath" protocol=TCP`), so it already admits a second listener in the same executable. Acceptance criterion 6 (§10) proves this on a real machine instead of trusting the reading.

### 3.4 IPC (additive; `IPCSchemaVersion` stays 1)

New request types alongside `GetStatus` / `SetSharedFolders` / `StartServer` / `StopServer` / `ExportConfig` / `ResetStats` / `SetNetworkPolicy`:

| Type | Payload | Reply |
|---|---|---|
| `SetBroadcastPolicy` | `enabled`, optional `port` | Status |
| `CreateBroadcast` | `kind` (`file`/`folder`), `hostPath`, `recursive`, `title`, `ttlSeconds` | The broadcast record (id, token, path, item count, `expiresAt`) |
| `ListBroadcasts` | - | `[]Broadcast` with per-item counters |
| `RefreshBroadcast` | `id` | The re-snapshotted record, same token |
| `RevokeBroadcast` | `id` | Status |

`Status` gains `broadcastEnabled`, `broadcastPort`, `broadcastCount` - additive fields an older Share Manager ignores. An older **worker** answers an unknown type with the existing benign `OK:false / "unsupported request type"`, which the Share Manager must render as "update Fast Media Sorter to broadcast" and never as a crash - the same degradation `ResetStats` and `SetNetworkPolicy` already rely on.

---

## 4. The `.spres` document (frozen wire contract, schemaVersion 1)

A new contract. **`.fmscfg` is not touched** - it describes an SFTP resource for the Android app and keeps its schema, its consumers, and its freeze.

```jsonc
{
  "schemaVersion": 1,                      // int - reject anything greater than you support
  "kind": "broadcast",                     // string - reserved discriminator; reject unknown values
  "resourceName": "Dune (2021)",           // string - suggested channel title
  "protocol": "http",                      // string - ALWAYS "http" in v1; reject anything else
  "mediaKind": "video",                    // "video" | "audio"
  "playlist": false,                       // true = the address answers with an M3U of items
  "itemCount": 1,                          // int >= 1
  "accessPaths": [                         // ORDERED lan -> ipv6 -> portforward; RACE them, never trust [0]
    { "kind": "lan",         "host": "192.168.1.100", "port": 55272 },
    { "kind": "ipv6",        "host": "2a02:...",      "port": 55272 },
    { "kind": "portforward", "host": "46.54.0.135",   "port": 55272 }
  ],
  "path": "/b/hT9x..",                     // same path on every access path
  "durationSeconds": 8520,                 // optional, best effort, 0 = unknown
  "expiresAt": "2026-08-09T21:00:00Z",     // RFC 3339 UTC; "" = until revoked
  "accessNote": "...",                     // optional localized line to show when every path fails
  "createdAt": "2026-08-08T21:00:00Z"      // RFC 3339 UTC
}
```

Rules, inherited deliberately from the `.fmscfg` contract so both importers behave the same way:

- **The URL is composed, never transmitted**: `http://{host}:{port}{path}`, and an IPv6 host is bracketed. One path, many addresses, is what lets a resource survive a LAN address change.
- **Ordering is advice; racing is the contract.** An importer that plays `accessPaths[0]` fails the day the PC moves to a different subnet.
- **Unknown fields are ignored, unknown `accessPaths.kind` entries are skipped** - that is the additive channel for v2.
- **Transport variants**: a `.spres` **file** is always plain UTF-8 JSON, no BOM. A **QR** payload is plain JSON when small, otherwise `SPRES1:` + base64(gzip(JSON)) - byte-identical in shape to the existing `FMSCFG1:` rule, with the same ~900-byte switch point.
- **The clipboard line is the lossy variant, on purpose**: `SPCH1 http://<best host>:<port>/b/<token>` reuses the player's existing `ChannelShareText` and therefore needs **no player change at all**. It carries one address, no title, no expiry, no playlist flag - so it is offered as "quick link", never as the primary export. FMS picks the best host: the port-forward address when the internet path is on, otherwise the LAN address.

At implementation time this section is lifted verbatim into `docs/contracts/CONTRACT_BROADCAST_RESOURCE.md`, next to `CONTRACT_ANDROID_RECONNECT.md`, and this spec references it - one source of truth for two repositories.

---

## 5. Fast Media Sorter side

### 5.1 LITE entry points (two menu items, nothing more)

- [src/Main_Form.VideoMenu.vb](../../src/Main_Form.VideoMenu.vb) - **"Broadcast this file.."** on the current video/audio file.
- [src/Main_Form.FolderMenu.vb](../../src/Main_Form.FolderMenu.vb) - **"Broadcast this folder.."** next to the existing "Share this folder..".

Both go through [src/Main_Form.ShareLauncher.vb](../../src/Main_Form.ShareLauncher.vb), which already wakes or cold-starts the Companion and forwards a path by `WM_COPYDATA`. The payload gains a command prefix (`::fms-broadcast::<path>`) so Companion knows to open the Broadcasts page rather than the shares list; an older Companion that does not know the prefix must treat the message as a plain folder path, not as garbage.

Both items are `#If Not NETFRAMEWORK` - the x86 fallback has no Share surface at all, and this is a feature, not a bug fix (maintenance policy).

**LITE learns nothing new.** No token, no gateway, no HTTP, no `.spres`. `grep -i broadcast src/*.vb` must surface only the two menu items and the launcher prefix.

### 5.2 Share Manager: the Broadcasts page

A new page in [src/FastMediaSorterCompanion/Forms/MainWindow.vb](../../src/FastMediaSorterCompanion/Forms/MainWindow.vb), gated behind `ServerFeatures.IsEnabled()` exactly as every other Share surface is, plus its **own** enable switch (§7.1).

- A list of live broadcasts: title, kind (file / N items), created, expires, hits, first 6 characters of the token.
- Per row: **Copy link** (the `SPCH1` line), **Save `.spres`..**, **Show QR**, **Refresh** (folder only), **Revoke**.
- Creating one from a forwarded path opens a small dialog: title (prefilled from the file/folder name), lifetime, "include subfolders" for a folder, and a **LAN only / also over the internet** choice that defaults to the persisted `LanOnlyExport`.
- The QR and the `.spres` are built on the **Companion** side, by a new `BroadcastConfigBuilder` modelled on [ShareConfigBuilder.vb](../../src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb) - same reason as before: only the host side knows the manually-forwarded port and the LAN-only preference, so the worker's own export cannot produce this document.
- Address facts (`LanAddress`, `Ipv6Address`, external host/port, `ExternalPortChecked/Open`) are read from the same `Reachability` block the `.fmscfg` export uses. No second network stack.

---

## 6. StreamsPlayer side

### 6.1 Reading the document (Core)

New in `StreamsPlayer.Core`: `BroadcastResource` (record) + `BroadcastResourceReader.Read(string json)` returning a status-bearing result in the shape of `ChannelShareRead` - `Ok`, `NotResourceDocument`, `UnsupportedVersion`, `InvalidDocument`. It **must not throw** on hostile input; a malformed document is a message to the user, not an exception in an `async void` handler.

Core stays platform-neutral: no WPF, no media dependency, no network call inside the reader.

### 6.2 Import surfaces

- **File association `.spres`** (the primary path: double-click what FMS saved). Registered by the installer and the MSIX manifest, in the same way the player registers what it already owns.
- **Import from file** ([MainWindow.ImportExport.cs](../../../Streams_Player/src/StreamsPlayer.App/MainWindow.ImportExport.cs)) - the existing dialog learns the extension and dispatches by content, keeping the M3U path untouched.
- **Drag-and-drop** onto the main window.
- **Paste** - already works via `ChannelShareText`, no change.

An import adds **one** channel with `SourceOrigin.Imported`, so `CatalogMerger` never updates or prunes it on a catalog refresh - the existing guarantee for user rows covers this by construction.

### 6.3 Storage and playback

`StreamChannel` gains additive, defaulted fields (an older state file deserializes to the initializer defaults and keeps its meaning):

| Field | Purpose |
|---|---|
| `AlternateUrls` | The other composed addresses. Without them a channel is pinned to one subnet and dies when the PC's LAN address changes. |
| `ResourceKey` | The broadcast `path`. **The de-duplication key**: re-importing a refreshed `.spres` for the same broadcast updates that row instead of adding a second one. |
| `ResourceExpiresAt` | Renders "expires in ..", and turns a `404` into the right sentence. |
| `IsPlaylistResource` | Whether the address answers with an M3U. |

Playback:

1. **Race the addresses.** Issue a `HEAD` (or a 1-byte `Range` `GET` for servers that dislike `HEAD`) to every stored address in parallel with a short deadline; the first `200`/`206` wins and is played. Same discipline as the Android importer, and the reason `HEAD` is mandatory in §3.1.
2. **A playlist channel** fetches its M3U from the winning address and plays the items in order inside one `PlayerWindow`, advancing at end-of-item, with the current position shown as `n/total`. The body is parsed by the **existing** `M3uPlaylistParser` - the gateway deliberately emits a plain `#EXTINF` list with no `#EXT-X-` tags, precisely because the parser refuses HLS manifests.
3. **A dead broadcast is a clear sentence, not a broken row.** `404`/`410`/expiry → "this broadcast is no longer available" plus the document's `accessNote`; the row stays, marked `PlayOutcome.Fail`. The player never deletes a user's row on its own.
4. **Both backends must work.** LibVLC and Flyleaf play plain HTTP; a broadcast channel must never force a backend. This is the payoff of §1 and is its own acceptance criterion.

### 6.4 Export safety (a real hole to close)

`ExportAsync` warns before exporting rows whose URL carries credentials (`CatalogUrlIdentity.HasCredentials`). A broadcast URL has **no** `user:pass@`, so today's check would wave it through - and the exported M3U would hand a stranger the token. The check must be widened: a row with a `ResourceKey` is a secret-bearing row and gets the same confirmation. Publishing broadcast rows into the shared stream catalog is forbidden outright (§12).

---

## 7. Security, privacy, and the gate

### 7.1 The gateway ships OFF

A second listening port is exactly what canon invariant 11 governs. Therefore:

- `ServerFeatures.IsEnabled()` remains the outer gate (nothing about broadcasts exists in the UI without it).
- The gateway has its **own** switch inside the Share Manager, **off by default**, independent of the SFTP server. Enabling SFTP must never enable HTTP.
- Turning it on states, in one sentence in the UI, what it does: "your PC will answer HTTP requests for the files you broadcast, for as long as a broadcast lives."

### 7.2 Blast radius of a leaked link

| | SFTP password (`.fmscfg`) | Broadcast token (`.spres`) |
|---|---|---|
| Grants | Every shared folder, browse + (optionally) write | One file, or one frozen list, read-only |
| Revocable individually | No | Yes |
| Expires | No | Yes, by default in 24 h |

This asymmetry is the reason the gateway does not simply reuse the SFTP credential.

### 7.3 The internet path is a decision, made twice

Off by default (`LanOnlyExport` is honoured). Turning it on for a broadcast shows a warning in the spirit of the existing `ShareText.InternetWarning`, adapted to the real difference: **HTTP is not encrypted**, so anyone between the two machines can see the film and the token. The suggested lifetime for an internet-exposed broadcast defaults to the shortest option.

### 7.4 Privacy surfaces stay consistent (invariant 14)

The privacy page, the Store data-safety answers and the README all currently describe one listening service. They are updated in the **same** change that lands the gateway: one more local listener, opened by explicit user action, contacting no server of ours, sending nothing anywhere. The "no telemetry" claim stays true and stays verifiable - the gateway's counters are the local `stats.json` and nothing else.

---

## 8. Localization

Both products are 13-language and both land their strings in the same change (invariant 17):

- FMS/Companion: keys through `Localization.T`/`TF` with the Russian source string as the key; anything carrying a value (title, expiry, item count, host) uses `TF` with a placeholder, never concatenation. `LocalizationParityTests` + `LocalizationCoverageTests` must stay green.
- StreamsPlayer: keys in all thirteen `Localization.<code>.xaml` dictionaries; `LocalizationParityTests` and `LocalizedCallSiteTests` gate placeholder arity against the call sites.
- No smart double quotes in any literal, in either product.

---

## 9. Phases

| # | Repository | Deliverable | Proof |
|---|---|---|---|
| **1** | `fms_companion` | `internal/httpgw` + tokens + `broadcasts.json` + the five IPC verbs + status fields | Go tests: range/partial content, method refusal, unknown/expired/revoked token → 404, no traversal, snapshot immutability, limits |
| **2** | this repo | Broadcast policy + create/list/refresh/revoke through `WorkerIpc`, `BroadcastConfigBuilder` (`.spres` + QR + `SPCH1`), the Broadcasts page | Manual: create → save → revoke → the link dies; older-worker degradation message |
| **3** | this repo | The two LITE menu items and the `::fms-broadcast::` forward | Manual: right-click a film → the Share Manager opens prepared |
| **4** | `Streams_Player` | Reader + import surfaces + association + additive channel fields + address race + playlist playback + export-safety widening | Core tests for the reader and the URL composition; run-and-observe for playback on **both** backends |
| **5** | both | README ×3, site, privacy page, store listing copy, CHANGELOG | `Build-SitePages.ps1 -Check` clean |

Phases 1-3 are useful without phase 4 only insofar as the link plays in VLC; the feature is **not** deliverable until phase 4 ships, so no release announces it before then.

---

## 10. Acceptance criteria

1. A film broadcast from FMS plays in StreamsPlayer on **another machine on the same LAN**, imported from a `.spres` file, and **seeks** to an arbitrary position.
2. The same channel plays on **both** video backends (LibVLC and Flyleaf) with no setting change.
3. A folder broadcast appears as **one** channel row and plays its items in order, showing `n/total`.
4. Moving the PC to a different LAN address leaves the channel playable without re-import (the race picks another stored address), and the case where no address answers produces the document's `accessNote`, not a silent failure.
5. Revoking a broadcast makes the channel fail with "no longer available" within one playback attempt; every other broadcast keeps working.
6. Enabling the gateway on a machine where the Share opt-in was already granted raises **no** new UAC prompt, and the port is reachable from another machine - i.e. the program-scoped firewall rule genuinely covers the second listener (§3.3).
7. `GET` with `Range`, `HEAD`, unknown token, `POST`, and a `../` in the request target all produce the answers in §3.1, verified against the running worker.
8. An older `fms-share-worker.exe` (no broadcast verbs) leaves the Share Manager fully usable and shows one clear "update needed" line.
9. A catalog refresh in StreamsPlayer leaves broadcast rows untouched, and exporting a list that contains one warns before writing the token to disk.
10. `.spres` round-trips: file, QR (both plain and `SPRES1:` compressed), and clipboard line all yield the same playable channel; a document with `schemaVersion: 2` is refused with an explanation.
11. Both localization test suites stay green in both repositories.

---

## 11. Planned files

**`fms_companion`**: `internal/httpgw/{gateway,tokens,ranges,snapshot}.go` + tests; `internal/ipc/protocol.go` (types + payloads); `internal/app/app.go` (dispatch); `internal/netaccess` (second mapping); `docs/CONFIG_FORMAT.md` (a section pointing at the `.spres` contract).

**this repository**: `src/FastMediaSorterCompanion/Core/BroadcastConfigBuilder.vb`, `Core/BroadcastController.vb`, `Core/WorkerIpc.vb` (verbs), `Forms/Share_Broadcasts_Form.vb` (or a page in `MainWindow.vb`), `Forms/Broadcast_Create_Form.vb`; `src/Main_Form.VideoMenu.vb`, `src/Main_Form.FolderMenu.vb`, `src/Main_Form.ShareLauncher.vb`; `docs/contracts/CONTRACT_BROADCAST_RESOURCE.md`; localization tables; `CHANGELOG.md`.

**`Streams_Player`**: `src/StreamsPlayer.Core/BroadcastResource.cs`, `BroadcastResourceReader.cs`, `BroadcastUrl.cs`; `src/StreamsPlayer.App/MainWindow.ImportExport.cs`, `MainWindow.Launch.cs`, `PlayerWindow.*` (playlist advance), the association in the installer/MSIX manifest, 13 localization dictionaries, `tests/StreamsPlayer.Core.Tests/*`.

---

## 12. Invariants

1. **The player accepts one class of addresses.** `IsLaunchable` stays `http`/`https`/`rtsp`. Nothing in this feature widens it.
2. **No transcoding, ever, anywhere in this path.** The gateway serves bytes.
3. **A token grants one broadcast.** It is never the SFTP credential, never reused across broadcasts, never logged in full.
4. **The gateway is off until switched on**, inside a Share surface that is itself behind `ServerFeatures.IsEnabled()`.
5. **No new privileged operation.** No service, no elevation, no second firewall rule, no admin path. If a change appears to need one, it does not ship.
6. **LITE stays a launcher.** No HTTP, no token, no document, no worker knowledge on the LITE side.
7. **`.fmscfg` is untouched.** `.spres` is a separate contract with its own `schemaVersion`; a change to either is a version bump, never a silent shape change.
8. **Broadcast rows never reach the published catalog.** They are `Imported` user rows; the stream bank stays a curated public list of public streams.
9. **The worker binary is built only in its own repository**, and its data dir - host key included - is never regenerated by this feature.
10. **Delivery stays document-based.** Fast Media Sorter does not look for, launch, or version-check `StreamsPlayer.exe`; neither product may become a runtime dependency of the other.

---

## 13. Out of scope

- **HLS / live remux** ("a real broadcast"). Deliberately deferred: it needs a transcoder in the loop and buys nothing for a file the player can already decode. If it ever ships, it is an additional `protocol` value in `.spres`, not a redesign - and note that `M3uPlaylistParser` refuses `#EXT-X-` bodies today, so the player side would need its own work.
- **HTTPS with a self-signed certificate.** It would trade an honest warning for a certificate error inside the player. Revisit only with a real name and a real certificate.
- **DRM-protected discs or streams.** Nothing here circumvents anything; the gateway serves files the user already has.
- **Launching StreamsPlayer from FMS** (`--url`), and **teaching the player `sftp://`** - both were considered and declined on 2026-08-08.
- **Android**: `.spres` targets the desktop player. The phone keeps `.fmscfg` and SFTP.
