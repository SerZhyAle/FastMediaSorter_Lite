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

> **Status (2026-07-23):** the copy below is **still queued for the NEXT submission** - none of it is on
> the live listing yet. The last submission to pass certification is **26.7.14.1801 (Submission 2)**,
> which used the *previous, shorter* description. Everything since then (26.7.15 Share Manager hardening
> + recipients panel, and the whole 26.7.23.1127 .NET 10 rewrite - zoom, HEIC/AVIF, audio/subtitle
> tracks, Open URL, video controls, background file ops, media context menus, and the viewer-core fix
> pass) has queued up in this same unsubmitted copy. The "What's new" block below covers **the full span
> from 26.7.14.1801 to 26.7.23.1127** - Store users last saw 26.7.14.1801, so all of it is "new" to them.
> Apply the blocks (Description + Product features + What's new) at the next Store submission, not
> retroactively.
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
Open source, no account, no ads, no telemetry
```

### What's new in this version (Store "release notes" field) - next submission (26.7.14.1801 -> 26.7.23.1127)

> Paste this into the submission's **"What's new in this version"** box (Partner Center does not accept
> this remotely - it is a per-submission field, **capped at 1500 characters**). EN is the primary; RU is
> optional for the RU listing. Keep it short - the Store shows only the first lines on the product page.
>
> **Covers everything since the last live submission (26.7.14.1801).** That includes the 26.7.15 items
> (recipients panel, share hardening) and the full 26.7.23.1127 .NET 10 rewrite. Biggest/most visible
> changes first, since the Store truncates; the long viewer-core fix pass is summarized, not itemized.
> Both blocks below are pre-trimmed to fit the 1500-char cap (EN ~1295, RU ~1420) - don't add detail back
> without re-checking the length.

**EN**
```
- Rebuilt as a modern 64-bit program with its own runtime: nothing extra to install, no .NET Framework needed. Your settings carry over.
- HEIC, HEIF and AVIF (iPhone/camera photos) and animated WEBP now open everywhere, with no extra codec required.
- Video always plays through the built-in VLC engine, now with a full control bar (seek, mute, volume), audio/subtitle track picking remembered by language, and "Open URL.." for network video.
- Zoom at the mouse cursor on the numeric keypad's grey keys, with drag-to-pan.
- Right-click a video or middle-click a picture for a menu with everything you can do to it - rotate, translate, move, delete, and more.
- File moves/copies now run in the background so the next file appears instantly; moving onto an existing name saves as "name (2)" instead of failing.
- New: a recipients panel over the media - click destination folders right on the image to sort one-handed with the mouse.
- "Dynamic perspective" fades the Ambilight-style background bars into a soft halo around the photo.
- Folder sharing is safer by default: "LAN only" truly blocks internet access, idle/stalled connections drop, failed logins are logged, and connections can be capped.
- A large pass of fixes across browsing, file operations, slideshow, video and window state.
```

**RU**
```
- Пересобрано как современная 64-битная программа со своей средой выполнения: ничего доустанавливать не нужно, .NET Framework больше не требуется. Настройки сохраняются.
- HEIC, HEIF и AVIF (фото с iPhone и камер) и анимированные WEBP теперь открываются везде, без дополнительных кодеков.
- Видео всегда воспроизводится встроенным движком VLC, теперь с панелью управления (перемотка, звук, громкость), выбором аудиодорожки и субтитров по языку и командой "Открыть по адресу.." для сетевого видео.
- Масштабирование под курсором мыши на серых клавишах цифрового блока, с перетаскиванием.
- Правый клик по видео или средний клик по картинке - меню со всеми действиями: поворот, перевод, перемещение, удаление и другое.
- Перемещение/копирование файлов теперь идёт в фоне, следующий файл появляется мгновенно; перемещение на существующее имя сохраняется как "имя (2)" вместо ошибки.
- Новое: панель получателей поверх медиа - кликайте по папкам назначения прямо на изображении и сортируйте одной рукой мышью.
- "Динамическая перспектива" превращает полосы фона в стиле ambilight в мягкий ореол вокруг фото.
- Общий доступ безопаснее по умолчанию: режим "только локальная сеть" действительно блокирует интернет, простаивающие подключения сбрасываются, неудачные входы записываются в журнал, а число подключений можно ограничить.
- Большой пакет исправлений в просмотре, файловых операциях, слайд-шоу, видео и состоянии окна.
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
