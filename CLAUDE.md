# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FastMediaSorter LITE** is a Windows Forms application for viewing and managing image and video files. It runs on .NET Framework 4.8 and supports a broad range of media formats through native IE WebBrowser playback (H.264/MP4) with LibVLC fallback for unsupported codecs (AVI, ZMBV, VP9, MKV, etc.).

## Build & Run

### Prerequisites
- .NET Framework 4.8
- Visual Studio 2022 (or MSBuild CLI)
- NuGet packages auto-restore via [src/FastMediaSorter.vbproj](src/FastMediaSorter.vbproj) (the project file lives in `src/`; the solution `FastMediaSorter.sln` is at the repo root)
- Project identity: `RootNamespace = fmsl`, `AssemblyName = FastMediaSorter_LITE`, startup object `fmsl.My.MyApplication`
- ILMerge (3.0.41) is referenced to bundle dependencies into the single output exe

### "Сборка" vs "Релиз" - two distinct flows (see [BUILD_AND_RELEASE.md](docs/guides/BUILD_AND_RELEASE.md))

- **Сборка / build** = LOCAL only: `.\build.ps1` (MSBuild Rebuild + deploy the single-file exe to the user's work folders). Test by hand, then commit/push. **Never creates or pushes a `v*` tag.** Pushing to a branch is free - GitHub Actions does NOT run on branch pushes.
- **Релиз / release** = GitHub build + winget + Microsoft Store: `.\tools\Release.ps1 -Push` (runs a free local check-build first, then creates and pushes the `vYY.M.D.HHmm` tag). The release `workflow` ([.github/workflows/release.yml](.github/workflows/release.yml)) is triggered **ONLY by pushing a `v*` tag** - that tag push is the single billable operation.
- **When the user asks for a "сборка"/"build"/"собери", run the LOCAL flow only and do NOT create or push a tag.** A tag (= paid GitHub run) requires an explicit "релиз"/"release" instruction. `tools/Release.ps1` defaults to a dry-run (no push) for safety; it pushes only with `-Push`.

### Build Commands

Build debug:
```powershell
msbuild FastMediaSorter.sln /p:Configuration=Debug /p:Platform="Any CPU"
```

Build release:
```powershell
msbuild FastMediaSorter.sln /p:Configuration=Release /p:Platform="Any CPU"
```

Run the app (after building):
```powershell
.\bin\Release\FastMediaSorter_LITE.exe
```

Or open in Visual Studio and press F5 to debug.

### Version Auto-Generation
The build system automatically generates version numbers in format `YY.M.D.HHmm` (e.g., `26.6.2.0124`) at build time via the `UpdateVersion` target in the .vbproj file. This updates `My Project\VersionInfo.vb` before compilation.

### Release Pipeline (CI)
Releases are cut by **pushing a `vYY.M.D.HHmm` tag** (or running the `Release` workflow manually with a tag input). [.github/workflows/release.yml](.github/workflows/release.yml) runs on `windows-latest` and:
1. Restores NuGet, then `msbuild ... /t:Rebuild` in Release.
2. Stages everything in `bin/Release` **except `.pdb`/`.xml`** (plus `README.md`, `LICENSE`) into `stage/FastMediaSorter-<version>-windows-x64/`.
3. Runs [tools/Prepare-OcrOfflinePayload.ps1](tools/Prepare-OcrOfflinePayload.ps1), which trims x86-only runtime trees from the x64 release and downloads all OCR `tessdata_fast` languages shown in the UI plus their `tessdata_best` counterparts into `tessdata/` and `tessdata-best/`.
4. Packages two **offline-ready** assets from that staged tree: a **portable ZIP** and an **Inno Setup installer EXE** (compiled via [installer/FastMediaSorter.iss](installer/FastMediaSorter.iss); Inno Setup installed on the runner with `choco install innosetup`). Each gets a `.sha256` sidecar.
5. Publishes a GitHub Release with all four files attached.

For a local release-style build, run:
```powershell
.\tools\Build-OfflineRelease.ps1
```
This mirrors the CI packaging flow and emits offline-ready artifacts in `dist/`.

The version in the tag is authoritative for asset names but is independent of the build-time `UpdateVersion` stamp - keep them consistent. See [SPECIFICATION_GITHUB_STORE.md](docs/specifications/SPECIFICATION_GITHUB_STORE.md) for the rationale behind shipping an EXE installer (GitHub-Store discoverability; a ZIP-only release is invisible to that marketplace).

### Publishing to winget - read before touching the manifest
**The winget manifest must point at the Inno `setup.exe` directly (`InstallerType: inno`) with NO declared dependencies.** This was learned the hard way (see [SPECIFICATION_WINGET_PUBLISHING.md](docs/specifications/SPECIFICATION_WINGET_PUBLISHING.md)). Quick rules:
- **Never** point winget at `single-exe.zip` - Defender ML flags the self-extracting bootstrap as `Program:Script/Wacapew.A!ml` (persistent false positive). It's a GitHub-only convenience download.
- **Don't** use the portable `windows-x64.zip` shape - the ~99 MB payload aborts the install with `0x80004004` after extraction.
- **Don't** declare the `Microsoft.VCRedist.2015+.x64` dependency - it sends winget into a resolution loop and aborts with `0x8A150044`; the app installs fine without it (validation never launches the app).
- **Don't** add `Scope: user` - it forces the harness into a non-elevated `--scope user` flow and the install fails with `0x8A150044` ("no suitable installer found for --scope user"). Omit `Scope` so validation runs elevated/machine.
- The "Missing `NestedInstallerType` / fewer `Tags`" inconsistency note is a non-blocking `Validation-Guide`, not a failure.
- Get the *real* failure reason from the build's `InstallationVerificationLogs` artifact, not the generic GitHub bot comments - the doc has a copy-paste snippet.

### Publishing to the Microsoft Store (MSIX) - see [STORE_PUBLISHING.md](docs/guides/STORE_PUBLISHING.md)
The Store path (Path A: MSIX) is **additive** - it does not change the GitHub release or winget flows. Full playbook + filled listing copy (description, features, runFullTrust justification, privacy text) is in [STORE_PUBLISHING.md](docs/guides/STORE_PUBLISHING.md); packaging detail in [msix/README.md](msix/README.md). Key facts:
- **No app code change is needed** - the app is already MSIX-safe: everything mutable goes to `%LOCALAPPDATA%\SZA\FastMediaSorter` (OCR downloads/cache via `OcrPaths`) or the registry, both writable inside the package container; bundled `tessdata` next to the exe is only ever read (the install dir is read-only under MSIX).
- Build with [msix/build-msix.ps1](msix/build-msix.ps1): MSBuild Release → stage the **offline payload** (same `bin/Release` tree minus `*.pdb`/`*.xml`, run through [tools/Prepare-OcrOfflinePayload.ps1](tools/Prepare-OcrOfflinePayload.ps1)) → generate logos from [assets/icons/store-icon-256.png](assets/icons/store-icon-256.png) → fill manifest → `makeappx pack`. `-SelfSign` makes a sideload-testable build; **no `-SelfSign` for the Store** (Microsoft re-signs on certification - no paid cert needed).
- [msix/AppxManifest.xml](msix/AppxManifest.xml) wraps the exe as a **full-trust** desktop app (`rescap:runFullTrust` keeps IE WebBrowser/LibVLC/GDI+/file-access/local-HTTP working) and declares image **file associations** (the manifest equivalent of the Inno per-user registry writes). Identity (`Name`/`Publisher`/`PublisherDisplayName`) is `__PLACEHOLDER__` filled at pack time from Partner Center (Product ▸ Product identity) via `-IdentityName`/`-Publisher`/`-PublisherDisplayName` - must match **exactly** or upload is rejected.
- **Version remap (Store requires revision = 0):** the script maps the exe's `YY.M.D.HHmm` stamp to `YY.(M*100+D).HHmm.0` (each part ≤ 65535). Don't hand-edit it.
- Needs the Windows SDK for `makeappx.exe`/`signtool.exe` (`winget install Microsoft.WindowsSDK`); they live under `C:\Program Files (x86)\Windows Kits\10\bin\*\x64\`.

## Architecture

### Core Components

**Main_Form** - Primary UI window, split across one `partial class` per concern (all `Partial Class Main_Form`; edit the relevant file, not just the main one):
- **[Main_Form.vb](src/Main_Form.vb)** (~3350 LOC) - core: file browsing, slideshow, keyboard shortcuts, drag-drop, recent files/folders (~100 max), English/Russian switching via `Is_Russian_Language`. **First-run UI language follows the Windows display language** (`SystemDefaultIsRussian()` in [Main_Form.Localization.vb](src/Main_Form.Localization.vb): Russian display language → Russian, everything else → English); once the user toggles it, the saved `Is_Russian_Language` registry value wins. Hosts the `COPYDATASTRUCT` declaration and `ProcessArgument()` entry point used for cross-instance arg forwarding (see Application_Events below).
- **[Main_Form.UILayout.vb](src/Main_Form.UILayout.vb)** - control sizing/positioning and responsive layout
- **[Main_Form.FileOperations.vb](src/Main_Form.FileOperations.vb)** - copy/move/rename/delete wiring from UI events to `FileManager`
- **[Main_Form.VideoPlayer.vb](src/Main_Form.VideoPlayer.vb)** - WebBrowser (H.264) + LibVLC fallback playback control
- **[Main_Form.PerspectiveBackground.vb](src/Main_Form.PerspectiveBackground.vb)** - Ambilight-like background fill (see below)
- **[Main_Form.OcrOverlay.vb](src/Main_Form.OcrOverlay.vb)** / **[Main_Form.OcrTranslate.vb](src/Main_Form.OcrTranslate.vb)** - OCR + on-image translation overlay pipeline (see "OCR + On-Image Translation" below). `OcrOverlay.vb` also hosts `PaintInfoOverlay()` - the optional top-left HUD (file name + `N/total`) drawn in the PictureBox `Paint` handler when `Is_Show_Info_Overlay` is on (never baked into the bitmap; useful in full-screen where the status bar is hidden).
- **[Main_Form.FileAssociation.vb](src/Main_Form.FileAssociation.vb)** - registers the app as default handler for image (`AssociateAllImageFormatsWithThisApp`) and video/audio (`AssociateAllVideoFormatsWithThisApp`) formats by writing per-user `HKCU\Software\Classes` (no admin rights), then `SHChangeNotify`
- **[Main_Form.DragDrop.vb](src/Main_Form.DragDrop.vb)** - lets a media file be dropped onto the **media surfaces** (both picture boxes, the `panel_Media` container, the help label, the video WebBrowser and the LibVLC view), not just the form. `WireSurfaceDragDrop()` (called from `Form1_Load`) sets `AllowDrop` + the shared `Form1_DragEnter`/`Form1_DragDrop` handlers on each surface; the OLE drop registration does NOT bubble to the parent, so a drop over a child without its own registration shows the "no-drop" cursor - hence every surface is wired explicitly. The WebBrowser ActiveX host never raises managed drag events, so instead `AllowWebBrowserDrop = True` lets IE accept the file and we cancel its `Navigating` to the dropped `file://` URL and route the path through `ProcessArgument()`. `WireVlcSurfaceDragDrop()` wires the LibVLC view when it is lazily created. **UIPI**: `ChangeWindowMessageFilter` whitelists `WM_DROPFILES`/`WM_COPYDATA`/`WM_COPYGLOBALDATA` so a drop from a normal-IL Explorer is accepted even when the app runs elevated (e.g. launched from an admin file manager); no-op otherwise. Every path ends in `ProcessArgument()` - open the file, switch to its folder, start playback.
- Other concern-specific partials: `Main_Form.MediaLoading.vb`, `.Lifecycle.vb`, `.FileScanning.vb`, `.KeyboardInput.vb`, `.MouseInput.vb`, `.Slideshow.vb`, `.GifPlayback.vb`, `.NativeMethods.vb`, `.ModernLayout.vb`, `.Localization.vb` - edit the file matching the concern.
- Uses WinForms controls: `HqPictureBox` (the two media surfaces, see below), WebBrowser (for videos), Label, Button, Timer

**Display resilience** (these never let a transient failure drop the image):
- `UpdateControlVisibility()`'s dynamic-background colour analysis and `Draw_Perspective()` both run their GDI+ pixel work inside a `Try/Catch`. GDI+ can transiently throw (`OverflowException`, `"Parameter is not valid"`) - e.g. while the background worker decodes on a slow network share - and the image is already on the PictureBox, so the analysis failure is swallowed and the frame keeps the previous tint/bars instead of aborting the load.
- `Draw_Perspective()` debounces with a trailing-edge timer: a redraw skipped by the `how_long_wait_before_draw_perspective` throttle is retried once scrolling stops, so the image you settle on always gets its bars.
- The background worker reads image dimensions via `Utils.GetImageDimensions()` (header parse, no GDI+) instead of `Image.FromFile`, so it never decodes the current image concurrently with the UI thread's `GetPixel`.
- **First media gets its bars too**: the first image at startup (a command-line file/the restored last folder) loads inside `Form1_Load` while `is_form_shown` is still `False`, so every `Draw_Perspective()` in that path is gated off. `Main_Form_Shown` redraws once (the form is now at its final size and `is_form_shown = True`); `Draw_Perspective` self-gates if no picture box is visible or perspective is off.
- **Seam fix**: `GetDisplayedVerticalEdgeColors`/`GetDisplayedHorizontalEdgeColors` sample one pixel **in** from the very edge (`edgeInset`). The outermost downscaled row/column folds in the darkest border/vignette pixels and comes out a few levels darker than what the `Zoom` PictureBox actually paints, leaving a faint dark seam between photo and bar; the second row/column matches the displayed edge, so the bar joins seamlessly.

**[Application_Events.vb](src/Application_Events.vb)** - `My.MyApplication` startup hooks for single-instance behavior
- On `Startup`: a path-independent named mutex (`FastMediaSorterSingleInstanceMutex`) detects an already-running instance (VB's built-in `IsSingleInstance` only matches same exe path, so debug/release/renamed copies would each start their own). If found, the new process forwards its command-line file path to the running window via `WM_COPYDATA` and cancels (`e.Cancel = True`).
- On `StartupNextInstance`: the running instance receives the new args, calls `Main_Form.ProcessArgument()`, and deliberately avoids stealing focus (restores the previously-foreground window).
- `WM_COPYDATA` payload is **UTF-8** on both send and receive (the receiver in `Main_Form.vb` decodes the bytes as UTF-8, not `PtrToStringAnsi`, so non-ASCII filenames survive).
- `ProcessArgument()` doesn't trust a single `File.Exists` for a `\\server` path: it probes with `File.GetAttributes` to tell a genuinely-missing file (fail fast) from a network hiccup (retry for the SMB session to recover) from an access-denied **lock** (the file is still being written/downloaded). A locked file is watched on a timer and **opens automatically once it unlocks**, instead of being silently dropped.

**[FileManager.vb](src/FileManager.vb)** - File I/O module
- `LoadImageWithStream()` - Load image + keep MemoryStream open (prevents file-lock issues)
- `RenameFile()`, `CopyFile()`, `MoveFile()`, `DeleteFile()` - Standard file operations
- GIF frame-count detection (catches corrupt GIFs early)
- **EXIF auto-orientation**: when `Is_Exif_AutoRotate` is on, `LoadImage` runs `ApplyExifOrientation()` on non-GIF images - it reads the `Orientation` tag (0x0112), `RotateFlip`s the pixels upright, then strips the tag. No-op when the tag is absent and failures are swallowed (a bad tag must never stop the load).

**[Common_Module.vb](src/Common_Module.vb)** - Global state & P/Invoke
- Public flags: `Is_Russian_Language`, `Is_Copying_not_Moving`, `Is_Pespective` (perspective background), `Is_No_Background_Tasks`
- **Viewer options** (the "Просмотр"/"Видео и качество" tabs of the Settings window; loaded/saved in `Main_Form.Lifecycle.vb`): `Is_Exif_AutoRotate`, `Is_HighQuality_Scaling` (bicubic downscaling, see `HqPictureBox`), `Is_Show_Info_Overlay` (on-image file name + position HUD), `Slideshow_Base_Interval_Ms` (base slideshow interval; repeated start halves it down to `slide_show_limit`)
- Color scheme selector: `Form_Color_Scheme` (0=dynamic, 1=black, 2=white, 3=most-frequent)
- WinAPI calls: `ShowWindow`, `SetForegroundWindow`, `GetForegroundWindow` (for single-instance enforcement)
- Hotkey storage: `Hardkeys_to_move_mediafile(10)` - Maps keyboard keys to folder shortcuts
- Current media state: `Current_File_Name`, `Current_Image_Path`

**[HqPictureBox.vb](src/HqPictureBox.vb)** - `PictureBox` subclass used for the two media surfaces (`Picture_Box_1`/`Picture_Box_2`). When `Is_HighQuality_Scaling` is on it sets `HighQualityBicubic` interpolation on the `Graphics` **inside `OnPaint` before `MyBase.OnPaint`** - the base draws the zoomed image there, so the control `Paint` event (which fires after) is too late. The existing OCR/info overlay `Paint` handlers still run afterwards.

**[Image_Panel_Form.vb](src/Image_Panel_Form.vb)** - Quick-access thumbnail panel
- Small window of file thumbnails
- Double-click to load, drag-drop to the main window

**[Table_Form.vb](src/Table_Form.vb)** - **Settings window** ("Настройки" / "Settings"), five tabs:
- **Каталоги-получатели / Destination folders** - the `Data_Grid_View` mapping each move/copy hotkey (DEL + 0..9) to a destination folder; grid is sized to exactly its 11 rows (no stretched empty area below).
- **Просмотр / Viewing** - background scheme (`grp_Background`), on-screen info overlay (`chk_Show_Info_Overlay`), slideshow interval (`num_Slideshow_Interval`), EXIF auto-rotate (`chk_Exif_AutoRotate`).
- **Видео и качество / Video and quality** - HQ scaling (`chk_Hq_Scaling`), video loop, default mute (`chk_Video_Mute`) and default volume (`num_Video_Volume`) - the audio defaults live as private fields in `Main_Form` and are read via `CurrentVideoMuted`/`CurrentVideoVolumePercent` and written via `SetVideoAudioState`.
- **Файлы и система / Files and system** - copy-vs-move, background file ops, no-confirm, register as default **image** viewer (`btn_Set_As_Default`) **and** default **video** player (`btn_Set_As_Default_Video` → `Main_Form.AssociateAllVideoFormatsWithThisApp`), "keep on top", and language toggle.
- **OCR и перевод / OCR & translation** (`Tab_Page_5`, built entirely in code in [Table_Form.Ocr.vb](src/Table_Form.Ocr.vb)) - the former standalone `OCR_Translate_Form` dialog folded into a tab. Global toggles (enable, auto), an inner two-page `TabControl` splitting **Перевод**/Translation (provider, endpoint, install/start Ollama, Ollama model + pull, API key, target language) from **Распознавание (OCR)**/Recognition (source language, `fast`/`best` model, page mode, download pack), then a shared footer (overlay opacity, disk cache, status). Unlike the rest of the form it builds once (`_ocrBuilt`) and edits the **live** `Main_Form` `OcrTranslateSettings` object directly via `Main_Form.EnsureOcrSettings()`/`OnOcrSettingsEditedFromSettingsWindow()`; `_ocrLoading` guards suppress handler side-effects during populate. Entering the tab clears the result cache (`Main_Form.ClearOcrResultCache()`); the toolbar Translate button's right-click opens the window on this tab via `Main_Form.ShowOcrTranslateSettings()` → `Table_Form.SelectOcrTab()`.
- Option toggles write straight to the `Common_Module` flags; the two that change painting (`chk_Hq_Scaling`, `chk_Show_Info_Overlay`) call `Main_Form.RepaintMedia()` so the change shows immediately.

**[Utils.vb](src/Utils.vb)** - Helper functions: array insert/remove, opposite-colour & luminance, clipboard, and **`GetImageDimensions()`** - reads JPEG/PNG/GIF/BMP pixel size straight from the file header (no GDI+), used by the background worker to avoid concurrent GDI+ decoding.

**OCR / Translation components** (see "OCR + On-Image Translation" below):
- **[Table_Form.Ocr.vb](src/Table_Form.Ocr.vb)** - the OCR/translate settings UI, now a tab of the Settings window (`Tab_Page_5`); see the Table_Form entry above. (Replaces the former standalone `OCR_Translate_Form`.)
- **[OcrTranslateSettings.vb](src/OcrTranslateSettings.vb)** - persisted settings (provider, endpoint, model, languages, opacity, disk-cache, OCR model quality `fast`/`best`, OCR page mode); API keys are encrypted via [src/Security/DpapiSecrets.vb](src/Security/DpapiSecrets.vb) (Windows DPAPI). Source language is resolved to an ordered single-language **attempt list** (`OcrAttemptCodes`), not a combined `eng+rus+ukr` string.
- **[OcrLanguageCatalog.vb](src/OcrLanguageCatalog.vb)** - language list + flag glyphs ([assets/flags/](assets/flags))
- **[src/Ocr/](src/Ocr)** - `IOcrEngine`, `TesseractOcrEngine` (multi-pass Tesseract via `Pix.LoadFromMemory`: scores several language × page-segmentation × preprocess attempts and keeps the strongest; `fast` or `best`/`tessdata_best` models downloaded at runtime), `OcrBlockBuilder`, `OcrModels`
- **[src/Translate/](src/Translate)** - `ITranslator`, `OllamaTranslator` (default, local LLM), `LibreTranslateTranslator`, `OllamaManager`, `TranslationCache` (memory + disk), and the shared `TranslateHttp` module

### Key Dependencies
- **LibVLCSharp** (3.9.3) - Video codec fallback (LibVLC binaries included in release)
- **WebView2** (1.0.3240) - HTML renderer (legacy; mostly unused)
- **Tesseract** (5.2.0) - OCR engine; `tessdata` language packs are downloaded at runtime
- **Tesseract** (5.2.0) - OCR engine; GitHub release assets bundle the supported `tessdata` packs for offline use, while dev builds still download missing packs on demand
- **System.Drawing** - Image rendering
- **System.Windows.Forms** - UI framework
- **VideoLAN.LibVLC.Windows** (3.0.21) - Native LibVLC binaries bundled at build time

### Perspective Background Effect
See [SPECIFICATION_BACKGROUND_EFFECT.md](docs/specifications/done/SPECIFICATION_BACKGROUND_EFFECT.md) for the full algorithm. Summary:
- Fills "black bars" (pillarbox/letterbox) when image aspect ratio ≠ viewport aspect ratio
- Two modes:
  - **Uniform**: Edge is solid color → fill with average color
  - **Perspective**: Edge is complex → extend each edge row/column into the bar
- Triggered on image load/resize; runs async to avoid UI lag
- Constants in Main_Form.vb (e.g., `percent_of_color_deviation = 4`, `step_size_while_color_Search = 100`)

### OCR + On-Image Translation
See [SPECIFICATION_OCR_TRANSLATION_OVERLAY.md](docs/specifications/done/SPECIFICATION_OCR_TRANSLATION_OVERLAY.md) for the full design. Summary:
- **Hotkeys**: `T` runs OCR + translate (or toggles the overlay) when the feature is enabled; `Shift+T` toggles auto-OCR mode. When the feature is off, `T`/`Shift+R` keep their legacy rotate meaning. The **Перевод / Translate** toolbar button triggers the same pipeline.
- **Pipeline** ([Main_Form.OcrTranslate.vb](src/Main_Form.OcrTranslate.vb), `RunOcrPipeline`): OCR the image → `OcrBlockBuilder` groups lines into blocks (and drops isolated tiny blocks that are texture noise) → translate all blocks in one batch → render the translated text as an overlay over each block ([Main_Form.OcrOverlay.vb](src/Main_Form.OcrOverlay.vb)). Overlay text is sized to the **original** text, not a tiny capped font: `FitFont` starts from the source block's median line height (`MedianBlockLineHeight × scale`) and only shrinks when a longer translation would overflow (bounds `MinOverlayFont = 8`, `MaxOverlayFont = 200` px), so headings stay big. Results are cached in memory and (optionally) on disk by `TranslationCache`, keyed on file path + write-time + engine + provider + languages - where the **engine** key folds in OCR model quality and page mode and the **provider** key folds in the Ollama model, so changing any of them invalidates cached results. **Empty (`No text found`) results are no longer cached**, so a bad first pass doesn't poison later runs; opening the settings dialog also clears the cache.
- **OCR engine**: multi-pass Tesseract (`TesseractOcrEngine`), loading images via `Pix.LoadFromMemory`. Instead of one `PageSegMode.Auto` pass it runs several scored attempts - single-language passes (in `OcrAttemptCodes` order, e.g. `rus → ukr → eng` for `auto`) across page-segmentation modes and light preprocessing (grayscale / inverted), upscaling small images - and keeps the highest-scoring result. `fast` (`tessdata`) or `best` (`tessdata_best`) packs download on demand; the **OCR mode** setting can force a specific `PageSegMode` (auto/block/sparse/line/vertical).
- **Translators** (`CreateTranslator()` picks by `ocr_Settings.Provider`): `OllamaTranslator` (default - local LLM at `localhost:11434`, batches blocks, retries once when the model echoes the source) and `LibreTranslateTranslator`. Both parse JSON arrays through `TranslateHttp.JsonItemToString`, which pulls the real string out of a wrapped object (`{"translation":"…"}`) - a raw `Convert.ToString` on such a `Dictionary` would otherwise leak `System.Collections.Generic.Dictionary\`2[…]` into the overlay.

## Code Style & Constraints

**VB.NET Strict Mode** - All modules enforce:
```vb
Option Explicit On    ' All variables must be declared
Option Strict On      ' No implicit type conversions
Option Compare Binary ' Case-sensitive string comparison
Option Infer On       ' Allow type inference where possible
```

**File Organization**:
- `.vb` files are code-behind; `.Designer.vb` files are auto-generated (don't edit manually)
- Form classes inherit from `System.Windows.Forms.Form`
- Modules are for shared utilities and P/Invoke declarations
- `Main_Form` is one logical class spread across multiple `Partial Class Main_Form` files (`Main_Form.*.vb`). Fields/methods are shared across all of them - put a change in the file matching its concern, and watch for name collisions across partials.
- Build treats a fixed set of VB warnings as errors (`WarningsAsErrors` in the .vbproj: 41999, 42016–42022, 42032, 42036 - mostly implicit-conversion and unused/return-path warnings), so these will fail the build, not just warn.

**Keyboard Shortcuts** - Most features driven by hotkeys, not menus:
- Stored in app settings (persisted to registry/config)
- Hardcoded hotkey array in Common_Module for folder shortcuts
- Inspect Main_Form keyboard event handlers for the full mapping

**UI Threading**:
- Image/video loading happens on background threads to avoid UI freeze
- Use `Invoke()` or `BeginInvoke()` when updating UI controls from worker threads
- LibVLC video playback is asynchronous; use `MediaPlayer.Play()` callbacks

**File Locking**:
- `LoadImageWithStream()` keeps the MemoryStream open to allow Windows to release the file handle
- Disposed explicitly when image is no longer needed
- Critical for fast file operations in a media sorter

## Testing & Validation

No automated test suite. Validation is manual:
1. **Build**: `msbuild FastMediaSorter.sln /p:Configuration=Release` - confirms syntax
2. **Run**: Open the built `.exe` and exercise features: load folders, navigate files, test slideshow, verify video playback
3. **Code analysis**: Visual Studio's built-in analyzer (enabled in Debug config: `<RunCodeAnalysis>true</RunCodeAnalysis>`)

## Git & PR Workflow

- Single solution/project; no monorepo complexity
- VB.NET agent ([.github/agents/vb-net-profy.agent.md](.github/agents/vb-net-profy.agent.md)) available for focused VB.NET tasks
- Prefer small, targeted commits over broad rewrites
- Preserve existing UI behavior unless explicitly requested

## Common Development Tasks

### Adding a New File Operation
1. Add function to [FileManager.vb](src/FileManager.vb)
2. Call from Main_Form event handler (e.g., button click, keyboard shortcut)
3. Update UI state (enable/disable buttons, refresh file list)
4. Test with actual files in a temp folder

### Tuning the Perspective Background
1. Adjust constants in Main_Form.vb top (e.g., `percent_of_color_deviation`, `step_size_while_color_Search`)
2. Trigger a UI resize or image reload to see effect
3. Monitor `Debug.WriteLine()` output for timing data

### Supporting a New Media Format
1. Add extension to `Image_File_Extensions` or `video_File_Extensions` in Main_Form.vb
2. Test playback; if native player fails, LibVLC fallback handles it automatically
3. Verify no memory leaks (dispose Image/Bitmap, MemoryStream)

## Environment Notes

- Windows 7/10/11 support; developed primarily on Windows 11
- Executable built to `bin/Release/FastMediaSorter_LITE.exe`
- Settings/state persisted to Windows registry
- Single-instance enforced via app mutex (`app_Mutex_Name = "FastMediaSorterSingleInstanceMutex"`)
