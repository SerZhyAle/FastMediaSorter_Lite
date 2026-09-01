# Publishing FastMediaSorter LITE to the Microsoft Store (MSIX)

The concrete, app-specific playbook for this repo. Adapted from the reusable CyrFlip playbook
(`P:\WINDOWS\CyrFlip\STORE_PUBLISHING.md`). Work top to bottom.

> **Already published?** This doc is the **first** publish. To ship a change to the live listing
> (new screenshots, search terms, or a new build), follow
> [SPECIFICATION_STORE_UPDATE.md](../specifications/done/SPECIFICATION_STORE_UPDATE.md) instead.

## Why this path (Path A: MSIX)

- **Developer account is free** (individuals since late 2025, companies since May 2026).
- **Microsoft re-signs the MSIX during certification** - no paid code-signing certificate needed.
  (The alternative "unpackaged exe/MSI" path *does* require a paid cert chaining to a Microsoft-trusted root.)
- Store-signed + Store-distributed also defuses antivirus heuristic false positives better than anything else.

This is **in addition to** the existing distribution channels (GitHub release EXE/ZIP and winget) -
nothing here changes those. See [SPECIFICATION_WINGET_PUBLISHING.md](../specifications/done/SPECIFICATION_WINGET_PUBLISHING.md)
and [SPECIFICATION_GITHUB_STORE.md](../specifications/done/SPECIFICATION_GITHUB_STORE.md) for those paths.

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
a **static and an animated `.webp`** (ImageSharp decodes them in-process; no Windows codec involved),
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
2. **Create a new product → MSIX or PWA app** → reserve the app name **FastMediaSorter LITE**.
3. **Product ▸ Product identity** → copy three values into the build command:
   - `Package/Identity/Name` → `-IdentityName`
   - `Package/Identity/Publisher` (e.g. `CN=…`) → `-Publisher`
   - `Package/Properties/PublisherDisplayName` → `-PublisherDisplayName`
4. Build the Store package (no `-SelfSign`) and upload the **unsigned** `.msix`:
   ```powershell
   cd publishing\msix
   .\build-msix.ps1 -ReleaseVersion "<the tag's YY.M.D.HHmm>" `
                    -IdentityName "<Name>" -Publisher "<CN=…>" -PublisherDisplayName "<…>"
   ```
   Pass `-ReleaseVersion` whenever the package belongs to a release: without it the projects stamp
   the current minute, so a package built an hour after the tag carries a different version than the
   GitHub and winget artifacts of the same release. Omit it only for a throwaway local build.

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
For FastMediaSorter LITE the rating is **live** on the Microsoft storefront:

| Field | Value |
| --- | --- |
| Global Rating ID | `7d9b315a-f211-8505-80d0-3f4bee633770` |
| Product title | FastMediaSorter LITE |
| Company | SZA |
| Storefront | Microsoft |
| Rating date | 2026-06-18 |

**The Global Rating ID is portable.** To list the app on another IARC-licensed storefront, paste this
ID when asked for a "Global Rating ID" / "IARC Certificate ID" instead of re-doing the questionnaire.

**When a re-rating is required:** the rating is bound to the questionnaire answers, so ordinary version
bumps do NOT need re-rating. You must re-run the IARC questionnaire only if a change would alter those
answers - e.g. adding in-app purchases, ad networks, or user-to-user sharing/social features. The
current app (local media viewer/sorter, optional user-configured translation, no ads/accounts/UGC
sharing) keeps the General rating across normal updates.

If a rating ever looks wrong, request a rating check via the link in the IARC "Live Rating Notice"
email (1-3 business days).

---

## Text templates

> **Bulk-import CSV:** [publishing/store/listingData.csv](../../publishing/store/listingData.csv) is Partner
> Center's own "Import listing data" CSV format (`Field,ID,Type,default,en-us,ru`), kept in the repo so
> each release can update it in place instead of rebuilding it from scratch. It carries the same copy as
> the templates below - Description, ReleaseNotes ("What's new"), ShortDescription, Feature1-15,
> SearchTerm1-7 - plus every screenshot/logo asset URL and blank field from the last export, untouched.
> At release time: update Description/ReleaseNotes/features in this doc first, then mirror the exact
> same text into the CSV's `en-us`/`ru` columns for the same `Field` rows (respect the file's own CSV
> quoting - double any literal `"` inside a quoted cell, keep embedded line breaks as bare `\n` inside the
> quotes). Re-export from Partner Center after any manual edit there and diff before overwriting this
> file, so asset URLs never go stale. Import it in Partner Center via the submission's listing page →
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
> **Rebrand note (2026-07):** the product **title stays "FastMediaSorter LITE"** in Partner Center
> (frozen reserved name = the update anchor - do NOT change it). Only the **Description** text below is
> refreshed to lead with the new brand name "Fast Media Sorter for Windows". Paste the EN block into the
> listing's Description at the next submission; the RU block is optional copy for a Russian-market listing.
>
> **Scope note (.NET 10):** the Store package is **x64-only and contains the mainline viewer alone**.
> Do NOT mention the 32-bit `FastMediaSorter_x86.exe` sibling in the Store listing - it exists only in
> the installer/winget/GitHub channels. Store copy describes the new build's *user-visible* wins
> (animated WEBP, VLC-only playback, no .NET Framework prerequisite), not the packaging split.

