# Pointer - `DOC-INTERNAL-QUALITY` and `DOC-EXTERNAL-QUALITY`

| | |
| --- | --- |
| **Ids** | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` |
| **Version** | 0.9 / 0.10, draft (read 2026-10-02 and again 2026-10-07; both are the catalog's current versions - `DOC-INTERNAL-QUALITY` stayed 0.9 when the external one went to 0.10, and the log row of 2026-10-06 is a companion note that moved no version). The 0.10 additions only widen what the gates already hold: the search-index threshold is met by holding the index against the sitemap page set (item B); the per-locale hreflang check and the `x-default` check are implemented (item D); the length limits count Unicode code points after entity decoding, exactly, since 2026-10-07 (item C; before it the count was UTF-16 units, the same for every character the thirteen locales use); item A holds by the guides' task sections and item E applies to no page of this site. Owner: FastMediaSorter Android |
| **Home** | `documentation-quality/README.md` in the shared contracts catalog |
| **Role here** | consumer of both, adopted 2026-09-26 - the internal corpus (`CLAUDE.md`, `docs/guides/`, `docs/contracts/`, the READMEs, `CHANGELOG.md`) and the published site at the repository root (37 pages in `sitemap.xml`) |

## What this repository does to stay conformant

- **Internal rules 1-3, 5-7:** [the document registry](../DOCUMENT_REGISTRY.jsonl) and
  [its policy](../document-registry-policy.json) are checked by
  [Test-DocRegistry.ps1](../../tools/Test-DocRegistry.ps1), an `agent-closure` check in
  `Run-AllTests.ps1`. It checks coverage in both directions, relative links and heading anchors,
  tracked image assets, prose style and offline safety. Local specification links are forbidden;
  cite the ticket or filename in plain text. `docs/specifications/` remains outside the corpus.
- **Internal rule 4:** the former canon mirrors are pointers. Settings page counts and the viewer
  warning-list parity claim in `CLAUDE.md` are checked against code by the registry gate; the warning
  list itself is read from the project files. Generated site pages, `robots.txt`, `sitemap.xml`
  (`Build-SitePages.ps1 -Check`) and glyph imports (`Sync-IconGlyphs.ps1 -Check`) stay in the release
  preflight. Render records name their generator; rendered HTML belongs to the published-site gate.
- **Gate verification:** [DocRegistry.ps1](../../tests/Integration/DocRegistry.ps1) plants missing
  declarations, targets, anchors and assets, style defects, unsafe links and code-fact drift in a
  temporary repository, and checks failure verdicts and missing-input verdicts.
- **External rules 1-7 (ticket 059, gated 2026-10-01):** [Test-SiteQuality.ps1](../../tools/Test-SiteQuality.ps1)
  holds every rule over the page set the sitemap announces - it runs in `Run-AllTests.ps1` and in the
  release preflight beside `Build-SitePages.ps1 -Check`. Rule 1: [topics.html](../../topics.html) links
  every root page and [glossary.html](../../glossary.html) defines every termbase term, both directions.
  Rule 2: `search-index.json` is rendered with the sitemap over the same page set. Rule 3: every locale's
  freshness record in [translation-fingerprints.json](../../tools/translation-fingerprints.json) must
  match the English it was translated from; a stale locale is named, and `-Record` re-stamps after a
  translation fan-out. Rule 4: [site-termbase.json](../../tools/site-termbase.json) pins approved forms
  and forbidden variants for 13 locales. Rule 5: the full SEO block - title, description, canonical,
  Open Graph, Twitter, JSON-LD, `hreflang`/`x-default` - with the 60/160 length limits. Rule 6: a guide
  declaring `data-ui-steps` carries its `assets/guides/` screenshots with alt text and true pixel sizes.
  Rule 7: every link, anchor and image resolves, and references are https-only.
- **Capture tool:** [Capture-GuideScreens.ps1](../../tools/Capture-GuideScreens.ps1) is the hand-run
  regenerator of the guide screenshots (viewer + Share Manager driven by posted keys and UI
  Automation, no focus taken; the interface language is borrowed to English and the destination rows
  to sample paths, both restored).

The gate does not infer Russian diacritics or the language of source comments. Those remain review
obligations under internal rule 5. HTTPS references are checked syntactically, without network access.

## Verification on 2026-10-01 (ticket 058)

- `pwsh -NoProfile -File tools/Test-DocRegistry.ps1`: PASS (90 documents, 329 links,
  source facts agree).
- `pwsh -NoProfile -File tests/Integration/DocRegistry.ps1`: PASS (20 behavior fixtures).
- `pwsh -NoProfile -File tools/Build-SitePages.ps1 -Check`: PASS (27 render targets).
- `pwsh -NoProfile -File tools/Run-AllTests.ps1`: FAIL in the current shared working tree.
  Final rerun: viewer net48 has five failures (515 passed), net10 has six (1317 passed), in
  `LifecycleAuditFollowupTests` and `WaveParticlesSessionTests`; Share Manager passes (228 passed,
  one skipped); the Go worker passes (five packages); placement and documentation both pass.
  Verdict: `Run-AllTests: FAIL (failed: viewer, 4 passed)`.
- `pwsh -NoProfile -File tools/Sync-IconGlyphs.ps1 -Check`: FAIL (four provenance/vocabulary drift
  findings against the current catalog).

The implementation is present; the catalog exception remains open for full-run acceptance and
existing glyph drift. No release or full-corpus acceptance is claimed by a focused PASS.

## Verification on 2026-10-03 (ticket 059, external; re-verified after the 10-01 page set was recreated)

- `pwsh -NoProfile -File tools/Test-SiteQuality.ps1`: PASS (37 pages, 1817 links, 35 images,
  13 locales, rules 1-7 held). Planted defects each failed with a named finding: a stale
  freshness fingerprint (three locales named), a 66-char title, a false `hreflang`, a missing
  target, an `http://` link, a missing anchor, and a search-index title drift.
