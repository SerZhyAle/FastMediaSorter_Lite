# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.14, draft (since 2026-09-24; 0.12 of 2026-10-02, 0.13 of 2026-10-07, 0.14 of 2026-10-17; read on 2026-10-20: sections 2, 8 and 10 and the consolidation amendment). Owner: StreamsPlayer. Both proposals this product filed, value-aware redaction and tail-only logs with optional carriers, were folded into 0.12 |
| **Home** | `diagnostic-report/README.md` in the shared contracts catalog |
| **Role here** | **producer**, modern only: "send logs to the author" - `src/Support/LogPackage.vb`, `src/Table_Form.SendLogs.vb`, `src/Support/MailSender.vb`; the Share Manager packs the same archive through the linked module (`src/FastMediaSorterCompanion/Forms/SendLogsFlow.vb`). A second bundle producer exists, `publishing/installer/collect-host-state.ps1` (a host-state zip for the exchange server, behind the exchange gate) |

## What this repository must do to stay conformant

- **Consent, no telemetry (rule 5).** The bundle is built only on the user's click, a consent dialog lists every
  entry, and it goes out through the user's own mail client; the app opens no connection.
- **No secret leaves the machine, redacted by content and not by key name (rule 3, 0.12 items C, section 7).**
  The settings in `report.txt` are an allow-list (`SafeSettingNames`), not a dump; `Share_RootParams` (access PINs and
  host paths) is never emitted (`NeverEmittedSettingNames`). Credential shapes, secret query parameters, URL userinfo
  and credential-in-path shapes are redacted in what is archived; the user-profile path becomes `<USER>`.
- **Exchange values (0.13, section 10).** Device token, pairing code, exchange login and server address and
  certificate fingerprints (old and replacement pins) are replaced with `[REDACTED]` at the log sink and again at
  export; an exchange account file is never in a bundle and the environment summary names no account identity.
- **Redaction before truncation (0.12 item C, 0.14).** Each line is redacted and only then cut to its head and its
  tail with `[Diag] LOG TRUNCATED | dropped_middle_bytes=<n> | kept_head_bytes=<n> | kept_tail_bytes=<n>` between
  them. An unsafe oversized source is omitted whole as `[Diag] LOG OMITTED | reason=oversized_untrusted |
  source_bytes=<n>`, fields in that order and no path, name or credential; no raw tail begins inside a private-key
  block (the whole stream is redacted first, with the key state kept across lines and across sink calls). The offline
  collector `publishing/installer/collect-host-state.ps1` follows the same order and markers.
- **The shape (0.12 items A, B, D).** `fms-logs-<version>-yyyyMMdd-HHmmss.zip`; `environment.txt` as `KEY=value`, UTC,
  `schema_version=1` first, facts only, with the metric set stated (`volume_metrics=none`); the rolled-over log is
  bundled beside its current log. Retention does not apply: logs roll in place at 8 MB (item D).
- **Evidence.** Each adopter tests the marker and the redaction on synthetic data: `LogPackageTests`,
  `LogPackageSourcesTests`, `LogTailSnapshotTests` and `DiagnosticRedactorTests` (the catalog's structured-value vector,
  the section 7 shapes, the section 10 values, a private-key block across sink calls) in `tests/Lite.Tests/`; the
  collector by hand with `tests/Integration/CollectHostState.ps1`. The redactor is `src/Support/DiagnosticRedactor.vb`
  (whole-file `#If Not NETFRAMEWORK`, linked into the Share Manager); the net48 exe has no send-logs bundle.

## Where it stands

The registry row of this product and its dated exceptions say which rules are proven and which are open; this
pointer never repeats them. Specification 244 holds the clause-by-clause audit.
