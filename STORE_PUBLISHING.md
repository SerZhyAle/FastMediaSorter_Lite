# Publishing FastMediaSorter LITE to the Microsoft Store (MSIX)

The concrete, app-specific playbook for this repo. Adapted from the reusable CyrFlip playbook
(`P:\WINDOWS\CyrFlip\STORE_PUBLISHING.md`). Work top to bottom.

> **Already published?** This doc is the **first** publish. To ship a change to the live listing
> (new screenshots, search terms, or a new build), follow
> [SPECIFICATION_STORE_UPDATE.md](SPECIFICATION_STORE_UPDATE.md) instead.

## Why this path (Path A: MSIX)

- **Developer account is free** (individuals since late 2025, companies since May 2026).
- **Microsoft re-signs the MSIX during certification** - no paid code-signing certificate needed.
  (The alternative "unpackaged exe/MSI" path *does* require a paid cert chaining to a Microsoft-trusted root.)
- Store-signed + Store-distributed also defuses antivirus heuristic false positives better than anything else.

This is **in addition to** the existing distribution channels (GitHub release EXE/ZIP and winget) -
nothing here changes those. See [SPECIFICATION_WINGET_PUBLISHING.md](SPECIFICATION_WINGET_PUBLISHING.md)
and [SPECIFICATION_GITHUB_STORE.md](SPECIFICATION_GITHUB_STORE.md) for those paths.

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
| Win32 APIs (IE WebBrowser, LibVLC, GDI+, file access, local HTTP) | restricted in a pure UWP container | `runFullTrust` keeps them all working. OK. |

**Rule of thumb:** anything that writes to the install dir, or to `%LOCALAPPDATA%`/`HKCU` and must be
visible *outside* the package, would need an MSIX-aware path. FastMediaSorter has none of those cases.

---

## Phase 2 - Packaging artifacts (in this repo)

| File | Role |
| --- | --- |
| [msix/AppxManifest.xml](msix/AppxManifest.xml) | Manifest: Identity placeholders, `runFullTrust`, image file associations, visual assets. |
| [msix/build-msix.ps1](msix/build-msix.ps1) | MSBuild → version remap → stage offline payload → generate logos → fill manifest → `makeappx pack` → optional self-sign. |
| [msix/README.md](msix/README.md) | Build/submit instructions. |
| [assets/icons/store-icon-256.png](assets/icons/store-icon-256.png) | 256px logo master the build script scales into Store tiles. |
| [tools/store/make-screenshot.ps1](tools/store/make-screenshot.ps1) | Produces a ≥1366×768 Store screenshot. |
| [docs/privacy.html](docs/privacy.html) | Privacy-policy page (host on GitHub Pages → URL for the listing). |

**Version gotcha (important):** the Store requires a 4-part version with the **revision = 0**
(`Major.Minor.Build.0`), each part ≤ 65535. `build-msix.ps1` remaps the app's `YY.M.D.HHmm` stamp to
`YY.(M*100+D).HHmm.0` - monotonic over time, unique per minute. (e.g. `26.6.13.0016` → `26.613.16.0`.)

Tooling: the Windows SDK (provides `makeappx.exe` + `signtool.exe`) and MSBuild:
```powershell
winget install Microsoft.WindowsSDK.10.0.26100
```

---

## Phase 3 - Verify locally before uploading

```powershell
cd msix
.\build-msix.ps1 -SelfSign            # add -NoBuild to reuse the current bin\Release build
# prints two commands: Import-Certificate (run as admin) + Add-AppxPackage
```
Then trust the printed cert and `Add-AppxPackage` the `.msix`. Test: open a folder of media, navigate,
slideshow, play an MP4 (IE path) and an AVI/MKV (LibVLC path), run OCR translate (`T`), and set the app
as a default image handler from *Settings ▸ Apps ▸ Default apps*.

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
   cd msix
   .\build-msix.ps1 -IdentityName "<Name>" -Publisher "<CN=…>" -PublisherDisplayName "<…>"
   ```

---

## Phase 5 - Listing materials

| Item | Requirement / gotcha | This app |
| --- | --- | --- |
| **Privacy policy** | Required (app reads local files, and can make optional network calls) | [docs/privacy.html](docs/privacy.html) on GitHub Pages → URL |
| **Screenshots** | At least 1, PNG **≥ 1366×768** | `tools/store/make-screenshot.ps1`, or use the real app screenshots in `docs/images/` |
| **Store logos** | Optional (Store falls back to package logos) | package tiles are generated from `assets/icons/store-icon-256.png` |
| **Description** | Required | template below |
| **Product features** | Bullet list, each ≤ 200 chars | template below |
| **Pricing** | "Free" = pick it in the **Retail price** dropdown | - |
| **runFullTrust justification** | Required for every desktop MSIX; **~1000-char limit** | template below |
| **Age rating** | Short questionnaire | Done - IARC rating is live (General audience). See "Age rating (IARC)" below. |

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

### Description
```
FastMediaSorter LITE is a fast, keyboard-driven viewer and sorter for images and video on Windows.

