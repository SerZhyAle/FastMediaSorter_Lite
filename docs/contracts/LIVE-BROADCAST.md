# Pointer - `LIVE-BROADCAST`

| | |
| --- | --- |
| **Id** | `LIVE-BROADCAST` |
| **Version** | 1.2, active (since 2026-10-07; 1.1 of 2026-10-17, 1.2 of 2026-10-19; read on 2026-10-20). Owner: FastMediaSorter Android |
| **Home** | `live-broadcast/README.md` in the shared contracts catalog, with `11_live_broadcast_contract.md` and the amendment files `13` to `16` that bind beside it |
| **Role here** | **consumer** in the modern viewer (`src/LiveBroadcast/`, `src/LiveBroadcast_Form.vb`, `src/Main_Form.LiveBroadcast.vb`; one stream at a time), **producer** in the Share Manager (`src/LiveBroadcast/Producer/`, `src/FastMediaSorterCompanion/Core/`), and the relay, directory and cast carriage in `exchange-server/` under `DEVICE-EXCHANGE`. Modern only: the net48 fallback has no live surface |

## What this repository must do to stay conformant

- **Consumer (README section 1).** Start at the live edge with a small buffer and never let the delay grow: after a
  stall or a reconnect rejoin at the live edge. Resilience is bounded reconnects, not a deep buffer; the buffer
  comes from the endpoint's `targetLatencyMs`. One connection per listener, no gain of its own for a quiet
  producer, `isLive` read from the descriptor, no probing.
- **Reading a descriptor.** By name, unknown members skipped, `sourceId` one opaque string, an endpoint transport
  it does not play skipped, and the host of a `TUNNEL.inner` never dialled. A higher schema is refused cleanly and
  never partly used, with the `format-unsupported` row of `DEVICE-EXCHANGE`, naming `LIVE-BROADCAST`: no "update
  the app" demand and no minimum program version (1.1).
- **Protected and sealed (items M and Q).** `auth` is answered with Digest at the producer and Basic inside TLS at
  the relay, and a password is never put in a URL. A sealed endpoint is never played as clear bytes: an unknown
  `seal.v` is skipped, a sealed `RELAY` goes through the consumer's own loopback proxy and a sealed `TUNNEL`
  through a TLS client that accepts only the pinned leaf.
- **Producer (README section 2).** Never publish a loopback address (no reachable LAN address means no broadcast
  and no descriptor); never offer `icy-metaint`; one descriptor for the link, the file and the barcode. A
  descriptor handed outside the account that carries a `RELAY` or `TUNNEL` endpoint is protected (item M). The keys
  of a sealed endpoint go only into a descriptor the person hands; a directory record carries `seal: {v}` and no
  key, and a key is never logged.
- **Several streams (1.2).** A receiver that offers several simultaneous streams keeps decode, cancellation and
  failure state per `broadcastId`, keeps an ended slot with an explicit reconnect or replace action, and never
  silently substitutes another broadcaster. A receiver that offers one stream stays conformant.
- **Evidence.** The handshake of section 6 is the conformance evidence of a kind, run against a live broadcast and
  recorded as a dated registry note; the contract ships no vector file.

## Where it stands

The registry row of this product and its dated exceptions say what is proven and what is open; this pointer never
repeats them. Specification 244 holds the clause-by-clause audit.
