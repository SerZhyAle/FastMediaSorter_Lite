# Publishing Fast Media Sorter & Sharing for Windows to the Microsoft Store (MSIX)

The concrete, app-specific playbook for this repo. Adapted from the reusable CyrFlip playbook
(`P:\WINDOWS\CyrFlip\STORE_PUBLISHING.md`). Work top to bottom.

> **Already published?** This doc is the **first** publish. To ship a change to the live listing
> (new screenshots, search terms, or a new build), follow
> `SPECIFICATION_STORE_UPDATE.md` (local specification) instead.

## Why this path (Path A: MSIX)

- **Developer account is free** (individuals since late 2025, companies since May 2026).
- **Microsoft re-signs the MSIX during certification** - no paid code-signing certificate needed.
  (The alternative "unpackaged exe/MSI" path *does* require a paid cert chaining to a Microsoft-trusted root.)
- Store-signed + Store-distributed also defuses antivirus heuristic false positives better than anything else.

This is **in addition to** the existing distribution channels (GitHub release EXE/ZIP and winget) -
nothing here changes those. See `SPECIFICATION_WINGET_PUBLISHING.md` (local specification)
and `SPECIFICATION_GITHUB_STORE.md` (local specification) for those paths.

---

## Phase 1 - Make the app MSIX-ready (code)

**Already done - no code change required.** MSIX runs the desktop app in a light container with
file/registry virtualization and a **read-only install dir**. FastMediaSorter already plays nicely:

| Concern | Why it could break under MSIX | Status in this app |
| --- | --- | --- |
| Writing downloaded `tessdata` / OCR cache | a write next to the exe (install dir) fails - it's read-only | Already writes to `%LOCALAPPDATA%\SZA\FastMediaSorter` (`OcrPaths`, `src/Ocr/TesseractOcrEngine.vb`). OK. |
| Settings persistence | `HKCU` is virtualized | App stores settings in the registry; per-package virtualization is fine (settings just live with the package). OK. |
| Bundled `tessdata` next to exe | read-only install dir | Only **read**, never written. OK. |
| File associations | a packaged `HKCU\Software\Classes` write is virtualized/ignored | Declared in the manifest as `windows.fileTypeAssociation` instead of registry writes. OK. |
| Win32 APIs (LibVLC, GDI+, file access, local HTTP) | restricted in a pure UWP container | `runFullTrust` keeps them all working. OK. |
| .NET runtime on the machine | a packaged app cannot run an installer for a prerequisite | The packaged viewer is **self-contained .NET 10** - the runtime rides inside the package, nothing to install. OK. |

**Rule of thumb:** anything that writes to the install dir, or to `%LOCALAPPDATA%`/`HKCU` and must be
visible *outside* the package, would need an MSIX-aware path. FastMediaSorter has none of those cases.

**What the Store package contains (since the .NET 10 build):** the viewer ships as **two exes side by
side** in the GitHub/winget channels - `FastMediaSorter_LITE.exe` (x64, .NET 10, the mainline) and
`FastMediaSorter_x86.exe` (32-bit net48, for Windows 7/8.1 and 32-bit machines). The **MSIX is x64-only
and carries the mainline alone**: the manifest declares one viewer `<Application>`, the Store gates by
architecture, and every machine that can install from the Store can run the mainline. `build-msix.ps1`
excludes the x86 sibling from the staged payload. The package's second `<Application>` is the
unrelated Share Manager companion. The mainline no longer hosts the IE WebBrowser control at all
(video is LibVLC-only), which removes a dependency on a Windows component that is being retired.

---

## Phase 2 - Packaging artifacts (in this repo)

| File | Role |
| --- | --- |
| [publishing/msix/AppxManifest.xml](../../publishing/msix/AppxManifest.xml) | Manifest: Identity placeholders, `runFullTrust`, image file associations, visual assets. |
| [publishing/msix/build-msix.ps1](../../publishing/msix/build-msix.ps1) | MSBuild → **`dotnet publish` the .NET 10 viewer** → version remap → stage offline payload (x86 sibling excluded) → generate logos → fill manifest → `makeappx pack` → optional self-sign. |
| [publishing/msix/README.md](../../publishing/msix/README.md) | Build/submit instructions. |
| [assets/icons/store-icon-256.png](../../assets/icons/store-icon-256.png) | 256px logo master the build script scales into Store tiles. |
| [publishing/store/make-screenshot.ps1](../../publishing/store/make-screenshot.ps1) | Produces a ≥1366×768 Store screenshot. |
| [docs/privacy.html](../privacy.html) | Privacy-policy page (host on GitHub Pages → URL for the listing). |

**Build gotcha (important, since the .NET 10 build):** **`msbuild` alone no longer produces
`FastMediaSorter_LITE.exe`** - it builds only the net48 x86 sibling (`FastMediaSorter_x86.exe`). The
mainline exe is born **only** from `dotnet publish src\Modern\FastMediaSorter.Modern.vbproj -c Release
-r win-x64` (self-contained, single-file). `build-msix.ps1` runs that publish itself, then takes both
the exe **and the version to remap** from the published output - so `-NoBuild` still skips only the
MSBuild step (which supplies the shared support trees: LibVLC, tessdata, flags), never the publish.

**Version gotcha (important):** the Store requires a 4-part version with the **revision = 0**
(`Major.Minor.Build.0`), each part ≤ 65535. `build-msix.ps1` remaps the app's `YY.M.D.HHmm` stamp to
`YY.(M*100+D).HHmm.0` - monotonic over time, unique per minute. (e.g. `26.6.13.0016` → `26.613.16.0`.)

Tooling: the Windows SDK (provides `makeappx.exe` + `signtool.exe`), MSBuild, and the **.NET 10 SDK**
(the mainline viewer and the Companion are both published with `dotnet publish`):
```powershell
winget install Microsoft.WindowsSDK.10.0.26100
winget install Microsoft.DotNet.SDK.10
```

---

## Phase 3 - Verify locally before uploading

```powershell
cd publishing\msix
.\build-msix.ps1 -SelfSign            # -NoBuild reuses the current bin\Release; the dotnet publish still runs
# prints two commands: Import-Certificate (run as admin) + Add-AppxPackage
```
Then trust the printed cert and `Add-AppxPackage` the `.msix`. Test: open a folder of media, navigate,
slideshow, play an MP4 **and** an AVI/MKV (both go through LibVLC now - there is no IE path left), open
a **static and an animated `.webp`** (Magick.NET decodes them in-process; no Windows codec involved),
run OCR translate (`T`), and set the app as a default image handler from *Settings ▸ Apps ▸ Default apps*.

Pitfalls hit in practice:
- `Square310x310Logo` requires a paired `Wide310x150Logo` - this manifest ships only the small/medium
  tiles (44/71/150) + StoreLogo, so it sidesteps that.
- `Add-AppxPackage` **installs but does not launch** - start it from the Start menu.
- The path-independent single-instance mutex makes the packaged copy exit if a dev/Release copy is
  already running. Close other copies when testing.

---

## Phase 4 - Partner Center: account + identity

1. **Account settings → Programs → Windows → Get started** (NOT "Windows Desktop Applications" - that
   one is telemetry for EV-signed Win32 apps). Registration is free.
2. **Create a new product → MSIX or PWA app** → reserve the app name **FastMediaSorter LITE**
   (or another available variant). The manifest `DisplayName` must equal the reserved name character
   for character, so it stays `FastMediaSorter LITE`: the Store name does not follow the product's
   renamed inscriptions (owner decision, 2026-10-03).
3. **Product ▸ Product identity** → copy the values into [publishing/msix/identity.json](../../publishing/msix/identity.json),
   the product's one machine-readable Store identity record:
   - `Package/Identity/Name` → `IdentityName`
   - `Package/Identity/Publisher` (e.g. `CN=…`) → `Publisher`
   - `Package/Properties/PublisherDisplayName` → `PublisherDisplayName`

   The file also holds the product's `StoreId`, `PackageFamilyName` and `IarcRatingId`. The build script reads it and stops
   before it builds when it is missing or incomplete.
4. Build the Store package (no `-SelfSign`) and upload the **unsigned** `.msix`:
   ```powershell
   cd publishing\msix
   .\build-msix.ps1 -ReleaseVersion "<the tag's YY.M.D.HHmm>"
   ```
   Pass `-ReleaseVersion` whenever the package belongs to a release: without it the projects stamp
   the current minute, so a package built an hour after the tag carries a different version than the
   GitHub and winget artifacts of the same release. Omit it only for a throwaway local build.

   The upload is a manual step in Partner Center; this repository's release flow starts no submission. The Store Developer
   CLI and the Store submission API give a read-only view of the live product: `msstore apps list`,
   `msstore submission status <StoreId>` and `msstore submission get <StoreId>` (the Store ID is in `identity.json`).
   With no pending submission `submission get` returns the last published one, so read its `Status`, never the mere
   existence of JSON. A write through the channel (publish, update, delete, rollout) is made only on the owner's
   instruction that names the product and the package version; a submission created in Partner Center is changed only in
   Partner Center. No account identifier or credential is written into this repository.

   Before the tag, `tools\Release.ps1` (step 7) calls the shared read-only Store preflight of the canon (`WINDOWS-STORE` 0.4
   section D, rules 21 and 23): the release stops with the reason when a Store submission is already pending, when the package
   version is not strictly above the live one, or when the price is not `Free`. With the channel unconfigured, its credential
   expired or its token refused, the release prints one line that the Store step is manual and goes on exactly as without the
   channel. After the tag is pushed, one evidence line per release (the Store ID, the package version and the preflight's
   answer, no account identifier or credential) is appended to `publishing/store/channel-evidence.jsonl`.

---

## Phase 5 - Listing materials

| Item | Requirement / gotcha | This app |
| --- | --- | --- |
| **Privacy policy** | Required (app reads local files, and can make optional network calls) | [docs/privacy.html](../privacy.html) on GitHub Pages → URL |
| **Screenshots** | At least 1, PNG **≥ 1366×768** | `publishing/store/make-screenshot.ps1`, or use the real app screenshots in `docs/images/` |
| **Store logos** | Optional (Store falls back to package logos) | package tiles are generated from `assets/icons/store-icon-256.png` |
| **Description** | Required | template below |
| **Product features** | Bullet list, each ≤ 200 chars | template below |
| **Pricing** | "Free" = pick it in the **Retail price** dropdown | - |
| **runFullTrust justification** | Required for every desktop MSIX; **~1000-char limit** | template below |
| **Age rating** | Short questionnaire | Done - IARC rating is live (General audience). See "Age rating (IARC)" below. |
| **System requirements** | Not free text - the Store derives the minimum OS from the manifest's `TargetDeviceFamily MinVersion` | **No change needed.** The manifest floor is `10.0.17763` (1809), already *above* the .NET 10 runtime floor of `10.0.14393` (Win10 1607), so the packaged mainline runs on every machine the Store offers it to. Do not lower it below 14393. |

---

## Phase 6 - Submit → certification (~ a few business days)

The optional translation feature makes outbound HTTP calls to a *user-configured* local/remote
endpoint (Ollama / LibreTranslate). Declaring this plainly in the description + privacy policy
pre-empts review questions about network use. OCR itself is fully local.

---

## Age rating (IARC)

The Store age rating is generated by **IARC** from the questionnaire answers, not assigned by hand.
For this app the rating is **live** on the Microsoft storefront:

| Field | Value |
| --- | --- |
| Global Rating ID | `7d9b315a-f211-8505-80d0-3f4bee633770` |
| Product title | FastMediaSorter LITE (as filed on the rating date; the certificate keeps the title it was issued under) |
| Company | SZA |
| Storefront | Microsoft |
| Rating date | 2026-06-18 |

**The Global Rating ID is portable.** To list the app on another IARC-licensed storefront, paste this
ID when asked for a "Global Rating ID" / "IARC Certificate ID" instead of re-doing the questionnaire.

**When a re-rating is required:** the rating is bound to the questionnaire answers, so ordinary version
bumps do NOT need re-rating. You must re-run the IARC questionnaire only if a change would alter those
answers - e.g. adding in-app purchases, ad networks, or user-to-user sharing/social features. The
current app (local media and file manager, optional user-configured translation, no ads/accounts/UGC
sharing) keeps the General rating across normal updates.

If a rating ever looks wrong, request a rating check via the link in the IARC "Live Rating Notice"
email (1-3 business days).

---

## Text templates

> **Bulk-import CSV:** [publishing/store/listingData.csv](../../publishing/store/listingData.csv) is Partner
> Center's own "Import listing data" CSV format (`Field,ID,Type,default,en-us,ru`), kept in the repo so
> each release can update it in place instead of rebuilding it from scratch. It carries the same copy as
> the templates below - Description, ReleaseNotes ("What's new"), ShortDescription, Feature1-20,
> SearchTerm1-7 - plus every screenshot/logo asset URL and blank field from the last export, untouched.
> At release time: update Description/ReleaseNotes/features in this doc first (and the search terms in
> `tools/site-facts.json`), then run `Build-ListingData.ps1` - never mirror the text into the CSV by hand.
> The file's shape is WINDOWS-STORE rule 8: UTF-8 **without** a BOM, **CRLF as the only line break - inside a
> quoted cell as much as between records** - and no trailing newline; an embedded quote is doubled. A bare LF
> inside a cell is refused by Partner Center, and the builder writes none. Re-export from Partner Center after
> any manual edit there and diff before overwriting this file, so asset URLs never go stale. Import it in
> Partner Center via the submission's listing page →
> "Import listing data".

> **Status (2026-09-01): submitted.** The owner uploaded
> `publishing/msix/dist/FastMediaSorter_LITE-26.901.1550.0-x64.msix` (26.9.1.1550 remapped; unsigned,
> because Microsoft re-signs on certification) with the copy below, and the submission is in
> certification - a few business days. **Submitted is not live**: until it passes, the storefront still
> serves the previous package, so do not treat the blocks below as published copy or start a new
> version's edits on top of them yet.
>
> That submission closed a six-week backlog. The last version recorded as passing certification before
> it was **26.7.14.1801 (Submission 2)**, with the previous, shorter description; everything after it
> had piled up unsubmitted - the 26.7.15 Share Manager hardening + recipients panel, the whole
> 26.7.23.1127 .NET 10 rewrite (zoom, HEIC/AVIF, audio/subtitle tracks, Open URL, video controls,
> background file ops, media context menus, the viewer-core fix pass), then 13 UI languages, the
> Recycle Bin + 50-step undo, reassignable shortcuts, ZIP/CBZ browsing, the image editor, and finally
> 26.9.1.1550 (audio as its own kind of file with its own screen, animation -> MP4, the decode cache,
> honest destination-folder failures). That is why the "What's new" block spans seven releases rather
> than one.
>
> **Next time, write the block for the span since whatever actually went live**, and check that in
> Partner Center rather than inferring it here: `publishing/store/listingData.csv` is exported from the
> submission page and mirrors the *draft* as much as the live listing, so its `ReleaseNotes` row is not
> evidence that a version shipped. Once this submission is certified, the live baseline becomes
> **26.9.1.1550** and the next block starts there.
>
> **Rename note (2026-10, ticket 111; corrected by the owner on 2026-10-03):** the product's visible
> name changed, **the Store name did not**. The **title stays "FastMediaSorter LITE"** in Partner Center
> (the `Title` row of `listingData.csv`, which the generator below does not own, and the manifest
> `DisplayName`: frozen reserved name = the update anchor, do NOT change it; nothing is reserved anew).
> Only the **Description** text below uses the compact name without a platform suffix (233) and sharing, and
> the sentence "On the Microsoft Store this app is published as .." stays, because it is true. The
> compact form "Fast Media Sorter & Sharing" is not a Store name. `Identity Name` and `Publisher` are
> the update anchors as before. Paste the EN block into the listing's Description at the next
> submission; the RU block is optional copy for a Russian-market listing. The short name
> "Fast Media Sorter" appears below only inside running text.
>
> **Scope note (.NET 10):** the Store package is **x64-only and contains the mainline viewer alone**.
> Do NOT mention the 32-bit `FastMediaSorter_x86.exe` sibling in the Store listing - it exists only in
> the installer/winget/GitHub channels. Store copy describes the new build's *user-visible* wins
> (animated WEBP, VLC-only playback, no .NET Framework prerequisite), not the packaging split.

