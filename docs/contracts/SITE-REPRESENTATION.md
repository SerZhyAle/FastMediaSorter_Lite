# Pointer - `SITE-REPRESENTATION`

| | |
| --- | --- |
| **Id** | `SITE-REPRESENTATION` |
| **Version** | 0.1, draft (written 2026-10-06 by FastMediaSorter Android, ticket S4096; unchanged on 2026-10-07, re-read that day) |
| **Home** | `product-site/SITE-REPRESENTATION.md` and the run-list `product-site/SITE-CHECKLIST.md` in the shared contracts catalog |
| **Role here** | consumer, opted in by the owner on 2026-10-06 (specification 147, D1); guide tier: rules 1-4 held by the positioning gate (ticket 155); rules 5-10 and 12 were run on the ten function pages on 2026-10-06 (specification 156) and fail on all ten (ticket 157) |

## What this repository must do

1. **Name one positioning source with an ordered pillar list** (rules 1, 2). Declared: `docs/guides/POSITIONING.md`
   (the family sentence and three optional components, in order: media viewer and sorter, local SFTP folder
   sharing, Media Exchange Server; FileDO hosting is a capability of sharing - **specification 249, 2026-10-20,
   replaced the three-pillar list**), with its machine form `tools/site-facts.json`. `tools/Test-SitePositioning.ps1`
   holds 41 surface files (the landing and its twelve locale pages, the server page, the three READMEs, the Store
   description, the winget description) to that order, and the component x channel availability matrix to the
   packaging files. Closed by ticket 155, reopened and re-closed by specification 249.
2. **Type a fact once, in its source** (rule 3). Names have one: `docs/guides/PRODUCT_NAMING.md`, gated by
   `tools/Test-ProductNaming.ps1`. The language count (13) and the platform floor (Windows 10 1607, build 14393;
   the Store package 1809) are typed in `tools/site-facts.json`, read against their real sources (`Localization.Codes`,
   the site copy's locales, both installer scripts, the Server winget manifest, the MSIX manifest) and compared with
   every claim in 60 scanned files by the same gate. Closed by ticket 155.
3. **Declare the public editions with one name each** (rule 4): the full-package edition
   (`Fast Media Sorter & Sharing for Windows`) and the Server edition (`Fast Media Sorter & Sharing: Folder
   Share Server`), declared in `tools/site-facts.json`; a colon-form edition name the file does not declare fails
   the gate. The Media Exchange Server is the third component, **in development and in no package**.
   **Dated local exception to rule 4 as the catalog words it (owner decision 2026-10-20, specification 249; it
   replaces the Q155-2 ruling of 2026-10-06, which allowed one future-tense mention in door 3):** the server is
   named wherever the three components are listed and wherever the data file's `development` block allows, always
   within 420 characters of a development marker in the page's own language, never inside a link to a download, an
   installer, a release, a store or a winget command, never with a date, and its door carries no call to action; the
   Microsoft Store listing does not name it at all. The catalog amendment (rule 4 and its registry row) is not
   written from this repository: `SITE-REPRESENTATION` is sealed and owned by FastMediaSorter Android, so on
   2026-10-21 (specification 251 T-6) it was filed as the proposal `PROPOSAL-2026-10-21-component-in-development-may-be-named`
   beside the contract (status: proposed). This exception stands until the owner folds the proposal in and the
   catalog says the same; then it is removed from this pointer in the same edit.
4. **Keep the trust pages saying one thing** (rule 12): the install-trust page is held by
   `TrustPageContractTests`. The data claims of the ten function pages were read against `docs/privacy.html` on
   2026-10-06: the UPnP / reachability-check wording, the firewall step and several local files do not agree or
   are missing (ticket 157 T-4).
5. **Say what the page's controls are called, state limits and availability in place, one function per page** (rules
   5-10): none of it holds today - quoted names that are not the product's strings, no requirements element or
   outcome callout, defaults repeated, no settings reference or capability matrix, two pages on one flow (ticket 157,
   owner questions Q157-1 and Q157-2).

## Where the evidence is

Specification 147 (`done/`), specification 155 (rules 1 to 4) and the catalog registry; specification 156 (`done/`, the browser run and the reading of the ten pages) and its follow-ups 157 (the function pages) and 158 (the browser findings and what is still not run).

## State on 2026-10-20 (specification 249)

Rules 1 to 4 hold under the three-component model: `tools/Test-SitePositioning.ps1` PASS (3 components, 41 regions
of 41 surface files, 2 edition names, 171 in-development mentions, 16 matrix cells over 20 evidence patterns, 138
typed claims in 69 files); its fixture self-test `tests/Integration/SitePositioning.ps1` PASS (38 cases). The ten
function pages (rules 5-10 and 12) were not re-read.

## State on 2026-10-07

Rules 1 to 4 still hold: `tools/Test-SitePositioning.ps1` PASS (3 pillars, 41 regions of 41 surface files, 2 edition
names, 33 reservation mentions, 118 typed claims in 60 files). The ten function pages (rules 5-10 and 12) were not
re-read and the gaps of ticket 157 stand. One name question sits beside rule 4 and is the family map's, not this
contract's: the visible full name is *Fast Media Sorter & Sharing for Windows* (`docs/guides/PRODUCT_NAMING.md`),
while the `Tool` cell of `SITE-FAMILY-MAP` still carries the name this product had before that rename - see
`docs/contracts/PAGE.md`.
