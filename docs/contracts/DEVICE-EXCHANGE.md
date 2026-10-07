# Pointer - `DEVICE-EXCHANGE`

| | |
| --- | --- |
| **Id** | `DEVICE-EXCHANGE` |
| **Version** | 0.17, draft, `[PROPOSED]` in full (created 2026-10-07 by FastMediaSorter Android, ticket S4116; 0.11-0.15 folded the proposals filed beside it, 0.16 added the rung 1 artifact `records-v1`, 0.17 is marked `breaking` for a draft because section 15 holds the owner's answers of the same day: the frame limit, the chunk bound, every outsider `connect` over TLS, the Drive client types, `access` on Drive by opt-in). Read in full at 0.17 on 2026-10-07; nothing is deployed anywhere |
| **Home** | `device-exchange/README.md` in the shared contracts catalog |
| **Role here** | **exchange-server implementer** (sections 5 and 7; local specifications 161-168) **and** device client, resource producer, resource consumer, broadcaster and receiver (local specifications 180, 170, 171, 172-174); the Drive channel waits on an experiment (175, 176). **Planned: nothing is built to this version** |

The contract replaces the exchange design of `ANYWHERE-ACCESS` sections 5-7 and the relay of the `LIVE-BROADCAST`
file 12: an exchange server (or a Google Drive folder) is where one user's devices list, request and register all
of their resources and broadcasts, and any other device of the user is a receiver. The server is a Go program for
Windows shipped in this product's package; the language is not normative.

## What this repository holds while the contract is `[PROPOSED]`

1. **Local specification 160** is the umbrella (nine roles, the process model, the order of work with the server
   first, the ladder); 161-168 and 170-180 are its children, 181 the synchronization that wrote this page.
2. **No `schemaVersion` 1 is kept.** The Go module `exchange-server/` was built to the superseded `ANYWHERE-ACCESS`
   0.10 wire (register by one deployment password, a flat 16 KiB frame, a plain `connect` accepted); it is
   migrated, not kept, by 161-167, and the registry carries one dated exception per owning specification. The
   bounds of 15 item T (16384 bytes until `welcome` or `enrolled`, 262144 after, a record at most 16 KiB, no
   paging) and the TLS-only outsider `connect` of item V are what 161 and 164 are written to.
3. **Rung 1 is not run.** `records-v1.json` needs a record codec and this product has none yet; the Go generator
   and the VB replay are 178 (with the device client library of 180). Until then the registry says `-`, not a
   conformance.
4. **Dual mode (section 9 rule 7).** With no account configured and no Google sign-in nothing in this product
   changes: no connection, no listener, no process, no file, no row, no changed export byte. Local specification
   177 captures a baseline of today before any client code lands and proves it afterwards.
5. **Gated twice and off by default.** The server ships behind the `INCLUDE_EXCHANGE` gate (off) and an unchecked
   installer component and starts only on an explicit opt-in; the clients sit behind a compile-time gate that
   stays shut until the owner opens it. Nothing ships before the server wire's rung 2.
6. **No proposal of this repository is open on this contract.** The one it filed on 2026-10-07 (the server's first
   read, fifteen asks) was folded: asks 2-15 into 0.15, the frame size and the chunk bound answered in 0.17
   items T and U.
7. **The registry row** (`DEVICE-EXCHANGE`, role P/C, `Implements` `-`) moves only with the evidence of a rung, and
   each rung's dated note names the two implementations that agreed (local specification 178).
8. **Modern only.** Everything here is the .NET 10 x64 mainline plus the Go server; the net48 fallback gets none
   of it.
