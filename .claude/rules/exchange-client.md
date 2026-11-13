---
paths:
  - "src/Exchange/**"
  - "src/Exchange_*.vb"
  - "tests/Lite.Tests/Exchange*.vb"
  - "tests/Lite.Tests/ConnectionDiagnosis*.vb"
  - "tests/Lite.Tests/FmsSftpCode*.vb"
  - "tests/Lite.Tests/DriveClientDeliveryTests.vb"
---

# Device-exchange client library

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

**[src/Exchange/](../../src/Exchange)** is the client of the device exchange (`DEVICE-EXCHANGE`, pointer [DEVICE-EXCHANGE.md](../../docs/contracts/DEVICE-EXCHANGE.md); wire `SchemaVersion` 2) and of remote administration (`REMOTE-ADMIN`, `ANYWHERE-ACCESS`). The server, the Share Manager's agent and the packaging are in [share-companion.md](share-companion.md); exchange entries shown as resources are in [resources-secure.md](resources-secure.md).

**One folder, three compilers.** Every file is whole-file `#If Not NETFRAMEWORK`. The viewer takes the folder by its `..\**\*.vb` glob, the Share Manager by ONE wildcard link in its vbproj, `Lite.Tests` by a wildcard link, the x86 project by nothing (`ExchangeClientGuardTests` holds all of it). A new file therefore lands in the Share Manager too: it may name no viewer-only code (`Main_Form`, `Localization.`, `MessageBox`) and needs no entry in the net48 list. The windows are outside the folder (`src/Exchange_*.vb`, `Main_Form.ResourceOpen.vb`) and are skins over it.

**Gates** (read through the class, never the constant or the environment)
- `ExchangeFeatures.IsEnabled()` / `BuildOffers()` is the one reader of the CLIENT side. `Offered` is True; the switch `FMS_EXCHANGE_CLIENT=1` and an installed server component (`ComponentInstalled()`) also turn the client on. `ExchangeClientGuardTests` allows True only with a reviewed evidence file. The SERVER side is not behind this class, and no client surface may mention `ExchangeProcess`.
- **Dormant**: with no account nothing connects, listens, writes or starts, and *reading* creates no key, value or folder (`ExchangeAccountStore.AllNames`, `ExchangeDormancyTests`).
- **Remote administration is a second, shut gate**: `ExchangeRemoteAdminFeatures.Offered = False` in the Share Manager (`Core/RemoteAdmin`), switch `FMS_EXCHANGE_REMOTE_ADMIN=1`. No viewer source may name `ExchangeRemoteAdminSession`.

**One place per decision** (never add a second)
- Wire numbers and the refusal table: `ExchangeWire` (frame limit 16 KiB before authentication, 256 KiB after). `ExchangeRefusal` and its `Names` are **append-only and index-addressed**: a reorder or removal changes a reason's meaning on the wire.
- Message shapes: `ExchangeEnvelope` is a **table of rows**, not hand-written serialisers; records keep members as raw JSON so a newer member passes through. `ExchangeJson` is the Go-compatible writer, `ExchangeFrames` the framing (reserved flag bits refused). A member changes in its row and in the Go module `exchange-server/internal/wire` together. Conformance is the contract's **vectors** (`ExchangeVectorReplayTests`), never a reading of the code; a client never writes `schemaVersion` 1.
- Retry or stop on a refusal: `ExchangeRefusalPolicy.ForHello` and `ExchangeBackoff`. A session never decides this itself; bad credentials, a revoked or signed-out device, an unsupported version and a pin mismatch are **never retried**.
- The sentence a person reads: `ConnectionDiagnosisMessages` and `ConnectionDiagnosisEngine` (ordered, read-only steps; `ToRedactedDiagnosticString` is the only text that leaves the machine). **No other library file holds a user-facing string** (guard test); the `ConnectionDiagnosis*` files, `OutsideVisibilityChecker` and `Connection_Diagnosis_Dialog` are the exempt ones.
- What a pasted thing is: `ExchangeConnectInput.Classify`; the window-free join is `ExchangeConnectFlow` / `ExchangeConsumer`. A usable `access`: `ExchangeResourceAccess`.

**Connection string** (`ExchangeConnectionString`, a pure codec, the second implementation of Go `internal/connstr`): prefix `FMSEXCH1:`, at most 512 characters; a `.fmsexch` file is one such line, at most 4096 bytes. It names ONE server and its certificate pin and **carries no password, token, device id or key**; only the one-time invitation is a capability, kept as a secret holder (never logged, never kept after use). The name in it is a label of unverified origin, never a title. A malformed string is refused WHOLE; an unknown key is ignored. A string from outside **fills the form and stops**: nothing is sent until Connect, and the destination is on screen before the password field takes focus.

