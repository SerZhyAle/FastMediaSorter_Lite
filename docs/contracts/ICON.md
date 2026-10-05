# Pointer - `ICON-SET`, `ICON-RENDER` and `ICON-EXTERNAL`

| | |
| --- | --- |
| **Ids** | `ICON-SET`, `ICON-RENDER`, `ICON-EXTERNAL` |
| **Version** | catalog 0.17 / 0.15 / 0.11, all draft (read 2026-10-02; vendored files re-imported 2026-10-03 with the Settings navigation glyphs). **Implemented: 0.17 / 0.15 / 0.11** - the vendored vocabulary and glyphs were already taken at 0.17 (PROVENANCE, 2026-10-02; re-taken 2026-10-03); the hit-target floor and the warning tone were adopted 2026-10-02 |
| **Home** | `iconography/README.md` in the shared contracts catalog |
| **Role here** | consumer - viewer, Share Manager and product site; `specification 027` (local specification) |

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

What 0.14-0.17 added for this product:

- **`status.warning` is active**, and this product's secure-container placeholder is its first shipper;
  the day tone `#EF6C00` (3.08:1 on white) is section 10 item D and is now what `ThemePalette` returns.
- **Rule 5 hit targets (0.15):** at least 28 logical px for a mouse- or pen-driven control, at least 44 px
  under coarse input. The viewer is DPI-unaware (`DpiUnaware`), so the numbers are logical pixels - the
  registry row says so. The floors the product had below 28 (`ChromeMetrics.MinTarget`, the toolbar row
  minimum) were raised 2026-10-02. The 44 px coarse-input floor is a separate open question: the product
  has no touch-specific surfaces, and whether to size everything to 44 (the rule's default when input
  kind cannot be told) is an owner decision carried in the registry exception.
- **Rule 6 (0.11, `ICON-EXTERNAL`):** a language is marked by its endonym, never by a flag. The dated
  exceptions recorded before the rule was decided (this product's flags in three menus, until
  2026-12-31) stay until their own dates - now as deviations from a decided rule; the owner decides
  remove-early versus keep-to-date. **Decided 2026-10-04 (owner: "everything by the contracts"): removed
  early on the .NET 10 build** - the interface-language menus and the OCR source/target lists show the
  endonym only (`LanguageSelectorContractTests`). The x86 fallback keeps its flags. The catalog
  registry row for the exception is the owner's to close; nothing in the catalog was edited from here.

Deviation still open, each a dated exception in the registry and a proposal beside the contract: meanings
the vocabulary lacked were folded into 0.15/0.17 (`app.theme`, `action.reveal`, `action.show-code`,
`action.choose-file`, `action.choose-folder` and the rest of
`PROPOSAL-2026-09-24-fms-windows-meanings.md`), the Windows rendering constraints were folded into 0.12-0.15
(`PROPOSAL-2026-09-24-fms-windows-rendering.md`), names beyond en/ru/uk were partly folded (0.14/0.17,
`PROPOSAL-2026-09-24-fms-names-beyond-three-languages.md`), flags-for-languages became rule 6 as option A
(`PROPOSAL-2026-09-24-fms-flags-for-languages.md`), and the site kit's theme-switch symbol `◐` remains the
one item of `PROPOSAL-2026-09-24-fms-page-style-kit-symbols.md` not folded on this folder's side. Since
2026-10-05 (specification 128) the settings navigation list also draws each page's glyph in the page's own
identity ink - the one deliberate departure from rule 2's role colour, carried by the dated registry
exception and `iconography/PROPOSAL-2026-10-05-fms-windows-nav-identity-colors.md` until the owner decides;
the ink table (`src/SettingsNavIconPalette.vb`) is gated to 3 : 1 against the sidebar and pairwise
distinct dark inks, and every other glyph surface stays mono.
