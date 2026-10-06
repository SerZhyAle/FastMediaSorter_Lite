# Pointer - `SITE-REPRESENTATION`

| | |
| --- | --- |
| **Id** | `SITE-REPRESENTATION` |
| **Version** | 0.1, draft (written 2026-10-06 by FastMediaSorter Android, ticket S4096) |
| **Home** | `product-site/SITE-REPRESENTATION.md` and the run-list `product-site/SITE-CHECKLIST.md` in the shared contracts catalog |
| **Role here** | consumer, opted in by the owner on 2026-10-06 (specification 147, D1); guide tier: rules 1-5 and 12 are read, rules 6-10 apply to the task guides and were not run |

## What this repository must do

1. **Name one positioning source with an ordered pillar list** (rules 1, 2). Declared: `docs/guides/POSITIONING.md`
   (the family sentence and three pillars, in order: View and sort, Share folders, FileDO virtual disks), with
   its machine form `tools/site-facts.json`. `tools/Test-SitePositioning.ps1` holds 41 surface files (the landing
   and its twelve locale pages, the server page, the three READMEs, the Store description, the winget description)
   to that order. Closed by ticket 155.
2. **Type a fact once, in its source** (rule 3). Names have one: `docs/guides/PRODUCT_NAMING.md`, gated by
   `tools/Test-ProductNaming.ps1`. The language count (13) and the platform floor (Windows 10 1607, build 14393;
   the Store package 1809) are typed in `tools/site-facts.json`, read against their real sources (`Localization.Codes`,
   the site copy's locales, both installer scripts, the Server winget manifest, the MSIX manifest) and compared with
   every claim in 60 scanned files by the same gate. Closed by ticket 155.
3. **Declare the public editions with one name each** (rule 4): the full-package edition
   (`Fast Media Sorter & Sharing for Windows`) and the Server edition (`Fast Media Sorter & Sharing: Folder
   Share Server`), declared in `tools/site-facts.json`; a colon-form edition name the file does not declare fails
   the gate. The Media Exchange Server is planned and not public. **Owner ruling Q155-2, 2026-10-06: door 3 stays**
   as the family-map reservation (specification 132 section 3.2) - named once, in the future tense, marked planned,
   with no link, date or download, on the landing, its twelve locale pages and the three READMEs; the gate fails a
   mention anywhere else.
4. **Keep the trust pages saying one thing** (rule 12): the install-trust page is held by
   `TrustPageContractTests`; the guides' data claims against the privacy page were not run.

## Where the evidence is

Specification 147 (`done/`), specification 155 (rules 1 to 4) and the catalog registry; the one open gap is the exception with ticket 156 (rules 5 to 10 and the browser boxes, not run).
