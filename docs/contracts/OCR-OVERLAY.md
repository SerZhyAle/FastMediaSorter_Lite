# Pointer - `OCR-OVERLAY`

| | |
| --- | --- |
| **Id** | `OCR-OVERLAY` |
| **Version** | 1.3, active (read 2026-10-02; 1.1-1.3 are additive to what this product holds) |
| **Home** | `ocr-overlay/README.md` in the shared contracts catalog |
| **Role here** | consumer - the in-app OCR + translation overlay (`src/Ocr/`, `src/Main_Form.Ocr*.vb`) |

## What this repository must do to stay conformant

- Recognize the pixels the reader sees, hold one image-to-viewport transform, and never let a plate drift;
  clear the overlay rather than draw it in the wrong place when the image is rotated - and never serve it
  back from the cache on another grid: the cache key carries the displayed size, the on-screen quarter
  turns and the EXIF setting, and a document whose size is not the picture's is neither applied nor painted
  (audit 051 E-01, 2026-09-25; before that, rotation cleared the overlay and the unchanged key brought it
  straight back).
- Keep the discard record written through the **same predicate** the pipeline drops by, including for an
  image that produced nothing at all. That record is the instrument everything else is measured with, and
  this product is currently the only one that writes it for the blank case. Under 1.3 it is also the
  **normative evidence route** for rule 10 (see below).
- Never write a recognized string into user data, a filename or a catalog row.
- Keep OCR failure best-effort, and keep "read fine, found no text" a different outcome from "recognition
  failed".
- Bump `OcrPipeline.OcrPipelineVersion` in any change that alters the **content** of an
  `OcrOverlayDocument`, never for one that only paints it differently - the disk cache keys on it.

What 1.1-1.3 added, and where this product stands:

- **1.1 (concealment modes).** Rule 7 gained the optional gradient "reconstruct" and "mask" modes. The
  amendment says an opaque rectangle still holds the rule and a product that draws only the rectangle has
  nothing to adopt - this product draws only the rectangle; nothing is owed.
- **1.2 (rule 13's third status).** The rule now admits a constant marked `policy`: chosen, with the reason
  in one line, never claimed as a measurement. The rows in `src/Ocr/` that say *chosen here, NOT derived*
  are exactly that status in this product's own words; the registry exception closed 2026-10-02 on that
  basis. Ticket 033 Ф-3's corrections stand: the owning ticket is 033 (025 is closed), `OCR-ACCURACY` is
  not cited for the reference's numbers, and the constants marked *inherited* come from `OCR-PIPELINE`.
- **1.3 (rule 10 evidence).** An assumed language counts as resolved when the product records evidence that
  the page was read in a script its language can read - including, by name in the amendment, "the rule 12
  discard record under stated thresholds (FastMediaSorter_Lite)". `OcrLanguageGuard` refuses when the
  language was assumed and the translatability filter refused almost everything it read, the status names
  the fix, and the browser button no longer sends the assumed ladder as a chosen language. What was an
  interpretation is now the rule's own second evidence route; the registry row cites the amendment.
  **Gap found and closed 2026-10-04:** that route is blind to debris the filter lets through - an English
  page read by the Cyrillic model came back as 20 "natural" lines, the record held nothing, and the plates
  were drawn (and the translator then invented text around them). The engine now compares the ladder's
  languages by the engine's own mean line confidence instead of taking the first "strong" reading, and
  `OcrLanguageGuard` gained the confidence of the winning reading as a second thing it reads
  (`MinAssumedConfidence` 0.64, `ClearlyReadConfidence` 0.80, both rule 13 `policy` rows in that file with
  the measurements they sit between). Refusing more than the amendment asks is conformant; what is owed
  is the registry row, which still describes the discard record alone, and an amendment proposal if the
  catalog should name confidence as an evidence kind (`OcrPipelineVersion` is 10 for the same change).

Dated exceptions still carried, each a written decision rather than an omission:

- **rule 8 on the x86 fallback only.** The sampler is modern-only and stays that way: sampled colours are
  an appearance change and that build takes bug fixes only. The decision is written at the top of
  `src/Ocr/OcrPlateColors.vb`, including what would change it.
- **rule 7, the plate's opacity and padding.** The plate is opaque by default; a lower opacity is the
  user's own setting (30-100 %), and an install that saved the old default of 210/255 keeps it. There is
  no padding: the plate is the mapped source box, a decision recorded at the fill in
  `src/Main_Form.OcrOverlay.vb` until a before/after run of the scene harness measures one.
- **rules 12 and 17 on the x86 fallback only.** The discard record is collected by both builds but written
  only by the mainline's `OcrDiagnostics`; the decision and what would change it are at the top of that
  file. Under 1.3 the written record is the mainline's rule 10 evidence route, which makes writing it on
  the x86 fallback a matter of that build's exception, not of rule 10.

**rule 10's missing language pack** is closed, not excepted: the status names the missing language and the
download (Settings -> OCR & translation -> Recognition), in both builds.
