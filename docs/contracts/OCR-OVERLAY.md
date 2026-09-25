# Pointer - `OCR-OVERLAY`

| | |
| --- | --- |
| **Id** | `OCR-OVERLAY` |
| **Version** | 1.0, active |
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
  this product is currently the only one that writes it for the blank case.
- Never write a recognized string into user data, a filename or a catalog row.
- Keep OCR failure best-effort, and keep "read fine, found no text" a different outcome from "recognition
  failed".
- Bump `OcrPipeline.OcrPipelineVersion` in any change that alters the **content** of an
  `OcrOverlayDocument`, never for one that only paints it differently - the disk cache keys on it.

Five deviations were recorded as dated exceptions in the catalog's registry. Ticket 025 closed three of
them outright - rule 5's type size is now the median of word heights, rule 6 no longer falls back to the
ink gap, and rule 9's last rung releases the plate instead of trimming - and answered rule 10 with a
refusal read off the discard record rather than a script pass. Two remain, both by decision rather than by
omission, and both re-argued in their registry rows:

- **rule 8 on the x86 fallback only.** The sampler is modern-only and stays that way: sampled colours are
  an appearance change and that build takes bug fixes only. The decision is written at the top of
  `src/Ocr/OcrPlateColors.vb`, including what would change it.
- **rule 13's third status.** Every module in `src/Ocr/` now carries a constant status table, and several
  rows honestly say *chosen here, NOT derived* - a status the rule does not admit, and the one the reference
  implementation proposed on 2026-09-25 as `policy`. Ticket 033 Ф-3 corrected the tables the same day: the
  owning ticket is 033 (025 is closed), `OCR-ACCURACY` is no longer cited for the reference's numbers, and
  the four constants that were marked *derived here* on the reference's measurements are *inherited* from
  `OCR-PIPELINE`.

Ticket 033 (2026-09-25) recorded two more exceptions, each a written decision, and closed one gap:

- **rule 7, the plate's opacity and padding.** The plate is opaque by default; a lower opacity is the
  user's own setting (30-100 %), and an install that saved the old default of 210/255 keeps it. There is
  no padding: the plate is the mapped source box, a decision recorded at the fill in
  `src/Main_Form.OcrOverlay.vb` until a before/after run of the scene harness measures one.
- **rules 12 and 17 on the x86 fallback only.** The discard record is collected by both builds but written
  only by the mainline's `OcrDiagnostics`; the decision and what would change it are at the top of that
  file.
- **rule 10's missing language pack** is closed, not excepted: the status names the missing language and
  the download (Settings -> OCR & translation -> Recognition), in both builds.

The rule 10 refusal itself holds by an interpretation - an assumed language resolved by the discard record -
that the rule's text does not state; the clarifying amendment is a proposal in the catalog, not a local
reading.