### Description (EN)
> First sentence must be a short, self-contained hook - the Store card truncates the description to
> its opening. Do NOT start with the "(published as ..)" parenthetical; it buries the lead.
```
Sort thousands of photos and videos in minutes. Fast Media Sorter is a fast, keyboard-driven viewer
and sorter for images and video on Windows.

Open a folder and fly through it: full-screen slideshow, quick panel navigation, and one-key Move /
Copy / Rename / Delete. Assign folders to hotkeys and file each item with a single press - or turn on
the recipients panel to click those destination folders right over the image and sort one-handed with
the mouse. It plays a broad range of video through its built-in VLC engine - H.264/MP4, AVI, MKV, VP9,
ZMBV and more - and fills letterbox/pillarbox bars with a matching "ambilight" background.

Rebuilt as a modern 64-bit app that carries its own runtime: there is nothing extra to install and no
.NET Framework prerequisite. Static and animated WEBP images open everywhere, because the app decodes
them itself instead of needing the Windows "WebP Image Extensions" codec that some editions of Windows
lack, and so do HEIC/HEIF and AVIF - the formats iPhones and modern cameras produce - with no paid codec
required. Video always runs through the built-in VLC engine, so it plays the same on systems where the
retired Internet Explorer component is no longer present, now with a full control bar (seek, volume,
audio track and subtitle picking) and "Open URL.." to play straight off a network address. Zoom at the
mouse cursor with drag-to-pan, and right-click/middle-click menus put every action right on the picture
or video.

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

Share folders with your phone: the bundled companion "Fast Media Sorter: Share Manager" turns this PC
into a private SFTP server for folders you pick, so the Fast Media Sorter Android app can browse them -
on your home Wi-Fi or over the internet - after a one-time QR pairing. Sharing is strictly opt-in and
stays between your own devices; nothing is uploaded to any third party. Sharing runs while you are
signed in. A separate always-on Server edition, which hosts the same sharing as a Windows service, is
not part of the Store version - it is available from the project website and winget.

Optional on-image OCR translation recognizes text in a picture (fully offline, Tesseract) and overlays
a translation using a provider you configure yourself - a local Ollama model or a LibreTranslate
endpoint. OCR works without any network connection.

No account, no ads, no telemetry. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite

On the Microsoft Store this app is published as "FastMediaSorter LITE".
```

### Description (RU - optional Russian-market listing)
```
Разберите тысячи фото и видео за минуты. Fast Media Sorter - быстрый просмотрщик и сортировщик
изображений и видео для Windows, управляемый с клавиатуры.

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
которые снимают iPhone и современные камеры - без каких-либо платных кодеков. Видео всегда идёт
через встроенный движок VLC, поэтому оно одинаково воспроизводится и на системах, где компонента
Internet Explorer больше нет, и теперь с полноценной панелью управления (перемотка, громкость,
выбор аудиодорожки и субтитров) и командой "Открыть по адресу.." для воспроизведения прямо по
сетевому пути. Масштабирование под курсором мыши с перетаскиванием, а меню по правому/среднему
клику выносят все действия прямо на изображение или видео.

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

Раздавайте папки на телефон: программа-компаньон "Fast Media Sorter: Share Manager" превращает этот
ПК в частный SFTP-сервер для выбранных вами папок, чтобы Android-приложение Fast Media Sorter
открывало их - в домашней сети Wi-Fi или через интернет - после однократного подключения по QR-коду.
Раздача включается только по вашему желанию и остаётся между вашими устройствами; никуда на сторону
ничего не выгружается. Раздача работает, пока вы вошли в систему. Отдельная серверная редакция,
которая поднимает ту же раздачу как службу Windows, в версию из Store не входит - она доступна на
сайте проекта и через winget.

Необязательный перевод текста прямо на картинке: распознавание полностью офлайн (Tesseract) и
наложение перевода через выбранного вами провайдера - локальную модель Ollama или сервер
LibreTranslate. Распознавание работает без интернета.

Без аккаунта, без рекламы, без телеметрии. Открытый исходный код:
https://github.com/SerZhyAle/FastMediaSorter_Lite

В Microsoft Store приложение публикуется как "FastMediaSorter LITE".
```

