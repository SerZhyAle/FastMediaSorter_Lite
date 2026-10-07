# Pointer - `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT` and `RULE-DELIVERY`

| | |
| --- | --- |
| **Ids** | `REPO-STAMP`, `HARNESS-PROFILE`, `REPO-LAYOUT`, `RULE-DELIVERY` |
| **Version** | `REPO-STAMP` 0.12, `HARNESS-PROFILE` 0.11, `REPO-LAYOUT` 0.11, `RULE-DELIVERY` 0.12, all draft (read 2026-10-07). `REPO-STAMP` and `RULE-DELIVERY` 0.12 are `breaking` for a **reader**, not for a stamp. Owner: sza-unified-rules |
| **Home** | `rule-adoption/README.md` in the shared contracts catalog |
| **Role here** | producer of the stamp (`.sza-canon.json`, written by the adoption run, never by hand), consumer of the other three. `HARNESS-PROFILE` is **not bound** here - declared below |

## What this repository must do to stay conformant

- **`REPO-STAMP`.** `.sza-canon.json` stays valid, truthful and written only by an adoption run (rule 8: a
  version-only re-stamp is a hand write too). It carries both dates: `adoptedOn` is the first adoption and no
  re-sync moves it; `reconciledOn` moves with `version` and `coreDigest` as one write. The 0.12 reading of an
  absent or unparseable date (**unknown age is the strictest age, past the 180-day window**) therefore never
  applies to this stamp - it tightens the canon's reader, which has to move, and changes nothing here. The
  optional keys the stamp declares (`docRegistryShape`/`docRegistryFile`, `editions: ["server"]`,
  `ledgerShape: 1` with no `ledgerFile`, `channels`, `site`, `privacy`) were all settled by the 2026-10-03
  re-sync.
- **`RULE-DELIVERY`.** Being behind is the normal state between reconciliations; being behind silently is the
  fault. The stamp declares canon `2026.10.02.3`, the canon has since moved (`2026.10.07.2`), so the digests
  differ: today that is the **warning** rung (`SZA-CANON03`, aged from `reconciledOn` 2026-10-03), not the
  error rung. The owed step is one `sza:adopt-canon` re-sync that re-reads the changed rule documents; the
  version pair is never typed by hand.
- **`REPO-LAYOUT` rule 1.** Every root agent-rules file carries the canon pointer or delegates wholesale to a
  sibling that does: `CLAUDE.md` carries it, `AGENTS.md` has its own "Canon pointer" section.
- **`REPO-LAYOUT` rule 2.** `docs/contracts/<ID>.md` holds pointers, never copies; a shared file names every
  id inside it (`FDSEC.md`, `AUTOMATED-CHECKS.md`, this file).
- **`REPO-LAYOUT` rule 3.** Specifications are `NNN_SPECIFICATION_*`, a scheme declared in `CLAUDE.md` (the
  number is a queue label), so they sit under the declared-scheme exception. The 0.11 third exception - the
  release package files of the canon's `RELEASE_AND_DISTRIBUTION` section 8 - is **not claimed**: this
  repository records that plan as skipped in `CLAUDE.md`. `docs/specifications/RELEASE_QUEUE.md` is a
  dependency-ordered work queue, not that file, and has no type prefix; it is a dated registry exception
  until it is renamed (`PLAN_` prefix) or declared. No gate checks the prefix.
- **`HARNESS-PROFILE` - not bound (rule 7).** There is no `.sza-profile.json` and the shipped harness is not
  run here: no `PLAN/` or `dev/` tree exists, and the tickets live in `docs/specifications/` under the
  scheme above, where the harness defaults do not look. The 0.11 derived `paths.changelog` is moot for the
  same reason (the stamp declares `ledgerShape` 1, which the contract leaves undecided; the repository's
  ledger is the root `CHANGELOG.md`). Before the harness is ever run here, a profile must be written first.
- Enforcement comes from the `sza` plugin's hooks, never a second local copy of them.
