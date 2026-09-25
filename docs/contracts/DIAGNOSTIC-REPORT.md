# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.9, draft (read 2026-09-24). Owner: StreamsPlayer. **Implemented: none** - the bundle predates the draft |
| **Home** | `diagnostic-report/README.md` in the shared contracts catalog |
| **Role here** | producer - "send logs to the author" (.NET 10 mainline only): `src/Support/LogPackage.vb`, `src/Table_Form.SendLogs.vb`, `src/Support/MailSender.vb` |

## What this repository must do to stay conformant

- **Never let a secret leave the machine.** Redaction by value *name* is not enough: `Share_RootParams` is a
  JSON blob holding access PINs and its name matches no filter. This is a leak today, a dated exception in
  the registry, and phase 1 of [ticket 039](../specifications/done/039_SPECIFICATION_DIAGNOSTIC_BUNDLE_REDACTION.md).
- Keep what already holds: the bundle is built only on the user's click, a consent dialog lists every
  entry, and it goes out through the user's own mail client - the app opens no connection (rule 5).
- Move towards the draft's shape (bundle name, `environment.txt` with `KEY=value` in UTC, counts instead of
  paths, log retention and head+tail truncation) once the owner answers the value-aware-redaction proposal
  filed beside the contract.
