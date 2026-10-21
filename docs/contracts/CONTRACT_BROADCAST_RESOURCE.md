# Pointer - `BROADCAST-RESOURCE`

| | |
| --- | --- |
| **Id** | `BROADCAST-RESOURCE` |
| **Version** | 1.0, active (since 2026-10-14; wire `schemaVersion` 1; read on 2026-10-20). Owner: FMS Companion and StreamsPlayer |
| **Home** | `broadcast-resource/README.md` in the shared contracts catalog |
| **Role here** | **producer** of the `.spres` document, its QR and the `SPCH1` quick link for a live broadcast: `src/FastMediaSorterCompanion/Core/BroadcastConfigBuilder.vb`, in the Share Manager (specification 196; plain HTTP through the worker's gateway, one resource per token). No reader here. Modern only |

## What this repository must do to stay conformant

- **The document.** `schemaVersion` 1, `kind` `broadcast`, `protocol` always `http`; the access paths ordered `lan`,
  `ipv6`, `portforward`, all under one `path`; `.fmscfg` is not touched and keeps its own schema. A `portforward`
  path that the gateway's own connect-back confirmed closed is not written, and a never-checked one stays - the
  producer choice of `FMSCFG` 2.5 (c) applied to this document (2026-10-21, specification 252 T-2; the contract is
  silent on it, so it is a recorded reading, not a clause).
- **Transport.** A `.spres` file is plain UTF-8 JSON without a BOM. A QR carries plain JSON when small and
  `SPRES1:` + base64(gzip(json)) above about 900 bytes, the same switch as `FMSCFG1:`.
- **The clipboard line is the lossy variant, on purpose.** `SPCH1 http://<best host>:<port>/b/<token>`: one
  address, no title, no expiry, no playlist flag, offered as a quick link and never as the primary export. The best
  host is the port-forward address when the internet path is on, otherwise the LAN address.
- **Evidence.** The producer's own bytes are pinned by `tests/Companion.Tests/BroadcastConfigBuilderTests.vb`.

## Where it stands

The registry row of this product and its dated exceptions say what is proven and what is open; this pointer never
repeats them. Specification 244 holds the clause-by-clause audit.
