# Technical Specification: OCR Accuracy Improvement

> Status: implemented (2026-06-06)
> Date: 2026-06-05
> Branch context: `main`
> Scope: improve OCR reliability in the Windows app when current behavior returns `No text found` or only partial text

## 1. Goal

Improve OCR quality in `FastMediaSorter_Lite` so that:

1. fewer images end in `No text found`;
2. partial recognition is reduced, especially for screenshots, manga/comics, scans, and noisy real-world photos;
3. OCR behavior moves closer to the stronger results already achieved in `P:\ANDROID\FastMediaSorter_mob_v2`;
4. navigation remains responsive and OCR failures never break the image-viewing flow.

This specification is about OCR accuracy and robustness only. It does not redesign the translation UX.

## 2. Current problem summary

The current Windows OCR path is functional, but too simple for difficult images.

Observed symptoms:

- `No text found` appears on images that visibly contain text.
- Only part of the text is recognized.
- Mixed-language results are unstable.
- Once a bad empty result is cached, the user can keep seeing the same bad outcome.

## 3. Verified current Windows behavior

Current implementation anchors:

- OCR engine: [src/Ocr/TesseractOcrEngine.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/Ocr/TesseractOcrEngine.vb>)
- OCR settings/language mapping: [src/OcrTranslateSettings.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/OcrTranslateSettings.vb>)
- OCR pipeline/apply/cache flow: [src/Main_Form.OcrTranslate.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/Main_Form.OcrTranslate.vb>)

### 3.1 Current OCR characteristics

The current Windows app:

- uses one OCR engine only: Tesseract;
- performs one OCR pass per image;
- always calls Tesseract with `PageSegMode.Auto`;
- flattens the image onto a white background and may downscale it;
- does not run alternative preprocessing passes;
- does not retry with alternative page-segmentation modes;
- treats source `auto` as combined Tesseract languages `eng+rus+ukr`;
- caches empty OCR results as valid disk/memory results.

Relevant code points:

- single pass with `PageSegMode.Auto`: [src/Ocr/TesseractOcrEngine.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/Ocr/TesseractOcrEngine.vb:103>)
- current `auto -> eng+rus+ukr` mapping: [src/OcrTranslateSettings.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/OcrTranslateSettings.vb:62>)
- empty block result cached as `No text found`: [src/Main_Form.OcrTranslate.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/Main_Form.OcrTranslate.vb:236>)

## 4. Why Android performs better

Reference anchors from `P:\ANDROID\FastMediaSorter_mob_v2`:

- engine initialization and single-language Tesseract behavior:
  [TesseractManager.kt](</p:/ANDROID/FastMediaSorter_mob_v2/app_v2/src/main/java/com/sza/fastmediasorter/ui/player/helpers/TesseractManager.kt:40>)
- OCR engine fallback provider:
  [OfflineOcrEngineProvider.kt](</p:/ANDROID/FastMediaSorter_mob_v2/app_v2/src/main/java/com/sza/fastmediasorter/domain/ocr/OfflineOcrEngineProvider.kt:19>)
- OCR decision path and fallback logic:
  [TranslationManager.kt](</p:/ANDROID/FastMediaSorter_mob_v2/app_v2/src/main/java/com/sza/fastmediasorter/ui/player/helpers/TranslationManager.kt:674>)
- optional high-quality Tesseract model management:
  [TesseractModelManager.kt](</p:/ANDROID/FastMediaSorter_mob_v2/app_v2/src/main/java/com/sza/fastmediasorter/ui/player/helpers/TesseractModelManager.kt:16>)

### 4.1 Important Android advantages

Compared with Windows, Android already has several quality advantages:

- it prefers single-language OCR initialization instead of always mixing `eng+rus+ukr`;
- it has OCR engine fallback behavior;
- it can use validated higher-quality `tessdata_best` models for important languages;
- it filters and post-processes results more aggressively;
- in some builds it can use PaddleOCR for difficult real-world images.

