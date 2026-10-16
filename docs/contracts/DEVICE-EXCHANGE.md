# Pointer - `DEVICE-EXCHANGE`

| | |
| --- | --- |
| **Id** | `DEVICE-EXCHANGE` |
| **Version** | **1.1, active; read 2026-10-16** (specification 235). 1.0 (2026-10-15, S4245) left draft and folded the chapters of 0.29 into sections 1-13, 17, 20, 22, 24 and 27; 1.1 (2026-10-15, S4276) rechecks the shared invitation setting at the commit of `register`. An item letter never moves; a section number from 14 to 27 in older text is an address of the old layout, and the catalog's `HISTORY.md` maps it to its place now. Owner: FastMediaSorter Android |
| **Home** | `device-exchange/README.md` in the shared contracts catalog |
| **Role here** | **exchange-server implementer** (the Go program `exchange-server/`), **device client** and **resource consumer / receiver** in the viewer (`src/Exchange/`, `src/Resources/`), **device client, resource producer, broadcaster and clipboard device** in the Share Manager (`src/FastMediaSorterCompanion/`). All built in the working tree; the client gate is open (`ExchangeFeatures.Offered = True`, specification 223, 2026-10-14) and the server package stays behind its own gate (`INCLUDE_EXCHANGE`, an unchecked installer component) by the owner's policy. **Implements `partial 1.1`** (registry row, verified 2026-10-16): no joint rung has run, the Drive channel is not built, and the differences listed in the registry's exception rows are open |

**Reader-facing description:** the portal page [Exchange server](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-server.html) of the shared "Media exchange between your devices" portal (not published until the Android site publishes it). Nothing there binds this contract.

The contract replaces the exchange design of `ANYWHERE-ACCESS` sections 5-7 and the relay of `LIVE-BROADCAST`
file 12: an exchange server (or a Google Drive folder) is where one user's devices list, request and register all
of their resources and broadcasts, and any other device of the user is a receiver. The server is a Go program for
Windows shipped in this product's package; the language is not normative.

## What this repository holds

1. **One state record**: the version table of
   specification 235
   says which version the code of each ticket was written against and which was read at release; the
   registry row and its exception rows say what is built and what is not. This pointer states neither a
   build state nor a ticket status beyond that, so it cannot go stale with them.
2. **Open differences have an owner.** Every difference found against 1.1 is a dated exception in the registry
   with its ticket: the Drive channel and its premise experiment (203),
   the joint rungs and the dual-mode baseline (205,
   deferred by the owner), the sealed receiver in the viewer and the honest text of item DE
   (234), the clipboard through
   Drive (230) and every
   other item in 239.
3. **One `schemaVersion`, 2.** No `schemaVersion` 1 is kept; a plain stream is answered only `tls-required` or
   `version-unsupported`.
4. **Rung 1 is this product's own generator against its own replay.** The generated frames, records,
   connection strings, protection, account-lifecycle, clipboard and sealed vectors are vendored under
   `tests/Lite.Tests/TestData/` with provenance and replayed in both modern test assemblies; that is not a
   second implementation's agreement, so no rung is claimed. The vector files filed beside the contract as
   proposals by this repository are `PROPOSAL-2026-10-11-fmsw-vectors.md`,
   `PROPOSAL-2026-10-15-fmsw-account-lifecycle-vectors.md`, `PROPOSAL-2026-10-15-fmsw-clipboard-vectors.md` and
   `PROPOSAL-2026-10-16-fmsw-sealed-vectors.md`; each file's own Status line says what the owner did with it.
   The vendored `protection-v1.json` is the regenerated 1.1 file, not the catalog's 0.19 copy (239 CR-12).
5. **Dual mode (section 9 rule 7).** With no account configured and no Google sign-in nothing in this product
   changes: no connection, no listener, no process, no file, no row, no changed export byte. The baseline of
   specification 205 holds this; it is red today on six registry names the viewer added (205's status line).
6. **Modern only.** Everything here is the .NET 10 x64 mainline plus the Go server; the net48 fallback gets none
   of it.
7. **The vocabulary** of items CV to DA (EN, RU, UK) is vendored as `tests/Lite.Tests/TestData/exchange-terms/`
   and held by `ExchangeVocabularyTests`; the names of the product in them are governed by
   [PRODUCT_NAMING.md](../guides/PRODUCT_NAMING.md) (`FMS_Win` is the label, `FMS_W` is retired).

## Amendments read here

- **Section 17 (resource PIN, specification 216):** AG to AL preserve the original resource PIN through public
  lock metadata and consumer capability gating. The exchange server filters both list and changed records;
  Windows performs the SSH root proof. Unsupported and malformed schemes never authorize attachment or a
  stored-resource open. Protected FMSCFG/QR exports are refused until their carrier can express the serving
  protocol without carrying the original PIN. The enforcement at the serving boundary (AH) is the worker's, in
  the sibling repository.
- **Section 20 (guests, links, protected broadcasts):** the server side of BD is built; the sender of a grant,
  the public link with a password (BE), the relay `verifiers` (BF) and the worker's per-grant login are **not**
  (239 CR-13, CR-14). Read the registry before relying on any outward-access statement elsewhere.
- **Section 22 (clipboard):** the server slot is built (specification 229) and the Share Manager is the client
  (230); the order of kinds and the line endings follow item CB, which differs from `CLIPBOARD-GUARD` item O
  (239 CR-18).
- **Section 27 (sealed content):** keys, sealed access, boxes, the sealed relay upload and the sealed clipboard
  are built; the receiver in the viewer's live session is not (234, E2b).
