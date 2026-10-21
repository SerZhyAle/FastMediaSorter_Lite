# exchange-server

<img src="../assets/exchange-mark/png/full-128.png" width="128" height="128" alt="Media Exchange Server">

The exchange server of the Fast Media Sorter package: accounts, enrolled devices, the directory of
resources and broadcasts, tunnels and the third-party port (contract `DEVICE-EXCHANGE` 1.1, read 2026-10-16, with
`ANYWHERE-ACCESS` 5.5 for the port; specifications 160-164). Developer notes only - the user-facing guide
arrives with ticket 145/179. The reader-facing description is the portal page [Exchange server](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-server.html) of the shared "Media exchange between your devices" portal (not published until the Android site publishes it; it describes the contract, not a shipped product). **Nothing here ships in any release package yet** (`INCLUDE_EXCHANGE` is `'false'` in `release.yml`): the contract left `[PROPOSED]` at 1.0 and no joint rung has run (205), and the installers, the Share Manager entry and the firewall opt-in are tickets
141-144.

The wire is `schemaVersion` 2. The superseded `ANYWHERE-ACCESS` 0.10 wire (`schemaVersion` 1: one server
password, `register`/`registered`) is **not** kept alive anywhere: the number 1 appears in the code in exactly
one place, the refusal that recognises it (`wire/codec.go`, held by a source scan). Built since: the
fan-out relay (165), cast routing (166), the disk, log and limit guards of ticket 167, and the vector
generator and the probe (178, T-1 and T-2) - see the implementation records of those tickets.

One Go program, `fms-exchange-server.exe`, windows/amd64 first. It is pure Go with no cgo and a
`GOOS=linux` build that must keep working (a future Linux microservice replaces `internal/deploy`,
never the protocol core).

## Layout

| Path | What it is |
| --- | --- |
| `cmd/fms-exchange-server` | the program: `serve` (console, or `--service` under the SCM), `stop`, `account add/passwd/remove/list`, `device list/revoke`, `set-listen`, `set-endpoint`, `connection-string`, `inspect`, `--version` |
| `cmd/exchange-fakeproducer` | **manual-proof tool, its own module** (enrolls into an account, publishes a share and serves a real SFTP subsystem through the bridge); see `testdata/manual-winscp.md`. Kept for the one thing the probe cannot do - serve a real SFTP subsystem for WinSCP or FileZilla (that needs `pkg/sftp`, which stays out of this module) |
| `cmd/exchange-vectors` | the conformance vector generator (ticket 205 T-1): runs the real server against a scripted client with an injected clock, seeded ids and tokens, and writes `testdata/vectors/frames-v2.json`, `records-v1.json` and `PROVENANCE.txt`. `go run ./cmd/exchange-vectors -out testdata/vectors` writes, `-check` regenerates and compares (one verdict line). Developer tool, never installed |
| `cmd/exchange-probe` | the self-test client (ticket 205 T-2): `exchange-probe --server host:port --fingerprint SHA256:.. --login x --password-stdin` runs enroll, hello, publish, list, pairing, subscribe, tunnels by resourceId and shareId, relay upload and listen, cast and revoke, leaves the account as it found it and prints one verdict line. Developer and joint-test tool, never installed |
| `internal/wire` | the typed envelopes of `schemaVersion` 2, the codec, the 4-byte length framing (`MaxFrame` 16384 before `welcome`, `MaxFrameAuth` 262144 after), the verbatim records, the nine reasons, the HTTP-opening reader - the only place that builds or parses a wire byte |
| `internal/accounts` | accounts, enrolled devices, token hashes, tombstones (`accounts.json`, `devices.json`) and the in-memory pairing codes |
| `internal/directory` | the per-account directory: presence, `list`/`changed` with a revision per transaction, resources (on disk without `access`, `shareId`, `publicPort`) and broadcasts (memory only), `directory.json` |
| `internal/tunnel` | the table of pending and bridged tunnels, the per-resource and per-broadcast caps, abort by resource, broadcast or device |
| `internal/admin` | the operator's operations (shared by the running server and the offline commands) and the local admin channel protocol |
| `internal/rendezvous` | the connection layer and the stream state machine (`conn.go`, `stream.go`: three openings on one port, TLS-only envelopes, the role of a stream) and the real `Backend` (`engine*.go`: enroll/hello, control stream, tunnels, the third-party port) |
| `internal/relay` | the splice (opaque bytes both ways, half-close, idle timeout) and the fan-out ring of ticket 165: one ring per broadcast, a cursor per listener, a 2 s / 8 MiB window, a listener that falls behind skips to a joinable chunk and never stalls the upload |
| `internal/cast` | the table of pending cast offers (ticket 166): memory only, three ids and a deadline, 16 per account, a 60 s answer window |
| `internal/statement` | the operator statement of `DEVICE-EXCHANGE` 7.9 (what the operator of a server can see), as keyed points the administration surface translates |
| `internal/ports` | the TCP port pool of published resources (third-party SFTP clients) |
| `internal/limits` | token buckets per address and class, the Argon2id verification bound, the pre-auth stream pool |
| `internal/logx` | the only logger: sessions are named by 8 hex characters of SHA-256, never by id |
| `internal/password` | the password policy and the Argon2id record (account passwords) |
| `internal/tlsid` | the self-signed identity, pinned by producers, never regenerated |
| `internal/config` | `exchange.json` (no secret in it; `publicEndpoint`, limits, admin SIDs) and the data directory |
| `internal/deploy` | the ONLY OS-specific layer (key-directory ACL, the stop event, the SCM wrapper and the service probe of ticket 144, the data-directory lock, the admin pipe or socket) |
| `internal/testkit` | wire client, scripted devices that enroll and say hello, a producer that serves a real SSH server through the bridge, a started server - used by the tests |

