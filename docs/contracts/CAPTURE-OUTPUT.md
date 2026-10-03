# Pointer - `CAPTURE-OUTPUT`

| | |
| --- | --- |
| **Id** | `CAPTURE-OUTPUT` - where a produced file lands, what it is called, what it is |
| **Version** | 0.3, draft (read 2026-10-02). Owner: FastMediaSorter Android |
| **Home** | `capture-output/README.md` in the shared contracts catalog |
| **Role here** | not bound, re-read 2026-10-02 - no rule 1 capture kind is written; section 5 excludes edited copies and conversions; only the QR image's name and folder await the owner (ticket 096) |

## What this repository does to stay conformant

- **Writes no capture.** No camera, screenshot, screen or voice recording, video frame, new text file,
  OCR text or translation reaches the disk: recognized text and its translation are drawn on the picture
  and cached under `%LOCALAPPDATA%`, never saved as a file.
- **Reads no `documents` kind yet.** Rule 14's reader half (accept CRLF and a byte-order mark, write
  neither) arrives with ticket 008's text preview, and that ticket must cite it.
- **Never parses a kind out of a name** (section 3): `MediaKindClassifier` classifies by extension only.
- **Derived files are outside section 5 (0.3).** Question 1 of
  `PROPOSAL-2026-09-26-derived-files-desktop-viewer.md` was accepted 2026-10-02:
  - the image editor's "Save as.." writes a derived picture under a name the user types (offered as
    `<stem> (edited).<ext>`) and leaves a collision to the dialog's overwrite prompt;
  - "Replace with video" writes `<stem>.mp4` beside the source with the rule 5 ordinal already
    (`VideoConvertPlan.TargetPathFor`, unit-tested), then deletes the source.
  Their names, folders and formats keep the existing editor/converter behavior; no capture naming test
  or exception is required for these excluded outputs.
- **QR place remains open.** Question 2 of the same proposal is still awaiting the owner, explicitly
  recorded in section 5 of 0.3. `FMSCFG` owns the Share Manager's QR payload; its name and folder remain
  `fms-qr-<share>-<yyyyMMdd-HHmm>.png` in `Pictures\Fast Media Sorter\`, with a told `%TEMP%` fallback
  and a name cached per window. No runtime change or `FMSCFG` proposal precedes that answer.
- **What must not change silently:** a capture kind added here - a frame taken from a video, a
  screenshot - is first a MINOR amendment of rule 1 in the catalog, then code (rule 2).
