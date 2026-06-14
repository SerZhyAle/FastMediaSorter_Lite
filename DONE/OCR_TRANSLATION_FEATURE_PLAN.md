# Feature Plan: Embedded OCR + Translation Overlay ("Google-Lens-style")

> **Status:** Research & design proposal (no code written yet)
> **Branch context:** `ui-modernization`
> **Date:** 2026-06-04
> **Author:** research/planning pass

---

## 1. The idea (restated)

While scrolling through images that contain text (manga, comics, screenshots, scanned
pages, signs, memes), the user wants to:

1. **OCR** the text in its original language (manually via a button, or automatically as
   each image is shown).
2. **Translate** it into a chosen destination language.
3. **Replace the text in place** - draw the translated text back over the original text
   regions on the image, *Google-Lens style*.

**Everything must be optional**: OCR off by default, manual vs. automatic mode, choice of
OCR engine, choice of translation backend, overlay on/off, source/target language.

### Decisions locked in for this plan (from kickoff Q&A)

| Decision | Choice | Consequence for the plan |
|---|---|---|
| Default translation backend | **Offline-first (Ollama / LibreTranslate)** | No API key needed to get value; mirrors the existing `EPUB_2_HTML` Ollama path. Cloud (DeepL/Google/Azure) stays pluggable & optional. |
| Overlay fidelity for v1 | **Translucent boxes (simple, robust)** | Phase 1 draws auto-fit translated text in semi-transparent boxes. Full inpaint/blend is deferred to Phase 4. |
| Languages that matter | **EN↔RU, UK, CJK, Western-European (DE/FR/ES/IT)** | Broad script coverage ⇒ **Windows.Media.Ocr** as primary engine (one API covers all via free OS language packs), **PaddleOCR optional** for best CJK quality. |

---

## 2. What the `EPUB_2_HTML` reference actually gives us

The reference at `P:\WINDOWS\EPUB_2_HTML` is a **Go** project. Important finding:
**it has no OCR** (it explicitly punts on scanned PDFs - see `internal/pdf/extract.go`:
*"OCR for scanned/image PDFs is a future TODO"*). So OCR is genuinely new ground here.

What it *does* give us is a **battle-tested translation layer design** we should port 1:1
in spirit:

- **Pluggable `Client` interface** with two implementations:
  - **Google Translate v2** (`internal/translator/translator.go`) - REST, API key in a file
    next to the exe, 5000-char batching, 3× retry with exponential backoff on 429/5xx,
    30 s timeout.
  - **Ollama local LLM** (`internal/translator/ollama.go`) - `http://localhost:11434/api/generate`,
    default model `gemma3:12b`, numbered-list batch prompts (20/batch), **echo-back
    detection + single-item retry**, concurrency via semaphore, `keep_alive: 0` to unload
    from VRAM on exit.
- **Transparent caching wrapper** (`internal/translator/cache.go`) - keyed by
  `"srcLang:dstLang:text"`, dedupes repeated strings within a run. **We will key by
  `engine:src:dst:textHash` and persist per-image.**
- **Progress reporting interface** for live UI feedback.
- **Graceful degradation** - missing API key ⇒ warn + skip, never crash.
- **Cost warning** before paid cloud calls (Google).

**Lesson applied:** the translation side is a solved problem we transcribe into VB.NET.
The novel work is **OCR + coordinate-accurate overlay rendering** inside a WinForms
`PictureBox` running in `Zoom` mode.

---

## 3. Current app: where this plugs in

All references verified against the working tree.

### 3.1 Project facts that constrain the design

