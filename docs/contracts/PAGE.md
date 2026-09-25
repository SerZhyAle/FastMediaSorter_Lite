# Pointer - `PAGE-CONTENT`, `PAGE-STYLE` and `SITE-FAMILY-MAP`

| | |
| --- | --- |
| **Ids** | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` |
| **Version** | 1.1 / 1.0 / 1.1, active (read 2026-09-24). Owner: the sza.od.ua hub |
| **Home** | `product-web-pages/` in the shared contracts catalog, with `reference/sza-kit.css` |
| **Role here** | consumer - the product site served from the repository root; audit in [specification 030](../specifications/030_SPECIFICATION_CONTRACT_SYNC_PRODUCT_WEB_PAGES.md) |

## What this repository must do to stay conformant

- `assets/sza-kit.css` is the catalog's reference stylesheet byte for byte; a local override goes in
  `assets/fms-site.css`, never in the kit.
- The footer's family grid lists every sibling at the URL `SITE-FAMILY-MAP` gives it, and the contact is
  the one the map names.
- The twelve `lang/<code>/index.html`, the trust pages, `robots.txt` and `sitemap.xml` are render targets
  of `tools/Build-SitePages.ps1`: every fix lands in the generator and its JSON, all thirteen locales in one
  edit, and `-Check` proves the committed pages match.
- The hand-authored root `index.html` receives the same shared blocks, so the root and the generated pages
  cannot drift.