### Description (EN)
> First sentence must be a short, self-contained hook - the Store card truncates the description to
> its opening. Do NOT start with the "(published as ..)" parenthetical; it buries the lead. The
> text leads with the media and file manager, then sharing, since that is what the product says it is
> (specification 273); the Store package carries the viewer with its file manager mode and the Share
> Manager, so it says nothing about a Windows service or a server of its own.
```
Sort thousands of photos and videos in minutes with one key per file - and manage your files on this PC
and on servers. Fast Media Sorter & Sharing is a media and file manager for Windows: it views and sorts
images and video from the keyboard, opens local folders and SFTP and SMB servers in a file manager mode,
and comes with a bundled Share Manager that can also share the folders you pick - optional, off until
you switch it on.

Open a folder and fly through it: full-screen slideshow, quick panel navigation, and one-key Move /
Copy / Rename / Delete. Assign folders to hotkeys and file each item with a single press - or turn on
the recipients panel to click those destination folders right over the image and sort one-handed with
the mouse. It plays a broad range of video through its built-in VLC engine - H.264/MP4, AVI, MKV, VP9,
ZMBV and more - and fills letterbox/pillarbox bars with a matching "ambilight" background.

Rebuilt as a modern 64-bit app that carries its own runtime: there is nothing extra to install and no
.NET Framework prerequisite. Static and animated WEBP images open everywhere, because the app decodes
them itself instead of needing the Windows "WebP Image Extensions" codec that some editions of Windows
lack, and so do HEIC/HEIF and AVIF - the formats iPhones and modern cameras produce - with no paid codec
required. Still and animated JPEG XL (JXL) images also open without an extra Windows codec. Edits can be
saved as PNG, JPEG or WEBP; JXL output is not offered. Video always runs through the built-in VLC engine, so it plays the same on systems where the
retired Internet Explorer component is no longer present, now with a full control bar (seek, volume,
audio track and subtitle picking) and "Open URL.." to play straight off a network address. Zoom at the
mouse cursor with drag-to-pan, and right-click/middle-click menus put every action right on the picture
or video.

A file manager is built in: the Image panel (F3) switches from sorting to a file manager mode with an
address bar and history, a list of places - local drives and the SFTP and SMB servers you add - and
queued file operations between them. A folder can also hold documents: with two opt-in switches a text
file shows as text, a PDF shows its pages, an EPUB shows its cover and any other file shows a card with
its name, size and date, so a mixed folder sorts in one pass. A password-protected .fd-sec container
made by FileDO opens like any picture or video, decoded from memory with nothing written to disk.

Sorting is not only about moving files. Del sends a file to the Recycle Bin where one exists and says
plainly when there is none - a network drive or a memory stick - instead of pretending; U walks back
through the last 50 operations, renames and deletions included, each file returning to its folder and
to its place in the list. A ZIP or CBZ opens like an ordinary folder, so a comic or a folder of scans
browses, zooms and translates exactly like loose files, with nothing unpacked in advance and nothing
left behind afterwards. A picture can be marked up and cropped in its own editing window - brush,
rectangle and ellipse, a live crop frame with handles, Ctrl+Z - then saved over the original or beside
it in any of six formats, EXIF carried across. An animated WEBP, APNG or GIF can be turned into an
ordinary MP4 that replaces it, with a seek bar, a pause and full colour. And a music file gets a screen
of its own: the album cover it carries, or a wave-and-particle animation when it has none, with the
title, artist, album and format along the bottom, an end-of-track action and a sleep timer.

In the 64-bit viewer, right-click a video or audio file or its folder and choose Broadcast. Share Manager exports a .spres file, QR code or quick link for import in StreamsPlayer. A folder is one playlist channel with a frozen list; Refresh replaces that list and Revoke stops its link. The HTTP gateway has its own switch, off by default. Each token grants read-only access to one broadcast and normally expires after 24 hours. HTTP is unencrypted: share the document privately and keep internet access off unless you need it.

Share folders with your phone: the bundled Share Manager turns this PC
into a private SFTP server for folders you pick, so the Android app can browse them -
on your home Wi-Fi or over the internet - after a one-time QR pairing. Sharing is strictly opt-in and
stays between your own devices; nothing is uploaded to any third party. Sharing runs while you are
signed in. The Share Manager also runs scheduled file operations - copy, move, delete or archive the
files of a folder at a set time - in their own window, with no folder sharing needed, while it runs in
your signed-in session; they come with the Share Manager, with nothing else to download. The same program family also has an always-on Server edition, which hosts the same sharing
as a Windows service, and FileDO virtual-disk hosting, a capability of sharing; these are not part of
the Store version - they are available from the project website and winget.

Optional on-image OCR translation recognizes text in a picture (fully offline, Tesseract) and overlays
a translation using a provider you configure yourself - a local Ollama model or a LibreTranslate
endpoint. OCR works without any network connection.

No account, no ads, no telemetry. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite

On the Microsoft Store this app is published as "FastMediaSorter LITE".
```

### Description (RU - optional Russian-market listing)
```
Разберите тысячи фото и видео за минуты одной клавишей на файл - и управляйте файлами на этом ПК и на
серверах. Fast Media Sorter & Sharing - медиа- и файловый менеджер для Windows: он показывает и
сортирует изображения и видео с клавиатуры, открывает локальные папки и серверы SFTP и SMB в режиме
файлового менеджера и идёт со встроенным Share Manager, который может ещё и раздавать выбранные вами
папки - по желанию, выключено, пока вы не включите.

Откройте папку и листайте её мгновенно: полноэкранное слайд-шоу, быстрая навигация по панели и
перемещение / копирование / переименование / удаление одной клавишей. Назначьте папки на горячие
клавиши и раскладывайте файлы одним нажатием - или включите панель получателей и кликайте по этим
папкам прямо поверх изображения, сортируя одной рукой мышью. Видео воспроизводится встроенным
движком VLC - H.264/MP4, AVI, MKV, VP9, ZMBV и другие - а поля по краям кадра заполняются фоном в
стиле "ambilight" под цвет изображения.

Программа пересобрана как современное 64-битное приложение со своей средой выполнения: ничего
доустанавливать не нужно, .NET Framework больше не требуется. Статические и анимированные
WEBP-изображения открываются везде - приложение декодирует их само, без кодека Windows "WebP Image
Extensions", которого нет в некоторых редакциях Windows, а также HEIC/HEIF и AVIF - форматы,
которые снимают iPhone и современные камеры - без каких-либо платных кодеков. Обычные и анимированные
JPEG XL (JXL) тоже открываются без дополнительного кодека Windows. Результат редактирования сохраняется
в PNG, JPEG или WEBP; сохранения в JXL нет. Видео всегда идёт
через встроенный движок VLC, поэтому оно одинаково воспроизводится и на системах, где компонента
Internet Explorer больше нет, и теперь с полноценной панелью управления (перемотка, громкость,
выбор аудиодорожки и субтитров) и командой "Открыть по адресу.." для воспроизведения прямо по
сетевому пути. Масштабирование под курсором мыши с перетаскиванием, а меню по правому/среднему
клику выносят все действия прямо на изображение или видео.

Файловый менеджер встроен: «Панель изображений» (F3) переключается из сортировки в режим файлового
менеджера - с адресной строкой и историей, списком мест из локальных дисков и добавленных вами серверов
SFTP и SMB и очередью файловых операций между ними. В папке могут лежать и документы: два
необязательных переключателя показывают текстовый файл как текст, PDF - страницами, EPUB - обложкой, а
любой другой файл - карточкой с именем, размером и датой, так что смешанная папка сортируется за один
проход. Защищённый паролем контейнер .fd-sec, созданный FileDO, открывается как обычная картинка или
видео: содержимое декодируется из памяти, и ничего не пишется на диск.

Сортировка - это не только перемещение файлов. Del отправляет файл в Корзину там, где она есть, и
честно предупреждает, когда её нет - на сетевом диске или флешке, - вместо того чтобы делать вид; U
отменяет последние 50 операций, включая переименования и удаления, и каждый файл возвращается в свою
папку и на своё место в списке. ZIP или CBZ открывается как обычная папка: комикс или папка сканов
листается, масштабируется и переводится ровно так же, как отдельные файлы, - ничего не нужно
распаковывать заранее и ничего не остаётся потом. Картинку можно разметить и кадрировать в отдельном
окне редактирования - кисть, прямоугольник и эллипс, живая рамка кадрирования с маркерами, Ctrl+Z, -
а затем сохранить поверх оригинала или рядом в одном из шести форматов, с переносом EXIF.
Анимированный WEBP, APNG или GIF можно превратить в обычный MP4, который его заменит, - с перемоткой,
паузой и полным цветом. А у музыкального файла появился свой экран: обложка альбома, если она есть, а
если нет - анимация из волн и частиц, с названием, исполнителем, альбомом и форматом внизу, действием
в конце дорожки и таймером сна.

В 64-битном просмотрщике нажмите правой кнопкой на видео, аудиофайл или его папку и выберите трансляцию. Менеджер общего доступа выдаёт файл .spres, QR-код или быструю ссылку для импорта в StreamsPlayer. Папка становится одним каналом с зафиксированным списком: обновление заменяет список, отзыв отключает ссылку. HTTP-шлюз имеет отдельный переключатель и по умолчанию выключен. Токен даёт доступ только для чтения одной трансляции, обычно на 24 часа. HTTP не шифруется: передавайте документ лично и включайте интернет-доступ только при необходимости.

Раздавайте папки на телефон: входящая в комплект программа Share Manager превращает этот
ПК в частный SFTP-сервер для выбранных вами папок, чтобы Android-приложение
открывало их - в домашней сети Wi-Fi или через интернет - после однократного подключения по QR-коду.
Раздача включается только по вашему желанию и остаётся между вашими устройствами; никуда на сторону
ничего не выгружается. Раздача работает, пока вы вошли в систему. Share Manager также выполняет
файловые операции по расписанию - копирует, переносит, удаляет или архивирует файлы папки в заданное
время - в своём окне и без раздачи папок, пока он работает в вашем сеансе входа; они приходят вместе
с Share Manager, ничего дополнительно скачивать не нужно. У того же семейства программ есть
ещё серверная редакция, которая поднимает ту же раздачу как службу Windows, и хостинг виртуальных
дисков FileDO - возможность раздачи; в версию из Store не входит ни то ни другое - всё это доступно
на сайте проекта и через winget.

Необязательный перевод текста прямо на картинке: распознавание полностью офлайн (Tesseract) и
наложение перевода через выбранного вами провайдера - локальную модель Ollama или сервер
LibreTranslate. Распознавание работает без интернета.

Без аккаунта, без рекламы, без телеметрии. Открытый исходный код:
https://github.com/SerZhyAle/FastMediaSorter_Lite

В Microsoft Store приложение публикуется как "FastMediaSorter LITE".
```


### Description (UK)
```
Розберіть тисячі фото та відео за хвилини однією клавішею на файл - і керуйте файлами на цьому ПК та на серверах. Fast Media Sorter & Sharing for Windows - це медіа- та файловий менеджер для Windows: він переглядає й сортує зображення та відео з клавіатури, відкриває локальні теки та сервери SFTP і SMB у режимі файлового менеджера і постачається з вбудованим Share Manager, який також може роздавати вибрані вами теки - за бажанням, вимкнено, доки ви не ввімкнете.

Відкрийте теку й гортайте її миттєво: повноекранне слайд-шоу, швидка навігація панеллю та переміщення / копіювання / перейменування / видалення однією клавішею. Призначте теки на гарячі клавіші та розкладайте файли одним натисканням - або ввімкніть панель отримувачів і клацайте по цих теках призначення прямо поверх зображення, сортуючи однією рукою мишею. Відео відтворюється через вбудований рушій VLC - H.264/MP4, AVI, MKV, VP9, ZMBV тощо - а порожні смуги обабіч кадру заповнюються фоном "ambilight" під колір зображення.

Перезібрано як сучасний 64-бітний застосунок зі своїм середовищем виконання: нічого додатково встановлювати не потрібно, .NET Framework не вимагається. Статичні та анімовані зображення WEBP відкриваються всюди - застосунок декодує їх сам, без потреби в кодеку Windows "WebP Image Extensions", якого бракує в деяких редакціях Windows, а також HEIC/HEIF та AVIF - формати сучасних камер та iPhone - без платних кодеків. Звичайні та анімовані JPEG XL (JXL) також відкриваються без додаткового кодека Windows. Результати редагування зберігаються у PNG, JPEG або WEBP; експорту в JXL немає. Відео завжди йде через вбудований рушій VLC, тож воно відтворюється однаково і на системах, де компонент Internet Explorer відсутній, тепер із повноцінною панеллю керування (перемотка, гучність, вибір аудіодоріжки та субтитрів) і "Відкрити URL.." для відтворення за мережевою адресою. Масштабування під курсором миші з перетягуванням, а контекстні меню виводять усі дії прямо на зображення або відео.

Файловий менеджер вбудовано: «Панель зображень» (F3) перемикається із сортування в режим файлового менеджера - з адресним рядком та історією, списком місць із локальних дисків та доданих серверів SFTP і SMB та чергою файлових операцій між ними. У теці можуть бути й документи: два необов'язкові перемикачі показують текстовий файл як текст, PDF - сторінками, EPUB - обкладинкою, а будь-який інший файл - карткою з назвою, розміром і датою, тож змішана тека сортується за один прохід. Захищений паролем контейнер .fd-sec, створений FileDO, відкривається як звичайна картинка чи відео: вміст декодується з пам'яті, і нічого не пишеться на диск.

Сортування - це не лише переміщення файлів. Del надсилає файл до Кошика там, де він є, і чесно попереджає, коли його немає - на мережевому диску чи флешці; U скасовує останні 50 операцій, включно з перейменуваннями та видаленнями, повертаючи кожен файл до своєї теки та на своє місце у списку. ZIP або CBZ відкривається як звичайна тека: комікс або теку сканів можна гортати, масштабувати та перекладати точно так само, як окремі файли, нічого не розпаковуючи заздалегідь. Картинку можна розмітити й кадрувати в окремому вікні редагування - пензель, прямокутник та еліпс, жива рамка кадрування, Ctrl+Z - а потім зберегти поверх оригіналу або поруч в одному із шести форматів з перенесенням EXIF. Анімований WEBP, APNG чи GIF можна перетворити на звичайний MP4, який його замінить, з перемоткою, паузою та повним кольором. А аудіофайл отримує власний екран: обкладинку альбому або анімацію з хвиль і часток, з назвою, виконавцем, альбомом, дією в кінці доріжки й таймером сну.

У 64-бітному переглядачі клацніть правою кнопкою на відео, аудіофайл або його теку та виберіть трансляцію. Share Manager надає файл .spres, QR-код або швидке посилання для імпорту в StreamsPlayer. Тека стає одним каналом трансляції із зафіксованим списком: оновлення замінює список, а відкликання вимикає посилання. HTTP-шлюз має власний перемикач, типово вимкнений. Кожен токен дає доступ лише для читання однієї трансляції, зазвичай на 24 години. HTTP не шифрується: передавайте документ конфіденційно та вмикайте доступ до інтернету лише за потреби.

Діліться теками з телефоном: комплектна програма Share Manager перетворює цей ПК на приватний SFTP-сервер для вибраних тек, щоб Android-застосунок переглядав їх у домашній мережі Wi-Fi або через інтернет після одноразового підключення за QR-кодом. Спільний доступ вмикається виключно за вашим бажанням і залишається між вашими пристроями; нікуди назовні нічого не вивантажується. Роздача працює, поки ви ввійшли в систему. Share Manager також виконує файлові операції за розкладом - копіює, переносить, видаляє або архівує файли теки у визначений час у власному вікні без потреби роздавати теки. У того ж сімейства програм є серверна редакція, яка піднімає роздачу як службу Windows, і хостинг віртуальних дисків FileDO; у версію з Microsoft Store вони не входять - вони доступні на сайті проекту та через winget.

Необов'язковий переклад тексту на зображенні розпізнає текст офлайн (Tesseract) і накладає переклад через провайдера, якого ви налаштуєте - локальну модель Ollama або сервер LibreTranslate. Розпізнавання працює без інтернету.

Без облікового запису, без реклами, без телеметрії. Відкритий код: https://github.com/SerZhyAle/FastMediaSorter_Lite

У Microsoft Store застосунок публікується як "FastMediaSorter LITE".
```

