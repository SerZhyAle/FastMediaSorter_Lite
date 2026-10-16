# Product naming

The one owner of the product's names. Every other file cites a row or a rule of this one by its number and
never restates the table. The previous rename left 33 files saying the old name because the name was copied
into each of them; the cure is one table, one code module ([src/ProductNames.vb](../../src/ProductNames.vb))
and one check ([tools/Test-ProductNaming.ps1](../../tools/Test-ProductNaming.ps1)).

Decided by the owner on 2026-10-03; carried out by ticket 111 (`docs/specifications/done/111_SPECIFICATION_PRODUCT_NAME_AND_TEXT_REVISION.md`).
This supersedes, for the **visible name only**, the light rebrand recorded in
`docs/specifications/done/SPECIFICATION_RENAME_FAST_MEDIA_SORTER_FOR_WINDOWS.md`. What that archive froze as
technical anchors stays frozen (section 2).

## 1. The names

| Form | Text | Where it is used |
| --- | --- | --- |
| **Full name** | `Fast Media Sorter & Sharing for Windows` | the About page, exe and assembly properties, the installer wizard and shortcuts, README and site headings, the release title, the first mention in any document (Store descriptions use the compact form, section 6) |
| **Compact name** | `Fast Media Sorter & Sharing` | title-length and label-length places: a page `<title>`, a tray or menu item, a file-type description, the Share Manager and Server edition names. **It is not the Store name** - see the Store row of section 2 |
| **Short name (legacy)** | `Fast Media Sorter` | running prose after the first full-name mention, the Share Manager's "Open Fast Media Sorter" tray item and settings link (they open the viewer), and **the viewer's title bar (`Fast Media Sorter by SZA`; owner decision 2026-10-07: sharing is run from the Share Manager's own window, so the viewer's caption does not say "Sharing")**. Never a heading, a store field, an Add/Remove Programs entry or any other window caption |
| **Alias FMS_Win** | `FMS_Win` | internal and technical text only: specifications, contracts, tickets, code comments, support triage. Never in the UI. Owner decision 2026-10-16; it replaced `FMS_W` |
| **Alias FMS_A** | `FMS_A` | the same, for the Android product |
| **Alias FMS_Web** | `FMS_Web` | the same, for the web product (its own repository, its own release calendar) |
| **Retired alias FMS_W** | `FMS_W` | not written in new text: it reads as "Web" as easily as "Windows". It stays verbatim only where the text is a quotation or a frozen name - the shared contracts catalog (which still writes it), the `PROPOSAL-*-fmsw-*` file names there, the `fmsw-` source-id prefix on the wire, `docs/specifications/done/*` and past `CHANGELOG.md` entries |
| **Android app** | `Fast Media Sorter & Organizer` | the Android product's own display name (its in-app title and the en-US Play title), named by a Windows page only when "the Android app" is not enough. Owned by the Android repository; it does **not** take "& Sharing" (owner answer to Q-4 of ticket 111, 2026-10-07). Its ru-RU and uk-UA Play titles differ (`FastMedia: ..`) and are not ours to unify - request 113 asks which one a Russian or Ukrainian Windows page should name |
| **Alias FMS** | `FMS` | only where the platform is obvious from context; a bare `FMS` is ambiguous between the Windows, Android and web products, so it is never the only name on a page |
| **Share Manager** | `Share Manager - Fast Media Sorter & Sharing` | the separate sharing program's window, tray, shortcuts and message captions (component first, product as suffix) |
| **Server edition** | `Fast Media Sorter & Sharing: Folder Share Server` | the Server installer's wizard and shortcut text |
| **Exchange server** | `Media Exchange Server` | the connect service between share servers and clients (tickets 132 door 3, 133): described as an "exchange server". Never shortened to "Exchange Server", which reads as Microsoft's product (`tools/Test-ProductNaming.ps1` rejects a bare "Exchange Server" not preceded by "Media"); not translated and Latin in all 13 languages, only the role description translates ("сервер обмена"). Surfaces: the site's third door (text), the Share Manager entry (the settings group and the tray item, ticket 142), the installer component, the deployment guide. In code it is `ProductNames.ExchangeServer`, cited as a constant or a `TF` placeholder |

The word **"Lite" is not part of the name any more.** The product does far more than the name promised.
"Lite" survives only as a frozen identifier (section 2), and every survivor is on the allow-list of
`tools/product-naming-allow.jsonl` with its reason.

**"Sharing" has one meaning:** giving media to someone else - the phone, another copy of this program, an
SFTP client. It is not "social sharing" and not "send a link". Text about *consuming* a remote resource says
"remote resources", not "sharing".

