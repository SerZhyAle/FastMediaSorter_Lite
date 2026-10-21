# Pointer - `FMSCFG`

| | |
| --- | --- |
| **Id** | `FMSCFG` |
| **Version** | 2.5, active (since 2026-10-07; 2.5 of 2026-10-17; read on 2026-10-20: the rules, `CONFIG_FORMAT.md` and the 2.5 amendment). Owner: FMS Companion (the Go worker repository) |
| **Home** | `config-interchange/README.md` and `CONFIG_FORMAT.md` in the shared contracts catalog |
| **Role here** | **producer and consumer**, modern only. Producers: the Share Manager (`src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb`, `ShareText.vb`, `ExchangeExport.vb`; the worker's own exporter is unused here) and, as a second producer, the viewer (`src/Resources/FmsConfigExport.vb`, published through `ExportWriter.vb`). Consumer: the viewer, through one reader (`src/Resources/FmsConfigReader.vb`, field table `FmsImportRules.vb`, entry `PhoneConnectionIntake.vb`). The Share Manager reads no `.fmscfg` |

## What this repository must do to stay conformant

These bind both producers; the viewer's export offers SFTP resources only, one per document, with no `accessNote` and no PIN field.

- **The payload is a secret (rule 6).** It carries the password and `accessPin`: never logged unredacted
  (`ShareConfigBuilder.RedactSecrets`), every surface that shows it says so, and a saved file gets an owner-only ACL
  and no stray copy.
- **Transports (rule 4).** Plain compact JSON, and `FMSCFG1:` + base64(gzip(json)) above comfortable QR density;
  the prefix decides. A payload that will not fit is reported, never truncated.
- **Versions (rules 1, 3).** A reader refuses a `schemaVersion` above its own cleanly, reads nothing else and never
  imports in part. A writer keeps the adaptive rule: `schemaVersion` 2 only when a root carries a v2 field; a field
  whose being ignored changes security or write semantics is a version bump.
- **Write semantics (2.1, 2.5 a, b).** `roots[].readOnly` is written on every root, `true` included. A destination is
  writable whatever the read-only hint says (`readOnly:true` beside `isDestination:true` is writable). The reader keeps
  the historical rule: a root is writable iff `readOnly` is false or `isDestination` is true, and an absent `readOnly`
  is true. A non-empty `accessPin` is never imported as unlocked: the viewer holds no resource PIN and refuses the root.
- **Access paths (rules 2, 7; 2.5 c).** Order `lan`, `ipv6`, `portforward`, then `rendezvousTunnel` last, only usable
  entries. A `portforward` the probe confirmed closed is dropped; a never-checked one stays, because unchecked is
  not working but not dead either. A tunnel path always carries a valid `host` and `port` and is never in a LAN-only
  export. A reader skips an unknown kind without dialling it and refuses a file with no usable path.
- **`accessNote` (2.3, 2.5 d).** Written in the exporter's UI language and, when that is not English, the same note in
  English after one line feed (mandatory since 2.5); English alone is written once.
- **Password (2.2 C).** An empty `password` means not carried: the importer asks the person first, and an importer
  that cannot ask refuses rather than create a resource that cannot log in.
- **Bytes (rule 5).** The reader is pinned to the catalog's two canonical vectors byte for byte (the fixture corpus
  `tests/Lite.Tests/TestData/resources/fmscfg/`, never regenerated here). Each producer is pinned to its own bytes
  and itemizes its difference from the vectors: `tests/Companion.Tests/ShareConfigCanonicalVectorTests.vb` and
  `tests/Lite.Tests/FmsConfigExportTests.vb`.
- **Defaults the contract does not write are this reader's own.** An absent fingerprint leaves the first connection
  to an explicit trust prompt whose default is No; none of it is a claim about the wire.
- **A saved QR image.** `FMSCFG` carries the QR as a payload variant and the file as `<name>.fmscfg`, not the image,
  so the image's name and folder are not decided here (`CAPTURE-OUTPUT` section 5 sends the question to this
  contract); the Share Manager keeps its own behaviour and the image holds the password.

## Where it stands

The registry row of this product and its dated exceptions say what is proven and what is open; this pointer never
repeats them. Specification 244 holds the clause-by-clause audit.
