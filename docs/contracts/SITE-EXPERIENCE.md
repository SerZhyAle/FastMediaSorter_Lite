# Pointer - `SITE-EXPERIENCE`

| | |
| --- | --- |
| **Id** | `SITE-EXPERIENCE` |
| **Version** | 0.1, draft (written 2026-10-06 by FastMediaSorter Android, ticket S4096) |
| **Home** | `product-site/SITE-EXPERIENCE.md` and the run-list `product-site/SITE-CHECKLIST.md` in the shared contracts catalog |
| **Role here** | consumer, opted in by the owner on 2026-10-06 (specification 147, D1); guide tier. Rules 1, 4, 7, 8, 10, 13-19 are read; the portal-component rules (2, 3, 5, 6, 9, 11, 12) are not bound at this tier |

## What this repository must do

1. **Serve the kit as the catalog's file, first** (rule 1): `assets/sza-kit.css` is vendored and linked before
   `assets/fms-site.css` on every page. Its text after normalizing line endings equals the catalog copy; the
   working-tree bytes are CRLF.
2. **Keep layout out of `style` attributes** (rule 4) and the pre-paint theme resolver before the first
   stylesheet (rule 7). Both are open exceptions.
3. **Share language and theme through `sza-lang` (`ru`, `en`, `ua`) and `sza-theme` (`dark`, `light`)**
   (rule 10); the landing and the guides already do.
4. **Take a version or a channel from the landing's run-time source** (rule 13): no page types a version.
5. **Declare and disclose every third-party origin** (rule 14): `fonts.googleapis.com`, `fonts.gstatic.com`
   and `api.github.com`, declared in `tools/site-origins.json` and named with purpose and data on the privacy page
   (en, ru, ua) and in every trust locale; `tools/Test-SiteOrigins.ps1` compares the pages with the declaration in
   both directions. Held since 2026-10-06 (specification 153).
6. **Keep the pages usable by keyboard, by landmark, in both themes, with a text equivalent for every image
   and under reduced motion** (rules 15-19): the skip link, the `nav` landmark, the light-theme contrast and
   the touch targets are open exceptions.

## Where the evidence is

Specification 147 (`done/`): the measured values, the boxes not run, and the gate-or-manual table. Gaps are
exceptions in the catalog registry, one per ticket (152, 154, 156; 153 is closed).
