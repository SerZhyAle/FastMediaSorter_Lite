# Pointer - `LIVE-BROADCAST`

| | |
| --- | --- |
| **Id** | `LIVE-BROADCAST` |
| **Version** | 0.18, draft (read in full 2026-10-07). 0.14 added the PC producer, the HTTP MPEG-TS video shape, the `RELAY` and `TUNNEL` endpoints, receivers and cast, all `[PROPOSED]`; 0.15-0.18 folded the proposals of the same day (`sourceId` names a source, the PC producer's numbers, the first-picture budget, `TUNNEL.inner` never omitted). The RTSP stream kinds are `[CONTRACT]` since 0.13. Owner: FastMediaSorter Android |
| **Home** | `live-broadcast/README.md`, `11_live_broadcast_contract.md` and `13_contract_amendment_2026-10-07.md` in the shared contracts catalog |
| **Role here** | **consumer** - local specification 023, built in the working tree on 2026-10-07 (audio over HTTP), real-device acceptance not run; **planned producer** (local specifications 172 and 173) and **planned receiver** over every transport and as a cast target (local specification 201), under the umbrella 160 |

**Reader-facing description:** the portal page [Live broadcast](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/live-broadcast.html) of the shared "Media exchange between your devices" portal (not published until the Android site publishes it). Nothing there binds this contract.

This file replaces a **copy** of the contract that was taken on 2026-09-13 and had already fallen behind:
it was missing the no-loopback producer rule, the link `package=` hint and the watch measurements of
2026-09-17. The copy was deleted rather than refreshed, which is the only thing that stops it drifting
again.

## What this repository must do as a consumer (023)

- Start at the live edge with a small buffer, and **never let the delay grow**: after a stall or a
  reconnect, rejoin at the live edge and drop what was missed.
- Put the resilience in bounded reconnects, not in a deep buffer. On a LAN the producer adds milliseconds;
  anything above half a second end to end was added here.
- Open exactly one connection per listener - a probe connection costs a listener slot, and the watch has
  four.
- Do not compensate for a quiet producer with gain of your own; that hides a defect that has a ticket.
- Read `isLive` from the descriptor rather than probing, and refuse a `schemaVersion` above the one this
  build supports with "update the app", never "invalid".
- Read `sourceId` as one opaque string (`<deviceSourceId>:<source>` is a single channel of its own), accept any
  channel count and sample rate of an `AUDIO_ONLY` body, skip an endpoint transport this build does not play
  (`RELAY`, `TUNNEL`), and never dial the host of a `TUNNEL.inner` - a consumer replaces that host and port with
  its own forwarder's (`DEVICE-EXCHANGE` 7.7; the documentation host `192.0.2.1` marks "no LAN address").
- Refuse a video mode by name until it is played (`VIDEO_AUDIO` and `VIDEO_ONLY` are refused as unsupported, as
  2.5 and 3.1 ask); when video is played, the first picture is due within the keyframe interval plus 1 s and the
  report records time to first picture beside time to first byte (0.17 item I).
- The `[PROPOSED]` sections bind nobody until they flip: the HTTP MPEG-TS shape, the `RELAY` and `TUNNEL` endpoints
  and cast are built behind a gate and are not claimed as conformance until the owner flips them.

## What is planned (nothing built yet)

- **Producer (172, 173).** The Share Manager captures the microphone, the system audio and the screen, serves
  `AUDIO_ONLY` in the 2.1 shape and the MPEG-TS shape on the LAN, and publishes the broadcast to the user's
  exchange account with `RELAY` and `TUNNEL` endpoints. Every ask of the proposal this repository filed was
  answered in the contract: `sourceId` per source with several broadcasts (item G), the MPEG-TS numbers, the
  token path segment and the channel count and sample rate (item H), `TUNNEL.inner` on `192.0.2.1` when there is no
  LAN address (item J). Specifications 172 and 173 still carry the older plan (one `sourceId` per device, stereo
  as a deviation, `inner` open) and are re-read against these items when they start.
- **Receiver (174).** Plays any endpoint the descriptor lists in the producer's order - LAN, then `RELAY`, then
  `TUNNEL` - audio and video, with certificate checking never switched off, and accepts a cast. It widens the
  endpoint model and the store together, because both drop the members they do not model today (`inner`,
  `certFingerprint`, `channels`).
- **One proposal is drafted and not filed** (the catalog is sealed): what the required top-level `url` carries when a
  descriptor has no LAN endpoint, and which of two timestamp-less descriptors of one `sourceId` wins - kept in this
  repository's `docs/specifications/183_contract_proposals/`, for the owner to place. 173 (the producer) is
  the ticket that has to choose a value until the owner answers; item J and producer rule 1 already exclude the
  documentation host and loopback.
- **The registry row** is role P/C: `Implements` `partial 0.18` for the consumer's audio path, producer and receiver
  not built; the open points (the first sound measured at 1.0-1.1 s against "within 1 s", the section 6
  handshake not run on a real phone or watch) are in its notes and in a dated exception.