- **.NET Framework 4.8**, `AnyCPU`, `WinExe`, `RootNamespace = fmsl`
  ([FastMediaSorter.vbproj:15](src/FastMediaSorter.vbproj#L15)).
- **`Option Strict On` / `Explicit On` / `Infer On`**, and a set of warnings are treated as
  **errors** ([FastMediaSorter.vbproj:55](src/FastMediaSorter.vbproj#L55)) - new code must be
  fully typed, no implicit conversions, no unused-return paths.
- **ILMerge bundles everything into a single exe**
  ([FastMediaSorter.vbproj:3](src/FastMediaSorter.vbproj#L3)). ⚠️ **ILMerge only merges
  *managed* assemblies.** Engines with **native** DLLs (Tesseract+Leptonica, PaddleOCR) need
  their `.dll`s copied next to the exe and excluded from merge. **Windows.Media.Ocr ships no
  DLLs at all** (it lives in the OS) → ILMerge-clean. This is a major point in its favour.
- **Single logical class across partials** - add a new `Partial Class Main_Form` file rather
  than bloating `Main_Form.vb`. Existing partials:
  `Main_Form.UILayout.vb`, `Main_Form.FileOperations.vb`, `Main_Form.VideoPlayer.vb`,
  `Main_Form.PerspectiveBackground.vb`, `Main_Form.ModernLayout.vb`.

### 3.2 Integration hook points (verified)

| Concern | Location | Use |
|---|---|---|
| Image display | `Picture_Box_1` / `Picture_Box_2`, **`SizeMode = Zoom`** ([Main_Form.Designer.vb](src/Main_Form.Designer.vb)) | Dual-buffered display. Overlay must follow whichever box is currently visible. |
| Image load | `LoadImageWithStream()` ([FileManager.vb:9](src/FileManager.vb#L9)) | Returns `Tuple(Of Image, MemoryStream)`. The `Image` is our OCR input. |
| Navigation / "show file" | `ReadShowMediaFile(read_Mode_Type As String)` ([Main_Form.vb](src/Main_Form.vb)) modes `ReadNextFile`/`ReadPrevFile`/`ReadFolderAndFile`/`InSlideShow` | Fired on every nav. **Auto-OCR debounces off this.** |
| Image actually assigned | `UpdateCurrentFileAndDisplay()` ([Main_Form.vb](src/Main_Form.vb)) | After the new `Image` is assigned to the visible PictureBox → enqueue OCR job here. |
| Current state | `Current_File_Name`, `Current_Image_Path` ([Common_Module.vb:13-14](src/Common_Module.vb#L13)) | Cache key + "is this still the current image?" guard. |
| Image extensions | `Image_File_Extensions` ([Main_Form.vb](src/Main_Form.vb)) | Only run OCR for these (skip video). |
| **Coordinate mapping** | `GetZoomedImageRectangle(srcW, srcH, tgtW, tgtH)` ([Main_Form.PerspectiveBackground.vb:133](src/Main_Form.PerspectiveBackground.vb#L133)) | **Exact math we need** to map image-pixel boxes → on-screen control rect. Currently `Private`; promote/duplicate for overlay use. |
| Edge-color sampling | `GetTrimmedAverageColor(...)` & edge sampling ([Main_Form.PerspectiveBackground.vb](src/Main_Form.PerspectiveBackground.vb)) | Reuse to pick a box fill / contrast color for overlay (and later, inpaint). |
| Background work | `BackgroundWorker` (`BgWorker_DoWork` / `RunWorkerCompleted`) + `InvokeRequired` pattern ([Main_Form.vb](src/Main_Form.vb)) | Model for the OCR/translate worker. `System.Threading.Tasks` is already imported. |
| Settings persistence | `GetSetting/SaveSetting(App_name, Second_App_Name, key, default)` → registry `HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter` ([Common_Module.vb:7-8](src/Common_Module.vb#L7)) | Add OCR/translation keys here; load in `Main_Form_Load`, save in `Form1_FormClosing`. |
| Global flags | `Common_Module.vb` (`Is_Russian_Language`, `Is_Pespective`, `Form_Color_Scheme`, …) | Add OCR/translation flags alongside. |
| Buttons + hotkeys | `KeybUse()` `Select Case e.KeyCode` ([Main_Form.vb](src/Main_Form.vb)); modern toolbar in `Main_Form.ModernLayout.vb` | Add an OCR toggle button + a hotkey (proposed **`T`** = translate, **`Shift+T`** = toggle auto). |

---

## 4. Technology research

### 4.1 OCR engine comparison

| Engine | License / cost | Offline | Bounding boxes | Languages (our set) | Native DLLs? (ILMerge) | Notes |
|---|---|---|---|---|---|---|
| **Windows.Media.Ocr** (WinRT) | Free, built into Win10/11 | ✅ | ✅ per-**word** `BoundingRect` + per-line | EN, RU, UK, DE, FR, ES, IT, **zh/ja/ko** - all via free OS **OCR language packs** | **None** (in OS) ✅ | **Chosen primary.** No key, no network, no DLL bloat, fast, decent quality. Each `OcrWord` has `Text` + `Rect` in image px. Works from **unpackaged** Win32/.NET-FW apps (PowerToys Text Extractor proves it). |
| **Tesseract** (`charlesw`, NuGet `Tesseract` 5.2.x) / fork `TesseractOCR` 5.5.x | Apache-2.0, free | ✅ | ✅ via `ResultIterator.TryGetBoundingBox` + `PageIteratorLevel` (block/line/word/symbol) | All our languages via `tessdata` files (~10–15 MB/lang) | **Yes** (native `tesseract*.dll`+`leptonica`, x86/x64) ⚠️ | **Chosen fallback / portability option.** Heavier setup; must ship `tessdata` + per-arch natives next to exe, exclude from ILMerge, ship both x86/x64 or pin platform. |
| **PaddleOCRSharp** (PP-OCRv4/v5) | Apache-2.0, free | ✅ | ✅ (detection polygons → boxes) | Best-in-class **CJK** + Latin; great on multi-column / stylized layouts | **Yes** (native paddle DLLs + models, large) ⚠️ | **Optional "CJK/quality" engine.** Largest footprint; offer as a downloadable add-on rather than bundling. |
| Cloud Vision (Google) / Azure Computer Vision / AWS Textract | Paid, per-image | ❌ | ✅ | All | None (REST) | **Optional** for users who want max accuracy and accept network + cost. Same pluggable interface. |

**Decision:** ship **Windows.Media.Ocr as the default/primary** (zero footprint, offline,
covers every requested script via OS packs). Add an **`IOcrEngine` abstraction** so
**Tesseract** (portable/offline fallback) and **PaddleOCR** (premium CJK) can be dropped in
later without touching the pipeline. Cloud OCR is a thin extra implementation.

> **Windows.Media.Ocr language packs:** enumerated at runtime via
> `OcrEngine.AvailableRecognizerLanguages`; a pack is added via
> *Settings → Time & Language → Language → Optional features*, or admin PowerShell:
> `Get-WindowsCapability -Online | Where-Object Name -like 'Language.OCR*'` then
> `Add-WindowsCapability`. The settings UI must **detect missing packs and guide the user**
> (and offer Tesseract as a no-install fallback).

### 4.2 Translation backend comparison

| Backend | Cost | Offline | Key needed | Quality | Notes |
|---|---|---|---|---|---|
| **Ollama (local LLM)** | Free | ✅ | No | Good–very good (model-dependent) | **Default (offline-first).** Direct port of the `EPUB_2_HTML` `ollama.go` design: numbered-list batch prompt, echo-back retry, `keep_alive:0` unload. Needs Ollama installed + a model pulled. |
| **LibreTranslate (self-host / public)** | Free (self-host) | ✅ (self-host) | Optional | Decent | Second offline option; simple REST `POST /translate`. Good for users who don't want an LLM. |
| **DeepL API Free** | **500k chars/mo free** | ❌ | Yes (free key) | Excellent (esp. EU langs) | Best cloud default when online; generous free tier. REST `POST /v2/translate`. |
| **Google Cloud Translation v2** | ~$20 / 1M chars | ❌ | Yes | Excellent, 130+ langs | Exactly the `EPUB_2_HTML` path; reuse batching + cost-warning UX. |
| **Azure Translator** | 2M chars/mo free tier | ❌ | Yes | Excellent | Alternative cloud; region + key. |

**Decision:** **Ollama is the default**, **LibreTranslate** the second offline option, and
**DeepL/Google/Azure** optional cloud providers behind the same `ITranslator` interface.
Carry over the reference project's **caching, batching, retry/backoff, and graceful
degradation** verbatim in design.

### 4.3 Overlay technique ("Google-Lens style")

Lens does: detect text → bounding boxes → translate → **erase original** (inpaint
background) → **render translated text fitted to the box**, matching color/size so it looks
native. Patent/》docs confirm the core is *"align a translated bounding box to the original
bounding box by centers and adapt color/typography."*

**Phased fidelity:**

- **Phase 1 (chosen v1): translucent boxes.** For each text region, draw a semi-transparent
  rounded rectangle (fill color sampled from the region so it reads as "covering" the
  original) and render the translated string **auto-shrunk to fit** the box, centered, with a
  contrasting color. Always legible, robust to OCR noise, no destructive image edits.
- **Phase 4 (later): inpaint blend.** Sample/clone the surrounding background to paint over
  the original glyphs, estimate original text color, and draw the translation at matched
  size/orientation so it blends in. Higher effort + edge-case risk (gradients, textured
  backgrounds, rotated text). The Phase-1 region/color machinery is reused.

The overlay is a **non-destructive layer** drawn on top of the displayed image - the
original file is never modified. A toggle (key/long-press) flips between *show translation*
and *show original*.

---

## 5. Proposed architecture

### 5.1 New files (all `Partial Class Main_Form` or standalone modules)

```
src/
  Ocr/
    IOcrEngine.vb            ' interface: Recognize(bitmap, srcLangHint) -> OcrDocument
    OcrModels.vb             ' OcrWord / OcrLine / OcrBlock / OcrDocument (boxes in image px)
    WindowsMediaOcrEngine.vb ' WinRT OcrEngine impl (primary)
    TesseractOcrEngine.vb    ' optional fallback (Phase 3)
  Translate/
    ITranslator.vb           ' interface: Translate(texts(), src, dst) -> String()
    OllamaTranslator.vb      ' default (offline) - port of ollama.go
    LibreTranslateTranslator.vb
    DeepLTranslator.vb       ' optional cloud (Phase 3)
    GoogleV2Translator.vb    ' optional cloud (Phase 3)
    CachingTranslator.vb     ' decorator: memory + disk sidecar cache
  Main_Form.OcrTranslate.vb  ' orchestration: job queue, debounce, cancellation, wiring
  Main_Form.OcrOverlay.vb    ' coordinate mapping + Paint-based overlay rendering
  OcrTranslateSettings.vb    ' registry load/save + settings model
```

> Place `Ocr/` and `Translate/` as folders with namespaced files. They have **no UI
> dependency** and are unit-testable in isolation (even though the project has no test suite
> yet, keeping them decoupled lets us add one).

### 5.2 Data model

```vb
' OcrModels.vb - coordinates are ORIGINAL IMAGE PIXELS
Public Class OcrWord
    Public Property Text As String
    Public Property Box As Rectangle        ' image-pixel space
    Public Property Confidence As Single     ' 0..1 (engine-dependent; may be NaN)
End Class

Public Class OcrLine
    Public Property Words As List(Of OcrWord)
    Public Property Text As String
    Public Property Box As Rectangle        ' union of word boxes
End Class

Public Class OcrBlock                        ' paragraph / bubble - translation unit
    Public Property Lines As List(Of OcrLine)
    Public Property Text As String
    Public Property Box As Rectangle
    Public Property TranslatedText As String ' filled by translation stage
End Class

Public Class OcrDocument
    Public Property SourceImageSize As Size
    Public Property DetectedLanguage As String
    Public Property Blocks As List(Of OcrBlock)
End Class
```

### 5.3 Pipeline (per image)

```
[image shown] 
   -> debounce ~350ms (skip if user keeps scrolling)
   -> cache lookup (key: filePath + mtime + engine + src + dst)   --hit--> render overlay
        |miss
   -> OCR (background): IOcrEngine.Recognize(bitmap)  -> OcrDocument (boxes in image px)
   -> group words -> lines -> blocks (bubble/paragraph heuristics: gap & alignment)
   -> language detect (engine result, or n-gram/script heuristic) 
   -> translate blocks (ITranslator, batched, cached)
   -> store result in cache (memory + optional disk sidecar)
   -> marshal to UI thread (BeginInvoke) -> render overlay over the CURRENT visible PictureBox
        (guard: discard if Current_File_Name changed while we were working)
```

### 5.4 Coordinate mapping (the crux)

`PictureBox.SizeMode = Zoom` letterboxes the image. To place an image-pixel box on screen,
reuse the exact logic from
[Main_Form.PerspectiveBackground.vb:133](src/Main_Form.PerspectiveBackground.vb#L133):

```vb
' scale + offset that Zoom applies:
Dim fit As Rectangle = GetZoomedImageRectangle(img.Width, img.Height, pb.ClientSize.Width, pb.ClientSize.Height)
Dim scale As Double = fit.Width / CDbl(img.Width)   ' == fit.Height / img.Height

' image-pixel box -> control-pixel box:
Function ImageToControl(b As Rectangle) As Rectangle
    Return New Rectangle(
        fit.X + CInt(b.X * scale),
        fit.Y + CInt(b.Y * scale),
        CInt(b.Width * scale),
        CInt(b.Height * scale))
End Function
```

⚠️ **Also account for manual zoom/pan** (`zoom_Scale`, Ctrl+Wheel) if a zoom is active -
multiply by `zoom_Scale` and add the pan offset before drawing. The overlay must invalidate
and recompute on `PictureBox.Resize`, window resize, fullscreen toggle, and zoom change.

### 5.5 Rendering (Phase-1 translucent boxes)

Draw in the visible PictureBox's `Paint` handler (or a transparent child control sized to the
box) - **not** baked into the image bitmap, so toggling is instant and lossless:

```vb
Private Sub Picture_Box_1_Paint(sender As Object, e As PaintEventArgs) Handles Picture_Box_1.Paint
    If Not Is_Overlay_Visible OrElse _currentOverlay Is Nothing Then Return
    e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
    e.Graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
    For Each blk In _currentOverlay.Blocks
        Dim r = ImageToControl(blk.Box)
        Using fill As New SolidBrush(Color.FromArgb(210, _boxColor))      ' translucent cover
            e.Graphics.FillRectangle(fill, r)
        End Using
        DrawAutoFitString(e.Graphics, blk.TranslatedText, r, _textColor)  ' shrink font to fit
    Next
End Sub
```

`DrawAutoFitString` measures with `TextRenderer.MeasureText` / `Graphics.MeasureString`,
stepping font size down until the wrapped text fits the box (min size floor → ellipsis).

### 5.6 WinRT OCR call (System.Drawing.Bitmap → SoftwareBitmap)

```vb
' WindowsMediaOcrEngine.vb (async; called from background)
Imports Windows.Media.Ocr
Imports Windows.Graphics.Imaging
Imports Windows.Storage.Streams

Public Async Function RecognizeAsync(bmp As Bitmap, langTag As String) As Task(Of OcrDocument)
    Using ras As New InMemoryRandomAccessStream()
        bmp.Save(ras.AsStream(), Imaging.ImageFormat.Png)   ' System.Drawing -> stream
        ras.Seek(0)
        Dim decoder = Await BitmapDecoder.CreateAsync(ras)
        Dim software = Await decoder.GetSoftwareBitmapAsync()
        Dim engine = If(String.IsNullOrEmpty(langTag),
                        OcrEngine.TryCreateFromUserProfileLanguages(),
                        OcrEngine.TryCreateFromLanguage(New Globalization.Language(langTag)))
        If engine Is Nothing Then Return Nothing   ' language pack missing -> caller falls back
        Dim result = Await engine.RecognizeAsync(software)
        Return MapToOcrDocument(result, bmp.Size)  ' result.Lines -> Words -> Word.BoundingRect
    End Using
End Function
```

> **csproj wiring for WinRT in .NET FW 4.8:** add `Microsoft.Windows.SDK.Contracts` NuGet
> (brings WinRT projections, reference-only so ILMerge-safe), plus references to
> `System.Runtime.WindowsRuntime` (enables `Await` on `IAsyncOperation`) and
> `System.Runtime.InteropServices.WindowsRuntime`. Alternatively set
> `<TargetPlatformVersion>10.0.19041.0</TargetPlatformVersion>` and reference `Windows.winmd`.
> `bmp.Save(ras.AsStream(), …)` needs `System.Runtime.WindowsRuntime` for `AsStream()`.

### 5.7 Threading, debounce, cancellation

- Single **serial worker** (dedicated `Task` loop or `BackgroundWorker`) consuming a
  1-deep "latest job wins" slot - when the user scrolls fast, stale jobs are dropped.
- **Debounce** ~300–400 ms after an image settles before starting OCR (matches existing
  preload feel; avoids hammering Ollama while flipping pages).
- A `CancellationTokenSource` per job; cancel when `Current_File_Name` changes.
- All UI mutation via `Me.BeginInvoke` (existing `InvokeRequired` pattern).
- Respect `Is_No_Background_Tasks` - when set, only manual (button/hotkey) OCR runs.

### 5.8 Caching

- **Memory:** `Dictionary(Of String, OcrDocument)` keyed by
  `filePath + "|" + fileMtimeTicks + "|" + engineId + "|" + srcLang + "|" + dstLang`.
- **Disk (optional, default on):** sidecar JSON in
  `%LOCALAPPDATA%\SZA\FastMediaSorter\ocrcache\<hash>.json` so re-visiting a folder is
  instant and offline. Also dedupe identical strings within a doc before translating
  (the `EPUB_2_HTML` `CachingClient` trick).

### 5.9 Settings (registry: `SZA\FastMediaSorter`)

| Key | Default | Meaning |
|---|---|---|
| `Ocr_Enabled` | `0` | Master on/off |
| `Ocr_AutoMode` | `0` | Auto-OCR each shown image vs. manual button only |
| `Ocr_Engine` | `windows` | `windows` \| `tesseract` \| `paddle` \| `cloud` |
| `Ocr_SourceLang` | `auto` | `auto` or BCP-47 tag |
| `Translate_Provider` | `ollama` | `ollama` \| `libre` \| `deepl` \| `google` \| `azure` \| `none` |
| `Translate_TargetLang` | follows UI (`ru`/`en`) | destination language |
| `Translate_OllamaModel` | `gemma3:12b` | local model |
| `Translate_Endpoint` | `http://localhost:11434` / Libre URL | backend URL |
| `Translate_ApiKey` | *(empty)* | cloud key (stored obfuscated, see Risks) |
| `Overlay_Mode` | `boxes` | `boxes` (v1) \| `inpaint` (v4) \| `panel` \| `off` |
| `Overlay_Opacity` | `210` | box alpha 0–255 |
| `Ocr_DiskCache` | `1` | persist sidecar cache |

Loaded in `Main_Form_Load`, saved in `Form1_FormClosing`, alongside existing settings.

### 5.10 UI

- **Toolbar toggle** in the modern toolbar (`Main_Form.ModernLayout.vb`): a "Translate"
  button - click = OCR+translate current image; the button's pressed state = overlay
  visible. Right-click / long-press = open OCR settings.
- **Hotkeys** (added to `KeybUse()` `Select Case`): **`T`** = translate current image /
  toggle overlay; **`Shift+T`** = toggle auto-mode. (Confirm `T` is unused - quick grep of
  `KeybUse` before claiming it.)
- **Settings dialog / panel**: engine picker, provider picker + endpoint/key/model, source
  (auto + list) and target language, overlay mode + opacity, "detect installed OCR
  languages" button with guidance to install missing packs, "clear cache".
- **Side-panel fallback** (`Overlay_Mode = panel`): show original + translation as text in a
  dockable panel (reuses zero coordinate math; good accessibility option).
- **Copy/export**: copy translated (or original) text to clipboard.
- **Status/progress**: reuse the existing progress-reporting affordance for "OCR…",
  "Translating 3/8…", and a clear "No OCR language pack for X - install or switch engine"
  message.

---

## 6. Pluggable interfaces (mirroring `EPUB_2_HTML`)

```vb
' IOcrEngine.vb
Public Interface IOcrEngine
    ReadOnly Property Id As String
    Function IsAvailable() As Boolean                 ' lang pack / native dll present?
    Function AvailableLanguages() As IEnumerable(Of String)
    Function RecognizeAsync(bmp As Bitmap, srcLangHintOrAuto As String) As Task(Of OcrDocument)
End Interface

' ITranslator.vb
Public Interface ITranslator
    ReadOnly Property Id As String
    Function IsAvailable() As Boolean                 ' endpoint reachable / key present?
    Function TranslateAsync(texts As IList(Of String), srcLang As String, dstLang As String,
                            ct As Threading.CancellationToken) As Task(Of IList(Of String))
End Interface
```

`CachingTranslator` decorates any `ITranslator`. A small factory builds the configured
engine/translator from settings, so the pipeline never hard-codes a backend - satisfying
"everything optional / pluggable."

---

## 7. Implementation phases

### Phase 0 - Scaffolding & decoupled engines (no UI)
- [ ] Add `Ocr/` + `Translate/` files, models, interfaces.
- [ ] `WindowsMediaOcrEngine` working end-to-end on a test bitmap (csproj WinRT wiring).
- [ ] `OllamaTranslator` + `CachingTranslator` ported from `ollama.go` (batch, echo-back
      retry, unload). `LibreTranslateTranslator`.
- [ ] Console/dev harness: load an image file → OCR → print boxes+text → translate → print.
- **Exit criteria:** OCR boxes + translated strings logged for a sample image; build passes
  with `WarningsAsErrors`.

### Phase 1 - Manual overlay in the app (the chosen v1)
- [ ] `Main_Form.OcrTranslate.vb` orchestration + serial worker + cancellation.
- [ ] `Main_Form.OcrOverlay.vb` coordinate mapping (reuse `GetZoomedImageRectangle`) +
      translucent-box rendering + auto-fit text + show/hide toggle.
- [ ] Toolbar button + `T` hotkey; settings persisted; honors `Is_No_Background_Tasks`.
- [ ] Recompute overlay on resize/fullscreen/zoom.
- **Exit criteria:** press `T` on a text image → translated translucent boxes appear over the
  right regions; toggle hides them; navigating away clears them.

### Phase 2 - Auto mode, caching, settings UI
- [ ] Auto-OCR hook in `UpdateCurrentFileAndDisplay` with debounce + "latest wins".
- [ ] Memory + disk sidecar cache.
- [ ] Settings dialog (engine/provider/langs/opacity/cache, install-pack guidance).
- [ ] Language auto-detect + per-block translation batching.
- **Exit criteria:** scrolling a folder auto-translates without UI stutter; revisits are
  instant; all toggles work and persist.

### Phase 3 - More backends (optional, pluggable)
- [ ] `DeepLTranslator`, `GoogleV2Translator` (+ cost warning), Azure - behind settings.
- [ ] `TesseractOcrEngine` fallback (ship `tessdata` + per-arch natives, exclude from
      ILMerge; document the packaging).
- **Exit criteria:** user can switch engine/provider in settings; cloud paths degrade
  gracefully without keys.

### Phase 4 - Lens-grade blend (stretch)
- [ ] Inpaint original text (background sampling/clone) + matched-color/size translated text.
- [ ] `PaddleOCRSharp` optional add-on for premium CJK.
- **Exit criteria:** translated text blends into the image; opt-in, off by default.

---

## 8. Packaging / build notes

- **Windows.Media.Ocr:** no shipped binaries; just csproj WinRT references. Verify the
  ILMerge step tolerates the `Microsoft.Windows.SDK.Contracts` reference (reference-only;
  should not be merged). Test the **single merged exe** actually runs OCR on a clean machine.
- **Tesseract/Paddle (Phase 3/4):** native DLLs + data files must be copied to output and
  **excluded from ILMerge**; decide x86 vs x64 vs both (project is `AnyCPU` - pin or ship
  both). Likely a **separate optional download** rather than bundling (keeps the LITE exe
  small).
- **Release pipeline** (`.github/workflows/release.yml`) stages everything in `bin/Release`
  except `.pdb`/`.xml`; any new runtime files (tessdata, models) must land there and in the
  Inno Setup installer (`installer/FastMediaSorter.iss`).
- **Update CLAUDE.md** with the new partials and the OCR/translate architecture once built.

---

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| **Missing OCR language pack** (Windows.Media.Ocr needs OS packs) | Detect via `AvailableRecognizerLanguages`; show install guidance; offer Tesseract (no install) as fallback. |
| **Coordinate drift** under Zoom + manual zoom/pan + fullscreen | Single `ImageToControl` mapping reused everywhere; invalidate on every resize/zoom/fullscreen event; visual test at multiple aspect ratios. |
| **UI jank during fast scroll** | Debounce + single "latest wins" worker + cancellation + cache; respect `Is_No_Background_Tasks`. |
| **Ollama not installed / model not pulled** | `IsAvailable()` probe; clear message + link; offer LibreTranslate or cloud; never block image viewing. |
| **CJK/stylized text accuracy** | PaddleOCR optional engine; per-block confidence; let user re-run with a different engine. |
| **Cloud cost / privacy** | Offline default; explicit cost warning before paid calls (as in `EPUB_2_HTML`); never send images for translation (only OCR'd text), and only when a cloud provider is chosen. |
| **API key storage** | Store in registry obfuscated (DPAPI `ProtectedData`), not plaintext; never log keys. |
| **ILMerge + WinRT projection conflicts** | Validate merged exe early in Phase 0 on a clean VM; if problematic, exclude the contracts assembly from merge or switch to `TargetPlatformVersion` approach. |
| **`WarningsAsErrors` build breakage** | New code fully typed (`Option Strict`), no unused returns; async via `Task` with explicit `Await`. |
| **Memory leaks (Bitmap/Stream)** | Mirror existing `LoadImageWithStream` discipline; `Using` blocks for every `Bitmap`/`SoftwareBitmap`/stream; dispose overlay bitmaps on image change. |

---

## 10. Open questions (non-blocking; sensible defaults chosen)

1. **Default target language** - follow the app's UI language (RU when `Is_Russian_Language`,
   else EN), or a fixed user choice? *(Plan assumes: follow UI, overridable in settings.)*
2. **Translation unit** - per text-block/bubble (better context, fewer calls) vs per-line
   (tighter boxes). *(Plan assumes: per-block, with line boxes kept for rendering.)*
3. **Bundle a Tesseract `tessdata` starter set**, or always rely on Windows packs + optional
   download? *(Plan assumes: Windows primary, Tesseract as optional download in Phase 3.)*
4. **Side panel vs on-image** as the *default* presentation. *(Plan assumes: on-image boxes
   default, panel available.)*

---

## 11. Rough effort estimate

| Phase | Scope | Est. |
|---|---|---|
| 0 | Engines/translators decoupled + dev harness | 2–3 days |
| 1 | Manual overlay in-app (v1 deliverable) | 2–3 days |
| 2 | Auto mode + cache + settings UI | 2–3 days |
| 3 | Extra backends (DeepL/Google/Azure, Tesseract) | 2–4 days |
| 4 | Lens-grade inpaint + PaddleOCR | 4–6 days (stretch) |

**Phases 0–2 deliver the full requested experience** (offline OCR + offline translation +
on-image translucent overlay, manual and auto). Phases 3–4 are optional polish/breadth.

---

## 12. Sources

- [Windows.Media.Ocr Namespace - Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr)
- [OcrEngine.AvailableRecognizerLanguages - Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine.availablerecognizerlanguages)
- [Call Windows Runtime APIs in desktop apps - Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [xulihang/WinRTOCR - WinRT OCR from a desktop console app](https://github.com/xulihang/WinRTOCR)
- [PowerToys Text Extractor (uses Windows.Media.Ocr unpackaged)](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor)
- [charlesw/tesseract - .NET wrapper](https://github.com/charlesw/tesseract) · [NuGet TesseractOCR 5.5.2](https://www.nuget.org/packages/TesseractOCR)
- [tesseract-ocr/tessdata_fast - language data](https://github.com/tesseract-ocr/tessdata_fast)
- [PaddleOCR vs Tesseract comparison (IronOCR)](https://ironsoftware.com/csharp/ocr/blog/compare-to-other-components/paddle-ocr-vs-tesseract/) · [CodeSOTA 2026 OCR benchmark](https://www.codesota.com/ocr/paddleocr-vs-tesseract)
- [DeepL vs Google Translate API pricing (Jun 2026)](https://www.buildmvpfast.com/api-costs/translation)
- Reference implementation: `P:\WINDOWS\EPUB_2_HTML` - `internal/translator/{translator.go, ollama.go, cache.go}` (Google v2 + Ollama + caching patterns)
