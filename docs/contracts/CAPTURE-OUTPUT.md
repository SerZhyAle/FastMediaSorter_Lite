# Pointer - `CAPTURE-OUTPUT`

| | |
| --- | --- |
| **Id** | `CAPTURE-OUTPUT` - where a produced file lands, what it is called, what it is |
| **Version** | 0.1, draft (read 2026-09-26). Owner: FastMediaSorter Android |
| **Home** | `capture-output/README.md` in the shared contracts catalog |
| **Role here** | none today, declared 2026-09-26 - the viewer and the Share Manager write none of rule 1's nine kinds; three files sit on the boundary and are a proposal beside the contract, held by ticket 096 |

## What this repository does to stay conformant

- **Writes no capture.** No camera, screenshot, screen or voice recording, video frame, new text file,
  OCR text or translation reaches the disk: recognized text and its translation are drawn on the picture
  and cached under `%LOCALAPPDATA%`, never saved as a file.
- **Reads no `documents` kind yet.** Rule 14's reader half (accept CRLF and a byte-order mark, write
  neither) arrives with ticket 008's text preview, and that ticket must cite it.
- **Never parses a kind out of a name** (section 3): `MediaKindClassifier` classifies by extension only.
- **On the boundary, unchanged until the owner answers** (`PROPOSAL-2026-09-26-derived-files-desktop-viewer.md`
  beside the contract):
  - the image editor's "Save as.." writes a derived picture under a name the user types (offered as
    `<stem> (edited).<ext>`) and leaves a collision to the dialog's overwrite prompt - the opposite of
    rules 5-7 if the file turns out to be inside the contract;
  - "Replace with video" writes `<stem>.mp4` beside the source with the rule 5 ordinal already
    (`VideoConvertPlan.TargetPathFor`, unit-tested), then deletes the source;
  - the Share Manager's QR image is `FMSCFG`'s export (out of scope by section 5), yet its place -
    `Pictures\Fast Media Sorter\`, a product subfolder section 5 refuses for captures, with a `%TEMP%`
    fallback that is told - is named by no contract.
- **What must not change silently:** a capture kind added here - a frame taken from a video, a
  screenshot - is first a MINOR amendment of rule 1 in the catalog, then code (rule 2).
