<!-- Downstream mirror of Unified_Rules @ ed69f27 on 2026-07-23. Source of truth: P:\WEB\sites.google.comsiteszaodua\Unified_Rules\REPOSITORY_LAYOUT.md - this repo's Overlay A core was extracted from this very file, so the canonical copy is now authoritative and may have moved ahead. Edit the canonical copy, not this; re-sync here. -->

# Repository Layout - the reusable convention

A portable structure for a **desktop app distributed through GitHub Releases + winget + Microsoft
Store**, extracted from Fast Media Sorter for Windows so it can be copied to sibling projects (same
publisher, same three channels). Read it as the *target shape*; adapt names, keep the roles.

The three goals it serves: (1) one obvious home for every kind of file, (2) a publication process that
is identical across projects, (3) nothing secret or heavy ever committed by accident.

## Top-level shape

```
<repo>/
  README.md  README_<lang>.md     product intro, per language (root - user-facing)
  CHANGELOG.md                     "What's new", Keep-a-Changelog style (root - user-facing)
  LICENSE
  CLAUDE.md                        agent/contributor operating instructions (root)
  <App>.sln  build.ps1  a.ps1      build entry points (root)
  src/                             source
  tests/                           test projects
  assets/                          shipped/icon/site assets (icons, flags, site CSS, store master art)
  publishing/                      << everything channel-specific to a release (see below)
  tools/                           build & release automation scripts (not channel-specific)
  docs/                            all internal documentation (see taxonomy below)
  index.html  *.html               GitHub Pages site (served from repo ROOT - see Site)
```

**Root stays minimal.** Only files that a *user* or a *first-time contributor* expects at eye level
live at root: the READMEs, CHANGELOG, LICENSE, the solution + build entry scripts, and the site's
landing pages. Everything explanatory goes under `docs/`; everything release-mechanical under
`publishing/` and `tools/`.

## `publishing/` - one home per distribution channel

The single most important convention. Every channel-specific artifact lives under one umbrella, one
subfolder per channel, so "how do we ship?" has one answer and other repos copy the folder wholesale.

```
publishing/
  installer/     Inno Setup script (.iss) + its helper .ps1 (elevation, pre-uninstall stop)
  winget/        the 3 winget manifests (version / installer / defaultLocale) - source of truth
  msix/          Store MSIX: AppxManifest.xml, build-msix.ps1, README.md  (+ gitignored dist/, stage/)
  store/         Partner Center listing material: listingData.csv, screenshot scripts, listing prompt
```

Rules that keep this portable:
- **Manifests are committed source, submissions are generated.** `publishing/winget/*.yaml` and
  `publishing/store/listingData.csv` are the canonical copies edited each release; the actual
  winget-pkgs PR and Partner Center upload are produced *from* them. Keep the repo copy in sync so the
  next release starts from the last shipped text, never from a blank export.
- **Channel folders are self-contained.** A script inside `publishing/<channel>/` finds the repo root
  by going **two** levels up (`Split-Path (Split-Path $PSScriptRoot -Parent) -Parent`). Any `.iss`/
  manifest relative path to repo assets is `..\..\assets\...`. If you move a channel folder, this is
  the one thing to re-check.
- **Frozen update anchors live *inside* these files, never in the folder name.** The winget
  `PackageIdentifier`/`PackageName`, the Inno `AppId`/`UninstallDisplayName`/`DefaultDirName`/
  `OutputBaseFilename`, and the MSIX Identity `Name`/`Publisher` are what correlate an *update* to an
  *install*. Reorganizing folders is safe; changing those values orphans every installed copy.
- **Generated output is gitignored, not deleted from the tree.** `publishing/msix/dist/` and
  `.../stage/` are covered by the global `dist/` + `stage/` ignore rules; they regenerate on build.

## `tools/` - automation, not artifacts

Scripts that *drive* a build or release but are not tied to one channel:

```
tools/
  Release.ps1                orchestrator: dry-run by default, -Push creates+pushes the v* tag
  Build-OfflineRelease.ps1   mirrors CI locally (portable ZIP + installer)
  Build-Installer.ps1        local copy-anywhere setup.exe only
  Prepare-<Offline>Payload.ps1   downloads/stages heavy optional payload (codecs, OCR models)
  Clean-Build.ps1  Run-AllTests.ps1
  <cache>/                   download caches - gitignored
```

A tools script references its siblings by `$PSScriptRoot` (depth-independent) and repo-root files by
`Join-Path $SolutionDir '<path>'`. That is why `tools/` scripts survive a `publishing/` reorg without
edits, while a script *inside* `publishing/` needs the two-levels-up root derivation above.

## `docs/` - documentation taxonomy

One folder per document *lifecycle stage*, one filename **prefix** per document *type*. The prefix is
the contract; the folder is where it currently sits.

