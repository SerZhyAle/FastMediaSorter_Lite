# Positioning

The one source every external surface is written from: the landing and its twelve locale pages, the server page, the three authored READMEs, the Store listing and the winget descriptions. No surface is written from another
surface; where two disagree, this file decides which one is wrong (`SITE-REPRESENTATION` rules 1 and 2, tickets 155, 249 and 273).

The machine form of everything below is [tools/site-facts.json](../../tools/site-facts.json), and
[tools/Test-SitePositioning.ps1](../../tools/Test-SitePositioning.ps1) holds the surfaces to it. This file says it
in prose and in the order; the data file carries the same values for a program to read. The two are compared by the
gate, so a change starts here and in the data file, and only then in a surface.

## 1. The statement

Every surface that says what the product is leads with this sentence, in the reader's language (the product name is
kept, [PRODUCT_NAMING](PRODUCT_NAMING.md)):

> Three optional parts: a media and file manager that views and sorts photos and videos with one key and manages files in local folders, on servers and on connected devices; a local sharing and scheduling service that shares folders over SFTP, runs file operations on a schedule and connects this PC to an exchange account; and a Media Exchange Server that you run yourself, through which your devices reach each other - start with the manager or with the folder-sharing server, and add the rest only if you want it.

The wording was decided by the owner on 2026-10-26 (specification 273, decisions Q273-1 to Q273-8). It replaces the
sentence of specification 249, which still called the first part a viewer and sorter and the second a folder-sharing
service only; the tree had outgrown both (specification 273 section 2). The Russian and Ukrainian forms are the
`ru.family` and `uk.family` of [tools/site-copy.json](../../tools/site-copy.json).

**The cross-link blurb.** A sibling product's page that links here copies one line verbatim beside the link, in the
reader's language: EN *Media and file manager for Windows: sort photos and videos with one key*. The 13 forms are
`positioning.blurb` of the data file (at most 80 characters each, the product name kept outside the line).

**Every surface states facts in the present tense** (canon invariant 17, owner ruling of 2026-10-26). No surface
carries a promise marker of any kind, in any language, and no surface names what is only planned. The words that
were used for that, together with the definitions the product outgrew, are `retiredPhrases` of the data file; the
gate fails on each of them in every scanned file, case-insensitively. History is not rewritten: the release notes and
`whats-new.html` keep what each release said.

## 2. The three components, in order

A surface that lists what the product does lists these in this order. A surface too short for all of them names the
first ones. Each part is independently optional: the manager is not a prerequisite of the server and a server is not a
prerequisite of the manager. No store policy asks for a different first function here, so there is no exception.

| # | Component | What it is | Availability |
| --- | --- | --- | --- |
| 1 | **Media and file manager** | The viewer program: fast paging through huge folders of photos and video, one key per file (move, copy, rename, delete), slideshow, playback, archives opened like folders, offline OCR and translation; and a file manager - the Grid panel's file manager mode (F3) - over local drives, SFTP servers and SMB shares with a login, and the devices of an exchange account, with queued file operations. Documents in the sorting loop are shown as previews: text as text, PDF pages, the cover of an EPUB, any other file as a card. | Shipped in every package. Always installed by the full package; an optional, unticked component of the Server edition. |
| 2 | **Local sharing and scheduling** | The Share Manager and its SFTP worker: the folders you pick open on an Android phone, another copy of the program or any SFTP client, over your own network, paired by QR code; file operations (copy, move, delete, archive) run on a timetable; this PC joins an account on an exchange server, with the account's devices as places, the server clipboard and end-to-end encryption; live broadcast of a screen, a window, a camera or audio. Hosting FileDO `.fdd` virtual disks is a capability of this component, not a part of its own. | Shipped, optional, off until you switch it on. Included (ticked in the Full type) in the User installer, winget and the ZIP, as a second application of the Store package, and as the whole point of the Server edition. |
| 3 | **Media Exchange Server** | A separate server you run yourself: your devices enroll into an account you create on it and reach each other through it when neither can be reached directly; it holds accounts, devices, invitations and limits, and is administered in the browser. It is not folder sharing, it is not a public relay anybody hosts for you, and neither the manager nor local sharing needs it. | Not in this package, in any channel: its shipping gate is shut (state `gated`). |

