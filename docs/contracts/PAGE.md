# Pointer - `PAGE-CONTENT`, `PAGE-STYLE` and `SITE-FAMILY-MAP`

| | |
| --- | --- |
| **Ids** | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` |
| **Version** | 1.4 / 1.6 / 1.3, active (read 2026-10-07). Owner: the sza.od.ua hub |
| **Home** | `product-web-pages/` in the shared contracts catalog, with `reference/sza-kit.css` |
| **Role here** | consumer - the product site served from the repository root; audit in `specification 030` and `specification 181` (local specifications) |

## What this repository must do to stay conformant

- `assets/sza-kit.css` is the catalog's reference stylesheet byte for byte - the revision of 14521 bytes that
  `PAGE-STYLE` 1.3 made (vendored again 2026-10-07; it was the 1.2 file until then) - and `.gitattributes` marks it
  `-text`. `tools/Build-SitePages.ps1 -Check` pins its SHA-256. The revision `PAGE-STYLE` 1.4 and 1.5 owe has **not
  landed** in the catalog (the file there is still the 1.3 one): when it does, vendor it and move the pin. A local
  override goes in `assets/fms-site.css`, never in the kit.
- Until that revision, the page layer carries what 1.4 and 1.5 say the kit will: 44 px for `.btn`, `.seg button`,
  `.theme-btn` and the other chrome for every pointer (ticket 158 T-4), and reduced motion that reaches `::before` and
  `::after`. They are page-layer rules in `assets/fms-site.css`, dropped once the kit carries them.
- The footer's family grid lists every sibling at the URL `SITE-FAMILY-MAP` gives it, under the `Tool` label the map
  gives it (the Android row reads *Fast Media Sorter & Organizer* since 1.3), and the contact is the one the map names.
  The grid comes from `tools/site-copy.json` (`_family`), never from a page.
- The twelve `lang/<code>/index.html`, the trust pages, `robots.txt` and `sitemap.xml` are render targets
  of `tools/Build-SitePages.ps1`: every fix lands in the generator and its JSON, all thirteen locales in one
  edit, and `-Check` proves the committed pages match.
- The hand-authored root `index.html` receives the same shared blocks, so the root and the generated pages
  cannot drift.
- The copy button's done state is the localized word for "Copied", with no check mark (1.4): `CopyWords` in the
  generator, `COPY` in the root page.

What 1.2 settled for this site:

- **`sza-lang` is exactly `ru`/`en`/`ua`.** The root page's switcher stores `ua`, maps a legacy stored
  `uk` to `ua` before paint, and offers RU/EN/UA. Choosing RU, EN or UA from the locale list of a generated
  page or a trust page writes `sza-lang` before navigating, and a further locale never does (4.2, 1.6; written
  2026-10-07, the script is `LocaleListScript` in the generator).
- **Open reading:** the generated `how-to-*.html`, `topics.html`, `glossary.html` and `server.html`
  collapse every non-Russian locale to English (`ru` or `en` only), so a stored `ua` shows English there.
  Whether 1.2's switcher rule reaches these child pages, or they are a documented exception, is an open
  question for the owner - carried in the registry row, not fixed silently.

## Width

Closed by `PAGE-STYLE` 1.3 (the owner's ruling of 2026-10-06): the kit's `--wide` is `100%` and its `--gutter` is
the side padding. This site's layer no longer redefines `--gutter`; it repeats `--wide: 100%` only so that
`tools/Test-SiteQuality.ps1` rule 8, which fails any page, template or stylesheet that caps a page, header, footer,
heading or text width, keeps reading the layer.

## Not conformant yet

The dated exceptions in the catalog's adoption section name them: the identity block (the name is the page's `h1`
and the kit's `.identity` classes are unused), the full name against the map's `Tool` cell, the documentation link
in the header, the Server edition in the get-it block, the form of the language picker, and the unlabelled theme
control (waits for the kit revision). The author's name in the landing footer is a link since 2026-10-07.
