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
| Release version | resolved at run time; no page types it - the one exception is the generated `whats-new.html`, whose version headings are rendered from `CHANGELOG.md`, never retyped | `src/My Project/VersionInfo.vb` and the tag | `tools/Test-ArtifactVersion.ps1`; `tools/Build-SitePages.ps1 -Check` for the page |

The channels (GitHub Releases, winget, the Microsoft Store) are named in `tools/site-copy.json` `_channels` and
carry their addresses in `tools/site-addresses.jsonl`; the address gate holds them.

## 5. Device classes

Desktop only: Windows PCs and Windows Server. There is no phone, tablet or TV edition of this product; the Android
program is a different product with its own site.

## 6. The worksheet - what sections 1 to 5 do not say

Sections 1 to 5 are the worksheet's published half: the statement, the pillars, the editions, the typed facts. This
section is its other half, written once so that every discoverability field of section 7 is derived from it and not
from a mood (canon `PROMOTION` section 2, ticket 207 D-3). It never names a competitor's product: the generic
alternative is named, and the products researched stay in the internal research guide
([RESEARCH_COMPETITOR_FEATURES.md](RESEARCH_COMPETITOR_FEATURES.md)), which is also where the gate reads them from.

### 6.1 Competitive alternatives - what a person does today without this product

| Alternative | Why it falls short for the job |
| --- | --- |
| The Photos app of Windows plus File Explorer drag and drop | Every file is a drag between two windows, there is no one-key destination, and a video and a photo do not page together with a destination list beside them. |
| A classic image viewer used as a sorter (generic "image viewers") | Strong at looking, weak at filing: moving a file is a menu or a second program, and video is another tool altogether. |
| A photo catalogue or asset manager | It imports and indexes first; the job here is to finish one folder in one sitting without importing anything. |
| For the share door: a cloud sync app, a USB cable, a messenger | The folder has to be uploaded or copied before the phone sees it; here the phone opens the PC's folder directly, over the local network or SFTP, with nothing leaving the machine. |

No real user report exists yet (the issue tracker has been empty since the project started), so no alternative here is
issue-sourced; they are the situations the product was built against and the research guide of 2026-06-21 recorded.

### 6.2 Unique attributes - each with its pointer

An attribute without a pointer is cut.

| Attribute | Pointer |
| --- | --- |
| One key per file: a digit moves, Shift + the digit copies, chosen at the moment of the action | README, the "Move or copy, decided as you go" bullet; `Main_Form.ExecuteRecipientAction` |
| Photos and videos in one sorter, video played offline through the built-in LibVLC engine | README "What the 64-bit program adds"; `src/Main_Form.VideoPlayer.vb` |
| Archives (ZIP, CBZ, 7z, RAR) and remote SFTP folders open like ordinary folders | `src/Archives/`, `src/Resources/`; [how-to-archives.html](../how-to-archives.html), [how-to-resources.html](../how-to-resources.html) |
| Offline OCR (Tesseract) with optional on-image translation by a local model or a server the user chooses | `src/Ocr/`, `src/Translate/`; [how-to-ocr.html](../how-to-ocr.html) |
| Optional folder sharing to an Android phone over SFTP, paired by QR code, off until switched on | the Share Manager; contract `FMSCFG`; [publish-folders-android.html](../../publish-folders-android.html) |
| Thirteen interface languages, three of them proofread | `Localization.Codes` in `src/Localization/Localization.vb`; the typed fact of section 4 |
| Free, MIT-licensed, no telemetry | `LICENSE`; [privacy.html](../privacy.html) |

### 6.3 Value - the job, in the user's words

When a camera or phone dump leaves thousands of mixed photos and videos in one folder, I want to look at each once and
send it to its place with one key, so I can finish the folder in one sitting instead of dragging files for an evening.

Door 2: when I want this PC's folder on my phone, I want to scan a code once, so I can open it from the phone without a
cloud in between.

### 6.4 Target persona

