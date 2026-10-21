# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | 0.11, draft (since 2026-09-24; 0.10 of 2026-09-26, 0.11 of 2026-10-14 with two corrections of 2026-10-16; read on 2026-10-20: the header, section 8 and item I). Owner: CyrFlip |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | **writer** (the Share Manager copies the SFTP password, login, the `.fmscfg` QR and the broadcast link; the viewer copies names, paths and one resource document with a password) and **participant and reader of the exchange clipboard** (item L): the Share Manager reads the clipboard on an explicit press, sends it to the user's exchange server and places a received value (`ClipboardWin32`, `ClipboardExchange`, `ClipboardCoordinator` in `src/FastMediaSorterCompanion/Core/`); the viewer reads the clipboard once, on an explicit paste, to take in a phone code (`QrCodeDecoder.DecodeClipboard`). It keeps no history and transforms nothing in place, so items D to K and rules 4 and 5 bind nothing here |

## What this repository must do to stay conformant

- **A secret written to the clipboard carries the privacy markers (rule 6, item I).** `CanIncludeInClipboardHistory`
  and `CanUploadToCloudClipboard` as DWORD 0, `ExcludeClipboardContentFromMonitorProcessing` by presence. The door
  is `SecureClipboard.ApplyPrivacyFlags` in the Share Manager and `SecretClipboard` in `src/Resources/` for the
  viewer. `ShareManager_NeverWritesTheClipboardOutsideTheHelper` keeps other writers out of the Share Manager; it
  cannot see `ClipboardWin32.PlaceNow`, the one deliberate marker-free writer (item Q), which is reviewed by hand.
- **Raw Win32, off a thread without a pump (rules 1, 2, items A, B).** The open retries 12 times 15 ms and
  `CloseClipboard` runs unconditionally; a high-level wrapper is allowed only on a UI thread that pumps messages.
- **Only an explicit act sends (items L, M).** A button, a tray item or the desktop shortcut: no timer, no change
  notification, no start-up send. Nothing filters by a privacy marker, previews or asks: what is sent is exactly what
  is on the clipboard, a password included, and the guide and the privacy page say so in the product's own words.
- **No hangs (item N).** The read runs off the UI thread in one open, closes the clipboard before any file system,
  conversion or network work, and abandons a delay-rendered format whose owner does not answer within 5 seconds.
  One operation at a time.
- **What is read, and how much (items O, P).** One regular file, streamed from disk after the close; a folder or
  several files are refused with one message; then text, then an image. The cap is the server's limit for the
  account, compared before anything is marshalled or uploaded.
- **Writing a received value (item Q).** One open, one `EmptyClipboard`, every format set, one close; never backed
  up or restored; no privacy markers on it.
- **No value in a log (item R).** A sent or received value, and a file name, never enter a log, a support bundle,
  a diagnostic dump or a temporary file outside the received-files folder; logs carry kind, size and a reason code.
- **Order of kinds and line ends.** Item O here (file, text, image; line ends as they are) and `DEVICE-EXCHANGE`
  item CB (file, image, text; CRLF to LF on the wire) differ; the Share Manager follows `DEVICE-EXCHANGE`.

## Where it stands

The registry rows of this product and its dated exceptions say what is proven and what is open; this pointer never
repeats them. Specification 244 holds the clause-by-clause audit.
