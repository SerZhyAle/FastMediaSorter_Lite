---
paths:
  - "**/*.html"
  - "publishing/**"
  - "docs/**"
  - "assets/**"
  - "README*.md"
  - "CHANGELOG.md"
  - "src/ProductNames.vb"
  - "tools/site-*.json"
  - "tools/*Site*.ps1"
  - "tools/Test-ProductNaming.ps1"
---

# Naming and positioning rules

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

### Naming rule (ticket 111)
The product's names (full, compact, short legacy, the `FMS_Win`/`FMS_A`/`FMS_Web` aliases, Share Manager, Server edition) live in **one table: [docs/guides/PRODUCT_NAMING.md](../../docs/guides/PRODUCT_NAMING.md)**. Cite a row, never restate it. In code the names are the constants of `src/ProductNames.vb`: a string that can carry the name takes a placeholder (`TF("... {0} ...", ProductNames.Full)`), it does not spell it. `tools/Test-ProductNaming.ps1` is the gate (forbidden forms, allow-list `tools/product-naming-allow.jsonl`).
- **No `Lite` in visible text** (windows, installer screens, listings, README, site pages; the check enforces it). It survives only as a frozen identifier and the Add/Remove Programs / winget package name.
- **`&` is part of the name** and is escaped per medium (`&amp;` in HTML/XML and `.vbproj` `<Product>`, `&&` in a WinForms `Label`/`Button`/menu text or an Inno label); never in a file name, URL or identifier; never "and". The name is not translated and stays Latin in all 13 languages.
- **Frozen anchors per channel** - only the *visible* name changes, never the string a program, a store or an installed machine correlates on:
  - **App / GitHub**: exe `FastMediaSorter_LITE.exe` - **the anchor belongs to the .NET 10 x64 mainline**, which replaces the installed exe in place so settings, associations, ARP and winget/Store correlation carry over; `FastMediaSorter_x86.exe` is a fallback sibling, not an anchor. Also frozen: mutex `FastMediaSorterSingleInstanceMutex`, registry `SZA\FastMediaSorter`, ProgIDs `FastMediaSorter.*`, repo slug, release-asset base `FastMediaSorter-*`, `tests/Lite.Tests`.
  - **Installer** (`publishing/installer/FastMediaSorter.iss`): wizard screens, shortcuts and `setup.exe` properties show the full name (`#define AppName`); the ARP DisplayName stays `FastMediaSorter LITE` via `UninstallDisplayName` (`#define AppNameArp`). **Frozen: `UninstallDisplayName`, `AppId {7371E7F1-…}`, `DefaultDirName` (`FastMediaSorter_LITE`), `OutputBaseFilename`.**
  - **winget** (`publishing/winget/`): `PackageName` is `FastMediaSorter LITE` and must match the installed ARP DisplayName so `winget upgrade` correlates; only the descriptions carry the new name. **Frozen: `PackageIdentifier`, `PackageName`, `Moniker`.** **Do NOT add `AppsAndFeaturesEntries`** - owner decision (ticket 111 Q-1) until Q-1 is reversed.
  - **Store / MSIX** (`publishing/msix/`): the manifest `DisplayName` and the Partner Center listing title stay `FastMediaSorter LITE` (reserved name = update anchor; owner decision: only inscriptions change, nothing is reserved anew); Identity `Name`/`Publisher` frozen too. The compact form `Fast Media Sorter & Sharing` is not a Store name (it is for page-title budgets, tray/menu labels and file-type descriptions). Only the Partner Center listing **Description** carries the compact name without the platform suffix (ticket 233) - EN/RU copy in [docs/guides/STORE_PUBLISHING.md](../../docs/guides/STORE_PUBLISHING.md), which keeps the truthful sentence that on the Microsoft Store the app is published as `FastMediaSorter LITE`. The Share Manager `<Application>` in the manifest keeps the Share Manager name (an in-package label).
- **The Share Manager** is `Share Manager - Fast Media Sorter & Sharing` in every caption, tray tooltip and shortcut (`ProductNames.ShareManager`); its `FindWindowW` message-window title, pipe and IPC strings are wire contracts and do not change.
- History is not rewritten: `docs/specifications/done/*`, past `CHANGELOG.md` entries and active specifications keep the name they were written with; the allow-list covers them.

### Positioning rule (ticket 155)
What the product says it is lives in **one source: [docs/guides/POSITIONING.md](../../docs/guides/POSITIONING.md)** - the family sentence, the **three optional components** in a fixed order (media viewer and sorter, local SFTP folder sharing, Media Exchange Server), the **two install intents** (viewer first, server first - intents, not packages or editions), the two public editions and the **availability matrix** (component x package: default, optional dependency, interactive or unattended hosting, elevation) - with its machine form `tools/site-facts.json`. Cite it, never restate it; a surface that lists what the product does lists the components in that order. **FileDO disk hosting is a capability of sharing, never a component, a pillar or a family-sentence item** (specification 249 retired the FileDO-third-pillar model). The **Media Exchange Server** is the third component and is **in development, in no package**: it may be named wherever the components are listed, always within reach of a development marker (the data file's `development` block, 13 languages), **never in a link to a download, an installer, a release, a store or a winget command, never with a date, and its door has no call to action** (this replaces the Q155-2 reservation; the catalog amendment of `SITE-REPRESENTATION` rule 4 is carried to a successor specification). The Microsoft Store copy describes the actual Store package: the viewer plus the Share Manager, no Windows service, no FileDO hosting, no server, and no external install link. `tools/Test-SitePositioning.ps1` (a step of `Run-AllTests`) holds the surfaces, the matrix against the packaging files, the **language count** and the **Windows floor** against their real sources: never type either by hand in a page without that gate agreeing. A packaging change that moves a default or a channel fails the gate until `tools/site-facts.json`, POSITIONING.md section 4.1 and the pages say the new truth (the matrix region of `server.html` is generated by `tools/Build-SitePages.ps1`). Adding a UI language or moving an installer `MinVersion` means changing `tools/site-facts.json` in the same edit.