Ordinary Windows users with large personal photo and video folders - the product compass's default. The second
persona exists only as a lens on door 2: the same person with an Android phone. No technical user is the persona; a
technical list may lead with SFTP, HEIC and AVIF decoding or offline OCR because that is what that audience
searches for, never the site, the Store or winget (ticket 207 D-4).

### 6.5 Market category

**Photo and video sorter**, with **image viewer** as its synonym - the two terms people already search for, and the
two the Store listing already carries. The product does not invent a category.

### 6.6 The positioning statement (internal, never published verbatim)

For ordinary Windows users with large photo and video folders who need to finish a mixed dump in one sitting, Fast
Media Sorter & Sharing is a photo and video sorter that moves or copies each file to its place with one key. Unlike
dragging files between Explorer windows, it pages through the whole folder, plays the video in the same window, and
puts the destinations on the keyboard.

## 7. Derived fields

Every discoverability field of every channel is written from section 6 and the family sentence, and its machine form
is the `discoverability` block of [tools/site-facts.json](../../tools/site-facts.json). A field changes here and in
that block first, then in the channel. `tools/Test-SitePositioning.ps1` holds the block (limits, forbidden words, no
competitor name) and the channels whose fields are already in the tree; the others are held when the step that writes
them lands (ticket 207 T-8) and are listed in the block's `applied`.

| Field | Value | Limit | Applied |
| --- | --- | --- | --- |
| Category | photo and video sorter (synonyms: image viewer, photo organizer) | - | the base of every field below |
| Tagline | Sort photos and videos with one key per file | 6 to 10 words | the social card; README and Store at T-8 |
| GitHub description | category noun first, the compact name, the optional-sharing sentence, Free, MIT - the exact text is `discoverability.githubDescription` | 350 characters; no em or en dash | live repository metadata: ticket 207 T-2, after the owner's go |
| Homepage | `https://serzhyale.github.io/FastMediaSorter_Lite/` (the site, not the repository) | - | T-2 |
| Topics | `discoverability.topics` (16): the category and its synonyms, the platform, the formats and engines the product really handles. `winforms` and `dotnet` are dropped as stale runtime words | 20; 50 characters each; lowercase and hyphens | T-2 |
| Page title | the job in the page's language, then the compact name: `{pageTitle} \| Fast Media Sorter & Sharing` (the separator the landing and the not-found page already use); `pageTitle` per locale in `tools/site-copy.json` | 60 characters | the 12 locale landing pages (done); the hand-authored pages keep their own |
| Page description | the locale's `tagline` in `tools/site-copy.json` | 160 characters | done |
| README first paragraph | the tagline plus who it is for | - | with the README pass |
| winget `ShortDescription`, `Tags` | the tagline in the full name's context; `discoverability.wingetTags` (16, each a topic or one of `wingetOnlyTags`); `Moniker` stays `fastmediasorter` | 3 to 256 characters; 16 tags | next version PR (T-8) |
| Store description opening, features | the value sentence of 6.3, then the attributes of 6.2 | features 20 x 200 | next submission (T-8) |
| Store search terms | `discoverability.storeSearchTerms` (en-us and ru, 7 each, written into the listing file by `publishing/store/Build-ListingData.ps1`); other locales are researched per locale, never translated literally | 7 per locale | en-us and ru in the listing file since 2026-10-13; the next submission carries them (T-8) |
| Social card | one artwork, `tools/New-SocialCard.ps1`: 1200x630 (every page's `og:image`, with `og:image:width`, `og:image:height`, `og:image:alt`) and 1280x640 (the repository's social preview, under 1 MB) | - | done; the preview is uploaded in the owner's browser |

**The frozen names stay frozen.** The Store title and the winget `PackageName` keep the name they were reserved and
published under ([PRODUCT_NAMING](PRODUCT_NAMING.md), ticket 111 Q-1); a listing or directory entry explains that in
one line rather than renaming it. The words the naming standard retired, "lightweight", "lean" and the bare
abbreviation, appear in no field above (`discoverability.forbiddenWords`), and none names a competitor (ticket 207
D-7): the gate reads the researched product names from the research guide and fails when one appears on a published
surface, in a topic, a tag or a search term, or as "an alternative to" one.
