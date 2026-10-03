# Pointer - `OCR-PIPELINE`

| | |
| --- | --- |
| **Id** | `OCR-PIPELINE` |
| **Version** | 1.8, active (re-read 2026-10-02: 1.3-1.8 are additive and no constant this product inherits moved - every `src/Ocr/` value matches the section 5 table). Owner: doc-html-translate (the reference implementation) |
| **Home** | `ocr-overlay/ocr-pipeline.md` in the shared contracts catalog |
| **Role here** | none as an implementer - the **source of inherited constants** |

## Why this repository points at it

`OCR-PIPELINE` binds nobody as rules; `OCR-OVERLAY` does that. But every constant in `src/Ocr/` marked
*inherited* names a source, and for the pitch, leading, type-size, coverage, line-fill, fit-ladder and
colour-sampler numbers that source is this contract - by section, and by the status column its amendment
1.2 item M added to section 5. Citing it by id is what rule 13 of `OCR-OVERLAY` requires of those rows.

- A change of one of those numbers in the catalog is a question for the owning ticket named on the row
  (ticket 033), never a silent local edit.
- No registry row is kept for this product: pointing at a source of constants is not an implementation
  (ticket 033, owner decision O-8).
- The ring-band sentence this repository found contradicting the owner's code (ticket 033 finding F9) was
  corrected by the owner in 1.2 item E; nothing is open against it.