### Description (DE)
```
Sortieren Sie Tausende von Fotos und Videos in Minuten mit einer Taste pro Datei - und verwalten Sie Dateien auf diesem PC und auf Servern. Fast Media Sorter & Sharing for Windows ist ein Medien- und Dateimanager für Windows: Er zeigt und sortiert Bilder und Videos über die Tastatur, öffnet lokale Ordner sowie SFTP- und SMB-Server im Dateimanager-Modus und enthält den gebündelten Share Manager, der ausgewählte Ordner freigeben kann - optional und standardmäßig deaktiviert.

Öffnen Sie einen Ordner und fliegen Sie hindurch: Vollbild-Diashow, schnelle Panel-Navigation und Verschieben / Kopieren / Umbenennen / Löschen mit einer einzigen Taste. Weisen Sie Ordner Schnelltasten zu und sortieren Sie jedes Element mit einem Tastendruck - oder aktivieren Sie das Empfänger-Panel, um Zielordner direkt über dem Bild mit der Maus anzuklicken. Videos werden über die integrierte VLC-Engine abgespielt (H.264/MP4, AVI, MKV, VP9, ZMBV und mehr), mit farblich passendem "Ambilight"-Hintergrund.

Als moderne 64-Bit-Anwendung mit eigener Laufzeitumgebung erstellt: keine Zusatzinstallationen, keine .NET Framework-Voraussetzung. Statische und animierte WEBP-Bilder öffnen sich ohne Windows-Zusatzcodecs, ebenso wie HEIC/HEIF und AVIF moderner Kameras und iPhones ohne kostenpflichtige Erweiterungen. Auch JPEG XL (JXL) wird nativ geöffnet. Bearbeitungen lassen sich als PNG, JPEG oder WEBP speichern. Videos laufen zuverlässig über VLC mit vollständiger Steuerleiste und "URL öffnen..". Zoom am Mauszeiger mit Drag-to-Pan und Kontextmenüs direkt auf Bild und Video.

Integrierter Dateimanager: Das Bild-Panel (F3) wechselt in den Dateimanager-Modus mit Adressleiste, Verlauf, Speicherorten (lokale Laufwerke, SFTP- und SMB-Server) und Datei-Warteschlange. Auch Dokumente lassen sich einbinden: Textdateien als Text, PDFs seitenweise, EPUBs mit Cover und sonstige Dateien als Infokarte. Passwortgeschützte .fd-sec-Container von FileDO öffnen sich direkt aus dem Speicher ohne Festplatten-Spuren.

Sortieren ist mehr als Verschieben. Entf nutzt den Papierkorb, wo vorhanden, und meldet ehrlich, wenn keiner existiert (Netzwerklaufwerke, USB-Sticks); U macht die letzten 50 Aktionen rückgängig. ZIP- und CBZ-Archive öffnen sich wie reguläre Ordner ohne vorheriges Entpacken. Bilder können im Bearbeitungsfenster markiert, zugeschnitten und gespeichert werden (EXIF bleibt erhalten). Animierte WEBP, APNG oder GIF lassen sich in MP4-Videos umwandeln. Musikdateien erhalten eine eigene Ansicht mit Albumcover oder Wellenanimation und Schlaftimer.

Im 64-Bit-Viewer lassen sich Videos, Audiodateien oder Ordner per Rechtsklick als Broadcast freigeben (Export als .spres-Datei, QR-Code oder Direktlink für StreamsPlayer).

Ordnerfreigabe für das Telefon: Der Share Manager verwandelt den PC in einen privaten SFTP-Server für ausgewählte Ordner, koppelbar per QR-Code. Freigaben bleiben rein privat auf Ihren Geräten. Der Share Manager führt zudem geplante Dateivorgänge (Kopieren, Verschieben, Löschen, Archivieren nach Zeitplan) in einem eigenen Fenster aus.

Optionale OCR-Übersetzung auf dem Bild erkennt Text offline (Tesseract) und übersetzt über einen frei konfigurierbaren Anbieter (lokales Ollama oder LibreTranslate).

Kein Konto, keine Werbung, keine Telemetrie. Quelloffen: https://github.com/SerZhyAle/FastMediaSorter_Lite

Hinweis: Die Benutzeroberfläche und dieser Store-Eintrag sind maschinell übersetzt.

Im Microsoft Store wird diese App als "FastMediaSorter LITE" veröffentlicht.
```

### Description (IT)
```
Ordina migliaia di foto e video in pochi minuti con un solo tasto per file e gestisci i tuoi file su questo PC e sui server. Fast Media Sorter & Sharing for Windows è un gestore di media e file per Windows: visualizza e ordina immagini e video da tastiera, apre cartelle locali e server SFTP e SMB in modalità file manager e include Share Manager per condividere le cartelle desiderate (funzione opzionale e disattivata per impostazione predefinita).

Apri una cartella ed esplorala all'istante: presentazione a schermo intero, navigazione rapida tramite pannello e spostamento, copia, ridenominazione ed eliminazione con un solo tasto. Assegna le cartelle a tasti di scelta rapida oppure attiva il pannello destinatari per fare clic sulle cartelle direttamente sopra l'immagine e ordinare con una sola mano tramite mouse. Riproduce un'ampia gamma di video tramite il motore VLC integrato (H.264/MP4, AVI, MKV, VP9, ZMBV e altri) con sfondo "ambilight" che riempie i bordi con i colori dell'immagine.

Costruito come moderna applicazione a 64 bit con runtime integrato: nessun componente aggiuntivo da installare e nessun prerequisito .NET Framework. Apre immagini WEBP statiche e animate in modo nativo senza codec aggiuntivi, così come HEIC/HEIF e AVIF prodotti da iPhone e fotocamere recenti senza codec a pagamento. Supporta anche JPEG XL (JXL). Le modifiche possono essere salvate in PNG, JPEG o WEBP. Video sempre fluidi con barra di controllo completa e opzione "Apri URL..". Zoom sotto il cursore con trascinamento e menu contestuali direttamente su foto e video.

File manager integrato: il pannello Immagini (F3) passa alla modalità file manager con barra degli indirizzi, cronologia, elenco delle posizioni (dischi locali, server SFTP e SMB) e coda delle operazioni sui file. Include supporto per documenti: file di testo mostrati come testo, PDF a pagine, copertine EPUB e schede informative per altri formati. I contenitori protetti da password .fd-sec di FileDO si aprono direttamente dalla memoria senza scrivere nulla su disco.

Ordinare non significa solo spostare file. Canc invia al Cestino dove disponibile ed evidenzia quando non lo è (unità di rete, chiavette USB); U annulla fino alle ultime 50 operazioni. Gli archivi ZIP o CBZ si aprono come normali cartelle senza estrazione preliminare. Gli strumenti di modifica consentono di ritagliare e annotare immagini mantenendo i dati EXIF. WEBP, APNG o GIF animate possono essere convertite in video MP4 standard. I file musicali dispongono di una schermata dedicata con copertina o animazione a onde e timer di spegnimento.

Nel visualizzatore a 64 bit, fai clic destro su video o cartelle per trasmetterli (esportazione di file .spres, codice QR o link rapido per StreamsPlayer).

Condivisione cartelle con lo smartphone: Share Manager trasforma il PC in un server SFTP privato accoppiato tramite codice QR. La condivisione resta strettamente locale tra i tuoi dispositivi. Share Manager esegue anche operazioni pianificate sui file (copia, spostamento, eliminazione, archiviazione).

Traduzione OCR opzionale sull'immagine: riconosce il testo offline (Tesseract) e applica la traduzione tramite provider configurabili (modello locale Ollama o LibreTranslate).

Nessun account, nessuna pubblicità, nessuna telemetria. Codice open source: https://github.com/SerZhyAle/FastMediaSorter_Lite

Nota: l'interfaccia e questa scheda dello Store sono fornite tramite traduzione automatica.

Nel Microsoft Store questa app è pubblicata come "FastMediaSorter LITE".
```

### Description (ES)
```
Organice miles de fotos y vídeos en cuestión de minutos con una sola tecla por archivo y administre sus ficheros en este PC y en servidores. Fast Media Sorter & Sharing for Windows es un gestor de medios y archivos para Windows: visualiza y clasifica imágenes y vídeo desde el teclado, abre carpetas locales y servidores SFTP y SMB en modo explorador de archivos e incluye el complemento Share Manager para compartir carpetas seleccionadas (opcional y desactivado de forma predeterminada).

Abra una carpeta y navegue al instante: pase de diapositivas a pantalla completa, navegación rápida mediante panel y mover, copiar, renombrar o eliminar con una sola tecla. Asigne carpetas a teclas de acceso rápido o active el panel de destinatarios para hacer clic en las carpetas de destino directamente sobre la imagen y clasificar con una sola mano mediante el ratón. Reproduce una gran variedad de vídeos mediante su motor VLC integrado (H.264/MP4, AVI, MKV, VP9, ZMBV y más) y añade un fondo "ambilight" que rellena los márgenes con los tonos de la imagen.

Creado como una aplicación moderna de 64 bits con entorno de ejecución propio: no requiere descargas adicionales ni .NET Framework. Abre imágenes WEBP estáticas y animadas sin necesidad del códec de Windows, así como archivos HEIC/HEIF y AVIF de iPhones y cámaras modernas sin códecs de pago. También admite JPEG XL (JXL). Las modificaciones pueden guardarse en PNG, JPEG o WEBP. Reproducción de vídeo con barra de control completa y opción "Abrir URL..". Zoom bajo el cursor con arrastre y menús contextuales directamente en la imagen o vídeo.

Explorador de archivos integrado: el panel de imágenes (F3) pasa a modo administrador de archivos con barra de direcciones, historial, lista de ubicaciones (unidades locales, servidores SFTP y SMB) y cola de operaciones de archivos. Admite documentos: notas de texto como texto, páginas PDF, portadas EPUB y tarjetas informativas para otros tipos de archivo. Los contenedores protegidos por contraseña .fd-sec creados con FileDO se abren en memoria sin escribir en disco.

La organización va más allá de mover archivos. Supr envía a la Papelera de reciclaje cuando existe y avisa con claridad cuando no está disponible (unidades de red o memorias USB); U deshace las últimas 50 operaciones. Los archivos ZIP o CBZ se abren como carpetas normales sin descompresión previa. La ventana de edición permite dibujar y recortar imágenes conservando los metadatos EXIF. Convierta animaciones WEBP, APNG o GIF en archivos MP4 estándar. La música cuenta con pantalla propia con carátula o animación de ondas y temporizador de apagado.

En el visor de 64 bits, haga clic derecho en un vídeo, pista de audio o carpeta para transmitir (exportación de archivo .spres, código QR o enlace directo para StreamsPlayer).

Comparta carpetas con su teléfono: Share Manager convierte este equipo en un servidor SFTP privado emparejado mediante código QR. Todo queda en sus propios dispositivos sin subirse a la nube. Share Manager también ejecuta operaciones programadas de archivos (copiar, mover, eliminar o archivar según un horario).

Traducción OCR opcional sobre la imagen: reconoce texto sin conexión (Tesseract) y superpone la traducción mediante el proveedor que elija (modelo local de Ollama o LibreTranslate).

Sin cuentas, sin publicidad, sin telemetría. Código abierto: https://github.com/SerZhyAle/FastMediaSorter_Lite

Nota: La interfaz y esta descripción de la tienda están disponibles mediante traducción automática.

En Microsoft Store esta aplicación se publica como "FastMediaSorter LITE".
```

### Description (FR)
```
Triez des milliers de photos et de vidéos en quelques minutes avec une seule touche par fichier, et gérez vos fichiers sur ce PC et sur vos serveurs. Fast Media Sorter & Sharing for Windows est un gestionnaire de médias et de fichiers pour Windows : il visualise et classe les images et vidéos depuis le clavier, ouvre les dossiers locaux ainsi que les serveurs SFTP et SMB en mode gestionnaire de fichiers, et intègre Share Manager pour partager les dossiers de votre choix (fonction optionnelle, désactivée par défaut).

Ouvrez un dossier et parcourez-le instantanément : diaporama en plein écran, navigation rapide par panneau et déplacement, copie, renommage ou suppression d'une seule touche. Associez des dossiers à des raccourcis clavier pour classer chaque élément d'une simple pression, ou activez le panneau des destinataires pour cliquer directement sur l'image et trier à la souris d'une seule main. Les vidéos sont lues via le moteur VLC intégré (H.264/MP4, AVI, MKV, VP9, ZMBV, etc.), avec un fond "ambilight" qui harmonise les bordures aux teintes de l'image.

Conçu comme une application 64 bits moderne intégrant son propre moteur d'exécution : aucune installation supplémentaire ni prérequis .NET Framework. Les images WEBP fixes et animées s'ouvrent sans nécessiter le codec de Windows, tout comme les formats HEIC/HEIF et AVIF des appareils photo récents et iPhones, sans codec payant. Prend également en charge le format JPEG XL (JXL). Les retouches peuvent être enregistrées en PNG, JPEG ou WEBP. Lecture vidéo fluide avec barre de contrôle complète et option "Ouvrir l'URL..". Zoom sous le curseur avec déplacement et menus contextuels directement sur l'image ou la vidéo.

Gestionnaire de fichiers intégré : le panneau Image (F3) passe en mode gestionnaire de fichiers avec barre d'adresse, historique, liste des emplacements (disques locaux, serveurs SFTP et SMB) et file d'attente des opérations. Prise en charge des documents : texte brut affiché sous forme de texte, PDF par pages, couvertures d'EPUB et fiches récapitulatives pour les autres formats. Les conteneurs sécurisés par mot de passe .fd-sec créés par FileDO s'ouvrent en mémoire sans rien écrire sur le disque.

Trier ne se résume pas à déplacer des fichiers. Suppr utilise la Corbeille lorsqu'elle existe et signale clairement son absence (lecteurs réseau, clés USB) ; U annule jusqu'aux 50 dernières opérations. Les archives ZIP ou CBZ s'ouvrent comme des dossiers ordinaires sans décompression préalable. La fenêtre d'édition permet d'annoter et de recadrer les photos en préservant les données EXIF. Les animations WEBP, APNG ou GIF peuvent être converties en vidéos MP4 standard. Les fichiers audio bénéficient d'un écran dédié avec pochette ou animation d'ondes et minuterie de mise en veille.

Dans la visionneuse 64 bits, faites un clic droit sur une vidéo, un fichier audio ou un dossier pour lancer une diffusion (export de fichier .spres, QR code ou lien direct pour StreamsPlayer).

Partagez des dossiers avec votre téléphone : Share Manager transforme votre PC en serveur SFTP privé appairé par QR code. Les partages restent strictement locaux entre vos appareils. Share Manager exécute également des opérations planifiées sur les fichiers (copie, déplacement, suppression ou archivage selon un calendrier).

Traduction OCR optionnelle sur l'image : reconnaît le texte hors ligne (Tesseract) et applique une traduction via le fournisseur de votre choix (modèle local Ollama ou serveur LibreTranslate).

Sans compte, sans publicité, sans télémétrie. Code source ouvert : https://github.com/SerZhyAle/FastMediaSorter_Lite

Remarque : L'interface et cette fiche du Store sont issues d'une traduction automatique.

Dans le Microsoft Store, cette application est publiée sous le nom "FastMediaSorter LITE".
```

