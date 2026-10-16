# Pointer - `LIVE-BROADCAST`

| | |
| --- | --- |
| **Id** | `LIVE-BROADCAST` |
| **Version** | **1.0, active; read 2026-10-16** (specification 235: file 11 and the amendment items A to Q of files 13 to 16; file 12 is superseded by item B and is a record). 0.14 added the PC producer, the HTTP MPEG-TS video shape, the `RELAY` and `TUNNEL` endpoints, receivers and cast; 0.15-0.18 folded the proposals of 2026-10-07 (`sourceId` names a source, the PC producer's numbers, the first-picture budget, `TUNNEL.inner` never omitted); items M to Q (protection, sources, watch, seal) arrived with the later amendment files 14 to 16. Owner: FastMediaSorter Android |
| **Home** | `live-broadcast/README.md`, `11_live_broadcast_contract.md` and the amendment files `13` to `16` in the shared contracts catalog |
| **Role here** | **consumer** (the viewer, specification 023, `src/LiveBroadcast/`), **PC producer** (the Share Manager: capture, encode, LAN serving, exchange publication, relay upload; specification 199, `src/LiveBroadcast/Producer/` and `src/FastMediaSorterCompanion/Core/`) and **receiver** of a broadcast listed on the exchange and as a cast target (specification 201). The gate is open (`LiveBroadcastFeatures.Offered = True`). **Implements `partial 1.0`** (registry row, verified 2026-10-16): the section 6 handshake has not been run by this product, the protected-broadcast completion (relay `verifiers`) and the sealed receiver are not built, and the differences are dated exceptions in the registry |

**Reader-facing description:** the portal page [Live broadcast](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/live-broadcast.html) of the shared "Media exchange between your devices" portal (not published until the Android site publishes it). Nothing there binds this contract.

This file replaces a **copy** of the contract that was taken on 2026-09-13 and had already fallen behind:
it was missing the no-loopback producer rule, the link `package=` hint and the watch measurements of
2026-09-17. The copy was deleted rather than refreshed, which is the only thing that stops it drifting
again.

The contract ships no conformance vector file, so there is nothing to replay; the shape of the descriptor is
held by `LiveBroadcastContractSyncTests` and `LiveProducerDescriptorWriterTests`.

## What this repository must do as a consumer (023)

- Start at the live edge with a small buffer, and **never let the delay grow**: after a stall or a
  reconnect, rejoin at the live edge and drop what was missed.
- Put the resilience in bounded reconnects, not in a deep buffer. On a LAN the producer adds milliseconds;
  anything above half a second end to end was added here. The buffer comes from `targetLatencyMs` (floor and
  ceiling under 2 s, item L).
- Open exactly one connection per listener - a probe connection costs a listener slot, and the watch has
  four.
- Do not compensate for a quiet producer with gain of your own; that hides a defect that has a ticket.
- Read `isLive` from the descriptor rather than probing, and refuse a `schemaVersion` above the one this
  build supports with "update the app", never "invalid".
- Read `sourceId` as one opaque string (`<deviceSourceId>:<source>` is a single channel of its own), accept any
  channel count and sample rate of an `AUDIO_ONLY` body, skip an endpoint transport this build does not play,
  and never dial the host of a `TUNNEL.inner` - a consumer replaces that host and port with its own
  forwarder's (`DEVICE-EXCHANGE` 7.7; the documentation host `192.0.2.1` marks "no LAN address").
- A protected broadcast (item M): read `auth`, answer Digest on the LAN and `TUNNEL` endpoints and Basic on
  `RELAY`, take the directory's `credentials` for an own device, ask for a password once and never put it in a
  URL.
- Refuse a video mode by name until it is played; when video is played, the first picture is due within the
  keyframe interval plus 1 s and the report records time to first picture beside time to first byte (item I).
- A sealed endpoint (item Q) is skipped until the viewer's receiver plays it: the parsing is built, the live
  session does not call it yet (specification 234, E2b).

## What the producer does (199)

The Share Manager captures the microphone, the system sound (with the process-loopback exclusion), the screen
(Windows Graphics Capture) and the camera (Media Foundation), serves `AUDIO_ONLY` in the 2.1 shape and the
MPEG-TS shape (item C) on the LAN with HTTP Digest, and publishes the broadcast to the user's exchange account
with `RELAY` and `TUNNEL` endpoints, one broadcast at a time with a stable `sourceId` (items G and N). **What it
does not yet do is in the registry, not here**, and the two that matter most are named so nobody relies on them:
a broadcast with a password is not offered on the relay (no `verifiers`, 239 CR-14), and a descriptor handed
outside the account may carry `RELAY` and `TUNNEL` with no password (239 CR-15).

- **The registry row** is role P/C, `Implements` `partial 1.0`; the section 6 handshake, the first-sound
  figure of the consumer (1.0-1.1 s against "within 1 s") and the joint run are in a dated exception tied to
  205.
- **One proposal is drafted and not filed** (the catalog is sealed): what the required top-level `url` carries
  when a descriptor has no LAN endpoint, and which of two timestamp-less descriptors of one `sourceId` wins - kept
  in this repository's `docs/specifications/183_contract_proposals/`, for the owner to place.
