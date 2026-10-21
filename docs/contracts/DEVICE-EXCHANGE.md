# Pointer - `DEVICE-EXCHANGE`

| | |
| --- | --- |
| **Id** | `DEVICE-EXCHANGE` |
| **Version** | 1.5, active (since 2026-10-07; 1.5 of 2026-10-19; read on 2026-10-20 with `WEB-BINDING`, `REMOTE-ADMIN` and the 1.5 amendment). Owner: FastMediaSorter Android |
| **Home** | `device-exchange/README.md` in the shared contracts catalog, with `WEB-BINDING.md`, `REMOTE-ADMIN.md` and the dated 1.5 amendment beside it |
| **Role here** | **exchange-server implementer** (the Go program `exchange-server/`), **device client, resource producer, broadcaster and clipboard device** in the Share Manager (`src/FastMediaSorterCompanion/`), **device client and receiver** in the modern viewer (`src/Exchange/`, `src/Resources/`). Modern only: the net48 fallback has none of it |

## What this repository must do to stay conformant

- **Wire (section 7.1, item D).** `schemaVersion` 2 only; a plain stream is answered only `tls-required` or
  `version-unsupported`. Every deployment serves TLS and a client never falls back to plain. A client validates the
  leaf as item D says: the platform chain first, otherwise the entry's pin, a new leaf only after the person has
  accepted it.
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

The registry row of this product and its dated exceptions say which sections are proven, open or excepted; this
pointer never repeats them. Specification 244 holds the clause-by-clause audit and the version table.
