# Contract - Android client reconnect after idle-session close

> Audience: the Android/client team (the app that imports the QR / `.fmscfg` and connects to a Fast Media Sorter Share over SFTP).
> Version: 1.0 (2026-07-15). Pairs with [SPECIFICATION_QR_IMPORT_ANDROID.md](done/SPECIFICATION_QR_IMPORT_ANDROID.md) (frozen wire contract) and [SPECIFICATION_SHARE_SECURITY_HARDENING.md](SPECIFICATION_SHARE_SECURITY_HARDENING.md) §3.4 (server side).
> Status: server side to be implemented in the worker (`fms-share-worker`); client side is this document.

---

## Why this contract exists

To reclaim resources from abandoned connections, the Share server now closes an **idle SFTP session after 3 hours** of no file activity. This is a benign, expected event - not a ban and not a credential change. Without a reconnect rule, a user who leaves a shared folder open on the phone and comes back later would see a spurious "connection closed / access denied" on the next tap. This contract makes that reconnect **transparent**.

Nothing about pairing, credentials, host-key pinning, or the `accessPaths` race changes. This only adds how the client reacts when an existing connection is closed under it.

---

## Server guarantees (what the worker promises)

1. **Idle close is graceful and non-punitive.** After ~3 hours with no SFTP request on a session, the server closes that SSH connection. It is a plain transport close - no auth revocation, no ban, no rate-limit penalty. The client is expected to simply reconnect.
2. **Idle is measured on SFTP activity, not wall clock.** An in-flight transfer (a long download, a slow ranged video read) continuously counts as activity and is **never** interrupted mid-transfer. Only a session with no file I/O for the full window is closed. Low-level SSH keepalives do **not** count as activity.
3. **Stable identity across reconnect.** The host-key fingerprint (`hostKeyFingerprintSha256`, your TOFU pin) and the credentials (`username` / `password`) are **unchanged** by an idle close and across worker restarts and PC reboots. A stored pin and stored credentials remain valid until the user deletes the share. There is no rotation.
4. **No hidden session state.** Every connection is independent: one authentication, then one SFTP subsystem. There is no server-side cursor or handle the client must restore - reconnecting and reopening the paths you care about is sufficient.
5. **Same listen port and access paths.** After an idle close the server is still listening on the same port; the `accessPaths` from the original config (and any mDNS rediscovery by fingerprint) remain valid. Re-run your normal path race; do not assume `accessPaths[0]`.

---

## Client obligations (what the app must do)

### O1 - Reconnect transparently on transport-level failure

On EOF / "connection closed by remote" / SSH disconnect / TCP reset / i/o timeout during any operation - and specifically **when the user taps Refresh** on an open resource - the client MUST, without surfacing an error to the user first:

1. re-dial the resource (race the stored `accessPaths` exactly as on first connect),
2. re-verify the server host key against the **stored pin**,
3. re-authenticate with the **stored credentials**,
4. reopen the SFTP subsystem,
5. retry the pending / refresh operation.

Only if this reconnect sequence itself fails does the client surface an error.

### O2 - Classify the failure; do not blindly retry-loop

The transparent reconnect in O1 applies to **transport-level** failures only. The client MUST distinguish these four classes and act differently:

| Failure observed at (re)connect | Meaning | Required client action |
|---|---|---|
| **Transport** - EOF, connection reset, closed by remote, i/o timeout | Idle close (3h) or a transient network blip | **Reconnect transparently** (O1). |
| **Auth rejected** - SSH authentication failure | Credentials no longer valid (share was deleted and re-created; must not happen from an idle close alone) | Do **not** retry-loop. Surface "re-pair needed" (rescan the QR) and stop. |
| **Host-key mismatch** - presented fingerprint != stored pin | Possible man-in-the-middle. The key never changes on a benign reconnect. | Do **not** connect and do **not** auto-accept the new key. Surface a security warning; keep the old pin. |
| **SFTP status** - permission denied, no such file, etc. | Normal application-level result (read-only root, deleted file) | Surface normally. Do **not** reconnect. |

The host-key rule is the security-critical one: because the server guarantees a stable key (guarantee 3), a mismatch after an idle reconnect is a real red flag, never a routine event.

### O3 - Bounded retry

The transparent reconnect SHOULD retry a small, bounded number of attempts (for example: race the access paths once, then 1-2 backed-off retries) before surfacing an error. **Never** an unbounded reconnect loop - that would hammer the server and drain the phone battery.

### O4 - Keepalive is an optimization, not a substitute

The client MAY keep a session warm with its own SSH keepalives if it wants to avoid the reconnect entirely, but it MUST NOT rely on the server holding an idle session open: the 3h close can still happen (keepalives are not counted as activity by guarantee 2). O1 is the contract; keepalive is only an optimization on top of it.

---

## Interaction with the connection limit

The server also enforces a "maximum simultaneous connections" limit (owner-configurable, default 10). Two consequences for the client:

- On reconnect, briefly **release the old connection first** where possible, so a reconnect does not transiently consume two slots against a low limit.
- If a (re)connect is refused at the transport level because the limit is momentarily full, treat it as a transport failure under O1/O3 (bounded backed-off retry), not as an auth or host-key failure.

---

## Acceptance (client-side conformance)

1. Leave a resource open, force an idle close (server-side the window is 3h; for testing the worker exposes a shortened value), then tap Refresh -> the resource reloads with no visible error, using the stored pin and password.
2. Point the client at a server presenting a different host key -> a security warning is shown and no data is transferred.
3. Delete and re-create the share on the PC (new credentials), then reconnect the old client -> a "re-pair needed" prompt, not a silent retry loop.
4. Pull the network mid-idle and restore it, then act on the resource -> transparent reconnect within the bounded retry budget.

---

## What did NOT change

Pairing, the `.fmscfg` / QR schema, `accessPaths` ordering and the race, mDNS rediscovery by fingerprint, credentials, and host-key pinning are all unchanged. If your client already reconnects cleanly on a dropped connection and pins the host key strictly, you likely already satisfy most of this - O2's four-way classification (especially the host-key-mismatch and auth-reject branches) is the part to verify.
