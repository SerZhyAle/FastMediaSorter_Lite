# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.12 implemented, draft (read 2026-10-02, re-read 2026-10-07 with sections 7 and 8, shape adopted 2026-10-16 under spec 039 phase 2). The catalog has since moved to 0.13 (section 10, exchange values) - not yet audited against the bundle, see the last bullet below. Both proposals this product filed - value-aware redaction and tail-only logs with optional carriers - were folded into 0.12. Owner: StreamsPlayer |
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
- **Redaction precedes truncation (0.12 item C) - held since spec 039 phase 2.** `LogPackage.CompactRedacted`
  reads a log line by line, replaces the user-profile path, and only then keeps the first 256 KB and the
  last ~1.75 MB (the product's own ceiling of 2 MB per log, which item D allows, below the contract's 16 MB).
  The cut leaves `[Diag] LOG TRUNCATED | dropped_middle_bytes=<n> | kept_head_bytes=<n> | kept_tail_bytes=<n>`
  between the two parts, the counts being those of the redacted text. A source over 64 MB is not read and
  becomes `[Diag] LOG OMITTED | reason=oversized_untrusted | source_bytes=<n>`. Held by `LogPackageTests`,
  `LogPackageSourcesTests` and `LogTailSnapshotTests`.
- **The 0.12 shape - held since spec 039 phase 2.** `environment.txt` is `KEY=value`, UTC, `schema_version=1`
  first, facts only, with the metric set stated as `volume_metrics=none` (this product keeps no catalog of
  titles, URLs or history); it travels beside the prose `report.txt` (item B). The archive is
  `fms-logs-<version>-yyyyMMdd-HHmmss.zip` with nothing after the stamp - a name taken in the same second
  moves to the next free second. The rolled-over log (`previous.log`, `companion.previous.log`) is bundled
  beside its current log. Retention does not apply: item D leaves "keep the last ten sessions" to products
  that write one log per session, and this one rolls in place at 8 MB (`AppFileLogger.Max_Log_Bytes`).
- **Still open (dated registry exception):** the redactor for the section 7 shapes, and the audit of the
  0.13 section 10 exchange values against the bundle (the catalog moved from 0.12 to 0.13 after this
  pointer was last read; the bundle never includes an exchange account file, and no check of the exchange
  logs' own content was made under spec 039).

Both proposals are accepted (README section 9); nothing of either remains open against the owner.
