# Technical Specification: OCR + Translation Overlay for Images

> Status: draft for implementation
> Date: 2026-06-04
> Branch context: `ui-modernization`
> This document supersedes the implementation decisions in `PLAN/OCR_TRANSLATION_FEATURE_PLAN.md`.

## 1. Goal

Add an optional feature to `FastMediaSorter_Lite` that can:

1. detect text on the currently displayed image;
2. translate that text into a chosen target language;
3. render the translation directly over the image as a non-destructive overlay.

Primary use cases: manga, comics, screenshots, scanned pages, memes, UI screenshots, signs.

The feature must be optional, must not block normal image browsing, and must degrade gracefully when OCR/translation backends are unavailable.

## 2. Revised research results

The earlier research was useful, but several implementation assumptions need to be corrected.

| Topic | Earlier assumption | Revised conclusion | Impact on implementation |
|---|---|---|---|
| Primary OCR engine | `Windows.Media.Ocr` can be the default engine for the current app | For the current unpackaged WinForms/.NET Framework app this is the wrong default. Microsoft currently lists `Windows.Media.Ocr.*` among WinRT APIs that require package identity in desktop apps. | Do not use `Windows.Media.Ocr` as v1 primary OCR path. Keep it only as a future option if the app moves to MSIX/package identity. |
| OCR coordinate mapping under zoom/pan | Overlay must multiply by `zoom_Scale` and add a separate pan offset | In this app zoom/pan is implemented by resizing and moving the `PictureBox` itself in `MouseUse`, not by internally panning the image. The overlay should therefore render in `PictureBox` client coordinates using the current `ClientSize`. | Reuse `GetZoomedImageRectangle(...)` against the current visible `PictureBox`; do not invent a second pan model. |
| Reusing current background worker | Existing `BgWorker` can host OCR/translation too | Existing `BgWorker` already handles metadata and next-image preload. Reusing it will couple OCR latency to navigation smoothness. | Introduce a separate OCR/translation pipeline with its own queue/cancellation. |
| LibreTranslate role | Safe generic default backend | LibreTranslate is a valid API option, but the official docs describe the project as AGPL-3.0 software. | Use LibreTranslate only as an external/self-hosted API option. Do not plan to bundle its server into the app without separate license review. |
| Tesseract position | Fallback only | For the current packaging model, Tesseract is the most practical offline OCR baseline. | Make Tesseract the primary v1 OCR engine. |

### 2.1 Final technology choices for v1

- OCR primary: `Tesseract` for .NET Framework 4.8.
- OCR future option: `Windows.Media.Ocr` only after packaging strategy changes.
- Translation default: local `Ollama` when available.
- Translation secondary offline/API option: external or self-hosted `LibreTranslate`.
- Translation optional cloud providers: `DeepL`, `Google Cloud Translation`, `Azure AI Translator`.
- Overlay style for v1: semi-transparent text boxes, not inpainting.

## 3. Current codebase integration points

The feature must plug into the current app structure instead of fighting it.

### 3.1 Relevant code that already exists

- Image loading: [`src/FileManager.vb`](../../../src/FileManager.vb)
  - `LoadImageWithStream(filePath As String)` returns `Tuple(Of Image, MemoryStream)`.
- Main navigation and file switching: [`src/Main_Form.vb`](../../../src/Main_Form.vb)
  - `ReadShowMediaFile(...)`
  - `UpdateCurrentFileAndDisplay(...)`
- Global current-file state: [`src/Common_Module.vb`](../../../src/Common_Module.vb)
  - `Current_File_Name`
  - `Current_Image_Path`
  - `Is_No_Background_Tasks`
- Overlay geometry helper already available: [`src/Main_Form.PerspectiveBackground.vb`](../../../src/Main_Form.PerspectiveBackground.vb)
  - `GetZoomedImageRectangle(...)`
- Current zoom/fullscreen/media layout: [`src/Main_Form.UILayout.vb`](../../../src/Main_Form.UILayout.vb)
  - `SyncMediaSurface()`
  - `SkipZoom()`
  - fullscreen / super-fullscreen handling
- Mouse zoom/pan behavior: [`src/Main_Form.vb`](../../../src/Main_Form.vb)
  - `MouseUse(...)`
- Settings load/save: [`src/Main_Form.vb`](../../../src/Main_Form.vb)
  - load in `Main_Form_Load`
  - save in `Form1_FormClosing`
- Keyboard integration: [`src/Main_Form.vb`](../../../src/Main_Form.vb)
  - `KeybUse(...)`
