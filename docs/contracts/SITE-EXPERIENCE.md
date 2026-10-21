# Pointer - `SITE-EXPERIENCE`

| | |
| --- | --- |
| **Id** | `SITE-EXPERIENCE` |
| **Version** | 0.4, draft (read 2026-10-20, specification 244; written 2026-10-06 by FastMediaSorter Android, ticket S4096; 0.2 added rule 20 and 0.3 made the portal language picker conforming, both read 2026-10-07). 0.4 carries the 2026-10-17 amendment of the domain's README, whose additions are the Support page type and the rendering of a release history; no rule text of this contract changed between 0.3 and 0.4 |
| **Home** | `product-site/SITE-EXPERIENCE.md` and the run-list `product-site/SITE-CHECKLIST.md` in the shared contracts catalog |
| **Role here** | consumer, opted in by the owner on 2026-10-06 (specification 147, D1); guide tier. Rules 1, 4, 7, 8, 10, 13-19 are read; the portal-component rules (2, 3, 5, 6, 9, 11, 12) are not bound at this tier |

## What this repository must do

1. **Serve the kit as the catalog's file, first** (rule 1): `assets/sza-kit.css` is vendored and linked before
   `assets/fms-site.css` on every page. The vendored copy is the revision of 15661 bytes, SHA-256 `27501a10..`, that
   carries `PAGE-STYLE` 1.4 and 1.5 (vendored 2026-10-13; marked `-text`, so the working-tree bytes are LF); the
   catalog's current kit is a later revision (see the [PAGE pointer](PAGE.md)), so this copy is not the catalog's
   file until it is vendored again. `Build-SitePages.ps1 -Check` pins the old SHA-256.
2. **Keep layout out of `style` attributes** (rule 4) and the pre-paint theme resolver before the first
   stylesheet (rule 7). Both are open exceptions.
3. **Share language and theme through `sza-lang` (`ru`, `en`, `ua`) and `sza-theme` (`dark`, `light`)**
   (rule 10); the landing and the guides already do, and the locale lists of the generated language and trust
   pages write `sza-lang` for RU, EN or UA and never for a further locale. Their picker is not yet the form rule 10
   names (it shows an endonym, not the current code, and lists endonyms without codes): a dated exception.
4. **Take a version or a channel from the landing's run-time source** (rule 13): no page types a version.
5. **Declare and disclose every third-party origin** (rule 14): `fonts.googleapis.com`, `fonts.gstatic.com`
   and `api.github.com`, declared in `tools/site-origins.json` and named with purpose and data on the privacy page
   (en, ru, ua) and in every trust locale; `tools/Test-SiteOrigins.ps1` compares the pages with the declaration in
   both directions. Held since 2026-10-06 (specification 153).
6. **Keep the pages usable by keyboard, by landmark, in both themes, with a text equivalent for every image
   and under reduced motion** (rules 15-19): the skip link, the `nav` landmark, the light-theme contrast and
   the 44 px targets for every pointer are built (the kit's own for `.btn`, `.seg button` and `.theme-btn` since the
   2026-10-13 re-vendoring, the rest in `assets/fms-site.css`; measured 2026-10-13 in headless Chrome, fine pointer,
   on the root landing at 360, 768, 1280 and 1920 px and on `lang/de/` and `lang/ar/` at 360 and 1280: no header
   control, get-it button, footer-bottom link, tools-grid link or copy button under 44 px and no horizontal overflow;
   the footer's documentation and author rows of the generated pages were not in that run); the privacy page now has
   the kit, the resolver and the skip link too. What was not run: a screen reader, a coarse pointer, the deployed
   site and the 2560 px width.
7. **Fill the screen** (rule 20, 0.2): the kit carries `--wide: 100%` and `--gutter`, the page layer repeats
   `--wide` for the gate, and `tools/Test-SiteQuality.ps1` rule 8 fails a cap on a page, a header, a footer or a
   heading. Measured in Chrome 2026-10-07 on the landing and a generated page at 1428 px: the `main` box is
   viewport minus 10 px; and in headless Chrome 2026-10-13 at 360, 768, 1280 and 1920 px on the root landing (`main` is
   the viewport less the 10 px scrollbar each time, the header and hero start at the gutter); 2560 px was not measured.

## Where the evidence is

Specification 147 (`done/`): the measured values, the boxes not run, and the gate-or-manual table. Gaps are
exceptions in the catalog registry, one per ticket (152, 154, 158; 153 is closed). The browser run of 2026-10-06
(specification 156) held rules 1, 10, 13, 15, 16, 17 (text contrast; the coarse-pointer target lift read, not emulated), 18 (`alt` present, content not read) and 20 on the tree, with the privacy page as the exception (158 F-2); what it found and what it could not
run (reduced motion, the coarse pointer, the deployed site) is ticket 158.