### Description (PT-BR)
```
Organize milhares de fotos e vídeos em minutos com uma tecla por arquivo e gerencie arquivos neste PC e em servidores. O Fast Media Sorter & Sharing for Windows é um gerenciador de mídia e arquivos para Windows: visualiza e organiza imagens e vídeos pelo teclado, abre pastas locais e servidores SFTP e SMB no modo gerenciador de arquivos e inclui o Share Manager para compartilhar as pastas que você escolher (recurso opcional, desativado por padrão).

Abra uma pasta e navegue instantaneamente: apresentação de slides em tela cheia, navegação rápida por painel e comandos de mover, copiar, renomear e excluir com uma única tecla. Associe pastas a teclas de atalho para classificar itens com um só toque ou ative o painel de destinatários para clicar nas pastas de destino diretamente sobre a imagem com o mouse. Reproduz vídeos com motor VLC integrado (H.264/MP4, AVI, MKV, VP9, ZMBV e mais), com fundo "ambilight" que preenche as bordas com as cores da imagem.

Criado como um aplicativo moderno de 64 bits com runtime próprio: sem instalações extras e sem necessidade de .NET Framework. Abre imagens WEBP estáticas e animadas nativamente sem depender de codecs do Windows, assim como HEIC/HEIF e AVIF gerados por iPhones e câmeras modernas, sem custos com codecs adicionais. Também suporta JPEG XL (JXL). Edições podem ser salvas em PNG, JPEG ou WEBP. Reprodução de vídeo com barra de controle completa e opção "Abrir URL..". Zoom no cursor com arrasto e menus de contexto sobre a imagem ou vídeo.

Gerenciador de arquivos integrado: o painel Imagem (F3) alterna para o modo gerenciador de arquivos com barra de endereços, histórico, locais (unidades locais, servidores SFTP e SMB) e fila de operações de arquivos. Suporta documentos: textos exibidos como texto, PDFs em páginas, capas de EPUB e cartões informativos para outros arquivos. Contêineres .fd-sec protegidos por senha do FileDO abrem diretamente na memória sem gravar no disco.

Organizar vai além de mover arquivos. Del envia para a Lixeira onde houver e avisa com clareza em discos de rede ou pendrives; U desfaz as últimas 50 ações. Arquivos ZIP ou CBZ abrem como pastas normais sem descompactação prévia. Janela de edição própria para desenhar e recortar imagens mantendo dados EXIF. Converta animações WEBP, APNG ou GIF em vídeos MP4 convencionais. Músicas contam com tela exclusiva com capa do álbum ou animação de ondas e temporizador para desligar.

No visualizador de 64 bits, clique com o botão direito em um vídeo, áudio ou pasta para transmitir (exportação de arquivo .spres, código QR ou link direto para StreamsPlayer).

Compartilhe pastas com seu celular: o Share Manager transforma o PC em um servidor SFTP privado pareado por QR code. Os dados ficam apenas nos seus dispositivos. O Share Manager também executa operações agendadas de arquivos (copiar, mover, excluir ou arquivar).

Tradução OCR opcional na imagem: reconhece texto offline (Tesseract) e sobrepõe a tradução pelo provedor que você configurar (modelo local Ollama ou LibreTranslate).

Sem contas, sem anúncios, sem telemetria. Código aberto: https://github.com/SerZhyAle/FastMediaSorter_Lite

Observação: A interface e esta descrição da Store são fornecidas por tradução automática.

Na Microsoft Store, este aplicativo é publicado como "FastMediaSorter LITE".
```

### Description (AR)
```
رتّب آلاف الصور ومقاطع الفيديو في دقائق بمفتاح واحد لكل ملف، وأدر ملفاتك على هذا الكمبيوتر وعلى الخوادم. برنامج Fast Media Sorter & Sharing for Windows هو مدير وسائط وملفات لنظام Windows: يعرض الصور والفيديو ويرتبها من لوحة المفاتيح، ويفتح المجلدات المحلية وخوادم SFTP وSMB في وضع مدير الملفات، ويأتي مع تطبيق Share Manager المدمج لمشاركة المجلدات التي تختارها (ميزة اختيارية معطّلة افتراضيًا).

افتح أي مجلد وتصفحه بسلاسة: عرض شرائح بملء الشاشة، وتنقل سريع باللوحة المصغرة، وأوامر نقل ونسخ وإعادة تسمية وحذف بضغطة زر واحدة. عيّن المجلدات لمفاتيح الاختصار لفرز كل عنصر بلمسة واحدة، أو فعّل لوحة الوجهات للنقر على مجلدات الوجهة مباشرة فوق الصورة والفرز بيد واحدة باستخدام الفأرة. يشغّل الفيديو عبر محرك VLC المدمج (H.264/MP4 وAVI وMKV وVP9 وZMBV وغيرها) مع خلفية "ambilight" تملأ الحواف بألوان متناسقة مع الصورة.

تطبيق حديث 64 بت يأتي مع بيئة التشغيل الخاصة به: لا يتطلب تثبيت مكونات إضافية ولا يحتاج إلى .NET Framework. يفتح صور WEBP الثابتة والمتحركة تلقائيًا دون الحاجة إلى برامج ترميز Windows إضافية، وكذلك HEIC/HEIF وAVIF من هواتف iPhone والكاميرات الحديثة دون شراء أي إضافات. يدعم صيغة JPEG XL (JXL) الأصلية. يمكن حفظ التعديلات بصيغ PNG أو JPEG أو WEBP. يتضمن شريط تحكم كامل بالفيديو وخيار "فتح عنوان URL..". تقريب عند مؤشر الفأرة وقوائم سياقية مباشرة على المحتوى.

مدير ملفات مدمج: تتحول لوحة الصور (F3) إلى وضع مدير الملفات مع شريط عناوين وسجل تنقل وقائمة بالأماكن (الأقراص المحلية وخوادم SFTP وSMB) وقائمة انتظار للعمليات. يدعم المستندات أيضًا: عرض ملفات النصوص، وصفحات PDF، وأغلفة EPUB، وبطاقات معلومات للملفات الأخرى. تفتح حاويات .fd-sec المحمية بكلمة مرور والمصنوعة بواسطة FileDO من الذاكرة مباشرة دون كتابة على القرص.

الترتيب أكثر من مجرد نقل: يرسل مفتاح Del الملفات إلى سلة المحذوفات إن وجدت وينبهك بوضوح عند عدم وجودها (أقراص الشبكة وذاكرات USB)، ويتيح مفتاح U التراجع عن آخر 50 عملية. تفتح ملفات ZIP وCBZ كمجلدات عادية دون فك الضغط مسبقًا. نافذة تحرير متخصصة للرسم والاقتصاص مع الحفاظ على بيانات EXIF. إمكانية تحويل صور WEBP وAPNG وGIF المتحركة إلى فيديو MP4 عادي. شاشة مخصصة لملفات الموسيقى تعرض غلاف الألبوم أو مؤثرات الأمواج ومؤقت النوم.

في عارض 64 بت، انقر بزر الفأرة الأيمن على الفيديو أو الصوت لبدء بث محلي وتصدير ملف .spres أو رمز QR لتطبيق StreamsPlayer.

مشاركة المجلدات مع الهاتف: يحول Share Manager جهازك إلى خادم SFTP خاص يقترن برمز QR. تبقى البيانات محصورة بين أجهزتك فقط. كما ينفذ Share Manager عمليات ملفات مجدولة بانتظام.

ترجمة OCR اختيارية على الصورة: يتعرف على النصوص دون اتصال بالإنترنت (Tesseract) ويعرض الترجمة عبر مزود من اختيارك (نموذج Ollama محلي أو خادم LibreTranslate).

دون حسابات، دون إعلانات، دون تتبع. مفتوح المصدر: https://github.com/SerZhyAle/FastMediaSorter_Lite

ملاحظة: الواجهة وهذا الوصف في المتجر مترجمان آليًا.

في Microsoft Store يُنشر هذا التطبيق باسم "FastMediaSorter LITE".
```

### Description (HI)
```
हज़ारों फ़ोटो और वीडियो को प्रति फ़ाइल एक कुंजी से मिनटों में व्यवस्थित करें और इस पीसी तथा सर्वर पर फ़ाइलें प्रबंधित करें। Fast Media Sorter & Sharing for Windows एक शक्तिशाली मीडिया और फ़ाइल मैनेजर है: यह कीबोर्ड से चित्र और वीडियो देखता और छाँटता है, फ़ाइल मैनेजर मोड में स्थानीय फ़ोल्डर तथा SFTP व SMB सर्वर खोलता है, और इसमें शेयर मैनेजर शामिल है जो आपके चुने फ़ोल्डर साझा कर सकता है (वैकल्पिक, डिफ़ॉल्ट रूप से बंद)।

फ़ोल्डर खोलें और तेज़ी से नेविगेट करें: फ़ुल-स्क्रीन स्लाइडशो, त्वरित पैनल नेविगेशन, और एक कुंजी से मूव / कॉपी / नाम बदलें / डिलीट करें। फ़ोल्डर को शॉर्टकट कुंजियों में असाइन करें और एक ही बटन से फ़ाइलें छाँटें - या माउस से एक हाथ से छाँटने के लिए छवि के ऊपर प्राप्तकर्ता पैनल सक्षम करें। यह इन-बिल्ट VLC इंजन से वीडियो चलाता है (H.264/MP4, AVI, MKV, VP9, ZMBV आदि) और किनारों को रंगीन "एम्बिलाइट" पृष्ठभूमि से भरता है।

एक आधुनिक 64-बिट ऐप के रूप में निर्मित: इसमें कोई अतिरिक्त घटक इंस्टॉल नहीं करना पड़ता और .NET Framework की आवश्यकता नहीं है। स्थिर और एनिमेटेड WEBP चित्र बिना किसी अतिरिक्त कोडेक के खुलते हैं, और iPhone व आधुनिक कैमरों के HEIC/HEIF और AVIF प्रारूप भी बिना भुगतान वाले कोडेक के समर्थित हैं। यह JPEG XL (JXL) भी खोलता है। संपादन PNG, JPEG या WEBP में सहेजे जा सकते हैं। पूर्ण नियंत्रण बार और "URL खोलें.." विकल्प के साथ वीडियो सुचारू रूप से चलता है। माउस कर्सर पर ड्रैग-टू-पैन ज़ूम और छवि या वीडियो पर सीधे राइट-क्लिक मेनू उपलब्ध हैं।

अंतर्निहित फ़ाइल मैनेजर: छवि पैनल (F3) एड्रेस बार, इतिहास, स्थानों की सूची (स्थानीय ड्राइव, SFTP और SMB सर्वर) और फ़ाइल संचालन कतार के साथ फ़ाइल मैनेजर मोड में बदल जाता है। दस्तावेज़ समर्थन: टेक्स्ट फ़ाइलें टेक्स्ट के रूप में, PDF पृष्ठों के रूप में, EPUB कवर के रूप में और अन्य फ़ाइलें विवरण कार्ड के रूप में दिखती हैं। FileDO द्वारा बनाए गए पासवर्ड-सुरक्षित .fd-sec कंटेनर सीधे मेमोरी से डिकोड होकर खुलते हैं।

फ़ाइल छाँटना केवल स्थानांतरित करने तक सीमित नहीं है। Del कुंजी फ़ाइल को रीसायकल बिन में भेजती है जहाँ वह उपलब्ध हो और नेटवर्क ड्राइव पर स्पष्ट बताती है; U कुंजी अंतिम 50 क्रियाओं को पूर्ववत करती है। ZIP या CBZ फ़ाइलें बिना अनपैक किए सामान्य फ़ोल्डर की तरह खुलती हैं। संपादन विंडो में EXIF विवरण सुरक्षित रखते हुए छवियों को मार्क अप और क्रॉप किया जा सकता है। एनिमेटेड WEBP, APNG या GIF को सामान्य MP4 वीडियो में बदलें। संगीत फ़ाइलों के लिए एल्बम कवर या तरंग एनीमेशन और स्लीप टाइमर के साथ एक समर्पित स्क्रीन है।

64-बिट व्यूअर में वीडियो, ऑडियो या फ़ोल्डर पर राइट-क्लिक करके ब्रॉडकास्ट करें (StreamsPlayer के लिए .spres फ़ाइल, QR कोड या त्वरित लिंक निर्यात)।

फ़ोन के साथ फ़ोल्डर साझा करें: शेयर मैनेजर इस पीसी को QR कोड से कनेक्ट होने वाले निजी SFTP सर्वर में बदलता है। डेटा केवल आपके उपकरणों के बीच रहता है। शेयर मैनेजर समय पर स्वचालित रूप से फ़ाइल संचालन (कॉपी, मूव, डिलीट, आर्काइव) भी चलाता है।

वैकल्पिक ऑन-इमेज OCR अनुवाद: ऑफ़लाइन टेक्स्ट पहचानता है (Tesseract) और आपके चुने प्रदाता (स्थानीय Ollama या LibreTranslate) के माध्यम से अनुवाद प्रस्तुत करता है।

कोई खाता नहीं, कोई विज्ञापन नहीं, कोई टेलीमेट्री नहीं। ओपन सोर्स: https://github.com/SerZhyAle/FastMediaSorter_Lite

सूचना: इंटरफ़ेस और यह स्टोर विवरण मशीन अनुवाद द्वारा उपलब्ध कराया गया है।

Microsoft Store पर यह ऐप "FastMediaSorter LITE" के रूप में प्रकाशित है।
```