- Modern toolbar host: [`src/Main_Form.ModernLayout.vb`](../../../src/Main_Form.ModernLayout.vb)

### 3.2 Constraints from the current app

- Target framework: `.NET Framework 4.8`.
- Build style: warnings-as-errors discipline is active in the project.
- The repository currently ships as a classic desktop app, not MSIX-packaged.
- Main form behavior is split across partial classes, so the enhancement should follow the same pattern.
- Existing `BgWorker` is already busy with metadata and next-image preloading; OCR must not block or replace that flow.

## 4. Scope

### 4.1 In scope for v1

- Manual OCR+translate of the current image.
- Optional auto mode after image settle/debounce.
- Overlay rendering on top of the visible image.
- OCR/translation caching.
- Settings for backend selection and target language.
- Clear status messages and graceful failure.

### 4.2 Out of scope for v1

- Editing the original file.
- Background inpainting / "native-looking" reconstruction.
- OCR for video frames.
- OCR for PDFs or EPUBs inside this app.
- Multi-user sync or cloud account management.
- Bundling a full LibreTranslate server.

## 5. Functional requirements

### 5.1 User scenarios

1. User opens an image with text and presses a toolbar button or hotkey.
2. App recognizes text and shows translated text over the image.
3. User toggles the overlay on and off without reloading the image.
4. User can enable auto mode so OCR/translation starts after each newly shown image settles.
5. If OCR finds no text, the app reports that state and keeps browsing responsive.
6. If the selected translator is unavailable, the app shows a non-blocking error and browsing continues.

### 5.2 Media eligibility

- Run only for supported image files.
- Never run for video files.
- Skip work when `Current_File_Name` is empty or the file no longer exists.

### 5.3 User controls

- Main toggle button in the modern toolbar: `Translate`.
- Hotkeys:
  - `T`: run OCR+translate for the current image, or toggle overlay if cached result already exists.
  - `Shift+T`: toggle auto mode.
- Settings entry point from toolbar context action or dedicated settings button.

## 6. Technical design

### 6.1 New files

```text
src/
  Ocr/
    IOcrEngine.vb
    OcrModels.vb
    TesseractOcrEngine.vb
    OcrBlockBuilder.vb
  Translate/
    ITranslator.vb
    OllamaTranslator.vb
    LibreTranslateTranslator.vb
    TranslationCache.vb
  Security/
    DpapiSecrets.vb
  Main_Form.OcrTranslate.vb
  Main_Form.OcrOverlay.vb
  OcrTranslateSettings.vb
```

### 6.2 Data model

Coordinates are stored in original image pixels.

```vb
Public Class OcrWord
    Public Property Text As String
    Public Property Box As Rectangle
    Public Property Confidence As Single
End Class

Public Class OcrLine
    Public Property Words As List(Of OcrWord)
    Public Property Text As String
    Public Property Box As Rectangle
End Class

Public Class OcrBlock
    Public Property Lines As List(Of OcrLine)
    Public Property SourceText As String
    Public Property TranslatedText As String
    Public Property Box As Rectangle
End Class

Public Class OcrOverlayDocument
    Public Property FilePath As String
    Public Property FileWriteTicks As Long
    Public Property ImageSize As Size
    Public Property SourceLanguage As String
    Public Property TargetLanguage As String
    Public Property Blocks As List(Of OcrBlock)
End Class
```

### 6.3 OCR engine strategy

#### v1 primary OCR: Tesseract

Use:

- NuGet `Tesseract` 5.2.0 for .NET Framework compatibility
- NuGet `Tesseract.Drawing` for `System.Drawing.Bitmap` interop

Reasons:

- compatible with `net48`;
- no package identity requirement;
- supports offline OCR;
- supports bounding boxes;
- integrates with the current `System.Drawing` image pipeline.

#### Language data strategy

Bundle a starter `tessdata` set for:

- `eng`
- `rus`
- `ukr`

Optional downloadable packs or separate bundled packs later for:

- `deu`, `fra`, `spa`, `ita`
- `jpn`, `kor`, `chi_sim`, `chi_tra`

#### Future OCR engines

- `Windows.Media.Ocr`: allowed only if release strategy changes to package identity / MSIX.
- `PaddleOCR`: optional later add-on if better CJK quality becomes necessary.

### 6.4 Translator strategy

#### Default translator: Ollama

Use the local API at `http://localhost:11434/api`.

Required behavior:

