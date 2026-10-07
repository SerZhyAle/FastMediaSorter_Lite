# Pointer - `OCR-PIPELINE`

| | |
| --- | --- |
| **Id** | `OCR-PIPELINE` |
| **Version** | 1.9, active (re-read 2026-10-07: 1.5-1.9 are additive and no constant this product inherits moved - every inherited `src/Ocr/` value still matches the section 5 table and the cited amendment rows). Owner: doc-html-translate (the reference implementation) |
| **Home** | `ocr-overlay/ocr-pipeline.md` in the shared contracts catalog |
| **Role here** | consumer of part of the mechanism and the **source of inherited constants**; the rules that bind are `OCR-OVERLAY`'s |

## Why this repository points at it

`OCR-PIPELINE` binds nobody as rules; `OCR-OVERLAY` does that. But every constant in `src/Ocr/` marked
*inherited* names a source, and for the pitch, leading, type-size, coverage, line-fill, fit-ladder and
colour-sampler numbers that source is this contract - by section, and by the status column its amendment
1.2 item M added to section 5. Citing it by id is what rule 13 of `OCR-OVERLAY` requires of those rows.

- A change of one of those numbers in the catalog is a question for the owning ticket named on the row
  (ticket 033), never a silent local edit.
- Since the contract names this product among its consumers, the registry carries a row (added 2026-10-07;
  until then none was kept, on the reading that pointing at a source of constants is not an implementation).

## What of it is ported, and what is not

Ported: the line split by word gap (1.1 item A, ratio 3.5, `OcrLineSplitter`), clustering by line pitch (2.5),
the translatability filter (2.6), the plate colour and ring sampler on the mainline (3.2, 3.3) and the fit
ladder (3.4).

Not ported, each stating that an older port keeps working: the concealment modes (1.3), the pipe repair (1.5),
the anchored rescue admission (1.6), the tall-word cut of the line split and the grey sweep (1.7 A and B), the
CJK join without a space (1.8), the line-associated rescue merge, word-union drawn geometry, drawable mask
padding and ragged-right alignment (1.9), and the whole rescue ladder and halftone paths of 2.7-2.10 that
they belong to. This product recognizes with a scored multi-attempt Tesseract pass instead of the reference's
rescue ladder, so 1.9's merge has nothing to attach to here. One dated registry exception names the two
that touch what this product does implement (1.7 A and 1.8).

- The ring-band sentence this repository found contradicting the owner's code (ticket 033 finding F9) was
  corrected by the owner in 1.2 item E; nothing is open against it.