### Description (BN)
```
প্রতি ফাইলে মাত্র একটি কী চেপে কয়েক মিনিটে হাজার হাজার ছবি ও ভিডিও সাজান এবং এই পিসি ও সার্ভারে ফাইল পরিচালনা করুন। Fast Media Sorter & Sharing for Windows হলো Windows-এর জন্য একটি মিডিয়া ও ফাইল ম্যানেজার: এটি কীবোর্ড থেকে ছবি ও ভিডিও প্রদর্শন ও বিন্যাস করে, ফাইল ম্যানেজার মোডে লোকাল ফোল্ডার এবং SFTP ও SMB সার্ভার খোলে এবং নির্বাচিত ফোল্ডার শেয়ার করতে Share Manager সরবরাহ করে (ঐচ্ছিক, ডিফল্টভাবে নিষ্ক্রিয়)।

যেকোনো ফোল্ডার খুলুন এবং দ্রুত ব্রাউজ করুন: ফুল-স্ক্রিন স্লাইডশো, দ্রুত প্যানেল নেভিগেশন এবং একটি কী চেপে সরানো / কপি / নাম পরিবর্তন / মুছে ফেলা। শর্টকাট কী-তে ফোল্ডার নির্ধারণ করুন এবং এক চাপে ফাইল সাজান - অথবা মাউস দিয়ে সহজে সাজানোর জন্য ছবির ওপর প্রাপক প্যানেল চালু করুন। এটি নিজস্ব VLC ইঞ্জিনের মাধ্যমে ভিডিও চালায় (H.264/MP4, AVI, MKV, VP9, ZMBV ইত্যাদি) এবং ছবির রঙের সাথে মিলিয়ে "ambilight" ব্যাকগ্রাউন্ড প্রদর্শন করে।

স্বতন্ত্র রানটাইমসহ আধুনিক 64-বিট অ্যাপ হিসেবে তৈরি: অতিরিক্ত কিছু ইনস্টল করতে হয় না এবং .NET Framework-এর প্রয়োজন নেই। কোনো অতিরিক্ত কোডেক ছাড়াই স্থির ও অ্যানিমেটেড WEBP ছবি খোলে, এবং iPhone ও আধুনিক ক্যামেরার HEIC/HEIF ও AVIF ফরম্যাটও কোনো পেইড কোডেক ছাড়া সমর্থন করে। এটি JPEG XL (JXL) ছবিও সমর্থন করে। সম্পাদিত ছবি PNG, JPEG বা WEBP-তে সংরক্ষণ করা যায়। পূর্ণ কন্ট্রোল বার এবং "URL খুলুন.." সুবিধাসহ ভিডিও প্লেব্যাক। মাউস কার্সারে ড্র্যাগ-টু-প্যান জুম এবং ছবির ওপর সরাসরি রাইট-ক্লিক মেনু।

বিল্ট-ইন ফাইল ম্যানেজার: ইমেজ প্যানেল (F3) ঠিকানা বার, ইতিহাস, অবস্থানের তালিকা (স্থানীয় ড্রাইভ, SFTP এবং SMB সার্ভার) সহ ফাইল ম্যানেজার মোডে পরিবর্তিত হয়। ডকুমেন্ট সমর্থন: টেক্সট ফাইল টেক্সট হিসেবে, PDF পৃষ্ঠা হিসেবে, EPUB প্রচ্ছদ হিসেবে এবং অন্যান্য ফাইল তথ্য কার্ড হিসেবে দেখা যায়। FileDO-এর তৈরি পাসওয়ার্ড-সুরক্ষিত .fd-sec ফাইল মেমরি থেকেই ডিকোড হয়ে খোলে।

ফাইল গোছানো মানে শুধু ফাইল স্থানান্তর নয়। Del ফাইলকে রিসাইকেল বিনে পাঠায় এবং নেটওয়ার্ক ড্রাইভে তা না থাকলে স্পষ্টভাবে জানায়; U শেষ 50টি কাজ পূর্বাবস্থায় ফিরিয়ে নেয়। কোনো এক্সট্র্যাক্ট ছাড়াই ZIP বা CBZ ফাইল সাধারণ ফোল্ডারের মতো খোলে। এডিটিং উইন্ডোতে ব্রাশ ও ক্রপ ফ্রেমের সাহায্যে ছবি সম্পাদনা করা যায় এবং EXIF ডেটা সংরক্ষিত থাকে। অ্যানিমেটেড WEBP, APNG বা GIF সাধারণ MP4 ভিডিওতে রূপান্তর করা যায়। মিউজিক ফাইলের জন্য অ্যালবাম কভার বা তরঙ্গ অ্যানিমেশন এবং স্লিপ টাইমারসহ পৃথক স্ক্রিন রয়েছে।

64-বিট ভিউয়ারে ভিডিও, অডিও বা ফোল্ডারে রাইট-ক্লিক করে ব্রডকাস্ট করুন (StreamsPlayer-এর জন্য .spres ফাইল, QR কোড বা লিংক তৈরি)।

ফোনের সাথে ফোল্ডার শেয়ার করুন: Share Manager এই পিসিকে QR কোডে যুক্ত ব্যক্তিগত SFTP সার্ভারে রূপান্তর করে। ডেটা কেবল আপনার ডিভাইসের মধ্যেই সংরক্ষিত থাকে। Share Manager সময়সূচী অনুযায়ী ফাইল অপারেশনও পরিচালনা করে।

ঐচ্ছিক অন-ইমেজ OCR অনুবাদ: অফলাইনে টেক্সট শনাক্ত করে (Tesseract) এবং আপনার পছন্দের মাধ্যমে অনুবাদ দেখায় (স্থানীয় Ollama বা LibreTranslate)।

কোনো অ্যাকাউন্ট নেই, বিজ্ঞাপন নেই, টেলিমেট্রি নেই। ওপেন সোর্স: https://github.com/SerZhyAle/FastMediaSorter_Lite

বিজ্ঞপ্তি: ইন্টারফেস এবং স্টোরের এই বিবরণটি মেশিন অনুবাদের মাধ্যমে তৈরি।

Microsoft Store-এ এই অ্যাপটি "FastMediaSorter LITE" নামে প্রকাশিত।
```

### Description (UR)
```
ہر فائل کے لیے ایک کلید سے منٹوں میں ہزاروں تصاویر اور ویڈیوز ترتیب دیں اور اس پی سی اور سرورز پر فائلیں منظم کریں۔ Fast Media Sorter & Sharing for Windows ونڈوز کے لیے ایک جدید میڈیا اور فائل مینیجر ہے: یہ کی بورڈ کے ذریعے تصاویر اور ویڈیوز دکھاتا اور ترتیب دیتا ہے، فائل مینیجر موڈ میں مقامی فولڈرز اور SFTP اور SMB سرورز کھولتا ہے، اور اس میں Share Manager شامل ہے جو آپ کے منتخب کردہ فولڈرز شیئر کر سکتا ہے (اختیاری، پہلے سے بند)۔

کوئی بھی فولڈر کھولیں اور تیزی سے براؤز کریں: فل سکرین سلائیڈ شو، فوری پینل نیویگیشن، اور ایک کلید سے فائل منتقل، نقل، نام تبدیل یا حذف کریں۔ فولڈرز کو شارٹ کٹ کلیدوں پر تفویض کریں اور ایک ہی بٹن سے فائلیں ترتیب دیں - یا ماؤس سے ایک ہاتھ سے چھانٹنے کے لیے تصویر کے اوپر موصول کنندہ پینل آن کریں۔ یہ اپنے بلٹ ان VLC انجن کے ذریعے ویڈیو چلاتا ہے (H.264/MP4، AVI، MKV، VP9، ZMBV وغیرہ) اور کناروں کو رنگین "ambilight" پس منظر سے بھرتا ہے۔

جدید 64 بٹ ایپ کے طور پر تیار کردہ: اس میں کوئی اضافی فائلیں انسٹال نہیں کرنی پڑتیں اور .NET Framework کی ضرورت نہیں ہے۔ اینیمیٹڈ اور ساکن WEBP تصاویر بغیر کسی اضافی کوڈیک کے کھلتی ہیں، اور iPhone اور جدید کیمروں کے HEIC/HEIF اور AVIF فارمیٹس بھی بغیر کسی معاوضے والے کوڈیک کے چلتے ہیں۔ یہ JPEG XL (JXL) بھی کھولتا ہے۔ ترامیم کو PNG، JPEG یا WEBP میں محفوظ کیا جا سکتا ہے۔ ویڈیو مکمل کنٹرول بار اور "یو آر ایل کھولیں.." کے ساتھ روانی سے چلتی ہے۔ ماؤس کرسر پر زوم اور تصویر یا ویڈیو پر دائیں کلک کے مینو دستیاب ہیں۔

بلٹ ان فائل مینیجر: امیج پینل (F3) ایڈریس بار، ہسٹری، مقامات کی فہرست (مقامی ڈرائیوز، SFTP اور SMB سرورز) اور فائل آپریشنز کی قطار کے ساتھ فائل مینیجر میں تبدیل ہو جاتا ہے۔ دستاویزات کی معاونت: ٹیکسٹ فائلیں متن کے طور پر، PDF صفحات کے طور پر، EPUB کور کے طور پر اور دیگر فائلیں معلوماتی کارڈ کے طور پر نظر آتی ہیں۔ FileDO کے پاس ورڈ سے محفوظ .fd-sec کنٹینرز بغیر ڈسک پر لکھے میموری سے ڈیکوڈ ہو کر کھلتے ہیں۔

چھانٹنا صرف فائلیں منتقل کرنا نہیں ہے۔ Del فائل کو ری سائیکل بن میں بھیجتا ہے جہاں وہ موجود ہو اور نیٹ ورک پر نہ ہونے کی صورت میں واضح بتاتا ہے؛ U آخری 50 افعال کو منسوخ کرتا ہے۔ ZIP یا CBZ فائلیں بغیر ان زپ کیے عام فولڈر کی طرح کھلتی ہیں۔ ایڈیٹنگ ونڈو میں EXIF تفصیلات برقرار رکھتے ہوئے تصاویر پر نشانات لگائے اور کراپ کیے جا سکتے ہیں۔ اینیمیٹڈ WEBP، APNG یا GIF کو عام MP4 ویڈیو میں تبدیل کریں۔ موسیقی کے لیے البم کور یا لہروں کی اینیمیشن اور سلیپ ٹائمر کے ساتھ مخصوص سکرین ہے۔

64 بٹ ویور میں ویڈیو، آڈیو یا فولڈر پر دائیں کلک کر کے براڈکاسٹ کریں (StreamsPlayer کے لیے .spres فائل، QR کوڈ یا فوری لنک برآمد کریں)۔

فون کے ساتھ فولڈرز شیئر کریں: Share Manager پی سی کو QR کوڈ سے منسلک ہونے والے نجی SFTP سرور میں تبدیل کرتا ہے۔ ڈیٹا صرف آپ کے آلات کے درمیان رہتا ہے۔ Share Manager شیڈول کے مطابق فائل آپریشنز بھی چلاتا ہے۔

تصویر پر اختیاری OCR ترجمہ: انٹرنیٹ کے بغیر متن پہچانتا ہے (Tesseract) اور آپ کے منتخب کردہ ذریعے (مقامی Ollama یا LibreTranslate) سے ترجمہ دکھاتا ہے۔

کوئی اکاؤنٹ نہیں، کوئی اشتہار نہیں، کوئی ٹیلی میٹری نہیں۔ اوپن سورس: https://github.com/SerZhyAle/FastMediaSorter_Lite

نوٹ: انٹرفیس اور اسٹور کی یہ تفصیل مشین ترجمے کے ذریعے فراہم کی گئی ہے۔

Microsoft Store پر یہ ایپ "FastMediaSorter LITE" کے نام سے شائع کی گئی ہے۔
```

### Description (ZH-HANS)
```
每个文件只需按一个键，即可在几分钟内整理数千张照片和视频，并在本机及服务器上轻松管理文件。Fast Media Sorter & Sharing for Windows 是一款适用于 Windows 的媒体与文件管理器：通过键盘浏览和整理照片与视频，在文件管理器模式下打开本地文件夹以及 SFTP 和 SMB 服务器，并自带 Share Manager 组件来共享您挑选的文件夹（可选功能，默认处于关闭状态）。

打开任意文件夹并飞速浏览：全屏幻灯片、快速缩略图面板导航，以及一键移动、复制、重命名或删除。将目标文件夹绑定到快捷键即可单手按键归档，或者开启悬浮目标面板，直接在画面上点击文件夹进行单手鼠标整理。内置 VLC 引擎流畅播放多种格式视频（H.264/MP4、AVI、MKV、VP9、ZMBV 等），并配备匹配画面色调的 "ambilight" 背景流光效果。

采用现代 64 位原生架构重构，自带独立运行时：无需安装额外依赖，也无需 .NET Framework 环境。直接打开静态和动态 WEBP 图片，无需依赖部分 Windows 版本缺少的扩展编解码器；无需付费即可直接打开 iPhone 及现代相机拍摄的 HEIC/HEIF 和 AVIF 照片。同时原生支持 JPEG XL (JXL)。编辑后的图片可保存为 PNG、JPEG 或 WEBP。视频播放具备完整的控制栏（快进快退、音量、音轨及字幕语言记忆）和“打开网络地址”流媒体功能。鼠标指针处平滑缩放与拖拽平移，右键和中键菜单将各项操作直接呈现在画面之上。

内置文件管理器：图像面板（F3）可从整理模式切换为文件管理器模式，具备地址栏、历史记录、位置列表（本地驱动器、SFTP 及 SMB 服务器）以及文件操作队列。混合文件夹支持：纯文本直接显示文本，PDF 逐页翻阅，EPUB 显示封面，其他文件显示信息卡片。FileDO 生成的密码保护型 .fd-sec 加密容器可直接在内存中解码并查看，磁盘不留痕迹。

整理不仅是移动文件。Del 键支持放进回收站，遇到网络驱动器或 U 盘等没有回收站的介质会如实提示；U 键支持撤销最近 50 次操作（包含重命名与删除）。ZIP 或 CBZ 压缩包可作为普通文件夹直接浏览、缩放和取词翻译，无需提前解压。独立的编辑窗口提供画笔、矩形、椭圆与动态裁剪框，Ctrl+Z 撤销，保留原始 EXIF 信息。可将动态 WEBP、APNG 或 GIF 一键转换为带进度条的标准 MP4 视频。音乐播放享有专属界面，显示专辑封面或粒子波形动画、曲目信息及睡眠定时器。

在 64 位查看器中，右键点击视频、音频或其文件夹即可开启广播，Share Manager 可导出 .spres 文件、二维码或快速链接供 StreamsPlayer 导入。

与手机共享文件夹：随附的 Share Manager 可将此电脑转为专属 SFTP 服务器，手机扫描二维码即可配对浏览。数据仅在您自己的设备间传输，不上传至任何第三方。Share Manager 还支持独立设置定时文件操作（定时复制、移动、清理或归档）。

可选的画面 OCR 取词翻译：离线识别文字（Tesseract），并通过您配置的本地 Ollama 模型或 LibreTranslate 服务进行翻译，完全无需联网。

无账户要求，无广告，无遥测。开源项目：https://github.com/SerZhyAle/FastMediaSorter_Lite

说明：本软件界面及应用商店文案提供机器翻译版本。

在 Microsoft Store 中，本应用以 "FastMediaSorter LITE" 名称发布。
```

### Product features (one per line, ≤200 chars each)
```
Media and file manager: sort with one key per file (Move / Copy / Rename / Delete to hotkey folders) and manage files on disks and SFTP/SMB servers
Optional recipients panel over the media: click destination folders on the image to sort one-handed with the mouse
Full-screen slideshow and quick panel/thumbnail navigation for large image and video collections
Plays video through a built-in VLC engine: H.264/MP4, AVI, MKV, VP9, ZMBV and more, with no extra codec packs
Opens static and animated WEBP on its own - no Windows "WebP Image Extensions" codec required
Opens HEIC, HEIF and AVIF too - the formats iPhones and modern cameras produce - with no paid codec needed
Modern 64-bit build with its own runtime: nothing extra to install, no .NET Framework prerequisite
Zoom at the mouse cursor with drag-to-pan, plus audio-track and subtitle picking remembered by language
A video control bar (seek, time, mute, volume) and "Open URL.." to play straight off a network address
Right-click/middle-click menus put every action - rotate, translate, move, delete, and more - right on the picture or video
"Ambilight" perspective background fills letterbox/pillarbox bars to match the image
Share folders with your phone: the bundled Share Manager runs a private, opt-in SFTP server paired by QR, and can run file operations on a schedule
Optional on-image OCR translation: offline Tesseract OCR + a translator you configure (Ollama / LibreTranslate)
Set it as your default image viewer for JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF and SVG
Del uses the Recycle Bin where there is one and says so when there is not; U undoes the last 50 operations, renames included
Opens a ZIP or CBZ like a folder: browse, zoom and translate a comic or a folder of scans with nothing unpacked in advance
Mark up and crop a picture in its own window - brush, rectangle, ellipse, live crop frame, Ctrl+Z - and save over it or beside it
Turn an animated WEBP, APNG or GIF into an ordinary MP4 that replaces it, with a seek bar, a pause and full colour
A screen of its own for music: album cover or a wave-and-particle animation, title/artist/album, end-of-track action, sleep timer
Open source, no account, no ads, no telemetry
```