- `pwsh -NoProfile -File tools/Build-SitePages.ps1 -Check`: PASS (29 render targets and
  11 conformance checks passed).
- `pwsh -NoProfile -File tools/Test-CheckPlacement.ps1`: PASS (24 records, 4 runners,
  16 verdict scripts; per-change runner: none (declared)).
- Rule 6's screenshots were captured the same date from the built viewer
  (`tools/Capture-GuideScreens.ps1`): `settings-files`, `settings-ocr`, `settings-sftp`,
  `ocr-overlay`, `viewer-main` under `assets/guides/`, embedded in `how-to-setup`,
  `how-to-ocr` and `how-to-sftp`. The Share Manager's main window was deliberately left out
  of the corpus: it shows this machine's real shared folders, LAN address and port, and
  blanking it would mean reconfiguring the live share service.
- The external dated exception in the catalog registry (until 2026-12-31) is closed with the
  command output above; rules 2, 3, 5 and 7 are gated in the release preflight.

## Verification on 2026-10-07 (specification 181, after the kit re-vendor, the copy-button words and the sibling label)

- `pwsh -NoProfile -File tools/Test-SiteQuality.ps1`: PASS (39 pages, 2344 links, 36 images, 13 locales, rules 1-7,
  the guides' interface words, the full-width rule and the keyboard-and-landmarks rule held), exit 0; the title and
  description limits now count code points.
- `pwsh -NoProfile -File tools/Build-SitePages.ps1 -Check`: PASS (30 render targets and 14 conformance checks
  passed), exit 0, the kit pinned to the catalog's current file.
- `pwsh -NoProfile -File tools/Test-DocRegistry.ps1`: PASS (111 documents, 401 links, source facts agree), exit 0 before
  the first edit of the day, and PASS (112 documents, 405 links) after the pointer edits. One run in between reported
  a defect that was not this ticket's: a `docs/contracts/` pointer written by a parallel contract-sync auditor and
  not yet declared in `docs/DOCUMENT_REGISTRY.jsonl`.
- Not run: `tools/Run-AllTests.ps1` (other sessions were building and editing the shared tree), so the full-run
  acceptance recorded for ticket 058 is neither confirmed nor withdrawn.
