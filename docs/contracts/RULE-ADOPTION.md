# Pointer - `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT` and `RULE-DELIVERY`

| | |
| --- | --- |
| **Ids** | `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT`, `RULE-DELIVERY` |
| **Version** | 0.9, draft (read 2026-09-24). Owner: sza-unified-rules |
| **Home** | `rule-adoption/README.md` in the shared contracts catalog |
| **Role here** | producer of the stamp (`.sza-canon.json`), consumer of the rest |

## What this repository must do to stay conformant

- `.sza-canon.json` stays valid and truthful: canon version and digest, overlay, ledger shape, role.
- Re-sync with the canon when the stamp goes stale (`sza:adopt-canon`); the stamp is behind today and the
  registry carries the dated exception.
- No `.sza-profile.json` exists, so the profile defaults apply; enforcement comes from the `sza` plugin's
  hooks, never a second local copy of them.
