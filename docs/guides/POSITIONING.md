# Positioning

The one source every external surface is written from: the landing and its twelve locale pages, the server page,
the three authored READMEs, the Store listing and the winget description. No surface is written from another
surface; where two disagree, this file decides which one is wrong (`SITE-REPRESENTATION` rules 1 and 2, ticket 155).

The machine form of everything below is [tools/site-facts.json](../../tools/site-facts.json), and
[tools/Test-SitePositioning.ps1](../../tools/Test-SitePositioning.ps1) holds the surfaces to it. This file says it
in prose and in the order; the data file carries the same values for a program to read. The two are compared by the
gate, so a change starts here and in the data file, and only then in a surface.

## 1. The statement

Every surface that says what the product is leads with this sentence, in the reader's language (the product name is
kept, [PRODUCT_NAMING](PRODUCT_NAMING.md)):

> View and sort your media; when you need it, the same program is also a folder-share server and a host for FileDO
> virtual disks - each of those ships optional, off until you ask for it.

The wording was decided in specification 132 section 3.1 (archived); this file is its home from now on.

## 2. The pillars, in order

A surface that lists what the product does lists these in this order. A surface too short for all of them names the
first ones. No store policy asks for a different first function here, so there is no exception.

| # | Pillar | What it is |
| --- | --- | --- |
| 1 | **View and sort** | The viewer and sorter: fast paging through huge folders of photos and video, one key per file (move, copy, rename, delete), slideshow, playback, archives and remote folders opened like folders. The core of every package. |
| 2 | **Share folders** | The folder-share server (the Share Manager and its SFTP worker): the folders you pick open on an Android phone, another copy of the program or any SFTP client. Optional and off until you switch it on. |
| 3 | **FileDO virtual disks** | Hosting FileDO `.fdd` virtual disks through the same worker. Optional and off until you register a disk. |

The three roles are the three rows of the editions below: the first is why every package exists, the second is the
Server edition's whole job, the third rides along as an optional component.

## 3. The public editions

A visitor can obtain two editions. Each has one display name, owned by [PRODUCT_NAMING](PRODUCT_NAMING.md) section 1,
shown the same on every page.

| Edition | Display name | Obtained as | Leads with |
| --- | --- | --- | --- |
| **The full package** | `Fast Media Sorter & Sharing for Windows` | the User installer, the portable ZIP, winget, the Microsoft Store; carries the viewer, the Share Manager and both viewer exes (the x64 mainline and the x86 fallback) | pillar 1; the other two are optional components |
| **The Server edition** | `Fast Media Sorter & Sharing: Folder Share Server` | its own setup.exe and its own winget package | pillar 2 - the server leads and the viewer rides along as an unticked component (specification 132 section 3.3); this is a focused package, not a second order of the pillars |

The Server edition's own page and manifest lead with the server because that is the package's job. They are
declared in the data file (`focusedEditions`), judged for the family sentence where they repeat it, and not for
pillar order beyond it.

### The reserved door (Q155-2, decided 2026-10-06)

The **Media Exchange Server** is planned and is not a public edition. The owner's ruling on `SITE-REPRESENTATION`
rule 4 (a variant that is not public is not mentioned) is that the reservation **stays**, as a place on the family
map and not as an edition: door 3 on the landing, in the twelve locale pages and in the three READMEs names it once,
in the future tense, marked planned, with no download, no date and no list of capabilities - the reading specification
132 section 3.2 already gave it. Nothing else mentions it: the gate fails a mention outside those declared places and
a link, a date or a download inside them. Reversing the ruling means taking door 3 off those surfaces and deleting the
`reservations` entry of the data file in the same change.

## 4. Facts typed once

A fact has one source, and a page that states it is compared with that source, never with another page
(`SITE-REPRESENTATION` rule 3).

| Fact | Value | Source of truth | Held by |
| --- | --- | --- | --- |
| Interface languages | 13 | `Localization.Codes` in `src/Localization/Localization.vb`, and the locale keys of `tools/site-copy.json` | the gate reads both and the data file; every `N languages` of a scanned file must equal them |
| Platform floor of the program | Windows 10 version 1607 (build 14393) for the x64 mainline, the installer's Share/FileDO/PDF components and the Server edition; Windows 7/8.1 and 32-bit Windows run the x86 fallback viewer only | `UseModernExe` and the component `MinVersion` lines of `publishing/installer/FastMediaSorter.iss`, `MinVersion` of `FastMediaSorterServer.iss`, `MinimumOSVersion` of the Server winget manifest | the gate reads them against the data file; every `Windows 10 <version>` of a scanned file must be a version the data file lists |
| Platform floor of the Store package | Windows 10 version 1809 (build 17763) - a Store-package floor, not the program's | `TargetDeviceFamily MinVersion` of `publishing/msix/AppxManifest.xml` | the gate reads it; the data file lists 1809 as the one other version a page may name |
| Names | the table of [PRODUCT_NAMING](PRODUCT_NAMING.md) | that table | `tools/Test-ProductNaming.ps1` |
| Release version | resolved at run time; no page types it | `src/My Project/VersionInfo.vb` and the tag | `tools/Test-ArtifactVersion.ps1` |

The channels (GitHub Releases, winget, the Microsoft Store) are named in `tools/site-copy.json` `_channels` and
carry their addresses in `tools/site-addresses.jsonl`; the address gate holds them.

## 5. Device classes

Desktop only: Windows PCs and Windows Server. There is no phone, tablet or TV edition of this product; the Android
program is a different product with its own site.