```
docs/
  README.md                  index of this tree (start here)
  guides/                    living operational docs - read while doing the task
                             BUILD_AND_RELEASE.md, STORE_PUBLISHING.md, TESTING.md,
                             REPOSITORY_LAYOUT.md (this file), DOCUMENTATION_CONCEPT.md
  specifications/            SPECIFICATION_*.md still being built
  specifications/done/       SPECIFICATION_*.md that shipped (archive - do not churn)
  roadmaps/                  ROADMAP_*.md
  contracts/                 CONTRACT_*.md - frozen wire/interface contracts with other products
  privacy.html               hosted privacy policy (required by the Store)
  assets/  images/           doc/site images
  dev-notes/                 throwaway diagnostics, captures, scratch (not authoritative)
```

**Filename prefixes** (uppercase, `SNAKE_CASE`, the type first):
`SPECIFICATION_`, `ROADMAP_`, `CONTRACT_`, `PROGRESS_`, `RESEARCH_`, `PLAN_`. A new doc takes the
prefix of its type and lands in the matching folder. The prefix is what makes `grep`/glob and the
index reliable; the folder can change without renaming the file.

> **Archive is frozen.** Files under `specifications/done/` predate this convention in places
> (mixed-case, suffix-instead-of-prefix, a plan referenced by its spec). They are historical records
> cross-linked by exact filename - leave them. Apply the convention to *new* docs only.

## Secrets - never in the repo

No secret is ever committed. The channels this layout targets are built so that none is required in-repo:

| Secret | Where it lives | Why not in-repo |
| --- | --- | --- |
| GitHub release / API token | the machine's `gh auth` cache / CI's `GITHUB_TOKEN` | ambient to the runner; scripts read it at call time (`gh auth token`) |
| Code-signing certificate | **not needed** | the Store re-signs the MSIX on certification; the Inno/ZIP path ships unsigned |
| Partner Center identity (`Publisher` CN, `IdentityName`) | script *defaults* + Partner Center | the CN is per-publisher, not a secret, but is passed as a parameter, never hardcoded as a credential |
| App-level API keys (if the app has any) | user's machine via OS secret store (DPAPI/Credential Manager) at runtime | belongs to the end user, not the build |

If a project genuinely needs a build secret, it goes in the CI secrets store and is referenced by name
in the workflow - never written to a tracked file. `.gitignore` should pre-empt the common leaks
(`*.pfx`, `*.snk`, `.env`, `*token*`, `*secret*`).

## Built binaries & artifacts - retention policy

Binaries are **build output, not source** - the repo never stores a compiled release.

- **Local build output** (`bin/`, `obj/`, `dist/`, `stage/`, `publishing/msix/dist/`) is gitignored;
  it regenerates from a tag.
- **The authoritative published binary is the GitHub Release asset**, named from the tag
  (`<App>-<version>-<platform>-setup.exe`, `...-<platform>.zip`, each with a `.sha256`). The Release
  *is* the artifact archive - versioned, immutable, downloadable. Don't duplicate it into the repo.
- **One vendored binary exception**, if unavoidable (e.g. a prebuilt sidecar from another repo): keep
  it under a clearly gitignored `payload/` with a narrow allow-list for just the needed file, and
  document that a fresh clone lacks it until fetched.
- **Version is the tag**, format `YY.M.D.HHmm` (e.g. `26.7.23.1127`) - date-derived, monotonic,
  unique per minute, sortable. It is stamped into every exe at build and remapped where a channel
  demands a different shape (the Store needs `Major.Minor.Build.0`). The tag is authoritative for
  asset names; keep the in-file stamp consistent with it.

## Site (GitHub Pages)

Pages is served from the repo **root** (not `docs/`), so the live pages are the root `*.html` +
root `assets/`. Keep must-be-live pages at root. If a `docs/`-based redesign exists, treat it as an
unpublished staging copy until Pages is switched over - do not assume root and `docs/` HTML are meant
to match. See [DOCUMENTATION_CONCEPT.md](DOCUMENTATION_CONCEPT.md) for the mandatory pages and the SEO
instrumentation each must carry.

## Applying this to a new project - checklist

1. Create `publishing/{installer,winget,msix,store}/`, `tools/`, `docs/{guides,specifications,roadmaps,contracts}/`.
2. Copy the publisher-constant values (Partner Center `Publisher` CN, `PublisherDisplayName`, IARC
   rating id) into the msix build script defaults - they are shared across all your products.
3. Reserve a **new** `IdentityName` / winget `PackageIdentifier` / Inno `AppId` per product - these are
   the frozen anchors, unique to each app.
4. Adopt the `YY.M.D.HHmm` tag + `CHANGELOG.md [Unreleased]` flow (see DOCUMENTATION_CONCEPT.md).
5. Point `.gitignore` at `bin/ obj/ dist/ stage/ *.pfx *.snk .env` and any download caches.
6. Set Pages to serve from root; add the mandatory site pages.
