# Pointer - `FMSCFG`

| | |
| --- | --- |
| **Id** | `FMSCFG` |
| **Version** | 2.4, active (read 2026-10-07: 2.3 `accessNote` in the exporter's language plus an English duplicate is implemented here; 2.4 `rendezvousTunnel` rule 7 holds for a dormant exporter and is skipped by the reader - details below). Owner: FMS Companion (the Go worker repository) |
| **Home** | `config-interchange/README.md` + `CONFIG_FORMAT.md` in the shared contracts catalog |
| **Role here** | **producer and consumer.** Consumer: the 64-bit viewer reads a `.fmscfg` through one reader, `src/Resources/FmsConfigReader.vb` - see "The consumer" below. Producer - `src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb` builds the Share Manager's shipped `.fmscfg` and its QR; since ticket 118 the viewer is the **second producer**: [src/Resources/FmsConfigExport.vb](../../src/Resources/FmsConfigExport.vb) turns one registered resource into the same document (file save + both clipboard transports, one resource per document, no `accessNote`, no PIN fields), published through [src/Resources/ExportWriter.vb](../../src/Resources/ExportWriter.vb) under the same rule 5 discipline, and pinned by its own bytes in `FmsConfigExportTests` (the per-producer vectors rule, 2.2 item B) |

This product is the producer that actually ships. The worker has an exporter of its own and it is unused:
it cannot advertise a manually forwarded router port, so the payload is assembled here instead.

## What this repository must do to stay conformant

These rules bind BOTH producers - the Share Manager and, since ticket 118, the viewer's
`FmsConfigExport` (which offers SFTP resources only: an SMB selection and an unpinned one are
refused with their own sentence, `accessPin` is never emitted because the viewer holds none,
and `"password":""` is written whenever the user chose "without" or no credential is stored).

- **The payload is a secret.** It carries the SFTP password, and `accessPin` travels in plaintext beside
  it. Nothing may log it unredacted - `ShareConfigBuilder.RedactSecrets` blanks the password, **every
  `accessPin`** and a tunnel path's share id (a bearer link) - and every surface that shows it says what it is: the package wizard's always-visible
  line, the email body, an owner-only ACL on a saved file (`WriteConfigFile`), no temporary copy left
  behind by "Send by email..".
- A destination is writable on the wire, whatever the soft read-only hint says: `readOnly:true` beside
  `isDestination:true` resolves to writable under 2.1 amendment A, so `AdvertisedReadOnly` ignores the
  hint on a destination and the UI greys it there. Never re-couple the two.
- Emit `accessPaths` in the order `lan` -> `ipv6` -> `portforward`, omitting any entry that is not
  actually usable. An absent entry is honest; a dead entry is a bug. **Dated exception (until
  2026-12-31):** a `portforward` whose external check failed at export time is still emitted, pending the
  owner's answer to item 2 of `PROPOSAL-2026-09-25-fms-windows-producer.md` - the one item of that
  proposal 2.3 still leaves open (reading (a) drop it, reading (b) keep it when `accessNote` names the
  failure; the note does name it today, in two languages). The behaviour is pinned by
  `ShareConfigBuilderTests.Build_CheckedAndClosedPortforward_IsStillExported_PendingOwner`. An unchecked
  port (`externalPortChecked = false`) is unconfirmed, never dead, and is kept (WORKER-IPC item C).
- **`accessNote` is in the exporter's language, then in English (2.3).** `ShareText.AccessNoteForExport`
  writes the note in the PC's UI language and, when that is not English, one line feed and the same note in
  English, read from the same string table without switching the process language. English alone is written
  once, which keeps both byte-pinned vectors unchanged. Pinned by `ShareConfigAccessNoteTests` (all 13 languages,
  every rung of the ladder). The window and the broadcast descriptor keep the one-language `ShareText.AccessNote`.
- **A `rendezvousTunnel` path (2.4) cannot be malformed, and is dormant in practice.** `ShareConfigBuilder.Build` takes
  the exchange argument as an optional one; the package wizard passes what `ExchangeProducerRules.ExportFor` returns
  (`PackageWizardForm.vb`), which is `Nothing` unless the producer is switched on (off by default) AND the worker
  reports a registered share - and no worker in the worker repository reports an exchange registration (2026-10-07), so
  no file carries a tunnel path today (tickets 148, 170, 171 own the publication). When one is passed, a tunnel path is
  written only with a valid `host` and `port` (loopback, blank, oversized port and a malformed share id publish none),
  after every direct path, never in the LAN-only export, and `schemaVersion` stays 1 or 2. Pinned by
  `ShareConfigTunnelPathTests`. The share-id member is named `shareId` as 2.4 registers it
  (`ExchangeWireNames.TunnelShareIdMember`; the provisional `sid` was renamed on 2026-10-07, and the log redaction
  still covers both spellings).
- **A saved QR image is this contract's to place, and it says nothing.** `CAPTURE-OUTPUT` 0.4 section 5 sends the
  question here; `FMSCFG` carries the QR as a payload variant and the file as `<name>.fmscfg`, not the image. The
  Share Manager keeps its own behaviour (`Qr_Zoom_Form.SavePng`: `fms-qr-<share>-<yyyyMMdd-HHmm>.png` in
  `Pictures\Fast Media Sorter\`, a told `%TEMP%` fallback; the image holds the password, so `QrImageOutputTests`
  and the on-screen warning stay) until the owner answers a proposal drafted 2026-10-07; nothing is changed first.
- `roots[].readOnly` is written on **every** root (`true` or `false`, 2.1 amendment A); the owner's choice between that
  and writing it only when it differs from the default stays open (2.2 "Left open") and changes no behaviour here.
- **The "do not include the password" export writes `"password":""`, and 2.2 now says what that means:**
  *not carried - the importer asks the user*, and an importer that cannot ask refuses rather than create a
  resource that cannot log in. The exception this pointer used to carry is closed (2026-10-02, item 4 of
  the same proposal, folded into 2.2 item C with the Android side's answer of 2026-10-01).
- Keep the adaptive version rule exactly: `schemaVersion` is 2 only when at least one root carries at
  least one v2 field, so an unconfigured share stays importable by apps that predate v2.
- Support both transports - plain JSON, and `FMSCFG1:` + base64(gzip(json)) above comfortable QR density -
  and when even the compressed payload will not fit, **say so and never truncate**.
- Adding an optional field is a version bump whenever ignoring it would change **security or write
  semantics**. `accessPin` and `isDestination` are the worked examples.

## Rule 5 under 2.2: split by role, and the producer exception is closed

Secret file publication creates an exclusive sibling file with an owner-only Windows
descriptor before writing bytes. It verifies that protection, flushes the file, checks
the destination identity again, and renames it into place while retaining its descriptor.
Inherited read access on the parent does not reach the exported file. FAT/exFAT and
unsafe or redirected destinations are refused with localized guidance.
Ancestor directories are checked too. Email handoff copies use an owned protected
subfolder under `%LOCALAPPDATA%/FastMediaSorterCompanion/mail`, avoiding a shared TEMP
ancestor; only the directory created by that handoff is cleaned up afterwards.

The writer keeps a protected sibling `.<target>.<guid>.tmp.recovery.tmp` until final
identity, ACL and byte verification. A failed or uncertain publication retains that
copy (the previous target's bytes when one existed). It never deletes a destination
by name after losing ownership. Recovery copies contain credentials and must be treated
like the original config; review them locally and remove them manually when no longer
needed. No automatic recovery-file expiry is introduced.

2.2 item B splits the conformance by role: the **parser** is pinned to the canonical vectors byte for
byte; the **producer** is pinned to **its own** bytes, with the difference from the vectors itemized
(field order, escaping, number formatting, timestamp shape). That is exactly what
`tests/Companion.Tests/ShareConfigCanonicalVectorTests.vb` has pinned since ticket 025 section 8 - this
producer's exact bytes for both canonical inputs, with `createdAt` and the machine-derived resource name
injected, and the proof that stripping `roots[].readOnly` and `accessNote` from the v1 output returns the
canonical v1 vector character for character. The owner accepted
`PROPOSAL-2026-09-22-canonical-vectors-per-producer.md` and folded it into 2.2 on 2026-10-02, closing the
rule 5 residual exception; nothing remains open from that proposal.

## The consumer: what the viewer reads (tickets 100, 101, 007)

The 64-bit viewer imports a `.fmscfg`; the 32-bit fallback has none of this. **One reader** serves every
surface - the open-file dialog, a dropped or launched file, the clipboard and the compressed text a scanner
hands over. `FmsConfigReader` turns bytes or text into a plan, and only `ResourceImportFlow` previews and stores
one; two source-scan tests hold both statements and that the flow looks at a failed read before it looks at a
plan, so no surface can reach a preview around the reader.

What the reader does, each point pinned by a named test:

- `schemaVersion` 1 and 2 are read. A higher one asks the person to update the app and **nothing else is read**,
  on every surface; a missing or invalid one is refused, never guessed.
- `protocol` is exactly `sftp`, case-sensitive. Access paths keep the producer's order; an unknown kind (the 2.4
  `rendezvousTunnel` included) is skipped without being validated and **never dialled**; a file whose only paths
  are unknown is refused. `FmsConfigTunnelKindTests` names the kind (2026-10-07): a tunnel path that breaks 2.4
  rule 7 is still skipped and the file kept, whatever its share id; a tunnel-only file is refused as having no
  usable path (the contract leaves that file undecided); the 16-path limit counts the tunnel.
  `FmsConfigAccessNoteTests`: a note with the exporter's language, a line feed and English (2.3) is accepted verbatim.
- A fingerprint that is present and malformed refuses the file. An absent or empty one leaves the first
  connection to an explicit trust prompt whose default is No. A re-import never clears a stored pin, and a
  changed one needs its own Cancel-default confirmation.
- `password` empty or absent means *not carried*: the person is asked before anything is saved, and a resource
  that cannot log in is never created.
- A root is writable iff `readOnly == false` or `isDestination == true`; `readOnly` absent means true. A non-empty
  `accessPin` refuses that root - the viewer has no resource PIN and would otherwise import it unlocked - and,
  as an **interim** policy that the owner has not chosen (Q100-1), a file with any such root is refused whole.
- A repeated JSON property is refused, never "last wins"; a malformed known field refuses the document and
  nothing is coerced.
- Reading and previewing open no connection and write nothing; the reader's source is scanned for it.

Field support is **one declared table**, `FmsFieldSupport` in `src/Resources/FmsImportRules.vb` (24 rows: mapped,
kept but not applied - `showSubfoldersAsItems` until Q100-4 decides - or "refuses the root when set" -
`accessPin`). A field the reader consumes without a row fails `FmsImportRulesTests`, and so does a row the
reader no longer reads; this page does not restate the rows. Each row also says whether the field reaches the
**saved resource**, and a differential test holds that: the name, addresses, user, pin, root, label, writability
and subfolder scan are saved, while the presentation values (`profile`, `mediaTypes`, `showHiddenFiles`,
`allFiles`, `destinationColor`, `slideshowInterval`, `comment`, `showSubfoldersAsItems`) and
`accessNote` are read and validated and then **not kept** - harmless by the contract, but 100 section 15.5.3 wants
them preserved with a notice, and that waits for Q100-4. A document that sets them imports exactly as one that does not.

The Windows limits are tactical defaults, not wire limits, frozen by the test
`The_limits_are_frozen_at_the_values_100_15_3_recorded`: 1 MiB plain, 256 KiB compressed, 1 MiB inflated
(aborted at the byte past it), JSON depth 32, 100 roots, 16 access paths, 4096 UTF-16 units per string. Q100-3 is
closed: a producer-shaped document at its maximum (16 paths, 100 roots, every v2 field, long free text) uses less
than a quarter of the plain and of the compressed limit, so the limits are not what a real export runs into.

Evidence: `FmsConfigReaderTests` (both canonical vectors byte for byte, the boundary and boundary+1 of every
limit), `FmsImportRulesTests` (the matrix, the pin re-import), `FmsFixtureCorpusTests` and `FmsEntrySurfaceTests`
(the manifest of the fixture corpus, the entry surfaces). The corpus is `tests/Lite.Tests/TestData/resources/fmscfg/`:
`PROVENANCE.txt` records the catalog documents it was taken from (2.4, read 2026-10-07, with digests) and
`MANIFEST.txt` the origin, intended validity and expected typed result of every file. The canonical vectors are the
catalog's own bytes and are never regenerated here; the other files are synthetic, with dummy credentials and
documentation addresses. **What this is not:** a `.fmscfg` written by the shipped Android app has not been
imported by the shipped viewer on two real devices, nor the reverse - parser tests prove the reading, not the
exchange, and that acceptance stays a manual item.

Decisions on record: Q100-1 (a mixed PIN file) and Q100-4 (the rest of the v2 mapping, a resource PIN) belong to the
owner and are open - the interim behaviour above refuses where it could guess. Q100-2 was closed by ticket 118
(the viewer exports one saved SFTP resource, above). Q101-1 and Q101-2 need no catalog change: the
empty-or-absent fingerprint rule is this consumer's default, not a claim about the wire.

Not filed, for the owner: the catalog's `FMSCFG` header names the reference producer, the Share Manager as a
producer, and Android as producer and importer. The viewer is now an importer and a second producer as well, so a
header mention and a registry row are to be added through a proposal; this repository does not edit the catalog.