### 4.2 Important conclusion

The gap is not only "Tesseract quality". The gap is mainly:

- better language choice;
- better fallback behavior;
- better model strategy;
- stronger retry logic.

This means the Windows app can be improved substantially without needing an immediate engine replacement.

## 5. Root causes of current Windows misses

### 5.1 Mixed-language `auto` mode is too broad

Current `auto` maps to `eng+rus+ukr`.

Problems:

- Tesseract language mixing can reduce accuracy;
- similar glyphs across Latin/Cyrillic increase confusion;
- one image often contains one dominant script, not three equal candidates.

This is a likely cause of both partial OCR and false `No text found`.

### 5.2 Only one page segmentation mode is tried

Different image types need different page segmentation assumptions.

Examples:

- screenshots and sparse UI labels often work better with sparse-text modes;
- manga/comics and speech bubbles often need looser segmentation;
- scanned pages can behave differently from screenshots.

Current code always uses only `PageSegMode.Auto`.

### 5.3 Preprocessing is too weak

Current preprocessing only:

- flattens to white;
- rescales down when large;
- keeps the general image content intact.

Missing useful variants:

- grayscale pass;
- contrast/threshold pass;
- inverted-text pass for light text on dark backgrounds;
- sharpened or morphology-friendly variants;
- optional "do not downscale" behavior for small text.

### 5.4 Empty OCR results are cached

Current pipeline stores empty OCR documents in memory and optionally on disk.

That makes a transient bad OCR pass sticky, even though another pass or later code improvement might succeed.

### 5.5 No quality escalation path

The Windows app currently has no concept of:

- fast model vs. high-quality model;
- first-pass OCR vs. retry OCR;
- primary engine vs. fallback engine.

Android already has those concepts.

## 6. Objectives

The Windows OCR improvement work must:

1. increase recognition success rate without harming navigation fluidity;
2. keep current Tesseract-based architecture usable;
3. preserve current overlay/translation flow;
4. make bad OCR results less sticky;
5. create a clean path for future engine fallback if needed.

## 7. Proposed design

## 7.1 Replace language mixing with a retry ladder

Do not treat `auto` as one combined `eng+rus+ukr` OCR call.

Instead, define ordered OCR attempts.

### 7.1.1 Auto-language attempt strategy

For `SourceLang = auto`, use a retry ladder such as:

1. `rus`
2. `ukr`
3. `eng`
4. optionally `rus+ukr`
5. optionally `eng+rus`

The first successful attempt with meaningful output wins.

### 7.1.2 Explicit-language attempt strategy

When user explicitly chooses a source language:

- first attempt: chosen language only;
- optional fallback: closely related language for that script;
- optional last fallback: `eng` for UI/screenshot cases.

Examples:

- `ru` -> `rus`, then optional `ukr`
- `uk` -> `ukr`, then optional `rus`
- `en` -> `eng`

### 7.1.3 Success heuristic

A pass should count as meaningful only if it produces enough real content, not just one noisy fragment.

Suggested minimum signals:

- at least one line with 3+ visible letters, or
- at least 2 lines with acceptable confidence, or
- total recognized text length over a configured minimum threshold.

## 7.2 Add OCR retry profiles

Each OCR attempt should support more than one profile.

Suggested profile dimensions:

- page segmentation mode;
- preprocessing variant;
- scale policy;
- model quality.

### 7.2.1 Minimum retry set for v1

For each language attempt:

1. `PageSegMode.Auto` on normal-preprocessed bitmap
2. `PageSegMode.SparseText` on normal-preprocessed bitmap
3. `PageSegMode.Auto` on thresholded/high-contrast bitmap
4. `PageSegMode.SparseText` on thresholded/high-contrast bitmap
5. optional invert pass when image appears dark-background/light-text

Stop on first strong result.

### 7.2.2 Why this is acceptable

