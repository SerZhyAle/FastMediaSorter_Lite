# Pointer - `PAGE-CONTENT`, `PAGE-STYLE` and `SITE-FAMILY-MAP`

| | |
| --- | --- |
| **Ids** | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` |
| **Version** | 1.4 / 1.6 / 1.3, active (read 2026-10-07). Owner: the sza.od.ua hub |
| **Home** | `product-web-pages/` in the shared contracts catalog, with `reference/sza-kit.css` |
| **Role here** | consumer - the product site served from the repository root; audit in `specification 030` and `specification 181` (local specifications) |

## What this repository must do to stay conformant

- `assets/sza-kit.css` is the catalog's reference stylesheet byte for byte - the revision of 15661 bytes, SHA-256
  `27501a10..`, that the catalog published on 2026-10-07 with `PAGE-STYLE` 1.4 and 1.5 (vendored 2026-10-13; it was the
  1.3 file, 14521 bytes, `aea958f8..`, until then) - and `.gitattributes` marks it `-text`.
  `tools/Build-SitePages.ps1 -Check` pins its SHA-256 (`Kit identity`). When the catalog moves the file again, vendor it
  and move the pin. A local override goes in `assets/fms-site.css`, never in the kit.
- The kit now carries what 1.4 and 1.5 say it will: 44 px for `.btn`, `.seg button` and `.theme-btn` for every pointer,
  the `nav.expand` marker of `details.sec`, the labelled form of the theme control, and reduced motion that reaches
  `::before` and `::after`. The page layer's copies of the 44 px and reduced-motion rules were dropped with the
  re-vendoring (2026-10-13); what stays in `assets/fms-site.css` is the 44 px of what the kit does not size - the page's
  own links and summary, `.tools-grid a` and `.copybox .copy` for a fine pointer (ticket 158 T-4).
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

Built on the tree 2026-10-13 (ticket 183 remainder, 13 locales; the registry rows are the owner's to close): the
identity block is the kit's (`.identity`, `.product-icon`, `.product-name`, then one outcome `h1`), the header carries
the documentation link (`.nav-doc`, to `topics.html`, a text link and not a `.btn` because the kit hides a header `.btn`
under 480 px), and the get-it block lists the Server edition beside the Store. The author's name in the landing footer
is a link since 2026-10-07.

The dated exceptions that remain: the full name against the map's `Tool` cell and the header brand that repeats the
full name where the contract has a short name as text, the form of the language picker (4.2 and 1.6), and the theme
control, which still carries the bare glyph (the kit now styles the labelled form of 4.3 and `nav.expand` is drawn by
the kit, but the markup of every page has not adopted the visible word yet).
