# Pointer - `FMSCFG`

| | |
| --- | --- |
| **Id** | `FMSCFG` |
| **Version** | 2.2, active (read 2026-10-02; this producer already behaves as 2.2 describes - it was written from this code). Owner: FMS Companion (the Go worker repository) |
| **Home** | `config-interchange/README.md` + `CONFIG_FORMAT.md` in the shared contracts catalog |
| **Role here** | **producer** - `src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb` builds the Share Manager's shipped `.fmscfg` and its QR; since ticket 118 the viewer is the **second producer**: [src/Resources/FmsConfigExport.vb](../../src/Resources/FmsConfigExport.vb) turns one registered resource into the same document (file save + both clipboard transports, one resource per document, no `accessNote`, no PIN fields), published through [src/Resources/ExportWriter.vb](../../src/Resources/ExportWriter.vb) under the same rule 5 discipline, and pinned by its own bytes in `FmsConfigExportTests` (the per-producer vectors rule, 2.2 item B) |

This product is the producer that actually ships. The worker has an exporter of its own and it is unused:
it cannot advertise a manually forwarded router port, so the payload is assembled here instead.

## What this repository must do to stay conformant

These rules bind BOTH producers - the Share Manager and, since ticket 118, the viewer's
`FmsConfigExport` (which offers SFTP resources only: an SMB selection and an unpinned one are
refused with their own sentence, `accessPin` is never emitted because the viewer holds none,
and `"password":""` is written whenever the user chose "without" or no credential is stored).

- **The payload is a secret.** It carries the SFTP password, and `accessPin` travels in plaintext beside
  it. Nothing may log it unredacted - `ShareConfigBuilder.RedactSecrets` blanks the password **and every
  `accessPin`** - and every surface that shows it says what it is: the package wizard's always-visible
  line, the email body, an owner-only ACL on a saved file (`WriteConfigFile`), no temporary copy left
  behind by "Send by email..".
- A destination is writable on the wire, whatever the soft read-only hint says: `readOnly:true` beside
  `isDestination:true` resolves to writable under 2.1 amendment A, so `AdvertisedReadOnly` ignores the
  hint on a destination and the UI greys it there. Never re-couple the two.
- Emit `accessPaths` in the order `lan` -> `ipv6` -> `portforward`, omitting any entry that is not
  actually usable. An absent entry is honest; a dead entry is a bug. **Dated exception (until
  2026-12-31):** a `portforward` whose external check failed at export time is still emitted, pending the
  owner's answer to item 2 of `PROPOSAL-2026-09-25-fms-windows-producer.md` - the one item of that
  proposal 2.2 leaves open. The behaviour is pinned by
  `ShareConfigBuilderTests.Build_CheckedAndClosedPortforward_IsStillExported_PendingOwner`.
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