> **20 is the ceiling** - the list above is exactly 20 lines. Adding a feature now means dropping one,
> not appending a 21st.

### Product features (RU - one per line, same order, ≤200 chars each)
```
Медиа- и файловый менеджер: сортировка одной клавишей на файл (перенос, копирование, переименование, удаление) и управление файлами на дисках и серверах SFTP/SMB
Панель получателей поверх медиа: кликайте по папкам назначения прямо на изображении и сортируйте одной рукой мышью
Полноэкранное слайд-шоу и быстрая навигация по панели миниатюр для больших коллекций фото и видео
Видео идёт через встроенный движок VLC: H.264/MP4, AVI, MKV, VP9, ZMBV и другие, без дополнительных кодек-паков
Открывает статические и анимированные WEBP сам - кодек Windows "WebP Image Extensions" не нужен
Открывает и HEIC, HEIF, AVIF - форматы iPhone и современных камер - без платных кодеков
Современная 64-битная сборка со своей средой выполнения: ничего доустанавливать не нужно, .NET Framework не требуется
Масштабирование под курсором мыши с перетаскиванием, выбор аудиодорожки и субтитров запоминается по языку
Панель управления видео (перемотка, время, звук, громкость) и "Открыть по адресу.." для воспроизведения по сетевому пути
Меню по правому и среднему клику выносят все действия - поворот, перевод, перемещение, удаление и другое - прямо на картинку или видео
Фон-перспектива в стиле "ambilight" заполняет поля по краям кадра под цвет изображения
Раздача папок на телефон: программа Share Manager поднимает частный SFTP-сервер по вашему желанию, подключение по QR-коду, и выполняет файловые операции по расписанию
Необязательный перевод текста на картинке: офлайн-распознавание Tesseract и переводчик по вашему выбору (Ollama / LibreTranslate)
Назначьте программу просмотрщиком по умолчанию для JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF и SVG
Del использует Корзину там, где она есть, и честно предупреждает, когда её нет; U отменяет последние 50 операций, включая переименования
Открывает ZIP или CBZ как папку: комикс или папку сканов можно листать, масштабировать и переводить, ничего не распаковывая
Разметка и кадрирование картинки в своём окне - кисть, прямоугольник, эллипс, живая рамка кадрирования, Ctrl+Z - с сохранением поверх или рядом
Превращает анимированный WEBP, APNG или GIF в обычный MP4, который его заменяет, - с перемоткой, паузой и полным цветом
Свой экран для музыки: обложка альбома или анимация из волн и частиц, название/исполнитель/альбом, действие в конце, таймер сна
Открытый исходный код, без аккаунта, без рекламы, без телеметрии
```

> The RU column of `listingData.csv` used to carry only `Feature1`; 2-20 were empty, so a Russian
> visitor saw one bullet. These are the missing nineteen, in the same order as the EN list so the two
> stay diffable.

> **The blocks on this page are the source; the CSV is generated from them.** After editing any of
> Description / Product features / What's new above, run
> `pwsh -NoProfile -File .\publishing\store\Build-ListingData.ps1` - it rewrites only the
> `Description`, `ReleaseNotes`, `Feature1..20` and `SearchTerm1..7` cells of
> [publishing/store/listingData.csv](../../publishing/store/listingData.csv) (both `en-us` and `ru`;
> the search terms come from `tools/site-facts.json`), carries every other cell through unchanged, and
> refuses to write if the copy breaks a Partner Center limit (1500 for What's new, 10000 for Description, 20
> features of 200 chars, 7 search terms; each counted after the line breaks become CRLF, as Partner Center
> counts them). `-Check` verifies the committed CSV is byte for byte what the builder writes and exits 1 if
> not. Import it in Partner Center via the
> submission's listing page -> "Import listing data". **After editing anything by hand in Partner
> Center, re-export and diff before overwriting the file** - it also holds screenshot and logo asset
> URLs that only a real export can produce.


### Product features (UK - one per line, same order, ≤200 chars each)
```
Медіа- та файловий менеджер: сортування однією клавішею на файл (перенесення, копіювання, перейменування, видалення) та керування файлами на дисках і серверах SFTP/SMB
Панель отримувачів поверх медіа: клацайте по теках призначення прямо на зображенні та сортуйте однією рукою мишею
Повноекранне слайд-шоу та швидка навігація панеллю мініатюр для великих колекцій фото та відео
Відтворення відео вбудованим рушієм VLC: H.264/MP4, AVI, MKV, VP9, ZMBV тощо, без додаткових кодеків
Відкриває статичні та анімовані WEBP самостійно - кодек Windows "WebP Image Extensions" не потрібен
Відкриває HEIC, HEIF та AVIF - формати сучасних камер та iPhone - без платних кодеків
Сучасна 64-бітна збірка зі своїм середовищем виконання: нічого додатково встановлювати не потрібно, без .NET
Масштабування під курсором миші з перетягуванням, запам'ятовування звукової доріжки та субтитрів за мовою
Панель керування відео (перемотка, час, звук, гучність) і «Відкрити URL..» для відтворення за мережевою адресою
Меню за правим і середнім кліком виносять усі дії - поворот, переклад, переміщення, видалення - прямо на екран
Фон-перспектива в стилі «ambilight» заповнює порожні смуги обабіч кадру кольором зображення
Роздача тек на телефон: Share Manager запускає приватний SFTP-сервер за QR-кодом і виконує операції за розкладом
Необов'язковий переклад тексту на зображенні: офлайн-Tesseract OCR та перекладач на вибір (Ollama/LibreTranslate)
Призначте програму переглядачем за замовчуванням для JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF та SVG
Del надсилає до Кошика там, де він є, і чесно каже, коли немає; U скасовує останні 50 операцій
Відкриває ZIP або CBZ як теку: гортайте, масштабуйте та перекладайте комікси або скани без розпакування
Розмітка та кадрування в окремому вікні - пензель, прямокутник, еліпс, жива рамка, Ctrl+Z - збереження поруч
Перетворення анімованого WEBP, APNG або GIF на звичайний MP4 з перемоткою, паузою та повним кольором
Окремий екран для музики: обкладинка альбому або анімація з хвиль і часток, деталі треку, таймер сну
Відкритий вихідний код, без облікового запису, без реклами, без телеметрії
```

### Product features (DE - one per line, same order, ≤200 chars each)
```
Medien- und Dateimanager: Sortieren mit einer Taste pro Datei (Verschieben/Kopieren/Umbenennen/Löschen) und Dateiverwaltung auf Datenträgern und SFTP/SMB-Servern
Optionales Empfänger-Panel über dem Medium: Zielordner direkt auf dem Bild anklicken und einhändig mit der Maus sortieren
Vollbild-Diashow und schnelle Panel-/Miniaturbild-Navigation für große Foto- und Videosammlungen
Videowiedergabe über integrierte VLC-Engine: H.264/MP4, AVI, MKV, VP9, ZMBV und mehr ohne zusätzliche Codec-Packs
Öffnet statische und animierte WEBP-Dateien nativ - kein Windows "WebP Image Extensions"-Codec erforderlich
Öffnet HEIC, HEIF und AVIF - die Formate von iPhones und modernen Kameras - ohne kostenpflichtige Codecs
Moderne 64-Bit-Version mit eigener Laufzeitumgebung: keine Zusatzinstallationen, kein .NET Framework erforderlich
Zoom am Mauszeiger mit Drag-to-Pan sowie Speicherung von Audiospur und Untertiteln nach Sprache
Videosteuerleiste (Suchlauf, Zeit, Stummschaltung, Lautstärke) und "URL öffnen.." für Netzwerk-Streams
Kontextmenüs per Rechts- und Mittelklick für alle Aktionen - Drehen, Übersetzen, Verschieben, Löschen - direkt auf dem Bild
"Ambilight"-Hintergrund füllt schwarze Balken passend zu den Bildfarben aus
Ordnerfreigabe fürs Smartphone: Share Manager stellt privaten SFTP-Server per QR-Code bereit und führt geplante Dateivorgänge aus
Optionale OCR-Übersetzung auf dem Bild: Offline-Tesseract-OCR plus konfigurierbarer Übersetzer (Ollama / LibreTranslate)
Als Standard-Bildbetrachter festlegbar für JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF und SVG
Entf nutzt den Papierkorb, wo vorhanden; U nimmt die letzten 50 Operationen samt Umbenennungen zurück
Öffnet ZIP oder CBZ wie Ordner: Comics oder Scans durchblättern, zoomen und übersetzen ohne vorheriges Entpacken
Bilder im eigenen Fenster bearbeiten und zuschneiden - Pinsel, Rechteck, Ellipse, Zuschnittrahmen, Strg+Z
Animierte WEBP, APNG oder GIF in reguläre MP4-Videos umwandeln mit Suchleiste, Pause und voller Farbe
Eigener Bildschirm für Musik: Albumcover oder Wellenanimation, Titel/Interpret/Album und Schlaftimer
Open Source, kein Konto erforderlich, werbefrei, ohne Telemetrie
```

### Product features (IT - one per line, same order, ≤200 chars each)
```
Gestore di media e file: ordina con un tasto per file (sposta/copia/rinomina/elimina) e gestisce file su dischi e server SFTP/SMB
Pannello destinatari opzionale sul file multimediale: fai clic sulle cartelle direttamente sull'immagine con il mouse
Presentazione a schermo intero e navigazione rapida con miniature per grandi raccolte di foto e video
Riproduce video tramite motore VLC integrato: H.264/MP4, AVI, MKV, VP9, ZMBV e altri senza pacchetti codec extra
Apre WEBP statici e animati nativamente senza necessità del codec Windows "WebP Image Extensions"
Apre HEIC, HEIF e AVIF - i formati di iPhone e fotocamere moderne - senza codec a pagamento
Moderna versione a 64 bit con runtime integrato: nulla da installare, nessun requisito .NET Framework
Zoom sul cursore con trascinamento e memorizzazione di traccia audio e sottotitoli in base alla lingua
Barra di controllo video (ricerca, tempo, muto, volume) e "Apri URL.." per riprodurre da indirizzi di rete
Menu contestuali con clic destro e centrale per ogni azione (ruota, traduci, sposta, elimina) direttamente sul file
Sfondo prospettico "ambilight" che riempie i bordi neri adattandosi ai colori dell'immagine
Condivisione con lo smartphone: Share Manager crea un server SFTP privato con QR ed esegue operazioni pianificate
Traduzione OCR su immagine opzionale: Tesseract OCR offline e traduttore configurabile (Ollama / LibreTranslate)
Impostabile come visualizzatore predefinito per JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF e SVG
Canc usa il Cestino dove presente; U annulla le ultime 50 operazioni comprese le ridenominazioni
Apre ZIP o CBZ come cartelle: sfoglia, ingrandisci e traduci fumetti o scansioni senza estrarli prima
Modifica e ritaglia immagini in una finestra dedicata (pennello, rettangolo, ellisse, crop frame, Ctrl+Z)
Trasforma WEBP, APNG o GIF animate in normali video MP4 con barra di avanzamento e pausa
Schermata dedicata per la musica: copertina dell'album o animazione a onde, dettagli traccia e timer di spegnimento
Open source, nessun account, nessuna pubblicità, nessuna telemetria
```

### Product features (ES - one per line, same order, ≤200 chars each)
```
Gestor de medios y archivos: ordene con una tecla por archivo (mover/copiar/renombrar/eliminar) y gestione archivos en discos y servidores SFTP/SMB
Panel de destinatarios opcional sobre el medio: haga clic en las carpetas sobre la imagen para ordenar con una mano usando el ratón
Pase de diapositivas a pantalla completa y navegación rápida por miniaturas para grandes colecciones de fotos y vídeos
Reproduce vídeo mediante motor VLC integrado: H.264/MP4, AVI, MKV, VP9, ZMBV y más sin paquetes de códecs adicionales
Abre imágenes WEBP estáticas y animadas por sí mismo, sin necesidad del códec de Windows "WebP Image Extensions"
Abre HEIC, HEIF y AVIF -formatos de iPhone y cámaras modernas- sin necesidad de pagar por códecs adicionales
Versión moderna de 64 bits con entorno propio: sin instalaciones extra y sin requisito de .NET Framework
Zoom en la posición del cursor con arrastre, y memoria de pistas de audio y subtítulos por idioma
Barra de control de vídeo (búsqueda, tiempo, silencio, volumen) y "Abrir URL.." para reproducir direcciones de red
Menús contextuales con clic derecho y central para realizar cualquier acción directamente sobre la imagen o vídeo
Fondo en perspectiva "ambilight" que rellena las franjas negras adaptándose a los colores de la imagen
Comparta carpetas con el móvil: Share Manager ejecuta un servidor SFTP privado mediante QR y programa operaciones
Traducción OCR sobre la imagen opcional: Tesseract OCR sin conexión y traductor configurable (Ollama / LibreTranslate)
Establézcalo como visor predeterminado para JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF y SVG
Supr utiliza la Papelera cuando existe; U deshace las últimas 50 operaciones, incluidos cambios de nombre
Abre archivos ZIP o CBZ como carpetas: explore, amplíe y traduzca cómics o escaneos sin descomprimirlos antes
Edite y recorte imágenes en su propia ventana (pincel, rectángulo, elipse, marco de recorte en vivo, Ctrl+Z)
Transforme imágenes animadas WEBP, APNG o GIF en vídeos MP4 estándar con barra de búsqueda, pausa y color completo
Pantalla dedicada para música: carátula de álbum o animación de ondas, datos de pista y temporizador de apagado
Código abierto, sin registro de cuenta, sin anuncios y sin telemetría
```

### Product features (FR - one per line, same order, ≤200 chars each)
```
Gestionnaire de médias et fichiers : tri d'une touche par fichier (déplacer/copier/renommer/supprimer) et gestion sur disques et serveurs SFTP/SMB
Panneau destinataires optionnel sur le média : cliquez sur les dossiers directement sur l'image pour trier d'une main à la souris
Diaporama plein écran et navigation rapide par miniatures pour les grandes collections de photos et de vidéos
Lecture vidéo via moteur VLC intégré : H.264/MP4, AVI, MKV, VP9, ZMBV et plus encore sans aucun pack de codecs supplémentaire
Ouvre les images WEBP statiques et animées en natif - aucun codec Windows "WebP Image Extensions" requis
Ouvre aussi HEIC, HEIF et AVIF - les formats des iPhone et appareils récents - sans codec payant
Application 64 bits moderne avec runtime intégré : rien à installer de plus, aucun prérequis .NET Framework
Zoom sous le curseur avec glisser-déplacer, mémorisation des pistes audio et sous-titres selon la langue
Barre de contrôle vidéo (recherche, durée, muet, volume) et commande "Ouvrir l'URL.." pour lire les flux réseau
Menus contextuels par clic droit et milieu pour toutes les actions (pivoter, traduire, déplacer, supprimer) sur le média
Fond d'ambiance "ambilight" qui harmonise les bordures noires en s'adaptant aux couleurs de l'image
Partage de dossiers avec le smartphone : Share Manager active un serveur SFTP privé par QR et planifie vos opérations
Traduction OCR optionnelle sur image : OCR Tesseract hors ligne et traducteur configurable (Ollama / LibreTranslate)
Définissable comme visionneuse par défaut pour JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF et SVG
Suppr utilise la Corbeille si disponible ; U annule les 50 dernières opérations, renommages compris
Ouvre les ZIP et CBZ comme des dossiers : feuilletez, zoomez et traduisez bandes dessinées et scans sans extraction
Annotez et recadrez les images dans une fenêtre dédiée (pinceau, rectangle, ellipse, cadre de recadrage, Ctrl+Z)
Convertit les WEBP, APNG ou GIF animés en vidéos MP4 classiques avec barre de recherche, pause et couleurs complètes
Écran dédié pour la musique : pochette d'album ou animation d'ondes, détails de la piste et minuteur de veille
Open source, aucun compte requis, sans publicité ni télémétrie
```