## 2. What stays frozen

A name is changed only where it is a *name*. Where a program, a store or an installed machine correlates on a
string, the string is an anchor and no naming change touches it.

| Anchor | Value | Why it stays |
| --- | --- | --- |
| Exe names | `FastMediaSorter_LITE.exe` (mainline), `FastMediaSorter_x86.exe`, `FastMediaSorterCompanion.exe` | file associations, the MSIX `Executable`, single-instance forwarding by process name, the installed path |
| Mutexes, pipe, mDNS, IPC | `FastMediaSorterSingleInstanceMutex`, `FastMediaSorterCompanionSingleInstanceMutex`, `\\.\pipe\fms-companion`, `_sftp-fms._tcp`, the `WM_COPYDATA` tags, the Share Manager's message-window title | wire and process contracts (`WORKER-IPC`, `SHARE-SESSION`) |
| Registry | `SZA\FastMediaSorter`, ProgIDs `FastMediaSorter.*` | user settings and "default app" choices |
| Data folders | `%LOCALAPPDATA%\SZA\FastMediaSorter`, `%LOCALAPPDATA%\FastMediaSorterCompanion`, `%ProgramData%\FastMediaSorterCompanion` | caches and the TOFU-pinned host key |
| Exchange server | `fms-exchange-server.exe` (in `exchange\` beside the Share Manager), the data folders `%LOCALAPPDATA%\FastMediaSorterExchange` and `%ProgramData%\FastMediaSorterExchange`, the service `FastMediaSorterExchangeServer` (display name `Media Exchange Server`), the firewall rule `FastMediaSorter Media Exchange Server`, the stop event `Local\FastMediaSorterExchange-Stop-*` | process, service and rule correlation by name (tickets 141, 143, 144); a producer's saved pin names none of these, but an installed service and a data folder do |
| Installer identity | Inno `AppId`, `DefaultDirName` `FastMediaSorter_LITE`, `OutputBaseFilename`, the Server edition's own `AppId` and directory, `UninstallDisplayName` | upgrade in place of an installed copy |
| Microsoft Store | the listing title, the MSIX `Properties/DisplayName` and the main application's `VisualElements DisplayName`, all `FastMediaSorter LITE` | owner decision 2026-10-03: the Store name does not change, only the inscriptions inside the program, the installer, the site and the documents. The name is reserved in Partner Center and a manifest `DisplayName` must equal it character for character |
| MSIX identity | `Identity Name` `SZA.FastMediaSorterLITE` and `Publisher` | the Store's update anchor |
| Add/Remove Programs | `UninstallDisplayName` `FastMediaSorter LITE` | `winget upgrade` correlates the installed entry with the manifest `PackageName` |
| winget | `PackageIdentifier`, `PackageName`, `Moniker`, the Server package's identifier | `winget upgrade` correlation (see the open question Q-1 of the ticket) |
| Release assets | `FastMediaSorter-<version>-*`, the tag shape `vYY.M.D.HHmm` | CI templates and the winget `InstallerUrl` |
| Repository | `SerZhyAle/FastMediaSorter_Lite`, `tests/Lite.Tests`, namespace `fmsl` | the Pages URL without a redirect, history, every script path |
| History | `docs/specifications/done/*`, past `CHANGELOG.md` entries, the "formerly" notes | an archive records what was true then |

## 3. Rules that follow from the form of the name

1. **`&` is part of the name.** It is escaped per medium and never replaced by "and": `&amp;` in HTML and
   XML (manifest, sitemap), `&&` in a WinForms `Label`, `Button` or menu text (or `UseMnemonic = False`; a
   `Form.Text`, a `NotifyIcon.Text` and a `MessageBox` need nothing), `&&` in an Inno label, plain in YAML
   scalars, CSV, JSON and Markdown. A file name, a URL or an identifier never contains it.
2. **The name is not translated and is written in Latin in all 13 languages**, including `ar`, `hi`, `bn`,
   `ur` and `zh`. Only the words around it are translated. In right-to-left pages the name sits in a
   left-to-right run (`dir="ltr"` or `<bdi>`), or the `&` visually swaps sides.
3. **Length budgets.** `NotifyIcon.Text` holds 63 characters (the full name is 39). A page `<title>` is about
   60 characters before a search result truncates it, so a long topic uses the compact form:
   "topic - Fast Media Sorter & Sharing".
4. **No "Lite" in any case or script in visible text.** The adjective "light" is a different word, but the
   product is no longer described as small or reduced, so "lean" and "lightweight" claims are reworded where
   a customer reads them.
5. **In code**, use the constants of `ProductNames.vb`; a string that can carry the name takes a placeholder
   (`TF("... {0} ...", ProductNames.Full)`) instead of spelling it.

## 4. How to write a sentence about the product

- The first mention in a document is the full name. After it, "the app", "the viewer" or the short name.
- Say the platform when it matters: "the Windows app", "the Android app". Do not use `FMS` alone in user text.
- A text for a customer never uses `FMS_Win`, `FMS_A` or `FMS_Web`; a specification, a contract pointer or a code comment may. Never write `FMS_W` in new text (see the retired alias row of section 1).
- The Share Manager is "the Share Manager" after its first full mention.
- Explain "Sharing" once on the first screen of a page that carries the name: what it gives, to whom, and where
  to read how (`how-to-sftp.html`, `publish-folders-android.html`).

## 5. The gate

`pwsh -NoProfile -File .\tools\Test-ProductNaming.ps1` scans the tracked files for a forbidden form
(`FastMediaSorter LITE`, `Fast Media Sorter LITE`, `Fast Media Sorter Lite`, the previous full name
`Fast Media Sorter for Windows`, a bare `LITE` or `Lite` word), for the exchange-vocabulary forms of
section 6, and for a required form that is missing - each time skipping what no allow-list record covers.
It ends in the shared one-line verdict (`CHECK-VERDICT`). An allow-list record
without a reason is itself a failure.

## 6. The exchange and live-broadcast vocabulary

Specification 206 section 3. One row per concept; cite a row, never restate it. The words are carried once
per language in the localization table of the ticket that introduces the surface - never translated ad hoc
in a second place.

| Say | Never say |
| --- | --- |
| **Live broadcast** (a device capturing now) | "Broadcast" alone, "stream", "transmission" for the producer concept. The Share Manager's "Broadcasts for StreamsPlayer" (196, 159) is a different thing and keeps its name |
| **Exchange account**, **My devices**, **Add a device**, **Pairing code** | "cloud", "sync", "login to Fast Media Sorter" |
| **Media Exchange Server** (unchanged, Latin, not translated; never shortened - see the row in section 1) | "relay server", "FMS server" in a visible string |
| **Publish my shares**, **Attach**, **Cast** / **Play on another device** | "upload my folders" (nothing is uploaded) |
| **Google Drive**, named, whenever it is meant | "the cloud" |
| **Connection string**, **Add from an exchange server**, **Get connection string**, **Sign out on this device** (226, 233) | calling a connection string a secret - it is an address with the certificate pin, carrying no password, token or pairing code |

The dictionary owner is `DEVICE-EXCHANGE` 1.1 section 2 items CV and CW and section 3 item CX (S4219; it was section 25 items CV-CZ at 0.27, before the renumbering). This section
cites its entries; it does not define another translation table. The imported conformance fixture is
`tests/Lite.Tests/TestData/exchange-terms/dictionary.json`; the shared runtime table is
`src/Localization/Localization.DeviceVocabulary.vb`, linked into the Share Manager.

| Concept | Dictionary reference and use |
| --- | --- |
| Exchange server and direct access | DEVICE-EXCHANGE section 2 item CW, corresponding entries; distinguish the role from the component name in section 1 |
| Google Drive path | DEVICE-EXCHANGE section 2 item CW, Drive channel; full wording where it fits, short wording only for a constrained label |
| Server fan-out | DEVICE-EXCHANGE section 2 item CW, relay; internal terminology only, never a visible string |
| Live broadcast and a ready file | DEVICE-EXCHANGE section 2 item CW, broadcast, broadcasting and ready-file entries; the stored-film feature keeps its StreamsPlayer label |
| Cast | DEVICE-EXCHANGE section 2 item CW, both cast entries; the destination distinguishes this from Google Cast |
| Server clipboard and its four operations | DEVICE-EXCHANGE section 2 item CW, clipboard entries; personal to the account, distinct from the system clipboard |
| Invitations and a password link | DEVICE-EXCHANGE section 2 item CW, invitation, both invitation kinds and public-link entries |
| Device sign-out | DEVICE-EXCHANGE section 9 item CZ; specification 226's device-local sign-out wording |

Specification 233 extends the existing naming gate with `tools/product-vocabulary.json`. It rejects
relay, shared-clipboard wording and mobile product descriptors in visible copy; mobile internet and
mobile data remain valid. Internal identifiers, wire values and engineering text retain their meaning.
The older vocabulary rules above still apply. Microsoft Store descriptions use the compact product
name without the platform suffix; the listing title and MSIX DisplayName remain frozen (section 2).