- probe availability before use;
- batch text blocks;
- set `keep_alive: 0` after generation flow;
- retry once when the model echoes the source instead of translating;
- never block navigation while waiting for the model.

#### Secondary API translator: LibreTranslate

Use only as an external or self-hosted HTTP API.

Supported request contract:

- `POST /translate`
- `q`
- `source`
- `target`
- `format=text`

Do not ship the LibreTranslate server inside the app in v1.

#### Optional cloud translators

Phase-2 or Phase-3 only:

- DeepL
- Google Cloud Translation
- Azure AI Translator

These providers must be pluggable and must not affect the offline path.

### 6.5 Pipeline

1. `UpdateCurrentFileAndDisplay(...)` finishes showing an image.
2. If auto mode is off, stop here until the user explicitly triggers translation.
3. If auto mode is on, start a debounce timer of about `300-400 ms`.
4. Capture a job snapshot:
   - current file path
   - current file write time
   - current image size
   - selected OCR engine
   - selected translator
   - source/target languages
5. Check in-memory cache.
6. Check disk cache.
7. If cache miss:
   - run OCR in background;
   - group words into lines and blocks;
   - translate blocks;
   - save overlay document to cache.
8. Marshal to UI thread.
9. Before applying result, verify the file shown to the user is still the same file.
10. If still current, assign overlay document and invalidate the visible `PictureBox`.

### 6.6 Threading model

Do not reuse the existing `BgWorker`.

Implement a separate serialized OCR pipeline:

- one active job at a time;
- one pending slot only;
- newest job wins;
- older pending jobs are discarded;
- active job is canceled when the user navigates away.

Possible implementation forms:

- `Task` + `CancellationTokenSource` + `SyncLock`, or
- a dedicated background worker only for OCR/translation.

Preferred approach: `Task`-based pipeline, because OCR and HTTP translation are naturally async.

### 6.7 Overlay rendering

Render in `Paint` handlers of `Picture_Box_1` and `Picture_Box_2`.

Rules:

- overlay is never baked into the original bitmap;
- overlay is visible only for the currently active image;
- overlay is cleared on navigation;
- overlay is recomputed or repainted on resize, fullscreen change, and zoom reset.

#### Coordinate mapping

Use the current visible `PictureBox.ClientSize` and reuse `GetZoomedImageRectangle(...)`.

Pseudo-flow:

```vb
Dim fit = GetZoomedImageRectangle(img.Width, img.Height, pb.ClientSize.Width, pb.ClientSize.Height)
Dim scale = fit.Width / CDbl(img.Width)
```

Then map every OCR block from image coordinates to picture-box client coordinates.

Important correction:

- No extra pan offset should be invented.
- In this app zoom/pan is expressed by changing the `PictureBox` bounds themselves.
- Therefore overlay math runs entirely inside the current `PictureBox` client rectangle.

#### Visual style for v1

- semi-transparent filled rectangle;
- 1 px border optional;
- translated text centered and auto-fitted;
- minimum font size floor with ellipsis fallback;
- strong contrast against fill color.

### 6.8 Cache

#### Memory cache

Key:

```text
filePath|fileWriteTicks|ocrEngine|translator|srcLang|dstLang
```

#### Disk cache

Path:

```text
%LOCALAPPDATA%\SZA\FastMediaSorter\ocr-cache\
```

Store:

- OCR block coordinates
- source text
- translated text
- engine/provider metadata
- source image size

Format: JSON.

### 6.9 Settings

Persist settings alongside existing app settings.

Required keys:

| Key | Default | Meaning |
|---|---|---|
| `OcrEnabled` | `0` | master switch |
| `OcrAutoMode` | `0` | auto after image settle |
| `OcrEngine` | `tesseract` | current engine |
| `OcrSourceLang` | `auto` | source language hint |
| `TranslateProvider` | `ollama` | current translator |
| `TranslateTargetLang` | UI language dependent | target language |
| `TranslateEndpoint` | provider dependent | endpoint URL |
| `TranslateApiKey` | empty | encrypted API key |
| `OverlayVisible` | `1` | last overlay state |
| `OverlayOpacity` | `210` | alpha |
| `OcrDiskCache` | `1` | disk cache enabled |

### 6.10 Secret storage

Do not store cloud API keys as plaintext.

Required behavior:

- encrypt with DPAPI (`ProtectedData`) under current user scope;
- store encrypted base64 payload in settings;
- never write keys to logs or status labels.

## 7. Packaging requirements

### 7.1 OCR runtime packaging

