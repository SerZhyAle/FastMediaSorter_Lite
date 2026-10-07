# exchange-server

The exchange server of the Fast Media Sorter package: accounts, enrolled devices, the directory of
resources and broadcasts, tunnels and the third-party port (contract `DEVICE-EXCHANGE` 0.17, with
`ANYWHERE-ACCESS` 5.5 for the port; specifications 160-164). Developer notes only - the user-facing guide
arrives with ticket 145/179. **Nothing here ships in any package yet**: the wire is a proposal until rung 2
freezes it (133 section 8), and the installers, the Share Manager entry and the firewall opt-in are tickets
141-144.

The wire is `schemaVersion` 2. The superseded `ANYWHERE-ACCESS` 0.10 wire (`schemaVersion` 1: one server
password, `register`/`registered`) is **not** kept alive anywhere: the number 1 appears in the code in exactly
one place, the refusal that recognises it (`wire/codec.go`, held by a source scan). Not built yet: the
fan-out relay (165), cast routing (166), the disk/limit guards of ticket 167 beyond what 162-164 needed, and
the vector generator and probe (178) - see the "Implementation record" of tickets 161-164.

One Go program, `fms-exchange-server.exe`, windows/amd64 first. It is pure Go with no cgo and a
`GOOS=linux` build that must keep working (a future Linux microservice replaces `internal/deploy`,
never the protocol core).

## Layout

| Path | What it is |
| --- | --- |
| `cmd/fms-exchange-server` | the program: `serve` (console, or `--service` under the SCM), `stop`, `account add/passwd/remove/list`, `device list/revoke`, `set-listen`, `set-endpoint`, `inspect`, `--version` |
| `cmd/exchange-fakeproducer` | **manual-proof tool, its own module** (enrolls into an account, publishes a share and serves a real SFTP subsystem through the bridge); see `testdata/manual-winscp.md`; ticket 178's probe will supersede it |
| `internal/wire` | the typed envelopes of `schemaVersion` 2, the codec, the 4-byte length framing (`MaxFrame` 16384 before `welcome`, `MaxFrameAuth` 262144 after), the verbatim records, the nine reasons, the HTTP-opening reader - the only place that builds or parses a wire byte |
| `internal/accounts` | accounts, enrolled devices, token hashes, tombstones (`accounts.json`, `devices.json`) and the in-memory pairing codes |
| `internal/directory` | the per-account directory: presence, `list`/`changed` with a revision per transaction, resources (on disk without `access`, `shareId`, `publicPort`) and broadcasts (memory only), `directory.json` |
| `internal/tunnel` | the table of pending and bridged tunnels, the per-resource and per-broadcast caps, abort by resource, broadcast or device |
| `internal/admin` | the operator's operations (shared by the running server and the offline commands) and the local admin channel protocol |
| `internal/rendezvous` | the connection layer and the stream state machine (`conn.go`, `stream.go`: three openings on one port, TLS-only envelopes, the role of a stream) and the real `Backend` (`engine*.go`: enroll/hello, control stream, tunnels, the third-party port) |
| `internal/relay` | the splice: opaque bytes both ways, half-close, idle timeout |
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