The OCR pipeline already runs off the UI thread, so multiple fast retries are acceptable if:

- cancellation remains immediate on navigation;
- only the latest image job is allowed to finish;
- retry count is capped.

## 7.3 Strengthen image preprocessing

Add a preprocessing builder that can create a small set of OCR-specific bitmap variants.

### 7.3.1 Required variants

- current flattened color bitmap
- grayscale bitmap
- high-contrast / threshold bitmap
- inverted bitmap when needed

### 7.3.2 Scaling rules

Current code downsizes long edge above `2600`.

Improved policy:

- preserve current downscale ceiling for huge images;
- avoid excessive downscale when text is already small;
- optionally upscale very small screenshots before OCR.

Suggested future settings:

- `Normal`
- `PreferSmallText`

`Normal` should be the default.

## 7.4 Stop caching empty OCR as final truth

Do not persist empty OCR documents to disk cache as if they were successful results.

Recommended behavior:

- non-empty OCR + translation: cache normally;
- OCR failure / no text: show status, but do not disk-cache as final success;
- optional short-lived negative memory cache is acceptable for the current session only.

This preserves responsiveness without making bad misses permanent.

## 7.5 Add optional high-quality model support

Introduce support for a quality tier similar to Android's `tessdata_best` handling.

### 7.5.1 Model policy

- keep `tessdata_fast` or current fast models as baseline;
- allow optional download/use of `tessdata_best` for key languages, first of all:
  - `rus`
  - `ukr`
  - optionally `eng`

### 7.5.2 Validation

Downloaded models should be validated before activation.

Minimum requirements:

- temporary download file;
- final rename only after successful completion;
- file existence/size check;
- optional SHA-256 pin for shipped recommendations.

## 7.6 Add OCR quality telemetry

The app needs better diagnostics for future tuning.

For each OCR attempt, log:

- source file path or short identifier;
- language attempt;
- preprocessing profile;
- page segmentation mode;
- line count;
- total text length;
- average confidence if available;
- chosen winning profile or final failure.

Optional debug-only feature:

- save failed OCR input variants to a temp diagnostics folder for manual comparison.

## 7.7 Prepare engine fallback seam

The current project already has `IOcrEngine`, which is good.

We should keep the immediate work inside `TesseractOcrEngine`, but structure it so a future fallback engine can be added cleanly.

Examples of future fallback paths:

- Windows OCR engine, if packaging/runtime conditions become favorable;
- ONNX/Paddle-based OCR;
- second Tesseract profile family for real-photo handling.

This is not required for the first patch, but the retry/result model should not block it.

## 8. Required code changes

### 8.1 [src/OcrTranslateSettings.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/OcrTranslateSettings.vb>)

Required changes:

- stop using `auto -> eng+rus+ukr` as the operational OCR strategy;
- keep simple mapping helpers, but add a new method for ordered OCR language attempts;
- explicitly separate:
  - chosen source language
  - OCR attempt sequence
  - fallback sequence

Suggested new helpers:

- `OcrLanguageAttempts() As List(Of String)`
- `RelatedFallbackLanguages(sourceCode As String) As List(Of String)`

### 8.2 [src/Ocr/TesseractOcrEngine.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/Ocr/TesseractOcrEngine.vb>)

Required changes:

- add retry orchestration inside OCR recognition;
- support multiple page segmentation modes;
- support multiple preprocessed bitmap variants;
- add result scoring / winner selection;
- add optional best-model lookup path before fast-model fallback;
- expand logging.

Suggested internal additions:

- `OcrAttemptProfile`
- `OcrAttemptResult`
- `BuildAttemptProfiles(...)`
- `ScoreResult(...)`
- `BuildBitmapVariants(...)`
- `RecognizeSingleAttempt(...)`

### 8.3 [src/Main_Form.OcrTranslate.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/Main_Form.OcrTranslate.vb>)

Required changes:

