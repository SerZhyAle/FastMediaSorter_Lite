# Documentation Concept - how a product presents, indexes, and supports itself

The companion strategy to [REPOSITORY_LAYOUT.md](REPOSITORY_LAYOUT.md). Layout says *where files
live*; this says *what we write, how we want to be found, and how we treat the user*. Written to be
copied to every sibling product (desktop app shipped via GitHub + winget + Microsoft Store).

Principle: **one source of truth per fact, rendered into many surfaces.** A feature, a version, a
change, a privacy statement each has exactly one authoritative home in the repo; the site, the store
listings, and the release notes are *renderings* of it, never independent re-authorings that drift.

---

## 1. The single sources of truth

| Fact | Authoritative home | Rendered into |
| --- | --- | --- |
| What the product is / does | `README.md` (+ `README_<lang>.md`) | site landing page, store Description, winget locale |
| What changed, per version | `CHANGELOG.md` | GitHub Release body, site "What's new", store "release notes" |
| Current version | the `vYY.M.D.HHmm` git tag | exe stamp, asset names, store version (remapped) |
| Publication mechanics | `docs/guides/BUILD_AND_RELEASE.md` + `STORE_PUBLISHING.md` | the release checklist / skill |
| Channel listing text | `publishing/store/listingData.csv`, `publishing/winget/*locale*` | Partner Center, winget-pkgs PR |
| Privacy | `docs/privacy.html` | Store listing URL, site footer |
| Frozen wire contracts | `docs/contracts/CONTRACT_*.md` | the other product that shares the contract |

If a fact appears in two places, one of them is a render target and must be regenerated, not
hand-edited. The store listing copy, for example, is trimmed to each field's character cap
(descriptions, and the **1500-char release-notes limit**) *from* the CHANGELOG text - keep the long
form in CHANGELOG, the trimmed form in `listingData.csv`.

## 2. Versioning & change tracking

- **Version = date tag**, `YY.M.D.HHmm` (e.g. `26.7.23.1127`). Monotonic, unique per minute, sorts
  chronologically, needs no manual bump decision. Each channel that wants a different shape derives it
  mechanically (Store: `YY.(M*100+D).HHmm.0`).
- **CHANGELOG is the ledger.** Keep-a-Changelog format, English only (it is published verbatim as the
  GitHub Release body and the site "What's new"). Categories: `Added / Changed / Fixed / Removed`.
  - Regular builds accrete bullets under `## [Unreleased]`.
  - A release moves `[Unreleased]` into `## [<version>] - <YYYY-MM-DD>`, opens a fresh empty
    `[Unreleased]`. That dated section *is* the release note - do not re-author it elsewhere.
- **Build vs Release are distinct and one is billable.** A "build" is local, free, tags nothing. A
  "release" pushes the `v*` tag, which is the single operation that triggers CI + costs. Documenting
  this boundary explicitly prevents accidental paid runs.

## 3. Discoverability - how we want to be indexed

We are found three ways: **the store's own search**, **winget search**, and **web search of the site**.
Each needs deliberate instrumentation; none is automatic.

**By functionality, not just name.** We present the product by the *jobs it does* - "sort photos and
videos with hotkeys", "open HEIC/AVIF without a codec", "share folders to a phone" - because that is
what users type. The name is a frozen anchor for *updates*, not the primary search hook.

- **Microsoft Store**: `SearchTerm1..N` and `Feature1..N` fields in `listingData.csv` carry the
  functional keywords; the Description's **first sentence** is the truncated card hook, so it must be a
  self-contained value statement, not a parenthetical. Fill every search-term slot with a distinct
  user phrase (`photo sorter`, `image viewer`, `heic avif viewer`, ...).
- **winget**: `Tags`, `Moniker`, and `ShortDescription` in the locale manifest are the search surface;
  `Description` leads with the functional pitch.
- **Web / GitHub Pages**: every site page carries the SEO instrumentation below.

### SEO instrumentation every site page must carry

A page without these is invisible to crawlers even when live:

- `<title>` - value + brand, under ~60 chars (`Fast photo & video sorting for Windows | Fast Media Sorter`)
- `<meta name="description">` - one-sentence functional summary, ~150 chars
- `<link rel="canonical">` - the absolute public URL (prevents duplicate-content splitting)
- Open Graph + Twitter card - `og:title/description/image/url`, `twitter:card=summary_large_image`,
  with a real preview image (≥1200×630) so shared links render richly
- One `<h1>` stating the job the product does; `<h2>`s for each feature area
- `JSON-LD` structured data (`SoftwareApplication`: name, OS, price, ratingValue) so search engines can
  show an app rich-result
- `hreflang` for each translated page; language toggles must use consistent ISO codes across the site
- A `sitemap.xml` + `robots.txt` at the site root listing every public page

## 4. Mandatory site pages

The minimum set for a credible, store-linked product site:

| Page | Purpose | Must have |
| --- | --- | --- |
| Landing (`index.html`) | value + download | H1 job statement, download/winget/Store CTAs, feature sections, screenshots, full SEO+OG |
| Privacy (`privacy.html`) | required by the Store | plain-language "what we access & why", "no telemetry" if true, contact email |
| How-to / guide page(s) | reduce support load | task-oriented ("open a folder and sort"), screenshots, keyboard shortcuts |
| "What's new" | retention + SEO freshness | rendered from CHANGELOG; each version dated |
| Support / contact | user trust | how to report a bug (issue tracker link), contact email, response expectation |

Buttons point at durable URLs (`/releases/latest`, the winget id, the Store product page), never a
hard-coded version, so the site never goes stale between releases.

## 5. User support & friendliness

The product's tone is a documented choice, not left to each writer:

- **Speak in the user's task, not our architecture.** "iPhone photos open now (HEIC, HEIF, AVIF)" -
  not "added an ISO-BMFF decoder path". CHANGELOG bullets lead with the user-visible win and only then,
  briefly, the mechanism.
- **Every honest limitation is stated up front**, in the listing and the app: what the free tier
  does, what needs a network call, what a permission is for. Pre-empting a support question in the
  description is cheaper than answering it and builds trust with reviewers too.
- **One friendly voice across surfaces.** Same phrasing for a feature in README, site, and store, so a
  user who reads two of them isn't confused by three names for one thing.
- **Support path is one click from everywhere**: issue tracker for bugs, an email for private
  contact, both linked from the site footer, the store listing, and the app's About.
- **Localize the user-facing surfaces** (README, site, store listing) to the audiences you actually
  have; keep the CHANGELOG English (it is the canonical technical ledger).

### House text style (applies to docs, UI, listings, comments)

- Hyphen `-`, never em-dash; `..` not `...`; use `ё` in Russian text.
- These are enforced everywhere user-visible; they are not applied to code or vendored files.

## 6. Applying to a new project

1. Stand up the six sources of truth (§1) - most are empty files to start.
2. Adopt the tag + CHANGELOG `[Unreleased]` flow (§2).
3. Fill the store/winget search + feature fields functionally (§3); add the SEO block to every page.
4. Ship the five mandatory pages (§4) with durable-URL CTAs.
5. Write the README/listing in the task-first friendly voice (§5) and localize the user-facing three.