**Secrets and storage**
- The **device token** lives only in a DPAPI (CurrentUser) file per entry (`device-token.dat`), written atomically, bound to its device id, never stored when it cannot be protected. The **password is never stored**: it lives in the sign-in field and the one `enroll` frame and is wiped.
- Non-secret account data is registry values (`Exchange_Account*`, `Exchange_DeviceId`; entries in `Exchange_ViewerEntries`), kept **out of** `LogPackage.SafeSettingNames`. `ToString` of an account or a secret holder names the type only.
- **Logging**: no client log call takes a token, password, code, `access`, id or fingerprint; an id goes through `ExchangeIds.ForLog` only (a guard test scans the statements).
- `ExchangeDirectory.WithoutSecrets` strips the secret members from every mirror saved or handed on: a secret is fetched on demand, never mirrored.

**Trust and TLS** - TLS 1.2/1.3 only; there is no clear path. Two trust functions that stay two:
- `ExchangeTrust.Evaluate` is **pin-first**: a stored pin decides (a mismatch is refused even where the platform accepts), the platform verdict counts only without a pin. Revocation `NoCheck` is deliberate. A pin is replaced only by an explicit user act (`ReplacePin`), never by a retry.
- `ExchangeAdminTrust.Decide` (remote administration) is **chain-first**, by the table in its header.
- Joining by string: a fingerprint in the string means the leaf must equal it, with no prompt; a different leaf is refused showing both fingerprints, with NOTHING pinned and no password sent. Without one, the platform decides first and a rejected leaf is pinned only after the person saw its fingerprint and accepted (default answer No). Enrol order: token stored first, entry written last.

**Accounts, entries, the agent**
- Two shapes of one account: the viewer's **entries** (`ExchangeEntries`, at most `MaxEntries` 16, each with its OWN device id, pin and token folder) and the Share Manager's single producer account (`ExchangeAccountStore`) - two devices of one account.
- Through the agent channel the viewer opens no control stream: it reads the agent's snapshot over the private pipe `fms-device-agent-<hash of the user SID>` (a product surface, not a contract) and opens data streams itself. Current-user DACL, one JSON line per connection, no password; the client **refuses a pipe whose server process is not run by the same user**. `GetAccess` is the one place a secret crosses.

**Sessions and lifetime**
- `ExchangeEntrySession`: callers hold **leases**, the first starts the session, `bye` goes out at the LAST lease. One control stream per device (`ExchangeControlSession`). The caller of a data stream owns and disposes it; `ExchangeBridge` pumps bytes and owns nothing.
- `ExchangeForwarder` binds the loopback address only, exists only while something attaches a tunnel, makes **exactly one opener call per accepted connection** (never zero, never two: a retry burns a tunnel) and caps at `MaxBridges` 64; a tunnel pin hands the connection to `ExchangeSealedTunnel`.
- The outside-visibility check runs **only on an explicit request**, bounded, and its log names no host or address.

## End-to-end encryption and device keys

Files: `ExchangeHpke.vb`, `ExchangeDeviceKey*.vb`, `ExchangeSealed*.vb`, `ExchangeClipboardSeal.vb`. The item letters in the code comments (DH, DK, DM, DN, DP) are items of `DEVICE-EXCHANGE`.

- **The suite is wire-frozen.** `ExchangeHpke` is RFC 9180 Auth mode written by hand: DHKEM(P-256, HKDF-SHA256), HKDF-SHA256, AES-128-GCM, single-shot. Never add a suite, a mode or a sequence counter. The RFC vectors are replayed through the test hooks `SealAuthTraced` / `DeriveKeyPairScalar`; product code never calls them. `ExchangeAead` is the one AES-GCM wrapper; `TryOpen` returns False for a bad size or tag and never throws.
- **One type owns each decision.**
  - `ExchangeDeviceKey`: the key. One P-256 pair per device id; `CheckCodeOf` is the check code people compare. `ExportPrivateScalar` is `Friend` and is called only by the key store; `ToString` returns the type name, so the key never reaches a log through interpolation.
  - `ExchangeDeviceKeyStore`: storage. `device-key-<deviceId>.dat`, DPAPI CurrentUser with its own entropy, written to a temp file then moved. `ExchangeDeviceKeys.LoadOrCreate` is the only creator; the key lives as long as its device id and is never replaced under the same id.
  - `ExchangeSealedBox`: the box. A random content key seals the payload and is wrapped per recipient by HPKE Auth with `info` `fmsx-e2e-v1`; at most `MaxRecipients` 40; `CurrentVersion` is 1 and a reader accepts only that.
  - `ExchangeSealedTrust`, `ExchangeSealedRecipients`, `ExchangeSealedAccess`, `ExchangeSealedGrants`: trust, state per device, the access and grant slots. Do not copy their decisions into a caller.
