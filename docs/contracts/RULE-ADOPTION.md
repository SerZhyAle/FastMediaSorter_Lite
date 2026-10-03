# Pointer - `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT` and `RULE-DELIVERY`

| | |
| --- | --- |
| **Ids** | `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT`, `RULE-DELIVERY` |
| **Version** | 0.11 / 0.10 / 0.10 / 0.11, draft (read 2026-10-02). Owner: sza-unified-rules |
| **Home** | `rule-adoption/README.md` in the shared contracts catalog |
| **Role here** | producer of the stamp (`.sza-canon.json`), consumer of the rest |

## What this repository must do to stay conformant

- `.sza-canon.json` stays valid and truthful: canon version and digest, overlay, ledger shape, role. The
  0.10/0.11 keys this repo owes are the optional truthfulness ones: this repo maintains a document
  registry (`docs/DOCUMENT_REGISTRY.jsonl` + its policy), so `docRegistryShape`/`docRegistryFile` should
  be declared beside `ledgerShape`, and the open `editions: []` question (the Server edition has its own
  winget package) should be settled - both ride the next `adopt-canon` run.
- **REPO-LAYOUT 0.10 rule 1:** every root agent-rules file carries the canon pointer or delegates
  wholesale to a sibling that does. `CLAUDE.md` carries the pointer; `AGENTS.md` does neither yet - a
  delegation line is owed (reported as `SZA-RULES02` by `check-compliance.ps1`).
- Re-sync with the canon when the stamp goes stale (`sza:adopt-canon`); the stamp is behind today (canon
  2026.09.06.1 against the live 2026.10.02.x) and the registry carries the dated exception. A re-sync at
  0.11 also writes `reconciledOn` beside the version and digest.
- No `.sza-profile.json` exists, so the profile defaults apply; enforcement comes from the `sza` plugin's
  hooks, never a second local copy of them.