Open a folder and fly through it: full-screen slideshow, quick panel navigation, and one-key Move /
Copy / Rename / Delete to sort large photo and video collections in minutes. Assign folders to hotkeys
and file each item with a single press. It plays a broad range of formats - H.264/MP4 via the built-in
player with an automatic LibVLC fallback for AVI, MKV, VP9, ZMBV and more - and fills letterbox/pillarbox
bars with a matching "ambilight" background.

It also includes optional on-image OCR translation: recognize text in a picture (fully offline,
Tesseract) and overlay a translation. Translation is performed by a provider you choose and configure
yourself - a local Ollama model or a LibreTranslate endpoint; OCR works without any network connection.

Runs on .NET Framework 4.8. No account, no ads, no telemetry. Open source:
https://github.com/SerZhyAle/FastMediaSorter_Lite
```

### Product features (one per line, ≤200 chars each)
```
Fast keyboard-driven sorting: one-key Move / Copy / Rename / Delete with hotkey-assigned folders
Full-screen slideshow and quick panel/thumbnail navigation for large image and video collections
Plays H.264/MP4 natively with an automatic LibVLC fallback for AVI, MKV, VP9, ZMBV and more
"Ambilight" perspective background fills letterbox/pillarbox bars to match the image
Optional on-image OCR translation: offline Tesseract OCR + a translator you configure (Ollama / LibreTranslate)
Set it as your default image viewer for JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF and SVG
Open source, no account, no ads, no telemetry
```

### runFullTrust justification (keep under ~1000 chars)
```
FastMediaSorter LITE is a full-trust Win32 desktop app (.NET Framework 4.8 / WinForms), not a UWP app,
so runFullTrust is required to run as a normal desktop process and to use the Win32 APIs its core
features depend on:
- File system access: it reads, copies, moves, renames and deletes the user's image/video files across
  arbitrary folders and network shares - that is the app's entire purpose, performed only on files and
  folders the user opens.
- Media playback: it hosts the system WebBrowser control (H.264/MP4) and the native LibVLC libraries
  (AVI/MKV/VP9/etc.) and uses GDI+ for image rendering.
- Optional local OCR/translation: it may call a user-configured translation endpoint (e.g. a local
  Ollama instance); OCR itself runs locally with bundled Tesseract data.
These APIs are available only to full-trust desktop apps. The app collects no user data and contains no
telemetry. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite
```

### Privacy policy (hosted as docs/privacy.html; key points)
```
FastMediaSorter LITE collects no personal data, has no accounts, no ads, and no telemetry/analytics.
It runs entirely on your device.

What it accesses and why:
- Your media files: read/copy/move/rename/delete only the files and folders you open, to sort them.
- Network (optional): only if you enable the on-image translation feature, the app sends the recognized
  text to the translation endpoint YOU configure (a local Ollama model or a LibreTranslate server) to get
  a translation. OCR runs fully offline. If you never use translation, the app makes no network requests
  except optionally downloading additional OCR language packs on demand.
Local files it writes: settings (registry) and OCR language data/cache under
%LOCALAPPDATA%\SZA\FastMediaSorter. These never leave your device.
Data sharing: none. Open source: https://github.com/SerZhyAle/FastMediaSorter_Lite. Contact: serzhyale@gmail.com
```

---

_See [msix/README.md](msix/README.md) for packaging detail and [docs/privacy.html](docs/privacy.html)
for the live privacy-policy page._
