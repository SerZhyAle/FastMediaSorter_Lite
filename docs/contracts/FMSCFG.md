# Pointer - `FMSCFG`

| | |
| --- | --- |
| **Id** | `FMSCFG` |
| **Version** | 2.1, active. Owner: FMS Companion (the Go worker repository) |
| **Home** | `config-interchange/README.md` + `CONFIG_FORMAT.md` in the shared contracts catalog |
| **Role here** | **producer** - `src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb` builds the shipped `.fmscfg` and its QR |

This product is the producer that actually ships. The worker has an exporter of its own and it is unused:
it cannot advertise a manually forwarded router port, so the payload is assembled here instead.

## What this repository must do to stay conformant

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
  2026-12-31):** a `portforward` whose external check failed at export time is still emitted, pending
  the owner's answer to item 2 of `PROPOSAL-2026-09-25-fms-windows-producer.md`.
- **Dated exception (until 2026-12-31):** the "do not include the password" export writes
  `"password":""`, which the contract does not describe; no surface may promise what the phone does with
  it until the Android side answers (item 4 of the same proposal).
- Keep the adaptive version rule exactly: `schemaVersion` is 2 only when at least one root carries at
  least one v2 field, so an unconfigured share stays importable by apps that predate v2.
- Support both transports - plain JSON, and `FMSCFG1:` + base64(gzip(json)) above comfortable QR density -
  and when even the compressed payload will not fit, **say so and never truncate**.
- Adding an optional field is a version bump whenever ignoring it would change **security or write
  semantics**. `accessPin` and `isDestination` are the worked examples.

The `roots[].readOnly` finding closed on 2026-09-22, when the owner accepted the proposal it produced and
`FMSCFG` went to 2.1. The byte-stability exception was **narrowed** the same day by ticket 025 section 8:
`tests/Companion.Tests/ShareConfigCanonicalVectorTests.vb` now pins this producer's exact bytes for both
canonical inputs, with `createdAt` and the machine-derived resource name injected, and proves the output
differs from the catalog's vectors only in optional fields - stripping `roots[].readOnly` and `accessNote`
from the v1 output returns the canonical v1 vector character for character.

What is left is the contract's, not ours: rule 5 as written asks a producer to be byte-identical to those
vectors, and the two shipped producers make different - both legal - choices about which optional fields to
emit, so no second producer can satisfy it. That went to the owner as a written proposal
(`PROPOSAL-2026-09-22-canonical-vectors-per-producer.md` in the catalog), never as a local behaviour
change.