## Build and test

```powershell
.\exchange-server\build.ps1 [-Version 26.10.6.1630] [-OutDir <dir>] [-Linux]   # exe + .sha256, exit 0 / 1 / 2 (2 = no 64-bit Go)
.\tools\Run-AllTests.ps1 -SkipGo                                               # includes the exchange-go row
```

`build.ps1` resolves a **64-bit** Go (the `go` first on PATH can be 32-bit), pins `GOARCH=amd64`,
stamps the exe with the version twice (`-X main.version` and a PE version resource from goversioninfo
v1.4.1, so `tools\Test-ArtifactVersion.ps1` can judge it), checks the PE machine type and writes the
`.sha256` sidecar. The same stamp builds the same bytes.

Module tests alone:

```powershell
cd exchange-server
$env:GOARCH='amd64'; $env:CGO_ENABLED='0'
go test -count=1 ./...
```

### The race detector

It needs cgo and a C compiler. On Windows install MinGW-w64 (`winget install
BrechtSanders.WinLibs.POSIX.UCRT`); `tools\Run-AllTests.ps1` finds it (on `PATH` or in the WinGet package
folder) and adds a `-race` step to the `exchange-go` row, and says `race not run (no C compiler)` on a
machine without one - it is never silently absent. By hand:

```powershell
cd exchange-server
$env:GOARCH='amd64'; $env:CGO_ENABLED='1'     # gcc on PATH
go test -race -count=1 ./...
```

### Linux (hand step)

The same suite also runs on Linux, which proves the portability boundary. WSL does:

```sh
export PATH=/tmp/fms-go/go/bin:$PATH            # a Linux Go (go1.26.8) unpacked there
CGO_ENABLED=1 go test -race -count=1 ./...      # copy the module out of the Windows drive first
GOOS=linux go vet ./...
```

The race run found one real race while this was written (a log call read a registration's lease
outside the lock); it is fixed, and the suite passes under `-race` on Linux.

## Running it: console, `stop`, service