What the table never says: that a browser can open SFTP, that anybody hosts a public relay for the user, that an
account has to be registered to use the manager or local sharing, or that the manager reads documents, e-books or
texts (the shipped previews are previews; reading is `planned`, section 2.2). Those are not consequences of the
three-part model.

### 2.1 A gated component on the surfaces

The Media Exchange Server is built and tested; its shipping gate (`INCLUDE_EXCHANGE` of the release workflow,
`/DNoExchange` in the packagers, the exchange guard of the Store packager) is shut, so no package carries it. On the
surfaces it is what it does, nothing more:

- it is named wherever the components are listed and described in the present tense, as working;
- it carries no promise marker, no date, no download, no installer name, no winget command and no store address, and
  it is never inside a link to any of those; no sentence tells the reader to download or install it;
- its door on the landing carries no call to action;
- where a packaging cell has to be filled (the matrix of section 4.1, a "where it comes from" cell of a README), the
  only words are the matrix label for `absent`: "Not in this package" in the reader's language.

The gate holds all four (the `gated` block of the data file). The packaging truth is checked against the packaging
files: when a release opens the shipping gate, the matrix cells of the channels that carry it change from `absent`, in
the same edit as the gate's evidence.

### 2.2 The capability register

What each component can do is typed once, in the `capabilities` block of the data file: one row per capability, with
its component, its state, the release that brought it (`since`, a version heading of `CHANGELOG.md`), its evidence
(a specification or a file) and the surface classes that may name it. The states:

| State | Means | On the surfaces |
| --- | --- | --- |
| `released` | a published release carries it | named as what it does; a coverage set (`mustName`) that must name it is held by the gate |
| `built` | in the source tree, in no published release yet | named only where the release that carries it is published |
| `gated` | built, held out of every package or offer by a shut switch | the rules of section 2.1; a capability of a gated component is never `released` |
| `development` | being built | named on no surface |
| `planned` | tickets only, nothing in the tree | named here (class A) only, never on a surface; its `plannedClaims` fail the gate wherever they appear outside an allowed context |

The surface classes: **A** this file, the data file, [PRODUCT_NAMING](PRODUCT_NAMING.md) and the termbase; **B**
internal and agent documents; **C** code-borne text (localization, About, the wizard, the installers); **D** help;
**E** the site; **F** channel listings, packages and the READMEs; **G** the canon's card of this product; **H**
sibling products' pages.

| Component | Released | Gated | Planned |
| --- | --- | --- | --- |
| Media and file manager | view and sort with one key (with slideshow, playback, archives as folders, editor, OCR and translation); SFTP servers and SMB shares opened like folders; the file manager mode of the Grid panel; secure `.fd-sec` containers; document previews | - | reading documents, e-books and texts |
| Local sharing and scheduling | folder sharing over SFTP with QR pairing and FileDO hosting; scheduled file operations; the Server edition; live broadcast; the exchange account | guest invitations and password links of an exchange account; the Google Drive channel | - |
| Media Exchange Server | - | the server itself: accounts, devices, invitations and registration, limits, service management, administration in the browser | - |

`built`, in no published release yet: the optional HTML page viewer of the media and file manager (specification
275, row `html-pages`) and its optional full PDF and EPUB reader (specification 274, row `document-reader`) - each
named in classes A to C only until the release that carries it is published.

The coverage sets: the three READMEs and the landing page (in English, Russian and Ukrainian) name every `released`
capability whose row lists them. A released capability a coverage set does not name fails the gate, naming the file
and the language.

## 3. Two install intents, and the public editions

The three components are not three downloads and not three editions. A visitor arrives with one of two intents and
each has one entry page; each entry page links the other in one sentence and does not bury its own path in a catalogue
of downloads.

