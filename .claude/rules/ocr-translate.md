---
paths:
  - "src/Ocr/**"
  - "src/Translate/**"
  - "src/Ocr*.vb"
  - "src/Main_Form.Ocr*.vb"
  - "src/Table_Form.Ocr.vb"
  - "src/Security/**"
  - "tests/Lite.Tests/Ocr*.vb"
  - "tests/Lite.Tests/Translat*.vb"
---

# OCR and on-image translation

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

Design: `SPECIFICATION_OCR_TRANSLATION_OVERLAY.md` and `SPECIFICATION_OCR_OVERLAY_ACCURACY.md` (local specifications). The settings tab (`Table_Form.Ocr.vb`) is in [settings-window.md](settings-window.md).

**Components**
- **[OcrTranslateSettings.vb](../../src/OcrTranslateSettings.vb)** - API keys are encrypted via [src/Security/DpapiSecrets.vb](../../src/Security/DpapiSecrets.vb) (DPAPI). The source language resolves to an ordered single-language **attempt list** (`OcrAttemptCodes`), never a combined `eng+rus+ukr` string.
- **[src/Ocr/](../../src/Ocr)** - everything here is pure and unit-tested except the engine itself; `Main_Form` keeps only the `Graphics` glue.
  - `OcrLineSplitter` cuts a line the engine stitched across two columns or two balloons (`OCR-PIPELINE` 1.7 A) and regroups the pieces in reading order IN FRONT of the clustering.
  - `OcrTextJoin` (`OCR-PIPELINE` 1.8) is the one place a line's text is made of its words and a plate's of its lines. **Nothing that is decided reads that text**: the line filter and the attempt scorer read `OcrLine.SpacedText` (words one space apart), so a cosmetic join cannot move a verdict.
  - `OcrImageScale.ComputeScale` resamples the picture ONCE per recognition (the short side has a floor so a webtoon strip keeps readable lettering); preprocess buffers are built lazily.
  - `OcrTextFilter` decides what is worth translating.
  - `OcrPlateColors` and `OcrDiagnostics` (`FMS_OCR_DIAG`/`FMS_OCR_DIAG_FILE`, line built by the pure `BuildDumpLine`) are whole-file modern-only.
  - Every constant carries an `OCR-OVERLAY` rule 13 status row (*inherited* from `OCR-PIPELINE`, ticket 033 owning, or *chosen here, NOT derived*, with a reason); the numbers live in the code and its tests, not here.
  - **`OcrPipeline.OcrPipelineVersion` in `OcrModels.vb` rides in the cache key**: bump it in any change that alters the CONTENT of an `OcrOverlayDocument`, never for one that only paints it differently, or the disk keeps serving documents built by the previous algorithm.
- **[src/Translate/](../../src/Translate)** - `ITranslator` returns a `TranslationBatch`: the text AND a per-segment "really translated" flag, plus a batch-wide `TranslationFailure`. `OllamaManager` starts the Ollama installer only when it is Authenticode-signed by Ollama Inc. ([src/Security/AuthenticodeCheck.vb](../../src/Security/AuthenticodeCheck.vb)).

**Behaviour**
- **Hotkeys**: `T` runs OCR + translate (or toggles the overlay) when the feature is enabled, `Shift+T` toggles auto-OCR; with the feature off `T`/`Shift+R` keep their legacy rotate meaning. The **Перевод / Translate** toolbar button runs the same pipeline.
- **Pipeline** ([Main_Form.OcrTranslate.vb](../../src/Main_Form.OcrTranslate.vb), `RunOcrPipeline`): OCR -> `OcrLineSplitter` -> `OcrBlockBuilder` (drops isolated tiny blocks that are texture noise) -> `OcrLanguageGuard` -> translate all blocks in ONE batch -> overlay ([Main_Form.OcrOverlay.vb](../../src/Main_Form.OcrOverlay.vb)).
  - `OcrLanguageGuard` refuses the whole run when the source language was only ASSUMED (`auto`) and the discard record says the page could not be read: no overlay, a status naming the fix (`OCR-OVERLAY` rule 10).
  - Overlay text is sized to the **original** text: `OcrOverlayFit.FontCandidates` starts from the median height of the source block's **words** (`MedianLineHeight`; line boxes only as a fallback - a line box is stretched by whatever fell inside it); the ladder and the growth/release budget are `OCR-OVERLAY` rule 9. No ellipsis and no crop at any rung, the top edge never moves, and where a released plate overlaps the one below, plates are painted in reading order so the lower one wins.
  - The plate is **opaque by default** (`OverlayOpacity` 255, rule 7; a lower value is the user's own slider choice, and a stored 210 from an older build is kept) and has **no padding** - both recorded decisions of ticket 033. Paragraph direction follows the drawn text (an Arabic or Hebrew plate is right-to-left whatever the UI language).
- **Cache**: `TranslationCache` (memory + optional disk) under `OcrResultPolicy.BuildCacheKey` = file path + write-time + the **displayed pixel grid** (size, on-screen quarter turns since the picture was shown, `Is_Exif_AutoRotate`) + engine + provider + languages; the **engine** key folds in OCR model quality, page mode and `OcrPipelineVersion`, the **provider** key the Ollama model. `R` cancels a running job and moves the key; a document whose size is not the picture's is neither applied nor painted.
  - **Only a complete translation is cached** (`OcrResultPolicy.IsCacheable`): an unreachable or timed-out translator, a refused language or segments handed back untranslated produce a document shown with a status ending in "T will retry", marked `IsPartial`, and `T` then re-runs instead of toggling. **Empty (`No text found`) results are never cached**; opening the settings dialog also clears the cache.
  - An expired HTTP timeout is a failed batch, never an `OperationCanceledException` - only the job's own token ends a run silently.
- **OCR engine**: multi-pass Tesseract (`TesseractOcrEngine`, `Pix.LoadFromMemory`) - scored single-language attempts in `OcrAttemptCodes` order (e.g. `rus -> ukr -> eng` for `auto`) across page-segmentation modes and light preprocessing, highest score wins. `fast` (`tessdata`) or `best` (`tessdata_best`) packs download on demand; a missing pack is reported by name, with the way to download it, in both builds. The **OCR mode** setting can force a `PageSegMode`. At shutdown the engine is disposed only if no pass holds it (`RetireEngine`) - closing never waits for a recognition in flight.
- **Translators** (`CreateTranslator()` picks by `ocr_Settings.Provider`): `OllamaTranslator` (default - local LLM at `localhost:11434`, repairs a bad segment with one retry) and `LibreTranslateTranslator` (its own language codes - `zt`, `nb` - and an up-front refusal for a target it has no model for). `TranslationQuality` (pure) decides what is a bad segment: a refusal is judged by SHAPE - the model talks about the request in words the source never used - never by everyday words like "пожалуйста" or "sorry"; an echo is accepted when the source is already in the target language or is one Latin word (a name). Both parse JSON arrays through `TranslateHttp.JsonItemToString`, which pulls the real string out of a wrapped object (`{"translation":".."}`) - a raw `Convert.ToString` on such a `Dictionary` would leak ``System.Collections.Generic.Dictionary`2[..]`` into the overlay.
