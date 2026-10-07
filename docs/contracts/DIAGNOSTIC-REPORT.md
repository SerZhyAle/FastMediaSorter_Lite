# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.12, draft (read 2026-10-02, re-read 2026-10-07 with sections 7 and 8; the catalog is still at 0.12). Both proposals this product filed - value-aware redaction and tail-only logs with optional carriers - were folded into 0.12. Owner: StreamsPlayer |
| **Home** | `diagnostic-report/README.md` in the shared contracts catalog |
| **Role here** | producer - "send logs to the author" (.NET 10 mainline only): `src/Support/LogPackage.vb`, `src/Table_Form.SendLogs.vb`, `src/Support/MailSender.vb`; the Share Manager packs the same archive through the linked module (`Forms/SendLogsFlow.vb`) |

## What this repository must do to stay conformant

- **Never let a secret leave the machine - and redact by content, not by key name (0.12 item C).** The
  settings in `report.txt` are an **allow-list** (`SafeSettingNames`), not a dump: switches, numbers and
  modes are written, path lists become counts, a recipient slot says only set or empty, a secret shows
  `<hidden>`, and `Share_RootParams` - the JSON blob that held access PINs and shared host paths - is in
  `NeverEmittedSettingNames` and skipped whole. Held by `LogPackageTests`. A setting added later is
  withheld until it is added to the list on purpose.
- Keep what already holds: the bundle is built only on the user's click, a consent dialog lists every
  entry, and it goes out through the user's own mail client - the app opens no connection (rule 5).
- **The credential shapes of section 7 are kept out of the logs at write time, not scanned for on the way
  out.** Writers do not log secrets (`ShareConfigBuilder` redacts its own JSON before it logs it; the
  credential-store messages name the failure, never the value), and the copy into the archive replaces the user-profile path with
  `<USER>` and nothing else. A redactor for secret query parameters, credential-in-path shapes and URL
  userinfo does not exist - a dated registry exception.
- Redaction precedes truncation (0.12 item C): today the tail cut lands on a record boundary
  (`ReadTail`) **before** the line-by-line `<USER>` replacement - a theoretical exposure, listed on the
  same exception, with the marker wording of the tail cut (`[.. truncated ..]`, not the contract's
  `[Diag] LOG TRUNCATED` / `LOG COMPACTED` forms).
- The 0.12 shape still to adopt, carried by the registry exception: an `environment.txt` with `KEY=value` in
  UTC and `schema_version` (the bundle has a prose `report.txt` with a local-time stamp and no schema
  marker - 0.12 item B lets a human-readable report travel *beside* the summary, not instead of it), the
  archive-name pattern `<Product>-logs[-<version>]-yyyyMMdd-HHmmss.zip` without this product's trailing
  random token, and a stated metric set (even a stated "no catalog family" line).

Both proposals are accepted (README section 9); nothing of either remains open against the owner.
