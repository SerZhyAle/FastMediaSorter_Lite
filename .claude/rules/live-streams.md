---
paths:
  - "src/LiveBroadcast/**"
  - "src/Streams/**"
  - "src/LiveBroadcast_Form.vb"
  - "src/Main_Form.LiveBroadcast.vb"
  - "src/Main_Form.Streams.vb"
  - "src/Localization/Localization.Broadcast.vb"
  - "src/Localization/Localization.LiveBroadcast.vb"
  - "src/Localization/Localization.Streams.vb"
  - "tests/Lite.Tests/Live*.vb"
  - "tests/Lite.Tests/StreamCatalog*.vb"
  - "tests/Lite.Tests/AdtsFrameCounterTests.vb"
---

# Live broadcasts, network streams and the stream catalogue

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

Four separate things share these folders; do not merge them. Local specifications: 023 (consumer), 199 / 201 / 263 (producer), 271 (end of a broadcast), 022 (catalogue), 196 (StreamsPlayer launcher). Contracts by id: `LIVE-BROADCAST`, `DEVICE-EXCHANGE`, `STREAM-BANK`, `USER-PLAYLIST` - amend the catalog first, never a local copy. The Share Manager side is in [share-companion.md](share-companion.md); the viewer's video player in [viewer-mainform.md](viewer-mainform.md).

