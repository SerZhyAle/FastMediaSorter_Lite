# Contract pointers

Every file in this folder is a **pointer**, never a copy. The contracts themselves live in the shared
catalog, whose location is named in exactly one file in this repository - [`CLAUDE.md`](../../CLAUDE.md),
the first line. Everything else, here and in the source, cites a contract by its **id** and a rule or
section number: `OCR-OVERLAY rule 8`, `FMSCFG rule 5`, `WORKER-IPC section 6`. Never a path, because
whoever clones this repository does not have the drive the catalog sits on.

A pointer that grows a second page has become a copy. If this repository ever disagrees with a contract,
that difference is an amendment to write in the catalog or a dated exception to record in its registry -
not a local edit and not a local copy kept "in sync".

| Pointer | Id | This product's role |
| --- | --- | --- |
| [`OCR-OVERLAY.md`](OCR-OVERLAY.md) | `OCR-OVERLAY` | consumer - the in-app OCR + translation overlay (1.3) |
| [`OCR-EXCHANGE.md`](OCR-EXCHANGE.md) | `OCR-EXCHANGE` | owner of the record |
| [`OCR-INVOCATION.md`](OCR-INVOCATION.md) | `OCR-INVOCATION` | consumer - the "translate in the browser" button (1.1) |
| [`OCR-PIPELINE.md`](OCR-PIPELINE.md) | `OCR-PIPELINE` | none as an implementer - the source every *inherited* OCR constant cites (ticket 033) |
| [`FMSCFG.md`](FMSCFG.md) | `FMSCFG` | producer - the Share Manager writes the `.fmscfg` and its QR (2.2) |
| [`CONTRACT_BROADCAST_RESOURCE.md`](CONTRACT_BROADCAST_RESOURCE.md) | `BROADCAST-RESOURCE` | producer - the Share Manager writes the `.spres`, QR and quick link (1.0) |
| [`WORKER-IPC.md`](WORKER-IPC.md) | `WORKER-IPC` | consumer - the Share Manager drives the worker (0.10, schema-1 client) |
| [`DISK-SHARE.md`](DISK-SHARE.md) | `DISK-SHARE` | consumer - file-backed Share Manager disk control; native/service release proof remains open |
| [`SHARE-SESSION.md`](SHARE-SESSION.md) | `SHARE-SESSION` | custodian of guarantee 3's state across installs, uninstalls and host-mode changes; no session implementation (1.1) |
| [`ANYWHERE-ACCESS.md`](ANYWHERE-ACCESS.md) | `ANYWHERE-ACCESS` | exchange-server implementer (section 5; local specification 133) - 0.10 draft [PROPOSED], nothing ships before its ladder moves; the server-role review is a catalog proposal |
| [`LIVE-BROADCAST.md`](LIVE-BROADCAST.md) | `LIVE-BROADCAST` | planned consumer (specification 023) |
| [`STREAM-BANK.md`](STREAM-BANK.md) | `STREAM-BANK` | planned producer (specification 022) |
| [`INSTALL-TRUST.md`](INSTALL-TRUST.md) | `INSTALL-TRUST` | producer - adopted 2026-09-22; the page is a render target in 13 locales (1.1) |
| [`FDSEC.md`](FDSEC.md) | `FDSEC-FORMAT`, `FDSEC-BEHAVIOUR` | consumer, reader half - shipped (specification 026); reader implements 1.1, catalog at 1.3/1.5 with no reader obligation moved |
| [`ICON.md`](ICON.md) | `ICON-SET`, `ICON-RENDER`, `ICON-EXTERNAL` | consumer - viewer, Share Manager and site (specification 027); vendored at 0.17/0.15/0.11 |
| [`APP-UX.md`](APP-UX.md) | `APP-BEHAVIOUR`, `APP-STYLE` | consumer, twice - viewer and Share Manager (specification 028); at 0.12/0.11 |
| [`WAVE-PARTICLES.md`](WAVE-PARTICLES.md) | `WAVE-PARTICLES` | consumer - the audio backdrop and the site hero, at 0.14 (specification 029) |
| [`LETTERBOX-FILL.md`](LETTERBOX-FILL.md) | `LETTERBOX-BARS`, `LETTERBOX-HALO` | **owner** - the perspective bars and the halo; rung 1 not yet held (specification 056) |
| [`PAGE.md`](PAGE.md) | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` | consumer - the product site (specification 030); at 1.2/1.2/1.2, kit re-vendored |
| [`DIAGNOSTIC-REPORT.md`](DIAGNOSTIC-REPORT.md) | `DIAGNOSTIC-REPORT` | producer - the send-logs bundle (0.12; the PIN leak is fixed, the shape items stay open) |
| [`APP-ACTIVATION.md`](APP-ACTIVATION.md) | `APP-ACTIVATION` | consumer - single-instance forwarding over `WM_COPYDATA` (ticket 040); declared transport under 0.11 |
| [`CLIPBOARD-GUARD.md`](CLIPBOARD-GUARD.md) | `CLIPBOARD-GUARD` | consumer - the Share Manager copies credentials (ticket 041); at 0.10 |
| [`MEDIA-CLASSIFICATION.md`](MEDIA-CLASSIFICATION.md) | `MEDIA-CLASSIFICATION` | consumer - category and browsing alignment to 0.10 (ticket 043) |
| [`AUTOMATED-CHECKS.md`](AUTOMATED-CHECKS.md) | `CHECK-VERDICT`, `CHECK-BASELINE`, `CHECK-PLACEMENT`, `BUILD-EVIDENCE` | consumer - the test runner and generator checks (ticket 042); at 0.11/0.10/0.11/0.10 |
| [`DOC-QUALITY.md`](DOC-QUALITY.md) | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` | consumer - the internal docs and the published site, adopted 2026-09-26 (tickets 058, 059); at 0.9/0.10 |
| [`CAPTURE-OUTPUT.md`](CAPTURE-OUTPUT.md) | `CAPTURE-OUTPUT` | not bound, re-read against 0.3 on 2026-10-02 - no capture kind is written; section 5 excludes edited copies and conversions; only the QR image's name and folder await the owner (ticket 096) |
| [`RULE-ADOPTION.md`](RULE-ADOPTION.md) | `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT`, `RULE-DELIVERY` | producer of the stamp, consumer of the rest; at 0.11/0.10/0.10/0.11 |
| [`PACKAGE-VERSIONING.md`](PACKAGE-VERSIONING.md) | `PACKAGE-VERSIONING` | declared consumer - one mechanical stamp per package; the `YY.M.D.HHmm` rendering is filed as a proposal (0.2) |
| [`INPUT-CONTROLS.md`](INPUT-CONTROLS.md) | `INPUT-CHORD`, `INPUT-PARITY` | declared, not yet implementing - no chord storage or global hook today; expected device-column contributor |
| [`NO-ROLE.md`](NO-ROLE.md) | `LAN-DISCOVERY`, `UPDATE-MANIFEST`, `USER-PLAYLIST` | none - declared so the next run does not re-investigate |

Last full alignment: **2026-10-02** (specification 098, the final inter-project contract sync before
freeze). Every contract in the catalog was re-classified for this product against the catalog's
2026-10-02 state; the per-contract verdicts and the freeze snapshot live in
`docs/specifications/098_freeze_inventory_2026-10-02.md`. Not listed here: `OCR-ACCURACY` - the Android
sibling's record; since ticket 033 Ф-3 removed the mis-citations nothing in this repository cites it, so
no pointer is owed (a decision, 2026-09-25) - and `CLI-EVENT-STREAM`, `LAYOUT-SIGNAL`, `LAYOUT-PALETTE`,
`SCENARIO-FILE`, `FDD-FORMAT`, `FDD-BEHAVIOUR` and `DISK-SHARE`, which belong to other
products' functions and bind nothing here (the
`WORKER-IPC` pointer covers the schema-2 disk extension of `DISK-SHARE` this host does not use).

Windows UI amendment, 2026-10-05: the `APP-UX` pointer covers `APP-SETTINGS` 0.2, `APP-STYLE` 0.12
and `WINDOWS-UI` 0.1. The owner adopted the modern viewer/settings as the shared Windows reference;
other projects' implementation and acceptance remain their own audits.
