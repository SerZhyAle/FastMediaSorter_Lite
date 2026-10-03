# Pointer - `ICON-SET`, `ICON-RENDER` and `ICON-EXTERNAL`

| | |
| --- | --- |
| **Ids** | `ICON-SET`, `ICON-RENDER`, `ICON-EXTERNAL` |
| **Version** | catalog 0.17 / 0.15 / 0.11, all draft (re-imported 2026-10-03 with the Settings navigation glyphs; first aligned at 0.13 / 0.11 / 0.9 by [ticket 044](../specifications/done/044_SPECIFICATION_ICON_GLYPH_RESYNC.md)). **Vendored: 0.17 / 0.15 / 0.11** |
| **Home** | `iconography/README.md` in the shared contracts catalog |
| **Role here** | consumer - viewer, Share Manager and product site; [specification 027](../specifications/done/027_SPECIFICATION_CONTRACT_SYNC_ICONOGRAPHY.md) |

The product vendors only the catalog glyph files it renders, with their source SHA-256 recorded in
`assets/glyphs/PROVENANCE.txt`, and the vocabulary its tests read, under
`tests/Lite.Tests/TestData/icon-vocabulary/`; the contract text remains in the catalog.
`tools/Sync-IconGlyphs.ps1` is the repeatable import step, and `tools/Sync-IconGlyphs.ps1 -Check` is the
drift check: it compares the vendored files against the **catalog**, not against the local provenance
record, and exits 0 (in sync), 1 (drift) or 2 (catalog unreachable). The catalog root comes from
`-CatalogRoot` or the `FMS_CONTRACTS_ROOT` environment variable. The x86 fallback retains its existing
visual glyphs under the dated exception in the registry and no longer embeds the glyph files it never
rendered; the .NET 10 viewer and Share Manager consume the catalog paths.

On the site, a control drawn with a vocabulary glyph either references the vendored file
(`assets/glyphs/<id>.svg`) or carries an inline copy marked `data-glyph="<id>"` where it has to take
`currentColor` (back-to-top and the releases link on the root page). `IconographyContractTests` holds both
forms to the vendored files.

The registry row and the dated exception this pointer relied on were first written on 2026-09-24; until
then neither existed. The drift they recorded (four glyphs and the vocabulary behind `ICON-SET` 0.13, text
arrows for back-to-top and download on the root page) was closed the same day by ticket 044.

Deviations still open, each a dated exception in the registry and a proposal beside the contract (all
written 2026-09-24 by ticket 027): meanings the vocabulary lacks (`PROPOSAL-2026-09-24-fms-windows-meanings.md`),
Windows rendering constraints - pointer targets, the colour tray icon (`PROPOSAL-2026-09-24-fms-windows-rendering.md`),
names in the ten languages beyond en/ru/uk (`PROPOSAL-2026-09-24-fms-names-beyond-three-languages.md`), flags
as language markers (`PROPOSAL-2026-09-24-fms-flags-for-languages.md`), and the site kit's theme-switch
symbol `◐`, which has no vocabulary meaning yet (`PROPOSAL-2026-09-24-fms-page-style-kit-symbols.md`).
