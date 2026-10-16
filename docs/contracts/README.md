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
| [`OCR-OVERLAY.md`](OCR-OVERLAY.md) | `OCR-OVERLAY` | consumer - the in-app OCR + translation overlay (1.3, re-read 2026-10-07) |
| [`OCR-EXCHANGE.md`](OCR-EXCHANGE.md) | `OCR-EXCHANGE` | owner of the record |
| [`OCR-INVOCATION.md`](OCR-INVOCATION.md) | `OCR-INVOCATION` | consumer - the "translate in the browser" button (1.1) |
| [`OCR-PIPELINE.md`](OCR-PIPELINE.md) | `OCR-PIPELINE` | consumer of part of the mechanism and the source every *inherited* OCR constant cites (1.9; registry row since 2026-10-07) |
| [`FMSCFG.md`](FMSCFG.md) | `FMSCFG` | producer - the Share Manager writes the `.fmscfg` and its QR (2.4: `accessNote` in the PC's language plus English, tunnel rule 7); consumer - the 64-bit viewer reads it through one reader (consumer section, re-read against 2.4 on 2026-10-07) |
| [`CONTRACT_BROADCAST_RESOURCE.md`](CONTRACT_BROADCAST_RESOURCE.md) | `BROADCAST-RESOURCE` | producer - the Share Manager writes the `.spres`, QR and quick link (1.0) |
| [`WORKER-IPC.md`](WORKER-IPC.md) | `WORKER-IPC` | consumer - the Share Manager drives the worker (0.14: host half of the exchange vectors, the `[excluded-range]` token, the status sub-fields and the unknown-is-never-authority rule for the disk requests) |
| [`DISK-SHARE.md`](DISK-SHARE.md) | `DISK-SHARE` | consumer - file-backed Share Manager disk control (1.4, partial); an unknown state, holder or record never enables a mount; native/service release proof remains open |
| [`SHARE-SESSION.md`](SHARE-SESSION.md) | `SHARE-SESSION` | client of the session for the viewer's SFTP resources (O1-O4); custodian of guarantee 3's state across installs, uninstalls and host-mode changes; no server half (1.1, re-read 2026-10-07) |
| [`DEVICE-EXCHANGE.md`](DEVICE-EXCHANGE.md) | `DEVICE-EXCHANGE` | **exchange-server implementer** and device client, resource producer and consumer, broadcaster and receiver (local specifications 160-234) - 1.1 read 2026-10-16, registry `partial 1.1`: the Go server and the clients are built and the client gate has been open since 2026-10-14 (specification 223); rung 1 is this product's own generator against its own replay, no joint rung has run (205), the Drive channel (203) and the outward access (239 CR-13) are not built, the version table of what was written against which version is in specification 235 |
| [`ANYWHERE-ACCESS.md`](ANYWHERE-ACCESS.md) | `ANYWHERE-ACCESS` | producer and consumer of the share descriptor and the third-party port (local specifications 170, 171 and 227) - 1.0 read 2026-10-16, registry `partial 1.0`; its exchange-server sections 5-7 moved to `DEVICE-EXCHANGE` in 0.11, sections 4, 5.5 and 8 bind; the `FMSSFTP1`/`FMSSFTP2` codec is built (`src/Exchange/FmsSftpCode.vb`) and its descriptor vector is replayed; the outward link (K, L) and the Drive opt-in (H) are open |
| [`LIVE-BROADCAST.md`](LIVE-BROADCAST.md) | `LIVE-BROADCAST` | consumer (specification 023), **PC producer** (199) and receiver (201) - 1.0 read 2026-10-16, registry `partial 1.0`; the gate is open, the section 6 handshake has not been run by this product, the protected-broadcast completion on the relay is open (239 CR-14, CR-15) |
| [`STREAM-BANK.md`](STREAM-BANK.md) | `STREAM-BANK` | producer, implemented (specification 022, `src/Streams/`, 2.2); on main, in no release |
| [`INSTALL-TRUST.md`](INSTALL-TRUST.md) | `INSTALL-TRUST` | producer - adopted 2026-09-22; the page is a render target in 13 locales (1.2: no "Unblock" instruction, held by a test) |
| [`FDSEC.md`](FDSEC.md) | `FDSEC-FORMAT`, `FDSEC-BEHAVIOUR` | consumer, reader half (specification 026): suite 1 of the format (reads 1.3) and the reader sections of the behaviour (1.5); suites 2 and 3 not opened; on main, in no release |
| [`ICON.md`](ICON.md) | `ICON-SET`, `ICON-RENDER`, `ICON-EXTERNAL` | consumer - viewer, Share Manager and site (specification 027); vendored at 0.33 (105 catalog glyphs, none product-drawn), `ICON-RENDER` 0.19, `ICON-EXTERNAL` 0.12, re-read 2026-10-16 (specification 235), partial under dated registry exceptions |
| [`APP-UX.md`](APP-UX.md) | `APP-BEHAVIOUR`, `APP-STYLE`, `APP-SETTINGS`, `WINDOWS-UI` | consumer, twice - viewer and Share Manager (specification 028); at 0.12 / 0.12 / 0.3 / 0.3, partial under dated registry exceptions |
| [`WAVE-PARTICLES.md`](WAVE-PARTICLES.md) | `WAVE-PARTICLES` | consumer - the audio backdrop and the site hero, at 0.15 (specification 029; 0.15 asks nothing of the viewer) |
| [`LETTERBOX-FILL.md`](LETTERBOX-FILL.md) | `LETTERBOX-BARS`, `LETTERBOX-HALO` | **owner** - the perspective bars and the halo at 0.1; rung 1 held since 2026-10-13 (the math in `src/LetterboxFillMath.vb`, the catalog's vectors in `LetterboxFillConformanceTests`, both runtimes); rung 2 (pixels against GDI+) unmeasured, rung 3 not written |
| [`PAGE.md`](PAGE.md) | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` | consumer - the product site (specifications 030, 181); at 1.4 / 1.6 / 1.3, the 1.3 kit vendored again 2026-10-07, gaps are exceptions |
| [`DIAGNOSTIC-REPORT.md`](DIAGNOSTIC-REPORT.md) | `DIAGNOSTIC-REPORT` | producer - the send-logs bundle (0.12; the PIN leak is fixed and the bundle shape is adopted, the credential-shape redactor and the 0.13 exchange-value audit stay open) |
| [`APP-ACTIVATION.md`](APP-ACTIVATION.md) | `APP-ACTIVATION` | consumer - single-instance forwarding over `WM_COPYDATA` (ticket 040); declared transport under 0.11, rule 5's foreground mechanism recorded as a deviation |
| [`CLIPBOARD-GUARD.md`](CLIPBOARD-GUARD.md) | `CLIPBOARD-GUARD` | consumer - **writer** (the Share Manager and the viewer's resource export copy credentials with the privacy markers; the wrapper writers keep the item B retry exception) and, since specification 230, **reader and participant** (the Share Manager's exchange clipboard, the viewer's phone-code paste); 0.11 read 2026-10-16, registry `partial 0.11`; the order of kinds differs (239 CR-18) |
| [`MEDIA-CLASSIFICATION.md`](MEDIA-CLASSIFICATION.md) | `MEDIA-CLASSIFICATION` | consumer - category and browsing alignment to 0.10 (ticket 043); rule 4 (sidecars) not implemented |
| [`USER-PLAYLIST.md`](USER-PLAYLIST.md) | `USER-PLAYLIST` | consumer, partial - the Extended M3U8 reader of the stream-catalog producer (specification 022) at 0.12; HLS manifest rejection and two section 8 bounds are exceptions |
| [`AUTOMATED-CHECKS.md`](AUTOMATED-CHECKS.md) | `CHECK-VERDICT`, `CHECK-BASELINE`, `CHECK-PLACEMENT`, `BUILD-EVIDENCE` | producer and consumer - the test runner and generator checks (ticket 042); at 0.12 / 0.11 / 0.11 / 0.10, partial, see the registry |
| [`DOC-QUALITY.md`](DOC-QUALITY.md) | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` | consumer - the internal docs and the published site, adopted 2026-09-26 (tickets 058, 059); at 0.9/0.10 |
| [`CAPTURE-OUTPUT.md`](CAPTURE-OUTPUT.md) | `CAPTURE-OUTPUT` | not bound, re-read against 0.5 on 2026-10-07 - no capture kind is written, so the PNG `tIME` rule 16 does not bind; section 5 excludes edited copies, conversions and the QR image, whose name and folder are `FMSCFG`'s to decide (a proposal is drafted) |
| [`RULE-ADOPTION.md`](RULE-ADOPTION.md) | `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT`, `RULE-DELIVERY` | producer of the stamp, consumer of the rest; at 0.12 / 0.11 / 0.11 / 0.12; `HARNESS-PROFILE` not bound |
| [`PACKAGE-VERSIONING.md`](PACKAGE-VERSIONING.md) | `PACKAGE-VERSIONING` | consumer, Windows desktop profile - one mechanical stamp per package, registered as dotted-loose (0.4); read at 0.5 |
| [`WINDOWS-STORE.md`](WINDOWS-STORE.md) | `WINDOWS-STORE` | producer - the MSIX and the listing copy (0.1); rules 1-5 and 17 held on the last package, the listing builder is not the contract's (exceptions) |
| [`INPUT-CONTROLS.md`](INPUT-CONTROLS.md) | `INPUT-CHORD`, `INPUT-PARITY` | consumer, partial - the viewer's shortcuts dialog stores and captures chords at 0.3; token, refusal and keyboard-column deviations are exceptions |
| [`SITE-STRUCTURE.md`](SITE-STRUCTURE.md) | `SITE-STRUCTURE` | consumer - opted in 2026-10-06, tier guide; the not-found page, the held-address list and the core-three locale floor are open exceptions (specification 147) |
| [`SITE-EXPERIENCE.md`](SITE-EXPERIENCE.md) | `SITE-EXPERIENCE` | consumer - opted in 2026-10-06, guide tier; kit, shared keys and origin disclosure held, skip link and `nav` landmark and light-theme contrast open (specification 147); `style` attributes and the resolver order conform in the tree since 2026-10-06 (specification 154); read at 0.3 on 2026-10-07 |
| [`SITE-REPRESENTATION.md`](SITE-REPRESENTATION.md) | `SITE-REPRESENTATION` | consumer - opted in 2026-10-06, guide tier; rules 1-4 held by `tools/Test-SitePositioning.ps1` (ticket 155), rules 5-10 and 12 run 2026-10-06 and failing on the ten function pages (specification 156; ticket 157) |
| [`CONTEXT-HELP.md`](CONTEXT-HELP.md) | `CONTEXT-HELP` | consumer, partial - in-app F1, top-right `?` button on help-bearing windows, shipped `help-map.json`, resolver, offline HTML help pages and static/locale/link gates (specification 240, 0.1) |
| [`NO-ROLE.md`](NO-ROLE.md) | `LAN-DISCOVERY`, `UPDATE-MANIFEST` | none - declared so the next run does not re-investigate (re-read at 0.10 and 0.11) |

Partial alignment: **2026-10-16** (specification 235, release 46): `DEVICE-EXCHANGE` 1.1, `LIVE-BROADCAST` 1.0,
`ANYWHERE-ACCESS` 1.0, `CLIPBOARD-GUARD` 0.11, `ICON-SET` 0.33 and `ICON-RENDER` 0.19 were read in full against the
working tree and their pointers and registry rows rewritten to the evidence; every other row of the table above was
**not** re-read on that day and carries the date of its own row. The check repeats before each later release.

Last full alignment: **2026-10-07** (specification 181, the contract sync after the catalog was sealed; the
previous one was 2026-10-02, specification 098, the final sync before the freeze). Every contract in the catalog was re-classified for this product against the catalog's
2026-10-02 state; the per-contract verdicts and the freeze snapshot live in
`docs/specifications/done/098_freeze_inventory_2026-10-02.md`. Not listed here: `OCR-ACCURACY` - the Android
sibling's record; since ticket 033 Ф-3 removed the mis-citations nothing in this repository cites it, so
no pointer is owed (a decision, 2026-09-25) - and `CLI-EVENT-STREAM`, `LAYOUT-SIGNAL`, `LAYOUT-PALETTE`,
`SCENARIO-FILE`, `FDD-FORMAT` and `FDD-BEHAVIOUR`, which belong to other
products' functions and bind nothing here (the
`WORKER-IPC` pointer covers the schema-2 disk extension of `DISK-SHARE` this host does not use).

Windows UI amendment, 2026-10-05: the `APP-UX` pointer covers `APP-SETTINGS` 0.3, `APP-STYLE` 0.12
and `WINDOWS-UI` 0.3 (read again on 2026-10-07). The owner adopted the modern viewer/settings as the shared Windows reference;
other projects' implementation and acceptance remain their own audits.