### Product features (PT-BR - one per line, same order, ≤200 chars each)
```
Gerenciador de mídia e arquivos: organize com uma tecla por arquivo (mover/copiar/renomear/excluir) e gerencie em discos e servidores SFTP/SMB
Painel de destinatários opcional sobre a mídia: clique nas pastas diretamente sobre a imagem para organizar com o mouse
Apresentação de slides em tela cheia e navegação rápida por miniaturas para grandes coleções de fotos e vídeos
Reprodução de vídeo por motor VLC integrado: H.264/MP4, AVI, MKV, VP9, ZMBV e mais sem pacotes adicionais de codecs
Abre arquivos WEBP estáticos e animados nativamente - sem exigir o codec "WebP Image Extensions" do Windows
Abre HEIC, HEIF e AVIF - formatos de iPhones e câmeras modernas - sem necessidade de comprar codecs adicionais
Versão moderna em 64 bits com runtime integrado: nada extra para instalar, sem dependência do .NET Framework
Zoom na posição do cursor com arrasto, além de memorização de faixas de áudio e legendas por idioma
Barra de controle de vídeo (busca, tempo, mudo, volume) e "Abrir URL.." para reproduzir endereços de rede
Menus com clique direito e do meio colocam todas as ações diretamente sobre a foto ou vídeo
Fundo em perspectiva "ambilight" preenche barras pretas combinando com as cores da imagem
Compartilhamento com celular: o Share Manager cria um servidor SFTP privado por QR code e agenda tarefas de arquivos
Tradução OCR opcional na imagem: Tesseract OCR offline com tradutor configurável (Ollama / LibreTranslate)
Defina como visualizador padrão para JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF e SVG
Del envia para a Lixeira se houver; U desfaz as últimas 50 operações, inclusive renomeações
Abre ZIP e CBZ como pastas: navegue, amplie e traduza quadrinhos ou digitalizações sem descompactar antes
Edite e recorte imagens em janela própria (pincel, retângulo, elipse, moldura de corte ao vivo, Ctrl+Z)
Converta WEBP, APNG ou GIF animados em vídeos MP4 comuns com barra de reprodução e pausa
Tela exclusiva para músicas: capa do álbum ou animação de ondas, dados da faixa e temporizador de repouso
Código aberto, sem necessidade de conta, sem anúncios e sem telemetria
```

### Product features (AR - one per line, same order, ≤200 chars each)
```
مدير وسائط وملفات: ترتيب بمفتاح واحد لكل ملف (نقل/نسخ/إعادة تسمية/حذف) وإدارة الملفات على الأقراص وخوادم SFTP/SMB
لوحة وجهات اختيارية فوق الوسائط: انقر على المجلدات المطلوبة مباشرة فوق الصورة لترتيبها بيد واحدة بالفأرة
عرض شرائح بملء الشاشة وتنقل سريع عبر المصغرات لمجموعات الصور والفيديو الكبيرة
تشغيل الفيديو عبر محرك VLC مدمج: يدعم H.264/MP4 وAVI وMKV وVP9 وZMBV دون الحاجة إلى حزم ترميز إضافية
يفتح صور WEBP الثابتة والمتحركة ذاتيًا دون الحاجة إلى برنامج ترميز "WebP Image Extensions" من Windows
يفتح صيغ HEIC وHEIF وAVIF من هواتف iPhone والكاميرات الحديثة دون الحاجة إلى شراء برامج ترميز مدفوعة
بنية حديثة 64 بت مع بيئة تشغيل مدمجة: لا حاجة لتثبيت برامج إضافية ولا يتطلب .NET Framework مسبقًا
تكبير فوري عند مؤشر الفأرة مع السحب والتحريك، وحفظ مسار الصوت والترجمة حسب اللغة المفضلة
شريط تحكم في الفيديو (تقديم وترجيع ووقت وكتم وصوت) مع خيار "فتح عنوان URL.." لتشغيل الروابط المباشرة
قوائم النقر بالزر الأيمن والأوسط تضع جميع الإجراءات - تدوير، ترجمة، نقل، حذف - مباشرة على الصورة أو الفيديو
خلفية "Ambilight" تملأ الحواف الجانبية بألوان مستوحاة من الصورة لتجربة مشاهدة مريحة
مشاركة المجلدات مع الهاتف: يشغل Share Manager خادم SFTP خاصًا باقتران QR مع جدولة العمليات
ترجمة نصوص الصور عبر OCR اختياريًا: تعرف Tesseract دون إنترنت وترجمة قابلة للضبط (Ollama / LibreTranslate)
تعيين كعارض صور افتراضي لصيغ JPG وPNG وGIF وBMP وTIFF وWEBP وHEIC وAVIF وSVG
مفتاح Del يرسل إلى سلة المحذوفات إن وجدت، ومفتاح U يتراجع عن آخر 50 عملية متتالية
يفتح ملفات ZIP أو CBZ كمجلدات عادية: تصفح وقراءة القصص المصورة والمسح الضوئي دون فك الضغط أولاً
تعديل واقتصاص الصور في نافذة مستقلة (فرشاة، مستطيل، شكل بيضاوي، إطار اقتصاص تفاعلي، Ctrl+Z)
تحويل صور WEBP أو APNG أو GIF المتحركة إلى فيديو MP4 عادي مع شريط تقدم وإيقاف مؤقت
شاشة مخصصة لتشغيل الموسيقى: غلاف الألبوم أو مؤثرات الأمواج، بيانات المقطع ومؤقت النوم
مفتوح المصدر بالكامل، دون اشتراك، دون إعلانات تجارية، ودون أي جمع لبيانات الاستخدام
```

### Product features (HI - one per line, same order, ≤200 chars each)
```
मीडिया और फ़ाइल मैनेजर: प्रति फ़ाइल एक कुंजी से छाँटें (मूव/कॉपी/नाम बदलें/हटाएँ) और डिस्क व SFTP/SMB सर्वर पर फ़ाइलें प्रबंधित करें
मीडिया पर वैकल्पिक प्राप्तकर्ता पैनल: माउस से एक हाथ से छाँटने के लिए चित्र के ऊपर स्थित गंतव्य फ़ोल्डरों पर क्लिक करें
बड़े फ़ोटो और वीडियो संग्रहों के लिए फ़ुल-स्क्रीन स्लाइडशो और त्वरित थंबनेल पैनल नेविगेशन
अंतर्निहित VLC इंजन से वीडियो प्लेबैक: बिना अतिरिक्त कोडेक पैक के H.264/MP4, AVI, MKV, VP9, ZMBV आदि चलाता है
स्थिर और एनिमेटेड WEBP फ़ाइलें बिना Windows "WebP Image Extensions" कोडेक के अपने आप खोलता है
iPhone और आधुनिक कैमरों के HEIC, HEIF और AVIF प्रारूप बिना किसी सशुल्क कोडेक के खोलता है
आधुनिक 64-बिट ऐप: कुछ भी अतिरिक्त इंस्टॉल करने की ज़रूरत नहीं, कोई .NET Framework पूर्व-आवश्यकता नहीं
माउस कर्सर पर ड्रैग-टू-पैन ज़ूम, और भाषा के अनुसार ऑडियो ट्रैक व सबटाइटल प्राथमिकता याद रखता है
वीडियो नियंत्रण बार (सीक, समय, म्यूट, वॉल्यूम) और नेटवर्क पते से सीधे चलाने के लिए "URL खोलें.." सुविधा
दाएँ और मध्य क्लिक मेनू हर क्रिया - घुमाना, अनुवाद, स्थानांतरण, हटाना - सीधे चित्र या वीडियो पर प्रदान करते हैं
"Ambilight" बैकग्राउंड खाली पट्टियों को चित्र के रंगों के अनुसार भरकर देखने का शानदार अनुभव देता है
फ़ोन के साथ साझा करें: Share Manager निजी SFTP सर्वर चलाता है और शेड्यूल पर फ़ाइल ऑपरेशन निष्पादित करता है
वैकल्पिक ऑन-इमेज OCR अनुवाद: ऑफ़लाइन Tesseract OCR और आपकी पसंद का अनुवादक (Ollama / LibreTranslate)
JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF और SVG के लिए अपने डिफ़ॉल्ट इमेज व्यूअर के रूप में सेट करें
Del उपलब्ध होने पर रीसायकल बिन का उपयोग करता है; U नाम बदलने सहित अंतिम 50 क्रियाओं को पूर्ववत करता है
ZIP या CBZ को फ़ोल्डर की तरह खोलता है: कॉमिक्स या स्कैन बिना अनपैक किए ब्राउज़, ज़ूम और अनुवाद करें
अलग विंडो में फ़ोटो एडिट और क्रॉप करें - ब्रश, आयत, दीर्घवृत्त, लाइव क्रॉप फ़्रेम और Ctrl+Z का समर्थन
एनिमेटेड WEBP, APNG या GIF को सीक बार और पॉज़ के साथ सामान्य MP4 वीडियो में बदलें
संगीत के लिए समर्पित स्क्रीन: एल्बम कवर या वेव एनीमेशन, ट्रैक विवरण और स्लीप टाइमर
ओपन सोर्स, कोई खाता आवश्यक नहीं, कोई विज्ञापन नहीं, कोई टेलीमेट्री ट्रैकिंग नहीं
```

### Product features (BN - one per line, same order, ≤200 chars each)
```
মিডিয়া ও ফাইল ম্যানেজার: প্রতি ফাইলে এক কী চেপে সাজান (সরান/কপি/নামবদল/মুছুন) এবং ডিস্ক ও SFTP/SMB সার্ভারে ফাইল পরিচালনা করুন
ছবির ওপর ঐচ্ছিক প্রাপক প্যানেল: মাউস দিয়ে এক হাতে সাজানোর জন্য ছবির ওপর গন্তব্য ফোল্ডারে ক্লিক করুন
বিশাল ছবি ও ভিডিও সংগ্রহের জন্য ফুল-স্ক্রিন স্লাইডশো এবং দ্রুত থাম্বনেইল প্যানেল নেভিগেশন
বিল্ট-ইন VLC ইঞ্জিনের মাধ্যমে ভিডিও প্লেব্যাক: কোনো অতিরিক্ত কোডেক প্যাক ছাড়াই H.264/MP4, AVI, MKV, VP9 চালায়
Windows-এর কোনো অতিরিক্ত কোডেক ছাড়াই স্থির ও অ্যানিমেটেড WEBP ছবি সরাসরি খোলে
iPhone ও আধুনিক ক্যামেরার HEIC, HEIF এবং AVIF ফরম্যাট কোনো পেইড কোডেক ছাড়াই খোলে
আধুনিক 64-বিট সংস্করণ: অতিরিক্ত কিছু ইনস্টল করার প্রয়োজন নেই, .NET Framework-এর দরকার নেই
মাউস কার্সারে ড্র্যাগ-টু-প্যান জুম, এবং ভাষা অনুযায়ী অডিও ট্র্যাক ও সাবটাইটেল মনে রাখে
ভিডিও নিয়ন্ত্রণ বার (সিক, সময়, মিউট, ভলিউম) এবং নেটওয়ার্ক ঠিকানা থেকে চালাতে "URL খুলুন.." সুবিধা
রাইট ও মিডল ক্লিক মেনু ছবির ওপরই সব প্রয়োজনীয় অ্যাকশন প্রদান করে
"Ambilight" ব্যাকগ্রাউন্ড ছবির রঙের সাথে মিল রেখে কালো বারগুলো পূরণ করে
ফোনের সাথে শেয়ার করুন: Share Manager একটি ব্যক্তিগত SFTP সার্ভার চালায় এবং সময়মতো ফাইল অপারেশন পরিচালনা করে
ছবিতে ঐচ্ছিক OCR অনুবাদ: অফলাইন Tesseract OCR এবং পছন্দসই অনুবাদক কনফিগারেশন (Ollama / LibreTranslate)
JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF এবং SVG-এর জন্য ডিফল্ট ইমেজ ভিউয়ার হিসেবে সেট করুন
Del রিসাইকেল বিন ব্যবহার করে; U রিনেমসহ সর্বশেষ 50টি অপারেশন আনডু করে
ZIP বা CBZ ফোল্ডারের মতো খোলে: কমিক বা স্ক্যান করা ছবি আনপ্যাক না করেই ব্রাউজ, জুম ও অনুবাদ করুন
নিজস্ব উইন্ডোতে ছবি আঁকুন ও ক্রপ করুন - ব্রাশ, আয়তক্ষেত্র, উপবৃত্ত, লাইভ ক্রপ ফ্রেম এবং Ctrl+Z
অ্যানিমেটেড WEBP, APNG বা GIF-কে সাধারণ MP4 ভিডিওতে রূপান্তর করুন
সঙ্গীতের জন্য স্বতন্ত্র স্ক্রিন: অ্যালবাম কভার বা তরঙ্গ অ্যানিমেশন, ট্র্যাকের বিবরণ ও স্লিপ টাইমার
ওপেন সোর্স, কোনো অ্যাকাউন্টের প্রয়োজন নেই, সম্পূর্ণ বিজ্ঞাপনহীন ও ট্র্যাকিংমুক্ত
```

### Product features (UR - one per line, same order, ≤200 chars each)
```
میڈیا اور فائل مینیجر: ہر فائل کے لیے ایک کلید سے ترتیب دیں (منتقل/نقل/نام بدلیں/حذف) اور ڈسک اور SFTP/SMB سرورز پر فائلیں منظم کریں
میڈیا پر اختیاری وصول کنندگان کا پینل: ماؤس کے ذریعے ایک ہاتھ سے چھانٹنے کے لیے تصویر کے اوپر منزل کے فولڈرز پر کلک کریں
بڑے فوٹو اور ویڈیو مجموعوں کے لیے فل سکرین سلائیڈ شو اور فوری تھمب نیل پینل نیویگیشن
بلٹ ان VLC انجن کے ذریعے ویڈیو پلے بیک: بغیر کسی اضافی کوڈیک پیک کے H.264/MP4، AVI، MKV، VP9 چلاتا ہے
ونڈوز کے اضافی کوڈیک کے بغیر ساکن اور متحرک WEBP فائلیں خود بخود کھولتا ہے
آئی فون اور جدید کیمروں کے HEIC، HEIF اور AVIF فارمیٹس بغیر کسی معاوضے والے کوڈیک کے کھولتا ہے
جدید 64 بٹ ورژن: کچھ بھی اضافی انسٹال کرنے کی ضرورت نہیں، .NET Framework کی کوئی شرط نہیں
ماؤس کرسر پر ڈریگ ٹو پین زوم، اور زبان کے لحاظ سے آڈیو ٹریک اور سب ٹائٹلز کو یاد رکھتا ہے
ویڈیو کنٹرول بار (تلاش، وقت، خاموش، آواز) اور نیٹ ورک ایڈریس سے براہ راست چلانے کے لیے "یو آر ایل کھولیں.."
دائیں اور درمیانے کلک کے مینو تمام ضروری افعال براہ راست تصویر یا ویڈیو پر فراہم کرتے ہیں
"Ambilight" پس منظر تصویر کے رنگوں کے مطابق خالی جگہوں کو بھر کر دلکش نظارہ دیتا ہے
فون کے ساتھ شیئر کریں: Share Manager ایک نجی SFTP سرور چلاتا ہے اور شیڈول کے مطابق فائل آپریشنز انجام دیتا ہے
تصویر پر اختیاری OCR ترجمہ: آف لائن Tesseract OCR اور قابل ترتیب مترجم (Ollama / LibreTranslate)
JPG، PNG، GIF، BMP، TIFF، WEBP، HEIC، AVIF اور SVG کے لیے اپنے ڈیفالٹ امیج ویور کے طور پر سیٹ کریں
Del دستیاب ہونے پر ری سائیکل بن کا استعمال کرتا ہے؛ U نام تبدیل کرنے سمیت آخری 50 کارروائیوں کو واپس کرتا ہے
ZIP یا CBZ کو فولڈر کی طرح کھولتا ہے: کامکس یا اسکینز کو پہلے کھولے بغیر براؤز، زوم اور ترجمہ کریں
مخصوص ونڈو میں تصاویر میں ترمیم اور کراپ کریں - برش، مستطیل، بیضوی، لائیو کراپ فریم اور Ctrl+Z
متحرک WEBP، APNG یا GIF کو سیک بار اور توقف کے ساتھ عام MP4 ویڈیوز میں تبدیل کریں
موسیقی کے لیے مخصوص اسکرین: البم کور یا ویو اینیمیشن، ٹریک کی تفصیلات اور سلیپ ٹائمر
اوپن سورس، کسی اکاؤنٹ کی ضرورت نہیں، اشتہارات سے پاک اور بغیر کسی ٹریکنگ کے
```

