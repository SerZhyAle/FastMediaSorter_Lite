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
| [`OCR-OVERLAY.md`](OCR-OVERLAY.md) | `OCR-OVERLAY` | consumer - the in-app OCR + translation overlay |
| [`OCR-EXCHANGE.md`](OCR-EXCHANGE.md) | `OCR-EXCHANGE` | owner of the record |
| [`OCR-INVOCATION.md`](OCR-INVOCATION.md) | `OCR-INVOCATION` | consumer - the "translate in the browser" button |
| [`OCR-PIPELINE.md`](OCR-PIPELINE.md) | `OCR-PIPELINE` | none as an implementer - the source every *inherited* OCR constant cites (ticket 033) |
| [`FMSCFG.md`](FMSCFG.md) | `FMSCFG` | producer - the Share Manager writes the `.fmscfg` and its QR |
| [`WORKER-IPC.md`](WORKER-IPC.md) | `WORKER-IPC` | consumer - the Share Manager drives the worker |
| [`SHARE-SESSION.md`](SHARE-SESSION.md) | `SHARE-SESSION` | none - this repository authored the document only |
| [`LIVE-BROADCAST.md`](LIVE-BROADCAST.md) | `LIVE-BROADCAST` | planned consumer (specification 023) |
| [`STREAM-BANK.md`](STREAM-BANK.md) | `STREAM-BANK` | planned producer (specification 022) |
| [`INSTALL-TRUST.md`](INSTALL-TRUST.md) | `INSTALL-TRUST` | producer - adopted 2026-09-22; the page is a render target in 13 locales |
| [`FDSEC.md`](FDSEC.md) | `FDSEC-FORMAT`, `FDSEC-BEHAVIOUR` | consumer, reader half - shipped (specification 026); one MINOR behind 1.1 (ticket 038) |
| [`ICON.md`](ICON.md) | `ICON-SET`, `ICON-RENDER`, `ICON-EXTERNAL` | consumer - viewer, Share Manager and site (specification 027); glyph drift (ticket 044) |
| [`APP-UX.md`](APP-UX.md) | `APP-BEHAVIOUR`, `APP-STYLE` | consumer, twice - viewer and Share Manager (specification 028) |
| [`WAVE-PARTICLES.md`](WAVE-PARTICLES.md) | `WAVE-PARTICLES` | consumer - the audio backdrop and the site hero, a look-alike today (specification 029) |
| [`LETTERBOX-FILL.md`](LETTERBOX-FILL.md) | `LETTERBOX-BARS`, `LETTERBOX-HALO` | **owner** - the perspective bars and the halo; rung 1 not yet held (specification 056) |
| [`PAGE.md`](PAGE.md) | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` | consumer - the product site (specification 030) |
| [`DIAGNOSTIC-REPORT.md`](DIAGNOSTIC-REPORT.md) | `DIAGNOSTIC-REPORT` | producer - the send-logs bundle; a PIN leak to fix first (ticket 039) |
| [`APP-ACTIVATION.md`](APP-ACTIVATION.md) | `APP-ACTIVATION` | consumer - single-instance forwarding over `WM_COPYDATA` (ticket 040) |
| [`CLIPBOARD-GUARD.md`](CLIPBOARD-GUARD.md) | `CLIPBOARD-GUARD` | consumer - the Share Manager copies credentials (ticket 041) |
| [`MEDIA-CLASSIFICATION.md`](MEDIA-CLASSIFICATION.md) | `MEDIA-CLASSIFICATION` | consumer - `MediaKindClassifier`, a local classifier today (ticket 043) |
| [`AUTOMATED-CHECKS.md`](AUTOMATED-CHECKS.md) | `CHECK-VERDICT`, `CHECK-BASELINE`, `CHECK-PLACEMENT`, `BUILD-EVIDENCE` | consumer - the test runner and generator checks (ticket 042) |
| [`DOC-QUALITY.md`](DOC-QUALITY.md) | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` | consumer - the internal docs and the published site, adopted 2026-09-26 (tickets 058, 059) |
| [`RULE-ADOPTION.md`](RULE-ADOPTION.md) | `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT`, `RULE-DELIVERY` | producer of the stamp, consumer of the rest |
| [`NO-ROLE.md`](NO-ROLE.md) | `LAN-DISCOVERY`, `UPDATE-MANIFEST`, `USER-PLAYLIST` | none - declared so the next run does not re-investigate |

Last full alignment: **2026-09-24**. Every contract in the catalog was classified for this product on that
date. Not listed here: `OCR-ACCURACY` - the Android sibling's record; since ticket 033 Ф-3 removed the
mis-citations nothing in this repository cites it, so no pointer is owed (a decision, 2026-09-25) - and
`CLI-EVENT-STREAM`, `LAYOUT-SIGNAL`,
`LAYOUT-PALETTE`, `SCENARIO-FILE`, which belong to other products' functions and bind nothing here.