| Intent | The visitor wants .. | Entry page | Obtained as | What gets installed | The manager |
| --- | --- | --- | --- | --- | --- |
| **Manager first** | to sort and look at photos and videos and manage files across places | the landing page, the Store listing and the READMEs | the full package: the User installer, the portable ZIP, winget `SerZhyAle.FastMediaSorter`, the Microsoft Store | the manager; local sharing and scheduling as an optional part that is off until switched on (left out by the Compact type) | always installed |
| **Server first** | a machine that stays reachable with nobody signed in | the Server edition page, [server.html](../../server.html) | the Server edition: its own setup.exe and winget `SerZhyAle.FastMediaSorter.Server` | the folder-sharing service as a Windows service, plus FileDO hosting (ticked, optional); the Media Exchange Server is not in this package | optional, unticked; the package is complete without it |

The machine ids of the two intents stay `viewer-first` and `server-first` (the `profiles` block): ids, not wording.

A visitor can obtain two editions. Each has one display name, owned by [PRODUCT_NAMING](PRODUCT_NAMING.md) section 1,
shown the same on every page.

| Edition | Display name | Obtained as | Leads with |
| --- | --- | --- | --- |
| **The full package** | `Fast Media Sorter & Sharing for Windows` | the User installer, the portable ZIP, winget, the Microsoft Store; carries the manager, the Share Manager and both viewer exes (the x64 mainline and the x86 fallback) | component 1; local sharing and scheduling is an optional part, the exchange server is not in this package |
| **The Server edition** | `Fast Media Sorter & Sharing: Folder Share Server` | its own setup.exe and its own winget package | component 2 - the folder-sharing server leads and the manager rides along as an unticked component (specification 132 section 3.3); this is a focused package, not a second order of the components |

The Server edition's own page and manifest lead with the server because that is the package's job. They are
declared in the data file (`focusedEditions`), judged for the family sentence where they repeat it, and not for
component order beyond it. A "Server edition" installs the *folder-sharing* server; it is never called the exchange
server, and the two are never described as one thing.

## 4. Facts typed once

A fact has one source, and a page that states it is compared with that source, never with another page
(`SITE-REPRESENTATION` rule 3).

| Fact | Value | Source of truth | Held by |
| --- | --- | --- | --- |
| Interface languages | 13 | `Localization.Codes` in `src/Localization/Localization.vb`, and the locale keys of `tools/site-copy.json` | the gate reads both and the data file; every `N languages` of a scanned file must equal them |
| Platform floor of the program | Windows 10 version 1607 (build 14393) for the x64 mainline, the installer's Share/FileDO/PDF components and the Server edition; Windows 7/8.1 and 32-bit Windows run the x86 fallback viewer only | `UseModernExe` and the component `MinVersion` lines of `publishing/installer/FastMediaSorter.iss`, `MinVersion` of `FastMediaSorterServer.iss`, `MinimumOSVersion` of the Server winget manifest | the gate reads them against the data file; every `Windows 10 <version>` of a scanned file must be a version the data file lists |
| Platform floor of the Store package | Windows 10 version 1809 (build 17763) - a Store-package floor, not the program's | `TargetDeviceFamily MinVersion` of `publishing/msix/AppxManifest.xml` | the gate reads it; the data file lists 1809 as the one other version a page may name |
| Component availability per channel | the matrix of section 4.1 | the installer scripts, the winget manifests, the MSIX manifest and packager, `release.yml` | the gate reads each cell's evidence from those files; `Build-SitePages.ps1` renders the matrix into `server.html` from the data file |
| What each component can do | the register of section 2.2 | the `capabilities` block, each row with its `since` release in `CHANGELOG.md` and its evidence | the gate (section 7 of the script): states, releases, evidence, coverage, planned claims |
| Names | the table of [PRODUCT_NAMING](PRODUCT_NAMING.md) | that table | `tools/Test-ProductNaming.ps1` |
| Release version | resolved at run time; no page types it - the one exception is the generated `whats-new.html`, whose version headings are rendered from `CHANGELOG.md`, never retyped | `src/My Project/VersionInfo.vb` and the tag | `tools/Test-ArtifactVersion.ps1`; `tools/Build-SitePages.ps1 -Check` for the page |

