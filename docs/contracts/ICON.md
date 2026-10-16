# Pointer - `ICON-SET`, `ICON-RENDER` and `ICON-EXTERNAL`

| | |
| --- | --- |
| **Ids** | `ICON-SET`, `ICON-RENDER`, `ICON-EXTERNAL` |
| **Version** | catalog 0.33 / 0.19 / 0.12, all draft (read 2026-10-16, specification 235). **Implemented: partial 0.33 / partial 0.19 / 0.12** - the vendored vocabulary and the 105 catalog glyphs this product renders (none product-drawn: `action.replace` is the catalog's since 0.31) were re-imported at 0.33 on 2026-10-16 (`assets/glyphs/PROVENANCE.txt`, `tools/Sync-IconGlyphs.ps1 -Check` exit 0); the set stays partial under the dated exceptions in the registry. Since 0.30 the catalog added 14 records (0.31: seven, 0.32: the six exchange meanings of `DEVICE-EXCHANGE` item CW, 0.33: `action.retry`), changed no shipped glyph and turned `action.email` active; `ICON-RENDER` and `ICON-EXTERNAL` did not move |
| **Home** | `iconography/README.md` in the shared contracts catalog |
| **Role here** | consumer - viewer, Share Manager and product site; `specification 027` (local specification) |

The product vendors only the catalog glyph files it renders, with their source SHA-256 recorded in
`assets/glyphs/PROVENANCE.txt`, and the vocabulary its tests read, under
`tests/Lite.Tests/TestData/icon-vocabulary/`; the contract text remains in the catalog.
`tools/Sync-IconGlyphs.ps1` is the repeatable import step (its list of ids is the product's declared subset: a
meaning that gets a control is added there in the same edit), and `tools/Sync-IconGlyphs.ps1 -Check` is the
drift check: it compares the vendored files against the **catalog**, not against the local provenance
record, and exits 0 (in sync), 1 (drift) or 2 (catalog unreachable). The catalog root comes from
`-CatalogRoot` or the `FMS_CONTRACTS_ROOT` environment variable. The x86 fallback retains its existing
visual glyphs under the dated exception in the registry and does not embed the glyph files; the .NET 10
viewer and Share Manager consume the catalog paths (`src/Icons/`, whole-file modern seam).

On the site, a control drawn with a vocabulary glyph either references the vendored file
(`assets/glyphs/<id>.svg`) or carries an inline copy marked `data-glyph="<id>"` where it has to take
`currentColor` (back-to-top and the releases link on the root page, the brush on the editor how-to).
`IconographyContractTests` holds both forms to the vendored files, so a re-import that redraws a glyph
fails until its inline copies follow.

## What 0.18-0.28 asked of this product (read 2026-10-07; re-read 2026-10-16 where marked)

- **`action.convert-to-video` (0.21)** is the picture of the animation-to-video button: `IconMap` row
  `btn_ToVideo` now names it, the glyph is vendored, and the chrome draws it instead of an icon-font code
  point. The button's words follow the record's canonical name since 2026-10-07 (specification 183): the
  localization key is «Преобразовать в видео», English "Convert to video", Ukrainian «Перетворити на відео»
  (`IconographyContractTests` holds all three to the vendored record), the same wording is the toolbar caption, the
  accessible name, the overflow line and the middle-click menu row; the other ten languages are this product's own
  wording until a proposal names them (rule 3). `app.language` (0.21), `view.table`
  (0.25) and `feature.drawing` (0.28) exist as records with drawings, but no control of this product draws
  them (the language is picked from a list of endonyms, there is no table view, the editor is entered with
  `action.edit`), so they are neither mapped nor vendored; the day a control needs one it takes a row in
  `IconMap` and an id in the sync script.
- **`tool.brush` was split (0.28, a MAJOR's cost in a draft):** the vendored brush is now the Material brush,
  never the pencil of `action.edit`; the editor's Brush tool uses it and `IconographyContractTests` fails if
  the two drawings coincide. Rule 6 (re-counted 2026-10-16 from the vendored vocabulary): **21 of the 105 vendored
  ids are `proposed` in 0.33**, among them `action.convert-to-video`, `action.choose-file`, `action.choose-folder`
  and `content.disk-container`; none has reached a released version yet (the last release has no
  `assets/glyphs`), so release 46 is this product's first shipment of every one of them and the "first product
  to ship it" report is owed with it (239 CR-20).
- **`ICON-RENDER` 0.17 / 0.18:** a Windows product that declares itself pointer-first may size to the 28 px floor
  without detecting touch. This product has not made that declaration - it is the owner's - so the coarse-input
  question stays an open exception. The web floor (28 CSS px fine pointer, 44 coarse) is the site's. Rule 7 (0.18)
  keeps `media.previous` / `media.next` fixed except for the pages and chapters of a document; the viewer mirrors
  its previous / next **file** buttons in a right-to-left layout, which the text neither allows nor forbids -
  recorded as an exception and put to the owner as a proposal.
- **`ICON-EXTERNAL` 0.12:** rule 3 (a document's own images) has no subject here. Rule 6 (endonyms, never flags)
  holds on the .NET 10 build since 2026-10-04 (`LanguageSelectorContractTests`); the x86 fallback keeps its flags
  under the maintenance policy.

**What the product reads of the catalog.** Its tests read only the `id` of each vendored record and its renderer
reads the SVG subset of `ICON-RENDER` rule 1 (`src/Icons/SvgPathData.vb`, `GlyphRenderer.vb`; a malformed file throws
to the caller, a missing record is a test failure, never a blank glyph). It reads no `colour`, `rtl` or `status`
field, so the section 5 defaults for an unknown value do not arise. **`vocabulary.version` is read** (since
2026-10-16, `e5eeb24`): `tools/Sync-IconGlyphs.ps1` refuses a catalog at a higher MAJOR than 0 with exit 2 in both
the `-Check` and the import mode, accepts a higher MINOR and never guesses a malformed or missing carrier
(`tests/Integration/IconCatalogCarrier.ps1`, run by hand; the earlier dated exception is closed in the registry).

## Adoption still open (read 2026-10-16)

Two catalog records exist where the product still draws the nearest meaning: "Skip" draws `media.next`
(`action.skip`, 0.31) and "Create desktop shortcuts" draws `action.add` (`action.create-shortcut`, 0.31). The
`feature.server-relay` record is vendored and drawn by no control. Not checked: the inline `data-glyph` copies on
the site pages against 0.33, and the names of the 0.31-0.33 records in the ten further languages. All four are
in the registry's dated exception on `ICON-SET` and in 239 CR-20.

## Deviations still open

Each is a dated exception in the catalog registry. Meanings with no usable catalog picture yet - the random
slideshow and the two filled shapes wait for variants the catalog still owes, the Share Manager's password eye
for adopting `action.reveal` - keep their pre-contract pictures and are listed, with their control names, in
`IconMap`; a test pins that list. The settings navigation's per-page identity inks are no longer a deviation
(`ICON-RENDER` 0.18 section 11, the ink table `src/SettingsNavIconPalette.vb` with its 3 : 1 gates). The site
theme switch still draws the kit's `◐` where `app.theme` is the decided glyph. The x86 fallback keeps its Unicode
toolbar and its flags.
