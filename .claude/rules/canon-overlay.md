---
paths:
  - "publishing/**"
  - ".github/**"
  - "**/*.html"
  - "assets/**"
  - ".sza-canon.json"
  - "contrib/**"
---

# Unified rules and release conventions (canon overlay)

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

The **universal** SZA conventions live in one canon. **Reference it, do not restate it here.**

**Canon:** the `sza` Claude Code plugin, from `github.com/SerZhyAle/sza-unified-rules` (start at `rules/INVARIANTS.md`; adoption stamped in `.sza-canon.json`). This repo is **Overlay A - Windows desktop** and the reference project the Overlay A core was extracted from, so most of its shape is the canonical text. The per-project record with evidence and remaining deltas is `contrib/fastmediasorter_lite.md` in the canon repo. FMS **references** the canon, never mirrors it; the two pre-canon originals under `docs/guides/` (`REPOSITORY_LAYOUT.md`, `DOCUMENTATION_CONCEPT.md`) are **downstream mirrors** whose banner names the canon as source of truth. Only the FMS-specific deltas below stay local.

**Deltas this repo owns:**
- **Dual-runtime, one source tree, two toolchains:** parity is enforced by compile-time `#If NETFRAMEWORK` seams, not a `PARITY.md` (DEVELOPMENT §13; see "Two builds, one source tree").
- **A frozen anchor can be re-pointed to a new implementation:** `FastMediaSorter_LITE.exe` now names the net10 mainline in place (same path/id), so update correlation carried over, and the old build took `FastMediaSorter_x86.exe`. The anchor ties to the install location/id, not the binary's contents, and the visible name is decoupled from it (see "Naming rule"; PLATFORM_OVERLAYS).
- **Runtime-migration compatibility pins:** net48 -> net10 flips three invisible defaults, each pinned back (DPI `PerMonitorV2`, culture `UseNls=true`, single-file `Assembly.Location`) - see "Modern-only compatibility pins" (DEVELOPMENT §14).
- **winget:** the failure-mode list, the `PackageName` = ARP DisplayName coupling and the mixed-floor `MinimumOSVersion` anti-pattern - see "Publishing to winget" in `build-release.md` (CHANNEL_MATRIX).
- **Site pages and HTML docs use the FULL screen width (owner decision; `PAGE-CONTENT` "Layout contract", `PAGE-STYLE` section 1).** No centred column: no px/ch/rem `max-width` on a page, its header, footer, headings, lead text or sections, and `--wide` is `100%`. The vendored kit `assets/sza-kit.css` owns `--wide` and `--gutter`; `assets/fms-site.css` repeats `--wide` only so the rule 8 guard on that file keeps reading it, and does not redefine `--gutter`. A narrow measure is allowed only inside a card or callout. It applies to every page you write or generate (root `*.html`, `how-to-*`, `docs/*.html`, the templates in `tools/Build-SitePages.ps1`), and `tools/Test-SiteQuality.ps1` rule 8 fails on a cap - so do not copy a `<style>` block with `.hero h1{max-width:..}` from an old page. First screen: the kit's identity block (`PAGE-STYLE` 4.1b: the program icon `assets/icons/store-icon-256.png` once and the full name as `.product-name`, both sized by the kit), then the one outcome `h1`. The header carries the documentation link beside the brand (`.nav-doc`, a text link and not a `.btn`: the kit hides a header `.btn` under 480 px) and all 13 languages (`.lang-menu`); the get-it block links the Server edition beside the Store; the footer carries the author and documentation rows.
- **Server/elevation feature ships OFF, gated opt-in:** the Share server surface is dormant until an explicit, never-silent opt-in (`ServerFeatures.IsEnabled()`) - the one owner-approved scoped exception to "no firewall / no elevation" (SECURITY_AND_PRIVACY §3), **widened (Q-133-9) to the Media Exchange Server too: still one program-scoped inbound firewall rule behind one visible UAC prompt, never silent, never at install, removed on uninstall.**
- **A vendored sidecar may be committed** (not only gitignored): the Go Share worker is committed via a narrow `.gitignore` negation, so a fresh clone has the Share feature (REPOSITORY_LAYOUT "Built binaries").

**Two answers this repo owes the canon (do not re-decide):**
- **No hooks are registered locally, and none should be.** `.claude/settings.json` carries permissions only; the enforcement layer (AI_USAGE §5, GITHUB_INTERACTION §6) arrives with the `sza` plugin (its `guard-*` hooks and prompt-submit advisories). A second local copy would run twice, and removing the plugin's copy would disarm it silently. If a guard looks absent, check the installed plugin **cache**, not a marketplace clone.
- **Release package plan (RELEASE_AND_DISTRIBUTION §8): SKIPPED, deliberately.** It projects a ticket store, and this repo has none: specifications are local, Git-ignored working documents whose `NNN_` filename number is a queue label, not a database id or release ticket (state: `docs/specifications/README.md`); they move by hand to `docs/specifications/done/` when shipped. The planning surface is `CHANGELOG.md [Unreleased]` plus that folder pair; a ticket store would be a new system and the owner's scope call.