The channels (GitHub Releases, winget, the Microsoft Store) are named in `tools/site-copy.json` `_channels` and
carry their addresses in `tools/site-addresses.jsonl`; the address gate holds them.

### 4.1 Availability: component by channel

Component, install profile and channel are three separate facts; the matrix is their product. It is typed once in the
`availability` block of the data file, every cell with the files that prove it, and rendered from there. The
states: **core** (always installed, cannot be unticked), **bundled** (in the folder or package, off until you switch it
on, no choice at install), **optional, ticked** (offered, ticked in the default type), **optional, unticked** (offered,
left out unless you tick it), **required** (the package is this) and **absent** (not in this package). A gated
component is `absent` in every channel.

| Component | Installer (.exe) | Portable ZIP | winget | Microsoft Store | Server setup (.exe) | winget (Server) |
| --- | --- | --- | --- | --- | --- | --- |
| Media and file manager | core | core | core | core (x64 mainline only) | optional, unticked | optional, unticked |
| Local sharing and scheduling | optional, ticked in Full; Compact leaves it out | bundled | optional, ticked (Full) | bundled (a second application, in your session) | required | required |
| FileDO hosting (a capability of sharing) | optional, unticked | bundled | optional, unticked | absent | optional, ticked | optional, ticked |
| Media Exchange Server | absent | absent | absent | absent | absent | absent |

| Fact per channel | User installer, ZIP, winget | Microsoft Store | Server edition (setup and winget) |
| --- | --- | --- | --- |
| Where sharing runs | the Share Manager, in your session; it can hand sharing to a Windows service later | the Share Manager, in your session; no Windows service (a Store package cannot register one) | a Windows service that starts with Windows, nobody signed in |
| To install | no administrator rights (per user) | the Store | administrator rights |
| To switch sharing on | one approval for the firewall rule | none: the package declares the firewall rule of its worker | the installer already did it |
| Windows floor | 10 1607 for sharing; Windows 7/8.1 and 32-bit get the viewer only | 10 1809 | 10 1607, 64-bit |

The default of every shipped optional part is *off at run time*: nothing listens, no firewall rule exists and no
service is registered until the user asks. Reading the table the other way round: choosing the manager needs no
server, and choosing the server needs no manager.

The User installer script defines the Media Exchange Server as a child of local sharing (`share\exchange`, unticked),
compiled in only when the shipping gate is open, because the Share Manager is what starts it (specification 251 T-1).
Setup then ticks the Share Manager together with it, also for an unattended `/COMPONENTS=..share\exchange`. The Server
edition keeps a flat `exchange` component: its sharing service is always installed.

