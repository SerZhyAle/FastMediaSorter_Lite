# Pointer - `DEVICE-EXCHANGE`

| | |
| --- | --- |
| **Id** | `DEVICE-EXCHANGE` |
| **Version** | 1.15, active (since 2026-10-07; read at 1.15 on 2026-11-15, L0323 catch-up, from 1.5 as read on 2026-10-20 with `WEB-BINDING`, `REMOTE-ADMIN` and the 1.5 amendment). Registry row `partial 1.14`: the items 1.5 to 1.14 less the clauses open and dated 2026-11-15 in the registry - 1.15 FQ-FV (the remote schedule: the server lists no `schedule` feature and the Share Manager answers no verb), 1.10 DY (the `ended` refusal reason of a tunnel `connect`), 1.11 EM (a stable `resourceId` per shared folder), 1.12 FB (`e2e-missing-peer`) and the 1.14 CW and CX terms in 13 languages; the vendored corpora and the WebSocket refusal artifacts wait on the producer's own exchange-vectors run; ticket L0328. Owner: FastMediaSorter Android |
| **Home** | `device-exchange/README.md` in the shared contracts catalog, with `WEB-BINDING.md`, `REMOTE-ADMIN.md` and the dated 1.5 amendment beside it |
| **Role here** | **exchange-server implementer** (the Go program `exchange-server/`), **device client, resource producer, broadcaster and clipboard device** in the Share Manager (`src/FastMediaSorterCompanion/`), **device client and receiver** in the modern viewer (`src/Exchange/`, `src/Resources/`). Modern only: the net48 fallback has none of it |

## What this repository must do to stay conformant

- **Wire (section 7.1, item D).** `schemaVersion` 2 only; a plain stream is answered only `tls-required` or
  `version-unsupported`. Every deployment serves TLS and a client never falls back to plain. A client validates the
  leaf as item D says: the platform chain first, otherwise the entry's pin, a new leaf only after the person has
  accepted it.
- **Settings standard (1.12, section 35, items EN to ER; local specification L0296).** The server lists `settings`
  only with its store open, keeps one standard per account and `appId` in `settings.json`, refuses with the four
  reasons of item EP and erases the standards with the account (item AU). The viewer sends `settings_save_standard`
  and `settings_get_standard` only on the person's act and only while `welcome` lists `settings`, writes no
  local-only key (item EQ) and applies nothing of a read before the whole payload is understood (item ER). The
  regenerated server transcript is not yet offered to the catalog (L0296 phase 1 step 7).
- **Notes sync, server half (1.13, items FG to FM; local specification L0321).** The server lists `notes` only with
  its store open and `sealed` listed, keeps the notes of every `member` account in `notes.json`, never opens a box,
  refuses a stale edit `note-conflict`, holds the count of item FL as `limits.notesPerAccount` in the config file
  (config only, no `policy` line), purges tombstones in its sweep and erases the notes with the account and on
  guest. The viewer of this product has no notes sync. Three points go to the catalog as the proposal `L0322`: the
  cursor of a paged `full` answer, the bound of a notes box against the delivered frame, and a notes-count member on
  the administration wire. The server runs on the specification's assumptions 1 to 15; the ones that read the
  contract's silence are 1 (cursor), 2 (frame bound), 3 (an edit of a purged note), 4 (a malformed member) and 9
  (the count switch). `go run ./cmd/exchange-vectors -notes` prints the transcript the catalog's proposed vectors
  are made from, produced at the release.
- **No account, no Drive, no change (section 9 rule 7).** With neither configured, nothing in the product changes.
- **Connection string (5.5, items BO, CK, CZ, DP; the 1.3 amendment).** A higher `FMSEXCH` prefix or descriptor schema is refused cleanly, never
  imported in part, with the catalog's canonical text, code `format-unsupported` (kind local, step `-`), and no
  "update the program" demand.
- **Protection and sealing (sections 17, 22, 27).** A `protection` member is the fact of a lock, never a key: an
  unknown scheme is listed as locked and never attached, the server hands `access` of a protected resource only to
  a device that declared `pin-v1`, and the PIN is proved at the producer's serving boundary, never on the
  exchange wire (items AG to AI). The clipboard follows section 22 and `CLIPBOARD-GUARD`; the server never opens a
  sealed box.
- **Server, browser binding (`WEB-BINDING`, normative resolutions 1, 2, 5, 6).** A server that offers the WebSocket
  opening, static client files or the operator surface owes the whole same-origin profile: `GET /v2/ws` with
  `fms-exchange-v2`, binary messages of exactly one frame each, https same-origin check (a listed origin is never
  operator permission), file lookup that rejects traversal, dot files, links out of the root and private keys, and
  serves a fixed `Content-Type` table.
- **Server, remote administration (`REMOTE-ADMIN`, `WEB-BINDING` operator-administration).** Off by default and
  turned on only at the server machine; `admin-auth` is accepted on both openings, paced per client address. The
  wire carries exactly the 18 operations of `administration-operations`; anything else, `grant.add`,
  `grant.remove` and `host.settings` included, is `bad-request`. The management operations of specification 246
  are local-only and are not contract operations. No reply returns a password, token, hash or resource `access`;
  the one-time codes and the new connection string appear only in their creation reply. The ten reply codes are
  shown in the contract's words, and the link is `https://<x>/#admin`.
- **Server, 1.5 amendment.** A bounded local operational audit of sign-ins, connections and mutations (secrets
  recorded only as "changed", untrusted fields escaped); a stalled or gone listener never stalls the other
  listeners or broadcasts; an administration token is never an account token or a recipient key.
- **Vectors.** What this product generates is filed beside the contract as a `PROPOSAL-<date>-<topic>.md`, never
  edited into it; only a run with a second implementation is a rung (section 11).

## Where it stands

Specification 234 records the 2026-10-23 local implementation and test evidence for section 27:
sealed broadcast playback, pinned inner tunnel TLS, the guest-broadcast sender and the verbatim
privacy statement. Section 27 was re-read in catalog edition 1.7, and the whole of 1.5 to 1.15 against the
working tree on 2026-11-15 (read, not run); no joint-device run is claimed. Folder guest access keeps specification
239's `OutwardAccessFeatures` gate. The portal hand-off is
[PORTAL_HONEST_TEXT_234.md](../guides/PORTAL_HONEST_TEXT_234.md).

The registry row of this product and its dated exceptions say which sections are proven, open or excepted; this
pointer never repeats them. Specification 244 holds the clause-by-clause audit and the version table.