- do not save empty OCR results to disk cache;
- optionally keep a short-lived in-memory negative cache only if needed;
- improve status text to distinguish:
  - no text found after all attempts
  - OCR runtime missing
  - OCR preprocessing/attempt failure

### 8.4 [src/Translate/TranslationCache.vb](</p:/WINDOWS/FastMediaSorter_Lite/src/Translate/TranslationCache.vb>)

Review needed:

- ensure cache semantics clearly distinguish successful overlay documents from transient OCR misses;
- keep on-disk format compatible if practical, but correctness is more important than legacy empty-result reuse.

## 9. UX and behavior requirements

### 9.1 Performance

The new OCR logic must not degrade browsing usability.

Rules:

- OCR still runs off the UI thread;
- cancellation on navigation remains mandatory;
- latest job wins;
- retry count must be bounded.

### 9.2 User-visible status

The user should still see short status text, but wording should better reflect multi-attempt behavior.

Suggested statuses:

- `OCR...`
- `OCR retry...`
- `No text found`
- `Loaded from cache`
- `OCR runtime missing`
- `OCR error`

### 9.3 Settings surface

Not all tuning must be exposed in v1.

Recommended:

- keep advanced retry/preprocessing settings internal for the first implementation;
- optionally expose later:
  - `OCR quality: Fast / Better`
  - `Prefer small text`
  - `Install high-quality language pack`

## 10. Acceptance criteria

The work is complete when all of the following are true:

1. Images that currently fail under `auto` show better recognition on a representative sample set.
2. `auto` no longer depends on one combined `eng+rus+ukr` OCR call.
3. At least two OCR retry profiles are implemented and used automatically.
4. Empty OCR results are no longer stored as durable disk-cache successes.
5. Navigation remains responsive while retries run.
6. Cancelling/navigating away never applies OCR results to the wrong image.
7. Logging is sufficient to tell which language/profile won or why all attempts failed.

## 11. Implementation phases

### Phase 1: High-value correctness fixes

- replace `auto` mixed-language pass with ordered single-language attempts;
- add `PageSegMode.Auto` + `PageSegMode.SparseText` retry ladder;
- stop disk-caching empty OCR results;
- add richer logging.

Expected impact:

- biggest gain for lowest risk;
- should reduce both `No text found` and partial text cases immediately.

### Phase 2: Better preprocessing

- add grayscale/high-contrast/invert variants;
- add result scoring across attempts;
- tune scaling for small text.

Expected impact:

- stronger screenshot/comic/UI OCR;
- better dark-theme and scan handling.

### Phase 3: Quality tier support

- add optional `tessdata_best` support for key languages;
- add model validation and install flow.

Expected impact:

- better Cyrillic recognition quality;
- closer parity with Android on supported languages.

### Phase 4: Future fallback engine

- evaluate second-engine fallback only after Phases 1-3 are measured.

Expected impact:

- best path for hard real-world photos and pathological layouts;
- should be driven by post-phase diagnostics, not assumed upfront.

## 12. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Too many OCR retries slow down auto mode | Cap profiles, cancel aggressively, newest job wins |
| Better models increase download size | Keep high-quality models optional |
| Result scoring picks a noisy pass | Start with conservative thresholds and log every winner |
| Cache behavior changes surprise users | Prefer correctness over stale misses; keep positive cache intact |
| Extra preprocessing creates memory churn | Reuse intermediate bitmaps carefully and dispose aggressively |

## 13. Notes on official guidance

Tesseract's own documentation recommends tuning both image preprocessing and page segmentation mode for difficult OCR cases. That aligns with this specification and supports the retry-based approach rather than relying on one universal OCR pass.

Reference sources:

- Tesseract ImproveQuality guide:
  https://tesseract-ocr.github.io/tessdoc/ImproveQuality.html
- Tesseract data-file guidance:
  https://github.com/tesseract-ocr/tesseract/wiki/Data-Files/dcb79962ceae5fe3b767b98f11a2ead4435a8dca

