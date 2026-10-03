# Pointer - `DISK-SHARE`

| | |
| --- | --- |
| **Id** | `DISK-SHARE` |
| **Version** | 1.3, draft, read 2026-10-04 |
| **Home** | `disk-sharing/README.md` + `DISK-SHARE.md` in the shared contracts catalog |
| **Owner** | FileDO |
| **Role here** | consumer - Share Manager disk UI and schema-2 worker client |

The Share Manager implements file-backed registration, lifecycle controls and disk status through the
existing worker protocol. Root ownership remains separate from ordinary folder updates. A local Windows
drive letter is a user-session alias of the worker mount, not another holder or a phone-facing field.

Evidence: Companion.Tests disk protocol/ownership tests, generated request fixtures and a Windows alias
round-trip; worker suites and its capability probe against an actual source-built FileDO. Native mounting,
encrypted/RAM/sealed SFTP parity, reboot and installed update proof remain open. A compatible published
FileDO component and explicitly approved administrator-capable hosting are release prerequisites;
the ordinary LocalService host is not silently elevated. Ticket 114 tracks these remaining obligations.