```text
fms-exchange-server serve [--config <file>]            console server; Ctrl+C or `stop` ends it cleanly
fms-exchange-server stop  [--config <file>]            asks that console server to stop; does not wait for it
fms-exchange-server serve --service [--config <file>]  only under the Windows service control manager
fms-exchange-server account add <login> [--generate]    an account; the password is read from stdin or made by --generate and printed ONCE
fms-exchange-server account passwd|remove|list ...      change, delete or list accounts (passwd --sign-out revokes every device)
fms-exchange-server device list <login> | revoke <login> <deviceId>
fms-exchange-server admin status [--json]               remote administration status and settings
fms-exchange-server admin on [--generate] [--allowed <list>] [--lifetime <minutes>]
fms-exchange-server admin off
fms-exchange-server admin passwd [--generate]           sets or generates the administrator password
fms-exchange-server admin set [--allowed <list>] [--lifetime <minutes>] [--web-dir <dir|->] [--origins <list|->] [--origin <o>].. [--remove-origin <o>].. [--ws-max-message <bytes>]
```

**Accounts.** `serve` refuses to start with no account (exit 3, it says how to add one). The `account` and
`device` commands take the data directory's lock when no server holds it and edit the stores directly; when a
server holds it they use the local admin channel instead (a named pipe `\\.\pipe\fms-exchange-admin-<8 hex>` on
Windows - the first 4 bytes of SHA-256 over the lower-cased absolute data directory, pinned by
`TestAdminPipeNameVector` - and a mode-0600 socket `admin.sock` elsewhere; one JSON request per connection,
no secret ever returned; extra Windows SIDs from `admin.manageSids` in `exchange.json`). `exchange.json` holds
no secret: accounts live in `accounts.json` (Argon2id records), devices and revoked-token tombstones in
`devices.json` (SHA-256 of a token only), resource records and revisions in `directory.json` - never `access`,
`shareId`, a broadcast or a pairing code. `set-endpoint <host:port|->` stores `publicEndpoint`, announced in
`welcome` (empty: the server serves and hides `tunnel` and `relay`). A leftover `passwordHash` member of the
superseded design is ignored, logged once as `obsolete` and removed by the next config save.

**Exit codes.** 0 ok; 1 a runtime failure (for `stop`: nothing is running for that config, or the
platform has no stop request yet); 2 usage (also `serve --service` from a console, and on Linux);
3 a `serve` with no account to serve, or a password the policy refuses; 4 a config or a store that cannot be read or written (for `serve`, 1 also means another server holds the data directory; for `account`/`device`, 1 also means no such account or device, 2 a bad login);
**5 a console `serve` that refused to start because the Media Exchange Server service is running or its
process holds the configured listen port** ("the service wins", 133 section 6.1). A stop request is
delivered with exit 0; the `stop` line in the log is how a caller sees that the server really closed.

**The stop event (Windows).** A console `serve` creates the named event
`Local\FastMediaSorterExchange-Stop-<8 hex>` as soon as its config is validated, waits on it, and on
signal cancels the same context Ctrl+C cancels: registrations close cleanly and the log gets its
`stop` line. `<8 hex>` is the first 4 bytes of SHA-256 over the **lower-cased absolute path of the
resolved config file** (`--config`, else the data-directory rules), lower-case hex; the test
`TestStopEventNameVector` pins one value so another implementation (the Share Manager) can check
itself. `stop` opens the event and sets it; no event means nothing is running (one line on stderr,
exit 1). The event lives exactly as long as the server and is readable by the current user, SYSTEM and
Administrators only. Two consoles on one config share the event, but the second never gets past
the listen. On Linux SIGTERM is the stop request and `stop` itself exits 1 (the Linux build is
phase 5). The service does not use this event: it is stopped through the SCM.

**`serve --service`** runs the same server under `golang.org/x/sys/windows/svc`: StartPending, a
500 ms start grace (a server that dies at once fails the start instead of reporting Running), Running;
Stop and Shutdown cancel the context and the handler waits up to 25 s for the clean shutdown. A server
that ends on its own is reported as a service-specific non-zero exit (its own 1..5, else 10; a stop
that does not finish is 11), so SCM recovery restarts it. From a console it refuses with exit 2.
Session 0 has no console, so what a console run prints (a missing password, a bad config) is appended
to `<data dir>\service.log`, which exists only once there is something to say.

