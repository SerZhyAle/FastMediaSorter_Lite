# Pointer - `PAGE-CONTENT`, `PAGE-STYLE` and `SITE-FAMILY-MAP`

| | |
| --- | --- |
| **Ids** | `PAGE-CONTENT`, `PAGE-STYLE`, `SITE-FAMILY-MAP` |
| **Version** | read on 2026-11-15 (L0323 catch-up) at `PAGE-CONTENT` 1.5, `PAGE-STYLE` 3.10 and `SITE-FAMILY-MAP` 1.12, active, the current catalog versions; the text below was written against 1.4 / 1.6 / 1.3 (2026-10-07) and read at 1.5 / 3.1 / 1.5 on 2026-10-20 (specification 244). `PAGE-CONTENT` partial: the header brand repeats the full name by the owner's choice of 2026-10-30, the hero action labels were not re-read. `PAGE-STYLE` partial: the vendored `assets/sza-kit.css` is the 3.3 file (30629 bytes) and the catalog reference is the 3.8 file (30698 bytes), `assets/fms-buttons.css` is kept beside it, the header brand is not aligned by logical properties (ar, ur) and `.pill` text is 4.07:1 on the light page (ticket L0331). `SITE-FAMILY-MAP` conforms; the help index address named by `CONTEXT-HELP` 0.4 item F is open (ticket L0327). Dated 2026-11-15 in the registry. `PAGE-STYLE` moved through 2.0 and 3.0 (breaking) and 2.1 and 3.1 (additive) on 2026-10-17, see "What `PAGE-STYLE` 2.0 to 3.1 ask of this site". Owner: the sza.od.ua hub |
| **Home** | `product-web-pages/` in the shared contracts catalog, with `reference/sza-kit.css` |
| **Role here** | consumer - the product site served from a Pages artifact of the files declared in `tools/site-served.json`; audit in `specification 030` and `specification 181` (local specifications) |

## What this repository must do to stay conformant

- `assets/sza-kit.css` is a vendored copy of the catalog's reference stylesheet, and `.gitattributes` marks it `-text`.
  Read 2026-11-15: the vendored copy is the 3.3 file (30629 bytes, `4ABA1DEE..`), pinned at
  `tools/Build-SitePages.ps1:2019`, while the catalog reference is now 30698 bytes (`D490ABF3..`, 3.8): re-vendor it
  and move the pin together (ticket L0331). The 2026-10-20 copy named here earlier (15926 bytes, `e438fef2..`, 3.1) is
  history. `tools/Build-SitePages.ps1 -Check` pins the SHA-256 (`Kit identity`): when the
  catalog moves the file, vendor it and move the pin together. A local override goes in `assets/fms-site.css` or in the
  button adapter below, never in the kit.
- **Superseded in part (read 2026-11-15):** from `PAGE-STYLE` 3.3 item 1 the kit itself is the `BUTTONS` adapter and is
  served byte-identically; what remains is the theme control (`.theme-btn` with a word, 3.3 item 5) and whether
  `assets/fms-buttons.css` is retired (ticket L0331). The paragraph below describes the 2026-10-20 state.
- The kit then drew the 44 px pills, the primary glow and the hover lift of `.btn`, `.seg button`, `.theme-btn` and
  `.to-top` (the catalog had not revised it for `BUTTONS`; `PAGE-STYLE` section 5 asked for an identified adapter instead).
  That adapter is `assets/fms-buttons.css`, linked after `assets/fms-site.css` on every page that loads the page layer -
  the generator's four templates and the hand-authored root and `docs/` pages - and held by check 15 ("Button adapter")
  of `Build-SitePages.ps1 -Check`. It also owns the row layout the root page used to carry inline (`.button-group`,
  `.dl-row`, `.scenario-foot`, `.dl-meta`), so the generated locales get it. What stays in `assets/fms-site.css` is the
  44 px of the page's own text links, `.tools-grid a` and the footer rows, which are links and cards and not buttons. The
  light accent and code-surface overrides the page layer carried were dropped with the re-vendoring (the kit has both);
  the light gold override stays, the kit's `#9a6a13` being 4.19 : 1 on the page.
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

## What `PAGE-STYLE` 2.0 to 3.1 ask of this site (read 2026-10-20)

- **Buttons and groups (2.0, 2.1, 3.0).** `BUTTONS` is the authority for every command button: the pill and gold
  command treatments are retired, the old fixed target defaults are superseded and a button wraps to its content.
  The catalog's kit is unchanged by this, so an identified adapter in the page layer is owed over the vendored kit
  (section 12), and a disclosure group draws its state triangle as `COLLAPSIBLE-GROUP` says, never the action
  chevron. The button adapter is `assets/fms-buttons.css` (2026-10-20); the group-marker adapter, the `btn-gold`
  and markup pass and the help corpus are still owed. The migration is specification 243's (see the
  [UNIFIED-UI pointer](UNIFIED-UI.md)) and no rendered conformance is claimed: the repository has no browser harness.
- **The 2026-10-17 amendment (3.1).** Locale of a single-URL translated page is `?lang=<ISO>` with a legacy `?l=`
  reader, `lang` winning; separate locale files stay valid. Child and documentation pages carry the contact line and
  a hub link; the shared pages of `SITE-FAMILY-MAP` section 2a are links, not cards; in-app help pages are served
  offline; the icon chrome takes no Unicode substitute where a vocabulary glyph exists (the theme control's bare
  `◐` is the open case). The `StreamsPlayer` cell of the family grid reads `STREAMS Player` since the map's 1.4.
  Not read: the `lang` query on every single-URL page, and the generator's `_family` cell.

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

The dated exceptions that remain (the map's `Tool` cell is closed since `SITE-FAMILY-MAP` 1.9, which makes it the full
name): the header brand that repeats the full name where the contract has a short name as text (a design record, the
owner's choice of 2026-10-30), the form of the language picker (4.2 and 1.6), and the theme
control, which still carries the bare glyph (the kit now styles the labelled form of 4.3 and `nav.expand` is drawn by
the kit, but the markup of every page has not adopted the visible word yet).
