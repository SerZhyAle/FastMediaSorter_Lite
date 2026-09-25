# Pointer - `DOC-INTERNAL-QUALITY` and `DOC-EXTERNAL-QUALITY`

| | |
| --- | --- |
| **Ids** | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` |
| **Version** | 0.9 / 0.9, draft (read 2026-09-26). Owner: FastMediaSorter Android |
| **Home** | `documentation-quality/README.md` in the shared contracts catalog |
| **Role here** | consumer of both, adopted 2026-09-26 - the internal corpus (`CLAUDE.md`, `docs/guides/`, `docs/contracts/`, the READMEs, `CHANGELOG.md`) and the published site at the repository root (35 pages in `sitemap.xml`) |

## What this repository does to stay conformant

- **Held by a gate today:** the generated site pages, `robots.txt` and `sitemap.xml` against their JSON
  sources (`tools/Build-SitePages.ps1 -Check`, in the release preflight) and the glyph import
  (`tools/Sync-IconGlyphs.ps1 -Check`) - internal rule 4 in part, external rule 2 in part.
- **Held by measurement only (2026-09-26):** internal rules 6 and 7, external rule 7 - no broken image or
  anchor, no `http://`.
- **Not held, recorded as a dated exception in the catalog registry (until 2026-12-31):** internal rules
  1-5 in whole or part - ticket 058; external rules 1, 3-6 and the gate half of 2 and 7 - ticket 059.
  `docs/specifications/` is git-ignored by owner decision and is not part of the corpus.
