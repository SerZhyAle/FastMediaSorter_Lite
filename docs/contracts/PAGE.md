# Pointer - `PAGE-CONTENT`, `PAGE-STYLE` and `SITE-FAMILY-MAP`

| | |
| --- | --- |
| **Ids** | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` |
| **Version** | 1.2 / 1.2 / 1.2, active (read 2026-10-02). Owner: the sza.od.ua hub |
| **Home** | `product-web-pages/` in the shared contracts catalog, with `reference/sza-kit.css` |
| **Role here** | consumer - the product site served from the repository root; audit in `specification 030` (local specification) |

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

What 1.2 settled for this site:

- **The kit was re-vendored** (2026-10-02): PAGE-STYLE 1.2 revised the reference stylesheet (a 44 px
  coarse-pointer rule, `.demo` ink, a print block), so `assets/sza-kit.css` was copied from the catalog's
  reference again - byte for byte, as the first rule above requires.
- **`sza-lang` is exactly `ru`/`en`/`ua`.** The root page's switcher stores `ua`, maps a legacy stored
  `uk` to `ua` before paint, and offers RU/EN/UA - the registry row that said "the switcher says UK" was
  stale and is corrected. The twelve static locale pages keep their own language and never write
  `sza-lang` (1.2 section 4.2's secondary text row).
- **Open reading:** the generated `how-to-*.html`, `topics.html`, `glossary.html` and `server.html`
  collapse every non-Russian locale to English (`ru` or `en` only), so a stored `ua` shows English there.
  Whether 1.2's switcher rule reaches these child pages, or they are a documented exception, is an open
  question for the owner - carried in the registry row, not fixed silently.
