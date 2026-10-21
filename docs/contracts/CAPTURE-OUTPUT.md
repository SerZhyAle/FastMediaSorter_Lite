# Pointer - `CAPTURE-OUTPUT`

| | |
| --- | --- |
| **Id** | `CAPTURE-OUTPUT` - where a produced file lands, what it is called, what it is |
| **Version** | 0.6, draft (read 2026-10-20, specification 244; 0.5 was read 2026-10-07). 0.6 adds only that a plain-text capture carries no time carrier - the file name's local time is the authority - and asks nothing of a product that writes none of those kinds. Owner: FastMediaSorter Android |
| **Home** | `capture-output/README.md` in the shared contracts catalog |
| **Role here** | not bound for a rule 1 kind - none is written; section 5 excludes edited copies, conversions and exports whose format another contract owns; one path is unsettled (the last item below) |

## What this repository does to stay conformant

- **Writes no capture.** No camera, screenshot, screen or voice recording, video frame, new text file,
  OCR text or translation reaches the disk: recognized text and its translation are drawn on the picture
  and cached under `%LOCALAPPDATA%`, never saved as a file. Searched again 2026-10-20: no screen-grab or
  frame-save call exists in `src/`.
- **Rule 16 (0.5, the PNG `tIME` chunk) does not bind this product.** It binds a PNG *capture*. The only PNGs
  written for the user are (1) the Share Manager's QR image (`Qr_Zoom_Form.SavePng`), an export whose format
  `FMSCFG` owns (0.4, section 5); (2) the image editor's derived picture (`ImageFileWriter`), a file derived from one
  the user has (0.3, section 5); the thumbnail cache's PNGs are private. None is a kind of rule 1, so none is given a
  `tIME` chunk and no exception is recorded for it.
- **Reads no `documents` kind yet.** Rule 14's reader half (accept CRLF and a byte-order mark, write
  neither) arrives with ticket 008's text preview, and that ticket must cite it.
- **Never parses a kind out of a name** (section 3): `MediaKindClassifier` classifies by extension only.
- **Derived files are outside section 5 (0.3).** The image editor's "Save as.." writes a derived picture under a name
  the user types (offered as `<stem> (edited).<ext>`) and leaves a collision to the dialog's overwrite prompt;
  "Replace with video" writes `<stem>.mp4` beside the source with the rule 5 ordinal already
  (`VideoConvertPlan.TargetPathFor`, unit-tested), then deletes the source.
- **The QR image's place is `FMSCFG`'s question (0.4), and `FMSCFG` is silent on it.** This contract decides nothing
  about it by silence; a proposal to `FMSCFG` is drafted (2026-10-07). Meanwhile `Qr_Zoom_Form` keeps
  `fms-qr-<share>-<yyyyMMdd-HHmm>.png` in `Pictures\Fast Media Sorter\`, with a told `%TEMP%` fallback and a name cached
  per window; no runtime change precedes the owner's answer.
- **What must not change silently:** a capture kind added here - a frame taken from a video, a
  screenshot - is first a MINOR amendment of rule 1 in the catalog, then code (rule 2).
- **A file received from another device is not named by this contract.** The Share Manager's clipboard receive
  path (specification 230: `ClipboardExchange`, `ExchangeClipboard`) writes a received item into a folder, and
  section 5 has no line for it; rule 2 says a prefix is never invented locally and rule 3 fixes the local
  `yyMMdd_HHmmss` grammar. How such a file is named is an open decision, not a claim of conformance - and not a
  new capture kind without the amendment above.
