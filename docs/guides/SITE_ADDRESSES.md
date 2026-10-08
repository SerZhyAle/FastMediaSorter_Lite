# Site addresses: what an address promises, and where the release notes live

The product site is served from the repository root by GitHub Pages. This page is the local statement of three
rules of the `SITE-STRUCTURE` contract (pointer: [docs/contracts/SITE-STRUCTURE.md](../contracts/SITE-STRUCTURE.md)):
the not-found page (rule 15), the declared release-notes page (rule 2) and what happens to an address when a page
moves or is retired (rule 8). It cites the contract by rule number and does not restate it.

## The not-found page

`404.html` at the repository root is a **render target**: `tools/Build-SitePages.ps1` writes it from the
`_notFound` block of `tools/site-copy.json` (edit the JSON and re-run, never the HTML). GitHub Pages serves it for
any address under the site that no page answers, at any depth. It therefore:

- builds **every** address from the base URL (`$BaseUrl` in the script), never a relative one;
- is `noindex`, and is **not** in `sitemap.xml` or `search-index.json` (it is not a page of the set);
- carries the core three languages in-page (English, Russian, Ukrainian) and picks one the way the landing does;
- offers the landing, the subject index (`topics.html`; the site has no search at the guide tier) and every guide
  listed in `_howto`.

`Build-SitePages.ps1 -Check` holds all of this (conformance check 12) and `Test-SiteQuality.ps1` walks its links.

## Release notes

**Decision (Q149-1, recommended answer taken 2026-10-06): the declared release-notes page is the GitHub Releases
address, not a page of this site.** The notes are written once, in English, in `CHANGELOG.md`, and published as the
body of each GitHub Release; the landing already links that address ("All releases"). A generated `whats-new.html`
would be a second copy of the same text to keep in step. It is added only if the owner wants the site to be
self-contained, and then it is a render target of `CHANGELOG.md`, never a hand-written page.

## A moved or retired page (forwarder policy)

An address that was ever published, by the site or by anything that holds it (the programs, the installer, the
winget manifests, the Store listing, the READMEs), keeps meaning what it meant.

- **A page that moves** leaves a forwarder at the old address: a small page that names the successor in a visible
  link, in `<link rel="canonical">` and in a `<meta http-equiv="refresh">`.
- **A page that is retired with no successor** leaves a page that says so, in the core three languages, and links
  the landing.
- **A section anchor** that an outside surface links is never renamed.
- **Applied 2026-10-08:** the eight task guides (`viewer`, `keys`, `setup`, `ocr`, `editor`, `archives`, `resources`,
  `filedo-disks`) moved from the root to `docs/how-to-<name>.html`, and each old root address
  `how-to-<name>.html` is now a forwarder (`noindex`, `<meta http-equiv="refresh">`, canonical at the new address).
  `how-to-sftp.html` was already a forwarder (to `publish-folders-android.html`) and stays where it is. A forwarder is
  not a page of the set: it is in neither `sitemap.xml` nor `search-index.json`, and `Test-SiteQuality.ps1` checks only
  that its refresh target exists.
- The addresses held outside the site are listed in `tools/site-addresses.jsonl` and resolved by
  `tools/Test-SiteAddresses.ps1` (next section). A new address in a program, listing or README is not accepted
  until it has its line there.

## The list of held addresses and its gate

`tools/site-addresses.jsonl` is the one list of the addresses of this site that something **outside the site's own
pages** holds: the programs (`src/Support/SitePages.vb`, the Share Manager's `ShareLinks.vb` and `HostingText.vb`,
the viewer's About page), the installer, both winget manifests, the release body in `release.yml`, the three
READMEs and the Store listing's privacy-policy field. Addresses of other sites (the Android app's site, FileDO,
sza.od.ua) are not on it: they are not resolvable against this page set. One record per (address, holder):

| Field | Meaning |
| --- | --- |
| `address` | the full address, on the site's base URL; a `#anchor` is allowed, a query string is not part of it |
| `holder` | the repository path of the file that holds it, or the name of the outside place (the Store listing) |
| `kind` | the page group: `landing`, `trust`, `privacy`, `edition`, `guide` |
| `carried` | `literal` (default: the address is in the holder verbatim), `built` (the holder assembles it - `fragment` is the text it must carry instead) or `external` (the holder is outside the tree, so only the address is resolved) |

The program constants stay constants. A holder that can read the list does not need to; the others are compared
with it by tests (`SiteAddressListTests` for the viewer's `SitePages`, `SiteAddressConstantsTests` for the Share
Manager's `ShareLinks` and `HostingText`).

**The locale-sibling form of each page group is stated once, in the list's `group` records** (a record with a
`locale` form and a `sitemap` flag per kind), and read from there by the gate and by the tests:

| Group | English page | Locale sibling | In `sitemap.xml` |
| --- | --- | --- | --- |
| `landing` | `index.html` | `lang/<code>/index.html` | yes |
| `trust` | `trust.html` | `lang/<code>/trust.html` | yes |
| `privacy` | `docs/privacy.html` | none (one page, en/ru/uk switched in-page; the page the Store listing names) | yes |
| `edition` | `server.html` | none | yes |
| `guide` | `docs/how-to-*.html`, `publish-folders-android.html` | none (en/ru/uk switched in-page) | yes |

`tools/Test-SiteAddresses.ps1` runs offline in `Run-AllTests.ps1` and judges: the list's own shape; that every
address is on the site and resolves to a file the Pages build serves, is in the sitemap when its group says so and
has its `#anchor`; that every group with a locale form has that page for every language of `tools/site-copy.json`;
that each holder carries its address; and that **no address of the site is held in a holder file that the list does
not name** (the scan covers `src/`, `publishing/`, `.github/`, the READMEs and `assets/help/`). Its verdict is the
shared one-line `CHECK-VERDICT`; the hand-run half is `-Live`, which requests every distinct address from the
published site once the pages are pushed (no answer is COULD NOT VERIFY, never a pass). `tests/Integration/SiteAddresses.ps1`
proves the gate itself on a throw-away tree: it passes a clean one and fails eight broken ones, each for its own
reason.