**Refuse beside the service.** `deploy.ServiceRunning()` asks the SCM with the minimal masks
(`SC_MANAGER_CONNECT`, `SERVICE_QUERY_STATUS`: a standard user gets them) about
`FastMediaSorterExchangeServer`; a service that is not registered is `(false, nil)`. A console
`serve` refuses when the service is running or starting, **or** when the service's process listens on
the configured port (a stopping service no longer says Running but still holds it; the owner of the
port comes from the TCP table). A probe that cannot answer (no SCM access) never refuses on its own:
it prints a warning and the listen decides. The probe is `deploy.Probe`, replaceable in tests.
## Conformance evidence (ticket 205)

`tools/Test-ExchangeConformance.ps1` regenerates the three JSON corpora and checks their provenance,
then builds and probes a fresh server on loopback. The generated files and `PROVENANCE.txt` are vendored
into `tests/Lite.Tests/TestData/exchange-vectors/`; `ExchangeVectorReplayTests` and
`ExchangeRecordReplayTests` run in both modern viewer and Share Manager test assemblies. Missing, changed
or stale vendored transcripts fail. Regenerate at the Go source, then copy all four files together.

The record replay preserves nested unknown members, lists unsupported kinds, reads Drive-wrapped records
without `access`, and enforces the reader's 16 KiB bound. The server bounds the record as delivered (item BS):
`resource-16KiB-exact` (16384 bytes as sent) is refused `capacity`, and `resource-delivered-16KiB-exact` is the
largest accepted record, listed at 16383 bytes while its device is online and at exactly 16384 once it is offline.
`PROPOSAL-2026-10-11-fmsw-vectors.md` records the earlier finding (a case accepted at 16384 and listed at 16475)
with the artifacts in the contract catalog.
No joint rung is claimed: phone/WinSCP or FileZilla runs, Drive parity, relay latency and cross-product cast
remain pending. Developer tools are not installed; the feature gates remain shut.

## Settled behaviour worth knowing before changing it

* **One answer for unknown, ended, foreign, offline and full.** `refused {reason:"unavailable"}` after a fixed minimum
  delay measured from the connect. `TestUnavailableIsOneAnswerForUnknownEndedForeignOfflineAndFull` compares the bytes and the timing.
* **Take a token before the expensive step, refund it on success** (`limits.Limiter.Refund`). Taking it
  after would let a parallel wave pass a "has a token" check before the first failure was counted.