The chosen Tesseract stack must be verified against the current packaging model:

- local debug build;
- release build;
- release ZIP;
- installer build if/when used.

Before final implementation the team must validate:

- where native OCR binaries are copied in output;
- where `tessdata` lives relative to the executable;
- whether `AnyCPU` remains viable or whether a build target decision is required.

### 7.2 Project file changes

Expected additions:

- new `Compile Include=...` entries in [`src/FastMediaSorter.vbproj`](../../../src/FastMediaSorter.vbproj)
- new NuGet references in [`src/packages.config`](../../../src/packages.config)

## 8. UX requirements

### 8.1 Status text

The app must display compact, non-blocking status updates:

- `OCR...`
- `Translating...`
- `No text found`
- `Translator unavailable`
- `OCR runtime missing`
- `Result loaded from cache`

### 8.2 Failure behavior

Any OCR or translator failure must:

- not crash the app;
- not block navigation;
- not freeze image display;
- leave the current image view usable.

### 8.3 Accessibility and fallback

Add a secondary display mode for later phases:

- `panel` mode, where translated text is shown in a docked side panel instead of on-image.

This is not mandatory for v1, but the data model should not prevent it.

## 9. Implementation phases

### Phase 1: technical spike

- choose exact Tesseract package combination and validate runtime packaging;
- prove OCR on one sample image;
- prove word/line bounding box extraction;
- prove local overlay rendering on the active `PictureBox`.

Exit result:

- build passes;
- OCR boxes can be drawn on one sample image in the app.

### Phase 2: manual feature

- add settings model;
- add OCR pipeline;
- add manual toolbar button and `T` hotkey;
- add overlay painting;
- add in-memory cache.

Exit result:

- user can manually translate the current image and toggle the overlay.

### Phase 3: auto mode and persistence

- add debounce auto mode;
- add disk cache;
- add provider selection;
- add settings persistence;
- add DPAPI secret storage.

Exit result:

- revisiting the same file is fast;
- auto mode works without UI stutter.

### Phase 4: optional providers

- add LibreTranslate API support;
- add DeepL / Google / Azure support if needed.

Exit result:

- provider switching is configuration-only;
- cloud providers degrade gracefully when not configured.

## 10. Acceptance criteria

1. Browsing images remains responsive while OCR/translation runs.
2. `T` on an image with text produces a visible translated overlay or a clear non-blocking failure state.
3. Navigating away cancels stale OCR work and never applies an overlay to the wrong image.
4. Overlay remains aligned after:
   - fullscreen toggle;
   - super-fullscreen exit/return;
   - Ctrl+wheel zoom;
   - Shift+wheel 1:1 mode;
   - resize / `SkipZoom()`.
5. Cached results are reused when the same file with the same modification time is reopened.
6. Video playback behavior is unchanged.
7. Missing OCR runtime or translator endpoint does not crash the app.

## 11. Main risks and mitigations

| Risk | Mitigation |
|---|---|
| OCR native runtime packaging is fragile | Make Phase 1 a packaging spike before broad UI work |
| OCR is too slow on large images | downscale copy for OCR only, keep original coordinates mapped back |
| Overlay box grouping is messy on comics | start with line-based fallback, then merge into blocks heuristically |
| Ollama is unavailable or too slow | availability probe, timeout, fallback to cached/manual provider selection |
| API key leakage | DPAPI + no logging |
| UI jitter during fast navigation | separate serialized pipeline with cancel/latest-wins semantics |

## 12. Official sources checked

Research in this section was re-validated on 2026-06-04 against primary sources.

- Microsoft Learn: support for WinRT APIs in desktop apps
  - https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-api-desktop-app-support
- Microsoft Learn: `OcrEngine.AvailableRecognizerLanguages`
  - https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine.availablerecognizerlanguages
- NuGet: `Tesseract` 5.2.0
  - https://www.nuget.org/packages/Tesseract/
- Ollama API docs
  - https://docs.ollama.com/api/introduction
  - https://docs.ollama.com/faq
- LibreTranslate docs
  - https://docs.libretranslate.com/
  - https://docs.libretranslate.com/api/operations/translate/
- DeepL docs
  - https://developers.deepl.com/api-reference/translate
  - https://developers.deepl.com/docs/resources/usage-limits
- Google Cloud Translation pricing
  - https://cloud.google.com/translate/pricing
- Azure AI Translator docs
  - https://learn.microsoft.com/en-us/azure/ai-services/translator/create-translator-resource
  - https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/quickstart/rest-api