**Components**
- **Consumer** (viewer): plays a phone's or relay's live audio. Entry points are the hand-off link (`LiveBroadcastLinkReader`), the "Open live broadcast" dialog ([LiveBroadcast_Form.vb](../../src/LiveBroadcast_Form.vb)), the exchange cast poll and the choose-file button's right-click menu. All of it is modern-only (whole-file `#If Not NETFRAMEWORK`).
- **Producer** (`src/LiveBroadcast/Producer/`): runs **only in the Share Manager**. The viewer exe compiles these files through its glob and never calls them - do not add a call from `Main_Form`.
- **StreamsPlayer launcher**: `::fms-broadcast::<path>` forwarded to the Share Manager by `Main_Form.ShareLauncher.vb`; its strings are `Localization.Broadcast.vb`. Unrelated to the live receiver.
- **Stream catalogue** (`src/Streams/`): builds and validates `stream-catalog.zip` from M3U/M3U8 (`STREAM-BANK`). Unrelated at run time to the live receiver; the only link is that the M3U reader refuses a `/b/` broadcast-token URL.
- **One owner per decision - change it there, never beside it**: endpoint choice `LiveEndpointSelector` (candidates in the producer's order, tried **sequentially**, restarting from the top after each lost connection); HTTP status meaning `LiveHttpPlaybackSession.ClassifyStatus`; retry and budget `LiveReconnectPolicy`; the loop `LiveBroadcastRunner`; drift `LiveDriftGuard`; LibVLC options `LivePlaybackOptions`; descriptor parsing `LiveBroadcastReader` (behind `LiveBroadcastIngress.TryRead`); recent entries `LiveBroadcastStore`; read-status wording `LiveBroadcastMessages`; stream identity `StreamIdentity.GetIdentityKey`; catalogue rules `StreamCatalogValidator` and `StreamCatalogProducer`.
- **Project lists**: the modern vbproj globs everything. The x86 vbproj lists the consumer and the `Streams\` files **by hand**; `Lite.Tests` lists the consumer files by hand and globs `Producer\*.vb` and `Streams\*.vb`; the Share Manager links `LiveBroadcastFeatures.vb` plus `Producer\*.vb`. A new consumer file goes into the hand-kept lists in the same commit. `StreamCatalogCli` is the one `Streams\` file with a net48 branch (there it only refuses, exit 1).

**Gate and entry**
- `LiveBroadcastFeatures.IsEnabled()` = the constant `Offered` (True) OR the developer switch `FMS_LIVE_BROADCAST=1`. `LiveBroadcastGateGuardTests` requires the evidence file `live-broadcast-gate-evidence.md` (among the local specifications) while `Offered` is True: flipping `Offered` is a release decision, not an edit.
- The cast poll runs only when `ExchangeFeatures.IsEnabled()` and an exchange account is configured. A cast is auto-accepted only with the preference `AutoAcceptLiveCast` (default off).
- An address typed in the dialog is played as live **only if the user ticks "this is live"**; a hand-off or recent entry is always live. A directory row the reader refused shows its reason and is never played.
- The catalogue command line (`--build-stream-catalog`, `--check-stream-catalog`, also with a single `-` or `/`) is intercepted in `Application_Events.vb` **before** the single-instance mutex. Exit codes: 0 pass, 1 failure, 2 usage.

**Behaviour of the receiver**
- **One HTTP request and one TCP connection per listener**: no redirects, no proxy, no cookies. The connection is closed **before** the player is stopped, and the player is stopped before the next connection (`LiveBroadcastRunner.CloseAsync`).
- **Status meaning** (only in `ClassifyStatus`): 404 and 410 are the fatal "not found or gone"; 503 is the producer's listener cap; 401/403 is access denied; 408, 429 and 5xx are retryable; anything else is refused. The relay answers 410 for an ended id (specification 271).
- **There is no in-band end signal.** After a body has played, any body end is a lost connection and goes to the reconnect policy; do not invent an "ended" marker.
- **Reconnect** (`LiveReconnectPolicy`): backoff 0/500/1000/2000/4000 ms, `MaxRetries` 5, a 12 s wall-clock budget, 10 s of stable playing refills the retries. A drift rejoin spends no retry; a sealed `NeedReconnect` is retried once.
- **Drift guard** (`LiveDriftGuard`): threshold = target + `MarginMs` 500 kept inside 1300..1500 ms, 5 s warm-up, 2 s sustain, growth of at least 300 ms, 15 s cooldown. Fed by `AdtsFrameCounter` and the `ProgressReadStream` figures.
- **LibVLC options** come only from `LivePlaybackOptions.ToMediaOptions`: `:demux=ts` or `:demux=aac`, `:network-caching`, `:live-caching` (target latency default 1000 ms, clamped 250..2000). Never add seek, resume or repeat options. `clock-jitter` / `clock-synchro` are absent on purpose (measured, see the comments there).
- **Threading**: the runner is awaited, the state arrives through `ILiveRunnerHost`, UI work is marshalled in `Main_Form.LiveBroadcast.vb`. `StopLivePlayerAsync` is bounded at 3 s so a stuck stop cannot hold the window; a generation number drops a late answer of a replaced run. Dispose `LiveVlcMedia` only after the session is closed and the player stopped.

**Secrets**
- A descriptor's `auth` carries only the scheme `password-v1` and a user name. The password lives in the session (`LiveBroadcastDescriptor.Password`), is cleared on exit and after an access denial, and is **never** stored. `LiveBroadcastStore` (`live-broadcasts.json`, `MaxEntries` 20) persists no password, no seal key, no sealed channel.
- A seal key or pin is never logged, shown or put in a `ToString`; `current.log` never gets an address. A sealed endpoint (`RequiresAgentSeal`) takes its key from the local agent pipe (`ExchangeAgentPipe.RequestGetBroadcastSeal`); the mechanism is `src/Exchange/ExchangeSealedBroadcast*.vb` ([exchange-client.md](exchange-client.md)), not here.

**Producer rules (Share Manager)**
- User-started only; one broadcast at a time; no publishable LAN address means no start; the port (`DefaultAudioPort` 8768, or the user's number) has no fallback; nothing is recorded.
- One `LiveCredentialPairs` set serves both the Digest login (realm `fms-broadcast`) and the relay verifiers (PBKDF2, 600000 rounds, made lazily once per pair). `LiveProducerDescriptorWriter.BuildHandOutJson` is the **single** hand-off writer - never build descriptor JSON anywhere else.

**Stream catalogue**
- `StreamCatalogSchema.ProducerColumns` is the 19 columns in fixed producer order; `ContractTag` is the contract version the code was written against - bump it only together with the contract.
- Identity (`GetIdentityKey`): one row per identity; do not add a second URL comparison elsewhere.
- The M3U reader refuses whole files over `MaxPlaylistBytes` (32 MiB) and rows that are not launchable (`USER-PLAYLIST` section 8). The churn guard refuses a replacement that loses more than `ChurnThresholdPercent` (20) of the previous identities once the previous catalogue has `ChurnThresholdMinRows` (50) rows, unless `--force` or the caller's callback agrees.

**Tests**: the gate `LiveBroadcastGateGuardTests`; loop `LiveBroadcastRunnerTests`; policy `LiveReconnectPolicyTests`; session and statuses `LiveHttpPlaybackSessionTests`; drift `LiveDriftGuardTests`; options `LivePlaybackOptionsTests`; producer `LiveProducer*Tests`; catalogue `StreamCatalog*Tests`. `LiveBroadcastLatencySpike` is a manual measuring rig, not a regression test.
