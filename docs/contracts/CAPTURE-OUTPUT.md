# Pointer - `CAPTURE-OUTPUT`

| | |
| --- | --- |
| **Id** | `CAPTURE-OUTPUT` - where a produced file lands, what it is called, what it is |
| **Version** | 0.8, draft (read 2026-11-15, L0323 catch-up; 0.6 was read 2026-10-20). 0.7 added the section 5 line for a file received from another device, 0.8 the line for a file whose place and name a calling program chose. Registry row `partial 0.8`, with one exception dated 2026-11-15 (the folder of `--out` was created): ticket L0324 closed it in code on 2026-11-15, compiled but not run, so the row moves after the release run of `AppCommandsTests` and `AppCommandsConformance`. Owner: FastMediaSorter Android |
| **Home** | `capture-output/README.md` in the shared contracts catalog |
| **Role here** | bound for the `screenshot` kind through the viewer's `screenshot window` verb (ticket L0318; the caller-named-path line of section 5); not bound for any other rule 1 kind - none is written. Section 5 excludes edited copies, conversions and exports whose format another contract owns |

## What this repository does to stay conformant

- **The screenshot (rules 3, 5, 12, 13, 16 and the 0.8 line of section 5).** `screenshot window --out <png> [--overwrite]`
  (`src/AppCommands/ViewerNavigation.vb`: target, name and the `tIME` chunk; `src/Main_Form.AppCommandsDrive.vb`;
  `CommandFiles.WriteWhole` in `src/AppCommands/AppCommandsProfile.vb`). The path is used exactly as given, an existing
  file is refused without `--overwrite`, a folder argument gets `screenshot_<yyMMdd>_<HHmmss>.png` with the rule 5
  ordinal, the PNG carries the `tIME` chunk of the capture's start in local time, and the file is complete before it is
  visible. **The folder of `--out` is never created** (the 0.8 line; ticket L0324): `ExecuteScreenshot` refuses a
  missing folder before it captures the window, with the reason `target-missing` (exit 11), and
  `CommandFiles.WriteWhole(.., createFolder:=False)` - the only writer of `--out` - throws `DirectoryNotFoundException`
  instead of creating it, which the same method answers the same way; `AppCommandsTests.An_out_file_never_creates_a_missing_folder`
  holds both. The folders the product owns (the reply file, `--result`) are still created, because the caller named
  no place for them. A full disk is answered with the closed reason `no-space` (APP-COMMANDS 0.2 item M, exit 13) and
  any other failed write with `error` (exit 1); neither is built-and-compiled yet (ticket L0324, release item).
- **No other capture is written.** No camera, screen or voice recording, video frame, new text file, OCR text or
  translation reaches the disk: recognized text and its translation are drawn on the picture and cached under
  `%LOCALAPPDATA%`, never saved as a file. Searched again 2026-11-15: `screenshot window` is the only screen-grab path
  in `src/`.
- **Rule 16 (the PNG `tIME` chunk) binds the screenshot and nothing else this product writes.** The other PNGs written
  for the user are (1) the Share Manager's QR image (`Qr_Zoom_Form.SavePng`), an export whose format `FMSCFG` owns
  (0.4, section 5); (2) the image editor's derived picture (`ImageFileWriter`), a file derived from one the user has
  (0.3, section 5); the thumbnail cache's PNGs are private. None is a kind of rule 1, so none is given a `tIME` chunk.
- **Reads no `documents` kind yet.** Rule 14's reader half (accept CRLF and a byte-order mark, write
  neither) arrives with ticket 008's text preview, and that ticket must cite it.
- **Never parses a kind out of a name** (section 3): `MediaKindClassifier` classifies by extension only.
- **Derived files are outside section 5 (0.3).** The image editor's "Save as.." writes a derived picture under a name
  the user types (offered as `<stem> (edited).<ext>`) and leaves a collision to the dialog's overwrite prompt;
  the text reader's "Save selection as a new file" (specification 277, Ctrl+Shift+S) writes an excerpt of a text file the
  user has in the same way - offered as `<stem> (excerpt).<ext>` beside the source, in the source's encoding, byte-order
  mark and dominant line ending, so rule 14 does not bind it (`TextExcerpt`, unit-tested);
  "Replace with video" writes `<stem>.mp4` beside the source with the rule 5 ordinal already
  (`VideoConvertPlan.TargetPathFor`, unit-tested), then deletes the source.
- **A file received from another device (0.7, section 5; `DEVICE-EXCHANGE` items CB and EL).** The Share Manager's
  clipboard receive path keeps a named item's base name, names a nameless one `clipboard-<yyyyMMdd-HHmmss>.<ext>` and
  never overwrites (`src/Exchange/ExchangeClipboard.vb`); rules 2 and 16 do not apply to such a file. Closed by reading,
  2026-11-15.
- **The QR image's place is `FMSCFG`'s question (0.4), and `FMSCFG` is silent on it.** This contract decides nothing
  about it by silence; a proposal to `FMSCFG` is drafted (2026-10-07). Meanwhile `Qr_Zoom_Form` keeps
  `fms-qr-<share>-<yyyyMMdd-HHmm>.png` in `Pictures\Fast Media Sorter\`, with a told `%TEMP%` fallback and a name cached
  per window; no runtime change precedes the owner's answer.
- **What must not change silently:** a capture kind added here - a frame taken from a video, a
  recording - is first a MINOR amendment of rule 1 in the catalog, then code (rule 2).