### Product features (one per line, ≤200 chars each)
```
Fast keyboard-driven sorting: one-key Move / Copy / Rename / Delete with hotkey-assigned folders
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
Share folders to your phone: a bundled tray companion runs a private, opt-in SFTP server paired by QR (Wi-Fi or internet)
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
Быстрая сортировка с клавиатуры: перемещение / копирование / переименование / удаление одной клавишей с папками на горячих клавишах
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
Раздача папок на телефон: программа-компаньон поднимает частный SFTP-сервер по вашему желанию, подключение по QR-коду (Wi-Fi или интернет)
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
> `Description`, `ReleaseNotes` and `Feature1..20` cells of
> [publishing/store/listingData.csv](../../publishing/store/listingData.csv) (both `en-us` and `ru`),
> carries every other cell through unchanged, and refuses to write if the copy breaks a Partner
> Center limit (1500 for What's new, 10000 for Description, 20 features of 200 chars). `-Check`
> verifies the committed CSV still matches and exits 1 if not. Import it in Partner Center via the
> submission's listing page -> "Import listing data". **After editing anything by hand in Partner
> Center, re-export and diff before overwriting the file** - it also holds screenshot and logo asset
> URLs that only a real export can produce.

### What's new in this version (Store "release notes" field) - submitted 2026-09-01 (26.7.14.1801 -> 26.9.1.1550)

> This is the text that went into the submission's **"What's new in this version"** box - **capped at
> 1500 characters** (measured: EN 1485, RU 1493). EN is the primary; RU is the RU listing's copy. Keep
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
- HEIC, HEIF, AVIF и анимированные WEBP открываются везде без дополнительных кодеков, а медленные форматы теперь открываются повторно мгновенно.
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

### runFullTrust justification (keep under ~1000 chars)

> **Over budget - decide before pasting (pre-existing, not new).** This block is **~1340 chars** vs the
> ~1000 stated above. It grew with the 26.7.15 Share-hardening copy, which was never submitted, so the
> length has never actually been tested against the Partner Center field - the live listing still has
> the older, shorter text. If the field rejects or truncates it, cut the security detail in the
> "folder sharing" bullet (the per-install password, "LAN only", connection cap and idle timeout are
> all restated in the privacy policy below and in the Store description); keep all four bullets and
> the "no telemetry" line, which are what the capability review actually asks for.
```
FastMediaSorter LITE is a full-trust Win32 desktop package (a WinForms viewer plus a .NET companion),
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
- Optional OCR/translation: may call a user-configured translation endpoint (e.g. a local Ollama
  instance); OCR runs locally with bundled Tesseract data.
No user data is collected; no telemetry. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite
```

### Privacy policy (hosted as docs/privacy.html; key points)
```
FastMediaSorter LITE collects no personal data, has no accounts, no ads, and no telemetry/analytics.
It runs entirely on your device.

What it accesses and why:
- Your media files: read/copy/move/rename/delete only the files and folders you open, to sort them.
- Folder sharing (optional, opt-in): if you enable it, the bundled Share Manager companion runs a local
  SFTP server so your own phone can browse the folders you choose, over your local network or the
  internet. Access credentials and the server host key are generated and stored only on your PC; the
  files are served directly between your own devices and are never sent to us or any third party. It
  keeps local-only connection counters (number/time of connections, files served) on your PC - this is
  not telemetry and never leaves the machine. Enabling it adds a Windows Firewall rule for the worker.
  While the share is on, any device on your current network (home, office, or a public Wi-Fi) can reach
  the PC on the share port; access requires the password embedded in the QR/config you share, so treat
  that code as a key. Turn "LAN only" on to keep the share off the internet entirely; turn sharing off
  when you are done. The password does not expire. When you enable internet access, the app asks the
  public service check-host.net to confirm the forwarded port is actually reachable, which discloses your
  public address and port to that service for the check.
- Network for translation (optional): only if you enable on-image translation, the app sends the
  recognized text to the translation endpoint YOU configure (a local Ollama model or a LibreTranslate
  server). OCR runs fully offline. Otherwise the app makes no network requests except optionally
  downloading OCR language packs on demand.
Local files it writes: settings (registry), OCR language data/cache, and sharing configuration/stats
under %LOCALAPPDATA%. These never leave your device.
Data sharing: none. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite. Contact: serzhyale@gmail.com
```

---

_See [publishing/msix/README.md](../../publishing/msix/README.md) for packaging detail and [docs/privacy.html](../privacy.html)
for the live privacy-policy page._