- **Associated data is the slot, built by the reader.** The context is `clipboard`, `access:<resourceId>`, `grant:<grantId>` or `broadcast:<broadcastId>`, the same string for AES-GCM and for HPKE. The READER builds it from where it found the box, never from a member of the box, so a box moved to another slot does not open. The sender's key must equal the key trusted for that device (`FixedTimeEquals`), else `UntrustedFromKey`.
- **Trust on first use; a kept key never moves.** `sealed-trust.json` is per entry, beside the device token. A different key presented for a device that already has one is `KeyChanged`; only `Forget` / `ForgetAll` drops a kept key. `ExchangeSealState` is `Ready`, `E2eMissing` (no `e2e-v1` capability or no key) or `KeyChanged`. There is no clear fallback: the result is a refusal, and its wording lives in `ConnectionDiagnosisMessages`, never here.
- **A keyless device presents its first key by `enroll` under the same device id** (`ExchangeKeyUpgrade`), with a pairing code asked from its own live stream; `ExchangeKeyUpgradeGate` paces the tries.
- **Inner tunnel TLS** (`ExchangeSealedTunnel`): one self-signed certificate per broadcast, in memory only. The consumer trusts exactly the leaf whose pin it was handed and never falls back to chain validation.
- **Sealed clipboard**: `ExchangeClipboardSeal.SegmentSize` (64 KiB) is wire-frozen.
- **What never leaves memory**: the private scalar goes out only through the key store; content, group and epoch keys are never written to disk, a log or the server.

## Drive channel

Files: `ExchangeDrive*.vb`; the Share Manager side (`DriveChannelHost`, `DriveChannelPublisher`, `DriveShareRecords`, `DriveChannelText`) is under [share-companion.md](share-companion.md).

- **The scope is `drive.appdata` and nothing else.** A token that comes back with any other scope is refused and nothing is stored. The flow is the installed-app one: a loopback listener on `127.0.0.1` (never `localhost`), PKCE `S256`, a `state` check.
- **The OAuth client pair reaches the product as one unit.** `ExchangeDriveClientConfig.Resolve` takes both shell variables (`FMS_DRIVE_CLIENT_ID` + `FMS_DRIVE_CLIENT_SECRET`, a developer override), else both `AssemblyMetadata` values the release build injects into the Share Manager, else nothing. One variable alone is ignored, so the id of one client is never sent with the secret of another. **No tracked file holds a value** (`DriveClientDeliveryTests`); `ToString` never prints it.
- **A build with no pair is `NotConfigured` and nothing else breaks.** The channel is dormant until sign-in: no timer, no request on a signed-out PC. Only the Share Manager signs in; the viewer never holds the pair.
- **Tokens**: the refresh token is DPAPI CurrentUser in `drive-token.dat` and carries a tag of the client id, so a token minted for another client is never offered. Access tokens live in memory only.
- **Failure classes are decided in one place each**: `ExchangeDriveClient` maps HTTP (`SignedOut` after one refresh, `DailyLimit`, `DriveFull`), `ExchangeDriveSession` maps the token endpoint and the sign-in outcomes (`SignedOut`, `ClientRejected`, `ConsentDenied`, `AccountRestricted`), `ExchangeDriveService` ends its loop or backs off on them. The words shown for each state come from `DriveChannelText`, never from these types.
- **Records** are `fmsx-<kind>-<id>.json` and expire on Drive's clock by the working lifetime of their kind (`ExchangeDriveRecords`). A resource record carries NO `access`: it is a listing.
- **The packet queue** is the folder `cross_device_transfer`: a sub-folder per packet, payload files first, `manifest.json` LAST as the commit marker. Names go through `SafeFileName`. A receive writes to a temp file, checks size and SHA-256, and moves it to a free name without overwrite; a mismatch leaves nothing behind.

## Tests

- Lite.Tests: `ExchangeClientGuardTests` (gates, seams, strings, logs), `ExchangeWireTests`, `ExchangeVectorReplayTests`, `ExchangeConnectionStringTests`, `ExchangeTlsTests`, `ExchangeAdminTrustTests`, `ExchangeStoresTests` (real DPAPI), `ExchangeEntryGuardTests`, `ExchangeDataStreamTests`, `ExchangeForwarderTests`, `ConnectionDiagnosisTests`; sealed: `ExchangeSealed*Tests`, `ExchangeClipboardTests`, `ExchangeEntryTunnelTests`; Drive: `ExchangeDrive*Tests`, `DriveClientDeliveryTests` (reads the release workflow and the MSIX script as files).
- Companion.Tests (links the same folder): `ExchangeConsumerTests`, `ExchangeConnectFlowTests`, `ExchangeControlSessionTests`, `ExchangeDevicePolicyTests`, `ExchangeDeviceAgent*Tests`.