* **The control stream carries envelopes only, for its whole life.** Every tunnel gets its own data
  stream through `open` / `attach`; after `connected` (or at once on a published resource's port) a stream is
  raw bytes. A plain stream is answered only with a refusal (`tls-required`, `version-unsupported`); every
  `enroll`, `hello` and `connect` runs inside TLS (`DEVICE-EXCHANGE` 15 item V). The frame reader reads exactly one frame so nothing behind it is lost.
* **The rate defaults depart from ticket 139 in one number:** `connection` is 120/min with a burst of
  40, not 30/10. One SFTP client opens up to 15 sessions through the exchange, each a connection from
  the same address; a burst of 10 would turn it away. Working defaults until rung 2.
* **Presence is the control stream.** A device is online while its control stream lives and offline after a
  `bye` or three missed keepalive intervals; offline keeps its resources listed without `access`, `shareId` and
  port lease and ENDS its broadcasts, in one transaction with one revision. A second control stream of the
  same device replaces the first and everything carries over.
* **The logger takes typed fields only.** `internal/logx/guard_test.go` fails on `log`, `log/slog`,
  direct writes to the standard streams outside `cmd/`, and an id or a password passed to `logx.Text`.
  Every test that starts a server also checks, at its end, that no secret it handled is in the log.

### set-listen

`fms-exchange-server set-listen [--config <file>] <host:port>` stores the rendezvous listen address (for example `:22022`), validated by the rules `serve` uses, and keeps every other member. A UI changes the port through it instead of editing the JSON. A running server reads the file only at start. `config.Save` rewrites the file in place when the caller may modify it but not add files to its directory (the service directory of ticket 144).

### set-endpoint

`fms-exchange-server set-endpoint [--config <file>] <host:port>` stores the public address announced in `welcome.publicEndpoint` (validated like any address, never loopback or a wildcard); `-` clears it. Like `set-listen`, a running server reads it only at start.

### connection-string

`fms-exchange-server connection-string [--config <file>] [--name <server name>] [--login <login>]` prints, on one line, the **connection string** clients paste into "Add from an exchange server.." (ticket 187, `DEVICE-EXCHANGE` proposal 5.5): `FMSEXCH1:x=<host:port>[&fp=SHA256:..][&n=..][&u=..]`. It is built from the configured `publicEndpoint` and the certificate the server really presents - never from a bind address or a guess - and `fp` is written only when that leaf is not validated by the platform trust store (a CA-signed deployment renews its leaf and writes none). It is not a secret and carries none: no password, token or pairing code. Exit codes: `0` the string on stdout; `2` no public endpoint is set (say to set the address for clients first) or the name or login cannot be carried in a string; `4` the config or the certificate cannot be read (the server never started). The codec is `internal/connstr`; its generated conformance file is `testdata/vectors/connection-string-v1.json` (`go run ./cmd/exchange-vectors -out testdata/vectors`), which the Windows viewer's VB codec replays.

### Remote administration (DEVICE-EXCHANGE 1.4, ticket 245)

The server provides a remote administration surface on both openings:
* The length-prefixed TLS envelope stream (`0x00`) per `REMOTE-ADMIN.md`.
* The WebSocket opening (`/v2/ws`) per `WEB-BINDING.md`.

Administration streams belong to no account and no device: no `welcome`, no presence, no directory record, and not counted in `Online`.

Key guarantees:
* **Off by default**: Configured in the `admin` section of `exchange.json`. When off, `admin-auth` is answered with `refused {reason:"admin-disabled"}`.
* **Per-client-address pacer**: Keyed by the limiter's client key alone (`limits.Key`: the address, an IPv6 client by its /64; not login). At most 1 evaluation per second per address; a third attempt within a slot is refused `rate-limited` immediately.
* **Concurrency bound**: Shares the password verifier capacity cap with other password checks (`refused {reason:"capacity"}`).
* **Session and token**: Successful `admin-auth` replies with `admin-auth-ok {token, expiresSeconds}`. The token lives in memory, bound to its stream, and lapses after inactivity timeout (default 15 minutes) or when the stream closes.
* **Carried operations**: exactly the 18 operations of `WEB-BINDING` operator-administration "Which operations" (`REMOTE-ADMIN`), listed below. The allowlist is `internal/rendezvous/admin_stream.go` (`carriedOps`); any other operation is answered `bad-request`. `invitation.create` on the wire returns `connectionString`.
* Host settings, operating-system matters (service, firewall, port forwarding, certificate/keys) and the remote admin switch itself are never carried over the wire. `grant.add`, `grant.remove` and resource `access` stay uncarried per Rule 9 audit; the visibility operations of the local channel (`broadcast.list`/`.end`, `resource.list`/`.unpublish`, `clipboard.info`/`.erase`, `admin.audit.list`) are not on the remote wire either.
* Conformance vectors are generated by `cmd/exchange-vectors` (`admin-native` and `admin-websocket` transcripts) and verified byte-for-byte with `-check`.

Both transcripts cover every carried operation and the disabled, excluded-address, wrong-password,
rate-limited, verifier-capacity and expired-session cases. The Share Manager offers local settings and
a browser link, copy and QR dialog with the certificate fingerprint and trust warning. Settings changes
apply on the next server start. The link is available only with administration enabled and `webDir`
configured. Cross-product runs with Android and Web remain open; these local checks do not prove them.

### The page and the WebSocket (WEB-BINDING, ticket 244 slice GO-2)

**`webDir`** (`exchange.json`; `admin set --web-dir <dir>` stores it as an absolute path, `-` clears it) is the directory the server
serves at `/` on the TLS port, and nothing else is ever served from the file system.
* **Where it may point.** `Validate` and the start of `serve` (exit 4, "the config is not valid") refuse a `webDir` that is the data
  directory, lies inside it, contains it, is the TLS directory (`<data>/tls`), lies inside or contains it, or contains the private key
  file (`tls.keyFile`, else `<data>/tls/key.pem`). A link is resolved first, so a junction to the data directory is the data directory.
  `admin set` runs the same check before it writes. Tests: `TestWebDirIsKeptOutOfTheDataAndTLSDirectories`,
  `TestWebDirThroughALinkIsTheDirectoryItLeadsTo`, `TestWebDirDoesNotContainTheConfiguredPrivateKey`,
  `TestServeRefusesAWebDirThatIsOrHoldsTheDataDirectory`, `TestAdminSetRefusesWhatTheConfigRefusesAndWritesNothing`.
* **What a request may name** (`internal/rendezvous/static.go`). The raw path is split on `/` first, then each segment is
  percent-decoded **once**; a second `%XX` in the result is refused. Refused: `.` and `..` and every dot component, `\`, `:` (NTFS
  streams, drive letters), `*?<>"|`, control characters, an empty segment, a trailing slash (no listing, ever), a trailing dot or space,
  8.3 short names (`NAME~1`), Windows device names, a component over 255 bytes, a path over 1024 bytes, invalid UTF-8, and the
  reserved first component `v2` in any spelling. The type comes from a **closed table** (html, htm, js, mjs, css, json, map,
  webmanifest, txt, xml, svg, png, jpg, jpeg, gif, webp, avif, ico, wasm, woff, woff2, ttf, otf); an extension that is not in it is a
  404, not `application/octet-stream`. Key and key-store extensions and names (`.key .pem .pfx .p12 .p8 .pk8 .der .jks .keystore
  .ppk`, `id_rsa`, `id_ed25519` and the like) are refused wherever they appear after the first dot, and a file whose first line is a PEM
  `PRIVATE KEY` header is refused whatever it is called.
* **No link is followed.** The root is resolved once; each component is `Lstat`-ed and a symlink, a junction or any other reparse point
  is a 404 even when it points back inside the root; the opened file must be the file that was `Lstat`-ed (`os.SameFile`), regular,
  at most 32 MiB.
* **The answer.** A refusal is a bare `404` with `Content-Length: 0` and no reason; a method other than GET is `405`. A `200`
  carries exactly `Content-Type`, `Content-Length`, `X-Content-Type-Options: nosniff` and `Connection: close` - no CORS header,
  no cookie, no cache header - and the file is streamed in 32 KiB chunks.
  Tests: `TestStaticPartsDecodeOnceAndRefuseWhatIsNotAPlainPath`, `TestStaticPolicyIsAClosedTableWithoutKeyMaterial`,
  `TestLooksLikePrivateKeyReadsTheHeadOfAFile`, `TestOpenStatic*` (junction, directory symlink, file symlink, size bound, key by
  content) and, over real TLS, `TestStaticFilesAreServedWithTheFixedHeaders`, `TestStaticRefusalsAreABare404ForEveryFormOfAnEscape`.

**Origins** (`admin.origins`; `admin set --origins <list|->`, `--origin <o>` and `--remove-origin <o>`, repeatable). An `Origin` header
is accepted only when it is `https://` with no path, query, userinfo or wildcard, and its host and **effective** port (443 when
absent) equal the request's `Host` header or one listed entry compared the same way; an IPv6 literal is compared as an address
(`[::1]` = `[0:0:0:0:0:0:0:1]`), a trailing dot is a different name, and the origin is never reflected. A listed entry is
validated when the config is read or written. Tests: `TestClassifyOriginHostAndEffectivePort`,
`TestListedOriginsAreValidatedAndNeverWildcards`.
* **A listed origin is a consumer's, never an operator's.** The administration stream over the WebSocket needs the server's own origin
  (the page it serves) or no `Origin` at all (a native client). A stream opened with an origin that is only listed is closed with no
  answer at all - not `admin-disabled`, not `bad-credentials` - and the audit line says `REFUSED:origin-not-same`. Test:
  `TestOperatorStreamOverTheWebSocketNeedsTheServersOwnOriginOrNone`.

**The message cap** (`limits.webSocketMessageBytes`; `admin set --ws-max-message <bytes>`, 0 = the default) caps one WebSocket message
in both directions; the server closes 1009 above it. Default 1048580 (the largest relay chunk with its length word), accepted
range 262148 to 16777216. `admin status` prints it.

**Every stream kind runs on the WebSocket** (`WEB-BINDING` websocket-binding "Streams", ticket 244 slice GO-3). After the `101` the first
envelope of the socket fixes its kind exactly as on the TLS opening - `hello`, `enroll`, `register`, an outsider `connect` or `admin-auth` -
and the same state machine (`serveStream` in `internal/rendezvous/stream.go`) serves it, so a browser device (`product` `fms-web`, stored and
listed raw) can sign in, enrol, register, use the clipboard and relay through the server. One WebSocket is one stream: a second stream kind
on it closes it (an `admin-auth` after `hello`, a `hello` after `admin-auth`). A device on a control stream is offline as soon as its socket
closes, or after the keepalive window (`keepaliveSeconds` is clamped to 10..120); a clipboard put ends at once on the empty message; an
outsider relay passes the half-close in both directions. The client address of the per-address limits and of the audit line is the TCP peer,
never a forwarded header. A device stream follows the Origin rule above - the server's own origin, no `Origin`, or a listed origin; any other
is a `403` before an envelope is read - and only the administration stream refuses a merely listed origin. Tests: `ws_streams_test.go`.

**The remote wire carries exactly the 18 operations** of the contract and nothing else: `account.add`, `account.passwd`,
`account.remove`, `account.list`, `account.kind`, `account.limits`, `device.list`, `device.revoke`, `device.pairing-code`,
`invitation.create`, `invitation.list`, `invitation.withdraw`, `shared_invitation.status`, `shared_invitation.set`, `policy.status`,
`policy.set`, `grant.list`, `status`. `broadcast.list`, `broadcast.end`, `resource.list`, `resource.unpublish`, `clipboard.info`,
`clipboard.erase` and `admin.audit.list` are answered `bad-request` on the remote stream and stay on the local administration channel
(`internal/admin`, the Share Manager's pipe; `visibility_test.go`). Tests: `TestCarriedOperationsAreExactlyTheEighteenOfTheContract`,
`TestRemoteAdministrationCarriesExactlyTheContractOperations`.

**The broadcast list of the local channel** (`broadcast.list`, `Ops.ListBroadcasts`; ticket 244 slice GO-4) answers the listeners a
broadcast has at that moment: the relay's listeners plus the tunnels of the broadcast, the two that share its `maxListeners`
(`Engine.broadcastListeners`, installed in the directory with `SetListeners` and asked after the directory's lock is released). A broadcast
nobody listens to, an ended one and a server without a relay say `0`. `bitrateEstimateKbps` is absent on purpose: the relay counts the bytes
of a broadcast over its life, not a rate, and a number is not invented. Tests: `TestBroadcastListShowsTheRelayListenersThatAreAttachedNow`,
`TestBroadcastListCountsTunnelsWithTheRelayListenersAndSurvivesAnEnd`, `TestBroadcastListCountsListenersFromTheHostAndZeroWithoutOne`.

**The session list and the audit ring are anonymous about the product on the remote path.** `admin-auth` holds only the password,
`admin-request` has no product member, and an operator is no enrolled device that could lend its record's product, so the server cannot
know which program signed in (a member a client adds is ignored, never recorded). Every remote operator is therefore `remote` in
`admin.audit.list` (`clientProduct` of a session and of a record) and in the product slot of the file audit (`[remote @ addr transport]`,
formerly `unknown`); only the local channel - the Share Manager's pipe and the command line - is told apart, by `local`
(`admin.ClientProductLocal` / `ClientProductRemote`, the one place the words live; the Share Manager takes a session whose product is not
`local` for an external administration). The transport of the remote stream (`TLS-stream` or `WebSocket`) is in the file audit line, not
in the ring. Tests: `TestRemoteAdministrationIsAnonymousAboutTheProductInTheRingAndTheFileAudit`,
`TestLocalChannelRecordsLocalInTheRingAndInTheFileAudit`, `TestClientProductWordsAreTheOnesTheShareManagerReads`.

**The administration pace** is keyed like every other limiter, by `limits.Key` (the address; an IPv6 client by its /64), so rotating
addresses inside one /64 is one client. Test: `TestAdminPaceKeysByTheLimitersKey`.
