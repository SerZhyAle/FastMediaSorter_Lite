# Pointer - `SITE-EXPERIENCE`

| | |
| --- | --- |
| **Id** | `SITE-EXPERIENCE` |
| **Version** | 0.3, draft (written 2026-10-06 by FastMediaSorter Android, ticket S4096; 0.2 added rule 20 and 0.3 made the portal language picker conforming, both read 2026-10-07) |
| **Home** | `product-site/SITE-EXPERIENCE.md` and the run-list `product-site/SITE-CHECKLIST.md` in the shared contracts catalog |
| **Role here** | consumer, opted in by the owner on 2026-10-06 (specification 147, D1); guide tier. Rules 1, 4, 7, 8, 10, 13-19 are read; the portal-component rules (2, 3, 5, 6, 9, 11, 12) are not bound at this tier |

## What this repository must do

1. **Serve the kit as the catalog's file, first** (rule 1): `assets/sza-kit.css` is vendored and linked before
   `assets/fms-site.css` on every page. It is the catalog's file byte for byte (the `PAGE-STYLE` 1.3 revision,
   vendored again 2026-10-07; marked `-text`, so the working-tree bytes are LF); `Build-SitePages.ps1 -Check` pins
   its SHA-256. The revision that `PAGE-STYLE` 1.4 and 1.5 owe is not in the catalog yet.
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
   the 44 px targets for every pointer are built (the last in `assets/fms-site.css`, 2026-10-07; measured in Chrome
   on the landing, fine pointer); the privacy page now has the kit, the resolver and the skip link too. What was not
   run: a screen reader, the deployed site and the 360 px width.
7. **Fill the screen** (rule 20, 0.2): the kit carries `--wide: 100%` and `--gutter`, the page layer repeats
   `--wide` for the gate, and `tools/Test-SiteQuality.ps1` rule 8 fails a cap on a page, a header, a footer or a
   heading. Measured in Chrome 2026-10-07 on the landing and a generated page at 1428 px: the `main` box is
   viewport minus 10 px; 1920 and 2560 px were not re-measured (the window would not resize).

## Where the evidence is

Specification 147 (`done/`): the measured values, the boxes not run, and the gate-or-manual table. Gaps are
exceptions in the catalog registry, one per ticket (152, 154, 158; 153 is closed). The browser run of 2026-10-06
(specification 156) held rules 1, 10, 13, 15, 16, 17 (text contrast; the coarse-pointer target lift read, not emulated), 18 (`alt` present, content not read) and 20 on the tree, with the privacy page as the exception (158 F-2); what it found and what it could not
run (reduced motion, the coarse pointer, the deployed site) is ticket 158.
