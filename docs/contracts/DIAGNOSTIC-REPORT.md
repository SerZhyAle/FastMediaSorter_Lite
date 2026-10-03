# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.12, draft (read 2026-10-02: both proposals this product filed - value-aware redaction and tail-only logs with optional carriers - are folded into 0.12). Owner: StreamsPlayer |
| **Home** | `diagnostic-report/README.md` in the shared contracts catalog |
| **Role here** | producer - "send logs to the author" (.NET 10 mainline only): `src/Support/LogPackage.vb`, `src/Table_Form.SendLogs.vb`, `src/Support/MailSender.vb` |

## What this repository must do to stay conformant

- **Never let a secret leave the machine - and redact by content, not by key name (0.12 item C).** The
  rule 3 leak is fixed: `Share_RootParams` - the JSON blob that held access PINs and shared host paths -
  is in `NeverEmittedSettingNames` and is skipped whole, not even its name reaching the report
  (`LogPackage.vb`); the allow-list otherwise admits switches and counts only. The registry exception
  (rule 3, until 2026-10-15) closed 2026-10-02 on that evidence, held by `LogPackageTests`.
- Keep what already holds: the bundle is built only on the user's click, a consent dialog lists every
  entry, and it goes out through the user's own mail client - the app opens no connection (rule 5).
- Redaction precedes truncation (0.12 item C): the tail cut in `ReadTail` lands on a record boundary
  before the line-by-line redaction runs - a theoretical, not a practical, exposure; aligning the order is
  the open item below.
- The 0.12 shape still to adopt, carried by the registry exception on rules 1, 2 and 4: an
  `environment.txt` with `KEY=value` in UTC and `schema_version` (the bundle today has a prose
  `report.txt` with a local-time stamp and carries no schema marker), the archive-name pattern without
  this product's trailing random token, and a stated metric set (even a stated "no catalog family" line).

`PROPOSAL-2026-09-24-value-aware-redaction.md` and
`PROPOSAL-2026-09-26-tail-only-logs-and-optional-carriers.md` were both accepted into 0.12 (README
section 9); nothing of either remains open against the owner.
