# Pointer - `DEVICE-EXCHANGE`

| | |
| --- | --- |
| **Id** | `DEVICE-EXCHANGE` |
| **Version** | 0.19, draft, `[PROPOSED]` in full (created 2026-10-07 by FastMediaSorter Android, ticket S4116; 0.11-0.15 folded the proposals filed beside it, 0.16 added the rung 1 artifact `records-v1`, 0.17 is marked `breaking` for a draft because section 15 holds the owner's answers of the same day: the frame limit, the chunk bound, every outsider `connect` over TLS, the Drive client types, `access` on Drive by opt-in). Read in full at 0.17 on 2026-10-07; nothing is deployed anywhere |
| **Home** | `device-exchange/README.md` in the shared contracts catalog |
| **Role here** | **exchange-server implementer** (sections 5 and 7; local specifications 161-168) **and** device client, resource producer, resource consumer, broadcaster and receiver (local specifications 180, 170, 171, 172-174); the Drive channel waits on an experiment (175, 176). **State (2026-10-07): the Go server core of 161-164 is built to 0.17 in the development tree and exercised by no client; the device client, the producer, the consumer, the broadcaster and the receiver are not built** |

**Reader-facing description:** the portal page [Exchange server](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-server.html) of the shared "Media exchange between your devices" portal (not published until the Android site publishes it). Nothing there binds this contract.

The contract replaces the exchange design of `ANYWHERE-ACCESS` sections 5-7 and the relay of the `LIVE-BROADCAST`
file 12: an exchange server (or a Google Drive folder) is where one user's devices list, request and register all
of their resources and broadcasts, and any other device of the user is a receiver. The server is a Go program for
Windows shipped in this product's package; the language is not normative.

## What this repository holds while the contract is `[PROPOSED]`

1. **Local specification 198** is the umbrella (nine roles, the process model, the order of work with the server
   first, the ladder); 161-168 and 170-180 are its children, 181 the synchronization that wrote this page.
2. **No `schemaVersion` 1 is kept.** The Go module `exchange-server/` was first built to the superseded
   `ANYWHERE-ACCESS` 0.10 wire (register by one deployment password, a flat 16 KiB frame, a plain `connect`
   accepted). Tickets 161-164 replaced that core on 2026-10-07 and read 0.17: `schemaVersion` 2 with one typed
   envelope per section 4 type, accounts and enrolment (no deployment password), the directory and publish records,
   tunnels and the third-party port, the bounds of 15 item T (`MaxFrame` 16384 until `welcome` or `enrolled`,
   `MaxFrameAuth` 262144 after, a record at most 16 KiB, no paging) and the TLS-only outsider `connect` of item V;
   a plain stream is answered only `tls-required` or `version-unsupported`. The module's own suite is green (race
   included). **Update 2026-10-11:** the local probe exercises the full flow against a fresh built server,
   and the VB client library is built; a phone joint run is not claimed. The registry remains `partial 0.17`.
3. **Rung 1 is not frozen.** Ticket 205 now vendors the generated frames, records, connection strings and
   provenance; the VB frame and record replay runs in both modern test assemblies. The 16 KiB input
   boundary grows beyond the reader's record limit after server-owned fields are added and is skipped;
   `PROPOSAL-2026-10-11-fmsw-vectors.md` offers the files and asks the owner to resolve this boundary.
   Another product's agreed record round trip and the joint rungs remain pending.
4. **Dual mode (section 9 rule 7).** With no account configured and no Google sign-in nothing in this product
   changes: no connection, no listener, no process, no file, no row, no changed export byte. Local specification
   177 captures a baseline of today before any client code lands and proves it afterwards.
5. **Gated twice and off by default.** The server ships behind the `INCLUDE_EXCHANGE` gate (off) and an unchecked
   installer component and starts only on an explicit opt-in; the clients sit behind a compile-time gate that
   stays shut until the owner opens it. Nothing ships before the server wire's rung 2.
6. **One proposal of this repository is open on this contract:** `PROPOSAL-2026-10-10-exchange-connection-string.md`
   (filed 2026-10-10 from the owner's input - the secret-free `FMSEXCH1` connection string, a consumer that needs no
   local agent, account entries with a `deviceId` per entry, a producer that joins at most one server and leaves
   cleanly; no wire change). Local specification 187 is built to its text behind the client gate (2026-10-11: the
   codec in Go and VB, the generated `connection-string-v1.json`, the operator's string, the viewer's own entries and
   session, the add dialog, the share service's join and clean leave). Two proposals of other products of the same day
   refine its grammar and canonical bytes - StreamsPlayer's (`...connection-string-grammar-and-handling.md`) and
   FastMediaSorter Android's (`...connection-string-entries-and-leave.md`, the contract owner) - and the codec follows
   the owner's serializer (the WHATWG form encoder) and the reader tolerance both ask for; a change at the fold is a
   ticket, not a deviation. The one filed on 2026-10-07 (the server's first read, fifteen asks) was folded: asks 2-15
   into 0.15, the frame size and the chunk bound answered in 0.17 items T and U.
7. **The registry row** (`DEVICE-EXCHANGE`, role P/C, `Implements` `partial 0.17` since 2026-10-11) moves only with the evidence of a rung, and
   each rung's dated note names the two implementations that agreed (local specification 205).
8. **Modern only.** Everything here is the .NET 10 x64 mainline plus the Go server; the net48 fallback gets none
   of it.

## Access and connection experience (specification 218, 2026-10-12)

Nothing new is binding. Specification 218 records what the implementation does, what the first live self-test
showed (twelve of twelve steps, after five defects fixed in the tree) and what must be checked before the access path
is called working. The part for the other products is filed in the catalog beside the contract as
`PROPOSAL-2026-10-12-access-and-connection-experience.md` (status `proposed`): a named joint conformance run for rung 2,
one user-visible outcome per failure class in section 9, no empty or unread credential on the wire, and a kept pending
state for a removal the server did not hear. The contract stays at 0.19 until its owner folds or rejects that proposal.

## Resource PIN amendment read at 0.19 (specification 216)

Section 17 AG-AL preserves the original resource PIN through public lock metadata and
consumer capability gating. The exchange server filters both list and changed records;
Windows performs the SSH root proof. Unsupported and malformed schemes never authorize
attachment or a stored-resource open. Protected FMSCFG/QR exports are refused until their
carrier can express the serving protocol without carrying the original PIN. The generated
`protection-v1.json` corpus is replayed independently by the VB resource reader.