### Product features (ZH-HANS - one per line, same order, ≤200 chars each)
```
媒体与文件管理器：每个文件单键整理（移动/复制/重命名/删除至快捷文件夹），并在磁盘与 SFTP/SMB 服务器上管理文件
画面悬浮目标面板：直接在图像上点击目标文件夹，用鼠标单手轻松整理
全屏幻灯片播放与快速缩略图面板，轻松浏览海量照片和视频合辑
内置 VLC 引擎播放视频：支持 H.264/MP4、AVI、MKV、VP9、ZMBV 等多种格式，无需额外安装解码器包
原生打开静态及动态 WEBP 图像，无需安装 Windows "WebP Image Extensions" 扩展
原生打开 iPhone 和相机拍摄的 HEIC、HEIF 及 AVIF 格式，无需购买任何付费解码插件
现代化 64 位独立构建版本：自带完整运行时，无需安装其他组件，无 .NET Framework 依赖
鼠标指针精准缩放与拖拽平移，并能按语言自动记忆音轨与字幕选择
完备的视频控制栏（进度条、时长、静音、音量）及"打开 URL.."网络流媒体播放功能
右键与中键快捷菜单将旋转、翻译、移动、删除等全部操作呈现在图像与视频上方
"Ambilight" 流光透视背景根据画面边缘色彩填充黑边，呈现沉浸式视觉感受
与手机共享文件夹：Share Manager 通过二维码配对开启私有 SFTP 服务，并支持定时文件任务
可选的图像 OCR 翻译：离线 Tesseract 文字识别与可自由配置的翻译接口（Ollama / LibreTranslate）
可设为 JPG、PNG、GIF、BMP、TIFF、WEBP、HEIC、AVIF 及 SVG 的默认看图软件
Del 键优先删除至回收站；U 键支持撤销包括重命名在内的最近 50 步操作
像普通文件夹一样直接打开 ZIP 或 CBZ 漫画与扫描文件，无需提前解压
独立窗口标注与裁剪图像：支持画笔、矩形、椭圆、实时裁剪框与 Ctrl+Z 撤销
将动态 WEBP、APNG 或 GIF 转换为带进度条和暂停功能的普通 MP4 视频
专属音乐播放界面：支持专辑封面与波形粒子动效、曲目详情及睡眠倒计时
开源软件，无需注册账户，无任何内置广告，无遥测追踪
```

### What's new in this version (Store "release notes" field) - submitted 2026-09-01 (26.7.14.1801 -> 26.9.1.1550)

> This is the text that went into the submission's **"What's new in this version"** box - **capped at
> 1500 characters** counted after the line breaks become CRLF, as Partner Center counts them (measured
> 2026-10-13: EN 1496, RU 1479; the RU block was 1504 and lost the end of its second bullet to fit). EN is the
> primary; RU is the RU listing's copy. Keep
> it short: the Store shows only the first lines on the product page, so the biggest, most visible
> changes come first.
>
> It covers seven releases because the previous certified version was 26.7.14.1801 - see the status
> note above. **The next block replaces this one and starts from 26.9.1.1550**, assuming this
> submission certifies; confirm that in Partner Center rather than assuming it. Don't add detail back
> into either block without re-measuring against the cap - `Build-ListingData.ps1` enforces it and will
> refuse to regenerate the CSV.

**EN**
```
- Rebuilt as a modern 64-bit program with its own runtime: nothing extra to install, no .NET Framework. Your settings carry over.
- HEIC, HEIF, AVIF and animated WEBP open everywhere with no extra codec, and now reopen instantly the second time.
- Del uses the Recycle Bin where there is one and says so honestly when there is not; U takes back the last 50 operations, renames and deletions included.
- A ZIP or CBZ opens like a folder: browse, zoom and translate a comic or a folder of scans with nothing unpacked in advance.
- New: mark up and crop a picture in its own editing window, then save over the original or beside it in any of six formats.
- New: turn an animated WEBP, APNG or GIF into an ordinary MP4 that replaces it, with a seek bar, a pause and full colour.
- New: music gets a screen of its own - album cover or a wave animation, track details, an end-of-track action and a sleep timer.
- Video: a full control bar, audio and subtitle tracks remembered by language, and "Open URL.." for network video.
- Zoom at the mouse cursor on the numeric keypad's grey keys with drag-to-pan, and menus right on the picture or video.
- A destination folder that is switched off or asleep says so on the first press instead of after twenty timeouts; a missing last folder is created for you.
- Keyboard shortcuts can be reassigned, and the settings that were listed but did nothing now do what they say.
- The interface speaks 13 languages, and folder sharing is safer by default.
```

**RU**
```
- Пересобрано как современная 64-битная программа со своей средой выполнения: ничего доустанавливать не нужно, .NET Framework не требуется. Настройки сохраняются.
- HEIC, HEIF, AVIF и анимированные WEBP открываются везде без дополнительных кодеков, а повторно открываются мгновенно.
- Del отправляет файл в Корзину там, где она есть, и честно предупреждает, когда её нет; U отменяет последние 50 операций, включая переименования.
- ZIP или CBZ открывается как папка: комикс или папку сканов можно листать, масштабировать и переводить, ничего не распаковывая заранее.
- Новое: разметка и кадрирование картинки в отдельном окне - поверх оригинала или рядом, в одном из шести форматов.
- Новое: анимированный WEBP, APNG или GIF превращается в обычный MP4 - с перемоткой, паузой и полным цветом.
- Новое: у музыки появился свой экран - обложка альбома или анимация из волн, сведения о дорожке, действие в конце и таймер сна.
- Видео: панель управления, выбор аудиодорожки и субтитров по языку и "Открыть по адресу.." для сетевого видео.
- Масштабирование под курсором на серых клавишах цифрового блока и меню прямо на картинке или видео.
- Папка-получатель, которая выключена или спит, сообщает об этом сразу, а не после двадцати таймаутов; недостающая последняя папка создаётся сама.
- Горячие клавиши можно переназначать, а настройки, которые были в списке, но ничего не делали, теперь работают.
- Интерфейс говорит на 13 языках, а раздача папок по умолчанию безопаснее.
```

**UK**
```
- Перезібрано як сучасну 64-бітну програму зі своїм середовищем виконання: нічого додатково встановлювати не потрібно, без .NET Framework. Налаштування зберігаються.
- HEIC, HEIF, AVIF та анімовані WEBP відкриваються всюди без додаткових кодеків, а повторно відкриваються миттєво.
- Del надсилає файл до Кошика там, де він є, і чесно попереджає, коли його немає; U скасовує останні 50 операцій, включно з перейменуваннями.
- ZIP або CBZ відкривається як тека: комікс або теку сканів можна гортати, масштабувати та перекладати, нічого не розпаковуючи заздалегідь.
- Нове: розмітка та кадрування картинки в окремому вікні - поверх оригіналу або поруч, в одному із шести форматів.
- Нове: анімований WEBP, APNG або GIF перетворюється на звичайний MP4 - з перемоткою, паузою та повним кольором.
- Нове: у музики з'явився свій екран - обкладинка альбому або анімація з хвиль, відомості про доріжку, дія в кінці та таймер сну.
- Відео: панель керування, вибір аудіодоріжки та субтитрів за мовою та "Відкрити за адресою.." для мережевого відео.
- Масштабування під курсором на сірих клавішах цифрового блоку та меню прямо на зображенні або відео.
- Тека-отримувач, яка вимкнена або спить, повідомляє про це одразу, а не після двадцяти таймаутів; відсутня остання тека створюється сама.
- Гарячі клавіші можна перепризначити, а налаштування, які були в списку, але нічого не робили, тепер працюють.
- Інтерфейс говорить 13 мовами, а роздача тек за замовчуванням безпечніша.
```

> **The other ten languages** (DE, IT, ES, FR, PT-BR, AR, HI, BN, UR, ZH-HANS) carry the English
> text per WINDOWS-STORE rule 16 ("What's new is authored only in the languages the product proofreads.
> The machine-translated columns carry the English text").


### runFullTrust justification (keep under ~1000 chars)

> **Over budget - decide before pasting (pre-existing, not new).** This block is **~1340 chars** vs the
> ~1000 stated above. It grew with the 26.7.15 Share-hardening copy, which was never submitted, so the
> length has never actually been tested against the Partner Center field - the live listing still has
> the older, shorter text. If the field rejects or truncates it, cut the security detail in the
> "folder sharing" bullet (the per-install password, "LAN only", connection cap and idle timeout are
> all restated in the privacy policy below and in the Store description); keep all four bullets and
> the "no telemetry" line, which are what the capability review actually asks for.
```
Fast Media Sorter & Sharing for Windows is a full-trust Win32 desktop package (a WinForms viewer plus a .NET companion),
not a UWP app, so runFullTrust is required to run as normal desktop processes and use the Win32 APIs
its core features depend on:
- File access: read/copy/move/rename/delete the user's own image/video files across arbitrary folders
  and network shares - the app's entire purpose, only on files and folders the user opens.
- Media playback: hosts native LibVLC (H.264/MP4, AVI, MKV, VP9, etc.), bundled in the package, and
  uses GDI+ for image rendering.
- Optional folder sharing: a bundled companion runs a local SFTP server for folders the user chooses,
  reachable from the user's own phone (opt-in; an inbound firewall rule is added only when enabled).
  While a share runs, devices on the current network (including public Wi-Fi) can reach the PC on the
  share port, protected by a per-install password; a "LAN only" switch keeps it off the internet, and
  the number of simultaneous connections is user-limited (default 10). Idle connections are closed after
  a few hours.
- Optional file broadcasts: a separately enabled HTTP listener in the same companion serves one read-only resource per expiring token; HTTP is unencrypted, and the existing program firewall rule covers it.
- Optional OCR/translation: may call a user-configured translation endpoint (e.g. a local Ollama
  instance); OCR runs locally with bundled Tesseract data.
No user data is collected; no telemetry. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite
```

### Privacy policy (hosted as docs/privacy.html; key points)

The exchange-enabled build's disclosure is the complete `DEVICE-EXCHANGE` item DE text below. The
server component's package availability remains governed by the existing shipping gate; this disclosure
does not add that component to the Store package. The hosted policy carries the same text in EN, RU
and UK, and [the portal hand-off](PORTAL_HONEST_TEXT_234.md) identifies the other maintained surfaces.

> The exchange server connects your devices, so it knows the names of your account, devices, folders and broadcasts, sees which devices are online, and how much is sent and when. It cannot open what goes through it: folders and their files, broadcasts and the clipboard are encrypted on your device and opened only on your other devices or by the people you gave access to. To look inside, whoever runs the server would have to add a device of their own to your account, and it would appear in your list of devices.

> Via Google Drive, files and the clipboard are kept in your own Google account, like your other files on Google Drive; no exchange server takes part.

> **Microsoft Store has no data-safety form** in the Play sense. Its "answers" are three texts: the
> privacy-policy URL (docs/privacy.html), the `runFullTrust` justification above and the description.
> This block is written FROM docs/privacy.html (ticket 037 Ф-5, INSTALL-TRUST rule 6), and the "what the
> program never does" list of the install-trust page (`tools/trust-copy.json`) says the same thing. Change
> one of the three and change the other two in the same edit.

```
Fast Media Sorter & Sharing for Windows collects no personal data, has no accounts, no ads, and no telemetry/analytics.
It runs entirely on your device. The developer receives nothing about you or your use of the app, unless
you choose to send your logs yourself (see Diagnostic logs below).

What it accesses and why:
- Your media files: reads, displays, copies, moves, renames and deletes only the files and folders you open.
- Folder sharing (optional, opt-in): the bundled Share Manager companion runs a local SFTP server so your
  own phone can browse the folders you choose, over your local network or the internet. The password and
  the server host key are generated and kept only on your PC; files go directly between your own devices,
  never through us or any third party. While a share is on, any device on your current network (home,
  office, or a public Wi-Fi) can reach the PC on the share port; access needs the password embedded in the
  QR/config you share, so treat that code as a key. "LAN only" keeps the share off the internet; idle
  connections close after a few hours. With internet access on, the app asks your own router to forward
  the share port (UPnP/NAT-PMP) and asks the public service check-host.net to confirm the port answers from
  outside, which shows that service your public address and port. While a share runs, the PC announces
  itself on the local network (mDNS) so a paired phone can find it again. Local-only connection counters
  are not telemetry and never leave the PC. In this Store package the firewall rule comes from the package
  manifest, and no Windows service is ever registered.
- Broadcasts for StreamsPlayer: a separate, explicitly enabled HTTP listener in the same worker. It serves one file or a frozen folder playlist per read-only token; links expire or can be revoked. HTTP is unencrypted, including the token. Exported .spres documents and QR codes are secrets. Its own port mapping and external probe follow the network policy and disclose the public address and port to the same services described above. Counters stay on this PC; no author service receives media or tokens. The existing program firewall rule covers both listeners.
- Always-on sharing (GitHub and winget builds only, never this package): a Windows service, only if you
  ask for it and approve an administrator prompt; it runs as LOCAL SERVICE with access only to the folders
  you share, and "Remove the server role" or uninstalling takes it away. Nothing collected changes.
- Network, only for what you use: on-image translation sends the recognized text to the endpoint YOU
  configure (a local Ollama model or a LibreTranslate server); OCR runs fully offline. Otherwise the app
  fetches only components you ask for or approve in a prompt that names them - OCR language packs,
  FFmpeg for "Replace with video", the OCR/video runtimes when a build did not bring them, the Ollama
  installer and translation models, doc-html-translate through winget - and sends nothing about your
  files with any of them.
- Diagnostic logs: written locally. "Send the logs to the author" packs them, shows what is inside and
  opens a message in YOUR mail program; nothing is sent until you send it yourself.
Local files it writes: settings (registry - inside the package container in this Store build), OCR data,
FFmpeg, the translation and decode caches under %LOCALAPPDATA%\SZA\FastMediaSorter, and sharing
configuration/stats under %LOCALAPPDATA%\FastMediaSorterCompanion. These never leave your device.
Data sharing: none with the developer. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite. Contact: sza@ukr.net
```

---

_See [publishing/msix/README.md](../../publishing/msix/README.md) for packaging detail and [docs/privacy.html](../privacy.html)
for the live privacy-policy page._