**What the matrix is evidence of.** It describes the packages as **defined in this source tree**: the installer
scripts, the winget manifests, the MSIX manifest and packager and the release workflow, which the gate reads. It is
not a statement about a release already published; a page that says what a particular download contains cites the
version that brought the part (the What's new page, the `since` of section 2.2), and a part reaches a channel only
when the release that carries it is published (the Store: when it passes certification). The Media Exchange Server is
`absent` in every channel because its shipping gate (`INCLUDE_EXCHANGE`) is shut.

## 5. Device classes

Desktop only: Windows PCs and Windows Server. There is no phone, tablet or TV edition of this product; the Android
program is a different product with its own site.

## 6. The worksheet - what sections 1 to 5 do not say

Sections 1 to 5 are the worksheet's published half: the statement, the components, the install intents and editions,
the typed facts. This section is its other half, written once so that every discoverability field of section 7 is
derived from it and not from a mood (canon `PROMOTION` section 2, ticket 207 D-3). It never names a competitor's
product: the generic alternative is named, and the products researched stay in the internal research guide
([RESEARCH_COMPETITOR_FEATURES.md](RESEARCH_COMPETITOR_FEATURES.md)), which is also where the gate reads them from.

### 6.1 Competitive alternatives - what a person does today without this product

| Alternative | Why it falls short for the job |
| --- | --- |
| The Photos app of Windows plus File Explorer drag and drop | Every file is a drag between two windows, there is no one-key destination, and a video and a photo do not page together with a destination list beside them. |
| A classic image viewer used as a sorter (generic "image viewers") | Strong at looking, weak at filing: moving a file is a menu or a second program, and video is another tool altogether. |
| A photo catalogue or asset manager | It imports and indexes first; the job here is to finish one folder in one sitting without importing anything. |
| A classic file manager plus a separate scheduler | Local, server and device places live in different tools, and a recurring copy or clean-up is a script or a task the user writes by hand; here the same places open in one panel and a file operation is put on a timetable in the Share Manager. |
| For component 2: a cloud sync app, a USB cable, a messenger | The folder has to be uploaded or copied before the phone sees it; here the phone opens the PC's folder directly, over the local network or SFTP, with nothing leaving the machine. |

No real user report exists yet (the issue tracker has been empty since the project started), so no alternative here is
issue-sourced; they are the situations the product was built against and the research guide of 2026-06-21 recorded.

### 6.2 Unique attributes - each with its pointer

An attribute without a pointer is cut.

| Attribute | Pointer |
| --- | --- |
| One key per file: a digit moves, Shift + the digit copies, chosen at the moment of the action | README, the "Move or copy, decided as you go" bullet; `Main_Form.ExecuteRecipientAction` |
| Photos and videos in one sorter, video played offline through the built-in LibVLC engine | README "What the 64-bit program adds"; `src/Main_Form.VideoPlayer.vb` |
| Archives (ZIP, CBZ, 7z, RAR) and remote SFTP folders and SMB shares open like ordinary folders | `src/Archives/`, `src/Resources/`; [how-to-archives.html](../how-to-archives.html), [how-to-resources.html](../how-to-resources.html) |
| One file manager over local drives, SFTP and SMB resources and the devices of an exchange account, with queued file operations | `src/Image_Panel_Form.FileManager.vb`, `src/FileManager/`; specification 257 |
| File operations on a timetable (copy, move, delete, archive), set up in the Share Manager and reached from the viewer's folder menu | specifications 222 and 231; the Share Manager's scheduled operations page |
| This PC as a device of an exchange account: the account's devices as places, the server clipboard, end-to-end encryption | specifications 224, 226 and 234; `src/Exchange/` |
| Offline OCR (Tesseract) with optional on-image translation by a local model or a server the user chooses | `src/Ocr/`, `src/Translate/`; [how-to-ocr.html](../how-to-ocr.html) |
| Optional folder sharing to an Android phone over SFTP, paired by QR code, off until switched on | the Share Manager; contract `FMSCFG`; [publish-folders-android.html](../../publish-folders-android.html) |
| Thirteen interface languages, three of them proofread | `Localization.Codes` in `src/Localization/Localization.vb`; the typed fact of section 4 |
| Free, MIT-licensed, no telemetry | `LICENSE`; [privacy.html](../privacy.html) |

### 6.3 Value - the job, in the user's words

When a camera or phone dump leaves thousands of mixed photos and videos in one folder, I want to look at each once and
send it to its place with one key, so I can finish the folder in one sitting instead of dragging files for an evening.

When my files live on this PC, a home server and my other devices, I want to reach them all from one window and let
the recurring copies and clean-ups run on their own, so I stop doing the same file chores by hand.

Component 2: when I want this PC's folder on my phone, I want to scan a code once, so I can open it from the phone
without a cloud in between.

### 6.4 Target persona

Ordinary Windows users with large personal photo and video folders - the product compass's default. The second
persona: the same kind of person who keeps files on several machines - a PC, a home server or NAS reached over SFTP or
SMB, an Android phone - and wants them in one place with the chores on a schedule. No technical user is the persona;
a technical list may lead with SFTP, HEIC and AVIF decoding or offline OCR because that is what that audience
searches for, never the site, the Store or winget (ticket 207 D-4).

### 6.5 Market category

**Media and file manager for Windows** (specification 273). The words people already search for stay as keywords and
synonyms: photo sorter, image viewer, video player, file manager, photo organizer (`discoverability.synonyms`, the
topics, the winget tags and the Store search terms). The category noun is format-neutral, so it needs no change when
a further file kind is handled.

### 6.6 The positioning statement (internal, never published verbatim)

For ordinary Windows users with large photo and video folders, and files spread over several machines, Fast Media
Sorter & Sharing is a media and file manager that sends each photo or video to its place with one key and manages
files in local folders, on servers and on connected devices. Unlike dragging files between Explorer windows and
writing scheduled tasks by hand, it pages through the whole folder, plays the video in the same window, puts the
destinations on the keyboard, and runs the recurring file operations on a timetable.

## 7. Derived fields

Every discoverability field of every channel is written from section 6 and the family sentence, and its machine form
is the `discoverability` block of [tools/site-facts.json](../../tools/site-facts.json). A field changes here and in
that block first, then in the channel. `tools/Test-SitePositioning.ps1` holds the block (limits, forbidden words, no
competitor name) and the channels whose fields are already in the tree; the others are held when the step that writes
them lands (ticket 207 T-8) and are listed in the block's `applied`.

| Field | Value | Limit | Applied |
| --- | --- | --- | --- |
| Category | media and file manager (synonyms: photo sorter, image viewer, video player, file manager, photo organizer) | - | the base of every field below |
| Tagline | Sort photos and videos with one key per file - the first unique attribute, kept | 6 to 10 words | the social card; README and Store at T-8 |
| Cross-link blurb | `positioning.blurb`, 13 languages (section 1) | 80 characters | sibling pages, through the hand-off packets of specification 273 T-9 |
| GitHub description | category noun first, the main jobs, the compact name, Free, MIT - the exact text is `discoverability.githubDescription` | 350 characters; no em or en dash | live repository metadata: ticket 207 T-2, after the owner's go |
| Homepage | `https://serzhyale.github.io/FastMediaSorter_Lite/` (the site, not the repository) | - | T-2 |
| Topics | `discoverability.topics` (18): the category and its synonyms, the platform, the formats and engines the product really handles | 20; 50 characters each; lowercase and hyphens | T-2 |
| Page title | the job in the page's language, then the compact name: `{pageTitle} \| Fast Media Sorter & Sharing` (the separator the landing and the not-found page already use); `pageTitle` per locale in `tools/site-copy.json` | 60 characters | the 12 locale landing pages (done); the hand-authored pages keep their own |
| Page description | the locale's `tagline` in `tools/site-copy.json` | 160 characters | done |
| README first paragraph | the family sentence, then the tagline | - | with the README pass |
| winget `ShortDescription`, `Tags` | the category noun in the full name's context; `discoverability.wingetTags` (16, each a topic or one of `wingetOnlyTags`); `Moniker` stays `fastmediasorter` | 3 to 256 characters; 16 tags | next version PR (T-8) |
| Store description opening, features | the family sentence and the value sentences of 6.3, then the attributes of 6.2 | features 20 x 200 | next submission (T-8) |
| Store search terms | `discoverability.storeSearchTerms` (en-us and ru, 7 each, written into the listing file by `publishing/store/Build-ListingData.ps1`); other locales are researched per locale, never translated literally | 7 per locale | en-us and ru in the listing file since 2026-10-13; the next submission carries them (T-8) |
| Social card | one artwork, `tools/New-SocialCard.ps1`: 1200x630 (every page's `og:image`, with `og:image:width`, `og:image:height`, `og:image:alt`) and 1280x640 (the repository's social preview, under 1 MB) | - | done; the preview is uploaded in the owner's browser |

**The frozen names stay frozen.** The Store title and the winget `PackageName` keep the name they were reserved and
published under ([PRODUCT_NAMING](PRODUCT_NAMING.md), ticket 111 Q-1); a listing or directory entry explains that in
one line rather than renaming it. The words the naming standard retired, "lightweight", "lean" and the bare
abbreviation, appear in no field above (`discoverability.forbiddenWords`), and none names a competitor (ticket 207
D-7): the gate reads the researched product names from the research guide and fails when one appears on a published
surface, in a topic, a tag or a search term, or as "an alternative to" one.
